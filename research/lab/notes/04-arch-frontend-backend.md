# 04 — 推論の前段（テキスト処理・トークナイザ）と後段（コーデック・透かし）／ONNX 化の可否

読解席（opus）。担当＝BRIEF §4-2 の材料のうち「前段」と「後段」。読むだけで完了、1 バイトも書き換えていない。

## 要点（10 行以内）

1. **G2P・音素化は無い**（upstream 全体に `pyopenjtalk`／`g2p`／`phoneme` の語が 0 件）。生テキスト → 正規化 → **ModernBERT-ja の Unigram トークナイザ**へ直行。
2. 正規化は純粋な文字列処理 13 行分（辞書 10・正規表現 4・括弧剥がし・NFKC・`...`→`…`）で、**外部辞書ゼロ＝C# へ逐語移植できる**。ただし NFKC が `…`→`...` に潰す順序依存が罠。
3. トークナイザは**チェックポイント同梱**（`tokenizer/tokenizer.json` 6.7MB・Unigram・語彙 102400・byte_fallback・Metaspace・normalizer null）。HF ネットワーク不要。絵文字は 45 個中 37 個がバイト分解。
4. コーデックは **48kHz・hop 1920（25 fps）・潜在 32 次元**の DACVAE。upstream 既定（44.1k/hop512）とは別配置＝Aratako 氏の再学習。
5. **DACVAE 内蔵の透かし枝（`facebook/dacvae-watermarked` 由来）は使われていない**＝`alpha=0.0` にして `watermark` を差し替え（`codec.py:82-96`）。重みは checkpoint に残るが推論経路から外れる。
6. デコーダの実行経路は **weight_norm 付き Conv1d／ConvTranspose1d／Snake／ELU／Tanh のみ・形状演算なし**＝ONNX 化は素直（動的長も自然に通る）。weight_norm の畳み込みは事前に fold する。
7. エンコーダは**参照 wav を渡すときだけ**必要（潜在／話者埋め込み指定なら不要）。ただし codec ロード時に latent_dim 推定で 1 回必ず走る（`codec.py:103-105`）。
8. 透かしは **SilentCipher 44.1k・payload "IRDTS"（5 バイト＝40bit）固定・on/off フラグ無し**（import できて重みが読めれば必ず掛かる）。48k↔44.1k の往復リサンプルが 1 発声ごとに 2 回。
9. SilentCipher は `torch.stft`／`torch.istft` を使う＝**まるごと ONNX 化は不可の見込み**。ただし**掛ける側は enc_c＋dec_c の 2 ネット（計約 2.2MB）だけ**＝STFT/iSTFT を C# で書けば ONNX 化できる。
10. 出力後処理にラウドネス正規化は**無い**（正規化は参照音声側だけ・`audiotools`＝julius/scipy 依存）。出力は尻尾刈り→透かし→clamp→符号化のみ。

---

## 0. 参照の付け方

- `upstream/...` ＝ `C:\Users\mugonkun\source\repos\irodori-native-research\upstream\...`
- `[venv]/...` ＝ `C:\irodori-TTS-server\Irodori-TTS-Server\.venv-rocm\Lib\site-packages\...`（稼働機・読むだけ）
- `[hf]/...` ＝ `C:\Users\mugonkun\.cache\huggingface\hub\...`（読むだけ・重みは torch.load していない。zip の `data.pkl` だけを pickle で読み、テンソル本体は persistent_load で捨てた）

Python を触った箇所は `PYTHONDONTWRITEBYTECODE=1` を付けた。clone 側に `__pycache__` が生成されていないことを実測で確認済み。

---

## 1. テキスト前段

### 1-1. 正規化規則の全列挙（`upstream/Irodori-TTS/irodori_tts/text_normalization.py`）

適用順は `normalize_text`（:60-74）で ①単純置換 → ②正規表現 → ③外側括弧剥がし → ④NFKC → ⑤`...`/`..` → `…` の 5 段。**この順序が結果を変える**（後述 1-3）。

**① `SIMPLE_REPLACE_MAP`（:6-17、`str.replace` を辞書順に適用）**

| 対象 | 置換後 | 行 |
|---|---|---|
| `\t`（タブ） | 削除 | :7 |
| `[n]` | 削除 | :8 |
| `\[n\]`（リテラル文字列としての `\[n\]`） | 削除 | :9 |
| `　`（U+3000 全角空白） | 削除 | :10 |
| `？` | `?` | :11 |
| `！` | `!` | :12 |
| `♥` | `♡` | :13 |
| `●` | `○` | :14 |
| `◯` | `○` | :15 |
| `〇` | `○` | :16 |

注：:9 は `re` ではなく `str.replace` に渡るので、**入力に literal `\[n\]` という 5 文字が含まれるときだけ**効く（ほぼデッドコード）。

**② `REGEX_REPLACE_MAP`（:19-24）**

| 正規表現（原文逐語） | 置換後 | 行 |
|---|---|---|
| `[;▼♀♂《》≪≫①②③④⑤⑥]` | `""` | :20 |
| `[\u02d7\u2010-\u2015\u2043\u2212\u23af\u23e4\u2500\u2501\u2e3a\u2e3b]` | `""` | :21 |
| `[\uff5e\u301C]` | `ー` | :22 |
| `…{3,}` | `……` | :23 |

:21 は各種ダッシュ・ハイフン・罫線（modifier minus、U+2010〜U+2015、hyphen bullet、minus sign、horizontal bar、wave dash 系罫線、box drawings、two/three-em dash）の一括削除。

**③ `strip_outer_brackets`（:27-57）** — 対 `{「:」, 『:』, （:）, 【:】, (:)}`（:28）。文字列全体を 1 対が包んでいる場合のみ剥がし、剥がせる限り繰り返す。深さ判定つき＝`（A）（B）` のような並びは剥がさない。

**④ `unicodedata.normalize("NFKC", text)`（:69）**

**⑤ `"..." → "…"`、`".." → "…"`（:71-72）**

### 1-2. 実測（`normalize_text` を単体 import して確認）

| 入力 | 出力 |
|---|---|
| `「こんにちは…」` | `こんにちは…` |
| `あ…………ん` | `あ……ん` |
| `ＡＢＣ１２３` | `ABC123` |
| `テスト～です` | `テストーです` |
| `（笑）` | `笑` |
| `え、そうなの？！` | `え、そうなの?!` |
| `囁き👂です` | `囁き👂です`（絵文字は素通り） |
| `ｱｲｳ` | `アイウ` |
| `①②③テスト` | `テスト` |
| `⑦テスト` | `7テスト`（①〜⑥だけ削除、⑦以降は NFKC で数字化） |
| `Ⅲ章` | `III章` |
| `㈱テスト` | `(株)テスト` |
| `‥`（U+2025） | `…`（NFKC で `..` → ⑤で `…`） |
| `･･･`（半角中黒） | `・・・` |
| `；セミコロン` | `;セミコロン`（**全角 `；` は②を通過し NFKC で `;` になる＝残る**） |
| `♀♂` | `` |
| `こんにちは。。` | `こんにちは。。`（全角句点は対象外） |

### 1-3. C# 移植で踏む罠（断定）

1. **NFKC は `…`（U+2026）を `...` に潰す**（実測：`NFKC("…") == "..."`）。だから ⑤の `"..."→"…"` は「戻し」であって「揃え」ではない。`…{3,}→……` は NFKC の**前**に走るので、`……` は NFKC 後に `......`（6 点）になり、⑤が 2 回効いて `……` に戻る。順序を変えると結果が変わる。
2. `NFKC("～"（U+FF5E）) == "~"` なので、②の `[\uff5e\u301C] → ー` が NFKC の前にある必要がある。
3. .NET の `String.Normalize(NormalizationForm.FormKC)` は ICU/Windows 実装。Python の `unicodedata`（Unicode 版が Python のビルドに依存）と**版差で結果がずれうる**＝要突き合わせテスト。**未確認**（実測していない）。
4. `strip_outer_brackets` の深さ判定は `start_char == end_char` のとき（該当ペア無し）を考えなくてよいが、`(` と `)` が同一文字ではない前提のロジック。移植は素直。

### 1-4. トークナイザ

| 項目 | 値 | 根拠 |
|---|---|---|
| 実装 | HuggingFace `AutoTokenizer.from_pretrained(..., use_fast=True, trust_remote_code=False)` の薄いラッパ | `upstream/Irodori-TTS/irodori_tts/tokenizer.py:39-54` |
| 型 | **Unigram**（SentencePiece 系）、`byte_fallback: true`、`unk_id: 0` | `[hf]/models--Aratako--Irodori-TTS-v4-Small/snapshots/4c92c7ee.../tokenizer/tokenizer.json`（`model.type` を json で読取） |
| 語彙サイズ | **102400**（`model.vocab` 実数）＋ `added_tokens` 18 | 同上。checkpoint の `config_json.text_vocab_size` も `102400` |
| normalizer | **null**（トークナイザ側では正規化しない＝§1-1 が唯一の正規化） | tokenizer.json `normalizer` |
| pre_tokenizer | `{"type":"Metaspace","replacement":"▁","prepend_scheme":"never","split":false}` | 同上 |
| decoder | `Sequence[Replace("▁"→" "), ByteFallback, Fuse]` | 同上 |
| post_processor | `TemplateProcessing`（`<s> A </s>`）— ただし**推論では使われない**（後述） | 同上 |
| 由来リポ | `sbintuitions/modernbert-ja-310m`（checkpoint メタデータ `text_tokenizer_repo`、revision `77675fc96a7e445e982e2ba90246b816efc74ec6`） | `model.safetensors` の `__metadata__.config_json` |
| コード既定値 | `text_tokenizer_repo: str = "sbintuitions/sarashina2.2-0.5b"`（**逐語**）／`text_vocab_size: int = 102400` | `upstream/Irodori-TTS/irodori_tts/config.py:18-19` |
| 実配置 | **checkpoint リポに同梱**。`tokenizer/tokenizer_config.json` があれば `local_files_only=True` でそこから読む | `upstream/Irodori-TTS/irodori_tts/inference_runtime.py:581-589`、`:666-674` |
| DL 対象 | `allow_patterns = ["model.safetensors", "tokenizer/*"]` | 同 `:559`（subfolder 付きは `:562-566`） |

**.model（SentencePiece protobuf）は存在しない。**実キャッシュにあるのは `tokenizer.json`（6,718,495 B）と `tokenizer_config.json`（668 B）だけ。`pyproject.toml:19` は `sentencepiece>=0.1.99,<0.2` を要求するが、これは `AutoTokenizer` の slow 経路の保険で、実際の読み込みは `tokenizers`（Rust）の fast 経路。

`tokenizer_config.json` 逐語抜粋：`"tokenizer_class": "TokenizersBackend"`、`"bos_token": "<s>"`、`"eos_token": "</s>"`、`"pad_token": "<pad>"`、`"padding_side": "right"`、`"add_dummy_prefix_space": false`、`"legacy": false`。

**符号化の実際の手順**（`tokenizer.py:71-79`、`:111-136`）:
- `self.tokenizer.encode(text, add_special_tokens=False)` ＝ **post_processor を通さない**（`<s>`/`</s>` は自動付与されない）。
- `add_bos=True`（checkpoint の `text_add_bos: true`）のとき **BOS(id=1) を手で先頭に差し込む**（`:78`）。EOS は付かない。
- バッチは `padding="max_length"`, `truncation=True`, `max_length=text_max_len-1` で本体を作り、先頭列を BOS、mask の先頭を True にして結合（`:111-135`）。
- `padding_side` は pretrained 既定にかかわらず `"right"` を強制（`:17`）。
- `pad_token_id` は `<pad>`（id 3）。

**呼び出し箇所**：本文 `inference_runtime.py:1091`（`normalize_text(raw_text).strip()`）→ `:1194-1197`（`self.tokenizer.batch_encode([normalized_text] * num_candidates, max_length=text_max_len)`）。既定 `text_max_len` は checkpoint の `max_text_len=256`（`:711-716`、`config_json.max_text_len: 256`）。

**キャプション（感情記述）は別扱い**：`:1210` の `caption_text = "" if req.caption is None else str(req.caption).strip()` ＝ **`normalize_text` を通らない**。トークナイザは同一 repo（`caption_tokenizer_repo: "sbintuitions/modernbert-ja-310m"`）、`max_caption_len=512`。空文字なら `caption_mask.zero_()`（`:1215-1216`）。

### 1-5. G2P・音素化の有無（断定）

`upstream/` 全体（`.py`/`.toml`/`.txt`/`.md`）を `pyopenjtalk|g2p|phoneme|jaconv|MeCab|fugashi|num2words|onnx` で grep → **0 件**。
数字の読み下し（「123」→「ひゃくにじゅうさん」）も**行われない**。NFKC で `１２３`→`123` になるだけで、以降は Unigram トークンとして音響モデルに渡る＝**読み分けは LLM 系エンコーダ（ModernBERT-ja 310m）任せ**。

参考（前段の一部だが本席の担当外）：テキスト条件付けは `text_encoder_type: "pretrained"` ＝ ModernBERT（`ModernBertForMaskedLM`, vocab 102400, hidden 768, 25 層, `local_attention: 128`, sliding/full 混在）。その重みは `model.safetensors` に同梱で、別途 HF から落とさない（`inference_runtime.py:648-651` の `load_pretrained_backbone_weights=not model_cfg.use_pretrained_text_encoder`＝False）。

### 1-6. 絵文字パレット（`upstream/Irodori-TTS/irodori_tts/gradio_emoji_palette.py`）

- **役割は純粋に UI**。45 個の絵文字ボタンを Gradio の Accordion に並べ（`:160-162`）、`onpointerdown` の JS（`:110-127`）でテキストボックスのカーソル位置に絵文字を挿入するだけ。推論側の処理は一切無い。
- 使用元は `gradio_app.py:449` と `gradio_app_voicedesign.py:468`、CSS は `gradio_app.py:655`。**`infer.py`・`inference_runtime.py` は参照しない**。
- 45 項目の意味づけは `EMOJI_PALETTE_ITEMS`（`:61-107`）にラベル＋説明で入っている。例（逐語）：`EmojiPaletteItem("👂", "囁き", "耳元の音")`、`EmojiPaletteItem("⏸️", "間", "沈黙")`、`EmojiPaletteItem("📢", "エコー", "リバーブ")`、`EmojiPaletteItem("⏩", "早口", "一気に、急いで")`。
- README（`upstream/Irodori-TTS/README.md:25`）逐語：`**Emoji-based Style Control**: Emoji annotations in input text can influence delivery and non-verbal vocal expressions in supported checkpoints`。
- **絵文字は正規化を素通りする**（実測：`囁き👂です` → `囁き👂です`。NFKC は絵文字・VS16(U+FE0F)・ZWJ を変えない）。
- 語彙内訳（tokenizer.json の vocab と突合、実測）：45 個中**単一トークンは 8 個のみ**、残り 37 個（`👂 😮‍💨 ⏸️ 🤭 🥵 📢 …`）は byte_fallback（`<0xE3>` 等）で 4 トークン前後に分解される。

→ **読み分けちゃん2 側の意味**：絵文字パレットはそのまま C#/WPF の「感情ボタン列」に写せる（データは 45 行の定数表 1 個）。モデル側の契約は「本文テキストに絵文字を混ぜる」だけ＝追加 API 不要。

### 1-7. C# 移植の見立て（前段）

| 部品 | 難度 | 備考 |
|---|---|---|
| `normalize_text` | **低**（断定） | 外部依存ゼロ。NFKC は `String.Normalize(FormKC)`。順序を逐語で写す。Unicode 版差は要テスト（**未確認**） |
| 絵文字パレット | **低**（断定） | 定数表 45 行の移植のみ |
| Unigram トークナイザ | **中**（見立て） | 語彙＋log 確率が `tokenizer.json` に全部あるので、Viterbi 最短経路＋byte_fallback＋Metaspace(`▁`, prepend なし)＋`<s>` 先頭付与、で自前実装できる。BOS 手付け・EOS なし・right padding という仕様も単純 |
| `Microsoft.ML.Tokenizers` 流用 | **未確認** | 同ライブラリの SentencePiece 系 API は protobuf の `.model` を前提とする理解。今回**あるのは `tokenizer.json` のみ**なので、そのまま食えるか要検証。食えなければ (a) `tokenizer.json` → `.model` 変換、(b) Rust `tokenizers` の C# バインディング同梱、(c) 自前 Viterbi、の 3 択 |
| ModernBERT 前段の ONNX | **未確認**（担当外） | 標準 Transformer なので出せる見込みだが、sliding attention＋RoPE の 2 系統（`rope_theta` が sliding 10000 / full 160000）を実測していない |

---

## 2. コーデック（DACVAE）

### 2-1. 実チェックポイントの構成（断定・`[hf]` の `weights.pth` メタデータ実測）

`Aratako/Semantic-DACVAE-Japanese-32dim` は `audiotools.ml.BaseModel.save` 形式の zip（429,620,065 B）。`state_dict` 317 個＋`metadata.kwargs`：

```
{'encoder_dim': 64, 'encoder_rates': [2, 8, 10, 12], 'latent_dim': 1024,
 'decoder_dim': 1536, 'decoder_rates': [12, 10, 8, 2], 'n_codebooks': 16,
 'codebook_size': 1024, 'codebook_dim': 32, 'quantizer_dropout': False,
 'sample_rate': 48000}
```

| 項目 | 値 | 導出 |
|---|---|---|
| サンプルレート | **48000 Hz** | 上記 kwargs |
| hop_length | **1920** = 2×8×10×12 | `[venv]/dacvae/model/dacvae.py:514`（`np.prod(encoder_rates)`） |
| 潜在フレームレート | **25 fps** | 48000/1920 |
| 潜在次元（RF-DiT が扱う） | **32** | `codebook_dim=32`。`quantizer.out_proj.weight_v` 形状 `(1024, 32, 1)` で実測。checkpoint の `config_json.latent_dim: 32` と一致 |
| エンコーダ内部次元 | 1024 | `quantizer.in_proj.weight_v` 形状 `(64, 1024, 1)`＝`1024 → 32(μ)+32(σ)` |
| デコーダ幅 | 1536 → 768 → 384 → 192 → 96 | `decoder.model.0.weight_v` `(1536,1024,7)`、`decoder.model.1.block.1.weight_v` `(1536,768,24)` で実測（k=24=2×stride12） |

**upstream 既定との差（断定）**：`DAC.__init__` の既定は `encoder_rates=[2,4,8,8]`（hop 512）・`sample_rate=44100`（`[venv]/dacvae/model/dacvae.py:486,489,495`）。Aratako 版は **48kHz・hop 1920 で組み替えて再学習**したもので、単なる fine-tune ではない。「Semantic」の中身（意味蒸留か否か）は**未確認**（キャッシュに `weights.pth` しか無く、モデルカードが手元に無い）。

### 2-2. VAE の μ/σ の扱い（断定）

`[venv]/dacvae/nn/bottleneck.py:15-42`：

- `in_proj` = `NormConv1d(1024, 64, kernel_size=1)`（weight_norm）→ `chunk(2, dim=1)` で `mean`／`scale`。
- `_vae_sample`（:36-42）：`stdev = softplus(scale) + 1e-4`、`latents = randn_like(mean) * stdev + mean`。**乱数を引く。**
- `out_proj` = `NormConv1d(32, 1024, kernel_size=1)`。

irodori 側は `deterministic_encode=True` 既定（`upstream/Irodori-TTS/irodori_tts/codec.py:54`）のとき **`_vae_sample` を通さず `mean` をそのまま使う**（`codec.py:249-251`）:

```
z = self.model.encoder(self.model._pad(waveform))
mean, _scale = self.model.quantizer.in_proj(z).chunk(2, dim=1)
encoded = mean
```

→ **推論は決定的**。`deterministic_encode=False` にすると `DACVAE.encode`（`dacvae.py:715-722`）が使われ σ サンプリングが入る。

### 2-3. 使われる演算（断定）

| 演算 | 出所 |
|---|---|
| `weight_norm`（**旧 API** `torch.nn.utils.weight_norm`。state_dict は `weight_g`/`weight_v`） | `[venv]/dacvae/nn/layers.py:14`（コメント逐語：`# Keep compatibility with older PyTorch versions` / `parametrizations` 版は :12 でコメントアウト）、`:47-52`、実 state_dict のキー名で確認 |
| `Snake1d`（`x + (α+1e-9).reciprocal() * sin(αx)²`、チャネル毎 α パラメータ） | `[venv]/dacvae/nn/layers.py:18-24, 38-44`。**`@torch.jit.script` 付き**（:18） |
| `Conv1d`（`NormConv1d`、`pad_mode="none"` は `padding=(k-stride)*dilation//2` を静的に設定） | 同 `:54-115` |
| `ConvTranspose1d`（`NormConvTranspose1d`、`pad_mode="none"` は `padding=(stride+1)//2`, `output_padding=stride%2`） | 同 `:117-176` |
| `ELU`, `Tanh` | `activation()` `:27-35` |
| `nn.LSTM`（`LSTMBlock`） | `dacvae.py:108-119`。**透かし枝内のみ＝推論経路に乗らない**（2-5） |
| `nn.Embedding`（`MsgProcessor`） | `layers.py:179-212`。**同じく推論経路外** |
| `F.pad(..., "reflect")`（`DACVAE._pad`） | `dacvae.py:707-713`。エンコード時に hop の倍数へ右詰め |

**カスタム CUDA カーネルは無い**（断定：`dacvae` パッケージは `model/`＋`nn/` の 8 ファイル・純 Python、拡張モジュール無し）。`[venv]/dacvae` の全ファイル＝`__init__.py`, `__main__.py`, `model/{__init__,base,dacvae,discriminator}.py`, `nn/{__init__,bottleneck,layers,loss,quantize}.py`（計 2150 行）。

### 2-4. 実行経路の正確な形（断定・`DecoderBlock.forward` のチャンク選択を追跡）

`DecoderBlock.__init__`（`dacvae.py:159-227`）は 12 層を作るが、`forward`（:229-234）は `_chunk_size=2` で 2 層ずつに割り、**`j % 2 == 0` のチャンクだけ**を `nn.Sequential` にして実行する。実行されるのは index 0,1,4,5,8,9：

```
Snake1d → NormConvTranspose1d(pad_mode="none", weight_norm)
        → ResidualUnit(dil=1, Snake, pad_mode="none")
        → ResidualUnit(dil=3, Snake, pad_mode="none")
        → ResidualUnit(dil=9, Snake, pad_mode="none")
        → Identity
```

**`pad_mode="auto"` の層（index 2,3,6,7,10,11＝ELU・causal 系）は透かし枝の up/downsample 群**（`upsample_group()` :236-240 / `downsample_group()` :242-246）で、通常デコードでは実行されない。

これは ONNX 化に効く：`pad_mode="auto"` の `NormConv1d.pad`（`layers.py:89-111`）と `NormConvTranspose1d.unpad`（:154-172）だけが `x.shape[-1]` と `math.ceil` を使う**データ依存の形状演算**で、**それが通らない**。

デコード全経路（`codec.decode_latent` → `DACVAE.decode` :724-726 → `Decoder.forward` :448-451）：

```
latent (B,T,32) → transpose → (B,32,T)
 → quantizer.out_proj  NormConv1d(32→1024, k=1)
 → decoder.model[0]    NormConv1d(1024→1536, k=7)
 → DecoderBlock ×4     stride 12, 10, 8, 2（各 ConvTranspose1d k=2*stride）
 → decoder.watermark(x)   ← irodori が差し替え済み（2-5）
 → (B,1,T*1920)
```

差し替え後の末尾は `wm_model.encoder_block.forward_no_conv(x)`＝`Snake1d(96) → NormConv1d(96→1, k=7, weight_norm) → Tanh → Identity`（`dacvae.py:326-332`、`:277-298`）。つまり**「潜在→波形」の最終ヘッドが透かしモジュールの中に同居している**という配置。実測形状 `decoder.wm_model.encoder_block.pre.1.weight_v = (1, 96, 7)`。

### 2-5. "watermarked" の意味（断定）

- 上流 `facebook/dacvae-watermarked` は **デコーダ内蔵の透かし枝**を持つ：`Watermarker`（`dacvae.py:384-412`）＝`WatermarkEncoderBlock` + `MsgProcessor(nbits=16)` + `WatermarkDecoderBlock`。`Decoder.__init__:445` で `self.alpha = wm_channels / d_wm_out`（既定 32/128 = 0.25）。
- Aratako の checkpoint にも**その重みは入っている**（実測：`decoder.wm_model.msg_processor.msg_processor.weight` 形状 `(32, 128)` ＝ `2*nbits=32`、`nbits=16`）。
- しかし irodori は読み込み直後に潰す（`upstream/Irodori-TTS/irodori_tts/codec.py:82-96`）。コメント逐語：

  > `# Irodori checkpoints were trained without the DACVAE watermark branch.`
  > `# Keep decode output mono while skipping that encode/decode path.`

  `decoder.alpha = 0.0` に加え、`decoder.watermark` を `forward_no_conv` だけ呼ぶ関数に差し替える（:88-96）。
- さらに `_configure_deterministic_decode`（:117-130）で `wm_model.random_message` を**全 0 固定**に置き換える（保険。差し替え後は呼ばれない）。

→ **結論：DACVAE レベルの透かしは掛かっていない。**掛かるのは §3 の SilentCipher だけ。ただし `facebook/dacvae-watermarked` 由来の重みは checkpoint 内に残る（＝BRIEF §2-2 のライセンス連鎖の対象からは外れない。判断は卓）。

### 2-6. エンコーダが要るのはいつか（断定）

| 場面 | エンコーダ | 根拠 |
|---|---|---|
| codec ロード時（latent_dim 推定） | **必ず 1 回**（`torch.zeros(1,1,2048)` を `model.encode`） | `codec.py:103-105` |
| 参照 wav 指定（`ref_wav`/`ref_wavs`） | 要る | `inference_runtime.py:945-950`（`self.codec.encode_waveform(...)`） |
| 参照潜在指定（`ref_latent` の `.pt`） | 不要（`torch.load` で読むだけ） | 同 `:908-924` |
| 話者埋め込み指定（`ref_embed`） | 不要 | `_load_speaker_embedding_condition`（`:1219 付近`）／`speaker_inversion.py` |
| 通常合成（デコードのみ） | 不要 | `:1395`, `:1414` |

→ **⒝ で「参照 wav は事前に潜在へ焼いておく」運用にすれば、エンコーダを配らずに済む**（読み分けちゃん2 の声プリセットは固定なので現実的）。ただし `codec.py:103-105` のダミー encode は無条件なので、C# 実装では単に `latent_dim=32` を定数にすればよい。

### 2-7. ONNX 化の判定

| 段 | 判定 | 根拠と条件 |
|---|---|---|
| **デコーダ**（out_proj + model[0..4] + 最終ヘッド） | **出せる（高確度）** | 実行層が Conv1d/ConvTranspose1d/Snake/Tanh のみ。`pad_mode="none"` なので**形状演算が一切無い**（2-4）。動的長は時間軸を dynamic axis にすれば自然に通る（全部 FCN） |
| ↑ 前処理 | `torch.nn.utils.remove_weight_norm` を全 `NormConv1d`/`NormConvTranspose1d` に掛けて `weight_g`/`weight_v` を fold してから export。掛けないと norm/div/mul が毎回グラフに焼かれる（推論結果は同じだが無駄） | `layers.py:47-52` |
| ↑ Snake の `@torch.jit.script` | `torch.onnx.export`（TorchScript 経路）なら問題にならない見込み。演算は `Sin`/`Pow`/`Reciprocal`/`Add`/`Mul`/`Reshape` の標準 op のみ。dynamo (`torch.export`) 経路との相性は**未確認** | `layers.py:18-24` |
| **エンコーダ**（encoder + quantizer.in_proj + chunk） | **出せる（高確度）**。ただし `_pad`（reflect pad で hop 倍数化）はグラフ外（C# 側）でやるのが素直 | `dacvae.py:707-713`、`codec.py:249-251` |
| ↑ μ だけ取る決定的経路 | `in_proj` の出力 64ch を `chunk(2)` して前半＝そのまま ONNX の `Split` になる。乱数を引かないので**再現性あり** | `codec.py:249-251` |
| **可逆性** | encode→decode は VAE なので厳密可逆ではないが、**参照音声を潜在に焼く用途では往復不要**。`decode(encode(x))` の一致検証は不要 | — |
| **ラウドネス正規化**（encode 前） | **ONNX に入らない**。`audiotools.AudioSignal.normalize` は ITU-R BS.1770-4 の K-weighting を julius/scipy で実装（`[venv]/audiotools/core/loudness.py:1-8`, `effects.py:200-220`）。C# へ移すなら BS.1770 メータを自前実装 or 事前焼き込みで回避 | `codec.py:153-174` |

**推定サイズ**（未確認・目安）：`weights.pth` 429MB のうち discriminator は含まれないので（state_dict は encoder/quantizer/decoder のみ 317 キー）、fp32 ONNX で概ね同オーダー。デコーダのみに絞れば encoder 分（`encoder.*` キー）は落とせる。**内訳の実測は未実施**。

---

## 3. 透かし（SilentCipher）

### 3-1. irodori 側の呼び方（断定・`upstream/Irodori-TTS/irodori_tts/watermark.py`）

| 項目 | 値 | 行 |
|---|---|---|
| ペイロード | `IRODORI_WATERMARK_PAYLOAD = (73, 82, 68, 84, 83)  # "IRDTS"` （**逐語**） | :10 |
| モデル種別 | `model_type: str = "44.1k"` 既定・**引数で変えられない**（呼び出し側が渡していない） | :29／`inference_runtime.py:619` |
| device | `SilentCipherWatermarker(device=str(self.codec_device))` ＝ **codec と同じデバイスに追従。`'cuda'` 固定ではない** | `inference_runtime.py:619` |
| 生成 | `silentcipher.get_model(model_type=model_type, device=device)` | :43 |
| 失敗時 | import 失敗／ロード失敗を `logger.warning` で握り潰し `None` を返す＝**透かし無しで合成が続く** | :36-50 |
| 呼び出し | `encoded, _ = self.model.encode_wav(vector.to(self.model.device), int(sample_rate), list(payload), calc_sdr=False)` | :70-75 |
| 戻し | `torch.as_tensor(encoded, dtype=torch.float32, device="cpu")`＝**必ず CPU に落ちる** | :76 |
| 入力 sample_rate | `int(self.codec.sample_rate)` ＝ **48000** | `inference_runtime.py:1437` |

**on/off フラグは存在しない**（断定）。`upstream/` 全体で `watermark|silentcipher` を grep しても、切替の引数・環境変数・API フィールドは 1 件も無い。分岐は `if self.watermarker.ready:`（`inference_runtime.py:1433`）のみ＝**「パッケージと重みが揃っていれば必ず掛かる／欠けていれば警告を出して掛からない」**。掛からなかった場合は返却メッセージに追記される（`:1442-1447`、逐語 `"warning: SilentCipher watermark is unavailable; generated audio was not watermarked."`）。

**モデルカード／README の文言（事実のみ・逐語）**：
- `upstream/Irodori-TTS/README.md:27`：`- **Automatic Watermarking**: Generated audio is watermarked with [SilentCipher](https://github.com/sony/silentcipher) when available`
- 同 `:296`：`Generated audio is passed through [SilentCipher](https://github.com/sony/silentcipher) watermarking automatically when the dependency and model files are available.`
- 同 `:563`：`- [SilentCipher](https://github.com/sony/silentcipher) — Audio watermarking`

→ **README は "automatically … when available" と書いているだけで、「透かしを外すな」という義務表現は upstream の README には無い**（この読みの範囲では）。HF モデルカード本文は手元キャッシュに無く**未確認**。ライセンス面の判断は BRIEF §5 のとおり卓の専管。

### 3-2. SilentCipher 本体（`[venv]/silentcipher/`）

パッケージは 3 ファイルのみ：`__init__.py`（54 B・`from .server import get_model`）、`model.py`（3,294 B）、`server.py`（22,972 B）、`stft.py`（1,831 B）。

**提供元（断定）**：`[venv]/silentcipher-1.0.5.dist-info/direct_url.json` 逐語
```json
{"url":"https://github.com/SesameAILabs/silentcipher.git","vcs_info":{"vcs":"git","commit_id":"d46d7d0893a583d8968ab3a6626e2289faec9152","requested_revision":"d46d7d0893a583d8968ab3a6626e2289faec9152"}}
```
＝ **sony 本家ではなく SesameAILabs のフォーク**（`upstream/Irodori-TTS/pyproject.toml:20`・`requirements.txt:12` と一致）。LICENSE は `MIT License / Copyright (c) 2024 Sony Research Inc.`（`[venv]/silentcipher-1.0.5.dist-info/licenses/LICENSE:1-3` 逐語）。

**重み（`[hf]/models--sony--silentcipher/snapshots/a1c4d02.../`）**

| 種別 | ファイル | サイズ |
|---|---|---|
| 44.1k（使用） | `44_1_khz/73999_iteration/enc_c.ckpt` | 184,765 B |
| | `44_1_khz/73999_iteration/dec_c.ckpt` | 2,008,794 B |
| | `44_1_khz/73999_iteration/dec_m_0.ckpt` | 9,554,818 B（**復号専用**） |
| | `44_1_khz/73999_iteration/opt.ckpt` | 23,448,174 B（**optimizer・推論に不要**） |
| 16k（未使用） | `16_khz/97561_iteration/*` | 同構成 |

`config.json` は逐語 `{ "Note": "Dummy file to track download counts" }`。

**hparams（44.1k・逐語抜粋）**：`SR: 44100`、`N_FFT: 4096`、`HOP_LENGTH: 2048`、`message_band_size: 1024`、`message_dim: 5`、`message_len: 21`、`message_sdr: 47`、`n_messages: 1`、`ensure_negative_message: true`、`ensure_constrained_message: false`、`frame_level_normalization: false`、`utterance_level_normalization: true`、`no_normalization: false`。
（16k 側は `SR: 16000`, `N_FFT: 2048`, `HOP_LENGTH: 1024`, `message_band_size: 512`, `message_dim: 4`, `message_len: 16`）

**メッセージ bit（断定）**：README（`[hf]/models--sony--silentcipher/.../README.md`）逐語 `The message should be in the form of five 8-bit characters, giving a total message capacity of 40 bits`。
実装（`[venv]/silentcipher/server.py:307-315`）：5 バイト → 40 bit 文字列 → **2bit ずつ 20 シンボル**。`letters_encoding`（:65-100）の `assert len(message_lst[i]) == self.config.message_len - 1` ＝ 20 == 21-1 で通る。
**注意（断定）**：16k モデルは `message_len=16` ＝ 15 シンボル要求で、5 バイト（20 シンボル）は assert に落ちる。**16k は 5 バイト payload では使えない。**

**装填の流れ（`server.py:243-366`）**

```
y(48000Hz, mono, CPU or GPU)
 ├ resample 48000→44100   torchaudio.functional.resample (:292)   ← WARNING を print する（:290-291）
 ├ power 正規化           y *= sqrt(average_energy_VCTK / mean(y²))  (:301)  average_energy_VCTK=0.002837200844477648 (:59)
 ├ STFT  n_fft=4096, hop=2048, win=hann(4096), return_complex=True  (stft.py:20-31)
 ├ enc_c(carrier)         Encoder: Conv2d×3（gated: conv * sigmoid(gate) + BatchNorm2d） (model.py:6-34)
 ├ enc_c.transform_message(msg)  Linear(5 → 1024) ＋ 帯域外 0 パディング (model.py:36-40)
 ├ cat[carrier_enc, carrier×32, msg_enc×32] → dec_c  CarrierDecoder: Conv2d×4 (model.py:42-67)
 │   config で utterance_level_normalization=true → message_info *= mean(carrier², dim=(2,3))^0.5 (:329)
 │   ensure_negative_message=true → message_info = -message_info; carrier_reconst = relu(message_info+carrier) (:333-335)
 ├ iSTFT  torch.istft(mag*cos(phase)+i*mag*sin(phase), 4096, 2048, hann)  (stft.py:33-39)
 ├ power 戻し             y *= sqrt(original_power / average_energy_VCTK)  (:344)
 └ resample 44100→48000 + 長さ切り詰め  (:347-351)
```

**副作用と注意点（断定）**
- `STFT` は `metaclass=Singleton`（`stft.py:3-10`）＝**プロセス内に 1 インスタンスのみ**。44.1k と 16k を同時に使うと後者が前者の n_fft を引き継ぐ。
- `self.stft.num_samples` を `transform` と `inverse` の間にインスタンス変数で受け渡す（`server.py:342`, `stft.py:37`）＝**スレッドセーフでない**。⒞（薄い API 層）で並行合成を許すなら要ロック（irodori 側は `self._infer_lock`（`inference_runtime.py:620`）で合成全体を直列化しているので現状は当たらない）。
- `self.window` は buffer ではなく素の Tensor なので `stft.to(device)` では動かない。毎回 `self.window.to(x.device)` している（`stft.py:22, 36`）。
- モジュール先頭で `librosa`・`pydub`・`scipy.stats`・`soundfile`・`yaml`・`torchaudio` を import（`server.py:1-18`）＝**透かしを有効にするだけで librosa/numba/llvmlite/pydub まで配布物に乗る**。

### 3-3. ONNX 化の判定（透かし）

| 部品 | 判定 |
|---|---|
| `torch.stft` | opset 17 の `STFT` op で出せる**見込み**（**未確認**：実際に export していない） |
| `torch.istft` | **ONNX export 未対応の見込み**（**未確認**）。ここが丸ごと ONNX 化の壁 |
| `Encoder`(enc_c) / `CarrierDecoder`(dec_c) | **出せる（高確度）**。Conv2d + sigmoid + BatchNorm2d + Linear のみ（`model.py:6-67`）。ただし `dec_c` に `h[:, :, self.message_band_size:, :] = 0`（`model.py:62`）というスライス代入がある＝ONNX では `Slice`+`Concat` か定数マスク乗算に書き換えると安全 |
| `MsgDecoder`(dec_m) | **復号専用**。埋め込みだけなら不要 |
| `letters_encoding` | numpy の one-hot 生成（`server.py:65-100`）。**グラフ外で C# 実装**（20 シンボル×5 次元の one-hot をフレーム数だけタイル）。payload は固定なので**定数テーブルに焼ける** |
| リサンプル 48k↔44.1k | `torchaudio.functional.resample` 既定 `lowpass_filter_width=6, rolloff=0.99, resampling_method="sinc_interp_hann", beta=None`（`[venv]/torchaudio/functional/functional.py:1435-1443`）。C# 側で同等の帯域制限 sinc 補間が要る。**厳密一致は要検証（未確認）** |

**実務上の見立て（推測・要検証）**：透かしを ⒝ で持ち込むなら「まるごと ONNX」ではなく **hybrid** が現実的。
- C# 側：hann 窓 4096/hop 2048 の STFT・iSTFT（overlap-add）、power 正規化、リサンプル、one-hot 生成
- ONNX 側：`enc_c`（185KB）＋`dec_c`（2.0MB）の 2 本だけ＝**合計約 2.2MB**
- `dec_m_0.ckpt`（9.5MB）と `opt.ckpt`（23MB）は**配らなくてよい**（埋め込みには不要）

代替案（推測）：`torch.istft` を `ConvTranspose1d`（窓×IDFT 行列）に手で書き換えれば全段 ONNX 化も原理的には可能。工数は「便 1 つ」規模。

---

## 4. 出力後処理

`inference_runtime.py:1380-1460` と `save_wav`（:1530-1541）、Server 側 `audio.py`。

| 段 | 内容 | 依存 | 行 |
|---|---|---|---|
| unpatchify | `latent_patch_size=1` なので実質 no-op | torch | `:1381-1388`, `codec.py:28-34` |
| 長さ決め | `target_samples`／`latent_steps = ceil(target_samples/1920)`（秒指定時）、または duration predictor から `target_samples = latent_steps * 1920` | torch | `:1254-1255`, `:1314-1316`, `:1329-1330` |
| デコード | `codec.decode_latent(z).cpu()`。`decode_mode` = `"sequential"`(既定) / `"batch"` | torch | `:1395`, `:1414` |
| 尻尾刈り | `find_flattening_point`（潜在の末尾窓が平坦かつ 0 近傍になる位置。既定 `window_size=20, std_threshold=0.05, mean_threshold=0.1`）× `hop_length`(1920) と `target_samples` の min | torch | `:150-190`, `:1399-1409`, `:242-245` |
| 透かし | §3 | silentcipher（+torchaudio/librosa/pydub/scipy） | `:1433-1441` |
| **ラウドネス正規化** | **無し** | — | 出力側には存在しない（`codec._normalize_loudness` は `encode_waveform` 専用＝参照音声のみ・`codec.py:216-222`） |
| **リサンプル** | **無し**（出力は 48000 Hz のまま）。ただし透かし内部で 48k→44.1k→48k を往復 | torchaudio | `:1437`, `server.py:292,347` |
| wav 書き出し（CLI） | `torchaudio.save` → 失敗時 `soundfile.write` にフォールバック | torchaudio / soundfile | `:1530-1541` |
| wav 読み込み（参照） | `torchaudio.load` → 失敗時 `soundfile.read` | 同上 | `:1515-1527`, `codec.py:269-282` |
| 符号化（Server） | `clamp(-1,1)` → pcm(`<i2` 直書き)／wav・flac・mp3・opus は soundfile → 失敗時 torchaudio → 失敗時 外部 `ffmpeg` | soundfile / torchaudio / ffmpeg | `upstream/Irodori-TTS-Server/src/irodori_openai_tts/audio.py:31-69, 80-91, 110-119, 140-169` |

**参照音声側の正規化**（C# に持ち込むなら要注意）：既定 `ref_normalize_db: float | None = -16.0`、`ref_ensure_max: bool = True`（`inference_runtime.py:210-211`）。中身は `audiotools.AudioSignal(...).normalize(-16.0)` ＋ `ensure_max_of_audio()`（`codec.py:161-163`）＝ **ITU-R BS.1770-4 K-weighting**（`[venv]/audiotools/core/loudness.py:1-8` が `julius`・`scipy`・`torchaudio` を import、`:268-300` の docstring 逐語 `Calculates loudness using an implementation of ITU-R BS.1770-4.`）。**参照潜在を事前に焼く運用ならこの依存は消える。**

**librosa/numba/llvmlite はどこから来るか（断定）**：`upstream/` の Python コードは `numba|llvmlite` を 1 件も import しない（grep 0 件）。`pyproject.toml:13-14` が明示要求しているのと、`silentcipher` が `librosa>=0.10.0` を要求する（`[venv]/silentcipher-1.0.5.dist-info/METADATA`）ことで入る。**＝透かしを外せば librosa/numba/llvmlite は落とせる。**

---

## 5. 各段が実行時に引く外部依存（import 実測）

### 5-1. irodori_tts 内（`grep -n "^import |^from |import " *.py`）

| ファイル | トップレベル import | 遅延 import |
|---|---|---|
| `text_normalization.py` | `re`, `unicodedata` | — |
| `tokenizer.py` | `torch`, `collections.abc` | `transformers.AutoTokenizer`（:40） |
| `codec.py` | `torch`, `torchaudio`, `huggingface_hub.hf_hub_download`, `sys`, `dataclasses`, `pathlib` | `dacvae.DACVAE`（:60/:65）、`audiotools.AudioSignal`（:154）、`soundfile`（:273） |
| `watermark.py` | `torch`, `logging`, `collections.abc` | `silentcipher`（:35） |
| `gradio_emoji_palette.py` | `gradio`, `dataclasses`, `html.escape` | — |
| `inference_runtime.py` | `torch`, `torchaudio`, `safetensors`, `safetensors.torch`, `gc/hashlib/json/math/secrets/threading/time` ＋自パッケージ | `huggingface_hub.snapshot_download`（:610）、`soundfile`（:1519, :1537） |

### 5-2. 外部パッケージが引くもの

| パッケージ | 依存（`METADATA` の `Requires-Dist` 逐語） | 実際に import されるもの |
|---|---|---|
| `dacvae` 1.0.0（`git+https://github.com/facebookresearch/dacvae` @ `414c2078`） | `argbind>=0.3.7`, `descript-audiotools>=0.7.2`, `einops`, `huggingface-hub`, `numpy`, `torch`, `torchaudio`, `tqdm` | `dacvae/__init__.py:8` が `audiotools`、`model/base.py` が `numpy/torch/tqdm/audiotools.AudioSignal`、`model/dacvae.py:17` が `huggingface_hub` |
| `descript-audiotools` 0.7.2（License: MIT） | `argbind`, `numpy`, `soundfile`, ...（`loudness.py` で `julius`, `scipy`, `torchaudio`） | `julius` 0.2.8, `scipy`, `soundfile` |
| `silentcipher` 1.0.5（SesameAILabs フォーク・MIT / Sony Research Inc.） | `torch>=2.4.0`, `torchaudio>=2.4.0`, `librosa>=0.10.0`, `numpy>=1.25.2`, `SoundFile>=0.12.1`, `scipy>=1.11.4`, `pyaml>=24.4.0`, `Flask>=2.2.5`, `pydub>=0.25.1`, `huggingface-hub>=0.23.4` | `server.py:1-18` が `yaml, numpy, soundfile, scipy.stats, librosa, pydub, torch, torchaudio` を**トップレベルで**引く |
| `transformers` 5.16.1 / `tokenizers` 0.23.1 | — | トークナイザ経路のみ |

### 5-3. 段ごとに ⒝（C# 化）で消せる／残る依存

| 段 | Python 依存 | ⒝ で消せるか |
|---|---|---|
| 正規化 | `re`, `unicodedata`（標準ライブラリのみ） | **完全に消える**（C# 逐語移植） |
| トークナイズ | `transformers` + `tokenizers`(Rust) | 消せる見込み（自前 Unigram or C# バインディング）。**未確認** |
| テキストエンコーダ | `torch` + `transformers`(ModernBERT) | ONNX 化次第（担当外・**未確認**） |
| 音響（RF-DiT） | `torch` | 担当外 |
| コーデック decode | `torch` + `dacvae` + `audiotools`(BaseModel.load のみ) | **消せる（高確度）**＝ONNX 1 本。重みは `.pth` から抜いて事前変換 |
| コーデック encode | ＋`audiotools`(BS.1770), `julius`, `scipy`, `torchaudio`(resample), `soundfile` | **参照潜在を事前に焼けば丸ごと不要**。焼かないなら BS.1770 メータの自前実装が要る |
| 透かし | `silentcipher` + `librosa` + `numba` + `llvmlite` + `pydub` + `scipy` + `yaml` + `torchaudio`(resample) | **enc_c/dec_c を ONNX 化し、STFT/iSTFT/resample を C# 実装すれば消せる（推測）**。iSTFT の書き換えが山 |
| 出力符号化 | `soundfile` / `torchaudio` / `ffmpeg` | 消せる（C# は `NAudio` 等 or 自前 wav writer。読み分けちゃん2 は wav で足りる） |

---

## 6. 主席への注意

1. **§4-2 の「ONNX へ出せるか」への答えの骨格**：前段（正規化・トークナイザ）は ONNX の問題ではなく**移植の問題**で、外部辞書ゼロなので低コスト。後段のコーデックは **decoder/encoder とも ONNX 化が素直**（`pad_mode="auto"` の形状演算層が実行経路から外れているのが決め手）。**壁は透かしの `torch.istft` だけ**。＝「部分移植」の境界は「透かしをどう扱うか」に一点集約される。

2. **透かしは on/off できない**（コード上・断定）。⒝ で透かしを落とすと、⒜⒞（Python 実装）と ⒝ で出力が食い違う。README は "when available"（＝依存が無ければ掛からない）としか書いていないが、**モデルカード本文は手元キャッシュに無く未確認**＝ライセンス席の確認事項に回してほしい。透かしを残す選択なら、**enc_c＋dec_c の 2.2MB だけを ONNX 化＋STFT/iSTFT を C# 実装**という現実解がある（`dec_m_0.ckpt` 9.5MB と `opt.ckpt` 23MB は不要＝配布物を約 33MB 削れる）。

3. **`facebook/dacvae-watermarked` 由来の透かし枝の重みは Aratako の checkpoint に残っているが、推論では一切通らない**（`codec.py:82-96` で `alpha=0.0`＋関数差し替え）。ライセンス連鎖（BRIEF §2-2）の議論では「使っていない／しかし同梱されている」という状態として扱う必要がある。ONNX 化するときは**この枝を除いてエクスポートできる**＝派生物のサイズも意味も小さくできる、という材料にもなる。

4. **サンプルレートは 48000 Hz・hop 1920・25 fps**（upstream 既定の 44100/512 ではない）。プローブ報告の wav 形式や ⒞ の API 設計と突き合わせてほしい。透かしが 1 発声ごとに 48k→44.1k→48k のリサンプルを 2 回するのは、レイテンシ（RTF）測定の内訳として `silentcipher_watermark` ステージのログ（`inference_runtime.py:1440-1441`）で分離できる。

5. **配布物の見積り材料**：checkpoint `model.safetensors` 3,064,295,596 B（v4-Small・トークナイザ 6.7MB 同梱）／DACVAE `weights.pth` 429,620,065 B／SilentCipher 44.1k の推論必要分 2,193,559 B（enc_c＋dec_c）。BRIEF §4-5 の「2.86GB」は v4-Small 本体（3.06GB = 2.85GiB）を指していると思われる。

6. **未確認として残したもの**：(a) `Microsoft.ML.Tokenizers` が `tokenizer.json` の Unigram を読めるか、(b) .NET の NFKC と Python の NFKC の版差、(c) `torch.stft`/`torch.istft` の現行 torch での ONNX export 可否（実測していない）、(d) 「Semantic」DACVAE の学習手法（意味蒸留か否か・モデルカードが手元に無い）、(e) `@torch.jit.script` された Snake と dynamo export の相性、(f) ONNX 化後のファイルサイズ実測。
