"""Physical host CPU telemetry probe for sandbox dataset collection."""

from __future__ import annotations

import platform
import socket
from dataclasses import dataclass

from hive_code_dev_1.contracts import PowerMode
from hive_code_dev_1.lavd.scheduler_adapter import TelemetrySnapshot


def psutil_available() -> bool:
    try:
        import psutil  # noqa: F401

        return True
    except ImportError:
        return False


@dataclass(frozen=True)
class HostTopology:
    profile_id: str
    hostname: str
    logical_cores: int
    physical_cores: int
    p_core_count: int
    e_core_count: int
    platform_system: str


def estimate_p_e_cores(physical: int, logical: int) -> tuple[int, int]:
    """Heuristic P/E split when OS hybrid topology is unavailable.

    Always returns ``(p, e)`` with ``p + e == max(physical, 1)``.
    ``logical`` is reserved for future hyperthread-aware splits.
    """
    _ = logical
    cores = max(1, int(physical))
    if cores <= 1:
        return 1, 0
    if cores == 2:
        return 1, 1
    # Prefer a balanced split; remainder goes to P-cores.
    p = max(1, cores // 2)
    e = cores - p
    return p, e


def detect_host_topology() -> HostTopology:
    import psutil

    hostname = socket.gethostname().split(".")[0] or "host"
    logical = psutil.cpu_count(logical=True) or 4
    physical = psutil.cpu_count(logical=False) or logical
    p_cores, e_cores = estimate_p_e_cores(physical, logical)
    profile_id = f"host-{hostname}-{physical}core"
    return HostTopology(
        profile_id=profile_id,
        hostname=hostname,
        logical_cores=logical,
        physical_cores=physical,
        p_core_count=p_cores,
        e_core_count=e_cores,
        platform_system=platform.system(),
    )


class HostTelemetryProbe:
    """Sample live CPU telemetry from the machine running the collector."""

    def __init__(self, *, sample_interval_s: float = 0.35) -> None:
        if not psutil_available():
            raise ImportError(
                "psutil is required for physical telemetry — install with: "
                "py -3.11 -m pip install -e \".[telemetry,dev]\""
            )
        self.sample_interval_s = sample_interval_s
        self.topology = detect_host_topology()

    def sample(
        self,
        *,
        latency_budget_ms: float = 50.0,
        queued_tasks: int | None = None,
        power_mode: PowerMode = PowerMode.BALANCED,
        task_lat_cri: list[float] | None = None,
    ) -> TelemetrySnapshot:
        import psutil

        percpu = psutil.cpu_percent(interval=self.sample_interval_s, percpu=True)
        utilizations = [max(0.0, min(1.0, u / 100.0)) for u in percpu]
        if not utilizations:
            utilizations = [0.1]

        while len(utilizations) < self.topology.logical_cores:
            utilizations.append(utilizations[-1])
        utilizations = utilizations[: self.topology.logical_cores]

        if queued_tasks is None:
            queued_tasks = max(1, min(32, len(psutil.pids()) // 8))

        overall = psutil.cpu_percent(interval=0.0)
        if isinstance(overall, list):
            overall = sum(overall) / len(overall) if overall else 0.0

        p_n = max(1, self.topology.p_core_count)
        # Align P/E with logical core_count so CpdomContext count matches util length (HT-safe).
        logical = self.topology.logical_cores
        p_cores, e_cores = estimate_p_e_cores(logical, logical)
        p_n = max(1, p_cores)
        p_utils = utilizations[:p_n]
        p_mean = sum(p_utils) / len(p_utils) if p_utils else 0.0
        # Activate E-cores under P-pressure, high overall util, or deep queues.
        e_core_active = e_cores > 0 and (
            overall >= 70.0
            or p_mean >= 0.75
            or queued_tasks >= max(4, logical)
        )

        return TelemetrySnapshot(
            core_count=logical,
            p_core_count=p_cores,
            e_core_count=e_cores,
            cpu_utilizations=utilizations,
            queued_tasks=queued_tasks,
            latency_budget_ms=latency_budget_ms,
            power_constraint=max(0.0, min(1.0, overall / 100.0)),
            power_mode=power_mode,
            task_lat_cri=task_lat_cri or [0.8, 0.3, 0.6],
            e_core_active=e_core_active,
        )

    def to_manifest(self) -> dict:
        return {
            "profile_id": self.topology.profile_id,
            "hostname": self.topology.hostname,
            "logical_cores": self.topology.logical_cores,
            "physical_cores": self.topology.physical_cores,
            "p_core_count": self.topology.p_core_count,
            "e_core_count": self.topology.e_core_count,
            "platform_system": self.topology.platform_system,
            "processor": platform.processor(),
        }
