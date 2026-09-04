# 09 ライセンスの鎖（BRIEF §2-2・§4-1「最初の門」）読解ノート

読解席（opus）／作成 2026-09-04／取得日時は全て **2026-09-04 01:43〜02:10 JST（＝2026-09-03 16:43〜17:10 UTC）**。
取得方法＝`curl -sS -L`（HF raw / HF API / raw.githubusercontent.com / api.github.com）。生檔は
`C:\Users\mugonkun\AppData\Local\Temp\claude\...\scratchpad\` に保存（セッション終了で消える前提）。
行番号は保存した raw 檔を `cat -n` / `grep -n` / `sed -n` で実際に数えたもの。

## 要点（10 行以内）

1. **上流の矛盾は「解けた」＝ facebook/dacvae の本体は Apache-2.0**。GitHub 側 README は 2025-12-19 13:20 UTC の commit `34e0b0d`「Update license in README」で **"SAM License"→"Apache-2.0" に直され**、LICENSE 檔の実体も Apache-2.0（11,358 byte）。
2. **HF の facebook/dacvae-watermarked カードだけが直っていない**。同日 17:25 UTC の更新でメタデータ `license: apache-2.0` を足しながら本文 46 行目の "SAM License" は放置＝**古い README の写しが残った状態**（＝矛盾は「HF カード本文のみ」に局在）。
3. 手元で使う 4 段＝コード（MIT・LICENSE 実体あり）／重み v4・v4.1（`license: mit`・本文 MIT・**LICENSE 檔なし**）／コーデック 32dim（`license: mit`・本文 "**License:** MIT"・**LICENSE 檔なし**）／その上流 dacvae-watermarked（Apache-2.0）。**再配布を禁じる条項はどこにも無い**。
4. **透かしは 2 系統ある**。DACVAE 内蔵の Audioseal は Irodori 側が `decoder.alpha = 0.0` で**殺している**（codec.py:84-96）。実際に乗るのは **SilentCipher（MIT・Sony Research Inc.）** で payload は固定文字列 `"IRDTS"`（watermark.py:10）。
5. SilentCipher の**重み**は HF `Sony/SilentCipher`（68MB）から自動取得。**このリポには license メタデータが無く（cardData=null）**、本文も「**the code** in this repository is released under the MIT license」＝**重みの明示が無い**＝本件で唯一残る空白。
6. `facebook/dacvae` は**存在しない**（HF API 401＝非公開/不在）。読むべきは `facebook/dacvae-watermarked` と GitHub `facebookresearch/dacvae` の 2 つだけ。
7. **重みだけ取ると license 文は手元に来ない**＝HF キャッシュの Irodori / DACVAE snapshot には `model.safetensors`・`weights.pth`・`tokenizer/` しか落ちていない（README も LICENSE も無い）。**同梱するなら license 文は自分で添える必要がある**（MIT の必須条件）。
8. 周辺で唯一「同梱の義務」が重いのは **FFmpeg（LGPL-2.1+／GPL 混入時 GPL）**。ただし **torchcodec は upstream のコードから一度も import されていない**（pyproject の宣言のみ）＝**推論に不要な公算が高い＝FFmpeg 問題は回避できる見込み**（未確認：`datasets` 経由の間接要求）。
9. Ethical Restrictions 4 条は v4／v4.1 で**逐語同一**。配信・商用・生成音声の公開を禁じる条項は**無い**。効くのは 1（実在人物のなりすまし禁止＝参照音声運用）と 3（偶然の類似の免責）。
10. **未確認の札**＝(a) Sony/SilentCipher の**重み**の license、(b) dacvae-watermarked の **weights.pth** 自体に添う条件（HF カード＝Apache-2.0 と読むのが素直だが本文が壊れている）、(c) `torchcodec` を落とせるか。

---

## 1. 鎖の表

### 1-1 段ごとの一覧

| # | 対象 | メタデータの license | 本文の license | LICENSE 檔の実体 | 再配布（同梱）＝原文から読める範囲 | 帰属表示等の義務 | 札 |
|---|---|---|---|---|---|---|---|
| ① | GitHub `Aratako/Irodori-TTS`（コード） | GitHub API `license.spdx_id = MIT` | README 552-554「**Code**: [MIT License](LICENSE)」 | **あり**（`upstream/Irodori-TTS/LICENSE`・21 行・MIT・`Copyright (c) 2026 Aratako`） | **可**（MIT） | 著作権表示＋MIT 全文の同梱 | 断定 |
| ② | GitHub `Aratako/Irodori-TTS-Server`（コード） | GitHub API `license.spdx_id = MIT` | README 605「This server code is released under the MIT License.」 | **あり**（`upstream/Irodori-TTS-Server/LICENSE`・21 行・同文） | **可**（MIT） | 同上 | 断定 |
| ③ | HF `Aratako/Irodori-TTS-v4-Small`（重み 2.86GB） | `license: mit`（README:2／API `cardData.license = "mit"`／tag `license:mit`） | README:187「This model is released under **[MIT]**.」 | **無し**（`raw/main/LICENSE` → HTTP 404 "Entry not found"／tree/main に無い） | **可**（MIT 宣言・禁止条項なし） | MIT の著作権表示＋全文（**自分で用意する必要あり**）／Ethical Restrictions 4 条は「license terms に**加えて**」適用と明記 | 断定（ただし LICENSE 檔なしは事実） |
| ③' | HF `Aratako/Irodori-TTS-v4.1-Small`（重み 2.86GB・upstream の既定） | `license: mit`（README:2／API 同） | README:73（③と逐語同一） | **無し**（404） | **可** | 同上 | 断定 |
| ④ | HF `Aratako/Semantic-DACVAE-Japanese-32dim`（コーデック重み 410MB） | `license: mit`（README:2／API 同） | README:29「* **License:** MIT」 | **無し**（404） | **可** | 同上／README:112「Weights derived from facebook/dacvae-watermarked」＝上流の帰属 | 断定 |
| ⑤ | HF `facebook/dacvae-watermarked`（④の由来・430MB） | `license: apache-2.0`（README:2／API `cardData.license = "apache-2.0"`／tag `license:apache-2.0`） | README:46「licensed under the **SAM License**」＝**メタデータと不一致** | **無し**（`raw/main/LICENSE` → 404） | **Apache-2.0 と読めば可**／SAM License と読めば「本 Agreement の写しを添えて」条件付き可 | Apache-2.0 なら NOTICE 相当・改変告知 | **矛盾（ただし §1-2 で由来が判明）** |
| ⑥ | GitHub `facebookresearch/dacvae`（⑤を読むコード・`pip install git+...`） | GitHub API `license.spdx_id = Apache-2.0` | README:67「This project is licensed under **Apache-2.0**」 | **あり**（`LICENSE`・11,358 byte・Apache License 2.0 全文） | **可**（Apache-2.0） | LICENSE の同梱・改変ファイルの告知 | 断定 |
| ⑦ | `silentcipher`（コード・pin `SesameAILabs/silentcipher@d46d7d0…`＝ `sony/silentcipher` の fork） | GitHub API 両方とも `MIT`／`pyproject.toml:12` classifier「License :: OSI Approved :: MIT License」 | HF `Sony/SilentCipher` README:135「The **code** in this repository is released under the MIT license」 | **あり**（pin commit の `LICENSE`・1,075 byte・MIT・`Copyright (c) 2024 Sony Research Inc.`） | **可**（MIT） | 著作権表示＋MIT 全文 | 断定 |
| ⑦' | HF `Sony/SilentCipher`（透かし**重み** 68MB） | **無し**（API `cardData` = `null`・tags に `license:*` 無し） | 本文は「the code …」だけ＝**重みへの言及なし** | **無し**（tree/main に LICENSE 無し） | **不明** | 不明 | **未確認（本件の唯一の空白）** |
| ⑧ | HF `sbintuitions/modernbert-ja-310m`（③のテキスト符号器の由来・重みに焼き込み済み） | `license: mit`（README:5） | README:196「[MIT License](…/blob/main/LICENSE)」 | あり（HF 上・未直読＝リンクのみ確認） | **可** | 著作権表示＋全文 | 断定（LICENSE 実体は未直読） |

### 1-2 ⑤の矛盾の由来（時系列＝ここが本ノートの核）

| 日時（UTC） | 場所 | commit | 中身 |
|---|---|---|---|
| 2025-10-03 13:36 | GitHub `facebookresearch/dacvae` | `d39b77d589` "Initial Commit" | README:43「licensed under the **SAM License**」／同 commit で **LICENSE 檔＝Apache-2.0 全文**を追加（LICENSE の commit 履歴はこの 1 件のみ） |
| 2025-10-03 14:14 | HF `facebook/dacvae-watermarked` | `07fe69464c` "initial commit" | README＝**frontmatter だけの 31 byte**（`license: apache-2.0`） |
| 2025-12-16 15:54 | HF | `9ca52e8c33` "Upload README.md with huggingface_hub" | GitHub の**当時の**README 本文をそのまま上書き＝**frontmatter が消え**、本文に "SAM License" が入る |
| **2025-12-19 13:20** | **GitHub** | **`34e0b0dbb6` "Update license in README"** | **README:43 を "SAM License" → "Apache-2.0" に修正**（LICENSE 檔は初回から Apache-2.0 のまま不変） |
| 2025-12-19 17:25 | HF | `8680102d14` "Update README.md" | frontmatter `license: apache-2.0` を**復活**させたが、本文 46 行目の "SAM License" は**直さなかった** |
| 2025-12-19 20:46 | GitHub | `2807862a05` "Update README.md" | 誤字修正等（License 節は Apache-2.0 のまま） |

→ **観測される事実**：LICENSE 檔の実体は初回から一度も変わらず Apache-2.0。「SAM License」は初回 README の記述で、**GitHub 側では 2025-12-19 に誤りとして修正された**が、HF カード本文には**修正が伝播していない**。
→ **解釈は卓の専管**（本ノートは断定しない）。ただし「メタデータ＝apache-2.0」「LICENSE 実体＝Apache-2.0」「GitHub 本文＝Apache-2.0」の 3 対 1 である。

### 1-3 「SAM License」とは何か（参考・最悪ケースの見積り用）

`SAM License` は Meta の Segment Anything 系の独自ライセンス（`facebookresearch/sam3/LICENSE`・"Last Updated: November 19, 2025"）。
**dacvae とは無関係の別プロジェクト**＝Meta 社内 README テンプレートの流用と見るのが自然（**推測・未確認**）。
仮にこれが効くとしても再配布は**禁止ではない**：§1.b.i「If you distribute … you may only do so under the terms of this Agreement and you shall **provide a copy of this Agreement** with any such SAM Materials」＝**Agreement の写しを添えれば配布可**。ただし §1.b.iv「reverse engineer, decompile or **discover the underlying components**」の禁止と §1.b.v（Trade Controls / ITAR）は Apache-2.0 に無い縛りで、**ONNX 変換（案⒝）の適法性に触れる可能性がある**＝卓の材料。
（注：SAM 3 は 2025-11-19 版・dacvae 初回 commit は 2025-10-03＝**時系列上、dacvae の "SAM License" は SAM 3 のそれを指しようがない**。この点も矛盾のうち。）

### 1-4 GitHub 本体への link の在り処（BRIEF §0 の確定事項）

| link 先 | どのカードのどこ | URL |
|---|---|---|
| `Aratako/Irodori-TTS` | v4-Small README:**16**（バッジ `[![Code](…badge/Code-GitHub-black)]`）と README:**95**（`👉 **[GitHub: Aratako/Irodori-TTS]**`） | `https://github.com/Aratako/Irodori-TTS` |
| 同上 | v4.1-Small README:**16**（バッジ）と README:**30**（`👉`）／同 16 行に Demo Space `https://huggingface.co/spaces/Aratako/Irodori-TTS-v4.1-Small-Demo` | 同上 |
| `Aratako/Irodori-TTS-Server` | **モデルカードには無い**。`upstream/Irodori-TTS/README.md:9`「For an OpenAI-compatible inference API server, see [Irodori-TTS-Server](…)」から辿る | `https://github.com/Aratako/Irodori-TTS-Server` |

clone の実体（`git remote -v`）＝`upstream/Irodori-TTS` → `https://github.com/Aratako/Irodori-TTS.git`（HEAD `8224dafb46d0aba89209a8f905f1cb7e3299d9c1` 2026-08-11「Update default model to v4.1-Small」）／`upstream/Irodori-TTS-Server` → `https://github.com/Aratako/Irodori-TTS-Server.git`（HEAD `841fb7c6ec57729c56b9b75c0ef2562249b13a10` 2026-08-02）。

### 1-5 tree/main の檔一覧（＝何を取ると何が付いてくるか）

| repo | 檔 |
|---|---|
| `Aratako/Irodori-TTS-v4-Small` | `samples/`(dir)・`tokenizer/`(dir)・`.gitattributes`・`EMOJI_ANNOTATIONS.md` 3,403B・`README.md` 19,959B・`VOICE_CLONING_BENCHMARK.md`・`VOICE_DESIGN_BENCHMARK.md`・`model.safetensors` **3,064,295,596B**／**LICENSE 無し** |
| `Aratako/Irodori-TTS-v4.1-Small` | `tokenizer/`・`.gitattributes`・`EMOJI_ANNOTATIONS.md`・`README.md` 6,950B・`model.safetensors` **3,064,295,596B**／**LICENSE 無し** |
| `Aratako/Semantic-DACVAE-Japanese-32dim` | `.gitattributes`・`README.md` 5,128B・`weights.pth` **429,620,065B**／**LICENSE 無し** |
| `facebook/dacvae-watermarked` | `.gitattributes`・`README.md` 1,715B・`weights.pth` **430,785,157B**／**LICENSE 無し** |
| `Sony/SilentCipher` | `16_khz/`・`44_1_khz/`・`.gitattributes`・`README.md` 7,794B・`config.json` 51B／**LICENSE 無し** |
| `facebookresearch/dacvae`(GitHub) | `.gitignore`・`CODE_OF_CONDUCT.md`・`CONTRIBUTING.md`・**`LICENSE` 11,358B**・`README.md`・`dacvae/`・`requirements.txt`・`setup.py` |

### 1-6 モデルカードの commit 履歴（ライセンス変更の有無）

| repo | 履歴 | license 変更 |
|---|---|---|
| `Aratako/Irodori-TTS-v4-Small` | `7a040d9afe`(2026-08-01 initial)→`c0097bd1be`(重み)→`df16513ce8`→`e4aaac4df3`→`4c92c7ee2b`(2026-08-11 Update README.md) | **無し**（一貫して mit・現行が最終） |
| `Aratako/Irodori-TTS-v4.1-Small` | `39c24a9427`(2026-08-11 initial)→`df20e70ca3`(重み)→`9712216387`→`2b28324dc2`(2026-08-11) | **無し**（mit） |
| `Aratako/Semantic-DACVAE-Japanese-32dim` | `56bfa86f17`(2026-03-14 initial)→`818b64119d`(weights.pth)→`47376ee248`(2026-03-14 Create README.md) | **無し**（mit） |
| `facebook/dacvae-watermarked` | §1-2 の表のとおり（**あり**：31byte frontmatter → 本文差替で frontmatter 消滅 → frontmatter 復活・本文は SAM のまま） | **あり** |

---

## 2. 逐語引用集

すべて 2026-09-04 01:43〜02:10 JST 取得。行番号は raw 檔基準。

### 2-1 HF `Aratako/Irodori-TTS-v4-Small`
URL: `https://huggingface.co/Aratako/Irodori-TTS-v4-Small/raw/main/README.md`（HTTP 200・19,959 byte）

- :2（YAML メタデータ）
  > `license: mit`
- :16
  > `[![Code](https://img.shields.io/badge/Code-GitHub-black)](https://github.com/Aratako/Irodori-TTS)`
- :32（透かし）
  > `  * **Integrated Watermarking:** Integrates [SilentCipher](https://github.com/sony/silentcipher) to apply robust, invisible audio watermarks directly to generated outputs, promoting responsible AI usage.`
- :95
  > `👉 **[GitHub: Aratako/Irodori-TTS](https://github.com/Aratako/Irodori-TTS)**`
- :183-196（License & Ethical Restrictions・全文）
  > `## 📜 License & Ethical Restrictions`
  > （185）`### License`
  > （187）`This model is released under **[MIT](https://choosealicense.com/licenses/mit/)**.`
  > （189）`### Ethical Restrictions`
  > （191）`In addition to the license terms, the following ethical restrictions apply:`
  > （193）`1.  **No Impersonation:** Do not use this model to clone or impersonate the voice of any individual (e.g., voice actors, celebrities, public figures) without their explicit consent.`
  > （194）`2.  **No Misinformation:** Do not use this model to generate deepfakes or synthetic speech intended to mislead others or spread misinformation.`
  > （195）`3.  **Voice Generation Disclaimer:** When generating speech purely from text or captions without using reference audio, it is possible that the generated voice may coincidentally resemble that of a real person. This is strictly a probabilistic artifact within the latent space. The model was not trained with the intent of reproducing specific individuals.`
  > （196）`4.  **Liability Disclaimer:** The developers assume no liability for any misuse of this model. Users are solely responsible for ensuring their use of the generated content complies with applicable laws and regulations in their jurisdiction.`
- :203, :205（Acknowledgments）
  > `  - [DACVAE](https://github.com/facebookresearch/dacvae) — Audio VAE`
  > `  - [SilentCipher](https://github.com/sony/silentcipher) — Audio watermarking integration`
- LICENSE: `https://huggingface.co/Aratako/Irodori-TTS-v4-Small/raw/main/LICENSE` → **HTTP 404**・本文「`Entry not found`」

### 2-2 HF `Aratako/Irodori-TTS-v4.1-Small`
URL: `https://huggingface.co/Aratako/Irodori-TTS-v4.1-Small/raw/main/README.md`（HTTP 200・6,950 byte）

- :2 `license: mit`
- :16
  > `[![Code](https://img.shields.io/badge/Code-GitHub-black)](https://github.com/Aratako/Irodori-TTS) [![Demo Space](https://img.shields.io/badge/Demo-HuggingFace%20Space-blue)](https://huggingface.co/spaces/Aratako/Irodori-TTS-v4.1-Small-Demo)`
- :69-82＝**v4-Small の :183-196 と逐語同一**（`## 📜 License & Ethical Restrictions` / `This model is released under **[MIT](https://choosealicense.com/licenses/mit/)**.` / Ethical Restrictions 1-4）
- :91
  > `  - [SilentCipher](https://github.com/sony/silentcipher) — Audio watermarking integration`
- **透かしの記述はカード本文に無い**（Key Features 節そのものが無い短いカード。Acknowledgments の :91 のみ）
- LICENSE → **HTTP 404**「Entry not found」

### 2-3 HF `Aratako/Semantic-DACVAE-Japanese-32dim`
URL: `https://huggingface.co/Aratako/Semantic-DACVAE-Japanese-32dim/raw/main/README.md`（HTTP 200・5,128 byte）

- :2 `license: mit`
- :18（由来）
  > `Like its predecessor, this model is based on [facebook/dacvae-watermarked](https://huggingface.co/facebook/dacvae-watermarked) and integrates WavLM semantic distillation inspired by the [Semantic-VAE paper](https://arxiv.org/abs/2509.22167).`
- :24（由来・2 段）
  > `* **Base Model:** Derived from [Semantic-DACVAE-Japanese](https://huggingface.co/Aratako/Semantic-DACVAE-Japanese) (originally [facebook/dacvae-watermarked](https://huggingface.co/facebook/dacvae-watermarked)).`
- :29
  > `* **License:** MIT`
- :85-87（**透かしを外す手順が公式に書かれている**）
  > `# Disable/bypass the default watermark since this model was fine-tuned without it`
  > `model.decoder.alpha = 0.0`
  > `model.decoder.watermark = lambda x, message=None, d=model.decoder: d.wm_model.encoder_block.forward_no_conv(x)`
- :112-113（Acknowledgements）
  > `* **Base Model:** Weights derived from [facebook/dacvae-watermarked](https://huggingface.co/facebook/dacvae-watermarked).`
  > `* **Training Code:** Leveraged the codebase from [Descript Audio Codec (DAC)](https://github.com/descriptinc/descript-audio-codec).`
- LICENSE → **HTTP 404**「Entry not found」

### 2-4 HF `facebook/dacvae-watermarked`
URL: `https://huggingface.co/facebook/dacvae-watermarked/raw/main/README.md`（HTTP 200・1,715 byte）

- :2
  > `license: apache-2.0`
- :34-38（透かし）
  > `## Watermarking`
  > （36）`The DAC-VAE decoder has been integrated with [Audioseal](https://github.com/facebookresearch/audioseal) to ensure all audios generated`
  > （37）`contain watermarks that are verifiable independently. We develop a new watermarking model with adapted architecture specifically for`
  > （38）`DAC-VAE to optimize the high-fidelity outcome. We also plan to release the detector API. Stay tuned !`
- :44-46（**矛盾の当該行**）
  > `## License`
  > （46）`This project is licensed under the SAM License - see the [LICENSE](LICENSE) file for details.`
- LICENSE: `https://huggingface.co/facebook/dacvae-watermarked/raw/main/LICENSE` → **HTTP 404**「Entry not found」（＝カード本文が指す LICENSE 檔は HF 側に存在しない）
- 旧版 `raw/9ca52e8c33/README.md`（2025-12-16）:41-43 も同文「SAM License」／`raw/01ea7995d6/README.md`（〜2025-12-16）は **全 3 行**
  > `---` / `license: apache-2.0` / `---`

### 2-5 HF `facebook/dacvae`
`https://huggingface.co/facebook/dacvae/raw/main/README.md` → **HTTP 401**・本文「`Invalid username or password.`」／
`https://huggingface.co/api/models/facebook/dacvae` → **HTTP 401**・`{"error":"Invalid username or password."}`
＝**取得失敗（未認証では 401＝HF は「非公開または不在」を 401 で返す）**。`facebook/dacvae-watermarked` は同条件で 200 なので、**`facebook/dacvae` は公開されていない**と読める（断定は避ける・未確認）。

### 2-6 GitHub `facebookresearch/dacvae`
- README `https://raw.githubusercontent.com/facebookresearch/dacvae/main/README.md`（200・2,423 byte）:65-67
  > `## License`
  > （67）`This project is licensed under Apache-2.0 - see the [LICENSE](LICENSE) file for details.`
- 初回 commit 版 `raw/d39b77d589/README.md`:41-43
  > `## License`
  > （43）`This project is licensed under the SAM License - see the [LICENSE](LICENSE) file for details.`
- 修正 commit 版 `raw/34e0b0dbb6/README.md`:43
  > `This project is licensed under Apache-2.0 - see the [LICENSE](LICENSE) file for details.`
- LICENSE `https://raw.githubusercontent.com/facebookresearch/dacvae/main/LICENSE`（200・11,358 byte）:2-4
  > `                                 Apache License`
  > `                           Version 2.0, January 2004`
  > `                        http://www.apache.org/licenses/`
  - **本文中に "SAM" も "Meta" も「Copyright <実名>」も無い**（`grep -n -i "sam\|meta\|copyright \["` の当たりは :187/:190 の定型附録のみ）＝Apache-2.0 の**素の全文で、著作権者名が埋まっていない**。
- `setup.py`:1
  > `# Copyright (c) Meta Platforms, Inc. and affiliates. All Rights Reserved\n`
- `setup.py`:28（**packaging の綻び**）
  > `    license_files = ("LICENSE.txt",)`
  - 実体の檔名は `LICENSE`（`.txt` 無し）＝**wheel/sdist に LICENSE が入らない**恐れ。同梱する側は GitHub から LICENSE を自分で取って添える必要（**未確認**：実際の wheel の中身は未検証）。
- `requirements.txt`:1-11
  > `argbind>=0.3.7` / `descript-audiotools>=0.7.2` / `einops` / `numpy` / `torch` / `torchaudio` / `tqdm` / `tensorboard` / `numba>=0.5.7` / `jupyterlab` / `huggingface-hub`
- GitHub API `license` = `{"key":"apache-2.0","spdx_id":"Apache-2.0","name":"Apache License 2.0"}`／`created_at` 2025-10-03T13:15:55Z

### 2-7 silentcipher
- pin 元（`upstream/Irodori-TTS/pyproject.toml:20`）
  > `    "silentcipher @ git+https://github.com/SesameAILabs/silentcipher.git@d46d7d0893a583d8968ab3a6626e2289faec9152",`
- `SesameAILabs/silentcipher` は `sony/silentcipher` の **fork**（GitHub API `fork: true`・`parent.full_name: "sony/silentcipher"`）。両者とも API `license.spdx_id = "MIT"`。
- pin commit の LICENSE `https://raw.githubusercontent.com/SesameAILabs/silentcipher/d46d7d0893a583d8968ab3a6626e2289faec9152/LICENSE`（200・1,075 byte）:1-3
  > `MIT License`
  > （3）`Copyright (c) 2024 Sony Research Inc.`
  - `sony/silentcipher` master の LICENSE と**バイト一致**（同 1,075 byte・同文）。
- pin commit の `pyproject.toml`:12
  > `    "License :: OSI Approved :: MIT License",`
- pin commit の `Models/` は **`README.md` 39 byte のみ**＝**重みはリポに入っていない**。
- HF `Sony/SilentCipher` README:133-135
  > `# License`
  > （135）`- The code in this repository is released under the MIT license as found in the [LICENSE file](LICENSE).`
  - **HF API `cardData` は `null`**（＝`license:` メタデータ無し・tags にも `license:*` 無し）／`lastModified` 2024-06-18T06:26:10Z／`gated: false`／`tree/main` に **LICENSE 檔は無い**（`16_khz/`・`44_1_khz/`・`.gitattributes`・`README.md`・`config.json`）。
  - README:49 も参照「`Find the latest models for 44.1kHz and 16kHz sampling rate in the release section of this repository [RELEASE](https://github.com/sony/silentcipher/releases)`」／:51「`**Note**: Soon the models will also be released on hugging face. Stay tuned !`」＝**重みの配布条件を述べた文が無い**。
- **重みの取得経路**（`src/silentcipher/server.py`:469-477）
  > `def get_model(model_type='44.1k', ckpt_path='../Models/44_1_khz/73999_iteration', config_path='../Models/44_1_khz/73999_iteration/hparams.yaml', device='cpu'):`
  > （473）`            print('ckpt path or config path does not exist! Downloading the model from the Hugging Face Hub...')`
  > （475）`            folder_dir = snapshot_download(repo_id="sony/silentcipher")`
  - ＝Irodori 側は `silentcipher.get_model(model_type="44.1k", device=…)` を引数なしで呼ぶ（`upstream/Irodori-TTS/irodori_tts/watermark.py:43`）ので、**必ず HF `sony/silentcipher` を丸ごと snapshot_download する**。

### 2-8 upstream clone（ローカル・読むだけ）
- `upstream/Irodori-TTS/LICENSE`:1-3
  > `MIT License`
  > （3）`Copyright (c) 2026 Aratako`
- `upstream/Irodori-TTS-Server/LICENSE`:1-3＝**同文**（`Copyright (c) 2026 Aratako`）
- `upstream/Irodori-TTS/pyproject.toml`:6
  > `license = "MIT"`
- `upstream/Irodori-TTS-Server/pyproject.toml`:6
  > `license = "MIT"`
- `upstream/Irodori-TTS-Server/pyproject.toml`:11
  > `    "irodori-tts @ git+https://github.com/Aratako/Irodori-TTS.git",`
- `upstream/Irodori-TTS/README.md`:552-555
  > `## License`
  > （554）`- **Code**: [MIT License](LICENSE)`
  > （555）`- **Model Weights**: Please refer to the [Irodori-TTS-v4.1-Small model card](https://huggingface.co/Aratako/Irodori-TTS-v4.1-Small) for licensing details`
- `upstream/Irodori-TTS-Server/README.md`:603-610
  > `## License`
  > （605）`This server code is released under the MIT License. See [LICENSE](LICENSE).`
  > （607）`Model weights and codec assets are distributed separately. Check the Hugging Face model cards for their licenses and usage terms:`
  > （609）`- [Aratako/Irodori-TTS-v4-Small](https://huggingface.co/Aratako/Irodori-TTS-v4-Small)`
  > （610）`- [Aratako/Semantic-DACVAE-Japanese-32dim](https://huggingface.co/Aratako/Semantic-DACVAE-Japanese-32dim)`
  - **札**：Server の README は既定が v4.1 に上がった後も **v4-Small を指したまま**（`upstream/Irodori-TTS` の HEAD は「Update default model to v4.1-Small」）。実務上の食い違い（ライセンスは同じ MIT なので実害は無い）。
- `upstream/Irodori-TTS/README.md`:296（透かし・**「available なとき」限定**という重要な但し書き）
  > `Generated audio is passed through [SilentCipher](https://github.com/sony/silentcipher) watermarking automatically when the dependency and model files are available.`
- `upstream/Irodori-TTS/irodori_tts/watermark.py`:10（**固定 payload**）
  > `IRODORI_WATERMARK_PAYLOAD = (73, 82, 68, 84, 83)  # "IRDTS"`
- `upstream/Irodori-TTS/irodori_tts/watermark.py`:36-40, :44-50（**透かしは失敗しても止まらない＝warning だけ**）
  > （38）`                "SilentCipher package is unavailable; generated audio will not be watermarked."`
  > （46-48）`                "SilentCipher model could not be loaded (%s); generated audio will not be "` / `                "watermarked.",`
- `upstream/Irodori-TTS/irodori_tts/inference_runtime.py`:1443-1446
  > `                    "warning: SilentCipher watermark is unavailable; generated audio was not "`
  > `                    "watermarked."`
- `upstream/Irodori-TTS/irodori_tts/codec.py`:84-96（**DACVAE 内蔵 Audioseal 透かしを殺している**）
  > （84）`            decoder.alpha = 0.0`
  > （86）`                # Irodori checkpoints were trained without the DACVAE watermark branch.`
  > （87）`                # Keep decode output mono while skipping that encode/decode path.`
  > （96）`                decoder.watermark = _watermark_passthrough`
- `upstream/Irodori-TTS/irodori_tts/codec.py`:51／`inference_runtime.py`:190（既定のコーデック repo）
  > `        repo_id: str = "Aratako/Semantic-DACVAE-Japanese-32dim",`
  > `    codec_repo: str = "Aratako/Semantic-DACVAE-Japanese-32dim"`
- `upstream/Irodori-TTS/irodori_tts/inference_runtime.py`:559-560, 567-571（**重みだけ取る＝README/LICENSE は取らない**の根拠）
  > `        checkpoint_relative = Path("model.safetensors")`
  > `        allow_patterns = ["model.safetensors", "tokenizer/*"]`
  > `        snapshot_download(` / `            repo_id=repo_id,` / `            allow_patterns=allow_patterns,`
- `upstream/Irodori-TTS/configs/train_v4_small.yaml`:10, :32
  > `  text_tokenizer_repo: sbintuitions/modernbert-ja-310m`
  > `  caption_tokenizer_repo: sbintuitions/modernbert-ja-310m`

### 2-9 SAM License の実体（参考）
URL: `https://raw.githubusercontent.com/facebookresearch/sam3/main/LICENSE`（200）
- :1-2
  > `SAM License`
  > `Last Updated: November 19, 2025`
- :30
  > `a. Grant of Rights. You are granted a non-exclusive, worldwide, non-transferable and royalty-free limited license under Meta's intellectual property or other rights owned by Meta embodied in the SAM Materials to use, reproduce, distribute, copy, create derivative works of, and make modifications to the SAM Materials.`
- :33
  > `i. Distribution of SAM Materials, and any derivative works thereof, are subject to the terms of this Agreement. If you distribute or make the SAM Materials, or any derivative works thereof, available to a third party, you may only do so under the terms of this Agreement and you shall provide a copy of this Agreement with any such SAM Materials.`
- :40
  > `iv. Your use of the SAM Materials will not involve or encourage others to reverse engineer, decompile or discover the underlying components of the SAM Materials.`

### 2-10 FFmpeg（torchcodec の実行時要求）
URL: `https://www.ffmpeg.org/legal.html`（200）
> `FFmpeg is licensed under the GNU Lesser General Public License (LGPL) version 2.1 or later. However, FFmpeg incorporates several optional parts and optimizations that are covered by the GNU General Public License (GPL) version 2 or later. If those parts get used the GPL applies to all of FFmpeg.`

LGPL 遵守チェックリストより（同ページ・逐語）
> `Compile FFmpeg without "--enable-gpl" and without "--enable-nonfree".` / `Use dynamic linking (on windows, this means linking to dlls) for linking with FFmpeg libraries.` / `Distribute the source code of FFmpeg, no matter if you modified it or not.` / `Host the FFmpeg source code on the same webserver as the binary you are distributing.`

torchcodec README（`https://raw.githubusercontent.com/pytorch/torchcodec/main/README.md`）
- :246-248
  > `## License`
  > `TorchCodec is released under the [BSD 3 license](./LICENSE).`
- :250-251
  > `However, TorchCodec may be used with code not written by Meta which may be`
  > `distributed under different licenses.`
- :111-115（FFmpeg は**同梱されず**利用者側の実体を使う）
  > `1. Install FFmpeg, if it's not already installed. TorchCodec supports all major`
  > `   FFmpeg versions in [4, 9]. Linux distributions usually come with FFmpeg`
  > `   pre-installed. You'll need FFmpeg that comes with separate shared libraries.`
  > `   This is especially relevant for Windows users: these are usually called the`
  > `   "shared" releases.`

---

## 3. 周辺ライブラリのライセンス名（F）

| パッケージ | ライセンス | 出典（取得 2026-09-04 01:5x JST） |
|---|---|---|
| `descript-audiotools` | **MIT** | PyPI `info.license = "MIT"`（`https://pypi.org/pypi/descript-audiotools/json`）／GitHub `descriptinc/audiotools` API `spdx_id = MIT` |
| `descript-audio-codec`（④の学習コード由来） | **MIT** | GitHub `descriptinc/descript-audio-codec` API `spdx_id = MIT` |
| `torchcodec` | **BSD-3-Clause** | GitHub `pytorch/torchcodec` API `spdx_id = BSD-3-Clause`／README:248 |
| FFmpeg（torchcodec が**実行時に**要求・同梱されない） | **LGPL-2.1-or-later／GPL-2.0-or-later（GPL部品を有効化した場合）** | `https://www.ffmpeg.org/legal.html`（§2-10 に逐語） |
| `torch` | **BSD-3-Clause 系（GitHub 判定は NOASSERTION="Other"）**＝`pytorch/pytorch` の `LICENSE` は BSD-3-Clause 本文に多数の第三者著作権表示を連ねた形（**未確認**：LICENSE 全文は本便で未直読） | GitHub `pytorch/pytorch` API `license.spdx_id = "NOASSERTION"`, `name = "Other"` |
| `torchaudio` | **BSD-2-Clause**（PyPI classifier「License :: OSI Approved :: BSD License」） | `https://pypi.org/pypi/torchaudio/json` |
| `transformers` | **Apache-2.0** | GitHub `huggingface/transformers` API `spdx_id = Apache-2.0`／PyPI `info.license = "Apache 2.0 License"` |
| `sentencepiece` | **Apache-2.0** | GitHub `google/sentencepiece` API `spdx_id = Apache-2.0` |
| `numba` | **BSD-2-Clause**（PyPI classifier「BSD License」・`info.license = "BSD"`） | GitHub `numba/numba` API `spdx_id = BSD-2-Clause` |
| `llvmlite` | **BSD-2-Clause** | GitHub `numba/llvmlite` API `spdx_id = BSD-2-Clause` |
| `gradio` | **Apache-2.0** | GitHub `gradio-app/gradio` API `spdx_id = Apache-2.0` |
| `fastapi` | **MIT** | GitHub `fastapi/fastapi` API `spdx_id = MIT` |
| `uvicorn` | **BSD-3-Clause** | GitHub `encode/uvicorn` API `spdx_id = BSD-3-Clause` |
| `huggingface-hub` | **Apache-2.0** | PyPI `info.license = "Apache-2.0"` |
| `peft` | **Apache-2.0** | PyPI classifier「Apache Software License」 |
| `safetensors` | **Apache-2.0** | PyPI classifier「Apache Software License」 |
| `soundfile` | **BSD-3-Clause**（＋同梱 libsndfile は **LGPL-2.1**＝**未確認・要追検**） | PyPI `info.license = "BSD 3-Clause License"` |
| `datasets` | **Apache-2.0** | PyPI `info.license = "Apache 2.0"` |
| `torchdata` | **BSD** | PyPI `info.license = "BSD"` |
| `pyyaml` | **MIT** | PyPI `info.license = "MIT"` |
| `tqdm` | **MPL-2.0 AND MIT** | PyPI `info.license = "MPL-2.0 AND MIT"` |
| `einops` | **MIT** | PyPI `info.license = "MIT"` |
| `argbind` | **MIT** | PyPI classifier「MIT License」 |

**FFmpeg の論点（同梱時の義務）**：torchcodec 自身は BSD-3 で FFmpeg を**同梱しない**（利用者環境の shared libs を使う）。したがって
⒜専用インストーラで **FFmpeg の DLL を同梱すると LGPL の義務（動的リンク・FFmpeg ソース同梱・同一 web サーバでの提供・改変差分の公開）が発生する**。
ただし **`torchcodec` は upstream のコードから一度も import されていない**（`grep -rn "torchcodec" --include="*.py"` の当たり **0 件**・宣言は `upstream/Irodori-TTS/pyproject.toml:23,35,41,47,55` のみ）。
→ **推論だけなら torchcodec ごと落とせる可能性が高く、FFmpeg 問題は回避できる見込み**（**未確認**：`datasets` が torchcodec を間接要求するか・`datasets` 自体を落とせるか）。

---

## 4. ローカル HF キャッシュの実測（G）＝「重みだけ取ると license 文が来ない」

`C:\Users\mugonkun\.cache\huggingface\hub\`（読むだけ・変更なし）。`find -type f` 全件：

| repo | snapshot 直下の檔 | README | LICENSE | 実サイズ |
|---|---|---|---|---|
| `models--Aratako--Irodori-TTS-v4-Small` | `model.safetensors`・`tokenizer/tokenizer.json`・`tokenizer/tokenizer_config.json` | **無し** | **無し** | 2.9G |
| `models--Aratako--Irodori-TTS-v4.1-Small` | 同上 | **無し** | **無し** | 2.9G |
| `models--Aratako--Semantic-DACVAE-Japanese-32dim` | `weights.pth` のみ | **無し** | **無し** | 410M |
| `models--sony--silentcipher` | `16_khz/97561_iteration/{dec_c,dec_m_0,enc_c,opt}.ckpt`＋`hparams.yaml`・`44_1_khz/73999_iteration/` 同構成・`README.md`・`config.json`・`.gitattributes` | **あり** | **無し** | 68M |

- 前 2 段が README すら落ちてこないのは `allow_patterns = ["model.safetensors", "tokenizer/*"]`（`inference_runtime.py`:560）と `hf_hub_download(filename="weights.pth")`（`codec.py`:72）による**明示の絞り込み**。
- SilentCipher だけ README が来るのは `snapshot_download(repo_id="sony/silentcipher")` が**全檔取得**だから（`server.py`:475）。ただし**その repo に LICENSE 檔が無い**ので、結局 **4 段すべてで license 全文は手元に来ない**。
- **含意**：⒜専用インストーラで重みを同梱するにせよ初回取得するにせよ、**MIT の必須条件（著作権表示＋許諾文の同梱）を満たす licenses フォルダは配布側で自作する必要がある**。upstream もそれをしていない（配布物に license 文を持たない）＝**上流の慣行に倣うだけでは MIT を満たさない**。

---

## 5. 「同梱できるもの／利用者に取得させるもの」切り分け案（裁定ではなく材料）

| 物 | 大きさ | 同梱の可否（原文から読める範囲） | 案 | 理由 |
|---|---|---|---|---|
| `Irodori-TTS` / `Irodori-TTS-Server` のコード | 数 MB | **可**（MIT・LICENSE 実体あり） | **同梱**（`LICENSE`＋`Copyright (c) 2026 Aratako` を `licenses/` に写す） | 争点なし |
| `dacvae` パッケージ（`facebookresearch/dacvae`） | 数百 KB | **可**（Apache-2.0・LICENSE 実体あり） | **同梱**（LICENSE を **GitHub から直接**取って添える。`setup.py:28` の `license_files=("LICENSE.txt",)` の綻びで wheel に入らない恐れ） | 争点なし |
| `silentcipher` パッケージ（pin `d46d7d0`） | 数百 KB | **可**（MIT・LICENSE 実体あり・`Copyright (c) 2024 Sony Research Inc.`） | **同梱** | 争点なし |
| 重み `Aratako/Irodori-TTS-v4.1-Small`（`model.safetensors`＋`tokenizer/`） | **2.86GB** | **可**（MIT 宣言）。矛盾は**この段には無い**（矛盾は 2 段上の HF カード本文） | **A案：初回取得**（配布物を軽く保てる・上流追随が楽・上流が更新したら追える）／**B案：同梱**（オフライン導入・初心者に優しい） | どちらも license 上は成立。**判断軸は配布物サイズと初心者の導線であって、ライセンスではない** |
| コーデック `Aratako/Semantic-DACVAE-Japanese-32dim`（`weights.pth`） | **410MB** | **MIT 宣言**。ただし派生元 `facebook/dacvae-watermarked` の HF カード本文に "SAM License" が残る（§1-2 で GitHub 側は Apache-2.0 に修正済み） | **A案：初回取得**（矛盾の解決を待つ・自分で再配布しない＝最も安全）／**B案：同梱**（Apache-2.0 と読めるなら可。NOTICE と改変告知を添える） | **卓の裁定が要る唯一の実質論点**。SAM License と読んでも「Agreement の写しを添えれば配布可」で**禁止ではない**（§2-9 :33） |
| 透かし重み `Sony/SilentCipher`（68MB） | 68MB | **不明**（HF に license メタデータ無し・本文は「the **code** …」のみ・LICENSE 檔なし） | **初回取得を強く推奨**（＝自分で再配布しない） | **原文に重みの配布条件が書かれていない**。取得は `get_model()` が自動でやる＝手数は増えない |
| テキスト符号器 `sbintuitions/modernbert-ja-310m` | — | **可**（MIT） | **取得不要**（重みに焼き込み済み・tokenizer は checkpoint repo の `tokenizer/` に同梱） | 実測：キャッシュに modernbert の repo は無い |
| `torch` / `torchaudio` の wheel | GB 級 | 可（BSD 系） | 同梱（埋め込み Python の site-packages ごと） | 別席（配布サイズ）の担当 |
| `torchcodec` ＋ FFmpeg DLL | — | torchcodec は BSD-3 で可／**FFmpeg DLL を同梱すると LGPL の義務が発生** | **そもそも torchcodec を落とす方向で検討**（コードから import 0 件） | §3 末尾 |

**この節の要旨**：**ライセンスは⒜⒝⒞のどれも止めていない**。止めうるとしたら「コーデック重みの再配布」1 点だが、
最悪解釈（SAM License）でも**配布禁止ではなく「Agreement の写しを添える」条件付き**であり、しかも上流 GitHub は既に Apache-2.0 に修正済み。
**回避策も安い**（＝重みは同梱せず初回取得にする）。したがって **BRIEF §2-2 の門は「×で止まる」形にはならない**（判断は卓）。

---

## 6. Ethical Restrictions が配信用途に効く条項（前回調査 `probe/irodori-tts-licenses.md` の更新）

前回（2026-08-29・Chrome 直読）の記述は**逐語で正しい**。以下は**追補と訂正**のみ。

| 前回の記述 | 本便の観測 | 差分 |
|---|---|---|
| 「モデルカード『📜 License & Ethical Restrictions』節の逐語」4 条 | v4-Small README:183-196・v4.1-Small README:69-82 で**逐語一致**（v4.1 も同文） | **追補**：v4.1 でも同一＝既定を v4.1 に上げても条項は変わらない |
| 「上流 `facebook/dacvae-watermarked` のライセンス表記は自己矛盾。解釈は卓の専管」 | **矛盾の出所が判明**＝GitHub 側 commit `34e0b0d`（2025-12-19 13:20 UTC）で SAM→Apache-2.0 に**修正済み**、HF カードだけ未追随（同日 17:25 UTC の更新でも本文は放置） | **訂正相当の追補**。「矛盾」から「**HF カードの写し忘れ**」へ格上げできる材料が揃った（断定は卓） |
| 「透かし: SilentCipher 透かしが常時埋め込まれる」 | **正確には「依存と重みが揃っているときだけ」**（README:296「when the dependency and model files are available」／`watermark.py`:36-50 は import 失敗・load 失敗のいずれも **warning のみで続行**） | **訂正**：「常時」ではない。**透かしなしで音が出る経路が正規に存在する**（＝オフライン配布で silentcipher を外すと透かしが消える）。payload は固定 `"IRDTS"`（`watermark.py`:10） |
| （前回言及なし） | DACVAE 内蔵の **Audioseal 透かしは Irodori 側が `decoder.alpha = 0.0` で無効化**（`codec.py`:84-96・モデルカード側 README:85-87 も同じ手順を公式に案内） | **追補**：透かしは 2 系統あり、乗るのは SilentCipher の 1 系統だけ |

### 配信用途に効く条項の整理（原文の範囲で）

1. **生成音声の公開・商用相当利用を禁じる条項は無い**（MIT 本則に加え、Ethical Restrictions 4 条のどれも配信・収益化を禁じていない）。
2. **1. No Impersonation** は「実在人物（声優・著名人・公人）の声を、明示的同意なくクローン／なりすまし」を禁止。
   → **読み分けちゃん2 で「参照音声」欄を露出するなら、ここが効く**。実在人物の音声を参照音声に入れる使い方を製品として推奨・容易化することは条項に触れうる。**UI の但し書き（同意のない実在人物の声を入れない）を出す形が素直**。
   → 逆に **caption（Voice Design）だけで声を作る運用は 1 に触れない**。
3. **2. No Misinformation** はディープフェイク・誤情報目的の禁止。通常の配信読み上げには効かない。
4. **3. Voice Generation Disclaimer** は「caption だけで作った声が偶然実在の声に似ることがある／潜在空間の確率的産物／特定個人の再現を意図していない」という**免責の宣言**。
   → **製品側の説明文にこの趣旨を書いておくのが安全**（開発者が既に免責している以上、利用者への周知は配布側の役目になる）。
5. **4. Liability Disclaimer**「The developers assume no liability for any misuse of this model. **Users are solely responsible** for ensuring their use of the generated content complies with applicable laws and regulations in their jurisdiction.」
   → **責任は利用者に一元化されている**＝読み分けちゃん2 が中間配布者になる場合、**この文言を配布物に転記しておく**のが実務上の要（MIT の「著作権表示とライセンス文の保持」とは別に、カード本文の条項として）。
6. **透かし**：SilentCipher の非可聴透かし（44.1kHz モデル・payload `"IRDTS"`）が**依存が揃っていれば**乗る。
   → **意図的に外せる**（silentcipher を入れない／モデル取得に失敗させる）が、**外すことを製品の既定にすると "promoting responsible AI usage"（v4-Small README:32）という上流の設計意図に逆行する**。ライセンス上の禁止ではないが、配信者向け製品としての姿勢の問題＝**卓の判断**。

---

## 7. 主席への注意

1. **§2-2 の「矛盾」は本便で局在が判明した**。報告では「上流が自己矛盾」ではなく「**HF カード本文だけが 2025-12-19 の修正に追随していない**（GitHub 側は同日修正済み・LICENSE 実体は初回から Apache-2.0）」と書けば、事実として正確かつ卓が裁定しやすい。§1-2 の時系列表をそのまま使える。
2. **門は「×」にならない**見込み。止まるとすれば「コーデック重みの**再配布**」だけで、それも「**同梱せず初回取得にする**」で回避でき、しかも回避コストはほぼゼロ（upstream が既に自動取得する設計）。**門を理由に⒜⒝を落とす根拠は無い**。
3. **唯一の空白は `Sony/SilentCipher` の重み**（license メタデータ無し・本文は code のみ言及）。ここだけは「**再配布しない＝初回取得**」で回避するのが安全。報告に「未確認」の札で残すべき。
4. **前回調査の 1 点を訂正する必要がある**：透かしは「常時」ではなく「依存と重みが揃っているときだけ」。`docs/` へ写す際に前回記述をそのまま引き継がないよう注意（§6 の表）。
5. **⒝（ONNX 化）に固有のライセンス論点がある**：SAM License 説を採ると §2-9 :40「reverse engineer, decompile or **discover the underlying components**」がコーデックの ONNX 変換に触れうる。Apache-2.0 説なら問題なし。**⒝を推す場合は、この一文が卓の裁定を要求する**（コーデック段だけ upstream の PyTorch 実装を残す退路も設計可能）。
6. **配布物に `licenses/` を自作する必要がある**（§4）。upstream 自身が license 文を配布物に持たないので、**⒜の作業見積りに「license 同梱の 1 手」を明示的に計上**しておくとよい（MIT の必須条件）。
7. **torchcodec は import 0 件**。§3 の指摘は⒜の配布サイズ（別席）にも効くので、渡しておくとよい。ただし `datasets` 経由の間接要求は**未確認**。
8. 未取得・未確認の札：`facebook/dacvae`（HF・401＝不在の公算）／`pytorch/pytorch` の LICENSE 全文／`sbintuitions/modernbert-ja-310m` の LICENSE 実体／`soundfile` 同梱 libsndfile（LGPL-2.1）の扱い／`dacvae` wheel に LICENSE が入るか。
