"""A-5: ``voice`` と ``no_ref`` の同時指定は 400.

Upstream resolves the two in ``_resolve_voice`` (app.py:418-459): an explicit
``no_ref`` wins and ``voice`` is silently discarded -- the caller gets a
different voice than it asked for, with a 200.  The distribution refuses the
combination instead and points at ``voice="デフォルト"``.
"""

from __future__ import annotations

import pytest

from conftest import DEFAULT_VOICE, speech_body


def test_voice_and_nested_no_ref_is_400(client):
    body = speech_body(voice=DEFAULT_VOICE, irodori={"no_ref": True})
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 400
    error = response.json()["error"]
    assert error["code"] == "ywk_voice_and_no_ref"
    assert error["param"] == "voice"


def test_voice_and_top_level_no_ref_is_400(client):
    body = speech_body(voice=DEFAULT_VOICE, no_ref=True)
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_voice_and_no_ref"


def test_voice_and_no_ref_false_is_still_400(client):
    """``no_ref: false`` is still a decision about the reference path."""
    body = speech_body(voice=DEFAULT_VOICE, irodori={"no_ref": False})
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_voice_and_no_ref"


def test_no_ref_alone_is_accepted(client, baseline):
    body = {"model": "irodori-tts", "input": "テスト。", "irodori": {"no_ref": True}}
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is True


def test_voice_alone_is_accepted(client, baseline):
    response = client.post("/v1/audio/speech", json=speech_body(voice=DEFAULT_VOICE))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is True


def test_empty_voice_with_no_ref_is_not_a_conflict(client, baseline):
    body = speech_body(voice="", irodori={"no_ref": True})
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is True


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("ref_wav", "other.wav"),
        ("ref_wavs", ["other.wav"]),
        ("ref_latent", "other.pt"),
        ("ref_latents", ["other.pt"]),
        ("ref_embed", "other.speaker.safetensors"),
    ],
)
def test_voice_and_a_reference_field_is_400(client, field, value):
    """app.py:437-454: any one reference field makes ``voice`` unread.

    The caller gets a 200 spoken by whatever the reference points at, and the
    speaker it chose is dropped without a word -- the "沈黙" the distribution
    exists to remove (contract ⑷ 4-2, acceptance「未知欄…沈黙 0 件」).
    """
    body = speech_body(voice="ずんだもん", irodori={field: value})
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 400, response.text
    error = response.json()["error"]
    assert error["code"] == "ywk_voice_and_reference"
    assert error["param"] == f"irodori.{field}"


@pytest.mark.parametrize("field", ["ref_wav", "ref_latent", "ref_embed"])
def test_voice_and_a_top_level_reference_field_is_400(client, field):
    body = speech_body(voice="ずんだもん", **{field: "other.wav"})
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 400, response.text
    assert response.json()["error"]["code"] == "ywk_voice_and_reference"


@pytest.mark.parametrize("field", ["ref_wav", "ref_latent", "ref_embed"])
def test_a_null_reference_field_is_not_a_conflict(client, field):
    """``null`` there is exactly what an omitted field looks like upstream."""
    body = speech_body(voice=DEFAULT_VOICE, irodori={field: None})
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 200, response.text


def test_a_reference_field_without_voice_still_works(client, baseline):
    body = {"model": "irodori-tts", "input": "テスト。", "irodori": {"ref_wav": "other.wav"}}
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].ref_wav == "other.wav"
