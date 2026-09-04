#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""(b) ONNX 案の「端から端まで」を 1 プロセスで測る（純 numpy + onnxruntime・torch 不使用）。

23 便（DirectML・AMD 8060S）の `lab/bin/40_loop_dml.py` + `23_e2e.py` を 1 本にまとめ、
CUDA EP / DirectML EP / CPU EP のどれでも同じ物差しで測れるようにしたもの。

測る段（すべて短文 3.76 秒・latent_steps 94・13 §1 と同一条件）:
  (b) encode_conditions   条件符号化（実トークン列 = data/cond_real.npz）
  (c) duration            長さ予測
  (d) dit_loop            DiT の実ループ（t 線形・CFG independent text3.0/caption3.0/speaker0.0・
                          cfg_min_t 0.5・Euler）。**バッチ形状ごとに session を分ける**（23 §4-2）
  (e) decode              DACVAE 復号（最終潜在 -> 波形）
  (f) watermark           silentcipher 透かし（DML では 1.24.4 がロードを拒むので CPU EP へ退避）
  (g) totals              段の合計と RTF（cold / warm）
  (h) latent_check        最終潜在を data/ 同梱の参照（AMD DML fp32・torch と 5e-5 以内）と照合
  (i) dit_fp16            fp16 DiT を同じループで 1 回（破綻の再確認）

使い方:
  python e2e_onnx.py --ep cuda --onnx-dir ..\\onnx --data-dir .\\data --json-out ..\\results\\onnx_e2e_cuda.json
  [--steps 40,10] [--reps 2] [--device-id 0] [--no-cpu-ref] [--skip-fp16] [--label ...]

失敗した段は逐語を JSON に残して次へ進む（全体は落とさない）。
"""
from __future__ import annotations

import argparse
import gc
import json
import os
import platform
import statistics
import subprocess
import sys
import time
import traceback
from pathlib import Path

import numpy as np

AUDIO_SEC = 3.76          # 短文の音声長（latent_steps 94・11 §5 / 13 §1）
CFG_TEXT = 3.0
CFG_CAPTION = 3.0
CFG_SPEAKER = 0.0         # independent・0 なので合成式には現れない（記録のみ）
CFG_MIN_T, CFG_MAX_T = 0.5, 1.0

EP_NAME = {
    "cuda": "CUDAExecutionProvider",
    "dml": "DmlExecutionProvider",
    "cpu": "CPUExecutionProvider",
}

# EP ごとに使うグラフ（先頭から順に、存在する最初のものを採る）
GRAPHS = {
    "encode_conditions": {
        "dml": ["encode_conditions_sdpa_az0.onnx", "encode_conditions_sdpa.onnx"],
        "cuda": ["encode_conditions_sdpa.onnx", "encode_conditions_sdpa_az0.onnx"],
        "cpu": ["encode_conditions_sdpa.onnx", "encode_conditions_sdpa_az0.onnx"],
    },
    "duration": {"*": ["duration.onnx"]},
    "dit_fp32": {"*": ["dit_step.onnx"]},
    "dit_fp16": {"*": ["dit_step_fp16.onnx"]},
    "decode": {
        "dml": ["dacvae_decoder_fp32_dml.onnx", "dacvae_decoder_fp32_az0.onnx",
                "dacvae_decoder_fp32.onnx"],
        "cuda": ["dacvae_decoder_fp32.onnx", "dacvae_decoder_fp32_dml.onnx"],
        "cpu": ["dacvae_decoder_fp32.onnx", "dacvae_decoder_fp32_dml.onnx"],
    },
    "watermark": {"*": ["silentcipher_watermark_fp32.onnx"]},
}
WM_PARTS = ["sc_stft.onnx", "sc_enc_c_fp32.onnx", "sc_dec_c_fp32.onnx"]

DTYPE_MAP = {
    "tensor(float)": np.float32,
    "tensor(float16)": np.float16,
    "tensor(double)": np.float64,
    "tensor(int64)": np.int64,
    "tensor(int32)": np.int32,
    "tensor(bool)": np.bool_,
}


# ---------------------------------------------------------------- 小道具

def now_ms(t0):
    return (time.perf_counter() - t0) * 1000.0


def r3(x):
    return None if x is None else round(float(x), 3)


def pick_graph(onnx_dir: Path, stage: str, ep: str):
    cands = GRAPHS[stage].get(ep) or GRAPHS[stage]["*"]
    for name in cands:
        p = onnx_dir / name
        if p.is_file():
            return p
    return None


def sess_options(ort, ep, threads=8):
    so = ort.SessionOptions()
    so.log_severity_level = 3
    if ep == "dml":
        # 17 (g) / ORT docs 準拠。23 §7 では 1.24.4 で強制されていなかったが安全側で入れる
        so.enable_mem_pattern = False
        so.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    elif ep == "cpu":
        so.intra_op_num_threads = threads
        so.inter_op_num_threads = 1
    return so


def providers_for(ep, device_id):
    name = EP_NAME[ep]
    if ep == "cpu":
        return ["CPUExecutionProvider"]
    if device_id:
        return [(name, {"device_id": int(device_id)}), "CPUExecutionProvider"]
    return [name, "CPUExecutionProvider"]


def make_session(ort, path: Path, ep, device_id, threads=8):
    """session を作って (sess, info) を返す。失敗は例外を投げる。"""
    so = sess_options(ort, ep, threads)
    t0 = time.perf_counter()
    sess = ort.InferenceSession(str(path), so, providers=providers_for(ep, device_id))
    info = {
        "model": path.name,
        "model_bytes": path.stat().st_size,
        "session_init_ms": r3(now_ms(t0)),
        "providers_used": list(sess.get_providers()),
    }
    want = EP_NAME[ep]
    if want not in sess.get_providers():
        info["ep_fallback"] = "requested %s but session reports %s" % (want, sess.get_providers())
    return sess, info


def cast_feeds(sess, feeds):
    """session が求める dtype に合わせる（fp16 グラフの入力が fp32 のこともある）。"""
    want = {i.name: DTYPE_MAP.get(i.type, np.float32) for i in sess.get_inputs()}
    out = {}
    for k, v in feeds.items():
        if k not in want:
            continue
        a = np.asarray(v)
        if a.dtype != want[k]:
            a = a.astype(want[k])
        out[k] = np.ascontiguousarray(a)
    missing = [i.name for i in sess.get_inputs() if i.name not in out]
    return out, missing


def timed(sess, feeds, reps, out_names=None):
    """1 回目 = cold、残り reps 回の中央値 = warm。最後の出力を返す。"""
    ts = []
    outs = None
    for _ in range(max(1, reps) + 1):
        t0 = time.perf_counter()
        outs = sess.run(out_names, feeds)
        ts.append(now_ms(t0))
    return outs, {
        "cold_ms": r3(ts[0]),
        "warm_ms": r3(statistics.median(ts[1:])) if len(ts) > 1 else r3(ts[0]),
        "runs_ms": [r3(x) for x in ts],
    }


def diffs(a, b):
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    if a.shape != b.shape:
        return {"shape_mismatch": [list(a.shape), list(b.shape)]}
    d = a - b
    den = float(np.linalg.norm(a))
    return {
        "max_abs": float(np.max(np.abs(d))),
        "rel_l2": float(np.linalg.norm(d) / den) if den else None,
        "ref_absmax": float(np.max(np.abs(a))),
    }


def free(*objs):
    for o in objs:
        del o
    gc.collect()


def load_inputs_json(onnx_dir: Path, data_dir: Path, model_name: str):
    """<model>.inputs.json を onnx-dir -> data-dir/inputs の順に探す。

    手術版（`*_az0` / `*_dml`）には専用の inputs.json が無いので、接尾辞を落とした
    元グラフの inputs.json も候補にする（形は同じ）。"""
    stem = Path(model_name).stem
    stems = [stem]
    for suf in ("_az0", "_dml"):
        if stem.endswith(suf):
            stems.append(stem[: -len(suf)])
    for base in (onnx_dir, data_dir / "inputs", data_dir):
        for s in stems:
            for cand in (base / (s + ".onnx.inputs.json"), base / (s + ".inputs.json")):
                if cand.is_file():
                    try:
                        return json.loads(cand.read_text(encoding="utf-8")), str(cand)
                    except Exception as exc:
                        return {}, "%s (parse error: %r)" % (cand, exc)
    return {}, None


def feeds_from_spec(sess, spec, rng):
    """inputs.json の形で入力を合成する（透かしのように実データを持たない段で使う）。"""
    feeds = {}
    for inp in sess.get_inputs():
        dtype = DTYPE_MAP.get(inp.type, np.float32)
        shape = [d if isinstance(d, int) and d > 0 else 1 for d in inp.shape]
        fill = None
        s = spec.get(inp.name)
        if s:
            if "shape" in s:
                shape = list(s["shape"])
            if "dtype" in s:
                dtype = np.dtype(s["dtype"]).type
            fill = s.get("fill")
        if fill == "zeros":
            feeds[inp.name] = np.zeros(shape, dtype=dtype)
        elif fill == "ones":
            feeds[inp.name] = np.ones(shape, dtype=dtype)
        elif dtype in (np.float32, np.float16, np.float64):
            feeds[inp.name] = rng.standard_normal(shape).astype(dtype)
        elif dtype is np.bool_:
            feeds[inp.name] = np.ones(shape, dtype=np.bool_)
        else:
            feeds[inp.name] = np.zeros(shape, dtype=dtype)
    return feeds


# ---------------------------------------------------------------- CPU EP の照合

def cpu_reference(ort, path: Path, feeds, ep, want_outputs=None, threads=8):
    """同じグラフを CPU EP で 1 回だけ回して基準出力を採る（ep=cpu のときは呼ばない）。"""
    rec = {}
    try:
        so = sess_options(ort, "cpu", threads)
        t0 = time.perf_counter()
        s = ort.InferenceSession(str(path), so, providers=["CPUExecutionProvider"])
        rec["session_init_ms"] = r3(now_ms(t0))
        f, missing = cast_feeds(s, feeds)
        if missing:
            rec["missing_inputs"] = missing
        t0 = time.perf_counter()
        out = s.run(want_outputs, f)
        rec["run_ms"] = r3(now_ms(t0))
        free(s)
        return out, rec
    except Exception as exc:
        rec["error"] = "%s: %s" % (type(exc).__name__, str(exc)[:1200])
        return None, rec


# ---------------------------------------------------------------- (d)(i) DiT ループ

def t_schedule(num_steps: int) -> np.ndarray:
    u = np.linspace(0.0, 1.0, num_steps + 1, dtype=np.float64)
    return ((1.0 - u) * 0.999).astype(np.float32)


def dit_loop(ort, path: Path, ep, device_id, x0, cond, steps, reps, sessions_n, threads=8):
    """40_loop_dml.py の写し。B=3 用と B=1 用に session を分ける（sessions_n=2）。"""
    ts, tm = cond["text_state"], cond["text_mask"]
    ss, sm = cond["speaker_state"], cond["speaker_mask"]
    cs, cm = cond["caption_state"], cond["caption_mask"]

    # CFG バンドル（ループ外 1 回・upstream rf.py:325-356 の写経）
    ts_u = np.zeros_like(ts); tm_u = np.zeros_like(tm)
    cs_u = np.zeros_like(cs); cm_u = np.zeros_like(cm)
    TS = np.ascontiguousarray(np.concatenate([ts, ts_u, ts], 0))
    TM = np.ascontiguousarray(np.concatenate([tm, tm_u, tm], 0))
    SS = np.ascontiguousarray(np.concatenate([ss, ss, ss], 0))
    SM = np.ascontiguousarray(np.concatenate([sm, sm, sm], 0))
    CS = np.ascontiguousarray(np.concatenate([cs, cs, cs_u], 0))
    CM = np.ascontiguousarray(np.concatenate([cm, cm, cm_u], 0))

    rec = {"model": path.name, "model_bytes": path.stat().st_size, "steps": steps,
           "reps": reps, "sessions": sessions_n, "latent_steps": int(x0.shape[1]),
           "cfg": {"text": CFG_TEXT, "caption": CFG_CAPTION, "speaker": CFG_SPEAKER,
                   "min_t": CFG_MIN_T, "max_t": CFG_MAX_T, "mode": "independent"},
           "loops": []}

    sess3, info3 = make_session(ort, path, ep, device_id, threads)
    rec["session_b3"] = info3
    if sessions_n == 2:
        sess1, info1 = make_session(ort, path, ep, device_id, threads)
        rec["session_b1"] = info1
    else:
        sess1 = sess3
        rec["session_b1"] = "shared with B=3"
    rec["providers_used"] = list(sess3.get_providers())

    want3 = {i.name: DTYPE_MAP.get(i.type, np.float32) for i in sess3.get_inputs()}
    fdt = want3.get("x_t", np.float32)

    sched = t_schedule(steps)
    z_final = None
    try:
        for rep_i in range(max(1, reps)):
            x = x0.astype(fdt, copy=True)
            step_ms, cfg_ms, plain_ms = [], [], []
            n_cfg = 0
            t_loop = time.perf_counter()
            for i in range(steps):
                t = float(sched[i]); t_next = float(sched[i + 1])
                t_step = time.perf_counter()
                if CFG_MIN_T <= t <= CFG_MAX_T:
                    n_cfg += 1
                    feeds = {
                        "x_t": np.ascontiguousarray(np.concatenate([x] * 3, 0)),
                        "t": np.full((3,), t, dtype=fdt),
                        "text_state": TS, "text_mask": TM,
                        "speaker_state": SS, "speaker_mask": SM,
                        "caption_state": CS, "caption_mask": CM,
                    }
                    feeds, _ = cast_feeds(sess3, feeds)
                    v_out = sess3.run(["v"], feeds)[0]
                    c0, c1, c2 = v_out[0:1], v_out[1:2], v_out[2:3]
                    v = c0 + CFG_TEXT * (c0 - c1) + CFG_CAPTION * (c0 - c2)
                    cfg_ms.append(now_ms(t_step))
                else:
                    feeds = {
                        "x_t": np.ascontiguousarray(x),
                        "t": np.full((1,), t, dtype=fdt),
                        "text_state": ts, "text_mask": tm,
                        "speaker_state": ss, "speaker_mask": sm,
                        "caption_state": cs, "caption_mask": cm,
                    }
                    feeds, _ = cast_feeds(sess1, feeds)
                    v = sess1.run(["v"], feeds)[0]
                    plain_ms.append(now_ms(t_step))
                step_ms.append(now_ms(t_step))
                x = x + v * (t_next - t)
            loop_s = time.perf_counter() - t_loop
            rec["loops"].append({
                "rep": rep_i,
                "loop_sec": round(loop_s, 3),
                "n_cfg_steps": n_cfg,
                "first_step_ms": r3(step_ms[0]),
                "step_median_ms": r3(statistics.median(step_ms)),
                "cfg_step_median_ms": r3(statistics.median(cfg_ms)) if cfg_ms else None,
                "cfg_first_ms": r3(cfg_ms[0]) if cfg_ms else None,
                "plain_step_median_ms": r3(statistics.median(plain_ms)) if plain_ms else None,
                "plain_first_ms": r3(plain_ms[0]) if plain_ms else None,
                "latent_absmax": float(np.abs(x).max()),
            })
            z_final = np.asarray(x, dtype=np.float32)
            print("[e2e]   loop %s" % json.dumps(rec["loops"][-1], ensure_ascii=False), flush=True)
    finally:
        if sessions_n == 2:
            free(sess1)
        free(sess3)

    ls = [l["loop_sec"] for l in rec["loops"]]
    rec["cold_sec"] = ls[0] if ls else None
    rec["warm_sec"] = round(statistics.median(ls[1:]), 3) if len(ls) > 1 else (ls[0] if ls else None)
    rec["warm_is_cold"] = len(ls) < 2
    return z_final, rec


# ---------------------------------------------------------------- 本体

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ep", required=True, choices=["cuda", "dml", "cpu"])
    ap.add_argument("--onnx-dir", required=True)
    ap.add_argument("--data-dir", required=True)
    ap.add_argument("--json-out", required=True)
    ap.add_argument("--steps", default="40,10", help="DiT のステップ数（カンマ区切り）")
    ap.add_argument("--reps", type=int, default=2, help="各段の反復（1 本目 = コールド）")
    ap.add_argument("--sessions", type=int, default=2, choices=(1, 2),
                    help="2 = バッチ形状ごとに session を分ける（既定・23 §4-2）")
    ap.add_argument("--device-id", type=int, default=0)
    ap.add_argument("--threads", type=int, default=8, help="CPU EP の intra_op スレッド数")
    ap.add_argument("--no-cpu-ref", action="store_true", help="各段の CPU EP 照合を省く")
    ap.add_argument("--skip-fp16", action="store_true")
    ap.add_argument("--label", default="")
    args = ap.parse_args()

    onnx_dir = Path(args.onnx_dir).resolve()
    data_dir = Path(args.data_dir).resolve()
    out_path = Path(args.json_out)
    steps_list = [int(s) for s in str(args.steps).replace(" ", "").split(",") if s]
    cpu_ref_on = (not args.no_cpu_ref) and args.ep != "cpu"

    R = {
        "schema": "onnx-e2e/1",
        "label": args.label,
        "ep": args.ep,
        "device_id": args.device_id,
        "onnx_dir": str(onnx_dir),
        "data_dir": str(data_dir),
        "audio_sec": AUDIO_SEC,
        "steps": steps_list,
        "reps": args.reps,
        "sessions": args.sessions,
        "cpu_ref": cpu_ref_on,
        "started": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "machine": {
            "platform": platform.platform(),
            "processor": platform.processor(),
            "cpu_count": os.cpu_count(),
            "python": sys.version.split()[0],
        },
        "condition": {
            "note": "13 §1 と同一（本文『こんにちは、読み分けちゃんのテストです。』/ caption『落ち着いた"
                    "女性の声で、丁寧に話す。』/ no_ref / seed 1234 / latent_steps 94 / 音声 3.76 s）",
            "cfg": {"text": CFG_TEXT, "caption": CFG_CAPTION, "speaker": CFG_SPEAKER,
                    "min_t": CFG_MIN_T, "max_t": CFG_MAX_T, "mode": "independent"},
        },
        "stages": {},
        "totals": {},
        "failures": [],
        "notes": [],
    }

    def fail(stage, exc):
        v = "%s: %s" % (type(exc).__name__, str(exc)[:1500])
        R["failures"].append({"stage": stage, "error": v,
                              "traceback": traceback.format_exc()[-1500:]})
        R["stages"].setdefault(stage, {})["error"] = v
        print("[e2e] FAIL %s -> %s" % (stage, v), flush=True)

    # ---- onnxruntime
    try:
        import onnxruntime as ort
    except Exception as exc:
        R["import_error"] = "%s: %s" % (type(exc).__name__, exc)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(json.dumps(R, ensure_ascii=False, indent=2), encoding="utf-8")
        print("[e2e] onnxruntime import failed: %s" % exc)
        return 3
    R["onnxruntime"] = ort.__version__
    R["available_providers"] = list(ort.get_available_providers())
    R["ort_get_device"] = ort.get_device()

    # (a) CUDA EP は preload_dlls() を呼ばないと pip の nvidia DLL を見つけられず黙って CPU に落ちる
    if args.ep == "cuda":
        if hasattr(ort, "preload_dlls"):
            try:
                ort.preload_dlls()
                R["preload_dlls"] = "ok"
            except Exception as exc:
                R["preload_dlls"] = "%s: %s" % (type(exc).__name__, exc)
        else:
            R["preload_dlls"] = "absent (ort %s)" % ort.__version__
        try:
            R["nvidia_smi"] = subprocess.run(
                ["nvidia-smi", "--query-gpu=name,driver_version,memory.total",
                 "--format=csv,noheader"],
                capture_output=True, text=True, timeout=30).stdout.strip()
        except Exception as exc:
            # 例外文字列に cp932 の Windows メッセージが混ざると JSON が化けるので型名だけ残す
            R["nvidia_smi"] = "unavailable (%s)" % type(exc).__name__
    if EP_NAME[args.ep] not in R["available_providers"]:
        R["notes"].append("requested EP %s is NOT in available_providers -> everything below "
                          "runs on the CPU EP" % EP_NAME[args.ep])

    # ---- data
    try:
        cond = dict(np.load(data_dir / "cond_real.npz"))
        x0 = np.load(data_dir / "x0.npz")["x0"]
        R["data"] = {"x0_shape": list(x0.shape),
                     "cond_keys": sorted(cond.keys()),
                     "latent_steps": int(x0.shape[1])}
    except Exception as exc:
        R["data_error"] = "%s: %s" % (type(exc).__name__, exc)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(json.dumps(R, ensure_ascii=False, indent=2), encoding="utf-8")
        print("[e2e] data load failed: %s" % exc)
        return 4
    refs = {}
    for s in steps_list:
        p = data_dir / ("ref_latent_dml_fp32_s%d.npz" % s)
        if p.is_file():
            refs[s] = np.load(p)["latent"]
    R["data"]["reference_latents"] = sorted("s%d" % s for s in refs)
    rng = np.random.default_rng(1234)

    wall0 = time.perf_counter()

    # ---- (b) encode_conditions -------------------------------------------------
    st = "encode_conditions"
    try:
        p = pick_graph(onnx_dir, st, args.ep)
        if p is None:
            raise FileNotFoundError("no graph for %s in %s" % (st, onnx_dir))
        sess, info = make_session(ort, p, args.ep, args.device_id, args.threads)
        feeds = {"text_ids": cond["text_ids"], "text_mask": cond["text_mask_in"],
                 "cap_ids": cond["cap_ids"], "cap_mask": cond["cap_mask_in"],
                 "ref_latent": cond["ref_latent"], "ref_mask": cond["ref_mask_in"]}
        feeds, missing = cast_feeds(sess, feeds)
        if missing:
            spec, src = load_inputs_json(onnx_dir, data_dir, p.name)
            synth = feeds_from_spec(sess, spec, rng)
            for k in missing:
                feeds[k] = synth[k]
            info["synthesized_inputs"] = missing
            info["inputs_json"] = src
        out, tinfo = timed(sess, feeds, args.reps)
        info.update(tinfo)
        names = [o.name for o in sess.get_outputs()]
        info["outputs"] = names
        info["vs_cond_real"] = {n: diffs(cond[n], o) for n, o in zip(names, out)
                                if n in cond and o.dtype != np.bool_}
        if cpu_ref_on:
            ref, rinfo = cpu_reference(ort, p, feeds, args.ep, threads=args.threads)
            if ref is not None:
                rinfo["max_abs_diff_vs_cpu_ep"] = max(
                    diffs(a, b).get("max_abs", float("nan")) for a, b in zip(ref, out))
            info["cpu_ref"] = rinfo
        R["stages"][st] = info
        free(sess)
        print("[e2e] %s cold=%s warm=%s ms (%s)" % (st, info["cold_ms"], info["warm_ms"], p.name),
              flush=True)
    except Exception as exc:
        fail(st, exc)

    # ---- (c) duration ----------------------------------------------------------
    st = "duration"
    try:
        p = pick_graph(onnx_dir, st, args.ep)
        if p is None:
            raise FileNotFoundError("no graph for %s in %s" % (st, onnx_dir))
        sess, info = make_session(ort, p, args.ep, args.device_id, args.threads)
        feeds = {"text_state": cond["text_state"], "text_mask": cond["text_mask"],
                 "speaker_state": cond["speaker_state"], "speaker_mask": cond["speaker_mask"],
                 "caption_state": cond["caption_state"], "caption_mask": cond["caption_mask"],
                 "duration_features": cond["duration_features"],
                 "has_speaker": cond["has_speaker"], "has_caption": np.ones((1,), np.bool_)}
        feeds, missing = cast_feeds(sess, feeds)
        if missing:
            info["missing_inputs"] = missing
        out, tinfo = timed(sess, feeds, args.reps)
        info.update(tinfo)
        info["log1p_frames"] = float(np.asarray(out[0]).reshape(-1)[0])
        if "pred_log_frames" in cond:
            info["vs_cond_real_log1p_frames"] = diffs(cond["pred_log_frames"], out[0])
        if cpu_ref_on:
            ref, rinfo = cpu_reference(ort, p, feeds, args.ep, threads=args.threads)
            if ref is not None:
                rinfo["max_abs_diff_vs_cpu_ep"] = diffs(ref[0], out[0]).get("max_abs")
            info["cpu_ref"] = rinfo
        R["stages"][st] = info
        free(sess)
        print("[e2e] %s cold=%s warm=%s ms" % (st, info["cold_ms"], info["warm_ms"]), flush=True)
    except Exception as exc:
        fail(st, exc)

    # ---- (d)(h) DiT ループ fp32 -------------------------------------------------
    latents = {}
    R["stages"]["dit_loop"] = {}
    p_dit = pick_graph(onnx_dir, "dit_fp32", args.ep)
    for s in steps_list:
        st = "dit_loop.s%d" % s
        try:
            if p_dit is None:
                raise FileNotFoundError("dit_step.onnx not found in %s" % onnx_dir)
            print("[e2e] dit_loop steps=%d ..." % s, flush=True)
            z, rec = dit_loop(ort, p_dit, args.ep, args.device_id, x0, cond, s,
                              args.reps, args.sessions, args.threads)
            if s in refs:
                rec["vs_reference_latent"] = diffs(refs[s], z)
                rec["reference"] = "ref_latent_dml_fp32_s%d.npz (AMD DirectML fp32・torch と 5e-5 以内)" % s
            latents[s] = z
            R["stages"]["dit_loop"]["s%d" % s] = rec
            print("[e2e] dit_loop s%d cold=%ss warm=%ss diff=%s" % (
                s, rec["cold_sec"], rec["warm_sec"],
                (rec.get("vs_reference_latent") or {}).get("max_abs")), flush=True)
        except Exception as exc:
            fail(st, exc)

    # ---- (e) decode ------------------------------------------------------------
    st = "decode"
    try:
        p = pick_graph(onnx_dir, st, args.ep)
        if p is None:
            raise FileNotFoundError("no decoder graph in %s" % onnx_dir)
        s_main = steps_list[0] if steps_list else 40
        z = latents.get(s_main)
        if z is None:
            z = refs.get(s_main) or (list(refs.values())[0] if refs else None)
            if z is None:
                raise RuntimeError("no latent to decode (DiT loop failed and no reference)")
            src = "reference latent (DiT loop failed)"
        else:
            src = "DiT loop s%d output" % s_main
        sess, info = make_session(ort, p, args.ep, args.device_id, args.threads)
        info["latent_source"] = src
        feeds = {"latent": np.ascontiguousarray(z.transpose(0, 2, 1))}
        feeds, missing = cast_feeds(sess, feeds)
        if missing:
            info["missing_inputs"] = missing
        out, tinfo = timed(sess, feeds, args.reps)
        info.update(tinfo)
        wav = np.asarray(out[0], dtype=np.float32)
        info["wav_shape"] = list(wav.shape)
        info["wav_absmax"] = float(np.abs(wav).max())
        info["wav_sec"] = round(wav.shape[-1] / 48000.0, 4)
        if s_main in refs and latents.get(s_main) is not None:
            fr = {"latent": np.ascontiguousarray(refs[s_main].transpose(0, 2, 1))}
            fr, _ = cast_feeds(sess, fr)
            wav_ref = np.asarray(sess.run(None, fr)[0], dtype=np.float32)
            info["wav_vs_reference_latent_decode"] = diffs(wav_ref, wav)
        if cpu_ref_on:
            ref, rinfo = cpu_reference(ort, p, feeds, args.ep, threads=args.threads)
            if ref is not None:
                rinfo["max_abs_diff_vs_cpu_ep"] = diffs(ref[0], out[0]).get("max_abs")
            info["cpu_ref"] = rinfo
        R["stages"][st] = info
        free(sess)
        print("[e2e] %s cold=%s warm=%s ms (%s)" % (st, info["cold_ms"], info["warm_ms"], p.name),
              flush=True)
    except Exception as exc:
        fail(st, exc)

    # ---- (f) watermark ---------------------------------------------------------
    st = "watermark"
    try:
        p = pick_graph(onnx_dir, st, args.ep)
        if p is None:
            raise FileNotFoundError("no watermark graph in %s" % onnx_dir)
        spec, src = load_inputs_json(onnx_dir, data_dir, p.name)
        info = {"model": p.name, "model_bytes": p.stat().st_size, "inputs_json": src,
                "input_note": "12/23 便と同じ静的形（y_pad 167936 サンプル = 3.50 s）で計測。"
                              "実音声 3.76 s より 7 % 短い＝わずかに楽な計測"}
        sess = None
        try:
            sess, sinfo = make_session(ort, p, args.ep, args.device_id, args.threads)
            info.update(sinfo)
            info["ep_used"] = (args.ep if EP_NAME[args.ep] in sess.get_providers()
                               else "cpu (the session fell back inside ORT)")
        except Exception as exc:
            info["ep_error"] = "%s: %s" % (type(exc).__name__, str(exc)[:1200])
            info["ep_used"] = "cpu (fallback)"
            info["fallback_reason"] = "requested EP could not load the graph -- retrying on CPU EP"
            sess, sinfo = make_session(ort, p, "cpu", 0, args.threads)
            info.update({k: v for k, v in sinfo.items() if k != "model"})
        feeds = feeds_from_spec(sess, spec, rng)
        feeds, missing = cast_feeds(sess, feeds)
        if missing:
            info["missing_inputs"] = missing
        out, tinfo = timed(sess, feeds, args.reps)
        info.update(tinfo)
        info["output_shapes"] = [list(np.asarray(o).shape) for o in out]
        info["output_absmax"] = [float(np.max(np.abs(np.asarray(o, dtype=np.float64)))) for o in out]
        if cpu_ref_on and info.get("ep_used") == args.ep:
            ref, rinfo = cpu_reference(ort, p, feeds, args.ep, threads=args.threads)
            if ref is not None:
                rinfo["max_abs_diff_vs_cpu_ep"] = max(
                    diffs(a, b).get("max_abs", float("nan")) for a, b in zip(ref, out))
            info["cpu_ref"] = rinfo
        R["stages"][st] = info
        free(sess)
        print("[e2e] %s cold=%s warm=%s ms (ep=%s)" % (
            st, info["cold_ms"], info["warm_ms"], info.get("ep_used")), flush=True)
    except Exception as exc:
        fail(st, exc)
        # 透かし本体が載らないときは部品（STFT + enc_c + dec_c）の合計を測る（23 §5）
        parts = {}
        for name in WM_PARTS:
            pp = onnx_dir / name
            if not pp.is_file():
                continue
            try:
                sess, pinfo = make_session(ort, pp, args.ep, args.device_id, args.threads)
                spec, srcj = load_inputs_json(onnx_dir, data_dir, name)
                feeds = feeds_from_spec(sess, spec, rng)
                feeds, _ = cast_feeds(sess, feeds)
                _, tinfo = timed(sess, feeds, args.reps)
                pinfo.update(tinfo)
                pinfo["inputs_json"] = srcj
                parts[name] = pinfo
                free(sess)
            except Exception as exc2:
                parts[name] = {"error": "%s: %s" % (type(exc2).__name__, str(exc2)[:600])}
        if parts:
            wsum = sum(v["warm_ms"] for v in parts.values() if v.get("warm_ms"))
            csum = sum(v["cold_ms"] for v in parts.values() if v.get("cold_ms"))
            R["stages"]["watermark_parts"] = {
                "note": "透かし本体が載らないので部品の合計で代用（iSTFT 抜き・23 §5 と同じ勘定）",
                "parts": parts, "warm_ms_sum": r3(wsum), "cold_ms_sum": r3(csum)}

    # ---- (i) fp16 DiT ----------------------------------------------------------
    if not args.skip_fp16:
        st = "dit_fp16"
        try:
            p = pick_graph(onnx_dir, "dit_fp16", args.ep)
            if p is None:
                raise FileNotFoundError("dit_step_fp16.onnx not found in %s" % onnx_dir)
            s = steps_list[0] if steps_list else 40
            print("[e2e] dit_fp16 steps=%d (1 rep) ..." % s, flush=True)
            z16, rec = dit_loop(ort, p, args.ep, args.device_id, x0, cond, s, 1,
                                args.sessions, args.threads)
            if s in refs:
                rec["vs_reference_latent"] = diffs(refs[s], z16)
            if latents.get(s) is not None:
                rec["vs_fp32_latent_same_run"] = diffs(latents[s], z16)
            rec["note"] = ("23 §4-3/4-4・25 §要点 6＝fp16 の DiT は GPU EP で数値が壊れる"
                           "（v の absmax 4.29 -> 0.072）。ここでの latent_absmax と参照との差が"
                           "その再確認。壊れていれば max_abs は 5 前後・rel_l2 は 1 前後になる")
            R["stages"][st] = rec
            print("[e2e] dit_fp16 s%d warm=%ss absmax=%s diff=%s" % (
                s, rec["warm_sec"], rec["loops"][-1]["latent_absmax"] if rec["loops"] else None,
                (rec.get("vs_reference_latent") or {}).get("max_abs")), flush=True)
        except Exception as exc:
            fail(st, exc)

    # ---- 実際にどの EP で走ったか（EP Error で黙って CPU に落ちることがある） ----
    def collect_providers(node, acc):
        if isinstance(node, dict):
            pu = node.get("providers_used")
            if isinstance(pu, list):
                acc.add(tuple(pu))
            for v in node.values():
                collect_providers(v, acc)
    seen = set()
    collect_providers(R["stages"], seen)
    R["ep_effective"] = {
        "provider_sets_seen": sorted([list(t) for t in seen]),
        "requested_ep_used_everywhere": (all(EP_NAME[args.ep] in t for t in seen)
                                         if seen else None),
    }

    # ---- (g) totals ------------------------------------------------------------
    def stage_ms(name, key):
        d = R["stages"].get(name) or {}
        v = d.get(key)
        return v if isinstance(v, (int, float)) else None

    wm_name = "watermark" if "warm_ms" in (R["stages"].get("watermark") or {}) else "watermark_parts"
    wm_key = "warm_ms" if wm_name == "watermark" else "warm_ms_sum"
    wm_key_c = "cold_ms" if wm_name == "watermark" else "cold_ms_sum"

    for s in steps_list:
        loop = (R["stages"].get("dit_loop") or {}).get("s%d" % s) or {}
        for kind, ck, dk, lk in (("warm", "warm_ms", wm_key, "warm_sec"),
                                 ("cold", "cold_ms", wm_key_c, "cold_sec")):
            parts = {
                "encode_conditions": stage_ms("encode_conditions", ck),
                "duration": stage_ms("duration", ck),
                "dit_loop": (loop.get(lk) * 1000.0) if loop.get(lk) else None,
                "decode": stage_ms("decode", ck),
                wm_name: stage_ms(wm_name, dk),
            }
            missing = [k for k, v in parts.items() if v is None]
            total_ms = sum(v for v in parts.values() if v is not None)
            R["totals"]["steps%d_%s" % (s, kind)] = {
                "ms": r3(total_ms),
                "sec": round(total_ms / 1000.0, 3),
                "rtf": round(total_ms / 1000.0 / AUDIO_SEC, 3),
                "breakdown_ms": {k: r3(v) for k, v in parts.items()},
                "missing_stages": missing,
            }

    R["wall_sec"] = round(time.perf_counter() - wall0, 1)
    R["reference_numbers"] = {
        "amd_8060S_dml_fp32_40steps": {"sec": 2.815, "rtf": 0.749, "src": "23 §6"},
        "amd_8060S_dml_fp32_10steps": {"sec": 1.036, "rtf": 0.276, "src": "23 §6"},
        "ort_cpu_ep_1244_40steps": {"sec": 12.305, "rtf": 3.273, "src": "23 §6"},
        "torch_cpu_end_to_end_40steps": {"sec": 15.262, "rtf": 4.06, "src": "11 §5"},
        "rocm_torch_bf16_warm_40steps": {"sec": 0.749, "rtf": 0.197, "src": "20 §2.3"},
        "cuda_ep_estimate_40steps": {"sec": 1.05, "rtf": 0.28, "src": "25 §要点 8（見立て・未実測）"},
    }
    R["finished"] = time.strftime("%Y-%m-%dT%H:%M:%S")

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(R, ensure_ascii=False, indent=2), encoding="utf-8")
    print("[e2e] wrote %s (wall %.1f s, failures %d)" % (out_path, R["wall_sec"], len(R["failures"])))
    for k, v in R["totals"].items():
        print("[e2e]   %-16s %8.3f s  RTF %6.3f  missing=%s" % (
            k, v["sec"], v["rtf"], ",".join(v["missing_stages"]) or "-"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
