"""裁定 160＝本体から参照ボイスを受ける口（契約 ⑷ 4-5）.

``POST /ywk/voices/import`` は**登録口ではない**。上流の書き込み 3 口を外した理由
（api_key が無いポートに multipart の書き込み面を残さない＝契約 ⑷ 4-3・⑼ D-3）は
そのまま生きているので、この口は

* **JSON だけ**（multipart は 415）、
* **``Origin``／``Referer`` を持つ要求は 403**、
* **32 MiB の上限**、
* **受け箱に置くだけ**（``voices.json``／``voices.ywk.json``／``refs\\`` には 1 行も書かない）

という 4 つで別物になっている。ここはその 4 つと、名付けの規則・重複・状態の読みを
釘付けする。名付けの規則はランチャの ``VoiceNameValidator`` と**同じ 1 枚の逐語**
（``voice-name-cases.json``）から引いて、両席が同じ判定になることを確かめる。
"""

from __future__ import annotations

import base64
import json
from pathlib import Path

import pytest

from conftest import DEFAULT_VOICE, JA_VOICE, VOICES, write_voices

NAME_CASES = json.loads(
    (Path(__file__).with_name("voice-name-cases.json")).read_text(encoding="utf-8")
)

#: 最短の本物らしい wav（RIFF の印だけ見る＝中身の検分はランチャの仕事）。
WAV = b"RIFF\x24\x00\x00\x00WAVE" + b"\x00" * 64
MP3_ID3 = b"ID3\x04\x00\x00\x00\x00\x00\x00" + b"\x00" * 32
MP3_SYNC = b"\xff\xfb\x90\x00" + b"\x00" * 32
FLAC = b"fLaC\x00\x00\x00\x22" + b"\x00" * 32
OGG = b"OggS\x00\x02\x00\x00" + b"\x00" * 32

JSON_HEADERS = {"Content-Type": "application/json"}


def inbox() -> Path:
    return VOICES / "inbox"


def inbox_files() -> list[str]:
    root = inbox()
    return sorted(path.name for path in root.iterdir()) if root.is_dir() else []


def body(**overrides):
    payload = {
        "display_name": "受け取った声",
        "audio_base64": base64.b64encode(WAV).decode("ascii"),
        "format": "wav",
    }
    payload.update(overrides)
    return payload


def post(client, payload, headers=None):
    """``json=`` を使わない＝``Content-Type`` そのものを試す回があるため。"""
    return client.post(
        "/ywk/voices/import",
        content=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers=JSON_HEADERS if headers is None else headers,
    )


# ---- 受け取れた回 ---------------------------------------------------------


def test_受け取ると202で2檔が置かれる(client):
    response = post(client, body(caption="明るく", client="yomiwakechan2"))
    assert response.status_code == 202
    payload = response.json()
    import_id = payload["id"]
    assert payload["state"] == "queued"
    assert payload["display_name"] == "受け取った声"
    assert len(import_id) == 24 and all(c in "0123456789abcdef" for c in import_id)

    assert inbox_files() == sorted([f"{import_id}.json", f"{import_id}.wav"])
    assert (inbox() / f"{import_id}.wav").read_bytes() == WAV

    sidecar = json.loads((inbox() / f"{import_id}.json").read_text(encoding="utf-8"))
    assert sidecar["id"] == import_id
    assert sidecar["display_name"] == "受け取った声"
    assert sidecar["caption"] == "明るく"
    assert sidecar["client"] == "yomiwakechan2"
    assert sidecar["format"] == "wav"
    assert sidecar["state"] == "queued"
    assert sidecar["error"] is None
    assert sidecar["bytes"] == len(WAV)
    assert sidecar["received_at"].endswith("Z")


def test_受け取っても話者台帳には1行も書かない(client):
    """所有者はランチャのまま（契約 ⑷ 4-3）＝wrapper は受け箱にしか書かない。"""
    before = json.loads((VOICES / "voices.json").read_text(encoding="utf-8"))
    assert post(client, body()).status_code == 202
    after = json.loads((VOICES / "voices.json").read_text(encoding="utf-8"))
    assert after == before
    assert not (VOICES / "voices.ywk.json").exists()
    assert not (VOICES / "refs").exists()
    # 話者はまだ 1 名も増えていない。
    assert [item["id"] for item in client.get("/ywk/voices").json()["data"]] == [DEFAULT_VOICE]


def test_状態の口に件数が載る(client):
    assert client.get("/ywk/status").json()["voices"]["inbox"] == 0
    assert post(client, body()).status_code == 202
    assert client.get("/ywk/status").json()["voices"]["inbox"] == 1
    assert post(client, body(display_name="もう 1 名")).status_code == 202
    assert client.get("/ywk/status").json()["voices"]["inbox"] == 2


def test_件数の欄は既存の欄の後ろに足す(client):
    """**欄を足すだけ**なので ``schema`` は上げない（契約 ⑻）＝並びは据え置き。"""
    voices = client.get("/ywk/status").json()["voices"]
    assert list(voices.keys()) == ["count", "dir", "error", "inbox"]


def test_受け取った直後の状態はqueued(client):
    import_id = post(client, body()).json()["id"]
    payload = client.get(f"/ywk/voices/import/{import_id}").json()
    assert payload == {
        "id": import_id,
        "state": "queued",
        "display_name": "受け取った声",
        "error": None,
        "received_at": payload["received_at"],
    }


@pytest.mark.parametrize(
    ("fmt", "data"),
    [
        ("wav", WAV),
        ("mp3", MP3_ID3),
        ("mp3", MP3_SYNC),
        ("flac", FLAC),
        ("ogg", OGG),
        ("opus", OGG),
    ],
)
def test_受ける形は5つ(client, fmt, data):
    response = post(
        client,
        body(format=fmt, audio_base64=base64.b64encode(data).decode("ascii")),
    )
    assert response.status_code == 202
    assert any(name.endswith("." + fmt) for name in inbox_files())


# ---- ブラウザ避け ---------------------------------------------------------


@pytest.mark.parametrize("header", ["Origin", "Referer"])
def test_ブラウザ由来の要求は403で何も書かない(client, header):
    headers = dict(JSON_HEADERS)
    headers[header] = "https://example.invalid/page"
    response = post(client, body(), headers=headers)
    assert response.status_code == 403
    assert response.json()["error"]["code"] == "ywk_browser_origin"
    assert inbox_files() == []


def test_multipartは415(client):
    response = client.post(
        "/ywk/voices/import",
        files={"file": ("a.wav", WAV, "audio/wav")},
        data={"display_name": "受け取った声"},
    )
    assert response.status_code == 415
    assert response.json()["error"]["code"] == "ywk_import_json_only"
    assert inbox_files() == []


def test_content_typeがformでも415(client):
    response = client.post(
        "/ywk/voices/import",
        content=b"display_name=x",
        headers={"Content-Type": "application/x-www-form-urlencoded"},
    )
    assert response.status_code == 415
    assert response.json()["error"]["code"] == "ywk_import_json_only"
    assert inbox_files() == []


# ---- body の検分 ----------------------------------------------------------


def test_未知の欄は400(client):
    response = post(client, body(audio_b64="x"))
    assert response.status_code == 400
    error = response.json()["error"]
    assert error["code"] == "ywk_unknown_field"
    assert error["message"] == "unknown field: audio_b64"
    assert inbox_files() == []


def test_JSONでなければ400(client):
    response = client.post(
        "/ywk/voices/import", content=b"{ not json", headers=JSON_HEADERS
    )
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_invalid_body"


def test_知らない形は400(client):
    response = post(client, body(format="m4a"))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_invalid_enum"


def test_captionが長すぎれば400(client):
    response = post(client, body(caption="あ" * 201))
    assert response.status_code == 400
    error = response.json()["error"]
    assert error["code"] == "ywk_out_of_range"
    assert error["param"] == "caption"


def test_clientが長すぎれば400(client):
    response = post(client, body(client="a" * 65))
    assert response.status_code == 400
    assert response.json()["error"]["param"] == "client"


def test_解けないbase64は400(client):
    response = post(client, body(audio_base64="これは base64 ではない"))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_import_bad_audio"
    assert inbox_files() == []


def test_形と中身が合わなければ400(client):
    """wav と名乗る flac＝**登録の後ではなく受け箱で**落とす。"""
    response = post(
        client, body(format="wav", audio_base64=base64.b64encode(FLAC).decode("ascii"))
    )
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_import_bad_audio"
    assert inbox_files() == []


def test_上限は32MiB(ywk):
    assert ywk.IMPORT_MAX_BYTES == 32 * 1024 * 1024


def test_大きすぎれば413(client, ywk, monkeypatch):
    """上限そのものは上の 1 行が釘付けする＝ここは**越えたときの態**を見る。

    本物の 32 MiB を毎回 base64 に起こすと 45 MB の本文になるので、上限だけを
    小さく差し替えて同じ路を通す（``IMPORT_MAX_BYTES`` は呼ばれるたびに読まれる）。
    """
    monkeypatch.setattr(ywk, "IMPORT_MAX_BYTES", 32)
    response = post(
        client,
        body(audio_base64=base64.b64encode(WAV + b"\x00" * 64).decode("ascii")),
    )
    assert response.status_code == 413
    assert response.json()["error"]["code"] == "ywk_import_too_large"
    assert inbox_files() == []


def test_解く前に大きさを見る(client, ywk, monkeypatch):
    """**base64 の長さで先に断る**＝32 MiB 超の文字列をメモリに展開しない。"""
    monkeypatch.setattr(ywk, "IMPORT_MAX_BYTES", 8)
    response = post(client, body(audio_base64="A" * 4096))
    assert response.status_code == 413
    assert response.json()["error"]["code"] == "ywk_import_too_large"


# ---- 名付けの規則（ランチャと同じ 1 枚から） -------------------------------


@pytest.mark.parametrize(
    ("name", "valid", "why"),
    [(case["name"], case["valid"], case["why"]) for case in NAME_CASES["cases"]],
)
def test_名付けの規則はランチャと同じ(ywk, name, valid, why):
    assert (ywk.validate_voice_name(name) is None) is valid, why


def test_上限の数はランチャと同じ64(ywk):
    assert ywk.IMPORT_NAME_MAX == NAME_CASES["max_length_utf16"] == 64


@pytest.mark.parametrize(
    "name",
    [case["name"] for case in NAME_CASES["cases"] if not case["valid"]],
)
def test_通らない名は400(client, name):
    response = post(client, body(display_name=name))
    assert response.status_code == 400
    error = response.json()["error"]
    assert error["code"] == "ywk_import_bad_name"
    assert error["param"] == "display_name"
    assert inbox_files() == []


def test_名が文字列でなければ400(client):
    response = post(client, body(display_name=7))
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_import_bad_name"


def test_名の前後の空白は落として置く(client):
    import_id = post(client, body(display_name="  ずんだもん  ")).json()["id"]
    sidecar = json.loads((inbox() / f"{import_id}.json").read_text(encoding="utf-8"))
    assert sidecar["display_name"] == "ずんだもん"


# ---- 重複 ----------------------------------------------------------------


def test_既にいる話者と同じ名は409(client):
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "alice.wav"}},
        files=("alice.wav",),
    )
    response = post(client, body(display_name=JA_VOICE))
    assert response.status_code == 409
    assert response.json()["error"]["code"] == "ywk_voice_exists"
    assert inbox_files() == []


def test_配布版の台帳にいる話者と同じ名も409(client):
    (VOICES / "voices.ywk.json").write_text(
        json.dumps(
            {"schema": 1, "voices": {"台帳だけの声": {"display_name": "台帳だけの声"}}},
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )
    response = post(client, body(display_name="台帳だけの声"))
    assert response.status_code == 409
    assert response.json()["error"]["code"] == "ywk_voice_exists"


def test_受け箱で待っている名と同じなら409(client):
    assert post(client, body(display_name="二重の声")).status_code == 202
    response = post(client, body(display_name="二重の声"))
    assert response.status_code == 409
    assert response.json()["error"]["code"] == "ywk_voice_exists"
    # 1 件目の 2 檔だけが在る。
    assert len(inbox_files()) == 2


@pytest.mark.parametrize("state", ["done", "failed"])
def test_決着のついた受け箱は重複にしない(client, state):
    """``done``／``failed`` の sidecar は残る（記録）が、同じ名の取り直しを塞がない。

    重複を見るのは**まだ登録していない分**（``queued``／``registering``）だけである。
    ``done`` の 1 件が塞ぐと「登録が済んだ声をもう 1 度渡せない」ことになるが、それは
    **話者としての重複**（``_existing_voice_ids``）が別に見ている態で、受け箱の役目ではない。
    """
    import_id = post(client, body(display_name="やり直す声")).json()["id"]
    sidecar_path = inbox() / f"{import_id}.json"
    record = json.loads(sidecar_path.read_text(encoding="utf-8"))
    record["state"] = state
    sidecar_path.write_text(json.dumps(record, ensure_ascii=False), encoding="utf-8")

    assert post(client, body(display_name="やり直す声")).status_code == 202


# ---- 状態の読み ----------------------------------------------------------


def test_ランチャが進めた状態がそのまま見える(client):
    import_id = post(client, body()).json()["id"]
    sidecar_path = inbox() / f"{import_id}.json"

    for state in ("registering", "done"):
        record = json.loads(sidecar_path.read_text(encoding="utf-8"))
        record["state"] = state
        sidecar_path.write_text(json.dumps(record, ensure_ascii=False), encoding="utf-8")
        assert client.get(f"/ywk/voices/import/{import_id}").json()["state"] == state

    assert client.get("/ywk/status").json()["voices"]["inbox"] == 0


def test_失敗は理由1行つきで見える(client):
    import_id = post(client, body()).json()["id"]
    sidecar_path = inbox() / f"{import_id}.json"
    record = json.loads(sidecar_path.read_text(encoding="utf-8"))
    record["state"] = "failed"
    record["error"] = "音声ファイルを読めませんでした。"
    sidecar_path.write_text(json.dumps(record, ensure_ascii=False), encoding="utf-8")

    payload = client.get(f"/ywk/voices/import/{import_id}").json()
    assert payload["state"] == "failed"
    assert payload["error"] == "音声ファイルを読めませんでした。"


def test_読めない状態は失敗と読む(client):
    """本体を永久に待たせない＝知らない ``state`` は ``failed``。"""
    import_id = post(client, body()).json()["id"]
    sidecar_path = inbox() / f"{import_id}.json"
    record = json.loads(sidecar_path.read_text(encoding="utf-8"))
    record["state"] = "なにか"
    sidecar_path.write_text(json.dumps(record, ensure_ascii=False), encoding="utf-8")

    assert client.get(f"/ywk/voices/import/{import_id}").json()["state"] == "failed"


@pytest.mark.parametrize(
    "bad", ["000000000000000000000000", "abc", "ABCDEF012345678901234567", "a" * 25]
)
def test_知らないidは404(client, bad):
    response = client.get(f"/ywk/voices/import/{bad}")
    assert response.status_code == 404
    assert response.json()["error"]["code"] == "ywk_import_unknown"


@pytest.mark.parametrize("bad", ["..%2F..", "..%2F..%2Fvoices.json", "%2E%2E"])
def test_路を遡るidは口に届かない(client, bad):
    """id は 16 進 24 字の形でしか通さない＝檔の路を組み立てさせない。"""
    response = client.get(f"/ywk/voices/import/{bad}")
    assert response.status_code == 404
    assert str(response.json()["error"]["code"]).startswith("ywk_")


def test_エラーの本文に絶対パスが0件(client):
    """契約 ⑶ 3-3＝どの態でもパスを出さない。"""
    responses = [
        post(client, body(display_name="")),
        post(client, body(audio_base64="!!!")),
        client.get("/ywk/voices/import/abc"),
    ]
    for response in responses:
        text = response.text
        assert "C:\\" not in text and "C:/" not in text
        assert str(VOICES) not in text
