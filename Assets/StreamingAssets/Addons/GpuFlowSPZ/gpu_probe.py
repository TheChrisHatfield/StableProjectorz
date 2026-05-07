"""Best-effort GPU utilization sampling for Windows/Linux (NVIDIA)."""

from __future__ import annotations

import os
import re
import shutil
import subprocess
from typing import Optional

_nvml_initialized = False


def _try_nvml() -> Optional[float]:
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


def _try_nvidia_smi() -> Optional[float]:
    exe = shutil.which("nvidia-smi")
    if not exe:
        common = os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        if os.path.isfile(common):
            exe = common
        else:
            return None
    try:
        out = subprocess.check_output(
            [exe, "--query-gpu=utilization.gpu", "--format=csv,noheader,nounits"],
            stderr=subprocess.DEVNULL,
            timeout=3.0,
            text=True,
        )
    except (subprocess.CalledProcessError, FileNotFoundError, OSError, subprocess.TimeoutExpired):
        return None
    m = re.search(r"(\d+(?:\.\d+)?)", (out or "").strip())
    if not m:
        return None
    try:
        pct = float(m.group(1))
    except ValueError:
        return None
    return max(0.0, min(1.0, pct / 100.0))


def sample_gpu_utilization_fraction() -> Optional[float]:
    """
    Returns GPU utilization as 0..1, or None if unavailable.
    """
    u = _try_nvml()
    if u is not None:
        return u
    return _try_nvidia_smi()
