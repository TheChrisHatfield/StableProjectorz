@echo off
setlocal EnableExtensions
REM Print Unity.exe for the parent project. Exit 0 when found.
REM No PowerShell. Used only when build_for_testing.bat cannot start PowerShell.

set "ROOT=%~dp0.."
pushd "%ROOT%" >nul || exit /b 1
set "ROOT=%CD%"
popd

if defined UNITY_EXE if exist "%UNITY_EXE%" (
  echo %UNITY_EXE%
  exit /b 0
)

set "LOCAL=%ROOT%\build_for_testing.local.ps1"
if exist "%LOCAL%" (
  for /f "usebackq tokens=2 delims='" %%P in (`findstr /C:"$SpzUnityEditorPath" "%LOCAL%"`) do (
    if exist "%%~P" (
      echo %%~P
      exit /b 0
    )
  )
)

set "VER="
if not exist "%ROOT%\ProjectSettings\ProjectVersion.txt" exit /b 1
for /f "usebackq tokens=1,* delims=:" %%A in ("%ROOT%\ProjectSettings\ProjectVersion.txt") do (
  if /i "%%A"=="m_EditorVersion" (
    set "VER=%%B"
    goto gotver
  )
)
:gotver
if not defined VER exit /b 1
for /f "tokens=* delims= " %%V in ("%VER%") do set "VER=%%V"

if defined ProgramW6432 if exist "%ProgramW6432%\Unity\Hub\Editor\%VER%\Editor\Unity.exe" (
  echo %ProgramW6432%\Unity\Hub\Editor\%VER%\Editor\Unity.exe
  exit /b 0
)
if exist "%ProgramFiles%\Unity\Hub\Editor\%VER%\Editor\Unity.exe" (
  echo %ProgramFiles%\Unity\Hub\Editor\%VER%\Editor\Unity.exe
  exit /b 0
)
set "PF86=%ProgramFiles(x86)%"
if defined PF86 if exist "%PF86%\Unity\Hub\Editor\%VER%\Editor\Unity.exe" (
  echo %PF86%\Unity\Hub\Editor\%VER%\Editor\Unity.exe
  exit /b 0
)
exit /b 1
