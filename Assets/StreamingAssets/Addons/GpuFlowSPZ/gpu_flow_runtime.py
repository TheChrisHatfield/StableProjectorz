"""
GPU pacing runtime: optional Thompson bandit (see gpu_flow_bandit) + wait-for-headroom.

State persists under the user temp dir so learning survives addon server restarts.
"""
from __future__ import annotations

import json
import os
import tempfile
import threading
import time
from typing import Any, Dict, List, Optional, Tuple

import gpu_flow_bandit as gfb
from gpu_probe import sample_gpu_utilization_fraction

# Modes: 0 off, 1 bandit, 2 fixed ceiling
MODE_OFF = 0
MODE_BANDIT = 1
MODE_FIXED = 2

_STATE_LOCK = threading.RLock()
_runtime: Optional["GpuFlowRuntime"] = None


def _state_path() -> str:
    return os.path.join(tempfile.gettempdir(), "spz_gpu_flow_bandit.json")


class GpuFlowRuntime:
    def __init__(self) -> None:
        self._mode = MODE_OFF
        self._fixed_ceiling = 0.85
        self._bandit_ts: List[Tuple[float, float]] = [(1.0, 1.0) for _ in range(gfb.NUM_ARMS)]
        self._reward_ema = gfb.default_reward_ema()
        self._current_arm = 0
        self._last_util: Optional[float] = None
        self._persist_every_n = 12
        self._ticks_since_persist = 0
        self._load()

    def _load(self) -> None:
        p = _state_path()
        try:
            if not os.path.isfile(p):
                return
            with open(p, "r", encoding="utf-8") as f:
                d = json.load(f)
            if not isinstance(d, dict):
                return
            self._bandit_ts = gfb.thompson_from_nested(d)
            self._reward_ema = gfb.reward_ema_from_nested(d)
            la = d.get("last_arm", 0)
            self._current_arm = int(la) % gfb.NUM_ARMS
        except Exception:
            pass

    def _save(self) -> None:
        nested = gfb.serialize_nested(self._bandit_ts, self._current_arm, self._reward_ema)
        try:
            with open(_state_path(), "w", encoding="utf-8") as f:
                json.dump(nested, f, indent=2)
        except Exception:
            pass

    def set_mode(self, mode: int) -> None:
        with _STATE_LOCK:
            self._mode = int(max(0, min(2, mode)))

    def set_fixed_ceiling(self, ceiling_01: float) -> None:
        with _STATE_LOCK:
            self._fixed_ceiling = float(max(0.35, min(0.995, ceiling_01)))

    def status(self) -> Dict[str, Any]:
        with _STATE_LOCK:
            u = sample_gpu_utilization_fraction()
            pol = gfb.policy_for_arm(self._current_arm)
            return {
                "mode": self._mode,
                "mode_label": ("off", "bandit", "fixed")[self._mode],
                "gpu_util_fraction": u,
                "gpu_available": u is not None,
                "bandit_arm": self._current_arm,
                "util_ceiling": pol.util_ceiling,
                "min_quiet_ms": pol.min_quiet_ms,
                "fixed_ceiling": self._fixed_ceiling,
                "state_file": _state_path(),
            }

    def _active_policy(self) -> gfb.ArmPolicy:
        if self._mode == MODE_FIXED:
            return gfb.ArmPolicy(self._fixed_ceiling, max(8, int(35 * (1.05 - self._fixed_ceiling))))
        if self._mode == MODE_BANDIT:
            return gfb.policy_for_arm(self._current_arm)
        return gfb.ArmPolicy(1.0, 0)

    def pace(self, max_wait_ms: int = 12000) -> Dict[str, Any]:
        """
        Block until GPU util <= policy ceiling (or timeout), apply min quiet gap, update bandit if enabled.
        """
        t0 = time.monotonic()
        max_wait_ms = int(max(50, min(120_000, max_wait_ms)))
        with _STATE_LOCK:
            mode = self._mode
            pol = self._active_policy()
            arm_for_reward = self._current_arm

        if mode == MODE_OFF:
            return {
                "ok": True,
                "skipped": True,
                "reason": "mode_off",
                "waited_ms": 0.0,
                "gpu_util_fraction": sample_gpu_utilization_fraction(),
            }

        deadline = time.monotonic() + max_wait_ms / 1000.0
        last_u: Optional[float] = None
        while time.monotonic() < deadline:
            last_u = sample_gpu_utilization_fraction()
            if last_u is None:
                break
            if last_u <= pol.util_ceiling + 1e-6:
                break
            time.sleep(0.02)

        if pol.min_quiet_ms > 0:
            time.sleep(pol.min_quiet_ms / 1000.0)

        after_u = sample_gpu_utilization_fraction()
        util_for_reward = after_u if after_u is not None else last_u
        waited_ms = (time.monotonic() - t0) * 1000.0

        if mode == MODE_BANDIT and util_for_reward is not None:
            with _STATE_LOCK:
                r01 = gfb.reward_from_util_sample(util_for_reward, pol.util_ceiling)
                r_ts = gfb.smooth_reward_for_arm(self._reward_ema, arm_for_reward, r01, True)
                gfb.update_arm(self._bandit_ts, arm_for_reward, r_ts)
                self._current_arm = gfb.select_arm(self._bandit_ts)
                self._ticks_since_persist += 1
                if self._ticks_since_persist >= self._persist_every_n:
                    self._ticks_since_persist = 0
                    gfb.shrink_posteriors(self._bandit_ts, True)
                self._save()

        return {
            "ok": True,
            "skipped": False,
            "waited_ms": round(waited_ms, 2),
            "gpu_util_fraction_before": last_u,
            "gpu_util_fraction_after": after_u,
            "bandit_arm": arm_for_reward,
            "util_ceiling": pol.util_ceiling,
            "min_quiet_ms": pol.min_quiet_ms,
        }


def get_runtime() -> GpuFlowRuntime:
    global _runtime
    with _STATE_LOCK:
        if _runtime is None:
            _runtime = GpuFlowRuntime()
        return _runtime
