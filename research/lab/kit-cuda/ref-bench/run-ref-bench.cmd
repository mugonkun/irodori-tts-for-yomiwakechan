@echo off
setlocal
cd /d "%~dp0.."
set "KIT=%CD%"
set "REF=%KIT%\ref-bench"
set "PY=%KIT%\.venv-cu130\Scripts\python.exe"
set "RES=%KIT%\results"
set "SCRIPT=%REF%\ref_bench_voices.py"

set "HF_HOME=%KIT%\hf"
set "HF_HUB_OFFLINE=1"
set "PYTHONPATH=%KIT%\src\Irodori-TTS"
set "PYTHONDONTWRITEBYTECODE=1"
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
set "PYTHONWARNINGS=ignore"

echo ============================================================
echo  Irodori-TTS reference-voice bench (note 32)
echo  real sample voices: VOICEVOX x4 + COEIROINK x1, 10s and 30s
echo ============================================================
echo  kit root : %KIT%
echo  voices   : %REF%\voices   (10 wav + manifest.json)
echo  results  : %RES%\ref_bench_*.json
echo             %RES%\ref-latent\*.pt    (precomputed reference latents)
echo             %RES%\ref-wav\*.wav      (synthesised audio, for listening)
echo  This writes NOTHING outside the kit folder. Takes about 20-40 min.
echo.

if not exist "%PY%" goto :no_venv
if not exist "%SCRIPT%" goto :no_script
if not exist "%REF%\voices\manifest.json" goto :no_voices
if not exist "%RES%" mkdir "%RES%"

REM ------------------------------------------------------------------
REM  Knobs for a CPU rehearsal only. On the 3090 leave them ALL unset.
REM    REF_STEPS=4                        step count of every pass
REM    REF_REPS=1                         repeats per condition
REM    REF_VOICES=vv_zundamon co_ameno    keep only these voice_id values
REM    REF_DEVICE=cpu                     device (bf16 needs CUDA or XPU)
REM    REF_PRECISION=fp32                 precision of every pass
REM ------------------------------------------------------------------
set "DEV=cuda"
set "REPS=3"
set "STEPS_A=40"
set "STEPS_B=40"
set "STEPS_C=10"
set "STEPS_D=40"
set "PREC_A=bf16"
set "PREC_B=bf16"
set "PREC_C=bf16"
set "PREC_D=fp32"
set "VOICEARG="
if defined REF_DEVICE set "DEV=%REF_DEVICE%"
if defined REF_REPS set "REPS=%REF_REPS%"
if defined REF_STEPS set "STEPS_A=%REF_STEPS%"
if defined REF_STEPS set "STEPS_B=%REF_STEPS%"
if defined REF_STEPS set "STEPS_C=%REF_STEPS%"
if defined REF_STEPS set "STEPS_D=%REF_STEPS%"
if defined REF_PRECISION set "PREC_A=%REF_PRECISION%"
if defined REF_PRECISION set "PREC_B=%REF_PRECISION%"
if defined REF_PRECISION set "PREC_C=%REF_PRECISION%"
if defined REF_PRECISION set "PREC_D=%REF_PRECISION%"
if defined REF_VOICES set "VOICEARG=--voices %REF_VOICES%"

set "TAG_A=a_%PREC_A%_s%STEPS_A%_short"
set "TAG_B=b_%PREC_B%_s%STEPS_B%_long"
set "TAG_C=c_%PREC_C%_s%STEPS_C%_short"
set "TAG_D=d_%PREC_D%_s%STEPS_D%_short"
set "FAILS=0"

echo [ref] device=%DEV%  reps=%REPS%  voice filter: %VOICEARG%
echo.
echo [ref] ---- (a) %PREC_A% %STEPS_A% steps, short text, with-latent, save-wav ----
"%PY%" "%SCRIPT%" --device %DEV% --precision %PREC_A% --steps %STEPS_A% --reps %REPS% --len short --with-latent --save-wav --tag %TAG_A% --out "%RES%\ref_bench_%TAG_A%.json" %VOICEARG%
if errorlevel 1 (
    set /a FAILS+=1
    echo [ref] WARNING: pass a exited non-zero
)

echo.
echo [ref] ---- (b) %PREC_B% %STEPS_B% steps, long text, save-wav ----
"%PY%" "%SCRIPT%" --device %DEV% --precision %PREC_B% --steps %STEPS_B% --reps %REPS% --len long --save-wav --tag %TAG_B% --out "%RES%\ref_bench_%TAG_B%.json" %VOICEARG%
if errorlevel 1 (
    set /a FAILS+=1
    echo [ref] WARNING: pass b exited non-zero
)

echo.
echo [ref] ---- (c) %PREC_C% %STEPS_C% steps, short text ----
"%PY%" "%SCRIPT%" --device %DEV% --precision %PREC_C% --steps %STEPS_C% --reps %REPS% --len short --tag %TAG_C% --out "%RES%\ref_bench_%TAG_C%.json" %VOICEARG%
if errorlevel 1 (
    set /a FAILS+=1
    echo [ref] WARNING: pass c exited non-zero
)

echo.
echo [ref] ---- (d) %PREC_D% %STEPS_D% steps, short text ----
"%PY%" "%SCRIPT%" --device %DEV% --precision %PREC_D% --steps %STEPS_D% --reps %REPS% --len short --tag %TAG_D% --out "%RES%\ref_bench_%TAG_D%.json" %VOICEARG%
if errorlevel 1 (
    set /a FAILS+=1
    echo [ref] WARNING: pass d exited non-zero
)

echo.
echo ------------------------------------------------------------
echo  Finished.  script failures: %FAILS%
echo  Send back:
echo    %RES%\ref_bench_%TAG_A%.json
echo    %RES%\ref_bench_%TAG_B%.json
echo    %RES%\ref_bench_%TAG_C%.json
echo    %RES%\ref_bench_%TAG_D%.json
echo    %RES%\ref-wav\        (listen to these)
echo ------------------------------------------------------------
goto :done

:no_venv
echo [ref] ERROR: %PY% not found.
echo        Run run-cu130.cmd first (it builds .venv-cu130 and unpacks src\Irodori-TTS).
goto :done

:no_script
echo [ref] ERROR: %SCRIPT% not found.
echo        Copy the whole ref-bench folder into the kit and run this again.
goto :done

:no_voices
echo [ref] ERROR: %REF%\voices\manifest.json not found.
echo        The voices folder (10 wav + manifest.json) must ship with this script.
goto :done

:done
echo.
pause
