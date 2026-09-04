"""段 3: DACVAE エンコーダ（deterministic mean 経路）の ONNX export と ORT 照合。

グラフは encoder -> quantizer.in_proj -> chunk 前半（mean）まで。
入力 wav は hop 1920 の倍数へ pad 済みであることを前提にする（pad はグラフ外）。
"""

from __future__ import annotations

import copy
import sys
import time
import traceback
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

sys.path.insert(0, str(Path(__file__).resolve().parent))
from onnx_common import (  # noqa: E402
    ONNX_DIR, OUT_DIR, compare, detach_wn_cache, dump, fold_weight_norm, load_codec,
    onnx_size, ort_session, timeit,
)

REPORT: dict = {"stage": "encoder"}


class EncoderExport(nn.Module):
    """波形 (B,1,T*1920) -> 潜在 mean (B,32,T)。

    upstream 経路（irodori_tts/codec.py:249-251）:
        z = model.encoder(model._pad(waveform))          # _pad はグラフ外
        mean, _scale = model.quantizer.in_proj(z).chunk(2, dim=1)
    """

    def __init__(self, codec_model):
        super().__init__()
        detach_wn_cache(codec_model)  # deepcopy 前に必須
        self.encoder = copy.deepcopy(codec_model.encoder)
        self.in_proj = copy.deepcopy(codec_model.quantizer.in_proj)
        self.latent_dim = int(codec_model.quantizer.codebook_dim)

    def forward(self, wav: torch.Tensor) -> torch.Tensor:
        z = self.encoder(wav)
        h = self.in_proj(z)
        return h[:, : self.latent_dim, :]


def main() -> None:
    import os

    torch.set_num_threads(int(os.environ.get("OMP_NUM_THREADS", "8")))
    ONNX_DIR.mkdir(parents=True, exist_ok=True)

    codec, load_s = load_codec("cpu")
    REPORT["codec_load_s"] = round(load_s, 2)

    import soundfile as sf

    wav_path = OUT_DIR / "steps40_seed1234.wav"
    data, sr = sf.read(str(wav_path), dtype="float32")
    wav = torch.from_numpy(np.asarray(data)).view(1, 1, -1)
    hop = int(codec.model.hop_length)
    n = int(wav.shape[-1])
    REPORT["source_wav"] = {"path": str(wav_path), "sr": sr, "samples": n,
                            "hop": hop, "already_multiple": n % hop == 0}

    # pad はグラフ外（upstream DACVAE._pad は reflect・dacvae/model/dacvae.py:707-713）
    if n % hop:
        wav = torch.nn.functional.pad(wav, (0, hop - (n % hop)), "reflect")
    REPORT["padded_samples"] = int(wav.shape[-1])

    # ---- torch 参照（upstream 経路そのまま）----
    with torch.inference_mode():
        ref_btd = codec.encode_waveform(wav, sr, normalize_db=None).clone()  # (B,T,D)
    ref_bdt = ref_btd.transpose(1, 2).contiguous()
    REPORT["latent_shape_BDT"] = list(ref_bdt.shape)

    wrapper = EncoderExport(codec.model).eval()
    folded, plain = fold_weight_norm(wrapper)
    REPORT["weight_norm"] = {"folded": folded, "plain_conv": plain}
    nparams = sum(p.numel() for p in wrapper.parameters())
    REPORT["encoder_params"] = nparams
    REPORT["encoder_params_fp32_bytes"] = nparams * 4

    with torch.no_grad():
        ref_fold = wrapper(wav).clone()
    REPORT["fold_vs_orig"] = compare(ref_bdt.numpy(), ref_fold.numpy())
    print("[fold vs orig]", REPORT["fold_vs_orig"], flush=True)

    # ---- export ----
    path = ONNX_DIR / "dacvae_encoder_fp32.onnx"
    Bdim = torch.export.Dim("B", min=1, max=8)
    Tdim = torch.export.Dim("T", min=4, max=4096)
    dyn = {"wav": {0: Bdim, 2: hop * Tdim}}
    log = []
    ok_mode = None
    for dynamo in (True, False):
        try:
            t0 = time.perf_counter()
            if dynamo:
                torch.onnx.export(wrapper, (wav,), str(path), input_names=["wav"],
                                  output_names=["latent"], dynamic_shapes=dyn, dynamo=True)
            else:
                torch.onnx.export(wrapper, (wav,), str(path), input_names=["wav"],
                                  output_names=["latent"], dynamo=False, opset_version=17,
                                  dynamic_axes={"wav": {0: "B", 2: "S"},
                                                "latent": {0: "B", 2: "T"}})
            log.append({"dynamo": dynamo, "ok": True, "sec": round(time.perf_counter() - t0, 2)})
            ok_mode = dynamo
            break
        except Exception:
            tb = traceback.format_exc()
            log.append({"dynamo": dynamo, "ok": False, "traceback": tb[-4000:]})
            print(f"[export dynamo={dynamo}] FAILED\n{tb}", flush=True)
    REPORT["export"] = log
    REPORT["export_mode"] = ok_mode
    if ok_mode is None:
        dump("onnx_encoder.json", REPORT)
        raise SystemExit("export failed")

    REPORT["onnx_size"] = onnx_size(path)
    import onnx as _onnx

    m = _onnx.load(str(path), load_external_data=False)
    REPORT["opset"] = {d.domain or "": d.version for d in m.opset_import}
    ops: dict[str, int] = {}
    for nd in m.graph.node:
        ops[nd.op_type] = ops.get(nd.op_type, 0) + 1
    REPORT["op_histogram"] = dict(sorted(ops.items(), key=lambda kv: -kv[1]))
    print("[onnx]", REPORT["onnx_size"], REPORT["opset"], flush=True)

    sess = ort_session(path, threads=8)
    iname = sess.get_inputs()[0].name
    wav_np = wav.numpy()
    out = sess.run(None, {iname: wav_np})[0]
    REPORT["ort_vs_torch"] = compare(ref_fold.numpy(), out)
    print("[ort vs torch]", REPORT["ort_vs_torch"], flush=True)

    # 動的軸: 別長（pad をグラフ外でやった上で）
    short = wav_np[:, :, : hop * 40]
    with torch.no_grad():
        r_s = wrapper(torch.from_numpy(short)).numpy()
    o_s = sess.run(None, {iname: short})[0]
    REPORT["dyn_T40"] = compare(r_s, o_s) | {"samples": short.shape[2]}
    print("[dyn]", REPORT["dyn_T40"], flush=True)

    # 往復（encode->decode）の目安
    dec_path = ONNX_DIR / "dacvae_decoder_fp32.onnx"
    if dec_path.exists():
        dsess = ort_session(dec_path, threads=8)
        rec = dsess.run(None, {dsess.get_inputs()[0].name: out})[0]
        REPORT["roundtrip_vs_input_wav"] = compare(wav_np, rec)
        print("[roundtrip]", REPORT["roundtrip_vs_input_wav"], flush=True)

    # ---- 所要 ----
    audio_s = wav.shape[-1] / 48000.0
    with torch.inference_mode():
        _ = codec.encode_waveform(wav, sr, normalize_db=None)
        t_torch = timeit(lambda: codec.encode_waveform(wav, sr, normalize_db=None), 3)
        t_torch.pop("_out")
        _ = wrapper(wav)
        t_fold = timeit(lambda: wrapper(wav), 3)
        t_fold.pop("_out")
    _ = sess.run(None, {iname: wav_np})
    t_ort = timeit(lambda: sess.run(None, {iname: wav_np}), 3)
    t_ort.pop("_out")
    REPORT["timing"] = {
        "audio_seconds": round(audio_s, 3),
        "torch_encode_waveform": t_torch,
        "torch_folded_wrapper": t_fold,
        "ort_cpu": t_ort,
        "speedup_ort_vs_torch": round(t_torch["ms_median"] / t_ort["ms_median"], 2),
    }
    print("[timing]", REPORT["timing"], flush=True)

    dump("onnx_encoder.json", REPORT)


if __name__ == "__main__":
    main()
