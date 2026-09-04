"""段5b: fp16 の実害を全ループ（40 step）で測る。あわせて op_block_list 版の
「同名ノード」不具合を回避して単ステップ差も採る。torch 側 encode_conditions の所要も採る。"""

from __future__ import annotations

import json
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
from importlib import import_module

loop_mod = import_module("40_loop") if False else None  # noqa: F841

SRC = C.ONNX_DIR / "dit_step.onnx"
FP16 = C.ONNX_DIR / "dit_step_fp16.onnx"
BLOCK_SRC = C.ONNX_DIR / "dit_step_fp16_block.onnx"
BLOCK_FIX = C.ONNX_DIR / "dit_step_fp16_block_fix.onnx"
BLOCK = ["ReduceMean", "Sqrt", "Reciprocal", "Cos", "Sin", "Softmax", "Tanh"]

CFG_TEXT = CFG_CAPTION = 3.0
CFG_MIN_T, CFG_MAX_T = 0.5, 1.0


def t_schedule(n):
    u = torch.linspace(0.0, 1.0, n + 1)
    return ((1.0 - u) * 0.999).numpy()


def ort_loop(sess, cond, x0, num_steps):
    ts, tm = cond["text_state"], cond["text_mask"]
    ss, sm = cond["speaker_state"], cond["speaker_mask"]
    cs, cm = cond["caption_state"], cond["caption_mask"]
    TS = np.concatenate([ts, np.zeros_like(ts), ts], 0)
    TM = np.concatenate([tm, np.zeros_like(tm), tm], 0)
    SS = np.concatenate([ss] * 3, 0)
    SM = np.concatenate([sm] * 3, 0)
    CS = np.concatenate([cs, cs, np.zeros_like(cs)], 0)
    CM = np.concatenate([cm, cm, np.zeros_like(cm)], 0)
    sched = t_schedule(num_steps)
    x = x0.copy()
    for i in range(num_steps):
        t, tn = float(sched[i]), float(sched[i + 1])
        if CFG_MIN_T <= t <= CFG_MAX_T:
            o = sess.run(["v"], {
                "x_t": np.ascontiguousarray(np.concatenate([x] * 3, 0)),
                "t": np.full((3,), t, dtype=np.float32),
                "text_state": TS, "text_mask": TM, "speaker_state": SS,
                "speaker_mask": SM, "caption_state": CS, "caption_mask": CM})[0]
            c0, c1, c2 = o[0:1], o[1:2], o[2:3]
            v = c0 + CFG_TEXT * (c0 - c1) + CFG_CAPTION * (c0 - c2)
        else:
            v = sess.run(["v"], {
                "x_t": np.ascontiguousarray(x), "t": np.full((1,), t, dtype=np.float32),
                "text_state": ts, "text_mask": tm, "speaker_state": ss,
                "speaker_mask": sm, "caption_state": cs, "caption_mask": cm})[0]
        x = x + v * (tn - t)
    return x


def fix_dup_names(rep):
    try:
        m = onnx.load(str(BLOCK_SRC))
        seen, n_fix = {}, 0
        for node in m.graph.node:
            nm = node.name
            if nm in seen:
                seen[nm] += 1
                node.name = f"{nm}__dup{seen[nm]}"
                n_fix += 1
            else:
                seen[nm] = 0
        rep["block_dup_names_fixed"] = n_fix
        d = BLOCK_FIX.with_suffix(".onnx.data")
        for p in (BLOCK_FIX, d):
            if p.exists():
                p.unlink()
        onnx.save(m, str(BLOCK_FIX), save_as_external_data=True,
                  all_tensors_to_one_file=True, location=d.name)
        del m
        return True
    except Exception:
        rep["block_fix_error"] = traceback.format_exc()[-2000:]
        return False


def main():
    torch.set_num_threads(8)
    rep = {}
    cd = np.load(C.TMP / "cond_real.npz")
    cond = {k: cd[k] for k in ("text_state", "text_mask", "speaker_state", "speaker_mask",
                               "caption_state", "caption_mask")}
    x0 = np.load(C.TMP / "x0.npz")["x0"]

    so = ort.SessionOptions()
    so.intra_op_num_threads = 8
    so.inter_op_num_threads = 1

    # --- op_block_list 版の同名ノードを直して単ステップ照合 ---
    if BLOCK_SRC.exists() and fix_dup_names(rep):
        try:
            s32 = ort.InferenceSession(str(SRC), so, providers=["CPUExecutionProvider"])
            feeds = {
                "x_t": np.ascontiguousarray(x0), "t": np.full((1,), 0.999, dtype=np.float32),
                "text_state": cond["text_state"], "text_mask": cond["text_mask"],
                "speaker_state": cond["speaker_state"], "speaker_mask": cond["speaker_mask"],
                "caption_state": cond["caption_state"], "caption_mask": cond["caption_mask"]}
            ref = s32.run(["v"], feeds)[0]
            del s32
            sb = ort.InferenceSession(str(BLOCK_FIX), so, providers=["CPUExecutionProvider"])
            ob = sb.run(["v"], feeds)[0]
            rep["fp16_block_diff_b1"] = C.diffs(ref, ob)
            tsx = []
            for _ in range(2):
                sb.run(["v"], feeds)
            for _ in range(3):
                t1 = time.perf_counter()
                sb.run(["v"], feeds)
                tsx.append(time.perf_counter() - t1)
            rep["fp16_block_sec_b1"] = round(float(np.median(tsx)), 4)
            del sb
        except Exception:
            rep["fp16_block_eval_error"] = traceback.format_exc()[-2000:]
            print(rep["fp16_block_eval_error"], flush=True)

    # --- 40 step 全ループ fp32 vs fp16 ---
    s32 = ort.InferenceSession(str(SRC), so, providers=["CPUExecutionProvider"])
    t0 = time.perf_counter()
    z32 = ort_loop(s32, cond, x0, 40)
    rep["loop40_fp32_sec"] = round(time.perf_counter() - t0, 2)
    del s32
    s16 = ort.InferenceSession(str(FP16), so, providers=["CPUExecutionProvider"])
    t0 = time.perf_counter()
    z16 = ort_loop(s16, cond, x0, 40)
    rep["loop40_fp16_sec"] = round(time.perf_counter() - t0, 2)
    del s16
    rep["loop40_latent_diff_fp16_vs_fp32"] = C.diffs(z32, z16)

    # --- torch コーデックで復号して wav 比較 ---
    rt, _ = C.load_runtime()
    with torch.inference_mode():
        a32 = rt.codec.decode_latent(torch.from_numpy(z32)).cpu()[0].numpy()
        a16 = rt.codec.decode_latent(torch.from_numpy(z16)).cpu()[0].numpy()
    n = min(a32.shape[-1], a16.shape[-1])
    rep["loop40_wav_diff_fp16_vs_fp32"] = C.diffs(a32[:, :n], a16[:, :n])
    rep["wav_absmax"] = float(np.abs(a32).max())
    import soundfile as sf

    sf.write(str(C.OUT / "13_steps40_ort_fp16.wav"), a16.T, int(rt.codec.sample_rate))

    # --- torch 側 encode_conditions の所要（ORT 0.618 s との比較用）---
    C.install_real_rope()
    rt.model._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    rt.model.speaker_encoder._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    C.bake_rope_caches(rt.model)
    cc = C.build_conditions(rt)
    kw = dict(
        text_input_ids=cc["text_ids"], text_mask=cc["text_mask_in"],
        ref_latent=cc["ref_latent"], ref_mask=cc["ref_mask_in"],
        caption_input_ids=cc["cap_ids"], caption_mask=cc["cap_mask_in"])
    with torch.inference_mode():
        for _ in range(2):
            rt.model.encode_conditions(**kw)
        tsx = []
        for _ in range(5):
            t1 = time.perf_counter()
            rt.model.encode_conditions(**kw)
            tsx.append(time.perf_counter() - t1)
    rep["torch_encode_conditions_sec_median"] = round(float(np.median(tsx)), 4)

    C.jdump(rep, C.OUT / "13_fp16_loop.json")
    print(json.dumps(rep, ensure_ascii=False, indent=2)[:5000])


if __name__ == "__main__":
    main()
