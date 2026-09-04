# 36 — 参照ボイス＋名付け＝話者 が Server の口でどこまで成立するか（TestClient・無害）
import json, os, sys, io, pathlib
BASE = pathlib.Path("C:/Users/mugonkun/source/repos/irodori-native-research")
WORK = BASE / "lab/tmp/36"
VOICES = WORK / "voices"
VOICES.mkdir(parents=True, exist_ok=True)
os.chdir(WORK)
os.environ["IRODORI_VOICES_DIR"] = str(VOICES)
os.environ["IRODORI_PRELOAD"] = "false"

# 1) 日本語名の参照ファイルと英字名を置く（中身はダミー＝列挙は中身を見ない）
(VOICES / "ずんだもん.wav").write_bytes(b"RIFF\x00\x00\x00\x00WAVEfmt ")
(VOICES / "alice.wav").write_bytes(b"RIFF\x00\x00\x00\x00WAVEfmt ")
(VOICES / "voices.json").write_text(json.dumps({
    "デフォルト": {"no_ref": True},
    "四国めたん": "alice.wav",
    "空白 入り": "alice.wav",
}, ensure_ascii=False), encoding="utf-8")

from fastapi.testclient import TestClient
import irodori_openai_tts.app as A

out = {}
with TestClient(A.app) as c:
    out["health_voices"] = c.get("/health").json()["voices"]
    out["voices_list"] = c.get("/v1/audio/voices").json()
    # 2) upload API に日本語 voice_id
    for vid in ["ずんだもん2", "ok_name-1", "空白 入り2"]:
        r = c.post("/v1/audio/voices",
                   files={"file": ("x.wav", b"RIFF\x00\x00\x00\x00WAVEfmt ", "audio/wav")},
                   data={"voice_id": vid})
        out.setdefault("upload", {})[vid] = {"status": r.status_code, "body": r.text[:300]}

# 3) resolve（合成まで行かせずレジストリ直呼び）
from irodori_openai_tts.voices import VoiceRegistry
from irodori_openai_tts.config import Settings
reg = VoiceRegistry(Settings())
res = {}
for v in ["ずんだもん", "四国めたん", "空白 入り", "デフォルト", "none", "デフォルト2", "alice"]:
    try:
        s = reg.resolve(v)
        res[v] = {"ok": True, "voice_id": s.voice_id, "no_ref": s.no_ref, "ref_wav": s.ref_wav}
    except Exception as e:
        res[v] = {"ok": False, "err": f"{type(e).__name__}: {e}"}
out["resolve"] = res

# 4) precision × device（GPU 無し機＝この lab 箱）
from irodori_tts.inference_runtime import resolve_runtime_dtype, resolve_runtime_device, \
    list_available_runtime_devices, list_available_runtime_precisions, default_runtime_device
pr = {}
for dev, prec in [("cpu","fp32"),("cpu","bf16"),("cuda","fp32"),("auto","fp32")]:
    try:
        d = resolve_runtime_device(dev) if dev != "auto" else resolve_runtime_device(default_runtime_device())
        t = resolve_runtime_dtype(precision=prec, device=d)
        pr[f"{dev}/{prec}"] = {"ok": True, "device": str(d), "dtype": str(t)}
    except Exception as e:
        pr[f"{dev}/{prec}"] = {"ok": False, "err": f"{type(e).__name__}: {e}"}
out["precision_device"] = pr
out["list_devices"] = list_available_runtime_devices()
out["default_device"] = default_runtime_device()
out["precisions_cpu"] = list_available_runtime_precisions("cpu")

print(json.dumps(out, ensure_ascii=False, indent=1))
