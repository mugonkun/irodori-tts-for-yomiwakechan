"""23 便: DirectML EP で B=1 と B=3 を同一 session で切り替えたときの費用を測る。"""
import json, statistics, sys, time
from pathlib import Path
import numpy as np, onnxruntime as ort
LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
EP = {"dml": ["DmlExecutionProvider", "CPUExecutionProvider"], "cpu": ["CPUExecutionProvider"]}
ep = sys.argv[1] if len(sys.argv) > 1 else "dml"
model = sys.argv[2] if len(sys.argv) > 2 else "dit_step.onnx"
z = np.load(LAB / "tmp" / "cond_real.npz"); x0 = np.load(LAB / "tmp" / "x0.npz")["x0"]
def feeds(B):
    r = lambda a: np.ascontiguousarray(np.concatenate([a] * B, 0))
    return {"x_t": r(x0), "t": np.full((B,), 0.7, np.float32),
            "text_state": r(z["text_state"]), "text_mask": r(z["text_mask"]),
            "speaker_state": r(z["speaker_state"]), "speaker_mask": r(z["speaker_mask"]),
            "caption_state": r(z["caption_state"]), "caption_mask": r(z["caption_mask"])}
def mk():
    so = ort.SessionOptions(); so.log_severity_level = 3
    if ep == "dml":
        so.enable_mem_pattern = False; so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    else:
        so.intra_op_num_threads = 8; so.inter_op_num_threads = 1
    return ort.InferenceSession(str(LAB / "onnx" / model), so, providers=EP[ep])
def run(s, B, n):
    f = feeds(B); ts = []
    for _ in range(n):
        t = time.perf_counter(); s.run(["v"], f); ts.append((time.perf_counter() - t) * 1000)
    return ts
out = {"ep": ep, "model": model, "ort": ort.__version__}
for B in (1, 3):
    s = mk(); t = run(s, B, 21)
    out["fresh_B%d" % B] = {"first_ms": round(t[0], 2), "median_ms": round(statistics.median(t[1:]), 2)}
    del s
s = mk()
t1 = run(s, 3, 21); t2 = run(s, 1, 21); t3 = run(s, 3, 11); t4 = run(s, 1, 11)
out["mixed"] = {
    "B3_first": round(t1[0], 2), "B3_med": round(statistics.median(t1[1:]), 2),
    "then_B1_first": round(t2[0], 2), "then_B1_med": round(statistics.median(t2[1:]), 2),
    "back_B3_first": round(t3[0], 2), "back_B3_med": round(statistics.median(t3[1:]), 2),
    "back_B1_first": round(t4[0], 2), "back_B1_med": round(statistics.median(t4[1:]), 2)}
p = LAB / "out" / ("23_shape_switch_%s_%s.json" % (ep, Path(model).stem))
p.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(out, ensure_ascii=False))
