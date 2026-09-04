"""共通ヘルパ（第2便・ONNX DiT 実験）。upstream は 1 バイトも触らない＝monkeypatch のみ。

- real_rope_patch(): irodori_tts.model の複素 RoPE を実数(cos/sin)版へ差し替える
- build_conditions(): 基準条件（no_ref・caption・本文）を torch 側で作って npz に保存/復元
"""

from __future__ import annotations

import json
import os
import time
from pathlib import Path

import numpy as np
import torch

LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
OUT = LAB / "out"
ONNX_DIR = LAB / "onnx"
TMP = LAB / "tmp"

# 基準条件（11-lab-setup.md §5 と同じ）
TEXT = "こんにちは、読み分けちゃんのテストです。"
CAPTION = "落ち着いた女性の声で、丁寧に話す。"
CKPT_REPO = "Aratako/Irodori-TTS-v4.1-Small"
SEED = 1234


# --------------------------------------------------------------------------
# RoPE 実数化 monkeypatch
# --------------------------------------------------------------------------
def real_precompute_freqs_cis(dim: int, end: int, theta: float = 10000.0) -> torch.Tensor:
    """複素版と同じ (cos, sin) を最終軸 2 の実数テンソルで返す: (end, dim//2, 2)."""
    freqs = 1.0 / (theta ** (torch.arange(0, dim, 2, dtype=torch.float32) / dim))
    t = torch.arange(end, dtype=torch.float32)
    freqs = torch.outer(t, freqs)
    return torch.stack([torch.cos(freqs), torch.sin(freqs)], dim=-1)


def real_apply_rotary_emb(x: torch.Tensor, freqs_cis: torch.Tensor) -> torch.Tensor:
    """x: (B,S,H,Dh) / freqs_cis: (S, Dh/2, 2)。複素版と数学的に等価。"""
    xf = x.float().reshape(x.shape[0], x.shape[1], x.shape[2], -1, 2)
    x0 = xf[..., 0]
    x1 = xf[..., 1]
    cos = freqs_cis[..., 0][None, :, None, :]
    sin = freqs_cis[..., 1][None, :, None, :]
    o0 = x0 * cos - x1 * sin
    o1 = x0 * sin + x1 * cos
    out = torch.stack([o0, o1], dim=-1).reshape(x.shape)
    return out.type_as(x)


def install_real_rope() -> None:
    import irodori_tts.model as M

    M.precompute_freqs_cis = real_precompute_freqs_cis
    M.apply_rotary_emb = real_apply_rotary_emb


def export_safe_attention_mask(x: torch.Tensor, mask: torch.Tensor):
    """model.py:433-449 の `_safe_attention_mask` をデータ依存分岐なしに書き直した版。
    元は `if bool(has_any.all()):` で早期 return する＝torch.export が
    GuardOnDataDependentSymNode で落ちる。torch.where で同値に置き換える。"""
    if mask.ndim != 2:
        raise ValueError(f"mask must be (B,S), got {tuple(mask.shape)}")
    mask = mask.to(device=x.device, dtype=torch.bool)
    has_any = mask.any(dim=1)
    # ORT CPU は Where(16) の bool 入力を実装していないので Mul / Or・And・Not で書く
    x = x * has_any[:, None, None].to(dtype=x.dtype)
    idx = torch.arange(mask.shape[1], device=mask.device)
    first_only = (idx == 0)[None, :].expand(mask.shape[0], -1)
    mask = torch.logical_or(mask, torch.logical_and(first_only, torch.logical_not(has_any)[:, None]))
    return x, mask


def install_export_safe_mask() -> None:
    import irodori_tts.model as M

    M._safe_attention_mask = export_safe_attention_mask


def bake_rope_caches(model, max_lat: int = 1024, max_spk: int = 4096) -> None:
    """export 前に RoPE テーブルを十分な長さで焼く（forward 内の再構築分岐を通らせない）。"""
    dev = model.device
    model._rope_freqs(max_lat, dev)
    if getattr(model, "speaker_encoder", None) is not None:
        model.speaker_encoder._rope_freqs(max_spk, dev)


# --------------------------------------------------------------------------
# 条件（text/speaker/caption state）の生成
# --------------------------------------------------------------------------
def load_runtime():
    from irodori_tts.inference_runtime import (
        InferenceRuntime,
        RuntimeKey,
        download_hf_checkpoint,
    )

    t0 = time.perf_counter()
    ckpt = download_hf_checkpoint(CKPT_REPO)
    rt = InferenceRuntime.from_key(
        RuntimeKey(checkpoint=ckpt, model_device="cpu", codec_device="cpu")
    )
    return rt, time.perf_counter() - t0


def build_conditions(rt, text: str = TEXT, caption: str = CAPTION):
    """no_ref・caption つきの条件一式と duration 予測を torch で作る。"""
    import math

    from irodori_tts.duration import build_duration_features
    from irodori_tts.text_normalization import normalize_text

    dev = rt.model_device
    dtype = next(rt.model.parameters()).dtype
    ntext = normalize_text(text).strip()
    text_ids, text_mask = rt.tokenizer.batch_encode([ntext], max_length=rt.default_text_max_len)
    text_ids = text_ids.to(dev)
    text_mask = text_mask.to(dev)
    cap_ids, cap_mask = rt.caption_tokenizer.batch_encode(
        [caption], max_length=rt.default_caption_max_len
    )
    cap_ids = cap_ids.to(dev)
    cap_mask = cap_mask.to(dev)

    # no_ref 経路（inference_runtime.py:872-887 と同じ）
    ref_len = max(1, int(rt.model_cfg.speaker_patch_size))
    ref_latent = torch.zeros(
        (1, ref_len, rt.model_cfg.latent_dim * rt.model_cfg.latent_patch_size),
        device=dev,
        dtype=dtype,
    )
    ref_mask = torch.zeros((1, ref_len), dtype=torch.bool, device=dev)

    with torch.inference_mode():
        (ts, tm, ss, sm, cs, cm) = rt.model.encode_conditions(
            text_input_ids=text_ids,
            text_mask=text_mask,
            ref_latent=ref_latent,
            ref_mask=ref_mask,
            caption_input_ids=cap_ids,
            caption_mask=cap_mask,
        )
        has_speaker = ref_mask.any(dim=1)
        feats = build_duration_features(
            [ntext],
            token_counts=text_mask.sum(dim=1),
            max_text_len=rt.default_text_max_len,
            has_speaker=has_speaker,
        ).to(dev)
        pred_log = rt.model.predict_duration_log_frames(
            text_state=ts,
            text_mask=tm,
            speaker_state=ss,
            speaker_mask=sm,
            caption_state=cs,
            caption_mask=cm,
            duration_features=feats,
            has_speaker=has_speaker,
            has_caption=torch.full((1,), True, dtype=torch.bool, device=dev),
        )
    pred_frames = torch.expm1(pred_log).float().mean().item()
    hop = int(rt.codec.model.hop_length)
    min_frames = max(1, math.ceil(0.5 * rt.codec.sample_rate / hop))
    max_frames = max(1, math.floor(30.0 * rt.codec.sample_rate / hop))
    latent_steps = int(round(pred_frames))
    latent_steps = max(min_frames, min(max_frames, latent_steps))

    return dict(
        text_ids=text_ids,
        text_mask_in=text_mask,
        cap_ids=cap_ids,
        cap_mask_in=cap_mask,
        ref_latent=ref_latent,
        ref_mask_in=ref_mask,
        text_state=ts,
        text_mask=tm,
        speaker_state=ss,
        speaker_mask=sm,
        caption_state=cs,
        caption_mask=cm,
        duration_features=feats,
        has_speaker=has_speaker,
        pred_log_frames=pred_log,
        pred_frames=pred_frames,
        latent_steps=latent_steps,
    )


def save_conditions(cond: dict, path: Path) -> None:
    arr = {}
    for k, v in cond.items():
        if isinstance(v, torch.Tensor):
            arr[k] = v.detach().cpu().numpy()
        else:
            arr[k] = np.array(v)
    np.savez(path, **arr)


def load_conditions(path: Path) -> dict:
    z = np.load(path)
    return {k: torch.from_numpy(z[k]) if z[k].ndim > 0 else z[k].item() for k in z.files}


# --------------------------------------------------------------------------
# DiT 1 ステップのラッパ（use_context_kv_cache=False 相当＝state を渡す）
# --------------------------------------------------------------------------
class DitStep(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.m = model

    def forward(
        self,
        x_t,
        t,
        text_state,
        text_mask,
        speaker_state,
        speaker_mask,
        caption_state,
        caption_mask,
    ):
        return self.m.forward_with_encoded_conditions(
            x_t=x_t,
            t=t,
            text_state=text_state,
            text_mask=text_mask,
            speaker_state=speaker_state,
            speaker_mask=speaker_mask,
            caption_state=caption_state,
            caption_mask=caption_mask,
            latent_mask=None,
            context_kv_cache=None,
        )


def diffs(a: np.ndarray, b: np.ndarray) -> dict:
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    d = np.abs(a - b)
    den = np.linalg.norm(a)
    return dict(
        max_abs=float(d.max()),
        mean_abs=float(d.mean()),
        rel_l2=float(np.linalg.norm(a - b) / den) if den > 0 else float("nan"),
        corr=float(np.corrcoef(a.ravel(), b.ravel())[0, 1]),
    )


def jdump(obj, path: Path) -> None:
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2), encoding="utf-8")
