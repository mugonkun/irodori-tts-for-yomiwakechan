"""段5c: fp16 op_block_list 版の「同一 Cast ノードの二重挿入」を取り除いて評価する。
（onnxruntime.transformers.float16 の不具合回避。upstream/ORT は無改変・後処理のみ）"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402

SRC = C.ONNX_DIR / "dit_step.onnx"
BLOCK_SRC = C.ONNX_DIR / "dit_step_fp16_block.onnx"
FIX = C.ONNX_DIR / "dit_step_fp16_block_fix.onnx"


def main():
    rep = {}
    m = onnx.load(str(BLOCK_SRC))
    seen = set()
    keep, dropped = [], 0
    for n in m.graph.node:
        key = (n.op_type, tuple(n.input), tuple(n.output), n.name)
        if key in seen:
            dropped += 1
            continue
        seen.add(key)
        keep.append(n)
    rep["dropped_duplicate_nodes"] = dropped
    del m.graph.node[:]
    m.graph.node.extend(keep)
    d = FIX.with_suffix(".onnx.data")
    for p in (FIX, d):
        if p.exists():
            p.unlink()
    onnx.save(m, str(FIX), save_as_external_data=True, all_tensors_to_one_file=True,
              location=d.name)
    del m
    rep["files"] = {f.name: f.stat().st_size for f in sorted(C.ONNX_DIR.glob(FIX.stem + "*"))}

    cd = np.load(C.TMP / "cond_real.npz")
    cond = {k: cd[k] for k in ("text_state", "text_mask", "speaker_state", "speaker_mask",
                               "caption_state", "caption_mask")}
    x0 = np.load(C.TMP / "x0.npz")["x0"]
    feeds = {
        "x_t": np.ascontiguousarray(x0), "t": np.full((1,), 0.999, dtype=np.float32),
        "text_state": cond["text_state"], "text_mask": cond["text_mask"],
        "speaker_state": cond["speaker_state"], "speaker_mask": cond["speaker_mask"],
        "caption_state": cond["caption_state"], "caption_mask": cond["caption_mask"]}
    so = ort.SessionOptions()
    so.intra_op_num_threads = 8
    so.inter_op_num_threads = 1
    s32 = ort.InferenceSession(str(SRC), so, providers=["CPUExecutionProvider"])
    ref = s32.run(["v"], feeds)[0]
    del s32
    sb = ort.InferenceSession(str(FIX), so, providers=["CPUExecutionProvider"])
    ob = sb.run(["v"], feeds)[0]
    rep["diff_vs_fp32_b1"] = C.diffs(ref, ob)
    for _ in range(2):
        sb.run(["v"], feeds)
    ts = []
    for _ in range(3):
        t1 = time.perf_counter()
        sb.run(["v"], feeds)
        ts.append(time.perf_counter() - t1)
    rep["sec_b1_median"] = round(float(np.median(ts)), 4)
    C.jdump(rep, C.OUT / "13_fp16_block.json")
    print(json.dumps(rep, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
