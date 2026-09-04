"""29 — torch 側のデバイス列挙と可視化変数の効き（読むだけ・GPU 触る）。
実行系＝C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe
出力＝JSON を stdout へ。
"""
from __future__ import annotations

import json
import os
import sys
import traceback

out: dict = {}
out["env_seen"] = {
    k: os.environ.get(k)
    for k in (
        "HIP_VISIBLE_DEVICES",
        "CUDA_VISIBLE_DEVICES",
        "ROCR_VISIBLE_DEVICES",
        "GPU_DEVICE_ORDINAL",
    )
}

import torch  # noqa: E402

out["torch_version"] = torch.__version__
out["hip"] = torch.version.hip
out["cuda"] = torch.version.cuda


def safe(fn):
    try:
        return {"ok": True, "value": fn()}
    except Exception as exc:  # noqa: BLE001
        return {"ok": False, "err_type": type(exc).__name__, "err": str(exc)}


out["is_available"] = safe(lambda: bool(torch.cuda.is_available()))
out["device_count"] = safe(lambda: int(torch.cuda.device_count()))

n = out["device_count"].get("value") or 0
devs = []
for i in range(max(n, 1)):
    d: dict = {"i": i}
    d["get_device_name"] = safe(lambda i=i: torch.cuda.get_device_name(i))
    def props(i=i):
        p = torch.cuda.get_device_properties(i)
        attrs = {}
        for a in dir(p):
            if a.startswith("_"):
                continue
            try:
                v = getattr(p, a)
            except Exception as exc:  # noqa: BLE001
                v = f"<err {type(exc).__name__}: {exc}>"
            if callable(v):
                continue
            attrs[a] = str(v)
        return {"repr": str(p), "dir": sorted(a for a in dir(p) if not a.startswith("_")), "attrs": attrs}
    d["get_device_properties"] = safe(props)
    d["capability"] = safe(lambda i=i: str(torch.cuda.get_device_capability(i)))
    d["mem_get_info"] = safe(lambda i=i: [int(x) for x in torch.cuda.mem_get_info(i)])
    devs.append(d)
out["devices"] = devs

out["current_device"] = safe(lambda: int(torch.cuda.current_device()))


def ctx_probe():
    r = {}
    for i in (0, 1):
        try:
            with torch.cuda.device(i):
                r[str(i)] = {"ok": True, "current_device": int(torch.cuda.current_device())}
        except Exception as exc:  # noqa: BLE001
            r[str(i)] = {"ok": False, "err_type": type(exc).__name__, "err": str(exc)}
    return r


out["torch_cuda_device_ctx"] = ctx_probe()

# 実際に確保してみる（範囲外 index の逐語エラーを取る）
alloc = {}
for spec in ("cuda", "cuda:0", "cuda:1", "cuda:5", "cuda:-1", "cuda:abc", "CUDA:0", "cuda:0 ", "gpu", "cpu"):
    e: dict = {"spec": repr(spec)}
    try:
        dev = torch.device(spec)
        e["torch_device"] = str(dev)
        e["type"] = dev.type
        e["index"] = dev.index
    except Exception as exc:  # noqa: BLE001
        e["torch_device_err_type"] = type(exc).__name__
        e["torch_device_err"] = str(exc)
        alloc[spec] = e
        continue
    try:
        t = torch.zeros(4, device=dev)
        e["alloc"] = "ok"
        e["alloc_device"] = str(t.device)
        del t
    except Exception as exc:  # noqa: BLE001
        e["alloc"] = "err"
        e["alloc_err_type"] = type(exc).__name__
        e["alloc_err"] = str(exc)
    # resolve_runtime_device も通す
    try:
        from irodori_tts.inference_runtime import resolve_runtime_device

        e["resolve_runtime_device"] = str(resolve_runtime_device(spec))
    except Exception as exc:  # noqa: BLE001
        e["rrd_err_type"] = type(exc).__name__
        e["rrd_err"] = str(exc)
    alloc[spec] = e
out["alloc_probe"] = alloc

# _parse_visible_devices の実挙動
try:
    from torch.cuda import _parse_visible_devices  # type: ignore

    out["_parse_visible_devices"] = str(_parse_visible_devices())
except Exception as exc:  # noqa: BLE001
    out["_parse_visible_devices_err"] = f"{type(exc).__name__}: {exc}"

try:
    out["_device_count_amdsmi_or_nvml"] = str(torch.cuda._device_count_amdsmi())  # type: ignore[attr-defined]
except Exception as exc:  # noqa: BLE001
    out["_device_count_amdsmi_err"] = f"{type(exc).__name__}: {exc}"

try:
    from irodori_tts.inference_runtime import (
        default_runtime_device,
        list_available_runtime_devices,
    )

    out["irodori_list_available_runtime_devices"] = list_available_runtime_devices()
    out["irodori_default_runtime_device"] = default_runtime_device()
    out["irodori_tts_file"] = __import__("irodori_tts").__file__
except Exception as exc:  # noqa: BLE001
    out["irodori_err"] = f"{type(exc).__name__}: {exc}\n{traceback.format_exc()}"

json.dump(out, sys.stdout, ensure_ascii=False, indent=1)
