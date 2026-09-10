"""A-3 / design §4-3: ``/health`` と ``/ywk/status`` はモデル未読込でも 200.

``device.actual`` must be read back from the loaded model rather than echoed
from the env, because that is the only way the 本体 can tell an honest
"running on cuda:0" from the silent CPU fallback the survey found.
"""

from __future__ import annotations

from typing import Any

from conftest import DEFAULT_VOICE, MODEL, write_voices


def test_health_is_200_without_a_model(client, unloaded):
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_status_is_200_without_a_model(client, unloaded):
    response = client.get("/ywk/status")
    assert response.status_code == 200
    payload = response.json()
    assert payload["engine"] == "irodori-ywk"
    # 裁定 105（ポート先行）＝bind は読込より先なので、この 3 欄が「まだ載っていない
    # が生きている」を名乗る唯一の場所である。`error` は失敗したときだけ非 null。
    assert payload["runtime"] == {"loaded": False, "loading": False, "error": None}
    assert payload["device"]["actual"] is None


def test_status_pins_the_upstream_commits(client, ywk):
    payload = client.get("/ywk/status").json()
    assert payload["upstream"] == {"irodori_tts": "8224daf", "server": "841fb7c"}
    assert payload["version"] == ywk.YWK_VERSION


def test_status_reports_the_real_device_once_loaded(client, baseline):
    payload = client.get("/ywk/status").json()
    assert payload["runtime"] == {"loaded": True, "loading": False, "error": None}
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


def test_status_names_the_process_that_answered(client):
    """便 D（2）: ``pid`` は「この応答は誰の物か」の 1 欄（契約 ⑹）.

    Why it has to be here: 裁定 105（ポート先行）で bind は 1 秒以内に来るように
    なったが、**窓は閉じていない**＝既にそのポートを握っている個体が居れば、こちらの
    子が bind に失敗して落ちるまでの間、同じ形の応答が返る。この欄が無ければランチャ
    は他人の数字を状態帯（裁定 67 ⑶）に出し、他人の readiness で 待機 に上げる。
    在れば、起こした子の pid と突合してその標本を落とせる。
    """
    import os

    payload = client.get("/ywk/status").json()
    assert payload["pid"] == os.getpid()
    assert isinstance(payload["pid"], int)
    assert not isinstance(payload["pid"], bool)


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


# ---------------------------------------------------------- 決裁 130 Q4（v2.0）


def test_status_declares_no_request_in_flight_when_idle(client):
    """契約 ⑹: ``requests.in_flight``＝いま走っている本物の合成の数（既定 0）。

    欄を足すだけなので ``schema`` は動かない（⑻）＝古い本体は知らない欄を無視し、
    古い wrapper に当たった新しいランチャは欄が無いのを 0 と読む（両方向で互換）。
    """
    payload = client.get("/ywk/status").json()
    assert payload["requests"] == {"in_flight": 0}
    assert isinstance(payload["requests"]["in_flight"], int)
    assert not isinstance(payload["requests"]["in_flight"], bool)


def test_status_reports_the_count_while_a_real_request_runs(client, baseline):
    """**本物の POST の路**と ``/ywk/status`` の欄が繋がっている。

    手で ``_real_request_in_flight()`` を握るだけでは、``ywk_status()`` の 1 行が
    別の名（例＝新しい計数）に差し替わっても契約テストは緑のまま「本体が使っている」を
    永久に 0 と答えうる。作法は ``tests/contract/test_warmup.py`` の
    ``test_the_speech_route_counts_the_request_while_it_runs`` から借り、
    数える先を ``pending_real_requests()`` ではなく **status の欄**にした。
    """
    seen: list[int] = []
    original = baseline.synthesize

    def counting(req: Any, *, log_fn: Any = None) -> Any:
        seen.append(client.get("/ywk/status").json()["requests"]["in_flight"])
        return original(req, log_fn=log_fn)

    baseline.synthesize = counting
    response = client.post(
        "/v1/audio/speech", json={"model": MODEL, "input": "テスト。", "voice": DEFAULT_VOICE}
    )
    assert response.status_code == 200, response.text
    assert seen == [1]
    assert client.get("/ywk/status").json()["requests"]["in_flight"] == 0


def test_status_comes_back_to_zero_and_release_is_idempotent(client, ywk):
    """``release`` は冪等＝二度呼んでも負に振れない（SSE は終わりを 2 つ持つ）。"""
    holder = ywk._real_request_in_flight()
    holder.__enter__()
    holder.release()
    holder.release()
    assert ywk.pending_real_requests() == 0
    assert client.get("/ywk/status").json()["requests"]["in_flight"] == 0
