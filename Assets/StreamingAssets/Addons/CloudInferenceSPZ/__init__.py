"""
CloudInferenceSPZ

End-user cloud inference for StableProjectorz: paste a key/session URL, Connect,
and keep the SD connection panel on 127.0.0.1:7860. A local Forge-compatible
shim serves /internal/ping, /sdapi/v1/*, and soft ControlNet stubs.

Backends:
  Demo         — solid PNG (validates SPZ without GPU)
  Remote/Colab — proxy to a Forge tunnel URL (Colab, RunPod, etc.)
  fal          — reserved (clear error until thick shim)
"""

from __future__ import annotations

import json
import os
import sys
import urllib.request
from typing import Dict, Optional, Tuple

_root = os.path.dirname(os.path.abspath(__file__))
if _root not in sys.path:
    sys.path.insert(0, _root)

addon_system_dir = os.path.join(_root, "..", "..", "AddonSystem")
if os.path.exists(addon_system_dir):
    sys.path.insert(0, addon_system_dir)

try:
    import spz
except ImportError:
    print("[CloudInferenceSPZ] Error: Could not import spz module")
    raise

import backends as be
import forge_shim as shim

ADDON_ID = "CloudInferenceSPZ"
ADDON_TITLE = "Cloud Inference"

_panel = None
_el: Dict[str, str] = {}
_SETTINGS_PATH = os.path.join(_root, "settings.json")

_BACKEND_LABELS = [
    "Demo (no GPU)",
    "Remote Forge / Colab URL",
    "fal (soon)",
]

_DEFAULT_SETTINGS = {
    "backend_index": 0,
    "credential": "",
    "auto_connect": False,
}


def _show(msg: str, duration: float = 2.8) -> None:
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
        s["backend_index"] = max(0, min(2, int(s.get("backend_index", 0))))
    except (TypeError, ValueError):
        s["backend_index"] = 0
    s["credential"] = str(s.get("credential", "") or "")
    s["auto_connect"] = bool(s.get("auto_connect", False))
    return s


def _save_settings(settings: Optional[dict] = None) -> bool:
    payload = settings or _read_settings_from_panel()
    out = {
        "backend_index": int(max(0, min(2, payload.get("backend_index", 0)))),
        "credential": str(payload.get("credential", "") or ""),
        "auto_connect": bool(payload.get("auto_connect", False)),
    }
    try:
        with open(_SETTINGS_PATH, "w", encoding="utf-8") as f:
            json.dump(out, f, indent=2)
        return True
    except Exception as e:
        print(f"[{ADDON_ID}] Failed to save settings: {e}")
        return False


def _panel_value_text(key: str, default: str = "") -> str:
    if _panel is None:
        return default
    element = _el.get(key)
    if not element:
        return default
    try:
        raw = _panel.get_value(element)
    except Exception:
        return default
    if raw is None:
        return default
    return str(raw).strip()


def _backend_index_from_panel() -> int:
    """Dropdown get_value returns an int index from AddonUI_MGR; accept label text as fallback."""
    raw = _panel_value_text("backend", "0")
    try:
        return max(0, min(2, int(float(raw))))
    except (TypeError, ValueError):
        pass
    for i, name in enumerate(_BACKEND_LABELS):
        if raw == name or raw.lower() == name.lower():
            return i
    return 0


def _mode_from_index(ix: int) -> str:
    ix = max(0, min(2, int(ix)))
    return ("demo", "remote_forge", "fal")[ix]


def _read_settings_from_panel() -> dict:
    return {
        "backend_index": _backend_index_from_panel(),
        "credential": _panel_value_text("credential", ""),
        "auto_connect": _panel_value_text("auto_connect", "Off").lower() in ("1", "true", "on", "yes"),
    }


def _set_status(text: str) -> None:
    if _panel is None or not _el.get("status"):
        return
    try:
        _panel.set_value(_el["status"], text)
    except Exception:
        pass


def _status_line() -> str:
    running = shim.is_running()
    snap = shim.get_state().snapshot_status()
    if not running:
        return "Disconnected — press Connect (keep SPZ SD panel on 127.0.0.1:7860)"
    err = snap.get("last_error") or ""
    base = f"Connected · {snap.get('backend')} · {snap.get('listen')}"
    if err:
        return f"{base} · last error: {err[:120]}"
    if snap.get("job_active"):
        return f"{base} · generating {float(snap.get('progress') or 0):.0%}"
    return base + " · ready"


def refresh_status() -> bool:
    _set_status(_status_line())
    return True


def _ping_local_shim() -> Tuple[bool, str]:
    try:
        with urllib.request.urlopen("http://127.0.0.1:7860/internal/ping", timeout=2.0) as resp:
            raw = resp.read()
            ok = int(resp.status) == 200
            return ok, raw.decode("utf-8", errors="replace")
    except Exception as exc:
        return False, str(exc)


def connect_cloud() -> bool:
    settings = _read_settings_from_panel()
    _save_settings(settings)
    mode = _mode_from_index(int(settings["backend_index"]))
    credential = str(settings.get("credential") or "")

    try:
        backend = be.build_backend(mode, credential)
    except be.BackendError as exc:
        _set_status(f"Connect failed: {exc}")
        _show(str(exc), duration=3.5)
        return False

    shim.get_state().set_backend(backend)
    ok, msg = shim.start_shim()
    if not ok:
        _set_status(msg)
        _show(msg, duration=4.0)
        return False

    ping_ok, ping_body = _ping_local_shim()
    if not ping_ok:
        shim.stop_shim()
        detail = f"Shim started but ping failed: {ping_body}"
        _set_status(detail)
        _show(detail, duration=4.0)
        return False

    line = _status_line()
    _set_status(line)
    _show(f"Cloud Inference ready ({backend.describe()}). Keep SD connection at 127.0.0.1:7860.", duration=3.5)
    print(f"[{ADDON_ID}] Connected: {backend.describe()} ping={ping_body[:80]}")
    return True


def disconnect_cloud() -> bool:
    ok, msg = shim.stop_shim()
    _set_status(_status_line() if ok else msg)
    _show("Cloud Inference disconnected." if ok else msg, duration=2.5)
    return ok


def save_settings() -> bool:
    if not _save_settings():
        _show("Failed to save settings", duration=2.5)
        return False
    _show("Settings saved", duration=2.0)
    refresh_status()
    return True


def register() -> None:
    global _panel

    api = spz.get_api()
    settings = _load_settings()
    ix = int(settings.get("backend_index", 0))

    _panel = api.ui.create_panel(ADDON_ID, ADDON_TITLE)
    if not _panel:
        raise RuntimeError(
            f"[{ADDON_ID}] create_panel failed — refusing successful load so Unity tears down the ribbon shell"
        )

    _el["status"] = _panel.add_text_input("Status", _status_line())
    _el["backend"] = _panel.add_dropdown("Backend", _BACKEND_LABELS, ix)
    _el["credential"] = _panel.add_text_input(
        "Key / session URL",
        str(settings.get("credential") or ""),
    )
    _el["auto_connect"] = _panel.add_text_input(
        "Auto-connect (On/Off)",
        "On" if settings.get("auto_connect") else "Off",
    )

    _panel.add_button("Connect", "connect_cloud")
    _panel.add_button("Disconnect", "disconnect_cloud")
    _panel.add_button("Refresh Status", "refresh_status")
    _panel.add_button("Save Settings", "save_settings")

    print(f"[{ADDON_ID}] Registered")
    _show("Cloud Inference panel ready. Demo works offline; Colab/RunPod = paste Forge URL.", duration=3.5)

    if settings.get("auto_connect"):
        try:
            connect_cloud()
        except Exception as e:
            print(f"[{ADDON_ID}] auto_connect failed: {e}")


def unregister() -> None:
    global _panel
    try:
        shim.stop_shim()
    except Exception as e:
        print(f"[{ADDON_ID}] stop_shim on unregister: {e}")
    _panel = None
    print(f"[{ADDON_ID}] Unregistered")
