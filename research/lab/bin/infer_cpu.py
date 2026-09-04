#!/usr/bin/env python3
"""lab 専用の infer.py 実行シム（upstream を 1 バイトも変更しない）。

- torchaudio 2.10 の save は torchcodec 専用になっており、Windows では
  FFmpeg 共有ライブラリ(4-8)が無いと ImportError で落ちる。
  ここで torchaudio.save を soundfile 実装に差し替えてから upstream/infer.py を実行する。
- 壁時計時間・ピーク RSS(Windows: peak_wset)・出力 wav の長さ/SR を計測して JSON で出す。

使い方:
  python lab/bin/infer_cpu.py --json-out <path> -- <infer.py に渡す引数...>
"""
from __future__ import annotations

import json
import os
import runpy
import sys
import time
from pathlib import Path

UPSTREAM = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS")

argv = sys.argv[1:]
json_out = None
if argv and argv[0] == "--json-out":
    json_out = argv[1]
    argv = argv[2:]
if argv and argv[0] == "--":
    argv = argv[1:]

import soundfile as sf  # noqa: E402
import torch  # noqa: E402
import torchaudio  # noqa: E402


def _save_soundfile(uri, src, sample_rate, **kwargs):
    audio = src.detach().to(device="cpu", dtype=torch.float32)
    arr = audio.squeeze(0).numpy() if audio.shape[0] == 1 else audio.T.numpy()
    sf.write(str(uri), arr, int(sample_rate))


torchaudio.save = _save_soundfile

out_wav = None
for i, a in enumerate(argv):
    if a == "--output-wav":
        out_wav = argv[i + 1]

sys.argv = [str(UPSTREAM / "infer.py")] + argv
t0 = time.perf_counter()
runpy.run_path(str(UPSTREAM / "infer.py"), run_name="__main__")
wall = time.perf_counter() - t0

info = {"wall_seconds_total": round(wall, 3)}
try:
    import psutil

    mi = psutil.Process().memory_info()
    info["peak_wset_mb"] = round(getattr(mi, "peak_wset", mi.rss) / 1048576.0, 1)
    info["rss_mb_end"] = round(mi.rss / 1048576.0, 1)
except Exception as exc:  # pragma: no cover
    info["peak_wset_mb"] = f"unavailable: {exc}"

if out_wav and Path(out_wav).is_file():
    with sf.SoundFile(out_wav) as f:
        info["sample_rate"] = f.samplerate
        info["channels"] = f.channels
        info["frames"] = len(f)
        info["audio_seconds"] = round(len(f) / float(f.samplerate), 3)
        info["subtype"] = f.subtype
    info["wav_bytes"] = Path(out_wav).stat().st_size
    info["rtf_total"] = round(wall / info["audio_seconds"], 3)

print("[lab] " + json.dumps(info, ensure_ascii=False))
if json_out:
    Path(json_out).write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding="utf-8")
