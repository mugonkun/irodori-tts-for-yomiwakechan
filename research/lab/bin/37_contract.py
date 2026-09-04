"""37 便 — 本体との契約面（パラメータ一覧の取得手段・話者・デフォルト）の実射。

稼働中の 8088／7861 には一切触れない。FastAPI TestClient + FakeRuntime のみ。
upstream は PYTHONPATH で読むだけ（1 バイトも書かない）。作業 CWD は lab/tmp37。

使い方:
  python lab/bin/37_contract.py --out lab/out/37_contract.json
"""

from __future__ import annotations

import argparse
import dataclasses
import json
import os
import secrets
import sys
import threading
from pathlib import Path
from typing import Any

LAB = Path(__file__).resolve().parent.parent
TMP = LAB / "tmp37"
VOICES = TMP / "voices"
VOICES.mkdir(parents=True, exist_ok=True)
os.chdir(TMP)

import torch  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

from irodori_openai_tts import app as main  # noqa: E402
from irodori_openai_tts.voices import VoiceRegistry  # noqa: E402
from irodori_tts.inference_runtime import SamplingRequest, SamplingResult  # noqa: E402

MODEL = "irodori-tts"
R: dict[str, Any] = {}


class FakeRuntime:
    def __init__(self) -> None:
        self.requests: list[Any] = []
        self.texts: list[str] = []

    def synthesize(self, req, *, log_fn=None):
        self.requests.append(req)
        self.texts.append(req.text)
        used_seed = int(secrets.randbits(63)) if req.seed is None else int(req.seed)
        audio = torch.zeros(1, max(1, len(req.text)) * 10)
        return SamplingResult(
            audio=audio, audios=[audio], sample_rate=1000, stage_timings=[],
            total_to_decode=0.1, used_seed=used_seed, messages=[],
        )


class FakeRuntimeManager:
    def __init__(self, runtime=None) -> None:
        self.runtime = runtime
        self.checkpoint_path = None
        self.is_loaded = runtime is not None
        self.is_loading = False

    def get(self):
        return self.runtime


DEFAULTS = {
    "api_key": None, "voices_dir": VOICES, "voice_aliases_file": None,
    "default_voice": None, "allow_no_ref_voice": True,
    "default_chunking_enabled": True, "default_chunk_min_chars": 80,
    "default_first_sentence_chunk_min_chars": None, "default_num_steps": 40,
    "default_duration_scale": 1.0, "max_concurrent_synthesis": 1,
    "synthesis_wait_timeout": 300.0, "model_device": "auto", "codec_device": "auto",
    "model_precision": "fp32", "codec_precision": "fp32", "empty_cache_interval": 0,
    "hf_checkpoint": "Aratako/Irodori-TTS-v4-Small", "checkpoint": None,
    "model_name": MODEL, "preload": False, "model_load_timeout": 300.0,
    "default_response_format": "wav", "compile_model": False, "compile_dynamic": False,
}


def reset(**over: Any) -> FakeRuntime:
    for k, v in DEFAULTS.items():
        setattr(main.settings, k, v)
    for k, v in over.items():
        setattr(main.settings, k, v)
    main.voice_registry = VoiceRegistry(main.settings)
    main._synthesis_semaphore = None
    main._synthesis_semaphore_limit = None
    fake = FakeRuntime()
    main.runtime_manager = FakeRuntimeManager(fake)
    return fake


def clear_voices() -> None:
    for p in VOICES.iterdir():
        if p.is_file():
            p.unlink()


def wav_bytes(seconds: float = 0.2, sr: int = 16000) -> bytes:
    import io
    import struct
    n = int(sr * seconds)
    data = b"".join(struct.pack("<h", 0) for _ in range(n))
    buf = io.BytesIO()
    buf.write(b"RIFF"); buf.write(struct.pack("<I", 36 + len(data))); buf.write(b"WAVEfmt ")
    buf.write(struct.pack("<IHHIIHH", 16, 1, 1, sr, sr * 2, 2, 16))
    buf.write(b"data"); buf.write(struct.pack("<I", len(data))); buf.write(data)
    return buf.getvalue()


def cl(**kw):
    return TestClient(main.app, raise_server_exceptions=False, **kw)


# ============================================================ (a) スキーマ
def part_a() -> None:
    out: dict[str, Any] = {}
    reset()
    c = cl()

    # a-1 素の GET（認証なし）
    for path in ("/openapi.json", "/docs", "/redoc", "/docs/oauth2-redirect"):
        r = c.get(path)
        out.setdefault("routes", {})[path] = {
            "status": r.status_code,
            "content_type": r.headers.get("content-type", ""),
            "bytes": len(r.content),
        }

    # a-2 API キーを設定しても /openapi.json は開くか
    reset(api_key="secret")
    c2 = cl()
    r = c2.get("/openapi.json")
    out["openapi_with_api_key"] = {"status": r.status_code, "bytes": len(r.content)}
    r = c2.get("/docs")
    out["docs_with_api_key"] = {"status": r.status_code}

    reset()
    c = cl()
    spec = c.get("/openapi.json").json()
    out["openapi_version"] = spec.get("openapi")
    out["info"] = spec.get("info")
    out["paths"] = sorted(spec.get("paths", {}).keys())
    schemas = spec.get("components", {}).get("schemas", {})
    out["schema_names"] = sorted(schemas.keys())

    iro = schemas.get("IrodoriOptions", {})
    sp = schemas.get("SpeechRequest", {})
    out["IrodoriOptions_raw"] = iro
    out["SpeechRequest_raw"] = sp
    out["IrodoriOptions_field_count"] = len(iro.get("properties", {}))
    out["SpeechRequest_field_count"] = len(sp.get("properties", {}))
    out["IrodoriOptions_additionalProperties"] = iro.get("additionalProperties", "<absent>")
    out["SpeechRequest_additionalProperties"] = sp.get("additionalProperties", "<absent>")

    # a-3 「型・範囲・既定」がスキーマに載るか
    def summarize(props: dict[str, Any]) -> dict[str, Any]:
        s = {}
        for name, node in props.items():
            entry: dict[str, Any] = {}
            if "anyOf" in node:
                entry["type"] = [x.get("type") or ("enum" if "enum" in x else "?") for x in node["anyOf"]]
                for x in node["anyOf"]:
                    if "enum" in x:
                        entry["enum"] = x["enum"]
                    for k in ("minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum"):
                        if k in x:
                            entry[k] = x[k]
            else:
                entry["type"] = node.get("type")
                if "enum" in node:
                    entry["enum"] = node["enum"]
            for k in ("default", "minimum", "maximum", "minLength", "maxLength"):
                if k in node:
                    entry[k] = node[k]
            entry["has_default"] = "default" in node
            entry["has_range"] = any(k in json.dumps(node) for k in ("minimum", "maximum"))
            entry["description"] = node.get("description", None)
            s[name] = entry
        return s

    out["IrodoriOptions_fields"] = summarize(iro.get("properties", {}))
    out["SpeechRequest_fields"] = summarize(sp.get("properties", {}))

    # 集計
    iof = out["IrodoriOptions_fields"]
    out["stats_IrodoriOptions"] = {
        "count": len(iof),
        "with_default_non_null": sorted(k for k, v in iof.items() if v.get("default") not in (None, "<absent>") and v["has_default"]),
        "with_default_null": sorted(k for k, v in iof.items() if v["has_default"] and v.get("default") is None),
        "without_default": sorted(k for k, v in iof.items() if not v["has_default"]),
        "with_range": sorted(k for k, v in iof.items() if v["has_range"]),
        "with_enum": {k: v["enum"] for k, v in iof.items() if "enum" in v},
        "with_description": sorted(k for k, v in iof.items() if v.get("description")),
    }
    spf = out["SpeechRequest_fields"]
    out["stats_SpeechRequest"] = {
        "count": len(spf),
        "with_range": sorted(k for k, v in spf.items() if v["has_range"]),
        "required": sp.get("required", []),
    }

    # a-4 SamplingRequest（真実）との差分
    sr_fields = {f.name: f for f in dataclasses.fields(SamplingRequest)}
    sr_defaults = {}
    for name, f in sr_fields.items():
        d = f.default
        sr_defaults[name] = None if d is dataclasses.MISSING else (str(d) if not isinstance(d, (int, float, str, bool, type(None))) else d)
    out["SamplingRequest_count"] = len(sr_fields)
    out["SamplingRequest_defaults"] = sr_defaults
    out["only_in_SamplingRequest"] = sorted(set(sr_fields) - set(iof) - {"text"})
    out["only_in_IrodoriOptions"] = sorted(set(iof) - set(sr_fields))

    # 既定値の一致／不一致（スキーマの default vs dataclass の default vs Settings の既定）
    from irodori_openai_tts.config import Settings
    st = Settings()
    mismatches = []
    for name in sorted(set(iof) & set(sr_fields)):
        schema_default = iof[name].get("default", "<absent>")
        dc_default = sr_defaults[name]
        env_name = f"default_{name}"
        env_default = getattr(st, env_name, "<no-env>")
        row = {"field": name, "openapi_default": schema_default,
               "SamplingRequest_default": dc_default, "Settings_default": env_default}
        if schema_default in (None, "<absent>") and dc_default is not None:
            row["schema_hides_real_default"] = True
        if env_default != "<no-env>" and env_default != dc_default:
            row["env_overrides_dataclass"] = True
        mismatches.append(row)
    out["default_table"] = mismatches
    out["schema_hides_real_default"] = sorted(r["field"] for r in mismatches if r.get("schema_hides_real_default"))
    out["env_overrides_dataclass"] = sorted(r["field"] for r in mismatches if r.get("env_overrides_dataclass"))

    # a-5 T-3／T-4：スキーマが真実と一致しない箇所を実射で確かめる
    reset()
    c = cl()
    fake = main.runtime_manager.runtime
    # (i) IrodoriOptions は extra=allow → 綴り違いは 200 で沈黙
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"no_ref": True, "num_step": 7, "totally_unknown": 1}})
    out["typo_in_nest"] = {"status": r.status_code,
                           "num_steps_seen": fake.requests[-1].num_steps if fake.requests else None}
    # (ii) top-level alias は Literal を迂回するか
    reset(); c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "no_ref": True,
               "t_schedule_mode": "zigzag", "decode_mode": "nonsense",
               "cfg_guidance_mode": "bogus"})
    out["toplevel_bypasses_literal"] = {
        "status": r.status_code,
        "t_schedule_mode": getattr(fake.requests[-1], "t_schedule_mode", None) if fake.requests else None,
        "decode_mode": getattr(fake.requests[-1], "decode_mode", None) if fake.requests else None,
        "cfg_guidance_mode": getattr(fake.requests[-1], "cfg_guidance_mode", None) if fake.requests else None,
    }
    # (iii) 同じ値を nest に入れたら 422 になるか（スキーマどおり）
    reset(); c = cl()
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"no_ref": True, "t_schedule_mode": "zigzag"}})
    out["nest_enforces_literal"] = {"status": r.status_code, "body": r.json() if r.status_code != 200 else None}
    # (iv) スキーマに無い範囲＝speed は ge/le が載るか、範囲外は 422 か
    reset(); c = cl()
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "no_ref": True, "speed": 9.0})
    out["speed_out_of_range"] = {"status": r.status_code}
    # (v) スキーマに範囲の無い num_steps に不正値
    reset(); c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"no_ref": True, "num_steps": -5}})
    out["num_steps_negative"] = {"status": r.status_code,
                                 "seen": fake.requests[-1].num_steps if fake.requests else None,
                                 "body": None if r.status_code == 200 else r.json()}
    reset(); c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"no_ref": True, "cfg_scale_text": 999.0}})
    out["cfg_scale_text_999"] = {"status": r.status_code,
                                 "seen": fake.requests[-1].cfg_scale_text if fake.requests else None}
    # (vi) 型違い（数値欄に文字列）
    reset(); c = cl()
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"no_ref": True, "num_steps": "たくさん"}})
    out["num_steps_string_in_nest"] = {"status": r.status_code, "body": r.json() if r.status_code != 200 else None}
    reset(); c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "no_ref": True,
               "num_steps": "たくさん"})
    out["num_steps_string_toplevel"] = {"status": r.status_code,
                                        "seen": repr(fake.requests[-1].num_steps) if fake.requests else None,
                                        "body": r.json() if r.status_code != 200 else None}
    # (vii) /health の defaults 欄（本体が既定を知る第二の手）
    reset(); c = cl()
    out["health"] = c.get("/health").json()

    R["a"] = out


# ============================================================ (b) 話者 voices
def part_b() -> None:
    out: dict[str, Any] = {}
    clear_voices()
    names = [
        "alice.wav", "ずんだもん.wav", "四国 めたん.wav", "with space.wav",
        "dot.name.wav", "UPPER.WAV", "emoji😊.wav", "n-hyphen_under.wav",
        "1234.wav", "春日部つむぎ.mp3",
    ]
    for n in names:
        (VOICES / n).write_bytes(wav_bytes())
    (VOICES / "precomputed.pt").write_bytes(b"\x00" * 16)
    (VOICES / "inv.speaker.safetensors").write_bytes(b"\x00" * 16)

    reset()
    c = cl()
    r = c.get("/v1/audio/voices")
    out["GET_voices"] = {"status": r.status_code, "body": r.json()}
    out["ids"] = [d["id"] for d in r.json()["data"]]

    # b-2 日本語 voice_id で合成できるか（＝ディレクトリ走査は検査しない）
    for vid in ["ずんだもん", "四国 めたん", "with space", "emoji😊", "UPPER", "alice", "1234", "dot.name"]:
        reset(); c = cl(); fake = main.runtime_manager.runtime
        r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "voice": vid})
        out.setdefault("synthesize_by_id", {})[vid] = {
            "status": r.status_code,
            "ref_wav": (fake.requests[-1].ref_wav if fake.requests else None),
            "body": None if r.status_code == 200 else r.json(),
        }

    # b-3 アップロード API（POST multipart）は日本語／空白を通すか
    reset()
    c = cl()
    cases = [
        ("upload_ascii", "newvoice.wav", None),
        ("upload_jp_filename", "新しい声.wav", None),
        ("upload_jp_voice_id", "another.wav", "ずんだもん2"),
        ("upload_space_voice_id", "another2.wav", "four koku"),
        ("upload_dot_voice_id", "another3.wav", "dot.id"),
        ("upload_ok_voice_id", "another4.wav", "zundamon_2-b"),
        ("upload_ext_pt", "latent.pt", "latent1"),
        ("upload_dup", "alice.wav", None),
    ]
    for label, fn, vid in cases:
        data = {"voice_id": vid} if vid else {}
        r = c.post("/v1/audio/voices", files={"file": (fn, wav_bytes(), "audio/wav")}, data=data)
        out.setdefault("upload", {})[label] = {
            "filename": fn, "voice_id": vid, "status": r.status_code,
            "body": r.json() if r.headers.get("content-type", "").startswith("application/json") else r.text[:400],
        }

    # b-4 GET/PUT/DELETE の {voice_id} も validate_voice_id を通るか
    reset(); c = cl()
    for vid in ["alice", "ずんだもん", "with space", "dot.name"]:
        r = c.get(f"/v1/audio/voices/{vid}")
        out.setdefault("GET_one", {})[vid] = {"status": r.status_code, "body": r.json()}
    for vid in ["ずんだもん", "alice"]:
        r = c.delete(f"/v1/audio/voices/{vid}")
        out.setdefault("DELETE_one", {})[vid] = {"status": r.status_code, "body": r.json()}
    out["files_after_delete"] = sorted(p.name for p in VOICES.iterdir())

    # b-5 voices.json alias（日本語キー・複数クリップ・ref_latent・no_ref）
    clear_voices()
    (VOICES / "zunda_10s.wav").write_bytes(wav_bytes())
    (VOICES / "zunda_30s.wav").write_bytes(wav_bytes())
    (VOICES / "zunda.pt").write_bytes(b"\x00" * 16)
    alias = {
        "ずんだもん": {"ref_wavs": ["zunda_10s.wav", "zunda_30s.wav"]},
        "四国 めたん": "zunda_10s.wav",
        "ずんだもん（潜在）": {"ref_latent": "zunda.pt"},
        "デフォルト": {"no_ref": True},
        "絶対パス": {"ref_wav": str((VOICES / "zunda_10s.wav").resolve())},
    }
    (VOICES / "voices.json").write_text(json.dumps(alias, ensure_ascii=False), encoding="utf-8")
    reset()
    c = cl()
    r = c.get("/v1/audio/voices")
    out["alias_GET_voices"] = {"status": r.status_code, "body": r.json()}
    for vid in ["ずんだもん", "四国 めたん", "ずんだもん（潜在）", "デフォルト", "絶対パス"]:
        reset(); c = cl(); fake = main.runtime_manager.runtime
        r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "voice": vid})
        rq = fake.requests[-1] if fake.requests else None
        out.setdefault("alias_synth", {})[vid] = {
            "status": r.status_code,
            "no_ref": getattr(rq, "no_ref", None),
            "ref_wav": getattr(rq, "ref_wav", None),
            "ref_wavs": getattr(rq, "ref_wavs", None),
            "ref_latent": getattr(rq, "ref_latent", None),
            "body": None if r.status_code == 200 else r.json(),
        }

    # b-6 話者一覧の変更検知（走査キャッシュ無し）
    reset(); c = cl()
    before = [d["id"] for d in c.get("/v1/audio/voices").json()["data"]]
    (VOICES / "hot_added.wav").write_bytes(wav_bytes())
    after = [d["id"] for d in c.get("/v1/audio/voices").json()["data"]]
    (VOICES / "hot_added.wav").unlink()
    after2 = [d["id"] for d in c.get("/v1/audio/voices").json()["data"]]
    out["hot_reload"] = {"before": before, "after_add": after, "after_remove": after2}

    # b-7 alias の壊れ方（voices.json が壊れていると一覧も合成も落ちるか）
    (VOICES / "voices.json").write_text("{ this is not json", encoding="utf-8")
    reset(); c = cl()
    r = c.get("/v1/audio/voices")
    out["broken_alias_GET"] = {"status": r.status_code, "body": r.json() if r.status_code != 200 else None}
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "irodori": {"no_ref": True}})
    out["broken_alias_speech_noref"] = {"status": r.status_code}
    r = c.get("/health")
    out["broken_alias_health"] = {"status": r.status_code}
    (VOICES / "voices.json").unlink()

    # b-8 ref_wav を直接渡す（voice 解決を飛ばす）
    reset(); c = cl(); fake = main.runtime_manager.runtime
    p = str((VOICES / "zunda_10s.wav").resolve())
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"ref_wav": p, "caption": "怒っている"}})
    out["direct_ref_wav"] = {"status": r.status_code,
                             "ref_wav": getattr(fake.requests[-1], "ref_wav", None) if fake.requests else None,
                             "caption": getattr(fake.requests[-1], "caption", None) if fake.requests else None}
    # 存在しないパスは？
    reset(); c = cl()
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"ref_wav": "C:/nonexistent/nope.wav"}})
    out["direct_ref_wav_missing"] = {"status": r.status_code, "body": r.json() if r.status_code != 200 else None}
    # alias が指す先が無い場合
    (VOICES / "voices.json").write_text(json.dumps({"欠番": "missing.wav"}, ensure_ascii=False), encoding="utf-8")
    reset(); c = cl()
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "voice": "欠番"})
    out["alias_points_missing"] = {"status": r.status_code, "body": r.json() if r.status_code != 200 else None}
    r = c.get("/v1/audio/voices")
    out["alias_points_missing_list"] = {"status": r.status_code,
                                        "ids": [d["id"] for d in r.json()["data"]] if r.status_code == 200 else None}
    (VOICES / "voices.json").unlink()

    # b-9 VoiceSpec はメタを持てるか（caption などを alias に書いたら？）
    (VOICES / "voices.json").write_text(json.dumps(
        {"メタ入り": {"ref_wav": "zunda_10s.wav", "caption": "優しい声", "cfg_scale_caption": 4.0,
                      "display_name": "ずんだもん", "num_steps": 10}}, ensure_ascii=False), encoding="utf-8")
    reset(); c = cl(); fake = main.runtime_manager.runtime
    r = c.get("/v1/audio/voices")
    out["alias_extra_keys_list"] = {"status": r.status_code, "body": r.json() if r.status_code == 200 else r.json()}
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "voice": "メタ入り"})
    rq = fake.requests[-1] if fake.requests else None
    out["alias_extra_keys_synth"] = {"status": r.status_code,
                                     "caption": getattr(rq, "caption", None),
                                     "num_steps": getattr(rq, "num_steps", None),
                                     "cfg_scale_caption": getattr(rq, "cfg_scale_caption", None)}
    out["VoiceSpec_fields"] = [f.name for f in dataclasses.fields(
        __import__("irodori_openai_tts.voices", fromlist=["VoiceSpec"]).VoiceSpec)]
    (VOICES / "voices.json").unlink()

    R["b"] = out


# ============================================================ (c) デフォルト
def part_c() -> None:
    out: dict[str, Any] = {}
    clear_voices()
    (VOICES / "zunda.wav").write_bytes(wav_bytes())

    # c-1 voice="デフォルト" を素で渡す
    for vid in ["デフォルト", "default", "none", "None", "NONE", "no_ref", "no-ref",
                "null", "text-only", "TEXT-ONLY", " none ", "デフォルト "]:
        reset(); c = cl(); fake = main.runtime_manager.runtime
        r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "voice": vid})
        rq = fake.requests[-1] if fake.requests else None
        out.setdefault("voice_word", {})[vid] = {
            "status": r.status_code, "no_ref": getattr(rq, "no_ref", None),
            "ref_wav": getattr(rq, "ref_wav", None),
            "body": None if r.status_code == 200 else r.json(),
        }

    # c-2 voice 省略・空文字・null
    for label, payload in [
        ("omitted", {"model": MODEL, "input": "あ。"}),
        ("null", {"model": MODEL, "input": "あ。", "voice": None}),
        ("empty", {"model": MODEL, "input": "あ。", "voice": ""}),
        ("dict_none", {"model": MODEL, "input": "あ。", "voice": {"id": "none"}}),
        ("no_ref_only", {"model": MODEL, "input": "あ。", "irodori": {"no_ref": True}}),
        ("no_ref_toplevel", {"model": MODEL, "input": "あ。", "no_ref": True}),
        ("no_ref_with_unknown_voice", {"model": MODEL, "input": "あ。", "voice": "デフォルト",
                                       "irodori": {"no_ref": True}}),
    ]:
        reset(); c = cl(); fake = main.runtime_manager.runtime
        r = c.post("/v1/audio/speech", json=payload)
        rq = fake.requests[-1] if fake.requests else None
        out.setdefault("voice_shape", {})[label] = {
            "status": r.status_code, "no_ref": getattr(rq, "no_ref", None),
            "ref_wav": getattr(rq, "ref_wav", None),
            "body": None if r.status_code == 200 else r.json(),
        }

    # c-3 IRODORI_DEFAULT_VOICE で「デフォルト」を仕込む道
    for dv in ["none", "デフォルト", "zunda"]:
        reset(default_voice=dv); c = cl(); fake = main.runtime_manager.runtime
        r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。"})
        rq = fake.requests[-1] if fake.requests else None
        out.setdefault("default_voice_env", {})[dv] = {
            "status": r.status_code, "no_ref": getattr(rq, "no_ref", None),
            "ref_wav": getattr(rq, "ref_wav", None),
            "body": None if r.status_code == 200 else r.json(),
        }

    # c-4 alias で「デフォルト」→ no_ref を作る（第 3 の道）
    (VOICES / "voices.json").write_text(json.dumps({"デフォルト": {"no_ref": True}},
                                                   ensure_ascii=False), encoding="utf-8")
    reset(); c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "voice": "デフォルト"})
    rq = fake.requests[-1] if fake.requests else None
    out["alias_default_no_ref"] = {"status": r.status_code, "no_ref": getattr(rq, "no_ref", None)}
    lst = c.get("/v1/audio/voices").json()
    out["alias_default_list"] = [d["id"] for d in lst["data"]]
    (VOICES / "voices.json").unlink()

    # c-5 allow_no_ref_voice=false のとき
    for vid in ["none", "デフォルト"]:
        reset(allow_no_ref_voice=False); c = cl()
        r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "voice": vid})
        out.setdefault("allow_no_ref_false", {})[vid] = {"status": r.status_code,
                                                         "body": None if r.status_code == 200 else r.json()}
    reset(allow_no_ref_voice=False); c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "irodori": {"no_ref": True}})
    out["allow_no_ref_false_nest"] = {"status": r.status_code,
                                      "no_ref": getattr(fake.requests[-1], "no_ref", None) if fake.requests else None}
    reset(allow_no_ref_voice=False); c = cl()
    out["allow_no_ref_false_list"] = [d["id"] for d in c.get("/v1/audio/voices").json()["data"]]

    # c-6 no_ref のとき caption だけが声を決める＝caption 無しでも通るか
    reset(); c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "irodori": {"no_ref": True}})
    out["no_ref_without_caption"] = {"status": r.status_code,
                                     "caption": getattr(fake.requests[-1], "caption", None) if fake.requests else None}
    reset(); c = cl()
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"no_ref": True, "caption": "   "}})
    out["caption_whitespace_only"] = {"status": r.status_code, "body": r.json() if r.status_code != 200 else None}
    reset(); c = cl()
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。",
               "irodori": {"no_ref": True, "caption": ""}})
    out["caption_empty"] = {"status": r.status_code, "body": r.json() if r.status_code != 200 else None}
    # 参照あり＋no_ref 併用は？
    reset(); c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": "あ。", "voice": "zunda",
               "irodori": {"no_ref": True}})
    rq = fake.requests[-1] if fake.requests else None
    out["voice_plus_no_ref"] = {"status": r.status_code, "no_ref": getattr(rq, "no_ref", None),
                                "ref_wav": getattr(rq, "ref_wav", None)}

    R["c"] = out


# ============================================================ (d) 契約の再確認
def part_d() -> None:
    out: dict[str, Any] = {}
    clear_voices()
    (VOICES / "zunda.wav").write_bytes(wav_bytes())

    # d-1 voice の指定が毎チャンクに複製されるか（参照の再符号化）
    reset(default_chunk_min_chars=10, default_first_sentence_chunk_min_chars=1)
    c = cl(); fake = main.runtime_manager.runtime
    body = "こんにちは。今日はいい天気ですね。散歩に行きましょう。とても楽しみです。"
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": body, "voice": "zunda",
               "irodori": {"caption": "元気な声", "seed": 1234}})
    out["chunked_with_ref"] = {
        "status": r.status_code,
        "chunks": len(fake.requests),
        "texts": list(fake.texts),
        "ref_wav_each": [getattr(q, "ref_wav", None) for q in fake.requests],
        "caption_each": [getattr(q, "caption", None) for q in fake.requests],
        "seed_each": [getattr(q, "seed", None) for q in fake.requests],
        "seed_header": r.headers.get("x-irodori-seed"),
    }
    # seed 未指定
    reset(default_chunk_min_chars=10, default_first_sentence_chunk_min_chars=1)
    c = cl(); fake = main.runtime_manager.runtime
    r = c.post("/v1/audio/speech", json={"model": MODEL, "input": body, "voice": "zunda"})
    out["chunked_no_seed"] = {"chunks": len(fake.requests),
                              "seed_each": [getattr(q, "seed", None) for q in fake.requests],
                              "seed_header": r.headers.get("x-irodori-seed")}

    # d-2 エラー態の逐語（本体アダプタが写像するもの）
    reset(); c = cl()
    cases = {
        "unknown_voice_400": {"model": MODEL, "input": "あ。", "voice": "居ない人"},
        "no_voice_400": {"model": MODEL, "input": "あ。"},
        "wrong_model_400": {"model": "gpt-4o-mini-tts", "input": "あ。", "irodori": {"no_ref": True}},
        "empty_input_422": {"model": MODEL, "input": "", "irodori": {"no_ref": True}},
        "long_input_422": {"model": MODEL, "input": "あ" * 5000, "irodori": {"no_ref": True}},
        "bad_format_400": {"model": MODEL, "input": "あ。", "response_format": "xyz",
                           "irodori": {"no_ref": True}},
    }
    for label, payload in cases.items():
        r = c.post("/v1/audio/speech", json=payload)
        b = r.json() if r.headers.get("content-type", "").startswith("application/json") else r.text[:600]
        out.setdefault("errors", {})[label] = {"status": r.status_code, "body": b}

    # d-3 /v1/models の逐語（本体が「エンジンが生きているか」を見る手）
    reset(); c = cl()
    out["models"] = c.get("/v1/models").json()

    R["d"] = out


def main_cli() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="abcd")
    args = ap.parse_args()
    if "a" in args.only:
        part_a()
    if "b" in args.only:
        part_b()
    if "c" in args.only:
        part_c()
    if "d" in args.only:
        part_d()
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(R, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
    print(f"wrote {args.out}")


if __name__ == "__main__":
    main_cli()
