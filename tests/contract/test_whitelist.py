"""A-5: 未知欄 400 ・ Literal 3 欄はネスト必須 ・ wav 以外の response_format は 400.

The upstream drops unknown fields silently (``extra="allow"`` on both
``SpeechRequest`` and ``IrodoriOptions``) and passes a flat
``t_schedule_mode:"zigzag"`` straight through to the runtime -- survey cases
S-1 and S-2 in ``research/lab/notes/37-handoff-contract.md``.  These tests are
the re-shots that must now come back 400.
"""

from __future__ import annotations

from conftest import DEFAULT_VOICE, speech_body


def test_unknown_top_level_field_is_400(client):
    response = client.post("/v1/audio/speech", json=speech_body(num_stepz=10))
    assert response.status_code == 400
    error = response.json()["error"]
    assert error["code"] == "ywk_unknown_field"
    assert error["message"] == "unknown field: num_stepz"


def test_unknown_nested_field_is_400(client):
    response = client.post("/v1/audio/speech", json=speech_body(irodori={"num_stepz": 10}))
    assert response.status_code == 400
    error = response.json()["error"]
    assert error["code"] == "ywk_unknown_field"
    assert error["message"] == "unknown field: irodori.num_stepz"


def test_literal_field_at_top_level_is_400(client):
    """S-2: flat Literal fields used to be accepted and passed through."""
    response = client.post("/v1/audio/speech", json=speech_body(t_schedule_mode="zigzag"))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_literal_top_level"


def test_literal_field_nested_with_bad_value_is_400(client):
    response = client.post(
        "/v1/audio/speech", json=speech_body(irodori={"t_schedule_mode": "zigzag"})
    )
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_invalid_enum"


def test_literal_field_nested_with_good_value_reaches_the_runtime(client, baseline):
    response = client.post(
        "/v1/audio/speech", json=speech_body(irodori={"t_schedule_mode": "sway"})
    )
    assert response.status_code == 200
    assert baseline.requests[-1].t_schedule_mode == "sway"


def test_non_wav_response_format_is_400(client):
    """The distribution ships no ffmpeg, so mp3/opus/aac must not be offered."""
    response = client.post("/v1/audio/speech", json=speech_body(response_format="mp3"))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_unsupported_response_format"


def test_wav_response_format_is_accepted(client):
    response = client.post("/v1/audio/speech", json=speech_body(response_format="wav"))
    assert response.status_code == 200
    assert response.content[:4] == b"RIFF"


def test_wrong_type_is_400(client):
    response = client.post("/v1/audio/speech", json=speech_body(irodori={"num_steps": "40"}))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_type_error"


def test_boolean_is_not_accepted_as_an_integer(client):
    response = client.post("/v1/audio/speech", json=speech_body(irodori={"num_steps": True}))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_type_error"


def test_nested_null_means_unset(client, baseline):
    """T-2: a nested ``null`` loses to the env default; it must not 400."""
    response = client.post("/v1/audio/speech", json=speech_body(irodori={"num_steps": None}))
    assert response.status_code == 200
    assert baseline.requests[-1].num_steps == 40


def test_priority_nested_beats_top_level(client, baseline):
    """The upstream priority (irodori.X > top-level X > env) is preserved."""
    body = speech_body(num_steps=8, irodori={"num_steps": 12})
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 200
    assert baseline.requests[-1].num_steps == 12


def test_top_level_beats_env(client, baseline):
    response = client.post("/v1/audio/speech", json=speech_body(num_steps=8))
    assert response.status_code == 200
    assert baseline.requests[-1].num_steps == 8


def test_body_that_is_not_an_object_is_400(client):
    response = client.post("/v1/audio/speech", content=b"[1,2,3]")
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_invalid_body"


def test_irodori_that_is_not_an_object_is_400(client):
    response = client.post("/v1/audio/speech", json=speech_body(irodori=[1, 2]))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_type_error"


def test_default_voice_still_synthesizes(client, baseline):
    response = client.post("/v1/audio/speech", json=speech_body(voice=DEFAULT_VOICE))
    assert response.status_code == 200
    assert baseline.requests[-1].no_ref is True


def test_nested_null_means_unset_for_every_explicit_option_field(client, baseline):
    """The three fields where the upstream reads an explicit ``null`` as "given".

    ``_explicit_option`` (app.py:1089-1095) consults ``model_fields_set``, so
    ``{"ref_normalize_db": null}`` arrives as "the caller asked for None" and
    beats the env default -- silently switching off the reference loudness
    normalisation, i.e. changing the audio.  The other 41 fields go through
    ``_coalesce``, where ``null`` loses.  ``/params.rules.null_means_unset``
    only holds because the wrapper deletes these three.
    """
    from irodori_openai_tts import app as upstream

    upstream.settings.default_ref_normalize_db = -16.0
    upstream.settings.default_max_ref_seconds = 30.0
    upstream.settings.default_first_sentence_chunk_min_chars = 12

    omitted = client.post("/v1/audio/speech", json=speech_body())
    assert omitted.status_code == 200, omitted.text
    baseline_request = baseline.requests[-1]

    nulls = {
        "ref_normalize_db": None,
        "max_ref_seconds": None,
        "first_sentence_chunk_min_chars": None,
    }
    with_nulls = client.post("/v1/audio/speech", json=speech_body(irodori=dict(nulls)))
    assert with_nulls.status_code == 200, with_nulls.text
    sent = baseline.requests[-1]

    assert sent.ref_normalize_db == baseline_request.ref_normalize_db == -16.0
    assert sent.max_ref_seconds == baseline_request.max_ref_seconds == 30.0


def test_top_level_null_means_unset_for_the_same_three(client, baseline):
    from irodori_openai_tts import app as upstream

    upstream.settings.default_ref_normalize_db = -16.0
    response = client.post("/v1/audio/speech", json=speech_body(ref_normalize_db=None))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].ref_normalize_db == -16.0


def test_a_real_value_still_wins_over_the_env_default(client, baseline):
    """The fold must not swallow a value the caller actually meant."""
    from irodori_openai_tts import app as upstream

    upstream.settings.default_ref_normalize_db = -16.0
    response = client.post(
        "/v1/audio/speech", json=speech_body(irodori={"ref_normalize_db": -24.0})
    )
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].ref_normalize_db == -24.0


def test_empty_caption_is_folded_not_rejected(client, baseline):
    """``/params`` reports ``caption`` default ``""``; it must round-trip."""
    response = client.post("/v1/audio/speech", json=speech_body(irodori={"caption": "   "}))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].caption is None


def test_empty_seed_is_folded_not_rejected(client, baseline):
    response = client.post("/v1/audio/speech", json=speech_body(irodori={"seed": ""}))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].seed is None


def test_num_steps_100_is_accepted(client, baseline):
    """設計書 §4-2 ⑶: the range is gradio's 1..120, not a narrower invention."""
    response = client.post("/v1/audio/speech", json=speech_body(irodori={"num_steps": 100}))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].num_steps == 100
