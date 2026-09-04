#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""便 P・VOICEROID2 の一次 wav を作る（decisions.md 17・26）。

VOICEROID2 は API を持たない 32bit WPF アプリなので、HTTP で叩ける VOICEVOX / COEIROINK と違い
GUI を駆動するしかない。実体の操作は C# の Vr2SaveTool.exe（tools/preset-voices/Vr2SaveTool/・
net48/x86・Codeer.Friendly）が行い、この檔はその前後（本文の書き出し・仕事表・結果台帳）を持つ。
出力する gen_voiceroid2.result.json は gen_voicevox.py / gen_coeiroink.py と同じ形なので、
make_presets.py がそのまま畳める。

  python gen_voiceroid2.py <out_dir> [--only <id>] [--variant 10s|30s]
                           [--exe <Vr2SaveTool.exe>] [--timeout <ms>] [--launch]

前提：VOICEROID2 エディターが起動していること（--launch を付ければこの道具が起こす）。
"""
from __future__ import annotations

import argparse
import json
import os
import struct
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
CORPUS = HERE / "corpus.json"
DEFAULT_EXE = HERE / "Vr2SaveTool" / "bin" / "Release" / "net48" / "Vr2SaveTool.exe"
VOICEROID2_EXE = Path(r"C:\Program Files (x86)\AHS\VOICEROID2\VoiceroidEditor.exe")

# decisions.md 17 の 12 名のうち VOICEROID2 の 4 名。
# preset は実機の `Vr2SaveTool.exe dump`（2026-09-05）が返した標準プリセット名と逐語一致：
#   琴葉 茜 / 琴葉 葵 / 結月ゆかり / 月読アイ(v1) / 月読ショウタ(v1) / 水奈瀬コウ(v1) / 鷹の爪 吉田くん(v1)
# voice は C:\Program Files (x86)\AHS\VOICEROID2\Voice\ に実在するボイス檔の名。
SPEAKERS = [
    {"id": "vr2_akane_west",      "preset": "琴葉 茜",             "display": "琴葉茜（関西弁）",
     "voice": "akane_west_emo_44", "style": "関西弁"},
    {"id": "vr2_yoshida",         "preset": "鷹の爪 吉田くん(v1)", "display": "吉田くん",
     "voice": "yoshidakun_44",     "style": "ノーマル"},
    {"id": "vr2_tsukuyomi_ai",    "preset": "月読アイ(v1)",        "display": "月読アイ",
     "voice": "ai_44",             "style": "ノーマル"},
    {"id": "vr2_tsukuyomi_shota", "preset": "月読ショウタ(v1)",    "display": "月読ショウタ",
     "voice": "shouta_44",         "style": "ノーマル"},
]

VARIANTS = ["10s", "30s"]


def wav_info(path: Path) -> dict:
    """wav ヘッダから諸元を読む（標準ライブラリのみ・他の gen_*.py と同じ流儀）。"""
    data = path.read_bytes()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"RIFF/WAVE ヘッダではない: {path}")
    pos, fmt, frames_bytes = 12, None, None
    while pos + 8 <= len(data):
        cid = data[pos:pos + 4]
        size = struct.unpack_from("<I", data, pos + 4)[0]
        body = pos + 8
        if cid == b"fmt ":
            fmt = struct.unpack_from("<HHIIHH", data, body)
        elif cid == b"data":
            frames_bytes = size
        pos = body + size + (size & 1)
    if fmt is None or frames_bytes is None:
        raise ValueError(f"fmt / data チャンクが見つからない: {path}")
    _tag, channels, rate, _bps, _align, bits = fmt
    frames = frames_bytes // (channels * (bits // 8))
    return {
        "sample_rate": rate, "channels": channels, "bits": bits,
        "frames": frames, "duration_s": round(frames / rate, 3), "bytes": len(data),
    }


def ensure_voiceroid_running(launch: bool) -> None:
    out = subprocess.run(
        ["tasklist", "/FI", "IMAGENAME eq VoiceroidEditor.exe"],
        capture_output=True, text=True, errors="ignore").stdout
    if "VoiceroidEditor.exe" in out:
        print("[ok] VOICEROID2 は起動済み")
        return
    if not launch:
        raise SystemExit("VOICEROID2 が起動していません。先に起こすか --launch を付けてください。")
    if not VOICEROID2_EXE.is_file():
        raise SystemExit(f"VOICEROID2 が見つかりません: {VOICEROID2_EXE}")
    print(f"[launch] {VOICEROID2_EXE}")
    subprocess.Popen([str(VOICEROID2_EXE)])
    for _ in range(60):
        time.sleep(1)
        out = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq VoiceroidEditor.exe"],
            capture_output=True, text=True, errors="ignore").stdout
        if "VoiceroidEditor.exe" in out:
            time.sleep(8)  # UI が組み上がるまで
            print("[ok] VOICEROID2 が起動した")
            return
    raise SystemExit("VOICEROID2 の起動を確認できませんでした。")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir", help="作業ディレクトリ（build/out/preset-work）")
    ap.add_argument("--only", default=None, help="この id だけ")
    ap.add_argument("--variant", default=None, choices=VARIANTS, help="この版だけ")
    ap.add_argument("--exe", default=str(DEFAULT_EXE), help="Vr2SaveTool.exe のパス")
    ap.add_argument("--timeout", type=int, default=120000, help="1 本あたりの期限（ms）")
    ap.add_argument("--launch", action="store_true", help="未起動なら VOICEROID2 を起こす")
    args = ap.parse_args()

    exe = Path(args.exe)
    if not exe.is_file():
        raise SystemExit(
            f"Vr2SaveTool.exe が無い: {exe}\n"
            f"  dotnet build \"{HERE / 'Vr2SaveTool' / 'Vr2SaveTool.csproj'}\" -c Release で建ててください。")

    corpus = json.loads(CORPUS.read_text(encoding="utf-8"))
    primary = corpus["primary"]

    out_dir = Path(args.out_dir).resolve()
    jobs_dir = out_dir / "jobs"
    wav_dir = out_dir / "primary"
    log_dir = out_dir / "logs"
    for d in (jobs_dir, wav_dir, log_dir):
        d.mkdir(parents=True, exist_ok=True)

    # ---- 本文を檔に出す（C# 側は UTF-8 の檔から読む＝コンソールの文字コードに依存しない）
    text_files: dict[str, Path] = {}
    texts: dict[str, str] = {}
    for v in VARIANTS:
        text = primary[f"text_{v}"]
        p = jobs_dir / f"text_{v}.txt"
        p.write_text(text, encoding="utf-8", newline="")
        text_files[v] = p
        texts[v] = text
        print(f"[text] {p}  {len(text)} 字")

    # ---- 仕事表
    rows = []
    for sp in SPEAKERS:
        if args.only and sp["id"] != args.only:
            continue
        for v in VARIANTS:
            if args.variant and v != args.variant:
                continue
            rows.append((sp, v, wav_dir / f"{sp['id']}_{v}.wav"))
    if not rows:
        raise SystemExit(f"--only / --variant に一致する組が無い。候補: {[s['id'] for s in SPEAKERS]}")

    jobs_path = jobs_dir / "jobs.tsv"
    with jobs_path.open("w", encoding="utf-8", newline="\n") as f:
        f.write("# 便 P VOICEROID2 一次 wav の仕事表（gen_voiceroid2.py が生成）\n")
        f.write("# プリセット名<TAB>本文檔<TAB>出力 wav\n")
        for sp, v, wav in rows:
            f.write(f"{sp['preset']}\t{text_files[v]}\t{wav}\n")
    print(f"[jobs] {jobs_path}  {len(rows)} 本")

    ensure_voiceroid_running(args.launch)

    # ---- GUI 駆動（実体は C#）
    log_path = log_dir / "vr2_batch.log"
    cmd = [str(exe), "batch", str(jobs_path), "--log", str(log_path), "--timeout", str(args.timeout)]
    print("[run] " + " ".join(cmd), flush=True)
    t0 = time.time()
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    elapsed = time.time() - t0
    for line in (proc.stdout or "").splitlines():
        if any(k in line for k in ("====", "保存完了", "失敗", "batch 完了", "ERROR", "警告", "突き合わせ")):
            print("  " + line)
    print(f"[run] 終了コード {proc.returncode}（{elapsed:.1f} s・ログ {log_path}）")

    # ---- 結果台帳（gen_voicevox.py / gen_coeiroink.py と同じ形）
    items = []
    missing = []
    for sp, v, wav in rows:
        if not wav.is_file():
            missing.append(str(wav))
            continue
        info = wav_info(wav)
        items.append({
            "id": sp["id"],
            "display_name": sp["display"],
            "engine": "voiceroid2",
            "engine_speaker": sp["preset"],
            "style": {"name": sp["style"], "id": sp["voice"]},
            "variant": v,
            "file": wav.name,
            "text": texts[v],
            "synth_elapsed_s": None,   # GUI 駆動なので 1 本あたりの合成時間は測っていない
            **info,
        })
        print(f"[gen] {wav.name:34s} {info['duration_s']:7.2f} s {info['sample_rate']} Hz "
              f"{info['channels']}ch/{info['bits']}bit")

    report = {
        "engine": "voiceroid2",
        "engine_version": None,           # VOICEROID2 は版を返す口を持たない
        "endpoint": "GUI (Codeer.Friendly)",
        "tool": str(exe),
        "outdir": str(wav_dir),
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "items": items,
    }
    rep_path = wav_dir / "gen_voiceroid2.result.json"
    rep_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[done] {len(items)} 本。台帳: {rep_path}")
    if missing:
        for m in missing:
            print(f"[NG] 出来ていない: {m}")
        return 1
    return 0 if proc.returncode == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
