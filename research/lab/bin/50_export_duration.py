"""段3: duration 予測器（predict_duration_log_frames 相当）の export と照合。
build_duration_features の 14 次元はグラフ外＝入力として受ける。"""

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

OUT = C.ONNX_DIR / "duration.onnx"


class DurStep(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.m = model

    def forward(
        self,
        text_state,
        text_mask,
        speaker_state,
        speaker_mask,
        caption_state,
        caption_mask,
        duration_features,
        has_speaker,
        has_caption,
    ):
        return self.m.predict_duration_log_frames(
            text_state=text_state,
            text_mask=text_mask,
            speaker_state=speaker_state,
            speaker_mask=speaker_mask,
            caption_state=caption_state,
            caption_mask=caption_mask,
            duration_features=duration_features,
            has_speaker=has_speaker,
            has_caption=has_caption,
        )


def main():
    torch.set_num_threads(8)
    rep = {}
    rt, _ = C.load_runtime()
    C.install_real_rope()
    rt.model._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    rt.model.speaker_encoder._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    C.bake_rope_caches(rt.model)
    cond = C.build_conditions(rt)

    args = (
        cond["text_state"],
        cond["text_mask"],
        cond["speaker_state"],
        cond["speaker_mask"],
        cond["caption_state"],
        cond["caption_mask"],
        cond["duration_features"],
        cond["has_speaker"],
        torch.full((1,), True, dtype=torch.bool),
    )
    names = [
        "text_state",
        "text_mask",
        "speaker_state",
        "speaker_mask",
        "caption_state",
        "caption_mask",
        "duration_features",
        "has_speaker",
        "has_caption",
    ]
    mod = DurStep(rt.model).eval()
    with torch.inference_mode():
        ref_orig = mod(*args).clone()
    # _safe_attention_mask のデータ依存分岐を torch.where 版へ差し替え（upstream 無改変）
    C.install_export_safe_mask()
    with torch.inference_mode():
        ref = mod(*args).clone()
    rep["safemask_patch_diff"] = C.diffs(ref_orig.numpy(), ref.numpy())
    rep["torch_pred_log"] = float(ref.item())
    rep["torch_pred_frames"] = float(torch.expm1(ref).item())

    Bd = torch.export.Dim("B", min=1, max=8)
    Kd = torch.export.Dim("S_spk", min=2, max=1024)
    dyn = {
        "text_state": {0: Bd},
        "text_mask": {0: Bd},
        "speaker_state": {0: Bd, 1: Kd},
        "speaker_mask": {0: Bd, 1: Kd},
        "caption_state": {0: Bd},
        "caption_mask": {0: Bd},
        "duration_features": {0: Bd},
        "has_speaker": {0: Bd},
        "has_caption": {0: Bd},
    }
    t0 = time.perf_counter()
    try:
        with torch.no_grad():
            torch.onnx.export(
                mod,
                args,
                str(OUT),
                dynamo=True,
                dynamic_shapes=dyn,
                input_names=names,
                output_names=["log1p_frames"],
                external_data=True,
                optimize=True,
            )
        rep["export"] = "dynamo=True OK"
    except Exception:
        rep["export_error"] = traceback.format_exc()[-4000:]
        print(rep["export_error"], flush=True)
        C.jdump(rep, C.OUT / "13_duration.json")
        return
    rep["export_sec"] = round(time.perf_counter() - t0, 2)
    files = sorted(C.ONNX_DIR.glob("duration*"))
    rep["files"] = {f.name: f.stat().st_size for f in files}
    rep["total_bytes"] = sum(f.stat().st_size for f in files)

    m = onnx.load(str(OUT), load_external_data=False)
    rep["opset"] = [{"domain": o.domain, "version": o.version} for o in m.opset_import]
    h = {}
    for n in m.graph.node:
        h[n.op_type] = h.get(n.op_type, 0) + 1
    rep["op_histogram"] = dict(sorted(h.items(), key=lambda kv: -kv[1]))
    rep["n_nodes"] = len(m.graph.node)
    del m

    so = ort.SessionOptions()
    so.intra_op_num_threads = 8
    sess = ort.InferenceSession(str(OUT), so, providers=["CPUExecutionProvider"])
    feeds = {n: np.ascontiguousarray(a.numpy()) for n, a in zip(names, args)}
    o = sess.run(["log1p_frames"], feeds)[0]
    rep["ort_pred_log"] = float(o.reshape(-1)[0])
    rep["ort_pred_frames"] = float(np.expm1(o.reshape(-1)[0]))
    rep["diff"] = C.diffs(ref.numpy(), o)
    rep["frames_diff"] = abs(rep["ort_pred_frames"] - rep["torch_pred_frames"])
    rep["rounded_same"] = int(round(rep["ort_pred_frames"])) == int(
        round(rep["torch_pred_frames"])
    )

    # B=2 の動的軸確認
    feeds2 = {n: np.ascontiguousarray(np.concatenate([a.numpy()] * 2, 0)) for n, a in zip(names, args)}
    o2 = sess.run(["log1p_frames"], feeds2)[0]
    rep["dyn_b2_shape"] = list(o2.shape)
    rep["dyn_b2_maxdiff_vs_b1"] = float(np.abs(o2 - o.reshape(-1)[0]).max())

    ts = []
    for _ in range(3):
        sess.run(["log1p_frames"], feeds)
    for _ in range(7):
        t1 = time.perf_counter()
        sess.run(["log1p_frames"], feeds)
        ts.append(time.perf_counter() - t1)
    rep["ort_sec_median"] = round(float(np.median(ts)), 4)
    tts = []
    with torch.inference_mode():
        for _ in range(2):
            mod(*args)
        for _ in range(5):
            t1 = time.perf_counter()
            mod(*args)
            tts.append(time.perf_counter() - t1)
    rep["torch_sec_median"] = round(float(np.median(tts)), 4)

    C.jdump(rep, C.OUT / "13_duration.json")
    print(json.dumps(rep, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
