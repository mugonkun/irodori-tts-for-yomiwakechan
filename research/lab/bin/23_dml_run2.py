"""23 便 棘2: DirectML の同一 session への多重 Run（別スレッド）を分離プロセスで試す。
  引数 same | two | serial
"""
import json, sys, threading, time
from pathlib import Path
import numpy as np, onnxruntime as ort
LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
mode = sys.argv[1]
z = np.load(LAB / "tmp" / "cond_real.npz")
feeds = {"text_state": z["text_state"], "text_mask": z["text_mask"],
         "speaker_state": z["speaker_state"], "speaker_mask": z["speaker_mask"],
         "caption_state": z["caption_state"], "caption_mask": z["caption_mask"],
         "duration_features": z["duration_features"], "has_speaker": z["has_speaker"],
         "has_caption": np.ones((1,), bool)}
M = str(LAB / "onnx" / "duration.onnx")
def mk():
    so = ort.SessionOptions(); so.log_severity_level = 3
    so.enable_mem_pattern = False; so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    return ort.InferenceSession(M, so, providers=["DmlExecutionProvider", "CPUExecutionProvider"])
def hammer(sess, n, out, i):
    try:
        t = time.perf_counter()
        for _ in range(n):
            sess.run(None, feeds)
        out[i] = {"ok": True, "sec": round(time.perf_counter() - t, 3)}
    except Exception as e:
        out[i] = {"ok": False, "error": "%s" % type(e).__name__}
print("mode=%s start" % mode, flush=True)
if mode == "serial":
    s = mk()
    s.run(None, feeds)
    t = time.perf_counter()
    for _ in range(60): s.run(None, feeds)
    print(json.dumps({"serial60_sec": round(time.perf_counter() - t, 3)}), flush=True)
elif mode == "same":
    s = mk(); s.run(None, feeds)
    out = {}
    th = [threading.Thread(target=hammer, args=(s, 30, out, i)) for i in range(2)]
    t = time.perf_counter()
    for x in th: x.start()
    for x in th: x.join()
    print(json.dumps({"same_session_2threads_wall": round(time.perf_counter() - t, 3), "threads": out}), flush=True)
elif mode == "two":
    a, b = mk(), mk(); a.run(None, feeds); b.run(None, feeds)
    out = {}
    th = [threading.Thread(target=hammer, args=(s, 30, out, i)) for i, s in enumerate((a, b))]
    t = time.perf_counter()
    for x in th: x.start()
    for x in th: x.join()
    print(json.dumps({"two_sessions_2threads_wall": round(time.perf_counter() - t, 3), "threads": out}), flush=True)
print("mode=%s end" % mode, flush=True)
