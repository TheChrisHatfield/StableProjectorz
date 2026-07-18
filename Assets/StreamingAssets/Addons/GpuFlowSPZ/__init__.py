"""
GPU Flow — adaptive pacing for the StableProjectorz Python add-on server.

Borrowed idea from Mocap Cleaner's ``latency_supervisor`` (Thompson sampling over
discrete pacing policies), applied to NVIDIA GPU utilization instead of WM timer
spacing: each \"arm\" is a (util_ceiling, min_quiet_ms) policy.

External tools (e.g. SD WebUI helpers) can call:
  GET  http://127.0.0.1:5557/api/v1/gpu-flow/status
  POST http://127.0.0.1:5557/api/v1/gpu-flow/pace  {\"max_wait_ms\": 8000}

Requires ``nvidia-smi`` or ``pynvml`` (first queried GPU only; NVML uses index 0). Without readings, waits still apply but the bandit does not learn.

Unity automatically POSTs to ``/api/v1/gpu-flow/pace`` before and after SD (WebUI) and Gen3D requests when the add-on HTTP server is running; mode **Off** keeps those calls as no-ops. The viewport render loop is not hooked. External scripts can still call the same REST endpoints or ``pace()`` directly.
"""

from __future__ import annotations

import json
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
_current_mode = 1
_SETTINGS_PATH = os.path.join(_root, "settings.json")

_DEFAULT_SETTINGS = {
    "mode": 1,
    "fixed_ceiling": 0.85,
}

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


def _load_settings() -> dict:
    s = dict(_DEFAULT_SETTINGS)
    try:
        if os.path.isfile(_SETTINGS_PATH):
            with open(_SETTINGS_PATH, "r", encoding="utf-8") as f:
                data = json.load(f)
            if isinstance(data, dict):
                s.update(data)
    except Exception as e:
        print(f"[{ADDON_ID}] Failed to load settings: {e}")
    try:
        s["mode"] = max(0, min(2, int(s.get("mode", 1))))
    except (TypeError, ValueError):
        s["mode"] = 1
    try:
        s["fixed_ceiling"] = max(0.5, min(0.99, float(s.get("fixed_ceiling", 0.85))))
    except (TypeError, ValueError):
        s["fixed_ceiling"] = 0.85
    return s


def _save_settings() -> None:
    if _panel is not None and _el.get("ceiling"):
        try:
            c = float(_panel.get_value(_el["ceiling"]) or 0.85)
        except (TypeError, ValueError):
            c = 0.85
    else:
        c = 0.85
    payload = {
        "mode": int(max(0, min(2, _current_mode))),
        "fixed_ceiling": float(max(0.5, min(0.99, c))),
    }
    try:
        with open(_SETTINGS_PATH, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2)
    except Exception as e:
        print(f"[{ADDON_ID}] Failed to save settings: {e}")


def _mode_label(ix: int) -> str:
    ix = max(0, min(2, int(ix)))
    return _MODES[ix]


def _set_mode(mode: int, announce: bool = False) -> None:
    global _current_mode
    mode = max(0, min(2, int(mode)))
    _current_mode = mode
    try:
        gfr.get_runtime().set_mode(mode)
    except Exception:
        pass
    if _panel is not None and _el.get("mode_state"):
        try:
            _panel.set_value(_el["mode_state"], _mode_label(mode))
        except Exception:
            pass
    if announce:
        _show(f"GPU Flow mode: {_mode_label(mode)}", duration=2.0)
    _save_settings()


def _sync_from_panel() -> None:
    global _panel, _el, _current_mode
    if _panel is None:
        return
    rt = gfr.get_runtime()
    rt.set_mode(_current_mode)
    try:
        c = float(_panel.get_value(_el.get("ceiling")) or 0.85)
    except (TypeError, ValueError):
        c = 0.85
    rt.set_fixed_ceiling(c)
    _save_settings()


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
    ceil_v = s.get("util_ceiling")
    ceil_s = f"{float(ceil_v):.2f}" if ceil_v is not None else "?"
    pfrac = s.get("power_frac_of_limit")
    pd = s.get("power_draw_w")
    pl = s.get("power_limit_w")
    if pfrac is not None and pd is not None and pl is not None:
        ptxt = f" | power≈{int(round(float(pfrac) * 100.0))}% ({float(pd):.0f}/{float(pl):.0f}W)"
    else:
        ptxt = ""
    _show(
        f"GPU ~{pct}% | mode={s.get('mode_label')} | arm={s.get('bandit_arm')} "
        f"| ceiling≈{ceil_s}{ptxt}",
        duration=3.0,
    )


def gpu_flow_pace_once() -> None:
    _sync_from_panel()
    r = gfr.get_runtime().pace(max_wait_ms=15000, source="addon_panel", phase="manual_pace")
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


def gpu_flow_mode_off() -> None:
    _set_mode(0, announce=True)


def gpu_flow_mode_adaptive() -> None:
    _set_mode(1, announce=True)


def gpu_flow_mode_fixed() -> None:
    _set_mode(2, announce=True)


def register() -> None:
    global _panel, _el, _current_mode

    api = spz.get_api()
    settings = _load_settings()
    _current_mode = int(settings.get("mode", 1))
    start_ceiling = float(settings.get("fixed_ceiling", 0.85))
    _panel = api.ui.create_panel(ADDON_ID, ADDON_TITLE)
    if not _panel:
        raise RuntimeError(
            f"[{ADDON_ID}] create_panel failed — refusing successful load so Unity tears down the ribbon shell"
        )

    # Dropdown widgets can be unreliable in add-on panels on some builds; use explicit mode buttons.
    _el["mode_state"] = _panel.add_text_input("Pacing mode (use buttons below)", _mode_label(_current_mode))
    _el["ceiling"] = _panel.add_slider("Fixed util ceiling", 0.5, 0.99, start_ceiling)

    _set_mode(_current_mode, announce=False)
    try:
        gfr.get_runtime().set_fixed_ceiling(start_ceiling)
    except Exception:
        pass

    _panel.add_button("Mode: Off", "gpu_flow_mode_off")
    _panel.add_button("Mode: Adaptive", "gpu_flow_mode_adaptive")
    _panel.add_button("Mode: Fixed ceiling", "gpu_flow_mode_fixed")
    _panel.add_button("Refresh GPU status", "gpu_flow_refresh_status")
    _panel.add_button("Pace once (wait for headroom)", "gpu_flow_pace_once")

    print(f"[{ADDON_ID}] Registered — REST: GET/POST /api/v1/gpu-flow/* ; Unity hooks SD/Gen3D when mode is not Off")
    _show(
        "GPU Flow loaded. Set Adaptive or Fixed to auto-pace before/after SD (A1111/Forge) and Gen3D jobs. Off = no delay.",
        duration=4.0,
    )
    try:
        s = gfr.get_runtime().status()
        print(f"[{ADDON_ID}] Telemetry JSONL: {s.get('telemetry_jsonl')}")
        print(f"[{ADDON_ID}] Startup mode={s.get('mode_label')} source={s.get('last_source')} phase={s.get('last_phase')}")
    except Exception:
        pass


def unregister() -> None:
    _save_settings()
    print(f"[{ADDON_ID}] Unregistered")
