@echo off
rem Start Irodori-TTS-Server (API server used by yomiwakechan2) on GPU (ROCm)
rem NOTE: "uv run" grabs the old CPU .venv - always start via this bat or the python line below.
rem (This file must stay CRLF + ASCII: UTF-8/LF broke cmd parsing and silently dropped the set lines.)
cd /d C:\irodori-TTS-server\Irodori-TTS-Server
set IRODORI_PRELOAD=true
set IRODORI_EMPTY_CACHE_INTERVAL=0
rem bf16: measured 4.8x faster warm synth, half VRAM (delete the next 2 lines to go back to fp32)
set IRODORI_MODEL_PRECISION=bf16
set IRODORI_CODEC_PRECISION=bf16
.venv-rocm\Scripts\python.exe -m irodori_openai_tts --host 0.0.0.0 --port 8088
pause
