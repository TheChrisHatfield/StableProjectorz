# Prints resolved Unity.exe path to stdout. Exit 0 on success, 1 on failure.
# Used by build_minimal_test.bat. build_for_testing.ps1 calls Resolve-SpzUnityEditor directly.
$ErrorActionPreference = "Continue"
$ProjectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "Resolve-SpzUnityEditor.ps1")
try {
    $exe = Resolve-SpzUnityEditor -ProjectRoot $ProjectRoot
    Write-Output $exe
    exit 0
} catch {
    # Write-Error is terminating when ErrorAction is Stop, and that skips exit 1.
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
