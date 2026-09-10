# licenses/ — 索引（何を・どこから・いつ・原文か自作か）

> **この索引と実檔は 1 対 1 で一致していなければならない**（受け入れ条件 A-6・突合＝
> `build/check-licenses.ps1`）。檔を足したら**必ずこの表に 1 行足す**。
>
> **2 系統ある**＝
> ⑴ **`licenses/` に置くもの**＝配布物（Release 資産＝ランチャ・`server/`・`ledger/`・`licenses/`）に
> 実際に入る物、および**初回取得で入るが上流に許諾文が存在しない物**（＝配布側が用意しないと
> 誰も持たない物）。
> ⑵ **`first-run-notices.md`**＝初回取得で利用者の機体に入る第三者物の通知。**配布物に入れる**
> （`decisions.md` 46）＝初回取得の UI が**取得を始める前に**表示する檔だから、取得の前から手元に
> 無ければならない。`decisions.md` 8 が配布物から締め出すのは**第三者物そのもの**（バイナリ・
> wheel・重み）であって、その通知文ではない。
>
> **停止域**＝**ライセンスの結論を推測で断定しない**（`decisions.md` 22）。
> 解釈が割れる箇所（§4・`first-run-notices.md` §C）は**原文の檔と行だけ**を記す。

---

## 1. 索引（実檔の全件）

| 檔 | 対象 | ライセンス | 原文か自作か | 入手元 | 取得日 | sha256 / バイト |
|---|---|---|---|---|---|---|
| `README.md` | この索引 | — | 自作 | — | — | — |
| `irodori-tts/LICENSE` | 上流コード `Aratako/Irodori-TTS`（pin `8224daf`） | **MIT**（`Copyright (c) 2026 Aratako`） | **原文**（`upstream/Irodori-TTS/LICENSE` の写し） | submodule（`https://github.com/Aratako/Irodori-TTS.git`） | 2026-09-04 | `dfd47cc99fd79cced8e0cab04ed81f70b3d4f2f475a75746b341671bd3991c00` / 1,064 B（LF）。**git blob hash `9224ba992ec5d21d4c0985e2b511df18cce56600` が submodule の `HEAD:LICENSE` と一致**＝byte 同一の証明 |
| `irodori-tts-server/LICENSE` | 上流コード `Aratako/Irodori-TTS-Server`（pin `841fb7c`） | **MIT**（同上・上と逐語同一） | **原文**（`upstream/Irodori-TTS-Server/LICENSE` の写し） | submodule | 2026-09-04 | 同上（2 本は sha256 一致） |
| `irodori-tts-v4.1-small/LICENSE.md` | モデルの重み `Aratako/Irodori-TTS-v4.1-Small` | **MIT（宣言）** | **MIT 全文は自作**＋**宣言の出典を併記**＋**Ethical Restrictions 4 条は原文** | 宣言＝HF モデルカード（`raw/main/README.md`:2・:69-82）。**LICENSE 檔は HF に存在しない（404）** | 2026-09-04 | 本檔 §2 に出典表 |
| `semantic-dacvae-japanese-32dim/LICENSE.md` | コーデックの重み `Aratako/Semantic-DACVAE-Japanese-32dim` | **MIT（宣言）** | **MIT 全文は自作**＋**宣言の出典を併記** | 宣言＝HF モデルカード（`raw/main/README.md`:2・:29）。**LICENSE 檔は HF に存在しない（404）** | 2026-09-04 | 本檔 §2 に出典表 |
| `silentcipher/LICENSE` | 透かしの**コード** `sony/silentcipher`（上流の pin は fork `SesameAILabs/silentcipher@d46d7d0893a583d8968ab3a6626e2289faec9152`） | **MIT**（`Copyright (c) 2024 Sony Research Inc.`） | **原文** | `https://raw.githubusercontent.com/sony/silentcipher/master/LICENSE` | **2026-09-04**（UTC 13:57） | `157b6af8c1bc4eac646abdf17473934a85e8815ade3a346cd83a58a171586a73` / 1,075 B。**pin commit の LICENSE と sha256 一致**（`research/lab/notes/16-verify-license.md` (f) の実測値と一致） |
| `dacvae/LICENSE` | コーデックの由来 `facebookresearch/dacvae` | **Apache-2.0** | **原文** | `https://raw.githubusercontent.com/facebookresearch/dacvae/main/LICENSE` | **2026-09-04**（UTC 13:57） | `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30` / 11,358 B。**5 リビジョン全部で sha256 一致**（`research/lab/notes/16` (a)）。**§4 も読むこと** |
| `irodori-tts-for-yomiwakechan/LICENSE` | **本リポジトリの自作分** | **MIT**（`Copyright (c) 2026 mugonkun`） | **自作** | — | 2026-09-04 | — |
| `first-run-notices.md` | 初回取得で利用者の機体に入る第三者物の通知 | — | 自作（逐語は `research/lab/notes/22` から） | — | 2026-09-04 | **配布物に入れる**（初回取得 UI が取得前に表示＝`decisions.md` 46） |
| `dotnet/README.md` | ランチャの exe に焼かれる第三者物の記帳（**裁定 51 の例外**） | — | 自作 | — | 2026-09-05 | — |
| `dotnet/dotnet-runtime-LICENSE.txt` | .NET ランタイム 10.0.11（`Microsoft.NETCore.App.Runtime.win-x64`・SelfContained publish で 1 exe に焼かれる） | **MIT**（`Copyright (c) .NET Foundation and Contributors`） | **原文** | NuGet の runtime pack（`microsoft.netcore.app.runtime.win-x64/10.0.11/LICENSE.TXT`） | 2026-09-05 | `d7a68596ab69b06f51ca278a6545148e4269a9381c26d597c13df5d88e08cf5b` / 1,139 B |
| `dotnet/windowsdesktop-runtime-LICENSE.txt` | WPF／WinForms ランタイム 10.0.11（`Microsoft.WindowsDesktop.App.Runtime.win-x64`・同上） | **MIT**（同上） | **原文** | NuGet の runtime pack（`microsoft.windowsdesktop.app.runtime.win-x64/10.0.11/LICENSE`） | 2026-09-05 | `a89886665765362eb77e0f8e26602c924520041d1711b2eedc136434fe4d01ab` / 1,137 B |
| `dotnet/dotnet-runtime-THIRD-PARTY-NOTICES.txt` | .NET ランタイムが同梱する第三者物の通知 | 各記載 | **原文** | NuGet の runtime pack（同 10.0.11 の `THIRD-PARTY-NOTICES.TXT`） | 2026-09-05 | `6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21` / 78,041 B |

**檔は 13 件**（この `README.md` を含む）。`build/check-licenses.ps1` はこの表の行と実檔を突合する。
`dotnet/` の 4 件は**便 D（ランチャ）が足した**＝裁定 51 で SelfContained publish（1 exe）が確定し、
.NET ランタイム（MIT）が成果物に焼かれるようになったため。**版が上がったらこの 4 行も更新する**
（`licenses/dotnet/README.md` §1 に版の読み方を書いた）。NAudio 2.2.1 の許諾全文が未取得である旨は
同 §3 に欠落として記帳してある。

---

## 2. なぜ重み 2 件だけ「MIT 全文が自作」なのか

**上流の HF リポジトリに LICENSE 檔が無いから**である（推測ではなく実測）。

| repo | `tree/main` の檔 | LICENSE |
|---|---|---|
| `Aratako/Irodori-TTS-v4.1-Small` | `tokenizer/`・`.gitattributes`・`EMOJI_ANNOTATIONS.md`・`README.md` 6,950 B・`model.safetensors` 3,064,295,596 B | **無し**（`raw/main/LICENSE` → HTTP 404「Entry not found」） |
| `Aratako/Semantic-DACVAE-Japanese-32dim` | `.gitattributes`・`README.md` 5,128 B・`weights.pth` 429,620,065 B | **無し**（同 404） |
| `Sony/SilentCipher` | `16_khz/`・`44_1_khz/`・`.gitattributes`・`README.md` 7,794 B・`config.json` 51 B | **無し** |

さらに、**重みを取得しても README すら手元に来ない**＝上流は
`allow_patterns = ["model.safetensors", "tokenizer/*"]`（`inference_runtime.py:559`）と
`hf_hub_download(filename="weights.pth")`（`codec.py:72`）で明示的に絞り込む
（ローカル HF キャッシュの実測でも README・LICENSE は 0 件）。

⇒ **MIT の必須条件（著作権表示＋許諾文の同梱）を満たす `licenses/` は、配布側が自作するしかない。**
上流自身はそれをしていない＝**上流の慣行に倣うだけでは MIT を満たさない**
（`research/lab/notes/09-license-chain.md` §4・`16-verify-license.md` §4-3）。

**自作した部分の内訳**（正直に）＝MIT の定型本文と、**著作権表示の行**。
著作権表示はモデルカードに書かれていないため、上流のコード側 LICENSE（`Copyright (c) 2026 Aratako`）に
倣って補った。**この 1 行だけが原文でない**ことを各檔の冒頭に明記してある。

---

## 3. Ethical Restrictions（重みの利用条件）の所在

- 原文（逐語 4 条）＝`irodori-tts-v4.1-small/LICENSE.md` §3、および `README.md`（リポジトリ直下）§4。
- 出典＝`Aratako/Irodori-TTS-v4.1-Small` の `README.md`:69-82。
  `Aratako/Irodori-TTS-v4-Small` の `README.md`:183-196 と**当該節が sha256 一致**（`63b5ad44…`）
  ＝v4 と v4.1 で逐語同一（`research/lab/notes/16-verify-license.md` (e)）。
- **観測された事実**（解釈ではない）＝4 条に **watermark／透かし の語は 1 つも無い**。
  3 つのモデルカードに `commercial`／`redistribut`／`prohibit` の語も **1 語も無い**（同 §4-8）。

---

## 4. dacvae の「SAM License」の記述について（裁定 29＝**当方の読み**・材料は原文のみ）

**同じプロジェクトについて、場所によって違うライセンス名が書かれている。**

**司令官は 2026-09-04 に「Apache-2.0 と読む」と裁定した**（`decisions.md` 29・3 対 1）。
**これは当方の読みであり、上流が明示したものではない。**（`README.md` §4 にも同じ旨を併記した。）
以下は**観測した檔と行**である＝裁定の材料であって、席の解釈ではない
（`decisions.md` 22 の停止域・`research/report/irodori-native-handoff-2026-09-04.md` §3 の 2）。

| # | 場所 | 檔:行 | 逐語 |
|---|---|---|---|
| 1 | GitHub `facebookresearch/dacvae` の **LICENSE 実体** | `LICENSE`:2-4 | `                                 Apache License` / `                           Version 2.0, January 2004` / `                        http://www.apache.org/licenses/`（11,358 B・Apache-2.0 全文。**本文中に "SAM" も "Meta" も無く、著作権者名が埋まっていない**） |
| 2 | GitHub `facebookresearch/dacvae` の **現行 README** | `README.md`:67 | `This project is licensed under Apache-2.0 - see the [LICENSE](LICENSE) file for details.` |
| 3 | GitHub `facebookresearch/dacvae` の **初回 commit の README**（`d39b77d589`） | `README.md`:43 | `This project is licensed under the SAM License - see the [LICENSE](LICENSE) file for details.` |
| 4 | HF `facebook/dacvae-watermarked` の **カード本文** | `README.md`:46 | `This project is licensed under the SAM License - see the [LICENSE](LICENSE) file for details.` |
| 5 | HF `facebook/dacvae-watermarked` の **メタデータ** | `README.md`:2 | `license: apache-2.0` |
| 6 | HF `facebook/dacvae-watermarked` の **LICENSE 檔** | — | `raw/main/LICENSE` → **HTTP 404**「Entry not found」＝**カード本文が指す LICENSE 檔は HF 側に存在しない** |
| 7 | GitHub API `license` | — | `{"key":"apache-2.0","spdx_id":"Apache-2.0","name":"Apache License 2.0"}` |

**観測された経過**（`research/lab/notes/09-license-chain.md` §1-2・`16-verify-license.md` §2-1〜§2-2）

- LICENSE 檔の実体は **5 リビジョン全部で sha256 一致**（`cfc7749b96f63bd3…`・11,358 B）で、
  `path=LICENSE` の commit は **1 件のみ**＝**初回から一度も変わっていない**。
- GitHub 側 README は commit `34e0b0dbb6`（**2025-12-19 13:20 UTC**・"Update license in README"）で
  "SAM License" → "Apache-2.0" に**変更された**（差分は README.md 1 檔・+1/-1 のみ）。
- HF カードは同日 17:25 UTC の更新（`8680102d14`）で frontmatter の `license: apache-2.0` を戻したが、
  **本文 46 行目の "SAM License" は変更されていない**。
- **日付の注意**＝初回 commit `d39b77d` の author date は 2025-10-03 だが、**committer date（GitHub に
  載った日）は 2025-12-10 16:53:31 UTC**。したがって「dacvae の初回 commit は SAM License
  （SAM 3 の LICENSE は 2025-11-19 版）より前だから指しようがない」という時系列の論証は**成り立たない**
  （`research/lab/notes/16-verify-license.md` (a')＝REFUTED）。**この点で強い言い方をしないこと。**

**本リポジトリが置いているのは 1 の原文**（`dacvae/LICENSE`）である。
なお本ソフトウェアは **dacvae の重みを再配布しない**（初回取得＝`decisions.md` 8）ので、
配布物に入るのは `facebookresearch/dacvae` の**純 Python のコード**（初回取得）だけである。

---

## 5. `Sony/SilentCipher` の「重み」について（唯一の空白）

- `licenses/silentcipher/LICENSE` は **コードの MIT** である。
- **重み（68 MB）の配布条件は、原文のどこにも書かれていない**＝
  HF `Sony/SilentCipher` は **`cardData` が `null`**（license メタデータ無し・tags にも `license:*` 無し）、
  本文 `README.md`:135 は
  > `- The code in this repository is released under the MIT license as found in the [LICENSE file](LICENSE).`
  ＝**"the code" としか言っておらず重みに触れていない**。`tree/main` に LICENSE 檔も無い。
  GitHub の README・Releases にも重みの条件を述べた文は無い（`research/lab/notes/16` §4-8 (ii)）。
- ⇒ **本ソフトウェアは重みを再配布しない**（初回取得＝上流の `get_model()` が
  `snapshot_download(repo_id="sony/silentcipher")` で自動取得する＝手数は増えない）。
  **これは回避であって、条件が判明したわけではない。**

---

## 6. プリセット話者（同梱する参照ボイス）の権利

**司令官の確認（`decisions.md` 18・2026-09-04）**＝

> **権利**（司令官確認済み）＝上記エンジンの生成ボイスは個人利用・商用・有償でも再配布可
> （元々そういう目的の商品）。本ソフトは無償配布なので問題なし。
> ※ 席は推測で断定しない＝各エンジンの規約原文の所在は `licenses/` の台帳に司令官の確認日付と共に記す。

**席は上の判断を追認も反証もしない**（規約の読みは司令官の管掌）。ここに記すのは、
**どのエンジンの規約をまだ取得していないか**という事実だけである。

### 6-1 プリセット話者 13 名と、由来のエンジン（`decisions.md` 17・**裁定 118**）

| # | 話者 | 由来のエンジン |
|---|---|---|
| 1 | 琴葉茜（関西弁） | VOICEROID2 |
| 2 | 弦巻マキ 英語（英語分解読み上げ） | CeVIO AI |
| 3 | 吉田くん | VOICEROID2 |
| 4 | 月読アイ | VOICEROID2 |
| 5 | 月読ショウタ | VOICEROID2 |
| 6 | ちび式じい | VOICEVOX |
| 7 | もち子さん（セクシー／あん子） | VOICEVOX |
| 8 | つくよみちゃん | COEIROINK |
| 9 | KANA（ないしょばなし） | COEIROINK |
| 10 | MANA（いっしょうけんめい） | COEIROINK |
| 11 | おふとんP（きざ） | COEIROINK |
| 12 | おふとんP（のーまる v2） | COEIROINK |
| 13 | シャンパンコール（ホスクラ） | **無し＝司令官提供の録音**（エンジン由来ではない） |

うち**同梱されるのは 12 名**＝2 番（弦巻マキ 英語）は `decisions.md` 27 で飛ばした（`status: skipped`）。

**作り方**（同 17）＝各エンジンで一次 wav を生成 →「！」「？」絵文字などを付与した本文で
既存 Irodori-TTS-Server（8088）を参照ボイスとして二次ボイスを生成 → **二次ボイスをテンプレート
参照ボイスにする**。**一次 wav は同梱しない**（同梱するのは二次ボイスだけ）。
**13 番だけは作り方が違う**（**裁定 118**・2026-09-10）＝司令官が「既に2次生成」の wav を
そのまま渡した＝**エンジンも席の生成も通っていない**。台帳では `engine: "external"`・`primary: null`・
`secondary.text_id`／`text`／`irodori` も `null` で、代わりに `provenance` が出所（渡した人・日・元の檔の路）を持つ。
**権利は 6-2 の表の外**＝司令官が提供したという事実だけが根拠で、**席は録音の出所を確かめていない**
（誰がいつ録った物か・元の権利者が誰か・第三者の声が入っていないか）。行の `rights_note` に同じ文がある。
生成は**便 P**（`decisions.md` 26＝一次 wav は存在せず席が生成する。弦巻マキ英語は司令官が後で提供＝同 27）。

### 6-2 規約原文の所在（**未取得＝便 P で確認する**）

| エンジン | 規約原文の所在 | 状態 |
|---|---|---|
| VOICEROID2（琴葉茜・吉田くん・月読アイ・月読ショウタ） | 各キャラクターの権利者が定める利用規約（AHS／株式会社の各サイト） | **未取得＝便 P で確認**。URL も本便では特定していない。**キャラ 4 名それぞれ別に確認が要る** |
| CeVIO AI（弦巻マキ 英語） | CeVIO AI および弦巻マキの各利用規約 | **未取得＝便 P で確認** |
| VOICEVOX（ちび式じい・もち子さん） | VOICEVOX の利用規約と、**キャラクターごとの個別規約**（VOICEVOX はキャラごとに条件が違う） | **未取得＝便 P で確認**。キャラ 2 名それぞれ別に確認が要る |
| COEIROINK（つくよみちゃん・KANA・MANA・おふとんP ×2） | COEIROINK の利用規約と、**話者ごとの個別規約** | **未取得＝便 P で確認**。話者 5 件それぞれ別に確認が要る |

**この表は「規約を読んで可と判断した」という意味ではない。**
本便（便 A）は**規約原文を 1 件も取得していない**。`decisions.md` 18 の司令官確認だけが根拠であり、
**席は推測で断定しない**（同 18 の但し書き・`decisions.md` 22 の停止域）。

### 6-3 同梱する二次ボイスの索引（**機械可読な正本＝`voices/presets.json`**）

配布物に入るのは **`voices/presets/<id>.wav` だけ**である（一次 wav は入れない＝6-1）。
12 檔のうち 11 檔は席が作った**二次ボイス**、1 檔（`ext_hostclub_champagne.wav`）は
**司令官が提供した録音そのもの**である（**裁定 118**・`engine: "external"`）。
**その 1 件ずつの由来・権利の記録は `voices/presets.json` が正本**であり、この檔に表を写して
二重管理にしない（便 P が生成を続けている間、写しは必ず腐る）。1 行が持つ欄＝

| 欄 | 意味 |
|---|---|
| `id`・`display_name` | 話者 id と表示名（＝`voices/presets/<id>.wav` の檔名） |
| `engine`・`engine_speaker`・`style` | 一次 wav を作った音声合成エンジンと話者・スタイル。**`engine: "external"` の行はエンジンが無い**（裁定 118） |
| `primary.file` | 一次 wav の檔名（**配布物には入れない**・保全先は `presets.json` の `notes`）。**`external` の行は `primary` が `null`** |
| `provenance` | **外部提供の出所**＝渡した人・渡した日・元の檔の路（**`external` の行だけが持つ印**・裁定 118） |
| `secondary.file` | 同梱する二次ボイスの檔名＋長さ・sample rate・生成に使った本文と `irodori` 設定 |
| `rights_note`・`rights_confirmed_by`・`rights_confirmed_at` | `decisions.md` 18 の司令官確認（**確認者と日付**） |
| `rights_source_fetched` | **規約原文を席が取得して読んだか**。6-2 のとおり現在は全行 `false` |

**`build/check-licenses.ps1` がこの突合を機械で行う**＝⑴ `voices/presets/` の各 `*.wav` に
`presets.json` の行があること（無ければ FAIL）、⑵ 各行に `rights_confirmed_by` と
`rights_confirmed_at` があること（無ければ FAIL）、⑶ `rights_source_fetched` が false の行が
1 件でもあれば **WARN**（＝Release の前に 6-2 を埋める必要がある、という印）。
**`engine: "external"` の行はこの WARN が永久に消えない**＝6-1 のとおり**権利は 6-2 の表の外**であり、
取得すべき規約原文が無い（根拠は司令官が提供したという事実だけ）。WARN の一覧に
`ext_hostclub_champagne.wav` が出るのは正しい状態である（裁定 118）。

### 6-4 便 P への申し送り

1. 上表の 4 エンジン・12 名について、**規約原文の URL・取得日・該当条項の逐語**をこの節に書き足す。
2. **二次ボイス（Irodori-TTS で生成した参照ボイス）を同梱・再配布してよいか**を、各規約の
   「生成音声の利用」「学習・音声合成への利用」条項に照らして確認する（**一次 wav を素材として
   別の音声合成の参照に使う**という使い方は、規約が明示していないことがある）。
3. **クレジット表記の要否**（多くの規約が生成音声の公開時にクレジットを求める）を確認し、
   要るなら本リポジトリの `README.md` と配布物の UI に載せる。
4. 確認の結果、同梱できない話者が出たら**その話者だけ落とす**（12 名は目標であって条件ではない）。
5. Irodori-TTS 側の **Ethical Restrictions 1（No Impersonation）** にも同時に照らすこと
   （§3・実在人物の声を同意なくクローンしない）。これは各エンジンの規約とは別の条項である。
