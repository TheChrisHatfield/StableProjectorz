# Addon server – "Connection refused" (WinError 10061)

## How this compares to Stable Diffusion (Unity ↔ SD)

**Stable Diffusion:** Unity does **not** listen for SD. The SD WebUI **listens** (e.g. on 7860). **Unity connects** to it: `ConnectionPanel_UI` runs a loop that does HTTP GET to `http://{ip}:7860/.../ping` every 0.5s. When the request succeeds, Unity treats the connection as confirmed (green icon). So **confirming** the SD connection = Unity successfully getting an HTTP response from the server.

**Addon system (two directions):**

| Direction | Who listens | Who connects | How we confirm |
|-----------|-------------|--------------|----------------|
| **Python → Unity (socket)** | Unity on **5555** (Addon_SocketServer) | Python (addon_server.py) | Python connects; Unity logs `[Addon_SocketServer] Started listening on 127.0.0.1:5555`. If Python gets WinError 10061, Unity was not listening. |
| **Unity → FastAPI (HTTP)** | Python on **5557** (FastAPI) | Unity (Addon_MGR) | Unity polls GET `http://127.0.0.1:5557/ready` (same idea as SD: Unity is the HTTP client). When the request succeeds and `ready: true`, Python has connected to Unity's socket and FastAPI is ready. |

So **Unity does not "listen to" FastAPI** — Unity **calls** FastAPI (GET /ready, POST /load_addon), just like Unity calls the SD server. To confirm Unity is talking to FastAPI, check Player.log for a successful response from port 5557 (e.g. "Addon HTTP server responding on 5557" or "Addon server ready"). If Unity never gets a response from 5557, either Python isn't running or the HTTP server didn't start (e.g. Python is stuck trying to connect to 5555 and hasn't finished startup).

---

## What's going on (WinError 10061)

- **Unity** runs a TCP server (Addon_SocketServer) on **127.0.0.1:5555**.
- **Python** (addon_server.py) is the **client** that connects to that port.
- **WinError 10061** means: at the moment Python tried to connect, **nothing was listening** on 127.0.0.1:5555.

So either Unity never opened the socket, or Python gave up before Unity was ready.

**File-based handshake (breaks the loop):** When Unity's socket server binds, it writes a **ready marker file** to your temp folder (e.g. `%TEMP%\spz_addon_5555_ready.txt`). Python **waits for this file** (up to 90s) before trying to connect. So:
- If you see **"Unity NEVER created the ready marker file"** → the addon scene or socket server did not run in the build; rebuild and/or check Player.log.
- If you see **"Unity socket ready marker found ... Connecting..."** then connection fails → socket/firewall issue after the game did bind.

---

## Step 1: Which script are you running?

When you start the addon server, it prints:

- `Waiting for Unity socket ready marker (up to 90s): ...` → you're on the **current** script (waits for marker file, then connects).
- `(will retry up to 60 times...)` or `(29/30)` → you're on an **old** copy; copy latest `addon_server.py` into the build's StreamingAssets or rebuild.

If you see 30 retries:

- **Option A:** Copy the latest script into the build:
  - From: `Assets/StreamingAssets/AddonSystem/addon_server.py`
  - To: `Build_IL2CPP/StableProjectorz_Data/StreamingAssets/AddonSystem/addon_server.py`
- **Option B:** Rebuild the project so the build gets the latest StreamingAssets.

---

## Step 2: Who starts first?

- **Intended:** Start the **game (StableProjectorz.exe)** first. It loads the addon scene, opens the socket on 5555, then starts Python via `StartAddonServer.bat`. Python then connects to an already-listening port.
- **If you start Python by hand:** Start the **game first**, wait until the main window is fully loaded, then run addon_server.py (or the .bat). If you start Python before/during game load, Python may hit 10061 because the socket isn't ready yet; the updated script retries for 60 seconds to give the game time to load.

---

## Step 3: Is Unity actually listening? (Player.log)

The only way to know is **Unity's log**.

1. Run **StableProjectorz.exe** and wait until the main window is up.
2. Close the game (or leave it open).
3. Open **Player.log**:
   - Next to the .exe: `Build_IL2CPP/StableProjectorz_Data/` (sometimes the log is one level up), or
   - `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\Player.log`
4. Search for: **`[Addon_SocketServer] Started listening on 127.0.0.1:5555`**

**If you see that line:** Unity is binding. Then:
- Make sure you're on the **60-retry** addon_server.py (see Step 1).
- Start order: game first, then (if needed) Python.

**If you never see that line:** Unity's socket server didn't run. Then:
- Do a **full rebuild** (so Tool_AddonSystem and the socket server script are in the build).
- Run the game again and check Player.log. If the line still never appears, the addon scene may not be loading or the Addon_SocketServer component may be missing/broken in the build.

---

## Summary

| Symptom | What to do |
|--------|------------|
| You see "(29/30)" or 30 retries | Copy latest `addon_server.py` into the build's StreamingAssets or rebuild. |
| Player.log has "Started listening on 127.0.0.1:5555" | Unity is fine; use 60-retry script and start game first, then Python if manual. |
| Player.log never has "Started listening" | Rebuild; if still missing, check addon scene and Addon_SocketServer in build. |
| Player.log has "Addon HTTP server (FastAPI) responding on 5557" | Unity successfully reached FastAPI (same role as Unity→SD). If "ready" never logs, Python hasn't connected to Unity socket 5555 yet. |
