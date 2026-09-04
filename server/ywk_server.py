"""ywk_server -- the distribution's wrapper around the upstream Irodori-TTS server.

One file.  It imports the upstream FastAPI app unmodified and adds what the
本体 (yomiwakechan2) needs but upstream does not have:

* ``GET /params``          -- the machine-readable parameter table (design §4-2)
* ``GET /ywk/status``      -- version, real device, voices, warmup (§4-3)
* ``POST``/``DELETE /ywk/warmup`` -- the 便 C warmup: one background thread fires
  the upstream synthesis function itself, no_ref ``seconds`` stages first and
  then one short shot per speaker, yielding to every real request (便 C 設計書
  §2-3).  Radeon needs it -- an unseen output/reference shape costs seconds the
  first time MIOpen meets it (research/lab/notes/40).
* ``GET /v1/audio/voices`` -- replaced: absolute paths dropped, 「デフォルト」
  always present and first, ``display_name`` / ``preset`` added, and a broken
  ``voices.json`` answers "0 件＋理由" instead of 500 (§4-4, contract ⑼ D-8).
  ``GET /ywk/voices`` too.
* ``POST /v1/audio/speech``-- replaced: the upstream's no-reference voice
  aliases are normalised to 「デフォルト」, three fields whose explicit ``null``
  upstream reads as "specified" are folded away, and whitelist + range +
  Literal-nesting + ``voice``/reference exclusivity checks run *before* the
  upstream handler, which is then called unchanged (§4-4, §4-5).
* ``POST``/``PUT``/``DELETE /v1/audio/voices`` -- dropped.  The distribution owns
  ``voices/`` (contract ⑷ 4-3) and the port has no api_key.
* precision follows the device (§4-6), and a bad device string exits 2 before
  the six-to-eleven second model load instead of after it.
* an error-shaping middleware so no error body carries an absolute path (§4-7),
  including the SSE ``event: error`` frames, which ride inside a 200 and would
  otherwise slip past it.

The upstream packages ``irodori_openai_tts`` and ``irodori_tts`` are never
edited; the only source change the distribution makes lives in ``patches/`` and
is applied to the *copy* under ``build/out/``.

Run it with::

    python -m ywk_server --host 127.0.0.1 --port 18088
"""

from __future__ import annotations

import argparse
import asyncio
import concurrent.futures
import json
import logging
import os
import re
import sys
import threading
import time
import uuid
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

YWK_VERSION = "0.1.0"
UPSTREAM_IRODORI_TTS = "8224daf"
UPSTREAM_SERVER = "841fb7c"

logger = logging.getLogger("ywk_server")

# --------------------------------------------------------------------------
# 1. env defaults -- MUST run before the upstream package is imported.
#    ``irodori_openai_tts.config.get_settings`` is ``lru_cache``'d and
#    ``app.py`` calls it at module import time, so anything set afterwards is
#    ignored.  ``setdefault`` throughout: the launcher's env always wins.
# --------------------------------------------------------------------------


def _data_dir() -> Path:
    """Per-user data root (first-run downloads land here; see design §2)."""
    override = os.environ.get("YWK_DATA_DIR")
    if override:
        return Path(override)
    local = os.environ.get("LOCALAPPDATA")
    if local:
        return Path(local) / "irodori-tts-ywk"
    return Path.home() / ".irodori-tts-ywk"


def apply_env_defaults() -> None:
    # ``ywk_params`` is pure stdlib (no upstream import), so reading the one
    # constant here cannot freeze ``get_settings`` early.
    from ywk_params import DEFAULT_VOICE_ID as _DEFAULT_VOICE_ID  # noqa: PLC0415

    data = _data_dir()
    voices = data / "voices"
    models = data / "models"

    defaults = {
        "IRODORI_HOST": "127.0.0.1",
        "IRODORI_PORT": "18088",
        "IRODORI_HF_CHECKPOINT": "Aratako/Irodori-TTS-v4.1-Small",
        "IRODORI_PRELOAD": "true",
        "IRODORI_EMPTY_CACHE_INTERVAL": "0",
        "IRODORI_ALLOW_NO_REF_VOICE": "false",
        # decisions.md 45.  ``/params`` reports ``request.voice`` with
        # ``default: "デフォルト"`` (design §4-2 ⑵ forbids a null default on an
        # exposed field), so a request that omits ``voice`` must not become the
        # upstream's 400 ("No voice was provided and IRODORI_DEFAULT_VOICE is
        # not set." -- voices.py:75-79).  Baking the same id here makes the
        # advertised default true; ``resolve_default_voice`` then folds it into
        # the reference-free request the alias stands for when ``voices.json``
        # cannot resolve it.  ``setdefault``: the launcher can still override.
        "IRODORI_DEFAULT_VOICE": _DEFAULT_VOICE_ID,
        "IRODORI_DEFAULT_NUM_STEPS": "40",
        "IRODORI_DEFAULT_RESPONSE_FORMAT": "wav",
        "IRODORI_VOICES_DIR": str(voices),
        "IRODORI_VOICE_ALIASES_FILE": str(voices / "voices.json"),
        "IRODORI_MODEL_DEVICE": "auto",
        "IRODORI_CODEC_DEVICE": "auto",
        "HF_HOME": str(models),
        # These four only bite a *child* interpreter; the launcher and the
        # ._pth set them for this one.  Kept here so a hand-started
        # ``python -m ywk_server`` still hands them down.
        "PYTHONUTF8": "1",
        "PYTHONDONTWRITEBYTECODE": "1",
        "PYTHONUNBUFFERED": "1",
    }
    for key, value in defaults.items():
        os.environ.setdefault(key, value)


_DEVICE_RE = re.compile(r"^(cpu|auto|cuda(:\d+)?)$")


def _die(message: str, code: int = 2) -> None:
    sys.stderr.write(f"ywk_server: {message}\n")
    sys.stderr.flush()
    raise SystemExit(code)


#: What the launcher built this runtime from: ``cuda`` (the default, decisions.md
#: 4), ``cpu``, or ``rocm-<arch>`` -- ``rocm-gfx1151`` is the one variant the
#: distribution has a machine for (decisions.md 5).  Read as a *name*, never as a
#: capability: what actually loaded is ``/ywk/status.device.actual``.
DEFAULT_VARIANT = "cuda"

_FP32_SPELLINGS = frozenset({"fp32", "float32", "f32"})


def current_variant() -> str:
    value = str(os.environ.get("YWK_VARIANT", "") or "").strip()
    return value or DEFAULT_VARIANT


def variant_is_rocm(variant: str | None = None) -> bool:
    name = current_variant() if variant is None else str(variant)
    return name.strip().lower().startswith("rocm")


def apply_variant_defaults() -> dict[str, str]:
    """The Radeon variant's two rules.  Runs **before** the upstream import.

    ⑴ **bf16 is fixed** (decisions.md 5・36).  fp32 on this chip is not merely
    slower: the same shot measured 32.1 s against bf16's 2.84 s, because MIOpen
    falls back to a zero-workspace solver whose decode is slower than the CPU's
    (``research/lab/notes/40-rocm-warmup.md`` §8).  Rather than serve that, the
    wrapper exits 2 with one line -- *before* ``irodori_openai_tts`` is imported,
    i.e. before the model load, exactly like the device check below.
    ``IRODORI_MODEL_DEVICE=cpu`` is still allowed and still means fp32: the
    upstream rejects bf16 on cpu outright (inference_runtime.py:304-312).

    ⑵ **MIOpen's two databases live under ``YWK_DATA_DIR``** so the first-run
    penalty is paid once per machine instead of once per process
    (``40-rocm-warmup.md`` §5: a cold db costs 11.2 s on a shot that costs 2.5 s
    warm, and the effect survives process restarts).  The names were read out of
    ``MIOpen.dll`` itself (§5-2), not guessed.  ``setdefault``: the launcher wins.

    Returns the variant and the two paths, for the banner and the tests.
    """
    variant = current_variant()
    if not variant_is_rocm(variant):
        return {"variant": variant}

    for role, device_env, precision_env in (
        ("model", "IRODORI_MODEL_DEVICE", "IRODORI_MODEL_PRECISION"),
        ("codec", "IRODORI_CODEC_DEVICE", "IRODORI_CODEC_PRECISION"),
    ):
        device = str(os.environ.get(device_env, "auto")).strip().lower()
        precision = str(os.environ.get(precision_env, "")).strip().lower()
        if precision in _FP32_SPELLINGS and device != "cpu":
            _die(
                f"{precision_env}={precision} is not supported on variant "
                f"{variant}: the Radeon build is fixed to bf16 (decisions.md 5/36; "
                f"fp32 measured 32.1s against bf16 2.84s on gfx1151). "
                f"Unset {precision_env} or set {device_env}=cpu."
            )

    miopen = _data_dir() / "miopen"
    paths = {"MIOPEN_USER_DB_PATH": miopen / "db", "MIOPEN_CUSTOM_CACHE_DIR": miopen / "cache"}
    for name, path in paths.items():
        os.environ.setdefault(name, str(path))
        try:
            Path(os.environ[name]).mkdir(parents=True, exist_ok=True)
        except OSError as exc:  # noqa: PERF203 -- diagnostics only; MIOpen falls back
            sys.stderr.write(f"ywk_server: could not create {name}: {exc}\n")

    return {
        "variant": variant,
        "miopen_db": os.environ["MIOPEN_USER_DB_PATH"],
        "miopen_cache": os.environ["MIOPEN_CUSTOM_CACHE_DIR"],
    }


def resolve_devices_and_precision() -> dict[str, str]:
    """Validate the device env and fold precision into it (design §4-6).

    * ``IRODORI_MODEL_DEVICE`` / ``IRODORI_CODEC_DEVICE`` must match
      ``^(cpu|auto|cuda(:\\d+)?)$``.
    * ``cuda`` with ``torch.cuda.is_available()`` false exits 2 *here* rather
      than letting the upstream loader fail six to eleven seconds in -- and it
      stops the silent fp32-on-CPU fallback the survey warned about.
    * ``cuda:N`` outside ``torch.cuda.device_count()`` exits 2 the same way.
    * ``IRODORI_MODEL_PRECISION`` / ``IRODORI_CODEC_PRECISION`` are set only
      when unset: ``bf16`` on cuda, ``fp32`` on cpu.  ``resolve_runtime_dtype``
      rejects bf16 on cpu outright (inference_runtime.py:304-312), so guessing
      wrong is a hard failure, not a slow one.
    """
    import torch  # noqa: PLC0415 -- deliberately after the env defaults

    cuda_available = bool(torch.cuda.is_available())
    device_count = torch.cuda.device_count() if cuda_available else 0

    effective: dict[str, str] = {}
    for role, env_name in (("model", "IRODORI_MODEL_DEVICE"), ("codec", "IRODORI_CODEC_DEVICE")):
        raw = str(os.environ.get(env_name, "auto")).strip().lower()
        if _DEVICE_RE.fullmatch(raw) is None:
            _die(
                f"{env_name}={raw!r} is not supported. Use cpu, auto, cuda or cuda:<index>."
            )
        if raw == "auto":
            resolved = "cuda" if cuda_available else "cpu"
        else:
            resolved = raw
        if resolved.startswith("cuda"):
            if not cuda_available:
                _die(
                    f"{env_name}={raw!r} but torch.cuda.is_available() is False "
                    f"(torch {torch.__version__}). Install the CUDA runtime variant "
                    f"or set {env_name}=cpu."
                )
            if ":" in resolved:
                index = int(resolved.split(":", 1)[1])
                if index >= device_count:
                    _die(
                        f"{env_name}={raw!r} but torch.cuda.device_count() is "
                        f"{device_count}. Valid indices are 0..{device_count - 1}."
                    )
        effective[role] = resolved

    for role, env_name in (
        ("model", "IRODORI_MODEL_PRECISION"),
        ("codec", "IRODORI_CODEC_PRECISION"),
    ):
        want = "bf16" if effective[role].startswith("cuda") else "fp32"
        os.environ.setdefault(env_name, want)

    _warn_if_rocm_torch_is_untagged(torch)
    return effective


def _warn_if_rocm_torch_is_untagged(torch: Any) -> str | None:
    """``YWK_VARIANT`` is a label, not a capability -- say so when they disagree.

    ``apply_variant_defaults`` keys both Radeon guards off that label, so a
    Radeon machine started **without** it (the default is ``cuda``) silently
    loses ⑴ the fp32 exit 2 -- the 32.1 s/shot setting decisions.md 5/36 forbade
    goes straight through -- and ⑵ the MIOpen redirection, whose db then lands
    in ``~/.miopen`` instead of ``YWK_DATA_DIR`` (C-7, and the uninstaller's
    sweep misses it).  Both were measured on this machine by the 是正席.

    This runs *after* torch is imported, which is too late to fix either: the
    MIOpen env has to be in place before that import, and the precision has
    already been folded in.  So it warns rather than repairs -- and it leaves
    ``/ywk/status.variant`` alone, which contract ⑹ says echoes the env
    verbatim.  Reporting and behaviour stay separate.
    """
    hip = getattr(getattr(torch, "version", None), "hip", None)
    if not hip or variant_is_rocm():
        return None
    arch = ""
    try:
        if torch.cuda.is_available() and torch.cuda.device_count() > 0:
            arch = str(getattr(torch.cuda.get_device_properties(0), "gcnArchName", "") or "")
    except Exception:  # noqa: BLE001 -- a diagnostic line must never be the failure
        arch = ""
    line = (
        f"ywk_server: torch is a ROCm build (hip={hip}"
        f"{' gcn=' + arch if arch else ''}) but YWK_VARIANT={current_variant()}: "
        f"the bf16 guard (decisions.md 5/36) and the MIOpen db redirection are off. "
        f"Set YWK_VARIANT=rocm-<gfx> to turn them on."
    )
    sys.stderr.write(line + "\n")
    sys.stderr.flush()
    return line


apply_env_defaults()
VARIANT_ENV = apply_variant_defaults()
EFFECTIVE_DEVICES = resolve_devices_and_precision()

# --------------------------------------------------------------------------
# 2. upstream import (settings freeze here)
# --------------------------------------------------------------------------

from fastapi import Request  # noqa: E402
from fastapi.exceptions import RequestValidationError  # noqa: E402
from fastapi.responses import JSONResponse, Response, StreamingResponse  # noqa: E402
from fastapi.routing import APIRoute  # noqa: E402
from pydantic import ValidationError  # noqa: E402

from irodori_openai_tts import app as upstream  # noqa: E402

import ywk_params  # noqa: E402
from ywk_params import (  # noqa: E402
    DEFAULT_VOICE_ID,
    EMPTY_STRING_MEANS_UNSET,
    EXPLICIT_NULL_WINS,
    IRODORI_KEYS,
    LITERAL_NESTED_ONLY,
    NO_REF_VOICE_IDS,
    REFERENCE_KEYS,
    RESPONSE_FORMATS,
    TOP_LEVEL_KEYS,
)

app = upstream.app
settings = upstream.settings

#: Set when the wrapper itself saw the runtime fail to load.  The upstream
#: ``RuntimeManager`` keeps no error field, and a *preload* failure aborts the
#: lifespan (uvicorn exits), so this only ever records a lazy-load failure.
_runtime_error: str | None = None


# --------------------------------------------------------------------------
# 3. error shaping
# --------------------------------------------------------------------------

#: ``research/lab/notes/37-handoff-contract.md`` §2-5 item 4 asks for error
#: bodies of at most 512 bytes.  Cap the message in *bytes*, not characters:
#: 400 Japanese characters would be 1,200 bytes.
_MESSAGE_LIMIT_BYTES = 300

# ``C:\dir\file`` / ``C:/dir/file`` / ``C:\\dir\\file`` and the usual POSIX
# roots.  Three things this has to survive, all of them observed:
#
# * the upstream raises ``KeyError`` and the server stringifies it, so the
#   message reaches us as the *repr* of the argument -- every backslash
#   doubled.  Hence ``\\{1,2}`` and the doubled forms in ``_known_prefixes``.
# * a user directory can contain spaces (``C:\Users\Taro Yamada\...``), so a
#   path segment may not stop at whitespace: a ``[^\s]`` class leaks
#   ``Yamada`` while still passing a ``:\``/``:/`` grep.
# * ``_WIN_ABS`` must run *before* ``_UNC_ABS``: on ``C:\\Users\\x`` the UNC
#   pattern matches from the doubled separator onwards and leaves ``C:`` behind.
_WIN_SEG = r"[^\\/\n\"',]"
_WIN_ABS = re.compile(
    r"[A-Za-z]:(?:\\{1,2}|/)(?:" + _WIN_SEG + r"+(?:\\{1,2}|/))*" + _WIN_SEG + r"*"
)
_NIX_ABS = re.compile(r"(?<![\w.])/(?:home|Users|usr|opt|var|tmp|mnt|etc)(?:/[^\s\"']*)?")
_UNC_ABS = re.compile(r"\\{2,4}[^\s\"']+")


def _known_prefixes() -> list[str]:
    """Every directory we know the name of, in each spelling it can arrive in.

    A prefix reaches an error body three ways: as itself, with its separators
    doubled (``str()`` of an exception whose argument was repr'd), and with them
    turned into forward slashes.  All three are replaced, longest first, so that
    a nested prefix cannot leave a fragment of its parent behind.
    """
    roots: list[str] = []
    try:
        roots.append(str(upstream.settings.voices_dir.expanduser()))
    except Exception:  # noqa: BLE001 -- diagnostics must never raise
        pass
    roots.append(str(Path(__file__).resolve().parent))
    roots.append(sys.prefix)
    hf_home = os.environ.get("HF_HOME")
    if hf_home:
        roots.append(hf_home)

    out: list[str] = []
    for root in roots:
        if not root:
            continue
        for spelling in (root, root.replace("\\", "\\\\"), root.replace("\\", "/")):
            if spelling not in out:
                out.append(spelling)
    out.sort(key=len, reverse=True)
    return out


def replace_paths(text: str) -> str:
    """Replace every absolute path in ``text`` with ``<path>``.  No truncation."""
    for prefix in _known_prefixes():
        if prefix and prefix in text:
            text = text.replace(prefix, "<path>")
    text = _WIN_ABS.sub("<path>", text)
    text = _UNC_ABS.sub("<path>", text)
    text = _NIX_ABS.sub("<path>", text)
    return text


def scrub_text(text: str) -> str:
    """``replace_paths`` plus the 300-byte cap used for error messages."""
    text = replace_paths(text)
    encoded = text.encode("utf-8")
    if len(encoded) > _MESSAGE_LIMIT_BYTES:
        text = encoded[:_MESSAGE_LIMIT_BYTES].decode("utf-8", "ignore") + "…"
    return text


def _scrub(value: Any, *, cap: bool = True) -> Any:
    """Walk a JSON value and scrub every string in it.

    ``cap`` is the 300-byte message cap: right for an error body, wrong for a
    ``GET /health`` that is scrubbed only to hide the paths in it.
    """
    if isinstance(value, str):
        return scrub_text(value) if cap else replace_paths(value)
    if isinstance(value, list):
        return [_scrub(item, cap=cap) for item in value]
    if isinstance(value, dict):
        return {key: _scrub(item, cap=cap) for key, item in value.items()}
    return value


def ywk_error(
    message: str,
    *,
    status_code: int = 400,
    code: str,
    param: str | None = None,
    error_type: str = "invalid_request_error",
) -> JSONResponse:
    return JSONResponse(
        status_code=status_code,
        content={
            "error": {
                "message": scrub_text(message),
                "type": error_type,
                "param": param,
                "code": code,
            }
        },
    )


class YwkRequestError(Exception):
    def __init__(self, message: str, *, code: str, param: str | None = None) -> None:
        super().__init__(message)
        self.message = message
        self.code = code
        self.param = param


#: Paths whose **successful** body is scrubbed too.  ``GET /health`` is the
#: upstream's own route (contract ⑵ says the 本体 must not interpret its body),
#: and it hands out ``settings.voices_dir`` and the resolved Hugging Face
#: snapshot directory verbatim -- i.e. the operating-system user name -- from an
#: unauthenticated port.  The keys stay exactly as upstream wrote them; only the
#: absolute paths inside become ``<path>``.
_SCRUB_OK_PATHS = frozenset({"/health"})

#: ``message`` fragment -> machine-readable ``code``, for the errors the
#: upstream raises itself.  ``openai_error_response`` (``app.py:1156-1167``)
#: hard-codes ``"code": None``, but contract ⑶ 3-3 promises the 本体 a code on
#: *every* shape -- it is what lets ``IsMissingVoiceFailure`` stop matching on
#: prose (contract ⑼ D-5).
_UPSTREAM_CODES: tuple[tuple[str, str], ...] = (
    ("Unknown voice=", "ywk_unknown_voice"),
    ("No voice was provided", "ywk_missing_voice"),
    ("Unsupported model", "ywk_unknown_model"),
    ("input must contain", "ywk_empty_input"),
    ("voice_id must contain only", "ywk_invalid_voice_id"),
)

#: status code -> fallback ``code`` when the message says nothing we know.
_STATUS_CODES: dict[int, str] = {
    404: "ywk_not_found",
    405: "ywk_method_not_allowed",
    422: "ywk_validation_error",
    503: "ywk_runtime_unavailable",
}


def classify_error(status_code: int, message: str) -> str:
    """The ``code`` to put on an upstream error that carries none."""
    for needle, code in _UPSTREAM_CODES:
        if needle in message:
            return code
    known = _STATUS_CODES.get(int(status_code))
    if known is not None:
        return known
    if int(status_code) >= 500:
        return "ywk_server_error"
    return "ywk_upstream_error"


def _ensure_error_code(payload: Any, status_code: int) -> Any:
    """Give every error body the contract's shape, ``code`` included.

    Two sources need it.  The upstream's own handler writes the OpenAI shape but
    hard-codes ``"code": None``.  Starlette's router raises
    ``starlette.exceptions.HTTPException`` for 404/405, which the upstream's
    handler -- registered for the *FastAPI* subclass -- never sees, so those come
    out as a bare ``{"detail": "Not Found"}``.
    """
    if not isinstance(payload, dict):
        return payload
    if "error" not in payload and "detail" in payload:
        detail = payload["detail"]
        payload = {
            "error": {
                "message": detail if isinstance(detail, str) else json.dumps(detail, ensure_ascii=False),
                "type": "invalid_request_error" if int(status_code) < 500 else "server_error",
                "param": None,
                "code": None,
            }
        }
    error = payload.get("error")
    if not isinstance(error, dict):
        return payload
    if not error.get("code"):
        error["code"] = classify_error(status_code, str(error.get("message", "")))
    error.setdefault("param", None)
    return payload


@app.middleware("http")
async def ywk_scrub_errors(request: Request, call_next):  # noqa: ANN001, ANN201
    """Design §4-7: no error body may carry an absolute path (contract ⑶ 3-3).

    The same pass fills in ``error.code`` for the upstream's own 400/404/503,
    and scrubs the paths out of ``GET /health`` -- the one 200 that leaks them.
    """
    response = await call_next(request)
    is_error = response.status_code >= 400
    if not is_error and request.url.path not in _SCRUB_OK_PATHS:
        return response
    content_type = response.headers.get("content-type", "")
    if "json" not in content_type.lower():
        return response

    body = b"".join([chunk async for chunk in response.body_iterator])
    try:
        payload = json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return Response(
            content=body,
            status_code=response.status_code,
            headers=_carry_headers(response.headers),
            media_type=content_type,
        )
    payload = _scrub(payload, cap=is_error)
    if is_error:
        payload = _ensure_error_code(payload, response.status_code)
    scrubbed = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    return Response(
        content=scrubbed,
        status_code=response.status_code,
        headers=_carry_headers(response.headers),
        media_type=content_type,
    )


def _carry_headers(headers: Any) -> dict[str, str]:
    return {
        key: value
        for key, value in headers.items()
        if key.lower() not in {"content-length", "content-type"}
    }


@app.exception_handler(RequestValidationError)
async def ywk_validation_handler(  # noqa: ANN201
    _request: Request, exc: RequestValidationError
):
    """Design §4-5 ⑹: replace the upstream 422 that echoes the whole body.

    Upstream returns ``str(exc)`` -- for an over-long ``input`` that is the
    entire 5,000-character request plus the absolute path of ``app.py``
    (``research/lab/notes/37-handoff-contract.md`` §2-1).  We return field
    names and reasons only.
    """
    return ywk_error(
        _summarize_validation(exc.errors()),
        status_code=422,
        code="ywk_validation_error",
    )


@app.exception_handler(Exception)
async def ywk_unhandled_handler(_request: Request, exc: Exception):  # noqa: ANN201
    """Starlette installs the ``Exception`` handler *outside* the user
    middleware stack, so ``ywk_scrub_errors`` never sees its body.  Scrub here
    instead (design §4-7 -- "エラー body 全件").
    """
    return ywk_error(
        f"{type(exc).__name__}: {exc}",
        status_code=500,
        code="ywk_server_error",
        error_type="server_error",
    )


def _summarize_validation(errors: Any) -> str:
    parts: list[str] = []
    for error in list(errors)[:8]:
        location = ".".join(str(item) for item in error.get("loc", ()) if item != "body")
        parts.append(f"{location or '<body>'}: {error.get('type', 'invalid')}")
    if not parts:
        return "request body is invalid"
    return "invalid fields -- " + "; ".join(parts)


# --------------------------------------------------------------------------
# 4. whitelist / range / exclusivity checks (design §4-5)
# --------------------------------------------------------------------------


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _is_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def check_field(key: str, value: Any, *, nested: bool) -> None:
    """Type/enum/range check for one field.  ``None`` means "not given"."""
    spec = ywk_params.spec_for(key, nested=nested)
    if spec is None:  # pragma: no cover -- callers check membership first
        raise YwkRequestError(f"unknown field: {key}", code="ywk_unknown_field", param=key)
    if value is None:
        return

    param = f"irodori.{key}" if nested else key
    kind = spec["type"]

    if kind == "boolean":
        if not isinstance(value, bool):
            raise YwkRequestError(
                f"{param} must be a boolean", code="ywk_type_error", param=param
            )
        return

    if kind == "enum":
        allowed = list(spec["enum"])
        if not isinstance(value, str) or value not in allowed:
            raise YwkRequestError(
                f"{param} must be one of: {', '.join(allowed)}",
                code="ywk_invalid_enum",
                param=param,
            )
        return

    if kind == "string":
        if not isinstance(value, str):
            raise YwkRequestError(
                f"{param} must be a string", code="ywk_type_error", param=param
            )
        min_length = spec.get("min_length")
        if min_length is not None and len(value) < min_length:
            raise YwkRequestError(
                f"{param} must be at least {min_length} characters",
                code="ywk_out_of_range",
                param=param,
            )
        max_length = spec.get("max_length")
        if max_length is not None and len(value) > max_length:
            raise YwkRequestError(
                f"{param} must be at most {max_length} characters (got {len(value)})",
                code="ywk_out_of_range",
                param=param,
            )
        return

    if kind == "array":
        if not isinstance(value, list) or not value:
            raise YwkRequestError(
                f"{param} must be a non-empty array of strings",
                code="ywk_type_error",
                param=param,
            )
        for item in value:
            if not isinstance(item, str) or not item.strip():
                raise YwkRequestError(
                    f"{param} entries must be non-empty strings",
                    code="ywk_type_error",
                    param=param,
                )
        return

    if kind == "integer":
        if not _is_int(value):
            raise YwkRequestError(
                f"{param} must be an integer", code="ywk_type_error", param=param
            )
    elif kind == "number":
        if not _is_number(value):
            raise YwkRequestError(
                f"{param} must be a number", code="ywk_type_error", param=param
            )
    else:  # pragma: no cover -- the table only uses the kinds above
        return

    low = spec.get("min")
    high = spec.get("max")
    if low is not None and value < low:
        raise YwkRequestError(
            f"{param} must be >= {low} (got {value})", code="ywk_out_of_range", param=param
        )
    if high is not None and value > high:
        raise YwkRequestError(
            f"{param} must be <= {high} (got {value})", code="ywk_out_of_range", param=param
        )


def normalize_speech_body(body: Any) -> Any:
    """Fold the three things "unset" is spelled as, before anything is checked.

    ⑴ **Upstream's own no-reference spellings.**  ``voice`` in
    :data:`~ywk_params.NO_REF_VOICE_IDS` (``none`` / ``no_ref`` / ``no-ref`` /
    ``null`` / ``text-only``, case-insensitive) becomes 「デフォルト」.  The
    upstream only honours those when ``allow_no_ref_voice`` is true
    (``voices.py:80``) and the distribution bakes it false, so without this the
    本体's current adapter -- which sends ``voice:"none"`` (contract ⑼ D-4) --
    would 400 on every request.  Design §4-4 requires this path not to 400.

    ⑵ **Explicit ``null`` on the three ``_explicit_option`` fields.**
    ``app.py:1089-1095`` consults ``model_fields_set``, so ``{"ref_normalize_db":
    null}`` reads as "the caller asked for None" and beats the env default --
    silently turning off the reference loudness normalisation, i.e. changing the
    audio.  The other 41 fields go through ``_coalesce`` where ``null`` loses.
    Deleting the key is what makes ``/params.rules.null_means_unset`` true.

    ⑶ **The ``""`` defaults.**  ``/params`` reports ``caption`` and ``seed`` with
    ``default: ""`` (design §4-2 ⑵ forbids a ``null`` default on an exposed
    field), so ``""`` must mean the same as "omitted" -- upstream 400s an empty
    caption and would never accept an empty seed.

    Returns the same object, mutated; a non-dict body is handed straight back
    for :func:`validate_speech_body` to reject.
    """
    if not isinstance(body, dict):
        return body

    voice = body.get("voice")
    if isinstance(voice, str) and voice.strip().lower() in NO_REF_VOICE_IDS:
        body["voice"] = DEFAULT_VOICE_ID

    options = body.get("irodori")
    if not isinstance(options, dict):
        options = None

    for key in EXPLICIT_NULL_WINS:
        if options is not None and key in options and options[key] is None:
            del options[key]
        if key in body and body[key] is None:
            del body[key]

    for key in EMPTY_STRING_MEANS_UNSET:
        for holder in (options, body):
            if holder is None:
                continue
            value = holder.get(key)
            if isinstance(value, str) and value.strip() == "":
                del holder[key]
    return body


def validate_speech_body(body: Any) -> None:
    """Everything the upstream does not check.  Raises :class:`YwkRequestError`."""
    if not isinstance(body, dict):
        raise YwkRequestError("request body must be a JSON object", code="ywk_invalid_body")

    # (1) unknown top-level fields
    for key in body:
        if key not in TOP_LEVEL_KEYS:
            if key in LITERAL_NESTED_ONLY:
                # (3) Literal fields sent flat are passed through unchecked by
                # the upstream (survey S-2); say so instead of "unknown".
                raise YwkRequestError(
                    f"{key} must be sent inside the irodori object",
                    code="ywk_literal_top_level",
                    param=key,
                )
            raise YwkRequestError(f"unknown field: {key}", code="ywk_unknown_field", param=key)

    options = body.get("irodori")
    if options is None:
        options = {}
    if not isinstance(options, dict):
        raise YwkRequestError("irodori must be a JSON object", code="ywk_type_error", param="irodori")

    # (1) unknown nested fields
    for key in options:
        if key not in IRODORI_KEYS:
            raise YwkRequestError(
                f"unknown field: irodori.{key}", code="ywk_unknown_field", param=f"irodori.{key}"
            )

    # (5) wav only -- the distribution ships no ffmpeg
    response_format = body.get("response_format")
    if response_format is not None and response_format not in RESPONSE_FORMATS:
        raise YwkRequestError(
            f"response_format must be one of: {', '.join(RESPONSE_FORMATS)}",
            code="ywk_unsupported_response_format",
            param="response_format",
        )

    # (4) voice is exclusive with every reference field.
    #
    # ``_resolve_voice`` (app.py:437-454) never even looks at ``voice`` once any
    # one of ref_wav / ref_wavs / ref_latent / ref_latents / ref_embed / no_ref
    # is present: the caller gets a 200 spoken by somebody else.  ``no_ref``
    # keeps its own code because the 本体's adapter branches on it (contract
    # ⑼ D-4); the five path fields share ``ywk_voice_and_reference``.
    voice = body.get("voice")
    has_voice = voice is not None and not (isinstance(voice, str) and voice.strip() == "")
    no_ref = options.get("no_ref")
    if no_ref is None:
        no_ref = body.get("no_ref")
    if has_voice and no_ref is not None:
        raise YwkRequestError(
            "voice and irodori.no_ref cannot be combined; "
            f"use voice=\"{DEFAULT_VOICE_ID}\" for reference-free synthesis",
            code="ywk_voice_and_no_ref",
            param="voice",
        )
    if has_voice:
        for key in REFERENCE_KEYS:
            given = options.get(key)
            where = f"irodori.{key}"
            if given is None:
                given = body.get(key)
                where = key
            if given is None:
                continue
            raise YwkRequestError(
                f"voice and {where} cannot be combined; the upstream would drop "
                "the voice silently. Send one or the other.",
                code="ywk_voice_and_reference",
                param=where,
            )

    # (2) types and ranges, both paths
    for key, value in body.items():
        if key == "irodori":
            continue
        check_field(key, value, nested=False)
    for key, value in options.items():
        check_field(key, value, nested=True)


# --------------------------------------------------------------------------
# 5. route replacement
# --------------------------------------------------------------------------


def _take_route(path: str, method: str):  # noqa: ANN202
    """Detach an upstream route and hand back its endpoint function."""
    for route in list(app.router.routes):
        if isinstance(route, APIRoute) and route.path == path and method in route.methods:
            app.router.routes.remove(route)
            return route.endpoint
    raise RuntimeError(f"upstream route {method} {path} was not found")


# The upstream GET is dropped outright: every element of its response carries
# the absolute path of the reference file (design §4-4).  The upstream POST is
# kept and called from ``ywk_create_speech`` once the body has been checked.
_take_route("/v1/audio/voices", "GET")
_upstream_create_speech = _take_route("/v1/audio/speech", "POST")

# The three write ports go too.  ``voices/`` and ``voices.json`` are owned by the
# distribution (contract ⑷ 4-3, ⑼ D-3) and the 本体 never calls them, but the
# server carries no api_key (decisions.md 31) -- so leaving POST/PUT/DELETE
# registered means any page the user has open can write a file into the speaker
# directory (multipart/form-data is a CORS "simple request": a no-cors POST
# arrives without a preflight).  Dropping them costs the distribution nothing
# and closes the only writable surface on the port.
for _method, _path in (
    ("POST", "/v1/audio/voices"),
    ("PUT", "/v1/audio/voices/{voice_id}"),
    ("DELETE", "/v1/audio/voices/{voice_id}"),
):
    _take_route(_path, _method)


def _voice_meta() -> dict[str, dict[str, Any]]:
    """``voices/voices.ywk.json`` -- the distribution's own speaker ledger.

    Owned by the launcher (便 D); ``build/assemble-app.ps1`` lays down the empty
    template.  Two shapes are accepted, because the file is written by another
    seat and the wrapper only ever reads it:

    * ``{"schema": 1, "voices": {"<id>": {...}}}`` -- what
      ``build/assemble-app.ps1`` writes today (it also carries a ``note``).
    * ``{"<id>": {...}}`` -- the flat form.

    Missing, unreadable or malformed is not an error: every field it supplies
    has a fallback (``display_name`` -> the id, ``preset`` -> false).
    """
    try:
        path = upstream.settings.voices_dir.expanduser() / "voices.ywk.json"
        if not path.is_file():
            return {}
        with path.open("r", encoding="utf-8") as handle:
            payload = json.load(handle)
    except (OSError, ValueError):
        return {}
    if not isinstance(payload, dict):
        return {}
    nested = payload.get("voices")
    if isinstance(nested, dict):
        payload = nested
    return {str(key): value for key, value in payload.items() if isinstance(value, dict)}


def _default_voice_entry() -> dict[str, Any]:
    return {
        "id": DEFAULT_VOICE_ID,
        "object": "voice",
        "display_name": DEFAULT_VOICE_ID,
        "preset": False,
        "no_ref": True,
    }


def _voice_list_with_reason() -> tuple[list[dict[str, Any]], str | None]:
    """The speaker list, plus why it is short when the ledger cannot be read.

    Two promises live here that the upstream does not make (contract ⑷, ⑵):

    * **「デフォルト」 is always present and first.**  The upstream only knows it
      because ``voices.json`` says so, and that file is written by the launcher
      (便 D) -- it does not exist on a first run.  The wrapper injects it rather
      than depending on another seat's file.
    * **A broken ``voices.json`` is not a 500.**  ``_load_aliases``
      (``voices.py:196-201``) raises ``ValueError`` with the absolute path in it,
      and the 本体's discovery step would see a 500 with no way forward.
      Contract ⑼ says D-8 disappears in the distribution: 0 件＋理由.
    """
    meta = _voice_meta()
    error: str | None = None
    try:
        specs = list(upstream.voice_registry.list())
    except (OSError, ValueError):
        specs = []
        error = "voices.json を読めなかった（一覧は「デフォルト」だけ・檔を直すか消すと戻る）"

    items: list[dict[str, Any]] = []
    for voice in specs:
        entry = meta.get(voice.voice_id, {})
        items.append(
            {
                "id": voice.voice_id,
                "object": "voice",
                # No ref_wav / ref_wavs / ref_latent / ref_latents / ref_embed:
                # the upstream returns absolute paths there (design §4-4).
                "display_name": str(entry.get("display_name") or voice.voice_id),
                "preset": bool(entry.get("preset", False)),
                "no_ref": bool(voice.no_ref),
            }
        )
    if not any(item["id"] == DEFAULT_VOICE_ID for item in items):
        items.append(_default_voice_entry())
    items.sort(key=lambda item: (item["id"] != DEFAULT_VOICE_ID,))
    return items, error


def _voice_list() -> list[dict[str, Any]]:
    return _voice_list_with_reason()[0]


def _registry_knows(voice_id: str) -> bool:
    """Can the upstream registry resolve this id on its own?"""
    try:
        upstream.voice_registry.resolve(voice_id)
    except Exception:  # noqa: BLE001 -- KeyError, ValueError, OSError all mean "no"
        return False
    return True


@app.get("/v1/audio/voices")
def ywk_list_voices() -> dict[str, Any]:
    """Upstream-shaped list with the absolute paths removed (design §4-4)."""
    return {"object": "list", "data": _voice_list()}


@app.get("/ywk/voices")
def ywk_voices() -> dict[str, Any]:
    data, error = _voice_list_with_reason()
    return {
        "object": "list",
        "data": data,
        "default_voice": upstream.settings.default_voice,
        "no_ref_voice": DEFAULT_VOICE_ID,
        "count": len(data),
        "error": error,
    }


def _carries_a_reference(body: dict[str, Any]) -> bool:
    """True when the body already names a reference (or asks for ``no_ref``).

    ``_resolve_voice`` (app.py:439-446) short-circuits on any of the six, so a
    body that carries one must be left exactly as it is -- **but the six are not
    tested alike**.  The five path fields short-circuit when they are *not None*;
    ``no_ref`` is the last term of the same ``or`` chain and is read for its
    **truth value** (``or explicit_no_ref``).  ``no_ref: false`` therefore does
    *not* short-circuit upstream: it falls through to
    ``voice_registry.resolve(None)`` -> ``settings.default_voice``.  Reading it
    as "a reference is present" (便 A's first cut) made the wrapper hand such a
    body on untouched, so on a first run -- ``voices.json`` not yet written by
    the launcher -- 「デフォルト」 could not be resolved and the caller got a 400
    where an omitted ``no_ref`` got a 200.  Same body, same meaning, two answers.
    """
    options = body.get("irodori")
    if not isinstance(options, dict):
        options = {}
    for key in REFERENCE_KEYS:
        if options.get(key) is not None or body.get(key) is not None:
            return True
    return bool(options.get("no_ref")) or bool(body.get("no_ref"))


def resolve_default_voice(body: dict[str, Any]) -> dict[str, Any]:
    """Make 「デフォルト」 work even when ``voices.json`` does not.

    ``voice: "デフォルト"`` is the contract's one guaranteed speaker (⑷ 4-2), but
    the upstream can only resolve it through ``voices.json``, which the launcher
    writes -- so on a first run, or with that file broken, the guaranteed
    speaker would 400.  When the registry cannot resolve it, the field is
    rewritten into the reference-free request the alias stands for.  Runs after
    the exclusivity check, so ``voice`` + ``no_ref`` is still a 400.

    An **omitted** ``voice`` takes the same road (decisions.md 45).  Upstream
    falls back to ``settings.default_voice`` (voices.py:75-79) and the wrapper
    bakes that to 「デフォルト」, so the omission has to end in the same 200 the
    explicit spelling gets -- otherwise ``/params.request.voice.default`` would
    be advertising a value that 400s.  A body that already carries a reference
    is handed on untouched: upstream ignores ``voice`` entirely in that case.
    """
    voice = body.get("voice")
    if voice is None or (isinstance(voice, str) and voice.strip() == ""):
        if _carries_a_reference(body):
            return body
        baked = str(getattr(upstream.settings, "default_voice", None) or "").strip()
        if baked != DEFAULT_VOICE_ID:
            # No baked default (the launcher emptied it), or one that names a
            # real voice file: let the upstream resolve it and report its own
            # 400 as ``ywk_missing_voice`` / ``ywk_unknown_voice``.
            return body
        voice = DEFAULT_VOICE_ID
        body["voice"] = DEFAULT_VOICE_ID
    if voice != DEFAULT_VOICE_ID or _registry_knows(DEFAULT_VOICE_ID):
        return body
    body.pop("voice", None)
    options = body.get("irodori")
    if not isinstance(options, dict):
        options = {}
        body["irodori"] = options
    options["no_ref"] = True
    return body


@app.post("/v1/audio/speech")
async def ywk_create_speech(request: Request):  # noqa: ANN201
    """Check the body, then hand it to the upstream handler unchanged (§4-5).

    The whole handler runs inside ``_real_request_in_flight``: that counter is
    what the warmup thread waits on, so a real request never queues behind more
    than the one warmup shot already running (便 C 設計書 §2-3, C-5).

    **The SSE path has to hold the counter past the handler.**  Returning a
    ``StreamingResponse`` is not the end of the request: the upstream generator
    takes ``_acquire_synthesis_slot`` again *per chunk* (``app.py:711``), so a
    count that drops when the handler returns leaves every gap between chunks
    open -- the 是正席 measured one real SSE request being overtaken by all
    three warmup shots, not by one.  The counter is therefore released by the
    body iterator's ``finally`` instead (client disconnect closes the generator,
    so that path releases too).  Non-SSE still releases at the handler's exit.
    """
    tracker = _real_request_in_flight()
    tracker.__enter__()
    try:
        response = await _create_speech(request)
    except BaseException:
        tracker.release()
        raise
    if isinstance(response, StreamingResponse):
        response.body_iterator = _hold_until_drained(response.body_iterator, tracker)
        return response
    tracker.release()
    return response


async def _hold_until_drained(inner: Any, tracker: Any) -> Any:
    """Pass the stream through, and drop the in-flight count when it ends."""
    try:
        async for chunk in inner:
            yield chunk
    finally:
        tracker.release()


async def _create_speech(request: Request):  # noqa: ANN202
    global _runtime_error

    raw = await request.body()
    try:
        body = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        return ywk_error(f"request body is not valid JSON: {exc}", code="ywk_invalid_body")

    body = normalize_speech_body(body)
    try:
        validate_speech_body(body)
    except YwkRequestError as exc:
        return ywk_error(exc.message, code=exc.code, param=exc.param)
    body = resolve_default_voice(body)

    try:
        payload = upstream.SpeechRequest(**body)
    except ValidationError as exc:
        return ywk_error(
            _summarize_validation(exc.errors()), code="ywk_validation_error"
        )
    except TypeError as exc:
        return ywk_error(f"request body is invalid: {exc}", code="ywk_invalid_body")

    try:
        response = await _upstream_create_speech(payload)
    except Exception as exc:  # noqa: BLE001 -- record, then re-raise
        if int(getattr(exc, "status_code", 500)) >= 500:
            _runtime_error = f"{type(exc).__name__}: {exc}"
        raise
    if upstream.runtime_manager.is_loaded:
        _runtime_error = None
        note_device_once()
    return _scrub_sse(response)


def _scrub_sse(response: Any) -> Any:
    """Scrub the SSE ``event: error`` frames too (design §4-7 + survey §2-5).

    An SSE stream is a **200**, so ``ywk_scrub_errors`` never looks at it, yet
    the error frames the upstream yields carry the same ``str(exc)`` -- and
    therefore the same absolute paths -- as the non-streaming 400/500.  Scrub
    per frame rather than buffering: the audio frames are left untouched, so
    streaming still streams, and no truncation is applied (an audio frame is
    base64 and must survive whole).
    """
    if not isinstance(response, StreamingResponse):
        return response
    inner = response.body_iterator

    async def scrubbed() -> Any:
        async for chunk in inner:
            text = chunk.decode("utf-8", "replace") if isinstance(chunk, bytes) else str(chunk)
            if text.startswith("event: error"):
                text = replace_paths(text)
            yield text.encode("utf-8")

    response.body_iterator = scrubbed()
    return response


# --------------------------------------------------------------------------
# 6. /params and /ywk/status
# --------------------------------------------------------------------------


def _live_runtime() -> Any:
    """The loaded ``InferenceRuntime``, or ``None``.  Never triggers a load."""
    manager = upstream.runtime_manager
    if not getattr(manager, "is_loaded", False):
        return None
    return getattr(manager, "_runtime", None) or getattr(manager, "runtime", None)


_device_logged = False


def note_device_once() -> str | None:
    """Design §4-8: one line naming the *real* device once the model is in.

    ``runtime loaded in %.2fs`` (upstream) says a model loaded; it does not say
    where.  This line is what tells a Radeon or a two-GPU machine apart from a
    silent CPU fallback, so the launcher can show it and the log can carry it.
    """
    global _device_logged
    if _device_logged:
        return None
    runtime = _live_runtime()
    if runtime is None:
        return None
    _device_logged = True
    device_obj = getattr(runtime, "model_device", None)
    model = getattr(runtime, "model", None)
    dtype = None
    if model is not None:
        try:
            parameter = next(model.parameters())
            device_obj = parameter.device
            dtype = str(parameter.dtype).replace("torch.", "")
        except (StopIteration, AttributeError):
            pass
    info = _cuda_device_info(device_obj)
    line = (
        f"ywk_server: device actual={device_obj} dtype={dtype} "
        f"name={info['name']} uuid={info['uuid']} pci_bus_id={info['pci_bus_id']}"
    )
    sys.stderr.write(line + "\n")
    sys.stderr.flush()
    return line


_upstream_lifespan = getattr(app.router, "lifespan_context", None)

if _upstream_lifespan is not None:

    @asynccontextmanager
    async def ywk_lifespan(scoped_app: Any):  # noqa: ANN201
        global _warmup_loop
        async with _upstream_lifespan(scoped_app):
            # Runs straight after the upstream startup(), i.e. after the
            # IRODORI_PRELOAD load has finished.
            note_device_once()
            # The warmup thread queues on the upstream's asyncio semaphore, so
            # it needs the loop the server actually runs on (§7).
            _warmup_loop = asyncio.get_running_loop()
            options = warmup_on_start_options()
            if options is not None:
                try:
                    started = start_warmup(**options)
                except WarmupBusy:  # pragma: no cover -- nothing else can be running yet
                    logger.warning("warmup on start: a run is already going")
                else:
                    logger.info(
                        "warmup on start: id=%s shots_total=%d",
                        started["id"],
                        started["shots_total"],
                    )
            try:
                yield
            finally:
                _warmup_cancel.set()
                with _pending_cond:
                    _pending_cond.notify_all()
                _warmup_loop = None

    app.router.lifespan_context = ywk_lifespan


@app.get("/params")
def ywk_get_params() -> dict[str, Any]:
    """Design §4-2.  200 with no model loaded; checkpoint-dependent欄 are null."""
    live = upstream.settings
    runtime = _live_runtime()
    return {
        "schema": ywk_params.SCHEMA,
        "engine": ywk_params.ENGINE,
        "version": YWK_VERSION,
        "model_loaded": bool(getattr(upstream.runtime_manager, "is_loaded", False)),
        "checkpoint": {
            "hf": live.hf_checkpoint,
            "max_text_len": getattr(runtime, "default_text_max_len", None),
            "max_caption_len": getattr(runtime, "default_caption_max_len", None),
            "ref_max_seconds": getattr(runtime, "default_max_ref_seconds", None),
        },
        "request": ywk_params.build_request_params(live, runtime),
        "irodori": ywk_params.build_irodori_params(live, runtime),
        "rules": ywk_params.build_rules(),
        "prefetch": {"available": False, "planned": "/ywk/prefetch"},
    }


def _cuda_device_info(device: Any) -> dict[str, Any]:
    """Name / uuid / pci_bus_id / hip / gcn_arch of the device the model is on.

    ROCm's torch reports device type ``cuda`` too, so the same four lines serve
    both machines; the two that tell them apart are ``hip``
    (``torch.version.hip`` -- ``null`` on a CUDA build) and ``gcn_arch``
    (``get_device_properties(i).gcnArchName``, e.g. ``gfx1151`` -- the attribute
    does not exist on a CUDA build, so ``getattr`` leaves it ``null``).
    ``research/lab/notes/29-gpu-designation-lab.md`` :317 has the live tuple.
    """
    info: dict[str, Any] = {
        "name": None,
        "uuid": None,
        "pci_bus_id": None,
        "hip": None,
        "gcn_arch": None,
    }
    if device is None or getattr(device, "type", None) != "cuda":
        return info
    try:
        import torch  # noqa: PLC0415

        index = device.index if device.index is not None else torch.cuda.current_device()
        props = torch.cuda.get_device_properties(index)
        info["name"] = getattr(props, "name", None)
        uuid_value = getattr(props, "uuid", None)
        info["uuid"] = None if uuid_value is None else str(uuid_value)
        info["pci_bus_id"] = getattr(props, "pci_bus_id", None)
        info["hip"] = getattr(torch.version, "hip", None)
        info["gcn_arch"] = getattr(props, "gcnArchName", None)
    except Exception:  # noqa: BLE001 -- status must never fail
        pass
    return info


@app.get("/ywk/status")
def ywk_status() -> dict[str, Any]:
    """Design §4-3.  ``device.actual`` is read from the model, not echoed."""
    import torch  # noqa: PLC0415

    live = upstream.settings
    manager = upstream.runtime_manager
    runtime = _live_runtime()

    actual: str | None = None
    precision = live.model_precision
    device_obj = None
    if runtime is not None:
        device_obj = getattr(runtime, "model_device", None)
        model = getattr(runtime, "model", None)
        if model is not None:
            try:
                parameter = next(model.parameters())
                device_obj = parameter.device
                precision = {
                    torch.float32: "fp32",
                    torch.bfloat16: "bf16",
                }.get(parameter.dtype, str(parameter.dtype))
            except (StopIteration, AttributeError):
                pass
        if device_obj is not None:
            actual = str(device_obj)

    # Same list the two voice routes serve, so a broken voices.json reports the
    # same "1 件 (デフォルト) ＋ 理由" here instead of a different number.
    voices, voices_error = _voice_list_with_reason()

    return {
        "engine": ywk_params.ENGINE,
        "version": YWK_VERSION,
        "variant": current_variant(),
        "upstream": {"irodori_tts": UPSTREAM_IRODORI_TTS, "server": UPSTREAM_SERVER},
        "host": live.host,
        "port": live.port,
        "runtime": {
            "loaded": bool(getattr(manager, "is_loaded", False)),
            "loading": bool(getattr(manager, "is_loading", False)),
            "error": _runtime_error,
        },
        "device": dict(
            configured=live.model_device,
            codec_configured=live.codec_device,
            actual=actual,
            precision=precision,
            **_cuda_device_info(device_obj),
        ),
        "torch": {
            "version": torch.__version__,
            "cuda": getattr(torch.version, "cuda", None),
            "hip": getattr(torch.version, "hip", None),
        },
        # Only the last path segment: design §4-3 forbids absolute paths here.
        "voices": {
            "count": len(voices),
            "dir": live.voices_dir.expanduser().name,
            "error": voices_error,
        },
        "warmup": warmup_snapshot(),
    }


# --------------------------------------------------------------------------
# 7. warmup (便 C 設計書 §2-3)
#
# Why it exists: on gfx1151 the first shot of an *unseen shape* pays MIOpen's
# algorithm search.  Two layers, both measured in research/lab/notes/40:
# the on-disk user db (11.2 s cold against 2.5 s warm, and it survives a restart
# -- §5-1/§5-2) and a per-process residue in ``decode_latent`` (+0.18..0.61 s,
# §5-1).  A shape is set by the output latent frame count (40 ms per frame) and,
# separately, by each reference clip's length -- the first VOICEROID2 reference
# of a session cost 14.8 s (decisions.md 40).  So the plan is: the ``seconds``
# stages first (no_ref, cheap, they warm the decoder), then one short shot per
# speaker the launcher names (natural length, the point being the reference).
#
# How it runs: one background thread calls the upstream's own synthesis path
# -- ``SpeechRequest`` -> ``_resolve_voice`` -> ``_build_sampling_request`` ->
# ``_synthesize_once`` -- rather than looping back through HTTP.  Nothing in the
# wrapper's whitelist is bypassed that matters (the bodies are ours), and going
# through ``_synthesize_once`` keeps the upstream's ``empty_cache_interval``
# accounting honest.  Serialisation is the upstream's own
# ``_acquire_synthesis_slot`` -- an **asyncio** Semaphore (app.py:523-547), not a
# threading one, so it is acquired on the server's event loop with
# ``run_coroutine_threadsafe``.  The wav is dropped on the floor.
# --------------------------------------------------------------------------

#: The stages 便 C fires by default: 4 / 8 / 12 s of output (research 40 §3 --
#: three stages cost 6.25 s together and cover the everyday range).
WARMUP_STAGES: tuple[float, ...] = (4.0, 8.0, 12.0)
WARMUP_TEXT = "暖機です。"

#: How long a shot waits for the real requests to drain before it fires anyway.
#: 設計書 §2-3: "最大 30 s・以後 1 射だけ撃って再確認".
WARMUP_QUIET_WAIT_S = 30.0

_WARMUP_MAX_STAGES = 12
_WARMUP_MAX_VOICES = 64
_WARMUP_MAX_SECONDS = 60.0
_WARMUP_MAX_TEXT_CHARS = 200
#: Attempts per shot when the upstream synthesis queue answers "full" (503).
_WARMUP_SLOT_ATTEMPTS = 3

_warmup_lock = threading.Lock()
_warmup_cancel = threading.Event()
_warmup_thread: threading.Thread | None = None

#: The server's event loop, captured in the lifespan.  ``None`` before startup
#: and after shutdown -- and in a unit test that drives the runner directly, in
#: which case the warmup simply does not queue on the upstream semaphore.
_warmup_loop: Any = None

_WARMUP_IDLE: dict[str, Any] = {
    "state": "idle",
    "id": None,
    "shots_done": 0,
    "shots_total": 0,
    "started_at": None,
    "finished_at": None,
    "last_shot": None,
    "error": None,
}
_warmup: dict[str, Any] = dict(_WARMUP_IDLE)

#: Real ``POST /v1/audio/speech`` calls in flight.  The warmup thread waits on
#: this reaching zero before each shot; the counter is the whole of the priority
#: mechanism (設計書 §2-3).
_pending_cond = threading.Condition()
_pending_real_requests = 0


class WarmupBusy(Exception):
    """A run is already going; the POST answers 409 (設計書 §2-3)."""

    def __init__(self, run_id: str | None) -> None:
        super().__init__(run_id or "")
        self.id = run_id


class _SlotBusy(Exception):
    """The upstream synthesis queue would not hand out a slot in time."""


def pending_real_requests() -> int:
    return _pending_real_requests


class _real_request_in_flight:  # noqa: N801 -- a context manager used as a verb
    """One real request's hold on the counter.  ``release`` is idempotent.

    Idempotent because the SSE path does not release at the ``with`` block's
    end: ``ywk_create_speech`` hands the hold to the response's body iterator,
    which releases in its ``finally``.  One holder, one decrement, whichever of
    the two ends first.
    """

    def __init__(self) -> None:
        self._held = False

    def __enter__(self) -> _real_request_in_flight:
        global _pending_real_requests
        with _pending_cond:
            _pending_real_requests += 1
            self._held = True
        return self

    def release(self) -> None:
        global _pending_real_requests
        with _pending_cond:
            if not self._held:
                return
            self._held = False
            _pending_real_requests = max(0, _pending_real_requests - 1)
            if _pending_real_requests == 0:
                _pending_cond.notify_all()

    def __exit__(self, *_exc: Any) -> None:
        self.release()
        return None


def _wait_until_quiet(timeout: float) -> bool:
    """Block until no real request is in flight.  True if it went quiet.

    False means the deadline passed (or the run was cancelled): the caller fires
    one shot anyway and asks again, which is what bounds the wait a real request
    can suffer at one warmup shot rather than a whole plan.
    """
    deadline = time.monotonic() + max(0.0, float(timeout))
    with _pending_cond:
        while _pending_real_requests > 0:
            if _warmup_cancel.is_set():
                return False
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                return False
            _pending_cond.wait(timeout=min(remaining, 0.2))
        return True


def warmup_plan(stages: Any, voices: Any) -> list[dict[str, Any]]:
    """The shot list: every ``seconds`` stage first, then one shot per speaker."""
    shots: list[dict[str, Any]] = []
    for seconds in stages:
        value = float(seconds)
        shots.append({"kind": "no_ref", "key": f"{value:g}s", "seconds": value})
    for voice in voices:
        shots.append({"kind": "voice", "key": str(voice), "seconds": None})
    return shots


def warmup_snapshot() -> dict[str, Any]:
    """``/ywk/status.warmup`` (設計書 §2-3).

    ``shots`` repeats ``shots_done`` under the name 便 A's ``/ywk/status`` already
    promised (contract ⑹); the launcher may read either.
    """
    with _warmup_lock:
        state = dict(_warmup)
    started = state.pop("started_at")
    finished = state.pop("finished_at")
    if started is None:
        elapsed = 0.0
    else:
        elapsed = round((finished if finished is not None else time.monotonic()) - started, 3)
    state["elapsed_s"] = elapsed
    state["shots"] = state["shots_done"]
    return state


def start_warmup(
    *,
    stages: Any = WARMUP_STAGES,
    voices: Any = (),
    text: str = WARMUP_TEXT,
) -> dict[str, Any]:
    """Start one run in a background thread.  Raises :class:`WarmupBusy`."""
    global _warmup_thread

    shots = warmup_plan(stages, voices)
    run_id = uuid.uuid4().hex[:12]
    with _warmup_lock:
        if _warmup["state"] == "running":
            raise WarmupBusy(_warmup["id"])
        _warmup.update(
            state="running",
            id=run_id,
            shots_done=0,
            shots_total=len(shots),
            started_at=time.monotonic(),
            finished_at=None,
            last_shot=None,
            error=None,
        )
    _warmup_cancel.clear()
    thread = threading.Thread(
        target=_warmup_run,
        args=(run_id, shots, str(text)),
        name="ywk-warmup",
        daemon=True,
    )
    _warmup_thread = thread
    try:
        thread.start()
    except RuntimeError as exc:  # out of threads: do not leave the record "running"
        _finish_warmup(run_id, "failed", scrub_text(f"{type(exc).__name__}: {exc}"))
        raise
    return {"id": run_id, "shots_total": len(shots), "state": "running"}


def _finish_warmup(run_id: str, state: str, error: str | None) -> None:
    with _warmup_lock:
        if _warmup["id"] != run_id:  # a newer run owns the record
            return
        _warmup["state"] = state
        _warmup["error"] = error
        _warmup["finished_at"] = time.monotonic()


def _warmup_run(run_id: str, shots: list[dict[str, Any]], text: str) -> None:
    total = len(shots)
    for index, shot in enumerate(shots, start=1):
        if _warmup_cancel.is_set():
            _finish_warmup(run_id, "cancelled", None)
            return
        quiet = _wait_until_quiet(WARMUP_QUIET_WAIT_S)
        if _warmup_cancel.is_set():
            _finish_warmup(run_id, "cancelled", None)
            return
        if not quiet:
            # 設計書 §2-3: after the 30 s ceiling fire *one* shot and ask again,
            # so a busy server still warms up instead of stalling forever.
            logger.info(
                "warmup shot %d/%d fires with %d real request(s) still in flight",
                index,
                total,
                pending_real_requests(),
            )
        started = time.perf_counter()
        try:
            _fire_warmup_shot(shot, text)
        except Exception as exc:  # noqa: BLE001 -- a warmup must not kill the server
            message = scrub_text(f"{type(exc).__name__}: {exc}")
            logger.warning(
                "warmup shot %d/%d failed: kind=%s key=%s error=%s",
                index,
                total,
                shot["kind"],
                shot["key"],
                message,
            )
            # A cancel that lands while a shot is retrying for a slot is a
            # cancel, not a failure.
            _finish_warmup(
                run_id,
                "cancelled" if _warmup_cancel.is_set() else "failed",
                None if _warmup_cancel.is_set() else message,
            )
            return
        elapsed_ms = (time.perf_counter() - started) * 1000.0
        last = {
            "kind": shot["kind"],
            "key": shot["key"],
            "seconds": shot["seconds"],
            "ms": round(elapsed_ms, 1),
        }
        with _warmup_lock:
            if _warmup["id"] != run_id:
                return
            _warmup["shots_done"] = index
            _warmup["last_shot"] = last
        # One line per shot (設計書 §2-3).
        logger.info(
            "warmup shot %d/%d kind=%s key=%s seconds=%s ms=%.0f",
            index,
            total,
            shot["kind"],
            shot["key"],
            "-" if shot["seconds"] is None else f"{shot['seconds']:g}",
            elapsed_ms,
        )
    _finish_warmup(run_id, "done", None)


def _fire_warmup_shot(shot: dict[str, Any], text: str) -> None:
    """One shot down the upstream's own path.  The audio is discarded."""
    body: dict[str, Any] = {"model": str(upstream.settings.model_name), "input": text}
    if shot["kind"] == "no_ref":
        # ``seconds`` pins the output length, which is exactly the knob that
        # picks the MIOpen shape -- and exactly why 本番の要求には送らない
        # (contract ⑶ 3-1: it also silently disables chunking).
        body["irodori"] = {"no_ref": True, "seconds": float(shot["seconds"])}
    else:
        body["voice"] = shot["key"]

    # The same two rewrites a real request gets, for the same reason.  A shot
    # that went straight to ``upstream._resolve_voice`` answered 400 for
    # 「デフォルト」 whenever ``voices.json`` could not resolve it -- a first run,
    # or a broken file -- while ``POST /v1/audio/speech`` answered 200 for the
    # very same speaker id (decisions.md 45).  Worse, 契約 ⑺ 7-2 stops a run at
    # its first failed shot, so the guaranteed speaker being first in the
    # launcher's list (設計書 §2-3's own example) killed every later shot too.
    # ``normalize_speech_body`` folds the upstream's no-reference spellings the
    # same way; the whitelist is deliberately not run (the bodies are ours).
    body = resolve_default_voice(normalize_speech_body(body))

    payload = upstream.SpeechRequest(**body)
    voice = upstream._resolve_voice(payload)
    sampling_request = upstream._build_sampling_request(payload, voice)
    upstream._validate_sampling_request(sampling_request)
    runtime = upstream.runtime_manager.get()

    last_error: Exception | None = None
    for _attempt in range(_WARMUP_SLOT_ATTEMPTS):
        try:
            semaphore = _acquire_upstream_slot()
        except _SlotBusy as exc:
            last_error = exc
            _wait_until_quiet(WARMUP_QUIET_WAIT_S)
            continue
        try:
            upstream._synthesize_once(runtime, sampling_request)
        finally:
            _release_upstream_slot(semaphore)
        return
    raise RuntimeError(f"synthesis slot was busy for {_WARMUP_SLOT_ATTEMPTS} attempts: {last_error}")


def _acquire_upstream_slot() -> Any:
    """Queue on the upstream's ``asyncio`` semaphore from this worker thread.

    ``_synthesis_semaphore`` is an ``asyncio.Semaphore`` created lazily by
    ``_get_synthesis_semaphore`` (app.py:523-530) and only ever awaited, so a
    plain thread cannot touch it: it has to be acquired on the loop the server
    runs on.  Without this a warmup shot and a real request would meet on the
    device at once -- worse than making the real request wait.  ``None`` (no
    loop, i.e. a direct unit-test call) means "no slot to release".
    """
    loop = _warmup_loop
    if loop is None or loop.is_closed():
        return None
    holder: list[Any] = []

    async def _acquire() -> None:
        holder.append(await upstream._acquire_synthesis_slot())

    future = asyncio.run_coroutine_threadsafe(_acquire(), loop)
    timeout = max(1.0, float(getattr(upstream.settings, "synthesis_wait_timeout", 60.0))) + 5.0
    try:
        future.result(timeout=timeout)
    except concurrent.futures.TimeoutError as exc:
        if not future.cancel() and holder:
            _release_upstream_slot(holder[0])
        raise _SlotBusy(f"the event loop did not answer in {timeout:.0f}s") from exc
    except Exception as exc:  # noqa: BLE001 -- upstream answers 503 when the queue is full
        raise _SlotBusy(str(getattr(exc, "detail", exc))) from exc
    return holder[0] if holder else None


def _release_upstream_slot(semaphore: Any) -> None:
    loop = _warmup_loop
    if semaphore is None or loop is None or loop.is_closed():
        return
    try:
        loop.call_soon_threadsafe(semaphore.release)
    except RuntimeError:  # the loop closed between the two lines
        pass


def _warmup_number_list(value: Any, param: str) -> list[float]:
    if not isinstance(value, list):
        raise YwkRequestError(
            f"{param} must be an array of numbers", code="ywk_type_error", param=param
        )
    if len(value) > _WARMUP_MAX_STAGES:
        raise YwkRequestError(
            f"{param} must hold at most {_WARMUP_MAX_STAGES} entries (got {len(value)})",
            code="ywk_out_of_range",
            param=param,
        )
    out: list[float] = []
    for item in value:
        if not _is_number(item):
            raise YwkRequestError(
                f"{param} entries must be numbers", code="ywk_type_error", param=param
            )
        seconds = float(item)
        if not 0 < seconds <= _WARMUP_MAX_SECONDS:
            raise YwkRequestError(
                f"{param} entries must be > 0 and <= {_WARMUP_MAX_SECONDS:g} (got {seconds:g})",
                code="ywk_out_of_range",
                param=param,
            )
        out.append(seconds)
    return out


def warmup_options(body: Any) -> dict[str, Any]:
    """Check the ``POST /ywk/warmup`` body.  Raises :class:`YwkRequestError`."""
    if body is None:
        body = {}
    if not isinstance(body, dict):
        raise YwkRequestError("request body must be a JSON object", code="ywk_invalid_body")
    for key in body:
        if key not in {"stages", "voices", "text"}:
            raise YwkRequestError(f"unknown field: {key}", code="ywk_unknown_field", param=key)

    stages_raw = body.get("stages")
    stages = list(WARMUP_STAGES) if stages_raw is None else _warmup_number_list(stages_raw, "stages")

    voices_raw = body.get("voices")
    voices: list[str] = []
    if voices_raw is not None:
        if not isinstance(voices_raw, list):
            raise YwkRequestError(
                "voices must be an array of strings", code="ywk_type_error", param="voices"
            )
        if len(voices_raw) > _WARMUP_MAX_VOICES:
            raise YwkRequestError(
                f"voices must hold at most {_WARMUP_MAX_VOICES} entries (got {len(voices_raw)})",
                code="ywk_out_of_range",
                param="voices",
            )
        for item in voices_raw:
            if not isinstance(item, str) or not item.strip():
                raise YwkRequestError(
                    "voices entries must be non-empty strings",
                    code="ywk_type_error",
                    param="voices",
                )
            voices.append(item.strip())

    text_raw = body.get("text")
    if text_raw is None:
        text = WARMUP_TEXT
    elif not isinstance(text_raw, str):
        raise YwkRequestError("text must be a string", code="ywk_type_error", param="text")
    else:
        text = text_raw.strip() or WARMUP_TEXT
    if len(text) > _WARMUP_MAX_TEXT_CHARS:
        raise YwkRequestError(
            f"text must be at most {_WARMUP_MAX_TEXT_CHARS} characters (got {len(text)})",
            code="ywk_out_of_range",
            param="text",
        )

    if not stages and not voices:
        raise YwkRequestError(
            "a warmup needs at least one stage or one voice",
            code="ywk_out_of_range",
            param="stages",
        )
    return {"stages": stages, "voices": voices, "text": text}


@app.post("/ywk/warmup")
async def ywk_start_warmup(request: Request):  # noqa: ANN201
    raw = await request.body()
    if raw.strip():
        try:
            body = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            return ywk_error(f"request body is not valid JSON: {exc}", code="ywk_invalid_body")
    else:
        body = {}

    try:
        options = warmup_options(body)
    except YwkRequestError as exc:
        return ywk_error(exc.message, code=exc.code, param=exc.param)

    try:
        started = start_warmup(**options)
    except WarmupBusy as exc:
        return ywk_error(
            f"暖機が走行中（id={exc.id}）。終わりを待つか DELETE /ywk/warmup/{exc.id} で止める。",
            status_code=409,
            code="ywk_warmup_running",
        )
    return JSONResponse(status_code=202, content=started)


@app.delete("/ywk/warmup/{run_id}")
def ywk_cancel_warmup(run_id: str):  # noqa: ANN201
    """Cancel: the runner stops **before the next shot** (設計書 §2-3).

    The shot already on the device runs to the end -- the upstream offers no
    cancellation either (contract ⑶ 3-4).
    """
    with _warmup_lock:
        current = _warmup["id"]
        state = _warmup["state"]
    if current != run_id:
        return ywk_error(
            f"unknown warmup id: {run_id}", status_code=404, code="ywk_warmup_unknown_id"
        )
    if state != "running":
        return {"id": run_id, "state": state, "cancel_requested": False}
    _warmup_cancel.set()
    with _pending_cond:  # wake a thread parked in _wait_until_quiet
        _pending_cond.notify_all()
    return {"id": run_id, "state": "cancelling", "cancel_requested": True}


def _truthy(value: Any) -> bool:
    return str(value).strip().lower() in {"1", "true", "yes", "on"}


def warmup_on_start_options() -> dict[str, Any] | None:
    """``YWK_WARMUP_ON_START=1`` and friends (設計書 §2-3).

    This is the road the runtime is checked on while 便 D's launcher does not
    exist yet: ``YWK_WARMUP_VOICES`` is a comma-separated list of speaker ids and
    ``YWK_WARMUP_STAGES`` a comma-separated list of seconds.
    """
    if not _truthy(os.environ.get("YWK_WARMUP_ON_START", "")):
        return None
    stages: list[float] = []
    raw_stages = str(os.environ.get("YWK_WARMUP_STAGES", "") or "").strip()
    if raw_stages:
        for part in raw_stages.split(","):
            part = part.strip()
            if not part:
                continue
            try:
                stages.append(float(part))
            except ValueError:
                logger.warning("YWK_WARMUP_STAGES: ignoring %r", part)
    else:
        stages = list(WARMUP_STAGES)
    voices = [
        item.strip()
        for item in str(os.environ.get("YWK_WARMUP_VOICES", "") or "").split(",")
        if item.strip()
    ]
    text = str(os.environ.get("YWK_WARMUP_TEXT", "") or "").strip() or WARMUP_TEXT
    try:
        return warmup_options({"stages": stages, "voices": voices, "text": text})
    except YwkRequestError as exc:
        logger.warning("warmup on start: %s", exc.message)
        return None


# --------------------------------------------------------------------------
# 8. startup checks
# --------------------------------------------------------------------------

# The routes above changed the schema; drop FastAPI's cached copy.
app.openapi_schema = None

_fastapi_openapi = app.openapi


def ywk_openapi() -> dict[str, Any]:
    """Keep ``SpeechRequest``/``IrodoriOptions`` in ``/openapi.json``.

    The replacement ``POST /v1/audio/speech`` takes a raw ``Request`` so that
    the whitelist runs before pydantic does (otherwise an over-long ``input``
    would 422 with the body echoed back).  That would drop the two models out
    of the generated schema, and the 44 field names in ``IrodoriOptions`` are
    the one thing the upstream openapi *was* good for, so they are injected
    back here -- straight from the live pydantic models, not from a copy.
    """
    if app.openapi_schema is not None:
        return app.openapi_schema
    schema = _fastapi_openapi()
    model_schema = upstream.SpeechRequest.model_json_schema(
        ref_template="#/components/schemas/{model}"
    )
    components = schema.setdefault("components", {}).setdefault("schemas", {})
    for name, definition in model_schema.pop("$defs", {}).items():
        components.setdefault(name, definition)
    components["SpeechRequest"] = model_schema
    operation = schema.get("paths", {}).get("/v1/audio/speech", {}).get("post")
    if operation is not None:
        operation["requestBody"] = {
            "required": True,
            "content": {
                "application/json": {"schema": {"$ref": "#/components/schemas/SpeechRequest"}}
            },
        }
    app.openapi_schema = schema
    return schema


app.openapi = ywk_openapi


def check_openapi_drift() -> list[str]:
    """Compare ``ywk_params`` with the live upstream openapi (design §4-2)."""
    try:
        problems = ywk_params.openapi_mismatches(app.openapi())
    except Exception as exc:  # noqa: BLE001 -- drift detection must not stop startup
        return [f"openapi の突合に失敗: {type(exc).__name__}: {exc}"]
    for line in problems:
        sys.stderr.write(f"ywk_server: openapi drift: {line}\n")
    return problems


OPENAPI_DRIFT = check_openapi_drift()


def banner() -> str:
    line = (
        f"ywk_server {YWK_VERSION} "
        f"upstream={UPSTREAM_IRODORI_TTS}/{UPSTREAM_SERVER} "
        f"variant={current_variant()} "
        f"device={EFFECTIVE_DEVICES['model']}/{EFFECTIVE_DEVICES['codec']} "
        f"precision={os.environ.get('IRODORI_MODEL_PRECISION')}"
    )
    if "miopen_db" in VARIANT_ENV:
        # Last two segments only, like ``/ywk/status.voices.dir``: the banner
        # ends up in the launcher's log window.
        db = Path(VARIANT_ENV["miopen_db"])
        line += f" miopen={db.parent.name}/{db.name}"
    return line


def main(argv: list[str] | None = None) -> None:
    import uvicorn  # noqa: PLC0415

    logging.basicConfig(level=logging.INFO, format="%(levelname)s:%(name)s:%(message)s")
    parser = argparse.ArgumentParser(prog="ywk_server", description="irodori-TTS for yomiwakechan")
    parser.add_argument("--host", default=settings.host)
    parser.add_argument("--port", type=int, default=settings.port)
    args = parser.parse_args(argv)

    sys.stderr.write(banner() + "\n")
    sys.stderr.flush()
    uvicorn.run(app, host=str(args.host), port=int(args.port))


if __name__ == "__main__":
    main()
