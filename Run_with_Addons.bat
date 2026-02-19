@echo off
REM Launches StableProjectorz with Python on PATH. The exe also auto-starts the addon server
REM when Python is found in common locations (dual-trigger). Use this if Python is in a custom
REM location or "Load addons now" fails with "Cannot connect to destination host".
setlocal
cd /d "%~dp0"

set "EXE=Build_IL2CPP\StableProjectorz.exe"
if not exist "%EXE%" (
    echo ERROR: %EXE% not found. Build the project first with build_for_testing.bat
    pause
    exit /b 1
)

REM Ensure Python is on PATH so Unity can start addon_server.py (port 5555 + HTTP 5557)
set "PYTHON_ADDED="
where python >nul 2>&1 && set "PYTHON_ADDED=1"
if not defined PYTHON_ADDED (
    where py >nul 2>&1 && set "PYTHON_ADDED=1"
)
if not defined PYTHON_ADDED (
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
        goto :found_python
    )
)
:found_python
if not defined PYTHON_ADDED (
    echo.
    echo WARNING: Python was not found. Addons will not work.
    echo Install Python 3.10+ and add it to PATH, or run this script from a shell where "python" works.
    echo.
    pause
)

REM Install addon server deps (FastAPI, uvicorn) so the exe's addon server has them at runtime
set "REQ=Build_IL2CPP\StableProjectorz_Data\StreamingAssets\AddonSystem\requirements.txt"
if exist "%REQ%" (
    echo Ensuring addon dependencies (FastAPI, uvicorn)...
    python -m pip install -r "%REQ%" -q 2>nul
)

REM So the exe can avoid auto-restart loop when it was already launched by this bat
set "SPZ_ADDONS_LAUNCHED=1"
echo Starting StableProjectorz...
start "" "%EXE%"
endlocal
