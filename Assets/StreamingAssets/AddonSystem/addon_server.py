#!/usr/bin/env python3
"""
StableProjectorz Add-on Server

This server manages all add-ons and provides communication between
Python add-ons and the Unity application.
Also runs a FastAPI HTTP server for REST API access.
"""

import sys
import os
import argparse
import importlib.util
import tempfile
import time
import threading
from pathlib import Path

# Add the AddonSystem directory to path so we can import spz
addon_system_dir = Path(__file__).parent
sys.path.insert(0, str(addon_system_dir))

try:
    import spz
except ImportError:
    print("Error: Could not import spz module. Make sure spz.py is in the AddonSystem directory.")
    sys.exit(1)

# Try to import FastAPI (optional - will fall back if not available)
try:
    from fastapi import FastAPI
    import uvicorn
    FASTAPI_AVAILABLE = True
except ImportError:
    FASTAPI_AVAILABLE = False
    print("Warning: FastAPI not available. Install with: pip install fastapi uvicorn")
    print("HTTP REST API will not be available.")


# Registry of loaded addon modules by id (for invoking callbacks from Unity)
_loaded_addon_modules = {}


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
        
        # Store module so we can invoke button callbacks from Unity
        _loaded_addon_modules[addon_id] = module

        # Call register() if it exists
        if hasattr(module, "register"):
            try:
                print(f"[Addon Server] Calling register() for {addon_id} (this will send create_panel to Unity over socket)...")
                module.register()
                print(f"[Addon Server] Registered add-on: {addon_id}")
                return True
            except Exception as e:
                print(f"Error registering add-on {addon_id}: {e}")
                _loaded_addon_modules.pop(addon_id, None)
                return False
        else:
            print(f"Warning: Add-on {addon_id} has no register() function")
            _loaded_addon_modules.pop(addon_id, None)
            return False
            
    except Exception as e:
        _loaded_addon_modules.pop(addon_id, None)  # avoid broken module staying in registry
        print(f"Error loading add-on {addon_id}: {e}")
        import traceback
        traceback.print_exc()
        return False


def load_addon_by_id(addon_id, addons_dir):
    """Load a single addon by id. Used when Unity enables an addon or at startup."""
    print(f"[Addon Server] load_addon_by_id requested: {addon_id}")
    addons = discover_addons(addons_dir)
    for info in addons:
        if info["id"] == addon_id:
            ok = load_addon(info)
            print(f"[Addon Server] load_addon({addon_id}) returned {ok}")
            return ok
    print(f"[Addon Server] Add-on not found: {addon_id}")
    return False


def invoke_addon_callback(addon_id, callback_name):
    """Invoke a named function in a loaded addon. Called by Unity when user clicks an addon button."""
    module = _loaded_addon_modules.get(addon_id)
    if module is None:
        print(f"[Addon Server] Addon not loaded: {addon_id}")
        return False
    func = getattr(module, callback_name, None)
    if not callable(func):
        print(f"[Addon Server] Callback not found or not callable: {addon_id}.{callback_name}")
        return False
    try:
        func()
        return True
    except Exception as e:
        print(f"[Addon Server] Error invoking {addon_id}.{callback_name}: {e}")
        import traceback
        traceback.print_exc()
        return False


def main():
    parser = argparse.ArgumentParser(description="StableProjectorz Add-on Server")
    parser.add_argument("--port", type=int, default=5555, help="Port to connect to Unity (default: 5555)")
    parser.add_argument("--http-port", type=int, default=5557, help="Port for HTTP REST API (default: 5557)")
    parser.add_argument("--addons-dir", type=str, default=None, help="Path to Addons directory")
    parser.add_argument("--no-http", action="store_true", help="Disable HTTP REST API server")
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
    
    # So the API client connects to the same port Unity is listening on (127.0.0.1 by default)
    os.environ["SPZ_PORT"] = str(args.port)
    os.environ.setdefault("SPZ_HOST", "127.0.0.1")
    
    # Initialize API connection (Python connects to Unity's socket at 127.0.0.1:args.port)
    api = spz.get_api()
    
    # Start HTTP server *before* waiting for Unity so port 5557 is listening when Unity calls /load_addon
    addons = discover_addons(addons_dir)
    http_thread = None
    # Shared flag: set when Python has connected to Unity (socket 5555). Kept in sync by connection_ready_callback so it resets if the socket drops.
    _connected_to_unity = [False]  # list so closure can assign
    _connection_lock = threading.Lock()  # synchronize access across connection_loop, _check_connection_ready, and main wait

    def _check_connection_ready():
        """Probe Unity socket; return True only if we can reach Unity. Resets _connected_to_unity if connection is dead."""
        try:
            api.cameras.get_pos(0)
            with _connection_lock:
                _connected_to_unity[0] = True
            return True
        except Exception:
            with _connection_lock:
                _connected_to_unity[0] = False
            return False

    if FASTAPI_AVAILABLE and not args.no_http:
        try:
            from http_server import start_server, set_api_instance, set_load_addon_callback, set_invoke_callback, set_connection_ready_callback
            set_api_instance(api)
            set_load_addon_callback(lambda addon_id: load_addon_by_id(addon_id, addons_dir))
            set_invoke_callback(invoke_addon_callback)
            set_connection_ready_callback(_check_connection_ready)
            http_thread = threading.Thread(
                target=start_server,
                args=("127.0.0.1", args.http_port),
                daemon=True
            )
            http_thread.start()
            print(f"[Add-on Server] HTTP REST API available on port {args.http_port}")
            print(f"[Add-on Server] API docs: http://127.0.0.1:{args.http_port}/docs")
        except Exception as e:
            print(f"[Add-on Server] Warning: Could not start HTTP server: {e}")
    else:
        if not addons:
            print("No add-ons found")
        else:
            print(f"Discovered {len(addons)} add-on(s). Unity will request load for enabled addons.")
    
    # File-based handshake: wait for Unity to write the ready marker (so we know the socket is bound before connecting).
    marker_name = f"spz_addon_{args.port}_ready.txt"
    marker_path = os.path.join(tempfile.gettempdir(), marker_name)
    marker_timeout = 90  # seconds to wait for game to load and bind
    print(f"Waiting for Unity socket ready marker (up to {marker_timeout}s): {marker_path}")
    for wait in range(marker_timeout):
        if os.path.isfile(marker_path):
            print(f"Unity socket ready marker found (game has bound port {args.port}). Connecting...")
            break
        if wait < marker_timeout - 1:
            if wait % 10 == 0 and wait > 0:
                print(f"  ... still waiting for game to start and bind ({wait}s)")
            time.sleep(1)
    else:
        print("")
        print("Unity NEVER created the ready marker file. So:")
        print("  - The addon scene did not load, OR the socket server did not start in the build.")
        print("  - Start the GAME first and wait until the main window is fully visible, then run this script.")
        print("  - Rebuild the game so Tool_AddonSystem and Addon_SocketServer are in the build.")
        print("  - Check Player.log for: [Addon_SocketServer] Started listening on 127.0.0.1:5555")
        print("     If that line never appears, the socket server is not running in your build.")
        return 1

    # Connect to Unity: Python (client) -> Unity Addon_SocketServer at 127.0.0.1:args.port.
    max_retries = 30  # usually immediate once marker exists
    print(f"Connecting to Unity at 127.0.0.1:{args.port} (up to {max_retries} tries)...")
    def connection_loop():
        for retry in range(max_retries):
            try:
                api.cameras.get_pos(0)
                with _connection_lock:
                    _connected_to_unity[0] = True
                return
            except Exception as e:
                if retry < max_retries - 1:
                    print(f"Waiting for Unity connection... ({retry + 1}/{max_retries})")
                    time.sleep(1)
                else:
                    print(f"Failed to connect to Unity: {e}")
    conn_thread = threading.Thread(target=connection_loop, daemon=True)
    conn_thread.start()
    while True:
        with _connection_lock:
            connected = _connected_to_unity[0]
        if connected or not conn_thread.is_alive():
            break
        time.sleep(0.3)
    with _connection_lock:
        if not _connected_to_unity[0]:
            print("Could not establish connection to Unity (marker was present but socket connect failed).")
            print("  Check firewall or try restarting the game and this script.")
            return 1
    print("Connected to Unity!")
    if FASTAPI_AVAILABLE and not args.no_http:
        if addons:
            print(f"Discovered {len(addons)} add-on(s). Unity will request load for enabled addons.")
        else:
            print("No add-ons found.")
    
    # If HTTP server is disabled, load all addons at startup (legacy behavior)
    if not (FASTAPI_AVAILABLE and not args.no_http) and addons:
        print(f"Loading {len(addons)} add-on(s)...")
        for addon_info in addons:
            load_addon(addon_info)
    
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
