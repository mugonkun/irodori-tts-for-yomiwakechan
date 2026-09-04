"""23 便・段1: lab/onnx の 13 グラフを DirectML EP と CPU EP でベンチする。

kit-cuda/bench_onnx.py の入力生成（make_input）をそのまま使い、
DirectML 用の session option（enable_mem_pattern=False / ORT_SEQUENTIAL）を足した版。
1 コマンド 10 分の上限に収めるため --group で段に切る。

  python 23_bench.py --ep dml --group small  --json-out ../out/23_bench_dml_small.json
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
import onnxruntime as ort

LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
sys.path.insert(0, str(LAB / "kit-cuda"))
import bench_onnx as B  # noqa: E402

GROUPS = {
    "small": [
        "duration.onnx",
        "sc_core_fp32.onnx",
        "sc_istft_slim.onnx",
        "sc_stft.onnx",
        "silentcipher_watermark_fp32.onnx",
        "speaker_encoder.onnx",
    ],
    "dacvae": [
        "dacvae_decoder_fp32.onnx",
        "dacvae_decoder_fp16.onnx",
        "dacvae_encoder_fp32.onnx",
        "dacvae_encoder_fp16.onnx",
    ],
    "dit": ["dit_step.onnx", "dit_step_fp16.onnx"],
    "cond": ["encode_conditions_sdpa.onnx"],
}

EP_MAP = {
    "dml": ["DmlExecutionProvider", "CPUExecutionProvider"],
    "cpu": ["CPUExecutionProvider"],
}


def make_so(ep: str, default_so: bool = False) -> ort.SessionOptions:
    so = ort.SessionOptions()
    so.log_severity_level = 3
    if ep == "dml" and not default_so:
        # ORT docs: DirectML EP does not support memory pattern optimizations
        # or parallel execution. These options must be disabled.
        so.enable_mem_pattern = False
        so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    if ep == "cpu":
        so.intra_op_num_threads = 8
        so.inter_op_num_threads = 1
    return so


def profile_providers(path: Path, feeds, ep: str, out_json: Path) -> dict:
    so = make_so(ep)
    so.enable_profiling = True
    so.profile_file_prefix = str(out_json.with_suffix(""))
    sess = ort.InferenceSession(str(path), so, providers=EP_MAP[ep])
    sess.run(None, feeds)
    sess.run(None, feeds)
    pf = Path(sess.end_profiling())
    ev = json.loads(pf.read_text(encoding="utf-8"))
    counts = {}
    for e in ev:
        if e.get("cat") == "Node" and e.get("args", {}).get("provider"):
            p = e["args"]["provider"]
            counts[p] = counts.get(p, 0) + 1
    try:
        pf.unlink()
    except Exception:
        pass
    return counts


def bench_one(path: Path, ep: str, runs: int, do_profile: bool, default_so: bool) -> dict:
    rec = {"model": path.name}
    data = path.with_suffix(".onnx.data")
    rec["bytes"] = path.stat().st_size + (data.stat().st_size if data.is_file() else 0)
    spec_path = path.parent / (path.name + ".inputs.json")
    specs = json.loads(spec_path.read_text(encoding="utf-8")) if spec_path.is_file() else {}
    rec["inputs_json"] = spec_path.name if spec_path.is_file() else None

    # --- CPU EP 基準 ---
    t0 = time.perf_counter()
    ref_sess = ort.InferenceSession(str(path), make_so("cpu"), providers=["CPUExecutionProvider"])
    rec["cpu_session_init_s"] = round(time.perf_counter() - t0, 3)
    rng = np.random.default_rng(1234)
    feeds, shapes = {}, {}
    for inp in ref_sess.get_inputs():
        arr, shape = B.make_input(inp.name, inp.type, inp.shape, specs.get(inp.name), rng, 4, 128)
        feeds[inp.name] = arr
        shapes[inp.name] = {"shape": shape, "type": inp.type}
    rec["feeds"] = shapes
    t0 = time.perf_counter()
    ref_out = ref_sess.run(None, feeds)
    rec["cpu_first_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
    cpu_t = []
    for _ in range(runs):
        t0 = time.perf_counter()
        ref_sess.run(None, feeds)
        cpu_t.append((time.perf_counter() - t0) * 1000.0)
    rec["cpu_median_ms"] = round(statistics.median(cpu_t), 3)
    rec["cpu_min_ms"] = round(min(cpu_t), 3)
    if ep == "cpu":
        rec["providers_used"] = ref_sess.get_providers()
        rec["median_ms"] = rec["cpu_median_ms"]
        rec["first_run_ms"] = rec["cpu_first_ms"]
        rec["max_abs_diff_vs_cpu_ep"] = 0.0
        return rec
    del ref_sess

    # --- 対象 EP ---
    try:
        t0 = time.perf_counter()
        sess = ort.InferenceSession(str(path), make_so(ep, default_so), providers=EP_MAP[ep])
        rec["session_init_s"] = round(time.perf_counter() - t0, 3)
        rec["providers_used"] = sess.get_providers()
        if EP_MAP[ep][0] not in sess.get_providers():
            rec["ep_fallback"] = "requested %s, got %s" % (EP_MAP[ep][0], sess.get_providers())
    except Exception as exc:
        rec["session_error"] = "%s: %s" % (type(exc).__name__, exc)
        rec["traceback"] = traceback.format_exc()[-2500:]
        return rec
    try:
        t0 = time.perf_counter()
        out = sess.run(None, feeds)
        rec["first_run_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
    except Exception as exc:
        rec["run_error"] = "%s: %s" % (type(exc).__name__, exc)
        rec["traceback"] = traceback.format_exc()[-2500:]
        return rec
    diffs = []
    for a, b in zip(ref_out, out):
        a = np.asarray(a)
        b = np.asarray(b)
        if a.dtype == np.bool_:
            diffs.append(float(np.max(a != b)) if a.size else 0.0)
        else:
            diffs.append(float(np.max(np.abs(a.astype(np.float64) - b.astype(np.float64)))))
    rec["max_abs_diff_vs_cpu_ep"] = max(diffs) if diffs else None
    rec["out_absmax"] = max(
        float(np.max(np.abs(np.asarray(a, dtype=np.float64)))) for a in ref_out
    )
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
    rec["speedup_vs_cpu_ep"] = round(rec["cpu_median_ms"] / rec["median_ms"], 3)
    del sess
    if do_profile:
        try:
            rec["node_providers"] = profile_providers(
                path, feeds, ep, LAB / "out" / ("23_prof_" + path.stem)
            )
        except Exception as exc:
            rec["profile_error"] = "%s: %s" % (type(exc).__name__, exc)
    return rec


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ep", required=True, choices=["dml", "cpu"])
    ap.add_argument("--group", required=True, choices=sorted(GROUPS) + ["all"])
    ap.add_argument("--json-out", required=True)
    ap.add_argument("--runs", type=int, default=40)
    ap.add_argument("--profile", action="store_true")
    ap.add_argument("--default-so", action="store_true", help="DML に既定 SessionOptions を渡す")
    args = ap.parse_args()

    names = (
        [n for g in ("small", "dacvae", "dit", "cond") for n in GROUPS[g]]
        if args.group == "all"
        else GROUPS[args.group]
    )
    res = {
        "ep": args.ep,
        "group": args.group,
        "runs": args.runs,
        "default_session_options": bool(args.default_so),
        "onnxruntime_version": ort.__version__,
        "available_providers": ort.get_available_providers(),
        "models": [],
    }
    for n in names:
        p = LAB / "onnx" / n
        print("[23] %s <- %s" % (args.ep, n), flush=True)
        try:
            rec = bench_one(p, args.ep, args.runs, args.profile, args.default_so)
        except Exception as exc:
            rec = {"model": n, "fatal": "%s: %s" % (type(exc).__name__, exc),
                   "traceback": traceback.format_exc()[-2500:]}
        res["models"].append(rec)
        print("[23]   " + json.dumps(
            {k: rec.get(k) for k in ("median_ms", "cpu_median_ms", "first_run_ms",
                                     "max_abs_diff_vs_cpu_ep", "session_error", "run_error",
                                     "ep_fallback", "node_providers")},
            ensure_ascii=False), flush=True)
        Path(args.json_out).write_text(
            json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")
    print("[23] wrote %s" % args.json_out)


if __name__ == "__main__":
    main()
