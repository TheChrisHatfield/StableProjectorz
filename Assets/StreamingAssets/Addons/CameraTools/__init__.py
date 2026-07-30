"""
Camera Tools Add-on

Example add-on demonstrating camera control and mesh selection.
"""

import sys
import os

# Add AddonSystem to path
addon_system_dir = os.path.join(os.path.dirname(__file__), "..", "..", "AddonSystem")
if os.path.exists(addon_system_dir):
    sys.path.insert(0, addon_system_dir)

try:
    import spz
except ImportError:
    print("Error: Could not import spz module")
    sys.exit(1)


def rotate_camera():
    """Rotate camera by 1 unit in X"""
    api = spz.get_api()
    pos = api.cameras.get_pos(0)
    if not pos:
        print("Could not get camera position")
        return False
    if api.cameras.set_pos(0, pos["x"] + 1, pos["y"], pos["z"]) is False:
        print("Could not set camera position")
        return False
    print(f"Camera moved to: {pos['x'] + 1}, {pos['y']}, {pos['z']}")


def reset_camera():
    """Reset camera to origin"""
    api = spz.get_api()
    ok_pos = api.cameras.set_pos(0, 0, 0, -10)
    ok_rot = api.cameras.set_rot(0, 0, 0, 0, 1)
    if ok_pos is False or ok_rot is False:
        print("Camera reset failed")
        return False
    print("Camera reset to origin")


def select_first_mesh():
    """Select the first available mesh"""
    api = spz.get_api()
    selected = api.models.get_selected()
    if selected:
        print(f"Currently selected meshes: {selected}")
    else:
        print("No meshes selected. Try selecting a mesh manually first.")
        return False


def register():
    """Register this add-on with the UI"""
    api = spz.get_api()
    
    # Create a panel for this add-on
    panel = api.ui.create_panel("CameraTools", "Camera Tools")
    if panel:
        # Add buttons
        panel.add_button("Rotate Camera", "rotate_camera")
        panel.add_button("Reset Camera", "reset_camera")
        panel.add_button("Show Selected", "select_first_mesh")
        print("Camera Tools add-on registered successfully")
    else:
        raise RuntimeError(
            "CameraTools: create_panel failed — refusing successful load so Unity tears down the ribbon shell"
        )


if __name__ == "__main__":
    # For testing
    register()
