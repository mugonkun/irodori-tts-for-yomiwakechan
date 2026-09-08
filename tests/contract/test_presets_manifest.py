"""``voices/presets.json`` の形の検分（サーバは起こさない・手で保つ正本の網）.

The manifest is the **product's** speaker list: ``PresetVoices.FromPresetsJson``
reads ``display_name`` and hands it to the launcher as the speaker **id**
(裁定 17・契約 ⑷ 4-1), so a typo here is a wrong id in
``GET /v1/audio/voices`` and an ``ywk_unknown_voice`` on the host side
(契約 ⑶ 3-3 の 1 行目 -- ``ywk_invalid_voice_id`` is the upstream *register*
API's charset check, and the distribution removes those 3 routes).  The file is
hand-maintained next to its generators (``tools/preset-voices/make_presets.py``
の ROSTER・``gen_voicevox.py`` の SPEAKERS), which is exactly the pair that
drifts unnoticed -- ``test_the_generators_agree_with_the_manifest`` は
その 3 者を突き合わせる.

裁定 108（司令官の改名指示・2026-09-07）＝「もち子さん」→「もち子さん
（セクシー／あん子）」.  ``engine_speaker`` は VOICEVOX の話者名なので動かない
（表示名だけがスタイルを添える）.  そのずれをこの模組が釘付けする.
"""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path
from types import ModuleType

import pytest

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "voices" / "presets.json"
PRESET_WAVS = ROOT / "voices" / "presets"
GENERATORS = ROOT / "tools" / "preset-voices"
CORPUS = GENERATORS / "corpus.json"

#: 裁定 17 の 12 名（status pending / skipped の行も最初から載っている）。
EXPECTED_ROWS = 12

#: 裁定 108 の 1 件。
MOCHIKO_ID = "vv_mochiko_sexy"
MOCHIKO_DISPLAY_NAME = "もち子さん（セクシー／あん子）"
MOCHIKO_ENGINE_SPEAKER = "もち子さん"
MOCHIKO_STYLE_NAME = "セクシー／あん子"
MOCHIKO_STYLE_ID = 66


@pytest.fixture(scope="module")
def presets() -> list[dict]:
    assert MANIFEST.is_file(), f"missing {MANIFEST}"
    document = json.loads(MANIFEST.read_text(encoding="utf-8"))
    rows = document["presets"]
    assert isinstance(rows, list)
    return rows


@pytest.fixture(scope="module")
def document() -> dict:
    assert MANIFEST.is_file(), f"missing {MANIFEST}"
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def corpus() -> dict:
    assert CORPUS.is_file(), f"missing {CORPUS}"
    return json.loads(CORPUS.read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def by_id(presets) -> dict[str, dict]:
    return {row["id"]: row for row in presets}


def _load_generator(name: str) -> ModuleType:
    """``tools/preset-voices/<name>.py`` を模組として読む（走らせはしない）.

    The generators are scripts, not a package: they live outside ``tests`` and
    are imported by path.  ``make_presets`` puts its own directory on
    ``sys.path`` at import time (it pulls ``verify_wavs`` from next to itself),
    so the path is restored afterwards to keep the rest of the suite clean.
    """
    path = GENERATORS / f"{name}.py"
    assert path.is_file(), f"missing {path}"
    spec = importlib.util.spec_from_file_location(f"_ywk_preset_{name}", path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    saved = list(sys.path)
    try:
        spec.loader.exec_module(module)
    finally:
        sys.path[:] = saved
    return module


def test_manifest_holds_the_twelve_rows(presets):
    assert len(presets) == EXPECTED_ROWS


def test_ids_and_display_names_are_unique(presets):
    ids = [row["id"] for row in presets]
    names = [row["display_name"] for row in presets]
    assert len(set(ids)) == len(ids), "id が重複している（wav の檔名を兼ねる）"
    # 表示名は話者 id そのもの（裁定 17）＝重なると一覧で 1 名が消える。
    assert len(set(names)) == len(names), "display_name が重複している（＝話者 id の衝突）"


def test_mochiko_is_renamed_with_the_style_suffix(by_id):
    row = by_id[MOCHIKO_ID]
    assert row["display_name"] == MOCHIKO_DISPLAY_NAME
    # エンジン側の名は据え置き（VOICEVOX のキャラクタ名＝スタイルは style が持つ）。
    assert row["engine_speaker"] == MOCHIKO_ENGINE_SPEAKER
    assert row["style"]["name"] == MOCHIKO_STYLE_NAME
    assert row["style"]["id"] == MOCHIKO_STYLE_ID


def test_no_row_still_carries_the_old_display_name(presets):
    # 旧い名が 1 行でも残っていれば、利用者の台帳の改名（裁定 108 の引き継ぎ）が
    # 旧い id を「正本に在る名」と見なして止まる。
    assert all(row["display_name"] != MOCHIKO_ENGINE_SPEAKER for row in presets)


def test_the_generators_agree_with_the_manifest(presets, by_id):
    """正本と生成器（ROSTER・SPEAKERS）のずれを釘付けする.

    ``voices/presets.json`` is hand-maintained, so a rename applied to the
    manifest alone would come back the next time somebody re-runs the
    generators.  Compare the 3 places row by row.
    """
    roster = _load_generator("make_presets").ROSTER
    assert len(roster) == len(presets), "ROSTER と正本で行数が違う"

    for row in roster:
        manifest_row = by_id[row["id"]]
        for field in ("display_name", "engine", "engine_speaker", "speaker_uuid", "status"):
            assert manifest_row[field] == row[field], f"{row['id']}: {field} がずれている"
        assert manifest_row["style"] == row["style"], f"{row['id']}: style がずれている"

    # VOICEVOX 分は gen_voicevox.py の SPEAKERS が実際に撃つ側（style_id が本物）。
    for spec in _load_generator("gen_voicevox").SPEAKERS:
        manifest_row = by_id[spec["id"]]
        assert manifest_row["display_name"] == spec["display_name"]
        assert manifest_row["engine_speaker"] == spec["engine_speaker"]
        assert manifest_row["style"]["name"] == spec["style_name"]
        assert manifest_row["style"]["id"] == spec["style_id"]


def test_every_secondary_row_quotes_the_corpus_text_it_names(presets, document, corpus):
    """行の ``secondary.text`` が ``text_id`` の本文と**逐語で**一致すること（裁定 112）.

    掃引は話者を数本だけ撃ち直す。``make_presets.py`` はかつて本文を台帳の**頭**から
    読んでいたので、別の本文で撃ち直した行が**古い本文を名乗る**ところだった
    （裁定 111 で ``generated_at`` について踏んだのと同じ穴・設計書 ben-p §11-2）。
    穴は 2 度とも手で塞いだだけで門が無かった＝この模組がその門。

    頭の ``corpus_texts`` は「いま何種類の本文が混ざっているか」を台帳の頭だけで
    見せる欄なので、行が名乗った id の集合と一致することも同じ 1 本で釘付けする。
    """
    texts = corpus["secondary"]
    named: set[str] = set()
    for row in presets:
        secondary = row.get("secondary")
        if not secondary:
            continue
        text_id = secondary.get("text_id")
        assert text_id, f"{row['id']}: secondary.text_id が無い"
        assert text_id in texts, f"{row['id']}: corpus.json に無い text_id ({text_id})"
        assert secondary.get("text") == texts[text_id]["text"], (
            f"{row['id']}: secondary.text が corpus の {text_id} と違う"
            "（掃引で撃ち直した行が古い本文を名乗っていないか）"
        )
        named.add(text_id)

    assert document.get("corpus_texts") == sorted(named), (
        "頭の corpus_texts が行の text_id の集合と合っていない"
    )


def test_done_rows_have_their_secondary_wav_with_the_recorded_size(presets):
    for row in presets:
        if row.get("status") != "done":
            continue
        secondary = row["secondary"]
        wav = PRESET_WAVS / secondary["file"]
        assert wav.is_file(), f"{row['id']}: missing {wav}"
        assert wav.stat().st_size == secondary["size_bytes"], f"{row['id']}: size drifted"
