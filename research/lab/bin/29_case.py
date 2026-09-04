"""29 — サーバ 1 ケース実射（起動→/health→合成 1 発→停止→port 確認）。

使い方（環境変数は呼び出し側で `env K=V ...` として渡す）:
  python 29_case.py --tag NAME [--boot-timeout 240] [--synth-timeout 300]

出力＝lab/out/29/case_<tag>.json（逐語）。ログ＝case_<tag>.out.log / case_<tag>.err.log
"""
from __future__ import annotations

import argparse
import json
import os
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT = "C:/Users/mugonkun/source/repos/irodori-native-research"
ROCM_PY = "C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe"
WORK = f"{ROOT}/lab/tmp/29"
OUT = f"{ROOT}/lab/out/29"
PORT = 8091
BASE = f"http://127.0.0.1:{PORT}"

TEXT = "こんにちは、読み分けちゃんのテストです。"
CAPTION = "落ち着いた女性の声で、丁寧に話す。"


def port_listening() -> bool:
    s = socket.socket()
    s.settimeout(0.4)
    try:
        s.connect(("127.0.0.1", PORT))
        return True
    except OSError:
        return False
    finally:
        s.close()


def netstat_8091() -> list[str]:
    p = subprocess.run(["netstat", "-ano"], capture_output=True, text=True, errors="replace")
    return [ln.strip() for ln in p.stdout.splitlines() if f":{PORT} " in ln or f":{PORT}\t" in ln]


def http(method: str, path: str, body: dict | None, timeout: float) -> dict:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(
        BASE + path,
        data=data,
        method=method,
        headers={"Content-Type": "application/json"} if data else {},
    )
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read()
            el = time.perf_counter() - t0
            hdr = {k.lower(): v for k, v in r.headers.items()}
            ct = hdr.get("content-type", "")
            rec: dict = {
                "status": r.status,
                "elapsed_s": round(el, 3),
                "headers": hdr,
                "bytes": len(raw),
            }
            if "audio" in ct:
                rec["body_head_hex"] = raw[:4].hex()
                rec["body_kind"] = "audio"
            else:
                rec["body"] = raw.decode("utf-8", "replace")
            return rec
    except urllib.error.HTTPError as e:  # noqa: PERF203
        raw = e.read()
        return {
            "status": e.code,
            "elapsed_s": round(time.perf_counter() - t0, 3),
            "headers": {k.lower(): v for k, v in e.headers.items()},
            "bytes": len(raw),
            "body": raw.decode("utf-8", "replace"),
        }
    except Exception as e:  # noqa: BLE001
        return {
            "status": None,
            "elapsed_s": round(time.perf_counter() - t0, 3),
            "err_type": type(e).__name__,
            "err": str(e),
        }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--tag", required=True)
    ap.add_argument("--boot-timeout", type=float, default=240.0)
    ap.add_argument("--synth-timeout", type=float, default=420.0)
    ap.add_argument("--steps", type=int, default=None)
    ap.add_argument("--no-warm", action="store_true")
    ap.add_argument("--text", default=TEXT)
    a = ap.parse_args()

    os.makedirs(WORK, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    outlog = f"{OUT}/case_{a.tag}.out.log"
    errlog = f"{OUT}/case_{a.tag}.err.log"

    rec: dict = {"tag": a.tag}
    rec["env"] = {
        k: os.environ.get(k)
        for k in sorted(os.environ)
        if k.startswith(("IRODORI_", "HIP_VISIBLE", "CUDA_VISIBLE", "ROCR_VISIBLE"))
    }
    rec["pre_netstat_8091"] = netstat_8091()

    t0 = time.perf_counter()
    fo = open(outlog, "wb")
    fe = open(errlog, "wb")
    proc = subprocess.Popen(
        [ROCM_PY, "-m", "irodori_openai_tts", "--host", "127.0.0.1", "--port", str(PORT)],
        cwd=WORK,
        stdout=fo,
        stderr=fe,
        env=os.environ.copy(),
    )
    rec["pid"] = proc.pid

    listening = False
    exited = None
    while time.perf_counter() - t0 < a.boot_timeout:
        rc = proc.poll()
        if rc is not None:
            exited = rc
            break
        if port_listening():
            listening = True
            break
        time.sleep(0.3)
    rec["boot_elapsed_s"] = round(time.perf_counter() - t0, 3)
    rec["listening"] = listening
    rec["proc_exit_code_during_boot"] = exited

    if listening:
        rec["health"] = http("GET", "/health", None, 30.0)
        body = {
            "model": "irodori-tts",
            "input": a.text,
            "irodori": {"caption": CAPTION, "no_ref": True, "seed": 1234},
        }
        if a.steps is not None:
            body["irodori"]["num_steps"] = a.steps
        rec["request_body"] = body
        rec["synth1"] = http("POST", "/v1/audio/speech", body, a.synth_timeout)
        # 2 発目（ウォーム）— 1 発目が 200 のときだけ
        if rec["synth1"].get("status") == 200 and not a.no_warm:
            rec["synth2"] = http("POST", "/v1/audio/speech", body, a.synth_timeout)
            rec["health_after"] = http("GET", "/health", None, 30.0)

    # 停止
    if proc.poll() is None:
        subprocess.run(
            ["taskkill", "/T", "/F", "/PID", str(proc.pid)],
            capture_output=True,
            text=True,
            errors="replace",
        )
        try:
            proc.wait(timeout=30)
        except subprocess.TimeoutExpired:
            pass
    rec["final_exit_code"] = proc.poll()
    fo.close()
    fe.close()
    time.sleep(1.0)
    rec["post_netstat_8091"] = netstat_8091()
    rec["post_port_listening"] = port_listening()

    for name, path in (("stdout", outlog), ("stderr", errlog)):
        try:
            with open(path, "rb") as f:
                txt = f.read().decode("utf-8", "replace")
        except OSError:
            txt = ""
        rec[f"{name}_bytes"] = len(txt)
        rec[f"{name}_tail"] = txt[-6000:]

    rec["total_elapsed_s"] = round(time.perf_counter() - t0, 3)
    with open(f"{OUT}/case_{a.tag}.json", "w", encoding="utf-8") as f:
        json.dump(rec, f, ensure_ascii=False, indent=1)

    print(
        json.dumps(
            {
                "tag": a.tag,
                "env": rec["env"],
                "boot_s": rec["boot_elapsed_s"],
                "listening": rec["listening"],
                "exit_during_boot": rec["proc_exit_code_during_boot"],
                "health_model": (rec.get("health", {}).get("body") or "")[:400],
                "synth1_status": rec.get("synth1", {}).get("status"),
                "synth1_s": rec.get("synth1", {}).get("elapsed_s"),
                "synth1_body": (rec.get("synth1", {}).get("body") or rec.get("synth1", {}).get("body_kind") or "")[:600],
                "synth2_status": rec.get("synth2", {}).get("status"),
                "synth2_s": rec.get("synth2", {}).get("elapsed_s"),
                "post_netstat_LISTEN": [l for l in rec["post_netstat_8091"] if "LISTEN" in l.upper()],
                "post_port_listening": rec["post_port_listening"],
                "total_s": rec["total_elapsed_s"],
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
