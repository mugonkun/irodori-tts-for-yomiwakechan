"""段 1・2: DACVAE デコーダの ONNX export と ORT 照合。

再現:
  PYTHONPATH=<upstream/Irodori-TTS> PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1 \
  PYTHONIOENCODING=utf-8 PYTHONUTF8=1 OMP_NUM_THREADS=8 MKL_NUM_THREADS=8 \
  lab/.venv/Scripts/python.exe lab/bin/export_decoder.py
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
    LAB, ONNX_DIR, OUT_DIR, compare, detach_wn_cache, dump, fold_weight_norm, load_codec,
    onnx_size, ort_session, replace_snake, timeit,
)

REPORT: dict = {"stage": "decoder"}


class DecoderExport(nn.Module):
    """潜在 (B,32,T) -> 波形 (B,1,T*1920)。

    upstream の実行経路を平坦化したもの:
      quantizer.out_proj                       (dacvae/nn/bottleneck.py:25)
      decoder.model[0]                         (dacvae/model/dacvae.py:426)
      decoder.model[1..4] の j%2==0 チャンクのみ (dacvae/model/dacvae.py:229-234)
      wm_model.encoder_block.pre[0..2]         (forward_no_conv 相当・dacvae.py:326-332)
    """

    def __init__(self, codec_model):
        super().__init__()
        detach_wn_cache(codec_model)  # deepcopy 前に必須（onnx_common.detach_wn_cache の説明を見よ）
        seq: list[nn.Module] = [copy.deepcopy(codec_model.quantizer.out_proj)]
        dec = codec_model.decoder
        seq.append(copy.deepcopy(dec.model[0]))
        self.trace: list[str] = ["quantizer.out_proj", "decoder.model[0]"]
        for bi, blk in enumerate(list(dec.model)[1:], start=1):
            cs = int(blk._chunk_size)
            layers = list(blk.block)
            chunks = [layers[i : i + cs] for i in range(0, len(layers), cs)]
            picked = [(j * cs + k, l) for j, ch in enumerate(chunks) if j % cs == 0
                      for k, l in enumerate(ch)]
            for idx, l in picked:
                seq.append(copy.deepcopy(l))
                self.trace.append(f"decoder.model[{bi}].block[{idx}]={type(l).__name__}")
        pre = list(dec.wm_model.encoder_block.pre)
        for i, l in enumerate(pre[:-1]):
            seq.append(copy.deepcopy(l))
            self.trace.append(f"wm_model.encoder_block.pre[{i}]={type(l).__name__}")
        self.net = nn.Sequential(*seq)

    def forward(self, latent: torch.Tensor) -> torch.Tensor:
        return self.net(latent)


def main() -> None:
    torch.set_num_threads(int(__import__("os").environ.get("OMP_NUM_THREADS", "8")))
    ONNX_DIR.mkdir(parents=True, exist_ok=True)

    # ---- 0. codec ----
    codec, load_s = load_codec("cpu")
    REPORT["codec_load_s"] = round(load_s, 2)
    REPORT["codec"] = {
        "sample_rate": codec.sample_rate,
        "latent_dim": codec.latent_dim,
        "dtype": str(codec.dtype),
        "deterministic_encode": codec.deterministic_encode,
        "hop_length": int(codec.model.hop_length),
        "decoder_alpha": float(codec.model.decoder.alpha),
    }
    print("[codec]", REPORT["codec"], flush=True)

    # ---- 1. 実潜在（lab/out/steps40_seed1234.wav を encode_waveform）----
    import soundfile as sf

    wav_path = OUT_DIR / "steps40_seed1234.wav"
    data, sr = sf.read(str(wav_path), dtype="float32")
    assert sr == 48000, sr
    wav = torch.from_numpy(np.asarray(data)).view(1, 1, -1)
    REPORT["source_wav"] = {"path": str(wav_path), "sr": sr, "samples": int(wav.shape[-1])}

    t0 = time.perf_counter()
    latent_btd = codec.encode_waveform(wav, sr, normalize_db=None)  # (B,T,D)
    REPORT["encode_waveform_s"] = round(time.perf_counter() - t0, 3)
    latent_bdt = latent_btd.transpose(1, 2).contiguous()  # (B,32,T)
    REPORT["latent_shape_BTD"] = list(latent_btd.shape)
    np.save(OUT_DIR / "latent_steps40.npy", latent_btd.numpy())
    print("[latent]", latent_btd.shape, "abs.max", float(latent_btd.abs().max()), flush=True)

    # ---- 2. torch 参照（upstream 原経路: weight_norm 生きたまま）----
    with torch.inference_mode():
        ref_orig = codec.decode_latent(latent_btd).clone()
    REPORT["decode_out_shape"] = list(ref_orig.shape)
    ref_orig_np = ref_orig.numpy()

    # ---- 3. export ラッパ（平坦化 + weight_norm fold）----
    wrapper = DecoderExport(codec.model).eval()
    REPORT["flattened_layers"] = wrapper.trace
    folded, plain = fold_weight_norm(wrapper)
    REPORT["weight_norm"] = {"folded": folded, "plain_conv": plain}
    nparams = sum(p.numel() for p in wrapper.parameters())
    REPORT["decoder_params"] = nparams
    REPORT["decoder_params_fp32_bytes"] = nparams * 4
    print(f"[wrapper] layers={len(wrapper.net)} folded={folded} plain={plain} params={nparams}", flush=True)

    with torch.no_grad():
        ref_fold = wrapper(latent_bdt).clone()
    REPORT["fold_vs_orig"] = compare(ref_orig_np, ref_fold.numpy())
    print("[fold vs orig]", REPORT["fold_vs_orig"], flush=True)

    # ---- 4. export（まず dynamo=True）----
    path = ONNX_DIR / "dacvae_decoder_fp32.onnx"
    Bdim = torch.export.Dim("B", min=1, max=8)
    Tdim = torch.export.Dim("T", min=8, max=4096)
    dyn = {"latent": {0: Bdim, 2: Tdim}}

    export_log: list[dict] = []
    ok_mode = None
    for dynamo in (True, False):
        try:
            t0 = time.perf_counter()
            if dynamo:
                torch.onnx.export(
                    wrapper, (latent_bdt,), str(path),
                    input_names=["latent"], output_names=["wav"],
                    dynamic_shapes=dyn, dynamo=True,
                )
            else:
                torch.onnx.export(
                    wrapper, (latent_bdt,), str(path),
                    input_names=["latent"], output_names=["wav"],
                    dynamic_axes={"latent": {0: "B", 2: "T"}, "wav": {0: "B", 2: "S"}},
                    dynamo=False, opset_version=17,
                )
            export_log.append({"dynamo": dynamo, "ok": True, "sec": round(time.perf_counter() - t0, 2)})
            ok_mode = dynamo
            break
        except Exception:
            tb = traceback.format_exc()
            export_log.append({"dynamo": dynamo, "ok": False, "traceback": tb[-4000:]})
            print(f"[export dynamo={dynamo}] FAILED\n{tb}", flush=True)
    REPORT["export"] = export_log
    REPORT["export_mode"] = ok_mode
    if ok_mode is None:
        dump("onnx_decoder.json", REPORT)
        raise SystemExit("export failed in both modes")

    REPORT["onnx_size"] = onnx_size(path)
    import onnx as _onnx

    m = _onnx.load(str(path), load_external_data=False)
    REPORT["opset"] = {d.domain or "": d.version for d in m.opset_import}
    ops: dict[str, int] = {}
    for n in m.graph.node:
        ops[n.op_type] = ops.get(n.op_type, 0) + 1
    REPORT["op_histogram"] = dict(sorted(ops.items(), key=lambda kv: -kv[1]))
    print("[onnx]", REPORT["onnx_size"], REPORT["opset"], flush=True)

    # ---- 5. ORT 照合 ----
    sess = ort_session(path, threads=8)
    iname = sess.get_inputs()[0].name
    lat_np = latent_bdt.numpy()
    ort_out = sess.run(None, {iname: lat_np})[0]
    REPORT["ort_vs_torch_fold"] = compare(ref_fold.numpy(), ort_out)
    REPORT["ort_vs_torch_orig"] = compare(ref_orig_np, ort_out)
    print("[ort vs fold]", REPORT["ort_vs_torch_fold"], flush=True)
    print("[ort vs orig]", REPORT["ort_vs_torch_orig"], flush=True)

    # 動的軸チェック: T を半分・バッチ 2
    half = lat_np[:, :, : lat_np.shape[2] // 2]
    with torch.no_grad():
        r_half = wrapper(torch.from_numpy(half)).numpy()
    o_half = sess.run(None, {iname: half})[0]
    REPORT["dyn_T_half"] = compare(r_half, o_half) | {"T": half.shape[2]}
    b2 = np.concatenate([half, half * 0.5], axis=0)
    with torch.no_grad():
        r_b2 = wrapper(torch.from_numpy(b2)).numpy()
    o_b2 = sess.run(None, {iname: b2})[0]
    REPORT["dyn_B2"] = compare(r_b2, o_b2) | {"B": 2, "T": b2.shape[2]}
    print("[dyn]", REPORT["dyn_T_half"], REPORT["dyn_B2"], flush=True)

    # ---- 6. 所要（同じ潜在・3 回の中央値・OMP=8）----
    with torch.inference_mode():
        t_torch = timeit(lambda: codec.decode_latent(latent_btd), 3)
    t_torch.pop("_out")
    t_fold = timeit(lambda: wrapper(latent_bdt), 3)
    t_fold.pop("_out")
    t_ort = timeit(lambda: sess.run(None, {iname: lat_np}), 3)
    t_ort.pop("_out")
    audio_s = ref_orig.shape[-1] / 48000.0
    REPORT["timing"] = {
        "audio_seconds": round(audio_s, 3),
        "torch_decode_latent": t_torch,
        "torch_folded_wrapper": t_fold,
        "ort_cpu": t_ort,
        "speedup_ort_vs_torch": round(t_torch["ms_median"] / t_ort["ms_median"], 2),
        "rtf_torch": round(t_torch["ms_median"] / 1000 / audio_s, 4),
        "rtf_ort": round(t_ort["ms_median"] / 1000 / audio_s, 4),
    }
    print("[timing]", REPORT["timing"], flush=True)

    # ---- 7. Snake を eager 版に差し替えたときの export（jit.script の影響切り分け）----
    if ok_mode is True:
        REPORT["snake_jit_note"] = "dynamo=True が @torch.jit.script の Snake を含んだまま成功"
    else:
        w2 = DecoderExport(codec.model).eval()
        fold_weight_norm(w2)
        n_snake = replace_snake(w2)
        p2 = ONNX_DIR / "dacvae_decoder_fp32_eagersnake.onnx"
        try:
            torch.onnx.export(w2, (latent_bdt,), str(p2), input_names=["latent"],
                              output_names=["wav"], dynamic_shapes=dyn, dynamo=True)
            REPORT["snake_eager_export"] = {"ok": True, "replaced": n_snake,
                                            "size": onnx_size(p2)}
        except Exception:
            REPORT["snake_eager_export"] = {"ok": False, "replaced": n_snake,
                                            "traceback": traceback.format_exc()[-4000:]}
        print("[snake-eager]", REPORT["snake_eager_export"], flush=True)

    dump("onnx_decoder.json", REPORT)


if __name__ == "__main__":
    main()
