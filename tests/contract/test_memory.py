"""裁定 87 ⑴・⑵: ``/ywk/status.memory`` と ``device.pci_bus_id`` の型.

Why it exists (裁定 67 ⑵⑶): the launcher polls ``/ywk/status`` every two seconds
and draws 使用量 (``allocated``), 占有量 (``reserved``) and 話者ごとの潜在の大きさ
(``latents``) from this one field.  ``gpu_used``/``gpu_total`` are still served
here unchanged, but since 裁定 110 the launcher no longer shows them as "the
whole card" -- it reads the Windows GPU counters for that instead.
Nothing here loads a model -- ``FakeRuntime`` names the device and the
four torch entry points are stood in for, because what has to be proved is the
*shape and the failure behaviour*, not the driver: that CPU and 未読込 answer
``null`` rather than a wrong number, that a speaker who was never baked is
absent instead of ``0``, that a probe which throws costs one line in
``memory.error`` and not the whole status route, and that ``pci_bus_id`` leaves
here as a **string on every machine**.

That last one is about torch's type, not about the machine: ``pci_bus_id`` is an
``int`` on both builds (gfx1151 実射 ``197``・RTX 3090 実射 ``1``＝decisions.md
75 ⑷) because CUDA and ROCm share one C++ binding -- torch's own code formats it
with ``f"{bus:02x}"`` (``torch/numa/binding.py``) and the type stub does not even
declare the attribute.  The wrapper's ``str()`` is what keeps the launcher's one
field from being two types; 便 D（2）の是正で契約 ⑹ の「CUDA 側は文字列」という
1 行を実測に直した（席が足した推測が正典に入っていた）。
"""

from __future__ import annotations

import time
from pathlib import Path
from typing import Any

import pytest
import torch
from conftest import DEFAULT_VOICE, JA_FILE_VOICE, JA_VOICE, VOICES, write_voices

import ywk_server

#: 裁定 87 ⑴ の逐語（この並びで・この 10 欄）.
MEMORY_KEYS = [
    "device",
    "allocated",
    "reserved",
    "max",
    "gpu_total",
    "gpu_free",
    "gpu_used",
    "latents",
    "latents_total",
    "sampled_at",
]
NUMERIC_KEYS = ["allocated", "reserved", "max", "gpu_total", "gpu_free", "gpu_used"]

#: Numbers the fake allocator answers with -- distinct so a mix-up shows.
ALLOCATED = 1_768_000_000
RESERVED = 3_030_000_000
PEAK = 3_248_000_000
GPU_TOTAL = 107_000_000_000
GPU_FREE = 96_000_000_000


# ---------------------------------------------------------------- 道具


class FakeProps:
    """``get_device_properties``: ``pci_bus_id`` is an int on *both* builds."""

    name = "AMD Radeon(TM) 8060S Graphics"
    uuid = "GPU-0000000000000000"
    pci_bus_id = 1
    gcnArchName = "gfx1151"  # noqa: N815 -- the upstream attribute's own spelling


def memory_of(client) -> dict[str, Any]:
    response = client.get("/ywk/status")
    assert response.status_code == 200
    return response.json()["memory"]


def fake_gpu(monkeypatch, runtime, *, props: Any = None) -> None:
    """Put the fake runtime on ``cuda:0`` and stand in for the four probes."""
    runtime.model_device = torch.device("cuda", 0)
    monkeypatch.setattr(
        torch.cuda, "get_device_properties", lambda _i: props or FakeProps(), raising=False
    )
    monkeypatch.setattr(torch.cuda, "memory_allocated", lambda _i: ALLOCATED, raising=False)
    monkeypatch.setattr(torch.cuda, "memory_reserved", lambda _i: RESERVED, raising=False)
    monkeypatch.setattr(torch.cuda, "max_memory_allocated", lambda _i: PEAK, raising=False)
    monkeypatch.setattr(
        torch.cuda, "mem_get_info", lambda _i: (GPU_FREE, GPU_TOTAL), raising=False
    )


def bake(voice_id: str, size: int) -> Path:
    """A ``latents/<stem>.pt`` of exactly ``size`` bytes.

    The field only ``stat``s the file, so the bytes are filler: what is under
    test is that the *size* is looked up by speaker id through
    :func:`ywk_server.latent_stem`, which is the same stem the precompute writes.
    """
    path = VOICES / ywk_server.PRECOMPUTE_SUBDIR / f"{ywk_server.latent_stem(voice_id)}.pt"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"\x00" * size)
    return path


# ---------------------------------------------------------------- 形


def test_memory_carries_the_ten_fields_of_the_ruling(client):
    """裁定 87 ⑴ の逐語＝10 欄。健全なときは ``error`` を足さない。"""
    memory = memory_of(client)
    assert list(memory) == MEMORY_KEYS


def test_memory_is_null_on_cpu(client, baseline):
    """device が cpu のとき数値 6 欄は null（0 ではない）＝「測っていない」の意。"""
    memory = memory_of(client)
    assert memory["device"] == "cpu"
    for key in NUMERIC_KEYS:
        assert memory[key] is None, key
    assert memory["latents"] == {}
    assert memory["latents_total"] == 0


def test_memory_is_null_when_nothing_is_loaded(client, unloaded):
    memory = memory_of(client)
    assert memory["device"] is None
    for key in NUMERIC_KEYS:
        assert memory[key] is None, key


def test_memory_stamps_every_sample(client):
    """毎 2 秒叩かれる欄なので、いつ測ったかが要る（ISO 8601・UTC）。"""
    stamp = memory_of(client)["sampled_at"]
    assert stamp.endswith("Z")
    assert stamp[4] == "-" and stamp[10] == "T"


def test_memory_carries_no_absolute_path(client):
    write_voices(aliases={DEFAULT_VOICE: {"no_ref": True}}, files=("alice.wav",))
    bake("alice", 4096)
    body = str(memory_of(client))
    assert ":\\" not in body and ":/" not in body


# ---------------------------------------------------------------- GPU 模擬


def test_memory_reports_numbers_on_a_gpu(client, baseline, monkeypatch):
    fake_gpu(monkeypatch, baseline)
    memory = memory_of(client)
    assert memory["device"] == "cuda:0"
    assert memory["allocated"] == ALLOCATED
    assert memory["reserved"] == RESERVED
    assert memory["max"] == PEAK
    assert memory["gpu_total"] == GPU_TOTAL
    assert memory["gpu_free"] == GPU_FREE
    assert "error" not in memory


def test_gpu_used_is_the_whole_card(client, baseline, monkeypatch):
    """``gpu_used == gpu_total - gpu_free``＝allocator の値とは別物.

    裁定 110（2026-09-08）＝**「カード全体（他プロセス込み）」という読みは撤回された**
    （Windows の ROCm では自プロセス相当で、他プロセスを含まない＝実測は
    ``docs/design/ben-d-launcher.md`` §27）。ここで釘付けするのは **JSON の算術**だけで、
    wrapper の側は何も変わっていない（欄も出所も据え置き）。名前は台帳の履歴として残す。
    """
    fake_gpu(monkeypatch, baseline)
    memory = memory_of(client)
    assert memory["gpu_used"] == GPU_TOTAL - GPU_FREE
    assert memory["gpu_used"] > memory["reserved"]


def test_memory_uses_the_current_device_when_the_index_is_implicit(
    client, baseline, monkeypatch
):
    """``torch.device("cuda")``（index なし）でも数値が出る。"""
    fake_gpu(monkeypatch, baseline)
    baseline.model_device = torch.device("cuda")
    monkeypatch.setattr(torch.cuda, "current_device", lambda: 0, raising=False)
    assert memory_of(client)["allocated"] == ALLOCATED


# ---------------------------------------------------------------- latents


def test_latents_are_keyed_by_speaker_id(client):
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "zunda.wav"}},
        files=("zunda.wav",),
    )
    bake(JA_VOICE, 100_356)
    memory = memory_of(client)
    assert memory["latents"] == {JA_VOICE: 100_356}
    assert memory["latents_total"] == 100_356


def test_unbaked_speakers_are_absent_not_zero(client):
    """焼いていない話者は載せない（0 と「まだ焼いていない」を混ぜない）。"""
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "zunda.wav"}},
        files=("zunda.wav", "alice.wav"),
    )
    bake(JA_VOICE, 512)
    memory = memory_of(client)
    assert list(memory["latents"]) == [JA_VOICE]
    assert DEFAULT_VOICE not in memory["latents"]
    assert "alice" not in memory["latents"]


def test_latents_total_sums_every_baked_speaker(client):
    write_voices(
        aliases={
            DEFAULT_VOICE: {"no_ref": True},
            JA_VOICE: {"ref_wav": "zunda.wav"},
            JA_FILE_VOICE: {"ref_wav": "metan.wav"},
        },
        files=("zunda.wav", "metan.wav"),
    )
    bake(JA_VOICE, 1_000)
    bake(JA_FILE_VOICE, 2_500)
    memory = memory_of(client)
    assert memory["latents"] == {JA_VOICE: 1_000, JA_FILE_VOICE: 2_500}
    assert memory["latents_total"] == 3_500


def test_latents_are_reported_even_on_cpu(client, baseline):
    """潜在の大きさは device に依らない＝未読込でも読める（裁定 87 ⑴）。"""
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "zunda.wav"}},
        files=("zunda.wav",),
    )
    bake(JA_VOICE, 777)
    memory = memory_of(client)
    assert memory["device"] == "cpu"
    assert memory["allocated"] is None
    assert memory["latents"] == {JA_VOICE: 777}


# ---------------------------------------------------------------- pci_bus_id


def test_pci_bus_id_is_a_string(client, baseline, monkeypatch):
    """裁定 87 ⑵: ROCm は int を返す＝wrapper が ``str()`` を掛ける（契約 ⑹）。"""
    fake_gpu(monkeypatch, baseline)
    device = client.get("/ywk/status").json()["device"]
    assert device["pci_bus_id"] == "1"
    assert isinstance(device["pci_bus_id"], str)
    assert device["gcn_arch"] == "gfx1151"


def test_pci_bus_id_stays_null_when_unknown(client, unloaded):
    assert client.get("/ywk/status").json()["device"]["pci_bus_id"] is None


# ---------------------------------------------------------------- 失敗


def test_a_failed_probe_is_one_line_and_still_200(client, baseline, monkeypatch):
    """取得に失敗しても ``/ywk/status`` は 200 のまま＝理由は memory の中に 1 行。"""
    fake_gpu(monkeypatch, baseline)

    def boom(_index: int) -> int:
        raise RuntimeError("HIP error: invalid device ordinal")

    monkeypatch.setattr(torch.cuda, "memory_allocated", boom, raising=False)
    response = client.get("/ywk/status")
    assert response.status_code == 200
    memory = response.json()["memory"]
    assert memory["error"].count("\n") == 0
    assert "RuntimeError: HIP error: invalid device ordinal" == memory["error"]
    for key in NUMERIC_KEYS:
        assert memory[key] is None, key
    assert memory["device"] == "cuda:0"


def test_a_failed_probe_keeps_the_rest_of_the_status(client, baseline, monkeypatch):
    fake_gpu(monkeypatch, baseline)
    monkeypatch.setattr(
        torch.cuda,
        "mem_get_info",
        lambda _i: (_ for _ in ()).throw(RuntimeError("no context")),
        raising=False,
    )
    payload = client.get("/ywk/status").json()
    assert payload["runtime"]["loaded"] is True
    assert payload["warmup"]["state"] == "idle"
    # The allocator numbers were already read when mem_get_info threw.
    assert payload["memory"]["allocated"] == ALLOCATED
    assert payload["memory"]["gpu_total"] is None
    assert payload["memory"]["error"] == "RuntimeError: no context"


def test_a_failed_latent_scan_folds_the_path(client, monkeypatch):
    """error の 1 行にも絶対パスを出さない（⑶ 3-3 と同じ規則）。"""

    def boom() -> Path:
        raise OSError("C:\\Users\\someone\\voices\\latents is gone")

    monkeypatch.setattr(ywk_server, "latents_dir", boom)
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "zunda.wav"}},
        files=("zunda.wav",),
    )
    memory = memory_of(client)
    assert "<path>" in memory["error"]
    assert "someone" not in memory["error"]
    assert memory["latents"] == {}
    assert memory["latents_total"] == 0


def test_an_unreadable_latent_is_skipped_not_fatal(client, monkeypatch):
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "zunda.wav"}},
        files=("zunda.wav",),
    )
    # A directory where the .pt should be: stat succeeds but it is not ours to
    # size, so the speaker is simply absent rather than an exception.
    path = VOICES / ywk_server.PRECOMPUTE_SUBDIR / f"{ywk_server.latent_stem(JA_VOICE)}.pt"
    path.parent.mkdir(parents=True, exist_ok=True)
    memory = memory_of(client)
    assert memory["latents"] == {}
    assert "error" not in memory


# ---------------------------------------------------------------- 速さ


def test_memory_snapshot_is_cheap_enough_for_a_two_second_poll(ywk, baseline):
    """毎 2 秒叩かれる欄＝1 回が数 ms。ここは stat の本数だけを見る。"""
    write_voices(
        aliases={DEFAULT_VOICE: {"no_ref": True}, JA_VOICE: {"ref_wav": "zunda.wav"}},
        files=("zunda.wav",),
    )
    bake(JA_VOICE, 2048)
    ids = [DEFAULT_VOICE, JA_VOICE]
    start = time.perf_counter()
    for _ in range(50):
        ywk.memory_snapshot(torch.device("cpu"), ids)
    elapsed_ms = (time.perf_counter() - start) * 1000 / 50
    assert elapsed_ms < 20.0, elapsed_ms


@pytest.mark.parametrize("voice_ids", [None, [], ()])
def test_memory_snapshot_takes_an_empty_speaker_list(ywk, voice_ids):
    memory = ywk.memory_snapshot(None, voice_ids)
    assert memory["latents"] == {}
    assert memory["latents_total"] == 0
    assert memory["device"] is None
