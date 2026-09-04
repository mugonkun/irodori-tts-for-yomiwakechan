"""段1d: S_spk（参照 speaker 文脈長）の動的軸が本当に効くかを、長い speaker 文脈で確認する。"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402


def main():
    torch.set_num_threads(8)
    rep = {}
    cd = np.load(C.TMP / "cond_real.npz")
    cond = {k: torch.from_numpy(cd[k]) for k in
            ("text_state", "text_mask", "speaker_state", "speaker_mask",
             "caption_state", "caption_mask")}
    x0 = torch.from_numpy(np.load(C.TMP / "x0.npz")["x0"])

    rt, _ = C.load_runtime()
    C.install_real_rope()
    C.install_export_safe_mask()
    rt.model._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    rt.model.speaker_encoder._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    C.bake_rope_caches(rt.model)
    step = C.DitStep(rt.model).eval()

    so = ort.SessionOptions()
    so.intra_op_num_threads = 8
    so.inter_op_num_threads = 1
    sess = ort.InferenceSession(str(C.ONNX_DIR / "dit_step.onnx"), so,
                                providers=["CPUExecutionProvider"])

    g = torch.Generator().manual_seed(5)
    for S_spk in (2, 64, 751):
        ss = torch.randn((1, S_spk, 768), generator=g) * 0.5
        sm = torch.ones((1, S_spk), dtype=torch.bool)
        sm[:, S_spk // 2:] = False  # 半分マスクした現実的な形
        args = (x0, torch.full((1,), 0.7), cond["text_state"], cond["text_mask"],
                ss, sm, cond["caption_state"], cond["caption_mask"])
        with torch.inference_mode():
            t1 = time.perf_counter()
            vt = step(*args).numpy()
            tsec = time.perf_counter() - t1
        feeds = {n: np.ascontiguousarray(a.numpy()) for n, a in zip(
            ["x_t", "t", "text_state", "text_mask", "speaker_state", "speaker_mask",
             "caption_state", "caption_mask"], args)}
        t1 = time.perf_counter()
        vo = sess.run(["v"], feeds)[0]
        osec = time.perf_counter() - t1
        rep[f"S_spk={S_spk}"] = dict(
            diff=C.diffs(vt, vo), torch_sec=round(tsec, 4), ort_sec=round(osec, 4))
        print(f"S_spk={S_spk} ok", flush=True)

    C.jdump(rep, C.OUT / "13_dyn_spk.json")
    print(json.dumps(rep, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
