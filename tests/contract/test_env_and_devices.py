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
    assert os.environ["IRODORI_PRELOAD"] == "true"
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


def test_banner_names_the_pinned_upstream(ywk):
    line = ywk.banner()
    assert line.startswith("ywk_server ")
    assert "upstream=8224daf/841fb7c" in line
