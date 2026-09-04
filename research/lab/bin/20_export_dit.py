"""段1b: DiT 1 ステップ（use_context_kv_cache=False 相当＝state 渡し）を ONNX へ export。"""

from __future__ import annotations

import json
import sys
import time
import traceback
from pathlib import Path

import numpy as np
import torch

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402


def main():
    torch.set_num_threads(8)
    rep = {}
    rt, load_s = C.load_runtime()
    C.install_real_rope()
    rt.model._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    rt.model.speaker_encoder._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    C.bake_rope_caches(rt.model, max_lat=1024, max_spk=4096)
    cond = C.load_conditions(C.TMP / "cond_real.npz")
    S = int(cond["text_state"].shape[0] and np.load(C.TMP / "x0.npz")["x0"].shape[1])

    x0 = torch.from_numpy(np.load(C.TMP / "x0.npz")["x0"])
    # export 用サンプル入力＝CFG バッチ 4・S_lat=S
    B = 4
    args = (
        torch.cat([x0] * B, 0),
        torch.full((B,), 0.999, dtype=torch.float32),
        torch.cat([cond["text_state"]] * B, 0),
        torch.cat([cond["text_mask"]] * B, 0),
        torch.cat([cond["speaker_state"]] * B, 0),
        torch.cat([cond["speaker_mask"]] * B, 0),
        torch.cat([cond["caption_state"]] * B, 0),
        torch.cat([cond["caption_mask"]] * B, 0),
    )
    rep["sample_shapes"] = [list(a.shape) for a in args]
    rep["sample_dtypes"] = [str(a.dtype) for a in args]

    step = C.DitStep(rt.model).eval()

    Bd = torch.export.Dim("B", min=1, max=8)
    Sd = torch.export.Dim("S_lat", min=2, max=768)
    Kd = torch.export.Dim("S_spk", min=2, max=1024)
    dyn = {
        "x_t": {0: Bd, 1: Sd},
        "t": {0: Bd},
        "text_state": {0: Bd},
        "text_mask": {0: Bd},
        "speaker_state": {0: Bd, 1: Kd},
        "speaker_mask": {0: Bd, 1: Kd},
        "caption_state": {0: Bd},
        "caption_mask": {0: Bd},
    }
    out = C.ONNX_DIR / "dit_step.onnx"
    t0 = time.perf_counter()
    try:
        with torch.no_grad():
            torch.onnx.export(
                step,
                args,
                str(out),
                dynamo=True,
                dynamic_shapes=dyn,
                input_names=[
                    "x_t",
                    "t",
                    "text_state",
                    "text_mask",
                    "speaker_state",
                    "speaker_mask",
                    "caption_state",
                    "caption_mask",
                ],
                output_names=["v"],
                external_data=True,
                optimize=True,
            )
        rep["export"] = "dynamo=True OK"
    except Exception:
        rep["export_dynamo_error"] = traceback.format_exc()[-4000:]
        print(rep["export_dynamo_error"], flush=True)
        raise
    rep["export_sec"] = round(time.perf_counter() - t0, 2)
    files = sorted(C.ONNX_DIR.glob("dit_step*"))
    rep["files"] = {f.name: f.stat().st_size for f in files}
    rep["total_bytes"] = sum(f.stat().st_size for f in files)
    C.jdump(rep, C.OUT / "13_export_dit.json")
    print(json.dumps({k: v for k, v in rep.items() if k != "export_dynamo_error"},
                     ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
