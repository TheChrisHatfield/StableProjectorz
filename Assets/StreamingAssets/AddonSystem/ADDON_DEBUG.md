# Addon tab not appearing – how to capture debug data

When you click **Load addons now** and addon tabs still don’t appear, capture the following so we can see where the chain stops.

## 0. Automatic debug log file (easiest)

While the game runs, addon-related Unity logs are written to a file. After a run you can open or share this file.

- **Path:** `AddonDebug.log` next to the project (Editor) or next to the .exe (build) — same drive as the game, not C: AppData.
- **Editor:** project root, e.g. `D:\...\StableProjectorz\AddonDebug.log`
- **Build:** folder containing the exe, e.g. `D:\...\Build_IL2CPP\AddonDebug.log`
- **To confirm:** check the Unity Console for `[AddonDebugCapture] Addon debug log file: ...` — that path is the file.

**How to use:** Run the game, open Add-on Manager, click **Load addons now**, then close the game (or leave it running). Open `AddonDebug.log` in a text editor. The file contains only `[Addon_MGR]`, `[Addon_SocketServer]`, `[AddonUI_MGR]`, and `[CommandRibbon_UI]` lines with timestamps. Share that file (or paste its contents) for debugging. A line `--- Load addons now finished ---` marks the end of one “Load addons now” run.

## 1. Unity Console (Player or Editor)

Open the Console and click **Load addons now**. Then copy or note all lines that start with:

- **`[Addon_MGR]`** – HTTP request to Python, response, how many addons were requested
- **`[Addon_SocketServer]`** – Whether Python’s `create_panel` call reached Unity (`create_panel from Python` / `create_panel OK` / `create_panel FAILED`)
- **`[AddonUI_MGR]`** – Whether CreatePanel ran, ribbon found, and which parent was used
- **`[CommandRibbon_UI]`** – Tab strip used, tab created, child count

**What to look for:**

- If you see **`[Addon_MGR] Successfully loaded addon: X`** but **no** `[Addon_SocketServer] create_panel from Python` → Python load succeeded but the addon never sent `create_panel` (socket/connection or addon code).
- If you see **`[Addon_SocketServer] create_panel from Python`** but then **`create_panel FAILED`** or **`[AddonUI_MGR] GetOrCreatePanelForAddon returned null`** → Unity received the call but ribbon/panel creation failed.
- If you see **`[CommandRibbon_UI] Addon tab created`** but no tab in the UI → tab is created but parent/layout or visibility issue.

## 2. Python addon server (if you start it manually)

If you run the addon server from a terminal (e.g. from `StableProjectorz_Data/StreamingAssets/AddonSystem`):

```bash
python addon_server.py --port 5555 --http-port 5557 --addons-dir "../Addons"
```

Watch for:

- **`[Addon Server] load_addon_by_id requested: MeshTools`** (or your addon id)
- **`[Addon Server] Calling register() for MeshTools ...`**
- **`[Addon Server] Registered add-on: MeshTools`**
- Any **traceback** or **Error registering** → addon’s `register()` or `create_panel` failed on the Python side.

If Unity starts the Python process for you, these prints may go to the Unity log or be hidden; the Unity `[Addon_MGR]` and `[Addon_SocketServer]` logs are then the main source of truth.

## 3. Build: “Cannot connect to destination host” (port 5557)

When running the **built exe** (not Editor), Unity starts the Python addon server itself. If you see:

- **`[Addon_MGR] load_addon failed for X: Cannot connect to destination host. Ensure Python server is running on port 5557`**

then Unity could not start Python (often because **`python` is not on PATH** when you launch the exe).

**Fix:** Run the game via the launcher so Python is on PATH when Unity starts:

- From the **project root**, run **`Run_with_Addons.bat`** (instead of double‑clicking `Build_IL2CPP\StableProjectorz.exe`).
- Or in-game: open **Add-on Manager** and click **Restart with addons** — the game finds `Run_with_Addons.bat` (same discovery as Run_noQuickEdit for WebUI; env `SPZ_ADDONS_RUN_PATH` optional), launches it, and quits so the bat starts the game with Python on PATH.
- The launcher finds Python (PATH, `py`, or common install paths), adds it to PATH, then starts the exe. Unity can then start the addon server and “Load addons now” can reach port 5557.

If Python is not installed, install Python 3.10+ and either add it to system PATH or run `Run_with_Addons.bat` (it will try common install locations).

## 4. Quick checklist

- Python addon server running and connected to Unity (port 5555)?
- HTTP server for addons running (port 5557) so **Load addons now** can call `/load_addon`?
- **Build:** Using **Run_with_Addons.bat** (or ensure `python` is on PATH when the exe starts)?
- Add-on Manager shows addons as **Enabled** before you click **Load addons now**?
- Right panel (with the ribbon) has been opened at least once so the ribbon exists in the scene?

Share the relevant `[Addon_MGR]`, `[Addon_SocketServer]`, `[AddonUI_MGR]`, and `[CommandRibbon_UI]` lines (and any Python output if you have it) to pinpoint where the chain stops.
