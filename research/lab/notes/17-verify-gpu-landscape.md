# 17 — 10-gpu-landscape.md の敵対検分（数値主張の一次照合）

席＝敵対検分席（opus・第 2 便）。対象＝`lab/notes/10-gpu-landscape.md`。
取得日時＝**2026-09-04 JST 02:13〜02:50**（curl の `Date:` 実測 `Thu, 03 Sep 2026 17:13:20 GMT` に始まる一連）。
方針＝各主張を潰しにいく。潰せなければ **CONFIRMED**、潰せたら **REFUTED**（正しい値つき）、決めきれなければ **UNVERIFIABLE / 未確認**。
生ログと中間物＝`lab/out/verify17/`（HTML 保存・`zipcd.py`＝remote zip の中央ディレクトリを HTTP Range で読む道具・`ents-*.json`）。

## 要点（10 行以内）

1. **サイズ系の主張は 13 件すべてバイト単位で CONFIRMED**（(b)(c)(d)(f)(k)(l)(m)）。1 バイトの誤りも無い。稀に見る精度。
2. (a) Steam は **一次で全部取れた**＝ベンダ比 72.88/18.68/8.03/0.41 も DX12 91.45% も Win11 70.97/Win10 22.90 も上位 10 全 NVIDIA も **CONFIRMED**。「一次直引きは未確認」の札は**外してよい**。
3. **最大の REFUTED＝要点 4**。稼働機の `whl-next` / `torch 2.13.0+rocm10.0.0` は **AMD 公式ドキュメント記載の構成そのもの**（ROCm 10.0.0 の Windows 節）。「公式サポート外」は誤り。
4. **連鎖して REFUTED＝要点 3**。「PyTorch のみ／訓練なし／Python 3.12 のみ／batch 1」は **7.2.1 で凍結された旧 doc セット**の記述。現行 10.0.0 は Windows で Python 3.11〜3.14・ROCm Core SDK 本体も配る。
5. **REFUTED＝要点 8**。Windows ML は 24H2 限定では**ない**＝「CPU と GPU(DirectML) は全ての supported Windows で動く／24H2 が要るのは NPU と特定 GPU 向けの追加 EP」と公式が明記。
6. **REFUTED＝主席への注意 2**。「cu126 なら 527.41 で足りる」は不成立。CUDA 12.6 GA の Windows ドライバは **>=560.76**。527.41 は 12.0 GA の値。
7. **UNVERIFIABLE＝(j) の「CUDA 13.x は Windows ≥580」**。一次表は 13.x の **Windows 欄が全て `N/A`**、OS 非依存表が `>= 580` を言うのみ。Windows 固有の数値は原文に無い。
8. (g) DirectML の 3 逐語は CONFIRMED。ただし「要件」として引かれた **Windows SDK 10.0.17134.0 は Build 節**＝実行時要件ではない（引き位置の誤り）。
9. (a) の「RTX 3060 が 3.76% と 3.92% で食い違った＝要約器の誤差」も **REFUTED**＝別表（全体表 3.76 / DX12 systems 表 3.92）。誤差ではない。
10. 新事実＝`nvidia-cublas` の**最新 13.6.1.10 に win_amd64 が無い**（Windows 最新は 13.6.0.2）。ORT の CUDA EP 導入が将来ここで折れうる。

---

## 1. 判定一覧（主張 → 判定 → 根拠）

### (a) Steam Hardware Survey 2026-08 — **CONFIRMED（かつ一次に格上げ）**

| 主張 | 判定 | 一次根拠（2026-09-04 取得） |
|---|---|---|
| NVIDIA 72.88 / AMD 18.68 / Intel 8.03 / その他 0.41 | **CONFIRMED** | 一次ページ HTML 内の埋め込み JS。逐語：`series = [{"label":"NVIDIA","color":"#98b354","data":[…,[1785567600000,72.88]],"max":84.68},{"label":"AMD",…,18.68…}` （末尾点＝2026-08-01）。`lab/out/verify17/steam.html` |
| DX12 GPU 91.45%（DX11 0.41 / DX10 0.21） | **CONFIRMED** | ただし所在は**主ページではなく videocard 副ページ**。逐語：`OVERALL DISTRIBUTION OF CARDS｜APR MAY JUN JUL AUG｜DirectX 12 GPUs｜91.89%｜91.91%｜91.74%｜91.37%｜91.45%`、続けて `DirectX 11 GPUs …0.41%`、`DirectX 10 GPUs …0.21%`。<https://store.steampowered.com/hwsurvey/videocard/> |
| Win11 64bit 70.97 / Win10 64bit 22.90 | **CONFIRMED** | 主ページ OS 表。逐語：`Windows 11 64 bit｜70.97%｜+0.71%` / `Windows 10 64 bit｜22.90%｜-0.40%`（Windows 計 93.95%、Win7 64bit 0.06%） |
| 上位 10 機種が全て NVIDIA | **CONFIRMED** | 主ページ videocard 表を降順に：3060 3.76／4060 Laptop 3.63／**5070 3.56**／4060 3.42／3050 3.04／5060 2.98／5060 Laptop 2.50／5060 Ti 2.41／GTX1650 2.37／4060 Ti 2.25。**11 位 3060 Ti 2.07・12 位 3070 1.92 も NVIDIA、非 NVIDIA の初出は 13 位 Apple M2 1.91** |
| （10-note の但し書き）「3.76 と 3.92 の食い違い＝要約器の誤差」 | **REFUTED** | 誤差ではない。**3.76%＝全体表**、**3.92%＝`DIRECTX 12 SYSTEMS (WIN10 WITH DX 12 GPU)` 表**（`steam-dx.html` 逐語：`NVIDIA GeForce RTX 3060｜…｜3.88%｜3.92%｜+0.04%`）。母集団が違う二つの表 |
| 機種の**並び順** | 微修正 | 10-note は「3060 / 4060L / 4060 / 5070 / 3050 …」。実際は 5070(3.56) > 4060(3.42)。集合は一致、順序 1 箇所ずれ |

> 効き＝10-note §1-1 の「ベンダ比の一次直引きは未確認」「個別機種の小数点以下は未確認」の 2 札は**両方とも外せる**。ページの数値は JS 描画ではなく **HTML に素で埋まっている**（`series = [...]`）ので、以後も curl だけで採れる。

### (b) download.pytorch.org の wheel サイズ — **CONFIRMED（全件バイト一致）**

`curl -sIL` の `Content-Length`（cp312/cp312/win_amd64）:

| wheel | 10-note | 実測 B | 実測 MiB |
|---|---|---|---|
| `torch-2.14.0+cpu` | 118.3 | 123,996,119 | **118.3** |
| `torch-2.14.0+cu126` | 2482.2 | 2,602,771,598 | **2482.2** |
| `torch-2.14.0+cu130` | 1898.4 | 1,990,604,486 | **1898.4** |
| `torch-2.14.0+cu132` | 1901.4 | 1,993,738,732 | **1901.4** |
| `torch-2.11.0+cu128` | 2625.6 | 2,753,189,216 | **2625.6** |
| `torch-2.9.1+cu128` | 2729.4 | 2,862,033,275 | **2729.4** |
| `libtorch-win-shared-with-deps-2.14.0+cu130.zip` | 3752.1 | 3,934,311,758 | **3752.1** |
| `libtorch-…+cpu.zip` | 191.8 | 201,165,018 | **191.8** |

索引の cp312/win_amd64 最終版（`sort -V` 実測）＝`cpu` `cu126` `cu130` `cu132` は **2.14.0**、**`cu128` は 2.11.0**、`cu129` は 2.9.0。→「cu128 は袋小路」**CONFIRMED**。

### (c) torch cu130 wheel が CUDA/cuDNN を同梱 — **CONFIRMED（中央ディレクトリを実読）**

`lab/out/verify17/zipcd.py` で ZIP64 EOCD → 中央ディレクトリを Range 取得して全 12,247 エントリを解析（`ents-cu130.json`）。

- `eocd_entries=12247 / parsed=12247`（10-note の 12,247 と一致）
- **DLL 39 個・展開後合計 2,870,497,880 B ＝ 2737.5 MiB**（10-note と一致）。39 個すべてが `torch/lib/` 配下
- `torch/lib/cublasLt64_13.dll` **455.8 MiB**、`torch_cuda.dll` 403.1、`torch_cpu.dll` 291.8、`cufft64_12.dll` 271.2、`cudnn_engines_precompiled64_9.dll` 211.6、`cusparse64_12.dll` 143.4 — 全て 10-note と一致
- **CUDA ランタイム本体 `cudart64_13.dll` 0.4 MiB / `cudnn64_9.dll` 0.3 MiB / `nvrtc64_130_0.dll` 86.7 / `nvJitLink_130_0.dll` 84.1 が実在**＝「CUDA Toolkit を別途入れさせなくてよい」の断定は支持される

### (d) AMD の Windows ROCm wheel — **CONFIRMED（サイズ全件一致）／リダイレクト先は 1 箇所訂正**

| wheel（cp312 or py3-none / win_amd64） | 10-note | 実測 B | 実測 MiB |
|---|---|---|---|
| `torch-2.13.0+rocm10.0.0` | 108.2 | 113,440,240 | **108.2** |
| `amd_torch_device_gfx1151-2.13.0+rocm10.0.0` | 47.7 | 50,010,016 | **47.7** |
| `amd_torch_device_gfx115x-2.13.0+rocm10.0.0` | 179.1 | 187,800,925 | **179.1** |
| `torchaudio-2.11.0.2+rocm10.0.0` | 2.0 | 2,086,475 | **2.0** |
| `rocm_sdk_core-10.0.0` | 722.9 | 758,050,981 | **722.9** |
| `rocm_sdk_libraries-10.0.0` | 111.4 | 116,831,755 | **111.4** |
| `rocm_sdk_device_gfx1151-10.0.0` | 132.9 | 139,346,623 | **132.9** |

合計＝gfx1151 のみ **1125.1 MiB**／gfx115x 込み **1304.2 MiB**（10-note の「≒1125／≒1304」と一致。torchaudio 2.0 を含めた数）。

- 訂正 1：`stable.repo.amd.com/rocm/whl-next/` の 301 先は**一様ではない**。`torch` `torchaudio` `amd-torch-device-*` → **`/rocm/pytorch/whl-next/`**、`rocm-sdk-core` `rocm-sdk-libraries` `rocm-sdk-device-gfx1151` → **`/rocm/core/whl-next/`**。
- `download.pytorch.org` の rocm 索引に win_amd64 が無い＝**CONFIRMED**。`rocm3.7`〜`rocm7.14` の **全 26 索引で `win_amd64` grep 0 件**、`manylinux` は 32〜68 件。

### (e) AMD 公式 docs（radeon-ryzen 7.2.1）— **逐語は CONFIRMED／だが「現行の公式見解」としては REFUTED**

逐語（`lab/out/verify17/160c78ac.html`・`4264dd9f.html`、2026-09-04 取得）:

- 「**To install the following wheels, Python 3.12 must be installed.**」
- 「**For the 7.2.1 PyTorch on Windows release, the 26.2.2 graphics driver must be installed.**」
- index＝`https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/`、wheel＝`torch-2.9.1%2Brocm7.2.1-cp312-cp312-win_amd64.whl`
- 「**Only Pytorch is currently available on Windows - the rest of the ROCm stack is only supported on Linux.**」
- 「**The torch.distributed module is currently not supported.** Some functions from diffusers and accelerate module may get affected.」
- 「**On Windows, only LLM batch sizes of 1 are officially supported.**」／「No ML training support. Only Python 3.12 is supported.」

**だが同じ 2 ページの冒頭に、10-note が拾っていない一文がある（逐語）**：

> 「**This ROCm on Radeon™ and Ryzen™ documentation covers releases through 7.2.1. Starting with ROCm Core SDK 7.13.0, documentation is unified across all supported hardware. For more information, see the current ROCm documentation.**」

＝この doc セットは **7.2.1 で凍結**され、7.13.0 以降は統合 doc に移っている。`…/en/latest/` を開いても中身は 7.2.1 のままなので「latest だから現行」は成り立たない。

**現行（統合）doc の実際**（`rocm.docs.amd.com/en/latest/` ＝ ヘッダ「**AMD ROCm 10.0.0**」）:

- `install/rocm.html` の導入法比較表（逐語）：「**pip｜Linux Windows｜Python and ML workflows (PyTorch, JAX)**」「**Tarball｜Linux Windows**」
- Windows 節の実コマンド（逐語）：`py -3.12 -m venv .venv` / `.venv\Scripts\activate` / `python -m pip install --index-url https://stable.repo.amd.com/rocm/whl-next/ "rocm[libraries,device-all]==10.0.0"`
- 検証例の出力に **`Name: AMD Radeon(TM) 8060S Graphics`**（＝司令官機の iGPU）が載っている
- `rocm.docs.amd.com/projects/ai-ecosystem/en/latest/frameworks/pytorch/install.html`：**`data-show-cond="{"os": ["windows"]}"` で囲まれたブロックの中**に
  `python -m pip install --index-url https://stable.repo.amd.com/rocm/whl-next/ "torch[device-gfx1151]==2.13.0+rocm10.0.0" "torchvision[device-gfx1151]==0.28.0+rocm10.0.0" "torchaudio==2.11.0.2+rocm10.0.0"`
  対応 HW 一覧に「**AMD Ryzen AI Max+ 395 (Radeon 8060S) (gfx1151)**」
- venv タブは **Python 3.14 / 3.13 / 3.12 / 3.11**（Windows 側は `py -3.14` … `py -3.11`）。索引実測でも `torch-2.13.0+rocm10.0.0` は **cp310・cp311・cp312・cp313・cp314** の win_amd64 が揃う
- 同ページの Known issues に gfx1151 関連で唯一あるのは（逐語）「Lower-than-expected performance might be observed in some large language model inference workloads, including **vLLM FP16 decode workloads with batch sizes of 8 or greater**, on … **AMD Ryzen AI MAX / MAX+ Series Processors** when using PyTorch versions **earlier than 2.14**. As a workaround, set the `TORCH_BLAS_PREFER_HIPBLASLT=1` environment variable」＝「batch 1 のみ」ではなく「batch 8 以上で性能が出にくい・回避策あり」

→ **要点 4「公式サポート外」・§2-3 の「重大」札・主席への注意 1-⑶ は REFUTED。**
→ **要点 3「Python 3.12 のみ／訓練なし／batch 1 のみ」も、現行 10.0.0 の Windows には当たらない（凍結 doc の記述）。**
→ 残る 未確認：統合 doc に **Windows 専用の limitations ページが在るか**（PyTorch ページ内に `limitat*` へのリンクは 0 件）。「7.2.1 の制限が全部撤回された」とは言えない。言えるのは「**現行 10.0.0 の doc はそれらを繰り返しておらず、それらを述べる doc は 7.2.1 で凍結されている**」まで。

### (f) PyPI / NuGet のサイズと版 — **CONFIRMED（全件一致・公開日も一致）**

| 対象 | 10-note | 実測 |
|---|---|---|
| `onnxruntime` 1.29.0 cp312 win | 13.4 MiB / 2026-08-17 | 14,001,407 B ＝ **13.4 MiB**／`2026-08-17T22:53:56` |
| `onnxruntime_gpu` 1.29.0 | 141.3 MiB | 148,173,233 B ＝ **141.3 MiB** |
| `onnxruntime_directml` **1.24.4** / 2026-03-17 | 23.9 MiB | 25,111,930 B ＝ **23.9 MiB**／`2026-03-17T21:47:18`。**PyPI の latest は今も 1.24.4** |
| NuGet `Microsoft.ML.OnnxRuntime.DirectML` | 1.24.4 / 11.9 MiB | **1.24.4** / 12,458,649 B ＝ **11.9 MiB** |
| NuGet `Microsoft.ML.OnnxRuntime` | 1.29.0 / 147.8 MiB | **1.29.0** / 155,008,437 B ＝ **147.8 MiB** |
| NuGet `Microsoft.Windows.AI.MachineLearning` | 2.3.42 / 49.1 MiB | **2.3.42** / 51,521,056 B ＝ **49.1 MiB** |
| （ついでに）`…Gpu.Windows` 134.5 / `…Managed` 1.0 / `WindowsAppSDK.ML` 2.1.74 0.1 | — | 全て一致 |

補強（10-note §3-1・§4-4 の裏取り）：`onnxruntime_directml-1.24.4` の中央ディレクトリ実読＝DLL は **3 個だけ**（`onnxruntime.dll` 20.1 / **`DirectML.dll` 17.7** / `onnxruntime_providers_shared.dll`）、`requires_dist` は `['flatbuffers','numpy>=1.21.6','packaging','protobuf','sympy']` ＝ **NVIDIA/AMD 系依存ゼロ＝自己完結は CONFIRMED**。
`onnxruntime_gpu-1.29.0` も DLL 4 個のみ（`onnxruntime_providers_cuda.dll` **164.0 MiB**・`onnxruntime.dll` 16.9・`onnxruntime_providers_tensorrt.dll` **0.8**・shared）＝ **CUDA を同梱しない**も CONFIRMED。

### (g) ORT の EP docs 逐語 — **CONFIRMED 3 件／引き位置の誤り 1 件**

<https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html>（2026-09-04）

- **CONFIRMED**「Note: DirectML is in sustained engineering. DirectML continues to be supported, but new feature development has moved to WinML for Windows-based ONNX Runtime deployments.」（続きに「WinML provides the same ONNX Runtime APIs while dynamically selecting the best execution provider based on your hardware.」）
- **CONFIRMED**「The DirectML execution provider does not support the use of memory pattern optimizations or parallel execution in onnxruntime.」＋「When supplying session options … these options must be disabled or an error will be returned」（＝任意ではなく必須設定）
- **CONFIRMED**「as the DirectML execution provider does not support parallel execution, it does not support multi-threaded calls to Run on the same inference session. That is, … only one thread may call Run at a time. Multiple threads are permitted to call Run simultaneously if they operate on **different inference session objects**.」
- **CONFIRMED（opset）**「The DirectML Execution Provider currently uses **DirectML version 1.15.2** and supports up to **ONNX opset 20 (ONNX v1.15)** with the exception of Gridsample 20: 5d and DeformConv, which are not yet supported. Evaluating models which require a higher opset version is unsupported and will yield poor performance.」
- **REFUTED（引き位置）** 10-note §3-3 が「要件」として並べた 3 行は逐語ではない。原文は Requirements 節が「The DirectML execution provider **requires a DirectX 12 capable device**. … DirectML was introduced in **Windows 10, version 1903**, and in the corresponding version of the Windows SDK.」で、「**The Windows 10 SDK (10.0.17134.0) …**」は **Build 節（ORT を自前ビルドする条件）**。実行時要件として書くと誤り。

ROCm EP（同サイト・2026-09-04）＝ **CONFIRMED**「ROCm Execution Provider has been removed since 1.23 release. Please Migrate your applications to use the MIGraphX Execution Provider」「ROCm 7.0 is the last offiicaly AMD supported distribution of this provider and all builds going forward (ROCm 7.1+) will have ROCm EP removed.」（原文の綴り誤り `offiicaly` を 10-note はそのまま写している＝正しい）

### (h) Windows ML — **EP 一覧は CONFIRMED／「24H2 以上限定」は REFUTED**

出典＝`MicrosoftDocs/windows-ai-docs` の raw md（2026-09-04 取得）。

- **CONFIRMED**（`supported-execution-providers.md`）「Included execution providers … * CPU * DirectML (legacy)」／「The execution providers listed below are available on **Windows 11 PCs running version 24H2 (build 26100) or greater** … for dynamic download via the Windows ML `ExecutionProviderCatalog` APIs.」
- **CONFIRMED**（EP 一覧）MIGraphX (AMD) / NvTensorRtRtx (NVIDIA) / OpenVINO (Intel) / QNN (Qualcomm) / VitisAI (AMD)。2.x 系の現行版は MIGraphX MSIX `1.8.55.0`、NvTensorRtRtx `0.0.28.0`、OpenVINO `1.8.69.0`（OpenVINO 2026.0）、QNN `2.2420.43.0`、VitisAI `1.8.59.0`。EP の更新は Windows Update の「D week releases」で配る
- **REFUTED**（`overview.md` System requirements・逐語）
  > 「**OS**: Version of Windows that Windows App SDK supports」「**Architecture**: x64 or ARM64」「**Hardware**: Any PC configuration (CPUs, integrated/discrete GPUs, NPUs)」
  > NOTE: 「**Support for CPU and GPU (via DirectML) is available on all supported Windows versions.** Hardware-optimized execution providers for NPUs and specific GPU hardware require Windows 11 version 24H2 (build 26100) or greater.」

  ＝**Windows ML 自体は 24H2 限定ではない**。24H2 が要るのは「動的取得する NPU/特定 GPU 向け EP」だけ。Win10 でも Windows ML の CPU＋DirectML GPU 経路は動く。
  → 要点 8「Steam の Win10 22.90% を落とす」・主席への注意 5「単独では初心者配布の解にならない」は**書き直しが要る**。
  → 未確認＝Windows App SDK が正確にどの Windows 版まで下がるか（`/windows/apps/windows-app-sdk/support` を辿っていない）。

### (i) GitHub Actions の GPU ランナー単価 — **CONFIRMED（逐語）**

<https://docs.github.com/en/billing/reference/actions-minute-multipliers>（2026-09-04）
逐語（表の生テキスト）：`GPU-powered larger runners｜Operating system｜Billing SKU｜Per-minute rate (USD)｜Linux 4-core｜linux_4_core_gpu｜$0.052｜Windows 4-core｜windows_4_core_gpu｜$0.102`
→ **Windows GPU ランナーが公式に存在する**という 10-note の要点も CONFIRMED。

### (j) NVIDIA のドライバ要件 — **一部 CONFIRMED／一部 UNVERIFIABLE／派生主張は REFUTED**

出典＝`docs.nvidia.com/cuda/cuda-toolkit-release-notes/index.html` と `…/deploy/cuda-compatibility/minor-version-compatibility.html`（2026-09-04）。

- **CONFIRMED**（release notes の実表・Windows x86_64 欄）：`CUDA 12.0 GA｜>=525.60.13｜**>=527.41**`、`CUDA 12.2 Update 2｜>=535.104.05｜**>=537.13**`
- **CONFIRMED**（minor-version-compatibility Table 1・逐語）：`CUDA 13.x｜**>= 580**｜N/A (backward compatibility applies for newer drivers)` / `CUDA 12.x｜**>= 525**｜< 580` / `CUDA 11.x｜>= 450｜< 525`。**この表に OS 別の列は無い**（OS 別列があるのは CUDA 10.x 以前の表だけ）
- **UNVERIFIABLE**「CUDA 13.x は **Windows** ≥580」：release notes の実表では **CUDA 13.0 GA〜13.3 Update 1 の Windows x86_64 欄が全て `N/A`**。Windows 固有の最小値は原文に無い。同ページの但し書き（逐語）「**Starting with CUDA 13.1, the Windows display driver is no longer bundled with the CUDA Toolkit.**」「**Some compatibility tables may list "N/A" for Windows driver versions. Users must still ensure the installed driver meets or exceeds the minimum required version for the CUDA Toolkit.**」→ 「580 以上」は OS 非依存表からの妥当な推定だが、**Windows の数字として断定はできない**
- **CONFIRMED**（minor version compatibility の逐語）「From CUDA 11 onwards, applications compiled with a CUDA Toolkit release from within a CUDA major release family can run, **with limited feature-set**, on systems having at least the minimum required driver version as indicated below.」
- **REFUTED（派生主張・§6 含意と主席への注意 2）**「torch cu126 を選べば **527.41 以上で足りる**＝古い機体を落とさない」
  - **CUDA 12.6 GA の Windows 最小ドライバは `>=560.76`**、12.6 Update 1/2 は `>=560.94`、**12.6 Update 3 は `>=561.17`**（release notes 実表）
  - 527.41 は CUDA **12.0 GA** の値。12.6 でビルドされた torch が 527.41 で動くのは *minor version compatibility*（「with limited feature-set」）に賭ける話であって、**torch wheel で実際に成立するかは未確認**
  - → 「サイズと対応ドライバ幅が逆相関」という綺麗な構図は**そのままでは書けない**。正しくは「cu126＝2482 MiB／Windows 560.76+（12.0 GA まで遡れるかは未検証）」対「cu130＝1898 MiB／580 相当」
- **CONFIRMED**「onnxruntime-gpu 1.27 以降の既定が CUDA 13 / cuDNN 9」：PyPI メタデータ実測で境界が正確に一致
  - `1.26.0` extra `cuda`＝`nvidia-cuda-nvrtc-cu12~=12.0` 等・`cudnn`＝`nvidia-cudnn-cu12~=9.0`
  - `1.27.0` extra `cuda`＝`nvidia-cuda-nvrtc-**cu13**~=13.0` 等・`cudnn`＝`nvidia-cudnn-**cu13**~=9.0`
  - `1.28.0`／`1.29.0` は接尾辞なし `nvidia-cuda-nvrtc~=13.0` 等（実体は 13 系）

### (k) python embeddable zip — **CONFIRMED（かつ 10-note の「未確認」札を外せる）**

- `python-3.12.10-embed-amd64.zip` ＝ **11,133,606 B ＝ 10.6 MiB**（10-note と一致）
- `3.12.11 / 3.12.12 / 3.12.13 / 3.12.14` はいずれも **`HTTP/1.1 404 Not Found`・Content-Length 146**（10-note の「146 B の応答」＝404 本文）
- 決定打＝`https://www.python.org/ftp/python/3.12.14/` のディレクトリ一覧に置かれているのは **`Python-3.12.14.tar.xz` / `.tgz` と署名類のみ**。Windows バイナリが 1 個も無い（3.12 系がソース配布のみのフェーズに入った）
  → 「3.12 系の埋め込み配布は 3.12.10 で固定」は **未確認 → CONFIRMED** に格上げしてよい
- 逃げ道（10-note に無い情報）＝`python-3.13.9-embed-amd64.zip` は存在し **10,926,675 B ＝ 10.4 MiB**。3.12 に縛られる理由が消えれば 3.13 系の埋め込みは使える

### (l) CUDA EP の実費内訳 — **CONFIRMED（全件一致）／将来の穴を 1 つ発見**

| wheel（win_amd64） | 版 | 10-note | 実測 B | 実測 MiB |
|---|---|---|---|---|
| `nvidia_cudnn_cu13` | 9.25.1.1 | 388.9 | 407,762,053 | **388.9** |
| `nvidia_cublas` | 13.6.0.2 | 376.3 | 394,568,225 | **376.3** |
| `nvidia_cufft` | 12.3.0.29 | 175.4 | 183,939,745 | **175.4** |
| `nvidia_curand` | 10.4.3.29 | 52.9 | 55,429,221 | **52.9** |
| `nvidia_cuda_nvrtc` | 13.3.33 | 43.2 | 45,319,163 | **43.2** |
| `nvidia_nvjitlink` | 13.3.33 | 36.0 | 37,766,359 | **36.0** |
| `nvidia_cuda_runtime` | 13.3.29 | 2.5 | 2,630,354 | **2.5** |
| **小計** | | 1075.2 | | **1075.2** |
| ＋`onnxruntime_gpu` 1.29.0 | | 141.3 | | **141.3** |
| **合計** | | ≒1216.5（1.19 GiB） | | **1216.5 MiB ＝ 1.188 GiB** |

`nvidia-cublas` が入る筋も確認＝`nvidia-cudnn-cu13 9.25.1.1` の `requires_dist` は **`['nvidia-cublas']`** の 1 件のみ。

- **新発見（10-note に無い・保守の穴）**：`nvidia-cublas` の PyPI **最新は 13.6.1.10 で、win_amd64 wheel が存在しない**（manylinux aarch64/x86_64 のみ）。Windows 用の最新は **13.6.0.2**。同様に 13.4.1.1 も Windows 欠番。
  → `pip install "onnxruntime-gpu[cuda,cudnn]"` を Windows で素に打つと、cublas の解決が版によって落ちうる。⒝ で CUDA EP を採るなら **`nvidia-cublas` を明示ピン**しないと壊れる時期がある。

### (m) torchaudio / torchcodec の版ずれ — **CONFIRMED**

- `download.pytorch.org/whl/cpu/torchaudio/` の cp312/win_amd64 最新＝**2.11.0**（2.9.0 → 2.9.1 → 2.10.0 → 2.11.0 で止まる）。`…/cu130/torchaudio/` も同じく **2.11.0**
- 同索引の torch は **2.14.0**＝「torchaudio が torch に追随していない」は **CONFIRMED**（ROCm 固有ではなく上流全体、も CONFIRMED）
- `torchcodec` cpu の cp312/win_amd64 最新＝**0.16.0**（0.13.0 → 0.14.0 → 0.15.0 → 0.16.0）。上流の釘 `torchcodec>=0.10.0,<0.11.0` との乖離も CONFIRMED

---

## 2. 主席への注意（報告で書き直すべき箇所）

1. **要点 4 と §2-3 の「重大」札を撤回すること。** 「司令官機の ROCm 10.0.0 / torch 2.13.0 は AMD 公式サポート外の `whl-next` 先行チャネル」は **REFUTED**。
   現行の統合 doc（`rocm.docs.amd.com/en/latest/` ＝ **AMD ROCm 10.0.0**）が、Windows 用の pip 経路として `--index-url https://stable.repo.amd.com/rocm/whl-next/` を、`os: ["windows"]` 条件下で `"torch[device-gfx1151]==2.13.0+rocm10.0.0"` を、対応 HW に「AMD Ryzen AI Max+ 395 (Radeon 8060S) (gfx1151)」を、それぞれ**明示している**。司令官機の構成は公式手順そのもの。
   従って **主席への注意 1 の理由 ⑶ は成り立たない**。「手順書は無保証と明記」も撤回対象。

2. **要点 3 と主席への注意 1 の理由 ⑵ を書き直すこと。** 「PyTorch のみ／訓練なし／Python 3.12 のみ／`torch.distributed` 不可／batch 1 のみ」は逐語としては正しいが、**出典が 7.2.1 で凍結された旧 doc セット**（当該ページ冒頭に「covers releases through 7.2.1 … unified across all supported hardware」と明記）。
   現行 10.0.0 は Windows で **Python 3.11〜3.14**（wheel も cp310〜cp314）を出し、**ROCm Core SDK 本体を Windows に pip/tarball で入れる手順**を持ち、gfx1151 の既知問題は「vLLM FP16 decode の batch 8 以上で性能が出にくい（torch<2.14・`TORCH_BLAS_PREFER_HIPBLASLT=1` で回避）」に留まる。
   ただし **「7.2.1 の制限が全部撤回された」とは書かないこと**＝統合 doc に Windows 専用 limitations ページを見つけられていない（未確認）。書けるのは「現行 doc はそれらを繰り返していない／それらを述べる doc は凍結されている」まで。
   **裁定への影響**：「ROCm を切る理由は成熟度」という結論は**弱くなる**。切るなら理由を「利用者分布（72.88% 対 18.68%）」「NVIDIA 実機が無い」「検証コストの二重化」に寄せ直すのが安全。

3. **主席への注意 2 の「cu126 なら 527.41 で足りる」を撤回すること。** **CUDA 12.6 GA の Windows 最小ドライバは >=560.76／12.6 U3 は >=561.17**。527.41 は 12.0 GA の値で、cu126 wheel がそこまで遡れるかは未検証（minor version compatibility は「with limited feature-set」）。
   併せて **「CUDA 13.x は Windows ≥580」も断定しないこと**＝一次表の 13.x は Windows 欄が全て `N/A`（13.1 以降 Windows ドライバを Toolkit に同梱しなくなったため）。OS 非依存表の `>= 580` からの推定である旨を添える。

4. **要点 8 と主席への注意 5 を書き直すこと。** 「Windows ML は Win11 24H2 以上限定」は **REFUTED**。公式の逐語は「**Support for CPU and GPU (via DirectML) is available on all supported Windows versions.** Hardware-optimized execution providers for NPUs and specific GPU hardware require Windows 11 version 24H2 (build 26100) or greater.」＝24H2 が要るのは**動的取得する EP だけ**。
   → Windows ML は Win10 を落とさない。「DirectML EP 同梱との二段構えが要る」は**不要**（Windows ML 自身が DirectML を内蔵 EP として持つ）。⒝ の推奨は**むしろ強まる**方向。

5. **§3-3 の「要件」3 行の引き位置を直すこと。** 「Windows SDK 10.0.17134.0 or newer」は **Build 節**（ORT を自前ビルドする条件）で、実行時要件ではない。実行時に原文が言うのは「DirectX 12 capable device を要する」「DirectML は Windows 10 1903 で導入された」の 2 点。

6. **§1-1 の 2 つの「未確認」札を外せること。** ⑴ Steam のベンダ比は**一次で直引きできる**（ページ HTML に `series = [{"label":"NVIDIA",…,72.88]…` が素で埋まっている）。⑵ 「3.76 と 3.92 の食い違いは要約器の誤差」は誤り＝**全体表（3.76）と DIRECTX 12 SYSTEMS 表（3.92）という別の 2 表**。上位 10 の集合は 10-note の通りで正しい（順序のみ 5070 が 4060 の前）。

7. **§4-1 の「3.12.10 が最後」の未確認札も外せること。** `python.org/ftp/python/3.12.14/` にはソース tarball と署名しか無い＝Windows バイナリが 1 個も無い。あわせて **`python-3.13.9-embed-amd64.zip`（10.4 MiB）は存在する**ので、⒜ が Python 3.12 に縛られる理由が消えれば 3.13 に上げられる、と 1 行添えておくと退路になる。

8. **⒝ で CUDA EP を採る場合の追加の棘を 1 行足すこと。** `nvidia-cublas` の PyPI 最新 **13.6.1.10 に win_amd64 wheel が無い**（Windows 最新は 13.6.0.2）。`onnxruntime-gpu[cuda,cudnn]` の素の解決が版によって Windows で落ちうるので、**cublas は明示ピン**が要る。

9. **潰せなかったもの（そのまま使ってよい）**：(b)(c)(d)(f)(k)(l)(m) のサイズ・版・個数は**全件バイト一致**。(g) の DirectML 3 逐語・opset 20・ROCm EP 廃止告知、(h) の EP 一覧、(i) の $0.052/$0.102、(a) の 4 数値はいずれも一次で確認済み。§4-3 の「ROCm 1.1〜1.3 GiB < CUDA 1.9 GiB」も**成立**＝「サイズを理由に ROCm を切るのは誤り」という 10-note の芯は**無傷**。

10. **残る未確認（検分でも解けなかったもの）**：統合 ROCm doc の Windows 専用 limitations の有無／Windows App SDK が下限とする Windows 版／CUDA 13.x の Windows 固有最小ドライバ／torch cu126 wheel が実際に 527.41 で動くか／forward compatibility パッケージの Windows 可否／日本市場・配信者限定の GPU ベンダ比／DirectML EP の fp16 明記／Windows での ROCm + Triton・torch.compile の実効性。

---

## 3. 再現手順（この検分をやり直す人向け）

```sh
# サイズ系（全て HEAD の Content-Length）
curl -sIL "https://download.pytorch.org/whl/cu130/torch-2.14.0%2Bcu130-cp312-cp312-win_amd64.whl" | grep -i content-length

# wheel の中身（ダウンロードせず中央ディレクトリだけ Range 取得）
python lab/out/verify17/zipcd.py "<wheel url>" out.json

# Steam のベンダ比（JS 描画ではない・HTML に素で入っている）
curl -sL -A "Mozilla/5.0" "https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam" \
  | grep -o '{"label":"NVIDIA".\{0,900\}'
# DX12 GPUs の割合は videocard 副ページ側
curl -sL -A "Mozilla/5.0" "https://store.steampowered.com/hwsurvey/videocard/" | grep -o 'DirectX 12 GPUs.\{0,200\}'

# PyPI の依存メタデータ（extras の CUDA 世代）
curl -s https://pypi.org/pypi/onnxruntime-gpu/1.27.0/json | python -c "import json,sys;[print(r) for r in json.load(sys.stdin)['info']['requires_dist']]"
```

保存済みの一次 HTML／JSON は `lab/out/verify17/` にある（`steam.html` `steam-vc.html` `steam-dx.html` `160c78ac.html`＝AMD install-pytorch 7.2.1、`4264dd9f.html`＝AMD limitationsryz、`rocm-install.html` `pt-rocm.html` `ort-dml.html` `ort-rocm.html` `winml-eps.md` `winml-ov.md` `gha.html` `cuda-rn.html` `cuda-mvc.html` `pypi-*.json` `ents-*.json`）。
`upstream/` ・ `yomiwakechan2/` ・ `C:/IrodoriTTS/` ・ HF キャッシュには一切触れていない。GPU 不使用・稼働中サーバへの送信なし。
