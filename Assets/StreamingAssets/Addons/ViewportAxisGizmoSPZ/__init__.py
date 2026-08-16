# ViewportAxisGizmoSPZ — viewport only (no command-ribbon tab).
# When enabled, Unity docks the XYZ orientation gizmo in the top-right of the 3D viewport
# (see spz.ui.attach_viewport_axis_gizmo). Clicking an axis snaps the view; the lantern
# in the middle frames the whole scene.
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

ADDON_ID = "ViewportAxisGizmoSPZ"
CENTER_ICON = "Addons/ViewportAxisGizmoSPZ/lantern.png"
CENTER_COMMAND = "viewport_axis_gizmo_overview"


def register() -> None:
    if spz is None:
        print(f"[{ADDON_ID}] spz not importable — run from the game; Add-on Manager can still attach the gizmo.")
        return
    api = spz.get_api()
    ok = api.ui.attach_viewport_axis_gizmo(
        size=104,
        center_icon=CENTER_ICON,
        center_command=CENTER_COMMAND,
    )
    if ok:
        print(f"[{ADDON_ID}] Viewport axis gizmo attached (no command-ribbon tab).")
    else:
        print(
            f"[{ADDON_ID}] attach_viewport_axis_gizmo not ready yet; "
            f"Add-on Manager will retry on the main thread."
        )
    # Intentionally no create_panel: the gizmo lives on the viewport, not in the right command-ribbon strip.


def addon_metadata() -> Dict[str, str]:
    return {
        "id": ADDON_ID,
        "ui": "viewport_gizmo_only",
        "viewport_rpc": "spz.ui.attach_viewport_axis_gizmo",
    }
