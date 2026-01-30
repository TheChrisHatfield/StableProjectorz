"""
Mesh Tools Add-on

Example add-on demonstrating expanded mesh operations, scene information,
and Stable Diffusion integration.
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


def center_selected_meshes():
    """Center all selected meshes at origin"""
    api = spz.get_api()
    selected = api.models.get_selected()
    
    if not selected:
        print("No meshes selected")
        return
    
    # Get bounds of selected meshes
    bounds = api.scene.get_selected_meshes_bounds()
    if bounds:
        center = bounds["center"]
        # Move each mesh by the negative of the center
        for mesh_id in selected:
            pos = api.models.get_pos(mesh_id)
            if pos:
                api.models.set_pos(mesh_id, 
                                 pos["x"] - center["x"],
                                 pos["y"] - center["y"],
                                 pos["z"] - center["z"])
        print(f"Centered {len(selected)} meshes")
    else:
        print("Could not get bounds")


def randomize_selected_positions():
    """Randomly offset selected meshes"""
    import random
    api = spz.get_api()
    selected = api.models.get_selected()
    
    if not selected:
        print("No meshes selected")
        return
    
    for mesh_id in selected:
        pos = api.models.get_pos(mesh_id)
        if pos:
            offset_x = random.uniform(-2, 2)
            offset_y = random.uniform(-2, 2)
            offset_z = random.uniform(-2, 2)
            api.models.set_pos(mesh_id,
                             pos["x"] + offset_x,
                             pos["y"] + offset_y,
                             pos["z"] + offset_z)
    print(f"Randomized positions of {len(selected)} meshes")


def hide_unselected():
    """Hide all unselected meshes"""
    api = spz.get_api()
    all_ids = api.scene.get_all_mesh_ids()
    selected = api.models.get_selected()
    selected_set = set(selected)
    
    hidden_count = 0
    for mesh_id in all_ids:
        if mesh_id not in selected_set:
            api.models.set_visibility(mesh_id, False)
            hidden_count += 1
    
    print(f"Hidden {hidden_count} meshes")


def show_all():
    """Show all meshes"""
    api = spz.get_api()
    all_ids = api.scene.get_all_mesh_ids()
    
    for mesh_id in all_ids:
        api.models.set_visibility(mesh_id, True)
    
    print(f"Shown {len(all_ids)} meshes")


def print_scene_info():
    """Print information about the scene"""
    api = spz.get_api()
    total = api.scene.get_total_mesh_count()
    selected = api.scene.get_selected_mesh_count()
    all_ids = api.scene.get_all_mesh_ids()
    
    print(f"Scene Info:")
    print(f"  Total meshes: {total}")
    print(f"  Selected meshes: {selected}")
    print(f"  Mesh IDs: {all_ids}")
    
    if selected > 0:
        bounds = api.scene.get_selected_meshes_bounds()
        if bounds:
            print(f"  Selected bounds center: ({bounds['center']['x']:.2f}, "
                  f"{bounds['center']['y']:.2f}, {bounds['center']['z']:.2f})")
            print(f"  Selected bounds size: ({bounds['size']['x']:.2f}, "
                  f"{bounds['size']['y']:.2f}, {bounds['size']['z']:.2f})")


def generate_with_prompt():
    """Set a prompt and trigger generation"""
    api = spz.get_api()
    
    if not api.sd.is_connected():
        print("Stable Diffusion not connected!")
        return
    
    if api.sd.is_generating():
        print("Generation already in progress!")
        return
    
    # Set prompts
    api.sd.set_positive_prompt("beautiful texture, high quality, detailed")
    api.sd.set_negative_prompt("blurry, low quality")
    
    # Trigger generation
    success = api.sd.trigger_generation(is_background=False)
    if success:
        print("Generation triggered!")
    else:
        print("Failed to trigger generation")


def check_generation_status():
    """Check if generation is in progress"""
    api = spz.get_api()
    is_gen = api.sd.is_generating()
    is_conn = api.sd.is_connected()
    
    print(f"SD Connected: {is_conn}")
    print(f"Generating: {is_gen}")
    
    if is_conn and not is_gen:
        pos_prompt = api.sd.get_positive_prompt()
        neg_prompt = api.sd.get_negative_prompt()
        print(f"Positive prompt: {pos_prompt}")
        print(f"Negative prompt: {neg_prompt}")


def register():
    """Register this add-on with the UI"""
    api = spz.get_api()
    
    # Create a panel for this add-on
    panel = api.ui.create_panel("MeshTools", "Mesh Tools")
    if panel:
        # Mesh manipulation buttons
        panel.add_button("Center Selected", "center_selected_meshes")
        panel.add_button("Randomize Positions", "randomize_selected_positions")
        panel.add_button("Hide Unselected", "hide_unselected")
        panel.add_button("Show All", "show_all")
        panel.add_button("Print Scene Info", "print_scene_info")
        
        # Stable Diffusion buttons
        panel.add_button("Generate with Prompt", "generate_with_prompt")
        panel.add_button("Check Gen Status", "check_generation_status")
        
        print("Mesh Tools add-on registered successfully")
    else:
        print("Failed to create UI panel for Mesh Tools add-on")


if __name__ == "__main__":
    # For testing
    register()
