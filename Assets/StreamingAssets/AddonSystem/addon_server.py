#!/usr/bin/env python3
"""
StableProjectorz Add-on Server

This server manages all add-ons and provides communication between
Python add-ons and the Unity application.
"""

import sys
import os
import argparse
import importlib.util
import time
from pathlib import Path

# Add the AddonSystem directory to path so we can import spz
addon_system_dir = Path(__file__).parent
sys.path.insert(0, str(addon_system_dir))

try:
    import spz
except ImportError:
    print("Error: Could not import spz module. Make sure spz.py is in the AddonSystem directory.")
    sys.exit(1)


def discover_addons(addons_dir):
    """Discover all add-ons in the Addons directory"""
    addons = []
    addons_path = Path(addons_dir)
    
    if not addons_path.exists():
        print(f"Addons directory does not exist: {addons_path}")
        return addons
    
    for addon_dir in addons_path.iterdir():
        if not addon_dir.is_dir():
            continue
        
        init_file = addon_dir / "__init__.py"
        if init_file.exists():
            addons.append({
                "id": addon_dir.name,
                "path": str(addon_dir),
                "init_file": str(init_file)
            })
            print(f"Discovered add-on: {addon_dir.name}")
    
    return addons


def load_addon(addon_info):
    """Load and register an add-on"""
    addon_id = addon_info["id"]
    init_file = addon_info["init_file"]
    
    try:
        # Load the add-on module
        spec = importlib.util.spec_from_file_location(f"addon_{addon_id}", init_file)
        if spec is None or spec.loader is None:
            print(f"Error: Could not load add-on {addon_id}")
            return False
        
        module = importlib.util.module_from_spec(spec)
        sys.modules[f"addon_{addon_id}"] = module
        spec.loader.exec_module(module)
        
        # Call register() if it exists
        if hasattr(module, "register"):
            try:
                module.register()
                print(f"Registered add-on: {addon_id}")
                return True
            except Exception as e:
                print(f"Error registering add-on {addon_id}: {e}")
                return False
        else:
            print(f"Warning: Add-on {addon_id} has no register() function")
            return False
            
    except Exception as e:
        print(f"Error loading add-on {addon_id}: {e}")
        import traceback
        traceback.print_exc()
        return False


def main():
    parser = argparse.ArgumentParser(description="StableProjectorz Add-on Server")
    parser.add_argument("--port", type=int, default=5555, help="Port to connect to Unity (default: 5555)")
    parser.add_argument("--addons-dir", type=str, default=None, help="Path to Addons directory")
    args = parser.parse_args()
    
    # Determine addons directory
    if args.addons_dir:
        addons_dir = args.addons_dir
    else:
        # Default: StreamingAssets/Addons relative to this script
        script_dir = Path(__file__).parent
        addons_dir = script_dir.parent / "Addons"
    
    print(f"StableProjectorz Add-on Server")
    print(f"Addons directory: {addons_dir}")
    print(f"Connecting to Unity on port {args.port}...")
    
    # Initialize API connection
    api = spz.get_api()
    
    # Wait for connection (Unity might not be ready yet)
    max_retries = 30
    retry_count = 0
    connected = False
    
    while retry_count < max_retries:
        try:
            # Try a simple request to test connection
            api.cameras.get_pos(0)
            connected = True
            break
        except Exception as e:
            retry_count += 1
            if retry_count < max_retries:
                print(f"Waiting for Unity connection... ({retry_count}/{max_retries})")
                time.sleep(1)
            else:
                print(f"Failed to connect to Unity: {e}")
                return 1
    
    if not connected:
        print("Could not establish connection to Unity")
        return 1
    
    print("Connected to Unity!")
    
    # Discover and load add-ons
    addons = discover_addons(addons_dir)
    
    if not addons:
        print("No add-ons found")
        return 0
    
    print(f"Loading {len(addons)} add-on(s)...")
    
    loaded_count = 0
    for addon_info in addons:
        if load_addon(addon_info):
            loaded_count += 1
    
    print(f"Loaded {loaded_count}/{len(addons)} add-on(s)")
    
    # Keep server running
    print("Add-on server running. Press Ctrl+C to stop.")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nShutting down...")
        api.close()
        return 0


if __name__ == "__main__":
    sys.exit(main())
