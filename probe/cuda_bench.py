#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Bench the assembled wrapper over HTTP and write one machine-made JSON.

Seat B (RTX 3090, remote) tool.  Acceptance conditions B-4, B-5 and B-6 of
``docs/design/ben-b-rtx-remote.md``; budgets from ``docs/acceptance.md``.

Standard library only, Python 3.12.  It never imports torch and never touches
the model: everything it knows about the run comes from ``GET /ywk/status``,
from the wav bytes the server answers with, and from ``nvidia-smi``.

Three modes
-----------
1. bench (default)::

       python cuda_bench.py --port 18090 --cases probe/bench_cases.json \\
           --json-out build/out/probe-log/cuda-bench-cu130.json

   Waits for ``GET /health``, reads ``/params`` and ``/ywk/status``, then fires
   the condition table from the cases file (case x steps x reference), then the
   preset sweep (one shot per preset, in order), and writes the JSON.

2. wav head-trim, used to build the 10 s reference clip::

       python cuda_bench.py --trim-wav in.wav --trim-out out.wav --trim-seconds 10.0

3. post-kill VRAM, called by cuda-bench.ps1 after the tree kill::

       python cuda_bench.py --post-kill --json-out <the bench json> --wait-seconds 10

Two RTF definitions
-------------------
``research/lab/kit-cuda/bench_infer.py`` computes ``rtf_synth`` =
``total_to_decode / audio_seconds``, where ``total_to_decode`` is a ``[timing]``
line the upstream ``infer.py`` prints inside the same process.  Through the
wrapper's HTTP port that line is not reachable, so this tool measures
``rtf_http`` = (HTTP round trip, from the start of the request to the last byte
of the response body) / (audio seconds of the returned wav).  ``rtf_http`` is
always the larger of the two: it carries chunk stitching, wav serialisation,
uvicorn scheduling and the loopback round trip on top of the synthesis.  Both
definitions are written into the JSON so a reader can never mistake one for the
other, and ``rtf_synth`` is emitted as null rather than guessed.

The JSON it writes is pure ASCII (``\\uXXXX`` for every Japanese character), LF,
no BOM, so ``Get-Content -Raw | ConvertFrom-Json`` reads it on Windows PowerShell
5.1 with a CP932 ANSI page as well as on PowerShell 7.  See ``write_json``.

Exit codes: 0 = every attempted shot answered 200 with a RIFF/WAVE body;
3 = an operational failure (server never became ready, a shot failed, a body was
not a wav); 4 = --fail-on-budget was given and a budget was missed.  A missed
budget alone is a *measurement*, not a script failure, so by default it is
reported in the JSON and the exit code stays 0.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import statistics
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

TOOL = "probe/cuda_bench.py"
SCHEMA = 1
USER_AGENT = "irodori-ywk-cuda-bench/1"

EXIT_OK = 0
EXIT_OPERATIONAL = 3
EXIT_BUDGET = 4

RTF_DEFINITIONS = {
    "rtf_http": (
        "This tool's number. (HTTP round trip of POST /v1/audio/speech, from the "
        "start of the request to the last byte of the response body, in seconds) "
        "/ (audio seconds of the returned wav, read from its RIFF header)."
    ),
    "rtf_synth": (
        "The kit's number (research/lab/kit-cuda/bench_infer.py): "
        "total_to_decode / audio_seconds, where total_to_decode is the [timing] "
        "line upstream infer.py prints in-process. Not reachable through the "
        "wrapper's HTTP port, so every rtf_synth field here is null - never "
        "estimated."
    ),
    "relation": (
        "rtf_http > rtf_synth for the same shot: HTTP carries chunk stitching, "
        "wav serialisation, uvicorn scheduling and the loopback round trip. The "
        "budgets in docs/acceptance.md (short 0.25 / long 0.10 / reference 0.35) "
        "were measured with rtf_synth, so a budget miss on rtf_http is not by "
        "itself a failure of the acceptance row."
    ),
}


# --------------------------------------------------------------------------
# console
# --------------------------------------------------------------------------


def _make_console_utf8() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def log(message: str) -> None:
    print("[bench] " + message, flush=True)


def utcnow() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


# --------------------------------------------------------------------------
# http
# --------------------------------------------------------------------------


def http_call(
    url: str,
    *,
    method: str = "GET",
    payload: dict | None = None,
    timeout: float = 600.0,
    accept: str = "*/*",
) -> dict:
    """One HTTP call.  Never raises; transport failures come back in ``error``."""
    data = None
    headers = {"Accept": accept, "User-Agent": USER_AGENT}
    if payload is not None:
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json; charset=utf-8"
    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    status = 0
    body = b""
    resp_headers: dict[str, str] = {}
    error = None
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            status = int(response.status)
            resp_headers = {str(k).lower(): str(v) for k, v in response.headers.items()}
            body = response.read()
    except urllib.error.HTTPError as exc:
        status = int(exc.code)
        try:
            resp_headers = {str(k).lower(): str(v) for k, v in exc.headers.items()}
        except Exception:
            resp_headers = {}
        try:
            body = exc.read()
        except Exception:
            body = b""
    except Exception as exc:  # URLError, timeout, connection refused, ...
        error = "%s: %s" % (type(exc).__name__, exc)
    elapsed_ms = round((time.perf_counter() - started) * 1000.0, 1)
    return {
        "url": url,
        "method": method,
        "status": status,
        "headers": resp_headers,
        "body": body,
        "elapsed_ms": elapsed_ms,
        "error": error,
    }


def body_text(result: dict, limit: int = 4000) -> str:
    try:
        return result["body"].decode("utf-8", "replace")[:limit]
    except Exception:
        return ""


def body_json(result: dict):
    try:
        return json.loads(result["body"].decode("utf-8"))
    except Exception:
        return None


# --------------------------------------------------------------------------
# wav
# --------------------------------------------------------------------------


def wav_info(blob: bytes) -> dict:
    """Read a RIFF/WAVE header.  No library: the runtime must stay untouched."""
    out = {
        "is_riff_wave": False,
        "reason": None,
        "audio_format": None,
        "channels": None,
        "sample_rate": None,
        "bits_per_sample": None,
        "frames": None,
        "audio_seconds": None,
        "data_bytes": None,
    }
    if len(blob) < 12 or blob[0:4] != b"RIFF" or blob[8:12] != b"WAVE":
        out["reason"] = "not a RIFF/WAVE payload (first 12 bytes: %s)" % (
            blob[:12].hex(" ") if blob else "<empty>",
        )
        return out
    out["is_riff_wave"] = True
    position = 12
    fmt = None
    data_bytes = None
    while position + 8 <= len(blob):
        chunk_id = blob[position : position + 4]
        (chunk_size,) = struct.unpack_from("<I", blob, position + 4)
        chunk_at = position + 8
        if chunk_id == b"fmt " and chunk_size >= 16 and chunk_at + 16 <= len(blob):
            fmt = struct.unpack_from("<HHIIHH", blob, chunk_at)
        elif chunk_id == b"data":
            available = len(blob) - chunk_at
            if chunk_size == 0 or chunk_size == 0xFFFFFFFF or chunk_size > available:
                # a streamed wav writes a placeholder size; trust what arrived
                data_bytes = available
            else:
                data_bytes = chunk_size
            break
        position = chunk_at + chunk_size + (chunk_size & 1)
    if fmt is None:
        out["reason"] = "no fmt chunk"
        return out
    audio_format, channels, sample_rate, _byte_rate, block_align, bits = fmt
    out["audio_format"] = audio_format
    out["channels"] = channels
    out["sample_rate"] = sample_rate
    out["bits_per_sample"] = bits
    out["data_bytes"] = data_bytes
    if data_bytes is None:
        out["reason"] = "no data chunk"
        return out
    if not block_align:
        block_align = max(1, (channels or 1) * max(1, bits // 8))
    frames = data_bytes // block_align
    out["frames"] = frames
    if sample_rate:
        out["audio_seconds"] = round(frames / float(sample_rate), 4)
    else:
        out["reason"] = "sample_rate is 0"
    return out


def wav_info_file(path: Path) -> dict:
    try:
        head = path.read_bytes()
    except OSError as exc:
        return {"is_riff_wave": False, "reason": "%s: %s" % (type(exc).__name__, exc)}
    return wav_info(head)


def trim_wav(source: Path, destination: Path, seconds: float) -> dict:
    """Write the first ``seconds`` of ``source`` as a canonical RIFF/WAVE file.

    A byte-level head cut of the PCM data chunk: no resampling, no re-encoding
    and no level normalisation (decisions.md 38 - upstream normalises every
    reference clip to -16 dB itself, so touching the level here would only
    invent a difference).
    """
    blob = source.read_bytes()
    info = wav_info(blob)
    if not info["is_riff_wave"] or info["audio_seconds"] is None:
        raise SystemExit("trim: %s is not a readable RIFF/WAVE file (%s)" % (source, info["reason"]))
    # find the data chunk offset again, this time keeping the position
    position = 12
    fmt_chunk = None
    data_at = None
    data_bytes = None
    while position + 8 <= len(blob):
        chunk_id = blob[position : position + 4]
        (chunk_size,) = struct.unpack_from("<I", blob, position + 4)
        chunk_at = position + 8
        if chunk_id == b"fmt ":
            fmt_chunk = blob[chunk_at : chunk_at + chunk_size]
        elif chunk_id == b"data":
            available = len(blob) - chunk_at
            data_at = chunk_at
            data_bytes = available if (chunk_size == 0 or chunk_size > available) else chunk_size
            break
        position = chunk_at + chunk_size + (chunk_size & 1)
    if fmt_chunk is None or data_at is None or data_bytes is None:
        raise SystemExit("trim: %s has no fmt/data chunk pair" % source)
    channels = info["channels"] or 1
    bits = info["bits_per_sample"] or 16
    sample_rate = info["sample_rate"]
    block_align = max(1, channels * max(1, bits // 8))
    wanted_frames = int(round(seconds * sample_rate))
    have_frames = data_bytes // block_align
    frames = min(wanted_frames, have_frames)
    kept = frames * block_align
    payload = blob[data_at : data_at + kept]
    if len(fmt_chunk) % 2:
        fmt_chunk = fmt_chunk + b"\x00"
    pad = b"\x00" if (len(payload) % 2) else b""
    riff_size = 4 + (8 + len(fmt_chunk)) + (8 + len(payload) + len(pad))
    out = bytearray()
    out += b"RIFF" + struct.pack("<I", riff_size) + b"WAVE"
    out += b"fmt " + struct.pack("<I", len(fmt_chunk)) + fmt_chunk
    out += b"data" + struct.pack("<I", len(payload)) + payload + pad
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(bytes(out))
    return {
        "source": str(source),
        "destination": str(destination),
        "requested_seconds": seconds,
        "source_seconds": info["audio_seconds"],
        "written_seconds": round(frames / float(sample_rate), 4),
        "written_bytes": len(out),
        "sample_rate": sample_rate,
        "channels": channels,
        "bits_per_sample": bits,
        "truncated_to_source": frames < wanted_frames,
    }


# --------------------------------------------------------------------------
# nvidia-smi
# --------------------------------------------------------------------------

_SMI_CACHE: dict[str, object] = {}


def nvidia_smi_path() -> str | None:
    if "path" not in _SMI_CACHE:
        found = shutil.which("nvidia-smi")
        if found is None:
            # research/lab/notes/33: on the RTX machine it lives in System32 and
            # is on PATH; a stripped PATH is still worth one explicit look.
            candidate = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32" / "nvidia-smi.exe"
            found = str(candidate) if candidate.is_file() else None
        _SMI_CACHE["path"] = found
    return _SMI_CACHE["path"]  # type: ignore[return-value]


def nvidia_smi_query(fields: str) -> list[str] | None:
    exe = nvidia_smi_path()
    if exe is None:
        return None
    try:
        completed = subprocess.run(
            [exe, "--query-gpu=" + fields, "--format=csv,noheader"],
            capture_output=True,
            text=True,
            timeout=60,
        )
    except Exception:
        return None
    if completed.returncode != 0:
        return None
    return [line.strip() for line in completed.stdout.splitlines() if line.strip()]


def vram_used_mib() -> int | None:
    """memory.used of GPU 0, in MiB.  None when there is no nvidia-smi."""
    lines = nvidia_smi_query("memory.used")
    if not lines:
        return None
    head = lines[0].replace("MiB", "").strip()
    try:
        return int(head)
    except ValueError:
        return None


def nvidia_smi_snapshot() -> dict:
    exe = nvidia_smi_path()
    if exe is None:
        return {
            "available": False,
            "path": None,
            "reason": "nvidia-smi not found on PATH nor in System32 (expected on a CPU-only machine)",
            "gpus": [],
        }
    lines = nvidia_smi_query("index,name,uuid,pci.bus_id,driver_version,memory.total,memory.used")
    gpus = []
    for line in lines or []:
        parts = [p.strip() for p in line.split(",")]
        while len(parts) < 7:
            parts.append("")
        gpus.append(
            {
                "index": parts[0],
                "name": parts[1],
                "uuid": parts[2],
                "pci_bus_id": parts[3],
                "driver_version": parts[4],
                "memory_total": parts[5],
                "memory_used": parts[6],
            }
        )
    return {"available": True, "path": exe, "reason": None, "gpus": gpus}


# --------------------------------------------------------------------------
# bench
# --------------------------------------------------------------------------


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def wait_ready(base: str, timeout: float) -> dict:
    """Poll GET /health until 200.

    With IRODORI_PRELOAD=true the upstream lifespan loads the model before
    uvicorn starts serving, so the first 200 already means "model in".
    """
    started = time.perf_counter()
    deadline = started + timeout
    attempts = 0
    last = None
    while time.perf_counter() < deadline:
        attempts += 1
        last = http_call(base + "/health", timeout=15.0)
        if last["status"] == 200:
            return {
                "ok": True,
                "attempts": attempts,
                "waited_seconds": round(time.perf_counter() - started, 3),
                "last_status": 200,
                "last_error": None,
            }
        time.sleep(0.7)
    return {
        "ok": False,
        "attempts": attempts,
        "waited_seconds": round(time.perf_counter() - started, 3),
        "last_status": (last or {}).get("status", 0),
        "last_error": (last or {}).get("error"),
        "timeout_seconds": timeout,
    }


def do_shot(
    base: str,
    *,
    text: str,
    caption: str,
    steps: int,
    voice: str,
    seed: int,
    timeout: float,
    save_to: Path | None = None,
) -> dict:
    payload = {
        "model": "irodori-tts",
        "input": text,
        "response_format": "wav",
        "voice": voice,
        "irodori": {"num_steps": steps, "caption": caption, "seed": seed},
    }
    result = http_call(
        base + "/v1/audio/speech",
        method="POST",
        payload=payload,
        timeout=timeout,
        accept="audio/wav",
    )
    entry: dict = {
        "http_status": result["status"],
        "elapsed_ms": result["elapsed_ms"],
        "response_bytes": len(result["body"]),
        "content_type": result["headers"].get("content-type"),
        "irodori_seed_header": result["headers"].get("x-irodori-seed"),
        "transport_error": result["error"],
        "audio_seconds": None,
        "rtf_http": None,
        "rtf_synth": None,
        "wav": None,
        "ok": False,
        "error": None,
    }
    if result["error"] is not None:
        entry["error"] = "transport: " + str(result["error"])
    elif result["status"] != 200:
        entry["error"] = "status %d: %s" % (result["status"], body_text(result, 800))
    else:
        info = wav_info(result["body"])
        entry["wav"] = info
        if not info["is_riff_wave"] or info["audio_seconds"] is None:
            entry["error"] = "response is not a usable wav: %s" % info["reason"]
        else:
            entry["audio_seconds"] = info["audio_seconds"]
            if info["audio_seconds"] > 0:
                entry["rtf_http"] = round(
                    (result["elapsed_ms"] / 1000.0) / info["audio_seconds"], 4
                )
            entry["ok"] = True
    entry["vram_used_mib_after_shot"] = vram_used_mib()
    if save_to is not None and entry["ok"]:
        try:
            save_to.parent.mkdir(parents=True, exist_ok=True)
            save_to.write_bytes(result["body"])
            entry["wav_file"] = str(save_to)
            entry["wav_sha256"] = hashlib.sha256(result["body"]).hexdigest()
        except OSError as exc:
            entry["wav_file_error"] = "%s: %s" % (type(exc).__name__, exc)
    return entry


def median_of(values: list[float]) -> float | None:
    clean = [v for v in values if v is not None]
    if not clean:
        return None
    return round(statistics.median(clean), 4)


def reference_for(cases: dict, name: str, preset_id: str, voices_dir: Path) -> dict:
    """Turn a reference axis name into {voice, wav path, measured seconds}."""
    table = cases["reference_voices"]
    if name == "none":
        return {
            "kind": "none",
            "voice": table["none"]["voice"],
            "wav_file": None,
            "reference_seconds": None,
            "available": True,
            "reason": None,
        }
    spec = table.get(name)
    if spec is None:
        return {
            "kind": name,
            "voice": None,
            "wav_file": None,
            "reference_seconds": None,
            "available": False,
            "reason": "no reference_voices.%s in the cases file" % name,
        }
    voice = preset_id + str(spec.get("voice_id_suffix", ""))
    path = voices_dir / (voice + ".wav")
    if not path.is_file():
        return {
            "kind": name,
            "voice": voice,
            "wav_file": str(path),
            "reference_seconds": None,
            "available": False,
            "reason": "reference wav is not staged in the voices dir: " + path.name,
        }
    info = wav_info_file(path)
    return {
        "kind": name,
        "voice": voice,
        "wav_file": path.name,
        "reference_seconds": info.get("audio_seconds"),
        "reference_sample_rate": info.get("sample_rate"),
        "available": True,
        "reason": None,
    }


def budget_for(cases: dict, case: str, steps: int, reference: str, precision: str | None):
    for rule in cases.get("budgets", {}).get("rtf", []):
        if (
            rule.get("case") == case
            and int(rule.get("steps", -1)) == steps
            and rule.get("reference") == reference
            and rule.get("precision") == precision
        ):
            return rule
    return None


def run_bench(args: argparse.Namespace) -> int:
    cases_path = Path(args.cases).resolve()
    cases = json.loads(cases_path.read_text(encoding="utf-8"))
    voices_dir = Path(args.voices_dir).resolve() if args.voices_dir else None
    wav_dir = Path(args.wav_dir).resolve() if args.wav_dir else None
    base = "http://%s:%d" % (args.host, args.port)
    seed = int(cases.get("request", {}).get("template", {}).get("irodori", {}).get("seed", 1234))
    caption = cases["caption"]

    result: dict = {
        "schema": SCHEMA,
        "tool": TOOL,
        "generated": utcnow(),
        "label": args.label,
        "run": {
            "base_url": base,
            "variant": args.variant,
            "device_arg": args.device,
            "precision_arg": args.precision,
            "quick": bool(args.quick),
            "warm_reps": int(args.warm_reps),
            "seed": seed,
            "shot_timeout_seconds": args.shot_timeout,
            "voices_dir": (voices_dir.name if voices_dir else None),
            "reference_preset_id": args.reference_preset,
            "preset_sweep_enabled": not (args.no_preset_sweep or args.quick),
            # decisions.md 34: "passing a UUID (GPU-xxxx) in CUDA_VISIBLE_DEVICES has
            # no live record in the survey; seat B is to fire it and decide."  This is
            # what the server process actually inherited, read from this process' own
            # environment (cuda-bench.ps1 sets it here and children inherit).
            "cuda_visible_devices": os.environ.get("CUDA_VISIBLE_DEVICES"),
        },
        "cases_file": {
            "path": str(cases_path),
            "sha256": sha256_file(cases_path),
            "frozen_text_from": cases.get("source", {}).get("frozen_text_from"),
        },
        "rtf_definitions": RTF_DEFINITIONS,
        "nvidia_smi": nvidia_smi_snapshot(),
        "vram": {
            "probe": ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader"],
            "unit": "MiB",
            "idle_before_start_mib": args.idle_vram_mib,
            "after_ready_mib": None,
            "peak_observed_mib": None,
            "after_kill": None,
            "note": (
                "memory.used is the whole process' occupancy of GPU 0, not the "
                "torch allocator's peak. It also counts anything else on the card."
            ),
        },
        "ready": None,
        "params": [],
        "cuda_visible_devices_uuid": None,
        "status_after_ready": None,
        "status_after_run": None,
        "conditions": [],
        "run_cold": None,
        "preset_sweep": None,
        "budget_checks": [],
        "checks": [],
        "errors": [],
        "ok": False,
    }

    def add_check(name: str, ok: bool, detail: str) -> None:
        result["checks"].append({"check": name, "ok": bool(ok), "detail": detail})
        log(("PASS  " if ok else "FAIL  ") + name + "  " + detail)

    # ---------------------------------------------------------------- ready
    log("waiting for %s/health (timeout %d s)" % (base, args.ready_timeout))
    ready = wait_ready(base, float(args.ready_timeout))
    result["ready"] = ready
    add_check(
        "GET /health",
        ready["ok"],
        "200 after %.3f s (%d attempts)" % (ready["waited_seconds"], ready["attempts"])
        if ready["ok"]
        else "no 200 within %d s (last status %s, last error %s)"
        % (args.ready_timeout, ready.get("last_status"), ready.get("last_error")),
    )
    if not ready["ok"]:
        result["errors"].append("server never became ready")
        write_json(args.json_out, result)
        return EXIT_OPERATIONAL

    result["vram"]["after_ready_mib"] = vram_used_mib()

    # ------------------------------------------------------------ /params
    for _ in range(2):
        probe = http_call(base + "/params", timeout=60.0)
        result["params"].append(
            {"status": probe["status"], "elapsed_ms": probe["elapsed_ms"], "error": probe["error"]}
        )
    last_params = result["params"][-1]
    add_check(
        "GET /params",
        last_params["status"] == 200,
        "status=%s, %s ms (second call)" % (last_params["status"], last_params["elapsed_ms"]),
    )

    status_probe = http_call(base + "/ywk/status", timeout=60.0)
    status_json = body_json(status_probe)
    result["status_after_ready"] = status_json
    device_block = (status_json or {}).get("device") or {}
    actual_precision = device_block.get("precision")
    add_check(
        "GET /ywk/status",
        status_probe["status"] == 200,
        "status=%s device.actual=%s name=%s precision=%s"
        % (
            status_probe["status"],
            device_block.get("actual"),
            device_block.get("name"),
            actual_precision,
        ),
    )
    result["run"]["precision_actual"] = actual_precision
    result["run"]["device_actual"] = device_block.get("actual")

    # --------------------------------------- decisions.md 34: the UUID route
    #
    # When CUDA_VISIBLE_DEVICES carries a UUID (GPU-xxxx...), the card the driver
    # exposes as cuda:0 must be that card.  /ywk/status names the uuid it actually
    # got, so the two strings are directly comparable.  This is the only evidence
    # for decisions.md 34; it is recorded whether it matches or not.
    visible = os.environ.get("CUDA_VISIBLE_DEVICES") or ""
    reported_uuid = device_block.get("uuid")
    uuid_block = {
        "cuda_visible_devices": (visible or None),
        "is_uuid_form": visible.strip().upper().startswith("GPU-"),
        "status_device_uuid": reported_uuid,
        "status_device_actual": device_block.get("actual"),
        "status_device_name": device_block.get("name"),
        "match": None,
        "note": (
            "decisions.md 34. Only meaningful when cuda-bench.ps1 was given "
            "-CudaVisibleDevices with a GPU-xxxx uuid from nvidia-smi -L; on every "
            "other run is_uuid_form is false and match is null."
        ),
    }
    if uuid_block["is_uuid_form"]:
        wanted = visible.strip().split(",")[0].strip()
        got = str(reported_uuid or "").strip()
        uuid_block["requested_uuid"] = wanted
        # nvidia-smi -L prints "GPU-<uuid>" while torch reports the bare uuid;
        # compare with the "GPU-" prefix stripped from both sides (ben B B-4b, 2026-09-05).
        def _bare(u):
            u = u.strip().upper()
            return u[4:] if u.startswith("GPU-") else u
        uuid_block["match"] = bool(got) and _bare(got) == _bare(wanted)
        add_check(
            "CUDA_VISIBLE_DEVICES uuid == /ywk/status device.uuid",
            bool(uuid_block["match"]),
            "requested=%s reported=%s device.actual=%s"
            % (wanted, reported_uuid, device_block.get("actual")),
        )
    result["cuda_visible_devices_uuid"] = uuid_block

    # --------------------------------------------------------- conditions
    axis = cases["conditions"]
    case_names = list(axis["cases"])
    steps_list = [int(s) for s in axis["steps"]]
    reference_list = list(axis["reference"])
    warm_reps = int(args.warm_reps)
    if args.quick:
        case_names = case_names[:1]
        steps_list = steps_list[:1]
        reference_list = ["none"]
        warm_reps = min(warm_reps, 1)
        result["run"]["quick_note"] = (
            "quick: one condition (%s / %d steps / no reference), %d warm shot(s), "
            "no preset sweep. A dry run of the plumbing, not a measurement of the "
            "acceptance rows." % (case_names[0], steps_list[0], warm_reps)
        )
    result["run"]["warm_reps_effective"] = warm_reps

    saved_wavs = 0
    peak_vram = None

    def note_vram(value) -> None:
        nonlocal peak_vram
        if value is None:
            return
        if peak_vram is None or value > peak_vram:
            peak_vram = value

    note_vram(result["vram"]["after_ready_mib"])

    is_first_shot_of_run = True
    for case_name in case_names:
        case = cases["cases"][case_name]
        for steps in steps_list:
            for reference_name in reference_list:
                reference = reference_for(
                    cases, reference_name, args.reference_preset, voices_dir or Path(".")
                )
                condition = {
                    "case": case_name,
                    "steps": steps,
                    "reference": reference_name,
                    "precision": actual_precision,
                    "voice": reference["voice"],
                    "reference_wav": reference["wav_file"],
                    "reference_seconds": reference["reference_seconds"],
                    "expected_audio_seconds_cpu": case.get("expected_audio_seconds_cpu"),
                    "skipped": False,
                    "skip_reason": None,
                    "shots": [],
                    "warm_median_elapsed_ms": None,
                    "warm_median_rtf_http": None,
                    "warm_median_audio_seconds": None,
                    "first_elapsed_ms": None,
                    "first_rtf_http": None,
                }
                label = "%s/%d/%s" % (case_name, steps, reference_name)
                if not reference["available"]:
                    condition["skipped"] = True
                    condition["skip_reason"] = reference["reason"]
                    result["conditions"].append(condition)
                    log("SKIP  %s  %s" % (label, reference["reason"]))
                    continue
                for shot_index in range(warm_reps + 1):
                    kind = "first" if shot_index == 0 else "warm"
                    save_to = None
                    if (
                        wav_dir is not None
                        and shot_index == 0
                        and saved_wavs < int(args.max_wavs)
                    ):
                        save_to = wav_dir / ("%s_s%d_%s.wav" % (case_name, steps, reference_name))
                        saved_wavs += 1
                    shot = do_shot(
                        base,
                        text=case["text"],
                        caption=caption,
                        steps=steps,
                        voice=reference["voice"],
                        seed=seed,
                        timeout=float(args.shot_timeout),
                        save_to=save_to,
                    )
                    shot["kind"] = kind
                    shot["rep"] = shot_index + 1
                    shot["is_run_cold"] = bool(is_first_shot_of_run)
                    note_vram(shot.get("vram_used_mib_after_shot"))
                    condition["shots"].append(shot)
                    if is_first_shot_of_run:
                        result["run_cold"] = {
                            "case": case_name,
                            "steps": steps,
                            "reference": reference_name,
                            "elapsed_ms": shot["elapsed_ms"],
                            "elapsed_seconds": round(shot["elapsed_ms"] / 1000.0, 3),
                            "audio_seconds": shot["audio_seconds"],
                            "rtf_http": shot["rtf_http"],
                            "ok": shot["ok"],
                            "note": (
                                "the first synthesis after GET /health answered 200. "
                                "With IRODORI_PRELOAD=true the model is already in, so "
                                "this measures the cold shapes, not the model load "
                                "(docs/acceptance.md cold row)."
                            ),
                        }
                        is_first_shot_of_run = False
                    log(
                        "%s rep=%d kind=%s ok=%s %s ms audio=%s rtf_http=%s vram=%s"
                        % (
                            label,
                            shot["rep"],
                            kind,
                            shot["ok"],
                            shot["elapsed_ms"],
                            shot["audio_seconds"],
                            shot["rtf_http"],
                            shot.get("vram_used_mib_after_shot"),
                        )
                    )
                    if not shot["ok"]:
                        result["errors"].append("%s rep %d: %s" % (label, shot["rep"], shot["error"]))
                        if shot_index == 0:
                            # a condition that fails on its first shot fails the
                            # same way three more times; do not burn the time
                            condition["aborted_after_first_failure"] = True
                            break
                warm = [s for s in condition["shots"] if s["kind"] == "warm" and s["ok"]]
                first = [s for s in condition["shots"] if s["kind"] == "first"]
                if first:
                    condition["first_elapsed_ms"] = first[0]["elapsed_ms"]
                    condition["first_rtf_http"] = first[0]["rtf_http"]
                if warm:
                    condition["warm_median_elapsed_ms"] = median_of([s["elapsed_ms"] for s in warm])
                    condition["warm_median_rtf_http"] = median_of([s["rtf_http"] for s in warm])
                    condition["warm_median_audio_seconds"] = median_of(
                        [s["audio_seconds"] for s in warm]
                    )
                condition["warm_median_rtf_synth"] = None
                result["conditions"].append(condition)

    result["vram"]["peak_observed_mib"] = peak_vram

    # -------------------------------------------------------- preset sweep
    sweep_spec = cases.get("preset_sweep", {})
    if args.no_preset_sweep or args.quick:
        result["preset_sweep"] = {
            "skipped": True,
            "reason": "--no-preset-sweep" if args.no_preset_sweep else "--quick",
        }
    elif voices_dir is None:
        result["preset_sweep"] = {"skipped": True, "reason": "no --voices-dir"}
    else:
        sweep = {
            "skipped": False,
            "purpose": sweep_spec.get("purpose"),
            "expectation": sweep_spec.get("expectation"),
            "case": sweep_spec.get("case", "short"),
            "steps": int(sweep_spec.get("steps", 40)),
            "reference": sweep_spec.get("reference", "ref30"),
            "shots": [],
        }
        sweep_case = cases["cases"][sweep["case"]]
        log("preset sweep: %d presets, one shot each" % len(sweep_spec.get("presets", [])))
        for preset in sweep_spec.get("presets", []):
            reference = reference_for(
                cases, sweep["reference"], preset["id"], voices_dir
            )
            entry: dict = {
                "id": preset["id"],
                "display_name": preset.get("display_name"),
                "engine": preset.get("engine"),
                "voice": reference["voice"],
                "reference_seconds": reference["reference_seconds"],
            }
            if not reference["available"]:
                entry["skipped"] = True
                entry["skip_reason"] = reference["reason"]
                sweep["shots"].append(entry)
                log("SKIP  preset %s  %s" % (preset["id"], reference["reason"]))
                continue
            shot = do_shot(
                base,
                text=sweep_case["text"],
                caption=caption,
                steps=sweep["steps"],
                voice=reference["voice"],
                seed=seed,
                timeout=float(args.shot_timeout),
                save_to=None,
            )
            note_vram(shot.get("vram_used_mib_after_shot"))
            entry["skipped"] = False
            entry["shot"] = shot
            sweep["shots"].append(entry)
            log(
                "preset %-22s ok=%s %s ms audio=%s rtf_http=%s vram=%s"
                % (
                    preset["id"],
                    shot["ok"],
                    shot["elapsed_ms"],
                    shot["audio_seconds"],
                    shot["rtf_http"],
                    shot.get("vram_used_mib_after_shot"),
                )
            )
            if not shot["ok"]:
                result["errors"].append("preset %s: %s" % (preset["id"], shot["error"]))
        fired = [e for e in sweep["shots"] if not e.get("skipped") and e["shot"]["ok"]]
        if fired:
            times = [e["shot"]["elapsed_ms"] for e in fired]
            slowest = max(fired, key=lambda e: e["shot"]["elapsed_ms"])
            sweep["summary"] = {
                "fired": len(fired),
                "min_elapsed_ms": min(times),
                "median_elapsed_ms": median_of(times),
                "max_elapsed_ms": max(times),
                "slowest_id": slowest["id"],
                "spread_ratio": round(max(times) / min(times), 3) if min(times) else None,
                "reading": (
                    "spread_ratio near 1 means CUDA pays no first-sight penalty for an "
                    "unseen reference shape (research/lab/notes/33 section 5). A single outlier "
                    "many times the median is the ROCm behaviour of decisions.md 40 "
                    "(0.6-1.0 s -> 14.8 s on the first VOICEROID2 reference)."
                ),
            }
        result["preset_sweep"] = sweep
        result["vram"]["peak_observed_mib"] = peak_vram

    status_probe2 = http_call(base + "/ywk/status", timeout=60.0)
    result["status_after_run"] = body_json(status_probe2)

    # ------------------------------------------------------------ budgets
    #
    # Every budget in docs/acceptance.md except the CPU row is a 3090 row. On a CPU
    # variant none of them applies (decisions.md 13: CPU synthesis is allowed and its
    # slowness is not cared about), so on cpu only the CPU row is evaluated and the
    # rest is not even listed - a "missed" 2.0 s cold budget on a CPU run would be a
    # lie about what was measured.
    budgets = cases.get("budgets", {})
    is_cpu_run = str(result["run"].get("device_actual") or args.device or "").lower().startswith("cpu")
    result["run"]["budget_basis"] = (
        "docs/acceptance.md CPU row only (cpu_max_rtf); the 3090 rows do not apply to a CPU run"
        if is_cpu_run
        else "docs/acceptance.md GPU rows for precision=%s" % actual_precision
    )
    cold_max = budgets.get("cold_max_seconds")
    if result["run_cold"] and cold_max is not None and not is_cpu_run:
        seconds = result["run_cold"]["elapsed_seconds"]
        result["budget_checks"].append(
            {
                "name": "cold first shot",
                "basis": "rtf_http timing (HTTP round trip)",
                "budget_seconds": cold_max,
                "measured_seconds": seconds,
                "ok": seconds is not None and seconds <= cold_max,
            }
        )
    for condition in result["conditions"]:
        if condition["skipped"] or condition["warm_median_rtf_http"] is None:
            continue
        if is_cpu_run:
            cpu_max = budgets.get("cpu_max_rtf")
            if cpu_max is None:
                continue
            result["budget_checks"].append(
                {
                    "name": "%s/%d/%s (cpu)"
                    % (condition["case"], condition["steps"], condition["reference"]),
                    "basis": "rtf_http vs the CPU row of docs/acceptance.md (RTF <= 4.0)",
                    "budget_rtf": cpu_max,
                    "measured_rtf_http": condition["warm_median_rtf_http"],
                    "ok": condition["warm_median_rtf_http"] <= cpu_max,
                }
            )
            continue
        rule = budget_for(
            cases, condition["case"], condition["steps"], condition["reference"], actual_precision
        )
        if rule is None:
            continue
        result["budget_checks"].append(
            {
                "name": "%s/%d/%s/%s"
                % (condition["case"], condition["steps"], condition["reference"], actual_precision),
                "basis": "rtf_http vs a budget measured with rtf_synth - see rtf_definitions",
                "budget_rtf": rule["max_rtf"],
                "measured_rtf_http": condition["warm_median_rtf_http"],
                "kit_measured_rtf_synth": rule.get("measured_ref"),
                "ok": condition["warm_median_rtf_http"] <= rule["max_rtf"],
            }
        )
    vram_max = (budgets.get("vram_max_mib") or {}).get(actual_precision or "")
    if vram_max and peak_vram is not None and not is_cpu_run:
        result["budget_checks"].append(
            {
                "name": "VRAM peak (nvidia-smi memory.used)",
                "basis": "whole-process occupancy of GPU 0, not the torch allocator peak",
                "budget_mib": vram_max,
                "measured_mib": peak_vram,
                "ok": peak_vram <= vram_max,
            }
        )

    fired_shots = []
    for condition in result["conditions"]:
        fired_shots.extend(condition["shots"])
    if isinstance(result["preset_sweep"], dict):
        for entry in result["preset_sweep"].get("shots", []) or []:
            if not entry.get("skipped"):
                fired_shots.append(entry["shot"])
    failed = [s for s in fired_shots if not s["ok"]]
    add_check(
        "every fired shot answered 200 with a wav",
        not failed and bool(fired_shots),
        "%d shot(s) fired, %d failed" % (len(fired_shots), len(failed)),
    )
    result["shots_fired"] = len(fired_shots)
    result["shots_failed"] = len(failed)
    missed = [b for b in result["budget_checks"] if not b["ok"]]
    result["budgets_missed"] = len(missed)
    result["ok"] = bool(fired_shots) and not failed

    write_json(args.json_out, result)
    log(
        "wrote %s (%d shots, %d failed, %d budget miss(es))"
        % (args.json_out, len(fired_shots), len(failed), len(missed))
    )
    if not result["ok"]:
        return EXIT_OPERATIONAL
    if missed and args.fail_on_budget:
        return EXIT_BUDGET
    return EXIT_OK


def write_json(path: str, payload: dict) -> None:
    """Write the result JSON as pure ASCII, LF, no BOM.

    ensure_ascii=True is not cosmetic.  The reader on the RTX machine is Windows
    PowerShell 5.1 on a ja-JP install, where ``Get-Content -Raw`` decodes with the
    ANSI code page (CP932), not UTF-8.  Measured here on 2026-09-05: with
    ensure_ascii=False the Japanese voice id in ``conditions[].voice`` came back
    mangled and the mangling ATE THE CLOSING QUOTE, so the whole pipe died with
    ``ConvertFrom-Json : Invalid object passed in, ':' or '}' expected``.  With
    every non-ASCII character written as \\uXXXX the bytes are ASCII, so CP932,
    UTF-8 and UTF-16 all decode them to the same text and ConvertFrom-Json is
    happy on both PowerShell 5.1 and 7.  A human reader loses nothing: PowerShell
    and Python both turn \\uXXXX back into the character on parse.
    """
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(payload, ensure_ascii=True, indent=2) + "\n"
    temporary = target.with_suffix(target.suffix + ".tmp")
    temporary.write_text(text, encoding="ascii", newline="\n")
    os.replace(temporary, target)


# --------------------------------------------------------------------------
# post-kill
# --------------------------------------------------------------------------


def run_post_kill(args: argparse.Namespace) -> int:
    wait = float(args.wait_seconds)
    log("post-kill: sleeping %.1f s before reading VRAM" % wait)
    before = vram_used_mib()
    time.sleep(wait)
    after = vram_used_mib()
    block = {
        "measured_at": utcnow(),
        "wait_seconds": wait,
        "immediately_after_kill_mib": before,
        "after_wait_mib": after,
        "note": (
            "docs/acceptance.md VRAM row: the card must be back at its idle value "
            "within 10 s of the kill. Compare after_wait_mib with "
            "vram.idle_before_start_mib. null on a machine without nvidia-smi."
        ),
    }
    target = Path(args.json_out)
    if not target.is_file():
        log("post-kill: %s does not exist; writing a standalone file" % target)
        write_json(args.json_out, {"schema": SCHEMA, "tool": TOOL, "vram": {"after_kill": block}})
        return EXIT_OK
    payload = json.loads(target.read_text(encoding="utf-8"))
    payload.setdefault("vram", {})["after_kill"] = block
    write_json(args.json_out, payload)
    log(
        "post-kill: immediately=%s MiB, after %.1f s=%s MiB, idle before start=%s MiB"
        % (before, wait, after, payload.get("vram", {}).get("idle_before_start_mib"))
    )
    return EXIT_OK


# --------------------------------------------------------------------------
# cli
# --------------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="cuda_bench.py",
        description="Bench the ywk wrapper over HTTP and write one machine-made JSON.",
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=18090)
    parser.add_argument("--cases", default="", help="path to probe/bench_cases.json")
    parser.add_argument("--json-out", default="", help="where to write the result JSON")
    parser.add_argument("--wav-dir", default="", help="save the first wav of each condition here")
    parser.add_argument("--max-wavs", type=int, default=6)
    parser.add_argument("--voices-dir", default="", help="the staged IRODORI_VOICES_DIR")
    parser.add_argument("--reference-preset", default="", help="preset id used by the ref10/ref30 axis")
    parser.add_argument("--warm-reps", type=int, default=3)
    parser.add_argument("--ready-timeout", type=int, default=600)
    parser.add_argument("--shot-timeout", type=float, default=1800.0)
    parser.add_argument("--label", default="")
    parser.add_argument("--variant", default="")
    parser.add_argument("--device", default="")
    parser.add_argument("--precision", default="")
    parser.add_argument("--idle-vram-mib", type=int, default=None)
    parser.add_argument("--quick", action="store_true", help="one condition, one warm shot, no sweep")
    parser.add_argument("--no-preset-sweep", action="store_true")
    parser.add_argument("--fail-on-budget", action="store_true")
    parser.add_argument("--trim-wav", default="")
    parser.add_argument("--trim-out", default="")
    parser.add_argument("--trim-seconds", type=float, default=10.0)
    parser.add_argument("--post-kill", action="store_true")
    parser.add_argument("--wait-seconds", type=float, default=10.0)
    return parser


def main(argv: list[str] | None = None) -> int:
    _make_console_utf8()
    args = build_parser().parse_args(argv)

    if args.trim_wav:
        if not args.trim_out:
            raise SystemExit("--trim-wav needs --trim-out")
        report = trim_wav(Path(args.trim_wav), Path(args.trim_out), float(args.trim_seconds))
        # ASCII for the same reason write_json is ASCII: cuda-bench.ps1 captures this
        # line and logs it, and a CP932 console must not mangle a path.
        print(json.dumps(report, ensure_ascii=True), flush=True)
        return EXIT_OK

    if args.post_kill:
        if not args.json_out:
            raise SystemExit("--post-kill needs --json-out")
        return run_post_kill(args)

    if not args.cases:
        raise SystemExit("--cases is required")
    if not args.json_out:
        raise SystemExit("--json-out is required")
    if not args.reference_preset:
        cases = json.loads(Path(args.cases).read_text(encoding="utf-8"))
        args.reference_preset = cases["reference_voices"]["primary_preset_id"]
    return run_bench(args)


if __name__ == "__main__":
    sys.exit(main())
