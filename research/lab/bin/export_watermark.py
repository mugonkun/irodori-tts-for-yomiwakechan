"""段 5: SilentCipher の ONNX 化実験。

(a) enc_c / dec_c / 両者を繋いだ「透かしコア」の export と ORT 照合
    - dec_c の h[:, :, message_band_size:, :] = 0（model.py:62）が export で通るか
    - enc_c/dec_c は upstream で .eval() されない（server.py:461-466）＝BatchNorm が
      バッチ統計モード。忠実に出すには BN を手書きのバッチ統計版に置き換える必要がある。
(b) 透かし全経路（torch.stft -> core -> torch.istft）の export。どこで落ちるかを逐語で記録。
(c) 透かし 1 回の所要と 48k<->44.1k リサンプルの内訳。
"""

from __future__ import annotations

import copy
import json
import sys
import time
import traceback
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F

sys.path.insert(0, str(Path(__file__).resolve().parent))
from onnx_common import ONNX_DIR, OUT_DIR, compare, dump, onnx_size, ort_session  # noqa: E402

R: dict = {"stage": "watermark"}
PAYLOAD = [73, 82, 68, 84, 83]  # "IRDTS"（irodori_tts/watermark.py:10）


class BatchStatBN2d(nn.Module):
    """training=True の nn.BatchNorm2d と等価な、統計をグラフ内で計算する版。

    torch の BN は training 時に「バッチ統計（biased var）」で正規化する。
    upstream は .eval() を呼ばないので、この形が upstream 忠実。
    """

    def __init__(self, bn: nn.BatchNorm2d):
        super().__init__()
        self.weight = nn.Parameter(bn.weight.detach().clone())
        self.bias = nn.Parameter(bn.bias.detach().clone())
        self.eps = float(bn.eps)

    def forward(self, x):
        mean = x.mean(dim=(0, 2, 3), keepdim=True)
        var = x.var(dim=(0, 2, 3), unbiased=False, keepdim=True)
        y = (x - mean) * torch.rsqrt(var + self.eps)
        return y * self.weight.view(1, -1, 1, 1) + self.bias.view(1, -1, 1, 1)


def swap_bn(module: nn.Module) -> int:
    n = 0
    for name, child in list(module.named_children()):
        if isinstance(child, nn.BatchNorm2d):
            setattr(module, name, BatchStatBN2d(child))
            n += 1
        else:
            n += swap_bn(child)
    return n


class EncCWrap(nn.Module):
    """carrier (B,1,F,T) -> carrier_enc (B,32,F,T) と msg (B,1,5,T) -> msg_enc (B,1,F,T)。"""

    def __init__(self, enc_c):
        super().__init__()
        self.enc_c = enc_c

    def forward(self, carrier, msg):
        return self.enc_c(carrier), self.enc_c.transform_message(msg)


class Core(nn.Module):
    """STFT と iSTFT の間の全部（server.py:305-336 を逐語で写したもの）。"""

    def __init__(self, enc_c, dec_c, message_sdr: float, cfg):
        super().__init__()
        self.enc_c = enc_c
        self.dec_c = dec_c
        self.message_sdr = float(message_sdr)
        self.cfg = cfg

    def forward(self, carrier, msg):
        carrier_enc = self.enc_c(carrier)
        msg_enc = self.enc_c.transform_message(msg)
        merged = torch.cat(
            (carrier_enc, carrier.repeat(1, 32, 1, 1), msg_enc.repeat(1, 32, 1, 1)), dim=1
        )
        info = self.dec_c(merged, self.message_sdr)
        if self.cfg.frame_level_normalization:
            info = info * (torch.mean(carrier**2, dim=2, keepdim=True) ** 0.5)
        elif self.cfg.utterance_level_normalization:
            info = info * (torch.mean(carrier**2, dim=(2, 3), keepdim=True) ** 0.5)
        if self.cfg.ensure_negative_message:
            info = -info
            return F.relu(info + carrier)
        return torch.abs(info + carrier)


class StftOnly(nn.Module):
    def __init__(self, n_fft, hop):
        super().__init__()
        self.n_fft, self.hop = n_fft, hop
        self.register_buffer("window", torch.hann_window(n_fft))

    def forward(self, x):  # (B, N)
        fft = torch.stft(x, self.n_fft, self.hop, self.n_fft,
                         window=self.window, return_complex=True)
        re, im = fft.real, fft.imag
        sq = re**2 + im**2
        eps = torch.ones_like(sq) * (sq == 0).float() * 1e-24
        mag = torch.sqrt(sq + eps) - torch.sqrt(eps)
        phase = torch.atan2(im, re)
        return mag, phase


class IstftOnly(nn.Module):
    def __init__(self, n_fft, hop):
        super().__init__()
        self.n_fft, self.hop = n_fft, hop
        self.register_buffer("window", torch.hann_window(n_fft))

    def forward(self, mag, phase):  # (B,F,T)
        comb = mag * torch.cos(phase) + 1j * mag * torch.sin(phase)
        return torch.istft(comb, self.n_fft, hop_length=self.hop,
                           win_length=self.n_fft, window=self.window)


class FullWM(nn.Module):
    def __init__(self, core, n_fft, hop):
        super().__init__()
        self.core, self.n_fft, self.hop = core, n_fft, hop
        self.register_buffer("window", torch.hann_window(n_fft))

    def forward(self, y, msg):  # y (1, N) @44.1k（power 正規化済み）, msg (1,1,5,T)
        x = F.pad(y, (0, self.n_fft - y.shape[1] % self.n_fft))
        fft = torch.stft(x, self.n_fft, self.hop, self.n_fft,
                         window=self.window, return_complex=True)
        re, im = fft.real, fft.imag
        sq = re**2 + im**2
        eps = torch.ones_like(sq) * (sq == 0).float() * 1e-24
        mag = torch.sqrt(sq + eps) - torch.sqrt(eps)
        phase = torch.atan2(im, re)
        rec = self.core(mag[:, None], msg)
        m2 = rec.squeeze(1)
        p2 = phase
        comb = m2 * torch.cos(p2) + 1j * m2 * torch.sin(p2)
        return torch.istft(comb, self.n_fft, hop_length=self.hop,
                           win_length=self.n_fft, window=self.window)


def try_export(tag, mod, args, path, dyn=None, input_names=None, output_names=None):
    log = {"tag": tag}
    for dynamo in (True, False):
        try:
            t0 = time.perf_counter()
            kw = dict(input_names=input_names, output_names=output_names)
            if dynamo:
                torch.onnx.export(mod, args, str(path), dynamic_shapes=dyn, dynamo=True, **kw)
            else:
                torch.onnx.export(mod, args, str(path), dynamo=False, opset_version=17, **kw)
            log[f"dynamo_{dynamo}"] = {"ok": True, "sec": round(time.perf_counter() - t0, 2)}
            log["mode"] = dynamo
            return log
        except Exception:
            tb = traceback.format_exc()
            log[f"dynamo_{dynamo}"] = {"ok": False, "traceback": tb[-6000:]}
            print(f"\n===== [{tag}] export dynamo={dynamo} FAILED =====\n{tb}", flush=True)
    log["mode"] = None
    return log


def main() -> None:
    torch.set_num_threads(8)
    ONNX_DIR.mkdir(parents=True, exist_ok=True)
    import silentcipher
    import soundfile as sf
    import torchaudio

    m = silentcipher.get_model(model_type="44.1k", device="cpu")
    cfg = m.config
    n_fft, hop = int(cfg.N_FFT), int(cfg.HOP_LENGTH)

    data, sr48 = sf.read(str(OUT_DIR / "steps40_seed1234.wav"), dtype="float32")
    y48 = torch.from_numpy(np.asarray(data)).float()
    R["input"] = {"sr": sr48, "samples": int(y48.numel()),
                  "seconds": round(y48.numel() / sr48, 3)}

    # ---------------- (c) 所要の内訳（server.py:283-351 を逐語で再現）----------------
    def stage_times():
        t = {}
        with torch.no_grad():
            t0 = time.perf_counter()
            y = torchaudio.functional.resample(y48.view(1, -1), 48000, 44100).squeeze()
            t["resample_48k_to_44k1"] = (time.perf_counter() - t0) * 1000

            t0 = time.perf_counter()
            original_power = torch.mean(y**2)
            yn = y * torch.sqrt(torch.tensor(m.average_energy_VCTK) / original_power)
            yn = yn.unsqueeze(0).unsqueeze(0)
            t["power_normalize"] = (time.perf_counter() - t0) * 1000

            t0 = time.perf_counter()
            carrier, cphase = m.stft.transform(yn.squeeze(1))
            t["stft"] = (time.perf_counter() - t0) * 1000
            carrier = carrier[:, None]
            cphase = cphase[:, None]

            t0 = time.perf_counter()
            bmsg = []
            b = "".join("{0:08b}".format(v) for v in PAYLOAD)
            for i in range(len(b) // 2):
                bmsg.append(int(b[i * 2 : i * 2 + 2], 2))
            msgs, _ = m.letters_encoding(carrier.shape[3], [bmsg])
            msg_enc0 = torch.tensor(msgs, dtype=torch.float32).unsqueeze(0)
            t["letters_encoding"] = (time.perf_counter() - t0) * 1000

            t0 = time.perf_counter()
            carrier_enc = m.enc_c(carrier)
            t["enc_c"] = (time.perf_counter() - t0) * 1000
            t0 = time.perf_counter()
            msg_enc = m.enc_c.transform_message(msg_enc0)
            t["transform_message"] = (time.perf_counter() - t0) * 1000

            t0 = time.perf_counter()
            merged = torch.cat((carrier_enc, carrier.repeat(1, 32, 1, 1),
                                msg_enc.repeat(1, 32, 1, 1)), dim=1)
            t["cat_merged"] = (time.perf_counter() - t0) * 1000
            t0 = time.perf_counter()
            info = m.dec_c(merged, cfg.message_sdr)
            t["dec_c"] = (time.perf_counter() - t0) * 1000

            t0 = time.perf_counter()
            info = info * (torch.mean(carrier**2, dim=(2, 3), keepdim=True) ** 0.5)
            info = -info
            rec = torch.nn.functional.relu(info + carrier)
            t["normalize_and_relu"] = (time.perf_counter() - t0) * 1000

            m.stft.num_samples = yn.shape[2]
            t0 = time.perf_counter()
            out = m.stft.inverse(rec.squeeze(1), cphase.squeeze(1))[0, 0]
            t["istft"] = (time.perf_counter() - t0) * 1000

            t0 = time.perf_counter()
            out = out * torch.sqrt(original_power / torch.tensor(m.average_energy_VCTK))
            t["power_restore"] = (time.perf_counter() - t0) * 1000
            t0 = time.perf_counter()
            out = torchaudio.functional.resample(out.view(1, -1), 44100, 48000).squeeze()
            out = out[: y48.numel()]
            t["resample_44k1_to_48k"] = (time.perf_counter() - t0) * 1000
        return t, out, carrier, cphase, msg_enc0, yn

    tt, out_manual, carrier, cphase, msg_enc0, yn44 = stage_times()
    tt2, _, _, _, _, _ = stage_times()
    tt3, _, _, _, _, _ = stage_times()
    med = {k: round(sorted([tt[k], tt2[k], tt3[k]])[1], 2) for k in tt}
    med["_sum"] = round(sum(med.values()), 2)
    R["stage_ms_median_of_3"] = med
    print("[stages]", json.dumps(med, ensure_ascii=False), flush=True)

    with torch.no_grad():
        t0 = time.perf_counter()
        ref_out, _ = m.encode_wav(y48, 48000, PAYLOAD, calc_sdr=False)
        e1 = (time.perf_counter() - t0) * 1000
        t0 = time.perf_counter()
        m.encode_wav(y48, 48000, PAYLOAD, calc_sdr=False)
        e2 = (time.perf_counter() - t0) * 1000
        t0 = time.perf_counter()
        m.encode_wav(y48, 48000, PAYLOAD, calc_sdr=False)
        e3 = (time.perf_counter() - t0) * 1000
    R["encode_wav_ms"] = {"median": round(sorted([e1, e2, e3])[1], 2),
                          "all": [round(e1, 2), round(e2, 2), round(e3, 2)]}
    R["manual_vs_encode_wav"] = compare(ref_out.numpy(), out_manual.numpy())
    R["watermark_rtf"] = round(R["encode_wav_ms"]["median"] / 1000 / (y48.numel() / 48000), 4)
    print("[encode_wav]", R["encode_wav_ms"], R["manual_vs_encode_wav"], flush=True)

    R["shapes"] = {"carrier": list(carrier.shape), "msg": list(msg_enc0.shape),
                   "y44k1": list(yn44.shape)}

    # ---------------- (a) enc_c / dec_c / core の export ----------------
    Tdim = torch.export.Dim("T", min=4, max=4096)

    # a-0: そのまま（training=True の BatchNorm を含む）export を試す
    encc_raw = copy.deepcopy(m.enc_c)
    R["export_enc_c_asis_training_mode"] = try_export(
        "enc_c(training BN as-is)", EncCWrap(encc_raw), (carrier, msg_enc0),
        ONNX_DIR / "sc_enc_c_asis.onnx",
        dyn={"carrier": {3: Tdim}, "msg": {3: Tdim}},
        input_names=["carrier", "msg"], output_names=["carrier_enc", "msg_enc"])

    # a-1: BN をバッチ統計版に置換してから export（upstream 忠実）
    encc = copy.deepcopy(m.enc_c)
    n1 = swap_bn(encc)
    encw = EncCWrap(encc).eval()
    with torch.no_grad():
        ref_ce, ref_me = encw(carrier, msg_enc0)
        real_ce = m.enc_c(carrier)
        real_me = m.enc_c.transform_message(msg_enc0)
    R["bn_swap_enc_c"] = {"swapped": n1,
                          "carrier_enc": compare(real_ce.numpy(), ref_ce.numpy()),
                          "msg_enc": compare(real_me.numpy(), ref_me.numpy())}
    p_enc = ONNX_DIR / "sc_enc_c_fp32.onnx"
    R["export_enc_c"] = try_export("enc_c(batch-stat BN)", encw, (carrier, msg_enc0), p_enc,
                                   dyn={"carrier": {3: Tdim}, "msg": {3: Tdim}},
                                   input_names=["carrier", "msg"],
                                   output_names=["carrier_enc", "msg_enc"])
    if R["export_enc_c"]["mode"] is not None:
        R["enc_c_size"] = onnx_size(p_enc)
        s = ort_session(p_enc, 8)
        o = s.run(None, {"carrier": carrier.numpy(), "msg": msg_enc0.numpy()})
        R["enc_c_ort_vs_torch"] = {"carrier_enc": compare(real_ce.numpy(), o[0]),
                                   "msg_enc": compare(real_me.numpy(), o[1])}
        print("[enc_c ort]", R["enc_c_ort_vs_torch"], flush=True)

    # a-2: dec_c 単体（スライス代入 h[:, :, 1024:, :] = 0 を含む）
    decc = copy.deepcopy(m.dec_c)
    n2 = swap_bn(decc)
    decc.eval()
    with torch.no_grad():
        merged = torch.cat((real_ce, carrier.repeat(1, 32, 1, 1),
                            real_me.repeat(1, 32, 1, 1)), dim=1)
        real_h = m.dec_c(merged.clone(), cfg.message_sdr)
        my_h = decc(merged.clone(), cfg.message_sdr)
    R["bn_swap_dec_c"] = {"swapped": n2, "h": compare(real_h.numpy(), my_h.numpy())}
    R["merged_shape"] = list(merged.shape)
    p_dec = ONNX_DIR / "sc_dec_c_fp32.onnx"
    R["export_dec_c"] = try_export("dec_c(batch-stat BN, slice-assign)", decc,
                                   (merged, float(cfg.message_sdr)), p_dec,
                                   dyn={"x": {3: Tdim}, "message_sdr": None},
                                   input_names=["merged"], output_names=["message_info"])
    if R["export_dec_c"]["mode"] is not None:
        R["dec_c_size"] = onnx_size(p_dec)
        s = ort_session(p_dec, 8)
        o = s.run(None, {s.get_inputs()[0].name: merged.numpy()})[0]
        R["dec_c_ort_vs_torch"] = compare(real_h.numpy(), o)
        import onnx as _onnx

        mm = _onnx.load(str(p_dec), load_external_data=False)
        ops: dict[str, int] = {}
        for nd in mm.graph.node:
            ops[nd.op_type] = ops.get(nd.op_type, 0) + 1
        R["dec_c_op_histogram"] = dict(sorted(ops.items(), key=lambda kv: -kv[1]))
        R["dec_c_slice_assign_ops"] = {k: v for k, v in ops.items()
                                       if k in ("ScatterND", "Slice", "Concat", "Pad",
                                                "Where", "Expand", "Mul", "Equal", "Range")}
        print("[dec_c ort]", R["dec_c_ort_vs_torch"], flush=True)
        print("[dec_c ops]", R["dec_c_op_histogram"], flush=True)

    # a-3: core（STFT と iSTFT の間ぜんぶ）
    core = Core(encc, decc, float(cfg.message_sdr), cfg).eval()
    with torch.no_grad():
        ref_core = core(carrier, msg_enc0)
    p_core = ONNX_DIR / "sc_core_fp32.onnx"
    R["export_core"] = try_export("core(enc_c+dec_c)", core, (carrier, msg_enc0), p_core,
                                  dyn={"carrier": {3: Tdim}, "msg": {3: Tdim}},
                                  input_names=["carrier", "msg"],
                                  output_names=["carrier_reconst"])
    if R["export_core"]["mode"] is not None:
        R["core_size"] = onnx_size(p_core)
        s = ort_session(p_core, 8)
        o = s.run(None, {"carrier": carrier.numpy(), "msg": msg_enc0.numpy()})[0]
        R["core_ort_vs_torch"] = compare(ref_core.numpy(), o)
        # core を ORT で回した結果を torch の iSTFT に戻して、最終波形まで照合
        with torch.no_grad():
            m.stft.num_samples = yn44.shape[2]
            wav_ort = m.stft.inverse(torch.from_numpy(o).squeeze(1), cphase.squeeze(1))[0, 0]
            orig_power = torch.mean(
                torchaudio.functional.resample(y48.view(1, -1), 48000, 44100).squeeze() ** 2)
            wav_ort = wav_ort * torch.sqrt(orig_power / torch.tensor(m.average_energy_VCTK))
            wav_ort = torchaudio.functional.resample(wav_ort.view(1, -1), 44100, 48000).squeeze()
            wav_ort = wav_ort[: y48.numel()]
        R["hybrid_ort_core_vs_full_torch"] = compare(ref_out.numpy(), wav_ort.numpy())
        # 復号できるか
        try:
            with torch.no_grad():
                d = m.decode_wav(wav_ort, 48000, phase_shift_decoding=False)
            R["hybrid_decode"] = {k: (v.tolist() if hasattr(v, "tolist") else v)
                                  for k, v in d.items()}
        except Exception as e:
            R["hybrid_decode"] = f"{type(e).__name__}: {e}"
        t0 = time.perf_counter()
        for _ in range(3):
            s.run(None, {"carrier": carrier.numpy(), "msg": msg_enc0.numpy()})
        R["core_ort_ms_avg3"] = round((time.perf_counter() - t0) / 3 * 1000, 2)
        print("[core ort]", R["core_ort_vs_torch"], R["hybrid_ort_core_vs_full_torch"],
              R["hybrid_decode"], flush=True)

    # ---------------- (b) STFT / iSTFT / 全経路 ----------------
    # 石: torch.export.Dim の名前は sympy.sympify に通される。"N" は sympy の関数 N と衝突し
    #     `TypeError: unsupported operand type(s) for *: 'function' and 'int'` になる
    #     （torch/export/dynamic_shapes.py:227）。N/S/E/I/O/C/Q 等の 1 文字名は避ける。
    Ndim = torch.export.Dim("NHOP", min=4, max=1 << 12)
    x44 = yn44.squeeze(1)  # (1, N)
    x44p = F.pad(x44, (0, n_fft - x44.shape[1] % n_fft))
    R["export_stft_only"] = try_export("torch.stft only", StftOnly(n_fft, hop).eval(), (x44p,),
                                       ONNX_DIR / "sc_stft.onnx",
                                       dyn={"x": {1: 2048 * Ndim}},
                                       input_names=["x"], output_names=["mag", "phase"])
    if R["export_stft_only"]["mode"] is not None:
        R["stft_size"] = onnx_size(ONNX_DIR / "sc_stft.onnx")
        try:
            s = ort_session(ONNX_DIR / "sc_stft.onnx", 8)
            o = s.run(None, {s.get_inputs()[0].name: x44p.numpy()})
            with torch.no_grad():
                rm, rp = StftOnly(n_fft, hop).eval()(x44p)
            R["stft_ort_vs_torch"] = {"mag": compare(rm.numpy(), o[0]),
                                      "phase": compare(rp.numpy(), o[1])}
        except Exception:
            R["stft_ort_vs_torch"] = {"error": traceback.format_exc()[-3000:]}
        print("[stft ort]", R["stft_ort_vs_torch"], flush=True)

    R["export_istft_only"] = try_export("torch.istft only", IstftOnly(n_fft, hop).eval(),
                                        (carrier.squeeze(1), cphase.squeeze(1)),
                                        ONNX_DIR / "sc_istft.onnx",
                                        dyn={"mag": {2: Tdim}, "phase": {2: Tdim}},
                                        input_names=["mag", "phase"], output_names=["y"])
    if R["export_istft_only"]["mode"] is not None:
        try:
            s = ort_session(ONNX_DIR / "sc_istft.onnx", 8)
            o = s.run(None, {"mag": carrier.squeeze(1).numpy(),
                             "phase": cphase.squeeze(1).numpy()})[0]
            with torch.no_grad():
                r = IstftOnly(n_fft, hop).eval()(carrier.squeeze(1), cphase.squeeze(1))
            R["istft_ort_vs_torch"] = compare(r.numpy(), o)
        except Exception:
            R["istft_ort_vs_torch"] = {"error": traceback.format_exc()[-3000:]}
        print("[istft ort]", R["istft_ort_vs_torch"], flush=True)

    R["export_full_path"] = try_export("full (stft->core->istft)",
                                       FullWM(core, n_fft, hop).eval(), (x44, msg_enc0),
                                       ONNX_DIR / "sc_full.onnx",
                                       dyn={"y": {1: 2048 * Ndim}, "msg": {3: Tdim}},
                                       input_names=["y", "msg"], output_names=["out"])

    dump("onnx_watermark.json", R)


if __name__ == "__main__":
    main()
