#!/usr/bin/env python3
"""lab 専用の infer.py 実行シム（GPU 版・upstream を 1 バイトも変更しない）。

lab/bin/infer_cpu.py の写し ＋ GPU 用の追加計測:
  - torch.cuda.max_memory_allocated() / max_memory_reserved()（VRAM ピーク）
  - torch.version.hip / torch.cuda.get_device_name(0)
  - InferenceRuntime.from_key の所要（モデル読み込み時間）
  - infer.py が出す [timing] 行を横取りして JSON に収める
  - 出力 wav の sha256

torchaudio.save は soundfile 実装に差し替える（Windows の torchcodec 問題の回避。
GPU 上のテンソルは .to("cpu") で降ろす）。

使い方:
  python lab/bin/infer_gpu.py --json-out <path> [--tag <name>] -- <infer.py に渡す引数...>
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import runpy
import sys
import time
from pathlib import Path

UPSTREAM = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS")

argv = sys.argv[1:]
json_out = None
tag = None
while argv and argv[0] in ("--json-out", "--tag"):
    if argv[0] == "--json-out":
        json_out = argv[1]
    else:
        tag = argv[1]
    argv = argv[2:]
if argv and argv[0] == "--":
    argv = argv[1:]

import soundfile as sf  # noqa: E402
import torch  # noqa: E402
import torchaudio  # noqa: E402


def _save_soundfile(uri, src, sample_rate, **kwargs):
    audio = src.detach().to(device="cpu", dtype=torch.float32)
    arr = audio.squeeze(0).numpy() if audio.shape[0] == 1 else audio.T.numpy()
    sf.write(str(uri), arr, int(sample_rate))


torchaudio.save = _save_soundfile

# ---- infer.py の stdout を tee して [timing] 行を拾う ----
TIMING_RE = re.compile(r"^\[timing\] ([A-Za-z0-9_]+): ([0-9.]+) ms\s*$")
TOTAL_RE = re.compile(r"^\[timing\] total_to_decode: ([0-9.]+) s\s*$")
SEED_RE = re.compile(r"^\[seed\] used_seed: (-?[0-9]+)\s*$")
stage_timings_ms: dict[str, float] = {}
captured: dict[str, object] = {}


class _Tee:
    def __init__(self, stream):
        self._s = stream
        self._buf = ""

    def write(self, s):
        self._s.write(s)
        self._buf += s
        while "\n" in self._buf:
            line, self._buf = self._buf.split("\n", 1)
            m = TIMING_RE.match(line)
            if m:
                stage_timings_ms[m.group(1)] = float(m.group(2))
                continue
            m = TOTAL_RE.match(line)
            if m:
                captured["total_to_decode_s"] = float(m.group(1))
                continue
            m = SEED_RE.match(line)
            if m:
                captured["used_seed"] = int(m.group(1))
        return len(s)

    def flush(self):
        self._s.flush()

    def __getattr__(self, name):
        return getattr(self._s, name)


# ---- モデル読み込み時間の計測（クラスに直接パッチ＝import 形式に依存しない） ----
t_import0 = time.perf_counter()
import irodori_tts.inference_runtime as _ir  # noqa: E402

t_import = time.perf_counter() - t_import0

_orig_from_key = _ir.InferenceRuntime.from_key


def _timed_from_key(*a, **kw):
    t0 = time.perf_counter()
    rt = _orig_from_key(*a, **kw)
    try:
        torch.cuda.synchronize()
    except Exception:
        pass
    captured["model_load_s"] = round(time.perf_counter() - t0, 3)
    try:
        captured["vram_after_load_alloc_mb"] = round(torch.cuda.memory_allocated() / 1048576.0, 1)
        captured["vram_after_load_reserved_mb"] = round(torch.cuda.memory_reserved() / 1048576.0, 1)
    except Exception:
        pass
    return rt


_ir.InferenceRuntime.from_key = classmethod(lambda cls, *a, **kw: _timed_from_key(*a, **kw))

out_wav = None
for i, a in enumerate(argv):
    if a == "--output-wav":
        out_wav = argv[i + 1]

try:
    torch.cuda.reset_peak_memory_stats()
except Exception:
    pass

sys.argv = [str(UPSTREAM / "infer.py")] + argv
_real_stdout = sys.stdout
sys.stdout = _Tee(_real_stdout)
t0 = time.perf_counter()
try:
    runpy.run_path(str(UPSTREAM / "infer.py"), run_name="__main__")
finally:
    sys.stdout = _real_stdout
wall = time.perf_counter() - t0

info: dict[str, object] = {
    "tag": tag,
    "wall_seconds_total": round(wall, 3),
    "import_inference_runtime_s": round(t_import, 3),
    "stage_timings_ms": stage_timings_ms,
}
info.update(captured)

# ---- GPU 情報 ----
gpu: dict[str, object] = {}
try:
    gpu["torch_version"] = torch.__version__
    gpu["hip"] = torch.version.hip
    gpu["cuda"] = torch.version.cuda
    gpu["is_available"] = torch.cuda.is_available()
    gpu["device_name"] = torch.cuda.get_device_name(0)
    gpu["max_memory_allocated_mb"] = round(torch.cuda.max_memory_allocated() / 1048576.0, 1)
    gpu["max_memory_reserved_mb"] = round(torch.cuda.max_memory_reserved() / 1048576.0, 1)
    free_b, total_b = torch.cuda.mem_get_info()
    gpu["device_free_mb"] = round(free_b / 1048576.0, 1)
    gpu["device_total_mb"] = round(total_b / 1048576.0, 1)
except Exception as exc:  # pragma: no cover
    gpu["error"] = repr(exc)
info["gpu"] = gpu

for k in ("OMP_NUM_THREADS", "HSA_OVERRIDE_GFX_VERSION", "PYTORCH_HIP_ALLOC_CONF"):
    if os.environ.get(k):
        info.setdefault("env", {})[k] = os.environ[k]  # type: ignore[index]

try:
    import psutil

    mi = psutil.Process().memory_info()
    info["peak_wset_mb"] = round(getattr(mi, "peak_wset", mi.rss) / 1048576.0, 1)
    info["rss_mb_end"] = round(mi.rss / 1048576.0, 1)
except Exception as exc:  # pragma: no cover
    info["peak_wset_mb"] = f"unavailable: {exc}"

if out_wav and Path(out_wav).is_file():
    with sf.SoundFile(out_wav) as f:
        info["sample_rate"] = f.samplerate
        info["channels"] = f.channels
        info["frames"] = len(f)
        info["audio_seconds"] = round(len(f) / float(f.samplerate), 3)
        info["subtype"] = f.subtype
    info["wav_bytes"] = Path(out_wav).stat().st_size
    info["sha256"] = hashlib.sha256(Path(out_wav).read_bytes()).hexdigest()
    info["rtf_total"] = round(wall / float(info["audio_seconds"]), 3)
    if "total_to_decode_s" in info:
        info["rtf_synth"] = round(
            float(info["total_to_decode_s"]) / float(info["audio_seconds"]), 3
        )

print("[lab] " + json.dumps(info, ensure_ascii=False))
if json_out:
    Path(json_out).write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding="utf-8")
