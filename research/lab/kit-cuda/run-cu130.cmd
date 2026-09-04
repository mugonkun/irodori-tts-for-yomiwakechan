@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-cuda-kit.ps1" -Variant cu130
echo.
echo Finished. Results are in "%~dp0results".
pause
