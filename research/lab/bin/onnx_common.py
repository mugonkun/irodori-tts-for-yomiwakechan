"""lab 共通ヘルパ（第 2 便・ONNX 実験）。upstream は 1 バイトも変更しない。

- DACVAE のロード（irodori 側の alpha=0.0 / watermark 差し替え済みの状態）
- weight_norm の fold（旧 API: weight_g/weight_v）
- Snake1d の eager 版差し替え（dynamo 経路で @torch.jit.script が問題になる場合の退路）
- 照合ユーティリティ
"""

from __future__ import annotations

import json
import os
import statistics
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

LAB = Path(__file__).resolve().parents[1]
ONNX_DIR = LAB / "onnx"
OUT_DIR = LAB / "out"


# ---------------------------------------------------------------- Snake（eager 版）
class LabSnake1d(nn.Module):
    """dacvae.nn.layers.Snake1d と数値等価な eager 実装（@torch.jit.script を使わない）。

    原文: dacvae/nn/layers.py:18-24, 38-44
        x = x.reshape(shape[0], shape[1], -1)
        x = x + (alpha + 1e-9).reciprocal() * torch.sin(alpha * x).pow(2)
        x = x.reshape(shape)
    入力は常に (B, C, T) の 3 次元なので reshape は恒等。
    """

    def __init__(self, alpha: torch.Tensor):
        super().__init__()
        self.alpha = nn.Parameter(alpha.detach().clone())

    def forward(self, x):
        return x + (self.alpha + 1e-9).reciprocal() * torch.sin(self.alpha * x).pow(2)


def replace_snake(module: nn.Module) -> int:
    """モジュール木の Snake1d を LabSnake1d に差し替える。差し替えた個数を返す。"""
    from dacvae.nn.layers import Snake1d

    n = 0
    for name, child in list(module.named_children()):
        if isinstance(child, Snake1d):
            setattr(module, name, LabSnake1d(child.alpha.data))
            n += 1
        else:
            n += replace_snake(child)
    return n


# ---------------------------------------------------------------- weight_norm fold
def detach_wn_cache(root: nn.Module) -> int:
    """weight_norm が pre-forward フックで作る非 leaf の `.weight` を detach する。

    石: 旧 API `torch.nn.utils.weight_norm` は __init__ 時に
    `setattr(module, 'weight', compute_weight(module))` を通常モードで実行するので、
    `.weight` が grad_fn 付きの非 leaf テンソルになる。この状態で copy.deepcopy すると
        RuntimeError: Only Tensors created explicitly by the user (graph leaves)
        support the deepcopy protocol at the moment.
    が出る（torch/_tensor.py:145）。1 度でも inference_mode の forward を通すと
    leaf に置き換わって通るため、実行順によって出たり出なかったりする。
    """
    n = 0
    for m in root.modules():
        w = getattr(m, "weight", None)
        if isinstance(w, torch.Tensor) and not isinstance(w, nn.Parameter) and w.grad_fn is not None:
            m.weight = w.detach()
            n += 1
    return n


def fold_weight_norm(module: nn.Module) -> tuple[int, int]:
    """weight_g/weight_v を持つ全モジュールに torch.nn.utils.remove_weight_norm を掛ける。

    返り値 = (fold した数, weight_norm を持たなかった Conv の数)
    """
    folded = 0
    plain = 0
    for m in module.modules():
        if isinstance(m, (nn.Conv1d, nn.ConvTranspose1d, nn.Conv2d)):
            if hasattr(m, "weight_g") or hasattr(m, "weight_v"):
                torch.nn.utils.remove_weight_norm(m)
                folded += 1
            else:
                plain += 1
    return folded, plain


# ---------------------------------------------------------------- codec
def load_codec(device: str = "cpu"):
    from irodori_tts.codec import DACVAECodec

    t0 = time.perf_counter()
    codec = DACVAECodec.load(
        repo_id="Aratako/Semantic-DACVAE-Japanese-32dim", device=device
    )
    dt = time.perf_counter() - t0
    return codec, dt


# ---------------------------------------------------------------- 照合
def compare(a: np.ndarray, b: np.ndarray) -> dict:
    a = np.asarray(a, dtype=np.float64).ravel()
    b = np.asarray(b, dtype=np.float64).ravel()
    assert a.shape == b.shape, f"shape mismatch {a.shape} vs {b.shape}"
    d = a - b
    max_abs = float(np.abs(d).max())
    denom = float(np.abs(a).max()) or 1.0
    rel = max_abs / denom
    l2a = float(np.sqrt((a * a).sum()))
    l2d = float(np.sqrt((d * d).sum()))
    snr = float("inf") if l2d == 0 else 20.0 * float(np.log10(l2a / l2d))
    return {
        "max_abs_diff": max_abs,
        "max_abs_ref": denom,
        "rel_max_diff": rel,
        "snr_db": snr,
        "mean_abs_diff": float(np.abs(d).mean()),
    }


def timeit(fn, repeat: int = 3) -> dict:
    ts = []
    out = None
    for _ in range(repeat):
        t0 = time.perf_counter()
        out = fn()
        ts.append(time.perf_counter() - t0)
    return {
        "ms_median": round(statistics.median(ts) * 1000, 2),
        "ms_all": [round(t * 1000, 2) for t in ts],
        "_out": out,
    }


def ort_session(path: Path, threads: int = 8):
    import onnxruntime as ort

    so = ort.SessionOptions()
    so.intra_op_num_threads = threads
    so.inter_op_num_threads = 1
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    return ort.InferenceSession(str(path), so, providers=["CPUExecutionProvider"])


def onnx_size(path: Path) -> dict:
    total = 0
    files = {}
    for p in sorted(path.parent.glob(path.name + "*")):
        files[p.name] = p.stat().st_size
        total += p.stat().st_size
    # 外部データが別名のこともあるので、同ディレクトリの .data も拾う
    for p in sorted(path.parent.glob("*.data")):
        if p.name not in files and p.name.startswith(path.stem):
            files[p.name] = p.stat().st_size
            total += p.stat().st_size
    return {"files": files, "total_bytes": total, "total_mb": round(total / 1024 / 1024, 2)}


def dump(name: str, obj: dict) -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    p = OUT_DIR / name
    p.write_text(json.dumps(obj, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
    print(f"[dump] {p}")
