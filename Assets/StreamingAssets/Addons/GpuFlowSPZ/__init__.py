"""
GPU Flow — adaptive pacing for the StableProjectorz Python add-on server.

Borrowed idea from Mocap Cleaner's ``latency_supervisor`` (Thompson sampling over
discrete pacing policies), applied to NVIDIA GPU utilization instead of WM timer
spacing: each \"arm\" is a (util_ceiling, min_quiet_ms) policy.

External tools (e.g. SD WebUI helpers) can call:
  GET  http://127.0.0.1:5557/api/v1/gpu-flow/status
  POST http://127.0.0.1:5557/api/v1/gpu-flow/pace  {\"max_wait_ms\": 8000}

Requires ``nvidia-smi`` or ``pynvml`` for readings; without them, pacing still runs but rewards are weaker.
"""

from __future__ import annotations

import os
import sys

_root = os.path.dirname(os.path.abspath(__file__))
if _root not in sys.path:
    sys.path.insert(0, _root)

addon_system_dir = os.path.join(_root, "..", "..", "AddonSystem")
if os.path.exists(addon_system_dir):
    sys.path.insert(0, addon_system_dir)

try:
    import spz
except ImportError:
    print("[GpuFlowSPZ] Error: Could not import spz module")
    raise

import gpu_flow_runtime as gfr

ADDON_ID = "GpuFlowSPZ"
ADDON_TITLE = "GPU Flow"

_panel = None
_el = {}

_MODES = [
    "Off (no pacing)",
    "Adaptive (Thompson bandit)",
    "Fixed ceiling",
]


def _show(msg: str, duration: float = 2.5) -> None:
    try:
        spz.get_api().ui_chrome.show_status_text(msg, duration=duration)
    except Exception:
        print(f"[{ADDON_ID}] {msg}")


def _sync_from_panel() -> None:
    global _panel, _el
    if _panel is None:
        return
    rt = gfr.get_runtime()
    try:
        mode_raw = _panel.get_value(_el.get("mode"))
        mode = int(mode_raw) if mode_raw is not None else 0
    except (TypeError, ValueError):
        mode = 0
    mode = max(0, min(2, mode))
    rt.set_mode(mode)
    try:
        c = float(_panel.get_value(_el.get("ceiling")) or 0.85)
    except (TypeError, ValueError):
        c = 0.85
    rt.set_fixed_ceiling(c)


def gpu_flow_refresh_status() -> None:
    _sync_from_panel()
    s = gfr.get_runtime().status()
    u = s.get("gpu_util_fraction")
    if u is None:
        _show(
            "GPU: no sampler (install NVIDIA driver / nvidia-smi or pip install nvidia-ml-py). "
            f"Mode={s.get('mode_label')}, arm={s.get('bandit_arm')}",
            duration=4.0,
        )
        return
    pct = int(round(float(u) * 100.0))
    _show(
        f"GPU ~{pct}% | mode={s.get('mode_label')} | arm={s.get('bandit_arm')} "
        f"| ceiling≈{s.get('util_ceiling'):.2f}",
        duration=3.0,
    )


def gpu_flow_pace_once() -> None:
    _sync_from_panel()
    r = gfr.get_runtime().pace(max_wait_ms=15000)
    if r.get("skipped"):
        _show("Pacing off (mode Off).", duration=2.0)
        return
    ub = r.get("gpu_util_fraction_before")
    ua = r.get("gpu_util_fraction_after")
    ms = r.get("waited_ms", 0)
    if ua is not None and ub is not None:
        _show(
            f"Pace done: waited {ms:.0f}ms, GPU {int(ub * 100)}% → {int(ua * 100)}%",
            duration=3.5,
        )
    else:
        _show(f"Pace done: waited {ms:.0f}ms (GPU sample n/a).", duration=3.0)


def register() -> None:
    global _panel, _el

    api = spz.get_api()
    _panel = api.ui.create_panel(ADDON_ID, ADDON_TITLE)
    if not _panel:
        print(f"[{ADDON_ID}] Failed to create panel")
        return

    _el["mode"] = _panel.add_dropdown("Pacing mode", _MODES, 0)
    _el["ceiling"] = _panel.add_slider("Fixed util ceiling", 0.5, 0.99, 0.85)

    _panel.add_button("Refresh GPU status", "gpu_flow_refresh_status")
    _panel.add_button("Pace once (wait for headroom)", "gpu_flow_pace_once")

    print(f"[{ADDON_ID}] Registered — REST: GET/POST /api/v1/gpu-flow/*")
    _show("GPU Flow add-on loaded. Enable Adaptive or Fixed, then use Pace or HTTP API.", duration=3.0)


def unregister() -> None:
    print(f"[{ADDON_ID}] Unregistered")
