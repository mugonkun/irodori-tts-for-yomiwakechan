#!/usr/bin/env python3
"""lab 専用: DirectML EP の素振り（第 3 便の土台）。

- onnxruntime.get_available_providers() に DmlExecutionProvider があるか
- lab/out/smoke_sdpa.onnx を DML EP と CPU EP で走らせて出力の最大絶対差
- DML が掴んだアダプタ（session の provider options ＋ 実測 device_id）
"""
from __future__ import annotations

import json
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort

MODEL = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab/out/smoke_sdpa.onnx"
OUT = r"C:/Users/mugonkun/source/repos/irodori-native-research/lab/out/dml_check.json"

info: dict = {
    "onnxruntime_version": ort.__version__,
    "available_providers": ort.get_available_providers(),
    "device": ort.get_device(),
    "has_dml": "DmlExecutionProvider" in ort.get_available_providers(),
}

so = ort.SessionOptions()
so.log_severity_level = 3

sess_cpu = ort.InferenceSession(MODEL, so, providers=["CPUExecutionProvider"])
info["model_inputs"] = [
    {"name": i.name, "type": i.type, "shape": i.shape} for i in sess_cpu.get_inputs()
]
info["model_outputs"] = [
    {"name": o.name, "type": o.type, "shape": o.shape} for o in sess_cpu.get_outputs()
]

iname = sess_cpu.get_inputs()[0].name
static = [d for d in sess_cpu.get_inputs()[0].shape if isinstance(d, int)]
info["static_dims"] = sess_cpu.get_inputs()[0].shape

rng = np.random.default_rng(1234)


def make(shape):
    return rng.standard_normal(size=shape, dtype=np.float32)


# 動的軸（note 11 §8 は {2: Dim("T")}）を含む形を 2 通り試す
shapes = []
base = [d if isinstance(d, int) else 8 for d in sess_cpu.get_inputs()[0].shape]
shapes.append(tuple(base))
alt = list(base)
for i, d in enumerate(sess_cpu.get_inputs()[0].shape):
    if not isinstance(d, int):
        alt[i] = 23
shapes.append(tuple(alt))

sess_dml = None
if info["has_dml"]:
    t0 = time.perf_counter()
    sess_dml = ort.InferenceSession(MODEL, so, providers=["DmlExecutionProvider"])
    info["dml_session_init_s"] = round(time.perf_counter() - t0, 3)
    info["dml_session_providers"] = sess_dml.get_providers()
    try:
        info["dml_provider_options"] = sess_dml.get_provider_options()
    except Exception as exc:
        info["dml_provider_options"] = repr(exc)

info["runs"] = []
for shp in shapes:
    x = make(shp)
    t0 = time.perf_counter()
    y_cpu = sess_cpu.run(None, {iname: x})[0]
    t_cpu = time.perf_counter() - t0
    row = {"shape": list(shp), "cpu_s": round(t_cpu, 4), "out_shape": list(y_cpu.shape)}
    if sess_dml is not None:
        t0 = time.perf_counter()
        y_dml = sess_dml.run(None, {iname: x})[0]
        t_dml = time.perf_counter() - t0
        # 2 回目（ウォーム）
        t0 = time.perf_counter()
        y_dml2 = sess_dml.run(None, {iname: x})[0]
        t_dml2 = time.perf_counter() - t0
        row.update(
            {
                "dml_s_first": round(t_dml, 4),
                "dml_s_warm": round(t_dml2, 4),
                "max_abs_diff": float(np.max(np.abs(y_cpu.astype(np.float64) - y_dml.astype(np.float64)))),
                "dml_selfconsistent": bool(np.array_equal(y_dml, y_dml2)),
            }
        )
    info["runs"].append(row)

Path(OUT).write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(info, ensure_ascii=False, indent=2))
