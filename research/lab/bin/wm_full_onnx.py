"""段 5(b) 結論: torch.istft を SlimISTFT に差し替えれば「透かし全経路」が ONNX 1 本で出るか。

グラフ = power 正規化 -> torch.stft -> enc_c/dec_c -> Slim iSTFT -> power 復元。
グラフ外に残るのは 48k<->44.1k リサンプルと pad と payload の one-hot だけ。
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
import torch.nn.functional as F

sys.path.insert(0, str(Path(__file__).resolve().parent))
from onnx_common import ONNX_DIR, OUT_DIR, compare, dump, onnx_size, ort_session  # noqa: E402
from export_watermark import Core, swap_bn  # noqa: E402
from wm_istft_slim import SlimISTFT  # noqa: E402

R: dict = {"stage": "wm_full_onnx"}
PAYLOAD = [73, 82, 68, 84, 83]
N_FFT, HOP = 4096, 2048
AVG_E = 0.002837200844477648  # silentcipher/server.py:59


class WatermarkFullONNX(nn.Module):
    """44.1kHz の pad 済み波形 (1,Npad) と one-hot メッセージ (1,1,5,T) -> 透かし入り (1,Nout)。

    server.py:301（power 正規化）〜:344（power 復元）を 1 本にまとめたもの。
    リサンプル・pad・one-hot 生成はグラフ外（C# 側）。
    """

    def __init__(self, core: nn.Module, n_fft=N_FFT, hop=HOP):
        super().__init__()
        self.core = core
        self.istft = SlimISTFT(n_fft, hop)
        self.n_fft, self.hop = n_fft, hop
        self.register_buffer("window", torch.hann_window(n_fft))
        self.register_buffer("avg_e", torch.tensor(AVG_E))

    def forward(self, y_pad, msg, orig_power):
        scale = torch.sqrt(self.avg_e / orig_power)
        x = y_pad * scale
        fft = torch.stft(x, self.n_fft, self.hop, self.n_fft,
                         window=self.window, return_complex=True)
        re, im = fft.real, fft.imag
        sq = re**2 + im**2
        eps = torch.ones_like(sq) * (sq == 0).float() * 1e-24
        mag = torch.sqrt(sq + eps) - torch.sqrt(eps)
        phase = torch.atan2(im, re)
        rec = self.core(mag[:, None], msg)
        out = self.istft(rec[:, 0], phase)
        return out * torch.sqrt(orig_power / self.avg_e)


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
        orig_power = torch.mean(y44**2).view(1)
        ypad = F.pad(y44.view(1, -1), (0, N_FFT - y44.numel() % N_FFT))
        b = "".join("{0:08b}".format(v) for v in PAYLOAD)
        bmsg = [int(b[i * 2 : i * 2 + 2], 2) for i in range(len(b) // 2)]
        nframes = ypad.shape[1] // HOP + 1
        msgs, _ = m.letters_encoding(nframes, [bmsg])
        msg = torch.tensor(msgs, dtype=torch.float32).unsqueeze(0)

    encc = copy.deepcopy(m.enc_c)
    swap_bn(encc)
    decc = copy.deepcopy(m.dec_c)
    swap_bn(decc)
    decc.eval()
    core = Core(encc, decc, float(cfg.message_sdr), cfg).eval()
    mod = WatermarkFullONNX(core).eval()

    with torch.no_grad():
        ref_t = mod(ypad, msg, orig_power)
    R["shapes"] = {"y_pad": list(ypad.shape), "msg": list(msg.shape),
                   "out": list(ref_t.shape), "frames": int(nframes)}

    p = ONNX_DIR / "silentcipher_watermark_fp32.onnx"
    Kdim = torch.export.Dim("KHOP", min=4, max=4096)
    log = {}
    ok = None
    for dynamo in (True, False):
        try:
            t0 = time.perf_counter()
            if dynamo:
                torch.onnx.export(mod, (ypad, msg, orig_power), str(p),
                                  input_names=["y_pad", "msg", "orig_power"],
                                  output_names=["y_wm"],
                                  dynamic_shapes={"y_pad": {1: HOP * Kdim},
                                                  "msg": {3: None}, "orig_power": None},
                                  dynamo=True)
            else:
                torch.onnx.export(mod, (ypad, msg, orig_power), str(p),
                                  input_names=["y_pad", "msg", "orig_power"],
                                  output_names=["y_wm"], dynamo=False, opset_version=17)
            log[f"dynamo_{dynamo}"] = {"ok": True, "sec": round(time.perf_counter() - t0, 2)}
            ok = dynamo
            break
        except Exception:
            log[f"dynamo_{dynamo}"] = {"ok": False, "traceback": traceback.format_exc()[-3500:]}
            print(f"=== dynamo={dynamo} FAILED ===\n{traceback.format_exc()[-2500:]}", flush=True)
    R["export"] = log
    R["export_mode"] = ok
    if ok is None:
        dump("onnx_wm_full.json", R)
        return

    R["size"] = onnx_size(p)
    import onnx

    mm = onnx.load(str(p), load_external_data=False)
    ops: dict[str, int] = {}
    for nd in mm.graph.node:
        ops[nd.op_type] = ops.get(nd.op_type, 0) + 1
    R["ops"] = dict(sorted(ops.items(), key=lambda kv: -kv[1]))
    R["opset"] = {d.domain or "": d.version for d in mm.opset_import}
    print("[size/ops]", R["size"], R["opset"], R["ops"], flush=True)

    try:
        s = ort_session(p, 8)
        o = s.run(None, {"y_pad": ypad.numpy(), "msg": msg.numpy(),
                         "orig_power": orig_power.numpy()})[0]
        R["ort_vs_torch_graph"] = compare(ref_t.numpy(), o)
        # 48k へ戻して upstream の encode_wav と最終照合
        w = torchaudio.functional.resample(torch.from_numpy(o), 44100, 48000).squeeze()
        w = w[: y48.numel()]
        R["ort_full_vs_upstream_encode_wav"] = compare(ref_out.numpy(), w.numpy())
        with torch.no_grad():
            d = m.decode_wav(w, 48000, phase_shift_decoding=False)
        R["decode"] = {k: (v.tolist() if hasattr(v, "tolist") else v) for k, v in d.items()}
        t0 = time.perf_counter()
        for _ in range(3):
            s.run(None, {"y_pad": ypad.numpy(), "msg": msg.numpy(),
                         "orig_power": orig_power.numpy()})
        R["ort_ms_avg3"] = round((time.perf_counter() - t0) / 3 * 1000, 2)
        sf.write(str(OUT_DIR / "wm_full_onnx.wav"), w.numpy(), 48000)
        print("[ort]", R["ort_vs_torch_graph"], R["ort_full_vs_upstream_encode_wav"],
              R["decode"], R["ort_ms_avg3"], flush=True)
    except Exception:
        R["ort_vs_torch_graph"] = {"error": traceback.format_exc()[-3000:]}
        print(traceback.format_exc(), flush=True)

    dump("onnx_wm_full.json", R)


if __name__ == "__main__":
    main()
