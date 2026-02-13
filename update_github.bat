@echo off
REM Push local changes to https://github.com/TheChrisHatfield/StableProjectorz
cd /d "%~dp0"

echo === Git Status ===
git status

echo.
echo === Staging all changes ===
git add .

echo.
echo === Committing (if there are changes) ===
git commit -m "fix: Add-on Manager UI, IL2CPP compatibility, and API robustness" 2>nul || echo No changes to commit or already committed.

echo.
echo === Pushing to origin main ===
git push origin main

echo.
echo Done. Check https://github.com/TheChrisHatfield/StableProjectorz
pause
