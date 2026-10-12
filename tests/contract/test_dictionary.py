"""decisions.md 166 / contract ⑶ 3-1・⑹: the app's own reading dictionary.

``<data>/dictionary.json`` (written by the launcher) is a ``from`` → ``to``
table applied to ``input`` top to bottom, every occurrence, *before* the emoji
handling of v2.0.10 -- the second tier under the 本体's own dictionary.  An edit
is picked up on the next request (size + mtime), a broken file is an empty
table plus one error line, and nothing of the text or the table is logged.
"""

from __future__ import annotations

import json
import logging
import os
import time
from pathlib import Path

import pytest
from conftest import TMP, speech_body

import ywk_dictionary

PARTY = "\U0001F389"  # 🎉
PRAY = "\U0001F64F"  # 🙏
SKIN = "\U0001F3FB"  # 🏻
FILE = TMP / "dictionary.json"


def write(entries: list[dict] | None, raw: str | None = None) -> None:
    if raw is not None:
        FILE.write_text(raw, encoding="utf-8")
    else:
        FILE.write_text(json.dumps({"schema": 1, "entries": entries}, ensure_ascii=False), encoding="utf-8")
    # mtime resolution on some file systems is coarse; make every write distinct
    stamp = time.time_ns()
    os.utime(FILE, ns=(stamp, stamp))
    ywk_dictionary.forget(FILE)


@pytest.fixture(autouse=True)
def _clean_dictionary():
    if FILE.exists():
        FILE.unlink()
    ywk_dictionary.forget(FILE)
    yield
    if FILE.exists():
        FILE.unlink()
    ywk_dictionary.forget(FILE)


def _sent(baseline) -> str:
    return baseline.requests[-1].text


# ---------------------------------------------------------------------------
# The file
# ---------------------------------------------------------------------------


def test_no_file_means_no_change(client, baseline):
    response = client.post("/v1/audio/speech", json=speech_body(input="www"))
    assert response.status_code == 200
    assert _sent(baseline) == "www"
    status = client.get("/ywk/status").json()["dictionary"]
    assert status == {"file": "dictionary.json", "entries": 0, "error": None}


def test_entries_replace_top_to_bottom_every_occurrence(client, baseline):
    write([{"from": "ab", "to": "x"}, {"from": "x", "to": "y"}, {"from": "www", "to": "わら"}])
    response = client.post("/v1/audio/speech", json=speech_body(input="ab ab www!"))
    assert response.status_code == 200
    assert _sent(baseline) == "y y わら!"  # ab→x, then x→y (順序が意味を持つ)
    assert client.get("/ywk/status").json()["dictionary"]["entries"] == 3


def test_empty_from_is_ignored_and_empty_to_erases(client, baseline):
    write([{"from": "", "to": "爆発"}, {"from": "草", "to": ""}, {"from": 3, "to": "x"}, "junk"])
    response = client.post("/v1/audio/speech", json=speech_body(input="それな草"))
    assert response.status_code == 200
    assert _sent(baseline) == "それな"
    assert client.get("/ywk/status").json()["dictionary"]["entries"] == 1


def test_broken_file_is_an_empty_table_with_one_error_line(client, baseline):
    write(None, raw="{not json")
    response = client.post("/v1/audio/speech", json=speech_body(input="テスト。"))
    assert response.status_code == 200
    assert _sent(baseline) == "テスト。"
    status = client.get("/ywk/status").json()["dictionary"]
    assert status["entries"] == 0
    assert status["error"] and "dictionary.json" in status["error"]
    assert "\\" not in status["error"] and str(TMP) not in status["error"]


def test_an_edit_takes_effect_on_the_next_request(client, baseline):
    write([{"from": "GPU", "to": "じーぴーゆー"}])
    client.post("/v1/audio/speech", json=speech_body(input="GPU"))
    assert _sent(baseline) == "じーぴーゆー"
    write([{"from": "GPU", "to": "ぐらふぃっくす"}])
    client.post("/v1/audio/speech", json=speech_body(input="GPU"))
    assert _sent(baseline) == "ぐらふぃっくす"
    FILE.unlink()
    ywk_dictionary.forget(FILE)
    client.post("/v1/audio/speech", json=speech_body(input="GPU"))
    assert _sent(baseline) == "GPU"


# ---------------------------------------------------------------------------
# Order against the emoji handling
# ---------------------------------------------------------------------------


def test_dictionary_runs_before_the_emoji_removal(client, baseline):
    write([{"from": PARTY, "to": "ぱちぱち"}])
    response = client.post("/v1/audio/speech", json=speech_body(input=f"やったー{PARTY}{PARTY}"))
    assert response.status_code == 200
    assert _sent(baseline) == "やったーぱちぱちぱちぱち"
    assert "x-ywk-emoji-removed" not in response.headers


def test_parts_the_dictionary_leaves_behind_are_swept(client, baseline):
    """「🙏→おねがい」 applied to 🙏🏻 leaves 🏻; the emoji step removes it."""
    write([{"from": PRAY, "to": "おねがい"}])
    response = client.post("/v1/audio/speech", json=speech_body(input=f"{PRAY}{SKIN}します"))
    assert response.status_code == 200
    assert _sent(baseline) == "おねがいします"
    assert response.headers["x-ywk-emoji-removed"] == "1"


def test_a_reading_can_make_an_emoji_only_comment_speakable(client, baseline):
    write([{"from": PARTY, "to": "くらっかー"}])
    response = client.post("/v1/audio/speech", json=speech_body(input=PARTY))
    assert response.status_code == 200
    assert "x-ywk-silent" not in response.headers
    assert _sent(baseline) == "くらっかー"


def test_caption_is_not_touched(client, baseline):
    write([{"from": "明るく", "to": "あかるく"}])
    client.post("/v1/audio/speech", json=speech_body(input="明るく", irodori={"caption": "明るく"}))
    assert _sent(baseline) == "あかるく"
    assert baseline.requests[-1].caption == "明るく"


# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------


def test_one_log_line_with_counts_only(client, baseline, caplog):
    caplog.set_level(logging.INFO, logger="ywk_server")
    write([{"from": "秘密の語", "to": "ひみつ"}])
    client.post("/v1/audio/speech", json=speech_body(input="秘密の語と秘密の語"))
    lines = [r.getMessage() for r in caplog.records if r.getMessage().startswith("dictionary applied:")]
    assert lines == ["dictionary applied: hits=2 chars=9->7"]
    assert not [r for r in caplog.records if "秘密" in r.getMessage() or "ひみつ" in r.getMessage()]


def test_no_log_line_when_nothing_matched(client, baseline, caplog):
    caplog.set_level(logging.INFO, logger="ywk_server")
    write([{"from": "GPU", "to": "じーぴーゆー"}])
    client.post("/v1/audio/speech", json=speech_body(input="こんにちは"))
    assert not [r for r in caplog.records if r.getMessage().startswith("dictionary applied:")]


def test_emoji_log_counts_from_the_worded_text(client, baseline, caplog):
    caplog.set_level(logging.INFO, logger="ywk_server")
    write([{"from": "GPU", "to": "じーぴーゆー"}])
    client.post("/v1/audio/speech", json=speech_body(input=f"GPU{PARTY}"))
    msgs = [r.getMessage() for r in caplog.records]
    assert "dictionary applied: hits=1 chars=4->7" in msgs
    assert any(m.startswith("emoji filtered:") and "chars=7->6" in m for m in msgs)


# ---------------------------------------------------------------------------
# The module itself
# ---------------------------------------------------------------------------


def test_apply_is_ordinal_and_case_sensitive():
    table = ywk_dictionary.DictionaryTable(
        entries=(ywk_dictionary.DictionaryEntry("Ab", "x"),)
    )
    assert ywk_dictionary.apply("ab Ab AB", table) == ("ab x AB", 1)


def test_file_name_is_the_one_the_launcher_writes():
    assert ywk_dictionary.FILE_NAME == "dictionary.json"
    assert Path(FILE).name == ywk_dictionary.FILE_NAME
