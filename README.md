# irodori-TTS for 読み分けちゃん

**Windows のパソコンに入れて開くと、書いた文をその場でしゃべってくれるアプリです。**
Python も pip も、黒い画面も要りません。同梱の 12 人の声から選んで、文を打って、鳴らす。それだけの道具です。
配信ソフト「読み分けちゃん2」を使っている人は、このアプリを開いておけばコメントの読み上げに使えます。

> **無料・非公式のソフトです。**もとになった音声合成 Irodori-TTS の作者である Aratako 氏とは関係がありません。
> **うまく動かないときも、Aratako 氏へは問い合わせないでください。**
> 連絡先は X の **[@yomiwakechan](https://x.com/yomiwakechan)** です。

---

## 0. 使う人はここから

| | |
|---|---|
| **落とす** | 公式ページ https://mugonkun.github.io/irodori-tts-for-yomiwakechan/ |
| **使い方** | [`docs/guide.md`](docs/guide.md)（入れ方・はじめの準備・しゃべらせ方・困ったときは・消し方） |
| **知らせる** | X の [@yomiwakechan](https://x.com/yomiwakechan) |

- お使いのパソコンに合う方を選びます＝**RTX（CUDA）**（NVIDIA の GeForce・RTX などのパソコンの方）／
  **Radeon（ROCm）**（AMD の Radeon のパソコンの方。動作を確かめられているのは
  Ryzen AI MAX+ 395 ／ Radeon 8060S のパソコンで、ほかの Radeon では試していません）。
- **はじめて開いたときに、動かすための一式（約 5.3 GB）をダウンロードします**（10 分ほど）。
  ほかの方が作ったプログラムや音声モデルを勝手に配って回らない方針なので、そのぶんをあなたのパソコンへ入れます。
  一度すませば、次からはすぐ使えます。
- **必要なもの**＝Windows 11 / 10（64 ビット）・NVIDIA か AMD のグラフィックス・空き 12 GB ほど・
  はじめの準備のときだけネット接続。入れ終わったあとにこのアプリが使うのは約 8 GB です。
- この版の案内＝[`docs/release-notes/v2.0.1.md`](docs/release-notes/v2.0.1.md)。

---

## 1. これは何をするアプリか

**VOICEROID2 や VOICEVOX と同じ感覚で置いておける、日本語の音声合成アプリ**です。
開発者向けのツールキットでも、玄人向けの道具立てでもありません。

- **Python は要りません。** 入れて、開いて、打つだけです。
- **12 人の声**が最初から入っています。自分の音声ファイル（wav）から声を増やすこともできます。
  追加した声は、読み分けちゃん2 からも同じ名前で選べます。
- **開けば使えます。**「起動」を押す釦はありません。窓を閉じればアプリは終わり、グラフィックスのメモリも返ります。
- **読み分けちゃん2 の読み上げにも使えます。** このアプリを開いておくだけで、読み分けちゃん2 の側が見つけます。
  こちら側で必要な操作はありません（アプリを閉じると読み上げも止まります）。
- **RTX（CUDA）と Radeon（ROCm）は別のアプリです。** 同じパソコンに両方入れられますが、設定も、追加した声も、
  ダウンロードした物もそれぞれ別に持ち、同時には開けません。片方を消しても、もう片方はそのまま使えます。

**誰のための物か**＝Python を入れられない／入れたくない人。配信者。声を使いたいだけの人。
**誰のための物ではないか**＝Python 環境を自分で組める人（その人は Irodori-TTS-Server をそのまま使えばよい）。

**注意**

- グラフィックスが無いパソコンでも動きますが、**とても遅い**ので配信には使えません。
- 出した音声には、**AI が作った音であるという目印**（人には聞こえません）が入ります。外す方法はありません。
- **実在する人の声を、本人の許しなくまねる目的では使えません**（もとになった音声合成の利用条件＝下の §4）。
- 消すときは、そのアプリが自分の場所に置いていた物（ダウンロードした一式・取り込んだ声・設定・記録）が
  すべて一緒に消えます。**声を追加するときに選んだ、あなたの音声ファイルには触れません。**

くわしい使い方・困ったときの手引きは **[`docs/guide.md`](docs/guide.md)** にあります。

---

> **ここから下は作る側の帳面です**（設計・契約・ライセンス・保守の記録）。
> 利用者向けの案内は上の 2 つ＝**公式ページ**と **`docs/guide.md`** にあります。
> 上流のコードは `upstream/`（submodule）で版を固定して**無改変**のまま持ち、必要な差分は `patches/`
> にだけ置きます（正典＝`decisions.md` 裁定 3）。
> 着地頁の実体＝`site/index.html`（`gh-pages` 枝の根に写して GitHub Pages で出す・`site/README.md`）。
> Release の一覧（過去の版・`SHA256SUMS.txt`）＝https://github.com/mugonkun/irodori-tts-for-yomiwakechan/releases
> 過去の版のリリース文の棚＝[`docs/release-notes/`](docs/release-notes/)（**記録なので書き替えない**。
> 旧い語彙＝「CUDA 版」「gfx1151」「5.26 GiB」がそのまま残っている＝§0 から指さないのはそのためである）。

---

## 2. 導入の流れ（概略）

> 便 D（ランチャ）と便 E（インストーラ）の実装で**確定した**。**確定手順は `docs/install.md`**（本節はその概略・
> **作る側の帳面**＝配布物には入らない）。利用者向けの手引きは `docs/guide.md`（**配布物に入る 1 檔**）。
> 受け入れ条件は「利用者操作 ≤ 6（vc_redist を黙って通せれば ≤ 5）」（`docs/acceptance.md`）＝
> **いまの規則は 4 押下**（v2.0 段 B）＝同意チェック・同意して次へ・準備を始める・発話テストへ。
> 実測は RTX 機の 1 周で採る（`docs/design/v2-plan.md` 段 H の 2）。**改訂前の実測＝5 押下**。

1. Release からインストーラ（ランチャ＋埋め込み Python の取得台帳＋自作分・数十 MB）を落として実行する。
2. インストーラが `%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda\`（Radeon（ROCm）版は
   `…\irodori-tts-ywk-radeon\`）に本体を置く（per-user・管理者権限なし）。**アプリ名も版で分かれる**＝
   「irodori-TTS for 読み分けちゃん － RTX（CUDA）」／「… － Radeon（ROCm）」
   （`decisions.md` 109 の名札を v2.0 段 F-1 が改めた＝`docs/design/charter.md` §6-2）。
   2 本は同じ機体に**同居できる**（v2.0 からは置き場も設定も共有しない＝`decisions.md` 133）。
3. 初回起動でランチャが**実行系とモデルを取得**する（合計 ≈5.4 GiB・100 Mbps 級で 10 分以内が目標）。
   取得先は**版ごとの樹**＝`%LOCALAPPDATA%\irodori-tts-ywk-cuda\`／`…\irodori-tts-ywk-radeon\`
   （`runtime\<variant>\`・`models\`・`voices\`・`logs\`＝`decisions.md` 133 ⑶・
   利用者向けの綴りは `docs/guide.md` §8）。
   - 実行系＝torch は `download.pytorch.org` の版固定 URL、依存は PyPI、いずれも sha256 で検証。
   - モデル＝Hugging Face（revision 固定）。
   - `vc_redist.x64.exe`（Microsoft 公式・≈25 MB）＝torch が `msvcp140.dll` を要求するため必須。
4. **動かし方はアプリが決める**（`decisions.md` 130 Q1＝はじめの準備の道の上に選ぶ段は無い）＝
   ドライバ 580 以上なら cu130・528.33 以上 580 未満なら cu126・Radeon 版は rocm-gfx1151。
   決めた結果は 1 行で告げ、変える口は 設定 › 詳細 › 動かし方 にだけ残す（`decisions.md` 126）。
5. **起動の釦は無い**（`decisions.md` 130 Q2）＝アプリを開くと帯が「準備しています…」→「使えます」と
   進み、`127.0.0.1:18088` で待ち受ける。読み上げの準備完了まで最長 120 秒。
   **ランチャの窓を閉じるとサーバも一緒に落ちる**（`decisions.md` 124）。
6. 本体（読み分けちゃん2）側は、配布版のエンジンを有効にするだけ。

**オフライン導入は用意しません**（`decisions.md` 8）。第三者バイナリ（wheel・exe・dll・モデル）を
配布物に入れない方針のためです。

利用者向けの手引き＝`docs/guide.md`（**配布物に入る**＝`{app}\docs\guide.md`。〔このアプリについて〕の
〔使い方を見る〕が開くのもこれ）。作る側の確定手順＝`docs/install.md`（**配布物には入らない**＝v2.0 段 F-2）。

---

## 3. 檔の並び

| 場所 | 中身 |
|---|---|
| `decisions.md` | **正典**。裁定はここが正。 |
| `docs/contract.md` | 配布版が本体に約束する HTTP 契約（本体が写す一枚）。 |
| `docs/acceptance.md` | 受け入れ条件（数値）。 |
| `docs/install.md` | 導入手順（**確定**・**作る側の帳面**＝利用者には指さない）。 |
| `docs/guide.md` | **利用者向けの使い方**（配布物に入る・本文の正本＝`docs/design/v2-copy.md` §6）。 |
| `docs/radeon.md` | Radeon（ROCm）版の注記（gfx1151 で確認済みの事実だけ・**作る側の帳面**＝v2.0 段 F-2 で配布物から外した）。 |
| `docs/design/` | 便ごとの設計書。 |
| `upstream/` | 上流 2 本の submodule（**無改変**・`Irodori-TTS` `8224daf`／`Irodori-TTS-Server` `841fb7c`）。 |
| `patches/` | ビルド時にだけ当てる差分（submodule には当てない）。 |
| `server/` | wrapper（`ywk_server.py`・`/params`・`/ywk/status`・白名簿・エラー整形）。 |
| `ledger/` | 取得台帳（URL・sha256・size）。第三者バイナリの代わりに置く唯一の正本。 |
| `build/` | ビルド台本（PowerShell）。 |
| `tests/` | 契約テスト。 |
| `licenses/` | ライセンス（詳細は §4）。 |
| `launcher/`・`installer/`・`probe/` | 便 D・E・実機検分（便 A では README のみ）。 |
| `research/` | 調査便の写し（**読むだけ**・事実の出典）。 |

---

## 4. ライセンス

**このリポジトリで新たに書いた部分（自作分）は MIT ライセンス**です
（`licenses/irodori-tts-for-yomiwakechan/LICENSE`）。

配布物に同梱するものと、**初回取得で利用者の機体に入るもの**の 2 系統があり、それぞれの許諾文を
`licenses/` に揃えています。索引＝**`licenses/README.md`**（何を・どこから・いつ取ったか・原文か自作か）。

- 上流 Irodori-TTS / Irodori-TTS-Server＝**MIT**（`Copyright (c) 2026 Aratako`）＝原文をコピー。
- 重み `Aratako/Irodori-TTS-v4.1-Small`・コーデック `Aratako/Semantic-DACVAE-Japanese-32dim`＝
  モデルカードで **MIT を宣言**しているが、**HF のリポジトリに LICENSE 檔が無い**（実測・404）。
  そのため MIT 全文はこちらで用意し、**宣言の出典（カードの URL・行・確認日）を併記**している。
- `silentcipher`（sony/silentcipher・**MIT**）と `dacvae`（facebookresearch/dacvae・**Apache-2.0**）＝
  GitHub の原文を取得して置いている（取得 URL と日付は `licenses/README.md`）。
  **dacvae について**＝HF `facebook/dacvae-watermarked` のカード本文 46 行目には
  `This project is licensed under the SAM License ...` の記述が残っている。本ソフトウェアは
  GitHub 側の LICENSE 実体（Apache-2.0 全文・**全リビジョンで sha256 一致**）を採る＝
  **これは当方の読みである**（`decisions.md` 29 の裁定・観測した檔と行の全件は `licenses/README.md` §4）。
- **初回取得で入る第三者物**（torch・NVIDIA CUDA/cuDNN の DLL・libsndfile・vc_redist・Python 本体など）
  の通知＝`licenses/first-run-notices.md`。この通知文は**配布物に入っており、初回取得の UI が
  取得を始める前に表示します**（`decisions.md` 46）。配布物に入れないのは**第三者物そのもの**
  （バイナリ・wheel・モデルの重み）です。

**このソフトウェアは libsndfile（LGPL-2.1）と libsoxr（LGPL-2.1-or-later）を使用します**
（`soundfile` パッケージが同梱する `libsndfile_x64.dll`、および `soxr` パッケージが同梱する
`soxr/soxr_ext.pyd`）。どちらも初回取得で利用者の機体に入ります。ソースの所在＝
`https://github.com/libsndfile/libsndfile`・`https://github.com/dofuuz/python-soxr`。
ライセンス全文と、ソースの入手手段の扱いは `licenses/first-run-notices.md`（A7・A8・A17）に記します。

### Ethical Restrictions（上流の重みの利用条件・原文）

以下は `Aratako/Irodori-TTS-v4.1-Small` モデルカード（README.md:69-82）および
`Aratako/Irodori-TTS-v4-Small` モデルカード（README.md:183-196）をそのまま写したものです（両者は sha256 一致＝
`research/lab/notes/16-verify-license.md` §3）。**日本語訳は添えません**（訳による意味の揺れを避ける）。

> ## 📜 License & Ethical Restrictions
>
> ### License
>
> This model is released under **[MIT](https://choosealicense.com/licenses/mit/)**.
>
> ### Ethical Restrictions
>
> In addition to the license terms, the following ethical restrictions apply:
>
> 1.  **No Impersonation:** Do not use this model to clone or impersonate the voice of any individual (e.g., voice actors, celebrities, public figures) without their explicit consent.
> 2.  **No Misinformation:** Do not use this model to generate deepfakes or synthetic speech intended to mislead others or spread misinformation.
> 3.  **Voice Generation Disclaimer:** When generating speech purely from text or captions without using reference audio, it is possible that the generated voice may coincidentally resemble that of a real person. This is strictly a probabilistic artifact within the latent space. The model was not trained with the intent of reproducing specific individuals.
> 4.  **Liability Disclaimer:** The developers assume no liability for any misuse of this model. Users are solely responsible for ensuring their use of the generated content complies with applicable laws and regulations in their jurisdiction.

**参照ボイスを使う利用者への注意**＝上記 1（No Impersonation）は、**参照ボイス欄に実在人物（声優・
著名人・公人）の音声を、その人の明示的な同意なく入れる使い方**に効きます。ランチャの参照ボイス欄には
この趣旨の但し書きを出します（便 D）。

---

## 5. 上流への謝辞

- [Aratako/Irodori-TTS](https://github.com/Aratako/Irodori-TTS)（MIT）
- [Aratako/Irodori-TTS-Server](https://github.com/Aratako/Irodori-TTS-Server)（MIT）
- [Aratako/Irodori-TTS-v4.1-Small](https://huggingface.co/Aratako/Irodori-TTS-v4.1-Small)（重み・MIT 宣言）
- [Aratako/Semantic-DACVAE-Japanese-32dim](https://huggingface.co/Aratako/Semantic-DACVAE-Japanese-32dim)（コーデック・MIT 宣言）
- [sony/silentcipher](https://github.com/sony/silentcipher)（透かし・MIT）
- [facebookresearch/dacvae](https://github.com/facebookresearch/dacvae)（コーデックの由来・Apache-2.0＝**当方の読み**・上の §4 と `licenses/README.md` §4）

繰り返しますが、**本ソフトウェアは非公式であり、上記の作者・組織のいずれとも関係がありません**。
