"""段 5(b) 追補: 表を持たない iSTFT（DFT op + 50% overlap-add を Slice/Add で）。

hop = n_fft/2 なので overlap-add は「フレーム t の後半 + フレーム t+1 の前半」で書ける
＝ ConvTranspose1d の単位行列（67 MB）も IDFT 行列（67 MB）も要らない。
"""

from __future__ import annotations

import sys
import time
import traceback
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

sys.path.insert(0, str(Path(__file__).resolve().parent))
from onnx_common import ONNX_DIR, OUT_DIR, compare, dump, onnx_size, ort_session  # noqa: E402
from export_watermark import IstftOnly  # noqa: E402

R: dict = {"stage": "istft_slim"}
N_FFT, HOP = 4096, 2048


class SlimISTFT(nn.Module):
    def __init__(self, n_fft=N_FFT, hop=HOP):
        super().__init__()
        assert hop * 2 == n_fft
        self.n_fft, self.hop = n_fft, hop
        win = torch.hann_window(n_fft)
        self.register_buffer("win", win)
        # 50% overlap の hann は winsq の和が定数（COLA）にならない端以外は
        # winsq[:hop] + winsq[hop:] で割ればよい
        wsq = win * win
        self.register_buffer("env_mid", (wsq[:hop] + wsq[hop:]).clamp_min(1e-11))
        self.register_buffer("wsq_head", wsq[:hop].clamp_min(1e-11))
        self.register_buffer("wsq_tail", wsq[hop:].clamp_min(1e-11))

    def forward(self, mag, phase):  # (B,F,T)
        re = mag * torch.cos(phase)
        im = mag * torch.sin(phase)
        frames = torch.fft.irfft(torch.complex(re, im), n=self.n_fft, dim=1)  # (B,n,T)
        frames = frames * self.win[None, :, None]
        a = frames[:, : self.hop, :]          # (B,hop,T) 各フレームの前半
        b = frames[:, self.hop :, :]          # (B,hop,T) 各フレームの後半
        # 出力ブロック i（i=0..T-2）= b[:, :, i] + a[:, :, i+1]
        y = (b[:, :, :-1] + a[:, :, 1:]) / self.env_mid[None, :, None]
        # (B,hop,T-1) -> (B,(T-1)*hop) は転置してから flatten
        return y.transpose(1, 2).reshape(y.shape[0], -1)


def main() -> None:
    torch.set_num_threads(8)
    import silentcipher
    import soundfile as sf
    import torchaudio

    m = silentcipher.get_model(model_type="44.1k", device="cpu")
    data, _ = sf.read(str(OUT_DIR / "steps40_seed1234.wav"), dtype="float32")
    y48 = torch.from_numpy(np.asarray(data)).float()
    with torch.no_grad():
        y44 = torchaudio.functional.resample(y48.view(1, -1), 48000, 44100).squeeze()
        yn = (y44 * torch.sqrt(torch.tensor(m.average_energy_VCTK) / torch.mean(y44**2))).view(1, -1)
        mag, pha = m.stft.transform(yn)
        ref = IstftOnly(N_FFT, HOP).eval()(mag, pha)
        slim = SlimISTFT().eval()
        my = slim(mag, pha)
    R["slim_vs_torch_istft"] = compare(ref.numpy(), my.numpy())
    R["shapes"] = {"mag": list(mag.shape), "ref": list(ref.shape), "slim": list(my.shape)}
    print("[slim vs torch]", R["slim_vs_torch_istft"], R["shapes"], flush=True)

    p = ONNX_DIR / "sc_istft_slim.onnx"
    Tdim = torch.export.Dim("TF", min=4, max=4096)
    try:
        torch.onnx.export(slim, (mag, pha), str(p), input_names=["mag", "phase"],
                          output_names=["y"], dynamic_shapes={"mag": {2: Tdim},
                                                              "phase": {2: Tdim}}, dynamo=True)
        R["export"] = {"ok": True}
    except Exception:
        R["export"] = {"ok": False, "traceback": traceback.format_exc()[-3000:]}
        print(traceback.format_exc(), flush=True)
        dump("onnx_istft_slim.json", R)
        return
    R["size"] = onnx_size(p)
    import onnx

    mm = onnx.load(str(p), load_external_data=False)
    ops: dict[str, int] = {}
    for nd in mm.graph.node:
        ops[nd.op_type] = ops.get(nd.op_type, 0) + 1
    R["ops"] = dict(sorted(ops.items(), key=lambda kv: -kv[1]))
    try:
        s = ort_session(p, 8)
        o = s.run(None, {"mag": mag.numpy(), "phase": pha.numpy()})[0]
        R["ort_vs_torch_istft"] = compare(ref.numpy(), o)
        t0 = time.perf_counter()
        for _ in range(3):
            s.run(None, {"mag": mag.numpy(), "phase": pha.numpy()})
        R["ort_ms_avg3"] = round((time.perf_counter() - t0) / 3 * 1000, 2)
        # 動的軸
        h = mag.shape[2] // 2
        o2 = s.run(None, {"mag": mag.numpy()[:, :, :h], "phase": pha.numpy()[:, :, :h]})[0]
        with torch.no_grad():
            r2 = slim(mag[:, :, :h], pha[:, :, :h])
        R["dyn_half"] = compare(r2.numpy(), o2) | {"T": h}
    except Exception:
        R["ort_vs_torch_istft"] = {"error": traceback.format_exc()[-2500:]}
    print("[slim onnx]", R["size"], R["ops"], R.get("ort_vs_torch_istft"),
          R.get("ort_ms_avg3"), R.get("dyn_half"), flush=True)
    dump("onnx_istft_slim.json", R)


if __name__ == "__main__":
    main()
