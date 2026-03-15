@echo off
cd /d "%~dp0"

REM Launches PowerShell build script. Double-click or: build_for_testing.bat [clean]
REM If PowerShell not found, falls back to running Unity directly.

if /i "%~1"=="clean" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_for_testing.ps1" -Clean
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_for_testing.ps1"
)
if "%ERRORLEVEL%"=="9009" goto RunUnityDirectly
exit /b %ERRORLEVEL%

:RunUnityDirectly
echo PowerShell not available. Running Unity directly...
set "PROJECT_PATH_NO_TRAIL=%~dp0"
set "PROJECT_PATH_NO_TRAIL=%PROJECT_PATH_NO_TRAIL:~0,-1%"
set "LOG_TEMP=%~dp0build_log_temp.txt"
set "LOG_FILE=%~dp0build_output.txt"
if not defined UNITY_EXE set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe"
if not exist "%UNITY_EXE%" set "UNITY_EXE=D:\PROGRAM_FILES\UNITY\Editor\Unity.exe"
if not exist "%UNITY_EXE%" (echo ERROR: Unity not found. & pause & exit /b 1)
if exist "%~dp0Library\lock" del /f /q "%~dp0Library\lock" 2>nul
echo Launching Unity...
"%UNITY_EXE%" -batchmode -quit -projectPath "%PROJECT_PATH_NO_TRAIL%" -executeMethod BuildForTesting.BuildWin64 -logFile "%LOG_TEMP%"
set "UNITY_EXIT=%ERRORLEVEL%"
if exist "%LOG_TEMP%" copy /y "%LOG_TEMP%" "%LOG_FILE%" >nul
echo.
echo *** BUILD FINISHED. Unity exit code: %UNITY_EXIT% ***
echo.
if exist "%~dp0Build_IL2CPP\StableProjectorz.exe" (
    echo Build succeeded.
    pause
    exit /b 0
)
echo ERROR: No exe produced. Check build_output.txt for errors.
echo.
if exist "%LOG_FILE%" (
    echo --- Last lines of build_output.txt ---
    powershell -NoProfile -Command "Get-Content '%LOG_FILE%' -Tail 10" 2>nul
    echo --- end ---
)
pause
exit /b 1
