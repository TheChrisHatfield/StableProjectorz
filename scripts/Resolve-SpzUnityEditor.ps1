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

function ConvertTo-SpzRootedPath {
    param([string]$ProjectRoot, [string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        return (Join-Path $ProjectRoot $Path)
    }
    return $Path
}

function Test-SpzUnityExecutable {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf -ErrorAction SilentlyContinue)) { return $false }
    return ([System.IO.Path]::GetFileName($Path) -eq "Unity.exe")
}

function Resolve-SpzUnityEditor {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot
    )

    $ProjectRoot = $ProjectRoot.TrimEnd('\', '/')

    $localOverride = Join-Path $ProjectRoot "build_for_testing.local.ps1"
    $overridePath = $null
    if (Test-Path -LiteralPath $localOverride -PathType Leaf -ErrorAction SilentlyContinue) {
        # Child scope so the override file cannot replace $ProjectRoot or other locals.
        $overridePath = & {
            $SpzUnityEditorPath = $null
            . $localOverride
            $SpzUnityEditorPath
        }
    }

    if ($overridePath) {
        $overridePath = ConvertTo-SpzRootedPath -ProjectRoot $ProjectRoot -Path $overridePath
        if (Test-SpzUnityExecutable $overridePath) {
            return (Resolve-Path -LiteralPath $overridePath).Path
        }
        Write-Warning "build_for_testing.local.ps1 set SpzUnityEditorPath to '$overridePath', which is not an existing Unity.exe. Ignoring it."
    }

    if ($env:UNITY_EXE) {
        $envUnity = ConvertTo-SpzRootedPath -ProjectRoot $ProjectRoot -Path $env:UNITY_EXE
        if (Test-SpzUnityExecutable $envUnity) {
            return (Resolve-Path -LiteralPath $envUnity).Path
        }
        Write-Warning "UNITY_EXE is '$envUnity', which is not an existing Unity.exe. Ignoring it."
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
