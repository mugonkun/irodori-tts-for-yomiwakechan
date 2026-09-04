#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""汎用 ONNX ベンチ（CUDA EP / DirectML EP / CPU EP）。

KitRoot\\onnx\\*.onnx を総当たりで
  1. CPU EP で 1 回まわして基準出力を取る
  2. 対象 EP で 40 回まわし、中央値 ms と CPU EP との max abs diff を出す
入力形状は `<model>.inputs.json`（あれば）を優先、無ければ動的軸を batch=4・その他 128 で埋める。

使い方:
  python bench_onnx.py --onnx-dir <dir> --ep cuda|dml|cpu --json-out <path> [--runs 40]

`<model>.inputs.json` の形（任意・全欄任意）:
  { "input_name": {"shape": [1, 32, 256], "dtype": "float32", "fill": "randn"|"zeros"|"ones"} }
"""
from __future__ import annotations

import argparse
import json
import statistics
import sys
import time
import traceback
from pathlib import Path

import numpy as np

EP_MAP = {
    "cuda": ["CUDAExecutionProvider", "CPUExecutionProvider"],
    "dml": ["DmlExecutionProvider", "CPUExecutionProvider"],
    "cpu": ["CPUExecutionProvider"],
}

DTYPE_MAP = {
    "tensor(float)": np.float32,
    "tensor(float16)": np.float16,
    "tensor(double)": np.float64,
    "tensor(int64)": np.int64,
    "tensor(int32)": np.int32,
    "tensor(bool)": np.bool_,
}


def make_input(name, ort_type, ort_shape, spec, rng, batch, other):
    dtype = DTYPE_MAP.get(ort_type, np.float32)
    shape = []
    for i, d in enumerate(ort_shape):
        if isinstance(d, int) and d > 0:
            shape.append(d)
        else:
            shape.append(batch if i == 0 else other)
    fill = None
    if spec:
        if "shape" in spec:
            shape = list(spec["shape"])
        if "dtype" in spec:
            dtype = np.dtype(spec["dtype"]).type
        fill = spec.get("fill")
    if fill == "zeros":
        return np.zeros(shape, dtype=dtype), shape
    if fill == "ones":
        return np.ones(shape, dtype=dtype), shape
    if dtype in (np.float32, np.float16, np.float64):
        return rng.standard_normal(shape).astype(dtype), shape
    if dtype is np.bool_:
        return np.ones(shape, dtype=np.bool_), shape
    return np.zeros(shape, dtype=dtype), shape


def bench_one(ort, path, ep, runs, batch, other):
    rec = {"model": path.name, "bytes": path.stat().st_size}
    spec_path = path.with_suffix(path.suffix + ".inputs.json")
    if not spec_path.is_file():
        spec_path = path.parent / (path.stem + ".inputs.json")
    specs = {}
    if spec_path.is_file():
        try:
            specs = json.loads(spec_path.read_text(encoding="utf-8"))
            rec["inputs_json"] = spec_path.name
        except Exception as exc:
            rec["inputs_json_error"] = repr(exc)
    try:
        so = ort.SessionOptions()
        so.log_severity_level = 3
        ref_sess = ort.InferenceSession(str(path), so, providers=["CPUExecutionProvider"])
    except Exception as exc:
        rec["cpu_session_error"] = "%s: %s" % (type(exc).__name__, exc)
        return rec
    rng = np.random.default_rng(1234)
    feeds = {}
    shapes = {}
    for inp in ref_sess.get_inputs():
        arr, shape = make_input(
            inp.name, inp.type, inp.shape, specs.get(inp.name), rng, batch, other
        )
        feeds[inp.name] = arr
        shapes[inp.name] = {"shape": shape, "type": inp.type}
    rec["feeds"] = shapes
    try:
        t0 = time.perf_counter()
        ref_out = ref_sess.run(None, feeds)
        rec["cpu_first_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
    except Exception as exc:
        rec["cpu_run_error"] = "%s: %s" % (type(exc).__name__, exc)
        return rec
    if ep == "cpu":
        sess = ref_sess
        rec["providers_used"] = sess.get_providers()
    else:
        try:
            so2 = ort.SessionOptions()
            so2.log_severity_level = 3
            sess = ort.InferenceSession(str(path), so2, providers=EP_MAP[ep])
            rec["providers_used"] = sess.get_providers()
            if EP_MAP[ep][0] not in sess.get_providers():
                rec["ep_fallback"] = "requested %s but session reports %s" % (
                    EP_MAP[ep][0],
                    sess.get_providers(),
                )
                try:
                    # second attempt without CPU fallback so the real error surfaces in the record
                    so3 = ort.SessionOptions()
                    so3.log_severity_level = 1
                    ort.InferenceSession(str(path), so3, providers=[EP_MAP[ep][0]])
                except Exception as exc2:
                    rec["ep_error"] = ("%s: %s" % (type(exc2).__name__, exc2))[:1500]
        except Exception as exc:
            rec["session_error"] = "%s: %s" % (type(exc).__name__, exc)
            rec["traceback"] = traceback.format_exc()[-3000:]
            return rec
    try:
        t0 = time.perf_counter()
        out = sess.run(None, feeds)
        rec["first_run_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
    except Exception as exc:
        rec["run_error"] = "%s: %s" % (type(exc).__name__, exc)
        rec["traceback"] = traceback.format_exc()[-3000:]
        return rec
    diffs = []
    for a, b in zip(ref_out, out):
        try:
            diffs.append(float(np.max(np.abs(np.asarray(a, dtype=np.float64) - np.asarray(b, dtype=np.float64)))))
        except Exception:
            diffs.append(float("nan"))
    rec["max_abs_diff_vs_cpu_ep"] = max(diffs) if diffs else None
    rec["output_shapes"] = [list(np.asarray(o).shape) for o in out]
    times = []
    for _ in range(runs):
        t0 = time.perf_counter()
        sess.run(None, feeds)
        times.append((time.perf_counter() - t0) * 1000.0)
    rec["runs"] = runs
    rec["median_ms"] = round(statistics.median(times), 3)
    rec["min_ms"] = round(min(times), 3)
    rec["max_ms"] = round(max(times), 3)
    return rec


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--onnx-dir", required=True)
    ap.add_argument("--ep", required=True, choices=sorted(EP_MAP))
    ap.add_argument("--json-out", required=True)
    ap.add_argument("--runs", type=int, default=40)
    ap.add_argument("--batch", type=int, default=4)
    ap.add_argument("--other", type=int, default=128)
    args = ap.parse_args()

    result = {"ep": args.ep, "onnx_dir": args.onnx_dir, "runs": args.runs, "models": []}
    try:
        import onnxruntime as ort

        result["onnxruntime_version"] = ort.__version__
        result["available_providers"] = ort.get_available_providers()
        # ORT >= 1.21 on Windows does not find the pip-installed NVIDIA DLLs (nvidia-cudnn-cu13 etc.)
        # unless preload_dlls() is called; without it the CUDA EP silently falls back to CPU.
        if args.ep == "cuda" and hasattr(ort, "preload_dlls"):
            try:
                ort.preload_dlls()
                result["preload_dlls"] = "ok"
                try:
                    ort.print_debug_info()
                except Exception:
                    pass
            except Exception as exc:
                result["preload_dlls"] = "%s: %s" % (type(exc).__name__, exc)
    except Exception as exc:
        result["import_error"] = "%s: %s" % (type(exc).__name__, exc)
        Path(args.json_out).write_text(
            json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        print("[kit-onnx] onnxruntime import failed: %s" % exc)
        return 3

    files = sorted(Path(args.onnx_dir).glob("*.onnx"))
    result["model_count"] = len(files)
    for p in files:
        print("[kit-onnx] %s <- %s" % (args.ep, p.name), flush=True)
        try:
            rec = bench_one(ort, p, args.ep, args.runs, args.batch, args.other)
        except Exception as exc:
            rec = {
                "model": p.name,
                "fatal": "%s: %s" % (type(exc).__name__, exc),
                "traceback": traceback.format_exc()[-3000:],
            }
        result["models"].append(rec)
        print("[kit-onnx]   %s" % json.dumps(
            {k: rec.get(k) for k in ("median_ms", "max_abs_diff_vs_cpu_ep", "session_error", "run_error")},
            ensure_ascii=False,
        ), flush=True)

    Path(args.json_out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.json_out).write_text(
        json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print("[kit-onnx] wrote %s" % args.json_out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
