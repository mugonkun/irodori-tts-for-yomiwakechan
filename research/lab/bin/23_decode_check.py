"""23 便・段2の照合側（torch）: 40_loop_dml.py が吐いた最終潜在 npz を lab/.venv の torch
コーデックで復号し、13 §6 と同じ torch 基準（同一 x0・同一条件）と突き合わせる。

  "$PY" 23_decode_check.py            # lab/out/23_latent_*.npz を総当たり
"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np
import soundfile as sf
import torch

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402

CFG_TEXT = 3.0
CFG_CAPTION = 3.0
CFG_SPEAKER = 0.0
CFG_MIN_T, CFG_MAX_T = 0.5, 1.0


def int16_diff(pa: Path, pb: Path) -> dict:
    ia, _ = sf.read(str(pa), dtype="int16")
    ib, _ = sf.read(str(pb), dtype="int16")
    n = min(len(ia), len(ib))
    d = np.abs(ia[:n].astype("i4") - ib[:n].astype("i4"))
    return {"max_lsb": int(d.max()), "n_diff": int((d > 0).sum()), "n": int(d.size)}


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

    x0 = torch.from_numpy(np.load(C.TMP / "x0.npz")["x0"])
    sr = int(rt.codec.sample_rate)

    refs = {}
    for num_steps in (40, 10):
        t0 = time.perf_counter()
        with torch.inference_mode():
            z_t = sample_euler_rf_cfg(
                model=rt.model,
                text_input_ids=cond["text_ids"], text_mask=cond["text_mask_in"],
                ref_latent=cond["ref_latent"], ref_mask=cond["ref_mask_in"],
                sequence_length=S,
                caption_input_ids=cond["cap_ids"], caption_mask=cond["cap_mask_in"],
                num_steps=num_steps,
                cfg_scale_text=CFG_TEXT, cfg_scale_caption=CFG_CAPTION,
                cfg_scale_speaker=CFG_SPEAKER, cfg_guidance_mode="independent",
                cfg_min_t=CFG_MIN_T, cfg_max_t=CFG_MAX_T,
                seed=C.SEED, use_context_kv_cache=True,
            )
            a_t = rt.codec.decode_latent(z_t).cpu()[0]
        wav_path = C.OUT / f"23_torch_s{num_steps}.wav"
        sf.write(str(wav_path), a_t.numpy().T, sr)
        refs[num_steps] = {"z": z_t.numpy(), "a": a_t.numpy(), "wav": wav_path}
        rep[f"torch_s{num_steps}_sec"] = round(time.perf_counter() - t0, 3)
        rep[f"torch_s{num_steps}_latent_absmax"] = float(np.abs(z_t.numpy()).max())
        rep[f"torch_s{num_steps}_wav_absmax"] = float(np.abs(a_t.numpy()).max())

    rep["cases"] = {}
    for p in sorted(C.OUT.glob("23_latent_*.npz")):
        tag = p.stem[len("23_latent_"):]
        num_steps = 10 if "_s10" in tag else 40
        z = np.load(p)["latent"]
        with torch.inference_mode():
            a = rt.codec.decode_latent(torch.from_numpy(z)).cpu()[0]
        wav_path = C.OUT / f"23_{tag}.wav"
        sf.write(str(wav_path), a.numpy().T, sr)
        ref = refs[num_steps]
        n = min(a.shape[-1], ref["a"].shape[-1])
        rec = {
            "steps": num_steps,
            "latent_diff_vs_torch": C.diffs(ref["z"], z),
            "wav_diff_vs_torch": C.diffs(ref["a"][:, :n], a.numpy()[:, :n]),
            "latent_absmax": float(np.abs(z).max()),
            "wav_absmax": float(np.abs(a.numpy()).max()),
            "int16_vs_torch": int16_diff(ref["wav"], wav_path),
            "wav": wav_path.name,
        }
        rep["cases"][tag] = rec
        print(json.dumps({tag: rec}, ensure_ascii=False), flush=True)

    C.jdump(rep, C.OUT / "23_decode_check.json")
    print("[23] wrote 23_decode_check.json")


if __name__ == "__main__":
    main()
