# first-run-notices.md — 初回取得で利用者の機体に入る第三者物の一覧と、各ライセンスの所在

> **この檔は配布物には入れない。**初回取得の UI（便 D／便 E）が、取得を始める前にこの内容を表示して
> 同意を取る。理由＝配布物には第三者バイナリを 1 つも入れない（`decisions.md` 8）ので、
> **`licenses/` に置くべきなのは「配布物に入っている物」の許諾文だけ**であり、
> 初回取得で入る物の通知は**取得の時点で出す**のが筋だからである。
>
> **出典**＝`research/lab/notes/22-runtime-license.md`（実行系のライセンス・A1〜A15 の一覧と逐語）・
> `research/lab/notes/09-license-chain.md` §3（周辺ライブラリのライセンス名）。
> 取得日時＝すべて **2026-09-04 03:08〜03:15 JST**（＝2026-09-03 18:08〜18:15 UTC）。
> **注記**＝調査便の生檔置き場（`lab/tmp/rt-license/`）は本リポジトリに写されていない
> （`research/` にはテキストのノートだけが入っている）。**下の逐語は 22 のノート本文からの引用**であり、
> **原文そのものは各 URL から取り直すこと**（外部取得の 4 件はビルド時に取得して sha256 を残す＝
> `build/` の管掌）。
>
> **法解釈は席が断定しない**（`decisions.md` 22 の停止域）。下の「卓の裁定が要る」欄がそれである。

---

## A. 初回取得で入る物（cu130／cu126 の CUDA 版）

| # | 何が入るか | ライセンス | 許諾文の所在 | 種別 | 備考 |
|---|---|---|---|---|---|
| A1 | **埋め込み Python 3.12.10**（`python-3.12.10-embed-amd64.zip`・11,133,606 B・35 エントリ・展開後 22,501,299 B・sha256 `4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3`） | **PSF License Agreement** | **zip の中に `LICENSE.txt`（36,874 B・702 行）が入っている**＝展開先にそのまま残す | zip 内 | 改変したら差分の要約を添える（PSF §3）。この zip は `vcruntime140.dll`（120,400 B）と `vcruntime140_1.dll`（49,776 B）を同梱するが **`msvcp140.dll` は無い**（A7 の根拠） |
| A2 | **torch**（cu130／cu126 の wheel） | **BSD-3-Clause 本文＋多数の著作権表示**（実体＝wheel 内 `dist-info/licenses/LICENSE` 3,548 B）。`METADATA` の宣言は `License-Expression: Apache-2.0 AND Apache-2.0 WITH LLVM-exception AND BSD-2-Clause AND BSD-3-Clause AND BSL-1.0 AND MIT` | **wheel の中**＝展開先の `torch-*.dist-info/licenses/` をそのまま残す | wheel 内 | 逐語（BSD-3 の再頒布条項）＝「Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.」 |
| A3 | torch の `third_party/` **93 檔** | 各種（`METADATA` の `License-File:` 94 件と 1 対 1） | **wheel の中**（`dist-info/licenses/third_party/`） | wheel 内 | ディレクトリ構造ごと残す。`llvm-openmp/LICENSE.txt`（17,315 B）＝`torch/lib/libiomp5md.dll` の分もここに含まれる |
| A4 | **NVIDIA CUDA の再頒布 DLL**（torch が同梱＝`cudart64_13.dll`・`cublas64_13.dll`・`cublasLt64_13.dll`・`cufft64_12.dll`・`cufftw64_12.dll`・`cusparse64_12.dll`・`cusolver64_12.dll`・`cusolverMg64_12.dll`・`curand64_10.dll`・`nvrtc64_130_0.dll`・`nvrtc64_130_0.alt.dll`・`nvrtc-builtins64_130.dll`・`nvJitLink_130_0.dll`・`cupti64_2025.3.0.dll`・`nvToolsExt64_1.dll`） | **NVIDIA CUDA Toolkit EULA**（独自 EULA・`LicenseRef-NVIDIA-Proprietary`） | **wheel に入っていない＝外部取得**＝`https://docs.nvidia.com/cuda/eula/index.html` | **外部取得** | **重要**＝PyTorch はライセンス面を肩代わりしていない。wheel の `License-Expression` にも 94 件の `License-File:` にも **NVIDIA 系は 1 件も無い**（NVIDIA 由来は `cudnn_frontend`（MIT・ヘッダのみ）と `NVTX`（Apache-2.0 WITH LLVM-exception）の 2 件だけ）。§2.6 Attachment A に上記 DLL が明記されている |
| A5 | **cuDNN の DLL**（`cudnn64_9.dll`・`cudnn_adv64_9.dll`・`cudnn_cnn64_9.dll`・`cudnn_engines_precompiled64_9.dll`・`cudnn_engines_runtime_compiled64_9.dll`・`cudnn_engines_tensor_ir64_9.dll`・`cudnn_ext64_9.dll`・`cudnn_graph64_9.dll`・`cudnn_heuristic64_9.dll`・`cudnn_ops64_9.dll` の 10 個） | **NVIDIA SDK SLA ＋ cuDNN Supplement**（v. February 22, 2022） | **外部取得**＝`https://docs.nvidia.com/deeplearning/cudnn/latest/reference/eula.html` | **外部取得** | **CUDA EULA 全文に `cudnn` の語は 0 件**＝CUDA EULA 1 枚では cuDNN を配れない。cuDNN SLA §2 の逐語＝「The following portions of the SDK are distributable under the Agreement: the runtime files .so and .dll.」 |
| A6 | **MSVC 再頒布**（`vc_redist.x64.exe` ≈25 MB・`msvcp140.dll` を入れるため） | **Microsoft Software License Terms（Visual Studio）の "Distributable Code" 節＋REDIST リスト** | **外部取得**＝`https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files`／`https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution` | **外部取得** | **必要な理由（実測）**＝`torch/lib/c10.dll` の PE インポートテーブルに `MSVCP140.dll`・`VCRUNTIME140.dll`・`VCRUNTIME140_1.dll`。埋め込み Python は後ろ 2 つしか持たない。**改変しない**こと。逐語＝「Distribution of the Visual C++ Runtime Redistributable package, merge modules, and individual binaries is limited to licensed Visual Studio users and is subject to Microsoft Software License Terms.」 |
| A7 | **libsndfile**（`soundfile` wheel が同梱する `_soundfile_data/libsndfile_x64.dll` 2,364,416 B） | **LGPL-2.1** | **wheel の中**＝`_soundfile_data/COPYING`（26,518 B・LGPL-2.1 全文）と `licensing/license_notes.md`（2,631 B）。**ソースの所在**＝`https://github.com/libsndfile/libsndfile`（リリース tarball＝`https://github.com/libsndfile/libsndfile/releases`） | wheel 内 | **「本ソフトウェアは libsndfile（LGPL-2.1）を使用しています」の目立つ通知が要る**（LGPL §6）＝本檔と `README.md` §4 に書いた。`license_notes.md` が同梱メディアライブラリ（libFLAC BSD-3／**libmp3lame LGPL-2+**／**libmpg123 LGPL-2.1**／libogg・libopus・libvorbis BSD-3）の著作権とソース URL を既に列挙している |
| A8 | **libsndfile のソース入手手段**（LGPL-2.1 §6 の a〜e のいずれか 1 つ） | — | **ソースの所在**＝`https://github.com/libsndfile/libsndfile`（公式リポジトリ）・リリース tarball の頁＝`https://github.com/libsndfile/libsndfile/releases` | **所在は記載済み・手段は卓の裁定待ち** | 所在 URL の記載は `decisions.md` 28（司令官裁定）が明示的に求めたもの。c)「3 年間有効な書面の申し出」か d)「配布ページに libsndfile のソースを並べる」が現実的。**a〜e のどれで満たすかは法解釈＝司令官の裁定**（`research/lab/notes/22` 未確認 5・§C 2） |
| A9 | **soundfile 本体** | **BSD-3-Clause**（`Copyright (c) 2013, Bastian Bechtold`） | **wheel の中**＝`dist-info/LICENSE`（1,512 B） | wheel 内 | `soundfile` は上流 4 箇所で import＝**落とせない**（`codec.py:273`・`inference_runtime.py:1519,1537`・`irodori_openai_tts/audio.py:9`） |
| A10 | **transformers・sentencepiece・tokenizers・huggingface-hub・safetensors** | **Apache-2.0** | **外部取得**＝`https://www.apache.org/licenses/LICENSE-2.0.txt`（11,358 B）を 1 部。各 wheel 内の LICENSE でも可 | wheel 内／外部取得 | §4(a)「a copy of this License」で足りる。**§4(d)（NOTICE）が発動するのは onnx だけ**で、transformers／sentencepiece／tokenizers は repo 直下に NOTICE が無い（実測） |
| A11 | **その他の純 Python 依存**（fastapi MIT・uvicorn BSD-3・pydantic 等） | 各 wheel の宣言どおり | **wheel の中**（`dist-info/`） | wheel 内 | 台帳（`ledger/runtime-*.json`）の各項目に `license`・`license_url` 欄を持つ＝そこが正本 |
| A12 | **`silentcipher` パッケージ**（pin `d46d7d0893a583d8968ab3a6626e2289faec9152`） | **MIT**（`Copyright (c) 2024 Sony Research Inc.`） | **本リポジトリに同梱済み**＝`licenses/silentcipher/LICENSE` | 同梱 | 配布物にはコードもバイナリも入れないが、許諾文は先に置いてある |
| A13 | **`dacvae` パッケージ**（`facebookresearch/dacvae`・commit 固定 zip） | **Apache-2.0** | **本リポジトリに同梱済み**＝`licenses/dacvae/LICENSE` | 同梱 | **wheel／sdist には LICENSE が入らない**（`setup.py:28` の `license_files=("LICENSE.txt",)` が実体の檔名 `LICENSE` と食い違う＝`research/lab/notes/16` (g) で実証）＝**GitHub から取って添えるしかない** |
| A14 | **`torch/lib/zlibwapi.dll`**（0.1 MiB） | **未特定** | **無し** | **未特定** | torch の `third_party/` に zlib 単体の LICENSE が無い。NVIDIA が cuDNN 用に配っていた zlibwapi と同名だが**裏が取れていない**（`research/lab/notes/22` 未確認 2）。**解消するか、対象 DLL を外せるか検分する**＝`docs/acceptance.md` §3 の 1 |
| A15 | **`torch/lib/nvperf_host.dll`**（26.5 MiB）・**`nvrtc64_130_0.alt.dll`**（86.8 MiB） | **未確認** | — | **未確認** | CUDA EULA 全文を `nvperf` で検索して **0 件**。`.alt` が Attachment A の "certain variations of these files that have version number or architecture specific information embedded in the file name" に当たるかは読めない（同 未確認 1・3）。**削れるかは未検証** |
| A16 | **モデルの重み 3 件**（`Aratako/Irodori-TTS-v4.1-Small` 2.86 GB・`Aratako/Semantic-DACVAE-Japanese-32dim` 410 MB・`sony/silentcipher` 68 MB） | **前 2 件は MIT 宣言／3 件目は不明** | **本リポジトリに同梱済み**＝`licenses/irodori-tts-v4.1-small/LICENSE.md`・`licenses/semantic-dacvae-japanese-32dim/LICENSE.md`・`licenses/silentcipher/LICENSE`（**コードの分**） | 同梱（自作 2・原文 1） | **`Sony/SilentCipher` の「重み」の配布条件は原文のどこにも書かれていない**（HF に license メタデータ無し・`cardData` が `null`・本文は "the **code** in this repository is released under the MIT license" のみ・LICENSE 檔も無し）＝**だから重みを再配布せず初回取得にする**（`decisions.md` 8・9） |

| A17 | **soxr / libsoxr**（`soxr` wheel・`soxr-1.1.0-cp312-abi3-win_amd64.whl` 174,357 B。ネイティブ実体は**独立 DLL ではなく** `site-packages/soxr/soxr_ext.pyd` 354,304 B に組み込まれている） | **LGPL-2.1-or-later**（wheel 自身の宣言＝`soxr-1.1.0.dist-info/METADATA`:6 `License-Expression: LGPL-2.1-or-later`） | **wheel の中**＝`soxr-1.1.0.dist-info/licenses/` の `COPYING.LGPL`・`LICENSE-libsoxr.txt`・`LICENSE-PFFFT.txt`・`LICENSE.txt`。**ソースの所在**＝`https://github.com/dofuuz/python-soxr`（libsoxr 本体＝`https://sourceforge.net/projects/soxr/`） | wheel 内 | **copyleft の 2 件目**。`librosa` が引く再標本化ライブラリで、台帳（`ledger/runtime-*.json`）の 3 変種すべてに載る。§C 2 の裁定は **libsndfile と soxr の両方**に掛かる。※ 実体が別 DLL でなく `.pyd` に入っている点だけが libsndfile と形が違う（**これは観測事実であり、義務の読みではない**） |
| A18 | **MPL-2.0 の純 Python 2 件**＝`certifi`（`METADATA` `License: MPL-2.0`・`dist-info/licenses/LICENSE`）・`tqdm`（`License: MPL-2.0 AND MIT`・`dist-info/licenses/LICENCE`） | **MPL-2.0**（tqdm は MPL-2.0 AND MIT） | **wheel の中**（`dist-info/licenses/`）。ソースの所在＝`https://github.com/certifi/python-certifi`・`https://github.com/tqdm/tqdm` | wheel 内 | 改変せずそのまま入れる（MPL §3.2 の「改変した檔」に当たる物が無い）。**どう読むかは書かない**＝観測した宣言と所在だけを記す |

**この表に無いもの**＝`onnx` の NOTICE（A13 相当）は **ONNX を配らないので非該当**。
DirectML は **⒝案（ONNX Runtime＋DirectML EP）を採らないので非該当**。

---

## B. Radeon 版（rocm-gfx1151）で追加で入る物

| # | 何 | ライセンス | 所在 | 備考 |
|---|---|---|---|---|
| B1 | `torch==2.13.0+rocm10.0.0`・`torchaudio==2.11.0.2+rocm10.0.0`・`rocm-sdk-*`（AMD の wheel・索引 `https://stable.repo.amd.com/rocm/whl-next/`） | **wheel からは特定できない** | **未特定** | `rocm_sdk_core-10.0.0.dist-info/METADATA` は **59 B・3 行**（`Metadata-Version` / `Name` / `Version`）で **License 欄も License-File 欄も分類子も 1 つも無い**。wheel 内の license 檔は `amd_comgr/LICENSE.txt`（15,289 B・Apache License v2.0 with LLVM Exceptions）と `hipcc/LICENSE.txt`（1,098 B）の 2 件だけ。参考＝GitHub `ROCm/ROCm` は MIT（`spdx_id`）だが、**それが wheel のバイナリにも及ぶかは未確認**（`research/lab/notes/22` 表 7・未確認 6） |

Radeon 版はそもそも**未保障・別リリース**（`docs/radeon.md`）。B1 が未特定であることを取得前に明示する。

---

## C. 卓（司令官）の裁定が要る 2 点（席は断定しない）

1. **「licensed Visual Studio users」に Visual Studio Community 2022 の利用者が含まれるか**（A6）。
   Microsoft Software License Terms 本文（`https://visualstudio.microsoft.com/license-terms/`）の
   "Distributable Code" 節と Community 版の利用資格条項の読み。**調査便は本文を取得していない**
   （`research/lab/notes/22` 未確認 4）。
2. **LGPL-2.1 §6 の a〜e のどれで libsndfile と soxr／libsoxr の義務を満たすか**（A8・A17）。
   §6 b) の「(1) uses at run time a copy of the library already present on the user's computer system」は、
   DLL を一緒に取得させる本方式には当たらない読みが自然＝c) か d) が安全、という材料までが調査の範囲。
   **どれを採るかは法解釈**（同 未確認 5）。**ソースの所在 URL は A7・A8・A17 に載せた**
   （`decisions.md` 28 が求めたのはこの記載であり、手段の選択ではない）。
   なお **soxr は実体が独立 DLL でなく `soxr/soxr_ext.pyd` に組み込まれている**＝libsndfile と形が違う
   （**観測事実**。§6 の当てはめが変わるかどうかは席が断定しない）。

---

## D. 初回取得 UI が出すべき文（**案**・便 D／便 E で確定）

> このアプリは、初回起動時に次のものをインターネットから取得して、あなたのパソコンに入れます
> （合計 約 5.4 GB）。それぞれの提供者と許諾条件は下のとおりです。取得先はすべて公式のサーバで、
> 取得した檔は sha256 で検証します。
>
> - Python 3.12.10（Python Software Foundation・PSF License）
> - PyTorch（BSD-3-Clause）と、それが同梱する NVIDIA CUDA／cuDNN のライブラリ
>   （NVIDIA CUDA Toolkit EULA・cuDNN SLA）
> - Microsoft Visual C++ 再頒布可能パッケージ（Microsoft Software License Terms）
> - libsndfile と libsoxr（いずれも **LGPL 系**）ほかの音声ライブラリ
> - Irodori-TTS のモデルの重み（MIT 宣言）・コーデックの重み（MIT 宣言）・SilentCipher の透かしモデル
> - その他の Python ライブラリ（Apache-2.0・MIT・BSD 系）
>
> 全文は「ライセンスを表示」から読めます。

**「ライセンスを表示」が開くもの**＝**本檔の内容**と `licenses/README.md`。

**本檔そのものは配布物に入らない**（冒頭の宣言・設計書 §1・`licenses/README.md` §1 の 9 行目）＝
`build/assemble-app.ps1` が `licenses/` を写すときに**この 1 檔だけ除外し**、写った場合は止まる。
したがって **§A〜§D の本文は便 D（ランチャ）が UI に持つ**（配布物に入る `licenses/README.md` は
そのまま開ける）。取得前に見せる檔を取得物と一緒に配るのは順序が逆になる、というのが除外の理由である。
