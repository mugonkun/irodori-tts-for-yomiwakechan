@echo off
setlocal
cd /d "%~dp0.."
set "KIT=%CD%"
set "E2E=%KIT%\onnx-e2e"
set "UV=%KIT%\uv\uv.exe"
set "UV_CACHE_DIR=%KIT%\uv-cache"
set "UV_PYTHON_INSTALL_DIR=%KIT%\python"
set "UV_NO_CONFIG=1"
set "PYTHONDONTWRITEBYTECODE=1"
set "PYTHONIOENCODING=utf-8"
set "PYTHONUTF8=1"
set "HF_HUB_OFFLINE=1"
set "PYC=%KIT%\.venv-e2e-cuda\Scripts\python.exe"
set "PYD=%KIT%\.venv-e2e-dml\Scripts\python.exe"
set "STEPS=40,10"
set "REPS=2"

echo ============================================================
echo  Irodori-TTS (b) ONNX end-to-end -- CUDA EP and DirectML EP
echo ============================================================
echo  kit root : %KIT%
echo  results  : %KIT%\results\onnx_e2e_cuda.json / onnx_e2e_dml.json
echo  This writes NOTHING outside the kit folder. Takes 15-40 min.
echo.

if not exist "%UV%" goto :no_uv
if not exist "%KIT%\onnx\dit_step.onnx" goto :no_onnx
if not exist "%E2E%\data\cond_real.npz" goto :no_data
if not exist "%KIT%\results" mkdir "%KIT%\results"

REM ------------------------------------------------------------ CUDA EP venv
if exist "%PYC%" goto :cuda_have_venv
echo [e2e] creating .venv-e2e-cuda ...
"%UV%" venv --python 3.12 "%KIT%\.venv-e2e-cuda"
if errorlevel 1 goto :cuda_skip
:cuda_have_venv
if exist "%KIT%\.venv-e2e-cuda\ort-ready.txt" goto :cuda_run
echo [e2e] installing onnxruntime-gpu[cuda,cudnn] (about 1.3 GB) ...
"%UV%" pip install --python "%PYC%" "onnxruntime-gpu[cuda,cudnn]" numpy
if not errorlevel 1 goto :cuda_mark
echo [e2e] extras [cuda,cudnn] not accepted -- installing the nvidia wheels explicitly ...
"%UV%" pip install --python "%PYC%" onnxruntime-gpu numpy nvidia-cuda-runtime-cu13 nvidia-cuda-nvrtc-cu13 nvidia-cudnn-cu13 nvidia-cublas-cu13 nvidia-cufft-cu13 nvidia-curand-cu13
if not errorlevel 1 goto :cuda_mark
echo [e2e] explicit nvidia wheels failed -- trying bare onnxruntime-gpu ...
"%UV%" pip install --python "%PYC%" onnxruntime-gpu numpy
if errorlevel 1 goto :cuda_skip
:cuda_mark
echo ready> "%KIT%\.venv-e2e-cuda\ort-ready.txt"
:cuda_run
echo.
echo [e2e] ---- CUDA EP run ----
"%PYC%" "%E2E%\e2e_onnx.py" --ep cuda --onnx-dir "%KIT%\onnx" --data-dir "%E2E%\data" --json-out "%KIT%\results\onnx_e2e_cuda.json" --steps %STEPS% --reps %REPS% --label "kit run: cuda"
if errorlevel 1 echo [e2e] WARNING: the CUDA run exited non-zero -- read results\onnx_e2e_cuda.json
goto :dml

:cuda_skip
echo [e2e] SKIPPED the CUDA EP run (onnxruntime-gpu could not be installed).

REM ------------------------------------------------------------ DirectML EP venv
:dml
if exist "%PYD%" goto :dml_have_venv
echo.
echo [e2e] creating .venv-e2e-dml ...
"%UV%" venv --python 3.12 "%KIT%\.venv-e2e-dml"
if errorlevel 1 goto :dml_skip
:dml_have_venv
if exist "%KIT%\.venv-e2e-dml\ort-ready.txt" goto :dml_run
echo [e2e] installing onnxruntime-directml (about 200 MB) ...
"%UV%" pip install --python "%PYD%" onnxruntime-directml numpy
if errorlevel 1 goto :dml_skip
echo ready> "%KIT%\.venv-e2e-dml\ort-ready.txt"
:dml_run
echo.
echo [e2e] ---- DirectML EP run ----
"%PYD%" "%E2E%\e2e_onnx.py" --ep dml --onnx-dir "%KIT%\onnx" --data-dir "%E2E%\data" --json-out "%KIT%\results\onnx_e2e_dml.json" --steps %STEPS% --reps %REPS% --label "kit run: dml"
if errorlevel 1 echo [e2e] WARNING: the DirectML run exited non-zero -- read results\onnx_e2e_dml.json
goto :done

:dml_skip
echo [e2e] SKIPPED the DirectML EP run (onnxruntime-directml could not be installed).
goto :done

:no_uv
echo [e2e] ERROR: uv not found at %UV%
echo        Copy the whole kit folder (it ships uv\uv.exe) and run this again.
goto :done

:no_onnx
echo [e2e] ERROR: %KIT%\onnx\dit_step.onnx not found.
echo        This script needs the kit's onnx\ folder (the same graphs run-cuda-kit.ps1 uses).
goto :done

:no_data
echo [e2e] ERROR: %E2E%\data\cond_real.npz not found (the data folder must ship with this script).
goto :done

:done
echo.
echo ------------------------------------------------------------
echo  Finished. Send back these two files:
echo    %KIT%\results\onnx_e2e_cuda.json
echo    %KIT%\results\onnx_e2e_dml.json
echo ------------------------------------------------------------
pause
