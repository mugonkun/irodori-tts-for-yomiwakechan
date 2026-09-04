@echo off
setlocal
set "SRC=%~dp0"
set "DST=C:\irodori-kit"
echo Copying the kit from "%SRC%" to "%DST%" (skips .venv* and results) ...
robocopy "%SRC%." "%DST%" /E /R:2 /W:5 /NP /NFL /NDL /XD .venv-cu126 .venv-cu130 .venv-ort-cuda .venv-ort-dml .venv-e2e-cuda .venv-e2e-dml results
if %ERRORLEVEL% GEQ 8 (echo COPY FAILED - see robocopy output above) else (echo Copy done.)
echo.
echo Next: 1) start "%DST%\sync-results.cmd" and keep it open   2) start "%DST%\run-cu130.cmd"
pause
