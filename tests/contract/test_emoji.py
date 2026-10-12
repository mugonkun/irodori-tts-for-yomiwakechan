"""decisions.md 165 / contract ⑶ 3-1: emoji in ``input``.

The checkpoint reads 45 emoji as style annotations (``EMOJI_ANNOTATIONS.md`` in
the model repo); any other emoji still asks the duration predictor for time
with nothing to say in it, and the acoustic model fills that time with
unexpected words (survey ``research/report/emoji-survey-2026-10-12.md``).  The
wrapper therefore:

* passes the 45 through, spelled as the model repo spells them
  (``⏸`` → ``⏸️``, ``🙏🏻`` → ``🙏``);
* removes every other emoji cluster and every orphaned emoji part;
* folds the characters the upstream's own ``normalize_text`` turns into text
  anyway (``㊗`` → 祝, ``‼`` → ``!!``, ``♥`` → ``♡``) plus ``❗❓`` → ``!?``
  and keycaps ``1️⃣`` → ``1``;
* answers **200 with silence** when nothing pronounceable is left -- never
  the upstream's 400, which the 本体 would show as 「本体側の不具合」;
* writes one log line without body text.

The expected strings in ``emoji-cases.json`` were produced independently of
the implementation (Unicode's ``emoji-data.txt`` tables and hand-written
sentences; survey §6-2), so these tests are not the implementation reading
itself back.
"""

from __future__ import annotations

import base64
import json
import logging
import re
import struct
from pathlib import Path

import pytest
from conftest import DEFAULT_VOICE, MODEL, speech_body
from irodori_openai_tts import app as upstream
from irodori_tts.duration import ALLOWED_ANNOTATION_EMOJIS

import ywk_emoji
import ywk_params

HERE = Path(__file__).resolve().parent
CASES = json.loads((HERE / "emoji-cases.json").read_text(encoding="utf-8"))
PALETTE = HERE.parents[1] / "upstream" / "Irodori-TTS" / "irodori_tts" / "gradio_emoji_palette.py"

PARTY = "\U0001F389"  # 🎉 -- the commonest unsupported emoji in a chat
JOY = "\U0001F606"  # 😆 -- supported #25
PAUSE_CANON = "\U000023F8\U0000FE0F"  # ⏸️ with VS16, as the model repo spells it
PAUSE_BARE = "\U000023F8"  # ⏸ without
PRAY = "\U0001F64F"  # 🙏
SKIN = "\U0001F3FB"  # 🏻
RI_J = "\U0001F1EF"  # 🇯 alone


def _frames(text: str) -> list[tuple[str, dict]]:
    out: list[tuple[str, dict]] = []
    for block in text.split("\n\n"):
        block = block.strip("\n")
        if not block:
            continue
        lines = block.split("\n")
        out.append((lines[0][len("event: ") :], json.loads(lines[1][len("data: ") :])))
    return out


def _wav_info(data: bytes) -> dict:
    assert data[:4] == b"RIFF" and data[8:12] == b"WAVE", data[:12]
    assert data[12:16] == b"fmt "
    fmt_size, audio_format, channels, rate, _byte_rate, _align, bits = struct.unpack(
        "<IHHIIHH", data[16:36]
    )
    assert fmt_size == 16
    data_at = 20 + fmt_size
    assert data[data_at : data_at + 4] == b"data", data[data_at : data_at + 4]
    (data_len,) = struct.unpack("<I", data[data_at + 4 : data_at + 8])
    return {
        "format": audio_format,
        "channels": channels,
        "rate": rate,
        "bits": bits,
        "samples": data_len // 2,
        "data_offset": data_at + 8,
    }


# ---------------------------------------------------------------------------
# The table (independent oracle) -- unit level, no server
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "case",
    CASES["cases"],
    ids=[f"{c['category']}:{c['id']}" for c in CASES["cases"]],
)
def test_filter_matches_the_independent_table(case):
    assert ywk_emoji.filter_text(case["input"]).text == case["expected"]


def test_the_table_is_the_decided_policy():
    assert CASES["supported_45"] == list(ywk_emoji.SUPPORTED_EMOJIS)
    assert CASES["count"] == len(CASES["cases"])


# ---------------------------------------------------------------------------
# The 45 and where they come from
# ---------------------------------------------------------------------------


def test_the_45_are_the_upstream_palette_in_its_order():
    """The list the wrapper passes must stay the list the model repo publishes.

    The Gradio palette (``gradio_emoji_palette.py``) mirrors
    ``EMOJI_ANNOTATIONS.md`` item for item; parsed as text because the module
    imports gradio.
    """
    source = PALETTE.read_text(encoding="utf-8")
    palette = re.findall(r'EmojiPaletteItem\("([^"]+)"', source)
    assert len(palette) == 45
    assert list(ywk_emoji.SUPPORTED_EMOJIS) == palette


def test_the_45_are_a_subset_of_what_the_duration_code_counts():
    """``duration.py`` lists 56 (45 + 11 never disclosed); the wrapper passes the 45."""
    allowed = set(ALLOWED_ANNOTATION_EMOJIS)
    assert set(ywk_emoji.SUPPORTED_EMOJIS) <= allowed
    assert len(allowed - set(ywk_emoji.SUPPORTED_EMOJIS)) == 11


@pytest.mark.parametrize("emoji", list(ywk_emoji.SUPPORTED_EMOJIS))
def test_each_supported_emoji_reaches_the_runtime_untouched(client, baseline, emoji):
    text = f"あ{emoji}い{emoji}{emoji}"
    response = client.post("/v1/audio/speech", json=speech_body(input=text))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == text
    assert "x-ywk-emoji-removed" not in response.headers
    assert "x-ywk-silent" not in response.headers


@pytest.mark.parametrize("emoji", sorted(set(ALLOWED_ANNOTATION_EMOJIS) - set(ywk_emoji.SUPPORTED_EMOJIS)))
def test_each_undisclosed_emoji_is_removed(client, baseline, emoji):
    """裁定 165 Q1: the 11 that only the code counts are not supported."""
    response = client.post("/v1/audio/speech", json=speech_body(input=f"あ{emoji}い"))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == "あい"
    assert response.headers["x-ywk-emoji-removed"] == "1"


# ---------------------------------------------------------------------------
# What reaches the runtime
# ---------------------------------------------------------------------------


def test_unsupported_emoji_are_removed_and_the_text_closes_up(client, baseline):
    response = client.post(
        "/v1/audio/speech", json=speech_body(input=f"こんにちは{PARTY}今日もがんばろう")
    )
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == "こんにちは今日もがんばろう"
    assert response.headers["x-ywk-emoji-removed"] == "1"


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        (f"待って{PAUSE_BARE}ね", f"待って{PAUSE_CANON}ね"),  # VS16 added
        ("\U000023E9\U0000FE0Fはやく", "\U000023E9はやく"),  # ⏩️ -> ⏩ (VS16 dropped)
        (f"ありがとう{PRAY}{SKIN}", f"ありがとう{PRAY}"),  # skin tone stripped
        ("\U0001F60A\U0000FE0Fね", "\U0001F60Aね"),  # 😊️ -> 😊
        ("\U0001F62E\U0001F4A8つかれた", "\U0001F62Eつかれた"),  # 😮💨 (no ZWJ): 💨 removed, no promotion
    ],
)
def test_variants_of_the_45_are_spelled_canonically(client, baseline, text, expected):
    response = client.post("/v1/audio/speech", json=speech_body(input=text))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == expected


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        (f"おねがい{SKIN}", "おねがい"),  # 辞書 🙏→おねがい applied to 🙏🏻
        ("ハート\U0000FE0F", "ハート"),  # 辞書 ❤→ハート applied to ❤️
        ("えっ\U0000200D", "えっ"),  # 辞書 😮→えっ applied to 😮‍💨 (💨 already gone)
        (f"日本{RI_J}", "日本"),  # a lone regional indicator
    ],
)
def test_orphaned_parts_the_dictionary_leaves_are_swept(client, baseline, text, expected):
    response = client.post("/v1/audio/speech", json=speech_body(input=text))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == expected


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("\U0001F647\U0000200D\U00002642\U0000FE0Fすみません", "すみません"),  # 🙇‍♂️ as one unit
        ("\U0001F1EF\U0001F1F5から", "から"),  # 🇯🇵
        ("1\U0000FE0F\U000020E3位", "1位"),  # keycap keeps its digit (Q9)
        ("\U00003297合格", "祝合格"),  # ㊗ folded as the upstream NFKC would (Q7)
        ("すごい\U0000203C", "すごい!!"),  # ‼
        ("大好き\U00002665", "大好き\U00002661"),  # ♥ -> ♡ (upstream's own replacement)
        ("本当\U00002753", "本当?"),  # ❓ -> ? (Q8)
        ("やった\U00002757", "やった!"),  # ❗ -> !
        ("\U000000A92026年", "2026年"),  # © is text-default but still an emoji (Q5 = P1)
        ("次へ\U000025B6", "次へ"),  # ▶
        ("いいね\U00002764\U0000FE0F", "いいね"),  # ❤️
        ("いいね\U00002764", "いいね"),  # ❤ (bare, 1 token in the vocab)
        ("ラララ\U0000266A\U00002605", "ラララ\U0000266A\U00002605"),  # ♪ ★ are not emoji
    ],
)
def test_sequences_folds_and_text_default_pictographs(client, baseline, text, expected):
    response = client.post("/v1/audio/speech", json=speech_body(input=text))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == expected


def test_text_without_emoji_is_passed_through_untouched(client, baseline):
    text = "テスト。(´・ω・`) www 888 〜ー々 ①②"
    response = client.post("/v1/audio/speech", json=speech_body(input=text))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == text
    assert "x-ywk-emoji-removed" not in response.headers


def test_growth_from_folding_is_clipped_on_grapheme_borders(client, baseline):
    """⏸ × 4096 becomes 8192 code points with VS16; the upstream caps input at 4096."""
    response = client.post("/v1/audio/speech", json=speech_body(input=PAUSE_BARE * 4096))
    assert response.status_code == 200, response.text
    sent = baseline.requests[-1].text
    assert len(sent) == 4096
    assert sent == PAUSE_CANON * 2048
    assert ywk_emoji.INPUT_MAX_CHARS == next(
        p["max_length"] for p in ywk_params.REQUEST_PARAMS if p["key"] == "input"
    )


# ---------------------------------------------------------------------------
# Nothing left to pronounce -> 200 with silence
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "text",
    [
        PARTY * 3,  # emoji only
        f"「{PARTY}」",  # brackets the upstream would strip -> empty after normalize
        f"▼{PARTY}",  # ▼ is deleted by the upstream normalizer
        f"{PARTY}！",  # punctuation only is nothing to pronounce (Q11)
        "。",
        "\x1c",  # .NET does not call this whitespace, Python does (Q18)
    ],
)
def test_nothing_pronounceable_answers_200_silence_without_the_runtime(client, baseline, text):
    response = client.post("/v1/audio/speech", json=speech_body(input=text))
    assert response.status_code == 200, response.text
    assert response.headers["content-type"].startswith("audio/wav")
    assert response.headers["x-ywk-silent"] == "1"
    assert response.headers["content-disposition"] == 'attachment; filename="speech.wav"'
    assert "x-irodori-seed" in response.headers
    info = _wav_info(response.content)
    assert info["format"] == 1 and info["channels"] == 1 and info["bits"] == 16
    assert info["rate"] == 48000  # FakeRuntime has no codec -> the shipped rate
    assert info["samples"] >= 1
    assert set(response.content[info["data_offset"] :]) == {0}
    assert baseline.requests == []  # the runtime was never asked


def test_whitespace_only_input_is_still_the_upstream_400(client, baseline):
    """The one shape the 本体 never sends keeps its 400 (contract 3-3)."""
    response = client.post("/v1/audio/speech", json=speech_body(input="  　\t"))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_empty_input"


def test_silence_does_not_hide_the_upstream_400s(client, baseline):
    """The checks the upstream runs before reading the text still run first."""
    unknown = client.post(
        "/v1/audio/speech", json={"model": MODEL, "input": PARTY * 3, "voice": "いない人"}
    )
    assert unknown.status_code == 400
    assert unknown.json()["error"]["code"] == "ywk_unknown_voice"

    wrong_model = client.post(
        "/v1/audio/speech", json={"model": "wrong-model", "input": PARTY * 3, "voice": DEFAULT_VOICE}
    )
    assert wrong_model.status_code == 400
    assert wrong_model.json()["error"]["code"] == "ywk_unknown_model"

    bad_stream = client.post(
        "/v1/audio/speech", json=speech_body(input=PARTY * 3, stream_format="x")
    )
    assert bad_stream.status_code == 400


def test_silence_is_answered_while_the_runtime_is_not_loaded(client, unloaded, ywk):
    """裁定 165 Q16: nothing to synthesise, so nothing to wait for."""
    response = client.post("/v1/audio/speech", json=speech_body(input=PARTY * 3))
    assert response.status_code == 200, response.text
    assert response.headers["x-ywk-silent"] == "1"
    assert _wav_info(response.content)["rate"] == 48000
    assert unloaded.is_loaded is False
    assert ywk._runtime_error is None
    assert ywk.pending_real_requests() == 0


def test_silence_releases_the_in_flight_counter(client, baseline, ywk):
    client.post("/v1/audio/speech", json=speech_body(input=PARTY))
    assert ywk.pending_real_requests() == 0


@pytest.mark.parametrize("text", [JOY * 3, "\U0001F62D", PAUSE_CANON, f"{JOY}{PARTY}"])
def test_supported_emoji_alone_are_something_to_voice(client, baseline, text):
    """The 本体 sends emoji-only comments on purpose (「絵文字のみは音が出る」):
    a supported one alone is a laugh or a sob, not nothing."""
    response = client.post("/v1/audio/speech", json=speech_body(input=text))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == text.replace(PARTY, "")
    assert "x-ywk-silent" not in response.headers


def test_punctuation_left_behind_a_word_is_kept(client, baseline):
    response = client.post("/v1/audio/speech", json=speech_body(input=f"やったー{PARTY}！"))
    assert response.status_code == 200, response.text
    assert baseline.requests[-1].text == "やったー！"
    assert "x-ywk-silent" not in response.headers


def test_a_chunk_that_normalizes_to_nothing_is_dropped_not_400(client, baseline):
    """The upstream splits at 。 once 80 chars have accumulated; a trailing
    ``「🎉」`` would become a chunk of ``「」`` -> empty -> 400 for the whole
    request.  The wrapper drops that piece and sends the rest."""
    upstream.settings.default_chunking_enabled = True
    head = "あ" * 85 + "。"
    response = client.post("/v1/audio/speech", json=speech_body(input=head + f"「{PARTY}」"))
    assert response.status_code == 200, response.text
    assert [r.text for r in baseline.requests] == [head]
    assert "x-ywk-silent" not in response.headers


def test_chunking_keeps_pronounceable_pieces(client, baseline):
    upstream.settings.default_chunking_enabled = True
    text = "あ" * 85 + "。" + f"「{PARTY}」いい。"
    response = client.post("/v1/audio/speech", json=speech_body(input=text))
    assert response.status_code == 200, response.text
    assert [r.text for r in baseline.requests] == ["あ" * 85 + "。", "「」いい。"]


# ---------------------------------------------------------------------------
# SSE (contract 3-5)
# ---------------------------------------------------------------------------


def test_sse_carries_the_rewritten_text(client, baseline):
    response = client.post(
        "/v1/audio/speech", json=speech_body(input=f"こんにちは{PARTY}", stream_format="sse")
    )
    assert response.status_code == 200, response.text
    frames = _frames(response.text)
    assert [name for name, _ in frames] == ["audio_chunk", "done"]
    assert frames[0][1]["text"] == "こんにちは"
    assert response.headers["x-ywk-emoji-removed"] == "1"


def test_sse_silence_is_one_silent_chunk_then_done(client, baseline, ywk):
    response = client.post(
        "/v1/audio/speech", json=speech_body(input=PARTY * 3, stream_format="sse")
    )
    assert response.status_code == 200, response.text
    assert response.headers["content-type"].startswith("text/event-stream")
    assert response.headers["cache-control"] == "no-cache"
    assert response.headers["x-ywk-silent"] == "1"
    frames = _frames(response.text)
    assert [name for name, _ in frames] == ["audio_chunk", "done"]
    name, data = frames[0]
    assert set(data) == {"index", "text", "format", "media_type", "audio_base64", "seed", "total_to_decode"}
    assert data["index"] == 0 and data["text"] == "" and data["format"] == "wav"
    assert data["media_type"] == "audio/wav"
    info = _wav_info(base64.b64decode(data["audio_base64"]))
    assert info["samples"] >= 1 and info["channels"] == 1 and info["bits"] == 16
    assert frames[1][1] == {"chunks": 1}
    assert baseline.requests == []
    assert ywk.pending_real_requests() == 0


# ---------------------------------------------------------------------------
# The log line and /params
# ---------------------------------------------------------------------------


def test_one_log_line_without_body_text(client, baseline, caplog):
    caplog.set_level(logging.INFO, logger="ywk_server")
    client.post("/v1/audio/speech", json=speech_body(input=f"こんにちは{PARTY}{PARTY}{PRAY}{SKIN}ね"))
    lines = [r.getMessage() for r in caplog.records if r.name == "ywk_server" and r.getMessage().startswith("emoji filtered:")]
    assert len(lines) == 1, caplog.text
    line = lines[0]
    assert "こんにちは" not in line and "ね" not in line
    assert f"removed={PARTY}x2" in line
    assert f"{PRAY}{SKIN}>{PRAY}" in line
    assert "chars=10->7" in line
    assert "silent" not in line


def test_silence_is_marked_in_the_log(client, baseline, caplog):
    caplog.set_level(logging.INFO, logger="ywk_server")
    client.post("/v1/audio/speech", json=speech_body(input=PARTY * 3))
    lines = [r.getMessage() for r in caplog.records if r.getMessage().startswith("emoji filtered:")]
    assert len(lines) == 1
    assert "chars=3->0" in lines[0] and "silent=1" in lines[0]


def test_no_log_line_when_nothing_changed(client, baseline, caplog):
    caplog.set_level(logging.INFO, logger="ywk_server")
    client.post("/v1/audio/speech", json=speech_body(input=f"こんにちは{JOY}"))
    assert not [r for r in caplog.records if r.getMessage().startswith("emoji filtered:")]


def test_invisible_parts_are_logged_as_code_points(client, baseline, caplog):
    caplog.set_level(logging.INFO, logger="ywk_server")
    client.post("/v1/audio/speech", json=speech_body(input=f"おねがい{SKIN}\U0000FE0F"))
    line = next(r.getMessage() for r in caplog.records if r.getMessage().startswith("emoji filtered:"))
    assert "U+1F3FB" in line and "U+FE0F" in line


def test_params_describe_the_rule_and_list_the_45(client):
    params = client.get("/params").json()
    field = params["request"]["input"]
    assert "絵文字" in field["description"]
    assert "無音" in field["description"]
    assert "空白だけは 400" in field["description"]
    rules = params["rules"]["emoji"]
    assert rules["passthrough"] == list(ywk_emoji.SUPPORTED_EMOJIS)
    assert rules["others"] == "removed"
    assert rules["nothing_to_pronounce"] == "silent_wav_200"


def test_regex_is_importable_and_knows_recent_emoji():
    """``regex`` is transformers' dependency, present in all four runtime
    ledgers and in the dev venv; its tables must recognise emoji newer than
    the interpreter's ``unicodedata`` (🫩 is Unicode 16)."""
    import regex

    assert regex.fullmatch(r"\p{Extended_Pictographic}", "\U0001FAE9")
    assert ywk_emoji.filter_text("あ\U0001FAE9い").text == "あい"
