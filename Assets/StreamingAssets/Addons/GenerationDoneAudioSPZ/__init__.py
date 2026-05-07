"""
GenerationDoneAudioSPZ

Plays a user-configured audio file when Stable Diffusion generation
transitions from "running" to "finished".
"""

from __future__ import annotations

import ctypes
import json
import os
import subprocess
import sys
import threading
import time
from typing import Optional


addon_system_dir = os.path.join(os.path.dirname(__file__), "..", "..", "AddonSystem")
if os.path.exists(addon_system_dir):
    sys.path.insert(0, addon_system_dir)

try:
    import spz
except ImportError:
    print("[GenerationDoneAudioSPZ] Error: Could not import spz module")
    raise


ADDON_ID = "GenerationDoneAudioSPZ"
ADDON_TITLE = "Gen Complete Audio"

_panel = None
_el = {}

_watcher_thread: Optional[threading.Thread] = None
_watcher_stop = threading.Event()
_watcher_lock = threading.Lock()

_cached_is_generating: Optional[bool] = None
_last_alert_at = 0.0
_MCI_ALIAS = "spz_gen_done_audio"
_SETTINGS_PATH = os.path.join(os.path.dirname(__file__), "settings.json")
_enabled = True

_DEFAULT_SETTINGS = {
    "enabled": True,
    "poll_seconds": 0.60,
    "sound_path": "",
}


def _show(msg: str, duration: float = 2.0) -> None:
    try:
        spz.get_api().ui_chrome.show_status_text(msg, duration=duration)
    except Exception:
        print(f"[{ADDON_ID}] {msg}")


def _load_settings() -> dict:
    settings = dict(_DEFAULT_SETTINGS)
    try:
        if os.path.isfile(_SETTINGS_PATH):
            with open(_SETTINGS_PATH, "r", encoding="utf-8") as f:
                data = json.load(f)
            if isinstance(data, dict):
                settings.update(data)
    except Exception as e:
        print(f"[{ADDON_ID}] Failed to load settings: {e}")
    settings["enabled"] = bool(settings.get("enabled", True))
    try:
        settings["poll_seconds"] = max(0.2, float(settings.get("poll_seconds", 0.6)))
    except (TypeError, ValueError):
        settings["poll_seconds"] = 0.6
    settings["sound_path"] = str(settings.get("sound_path", "") or "")
    return settings


def _save_settings(settings: dict) -> bool:
    payload = {
        "enabled": bool(settings.get("enabled", True)),
        "poll_seconds": max(0.2, float(settings.get("poll_seconds", 0.6))),
        "sound_path": str(settings.get("sound_path", "") or ""),
    }
    try:
        with open(_SETTINGS_PATH, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2)
        return True
    except Exception as e:
        print(f"[{ADDON_ID}] Failed to save settings: {e}")
        return False


def _read_settings_from_panel() -> dict:
    return {
        "enabled": _panel_value_enabled(),
        "poll_seconds": max(0.2, _panel_value_float("poll_seconds", 0.6)),
        "sound_path": _resolve_sound_path(),
    }


def _pick_audio_file() -> Optional[str]:
    if os.name != "nt":
        return None

    # Try tkinter first (best UX if available in the Python runtime).
    try:
        import tkinter as tk
        from tkinter import filedialog

        root = tk.Tk()
        root.withdraw()
        root.attributes("-topmost", True)
        path = filedialog.askopenfilename(
            title="Select completion sound",
            filetypes=[
                ("Audio files", "*.wav *.mp3 *.m4a *.aac *.wma *.ogg *.flac"),
                ("All files", "*.*"),
            ],
        )
        root.destroy()
        if path:
            return path
    except Exception:
        pass

    # Fallback: PowerShell + Windows Forms dialog.
    try:
        ps_script = (
            "Add-Type -AssemblyName System.Windows.Forms; "
            "$dlg = New-Object System.Windows.Forms.OpenFileDialog; "
            "$dlg.Title = 'Select completion sound'; "
            "$dlg.Filter = 'Audio Files (*.wav;*.mp3;*.m4a;*.aac;*.wma;*.ogg;*.flac)|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.ogg;*.flac|All Files (*.*)|*.*'; "
            "$dlg.Multiselect = $false; "
            "if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { Write-Output $dlg.FileName }"
        )
        proc = subprocess.run(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps_script],
            capture_output=True,
            text=True,
            check=False,
        )
        picked = (proc.stdout or "").strip()
        return picked or None
    except Exception:
        return None


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


def _panel_value_float(key: str, default: float) -> float:
    txt = _panel_value_text(key, str(default))
    try:
        return float(txt)
    except (TypeError, ValueError):
        return default


def _panel_value_enabled() -> bool:
    return bool(_enabled)


def _resolve_sound_path() -> str:
    raw = _panel_value_text("sound_path", "")
    if not raw:
        return ""
    return os.path.expanduser(raw)


def _is_audio_file(path: str) -> bool:
    ext = os.path.splitext(path)[1].lower()
    return ext in {".wav", ".mp3", ".m4a", ".aac", ".wma", ".ogg", ".flac"}


def _play_with_mci(path: str) -> bool:
    if os.name != "nt":
        return False
    try:
        winmm = ctypes.windll.winmm
        winmm.mciSendStringW(f'close {_MCI_ALIAS}', None, 0, None)
        safe_path = path.replace('"', '\\"')
        open_cmd = f'open "{safe_path}" type mpegvideo alias {_MCI_ALIAS}'
        err = winmm.mciSendStringW(open_cmd, None, 0, None)
        if err != 0:
            return False
        err = winmm.mciSendStringW(f'play {_MCI_ALIAS} from 0', None, 0, None)
        return err == 0
    except Exception:
        return False


def _play_with_winsound(path: str) -> bool:
    if os.name != "nt" or not path.lower().endswith(".wav"):
        return False
    try:
        import winsound

        winsound.PlaySound(path, winsound.SND_FILENAME | winsound.SND_ASYNC | winsound.SND_NODEFAULT)
        return True
    except Exception:
        return False


def _play_with_powershell(path: str) -> bool:
    if os.name != "nt":
        return False
    try:
        escaped = path.replace("'", "''")
        script = (
            "Add-Type -AssemblyName presentationCore; "
            "$p = New-Object System.Windows.Media.MediaPlayer; "
            f"$p.Open([Uri]'{escaped}'); "
            "$p.Volume = 1.0; "
            "$p.Play(); "
            "Start-Sleep -Milliseconds 500"
        )
        subprocess.Popen(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        return True
    except Exception:
        return False


def _play_sound_now(from_test_button: bool = False) -> None:
    path = _resolve_sound_path()
    if not path:
        _show("Set an audio file path first.", duration=3.0)
        return
    if not os.path.isfile(path):
        _show("Audio file not found. Check the path.", duration=3.0)
        print(f"[{ADDON_ID}] Missing audio file: {path}")
        return
    if not _is_audio_file(path):
        _show("Unsupported audio extension. Use wav/mp3/m4a/aac/wma/ogg/flac.", duration=3.0)
        return

    ok = _play_with_winsound(path) or _play_with_mci(path) or _play_with_powershell(path)
    if ok:
        if from_test_button:
            _show("Test sound played.", duration=2.0)
        return

    _show("Unable to play the audio file on this machine.", duration=3.0)
    print(f"[{ADDON_ID}] Playback failed for: {path}")


def _refresh_enabled_state_field() -> None:
    if _panel is None:
        return
    eid = _el.get("enabled_state")
    if not eid:
        return
    try:
        _panel.set_value(eid, "On" if _enabled else "Off")
    except Exception:
        pass


def _set_enabled(value: bool, announce: bool = False) -> None:
    global _enabled
    _enabled = bool(value)
    _refresh_enabled_state_field()
    if announce:
        _show(f"Gen complete sound: {'On' if _enabled else 'Off'}", duration=2.0)
    _save_settings(_read_settings_from_panel())


def _poll_loop() -> None:
    global _cached_is_generating, _last_alert_at

    api = spz.get_api()
    while not _watcher_stop.is_set():
        interval = max(0.2, _panel_value_float("poll_seconds", 0.6))
        enabled = _panel_value_enabled()
        try:
            is_generating = bool(api.sd.is_generating())
        except Exception:
            is_generating = None

        if enabled and is_generating is not None:
            if _cached_is_generating is True and is_generating is False:
                now = time.time()
                if now - _last_alert_at > 0.9:
                    _last_alert_at = now
                    _play_sound_now(from_test_button=False)
            _cached_is_generating = is_generating
        elif is_generating is not None:
            _cached_is_generating = is_generating

        _watcher_stop.wait(interval)


def _ensure_watcher_running() -> None:
    global _watcher_thread
    with _watcher_lock:
        if _watcher_thread is not None and _watcher_thread.is_alive():
            return
        _watcher_stop.clear()
        _watcher_thread = threading.Thread(target=_poll_loop, name="SPZ-GenDoneAudio", daemon=True)
        _watcher_thread.start()


def test_sound() -> None:
    _play_sound_now(from_test_button=True)


def enable_sound_alerts() -> None:
    _set_enabled(True, announce=True)


def disable_sound_alerts() -> None:
    _set_enabled(False, announce=True)


def toggle_sound_alerts() -> None:
    _set_enabled(not _enabled, announce=True)


def browse_audio_file() -> None:
    if _panel is None:
        return
    picked = _pick_audio_file()
    if not picked:
        _show("No file selected.", duration=2.0)
        return
    _panel.set_value(_el["sound_path"], picked)
    settings = _read_settings_from_panel()
    _save_settings(settings)
    _show("Audio file selected and saved.", duration=2.0)


def apply_settings() -> None:
    if _panel is None:
        return
    poll = max(0.2, _panel_value_float("poll_seconds", 0.6))
    _panel.set_value(_el["poll_seconds"], f"{poll:.2f}")
    settings = _read_settings_from_panel()
    _save_settings(settings)
    _show("Gen complete audio settings applied.", duration=2.0)


def register() -> None:
    global _panel, _cached_is_generating, _enabled

    api = spz.get_api()
    settings = _load_settings()
    _enabled = bool(settings.get("enabled", True))
    _panel = api.ui.create_panel(ADDON_ID, ADDON_TITLE)
    if not _panel:
        print(f"[{ADDON_ID}] Failed to create panel")
        return

    poll_default = f"{float(settings.get('poll_seconds', 0.6)):.2f}"
    sound_default = str(settings.get("sound_path", "") or "")
    _el["enabled_state"] = _panel.add_text_input("Play on SD finish (buttons below)", "On" if _enabled else "Off")
    _el["poll_seconds"] = _panel.add_text_input("Poll interval seconds (0.2+)", poll_default)
    _el["sound_path"] = _panel.add_text_input("Audio file path (wav/mp3/...)", sound_default)

    _panel.add_button("Enable Sound Alert", "enable_sound_alerts")
    _panel.add_button("Disable Sound Alert", "disable_sound_alerts")
    _panel.add_button("Toggle On/Off", "toggle_sound_alerts")
    _panel.add_button("Apply Settings", "apply_settings")
    _panel.add_button("Browse Audio File...", "browse_audio_file")
    _panel.add_button("Test Sound", "test_sound")

    try:
        _cached_is_generating = bool(api.sd.is_generating())
    except Exception:
        _cached_is_generating = None

    _ensure_watcher_running()
    print(f"[{ADDON_ID}] Registered")
    _show("Gen Complete Audio loaded. Settings restored.", duration=3.5)


def unregister() -> None:
    global _panel, _watcher_thread
    try:
        if _panel is not None:
            _save_settings(_read_settings_from_panel())
    except Exception:
        pass
    _watcher_stop.set()
    with _watcher_lock:
        prev = _watcher_thread
        _watcher_thread = None
    if prev is not None and prev.is_alive():
        prev.join(timeout=1.0)
    _panel = None
    print(f"[{ADDON_ID}] Unregistered")
