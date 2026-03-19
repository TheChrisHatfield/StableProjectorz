# Where to find Player.log (Unity build)

When the **game** (StableProjectorz.exe) runs, Unity writes a log file. Use it to see if the addon socket server started.

## Locations (try in this order)

1. **Next to the .exe (or one folder up)**  
   - `Build_IL2CPP/Player.log`  
   - or `Build_IL2CPP/StableProjectorz_Data/Player.log`  
   - or in the same folder as `StableProjectorz.exe`

2. **Windows AppData**  
   - `%USERPROFILE%\AppData\LocalLow\StableProjectorz\Stable Projectorz 2.4.5\Player.log`  
   - (Company Name and Product Name from ProjectSettings; version may vary.)

## What to search for

- **`[Addon_SocketServer] Started listening on 127.0.0.1:5555`**  
  → Socket server is running; Python should be able to connect.

- **`[Addon_MGR] Addon_SocketServer was missing in scene; created at runtime`**  
  → Scene had no socket server; we created one. You should still see "Started listening" right after.

- **`[Addon_MGR] Addon system initializing`**  
  → Addon system ran. If you never see "Started listening", the listener failed to bind (e.g. port in use or exception).

If **none** of these lines appear, the addon scene (Tool_AddonSystem) or Addon_MGR did not run in that build:

- **Rebuild** the game (File → Build Settings → ensure `Tool_AddonSystem.unity` is enabled).
- Start the **game first** (StableProjectorz.exe), wait until the main window is visible, then run `addon_server.py` or `Run_with_Addons.bat`.
- Check for **"Skipping scene '...Tool_AddonSystem...' because it is not in the Build Settings"** in Player.log—if present, add the scene in Build Settings and rebuild.
