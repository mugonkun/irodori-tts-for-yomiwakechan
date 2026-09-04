#!/usr/bin/env python3
"""lab 専用: 同一プロセス内で N 回 synthesize して「常駐サーバのウォーム値」を測る。

infer.py は 1 実行 = 1 プロセスなので、形状チューニング/カーネル選択の
一度きりの費用が毎回のっている。常駐サーバ（8088）が見る値を分離するために、
InferenceRuntime を 1 回だけ作って同じ SamplingRequest を N 回投げる。

使い方:
  python gpu_inproc.py --precision bf16 --steps 40 --len short --reps 4 \
      --json-out out/gpu_inproc_bf16_s40_short.json
upstream は読むだけ（import のみ・1 バイトも変更しない）。
"""
from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

import torch

from irodori_tts.inference_runtime import (
    InferenceRuntime,
    RuntimeKey,
    SamplingRequest,
    download_hf_checkpoint,
)

SHORT = "こんにちは、読み分けちゃんのテストです。"
LONG = (
    "読み分けちゃん2 は、配信中の読み上げを担当するソフトです。"
    "今日は九番目のエンジンとして、彩り音声合成を試しています。"
)
CAPTION = "落ち着いた女性の声で、丁寧に話す。"

ap = argparse.ArgumentParser()
ap.add_argument("--precision", default="bf16")
ap.add_argument("--steps", type=int, default=40)
ap.add_argument("--len", dest="length", default="short", choices=["short", "long", "both"])
ap.add_argument("--reps", type=int, default=4)
ap.add_argument("--device", default="cuda")
ap.add_argument("--json-out", required=True)
args = ap.parse_args()

t0 = time.perf_counter()
ckpt = download_hf_checkpoint("Aratako/Irodori-TTS-v4.1-Small")
t_resolve = time.perf_counter() - t0

t0 = time.perf_counter()
rt = InferenceRuntime.from_key(
    RuntimeKey(
        checkpoint=ckpt,
        model_device=args.device,
        codec_device=args.device,
        model_precision=args.precision,
        codec_precision=args.precision,
    )
)
torch.cuda.synchronize()
t_load = time.perf_counter() - t0

out: dict = {
    "precision": args.precision,
    "steps": args.steps,
    "length": args.length,
    "device": args.device,
    "hf_resolve_s": round(t_resolve, 3),
    "model_load_s": round(t_load, 3),
    "vram_after_load_alloc_mb": round(torch.cuda.memory_allocated() / 1048576.0, 1),
    "runs": [],
}

texts = {"short": SHORT, "long": LONG}
order = ["short", "long"] if args.length == "both" else [args.length]

for name in order:
    for i in range(1, args.reps + 1):
        t0 = time.perf_counter()
        res = rt.synthesize(
            SamplingRequest(
                text=texts[name],
                caption=CAPTION,
                no_ref=True,
                num_steps=args.steps,
                seed=1234,
            ),
            log_fn=None,
        )
        torch.cuda.synchronize()
        wall = time.perf_counter() - t0
        secs = res.audio.shape[-1] / float(res.sample_rate)
        out["runs"].append(
            {
                "text": name,
                "rep": i,
                "wall_s": round(wall, 3),
                "total_to_decode_s": round(float(res.total_to_decode), 3),
                "stage_ms": {k: round(v * 1000.0, 1) for k, v in res.stage_timings},
                "audio_s": round(secs, 3),
                "rtf": round(float(res.total_to_decode) / secs, 3),
                "vram_peak_alloc_mb": round(torch.cuda.max_memory_allocated() / 1048576.0, 1),
            }
        )
        print(json.dumps(out["runs"][-1], ensure_ascii=False), flush=True)

Path(args.json_out).write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
print("[lab] written " + args.json_out)
