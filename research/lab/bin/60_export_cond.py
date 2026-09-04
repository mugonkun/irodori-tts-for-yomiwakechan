"""段4: 条件符号化（ModernBERT 骨格＋射影器 2 本＋ReferenceLatentEncoder）の export 試行。
attn_implementation を sdpa / eager で試し、落ちたら逐語を残して部分（射影器・参照 encoder）だけ出す。"""

from __future__ import annotations

import json
import sys
import time
import traceback
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import torch

sys.path.insert(0, str(Path(__file__).parent))
import dit_common as C  # noqa: E402


class EncodeAll(torch.nn.Module):
    """encode_conditions 丸ごと（text/caption 骨格＋射影器＋参照 encoder）。"""

    def __init__(self, model):
        super().__init__()
        self.m = model

    def forward(self, text_ids, text_mask, cap_ids, cap_mask, ref_latent, ref_mask):
        ts, tm, ss, sm, cs, cm = self.m.encode_conditions(
            text_input_ids=text_ids,
            text_mask=text_mask,
            ref_latent=ref_latent,
            ref_mask=ref_mask,
            caption_input_ids=cap_ids,
            caption_mask=cap_mask,
        )
        return ts, ss, sm, cs


class TextBranch(torch.nn.Module):
    """ModernBERT 骨格 ＋ text 射影器 ＋ text_norm（caption 側も同じ形）。"""

    def __init__(self, model, which="text"):
        super().__init__()
        self.m = model
        self.which = which

    def forward(self, ids, mask):
        enc = self.m.text_encoder if self.which == "text" else self.m.caption_encoder
        norm = self.m.text_norm if self.which == "text" else self.m.caption_norm
        return norm(enc(self.m.pretrained_text_backbone, ids, mask))


class SpeakerBranch(torch.nn.Module):
    """参照潜在（patch 済み）→ ReferenceLatentEncoder → speaker_norm → 平均トークン前置。"""

    def __init__(self, model):
        super().__init__()
        self.m = model

    def forward(self, ref_patched, ref_mask):
        st = self.m.speaker_encoder(ref_patched, ref_mask)
        st = self.m.speaker_norm(st)
        st, mk = self.m._prepend_masked_mean_token(st, ref_mask)
        return st, mk


def try_export(mod, args, path, dyn, input_names, output_names, rep, key):
    t0 = time.perf_counter()
    try:
        with torch.no_grad():
            torch.onnx.export(
                mod,
                args,
                str(path),
                dynamo=True,
                dynamic_shapes=dyn,
                input_names=input_names,
                output_names=output_names,
                external_data=True,
                optimize=True,
            )
        rep[key] = {"status": "OK", "sec": round(time.perf_counter() - t0, 2)}
        files = sorted(path.parent.glob(path.stem + "*"))
        rep[key]["files"] = {f.name: f.stat().st_size for f in files}
        rep[key]["total_bytes"] = sum(f.stat().st_size for f in files)
        return True
    except Exception:
        tb = traceback.format_exc()
        rep[key] = {"status": "FAIL", "sec": round(time.perf_counter() - t0, 2),
                    "error_tail": tb[-2500:]}
        print(f"[{key}] FAILED\n" + tb[-2500:], flush=True)
        return False


def main():
    torch.set_num_threads(8)
    rep = {}
    rt, _ = C.load_runtime()
    C.install_real_rope()
    C.install_export_safe_mask()
    rt.model._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    rt.model.speaker_encoder._freqs_cis_cache = torch.empty(0, 0, dtype=torch.complex64)
    C.bake_rope_caches(rt.model)
    cond = C.build_conditions(rt)

    bb = rt.model.pretrained_text_backbone.backbone
    rep["backbone_class"] = type(bb).__name__
    rep["attn_impl_before"] = str(getattr(bb.config, "_attn_implementation", None))

    args_all = (
        cond["text_ids"],
        cond["text_mask_in"],
        cond["cap_ids"],
        cond["cap_mask_in"],
        cond["ref_latent"],
        cond["ref_mask_in"],
    )
    names_all = ["text_ids", "text_mask", "cap_ids", "cap_mask", "ref_latent", "ref_mask"]
    outs_all = ["text_state", "speaker_state", "speaker_mask", "caption_state"]
    Bd = torch.export.Dim("B", min=1, max=8)
    dyn_all = {
        "text_ids": {0: Bd},
        "text_mask": {0: Bd},
        "cap_ids": {0: Bd},
        "cap_mask": {0: Bd},
        "ref_latent": {0: Bd},
        "ref_mask": {0: Bd},
    }
    mod_all = EncodeAll(rt.model).eval()
    with torch.inference_mode():
        ref_all = [t.clone() for t in mod_all(*args_all)]

    ok = False
    for impl in ("sdpa", "eager"):
        try:
            bb.set_attn_implementation(impl)
            rep[f"set_attn_{impl}"] = "OK"
        except Exception as e:
            rep[f"set_attn_{impl}"] = f"FAIL: {e!r}"
            try:
                bb.config._attn_implementation = impl
                rep[f"set_attn_{impl}"] += " / config fallback set"
            except Exception as e2:
                rep[f"set_attn_{impl}"] += f" / config fallback FAIL: {e2!r}"
        with torch.inference_mode():
            chk = mod_all(*args_all)
        rep[f"attn_{impl}_torch_diff"] = C.diffs(ref_all[0].numpy(), chk[0].numpy())
        p = C.ONNX_DIR / f"encode_conditions_{impl}.onnx"
        if try_export(mod_all, args_all, p, dyn_all, names_all, outs_all, rep, f"export_all_{impl}"):
            # ORT 照合
            try:
                so = ort.SessionOptions()
                so.intra_op_num_threads = 8
                sess = ort.InferenceSession(str(p), so, providers=["CPUExecutionProvider"])
                feeds = {n: np.ascontiguousarray(a.numpy()) for n, a in zip(names_all, args_all)}
                o = sess.run(outs_all, feeds)
                rep[f"ort_all_{impl}"] = {
                    outs_all[i]: C.diffs(ref_all[i].numpy(), o[i]) for i in range(4)
                }
                ts = []
                for _ in range(2):
                    sess.run(outs_all, feeds)
                for _ in range(5):
                    t1 = time.perf_counter()
                    sess.run(outs_all, feeds)
                    ts.append(time.perf_counter() - t1)
                rep[f"ort_all_{impl}_sec_median"] = round(float(np.median(ts)), 4)
                m = onnx.load(str(p), load_external_data=False)
                h = {}
                for n in m.graph.node:
                    h[n.op_type] = h.get(n.op_type, 0) + 1
                rep[f"ops_all_{impl}"] = dict(sorted(h.items(), key=lambda kv: -kv[1]))
                rep[f"nodes_all_{impl}"] = len(m.graph.node)
                del m
                ok = True
            except Exception:
                rep[f"ort_all_{impl}_error"] = traceback.format_exc()[-2500:]
                print(rep[f"ort_all_{impl}_error"], flush=True)
        if ok:
            break

    # --- 参照 encoder 単体（骨格が駄目でもここは出せるか） ---
    from irodori_tts.model import patch_sequence_with_mask

    g = torch.Generator().manual_seed(99)
    fake_ref = torch.randn((1, 40, 32), generator=g, dtype=torch.float32)
    fake_mask = torch.ones((1, 40), dtype=torch.bool)
    with torch.inference_mode():
        ref_patched, ref_mask_p = patch_sequence_with_mask(
            seq=fake_ref, mask=fake_mask,
            patch_size=rt.model_cfg.speaker_patch_size,
        )
    spk = SpeakerBranch(rt.model).eval()
    with torch.inference_mode():
        spk_ref = [t.clone() for t in spk(ref_patched, ref_mask_p)]
    Kd = torch.export.Dim("S_ref", min=2, max=1024)
    p2 = C.ONNX_DIR / "speaker_encoder.onnx"
    if try_export(
        spk,
        (ref_patched, ref_mask_p),
        p2,
        {"ref_patched": {0: Bd, 1: Kd}, "ref_mask": {0: Bd, 1: Kd}},
        ["ref_patched", "ref_mask"],
        ["speaker_state", "speaker_mask"],
        rep,
        "export_speaker_encoder",
    ):
        try:
            so = ort.SessionOptions()
            so.intra_op_num_threads = 8
            s2 = ort.InferenceSession(str(p2), so, providers=["CPUExecutionProvider"])
            # S_ref=1 は Dim(min=2) の外なので 8 フレームでも検証
            o2 = s2.run(
                ["speaker_state", "speaker_mask"],
                {
                    "ref_patched": np.ascontiguousarray(ref_patched.numpy()),
                    "ref_mask": np.ascontiguousarray(ref_mask_p.numpy()),
                },
            )
            rep["ort_speaker_encoder"] = C.diffs(spk_ref[0].numpy(), o2[0])
        except Exception:
            rep["ort_speaker_encoder_error"] = traceback.format_exc()[-2500:]
            print(rep["ort_speaker_encoder_error"], flush=True)

    C.jdump(rep, C.OUT / "13_cond.json")
    small = {k: v for k, v in rep.items() if not k.endswith("error") and k != "ops_all_sdpa"}
    print(json.dumps(small, ensure_ascii=False, indent=2)[:6000])


if __name__ == "__main__":
    main()
