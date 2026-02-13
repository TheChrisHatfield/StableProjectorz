@echo off
cd /d "%~dp0"
set LOG=push_diagnostic.txt
echo StableProjectorz - GitHub push diagnostic > "%LOG%"
echo Run time: %date% %time% >> "%LOG%"
echo. >> "%LOG%"

echo === 1. Git version === >> "%LOG%"
git --version >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo === 2. Current branch === >> "%LOG%"
git branch >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo === 3. Remote origin === >> "%LOG%"
git remote get-url origin >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo === 4. Status (uncommitted changes?) === >> "%LOG%"
git status >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo === 5. Last 3 commits === >> "%LOG%"
git log --oneline -3 >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo === 6. Staging and committing === >> "%LOG%"
git add . >> "%LOG%" 2>&1
git commit -m "fix: Add-on Manager UI, IL2CPP compatibility, and API robustness" >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo === 7. Pushing to origin main (this is where auth errors appear) === >> "%LOG%"
git push origin main >> "%LOG%" 2>&1
set PUSH_ERR=%ERRORLEVEL%

echo. >> "%LOG%"
echo === Push exit code: %PUSH_ERR% === >> "%LOG%"

type "%LOG%"
echo.
echo Full log saved to: %LOG%
if %PUSH_ERR% neq 0 (
    echo.
    echo PUSH FAILED. See above. Common fix: use a Personal Access Token instead of password.
    echo https://github.com/settings/tokens - create token with repo scope, then use it as password when prompted.
)
pause
