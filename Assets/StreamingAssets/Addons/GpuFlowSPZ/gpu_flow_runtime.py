"""
GPU pacing runtime: optional Thompson bandit (see gpu_flow_bandit) + wait-for-headroom.

State/telemetry persist under ``Documents/StableProjectorz/GpuFlowSPZ`` so learning survives restarts.
"""
from __future__ import annotations

import atexit
import json
import os
import tempfile
import threading
import time
import uuid
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Tuple

import gpu_flow_bandit as gfb
from gpu_probe import sample_gpu_power_metrics, sample_gpu_utilization_fraction

# Modes: 0 off, 1 bandit, 2 fixed ceiling
MODE_OFF = 0
MODE_BANDIT = 1
MODE_FIXED = 2

_MODE_LABELS = ("off", "bandit", "fixed")

_STATE_LOCK = threading.RLock()
_runtime: Optional["GpuFlowRuntime"] = None


def _state_path() -> str:
    return os.path.join(_runtime_dir(), "gpu_flow_bandit_state.json")


def _runtime_dir() -> str:
    home = os.path.expanduser("~")
    docs = os.path.join(home, "Documents")
    base = docs if os.path.isdir(docs) else home
    d = os.path.join(base, "StableProjectorz", "GpuFlowSPZ")
    try:
        os.makedirs(d, exist_ok=True)
    except OSError:
        # Last-resort fallback keeps addon operational even on unusual user profiles.
        d = tempfile.gettempdir()
    return d


def _telemetry_jsonl_path() -> str:
    return os.path.join(_runtime_dir(), "gpu_flow_telemetry.jsonl")


def _session_state_path() -> str:
    return os.path.join(_runtime_dir(), "gpu_flow_session_state.json")


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


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
        self._session_id = str(uuid.uuid4())
        self._started_at_iso = _utc_now_iso()
        self._recent_high_util_hits = 0
        self._recent_power_near_limit_hits = 0
        self._recent_timeouts = 0
        self._crash_streak = 0
        self._last_source = "unknown"
        self._last_phase = "unknown"
        self._session_state_dirty = False
        self._load()
        self._recover_session_state()
        self._mark_session_started()
        self._append_event(
            "session_start",
            {
                "session_id": self._session_id,
                "mode": int(self._mode),
                "mode_label": _MODE_LABELS[int(self._mode)] if 0 <= int(self._mode) < len(_MODE_LABELS) else "unknown",
                "crash_streak": self._crash_streak,
                "state_file": _state_path(),
                "telemetry_jsonl": _telemetry_jsonl_path(),
            },
        )
        atexit.register(self._on_clean_exit)

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
            try:
                self._current_arm = int(la) % gfb.NUM_ARMS if la is not None else 0
            except (TypeError, ValueError):
                self._current_arm = 0
            self._crash_streak = self._coerce_int_nonneg(d.get("crash_streak", 0), default=0)
            self._recent_high_util_hits = self._coerce_int_nonneg(d.get("recent_high_util_hits", 0), default=0)
            self._recent_power_near_limit_hits = self._coerce_int_nonneg(d.get("recent_power_near_limit_hits", 0), default=0)
            self._recent_timeouts = self._coerce_int_nonneg(d.get("recent_timeouts", 0), default=0)
        except Exception:
            pass

    def _coerce_int_nonneg(self, value: Any, default: int = 0) -> int:
        try:
            if value is None:
                return int(max(0, default))
            return int(max(0, int(value)))
        except (TypeError, ValueError):
            return int(max(0, default))

    def _save(self) -> None:
        nested = gfb.serialize_nested(self._bandit_ts, self._current_arm, self._reward_ema)
        nested["crash_streak"] = int(max(0, self._crash_streak))
        nested["recent_high_util_hits"] = int(max(0, self._recent_high_util_hits))
        nested["recent_power_near_limit_hits"] = int(max(0, self._recent_power_near_limit_hits))
        nested["recent_timeouts"] = int(max(0, self._recent_timeouts))
        try:
            with open(_state_path(), "w", encoding="utf-8") as f:
                json.dump(nested, f, indent=2)
        except Exception:
            pass

    def _recover_session_state(self) -> None:
        p = _session_state_path()
        try:
            if not os.path.isfile(p):
                return
            with open(p, "r", encoding="utf-8") as f:
                d = json.load(f)
            if not isinstance(d, dict):
                return
            prev_clean = bool(d.get("last_exit_clean", True))
            prev_session = d.get("last_session_id")
            if not prev_clean and prev_session:
                self._crash_streak = int(max(0, self._crash_streak + 1))
                self._append_event(
                    "session_recovered_unclean_exit",
                    {
                        "previous_session_id": str(prev_session),
                        "crash_streak": self._crash_streak,
                    },
                )
        except Exception:
            # Keep runtime resilient even if the session index file is malformed.
            pass

    def _mark_session_started(self) -> None:
        self._write_session_state(last_exit_clean=False)

    def _on_clean_exit(self) -> None:
        with _STATE_LOCK:
            self._append_event(
                "session_exit",
                {
                    "session_id": self._session_id,
                    "uptime_s": round(max(0.0, time.time() - self._startup_unix_s()), 3),
                    "recent_high_util_hits": self._recent_high_util_hits,
                    "recent_power_near_limit_hits": self._recent_power_near_limit_hits,
                    "recent_timeouts": self._recent_timeouts,
                    "crash_streak": self._crash_streak,
                },
            )
            self._write_session_state(last_exit_clean=True)
            self._save()

    def _startup_unix_s(self) -> float:
        try:
            return datetime.fromisoformat(self._started_at_iso).timestamp()
        except Exception:
            return time.time()

    def _write_session_state(self, last_exit_clean: bool) -> None:
        d = {
            "last_session_id": self._session_id,
            "started_at_utc": self._started_at_iso,
            "last_exit_clean": bool(last_exit_clean),
        }
        try:
            with open(_session_state_path(), "w", encoding="utf-8") as f:
                json.dump(d, f, indent=2)
            self._session_state_dirty = True
        except Exception:
            pass

    def _append_event(self, event_type: str, payload: Dict[str, Any]) -> None:
        rec = {
            "ts_utc": _utc_now_iso(),
            "event": str(event_type),
            "session_id": self._session_id,
        }
        rec.update(payload or {})
        line = json.dumps(rec, ensure_ascii=True)
        try:
            with open(_telemetry_jsonl_path(), "a", encoding="utf-8") as f:
                f.write(line + "\n")
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
            pm = sample_gpu_power_metrics()
            # Active pacing policy (fixed mode uses fixed ceiling, not the bandit arm).
            pol = self._active_policy()
            m = int(self._mode)
            label = _MODE_LABELS[m] if 0 <= m < len(_MODE_LABELS) else "unknown"
            return {
                "mode": m,
                "mode_label": label,
                "gpu_util_fraction": u,
                "gpu_available": u is not None,
                "power_draw_w": pm.get("power_draw_w"),
                "power_limit_w": pm.get("power_limit_w"),
                "power_frac_of_limit": pm.get("power_frac_of_limit"),
                "bandit_arm": self._current_arm,
                "util_ceiling": pol.util_ceiling,
                "min_quiet_ms": pol.min_quiet_ms,
                "fixed_ceiling": self._fixed_ceiling,
                "state_file": _state_path(),
                "telemetry_jsonl": _telemetry_jsonl_path(),
                "session_id": self._session_id,
                "crash_streak": self._crash_streak,
                "recent_high_util_hits": self._recent_high_util_hits,
                "recent_power_near_limit_hits": self._recent_power_near_limit_hits,
                "recent_timeouts": self._recent_timeouts,
                "last_source": self._last_source,
                "last_phase": self._last_phase,
                "note": "Unity calls /api/v1/gpu-flow/pace before/after SD and Gen3D requests when the add-on HTTP server is up; mode Off skips delays.",
            }

    def _active_policy(self) -> gfb.ArmPolicy:
        # Safety derate after unstable sessions/high-util streaks to avoid PSU-triggered spikes.
        safety_drop = min(
            0.28,
            0.03 * self._crash_streak
            + 0.008 * self._recent_high_util_hits
            + 0.012 * self._recent_power_near_limit_hits,
        )
        extra_quiet = min(180, 8 * self._crash_streak + 3 * self._recent_timeouts + 4 * self._recent_power_near_limit_hits)
        if self._mode == MODE_FIXED:
            c = max(0.35, min(0.995, self._fixed_ceiling - safety_drop))
            return gfb.ArmPolicy(c, max(8, int(35 * (1.05 - c))) + extra_quiet)
        if self._mode == MODE_BANDIT:
            pol = gfb.policy_for_arm(self._current_arm)
            c = max(0.35, min(0.995, pol.util_ceiling - safety_drop))
            return gfb.ArmPolicy(c, int(pol.min_quiet_ms + extra_quiet))
        return gfb.ArmPolicy(max(0.8, 1.0 - safety_drop), int(extra_quiet))

    def pace(
        self,
        max_wait_ms: int = 12000,
        source: str = "unknown",
        phase: str = "unknown",
        run_id: Optional[str] = None,
    ) -> Dict[str, Any]:
        """
        Block until GPU util <= policy ceiling (or timeout), apply min quiet gap, update bandit if enabled.
        """
        t0 = time.monotonic()
        max_wait_ms = int(max(50, min(120_000, max_wait_ms)))
        source_s = str(source or "unknown")
        phase_s = str(phase or "unknown")
        with _STATE_LOCK:
            mode = self._mode
            pol = self._active_policy()
            arm_for_reward = self._current_arm
            self._last_source = source_s
            self._last_phase = phase_s

        if mode == MODE_OFF:
            out = {
                "ok": True,
                "skipped": True,
                "reason": "mode_off",
                "waited_ms": 0.0,
                "gpu_util_fraction": sample_gpu_utilization_fraction(),
                "session_id": self._session_id,
            }
            self._append_event(
                "pace_skipped",
                {
                    "source": source_s,
                    "phase": phase_s,
                    "run_id": run_id,
                    "mode": int(mode),
                    "mode_label": _MODE_LABELS[int(mode)] if 0 <= int(mode) < len(_MODE_LABELS) else "unknown",
                    "max_wait_ms": max_wait_ms,
                    "gpu_util_fraction": out.get("gpu_util_fraction"),
                },
            )
            return out

        deadline = time.monotonic() + max_wait_ms / 1000.0
        last_u: Optional[float] = None
        last_pf: Optional[float] = None
        headroom_ok = False
        while time.monotonic() < deadline:
            last_u = sample_gpu_utilization_fraction()
            p = sample_gpu_power_metrics()
            last_pf = p.get("power_frac_of_limit")
            if last_u is None:
                # If util is unavailable, allow power-based gating to continue.
                if last_pf is None:
                    break
            # If util is unavailable, rely on power gate only instead of forcing timeout.
            util_ok = (last_u is None) or (last_u <= pol.util_ceiling + 1e-6)
            power_ok = (last_pf is None or last_pf <= 0.95)
            if util_ok and power_ok:
                headroom_ok = True
                break
            time.sleep(0.02)

        if pol.min_quiet_ms > 0:
            time.sleep(pol.min_quiet_ms / 1000.0)

        after_u = sample_gpu_utilization_fraction()
        after_pm = sample_gpu_power_metrics()
        after_pf = after_pm.get("power_frac_of_limit")
        util_for_reward = after_u if after_u is not None else last_u
        waited_ms = (time.monotonic() - t0) * 1000.0
        util_ok_after = (after_u is None) or (after_u <= pol.util_ceiling + 1e-6)
        if util_ok_after and (after_pf is None or after_pf <= 0.95):
            headroom_ok = True

        if mode == MODE_BANDIT and util_for_reward is not None:
            with _STATE_LOCK:
                r01 = gfb.reward_from_util_sample(util_for_reward, pol.util_ceiling)
                if after_pf is not None:
                    # Penalize near-limit power draw even when util seems acceptable.
                    if after_pf >= 1.00:
                        r01 *= 0.55
                    elif after_pf >= 0.96:
                        r01 *= 0.72
                    elif after_pf >= 0.92:
                        r01 *= 0.86
                r_ts = gfb.smooth_reward_for_arm(self._reward_ema, arm_for_reward, r01, True)
                gfb.update_arm(self._bandit_ts, arm_for_reward, r_ts)
                self._current_arm = gfb.select_arm(self._bandit_ts)
                self._ticks_since_persist += 1
                if self._ticks_since_persist >= self._persist_every_n:
                    self._ticks_since_persist = 0
                    gfb.shrink_posteriors(self._bandit_ts, True)
        with _STATE_LOCK:
            if util_for_reward is not None and util_for_reward >= 0.96:
                self._recent_high_util_hits = min(5000, self._recent_high_util_hits + 1)
            else:
                self._recent_high_util_hits = max(0, self._recent_high_util_hits - 1)
            pf_for_risk = after_pf if after_pf is not None else last_pf
            if pf_for_risk is not None and pf_for_risk >= 0.95:
                self._recent_power_near_limit_hits = min(5000, self._recent_power_near_limit_hits + 1)
            else:
                self._recent_power_near_limit_hits = max(0, self._recent_power_near_limit_hits - 1)
            if not headroom_ok:
                self._recent_timeouts = min(5000, self._recent_timeouts + 1)
            else:
                self._recent_timeouts = max(0, self._recent_timeouts - 1)
            self._save()

        out = {
            "ok": True,
            "skipped": False,
            "headroom_ok": headroom_ok,
            "waited_ms": round(waited_ms, 2),
            "gpu_util_fraction_before": last_u,
            "gpu_util_fraction_after": after_u,
            "power_draw_w_after": after_pm.get("power_draw_w"),
            "power_limit_w_after": after_pm.get("power_limit_w"),
            "power_frac_of_limit_after": after_pf,
            "bandit_arm": arm_for_reward,
            "util_ceiling": pol.util_ceiling,
            "min_quiet_ms": pol.min_quiet_ms,
            "session_id": self._session_id,
            "source": source_s,
            "phase": phase_s,
            "run_id": run_id,
            "crash_streak": self._crash_streak,
            "recent_high_util_hits": self._recent_high_util_hits,
            "recent_power_near_limit_hits": self._recent_power_near_limit_hits,
            "recent_timeouts": self._recent_timeouts,
        }
        self._append_event(
            "pace",
            {
                "source": source_s,
                "phase": phase_s,
                "run_id": run_id,
                "mode": int(mode),
                "mode_label": _MODE_LABELS[int(mode)] if 0 <= int(mode) < len(_MODE_LABELS) else "unknown",
                "max_wait_ms": max_wait_ms,
                "waited_ms": out["waited_ms"],
                "headroom_ok": headroom_ok,
                "gpu_util_fraction_before": last_u,
                "gpu_util_fraction_after": after_u,
                "power_draw_w_after": after_pm.get("power_draw_w"),
                "power_limit_w_after": after_pm.get("power_limit_w"),
                "power_frac_of_limit_after": after_pf,
                "util_ceiling": pol.util_ceiling,
                "min_quiet_ms": pol.min_quiet_ms,
                "bandit_arm": arm_for_reward,
                "crash_streak": self._crash_streak,
                "recent_high_util_hits": self._recent_high_util_hits,
                "recent_power_near_limit_hits": self._recent_power_near_limit_hits,
                "recent_timeouts": self._recent_timeouts,
            },
        )
        return out


def get_runtime() -> GpuFlowRuntime:
    global _runtime
    with _STATE_LOCK:
        if _runtime is None:
            _runtime = GpuFlowRuntime()
        return _runtime
