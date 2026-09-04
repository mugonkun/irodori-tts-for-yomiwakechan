#!/usr/bin/env bash
# lab 専用: infer_gpu.py で 1 条件を N 回まわす。
# 使い方: bash run_gpu_case.sh <prec> <steps> <short|long> <n_reps>
set -u
PY="C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe"
LAB="C:/Users/mugonkun/source/repos/irodori-native-research/lab"
export PYTHONPATH="C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS"
export PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1 PYTHONIOENCODING=utf-8 PYTHONUTF8=1
export PYTHONWARNINGS=ignore OMP_NUM_THREADS=8 MKL_NUM_THREADS=8

PREC="$1"; STEPS="$2"; LEN="$3"; N="${4:-3}"
SHORT="こんにちは、読み分けちゃんのテストです。"
LONG="読み分けちゃん2 は、配信中の読み上げを担当するソフトです。今日は九番目のエンジンとして、彩り音声合成を試しています。"
if [ "$LEN" = "short" ]; then TEXT="$SHORT"; else TEXT="$LONG"; fi

for i in $(seq 1 "$N"); do
  TAG="${PREC}_s${STEPS}_${LEN}_r${i}"
  echo "=== RUN $TAG ==="
  "$PY" "$LAB/bin/infer_gpu.py" \
    --json-out "$LAB/out/gpu_${TAG}.json" --tag "$TAG" -- \
    --hf-checkpoint Aratako/Irodori-TTS-v4.1-Small \
    --text "$TEXT" \
    --caption "落ち着いた女性の声で、丁寧に話す。" \
    --no-ref --seed 1234 \
    --model-device cuda --codec-device cuda \
    --model-precision "$PREC" --codec-precision "$PREC" \
    --num-steps "$STEPS" \
    --output-wav "$LAB/out/gpu_${TAG}.wav" 2>&1 | grep -v "^MIOpen: Warning" | tail -14
done
