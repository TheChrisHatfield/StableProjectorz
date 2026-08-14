@echo off
REM Launches StableProjectorz with Python on PATH. The exe also auto-starts the addon server
REM when Python is found in common locations (dual-trigger). Use this if Python is in a custom
REM location or "Load addons now" fails with "Cannot connect to destination host".
REM
REM Args:
REM   wait              - run exe in foreground and pause on crash (debug)
REM   <pid>             - wait for that PID to exit, then launch (used by in-app "Restart with addons")
REM   wait <pid>        - same wait-for-PID then foreground run
setlocal
cd /d "%~dp0"

set "WAIT_PID="
set "FOREGROUND="
if /i "%~1"=="wait" set "FOREGROUND=1"
if /i "%~1"=="wait" if not "%~2"=="" set "WAIT_PID=%~2"
if /i not "%~1"=="wait" if not "%~1"=="" set "WAIT_PID=%~1"

REM In-app restart launches this bat hidden — never block on pause (no console input).
if not "%WAIT_PID%"=="" set "SPZ_ADDONS_NONINTERACTIVE=1"

if "%WAIT_PID%"=="" goto after_wait_pid
echo Waiting for StableProjectorz PID %WAIT_PID% to exit before relaunch...
:wait_old_exit
tasklist /FI "PID eq %WAIT_PID%" 2>nul | find "%WAIT_PID%" >nul
if errorlevel 1 goto wait_old_done
REM ping works when stdin is redirected; timeout does not (CREATE_NO_WINDOW / PowerShell).
ping -n 2 127.0.0.1 >nul
goto wait_old_exit
:wait_old_done
REM Brief settle so sockets/GPU from the old process can release.
ping -n 2 127.0.0.1 >nul
:after_wait_pid

set "EXE=Build_IL2CPP\StableProjectorz.exe"
if exist "%EXE%" goto exe_ok
echo ERROR: %EXE% not found. Build the project first with build_for_testing.bat
if not defined SPZ_ADDONS_NONINTERACTIVE pause
exit /b 1
:exe_ok

REM Ensure Python is on PATH so Unity can start addon_server.py (port 5555 + HTTP 5557)
set "PYTHON_ADDED="
where python >nul 2>&1 && set "PYTHON_ADDED=1"
if not defined PYTHON_ADDED where py >nul 2>&1 && set "PYTHON_ADDED=1"
if defined PYTHON_ADDED goto found_python
for %%P in (
    "%LOCALAPPDATA%\Programs\Python\Python312\python.exe"
    "%LOCALAPPDATA%\Programs\Python\Python311\python.exe"
    "%LOCALAPPDATA%\Programs\Python\Python310\python.exe"
    "%ProgramFiles%\Python312\python.exe"
    "%ProgramFiles%\Python311\python.exe"
    "%ProgramFiles%\Python310\python.exe"
) do if exist %%P (
    for %%D in ("%%~dpP.") do set "PYDIR=%%~fD"
    set "PATH=%PYDIR%;%PATH%"
    set "PYTHON_ADDED=1"
    echo Using Python: %%P
    goto found_python
)
:found_python
if defined PYTHON_ADDED goto python_ready
echo.
echo WARNING: Python was not found. Addons will not work.
echo Install Python 3.10+ and add it to PATH, or run this script from a shell where "python" works.
echo.
if not defined SPZ_ADDONS_NONINTERACTIVE pause
:python_ready

REM Install addon server deps (FastAPI, uvicorn) so the exe's addon server has them at runtime
set "REQ=Build_IL2CPP\StableProjectorz_Data\StreamingAssets\AddonSystem\requirements.txt"
if exist "%REQ%" (
    echo Ensuring addon dependencies (FastAPI, uvicorn)...
    python -m pip install -r "%REQ%" -q 2>nul
)

REM So the exe can avoid auto-restart loop when it was already launched by this bat
set "SPZ_ADDONS_LAUNCHED=1"
echo Starting StableProjectorz...
if not defined FOREGROUND goto start_detached
echo Running and waiting for exit ^(to see crash code^)...
"%EXE%"
set EXIT_CODE=%ERRORLEVEL%
if %EXIT_CODE% NEQ 0 (
    echo.
    echo Process exited with code %EXIT_CODE%. Check Player.log for crash details:
    echo   %%USERPROFILE%%\AppData\LocalLow\StableProjectorz\...\Player.log
)
if not defined SPZ_ADDONS_NONINTERACTIVE pause
endlocal
exit /b %EXIT_CODE%

:start_detached
start "" "%EXE%"
endlocal
