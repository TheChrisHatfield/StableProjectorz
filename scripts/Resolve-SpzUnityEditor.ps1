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

    if ($editorVersion) {
        $hubExact = Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
        if (Test-Path $hubExact -ErrorAction SilentlyContinue) {
            return (Resolve-Path $hubExact).Path
        }
    }

    $hubRoot = Join-Path ${env:ProgramFiles} "Unity\Hub\Editor"
    if (Test-Path $hubRoot -ErrorAction SilentlyContinue) {
        $installs = Get-ChildItem $hubRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object { $_.Name } -Descending

        if ($editorVersion) {
            foreach ($dir in $installs) {
                if ($dir.Name -ne $editorVersion) { continue }
                $exe = Join-Path $dir.FullName "Editor\Unity.exe"
                if (Test-Path $exe -ErrorAction SilentlyContinue) {
                    return (Resolve-Path $exe).Path
                }
            }

            $major = ($editorVersion -split '\.')[0]
            foreach ($dir in $installs) {
                if ($dir.Name -notlike "$major*") { continue }
                $exe = Join-Path $dir.FullName "Editor\Unity.exe"
                if (Test-Path $exe -ErrorAction SilentlyContinue) {
                    Write-Warning "Using Unity $($dir.Name); project requests $editorVersion (install matching version via Hub when possible)."
                    return (Resolve-Path $exe).Path
                }
            }
        }

        foreach ($dir in $installs) {
            $exe = Join-Path $dir.FullName "Editor\Unity.exe"
            if (Test-Path $exe -ErrorAction SilentlyContinue) {
                Write-Warning "Using newest Hub Unity $($dir.Name) (no ProjectVersion match)."
                return (Resolve-Path $exe).Path
            }
        }
    }

    $hint = if ($editorVersion) { " Install Unity $editorVersion via Unity Hub." } else { "" }
    throw @"
Unity Editor not found.$hint
Set environment variable UNITY_EXE, or copy build_for_testing.local.ps1.example to build_for_testing.local.ps1 and set `$SpzUnityEditorPath.
"@
}
