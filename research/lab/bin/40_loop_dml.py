"""23 便・段2: lab/bin/40_loop.py の ORT ループを .venv-dml で動く純 numpy + ORT の写しにした版。

torch は使わない（条件は lab/tmp/cond_real.npz、初期ノイズは lab/tmp/x0.npz から読む）。
最終潜在は lab/out/23_latent_<tag>.npz に落とし、復号と照合は lab/.venv 側（torch）で行う。

  python 40_loop_dml.py --ep dml --model dit_step.onnx --steps 40 --tag dml_fp32_s40
"""

from __future__ import annotations

import argparse
import json
import statistics
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort

LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
CFG_TEXT = 3.0
CFG_CAPTION = 3.0
CFG_MIN_T, CFG_MAX_T = 0.5, 1.0

EP_MAP = {
    "dml": ["DmlExecutionProvider", "CPUExecutionProvider"],
    "cpu": ["CPUExecutionProvider"],
}


def t_schedule(num_steps: int) -> np.ndarray:
    u = np.linspace(0.0, 1.0, num_steps + 1, dtype=np.float64)
    return ((1.0 - u) * 0.999).astype(np.float32)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ep", default="dml", choices=sorted(EP_MAP))
    ap.add_argument("--model", default="dit_step.onnx")
    ap.add_argument("--steps", type=int, default=40)
    ap.add_argument("--tag", required=True)
    ap.add_argument("--reps", type=int, default=2, help="ループ全体の反復（1 本目=コールド）")
    ap.add_argument("--sessions", type=int, default=1, choices=(1, 2),
                    help="2 なら B=3 用と B=1 用に session を分ける（DML の形状切替の罰を避ける）")
    args = ap.parse_args()

    z = np.load(LAB / "tmp" / "cond_real.npz")
    x0 = np.load(LAB / "tmp" / "x0.npz")["x0"]
    ts, tm = z["text_state"], z["text_mask"]
    ss, sm = z["speaker_state"], z["speaker_mask"]
    cs, cm = z["caption_state"], z["caption_mask"]
    S = int(x0.shape[1])

    # CFG バンドル（ループ外 1 回・rf.py:325-356 の写経）
    ts_u = np.zeros_like(ts); tm_u = np.zeros_like(tm)
    cs_u = np.zeros_like(cs); cm_u = np.zeros_like(cm)
    TS = np.concatenate([ts, ts_u, ts], 0)
    TM = np.concatenate([tm, tm_u, tm], 0)
    SS = np.concatenate([ss, ss, ss], 0)
    SM = np.concatenate([sm, sm, sm], 0)
    CS = np.concatenate([cs, cs, cs_u], 0)
    CM = np.concatenate([cm, cm, cm_u], 0)

    so = ort.SessionOptions()
    so.log_severity_level = 3
    if args.ep == "dml":
        so.enable_mem_pattern = False
        so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    else:
        so.intra_op_num_threads = 8
        so.inter_op_num_threads = 1
    t0 = time.perf_counter()
    sess = ort.InferenceSession(str(LAB / "onnx" / args.model), so, providers=EP_MAP[args.ep])
    sess1 = sess
    if args.sessions == 2:
        sess1 = ort.InferenceSession(str(LAB / "onnx" / args.model), so, providers=EP_MAP[args.ep])
    init_s = time.perf_counter() - t0

    rep = {
        "tag": args.tag, "ep": args.ep, "model": args.model, "steps": args.steps,
        "latent_steps": S, "onnxruntime": ort.__version__,
        "providers_used": sess.get_providers(),
        "session_init_s": round(init_s, 3), "sessions": args.sessions,
        "loops": [],
    }
    sched = t_schedule(args.steps)
    z_final = None
    for rep_i in range(args.reps):
        x = x0.copy()
        step_ms, cfg_ms, plain_ms = [], [], []
        n_cfg = 0
        t_loop = time.perf_counter()
        for i in range(args.steps):
            t = float(sched[i]); t_next = float(sched[i + 1])
            t_step = time.perf_counter()
            if CFG_MIN_T <= t <= CFG_MAX_T:
                n_cfg += 1
                feeds = {
                    "x_t": np.ascontiguousarray(np.concatenate([x] * 3, 0)),
                    "t": np.full((3,), t, dtype=np.float32),
                    "text_state": TS, "text_mask": TM,
                    "speaker_state": SS, "speaker_mask": SM,
                    "caption_state": CS, "caption_mask": CM,
                }
                v_out = sess.run(["v"], feeds)[0]
                c0, c1, c2 = v_out[0:1], v_out[1:2], v_out[2:3]
                v = c0 + CFG_TEXT * (c0 - c1) + CFG_CAPTION * (c0 - c2)
                cfg_ms.append((time.perf_counter() - t_step) * 1000.0)
            else:
                feeds = {
                    "x_t": np.ascontiguousarray(x),
                    "t": np.full((1,), t, dtype=np.float32),
                    "text_state": ts, "text_mask": tm,
                    "speaker_state": ss, "speaker_mask": sm,
                    "caption_state": cs, "caption_mask": cm,
                }
                v = sess1.run(["v"], feeds)[0]
                plain_ms.append((time.perf_counter() - t_step) * 1000.0)
            step_ms.append((time.perf_counter() - t_step) * 1000.0)
            x = x + v * (t_next - t)
        loop_s = time.perf_counter() - t_loop
        rep["loops"].append({
            "rep": rep_i,
            "loop_sec": round(loop_s, 3),
            "n_cfg_steps": n_cfg,
            "first_step_ms": round(step_ms[0], 2),
            "step_median_ms": round(statistics.median(step_ms), 3),
            "cfg_step_median_ms": round(statistics.median(cfg_ms), 3) if cfg_ms else None,
            "cfg_first_ms": round(cfg_ms[0], 2) if cfg_ms else None,
            "plain_step_median_ms": round(statistics.median(plain_ms), 3) if plain_ms else None,
            "plain_first_ms": round(plain_ms[0], 2) if plain_ms else None,
            "latent_absmax": float(np.abs(x).max()),
        })
        z_final = x
        print(json.dumps(rep["loops"][-1], ensure_ascii=False), flush=True)

    np.savez(LAB / "out" / ("23_latent_" + args.tag + ".npz"), latent=z_final)
    (LAB / "out" / ("23_loop_" + args.tag + ".json")).write_text(
        json.dumps(rep, ensure_ascii=False, indent=2), encoding="utf-8")
    print("[23] wrote 23_loop_%s.json / 23_latent_%s.npz" % (args.tag, args.tag))


if __name__ == "__main__":
    main()
