@echo off
setlocal
cd /d "%~dp0"
echo [onnx-only] CUDA EP ...
if exist ".venv-ort-cuda\Scripts\python.exe" (".venv-ort-cuda\Scripts\python.exe" bench_onnx.py --onnx-dir onnx --ep cuda --json-out results\onnx_cuda_rerun.json) else (echo .venv-ort-cuda not found - run the full kit first)
echo [onnx-only] DirectML EP ...
if exist ".venv-ort-dml\Scripts\python.exe" (".venv-ort-dml\Scripts\python.exe" bench_onnx.py --onnx-dir onnx --ep dml --json-out results\onnx_dml_rerun.json) else (echo .venv-ort-dml not found - run the full kit first)
echo.
echo Finished. Results: results\onnx_cuda_rerun.json and results\onnx_dml_rerun.json
pause
