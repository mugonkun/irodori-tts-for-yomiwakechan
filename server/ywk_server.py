"""ywk_server -- the distribution's wrapper around the upstream Irodori-TTS server.

One file.  It imports the upstream FastAPI app unmodified and adds what the
本体 (yomiwakechan2) needs but upstream does not have:

* ``GET /params``          -- the machine-readable parameter table (design §4-2)
* ``GET /ywk/status``      -- version, real device, voices, warmup (§4-3)
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
import json
import logging
import os
import re
import sys
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

    return effective


apply_env_defaults()
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
    """True when the body already names a reference (or ``no_ref``).

    ``_resolve_voice`` (app.py:437-454) short-circuits on any of the six, so a
    body that carries one must be left exactly as it is.
    """
    options = body.get("irodori")
    if not isinstance(options, dict):
        options = {}
    for key in (*REFERENCE_KEYS, "no_ref"):
        if options.get(key) is not None or body.get(key) is not None:
            return True
    return False


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
    """Check the body, then hand it to the upstream handler unchanged (§4-5)."""
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
        async with _upstream_lifespan(scoped_app):
            # Runs straight after the upstream startup(), i.e. after the
            # IRODORI_PRELOAD load has finished.
            note_device_once()
            yield

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
    info: dict[str, Any] = {"name": None, "uuid": None, "pci_bus_id": None}
    if device is None or getattr(device, "type", None) != "cuda":
        return info
    try:
        import torch  # noqa: PLC0415

        index = device.index if device.index is not None else torch.cuda.current_device()
        props = torch.cuda.get_device_properties(index)
        info["name"] = getattr(props, "name", None)
        uuid = getattr(props, "uuid", None)
        info["uuid"] = None if uuid is None else str(uuid)
        info["pci_bus_id"] = getattr(props, "pci_bus_id", None)
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
        "warmup": {"state": "idle", "shots": 0},
    }


# --------------------------------------------------------------------------
# 7. startup checks
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
    return (
        f"ywk_server {YWK_VERSION} "
        f"upstream={UPSTREAM_IRODORI_TTS}/{UPSTREAM_SERVER} "
        f"device={EFFECTIVE_DEVICES['model']}/{EFFECTIVE_DEVICES['codec']} "
        f"precision={os.environ.get('IRODORI_MODEL_PRECISION')}"
    )


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
