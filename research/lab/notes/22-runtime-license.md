# 22 実行系の再配布条件（BRIEF §2-2 の門・Irodori の鎖 5 段の「外側」）

第 3 便サブ席（opus）／作成 2026-09-04。取得日時は全て **2026-09-04 03:08〜03:15 JST（＝2026-09-03 18:08〜18:15 UTC）**。
取得方法＝`curl -sS -L`（docs.nvidia.com / learn.microsoft.com / docs.python.org / nuget.org / api.github.com / raw.githubusercontent.com）と
**HTTP Range による remote wheel の実読**（`lab/tmp/rt-license/zipget.py`＝`lab/out/verify17/zipcd.py` に local header offset 取得と本文抽出を足したもの）。
生檔は `C:\Users\mugonkun\source\repos\irodori-native-research\lab\tmp\rt-license\` に保存。バイト数は全て実測。
本便は**実行系のみ**を担当＝Irodori 本体・重み・コーデック・透かしの鎖 5 段は 09／16 の担当で、本便は触れていない。

## 要点（10 行以内）

1. **実行系の門は塞がらない**＝CUDA・cuDNN・MSVC・DirectML・libsndfile・PSF・Apache 系のいずれにも**明文の再配布許諾がある**。ただし全て条件付きで、条件は 4 種（①ライセンス本文を添える ②自作機能を持つこと ③改変しないこと ④LGPL のソース入手手段）。
2. **最大の発見＝PyTorch は NVIDIA の DLL を同梱するが、その license 文を wheel に入れていない**。`torch-2.14.0+cu130` の `METADATA:7` は `License-Expression: Apache-2.0 AND Apache-2.0 WITH LLVM-exception AND BSD-2-Clause AND BSD-3-Clause AND BSL-1.0 AND MIT`＝**NVIDIA 系が 1 語も無い**。`License-File:` は 94 件あるが **CUDA EULA も cuDNN SLA も含まれない**（NVIDIA 由来は cudnn_frontend（MIT・ヘッダのみ）と NVTX（Apache-2.0+LLVM 例外）の 2 件だけ）。**⒜ で torch を再配布する側が自分で添える必要がある。**
3. **CUDA EULA Attachment A に cudnn は 1 語も無い**（EULA 全文の grep で 0 件）。cuDNN は**別 SLA**で、そちらの §2 が「the runtime files .so and .dll」を distributable と明記＝2 枚必要。
4. **新規の穴＝`msvcp140.dll`**。`torch/lib/c10.dll` の PE インポートテーブルを直読すると **MSVCP140.dll / VCRUNTIME140.dll / VCRUNTIME140_1.dll** を要求。python-embed は後 2 者を同梱するが **msvcp140.dll を持たない**＝⒜ は MSVC 再頒布（vc_redist.x64.exe 等）が**別途要る**。10-note §4-6 の見積に無い項目。
5. **DirectML.dll は MIT ではない**。GitHub `microsoft/DirectML` の LICENSE は MIT だが、実バイナリを配る NuGet `Microsoft.AI.DirectML` の license は「MICROSOFT SOFTWARE LICENSE TERMS — MICROSOFT DIRECTX MACHINE LEARNING (DIRECTML)」。再配布は明文で可だが **Windows/Xbox 限定**、通知の改変禁止、単体配布禁止。ORT wheel の `ThirdPartyNotices.txt`（331,175 B）にも DirectML の記載は **0 件**。
6. **python-3.12.10-embed-amd64.zip は実測 11,133,606 B・35 エントリ**。`LICENSE.txt` 36,874 B・`vcruntime140.dll` 120,400 B・`vcruntime140_1.dll` 49,776 B を**同梱**、`msvcp140.dll` は**無し**（＝4 の裏取り）。
7. **libsndfile の LGPL 義務は soundfile wheel が半分済ませてある**＝`_soundfile_data/COPYING`（26,518 B・LGPL-2.1 全文）と `licensing/license_notes.md`（2,631 B・同梱メディアライブラリの著作権とソース URL）が入っている。残るは §6 a)〜e) の**どれで満たすか**の選択のみ。soundfile は upstream 4 箇所で import＝落とせない。
8. **Apache-2.0 §4(d) の NOTICE 義務が発動するのは onnx だけ**。transformers / sentencepiece / tokenizers は repo 直下に NOTICE が無い（LICENSE のみ）。onnx は `NOTICE` 2,133 B あり。4 つとも §4(a)「a copy of this License」は要る。
9. **ROCm は wheel から license の裏取りができない**＝`rocm_sdk_core-10.0.0.dist-info/METADATA` は **59 B・3 行**（Metadata-Version / Name / Version のみ）で License 欄も分類子も無い。dacvae wheel（16 §3）と同じ穴。CUDA のみ案ならこの穴は開かない。
10. **09 §3 の torch 行の「未確認：LICENSE 全文は本便で未直読」の札は外せる**＝wheel 内 `dist-info/licenses/LICENSE` 3,548 B を直読し、BSD-3-Clause 本文 ＋ 多数の著作権表示であることを確認した。

---

## 表（対象／ライセンス／再配布条項の逐語／同梱時の義務／可否の読み・札）

> 逐語が長いものは表では要点だけ引き、次節「## 逐語（全文抜粋）」に全文を置いた。全て 2026-09-04 03:08〜03:15 JST 取得。

| # | 対象 | ライセンス | 再配布条項の逐語（URL・取得日時） | 同梱時の義務 | 可否の読み・札 |
|---|---|---|---|---|---|
| 1 | **torch 本体**（`torch-2.14.0+cu130-cp312-cp312-win_amd64.whl`・1,990,604,486 B・12,247 エントリ） | **BSD-3-Clause 本文 ＋ 多数の著作権表示**（実体 `dist-info/licenses/LICENSE` 3,548 B）／ただし `METADATA:7` の宣言は `License-Expression: Apache-2.0 AND Apache-2.0 WITH LLVM-exception AND BSD-2-Clause AND BSD-3-Clause AND BSL-1.0 AND MIT` | 「**Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.**」（wheel 内 LICENSE・`https://download.pytorch.org/whl/cu130/torch-2.14.0%2Bcu130-cp312-cp312-win_amd64.whl` を HTTP Range で実読・2026-09-04 03:09 JST） | LICENSE（3,548 B）と `third_party/` 93 檔（`METADATA:8-102` の `License-File:` 宣言と 1 対 1）を配布物に添える | **可・断定**。09 §3 の「未確認：LICENSE 全文は未直読」の**札を外せる** |
| 1b | **torch が同梱する NVIDIA DLL 26 個**（`cublasLt64_13.dll` 455.8 MiB・`cudnn_engines_precompiled64_9.dll` 211.6 MiB・`cudart64_13.dll`・`cudnn64_9.dll`・`nvrtc64_130_0.dll`・`nvJitLink_130_0.dll` 等。17 (c) の 39 DLL のうち NVIDIA 由来分） | **wheel の中では扱われていない**＝NVIDIA 由来の license 檔は `third_party/cudnn_frontend/LICENSE.txt`（1,169 B・MIT・`Copyright (c) 2020, NVIDIA CORPORATION`＝**ヘッダのみのフロントエンド API であって cuDNN 本体ではない**）と `third_party/NVTX/LICENSE.txt`（12,706 B・Apache-2.0 WITH LLVM-exception）の **2 件だけ** | wheel 全 12,247 エントリのうち名前に `nvidia|cuda|cudnn|cublas|nvrtc|nvjitlink|cufft|curand|cusparse|cusolver|nccl` を含む非コード檔は上記 1 件のみ。**`NOTICE` という名の檔は 0 件**。`METADATA` 本文で NVIDIA に触れるのは README 由来のビルド手順（:344-350 等）だけで、**再配布条件への言及は 1 行も無い** | **PyTorch はライセンス面を肩代わりしていない**＝ CUDA EULA と cuDNN SLA を**我々が自分で添える** | **可だが義務は我々に降りる・断定**。「torch を入れれば CUDA の話は終わり」は**誤り** |
| 2a | **NVIDIA CUDA Toolkit EULA**（cudart / cublas / cublasLt / cufft / cusparse / cusolver / curand / nvrtc / nvJitLink / cupti / nvToolsExt） | **NVIDIA 独自 EULA**（OSI 承認外・`LicenseRef-NVIDIA-Proprietary`） | §2.6 Attachment A: 「**The following CUDA Toolkit files may be distributed with applications developed by you, including certain variations of these files that have version number or architecture specific information embedded in the file name - as an example only, for release version 9.0 of the 64-bit Windows software, the file cudart64_90.dll is redistributable.**」（`https://docs.nvidia.com/cuda/eula/index.html`・2026-09-04 03:09 JST）。Windows 欄に `cudart.dll` / `cufft.dll, cufftw.dll` / `cublas.dll, cublasLt.dll` / `cusparse.dll` / `cusolver.dll` / `curand.dll` / `nvrtc.dll, nvrtc-builtins.dll` / `libnvJitLink.dll` / `cupti.dll` / `nvToolsExt.dll` を**明記** | §1.1.2 Distribution Requirements（4 条件・全文は次節）＝ ①「**Your application must have material additional functionality, beyond the included portions of the SDK.**」②「**The distributable portions of the SDK shall only be accessed by your application.**」③ 改変版に「This software contains source code provided by NVIDIA Corporation.」を添える ④「**The terms under which you distribute your application must be consistent with the terms of this Agreement**」。加えて §1.2「**You may not ... remove copyright or other proprietary notices from any portion of the SDK**」「**You may not use the SDK in any manner that would cause it to become subject to an open source software license**」 | **可・断定**。読み分けちゃん2 の TTS エンジンは①の "material additional functionality" を満たす。**②により CUDA DLL を利用者に汎用ライブラリとして提供する形にはできない**（アプリ専用フォルダに置く）。**札：GPL 汚染禁止条項があるため、同一配布物に GPL コードを入れると衝突する＝FFmpeg（GPL 有効版）を足す案は CUDA と両立しない（09 §3 の FFmpeg 論点と接続）** |
| 2b | **cuDNN**（`cudnn64_9.dll` ほか cudnn_* 9 個） | **NVIDIA SDK SLA ＋ cuDNN Supplement**（v. February 22, 2022） | §2 Distribution: 「**The following portions of the SDK are distributable under the Agreement: the runtime files .so and .dll.**」（`https://docs.nvidia.com/deeplearning/cudnn/sla/` → 301 → `https://docs.nvidia.com/deeplearning/cudnn/latest/reference/eula.html`・2026-09-04 03:09 JST） | Distribution Requirements は CUDA EULA と同文（4 条件）。**cuDNN の語は CUDA EULA 全文に 0 件**＝ CUDA EULA では cuDNN を配れない | **可・断定**。ただし**添える licenses は 2 枚**（CUDA EULA と cuDNN SLA）。1 枚で済ませると cuDNN 側が欠ける |
| 2c | **PyPI の nvidia-* wheel のメタデータ**（⒝ の CUDA EP 経路） | `nvidia-cublas` 13.6.1.10 のみ **`license_expression = "LicenseRef-NVIDIA-Proprietary"`**。`nvidia-cudnn-cu13` 9.25.1.1 / `nvidia-cuda-runtime` 13.3.29 / `nvidia-cuda-nvrtc` 13.3.33 / `nvidia-cufft` 12.3.0.29 / `nvidia-curand` 10.4.3.29 / `nvidia-nvjitlink` 13.3.33 は **`license` も `license_expression` も null・License 分類子 0 件** | `https://pypi.org/pypi/<name>/json` の `info.license` / `info.license_expression` / `info.classifiers`・2026-09-04 03:10 JST（生檔 `lab/tmp/rt-license/pypi-*.json`） | メタデータからは条件が読めない＝ 2a/2b の EULA/SLA に戻る以外に道が無い | **可だが wheel からは裏取り不能・札**。17 (l) の「`nvidia-cublas` を明示ピンしないと壊れる」に**ライセンス欄も 1 社しか埋まっていない**を並べられる |
| 3 | **MSVC 再頒布**（`vcruntime140.dll`・`vcruntime140_1.dll`・**`msvcp140.dll`**） | **Microsoft Software License Terms（Visual Studio）の "Distributable Code" 節 ＋ REDIST リスト** | 「**Distribution of the Visual C++ Runtime Redistributable package, merge modules, and individual binaries is limited to licensed Visual Studio users and is subject to Microsoft Software License Terms.**」（`https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files`・2026-09-04 03:09 JST）／「**Subject to the License Terms for the software, you may copy and distribute with your program any of the files within the following folder and its subfolders except as noted below. You may not modify these files. [VisualStudioFolder]\VC\redist**」（`https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution`・2026-09-04 03:11 JST） | ①配る側が **licensed Visual Studio user** であること ②**改変しない**こと ③`debug_nonredist` フォルダの中身は**配れない** ④推奨は `vc_redist.x64.exe` の同梱・実行（app-local な DLL 直置きは「For servicing reasons, we don't recommend」） | **可・ただし「licensed Visual Studio users」の読みは卓**（Community 版利用者が含まれるかは Microsoft Software License Terms 本文の読みで、本便では断定しない＝**未確認札**）。**⒜ で必要なのは事実**（下の 4 と 5 参照） |
| 3b | **torch が MSVCP140.dll を要求する（実測）** | — | `torch/lib/c10.dll`（1,113,600 B・wheel から HTTP Range で抽出）の **PE インポートテーブルを直読**＝`ADVAPI32.dll / KERNEL32.dll / **MSVCP140.dll** / dbghelp.dll / **VCRUNTIME140.dll** / **VCRUNTIME140_1.dll** / api-ms-win-crt-*`。`torch/lib/shm.dll` も VCRUNTIME140/_1 を要求（2026-09-04 03:11 JST 抽出・同日解析） | python-embed は vcruntime140/_1 を同梱するが **msvcp140.dll を持たない**（下の 4）＝ **⒜ は MSVC 再頒布が別途要る** | **断定（実測）**。10-note §4-6 の配布物見積に **vc_redist.x64.exe ≒25 MB と導入手順 1 段**を足すこと |
| 4 | **python-3.12.10-embed-amd64.zip** | **PSF License Agreement**（同梱 `LICENSE.txt` 36,874 B・702 行。PSF / BeOpen / CNRI / CWI の 4 段を含む） | PSF §2: 「**PSF hereby grants Licensee a nonexclusive, royalty-free, world-wide license to reproduce, analyze, test, perform and/or display publicly, prepare derivative works, distribute, and otherwise use Python alone or in any derivative version, provided, however, that PSF's License Agreement and PSF's notice of copyright ... are retained in Python alone or in any derivative version prepared by Licensee.**」（zip 内 LICENSE.txt:80-88・2026-09-04 03:08 JST ダウンロード）／§3「**Licensee hereby agrees to include in any such work a brief summary of the changes made to Python.**」 | `LICENSE.txt` をそのまま添えるだけ。改変したら差分の要約を添える | **可・断定**。実測＝11,133,606 B（10-note・17 (k) と一致）、sha256 `4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3`、**35 エントリ・展開後 22,501,299 B** |
| 4b | **embed zip の中身（MSVC 再頒布の一次確認）** | — | **`vcruntime140.dll` 120,400 B ／ `vcruntime140_1.dll` 49,776 B ／ `LICENSE.txt` 36,874 B ＝ いずれも在り。`msvcp140.dll` ＝ 無し**（35 エントリの全一覧を実読・生檔 `lab/tmp/rt-license/python-3.12.10-embed-amd64.zip`） | PSF が VC ランタイムの一部を既に再頒布している＝**Python 部分については我々が別途 vc_redist を配る必要は無い**。だが torch の C++ 部分（`msvcp140.dll`）は**埋まらない** | **断定（実測）**。裏取り＝`python312.dll` と `python.exe` の PE インポートは `VCRUNTIME140.dll` と `api-ms-win-crt-*`（＝OS 同梱の UCRT）のみで、**MSVCP140 を要求しない** |
| 4c | **embeddable package の位置づけ（公式 docs）** | — | 「**The embedded distribution is a ZIP file containing a minimal Python environment. It is intended for acting as part of another application, rather than being directly accessed by end-users.**」「**Third-party packages should be installed by the application installer alongside the embedded distribution. Using pip to manage dependencies as for a regular Python installation is not supported with this distribution ... In general, third-party packages should be treated as part of the application ("vendoring")**」（`https://docs.python.org/3/using/windows.html#the-embeddable-package`・2026-09-04 03:09 JST） | 「**vendoring**」＝上流が想定している使い方そのもの | **可・断定**。⒜ の埋め込み配布は python.org の**推奨用法**の範囲内 |
| 5a | **DirectML.dll**（`onnxruntime_directml-1.24.4-cp312-cp312-win_amd64.whl`・25,111,930 B・322 エントリの中に **18,527,776 B** で同梱） | **MIT ではない**＝「MICROSOFT SOFTWARE LICENSE TERMS / MICROSOFT DIRECTX MACHINE LEARNING (DIRECTML)」（NuGet `Microsoft.AI.DirectML` 1.15.4 の埋め込み license 檔。`licenseExpression` は**空**、`licenseUrl = https://www.nuget.org/packages/Microsoft.AI.DirectML/1.15.4/license`） | §1(a): 「**Subject to the terms of this agreement, you may install and use any number of copies of the software, and solely for use on Windows and Xbox. You may copy and distribute the software (i.e. make available for third parties) in applications and services you develop in the build with Machine Learning tools and frameworks, and/or games that run on Windows and Xbox.**」（`https://www.nuget.org/packages/Microsoft.AI.DirectML/1.15.4/license`・2026-09-04 03:14 JST） | §3(c)「**remove, minimize, block, or modify any notices of Microsoft or its suppliers in the software**」＝通知の改変禁止。§3(e)「**except as expressly stated in Section 1, share, publish, distribute, or lease the software, provide the software as a stand-alone offering for others to use**」＝**単体配布禁止**（アプリに組み込んで配るのは可）。§2(a) にテレメトリ条項あり | **可・断定**。**ただし「ORT は MIT だから DirectML も軽い」は誤り**。**Windows 限定**の条項があるので Linux 版を作る道は塞がる（本件は Windows 専用なので実害なし） |
| 5b | **onnxruntime-directml wheel の license 表記** | **MIT**（PyPI `info.license = "MIT License"`・分類子 `License :: OSI Approved :: MIT License`・2026-09-04 03:10 JST）。wheel 内 `onnxruntime/LICENSE` 1,094 B＝MIT 本文（`Copyright (c) Microsoft Corporation`） | wheel 内 `onnxruntime/ThirdPartyNotices.txt`（**331,175 B**）を全文検索して **`directml` の語は 0 件**（大小文字無視・2026-09-04 03:13 JST 抽出） | **DirectML.dll は wheel の MIT にも ThirdPartyNotices にもカバーされていない**＝ 5a の MSLT を**我々が自分で足す**しかない | **札：ORT の同梱通知に穴がある**。⒝ の licenses/ を組むとき見落としやすい |
| 5c | **NuGet `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4** | **MIT License**（埋め込み license 檔・本文は ORT の MIT） | `https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.DirectML/1.24.4/license`・2026-09-04 03:14 JST | C# から使う経路でも DirectML.dll 自体は 5a の MSLT | **可・断定**（NuGet 経路でも結論は同じ） |
| 6 | **libsndfile**（`soundfile-0.14.0-py2.py3-none-win_amd64.whl`・1,021,480 B・11 エントリ。`_soundfile_data/libsndfile_x64.dll` **2,364,416 B**） | **LGPL-2.1**（wheel 内 `_soundfile_data/COPYING` 26,518 B に全文同梱）。soundfile 本体は **BSD-3-Clause**（`dist-info/LICENSE` 1,512 B・`Copyright (c) 2013, Bastian Bechtold`） | LGPL-2.1 §6（COPYING を直読・2026-09-04 03:12 JST）: 「**You must give prominent notice with each copy of the work that the Library is used in it and that the Library and its use are covered by this License. You must supply a copy of this License.**」＋ a)〜e) の**いずれか 1 つ**。動的リンクの逃げ道は b)「**Use a suitable shared library mechanism for linking with the Library. A suitable mechanism is one that (1) uses at run time a copy of the library already present on the user's computer system, rather than copying library functions into the executable, and (2) will operate properly with a modified version of the library, if the user installs one, as long as the modified version is interface-compatible with the version that the work was made with.**」 | ①「libsndfile を使っている」旨の目立つ通知 ②LGPL-2.1 全文（wheel の COPYING をそのまま） ③a〜e のいずれか。**⒜ は DLL を同梱するので b) の (1)「already present on the user's computer system」に当たらない読みが自然**＝ **c)「a written offer, valid for at least three years」** か **d)「offer equivalent access to copy the above specified materials from the same place」**（＝配布サイトに libsndfile のソース zip を並べる）が安全 | **可・ただし a〜e のどれで満たすかは卓**。`licensing/license_notes.md`（2,631 B）が同梱メディアライブラリ（libFLAC BSD-3／**libmp3lame LGPL-2+**／**libmpg123 LGPL-2.1**／libogg BSD-3／libopus BSD-3／libvorbis BSD-3）の著作権とソース URL を既に列挙**＝そのまま配れば通知義務は満たせる** |
| 6b | **soundfile は落とせるか** | — | `upstream/Irodori-TTS/irodori_tts/codec.py:273`・`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1519,1537`・`upstream/Irodori-TTS-Server/src/irodori_openai_tts/audio.py:9` で `import soundfile as sf`。`pyproject.toml:18` に `soundfile>=0.12.0` | — | **落とせない・断定**。09 §3 の torchcodec/FFmpeg とは違い、こちらは**実際に import されている** |
| 7 | **AMD ROCm Windows wheel**（`rocm_sdk_core-10.0.0-py3-none-win_amd64.whl`・758,050,981 B・627 エントリ） | **wheel からは特定できない**＝`rocm_sdk_core-10.0.0.dist-info/METADATA` は **59 B・3 行**（`Metadata-Version: 2.4` / `Name: rocm-sdk-core` / `Version: 10.0.0`）で **License 欄も License-File 欄も分類子も 1 つも無い** | wheel 内の license 檔は **2 件だけ**＝`_rocm_sdk_core/share/doc/amd_comgr/LICENSE.txt` 15,289 B（「**The Comgr Project is under the Apache License v2.0 with LLVM Exceptions**」）と `_rocm_sdk_core/share/doc/hipcc/LICENSE.txt` 1,098 B（`https://stable.repo.amd.com/rocm/core/whl-next/rocm-sdk-core/rocm_sdk_core-10.0.0-py3-none-win_amd64.whl` を HTTP Range で実読・2026-09-04 03:14〜03:15 JST）。PyPI の `rocm-sdk-core` は別物（0.1.0・license 欄も null） | 同梱するなら **AMD の Software License の一次資料を別途探して添える必要がある**（本便では見つけていない）。参考＝GitHub `ROCm/ROCm` は **MIT**（API `license.spdx_id = "MIT"`・LICENSE 1,113 B・2026-09-04 03:15 JST） | **未確認札**（wheel 単体では裏取り不能）。**CUDA のみ案ならこの穴は開かない**＝「ROCm を切る根拠」に 1 本足せる |
| 8 | **transformers / onnx / sentencepiece / tokenizers** | **Apache-2.0**（09 §3 と一致） | §4 Redistribution (a)「**You must give any other recipients of the Work or Derivative Works a copy of this License**」／(d)「**If the Work includes a "NOTICE" text file as part of its distribution, then any Derivative Works that You distribute must include a readable copy of the attribution notices contained within such NOTICE file ... in at least one of the following places: within a NOTICE text file distributed as part of the Derivative Works; within the Source form or documentation, if provided along with the Derivative Works; or, within a display generated by the Derivative Works**」（`https://www.apache.org/licenses/LICENSE-2.0.txt`・2026-09-04 03:13 JST） | **NOTICE の有無の実測**（GitHub API `contents/`・2026-09-04 03:14 JST）＝ `huggingface/transformers`: **LICENSE 11,418 B のみ・NOTICE 無し**／`onnx/onnx`: LICENSE 11,358 B ＋ **NOTICE 2,133 B あり**／`google/sentencepiece`: **LICENSE 11,358 B のみ**／`huggingface/tokenizers`: **LICENSE 11,357 B のみ** | **可・断定**。**§4(d) が発動するのは onnx だけ**＝ onnx の NOTICE（abseil-cpp / protobuf 等の著作権表示）を配布物に写す。他 3 つは §4(a) の「Apache-2.0 本文 1 部」で足りる |

---

## 逐語（全文抜粋）

### CUDA EULA §1.1.2 Distribution Requirements（`https://docs.nvidia.com/cuda/eula/index.html`・2026-09-04 03:09 JST）

> These are the distribution requirements for you to exercise the distribution grant:
> - Your application must have material additional functionality, beyond the included portions of the SDK.
> - The distributable portions of the SDK shall only be accessed by your application.
> - The following notice shall be included in modifications and derivative works of sample source code distributed: “This software contains source code provided by NVIDIA Corporation.”
> - Unless a developer tool is identified in this Agreement as distributable, it is delivered for your internal use only.
> - The terms under which you distribute your application must be consistent with the terms of this Agreement, including (without limitation) terms relating to the license grant and license restrictions and protection of NVIDIA’s intellectual property rights. Additionally, you agree that you will protect the privacy, security and legal rights of your application users.
> - You agree to notify NVIDIA in writing of any known or suspected distribution or use of the SDK not in compliance with the requirements of this Agreement, and to enforce the terms of your agreements with respect to distributed SDK.

同 §1.2 Limitations（抜粋）:

> You may not use the SDK in any manner that would cause it to become subject to an open source software license. As examples, licenses that require as a condition of use, modification, and/or distribution that the SDK be: Disclosed or distributed in source code form; Licensed for the purpose of making derivative works; or Redistributable at no charge.

同 §2.2 Distribution:

> The portions of the SDK that are distributable under the Agreement are listed in Attachment A.

### cuDNN SLA Supplement §2（`https://docs.nvidia.com/deeplearning/cudnn/latest/reference/eula.html`・2026-09-04 03:09 JST・(v. February 22, 2022)）

> 2. Distribution . The following portions of the SDK are distributable under the Agreement: the runtime files .so and .dll.

### Microsoft「Redistributing Visual C++ Files」（`https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files`・2026-09-04 03:09 JST）

> Distribution of the Visual C++ Runtime Redistributable package, merge modules, and individual binaries is limited to licensed Visual Studio users and is subject to Microsoft Software License Terms.

> It's also possible to directly install the Redistributable DLLs in the application local folder . The application local folder is the folder that contains your executable application file. For servicing reasons, we don't recommend that you use this installation location.

### VS 2022 Distributable Code（`https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution`・2026-09-04 03:11 JST）

> The following section is the "Distributable List" or "REDIST.txt" that is referenced in the "Distributable Code" section of the Microsoft Software License Terms for Visual Studio Enterprise 2022, Visual Studio Professional 2022, Visual Studio Community 2022 ("the software"). If you have a validly licensed copy of such software, you may copy and distribute with your program the unmodified form of the files listed below, subject to the License Terms for the software.

**Visual C++ Runtime Files** 節:

> Subject to the License Terms for the software, you may copy and distribute with your program any of the files within the following folder and its subfolders except as noted below. You may not modify these files.
> [VisualStudioFolder]\VC\redist

> You may not distribute the contents of the following folders ... [VisualStudioFolder]VC\Redist\MSVC\[version]\debug_nonredist

### DirectML MSLT §1(a)（`https://www.nuget.org/packages/Microsoft.AI.DirectML/1.15.4/license`・2026-09-04 03:14 JST）

> **MICROSOFT SOFTWARE LICENSE TERMS / MICROSOFT DIRECTX MACHINE LEARNING (DIRECTML)**
> 1. INSTALLATION AND USE RIGHTS.
> a) General. Subject to the terms of this agreement, you may install and use any number of copies of the software, and solely for use on Windows and Xbox. You may copy and distribute the software (i.e. make available for third parties) in applications and services you develop in the build with Machine Learning tools and frameworks, and/or games that run on Windows and Xbox.

> 3. SCOPE OF LICENSE. The software is licensed, not sold. Microsoft reserves all other rights. Unless applicable law gives you more rights despite this limitation, you will not (and have no right to): ... c) remove, minimize, block, or modify any notices of Microsoft or its suppliers in the software; ... e) except as expressly stated in Section 1, share, publish, distribute, or lease the software, provide the software as a stand-alone offering for others to use, or transfer the software or this agreement to any third party.

### LGPL-2.1 §6（soundfile wheel 内 `_soundfile_data/COPYING`・2026-09-04 03:12 JST 抽出）

> 6. As an exception to the Sections above, you may also combine or link a "work that uses the Library" with the Library to produce a work containing portions of the Library, and distribute that work under terms of your choice, provided that the terms permit modification of the work for the customer's own use and reverse engineering for debugging such modifications.
>
> You must give prominent notice with each copy of the work that the Library is used in it and that the Library and its use are covered by this License. You must supply a copy of this License. If the work during execution displays copyright notices, you must include the copyright notice for the Library among them, as well as a reference directing the user to the copy of this License. Also, you must do one of these things:
> - a) Accompany the work with the complete corresponding machine-readable source code for the Library including whatever changes were used in the work ...
> - b) Use a suitable shared library mechanism for linking with the Library. A suitable mechanism is one that (1) uses at run time a copy of the library already present on the user's computer system, rather than copying library functions into the executable, and (2) will operate properly with a modified version of the library, if the user installs one, as long as the modified version is interface-compatible with the version that the work was made with.
> - c) Accompany the work with a written offer, valid for at least three years, to give the same user the materials specified in Subsection 6a, above, for a charge no more than the cost of performing this distribution.
> - d) If distribution of the work is made by offering access to copy from a designated place, offer equivalent access to copy the above specified materials from the same place.
> - e) Verify that the user has already received a copy of these materials or that you have already sent this user a copy.

### PSF License §2・§3（`python-3.12.10-embed-amd64.zip` 内 `LICENSE.txt`:80-95・2026-09-04 03:08 JST ダウンロード）

> 2. Subject to the terms and conditions of this License Agreement, PSF hereby grants Licensee a nonexclusive, royalty-free, world-wide license to reproduce, analyze, test, perform and/or display publicly, prepare derivative works, distribute, and otherwise use Python alone or in any derivative version, provided, however, that PSF's License Agreement and PSF's notice of copyright, i.e., "Copyright (c) 2001, ... 2023 Python Software Foundation; All Rights Reserved" are retained in Python alone or in any derivative version prepared by Licensee.
>
> 3. In the event Licensee prepares a derivative work that is based on or incorporates Python or any part thereof, and wants to make the derivative work available to others as provided herein, then Licensee hereby agrees to include in any such work a brief summary of the changes made to Python.

---

## ⒜⒝の配布物に要る licenses/ の一覧（檔名・入手元 URL・何を書き添えるか）

> 「入手元」が **wheel 内**のものは**そのままコピーすれば済む**。「自作」「外部取得」のものは**我々が用意しないと欠ける**＝ここが工数。
> Irodori 本体・重み・コーデック・SilentCipher の MIT 文（09 §4・16 §4-3 の「licenses/ 自作 1 手は必須」）は本便の外だが、同じ `licenses/` に並ぶので末尾に再掲した。

### ⒜（python-embed ＋ torch cu130・CUDA のみ）

| # | 配布物の檔名（案） | 入手元 | 種別 | 書き添えること |
|---|---|---|---|---|
| A1 | `licenses/python/LICENSE.txt` | `python-3.12.10-embed-amd64.zip` 内 `LICENSE.txt`（36,874 B） | **wheel/zip 内** | そのまま。改変したら差分要約（PSF §3） |
| A2 | `licenses/pytorch/LICENSE` | torch wheel `torch-2.14.0+cu130.dist-info/licenses/LICENSE`（3,548 B） | **wheel 内** | そのまま |
| A3 | `licenses/pytorch/third_party/**`（93 檔） | torch wheel `torch-2.14.0+cu130.dist-info/licenses/third_party/`（`METADATA:8-102` の `License-File:` と 1 対 1） | **wheel 内** | ディレクトリ構造ごとコピー |
| A4 | `licenses/nvidia/CUDA-EULA.txt` | `https://docs.nvidia.com/cuda/eula/index.html` | **外部取得（wheel に無い）** | 取得日を檔頭に。対象 DLL は `cudart64_13.dll` `cublas64_13.dll` `cublasLt64_13.dll` `cufft64_12.dll` `cufftw64_12.dll` `cusparse64_12.dll` `cusolver64_12.dll` `cusolverMg64_12.dll` `curand64_10.dll` `nvrtc64_130_0.dll` `nvrtc64_130_0.alt.dll` `nvrtc-builtins64_130.dll` `nvJitLink_130_0.dll` `cupti64_2025.3.0.dll` `nvToolsExt64_1.dll` |
| A5 | `licenses/nvidia/cuDNN-SLA.txt` | `https://docs.nvidia.com/deeplearning/cudnn/latest/reference/eula.html` | **外部取得（wheel に無い）** | 対象は `cudnn64_9.dll` `cudnn_adv64_9.dll` `cudnn_cnn64_9.dll` `cudnn_engines_precompiled64_9.dll` `cudnn_engines_runtime_compiled64_9.dll` `cudnn_engines_tensor_ir64_9.dll` `cudnn_ext64_9.dll` `cudnn_graph64_9.dll` `cudnn_heuristic64_9.dll` `cudnn_ops64_9.dll`（10 個） |
| A6 | `licenses/llvm-openmp/LICENSE.txt` | torch wheel `third_party/llvm-openmp/LICENSE.txt`（17,315 B） | **wheel 内** | `torch/lib/libiomp5md.dll`（1.5 MiB）・`libiompstubs5md.dll` の分。A3 に含まれるので重複可 |
| A7 | `licenses/msvc/` ＋ `vc_redist.x64.exe` | Visual Studio（`[VisualStudioFolder]\VC\redist`）または `https://aka.ms/vs/17/release/vc_redist.x64.exe` | **外部取得** | **`msvcp140.dll` が要る根拠**＝`torch/lib/c10.dll` が MSVCP140.dll を import。改変禁止。「licensed Visual Studio user」の要件を満たす前提を記録する |
| A8 | `licenses/libsndfile/COPYING`（LGPL-2.1 全文） | soundfile wheel `_soundfile_data/COPYING`（26,518 B） | **wheel 内** | ＋「本ソフトウェアは libsndfile（LGPL-2.1）を使用しています」の**目立つ通知**を README 等に |
| A9 | `licenses/libsndfile/license_notes.md` | soundfile wheel `licensing/license_notes.md`（2,631 B） | **wheel 内** | 同梱メディアライブラリ（libmp3lame LGPL-2+ / libmpg123 LGPL-2.1 等）の著作権とソース URL |
| A10 | `licenses/libsndfile/SOURCE-OFFER.txt`（**自作**） | — | **自作** | LGPL §6 c) の「3 年間有効な書面の申し出」、または d) を採るなら**配布ページに libsndfile ソース zip を並べる**旨。**どちらを採るかは卓** |
| A11 | `licenses/soundfile/LICENSE` | soundfile wheel `dist-info/LICENSE`（1,512 B・BSD-3） | **wheel 内** | そのまま |
| A12 | `licenses/apache-2.0.txt` | `https://www.apache.org/licenses/LICENSE-2.0.txt`（11,358 B） | **外部取得** | transformers / sentencepiece / tokenizers / huggingface-hub / safetensors / peft / datasets の共有分（§4(a)） |
| A13 | `licenses/onnx/NOTICE` | `https://raw.githubusercontent.com/onnx/onnx/main/NOTICE`（2,133 B） | **外部取得** | **§4(d) が発動する唯一の檔**。onnx を配布物に入れる場合のみ |
| A14 | `licenses/zlib/LICENSE`（**未確認**） | — | **未確認** | `torch/lib/zlibwapi.dll`（0.1 MiB）の出所と license を特定できていない。torch の `third_party/` にも zlib 単体の LICENSE は無い |
| A15〜 | `licenses/irodori/`・`licenses/dacvae/`・`licenses/silentcipher/` の MIT 文 | **自作**（HF repo に LICENSE 檔が無い＝09 §4・16 §4-3） | **自作** | 本便の外。同じ `licenses/` に並ぶので忘れないこと |

### ⒝（ONNX Runtime ＋ DirectML EP・C# から）

| # | 配布物の檔名（案） | 入手元 | 種別 | 書き添えること |
|---|---|---|---|---|
| B1 | `licenses/onnxruntime/LICENSE` | ORT wheel `onnxruntime/LICENSE`（1,094 B・MIT） | **wheel 内** | そのまま |
| B2 | `licenses/onnxruntime/ThirdPartyNotices.txt` | ORT wheel `onnxruntime/ThirdPartyNotices.txt`（331,175 B） | **wheel 内** | そのまま。**DirectML の記載は無い**ので B3 で補う |
| B3 | `licenses/directml/MICROSOFT-DIRECTML-LICENSE.txt` | `https://www.nuget.org/packages/Microsoft.AI.DirectML/<ver>/license` | **外部取得（wheel にも ThirdPartyNotices にも無い）** | `DirectML.dll`（18,527,776 B）の分。**通知の改変禁止**（§3(c)）・**Windows/Xbox 限定**（§1(a)）を記録 |
| B4 | `licenses/msvc/` ＋ `vc_redist.x64.exe` | 同 A7 | **外部取得** | C# 側／ORT native も MSVC ランタイムを要求（要実測・**未確認**） |
| B5 | `licenses/apache-2.0.txt` ＋ `licenses/onnx/NOTICE` | 同 A12・A13 | **外部取得** | ONNX モデルを配るだけなら不要な公算（onnx ランタイムを配らない）＝**未確認** |
| B6 | libsndfile 一式（A8〜A11） | — | — | 音声 I/O を C# 側（NAudio 等）に移せば **丸ごと落とせる**＝⒝ の隠れた利点 |
| B7 | `licenses/irodori/` 他 MIT 文 | **自作** | **自作** | 同 A15 |

**⒜ と ⒝ の差**＝⒜ は **外部取得 5 種＋自作 2 種**（CUDA EULA・cuDNN SLA・vc_redist・Apache-2.0・onnx NOTICE ＋ LGPL ソース申し出・MIT 文）。⒝ は **外部取得 2〜3 種＋自作 1 種**（DirectML MSLT・vc_redist・(Apache) ＋ MIT 文）で、**libsndfile の LGPL 義務ごと消せる可能性がある**。

---

## 未確認

1. **`torch/lib/nvperf_host.dll`（26.5 MiB）が CUDA EULA Attachment A に無い**。EULA 全文を `nvperf` で検索して **0 件**。`cupti.dll` は列挙されているので `cupti64_2025.3.0.dll` は「certain variations of these files that have version number ... embedded in the file name」で救えるが、**`nvperf_host` は別名**で救えるか読めない。→ ⒜ で torch/lib をそのまま配るなら**この 1 個だけ根拠が無い**。（削れるかは未検証）
2. **`torch/lib/zlibwapi.dll`（0.1 MiB）の出所と license**。NVIDIA が cuDNN 用に配っていた zlibwapi と同名だが、torch wheel の `third_party/` に zlib 単体の LICENSE は無い。
3. **`torch/lib/nvrtc64_130_0.alt.dll`（86.8 MiB）** が Attachment A の "certain variations" に当たるか（`.alt` は版番号でも architecture でもない）。
4. **「licensed Visual Studio users」に Visual Studio Community 2022 の利用者が含まれるか**＝ Microsoft Software License Terms 本文（`https://visualstudio.microsoft.com/license-terms/`）の "Distributable Code" 節と Community 版の利用資格条項の読み。**本便では本文を取得していない＝卓の専管**。
5. **LGPL-2.1 §6 の b) が「アプリと一緒に DLL を同梱する形」に使えるか**の読み。b)(1) の「already present on the user's computer system」の文言との整合。**卓**。
6. **`rocm_sdk_core` / `rocm_sdk_libraries` / `amd_torch_device_*` の license の一次資料**。wheel の METADATA が 3 行で欄が無く、`stable.repo.amd.com` にも LICENSE ページを見つけられていない。GitHub `ROCm/ROCm` の MIT が wheel のバイナリにも及ぶかは**未確認**。
7. **PyPI の `nvidia-*` wheel の中身**（license 檔が入っているか）。本便は torch wheel のみ実読。⒝ で CUDA EP を採る場合に要追検。
8. **C# 側／ORT native（`onnxruntime.dll` 21.1 MiB）の MSVC ランタイム依存**（B4）。PE インポート未読。
9. **`onnxruntime/ThirdPartyNotices.txt` 331,175 B の中身の網羅性**。`directml` が 0 件であることだけ確認し、他の項目は読んでいない。
10. **Microsoft.AI.DirectML の版と ORT 同梱 DirectML.dll の版の対応**。参照した MSLT は NuGet 1.15.4（registration の最終エントリ）で、ORT 1.24.4 が同梱する DirectML.dll の版とは**一致を確認していない**。条項の内容は版によって変わりうる。

---

## 主席への注意

1. **BRIEF §2-2 の門は実行系でも塞がらない＝⒜⒝は「ライセンスでは」成立する。** CUDA・cuDNN・MSVC・DirectML・PSF・LGPL いずれにも明文の再配布許諾があり、禁止条項は無い。ただし**無条件ではなく、7 種の檔を自分で用意する義務が付く**（上の licenses/ 一覧）。「門は通る、ただし通行料がある」という報告の仕方が正確。

2. **【要修正・重】10-note §4-2 の断定に 1 行足すこと。** 「Windows の torch CUDA wheel は CUDA ランタイムと cuDNN を自前で同梱する＝利用者に CUDA Toolkit の別途導入を求めない」は**技術的には正しい**。だが **PyTorch はライセンス面を肩代わりしていない**＝ wheel の `License-Expression`（6 ライセンスの AND）にも 94 件の `License-File:` にも **NVIDIA 系は 1 件も無い**（NVIDIA 由来は cudnn_frontend の MIT と NVTX の Apache-2.0 だけで、どちらも CUDA/cuDNN 本体の条件ではない）。**「torch を配れば CUDA の話は終わり」は誤り**＝ CUDA EULA と cuDNN SLA の 2 枚を我々が添える。

3. **【新規・重】⒜ の配布物見積に `vc_redist.x64.exe` が抜けている。** `torch/lib/c10.dll` の PE インポートテーブル直読で **MSVCP140.dll** 要求を確認。python-embed は `vcruntime140.dll`（120,400 B）と `vcruntime140_1.dll`（49,776 B）を同梱するが **msvcp140.dll は無い**（35 エントリ全読）。10-note §4-6 の ⒜ 見積（≒5.4 GiB）に **+≒25 MB** と、BRIEF §4-5「初心者が詰まる箇所」に **「VC++ 再頒布の導入 1 段」が残る**ことを足すこと。※ ⒝ でも同じ確認が要る（未確認 8）。

4. **【要修正・重】⒝ の「ONNX＋DirectML は軽い」に注釈が要る。** 10-note §4-6 の「⒝ ONNX + DirectML EP ≒25 MiB」は**サイズとしては正しい**。だが **DirectML.dll は MIT ではない**＝ Microsoft の独自 MSLT で、①**Windows/Xbox 限定** ②通知の改変禁止 ③単体配布禁止。しかも ORT wheel の `ThirdPartyNotices.txt`（331,175 B）に **DirectML の記載が 0 件**＝ **我々が MSLT を自分で添えないと欠ける**。ORT の MIT だけ添えて済ませると穴が開く。

5. **【札を外せる】09 §3 の torch 行の「未確認：LICENSE 全文は本便で未直読」を削れる。** 実体は `dist-info/licenses/LICENSE` 3,548 B ＝ BSD-3-Clause 本文 ＋ Facebook/Idiap/NYU/NEC/Deepmind/Kakao Brain/Cruise/Tri Dao/Arm 等の著作権表示。GitHub API の `NOASSERTION` は「BSD-3 本文に多数の著作権表示を連ねた形」で正しかった。ただし**報告に書くなら `License-Expression` の 6 ライセンス AND のほうが正確**。

6. **【比較表に足せる】libsndfile は ⒜⒝の差を 1 本増やす。** soundfile は upstream 4 箇所で import＝**⒜（Python 据置）では落とせない**＝ LGPL-2.1 §6 の義務（通知・ライセンス全文・ソース入手手段）が付く。**⒝ で音声 I/O を C# 側に移せば、この義務ごと消える**。BRIEF §2-4 の「ライセンス」軸に書ける実質的な差はここ。※ 通知とライセンス全文は soundfile wheel が `_soundfile_data/COPYING` と `licensing/license_notes.md` で既に用意しているので、⒜ でも**追加作業は「3 年間のソース申し出文」1 枚だけ**。a〜e のどれで満たすかは**卓の判断**。

7. **【ROCm を切る根拠に 1 本足せる】** 10-note §4-3 の「ダウンロード量では ROCm < CUDA ＝ ROCm を切る根拠は配布物の重さではない」はそのまま。**だがライセンス面では ROCm 側が弱い**＝ `rocm_sdk_core-10.0.0.dist-info/METADATA` は **59 B・3 行**で License 欄も分類子も無く、wheel 内の license 檔は amd_comgr と hipcc の 2 件だけ（16 §3 が dacvae wheel で見つけたのと同じ穴）。**「CUDA のみで出す」案には、ライセンスの裏取りコストという根拠を 1 本足せる**。

8. **【卓に上げる 2 点】** ① MSVC の「limited to licensed Visual Studio users」に Community 版が含まれるかの読み（本便は Microsoft Software License Terms 本文を未取得）。② LGPL §6 の a〜e のどれで満たすか。どちらも**法解釈**で、席が断定してはいけない領域。

9. **【小・工数】licenses/ の実作業は「外部取得 5 種＋自作 2 種」（⒜）／「外部取得 2〜3 種＋自作 1 種」（⒝）。** 16 §4-3 の「licenses/ 自作 1 手は必須」を、**実行系込みで「1 手」では収まらない**に更新すべき。ただし大半は wheel 内の檔のコピーで、新規に書き起こすのは LGPL のソース申し出文と Irodori 系 3 つの MIT 文だけ＝**半便には届かない**。

10. **生檔の所在**＝`C:\Users\mugonkun\source\repos\irodori-native-research\lab\tmp\rt-license\`（`cuda-eula.txt` / `cudnn-sla.txt` / `msvc-redist.txt` / `vs2022-redist.txt` / `pywin-embed.txt` / `nuget-dml-license.txt` / `libsndfile-COPYING.txt` / `soundfile-license_notes.md` / `apache2.txt` / `onnx-NOTICE.txt` / `pypi-*.json` / `torchwhl/` / `ortdml/` / `amdcore/` / `python-3.12.10-embed-amd64.zip` / `soundfile.whl` / `zipget.py`）。`zipget.py` は remote wheel からエントリを Range 抽出する道具＝**再検分でそのまま使える**（使い方は檔冒頭）。wheel 全体は 1 個もダウンロードしていない（torch cu130 は 1.9 GiB のうち **実取得 ≒1.2 MB**）。
