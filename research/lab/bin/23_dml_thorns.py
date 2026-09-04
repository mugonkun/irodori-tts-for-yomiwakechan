"""23 便: 17-verify §(g) が挙げた DirectML の 3 つの棘を実測で確かめる。
  棘1 memory pattern / parallel execution を無効にしないとどうなるか
  棘2 同一 session への多重 Run（別スレッド）
  棘3 opset 20 の上限（グラフの opset を読む）
"""
import json, statistics, threading, time
from pathlib import Path
import numpy as np, onnxruntime as ort
LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
P = ["DmlExecutionProvider", "CPUExecutionProvider"]
res = {"onnxruntime": ort.__version__}

z = np.load(LAB / "tmp" / "cond_real.npz")
dur_feeds = {"text_state": z["text_state"], "text_mask": z["text_mask"],
             "speaker_state": z["speaker_state"], "speaker_mask": z["speaker_mask"],
             "caption_state": z["caption_state"], "caption_mask": z["caption_mask"],
             "duration_features": z["duration_features"], "has_speaker": z["has_speaker"],
             "has_caption": np.ones((1,), bool)}
M = str(LAB / "onnx" / "duration.onnx")

# --- 棘1 ---
for label, cfg in (("default", None), ("mem_pattern_on", "mp"), ("parallel", "par"),
                   ("docs_compliant", "ok")):
    so = ort.SessionOptions(); so.log_severity_level = 3
    if cfg == "mp":
        so.enable_mem_pattern = True
        so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    elif cfg == "par":
        so.enable_mem_pattern = False
        so.execution_mode = ort.ExecutionMode.ORT_PARALLEL
    elif cfg == "ok":
        so.enable_mem_pattern = False
        so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    try:
        s = ort.InferenceSession(M, so, providers=P)
        o = s.run(None, dur_feeds)[0]
        ts = []
        for _ in range(20):
            t = time.perf_counter(); s.run(None, dur_feeds); ts.append((time.perf_counter() - t) * 1000)
        res["thorn1_" + label] = {"providers": s.get_providers(), "ok": True,
                                  "median_ms": round(statistics.median(ts), 3),
                                  "value": float(o.ravel()[0])}
    except Exception as e:
        res["thorn1_" + label] = {"ok": False, "error": "%s: %s" % (type(e).__name__, str(e)[:200])}
    print(label, res["thorn1_" + label])

# --- 棘2: 同一 session に 2 スレッドから Run / 別 session 2 本から Run ---
def mk():
    so = ort.SessionOptions(); so.log_severity_level = 3
    so.enable_mem_pattern = False; so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    return ort.InferenceSession(M, so, providers=P)

def hammer(sess, n, out, idx):
    try:
        t = time.perf_counter()
        for _ in range(n):
            sess.run(None, dur_feeds)
        out[idx] = {"ok": True, "sec": round(time.perf_counter() - t, 3)}
    except Exception as e:
        out[idx] = {"ok": False, "error": "%s: %s" % (type(e).__name__, str(e)[:200])}

s1 = mk()
t0 = time.perf_counter()
for _ in range(60):
    s1.run(None, dur_feeds)
res["thorn2_serial_60"] = {"sec": round(time.perf_counter() - t0, 3)}
out = {}
th = [threading.Thread(target=hammer, args=(s1, 30, out, i)) for i in range(2)]
t0 = time.perf_counter()
for t in th: t.start()
for t in th: t.join()
res["thorn2_same_session_2threads"] = {"wall_sec": round(time.perf_counter() - t0, 3), "threads": out}
s2 = mk()
out2 = {}
th = [threading.Thread(target=hammer, args=(s, 30, out2, i)) for i, s in enumerate((s1, s2))]
t0 = time.perf_counter()
for t in th: t.start()
for t in th: t.join()
res["thorn2_two_sessions_2threads"] = {"wall_sec": round(time.perf_counter() - t0, 3), "threads": out2}

# --- 棘3: opset ---
import onnx
ops = {}
for f in sorted((LAB / "onnx").glob("*.onnx")):
    try:
        m = onnx.load(str(f), load_external_data=False)
        ops[f.name] = {o.domain or "": o.version for o in m.opset_import}
    except Exception as e:
        ops[f.name] = "err:%s" % type(e).__name__
res["thorn3_opsets"] = ops
(LAB / "out" / "23_dml_thorns.json").write_text(json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in res.items() if k.startswith("thorn2")}, ensure_ascii=False, indent=1))
