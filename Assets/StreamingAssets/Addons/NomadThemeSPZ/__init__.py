"""Nomad-inspired semantic UI palette for StableProjectorz add-on surfaces."""

from __future__ import annotations

import os
import sys
from typing import Any, Dict, Optional, Tuple


_addon_system = os.path.normpath(
    os.path.join(os.path.dirname(__file__), "..", "..", "AddonSystem")
)
if os.path.isdir(_addon_system) and _addon_system not in sys.path:
    sys.path.insert(0, _addon_system)

try:
    import spz
except ImportError:
    print("[NomadThemeSPZ] Error: Could not import spz module")
    raise


ADDON_ID = "NomadThemeSPZ"
ADDON_TITLE = "Nomad Theme"
THEME_ID = "nomad-inspired"
THEME_LABEL = "Nomad inspired"

# Pro-Studio Monolith palette from the supplied Nomad UI replication design.
TOKENS: Dict[str, Any] = {
    "panel_bg": "#1E1F23F2",
    "control_bg": "#292A2EFF",
    "field_bg": "#121317FF",
    "accent": "#F2CA50FF",
    "text_primary": "#E3E2E7FF",
    "text_muted": "#D0C5AFFF",
    "handle": "#C8C5CBFF",
    "success": "#7BC96FFF",
    "danger": "#FFB4ABFF",
    "border": "#99907C66",
    "tab_active": "#343539FF",
    "selection": "#F2CA5033",
    "font_scale": 0.84,
    "spacing_scale": 0.94,
    "corner_radius": 5,
    "icon_tint": "#D0C5AFFF",
    "panel_width": 220,
    "panel_alpha": 0.92,
    "ribbon_icon_only": 1,
}

# CommandRibbon strip glyphs (icon pack) — tab name substring → StudioLineIcon.
# More specific matches first; avoid bare "Art"/"BG" (would rematch Art BG).
_STRIP_LINE_ICONS: Tuple[Tuple[str, str], ...] = (
    ("Paint", "Brush"),
    ("art bg", "Layers"),
    ("Art BG", "Layers"),
    ("art list", "Image"),
    ("Control", "Grid"),
    ("CTRL", "Grid"),
    ("controlnet", "Grid"),
    ("Mesh", "Mesh"),
    ("3D", "Mesh"),
    ("Obj", "Mesh"),
    ("Nomad", "Settings"),
)

# Charcoal skybox from panel_bg / field_bg (RGB only; alpha 1).
_SKYBOX_TOP = (0x1E / 255.0, 0x1F / 255.0, 0x23 / 255.0, 1.0)
_SKYBOX_BOTTOM = (0x12 / 255.0, 0x13 / 255.0, 0x17 / 255.0, 1.0)
# When no pre-Apply capture exists, restore to clear gradient (SPZ “no solid BG” default).
_SKYBOX_CLEAR = (0.0, 0.0, 0.0, 0.0)

_api: Optional[Any] = None
_panel: Optional[Any] = None
_font_slider_id: Optional[str] = None
_spacing_slider_id: Optional[str] = None
_skybox_capture: Optional[Tuple[Dict[str, float], Dict[str, float]]] = None


def _require_success(result: Any, operation: str) -> Dict[str, Any]:
    if isinstance(result, dict) and result.get("success") is True:
        return result
    error = result.get("error") if isinstance(result, dict) else repr(result)
    raise RuntimeError(f"{operation} failed: {error or 'unknown error'}")


def _get_api() -> Any:
    global _api
    if _api is None:
        _api = spz.get_api()
    return _api


def _register_preset(api: Any) -> None:
    _require_success(
        api.ui.register_theme(
            THEME_ID,
            TOKENS,
            label=THEME_LABEL,
            owner=ADDON_ID,
        ),
        "register_theme",
    )


def _best_effort_cleanup(api: Any) -> None:
    """Avoid leaving an active/orphan preset after a partial register failure."""
    try:
        current = api.ui.get_theme()
        if isinstance(current, dict) and current.get("theme_id") == THEME_ID:
            api.ui.reset_theme()
    except Exception:
        pass
    try:
        api.ui.unregister_theme(THEME_ID)
    except Exception:
        pass


def _rgba_dict(color: Any) -> Optional[Dict[str, float]]:
    if not isinstance(color, dict):
        return None
    return {
        "r": float(color.get("r", 0.0)),
        "g": float(color.get("g", 0.0)),
        "b": float(color.get("b", 0.0)),
        "a": float(color.get("a", 1.0)),
    }


def _capture_skybox_if_needed(api: Any) -> None:
    global _skybox_capture
    if _skybox_capture is not None:
        return
    top = _rgba_dict(api.background.get_skybox_top_color())
    bottom = _rgba_dict(api.background.get_skybox_bottom_color())
    if top is None or bottom is None:
        top = {"r": _SKYBOX_CLEAR[0], "g": _SKYBOX_CLEAR[1], "b": _SKYBOX_CLEAR[2], "a": _SKYBOX_CLEAR[3]}
        bottom = dict(top)
    _skybox_capture = (top, bottom)


def _set_skybox(api: Any, top: Tuple[float, float, float, float], bottom: Tuple[float, float, float, float]) -> None:
    ok_top = api.background.set_skybox_color(True, top[0], top[1], top[2], top[3])
    ok_bot = api.background.set_skybox_color(False, bottom[0], bottom[1], bottom[2], bottom[3])
    if not ok_top or not ok_bot:
        print(f"[{ADDON_ID}] Warning: skybox compose partially failed (top={ok_top}, bottom={ok_bot})")


def _set_skybox_dict(api: Any, top: Dict[str, float], bottom: Dict[str, float]) -> None:
    _set_skybox(
        api,
        (top["r"], top["g"], top["b"], top["a"]),
        (bottom["r"], bottom["g"], bottom["b"], bottom["a"]),
    )


def _compose_nomad_skybox(api: Any) -> None:
    _capture_skybox_if_needed(api)
    _set_skybox(api, _SKYBOX_TOP, _SKYBOX_BOTTOM)


def _restore_skybox(api: Any) -> None:
    global _skybox_capture
    if _skybox_capture is not None:
        top, bottom = _skybox_capture
        _set_skybox_dict(api, top, bottom)
        _skybox_capture = None
        return
    _set_skybox(api, _SKYBOX_CLEAR, _SKYBOX_CLEAR)


def _read_slider(panel: Any, element_id: Optional[str], default: float) -> float:
    if not element_id:
        return default
    value = panel.get_value(element_id)
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _compose_nomad_strip_icons(api: Any) -> None:
    """Assign StudioLineIcon glyphs on CommandRibbon strip tabs (best-effort; no hard fail)."""
    set_icon = getattr(api.ui, "set_line_icon", None)
    if not callable(set_icon):
        print(f"[{ADDON_ID}] Warning: set_line_icon unavailable — strip icons skipped")
        return
    ok = 0
    for tab, icon in _STRIP_LINE_ICONS:
        try:
            result = set_icon(tab, icon)
            if isinstance(result, dict) and result.get("success") is True:
                ok += 1
            else:
                err = result.get("error") if isinstance(result, dict) else repr(result)
                # Missing tabs (e.g. BG not loaded) are expected — keep quiet unless unexpected.
                if err and "No strip tab matching" not in str(err):
                    print(f"[{ADDON_ID}] set_line_icon({tab!r},{icon!r}): {err}")
        except Exception as e:
            print(f"[{ADDON_ID}] set_line_icon({tab!r},{icon!r}) raised: {e}")
    print(f"[{ADDON_ID}] Strip line icons applied ({ok}/{len(_STRIP_LINE_ICONS)} matches)")


def apply_nomad_palette() -> None:
    """Apply registered palette + scales via theme API, then strip icons. Keeps SPZ skybox/background."""
    api = _get_api()
    tokens = dict(TOKENS)
    if _panel is not None:
        tokens["font_scale"] = _read_slider(_panel, _font_slider_id, float(TOKENS["font_scale"]))
        tokens["spacing_scale"] = _read_slider(_panel, _spacing_slider_id, float(TOKENS["spacing_scale"]))
    _require_success(
        api.ui.register_theme(THEME_ID, tokens, label=THEME_LABEL, owner=ADDON_ID),
        "register_theme",
    )
    try:
        _require_success(api.ui.apply_theme(THEME_ID), "apply_theme")
    except Exception:
        _best_effort_cleanup(api)
        raise
    # SPZ viewport/skybox gradient stays — Nomad is chrome only.
    _compose_nomad_strip_icons(api)
    print(f"[{ADDON_ID}] Applied '{THEME_ID}' via apply_theme + strip icons (SPZ skybox kept)")


def restore_stableprojectorz_palette() -> None:
    """Restore builtin theme tokens and pre-Nomad skybox."""
    api = _get_api()
    _require_success(api.ui.reset_theme(), "reset_theme")
    _restore_skybox(api)
    print(f"[{ADDON_ID}] Restored StableProjectorz default palette + skybox")


def apply_nomad_scales() -> None:
    """Patch font_scale / spacing_scale while Nomad is active (fail closed otherwise)."""
    api = _get_api()
    current = _require_success(api.ui.get_theme(), "get_theme")
    if current.get("theme_id") != THEME_ID:
        raise RuntimeError(
            f"[{ADDON_ID}] apply_nomad_scales refused — active theme is "
            f"{current.get('theme_id')!r}, expected {THEME_ID!r}. Apply Nomad Palette first."
        )
    if _panel is None:
        raise RuntimeError(f"[{ADDON_ID}] apply_nomad_scales refused — panel missing")
    font_scale = _read_slider(_panel, _font_slider_id, float(TOKENS["font_scale"]))
    spacing_scale = _read_slider(_panel, _spacing_slider_id, float(TOKENS["spacing_scale"]))
    _require_success(
        api.ui.apply_theme(
            THEME_ID,
            tokens={"font_scale": font_scale, "spacing_scale": spacing_scale},
            mode="patch",
        ),
        "apply_theme patch scales",
    )
    print(f"[{ADDON_ID}] Patched scales font={font_scale:.3f} spacing={spacing_scale:.3f}")


def refresh_nomad_theme_status() -> None:
    """Log active theme id and bound surface count (honesty check; no fake success)."""
    api = _get_api()
    theme = _require_success(api.ui.get_theme(), "get_theme")
    catalog = _require_success(api.ui.list_themes(), "list_themes")
    surfaces = theme.get("surfaces") or []
    bound = sum(1 for s in surfaces if isinstance(s, dict) and s.get("bound") is True)
    print(
        f"[{ADDON_ID}] status theme_id={theme.get('theme_id')!r} "
        f"bound_surfaces={bound}/{len(surfaces)} "
        f"registered={catalog.get('registered_count')} "
        f"active_catalog={catalog.get('active_theme_id')!r}"
    )


def register() -> None:
    """Register the preset and expose apply/restore/scale/status controls (does not auto-apply)."""
    global _panel, _font_slider_id, _spacing_slider_id

    api = _get_api()
    try:
        _register_preset(api)
        _panel = api.ui.create_panel(ADDON_ID, ADDON_TITLE)
    except Exception:
        _panel = None
        _best_effort_cleanup(api)
        raise
    if _panel is None:
        _best_effort_cleanup(api)
        raise RuntimeError(
            f"[{ADDON_ID}] create_panel failed — refusing successful load so Unity tears down the ribbon shell"
        )

    _panel.add_button("Apply Nomad Palette", "apply_nomad_palette")
    _panel.add_button("Restore SPZ Palette", "restore_stableprojectorz_palette")
    _font_slider_id = _panel.add_slider("Font scale", 0.75, 1.5, float(TOKENS["font_scale"]))
    _spacing_slider_id = _panel.add_slider("Spacing scale", 0.75, 1.5, float(TOKENS["spacing_scale"]))
    _panel.add_button("Apply Scales", "apply_nomad_scales")
    _panel.add_button("Refresh Theme Status", "refresh_nomad_theme_status")
    print(
        f"[{ADDON_ID}] Registered. Use Apply Nomad Palette for theme + strip icons "
        "(SPZ skybox/background kept); Apply Scales patches font_scale/spacing_scale while Nomad is active."
    )


def unregister() -> None:
    """Restore defaults if active, then remove this add-on-owned preset."""
    global _api, _panel, _font_slider_id, _spacing_slider_id, _skybox_capture

    api = _get_api()
    current = _require_success(api.ui.get_theme(), "get_theme")
    if current.get("theme_id") == THEME_ID:
        _require_success(api.ui.reset_theme(), "reset_theme")
        _restore_skybox(api)

    catalog = _require_success(api.ui.list_themes(), "list_themes")
    registered_ids = {
        item.get("id")
        for item in catalog.get("themes", [])
        if isinstance(item, dict) and item.get("source") == "registered"
    }
    if THEME_ID in registered_ids:
        _require_success(api.ui.unregister_theme(THEME_ID), "unregister_theme")

    _panel = None
    _font_slider_id = None
    _spacing_slider_id = None
    _skybox_capture = None
    _api = None
    print(f"[{ADDON_ID}] Unregistered")


def addon_metadata() -> Dict[str, str]:
    return {
        "id": ADDON_ID,
        "theme_id": THEME_ID,
        "rpc": "1.18",
        "coverage": (
            "tokens:colors+font_scale+spacing_scale+corner_radius+icon_tint+panel_width+panel_alpha+ribbon_icon_only;"
            "surfaces:bound ThemeChanged roots incl. lists/pins/workflow_options/context_menus;"
            "compose:set_skybox_color+set_line_icon;"
            "command_ribbon:icon-only when ribbon_icon_only;"
            "persist:player_prefs;"
            "not:set_ui_scale|set_ui_target_active|blur"
        ),
    }
