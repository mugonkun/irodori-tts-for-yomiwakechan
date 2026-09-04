#!/usr/bin/env python3
"""便 P — wav の諸元と品位を JSON にする（Python 3.13 標準ライブラリ＋struct のみ・numpy 不使用）。

出す値: Hz・ch・bit・秒・RMS（dBFS）・ピーク（dBFS）・先頭/末尾の無音長・クリップ有無
       ・末尾判定（decisions 39）。

末尾判定（tail_verdict）＝**3 条件すべて**を満たしたときだけ `clean`。1 つでも外れたら
`tail_suspect`（＝語の途中で終端している疑い）。

    ⒜ 相対  tail_delta_db = tail50_dbfs - rms_dbfs  が  <= -20.0
    ⒝ 絶対  tail50_dbfs  が  <= -40.0 dBFS
    ⒞ 立ち上がり無し
       末尾 300 ms を 25 ms 刻みに割り、最後の 100 ms の各区間が
       直前の区間より +0.5 dB を超えて上がっていない（＝単調減衰）。
       ただし -60 dBFS 以下の区間は「無音」とみなし、立ち上がりに数えない。

⒜ だけだった旧規則は、末尾がいったん無音に落ちてから**また鳴り出して切れる**檔
（月読アイ・Δ=-20.19 dB で縁を通過）を取り逃がした。⒝ が絶対水準で、⒞ が形で押さえる。
末尾が完全な無音（振幅 0）なら 3 条件とも満たす＝clean。

窓・閾はすべて口から動かせる（--tail-ms / --tail-margin-db / --tail-abs-dbfs /
--tail-profile-ms / --tail-step-ms / --tail-final-ms / --tail-rise-db / --tail-floor-dbfs）。

使い方:
    python verify_wavs.py <wav か ディレクトリ> [...] [--out <report.json>]
                          [--silence-db -50] [--clip-threshold 0.999]
                          [--tail-ms 50] [--tail-margin-db 20] [--tail-abs-dbfs -40]
                          [--tail-profile-ms 300] [--tail-step-ms 25] [--tail-final-ms 100]
                          [--tail-rise-db 0.5] [--tail-floor-dbfs -60]
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


# ---- 末尾判定（decisions 39）の既定値 ------------------------------------------------
TAIL_MS = 50.0            # ⒜⒝ が見る末尾の窓
TAIL_MARGIN_DB = 20.0     # ⒜ 全体 RMS との差の閾
TAIL_ABS_DBFS = -40.0     # ⒝ 末尾 RMS の絶対上限
TAIL_PROFILE_MS = 300.0   # ⒞ 形を見る区間
TAIL_STEP_MS = 25.0       # ⒞ 刻み
TAIL_FINAL_MS = 100.0     # ⒞ 「立ち上がっていないこと」を要求する末端
TAIL_RISE_DB = 0.5        # ⒞ 許す立ち上がり（測定の揺れ分）
TAIL_FLOOR_DBFS = -60.0   # ⒞ これ以下は「無音」＝立ち上がりに数えない


def tail_rule_text(
    tail_ms: float = TAIL_MS,
    tail_margin_db: float = TAIL_MARGIN_DB,
    tail_abs_dbfs: float = TAIL_ABS_DBFS,
    tail_profile_ms: float = TAIL_PROFILE_MS,
    tail_step_ms: float = TAIL_STEP_MS,
    tail_final_ms: float = TAIL_FINAL_MS,
    tail_rise_db: float = TAIL_RISE_DB,
    tail_floor_dbfs: float = TAIL_FLOOR_DBFS,
) -> str:
    """台帳に載せる規則の逐語。道具どうしで文言を揃えるためここ 1 か所で作る。"""
    return (
        f"clean ⇔ 3 条件すべて: "
        f"(a) tail_delta_db = tail{tail_ms:g}ms_dbfs - rms_dbfs <= {-abs(tail_margin_db):g} ; "
        f"(b) tail{tail_ms:g}ms_dbfs <= {tail_abs_dbfs:g} dBFS ; "
        f"(c) 末尾 {tail_profile_ms:g} ms を {tail_step_ms:g} ms 刻みに割り、"
        f"最後の {tail_final_ms:g} ms の各区間が直前より +{tail_rise_db:g} dB を超えて上がらない"
        f"（{tail_floor_dbfs:g} dBFS 以下の区間は無音として除く）"
    )


def _rms_db(seg: list[float]) -> float | None:
    if not seg:
        return None
    return _db(math.sqrt(sum(v * v for v in seg) / len(seg)))


def _tail_profile(
    mono: list[float], rate: int, profile_ms: float, step_ms: float
) -> list[float | None]:
    """末尾 profile_ms を step_ms 刻みに割り、各区間の RMS(dBFS) を古い順に返す。

    振幅ちょうど 0 の区間は None（-inf）。檔が窓より短ければ入る分だけ返す。
    """
    if not mono or not rate:
        return []
    step_n = max(1, int(round(rate * step_ms / 1000.0)))
    n_buckets = max(1, int(round(profile_ms / step_ms)))
    total = len(mono)
    prof: list[float | None] = []
    for k in range(n_buckets, 0, -1):
        end = total - (k - 1) * step_n
        if end <= 0:
            continue
        start = max(0, end - step_n)
        if end <= start:
            continue
        prof.append(_rms_db(mono[start:end]))
    return prof


def _tail_rise(
    prof: list[float | None],
    final_ms: float,
    step_ms: float,
    floor_dbfs: float,
) -> tuple[float | None, int]:
    """末尾 final_ms 分の各区間が直前より何 dB 立ち上がったかの最大値を返す。

    floor_dbfs 以下の区間は「無音」＝立ち上がりに数えない（雑音床の揺れで誤検出しないため）。
    直前が無音側なら floor_dbfs を基準にする（無音→発音の立ち上がりを取り逃がさない）。
    返り値＝(最大の立ち上がり dB か None、判定に使った区間の数)。
    None＝末端が全部無音で、比べる相手が無かった。
    """
    if len(prof) < 2:
        return None, 0
    n_final = max(1, int(round(final_ms / step_ms)))
    first = max(1, len(prof) - n_final)  # 索引 0 は「直前」が無いので基準にしか使わない
    worst: float | None = None
    checked = 0
    for i in range(first, len(prof)):
        cur = prof[i]
        if cur is None or cur <= floor_dbfs:
            continue  # 無音＝立ち上がりではない
        prev = prof[i - 1]
        base = floor_dbfs if (prev is None or prev < floor_dbfs) else prev
        rise = cur - base
        checked += 1
        if worst is None or rise > worst:
            worst = rise
    return (round(worst, 2) if worst is not None else None), checked


def analyze(
    path: Path,
    silence_db: float,
    clip_threshold: float,
    tail_ms: float = TAIL_MS,
    tail_margin_db: float = TAIL_MARGIN_DB,
    *,
    tail_abs_dbfs: float = TAIL_ABS_DBFS,
    tail_profile_ms: float = TAIL_PROFILE_MS,
    tail_step_ms: float = TAIL_STEP_MS,
    tail_final_ms: float = TAIL_FINAL_MS,
    tail_rise_db: float = TAIL_RISE_DB,
    tail_floor_dbfs: float = TAIL_FLOOR_DBFS,
) -> dict:
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

    # ---- 末尾判定（decisions 39）＝3 条件すべてを満たしたときだけ clean。
    #  ⒜ 相対＝末尾 tail_ms の RMS が全体 RMS より tail_margin_db 以上低い
    #  ⒝ 絶対＝末尾 tail_ms の RMS が tail_abs_dbfs 以下
    #  ⒞ 立ち上がり無し＝末尾 tail_profile_ms を tail_step_ms 刻みに割り、最後の tail_final_ms の
    #     各区間が直前より tail_rise_db を超えて上がっていない（tail_floor_dbfs 以下は無音扱い）
    # 自然に言い終わった音は末尾が減衰する。⒜ だけでは、いったん無音に落ちてから鳴り出して
    # 切れる檔（月読アイ）が縁を通り抜けた＝⒝⒞ を足して押さえる。
    tail_n = max(1, int(round(rate * tail_ms / 1000.0))) if rate else len(mono)
    tail_n = min(tail_n, len(mono))
    tail_seg = mono[-tail_n:]
    tail_rms = math.sqrt(sum(v * v for v in tail_seg) / len(tail_seg))
    tail_dbfs = _db(tail_rms)

    if tail_dbfs is None or rms <= 0.0:
        # 末尾が完全な無音（振幅ちょうど 0）＝減衰しきっている＝⒜ を満たす扱い。
        # 全体が無音なら比べる意味がないので clean 扱いにして all_silent の印に委ねる。
        tail_delta_db = None
        tail_delta_clean = True
    else:
        tail_delta_db = round(tail_dbfs - _db(rms), 2)  # type: ignore[operator]
        tail_delta_clean = tail_delta_db <= -abs(tail_margin_db)

    # ⒝ 絶対値。末尾が振幅 0（-inf dBFS）なら当然満たす。
    tail_abs_clean = True if tail_dbfs is None else tail_dbfs <= tail_abs_dbfs

    # ⒞ 形。25 ms 刻みの並びを残しておく（司令官が目で追えるように）。
    tail_prof = _tail_profile(mono, rate, tail_profile_ms, tail_step_ms)
    tail_rise_max, tail_rise_checked = _tail_rise(
        tail_prof, tail_final_ms, tail_step_ms, tail_floor_dbfs
    )
    tail_rise_clean = tail_rise_max is None or tail_rise_max <= tail_rise_db

    tail_fail: list[str] = []
    if not tail_delta_clean:
        tail_fail.append("delta")
    if not tail_abs_clean:
        tail_fail.append("abs")
    if not tail_rise_clean:
        tail_fail.append("rise")
    tail_clean = not tail_fail

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
        # ---- 末尾判定（decisions 39）。既存の欄は 1 つも消していない。
        "tail_window_ms": round(tail_n / rate * 1000.0, 2) if rate else None,
        "tail_rms": round(tail_rms, 8),
        "tail_rms_dbfs": tail_dbfs,
        "tail_delta_db": tail_delta_db,
        "tail_margin_db": abs(tail_margin_db),
        "tail_clean": tail_clean,
        "tail_verdict": "clean" if tail_clean else "tail_suspect",
        # ⒜⒝⒞ の内訳（2026-09-05 に足した 2 条件）
        "tail_delta_clean": tail_delta_clean,
        "tail_abs_dbfs": tail_abs_dbfs,
        "tail_abs_clean": tail_abs_clean,
        "tail_profile_ms": tail_profile_ms,
        "tail_step_ms": tail_step_ms,
        "tail_final_ms": tail_final_ms,
        "tail_floor_dbfs": tail_floor_dbfs,
        "tail_rise_limit_db": tail_rise_db,
        "tail_profile_dbfs": tail_prof,
        "tail_rise_db": tail_rise_max,
        "tail_rise_checked": tail_rise_checked,
        "tail_rise_clean": tail_rise_clean,
        "tail_fail_reasons": tail_fail,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="wav の諸元・RMS・ピーク・無音・クリップを JSON にする")
    ap.add_argument("targets", nargs="+", help="wav ファイル、またはそれを含むディレクトリ")
    ap.add_argument("--out", default=None, help="JSON の出力先（省略時は標準出力のみ）")
    ap.add_argument("--silence-db", type=float, default=-50.0, help="無音とみなす dBFS（既定 -50）")
    ap.add_argument("--clip-threshold", type=float, default=0.999, help="クリップとみなす絶対値（既定 0.999）")
    ap.add_argument("--tail-ms", type=float, default=TAIL_MS, help="末尾判定に使う窓のミリ秒（既定 50）")
    ap.add_argument("--tail-margin-db", type=float, default=TAIL_MARGIN_DB,
                    help="⒜ 末尾 RMS が全体 RMS よりこの dB 以上低いこと（既定 20）")
    ap.add_argument("--tail-abs-dbfs", type=float, default=TAIL_ABS_DBFS,
                    help="⒝ 末尾 RMS の絶対上限 dBFS（既定 -40）")
    ap.add_argument("--tail-profile-ms", type=float, default=TAIL_PROFILE_MS,
                    help="⒞ 形を見る末尾の長さ（既定 300 ms）")
    ap.add_argument("--tail-step-ms", type=float, default=TAIL_STEP_MS,
                    help="⒞ 刻み（既定 25 ms）")
    ap.add_argument("--tail-final-ms", type=float, default=TAIL_FINAL_MS,
                    help="⒞ 立ち上がりを許さない末端の長さ（既定 100 ms）")
    ap.add_argument("--tail-rise-db", type=float, default=TAIL_RISE_DB,
                    help="⒞ 区間ごとに許す立ち上がり dB（既定 0.5＝測定の揺れ分）")
    ap.add_argument("--tail-floor-dbfs", type=float, default=TAIL_FLOOR_DBFS,
                    help="⒞ これ以下の区間は無音として立ち上がりに数えない dBFS（既定 -60）")
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
            info = analyze(
                f,
                args.silence_db,
                args.clip_threshold,
                args.tail_ms,
                args.tail_margin_db,
                tail_abs_dbfs=args.tail_abs_dbfs,
                tail_profile_ms=args.tail_profile_ms,
                tail_step_ms=args.tail_step_ms,
                tail_final_ms=args.tail_final_ms,
                tail_rise_db=args.tail_rise_db,
                tail_floor_dbfs=args.tail_floor_dbfs,
            )
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
            # 末尾判定は既存の行の末に足す（既存の欄の並びは変えない）。
            tmark = "clean" if info["tail_clean"] else "TAIL?"
            tdelta = "-inf" if info["tail_delta_db"] is None else f"{info['tail_delta_db']:+.2f}"
            trise = "none" if info["tail_rise_db"] is None else f"{info['tail_rise_db']:+.2f}"
            why = f" why={'+'.join(info['tail_fail_reasons'])}" if info["tail_fail_reasons"] else ""
            print(
                f"[{flag:^6}] {info['file']:<44} {info['duration_s']:>6.2f}s {info['sample_rate']:>6}Hz "
                f"{info['channels']}ch/{info['bits']}bit  peak={info['peak_dbfs']} dBFS  "
                f"rms={info['rms_dbfs']} dBFS  lead={info['lead_silence_s']}s trail={info['trail_silence_s']}s "
                f"run={info['max_clip_run']}  tail50={info['tail_rms_dbfs']} dBFS "
                f"Δ={tdelta} dB [{tmark}] rise={trise} dB{why}",
                flush=True,
            )

    ok_items = [i for i in items if "error" not in i]
    tail_clean = sum(1 for i in ok_items if i.get("tail_clean"))
    tail_suspect = [i["file"] for i in ok_items if not i.get("tail_clean")]
    report = {
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "silence_threshold_dbfs": args.silence_db,
        "clip_threshold": args.clip_threshold,
        "tail_window_ms": args.tail_ms,
        "tail_margin_db": args.tail_margin_db,
        "tail_abs_dbfs": args.tail_abs_dbfs,
        "tail_profile_ms": args.tail_profile_ms,
        "tail_step_ms": args.tail_step_ms,
        "tail_final_ms": args.tail_final_ms,
        "tail_rise_db": args.tail_rise_db,
        "tail_floor_dbfs": args.tail_floor_dbfs,
        "tail_rule": tail_rule_text(
            args.tail_ms,
            args.tail_margin_db,
            args.tail_abs_dbfs,
            args.tail_profile_ms,
            args.tail_step_ms,
            args.tail_final_ms,
            args.tail_rise_db,
            args.tail_floor_dbfs,
        ),
        "count": len(items),
        "errors": sum(1 for i in items if "error" in i),
        "clipped": sum(1 for i in items if i.get("clipped")),
        "tail_clean": tail_clean,
        "tail_suspect": len(tail_suspect),
        "tail_suspect_files": tail_suspect,
        "tail_fail_by_reason": {
            "delta": sum(1 for i in ok_items if "delta" in i.get("tail_fail_reasons", [])),
            "abs": sum(1 for i in ok_items if "abs" in i.get("tail_fail_reasons", [])),
            "rise": sum(1 for i in ok_items if "rise" in i.get("tail_fail_reasons", [])),
        },
        "items": items,
    }
    print(f"[tail] 規則: {report['tail_rule']}", flush=True)
    print(
        f"[tail] clean {tail_clean} / {len(ok_items)} 本"
        + (f"　tail_suspect: {', '.join(tail_suspect)}" if tail_suspect else ""),
        flush=True,
    )
    if args.out:
        op = Path(args.out)
        op.parent.mkdir(parents=True, exist_ok=True)
        op.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"[done] {len(items)} 本。台帳: {op}", flush=True)
    return 1 if report["errors"] else 0


if __name__ == "__main__":
    sys.exit(main())
