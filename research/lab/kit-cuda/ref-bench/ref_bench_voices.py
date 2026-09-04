#!/usr/bin/env python3
"""32 便の台本＝**実機の参照ボイス**（VOICEVOX／COEIROINK で作った wav）を当てたときの
コストを段別に測る。`lab/bin/ref_bench.py`（31 便）を土台に、「参照秒数から合成した素材」
ではなく **`voices/` の実ファイルを回す**版にしたもの。

upstream は **読むだけ**（import のみ・1 バイトも変更しない）。上流の書き換えは
すべて Python の monkeypatch（`torchaudio.load/save` を soundfile へ差し替えるシムだけ）。

条件（`--voices` で絞らなければ manifest の全ファイル）:
  - `no-ref`                      … 参照なし（CFG バッチ 3・これまでの全実測の条件）
  - `wav:<stem>`  各 voice ファイル … 通常経路（ref_wav・毎回 DACVAE encoder が走る）
  - `lat:<stem>`  各 voice の潜在   … `--with-latent` のときだけ。31 §3「逃げ道」の実測

測るもの:
  A) 端から端まで（`InferenceRuntime.synthesize`）を 条件 × `--reps` 回。
     **rep を外側・条件を内側に回す（interleave 固定）**＝機体の混み方が条件に偏らない。
     段別は `SamplingResult.stage_timings` をそのまま採る
     （prepare_reference / tokenize_text / predict_duration / sample_rf /
       unpatchify_latent / decode_latent / silentcipher_watermark）。
  B) 参照符号化の分解（voice ファイルごとに 1 回だけ・`--micro-reps` 反復）:
       load        = `inference_runtime._load_audio`
       encode_norm = `DACVAECodec.encode_waveform`（既定＝ラウドネス正規化 -16 dB 込み）
       encode_raw  = 同上・`normalize_db=None`
       ref_encoder = `patch_sequence_with_mask` → `ReferenceLatentEncoder`(8層) →
                     `speaker_norm` → `_prepend_masked_mean_token`
     ここで **潜在フレーム数と speaker トークン数**を実測し、31 §1-2 の式
     `floor(round(sec*25)/4)+1` と突き合わせる。

使い方（3090 機・キット内）:
  .venv-cu130\\Scripts\\python.exe ref-bench\\ref_bench_voices.py --device cuda \\
      --precision bf16 --steps 40 --reps 3 --len short --with-latent --save-wav \\
      --tag a_bf16_s40_short --out results\\ref_bench_a_bf16_s40_short.json

使い方（lab の CPU 素振り）:
  python ref_bench_voices.py --device cpu --precision fp32 --steps 4 --reps 1 \\
      --voices vv_zundamon --with-latent --save-wav --tag smoke --out out/32_smoke.json
"""
from __future__ import annotations

import argparse
import json
import statistics
import sys
import time
from pathlib import Path

import soundfile as sf
import torch

SHORT = "こんにちは、読み分けちゃんのテストです。"
LONG = (
    "読み分けちゃん2 は、配信中の読み上げを担当するソフトです。"
    "今日は九番目のエンジンとして、彩り音声合成を試しています。"
)
CAPTION = "落ち着いた女性の声で、丁寧に話す。"

HERE = Path(__file__).resolve().parent

ap = argparse.ArgumentParser()
ap.add_argument("--device", default="cuda", help="cuda / cuda:N / cpu / xpu ...")
ap.add_argument("--codec-device", default=None, help="既定＝--device と同じ")
ap.add_argument("--precision", default="bf16", choices=["fp32", "bf16"])
ap.add_argument("--steps", type=int, default=40, help="Euler ステップ数")
ap.add_argument("--reps", type=int, default=3, help="各条件の反復。rep1=cold・rep2 以降=warm")
ap.add_argument("--len", dest="length", default="short", choices=["short", "long"])
ap.add_argument("--caption", default=CAPTION)
ap.add_argument("--seed", type=int, default=1234)
ap.add_argument("--checkpoint", default="Aratako/Irodori-TTS-v4.1-Small")
ap.add_argument("--voices-dir", default=None, help="既定＝この台本と同じ場所の voices/")
ap.add_argument("--manifest", default=None, help="既定＝<voices-dir>/manifest.json")
ap.add_argument(
    "--voices",
    nargs="+",
    default=None,
    help="voice_id の絞り込み（例 vv_zundamon co_ameno）。省略で manifest の全部",
)
ap.add_argument(
    "--with-latent",
    action="store_true",
    help="各 voice の参照潜在を runtime と同じ経路で事前計算して "
    "<results>/ref-latent/<file>.pt に保存し、ref_latent 経路の条件も回す（31 §3 の逃げ道）",
)
ap.add_argument(
    "--save-wav",
    action="store_true",
    help="最終 rep の出力 wav を <results>/ref-wav/<tag>_<cond>.wav に保存する（聴き比べ用）",
)
ap.add_argument(
    "--seconds",
    type=float,
    default=None,
    help="出力尺を固定して duration 予測をバイパス（参照の有無で S_lat が変わるのを止める）",
)
ap.add_argument("--micro-reps", type=int, default=2, help="B) 参照符号化の分解測定の反復")
ap.add_argument("--skip-micro", action="store_true", help="B) を飛ばす")
ap.add_argument("--skip-synth", action="store_true", help="A) を飛ばす")
ap.add_argument("--threads", type=int, default=8, help="torch.set_num_threads（CPU のみ）")
ap.add_argument("--results-dir", default=None, help="既定＝--out の親ディレクトリ")
ap.add_argument("--tag", default=None)
ap.add_argument("--out", required=True)
args = ap.parse_args()

voices_dir = Path(args.voices_dir) if args.voices_dir else (HERE / "voices")
manifest_path = Path(args.manifest) if args.manifest else (voices_dir / "manifest.json")
out_path = Path(args.out).resolve()
results_dir = Path(args.results_dir).resolve() if args.results_dir else out_path.parent
tag = args.tag or out_path.stem

if not manifest_path.is_file():
    print(f"[ref] ERROR: manifest not found: {manifest_path}", file=sys.stderr)
    raise SystemExit(2)

entries = json.loads(manifest_path.read_text(encoding="utf-8"))
if args.voices:
    want = set(args.voices)
    unknown = want - {str(e.get("voice_id")) for e in entries}
    if unknown:
        print(f"[ref] ERROR: unknown voice_id(s): {sorted(unknown)}", file=sys.stderr)
        raise SystemExit(2)
    entries = [e for e in entries if str(e.get("voice_id")) in want]
if not entries:
    print("[ref] ERROR: no voice entries selected.", file=sys.stderr)
    raise SystemExit(2)

for e in entries:
    p = voices_dir / str(e["file"])
    if not p.is_file():
        print(f"[ref] ERROR: voice file missing: {p}", file=sys.stderr)
        raise SystemExit(2)
    e["_path"] = p
    e["_stem"] = p.stem
    info = sf.info(str(p))
    e["_actual_seconds"] = round(float(info.frames) / float(info.samplerate), 3)
    e["_actual_sr"] = int(info.samplerate)
    e["_channels"] = int(info.channels)
    e["_bytes"] = int(p.stat().st_size)

# ---------------------------------------------------------------- torchaudio シム
# torchaudio 2.10 の load/save は torchcodec 専用で、torchcodec が無いと ImportError。
# upstream の _load_audio は `except RuntimeError` しか持たないので ImportError が
# 貫通する（18 §3-e / 31 §7）。ここで soundfile 実装へ差し替える＝upstream は無改変。
import torchaudio  # noqa: E402

_probe = str(entries[0]["_path"])
_shim = {"load": False, "save": False}
try:
    torchaudio.load(_probe)
except Exception as exc:  # noqa: BLE001
    _shim["load"] = f"{type(exc).__name__}: {str(exc)[:120]}"

    def _load_soundfile(uri, *a, **kw):
        data, sr = sf.read(str(uri), dtype="float32", always_2d=True)
        return torch.from_numpy(data.T.copy()), int(sr)

    torchaudio.load = _load_soundfile


def _save_soundfile(uri, src, sample_rate, **kw):
    audio = src.detach().to(device="cpu", dtype=torch.float32)
    arr = audio.squeeze(0).numpy() if audio.ndim == 2 and audio.shape[0] == 1 else audio.numpy()
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


IS_CUDA = args.device.startswith("cuda")

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
runtime_dtype = next(rt.model.parameters()).dtype

MAX_REF_S = float(rt.default_max_ref_seconds)
SPK_PATCH = int(rt.model_cfg.speaker_patch_size)
LAT_PATCH = int(rt.model_cfg.latent_patch_size)
CODEC_SR = int(rt.codec.sample_rate)
HOP = int(rt.codec.model.hop_length)
LATENT_FPS = float(CODEC_SR) / float(HOP)


def expected_tokens(seconds: float) -> dict:
    """31 §1-2 の式＝floor(round(sec*25)/4)+1。fps と patch は実機の値から採る。

    潜在フレーム数の実測は round ではなく **ceil**（端数のフレームも 1 本出る）なので
    ceil 版も併記する。speaker トークン数は //4 で丸められるため両者はふつう一致する。
    """
    frames = int(round(seconds * LATENT_FPS))
    frames_ceil = int(-(-seconds * LATENT_FPS // 1))
    return {
        "latent_fps": round(LATENT_FPS, 3),
        "latent_frames": frames,
        "latent_frames_ceil": frames_ceil,
        "speaker_tokens": frames // SPK_PATCH + 1,
        "speaker_tokens_ceil": frames_ceil // SPK_PATCH + 1,
    }


out: dict = {
    "tag": tag,
    "device": args.device,
    "codec_device": codec_device,
    "precision": args.precision,
    "steps": args.steps,
    "reps": args.reps,
    "seed": args.seed,
    "text_len": args.length,
    "seconds_override": args.seconds,
    "with_latent": bool(args.with_latent),
    "save_wav": bool(args.save_wav),
    "voices_dir": str(voices_dir),
    "manifest": str(manifest_path),
    "voices_filter": args.voices,
    "threads": int(args.threads) if args.device == "cpu" else None,
    "torch": torch.__version__,
    "torchaudio_shim": _shim,
    "hf_resolve_s": round(t_resolve, 3),
    "model_load_s": round(t_load, 3),
    "model_dtype": str(runtime_dtype),
    "codec_sample_rate": CODEC_SR,
    "codec_hop_length": HOP,
    "latent_fps": round(LATENT_FPS, 3),
    "speaker_patch_size": SPK_PATCH,
    "latent_patch_size": LAT_PATCH,
    "ref_max_seconds_default": MAX_REF_S,
    "voices": [],
    "latents": [],
    "runs": [],
    "ref_encode_micro": [],
}
if IS_CUDA:
    out["gpu_name"] = torch.cuda.get_device_name(rt.model_device)
    out["torch_cuda"] = torch.version.cuda
    out["torch_hip"] = torch.version.hip
    out["vram_after_load_mb"] = round(torch.cuda.memory_allocated() / 1048576.0, 1)

for e in entries:
    out["voices"].append(
        {
            "voice_id": e.get("voice_id"),
            "file": e.get("file"),
            "engine": e.get("engine"),
            "speaker": e.get("speaker"),
            "style": e.get("style"),
            "manifest_duration_s": e.get("duration_s"),
            "manifest_sample_rate": e.get("sample_rate"),
            "actual_seconds": e["_actual_seconds"],
            "actual_sample_rate": e["_actual_sr"],
            "channels": e["_channels"],
            "bytes": e["_bytes"],
            "over_max_ref_seconds": bool(e["_actual_seconds"] > MAX_REF_S),
            "expected": expected_tokens(e["_actual_seconds"]),
        }
    )

text = SHORT if args.length == "short" else LONG

# ============================ 参照潜在の事前計算（--with-latent・31 §3 の逃げ道）
# runtime の wav 経路（inference_runtime._load_reference_latent の else 枝・:929-956）と
# 同じ順で作る＝_load_audio → 単一 wav なら秒で先に切る → encode_waveform → .cpu()。
latent_paths: dict[str, Path] = {}
if args.with_latent:
    lat_dir = results_dir / "ref-latent"
    lat_dir.mkdir(parents=True, exist_ok=True)
    for e in entries:
        path = str(e["_path"])
        t0 = time.perf_counter()
        wav, sr = _load_audio(path)
        trimmed = False
        if MAX_REF_S > 0:
            max_ref_samples = max(1, int(MAX_REF_S * float(sr)))
            if wav.shape[1] > max_ref_samples:
                wav = wav[:, :max_ref_samples]
                trimmed = True
        piece = rt.codec.encode_waveform(
            wav.unsqueeze(0), sample_rate=int(sr), normalize_db=-16.0, ensure_max=True
        ).cpu()
        _sync(rt.codec_device)
        dt = time.perf_counter() - t0
        lat2d = piece[0].contiguous()
        dst = lat_dir / (e["_stem"] + ".pt")
        torch.save(lat2d, str(dst))
        latent_paths[e["_stem"]] = dst
        row = {
            "voice_id": e.get("voice_id"),
            "file": e.get("file"),
            "pt": str(dst),
            "precompute_ms": round(dt * 1000.0, 1),
            "latent_shape": list(lat2d.shape),
            "latent_dtype": str(lat2d.dtype),
            "latent_frames": int(lat2d.shape[0]),
            "expected_latent_frames": expected_tokens(e["_actual_seconds"])["latent_frames"],
            "speaker_tokens_expected": int(lat2d.shape[0]) // SPK_PATCH + 1,
            "pt_bytes": int(dst.stat().st_size),
            "trimmed_by_max_ref_seconds": trimmed,
        }
        out["latents"].append(row)
        print(json.dumps(row, ensure_ascii=False), flush=True)

# ------------------------------------------------------------------ 条件を並べる
# 条件＝no-ref ＋ 各 voice ファイル（＋ --with-latent のとき各 latent）
conds: list[dict] = [{"key": "no-ref", "kind": "no-ref", "voice_id": None, "file": None}]
for e in entries:
    conds.append(
        {
            "key": "wav:" + e["_stem"],
            "kind": "wav",
            "voice_id": e.get("voice_id"),
            "file": e.get("file"),
            "path": str(e["_path"]),
            "ref_seconds": e["_actual_seconds"],
        }
    )
if args.with_latent:
    for e in entries:
        conds.append(
            {
                "key": "lat:" + e["_stem"],
                "kind": "latent",
                "voice_id": e.get("voice_id"),
                "file": e.get("file"),
                "path": str(latent_paths[e["_stem"]]),
                "ref_seconds": e["_actual_seconds"],
            }
        )

out["conditions"] = [dict(c) for c in conds]

wav_dir = results_dir / "ref-wav"
if args.save_wav:
    wav_dir.mkdir(parents=True, exist_ok=True)


def _safe(name: str) -> str:
    return "".join(ch if (ch.isalnum() or ch in "-_.") else "_" for ch in name)


# ================================================================ A) 端から端まで
if not args.skip_synth:
    # rep を外側・条件を内側（interleave 固定）
    plan = [(c, rep) for rep in range(1, args.reps + 1) for c in conds]
    for c, rep in plan:
        req = SamplingRequest(
            text=text,
            caption=args.caption,
            no_ref=(c["kind"] == "no-ref"),
            ref_wav=c["path"] if c["kind"] == "wav" else None,
            ref_latent=c["path"] if c["kind"] == "latent" else None,
            seconds=args.seconds,
            num_steps=args.steps,
            seed=args.seed,
        )
        if IS_CUDA:
            torch.cuda.reset_peak_memory_stats()
        t0 = time.perf_counter()
        res = rt.synthesize(req, log_fn=None)
        _sync(rt.model_device)
        wall = time.perf_counter() - t0
        secs = res.audio.shape[-1] / float(res.sample_rate)
        row = {
            "cond": c["key"],
            "kind": c["kind"],
            "voice_id": c["voice_id"],
            "file": c["file"],
            "ref_seconds": c.get("ref_seconds"),
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
        if IS_CUDA:
            row["vram_peak_mb"] = round(torch.cuda.max_memory_allocated() / 1048576.0, 1)
        out["runs"].append(row)
        print(json.dumps(row, ensure_ascii=False), flush=True)

        if args.save_wav and rep == args.reps:
            dst = wav_dir / (_safe(tag) + "_" + _safe(c["key"].replace(":", "-")) + ".wav")
            audio = res.audio.detach().to(device="cpu", dtype=torch.float32)
            arr = audio.squeeze(0).numpy() if audio.ndim == 2 and audio.shape[0] == 1 else audio.numpy()
            sf.write(str(dst), arr, int(res.sample_rate))
            row["wav_out"] = str(dst)

    # ---------------------------------------------------------------- summary
    # 混んだ機では min が最も素直（他プロセスの負荷は足し算にしか働かない）。
    # warm 中央値は「常駐サーバでの実効値」に近い（rep1=cold はモデルの初回暖機を被る）。
    def _stat_block(rows: list[dict]) -> dict:
        keys = sorted({k for r in rows for k in r["stage_ms"]})
        blk = {
            "n": len(rows),
            "min_total_s": min(r["total_to_decode_s"] for r in rows),
            "min_rtf": min(r["rtf"] for r in rows),
            "min_stage_ms": {k: min(r["stage_ms"].get(k, 0.0) for r in rows) for k in keys},
        }
        warm = [r for r in rows if r["phase"] == "warm"]
        if warm:
            blk["warm_n"] = len(warm)
            blk["warm_median_total_s"] = round(
                statistics.median(r["total_to_decode_s"] for r in warm), 3
            )
            blk["warm_median_rtf"] = round(statistics.median(r["rtf"] for r in warm), 4)
            blk["warm_median_stage_ms"] = {
                k: round(statistics.median(r["stage_ms"].get(k, 0.0) for r in warm), 1)
                for k in keys
            }
        else:
            blk["warm_n"] = 0
            blk["warm_median_total_s"] = None
            blk["warm_median_rtf"] = None
            blk["warm_median_stage_ms"] = None
        return blk

    summary: dict = {}
    for c in conds:
        rows = [r for r in out["runs"] if r["cond"] == c["key"]]
        if not rows:
            continue
        blk = _stat_block(rows)
        blk["kind"] = c["kind"]
        blk["voice_id"] = c["voice_id"]
        blk["ref_seconds"] = c.get("ref_seconds")
        blk["audio_s"] = rows[0]["audio_s"]
        summary[c["key"]] = blk

    base = summary.get("no-ref")
    if base:
        for key, blk in summary.items():
            d: dict = {
                "min_total_pct": round(
                    (blk["min_total_s"] / base["min_total_s"] - 1.0) * 100.0, 1
                ),
                "min_total_delta_s": round(blk["min_total_s"] - base["min_total_s"], 3),
                "audio_s_delta": round(blk["audio_s"] - base["audio_s"], 3),
            }
            if blk["warm_median_total_s"] and base["warm_median_total_s"]:
                d["warm_median_total_pct"] = round(
                    (blk["warm_median_total_s"] / base["warm_median_total_s"] - 1.0) * 100.0, 1
                )
                d["warm_median_delta_s"] = round(
                    blk["warm_median_total_s"] - base["warm_median_total_s"], 3
                )
            for st in ("prepare_reference", "predict_duration", "sample_rf", "decode_latent"):
                b = base["min_stage_ms"].get(st)
                v = blk["min_stage_ms"].get(st)
                if b is None or v is None:
                    continue
                d[st + "_delta_ms"] = round(v - b, 1)
                if b > 0.5:
                    d[st + "_pct"] = round((v / b - 1.0) * 100.0, 1)
            blk["vs_no_ref"] = d
    out["summary"] = summary
    print(json.dumps({"summary": summary}, ensure_ascii=False), flush=True)

# ================================================ B) 参照符号化の分解（voice ごと）
if not args.skip_micro:
    for e in entries:
        path = str(e["_path"])
        tl, ten, tenraw, tref = [], [], [], []
        shapes: dict = {}
        for _ in range(max(1, args.micro_reps)):
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

            patched = patchify_latent(lat.cpu(), LAT_PATCH).to(
                device=rt.model_device, dtype=runtime_dtype
            )
            mask = torch.ones(
                (patched.shape[0], patched.shape[1]), dtype=torch.bool, device=rt.model_device
            )
            t0 = time.perf_counter()
            with torch.inference_mode():
                seq, m2 = patch_sequence_with_mask(
                    seq=patched, mask=mask, patch_size=SPK_PATCH
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
                "latent_vs_raw_max_abs": float((lat.float() - lat_raw.float()).abs().max()),
            }

        def med(v):
            return round(statistics.median(v) * 1000.0, 1)

        def mn(v):
            return round(min(v) * 1000.0, 1)

        exp = expected_tokens(e["_actual_seconds"])
        row = {
            "voice_id": e.get("voice_id"),
            "file": e.get("file"),
            "ref_seconds": e["_actual_seconds"],
            "shapes": shapes,
            "latent_frames_measured": int(shapes["latent"][1]),
            "latent_frames_expected": exp["latent_frames"],
            "latent_frames_expected_ceil": exp["latent_frames_ceil"],
            "speaker_tokens_measured": int(shapes["speaker_state"][1]),
            "speaker_tokens_expected": exp["speaker_tokens"],
            "formula_matches": bool(
                int(shapes["speaker_state"][1]) == exp["speaker_tokens"]
            ),
            "load_ms": mn(tl),
            "encode_waveform_norm16_ms": mn(ten),
            "encode_waveform_nonorm_ms": mn(tenraw),
            "audiotools_norm_ms": round(mn(ten) - mn(tenraw), 1),
            "ref_encoder_ms": mn(tref),
            "prepare_reference_sum_ms": round(mn(tl) + mn(ten), 1),
            "ms_per_ref_second": round(mn(ten) / max(1e-6, e["_actual_seconds"]), 1),
            "median_ms": {
                "load": med(tl),
                "encode_norm16": med(ten),
                "encode_nonorm": med(tenraw),
                "ref_encoder": med(tref),
            },
        }
        out["ref_encode_micro"].append(row)
        print(json.dumps(row, ensure_ascii=False), flush=True)

out_path.parent.mkdir(parents=True, exist_ok=True)
out_path.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
print("[ref] written " + str(out_path), file=sys.stderr)
