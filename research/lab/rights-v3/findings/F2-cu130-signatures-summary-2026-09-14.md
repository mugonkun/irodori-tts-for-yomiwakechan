# cu130 Authenticode signature audit  (read-only)  2026-09-14

root: %LOCALAPPDATA%\irodori-tts-ywk-cuda\runtime\cu130
total PE files (exe/dll/pyd): 325

## versions
  nvidia-smi: NVIDIA GeForce RTX 3090, 616.92
  windows: Microsoft Windows 11 Pro 10.0.26200.9445

## per package (files / Valid by signer / NotSigned count / NotSigned bytes)

| package | files | Valid | signers | NotSigned | NotSigned bytes |
|---|---|---|---|---|---|
| _cffi_backend.cp312-win_amd64.pyd | 1 | 0 |  | 1 | 181248 |
| _soundfile_data | 1 | 0 |  | 1 | 2364416 |
| charset_normalizer | 2 | 0 |  | 2 | 310784 |
| google | 1 | 0 |  | 1 | 745134 |
| grpc | 1 | 0 |  | 1 | 11177984 |
| hf_xet | 1 | 0 |  | 1 | 9499136 |
| httptools | 2 | 0 |  | 2 | 176640 |
| llvmlite | 1 | 0 |  | 1 | 120369664 |
| llvmlite.libs | 1 | 1 | Microsoft Windows Software Compatibility Publisher=1 | 0 |  |
| markupsafe | 1 | 0 |  | 1 | 13312 |
| msgpack | 1 | 0 |  | 1 | 126464 |
| numba | 14 | 0 |  | 14 | 514048 |
| numpy | 19 | 0 |  | 19 | 7217152 |
| numpy.libs | 2 | 1 | Microsoft Windows Software Compatibility Publisher=1 | 1 | 20495360 |
| PIL | 8 | 0 |  | 8 | 13438464 |
| pydantic_core | 1 | 0 |  | 1 | 5157888 |
| python-embed | 31 | 31 | Python Software Foundation=29; Microsoft Windows Software Compatibility Publisher=2 | 0 |  |
| regex | 1 | 0 |  | 1 | 726016 |
| safetensors | 1 | 0 |  | 1 | 734720 |
| scipy | 106 | 0 |  | 106 | 56140800 |
| scipy.libs | 1 | 0 |  | 1 | 20262912 |
| sentencepiece | 1 | 0 |  | 1 | 1459712 |
| setuptools | 8 | 0 |  | 8 | 103424 |
| sklearn | 71 | 2 | Microsoft Windows Software Compatibility Publisher=2 | 69 | 10172928 |
| soxr | 1 | 0 |  | 1 | 354304 |
| tokenizers | 1 | 0 |  | 1 | 7847936 |
| torch | 39 | 25 | NVIDIA Corporation=23; Intel Corporation=2 | 14 | 697849344 |
| torchaudio | 4 | 0 |  | 4 | 8908800 |
| watchfiles | 1 | 0 |  | 1 | 636416 |
| websockets | 1 | 0 |  | 1 | 11264 |
| yaml | 1 | 0 |  | 1 | 253952 |

## torch/lib DLLs (NVIDIA-origin vs torch-own)

torch/lib DLL count: 37

| name | bytes | status | signer |
|---|---|---|---|
| c10.dll | 1081344 | NotSigned |  |
| c10_cuda.dll | 412160 | NotSigned |  |
| caffe2_nvrtc.dll | 54272 | NotSigned |  |
| cublas64_13.dll | 50288240 | Valid | NVIDIA Corporation |
| cublasLt64_13.dll | 477896816 | Valid | NVIDIA Corporation |
| cudart64_13.dll | 470640 | Valid | NVIDIA Corporation |
| cudnn_adv64_9.dll | 88123936 | Valid | NVIDIA Corporation |
| cudnn_cnn64_9.dll | 1428080 | Valid | NVIDIA Corporation |
| cudnn_engines_precompiled64_9.dll | 188558368 | Valid | NVIDIA Corporation |
| cudnn_engines_runtime_compiled64_9.dll | 19942944 | Valid | NVIDIA Corporation |
| cudnn_graph64_9.dll | 2285600 | Valid | NVIDIA Corporation |
| cudnn_heuristic64_9.dll | 53315104 | Valid | NVIDIA Corporation |
| cudnn_ops64_9.dll | 29970976 | Valid | NVIDIA Corporation |
| cudnn64_9.dll | 266352 | Valid | NVIDIA Corporation |
| cufft64_12.dll | 284331040 | Valid | NVIDIA Corporation |
| cufftw64_12.dll | 201264 | Valid | NVIDIA Corporation |
| cupti64_2025.3.0.dll | 2067488 | Valid | NVIDIA Corporation |
| curand64_10.dll | 58863672 | Valid | NVIDIA Corporation |
| cusolver64_12.dll | 126421024 | Valid | NVIDIA Corporation |
| cusolverMg64_12.dll | 95365152 | Valid | NVIDIA Corporation |
| cusparse64_12.dll | 150326304 | Valid | NVIDIA Corporation |
| libiomp5md.dll | 1614192 | Valid | Intel Corporation |
| libiompstubs5md.dll | 43888 | Valid | Intel Corporation |
| nvJitLink_130_0.dll | 88218656 | Valid | NVIDIA Corporation |
| nvperf_host.dll | 27764256 | Valid | NVIDIA Corporation |
| nvrtc64_130_0.alt.dll | 91028512 | Valid | NVIDIA Corporation |
| nvrtc64_130_0.dll | 90961440 | Valid | NVIDIA Corporation |
| nvrtc-builtins64_130.dll | 4471328 | Valid | NVIDIA Corporation |
| nvToolsExt64_1.dll | 48128 | NotSigned |  |
| shm.dll | 15360 | NotSigned |  |
| torch.dll | 9728 | NotSigned |  |
| torch_cpu.dll | 265100288 | NotSigned |  |
| torch_cuda.dll | 408946688 | NotSigned |  |
| torch_global_deps.dll | 9728 | NotSigned |  |
| torch_python.dll | 19076096 | NotSigned |  |
| uv.dll | 195072 | NotSigned |  |
| zlibwapi.dll | 89088 | NotSigned |  |

## statuses other than Valid / NotSigned
  (none)

## NVIDIA-signed total: 23
