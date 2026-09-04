#!/usr/bin/env python3
"""40 便の台本＝ROCm(bf16) の「初見形状の罰」を段別に測り、暖機レシピの材料を採る。

upstream は **読むだけ**（import のみ・1 バイトも変更しない）。上流の書き換えは
Python の monkeypatch（torchaudio.load/save のシムだけ・torchcodec が無い箱のため）。

1 プロセス＝1 プラン。プランは JSON で与える:

{
  "texts":  {"t1": "...", "t2": "..."},           # 本文の辞書
  "refs":   [{"id": "r5", "seconds": 5.0}, ...],  # --ref-src から作る参照 wav
  "latents": ["r5", "r10"],                       # 上の参照から潜在(.pt)を事前計算
  "shots":  [ {"tag": "...", "text": "t1",
               "seconds": null|4.0,               # 出力尺の固定（duration 予測をバイパス）
               "ref": null|"wav:r5"|"lat:r5"} ]
}

各射で採るもの＝段別 ms（stage_timings 生値）・wall・total_to_decode・音声長・
S_lat（潜在フレーム数）・VRAM ピーク・messages。empty_cache は呼ばない。
"""
from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys
import time
from pathlib import Path

import numpy as np
import soundfile as sf
import torch

ap = argparse.ArgumentParser()
ap.add_argument("--plan", required=True, help="プラン JSON のパス")
ap.add_argument("--out", required=True)
ap.add_argument("--device", default="cuda")
ap.add_argument("--codec-device", default=None)
ap.add_argument("--precision", default="bf16", choices=["fp32", "bf16"])
ap.add_argument("--steps", type=int, default=40)
ap.add_argument("--seed", type=int, default=1234)
ap.add_argument("--caption", default="落ち着いた女性の声で、丁寧に話す。")
ap.add_argument("--checkpoint", default="Aratako/Irodori-TTS-v4.1-Small")
ap.add_argument("--ref-src", default=None)
ap.add_argument("--ref-dir", default=None, help="作った参照 wav / 潜在の置き場（既定＝lab/tmp/40）")
ap.add_argument("--tag", default=None)
args = ap.parse_args()

HERE = Path(__file__).resolve().parent
LAB = HERE.parent
ref_dir = Path(args.ref_dir) if args.ref_dir else (LAB / "tmp" / "40")
ref_dir.mkdir(parents=True, exist_ok=True)

plan = json.loads(Path(args.plan).read_text(encoding="utf-8"))
TEXTS: dict[str, str] = plan.get("texts", {})

# ------------------------------------------------------------- torchaudio シム
import torchaudio  # noqa: E402

_shim = {"load": False, "save": False}
_probe_src = args.ref_src or str(LAB / "out" / "refvoices" / "vv_zundamon_30s.wav")
try:
    if Path(_probe_src).is_file():
        torchaudio.load(_probe_src)
except Exception as exc:  # noqa: BLE001
    _shim["load"] = f"{type(exc).__name__}: {str(exc)[:120]}"

    def _load_soundfile(uri, *a, **kw):
        data, sr = sf.read(str(uri), dtype="float32", always_2d=True)
        return torch.from_numpy(data.T.copy()), int(sr)

    torchaudio.load = _load_soundfile


def _save_soundfile(uri, src, sample_rate, **kw):
    audio = src.detach().to(device="cpu", dtype=torch.float32)
    arr = audio.squeeze(0).numpy() if audio.shape[0] == 1 else audio.T.numpy()
    sf.write(str(uri), arr, int(sample_rate))


torchaudio.save = _save_soundfile
_shim["save"] = True

from irodori_tts.inference_runtime import (  # noqa: E402
    InferenceRuntime,
    RuntimeKey,
    SamplingRequest,
    _load_audio,
    download_hf_checkpoint,
)


def _sync(dev: torch.device) -> None:
    if dev.type == "cuda":
        torch.cuda.synchronize(dev)


# ----------------------------------------------------------- 参照 wav を作る
ref_src = Path(args.ref_src) if args.ref_src else (LAB / "out" / "refvoices" / "vv_zundamon_30s.wav")
ref_wavs: dict[str, Path] = {}
for spec in plan.get("refs", []):
    rid = spec["id"]
    secs = float(spec["seconds"])
    dst = ref_dir / f"40_{rid}.wav"
    data, sr = sf.read(str(ref_src), dtype="float32", always_2d=True)
    need = int(round(secs * sr))
    if data.shape[0] < need:
        data = np.tile(data, (int(np.ceil(need / data.shape[0])), 1))
    sf.write(str(dst), data[:need], sr, subtype="PCM_16")
    ref_wavs[rid] = dst

# ------------------------------------------------------------------ runtime
t0 = time.perf_counter()
ckpt = args.checkpoint
if not Path(ckpt).is_file():
    ckpt = download_hf_checkpoint(ckpt)
t_resolve = time.perf_counter() - t0

codec_device = args.codec_device or args.device
t0 = time.perf_counter()
rt = InferenceRuntime.from_key(
    RuntimeKey(
        checkpoint=ckpt,
        model_device=args.device,
        codec_device=codec_device,
        model_precision=args.precision,
        codec_precision=args.precision,
    )
)
_sync(rt.model_device)
t_load = time.perf_counter() - t0

HOP = int(rt.codec.model.hop_length)
SR = int(rt.codec.sample_rate)

out: dict = {
    "tag": args.tag,
    "plan": args.plan,
    "device": args.device,
    "precision": args.precision,
    "steps": args.steps,
    "seed": args.seed,
    "checkpoint": args.checkpoint,
    "torch": torch.__version__,
    "torch_hip": torch.version.hip,
    "torchaudio_shim": _shim,
    "hf_resolve_s": round(t_resolve, 3),
    "model_load_s": round(t_load, 3),
    "hop_length": HOP,
    "codec_sample_rate": SR,
    "miopen_env": {
        k: os.environ.get(k)
        for k in ("MIOPEN_USER_DB_PATH", "MIOPEN_CUSTOM_CACHE_DIR", "MIOPEN_DISABLE_CACHE")
    },
    "ref_src": str(ref_src),
    "latents": [],
    "shots": [],
}
if args.device.startswith("cuda"):
    out["gpu_name"] = torch.cuda.get_device_name(0)
    out["vram_after_load_mb"] = round(torch.cuda.memory_allocated() / 1048576.0, 1)

# ------------------------------------------- 参照潜在の事前計算（31 §3 の逃げ道）
# runtime の wav 経路（inference_runtime._load_reference_latent の else 枝）と同じ順。
ref_latents: dict[str, Path] = {}
for rid in plan.get("latents", []):
    src = ref_wavs[rid]
    t0 = time.perf_counter()
    wav, sr = _load_audio(str(src))
    piece = rt.codec.encode_waveform(
        wav.unsqueeze(0), sample_rate=int(sr), normalize_db=-16.0, ensure_max=True
    ).cpu()
    _sync(rt.codec_device)
    dt = time.perf_counter() - t0
    lat2d = piece[0].contiguous()
    dst = ref_dir / f"40_{rid}.pt"
    torch.save(lat2d, str(dst))
    ref_latents[rid] = dst
    row = {
        "id": rid,
        "pt": str(dst),
        "precompute_ms": round(dt * 1000.0, 1),
        "latent_shape": list(lat2d.shape),
        "pt_bytes": int(dst.stat().st_size),
    }
    out["latents"].append(row)
    print(json.dumps(row, ensure_ascii=False), flush=True)

# --------------------------------------------------------------------- 射
FRAMES_RE = re.compile(r"using_frames=(\d+)")

for i, shot in enumerate(plan["shots"], 1):
    text_key = shot["text"]
    text = TEXTS.get(text_key, text_key)
    seconds = shot.get("seconds")
    ref = shot.get("ref")
    ref_wav = ref_latent = None
    if ref:
        kind, rid = ref.split(":", 1)
        if kind == "wav":
            ref_wav = str(ref_wavs[rid])
        elif kind == "lat":
            ref_latent = str(ref_latents[rid])
        else:
            raise SystemExit(f"unknown ref spec {ref}")
    req = SamplingRequest(
        text=text,
        caption=args.caption,
        no_ref=(ref is None),
        ref_wav=ref_wav,
        ref_latent=ref_latent,
        seconds=seconds,
        num_steps=args.steps,
        seed=args.seed,
    )
    if args.device.startswith("cuda"):
        torch.cuda.reset_peak_memory_stats()
    t0 = time.perf_counter()
    res = rt.synthesize(req, log_fn=None)
    _sync(rt.model_device)
    wall = time.perf_counter() - t0
    secs = res.audio.shape[-1] / float(res.sample_rate)
    s_lat = None
    for m in res.messages:
        hit = FRAMES_RE.search(m)
        if hit:
            s_lat = int(hit.group(1))
    if s_lat is None and seconds is not None:
        s_lat = math.ceil(max(1, int(float(seconds) * SR)) / HOP)
    row = {
        "i": i,
        "tag": shot.get("tag", f"shot{i}"),
        "text_key": text_key,
        "text_chars": len(text),
        "seconds_arg": seconds,
        "ref": ref,
        "s_lat": s_lat,
        "wall_s": round(wall, 3),
        "total_to_decode_s": round(float(res.total_to_decode), 3),
        "stage_ms": {k: round(v * 1000.0, 1) for k, v in res.stage_timings},
        "audio_s": round(secs, 3),
        "rtf": round(float(res.total_to_decode) / secs, 4),
        "messages": res.messages,
    }
    if args.device.startswith("cuda"):
        row["vram_peak_mb"] = round(torch.cuda.max_memory_allocated() / 1048576.0, 1)
    out["shots"].append(row)
    print(json.dumps({k: row[k] for k in row if k != "messages"}, ensure_ascii=False), flush=True)

if args.device.startswith("cuda"):
    out["vram_peak_overall_mb"] = round(torch.cuda.max_memory_allocated() / 1048576.0, 1)

Path(args.out).parent.mkdir(parents=True, exist_ok=True)
Path(args.out).write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
print("[lab] written " + args.out, file=sys.stderr)
