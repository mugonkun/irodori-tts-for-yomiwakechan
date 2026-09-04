"""``patches/0001`` の形の検分（git を使わない・台本の前に落とすための網）.

The real check is ``git apply --check`` inside ``build/assemble-app.ps1``.  What
this module guards is the one thing that check cannot recover from and that a
fresh clone can silently break: the patch file must stay **LF**.  Measured on
this machine (see ``patches/README.md``):

    LF tree   + LF patch   -> apply ok
    CRLF tree + LF patch   -> apply ok
    LF tree   + CRLF patch -> "patch does not apply"

``.gitattributes`` currently says ``* text=auto``, so a checkout with
``core.autocrlf=true`` turns the patch into CRLF unless ``*.patch -text`` is
added.  This test fails loudly if that happens.
"""

from __future__ import annotations

from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
PATCH = ROOT / "patches" / "0001-audio-io-fallback-on-importerror.patch"
UPSTREAM_CORE = ROOT / "upstream" / "Irodori-TTS"

TARGETS = (
    "irodori_tts/inference_runtime.py",
    "irodori_tts/codec.py",
)


@pytest.fixture(scope="module")
def raw() -> bytes:
    assert PATCH.is_file(), f"missing {PATCH}"
    return PATCH.read_bytes()


def test_patch_file_is_lf_only(raw):
    assert b"\r" not in raw, (
        "the patch file has CRLF line endings; git apply will refuse it against "
        "an LF copy. Add '*.patch -text' to .gitattributes."
    )


def test_patch_touches_only_the_two_upstream_files(raw):
    text = raw.decode("utf-8")
    headers = [line for line in text.splitlines() if line.startswith("diff --git ")]
    assert headers == [f"diff --git a/{name} b/{name}" for name in sorted(TARGETS)]


def test_patch_has_exactly_three_hunks(raw):
    text = raw.decode("utf-8")
    assert text.count("\n@@ ") == 3
    assert text.count("\n-        except RuntimeError:") == 1  # codec.encode_file
    assert text.count("\n-    except RuntimeError:") == 2  # _load_audio, save_wav
    assert text.count("except (RuntimeError, ImportError):") == 3


def test_patch_names_the_pinned_base(raw):
    assert "8224dafb46d0aba89209a8f905f1cb7e3299d9c1" in raw.decode("utf-8")


def test_patch_context_still_matches_the_pinned_upstream(raw):
    """The context lines must exist verbatim in the submodule, or it has moved."""
    text = raw.decode("utf-8")
    diff = text[text.index("diff --git ") :]  # skip the commit-message preamble
    context = [
        line[1:] for line in diff.splitlines() if line.startswith(" ") and line.strip()
    ]
    assert context, "the patch carries no context lines"
    haystack = "\n".join(
        (UPSTREAM_CORE / name).read_text(encoding="utf-8") for name in TARGETS
    )
    for line in context:
        assert line in haystack, f"context line no longer in upstream: {line!r}"


def test_upstream_still_has_the_three_narrow_excepts():
    """If upstream fixes this itself, the patch must be retired, not re-based."""
    runtime = (UPSTREAM_CORE / "irodori_tts" / "inference_runtime.py").read_text(
        encoding="utf-8"
    )
    codec = (UPSTREAM_CORE / "irodori_tts" / "codec.py").read_text(encoding="utf-8")
    assert runtime.count("    except RuntimeError:\n        import soundfile as sf") == 2
    assert codec.count("        except RuntimeError:\n            import soundfile as sf") == 1


def test_torchaudio_still_raises_importerror_without_torchcodec():
    """The reason the patch exists.  If this stops holding, retire the patch."""
    import torch
    import torchaudio

    try:
        import torchcodec  # noqa: F401
    except ImportError:
        pass
    else:
        pytest.skip("torchcodec is installed here; the fallback cannot be exercised")

    with pytest.raises(ImportError):
        torchaudio.load("does-not-exist.wav")
    with pytest.raises(ImportError):
        torchaudio.save("does-not-exist.wav", torch.zeros(1, 10), 16000)
