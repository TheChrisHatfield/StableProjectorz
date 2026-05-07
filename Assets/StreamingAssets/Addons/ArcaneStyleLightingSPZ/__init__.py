"""
Arcane Style Lighting (StableProjectorz edition)

Blender add-ons can create real light objects; StableProjectorz add-ons currently expose
camera + skybox + SD workflow controls over JSON-RPC. This add-on emulates an "Arcane-like"
look by:
- framing the camera around current selection,
- applying a cinematic sky gradient,
- and allowing quick intensity + FOV tuning from add-on UI widgets.
"""

import os
import sys
from math import sqrt


ADDON_ID = "ArcaneStyleLightingSPZ"
ADDON_TITLE = "Arcane Style Look"


# Add AddonSystem to import path (same pattern as bundled add-ons).
addon_system_dir = os.path.join(os.path.dirname(__file__), "..", "..", "AddonSystem")
if os.path.exists(addon_system_dir):
    sys.path.insert(0, addon_system_dir)

try:
    import spz
except ImportError:
    print(f"[{ADDON_ID}] Error: Could not import spz module")
    raise


_panel = None
_el = {}
_captured_state = None

_STYLE_PRESETS = [
    "Arcane",
    "Soft Studio",
    "Noir",
    "Warm Key / Cool Rim",
]

_STYLE_PROMPT_HINTS = {
    "Arcane": "arcane style lighting, cinematic key light, cool rim light, volumetric glow",
    "Soft Studio": "soft cinematic studio lighting, gentle fill, subtle rim light, filmic contrast",
    "Noir": "dramatic noir lighting, high contrast shadows, moody cinematic light",
    "Warm Key / Cool Rim": "warm key light, cool blue rim light, cinematic portrait lighting",
}


def _clamp01(v):
    return max(0.0, min(1.0, float(v)))


def _safe_get_value(element_id, fallback):
    if _panel is None or not element_id:
        return fallback
    try:
        v = _panel.get_value(element_id)
        return fallback if v is None else v
    except Exception:
        return fallback


def _show(msg, duration=2.5):
    try:
        spz.get_api().ui_chrome.show_status_text(msg, duration=duration)
    except Exception:
        print(f"[{ADDON_ID}] {msg}")


def _selected_style_name():
    idx_raw = _safe_get_value(_el.get("preset"), 0)
    try:
        idx = int(idx_raw)
    except Exception:
        idx = 0
    idx = max(0, min(len(_STYLE_PRESETS) - 1, idx))
    return _STYLE_PRESETS[idx]


def _selection_bounds_or_none(api):
    b = api.scene.get_selected_meshes_bounds()
    if not b:
        return None
    center = b.get("center") or {}
    size = b.get("size") or {}
    if not all(k in center for k in ("x", "y", "z")):
        return None
    if not all(k in size for k in ("x", "y", "z")):
        return None
    return b


def _frame_camera_for_selection(api, distance_multiplier=2.2, fov=40.0):
    b = _selection_bounds_or_none(api)
    if not b:
        return False

    center = b["center"]
    size = b["size"]
    diag = sqrt(size["x"] * size["x"] + size["y"] * size["y"] + size["z"] * size["z"])
    base = max(1.0, diag)
    dist = base * max(0.5, float(distance_multiplier))

    # Arcane-ish composition: front-right and slightly above.
    cam_x = center["x"] + dist * 0.95
    cam_y = center["y"] + dist * 0.38
    cam_z = center["z"] - dist * 0.90

    api.cameras.set_pos(0, cam_x, cam_y, cam_z)
    api.cameras.set_fov(0, float(fov))
    return True


def capture_current_look():
    global _captured_state
    api = spz.get_api()
    top = api.background.get_skybox_top_color()
    bottom = api.background.get_skybox_bottom_color()
    cam_pos = api.cameras.get_pos(0)
    cam_rot = api.cameras.get_rot(0)
    cam_fov = api.cameras.get_fov(0)
    if not top or not bottom or not cam_pos or not cam_rot:
        _show("Could not capture current look. Ensure scene and camera are ready.", duration=3.0)
        return
    _captured_state = {
        "top": top,
        "bottom": bottom,
        "cam_pos": cam_pos,
        "cam_rot": cam_rot,
        "cam_fov": 50.0 if cam_fov is None else float(cam_fov),
    }
    _show("Captured current look.", duration=2.0)


def restore_captured_look():
    if not _captured_state:
        _show("No captured look yet. Click 'Capture Current Look' first.", duration=3.0)
        return
    api = spz.get_api()
    t = _captured_state["top"]
    b = _captured_state["bottom"]
    p = _captured_state["cam_pos"]
    r = _captured_state["cam_rot"]
    f = _captured_state["cam_fov"]

    api.background.set_skybox_color(True, t["r"], t["g"], t["b"], t.get("a", 1.0))
    api.background.set_skybox_color(False, b["r"], b["g"], b["b"], b.get("a", 1.0))
    api.cameras.set_pos(0, p["x"], p["y"], p["z"])
    api.cameras.set_rot(0, r["x"], r["y"], r["z"], r["w"])
    api.cameras.set_fov(0, f)
    _show("Restored captured look.", duration=2.0)


def frame_camera_to_selection():
    api = spz.get_api()
    dist_mult = float(_safe_get_value(_el.get("distance"), 2.2))
    fov = float(_safe_get_value(_el.get("fov"), 40.0))
    ok = _frame_camera_for_selection(api, dist_mult, fov)
    if ok:
        _show("Camera framed to selection.", duration=2.0)
    else:
        _show("Select one or more meshes to frame the camera.", duration=3.0)


def _apply_style(style_name):
    api = spz.get_api()

    key = float(_safe_get_value(_el.get("key"), 1.00))
    fill = float(_safe_get_value(_el.get("fill"), 0.55))
    rim = float(_safe_get_value(_el.get("rim"), 0.90))
    fov = float(_safe_get_value(_el.get("fov"), 40.0))
    dist_mult = float(_safe_get_value(_el.get("distance"), 2.2))

    if style_name == "Soft Studio":
        top = {
            "r": _clamp01(0.35 + 0.18 * fill),
            "g": _clamp01(0.34 + 0.20 * fill),
            "b": _clamp01(0.45 + 0.18 * key),
        }
        bot = {
            "r": _clamp01(0.09 + 0.18 * key),
            "g": _clamp01(0.09 + 0.18 * key),
            "b": _clamp01(0.13 + 0.12 * rim),
        }
    elif style_name == "Noir":
        top = {
            "r": _clamp01(0.12 + 0.10 * fill),
            "g": _clamp01(0.12 + 0.10 * fill),
            "b": _clamp01(0.16 + 0.08 * key),
        }
        bot = {
            "r": _clamp01(0.01 + 0.06 * key),
            "g": _clamp01(0.01 + 0.06 * key),
            "b": _clamp01(0.02 + 0.08 * rim),
        }
    elif style_name == "Warm Key / Cool Rim":
        top = {
            "r": _clamp01(0.38 + 0.24 * key),
            "g": _clamp01(0.29 + 0.16 * key),
            "b": _clamp01(0.26 + 0.12 * fill),
        }
        bot = {
            "r": _clamp01(0.05 + 0.08 * fill),
            "g": _clamp01(0.07 + 0.10 * fill),
            "b": _clamp01(0.22 + 0.24 * rim),
        }
    else:
        # Arcane-ish: cool-violet shadows + warm key lift.
        top = {
            "r": _clamp01(0.24 + 0.22 * fill),
            "g": _clamp01(0.20 + 0.18 * fill),
            "b": _clamp01(0.50 + 0.22 * key),
        }
        bot = {
            "r": _clamp01(0.05 + 0.18 * key),
            "g": _clamp01(0.04 + 0.14 * key),
            "b": _clamp01(0.17 + 0.20 * rim),
        }

    api.background.set_skybox_color(True, top["r"], top["g"], top["b"], 1.0)
    api.background.set_skybox_color(False, bot["r"], bot["g"], bot["b"], 1.0)

    framed = _frame_camera_for_selection(api, dist_mult, fov)
    if framed:
        _show(f"Applied {style_name} look and reframed camera.", duration=2.5)
    else:
        _show(f"Applied {style_name} sky look. Select meshes to auto-frame camera.", duration=3.0)


def apply_arcane_look():
    _apply_style("Arcane")


def apply_soft_studio_look():
    _apply_style("Soft Studio")


def apply_selected_preset():
    _apply_style(_selected_style_name())


def apply_lighting_prompt():
    api = spz.get_api()
    style_name = _selected_style_name()
    hint = _STYLE_PROMPT_HINTS.get(style_name, "")
    if not hint:
        _show("No prompt hint found for this style.", duration=2.5)
        return
    current = api.sd.get_positive_prompt() or ""
    if hint.lower() in current.lower():
        _show("Prompt already contains this lighting hint.", duration=2.5)
        return
    if current.strip():
        updated = current.strip() + ", " + hint
    else:
        updated = hint
    ok = api.sd.set_positive_prompt(updated)
    if ok:
        _show(f"Applied {style_name} prompt hint.", duration=2.2)
    else:
        _show("Failed to set prompt hint.", duration=2.8)


def apply_selected_preset_and_prompt():
    apply_selected_preset()
    apply_lighting_prompt()


def register():
    global _panel, _el

    api = spz.get_api()
    _panel = api.ui.create_panel(ADDON_ID, ADDON_TITLE)
    if not _panel:
        print(f"[{ADDON_ID}] Failed to create panel")
        return

    # "Lighting" controls (mapped to SPZ sky + framing parameters).
    _el["preset"] = _panel.add_dropdown("Preset", _STYLE_PRESETS, 0)
    _el["key"] = _panel.add_slider("Key Intensity", 0.0, 2.0, 1.0)
    _el["fill"] = _panel.add_slider("Fill Intensity", 0.0, 2.0, 0.55)
    _el["rim"] = _panel.add_slider("Rim Intensity", 0.0, 2.0, 0.9)
    _el["distance"] = _panel.add_slider("Camera Distance", 0.6, 4.0, 2.2)
    _el["fov"] = _panel.add_slider("Camera FOV", 20.0, 80.0, 40.0)

    _panel.add_button("Capture Current Look", "capture_current_look")
    _panel.add_button("Apply Arcane Look", "apply_arcane_look")
    _panel.add_button("Apply Soft Studio Look", "apply_soft_studio_look")
    _panel.add_button("Apply Selected Preset", "apply_selected_preset")
    _panel.add_button("Apply Selected Preset + Prompt", "apply_selected_preset_and_prompt")
    _panel.add_button("Apply Lighting Prompt Hint", "apply_lighting_prompt")
    _panel.add_button("Frame Camera To Selection", "frame_camera_to_selection")
    _panel.add_button("Restore Captured Look", "restore_captured_look")

    print(f"[{ADDON_ID}] Registered")
    _show("Arcane Style Look add-on loaded.", duration=2.0)


def unregister():
    print(f"[{ADDON_ID}] Unregistered")


if __name__ == "__main__":
    register()
