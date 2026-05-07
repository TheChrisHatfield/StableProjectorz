"""Best-effort GPU telemetry sampling for Windows/Linux (NVIDIA)."""

from __future__ import annotations

import os
import shutil
import subprocess
from typing import Dict, Optional

_nvml_initialized = False


def _first_number(text: str) -> Optional[float]:
    if not text:
        return None
    num = ""
    dot_seen = False
    started = False
    for ch in text:
        if ch.isdigit():
            num += ch
            started = True
        elif ch == "." and started and not dot_seen:
            num += ch
            dot_seen = True
        elif started:
            break
    if not num:
        return None
    try:
        return float(num)
    except ValueError:
        return None


def _resolve_nvidia_smi() -> Optional[str]:
    exe = shutil.which("nvidia-smi")
    if exe:
        return exe
    common = os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
    if os.path.isfile(common):
        return common
    return None


def _try_nvml_util() -> Optional[float]:
    global _nvml_initialized
    try:
        import pynvml  # type: ignore
    except ImportError:
        return None
    try:
        if not _nvml_initialized:
            pynvml.nvmlInit()
            _nvml_initialized = True
        h = pynvml.nvmlDeviceGetHandleByIndex(0)
        rate = pynvml.nvmlDeviceGetUtilizationRates(h)
        return float(rate.gpu) / 100.0
    except Exception:
        return None


def _try_nvml_power_metrics() -> Dict[str, Optional[float]]:
    global _nvml_initialized
    try:
        import pynvml  # type: ignore
    except ImportError:
        return {"power_draw_w": None, "power_limit_w": None, "power_frac_of_limit": None}
    try:
        if not _nvml_initialized:
            pynvml.nvmlInit()
            _nvml_initialized = True
        h = pynvml.nvmlDeviceGetHandleByIndex(0)
        draw_mw = float(pynvml.nvmlDeviceGetPowerUsage(h))
        lim_mw = float(pynvml.nvmlDeviceGetEnforcedPowerLimit(h))
        draw_w = max(0.0, draw_mw / 1000.0)
        lim_w = max(0.0, lim_mw / 1000.0)
        frac = (draw_w / lim_w) if lim_w > 1e-6 else None
        return {"power_draw_w": draw_w, "power_limit_w": lim_w, "power_frac_of_limit": frac}
    except Exception:
        return {"power_draw_w": None, "power_limit_w": None, "power_frac_of_limit": None}


def _try_nvidia_smi() -> Dict[str, Optional[float]]:
    exe = _resolve_nvidia_smi()
    if not exe:
        return {"util_frac": None, "power_draw_w": None, "power_limit_w": None, "power_frac_of_limit": None}
    try:
        out = subprocess.check_output(
            [exe, "--query-gpu=utilization.gpu,power.draw,power.limit", "--format=csv,noheader,nounits"],
            stderr=subprocess.DEVNULL,
            timeout=3.0,
            text=True,
        )
    except (subprocess.CalledProcessError, FileNotFoundError, OSError, subprocess.TimeoutExpired):
        return {"util_frac": None, "power_draw_w": None, "power_limit_w": None, "power_frac_of_limit": None}
    lines = (out or "").strip().splitlines()
    line = lines[0] if lines else ""
    parts = [p.strip() for p in line.split(",")]
    util_pct = _first_number(parts[0]) if len(parts) > 0 else None
    draw_w = _first_number(parts[1]) if len(parts) > 1 else None
    lim_w = _first_number(parts[2]) if len(parts) > 2 else None
    util = max(0.0, min(1.0, util_pct / 100.0)) if util_pct is not None else None
    power_frac = (draw_w / lim_w) if (draw_w is not None and lim_w is not None and lim_w > 1e-6) else None
    return {"util_frac": util, "power_draw_w": draw_w, "power_limit_w": lim_w, "power_frac_of_limit": power_frac}


def sample_gpu_utilization_fraction() -> Optional[float]:
    """
    Returns GPU utilization as 0..1, or None if unavailable.
    """
    u = _try_nvml_util()
    if u is not None:
        return u
    return _try_nvidia_smi().get("util_frac")


def sample_gpu_power_metrics() -> Dict[str, Optional[float]]:
    """
    Returns GPU power metrics for device 0:
    ``power_draw_w``, ``power_limit_w``, ``power_frac_of_limit``.
    """
    p = _try_nvml_power_metrics()
    if p.get("power_draw_w") is not None:
        return p
    smi = _try_nvidia_smi()
    return {
        "power_draw_w": smi.get("power_draw_w"),
        "power_limit_w": smi.get("power_limit_w"),
        "power_frac_of_limit": smi.get("power_frac_of_limit"),
    }
