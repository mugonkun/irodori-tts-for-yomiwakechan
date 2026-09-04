"""Contract-test scaffolding.

The runtime is never loaded: ``FakeRuntimeManager`` / ``FakeRuntime`` stand in
for it, ported from ``research/lab/bin/37_contract.py`` (:39-61) and
``research/lab/bin/38_naming.py`` (:34-56).  The upstream trees are read-only
imports on ``sys.path``; nothing is installed with ``-e`` and no bytecode is
written into them (``PYTHONDONTWRITEBYTECODE`` below, plus
``-p no:cacheprovider`` in ``build/run-tests.ps1``).

Every environment variable the wrapper reads is set *before* ``ywk_server`` is
imported, because ``irodori_openai_tts.config.get_settings`` is ``lru_cache``'d
at upstream import time.
"""

from __future__ import annotations

import atexit
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any

import pytest

ROOT = Path(__file__).resolve().parents[1]
UPSTREAM_CORE = ROOT / "upstream" / "Irodori-TTS"
UPSTREAM_SERVER = ROOT / "upstream" / "Irodori-TTS-Server" / "src"
SERVER = ROOT / "server"

for entry in (Path(__file__).resolve().parent, SERVER, UPSTREAM_SERVER, UPSTREAM_CORE):
    if str(entry) not in sys.path:
        sys.path.insert(0, str(entry))

os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
sys.dont_write_bytecode = True

# A throwaway data root outside the repository.  ``YWK_DATA_DIR`` is the
# wrapper's own override hook, so this also exercises it.
TMP = Path(tempfile.mkdtemp(prefix="ywk-contract-"))
VOICES = TMP / "voices"
VOICES.mkdir(parents=True, exist_ok=True)
atexit.register(shutil.rmtree, TMP, True)

os.environ["YWK_DATA_DIR"] = str(TMP)
os.environ["IRODORI_VOICES_DIR"] = str(VOICES)
os.environ["IRODORI_VOICE_ALIASES_FILE"] = str(VOICES / "voices.json")
os.environ["HF_HOME"] = str(TMP / "models")
# Without this the TestClient lifespan would try to download a 3 GB checkpoint.
os.environ["IRODORI_PRELOAD"] = "false"
os.environ["IRODORI_MODEL_DEVICE"] = "cpu"
os.environ["IRODORI_CODEC_DEVICE"] = "cpu"
os.environ.pop("IRODORI_API_KEY", None)
os.environ.pop("IRODORI_DEFAULT_VOICE", None)

import torch  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

from irodori_openai_tts import app as upstream  # noqa: E402
from irodori_openai_tts.voices import VoiceRegistry  # noqa: E402
from irodori_tts.inference_runtime import SamplingResult  # noqa: E402

import ywk_server  # noqa: E402
from pydantic import BaseModel  # noqa: E402


class _ProbeBody(BaseModel):
    file: str


@ywk_server.app.post("/ywk/_contract_test/probe")
def _contract_test_probe(payload: _ProbeBody) -> dict:
    """A route that exists only to fire ``RequestValidationError``.

    422 is one of the five error shapes the contract names (⑶ 3-3), but after
    the wrapper is done no *product* route can raise it any more: ``POST
    /v1/audio/speech`` reads the raw body so the whitelist runs before pydantic,
    and the three ``/v1/audio/voices`` write ports are dropped (contract ⑷ 4-3).
    This probe is mounted by the test scaffolding, never by ``ywk_server``.
    """
    return {"ok": payload.file}


MODEL = "irodori-tts"
DEFAULT_VOICE = "デフォルト"
JA_VOICE = "ずんだもん"
JA_FILE_VOICE = "四国めたん"


class FakeRuntime:
    """Records the ``SamplingRequest`` it is handed and returns silence."""

    def __init__(self) -> None:
        self.requests: list[Any] = []
        self.model_device = torch.device("cpu")
        self.codec_device = torch.device("cpu")
        self.default_text_max_len = 256
        self.default_caption_max_len = 256
        self.default_max_ref_seconds = 120.0

    def synthesize(self, req: Any, *, log_fn: Any = None) -> SamplingResult:
        self.requests.append(req)
        audio = torch.zeros(1, 100)
        return SamplingResult(
            audio=audio,
            audios=[audio],
            sample_rate=16000,
            stage_timings=[],
            total_to_decode=0.1,
            used_seed=1 if req.seed is None else int(req.seed),
            messages=[],
        )


class FakeRuntimeManager:
    def __init__(self, runtime: FakeRuntime | None = None) -> None:
        self.runtime = runtime
        self._runtime = runtime
        self.checkpoint_path = None
        self.is_loaded = runtime is not None
        self.is_loading = False

    def get(self) -> FakeRuntime:
        if self.runtime is None:
            raise RuntimeError("runtime is not loaded")
        return self.runtime


#: The settings the distribution bakes in (design §2), reasserted per test so
#: one test cannot leak into the next.
BASELINE = {
    "api_key": None,
    "model_name": MODEL,
    "voices_dir": VOICES,
    "voice_aliases_file": VOICES / "voices.json",
    "default_voice": None,
    "allow_no_ref_voice": False,
    "preload": False,
    "hf_checkpoint": "Aratako/Irodori-TTS-v4.1-Small",
    "model_device": "cpu",
    "codec_device": "cpu",
    "model_precision": "fp32",
    "codec_precision": "fp32",
    "host": "127.0.0.1",
    "port": 18088,
    "empty_cache_interval": 0,
    "max_concurrent_synthesis": 1,
    "synthesis_wait_timeout": 60.0,
    "model_load_timeout": 60.0,
    "default_response_format": "wav",
    "default_num_steps": 40,
    "default_chunking_enabled": False,
    "default_chunk_min_chars": 80,
    "default_first_sentence_chunk_min_chars": None,
}


def write_voices(aliases: dict[str, Any] | None = None, files: tuple[str, ...] = ()) -> None:
    for path in VOICES.iterdir():
        if path.is_file():
            path.unlink()
    for name in files:
        (VOICES / name).write_bytes(b"RIFF\x00\x00\x00\x00WAVE")
    if aliases is not None:
        (VOICES / "voices.json").write_text(
            json.dumps(aliases, ensure_ascii=False), encoding="utf-8"
        )


@pytest.fixture(autouse=True)
def baseline() -> Any:
    """Reset settings, voices and the fake runtime before every test."""
    for key, value in BASELINE.items():
        setattr(upstream.settings, key, value)
    upstream.voice_registry = VoiceRegistry(upstream.settings)
    upstream._synthesis_semaphore = None
    upstream._synthesis_semaphore_limit = None
    ywk_server._runtime_error = None
    ywk_server._device_logged = False
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}})
    fake = FakeRuntime()
    upstream.runtime_manager = FakeRuntimeManager(fake)
    yield fake


@pytest.fixture()
def unloaded() -> Any:
    """A runtime manager that reports "not loaded" (design §4-2 / §4-3)."""
    upstream.runtime_manager = FakeRuntimeManager(None)
    return upstream.runtime_manager


@pytest.fixture()
def client() -> Any:
    with TestClient(ywk_server.app, raise_server_exceptions=False) as test_client:
        yield test_client


@pytest.fixture()
def ywk() -> Any:
    return ywk_server


def speech_body(**overrides: Any) -> dict[str, Any]:
    body: dict[str, Any] = {"model": MODEL, "input": "テスト。", "voice": DEFAULT_VOICE}
    body.update(overrides)
    return body
