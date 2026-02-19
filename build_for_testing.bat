@echo off
REM StableProjectorz - Build for testing (Win64)
REM Close Unity Editor AND Unity Hub (for this project) before running.
REM If nothing seems to happen: open cmd, cd to this folder, run build_for_testing.bat

setlocal
cd /d "%~dp0"

set UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe
set "PROJECT_PATH=%~dp0"
REM Unity -projectPath must NOT have trailing backslash (can cause exit 1)
set "PROJECT_PATH_NO_TRAIL=%PROJECT_PATH:~0,-1%"
set BUILD_PATH=%PROJECT_PATH%Build_IL2CPP\StableProjectorz.exe
set LOG_FILE=%PROJECT_PATH%build_output.txt
set LOG_FILE_TEMP=%PROJECT_PATH%build_log_temp.txt

if not exist "%UNITY_EXE%" (
    echo ERROR: Unity not found at %UNITY_EXE%
    pause
    exit /b 1
)

echo Building StableProjectorz for testing...
echo Project: %PROJECT_PATH%
echo Output: %BUILD_PATH%
echo Log: %LOG_FILE%
echo.

REM Optional: remove old Data for a clean rebuild. Skipping by default so a failed build leaves a runnable exe+Data.
set BUILD_DIR=%PROJECT_PATH%Build_IL2CPP
if /i "%~1"=="clean" (
    if exist "%BUILD_DIR%\StableProjectorz_Data" (
        echo Removing old Data folder for clean rebuild...
        rd /s /q "%BUILD_DIR%\StableProjectorz_Data" 2>nul
    )
    if exist "%BUILD_DIR%\StableProjectorz_BackUpThisFolder_ButDontShipItWithYourGame" (
        rd /s /q "%BUILD_DIR%\StableProjectorz_BackUpThisFolder_ButDontShipItWithYourGame" 2>nul
    )
) else (
    echo Keeping existing Data folder; use "build_for_testing.bat clean" for a full clean rebuild.
)

REM Remove project lock file so batch mode can open the project (fixes exit code 1 when no Editor is open)
set LOCK_FILE=%PROJECT_PATH%Library\lock
if exist "%LOCK_FILE%" (
    echo Removing stale Library\lock so Unity batch mode can open the project...
    del /f /q "%LOCK_FILE%" 2>nul
)
echo.

echo This may take 10-30 minutes. Do not open this project in Unity Editor while building.
echo.
echo Launching Unity in batch mode ^(no window - check Task Manager for Unity.exe if unsure^)...
echo.

REM Unity 6: use -executeMethod (build script) instead of deprecated -buildWindows64Player to avoid exit code 1
"%UNITY_EXE%" -batchmode -quit -projectPath "%PROJECT_PATH_NO_TRAIL%" -executeMethod BuildForTesting.BuildWin64 -logFile "%LOG_FILE_TEMP%"
set UNITY_EXIT=%ERRORLEVEL%

if exist "%LOG_FILE_TEMP%" (
    copy /y "%LOG_FILE_TEMP%" "%LOG_FILE%" >nul
    del /f /q "%LOG_FILE_TEMP%" 2>nul
)

echo.
echo Unity process finished. Exit code: %UNITY_EXIT%

REM Unity sometimes returns non-zero even when build succeeded; if exe exists, treat as success
set "DATA_FOLDER=%BUILD_DIR%\StableProjectorz_Data"
if exist "%BUILD_PATH%" (
    echo.
    echo Build succeeded: %BUILD_PATH%
    for %%F in ("%BUILD_PATH%") do echo Exe timestamp: %%~tF  ^(run this exe to test^)
    if not exist "%DATA_FOLDER%" (
        echo.
        echo WARNING: StableProjectorz_Data folder is missing next to the exe.
        echo The game will show "Data folder not found" until you run a full rebuild.
        echo Close Unity Editor and run this script again to get a complete build.
    ) else (
        echo Data folder: %DATA_FOLDER%  ^(OK^)
        echo Run the exe from: %BUILD_DIR%
        echo For addons: use Run_with_Addons.bat so Python is on PATH when the game starts.
    )
    if %UNITY_EXIT% NEQ 0 (
        echo Unity exit code was %UNITY_EXIT% ^(exe was still created^)
    )
) else (
    echo.
    echo ERROR: No exe was produced. Unity exit code: %UNITY_EXIT%
    echo.
    echo Exit code 1: project path, lock file, or log file issue.
    echo - build_output.txt was updated with this run; open it and check the first 50 lines for the real error.
    echo - Close build_output.txt if you had it open, then run this bat again.
    echo - Or close Unity/Hub, delete Library\lock, then run again.
    pause
    exit /b 1
)

echo.
pause
endlocal
