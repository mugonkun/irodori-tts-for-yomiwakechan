#!/usr/bin/env python3
"""lab 専用: DML EP が実際にどのアダプタを掴んでいるかを、
2048x2048 の MatMul のスループット（GFLOPS）と device_id 別の挙動で確かめる。

Win32_VideoController には仮想ディスプレイ(MS Idd Device)が 3 枚同居しているため、
「DML が 8060S を掴んだ」ことは provider options では確認できない（空 {} が返る）。
そこで実測スループットで代替する（CPU との比・ハードウェア理論値との桁比較）。
"""
from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort

MODEL = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab/out/matmul2048.onnx"
OUT = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab/out/dml_adapter.json"
N = 2048
FLOPS = 2.0 * N * N * N

rng = np.random.default_rng(0)
A = rng.standard_normal((N, N), dtype=np.float32)
B = rng.standard_normal((N, N), dtype=np.float32)

so = ort.SessionOptions()
so.log_severity_level = 3
res: dict = {"onnxruntime": ort.__version__, "N": N, "results": []}


def bench(providers, label, iters=20):
    try:
        s = ort.InferenceSession(MODEL, so, providers=providers)
    except Exception as exc:
        res["results"].append({"label": label, "error": repr(exc)})
        return None
    y = s.run(None, {"A": A, "B": B})[0]  # warm
    t0 = time.perf_counter()
    for _ in range(iters):
        y = s.run(None, {"A": A, "B": B})[0]
    dt = (time.perf_counter() - t0) / iters
    res["results"].append(
        {
            "label": label,
            "providers": s.get_providers(),
            "seconds_per_matmul": round(dt, 5),
            "gflops": round(FLOPS / dt / 1e9, 1),
            "checksum": float(y[0, 0]),
        }
    )
    return y


y_cpu = bench(["CPUExecutionProvider"], "cpu")
y_dml = bench(["DmlExecutionProvider"], "dml_default")
for did in (0, 1, 2, 3):
    bench([("DmlExecutionProvider", {"device_id": did})], f"dml_device_id_{did}", iters=5)

if y_cpu is not None and y_dml is not None:
    res["cpu_vs_dml_max_abs_diff"] = float(
        np.max(np.abs(y_cpu.astype(np.float64) - y_dml.astype(np.float64)))
    )
    res["cpu_vs_dml_rel"] = float(
        np.max(np.abs(y_cpu.astype(np.float64) - y_dml.astype(np.float64)))
        / max(1e-9, float(np.max(np.abs(y_cpu))))
    )

if len(sys.argv) > 1 and sys.argv[1] == "--loop":
    # GPU カウンタ観測用に一定時間まわし続ける
    s = ort.InferenceSession(MODEL, so, providers=["DmlExecutionProvider"])
    end = time.time() + float(sys.argv[2] if len(sys.argv) > 2 else 15)
    n = 0
    while time.time() < end:
        s.run(None, {"A": A, "B": B})
        n += 1
    res["loop_iters"] = n

Path(OUT).write_text(json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(res, ensure_ascii=False, indent=2))
