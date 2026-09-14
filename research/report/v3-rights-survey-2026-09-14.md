# v3 権利調査 報告 — 2026-09-14（裁定 148 ⑶「権利の確認」・司令官の指示「version3系統に向けた権利関係の調査を実施」）

> 席＝Fable（主席・本報告の執筆）／サブ席＝Opus 5 の研究席 6（R1 NVIDIA・R2 AMD・R3 Microsoft と置き場と署名・R4 Python と torch・R5 PyPI 99 件・R6 署名の実測）＋検証席 6（R1・R4・R6 の各「逐語照合」「過大主張」）。
> **R2・R3・R5 の検証席と批評席は起こしていない**（司令官の指示 2026-09-14 11:0x＝サブスクの枠が尽きるため Workflow を停止・以後は Fable 単体）。したがって R2・R3・R5 の所見は**主席の目視のみ**で通した＝R1・R4・R6 と同じ密度の裏取りは無い。
> 本報告は**裁定の材料**であり正典ではない。**席は法解釈を断定しない**（`decisions.md` 22 の停止域）＝書けるのは「原文の檔の何行目に何と書いてあるか」と、そこから機械的に読める分類まで。
> **原文と所見の置き場**＝`research/lab/rights-v3/`（`BRIEF.md`＝各席への依頼文・`originals/`＝取得した原文のテキスト分と `SHA256SUMS.txt`・`findings/`＝6 席の JSON と CSV・`torch-cu130-lib-and-licenses.txt`／`cu126`＝torch wheel の目次）。html・docx・dll・tar.gz はリポジトリに写していない（scratchpad に残る）。
> 逐語の行番号は各席の JSON が指す originals の檔の行。検証席が見つけた行番号のずれ（R1 で 19 件・R4 で 5 件）は JSON 側を直していない＝**行番号は ±2 行の誤差込みで読むこと**。本文の断定的な札（explicit-yes 等）は検証席の指摘に従って主席が下げた（§6）。

---

## 1. 要点（10 行）

1. **実行系のうち「再配布の明文許諾が原文にある物」**＝埋め込み Python（PSF §2）・torch／torchaudio 本体（BSD-3／BSD-2）・NVIDIA の CUDA 実行時 DLL（EULA §1.1.1＋Attachment A）・cuDNN（SLA Supplement §2「the runtime files .so and .dll」）・PyPI の permissive 71 件・vc_redist（Visual Studio の Distributable Code 節）。**いずれも条件付き**（許諾文の同梱・改変禁止・自作機能・配布条件の整合・LGPL の源入手手段など）。
2. **Radeon（ROCm）版は権利の根拠が原文に無い**＝AMD の wheel 8 件のうち許諾を名乗るのは純 Python の `rocm` sdist（SPDX MIT ヘッダ）だけ。`rocm-sdk-core`（758 MB）・`rocm-sdk-libraries`・`rocm-sdk-device-gfx1151`・`amd-torch-device-*` は METADATA に License 欄が無く、wheel 内の許諾檔は `amd_comgr` と `hipcc` の 2 件のみ。取得元 `stable.repo.amd.com` に terms は無く（404 を 4 URL で確認）、TheRock（MIT）にも配布方針の記述は無い。**AMD Software EULA は「distribute … or otherwise transfer」を禁じるが、それが wheel に及ぶという一次資料は無い**（R2-10）。
3. **NVIDIA の DLL のうち Attachment A に名が無い物が cu130／cu126 それぞれに 3 個ある**＝`nvperf_host.dll`（27.7 MB）・`nvrtc64_1x0_0.alt.dll`（91 MB／46 MB）・`cusolverMg64_1x.dll`（95 MB／85 MB）。加えて `nvJitLink_1x0_0.dll` は Attachment A の表記が `libnvJitLink.dll`（lib 接頭辞つき）で同梱物と一致しない。柱書「certain variations of these files …」で救えるかは卓（R1-03・検証席 R1 の指摘）。
4. **torch の wheel は NVIDIA の EULA も SLA も同梱していない**（2.10.0 で再確認・NOTICE に NVIDIA/CUDA/cuDNN の語 0 件）＝配布側が 2 枚添えるほかない、という 2026-09-04 のノート 22 の要点 2 は pin 版でも同じ。ただし **2.10.0 の wheel には `dist-info/licenses/` の木が無く**、許諾文は `LICENSE`（547,174 B・116 件連結）と `NOTICE` の 2 檔だけ。`libiomp5md.dll`・`zlibwapi.dll` を覆う節はその中に無い（R4-06）。
5. **vc_redist を自前で配る根拠は Visual Studio Community／Professional／Enterprise の licence にしかない**＝本機に入っている **Build Tools 2026 の licence には Distributable Code 節が無い**（'Distributable' 0 件）。一方 VS2026 の Distributable List の柱書は Build Tools を名指しし、Build Tools 同梱の `Redist.txt` も同じ一覧を指す＝**頁と licence 本文が食い違う**。しかも `vc_redist.x64.exe` はどの一覧にも檔名で載っておらず（「`[VisualStudioFolder]\VC\redist` フォルダとその下」という場所の指定）、exe 単体の EULA（Cpp_v14）は「combined with any of your applications for others to use」を禁じる（R3-01〜R3-05）。
6. **署名の実測**（Radeon 版の実行系・PE 445 件）＝署名済み **36 件**（PSF 29・Microsoft 7）・**未署名 409 件・3,831,107,758 B（全体の 99.46 %）**。AMD の署名は 0 件、torch 自身の DLL は 0 件、scipy／numpy／sklearn の .pyd は全数未署名。cu 版と共通の 99 wheel から出る PE 282 件は展開物と sha256 が 282/282 一致＝cu 版でも同じ。**NVIDIA の DLL は本機に無く未測**（→ 2026-09-14 15:xx に RTX 席で cu130 の全数を実測＝§8-1-2）（RTX 席で要実測）。
7. **署名の権利**＝Azure Trusted Signing は「Azure Artifact Signing」に改称。**Public Trust の個人申込は米国・カナダ所在に限られ、日本の個人は対象外**（組織なら可）。「署名してよい物の範囲」を定める Terms of Use は Azure ポータル内でしか提示されず**未取得**。CA/Browser Forum の CSBR にも「自分の物しか署名してはならない」条文は無い（R3-09・R3-10）。
8. **置き場**＝GitHub Releases は 1 檔 2 GiB 未満・1 リリース 1,000 檔・総量と帯域は無制限（GitHub Docs）。**cu126 の torch wheel（2,589,881,452 B）は 2 GiB（2,147,483,648 B）を超える**＝そのままでは載らない。AUP §9 の「significantly excessive」は GitHub の裁量と明記（R3-11・R3-12）。
9. **copyleft は first-run-notices の 3 件（soxr・certifi・tqdm）に加えて新規 3 件**＝setuptools 同梱 `autocommand`（LGPL-3.0）・grpcio 同梱 `roots.pem`（MPL-2.0）・setuptools 同梱 validate-pyproject（MPL-2.0）。**numpy・scipy 同梱の OpenBLAS は wheel 自身の LICENSE が GPL-3.0-or-later WITH GCC-exception-3.1 を名乗る**（宣言の License-Expression には無い語）。**torch の LICENSE 連結本文にも GPL／LGPL 全文が混ざる**（cpr/test・ffnvcodec・vulkan）。CUDA EULA §1.2 第 5 条（open source 化の禁止）との同居の読みは卓。
10. **許諾文が wheel に無く配布側が用意する物**＝PyPI 5 件（descript-audiotools・ffmpy・sentencepiece・tensorboard-data-server・tokenizers）＋導入手順で檔が落ちる 4 件（dacvae・silentcipher・argbind・randomname）＋埋め込み Python が同梱する OpenSSL・SQLite・expat・liblzma・mpdecimal（`LICENSE.txt` に語が 0 件）＋NVIDIA 2 枚＋zlib＋libiomp5md。

---

## 2. 構成物ごとの台帳の素（v3 の権利台帳の元表）

> 欄＝**再配布の明文**＝原文にその物の再配布を許す文があるか（明文可／条件付き／見つからず／明文で禁止／不明）。**改変・再署名**＝改変（Authenticode 署名の付け直しを含みうる）に触れる文。**署名**＝R6 の実測。**札**＝主席の分類（判断ではない）。

| # | 構成物（台帳の pin） | ライセンス（原文の名乗り） | 原文の所在（取得 2026-09-14） | 再配布の明文 | 主な条件（逐語の要約・檔:行は findings） | 改変・再署名に触れる文 | 署名（実測） | 札 |
|---|---|---|---|---|---|---|---|---|
| 1 | 埋め込み Python 3.12.10（zip 35 エントリ・11,133,606 B） | PSF License Agreement（zip 内 `LICENSE.txt` 36,874 B・702 行・2026-09-04 と CRLF 差のみ） | `originals/py312-embed-LICENSE-2026-09-14.txt` | **条件付き**（PSF §2 :81-89 は明文許諾。ただし対象は Windows binary build＝同檔 :283-320「Additional Conditions for this Windows binary build」が Microsoft Distributable Code について転嫁義務と 4 つの禁止を課す） | PSF §2「provided, however, that PSF's License Agreement and PSF's notice of copyright … are retained」／§3「include in any such work a brief summary of the changes made to Python」／:291-314 の転嫁義務と禁止列挙／PSF §6・BeOpen §5・CNRI §6 の自動終了 | PSF §2「prepare derivative works」（改変は許諾）。**署名の付け直しに触れる語は 0 件**。:303-304「alter any copyright, trademark or patent notice in Microsoft's Distributable Code」 | **31/31 署名済み**（PSF 29・Microsoft 2）＝手を入れる必要が無い唯一の区画 | 条件付き可。配布側が変えるのは `python312._pth` 1 檔（80 B→378 B）だけで、`LICENSE.txt`・`python312.zip`・exe・dll は sha256 無改変（実測）。§3 の当てはめは卓 |
| 1b | 同 zip 内の第三者ネイティブ物（`libcrypto-3.dll` 5.2 MB・`libssl-3.dll`・`sqlite3.dll`・`_lzma.pyd`・`pyexpat.pyd`・`_decimal.pyd`） | **zip の `LICENSE.txt` に OpenSSL・SQLite・expat・lzma(xz)・zlib・mpdecimal の語が 0 件**（実測）。在るのは bzip2・libffi・無名の Apache-2.0（:389-566）・Tcl/Tk/Tix のみ | 同上 | **不明**（許諾文が同梱されていない） | — | — | 署名済み（PSF） | **穴**＝v3 で配るなら各許諾文を別途用意する |
| 2 | torch 2.10.0+cu130／+cu126 本体（NVIDIA 以外＝`torch_cpu.dll` 265 MB・`torch_cuda.dll` 409 MB／1,039 MB・`torch_python.dll`・`c10*.dll`・`caffe2_nvrtc.dll`・`uv.dll`・`libiomp5md.dll`・`libiompstubs5md.dll`） | `METADATA:6` `License: BSD-3-Clause`（License-Expression 行・分類子とも 0 件）。`dist-info/LICENSE` 547,174 B＝冒頭 BSD-3（From PyTorch :1-11・From Caffe2 :13-57）＋ `Name:/License:/Files:` 116 件の一覧（:90-668）＋各本文の連結（:670-9105）。`NOTICE` 24,088 B。**cu130 と cu126 で LICENSE・NOTICE は sha256 同一** | `originals/torch-2.10.0+cu130-dist-info-LICENSE-2026-09-14.txt`（sha256 `114a3beb…`）・`…NOTICE…`・`…METADATA…`（cu126 も同名で保存） | **条件付き**（BSD-3 §2「Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution」） | 116 件の内訳（検証席の実測）＝MIT 37／BSD-3 37／Apache-2.0 21／BSD-2 7／MIT with exception 5／Apache-2.0 with exception 4／BSL-1.0 2／Permissive・Public Domain 系 3。**連結本文に GPL／LGPL 全文が入る**（:3746 cpr\test・:6909 ffnvcodec・:7299 vulkan）。:2227-2237 に SPDX 識別子と「明文の禁止」（検証席 R4 過大主張 R4-05 の指摘・主席は未精読） | BSD-3 に「modification」の語（許諾側）。署名の語は :7077「signature of Ty Coon」（LGPL 雛形）のみ | **torch 自身の DLL は 0 件署名**（Radeon 版で 15/16 未署名・345 MB。署名があるのは Microsoft 由来 `libomp140.x86_64.dll` だけ） | 条件付き可。**LICENSE に DLL 名は 1 件も無い**＝「冒頭 BSD-3 が torch_cpu.dll を覆う」は読み。**`libiomp5md.dll`・`libiompstubs5md.dll` を覆う節が無い**（openmp 0・iomp 0・llvm-openmp 無し＝2.14.0 の `third_party/llvm-openmp/LICENSE.txt` が 2.10.0 には無い）。`uv.dll` に近いのは tensorpipe/third_party/libuv（MIT）1 項目のみ |
| 3 | NVIDIA CUDA Toolkit 実行時 DLL（cu130＝`cudart64_13`・`cublas64_13`・`cublasLt64_13` 478 MB・`cufft64_12`・`cufftw64_12`・`cusparse64_12`・`cusolver64_12`・`curand64_10`・`nvrtc64_130_0`・`nvrtc-builtins64_130`・`cupti64_2025.3.0`・`nvToolsExt64_1`／cu126 は 12 系の同名） | License Agreement for NVIDIA Software Development Kits（CUDA Toolkit EULA v13.4・**2026-09-04 の v13.3 と許諾・制限・Attachment A の語句差 0 件**） | `originals/cuda-eula-2026-09-14.txt`（79,670 B・sha256 `83b4545e…`）・html 103,131 B | **条件付き**（§1.1.1 License Grant :70-73・§2.2「The portions of the SDK that are distributable under the Agreement are listed in Attachment A」・Attachment A Windows 欄に上記の名） | §1.1.2 Distribution Requirements 6 条（:75-81）＝material additional functionality／「shall only be accessed by your application」／sample source の告知／**「Unless a developer tool is identified in this Agreement as distributable, it is delivered for your internal use only」**／配布条件の整合／不遵守の通知。§1.2 Limitations＝notices 除去禁止・**modify 禁止（「Except as expressly provided in this Agreement」の但書つき）**・stand-alone 配布禁止・**open source software license の subject にする使い方の禁止（列挙＝source 開示・派生物許諾・「Redistributable at no charge」）**。§2.1 License Scope :142「only for use in systems with NVIDIA GPUs」。§1.6 Termination :120-126 | §1.2 第 2 条「modify」／第 1 条「remove copyright or other proprietary notices」／第 4 条「bypass … authentication mechanism」。**signature・authenticode・certificate の語は 0 件**（「signed」は契約書面と affidavit の 3 件のみ） | **未測**（本機に無い） | 条件付き可。**EULA は torch の wheel に同梱されていない＝配布側が添える**。「no charge」は §1.2 の列挙内の 1 語で、無償配布を直接禁じる条文ではない（自作分 MIT との整合は卓） |
| 3b | Attachment A に名が見つからない NVIDIA 由来 DLL＝`nvperf_host.dll`（cu130 27,764,256 B／cu126 15,363,656 B）・`nvrtc64_130_0.alt.dll`（91,028,512 B）／`nvrtc64_120_0.alt.dll`（45,926,912 B）・`cusolverMg64_12.dll`（95,365,152 B）／`cusolverMg64_11.dll`（84,664,320 B）・**`nvJitLink_130_0.dll`／`nvJitLink_120_0.dll`（Attachment A は `libnvJitLink.dll` 表記）** | CUDA EULA（Attachment A に個別の名が無い＝grep 0 件：nvperf・nvperf_target・cusolverMg・「.alt」） | 同上 | **見つからず** | 柱書「The following CUDA Toolkit files may be distributed with applications developed by you, including certain variations of these files that have version number or architecture specific information embedded in the file name」 | — | 未測 | **v3 の形を変えうる**＝救えなければ外す（torch が読み込み時に要求するかは未検分） |
| 4 | cuDNN 実行時 DLL（各版 8 件＝`cudnn64_9`・`cudnn_adv64_9`・`cudnn_cnn64_9`・`cudnn_engines_precompiled64_9` 189 MB／514 MB・`cudnn_engines_runtime_compiled64_9`・`cudnn_graph64_9`・`cudnn_heuristic64_9`・`cudnn_ops64_9`） | NVIDIA SDK SLA＋cuDNN Supplement（v. February 22, 2022・2026-09-04 と本文差 0 件） | `originals/cudnn-eula-2026-09-14.txt`（21,468 B・sha256 `f2d637fb…`） | **条件付き**（Supplement §2「The following portions of the SDK are distributable under the Agreement: the runtime files .so and .dll.」） | Distribution Requirements は CUDA と同文。Limitations 2.1〜2.5＝CUDA §1.2 とほぼ同文（**CUDA 側の「For clarity, you may not distribute or sublicense the SDK as a stand-alone product」が cuDNN 側に無い**など条ごとに差あり）。**cuDNN SLA に §1.1.6 第 2 段（OSI アプリの develop and test）に当たる文は無い** | 2.2「modify」・2.1「remove … notices」 | 未測 | 条件付き可。**cudnn の語は CUDA EULA 全文に 0 件＝2 枚要る** |
| 5 | `torch/lib/zlibwapi.dll`（89,088 B・cu130／cu126 で sha256 一致） | 版資源＝製品名 ZLib.DLL・版 1.2.3.0・(C) 1995-2003 Jean-loup Gailly & Mark Adler・元檔名 zlib.dll・**CompanyName 空・NVIDIA の名は 0 文字**。zlib License 本文を保存 | `originals/zlib-license-2026-09-14.txt`（1,462 B） | **不明**（zlib License は明文許諾だが、この DLL が zlib 公式 build か NVIDIA 配布物かの同定が未了） | zlib License 制限 2・3 は「source version」「source distribution」に掛かる文 | zlib License 2.「Altered source versions must be plainly marked as such」 | **NotSigned**（1 檔だけ実測） | torch の LICENSE／NOTICE に zlib の記載 0 件＝添えるなら配布側 |
| 6 | torchaudio 2.10.0+cu130（2,008,471 B・落として実読・sha256 台帳と一致） | **BSD 2-Clause**（wheel 内 LICENSE 1,363 B・NOTICE 無し・License-File 1 件） | `originals/torchaudio-2.10.0+cu130-dist-info-LICENSE-2026-09-14.txt` | **条件付き** | BSD-2 §2（binary 再頒布の表示） | — | 未署名（Radeon 版の 3 件で実測） | 同梱の `libctc_prefix_decoder.pyd`・`pybind11_prefixctc.pyd` に第三者の許諾檔が無い（検証席の指摘） |
| 7 | AMD ROCm wheel 8 件（`rocm-sdk-core` 758,050,981 B・`rocm-sdk-libraries` 116,831,755 B・`rocm-sdk-device-gfx1151` 139,346,623 B・`rocm` sdist 24,780 B・`amd-torch-device-gfx1151` 50,010,016 B・`amd-torch-device-gfx115x` 187,800,925 B・`torch` 2.13.0+rocm10.0.0 113,440,240 B・`torchaudio` 2.11.0.2+rocm10.0.0） | wheel の宣言＝**core／libraries／device／amd-torch-device の 5 件は METADATA に License 欄が無い**。wheel 内の許諾檔＝core の `amd_comgr/LICENSE.txt`（Apache-2.0 WITH LLVM Exceptions・15,289 B）と `hipcc/LICENSE`（MIT 型）の 2 件だけ、libraries／device は 0 件。sdist `rocm` は各 .py の 2 行目に `SPDX-License-Identifier: MIT`（11 檔）。torch は License-File 107 件だが aotriton・liblzma・MIOpen・rocm 系は 0 件 | `originals/rocm-license-page-2026-09-14.txt`・`therock-LICENSE-2026-09-14.txt`（MIT 1,113 B）・`therock-README／RELEASES／python_packaging-2026-09-14.md`・`amd-software-eula-2026-09-14.txt`・`wheel-rocm_sdk_core-amd_comgr-LICENSE-2026-09-14.txt`・`…hipcc…`・`deployed-rocm-dist-info-METADATA-2026-09-14.txt` | **見つからず**（sdist `rocm` のみ条件付き可） | ROCm 公式 licensing 頁＝「licensed per component separately」＋成分表（MIT／NCSA／BSD-2 等）＋許諾文の所在は `/opt/rocm/share/doc/<component-name>/` と指示。**Windows wheel の share/doc には 2 件しか無い**。Comgr は頁（NCSA）と同梱檔（Apache-2.0 WITH LLVM-exception）が食い違う。取得元 `stable.repo.amd.com` は href の羅列のみで terms／LICENSE は 404（4 URL）。TheRock の README・RELEASES・python_packaging に licen の語 0 件＝wheel に License 欄が無いのは梱包テンプレの `setup()` に license 引数が無いため（観測） | **AMD Software EULA §3「distribute, publish, display, sublicense, assign, or otherwise transfer」禁止・「so that any part becomes subject to a Free Software License」禁止**。ただし **この EULA が ROCm の wheel に掛かると書いた一次資料は無い** | **AMD の署名 0 件**＝`_rocm_sdk_core` 94/94 未署名（2,188,101,632 B・最大 `MIOpen.dll` 493,655,040 B）・`_rocm_sdk_libraries` 50/50 未署名（999,872,512 B） | **v3 の形を変えうる**＝許諾文の無い約 60 DLL（`amdhip64_7`・`amdocl64`・`rocm-openblas*`・`libclang`・`rocm_kpack`・`cltrace`・`hipdnn_backend`・`origami` ほか）を自前の置き場から配る根拠が原文に無い。上流 GitHub（ROCm/ROCm・clr・rocBLAS・hipBLASLt・MIOpen・llvm-project・ROCR-Runtime・rocm-libraries・aotriton）は MIT／NCSA／Apache-2.0 WITH LLVM-exception／BSD-2 で明文可だが「ソースの許諾がこのバイナリにも及ぶ」は法解釈。`hipdnn_backend.dll`・`origami.dll`・`rocm-openblas*.dll`・`rocm_kpack.dll` は成分表にも公開リポジトリにも対応が見つからない |
| 8 | PyPI wheel／sdist 99 件（torch・torchaudio を除く台帳全件・cu126／rocm と共通・ネイティブ物 251 檔） | 台帳の license 欄と wheel の実値＝**96 件一致・1 件不一致（scipy＝台帳は分類子を書くが wheel に分類子が無い）・2 件は宣言そのものが無い（dacvae・silentcipher）** | `findings/R5-pypi-table.csv`（99 行・native_paths 欄つき）・`originals/`（soundfile・soxr・certifi・tqdm・numpy・scipy・pillow・regex・llvmlite・requests・msgpack・pyaml・cffi・typing_extensions・setuptools 系ほか）・`originals/R5-originals-index.json` | permissive 71 件＝**条件付き**（表示保持）／copyleft 7 件＝**条件付き**（下）／許諾文欠落 9 件＝**不明のまま配布側が用意** | **許諾文の檔が wheel に 0 件＝5 件**（descript-audiotools・ffmpy・sentencepiece・tensorboard-data-server・tokenizers＝上流 HEAD から今日取得・pin 版のタグではない）。**package_dir の外に在って導入で落ちる 4 件**（dacvae・silentcipher・argbind・randomname）。**Apache-2.0 §4(d) の NOTICE が発動するのは requests 1 件だけ**（38 B）。fire・msgpack は Apache-2.0 宣言だが同梱は定型告知 13 行のみ。regex＝Apache-2.0 AND **CNRI-Python**（§2 の代替文の選択）。pillow＝MIT-CMU（advertising 条項）＋**FreeType は FTL と GPLv2 の二者択一**。llvmlite＝BSD-2 AND Apache-2.0 WITH LLVM-exception。cffi MIT-0・pyaml WTFPL・packaging（OR）・typing-extensions PSF | LGPL-2.1 §6 a〜e（soxr・libsndfile）／MPL-2.0 §3.2「such Covered Software must also be made available in Source Code Form」（certifi・tqdm・roots.pem・validate-pyproject）／**LGPL-3.0**（setuptools 同梱 autocommand 2.2.2） | **全数未署名**（scipy 106・sklearn 69・numpy 19・numba 14・PIL 8 ほか）。例外は wheel 同梱の Microsoft 署名 `msvcp140`（llvmlite.libs 557,648 B・numpy.libs 575,056 B・sklearn\.libs 642,720 B＝**3 本とも版が違う**）と `vcomp140` | 条件付き可（大半）。**copyleft 7 件**＝soxr（LGPL-2.1-or-later・実体は `soxr_ext.pyd` に組み込み）・libsndfile（LGPL-2.1・`_soundfile_data/COPYING` 同梱）・certifi（MPL-2.0）・tqdm（MPL-2.0 AND MIT）・**新規** autocommand（LGPL-3.0）・grpcio `roots.pem`（MPL-2.0）・setuptools validate-pyproject（MPL-2.0）。**numpy・scipy 同梱 OpenBLAS＝GPL-3.0-or-later WITH GCC-exception-3.1**（wheel 自身の LICENSE が名乗る・宣言には無い）。**msvcp140／vcomp140 が 3 wheel に同梱**＝MSVC 再頒布の「licensed Visual Studio users」「You may not modify」（numpy・llvmlite は檔名を改名）が同梱 wheel にも掛かるかは卓 |
| 9 | `VC_redist.x64.exe` 14.44.35211.0（25,635,768 B・本機の Build Tools 2026 同梱は 14.51.36247.0） | Visual Studio の Microsoft Software License Terms「Distributable Code」節＋Distributable List（vs17／vs18）／exe 単体の EULA＝MICROSOFT VISUAL C++ V14 REDISTRIBUTABLE and RUNTIME（Cpp_v14） | `originals/vs2022-ga-community-2026-09-14.txt`・`vs2022-ga-proenterprise…`・`vs2022-ga-diagnosticbuildtools…`・`vs2026-ga-community…`・`vs2026-ga-pro-enterprise…`・`vs2026-ga-diagnostic-buildtools…`・`vs2026-ga-visualcpp-v14-redist-runtime…`・`vs2022-redist…`（2026-09-04 と本文同一）・`vs2026-redist…`・`msvc-redist…`（同一）・`latest-supported-vc-redist…`（新規） | **条件付き（Community／Pro／Enterprise の licence）／見つからず（Build Tools の licence）／明文で禁止（Cpp_v14 EULA）** | Distributable List 柱書「If you have a validly licensed copy of such software, you may copy and distribute with your program the unmodified form of the files listed below」／Visual C++ Runtime Files 節「any of the files within the following folder and its subfolders … You may not modify these files.」／Distribution Requirements＝significant primary functionality・再頒布者と利用者に同等の条件への同意を求める・（2026 版と Pro／Ent）Microsoft への indemnify／「Distribution … is limited to licensed Visual Studio users」／**`vc_redist.x64.exe` は檔名でどの一覧にも無い**／Cpp_v14 EULA「combined with any of your applications for others to use」禁止 | 「You may not modify these files」（Distributable List）・Cpp_v14 EULA の改変禁止 | **署名済み**（Microsoft・実物で確認） | **v3 の形を変えうる**＝本機の Build Tools だけでは条文上の根拠が無い（VS2026 の一覧の柱書は Build Tools を名指しする＝食い違い）。Community の利用資格条（individual／OSS／academic／5 users）で使う者が「licensed Visual Studio users」かは卓（first-run-notices §C 1 と同じ論点・今回は本文が揃った） |
| 10 | 置き場＝GitHub Releases | GitHub Docs About releases／Acceptable Use Policies §9／Terms of Service／Additional Product Terms | `originals/gh-about-releases…`・`gh-large-files…`・`gh-aup…`・`gh-tos…`・`gh-additional-product-terms…` | （権利ではなく上限） | 「Up to 1000 release assets … Each file included in a release must be under 2 GiB. There is no limit on the total size of a release, nor bandwidth usage」／AUP §9「significantly excessive in relation to other users of similar features」（GitHub の裁量）／Additional Product Terms に Releases の項は無い | — | — | **cu126 の torch wheel 2,589,881,452 B は 2 GiB 超**＝分割・cu126 を切る・自前配信のいずれか（設計＝卓）。cu130 の torch 1,867,405,006 B と Radeon 版の各檔は 2 GiB 未満 |
| 11 | 署名の権利＝Azure Artifact Signing（旧 Trusted Signing）／CA/Browser Forum CSBR 3.11.0 | Microsoft Learn（overview・quickstart・FAQ・concept 5 頁・pricing）／CSBR | `originals/artifact-signing-*-2026-09-14.txt`・`trusted-signing-overview…`・`cabf-csbr-index-2026-09-14.txt` | （該当なし） | **Public Trust の Individual developers は米国・カナダ所在に限る（日本の個人は対象外・組織なら日本も可）**／証明書は 72 時間で更新・CN／O は選べない／識別検証＝政府発行 ID＋住所証明＋顔照合／料金は頁の静的 HTML では `$-`（Basic／Premium・月の署名数 quota・超過は 1 署名単位）／**「署名してよい物の範囲」の条文は 9 頁のどこにも無く、service Terms of Use は Azure ポータル内のみ＝未取得**／CSBR §9.6.3 に「所有していない物に署名してはならない」条文は無く、明文の制限は「Suspect Code に署名しないこと」のみ | — | — | **v3 の形を変えうる**＝司令官が個人として Public Trust を取れない（所在地条件）。CA は CSBR より厳しい条件を Terms of Use で課しうる＝未取得の Terms を読むまで「第三者の未署名 .pyd／.dll に署名してよいか」は決まらない |

---

## 3. 署名の実測（R6・Radeon 版の展開済み実行系＝`%LOCALAPPDATA%\irodori-tts-ywk-radeon\runtime\rocm-gfx1151\`・`findings/R6-signatures.csv` 445 行）

| 区画 | PE 檔数 | 署名済み（署名者） | 未署名 | 未署名バイト |
|---|---|---|---|---|
| python-embed（直下） | 31 | 31（PSF 29・Microsoft Windows Software Compatibility Publisher 2＝vcruntime140／_1） | 0 | 0 |
| torch 2.13.0+rocm10.0.0 | 16 | 1（Microsoft Corporation＝`libomp140.x86_64.dll`） | 15 | 345,119,744 |
| `_rocm_sdk_core` | 94 | 0 | 94 | 2,188,101,632 |
| `_rocm_sdk_libraries` | 50 | 0 | 50 | 999,872,512 |
| scipy／scipy.libs | 107 | 0 | 107 | — |
| sklearn／sklearn\.libs | 71 | 2（Microsoft＝`msvcp140.dll`・`vcomp140.dll`） | 69 | — |
| numpy／numpy.libs | 21 | 1（Microsoft＝`msvcp140-<hash>.dll`） | 20 | — |
| llvmlite／llvmlite.libs | 2 | 1（Microsoft＝`msvcp140-<hash>.dll`） | 1 | — |
| その他（PIL 8・numba 14・torchaudio 3・libsndfile 1・soxr 1・setuptools の exe 雛形 8・ほか 1 件ずつ） | 53 | 0 | 53 | — |
| **合計** | **445** | **36（全件タイムスタンプ付き）** | **409** | **3,831,107,758 B（全 3,851,785,822 B の 99.46 %）** |

- cu 版と共通の 99 wheel（build-cache）から取り出した PE 282 件は展開物と sha256 が 282/282 一致＝cu 版でも共通分は同じ署名状況。差 163 件（`_rocm_sdk_*` 144・torch 16・torchaudio 3）は Radeon 版だけの物。
- **NVIDIA の DLL は本機に無く未測**（→ 2026-09-14 15:xx に RTX 席で cu130 の全数を実測＝§8-1-2）。NVIDIA が再頒布 DLL に署名して配っているかの一次資料も見つからず（CUDA Windows インストールガイドにコード署名の記述無し）。**RTX 席で cu130／cu126 の実行系に `Get-AuthenticodeSignature` を全数で掛けるのが唯一の確実な道**＝v3 の署名工数の見積はそれまで確定しない。
- 檔名を改名した `msvcp140-<hash>.dll` が Microsoft の署名を保ったまま Valid で検証できる（改名は Authenticode を壊さない・実測）。
- 検証席 R6（逐語）の指摘＝R6 の JSON の「引用」61 件のうち原文そのままの逐語は 2 件で、残りは CSV からの派生集計や地の文。**数値はすべて CSV から再計算して一致**（GRAND files=445 valid=36 unsigned=409）。実測に使った PowerShell 台本は保存されていない（再現は CSV から）。

---

## 4. v3 の形を変えうる物（**条文上、配れない可能性がある物**＝裁定 148 ⑶「配れない物があれば v3 の形を変える」の入力）

1. **Radeon（ROCm）版の実行系**＝許諾文の無い AMD の wheel 5 件（表 #7）。原文に再配布の根拠が無い＝自前の置き場から配る形は、AMD の文書確認か「ソースの許諾がバイナリに及ぶ」という卓の読みが無ければ成り立たない。**v2 のまま初回取得に残す**のが条文上は安全側（席の判断ではなく、明文の有無の対比）。
2. **cu126 版**＝torch wheel が 2 GiB 超で GitHub Release に 1 檔で載らない（表 #10）。
3. **NVIDIA の 3 DLL＋nvJitLink の接頭辞**（表 #3b）＝Attachment A に名が無い。救えなければ外す（外せるかは技術検分＝未実施）。
4. **vc_redist**（表 #9）＝Build Tools のみの本機では Distributable Code の条文が無い。Community 等の licence を根拠に置くか、v2 の現行（Microsoft の URL への誘導）を v3 でも残すか（裁定 148 ⑵ との関係）。
5. **署名**（表 #11）＝日本の個人は Public Trust の対象外。組織での申込・OV／EV 証明書（裁定 150 ⑴ の ⒝）・Private Trust（スマート アプリ コントロールの信頼に届くかは未検分）のいずれか。「第三者の未署名バイナリに署名してよいか」の条文は未取得の Terms of Use にしか無い公算。
6. **copyleft と NVIDIA §1.2 第 5 条の同居**＝LGPL 2 件・LGPL-3.0 1 件・MPL 4 件・OpenBLAS の GPL-3.0 WITH GCC-exception・torch LICENSE 内の GPL／LGPL 本文と、CUDA EULA／cuDNN SLA／AMD EULA の「open source software license の subject にしない」条項が同じ配布物に並ぶ（読みは卓）。
7. **許諾文を配布側が用意する物**（要点 10）＝配布物の `licenses/` が v2 の 13 檔から大きく増える（NVIDIA 2・zlib・libiomp5md・OpenSSL 系 5・PyPI 9・torch 連結 LICENSE の扱い）。

---

## 5. 卓（司令官）の裁定が要る点（席は断定しない・原文の所在つき）

| # | 論点 | 原文 |
|---|---|---|
| 1 | Attachment A の柱書「certain variations of these files that have version number or architecture specific information embedded in the file name」で `nvperf_host.dll`・`nvrtc64_1x0_0.alt.dll`・`cusolverMg64_1x.dll`・`nvJitLink_1x0_0.dll`（原文は `libnvJitLink.dll`）を救えるか | cuda-eula :153-302（Attachment A） |
| 2 | §1.1.2 第 2 条「shall only be accessed by your application」を、GitHub Release から落として専用フォルダに展開する形が満たすか。第 4 条「internal use only」が nvperf_host に掛かるか | cuda-eula :76・:79 |
| 3 | §1.2 第 2 条「modify」・第 1 条「remove … notices」・第 4 条「authentication mechanism」に、NVIDIA の DLL への Authenticode 再署名（既存署名の上書きを含む）が当たるか。同じ問いが AMD・LGPL（§6「modification of the work for the customer's own use」）・Microsoft（「You may not modify these files」・LICENSE.txt :303-304）・BSD／Apache／MIT の「modification」にも掛かる | cuda-eula :99-106・cudnn :130-138・soundfile-COPYING §6・vs2022-redist・py312-embed-LICENSE :303-304 |
| 4 | §1.2 第 5 条「use the SDK in any manner that would cause it to become subject to an open source software license」（列挙＝source 開示・派生物許諾・「Redistributable at no charge」）と、自作分 MIT・無償配布・LGPL／MPL／GPL-with-exception の同居 | cuda-eula :103-106・cudnn :138・amd-software-eula §3 |
| 5 | §1.1.2 第 5 条「The terms under which you distribute your application must be consistent with the terms of this Agreement」を配布ページ・同梱 LICENSE でどう組むか | cuda-eula :80 |
| 6 | AMD の wheel＝「許諾文が無い＝上流の梱包漏れ」と読むか「許諾が及ばない」と読むか。AMD Software EULA が wheel に掛かるか。AMD に文書で確認するか。上流 GitHub の許諾文を配布側が添える形でよいか | rocm-license-page・amd-software-eula・therock-* |
| 7 | 「licensed Visual Studio users」「validly licensed copy」に Community の利用資格条で使う者が当たるか。Build Tools 2026 の licence（Distributable Code 節無し）と VS2026 Distributable List の柱書（Build Tools を名指し）の食い違いをどう扱うか。Cpp_v14 EULA の「combined with any of your applications」禁止と Distributable Code の許諾のどちらがどの立場に掛かるか。indemnify 条項を負うか | vs2022-ga-community §（Distributable Code・利用資格）・vs2026-ga-diagnostic-buildtools・vs2026-redist 柱書・vs2026-ga-visualcpp-v14-redist-runtime |
| 8 | LGPL-2.1 §6 a〜e のどれで libsndfile・soxr の義務を満たすか（既出＝first-run-notices §C 2）。MPL-2.0 §3.2 の Source Code Form を上流 GitHub の参照で満たすか。autocommand（LGPL-3.0）・roots.pem・validate-pyproject を含む setuptools／grpcio を配布物に残すか | soundfile-COPYING.LGPL21 §6・soxr-COPYING.LGPL・mpl-2.0 §3.2〜3.4・setuptools-vendor-autocommand-LICENSE |
| 9 | numpy・scipy の OpenBLAS＝宣言（BSD 系）と同梱 LICENSE（GPL-3.0-or-later WITH GCC-exception-3.1）のどちらを通知に書くか。pillow の FreeType を FTL と GPLv2 のどちらで受けるか。regex の CNRI §2 代替文を使うか。packaging の OR をどちらで受けるか。pyaml の WTFPL を公開頁でどう見せるか | numpy-LICENSE・scipy-LICENSE・pillow-LICENSE・regex-LICENSE・packaging-LICENSE・pyaml-COPYING |
| 10 | PSF §3「brief summary of the changes made to Python」が `python312._pth` の差し替え・`site-packages/` の追加に当たるか。`LICENSE.txt` :291-314 の Microsoft Distributable Code の転嫁義務を v3 の同意 UI でどう満たすか。zip 内 OpenSSL・SQLite・expat・liblzma・mpdecimal の許諾文を誰が用意するか | py312-embed-LICENSE :91-95・:283-320 |
| 11 | torch の LICENSE（547,174 B・GPL／LGPL 本文と「明文の禁止」:2233-2236 を含む）を配布物にそのまま同梱するか。`libiomp5md.dll`・`zlibwapi.dll`・`uv.dll` の許諾を別途取るか外すか。torchaudio 同梱 .pyd の第三者物 | torch-2.10.0+cu130-dist-info-LICENSE :1-57・:2227-2237・:3746・:6909・:7299 |
| 12 | 署名＝個人（日本）が Public Trust を取れない事実を受けて、組織申込・OV／EV・Private Trust・署名しない（裁定 146 継続）のどれを採るか。Terms of Use（未取得）を読むまで「第三者バイナリへの署名の可否」は保留 | artifact-signing-overview／faq／quickstart |
| 13 | 置き場＝cu126 の torch wheel（2 GiB 超）を分割するか・cu126 を切るか・自前配信にするか。AUP §9 の帯域の裁量 | gh-about-releases・gh-aup §9 |
| 14 | SilentCipher の LICENSE（Sony Research Inc.）と台帳の取得先（SesameAILabs の fork）の組織名不一致（観測のみ） | silentcipher-LICENSE |
| 15 | 台帳の license 欄の出所＝scipy は wheel に分類子が無く PyPI の分類子から取った値。`make-ledger.ps1` の出所を書き直すか | ledger/runtime-cu130.json・R5-pypi-table.csv |

---

## 6. 検証の結果と、主席が直した点・直していない点

- **R1（NVIDIA）逐語**＝原文に無い引用は 68 件中 1 件（R1-04 の省略記号入り）。sha256 は 11 檔すべて再計算一致。行番号のずれ 19 件・途中切り 5 件。**過大主張**＝⑴ `nvJitLink_*.dll` を「Attachment A に名がある族」に入れていた→**本報告は 3b（見つからず）へ移した**。⑵ `zlibwapi.dll` を explicit-yes／allowed としていた→**不明に下げた**。⑶ cuDNN SLA に無い §1.1.6 第 2 段を cuDNN 込みで論じていた→表 #4 に「無い」と明記。⑷ cuDNN DLL「10 個」→各版 8 件。⑸ Limitations の CUDA／cuDNN 差→表 #4 に反映。未引用の条（§2.1 License Scope・§1.1.7・§1.7・§1.1.5・§1.3）は表 #3 に §2.1 のみ足した。
- **R4（Python・torch）逐語**＝捏造 0・36 件すべて原文に実在（smart quote の ASCII 化 2 件・行途中切り 1 件・行範囲 5 件）。**過大主張**＝R4-01 の explicit-yes→**条件付きに下げた**（Windows binary build の :283-320）。R4-02 の第三者物は物ごとに分けた（表 #1b）。R4-05 に GPL／LGPL 本文と :2227-2237 の存在を足した。R4-06 の grant は「LICENSE に DLL 名 0 件」の事実だけに戻した。終了条項（PSF §6・BeOpen §5・CNRI §6）を表 #1 に足した。
- **R6（署名）逐語**＝引用の大半が派生値・地の文（§3 末尾）。数値は全件再計算一致。
- **R2・R3・R5 は未検証**＝主席の目視で表に写した。R2 は「判断を一切書いていない」旨を自己申告、R3 は URL の 404 を正しい URL に読み替えて取得（vs2022-ga-buildtools／professional は 404・本文は diagnosticbuildtools／proenterprise）。R5 は上流 HEAD の許諾檔を取っており pin 版のタグではない。
- **JSON 側は直していない**（`findings/` は各席の出力のまま）。本報告の表が訂正後の値。

---

## 7. やり残し（本調査で取れなかった物）

1. ~~NVIDIA の DLL の Authenticode 実測（RTX 席）。~~ **済**（2026-09-14 15:xx・cu130 の全 PE 325 檔＝§8-1-2・`findings/F2-cu130-signatures-full-2026-09-14.csv`）。cu126 は未測のまま（断念の方向なので掛けていない）。
2. Azure Artifact Signing の service Terms of Use（Azure ポータル内・要サインイン）と料金の実額。
3. AMD への確認（wheel の再配布可否・許諾文の所在・`hipdnn_backend.dll` 等の出所）。`repo.radeon.com` 側の proprietary 区分が Windows wheel に及ぶか。`https://www.amd.com/en/corporate/copyright` は HTTP 200 のみ確認・未精読。
4. `nvperf_host.dll`・`nvrtc64_1x0_0.alt.dll`・`cusolverMg64_1x.dll` を外して torch が読み込めるかの技術検分。
5. PyPI の `nvidia-*` wheel の中身（ノート 22 未確認 7）。CUDA EULA の PDF 版は検証席が取得可能を確認（228,421 B・sha256 `4f51a431…`）だが originals に無い。
6. R5 の 5 件の許諾檔を pin 版のタグから取り直す。grpcio の LICENSE・scikit-learn の COPYING の originals 保存。
7. torchaudio 2.10.0+cu126 の wheel（cu130 と同じかは未確認）。
8. 埋め込み Python が同梱する OpenSSL・SQLite・expat・liblzma・mpdecimal の一次ライセンス。`libiomp5md.dll` の一次資料（Intel OpenMP か LLVM openmp か）。
9. R2・R3・R5 の敵対検証と批評席（枠切れ）。
10. R6 の実測台本の保存（再現は CSV から可能）。

---

## 8. 対象の絞り込み（司令官の指示 2026-09-14 12:2x・逐語「調査の対象は、未署名でWindowsセキュリティで停止されうるもので、こちらの署名で停止対象にならないもの。セキュリティ停止対象で、権利関係がクリアできないものであればv3は断念の方向で。」）

> 問いは 2 段＝⑴ **止められうる物**＝未署名の PE（.exe／.dll／.pyd）で実行時に読み込まれる物（裁定 150 ⑵の実例＝scipy の `_spline.cp312-win_amd64.pyd`）。署名済みの物（PSF・Microsoft・NVIDIA・Intel）は対象外。⑵ その物に**こちらが署名を付ける権利**（改変を許す文が原文にあるか）と**自前の置き場から配る権利**が原文で立つか。
> **追加の実測**（Fable 単体・2026-09-14 12:2x）＝torch 2.10.0+cu130／+cu126 の wheel から HTTP Range で 28 檔を抜き `Get-AuthenticodeSignature` を掛けた（`findings/F-nvidia-dll-signatures-sample-2026-09-14.csv`・sha256 つき。DLL 本体はリポジトリに入れていない）。

### 8-1 追加の実測＝NVIDIA の DLL の署名（標本 28 檔）

| 変種 | NVIDIA 署名あり（Valid・タイムスタンプ付き） | 未署名 |
|---|---|---|
| cu130 | `cudart64_13`・`cublas64_13`（50 MB）・`cufftw64_12`・`cupti64_2025.3.0`・`nvJitLink_130_0`（88 MB）・`nvperf_host`（27.7 MB）・`nvrtc-builtins64_130`・`cudnn64_9`・`cudnn_cnn64_9`・`cudnn_graph64_9`・`cudnn_ops64_9`（30 MB）＝**11/11** | `nvToolsExt64_1.dll`（48 KB・NVTX＝ノート 22 の観測では Apache-2.0 WITH LLVM-exception） |
| cu126 | `cudnn64_9`・`cudnn_cnn64_9`・`cudnn_graph64_9`・`cupti64_2024.3.2`・`nvperf_host`＝5 | **`cudart64_12.dll`・`cufftw64_11.dll`・`nvrtc-builtins64_126.dll`・`nvJitLink_120_0.dll`（39 MB）＝CUDA 12.6 本体の 4/4 が未署名**・`nvToolsExt64_1.dll` |
| 両方 | `libiomp5md.dll`＝**Intel Corporation の署名**（＝Intel OpenMP ランタイム。torch の LICENSE に該当節が無い物＝表 #2） | torch 自身（`c10.dll`・`c10_cuda.dll`・`caffe2_nvrtc.dll`）・`uv.dll`（cu130 で実測。Radeon 版の実測と同じく torch 自身は未署名） |

未標本＝cu130 の `cublasLt64_13`（478 MB）・`cufft64_12`・`cusparse64_12`・`cusolver64_12`・`cusolverMg64_12`・`curand64_10`・`nvrtc64_130_0`・`nvrtc64_130_0.alt`・`cudnn_engines_precompiled64_9`・`cudnn_adv64_9`・`cudnn_heuristic64_9`・`cudnn_engines_runtime_compiled64_9`・`zlibwapi`（Radeon 版で NotSigned を実測済み）。cu126 も同じ族が未標本。**全数は RTX 席で掛けた**（→ 8-1-2）。標本の範囲では「CUDA 13 系は NVIDIA が署名して配り、CUDA 12.6 系の本体は未署名」という差が出た。

### 8-1-2 全数実測＝cu130 の展開済み実行系の全 PE 325 檔（RTX 席「12900kのメインリポ」・2026-09-14 15:xx・読むだけ）

樹＝RTX 3090 機の `%LOCALAPPDATA%\irodori-tts-ywk-cuda\runtime\cu130\`（v2.0.x が取得した実行系・ドライバ 616.92・Windows 11 Pro 26200.9445）。`Get-AuthenticodeSignature`＋sha256 を exe／dll／pyd 全数に掛けた。原本＝N: `irodori-ywk\rtx\sig-audit-2026-09-14\`、写し＝`findings/F2-cu130-signatures-full-2026-09-14.csv`（325 行）と `findings/F2-cu130-signatures-summary-2026-09-14.md`。status は全 325 が Valid か NotSigned のみ（HashMismatch 等の異常 0）。

| 区分 | 檔数 | バイト | 内訳 |
|---|---|---|---|
| Valid（タイムスタンプ付き） | 60 | — | PSF 29・**NVIDIA 23**・Microsoft 6（llvmlite.libs・numpy.libs・sklearn 2・python-embed 2）・Intel 2（`libiomp5md`・`libiompstubs5md`） |
| NotSigned | 265 | 997,250,222 | torch 自身 10（`torch_cuda` 408,946,688・`torch_cpu` 265,100,288・`torch_python`・`c10`・`c10_cuda`・`caffe2_nvrtc`・`shm`・`torch`・`torch_global_deps`・`uv`）・NVTX 1（`nvToolsExt64_1.dll` 48,128）・`zlibwapi.dll` 89,088・PyPI の C 拡張 ≈ 250（scipy 106・sklearn 69・numpy 19・numba 14・PIL 8・setuptools 8・torchaudio 4・llvmlite・grpc・hf_xet・tokenizers ほか） |
| 全体 | 325 | 2,951,518,622 | |

**torch\lib の 37 DLL**＝NVIDIA 署名 23（未標本だった 13 のうち `zlibwapi` 以外の 12＝`cublasLt64_13`（477,896,816 B）・`cufft64_12`・`cusparse64_12`・`cusolver64_12`・`cusolverMg64_12`・`curand64_10`・`nvrtc64_130_0`・`nvrtc64_130_0.alt`・`cudnn_engines_precompiled64_9`・`cudnn_adv64_9`・`cudnn_heuristic64_9`・`cudnn_engines_runtime_compiled64_9` は**全て Valid／NVIDIA Corporation**）・Intel 署名 2・未署名 12（torch 自身 10＋NVTX＋zlibwapi）。

**8-2 の表への効き**＝⑴ 「未署名の NVIDIA DLL が 1 つも無い」は**字義どおりには不成立**＝`nvToolsExt64_1.dll`（NVTX・48 KB）が未署名。ただし NVTX はノート 22 の観測どおり Apache-2.0 WITH LLVM-exception（改変を許す文あり）で、8-2 の cu130 行では最初から ⑵ として「こちらが署名する側」に置いてある＝**CUDA EULA の下にある DLL（cudart・cublas・cuDNN・nvrtc・nvJitLink・cupti・nvperf ほか 23 檔）は全数が NVIDIA 署名済み**で、「こちらの署名」の対象外＝**cu130 の方向（8-3）は変わらない**。⑵ `zlibwapi.dll`＝cuDNN 同梱の zlib（作者 zlib・NVIDIA 署名なし）＝zlib License で改変可（出所の確認は §5／A9 のまま）。⑶ 8-2 の cu130 行の「torch 自身 7 種」は実測では **10**（`shm`・`torch`・`torch_global_deps` の小 DLL 3 本が加わる）＝B3 の「≒ 270 檔」は **265 檔・997,250,222 B** に置き換える。⑷ NVTX を NVIDIA 由来と数えるかは字義の問題であり、権利は Apache 側＝卓の裁定を要する新規の点は増えない。

ドライバ更新の許可（司令官 2026-09-14「ドライバ更新を許可。」）は RTX 席に伝えたが、この実測には不要（616.92／CUDA 13.4 で cu130 の NVIDIA DLL は全て署名検証を通っている）＝**更新は実行していない**。実行するなら前後の版を N: の summary.md に記録してから（席の申し合わせ）。

### 8-2 止められうる物 × 権利（変種ごと）

| 変種 | 止められうる物（未署名・実行時に読み込まれる） | 署名を付ける権利（改変を許す文） | 配る権利 | 材料から出る方向 |
|---|---|---|---|---|
| **cu130（RTX 既定）** | ⑴ torch 自身の DLL 7 種（`torch_cpu`・`torch_cuda`・`torch_python`・`c10`・`c10_cuda`・`caffe2_nvrtc`・`uv`）⑵ `nvToolsExt64_1.dll` ⑶ `zlibwapi.dll` ⑷ PyPI の .pyd／.dll 251 檔（scipy 106・sklearn 69・numpy 19・numba 14・PIL 8・llvmlite・libsndfile・soxr ほか）⑸ torchaudio 3 | ⑴ BSD-3・MIT（libuv）＝改変を許す文あり・禁止文なし ⑵ Apache-2.0 WITH LLVM-exception＝改変可 ⑶ zlib License＝改変可（出所未同定） ⑷ BSD／MIT／Apache／MIT-CMU＝改変可。LGPL-2.1（libsndfile・soxr）＝改変可＋§6 の条件。GPL-3.0 WITH GCC-exception（OpenBLAS）＝改変可＋条件 ⑸ BSD-2＝改変可 | 同上＝明文あり（条件＝表示保持・許諾文同梱・LGPL §6 の源入手手段・requests の NOTICE）。**NVIDIA の DLL は署名済みなので「こちらの署名」の対象外**＝EULA §1.2「modify」の問いは cu130 では立たない（ただし自前の置き場から配る権利は別に §1.1.1＋Attachment A の条件つき許諾・表 #3／#3b） | **権利上の障害は原文に見当たらない**（条件つき）。残る障害は権利以外＝⒜ 署名サービス（日本の個人は Public Trust 対象外・表 #11）⒝ Attachment A に名の無い 3 DLL＋nvJitLink の接頭辞（配る権利の側・表 #3b）⒞ 未標本の NVIDIA DLL の全数実測 |
| **cu126（選択肢）** | cu130 の集合＋**CUDA 12.6 本体の NVIDIA DLL（`cudart64_12`・`cufftw64_11`・`nvrtc-builtins64_126`・`nvJitLink_120_0`。未標本の `cublas64_12`・`cublasLt64_12`・`cufft64_11`・`cusparse64_12`・`cusolver64_11`・`curand64_10`・`nvrtc64_120_0` も同じ族の公算）** | NVIDIA の DLL＝CUDA EULA §1.2 第 2 条「Except as expressly provided in this Agreement, you may not … modify …」・第 1 条「remove copyright or other proprietary notices」＝**改変を許す文が無い**（署名の付け直しが modify に当たるかは卓＝§5-3。当たる読みなら原文ではクリアできない） | §1.1.1＋Attachment A（条件つき） | **断念の方向の材料が 2 本**＝⑴ 未署名の NVIDIA DLL にこちらが署名する権利が原文に無い ⑵ torch wheel 2,589,881,452 B が GitHub Release の 2 GiB 上限を超える（表 #10） |
| **Radeon（ROCm）** | cu130 の PyPI 集合＋**AMD の DLL 144 檔（`_rocm_sdk_core` 94・`_rocm_sdk_libraries` 50＝3,187,974,144 B・AMD の署名 0 件）**＋torch 自身 15 | AMD の DLL＝**許諾文が無い**（表 #7＝wheel に License 欄無し・許諾檔は amd_comgr・hipcc の 2 件のみ・取得元に terms 無し）。AMD Software EULA は改変（「modify」）と配布を禁じるが wheel に及ぶ資料は無い | **原文に根拠が無い** | **断念の方向**（司令官の基準どおり）＝止められうる物の中核（MIOpen.dll 494 MB ほか）の権利が原文でクリアできない。覆すには AMD の文書確認か「上流 GitHub の MIT／NCSA がバイナリに及ぶ」という卓の読みが要る |

**共通の注意**＝⑴ v2 で実際に止められた `_spline.pyd` は ⑷ の集合＝BSD で署名可。⑵ 署名済みの物（python-embed 31・Microsoft の msvcp140／vcomp140・NVIDIA・Intel）は「こちらの署名」の対象外だが、**自前の置き場から配る権利**は別の問い（vc_redist＝表 #9・Intel OpenMP＝許諾文未取得・NVIDIA＝表 #3）。⑶ 「止められうる」は評判の揺れ（裁定 150 ⑸）に依るので、未署名の全量（Radeon 版で 409 檔）が潜在的な対象。

### 8-3 方向（席の判断ではなく、上の表の写し）

- **cu130 だけの v3**＝権利上は原文で立つ（条件つき）。決めるのは権利以外の 2 点＝署名サービスの資格（個人・日本）と、Attachment A に無い DLL の扱い。**全数実測（8-1-2）後も同じ**＝CUDA EULA 下の NVIDIA DLL 23 檔は全て署名済み、未署名は NVTX（Apache）と zlibwapi（zlib）と torch 自身（BSD）と PyPI の C 拡張＝いずれも改変を許す文がある側。
- **cu126 と Radeon の v3**＝止められうる物の中に権利が原文でクリアできない物がある＝**司令官の基準では断念の方向**。cu126 は 2 GiB 超も重なる。
- **v3 を cu130 限定で進めるか、v3 全体を断念するか**＝卓。

---

## 9. cu130 専用版（v3）を作る場合に不明瞭な点の一覧（司令官の求め 2026-09-14 13:xx）

> 「不明瞭」＝原文・実測のどちらでもまだ決まっていない点。**A** は権利（卓の裁定か外部への確認）、**B** は署名（資格と効き目）、**C** は置き場と形（設計）、**D** は調査の未了。番号は §5 の論点番号と対応させていない。

### A. 権利（原文で決まらない）

| # | 不明瞭な点 | 原文・実測の現状 | 決め方 |
|---|---|---|---|
| A1 | Attachment A に名の無い DLL＝`nvperf_host.dll`・`nvrtc64_130_0.alt.dll`・`cusolverMg64_12.dll`、および `nvJitLink_130_0.dll`（原文は `libnvJitLink.dll`） | 柱書「certain variations of these files that have version number or architecture specific information embedded in the file name」だけが根拠候補。外して torch が動くかは未検分 | 卓の読み、または外す技術検分（RTX 席） |
| A2 | CUDA EULA §1.1.2 第 2 条「shall only be accessed by your application」を、GitHub Release から落として専用フォルダに展開する形が満たすか。第 4 条「developer tool … internal use only」が `cupti`・`nvperf_host` に掛かるか | 条文のみ | 卓 |
| A3 | §1.2 第 5 条（SDK を open source software license の subject にしない）と、同一配布物の自作 MIT・LGPL-2.1（libsndfile・soxr）・LGPL-3.0（setuptools 同梱 autocommand）・MPL-2.0（certifi・tqdm・grpcio roots.pem・validate-pyproject）・GPL-3.0 WITH GCC-exception（numpy／scipy の OpenBLAS）・torch LICENSE 内の GPL／LGPL 本文の同居 | 条文のみ（NVIDIA・cuDNN とも同型の条） | 卓（必要なら NVIDIA へ照会） |
| A4 | §1.1.2 第 5 条「配布条件の整合」を配布ページ・同梱 LICENSE・初回同意 UI でどう組むか（自作分 MIT・NVIDIA 分 EULA の切り分け表示） | 条文のみ | 設計＋卓 |
| A5 | cuDNN Supplement §2「the runtime files .so and .dll」に `cudnn_engines_precompiled64_9.dll`（189 MB・カーネル像）等も含むか | 文面は種別を分けていない | 卓 |
| A6 | `vc_redist.x64.exe`＝本機の Build Tools 2026 の licence に Distributable Code 節が無い。VS Community を入れて根拠にするか、v2 と同じ Microsoft の URL 誘導を残すか（裁定 148 ⑵「全部自前」との関係）。exe 単体の Cpp_v14 EULA（結合配布の禁止）との二枚掛かり。indemnify 条項 | 条文の食い違いまで確認済み | 卓 |
| A7 | wheel 同梱の `msvcp140`（numpy・llvmlite は改名済み）・`vcomp140`（sklearn）＝Microsoft 署名済みだが、自前で配る権利は VS の licence 側（「You may not modify these files」「licensed Visual Studio users」） | 実測＝3 本とも版が違う。どれが読み込まれるか未測 | 卓＋実測 |
| A8 | 埋め込み Python＝PSF §3「brief summary of the changes」が `python312._pth` の差し替えに当たるか。`LICENSE.txt` :291-314（Microsoft Distributable Code の転嫁義務）を同意 UI でどう満たすか。zip 同梱の OpenSSL・SQLite・expat・liblzma・mpdecimal の許諾文が無い | 実測＝LICENSE.txt に語 0 件 | 卓＋許諾文の取得（PSF の配布物から） |
| A9 | torch の `LICENSE`（547,174 B・GPL／LGPL 本文と :2233-2236 の明文禁止を含む）をそのまま同梱するか。`libiomp5md.dll`＝**Intel の署名＝Intel OpenMP**＝Intel の再頒布条件が未取得（torch の LICENSE に該当節無し）。`zlibwapi.dll` の出所未同定。`uv.dll` と libuv 項目の対応 | 実測まで | Intel の条件取得＋卓 |
| A10 | torchaudio 同梱の `libctc_prefix_decoder.pyd`・`pybind11_prefixctc.pyd` の第三者許諾が wheel に無い | 実測 | 上流確認 |
| A11 | PyPI＝許諾文が無い 5 件を pin 版のタグから取り直す／導入で落ちる 4 件の同梱／requests の NOTICE／fire・msgpack の本文無し／pillow の FreeType（FTL か GPLv2 か）／regex の CNRI 代替文／packaging の OR／pyaml の WTFPL の見せ方／OpenBLAS の宣言と LICENSE の食い違い／LGPL §6 a〜e の選択／MPL §3.2 の源の示し方／setuptools（LGPL-3＋MPL）と grpcio `roots.pem` を落とせるか | 実測と条文まで | 卓＋合成の技術検分 |
| A12 | SilentCipher の著作権者（Sony Research Inc.）と取得先（SesameAILabs の fork）の組織名不一致 | 観測のみ | 卓（気にするか） |

### B. 署名（資格と効き目）

| # | 不明瞭な点 | 現状 | 決め方 |
|---|---|---|---|
| B1 | 署名の手段＝Azure Artifact Signing の Public Trust は**日本の個人は対象外**（組織なら可）。代替＝OV／EV 証明書（裁定 150 ⑴ ⒝・年 100〜400 USD 級・ハードトークン）か Private Trust（スマート アプリ コントロールの信頼に届くか未検分） | 頁の条文まで | 卓（法人化するか・証明書を買うか） |
| B2 | **第三者の未署名バイナリに自分の証明書で署名してよいか**＝Artifact Signing の Terms of Use（Azure ポータル内・未取得）・証明書 CA の Subscriber Agreement | 未取得。CSBR には禁止文無し | 取得して読む（要サインイン） |
| B3 | 署名する範囲＝cu130 の未署名 PE（torch 自身 7・NVTX 1・zlibwapi 1・PyPI 251・torchaudio 3・setuptools の exe 8 ≒ 270 檔）の全部か、実行時に読み込まれる物だけか | 読み込まれる物の実測が無い | 実測（Process Monitor 等） |
| B4 | **署名がスマート アプリ コントロールの遮断に実際に効くか**＝裁定 150 の観測は「未署名で遮断」だけで、署名済みとの対照が無い。Public Trust でも評判が要る可能性 | 未検分 | RTX 機で対照実験（署名した .pyd を読み込ませる） |
| B5 | ~~未標本の NVIDIA DLL の署名~~ → **実測済み（8-1-2・RTX 席・2026-09-14 15:xx）**＝未標本 13 のうち `zlibwapi` 以外の 12 は全て NVIDIA 署名済み。CUDA EULA 下の NVIDIA DLL 23 檔に未署名は無い。未署名の NVIDIA 由来は NVTX（`nvToolsExt64_1.dll` 48 KB・Apache-2.0 WITH LLVM-exception）の 1 本だけで、こちらが署名する側（B3 の集合）に入る | **解消**（cu126 と同じ問題は起きない） | — |
| B6 | 署名した後の検証＝sha256 が変わるので台帳（`ledger/*.json`）の pin と検証の仕組みを「署名後の値」に組み直す必要がある | 設計未着手 | 設計 |

### C. 置き場と形

| # | 不明瞭な点 | 現状 | 決め方 |
|---|---|---|---|
| C1 | 檔の分割単位＝torch wheel 1,867,405,006 B は 2 GiB 未満だが、実行系一式（2,038,518,046 B）を 1 檔にすると超える。wheel 単位か zip 分割か。GitHub AUP §9 の帯域は裁量 | 上限のみ確認 | 設計 |
| C2 | 「自前で組む」の形＝公式 wheel をそのまま束ねるか、展開済み `site-packages` を zip にするか。後者は Microsoft の「unmodified form」・BSD の表示保持・torch の LICENSE／NOTICE 同梱を保つ必要がある | 未決 | 設計＋卓 |
| C3 | Radeon 版と cu126 の扱い＝v3 で落とすのか、v2（初回取得）のまま並走させるのか。着地頁の「RTX（CUDA）」「Radeon（ROCm）」の 2 ダウンロード構成が変わる | 未決 | 卓 |
| C4 | モデルは初回取得のまま（148-2）＝初回に約 3.4 GB の取得は残る。憲章 §1 との関係は v2 と同じ | 方針どおり | — |
| C5 | 配布物の `licenses/` の増分＝NVIDIA 2 枚・zlib・Intel OpenMP・OpenSSL 系 5・PyPI 9・torch 連結 LICENSE・LGPL の源入手の申し出文＝`build/check-licenses.ps1` の突合表を作り直す | 一覧まで | 設計 |

### D. 調査の未了

| # | 未了 |
|---|---|
| D1 | R2（AMD）・R3（Microsoft と署名と置き場）・R5（PyPI）の敵対検証と批評席（枠切れ）。cu130 専用なら R2 は不要 |
| D2 | R1・R4 の JSON の行番号ずれ（19 件・5 件）の修正 |
| D3 | Artifact Signing の料金実額・Terms of Use（B2） |
| D4 | NVIDIA の PDF 版 EULA（検証席が取得可能を確認・228,421 B）の originals 保存 |
