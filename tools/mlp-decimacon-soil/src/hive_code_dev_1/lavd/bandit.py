"""Thompson sampling bandit loop for Smart LAVAD CPU Scheduler (spec R5)."""

from __future__ import annotations

import math
import random
from dataclasses import dataclass

from hive_code_dev_1.contracts import (
    BanditArm,
    BanditArmState,
    BanditState,
    DeploymentPrior,
    PerformanceFeedback,
)


def default_bandit_arms(prior: DeploymentPrior | None = None) -> list[BanditArmState]:
    """Initialize all four scheduling-policy arms with optional prior overrides."""
    arms: list[BanditArmState] = []
    for arm in BanditArm:
        alpha = 1.0
        beta = 1.0
        if prior:
            alpha = prior.arm_alpha_overrides.get(int(arm), alpha)
            beta = prior.arm_beta_overrides.get(int(arm), beta)
        arms.append(BanditArmState(arm=arm, alpha=alpha, beta=beta, pulls=0))
    return arms


def thompson_select(
    state: BanditState,
    *,
    rng: random.Random | None = None,
    arm_bias: int | None = None,
) -> tuple[BanditArm, float]:
    """Sample each arm's Beta posterior and return the highest (Thompson sampling)."""
    if not state.arms:
        raise ValueError("thompson_select requires at least one bandit arm")
    rng = rng or random.Random()
    best_arm = state.arms[0].arm
    best_sample = -1.0
    total_pulls = max(state.total_pulls, 1)

    for arm_state in state.arms:
        alpha = arm_state.alpha if math.isfinite(arm_state.alpha) and arm_state.alpha > 0 else 1e-6
        beta = arm_state.beta if math.isfinite(arm_state.beta) and arm_state.beta > 0 else 1e-6
        sample = rng.betavariate(max(alpha, 1e-6), max(beta, 1e-6))
        if state.rareness_correction > 0:
            overuse = arm_state.pulls / total_pulls
            sample *= 1.0 - state.rareness_correction * overuse
        if state.exploration_boost > 0 and arm_state.pulls == 0:
            sample += state.exploration_boost
        if arm_bias is not None and int(arm_state.arm) == int(arm_bias):
            sample += 0.15
        if sample > best_sample:
            best_sample = sample
            best_arm = arm_state.arm

    return best_arm, best_sample


def compute_evidence_quality(feedback: PerformanceFeedback, latency_budget: float) -> float:
    """Composite SLO + accuracy weight (approved formula C, Task 10)."""
    if latency_budget <= 0 or not math.isfinite(latency_budget):
        return 0.2
    acc = feedback.actual_accuracy if math.isfinite(feedback.actual_accuracy) else 0.0
    lat = feedback.actual_latency if math.isfinite(feedback.actual_latency) else latency_budget
    latency_term = 1.0 - lat / latency_budget
    raw = 0.5 * acc + 0.5 * latency_term
    if not math.isfinite(raw):
        return 0.2
    return max(0.2, min(1.0, raw))


def bandit_success(feedback: PerformanceFeedback, latency_budget: float) -> bool:
    if latency_budget <= 0 or not math.isfinite(latency_budget):
        return False
    if not math.isfinite(feedback.actual_accuracy) or not math.isfinite(feedback.actual_latency):
        return False
    return feedback.actual_accuracy > 0.95 and feedback.actual_latency < latency_budget


@dataclass
class BanditUpdateResult:
    updated: bool
    skipped_reason: str = ""
    success: bool = False


def update_posterior(
    state: BanditState,
    feedback: PerformanceFeedback,
    latency_budget: float,
) -> BanditUpdateResult:
    """Gate posterior update on compound ``bandit_success`` criterion (spec R5)."""
    if latency_budget <= 0:
        return BanditUpdateResult(updated=False, skipped_reason="invalid_latency_budget")

    success = bandit_success(feedback, latency_budget)
    if not success and feedback.actual_accuracy <= 0 and feedback.actual_latency <= 0:
        return BanditUpdateResult(updated=False, skipped_reason="missing_measured_outcome")

    try:
        arm = BanditArm(feedback.selected_arm)
    except ValueError:
        return BanditUpdateResult(updated=False, skipped_reason="unknown_arm")

    arm_state = _find_arm(state, arm)
    if arm_state is None:
        return BanditUpdateResult(updated=False, skipped_reason="unknown_arm")

    quality = (
        feedback.evidence_quality
        if 0.0 < feedback.evidence_quality <= 1.0
        else compute_evidence_quality(feedback, latency_budget)
    )
    weight = quality * state.posterior_discount

    if success:
        arm_state.alpha += 1.0 * weight
    else:
        arm_state.beta += 1.0 * weight
        best_mean = max(a.alpha / (a.alpha + a.beta) for a in state.arms)
        chosen_mean = arm_state.alpha / (arm_state.alpha + arm_state.beta)
        state.cumulative_regret += max(0.0, best_mean - chosen_mean)
    arm_state.pulls += 1
    state.total_pulls += 1
    return BanditUpdateResult(updated=True, success=success)


def _find_arm(state: BanditState, arm: BanditArm) -> BanditArmState | None:
    for arm_state in state.arms:
        if arm_state.arm == arm:
            return arm_state
    return None
