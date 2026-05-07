"""
Thompson bandit over discrete GPU pacing policies (inspired by Mocap Cleaner
``latency_supervisor``: Beta posteriors + per-arm reward smoothing).

Each arm is a (util_ceiling, min_quiet_ms) pair: we try to avoid bursting the GPU
past ``util_ceiling`` (0..1 of utilization) while keeping idle gaps small enough
for throughput.
"""
from __future__ import annotations

import math
import random
from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Tuple

SCHEMA_VERSION = 1

BANDIT_REWARD_EWMA_ALPHA = 0.35
BANDIT_POSTERIOR_SHRINK = 0.985


@dataclass(frozen=True)
class ArmPolicy:
    """Wait until utilization (0..1) is at or below ``util_ceiling``; then enforce ``min_quiet_ms``."""

    util_ceiling: float
    min_quiet_ms: int


# Conservative → aggressive (higher allowed util, shorter enforced gaps).
ARMS: Tuple[ArmPolicy, ...] = (
    ArmPolicy(0.62, 55),
    ArmPolicy(0.72, 40),
    ArmPolicy(0.80, 28),
    ArmPolicy(0.88, 18),
    ArmPolicy(0.93, 12),
    ArmPolicy(0.97, 6),
)

NUM_ARMS = len(ARMS)


def policy_for_arm(idx: int) -> ArmPolicy:
    return ARMS[idx % NUM_ARMS]


def default_reward_ema() -> List[float]:
    return [0.5 for _ in range(NUM_ARMS)]


def default_nested_dict() -> Dict[str, Any]:
    return {
        "schema_version": SCHEMA_VERSION,
        "thompson": [[1.0, 1.0] for _ in range(NUM_ARMS)],
        "last_arm": 0,
        "reward_ema": default_reward_ema(),
    }


def coerce_ts_state(ts_state: Optional[Any]) -> List[Tuple[float, float]]:
    out: List[Tuple[float, float]] = []
    rows = ts_state if isinstance(ts_state, list) else []
    for ab in rows:
        if isinstance(ab, (list, tuple)) and len(ab) >= 2:
            try:
                a, b = float(ab[0]), float(ab[1])
                if math.isfinite(a) and math.isfinite(b):
                    out.append((max(1e-3, a), max(1e-3, b)))
                    continue
            except (TypeError, ValueError):
                pass
        out.append((1.0, 1.0))
    out = out[:NUM_ARMS]
    while len(out) < NUM_ARMS:
        out.append((1.0, 1.0))
    return out


def thompson_from_nested(nested: Optional[Dict[str, Any]]) -> List[Tuple[float, float]]:
    out: List[Tuple[float, float]] = [(1.0, 1.0) for _ in range(NUM_ARMS)]
    if not nested or not isinstance(nested, dict):
        return out
    if int(nested.get("schema_version", 1)) > SCHEMA_VERSION:
        return out
    ts = nested.get("thompson")
    if not isinstance(ts, list):
        return out
    for i in range(min(NUM_ARMS, len(ts))):
        row = ts[i]
        if isinstance(row, (list, tuple)) and len(row) >= 2:
            try:
                a, b = float(row[0]), float(row[1])
                if math.isfinite(a) and math.isfinite(b):
                    out[i] = (max(1e-3, a), max(1e-3, b))
            except (TypeError, ValueError):
                pass
    return out


def reward_ema_from_nested(nested: Optional[Dict[str, Any]]) -> List[float]:
    ema = default_reward_ema()
    if not nested or not isinstance(nested, dict):
        return ema
    raw = nested.get("reward_ema")
    if not isinstance(raw, list):
        return ema
    for i in range(min(NUM_ARMS, len(raw))):
        try:
            v = float(raw[i])
            if math.isfinite(v):
                ema[i] = max(0.0, min(1.0, v))
        except (TypeError, ValueError):
            pass
    return ema


def smooth_reward_for_arm(
    reward_ema: List[float], arm_idx: int, raw_reward01: float, smoothing_on: bool
) -> float:
    arm_idx = int(arm_idx) % NUM_ARMS
    r = float(max(0.0, min(1.0, raw_reward01)))
    if not smoothing_on:
        reward_ema[arm_idx] = r
        return r
    a = BANDIT_REWARD_EWMA_ALPHA
    prev = reward_ema[arm_idx]
    sm = a * r + (1.0 - a) * prev
    sm = max(0.0, min(1.0, sm))
    reward_ema[arm_idx] = sm
    return sm


def shrink_posteriors(ts_state: List[Tuple[float, float]], enabled: bool) -> None:
    if not enabled:
        return
    lam = BANDIT_POSTERIOR_SHRINK
    for i in range(len(ts_state)):
        a, b = ts_state[i]
        na = 1.0 + lam * (a - 1.0)
        nb = 1.0 + lam * (b - 1.0)
        ts_state[i] = (max(1e-3, na), max(1e-3, nb))


def select_arm(ts_state: List[Tuple[float, float]]) -> int:
    samples = [random.betavariate(max(1e-3, a), max(1e-3, b)) for a, b in ts_state]
    return int(max(range(len(samples)), key=lambda j: samples[j]))


def update_arm(ts_state: List[Tuple[float, float]], arm_idx: int, reward01: float) -> None:
    r = float(max(0.0, min(1.0, reward01)))
    if not math.isfinite(r):
        r = 0.5
    arm_idx = int(arm_idx) % NUM_ARMS
    a, b = ts_state[arm_idx]
    ts_state[arm_idx] = (a + r, b + (1.0 - r))


def reward_from_util_sample(util_frac: float, ceiling: float) -> float:
    """
    ``util_frac`` and ``ceiling`` are 0..1 (fraction of GPU utilization).
    High reward when under ceiling; penalize overshoots (bursting past policy).
    """
    u = float(max(0.0, min(1.0, util_frac)))
    c = float(max(0.05, min(0.995, ceiling)))
    if u <= c:
        return max(0.0, min(1.0, 1.0 - 0.3 * (u / c)))
    over = (u - c) / (1.0 - c + 1e-6)
    return max(0.0, min(1.0, 1.0 - 0.7 * over))


def serialize_nested(
    ts_state: List[Tuple[float, float]],
    last_arm: int,
    reward_ema: Optional[List[float]] = None,
) -> Dict[str, Any]:
    ema = reward_ema if reward_ema is not None else default_reward_ema()
    ema_out = []
    for i in range(NUM_ARMS):
        try:
            ema_out.append(float(max(0.0, min(1.0, ema[i]))))
        except (IndexError, TypeError, ValueError):
            ema_out.append(0.5)
    ts_pad = coerce_ts_state(ts_state)
    return {
        "schema_version": SCHEMA_VERSION,
        "thompson": [[float(a), float(b)] for a, b in ts_pad],
        "last_arm": int(last_arm) % NUM_ARMS,
        "reward_ema": ema_out,
    }
