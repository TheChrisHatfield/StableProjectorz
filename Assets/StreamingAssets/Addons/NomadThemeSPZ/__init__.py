"""Nomad-inspired semantic UI palette for StableProjectorz add-on surfaces."""

from __future__ import annotations

import os
import sys
from typing import Any, Dict, Optional


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

TOKENS: Dict[str, str] = {
    # Pro-Studio Monolith palette from the supplied Nomad UI replication design.
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
}

_api: Optional[Any] = None
_panel: Optional[Any] = None


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


def apply_nomad_palette() -> None:
    """Apply registered palette colors only (retry-safe re-register + apply_theme)."""
    api = _get_api()
    _register_preset(api)
    try:
        _require_success(api.ui.apply_theme(THEME_ID), "apply_theme")
    except Exception:
        _best_effort_cleanup(api)
        raise
    print(f"[{ADDON_ID}] Applied '{THEME_ID}' colors")


def restore_stableprojectorz_palette() -> None:
    """Restore builtin colors while leaving this preset available."""
    api = _get_api()
    _require_success(api.ui.reset_theme(), "reset_theme")
    print(f"[{ADDON_ID}] Restored StableProjectorz default palette")


def register() -> None:
    """Register the preset and expose apply/restore controls (does not auto-apply colors)."""
    global _panel

    api = _get_api()
    try:
        # Register only — Apply Nomad Palette button changes colors explicitly.
        _register_preset(api)
        _panel = api.ui.create_panel(ADDON_ID, ADDON_TITLE)
    except Exception:
        _panel = None
        _best_effort_cleanup(api)
        raise
    if _panel is None:
        # Soft-success left dial ON + empty eager ribbon tab and orphaned theme registration.
        _best_effort_cleanup(api)
        raise RuntimeError(
            f"[{ADDON_ID}] create_panel failed — refusing successful load so Unity tears down the ribbon shell"
        )

    _panel.add_button("Apply Nomad Palette", "apply_nomad_palette")
    _panel.add_button("Restore SPZ Palette", "restore_stableprojectorz_palette")
    print(
        f"[{ADDON_ID}] Registered. Use Apply Nomad Palette to change colors only "
        "(command ribbon, Paint, manager, Settings, status, viewport ribbons)."
    )


def unregister() -> None:
    """Restore defaults if active, then remove this add-on-owned preset."""
    global _api, _panel

    api = _get_api()
    current = _require_success(api.ui.get_theme(), "get_theme")
    if current.get("theme_id") == THEME_ID:
        _require_success(api.ui.reset_theme(), "reset_theme")

    catalog = _require_success(api.ui.list_themes(), "list_themes")
    registered_ids = {
        item.get("id")
        for item in catalog.get("themes", [])
        if isinstance(item, dict) and item.get("source") == "registered"
    }
    if THEME_ID in registered_ids:
        _require_success(api.ui.unregister_theme(THEME_ID), "unregister_theme")

    _panel = None
    _api = None
    print(f"[{ADDON_ID}] Unregistered")


def addon_metadata() -> Dict[str, str]:
    return {
        "id": ADDON_ID,
        "theme_id": THEME_ID,
        "rpc": "1.12",
        "coverage": (
            "addon_panels,command_ribbon,paint_tab,addon_manager,"
            "settings,viewport_statusline,viewport_ribbons,"
            "sd_input_panel,export_save_menu,scene_resolution,connection_panels"
        ),
    }
