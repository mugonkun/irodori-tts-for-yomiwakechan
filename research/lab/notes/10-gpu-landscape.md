# 10 GPU 対応範囲の外部材料（BRIEF §4-7 / §4-5）

読解席（opus）・作成 2026-09-04。数値の取得日時は特記なき限り **2026-09-04 JST**（curl の `Date:` ヘッダ実測＝`Thu, 03 Sep 2026 16:46:20 GMT`）。
外部数値は URL 付き。ローカル実測は「相対パス:行番号」または `du -sm` の実測値。推測には **未確認** の札。

## 要点（10 行以内）

1. Steam 2026-08＝**NVIDIA 72.88% / AMD 18.68% / Intel 8.03% / その他 0.41%**。DX12 対応 GPU は 91.45%。
2. だが「サイズが理由で ROCm を切る」は **成立しない**＝Windows ROCm の wheel 合計 約 **1.1〜1.3 GiB** は CUDA torch 単体 **1898 MiB** より小さい。
3. 切る理由は成熟度＝AMD 公式は Windows で「PyTorch のみ・訓練なし・Python 3.12 のみ・`torch.distributed` 不可・LLM は batch 1 のみ」と明記。
4. 稼働機の `torch 2.13.0+rocm10.0.0` は AMD 公式ドキュメント記載版（ROCm 7.2.1 / torch 2.9.1）**ではない** `whl-next` チャネル＝公式サポート外。
5. `download.pytorch.org` の rocm 索引は **全て manylinux**＝Windows の ROCm torch は AMD 自前索引のみ。
6. ⒝ の要＝**DirectML EP は 23.9 MiB 自己完結**で NVIDIA/AMD/Intel 全対応。ただし「sustained engineering」で pip/NuGet は 1.24.4（2026-03）＝本流 1.29.0 から 5 マイナー遅れ。
7. ONNX Runtime CUDA EP は自前で CUDA を持たない＝**別途 CUDA13+cuDNN9 で約 1.08 GiB**、かつ **ドライバ 580 以上**。
8. Windows ML（EP を OS が自動配布）は **Win11 24H2 以上限定**＝Steam の Win10 22.90% を落とす。
9. NVIDIA 実機なしの検証は **GitHub Actions `windows_4_core_gpu` $0.102/分（≒$6.12/時）** が最も現実的。
10. モデル実測＝`Irodori-TTS-v4-Small/model.safetensors` **2922.3 MiB**、コーデック **409.7 MiB**（合計 3.25 GiB）。

---

## 1. 利用者層（GPU ベンダ比・配信者の構造的偏り）

### 1-1 Steam Hardware Survey（月次・全世界）

| 月 | NVIDIA | AMD | Intel | その他 | 出典 |
|---|---|---|---|---|---|
| 2026-05 | 72.42% | 19.13% | 8.05% | 0.4% | tipranks（二次） |
| 2026-07 | 72.72% | 18.71% | 8.16% | 0.41% | 検索要約（二次・複数一致） |
| **2026-08** | **72.88%** | **18.68%** | **8.03%** | **0.41%** | gamepc-zone.jp（日本語二次・8 月版） |

- 一次（Valve）: <https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam>（2026-09-04 取得）＝**2026 年 8 月版**。
  ベンダ別集計表は JS 描画のため WebFetch では取れず、**ベンダ比の一次直引きは未確認**。ただし上位機種は二次と一致（RTX 3060 3.76% / RTX 5070 3.56% / RTX 5060 Ti 2.41%）＝二次の数値は信頼できる。
- 一次から直接取れた 2026-08 の値:
  - **DirectX 12 GPU 91.45%**（DX11 0.41% / DX10 0.21%）
  - **Windows 11 64bit 70.97% / Windows 10 64bit 22.90%**
  - 言語 **Japanese 2.50%**
- 上位機種（2026-08・一次）: RTX 3060 / RTX 4060 Laptop / RTX 4060 / RTX 5070 / RTX 3050 / RTX 5060 / RTX 5060 Laptop / RTX 5060 Ti / GTX 1650 / RTX 4060 Ti。**上位 10 が全て NVIDIA**。
  ※ 一次ページを 2 回 fetch したところ RTX 3060 が 3.76% と 3.92% で食い違った＝要約器の誤差。**個別機種の小数点以下は未確認**、順位と「上位 10 が全て NVIDIA」は確度が高い。
- 追加根拠（二次）: 単体ボード（AIB）出荷ベースでは NVIDIA が 90% 超という調査がある＝Steam の 72% は iGPU/ノートを含むため低めに出る。**具体的な調査名・四半期は未確認**。

### 1-2 日本市場・配信者層

| 問い | 結論 | 根拠 |
|---|---|---|
| 日本固有の GPU ベンダ比 | **未確認** | gamepc-zone.jp は Steam の全世界値をそのまま紹介＝日本固有データなし |
| 日本の dGPU 市場規模 | 2025 年 77.9 億 USD → 2026 年 89.5 億 USD（Mordor/GII の市場レポート要約） | 二次・ベンダ比の内訳なし＝**本件には使えない** |
| 配信者が NVIDIA に寄る構造 | **状況証拠あり（強い断定は避ける）** | 下記 |

構造的理由（いずれも二次出典・**一次未確認**）:

- NVENC AV1 は **RTX 40/50 世代**が要件（OBS 29.1 以降）。
- AMD の AMF AV1 が「NVENC に並んだ」とされるのは **RDNA4（RX 9000, 2026 年初）以降**＝それ以前の AMD 機は品質面で不利という評価が長く続いた。
- 出典: <https://streamersize.com/blog/nvenc-av1-explained/> / <https://obs-versions.com/encoding> / <https://streamersize.com/blog/best-gpu-for-streaming-2026/>（いずれも 2026-09-04 取得・アフィリエイト系メディア＝**信頼度は中**）。
- 一次に近いもの: <https://nvidia.com/en-us/geforce/guides/broadcasting-guide>（NVIDIA 自身の宣伝＝中立ではない）。

> **札**: 「配信者は NVIDIA が多数」は Steam 全体の 72.88% と「上位 10 機種が全て NVIDIA」で**十分に支えられる**。配信者に限定した統計は**未確認**。

---

## 2. Windows での PyTorch＋ROCm の成熟度

### 2-1 時系列（AMD 公式・二次含む）

| 時期 | 出来事 | 出典 |
|---|---|---|
| 2025 年前半 | AMD が「2025 年後半に Radeon 向け ROCm を Windows/Linux へ」と表明 | amd.com blog「The Road to ROCm on Radeon for Windows and Linux」（本文 fetch は 60 秒でタイムアウト＝**逐語は未確認**） |
| 2025-09 | **ROCm 6.4.4 で PyTorch が Windows ネイティブ動作（public preview）**。対象＝Radeon RX 7000（RDNA3）/ RX 9000（RDNA4）/ Ryzen AI 300「Strix」/ Ryzen AI MAX「Strix Halo」 | videocardz / techpowerup / wccftech（二次・複数一致） |
| 2025-11-26 | **ROCm 7.1.1 で PyTorch 2.9 対応**（Windows） | AMD リリースノート `RN-AMDGPU-WINDOWS-PYTORCH-7-1-1` |
| 現行（2026-09 時点の docs `latest`） | **ROCm 7.2.1**：torch **2.9.1+rocm7.2.1** / torchvision 0.24.1 / torchaudio 2.9.1 | <https://rocm.docs.amd.com/projects/radeon-ryzen/en/docs-7.2.1/docs/install/installryz/windows/install-pytorch.html>（2026-09-04 取得） |

公式インストール条件（同ページ・逐語）:

- 「**Python 3.12 must be installed**」
- 「**26.2.2 graphics driver must be installed**」（Windows）
- pip index: **`https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/`**

### 2-2 Windows の制限（AMD 公式・逐語）

<https://rocm.docs.amd.com/projects/radeon-ryzen/en/latest/docs/limitations/limitationsryz.html>（2026-09-04 取得）

- 「**Only Pytorch is currently available on Windows - the rest of the ROCm stack is only supported on Linux.**」
- 「**The torch.distributed module is currently not supported.** Some functions from diffusers and accelerate module may get affected.」
- 「**On Windows, only LLM batch sizes of 1 are officially supported.**」
- 「The latest version of transformers should be installed, via pip install. Some older versions of transformers (<4.55.5) might not be supported.」
- Windows で **ML 訓練はサポート外**。**Python は 3.12 のみ**。
- Radeon 側 <https://rocm.docs.amd.com/projects/radeon-ryzen/en/latest/docs/limitations/limitationsrad.html>: 「AO Triton with PyTorch 2.9 is disabled by default for AMD Radeon RX 7000 series graphics products」（要手動有効化）。
- **torch.compile / bf16 の可否は当該ページに明記なし＝未確認。**

### 2-3 稼働機（司令官機）が実際に使っている経路

| 項目 | 実測値 | 根拠 |
|---|---|---|
| pip index | `https://stable.repo.amd.com/rocm/whl-next/` | `yomiwakechan2/probe/irodori-tts-rocm-setup-guide.md:69`, `:99` |
| torch | `torch==2.13.0+rocm10.0.0` | `C:/IrodoriTTS/Irodori-TTS/overrides-rocm.txt:1` |
| torchaudio | `torchaudio==2.11.0.2+rocm10.0.0` | 同 `:2` |
| 追加パッケージ | `amd_torch_device_gfx1151`, `amd_torch_device_gfx115x`（共に 2.13.0+rocm10.0.0）、`rocm_sdk_core` / `rocm_sdk_libraries` / `rocm_sdk_device_gfx1151`（全て 10.0.0） | `.venv-rocm/Lib/site-packages` の dist-info 実測 |
| 導入コマンド | `uv pip install ... --extra-index-url https://stable.repo.amd.com/rocm/whl-next/ --index-strategy unsafe-best-match "torch[device-gfx1151]==2.13.0+rocm10.0.0"` | `irodori-tts-rocm-setup-guide.md:69` |
| wheel タグ | `cp312-cp312-win_amd64`（Generator: `rocm-kpack-split-python-wheels`） | `.venv-rocm/.../amd_torch_device_gfx1151-*.dist-info/WHEEL` |

> **重大**: これは **AMD 公式ドキュメントに載っている Windows 版（ROCm 7.2.1 / torch 2.9.1）ではない**。`whl-next` は先行チャネル（ROCm 10.0.0 系）。公式の Windows サポート表・制限表は **この構成を保証しない**。上流追随の保守で最も脆い点。

### 2-4 索引の実測（どこに Windows 版があるか）

| 索引 | Windows(win_amd64) torch | 実測 |
|---|---|---|
| `download.pytorch.org/whl/rocm6.4 / 7.0 / 7.1 / 7.2 / 7.14` | **無し**（全て `manylinux_2_28_x86_64`） | `rocm7.2` の cp312 最新＝`torch-2.14.0+rocm7.2-cp312-cp312-manylinux_2_28_x86_64.whl` |
| `repo.amd.com/rocm/whl/gfx1151/` | 有り＝`torch-2.9.1+rocm7.11.0 / 7.12.0 / 7.13.0` の cp310〜cp313 | 索引 HTML 実測 |
| `rocm.nightlies.amd.com/v2/gfx1151/` | 有り＝`torch-2.12.0a0+rocm7.13.0a2026MMDD` 等の nightly | 索引 HTML 実測 |
| `stable.repo.amd.com/rocm/whl-next/`（→ `…/rocm/pytorch/whl-next/` へ 301） | 有り＝`torch-2.13.0+rocm10.0.0` cp310〜**cp314** | 索引 HTML 実測 |

- **triton**: `download.pytorch.org/whl/triton/` に `win_amd64` の wheel は **0 件**（grep 実測）。AMD の各索引には `triton/` ディレクトリが存在するが、**Windows での torch.compile の実効性は未確認**。

### 2-5 版ずれ（torchaudio）

- 稼働機: torch **2.13.0** に対し torchaudio **2.11.0.2**。
- 上流でも同じ構図＝`download.pytorch.org/whl/cpu` の cp312/win_amd64 で torch は **2.14.0** まであるのに torchaudio は **2.11.0** が最新（実測）。
  → 「torchaudio の版が torch に追随しない」のは ROCm 固有ではなく **上流全体の現況**。⒜⒞ でも同じ縛りを受ける。
- 上流 Irodori-TTS の宣言は `torch>=2.10.0` / `torchaudio>=2.10.0` / `torchcodec>=0.10.0,<0.11.0`（`C:/IrodoriTTS/Irodori-TTS/requirements.txt:13-16`）。
  **torchcodec の上限 `<0.11.0` は現行 0.16.0 と乖離**＝上流追随で真っ先に効く固定。

---

## 3. ONNX Runtime の実行プロバイダ（Windows）

### 3-1 EP 比較表

| EP | Windows | ベンダ | 追加ランタイム | パッケージ実測 | 主な制約 |
|---|---|---|---|---|---|
| CPU | ○ | 全部 | 不要 | `onnxruntime` 1.29.0 **13.4 MiB** | — |
| **CUDA** | ○ | NVIDIA のみ | **要**（CUDA 13 + cuDNN 9） | `onnxruntime-gpu` 1.29.0 **141.3 MiB**（内 `onnxruntime_providers_cuda.dll` 展開 164.0 MiB） | ORT 1.27 以降の既定は **CUDA 13.0 / cuDNN 9.x**＝**ドライバ 580 以上** |
| **DirectML** | ○ | NVIDIA/AMD/Intel/Qualcomm | **不要**（`DirectML.dll` 同梱 17.7 MiB） | `onnxruntime-directml` **1.24.4**（2026-03-17）**23.9 MiB** | 下記 3-3 |
| ROCm | **×** | AMD | — | `onnxruntime_rocm`（manylinux のみ） | 「ROCm Execution Provider has been removed since 1.23 release」「ROCm 7.0 is the last offiicaly AMD supported distribution of this provider」 |
| MIGraphX | △ | AMD | 要 | 素の pip での Windows 版は **未確認** | AMD 公式の ROCm EP 後継。**Windows ML 経由ならダウンロード可**（3-4） |
| TensorRT | ○ | NVIDIA | 要（TensorRT 本体別途） | `onnxruntime_providers_tensorrt.dll` 0.8 MiB が `onnxruntime-gpu` に同梱 | Windows ML では `NvTensorRtRtx`（RTX 30 以降） |
| OpenVINO | ○ | Intel | 要 | 共有ライブラリ `onnxruntime_providers_openvino.dll` | Intel CPU/GPU/NPU |

### 3-2 CUDA EP の実費（Windows・pip 経路）

`onnxruntime-gpu` は CUDA を同梱しない（wheel 内 DLL は EP と ORT 本体のみ＝実測）。extras は
`nvidia-cuda-nvrtc~=13.0` / `nvidia-cuda-runtime~=13.0` / `nvidia-cufft~=12.0` / `nvidia-curand~=10.0` / `nvidia-cudnn-cu13~=9.0`（PyPI メタデータ実測）。

| wheel（win_amd64） | 版 | サイズ |
|---|---|---|
| `nvidia_cudnn_cu13` | 9.25.1.1 | **388.9 MiB** |
| `nvidia_cublas`（cudnn の依存） | 13.6.0.2 | **376.3 MiB** |
| `nvidia_cufft` | 12.3.0.29 | 175.4 MiB |
| `nvidia_curand` | 10.4.3.29 | 52.9 MiB |
| `nvidia_cuda_nvrtc` | 13.3.33 | 43.2 MiB |
| `nvidia_nvjitlink` | 13.3.33 | 36.0 MiB |
| `nvidia_cuda_runtime` | 13.3.29 | 2.5 MiB |
| **小計** | | **1075.2 MiB** |
| `onnxruntime_gpu` | 1.29.0 | 141.3 MiB |
| **CUDA EP 合計** | | **≒1216.5 MiB（1.19 GiB）** |

参考（NVIDIA 公式 redist の zip・非 pip 経路。`developer.download.nvidia.com/compute/.../redistrib_*.json` 実測）:
CUDA 12.9.1 Windows＝cudart 3.4 / cublas 524.3 / cufft 189.2 / curand 64.8 / nvrtc 300.0 / cusolver 307.3 / cusparse 341.5 MiB（**計 1730.4 MiB**）。
cuDNN 9.25.1.1 Windows＝**cuda12 版 1784.0 MiB / cuda13 版 1177.3 MiB**（開発用の静的 lib 込み。実行時 DLL だけなら pip wheel の 388.9〜698.4 MiB が実勢）。

### 3-3 DirectML EP の性質（逐語）

<https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html>（2026-09-04 取得）

- 「**DirectML is in sustained engineering. DirectML continues to be supported, but new feature development has moved to WinML for Windows-based ONNX Runtime deployments.**」
- 要件: 「Minimum OS: Windows 10, version 1903」「Minimum API: DirectX 12 capable device」「Windows SDK 10.0.17134.0 or newer」
- 対応 HW: NVIDIA Kepler（GTX 600）以降 / AMD GCN 第 1 世代（HD 7000）以降 / Intel Haswell 第 4 世代 HD Graphics 以降 / Qualcomm Adreno 600 以降
- 制約（**⒝ の検分に直結**）:
  - 「**DirectML execution provider does not support the use of memory pattern optimizations or parallel execution**」
  - 「**Does not support multi-threaded calls to Run on the same inference session**」
  - shape がセッション作成時に確定していると最も効率が良い／free dimension は override API での固定が要る
  - opset は **20（ONNX 1.15）まで**
- 版の遅れ（PyPI 実測）: `onnxruntime-directml` は 1.21.1(2025-04) → 1.22.0(2025-05) → 1.23.0(2025-09) → 1.24.1〜**1.24.4(2026-03-17)** で止まる。本流 `onnxruntime` は **1.29.0(2026-08-17)**。**約 5 か月・5 マイナー版の遅れ**。NuGet も同じ（`Microsoft.ML.OnnxRuntime.DirectML` 最新 1.24.4）。
- **fp16 の可否は当該ページに明記なし＝未確認**（DirectML 自体は fp16 を扱えるが、ORT EP 側の記述として引ける原文が取れていない）。

### 3-4 C# / NuGet と Windows ML

| NuGet パッケージ | 最新版 | nupkg サイズ |
|---|---|---|
| `Microsoft.ML.OnnxRuntime`（CPU） | 1.29.0 | **147.8 MiB** |
| `Microsoft.ML.OnnxRuntime.Gpu`（メタ） | 1.29.0 | 0.3 MiB |
| `Microsoft.ML.OnnxRuntime.Gpu.Windows` | 1.29.0 | **134.5 MiB** |
| `Microsoft.ML.OnnxRuntime.DirectML` | **1.24.4** | **11.9 MiB** |
| `Microsoft.ML.OnnxRuntime.Managed`（C# バインディング） | 1.29.0 | 1.0 MiB |
| `Microsoft.Windows.AI.MachineLearning`（Windows ML） | 2.3.42 | **49.1 MiB** |
| `Microsoft.WindowsAppSDK.ML` | 2.1.74 | 0.1 MiB |

（api.nuget.org の flatcontainer に対する HEAD 実測・2026-09-04）

**Windows ML の現況**（<https://github.com/MicrosoftDocs/windows-ai-docs/blob/docs/docs/new-windows-ml/supported-execution-providers.md> ほか・2026-09-04 取得）:

- 2025 年 5 月 Build で公開 → **2025 年 9 月下旬に GA**。Windows App SDK **1.8.1 以降**に同梱。
- 動作要件: **Windows 11 24H2（build 26100）以上**。
- 内蔵 EP: **CPU / DirectML（legacy）**。ダウンロード配布される EP: **MIGraphX（AMD）/ NvTensorRtRtx（NVIDIA・RTX 30 以降）/ OpenVINO（Intel）/ QNN（Qualcomm）/ VitisAI（AMD NPU）**。
- 「available … for dynamic download via the Windows ML `ExecutionProviderCatalog` APIs」＝**アプリが CUDA も ROCm も同梱しなくてよい**。
- 使う SDK: `Microsoft.WindowsAppSDK.ML` 1.8.x / 2.x、`Microsoft.Windows.AI.MachineLearning` 2.x。

> **効き所**: Windows ML なら「CUDA か ROCm か」の分岐そのものが OS 側に落ちる。ただし **Win11 24H2 以上**＝Steam 2026-08 で Win10 が 22.90% 残る。**初心者向け配布の唯一解にはできない**（DirectML EP 同梱との二段構えが要る）。

---

## 4. 配布物の大きさ（実数）

### 4-1 PyTorch 系 wheel（cp312 / win_amd64・HEAD 実測 2026-09-04）

| wheel | サイズ |
|---|---|
| `torch-2.14.0+cpu` | **118.3 MiB**（123,996,119 B） |
| `torch-2.14.0+cu126` | **2482.2 MiB** |
| `torch-2.9.1+cu128` | **2729.4 MiB** |
| `torch-2.11.0+cu128`（cu128 索引の最終版） | **2625.6 MiB** |
| `torch-2.14.0+cu130` | **1898.4 MiB**（1,990,604,486 B） |
| `torch-2.14.0+cu132` | **1901.4 MiB** |
| `torchaudio-2.11.0+cpu` | 0.3 MiB |
| `torchaudio-2.11.0+cu130` | 1.6 MiB |
| `torchcodec-0.16.0+cpu` | 6.1 MiB |
| `libtorch-win-shared-with-deps-2.14.0+cu130.zip`（TorchSharp 経路） | **3752.1 MiB** |
| `libtorch-win-shared-with-deps-2.14.0+cpu.zip` | 191.8 MiB |
| `python-3.12.10-embed-amd64.zip` | **10.6 MiB**（11,133,606 B） |

- 索引の最新版（実測）: `cpu` / `cu126` / `cu130` / `cu132` は **2.14.0** まで。**`cu128` は 2.11.0 で止まる**、`cu129` は 2.9.0 止まり。
  → **cu128 に固定する案は既に袋小路**。CUDA 12 系を続けるなら `cu126`（2482 MiB）、新しくするなら `cu130`（1898 MiB）。
- Python 3.12 の embeddable zip は **3.12.10 が最後のバイナリ**らしい（3.12.11〜3.12.14 は HEAD が 146 B の応答＝バイナリ無し）。**3.12 系の埋め込み配布は 3.12.10 で固定される見込み＝未確認だが強く示唆**。

### 4-2 torch cu130 wheel の中身（HTTP Range で中央ディレクトリを実読・12,247 エントリ）

| DLL | 展開後 |
|---|---|
| `torch/lib/cublasLt64_13.dll` | 455.8 MiB |
| `torch/lib/torch_cuda.dll` | 403.1 MiB |
| `torch/lib/torch_cpu.dll` | 291.8 MiB |
| `torch/lib/cufft64_12.dll` | 271.2 MiB |
| `torch/lib/cudnn_engines_precompiled64_9.dll` | 211.6 MiB |
| `torch/lib/cusparse64_12.dll` | 143.4 MiB |
| （ほか cusolver / cudnn_graph / cudnn_adv / nvrtc / nvJitLink / curand / cublas64_13 …） | |
| **DLL 合計 39 個** | **2737.5 MiB** |

> **断定**: Windows の torch CUDA wheel は **CUDA ランタイムと cuDNN を自前で同梱**する＝利用者に CUDA Toolkit の別途導入を求めない。⒜（埋め込み配布）で「CUDA インストーラを踏ませない」ことは**可能**。代償が 1.9 GiB。

### 4-3 AMD の Windows ROCm wheel（`stable.repo.amd.com` HEAD 実測）

| wheel（cp312 / win_amd64） | サイズ | 導入後（`du -sm` 実測） |
|---|---|---|
| `torch-2.13.0+rocm10.0.0` | **108.2 MiB** | `torch/` 739 MB |
| `amd_torch_device_gfx1151-2.13.0+rocm10.0.0` | 47.7 MiB | （torch 内） |
| `amd_torch_device_gfx115x-2.13.0+rocm10.0.0` | 179.1 MiB | （torch 内） |
| `torchaudio-2.11.0.2+rocm10.0.0` | 2.0 MiB | 10 MB |
| `rocm_sdk_core-10.0.0-py3-none-win_amd64` | **722.9 MiB** | `_rocm_sdk_core/` **2132 MB** |
| `rocm_sdk_libraries-10.0.0-py3-none-win_amd64` | 111.4 MiB | `_rocm_sdk_libraries/` **1028 MB** |
| `rocm_sdk_device_gfx1151-10.0.0-py3-none-win_amd64` | 132.9 MiB | （上記に含む） |
| **合計（gfx1151 のみ）** | **≒1125 MiB** | |
| **合計（gfx115x も入れる＝稼働機の実構成）** | **≒1304 MiB** | |

venv 実測（`C:/IrodoriTTS/Irodori-TTS/`）: `.venv`（CPU・torch 2.10.0）**1335 MB** / `.venv-rocm` **4873 MB** ＝ **ROCm 化の実装差 +3538 MB**。
CPU venv の内訳: `torch/` 447 MB、非 torch 依存 ≒888 MB。

> **断定（司令官の裁定に直結）**: **ダウンロード量では ROCm(1.1〜1.3 GiB) < CUDA(1.9 GiB)**。ROCm を切る根拠は「配布物が重い」では**ない**。

### 4-4 ONNX Runtime 系（再掲・pip / win_amd64・cp312）

| wheel | 版 | サイズ |
|---|---|---|
| `onnxruntime` | 1.29.0 | 13.4 MiB |
| `onnxruntime_gpu` | 1.29.0 | 141.3 MiB（＋CUDA13/cuDNN9 で +1075.2 MiB） |
| `onnxruntime_directml` | **1.24.4** | **23.9 MiB（追加ランタイム 0）** |

### 4-5 モデル重み（HF API 実測 2026-09-04）

| repo | 実体 | サイズ | メタデータの license |
|---|---|---|---|
| `Aratako/Irodori-TTS-v4-Small` | `model.safetensors` | **2922.3 MiB**（repo 全体 2936.8 MiB） | `mit` |
| `Aratako/Semantic-DACVAE-Japanese-32dim` | `weights.pth` | **409.7 MiB** | `mit` |
| `facebook/dacvae-watermarked` | `weights.pth` | 410.8 MiB | `apache-2.0`（※本文との矛盾は BRIEF §2-2 の通り・**卓の専管**） |

※ BRIEF の「モデル 2.86GB」は 2922.3 MiB = 2.854 GiB と一致。

### 4-6 配布物の総量（見積・ダウンロードバイト）

| 案 / 経路 | 実行系 | モデル | 合計 |
|---|---|---|---|
| ⒜ CUDA のみ（python-embed + torch cu130） | 10.6 + 1898.4 + 1.6 + 6.1 + 非 torch 依存 ≒300（**未確認・CPU venv 実測 888 MB からの圧縮見積**） | 3332.0 | **≒5.4 GiB** |
| ⒜ ＋ ROCm も同梱 | 上記 + 1125〜1304 | 3332.0 | **≒6.5 GiB** |
| ⒜ ROCm を「司令官機だけ手動」に逃がす | ⒜ CUDA のみと同じ | 同 | 配布物は増えない |
| ⒝ ONNX + CUDA EP | 141.3 + 1075.2 | ONNX 化後サイズ **未確認** | ≒1.19 GiB + モデル |
| ⒝ ONNX + DirectML EP | **23.9**（+ C# 側 1.0） | 同上 | **≒25 MiB + モデル** |
| ⒝ Windows ML | 49.1（EP は OS 配布） | 同上 | **≒49 MiB + モデル** |
| ⒞ サーバ据置（現状 venv） | `.venv-rocm` 4873 MB / `.venv` 1335 MB（導入後実測） | 3332.0 | — |

---

## 5. NVIDIA 側の検証手段（手元に NVIDIA 機が無い前提）

| 手段 | 費用 | Windows | 出典 / 札 |
|---|---|---|---|
| **GitHub Actions hosted GPU runner** | `linux_4_core_gpu` **$0.052/分**、**`windows_4_core_gpu` $0.102/分（≒$6.12/時）** | **○** | <https://docs.github.com/en/billing/reference/actions-minute-multipliers>（2026-09-04）。**Windows GPU ランナーが公式に存在する**のが要点 |
| AWS EC2 `g4dn.xlarge`（NVIDIA T4 16GB） | Linux オンデマンド **$0.5260/時**（us-east-1） | Windows 価格は**未確認**（ライセンス加算あり） | <https://instances.vantage.sh/aws/ec2/g4dn.xlarge>（2026-09-04） |
| Azure `NVads A10 v5` | NV12ads **$0.908/時** / NV18ads $1.6/時 / NV36ads $3.2/時（Linux 前提の二次要約） | Windows 価格は**未確認** | instances.vantage.sh 由来の検索要約＝**一次未確認** |
| 第三者 GPU ランナー（RunsOn / machine.dev） | machine.dev は **$0.003/分〜**（spot）。RunsOn は T4/A10G/L4/L40S/M60/V100/A100/H100/H200 | **Windows 対応は未確認** | <https://runs-on.com/runners/gpu/> / <https://machine.dev/docs/platform-specifications/gpu-runners/> |
| 協力者検証（RTX 機を持つ利用者・配信者） | 0 円 | ○ | 出典なし＝**運用判断**。再現性・秘匿性の担保が要る |

概算: **1 便あたり「30 分の Windows GPU ランナー」で約 $3**。torch の版が上がるたびの再検証（年 4〜6 回と仮定・**仮定は未確認**）でも年 $20 未満。
**GPU 実機の購入は不要**という結論を支える数字。

---

## 6. ドライバ要件（NVIDIA）

<https://docs.nvidia.com/deploy/cuda-compatibility/minor-version-compatibility.html> / <https://docs.nvidia.com/cuda/cuda-toolkit-release-notes/index.html>（2026-09-04 取得）

| CUDA Toolkit | 最小ドライバ | 備考 |
|---|---|---|
| CUDA 11.x | >= 450（上限 <525） | |
| **CUDA 12.x** | **>= 525**（Windows x86_64 の実セルは **`>=527.41`**＝12.0 GA、12.2 Update 2 は `>=537.13`）。上限 <580 | |
| **CUDA 13.x** | **>= 580** | **CUDA 13.1 以降、Windows ディスプレイドライバは Toolkit に同梱されない** |

- minor version compatibility（逐語）: 「**applications compiled with a CUDA Toolkit release from within a CUDA major release family can run, with limited feature-set, on systems having at least the minimum required driver version.**」
- 但し書き（3 点）: 古いドライバでは機能が限定される／**PTX でコンパイルしたアプリは古いドライバで動かない**／`nvcc -arch=sm_xx` の明示が要る。
- **forward compatibility パッケージが Linux 専用かどうかは未確認**（当該ページから逐語が取れず）。

含意:

- `torch cu126`（2482 MiB）を選べば **527.41 以上**で足りる＝古い機体を落とさない。
- `torch cu130` / `cu132`（1898〜1901 MiB）や **ORT 1.27 以降の `onnxruntime-gpu`（既定が CUDA 13）** を選ぶと **580 以上**を要求する＝ドライバ更新を利用者に強いる。
- サイズ（1898 vs 2482 MiB）と対応ドライバ幅（580+ vs 527+）が**トレードオフ**になっている。

---

## 7. 引用集（URL・取得日は全て 2026-09-04）

- Steam Hardware & Software Survey（一次・2026-08 版）: https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam
- Steam 8 月版のベンダ比（日本語二次）: https://gamepc-zone.jp/steam-report/
- Steam 5 月版（二次）: https://www.tipranks.com/news/amd-continues-to-gain-on-intel-nvidia-in-the-may-2026-steam-hardware-survey
- AMD ROCm on Radeon/Ryzen・Windows PyTorch 導入: https://rocm.docs.amd.com/projects/radeon-ryzen/en/docs-7.2.1/docs/install/installryz/windows/install-pytorch.html
- AMD ROCm・Ryzen の制限: https://rocm.docs.amd.com/projects/radeon-ryzen/en/latest/docs/limitations/limitationsryz.html
- AMD ROCm・Radeon の制限: https://rocm.docs.amd.com/projects/radeon-ryzen/en/latest/docs/limitations/limitationsrad.html
- AMD blog（Windows/Linux ROCm 到達点）: https://www.amd.com/en/blogs/2025/the-road-to-rocm-on-radeon-for-windows-and-linux.html （**本文 fetch はタイムアウト＝逐語未確認**）
- AMD リリースノート（Windows PyTorch 7.1.1 / 7.2 / preview）: https://www.amd.com/en/resources/support-articles/release-notes/RN-AMDGPU-WINDOWS-PYTORCH-7-1-1.html ほか
- AMD wheel 索引: https://stable.repo.amd.com/rocm/whl-next/ ／ https://repo.amd.com/rocm/whl/gfx1151/ ／ https://rocm.nightlies.amd.com/v2/gfx1151/
- PyTorch wheel 索引: https://download.pytorch.org/whl/
- ORT DirectML EP: https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html
- ORT CUDA EP: https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html
- ORT ROCm EP（廃止告知）: https://onnxruntime.ai/docs/execution-providers/ROCm-ExecutionProvider.html
- ORT MIGraphX EP: https://onnxruntime.ai/docs/execution-providers/MIGraphX-ExecutionProvider.html
- Windows ML の対応 EP: https://github.com/MicrosoftDocs/windows-ai-docs/blob/docs/docs/new-windows-ml/supported-execution-providers.md
- GitHub Actions 分単価: https://docs.github.com/en/billing/reference/actions-minute-multipliers
- NVIDIA CUDA compatibility: https://docs.nvidia.com/deploy/cuda-compatibility/minor-version-compatibility.html
- NVIDIA CUDA Toolkit release notes: https://docs.nvidia.com/cuda/cuda-toolkit-release-notes/index.html
- NVIDIA redist マニフェスト: https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.9.1.json ／ https://developer.download.nvidia.com/compute/cudnn/redist/redistrib_9.25.1.json
- PyPI JSON API（onnxruntime / onnxruntime-gpu / onnxruntime-directml / nvidia-*）: https://pypi.org/pypi/<name>/json
- NuGet flatcontainer: https://api.nuget.org/v3-flatcontainer/<id>/index.json
- HF API（モデルサイズ・license）: https://huggingface.co/api/models/Aratako/Irodori-TTS-v4-Small?blobs=true ほか

ローカル根拠:

- `yomiwakechan2/probe/irodori-tts-rocm-setup-guide.md:69`（`--extra-index-url https://stable.repo.amd.com/rocm/whl-next/`）、`:84-85`、`:99`
- `C:/IrodoriTTS/Irodori-TTS/overrides-rocm.txt:1-3`
- `C:/IrodoriTTS/Irodori-TTS/requirements.txt:13-16`
- `C:/IrodoriTTS/Irodori-TTS/.venv-rocm/Lib/site-packages/`（dist-info 一覧・`du -sm` 実測）
- `C:/IrodoriTTS/Irodori-TTS/.venv/Lib/site-packages/`（同）

---

## 8. 主席への注意（裁定に効く事実）

1. **「CUDA のみで出し、ROCm は司令官機だけの手動手順」は成立する。ただし理由を取り違えないこと。**
   配布物サイズを理由にすると誤り＝ROCm wheel 合計 1.1〜1.3 GiB < CUDA torch 1.9 GiB。
   正しい理由は ⑴ 利用者の 72.88% が NVIDIA・18.68% が AMD、⑵ AMD 公式が Windows で「PyTorch のみ／訓練なし／Python 3.12 のみ／`torch.distributed` 不可／batch 1 のみ」と明記、⑶ **司令官機の構成（ROCm 10.0.0 / torch 2.13.0）はそもそも AMD 公式サポート外の `whl-next` チャネル**＝手順書は「無保証」と明記して残すのが筋。
2. **⒜ で CUDA 版を選ぶ際、`cu128` は既に行き止まり**（索引の最終が torch 2.11.0）。`cu126`（2482 MiB・ドライバ 527.41+）か `cu130`（1898 MiB・ドライバ **580+**）の二択で、**ドライバ要件と配布サイズが逆相関**する。初心者向けなら「小さいが新ドライバ必須」の cu130 が有利に見えるが、ドライバ更新を促す UI が要る。
3. **⒝ の本命は CUDA EP ではなく DirectML EP**。23.9 MiB 自己完結・DX12 対応 GPU は Steam の 91.45%・NVIDIA/AMD/Intel を 1 本で覆う＝**「CUDA か ROCm か」の分岐自体が消える**。BRIEF §4-7 の軸（配布物・導入分岐・検証機・保守）を**全部**改善する唯一の道。
4. ただし DirectML には二つの棘＝⑴ pip/NuGet が **1.24.4 で 5 マイナー版遅れ**、⑵ 「memory pattern 最適化・並列実行 非対応」「同一 session への多重 Run 不可」「動的 shape は override 前提」。**TTS の可変長入力とストリーミングに直撃しうる**＝⒝ を推すなら、この 3 点の実測が最初の便になる。
5. **Windows ML は Win11 24H2 以上**＝Steam 2026-08 で Win10 が 22.90%。魅力的（EP を OS が配る）だが、単独では初心者配布の解にならない。DirectML EP 同梱との二段構えが要る。
6. **NVIDIA 実機は買わなくてよい**。GitHub Actions の `windows_4_core_gpu` が $0.102/分＝30 分の検証で約 $3。これで「検証機の要否」の軸は CUDA 側の減点をほぼ消せる。
7. **torchcodec の `<0.11.0` 固定**（`requirements.txt:15`、現行 0.16.0）と **torchaudio が torch に追随しない上流事情**（torch 2.14.0 に対し torchaudio 2.11.0 が最新）は、CUDA/ROCm を問わず ⒜⒞ の保守を重くする。⒝（ONNX 化）だけがこの鎖から抜ける。
8. **未確認として残したもの**: 日本市場固有の GPU ベンダ比／配信者限定の統計／DirectML EP の fp16 明記／Windows での ROCm + Triton・torch.compile の実効性／素の ORT での Windows MIGraphX EP／Windows GPU クラウドの Windows ライセンス込み時間単価／Irodori-TTS を ONNX 化した後のモデルサイズ。
