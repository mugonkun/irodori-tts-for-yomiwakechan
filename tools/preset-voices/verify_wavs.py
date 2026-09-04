#!/usr/bin/env python3
"""便 P — wav の諸元と品位を JSON にする（Python 3.13 標準ライブラリ＋struct のみ・numpy 不使用）。

出す値: Hz・ch・bit・秒・RMS（dBFS）・ピーク（dBFS）・先頭/末尾の無音長・クリップ有無。

使い方:
    python verify_wavs.py <wav か ディレクトリ> [...] [--out <report.json>]
                          [--silence-db -50] [--clip-threshold 0.999]
"""

from __future__ import annotations

import argparse
import array
import json
import math
import struct
import sys
import time
from pathlib import Path


def _parse_wav(path: Path) -> tuple[dict, bytes]:
    """RIFF/WAVE を手読みし、(fmt, data のバイト列) を返す。"""
    raw = path.read_bytes()
    if len(raw) < 12 or raw[0:4] != b"RIFF" or raw[8:12] != b"WAVE":
        raise ValueError("RIFF/WAVE ではない")
    pos = 12
    fmt: dict = {}
    payload = b""
    while pos + 8 <= len(raw):
        cid = raw[pos : pos + 4]
        (csize,) = struct.unpack_from("<I", raw, pos + 4)
        body = pos + 8
        end = min(body + csize, len(raw))
        if cid == b"fmt ":
            tag, ch, rate, byte_rate, align, bits = struct.unpack_from("<HHIIHH", raw, body)
            fmt = {
                "format_tag": tag,
                "channels": ch,
                "sample_rate": rate,
                "byte_rate": byte_rate,
                "block_align": align,
                "bits": bits,
            }
        elif cid == b"data":
            payload = raw[body:end]
        pos = body + csize + (csize & 1)
    if not fmt:
        raise ValueError("fmt チャンクがない")
    if not payload:
        raise ValueError("data チャンクがない/空")
    fmt["file_bytes"] = len(raw)
    return fmt, payload


def _samples_mono(fmt: dict, payload: bytes) -> tuple[list[float], int]:
    """-1.0..+1.0 に正規化したモノラル列（ch 平均）と、元のフレーム数を返す。

    対応: PCM 8/16/32bit 整数（tag 1）と IEEE float32（tag 3）。WAVE_FORMAT_EXTENSIBLE(0xFFFE)
    は bits から推定する。
    """
    tag, bits, ch = fmt["format_tag"], fmt["bits"], max(1, fmt["channels"])
    is_float = tag == 3 or (tag == 0xFFFE and bits == 32 and fmt.get("byte_rate", 0) and False)

    if is_float or (tag == 3):
        vals = array.array("f")
        vals.frombytes(payload[: len(payload) - (len(payload) % 4)])
        if sys.byteorder != "little":
            vals.byteswap()
        flat = list(vals)
        scale = 1.0
    elif bits == 16:
        vals = array.array("h")
        vals.frombytes(payload[: len(payload) - (len(payload) % 2)])
        if sys.byteorder != "little":
            vals.byteswap()
        flat = list(vals)
        scale = 32768.0
    elif bits == 32:
        vals = array.array("i")
        vals.frombytes(payload[: len(payload) - (len(payload) % 4)])
        if sys.byteorder != "little":
            vals.byteswap()
        flat = list(vals)
        scale = 2147483648.0
    elif bits == 8:
        # 8bit wav は符号なし（0..255・中心 128）
        flat = [b - 128 for b in payload]
        scale = 128.0
    elif bits == 24:
        n = len(payload) // 3
        flat = []
        for i in range(n):
            b0, b1, b2 = payload[3 * i], payload[3 * i + 1], payload[3 * i + 2]
            v = b0 | (b1 << 8) | (b2 << 16)
            if v & 0x800000:
                v -= 0x1000000
            flat.append(v)
        scale = 8388608.0
    else:
        raise ValueError(f"未対応の形式: format_tag={tag} bits={bits}")

    frames = len(flat) // ch
    if ch == 1:
        mono = [v / scale for v in flat[:frames]]
    else:
        mono = []
        for i in range(frames):
            s = 0.0
            base = i * ch
            for c in range(ch):
                s += flat[base + c]
            mono.append(s / ch / scale)
    return mono, frames


def _db(x: float) -> float | None:
    """振幅（0..1）を dBFS にする。0 は None（-inf の代わり）。"""
    if x <= 0.0:
        return None
    return round(20.0 * math.log10(x), 2)


def analyze(path: Path, silence_db: float, clip_threshold: float) -> dict:
    fmt, payload = _parse_wav(path)
    mono, frames = _samples_mono(fmt, payload)
    rate = fmt["sample_rate"]
    duration = round(frames / rate, 3) if rate else 0.0

    if not mono:
        raise ValueError("サンプルが 0 個")

    peak = max(abs(v) for v in mono)
    rms = math.sqrt(sum(v * v for v in mono) / len(mono))

    # クリップ＝閾値以上の絶対値をとるサンプル数と、その最長連続長。
    # ピーク正規化された音は最大値ちょうどのサンプルが数個だけ孤立して出る（連続 1〜2）。
    # 本当に潰れている音は full scale が連続する。両者を分けるため max_clip_run を出す。
    clipped = 0
    clip_run = 0
    max_clip_run = 0
    for v in mono:
        if abs(v) >= clip_threshold:
            clipped += 1
            clip_run += 1
            if clip_run > max_clip_run:
                max_clip_run = clip_run
        else:
            clip_run = 0
    # 連続 3 サンプル以上を「潰れている」とみなす
    truly_clipped = max_clip_run >= 3

    # 先頭/末尾の無音（silence_db を下回り続ける区間）
    thr = 10.0 ** (silence_db / 20.0)
    lead = 0
    for v in mono:
        if abs(v) > thr:
            break
        lead += 1
    trail = 0
    for v in reversed(mono):
        if abs(v) > thr:
            break
        trail += 1
    all_silent = lead >= len(mono)
    if all_silent:
        trail = 0

    return {
        "file": path.name,
        "path": str(path),
        "sample_rate": rate,
        "channels": fmt["channels"],
        "bits": fmt["bits"],
        "format_tag": fmt["format_tag"],
        "frames": frames,
        "duration_s": duration,
        "file_bytes": fmt["file_bytes"],
        "peak": round(peak, 6),
        "peak_dbfs": _db(peak),
        "rms": round(rms, 6),
        "rms_dbfs": _db(rms),
        "clipped_samples": clipped,
        "max_clip_run": max_clip_run,
        "clipped": truly_clipped,
        "at_full_scale": clipped > 0,
        "lead_silence_s": round(lead / rate, 3) if rate else None,
        "trail_silence_s": round(trail / rate, 3) if rate else None,
        "all_silent": all_silent,
        "silence_threshold_dbfs": silence_db,
        "clip_threshold": clip_threshold,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="wav の諸元・RMS・ピーク・無音・クリップを JSON にする")
    ap.add_argument("targets", nargs="+", help="wav ファイル、またはそれを含むディレクトリ")
    ap.add_argument("--out", default=None, help="JSON の出力先（省略時は標準出力のみ）")
    ap.add_argument("--silence-db", type=float, default=-50.0, help="無音とみなす dBFS（既定 -50）")
    ap.add_argument("--clip-threshold", type=float, default=0.999, help="クリップとみなす絶対値（既定 0.999）")
    args = ap.parse_args()

    files: list[Path] = []
    for t in args.targets:
        p = Path(t)
        if p.is_dir():
            files.extend(sorted(p.glob("*.wav")))
        elif p.is_file():
            files.append(p)
        else:
            print(f"[skip] 見つからない: {p}", file=sys.stderr)
    if not files:
        raise SystemExit("対象の wav が 1 本もない")

    items: list[dict] = []
    for f in files:
        try:
            info = analyze(f, args.silence_db, args.clip_threshold)
        except Exception as exc:  # noqa: BLE001 — 1 本の失敗で全体を落とさない
            info = {"file": f.name, "path": str(f), "error": repr(exc)}
        items.append(info)
        if "error" in info:
            print(f"[NG]   {info['file']}  {info['error']}", flush=True)
        else:
            if info["clipped"]:
                flag = "CLIP"
            elif info["all_silent"]:
                flag = "SILENT"
            elif info["at_full_scale"]:
                flag = "norm"  # ピーク正規化（孤立した full scale）＝異常ではない
            else:
                flag = "ok"
            print(
                f"[{flag:^6}] {info['file']:<44} {info['duration_s']:>6.2f}s {info['sample_rate']:>6}Hz "
                f"{info['channels']}ch/{info['bits']}bit  peak={info['peak_dbfs']} dBFS  "
                f"rms={info['rms_dbfs']} dBFS  lead={info['lead_silence_s']}s trail={info['trail_silence_s']}s "
                f"run={info['max_clip_run']}",
                flush=True,
            )

    report = {
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "silence_threshold_dbfs": args.silence_db,
        "clip_threshold": args.clip_threshold,
        "count": len(items),
        "errors": sum(1 for i in items if "error" in i),
        "clipped": sum(1 for i in items if i.get("clipped")),
        "items": items,
    }
    if args.out:
        op = Path(args.out)
        op.parent.mkdir(parents=True, exist_ok=True)
        op.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"[done] {len(items)} 本。台帳: {op}", flush=True)
    return 1 if report["errors"] else 0


if __name__ == "__main__":
    sys.exit(main())
