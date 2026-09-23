# How to Build StableProjectorz

## Using `build_for_testing.bat` (Windows, recommended for GitHub clones)

This is the **one file you double-click**. It runs the PowerShell build script in the background (progress bar and log). You do not need to open the Unity Editor for every build.

### Before you start

1. **Install Unity** via [Unity Hub](https://unity.com/download): use the exact version in `ProjectSettings/ProjectVersion.txt` (for example `6000.3.6f1`).
2. In Hub → **Installs** → gear on that version → **Add modules** → enable **Windows Build Support (IL2CPP)**.
3. **Activate license once:** Hub → **Open** this project folder → wait until the Editor finishes loading → close Unity.
4. **Close Unity** before building. The script refuses to run while the Editor has this project open (avoids a broken or instant-fail build).
5. **Close `StableProjectorz.exe`** if it is running from a previous build.

### Run the build

1. Open the repo root in File Explorer (folder that contains `Assets`, `ProjectSettings`, and `build_for_testing.bat`).
2. Double-click **`build_for_testing.bat`**.
3. Leave the window open until you see **Build succeeded** or an error. Do not open this project in Unity while the window is building.
4. Run the game from **`Build_IL2CPP\StableProjectorz.exe`**.

**Full clean (optional):** delete `Build_IL2CPP\StableProjectorz_Data` and the `StableProjectorz_BackUpThisFolder_ButDontShipItWithYourGame` folder yourself, or run:

```bat
build_for_testing.bat clean
```

That removes Data/BackUp before Unity starts. Unity still cleans most of `Build_IL2CPP` during the build and keeps WebUI folders if present.

### Logs and output

| Item | Location |
|------|----------|
| Game executable | `Build_IL2CPP\StableProjectorz.exe` |
| Build log | `build_output.txt` (repo root) |
| Package resolve log (only if UPM was repaired) | `pkg_resolve_log.txt` |

### If Unity is not found automatically

The script looks for Hub’s install under Program Files, matching `ProjectSettings/ProjectVersion.txt`.

**Option A — environment variable:** set `UNITY_EXE` to your `Unity.exe` path, then run the `.bat` again.

**Option B — local override (not committed to git):** copy `build_for_testing.local.ps1.example` to `build_for_testing.local.ps1` and set:

```powershell
$SpzUnityEditorPath = 'C:\Path\To\Unity\Editor\Unity.exe'
```

Use the Hub layout, for example: `C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe`.

### Common `build_for_testing.bat` issues

| Symptom | What to do |
|---------|------------|
| “Unity is already running” | Close all Unity Editor windows and any stray `Unity.exe` in Task Manager, then retry. |
| Very short log, no `[BuildForTesting]` in `build_output.txt` | Project still open elsewhere or `Library` locked; close Unity and rebuild. |
| “Unity … was not found” | Install the editor version from `ProjectVersion.txt` in Hub, or set `UNITY_EXE` / `build_for_testing.local.ps1`. |
| IL2CPP / Bee errors in the log | Install **Windows Build Support (IL2CPP)** for that Unity version; free disk space; close the built `.exe` and rebuild. |
| License / exit code 198 | Open the project once in the Editor (see license step above). The script may retry without batch mode. |

---

## Other build methods

### Method 1: Using Unity Editor

1. **Open Unity Hub** (Start menu or your Hub install path).

2. **Open the Project:**
   - In Unity Hub, click "Open" or "Add"
   - Select this repository folder (the directory that contains `Assets` and `ProjectSettings`)
   - Click "Open"

3. **Wait for Unity Editor to Load:**
   - Unity will compile scripts and import assets
   - This may take a few minutes

4. **Build the Project:**
   - Press `Ctrl + Shift + B` (or go to File → Build Settings)
   - Click **"Build and Run"**
   - Choose output location (default: `Build_IL2CPP\StableProjectorz.exe`)
   - Wait for build to complete

5. **Verify Build:**
   - Check `Build_IL2CPP\` folder for `StableProjectorz.exe`
   - The executable should be ready to run

### Method 2: PowerShell directly

From the repo root:

```powershell
.\build_for_testing.ps1
.\build_for_testing.ps1 -Clean
```

Same behavior as the `.bat` file.

### Method 3: Manual command line

Use the Editor version from `ProjectSettings/ProjectVersion.txt` (e.g. `6000.3.6f1`):

```powershell
$Project = (Get-Location).Path
$Unity = "${env:ProgramFiles}\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe"
& $Unity -batchmode -quit -projectPath $Project -executeMethod BuildForTesting.BuildWin64 -logFile "build_output.txt"
```

## Build Output Location

- **IL2CPP Build:** `Build_IL2CPP\StableProjectorz.exe`
- **Mono Build (if used):** `Build\StableProjectorz.exe`

## Troubleshooting

### If Build Fails with "IL2CPP not installed":
1. Open Unity Hub
2. Go to **Installs** tab
3. Find the Unity version listed in `ProjectSettings/ProjectVersion.txt`
4. Click gear icon (⚙️) → **Add modules**
5. Check **Windows Build Support (IL2CPP)**
6. Click **Install**
7. Wait for installation, then try building again

### If Unity Editor Won't Open:
- Make sure no other Unity instance is running
- Check that Unity 6 (6000.x) is properly installed
- Try opening from Unity Hub instead of directly

### If you see "License error" / "No valid Unity Editor license found":
- Unity Personal is free (students, hobbyists, small devs). Open this project once in the Editor (Hub → Open), let it load fully, then close. That activates your license; after that, builds (including `build_for_testing.bat`) should work.

## Build Requirements Checklist

- ✅ Unity 6 (6000.x) installed
- ✅ IL2CPP module installed (you said this is resolved)
- ✅ Visual Studio 2022 with C++ Build Tools
- ✅ Project configured for IL2CPP (already done)

## Notes

- **Development Build:** Keep unchecked for performance (as per README)
- **Build Time:** First build may take 10-30 minutes depending on your system
- **Subsequent Builds:** Will be faster due to caching

---

**Ready to build!** On Windows, double-click **`build_for_testing.bat`**, or open Unity and press `Ctrl + Shift + B`.
