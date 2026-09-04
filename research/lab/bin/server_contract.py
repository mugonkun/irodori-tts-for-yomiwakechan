"""Irodori-TTS-Server の「無害な実射」ハーネス。

稼働中の 8088 には一切触れない。FastAPI TestClient + 自前 FakeRuntime で
upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py の実挙動を確定する。

- upstream は PYTHONPATH で読むだけ（editable install なし・1 バイトも書かない）
- pytest は使わない（.pytest_cache を upstream に書かないため）
- 作業ディレクトリは lab/tmp（voices/・.env はそこ）

使い方:
  python lab/bin/server_contract.py --out lab/out/server_contract.json
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import secrets
import sys
import threading
from pathlib import Path
from typing import Any

LAB = Path(__file__).resolve().parent.parent
TMP = LAB / "tmp"
TMP.mkdir(parents=True, exist_ok=True)
os.chdir(TMP)

import torch  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

from irodori_openai_tts import app as main  # noqa: E402
from irodori_openai_tts.runtime import RuntimeManager  # noqa: E402
from irodori_openai_tts.voices import VoiceRegistry  # noqa: E402
from irodori_tts.inference_runtime import SamplingResult, resolve_runtime_device  # noqa: E402

MODEL = "irodori-tts"
REPORT: list[dict[str, Any]] = []


# ---------------------------------------------------------------- Fake 一式
class FakeRuntime:
    """tests/conftest.py + tests/test_api.py:18-42 の FakeRuntime と同等の自前版。

    差分は 2 点だけ:
      - 呼び出しごとの SamplingRequest を丸ごと保存する（契約の照合用）
      - used_seed を upstream の実装（inference_runtime.py:1173-1179）どおりに
        「req.seed が None なら secrets.randbits(63)」で決める
        ＝ 実機で「チャンクごとに seed が変わるか」を Fake で再現するため
    """

    def __init__(self, exc: BaseException | None = None, messages: list[str] | None = None) -> None:
        self.exc = exc
        self.messages = messages or []
        self.requests: list[Any] = []
        self.texts: list[str] = []
        self.seeds_in: list[int | None] = []
        self.used_seeds: list[int] = []
        self.thread_ids: list[int] = []

    def synthesize(self, req, *, log_fn=None):
        self.requests.append(req)
        self.texts.append(req.text)
        self.seeds_in.append(req.seed)
        self.thread_ids.append(threading.get_ident())
        if self.exc is not None:
            raise self.exc
        if log_fn is not None:
            log_fn("fake synthesize")
        # upstream inference_runtime.py:1173-1179 と同じ規則
        used_seed = int(secrets.randbits(63)) if req.seed is None else int(req.seed)
        self.used_seeds.append(used_seed)
        audio = torch.zeros(1, max(1, len(req.text)) * 10)
        return SamplingResult(
            audio=audio,
            audios=[audio],
            sample_rate=1000,
            stage_timings=[],
            total_to_decode=0.1,
            used_seed=used_seed,
            messages=list(self.messages),
        )


class FakeRuntimeManager:
    def __init__(self, runtime=None, exc: BaseException | None = None) -> None:
        self.runtime = runtime
        self.exc = exc
        self.checkpoint_path = None
        self.is_loaded = runtime is not None
        self.is_loading = False
        self.thread_ids: list[int] = []

    def get(self):
        self.thread_ids.append(threading.get_ident())
        if self.exc is not None:
            raise self.exc
        return self.runtime


DEFAULT_SETTINGS = {
    "api_key": None,
    "voices_dir": TMP / "voices",
    "voice_aliases_file": None,
    "default_voice": None,
    "allow_no_ref_voice": True,
    "default_chunking_enabled": True,
    "default_chunk_min_chars": 80,
    "default_first_sentence_chunk_min_chars": None,
    "default_num_steps": 40,
    "default_duration_scale": 1.0,
    "max_concurrent_synthesis": 1,
    "synthesis_wait_timeout": 300.0,
    "model_device": "auto",
    "codec_device": "auto",
    "model_precision": "fp32",
    "codec_precision": "fp32",
    "empty_cache_interval": 0,
    "hf_checkpoint": "Aratako/Irodori-TTS-v4-Small",
    "checkpoint": None,
    "model_name": "irodori-tts",
    "preload": False,
    "model_load_timeout": 300.0,
    "default_response_format": "wav",
    "compile_model": False,
    "compile_dynamic": False,
}


def reset(**overrides: Any) -> FakeRuntime:
    """settings を既定に戻し、FakeRuntime を差し込む（conftest.py 相当）。"""
    for key, value in DEFAULT_SETTINGS.items():
        setattr(main.settings, key, value)
    for key, value in overrides.items():
        setattr(main.settings, key, value)
    (TMP / "voices").mkdir(parents=True, exist_ok=True)
    main.voice_registry = VoiceRegistry(main.settings)
    main._synthesis_semaphore = None
    main._synthesis_semaphore_limit = None
    fake = FakeRuntime()
    main.runtime_manager = FakeRuntimeManager(fake)
    return fake


def req_dump(req) -> dict[str, Any]:
    """SamplingRequest から契約検分に使う欄だけ抜く。"""
    keys = (
        "text caption ref_wav ref_wavs ref_latent ref_latents ref_embed no_ref "
        "ref_normalize_db ref_ensure_max num_candidates decode_mode seconds duration_scale "
        "min_seconds max_seconds max_ref_seconds max_text_len max_caption_len num_steps "
        "cfg_scale_text cfg_scale_caption cfg_scale_speaker cfg_guidance_mode cfg_scale "
        "cfg_min_t cfg_max_t context_kv_cache speaker_uncond_mode seed t_schedule_mode "
        "sway_coeff trim_tail tail_window_size lora_adapter"
    ).split()
    return {k: getattr(req, k) for k in keys}


def sse_events(text: str) -> list[dict[str, Any]]:
    out = []
    for block in text.strip().split("\n\n"):
        lines = block.splitlines()
        event = next(ln.removeprefix("event: ") for ln in lines if ln.startswith("event: "))
        data = next(ln.removeprefix("data: ") for ln in lines if ln.startswith("data: "))
        out.append({"event": event, "data": json.loads(data)})
    return out


def shoot(
    name: str,
    payload: dict[str, Any] | None,
    *,
    fake: FakeRuntime | None = None,
    headers: dict[str, str] | None = None,
    method: str = "POST",
    path: str = "/v1/audio/speech",
    raise_server_exceptions: bool = True,
    note: str = "",
) -> dict[str, Any]:
    client = TestClient(main.app, raise_server_exceptions=raise_server_exceptions)
    if method == "POST":
        resp = client.post(path, json=payload, headers=headers)
    else:
        resp = client.get(path, headers=headers)

    ctype = resp.headers.get("content-type", "")
    entry: dict[str, Any] = {
        "case": name,
        "note": note,
        "request": payload,
        "status": resp.status_code,
        "headers": {
            k: v
            for k, v in resp.headers.items()
            if k.lower().startswith("x-irodori") or k.lower() in
            ("content-type", "content-disposition", "cache-control", "x-accel-buffering")
        },
    }
    if "text/event-stream" in ctype:
        entry["sse"] = sse_events(resp.text)
        for ev in entry["sse"]:
            if ev["event"] == "audio_chunk":
                raw = base64.b64decode(ev["data"]["audio_base64"])
                ev["data"]["audio_base64"] = f"<{len(raw)} bytes, head={raw[:4]!r}>"
    elif ctype.startswith("audio/"):
        entry["body"] = f"<binary {len(resp.content)} bytes, head={resp.content[:4]!r}>"
    else:
        try:
            entry["body"] = resp.json()
        except Exception:
            entry["body"] = resp.text[:2000]
    if fake is not None:
        entry["runtime_calls"] = len(fake.requests)
        entry["chunk_texts"] = list(fake.texts)
        entry["seed_in_request"] = list(fake.seeds_in)
        entry["used_seeds"] = list(fake.used_seeds)
        if fake.requests:
            entry["sampling_request[0]"] = req_dump(fake.requests[0])
    REPORT.append(entry)
    return entry


def record(name: str, data: dict[str, Any], note: str = "") -> dict[str, Any]:
    entry = {"case": name, "note": note, **data}
    REPORT.append(entry)
    return entry


TEXT3 = "こんにちは。今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。"


# ------------------------------------------------------------------- 問い 1
def q1_voice_omission() -> None:
    f = reset()
    shoot("1a irodori.no_ref=true / voice 省略", {"model": MODEL, "input": "こんにちは。", "irodori": {"no_ref": True}}, fake=f,
          note="app.py:439-455 の分岐に入るか")

    f = reset()
    shoot("1b voice 省略・no_ref なし", {"model": MODEL, "input": "こんにちは。"}, fake=f,
          note="probe の 400 の再現（文言逐語）")

    f = reset()
    shoot("1c トップレベル平坦 no_ref=true / voice 省略", {"model": MODEL, "input": "こんにちは。", "no_ref": True}, fake=f,
          note="app.py:436-437 _extra 経由")

    f = reset()
    shoot("1d irodori.no_ref=false / voice 省略", {"model": MODEL, "input": "こんにちは。", "irodori": {"no_ref": False}}, fake=f,
          note="`or explicit_no_ref` が falsy → レジストリへ落ちるか")

    f = reset()
    shoot("1e voice='none'", {"model": MODEL, "input": "こんにちは。", "voice": "none"}, fake=f)

    f = reset()
    shoot("1f voice='' 空文字", {"model": MODEL, "input": "こんにちは。", "voice": ""}, fake=f)

    f = reset()
    shoot("1g voice=null 明示", {"model": MODEL, "input": "こんにちは。", "voice": None}, fake=f)

    f = reset(allow_no_ref_voice=False)
    shoot("1h ALLOW_NO_REF_VOICE=false + irodori.no_ref=true", {"model": MODEL, "input": "こんにちは。", "irodori": {"no_ref": True}}, fake=f,
          note="no_ref 分岐はレジストリを飛ばすので allow_no_ref_voice に縛られないはず")

    f = reset(allow_no_ref_voice=False)
    shoot("1i ALLOW_NO_REF_VOICE=false + voice='none'", {"model": MODEL, "input": "こんにちは。", "voice": "none"}, fake=f)

    f = reset(default_voice="none")
    shoot("1j IRODORI_DEFAULT_VOICE=none + voice 省略", {"model": MODEL, "input": "こんにちは。"}, fake=f,
          note="probe U-2 の未測項目")

    f = reset()
    shoot("1k voice={'id':'none'} オブジェクト", {"model": MODEL, "input": "こんにちは。", "voice": {"id": "none"}}, fake=f)

    f = reset()
    shoot("1l 未知 voice", {"model": MODEL, "input": "こんにちは。", "voice": "dare"}, fake=f)


# ------------------------------------------------------------------- 問い 2
def q2_flat_aliases() -> None:
    f = reset()
    shoot("2a トップレベル num_steps=7", {"model": MODEL, "input": "あ。", "voice": "none", "num_steps": 7}, fake=f)

    f = reset()
    shoot("2b irodori.num_steps=11 と トップレベル num_steps=7 の併記",
          {"model": MODEL, "input": "あ。", "voice": "none", "num_steps": 7, "irodori": {"num_steps": 11}}, fake=f,
          note="_coalesce(opts.X, _extra(X), env) の優先順（app.py:920-923）")

    f = reset()
    shoot("2c 綴り違い num_step=7（トップレベル）", {"model": MODEL, "input": "あ。", "voice": "none", "num_step": 7}, fake=f,
          note="黙って無視されるか（既定 40 のままか）")

    f = reset()
    shoot("2d 綴り違い irodori.num_step=7（ネスト内）", {"model": MODEL, "input": "あ。", "voice": "none", "irodori": {"num_step": 7}}, fake=f)

    f = reset()
    shoot("2e トップレベル caption / cfg_scale_caption",
          {"model": MODEL, "input": "あ。", "voice": "none", "caption": "元気な声で", "cfg_scale_caption": 4.0}, fake=f)

    f = reset()
    shoot("2f トップレベル chunking=false（chunking_enabled の別名）",
          {"model": MODEL, "input": TEXT3, "voice": "none", "chunking": False, "chunk_min_chars": 5}, fake=f,
          note="app.py:555 の別名。無効化されればチャンクは 1 本")

    f = reset()
    shoot("2g irodori.chunking=false（ネスト内に別名を書く＝効かないはず）",
          {"model": MODEL, "input": TEXT3, "voice": "none", "irodori": {"chunking": False, "chunk_min_chars": 5}}, fake=f,
          note="_extra はトップレベルしか見ない（app.py:1225-1229）")

    f = reset()
    shoot("2h irodori.num_steps に非数値（ネスト＝pydantic 型検査）",
          {"model": MODEL, "input": "あ。", "voice": "none", "irodori": {"num_steps": "abc"}}, fake=f)

    f = reset()
    shoot("2i トップレベル num_steps に非数値（extra 経路＝_as_int）",
          {"model": MODEL, "input": "あ。", "voice": "none", "num_steps": "abc"}, fake=f,
          note="欄名がメッセージに入るか")

    f = reset()
    shoot("2j irodori.t_schedule_mode に範囲外（Literal）",
          {"model": MODEL, "input": "あ。", "voice": "none", "irodori": {"t_schedule_mode": "zigzag"}}, fake=f)

    f = reset()
    shoot("2k トップレベル t_schedule_mode に範囲外（extra 経路＝素通し）",
          {"model": MODEL, "input": "あ。", "voice": "none", "t_schedule_mode": "zigzag"}, fake=f,
          note="Literal 検査を回避できてしまうか")

    f = reset(default_first_sentence_chunk_min_chars=3)
    shoot("2l env 既定 first_sentence=3 のまま（何も指定しない）",
          {"model": MODEL, "input": TEXT3, "voice": "none"}, fake=f,
          note="対照。先頭文だけ 3 字閾値で切れる")

    f = reset(default_first_sentence_chunk_min_chars=3)
    shoot("2m irodori.first_sentence_chunk_min_chars=null（明示 null で env 既定を打ち消す）",
          {"model": MODEL, "input": TEXT3, "voice": "none", "irodori": {"first_sentence_chunk_min_chars": None}}, fake=f,
          note="_explicit_option（app.py:1232-1238）")

    f = reset(default_first_sentence_chunk_min_chars=3)
    shoot("2m2 トップレベル first_sentence_chunk_min_chars=null（extra 経路の明示 null）",
          {"model": MODEL, "input": TEXT3, "voice": "none", "first_sentence_chunk_min_chars": None}, fake=f)

    f = reset()
    shoot("2n0 irodori に非オブジェクト（文字列）を渡す",
          {"model": MODEL, "input": "あ。", "voice": "none", "irodori": "num_steps=7"}, fake=f)

    f = reset()
    shoot("2n 完全に未知の欄（トップレベル・irodori 両方）",
          {"model": MODEL, "input": "あ。", "voice": "none", "totally_unknown": 1,
           "irodori": {"also_unknown": "x"}}, fake=f)


# ------------------------------------------------------------------- 問い 3
def q3_sse() -> None:
    f = reset()
    shoot("3a SSE + chunking_enabled + chunk_min_chars=80 + first_sentence=1",
          {"model": MODEL, "input": TEXT3, "voice": "none", "stream_format": "sse",
           "irodori": {"chunking_enabled": True, "chunk_min_chars": 80,
                       "first_sentence_chunk_min_chars": 1}}, fake=f,
          note="依頼の本命ケース。seed 未指定")

    f = reset()
    shoot("3b 同じ本文・同じノブで seed=1234 を明示",
          {"model": MODEL, "input": TEXT3, "voice": "none", "stream_format": "sse",
           "irodori": {"chunking_enabled": True, "chunk_min_chars": 80,
                       "first_sentence_chunk_min_chars": 1, "seed": 1234}}, fake=f,
          note="seed を固定するとチャンク間で同じになるか")

    f = reset()
    shoot("3c SSE + chunk_min_chars=10 + first_sentence=1（細かく切る）",
          {"model": MODEL, "input": TEXT3, "voice": "none", "stream_format": "sse",
           "irodori": {"chunk_min_chars": 10, "first_sentence_chunk_min_chars": 1}}, fake=f)

    f = reset()
    shoot("3d 非ストリームで同じ本文・同じノブ（連結される）",
          {"model": MODEL, "input": TEXT3, "voice": "none",
           "irodori": {"chunk_min_chars": 80, "first_sentence_chunk_min_chars": 1}}, fake=f,
          note="X-Irodori-Seed は 1 本目の seed（app.py:688）")

    f = reset()
    shoot("3e stream_format='SSE'（大文字）", {"model": MODEL, "input": "あ。", "voice": "none", "stream_format": "SSE"}, fake=f)

    f = reset()
    shoot("3f stream_format='audio'（不正値）", {"model": MODEL, "input": "あ。", "voice": "none", "stream_format": "audio"}, fake=f)


# ------------------------------------------------------------------- 問い 4
def q4_split() -> None:
    cases = [
        ("読点だけ・min=5", "あいうえお、かきくけこ、さしすせそ、たちつてと", 5, None),
        ("読点だけ・min=80", "あいうえお、かきくけこ、さしすせそ、たちつてと", 80, None),
        ("読点だけ・min=5・first=1", "あいうえお、かきくけこ、さしすせそ、たちつてと", 5, 1),
        ("80 字未満・既定 min=80", TEXT3, 80, None),
        ("80 字未満・min=80・first=1", TEXT3, 80, 1),
        ("80 字未満・min=10", TEXT3, 10, None),
        ("改行入り・min=80", "第一行です\n第二行です\r\n第三行です", 80, None),
        ("改行入り・min=3", "第一行です\n第二行です\r\n第三行です", 3, None),
        ("改行入り・min=3・first=1", "第一行です\n第二行です\r\n第三行です", 3, 1),
        ("句点なし単文・min=1", "句点のない文", 1, None),
        ("空白で稼ぐ・min=5", "あ 、い 、う 、え 、お 。", 5, None),
        ("長文 100 字・min=80", "あいうえお、" * 20, 80, None),
    ]
    out = []
    for label, text, min_chars, first in cases:
        chunks = main._split_text_for_speech(text, min_chars=min_chars,
                                             first_sentence_min_chars=first)
        out.append({
            "label": label,
            "text": text,
            "len_chars": len(text),
            "min_chars": min_chars,
            "first_sentence_min_chars": first,
            "chunks": chunks,
            "n": len(chunks),
            "chunk_lens": [len(c) for c in chunks],
        })
    record("4 _split_text_for_speech 直接呼び出し", {"cases": out},
           note="app.py:606-639 の純関数。HTTP を介さず実測")


# ------------------------------------------------------------------- 問い 5
def q5_speed_seconds() -> None:
    f = reset()
    shoot("5a speed=1.25", {"model": MODEL, "input": "あ。", "voice": "none", "speed": 1.25}, fake=f)

    f = reset()
    shoot("5b speed=1.25 + irodori.duration_scale=2.0",
          {"model": MODEL, "input": "あ。", "voice": "none", "speed": 1.25,
           "irodori": {"duration_scale": 2.0}}, fake=f,
          note="掛け合わせ（除算）になるか")

    f = reset()
    shoot("5c speed=1.0 + duration_scale=2.0（speed==1.0 は除算しない）",
          {"model": MODEL, "input": "あ。", "voice": "none", "speed": 1.0,
           "irodori": {"duration_scale": 2.0}}, fake=f)

    f = reset()
    shoot("5d speed=5.0（範囲外）", {"model": MODEL, "input": "あ。", "voice": "none", "speed": 5.0}, fake=f)

    f = reset()
    shoot("5e irodori.seconds=5.0 + 長文（chunking 無効化されるか）",
          {"model": MODEL, "input": TEXT3, "voice": "none",
           "irodori": {"seconds": 5.0, "chunk_min_chars": 10}}, fake=f,
          note="app.py:562-564")

    f = reset()
    shoot("5f irodori.seconds=5.0 + speed=1.25（seconds も割られるか）",
          {"model": MODEL, "input": "あ。", "voice": "none", "speed": 1.25,
           "irodori": {"seconds": 5.0}}, fake=f)

    f = reset()
    shoot("5g irodori.duration_scale=0（400 のはず）",
          {"model": MODEL, "input": "あ。", "voice": "none", "irodori": {"duration_scale": 0}}, fake=f)


# ------------------------------------------------------------------- 問い 6
def q6_device() -> None:
    rm_cases = ["cuda:1", "auto", "", "AUTO", "  auto  ", "CUDA:1", "cpu", "cuda", "rocm", "mps"]
    rm = {}
    for value in rm_cases:
        try:
            rm[repr(value)] = {"ok": True, "value": RuntimeManager._resolve_device(value)}
        except Exception as exc:  # noqa: BLE001
            rm[repr(value)] = {"ok": False, "error": f"{type(exc).__name__}: {exc}"}

    rr_cases = ["cuda:1", "cuda", "cpu", "cpu:0", "CUDA:1", "mps", "mps:0", "xpu", "xpu:0",
                "rocm", "gpu", "", "auto"]
    rr = {}
    for value in rr_cases:
        try:
            rr[repr(value)] = {"ok": True, "value": str(resolve_runtime_device(value))}
        except Exception as exc:  # noqa: BLE001
            rr[repr(value)] = {"ok": False, "error": f"{type(exc).__name__}: {exc}"}

    record("6 デバイス解決", {
        "torch_version": torch.__version__,
        "torch.cuda.is_available()": bool(torch.cuda.is_available()),
        "torch.version.cuda": getattr(torch.version, "cuda", None),
        "torch.version.hip": getattr(torch.version, "hip", None),
        "RuntimeManager._resolve_device": rm,
        "inference_runtime.resolve_runtime_device": rr,
    }, note="runtime.py:97-102 / inference_runtime.py:55-77。この機は CPU のみ")


# ------------------------------------------------------------------- 問い 7
def q7_health() -> None:
    reset()
    main.runtime_manager = RuntimeManager(main.settings)   # 本物・未ロードのまま（get() は呼ばない）
    shoot("7a /health（既定・本物の RuntimeManager が未ロードの状態）", None, method="GET", path="/health",
          note="モデルを読まない（runtime_manager.get() は呼ばれない）")

    reset(model_device="cuda:1", codec_device="cpu", model_precision="bf16",
          hf_checkpoint="Aratako/Irodori-TTS-v4.1-Small")
    main.runtime_manager = RuntimeManager(main.settings)
    shoot("7b /health（model_device='cuda:1' を設定・CPU 機で未ロード）", None, method="GET", path="/health",
          note="設定値の echo であることの確認（実デバイスではない）")

    reset(api_key="secret")
    main.runtime_manager = RuntimeManager(main.settings)
    shoot("7c /health（API キー設定時・ヘッダ無し）", None, method="GET", path="/health",
          note="/health だけ require_auth を持たない（app.py:165）")

    f = reset()
    main.runtime_manager = FakeRuntimeManager(f)
    main.runtime_manager.is_loaded = True
    main.runtime_manager.is_loading = False
    main.runtime_manager.checkpoint_path = "C:/fake/model.safetensors"
    shoot("7d /health（ロード済みを模した状態）", None, method="GET", path="/health")

    reset()
    shoot("7e /v1/models", None, method="GET", path="/v1/models")

    reset()
    shoot("7f /v1/audio/voices（空の voices ディレクトリ）", None, method="GET", path="/v1/audio/voices")


# ------------------------------------------------------------------- 問い 8
def q8_errors() -> None:
    exc_cases = [
        (ValueError("bad reference"), "ValueError"),
        (FileNotFoundError("no such file: C:/x/y.wav"), "FileNotFoundError"),
        (RuntimeError("Dynamic LoRA loading is not compatible with compile_model=True."),
         "RuntimeError(LoRA 文言)"),
        (RuntimeError("kaboom"), "RuntimeError(その他)"),
    ]
    try:
        exc_cases.append((torch.cuda.OutOfMemoryError("CUDA out of memory."),
                          "torch.cuda.OutOfMemoryError"))
    except Exception:  # noqa: BLE001
        pass
    for exc, label in exc_cases:
        reset()
        fake = FakeRuntime(exc=exc)
        main.runtime_manager = FakeRuntimeManager(fake)
        shoot(f"8 非ストリームで {label}", {"model": MODEL, "input": "あ。", "voice": "none"},
              fake=fake, raise_server_exceptions=False,
              note="app.py:359-364 / 160-162")

    for exc, label in (
        (ValueError("bad reference"), "ValueError"),
        (RuntimeError("kaboom"), "RuntimeError(その他)"),
    ):
        reset()
        fake = FakeRuntime(exc=exc)
        main.runtime_manager = FakeRuntimeManager(fake)
        shoot(f"8 SSE で {label}", {"model": MODEL, "input": "あ。", "voice": "none",
                                    "stream_format": "sse"},
              fake=fake, raise_server_exceptions=False, note="app.py:746-762")

    # ランタイム読み込み時間切れ
    from irodori_openai_tts.runtime import RuntimeLoadTimeoutError
    reset()
    main.runtime_manager = FakeRuntimeManager(None, exc=RuntimeLoadTimeoutError(
        "Model is still loading. Retry after a moment. timeout=300.0s"))
    shoot("8 非ストリームで RuntimeLoadTimeoutError", {"model": MODEL, "input": "あ。", "voice": "none"},
          raise_server_exceptions=False)

    reset()
    main.runtime_manager = FakeRuntimeManager(None, exc=RuntimeLoadTimeoutError(
        "Model is still loading. Retry after a moment. timeout=300.0s"))
    shoot("8 SSE で RuntimeLoadTimeoutError", {"model": MODEL, "input": "あ。", "voice": "none",
                                               "stream_format": "sse"},
          raise_server_exceptions=False)

    # 入力の異常系（推論に届く前）
    f = reset()
    shoot("8 model 不一致", {"model": "gpt-4o-mini-tts", "input": "あ。", "voice": "none"}, fake=f)
    f = reset()
    shoot("8 input 空白のみ", {"model": MODEL, "input": "   ", "voice": "none"}, fake=f)
    f = reset()
    shoot("8 input 空文字（pydantic）", {"model": MODEL, "input": "", "voice": "none"}, fake=f)
    f = reset()
    shoot("8 response_format 未対応", {"model": MODEL, "input": "あ。", "voice": "none",
                                     "response_format": "ogg"}, fake=f)
    f = reset()
    shoot("8 caption 空文字", {"model": MODEL, "input": "あ。", "voice": "none",
                             "irodori": {"caption": ""}}, fake=f)
    f = reset()
    shoot("8 no_ref と ref_wav の併用", {"model": MODEL, "input": "あ。",
                                      "irodori": {"no_ref": True, "ref_wav": "C:/x.wav"}}, fake=f)
    f = reset()
    shoot("8 chunk_min_chars=0", {"model": MODEL, "input": "あ。", "voice": "none",
                                 "irodori": {"chunk_min_chars": 0}}, fake=f)
    reset()
    fake = FakeRuntime(messages=["warning: SilentCipher watermark is unavailable; skipping."])
    main.runtime_manager = FakeRuntimeManager(fake)
    shoot("8 X-Irodori-Messages（messages 非空のとき）",
          {"model": MODEL, "input": "あ。", "voice": "none"}, fake=fake)


# ------------------------------------------------------------------- 問い 9
def q9_auth() -> None:
    variants = [
        ("ヘッダ無し", None),
        ("Bearer secret（正）", "Bearer secret"),
        ("bearer secret（小文字 b）", "bearer secret"),
        ("BEARER secret", "BEARER secret"),
        ("Bearer  secret（空白 2 個）", "Bearer  secret"),
        ("Bearer secret （末尾空白）", "Bearer secret "),
        (" Bearer secret（先頭空白）", " Bearer secret"),
        ("Bearer wrong", "Bearer wrong"),
        ("secret（素の値）", "secret"),
        ("Token secret", "Token secret"),
    ]
    for label, value in variants:
        f = reset(api_key="secret")
        headers = {"Authorization": value} if value is not None else None
        shoot(f"9 {label} → /v1/audio/speech", {"model": MODEL, "input": "あ。", "voice": "none"},
              fake=f, headers=headers)

    reset(api_key="secret")
    shoot("9 /v1/models（ヘッダ無し）", None, method="GET", path="/v1/models")
    reset(api_key="secret")
    shoot("9 /v1/models（Bearer secret）", None, method="GET", path="/v1/models",
          headers={"Authorization": "Bearer secret"})
    reset(api_key=None)
    shoot("9 api_key 未設定 + 間違ったヘッダ（素通り）", {"model": MODEL, "input": "あ。", "voice": "none"},
          headers={"Authorization": "Bearer whatever"})


# ------------------------------------------------------- 追補（⒞ に効く小問）
def q0_extra() -> None:
    # 401 に WWW-Authenticate が付くか
    reset(api_key="secret")
    client = TestClient(main.app)
    resp = client.get("/v1/models")
    record("0a 401 の全ヘッダ", {"status": resp.status_code,
                                "all_headers": dict(resp.headers)},
           note="WWW-Authenticate の有無")

    # SSE 経路でも検証は先に走る（＝SSE ではなく JSON 400 が返る）
    f = reset()
    shoot("0b stream_format=sse + 未知 voice（検証が先か）",
          {"model": MODEL, "input": "あ。", "voice": "dare", "stream_format": "sse"}, fake=f,
          note="app.py:316-342 は _stream_speech_response より前")
    f = reset()
    shoot("0c stream_format=sse + model 不一致",
          {"model": "x", "input": "あ。", "voice": "none", "stream_format": "sse"}, fake=f)

    # response_format の正規化
    for fmt in [" WAV ", "PCM", "flac", "mp3", "opus", "aac"]:
        f = reset()
        shoot(f"0d response_format={fmt!r}",
              {"model": MODEL, "input": "あ。", "voice": "none", "response_format": fmt}, fake=f)

    # input の長さ境界
    for n, label in ((4096, "4096 字（上限ちょうど）"), (4097, "4097 字（上限超え）")):
        f = reset()
        shoot(f"0e input {label}",
              {"model": MODEL, "input": "あ" * n, "voice": "none",
               "irodori": {"chunking_enabled": False}}, fake=f)

    # トップレベル ref_wav で voice を迂回できるか
    f = reset()
    shoot("0f トップレベル ref_wav（voice 省略）",
          {"model": MODEL, "input": "あ。", "ref_wav": "C:/nowhere/ref.wav"}, fake=f,
          note="サーバ側パス。Fake なので存在検査は走らない")

    # 複数チャンクの messages が積み上がるか
    reset()
    fake = FakeRuntime(messages=["warning: SilentCipher watermark is unavailable; skipping."])
    main.runtime_manager = FakeRuntimeManager(fake)
    shoot("0g 複数チャンクの X-Irodori-Messages（連結される）",
          {"model": MODEL, "input": TEXT3, "voice": "none",
           "irodori": {"chunk_min_chars": 10}}, fake=fake)

    # chunking_enabled=false + SSE
    f = reset()
    shoot("0h SSE + chunking_enabled=false（長文でも 1 イベント）",
          {"model": MODEL, "input": TEXT3, "voice": "none", "stream_format": "sse",
           "irodori": {"chunking_enabled": False}}, fake=f)

    # num_candidates>1 はそのまま渡る（選択はランタイム側）
    f = reset()
    shoot("0i irodori.num_candidates=4",
          {"model": MODEL, "input": "あ。", "voice": "none", "irodori": {"num_candidates": 4}}, fake=f)

    # speaker_uncond_mode を送っても届かない（サーバが渡さない唯一の欄）
    f = reset()
    shoot("0j speaker_uncond_mode を送る（無視されるはず）",
          {"model": MODEL, "input": "あ。", "voice": "none",
           "speaker_uncond_mode": "zero", "irodori": {"speaker_uncond_mode": "zero"}}, fake=f)


# ------------------------------- 追補2（同時 2 本の配車：SSE と非ストリームの差）
def qc_concurrency() -> None:
    import asyncio
    import time as _time

    import httpx

    order: list[str] = []

    class TracingRuntime(FakeRuntime):
        def synthesize(self, req, *, log_fn=None):
            order.append(req.text)
            _time.sleep(0.05)          # 合成 1 回ぶんの見立て（ワーカースレッド内）
            return super().synthesize(req, log_fn=log_fn)

    async def fire(stream: bool) -> list[str]:
        order.clear()
        reset()
        main.runtime_manager = FakeRuntimeManager(TracingRuntime())
        transport = httpx.ASGITransport(app=main.app)
        async with httpx.AsyncClient(transport=transport, base_url="http://t") as client:
            def body(tag: str) -> dict:
                payload = {
                    "model": MODEL,
                    "input": f"{tag}1です。{tag}2です。{tag}3です。",
                    "voice": "none",
                    "irodori": {"chunk_min_chars": 5},
                }
                if stream:
                    payload["stream_format"] = "sse"
                return payload

            await asyncio.gather(
                client.post("/v1/audio/speech", json=body("A")),
                client.post("/v1/audio/speech", json=body("B")),
            )
        return list(order)

    sse_order = asyncio.run(fire(True))
    plain_order = asyncio.run(fire(False))
    record("C 同時 2 本の合成順（max_concurrent_synthesis=1）", {
        "sse_order": sse_order,
        "non_stream_order": plain_order,
    }, note="SSE はチャンクごとにスロットを取り直す（app.py:711-719）／非ストリームは 1 要求 1 スロット（app.py:356-366）")


# ----------------------------------------- 追補3（None と False の優先順の罠）
def qp_precedence() -> None:
    f = reset()
    shoot("P1 irodori.no_ref=false + トップレベル no_ref=true",
          {"model": MODEL, "input": "あ。", "irodori": {"no_ref": False}, "no_ref": True}, fake=f,
          note="_coalesce は None だけを飛ばす＝ネストの false が勝つ")

    f = reset()
    shoot("P2 irodori.chunking_enabled=false + トップレベル chunking=true",
          {"model": MODEL, "input": TEXT3, "voice": "none", "chunking": True,
           "irodori": {"chunking_enabled": False, "chunk_min_chars": 5}}, fake=f)

    f = reset()
    shoot("P3 irodori.num_steps=null（明示 null）+ トップレベル num_steps=7",
          {"model": MODEL, "input": "あ。", "voice": "none", "num_steps": 7,
           "irodori": {"num_steps": None}}, fake=f,
          note="明示 null は None＝_coalesce が飛ばす＝トップレベルが勝つ")

    f = reset()
    shoot("P4 irodori.caption=null + トップレベル caption",
          {"model": MODEL, "input": "あ。", "voice": "none", "caption": "元気に",
           "irodori": {"caption": None}}, fake=f)

    f = reset()
    shoot("P5 voice={'path': ...}（id 以外の鍵）",
          {"model": MODEL, "input": "あ。", "voice": {"path": "C:/x.wav"}}, fake=f)

    f = reset()
    shoot("P6 voice={'id': 'none', 'ref_wav': 'C:/x.wav'}（余分な鍵は無視されるか）",
          {"model": MODEL, "input": "あ。", "voice": {"id": "none", "ref_wav": "C:/x.wav"}}, fake=f)


def main_() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=str(LAB / "out" / "server_contract.json"))
    parser.add_argument("--only", default="")
    args = parser.parse_args()

    steps = {
        "0": q0_extra,
        "C": qc_concurrency,
        "P": qp_precedence,
        "1": q1_voice_omission, "2": q2_flat_aliases, "3": q3_sse, "4": q4_split,
        "5": q5_speed_seconds, "6": q6_device, "7": q7_health, "8": q8_errors, "9": q9_auth,
    }
    keys = list(args.only) if args.only else list(steps)
    for key in keys:
        print(f"=== 問い {key} ===", flush=True)
        steps[key]()

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(REPORT, ensure_ascii=False, indent=2, default=str),
                   encoding="utf-8")
    print(f"\nwrote {out} ({len(REPORT)} entries)")


if __name__ == "__main__":
    sys.exit(main_())
