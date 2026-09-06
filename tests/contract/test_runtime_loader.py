"""裁定 105（便 G＝ポート先行）＝bind してから裏でモデルを載せる。

焼く `IRODORI_PRELOAD` は `false`（`test_env_and_devices.py`）で、上流の
`startup()` は何も載せずに返る。読込は `ywk_lifespan` が **yield の前に**起こす
1 本の糸が担い、その糸が上流の `runtime_manager.get()` をそのまま呼ぶ
（＝読込中に来た合成要求は上流の遅延読込に合流して待つ・裁定 105 ⑶）。

ここで釘を打つのは 4 つ：

⑴ 起動の後に糸が走り、`runtime.loaded` が真になる。
⑵ `get()` が落ちる個体は **`loaded=false`・`loading=false`・`error` に理由 1 行**で、
   `/health` も `/ywk/status` も 200 のまま＝**プロセスは生きている**（裁定 105 ⑴）。
⑶ 事前計算・暖機・`device actual=` の 1 行は **読込の成功後にしか走らない**（105 ⑵）。
⑷ 糸は 1 本きり＝何度呼んでも読込は 1 回（`get()` の呼び出し回数で見る）。

モデルは 1 度も載せない＝`conftest` の偽物を、この檔の中でだけ「遅い」「落ちる」
に差し替える。
"""

from __future__ import annotations

import threading
import time
from typing import Any

import pytest
from fastapi.testclient import TestClient

from irodori_openai_tts import app as upstream

import ywk_server
from conftest import DEFAULT_VOICE, JA_VOICE, FakeRuntime, speech_body, write_voices

#: 上流が投げる形そのまま（絶対パスつき＝⑹「絶対パスは出さない」の釘も兼ねる）。
CHECKPOINT_MISSING = "Checkpoint not found: C:\\Users\\someone\\models\\v4.1-small.safetensors"


class RecordingManager:
    """上流の `RuntimeManager` の代役。`get()` の回数と旗の遷移を憶える。

    旗の意味は上流と同じ（`runtime.py:72-78`）＝`is_loading` は「まだ載って
    いないのに `get()` の中に居る」。ここは鍵ではなく数で表す（合流そのものを
    見るのは `LockingManager` の受け持ち）。
    """

    def __init__(self, runtime: Any | None = None, *, fail: str | None = None) -> None:
        self.runtime = runtime
        self._runtime: Any | None = None
        self.checkpoint_path = None
        self.fail = fail
        self.calls = 0
        self.entered = threading.Event()
        self.gate = threading.Event()
        self.gate.set()

    @property
    def is_loaded(self) -> bool:
        return self._runtime is not None

    @property
    def is_loading(self) -> bool:
        return self._runtime is None and self.entered.is_set() and not self.gate.is_set()

    def get(self) -> Any:
        self.calls += 1
        self.entered.set()
        self.gate.wait(20)
        if self.fail is not None:
            raise RuntimeError(self.fail)
        self._runtime = self.runtime
        return self.runtime


class LockingManager:
    """上流の `RuntimeManager` を**鍵ごと**写した代役（`runtime.py:31-66`）。

    `RecordingManager` は旗を数で表すので、「読込中に来た合成要求が同じ読込へ
    合流する」という裁定 105 ⑶ の眼目そのものは見られない。此方は上流と同じ形
    ＝`_runtime` が空なら鍵を取り、`is_loading` は `_runtime is None and
    lock.locked()`。載せに行った回数（`loads`）が 1 のままなら合流している。
    """

    def __init__(self, runtime: Any | None = None, *, fail: str | None = None) -> None:
        self.runtime = runtime
        self._runtime: Any | None = None
        self.checkpoint_path = None
        self.fail = fail
        #: **実際に載せに行った**回数（早戻りは数えない＝上流と同じ）。
        self.loads = 0
        self._lock = threading.Lock()
        self.entered = threading.Event()
        self.gate = threading.Event()
        self.gate.set()

    @property
    def is_loaded(self) -> bool:
        return self._runtime is not None

    @property
    def is_loading(self) -> bool:
        return self._runtime is None and self._lock.locked()

    def get(self) -> Any:
        if self._runtime is not None:
            return self._runtime
        with self._lock:
            if self._runtime is None:
                self.loads += 1
                self.entered.set()
                self.gate.wait(20)
                if self.fail is not None:
                    raise RuntimeError(self.fail)
                self._runtime = self.runtime
            return self._runtime


@pytest.fixture()
def quiet_start(monkeypatch: Any) -> None:
    """暖機・事前計算の起動時実行を既定（どちらも切）に戻す。"""
    monkeypatch.delenv("YWK_WARMUP_ON_START", raising=False)
    monkeypatch.delenv("YWK_PRECOMPUTE_ON_START", raising=False)
    monkeypatch.setenv("YWK_VARIANT", "cuda")


def _wait_loader(timeout: float = 20.0) -> bool:
    return ywk_server._loader_done.wait(timeout)


# ------------------------------------------------------------------ ⑴ 成功の路


def test_the_loader_runs_after_startup_and_the_runtime_becomes_loaded(quiet_start):
    manager = RecordingManager(FakeRuntime())
    upstream.runtime_manager = manager

    with TestClient(ywk_server.app, raise_server_exceptions=False) as client:
        assert _wait_loader(), "the loader thread did not finish"
        assert manager.calls == 1
        payload = client.get("/ywk/status").json()
        assert payload["runtime"] == {"loaded": True, "loading": False, "error": None}
        assert client.get("/health").status_code == 200


def test_the_port_answers_while_the_model_is_still_loading(quiet_start):
    """`/health` 200 ＋ `loading=true` の窓が本当に在る（受け入れ＝起動 ≤ 3 s）。"""
    manager = RecordingManager(FakeRuntime())
    manager.gate.clear()
    upstream.runtime_manager = manager

    with TestClient(ywk_server.app, raise_server_exceptions=False) as client:
        assert manager.entered.wait(10), "the loader never called get()"
        assert client.get("/health").status_code == 200
        status = client.get("/ywk/status")
        assert status.status_code == 200
        assert status.json()["runtime"] == {
            "loaded": False,
            "loading": True,
            "error": None,
        }
        manager.gate.set()
        assert _wait_loader()
        assert client.get("/ywk/status").json()["runtime"]["loaded"] is True


# ------------------------------------------------------------------ ⑵ 失敗の路


def test_a_failed_load_leaves_the_process_alive_with_the_reason(quiet_start):
    manager = RecordingManager(fail=CHECKPOINT_MISSING)
    upstream.runtime_manager = manager

    with TestClient(ywk_server.app, raise_server_exceptions=False) as client:
        assert _wait_loader()
        assert client.get("/health").status_code == 200
        status = client.get("/ywk/status")
        assert status.status_code == 200
        runtime = status.json()["runtime"]
        assert runtime["loaded"] is False
        assert runtime["loading"] is False
        assert runtime["error"]
        # 理由は 1 行で、絶対パスを含まない（⑶ 3-3・⑹ と同じ規則）。
        assert "\n" not in runtime["error"]
        assert "Checkpoint not found" in runtime["error"]
        assert ":\\" not in status.text and ":/" not in status.text


def test_a_failed_load_is_told_on_stderr(quiet_start, capfd):
    upstream.runtime_manager = RecordingManager(fail=CHECKPOINT_MISSING)

    with TestClient(ywk_server.app, raise_server_exceptions=False):
        assert _wait_loader()

    err = capfd.readouterr().err
    assert "ywk_server: runtime load started" in err
    assert "ywk_server: runtime load failed:" in err
    assert "Checkpoint not found" in err


# --------------------------------------------- ⑶ 事前計算・暖機は成功後にしか走らない


def test_a_failed_load_runs_neither_precompute_nor_warmup(monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")  # 事前計算の既定 ON
    monkeypatch.setenv("YWK_WARMUP_ON_START", "1")
    monkeypatch.setenv("YWK_WARMUP_STAGES", "4")
    monkeypatch.delenv("YWK_PRECOMPUTE_ON_START", raising=False)
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "zunda.wav"}},
        files=("zunda.wav",),
    )
    upstream.runtime_manager = RecordingManager(fail=CHECKPOINT_MISSING)

    with TestClient(ywk_server.app, raise_server_exceptions=False) as client:
        assert _wait_loader()
        payload = client.get("/ywk/status").json()
        assert payload["precompute"]["state"] == "idle"
        assert payload["precompute"]["id"] is None
        assert payload["warmup"]["state"] == "idle"
        assert payload["warmup"]["shots"] == 0


def test_precompute_on_start_does_not_run_before_the_load_succeeds(monkeypatch):
    monkeypatch.setenv("YWK_VARIANT", "rocm-gfx1151")
    monkeypatch.delenv("YWK_PRECOMPUTE_ON_START", raising=False)
    monkeypatch.delenv("YWK_WARMUP_ON_START", raising=False)
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "zunda.wav"}},
        files=("zunda.wav",),
    )
    manager = RecordingManager(FakeRuntime())
    manager.gate.clear()
    upstream.runtime_manager = manager

    with TestClient(ywk_server.app, raise_server_exceptions=False) as client:
        assert manager.entered.wait(10)
        # 読込中は 1 件も焼き始めていない（105 ⑵）。
        assert client.get("/ywk/status").json()["precompute"]["state"] == "idle"
        manager.gate.set()
        assert _wait_loader()

    # 成功の後にだけ起きる（走り切るかは test_precompute.py の受け持ち）。
    assert manager.calls == 1


def test_the_device_line_waits_for_a_successful_load(quiet_start, capfd):
    upstream.runtime_manager = RecordingManager(fail=CHECKPOINT_MISSING)
    ywk_server._device_logged = False

    with TestClient(ywk_server.app, raise_server_exceptions=False):
        assert _wait_loader()

    assert "device actual=" not in capfd.readouterr().err
    assert ywk_server._device_logged is False


# ------------------------------------------------------------------ ⑷ 冪等


def test_the_loader_never_loads_twice(quiet_start):
    manager = RecordingManager(FakeRuntime())
    upstream.runtime_manager = manager

    with TestClient(ywk_server.app, raise_server_exceptions=False):
        assert _wait_loader()
        first = ywk_server._loader_thread
        for _ in range(5):
            assert ywk_server.start_runtime_loader() is first
        assert manager.calls == 1


def test_a_second_run_gets_its_own_loader(quiet_start):
    """`reset_runtime_loader` があるので 2 個目の TestClient でも糸は起きる。"""
    manager = RecordingManager(FakeRuntime())
    upstream.runtime_manager = manager

    with TestClient(ywk_server.app, raise_server_exceptions=False):
        assert _wait_loader()
    assert manager.calls == 1

    manager._runtime = None
    with TestClient(ywk_server.app, raise_server_exceptions=False):
        assert _wait_loader()
    assert manager.calls == 2


# --------------------------------------- ⑸ 合成要求は読込へ**合流**する（105 ⑶）


def test_a_speech_request_during_the_load_joins_it_instead_of_loading_twice(quiet_start):
    """読込中に撃った合成が、**同じ読込**を待って 200 で返る（裁定 105 ⑶）。

    上流の `create_speech` は `runtime_manager.get()` を糸池へ逃がす
    （`app.py:353`）ので、糸は 2 本＝裏読込と合成の 2 つが同じ鍵の前に並ぶ。
    載せに行った回数が 1 のままなら**二重に載っていない**。
    """
    manager = LockingManager(FakeRuntime())
    manager.gate.clear()
    upstream.runtime_manager = manager

    with TestClient(ywk_server.app, raise_server_exceptions=False) as client:
        assert manager.entered.wait(10), "the loader never called get()"
        assert manager.is_loading is True

        shot: dict[str, Any] = {}

        def fire() -> None:
            shot["response"] = client.post("/v1/audio/speech", json=speech_body())

        caller = threading.Thread(target=fire, name="ywk-test-speech", daemon=True)
        caller.start()
        # 合成の糸が `get()` の鍵の前まで進むだけの間。
        time.sleep(0.4)
        assert manager.loads == 1, "the speech request started a second load"

        manager.gate.set()
        caller.join(30)
        assert _wait_loader()

    assert manager.loads == 1
    assert shot["response"].status_code == 200


def test_a_speech_request_after_a_failed_load_keeps_the_existing_shaping(quiet_start):
    """失敗した後の合成は**今まで通りの形**で断る（契約 ⑶ 3-3・裁定 105 ⑵）。

    新しい `code` は作らない＝`_ensure_error_code` が既に付けている物を確かめる
    だけ。同時に、遅延読込の路が `runtime.error` を**上書きしない・畳んだ 1 行の
    まま**であることも見る（是正・便 G）。
    """
    manager = LockingManager(fail=CHECKPOINT_MISSING)
    upstream.runtime_manager = manager

    with TestClient(ywk_server.app, raise_server_exceptions=False) as client:
        assert _wait_loader()
        before = client.get("/ywk/status").json()["runtime"]["error"]
        assert before and "<path>" in before

        response = client.post("/v1/audio/speech", json=speech_body())
        assert response.status_code >= 500
        error = response.json()["error"]
        assert error["code"] == "ywk_server_error"
        # 断りの本文にも絶対パスは出ない（⑹）。
        assert ":\\" not in response.text and ":/" not in response.text

        status = client.get("/ywk/status")
        assert status.status_code == 200
        runtime = status.json()["runtime"]
        assert runtime["loaded"] is False
        assert runtime["error"] == before
        assert ":\\" not in status.text and ":/" not in status.text


# ------------------ ⑹ `runtime.error` は「載らなかった」以外を意味しない（是正・便 G）


def test_a_synthesis_failure_on_a_loaded_server_never_sets_runtime_error(client, ywk):
    """載っている個体の 5xx は `runtime.error` を汚さない。

    裁定 105 ⑷ でこの欄はランチャの**終端状態** Failed の材料になった。1 回の
    合成の失敗（CUDA OOM・上流の「待ち行列が一杯」503）で健全な個体が永久に
    「モデルの読込に失敗＝…」と名乗るのを止める釘である。
    """
    original = ywk._upstream_create_speech

    async def boom(payload: Any) -> Any:
        raise RuntimeError("CUDA out of memory")

    ywk._upstream_create_speech = boom
    try:
        assert client.post("/v1/audio/speech", json=speech_body()).status_code >= 500
    finally:
        ywk._upstream_create_speech = original

    runtime = client.get("/ywk/status").json()["runtime"]
    assert runtime == {"loaded": True, "loading": False, "error": None}


def test_the_lazy_writer_folds_the_absolute_path_away(client, ywk):
    """未読込の個体の 5xx は記録するが、**畳んだ 1 行**でしか記録しない。

    `/ywk/status` は 200 なので `ywk_scrub_errors` は本文を見ない＝ここで畳んで
    おかないと、ランチャの状態帯（裁定 105 ⑷）に利用者の名前が出る。
    """
    upstream.runtime_manager = RecordingManager(fail=CHECKPOINT_MISSING)
    original = ywk._upstream_create_speech

    async def boom(payload: Any) -> Any:
        raise FileNotFoundError(CHECKPOINT_MISSING)

    ywk._upstream_create_speech = boom
    try:
        assert client.post("/v1/audio/speech", json=speech_body()).status_code >= 500
    finally:
        ywk._upstream_create_speech = original

    status = client.get("/ywk/status")
    assert status.status_code == 200
    error = status.json()["runtime"]["error"]
    assert error and "<path>" in error
    assert ":\\" not in status.text and ":/" not in status.text


def test_a_stale_loader_does_not_write_into_the_next_run(quiet_start):
    """停止の join を生き延びた糸は、**次の走行**の欄を触らない（是正・便 G）。

    本物の読込は 20〜70 s で、停止側の join は 2 s しか待たない＝はぐれた糸は
    普通に出る。旗と合図を走行ごとに作り、書く前に「自分は今の糸か」を確かめる。
    """
    slow = RecordingManager(fail=CHECKPOINT_MISSING)
    slow.gate.clear()
    upstream.runtime_manager = slow

    with TestClient(ywk_server.app, raise_server_exceptions=False):
        assert slow.entered.wait(10)
    stale = ywk_server._loader_thread  # 走行は終わったが糸はまだ `get()` の中

    healthy = RecordingManager(FakeRuntime())
    upstream.runtime_manager = healthy
    with TestClient(ywk_server.app, raise_server_exceptions=False) as client:
        assert _wait_loader()
        assert ywk_server._loader_thread is not stale
        # ここではぐれた糸を起こす＝理由を書きに来る番。
        slow.gate.set()
        if stale is not None:
            stale.join(10)
        payload = client.get("/ywk/status").json()
        assert payload["runtime"] == {"loaded": True, "loading": False, "error": None}
