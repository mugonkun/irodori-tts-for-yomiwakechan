#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""CUDA 検証キット用の合成ベンチ（lab/bin/infer_cpu.py と同型のシム）。

lab/bin/infer_cpu.py との差分:
  1. 同一プロセス内で N 回まわす。InferenceRuntime.from_key を memo 化するので
     1 回目＝コールド（モデル読み込み＋CUDA の形状チューニング込み）、2 回目以降＝ウォーム。
  2. torch.cuda の情報（get_device_name / version.cuda / cudnn / max_memory_allocated /
     total_memory）と nvidia-smi のドライバ版を JSON に落とす。
  3. upstream infer.py の [timing] 行を捕まえて段別時刻を JSON にする。

upstream は 1 バイトも変更しない（zip から展開したものを PYTHONPATH で読むだけ）。

使い方:
  python bench_infer.py --json-out out.json --label steps40_short --repeats 3 \
      --src <Irodori-TTS のフォルダ> -- <infer.py に渡す引数...>
"""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
import platform
import re
import runpy
import subprocess
import sys
import time
import traceback
from pathlib import Path

# ---------------------------------------------------------------- 引数


def _parse_args(argv):
    opts = {
        "json_out": None,
        "label": "bench",
        "repeats": 3,
        "src": os.environ.get("IRODORI_SRC"),
        "cases_json": None,
        "case": None,
    }
    rest = []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--":
            rest = argv[i + 1 :]
            break
        if a == "--json-out":
            opts["json_out"] = argv[i + 1]
            i += 2
        elif a == "--label":
            opts["label"] = argv[i + 1]
            i += 2
        elif a == "--repeats":
            opts["repeats"] = int(argv[i + 1])
            i += 2
        elif a == "--src":
            opts["src"] = argv[i + 1]
            i += 2
        elif a == "--cases-json":
            opts["cases_json"] = argv[i + 1]
            i += 2
        elif a == "--case":
            opts["case"] = argv[i + 1]
            i += 2
        else:
            raise SystemExit("unknown option: %s" % a)
    return opts, rest


OPTS, INFER_ARGV = _parse_args(sys.argv[1:])

# --text / --caption は PowerShell を通さず、UTF-8 の cases.json から直接注入する
# （PS 5.1 は BOM 無しの非 ASCII を化けさせるため。cases.json は 11 §5 と同一文面）
CASE_META = None
if OPTS["cases_json"] and OPTS["case"]:
    _cj = json.loads(Path(OPTS["cases_json"]).read_text(encoding="utf-8"))
    _case = _cj["cases"][OPTS["case"]]
    CASE_META = {"case": OPTS["case"], "text": _case["text"], "caption": _cj.get("caption")}
    INFER_ARGV = list(INFER_ARGV) + ["--text", _case["text"]]
    if _cj.get("caption"):
        INFER_ARGV += ["--caption", _cj["caption"]]
if not OPTS["src"]:
    raise SystemExit("--src (or IRODORI_SRC) is required")
SRC = Path(OPTS["src"]).resolve()
INFER_PY = SRC / "infer.py"
if not INFER_PY.is_file():
    raise SystemExit("infer.py not found under %s" % SRC)
if str(SRC) not in sys.path:
    sys.path.insert(0, str(SRC))

# ---------------------------------------------------------------- 環境情報

RESULT = {
    "label": OPTS["label"],
    "repeats": OPTS["repeats"],
    "case": CASE_META,
    "infer_argv": INFER_ARGV,
    "python": sys.version.split()[0],
    "platform": platform.platform(),
    "env": {
        k: os.environ.get(k)
        for k in (
            "OMP_NUM_THREADS",
            "MKL_NUM_THREADS",
            "HF_HOME",
            "HF_HUB_OFFLINE",
            "CUDA_VISIBLE_DEVICES",
            "PYTORCH_CUDA_ALLOC_CONF",
        )
    },
    "runs": [],
    "errors": [],
}


def _nvidia_smi_driver():
    try:
        out = subprocess.run(
            ["nvidia-smi", "--query-gpu=driver_version,name,memory.total", "--format=csv,noheader"],
            capture_output=True,
            text=True,
            timeout=60,
        )
        if out.returncode == 0:
            return [ln.strip() for ln in out.stdout.strip().splitlines() if ln.strip()]
        return ["nvidia-smi rc=%d: %s" % (out.returncode, out.stderr.strip()[:400])]
    except FileNotFoundError:
        return ["nvidia-smi not found on PATH (no NVIDIA driver on this machine?)"]
    except Exception as exc:
        return ["nvidia-smi unavailable: %s" % (type(exc).__name__,)]


import soundfile as sf  # noqa: E402
import torch  # noqa: E402
import torchaudio  # noqa: E402

RESULT["torch"] = {
    "version": torch.__version__,
    "version_cuda": torch.version.cuda,
    "version_hip": getattr(torch.version, "hip", None),
    "cuda_is_available": bool(torch.cuda.is_available()),
    "cudnn_version": None,
    "device_count": 0,
    "devices": [],
}
try:
    RESULT["torch"]["cudnn_version"] = torch.backends.cudnn.version()
except Exception as exc:
    RESULT["errors"].append("cudnn_version: %r" % (exc,))
if torch.cuda.is_available():
    try:
        n = torch.cuda.device_count()
        RESULT["torch"]["device_count"] = n
        for i in range(n):
            p = torch.cuda.get_device_properties(i)
            RESULT["torch"]["devices"].append(
                {
                    "index": i,
                    "name": torch.cuda.get_device_name(i),
                    "capability": "%d.%d" % (p.major, p.minor),
                    "total_memory_mib": round(p.total_memory / 1048576.0, 1),
                    "multi_processor_count": getattr(p, "multi_processor_count", None),
                }
            )
    except Exception as exc:
        RESULT["errors"].append("device_properties: %r" % (exc,))
RESULT["nvidia_smi"] = _nvidia_smi_driver()

# ---------------------------------------------------------------- シム 1: torchaudio.save


def _save_soundfile(uri, src, sample_rate, **kwargs):
    audio = src.detach().to(device="cpu", dtype=torch.float32)
    arr = audio.squeeze(0).numpy() if audio.shape[0] == 1 else audio.T.numpy()
    sf.write(str(uri), arr, int(sample_rate))
    return str(uri)


torchaudio.save = _save_soundfile

# ---------------------------------------------------------------- シム 2: runtime の memo 化

_RUNTIME_CACHE = {}
_MEMO_OK = False
try:
    from irodori_tts import inference_runtime as _ir

    _orig_from_key = _ir.InferenceRuntime.from_key

    def _memo_from_key(key):
        k = repr(key)
        if k not in _RUNTIME_CACHE:
            _RUNTIME_CACHE[k] = _orig_from_key(key)
        return _RUNTIME_CACHE[k]

    _ir.InferenceRuntime.from_key = _memo_from_key
    _MEMO_OK = True
except Exception as exc:
    RESULT["errors"].append("runtime memo patch failed: %r" % (exc,))
RESULT["runtime_memoized"] = _MEMO_OK

# ---------------------------------------------------------------- 実行

_TIMING_RE = re.compile(r"^\[timing\]\s+(\S+):\s+([0-9.]+)\s*(ms|s)\s*$")
_SEED_RE = re.compile(r"^\[seed\]\s+used_seed:\s+(\S+)\s*$")


def _parse_stdout(text):
    stages = {}
    total = None
    seed = None
    for line in text.splitlines():
        m = _TIMING_RE.match(line.strip())
        if m:
            name, val, unit = m.group(1), float(m.group(2)), m.group(3)
            if name == "total_to_decode":
                total = val if unit == "s" else val / 1000.0
            else:
                stages[name] = round(val if unit == "ms" else val * 1000.0, 1)
            continue
        m = _SEED_RE.match(line.strip())
        if m:
            seed = m.group(1)
    return stages, total, seed


out_wav = None
for i, a in enumerate(INFER_ARGV):
    if a == "--output-wav":
        out_wav = INFER_ARGV[i + 1]

for rep in range(1, OPTS["repeats"] + 1):
    entry = {"rep": rep, "kind": "cold" if rep == 1 else "warm"}
    if torch.cuda.is_available():
        try:
            torch.cuda.reset_peak_memory_stats()
            torch.cuda.synchronize()
        except Exception as exc:
            entry["cuda_reset_error"] = repr(exc)
    buf = io.StringIO()
    sys.argv = [str(INFER_PY)] + list(INFER_ARGV)
    t0 = time.perf_counter()
    ok = True
    try:
        with contextlib.redirect_stdout(buf):
            runpy.run_path(str(INFER_PY), run_name="__main__")
    except BaseException as exc:  # SystemExit も拾う
        ok = False
        entry["error"] = "%s: %s" % (type(exc).__name__, exc)
        entry["traceback"] = traceback.format_exc()[-4000:]
    wall = time.perf_counter() - t0
    text = buf.getvalue()
    sys.stdout.write(text)
    sys.stdout.flush()
    entry["ok"] = ok
    entry["wall_seconds_total"] = round(wall, 3)
    stages, total, seed = _parse_stdout(text)
    entry["stage_timings_ms"] = stages
    entry["total_to_decode_s"] = total
    entry["used_seed"] = seed
    entry["stdout_tail"] = text[-1500:]
    if torch.cuda.is_available():
        try:
            torch.cuda.synchronize()
            entry["cuda_max_memory_allocated_mib"] = round(
                torch.cuda.max_memory_allocated() / 1048576.0, 1
            )
            entry["cuda_max_memory_reserved_mib"] = round(
                torch.cuda.max_memory_reserved() / 1048576.0, 1
            )
        except Exception as exc:
            entry["cuda_mem_error"] = repr(exc)
    try:
        import psutil

        mi = psutil.Process().memory_info()
        entry["peak_wset_mb"] = round(getattr(mi, "peak_wset", mi.rss) / 1048576.0, 1)
    except Exception:
        pass
    if ok and out_wav and Path(out_wav).is_file():
        try:
            with sf.SoundFile(out_wav) as f:
                entry["sample_rate"] = f.samplerate
                entry["channels"] = f.channels
                entry["frames"] = len(f)
                entry["audio_seconds"] = round(len(f) / float(f.samplerate), 3)
                entry["subtype"] = f.subtype
            entry["wav_bytes"] = Path(out_wav).stat().st_size
            entry["wav_sha256"] = hashlib.sha256(Path(out_wav).read_bytes()).hexdigest()
            if entry.get("audio_seconds"):
                if total is not None:
                    entry["rtf_synth"] = round(total / entry["audio_seconds"], 3)
                entry["rtf_wall"] = round(wall / entry["audio_seconds"], 3)
        except Exception as exc:
            entry["wav_error"] = repr(exc)
    RESULT["runs"].append(entry)
    print(
        "[kit] %s rep=%d kind=%s ok=%s wall=%.2fs total_to_decode=%s rtf=%s"
        % (
            OPTS["label"],
            rep,
            entry["kind"],
            ok,
            wall,
            entry.get("total_to_decode_s"),
            entry.get("rtf_synth"),
        ),
        flush=True,
    )
    if not ok and rep == 1:
        # 1 回目が落ちる条件（bf16 on CPU 等）は繰り返しても同じ＝早く抜ける
        RESULT["aborted_after_first_failure"] = True
        break

warm = [r for r in RESULT["runs"] if r.get("kind") == "warm" and r.get("ok")]
if warm:
    tt = [r["total_to_decode_s"] for r in warm if r.get("total_to_decode_s") is not None]
    rf = [r["rtf_synth"] for r in warm if r.get("rtf_synth") is not None]
    if tt:
        RESULT["warm_median_total_to_decode_s"] = round(sorted(tt)[len(tt) // 2], 3)
    if rf:
        RESULT["warm_median_rtf_synth"] = round(sorted(rf)[len(rf) // 2], 3)
cold = [r for r in RESULT["runs"] if r.get("kind") == "cold" and r.get("ok")]
if cold:
    RESULT["cold_total_to_decode_s"] = cold[0].get("total_to_decode_s")
    RESULT["cold_rtf_synth"] = cold[0].get("rtf_synth")
    RESULT["cold_wall_seconds_total"] = cold[0].get("wall_seconds_total")
RESULT["ok"] = any(r.get("ok") for r in RESULT["runs"])

if OPTS["json_out"]:
    Path(OPTS["json_out"]).parent.mkdir(parents=True, exist_ok=True)
    Path(OPTS["json_out"]).write_text(
        json.dumps(RESULT, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print("[kit] wrote %s" % OPTS["json_out"], flush=True)
sys.exit(0 if RESULT["ok"] else 3)
