"""段2: 全ループ照合。rf.sample_euler_rf_cfg（torch）と、同じ式を lab 側で写経して
ステップ関数だけ ORT に差し替えたループを突き合わせる。最後に torch コーデックで復号して wav も比較。"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402

MODEL = C.ONNX_DIR / "dit_step.onnx"
CFG_TEXT = 3.0
CFG_CAPTION = 3.0
CFG_SPEAKER = 0.0  # no_ref なので inference_runtime.py:1130+ / :335-341 で 0 に落ちる
CFG_MIN_T, CFG_MAX_T = 0.5, 1.0


def t_schedule(num_steps: int) -> torch.Tensor:
    u = torch.linspace(0.0, 1.0, num_steps + 1)
    return (1.0 - u) * 0.999


def ort_loop(sess, cond, x0, num_steps):
    """rf.py:189-208 / :459-582 の写経。ステップ関数だけ ORT。"""
    ts = cond["text_state"].numpy()
    tm = cond["text_mask"].numpy()
    ss = cond["speaker_state"].numpy()
    sm = cond["speaker_mask"].numpy()
    cs = cond["caption_state"].numpy()
    cm = cond["caption_mask"].numpy()
    ts_u = np.zeros_like(ts)
    tm_u = np.zeros_like(tm)
    cs_u = np.zeros_like(cs)
    cm_u = np.zeros_like(cm)
    # independent_names = ["cond", "text", "caption"]（speaker は cfg_scale 0 で無効）
    TS = np.concatenate([ts, ts_u, ts], 0)
    TM = np.concatenate([tm, tm_u, tm], 0)
    SS = np.concatenate([ss, ss, ss], 0)
    SM = np.concatenate([sm, sm, sm], 0)
    CS = np.concatenate([cs, cs, cs_u], 0)
    CM = np.concatenate([cm, cm, cm_u], 0)
    scales = [CFG_TEXT, CFG_CAPTION]

    sched = t_schedule(num_steps).numpy()
    x = x0.numpy().copy()
    n_cfg = 0
    for i in range(num_steps):
        t = float(sched[i])
        t_next = float(sched[i + 1])
        if CFG_MIN_T <= t <= CFG_MAX_T:
            n_cfg += 1
            feeds = {
                "x_t": np.ascontiguousarray(np.concatenate([x] * 3, 0)),
                "t": np.full((3,), t, dtype=np.float32),
                "text_state": TS,
                "text_mask": TM,
                "speaker_state": SS,
                "speaker_mask": SM,
                "caption_state": CS,
                "caption_mask": CM,
            }
            v_out = sess.run(["v"], feeds)[0]
            c0, c1, c2 = v_out[0:1], v_out[1:2], v_out[2:3]
            v = c0 + scales[0] * (c0 - c1) + scales[1] * (c0 - c2)
        else:
            feeds = {
                "x_t": np.ascontiguousarray(x),
                "t": np.full((1,), t, dtype=np.float32),
                "text_state": ts,
                "text_mask": tm,
                "speaker_state": ss,
                "speaker_mask": sm,
                "caption_state": cs,
                "caption_mask": cm,
            }
            v = sess.run(["v"], feeds)[0]
        x = x + v * (t_next - t)
    return x, n_cfg


def main():
    torch.set_num_threads(8)
    rep = {}
    rt, _ = C.load_runtime()
    C.install_real_rope()
    rt.model._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    rt.model.speaker_encoder._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    C.bake_rope_caches(rt.model)
    cond = C.build_conditions(rt)
    S = int(cond["latent_steps"])
    rep["latent_steps"] = S

    from irodori_tts.rf import sample_euler_rf_cfg

    so = ort.SessionOptions()
    so.intra_op_num_threads = 8
    so.inter_op_num_threads = 1
    sess = ort.InferenceSession(str(MODEL), so, providers=["CPUExecutionProvider"])

    g = torch.Generator(device="cpu").manual_seed(C.SEED)
    x0 = torch.randn((1, S, 32), generator=g, dtype=torch.float32)

    for num_steps in (40, 10):
        tag = f"steps{num_steps}"
        # --- torch 基準（上流のループ・use_context_kv_cache=True＝上流既定）---
        t0 = time.perf_counter()
        with torch.inference_mode():
            z_t = sample_euler_rf_cfg(
                model=rt.model,
                text_input_ids=cond["text_ids"],
                text_mask=cond["text_mask_in"],
                ref_latent=cond["ref_latent"],
                ref_mask=cond["ref_mask_in"],
                sequence_length=S,
                caption_input_ids=cond["cap_ids"],
                caption_mask=cond["cap_mask_in"],
                num_steps=num_steps,
                cfg_scale_text=CFG_TEXT,
                cfg_scale_caption=CFG_CAPTION,
                cfg_scale_speaker=CFG_SPEAKER,
                cfg_guidance_mode="independent",
                cfg_min_t=CFG_MIN_T,
                cfg_max_t=CFG_MAX_T,
                seed=C.SEED,
                use_context_kv_cache=True,
            )
        torch_sec = time.perf_counter() - t0
        # --- torch（KV キャッシュ無し＝ONNX と同じ形）---
        t0 = time.perf_counter()
        with torch.inference_mode():
            z_t_nokv = sample_euler_rf_cfg(
                model=rt.model,
                text_input_ids=cond["text_ids"],
                text_mask=cond["text_mask_in"],
                ref_latent=cond["ref_latent"],
                ref_mask=cond["ref_mask_in"],
                sequence_length=S,
                caption_input_ids=cond["cap_ids"],
                caption_mask=cond["cap_mask_in"],
                num_steps=num_steps,
                cfg_scale_text=CFG_TEXT,
                cfg_scale_caption=CFG_CAPTION,
                cfg_scale_speaker=CFG_SPEAKER,
                cfg_guidance_mode="independent",
                cfg_min_t=CFG_MIN_T,
                cfg_max_t=CFG_MAX_T,
                seed=C.SEED,
                use_context_kv_cache=False,
            )
        torch_nokv_sec = time.perf_counter() - t0
        # --- ORT ループ ---
        t0 = time.perf_counter()
        z_o, n_cfg = ort_loop(sess, cond, x0, num_steps)
        ort_sec = time.perf_counter() - t0

        rep[tag] = {
            "n_cfg_steps": n_cfg,
            "torch_kv_sec": round(torch_sec, 3),
            "torch_nokv_sec": round(torch_nokv_sec, 3),
            "ort_sec": round(ort_sec, 3),
            "latent_diff_ort_vs_torchkv": C.diffs(z_t.numpy(), z_o),
            "latent_diff_ort_vs_torchnokv": C.diffs(z_t_nokv.numpy(), z_o),
            "latent_diff_torchkv_vs_torchnokv": C.diffs(z_t.numpy(), z_t_nokv.numpy()),
            "latent_absmax": float(np.abs(z_t.numpy()).max()),
        }

        # --- wav 復号（torch コーデック）---
        with torch.inference_mode():
            a_t = rt.codec.decode_latent(z_t).cpu()[0]
            a_o = rt.codec.decode_latent(torch.from_numpy(z_o)).cpu()[0]
        n = min(a_t.shape[-1], a_o.shape[-1])
        rep[tag]["wav_diff"] = C.diffs(a_t.numpy()[:, :n], a_o.numpy()[:, :n])
        rep[tag]["wav_absmax_torch"] = float(np.abs(a_t.numpy()).max())
        rep[tag]["wav_samples"] = int(n)

        import soundfile as sf

        sf.write(
            str(C.OUT / f"13_{tag}_torch.wav"), a_t.numpy().T, int(rt.codec.sample_rate)
        )
        sf.write(str(C.OUT / f"13_{tag}_ort.wav"), a_o.numpy().T, int(rt.codec.sample_rate))
        print(json.dumps({tag: rep[tag]}, ensure_ascii=False, indent=2), flush=True)

    C.jdump(rep, C.OUT / "13_loop.json")


if __name__ == "__main__":
    main()
