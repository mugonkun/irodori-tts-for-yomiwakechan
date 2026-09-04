"""decisions.md 45: ``IRODORI_DEFAULT_VOICE`` is baked to 「デフォルト」.

``/params`` reports ``request.voice`` with ``default: "デフォルト"`` because
design §4-2 ⑵ forbids a ``null`` default on a field the 本体 shows.  A default
the caller cannot send back is a lie, and until this裁定 an omitted ``voice``
hit the upstream's ``No voice was provided and IRODORI_DEFAULT_VOICE is not
set.`` (``voices.py:75-79``) -- a 400.  The wrapper therefore ``setdefault``s
the same id into the env, and folds it into the reference-free request the
alias stands for when ``voices.json`` cannot resolve it.

The launcher still wins: ``setdefault`` never overwrites, and a launcher that
names a real speaker makes *that* the effective default.
"""

from __future__ import annotations

import os

from conftest import DEFAULT_VOICE, JA_VOICE, MODEL, VOICES, write_voices


def _body(**overrides):
    """A speech body with **no** ``voice`` field at all."""
    body = {"model": MODEL, "input": "テスト。"}
    body.update(overrides)
    return body


# --------------------------------------------------------------------------- env


def test_the_baked_env_names_the_reserved_no_ref_speaker(ywk, monkeypatch):
    monkeypatch.delenv("IRODORI_DEFAULT_VOICE", raising=False)
    ywk.apply_env_defaults()
    assert os.environ["IRODORI_DEFAULT_VOICE"] == DEFAULT_VOICE


def test_the_baked_env_matches_the_wrapper_constant(ywk):
    """One spelling of 「デフォルト」, not two."""
    import ywk_params

    assert ywk.DEFAULT_VOICE_ID == ywk_params.DEFAULT_VOICE_ID == DEFAULT_VOICE


def test_the_launcher_can_override_the_baked_default_voice(ywk, monkeypatch):
    """``setdefault``: the launcher's env always wins (design §2)."""
    monkeypatch.setenv("IRODORI_DEFAULT_VOICE", JA_VOICE)
    ywk.apply_env_defaults()
    assert os.environ["IRODORI_DEFAULT_VOICE"] == JA_VOICE


# --------------------------------------------------------------------------- POST


def test_speech_without_voice_is_200_on_the_no_ref_path(client, baseline):
    response = client.post("/v1/audio/speech", json=_body())
    assert response.status_code == 200, response.text
    assert response.content[:4] == b"RIFF"
    assert baseline.requests[-1].no_ref is True
    assert baseline.requests[-1].ref_wav is None


def test_speech_without_voice_is_200_even_with_no_voices_json(client, baseline):
    """First run: the launcher has not written ``voices.json`` yet."""
    write_voices(aliases=None)
    assert (VOICES / "voices.json").exists() is False

    response = client.post("/v1/audio/speech", json=_body())
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is True


def test_speech_without_voice_uses_the_launcher_named_speaker(client, baseline):
    """A launcher that names its own default is not overridden by the wrapper."""
    from irodori_openai_tts import app as upstream

    write_voices(aliases={JA_VOICE: {"no_ref": True}}, files=("alice.wav",))
    upstream.settings.default_voice = JA_VOICE

    response = client.post("/v1/audio/speech", json=_body())
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is True


def test_an_omitted_voice_never_overrides_an_explicit_reference(client, baseline):
    """``_resolve_voice`` short-circuits on a reference (app.py:437-454).

    Folding ``no_ref`` in would silence a request that asked for a reference.
    """
    write_voices(aliases={}, files=("alice.wav",))
    ref = str(VOICES / "alice.wav")
    response = client.post("/v1/audio/speech", json=_body(irodori={"ref_wav": ref}))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is False
    assert baseline.requests[-1].ref_wav == ref


def test_an_omitted_voice_with_no_ref_stays_a_no_ref_request(client, baseline):
    response = client.post("/v1/audio/speech", json=_body(irodori={"no_ref": True}))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is True


def test_an_emptied_default_voice_is_still_the_upstream_400(client):
    """The launcher may empty it; then the upstream's own 400 comes back --
    with the machine-readable ``code`` contract ⑶ 3-3 promises."""
    from irodori_openai_tts import app as upstream

    upstream.settings.default_voice = None
    response = client.post("/v1/audio/speech", json=_body())
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_missing_voice"


# --------------------------------------------------------------------------- /params


def test_params_voice_default_is_the_baked_value_and_not_required(client):
    voice = client.get("/params").json()["request"]["voice"]
    assert voice["default"] == DEFAULT_VOICE
    assert voice["default_source"] == "ywk"
    assert voice["required"] is False
    assert voice["nullable"] is False
    assert "IRODORI_DEFAULT_VOICE" in voice["note"]


def test_params_voice_note_matches_what_the_port_actually_does(client, baseline):
    """The advertised default must be a value the caller can send back."""
    voice = client.get("/params").json()["request"]["voice"]
    response = client.post("/v1/audio/speech", json=_body(voice=voice["default"]))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is True

    omitted = client.post("/v1/audio/speech", json=_body())
    assert omitted.status_code == 200, omitted.text


def test_params_voice_is_required_again_when_the_default_is_emptied(client):
    from irodori_openai_tts import app as upstream

    upstream.settings.default_voice = None
    voice = client.get("/params").json()["request"]["voice"]
    assert voice["required"] is True
    assert voice["default"] == DEFAULT_VOICE
    assert voice["default_source"] == "ywk"


def test_params_voice_follows_a_launcher_named_speaker(client):
    from irodori_openai_tts import app as upstream

    upstream.settings.default_voice = JA_VOICE
    voice = client.get("/params").json()["request"]["voice"]
    assert voice["default"] == JA_VOICE
    assert voice["default_source"] == "settings_optional"
    assert voice["required"] is False
    assert voice["nullable"] is False
