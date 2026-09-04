# irodori-tts 読み分けちゃん専用化 調査報告 — 2026-09-04

> 席＝Fable（主席・判断と比較表）／サブ席＝opus 40 席（読解 11＝`01`〜`10`・`22`／実験 12＝`11`〜`15`・`20`・`21`・`23`・`29`〜`31`・`40`／敵対検分 4＝`16`〜`19`／報告検分 8／キット 2＝`26`・`32`／引き継ぎ 3＝`36`〜`38`）・ultracode ON。
> 依頼文＝`BRIEF.md`（正本 yomiwakechan2/docs/irodori-native-research-brief-2026-09-04.md）。本報告は**裁定の材料**であり正典ではない。製品コードは 1 行も書いていない。
> 根拠ノート＝`lab/notes/01`〜`38`・`40`。本体の暫定要求 8 項への埋め（引き継ぎ）は別檔 `report/irodori-native-handoff-2026-09-04.md`。各ノートが「檔:行」で一次資料を引く。本文の引用は `ノート番号 §節` で示し、要所は檔:行を直接添える。第 1 次読解の主張は敵対検分（`16`〜`19`）と報告検分を通した後の値に統一した。
>
> **用語**＝**便**＝実装の 1 単位（実装＋敵対検分＋書き戻し・BRIEF §4-6）。調査の回は「第 N 次」と呼び分ける／**RTF**＝合成時間÷音声長。**小さいほど速く、1 未満で実時間より速い**／**元栓**＝本体の一括起動面（エンジンの起動と環境変数注入を担う層）／**卓**＝司令官の裁定／**檔:行**＝ファイル:行番号／**札**＝その主張の確度（断定・未確認）／**⒜⒝⒞**＝三案。サイズは **MiB/GiB**（実バイト÷2^20/2^30）で統一（導入後ディスクの `du` 実測だけ MB 表記）。
>
> **引用の根**＝`upstream/<repo>/…`＝上流 clone（本調査フォルダ配下）／`local-additions/…`＝本調査フォルダ配下（稼働機に足した檔の写し）／yomiwakechan2 はリポ根 `C:/Users/mugonkun/source/repos/yomiwakechan2/`（`docs/`・`probe/`・`decisions.md` は根直下、**C# だけ `yomiwakechan2/` が 1 段挟まる**＝`yomiwakechan2/Core/…`・`yomiwakechan2/Engines/…`）／torch の行番号は稼働機 `.venv-rocm` の torch 2.13.0+rocm10.0.0 のもの。
>
> **停止域（BRIEF §5・5 項）**＝①上流 clone・`C:\IrodoriTTS\`・`C:\irodori-TTS-server\` の書き換え＝**なし**（各便で `git status` クリーン・`find -newermt` 0 件を確認。実験は `lab/` の別 venv）。②GPU 実験＝司令官の許可（「配信もしないし PC も触らない」2026-09-04）を得た第 2 次 b 以降のみ。8088 は**こちらが起こした個体をこちらで止めた**だけで、稼働個体を止めた便は無い（設営時から停止中）。**8088／7861 は現在も停止中＝配信前に `irodori-server-gpu.bat` を起こす必要がある**。③ライセンスの結論＝原文の檔と行を引き、矛盾は矛盾のまま（解釈は卓）。④yomiwakechan2＝読むだけ。⑤外部公開＝なし（HF・GitHub への push なし）。
>
> **前提の変更（BRIEF §2-3）**＝「NVIDIA 機での検証は無い」は便の途中で変わった。司令官が **RTX 3090 機**を提示し、自己完結キット 3 種（`21` kit-cuda・`26` onnx-e2e・`27` kit-ssd＝`N:\irodori-native-research\`）を用意し、**cu126・cu130・ONNX の CUDA EP／DirectML EP をすべて回収し、cu130 はローカル SSD で再走して基準値を採り、⒝の端から端までも両 EP で実測した**（`24`・`25`・`27`・`28`・§2-4）。A4 の実測は完了＝§2-3 を正式に改訂するかは卓の裁定（§0-2 の 8）。

---

## 0. 先に結論

| 問い | 答え | 理由 | 残る不確実性 |
|---|---|---|---|
| 門（ライセンスの鎖） | **閉じない**（Irodori の鎖 5 段も実行系も、再配布を禁じる条項は無い）。ただし通行料＝配布側が `licenses/` を 7 種そろえる | コード MIT・重み MIT 宣言・コーデック MIT 宣言。上流 facebook/dacvae は LICENSE 実体が公開初日から Apache-2.0・GitHub 本文も 2025-12-19 に Apache-2.0 へ修正済みで、HF カード本文だけ SAM 記述のまま（3 対 1・断定は卓）。CUDA EULA・cuDNN SLA・MSVC・DirectML MSLT・LGPL に明文の再配布許諾あり | 未記載 3 種＝Sony/SilentCipher 重みの条件（回避＝初回取得）／Aratako 3 repo に LICENSE 檔なし（回避＝MIT 全文を自作同梱）／HF 本文の SAM 記述（読みは卓）。MSVC「licensed Visual Studio users」の読みと LGPL §6 の選択も卓 |
| 推論の芯と ONNX | **「出せない段」は無い**。ニューラル段は全部 ONNX へ出て ORT で照合済み。C# に残るのは前段・ループ・トリム・リサンプル・pad | DiT 全 40 ステップの wav 差は int16 で 1 LSB・duration は bit 一致・DACVAE 復号 SNR 129 dB・透かしも iSTFT 手書きで ONNX 1 本（2.7 MiB）・前段は C# 231 行（実効）で id 完全一致 | GPU 実測＝DirectML fp32 で RTF 0.749（AMD iGPU・段合計・透かしは DML 未搭載＝今日組める形は透かしを CPU に出して 0.827・§2-4）＝実時間には届くが ROCm torch bf16（0.197）の 3.8 分の 1。3090（実測・`28`）＝DirectML 0.527（透かし抜き・込みで ≈0.6）／CUDA EP 0.291。fp16 の DiT は GPU EP 全般で壊れる（原因未特定）。INT8 未実施 |
| 感情制御 | サーバは 43 欄中 42 欄を受ける（未露出は `speaker_uncond_mode` 1 欄）。**未露出は本体アダプタ側の裁定 6**。感情＝caption 自由文＋本文中の絵文字 45 種＋`cfg_scale_caption`。数値の感情軸は無い | AST 差集合で機械照合・TestClient と実サーバの実射で 12 契約を確定（`no_ref=true` なら voice 不要・綴り違いは沈黙・`X-Irodori-Seed` は複数チャンクで再現不能 等） | チャンク分割込みの seed 決定性・視聴者絵文字の暴発の程度（実装便） |
| GPU 指定 | **コード上は Server の環境変数だけで `cuda:N`・CPU 強制が通る**（上流無改変）。1 プロセス 1 デバイス。ROCm 分岐は 0 行 | 関門は `inference_runtime.py:55-77` の 1 箇所（`:62` で index 保持・範囲検査なし）。本体側は「推論デバイスを選ぶ」前例ゼロだが「デバイス名→添字の再解決」「エンジン device の表示」の器は既存 | 多 GPU 実機での `cuda:N` の実射は未了（本機・3090 機とも 1 GPU）＝U-3。範囲外・綴り違い・可視化変数・model/codec 分割・掴んだ GPU の確認手段は司令官機で実射済み（`29`・§10-2） |
| 追加＝python なし配布・複数 GPU・参照音声（§10） | **python なし配布は⒜で実射済みに成立**（埋め込み 3.12.10＋`._pth`＋Server 起動 `/health` 200）。**複数 GPU 指定は可能**（Server 経由・model/codec の 2 env・`PRELOAD=true`）。参照音声 10〜30 s は**落ちるが小さく、GPU なら RTF 0.5 を割らない**（3090 bf16 で +0〜13 %・最悪 RTF 0.34、fp32 0.39・ROCm 0.387。参照潜在の事前計算で増分は消える） | 条件＝vc_redist・絶対パスの `voices_dir`・`dist-info` 同梱・パスは `._pth`／設定は env。範囲外 index は `.to()` で落ちる（PRELOAD で起動時か 500 か）。GPU の番号は CUDA／nvidia-smi／DXGI で体系が違い再起動で変わりうる＝安定鍵は UUID／PCI | 2 枚機の実挙動（U-3）。参照音声の耳検分は済み（司令官「非常に似ている」・`35`）。CUDA ビルドの `uuid`／`pci_bus_id` と `nvidia-smi` の所在は 3090 で確認済み |
| 導入の重さ | Windows の CPU 転落は**上流 pyproject の `sys_platform == 'linux'` マーカー**が原因。埋め込み配布で 21 操作中 16 が消え、残るは 6 操作（ドライバ確認・空き容量・VC++ 再頒布・動作確認・停止・CPU 復帰。vc_redist をインストーラが黙って通せれば 5） | 配布物＝CUDA cu130 で ≈5.4 GiB（モデル 3.3 GiB 込み）／CPU ≈3.7 GiB。torchcodec は外せるが上流パッチ 2 箇所が条件。起動直後・初見形状のスパイク（最悪 40 秒級）は残る | 配布経路（実行系 ≈2.1 GiB は GitHub Releases の資産上限超）・不要 ≈500 MiB を外した embed 配置の起動確認・スパイクの根治 |
| 三案の推奨 | **推奨＝⒜（CUDA のみ・Server 土台・python-embed）＋⒞の要素を本体アダプタに内包（計 6 便）。退路＝⒞単独（3 便）**。⒝は「技術的に成立」が実証された次期候補（8 便＋2）だが、DirectML の実測が AMD iGPU 0.749・RTX 3090 0.527（透かし抜き）とも判断則（40 steps で RTF ≤ 0.5）に届かず**保留が両機で確定**。復活条件は §6-3 | 感情と GPU は上流無改変・Core 不触で本体側の裁定改訂だけで満たせる。初心者導入の主因（torch の選択を利用者に委ねる構造）は「配布側が torch を固定する」以外に解けない＝⒜か⒝。⒜は 3090 でも RTF 0.21（bf16・短文・cu126/cu130 とも SSD 実測で同値）と裏づけられた。⒝は DirectML の速度で⒜（torch）に負けている今は採れない | ⒜＝cu126/cu130 の選択（両方 3090 の SSD で再走し同値＝速度では決まらず、配布サイズとドライバ要件の裁定）。⒝＝端から端までは両機で実測済み（`28`）。残るは fp16 の DiT が GPU EP 全般で壊れる原因と、透かしの DFT 抜き再 export（DirectML では透かしが載らない）。**耳検分（司令官・9 対・`35`）＝bf16／fp32・10／40 steps・DirectML／torch・ROCm／CUDA／CPU のどれも差が聞き取れず**＝10 steps を配信品質と認めるなら⒝の DirectML は両機で RTF 0.5 未満（AMD 0.28・3090 0.24・透かし CPU 込みでも ≈0.36／0.30）＝判断則の steps は卓が決める（§10-4 の 14） |
| CUDA のみ vs ROCm も | **「CUDA のみで出し、ROCm は司令官機だけの手動手順」は成立する** | 理由は配布物サイズではなく（ROCm wheel DL 1.1〜1.3 GiB ＜ CUDA 1.7 GiB）、「非公式・無保証」でもない（司令官機の構成は AMD 公式 doc の Windows 手順そのもの）。正しい理由＝利用者比 72.9% vs 18.7%・GPU 機種別パッケージと Python 版・index の分岐・検証と保守の二重化・ROCm wheel はライセンスの裏取りが取れない | ⒝（DirectML）だけがこの分岐を消す＝⒝の最大の存在理由 |

### 0-2 卓が決めること（決めないと止まる便）

1. **コーデック重み（④）の扱い**＝初回取得（推奨・A2 便がこの前提）か同梱か。ライセンス上はどちらも可・差はサイズと導線。→ A2
2. **facebook/dacvae-watermarked の HF 本文「SAM License」の読み**＝Apache-2.0（3 対 1）と読むか。SAM 説を採る場合は §1.b.iv が⒝のコーデック変換に触れうる。→ ⒝
3. **透かし（SilentCipher）を既定で残すか切るか**＝ライセンス上の義務は無い（§1-4）。残す＝現状（GPU では 32 ms・DL 65 MiB）。切る＝製品姿勢の問題。⒝では facebook 由来の透かし枝が成果物から落ちる。→ A1／⒝
4. **既決の改訂**＝裁定 6（露出 4 本）→ C1、裁定 16（既定パス候補なし）→ A3、「GPU は本体管轄外」（`docs/irodori-tts-survey.md:255-257`）→ A3、「本体はバックエンド非依存」（`docs/irodori-tts-requests.md:105-106`）→ ⒝のみ（設計原則の変更＝他 8 エンジンへの波及）。
5. **推奨の採否**＝⒜＋⒞は今裁定できる（§6-3）。⒝は判断則（§6-3）で GPU 実測後に。
6. **cu126 か cu130 か**＝結果は出た（`24`・`25`・`27`）＝**cu130 既定・cu126 は古いドライバ層向けの手動手順**を今裁定できる。→ A1
7. **法解釈 3 点**＝MSVC 再頒布の「licensed Visual Studio users」に Community 版が含まれるか／LGPL-2.1 §6 の a〜e のどれで満たすか／DirectML MSLT（Windows 限定・通知改変禁止）の受容。→ A1／⒝
8. **BRIEF §2-3 の維持か改訂か**＝3090 機での CUDA 実測（A4）は実施済み（`24`・`25`・`27`・SSD なら全段 6.2 分）。**改訂を推奨**（費用 0・A1 の版選択と U-7 の前提は実測で埋まった）→ **先頭便＝A1**（cu130 既定・並行して C1/C2 を先行可）。維持する場合も A1 の中身は同じ（版の実射結果は既に手元）。

**裁定の記録（2026-09-04・司令官）＝「ROCm は未保障。ただし Strix Halo（gfx1151）のみ確認済みとして、使えるパスは消さない」。** 形は **② 別途「Radeon 用」としてリリース**（同日の追加裁定＝「Radeon 版は独立にする。bf16 固定で、起動時にできるだけウォームアップを実施して本体からの読み上げに備える」。①同一配布物は採らない）。暖機の設計材料（初見形状の罰の粒度・`seconds` 段階化・MIOpen 保存の持続・暖機の所要）は `40` で実射。配布の既定は CUDA、対応表・自動判定（§10-5 の道具）は持たず、保証は「gfx1151（Ryzen AI MAX+ 395／8060S）で確認済み」の事実だけを書く。§10-4 の 9〜12（GPU の同定・可視化・PRELOAD・fp32 転落）は NVIDIA 前提で読み、① を採る場合だけ可視化変数に `HIP_VISIBLE_DEVICES` の分岐が残る。

---

## 1. ライセンスの鎖（BRIEF §2-2・§4-1）

根拠＝`09`（一次取得 2026-09-04）、検分＝`16`（12 主張中 11 CONFIRMED・日付 1 点訂正）、実行系＝`22`。

### 1-1 Irodori の鎖 5 段（＋周辺 3 段）

| # | 対象 | メタデータ | 本文 | LICENSE 檔 | 再配布 | 義務 | 札 |
|---|---|---|---|---|---|---|---|
| ① | GitHub Aratako/Irodori-TTS（コード） | MIT | README:552-554 | あり（`upstream/Irodori-TTS/LICENSE`・Copyright (c) 2026 Aratako） | 可 | 著作権表示＋全文同梱 | 断定 |
| ② | GitHub Aratako/Irodori-TTS-Server（コード） | MIT | README:605 | あり（同文） | 可 | 同上 | 断定 |
| ③ | HF Aratako/Irodori-TTS-v4-Small／v4.1-Small（重み 3,064,295,596 B＝2.85 GiB） | `license: mit`（README:2） | 「This model is released under MIT」（v4:187／v4.1:73・当該節 sha256 一致） | **なし**（404） | 可 | MIT 全文を**配布側が用意**／Ethical Restrictions 4 条は「license terms に加えて」 | 断定 |
| ④ | HF Aratako/Semantic-DACVAE-Japanese-32dim（コーデック 429,620,065 B＝410 MiB） | `license: mit` | README:29「License: MIT」／README:24,112「Derived from facebook/dacvae-watermarked」 | **なし** | 可 | 上流帰属 | 断定 |
| ⑤ | HF facebook/dacvae-watermarked（④の由来） | `license: apache-2.0`（README:2） | README:46「licensed under the SAM License」 | **なし**（404） | Apache-2.0 と読めば可 | NOTICE 相当 | **本文だけ不一致（§1-2）** |
| ⑥ | GitHub facebookresearch/dacvae（⑤を読むコード） | Apache-2.0 | README:67「licensed under Apache-2.0」 | **あり**（11,358 B・5 リビジョンで sha256 不変） | 可 | LICENSE 同梱。**wheel には入らない（実証・`16` §2-7）**＝`setup.py:28` `license_files=("LICENSE.txt",)` の綻び | 断定 |
| ⑦ | silentcipher コード（pin SesameAILabs@d46d7d0＝sony の fork） | MIT | HF README:135「The **code** … MIT」 | あり（Copyright (c) 2024 Sony Research Inc.・本家とバイト一致） | 可 | 著作権表示 | 断定 |
| ⑦' | HF Sony/SilentCipher（透かし**重み** 65 MiB・推論に要るのは enc_c＋dec_c 2.1 MiB） | **無し**（cardData null） | 重みへの言及なし（GitHub README・Releases にも無し） | **なし** | **不明** | — | **未確認** |
| ⑧ | HF sbintuitions/modernbert-ja-310m（テキスト符号器・重みに焼き込み済） | mit | README:196 | あり（1,070 B・Copyright (c) 2025 SB Intuitions） | 可 | 著作権表示 | 断定 |

### 1-2 ⑤の「矛盾」の解き方（観測事実・解釈は卓）

| 日時 UTC（**committer date＝公開日**） | 場所 | commit | 中身 |
|---|---|---|---|
| 2025-10-03 13:15 | GitHub dacvae | — | repo 作成（空） |
| 2025-10-03 14:14 | HF dacvae-watermarked | `07fe694` | README＝frontmatter のみ（`license: apache-2.0`）。以後 weights.pth を 3 回 upload |
| 2025-11-19 07:07 | GitHub sam3 | `a13e358` | 「SAM License」檔が初めて公開 |
| **2025-12-10 16:53** | **GitHub dacvae** | **`d39b77d` Initial Commit**（author date は 10-03 13:36） | README:43「SAM License」／**同 commit で LICENSE 檔＝Apache-2.0 全文**（以後不変） |
| 2025-12-16 15:54 | HF | `9ca52e8` | GitHub 当時の README で上書き＝frontmatter 消失・本文に「SAM License」 |
| **2025-12-19 13:20** | **GitHub** | **`34e0b0d` "Update license in README"** | README を SAM→Apache-2.0 に修正（差分は README.md の +1/−1 のみ） |
| 2025-12-19 17:25 | HF | `8680102` | frontmatter `license: apache-2.0` 復活・**本文 46 行目は未修正**（現在に至る） |

→ 「メタデータ＝Apache-2.0」「LICENSE 実体＝Apache-2.0」「GitHub 本文＝Apache-2.0」の 3 対 1。前回調査の「自己矛盾」は「HF カード本文の写し忘れ」と局在化できる（断定は卓）。
→ 最悪解釈（SAM License）でも §1.b.i「Agreement の写しを添えれば配布可」＝禁止ではない。ただし §1.b.iv「reverse engineer, decompile or discover the underlying components」は⒝のコーデック ONNX 変換に触れうる。公開順は SAM License（11-19）→ GitHub 版 dacvae（12-10）なので「時系列上ありえない」とは言えない（`16` §2-9）。
→ ⒝に有利な材料＝ONNX 化すると `facebook/dacvae-watermarked` 由来の透かし枝（9,327,713 パラメータ＝35.6 MiB）が成果物から**完全に落ちる**（残るのは最終ヘッド 770 パラメータ・`12` §4-1）。

### 1-3 実行系の再配布条件（`22`＝⒜の配布物のうち実行系 ≈2.1 GiB の 8 割はここ）

| 対象 | ライセンス | 再配布条項（逐語は `22`） | 義務 | 札 |
|---|---|---|---|---|
| torch 本体 | BSD-3 本文＋多数の著作権表示（`License-Expression` は 6 ライセンスの AND） | 「Redistributions in binary form must reproduce the above copyright notice…」 | LICENSE＋`third_party/` 93 檔を添える | 可・断定 |
| **torch が同梱する NVIDIA DLL 26 個** | **wheel の中で扱われていない**（`License-File:` 94 件に CUDA EULA も cuDNN SLA も無い） | — | **CUDA EULA と cuDNN SLA の 2 枚を配布側が添える**（PyTorch は肩代わりしていない） | 可だが義務は配布側に・断定 |
| CUDA Toolkit（cudart/cublas/cublasLt/cufft/cusparse/cusolver/curand/nvrtc/nvJitLink/cupti/nvToolsExt） | NVIDIA EULA | Attachment A「The following CUDA Toolkit files may be distributed with applications developed by you…」＋ §1.1.2 の 4 条件（自作機能・アプリ専用アクセス・条件の一貫性…） | アプリ専用フォルダに置く。**GPL 汚染禁止条項**（FFmpeg GPL 版と両立しない） | 可・断定。`nvperf_host.dll`・`zlibwapi.dll`・`nvrtc64_130_0.alt.dll` は Attachment A に列挙が無く**未確認** |
| cuDNN（cudnn*_9.dll 10 個） | NVIDIA SDK SLA＋cuDNN Supplement | §2「the runtime files .so and .dll」が distributable | 別 SLA＝CUDA EULA では配れない | 可・断定 |
| **MSVC 再頒布**（`msvcp140.dll`） | Microsoft Software License Terms（VS）の Distributable Code | 「Distribution … is limited to licensed Visual Studio users」「you may copy and distribute with your program … [VisualStudioFolder]\VC\redist … You may not modify these files」 | **`torch/lib/c10.dll` が MSVCP140.dll を要求（PE 直読）。python-embed は vcruntime140/_1 を同梱するが msvcp140 は無い＝`vc_redist.x64.exe`（≈25 MiB）が別途要る** | 可・「licensed Visual Studio users」に Community 版が含まれるかは**卓** |
| python-3.12.10-embed | PSF License（同梱 LICENSE.txt） | §2 の再配布許諾。公式 docs「intended for acting as part of another application … vendoring」 | LICENSE.txt をそのまま添える | 可・断定（⒜は python.org の推奨用法） |
| **DirectML.dll**（onnxruntime-directml に同梱 17.7 MiB） | **MIT ではない**＝Microsoft Software License Terms（DirectML） | §1(a)「You may copy and distribute the software … in applications … that run on Windows and Xbox」 | **Windows/Xbox 限定・通知改変禁止・単体配布禁止**。ORT の `ThirdPartyNotices.txt`（323 KiB）に DirectML の記載は 0 件＝**MSLT を配布側が添える** | 可・断定（⒝の「24 MiB で軽い」には注釈が要る） |
| libsndfile（soundfile が同梱） | LGPL-2.1（wheel に COPYING と license_notes.md 同梱） | §6 の通知＋全文＋a〜e のいずれか | soundfile は upstream 4 箇所で import＝**落とせない**。⒜は同梱 DLL なので c)「3 年間の書面申し出」か d)「同じ場所でソース提供」が自然 | 可・a〜e の選択は**卓**。⒝で音声 I/O を C# に移せば義務ごと消える |
| transformers／sentencepiece／tokenizers／onnx | Apache-2.0 | §4(a) 本文 1 部。§4(d) NOTICE 義務は **onnx だけ**（NOTICE 2,133 B あり） | — | 可・断定 |
| **AMD ROCm Windows wheel** | **wheel から特定不能**（`rocm_sdk_core` METADATA は 59 B・3 行・License 欄なし） | — | 同梱するなら AMD の一次資料を別途探す必要 | **未確認**＝CUDA のみ案ならこの穴は開かない |

**licenses/ の実作業**（`22` の一覧）＝⒜は外部取得 5 種（CUDA EULA・cuDNN SLA・vc_redist・Apache-2.0・onnx NOTICE）＋自作 2 種（LGPL ソース申し出文・Irodori 系 3 つの MIT 文）。⒝は外部取得 2〜3 種（DirectML MSLT・vc_redist・Apache）＋自作 1 種。大半は wheel 内檔のコピーで半便には届かないが、「1 手」では収まらない。

### 1-4 同梱できるもの／利用者に取得させるもの（材料）

| 物 | 大きさ | 案 | 理由 |
|---|---|---|---|
| ①②⑥⑦ コード | 数 MiB | **同梱**（`licenses/` に各 LICENSE を写す。dacvae は GitHub から） | 争点なし |
| ③ 重み v4.1-Small＋tokenizer/ | 2.85 GiB | **初回取得を推奨**（A2 便がこの前提。同梱に倒しても license 上は可＝差は配布物 +2.85 GiB と導線だけ） | 上流は重みだけ取る（`inference_runtime.py:559` allow_patterns）うえ HF repo に LICENSE 檔が無い＝MIT 全文を自作 |
| ④ コーデック重み | 410 MiB | **初回取得を推奨**（再配布しない） | 卓の裁定が要る唯一の実質論点（§0-2 の 1） |
| ⑦' 透かし重み | 65 MiB（`get_model()` が自動取得） | **初回取得** | 原文に重みの配布条件が無い |
| ⑧ ModernBERT | — | **取得不要**（checkpoint に焼き込み・tokenizer も checkpoint 側） | HF キャッシュに modernbert repo は無い |
| torch・CUDA・cuDNN・MSVC・libsndfile | GiB 級 | 同梱（§1-3 の義務つき） | — |
| FFmpeg DLL | — | **同梱しない**（torchcodec ごと外す＝§5-2 の条件つき。wav/flac/pcm は soundfile で完結） | LGPL/GPL の義務・CUDA EULA の GPL 汚染禁止と衝突 |

### 1-5 Ethical Restrictions・透かし（前回調査の訂正 1 点）

- 4 条は v4／v4.1 で逐語同一。**配信・商用・生成音声の公開を禁じる条項は無い**（3 カードとも `commercial`/`redistribut`/`prohibit` の語が 0 件）。効くのは 1（実在人物のなりすまし禁止＝参照音声欄に但し書き）と 3（偶然の類似の免責＝製品説明に転記）。4「Users are solely responsible」は配布物に転記。
- **訂正**：前回「透かしが常時埋め込まれる」→正しくは「**依存と重みが揃っているときだけ**」（README:296／`watermark.py:34-50` は import 失敗・ロード失敗を warning で握って続行）。payload は固定 `"IRDTS"`。**モデルカードに「透かしを保持せよ」に相当する義務表現は無い**（32dim カード README:85-87 は外し方を公式に案内）。
- 透かしを切るか残すかの材料（卓・§0-2 の 3）：

| | 残す（現状） | 切る |
|---|---|---|
| 所要 | GPU bf16 で 32 ms／CPU で 264〜331 ms | −0.03〜−0.3 s |
| 配布・取得 | Sony 重み 65 MiB（ライセンス未記載＝初回取得） | 取得不要 |
| ⒝での扱い | 透かし全経路を ONNX 2.7 MiB で再現済み（BN 学習モードの再現で SNR 118.8 dB） | 段が 1 つ減る |
| 製品姿勢 | v4 README:32 "promoting responsible AI usage" と整合 | 上流の設計意図から外れる（ライセンス違反ではない） |

---

## 2. 推論の芯と ONNX（BRIEF §4-2）

根拠＝`03`・`04`（読解）、`12`・`13`・`15`（lab 実証）、`11`・`20`（基準測定）、検分＝`18`。

### 2-1 段構成（`inference_runtime.py:1036` `synthesize` の実行順）

| # | 段 | 実装 | 檔:行 | パラメータ |
|---|---|---|---|---|
| 0 | テキスト正規化 | `normalize_text`（辞書 10・正規表現 4・括弧剥がし・NFKC・`...`→`…`）。**G2P・音素化は無い** | `text_normalization.py:60-74` | — |
| 1 | トークン化 | HF `tokenizers` Unigram・vocab 102400・byte_fallback・checkpoint 同梱 `tokenizer/`（6.4 MiB）。BOS 手付け・EOS なし・256/512 固定パディング | `tokenizer.py:71-79,111-136` | — |
| 2 | 参照音声→潜在（参照 wav 時のみ） | DACVAE encoder（deterministic mean） | `codec.py:177,249-251` | 27.3M |
| 3 | 条件符号化 | 共有 **ModernBERT-ja-310m**（25 層・重み同梱）＋射影器 2 本＋`ReferenceLatentEncoder`（8 層）。**1 合成につき 2 回走る**（`inference_runtime.py:1281` と `rf.py:220`＝上流の無駄・CPU で 0.95 s/回） | `model.py:1720,675,803,880` | 315M＋3.4M＋60.5M |
| 4 | duration 予測 | `DurationPredictor`（caption も入る） | `model.py:985,2045`／`duration.py:105` | 21.8M |
| 5 | 条件 KV 射影（1 回） | `build_context_kv_cache`（純粋な速度最適化・数値不変） | `model.py:2021` | — |
| 6 | **RF Euler サンプリング** | `sample_euler_rf_cfg`：**純 Python の for ループ**（`rf.py:459`）・早期停止なし。1 ステップ＝12×`DiffusionBlock`（SDPA・**複素数 RoPE**・half-RoPE・LowRankAdaLN・SwiGLU） | `rf.py:116-582`／`model.py:30-42,247-250,924` | **358.4M** |
| 7-8 | unpatch・末尾トリム | `find_flattening_point`（Python ループ） | `inference_runtime.py:150-184` | — |
| 9 | コーデック復号 | DACVAE decoder（Conv1d／ConvTranspose1d・weight_norm・Snake・Tanh）。48 kHz・hop 1920・潜在 32 次元。**`alpha=0.0` だけでは 96ch のまま返る＝`_watermark_passthrough` の差し替えが最終ヘッド供給に必須**（`dacvae.py:455-456`・`326-332`） | `codec.py:82-96,256` | 65.3M（実行経路） |
| 10 | 透かし | SilentCipher 44.1k：`torch.stft`→enc_c/dec_c（Conv2d・**BatchNorm が学習モードのまま**＝`server.py:461-466` に `.eval()` 無し）→`torch.istft`、48k↔44.1k リサンプル×2（往復 1.7 ms） | `watermark.py:79`／silentcipher `stft.py:22,36`・`server.py:243-366` | 0.54M |

- 諸元（safetensors ヘッダ直読・v4-Small と v4.1-Small で完全同一）：**766,052,385 パラメータ・全部 F32**・2.85 GiB。内訳＝DiT 46.8%／ModernBERT 41.1%／speaker encoder 7.9%／duration 2.8%。
- 精度＝既定 fp32。bf16 は `cuda`/`xpu` 限定（`inference_runtime.py:310`）。fp16 という選択肢は無い。
- CFG independent（既定）＝各条件の uncond 枝をバッチ方向に連結して 1 回で流す（`rf.py:467-483`）。**参照ありで 4 倍・`--no-ref` では 3 倍**。CFG が入るのは `cfg_min_t=0.5` 以上の半分のステップ。

### 2-2 ONNX 化の段別判定（lab 実証・torch 2.10 dynamo exporter・ORT 1.29 CPU）

| 段 | 判定 | 実証（`lab/onnx/`） | 手当て（檔:行） |
|---|---|---|---|
| 0 正規化 | **C# 逐語移植・完了** | `15`：実効 82 行・77 件一致。**NFKC は 68,218 コードポイントで Python と 0 差**。U+FFFE と孤立サロゲートで .NET が例外＝衛生処理 1 行 | 順序依存（`…`→`...` の戻し）を逐語で写す |
| 1 トークナイザ | **C# 自前 Viterbi・完了** | `15`：実効 149 行（物理 171 行）・**108,430 件すべてで id 一致（不一致 0 件）**。Microsoft.ML.Tokenizers は安定版 2.0.0 でも使えるが **Unigram の byte_fallback id が +255 ずれるバグ**（未報告）＝変換 `.model` の byte 片を NORMAL 型で書く回避が要る。`tokenizer.json` 直読み API は main のみで未リリース | 起動 13.1 s・RSS 555 MB（`import transformers`）が C# では 71 ms・+15 MB |
| 2 DACVAE encoder | **出た**（105 MiB fp32） | `12`：max abs 3.96e-5・SNR 109.7 dB・動的長可 | weight_norm fold（31 個・bit 一致）・pad はグラフ外 |
| 3 条件符号化（ModernBERT×2＋射影器＋参照 encoder） | **丸ごと 1 グラフで出た**（1.42 GiB） | `13`：差 1.9e-6・ORT 0.618 s vs torch 0.951 s（CPU） | `attn_implementation="sdpa"` 既定のまま。参照 encoder は別グラフ（S_ref 動的 2〜1024）にも切れる |
| 4 duration | **出た・bit 一致**（83 MiB） | `13`：差 0.0・ORT 9.7 ms vs torch 30.4 ms | **`_safe_attention_mask`（`model.py:440` `if bool(has_any.all()):`）の書き換えが必須＝export が落ちる**。ORT CPU に bool `Where` が無い→Or/And/Not で |
| 6a DiT 1 ステップ | **出た**（1.37 GiB fp32・2031 ノード・opset 20） | `13`：1 ステップ差 2〜7e-6・**動的軸 B／S_lat／S_spk（751 まで）すべて実効**。ORT CPU は torch 同形より 1.5×速く、KV キャッシュ無しで torch の KV 有りに並ぶ | **複素数 RoPE の実数化（cos/sin）は torch 出力 bit 一致**（monkeypatch 2 関数・重み不変）。half-RoPE は構造上自動的に正しい |
| 6 ループ・CFG・Euler | **C# 側 35 行** | `13` §6：写経ループ＋ORT ステップで **40 ステップの最終潜在差 3.6e-5・復号 wav 差 1.16e-5・相関 0.999999999997・int16 で最大 1 LSB** | 乱数はグラフ外＝seed の扱いは C# の自由（Python 版とのバイト一致は約束しない） |
| 8 末尾トリム・duration→frames・14 次元特徴 | **C# 側 ≈30 行** | — | `inference_runtime.py:150-184,1310-1316`・`duration.py:105` |
| 9 DACVAE decoder | **出た**（250 MiB fp32） | `12`：差 4.77e-7・SNR 128.7 dB・動的 B/T 可 | weight_norm fold・差し替え後ヘッドで export。**ORT CPU は torch より 1.56 倍遅い**（Snake の `Sin` が 29%） |
| 10 SilentCipher | **透かし全経路が ONNX 1 本で出た**（2.7 MiB） | `12` §5：upstream `encode_wav` と SNR 118.8 dB・`decode_wav` で "IRDTS" confidence 1.0。`torch.istft` は DFT op で出るが ORT が int32 ScatterND を拒否→表ゼロの手書き iSTFT（38 KiB・torch と bit 一致）で解消 | BN をバッチ統計版に置換（忠実）。C# に残るのは 48k↔44.1k リサンプル・pad・payload の one-hot 定数表 |

**結論（断定）＝「出せない段」は無い。** 第 1 次読解の「SilentCipher は出せない」は torch 2.10 では成立しない。⒝の境界は「ニューラル段は全部 ONNX／C# は前段・ループ・トリム・リサンプル・pad・音声 I/O」で確定した。

- 3 グラフ（DiT＋duration＋条件符号化）の fp32 合計＝**2.87 GiB（safetensors 比 +0.57%）**、後段（decoder＋透かし）＝253 MiB（fp16 128 MiB）。ONNX 化してもモデルの大きさは実質変わらない。
- **fp16 は CPU では禁じ手**（DiT で 9.2 倍遅い・`op_block_list` は効かない・wav rel L2 0.9%）で、**DirectML では数値が壊れる**（§2-4）。サイズ半減は CUDA EP で検証できるまで見込みに入れない。INT8 は未実施。
- 量子化（torchao）は ONNX へ持ち越せない＝⒜と⒝で使える軽量化の手が違う。上流に ONNX 関連コードは 0 行＝⒝は全て自前。

### 2-3 CPU 基準と ORT の速度（lab・fp32・OMP=8）

| 条件 | torch | ORT CPU | 備考 |
|---|---|---|---|
| 短文 3.76 s・40 steps・全体（`11` §5） | 15.26 s（**RTF 4.06**）＝sample_rf 84%＋duration 1.1 s＋decode 1.0 s＋watermark 0.3 s | — | モデル読込 11.3 s・RSS 6.3〜7.1 GB |
| DiT 40 ステップ全ループ | 13.10 s | **10.86 s（1.21×）** | 1 ステップ B=1：0.218→0.145 s |
| 条件符号化 | 0.951 s | 0.618 s | 2 回走る無駄を 1 回化すると −0.95 s |
| DACVAE decode | 778 ms | **1,215 ms（1.56× 遅い）** | Snake |
| 透かし | 264 ms | 331 ms（1.26× 遅い） | dec_c が 86% |
| ⒝合計の見立て（CPU） | — | **RTF ≈3.2〜3.5** | **CPU ではリアルタイム未達＝⒜⒝⒞共通** |

### 2-4 GPU 実測

**ROCm・torch（この機体・Radeon 8060S・`20`）**＝常駐・ウォーム・bf16 の列だけを RTF 軸に使う（CLI 1 発の値は毎回コールドで 0.8〜5.6 になる）。

| 条件 | bf16 ウォーム | fp32 ウォーム | CPU 比（bf16） | 段別（bf16・短文 40） |
|---|---|---|---|---|
| 短文 3.76 s・40 steps | **0.749 s／RTF 0.197** | 3.789 s／1.008 | 20.4 倍速 | duration 34 ms／**sample_rf 626 ms（84%）**／decode 55 ms／watermark 32 ms |
| 短文・10 steps | 0.309 s／0.081 | — | 21.8 倍 | — |
| 長文 10.92 s・40 steps | **1.729 s／RTF 0.158** | 9.845 s／0.902 | 23.3 倍 | — |
| 実サーバ 8088（v4-Small・bf16・5 連射定常） | **0.75 s／RTF 0.229**（HTTP 込み） | — | probe の 0.77 s と一致 | — |
| VRAM ピーク | **bf16 3.08 GB（音声長に依らず一定）** | fp32 4.5〜5.9 GB | — | 統合メモリ機の値＝dGPU では未確認 |

- **fp32 は ROCm で `decode_latent` が病的**（ウォーム 1.98 s＝CPU より遅い・MIOpen が workspace 0 で遅い solver に落ちる警告）＝**ROCm では bf16 は必須**。⒜⒞の既定は bf16 固定。CUDA で同じかは 3090 キットで確定。
- **未見の音声長は初回 3〜6 倍遅い**（プロセス内・`IRODORI_EMPTY_CACHE_INTERVAL=0` でも消えない）。MIOpen のカーネル db（`~/.miopen`・2.5 MB）はディスク持続するが、アルゴリズム選択はプロセス内のみ＝**常駐サーバ必須・毎回プロセスを起こす設計は不可**。
- **⒝の投資先は sample_rf 一点**（GPU では decode・duration・watermark が各 30〜55 ms）。
- SSE（`first_sentence_chunk_min_chars=1`）の**初音 0.72〜1.09 s**、先頭チャンクが 2.84 秒ぶんの音声を返すので次チャンク（+0.93 s）まで途切れない。合計は 0.4 s 遅く、喋りが 1.36 秒伸びる（各チャンクが独立に長さ予測）＝耳検分は未測。

**DirectML・ONNX（この機体・Radeon 8060S・onnxruntime-directml 1.24.4・`23`）**＝⒝の GPU 数値。条件は ROCm 行と同一（短文 3.76 s・no_ref・seed 1234・常駐ウォーム）。

| 構成 | 40 steps | RTF | 10 steps | RTF |
|---|---|---|---|---|
| **⒝ DirectML fp32（session を形状ごとに 2 本）** | **2.815 s** | **0.749** | **1.036 s** | **0.276** |
| ⒝ ORT CPU EP | 12.31 s | 3.27 | 4.64 s | 1.24 |
| （参考）ROCm torch bf16 常駐 | 0.749 s | 0.197 | 0.309 s | 0.081 |
| （参考）CPU torch | 15.26 s | 4.06 | 6.73 s | 1.79 |

- 段別（DML／CPU EP）＝条件符号化 209.5／570 ms・duration 2.2／7.1 ms・**DiT 40 steps 2,388／10,295 ms**・decode 178／1,102 ms・透かし部品 ≈37／≈260 ms。**GPU に載せても音は変わらない**（40 steps の wav 差 2.31e-5・int16 で最大 1 LSB）。
- **⒝＋DirectML はリアルタイムに届く（RTF 0.749）が、ROCm torch bf16 の 3.8 分の 1**。10 steps なら RTF 0.276。⒝を「速いから選ぶ」根拠にはまだならない。
- **fp16 は DirectML で数値が壊れる**（DiT の出力振幅が 1/60・wav 相関 0.161・NaN なし・`op_block_list` でも直らない）＝**⒝の本命は fp32**。直れば RTF 0.520／10 steps 0.214 の計算（原因 op の特定は 1 便・U-5）。
- **DirectML 固有の手当て 3 つ**（ビルド時 1 回・実行時コスト 0）＝(a) exporter が撒く `Reshape` の `allowzero=1` を剥がす（剥がさないと条件符号化が DML に一切載らない）、(b) 1 次元 `ConvTranspose` を 2 次元へ挟み替える（DACVAE 復号）、(c) **バッチ形状ごとに session を分ける**（同一 session で B=3 の後の B=1 が 3.6 倍遅くなって戻らない＝分けると 40 steps が 4.49→2.39 s）。
- **透かしグラフは onnxruntime-directml 1.24.4 でロード不可**（`DFT(inverse, onesided)` の属性を拒む。同じ檔が CPU 版 1.29.0 では通る＝**ORT 1.29 と DirectML 1.24 の版ずれが初めて実害として出た**）。回避＝iSTFT を DFT 以外で組み直す（部品は DML で ≈37 ms）か透かしだけ C#/CPU（+331 ms・RTF 0.827）。
- **DirectML は同時 Run が安全でない**（同一 session でも別 session でも 6 走中 5 走で例外・3 走で segfault）＝⒝のサーバは合成を必ず直列化。ORT docs の「別 session なら同時可」は本機で不成立。
- **初回費用は ROCm より桁で軽い**（DiT ループ 2.65→2.39 s＝1.11 倍・端から端まで +1.1 s）＝⒜⒞に必須の「起動時ウォームアップ射」が⒝では 1 射で足りる。GPU メモリ＝DiT 常駐で +1.6 GB（全グラフ同時常駐は未測・単純合計 ≈3.3 GB）。

**CUDA・torch（RTX 3090・ドライバ 591.86・torch 2.10.0・`27`＝cu130 をローカル SSD で再走＝基準値・`24`＝cu126・`25`＝cu130 の N:\ 直走行）**＝司令官がキットを 3 回実行。条件は ROCm 行と同一・常駐ウォーム。

| 条件 | **cu130 bf16（SSD・`27`）** | cu130 fp32（SSD） | cu126 bf16（SSD・`27` 追記） | cu126 fp32（SSD） | VRAM（bf16／fp32） |
|---|---|---|---|---|---|
| 短文 3.76 s・40 steps | **0.808 s／RTF 0.215**（cold 1.032） | 0.875／0.233 | 0.801／0.213（cold 1.041） | 0.877／0.233 | 3,079／4,134 MiB |
| 長文 10.96 s（fp32 は 10.92 s）・40 steps | **0.917 s／RTF 0.084**（cold 1.170） | 1.563／0.143 | 0.929／0.085 | 1.603／0.147 | 3,079／4,682 MiB |
| 短文・10 steps | 0.300／0.080 | 0.387／0.103 | 0.289／0.077 | 0.444／0.118 | — |
| 長文・10 steps | 0.422／0.039 | 0.623／0.057 | 0.390／0.036 | 0.605／0.055 | — |

- **短文は司令官機 ROCm bf16（0.749 s）と同等・長文は 2 倍速**（sample_rf 短文 706 ms vs 626 ms＝小バッチではカーネル起動が支配し GPU の格が効かない。長文 769 vs 1,438 ms）。
- **cu130 の異常値は消えた**＝N:\ 直走行（`25`）では bf16 短文 40 steps が 1.496 s・cold<warm の逆転だったが、SSD 再走（`27`）で 0.808 s・cold 1.032 > warm の正常な順序＝NAS 上の走行ノイズだった（U-4 は閉じた）。**cu126 も同じ SSD で再走し、cu130 と同値**（bf16 短文 40 steps 0.801 vs 0.808・全 8 条件で差 ±3 %・fp32 10 steps 短文だけ 0.444 vs 0.387）＝**版の選択は速度では決まらない＝配布サイズとドライバ要件で決める**。NAS 走行の cu126 0.722 s は NAS 側のノイズ。
- **fp32 も正常**（decode 31〜99 ms＝ROCm fp32 の病的な遅さは CUDA では無い）＝CUDA では bf16 は必須でなく「速いから選ぶ」。
- **コールド罰が軽い**（SSD で 1.1〜2.1 倍・ROCm の 3〜12 倍に相当するものは無い）。VRAM bf16 3,079 MiB は司令官機と同値。
- **`torch.compile` は CUDA でも失敗**＝`TritonMissing`（3 回とも・CPU は `cl` 不在）＝⒜の武器にしない判断は確定。
- **cu126・cu130 とも torch 2.10.0 の pin のまま入り、ドライバ 591.86 で動いた**。導入後＝cu126 venv 4,478 MB・`torch/` 3,937 MB／cu130 venv 3,193 MB・`torch/` 2,649 MB（本機の自己検証と同値）。
- **N:\ 直走行（`24`・`25`）は 1 variant 138〜167 分**（各条件でモデルと torch の DLL を NAS から読み直し・`d_torch_guard`＝`import torch` に 5〜9 分・入れ直しではない）。**SSD では全段 6.0〜6.2 分**（`import torch` 1.8 s・torch の PyPI 取得＋導入 cu130 36 s／cu126 45 s・3090 機の回線・`27`）。`total_to_decode` はモデル読込を含まないが、NAS 走行の値は SSD 値と最大 1.7 倍ずれた（遅い側が多いが cu126 bf16 短文は速い側＝ノイズ幅の広さ）＝**CUDA の基準値は SSD 列**。

**ONNX（RTX 3090・`25`＝NAS 走行・`27`＝SSD 再測で同値・40 回中央値・入力は実合成と同形。`duration` の CUDA EP だけ `27` では 2.2 ms＝`25` の 7.7 ms より速い）**

| グラフ | CUDA EP（ORT 1.29・CUDA 13） | DirectML（ORT 1.24.4） | 8060S DirectML（`23`） |
|---|---|---|---|
| `dit_step` fp32（B=3） | **30.6 ms**（CPU EP 差 9.4e-3＝Ampere の TF32 既定） | 45.1 ms（差 2.5e-5） | 80.6 ms |
| `dit_step_fp16` | 26.9 ms・**差 6.20＝破綻** | 28.1 ms・**差 6.20＝破綻** | 46.7 ms・破綻 |
| 条件符号化（無手術／`_az0`） | 35.6 ms | CPU 落ち／**445 ms** | CPU 落ち／209.5 ms |
| DACVAE 復号 fp32（無手術／`_dml`） | 38.1 ms | run 失敗／85.3 ms | run 失敗／178 ms |
| 透かし（DFT 込み） | **47.3 ms（動く）** | ロード不可 | ロード不可 |
| duration／参照 encoder | 7.7／8.0 ms | 2.8／5.4 ms | 2.2／7.0 ms |

- **fp16 の DiT は CUDA EP でも壊れる**（差 6.20＝DirectML と同値）＝**破綻は DirectML 固有ではなく GPU EP 全般**（グラフ側の op が原因の公算・未特定）。復号の fp16 は両 EP とも正常。
- DirectML の後処理 3 種（`23`）は NVIDIA でも同じ箇所で必要＝**ベンダ非依存**。ORT 1.29 の CUDA EP なら透かしグラフも GPU に載る（版ずれの壁は DirectML 側だけ）。
**⒝の端から端まで（RTX 3090・`28`＝`26` のキットを SSD で実行・短文 3.76 s・fp32・DiT 実ループ session 2 本・warm）**

| EP（ORT） | 40 steps s／RTF | 10 steps s／RTF | DiT ループ（CFG／非 CFG ステップ） | 条件符号化 | 復号 | 透かし | 最終潜在の参照差 |
|---|---|---|---|---|---|---|---|
| CUDA EP（1.29.0） | **1.094／0.291**（cold 1.376／0.366） | 0.378／0.100 | 964 ms（30.5／16.4 ms） | 37.7 ms | 38.0 ms | 51.8 ms（GPU で動く） | 0.021（TF32） |
| DirectML（1.24.4） | **1.982／0.527（透かし抜き）**（cold 2.579／0.686） | 0.906／0.241 | 1,444 ms（46.5／25.0 ms） | 445.7 ms（`_az0`） | 89.7 ms（`_dml`） | **ロード不可**（DFT・CPU EP でも `is_onesided and inverse … cannot be enabled at the same time`） | 1.9e-5 |
| 参考: 8060S DirectML（`23`） | 2.815／0.749（透かし CPU 331 ms 込み） | 1.036／0.276 | — | 209.5 ms | 178 ms | CPU | 5e-5 以内 |

- **見立て（段合計）は当たった**＝CUDA EP ≈0.28→実測 0.291、DirectML ≈0.6→透かし抜きで 0.527（透かしを CPU に出す今日の形を足せば ≈0.6）。要求 EP は両方とも全段で使われている（CPU 落ちなし）。
- **判断則（DirectML・40 steps・fp32・両機で RTF ≤ 0.5）は NVIDIA 側でも不合格**＝3090 でも DiT ループ 1.44 s・条件符号化 446 ms（8060S の 210 ms より遅い）が重く、GPU の格差ほどには縮まらない。fp16 の DiT は両 EP とも参照との差 5.38（破綻の再現 4 回目）。

---

## 3. 感情制御（BRIEF §4-3）

根拠＝`05`・`06`・`01` §3・`02`、実射＝`14`（TestClient）・`20` §3（実サーバ）、検分＝`18`・`19`。

### 3-1 依頼の前提の更新

- 最内の合成関数は 1 つ＝`InferenceRuntime.synthesize(req: SamplingRequest)`（`inference_runtime.py:1036`）。引数は `SamplingRequest` の **43 欄**（`:202-246`・AST 実測）。
- **サーバは `speaker_uncond_mode` 以外の 42 欄を `irodori` ネスト（44 欄＝42＋chunking 専用 3 −`text`）で受ける**（`app.py:37-83,850-1041`・差集合を機械照合＝`lab/out/18-field-diff.json`）。逆に `min_seconds`/`max_seconds`/`tail_*`/`lora_adapter` は API にあって gradio に無い。
- したがって「HTTP API が露出していないパラメータ」は実質ゼロ。**露出していないのは読み分けちゃん側**＝裁定 6「露出は caption/seed/speed/volume（＋追撃で参照音声）」（`docs/irodori-tts-requests.md:145-151`）。
- 露出の真の穴は引数ではなく別の層＝⑴ device/precision はプロセス起動時固定（`RuntimeKey`）、⑵ 進捗コールバック無し、⑶ キャンセル不可（`asyncio.shield`・`app.py:778-784`）、⑷ 透かし on/off がどこにも無い（4 経路すべて grep 0 件）。

### 3-2 感情に効くもの（三層＋尺）

| 層 | 手段 | 粒度 | 根拠 |
|---|---|---|---|
| 発話全体のトーン | **`caption`（日本語自由文）**。例「深く傷つき、今にも泣き出しそうな様子。声が震えており、悲痛なトーンで弱々しく話す。」。上限 512 トークン（超過は黙って切詰め）。`normalize_text` を通らない | 1 発話 | README:130／`inference_runtime.py:1210`。語彙表・タグ一覧は上流に無い |
| 局所の演技・非言語音 | **本文中の絵文字 45 種**（👂囁き・😭泣き声・😠怒り・⏩早口・🐢ゆっくり・📖朗読 …）。正規化を素通り | 文字位置 | `gradio_emoji_palette.py:61-107`／README:25「Emoji-based Style Control」・README:570（BibTeX 題 "Emoji-driven Style Control"） |
| 効きの強さ | `cfg_scale_caption`（0〜10・Server 既定 3.0＝text の既定を流用 `app.py:936`・Gradio 初期値 4.0＝不一致） | 連続値 | `config.py` に caption 用 env 無し |
| （尺・話速） | `duration_scale`（`speed` の逆数・`app.py:843-844`）。caption も duration に入る。`speed` と `duration_scale` を両方送ると除算で合成（`14` 5b） | 1 発話 | — |

**「怒り度 0.7」のような数値の感情軸は存在しない。**

### 3-3 「声の値」欄に写せるか（本体側の器との突合）

本体側の器（`02` §1・`19` 検分済み）：`ParamKind`＝Number／Choice／Flag／Text（`yomiwakechan2/Core/Models/Capabilities.cs:8-20`）。`VoiceSetting`＝1 キャラ＋全パラメータ絶対値、本数は発見時固定。`ParamDescriptor.IsEmotion`（`:83`）で「感情」グループに出せる器は既存（CeVIO／VOICEPEAK／VOICEROID2 が使用・Irodori は 0 本）。**UI の振り分けは `IsFullWidth` が先勝ち**（`yomiwakechan2/UI/ViewModels/Settings/VoiceEditorViewModel.cs:230-232`）＝横長の Text（プロンプト）は感情欄に入らない。Text の新設は Core+UI 波及 4 箇所限定（裁定 5）。

| 値 | 写す形 | キャラ固定／発話ごと | 本体側の追加コスト |
|---|---|---|---|
| `caption` | Text（既存「プロンプト」欄＋定型集） | キャラ固定＋**発話ごと上書き**が欲しい | 上書き経路は未実装。多行は不可（`yomiwakechan2/UI/ViewModels/Settings/TextParamViewModel.cs:14`） |
| 絵文字（本文） | 本文編集側＝45 種の定数表をパレットに／キャラ既定の「前置き絵文字」を Choice（IsEmotion） | 発話ごと（視聴者コメントの絵文字がそのまま効く＝機能でもあり暴発でもある） | Choice 1 本＋アダプタ側で前置き＝Core 不触 |
| `cfg_scale_caption`・`cfg_scale_speaker`・`cfg_scale_text`・`num_steps`・`sway` | Number（IsEmotion・幅を要求しない）／Choice | キャラ固定 | **Core 不触**。裁定 6 の改訂が要る |
| `seed`・`speed`・`volume`・参照音声 | 既存 5 本 | — | — |
| device／precision | **写せない**（プロセス単位＝元栓の管轄） | — | §4 |

→ 数値の感情軸を N 本立てるだけなら Core を 1 バイトも触らずに載る。

### 3-4 実行時の進捗・キャンセル・逐次（TestClient＋実サーバで確定した契約 T-1〜T-12＝`14` §10）

- 進捗＝無い。キャンセル＝無い（`_infer_lock`・`asyncio.shield`）。**中止の粒度＝チャンク**が上限。
- **`irodori.no_ref=true` だけで voice は不要（TestClient・実サーバとも 200）**＝voice CRUD もダミー voice も要らない（T-1）。`IRODORI_ALLOW_NO_REF_VOICE=false` は no-ref を禁止できない。
- 優先順＝`irodori.X`（非 null）→トップレベル `X`→env。**綴り違い・未知欄は 4 通りとも 200 で沈黙・トップレベル経路は `Literal` 検査を回避**（`t_schedule_mode:"zigzag"` が通る）＝**白名簿検査が必須**（T-2・T-3）。
- **`X-Irodori-Seed` は複数チャンク時に 1 本目の seed しか返さない**＝その値で再現できない。seed 未指定はチャンクごとに別 seed、**指定すると全チャンク同一 seed**（実サーバで確認）。先読みキャッシュは `chunking_enabled:false` か `irodori.seed` 明示が前提（T-4）。
- **SSE はチャンク粒度で他要求と交互配車**（T-5）＝順序を守るなら層側で 1 本ずつ直列に。SSE を頼んでも検証段では JSON の 400 が返る（T-12）。
- `seconds` 明示で chunking が黙って無効化（T-7）。**境界文字（`。、，,．.!！?？\n\r`）が無い本文は分割されない**（T-6）。80 字未満の本文は既定（`chunk_min_chars=80`）では分割されない＝読み分けちゃんの 1 コメントでは chunking は既定で効かない。
- エラーは 400／422／500／503／SSE `event: error` の 5 態・422 と未知 voice の 400 はサーバの絶対パスを漏らす（T-8）。`/health` は認証不要・`runtime.loaded` が ready・**`model_device` は設定値の echo＝GPU を掴んだかは分からない**（T-9・`20` §3.2）。
- chunk 連結は `torch.cat` のみ（クロスフェード無し）＝韻律連続性は保証されない。

---

## 4. GPU 指定（BRIEF §4-4）

根拠＝`07`・`06` §2-3、実射＝`14` §6・`20`・`29`、検分＝`18`・`19`。**多 GPU 実機での実射は未了（U-3）＝以下はコード読解と 1 GPU 機での実測**。範囲外 index の落ち方・可視化変数・model/codec 分割・掴んだ GPU の確認手段の実射は §10-2（`29`）。

### 4-1 決定点

| 檔:行 | 何を決めるか | 仕組み |
|---|---|---|
| `irodori_tts/inference_runtime.py:55-77` | **唯一の関門** `resolve_runtime_device()` | `torch.device()` に丸投げ。受理＝`cpu/cuda/mps/xpu`。**`cuda` だけ index を通す**（`:62 return resolved`・`cpu:0` も通る）。`mps:0`/`xpu:0` は拒否。index の範囲検査は無い |
| 同 `:80-93` | `auto` の解決 | `cuda→mps→xpu→cpu` の先頭＝GPU があれば必ず 0 番 |
| Server `config.py:27-28` | `IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`（既定 `auto`） | pydantic-settings（env＋CWD 相対 `.env`・`extra="ignore"`）。**環境変数で device を触れるのは Server だけ** |
| Server `runtime.py:97-102` | `_resolve_device` | auto/空以外は**原文のまま素通し**。**`CUDA:1`（大文字）・`rocm`・`gpu` はサーバで検査されず torch の `RuntimeError`→500**（`preload=true` なら起動ごと落ちる） |
| Server `__main__.py:17-21` | CLI | `--host/--port/--reload` のみ |
| gradio `gradio_app*.py:642-645,660-663` | CLI | **device 引数ゼロ・env も読まない**。Dropdown は型名のみ |
| `infer.py:92-113` | CLI | `--model-device cuda:1 --codec-device cuda:1` 可 |
| `runtime.py:48` | 1 プロセス 1 モデル | `unload` も `get_cached_runtime` も使わない＝複数 GPU は複数プロセス |

- ROCm／CUDA の分岐は**コードに 0 行**（ROCm も `cuda` を名乗る・`compose.rocm.yaml:12`）。分岐は wheel の index（`pyproject.toml:76,82` の `sys_platform == 'linux'` マーカー／Server `uv.lock:1871,1874`）だけ。
- `torch.cuda.empty_cache()` の device 引数なし＝Server 経路で踏むのは **`app.py:512` の 1 箇所**（`inference_runtime.py:1471` は `unload()` 内で Server 未到達・上流 docstring `app.py:477-479`）。`IRODORI_EMPTY_CACHE_INTERVAL=0` で消える。
- 透かし器は codec 側 device に張り付く（`inference_runtime.py:619`）。

### 4-2 結論の材料

| やりたいこと | 手段 | 注意 |
|---|---|---|
| 1 番 GPU を使う | **方式 A「可視 GPU 制限」（推奨）**：`CUDA_VISIBLE_DEVICES=1`（AMD は `HIP_VISIBLE_DEVICES=1`・torch 2.13 `cuda/__init__.py:870-895` `_parse_visible_devices`）＋device は `auto` | 上流無改変・gradio でも効く。A と B は排他 |
| 同上 | 方式 B「device 直指定」：`IRODORI_MODEL_DEVICE=cuda:1` **＋ `IRODORI_CODEC_DEVICE=cuda:1`** | codec を忘れると 0 番に残る（`config.py:28`） |
| CPU 強制 | `MODEL_DEVICE=cpu`＋`CODEC_DEVICE=cpu`＋**`MODEL_PRECISION=fp32`＋`CODEC_PRECISION=fp32`** | 精度を戻さないと `:310` で `ValueError`（稼働機の bat は bf16） |
| 複数 GPU で 2 サーバ | ポート違いで 2 プロセス・各々に方式 A | 層が振り分ける |
| 実 device の確認 | **HTTP からは不可**（`/health` は `"auto"` の echo）。サーバ stderr の `model_device=cuda` 行のみ | 本体アダプタの現行 CPU 判別と同じ制約 |
| `IRODORI_COMPILE_MODEL` | **触らない**（CPU では MSVC の `cl` 不在で落ちる＝`21` §3。CUDA 側は 3090 キットで確定） | 初心者配布に Build Tools を要求することになる |

→ **答え＝「コード上は Server の環境変数だけで GPU index 指定と CPU 強制は足りる（上流無改変）。ただし引数ではなく環境変数・codec 側も必ず・値は lower/strip して白名簿で検査・1 プロセス 1 デバイス。多 GPU 実機での実射は未了」**（範囲外・綴り違い・可視化変数・分割の落ち方は `29` で実射済み＝§10-2。`PRELOAD=true` なら掴み損ねは起動時に exit 3・`false` なら全合成 500）。⒞の薄い層の仕事は「env を正しく組む」こと＝元栓が precision 2 択を env 注入している型（`yomiwakechan2/Engines/Irodori/IrodoriConstants.cs:216-230`）にセレクタを 1 つ足す規模。
→ 本体側の前例＝「推論デバイスを**選ぶ**」前例はゼロだが、「デバイスを名前で保存し添字へ再解決する器」（`yomiwakechan2/Engines/Common/AudioOutputDevices.cs:21,24,52`・`yomiwakechan2/Core/Models/AppSettings.cs:533`）と「エンジンの device を受け取って表示する」前例（COEIROINK・`yomiwakechan2/Engines/Coeiroink/CoeiroinkSpeakerInfo.cs:36-37`）が既にある＝**既存 2 型の合成**として提案できる（`19` §2-1）。「アダプタはエンジン内部設定を管掌しない」流儀（`docs/irodori-tts-survey.md:255-257`）の上書き裁定は要る（§0-2 の 4）。**gradio を土台にすると GPU 指定で不利**＝⒜⒞とも Server を土台に。

---

## 5. 導入の重さ（BRIEF §4-5）

根拠＝`08`・`01` §2・`10` §4・`11`・`21`・`22`、検分＝`17`・`18`・`19`。

### 5-1 初心者が詰まる箇所（現状）と⒜での帰趨

| 詰まり | 一次根拠 | ⒜で |
|---|---|---|
| **Windows で `--extra rocm` が無言で CPU 化** | `pyproject.toml:76`／Server `uv.lock:1871,1874` | **消える**（配布側が torch を固定） |
| uv 未導入・PATH 反映・`--extra` 4 択・torch index | rocm-setup-guide:36-39,69／README:61-71 | 消える |
| Python 3.10（上流）と ROCm 3.11+（AMD）の版の矛盾 | guide:57,61-62 | 消える（embed の版をこちらが決める） |
| `sentencepiece<0.2` が cp312 でビルド失敗／`overrides` の手作り | guide:86-90／`pyproject.toml:19` | 消える（build 機で 0.2.1。lab でも合成正常） |
| `uv run` が旧 venv を掴む／**稼働機の venv は uv 管理 python を子プロセスで再実行する＝uv の python を消すと venv が死ぬ** | guide:126／`20` §0.1 | 消える（venv を使わず埋め込み python を直起動） |
| **bat の環境変数が無言で落ちる**＝bat 自身が「CRLF + ASCII 維持。UTF-8/LF で `set` 行が黙って落ちた」と明記（`local-additions/Irodori-TTS-Server/irodori-server-gpu.bat:4`）。yomiwakechan2 側の記録は「文字コード事故」で、**bat 経由は不採用の裁定**（`docs/log.md:5503-5505`・`decisions.md:8305-8306`） | 同左 | 消える（本体が env を直接注入＝既に irodori 席で成立） |
| `cp .env.example .env`（bash 前提）・`.env`/`voices` が CWD 相対 | Server README:42／`config.py:13,41` | 消える（CWD 固定・絶対パス env） |
| `localhost` で +1.2〜2.0 秒／`EMPTY_CACHE_INTERVAL=10` で約 +15 秒のスパイク再発（周期は未検証）／`PRELOAD=false` で初回合成に読込 11〜25 秒が乗る | probe-report:255-257／gpu-latency-raw:48-52／rocm-setup-guide:130-131 | 消える（既定を焼く） |
| **起動直後・初見形状のスパイク**＝同文 12 連射でも `EMPTY_CACHE=0` で 1 発目 **40.8 s**・3 発目 19.4 s（定常 4.1 s の 10 倍／4.7 倍・4 発目以降 9 射は 4.09〜4.18 s）。別現象として未見の音声長の初回 3〜6 倍 | gpu-latency-raw:57-68（`19` §2-2）／`20` §3.8 | **残る**（常駐＋起動時ウォームアップ射で緩和。原因はカーネル選択＝根治は未確認。配信中に 40 秒の無音は致命的＝U-6） |
| **GPU ドライバ**（NVIDIA／Adrenalin） | guide:27-29,159 | **残る** |
| **VC++ 再頒布（msvcp140.dll）** | `22`（`c10.dll` の PE 直読・embed zip に無し） | **残る**（`vc_redist.x64.exe` 同梱＋導入 1 段） |
| **初回モデル取得 ≈3.3 GiB** | `inference_runtime.py:554-570`／`codec.py:72`／silentcipher `server.py:475` | **残る** |
| ディスク空き | probe-report:96,345 | 残る |

→ guide の 21 操作（`01` §2.4）のうち **16（表の 3〜18）が同梱で消える**。残るのは **6 操作**＝ドライバ確認・空き容量・VC++ 再頒布・動作確認・停止・CPU 復帰（vc_redist をインストーラが黙って通せれば 5）。

### 5-2 python-embed 化の可否（成立・条件つき）

| 論点 | 判定 | 根拠 |
|---|---|---|
| 現 venv の再配置 | **不可**（`pyvenv.cfg` の `home` と `__editable__…pth` が絶対パス） | ⒜は「移送」でなく「作り直し」 |
| torch の DLL 探索／soundfile | ○（package 相対・libsndfile 同梱） | torch `__init__.py:169-242` |
| `._pth` と `site` | **不要**（`._pth` だけで Server 起動を実射・`import site` なし。protobuf ≥4 なら nspkg .pth も消える）。**`._pth` があると `PYTHONPATH` は無視される**＝パスは `._pth`・設定は env | `30` §0-3・§1-B（§10-1） |
| **埋め込みでの起動** | **成立**（3.12.10 embed＋`._pth`＋CPU torch で `/health` 200・`PRELOAD` 読込 28.2 s）。残る条件＝vc_redist・絶対パスの `voices_dir`（起動時 mkdir）・`*.dist-info` 同梱 | `30` §0-3・N-7・N-8（§10-1） |
| **MSVC ランタイム** | **`vc_redist.x64.exe` が要る**（embed は vcruntime140/_1 を同梱するが msvcp140 は無い・torch が要求） | `22` §3b・4b |
| **torchcodec／FFmpeg** | **外せる。ただし上流パッチ 2 箇所が条件**＝`inference_runtime.py:1518`（`_load_audio`＝**Server の参照音声経路**）と `:1536`（`save_wav`）の `except RuntimeError` が torchcodec 不在時の `ImportError` を取りこぼす（lab 実測・`18` §3-e）。稼働機は「入っているが DLL で落ちる＝RuntimeError」で救われているだけ。wav/flac/pcm の出力は soundfile 先行で torchcodec 非依存（`audio.py:44-49`） | 「torchcodec なし配布＋参照音声 voice」は無改造では 500 |
| 不要パッケージ | サーバ経路で import されない gradio・wandb・llvmlite/numba・jedi・matplotlib・torchcodec・peft・datasets・IPython ≈370 MiB **＋ pandas/pyarrow ≈130 MiB**（transformers は sklearn 経由の任意 import・`datasets` を入れなければ不要・`18` §3-m）＝**≈500 MiB**＋`torch/include`・`*.lib` 93 MiB | 未検証（外して起動確認はしていない） |
| 出力形式 | wav/flac/pcm は soundfile で完結（aac だけ外部 ffmpeg プロセス）＝**wav 固定で FFmpeg 不要** | `audio.py:44-69`・`14` U-5 |
| モデル置き場 | `HF_HOME` 固定／同梱時は `IRODORI_CHECKPOINT`＋隣の `tokenizer/`＋`IRODORI_CODEC_REPO`＝ローカルパス | `08` §5-B |
| Server 既定 checkpoint | **v4-Small のまま**（`config.py:23`）。契約面（形状・上限値）は v4.1 と同一だが**インストーラが v4.1 に決め打つ** | `18` §7 |
| Python の版 | 3.12 embed は **3.12.10 が最後のバイナリ**。**3.13.9 embed（10.4 MiB）が存在**＝退路 | `17` (k) |
| **配布経路** | **未検討**。実行系だけで ≈2.1 GiB＝GitHub Releases の 1 資産上限（2 GiB）を超える＝分割・外部ストレージ・段階取得のいずれかが要る。⒝なら 24 MiB で本体に内包＝この論点も消える | U-8 |

### 5-3 配布物の大きさ（ダウンロード量・wheel は一次でバイト一致 `17`・`21`）

| 区分 | 実行系（DL） | モデル一式（DL） | **合計 DL** | 導入後ディスク（実測・参考） |
|---|---|---|---|---|
| **⒜ CUDA cu130**（embed 10.6＋torch 2.10.0+cu130 **1,781**＋非 torch ≈250＋vc_redist 25 MiB） | ≈2.1 GiB | ≈3.3 GiB | **≈5.4 GiB** | venv 3,192 MB（本機 `21` §4・3090 機 `25`・`27` とも 3,193 MB）＋モデル ≈3.5 GB ≈ 6.9 GB |
| ⒜ CUDA cu126（torch 2.10.0+cu126 **2,470** MiB） | ≈2.7 GiB | 同 | ≈6.0 GiB | 3090 機の実測＝venv 4,478 MB・`torch/` 3,937 MB（`24`）＋モデル ≈3.5 GB ≈ 8.0 GB |
| ⒜＋ROCm 同梱 | ＋1,125〜1,304 MiB | 同 | ≈6.5 GiB | ROCm 固有 ≈3.5 GB が展開後に乗る |
| CPU のみ（torch 2.10.0+cpu 108 MiB） | ≈0.4 GiB | 同 | **≈3.7 GiB** | venv 1.24 GB（削って ≈0.75 GB）＋モデル ≈ 4.3〜4.8 GB |
| 稼働機 ROCm venv（参考） | — | — | — | 4.76 GB（ROCm 固有 3.46 GB）＋モデル ≈ 8.3 GB |
| **⒝ ONNX＋DirectML EP** | **23.9 MiB**（＋C# 1.0 MiB） | ONNX fp32 2.87 GiB＋後段 0.25 GiB | **≈3.1 GiB**（fp16 が CUDA EP で使えれば 1.6 GiB。DirectML では fp16 が壊れる。**CUDA EP を選ぶと `onnxruntime-gpu[cuda,cudnn]` が DL 1,227 MiB・展開 1,788 MB 加わり、torch cu130 との差は ≈550 MiB に縮む**＝`26`・`27`。DirectML 版 ORT は 139 MB） | ≈同左 |
| ⒝ ONNX＋CUDA EP | 141 MiB＋CUDA13/cuDNN9 1,075 MiB（`onnxruntime-gpu[cuda,cudnn]` の extras のまま 12 パッケージに解決＝明示ピン不要・DL 1,227 MiB・展開 1,788 MB＝`26`・`27`） | 同 | ≈2.8〜4.3 GiB | — |

- モデル一式＝重み 2,922 MiB＋コーデック 410 MiB＋透かし 65 MiB＋tokenizer 6 MiB ≈ **3.3 GiB**。
- **`cu128` は袋小路**（索引の最終 torch 2.11.0）。**cu126（2,470 MiB・Windows 最小ドライバ ≥560.76＝12.6 GA）か cu130（1,781 MiB・CUDA 13 は OS 非依存表で ≥580・Windows 固有値は原文に無し）**の二択。両索引に torch 2.10.0 が実在＝上流の pin はどちらでも通る。「cu126 なら 527.41 で足りる」は誤り（`17` (j)）。
- Windows の torch CUDA wheel は CUDA ランタイム・cuDNN を同梱（torch 2.10.0+cu130 で `torch/lib` 展開後 2,554 MB＝`21` §4。2.14.0 なら DLL 39 個 2,738 MiB＝`17` (c)）＝利用者に CUDA Toolkit を踏ませなくてよい。**ただしライセンス面は肩代わりされていない**（§1-3）。
- 初回起動の体感＝モデル読込 11〜25 秒（PRELOAD・bf16）・HF 初回 DL 70 秒級（本機）。⒝なら前段の 13 秒／500 MB が消える。

---

## 6. 三案の比較表と推奨（BRIEF §2-4・§4-6）

### 6-1 三案の定義（本調査で確定した姿）

- **⒜ 専用インストーラ**＝python-3.12 embed（または 3.13）＋build 機で固定した site-packages（CUDA のみ・不要 ≈500 MiB 削除・`licenses/` 7 種・torchcodec 除去＋上流パッチ 2 箇所 or 参照音声を soundfile 固定・vc_redist 同梱）＋**Irodori-TTS-Server を土台**（gradio 非同梱）＋モデル初回取得（音声認識内包便の型＝sha256・原子的着地・`%LOCALAPPDATA%`・`decisions.md:8549-8554`）＋元栓に既定パス候補・GPU セレクタ。
- **⒝ 非 Python 化**＝ONNX 5 グラフ（条件符号化・duration・DiT ステップ・DACVAE 復号・透かし。任意で encoder）＋C# 前段（231 行・実証済み）＋C# サンプラ（35 行）＋ORT（DirectML／CUDA／CPU）を本体に内包。写経元＝音声認識内包便（sherpa-onnx NuGet・`decisions.md:8486-8489`）。
- **⒞ 専用制御方式**＝Server 据え置き。**「薄い層」は別プロセスを建てず本体アダプタ＋元栓が担う**（GPU セレクタ＝env 注入・感情露出＝既存の Number/Choice/Text 器・chunk/SSE 制御・白名簿検査・複数サーバの振り分け）。上流無改変。

### 6-2 比較表

| 軸 | ⒜ インストーラ（CUDA のみ・Server 土台） | ⒝ 非 Python 化（ONNX＋DirectML） | ⒞ 専用制御方式（Server 据え置き） |
|---|---|---|---|
| 初心者の導入手順の長さ | **6 操作**（ドライバ確認・空き容量・VC++ 再頒布・動作確認・停止・CPU 復帰。vc_redist を黙って通せれば 5） | **最短**（本体に内包・追加ランタイム 24 MiB・EP 分岐なし・Win10 も可）。残る＝モデル DL・VC++ 再頒布（要否は U-8） | **現状 21 操作**（配布ページの手順書に依存・地雷が全部残る） |
| 感情パラメータの露出度 | ⒞と同じ | **最大**（全 43 欄＋ステップ単位の進捗・キャンセル＝⒝でしか得られない） | 42/43 欄が API に既にある＝アダプタ側の裁定 6 改訂で満点（Core 不触で数値軸 N 本） |
| GPU 指定 | 元栓の GPU セレクタ（`CUDA_VISIBLE_DEVICES` 注入）。複数 GPU＝複数プロセス。**多 GPU 実機は未実射**（落ち方は `29` で確定・§10-2） | ORT の device_id＝in-process・複数セッション。**番号は DXGI 順＝⒜の CUDA 順と別体系**（`30` §3-3） | 同⒜ |
| GPU 対応範囲 | **CUDA のみ**（ROCm は司令官機の手動＝AMD 公式経路） | **NVIDIA/AMD/Intel を DirectML 1 本で**（DX12＝Steam の 91.45%）。司令官機の iGPU も載る | 利用者が venv を選ぶ＝手順が分岐 |
| レイテンシ（RTF） | **CUDA（3090・cu130・bf16・SSD 基準値）＝短文 0.215／長文 0.084**（fp32 でも 0.233／0.143・コールド罰 1.1〜2.1 倍。cu126 も同値 0.213／0.085）。ROCm（司令官機・bf16）＝0.197／0.158・実サーバ 0.229 | CPU＝RTF 3.27（未達）。**DirectML fp32（AMD iGPU）＝RTF 0.749（40 steps・透かし DML 未搭載の段合計。透かしを CPU に出す今日の形は 0.827）／0.276（10 steps）**＝届くが ROCm torch の 3.8 分の 1。**3090（実測・`28`）＝DirectML 0.527（透かし抜き・CPU 透かし込みで ≈0.6）／CUDA EP 0.291**。fp16 は GPU EP 全般で壊れる。初回費用は ROCm より桁で軽い（+1.1 s） | 実測のまま（ROCm bf16 0.197〜0.229）。SSE＋先頭文短縮で初音 0.72〜1.09 s。起動直後のスパイク最悪 40 秒級は⒜と共通 |
| 配布物の大きさ（DL） | ≈5.4 GiB（cu130）／≈6.0 GiB（cu126） | **≈24 MiB＋ONNX fp32 3.1 GiB**（fp16 なら 1.6 GiB だが DirectML では壊れる＝CUDA EP の検証後。CUDA EP 採用時は ORT＋cuDNN の DL 1.2 GiB が加わる） | 0（利用者機に 4.3〜8.3 GB） |
| ライセンス | 鎖は可・**実行系の licenses/ 7 種**（CUDA EULA・cuDNN SLA・MSVC・LGPL 申し出）が配布側の義務 | 鎖は可・licenses/ 3〜4 種（DirectML MSLT＝Windows 限定）。**透かし枝（facebook 由来 35.6 MiB）が成果物から落ちる**・libsndfile の LGPL 義務も消せる。SAM 説では §1.b.iv が卓の材料 | 最も軽い（再配布なし） |
| 保守（上流追随） | torch 版ごとに wheel 再解決＋再検証・`torchcodec<0.11`/`torch<2.11`/torchaudio 追随遅れの pin を持ち回る・**上流パッチ 2 箇所を版ごとに再当て**（or 参照音声を soundfile 固定にして不要化） | **再 export の保守**（上流に ONNX 0 行・チェックポイント更新ごとに 5 グラフ再出力＋照合。LoRA・torchao 量子化は非対応）。**DirectML EP は 1.24.4＝本流 1.29 から 5 マイナー遅れ** | 上流の pin に追随するだけ |
| 既決との衝突 | 裁定 16「既定パス候補なし」を変える（卓の裁定 1 件・前例＝irodori 便）／配布ページ更新便と合流 | **「本体はバックエンド非依存」（`requests.md:105-106`）を書き換える＝設計原則の変更・他 8 エンジンへの波及**／音声認識内包便が前例 | 裁定 6（露出）と「本体管轄外」（GPU）の上書き（各 1 件） |
| 技術リスク（実証後） | 低（embed 配置の起動確認・MSVC・パッチ 2 箇所・配布経路） | **中**＝fp32 の数値と GPU 実行は実証済み。残る＝**fp16 が DirectML で壊れる**（原因未特定）・**透かしグラフが DirectML 1.24.4 で載らない**（版ずれ）・**同時 Run が segfault**（合成の直列化が必須）・形状ごとの session 分割・opset 20 上限（Attention 融合不可）・CUDA EP 未測・INT8 未実施 | 低 |
| 工数（便） | **4 便**（＋⒞要素 2 便＝計 6） | **8 便＋不確実性 2 便** | **3 便** |

### 6-3 推奨と退路

**推奨＝⒜（CUDA のみ・Server 土台・python-embed）＋⒞の要素を本体アダプタに内包（計 6 便）**。

理由＝⑴ 目的の 3 つのうち、感情と GPU は**上流無改変・Core 不触**で本体側の裁定改訂だけで満たせる（§3・§4）。⑵ 初心者導入の主因（torch の選択を利用者に委ねる構造）は「配布側が torch を固定する」以外に解けず、それを解くのは⒜か⒝。**⒝も解ける。だが DirectML の実測は AMD で RTF 0.749＝判断則（≤0.5）に届かず、NVIDIA（3090）も 0.527（透かし抜き・`28`）で不合格**＝今は採れない。⑶ 音声認識内包便が「初回 DL・sha256・原子的着地・licenses 同梱」の型を作っている。⑷ ライセンスの門は閉じない（通行料あり）。⑸ bat 経由の環境変数注入は実機事故で既に不採用＝⒜は「bat を利用者に書かせない」形と整合。

**⒜の推奨は CUDA 実測で裏づけられた**＝RTX 3090 で bf16 短文 RTF 0.21／長文 0.08（cu126・cu130 同値）、fp32 でも 0.23／0.14、コールド罰 1.1〜2.1 倍、`torch.compile` は Triton 不在で失敗（`24`・`27`）。したがって卓は**今**裁定してよい。覆る条件だった「3090 で RTF>1」は消えた。残るのは cu126/cu130 の選択だけ（両方ドライバ 591.86 で動作確認済み・cu130 の異常値は SSD 再走で消えた＝`27`）。

| 便 | 内容 | 前提／並行 | 敵対検分の要点 |
|---|---|---|---|
| A4 NVIDIA 実測と版の裁定 | **RTX 3090 機でキット実行**（`N:\irodori-native-research\kit-cuda\`＝`lab/kit-cuda/`＋ONNX 8 グラフの写し）＝cu126/cu130・ドライバ・RTF・VRAM・`torch.compile`・ONNX の CUDA EP/DirectML EP | **実施済み**（`24`・`25`・`27`＝cu126/cu130 とも動作・cu130 の SSD 基準値・ONNX 両 EP）。所要＝SSD で全段 6.2 分（N:\ 直走行は 138〜167 分）。⒝の端から端までも両 EP で実測済み（`28`） | 持ち帰った `results/` の読み方＝`21` §5 |
| A1 配布物ビルド | build 機で embed＋site-packages 固定（A4 の結果で cu126/cu130）・不要 ≈500 MiB 削除・torchcodec 除去＋**上流パッチ 2 箇所**（`:1518`/`:1536`）or 参照音声 soundfile 固定・`licenses/` 7 種・vc_redist 同梱・v4.1 決め打ち・配布経路の裁定 | 先頭（A4 は完了） | 別機（3090）で起動・参照音声つき合成・licenses/ の網羅 |
| A2 モデル初回取得 | 音声認識内包便の写経（重み 2.85 GiB＋コーデック 410 MiB＋透かし）・`HF_HOME` 固定・sha256・進捗・再開 | 独立（先行可） | 途中切断・ディスク不足 |
| A3 元栓改修 | 既定パス候補（裁定 16 改訂）・GPU セレクタ（`CUDA_VISIBLE_DEVICES`・デバイス名→添字の既存器を流用）・CPU 退避＝precision 連動 4 本・device 文字列の白名簿・CWD 固定・`PRELOAD`/`EMPTY_CACHE=0` 焼き込み・起動時ウォームアップ射 | C1/C2 と同じ元栓・アダプタを触るので直列 | 多 GPU 振り分け（実機は未実射）・bf16 事故 |
| A3-lite（退路用） | GPU セレクタ＋device 白名簿＋precision 連動**のみ**（既定パス候補・CWD 固定・焼き込みは持たない） | ⒞単独のとき A3 の代わり | — |
| C1 感情露出 | 裁定 6 改訂＝`cfg_scale_caption`/`cfg_scale_speaker`/`cfg_scale_text`/`num_steps`/`sway`（Number/Choice・IsEmotion）＋絵文字パレット（本文編集側 or 前置き Choice）＋発話ごとの caption 上書き | ⒜と独立に先行可 | 視聴者絵文字の暴発・512 トークンの黙殺 |
| C2 chunk/SSE/中止/白名簿 | `chunking_enabled` 制御・SSE＋先頭文短縮・中止粒度＝チャンクの明文化・**白名簿検査**（綴り違い沈黙・Literal 回避）・`X-Irodori-Seed` の非再現性への対処（seed 明示） | ⒜と独立に先行可 | 直列サーバでの先読みと交互配車・分割で喋りが伸びる件の耳検分 |

**退路＝⒞単独（3 便＝C1・C2・A3-lite）**＝インストーラを作らず、配布ページ更新便（引き金成立済み・未起票）の手順書で導入を案内し、アダプタ側で感情露出と GPU セレクタだけを足す。

**⒝＝次期候補（今便では採らず・現時点では保留）。** 「動くか」は決着した（全段 ONNX・wav 差 1 LSB・前段 C# 完全一致・ループ 35 行・**DirectML で GPU 実行＝RTF 0.749**）。

- **判断則**（短文・40 steps・fp32・常駐ウォームで測る）＝**DirectML EP が司令官機（AMD）と 3090（NVIDIA）の両方で RTF ≤ 0.5** なら、⒝は「CUDA か ROCm か」の分岐と Python 配布を同時に消す唯一の案として⒜の配布層を置き換える。閾値 0.5 は「配信の先読み運用に要る余裕＝実時間の 2 倍」という主席の判断で、実測の裏付けは無い。⒜側の参照値＝3090 CUDA torch bf16 0.215（`27`）・司令官機 ROCm bf16 0.197。
- **司令官機の結果（`23`）＝40 steps で 0.749＝不合格が確定**＝⒝は現時点で保留（40 steps 基準）。**3090 の DirectML も端から端まで 1.982 s／RTF 0.527（透かし抜き・`28`）**＝透かしを CPU に出す今日の形なら ≈0.6＝**NVIDIA 側でも不合格が確定**（DiT ループ 1.44 s・条件符号化 446 ms が重い・透かしは ORT 1.24.4 では載らない）。**CUDA EP なら 1.094 s／0.291 で届く**が、CUDA EP で採ると「分岐を消す」という⒝の存在理由が消え、⒜（torch・bf16 0.215・`27`）にも速度で勝てない。**ただし司令官の耳検分（`35`・9 対）で 10 steps と 40 steps に差が聞き取れなかった**＝判断則を 10 steps で引けば DirectML は AMD 1.036 s／RTF 0.276（`23`）・3090 0.906 s／0.241（透かし抜き・`28`）で、透かしを CPU に出しても ≈0.36／≈0.30＝**両機とも通る**。判断則の 40 steps は主席が置いた値で実測の裏付けが無いので、steps の裁定は卓（§10-4 の 14）。通すなら⒝は「保留」から「復活」に変わる（工数 8 便＋2 は不変・fp16 と透かしの DFT の札も不変）。
- **復活の条件**＝⑴ fp16 破綻の解消（GPU EP 全般で壊れている＝原因 op の特定が先。直っても AMD 40 steps 0.52 で判断則は満たさない）＋10 steps 運用（fp16 で 0.214）の音質が司令官の聴取で合格、または ⑵ KV 入力版 ONNX・opset 23 Attention 融合（DirectML では塞がる＝Windows ML／CUDA EP 側のみ）等で 40 steps が 0.5 を切る、または ⑶ 条件符号化を DirectML から CPU/CUDA へ逃がす等の EP 混成。いずれも 1〜2 便の実験。
- **乗り換えの手戻り**＝A1（配布物ビルド）は全損。A2（初回取得の型）・A3（GPU セレクタの概念）・C1・C2 は流用可＝**捨てるのは 1 便**。
- ⒝の便（8＋2）：

| 便 | 内容 | 根拠の所在 |
|---|---|---|
| B1 export パイプライン＋**DirectML 向け後処理** | RoPE 実数化・`_safe_attention_mask` 書き換え・weight_norm fold・透かし BN・5 グラフの自動再 export と照合（回帰試験）＋ `allowzero` 剥がし・`ConvTranspose` 2 次元化・透かしの iSTFT を DFT 抜きで組み直し | lab 実装済み（`lab/bin/10_〜90_*.py`・`export_*.py`・`wm_full_onnx.py`・`23_allowzero.py`・`23_conv1d_to_2d.py`） |
| B2 C# 前段＋サンプラ | 正規化・Unigram・BOS/パディング・t スケジュール・CFG・Euler・末尾トリム・duration 特徴 14 次元 | `15`（231 行）・`13` §6-1（35 行） |
| B3 ORT 組込み | Microsoft.ML.OnnxRuntime.DirectML（＋CUDA/CPU の切替）・session options（mem pattern 無効・sequential）・**バッチ形状ごとの session 分割**・**合成の直列化**・2.9 GiB の読込・fp32 固定（fp16 は DirectML で壊れる）・DXGI でのアダプタ列挙 | `17` (g)・`20` §5・`23` §4・§7 |
| B4 音声 I/O・後段 | 48k↔44.1k リサンプル（sinc の係数一致）・pad・one-hot・wav 書出し（NAudio） | `12` §5-4 |
| B5 モデル初回取得 | A2 の型（ONNX 2.9 GiB＋後段）・sha256・原子的着地 | A2 流用 |
| B6 エンジン統合 | in-process の `IEngineSynthesizer`・ステップ単位の中止・進捗・GPU セレクタ・感情露出（C1 流用） | `02` §1 |
| B7 検証 | DirectML on AMD/NVIDIA・fp16 聴取 A/B・動的形状・メモリ・照合の回帰 | `23`・3090 キット |
| B8 配布・licenses・追随手順 | licenses/（DirectML MSLT 等）・配布経路・新チェックポイントの再 export 手順・LoRA/量子化非対応の明記 | `22` |
| ＋2（不確実性） | DirectML の並列実行不可・動的 shape の制約が実測で刺さった場合の KV 入力版 ONNX／opset 23 Attention 融合／ModernBERT グラフの fp16 問題 | `13` §9・`17` (g) |

---

## 7. GPU 対応範囲のコスト比較（BRIEF §4-7）

根拠＝`10`（一次・2026-09-04）・`08` §6・`07`・`22`、検分＝`17`（サイズ系 13 件バイト一致・成熟度と Windows ML の 2 件を訂正）。

### 7-1 軸ごとの比較（CUDA のみ vs CUDA＋ROCm）

| 軸 | ⒜ インストーラ | ⒝ ONNX | ⒞ 制御層 |
|---|---|---|---|
| 配布物 | CUDA torch 2.10.0 cu130 1,781 MiB（cu126 2,470）。ROCm を足すと **＋1,125〜1,304 MiB（DL）／展開後 ≈3.5 GB**（AMD wheel は DL では CUDA より小さい）。機種別パッケージ（`amd_torch_device_gfx1151`）＝1 本で全 AMD は賄えない（`device-all` extra の有無は未確認） | **DirectML 23.9 MiB で両方**（分岐消滅）。CUDA EP なら ＋1.19 GiB | 配らない |
| 導入手順の分岐 | 1 本 vs **2 本**（AMD は index・`[device-gfxXXXX]`・overrides・Python 版が別） | **0**（DX12 なら動く。Windows ML は CPU＋DirectML が全サポート Windows で動き、24H2 が要るのは動的取得 EP だけ） | 利用者が選ぶ |
| 検証機の要否 | CUDA＝**RTX 3090 機あり**（キット・費用 0）。ROCm＝司令官機 | 両ベンダとも司令官機＋3090 機で測れる | 同⒜ |
| Windows ROCm の成熟度 | **司令官機の構成（`whl-next`・torch 2.13.0+rocm10.0.0・gfx1151）は AMD 公式統合 doc（ROCm 10.0.0）の Windows pip 手順そのもの**（Python 3.11〜3.14・`17` (e)）。「PyTorch のみ／訓練なし／batch 1」は 7.2.1 で凍結された旧 doc の記述。既知問題＝vLLM FP16 decode の batch≥8（torch<2.14）。**fp32 は MIOpen で decode が壊れる（bf16 必須・`20`）**。Windows 専用 limitations の現行版は未確認 | DirectML は "sustained engineering"（新機能は Windows ML へ）。ROCm EP は ORT 1.23 で削除・MIGraphX は Windows ML 経由 | 同⒜ |
| ライセンスの裏取り | CUDA＝EULA/SLA の一次資料あり。**ROCm wheel は METADATA 3 行・License 欄なし＝一次資料が取れていない**（`22`） | DirectML MSLT あり | — |
| 上流追随の保守 | 1 系統 vs 2 系統（torchaudio が torch に追随しない上流事情＝2.14.0 に対し 2.11.0） | ORT 版のみ（torch の鎖から抜ける）。再 export の保守 | 上流の pin のみ |
| 利用者層 | Steam 2026-08（一次＝`store.steampowered.com/hwsurvey/`・2026-09-04 取得・保存 `lab/out/verify17/steam.html`）：**NVIDIA 72.88／AMD 18.68／Intel 8.03**・上位 12 機種が全て NVIDIA・DX12 91.45%・Win11 70.97／Win10 22.90。配信者限定の統計は未確認 | 同左＝DirectML なら 91.45% を 1 本で | 同左 |

### 7-2 結論

> **裁定（2026-09-04・司令官）＝ROCm は未保障。ただし Strix Halo（gfx1151）のみ確認済みとして使えるパスは消さない＝② 別途「Radeon 用」としてリリース（追加裁定＝Radeon 版は独立・bf16 固定・起動時にできるだけウォームアップして本体からの読み上げに備える）。** 以下の比較と①②の表は裁定前の材料として残す。配布の既定は CUDA、対応表・自動判定は持たない。

**① と ② の材料（事実だけ・決めない）**

| 軸 | ① 同一配布物に「未保障・Strix Halo のみ確認」の ROCm 経路を残す | ② 別途「Radeon 用」としてリリース |
|---|---|---|
| 配布物 | CUDA 1,781 MiB に ROCm 1,125〜1,304 MiB を同梱すれば ≈3 GB、利用者に選ばせて取得なら CUDA 版と同じ。ROCm 側は `amd_torch_device_gfx1151`（115x）決め打ち | Radeon 用は ROCm wheel だけ（DL 1,125〜1,304 MiB・展開 ≈3.5 GB）＝DL は CUDA 版より小さい |
| 導入手順 | 1 本の中に分岐（index `whl-next`・`torch[device-gfx1151]==2.13.0+rocm10.0.0`・overrides・Python 版）。分岐の入口は**利用者の明示選択**（自動判定は持たない） | 2 本の独立した手順・インストーラ。分岐は「どちらを落とすか」で消える |
| 保証の書き方 | 「gfx1151 で確認済み・他の Radeon は未保障」を選択画面と手順書に明記 | 同じ文言をリリース名に付ける（例「Radeon 版・Strix Halo 確認済み」） |
| 実行時の差（コードは同一） | 精度は **bf16 固定**（fp32 復号が病的に遅い・fp32 は GPU 不可視で CPU 転落＝`20`・`29`）・可視化変数は `HIP_VISIBLE_DEVICES`・初見形状の罰（起動時の暖機・参照長ごとに最大 19 s＝`31`） | 同左 |
| 保守 | pin 2 系統（cu130 2.10.0／rocm 2.13.0）を 1 配布で持つ＝更新のたびに両方通す | 2 系統を 2 リリースで持つ＝**Radeon 版だけ凍結する**ことができる |
| 検証機 | 司令官機（gfx1151）のみ | 同左 |
| ライセンス | ROCm wheel の一次資料は未取得（`22`）＝同梱・再配布するなら 1 射 | 同左 |
| 工数（見積り） | ⒜の A1 に ROCm 分岐 +1 便級（overrides・embed の Python 版・暖機の手当） | ビルド系統をもう 1 本 +1 便級。以後は「Radeon 版は凍結」が選べる |

**「CUDA のみで出し、ROCm は司令官機だけの手動導入手順として残す」は成立する。** ただし理由を取り違えないこと＝**配布物サイズは理由にならない**（ROCm wheel の方が小さい）。**「非公式・無保証」も理由にならない**（司令官機の手順は AMD 公式・「公式経路」と書いてよい）。正しい理由は ⑴ 利用者比（72.9% vs 18.7%）、⑵ GPU 機種別パッケージと Python 版・index の分岐＝導入手順が 2 本に割れる、⑶ 検証と保守の二重化（NVIDIA 機は 3090 で埋まるが両系統の再検証は続く）、⑷ ROCm wheel はライセンスの裏取りが取れない。**コードに ROCm 分岐が 0 行**なので、手動手順は `overrides-rocm.txt` 3 行＋AMD index の差し替えだけで将来も成立する（`local-additions/` が実例）。

CUDA の版＝`cu128` は行き止まり。**cu126（2,470 MiB・ドライバ ≥560.76）か cu130（1,781 MiB・≥580 相当）**は A4 便（3090 キット）の実測で裁定する前提だった＝実測は完了。**cu126・cu130 とも 3090（ドライバ 591.86）で pin のまま導入・動作を確認済み**（`24`・`25`・`27`）。推奨＝**配布 689 MiB・導入後 1.3 GB 軽い cu130 を既定**とし、古いドライバ層（560.76〜579）には cu126 の手動手順を残す。cu130 の bf16 短文 40 steps に出た異常値（N:\ 直走行で 1.496 s・ウォームがコールドより遅い）は**ローカル SSD の再走で消えた**（0.808 s／RTF 0.215・`27`）＝**cu130 既定の条件は満たされた**。cu126 も同じ SSD で再走して cu130 と同値（bf16 短文 0.801 vs 0.808）＝速度は版の選択に効かない。

**⒝（DirectML）だけがこの問い自体を消す**＝⒝の最大の存在理由。

---

## 8. 未確認・要 1 射の一覧

| # | 事項 | 状態 | 誰が埋めるか |
|---|---|---|---|
| U-1 | Sony/SilentCipher **重み**のライセンス／Aratako 3 repo に LICENSE 檔なし／HF 本文の SAM 記述 | 原文に記載なし・写し忘れ | 卓（回避＝初回取得・MIT 自作） |
| U-2 | 法解釈 3 点＝MSVC「licensed Visual Studio users」の Community 版の読み／LGPL §6 の a〜e／DirectML MSLT の受容。`nvperf_host.dll`・`zlibwapi.dll`・`nvrtc64_130_0.alt.dll` の EULA 上の扱い | 未取得・未確認 | 卓／A1 |
| U-3 | **多 GPU 実機での `cuda:N` の実効**（`cuda:1` が別 GPU に載る・`HIP_VISIBLE_DEVICES=1` の再番号付け・`empty_cache` の 0 番誤爆）。範囲外 index の文言・落ちる場所・可視化変数・model/codec 分割は**司令官機で実射済み**（`29`・§10-2）。CUDA ビルドの `uuid`／`pci_bus_id` は 3090 で確認済み（`33`）。NVIDIA での範囲外 index の逐語は未 | 本機・3090 機とも 1 GPU＝2 枚機の実挙動だけ未 | 実装便で 2 枚機が出たとき（現有機では不可） |
| U-4 | CUDA 実測＝**cu126・cu130・ONNX 両 EP とも回収済み・cu130 は SSD で再走し異常値は解消**（`24`・`25`・`27`）。cu126 も SSD で再走し同値 | 解消 | — |
| U-5 | DirectML（AMD）＝RTF は実測済み（0.749）。3090 の端から端までは実測済み（DirectML 0.527 透かし抜き／CUDA EP 0.291・`28`）。残る＝**fp16 の DiT が GPU EP 全般（DirectML・CUDA EP）で壊れる原因 op の特定**（直れば AMD で RTF 0.52）・透かしの DFT 抜き再 export と DML 実測・全グラフ同時常駐の VRAM・長文の DML 実測・`speaker_encoder` 手術版の厳密照合。fp16 の CPU EP が 11 倍遅かった件は ORT の版差（1.24.4 vs 1.29.0）で確定＝札は外した（`26`） | 未実施 | ⒝採用時（1〜2 便） |
| U-6 | 起動直後のスパイク（同文連射でも `EMPTY_CACHE=0` で **40.8 s／19.4 s**・未見形状で 3〜6 倍）の根治／SSE 分割で喋りが伸びる件の耳検分。**`40` で機構を確定**＝罰の鍵は出力潜在フレーム数 S_lat（40 ms 刻み）・出る段は畳み込み 3 段（復号・透かし・参照符号化）だけ・MIOpen db はディスク永続（機体初見 +3〜10 s・以後はプロセス内 +0.2〜0.6 s）・`seconds` 段階化で有限化・`ref_latent` で参照長の罰は消滅・同一形状連射は 1 発目だけ遅い（bf16 2.8 s／fp32 32 s）＝3 発目 19.4 s は in-process で再現せず未確認。**bf16 の A/B・10 steps 対 40 steps・DirectML 対 torch・ROCm 対 CUDA・CPU 対 CUDA は司令官が 9 対を聴取し「どれも耳で差がわからない・配信中ならなおさら」（`35`）＝閉じた** | 一部残 | 実装便 |
| U-7 | `IRODORI_COMPILE_MODEL`＝**CUDA でも失敗が確定**（`TritonMissing`・`24`・`25`・`27` の 3 回とも。CPU は `cl` 不在）。Windows 用 triton の入手可否のみ未確認 | 確定（使わない） | — |
| U-8 | 不要 ≈500 MiB を外した embed 配置で起動するか／配布経路（2.1 GiB の資産上限）／⒝（ORT・C# 単体）が `vc_redist` を要するか（DLL の PE 直読 1 手）。**embed の `._pth` 細部は解消**（`30`・§10-1＝`._pth` だけで起動・`PYTHONPATH` 無視・`dist-info` 要）。`msvcp140.dll` 欠落機の実挙動は再現不能（本機は再頒布済み） | 一部残 | A1／⒝採用時（B3） |
| U-9 | チャンク分割込みの seed 決定性／同時 2 本の交互配車の実順序／`num_candidates>1` | Fake では決まらない | 実装便 |
| U-10 | AMD 統合 doc の Windows 専用 limitations／torch の `device-all` extra／ROCm wheel のライセンス一次資料 | 未確認 | — |
| U-11 | 配信者限定の GPU ベンダ統計・日本市場比 | 未確認（Steam 全世界のみ） | — |
| U-12 | INT8 量子化・opset 23 Attention 融合・KV 入力版 ONNX・speaker_inversion 経路の export | 未実施 | ⒝採用時 |
| U-13 | Microsoft.ML.Tokenizers の byte_fallback バグの上流報告 | 未報告 | ⒝採用時（自前 171 行なら不要） |
| U-14 | cu126 wheel が実際に 527.41 級の古いドライバで動くか（minor version compatibility は「limited feature-set」）／forward compatibility の Windows 可否 | 未検証 | A1（古いドライバの実機なし＝卓） |
| U-15 | soundfile 同梱 libsndfile の LGPL 対応（a〜e）＝U-2 と同じ | — | 卓 |

---

## 9. 付録

### 9-1 上流 URL（BRIEF §0 の確定事項）

- `https://github.com/Aratako/Irodori-TTS`（v4.1-Small モデルカード README:16 バッジ・:30）。clone HEAD `8224daf`（2026-08-11）。
- `https://github.com/Aratako/Irodori-TTS-Server`（モデルカードには無く Irodori-TTS README:9 から）。clone HEAD `841fb7c`（2026-08-02）。
- HF：`Aratako/Irodori-TTS-v4.1-Small`（推奨）／`Aratako/Irodori-TTS-v4-Small`（Server 既定）／`Aratako/Semantic-DACVAE-Japanese-32dim`／`sony/silentcipher`／`facebook/dacvae-watermarked`。

### 9-2 ノート一覧（`lab/notes/`）

| # | 檔 | 担当 |
|---|---|---|
| 01 | prereq-probe | 前提資料（実測側）・API 契約 44 欄（5 パス 8 オペレーション）・ROCm 化手順 21 操作 |
| 02 | prereq-canon | 正典・要求・本体アダプタ・ParamKind・前例 |
| 03 | arch-acoustic | DiT・RF・duration・諸元・ONNX 段別判定 |
| 04 | arch-frontend-backend | 正規化・トークナイザ・DACVAE・SilentCipher |
| 05 | params-inference | SamplingRequest 43 欄（ノート内の 45 は誤り）・caption 文法・絵文字 45 種 |
| 06 | server-api | エンドポイント・IrodoriOptions 44 欄・SSE・キャンセル・voices |
| 07 | device-selection | 決定点の表・`cuda:N`・方式 A/B |
| 08 | install-deps | 導入手順・依存木・venv 実測・embed 化 |
| 09 | license-chain | 鎖の表・時系列・逐語引用集 |
| 10 | gpu-landscape | Steam・AMD・ORT EP・wheel サイズ・検証手段 |
| 11 | lab-setup | lab venv・CPU 基準・torchcodec の門・onnx 依存衝突 |
| 12 | onnx-codec-watermark | DACVAE・SilentCipher の export と照合（透かし全経路 2.7 MiB） |
| 13 | onnx-dit | DiT ステップ・duration・条件符号化・全ループ照合・fp16 |
| 14 | server-contract | TestClient 実射・確定契約 T-1〜T-12 |
| 15 | tokenizer-dotnet | C# 前段 231 行・108,430 件一致・NFKC 0 差・ML.NET バグ |
| 16〜19 | verify-* | 敵対検分（ライセンス・GPU 事情・コード主張・正典/probe） |
| 20 | gpu-baseline-live-probe | ROCm bf16 常駐値・実サーバ実射（no_ref・SSE）・DML venv |
| 21 | cuda-kit | RTX 3090 機向けキット（`N:\irodori-native-research\kit-cuda\`） |
| 22 | runtime-license | 実行系（torch/CUDA/cuDNN/MSVC/embed/DirectML/libsndfile/ROCm）の再配布条件 |
| 23 | directml-amd | DirectML EP の実測（Radeon 8060S）＝RTF 0.749・fp16 破綻・後処理 3 種・同時 Run の segfault |
| 24 | rtx3090-cu126 | RTX 3090 の CUDA 実測（cu126・第 1 回収）＝bf16 RTF 0.192／0.079・fp32 正常・compile 失敗・DirectML 45 ms・CUDA EP は DLL 読込失敗（ベンチ側を修正） |
| 25 | rtx3090-cu130-onnx | 第 2 回収＝cu130・ONNX の CUDA EP（DiT 30.6 ms・透かし可・TF32）・DirectML 手術版・fp16 は GPU EP 全般で破綻・N:\ 直走行の所要の正体 |
| 26 | onnx-e2e-kit | ⒝の端から端まで計測キット（CUDA EP／DirectML・DiT 実ループを session 分割・numpy のみ）＝本機 DML 2.69〜2.82 s／RTF 0.72〜0.75 で検証・最終潜在は参照とビット一致・ORT CUDA EP は DL 1,227 MiB・fp16 CPU EP の 11 倍遅は ORT 版差 |
| 27 | rtx3090-ssd-cu130 | 第 3 回収＝軽量キット `kit-ssd` を 3090 機のローカル SSD で再走・全段 6.2 分・cu130 の異常値は消滅（bf16 短文 40 steps 0.808 s／RTF 0.215）・cu126 も SSD で再走し cu130 と同値・CUDA の基準値を SSD 値に |
| 28 | rtx3090-onnx-e2e | 第 4 回収＝⒝の端から端まで（3090・`26` のキット）＝CUDA EP 1.094 s／RTF 0.291・DirectML 1.982 s／0.527（透かし抜き＝ORT 1.24.4 ではロード不可）＝判断則不合格は両機で確定・fp16 破綻 4 回目・DML の潜在は参照と 1.9e-5 |
| 29 | gpu-designation-lab | 司令官機で Server 19 走（8091）＝範囲外 index は `.to()`（`inference_runtime.py:657`／`codec.py:78`）で落ちる・落ちる場所は PRELOAD が決める・綴り違いの逐語・HIP／CUDA_VISIBLE_DEVICES 有効／ROCR 無効・fp32 の黙った CPU 転落 267 s・`/health` は生 echo・model/codec 分割 2.2 倍・`uuid`／`pci_bus_id` |
| 30 | embed-and-gpu-control-facts | 埋め込み Python 3.12.10 で Server 起動を実射（`._pth` のみ・PRELOAD 28.2 s）・壊れる前提はほぼ無し（残る穴 4 つ）・`IRODORI_` 50 欄・ready 2 段・停止は kill・`CUDA_DEVICE_ORDER` 既定 FASTEST_FIRST・DirectML `device_id` は DXGI 順・安定鍵は UUID／PCI |
| 31 | reference-voice-cost | 参照音声のコスト＝CFG 枝 3→4（+20.6 %）・話者トークン数は秒に比例（10 s 63／30 s 188）・符号化は秒に比例（CPU 10 s 1.17 s／30 s 3.35 s・97 % が DACVAE encoder）・キャッシュ無し・チャンクごと再符号化・上限 120 s・`ref_latent` の逃げ道・GPU 台本・追記＝ROCm 実測（最悪 RTF 0.387） |
| 32 | ref-bench-kit | 3090 用の参照ボイス計測キット（VOICEVOX 4 話者＋COEIROINK 1 話者×10 s／30 s・no-ref 対照・`--with-latent`＝事前計算潜在の経路・出力 wav 保存）。CPU 検証で `prepare_reference` 1,175→9.5 ms（10 s）／3,808→8.3 ms（30 s）＝`ref_latent` の効きを実測・トークン数の式を実測照合 |
| 33 | rtx3090-ref-voices | 第 5 回収＝3090 で参照ボイス 5 話者×10 s／30 s（bf16 40／10 steps・fp32・長文）＝bf16 40 steps で +0〜13 %・全条件 RTF < 0.5（最悪 0.39）・増分は参照符号化 41〜117 ms のみ・`ref_latent` で増分消滅・fp32 では DiT が伸びる・CUDA に初見罰なし・出力 wav 32 本。追記＝`gpu_props.json`（CUDA ビルドの torch にも `uuid`・`pci_bus_id` あり・`nvidia-smi` は System32・`-L` 0.04 s） |
| 34 | rocm-environment-detection | ROCm の環境判定の材料（主席・司令官機）＝`rocm-bootstrap`（PyPI・純 Python・Windows は `clinfo` 経由）で `gfx1151` を 1.5 s・AMD 索引の Windows device wheel は RDNA1〜4 に並ぶ・難しいのは判定後の保証（実測は gfx1151 のみ・ドライバ要件未取得・ROCm 固有の挙動） |
| 35 | listening-result | 司令官の耳検分（`listening` 9 対）＝「AB どちらも耳では差がわからない・配信中ならなおさら」＝bf16 A/B・10／40 steps・DirectML／torch・ROCm／CUDA・CPU／CUDA が閉じた。最も効く帰結＝10 steps を認めるなら⒝は両機で判断則を通る（steps は卓）。追記＝参照ボイスの出力は「非常に似ている」 |
| 36 | handoff-install-ui | 本体の暫定要求①②③⑤⑥＝埋め込み配布の条件 4 つ・Server に UI 資産 0・gradio_app は index 不可で env を読まない・VOICEVOX との対応表（足りないのは話者列挙の利用だけ）・CPU×bf16 は起動時 ValueError（本体定数の見込み外れ）・「暗黙の GPU」は原文上必然・CUDA 版切替 3 案・起動主体 4 択 |
| 37 | handoff-contract | 要求④⑥⑦⑧＝/openapi.json は出るが既定 0・範囲 0（enum 3 本のみ）・既定値は env 22 欄で動く・話者名の日本語は檔直置きと voices.json alias では通り登録 API は ASCII 限定・VoiceSpec はパスと no_ref だけ・「デフォルト」は alias の no_ref で成立・no_ref は voice を無効化・上流の穴 3 つ・現行アダプタとの差分 D-1〜D-8・追記＝日本語パスの参照で実合成 OK |
| 38 | handoff-decisions | 要求と事実の衝突 X-1〜X-18・本体が決めること 26 項＋①②（推奨つき）・新リポの受け入れ条件（数値）・未確認の仕分け（止める 4 件＝法解釈・配布経路・cu126 古ドライバ・起動主体）・透かしは同梱しないだけで切れる・Server 既定は配布向きでない |
| 40 | rocm-warmup | Radeon 版の暖機の材料（司令官機・in-process）＝罰の鍵は S_lat 1 つ（40 ms 刻み）・出る段は畳み込み 3 段・二層（MIOpen db 永続 +3〜10 s／プロセス内 +0.2〜0.6 s）・`seconds` 3 段で有限化（尺は指定秒ちょうど）・`ref_latent` で参照長の罰消滅・暖機 26.2 s（3 段）／39.7 s（12 段）・bf16 固定で起動直後 2.8 s 対 fp32 32 s |

### 9-3 lab の成果物

- `lab/.venv`（CPython 3.12.14・torch 2.10.0+cpu・onnx 1.22・onnxruntime 1.29）、`lab/.venv-dml`（onnxruntime-directml 1.24.4）。
- `lab/bin/`＝再現スクリプト（`infer_cpu.py`・`infer_gpu.py`・`gpu_inproc.py`・`live_probe.py`・`export_*.py`・`10_rope_check.py`〜`90_dyn_spk.py`・`wm_full_onnx.py`・`server_contract.py`・`tok_*.py`・`nfkc_diff.py`）。
- `lab/onnx/`＝`dit_step.onnx`（1.37 GiB）・`dit_step_fp16.onnx`（700 MiB）・`duration.onnx`（83 MiB）・`encode_conditions_sdpa.onnx`（1.42 GiB）・`speaker_encoder.onnx`（234 MiB）・`dacvae_{encoder,decoder}_fp{32,16}.onnx`・`silentcipher_watermark_fp32.onnx`（2.7 MiB）・`sc_istft_slim.onnx`（38 KiB）・各 `*.inputs.json`・DirectML 手術版 `*_az0.onnx`（4）・`*_dml.onnx`（2）。
- DirectML の再現＝`lab/bin/23_*.py`（21 本）・`40_loop_dml.py`・`23_gpu_counters.ps1`・生データ `lab/out/23_*`（59 檔）。
- `lab/dotnet/TokProbe/`＝`HfUnigramTokenizer.cs`（物理 171 行・実効 149）・`IrodoriTextNormalization.cs`（物理 110 行・実効 82）。
- `lab/kit-cuda/`（`21`＝根 14 檔・567 KiB＋同梱 uv.exe 40 MiB＋`onnx-e2e/` 22 檔・1.66 MiB＝`26`＋`ref-bench/` 14 檔・12 MB＝`32`＋`gpu-props.cmd`／`gpu_props.py`（GPU の UUID／PCI 列挙・`nvidia-smi` の所在）。`copy-kit-to-ssd.cmd`／`sync-results.cmd`／`README-ssd.md` は `27` の kit-ssd 経路）。`N:\irodori-native-research\kit-ssd\`＝63 檔・7.7 GB（uv キャッシュ・venv を含まない軽量版・`27`）。`N:\irodori-native-research\kit-cuda\` にはこれに加えて `onnx/`（`dit_step` fp32/fp16・`duration`・`encode_conditions_sdpa`・`dacvae_decoder` fp32/fp16・`silentcipher_watermark`・`speaker_encoder`＋各 `*.inputs.json`＝24 檔・≈4.1 GiB）を置いた＝3090 機で CUDA EP／DirectML EP のベンチが自動で走る。
- `lab/out/`＝基準 wav（sha256 `678d2384…`）・照合 JSON・`server_contract.json`（102 件）・`18-field-diff.json`・`gpu_*.json`・`live_*.json`・`verify17/`・`26_*`（e2e キット検証の生データ 6 檔）・`29/`（GPU 指定の実射 101 檔）・`31_*`（参照音声の CPU／ROCm 実測）・`refvoices/`（VOICEVOX 4 話者＋COEIROINK 1 話者×10 s／30 s＋manifest）・**`3090-results-0609/`・`3090-results-cu130-1604/`・`3090-results-ssd-cu130/`・`3090-results-ssd-cu126/`・`3090-results-ssd-ref/`（3090 機 cu126／cu130 NAS 走行／cu130・cu126 SSD 再走＋e2e 両 EP／参照ボイス 4 走＋出力 wav 32 本の回収物一式）**。
- 再現の環境変数一式＝`11-lab-setup.md` §10。

---

## 10. 追加の問い（司令官・報告後）＝python なし配布の細目・複数 GPU 時の指定・参照音声のコスト

根拠＝`29`（司令官機で Server を 19 走・8091・全ケース逐語）・`30`（埋め込み Python で Server を実際に起動・原文 URL つき）・`31`（参照音声の経路と CPU 実測）。**設計・UI は決めない**＝事実と、卓が決める論点だけ（§10-4）。

### 10-1 python なし配布＝⒜は「見込み」から「実射で成立」へ

| 論点 | 事実 | 根拠 |
|---|---|---|
| **起動の実射** | 公式 embed **3.12.10** を展開し、`python312._pth` に site-packages・`Irodori-TTS`・`Irodori-TTS-Server/src` の 3 パスを書いただけで、**`import site` なし**に torch 2.10.0+cpu・transformers・soundfile・Server が読め、`python.exe -m irodori_openai_tts --port 8092` が起動して `/health` 200。`PRELOAD=true` でも `runtime loaded in 28.16s` | `30` §0-3 |
| **壊れる前提** | `sys.executable` 再実行・`multiprocessing`・`subprocess`（ffmpeg 以外）・`pkg_resources`＝**grep 0 件**。`importlib.metadata` は量子化 checkpoint の**保存**時だけ（`irodori_tts/quantization.py:243`） | `30` §1-A |
| **残る穴 4 つ** | ① `msvcp140.dll`（embed zip に無い・torch が要求＝`torch/__init__.py:237-240`）→ vc_redist。② **CWD 相対**の `.env`（`config.py:13`）と `voices_dir`（`config.py:41`・**起動時に mkdir**＝読取専用の導入先だと起動で落ちる→ `IRODORI_VOICES_DIR` を絶対パスで）。③ **`*.dist-info` の同梱が要る**（transformers が `importlib.metadata.version()` を呼ぶ＝package フォルダだけの写しは壊れる）。④ `__pycache__` の書込失敗は**沈黙して続行**（CPython `_bootstrap_external.py:1232-1244`） | `30` §1-A・§6-1 N-7・N-8 |
| **`._pth` の実挙動** | `isolated=1／no_site=1／ignore_environment=1` になり **`PYTHONPATH` は無視**。ただし `PYTHONUTF8`・`PYTHONIOENCODING`・`PYTHONDONTWRITEBYTECODE`・`PYTHONWARNINGS`・`IRODORI_*`・`HF_*` は**効く**＝**パスは `._pth` 専管、それ以外は env** | `30` §1-B・N-2 |
| **本体が握る制御面** | CLI は `--host/--port/--reload` の 3 つだけ（`__main__.py:17-21`）。設定は `IRODORI_` **50 欄**（`config.py:18-72`・既定値つき一覧＝`30` §2-1）。env 名は大小無視・device 値の前後空白は素通し・空白だけなら `auto`。`IRODORI_CORS_ORIGINS` は **JSON 配列必須**（カンマ区切りは `SettingsError` で起動即死） | `30` §2-1・N-3・N-4 |
| **準備完了の検知** | 2 段＝`PRELOAD=true` は**読込完了までポートが開かない**（実測 26 s refused → 200）／`false` は 4〜5 s で 200 だが `runtime.loaded=false`。**ready 待ちのタイムアウトは 30 s では足りない**（CPU 28 s・NAS ならさらに） | `30` §2-3・N-5 |
| **停止** | **kill のみ**（graceful エンドポイント無し・ルート 7 本）。`asyncio.shield`（`app.py:781`）は **SSE だけ**＝切断されても合成は走り切る | `30` §2-4 |
| **ログ** | access ログは stdout（uvicorn）、他は stderr の `INFO:irodori_openai_tts.*`。stdout はバッファされて順序が狂う（silentcipher の生 `print`・`silentcipher/server.py:473` に flush 無し）＝本体の取り込みは stderr が本命 | `30` §2-5・N-6 |
| **1 プロセス 1 デバイス** | `runtime.py:28,31-34,48-61`＋`inference_runtime.py:1487-1496`＝**多 GPU 同時利用はプロセス複数・ポート複数** | `30` §2-6 |
| **残る未確認** | `msvcp140.dll` 欠落機での実挙動（本機は再頒布済みで再現不能）・不要 ≈500 MiB を外した配置で起動するか・書込不可フォルダ・`.env` と env の優先順・Ctrl+C で進行中の合成を待つか | `30` §6-2 V-5〜V-10 |

→ **答え＝「利用者機に Python を入れない配布」は⒜で実射済みに成立。条件は vc_redist・絶対パスの `voices_dir`・`dist-info` 同梱・パスは `._pth`／設定は env、の 4 つ。**「プロセス内にも Python を置かない」は⒝のみ（§6-3・速度で保留）。

### 10-2 複数 GPU 時の指定＝可能（3 条件）・落ち方は実射で確定

**答え＝可能。ただし ⑴ Server 経由（gradio・infer.py は env を読まない）⑵ `IRODORI_MODEL_DEVICE` と `IRODORI_CODEC_DEVICE` の 2 本を必ず ⑶ `IRODORI_PRELOAD=true`（掴み損ねを起動時に爆発させる）。** 2 枚機の実射は依然 U-3（本機・3090 機とも 1 枚）だが、「範囲外を指したときにどこでどう落ちるか」「可視化変数」「model/codec 分割」「掴んだ GPU の確認手段」は司令官機で実射した（`29`）。

| ケース（`29` §1-1・全 19 走・ROCm・8091） | 結果 | 檔:行・逐語 |
|---|---|---|
| `IRODORI_MODEL_DEVICE=cuda:0` | 200・ウォーム 0.757 s（index 指定は env に書くだけで通る） | `29` G2 |
| **範囲外 `cuda:1`**（1 枚機） | サーバ層でもランタイムの関門でも**検査されない**。落ちるのは**重みを読み終えて載せる瞬間**＝`inference_runtime.py:657` `model.to(model_device)`（6.07 s 後）／codec なら `codec.py:78`（11.28 s 後）。逐語＝`torch.AcceleratorError: CUDA error: invalid device ordinal / GPU device may be out of range, do you have enough GPUs?` | `29` §1-2 |
| 落ちる場所 | **`PRELOAD=true`＝起動時に死ぬ**（`Application startup failed. Exiting.`・exit 3・ポートは開かない）／**`false`＝起動は成功し全合成が 500**（torch の生文が body にそのまま出る）。中間なし | `29` §1-3 |
| 綴り違い | `cuda:-1`／`cuda:abc`／**`"cuda:0 "`（末尾空白）**＝`Invalid device string: …`、`CUDA:0`／`gpu`＝`Expected one of cpu, cuda, ipu, xpu, …`。`_resolve_device` が原文を返す（`runtime.py:102`）ので空白も大文字もそのまま torch へ | `29` §1-2 |
| **可視化変数** | **`HIP_VISIBLE_DEVICES` は Windows ROCm で効き、`CUDA_VISIBLE_DEVICES` も HIP／ROCR が未設定なら効く**（HIP が優先・`07` §5(c) の「hip 分岐で上書き」は誤り＝訂正）。**`ROCR_VISIBLE_DEVICES` は個数しか見ず GPU を隠せない**。HIP と ROCR が食い違うと `RuntimeError` で、`is_available()` は True なのに `device_count()` が例外 | `29` §2-1 |
| **GPU が消えたときの既定（auto）** | **精度で真っ二つ**＝bf16 は起動時 `ValueError: precision='bf16' currently requires CUDA or XPU device.`（見える）。**fp32 は黙って CPU に落ちて 200**＝2 文字・10 steps で **266.974 s**（GPU 0.78 s の約 340 倍）、`/health` は `"auto"` のまま。**bf16 既定は速度だけでなく安全装置** | `29` §2-2 |
| **掴んだ GPU の確認** | **HTTP からは不可**＝`/health` は設定の**生 echo**（`"cuda:0 "` も `"gpu"` もそのまま）。起動ログに device は 1 文字も出ず、合成ごとの `[runtime] start synthesize model_device=…` も env 文字列の echo（`inference_runtime.py:1053`）＝`auto` は `cuda` としか出ず index 不明。**上流に穴** | `29` §4 |
| **model と codec を別デバイス** | 成立＝`cuda:0`＋`cpu` はウォーム 1.712 s（全 GPU 0.777 s の 2.2 倍・透かしが codec 側に張り付き 30.8→247 ms）、`cpu`＋`cuda:0` も 200 だが 168 s（10 steps）。**跨デバイスの受け渡しは `codec.py:266` の 1 行だけ**＝改造ゼロで別 GPU 分割が可能（2 枚機では未実射） | `29` §3 |
| VRAM | kill で完全解放（3,868 → 7,993 → 3,865 MB） | `29` §6-2 |

**index の安定性＝「GPU を N 番で覚える」設計は原文レベルで壊れる**（`30` §3）

| 番号体系 | 順序 | 原文 |
|---|---|---|
| CUDA の `cuda:N` | **`CUDA_DEVICE_ORDER` 既定 `FASTEST_FIRST`**（速さ順の発見的順序）。`PCI_BUS_ID` で PCI 順に固定できる | NVIDIA CUDA Programming Guide 5.2（環境変数） |
| `nvidia-smi` の Index | ドライバの自然列挙順。NVIDIA 自身が「**enumeration ordering is not guaranteed to be consistent between reboots**」と明記＝安定鍵は **UUID か PCI bus ID** | nvidia-smi manual |
| DirectML（⒝）の `device_id` | **`IDXGIFactory::EnumAdapters` 順**。「主ディスプレイ＝0 番が最速とは限らない」と原文が明言 | ORT DirectML EP docs |

- 揃える手段＝`CUDA_DEVICE_ORDER=PCI_BUS_ID` か **UUID／PCI bus ID での照合**。torch は `get_device_properties(i).uuid` を持ち、`CUDA_VISIBLE_DEVICES` に UUID を書く形も解する（torch 2.10・lab `.venv` の行番号で `torch/cuda/__init__.py:895-948`）。ROCm 実機では `pci_bus_id`・`pci_device_id`・`pci_domain_id` も取れた（`29` §5）。**CUDA ビルド（3090・torch 2.10.0+cu130）でも `uuid` と `pci_bus_id`／`pci_device_id`／`pci_domain_id` が全部ある**（`33` 追記＝`gpu_props.json`。stub `_CudaDeviceProperties` の欠落は stub 側の不備）。torch の `uuid` `19adfe89-…` は `nvidia-smi -L` の `GPU-19adfe89-…` と一致（接頭辞 `GPU-` の差だけ）、PCI は `0000:01:00.0` ↔ domain 0／bus 1／device 0 で照合できる＝**NVIDIA でも AMD でも torch 側で UUID・PCI の両方が取れる**。
- 列挙の費用＝`import torch` が支配的（3090・SSD 1.2〜1.8 s／embed＋CPU torch 2.65〜4.13 s／ROCm 4.37 s・`device_count()` 自体は 0.2 ms）に対し素の embed python は 133〜315 ms。**`nvidia-smi` は Windows では `C:\WINDOWS\system32\nvidia-smi.EXE`（PATH 上）で、`-L` は 0.04 s**（`33` 追記・出力＝`GPU 0: NVIDIA GeForce RTX 3090 (UUID: GPU-19adfe89-…)`、`--query-gpu=index,name,uuid,pci.bus_id,driver_version --format=csv` も可）。NVIDIA 専用。C# の DXGI 列挙はベンダ非依存だが **CUDA とは別の番号体系**。
- NVIDIA と AMD＝`cuda:N` の書き方は同じ（ROCm 分岐 0 行）。違うのは可視化変数名だけ（`CUDA_VISIBLE_DEVICES` vs `HIP_VISIBLE_DEVICES`＝Windows 推奨・ROCm 原文）。Intel Arc（xpu）は index 拒否（`inference_runtime.py:70-71`）。
- ⒞の薄い層の検査は 1 つに集約できる＝trim → `^(cpu|cuda(:\d+)?)$` の白名簿 → `device_count()` で範囲検査。**上流が 6〜11 秒かけて落とすものを 0 秒で弾ける**。
- 置き場の選択肢（挙げるだけ・`30` §5）＝ランチャがプロセス env に載せる／配布物の `.env`（CWD 相対）／bat の `set`（前例が CRLF・UTF-8 で無言で壊れた）／本体の設定から起動時に組む／UUID を保存して起動時に index へ解決。
- 残る未確認＝2 枚機の実挙動全般（`cuda:1` が別 GPU に載る・`HIP_VISIBLE_DEVICES=1` の再番号付け・`empty_cache` の 0 番誤爆）・NVIDIA での逐語・`ROCR` が 2 枚機で何をするか（`29` §8）。

### 10-3 参照音声（サンプルボイス 10〜30 s）を当てたときの応答（`31`）

**これまでの速度表は全部 `--no-ref`（CFG バッチ 3）の値**（`13`・`20`・`23`・`24`・`25`・`27`・`28`）。参照ありの増分は二層。

| 層 | 事実 | 檔:行 |
|---|---|---|
| **参照の符号化**（参照秒数に比例） | `_load_audio` → DACVAE **encoder** → `ReferenceLatentEncoder`。CPU 実測 10 s **1.17 s**／30 s **3.35 s**、その **97 % は DACVAE encoder** | `31` §2・§5-4 |
| **DiT の増分** | ⑴ **CFG 枝 3→4**（speaker CFG の既定が正）＝CPU 実測 **+20.6 %**（S_lat を揃えた対照）。⑵ 話者トークン数は **`floor(round(秒×25)/4)+1`**（10 s→63・30 s→188・参照なし 2）＝joint attention の鍵長 864→925→1,050（`model.py:401`・+7 %／+22 %）。⑵は⑴より小さい | `31` §4 |
| **端から端まで（CPU・fp32・40 steps・短文）** | 参照なし **14.53 s** → 10 s **17.46 s（+20 %）** → 30 s **19.81 s（+36 %）**。RTF 3.86→5.20→6.04 | `31` §5-2 |
| **キャッシュ** | **一切ない**＝同じ voice でも毎要求で全段が走る。**チャンク分割すると 1 チャンクごとに参照を符号化し直す**（`app.py:657`／`:710` が text しか差し替えない）。1 合成で参照 encoder は 2 回走る（`inference_runtime.py:1281`・`rf.py:220`） | `31` §3 |
| **長さ上限** | `max_ref_seconds`＝**120 s**（checkpoint 値・`inference_runtime.py:842-846`）。超過は**頭から**切り詰め＋warning。ランダム切り出しは無い。上流推奨＝「同一話者の短いクリップ複数・合計 30 s 程度で飽和」（`docs/parameters.md:59-63`）＝**10〜30 s は推奨レンジの真ん中・切り詰めは起きない** | `31` §1-3 |
| **逃げ道** | **参照潜在（`.pt`）を事前計算して `voices/` に置く**＝符号化費の 97 % が消える（`voices.py:184-185`・サーバ無改造）。Speaker Inversion（`*.speaker.safetensors`）なら参照 encoder ごと消える（CFG 枝 4 は残る）。`speaker_cfg_scale=0` で枝 3 に戻る（似せ具合と引き換え） | `31` §3 |
| 副作用 | **参照を当てると予測尺が短くなる**（94.3→83.8→82.4 フレーム）＝同じ本文で出力長が変わる＝尺合わせ・字幕同期の仕様に関わる | `31` §8 |

**GPU 実測（司令官機 ROCm・8060S・bf16・40 steps・warm 最良走・参照＝VOICEVOX ずんだもんの 30 s 素材から切り出し・`31` 追記）**

| 本文 | 参照 | 出力 s | 端から端まで s／RTF | 参照符号化 ms | sample_rf ms | 増分（時間） |
|---|---|---|---|---|---|---|
| 短文 | なし | 3.80 | **0.922／0.243** | 0 | 761 | — |
| 短文 | 10 s | 3.16 | **1.048／0.332** | 136 | 769（+1 %） | **+0.13 s（+14 %）** |
| 短文 | 30 s | 3.20 | **1.239／0.387** | 328 | 782（+3 %） | **+0.32 s（+34 %）** |
| 長文 | なし | 10.96 | **1.753／0.160** | 0 | 1,452 | — |
| 長文 | 10 s | 10.72 | **2.079／0.194** | 132 | 1,644（+13 %） | **+0.33 s（+19 %）** |
| 長文 | 30 s | 10.88 | **2.360／0.217** | 325 | 1,730（+19 %） | **+0.61 s（+35 %）** |

- **答え＝落ちるが、GPU なら RTF 0.5 を割らない**（最悪の短文＋30 s でも 0.387）。増分の内訳＝参照符号化（10 s 130 ms／30 s 330 ms・秒に比例・DACVAE encoder が 9 割・参照 encoder 自体は 9 ms 級）＋DiT の増分（短文は +1〜3 %・長文は +13〜19 %＝小バッチではカーネル起動が支配し、CFG 枝 4 も鍵長の増分もほぼ埋もれる）。**短文の RTF 上昇の一部は出力が短くなる副作用**（3.80→3.16 s）。
- **ROCm 固有の罠**＝参照の長さごとに初見形状の罰が出る（短文 30 s の cold 4.14 s・長文 10 s の cold **19.4 s**・30 s 13.0 s）＝DACVAE encoder の新しい入力長で MIOpen の調整が走る。CUDA では 3090 の cold 罰が軽い（`27`）ので同程度にはならない見込み＝3090 実測で確認。VRAM ピークは短文で +700 MB・長文では +90 MB＝3,030 MB で頭打ち。
**GPU 実測（RTX 3090・cu130・実機 VOICEVOX 4 話者＋COEIROINK 1 話者×10 s／30 s・キット `32`・warm 中央値・5 話者の幅・`33`）**

| 条件 | 参照なし | 実ファイル 10 s | 実ファイル 30 s | 事前計算潜在 10 s／30 s |
|---|---|---|---|---|
| bf16 40 steps 短文（3.76 s） | **0.866 s／RTF 0.230** | 0.87〜0.95 s（+0〜10 %）／0.27〜0.32 | 0.96〜0.98 s（+11〜13 %）／0.29〜0.34 | 0.85〜0.92（−2〜+6 %）／0.85〜0.90（−2〜+4 %） |
| bf16 40 steps 長文（10.96 s） | **1.232／0.112** | 1.22〜1.42（−1〜+15 %）／0.12〜0.15 | 1.21〜1.64（−2〜+33 %）／0.13〜0.16 | — |
| bf16 10 steps 短文 | **0.340／0.090** | 0.36〜0.42（+6〜23 %）／0.11〜0.15 | 0.41〜0.46（+21〜34 %）／0.13〜0.14 | — |
| fp32 40 steps 短文 | **0.927／0.247** | 0.98〜1.19（+6〜28 %）／0.31〜0.35 | 1.09〜1.26（+18〜36 %）／0.34〜0.39 | — |

- **答え＝落ちるが小さい**。bf16 40 steps で +0〜13 %・**全条件で RTF 0.5 未満**（最悪＝fp32 30 s の 0.39・bf16 は 0.34）。3090 の見積り（0.95〜1.05 s）は実測 0.87〜0.98 s で上振れの見積りだった。
- **増分はほぼ参照符号化だけ**（bf16）＝10 s 41〜52 ms・30 s 105〜117 ms（ROCm の 1/3）。`sample_rf` は 740〜800 ms で参照の有無に依らない。**fp32 では DiT が伸びる**（820→821〜1,024 ms＝CFG 枝 4 が計算律速で表に出る）＝bf16 既定の理由が 1 つ増えた。
- **事前計算潜在（`ref_latent`）で増分は消える**（`prepare_reference` 1 ms・−2〜+6 %）＝逃げ道は GPU でも成立。ただし GPU の節約は絶対値 40〜115 ms＝CPU 経路（1〜4 s）ほどの値打ちは無い。
- **CUDA には ROCm のような参照長ごとの初見罰が無い**（cold 0.88〜1.46 s＝参照なし cold 1.095 s と同級）。
- 出力尺の変化（短文 3.76→2.84〜3.40 s・長文 10.96→9.28〜11.24 s）が RTF と長文の増分の幅（−2〜+33 %）の主因。10 steps では参照符号化の比率が上がる（+6〜34 %）が RTF ≤ 0.15。話者差・サンプルレート差（COEIROINK 44.1 kHz）はノイズ幅の中。
- **聴感＝司令官が出力 wav（`results-ssd\ref-wav\` 32 本）を元音声と聴き比べ「非常に似ている」**（2026-09-04・`35` 追記）＝10〜30 s の参照で声は取れる。参照なし（`no-ref`）は Irodori 自身の声（キャプションのみ）。
- ⒞の層の候補に「参照潜在のキャッシュ」が立つ（`06` §8 の「⒞で自前に建てるもの」に 1 項の候補・採るかは 10-4 の 13）。

### 10-4 卓が決めること（追加分）

9. **GPU の同定を何で持つか**＝index（再起動で変わりうる・⒜と⒝で体系が違う）か UUID／PCI bus ID か。
10. **掴んだ GPU の可視化**＝上流に `/health` 1 行の提案を出すか、層が起動時に 1 発焼いて所要で判定するか、層が別プロセスで torch 列挙するか。
11. **`PRELOAD=true` 既定と ready 待ちのタイムアウト**（30 s では足りない・CPU 28 s）。
12. **fp32 の黙った CPU 転落を層で塞ぐか**（bf16 既定を安全装置として使うか）。
13. **参照潜在のキャッシュを⒞の層に持つか**（毎要求・毎チャンクの再符号化を止める最も効く手。`ref_embed`・`chunking_enabled:false` も選択肢）。
14. **判断則の steps（40 か 10 か）と配信の既定 steps**＝司令官の耳検分（`35`）で 10 と 40 に差が聞き取れなかった。10 steps を配信品質と認めるなら、⒜は 3090 で 0.300 s／RTF 0.080・参照ありでも ≤ 0.15、⒝の DirectML は両機で ≤ 0.5（§6-3）＝⒝の保留が「復活」に変わりうる。CPU は 10 steps でも RTF 1.79 で未達のまま。

### 10-5 ROCm の環境判定は難しいか（司令官の追加の問い・`34`）

**答え＝「判定」そのものは AMD の道具で数秒・自動化できる。難しいのは判定後の保証（対応表の維持と挙動）で、§7-2 の「CUDA のみで出し、ROCm は司令官機だけの手動手順」の根拠は変わらない。**

| 段 | 事実 | 根拠 |
|---|---|---|
| 判定の道具 | **`rocm-bootstrap` 0.2.0**（PyPI に単体・純 Python 172 KB・依存なし）。Windows では AMD ドライバ同梱の **`clinfo`**（`C:\WINDOWS\system32\clinfo.exe`）を呼んで gfx 名を返す。司令官機で `rocm-bootstrap-detect.exe` → **`gfx1151`・1.5 s**。`hipInfo.exe`（`rocm-sdk-core`）でも同じ。上書き＝`ROCM_BOOTSTRAP_FORCE_GFX_ARCH`／`ROCM_BOOTSTRAP_DISABLE_DETECTION` | `34` §要点 1〜2 |
| 対応表 | AMD 索引（`whl-next`）の `amd-torch-device-*` は gfx1010〜1036（RDNA1/2）・gfx1100〜1103／110x（RDNA3）・gfx1150〜1153／115x（RDNA3.5）・gfx1200／1201（RDNA4）に並び、win_amd64 は確認した 6 系列で各 15 本。`rocm-sdk targets` も同じ並び | `34` §要点 3 |
| 手順 | ① DXGI／WMI で vendor=AMD → ② `rocm-bootstrap` で gfx 名 → ③ 索引に win wheel が有るか → ④ `torch[device-<gfx>]==2.13.0+rocm10.0.0`（DL 1.1〜1.3 GB・展開 ≈3.5 GB）を入れて bf16 で 1 発焼く。**①〜③は数秒・④だけが重い** | `34` §要点 4・`17` |
| **難しさの正体** | ⑴ 実測があるのは gfx1151 だけ＝索引に wheel があることと動くことは別。⑵ Windows の ROCm ドライバ要件の原文は未取得（司令官機＝AMD 32.0.31041.1004）。⑶ ROCm 固有の挙動＝fp32 復号の病的遅さ（bf16 必須）・初見形状の罰（3〜12 倍・参照長ごとに最大 19 s）・GPU が見えないと fp32 は黙って CPU 転落。⑷ AMD 2 枚（iGPU＋dGPU）機は `clinfo` が両方列挙し 0 番の決まり方が未確認。⑸ `whl-next` は公式手順に載る先行チャネルで版が動く | `34` §要点 5・`20`・`29`・`31` |
| 自動化するなら | ⒜の導入便に「検出 → device wheel 選択 → 試し撃ち」の 1 段が足される（B 系・1 便級・未検証 arch の検分は含まず）。採るかは卓（§7-2 の裁定に紐づく） | — |

未確認＝`rocm-bootstrap` を AMD 以外・`clinfo` 無しの機体で走らせたときの戻り／gfx1151 以外の実挙動／ドライバ要件の原文。

**裁定（2026-09-04・司令官）＝ROCm は未保障。ただし Strix Halo（gfx1151）のみ確認済みの使えるパスは残す（① 同一配布物に残す／② 別途 Radeon 用リリース・§7-2）**＝自動判定（本節の道具）は持たず、利用者が Radeon（Strix Halo）を明示的に選ぶ形。本節は材料として残す。
