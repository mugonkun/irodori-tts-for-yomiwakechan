"""Design §2 / §4-6: env の焼き込みと精度の device 連動.

``get_settings`` is ``lru_cache``'d at upstream import time, so anything the
wrapper wants to change has to be in ``os.environ`` *before* that import.  The
device check runs even earlier: ``cuda`` on a machine without CUDA must exit 2
in milliseconds instead of failing six to eleven seconds into the model load,
and ``bf16`` on cpu is rejected outright by
``resolve_runtime_dtype`` (inference_runtime.py:304-312).
"""

from __future__ import annotations

import os

import pytest


def test_env_defaults_are_setdefault_not_overwrite(ywk, monkeypatch):
    monkeypatch.setenv("IRODORI_PORT", "19999")
    ywk.apply_env_defaults()
    assert os.environ["IRODORI_PORT"] == "19999"


def test_env_defaults_fill_in_the_baked_values(ywk, monkeypatch):
    for name in (
        "IRODORI_HOST",
        "IRODORI_PORT",
        "IRODORI_HF_CHECKPOINT",
        "IRODORI_PRELOAD",
        "IRODORI_EMPTY_CACHE_INTERVAL",
        "IRODORI_ALLOW_NO_REF_VOICE",
        "IRODORI_DEFAULT_NUM_STEPS",
        "IRODORI_DEFAULT_RESPONSE_FORMAT",
    ):
        monkeypatch.delenv(name, raising=False)
    ywk.apply_env_defaults()
    assert os.environ["IRODORI_HOST"] == "127.0.0.1"
    assert os.environ["IRODORI_PORT"] == "18088"
    assert os.environ["IRODORI_HF_CHECKPOINT"] == "Aratako/Irodori-TTS-v4.1-Small"
    # 裁定 105（便 G＝ポート先行）＝裁定 7 の `preload=true` を覆した。上流の
    # startup がモデルを載せると bind が 20〜70 s 遅れるので、焼くのは `false` で、
    # 読込は `ywk_lifespan` が起こす裏の糸が担う（`tests/contract/test_runtime_loader.py`）。
    assert os.environ["IRODORI_PRELOAD"] == "false"
    assert os.environ["IRODORI_EMPTY_CACHE_INTERVAL"] == "0"
    assert os.environ["IRODORI_ALLOW_NO_REF_VOICE"] == "false"
    assert os.environ["IRODORI_DEFAULT_NUM_STEPS"] == "40"
    assert os.environ["IRODORI_DEFAULT_RESPONSE_FORMAT"] == "wav"


def test_the_baked_env_reached_the_live_settings(client):
    from irodori_openai_tts import app as upstream

    assert upstream.settings.host == "127.0.0.1"
    assert upstream.settings.port == 18088
    assert upstream.settings.hf_checkpoint == "Aratako/Irodori-TTS-v4.1-Small"
    assert upstream.settings.empty_cache_interval == 0
    assert upstream.settings.allow_no_ref_voice is False
    assert upstream.settings.api_key is None


@pytest.mark.parametrize("value", ["mps", "xpu", "cuda:", "cuda:x", "gpu", "cuda:0:1", ""])
def test_unsupported_device_string_exits_2(ywk, monkeypatch, value):
    monkeypatch.setenv("IRODORI_MODEL_DEVICE", value)
    monkeypatch.setenv("IRODORI_CODEC_DEVICE", "cpu")
    with pytest.raises(SystemExit) as excinfo:
        ywk.resolve_devices_and_precision()
    assert excinfo.value.code == 2


def test_cuda_without_cuda_exits_2(ywk, monkeypatch):
    import torch

    if torch.cuda.is_available():
        pytest.skip("this machine has CUDA; the fallback guard cannot be exercised here")
    monkeypatch.setenv("IRODORI_MODEL_DEVICE", "cuda")
    monkeypatch.setenv("IRODORI_CODEC_DEVICE", "cuda")
    with pytest.raises(SystemExit) as excinfo:
        ywk.resolve_devices_and_precision()
    assert excinfo.value.code == 2


def test_cuda_index_out_of_range_exits_2(ywk, monkeypatch):
    monkeypatch.setenv("IRODORI_MODEL_DEVICE", "cuda:99")
    monkeypatch.setenv("IRODORI_CODEC_DEVICE", "cpu")
    with pytest.raises(SystemExit) as excinfo:
        ywk.resolve_devices_and_precision()
    assert excinfo.value.code == 2


def test_cpu_device_bakes_fp32(ywk, monkeypatch):
    monkeypatch.setenv("IRODORI_MODEL_DEVICE", "cpu")
    monkeypatch.setenv("IRODORI_CODEC_DEVICE", "cpu")
    monkeypatch.delenv("IRODORI_MODEL_PRECISION", raising=False)
    monkeypatch.delenv("IRODORI_CODEC_PRECISION", raising=False)
    effective = ywk.resolve_devices_and_precision()
    assert effective == {"model": "cpu", "codec": "cpu"}
    assert os.environ["IRODORI_MODEL_PRECISION"] == "fp32"
    assert os.environ["IRODORI_CODEC_PRECISION"] == "fp32"


def test_explicit_precision_wins(ywk, monkeypatch):
    monkeypatch.setenv("IRODORI_MODEL_DEVICE", "cpu")
    monkeypatch.setenv("IRODORI_CODEC_DEVICE", "cpu")
    monkeypatch.setenv("IRODORI_MODEL_PRECISION", "bf16")
    monkeypatch.setenv("IRODORI_CODEC_PRECISION", "bf16")
    ywk.resolve_devices_and_precision()
    assert os.environ["IRODORI_MODEL_PRECISION"] == "bf16"


def test_auto_resolves_by_torch_availability(ywk, monkeypatch):
    import torch

    monkeypatch.setenv("IRODORI_MODEL_DEVICE", "auto")
    monkeypatch.setenv("IRODORI_CODEC_DEVICE", "auto")
    monkeypatch.delenv("IRODORI_MODEL_PRECISION", raising=False)
    monkeypatch.delenv("IRODORI_CODEC_PRECISION", raising=False)
    effective = ywk.resolve_devices_and_precision()
    want = "cuda" if torch.cuda.is_available() else "cpu"
    assert effective["model"] == want
    assert os.environ["IRODORI_MODEL_PRECISION"] == ("bf16" if want == "cuda" else "fp32")


def test_allocator_flag_turns_off_implicit_empty_cache(ywk, monkeypatch):
    # 裁定 160＝MIOpen の「初見の形状ごとの emptyCache」を止める。CUDA では元から届かない道なので no-op。
    import torch

    monkeypatch.delenv("YWK_GPU_MEMORY_LIMIT_MIB", raising=False)  # 検分の是正＝環境の残りで欄が増えない
    flags = ywk.apply_allocator_defaults({"model": "cuda:0", "codec": "cuda:0"})
    setter = getattr(torch._C, "_cudnn_set_conv_benchmark_empty_cache", None)
    if setter is None:
        assert flags == {"conv_empty_cache": "unavailable"}
        return
    assert flags["conv_empty_cache"] == "off"
    assert set(flags) <= {"conv_empty_cache", "allocator_gc"}  # allocator_gc は CUDA の GPU 機だけ
    getter = getattr(torch._C, "_cuda_get_conv_benchmark_empty_cache", None)
    if getter is not None:
        assert getter() is False


def test_gpu_memory_limit_env_is_parsed_in_mib(ywk, monkeypatch):
    # 裁定 160＝YWK_GPU_MEMORY_LIMIT_MIB（0・空・非数＝制限しない）。
    monkeypatch.delenv("YWK_GPU_MEMORY_LIMIT_MIB", raising=False)
    assert ywk._gpu_memory_limit_bytes() is None
    monkeypatch.setenv("YWK_GPU_MEMORY_LIMIT_MIB", "0")
    assert ywk._gpu_memory_limit_bytes() is None
    monkeypatch.setenv("YWK_GPU_MEMORY_LIMIT_MIB", "abc")
    assert ywk._gpu_memory_limit_bytes() is None
    monkeypatch.setenv("YWK_GPU_MEMORY_LIMIT_MIB", "6144")
    assert ywk._gpu_memory_limit_bytes() == 6 * 1024**3


def test_gpu_memory_limit_is_applied_or_reported_unavailable(ywk, monkeypatch):
    # 裁定 160＝GPU が在れば set_per_process_memory_fraction に写り、無ければ「unavailable」と名乗るだけ。
    import torch

    monkeypatch.setenv("YWK_GPU_MEMORY_LIMIT_MIB", "1024")
    flags = ywk.apply_allocator_defaults({"model": "cuda:0", "codec": "cuda:0"})
    if not torch.cuda.is_available():
        assert flags["gpu_memory_limit"] == "unavailable"
        assert "gpu_memory_limit_bytes" not in flags
        return
    assert flags["gpu_memory_limit"] == "on"
    assert flags["gpu_memory_limit_bytes"] == 1024**3
    total = int(torch.cuda.get_device_properties(0).total_memory)
    assert abs(torch.cuda.get_per_process_memory_fraction(0) - min(1.0, 1024**3 / total)) < 1e-6
    torch.cuda.set_per_process_memory_fraction(1.0, 0)  # leave the process as we found it


def test_executor_workers_default_and_clamp(ywk, monkeypatch):
    # 裁定 160・検分の是正 2＝上流の既定 pool を絞る（スレッドごとの BLAS 作業領域の積み上がりを止める）。
    monkeypatch.delenv("YWK_EXECUTOR_WORKERS", raising=False)
    assert ywk.executor_workers() == 2
    monkeypatch.setenv("YWK_EXECUTOR_WORKERS", "0")
    assert ywk.executor_workers() == 1
    monkeypatch.setenv("YWK_EXECUTOR_WORKERS", "99")
    assert ywk.executor_workers() == 8
    monkeypatch.setenv("YWK_EXECUTOR_WORKERS", "abc")
    assert ywk.executor_workers() == 2
    executor = ywk.bounded_executor()
    try:
        assert executor._max_workers == 2  # noqa: SLF001 -- the only observable
    finally:
        executor.shutdown(wait=False)


def test_allocator_flag_is_skipped_on_cpu(ywk):
    assert ywk.apply_allocator_defaults({"model": "cpu", "codec": "cpu"}) == {}


def test_banner_names_the_pinned_upstream(ywk):
    line = ywk.banner()
    assert line.startswith("ywk_server ")
    assert "upstream=8224daf/841fb7c" in line
