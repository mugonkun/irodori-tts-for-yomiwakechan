"""A-5: 「デフォルト」常在・``none`` 非露出・日本語話者名 200・絶対パス 0 件.

Facts the upstream established (``research/lab/notes/38-handoff-decisions.md``
and the 37/38 re-shots): ``GET /v1/audio/voices`` returns the absolute
``ref_wav`` of every voice, sorts by Unicode code point, and injects a
synthetic ``none`` voice whenever ``allow_no_ref_voice`` is true
(voices.py:68-69).  Japanese ids work everywhere except the registration API
(``VOICE_ID_PATTERN`` is ASCII-only, and ``list``/``resolve`` never call it).
"""

from __future__ import annotations

import json

import pytest

from conftest import DEFAULT_VOICE, JA_FILE_VOICE, JA_VOICE, VOICES, speech_body, write_voices


def _ids(client):
    response = client.get("/v1/audio/voices")
    assert response.status_code == 200
    return [item["id"] for item in response.json()["data"]]


def test_default_voice_is_always_first(client):
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "alice.wav"}},
        files=("alice.wav", "zzz.wav"),
    )
    ids = _ids(client)
    assert ids[0] == DEFAULT_VOICE
    assert JA_VOICE in ids


def test_default_voice_is_present_even_with_no_other_voice(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}})
    assert _ids(client) == [DEFAULT_VOICE]


def test_none_is_not_exposed(client):
    """``allow_no_ref_voice=false`` is baked in; ``none`` must never appear."""
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("alice.wav",))
    ids = _ids(client)
    assert "none" not in ids
    assert "no_ref" not in ids


def test_voices_carry_no_absolute_path(client):
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "alice.wav"}},
        files=("alice.wav", f"{JA_FILE_VOICE}.wav"),
    )
    response = client.get("/v1/audio/voices")
    body = response.text
    assert ":\\" not in body and ":/" not in body
    assert str(VOICES) not in body
    for item in response.json()["data"]:
        assert set(item) == {
            "id",
            "object",
            "display_name",
            "preset",
            "no_ref",
            # 裁定 65 added these two; they are booleans, never a path.
            "latent",
            "latent_stale",
        }


def test_default_voice_reports_no_ref(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("alice.wav",))
    by_id = {item["id"]: item for item in client.get("/v1/audio/voices").json()["data"]}
    assert by_id[DEFAULT_VOICE]["no_ref"] is True
    assert by_id["alice"]["no_ref"] is False


def test_display_name_and_preset_come_from_the_ywk_ledger(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("kotonoha_akane.wav",))
    (VOICES / "voices.ywk.json").write_text(
        json.dumps(
            {"kotonoha_akane": {"display_name": "琴葉茜（関西弁）", "preset": True}},
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )
    by_id = {item["id"]: item for item in client.get("/v1/audio/voices").json()["data"]}
    assert by_id["kotonoha_akane"]["display_name"] == "琴葉茜（関西弁）"
    assert by_id["kotonoha_akane"]["preset"] is True
    assert by_id[DEFAULT_VOICE]["preset"] is False
    assert by_id[DEFAULT_VOICE]["display_name"] == DEFAULT_VOICE


def test_ywk_ledger_in_the_assemble_app_shape_is_read(client):
    """build/assemble-app.ps1 writes {"schema":1,"note":...,"voices":{...}}."""
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("kotonoha_akane.wav",))
    (VOICES / "voices.ywk.json").write_text(
        json.dumps(
            {
                "schema": 1,
                "note": "distributor-side speaker table",
                "voices": {
                    "kotonoha_akane": {"display_name": "琴葉茜（関西弁）", "preset": True}
                },
            },
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )
    by_id = {item["id"]: item for item in client.get("/v1/audio/voices").json()["data"]}
    assert by_id["kotonoha_akane"]["display_name"] == "琴葉茜（関西弁）"
    assert by_id["kotonoha_akane"]["preset"] is True


def test_malformed_ywk_ledger_is_not_an_error(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("alice.wav",))
    (VOICES / "voices.ywk.json").write_text("{ not json", encoding="utf-8")
    response = client.get("/v1/audio/voices")
    assert response.status_code == 200
    by_id = {item["id"]: item for item in response.json()["data"]}
    assert by_id["alice"]["display_name"] == "alice"
    assert by_id["alice"]["preset"] is False


def test_missing_ywk_ledger_is_not_an_error(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("alice.wav",))
    assert (VOICES / "voices.ywk.json").exists() is False
    assert client.get("/v1/audio/voices").status_code == 200


def test_japanese_alias_synthesizes(client, baseline):
    """"日本語話者名 200" -- via ``voices.json`` alias."""
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "alice.wav"}},
        files=("alice.wav",),
    )
    response = client.post("/v1/audio/speech", json=speech_body(voice=JA_VOICE))
    assert response.status_code == 200, response.text
    assert response.content[:4] == b"RIFF"
    assert baseline.requests[-1].ref_wav.endswith("alice.wav")


def test_japanese_file_name_synthesizes(client, baseline):
    """"日本語話者名 200" -- via the directory-scan path (no alias file entry)."""
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}}, files=(f"{JA_FILE_VOICE}.wav",)
    )
    assert JA_FILE_VOICE in _ids(client)
    response = client.post("/v1/audio/speech", json=speech_body(voice=JA_FILE_VOICE))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].ref_wav.endswith(f"{JA_FILE_VOICE}.wav")


def test_ywk_voices_mirrors_the_openai_shaped_list(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("alice.wav",))
    openai_shaped = client.get("/v1/audio/voices").json()
    ywk_shaped = client.get("/ywk/voices").json()
    assert ywk_shaped["data"] == openai_shaped["data"]
    assert ywk_shaped["count"] == len(openai_shaped["data"])
    assert ywk_shaped["no_ref_voice"] == DEFAULT_VOICE


@pytest.mark.parametrize(
    "alias", ["none", "no_ref", "no-ref", "null", "text-only", "None", "NONE", " none "]
)
def test_upstream_no_ref_aliases_are_normalised_to_the_default_voice(client, baseline, alias):
    """設計書 §4-4: the ``NO_REF_IDS`` path must never 400.

    ``voices.py:80`` only honours those ids when ``allow_no_ref_voice`` is true,
    and the distribution bakes it false (so ``none`` stays out of the list --
    ⑷ 4-1).  The 本体's current adapter sends ``voice: "none"`` (⑼ D-4), so the
    wrapper folds every spelling into 「デフォルト」 rather than letting the
    upstream answer "Unknown voice".
    """
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("alice.wav",))
    response = client.post("/v1/audio/speech", json=speech_body(voice=alias))
    assert response.status_code == 200, response.text
    assert response.content[:4] == b"RIFF"
    assert baseline.requests[-1].no_ref is True
    assert baseline.requests[-1].ref_wav is None
    assert "none" not in _ids(client)


def test_a_no_ref_alias_still_conflicts_with_no_ref(client):
    """Normalising must not smuggle the combination past the ⑷ exclusivity."""
    body = speech_body(voice="none", irodori={"no_ref": True})
    response = client.post("/v1/audio/speech", json=body)
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_voice_and_no_ref"


def test_default_voice_survives_a_missing_voices_json(client, baseline):
    """contract ⑷: 「デフォルト」 is the wrapper's promise, not voices.json's.

    ``voices.json`` is written by the launcher (便 D); on a first run it does not
    exist yet, and without this the guaranteed speaker -- and every ``none``
    normalised onto it -- would 400.
    """
    write_voices(aliases=None)
    assert (VOICES / "voices.json").exists() is False

    ids = _ids(client)
    assert ids == [DEFAULT_VOICE]
    assert client.get("/ywk/voices").json()["count"] == 1

    response = client.post("/v1/audio/speech", json=speech_body(voice=DEFAULT_VOICE))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].no_ref is True


def test_default_voice_is_first_even_when_voices_json_omits_it(client):
    write_voices(aliases={JA_VOICE: {"ref_wav": "alice.wav"}}, files=("alice.wav",))
    ids = _ids(client)
    assert ids[0] == DEFAULT_VOICE
    assert JA_VOICE in ids


def test_the_upstream_voice_write_ports_are_gone(client):
    """contract ⑷ 4-3 / ⑼ D-3: only the distribution writes voices/.

    The port carries no api_key (decisions.md 31), so a registered POST is a
    writable surface for anything running in the user's browser -- multipart is
    a CORS simple request and needs no preflight.
    """
    assert client.post(
        "/v1/audio/voices", files={"file": ("x.wav", b"RIFF")}, data={"voice_id": "drive_by"}
    ).status_code in (404, 405)
    assert client.put("/v1/audio/voices/drive_by", files={"file": ("x.wav", b"RIFF")}).status_code in (404, 405)
    assert client.delete("/v1/audio/voices/drive_by").status_code in (404, 405)
    assert not (VOICES / "drive_by.wav").exists()
    assert "drive_by" not in _ids(client)
