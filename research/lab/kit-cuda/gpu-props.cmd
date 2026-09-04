@echo off
setlocal
cd /d "%~dp0"
set "KIT=%CD%"
if not exist "%KIT%\.venv-cu130\Scripts\python.exe" (
  echo .venv-cu130 not found - run run-cu130.cmd first.
  pause
  exit /b 1
)
if not exist "%KIT%\results" mkdir "%KIT%\results"
set PYTHONDONTWRITEBYTECODE=1
set PYTHONUTF8=1
echo [gpu-props] enumerating GPUs with torch and nvidia-smi ...
"%KIT%\.venv-cu130\Scripts\python.exe" "%KIT%\gpu_props.py" "%KIT%\results\gpu_props.json"
echo.
echo Done. Result: %KIT%\results\gpu_props.json
pause
