#!/usr/bin/env python3
"""便 P — COEIROINK で一次 wav を生成する（Python 3.13 標準ライブラリのみ）。

decisions.md 17・26 が正典。
POST /v1/synthesis に 10 欄すべてを載せる（欠けると 422 になる実装がある）。

使い方:
    python gen_coeiroink.py <出力ディレクトリ> [--host 127.0.0.1] [--port 50032]
                            [--corpus <corpus.json>] [--wait 240] [--only <id>]

出力: <出力ディレクトリ>/<id>_10s.wav・<id>_30s.wav と gen_coeiroink.result.json
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 50032
OUTPUT_SAMPLING_RATE = 44100

# decisions.md 17 のうち COEIROINK 分。uuid / styleId は実機 GET /v1/speakers で確認済み（2026-09-04）。
SPEAKERS = [
    {
        "id": "co_tsukuyomi",
        "display_name": "つくよみちゃん",
        "engine_speaker": "つくよみちゃん",
        "speaker_uuid": "3c37646f-3881-5374-2a83-149267990abc",
        "style_name": "れいせい",
        "style_id": 0,
    },
    {
        "id": "co_kana_naisho",
        "display_name": "KANA",
        "engine_speaker": "KANA",
        "speaker_uuid": "297a5b91-f88a-6951-5841-f1e648b2e594",
        "style_name": "ないしょばなし",
        "style_id": 33,
    },
    {
        "id": "co_mana_isshoukenmei",
        "display_name": "MANA",
        "engine_speaker": "MANA",
        "speaker_uuid": "292ea286-3d5f-f1cc-157c-66462a6a9d08",
        "style_name": "いっしょうけんめい",
        "style_id": 7,
    },
    {
        "id": "co_ofutonp_kiza",
        "display_name": "おふとんP",
        "engine_speaker": "おふとんP",
        "speaker_uuid": "a60ebf6c-626a-7ce6-5d69-c92bf2a1a1d0",
        "style_name": "きざ",
        "style_id": 23,
    },
    {
        "id": "co_ofutonp_normal_v2",
        "display_name": "おふとんP",
        "engine_speaker": "おふとんP",
        "speaker_uuid": "a60ebf6c-626a-7ce6-5d69-c92bf2a1a1d0",
        "style_name": "のーまるv2",
        "style_id": 2,
    },
]


# ---------------------------------------------------------------- HTTP helpers


def _request(url: str, *, data: bytes | None = None, timeout: float = 120.0) -> bytes:
    headers = {"Accept": "*/*"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method="POST" if data is not None else "GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.read()
    except urllib.error.HTTPError as exc:
        body = exc.read()[:2000].decode("utf-8", "replace")
        raise RuntimeError(f"HTTP {exc.code} {url}\n{body}") from exc


def wait_for_engine(host: str, port: int, wait_s: float) -> list[dict]:
    """エンジンの生存を待つ。GET /v1/speakers の中身を返す。"""
    url = f"http://{host}:{port}/v1/speakers"
    deadline = time.time() + wait_s
    last = "(まだ試していない)"
    while time.time() < deadline:
        try:
            return json.loads(_request(url, timeout=10.0))
        except Exception as exc:  # noqa: BLE001 — 起動待ちなので全部握る
            last = repr(exc)
            time.sleep(2.0)
    raise SystemExit(
        f"COEIROINK エンジンが {wait_s:.0f} 秒たっても {url} に応答しない。最後の例外: {last}\n"
        f'  起動例: "C:/Program Files/COEIROINK_WIN_CPU_v.2.13.0/engine/engine.exe"'
    )


def verify_styles(catalog: list[dict], speakers: list[dict]) -> list[str]:
    """GET /v1/speakers の応答で uuid / styleId の実在と名前を確認し、ログ行を返す。"""
    table: dict[tuple[str, int], tuple[str, str]] = {}
    for sp in catalog:
        for st in sp.get("styles", []):
            table[(sp["speakerUuid"], int(st["styleId"]))] = (sp["speakerName"], st["styleName"])
    lines: list[str] = []
    for spec in speakers:
        key = (spec["speaker_uuid"], spec["style_id"])
        if key not in table:
            raise SystemExit(
                f"{spec['id']}: uuid={spec['speaker_uuid']} styleId={spec['style_id']} が"
                " このエンジンに存在しない。GET /v1/speakers を確認せよ。"
            )
        name, style = table[key]
        lines.append(
            f"  {name:<12} styleId={spec['style_id']:>3}  style={style}  "
            f"(台帳: {spec['engine_speaker']} / {spec['style_name']})"
        )
    return lines


# ---------------------------------------------------------------- wav helpers


def wav_info(path: Path) -> dict:
    """wav ヘッダを読んで秒数・Hz・ch・bit を返す（標準ライブラリのみ・struct 手読み）。"""
    data = path.read_bytes()
    if len(data) < 12 or data[0:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"{path} は RIFF/WAVE ではない")
    pos = 12
    fmt: dict = {}
    data_bytes = 0
    while pos + 8 <= len(data):
        cid = data[pos : pos + 4]
        (csize,) = struct.unpack_from("<I", data, pos + 4)
        body = pos + 8
        if cid == b"fmt ":
            tag, ch, rate, _byte_rate, _align, bits = struct.unpack_from("<HHIIHH", data, body)
            fmt = {"format_tag": tag, "channels": ch, "sample_rate": rate, "bits": bits}
        elif cid == b"data":
            data_bytes = min(csize, len(data) - body)
        pos = body + csize + (csize & 1)
    if not fmt or not data_bytes:
        raise ValueError(f"{path} に fmt/data チャンクが揃っていない")
    frame = max(1, fmt["channels"] * fmt["bits"] // 8)
    frames = data_bytes // frame
    return {
        "sample_rate": fmt["sample_rate"],
        "channels": fmt["channels"],
        "bits": fmt["bits"],
        "frames": frames,
        "duration_s": round(frames / fmt["sample_rate"], 3),
        "bytes": len(data),
    }


# ---------------------------------------------------------------- synthesis


def synth(host: str, port: int, text: str, spec: dict) -> bytes:
    """POST /v1/synthesis。10 欄すべてを必ず載せる。"""
    payload = {
        "speakerUuid": spec["speaker_uuid"],
        "styleId": spec["style_id"],
        "text": text,
        "speedScale": 1.0,
        "volumeScale": 1.0,
        "pitchScale": 0.0,
        "intonationScale": 1.0,
        "prePhonemeLength": 0.1,
        "postPhonemeLength": 0.1,
        "outputSamplingRate": OUTPUT_SAMPLING_RATE,
    }
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    return _request(f"http://{host}:{port}/v1/synthesis", data=body, timeout=600.0)


def main() -> int:
    ap = argparse.ArgumentParser(description="COEIROINK で便 P の一次 wav を生成する")
    ap.add_argument("outdir", help="出力ディレクトリ")
    ap.add_argument("--host", default=DEFAULT_HOST)
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--corpus", default=str(Path(__file__).with_name("corpus.json")))
    ap.add_argument("--wait", type=float, default=240.0, help="エンジン起動待ちの秒数")
    ap.add_argument("--only", default=None, help="この id だけ生成する")
    args = ap.parse_args()

    outdir = Path(args.outdir).resolve()
    outdir.mkdir(parents=True, exist_ok=True)

    corpus = json.loads(Path(args.corpus).read_text(encoding="utf-8"))
    texts = {"10s": corpus["primary"]["text_10s"], "30s": corpus["primary"]["text_30s"]}

    speakers = [s for s in SPEAKERS if args.only in (None, s["id"])]
    if not speakers:
        raise SystemExit(f"--only {args.only!r} に一致する話者がない。候補: {[s['id'] for s in SPEAKERS]}")

    print(f"[wait] COEIROINK エンジンを待つ: http://{args.host}:{args.port}/v1/speakers", flush=True)
    catalog = wait_for_engine(args.host, args.port, args.wait)
    print(f"[ok]   GET /v1/speakers = {len(catalog)} 話者", flush=True)

    print("[verify] uuid / styleId を確認:", flush=True)
    for line in verify_styles(catalog, speakers):
        print(line, flush=True)

    results: list[dict] = []
    for spec in speakers:
        for tag, text in texts.items():
            name = f"{spec['id']}_{tag}.wav"
            dest = outdir / name
            t0 = time.perf_counter()
            wav = synth(args.host, args.port, text, spec)
            dest.write_bytes(wav)
            elapsed = time.perf_counter() - t0
            info = wav_info(dest)
            print(
                f"[gen]  {name}  {info['duration_s']:>6.2f} s  {info['sample_rate']} Hz  "
                f"{info['channels']}ch/{info['bits']}bit  ({elapsed:.1f} s で合成)",
                flush=True,
            )
            results.append(
                {
                    "id": spec["id"],
                    "display_name": spec["display_name"],
                    "engine": "coeiroink",
                    "engine_speaker": spec["engine_speaker"],
                    "speaker_uuid": spec["speaker_uuid"],
                    "style": {"name": spec["style_name"], "id": spec["style_id"]},
                    "variant": tag,
                    "file": name,
                    "text": text,
                    "synth_elapsed_s": round(elapsed, 2),
                    **info,
                }
            )

    report = {
        "engine": "coeiroink",
        "endpoint": f"http://{args.host}:{args.port}",
        "outdir": str(outdir),
        "output_sampling_rate": OUTPUT_SAMPLING_RATE,
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "items": results,
    }
    rp = outdir / "gen_coeiroink.result.json"
    rp.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[done] {len(results)} 本。台帳: {rp}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
