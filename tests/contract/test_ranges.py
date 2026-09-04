"""A-5: 範囲外は 400.

Survey cases S-5 / S-6 (``research/lab/notes/37-handoff-contract.md``):
``num_steps:-5`` and ``cfg_scale_text:999`` both returned **200** upstream and
the bad value reached the runtime.  The upstream HTTP API performs no range
checking at all, so the wrapper's table is the only place it happens.
"""

from __future__ import annotations

import pytest

from conftest import speech_body


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("num_steps", -5),
        ("num_steps", 0),
        ("num_steps", 1000),
        ("cfg_scale_text", 999),
        ("cfg_scale_text", -1),
        ("cfg_scale_speaker", 10.5),
        ("duration_scale", 0.0),
        ("duration_scale", 3.0),
        ("sway_coeff", -2.0),
        ("num_candidates", 0),
        ("num_candidates", 33),
        ("cfg_min_t", 1.5),
        ("cfg_max_t", -0.1),
        ("speaker_kv_min_t", 1.1),
        ("speaker_kv_scale", 0.0),
        ("truncation_factor", 0.0),
        ("tail_window_size", 0),
        ("chunk_min_chars", 0),
        ("max_text_len", 0),
        ("seed", -1),
    ],
)
def test_out_of_range_nested_is_400(client, field, value):
    response = client.post("/v1/audio/speech", json=speech_body(irodori={field: value}))
    assert response.status_code == 400, response.text
    error = response.json()["error"]
    assert error["code"] == "ywk_out_of_range"
    assert error["param"] == f"irodori.{field}"


@pytest.mark.parametrize(("field", "value"), [("num_steps", -5), ("cfg_scale_text", 999)])
def test_out_of_range_top_level_is_400(client, field, value):
    """The flat path must be checked too -- upstream reads it from model_extra."""
    response = client.post("/v1/audio/speech", json=speech_body(**{field: value}))
    assert response.status_code == 400, response.text
    assert response.json()["error"]["code"] == "ywk_out_of_range"


@pytest.mark.parametrize("value", [0.2, 5.0])
def test_speed_out_of_range_is_400(client, value):
    response = client.post("/v1/audio/speech", json=speech_body(speed=value))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_out_of_range"


def test_over_long_input_is_400_and_does_not_echo_the_body(client):
    """The upstream 422 echoed the whole 5,000-character body (§2-1)."""
    long_text = "あ" * 5000
    response = client.post("/v1/audio/speech", json=speech_body(input=long_text))
    assert response.status_code == 400
    body = response.text
    assert "あ" * 100 not in body
    assert len(response.content) <= 512


@pytest.mark.parametrize(
    ("field", "value"),
    [
        # gradio's slider is 1..120 (gradio_app.py:481); 設計書 §4-2 ⑶ says the
        # range must be the one the source of the number actually gives, so
        # both ends are accepted and 10/40 live in ``presets`` instead.
        ("num_steps", 1),
        ("num_steps", 4),
        ("num_steps", 64),
        ("num_steps", 120),
        ("cfg_scale_text", 0.0),
        ("cfg_scale_text", 10.0),
    ],
)
def test_range_edges_are_accepted(client, baseline, field, value):
    response = client.post("/v1/audio/speech", json=speech_body(irodori={field: value}))
    assert response.status_code == 200, response.text
    assert getattr(baseline.requests[-1], field) == value
