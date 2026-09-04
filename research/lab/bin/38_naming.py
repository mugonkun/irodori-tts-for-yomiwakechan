"""38 — 「参照ボイス＋名付け＝話者」「デフォルト＝no_ref」の実射（TestClient・FakeRuntime）。

稼働機・8088/7861 には触れない。モデルは読まない（FakeRuntime）。
lab/tmp/naming38 を CWD にして voices/ と voices.json を置く。
"""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from urllib.parse import quote

LAB = Path(__file__).resolve().parent.parent
TMP = LAB / "tmp" / "naming38"
(TMP / "voices").mkdir(parents=True, exist_ok=True)
os.chdir(TMP)

sys.path.insert(0, str(LAB / "bin"))

import torch  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

from irodori_openai_tts import app as main  # noqa: E402
from irodori_openai_tts.voices import VoiceRegistry, VOICE_ID_PATTERN  # noqa: E402
from irodori_tts.inference_runtime import SamplingResult  # noqa: E402

OUT: dict = {"cases": []}
JA = "ずんだもん"
JA2 = "四国めたん"
DEFAULT_NAME = "デフォルト"


class FakeRuntime:
    def __init__(self) -> None:
        self.requests = []

    def synthesize(self, req, *, log_fn=None):
        self.requests.append(req)
        audio = torch.zeros(1, 100)
        return SamplingResult(
            audio=audio, audios=[audio], sample_rate=1000, stage_timings=[],
            total_to_decode=0.1, used_seed=1, messages=[],
        )


class FakeRuntimeManager:
    def __init__(self, runtime) -> None:
        self.runtime = runtime
        self.checkpoint_path = None
        self.is_loaded = True
        self.is_loading = False

    def get(self):
        return self.runtime


def setup(**overrides):
    for k, v in {
        "api_key": None,
        "voices_dir": TMP / "voices",
        "voice_aliases_file": None,
        "default_voice": None,
        "allow_no_ref_voice": True,
        "default_chunking_enabled": False,
        "model_name": "irodori-tts",
        "default_response_format": "wav",
        "max_concurrent_synthesis": 1,
        "synthesis_wait_timeout": 60.0,
        "empty_cache_interval": 0,
    }.items():
        setattr(main.settings, k, v)
    for k, v in overrides.items():
        setattr(main.settings, k, v)
    main.voice_registry = VoiceRegistry(main.settings)
    main._synthesis_semaphore = None
    main._synthesis_semaphore_limit = None
    fake = FakeRuntime()
    main.runtime_manager = FakeRuntimeManager(fake)
    return fake


def speak(client, voice):
    body = {"model": "irodori-tts", "input": "テスト。", "voice": voice}
    r = client.post("/v1/audio/speech", json=body)
    return r


def rec(name, **kw):
    OUT["cases"].append({"case": name, **kw})
    print(f"[{name}] {json.dumps(kw, ensure_ascii=False)[:400]}")


# ---- 素材 ----
vd = TMP / "voices"
for f in vd.glob("*"):
    f.unlink()
(vd / "alice.wav").write_bytes(b"RIFF0000WAVE")           # ASCII 檔名
(vd / f"{JA2}.wav").write_bytes(b"RIFF0000WAVE")          # 日本語 檔名（スキャン経路）
aliases = {
    JA: {"ref_wav": "alice.wav"},                          # 日本語 alias → 実ファイル
    DEFAULT_NAME: {"no_ref": True},                        # 「デフォルト」＝no_ref
    "with space と記号!": {"ref_wav": "alice.wav"},
}
(vd / "voices.json").write_text(json.dumps(aliases, ensure_ascii=False), encoding="utf-8")

fake = setup()
client = TestClient(main.app, raise_server_exceptions=False)

# 1. 一覧
r = client.get("/v1/audio/voices")
ids = [d["id"] for d in r.json()["data"]]
rec("1 list", status=r.status_code, ids=ids, leaks_abs_path=any(
    (d.get("ref_wav") or "").startswith(str(TMP)) for d in r.json()["data"]))

# 2. 日本語 alias で合成
r = speak(client, JA)
rec("2 alias-ja speech", status=r.status_code,
    ref_wav=getattr(fake.requests[-1], "ref_wav", None) if fake.requests else None,
    no_ref=getattr(fake.requests[-1], "no_ref", None) if fake.requests else None)

# 3. 日本語 檔名（スキャン経路）で合成
r = speak(client, JA2)
rec("3 scan-ja speech", status=r.status_code,
    ref_wav=getattr(fake.requests[-1], "ref_wav", None) if fake.requests else None)

# 4. 「デフォルト」＝no_ref
r = speak(client, DEFAULT_NAME)
rec("4 default-ja speech", status=r.status_code,
    no_ref=getattr(fake.requests[-1], "no_ref", None) if fake.requests else None,
    ref_wav=getattr(fake.requests[-1], "ref_wav", None) if fake.requests else None)

# 5. 空白・記号入りの名
r = speak(client, "with space と記号!")
rec("5 space-symbol speech", status=r.status_code)

# 6. アップロード API に日本語 voice_id
r = client.post("/v1/audio/voices",
                files={"file": ("ref.wav", b"RIFF0000WAVE", "audio/wav")},
                data={"voice_id": JA})
rec("6 upload voice_id=ja", status=r.status_code, body=r.text[:200],
    pattern=VOICE_ID_PATTERN.pattern)

# 7. アップロード API に日本語ファイル名（voice_id 省略＝stem を使う）
r = client.post("/v1/audio/voices",
                files={"file": (f"{JA}.wav", b"RIFF0000WAVE", "audio/wav")})
rec("7 upload filename=ja", status=r.status_code, body=r.text[:200])

# 8. アップロード API に ASCII 名（対照）
r = client.post("/v1/audio/voices",
                files={"file": ("bob.wav", b"RIFF0000WAVE", "audio/wav")},
                data={"voice_id": "bob"})
rec("8 upload ascii", status=r.status_code, body=r.text[:200])

# 9. GET /v1/audio/voices/{id} — 日本語ファイル名（URL エンコード）
r = client.get(f"/v1/audio/voices/{quote(JA2)}")
rec("9 get file ja-scan", status=r.status_code, body=r.text[:200])

# 10. GET /v1/audio/voices/{id} — alias のみの名（実ファイルが別名）
r = client.get(f"/v1/audio/voices/{quote(JA)}")
rec("10 get file ja-alias", status=r.status_code, body=r.text[:200])

# 11. DELETE 日本語（スキャン経路）
r = client.delete(f"/v1/audio/voices/{quote(JA2)}")
rec("11 delete ja-scan", status=r.status_code, body=r.text[:200])
(vd / f"{JA2}.wav").write_bytes(b"RIFF0000WAVE")

# 12. default_voice を日本語で
setup(default_voice=DEFAULT_NAME)
client = TestClient(main.app, raise_server_exceptions=False)
r = client.post("/v1/audio/speech", json={"model": "irodori-tts", "input": "テスト。"})
rec("12 default_voice=ja no voice field", status=r.status_code,
    no_ref=getattr(main.runtime_manager.runtime.requests[-1], "no_ref", None)
    if main.runtime_manager.runtime.requests else None)

# 13. allow_no_ref_voice=false でも alias の no_ref は通るか
setup(allow_no_ref_voice=False)
client = TestClient(main.app, raise_server_exceptions=False)
r = speak(client, DEFAULT_NAME)
rec("13 alias no_ref with allow_no_ref_voice=false", status=r.status_code,
    no_ref=getattr(main.runtime_manager.runtime.requests[-1], "no_ref", None)
    if main.runtime_manager.runtime.requests else None)
r2 = speak(client, "none")
rec("13b voice=none with allow_no_ref_voice=false", status=r2.status_code, body=r2.text[:160])

# 14. openapi / docs の露出（認証なしで取れるか）
setup(api_key="secret")
client = TestClient(main.app, raise_server_exceptions=False)
r = client.get("/openapi.json")
schema = r.json() if r.status_code == 200 else {}
irodori_opts = list(
    schema.get("components", {}).get("schemas", {}).get("IrodoriOptions", {}).get("properties", {}).keys()
)
rec("14 openapi.json without auth", status=r.status_code,
    n_irodori_fields=len(irodori_opts),
    has_defaults=any(
        "default" in v for v in schema.get("components", {}).get("schemas", {})
        .get("IrodoriOptions", {}).get("properties", {}).values()
    ),
    fields=irodori_opts[:6])
r = client.get("/docs")
rec("14b /docs without auth", status=r.status_code)
r = client.get("/v1/audio/voices")
rec("14c /v1/audio/voices without auth (api_key set)", status=r.status_code)
r = client.get("/health")
rec("14d /health without auth (api_key set)", status=r.status_code)

out = LAB / "out" / "38-naming.json"
out.write_text(json.dumps(OUT, ensure_ascii=False, indent=1), encoding="utf-8")
print("WROTE", out)
