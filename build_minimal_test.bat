@echo off
REM Minimal test: find where the crash happens. Run this and then check build_debug.txt.
REM If you see STEP 0,1,2 - crash is elsewhere. If you see only STEP 0,1 - Unity or "start" is killing the window.
setlocal
cd /d "%~dp0"
set "DEBUG_LOG=%~dp0build_debug.txt"
set "LOG_TEMP=%~dp0build_log_temp.txt"

if not defined UNITY_EXE (
    for /f "usebackq delims=" %%U in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Get-SpzUnityExe.ps1"`) do set "UNITY_EXE=%%U"
)
if not defined UNITY_EXE (echo Unity not found. Set UNITY_EXE or build_for_testing.local.ps1 & pause & exit /b 1)
if not exist "%UNITY_EXE%" (echo Unity not found at "%UNITY_EXE%" & pause & exit /b 1)

set "PROJECT_PATH_NO_TRAIL=%~dp0"
set "PROJECT_PATH_NO_TRAIL=%PROJECT_PATH_NO_TRAIL:~0,-1%"

REM Each STEP written via cmd /c so the write is flushed (separate process)
cmd /c echo [%date% %time%] STEP 0: minimal test started >> "%DEBUG_LOG%"
cmd /c echo [%date% %time%] STEP 1: about to run Unity via cmd /c >> "%DEBUG_LOG%"

echo Running Unity... If the window closes, check build_debug.txt. STEP 2 means Unity returned.

REM Run Unity directly. If window closes before STEP 2, Unity (or starting it) is the cause.
"%UNITY_EXE%" -batchmode -quit -projectPath "%PROJECT_PATH_NO_TRAIL%" -executeMethod BuildForTesting.BuildWin64 -logFile "%LOG_TEMP%"
set UNITY_EXIT=%ERRORLEVEL%

cmd /c echo [%date% %time%] STEP 2: Unity finished exit=%UNITY_EXIT% >> "%DEBUG_LOG%"
echo Unity exit code: %UNITY_EXIT%
pause
