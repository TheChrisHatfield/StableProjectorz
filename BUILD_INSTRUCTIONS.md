# How to Build StableProjectorz

## Quick Build Steps

Since you've resolved the IL2CPP installation, here's how to build:

### Method 1: Using Unity Editor (Recommended)

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

### Method 2: One-click script (Windows)

Double-click **`build_for_testing.bat`** (or run `.\build_for_testing.ps1`). The script finds Unity using `ProjectSettings/ProjectVersion.txt` and the Unity Hub install folder under Program Files.

**Machine-specific path (optional, not for GitHub):** copy `build_for_testing.local.ps1.example` to `build_for_testing.local.ps1` and set `$SpzUnityEditorPath`, or set environment variable `UNITY_EXE`.

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
3. Find Unity 6000.2.6f2
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

**Ready to build!** Open Unity Editor and press `Ctrl + Shift + B` to get started.
