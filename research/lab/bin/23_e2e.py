"""23 便・段4: ⒝の端から端までの見積り（短文 3.76 s）を JSON にまとめる。"""
import json
from pathlib import Path
LAB = Path(r"C:/Users/mugonkun/source/repos/irodori-native-research/lab")
AUDIO = 3.76  # 秒（latent_steps 94・11 §5 / 13 §1 と同じ）
S = {
 "dml_warm": {  # すべて本便の実測（ウォーム中央値）
   "encode_conditions(az0)": 209.5, "duration": 2.15,
   "dit_40steps_fp32_2sessions": 2388.0, "dit_10steps_fp32_2sessions": 609.0,
   "decode_fp32(surgery)": 178.1, "decode_fp16(surgery)": 127.7,
   "watermark_parts(stft+enc_c+dec_c)": 37.3,
 },
 "ort_cpu_ep_1244": {
   "encode_conditions": 570.1, "duration": 7.12,
   "dit_40steps_fp32": 10295.0, "dit_10steps_fp32": 2634.0,
   "decode_fp32": 1101.8, "watermark": 331.0,   # 透かしは 1.24.4 で載らない＝12 §5-4 の 1.29 値
 },
}
def total(d, steps, wm):
    return d["encode_conditions(az0)"] + d["duration"] + d["dit_%dsteps_fp32_2sessions" % steps] \
           + d["decode_fp32(surgery)"] + wm
out = {"audio_sec": AUDIO, "stages_ms": S, "totals": {}}
for steps in (40, 10):
    for wmname, wm in (("dml_watermark_parts", 37.3), ("cpu_watermark_331ms", 331.0)):
        t = total(S["dml_warm"], steps, wm)
        out["totals"]["dml_%dsteps_%s" % (steps, wmname)] = {
            "ms": round(t, 1), "sec": round(t / 1000, 3), "rtf": round(t / 1000 / AUDIO, 3)}
    c = S["ort_cpu_ep_1244"]
    t = c["encode_conditions"] + c["duration"] + c["dit_%dsteps_fp32" % steps] + c["decode_fp32"] + c["watermark"]
    out["totals"]["ort_cpu_%dsteps" % steps] = {
        "ms": round(t, 1), "sec": round(t / 1000, 3), "rtf": round(t / 1000 / AUDIO, 3)}
out["reference"] = {
  "rocm_torch_bf16_warm_40steps": {"sec": 0.749, "audio_sec": 3.80, "rtf": 0.197, "src": "20 §2.3"},
  "rocm_torch_bf16_warm_10steps": {"sec": 0.309, "audio_sec": 3.80, "rtf": 0.081, "src": "20 §2.3"},
  "ort_cpu_dit_loop_only_40steps": {"sec": 10.855, "src": "13 §6 (ORT 1.29)"},
  "torch_cpu_end_to_end_40steps": {"sec": 15.262, "rtf": 4.06, "src": "11 §5"},
}
(LAB / "out" / "23_e2e.json").write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(out["totals"], ensure_ascii=False, indent=1))
