"""
Multiview Manager — isolate view slots, per-view bookmarks, and local multiview presets.

Uses spz.view_cameras RPCs (get_povs / isolate / restore_povs / apply_slot_pov).
"""

from __future__ import annotations

import json
import os
import sys
from typing import Any, Dict, List, Optional

_root = os.path.dirname(os.path.abspath(__file__))
if _root not in sys.path:
    sys.path.insert(0, _root)

addon_system_dir = os.path.join(_root, "..", "..", "AddonSystem")
if os.path.exists(addon_system_dir):
    sys.path.insert(0, addon_system_dir)

try:
    import spz
except ImportError:
    print("[MultiviewManagerSPZ] Error: Could not import spz module")
    raise

ADDON_ID = "MultiviewManagerSPZ"
ADDON_TITLE = "Multiview"

MAX_SLOTS = 6
_panel = None
_el: Dict[str, Any] = {}
_pre_isolate_snapshot: Optional[Dict[str, Any]] = None

_SLOT_PATH = os.path.join(_root, "slot_bookmarks.json")
_PRESETS_PATH = os.path.join(_root, "presets.json")


def _clear_pre_isolate() -> None:
    global _pre_isolate_snapshot
    _pre_isolate_snapshot = None


def _api():
    return spz.get_api()


def _show(msg: str, duration: float = 2.5) -> None:
    try:
        _api().ui_chrome.show_status_text(msg, duration=duration)
    except Exception:
        print(f"[{ADDON_ID}] {msg}")


def _panel_value(key: str, default: Any = None) -> Any:
    if _panel is None:
        return default
    element = _el.get(key)
    if not element:
        return default
    try:
        val = _panel.get_value(element)
        return default if val is None else val
    except Exception:
        return default


def _load_json(path: str) -> dict:
    try:
        if os.path.isfile(path):
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            if isinstance(data, dict):
                return data
    except Exception as e:
        print(f"[{ADDON_ID}] Failed to load {path}: {e}")
    return {}


def _save_json(path: str, payload: dict) -> None:
    try:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2)
    except Exception as e:
        print(f"[{ADDON_ID}] Failed to save {path}: {e}")


def _view_state() -> dict:
    try:
        return _api().view_cameras.get_state() or {}
    except Exception:
        return {}


def _pov_snapshot() -> dict:
    try:
        snap = _api().view_cameras.get_povs() or {}
        if snap.get("success") is False and not snap.get("povs"):
            return {}
        return snap
    except Exception:
        return {}


def _slot_label(index: int) -> str:
    return f"View {index + 1}"


def _preset_names() -> List[str]:
    presets = _load_json(_PRESETS_PATH)
    names = sorted(presets.keys())
    return names if names else ["(none)"]


def _preset_dropdown_index() -> int:
    """Dropdown get_value returns an int index from AddonUI_MGR."""
    raw = _panel_value("preset_pick", 0)
    try:
        return max(0, int(raw))
    except (TypeError, ValueError):
        return 0


def _preset_name_from_panel() -> str:
    """Prefer typed preset name; fall back to dropdown selection by index."""
    name = str(_panel_value("preset_name", "") or "").strip()
    if name:
        return name
    names = _preset_names()
    if not names or names == ["(none)"]:
        return ""
    idx = _preset_dropdown_index()
    if 0 <= idx < len(names) and names[idx] != "(none)":
        return names[idx]
    return ""


def _refresh_preset_dropdown() -> None:
    if _panel is None or not _el.get("preset_pick"):
        return
    names = _preset_names()
    try:
        _panel.set_value(_el["preset_pick"], 0)
    except Exception:
        pass
    if names and names[0] != "(none)" and _el.get("preset_name"):
        try:
            _panel.set_value(_el["preset_name"], names[0])
        except Exception:
            pass


def refresh_status() -> None:
    state = _view_state()
    if not state.get("success", True) and "num_active" not in state:
        _show("Could not read multiview state")
        return
    num_active = int(state.get("num_active", state.get("count", 1)) or 1)
    cur = int(state.get("current_index", 0) or 0)
    if _panel is not None:
        if _el.get("cam_count"):
            try:
                _panel.set_value(_el["cam_count"], float(num_active))
            except Exception:
                pass
        if _el.get("isolate_pick"):
            try:
                max_ix = max(0, num_active - 1)
                _panel.set_value(_el["isolate_pick"], float(min(cur, max_ix) + 1))
            except Exception:
                pass
    _show(f"{num_active} active view(s) · current {_slot_label(cur)}")


def apply_camera_count() -> None:
    count = int(_panel_value("cam_count", 1) or 1)
    count = max(1, min(MAX_SLOTS, count))
    ok = _api().view_cameras.set_enabled_count(count)
    if ok:
        _show(f"Enabled {count} view camera(s)")
        refresh_status()
    else:
        _show("Failed to set camera count")


def _capture_pre_isolate_if_multiview() -> None:
    """Keep the first multiview snapshot so Show all can restore after multiple solos."""
    global _pre_isolate_snapshot
    if _pre_isolate_snapshot is not None:
        return
    snap = _pov_snapshot()
    num = int(snap.get("num_enabled", 0) or 0)
    if num > 1 and snap.get("povs"):
        _pre_isolate_snapshot = snap


def _isolate_index() -> int:
    raw = int(_panel_value("isolate_pick", 1) or 1)
    return max(0, min(MAX_SLOTS - 1, raw - 1))


def _isolate_view_at(index: int) -> None:
    global _pre_isolate_snapshot
    index = max(0, min(MAX_SLOTS - 1, int(index)))
    _capture_pre_isolate_if_multiview()
    ok = _api().view_cameras.isolate(index)
    if ok:
        _api().view_cameras.set_current(index)
        if _panel is not None and _el.get("isolate_pick"):
            try:
                _panel.set_value(_el["isolate_pick"], float(index + 1))
            except Exception:
                pass
        _show(f"Solo {_slot_label(index)}")
    else:
        _show(f"Could not isolate {_slot_label(index)}")


def isolate_selected_view() -> None:
    _isolate_view_at(_isolate_index())


def isolate_view_1() -> None:
    _isolate_view_at(0)


def isolate_view_2() -> None:
    _isolate_view_at(1)


def isolate_view_3() -> None:
    _isolate_view_at(2)


def isolate_view_4() -> None:
    _isolate_view_at(3)


def isolate_view_5() -> None:
    _isolate_view_at(4)


def isolate_view_6() -> None:
    _isolate_view_at(5)


def show_all_views() -> None:
    global _pre_isolate_snapshot
    povs = None
    if _pre_isolate_snapshot and _pre_isolate_snapshot.get("povs"):
        povs = _pre_isolate_snapshot["povs"]
    if not povs:
        _show("No multiview layout to restore — set up views first, then isolate")
        return
    ok = _api().view_cameras.restore_povs(povs)
    _pre_isolate_snapshot = None
    if ok:
        _show("Restored all views")
        refresh_status()
    else:
        _show("Failed to restore multiview layout")


def set_current_view() -> None:
    idx = _isolate_index()
    ok = _api().view_cameras.set_current(idx)
    _show(f"Current: {_slot_label(idx)}" if ok else "Could not set current view")


def _slot_index_from_callback_suffix(suffix: str) -> int:
    try:
        return max(0, min(MAX_SLOTS - 1, int(suffix) - 1))
    except (TypeError, ValueError):
        return 0


def save_slot_1() -> None:
    save_slot_n("1")


def save_slot_2() -> None:
    save_slot_n("2")


def save_slot_3() -> None:
    save_slot_n("3")


def save_slot_4() -> None:
    save_slot_n("4")


def save_slot_5() -> None:
    save_slot_n("5")


def save_slot_6() -> None:
    save_slot_n("6")


def recall_slot_1() -> None:
    recall_slot_n("1")


def recall_slot_2() -> None:
    recall_slot_n("2")


def recall_slot_3() -> None:
    recall_slot_n("3")


def recall_slot_4() -> None:
    recall_slot_n("4")


def recall_slot_5() -> None:
    recall_slot_n("5")


def recall_slot_6() -> None:
    recall_slot_n("6")


def save_slot_n(suffix: str) -> None:
    idx = _slot_index_from_callback_suffix(suffix)
    snap = _pov_snapshot()
    povs = snap.get("povs") or []
    if idx >= len(povs):
        _show(f"No data for {_slot_label(idx)}")
        return
    bookmarks = _load_json(_SLOT_PATH)
    bookmarks[str(idx)] = povs[idx]
    _save_json(_SLOT_PATH, bookmarks)
    _show(f"Saved bookmark · {_slot_label(idx)}")


def recall_slot_n(suffix: str) -> None:
    idx = _slot_index_from_callback_suffix(suffix)
    bookmarks = _load_json(_SLOT_PATH)
    pov = bookmarks.get(str(idx))
    if not pov:
        _show(f"No bookmark for {_slot_label(idx)}")
        return
    ok = _api().view_cameras.apply_slot_pov(idx, pov)
    _show(f"Recalled {_slot_label(idx)}" if ok else f"Failed to recall {_slot_label(idx)}")


def save_global_preset() -> None:
    name = str(_panel_value("preset_name", "") or "").strip()
    if not name:
        _show("Enter a preset name first")
        return
    snap = _pov_snapshot()
    if not snap.get("povs"):
        _show("Could not capture multiview layout")
        return
    presets = _load_json(_PRESETS_PATH)
    presets[name] = {
        "povs": snap["povs"],
        "num_enabled": snap.get("num_enabled"),
        "current_index": snap.get("current_index"),
    }
    _save_json(_PRESETS_PATH, presets)
    if _panel is not None and _el.get("preset_name"):
        try:
            _panel.set_value(_el["preset_name"], name)
        except Exception:
            pass
    _refresh_preset_dropdown()
    _show(f"Saved preset '{name}'")


def load_global_preset() -> None:
    pick = _preset_name_from_panel()
    if not pick or pick == "(none)":
        _show("Select a preset to load")
        return
    presets = _load_json(_PRESETS_PATH)
    entry = presets.get(pick)
    if not entry or not entry.get("povs"):
        _show(f"Preset '{pick}' not found")
        return
    ok = _api().view_cameras.restore_povs(entry["povs"])
    if ok and entry.get("current_index") is not None:
        _api().view_cameras.set_current(int(entry["current_index"]))
    _show(f"Loaded preset '{pick}'" if ok else f"Failed to load '{pick}'")
    refresh_status()


def delete_global_preset() -> None:
    pick = _preset_name_from_panel()
    if not pick or pick == "(none)":
        _show("Select a preset to delete")
        return
    presets = _load_json(_PRESETS_PATH)
    if pick not in presets:
        _show(f"Preset '{pick}' not found")
        return
    del presets[pick]
    _save_json(_PRESETS_PATH, presets)
    _refresh_preset_dropdown()
    _show(f"Deleted preset '{pick}'")


def register() -> None:
    global _panel, _el
    api = _api()
    _panel = api.ui.create_panel(ADDON_ID, ADDON_TITLE)
    if not _panel:
        raise RuntimeError(
            f"{ADDON_ID}: create_panel failed — refusing load so Unity tears down the ribbon shell"
        )

    _el = {}
    state = _view_state()
    max_count = int(state.get("max_count", MAX_SLOTS) or MAX_SLOTS)
    num_active = int(state.get("num_active", 1) or 1)
    cur_ix = int(state.get("current_index", 0) or 0)

    _el["cam_count"] = _panel.add_slider("Active cameras", 1, max_count, num_active)
    _panel.add_button("Apply count", "apply_camera_count")
    _panel.add_button("Refresh", "refresh_status")

    iso_fold = _panel.add_foldout("Isolate view (solo)", start_open=True)
    if iso_fold:
        iso = type(_panel)(_panel._client, iso_fold, ADDON_ID)
        _el["isolate_pick"] = iso.add_slider("View slot", 1, max_count, max(1, min(max_count, cur_ix + 1)))
        iso.add_button("Solo selected view", "isolate_selected_view")
        iso.add_button("View 1", "isolate_view_1")
        iso.add_button("View 2", "isolate_view_2")
        iso.add_button("View 3", "isolate_view_3")
        iso.add_button("View 4", "isolate_view_4")
        iso.add_button("View 5", "isolate_view_5")
        iso.add_button("View 6", "isolate_view_6")
        iso.add_button("Show all views", "show_all_views")
        iso.add_button("Set as current (orbit)", "set_current_view")
    else:
        _el["isolate_pick"] = _panel.add_slider("View slot", 1, max_count, max(1, min(max_count, cur_ix + 1)))
        _panel.add_button("Solo selected view", "isolate_selected_view")
        _panel.add_button("Show all views", "show_all_views")

    slot_fold = _panel.add_foldout("Per-view bookmarks", start_open=False)
    if slot_fold:
        slots = type(_panel)(_panel._client, slot_fold, ADDON_ID)
        slots.add_button("Save View 1", "save_slot_1")
        slots.add_button("Recall View 1", "recall_slot_1")
        slots.add_button("Save View 2", "save_slot_2")
        slots.add_button("Recall View 2", "recall_slot_2")
        slots.add_button("Save View 3", "save_slot_3")
        slots.add_button("Recall View 3", "recall_slot_3")
        slots.add_button("Save View 4", "save_slot_4")
        slots.add_button("Recall View 4", "recall_slot_4")
        slots.add_button("Save View 5", "save_slot_5")
        slots.add_button("Recall View 5", "recall_slot_5")
        slots.add_button("Save View 6", "save_slot_6")
        slots.add_button("Recall View 6", "recall_slot_6")
    else:
        _panel.add_button("Save View 1", "save_slot_1")
        _panel.add_button("Recall View 1", "recall_slot_1")

    preset_fold = _panel.add_foldout("Global presets", start_open=False)
    if preset_fold:
        presets = type(_panel)(_panel._client, preset_fold, ADDON_ID)
        _el["preset_name"] = presets.add_text_input("Preset name", "")
        _el["preset_pick"] = presets.add_dropdown("Saved presets", _preset_names(), 0)
        presets.add_button("Save preset", "save_global_preset")
        presets.add_button("Load preset", "load_global_preset")
        presets.add_button("Delete preset", "delete_global_preset")
    else:
        _el["preset_name"] = _panel.add_text_input("Preset name", "")
        _el["preset_pick"] = _panel.add_dropdown("Saved presets", _preset_names(), 0)
        _panel.add_button("Save preset", "save_global_preset")
        _panel.add_button("Load preset", "load_global_preset")

    print(f"[{ADDON_ID}] registered")


def unregister() -> None:
    global _panel, _el, _pre_isolate_snapsho