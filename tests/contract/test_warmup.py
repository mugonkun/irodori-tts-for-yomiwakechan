"""便 C 設計書 §2-3: 暖機 API・優先度・Radeon 変種の欄.

Why there is a warmup at all: on gfx1151 the first shot of an unseen shape pays
MIOpen's algorithm search -- 11.2 s against 2.5 s warm on the same shot, and the
first VOICEROID2 reference of a session cost 14.8 s
(``research/lab/notes/40-rocm-warmup.md`` §5, ``decisions.md`` 40).  So the
wrapper fires the shapes itself, before the 本体 asks for them.

Nothing here loads a model: ``FakeRuntime`` (conftest) records the
``SamplingRequest`` it is handed, which is exactly what these tests need to see
-- that the ``seconds`` stages went out as reference-free shots and the speaker
shots went out with the reference the ledger names.

The one thing the fakes cannot fake is time, so the tests that need a shot to be
*in flight* hand ``FakeRuntime`` a gate to block on.
"""

from __future__ import annotations

import threading
import time
from typing import Any

import pytest
from conftest import DEFAULT_VOICE, JA_VOICE, MODEL, VOICES, write_voices

import ywk_params

WAIT_S = 10.0


def wait_for(predicate, timeout: float = WAIT_S, interval: float = 0.02) -> bool:
    """Poll ``predicate`` until it is true.  Returns whether it became true."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        time.sleep(interval)
    return bool(predicate())


def warmup_of(client) -> dict[str, Any]:
    return client.get("/ywk/status").json()["warmup"]


def wait_for_state(client, *states: str, timeout: float = WAIT_S) -> dict[str, Any]:
    wait_for(lambda: warmup_of(client)["state"] in states, timeout=timeout)
    return warmup_of(client)


class GatedRuntime:
    """A ``FakeRuntime`` whose every synthesis parks until the test lets it go."""

    def __init__(self) -> None:
        import torch
        from irodori_tts.inference_runtime import SamplingResult

        self._torch = torch
        self._result = SamplingResult
        self.requests: list[Any] = []
        self.entered = threading.Semaphore(0)
        self.release = threading.Event()
        self.model_device = torch.device("cpu")
        self.codec_device = torch.device("cpu")
        self.default_text_max_len = 256
        self.default_caption_max_len = 256
        self.default_max_ref_seconds = 120.0

    def synthesize(self, req: Any, *, log_fn: Any = None) -> Any:
        self.requests.append(req)
        self.entered.release()
        self.release.wait(timeout=WAIT_S)
        audio = self._torch.zeros(1, 100)
        return self._result(
            audio=audio,
            audios=[audio],
            sample_rate=16000,
            stage_timings=[],
            total_to_decode=0.1,
            used_seed=1,
            messages=[],
        )


@pytest.fixture()
def gated(ywk) -> Any:
    """Install a runtime that parks inside every shot."""
    from conftest import FakeRuntimeManager
    from irodori_openai_tts import app as upstream

    runtime = GatedRuntime()
    upstream.runtime_manager = FakeRuntimeManager(runtime)
    yield runtime
    runtime.release.set()


def with_speaker() -> str:
    """A ``voices.json`` that resolves one real speaker, and its id."""
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "alice.wav"}},
        files=("alice.wav",),
    )
    return JA_VOICE


# --------------------------------------------------------------------- 状態遷移


def test_status_warmup_is_idle_before_anything(client):
    warmup = warmup_of(client)
    assert warmup["state"] == "idle"
    assert warmup["id"] is None
    assert warmup["shots_done"] == 0
    assert warmup["shots_total"] == 0
    assert warmup["elapsed_s"] == 0.0
    assert warmup["last_shot"] is None
    assert warmup["error"] is None
    # 便 A の contract ⑹ が約束した欄も残す。
    assert warmup["shots"] == 0
    assert set(warmup) == set(ywk_params.WARMUP_KEYS)


def test_the_state_names_match_the_published_vocabulary(ywk):
    """The five states the launcher (便 D) may see, named in one place."""
    assert ywk._WARMUP_IDLE["state"] == "idle"
    assert ywk_params.WARMUP_STATES == ("idle", "running", "done", "failed", "cancelled")


def test_warmup_fires_the_stages_then_the_speakers(client, baseline):
    voice = with_speaker()
    response = client.post(
        "/ywk/warmup", json={"stages": [4, 8, 12], "voices": [voice], "text": "暖機です。"}
    )
    assert response.status_code == 202, response.text
    started = response.json()
    assert started["shots_total"] == 4
    assert started["state"] == "running"
    assert started["id"]

    final = wait_for_state(client, "done", "failed")
    assert final["state"] == "done", final
    assert final["shots_done"] == 4
    assert final["error"] is None
    assert final["elapsed_s"] >= 0.0
    assert final["last_shot"]["kind"] == "voice"
    assert final["last_shot"]["key"] == voice
    assert final["last_shot"]["ms"] >= 0.0

    # 射の順と中身は FakeRuntime が受けた SamplingRequest が証拠。
    seen = baseline.requests
    assert len(seen) == 4
    assert [request.seconds for request in seen] == [4.0, 8.0, 12.0, None]
    assert [request.no_ref for request in seen] == [True, True, True, False]
    assert seen[-1].ref_wav == str(VOICES / "alice.wav")
    assert all(request.text == "暖機です。" for request in seen)


def test_warmup_defaults_to_the_three_stages(client, baseline):
    response = client.post("/ywk/warmup", json={})
    assert response.status_code == 202, response.text
    assert response.json()["shots_total"] == 3
    assert wait_for_state(client, "done", "failed")["state"] == "done"
    assert [request.seconds for request in baseline.requests] == [4.0, 8.0, 12.0]


def test_an_empty_body_is_the_same_as_the_defaults(client, baseline):
    response = client.post("/ywk/warmup", content=b"")
    assert response.status_code == 202, response.text
    assert response.json()["shots_total"] == 3


def test_a_second_warmup_while_one_runs_is_409(client, gated):
    first = client.post("/ywk/warmup", json={"stages": [4, 8]})
    assert first.status_code == 202, first.text
    assert gated.entered.acquire(timeout=WAIT_S)  # shot 1 is on the device

    second = client.post("/ywk/warmup", json={"stages": [4]})
    assert second.status_code == 409
    error = second.json()["error"]
    assert error["code"] == "ywk_warmup_running"
    assert first.json()["id"] in error["message"]

    gated.release.set()
    assert wait_for_state(client, "done", "failed")["state"] == "done"

    # 走り終われば次の暖機は受け付ける。
    assert client.post("/ywk/warmup", json={"stages": [4]}).status_code == 202


def test_delete_stops_before_the_next_shot(client, gated):
    started = client.post("/ywk/warmup", json={"stages": [4, 8, 12]}).json()
    assert gated.entered.acquire(timeout=WAIT_S)

    cancelled = client.delete(f"/ywk/warmup/{started['id']}")
    assert cancelled.status_code == 200, cancelled.text
    assert cancelled.json() == {
        "id": started["id"],
        "state": "cancelling",
        "cancel_requested": True,
    }

    gated.release.set()  # the shot already on the device finishes
    final = wait_for_state(client, "cancelled", "done", "failed")
    assert final["state"] == "cancelled", final
    assert final["shots_done"] == 1
    assert final["shots_total"] == 3
    assert len(gated.requests) == 1


def test_delete_of_an_unknown_id_is_404(client):
    response = client.delete("/ywk/warmup/deadbeef")
    assert response.status_code == 404
    assert response.json()["error"]["code"] == "ywk_warmup_unknown_id"


def test_delete_after_the_run_finished_reports_the_final_state(client):
    started = client.post("/ywk/warmup", json={"stages": [4]}).json()
    assert wait_for_state(client, "done", "failed")["state"] == "done"
    response = client.delete(f"/ywk/warmup/{started['id']}")
    assert response.status_code == 200
    assert response.json() == {"id": started["id"], "state": "done", "cancel_requested": False}


def test_a_failing_shot_lands_in_failed_with_a_scrubbed_reason(client, ywk):
    from conftest import FakeRuntimeManager
    from irodori_openai_tts import app as upstream

    class Exploding:
        model_device = None
        codec_device = None

        def synthesize(self, req: Any, *, log_fn: Any = None) -> Any:
            raise RuntimeError(f"MIOpen failed while reading {VOICES / 'x.db'}")

    upstream.runtime_manager = FakeRuntimeManager(Exploding())
    assert client.post("/ywk/warmup", json={"stages": [4, 8]}).status_code == 202

    final = wait_for_state(client, "failed", "done")
    assert final["state"] == "failed", final
    assert final["shots_done"] == 0
    assert "RuntimeError" in final["error"]
    assert ":\\" not in final["error"] and ":/" not in final["error"]


def test_an_unknown_speaker_fails_the_run(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}})
    assert client.post("/ywk/warmup", json={"stages": [], "voices": ["いない人"]}).status_code == 202
    final = wait_for_state(client, "failed", "done")
    assert final["state"] == "failed", final
    assert final["error"]


# ----------------------------------------------------------------------- 優先度


def test_a_real_request_makes_the_next_shot_wait(client, baseline, ywk):
    """設計書 §2-3: 次の射の前に pending が 0 になるまで待つ。"""
    in_flight = ywk._real_request_in_flight()
    in_flight.__enter__()  # a real request is now in flight
    try:
        assert client.post("/ywk/warmup", json={"stages": [4, 8]}).status_code == 202
        assert ywk.pending_real_requests() == 1
        # 1 射も撃たないまま待つ（30 s の上限までは黙って待つ）。
        assert not wait_for(lambda: warmup_of(client)["shots_done"] > 0, timeout=0.5)
        assert warmup_of(client)["state"] == "running"
        assert baseline.requests == []
    finally:
        in_flight.__exit__(None, None, None)

    final = wait_for_state(client, "done", "failed")
    assert final["state"] == "done", final
    assert final["shots_done"] == 2


def test_the_speech_route_counts_the_request_while_it_runs(client, ywk, baseline):
    seen: list[int] = []
    original = baseline.synthesize

    def counting(req: Any, *, log_fn: Any = None) -> Any:
        seen.append(ywk.pending_real_requests())
        return original(req, log_fn=log_fn)

    baseline.synthesize = counting
    response = client.post(
        "/v1/audio/speech", json={"model": MODEL, "input": "テスト。", "voice": DEFAULT_VOICE}
    )
    assert response.status_code == 200, response.text
    assert seen == [1]
    assert ywk.pending_real_requests() == 0


def test_the_counter_comes_back_down_after_a_rejected_request(client, ywk):
    bad = client.post("/v1/audio/speech", json={"model": MODEL, "input": "x", "num_step": 1})
    assert bad.status_code == 400
    assert ywk.pending_real_requests() == 0


def test_the_warmup_waits_on_the_upstream_semaphore(client, gated, ywk):
    """A shot holds the upstream slot, so a real request queues behind it.

    ``_acquire_synthesis_slot`` is an **asyncio** semaphore (``app.py:523-547``),
    so the warmup thread has to take it on the server's loop -- otherwise the
    shot and the real request would meet on the device at the same moment.
    """
    assert client.post("/ywk/warmup", json={"stages": [4]}).status_code == 202
    assert gated.entered.acquire(timeout=WAIT_S)
    assert ywk._warmup_loop is not None

    done = threading.Event()

    def real_request() -> None:
        client.post(
            "/v1/audio/speech",
            json={"model": MODEL, "input": "テスト。", "voice": DEFAULT_VOICE},
        )
        done.set()

    caller = threading.Thread(target=real_request, daemon=True)
    caller.start()
    try:
        # 暖機の射が枠を握っている間、本物は合成に**入らない**（同時に走らない）。
        # 枠を取っていなければ本物は executor で synthesize に入ってしまう＝
        # entered が 2 本目を出す。
        assert not gated.entered.acquire(timeout=0.5)
        assert len(gated.requests) == 1
        assert not done.is_set()
        gated.release.set()
        assert done.wait(timeout=WAIT_S)
    finally:
        gated.release.set()
        caller.join(timeout=WAIT_S)
    assert wait_for_state(client, "done", "failed")["state"] == "done"


# ------------------------------------------------------------------- body の検査


@pytest.mark.parametrize(
    ("body", "code"),
    [
        ({"stage": [4]}, "ywk_unknown_field"),
        ({"stages": 4}, "ywk_type_error"),
        ({"stages": ["4"]}, "ywk_type_error"),
        ({"stages": [0]}, "ywk_out_of_range"),
        ({"stages": [-4]}, "ywk_out_of_range"),
        ({"stages": [600]}, "ywk_out_of_range"),
        ({"stages": [1] * 13}, "ywk_out_of_range"),
        ({"voices": "ずんだもん"}, "ywk_type_error"),
        ({"voices": [""]}, "ywk_type_error"),
        ({"voices": [1]}, "ywk_type_error"),
        ({"text": 5}, "ywk_type_error"),
        ({"text": "あ" * 201}, "ywk_out_of_range"),
        ({"stages": [], "voices": []}, "ywk_out_of_range"),
    ],
)
def test_a_bad_warmup_body_is_400_with_a_code(client, body, code):
    response = client.post("/ywk/warmup", json=body)
    assert response.status_code == 400, response.text
    assert response.json()["error"]["code"] == code


def test_a_body_that_is_not_json_is_400(client):
    response = client.post("/ywk/warmup", content=b"{oops")
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "ywk_invalid_body"


def test_an_empty_text_falls_back_to_the_default(ywk):
    assert ywk.warmup_options({"text": "   "})["text"] == ywk.WARMUP_TEXT


# --------------------------------------------------------------- 起動時暖機の env


def test_warmup_on_start_is_off_by_default(ywk, monkeypatch):
    monkeypatch.delenv("YWK_WARMUP_ON_START", raising=False)
    assert ywk.warmup_on_start_options() is None


def test_warmup_on_start_reads_the_three_env(ywk, monkeypatch):
    monkeypatch.setenv("YWK_WARMUP_ON_START", "1")
    monkeypatch.setenv("YWK_WARMUP_STAGES", "4, 8")
    monkeypatch.setenv("YWK_WARMUP_VOICES", "デフォルト, vv_mochiko_sexy ,")
    monkeypatch.delenv("YWK_WARMUP_TEXT", raising=False)
    options = ywk.warmup_on_start_options()
    assert options == {
        "stages": [4.0, 8.0],
        "voices": ["デフォルト", "vv_mochiko_sexy"],
        "text": ywk.WARMUP_TEXT,
    }


def test_warmup_on_start_runs_at_ready(ywk, monkeypatch, baseline):
    from fastapi.testclient import TestClient

    monkeypatch.setenv("YWK_WARMUP_ON_START", "true")
    monkeypatch.setenv("YWK_WARMUP_STAGES", "4")
    monkeypatch.delenv("YWK_WARMUP_VOICES", raising=False)
    with TestClient(ywk.app, raise_server_exceptions=False) as client:
        final = wait_for_state(client, "done", "failed")
        assert final["state"] == "done", final
        assert final["shots_total"] == 1
    assert [request.seconds for request in baseline.requests] == [4.0]


# ------------------------------------------------------------------- 変種の欄


def test_status_names_the_variant(client, ywk):
    payload = client.get("/ywk/status").json()
    assert payload["variant"] == ywk.current_variant() == ywk_params.DEFAULT_VARIANT


def test_the_variant_default_is_one_spelling(ywk):
    assert ywk.DEFAULT_VARIANT == ywk_params.DEFAULT_VARIANT == "cuda"


def test_status_device_carries_hip_and_gcn_arch(client):
    device = client.get("/ywk/status").json()["device"]
    assert set(device) == set(ywk_params.STATUS_DEVICE_KEYS)
    # cpu なので両方 null（CUDA 機でも gcn_arch は null＝ROCm 専用の属性）。
    assert device["hip"] is None
    assert device["gcn_arch"] is None


def test_the_variant_is_read_from_the_env(ywk, monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    assert ywk.current_variant() == "rocm-gfx1151"
    assert ywk.variant_is_rocm() is True
    monkeypatch.setenv("YWK_VARIANT", "  ")
    assert ywk.current_variant() == "cuda"
    assert ywk.variant_is_rocm() is False


# ------------------------------------------------------- Radeon 変種＝bf16 固定


@pytest.mark.parametrize("precision", ["fp32", "float32", "FP32"])
@pytest.mark.parametrize("device", ["auto", "cuda", "cuda:0"])
def test_rocm_variant_exits_2_on_fp32(ywk, monkeypatch, precision, device):
    """decisions.md 5・36: fp32 は 32.1 s 対 bf16 2.84 s＝出してはいけない値."""
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.setenv("IRODORI_MODEL_DEVICE", device)
    monkeypatch.setenv("IRODORI_MODEL_PRECISION", precision)
    with pytest.raises(SystemExit) as excinfo:
        ywk.apply_variant_defaults()
    assert excinfo.value.code == 2


def test_rocm_variant_exits_2_on_a_fp32_codec(ywk, monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.setenv("IRODORI_CODEC_DEVICE", "auto")
    monkeypatch.setenv("IRODORI_CODEC_PRECISION", "fp32")
    with pytest.raises(SystemExit) as excinfo:
        ywk.apply_variant_defaults()
    assert excinfo.value.code == 2


def test_rocm_variant_allows_fp32_on_cpu(ywk, monkeypatch):
    """設計書 §2-3: cpu 指定は許す（上流は cpu×bf16 を ValueError で弾く）."""
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.setenv("IRODORI_MODEL_DEVICE", "cpu")
    monkeypatch.setenv("IRODORI_CODEC_DEVICE", "cpu")
    monkeypatch.setenv("IRODORI_MODEL_PRECISION", "fp32")
    monkeypatch.setenv("IRODORI_CODEC_PRECISION", "fp32")
    assert ywk.apply_variant_defaults()["variant"] == "rocm-gfx1151"


def test_rocm_variant_leaves_bf16_alone(ywk, monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.setenv("IRODORI_MODEL_DEVICE", "auto")
    monkeypatch.setenv("IRODORI_MODEL_PRECISION", "bf16")
    assert ywk.apply_variant_defaults()["variant"] == "rocm-gfx1151"


def test_the_cuda_variant_does_not_touch_precision(ywk, monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "cuda")
    monkeypatch.setenv("IRODORI_MODEL_PRECISION", "fp32")
    assert ywk.apply_variant_defaults() == {"variant": "cuda"}


# ------------------------------------------------------------ MIOpen の db の置き場


def test_rocm_variant_points_miopen_at_the_data_dir(ywk, monkeypatch, tmp_path):
    """C-7: db がプロセスを跨いで効くには置き場が固定されている必要がある."""
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.setenv("YWK_DATA_DIR", str(tmp_path))
    monkeypatch.delenv("MIOPEN_USER_DB_PATH", raising=False)
    monkeypatch.delenv("MIOPEN_CUSTOM_CACHE_DIR", raising=False)
    monkeypatch.delenv("IRODORI_MODEL_PRECISION", raising=False)
    monkeypatch.delenv("IRODORI_CODEC_PRECISION", raising=False)

    reported = ywk.apply_variant_defaults()
    import os

    assert os.environ["MIOPEN_USER_DB_PATH"] == str(tmp_path / "miopen" / "db")
    assert os.environ["MIOPEN_CUSTOM_CACHE_DIR"] == str(tmp_path / "miopen" / "cache")
    assert (tmp_path / "miopen" / "db").is_dir()
    assert (tmp_path / "miopen" / "cache").is_dir()
    assert reported["miopen_db"] == os.environ["MIOPEN_USER_DB_PATH"]


def test_the_launcher_can_move_the_miopen_db(ywk, monkeypatch, tmp_path):
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.setenv("YWK_DATA_DIR", str(tmp_path))
    monkeypatch.setenv("MIOPEN_USER_DB_PATH", str(tmp_path / "elsewhere"))
    monkeypatch.delenv("MIOPEN_CUSTOM_CACHE_DIR", raising=False)
    monkeypatch.delenv("IRODORI_MODEL_PRECISION", raising=False)
    monkeypatch.delenv("IRODORI_CODEC_PRECISION", raising=False)
    import os

    ywk.apply_variant_defaults()
    assert os.environ["MIOPEN_USER_DB_PATH"] == str(tmp_path / "elsewhere")


def test_the_cuda_variant_sets_no_miopen_env(ywk, monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "cuda")
    monkeypatch.delenv("MIOPEN_USER_DB_PATH", raising=False)
    monkeypatch.delenv("MIOPEN_CUSTOM_CACHE_DIR", raising=False)
    ywk.apply_variant_defaults()
    import os

    assert "MIOPEN_USER_DB_PATH" not in os.environ
    assert "MIOPEN_CUSTOM_CACHE_DIR" not in os.environ


# ------------------------------------------------- 是正席が足した題目（敵対検分）


def test_the_counter_is_held_for_the_whole_sse_stream(client, ywk, baseline):
    """優先度は SSE でも効く（契約 ⑺ 7-2 に例外は書かれていない）。

    上流は SSE のチャンクごとに ``_acquire_synthesis_slot`` を取り直す
    (``app.py:711``) ので、``StreamingResponse`` を返した時点でカウンタを下ろす
    と、1 本の本物の要求のチャンクの合間に暖機が**射の数だけ**割り込む。
    数え方は非 SSE と同じにする＝合成に入るたび pending は 1 以上。
    """
    seen: list[int] = []
    original = baseline.synthesize

    def counting(req: Any, *, log_fn: Any = None) -> Any:
        seen.append(ywk.pending_real_requests())
        return original(req, log_fn=log_fn)

    baseline.synthesize = counting
    response = client.post(
        "/v1/audio/speech",
        json={
            "model": MODEL,
            "input": "テスト。",
            "voice": DEFAULT_VOICE,
            "stream_format": "sse",
        },
    )
    assert response.status_code == 200, response.text
    assert response.headers["content-type"].startswith("text/event-stream")
    assert seen and all(n >= 1 for n in seen), seen
    # 流し終われば下りている（漏らさない）。
    assert wait_for(lambda: ywk.pending_real_requests() == 0)


def test_the_sse_counter_comes_down_even_when_the_stream_errors(client, ywk, baseline):
    """射が例外を投げても body_iterator の finally で必ず下ろす。"""

    def boom(req: Any, *, log_fn: Any = None) -> Any:
        raise RuntimeError("stream blew up")

    baseline.synthesize = boom
    response = client.post(
        "/v1/audio/speech",
        json={
            "model": MODEL,
            "input": "テスト。",
            "voice": DEFAULT_VOICE,
            "stream_format": "sse",
        },
    )
    assert response.status_code == 200, response.text
    assert "event: error" in response.text
    assert wait_for(lambda: ywk.pending_real_requests() == 0)


def test_a_warmup_shot_resolves_the_guaranteed_speaker_like_a_real_request(
    client, baseline
):
    """decisions.md 45: 「デフォルト」は voices.json が無くても 200。

    暖機の射も本番と同じ解決（``resolve_default_voice``）を通らなければ、同じ
    話者 id が本番 200・暖機 400 という二枚舌になり、run 全体が failed で止まる
    （契約 ⑺ 7-2 の例文そのもの）。
    """
    write_voices(aliases={})  # 初回＝launcher がまだ voices.json を書いていない
    real = client.post(
        "/v1/audio/speech",
        json={"model": MODEL, "input": "テスト。", "voice": DEFAULT_VOICE},
    )
    assert real.status_code == 200, real.text

    response = client.post("/ywk/warmup", json={"stages": [], "voices": [DEFAULT_VOICE]})
    assert response.status_code == 202, response.text
    final = wait_for_state(client, "done", "failed")
    assert final["state"] == "done", final
    assert final["shots_done"] == 1
    # 射は参照無しに書き換わっている（本番の 200 と同じ形）。
    assert len(baseline.requests) == 2
    assert baseline.requests[-1].no_ref is True


def test_a_warmup_run_does_not_stop_at_the_guaranteed_speaker(client, baseline):
    """1 射目が 400 で全滅すると後続の話者が焼かれない。その回帰止め。"""
    write_voices(aliases={JA_VOICE: {"ref_wav": "alice.wav"}}, files=("alice.wav",))
    response = client.post(
        "/ywk/warmup", json={"stages": [4], "voices": [DEFAULT_VOICE, JA_VOICE]}
    )
    assert response.status_code == 202, response.text
    final = wait_for_state(client, "done", "failed")
    assert final["state"] == "done", final
    assert final["shots_done"] == 3
    assert final["shots_total"] == 3
    assert len(baseline.requests) == 3


class _FakeTorchVersion:
    def __init__(self, hip: Any) -> None:
        self.hip = hip


class _FakeTorch:
    """Just enough torch for the mismatch warning (no device is touched)."""

    def __init__(self, hip: Any) -> None:
        self.version = _FakeTorchVersion(hip)

    class cuda:  # noqa: N801 -- mirrors the module it stands in for
        @staticmethod
        def is_available() -> bool:
            return False

        @staticmethod
        def device_count() -> int:
            return 0


def test_a_rocm_torch_under_the_cuda_label_is_warned_about(ywk, monkeypatch):
    """変種は札であって能力ではない（是正席の指摘）。

    札を載せ忘れた Radeon 機では fp32 の exit 2 も MIOpen の付け替えも黙って
    消える。torch は既に import 済みなので直せないが、1 行は出せる。
    """
    monkeypatch.delenv("YWK_VARIANT", raising=False)
    line = ywk._warn_if_rocm_torch_is_untagged(_FakeTorch("7.15.26333"))
    assert line is not None
    assert "hip=7.15.26333" in line
    assert "YWK_VARIANT=cuda" in line
    assert "bf16" in line


def test_the_warning_is_silent_when_the_label_matches(ywk, monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    assert ywk._warn_if_rocm_torch_is_untagged(_FakeTorch("7.15.26333")) is None


def test_the_warning_is_silent_on_a_torch_that_is_not_rocm(ywk, monkeypatch):
    monkeypatch.delenv("YWK_VARIANT", raising=False)
    assert ywk._warn_if_rocm_torch_is_untagged(_FakeTorch(None)) is None
