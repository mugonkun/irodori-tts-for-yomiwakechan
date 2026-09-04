"""裁定 65: 参照潜在キャッシュ（`ref_latent` の事前計算）.

Why it exists (`docs/radeon.md` §7-6・`research/lab/notes/40-rocm-warmup.md` §6-2):
on gfx1151 每 speaker switch pays `prepare_reference` 0.95〜1.44 s through the
waveform path, and 10.4〜11.5 ms through the latent path.  So the wrapper encodes
each reference once into `<voices_dir>/latents/<ascii-stem>.pt` and points the
`voices.json` alias at it.

Nothing here loads a model.  ``FakeCodec`` stands in for the DACVAE encoder --
what these tests have to prove is not the audio but the *plumbing*: that the
file lands somewhere the upstream scan cannot see, that the alias stops naming
the wav, that a changed wav is noticed, and above all that the upstream's own
``_resolve_voice`` → ``_build_sampling_request`` road really does end with
``SamplingRequest.ref_latent`` set and ``ref_wav`` ``None`` (item 4 of the
seat's brief).  The last one is the only claim that matters at request time and
it is the one a fake cannot fudge: the code under test there is upstream's.
"""

from __future__ import annotations

import json
import shutil
import struct
import threading
import time
import wave
from pathlib import Path
from typing import Any

import pytest
import torch
from conftest import DEFAULT_VOICE, JA_VOICE, MODEL, VOICES, write_voices

import ywk_server

WAIT_S = 10.0
LATENT_DIM = 32
#: 512 samples of 16 kHz audio per latent frame -- the number only has to be
#: consistent, the real codec's hop is a property of the checkpoint.
HOP = 512
SAMPLE_RATE = 16000


# ---------------------------------------------------------------- 道具

def wait_for(predicate, timeout: float = WAIT_S, interval: float = 0.02) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        time.sleep(interval)
    return bool(predicate())


def status_of(client) -> dict[str, Any]:
    return client.get("/ywk/status").json()["precompute"]


def wait_for_state(client, *states: str, timeout: float = WAIT_S) -> dict[str, Any]:
    wait_for(lambda: status_of(client)["state"] in states, timeout=timeout)
    return status_of(client)


class FakeCodecModel:
    hop_length = HOP


class FakeCodec:
    """Counts encodes and returns a ``(1, T, D)`` latent, like the real one.

    ``encode_waveform``'s contract (codec.py:177-192) is the whole of what the
    precompute leans on: input ``(B, C, T)``, output ``(B, T_latent, D)``.  The
    dtype is deliberately **bf16** so the test proves the wrapper casts to fp32
    before saving -- on the Radeon build the codec really does run bf16
    (decisions.md 5) and research U-REF-6 leaves bf16 ``.pt`` portability
    unverified.
    """

    def __init__(self) -> None:
        self.sample_rate = SAMPLE_RATE
        self.model = FakeCodecModel()
        self.calls: list[dict[str, Any]] = []
        self.gate: threading.Event | None = None
        self.entered = threading.Semaphore(0)
        self.fail_on: set[str] = set()

    def encode_waveform(
        self,
        waveform: Any,
        sample_rate: int,
        *,
        normalize_db: Any = None,
        ensure_max: Any = None,
    ) -> Any:
        self.calls.append(
            {
                "shape": tuple(waveform.shape),
                "sample_rate": int(sample_rate),
                "normalize_db": normalize_db,
                "ensure_max": ensure_max,
            }
        )
        self.entered.release()
        if self.gate is not None:
            self.gate.wait(timeout=WAIT_S)
        frames = max(1, int(waveform.shape[-1]) // HOP)
        return torch.zeros(1, frames, LATENT_DIM, dtype=torch.bfloat16)


def write_wav(path: Path, *, seconds: float = 1.0, value: int = 1000) -> Path:
    """A real 16-bit PCM wav, so the upstream ``_load_audio`` reads it for real."""
    path.parent.mkdir(parents=True, exist_ok=True)
    count = int(SAMPLE_RATE * seconds)
    frames = struct.pack("<%dh" % count, *([value] * count))
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        handle.writeframes(frames)
    return path


#: Where the test speakers' reference wavs live.  A subdirectory, because a
#: ``.wav`` in ``voices_dir`` itself is a speaker in its own right
#: (``_scan_voice_files``) and these tests want to count speakers exactly.
#: ``_resolve_voice_path`` (voices.py:245-249) joins the relative alias value
#: onto ``voices_dir``, so ``"src/ref.wav"`` resolves the same as any other.
SRC = "src"


def fit_runtime(baseline, codec: FakeCodec | None = None) -> FakeCodec:
    """Give the ``FakeRuntime`` the two attributes the precompute reads."""
    codec = codec or FakeCodec()
    baseline.codec = codec
    baseline.model_cfg = type("Cfg", (), {"latent_dim": LATENT_DIM})()
    baseline.default_max_ref_seconds = 30.0
    return codec


def one_speaker(baseline, *, seconds: float = 1.0, value: int = 1000) -> FakeCodec:
    """One alias speaker whose reference is a real wav; returns the fake codec."""
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": f"{SRC}/ref.wav"}}
    )
    write_wav(VOICES / SRC / "ref.wav", seconds=seconds, value=value)
    return fit_runtime(baseline)


def two_speakers(baseline, codec: FakeCodec | None = None) -> FakeCodec:
    write_voices(
        aliases={
            DEFAULT_VOICE: {"no_ref": True},
            "alpha": {"ref_wav": f"{SRC}/a.wav"},
            "beta": {"ref_wav": f"{SRC}/b.wav"},
        }
    )
    write_wav(VOICES / SRC / "a.wav")
    write_wav(VOICES / SRC / "b.wav")
    return fit_runtime(baseline, codec)


@pytest.fixture(autouse=True)
def clean_sources() -> Any:
    """``write_voices`` only unlinks the files at the top of ``voices/``."""
    shutil.rmtree(VOICES / SRC, ignore_errors=True)
    yield
    shutil.rmtree(VOICES / SRC, ignore_errors=True)


def aliases() -> dict[str, Any]:
    return json.loads((VOICES / "voices.json").read_text(encoding="utf-8"))


def run_and_wait(client, body: dict[str, Any]) -> dict[str, Any]:
    response = client.post("/ywk/voices/precompute", json=body)
    assert response.status_code == 202, response.text
    wait_for_state(client, "done", "failed", "cancelled")
    return response.json()


# ---------------------------------------------------------------- 状態遷移

def test_status_starts_idle(client):
    state = status_of(client)
    assert state["state"] == "idle"
    assert state["id"] is None
    assert state["done"] == 0
    assert state["total"] == 0
    assert state["last"] is None
    assert state["error"] is None


def test_run_goes_idle_to_running_to_done(client, baseline):
    one_speaker(baseline)
    started = run_and_wait(client, {"all": True})
    assert set(started) == {"id", "total", "state"}
    assert started["total"] == 1
    assert started["state"] == "running"

    state = status_of(client)
    assert state["state"] == "done"
    assert state["id"] == started["id"]
    assert state["done"] == 1
    assert state["total"] == 1
    assert state["built"] == 1
    assert state["failed"] == 0
    assert state["error"] is None
    assert state["last"]["id"] == JA_VOICE
    assert state["last"]["state"] == "built"
    assert state["elapsed_s"] >= 0.0


def test_a_second_run_while_one_is_going_is_409(client, baseline):
    codec = one_speaker(baseline)
    codec.gate = threading.Event()
    first = client.post("/ywk/voices/precompute", json={"all": True})
    assert first.status_code == 202
    assert codec.entered.acquire(timeout=WAIT_S)

    second = client.post("/ywk/voices/precompute", json={"all": True})
    assert second.status_code == 409
    assert second.json()["error"]["code"] == "ywk_precompute_running"

    codec.gate.set()
    assert wait_for_state(client, "done")["state"] == "done"


def test_the_default_speaker_is_never_a_target(client, baseline):
    one_speaker(baseline)
    assert ywk_server.precompute_targets(None) == [JA_VOICE]


# ---------------------------------------------------------------- 焼く形

def test_the_pt_holds_a_two_dimensional_fp32_tensor(client, baseline):
    """The shape and dtype ``_load_reference_latent`` can actually read.

    ``torch.load(path, map_location="cpu", weights_only=True)`` followed by
    ``_coerce_latent_shape`` (inference_runtime.py:906-909) is the upstream's
    whole reader, and it calls ``.ndim`` on whatever came back -- a dict would
    survive ``weights_only`` and die there.  fp32 because line 912 casts to the
    runtime dtype on load, so the file never has to be bf16.
    """
    from irodori_tts.inference_runtime import _coerce_latent_shape

    codec = one_speaker(baseline, seconds=2.0)
    run_and_wait(client, {"all": True})

    pt_path, sidecar = ywk_server.latent_paths(JA_VOICE)
    assert pt_path.is_file()
    assert sidecar.is_file()

    loaded = torch.load(str(pt_path), map_location="cpu", weights_only=True)
    assert isinstance(loaded, torch.Tensor)
    assert loaded.ndim == 2
    assert loaded.dtype is torch.float32
    assert loaded.shape[1] == LATENT_DIM
    # The fake codec's own output was bf16: the wrapper cast it.
    assert codec.calls and _coerce_latent_shape(loaded, latent_dim=LATENT_DIM).shape == loaded.shape


def test_the_encode_uses_the_upstream_reference_knobs(client, baseline):
    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})
    call = codec.calls[0]
    # ``settings.default_ref_normalize_db`` / ``default_ref_ensure_max``
    # (config.py:60-61) -- the same two the waveform path passes at request time.
    assert call["normalize_db"] == pytest.approx(-16.0)
    assert call["ensure_max"] is True
    assert call["sample_rate"] == SAMPLE_RATE
    # (B, C, T): ``_load_audio`` gives (C, T) and the wrapper unsqueezes.
    assert len(call["shape"]) == 3


def test_the_stem_is_ascii_even_for_a_japanese_speaker(client, baseline):
    one_speaker(baseline)
    stem = ywk_server.latent_stem(JA_VOICE)
    assert stem.isascii()
    assert all(char.isalnum() or char in "_-" for char in stem)
    # Stable, and different speakers never collide on it.
    assert stem == ywk_server.latent_stem(JA_VOICE)
    assert stem != ywk_server.latent_stem(JA_VOICE + "2")


def test_the_pt_lives_in_a_subdirectory_and_does_not_join_the_list(client, baseline):
    """research handoff §1-7 ⒝: the scan is one level deep and ``.wav`` wins.

    Both traps are avoided by the same decision -- put the ``.pt`` in
    ``voices/latents/``.  What this asserts is the visible consequence: the
    speaker list is exactly as long after the run as before it, and no entry
    named after the stem appears.
    """
    one_speaker(baseline)
    before = [item["id"] for item in client.get("/ywk/voices").json()["data"]]
    run_and_wait(client, {"all": True})
    after = [item["id"] for item in client.get("/ywk/voices").json()["data"]]

    assert after == before
    assert ywk_server.latent_stem(JA_VOICE) not in after
    assert (VOICES / "latents").is_dir()
    assert ywk_server.latent_paths(JA_VOICE)[0].parent == VOICES / "latents"


# ---------------------------------------------------------------- alias

def test_the_alias_is_rewritten_to_ref_latent_and_keeps_no_wav(client, baseline):
    one_speaker(baseline)
    run_and_wait(client, {"all": True})

    entry = aliases()[JA_VOICE]
    assert entry == {"ref_latent": f"latents/{ywk_server.latent_stem(JA_VOICE)}.pt"}
    # ``ref_wav`` must be gone, not merely joined: upstream 400s on both
    # ("Waveform and latent references cannot be combined", app.py:1062-1067).
    assert "ref_wav" not in entry
    # The reserved speaker is untouched.
    assert aliases()[DEFAULT_VOICE] == {"no_ref": True}


def test_the_alias_path_is_relative(client, baseline):
    one_speaker(baseline)
    run_and_wait(client, {"all": True})
    text = (VOICES / "voices.json").read_text(encoding="utf-8")
    assert str(VOICES) not in text
    assert ":\\" not in text and ":/" not in text


def test_a_broken_voices_json_is_never_clobbered(client, baseline):
    """A broken alias file must not be rewritten from an empty dict.

    Starting over from ``{}`` would erase 「デフォルト」 and every speaker the
    launcher wrote -- a far worse outcome than a failed precompute.  So the read
    raises and the speaker cannot resolve in the first place: the POST is a 400
    (⑷ says a broken ``voices.json`` is "0 件＋理由", never a 500), and the file
    on disk is byte-for-byte what it was.
    """
    one_speaker(baseline)
    (VOICES / "voices.json").write_text("{ broken", encoding="utf-8")

    named = client.post("/ywk/voices/precompute", json={"ids": [JA_VOICE]})
    assert named.status_code == 400
    assert named.json()["error"]["code"] == "ywk_unknown_voice"

    # ``all`` has nothing to walk, so it finishes at once rather than failing.
    everything = run_and_wait(client, {"all": True})
    assert everything["total"] == 0
    assert status_of(client)["state"] == "done"

    assert (VOICES / "voices.json").read_text(encoding="utf-8") == "{ broken"


def test_a_wav_sitting_in_voices_dir_itself_is_covered_too(client, baseline):
    """The preset shape: a ``.wav`` at the top of ``voices/`` **is** a speaker.

    ``_scan_voice_files`` turns ``voices/kotonoha.wav`` into the speaker
    ``kotonoha`` with no alias at all, and research handoff §1-7 ⒝ records that
    inside that scan a ``.wav`` beats a ``.pt`` of the same stem -- which is why
    the ``.pt`` goes to ``latents/`` and an **alias** is written for a speaker
    that never had one.  ``resolve()`` reads aliases before the scan
    (voices.py:80-88), so the latent wins where a sibling ``.pt`` would have
    lost.
    """
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=())
    write_wav(VOICES / "kotonoha.wav")
    fit_runtime(baseline)

    assert ywk_server.precompute_targets(None) == ["kotonoha"]
    run_and_wait(client, {"all": True})

    assert aliases()["kotonoha"] == {
        "ref_latent": f"latents/{ywk_server.latent_stem('kotonoha')}.pt"
    }
    assert (VOICES / "kotonoha.wav").is_file()

    response = client.post(
        "/v1/audio/speech", json={"model": MODEL, "input": "テスト。", "voice": "kotonoha"}
    )
    assert response.status_code == 200, response.text
    request = baseline.requests[-1]
    assert request.ref_wav is None
    assert Path(request.ref_latent) == ywk_server.latent_paths("kotonoha")[0]


# ---------------------------------------------------------------- 上流の釘打ち

def test_the_upstream_really_takes_the_latent_road(client, baseline):
    """Item 4 of the brief -- the one claim only the upstream can answer.

    After the rewrite, a plain ``POST /v1/audio/speech`` with this speaker must
    reach ``InferenceRuntime.synthesize`` with ``ref_latent`` naming the ``.pt``
    and ``ref_wav`` ``None``.  The road is upstream's own
    ``_resolve_voice`` (app.py:418-454) → ``_build_sampling_request``
    (app.py:849-861); the wrapper only wrote the alias.  If this ever inverts --
    the ``.wav`` beating the ``.pt`` as it does inside ``_scan_voice_files`` --
    the cache would be silently dead and every shot would pay the 0.95〜1.44 s
    again, with nothing in the response to say so.
    """
    one_speaker(baseline)
    run_and_wait(client, {"all": True})

    response = client.post(
        "/v1/audio/speech", json={"model": MODEL, "input": "テスト。", "voice": JA_VOICE}
    )
    assert response.status_code == 200, response.text

    request = baseline.requests[-1]
    assert request.ref_wav is None
    assert request.ref_wavs is None
    assert request.ref_latent is not None
    assert Path(request.ref_latent) == ywk_server.latent_paths(JA_VOICE)[0]
    assert request.no_ref is False


def test_the_saved_latent_survives_the_upstream_reader(client, baseline):
    """The same file, read the way ``_load_reference_latent`` reads it."""
    from irodori_tts.inference_runtime import _coerce_latent_shape

    one_speaker(baseline, seconds=3.0)
    run_and_wait(client, {"all": True})
    pt_path = ywk_server.latent_paths(JA_VOICE)[0]

    raw = torch.load(str(pt_path), map_location="cpu", weights_only=True)
    piece = _coerce_latent_shape(raw, latent_dim=LATENT_DIM).unsqueeze(0)
    assert piece.ndim == 3
    assert piece.shape[0] == 1
    assert piece.shape[2] == LATENT_DIM
    assert piece.shape[1] > 0
    # The runtime casts on load; a bf16 runtime therefore needs nothing from us.
    assert piece.to(dtype=torch.bfloat16).dtype is torch.bfloat16


# ---------------------------------------------------------------- stale

def test_an_unchanged_wav_is_reused_without_encoding_again(client, baseline):
    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})
    assert len(codec.calls) == 1

    run_and_wait(client, {"all": True})
    assert len(codec.calls) == 1
    assert status_of(client)["reused"] == 1


def test_force_rebuilds_even_when_nothing_changed(client, baseline):
    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})
    run_and_wait(client, {"all": True, "force": True})
    assert len(codec.calls) == 2
    assert status_of(client)["built"] == 1


def test_a_changed_wav_turns_the_speaker_stale_and_is_rebuilt(client, baseline):
    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})

    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent"] is True
    assert by_id[JA_VOICE]["latent_stale"] is False

    # Same name, different audio: the sha256 in the sidecar stops matching.
    with ywk_server._digest_lock:
        ywk_server._digest_cache.clear()
    write_wav(VOICES / SRC / "ref.wav", seconds=2.0, value=3000)

    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent"] is True
    assert by_id[JA_VOICE]["latent_stale"] is True

    run_and_wait(client, {"all": True})
    assert len(codec.calls) == 2
    assert status_of(client)["built"] == 1

    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent_stale"] is False


def test_changing_the_encode_knobs_turns_the_speaker_stale(client, baseline, monkeypatch):
    """The latent bakes the loudness in, so the knob is part of the key.

    Once the alias names the ``.pt``, ``ref_normalize_db`` at request time does
    nothing -- the upstream's latent branch never reads it
    (``inference_runtime.py:903-924``).  A launcher that changes the default
    must therefore be told the cache no longer matches, not left serving audio
    normalised to the old target.
    """
    from irodori_openai_tts import app as upstream

    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})
    assert len(codec.calls) == 1

    monkeypatch.setattr(upstream.settings, "default_ref_normalize_db", -20.0)
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent_stale"] is True

    run_and_wait(client, {"all": True})
    assert len(codec.calls) == 2
    assert codec.calls[1]["normalize_db"] == pytest.approx(-20.0)
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent_stale"] is False


def test_a_missing_pt_is_stale(client, baseline):
    one_speaker(baseline)
    run_and_wait(client, {"all": True})
    ywk_server.latent_paths(JA_VOICE)[0].unlink()

    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent_stale"] is True


def test_a_rebuild_after_the_alias_moved_still_finds_the_wav(client, baseline):
    """Once the alias names the ``.pt``, the sidecar is the only map back.

    ``VoiceSpec`` cannot hold both, so after the first run the live spec no
    longer mentions ``ref.wav``; the second run has to read the source path out
    of ``<stem>.json`` or the cache could never be refreshed at all.
    """
    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})
    assert upstream_spec(JA_VOICE).ref_wav is None

    with ywk_server._digest_lock:
        ywk_server._digest_cache.clear()
    write_wav(VOICES / SRC / "ref.wav", seconds=2.0, value=2500)
    run_and_wait(client, {"all": True})

    assert len(codec.calls) == 2
    assert status_of(client)["built"] == 1
    assert status_of(client)["failed"] == 0


def upstream_spec(voice_id: str):
    from irodori_openai_tts import app as upstream

    return upstream.voice_registry.resolve(voice_id)


# ------------------------------------------------- sidecar を失ったとき（⑷ 4-4）

def test_a_lost_sidecar_turns_our_own_latent_stale(client, baseline):
    """``latents/<stem>.pt`` with no ``<stem>.json`` is *ours with the map lost*.

    It used to report ``latent_stale: false``＝healthy, which is the worst of the
    three possible answers: the alias no longer names a wav, the sidecar that
    remembered it is gone, and the list told the launcher there was nothing to
    fix.  The stem carries twelve hex digits of the speaker id's sha256, so a
    ``.pt`` at that exact path can only be one we wrote.
    """
    one_speaker(baseline)
    run_and_wait(client, {"all": True})
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent_stale"] is False

    ywk_server.latent_paths(JA_VOICE)[1].unlink()
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent"] is True
    assert by_id[JA_VOICE]["latent_stale"] is True


def test_a_foreign_pt_without_a_sidecar_stays_not_stale(client, baseline):
    """The other half of the same rule: someone else's ``.pt`` is left alone.

    Nothing is known about its provenance, and calling it stale would invite the
    launcher to overwrite a file its owner put there deliberately (⑷ 4-4).
    """
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_latent": f"{SRC}/theirs.pt"}}
    )
    (VOICES / SRC).mkdir(parents=True, exist_ok=True)
    torch.save(torch.zeros(4, LATENT_DIM), str(VOICES / SRC / "theirs.pt"))
    fit_runtime(baseline)

    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent"] is True
    assert by_id[JA_VOICE]["latent_stale"] is False


def test_a_lost_sidecar_is_rebuilt_from_the_scanned_wav(client, baseline):
    """The road back when the map is gone: the upstream's own naming rule.

    A wav dropped straight into ``voices/`` is a speaker named after its stem
    (``_scan_voice_files``), and it is **still there** after the alias took over
    -- the alias merely wins the lookup.  So the wrapper looks for it again
    rather than declaring the speaker unbakeable forever.
    """
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}})
    write_wav(VOICES / "scanned.wav")
    codec = fit_runtime(baseline)

    run_and_wait(client, {"ids": ["scanned"]})
    assert aliases()["scanned"] == {"ref_latent": ywk_server.latent_alias_value("scanned")}
    assert upstream_spec("scanned").ref_wav is None

    ywk_server.latent_paths("scanned")[1].unlink()
    run_and_wait(client, {"ids": ["scanned"]})

    state = status_of(client)
    assert state["built"] == 1, state
    assert state["failed"] == 0
    assert len(codec.calls) == 2
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id["scanned"]["latent_stale"] is False


def test_a_lost_sidecar_is_rebuilt_from_the_launcher_ledger(client, baseline):
    """``voices.ywk.json`` may name the wav too -- 便 D's ledger, read only."""
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}})
    write_wav(VOICES / SRC / "ref.wav")
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": f"{SRC}/ref.wav"}}
    )
    (VOICES / "voices.ywk.json").write_text(
        json.dumps({"schema": 1, "voices": {JA_VOICE: {"ref_wav": f"{SRC}/ref.wav"}}}),
        encoding="utf-8",
    )
    codec = fit_runtime(baseline)

    run_and_wait(client, {"all": True})
    ywk_server.latent_paths(JA_VOICE)[1].unlink()
    run_and_wait(client, {"all": True})

    state = status_of(client)
    assert state["built"] == 1, state
    assert len(codec.calls) == 2


def test_force_on_an_untraceable_speaker_fails_instead_of_skipping(client, baseline):
    """``force`` means "bake it again"; ``skipped`` would say "all is well".

    Nothing can be rebuilt here -- the reference lived in a subdirectory the
    upstream never scans and no ledger names it -- so the operator has to be
    told, once, rather than reading a ``done`` run that built nothing.
    """
    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})
    ywk_server.latent_paths(JA_VOICE)[1].unlink()

    run_and_wait(client, {"ids": [JA_VOICE], "force": True})
    state = status_of(client)
    assert state["state"] == "failed"
    assert state["failed"] == 1
    assert state["skipped"] == 0
    assert "sidecar" in state["error"]
    assert len(codec.calls) == 1
    # ⑶ 3-3: no absolute path in the reason.
    body = client.get("/ywk/status").text
    assert ":\\" not in body and ":/" not in body


def test_a_no_ref_speaker_is_still_skipped_under_force(client, baseline):
    """Only "the trail is broken" becomes a failure -- not "there is no wav"."""
    one_speaker(baseline)
    run_and_wait(client, {"ids": [DEFAULT_VOICE], "force": True})
    state = status_of(client)
    assert state["skipped"] == 1
    assert state["failed"] == 0
    assert state["state"] == "done"


# ------------------------------------------------- チェックポイントも鍵のうち

def test_the_checkpoint_is_part_of_the_key(client, baseline, monkeypatch):
    """A latent lives in *that checkpoint's* latent space, and nothing else checks.

    The upstream's latent branch reads the shape and ``latent_dim`` only
    (``inference_runtime.py:906-912``, ``_coerce_latent_shape`` :136-147), so a
    ``.pt`` baked under a different model of the same width -- 32 on this
    machine -- would be swallowed without a word.  ``/params`` publishes
    ``checkpoint.hf``, so this is a knob that really does get turned.
    """
    from irodori_openai_tts import app as upstream

    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})
    stored = ywk_server.read_sidecar(JA_VOICE)["params"]
    assert stored["checkpoint"] == "Aratako/Irodori-TTS-v4.1-Small"
    assert stored["latent_dim"] == LATENT_DIM

    monkeypatch.setattr(upstream.settings, "hf_checkpoint", "Aratako/Irodori-TTS-v4.1-Large")
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent_stale"] is True

    run_and_wait(client, {"all": True})
    assert len(codec.calls) == 2
    assert status_of(client)["built"] == 1
    assert ywk_server.read_sidecar(JA_VOICE)["params"]["checkpoint"] == (
        "Aratako/Irodori-TTS-v4.1-Large"
    )
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent_stale"] is False


def test_a_schema_1_sidecar_cannot_be_vouched_for(client, baseline):
    """The old sidecar named no checkpoint, so it rebakes once."""
    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})

    _, sidecar_path = ywk_server.latent_paths(JA_VOICE)
    payload = json.loads(sidecar_path.read_text(encoding="utf-8"))
    payload["schema"] = 1
    payload["params"].pop("checkpoint")
    payload["params"].pop("latent_dim")
    sidecar_path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")

    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent_stale"] is True
    run_and_wait(client, {"all": True})
    assert len(codec.calls) == 2
    assert status_of(client)["built"] == 1


# ------------------------------------------------- 潜在を外す口（⑷ 4-4・⑺ 7-3）

def test_dropping_the_latent_puts_the_alias_back_on_the_wav(client, baseline):
    """The road back out of the bake, so 便 D never has to edit files by hand.

    ``resolve()`` reads aliases before it scans (voices.py:80-88) and the three
    upstream write ports are closed (⑷ 4-3), so without this port deleting the
    wav would leave the speaker in the list, still speaking from a ``.pt``.
    """
    codec = one_speaker(baseline)
    run_and_wait(client, {"all": True})
    pt_path, sidecar_path = ywk_server.latent_paths(JA_VOICE)
    assert pt_path.is_file() and sidecar_path.is_file()

    response = client.delete(f"/ywk/voices/{JA_VOICE}/latent")
    assert response.status_code == 200, response.text
    body = response.json()
    assert body["id"] == JA_VOICE
    assert body["state"] == "reverted"
    assert body["alias"] == "ref_wav"
    assert sorted(body["removed"]) == sorted(
        [f"latents/{pt_path.name}", f"latents/{sidecar_path.name}"]
    )
    # ⑶ 3-3: relative, so no absolute path leaves the process.
    assert ":\\" not in response.text and ":/" not in response.text

    assert aliases()[JA_VOICE] == {"ref_wav": f"{SRC}/ref.wav"}
    assert not pt_path.is_file() and not sidecar_path.is_file()
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent"] is False
    assert by_id[JA_VOICE]["latent_stale"] is False

    # And the speaker can be baked again afterwards.
    run_and_wait(client, {"all": True})
    assert status_of(client)["built"] == 1
    assert len(codec.calls) == 2


def test_dropping_the_latent_keeps_the_launcher_own_keys(client, baseline):
    write_voices(
        aliases={
            DEFAULT_VOICE: {"no_ref": True},
            JA_VOICE: {"ref_wav": f"{SRC}/ref.wav", "note": "便 D の欄"},
        }
    )
    write_wav(VOICES / SRC / "ref.wav")
    fit_runtime(baseline)

    run_and_wait(client, {"all": True})
    assert aliases()[JA_VOICE]["note"] == "便 D の欄"
    assert client.delete(f"/ywk/voices/{JA_VOICE}/latent").status_code == 200
    assert aliases()[JA_VOICE] == {"note": "便 D の欄", "ref_wav": f"{SRC}/ref.wav"}


def test_dropping_a_latent_that_was_never_baked_is_absent(client, baseline):
    one_speaker(baseline)
    response = client.delete(f"/ywk/voices/{JA_VOICE}/latent")
    assert response.status_code == 200
    assert response.json() == {
        "id": JA_VOICE,
        "state": "absent",
        "alias": "unchanged",
        "removed": [],
    }
    assert aliases()[JA_VOICE] == {"ref_wav": f"{SRC}/ref.wav"}


def test_dropping_a_foreign_latent_unlinks_the_alias_but_not_the_file(client, baseline):
    """Someone else's ``.pt`` is unaliased, never deleted (⑷ 4-4 と同じ手加減)."""
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_latent": f"{SRC}/theirs.pt"}}
    )
    (VOICES / SRC).mkdir(parents=True, exist_ok=True)
    theirs = VOICES / SRC / "theirs.pt"
    torch.save(torch.zeros(4, LATENT_DIM), str(theirs))
    write_wav(VOICES / f"{JA_VOICE}.wav")  # the scan can take the speaker back
    fit_runtime(baseline)

    response = client.delete(f"/ywk/voices/{JA_VOICE}/latent")
    assert response.status_code == 200, response.text
    assert response.json()["removed"] == []
    assert theirs.is_file(), "someone else's file was deleted"
    assert aliases()[JA_VOICE] == {"ref_wav": f"{JA_VOICE}.wav"}


def test_dropping_the_latent_of_an_unknown_speaker_is_404(client, baseline):
    one_speaker(baseline)
    response = client.delete("/ywk/voices/いない人/latent")
    assert response.status_code == 404
    assert response.json()["error"]["code"] == "ywk_unknown_voice"


def test_dropping_a_latent_while_a_run_is_going_is_409(client, baseline):
    codec = one_speaker(baseline)
    codec.gate = threading.Event()
    started = client.post("/ywk/voices/precompute", json={"all": True}).json()
    assert codec.entered.acquire(timeout=WAIT_S)
    try:
        response = client.delete(f"/ywk/voices/{JA_VOICE}/latent")
        assert response.status_code == 409
        error = response.json()["error"]
        assert error["code"] == "ywk_precompute_running"
        assert started["id"] in error["message"]
    finally:
        codec.gate.set()
    assert wait_for_state(client, "done")["state"] == "done"


# ---------------------------------------------------------------- 一覧の欄

def test_the_list_reports_latent_false_before_a_run(client, baseline):
    one_speaker(baseline)
    by_id = {item["id"]: item for item in client.get("/ywk/voices").json()["data"]}
    assert by_id[JA_VOICE]["latent"] is False
    assert by_id[JA_VOICE]["latent_stale"] is False
    assert by_id[DEFAULT_VOICE]["latent"] is False
    assert by_id[DEFAULT_VOICE]["latent_stale"] is False


def test_the_list_still_carries_no_absolute_path_after_a_run(client, baseline):
    one_speaker(baseline)
    run_and_wait(client, {"all": True})
    for path in ("/ywk/voices", "/v1/audio/voices", "/ywk/status"):
        body = client.get(path).text
        assert ":\\" not in body and ":/" not in body
        assert str(VOICES) not in body


# ---------------------------------------------------------------- 優先度

def test_a_real_request_makes_the_worker_wait_before_the_next_speaker(client, baseline):
    """The warmup's rule, applied to the precompute (裁定 65 の「譲る」).

    Two speakers, a gate on the codec.  While the first encode is parked, a
    real request is counted in flight; the worker must not start the second
    encode until that count drops back to zero.
    """
    codec = FakeCodec()
    codec.gate = threading.Event()
    two_speakers(baseline, codec)

    assert client.post("/ywk/voices/precompute", json={"all": True}).status_code == 202
    assert codec.entered.acquire(timeout=WAIT_S)

    holder = ywk_server._real_request_in_flight()
    holder.__enter__()
    try:
        codec.gate.set()
        # The first encode finishes; the second must not start while the real
        # request is counted.
        time.sleep(0.3)
        assert len(codec.calls) == 1
        assert status_of(client)["done"] == 1
    finally:
        holder.release()

    assert wait_for_state(client, "done")["state"] == "done"
    assert len(codec.calls) == 2


def test_the_run_takes_the_upstream_synthesis_slot(client, baseline):
    """Same semaphore the warmup and the real requests queue on (⑶ 3-4).

    An encode running beside a synthesis on the same device is worse than an
    encode that waits, so the worker borrows ``_acquire_synthesis_slot`` exactly
    the way ``_fire_warmup_shot`` does.
    """
    calls: list[str] = []
    real_acquire = ywk_server._acquire_upstream_slot
    real_release = ywk_server._release_upstream_slot

    def spy_acquire():
        calls.append("acquire")
        return real_acquire()

    def spy_release(semaphore):
        calls.append("release")
        return real_release(semaphore)

    one_speaker(baseline)
    ywk_server._acquire_upstream_slot = spy_acquire
    ywk_server._release_upstream_slot = spy_release
    try:
        run_and_wait(client, {"all": True})
    finally:
        ywk_server._acquire_upstream_slot = real_acquire
        ywk_server._release_upstream_slot = real_release
    assert calls == ["acquire", "release"]


# ---------------------------------------------------------------- 取消

def test_cancel_stops_before_the_next_speaker(client, baseline):
    codec = FakeCodec()
    codec.gate = threading.Event()
    two_speakers(baseline, codec)

    started = client.post("/ywk/voices/precompute", json={"all": True}).json()
    assert started["total"] == 2
    assert codec.entered.acquire(timeout=WAIT_S)

    cancelled = client.delete(f"/ywk/voices/precompute/{started['id']}")
    assert cancelled.status_code == 200
    assert cancelled.json() == {
        "id": started["id"],
        "state": "cancelling",
        "cancel_requested": True,
    }

    codec.gate.set()
    state = wait_for_state(client, "cancelled", "done")
    assert state["state"] == "cancelled"
    # The encode already on the device ran to the end; the second never started.
    assert len(codec.calls) == 1
    assert state["done"] == 1
    assert state["total"] == 2


def test_cancel_lands_at_once_while_a_real_request_is_in_flight(client, baseline):
    """The ``DELETE`` has to break the *wait*, not only the loop after it.

    With a real request counted in flight the worker parks inside
    ``_wait_until_quiet`` for up to ``PRECOMPUTE_QUIET_WAIT_S``＝30 s.  That wait
    read the **warmup's** cancel flag, so the precompute's own cancel could not
    reach it: the route's ``notify_all`` woke the thread, the condition was still
    "a real request is in flight", and it went back to sleep.  A launcher asking
    to stop got its ``202``-shaped answer and then up to half a minute of the
    run carrying on.
    """
    codec = FakeCodec()
    codec.gate = threading.Event()
    two_speakers(baseline, codec)

    started = client.post("/ywk/voices/precompute", json={"all": True}).json()
    assert codec.entered.acquire(timeout=WAIT_S)

    holder = ywk_server._real_request_in_flight()
    holder.__enter__()
    try:
        codec.gate.set()  # the first encode finishes; the worker parks on the wait
        assert wait_for(lambda: status_of(client)["done"] == 1)
        began = time.monotonic()
        assert client.delete(f"/ywk/voices/precompute/{started['id']}").status_code == 200
        assert wait_for(lambda: status_of(client)["state"] == "cancelled", timeout=2.0)
        assert time.monotonic() - began < 1.0
    finally:
        holder.release()

    assert len(codec.calls) == 1
    assert status_of(client)["state"] == "cancelled"


def test_a_cancelled_warmup_does_not_stop_the_precompute_from_yielding(client, baseline):
    """The two runs have separate cancel flags (契約 ⑺ 7-3 の「譲る」).

    ``_wait_until_quiet`` used to read ``_warmup_cancel`` whoever called it, and
    that flag was only ever cleared at the *start* of a warmup.  So one cancelled
    warmup -- something the launcher does whenever the user starts speaking
    during startup -- turned the yielding off for the rest of the process: every
    later precompute item ran straight over the top of a real request.
    """
    ywk_server._warmup_cancel.set()  # the state one cancelled warmup leaves behind
    try:
        codec = FakeCodec()
        codec.gate = threading.Event()
        two_speakers(baseline, codec)

        assert client.post("/ywk/voices/precompute", json={"all": True}).status_code == 202
        assert codec.entered.acquire(timeout=WAIT_S)

        holder = ywk_server._real_request_in_flight()
        holder.__enter__()
        try:
            codec.gate.set()
            time.sleep(0.3)
            assert len(codec.calls) == 1, "the second encode ran over the real request"
            assert status_of(client)["done"] == 1
        finally:
            holder.release()

        assert wait_for_state(client, "done")["state"] == "done"
        assert len(codec.calls) == 2
    finally:
        ywk_server._warmup_cancel.clear()


def test_cancelling_an_unknown_id_is_404(client):
    response = client.delete("/ywk/voices/precompute/deadbeef")
    assert response.status_code == 404
    assert response.json()["error"]["code"] == "ywk_precompute_unknown_id"


def test_cancelling_a_finished_run_reports_its_state(client, baseline):
    one_speaker(baseline)
    started = run_and_wait(client, {"all": True})
    response = client.delete(f"/ywk/voices/precompute/{started['id']}")
    assert response.status_code == 200
    assert response.json() == {"id": started["id"], "state": "done", "cancel_requested": False}


# ---------------------------------------------------------------- body の検査

@pytest.mark.parametrize(
    ("body", "code"),
    [
        ({"nope": 1}, "ywk_unknown_field"),
        ({"ids": "x"}, "ywk_type_error"),
        ({"ids": [""]}, "ywk_type_error"),
        ({"ids": []}, "ywk_out_of_range"),
        ({}, "ywk_out_of_range"),
        ({"all": "yes"}, "ywk_type_error"),
        ({"force": 1}, "ywk_type_error"),
        ({"ids": ["x"], "all": True}, "ywk_invalid_body"),
    ],
)
def test_bad_bodies_are_400(client, baseline, body, code):
    one_speaker(baseline)
    response = client.post("/ywk/voices/precompute", json=body)
    assert response.status_code == 400, response.text
    assert response.json()["error"]["code"] == code


def test_an_unknown_speaker_id_is_400(client, baseline):
    one_speaker(baseline)
    response = client.post("/ywk/voices/precompute", json={"ids": ["いない人"]})
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_unknown_voice"


def test_ids_takes_only_the_named_speaker(client, baseline):
    two_speakers(baseline)

    started = run_and_wait(client, {"ids": ["beta"]})
    assert started["total"] == 1
    assert "ref_latent" in aliases()["beta"]
    assert aliases()["alpha"] == {"ref_wav": f"{SRC}/a.wav"}


# ---------------------------------------------------------------- 飛ばす・失敗

def test_a_no_ref_speaker_is_skipped_not_failed(client, baseline):
    one_speaker(baseline)
    run_and_wait(client, {"ids": [DEFAULT_VOICE]})
    state = status_of(client)
    assert state["skipped"] == 1
    assert state["failed"] == 0
    assert state["state"] == "done"
    assert state["last"]["state"] == "skipped"
    assert aliases()[DEFAULT_VOICE] == {"no_ref": True}


def test_one_bad_speaker_does_not_cost_the_others_their_latent(client, baseline):
    """Unlike the warmup, a failure does not stop the run (contract ⑺ 7-3).

    The warmup stops at the first failure because a bad speaker id there makes
    every later shot wrong too.  Here twelve presets are independent, and one
    unreadable wav must not leave the other eleven paying 1 s per switch.
    """
    write_voices(
        aliases={
            DEFAULT_VOICE: {"no_ref": True},
            "bad": {"ref_wav": f"{SRC}/bad.wav"},
            "good": {"ref_wav": f"{SRC}/good.wav"},
        }
    )
    write_wav(VOICES / SRC / "good.wav")
    (VOICES / SRC / "bad.wav").parent.mkdir(parents=True, exist_ok=True)
    (VOICES / SRC / "bad.wav").write_bytes(b"not a wav at all")
    fit_runtime(baseline)

    run_and_wait(client, {"all": True})
    state = status_of(client)
    assert state["state"] == "failed"
    assert state["failed"] == 1
    assert state["built"] == 1
    assert state["done"] == 2
    assert state["error"] and state["error"].startswith("bad: ")
    assert "ref_latent" in aliases()["good"]
    assert aliases()["bad"] == {"ref_wav": f"{SRC}/bad.wav"}


def test_the_error_carries_no_absolute_path(client, baseline):
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, "bad": {"ref_wav": f"{SRC}/missing.wav"}}
    )
    fit_runtime(baseline)
    run_and_wait(client, {"all": True})
    body = client.get("/ywk/status").text
    assert ":\\" not in body and ":/" not in body
    assert str(VOICES) not in body


# ---------------------------------------------------------------- 変種の既定

def test_the_default_is_on_for_rocm_and_off_for_cuda(monkeypatch):
    """decisions.md 11 kept the CUDA build out of this; 65 let the Radeon in."""
    monkeypatch.delenv("YWK_PRECOMPUTE_ON_START", raising=False)

    monkeypatch.setenv("YWK_VARIANT", "cuda")
    assert ywk_server.precompute_on_start_enabled() is False
    monkeypatch.setenv("YWK_VARIANT", "cpu")
    assert ywk_server.precompute_on_start_enabled() is False
    monkeypatch.delenv("YWK_VARIANT", raising=False)
    assert ywk_server.precompute_on_start_enabled() is False

    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    assert ywk_server.precompute_on_start_enabled() is True


def test_the_env_overrides_the_variant_default_both_ways(monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.setenv("YWK_PRECOMPUTE_ON_START", "0")
    assert ywk_server.precompute_on_start_enabled() is False

    monkeypatch.setenv("YWK_VARIANT", "cuda")
    monkeypatch.setenv("YWK_PRECOMPUTE_ON_START", "1")
    assert ywk_server.precompute_on_start_enabled() is True

    # A blank value is not a choice; the variant decides.
    monkeypatch.setenv("YWK_PRECOMPUTE_ON_START", "   ")
    assert ywk_server.precompute_on_start_enabled() is False


def test_startup_does_not_run_a_precompute_on_the_cuda_variant(monkeypatch, baseline):
    from fastapi.testclient import TestClient

    one_speaker(baseline)
    monkeypatch.setenv("YWK_VARIANT", "cuda")
    monkeypatch.delenv("YWK_PRECOMPUTE_ON_START", raising=False)
    monkeypatch.delenv("YWK_WARMUP_ON_START", raising=False)
    with TestClient(ywk_server.app, raise_server_exceptions=False) as test_client:
        assert status_of(test_client)["state"] == "idle"
        assert status_of(test_client)["id"] is None


def test_startup_runs_a_precompute_on_the_rocm_variant(monkeypatch, baseline):
    from fastapi.testclient import TestClient

    codec = one_speaker(baseline)
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.delenv("YWK_PRECOMPUTE_ON_START", raising=False)
    monkeypatch.delenv("YWK_WARMUP_ON_START", raising=False)
    with TestClient(ywk_server.app, raise_server_exceptions=False) as test_client:
        state = wait_for_state(test_client, "done", "failed")
        assert state["state"] == "done"
        assert state["total"] == 1
        assert state["built"] == 1
    assert len(codec.calls) == 1
    assert "ref_latent" in aliases()[JA_VOICE]
