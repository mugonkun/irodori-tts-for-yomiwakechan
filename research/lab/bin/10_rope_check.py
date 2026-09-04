"""段1a: 複素 RoPE → 実数 RoPE の monkeypatch 前後で torch 出力が一致するかを確認し、
基準条件（text/speaker/caption state・duration）を npz に保存する。"""

from __future__ import annotations

import sys
import time
from pathlib import Path

import numpy as np
import torch

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402


def reset_rope_caches(model):
    model._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    for name in ("speaker_encoder", "text_encoder", "caption_encoder"):
        mod = getattr(model, name, None)
        if mod is not None and hasattr(mod, "_freqs_cis_cache"):
            mod._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)


def main():
    torch.set_num_threads(8)
    rep = {}
    rt, load_s = C.load_runtime()
    rep["load_sec"] = round(load_s, 3)
    print(f"[load] {load_s:.2f}s", flush=True)

    # ---- 1) 複素版（上流のまま）で条件と 1 ステップ出力 ----
    cond0 = C.build_conditions(rt)
    S = int(cond0["latent_steps"])
    rep["latent_steps"] = S
    rep["pred_frames"] = round(float(cond0["pred_frames"]), 4)
    print(f"[cond] latent_steps={S} pred_frames={cond0['pred_frames']:.3f}", flush=True)
    print(
        "[cond] shapes: text=%s spk=%s cap=%s"
        % (
            tuple(cond0["text_state"].shape),
            tuple(cond0["speaker_state"].shape),
            tuple(cond0["caption_state"].shape),
        ),
        flush=True,
    )

    g = torch.Generator(device="cpu").manual_seed(C.SEED)
    x0 = torch.randn((1, S, 32), generator=g, dtype=torch.float32)
    tt = torch.full((1,), 0.999, dtype=torch.float32)

    step = C.DitStep(rt.model).eval()
    with torch.inference_mode():
        v_cplx = step(
            x0,
            tt,
            cond0["text_state"],
            cond0["text_mask"],
            cond0["speaker_state"],
            cond0["speaker_mask"],
            cond0["caption_state"],
            cond0["caption_mask"],
        ).clone()
        # CFG バッチ 4 の形でも 1 本測る
        x4 = torch.cat([x0] * 4, dim=0)
        t4 = tt.repeat(4)
        ts4 = torch.cat([cond0["text_state"]] * 4, 0)
        tm4 = torch.cat([cond0["text_mask"]] * 4, 0)
        ss4 = torch.cat([cond0["speaker_state"]] * 4, 0)
        sm4 = torch.cat([cond0["speaker_mask"]] * 4, 0)
        cs4 = torch.cat([cond0["caption_state"]] * 4, 0)
        cm4 = torch.cat([cond0["caption_mask"]] * 4, 0)
        v_cplx4 = step(x4, t4, ts4, tm4, ss4, sm4, cs4, cm4).clone()

    # ---- 2) 実数版へ patch ----
    C.install_real_rope()
    reset_rope_caches(rt.model)
    C.bake_rope_caches(rt.model, max_lat=1024, max_spk=4096)
    print(
        "[patch] dit cache=%s spk cache=%s"
        % (
            tuple(rt.model._freqs_cis_cache.shape),
            tuple(rt.model.speaker_encoder._freqs_cis_cache.shape),
        ),
        flush=True,
    )

    cond1 = C.build_conditions(rt)
    with torch.inference_mode():
        v_real = step(
            x0,
            tt,
            cond1["text_state"],
            cond1["text_mask"],
            cond1["speaker_state"],
            cond1["speaker_mask"],
            cond1["caption_state"],
            cond1["caption_mask"],
        ).clone()
        v_real4 = step(x4, t4, ts4, tm4, ss4, sm4, cs4, cm4).clone()

    rep["cond_diff"] = {
        k: C.diffs(cond0[k].numpy(), cond1[k].numpy())
        for k in ("text_state", "speaker_state", "caption_state")
    }
    rep["duration_diff"] = C.diffs(
        cond0["pred_log_frames"].numpy(), cond1["pred_log_frames"].numpy()
    )
    rep["v_diff_b1"] = C.diffs(v_cplx.numpy(), v_real.numpy())
    rep["v_diff_b4_sameinput"] = C.diffs(v_cplx4.numpy(), v_real4.numpy())
    rep["v_stats"] = dict(
        absmax=float(np.abs(v_cplx.numpy()).max()), std=float(v_cplx.numpy().std())
    )

    # ---- 3) 所要（torch 1 ステップ・CFG バッチ 4・中央値） ----
    times = []
    with torch.inference_mode():
        for _ in range(3):
            step(x4, t4, ts4, tm4, ss4, sm4, cs4, cm4)
        for _ in range(7):
            t1 = time.perf_counter()
            step(x4, t4, ts4, tm4, ss4, sm4, cs4, cm4)
            times.append(time.perf_counter() - t1)
    rep["torch_step_b4_sec"] = dict(
        median=round(float(np.median(times)), 4),
        min=round(float(np.min(times)), 4),
        n=len(times),
    )
    times1 = []
    with torch.inference_mode():
        for _ in range(2):
            step(
                x0,
                tt,
                cond1["text_state"],
                cond1["text_mask"],
                cond1["speaker_state"],
                cond1["speaker_mask"],
                cond1["caption_state"],
                cond1["caption_mask"],
            )
        for _ in range(5):
            t1 = time.perf_counter()
            step(
                x0,
                tt,
                cond1["text_state"],
                cond1["text_mask"],
                cond1["speaker_state"],
                cond1["speaker_mask"],
                cond1["caption_state"],
                cond1["caption_mask"],
            )
            times1.append(time.perf_counter() - t1)
    rep["torch_step_b1_sec"] = dict(median=round(float(np.median(times1)), 4), n=len(times1))

    # ---- 4) 条件と初期ノイズを保存（以降の段で再利用） ----
    C.save_conditions(cond1, C.TMP / "cond_real.npz")
    np.savez(C.TMP / "x0.npz", x0=x0.numpy(), v_ref_b1=v_real.numpy(), v_ref_b4=v_real4.numpy())

    C.jdump(rep, C.OUT / "13_rope_check.json")
    print(__import__("json").dumps(rep, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
