"""LAVAD Smart CPU Scheduler adapter (Task 3 / spec R5).

Implements ``hook.hive.scheduler.dispatch`` and ``hook.hive.scheduler.bandit``.
Operates as **parent** sensor + allocator above the OS worker pool — emits
``encoded_scheduler_state`` (learned latent), not raw CPU stats on KV ingress.
"""

from __future__ import annotations

import random
from dataclasses import dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING

from hive_code_dev_1.contracts import (
    AdapterId,
    BanditArm,
    BanditState,
    CpdomContext,
    CpuContext,
    DeploymentPrior,
    HardwareProfile,
    InputLatentBundle,
    LavadSmartSchedulerState,
    PerformanceFeedback,
    PowerMode,
    PriorSource,
    ProbeResult,
    ProbeTier,
    SchedulerIntegrationLevel,
    SchedulerSignalPacket,
    SysStat,
    TaskContext,
)
from hive_code_dev_1.hooks import HOOK_SCHEDULER_BANDIT, HOOK_SCHEDULER_DISPATCH
from hive_code_dev_1.lavd.bandit import (
    BanditUpdateResult,
    bandit_success,
    compute_evidence_quality,
    default_bandit_arms,
    thompson_select,
    update_posterior,
)
from hive_code_dev_1.lavd.catalyst import catalyst_routing_hints, preferred_arm_from_hints
from hive_code_dev_1.lavd.scheduler_intent import routing_hints_for_arm

if TYPE_CHECKING:
    from hive_code_dev_1.adapters.pool import AdapterPool


@dataclass
class TelemetrySnapshot:
    """CPU/core telemetry — matrix/mock or physical probe filled."""

    core_count: int = 4
    p_core_count: int = 2
    e_core_count: int = 2
    cpu_utilizations: list[float] = field(default_factory=lambda: [0.3, 0.5, 0.2, 0.4])
    queued_tasks: int = 4
    latency_budget_ms: float = 50.0
    power_constraint: float = 0.5
    power_mode: PowerMode = PowerMode.BALANCED
    task_lat_cri: list[float] = field(default_factory=lambda: [0.8, 0.15, 0.6])
    integration_level: SchedulerIntegrationLevel = SchedulerIntegrationLevel.APPLICATION
    e_core_active: bool = False  # set True only under e-core pressure constraints


class LavadSmartScheduler:
    """Smart LAVAD CPU Scheduler — Thompson bandit + telemetry encoder."""

    def __init__(
        self,
        *,
        sigma_dim: int = 16,
        deployment_prior: DeploymentPrior | None = None,
        rng: random.Random | None = None,
        adapter_pool: AdapterPool | None = None,
        adapter_pool_path: Path | str | None = None,
    ) -> None:
        from hive_code_dev_1.adapters.sigma import pooled_adapter_chain

        self._rng = rng or random.Random()
        self._pool = self._resolve_adapter_pool(adapter_pool, adapter_pool_path, sigma_dim)
        self._adapters = pooled_adapter_chain(self._pool, sigma_dim)
        self._scheduler_adapter = self._adapters[AdapterId.SCHEDULER_TO_SIGMA]
        self._feedback_adapter = self._adapters[AdapterId.SIGMA_TO_SCHEDULER]
        self._deployment_prior = deployment_prior or DeploymentPrior()
        self._hardware_profile: HardwareProfile | None = None
        self._state = LavadSmartSchedulerState(
            bandit=BanditState(
                arms=default_bandit_arms(self._deployment_prior),
                rareness_correction=0.15,
            ),
            profiling_complete=False,
        )

    @staticmethod
    def _resolve_adapter_pool(
        pool: AdapterPool | None,
        path: Path | str | None,
        sigma_dim: int,
    ) -> AdapterPool:
        from hive_code_dev_1.adapters.pool import AdapterPool as PoolCls

        if pool is not None:
            return pool
        candidates: list[Path] = []
        if path is not None:
            candidates.append(Path(path))
        candidates.append(Path("artifacts/mlp-decimacon/adapter-pool.json"))
        candidates.append(Path("releases/mlp-decimacon/adapter-pool.json"))
        for candidate in candidates:
            if candidate.is_file():
                return PoolCls.load(candidate)
        return PoolCls(sigma_dim=sigma_dim)

    @property
    def hook_dispatch(self) -> str:
        return HOOK_SCHEDULER_DISPATCH

    @property
    def hook_bandit(self) -> str:
        return HOOK_SCHEDULER_BANDIT

    def profile_hardware(
        self,
        profile_id: str,
        core_count: int,
        *,
        p_core_count: int | None = None,
        e_core_count: int | None = None,
        probe_results: list[ProbeResult] | None = None,
    ) -> HardwareProfile:
        """Startup benchmark — profile once, then adapt (``LAVD_BASETEXT``)."""
        p = p_core_count if p_core_count is not None else max(1, core_count // 2)
        e = e_core_count if e_core_count is not None else core_count - p
        # Scale probe tiers with available cores — not fixed 5/8/12 ms stubs.
        base_lat = max(1.0, 24.0 / max(1, core_count))
        base_tps = float(20 * max(1, core_count))
        probes = probe_results or [
            ProbeResult(
                tier=ProbeTier.SOFT,
                latency_ms=base_lat,
                throughput=base_tps,
                cores_available=core_count,
            ),
            ProbeResult(
                tier=ProbeTier.MEDIUM,
                latency_ms=base_lat * 1.6,
                throughput=base_tps * 0.8,
                cores_available=core_count,
            ),
            ProbeResult(
                tier=ProbeTier.HARD,
                latency_ms=base_lat * 2.4,
                throughput=base_tps * 0.5,
                cores_available=max(1, core_count // 2),
            ),
        ]
        profile = HardwareProfile(
            profile_id=profile_id,
            core_count=core_count,
            p_core_count=p,
            e_core_count=e,
            baseline_latency_ms=probes[1].latency_ms,
            baseline_throughput=probes[1].throughput,
            probe_results=probes,
        )
        self._hardware_profile = profile
        self._deployment_prior = DeploymentPrior(
            prior_source=PriorSource.HARDWARE_PROFILE,
            hardware_profile=profile,
            arm_alpha_overrides={int(BanditArm.LATENCY_CRITICAL): 2.0},
        )
        self._state.bandit.arms = default_bandit_arms(self._deployment_prior)
        self._state.deployment_profile_id = profile_id
        self._state.profiling_complete = True
        return profile

    def dispatch(self, telemetry: TelemetrySnapshot) -> SchedulerSignalPacket:
        """``hook.hive.scheduler.dispatch`` — encode telemetry + Thompson arm selection."""
        telemetry = self._normalize_telemetry_topology(telemetry)
        self._detect_regime_shift(telemetry)
        cpu_domains = self._build_cpdom_contexts(telemetry)
        task_contexts = self._build_task_contexts(telemetry)
        cpu_contexts = self._build_cpu_contexts(telemetry)
        sys_stat = self._build_sys_stat(telemetry, task_contexts)
        catalyst_hints = catalyst_routing_hints(task_contexts, cpu_domains)
        arm_bias = preferred_arm_from_hints(catalyst_hints)
        selected_arm, policy_sample = thompson_select(
            self._state.bandit,
            rng=self._rng,
            arm_bias=arm_bias,
        )

        latency_budget = telemetry.latency_budget_ms
        if any(d.is_big is False and d.is_active for d in cpu_domains):
            latency_budget = min(latency_budget, 10.0)

        compute_budget = self._compute_budget_units(telemetry, task_contexts)
        worker_pool = telemetry.core_count
        if telemetry.integration_level == SchedulerIntegrationLevel.CUSTOM_RUNTIME:
            # Bind worker pool / budget across submodels — tighter than APPLICATION.
            if telemetry.queued_tasks >= 8:
                worker_pool = max(1, telemetry.core_count // 2)
            compute_budget = max(1.0, compute_budget * 0.75)

        packet = SchedulerSignalPacket(
            cpu_domains=cpu_domains,
            task_contexts=task_contexts,
            cpu_contexts=cpu_contexts,
            sys_stat=sys_stat,
            integration_level=telemetry.integration_level,
            worker_pool_size=worker_pool,
            compute_budget_units=compute_budget,
            hardware_profile_id=self._state.deployment_profile_id,
            latency_budget=latency_budget,
            power_constraint=telemetry.power_constraint,
            power_mode=telemetry.power_mode,
            selected_arm=selected_arm,
            policy_sample=policy_sample,
            signals_stable=self._signals_stable(telemetry),
            resource_signals={
                "free_cores": float(
                    max(
                        0.0,
                        float(telemetry.core_count)
                        - sum(telemetry.cpu_utilizations[: telemetry.core_count]),
                    )
                ),
                "queued_tasks": float(telemetry.queued_tasks),
                "mean_slice_ns": float(sys_stat.slice),
            },
            priority_signals=self._priority_signals(task_contexts),
            routing_hints={
                **catalyst_hints,
                **routing_hints_for_arm(selected_arm),
                "e_core_active": float(any(not d.is_big and d.is_active for d in cpu_domains)),
                "sparsity_hint": catalyst_hints.get("sparsity_floor", 0.0),
                "integration_level": float(
                    1.0
                    if telemetry.integration_level == SchedulerIntegrationLevel.CUSTOM_RUNTIME
                    else 0.0
                ),
            },
        )
        packet.encoded_scheduler_state = self._scheduler_adapter.translate_to_sigma(packet)
        self._state.latest_signal = packet
        return packet

    def update_bandit(self, feedback: PerformanceFeedback) -> BanditUpdateResult:
        """``hook.hive.scheduler.bandit`` — posterior update gated on ``bandit_success``."""
        latest = self._state.latest_signal
        # Default-constructed packet has latency_budget=0 — updating then marks every
        # measured outcome a failure (actual_latency < 0 is impossible) and poisons Beta.
        if latest is None or latest.latency_budget <= 0:
            return BanditUpdateResult(updated=False, skipped_reason="no_dispatch_budget")
        latency_budget = latest.latency_budget
        if feedback.evidence_quality <= 0.0:
            feedback.evidence_quality = compute_evidence_quality(feedback, latency_budget)
        result = update_posterior(self._state.bandit, feedback, latency_budget)
        if self._state.bandit.regime_shift_detected and self._state.bandit.exploration_boost > 0:
            self._state.bandit.exploration_boost *= 0.9
            if self._state.bandit.exploration_boost < 0.05:
                self._state.bandit.regime_shift_detected = False
                self._state.bandit.exploration_boost = 0.0
        return result

    def ingest_feedback_via_sigma(
        self,
        sigma_vector: list[float],
        *,
        measured: PerformanceFeedback | None = None,
    ) -> PerformanceFeedback:
        """Simulate ``T[NN→Σ] → T[Σ→Scheduler]`` feedback path.

        Measured latency/accuracy must be supplied via ``measured`` — they are
        never fabricated from ``latency_budget``.
        """
        decoded = self._feedback_adapter.translate_from_sigma("scheduler", sigma_vector)
        base = measured or PerformanceFeedback()
        if isinstance(decoded, SchedulerSignalPacket):
            # Preserve measured arm when supplied — Σ decode is non-invertible stub.
            arm = int(base.selected_arm) if measured is not None else int(decoded.selected_arm)
            return PerformanceFeedback(
                selected_arm=arm,
                actual_latency=base.actual_latency,
                actual_accuracy=base.actual_accuracy,
                throughput=base.throughput,
                bandit_success=base.bandit_success,
                evidence_quality=base.evidence_quality,
            )
        return base

    def get_state(self) -> LavadSmartSchedulerState:
        return self._state

    def _detect_regime_shift(self, telemetry: TelemetrySnapshot) -> None:
        if not self._hardware_profile:
            return
        if abs(telemetry.core_count - self._hardware_profile.core_count) >= 2:
            self._state.bandit.regime_shift_detected = True
            self._state.bandit.exploration_boost = max(self._state.bandit.exploration_boost, 0.25)
            # Discount stale posteriors on environment change (compound, floored).
            self._state.bandit.posterior_discount = max(
                0.25, self._state.bandit.posterior_discount * 0.85
            )

    def _signals_stable(self, t: TelemetrySnapshot) -> bool:
        if t.core_count <= 0:
            return False
        utils = [u for u in t.cpu_utilizations[: t.core_count] if u is not None]
        if not utils:
            return False
        if any(u < -0.05 or u > 1.05 for u in utils):
            return False
        spread = max(utils) - min(utils)
        # Extreme spread with empty queue is untrustworthy.
        if spread > 0.9 and t.queued_tasks == 0:
            return False
        return True

    def _compute_budget_units(self, t: TelemetrySnapshot, tasks: list[TaskContext]) -> float:
        """Scale Decimacon budget from cores, queue depth, and mean slice_ns (spec R5)."""
        core_term = float(max(1, t.core_count - t.queued_tasks // 2))
        if not tasks:
            return core_term
        mean_slice = sum(tc.slice_ns for tc in tasks) / len(tasks)
        # 1e6 ns ≈ nominal unit; shorter slices → frugal budget.
        slice_scale = max(0.25, min(2.0, float(mean_slice) / 1_000_000.0))
        return max(1.0, core_term * slice_scale)

    @staticmethod
    def _normalize_telemetry_topology(t: TelemetrySnapshot) -> TelemetrySnapshot:
        """Ensure ``p_core_count + e_core_count == core_count`` (HT-safe)."""
        if t.p_core_count >= 0 and t.e_core_count >= 0 and t.p_core_count + t.e_core_count == t.core_count:
            return t
        from hive_code_dev_1.lavd.telemetry_probe import estimate_p_e_cores

        p, e = estimate_p_e_cores(t.core_count, t.core_count)
        return TelemetrySnapshot(
            core_count=t.core_count,
            p_core_count=p,
            e_core_count=e,
            cpu_utilizations=list(t.cpu_utilizations),
            queued_tasks=t.queued_tasks,
            latency_budget_ms=t.latency_budget_ms,
            power_constraint=t.power_constraint,
            power_mode=t.power_mode,
            task_lat_cri=list(t.task_lat_cri),
            integration_level=t.integration_level,
            e_core_active=t.e_core_active,
        )

    def _build_cpdom_contexts(self, t: TelemetrySnapshot) -> list[CpdomContext]:
        p, e = t.p_core_count, t.e_core_count
        domains: list[CpdomContext] = []
        for i in range(p):
            domains.append(CpdomContext(id=i, alt_id=i + 100, is_big=True, is_active=True, is_stealer=False))
        for i in range(e):
            idx = p + i
            # E-cores only under pressure. Secondary E-cores are stealers when active.
            e_pressure = bool(t.e_core_active)
            is_stealer = e_pressure and i > 0
            e_on = e_pressure and (i == 0 or is_stealer)
            domains.append(
                CpdomContext(
                    id=idx,
                    alt_id=idx + 100,
                    is_big=False,
                    is_active=e_on,
                    is_stealer=is_stealer,
                )
            )
        return domains

    def _build_task_contexts(self, t: TelemetrySnapshot) -> list[TaskContext]:
        contexts: list[TaskContext] = []
        mean_util = (
            sum(t.cpu_utilizations[: t.core_count]) / max(1, min(len(t.cpu_utilizations), t.core_count))
            if t.cpu_utilizations
            else 0.5
        )
        queue_pressure = min(1.0, t.queued_tasks / max(1.0, float(t.core_count) * 4.0))
        for i, lat_cri in enumerate(t.task_lat_cri):
            # Keep declared lat_cri dominant so catalyst bands stay reachable;
            # soft nudge from util / queue as waker/wakee stand-in.
            derived_lat = max(
                0.0,
                min(1.0, 0.9 * lat_cri + 0.07 * mean_util + 0.03 * queue_pressure),
            )
            slice_ns = int(1_000_000 * (0.5 + 0.5 * (1.0 - derived_lat)))
            contexts.append(
                TaskContext(
                    lat_cri=derived_lat,
                    perf_cri=max(0.0, min(1.0, 1.0 - derived_lat)),
                    acc_runtime=float(i * 10) + mean_util * 5.0,
                    avg_runtime=2.0 + derived_lat,
                    slice_ns=slice_ns,
                )
            )
        return contexts

    def _build_cpu_contexts(self, t: TelemetrySnapshot) -> list[CpuContext]:
        contexts: list[CpuContext] = []
        for i, util in enumerate(t.cpu_utilizations[: t.core_count]):
            # Relatively lower cpuperf on E-cores; scale with live util.
            base_perf = 1.0 if i < t.p_core_count else 0.65
            contexts.append(
                CpuContext(
                    avg_util=util * 0.9,
                    cur_util=util,
                    cpuperf_cur=max(0.2, base_perf * (0.7 + 0.3 * (1.0 - util))),
                    is_online=True,
                )
            )
        while len(contexts) < t.core_count:
            contexts.append(CpuContext(cur_util=0.1, is_online=True))
        return contexts

    def _build_sys_stat(self, t: TelemetrySnapshot, tasks: list[TaskContext]) -> SysStat:
        lat_values = [tc.lat_cri for tc in tasks] or [0.0]
        slices = [tc.slice_ns for tc in tasks] or [1_000_000]
        return SysStat(
            avg_lat_cri=sum(lat_values) / len(lat_values),
            max_lat_cri=max(lat_values),
            nr_queued_task=t.queued_tasks,
            slice=int(sum(slices) / len(slices)),
        )

    def _priority_signals(self, tasks: list[TaskContext]) -> dict[str, float]:
        """Virtual deadline ordering — higher ``lat_cri`` → earlier deadline."""
        if not tasks:
            return {}
        ranked = sorted(tasks, key=lambda tc: tc.lat_cri, reverse=True)
        return {f"task_priority_{i}": tc.lat_cri for i, tc in enumerate(ranked)}


def build_input_bundle(packet: SchedulerSignalPacket) -> InputLatentBundle:
    """Build ``InputLatentBundle`` from a live dispatch packet (Task 4 ingress)."""
    sigma = list(packet.encoded_scheduler_state)
    return InputLatentBundle(
        scheduler_signal=packet,
        shared_latent_vector=sigma,
        structured_tensor=sigma,
        ingress_confidence=0.75 if sigma else 0.25,
    )


__all__ = ["LavadSmartScheduler", "TelemetrySnapshot", "bandit_success", "build_input_bundle", "compute_evidence_quality"]
