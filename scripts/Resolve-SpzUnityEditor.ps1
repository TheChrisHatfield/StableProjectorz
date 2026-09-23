# Resolves Unity.exe for CLI builds (Hub install, project version, env overrides).
# Used by build_for_testing.ps1 and batch fallbacks.

function Get-SpzProjectEditorVersion {
    param([string]$ProjectRoot)
    $versionFile = Join-Path $ProjectRoot "ProjectSettings\ProjectVersion.txt"
    if (-not (Test-Path $versionFile -ErrorAction SilentlyContinue)) {
        return $null
    }
    foreach ($line in Get-Content $versionFile -ErrorAction SilentlyContinue) {
        if ($line -match '^\s*m_EditorVersion:\s*(.+)\s*$') {
            return $matches[1].Trim()
        }
    }
    return $null
}

function Resolve-SpzUnityEditor {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot
    )

    $ProjectRoot = $ProjectRoot.TrimEnd('\', '/')

    $localOverride = Join-Path $ProjectRoot "build_for_testing.local.ps1"
    if (Test-Path $localOverride -ErrorAction SilentlyContinue) {
        . $localOverride
    }

    if ($SpzUnityEditorPath -and (Test-Path $SpzUnityEditorPath -ErrorAction SilentlyContinue)) {
        return (Resolve-Path $SpzUnityEditorPath).Path
    }

    if ($env:UNITY_EXE -and (Test-Path $env:UNITY_EXE -ErrorAction SilentlyContinue)) {
        return (Resolve-Path $env:UNITY_EXE).Path
    }

    $editorVersion = Get-SpzProjectEditorVersion -ProjectRoot $ProjectRoot
    if (-not $editorVersion) {
        throw @"
ProjectSettings\ProjectVersion.txt has no m_EditorVersion.
Set environment variable UNITY_EXE, or copy build_for_testing.local.ps1.example to build_for_testing.local.ps1 and set `$SpzUnityEditorPath.
"@
    }

    # Exact Hub folder only. A different editor version rewrites ProjectSettings / Library.
    $hubRoots = @()
    foreach ($envName in @("ProgramW6432", "ProgramFiles", "ProgramFiles(x86)")) {
        $base = [Environment]::GetEnvironmentVariable($envName)
        if ($base) {
            $hubRoots += (Join-Path $base "Unity\Hub\Editor")
        }
    }
    $seen = @{}
    foreach ($hubRoot in $hubRoots) {
        if ($seen.ContainsKey($hubRoot)) { continue }
        $seen[$hubRoot] = $true
        $hubExact = Join-Path $hubRoot "$editorVersion\Editor\Unity.exe"
        if (Test-Path -LiteralPath $hubExact -PathType Leaf -ErrorAction SilentlyContinue) {
            return (Resolve-Path -LiteralPath $hubExact).Path
        }
    }

    throw @"
Unity $editorVersion was not found under Unity Hub (Program Files).
Install that exact editor version. A different Unity version is not used automatically.
Or set UNITY_EXE, or copy build_for_testing.local.ps1.example to build_for_testing.local.ps1 and set `$SpzUnityEditorPath.
"@
}
