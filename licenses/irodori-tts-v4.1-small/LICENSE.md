# Aratako/Irodori-TTS-v4.1-Small（モデルの重み）— MIT

**この檔の MIT 全文は、配布側（本リポジトリ）が用意したものである。**
上流の Hugging Face リポジトリには **LICENSE 檔が存在しない**（実測・下の §2）。
モデルカードが MIT を宣言しているので、宣言の出典を併記したうえで MIT の全文をここに置く。

対象＝`https://huggingface.co/Aratako/Irodori-TTS-v4.1-Small`
（revision `2b28324dc263ed5e6638b3cf3dd94c82ead07b4b`・`model.safetensors` 3,064,295,596 B・
`tokenizer/`）。配布物には**同梱しない**（初回取得＝`decisions.md` 8）。

---

## 1. MIT License（全文・配布側が用意した写し）

```
MIT License

Copyright (c) 2026 Aratako

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

> **著作権表示の行について（正直な注記）**
> `Copyright (c) 2026 Aratako` の行は**モデルカードには書かれていない**。
> 上流のコード側の LICENSE 檔（`upstream/Irodori-TTS/LICENSE`・`upstream/Irodori-TTS-Server/LICENSE`
> ＝いずれも `Copyright (c) 2026 Aratako`）に倣って**配布側が補った**ものであり、原文ではない。
> 本文（第 3 段落以降）は MIT の定型文そのものである。

---

## 2. MIT 宣言の出典（何を根拠に MIT と書いているか）

| # | 何 | 逐語・値 | 確認日 |
|---|---|---|---|
| 1 | モデルカード `README.md` の YAML メタデータ **:2** | `license: mit` | 2026-09-04 |
| 2 | 同 `README.md` **:69-82**（License & Ethical Restrictions 節） | `This model is released under **[MIT](https://choosealicense.com/licenses/mit/)**.` | 2026-09-04 |
| 3 | HF API `cardData.license` | `"mit"`／tag に `license:mit` | 2026-09-04 |
| 4 | **LICENSE 檔** `https://huggingface.co/Aratako/Irodori-TTS-v4.1-Small/raw/main/LICENSE` | **HTTP 404**・本文 `Entry not found`（`tree/main` にも無い） | 2026-09-04 |
| 5 | ライセンス変更の履歴 | **無し**（`39c24a9427` initial → `df20e70ca3` → `9712216387` → `2b28324dc2`・一貫して `mit`） | 2026-09-04 |
| 6 | v4-Small（`README.md`:183-196）との比較 | **当該節は sha256 一致**（`63b5ad44…`）＝v4 と v4.1 で逐語同一 | 2026-09-04 |

- 取得元＝`https://huggingface.co/Aratako/Irodori-TTS-v4.1-Small/raw/main/README.md`（HTTP 200・6,950 B）。
- 取得方法＝`curl -sS -L`。行番号は取得した raw 檔を `grep -n` / `sed -n` で数えた実測値。
- 出典ノート＝`research/lab/notes/09-license-chain.md` §1-1 ③'・§2-2、
  `research/lab/notes/16-verify-license.md` (e)・§3。

**重みだけを取ると、この license 文は手元に来ない**＝上流は
`allow_patterns = ["model.safetensors", "tokenizer/*"]`（`inference_runtime.py:559`）で絞り込んで取得し、
そもそも repo に LICENSE 檔が無い。**だから配布側が用意する必要がある**（MIT の必須条件）。

---

## 3. Ethical Restrictions（原文・逐語）

以下は `README.md`:69-82 の**逐語**である（v4-Small の :183-196 と sha256 一致）。
**訳は添えない**（訳による意味の揺れを避けるため）。

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

### 観測された事実（解釈ではない）

- 4 条のどこにも **watermark / 透かし** の語は無い（`grep -in` の当たり 0 件＝
  `research/lab/notes/16-verify-license.md` §3）。
- 配信・商用・生成音声の公開を禁じる条項は**無い**（3 カードに `commercial`／`redistribut`／
  `prohibit` の語が 1 語も無い＝同 §4-8）。
- 「1. No Impersonation」は**参照ボイスの運用に効く**（実在人物の声を同意なくクローンしない）。
- 「4. Liability Disclaimer」は**責任を利用者に一元化**している＝中間配布者になる本ソフトウェアは、
  この文言を配布物に転記しておく（本檔と `README.md` の 2 箇所）。

---

## 4. 由来（帰属）

- テキスト符号器＝`sbintuitions/modernbert-ja-310m`（**MIT**・LICENSE 実体 1,070 B・
  `Copyright (c) 2025 SB Intuitions`・`research/lab/notes/16` (h) で直読確認）。
  **重みに焼き込み済みで、取得は不要**（tokenizer は checkpoint repo の `tokenizer/` に同梱）。
- 透かし＝`SilentCipher`（`licenses/silentcipher/LICENSE`）。
- コーデック＝`Aratako/Semantic-DACVAE-Japanese-32dim`（`licenses/semantic-dacvae-japanese-32dim/LICENSE.md`）。
