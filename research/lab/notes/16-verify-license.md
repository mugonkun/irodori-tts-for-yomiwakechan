# 16 ライセンスの鎖・敵対検分（09-license-chain.md の主張を潰す試み）

> 席＝第 2 便サブ席（opus）・**敵対検分**／実施 2026-09-04。
> 取得日時＝すべて **2026-09-03 17:13〜17:32 UTC（＝2026-09-04 02:13〜02:32 JST）**。手段＝`curl -sS -L`（api.github.com / raw.githubusercontent.com / huggingface.co raw・api）＋ローカル実見。
> 生檔＝`C:\Users\mugonkun\AppData\Local\Temp\claude\...\scratchpad\v\`（セッション終了で消える前提）。行番号は取得した raw 檔を `grep -n` / `sed -n` で実際に数えた値。
> 書いたのは本檔のみ。upstream / yomiwakechan2 / C:/IrodoriTTS / C:/irodori-TTS-server / HF キャッシュは 1 バイトも触っていない（読取は `lab/.venv` と `upstream` の read のみ）。

## 要点（10 行以内）

1. **12 主張中 11 が CONFIRMED、0 が REFUTED、1 が「実質 CONFIRMED＋副次的に REFUTED（要修正）」**。門の結論（＝ライセンスは⒜⒝⒞を止めない）を覆す事実は出なかった。
2. (a) LICENSE は **5 リビジョン全部で sha256 一致**（`cfc7749b96f63bd3…`・11,358 B）・path=LICENSE の commit は **1 件のみ**＝CONFIRMED。
3. **ただし日付が誤り**＝`d39b77d` の「2025-10-03 13:36」は **author date**。**committer date（＝GitHub に載った日）は 2025-12-10T16:53:31Z**。09 §1-2 の表はこの区別をしていない。
4. **これが §1-3 の時系列論証を壊す**＝「dacvae 初回 commit(10-03) は SAM 3 の LICENSE(11-19) より前だから指しようがない」は **REFUTED**。公開順は SAM3 LICENSE **11-19** → dacvae GitHub 公開 **12-10** → HF カード本文に SAM 混入 **12-16**＝**全部あと**。
5. (g) **実証で確定＝dacvae の wheel に LICENSE は入っていない**。`dacvae-1.0.0.dist-info/RECORD` に LICENSE 行なし・`METADATA` に `License:` も `License-File:` も License 分類子も**一つも無い**。比較対象の `silentcipher-1.0.5.dist-info/licenses/LICENSE` は**入っている**。
6. (b)(c)(d)(e)(f)(j)(k)(l) はすべて逐語・バイト単位で CONFIRMED（(e) は 2 カードの当該節が **sha256 一致** `63b5ad44…`）。
7. (h) は **LICENSE 実体を直読して CONFIRMED**＝1,070 B・MIT・`Copyright (c) 2025 SB Intuitions`。09 ⑧の「未直読」札は**外してよい**。
8. (k) は**中身は正しいが行番号が 1 ずれ**＝`allow_patterns` は **:559**（:560 は `else:`）。09 §2-8 の「:559-560」表記と §4 の「:560」は要修正。
9. **追加問い＝Ethical Restrictions に「透かしを外すな」に類する義務表現は 1 文も無い**（4 条の逐語＝§3(e)）。逆に 32dim カード :85-87 は**外す手順を公式に案内**している。
10. 主席への注意は §4（**書き直すべき箇所 5 点**）。

---

## 1. 判定一覧（1 行ずつ）

| # | 主張 | 判定 | 根拠 |
|---|---|---|---|
| (a) | `facebookresearch/dacvae` の LICENSE は初回 `d39b77d` から Apache-2.0 全文で不変 | **CONFIRMED** | §2-1 |
| (a') | （09 §1-2 の付随記述）その初回 commit は「2025-10-03 13:36 UTC」 | **REFUTED（日付の意味）** | author date は 10-03、**committer date は 2025-12-10T16:53:31Z**。§2-1 |
| (b) | `34e0b0d`（2025-12-19 13:20 UTC）で README の "SAM License"→"Apache-2.0" | **CONFIRMED** | §2-2（差分は README.md 1 檔・+1/-1 のみ） |
| (c) | HF `facebook/dacvae-watermarked` は本文 "SAM License"・frontmatter apache-2.0・LICENSE 404 | **CONFIRMED** | §2-3 |
| (d) | HF `Sony/SilentCipher` は cardData に license 無し・LICENSE 檔無し・本文は code のみ | **CONFIRMED** | §2-4 |
| (e) | Aratako 3 リポは `license: mit`・本文 MIT・LICENSE 404／v4 と v4.1 の Ethical Restrictions は逐語同一 | **CONFIRMED** | §2-5（当該節 sha256 一致） |
| (f) | pin `d46d7d0` の LICENSE は MIT・Sony Research Inc.・`sony/silentcipher` とバイト一致 | **CONFIRMED** | §2-6（両方 1,075 B・sha256 `157b6af8…`） |
| (g) | `setup.py` の `license_files=("LICENSE.txt",)` で wheel に LICENSE が入らない恐れ | **CONFIRMED（恐れは現実になった）** | §2-7（実見） |
| (h) | `sbintuitions/modernbert-ja-310m` は MIT | **CONFIRMED（実体を直読）** | §2-8 |
| (i) | SAM License §1.b.i / §1.b.iv の逐語が実在 | **CONFIRMED** | §2-9（1 字だけ差＝§2-9 末尾） |
| (j) | README:296 の逐語／`watermark.py` が失敗を warning で握って続行 | **CONFIRMED** | §2-10 |
| (k) | `allow_patterns`＝model.safetensors と tokenizer/*、codec は weights.pth のみ | **CONFIRMED（行番号のみ要修正）** | §2-11 |
| (l) | v4.1 の `model.safetensors` は 3,064,295,596 B | **CONFIRMED** | §2-12（API と HTTP header の 2 経路で一致） |
| 追加 | Ethical Restrictions に「透かしを外すな」相当の義務表現があるか | **無い（＝09 の暗黙の前提を支持）** | §3 |

---

## 2. 各主張の検分（逐語つき）

### 2-1 (a) LICENSE 檔の不変性 ＝ CONFIRMED／付随の日付は REFUTED

`GET https://api.github.com/repos/facebookresearch/dacvae/commits?path=LICENSE&per_page=100`（200）

```
count 1
d39b77d58984f57295678ad8fd172f713b435e62  author=2025-10-03T13:36:46Z  committer=2025-12-10T16:53:31Z  'Initial Commit'
```

`raw.githubusercontent.com/facebookresearch/dacvae/{ref}/LICENSE` を 5 本取り、全部 **HTTP 200・11,358 B**：

| ref | sha256 |
|---|---|
| `d39b77d5898…`（初回） | `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30` |
| `34e0b0dbb64…` | 同 |
| `2807862a058…` | 同 |
| `414c20785fc…`（HEAD・lab の venv が入れた commit） | 同 |
| `main` | 同 |

先頭 3 行（逐語・1 行目は空行）：
> （2）`                                 Apache License`
> （3）`                           Version 2.0, January 2004`
> （4）`                        http://www.apache.org/licenses/`

→ **反証できず＝CONFIRMED。** 09 §1-2 の「LICENSE の commit 履歴はこの 1 件のみ」も一致。

**ただし日付**：repo 全体の commit は 4 件で、author / committer が食い違うのは初回だけ。

```
414c20785f  author=2025-12-22T14:58:12Z  committer=2025-12-22T14:58:12Z  'Merge pull request #7 from bigpon/patch-1'
2807862a05  author=2025-12-19T20:46:43Z  committer=2025-12-19T20:46:43Z  'Update README.md'
34e0b0dbb6  author=2025-12-19T13:20:30Z  committer=2025-12-19T13:20:30Z  'Update license in README'
d39b77d589  author=2025-10-03T13:36:46Z  committer=2025-12-10T16:53:31Z  'Initial Commit'
```

repo の `created_at` は `2025-10-03T13:15:55Z`（09 §2-6 が引いた値）。
→ **09 §1-2 の「2025-10-03 13:36 GitHub」は「Meta 社内で書かれた日」であって「GitHub に出た日」ではない。** 実際に GitHub に載ったのは **2025-12-10**。

### 2-2 (b) commit 34e0b0d ＝ CONFIRMED

`GET /repos/facebookresearch/dacvae/commits/34e0b0dbb6400140da62f8f6366359e2f0377a1c`（200）

```
msg: 'Update license in README'
date: 2025-12-19T13:20:30Z
FILE README.md modified +1 -1
@@ -40,4 +40,4 @@
 ## License

-This project is licensed under the SAM License - see the [LICENSE](LICENSE) file for details.
+This project is licensed under Apache-2.0 - see the [LICENSE](LICENSE) file for details.
```

**変更は README.md 1 檔だけ**（LICENSE 檔には触れていない）。初回版 README:43 と main の README:67 も 09 の記述どおり（`grep -n -i licen`：初回 `41:## License` / `43:… the SAM License …`、main `65:## License` / `67:… Apache-2.0 …`）。→ CONFIRMED。

### 2-3 (c) HF `facebook/dacvae-watermarked` ＝ CONFIRMED

- `raw/main/README.md`（**200・1,715 B**）：`2:license: apache-2.0` ／ `44:## License` ／ `46:This project is licensed under the SAM License - see the [LICENSE](LICENSE) file for details.`
- `raw/main/LICENSE` → **404**・本文 `Entry not found`（15 B）
- `api/models/facebook/dacvae-watermarked`（200）：`cardData {'license': 'apache-2.0'}` ／ `tags ['license:apache-2.0','region:us']` ／ `lastModified 2025-12-19T17:25:47.000Z` ／ `siblings ['.gitattributes','README.md','weights.pth']`

**commit 履歴（09 §1-2 の補正）**＝`api/models/facebook/dacvae-watermarked/commits/main`（200）：

```
8680102d1418  2025-12-19T17:25:47Z  'Update README.md'
9ca52e8c33d3  2025-12-16T15:54:42Z  'Upload README.md with huggingface_hub'
01ea7995d62d  2025-10-14T16:12:59Z  'Upload weights.pth with huggingface_hub'   ← 09 の表に無い
845726a7663b  2025-10-06T13:56:28Z  'Upload weights.pth with huggingface_hub'   ← 09 の表に無い
b551294d3d7b  2025-10-03T14:15:23Z  'Upload weights.pth with huggingface_hub'   ← 09 の表に無い
07fe69464c6a  2025-10-03T14:14:50Z  'initial commit'
```

各版の実体も確認（`raw/<rev>/README.md`）：`07fe69464c`＝31 B・`2:license: apache-2.0` のみ／`01ea7995d6`＝**31 B・同じ**（09 は「〜2025-12-16 は全 3 行」と書いており一致）／`9ca52e8c33`＝1,688 B・`43:… the SAM License …`（frontmatter 無し）／`8680102d14`＝1,715 B・`2:license: apache-2.0` と `46:… the SAM License …` が**同居**。
→ **09 §1-2 の 3 行（HF 側）はすべて正しい。重み upload 3 件が抜けているだけ（license には無関係）。** CONFIRMED。

### 2-4 (d) HF `Sony/SilentCipher` ＝ CONFIRMED

`api/models/Sony/SilentCipher`（200）：
```
id Sony/SilentCipher | cardData None
tags ['transformers','arxiv:2406.03822','endpoints_compatible','region:us']
gated False | lastModified 2024-06-18T06:26:10.000Z
siblings ['.gitattributes','16_khz/97561_iteration/{dec_c,dec_m_0,enc_c,opt}.ckpt','16_khz/97561_iteration/hparams.yaml',
          '44_1_khz/73999_iteration/…同構成…','README.md','config.json']
```
- 小文字 `api/models/sony/silentcipher` も **同一 repo に正規化**され（`id Sony/SilentCipher`）、`cardData None`。＝コード側の `snapshot_download(repo_id="sony/silentcipher")` と同じ物。
- `raw/main/LICENSE` → **404**。`siblings` にも LICENSE 無し。
- README（200・7,794 B）で `grep -in licen` の当たりは **2 行だけ**：
  > `133:# License`
  > `135:- The code in this repository is released under the MIT license as found in the [LICENSE file](LICENSE).`
  （＝その [LICENSE file] リンク先が HF 側に**存在しない**）

**潰しに行った追加検分**（重みの条件がどこかに書いてないか）：
- GitHub `sony/silentcipher/master/README.md`（200・7,268 B）も **同文 1 行だけ**＝`128:- The code in this repository is released under the MIT license…`。重みへの言及は `47:Find the latest models … in the release section`／`48:The models have also been released on [HuggingFace](…)` のみで**条件を述べていない**。
- `api.github.com/repos/sony/silentcipher/releases`（200・2 件）＝`release`(2024-06-16, body **空**)／`pre-release`(2024-06-07, body `'Initial packages. Help us solve the bugs by posting issues!'`)＝**license 記述なし**。

→ **反証できず。09 ⑦' の「唯一の空白／未確認」札はそのまま正しい。** CONFIRMED。

### 2-5 (e) Aratako 3 リポ ＝ CONFIRMED

`api/models/…`（すべて 200）：

| repo | cardData.license | tags | LICENSE(raw/main) | siblings に LICENSE |
|---|---|---|---|---|
| `Aratako/Irodori-TTS-v4-Small` | `mit` | `license:mit` | **404** | 無し |
| `Aratako/Irodori-TTS-v4.1-Small` | `mit` | `license:mit` | **404** | 無し |
| `Aratako/Semantic-DACVAE-Japanese-32dim` | `mit` | `license:mit` | **404** | 無し |

README の `grep -in licen`：
- v4-Small（19,959 B）：`2:license: mit` / `183:## 📜 License & Ethical Restrictions` / `185:### License` / `187:This model is released under **[MIT](https://choosealicense.com/licenses/mit/)**.` / `191:In addition to the license terms, …`
- v4.1-Small（6,950 B）：`2:license: mit` / `69:` / `71:` / `73:`（同文）/ `77:`
- 32dim（5,128 B）：`2:license: mit` / `29:* **License:** MIT`

**逐語同一の検証**＝v4 の :183-200 と v4.1 の :69-86 を切り出して `diff`＝**差分ゼロ**、`sha256` も一致 `63b5ad44cd9d1b302057e84aafe635c5677700d9cbb163e2c9b475276947812c`。Ethical Restrictions 4 条の逐語は §3 に再掲。
→ CONFIRMED。**さらに**：3 カードとも `commercial` / `redistribut` / `non-commercial` / `research only` / `prohibit` の**当たり 0 件**＝再配布・商用を禁じる語は 1 語も無い（09 の結論を独立に支持）。

### 2-6 (f) silentcipher の LICENSE バイト一致 ＝ CONFIRMED

| 取得先 | HTTP | size | sha256 |
|---|---|---|---|
| `raw…/SesameAILabs/silentcipher/d46d7d0893a583d8968ab3a6626e2289faec9152/LICENSE` | 200 | **1,075** | `157b6af8c1bc4eac646abdf17473934a85e8815ade3a346cd83a58a171586a73` |
| `raw…/sony/silentcipher/master/LICENSE` | 200 | **1,075** | **同一** |
| （`sony/silentcipher/main/LICENSE` は 404＝既定枝は `master`） | 404 | 14 | — |

逐語（pin 版 :1-3）：
> `MIT License`
> （空行）
> `Copyright (c) 2024 Sony Research Inc.`

`api.github.com/repos/SesameAILabs/silentcipher`＝`fork True` / `parent sony/silentcipher` / `license MIT`。→ CONFIRMED。

### 2-7 (g) dacvae wheel に LICENSE が入るか ＝ **実証：入っていない**

上流檔（`raw…/dacvae/414c20785f…/setup.py`・200）:
> `1:# Copyright (c) Meta Platforms, Inc. and affiliates. All Rights Reserved\n`
> `28:    license_files = ("LICENSE.txt",),`
（実体の檔名は `LICENSE`＝`.txt` 無し。§2-1 の tree で確認済み）

**実見**（`C:/Users/mugonkun/source/repos/irodori-native-research/lab/.venv/Lib/site-packages/dacvae-1.0.0.dist-info/`・読むだけ）:

```
INSTALLER  METADATA(4203)  RECORD(1506)  REQUESTED  WHEEL(91)  direct_url.json(132)  top_level.txt  uv_build.json
```

- **`LICENSE` も `licenses/` も無い。**
- `RECORD` 全 21 行に LICENSE 行**なし**（`dacvae-1.0.0.dist-info/{INSTALLER,METADATA,RECORD,REQUESTED,WHEEL,direct_url.json,top_level.txt,uv_build.json}` と `dacvae/**.py` のみ）。
- **`METADATA` の `grep -in license` の当たりは 2 行だけ**、しかもそれは長文説明（README の写し）の中：
  > `116:## License`
  > `118:This project is licensed under Apache-2.0 - see the [LICENSE](LICENSE) file for details.`
  ＝**`License:` 欄も `License-File:` 欄も `Classifier: License :: …` も 1 つも無い。**（`Metadata-Version: 2.4` / `Generator: setuptools (84.0.0)`）
- `direct_url.json` = `{"url":"https://github.com/facebookresearch/dacvae","vcs_info":{"vcs":"git","commit_id":"414c20785fc3a28373073ea8ef7a1316eeeaca6e"}}`（11-lab-setup §3 の commit と一致）

**対照実験**＝同じ venv の `silentcipher-1.0.5.dist-info/` には **`licenses/LICENSE`（1,096 B）が入っている**。`METADATA`：
> `9:Classifier: License :: OSI Approved :: MIT License`
> `13:License-File: LICENSE`
> `24:Dynamic: license-file`
そして中身は GitHub の 1,075 B と **CRLF 変換を戻すと完全一致**（`a.replace(b'\r\n', b'\n') == b` → `True`。差 21 B ＝ 21 行）。

→ **09 §5 の「（未確認）wheel に入らない恐れ」は恐れではなく事実。「LICENSE を GitHub から自分で取って添える」は必須手順。** CONFIRMED（かつ札を「未確認」から外せる）。

### 2-8 (h) `sbintuitions/modernbert-ja-310m` ＝ CONFIRMED（実体を直読）

- `api/models/sbintuitions/modernbert-ja-310m`（200）＝`cardData.license = mit` / `tags ['license:mit']` / `siblings` に **`LICENSE` あり**
- README（200・16,302 B）：`5:license: mit` / `194:## License` / `196:[MIT License](https://huggingface.co/sbintuitions/modernbert-ja-310m/blob/main/LICENSE)`
- **`raw/main/LICENSE`（200・1,070 B・sha256 `284353c80e0d52c06e97af62fd40e4dd1d253fa28f5d1e756121ea1d5d441509`）** 逐語 :1-3
  > `MIT License`
  > （空行）
  > `Copyright (c) 2025 SB Intuitions`

→ CONFIRMED。**09 ⑧の「LICENSE 実体は未直読」札は外してよい。著作権者は `SB Intuitions`（2025）。**

### 2-9 (i) SAM License の逐語 ＝ CONFIRMED（1 字の差あり）

`https://raw.githubusercontent.com/facebookresearch/sam3/main/LICENSE`（200・7,353 B）
- `1:SAM License` / `2:Last Updated: November 19, 2025`
- `33:` 逐語
  > `i. Distribution of SAM Materials, and any derivative works thereof, are subject to the terms of this Agreement. If you distribute or make the SAM Materials, or any derivative works thereof, available to a third party, you may only do so under the terms of this Agreement and you shall provide a copy of this Agreement with any such SAM Materials.`
- `40:` 逐語
  > `iv. Your use of the SAM Materials will not involve or encourage others to reverse engineer, decompile or discover the underlying components of the SAM Materials.`
- `41:`（09 が「§1.b.v（Trade Controls / ITAR）」と呼んだ条項の実体）
  > `v. You are not the target of Trade Controls and your use of SAM Materials must comply with Trade Controls. You agree not to use, or permit others to use, SAM Materials for any activities subject to the International Traffic in Arms Regulations (ITAR) or end uses prohibited by Trade Controls, including those related to military or warfare purposes, nuclear industries or applications, espionage, or the development or use of guns or illegal weapons.`

→ 行番号・逐語ともに CONFIRMED。
**微修正 1 点**＝09 §2-9 の :30 引用は `Meta's intellectual property` と ASCII アポストロフィで写しているが、原文は **U+2019（`Meta’s`）**。報告に逐語で載せるなら原字で。

**（i) に付随する REFUTED＝§1-3 の時系列論証）**
`api.github.com/repos/facebookresearch/sam3`＝`created_at 2025-07-17T16:15:40Z`・`license {'spdx_id': 'NOASSERTION'}`。
`commits?path=LICENSE`＝**1 件のみ** `a13e358df4` author `2025-11-19T07:07:42Z` / committer `2025-11-19T07:07:54Z`（`'Initial commit\n\nfbshipit-source-id: …'`）。
参考：`facebookresearch/sam2` は `Apache-2.0`（＝「SAM License」という名の檔は SAM3 以前に公開されていない。**未確認**＝Meta 社内の非公開版まではたどれない）。

**公開の時系列（committer date で並べ直したもの）**

| UTC | 出来事 |
|---|---|
| 2025-10-03 13:15 | GitHub `facebookresearch/dacvae` repo 作成（中身は空） |
| 2025-10-03 14:14 | HF `facebook/dacvae-watermarked` 初回＝31 B frontmatter（`license: apache-2.0`）のみ |
| 2025-10-03〜10-14 | HF に weights.pth を 3 回 upload（README は 31 B のまま） |
| **2025-11-19 07:07** | **GitHub `facebookresearch/sam3` に「SAM License」檔が初めて公開される** |
| **2025-12-10 16:53** | **GitHub `dacvae` の初回 commit `d39b77d` が push される（README:43 に "SAM License"・LICENSE は Apache-2.0）** |
| 2025-12-16 15:54 | HF カードが GitHub の当時の本文で上書きされ "SAM License" が混入（frontmatter 消滅） |
| 2025-12-19 13:20 | GitHub `34e0b0d` で "SAM License"→"Apache-2.0" |
| 2025-12-19 17:25 | HF が frontmatter を復活・本文は放置（現在に至る） |

→ **09 §1-3 の括弧書き「SAM 3 は 2025-11-19 版・dacvae 初回 commit は 2025-10-03＝時系列上、dacvae の "SAM License" は SAM 3 のそれを指しようがない」は REFUTED。**
公開順では SAM License(11-19) が dacvae の公開(12-10) より **前**。「指しようがない」とは言えない。
（言えるのは「**社内で書かれた日**は 10-03 で SAM3 LICENSE の公開より前」までで、これは "テンプレート流用" 説を弱めも強めもしない。**この一文は報告から落とすか、上表に差し替えるべき。**）

### 2-10 (j) 透かしは「揃っているときだけ」＝ CONFIRMED

`upstream/Irodori-TTS/README.md:296`（逐語）
> `Generated audio is passed through [SilentCipher](https://github.com/sony/silentcipher) watermarking automatically when the dependency and model files are available.`

`upstream/Irodori-TTS/irodori_tts/watermark.py`（逐語・行番号は `grep -n` 実測）
> `10:IRODORI_WATERMARK_PAYLOAD = (73, 82, 68, 84, 83)  # "IRDTS"`
> `34:        try:` / `35:            import silentcipher` / `36:        except ImportError:` / `37:            logger.warning(`
> `38:                "SilentCipher package is unavailable; generated audio will not be watermarked."` / `39:            )` / `40:            return None`
> `42:        try:` / `43:            return silentcipher.get_model(model_type=model_type, device=device)` / `44:        except Exception as exc:` / `45:            logger.warning(`
> `46:                "SilentCipher model could not be loaded (%s); generated audio will not be "` / `47:                "watermarked.",` / `48:                exc,` / `49:            )` / `50:            return None`
> `52:    @property` / `53:    def ready(self) -> bool:` / `54:        return self.model is not None`

→ import 失敗（`ImportError`）もロード失敗（**裸の `Exception`**＝ネットワーク・檔欠損・重み破損を全部含む）も **`return None` して続行**。`ready` が False になるだけで例外は上がらない。**CONFIRMED**（09 §6 の「訂正」は正しい）。

### 2-11 (k) 重みだけ取る ＝ CONFIRMED（**行番号は 1 ずれ**）

`upstream/Irodori-TTS/irodori_tts/inference_runtime.py`（`grep -n` 実測）
> `554:    from huggingface_hub import snapshot_download`
> `558:        checkpoint_relative = Path("model.safetensors")`
> **`559:        allow_patterns = ["model.safetensors", "tokenizer/*"]`**
> `560:    else:`（← 09 が `allow_patterns` の一部として引いた行は実際には `else:`）
> `568:        snapshot_download(` / `569:            repo_id=repo_id,` / `570:            allow_patterns=allow_patterns,`

`upstream/Irodori-TTS/irodori_tts/codec.py`
> `9:from huggingface_hub import hf_hub_download`
> `51:        repo_id: str = "Aratako/Semantic-DACVAE-Japanese-32dim",`
> `70:        if not Path(location).exists() and "/" in location and not location.endswith(".pth"):`
> **`72:                location = hf_hub_download(repo_id=location, filename="weights.pth")`**

→ **中身は CONFIRMED**（README も LICENSE も取らない）。**ただし 09 §2-8 の「:559-560」と §4 の「(`inference_runtime.py`:560)」は :558-559 / :559 に直す必要がある。**
なお **補足の重要点**＝仮に `allow_patterns` を外しても、v4/v4.1/32dim の HF repo には **そもそも LICENSE が無い**（§2-5 の siblings）。つまり「license 文が手元に来ない」原因は allow_patterns **だけではない**＝上流に檔が無いことが根本。09 §4 の書き方はやや allow_patterns に寄っている。

### 2-12 (l) v4.1 の重みサイズ ＝ CONFIRMED（2 経路一致）

- `api/models/Aratako/Irodori-TTS-v4.1-Small?blobs=true`：`model.safetensors  size=3064295596  lfs.size=3064295596`
- `curl -I .../resolve/main/model.safetensors`：`HTTP/1.1 302` → `X-Linked-Size: 3064295596` → 実体 `HTTP/1.1 200` / `content-length: 3064295596`
- 参考（09 §1-5 の他の値も全部照合済み・全一致）：v4 `model.safetensors 3064295596` / `README.md 19959`、v4.1 `README.md 6950` / `tokenizer/tokenizer.json 6718495` / `tokenizer/tokenizer_config.json 668` / `EMOJI_ANNOTATIONS.md 3403`、32dim `weights.pth 429620065` / `README.md 5128`、dacvae-watermarked `weights.pth 430785157` / `README.md 1715`。

→ CONFIRMED。3,064,295,596 B = 2.854 GiB。

---

## 3. 追加問い＝Ethical Restrictions に「透かしを外すな」に類する義務表現はあるか ＝ **無い**

v4-Small README:183-196（＝v4.1-Small README:69-82 と **sha256 一致**）の全文（逐語）：

> `## 📜 License & Ethical Restrictions`
> `### License`
> `This model is released under **[MIT](https://choosealicense.com/licenses/mit/)**.`
> `### Ethical Restrictions`
> `In addition to the license terms, the following ethical restrictions apply:`
> `1.  **No Impersonation:** Do not use this model to clone or impersonate the voice of any individual (e.g., voice actors, celebrities, public figures) without their explicit consent.`
> `2.  **No Misinformation:** Do not use this model to generate deepfakes or synthetic speech intended to mislead others or spread misinformation.`
> `3.  **Voice Generation Disclaimer:** When generating speech purely from text or captions without using reference audio, it is possible that the generated voice may coincidentally resemble that of a real person. This is strictly a probabilistic artifact within the latent space. The model was not trained with the intent of reproducing specific individuals.`
> `4.  **Liability Disclaimer:** The developers assume no liability for any misuse of this model. Users are solely responsible for ensuring their use of the generated content complies with applicable laws and regulations in their jurisdiction.`

**4 条のどこにも watermark / 透かし の語が無い。** カード全体で `grep -in "watermark|remove|strip|透かし"` の当たりは：
- v4-Small：`32:  * **Integrated Watermarking:** Integrates [SilentCipher](…) to apply robust, invisible audio watermarks directly to generated outputs, promoting responsible AI usage.`（＝**機能の説明であって義務文ではない**。"must"/"shall"/"do not remove" のいずれも無い）／`205:` は謝辞
- v4.1-Small：`91:` の謝辞 **1 件のみ**（機能説明すら無い）
- 32dim：`85-87` は**外し方の案内**
  > `85:# Disable/bypass the default watermark since this model was fine-tuned without it`
  > `86:model.decoder.alpha = 0.0`
  > `87:model.decoder.watermark = lambda x, message=None, d=model.decoder: d.wm_model.encoder_block.forward_no_conv(x)`

→ **結論（断定）：モデルカードのライセンス／倫理条項に「透かしを保持せよ」「除去するな」に相当する義務表現は存在しない。** 透かしを外すことは**ライセンス違反にならない**。ただし v4-Small :32 の "promoting responsible AI usage" という設計意図の表明はある（＝09 §6-6 の整理はそのまま妥当）。

---

## 4. 主席への注意（報告で書き直すべき箇所）

1. **【要修正・中】09 §1-3 の括弧書きを落とすか差し替える。**「dacvae 初回 commit は 2025-10-03 ＝ SAM 3(2025-11-19) より前だから指しようがない」は **REFUTED**。GitHub に公開されたのは **2025-12-10 16:53 UTC**（committer date）で SAM License 公開の**あと**。§2-9 末尾の時系列表をそのまま差し替えに使える。**「時系列上ありえない」という強い言い方は報告から消すこと。**
2. **【要修正・小】09 §1-2 の表の日付列に「author / committer」の別を明記する。** `d39b77d` だけ author 10-03 13:36 / committer 12-10 16:53 と食い違う（他 3 commit は一致）。表のまま報告に写すと 1 の誤りが再生産される。
3. **【札を外せる・中】(g) は実証で確定した。** 09 §5 の「（未確認：実際の wheel の中身は未検証）」を削り、**断定**に格上げしてよい：`dacvae-1.0.0.dist-info/` に LICENSE は無く、METADATA には `License:` 欄も `License-File:` 欄も分類子も**一つも無い**。対照の `silentcipher-1.0.5.dist-info/licenses/LICENSE`（1,096 B・CRLF 差のみで GitHub 版と同一）は入っている。**⒜の工数見積の「licenses/ 自作 1 手」は"念のため"ではなく"必須"。**
4. **【札を外せる・小】(h) の「LICENSE 実体は未直読」を外す。** 直読済み＝1,070 B・MIT・`Copyright (c) 2025 SB Intuitions`。09 §7-8 の未確認リストからも 1 件減る。
5. **【要修正・小】(k) の行番号。** `allow_patterns` は `inference_runtime.py:559`（:560 は `else:`）。09 §2-8 の「:559-560」と §4 の「:560」を **:558-559 / :559** に。あわせて §4 の因果を 1 語補う：**license 文が来ないのは allow_patterns のせいだけでなく、v4/v4.1/32dim の HF repo に LICENSE 檔が最初から無いから**。
6. **【補強・小】§2-9 の :30 引用のアポストロフィが原文と違う**（`Meta's` → 原文は `Meta’s`・U+2019）。逐語で載せるなら原字で。
7. **【補強・小】09 §1-2 の HF 側履歴に weights.pth upload 3 件（2025-10-03 14:15 / 10-06 13:56 / 10-14 16:12）が抜けている。** license には無関係なので表を増やす必要は無いが、「履歴は全 6 件でうち README 系は 3 件」と一言添えると再検証されたとき齟齬が出ない。
8. **【追認・重要】09 の門の結論は覆らない。** 独立に確認した支持材料 3 つ＝(i) Aratako 3 カードに `commercial`/`redistribut`/`prohibit` 等の語が **1 語も無い**、(ii) `Sony/SilentCipher` の重みの条件は GitHub README・HF README・GitHub Releases の**どこにも書かれていない**（＝「初回取得で回避」の方針は妥当・09 ⑦' の未確認札は正しい）、(iii) **Ethical Restrictions に透かし保持義務は無い**（§3）。
9. **【新規・卓向け】透かしについて報告に 1 行足せる**：「モデルカードは透かしの**保持を義務づけていない**（32dim カードは外し方を公式に案内すらしている）。よって透かしを切るかどうかは**ライセンス問題ではなく製品姿勢の問題**」＝09 §6-6 の言い方をこの根拠で断定に上げられる。
10. **未確認のまま残るもの**（本便でも埋まらなかった）＝`facebook/dacvae`（HF・**401 を再現**＝`{"error":"Invalid username or password."}`。非公開/不在の公算だが断定不可）／`Sony/SilentCipher` の**重み**の license／Meta 社内に 2025-10-03 以前の「SAM License」があったか／`pytorch/pytorch` LICENSE 全文／`soundfile` 同梱 libsndfile。
