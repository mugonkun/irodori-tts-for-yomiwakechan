#!/usr/bin/env python3
"""31 便の台本＝参照音声（サンプルボイス）を当てたときのコストを段別に測る。

upstream は **読むだけ**（import のみ・1 バイトも変更しない）。上流の書き換えは
すべて Python の monkeypatch（torchaudio.load/save のシムだけ）で代替する。

測るもの:
  A) 端から端まで（InferenceRuntime.synthesize）を --ref の各長さ × --reps 回。
     段別は SamplingResult.stage_timings をそのまま採る
     （prepare_reference / tokenize_text / predict_duration / sample_rf /
       unpatchify_latent / decode_latent / silentcipher_watermark）。
  B) 参照の符号化だけを分解して測る（合成を回さない）:
       load        = inference_runtime._load_audio（wav 読み）
       encode_norm = DACVAECodec.encode_waveform（既定＝ラウドネス正規化 -16 dB 込み）
       encode_raw  = 同上・normalize_db=None（audiotools を通さない＝DACVAE encoder 単体に近い）
       ref_encoder = patch_sequence_with_mask → ReferenceLatentEncoder(8層) →
                     speaker_norm → _prepend_masked_mean_token
     ＝「長さに比例する段はどれか」を形状つきで出す。

使い方（CPU）:
  python lab/bin/ref_bench.py --device cpu --precision fp32 --ref 0 10 30 \
      --steps 40 --reps 2 --out lab/out/31_ref_cpu.json

使い方（GPU・主席用の 1 行は §ノート参照）:
  python lab/bin/ref_bench.py --device cuda --precision bf16 --ref 0 10 30 \
      --steps 40 --reps 3 --out out/31_ref_cuda_bf16.json
"""
from __future__ import annotations

import argparse
import json
import statistics
import sys
import time
from pathlib import Path

import numpy as np
import soundfile as sf
import torch

SHORT = "こんにちは、読み分けちゃんのテストです。"
LONG = (
    "読み分けちゃん2 は、配信中の読み上げを担当するソフトです。"
    "今日は九番目のエンジンとして、彩り音声合成を試しています。"
)
CAPTION = "落ち着いた女性の声で、丁寧に話す。"

ap = argparse.ArgumentParser()
ap.add_argument("--device", default="cpu", help="cpu / cuda / cuda:N / xpu ...")
ap.add_argument("--codec-device", default=None, help="既定＝--device と同じ")
ap.add_argument("--precision", default="fp32", choices=["fp32", "bf16"])
ap.add_argument(
    "--ref",
    nargs="+",
    type=float,
    default=[0, 10, 30],
    help="参照音声の秒数。0 は --no-ref（参照なし）",
)
ap.add_argument("--steps", type=int, default=40)
ap.add_argument("--reps", type=int, default=2)
ap.add_argument("--seed", type=int, default=1234)
ap.add_argument("--len", dest="length", default="short", choices=["short", "long"])
ap.add_argument("--caption", default=CAPTION)
ap.add_argument("--checkpoint", default="Aratako/Irodori-TTS-v4.1-Small")
ap.add_argument(
    "--ref-src",
    default=None,
    help="参照音声の素材 wav（既定＝lab/out/long_steps40.wav・10.92 s）。"
    "足りない長さはこの wav の繰り返しで作る（声質は問わない＝時間だけを測る）",
)
ap.add_argument("--ref-dir", default=None, help="作った参照 wav の置き場（既定＝lab/tmp）")
ap.add_argument("--threads", type=int, default=8, help="torch.set_num_threads（CPU のみ意味を持つ）")
ap.add_argument("--micro-reps", type=int, default=3, help="B) 参照符号化の分解測定の反復")
ap.add_argument("--skip-synth", action="store_true", help="A) を飛ばして B) だけ測る")
ap.add_argument("--skip-micro", action="store_true", help="B) を飛ばして A) だけ測る")
ap.add_argument(
    "--seconds",
    type=float,
    default=None,
    help="出力尺を固定する（duration 予測をバイパス）。参照の有無で S_lat が変わるのを止めて "
    "DiT の増分だけを見たいときに使う",
)
ap.add_argument(
    "--interleave",
    action="store_true",
    help="rep を外側・参照長を内側に回す（他プロセスの負荷が偏るのを避ける）。"
    "混雑した機では必ず付けること",
)
ap.add_argument("--tag", default=None)
ap.add_argument("--out", required=True)
args = ap.parse_args()

HERE = Path(__file__).resolve().parent
LAB = HERE.parent
ref_src = Path(args.ref_src) if args.ref_src else (LAB / "out" / "long_steps40.wav")
ref_dir = Path(args.ref_dir) if args.ref_dir else (LAB / "tmp")
ref_dir.mkdir(parents=True, exist_ok=True)

# ---------------------------------------------------------------- torchaudio シム
# torchaudio 2.10 の load/save は torchcodec 専用になっており、torchcodec が
# 無いと ImportError。upstream の _load_audio(:1518) / save_wav(:1536) は
# `except RuntimeError` しか持たないので ImportError は貫通する（18 ノート §3-e）。
# ここで soundfile 実装に差し替える＝upstream は無改変のまま。
import torchaudio  # noqa: E402

_shim = {"load": False, "save": False}
try:
    torchaudio.load(str(ref_src))
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

from irodori_tts.codec import patchify_latent  # noqa: E402
from irodori_tts.inference_runtime import (  # noqa: E402
    InferenceRuntime,
    RuntimeKey,
    SamplingRequest,
    _load_audio,
    download_hf_checkpoint,
)
from irodori_tts.model import patch_sequence_with_mask  # noqa: E402

if args.device == "cpu":
    torch.set_num_threads(int(args.threads))


def _sync(dev: torch.device) -> None:
    if dev.type == "cuda":
        torch.cuda.synchronize(dev)
    elif dev.type == "xpu" and hasattr(torch, "xpu"):
        torch.xpu.synchronize()


# ------------------------------------------------------- 参照 wav を必要な長さで作る
def build_ref(seconds: float) -> Path:
    dst = ref_dir / f"31_ref_{int(round(seconds))}s.wav"
    data, sr = sf.read(str(ref_src), dtype="float32", always_2d=True)
    need = int(round(seconds * sr))
    if data.shape[0] < need:
        reps = int(np.ceil(need / data.shape[0]))
        data = np.tile(data, (reps, 1))
    sf.write(str(dst), data[:need], sr, subtype="PCM_16")
    return dst


ref_paths: dict[float, Path] = {}
for s in args.ref:
    if s > 0:
        ref_paths[s] = build_ref(s)

# ------------------------------------------------------------------ runtime を作る
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

out: dict = {
    "tag": args.tag,
    "device": args.device,
    "codec_device": codec_device,
    "precision": args.precision,
    "steps": args.steps,
    "reps": args.reps,
    "seed": args.seed,
    "text_len": args.length,
    "ref_seconds": args.ref,
    "ref_src": str(ref_src),
    "threads": int(args.threads) if args.device == "cpu" else None,
    "torch": torch.__version__,
    "torchaudio_shim": _shim,
    "hf_resolve_s": round(t_resolve, 3),
    "model_load_s": round(t_load, 3),
    "model_dtype": str(next(rt.model.parameters()).dtype),
    "codec_sample_rate": int(rt.codec.sample_rate),
    "speaker_patch_size": int(rt.model_cfg.speaker_patch_size),
    "latent_patch_size": int(rt.model_cfg.latent_patch_size),
    "ref_max_seconds_default": float(rt.default_max_ref_seconds),
    "runs": [],
    "ref_encode_micro": [],
}
if args.device.startswith("cuda"):
    out["gpu_name"] = torch.cuda.get_device_name(0)
    out["torch_hip"] = torch.version.hip
    out["vram_after_load_mb"] = round(torch.cuda.memory_allocated() / 1048576.0, 1)

text = SHORT if args.length == "short" else LONG

# ============================================================ A) 端から端まで
if not args.skip_synth:
    if args.interleave:
        plan = [(s, rep) for rep in range(1, args.reps + 1) for s in args.ref]
    else:
        plan = [(s, rep) for s in args.ref for rep in range(1, args.reps + 1)]
    for s, rep in plan:
        req = SamplingRequest(
            text=text,
            caption=args.caption,
            no_ref=(s == 0),
            ref_wav=None if s == 0 else str(ref_paths[s]),
            seconds=args.seconds,
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
        row = {
            "ref_s": s,
            "rep": rep,
            "phase": "cold" if rep == 1 else "warm",
            "wall_s": round(wall, 3),
            "total_to_decode_s": round(float(res.total_to_decode), 3),
            "stage_ms": {k: round(v * 1000.0, 1) for k, v in res.stage_timings},
            "audio_s": round(secs, 3),
            "rtf": round(float(res.total_to_decode) / secs, 4),
            "used_seed": int(res.used_seed),
            "messages": res.messages,
        }
        if args.device.startswith("cuda"):
            row["vram_peak_mb"] = round(torch.cuda.max_memory_allocated() / 1048576.0, 1)
        out["runs"].append(row)
        print(json.dumps(row, ensure_ascii=False), flush=True)

    # 混雑した機では中央値より最小値が素直（他プロセスの負荷は足し算にしか働かない）
    summary: dict = {}
    for s in args.ref:
        rows = [r for r in out["runs"] if r["ref_s"] == s]
        keys = sorted({k for r in rows for k in r["stage_ms"]})
        summary[str(s)] = {
            "n": len(rows),
            "audio_s": rows[0]["audio_s"],
            "min_total_s": min(r["total_to_decode_s"] for r in rows),
            "min_rtf": min(r["rtf"] for r in rows),
            "min_stage_ms": {
                k: min(r["stage_ms"].get(k, 0.0) for r in rows) for k in keys
            },
        }
    out["summary_min"] = summary
    print(json.dumps({"summary_min": summary}, ensure_ascii=False), flush=True)

# ================================================ B) 参照符号化の分解（段別・長さ別）
runtime_dtype = next(rt.model.parameters()).dtype
for s in args.ref:
    if s == 0 or args.skip_micro:
        continue
    path = str(ref_paths[s])
    tl, ten, tenraw, tref = [], [], [], []
    shapes: dict = {}
    for _ in range(args.micro_reps):
        t0 = time.perf_counter()
        wav, sr = _load_audio(path)
        tl.append(time.perf_counter() - t0)

        t0 = time.perf_counter()
        lat = rt.codec.encode_waveform(
            wav.unsqueeze(0), sample_rate=int(sr), normalize_db=-16.0, ensure_max=True
        )
        _sync(rt.codec_device)
        ten.append(time.perf_counter() - t0)

        t0 = time.perf_counter()
        lat_raw = rt.codec.encode_waveform(
            wav.unsqueeze(0), sample_rate=int(sr), normalize_db=None, ensure_max=True
        )
        _sync(rt.codec_device)
        tenraw.append(time.perf_counter() - t0)

        patched = patchify_latent(lat.cpu(), rt.model_cfg.latent_patch_size).to(
            device=rt.model_device, dtype=runtime_dtype
        )
        mask = torch.ones(
            (patched.shape[0], patched.shape[1]), dtype=torch.bool, device=rt.model_device
        )
        t0 = time.perf_counter()
        with torch.inference_mode():
            seq, m2 = patch_sequence_with_mask(
                seq=patched, mask=mask, patch_size=rt.model_cfg.speaker_patch_size
            )
            st = rt.model.speaker_encoder(seq, m2)
            st = rt.model.speaker_norm(st)
            st, m3 = rt.model._prepend_masked_mean_token(st, m2)
        _sync(rt.model_device)
        tref.append(time.perf_counter() - t0)

        shapes = {
            "wav": list(wav.shape),
            "sr_in": int(sr),
            "latent": list(lat.shape),
            "patched": list(patched.shape),
            "speaker_encoder_in": list(seq.shape),
            "speaker_state": list(st.shape),
            "speaker_mask": list(m3.shape),
            "latent_vs_raw_max_abs": float((lat - lat_raw).abs().max()),
        }

    def med(v):
        return round(statistics.median(v) * 1000.0, 1)

    def mn(v):
        return round(min(v) * 1000.0, 1)

    row = {
        "ref_s": s,
        "shapes": shapes,
        "load_ms": mn(tl),
        "encode_waveform_norm16_ms": mn(ten),
        "encode_waveform_nonorm_ms": mn(tenraw),
        "audiotools_norm_ms": round(mn(ten) - mn(tenraw), 1),
        "ref_encoder_ms": mn(tref),
        "prepare_reference_sum_ms": round(mn(tl) + mn(ten), 1),
        "median_ms": {
            "load": med(tl),
            "encode_norm16": med(ten),
            "encode_nonorm": med(tenraw),
            "ref_encoder": med(tref),
        },
    }
    out["ref_encode_micro"].append(row)
    print(json.dumps(row, ensure_ascii=False), flush=True)

Path(args.out).parent.mkdir(parents=True, exist_ok=True)
Path(args.out).write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
print("[lab] written " + args.out, file=sys.stderr)
