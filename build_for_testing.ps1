# StableProjectorz - Build for testing (Win64)
# Run: .\build_for_testing.ps1  or  .\build_for_testing.ps1 -Clean
# Or double-click build_for_testing.bat

param([switch]$Clean)

$ErrorActionPreference = "Continue"
$ProjectRoot = $PSScriptRoot
$ProjectPath = $ProjectRoot.TrimEnd('\', '/')
$BuildPath = Join-Path $ProjectRoot "Build_IL2CPP\StableProjectorz.exe"
$LogFile = Join-Path $ProjectRoot "build_output.txt"
$LogTemp = Join-Path $ProjectRoot "build_log_temp.txt"
$LogRef = Join-Path $ProjectRoot "build_output_reference.txt"
$BuildDir = Join-Path $ProjectRoot "Build_IL2CPP"
$LockFile = Join-Path $ProjectRoot "Library\lock"

# Find Unity
$UnityExe = $env:UNITY_EXE
if (-not $UnityExe -or -not (Test-Path $UnityExe -ErrorAction SilentlyContinue)) {
    $UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe"
}
if (-not (Test-Path $UnityExe -ErrorAction SilentlyContinue)) {
    $UnityExe = "D:\PROGRAM_FILES\UNITY\Editor\Unity.exe"
}
if (-not (Test-Path $UnityExe -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: Unity not found. Set UNITY_EXE or install to default path."
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host ""
Write-Host "Building StableProjectorz for testing..."
Write-Host "Project: $ProjectPath"
Write-Host "Output: $BuildPath"
Write-Host "Log: $LogFile"
Write-Host ""

# Close running build exe so the next build can overwrite (avoids "user-mapped section open")
$procs = Get-Process -Name "StableProjectorz" -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "Stopping running StableProjectorz.exe ($($procs.Count) process(es))..."
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Host "Done."
}

if ($Clean) {
    $dataDir = Join-Path $BuildDir "StableProjectorz_Data"
    $backupDir = Join-Path $BuildDir "StableProjectorz_BackUpThisFolder_ButDontShipItWithYourGame"
    if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path $backupDir) { Remove-Item $backupDir -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host "Clean: removed Data and BackUp folders."
} else {
    Write-Host "Keeping existing Data. Use -Clean for full clean rebuild."
}

if (Test-Path $LockFile -ErrorAction SilentlyContinue) {
    Remove-Item $LockFile -Force -ErrorAction SilentlyContinue
    Write-Host "Removed Library\lock"
}

Write-Host ""
Write-Host "This may take 10-30 minutes. Do not open this project in Unity Editor while building."
Write-Host "Build indicators: progress bar updates every 3s; 'Still building...' heartbeat every ~15s."
Write-Host "When done you will see: *** BUILD FINISHED. Unity exit code: N ***"
Write-Host ""

# If the Editor (or any Unity) already has this project, batchmode often exits immediately
# (tiny log, no "[BuildForTesting]" lines, return code 1).
if (-not $env:SPZ_BUILD_ALLOW_UNITY_RUNNING) {
    $unityProcs = Get-Process -Name "Unity" -ErrorAction SilentlyContinue
    if ($unityProcs) {
        Write-Host "ERROR: Unity is already running (PID(s): $(($unityProcs | ForEach-Object { $_.Id }) -join ', '))."
        Write-Host "Close the Unity Editor (and any other Unity using this project), then run the build again."
        Write-Host "Advanced: set env SPZ_BUILD_ALLOW_UNITY_RUNNING=1 to skip this check (build may still fail)."
        Write-Host ""
        Read-Host "Press Enter to close"
        exit 1
    }
}

# Reference log size for progress estimation
$refSize = 0
if (Test-Path $LogRef -ErrorAction SilentlyContinue) {
    $refSize = (Get-Item $LogRef -ErrorAction SilentlyContinue).Length
}
$estTotalSec = 25 * 60
$maxWaitMin = 40

function Test-BuildSucceededFromLog {
    param([string]$Path)
    if (-not (Test-Path $Path -ErrorAction SilentlyContinue)) { return $false }
    try {
        $hits = Select-String -Path $Path -Pattern "\[BuildForTesting\] Build succeeded\.|Build Finished, Result: Success\." -SimpleMatch:$false -ErrorAction SilentlyContinue
        return ($null -ne $hits -and $hits.Count -gt 0)
    } catch {
        return $false
    }
}

function Wait-ForBuildExe {
    param(
        [string]$ExactPath,
        [string]$BuildDir,
        [int]$TimeoutSeconds = 45
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if (Test-Path $ExactPath -ErrorAction SilentlyContinue) {
            return $ExactPath
        }
        # Fallback: resolve any produced game exe (exclude crash handler).
        if (Test-Path $BuildDir -ErrorAction SilentlyContinue) {
            $exe = Get-ChildItem $BuildDir -Filter *.exe -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ne "UnityCrashHandler64.exe" } |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1
            if ($exe -and (Test-Path $exe.FullName -ErrorAction SilentlyContinue)) {
                return $exe.FullName
            }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function RunUnityBuild {
    param([string]$ExtraArgs)
    
    $arguments = "$ExtraArgs -quit -projectPath `"$ProjectPath`" -executeMethod BuildForTesting.BuildWin64 -logFile `"$LogTemp`""
    Write-Host "  Unity command: $UnityExe $arguments"
    Write-Host ""

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $UnityExe
    $psi.Arguments = $arguments
    $psi.WorkingDirectory = $ProjectPath
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $false

    $p = $null
    try {
        $p = [System.Diagnostics.Process]::Start($psi)
    } catch {
        Write-Host "ERROR: Failed to start Unity: $_"
        return -1
    }

    Write-Host "  Build started (PID $($p.Id)). Heartbeat below..."
    Write-Host ""

    $start = Get-Date
    $heartbeat = 0
    $code = $null

    while ($p -and -not $p.HasExited) {
        $elapsedMin = ((Get-Date) - $start).TotalMinutes
        if ($elapsedMin -ge $maxWaitMin) {
            Write-Host ""
            Write-Host "  (Timeout: $maxWaitMin min - Unity still running; stopping process. Build did not finish in time.)"
            try {
                if (-not $p.HasExited) {
                    $p.Kill()
                    $p.WaitForExit(60000) | Out-Null
                }
            } catch {
                Write-Host "  (Could not terminate Unity process: $_)"
            }
            $code = 124
            break
        }

        $curSize = 0
        if (Test-Path $LogTemp -ErrorAction SilentlyContinue) {
            $curSize = (Get-Item $LogTemp -ErrorAction SilentlyContinue).Length
        }

        if ($refSize -gt 1000 -and $curSize -gt 0) {
            $pct = [Math]::Min(95, [int](($curSize / $refSize) * 100))
            $status = "Log ${pct}% (vs reference)"
        } else {
            $elapsed = ((Get-Date) - $start).TotalSeconds
            $pct = [Math]::Min(95, [int](($elapsed / $estTotalSec) * 100))
            $status = "Est. ${pct}% (time)"
        }

        $barWidth = 50
        $filled = [int]($pct / 100 * $barWidth)
        $empty = $barWidth - $filled
        $bar = ("[" + ("=" * $filled) + (" " * $empty) + "]")
        Write-Host ("`r  $bar $status - do not close") -NoNewline

        $heartbeat++
        if ($heartbeat -ge 5) {
            $min = [int]$elapsedMin
            $kb = [int]($curSize / 1024)
            Write-Host ""
            Write-Host "  Still building... $min min elapsed, log $kb KB. Unity running (PID $($p.Id))."
            $heartbeat = 0
        }

        Start-Sleep -Seconds 3
    }

    # Clear the in-place progress line (\r + NoNewline) so following output is readable.
    Write-Host ""

    if ($null -eq $code -and $p) {
        try {
            if (-not $p.HasExited) { $p.WaitForExit() }
            $code = $p.ExitCode
        } catch {
            $code = 1
        }
        if ($null -eq $code) { $code = 0 }
    }

    if ($p) {
        try { $p.Dispose() } catch { }
    }

    return $code
}

# First attempt: batch mode
Write-Host "Launching Unity in batch mode..."
$exitCode = RunUnityBuild "-batchmode"

# If 198 (license/headless entitlement issue), retry without batchmode
if ($exitCode -eq 198) {
    Write-Host ""
    Write-Host "  Exit 198: Unity batch mode license check failed."
    Write-Host "  Retrying WITHOUT -batchmode (Unity window will open briefly)..."
    Write-Host ""
    if (Test-Path $LogTemp -ErrorAction SilentlyContinue) {
        Remove-Item $LogTemp -Force -ErrorAction SilentlyContinue
    }
    $exitCode = RunUnityBuild ""
}

# Copy log
if (Test-Path $LogTemp -ErrorAction SilentlyContinue) {
    Copy-Item $LogTemp $LogFile -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host ""
Write-Host "  *** BUILD FINISHED. Unity exit code: $exitCode ***"
Write-Host ""

# Immediate exit with almost no log = project lock / second instance, not a compile error.
if ($exitCode -ne 0 -and (Test-Path $LogFile -ErrorAction SilentlyContinue)) {
    $logLen = (Get-Item $LogFile -ErrorAction SilentlyContinue).Length
    $hasBuildMarker = $false
    try {
        $hasBuildMarker = $null -ne (Select-String -Path $LogFile -SimpleMatch "[BuildForTesting]" -ErrorAction SilentlyContinue | Select-Object -First 1)
    } catch { }
    if ($logLen -lt 4096 -and -not $hasBuildMarker) {
        Write-Host ""
        Write-Host "  Hint: Log is very short and BuildForTesting never started. Usually the Unity Editor"
        Write-Host "  still has this project open, or a stuck Unity process holds the Library lock."
        Write-Host "  Close all Unity windows for this project, end any stray Unity.exe in Task Manager if needed, then rebuild."
        Write-Host ""
    }
}

# Ground truth: BuildForTesting success marker in log (preferred), then exit code fallback.
$logIndicatesSuccess = Test-BuildSucceededFromLog $LogFile
if (-not $logIndicatesSuccess -and (Test-Path $LogTemp -ErrorAction SilentlyContinue)) {
    $logIndicatesSuccess = Test-BuildSucceededFromLog $LogTemp
}
$resolvedBuildPath = $BuildPath
if ($logIndicatesSuccess) {
    $waited = Wait-ForBuildExe -ExactPath $BuildPath -BuildDir $BuildDir -TimeoutSeconds 60
    if ($waited) { $resolvedBuildPath = $waited }
}
$buildLooksSuccessful = $logIndicatesSuccess -or ($exitCode -eq 0)
if (Test-Path $LogTemp -ErrorAction SilentlyContinue) {
    Remove-Item $LogTemp -Force -ErrorAction SilentlyContinue
}

if ($buildLooksSuccessful -and (Test-Path $resolvedBuildPath -ErrorAction SilentlyContinue)) {
    if ($exitCode -ne 0 -and $logIndicatesSuccess) {
        Write-Host "Note: Unity exited with code $exitCode, but build log reports success. Treating as successful build."
    }
    if ($resolvedBuildPath -ne $BuildPath) {
        Write-Host "Note: expected output path differed; resolved build exe at: $resolvedBuildPath"
    }
    Write-Host "Build succeeded: $resolvedBuildPath"
    $ts = (Get-Item $resolvedBuildPath).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
    Write-Host "Exe timestamp: $ts"
    $dataFolder = Join-Path $BuildDir "StableProjectorz_Data"
    if (Test-Path $dataFolder) { Write-Host "Data folder: OK" }
    else { Write-Host "WARNING: StableProjectorz_Data missing." }
    if (Test-Path $LogFile -ErrorAction SilentlyContinue) {
        Copy-Item $LogFile $LogRef -Force -ErrorAction SilentlyContinue
    }
    Write-Host ""
    Write-Host "Done. Build completed successfully."
    Write-Host ""
    Read-Host "Press Enter to close"
    exit 0
}

if (Test-Path $BuildPath -ErrorAction SilentlyContinue) {
    Write-Host "WARNING: Unity exited with code $exitCode but exe exists (may be from a previous build)."
    Write-Host "Exe: $BuildPath"
    Write-Host "Check $LogFile for errors (e.g. script compile failure, build failure)."
    Write-Host ""
    if (Test-Path $LogFile -ErrorAction SilentlyContinue) {
        Write-Host "--- Last 15 lines of build_output.txt ---"
        try {
            $lines = Get-Content $LogFile -Tail 15 -ErrorAction SilentlyContinue
            foreach ($line in $lines) { Write-Host "  $line" }
        } catch { }
        Write-Host "--- end ---"
    }
    Write-Host ""
    Read-Host "Press Enter to close"
    exit 1
}

# Build failed - no exe or Unity failed
Write-Host "ERROR: No exe produced. Unity exit code: $exitCode"
Write-Host ""
if (Test-Path $LogFile -ErrorAction SilentlyContinue) {
    Write-Host "--- Last 15 lines of build_output.txt ---"
    try {
        $lines = Get-Content $LogFile -Tail 15 -ErrorAction SilentlyContinue
        foreach ($line in $lines) { Write-Host "  $line" }
    } catch {
        Write-Host "  (Could not read log file)"
    }
    Write-Host "--- end ---"
    Write-Host ""
    Write-Host "Full log: $LogFile"
} else {
    Write-Host "No log file was produced. Unity may have crashed before writing any output."
}
Write-Host ""
Read-Host "Press Enter to close"
exit 1
