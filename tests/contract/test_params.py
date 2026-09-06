"""A-5: ``/params`` に説明のつかない ``default: null`` が 0 件・型が openapi と一致.

``GET /params`` is the whole reason the distribution exists as a wrapper: the
upstream ``/openapi.json`` carries the 44 field names and their types but
**zero** defaults, ranges and descriptions
(``research/lab/notes/37-handoff-contract.md`` §1-2), so the 本体's
``ParamDescriptor`` (Min/Max/Step/DefaultValue) cannot be built from it.
"""

from __future__ import annotations

import time

import pytest

import ywk_params

#: ``settings``/``dataclass``/``ywk`` defaults are concrete values; the other
#: three sources are the only ones allowed to report ``null`` (see ywk_params).
CONCRETE_SOURCES = {"settings", "dataclass", "ywk"}
NULLABLE_SOURCES = {"settings_optional", "checkpoint", "unset"}

#: 設計書 §4-2 ⑵ verbatim: the fields the 本体 turns into a ``ParamDescriptor``.
EXPOSED = {
    "caption",
    "seed",
    "num_steps",
    "cfg_scale_text",
    "cfg_scale_caption",
    "cfg_scale_speaker",
    "t_schedule_mode",
    "sway_coeff",
}


@pytest.fixture()
def params(client):
    response = client.get("/params")
    assert response.status_code == 200
    return response.json()


def test_params_is_200_without_a_model(client, unloaded):
    response = client.get("/params")
    assert response.status_code == 200
    assert response.json()["model_loaded"] is False


def test_params_answers_in_under_100ms(client, unloaded):
    client.get("/params")  # warm the route
    start = time.perf_counter()
    response = client.get("/params")
    elapsed = time.perf_counter() - start
    assert response.status_code == 200
    assert elapsed < 0.100, f"/params took {elapsed * 1000:.1f} ms"


def test_params_lists_all_44_irodori_fields(params):
    keys = [item["key"] for item in params["irodori"]]
    assert len(keys) == 44
    assert len(set(keys)) == 44
    assert keys == [spec["key"] for spec in ywk_params.IRODORI_PARAMS]


def test_exposed_fields_declare_themselves(params):
    """設計書 §4-2 ⑵: exactly these nine (+ voice, speed) are exposed."""
    exposed = {item["key"] for item in params["irodori"] if item["exposed_to_ywk"]}
    assert exposed == EXPOSED
    request_exposed = {k for k, v in params["request"].items() if v["exposed_to_ywk"]}
    assert request_exposed == {"voice", "speed"}
    assert set(params["rules"]["exposed_to_ywk"]) == EXPOSED | {"voice", "speed"}


def test_no_null_default_in_the_exposed_set(params):
    """A-5: "``exposed_to_ywk: true`` の欄に ``default: null`` 0 件".

    A field the 本体 renders has to have something to render.  ``caption`` and
    ``seed`` say ``""`` rather than ``null`` (the wrapper folds ``""`` back into
    "omitted"); the other seven come from ``settings``.
    """
    offenders = [
        item["key"]
        for item in params["irodori"]
        if item["exposed_to_ywk"] and item["default"] is None
    ]
    assert offenders == []
    request_offenders = [
        key
        for key, item in params["request"].items()
        if item["exposed_to_ywk"] and item["default"] is None
    ]
    assert request_offenders == []


def test_the_two_empty_string_defaults_round_trip(client, baseline):
    """Sending back what ``/params`` reported must not 400 (design §4-2 ⑵)."""
    from conftest import speech_body

    payload = client.get("/params").json()
    by_key = {item["key"]: item for item in payload["irodori"]}
    assert by_key["caption"]["default"] == ""
    assert by_key["seed"]["default"] == ""
    assert payload["rules"]["empty_string_means_unset"] == ["caption", "seed"]

    response = client.post(
        "/v1/audio/speech", json=speech_body(irodori={"caption": "", "seed": ""})
    )
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].caption is None


def test_no_unexplained_null_default(params):
    """Every remaining null must name why it is null, and say it is nullable."""
    offenders = []
    for item in params["irodori"]:
        source = item["default_source"]
        assert source in CONCRETE_SOURCES | NULLABLE_SOURCES, item
        if item["default"] is None:
            if source in CONCRETE_SOURCES:
                offenders.append(item["key"])
            else:
                assert item["nullable"] is True, item["key"]
    assert offenders == []


def test_nullable_is_declared_on_every_field(params):
    """設計書 §4-2 ⑴: "未指定＝上流の挙動" is spelled ``nullable: true``."""
    for item in params["irodori"]:
        assert isinstance(item["nullable"], bool), item["key"]
        if item["default_source"] in NULLABLE_SOURCES:
            assert item["nullable"] is True, item["key"]
    # the two ``ywk`` fields keep nullable:true -- "" and null mean the same
    by_key = {item["key"]: item for item in params["irodori"]}
    assert by_key["caption"]["nullable"] is True
    assert by_key["seed"]["nullable"] is True
    assert by_key["num_steps"]["nullable"] is False


def test_checkpoint_dependent_defaults_are_the_only_model_gated_nulls(client, unloaded):
    payload = client.get("/params").json()
    assert payload["model_loaded"] is False
    checkpoint_gated = {
        item["key"] for item in payload["irodori"] if item["default_source"] == "checkpoint"
    }
    assert checkpoint_gated == {"max_ref_seconds", "max_text_len", "max_caption_len"}
    nulls = {item["key"] for item in payload["irodori"] if item["default"] is None}
    unset = {item["key"] for item in payload["irodori"] if item["default_source"] == "unset"}
    optional = {
        item["key"] for item in payload["irodori"] if item["default_source"] == "settings_optional"
    }
    # Every null is accounted for by exactly one of the three explained sources.
    assert nulls == checkpoint_gated | unset | optional


def test_checkpoint_dependent_defaults_fill_in_once_loaded(client, baseline):
    """The fake runtime carries the v4.1-Small numbers; they must surface."""
    payload = client.get("/params").json()
    assert payload["model_loaded"] is True
    assert payload["checkpoint"]["max_text_len"] == 256
    assert payload["checkpoint"]["ref_max_seconds"] == 120.0
    by_key = {item["key"]: item for item in payload["irodori"]}
    assert by_key["max_text_len"]["default"] == 256
    assert by_key["max_caption_len"]["default"] == 256
    assert by_key["max_ref_seconds"]["default"] == 120.0


def test_effective_defaults_match_the_live_settings(params):
    from irodori_openai_tts import app as upstream

    by_key = {item["key"]: item for item in params["irodori"]}
    assert by_key["num_steps"]["default"] == upstream.settings.default_num_steps == 40
    assert by_key["cfg_scale_text"]["default"] == upstream.settings.default_cfg_scale_text
    assert by_key["cfg_scale_speaker"]["default"] == upstream.settings.default_cfg_scale_speaker
    assert by_key["t_schedule_mode"]["default"] == upstream.settings.default_t_schedule_mode
    assert by_key["trim_tail"]["default"] is True


def test_cfg_scale_caption_reports_the_server_effective_value(params):
    """app.py:932-939 feeds ``default_cfg_scale_text`` into ``cfg_scale_caption``."""
    by_key = {item["key"]: item for item in params["irodori"]}
    caption = by_key["cfg_scale_caption"]
    assert caption["default"] == 3.0
    assert caption["default"] == by_key["cfg_scale_text"]["default"]
    assert "4.0" in caption["note"]


def test_types_match_the_upstream_openapi(client):
    """腐り検知: the table and ``IrodoriOptions`` must not drift apart."""
    schema = client.get("/openapi.json").json()
    assert ywk_params.openapi_mismatches(schema) == []


def test_startup_drift_check_is_clean(ywk):
    assert ywk.OPENAPI_DRIFT == []


def test_every_numeric_field_carries_a_range_and_its_source(params):
    for item in params["irodori"]:
        if item["type"] not in ("number", "integer"):
            continue
        assert "min" in item and "max" in item, item["key"]
        assert item["min"] <= item["max"]
        assert item.get("step") is not None, item["key"]
        assert item.get("range_source"), item["key"]


def test_every_field_carries_a_japanese_label_and_description(params):
    for item in params["irodori"]:
        assert item["label"].strip()
        assert item["description"].strip()
        assert item["group"]
        assert any(ord(char) > 0x2E80 for char in item["label"]), item["key"]


def test_enum_fields_declare_their_values(params):
    by_key = {item["key"]: item for item in params["irodori"]}
    assert by_key["t_schedule_mode"]["enum"] == ["linear", "sway"]
    assert by_key["decode_mode"]["enum"] == ["sequential", "batch"]
    assert by_key["cfg_guidance_mode"]["enum"] == [
        "independent",
        "joint",
        "alternating",
    ]


def test_request_block_matches_the_upstream_speech_request(params):
    request = params["request"]
    assert set(request) == {
        "model",
        "input",
        "voice",
        "speed",
        "response_format",
        "stream_format",
    }
    assert request["input"]["max_length"] == 4096
    assert request["speed"]["min"] == 0.25
    assert request["speed"]["max"] == 4.0
    assert request["speed"]["default"] == 1.0
    assert request["response_format"]["enum"] == ["wav"]
    assert request["response_format"]["default"] == "wav"


def test_rules_block_states_the_contract(params):
    rules = params["rules"]
    assert rules["priority"] == "irodori.X > top-level X > env"
    assert rules["literal_must_be_nested"] == [
        "t_schedule_mode",
        "decode_mode",
        "cfg_guidance_mode",
    ]
    assert rules["voice_and_no_ref_exclusive"] is True
    assert rules["unknown_field"] == "400"
    assert rules["out_of_range"] == "400"
    assert rules["response_format"] == ["wav"]


def test_prefetch_is_declared_unavailable(params):
    assert params["prefetch"]["available"] is False


def test_params_carries_no_absolute_path(client):
    body = client.get("/params").text
    assert ":\\" not in body and ":/" not in body


def test_schema_and_engine(params):
    assert params["schema"] == 1
    assert params["engine"] == "irodori-ywk"
    assert params["checkpoint"]["hf"] == "Aratako/Irodori-TTS-v4.1-Small"


def test_cfg_scale_text_is_the_emotion_strength_field(params):
    """decisions.md 99: the 本体 shows ``cfg_scale_text`` as 「感情表現の強さ」.

    The label is what the 本体 turns into ``ParamDescriptor.DisplayName``, so
    the canon word is pinned here; the description must keep the upstream
    meaning (text-condition adherence, emoji included) and the 0 warning.
    """
    by_key = {item["key"]: item for item in params["irodori"]}
    field = by_key["cfg_scale_text"]
    assert field["label"] == "感情表現の強さ"
    assert not field["label"].startswith("CFG 強度")
    assert field["group"] == "emotion"
    assert field["exposed_to_ywk"] is True
    assert (field["min"], field["max"], field["step"]) == (0.0, 10.0, 0.1)
    assert "絵文字" in field["description"]
    assert "追従" in field["description"]
    assert "0 は" in field["description"]
    assert "IsEmotion" in field["note"]
