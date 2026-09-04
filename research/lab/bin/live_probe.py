#!/usr/bin/env python3
"""lab 専用: 稼働機の Irodori-TTS-Server(8088) へ実射する（127.0.0.1 固定）。

- 送るのは /health（GET）と /v1/audio/speech（POST）のみ。
- voices の CRUD は一切叩かない（稼働機に檔を足さないため）。
- 逐語記録: 要求 JSON・ステータス・ヘッダ・本文（音声はサイズと音声長）・壁時計。

使い方: python live_probe.py <phase> [args...]
"""
from __future__ import annotations

import base64
import json
import struct
import sys
import time
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:8088"
CAPTION = "落ち着いた女性の声で、丁寧に話す。"
BODY = "こんにちは。今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。"
SHORT = "こんにちは、読み分けちゃんのテストです。"


def wav_info(b: bytes) -> dict:
    """RIFF ヘッダから sr/ch/bits/frames を読む（先頭 44 バイト前提でなく chunk 走査）。"""
    if len(b) < 12 or b[:4] != b"RIFF" or b[8:12] != b"WAVE":
        return {"error": "not RIFF/WAVE", "bytes": len(b), "head": b[:16].hex()}
    pos, out = 12, {"bytes": len(b)}
    while pos + 8 <= len(b):
        cid = b[pos : pos + 4]
        csz = struct.unpack("<I", b[pos + 4 : pos + 8])[0]
        body = b[pos + 8 : pos + 8 + csz]
        if cid == b"fmt ":
            fmt, ch, sr, _br, _ba, bits = struct.unpack("<HHIIHH", body[:16])
            out.update({"fmt_tag": fmt, "channels": ch, "sample_rate": sr, "bits": bits})
        elif cid == b"data":
            out["data_bytes"] = csz
        pos += 8 + csz + (csz & 1)
    if "data_bytes" in out and out.get("sample_rate"):
        out["frames"] = out["data_bytes"] // (out["channels"] * out["bits"] // 8)
        out["audio_seconds"] = round(out["frames"] / float(out["sample_rate"]), 3)
    return out


def post(payload: dict, timeout: float = 600.0) -> dict:
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        BASE + "/v1/audio/speech",
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    t0 = time.perf_counter()
    rec: dict = {"request": payload}
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            body = r.read()
            rec["status"] = r.status
            rec["headers"] = dict(r.headers)
            rec["wall_s"] = round(time.perf_counter() - t0, 3)
            ct = r.headers.get("Content-Type", "")
            if "audio" in ct:
                rec["body"] = wav_info(body)
            else:
                rec["body"] = body.decode("utf-8", "replace")[:4000]
    except urllib.error.HTTPError as e:
        body = e.read()
        rec["status"] = e.code
        rec["headers"] = dict(e.headers)
        rec["wall_s"] = round(time.perf_counter() - t0, 3)
        rec["body"] = body.decode("utf-8", "replace")[:4000]
    except Exception as exc:
        rec["status"] = "EXC"
        rec["wall_s"] = round(time.perf_counter() - t0, 3)
        rec["body"] = repr(exc)
    return rec


def post_sse(payload: dict, timeout: float = 600.0) -> dict:
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        BASE + "/v1/audio/speech",
        data=data,
        headers={"Content-Type": "application/json", "Accept": "text/event-stream"},
        method="POST",
    )
    t0 = time.perf_counter()
    rec: dict = {"request": payload, "events": []}
    buf = b""
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            rec["status"] = r.status
            rec["headers"] = dict(r.headers)
            while True:
                piece = r.read1(65536) if hasattr(r, "read1") else r.read(65536)
                if not piece:
                    break
                buf += piece
                while b"\n\n" in buf:
                    raw, buf = buf.split(b"\n\n", 1)
                    dt = round(time.perf_counter() - t0, 3)
                    ev, dat = None, None
                    for line in raw.decode("utf-8", "replace").split("\n"):
                        if line.startswith("event: "):
                            ev = line[7:]
                        elif line.startswith("data: "):
                            dat = line[6:]
                    e: dict = {"t_since_request_s": dt, "event": ev}
                    if dat:
                        try:
                            d = json.loads(dat)
                        except Exception:
                            e["raw"] = dat[:2000]
                            rec["events"].append(e)
                            continue
                        if "audio_base64" in d:
                            ab = base64.b64decode(d.pop("audio_base64"))
                            e["wav"] = wav_info(ab)
                        e["data"] = d
                    rec["events"].append(e)
    except urllib.error.HTTPError as exc:
        rec["status"] = exc.code
        rec["body"] = exc.read().decode("utf-8", "replace")[:4000]
    except Exception as exc:
        rec["status"] = "EXC"
        rec["body"] = repr(exc)
    rec["wall_s_total"] = round(time.perf_counter() - t0, 3)
    firsts = [e["t_since_request_s"] for e in rec["events"] if e.get("event") == "audio_chunk"]
    if firsts:
        rec["first_audio_chunk_s"] = firsts[0]
    return rec


def health() -> dict:
    t0 = time.perf_counter()
    with urllib.request.urlopen(BASE + "/health", timeout=30) as r:
        j = json.loads(r.read().decode("utf-8"))
    return {"wall_s": round(time.perf_counter() - t0, 3), "json": j}


IRO = {"caption": CAPTION, "no_ref": True}


def main() -> None:
    phase = sys.argv[1]
    out: dict = {"phase": phase}

    if phase == "wait":
        deadline = time.time() + 150
        t0 = time.time()
        n = 0
        while time.time() < deadline:
            n += 1
            try:
                h = health()
                if h["json"].get("runtime", {}).get("loaded") is True:
                    out["polls"] = n
                    out["seconds_to_loaded"] = round(time.time() - t0, 2)
                    out["health"] = h
                    break
                out.setdefault("trace", []).append(
                    {"t": round(time.time() - t0, 2), "loaded": h["json"].get("runtime")}
                )
            except Exception as exc:
                out.setdefault("trace", []).append(
                    {"t": round(time.time() - t0, 2), "err": repr(exc)}
                )
            time.sleep(3)
        else:
            out["timeout"] = True

    elif phase == "P1":
        out["P1"] = post({"model": "irodori-tts", "input": SHORT, "irodori": IRO})
    elif phase == "P2":
        out["P2"] = post({"model": "irodori-tts", "input": SHORT, "irodori": {"caption": CAPTION}})
    elif phase == "P3P5":
        rep = int(sys.argv[2])
        out["P3"] = post({"model": "irodori-tts", "input": BODY, "voice": "none", "irodori": IRO})
        out["P5"] = post(
            {
                "model": "irodori-tts",
                "input": BODY,
                "voice": "none",
                "irodori": dict(IRO, chunking_enabled=False),
            }
        )
        out["rep"] = rep
    elif phase == "P4":
        seed = sys.argv[2]
        iro = dict(
            IRO, chunking_enabled=True, chunk_min_chars=80, first_sentence_chunk_min_chars=1
        )
        if seed != "none":
            iro["seed"] = int(seed)
        out["P4"] = post_sse(
            {
                "model": "irodori-tts",
                "input": BODY,
                "voice": "none",
                "stream_format": "sse",
                "irodori": iro,
            }
        )
    elif phase == "P8":
        shots = []
        for i in range(5):
            shots.append(post({"model": "irodori-tts", "input": SHORT, "voice": "none", "irodori": IRO}))
        out["P8"] = shots
    elif phase == "health":
        out["health"] = health()
    else:
        raise SystemExit("unknown phase " + phase)

    print(json.dumps(out, ensure_ascii=False, indent=2))


main()
