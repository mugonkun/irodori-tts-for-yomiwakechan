"""A-3 / design §4-3: ``/health`` と ``/ywk/status`` はモデル未読込でも 200.

``device.actual`` must be read back from the loaded model rather than echoed
from the env, because that is the only way the 本体 can tell an honest
"running on cuda:0" from the silent CPU fallback the survey found.
"""

from __future__ import annotations

from conftest import DEFAULT_VOICE, write_voices


def test_health_is_200_without_a_model(client, unloaded):
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_status_is_200_without_a_model(client, unloaded):
    response = client.get("/ywk/status")
    assert response.status_code == 200
    payload = response.json()
    assert payload["engine"] == "irodori-ywk"
    assert payload["runtime"]["loaded"] is False
    assert payload["device"]["actual"] is None


def test_status_pins_the_upstream_commits(client, ywk):
    payload = client.get("/ywk/status").json()
    assert payload["upstream"] == {"irodori_tts": "8224daf", "server": "841fb7c"}
    assert payload["version"] == ywk.YWK_VERSION


def test_status_reports_the_real_device_once_loaded(client, baseline):
    payload = client.get("/ywk/status").json()
    assert payload["runtime"]["loaded"] is True
    assert payload["device"]["configured"] == "cpu"
    assert payload["device"]["actual"] == "cpu"
    assert payload["device"]["precision"] == "fp32"


def test_status_reports_torch(client):
    payload = client.get("/ywk/status").json()
    assert payload["torch"]["version"]
    assert "cuda" in payload["torch"]
    assert "hip" in payload["torch"]


def test_status_carries_no_absolute_path(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("alice.wav",))
    response = client.get("/ywk/status")
    body = response.text
    assert ":\\" not in body and ":/" not in body
    payload = response.json()
    assert payload["voices"]["dir"] == "voices"
    assert payload["voices"]["count"] == 2


def test_status_declares_warmup_idle(client):
    """便 A promised ``{"state","shots"}``; 便 C filled the record in.

    The two 便 A keys still read the same on an untouched server -- the rest of
    the record (``tests/contract/test_warmup.py``) was added around them, and
    contract ⑻ says adding fields does not move ``schema``.
    """
    payload = client.get("/ywk/status").json()
    assert payload["warmup"]["state"] == "idle"
    assert payload["warmup"]["shots"] == 0


def test_bind_and_port_defaults(client):
    payload = client.get("/ywk/status").json()
    assert payload["host"] == "127.0.0.1"
    assert payload["port"] == 18088


def test_device_line_is_logged_once_when_the_model_is_in(ywk, capsys, baseline):
    """§4-8: 読込完了時に実 device を 1 行。二度は出さない。"""
    ywk._device_logged = False
    first = ywk.note_device_once()
    second = ywk.note_device_once()
    assert first is not None
    assert first.startswith("ywk_server: device actual=cpu")
    assert second is None
    assert first in capsys.readouterr().err


def test_device_line_is_not_logged_before_the_model_is_in(ywk, unloaded):
    ywk._device_logged = False
    assert ywk.note_device_once() is None
    assert ywk._device_logged is False
