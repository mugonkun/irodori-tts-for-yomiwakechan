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

seed 掃引モード（--sweep-seed・decisions 39）:
  末尾が切れている（verify_wavs.py の tail_verdict が tail_suspect）採用版について、
  本文も参照も変えずに seed だけを 1234 から順に最大 8 個試し、生成のたびに末尾判定を掛けて
  clean になった最初の seed を採る。8 個で出なければ irodori.trim_tail=false で同じ seed 列を
  もう一巡する（上流 SamplingRequest.trim_tail＝潜在の平坦点で音を切る仕組み。false にすると
  推定長 target_samples まで残す）。採用 seed と試行回数は台帳に残す。
  末尾判定は verify_wavs.py の 3 条件（⒜ 相対 Δ≦−20 dB・⒝ 絶対 ≦−40 dBFS・
  ⒞ 末尾 300 ms を 25 ms 刻みで見て最後の 100 ms に立ち上がりが無い）をそのまま使う。
  起点を変えたいときは --sweep-seed-start（例 1235＝直前の採用 seed の次から）。

参照ボイスへの寄せ具合（--cfg-scale-speaker・裁定 111）:
  上流 SamplingRequest.cfg_scale_speaker＝参照ボイス（話者）の誘導の強さ。渡さなければ
  上流既定 5.0（`upstream/Irodori-TTS-Server/src/irodori_openai_tts/config.py:54`
  default_cfg_scale_speaker）が効く。値を渡すと全射の irodori 欄に載せる（暖機の no_ref は除く）。
  司令官 2026-09-09「２次生成ボイスが途中から参照ボイスになってないね。CFG Scale Speaker を
  2 ほど上げて再生成を頼む」＝3 本（KANA ないしょばなし・おふとんP きざ・琴葉茜 関西弁）を
  5.0＋2＝7.0 で撃ち直した。
  値の残り先は 4 つ＝⑴ 採用した射の items[].request.irodori、⑵ 掃引の行と attempts の
  cfg_scale_speaker（None＝渡していない＝上流既定 5.0）、⑶ その run の logs/run_secondary.sweep.json
  の irodori_params、⑷ 畳み込み先 run_secondary.result.json の seed_sweep.irodori_params。
  畳み込み先の **top-level** irodori_params は最初に 22 本を撃った run のままで差し替えない
  （撃っていない行の既定にまで新しい値が被らないように＝merge_result の註）。

本文の長さ（--text・裁定 112）:
  corpus.json の secondary の本文を名で選ぶ。expressive_30s（146 字・既定）・
  expressive_20s（90 字）・greeting_10s（44 字）。
  司令官 2026-09-09「3ファイルともおなじところで参照ボイスが外れるね。アプローチを変えよう。
  文章を20秒程度に見積もって再生成。」＝cfg_scale_speaker 7.0 で撃った 3 本が 3 本とも同じ位置で
  参照ボイスから離れた＝長さ／位置に依る、という耳の所見。手当てが expressive_20s。
  **撃った本文は射ごとに items[].text_id／items[].text に刻む**（merge_result はこれを持ったまま畳む）。
  畳み込み先の top-level の text_id／text は最初に 22 本を撃った run のままで差し替えない
  ＝撃っていない行にまで新しい本文が被らないように（generated_at／irodori_params と同じ扱い）。
  make_presets.py は行の text_id／text を先に見て、無い行だけ top-level を使う。

使い方:
    python run_secondary.py [--work <preset-work>] [--port 8090] [--also-ref10]
                            [--text expressive_30s|expressive_20s|greeting_10s] [--only <id>]
                            [--ready-timeout 600] [--keep-server]
                            [--cfg-scale-speaker 7.0]

    python run_secondary.py --sweep-seed [--sweep-ids id1,id2,...]
                            [--sweep-max-seeds 8] [--sweep-seed-start 1234]
                            [--no-trim-fallback] [--cfg-scale-speaker 7.0]
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

sys.path.insert(0, str(Path(__file__).resolve().parent))
import verify_wavs as _vw  # noqa: E402 — 同じディレクトリの道具
from verify_wavs import analyze as _analyze_wav  # noqa: E402
from verify_wavs import tail_rule_text as _tail_rule_text  # noqa: E402

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


# ---------------------------------------------------------------- 末尾判定（decisions 39）


def tail_params(args) -> dict:
    """末尾判定の窓と閾を 1 つの辞書に畳む（verify_wavs.analyze のキーワードと同名）。"""
    return {
        "tail_ms": args.tail_ms,
        "tail_margin_db": args.tail_margin_db,
        "tail_abs_dbfs": args.tail_abs_dbfs,
        "tail_profile_ms": args.tail_profile_ms,
        "tail_step_ms": args.tail_step_ms,
        "tail_final_ms": args.tail_final_ms,
        "tail_rise_db": args.tail_rise_db,
        "tail_floor_dbfs": args.tail_floor_dbfs,
    }


def tail_of(path: Path, tp: dict) -> dict:
    """verify_wavs.analyze を掛けて末尾判定の欄だけ抜く（3 条件・decisions 39）。"""
    kw = dict(tp)
    tail_ms = kw.pop("tail_ms")
    tail_margin_db = kw.pop("tail_margin_db")
    a = _analyze_wav(path, -50.0, 0.999, tail_ms, tail_margin_db, **kw)
    return {
        "duration_s": a["duration_s"],
        "rms_dbfs": a["rms_dbfs"],
        "tail_rms_dbfs": a["tail_rms_dbfs"],
        "tail_delta_db": a["tail_delta_db"],
        "tail_clean": a["tail_clean"],
        "tail_verdict": a["tail_verdict"],
        # 足した 2 条件（⒝ 絶対値・⒞ 立ち上がり無し）の内訳も残す
        "tail_delta_clean": a["tail_delta_clean"],
        "tail_abs_clean": a["tail_abs_clean"],
        "tail_rise_db": a["tail_rise_db"],
        "tail_rise_clean": a["tail_rise_clean"],
        "tail_profile_dbfs": a["tail_profile_dbfs"],
        "tail_fail_reasons": a["tail_fail_reasons"],
    }


def tail_suspect_ids(out_dir: Path, ids: list[str], tp: dict) -> list[str]:
    """採用版（<id>_secondary.wav）のうち末尾が tail_suspect の id を拾う。"""
    picked: list[str] = []
    for vid in ids:
        f = out_dir / f"{vid}_secondary.wav"
        if not f.is_file():
            continue
        t = tail_of(f, tp)
        if not t["tail_clean"]:
            picked.append(vid)
            print(
                f"[tail] tail_suspect: {vid}  Δ={t['tail_delta_db']} dB "
                f"tail50={t['tail_rms_dbfs']} dBFS rise={t['tail_rise_db']} dB "
                f"why={'+'.join(t['tail_fail_reasons'])}",
                flush=True,
            )
    return picked


def merge_result(logs: Path, sweep_report: dict) -> Path:
    """掃引の結果を既存の run_secondary.result.json に畳み込む。

    掃引は数本しか作らないので、そのまま上書きすると 11 名 22 本の台帳が消える。
    voice が一致する項目だけ差し替え、掃引の記録を seed_sweep として足す。
    seed_sweep.results も voice ごとに畳む（前回の掃引で採った話者の記録を消さない）。
    各行は自分の run の seeds と規則を持つので、run をまたいでも読める。

    畳み込み先の top-level（generated_at・server_log・irodori_params・text_id・text）は**触らない**
    ＝あれは「最初に 22 本を撃った run」の素性で、掃引で撃ち直した数本のものではない。
    そのままだと撃ち直した行が古い日付・古い本文を名乗るので（裁定 111 の付帯・裁定 112）、
    畳み込む item に自分の run の generated_at・server_log・text_id・text を持たせる
    （make_presets.py が行ごとの generated_at・text_id・text にこれを使う）。
    掃引 run の irodori_params は seed_sweep の下に置く＝top-level には混ぜない
    （混ぜると撃っていない行の既定にまで新しい値が被る）。
    """
    rp = logs / "run_secondary.result.json"
    base: dict = {}
    if rp.is_file():
        base = json.loads(rp.read_text(encoding="utf-8"))
    if not base:
        base = dict(sweep_report)
        base["items"] = []
    adopted = {i["voice"]: i for i in sweep_report.get("items", []) if "error" not in i}
    for it in adopted.values():
        # この射を撃ったのは「今の run」＝行に自分の日付と server ログを持たせる。
        it["generated_at"] = sweep_report.get("generated_at")
        it["server_log"] = sweep_report.get("server_log")
        # 本文も同じ扱い（裁定 112）。掃引の item は自分で刻んでいるが、古い形の
        # 報告を畳むときのために、無ければこの run の本文で埋める。
        it.setdefault("text_id", sweep_report.get("text_id"))
        it.setdefault("text", sweep_report.get("text"))
    items = []
    for it in base.get("items", []):
        items.append(adopted.pop(it.get("voice"), it))
    items.extend(adopted.values())
    base["items"] = items

    # 掃引の行も voice ごとに畳む（今回撃たなかった話者の前回の記録を残す）。
    new_rows = {r["voice"]: r for r in sweep_report.get("sweep", [])}
    rows: list[dict] = []
    for r in (base.get("seed_sweep") or {}).get("results", []):
        rows.append(new_rows.pop(r.get("voice"), r))
    rows.extend(new_rows.values())

    base["seed_sweep"] = {
        "generated_at": sweep_report["generated_at"],
        "server_log": sweep_report.get("server_log"),
        # この掃引 run に渡した既定（cfg_scale_speaker を渡した run ならここに載る）。
        # top-level の irodori_params は最初の run のものなので差し替えない。
        "irodori_params": sweep_report.get("irodori_params"),
        "seeds": sweep_report["seeds"],
        "tail_window_ms": sweep_report["tail_window_ms"],
        "tail_margin_db": sweep_report["tail_margin_db"],
        "tail_abs_dbfs": sweep_report["tail_abs_dbfs"],
        "tail_profile_ms": sweep_report["tail_profile_ms"],
        "tail_step_ms": sweep_report["tail_step_ms"],
        "tail_final_ms": sweep_report["tail_final_ms"],
        "tail_rise_db": sweep_report["tail_rise_db"],
        "tail_floor_dbfs": sweep_report["tail_floor_dbfs"],
        "tail_rule": sweep_report["tail_rule"],
        "trim_tail_fallback": sweep_report["trim_tail_fallback"],
        "results": rows,
    }
    rp.write_text(json.dumps(base, ensure_ascii=False, indent=2), encoding="utf-8")
    return rp


# ---------------------------------------------------------------- main


def main() -> int:
    ap = argparse.ArgumentParser(description="私設 8090 サーバで便 P の二次ボイスを生成する")
    ap.add_argument("--work", default=str(REPO / "build" / "out" / "preset-work"))
    ap.add_argument("--port", type=int, default=8090)
    ap.add_argument("--corpus", default=str(Path(__file__).with_name("corpus.json")))
    ap.add_argument("--text", default="expressive_30s",
                    choices=["expressive_30s", "expressive_20s", "greeting_10s"],
                    help="corpus.json の secondary の本文を名で選ぶ（裁定 112 で expressive_20s＝90 字を足した）")
    ap.add_argument("--also-ref10", action="store_true", help="10 s 参照でも生成して比較用に残す（_ref10）")
    ap.add_argument("--only", default=None, help="この id だけ生成する")
    ap.add_argument("--ready-timeout", type=float, default=600.0)
    ap.add_argument("--keep-server", action="store_true", help="終了時にサーバを落とさない（調査用）")
    ap.add_argument("--cfg-scale-speaker", type=float, default=None,
                    help="参照ボイス（話者）の誘導の強さ。既定＝渡さない＝上流既定 5.0。"
                         "渡すと全射の irodori 欄に載る（裁定 111 は 7.0）")
    # ---- seed 掃引（decisions 39）
    ap.add_argument("--sweep-seed", action="store_true",
                    help="末尾が切れている採用版について seed を掃引し、clean になった最初の seed を採る")
    ap.add_argument("--sweep-ids", default=None,
                    help="掃引する id をカンマ区切りで指定（既定＝採用版が tail_suspect の id を自動で拾う）")
    ap.add_argument("--sweep-max-seeds", type=int, default=8, help="試す seed の個数（既定 8）")
    ap.add_argument("--sweep-seed-start", type=int, default=1234, help="掃引の起点 seed（既定 1234）")
    ap.add_argument("--no-trim-fallback", action="store_true",
                    help="seed を使い切っても clean が出ない時に irodori.trim_tail=false を試す経路を切る")
    ap.add_argument("--tail-ms", type=float, default=_vw.TAIL_MS, help="末尾判定の窓（ミリ秒・既定 50）")
    ap.add_argument("--tail-margin-db", type=float, default=_vw.TAIL_MARGIN_DB,
                    help="⒜ 末尾 RMS が全体 RMS よりこの dB 以上低いこと（既定 20）")
    ap.add_argument("--tail-abs-dbfs", type=float, default=_vw.TAIL_ABS_DBFS,
                    help="⒝ 末尾 RMS の絶対上限 dBFS（既定 -40）")
    ap.add_argument("--tail-profile-ms", type=float, default=_vw.TAIL_PROFILE_MS,
                    help="⒞ 形を見る末尾の長さ（既定 300 ms）")
    ap.add_argument("--tail-step-ms", type=float, default=_vw.TAIL_STEP_MS,
                    help="⒞ 刻み（既定 25 ms）")
    ap.add_argument("--tail-final-ms", type=float, default=_vw.TAIL_FINAL_MS,
                    help="⒞ 立ち上がりを許さない末端の長さ（既定 100 ms）")
    ap.add_argument("--tail-rise-db", type=float, default=_vw.TAIL_RISE_DB,
                    help="⒞ 区間ごとに許す立ち上がり dB（既定 0.5）")
    ap.add_argument("--tail-floor-dbfs", type=float, default=_vw.TAIL_FLOOR_DBFS,
                    help="⒞ これ以下は無音として立ち上がりに数えない dBFS（既定 -60）")
    args = ap.parse_args()
    tp = tail_params(args)

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
    # corpus の既定を壊さないよう写しを持つ。--cfg-scale-speaker を渡した run では
    # その値を台帳の irodori_params にも残す（裁定 111）。渡さない run は欄ごと現れない
    #＝「上流既定 5.0 で撃った」の意（値を捏造しない）。
    par = dict(corpus["irodori_params"])
    if args.cfg_scale_speaker is not None:
        par["cfg_scale_speaker"] = args.cfg_scale_speaker

    def irodori_body(seed: int) -> dict:
        """1 射の irodori 欄を組む（暖機の no_ref では使わない）。"""
        body = {"num_steps": par["num_steps"], "seed": seed}
        if args.cfg_scale_speaker is not None:
            body["cfg_scale_speaker"] = args.cfg_scale_speaker
        return body

    ids = [i for i in PRIMARY_IDS if args.only in (None, i)]
    if not ids:
        raise SystemExit(f"--only {args.only!r} に一致する id がない。候補: {PRIMARY_IDS}")

    seeds: list[int] = []
    if args.sweep_seed:
        seeds = [args.sweep_seed_start + k for k in range(max(1, args.sweep_max_seeds))]
        if args.sweep_ids:
            want = [s.strip() for s in args.sweep_ids.split(",") if s.strip()]
            unknown = [w for w in want if w not in PRIMARY_IDS]
            if unknown:
                raise SystemExit(f"--sweep-ids に知らない id がある: {unknown}")
            ids = want
        else:
            ids = tail_suspect_ids(out_dir, ids, tp)
            if not ids:
                print("[sweep] 末尾 tail_suspect の採用版が無い。何もしない。", flush=True)
                return 0
        print(f"[sweep] 対象 {len(ids)} 本: {', '.join(ids)}", flush=True)
        print(f"[sweep] seed: {seeds}", flush=True)
        print(
            "[sweep] cfg_scale_speaker: "
            + (f"{args.cfg_scale_speaker}（明示）" if args.cfg_scale_speaker is not None
               else "渡さない（上流既定 5.0）"),
            flush=True,
        )
        print(f"[sweep] text: {sec['id']}（{len(text)} 字）", flush=True)

    # ---- 1. 参照ボイスを voices_dir に ASCII 檔名で置く
    refs: dict[str, list[str]] = {}
    # 掃引は「採用版」＝30 s 参照だけを撃つ（decisions 39＝参照は 30 s 版のまま）。
    variants = ["30s"] if args.sweep_seed else ["30s"] + (["10s"] if args.also_ref10 else [])
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
    if args.sweep_seed:
        report.update(
            {
                "mode": "seed_sweep",
                "seeds": seeds,
                "tail_window_ms": args.tail_ms,
                "tail_margin_db": args.tail_margin_db,
                "tail_abs_dbfs": args.tail_abs_dbfs,
                "tail_profile_ms": args.tail_profile_ms,
                "tail_step_ms": args.tail_step_ms,
                "tail_final_ms": args.tail_final_ms,
                "tail_rise_db": args.tail_rise_db,
                "tail_floor_dbfs": args.tail_floor_dbfs,
                "tail_rule": _tail_rule_text(**tp),
                "trim_tail_fallback": not args.no_trim_fallback,
                "sweep": [],
            }
        )
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

        # ---- 4b. seed 掃引（decisions 39）
        if args.sweep_seed:
            sweep_dir = out_dir / "sweep"
            sweep_dir.mkdir(parents=True, exist_ok=True)
            for vid in refs:
                voice_id = vid  # 掃引は 30 s 参照＝素の id
                attempts: list[dict] = []
                adopted: dict | None = None
                # trim_tail=True で seed を掃引 → 出なければ trim_tail=False で同じ seed 列を一巡。
                passes = [True] if args.no_trim_fallback else [True, False]
                for trim_tail in passes:
                    if adopted:
                        break
                    for seed in seeds:
                        irodori = irodori_body(seed)
                        if not trim_tail:
                            irodori["trim_tail"] = False
                        payload = {
                            "model": par["model"],
                            "input": text,
                            "voice": voice_id,
                            "response_format": par["response_format"],
                            "irodori": irodori,
                        }
                        suffix = "" if trim_tail else "_notrim"
                        tmp = sweep_dir / f"{voice_id}_seed{seed}{suffix}.wav"
                        t0 = time.perf_counter()
                        try:
                            wav, ctype = _post(f"{base}/v1/audio/speech", payload, timeout=1800.0)
                        except Exception as exc:  # noqa: BLE001 — 1 射の失敗で掃引を止めない
                            print(f"[NG]   {voice_id} seed={seed} trim_tail={trim_tail}: {exc}", flush=True)
                            attempts.append(
                                {
                                    "seed": seed,
                                    "trim_tail": trim_tail,
                                    "cfg_scale_speaker": args.cfg_scale_speaker,
                                    "error": str(exc)[:1000],
                                }
                            )
                            rc = 1
                            continue
                        elapsed = time.perf_counter() - t0
                        tmp.write_bytes(wav)
                        t = tail_of(tmp, tp)
                        attempts.append(
                            {
                                "seed": seed,
                                "trim_tail": trim_tail,
                                # 射ごとに何で撃ったかを残す（None＝渡していない＝上流既定 5.0）。
                                # 檔名（<id>_seed<N>.wav）には cfg が入らないので、ここが唯一の素性。
                                "cfg_scale_speaker": args.cfg_scale_speaker,
                                "file": tmp.name,
                                "bytes": len(wav),
                                "elapsed_s": round(elapsed, 2),
                                **t,
                            }
                        )
                        mark = "clean" if t["tail_clean"] else "TAIL?"
                        why = f" why={'+'.join(t['tail_fail_reasons'])}" if t["tail_fail_reasons"] else ""
                        print(
                            f"[sweep] {voice_id} seed={seed} trim_tail={trim_tail} "
                            f"{t['duration_s']:.2f}s Δ={t['tail_delta_db']} dB "
                            f"tail50={t['tail_rms_dbfs']} dBFS rise={t['tail_rise_db']} dB "
                            f"[{mark}]{why} {elapsed:.1f} s",
                            flush=True,
                        )
                        if t["tail_clean"]:
                            name = f"{voice_id}_secondary.wav"
                            dest = out_dir / name
                            dest.write_bytes(wav)
                            adopted = {
                                "id": vid,
                                "voice": voice_id,
                                "ref_variant": "30s",
                                # 何の本文で撃ったかを行に刻む（裁定 112）。畳み込み先の top-level は
                                # 最初の run のままなので、行が自分の本文を持たないと嘘を名乗る。
                                "text_id": sec["id"],
                                "text": text,
                                "file": name,
                                "path": str(dest),
                                "bytes": len(wav),
                                "content_type": ctype,
                                "elapsed_s": round(elapsed, 2),
                                "request": payload,
                                "tail": t,
                                "seed": seed,
                                "trim_tail": trim_tail,
                                "tries": len(attempts),
                                "sweep_file": tmp.name,
                            }
                            report["items"].append(adopted)
                            print(
                                f"[adopt] {name}  seed={seed} trim_tail={trim_tail} "
                                f"（{len(attempts)} 回目で clean）",
                                flush=True,
                            )
                            break
                row = {
                    "id": vid,
                    "voice": voice_id,
                    # この行を撃った run の条件（run をまたいで畳んでも読めるように行に持たせる）
                    "seeds": seeds,
                    "cfg_scale_speaker": args.cfg_scale_speaker,
                    "tail_rule": _tail_rule_text(**tp),
                    "adopted": bool(adopted),
                    "adopted_seed": adopted["seed"] if adopted else None,
                    "adopted_trim_tail": adopted["trim_tail"] if adopted else None,
                    "tries": len(attempts),
                    "attempts": attempts,
                }
                if not adopted:
                    ok = [a for a in attempts if "error" not in a]
                    # 外れた条件が少ない順 → Δ が小さい順。3 条件になったので単純な Δ 最小では選べない。
                    best = (
                        min(
                            ok,
                            key=lambda a: (
                                len(a.get("tail_fail_reasons") or []),
                                a["tail_delta_db"] if a["tail_delta_db"] is not None else -999,
                            ),
                        )
                        if ok
                        else None
                    )
                    row["best_tail_delta_db"] = best["tail_delta_db"] if best else None
                    row["best_tail_rms_dbfs"] = best["tail_rms_dbfs"] if best else None
                    row["best_tail_rise_db"] = best["tail_rise_db"] if best else None
                    row["best_tail_fail_reasons"] = best.get("tail_fail_reasons") if best else None
                    row["best_seed"] = best["seed"] if best else None
                    row["best_trim_tail"] = best["trim_tail"] if best else None
                    row["why"] = (
                        f"seed {seeds[0]}〜{seeds[-1]} の {len(seeds)} 個"
                        + ("" if args.no_trim_fallback else "＋trim_tail=false の一巡")
                        + f"（計 {len(attempts)} 射）で末尾 clean が出なかった。"
                        + (f"最良は seed={best['seed']} trim_tail={best['trim_tail']} "
                           f"Δ={best['tail_delta_db']} dB tail50={best['tail_rms_dbfs']} dBFS "
                           f"rise={best['tail_rise_db']} dB 外れた条件={'+'.join(best.get('tail_fail_reasons') or []) or 'なし'}。"
                           if best else "全射が失敗した。")
                    )
                    print(f"[unres] {voice_id}: {row['why']}", flush=True)
                    rc = 1
                report["sweep"].append(row)

        # ---- 4. 二次生成
        for vid, voice_ids in (refs.items() if not args.sweep_seed else []):
            for voice_id in voice_ids:
                name = f"{voice_id}_secondary.wav"
                dest = out_dir / name
                payload = {
                    "model": par["model"],
                    "input": text,
                    "voice": voice_id,
                    "response_format": par["response_format"],
                    "irodori": irodori_body(par["seed"]),
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
                        # 掃引と同じく、何の本文で撃ったかを行に刻む（裁定 112）。
                        "text_id": sec["id"],
                        "text": text,
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
        if args.sweep_seed:
            sp = logs / "run_secondary.sweep.json"
            sp.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
            rp = merge_result(logs, report)
            print(f"[done] 掃引の台帳: {sp}\n[done] 畳み込み先: {rp}", flush=True)
        else:
            rp = logs / "run_secondary.result.json"
            rp.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"[done] 台帳: {rp}", flush=True)

    return rc


if __name__ == "__main__":
    sys.exit(main())
