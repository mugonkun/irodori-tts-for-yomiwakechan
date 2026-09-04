"""段 5(b) の詰め: torch.istft が「ONNX には出るが ORT が拒む」ことの逐語確認と、
手書き iSTFT（irfft を matmul、overlap-add を ConvTranspose1d）で壁を越えられるかの実証。

さらに、STFT->core->iSTFT を全部 ONNX で回して、torch の encode_wav と最終波形を照合する。
"""

from __future__ import annotations

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
from export_watermark import Core, IstftOnly, StftOnly, swap_bn  # noqa: E402

R: dict = {"stage": "istft_workaround"}
PAYLOAD = [73, 82, 68, 84, 83]
N_FFT, HOP = 4096, 2048


class ManualISTFT(nn.Module):
    """torch.istft(center=True, onesided, normalized=False, hann) と等価な手書き版。

    - irfft: 片側スペクトル (F=n/2+1) -> 時間フレーム (n) を実数行列 2 枚の matmul で
    - overlap-add: ConvTranspose1d(in=n, out=1, k=n, stride=hop, weight=単位行列)
    - 窓二乗の envelope で割る（torch.istft と同じ正規化）
    - center 分（n//2）を両端から落とす
    """

    def __init__(self, n_fft: int = N_FFT, hop: int = HOP):
        super().__init__()
        self.n_fft, self.hop = n_fft, hop
        n = n_fft
        f = n // 2 + 1
        k = torch.arange(f, dtype=torch.float64)[:, None]
        j = torch.arange(n, dtype=torch.float64)[None, :]
        ang = 2.0 * torch.pi * k * j / n
        w = torch.full((f, 1), 2.0, dtype=torch.float64)
        w[0, 0] = 1.0
        if n % 2 == 0:
            w[-1, 0] = 1.0
        self.register_buffer("cmat", (w * torch.cos(ang) / n).float())   # (F, n)
        self.register_buffer("smat", (w * torch.sin(ang) / n).float())   # (F, n)
        win = torch.hann_window(n)
        self.register_buffer("win", win)
        eye = torch.zeros(n, 1, n)
        eye[torch.arange(n), 0, torch.arange(n)] = 1.0
        self.register_buffer("ola", eye)                                  # (n,1,n)
        self.register_buffer("winsq", (win * win)[None, :, None])         # (1,n,1)

    def forward(self, mag, phase):  # (B,F,T)
        re = mag * torch.cos(phase)
        im = mag * torch.sin(phase)
        # (B,T,F) @ (F,n) -> (B,T,n) -> (B,n,T)
        frames = (re.transpose(1, 2) @ self.cmat - im.transpose(1, 2) @ self.smat).transpose(1, 2)
        frames = frames * self.win[None, :, None]
        y = F.conv_transpose1d(frames, self.ola, stride=self.hop)          # (B,1,L)
        env = F.conv_transpose1d(
            self.winsq.expand(frames.shape[0], -1, frames.shape[2]), self.ola, stride=self.hop
        )
        y = (y / env).squeeze(1)
        h = self.n_fft // 2
        return y[:, h : y.shape[1] - h]


class ManualISTFT_irfft(ManualISTFT):
    """irfft だけ torch.fft.irfft に置き換えた版（ONNX の DFT op に落ちるか見る）。"""

    def forward(self, mag, phase):
        re = mag * torch.cos(phase)
        im = mag * torch.sin(phase)
        spec = torch.complex(re, im)
        frames = torch.fft.irfft(spec, n=self.n_fft, dim=1)                # (B,n,T)
        frames = frames * self.win[None, :, None]
        y = F.conv_transpose1d(frames, self.ola, stride=self.hop)
        env = F.conv_transpose1d(
            self.winsq.expand(frames.shape[0], -1, frames.shape[2]), self.ola, stride=self.hop
        )
        y = (y / env).squeeze(1)
        h = self.n_fft // 2
        return y[:, h : y.shape[1] - h]


def try_export(tag, mod, args, path, dyn=None, inames=None, onames=None):
    log = {"tag": tag}
    for dynamo in (True, False):
        try:
            kw = dict(input_names=inames, output_names=onames)
            if dynamo:
                torch.onnx.export(mod, args, str(path), dynamic_shapes=dyn, dynamo=True, **kw)
            else:
                torch.onnx.export(mod, args, str(path), dynamo=False, opset_version=17, **kw)
            log[f"dynamo_{dynamo}"] = {"ok": True}
            log["mode"] = dynamo
            return log
        except Exception:
            log[f"dynamo_{dynamo}"] = {"ok": False, "traceback": traceback.format_exc()[-3500:]}
            print(f"\n=== [{tag}] dynamo={dynamo} FAILED ===\n{traceback.format_exc()[-2500:]}",
                  flush=True)
    log["mode"] = None
    return log


def main() -> None:
    torch.set_num_threads(8)
    import silentcipher
    import soundfile as sf
    import torchaudio

    m = silentcipher.get_model(model_type="44.1k", device="cpu")
    cfg = m.config
    data, _ = sf.read(str(OUT_DIR / "steps40_seed1234.wav"), dtype="float32")
    y48 = torch.from_numpy(np.asarray(data)).float()

    with torch.no_grad():
        ref_out, _ = m.encode_wav(y48, 48000, PAYLOAD, calc_sdr=False)
        y44 = torchaudio.functional.resample(y48.view(1, -1), 48000, 44100).squeeze()
        orig_power = torch.mean(y44**2)
        yn = (y44 * torch.sqrt(torch.tensor(m.average_energy_VCTK) / orig_power)).view(1, -1)
        ynp = F.pad(yn, (0, N_FFT - yn.shape[1] % N_FFT))
        carrier, cphase = m.stft.transform(yn)
        carrier = carrier[:, None]
        cphase = cphase[:, None]
        b = "".join("{0:08b}".format(v) for v in PAYLOAD)
        bmsg = [int(b[i * 2 : i * 2 + 2], 2) for i in range(len(b) // 2)]
        msgs, _ = m.letters_encoding(carrier.shape[3], [bmsg])
        msg = torch.tensor(msgs, dtype=torch.float32).unsqueeze(0)

    # ---- 1. 出た sc_istft.onnx の中身（ORT が拒む節）----
    import onnx

    p_bad = ONNX_DIR / "sc_istft.onnx"
    if p_bad.exists():
        mm = onnx.load(str(p_bad), load_external_data=False)
        ops: dict[str, int] = {}
        for nd in mm.graph.node:
            ops[nd.op_type] = ops.get(nd.op_type, 0) + 1
        R["istft_onnx_ops"] = dict(sorted(ops.items(), key=lambda kv: -kv[1]))
        R["istft_onnx_opset"] = {d.domain or "": d.version for d in mm.opset_import}
        R["istft_onnx_size"] = onnx_size(p_bad)
        try:
            onnx.checker.check_model(str(p_bad))
            R["istft_onnx_checker"] = "onnx.checker: OK（ONNX 仕様上は妥当）"
        except Exception as e:
            R["istft_onnx_checker"] = f"{type(e).__name__}: {e}"
        print("[bad istft ops]", R["istft_onnx_ops"], R["istft_onnx_checker"], flush=True)

    # ---- 2. 手書き iSTFT の torch 照合 ----
    mag = carrier.squeeze(1)
    pha = cphase.squeeze(1)
    with torch.no_grad():
        ref_i = IstftOnly(N_FFT, HOP).eval()(mag, pha)
        man = ManualISTFT().eval()
        my_i = man(mag, pha)
        my_i2 = ManualISTFT_irfft().eval()(mag, pha)
    R["manual_istft_vs_torch_istft"] = compare(ref_i.numpy(), my_i.numpy())
    R["manual_irfft_vs_torch_istft"] = compare(ref_i.numpy(), my_i2.numpy())
    R["manual_istft_table_bytes"] = int(
        man.cmat.numel() * 4 + man.smat.numel() * 4 + man.ola.numel() * 4)
    print("[manual istft vs torch]", R["manual_istft_vs_torch_istft"], flush=True)

    # ---- 3. export & ORT ----
    Tdim = torch.export.Dim("TF", min=4, max=4096)
    for tag, mod, path in (
        ("manual_matmul", ManualISTFT().eval(), ONNX_DIR / "sc_istft_manual.onnx"),
        ("manual_irfft", ManualISTFT_irfft().eval(), ONNX_DIR / "sc_istft_irfft.onnx"),
    ):
        key = f"export_istft_{tag}"
        R[key] = try_export(f"iSTFT {tag}", mod, (mag, pha), path,
                            dyn={"mag": {2: Tdim}, "phase": {2: Tdim}},
                            inames=["mag", "phase"], onames=["y"])
        if R[key]["mode"] is None:
            continue
        R[f"{tag}_size"] = onnx_size(path)
        mm = onnx.load(str(path), load_external_data=False)
        o: dict[str, int] = {}
        for nd in mm.graph.node:
            o[nd.op_type] = o.get(nd.op_type, 0) + 1
        R[f"{tag}_ops"] = dict(sorted(o.items(), key=lambda kv: -kv[1]))
        try:
            s = ort_session(path, 8)
            out = s.run(None, {"mag": mag.numpy(), "phase": pha.numpy()})[0]
            R[f"{tag}_ort_vs_torch_istft"] = compare(ref_i.numpy(), out)
            t0 = time.perf_counter()
            for _ in range(3):
                s.run(None, {"mag": mag.numpy(), "phase": pha.numpy()})
            R[f"{tag}_ort_ms_avg3"] = round((time.perf_counter() - t0) / 3 * 1000, 2)
        except Exception:
            R[f"{tag}_ort_vs_torch_istft"] = {"error": traceback.format_exc()[-2500:]}
        print(f"[{tag}]", R[f"{tag}_size"], R[f"{tag}_ops"],
              R[f"{tag}_ort_vs_torch_istft"], flush=True)

    # ---- 4. 全経路（静的形状）の export と ORT 受け入れ ----
    from export_watermark import FullWM
    import copy

    encc = copy.deepcopy(m.enc_c)
    swap_bn(encc)
    decc = copy.deepcopy(m.dec_c)
    swap_bn(decc)
    decc.eval()
    core = Core(encc, decc, float(cfg.message_sdr), cfg).eval()
    p_full = ONNX_DIR / "sc_full_static.onnx"
    R["export_full_static"] = try_export("full static (stft->core->istft)",
                                         FullWM(core, N_FFT, HOP).eval(), (yn, msg), p_full,
                                         dyn=None, inames=["y", "msg"], onames=["out"])
    if R["export_full_static"]["mode"] is not None:
        R["full_static_size"] = onnx_size(p_full)
        try:
            s = ort_session(p_full, 8)
            R["full_static_ort_session"] = "OK"
        except Exception as e:
            R["full_static_ort_session"] = f"{type(e).__name__}: {e}"
        print("[full static ORT]", R["full_static_ort_session"], flush=True)

    # ---- 5. STFT.onnx -> core.onnx -> manual_istft.onnx を ORT だけで通す ----
    try:
        s_stft = ort_session(ONNX_DIR / "sc_stft.onnx", 8)
        s_core = ort_session(ONNX_DIR / "sc_core_fp32.onnx", 8)
        s_ist = ort_session(ONNX_DIR / "sc_istft_manual.onnx", 8)
        t0 = time.perf_counter()
        mg, ph = s_stft.run(None, {s_stft.get_inputs()[0].name: ynp.numpy()})
        rec = s_core.run(None, {"carrier": mg[:, None], "msg": msg.numpy()})[0]
        wav = s_ist.run(None, {"mag": rec[:, 0], "phase": ph})[0]
        chain_ms = (time.perf_counter() - t0) * 1000
        w = torch.from_numpy(wav)[0]
        w = w * torch.sqrt(orig_power / torch.tensor(m.average_energy_VCTK))
        w = torchaudio.functional.resample(w.view(1, -1), 44100, 48000).squeeze()[: y48.numel()]
        R["onnx_chain_vs_torch_encode_wav"] = compare(ref_out.numpy(), w.numpy())
        R["onnx_chain_ms"] = round(chain_ms, 2)
        with torch.no_grad():
            d = m.decode_wav(w, 48000, phase_shift_decoding=False)
        R["onnx_chain_decode"] = {k: (v.tolist() if hasattr(v, "tolist") else v)
                                  for k, v in d.items()}
        print("[onnx chain]", R["onnx_chain_vs_torch_encode_wav"], R["onnx_chain_ms"],
              R["onnx_chain_decode"], flush=True)
        sf.write(str(OUT_DIR / "wm_onnxchain.wav"), w.numpy(), 48000)
    except Exception:
        R["onnx_chain_vs_torch_encode_wav"] = {"error": traceback.format_exc()[-3000:]}
        print("[onnx chain] FAILED", flush=True)
        print(traceback.format_exc(), flush=True)

    dump("onnx_istft_workaround.json", R)


if __name__ == "__main__":
    main()
