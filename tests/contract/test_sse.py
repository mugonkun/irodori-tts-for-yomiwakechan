"""decisions.md 47 / contract ⑶ 3-5: the SSE frame, taken as it is.

``stream_format:"sse"`` is an upstream feature the distribution refuses to
kill, and the 本体 does not use it -- so what the contract owes is the *shape*
of the frames, copied from ``app.py`` rather than guessed, plus the one place
where the wrapper has to interfere: an SSE stream is a **200**, so the error
middleware never sees it, yet the ``event: error`` frames carry the same
``str(exc)`` -- and the same absolute paths -- as the non-streaming 400/500.

Frame source of fact:

* ``_sse_event`` (app.py:787-789)   -- ``event: <name>\\ndata: <json>\\n\\n``
* ``audio_chunk`` (app.py:734-744)  -- the seven data fields asserted below
* ``done`` (app.py:769)             -- ``{"chunks": n}``
* ``_sse_error_event`` (app.py:792-810) -- ``{"error": {message,type,param,code}}``
* ``_stream_speech_response`` (app.py:770-774) -- media type and headers
"""

from __future__ import annotations

import base64
import json
import re

from conftest import VOICES, speech_body

#: Same grep as ``test_error_bodies``: a Windows drive path either way round.
ABSOLUTE_PATH = re.compile(r"[A-Za-z]:[\\/]")


def _frames(text: str) -> list[tuple[str, dict]]:
    """Split an SSE body into ``(event, data)`` pairs."""
    out: list[tuple[str, dict]] = []
    for block in text.split("\n\n"):
        block = block.strip("\n")
        if not block:
            continue
        lines = block.split("\n")
        assert lines[0].startswith("event: "), block
        assert lines[1].startswith("data: "), block
        out.append((lines[0][len("event: ") :], json.loads(lines[1][len("data: ") :])))
    return out


def test_sse_answers_200_with_the_event_stream_media_type(client):
    response = client.post("/v1/audio/speech", json=speech_body(stream_format="sse"))
    assert response.status_code == 200, response.text
    assert response.headers["content-type"].startswith("text/event-stream")
    assert response.headers["cache-control"] == "no-cache"
    assert response.headers["x-accel-buffering"] == "no"


def test_sse_audio_chunk_and_done_carry_the_upstream_field_names(client):
    response = client.post("/v1/audio/speech", json=speech_body(stream_format="sse"))
    assert response.status_code == 200, response.text
    frames = _frames(response.text)

    assert [name for name, _ in frames] == ["audio_chunk", "done"]

    name, data = frames[0]
    assert set(data) == {
        "index",
        "text",
        "format",
        "media_type",
        "audio_base64",
        "seed",
        "total_to_decode",
    }
    assert data["index"] == 0
    assert data["format"] == "wav"
    assert data["media_type"] == "audio/wav"
    # Each chunk is a complete wav, not a slice of one (app.py:719-724).
    assert base64.b64decode(data["audio_base64"])[:4] == b"RIFF"

    assert frames[1][1] == {"chunks": 1}


def test_sse_error_frame_carries_no_absolute_path(client, baseline):
    """The frame rides inside a 200, so ``ywk_scrub_errors`` never sees it.

    ``_scrub_sse`` is what keeps contract ⑶ 3-3's "0 absolute paths" true on
    the streaming road as well.
    """
    leak = str(VOICES / "alice.wav")

    def boom(req, *, log_fn=None):
        raise ValueError(f"could not read {leak}: no such file")

    baseline.synthesize = boom

    response = client.post("/v1/audio/speech", json=speech_body(stream_format="sse"))
    assert response.status_code == 200, response.text
    text = response.text
    assert ABSOLUTE_PATH.search(text) is None, f"absolute path in an SSE frame: {text}"
    assert str(VOICES) not in text
    assert "<path>" in text

    frames = _frames(text)
    assert [name for name, _ in frames] == ["error"]
    error = frames[0][1]["error"]
    assert set(error) == {"message", "type", "param", "code"}
    # Upstream's own code, not a ``ywk_`` one: the middleware rewrites 4xx/5xx
    # bodies and a 200 is neither (contract ⑶ 3-5).
    assert error["code"] == "invalid_request"


def test_sse_error_frame_ends_the_stream(client, baseline):
    """``done`` never follows an ``error`` frame: upstream returns."""

    def boom(req, *, log_fn=None):
        raise ValueError("nope")

    baseline.synthesize = boom
    response = client.post("/v1/audio/speech", json=speech_body(stream_format="sse"))
    assert [name for name, _ in _frames(response.text)] == ["error"]


def test_stream_format_other_than_sse_is_a_400(client):
    response = client.post("/v1/audio/speech", json=speech_body(stream_format="ndjson"))
    assert response.status_code == 400
    assert response.json()["error"]["code"].startswith("ywk_")


def test_params_declares_the_sse_enum(client):
    stream = client.get("/params").json()["request"]["stream_format"]
    assert stream["enum"] == ["sse"]
