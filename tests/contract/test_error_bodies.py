"""A-5: エラー body に絶対パスが 0 件.

Upstream leaks absolute paths in at least two shapes (survey §5-2): the 400 for
an unknown voice quotes ``settings.voices_dir``, and the 422 quotes the
``app.py`` path plus the entire request body.  Design §4-7 requires the wrapper
to scrub every error body; ``research/lab/notes/37-handoff-contract.md`` §2-5
also asks for a machine-readable ``code`` and a body of at most 512 bytes.

This module walks all five error shapes the contract names -- 400, 422, 500,
503 and 404 -- plus the two upstream leaks, and greps each body.
"""

from __future__ import annotations

import json
import re

import pytest

from conftest import VOICES, speech_body, write_voices

#: The grep the acceptance condition names: a Windows drive path in either
#: slash direction.  ``\\`` covers the JSON-escaped form.
ABSOLUTE_PATH = re.compile(r"[A-Za-z]:[\\/]")


def _assert_clean(response) -> dict:
    text = response.text
    assert ABSOLUTE_PATH.search(text) is None, f"absolute path in error body: {text}"
    assert str(VOICES) not in text
    assert len(response.content) <= 512, f"error body is {len(response.content)} bytes: {text}"
    payload = json.loads(text)
    return payload


def test_400_unknown_voice_does_not_leak_the_voices_dir(client):
    """Upstream: ``Unknown voice=... Put a reference audio file in <abspath>``."""
    response = client.post("/v1/audio/speech", json=speech_body(voice="いない人"))
    assert response.status_code == 400
    payload = _assert_clean(response)
    assert "<path>" in payload["error"]["message"]


def test_400_from_the_wrapper_is_clean(client):
    response = client.post("/v1/audio/speech", json=speech_body(num_stepz=1))
    assert response.status_code == 400
    payload = _assert_clean(response)
    assert payload["error"]["code"] == "ywk_unknown_field"


def test_422_is_replaced_and_clean(client):
    """The probe route in conftest.py fires ``RequestValidationError``."""
    response = client.post("/ywk/_contract_test/probe", json={})
    assert response.status_code == 422
    payload = _assert_clean(response)
    assert payload["error"]["code"] == "ywk_validation_error"
    assert "file" in payload["error"]["message"]


def test_500_is_clean(client, baseline):
    def boom(req, *, log_fn=None):
        raise RuntimeError(r"exploded while reading C:\Users\someone\models\weights.safetensors")

    baseline.synthesize = boom
    response = client.post("/v1/audio/speech", json=speech_body())
    assert response.status_code == 500
    payload = _assert_clean(response)
    assert payload["error"]["type"] == "server_error"
    assert payload["error"]["code"] == "ywk_server_error"


def test_503_is_clean(client):
    from irodori_openai_tts import app as upstream
    from irodori_openai_tts.runtime import RuntimeLoadTimeoutError

    class Stalled:
        is_loaded = False
        is_loading = True
        checkpoint_path = None

        def get(self):
            raise RuntimeLoadTimeoutError(
                r"Model is still loading from C:\Users\someone\.cache\huggingface"
            )

    upstream.runtime_manager = Stalled()
    response = client.post("/v1/audio/speech", json=speech_body())
    assert response.status_code == 503
    _assert_clean(response)


def test_404_is_clean(client):
    response = client.get("/no/such/route")
    assert response.status_code == 404
    _assert_clean(response)


def test_voice_alias_error_is_not_a_500(client):
    """contract ⑵/⑼: a broken ``voices.json`` is "0 件＋理由", never a 500.

    ``_load_aliases`` (voices.py:196-201) raises ``ValueError`` with the absolute
    path of the file in the message.  D-8 -- "the 本体 cannot tell a broken
    ledger from a healthy one" -- disappears because the distribution answers
    with the guaranteed speaker and says why the rest is missing.
    """
    write_voices(aliases=None)
    (VOICES / "voices.json").write_text("[]", encoding="utf-8")

    response = client.get("/v1/audio/voices")
    assert response.status_code == 200, response.text
    assert ABSOLUTE_PATH.search(response.text) is None, response.text
    assert [item["id"] for item in response.json()["data"]] == ["デフォルト"]

    ywk = client.get("/ywk/voices")
    assert ywk.status_code == 200
    assert ywk.json()["count"] == 1
    assert ywk.json()["error"]
    assert ABSOLUTE_PATH.search(ywk.text) is None, ywk.text

    status = client.get("/ywk/status")
    assert status.status_code == 200
    assert status.json()["voices"]["error"]


@pytest.mark.parametrize(
    ("body", "code"),
    [
        ({"model": "wrong-model", "input": "テスト。", "voice": "デフォルト"}, "ywk_unknown_model"),
        ({"model": "irodori-tts", "input": "   ", "voice": "デフォルト"}, "ywk_empty_input"),
        ({"model": "irodori-tts", "input": "テスト。", "voice": "いない人"}, "ywk_unknown_voice"),
    ],
)
def test_upstream_400s_are_clean_and_carry_a_code(client, body, code):
    """contract ⑶ 3-3: every shape carries a machine-readable ``code``.

    The upstream hard-codes ``"code": None`` (app.py:1156-1167), which is what
    forces the 本体's ``IsMissingVoiceFailure`` to match on prose (⑼ D-5).
    """
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 400
    payload = _assert_clean(response)
    assert payload["error"]["code"] == code


def test_upstream_404_carries_a_code(client):
    payload = _assert_clean(client.get("/no/such/route"))
    assert payload["error"]["code"] == "ywk_not_found"


def test_dropped_write_ports_carry_a_code(client):
    """The three voice write ports are gone; what is left must still be clean."""
    for method, path in (
        ("post", "/v1/audio/voices"),
        ("put", "/v1/audio/voices/x"),
        ("delete", "/v1/audio/voices/x"),
    ):
        response = getattr(client, method)(path)
        assert response.status_code in (404, 405), (method, path, response.status_code)
        payload = _assert_clean(response)
        assert payload["error"]["code"].startswith("ywk_")


def test_503_carries_a_code(client):
    from irodori_openai_tts import app as upstream
    from irodori_openai_tts.runtime import RuntimeLoadTimeoutError

    class Stalled:
        is_loaded = False
        is_loading = True
        checkpoint_path = None

        def get(self):
            raise RuntimeLoadTimeoutError("Model is still loading")

    upstream.runtime_manager = Stalled()
    response = client.post("/v1/audio/speech", json=speech_body())
    assert response.status_code == 503
    payload = _assert_clean(response)
    assert payload["error"]["code"] == "ywk_runtime_unavailable"


def test_sse_error_frame_is_clean(client, baseline):
    """The 5th error shape: ``event: error`` inside a 200 SSE stream.

    The middleware only looks at status >= 400, so this frame would otherwise
    carry ``str(exc)`` -- absolute path and all -- straight to the caller.
    """

    def boom(req, *, log_fn=None):
        raise ValueError(r"reference missing: C:\Users\someone\voices\a.wav")

    baseline.synthesize = boom
    response = client.post("/v1/audio/speech", json=speech_body(stream_format="sse"))
    assert response.status_code == 200
    text = response.text
    assert "event: error" in text
    assert ABSOLUTE_PATH.search(text) is None, text
    assert "<path>" in text


def test_sse_audio_frame_survives_whole(client, baseline):
    """Scrubbing must not truncate or mangle the base64 audio frames."""
    import base64

    response = client.post("/v1/audio/speech", json=speech_body(stream_format="sse"))
    assert response.status_code == 200
    frames = [
        block
        for block in response.text.split("\n\n")
        if block.startswith("event: audio_chunk")
    ]
    assert len(frames) == 1
    payload = json.loads(frames[0].split("data: ", 1)[1])
    assert base64.b64decode(payload["audio_base64"])[:4] == b"RIFF"


@pytest.mark.parametrize("folder", ["Taro Yamada", "山田 太郎", "adv 0r5_toml"])
def test_unknown_voice_leaks_no_segment_of_a_spaced_or_japanese_path(client, tmp_path, folder):
    """The grep for ``:\\``/``:/`` is not enough on its own.

    ``str(KeyError(...))`` re-quotes its argument, so the path arrives with
    every backslash doubled: the plain prefix match misses it and a
    ``[^\\s]``-based pattern stops at the space in ``Taro Yamada``, leaving
    ``Yamada`` in the body while the acceptance grep still passes.  Assert on the
    path *segments* instead.
    """
    from pathlib import Path

    from irodori_openai_tts import app as upstream
    from irodori_openai_tts.voices import VoiceRegistry

    root = tmp_path / folder / "voices"
    root.mkdir(parents=True)
    (root / "voices.json").write_text(
        json.dumps({"デフォルト": {"no_ref": True}}, ensure_ascii=False), encoding="utf-8"
    )
    upstream.settings.voices_dir = root
    upstream.settings.voice_aliases_file = root / "voices.json"
    upstream.voice_registry = VoiceRegistry(upstream.settings)

    response = client.post("/v1/audio/speech", json=speech_body(voice="いない人"))
    assert response.status_code == 400
    text = response.text
    assert ABSOLUTE_PATH.search(text) is None, text
    for segment in Path(root).parts:
        segment = segment.strip("\\/")
        if len(segment) < 3 or segment.endswith(":"):
            continue
        assert segment not in text, f"path segment {segment!r} leaked: {text}"


def test_health_carries_no_absolute_path(client):
    """``/health`` is a 200, so the error middleware would skip it.

    Upstream puts ``str(voices_dir)`` and the resolved Hugging Face snapshot
    directory in it (app.py:167-193) -- the operating-system user name, from a
    port with no api_key.  The keys stay upstream's (contract ⑵); the paths go.
    """
    response = client.get("/health")
    assert response.status_code == 200
    assert ABSOLUTE_PATH.search(response.text) is None, response.text
    assert str(VOICES) not in response.text
    body = response.json()
    assert "voices" in body and "model" in body


def test_scrub_text_replaces_paths(ywk):
    scrubbed = ywk.scrub_text(r"open C:\Users\x\voices\a.wav and /home/x/b.wav failed")
    assert ABSOLUTE_PATH.search(scrubbed) is None
    assert "/home/x" not in scrubbed
    assert "<path>" in scrubbed


def test_scrub_text_caps_the_message(ywk):
    scrubbed = ywk.scrub_text("あ" * 5000)
    assert len(scrubbed.encode("utf-8")) <= 320
