"""段 5 の下ごしらえ: SilentCipher 44.1k をロードして、export の前提を実測する。

確認するもの:
  - enc_c / dec_c が eval() されているか（server.py:461-466 に .eval() が無い）
  - BatchNorm2d が学習モードなら running 統計が呼び出しごとに動くか
  - .eval() に切り替えると出力が変わるか（＝別の透かしになるか）
"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import numpy as np
import torch

sys.path.insert(0, str(Path(__file__).resolve().parent))
from onnx_common import OUT_DIR, compare, dump  # noqa: E402

R: dict = {"stage": "wm_probe"}


def main() -> None:
    torch.set_num_threads(8)
    import silentcipher

    t0 = time.perf_counter()
    m = silentcipher.get_model(model_type="44.1k", device="cpu")
    R["load_s"] = round(time.perf_counter() - t0, 2)
    cfg = vars(m.config)
    R["config"] = {k: v for k, v in cfg.items() if not k.startswith("_")}
    R["training_flags"] = {
        "enc_c": bool(m.enc_c.training),
        "dec_c": bool(m.dec_c.training),
        "dec_m0": bool(m.dec_m[0].training),
    }
    R["stft"] = {"n_fft": m.stft.filter_length, "hop": m.stft.hop_len, "win": m.stft.win_len}
    R["params"] = {
        "enc_c": sum(p.numel() for p in m.enc_c.parameters()),
        "dec_c": sum(p.numel() for p in m.dec_c.parameters()),
        "dec_m0": sum(p.numel() for p in m.dec_m[0].parameters()),
    }
    bns = [(n, mod) for n, mod in m.enc_c.named_modules()
           if isinstance(mod, torch.nn.BatchNorm2d)]
    R["batchnorm_count"] = {
        "enc_c": len(bns),
        "dec_c": sum(1 for _, mod in m.dec_c.named_modules()
                     if isinstance(mod, torch.nn.BatchNorm2d)),
    }
    print(json.dumps(R, ensure_ascii=False, indent=2), flush=True)

    import soundfile as sf

    data, sr = sf.read(str(OUT_DIR / "steps40_seed1234.wav"), dtype="float32")
    y = torch.from_numpy(np.asarray(data)).float()
    payload = [73, 82, 68, 84, 83]

    # running 統計が動くか
    name0, bn0 = bns[0]
    before = bn0.running_mean.detach().clone()
    nbt_before = int(bn0.num_batches_tracked.item())
    with torch.no_grad():
        out1, _ = m.encode_wav(y, 48000, payload, calc_sdr=False)
    after = bn0.running_mean.detach().clone()
    R["bn_running_mean_moved"] = {
        "module": name0,
        "moved": bool(not torch.equal(before, after)),
        "max_abs_delta": float((after - before).abs().max()),
        "num_batches_tracked": [nbt_before, int(bn0.num_batches_tracked.item())],
    }
    with torch.no_grad():
        out2, _ = m.encode_wav(y, 48000, payload, calc_sdr=False)
    R["repeat_bitwise_identical"] = bool(torch.equal(out1, out2))
    R["out_shape"] = list(out1.shape)

    # .eval() に切り替えたときの差
    m.enc_c.eval()
    m.dec_c.eval()
    with torch.no_grad():
        out_eval, _ = m.encode_wav(y, 48000, payload, calc_sdr=False)
    R["train_vs_eval"] = compare(out1.numpy(), out_eval.numpy())
    R["eval_vs_input"] = compare(y.numpy(), out_eval.numpy())
    R["train_vs_input"] = compare(y.numpy(), out1.numpy())
    print("[train vs eval]", R["train_vs_eval"], flush=True)
    print("[train vs input(=透かしの強さ)]", R["train_vs_input"], flush=True)

    # 復号して payload が取れるか（train / eval 双方）
    m.enc_c.train()
    m.dec_c.train()
    try:
        with torch.no_grad():
            dec_tr = m.decode_wav(out1, 48000, phase_shift_decoding=False)
        R["decode_train"] = {k: (v.tolist() if hasattr(v, "tolist") else v)
                             for k, v in dec_tr.items()} if isinstance(dec_tr, dict) else str(dec_tr)
    except Exception as e:
        R["decode_train"] = f"{type(e).__name__}: {e}"
    m.enc_c.eval()
    m.dec_c.eval()
    try:
        with torch.no_grad():
            dec_ev = m.decode_wav(out_eval, 48000, phase_shift_decoding=False)
        R["decode_eval"] = {k: (v.tolist() if hasattr(v, "tolist") else v)
                            for k, v in dec_ev.items()} if isinstance(dec_ev, dict) else str(dec_ev)
    except Exception as e:
        R["decode_eval"] = f"{type(e).__name__}: {e}"
    print("[decode train]", R["decode_train"], flush=True)
    print("[decode eval ]", R["decode_eval"], flush=True)

    dump("wm_probe.json", R)


if __name__ == "__main__":
    main()
