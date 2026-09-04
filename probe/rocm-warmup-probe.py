#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""便 C の実射台本（HTTP 側）。設計書 `docs/design/ben-c-radeon.md` §2-4。

この檔は **測るだけ** である。サーバの起動・停止・GPU メモリの採取は
`probe/rocm-warmup-probe.ps1` が行い、この檔はそれが起こした 1 個体に
HTTP で話しかけて数値を JSON で返す。数値は測った物だけを書き、
測れなかった欄は ``null`` にする（手で写した値を混ぜない＝`probe/README.md` §1）。

依存は標準ライブラリだけ（`urllib`・`wave`・`json`）。torch は読まない
＝この檔は GPU を一切掴まない。走らせる python は `.venv-dev` の物でよい。

小口（`--step`）:

``prepare``
    `voices/presets.json` を読み、二次 wav（`voices/presets/<id>.wav`）を
    `build/out/app/voices/` へ複写し、`voices.json`（デフォルト＋11 名）を書く。
    `voices/` 自体は読むだけ。
``warmup``
    `POST /ywk/warmup` を撃ち、`/ywk/status.warmup` を 50 ms 間隔で読んで
    **1 射ごとの ms** を集める（`last_shot` は 1 射分しか残らないため、
    `shots_done` が増えた瞬間に拾う）。
``shots``
    `POST /v1/audio/speech` を順に撃ち、所要 ms・返り byte 数・wav の秒数・
    RTF（所要 s ÷ wav 秒数）を採る。
``priority``
    暖機を走らせ、その最中に**本物の要求**を 1 本入れて待たされた時間を測る
    （受け入れ条件 C-5）。
``presets``
    11 プリセットを参照にして 1 射ずつ（受け入れ条件 C-8）。
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import shutil
import sys
import time
import urllib.error
import urllib.request
import wave
from pathlib import Path
from typing import Any

DEFAULT_VOICE_ID = "デフォルト"
#: 実射に使う短文（19 字）。暖機の既定文とは別にする＝暖機で焼いた S_lat と
#: 本物の要求の S_lat が偶然一致しないようにするため。
SHORT_TEXT = "こんにちは、読み分けちゃんの実射です。"
WARMUP_TEXT = "暖機です。"


# --------------------------------------------------------------------------- http


class Answer:
    """1 回の HTTP のなま結果。"""

    def __init__(self, status: int, body: bytes, ctype: str, ms: float) -> None:
        self.status = status
        self.body = body
        self.ctype = ctype
        self.ms = ms

    def json(self) -> Any:
        try:
            return json.loads(self.body.decode("utf-8"))
        except Exception:  # noqa: BLE001 - 生のまま返す
            return None

    def text(self) -> str:
        try:
            return self.body.decode("utf-8", "replace")
        except Exception:  # noqa: BLE001
            return ""


def http(base: str, path: str, *, method: str = "GET", payload: Any = None, timeout: float = 1800.0) -> Answer:
    url = base.rstrip("/") + path
    data = None
    headers = {"Accept": "*/*", "User-Agent": "irodori-tts-ywk-rocm-probe/1"}
    if payload is not None:
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json; charset=utf-8"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            body = resp.read()
            ms = (time.perf_counter() - started) * 1000.0
            return Answer(int(resp.status), body, str(resp.headers.get("Content-Type", "")), ms)
    except urllib.error.HTTPError as exc:
        body = exc.read()
        ms = (time.perf_counter() - started) * 1000.0
        return Answer(int(exc.code), body, str(exc.headers.get("Content-Type", "")), ms)
    except Exception as exc:  # noqa: BLE001 - 通信そのものが立たない場合
        ms = (time.perf_counter() - started) * 1000.0
        return Answer(0, str(exc).encode("utf-8"), "", ms)


def wav_seconds(data: bytes) -> float | None:
    """返ってきた wav の秒数。RIFF/WAVE でなければ None。"""
    if len(data) < 44 or data[0:4] != b"RIFF" or data[8:12] != b"WAVE":
        return None
    try:
        with wave.open(io.BytesIO(data), "rb") as handle:
            frames = handle.getnframes()
            rate = handle.getframerate()
        if rate <= 0:
            return None
        return frames / float(rate)
    except Exception:  # noqa: BLE001
        return None


def status(base: str) -> dict[str, Any]:
    ans = http(base, "/ywk/status", timeout=60.0)
    body = ans.json()
    return body if isinstance(body, dict) else {"_http_status": ans.status, "_body": ans.text()[:400]}


# --------------------------------------------------------------------------- shots


def speech_body(voice: str, text: str, num_steps: int | None) -> dict[str, Any]:
    body: dict[str, Any] = {"model": "irodori-tts", "input": text, "response_format": "wav"}
    if voice == "@no_ref":
        body["irodori"] = {"no_ref": True}
    else:
        body["voice"] = DEFAULT_VOICE_ID if voice == "@default" else voice
    if num_steps is not None:
        body.setdefault("irodori", {})
        body["irodori"]["num_steps"] = int(num_steps)
    return body


def fire(base: str, name: str, voice: str, text: str, num_steps: int | None) -> dict[str, Any]:
    body = speech_body(voice, text, num_steps)
    ans = http(base, "/v1/audio/speech", method="POST", payload=body)
    seconds = wav_seconds(ans.body) if ans.status == 200 else None
    row: dict[str, Any] = {
        "name": name,
        "voice": voice,
        "text_chars": len(text),
        "num_steps": num_steps,
        "http_status": ans.status,
        "ms": round(ans.ms, 1),
        "bytes": len(ans.body),
        "wav_seconds": None if seconds is None else round(seconds, 3),
        "rtf": None if not seconds else round((ans.ms / 1000.0) / seconds, 4),
        "error": None if ans.status == 200 else ans.text()[:600],
    }
    return row


# --------------------------------------------------------------------------- steps


def step_prepare(args: argparse.Namespace) -> dict[str, Any]:
    """二次 wav を build/out/app/voices/ に置き、voices.json を書く。"""
    presets_json = Path(args.presets_json)
    presets_dir = Path(args.presets_dir)
    app_voices = Path(args.app_voices)
    app_voices.mkdir(parents=True, exist_ok=True)

    payload = json.loads(presets_json.read_text(encoding="utf-8"))
    rows: list[dict[str, Any]] = []
    aliases: dict[str, Any] = {DEFAULT_VOICE_ID: {"no_ref": True}}
    for preset in payload.get("presets", []):
        secondary = preset.get("secondary") or {}
        name = secondary.get("file")
        if not name:
            continue
        src = presets_dir / name
        if not src.is_file():
            raise SystemExit(f"preset wav is missing: {src}")
        dst = app_voices / name
        shutil.copyfile(src, dst)
        raw = dst.read_bytes()
        md5 = hashlib.md5(raw).hexdigest()  # noqa: S324 - presets.json と突合するためだけ
        if secondary.get("md5") and secondary["md5"] != md5:
            raise SystemExit(f"{name}: md5 {md5} != presets.json {secondary['md5']}")
        voice_id = str(preset["id"])
        aliases[voice_id] = name
        rows.append(
            {
                "id": voice_id,
                "display_name": preset.get("display_name"),
                "file": name,
                "bytes": len(raw),
                "md5": md5,
                "ref_seconds": secondary.get("duration_s"),
                "wav_seconds": (lambda s: None if s is None else round(s, 3))(wav_seconds(raw)),
            }
        )

    voices_json = app_voices / "voices.json"
    voices_json.write_text(
        json.dumps(aliases, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    return {
        "step": "prepare",
        "app_voices": str(app_voices),
        "voices_json": str(voices_json),
        "alias_count": len(aliases),
        "presets": rows,
    }


def poll_warmup(base: str, *, poll_s: float, timeout_s: float) -> dict[str, Any]:
    """暖機が終わるまで `/ywk/status.warmup` を読み、1 射ごとの ms を拾う。"""
    shots: list[dict[str, Any]] = []
    seen = 0
    started = time.perf_counter()
    final: dict[str, Any] = {}
    missed = False
    while True:
        snap = status(base).get("warmup") or {}
        done = int(snap.get("shots_done") or 0)
        if done > seen and snap.get("last_shot"):
            if done > seen + 1:
                # 2 射以上まとめて進んだ＝間の射の ms を取り逃した。黙って
                # 埋めず、取り逃したことを記録する（stderr の 1 射 1 行が正）。
                missed = True
            last = dict(snap["last_shot"])
            last["index"] = done
            last["at_s"] = round(time.perf_counter() - started, 3)
            shots.append(last)
            seen = done
        final = snap
        if str(snap.get("state")) != "running":
            break
        if time.perf_counter() - started > timeout_s:
            final = dict(final)
            final["_timeout"] = True
            break
        time.sleep(poll_s)
    return {
        "shots": shots,
        "final": final,
        "missed_shots": missed,
        "wall_s": round(time.perf_counter() - started, 3),
    }


def step_warmup(args: argparse.Namespace) -> dict[str, Any]:
    stages = [float(x) for x in args.stages.split(",") if x.strip()] if args.stages else []
    voices = [v for v in (args.voices.split(",") if args.voices else []) if v.strip()]
    body: dict[str, Any] = {"stages": stages, "voices": voices, "text": args.text}
    before = status(args.base)
    ans = http(args.base, "/ywk/warmup", method="POST", payload=body, timeout=120.0)
    out: dict[str, Any] = {
        "step": "warmup",
        "label": args.label,
        "request": body,
        "http_status": ans.status,
        "response": ans.json(),
        "status_before": before,
    }
    if ans.status != 202:
        out["error"] = ans.text()[:600]
        return out
    polled = poll_warmup(args.base, poll_s=args.poll, timeout_s=args.timeout)
    out["shots"] = polled["shots"]
    out["final"] = polled["final"]
    out["missed_shots"] = polled["missed_shots"]
    out["wall_s"] = polled["wall_s"]
    out["status_after"] = status(args.base)
    return out


def step_shots(args: argparse.Namespace) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    for spec in args.shot:
        name, _, voice = spec.partition("=")
        if not voice:
            raise SystemExit(f"--shot wants NAME=VOICE, got {spec!r}")
        rows.append(fire(args.base, name, voice, args.text, args.num_steps))
    return {
        "step": "shots",
        "label": args.label,
        "text": args.text,
        "shots": rows,
        "status_after": status(args.base),
    }


def step_priority(args: argparse.Namespace) -> dict[str, Any]:
    """暖機の最中に本物の要求を 1 本入れて、待たされた時間を測る（C-5）。

    ⑴ 暖機を起こす → ⑵ ``--delay`` 秒待って本物を 1 本撃つ（そのときの
    ``shots_done`` を控える）→ ⑶ 本物が返るまでの ms を採る → ⑷ 暖機の残りを
    見届ける。判定は「本物が待たされた時間 ≦ 暖機 1 射の所要」である。
    """
    stages = [float(x) for x in args.stages.split(",") if x.strip()] if args.stages else []
    body = {"stages": stages, "voices": [], "text": WARMUP_TEXT}
    ans = http(args.base, "/ywk/warmup", method="POST", payload=body, timeout=120.0)
    out: dict[str, Any] = {
        "step": "priority",
        "request": body,
        "http_status": ans.status,
        "response": ans.json(),
    }
    if ans.status != 202:
        out["error"] = ans.text()[:600]
        return out

    time.sleep(args.delay)
    at_fire = status(args.base).get("warmup") or {}
    real = fire(args.base, "real_during_warmup", args.voice, args.text, args.num_steps)
    after = status(args.base).get("warmup") or {}
    polled = poll_warmup(args.base, poll_s=args.poll, timeout_s=args.timeout)

    out["warmup_at_fire"] = at_fire
    out["warmup_after_real"] = after
    out["real"] = real
    out["shots_after"] = polled["shots"]
    out["missed_shots"] = polled["missed_shots"]
    out["final"] = polled["final"]
    # 暖機が本当に走っている最中に撃てたか（走り終えた後なら検分にならない）
    out["fired_while_running"] = str(at_fire.get("state")) == "running"
    return out


def step_presets(args: argparse.Namespace) -> dict[str, Any]:
    ids = [v for v in args.ids.split(",") if v.strip()]
    rows: list[dict[str, Any]] = []
    for voice_id in ids:
        rows.append(fire(args.base, voice_id, voice_id, args.text, args.num_steps))
    ok = [r for r in rows if r["http_status"] == 200 and r["rtf"] is not None]
    return {
        "step": "presets",
        "label": args.label,
        "text": args.text,
        "num_steps": args.num_steps,
        "shots": rows,
        "count": len(rows),
        "count_200": len([r for r in rows if r["http_status"] == 200]),
        "rtf_max": max((r["rtf"] for r in ok), default=None),
        "rtf_min": min((r["rtf"] for r in ok), default=None),
        "status_after": status(args.base),
    }


def step_status(args: argparse.Namespace) -> dict[str, Any]:
    return {"step": "status", "label": args.label, "status": status(args.base)}


# --------------------------------------------------------------------------- main


def main(argv: list[str]) -> int:
    # Speaker names and error texts are Japanese; a console left on cp932 would turn a real
    # error into a UnicodeEncodeError and hide it. The JSON on disk is UTF-8 either way.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")  # type: ignore[union-attr]
        except Exception:  # noqa: BLE001 - a stream that cannot be reconfigured is fine
            pass
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", required=True, help="この段の結果を書く JSON の道")
    sub = parser.add_subparsers(dest="step", required=True)

    p = sub.add_parser("prepare")
    p.add_argument("--presets-json", required=True)
    p.add_argument("--presets-dir", required=True)
    p.add_argument("--app-voices", required=True)
    p.set_defaults(func=step_prepare)

    p = sub.add_parser("warmup")
    p.add_argument("--base", required=True)
    p.add_argument("--stages", default="4,8,12")
    p.add_argument("--voices", default="")
    p.add_argument("--text", default=WARMUP_TEXT)
    p.add_argument("--label", default="")
    p.add_argument("--poll", type=float, default=0.05)
    p.add_argument("--timeout", type=float, default=1800.0)
    p.set_defaults(func=step_warmup)

    p = sub.add_parser("shots")
    p.add_argument("--base", required=True)
    p.add_argument("--shot", action="append", default=[], help="NAME=VOICE（@default／@no_ref も可）")
    p.add_argument("--text", default=SHORT_TEXT)
    p.add_argument("--num-steps", type=int, default=None)
    p.add_argument("--label", default="")
    p.set_defaults(func=step_shots)

    p = sub.add_parser("priority")
    p.add_argument("--base", required=True)
    p.add_argument("--stages", default="5,7,9,11")
    p.add_argument("--voice", default="@default")
    p.add_argument("--text", default=SHORT_TEXT)
    p.add_argument("--num-steps", type=int, default=None)
    p.add_argument("--delay", type=float, default=0.4)
    p.add_argument("--poll", type=float, default=0.05)
    p.add_argument("--timeout", type=float, default=1800.0)
    p.set_defaults(func=step_priority)

    p = sub.add_parser("presets")
    p.add_argument("--base", required=True)
    p.add_argument("--ids", required=True)
    p.add_argument("--text", default=SHORT_TEXT)
    p.add_argument("--num-steps", type=int, default=None)
    p.add_argument("--label", default="")
    p.set_defaults(func=step_presets)

    p = sub.add_parser("status")
    p.add_argument("--base", required=True)
    p.add_argument("--label", default="")
    p.set_defaults(func=step_status)

    args = parser.parse_args(argv)
    result = args.func(args)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    sys.stdout.write(json.dumps({"step": result.get("step"), "out": str(out)}, ensure_ascii=False) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
