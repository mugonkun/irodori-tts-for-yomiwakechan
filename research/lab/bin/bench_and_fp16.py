"""段 2 の所要の取り直し（warmup + 5 回中央値 + スレッド掃引 + ORT op プロファイル）と
段 4 の fp16 変換（onnxruntime.transformers.float16）。

codec のロードを避けるため、torch 側は export ラッパを .pt で持たずに再構築する
（= codec ロードは必要）。潜在は lab/out/latent_steps40.npy を再利用。
"""

from __future__ import annotations

import json
import os
import sys
import time
import traceback
from pathlib import Path

import numpy as np
import torch

sys.path.insert(0, str(Path(__file__).resolve().parent))
from onnx_common import (  # noqa: E402
    ONNX_DIR, OUT_DIR, compare, dump, fold_weight_norm, load_codec, onnx_size, ort_session,
)
from export_decoder import DecoderExport  # noqa: E402
from export_encoder import EncoderExport  # noqa: E402

REPORT: dict = {"stage": "bench+fp16"}


def bench(fn, warmup: int = 1, reps: int = 5) -> dict:
    for _ in range(warmup):
        fn()
    ts = []
    for _ in range(reps):
        t0 = time.perf_counter()
        fn()
        ts.append((time.perf_counter() - t0) * 1000)
    ts_sorted = sorted(ts)
    return {"ms_median": round(ts_sorted[len(ts) // 2], 2),
            "ms_min": round(ts_sorted[0], 2),
            "ms_all": [round(t, 2) for t in ts]}


def main() -> None:
    codec, _ = load_codec("cpu")
    latent_btd = torch.from_numpy(np.load(OUT_DIR / "latent_steps40.npy"))
    latent_bdt = latent_btd.transpose(1, 2).contiguous()
    lat_np = latent_bdt.numpy()

    import soundfile as sf

    data, sr = sf.read(str(OUT_DIR / "steps40_seed1234.wav"), dtype="float32")
    wav = torch.from_numpy(np.asarray(data)).view(1, 1, -1)
    wav_np = wav.numpy()
    audio_s = wav.shape[-1] / 48000.0
    REPORT["audio_seconds"] = round(audio_s, 3)

    dec_w = DecoderExport(codec.model).eval()
    fold_weight_norm(dec_w)
    enc_w = EncoderExport(codec.model).eval()
    fold_weight_norm(enc_w)

    dec_onnx = ONNX_DIR / "dacvae_decoder_fp32.onnx"
    enc_onnx = ONNX_DIR / "dacvae_encoder_fp32.onnx"

    # -------- スレッド掃引 --------
    sweep: dict = {}
    for th in (8, 16):
        torch.set_num_threads(th)
        with torch.inference_mode():
            t_dec = bench(lambda: codec.decode_latent(latent_btd))
            t_enc = bench(lambda: codec.encode_waveform(wav, sr, normalize_db=None))
        ds = ort_session(dec_onnx, threads=th)
        es = ort_session(enc_onnx, threads=th)
        din = ds.get_inputs()[0].name
        ein = es.get_inputs()[0].name
        o_dec = bench(lambda: ds.run(None, {din: lat_np}))
        o_enc = bench(lambda: es.run(None, {ein: wav_np}))
        sweep[f"threads_{th}"] = {
            "torch_decode": t_dec, "ort_decode": o_dec,
            "torch_encode": t_enc, "ort_encode": o_enc,
            "rtf_torch_decode": round(t_dec["ms_median"] / 1000 / audio_s, 4),
            "rtf_ort_decode": round(o_dec["ms_median"] / 1000 / audio_s, 4),
        }
        print(f"[sweep {th}]", json.dumps(sweep[f"threads_{th}"], ensure_ascii=False), flush=True)
    REPORT["thread_sweep"] = sweep
    torch.set_num_threads(8)

    # -------- ORT op プロファイル（decoder・8 スレッド）--------
    try:
        import onnxruntime as ort

        so = ort.SessionOptions()
        so.intra_op_num_threads = 8
        so.inter_op_num_threads = 1
        so.enable_profiling = True
        so.profile_file_prefix = str(OUT_DIR / "ortprof_dec")
        ps = ort.InferenceSession(str(dec_onnx), so, providers=["CPUExecutionProvider"])
        pin = ps.get_inputs()[0].name
        for _ in range(3):
            ps.run(None, {pin: lat_np})
        prof = Path(ps.end_profiling())
        rows = json.loads(prof.read_text(encoding="utf-8"))
        agg: dict[str, list] = {}
        for r in rows:
            if r.get("cat") == "Node" and r.get("name", "").endswith("_kernel_time"):
                op = r["args"].get("op_name", "?")
                a = agg.setdefault(op, [0, 0])
                a[0] += r["dur"]
                a[1] += 1
        total = sum(v[0] for v in agg.values()) or 1
        REPORT["ort_decoder_op_profile_us"] = {
            k: {"us_total": v[0], "calls": v[1], "pct": round(100 * v[0] / total, 1)}
            for k, v in sorted(agg.items(), key=lambda kv: -kv[1][0])[:12]
        }
        REPORT["ort_decoder_profile_file"] = str(prof)
        print("[ort profile]", json.dumps(REPORT["ort_decoder_op_profile_us"], ensure_ascii=False), flush=True)
    except Exception:
        REPORT["ort_decoder_op_profile_us"] = {"error": traceback.format_exc()[-2000:]}

    # -------- fp16 変換 --------
    fp16: dict = {}
    try:
        import onnx
        from onnxruntime.transformers.float16 import convert_float_to_float16

        for tag, src, sample, wrapper in (
            ("decoder", dec_onnx, lat_np, dec_w),
            ("encoder", enc_onnx, wav_np, enc_w),
        ):
            dst = ONNX_DIR / f"dacvae_{tag}_fp16.onnx"
            m = onnx.load(str(src))  # 外部データ込みで読む
            m16 = convert_float_to_float16(m, keep_io_types=True, disable_shape_infer=True)
            onnx.save(m16, str(dst), save_as_external_data=True,
                      all_tensors_to_one_file=True, location=dst.name + ".data",
                      size_threshold=1024)
            info = {"size": onnx_size(dst)}
            try:
                s16 = ort_session(dst, threads=8)
                n16 = s16.get_inputs()[0].name
                o16 = s16.run(None, {n16: sample})[0]
                with torch.no_grad():
                    ref = wrapper(torch.from_numpy(sample)).numpy()
                info["ort_fp16_vs_torch_fp32"] = compare(ref, o16)
                info["timing"] = bench(lambda: s16.run(None, {n16: sample}))
                info["runnable_on_cpu_ep"] = True
            except Exception:
                info["runnable_on_cpu_ep"] = False
                info["error"] = traceback.format_exc()[-2500:]
            fp16[tag] = info
            print(f"[fp16 {tag}]", json.dumps(info, ensure_ascii=False, default=str), flush=True)
    except Exception:
        fp16["error"] = traceback.format_exc()[-2500:]
        print("[fp16] FAILED", fp16["error"], flush=True)
    REPORT["fp16"] = fp16

    dump("onnx_bench_fp16.json", REPORT)


if __name__ == "__main__":
    main()
