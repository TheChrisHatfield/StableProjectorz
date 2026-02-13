# Unity Hub 3.16.1 Installation Locations

## ✅ Found Unity Hub 3.16.1

**Your Unity Hub Location:**
```
D:\PROGRAM_FILES\UNITY_HUB\Unity Hub\Unity Hub.exe
```

**Version:** 3.16.1.0 ✓

## Standard Installation Paths

Unity Hub 3.16.1 is typically installed in one of these locations on Windows:

### Primary Location (Most Common)
```
C:\Users\<YourUsername>\AppData\Local\Programs\Unity Hub\Unity Hub.exe
```

For your system, this would be:
```
C:\Users\YoDaddy\AppData\Local\Programs\Unity Hub\Unity Hub.exe
```

### Alternative Locations
```
C:\Program Files\Unity Hub\Unity Hub.exe
C:\Program Files (x86)\Unity Hub\Unity Hub.exe
```

## Quick Access Methods

### Method 1: Start Menu
1. Press `Windows Key`
2. Type "Unity Hub"
3. Click on the Unity Hub application

### Method 2: Run Dialog
1. Press `Windows Key + R`
2. Type: `%LOCALAPPDATA%\Programs\Unity Hub\Unity Hub.exe`
3. Press Enter

### Method 3: File Explorer
1. Open File Explorer
2. Navigate to: `C:\Users\YoDaddy\AppData\Local\Programs\Unity Hub\`
3. Double-click `Unity Hub.exe`

### Method 4: PowerShell Command
```powershell
& "$env:LOCALAPPDATA\Programs\Unity Hub\Unity Hub.exe"
```

## If Unity Hub is Not Found

If Unity Hub is not installed, you can:

1. **Download Unity Hub:**
   - Visit: https://unity.com/download
   - Or direct link: https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe

2. **Install Unity Hub:**
   - Run the installer
   - It will typically install to: `C:\Users\<YourUsername>\AppData\Local\Programs\Unity Hub\`

## Unity Hub Data Locations

Even if the executable isn't found, Unity Hub stores data in:

- **Settings/Config:** `C:\Users\YoDaddy\AppData\Roaming\UnityHub\`
- **Updater:** `C:\Users\YoDaddy\AppData\Local\unityhub-updater\`
- **Unity Installations:** Usually in `C:\Program Files\Unity\` or `D:\PROGRAM_FILES\UNITY\` (as seen in your logs)

## Finding Unity Hub via Registry

You can also check the registry:
```
HKEY_CURRENT_USER\SOFTWARE\Unity Technologies\Unity Hub
```

## Quick Check Script

Run this PowerShell command to check all common locations:

```powershell
$locations = @(
    "$env:LOCALAPPDATA\Programs\Unity Hub\Unity Hub.exe",
    "C:\Program Files\Unity Hub\Unity Hub.exe",
    "C:\Program Files (x86)\Unity Hub\Unity Hub.exe"
)

foreach ($loc in $locations) {
    if (Test-Path $loc) {
        Write-Host "✓ Found: $loc" -ForegroundColor Green
        $version = (Get-Item $loc).VersionInfo.FileVersion
        Write-Host "  Version: $version" -ForegroundColor Cyan
    }
}
```

## After Finding Unity Hub

Once you locate Unity Hub, you can:

1. **Open Unity Hub**
2. **Go to Installs tab**
3. **Find Unity 6000.2.6f2**
4. **Click the gear icon (⚙️) → Add modules**
5. **Check "Windows Build Support (IL2CPP)"**
6. **Click Install**

This will install the missing IL2CPP module needed to build your StableProjectorz project.
