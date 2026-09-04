# irodori-TTS for 読み分けちゃん2（配布版 irodori-TTS）

> **これは非公式のソフトウェアです。上流 Irodori-TTS / Irodori-TTS-Server の作者である Aratako 氏とは
> 一切関係がありません。**上流へ問い合わせないでください。不具合の報告先は本リポジトリです。
> 上流のコードは `upstream/`（submodule）で版を固定して**無改変**のまま持ち、必要な差分は `patches/`
> にだけ置きます（正典＝`decisions.md` 裁定 3）。

---

## 1. これは何をするアプリか

**Python を知らなくても導入できる、単体で使える日本語 TTS アプリ**です。

- **Python 不要**＝埋め込み Python（python.org の embeddable package）と必要な wheel を、
  ランチャが初回起動時に取得して組み立てます。利用者は Python も pip も uv も触りません。
- **単体の TTS として使える**＝付属のランチャ UI で、GPU・CUDA 版・パラメータ・参照ボイスを
  自分の環境で試せます（配信外の読み上げテスト＝`decisions.md` 15）。
- **読み分けちゃん2（本体）の 10 番目のエンジンとしても使える**＝配布版は
  `http://127.0.0.1:18088` で HTTP の口を開けて常駐し、本体は `/health` でそれを見つけるだけです
  （起動主体＝配布版・`decisions.md` 6）。本体が読み上げるときは、**ランチャで選んだ GPU と精度が
  暗黙に使われます**（device は Server プロセスの環境変数でしか決まらない＝1 プロセス 1 デバイス）。
- **既存の Irodori-TTS-Server（8088）とは別物・併用できます**。本体の 9 番目のアダプタ（`irodori`）は
  今までどおり 8088 の個体を叩き、配布版は 18088 を使います（`decisions.md` 2）。ポートが違うだけで
  なく、**配布版は上流に無い口（`/params`・`/ywk/status`・話者一覧の覆い）を足し、未知欄・範囲外を
  400 で弾きます**（上流は黙って捨てます）。

**声の決まり方**＝参照ボイス（wav）＋名付け＝「話者」です。参照なしの話者は
「**デフォルト**」という名前で一覧の先頭に常在します（`decisions.md` 16）。声色は `caption`
（演技指示の自由文）と本文中の絵文字で作ります。

**できないこと・注意**

- CPU でも合成できますが**とても遅い**（実測 RTF 3.27・40 steps）＝配信用途には使えません
  （`decisions.md` 13）。UI で選べるだけの露出にしています。
- **Radeon（ROCm）は未保障**＝別リリースです。保証できるのは「gfx1151（Ryzen AI MAX+ 395／
  Radeon 8060S）で確認済み」という事実だけです（`docs/radeon.md`）。
- 出力音声には **SilentCipher の非可聴透かし**が既定で乗ります（切る経路は持ちません＝
  `decisions.md` 9）。

---

## 2. 導入の流れ（**案**・便 E で確定）

> この節はまだ**案**です。インストーラ（便 E）とランチャ（便 D）の実装で確定します。
> 受け入れ条件は「利用者操作 ≤ 6（vc_redist を黙って通せれば ≤ 5）」（`docs/acceptance.md`）。

1. Release からインストーラ（ランチャ＋埋め込み Python の取得台帳＋自作分・数十 MB）を落として実行する。
2. インストーラが `%LOCALAPPDATA%\Programs\irodori-tts-ywk\` に本体を置く（per-user・管理者権限なし）。
3. 初回起動でランチャが**実行系とモデルを取得**する（合計 ≈5.4 GiB・100 Mbps 級で 10 分以内が目標）。
   取得先は `%LOCALAPPDATA%\irodori-tts-ywk\`（`runtime\<variant>\`・`models\`・`voices\`・`logs\`）。
   - 実行系＝torch は `download.pytorch.org` の版固定 URL、依存は PyPI、いずれも sha256 で検証。
   - モデル＝Hugging Face（revision 固定）。
   - `vc_redist.x64.exe`（Microsoft 公式・≈25 MB）＝torch が `msvcp140.dll` を要求するため必須。
4. ランチャで GPU と CUDA 版（既定 cu130／古いドライバなら cu126）を選ぶ。
5. 「起動」を押すと `127.0.0.1:18088` で常駐する。読み上げの準備完了まで最長 120 秒。
6. 本体（読み分けちゃん2）側は、配布版のエンジンを有効にするだけ。

**オフライン導入は用意しません**（`decisions.md` 8）。第三者バイナリ（wheel・exe・dll・モデル）を
配布物に入れない方針のためです。

詳細＝`docs/install.md`（同じく案）。

---

## 3. 檔の並び

| 場所 | 中身 |
|---|---|
| `decisions.md` | **正典**。裁定はここが正。 |
| `docs/contract.md` | 配布版が本体に約束する HTTP 契約（本体が写す一枚）。 |
| `docs/acceptance.md` | 受け入れ条件（数値）。 |
| `docs/install.md` | 導入手順（案・便 E で確定）。 |
| `docs/radeon.md` | Radeon 版の注記（gfx1151 で確認済みの事実だけ）。 |
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
  の通知＝`licenses/first-run-notices.md`。これは**配布物には入れず、初回取得の UI が表示します**。

**このソフトウェアは libsndfile（LGPL-2.1）と libsoxr（LGPL-2.1-or-later）を使用します**
（`soundfile` パッケージが同梱する `libsndfile_x64.dll`、および `soxr` パッケージが同梱する
`soxr/soxr_ext.pyd`）。どちらも初回取得で利用者の機体に入ります。ソースの所在＝
`https://github.com/libsndfile/libsndfile`・`https://github.com/dofuuz/python-soxr`。
ライセンス全文と、ソースの入手手段の扱いは `licenses/first-run-notices.md`（A7・A8・A17）に記します。

### Ethical Restrictions（上流の重みの利用条件・原文）

以下は `Aratako/Irodori-TTS-v4.1-Small` モデルカード（README.md:69-82）および
`Aratako/Irodori-TTS-v4-Small` モデルカード（README.md:183-196）の逐語です（両者は sha256 一致＝
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
