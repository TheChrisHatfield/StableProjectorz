# RibbonOnlyFullscreen — viewport only (no command-ribbon tab).
# When enabled, Unity attaches the FULL/SRN control next to Gen Art (see spz.ui.attach_viewport_fullview_toggle).
# Add-on Manager also runs the same attach when HTTP is off (Python register may not run).

import os
import sys
from typing import Dict

_addon_system = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "AddonSystem"))
if os.path.isdir(_addon_system) and _addon_system not in sys.path:
    sys.path.insert(0, _addon_system)

try:
    import spz
except ImportError:
    spz = None  # type: ignore[assignment, misc]

ADDON_ID = "RibbonOnlyFullscreen"


def register() -> None:
    if spz is None:
        print(f"[{ADDON_ID}] spz not importable — run from the game; Add-on Manager can still attach the viewport control.")
        return
    api = spz.get_api()
    v_ok = api.ui.attach_viewport_fullview_ribbon_toggle(
        button_label="FULL\nSRN",
        command="viewport_fullview_toggle",
    )
    if v_ok:
        print(f"[{ADDON_ID}] Viewport full-view toggle attached (no command-ribbon tab).")
    else:
        print(
            f"[{ADDON_ID}] attach_viewport_fullview_toggle not ready yet; "
            f"Add-on Manager will retry on the main thread."
        )
    # Intentionally no create_panel: do not add a "Full view" tab to the right command-ribbon strip.


def addon_metadata() -> Dict[str, str]:
    return {
        "id": ADDON_ID,
        "ui": "viewport_dock_only",
        "viewport_rpc": "spz.ui.attach_viewport_fullview_toggle",
    }
