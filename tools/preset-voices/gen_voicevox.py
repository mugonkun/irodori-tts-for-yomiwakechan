#!/usr/bin/env python3
"""便 P — VOICEVOX で一次 wav を生成する（Python 3.13 標準ライブラリのみ）。

decisions.md 17・26 が正典。
2 段 API＝POST /audio_query?text=..&speaker=<id> → POST /synthesis?speaker=<id>（body＝query JSON）。

使い方:
    python gen_voicevox.py <出力ディレクトリ> [--host 127.0.0.1] [--port 50021]
                           [--corpus <corpus.json>] [--wait 180] [--only <id>]

出力: <出力ディレクトリ>/<id>_10s.wav・<id>_30s.wav と gen_voicevox.result.json
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 50021

# decisions.md 17 のうち VOICEVOX 分。style_id は実機 GET /speakers で確認済み（2026-09-04）。
SPEAKERS = [
    {
        "id": "vv_mochiko_sexy",
        "display_name": "もち子さん",
        "engine_speaker": "もち子さん",
        "style_name": "セクシー／あん子",
        "style_id": 66,
    },
    {
        "id": "vv_chibishikijii",
        "display_name": "ちび式じい",
        "engine_speaker": "ちび式じい",
        "style_name": "ノーマル",
        "style_id": 42,
    },
]


# ---------------------------------------------------------------- HTTP helpers


def _request(url: str, *, data: bytes | None = None, timeout: float = 120.0) -> bytes:
    headers = {"Accept": "*/*"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method="POST" if data is not None else "GET")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def wait_for_engine(host: str, port: int, wait_s: float) -> str:
    """エンジンの生存を待つ。version 文字列を返す。"""
    url = f"http://{host}:{port}/version"
    deadline = time.time() + wait_s
    last = "(まだ試していない)"
    while time.time() < deadline:
        try:
            body = _request(url, timeout=5.0).decode("utf-8", "replace").strip()
            return body
        except Exception as exc:  # noqa: BLE001 — 起動待ちなので全部握る
            last = repr(exc)
            time.sleep(2.0)
    raise SystemExit(
        f"VOICEVOX エンジンが {wait_s:.0f} 秒たっても {url} に応答しない。最後の例外: {last}\n"
        f'  起動例: "C:/Program Files/VOICEVOX/vv-engine/run.exe" --host {host} --port {port}'
    )


def verify_styles(host: str, port: int, speakers: list[dict]) -> list[str]:
    """GET /speakers で style_id の実在と名前を確認し、ログ行を返す。"""
    raw = _request(f"http://{host}:{port}/speakers", timeout=30.0)
    table: dict[int, tuple[str, str]] = {}
    for sp in json.loads(raw):
        for st in sp.get("styles", []):
            table[int(st["id"])] = (sp["name"], st["name"])
    lines: list[str] = []
    for spec in speakers:
        sid = spec["style_id"]
        if sid not in table:
            raise SystemExit(f"styleId={sid}（{spec['id']}）がこのエンジンに存在しない。GET /speakers を確認せよ。")
        name, style = table[sid]
        lines.append(f"  styleId={sid:>3}  speaker={name}  style={style}  (台帳: {spec['engine_speaker']} / {spec['style_name']})")
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


def synth(host: str, port: int, text: str, style_id: int) -> bytes:
    base = f"http://{host}:{port}"
    q = urllib.parse.urlencode({"text": text, "speaker": style_id})
    query_json = _request(f"{base}/audio_query?{q}", data=b"", timeout=120.0)
    q2 = urllib.parse.urlencode({"speaker": style_id})
    return _request(f"{base}/synthesis?{q2}", data=query_json, timeout=600.0)


def main() -> int:
    ap = argparse.ArgumentParser(description="VOICEVOX で便 P の一次 wav を生成する")
    ap.add_argument("outdir", help="出力ディレクトリ")
    ap.add_argument("--host", default=DEFAULT_HOST)
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--corpus", default=str(Path(__file__).with_name("corpus.json")))
    ap.add_argument("--wait", type=float, default=180.0, help="エンジン起動待ちの秒数")
    ap.add_argument("--only", default=None, help="この id だけ生成する")
    args = ap.parse_args()

    outdir = Path(args.outdir).resolve()
    outdir.mkdir(parents=True, exist_ok=True)

    corpus = json.loads(Path(args.corpus).read_text(encoding="utf-8"))
    texts = {"10s": corpus["primary"]["text_10s"], "30s": corpus["primary"]["text_30s"]}

    speakers = [s for s in SPEAKERS if args.only in (None, s["id"])]
    if not speakers:
        raise SystemExit(f"--only {args.only!r} に一致する話者がない。候補: {[s['id'] for s in SPEAKERS]}")

    print(f"[wait] VOICEVOX エンジンを待つ: http://{args.host}:{args.port}/version", flush=True)
    version = wait_for_engine(args.host, args.port, args.wait)
    print(f"[ok]   VOICEVOX engine version = {version}", flush=True)

    print("[verify] GET /speakers で styleId を確認:", flush=True)
    for line in verify_styles(args.host, args.port, speakers):
        print(line, flush=True)

    results: list[dict] = []
    for spec in speakers:
        for tag, text in texts.items():
            name = f"{spec['id']}_{tag}.wav"
            dest = outdir / name
            t0 = time.perf_counter()
            wav = synth(args.host, args.port, text, spec["style_id"])
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
                    "engine": "voicevox",
                    "engine_speaker": spec["engine_speaker"],
                    "style": {"name": spec["style_name"], "id": spec["style_id"]},
                    "variant": tag,
                    "file": name,
                    "text": text,
                    "synth_elapsed_s": round(elapsed, 2),
                    **info,
                }
            )

    report = {
        "engine": "voicevox",
        "engine_version": version,
        "endpoint": f"http://{args.host}:{args.port}",
        "outdir": str(outdir),
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "items": results,
    }
    rp = outdir / "gen_voicevox.result.json"
    rp.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[done] {len(results)} 本。台帳: {rp}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
