# How to Build StableProjectorz

## Quick Build Steps

Since you've resolved the IL2CPP installation, here's how to build:

### Method 1: Using Unity Editor (Recommended)

1. **Open Unity Hub:**
   ```
   D:\PROGRAM_FILES\UNITY_HUB\Unity Hub\Unity Hub.exe
   ```

2. **Open the Project:**
   - In Unity Hub, click "Open" or "Add"
   - Navigate to: `D:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\StableProjectorz`
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

### Method 2: Command Line Build (If Unity.exe is accessible)

If you can locate Unity.exe, you can build from command line:

```powershell
& "D:\PROGRAM_FILES\UNITY\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "D:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\StableProjectorz" `
  -buildTarget Win64 `
  -buildWindows64Player "D:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\StableProjectorz\Build_IL2CPP\StableProjectorz.exe" `
  -logFile "build_output.txt"
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
- Check that Unity 6000.2.6f2 is properly installed
- Try opening from Unity Hub instead of directly

## Build Requirements Checklist

- ✅ Unity 6000.2.6f2 installed
- ✅ IL2CPP module installed (you said this is resolved)
- ✅ Visual Studio 2022 with C++ Build Tools
- ✅ Project configured for IL2CPP (already done)

## Notes

- **Development Build:** Keep unchecked for performance (as per README)
- **Build Time:** First build may take 10-30 minutes depending on your system
- **Subsequent Builds:** Will be faster due to caching

---

**Ready to build!** Open Unity Editor and press `Ctrl + Shift + B` to get started.
