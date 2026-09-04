#!/usr/bin/env python3
"""便 P — 私設 Irodori-TTS-Server（8090）を立てて二次ボイスを生成する。

decisions.md 17・26 が正典。停止域＝C:/irodori-TTS-server/ と C:/IrodoriTTS/ は書き換えない
（.venv-rocm の python.exe を「実行する」だけ）。8088 は起こさない＝自分で 8090 を立てる。
voices_dir と cwd は必ず自分の作業ディレクトリに向ける。

流れ:
  1. 一次 30 s（既定）/10 s wav を voices_dir へ ASCII 檔名で複写＝これが参照ボイス。
  2. .venv-rocm の python で `-m irodori_openai_tts --host 127.0.0.1 --port 8090` を子で起動。
  3. /health の runtime.loaded を待つ → no_ref 短文で暖機 1 発。
  4. POST /v1/audio/speech で二次を生成（既定は 30 s 参照。--also-ref10 で 10 s 版も）。
  5. 子プロセスをツリー kill。

使い方:
    python run_secondary.py [--work <preset-work>] [--port 8090] [--also-ref10]
                            [--text expressive_30s|greeting_10s] [--only <id>]
                            [--ready-timeout 600] [--keep-server]
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
VENV_PY = Path("C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe")
# 上流を読むだけの保険。編集可能インストール（-e）が効いていれば不要だが、cwd を変えても
# import できなかった場合にだけ PYTHONPATH に足す。
UPSTREAM_SRC = [
    Path("C:/irodori-TTS-server/Irodori-TTS-Server/src"),
    Path("C:/IrodoriTTS/Irodori-TTS"),
]

# decisions 17 の 12 名のうち、一次 wav が揃った 11 名。
# 先の 7 名＝HTTP エンジン（VOICEVOX・COEIROINK）。
# 後の 4 名＝VOICEROID2。API が無いので Vr2SaveTool（tools/preset-voices/Vr2SaveTool/）で
#            GUI の［音声保存］から取り出した（2026-09-05・G3 席・44100 Hz 1ch/16bit）。
# CeVIO AI の弦巻マキ（英）は decisions 27 で今回は飛ばす。
PRIMARY_IDS = [
    "vv_mochiko_sexy",
    "vv_chibishikijii",
    "co_tsukuyomi",
    "co_kana_naisho",
    "co_mana_isshoukenmei",
    "co_ofutonp_kiza",
    "co_ofutonp_normal_v2",
    "vr2_akane_west",
    "vr2_yoshida",
    "vr2_tsukuyomi_ai",
    "vr2_tsukuyomi_shota",
]


# ---------------------------------------------------------------- HTTP helpers


def _get_json(url: str, timeout: float = 10.0) -> dict:
    with urllib.request.urlopen(url, timeout=timeout) as r:
        return json.loads(r.read())


def _post(url: str, payload: dict, timeout: float = 900.0) -> tuple[bytes, str]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        url, data=body, headers={"Content-Type": "application/json", "Accept": "*/*"}, method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.read(), r.headers.get("Content-Type", "")
    except urllib.error.HTTPError as exc:
        detail = exc.read()[:4000].decode("utf-8", "replace")
        raise RuntimeError(f"HTTP {exc.code} {url}\n{detail}") from exc


# ---------------------------------------------------------------- server


def build_env(voices_dir: Path) -> dict[str, str]:
    env = dict(os.environ)
    env.update(
        {
            "IRODORI_VOICES_DIR": str(voices_dir),          # 必ず絶対パス
            "IRODORI_PRELOAD": "true",
            "IRODORI_EMPTY_CACHE_INTERVAL": "0",
            "IRODORI_MODEL_PRECISION": "bf16",
            "IRODORI_CODEC_PRECISION": "bf16",
            "IRODORI_HF_CHECKPOINT": "Aratako/Irodori-TTS-v4.1-Small",
            "HF_HUB_OFFLINE": "1",
            "PYTHONDONTWRITEBYTECODE": "1",
            "PYTHONUTF8": "1",
        }
    )
    return env


def import_ok(env: dict[str, str], cwd: Path) -> bool:
    """cwd を変えても irodori_openai_tts が import できるかを実射で確かめる。"""
    p = subprocess.run(
        [str(VENV_PY), "-c", "import irodori_openai_tts, irodori_tts; print(irodori_openai_tts.__file__)"],
        env=env,
        cwd=str(cwd),
        capture_output=True,
        text=True,
        timeout=180,
    )
    if p.returncode == 0:
        print(f"[import] OK  {p.stdout.strip()}", flush=True)
        return True
    print(f"[import] NG  rc={p.returncode}\n{p.stderr[-1500:]}", flush=True)
    return False


def start_server(env: dict[str, str], cwd: Path, port: int, log_path: Path) -> tuple[subprocess.Popen, object]:
    cwd.mkdir(parents=True, exist_ok=True)
    log_path.parent.mkdir(parents=True, exist_ok=True)
    log = log_path.open("wb")
    cmd = [str(VENV_PY), "-m", "irodori_openai_tts", "--host", "127.0.0.1", "--port", str(port)]
    print(f"[spawn] {' '.join(cmd)}\n        cwd={cwd}\n        log={log_path}", flush=True)
    creationflags = 0
    if os.name == "nt":
        creationflags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    proc = subprocess.Popen(cmd, env=env, cwd=str(cwd), stdout=log, stderr=subprocess.STDOUT, creationflags=creationflags)
    return proc, log


def kill_tree(proc: subprocess.Popen) -> None:
    """子プロセスをツリーごと殺す（uvicorn の孫が残らないように）。"""
    if proc.poll() is not None:
        return
    if os.name == "nt":
        # text=True にしない。taskkill の出力は日本語 Windows では CP932 で、
        # PYTHONUTF8=1 の下では UTF-8 として復号できず読み取りスレッドが例外を吐く。
        subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], capture_output=True)
    else:
        proc.terminate()
    try:
        proc.wait(timeout=30)
    except subprocess.TimeoutExpired:
        proc.kill()


def wait_ready(port: int, proc: subprocess.Popen, timeout_s: float, log_path: Path) -> dict:
    """/health の runtime.loaded が true になるまで待つ（ROCm は読込 20〜30 s）。"""
    url = f"http://127.0.0.1:{port}/health"
    deadline = time.time() + timeout_s
    t0 = time.time()
    last = "(まだ届かない)"
    while time.time() < deadline:
        if proc.poll() is not None:
            tail = log_path.read_text("utf-8", "replace")[-3000:] if log_path.exists() else "(ログなし)"
            raise SystemExit(f"サーバが起動中に落ちた rc={proc.returncode}\n--- ログ末尾 ---\n{tail}")
        try:
            h = _get_json(url, timeout=5.0)
            rt = h.get("runtime", {})
            if rt.get("loaded"):
                print(f"[ready] runtime.loaded=true ({time.time() - t0:.1f} s)", flush=True)
                return h
            last = f'loaded={rt.get("loaded")} loading={rt.get("loading")}'
        except Exception as exc:  # noqa: BLE001 — 起動待ちなので全部握る
            last = repr(exc)[:120]
        time.sleep(2.0)
    tail = log_path.read_text("utf-8", "replace")[-3000:] if log_path.exists() else "(ログなし)"
    raise SystemExit(f"{timeout_s:.0f} 秒たっても ready にならない。最後: {last}\n--- ログ末尾 ---\n{tail}")


# ---------------------------------------------------------------- main


def main() -> int:
    ap = argparse.ArgumentParser(description="私設 8090 サーバで便 P の二次ボイスを生成する")
    ap.add_argument("--work", default=str(REPO / "build" / "out" / "preset-work"))
    ap.add_argument("--port", type=int, default=8090)
    ap.add_argument("--corpus", default=str(Path(__file__).with_name("corpus.json")))
    ap.add_argument("--text", default="expressive_30s", choices=["expressive_30s", "greeting_10s"])
    ap.add_argument("--also-ref10", action="store_true", help="10 s 参照でも生成して比較用に残す（_ref10）")
    ap.add_argument("--only", default=None, help="この id だけ生成する")
    ap.add_argument("--ready-timeout", type=float, default=600.0)
    ap.add_argument("--keep-server", action="store_true", help="終了時にサーバを落とさない（調査用）")
    args = ap.parse_args()

    work = Path(args.work).resolve()
    primary_dir = work / "primary"
    voices_dir = work / "voices"
    out_dir = work / "secondary"
    server_cwd = work / "server"
    logs = work / "logs"
    for d in (voices_dir, out_dir, server_cwd, logs):
        d.mkdir(parents=True, exist_ok=True)

    if not VENV_PY.is_file():
        raise SystemExit(f"venv の python が見つからない: {VENV_PY}")

    corpus = json.loads(Path(args.corpus).read_text(encoding="utf-8"))
    sec = corpus["secondary"][args.text]
    text = sec["text"]
    par = corpus["irodori_params"]

    ids = [i for i in PRIMARY_IDS if args.only in (None, i)]
    if not ids:
        raise SystemExit(f"--only {args.only!r} に一致する id がない。候補: {PRIMARY_IDS}")

    # ---- 1. 参照ボイスを voices_dir に ASCII 檔名で置く
    refs: dict[str, list[str]] = {}
    variants = ["30s"] + (["10s"] if args.also_ref10 else [])
    for vid in ids:
        placed = []
        for var in variants:
            src = primary_dir / f"{vid}_{var}.wav"
            if not src.is_file():
                print(f"[skip] 一次 wav がない: {src}", flush=True)
                continue
            # 30 s 版は素の id 名（既定の参照）、10 s 版は <id>_ref10
            voice_id = vid if var == "30s" else f"{vid}_ref10"
            dst = voices_dir / f"{voice_id}.wav"
            shutil.copy2(src, dst)
            placed.append(voice_id)
        if placed:
            refs[vid] = placed
            print(f"[ref]  {vid}: {', '.join(placed)}", flush=True)
    if not refs:
        raise SystemExit("参照ボイスを 1 本も置けなかった。先に gen_voicevox.py / gen_coeiroink.py を回すこと。")

    # ---- 2. サーバを起動
    env = build_env(voices_dir)
    if not import_ok(env, server_cwd):
        extra = os.pathsep.join(str(p) for p in UPSTREAM_SRC if p.is_dir())
        env["PYTHONPATH"] = extra + (os.pathsep + env["PYTHONPATH"] if env.get("PYTHONPATH") else "")
        print(f"[import] PYTHONPATH を足して再試行: {extra}", flush=True)
        if not import_ok(env, server_cwd):
            raise SystemExit("PYTHONPATH を足しても import できない。稼働機の venv を確認せよ。")

    log_path = logs / f"server-8090-{time.strftime('%Y%m%d-%H%M%S')}.log"
    proc, log = start_server(env, server_cwd, args.port, log_path)

    report: dict = {
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "port": args.port,
        "work": str(work),
        "voices_dir": str(voices_dir),
        "server_log": str(log_path),
        "text_id": sec["id"],
        "text": text,
        "irodori_params": par,
        "items": [],
    }
    rc = 0
    try:
        health = wait_ready(args.port, proc, args.ready_timeout, log_path)
        report["health"] = health
        print(
            f"[health] model={health.get('model', {}).get('hf_checkpoint')} "
            f"precision={health.get('model', {}).get('model_precision')} "
            f"voices.files={health.get('voices', {}).get('files')}",
            flush=True,
        )

        base = f"http://127.0.0.1:{args.port}"

        # ---- 3. 暖機 1 発（no_ref・短文）
        t0 = time.perf_counter()
        try:
            warm, _ = _post(
                f"{base}/v1/audio/speech",
                {
                    "model": par["model"],
                    "input": "暖機です。",
                    "voice": "no_ref",
                    "response_format": "wav",
                    "irodori": {"num_steps": par["num_steps"], "seed": par["seed"]},
                },
                timeout=900.0,
            )
            print(f"[warm] no_ref 暖機 OK  {len(warm)} bytes  {time.perf_counter() - t0:.1f} s", flush=True)
            report["warmup"] = {"ok": True, "bytes": len(warm), "elapsed_s": round(time.perf_counter() - t0, 2)}
        except Exception as exc:  # noqa: BLE001 — 暖機の失敗では止めない
            print(f"[warm] 暖機に失敗（続行する）: {exc}", flush=True)
            report["warmup"] = {"ok": False, "error": str(exc)[:1000]}

        # ---- 4. 二次生成
        for vid, voice_ids in refs.items():
            for voice_id in voice_ids:
                name = f"{voice_id}_secondary.wav"
                dest = out_dir / name
                payload = {
                    "model": par["model"],
                    "input": text,
                    "voice": voice_id,
                    "response_format": par["response_format"],
                    "irodori": {"num_steps": par["num_steps"], "seed": par["seed"]},
                }
                t0 = time.perf_counter()
                try:
                    wav, ctype = _post(f"{base}/v1/audio/speech", payload, timeout=1800.0)
                except Exception as exc:  # noqa: BLE001 — 1 名の失敗で全体を落とさない
                    print(f"[NG]   {voice_id}: {exc}", flush=True)
                    report["items"].append({"id": vid, "voice": voice_id, "error": str(exc)[:2000]})
                    rc = 1
                    continue
                elapsed = time.perf_counter() - t0
                dest.write_bytes(wav)
                print(
                    f"[gen]  {name}  {len(wav)} bytes  {elapsed:.1f} s  ({ctype})",
                    flush=True,
                )
                report["items"].append(
                    {
                        "id": vid,
                        "voice": voice_id,
                        "ref_variant": "30s" if voice_id == vid else "10s",
                        "file": name,
                        "path": str(dest),
                        "bytes": len(wav),
                        "content_type": ctype,
                        "elapsed_s": round(elapsed, 2),
                        "request": payload,
                    }
                )
    finally:
        if args.keep_server:
            print(f"[keep] サーバを残す pid={proc.pid} port={args.port}（自分で止めること）", flush=True)
        else:
            kill_tree(proc)
            print(f"[kill] サーバを落とした pid={proc.pid}", flush=True)
        try:
            log.close()
        except Exception:  # noqa: BLE001
            pass
        rp = logs / "run_secondary.result.json"
        rp.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"[done] 台帳: {rp}", flush=True)

    return rc


if __name__ == "__main__":
    sys.exit(main())
