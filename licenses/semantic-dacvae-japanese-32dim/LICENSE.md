# Aratako/Semantic-DACVAE-Japanese-32dim（コーデックの重み）— MIT

**この檔の MIT 全文は、配布側（本リポジトリ）が用意したものである。**
上流の Hugging Face リポジトリには **LICENSE 檔が存在しない**（実測・下の §2）。
モデルカードが MIT を宣言しているので、宣言の出典を併記したうえで MIT の全文をここに置く。

対象＝`https://huggingface.co/Aratako/Semantic-DACVAE-Japanese-32dim`
（`weights.pth` 429,620,065 B ≈410 MB）。配布物には**同梱しない**（初回取得＝`decisions.md` 8）。

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
> `Copyright (c) 2026 Aratako` の行は**モデルカードには書かれていない**。上流のコード側の
> LICENSE 檔（`Copyright (c) 2026 Aratako`）に倣って**配布側が補った**ものであり、原文ではない。
> なお本モデルの初回 commit は 2026-03-14 で、年の表記が実際の権利発生年と一致する保証は無い。

---

## 2. MIT 宣言の出典

| # | 何 | 逐語・値 | 確認日 |
|---|---|---|---|
| 1 | モデルカード `README.md` の YAML メタデータ **:2** | `license: mit` | 2026-09-04 |
| 2 | 同 `README.md` **:29** | `* **License:** MIT` | 2026-09-04 |
| 3 | HF API `cardData.license` | `"mit"` | 2026-09-04 |
| 4 | **LICENSE 檔** `https://huggingface.co/Aratako/Semantic-DACVAE-Japanese-32dim/raw/main/LICENSE` | **HTTP 404**・`Entry not found` | 2026-09-04 |
| 5 | ライセンス変更の履歴 | **無し**（`56bfa86f17` initial → `818b64119d`(weights.pth) → `47376ee248`・一貫して `mit`） | 2026-09-04 |

- 取得元＝`https://huggingface.co/Aratako/Semantic-DACVAE-Japanese-32dim/raw/main/README.md`
  （HTTP 200・5,128 B）。取得方法＝`curl -sS -L`。
- 出典ノート＝`research/lab/notes/09-license-chain.md` §1-1 ④・§2-3、
  `research/lab/notes/16-verify-license.md` (e)。

---

## 3. 由来（帰属）

モデルカード本文の逐語：

- **:18**
  > `Like its predecessor, this model is based on [facebook/dacvae-watermarked](https://huggingface.co/facebook/dacvae-watermarked) and integrates WavLM semantic distillation inspired by the [Semantic-VAE paper](https://arxiv.org/abs/2509.22167).`
- **:24**
  > `* **Base Model:** Derived from [Semantic-DACVAE-Japanese](https://huggingface.co/Aratako/Semantic-DACVAE-Japanese) (originally [facebook/dacvae-watermarked](https://huggingface.co/facebook/dacvae-watermarked)).`
- **:112-113**
  > `* **Base Model:** Weights derived from [facebook/dacvae-watermarked](https://huggingface.co/facebook/dacvae-watermarked).`
  > `* **Training Code:** Leveraged the codebase from [Descript Audio Codec (DAC)](https://github.com/descriptinc/descript-audio-codec).`

由来の側のライセンスは `licenses/dacvae/LICENSE`（Apache-2.0 の原文）を参照。
**HF `facebook/dacvae-watermarked` のカード本文には「SAM License」という別の記述が残っている。
その件は `licenses/README.md` §4 に原文の檔と行だけを記した（結論は書かない＝停止域）。**

---

## 4. 透かしについての事実（解釈ではない）

- **DACVAE 内蔵の Audioseal 透かしは Irodori 側が無効化している**＝`decoder.alpha = 0.0`
  （`upstream/Irodori-TTS/irodori_tts/codec.py:84-96`）。モデルカード自身も **:85-87** で
  外す手順を案内している：
  > `# Disable/bypass the default watermark since this model was fine-tuned without it`
  > `model.decoder.alpha = 0.0`
  > `model.decoder.watermark = lambda x, message=None, d=model.decoder: d.wm_model.encoder_block.forward_no_conv(x)`
- 実際に生成音声に乗るのは **SilentCipher**（payload は固定文字列 `"IRDTS"`＝
  `upstream/Irodori-TTS/irodori_tts/watermark.py:10`）の 1 系統だけである。
  本ソフトウェアはこれを**既定 ON で使い、切る経路を持たない**（`decisions.md` 9）。
