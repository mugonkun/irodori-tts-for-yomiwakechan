"""段5: DiT ステップ ONNX の fp16 変換（サイズと照合誤差）。RMSNorm/AdaLN/RoPE を fp32 に
残す必要があるかを op_block_list の有無で比べる。"""

from __future__ import annotations

import json
import shutil
import sys
import time
import traceback
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import torch

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402

SRC = C.ONNX_DIR / "dit_step.onnx"

# fp32 で残す候補＝RMSNorm(ReduceMean/Sqrt/Reciprocal)・AdaLN(Tanh)・RoPE(Cos/Sin)・Softmax
BLOCK = ["ReduceMean", "Sqrt", "Reciprocal", "Cos", "Sin", "Softmax", "Tanh"]


def build(dst: Path, block_list, rep, key):
    t0 = time.perf_counter()
    try:
        from onnxruntime.transformers.float16 import convert_float_to_float16

        m = onnx.load(str(SRC))  # external data 込みで読む
        m16 = convert_float_to_float16(
            m, keep_io_types=True, op_block_list=block_list, disable_shape_infer=True
        )
        if dst.exists():
            dst.unlink()
        d = dst.with_suffix(".onnx.data")
        if d.exists():
            d.unlink()
        onnx.save(
            m16,
            str(dst),
            save_as_external_data=True,
            all_tensors_to_one_file=True,
            location=d.name,
        )
        files = sorted(dst.parent.glob(dst.stem + "*"))
        rep[key] = {
            "status": "OK",
            "sec": round(time.perf_counter() - t0, 2),
            "files": {f.name: f.stat().st_size for f in files},
            "total_bytes": sum(f.stat().st_size for f in files),
        }
        del m, m16
        return True
    except Exception:
        rep[key] = {"status": "FAIL", "error_tail": traceback.format_exc()[-2500:]}
        print(rep[key]["error_tail"], flush=True)
        return False


def evaluate(path: Path, rep, key, cond, x0, ref_b1, ref_b3):
    try:
        so = ort.SessionOptions()
        so.intra_op_num_threads = 8
        so.inter_op_num_threads = 1
        sess = ort.InferenceSession(str(path), so, providers=["CPUExecutionProvider"])
    except Exception:
        rep[key + "_session_error"] = traceback.format_exc()[-2000:]
        return

    def make(B, x, tval=0.999):
        return {
            "x_t": np.ascontiguousarray(np.concatenate([x] * B, 0)),
            "t": np.full((B,), tval, dtype=np.float32),
            "text_state": np.ascontiguousarray(np.concatenate([cond["text_state"]] * B, 0)),
            "text_mask": np.ascontiguousarray(np.concatenate([cond["text_mask"]] * B, 0)),
            "speaker_state": np.ascontiguousarray(np.concatenate([cond["speaker_state"]] * B, 0)),
            "speaker_mask": np.ascontiguousarray(np.concatenate([cond["speaker_mask"]] * B, 0)),
            "caption_state": np.ascontiguousarray(np.concatenate([cond["caption_state"]] * B, 0)),
            "caption_mask": np.ascontiguousarray(np.concatenate([cond["caption_mask"]] * B, 0)),
        }

    o1 = sess.run(["v"], make(1, x0))[0]
    o3 = sess.run(["v"], make(3, x0))[0]
    rep[key + "_diff_b1"] = C.diffs(ref_b1, o1)
    rep[key + "_diff_b3"] = C.diffs(ref_b3, o3)
    for B in (1, 3):
        feeds = make(B, x0)
        for _ in range(2):
            sess.run(["v"], feeds)
        ts = []
        for _ in range(5):
            t1 = time.perf_counter()
            sess.run(["v"], feeds)
            ts.append(time.perf_counter() - t1)
        rep[f"{key}_sec_b{B}"] = round(float(np.median(ts)), 4)


def main():
    torch.set_num_threads(8)
    rep = {}
    z = np.load(C.TMP / "x0.npz")
    x0 = z["x0"]
    cd = np.load(C.TMP / "cond_real.npz")
    cond = {k: cd[k] for k in ("text_state", "text_mask", "speaker_state", "speaker_mask",
                               "caption_state", "caption_mask")}

    # fp32 ORT を基準にする（torch との差は 13_ort_check.json 側で確認済み）
    so = ort.SessionOptions()
    so.intra_op_num_threads = 8
    so.inter_op_num_threads = 1
    s32 = ort.InferenceSession(str(SRC), so, providers=["CPUExecutionProvider"])

    def make(B, x, tval=0.999):
        return {
            "x_t": np.ascontiguousarray(np.concatenate([x] * B, 0)),
            "t": np.full((B,), tval, dtype=np.float32),
            "text_state": np.ascontiguousarray(np.concatenate([cond["text_state"]] * B, 0)),
            "text_mask": np.ascontiguousarray(np.concatenate([cond["text_mask"]] * B, 0)),
            "speaker_state": np.ascontiguousarray(np.concatenate([cond["speaker_state"]] * B, 0)),
            "speaker_mask": np.ascontiguousarray(np.concatenate([cond["speaker_mask"]] * B, 0)),
            "caption_state": np.ascontiguousarray(np.concatenate([cond["caption_state"]] * B, 0)),
            "caption_mask": np.ascontiguousarray(np.concatenate([cond["caption_mask"]] * B, 0)),
        }

    ref_b1 = s32.run(["v"], make(1, x0))[0]
    ref_b3 = s32.run(["v"], make(3, x0))[0]
    for B in (1, 3):
        feeds = make(B, x0)
        for _ in range(2):
            s32.run(["v"], feeds)
        ts = []
        for _ in range(5):
            t1 = time.perf_counter()
            s32.run(["v"], feeds)
            ts.append(time.perf_counter() - t1)
        rep[f"fp32_sec_b{B}"] = round(float(np.median(ts)), 4)
    rep["fp32_files"] = {f.name: f.stat().st_size for f in sorted(C.ONNX_DIR.glob("dit_step.onnx*"))}
    del s32

    p_plain = C.ONNX_DIR / "dit_step_fp16.onnx"
    if build(p_plain, [], rep, "fp16_plain"):
        evaluate(p_plain, rep, "fp16_plain", cond, x0, ref_b1, ref_b3)
    p_block = C.ONNX_DIR / "dit_step_fp16_block.onnx"
    if build(p_block, BLOCK, rep, "fp16_block"):
        evaluate(p_block, rep, "fp16_block", cond, x0, ref_b1, ref_b3)
    rep["block_list"] = BLOCK

    # サイズ表（全 onnx）
    rep["size_table"] = {
        f.name: f.stat().st_size for f in sorted(C.ONNX_DIR.iterdir()) if f.is_file()
    }
    rep["disk_total_bytes"] = sum(v for v in rep["size_table"].values())
    C.jdump(rep, C.OUT / "13_fp16.json")
    print(json.dumps(rep, ensure_ascii=False, indent=2)[:7000])


if __name__ == "__main__":
    main()
