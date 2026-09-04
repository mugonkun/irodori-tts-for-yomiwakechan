"""段1c: ORT CPU で DiT 1 ステップを回し、torch（実数 RoPE patch 後）と照合＋所要比較。"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import torch

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402

MODEL = C.ONNX_DIR / "dit_step.onnx"


def main():
    torch.set_num_threads(8)
    rep = {}

    m = onnx.load(str(MODEL), load_external_data=False)
    rep["opset"] = [{"domain": o.domain, "version": o.version} for o in m.opset_import]
    ops = {}
    for n in m.graph.node:
        ops[n.op_type] = ops.get(n.op_type, 0) + 1
    rep["op_histogram"] = dict(sorted(ops.items(), key=lambda kv: -kv[1]))
    rep["n_nodes"] = len(m.graph.node)
    rep["inputs"] = [
        {
            "name": i.name,
            "dims": [
                d.dim_param if d.HasField("dim_param") else d.dim_value
                for d in i.type.tensor_type.shape.dim
            ],
            "elem": i.type.tensor_type.elem_type,
        }
        for i in m.graph.input
    ]
    del m

    so = ort.SessionOptions()
    so.intra_op_num_threads = 8
    so.inter_op_num_threads = 1
    t0 = time.perf_counter()
    sess = ort.InferenceSession(str(MODEL), so, providers=["CPUExecutionProvider"])
    rep["session_init_sec"] = round(time.perf_counter() - t0, 2)

    cond = C.load_conditions(C.TMP / "cond_real.npz")
    z = np.load(C.TMP / "x0.npz")
    x0 = torch.from_numpy(z["x0"])
    S = x0.shape[1]

    def make(B, x, tval=0.999):
        return {
            "x_t": np.ascontiguousarray(torch.cat([x] * B, 0).numpy()),
            "t": np.full((B,), tval, dtype=np.float32),
            "text_state": np.ascontiguousarray(torch.cat([cond["text_state"]] * B, 0).numpy()),
            "text_mask": np.ascontiguousarray(torch.cat([cond["text_mask"]] * B, 0).numpy()),
            "speaker_state": np.ascontiguousarray(
                torch.cat([cond["speaker_state"]] * B, 0).numpy()
            ),
            "speaker_mask": np.ascontiguousarray(torch.cat([cond["speaker_mask"]] * B, 0).numpy()),
            "caption_state": np.ascontiguousarray(
                torch.cat([cond["caption_state"]] * B, 0).numpy()
            ),
            "caption_mask": np.ascontiguousarray(torch.cat([cond["caption_mask"]] * B, 0).numpy()),
        }

    # --- 保存済み torch 出力（B=1, B=4, S=94, t=0.999）との照合 ---
    for B, key in ((1, "v_ref_b1"), (4, "v_ref_b4")):
        o = sess.run(["v"], make(B, x0))[0]
        rep[f"ort_vs_torch_b{B}"] = C.diffs(z[key], o)

    # --- 動的軸の確認：別の S_lat / 別の t / B=3 ---
    g = torch.Generator(device="cpu").manual_seed(7)
    xB = torch.randn((1, 251, 32), generator=g, dtype=torch.float32)
    o_dyn = sess.run(["v"], make(3, xB, tval=0.31))[0]
    rep["dyn_shape_out"] = list(o_dyn.shape)

    # --- 所要（ORT 1 ステップ・中央値） ---
    for B in (1, 4):
        feeds = make(B, x0)
        for _ in range(3):
            sess.run(["v"], feeds)
        ts = []
        for _ in range(7):
            t1 = time.perf_counter()
            sess.run(["v"], feeds)
            ts.append(time.perf_counter() - t1)
        rep[f"ort_step_b{B}_sec"] = dict(
            median=round(float(np.median(ts)), 4), min=round(float(np.min(ts)), 4)
        )

    # --- torch 側を同一プロセスで再測（同条件比較） ---
    rt, _ = C.load_runtime()
    C.install_real_rope()
    rt.model._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    rt.model.speaker_encoder._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    C.bake_rope_caches(rt.model)
    step = C.DitStep(rt.model).eval()

    def targs(B, x, tval=0.999):
        return (
            torch.cat([x] * B, 0),
            torch.full((B,), tval, dtype=torch.float32),
            torch.cat([cond["text_state"]] * B, 0),
            torch.cat([cond["text_mask"]] * B, 0),
            torch.cat([cond["speaker_state"]] * B, 0),
            torch.cat([cond["speaker_mask"]] * B, 0),
            torch.cat([cond["caption_state"]] * B, 0),
            torch.cat([cond["caption_mask"]] * B, 0),
        )

    with torch.inference_mode():
        t_dyn = step(*targs(3, xB, 0.31)).numpy()
    rep["ort_vs_torch_dyn_b3_S251_t0.31"] = C.diffs(t_dyn, o_dyn)

    with torch.inference_mode():
        for B in (1, 4):
            a = targs(B, x0)
            for _ in range(2):
                step(*a)
            ts = []
            for _ in range(5):
                t1 = time.perf_counter()
                step(*a)
                ts.append(time.perf_counter() - t1)
            rep[f"torch_step_b{B}_sec"] = dict(
                median=round(float(np.median(ts)), 4), min=round(float(np.min(ts)), 4)
            )

    # --- 参考: torch 側 KV キャッシュ有りの 1 ステップ（上流既定の速さ） ---
    with torch.inference_mode():
        for B in (1, 4):
            kv = rt.model.build_context_kv_cache(
                text_state=torch.cat([cond["text_state"]] * B, 0),
                speaker_state=torch.cat([cond["speaker_state"]] * B, 0),
                caption_state=torch.cat([cond["caption_state"]] * B, 0),
            )
            kwargs = dict(
                x_t=torch.cat([x0] * B, 0),
                t=torch.full((B,), 0.999, dtype=torch.float32),
                text_state=torch.cat([cond["text_state"]] * B, 0),
                text_mask=torch.cat([cond["text_mask"]] * B, 0),
                speaker_state=torch.cat([cond["speaker_state"]] * B, 0),
                speaker_mask=torch.cat([cond["speaker_mask"]] * B, 0),
                caption_state=torch.cat([cond["caption_state"]] * B, 0),
                caption_mask=torch.cat([cond["caption_mask"]] * B, 0),
                context_kv_cache=kv,
            )
            for _ in range(2):
                rt.model.forward_with_encoded_conditions(**kwargs)
            ts = []
            for _ in range(5):
                t1 = time.perf_counter()
                rt.model.forward_with_encoded_conditions(**kwargs)
                ts.append(time.perf_counter() - t1)
            rep[f"torch_step_kvcache_b{B}_sec"] = dict(median=round(float(np.median(ts)), 4))

    C.jdump(rep, C.OUT / "13_ort_check.json")
    print(json.dumps(rep, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
