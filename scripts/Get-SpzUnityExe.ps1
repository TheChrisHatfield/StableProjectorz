# Prints resolved Unity.exe path to stdout (for .bat fallbacks). Exit 0 on success, 1 on failure.
$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "Resolve-SpzUnityEditor.ps1")
try {
    $exe = Resolve-SpzUnityEditor -ProjectRoot $ProjectRoot
    Write-Output $exe
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
