"""
CloudInferenceSPZ

End-user cloud inference for StableProjectorz: paste a key/session URL, Connect,
and keep the SD connection panel on 127.0.0.1:7860. A local Forge-compatible
shim serves /internal/ping, /sdapi/v1/*, and soft ControlNet stubs.

Backends:
  Demo         — solid PNG (validates SPZ without GPU)
  Remote/Colab — proxy to a Forge tunnel URL (Colab, RunPod, etc.)
  fal          — thick translator: paste API key → Flux via local Forge shim
"""

from __future__ import annotations

import json
import os
import sys
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
    "Demo — try without a GPU",
    "Remote Forge / Colab",
    "fal — paste API key",
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


def _panel_value_bool(key: str, default: bool = False) -> bool:
    if _panel is None:
        return default
    element = _el.get(key)
    if not element:
        return default
    try:
        raw = _panel.get_value(element)
    except Exception:
        return default
    if isinstance(raw, bool):
        return raw
    if raw is None:
        return default
    if isinstance(raw, (int, float)):
        return int(raw) != 0
    return str(raw).strip().lower() in ("1", "true", "on", "yes")


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
        "auto_connect": _panel_value_bool("auto_connect", False),
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
        return "Off — press Connect. Keep SD SERV on 127.0.0.1:7860."
    err = snap.get("last_error") or ""
    backend = snap.get("backend") or "backend"
    listen = snap.get("listen") or "127.0.0.1:7860"
    if err:
        return f"Error · {backend} · {err[:100]}"
    if snap.get("job_active"):
        return f"On · {backend} · {listen} · generating {float(snap.get('progress') or 0):.0%}"
    return f"On · {backend} · {listen} · ready"


def refresh_status() -> bool:
    _set_status(_status_line())
    return True


def _ping_local_shim() -> Tuple[bool, str]:
    """Confirm the listener is CloudInferenceSPZ on the actual bind, not an unrelated Forge."""
    return shim.ping_listen()


def connect_cloud() -> bool:
    settings = _read_settings_from_panel()
    _save_settings(settings)
    mode = _mode_from_index(int(settings["backend_index"]))
    credential = str(settings.get("credential") or "")

    # Guard: remote URL must not target the active local shim listen endpoint.
    if mode == "remote_forge":
        listen = shim.listen_endpoint()  # e.g. 127.0.0.1:7860
        cred_l = credential.strip().lower().rstrip("/")
        if "://" not in cred_l:
            cred_l = "http://" + cred_l
        if listen.lower() in cred_l or cred_l.endswith(listen.lower()):
            msg = (
                f"Remote URL cannot target the local shim ({listen}). "
                "Paste a Colab/RunPod public Forge URL, or use Demo."
            )
            _set_status(f"Connect failed: {msg}")
            _show(msg, duration=4.0)
            return False

    try:
        backend = be.build_backend(mode, credential)
    except be.BackendError as exc:
        _set_status(f"Connect failed: {exc}")
        _show(str(exc), duration=3.5)
        return False

    if mode == "remote_forge":
        ok_p, msg_p = backend.probe()
        if not ok_p:
            detail = f"Remote Forge unreachable: {msg_p}"
            _set_status(f"Connect failed: {detail}")
            _show(detail, duration=4.5)
            return False

    if mode == "fal":
        ok_p, msg_p = backend.probe()
        if not ok_p:
            detail = f"fal key check failed: {msg_p}"
            _set_status(f"Connect failed: {detail}")
            _show(detail, duration=4.5)
            return False

    # Bind listener first; only claim the backend after ping proves OUR shim is up.
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

    shim.get_state().set_backend(backend)
    line = _status_line()
    _set_status(line)
    _show(f"Cloud Inference ready ({backend.describe()}). Keep SD connection at 127.0.0.1:7860.", duration=3.5)
    print(f"[{ADDON_ID}] Connected: {backend.describe()} ping={ping_body[:80]}")
    return True


def disconnect_cloud() -> bool:
    ok, msg = shim.stop_shim()
    # Clear SERV Cloud emblem immediately — do not wait for Unity ping timeout.
    try:
        spz.get_api().sd.mark_sd_disconnected()
    except Exception as e:
        print(f"[{ADDON_ID}] mark_sd_disconnected: {e}")
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


def on_auto_connect() -> bool:
    """Toggle callback: persist without a separate Save click."""
    return save_settings()


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

    # Scan → configure → act → rare. Status is live copy, not a setting.
    _el["status"] = _panel.add_text_input("Status", _status_line())
    _el["backend"] = _panel.add_dropdown("Backend", _BACKEND_LABELS, ix)
    _el["credential"] = _panel.add_text_input(
        "API key / Remote URL",
        str(settings.get("credential") or ""),
    )
    _panel.add_button("Connect", "connect_cloud")
    _panel.add_button("Disconnect", "disconnect_cloud")
    _el["auto_connect"] = _panel.add_toggle(
        "Connect when this add-on loads",
        bool(settings.get("auto_connect")),
        "on_auto_connect",
    )

    fold_id = _panel.add_foldout("More", start_open=False)
    if fold_id:
        more = type(_panel)(_panel._client, fold_id, ADDON_ID)
        more.add_button("Refresh status", "refresh_status")
        more.add_button("Save settings", "save_settings")
    else:
        _panel.add_button("Refresh status", "refresh_status")
        _panel.add_button("Save settings", "save_settings")

    print(f"[{ADDON_ID}] Registered")
    _show("Cloud Inference ready. Demo offline · Forge URL · or fal API key.", duration=3.5)

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
    try:
        spz.get_api().sd.mark_sd_disconnected()
    except Exception as e:
        print(f"[{ADDON_ID}] mark_sd_disconnected on unregister: {e}")
    _el.clear()
    _panel = None
    print(f"[{ADDON_ID}] Unregistered")
