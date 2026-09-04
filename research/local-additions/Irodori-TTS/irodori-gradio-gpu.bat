@echo off
rem Irodori-TTS 本体（Gradio 実験卓）を GPU(ROCm) で起動する
rem ※uv run で起動すると CPU の旧 .venv を掴むので、必ずこの bat か下の 1 行で起動すること
cd /d C:\IrodoriTTS\Irodori-TTS
.venv-rocm\Scripts\python.exe gradio_app_voicedesign.py --server-name 0.0.0.0 --server-port 7861
pause
