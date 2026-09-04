# 15 — 前段（正規化・Unigram トークナイザ）の .NET 移植可否 ＝実証

> 席＝実験サブ席（opus）／担当＝BRIEF §4-2 の「出せない段」の代替コスト（前段）。実施日 2026-09-04。
> 04 ノート §1（前段）と 11 ノート §10（環境変数一式）を前提にした。GPU 未使用・8088/7861 に触れていない。
> `upstream/` ・ `yomiwakechan2/` ・ `C:/IrodoriTTS/` ・ `C:/irodori-TTS-server/` ・ HF キャッシュは 1 バイトも変更していない（§8）。

## 要点（10 行以内）

1. **前段は丸ごと C# に出せる。実証済み**＝正規化 82 行＋トークナイザ 149 行（実効・計 231 行）で、**Python と id 列が完全一致**した。
2. `tokenizer.json`（Unigram・102400・byte_fallback）→ SentencePiece `.model` 変換は成功。**2,035,786 B**（tokenizer.json 6,718,495 B の 30%）。`lab/out/irodori_tok.model`。
3. Python 側照合＝変換 `.model` を sentencepiece 0.2.1 で読み、HF fast と **60 文すべて一致**（生文・正規化後とも）。無作為 6000 文・全語彙 102126 片でも 0 件不一致。
4. **Microsoft.ML.Tokenizers に実バグを発見**＝Unigram の ModelProto 経路で `ByteCodeToIdOffset` が **byte 片の先頭でなく末尾**を拾い、byte_fallback の id が全部 **+255 ずれる**（`SentencePieceUnigramModel.cs:70`）。BPE 経路にはこの誤りが無い。**未報告**（GitHub issue 検索 0 件）。
5. 回避＝変換時に byte 片を `BYTE` でなく `NORMAL` 型で書く。これで **2.0.0（安定版）でも 108,430 件すべて一致**（0 不一致）。ただしその `.model` は本家 sentencepiece が読めなくなる（片道）。
6. **より筋のよい退路＝自前 Viterbi**。`tokenizer.json` を直読みする C# 実装 **171 行**を書き、**108,430 件で 0 不一致**。NuGet 依存ゼロ・変換工程ゼロ・特殊トークンの扱いまで HF と一致。
7. **NFKC の版差は実害なし（断定）**＝BMP 全域＋補助面 7 ブロックの **68,218 コードポイント**で Python 3.12（UCD 15.0.0）と .NET 10 の `FormKC` は **0 件差**。
8. ただし **U+FFFE だけ .NET が例外を投げる**（`ArgumentException: String contains invalid Unicode code points.`）。孤立サロゲートも同様。**入力の衛生処理が 1 行要る**。
9. `normalize_text` の C# 逐語移植は 04 ノート §1-2 の実測表 17 行＋60 文の **77 件すべて Python と一致**。
10. 性能＝C# は 1 文 **2.4〜7.4 µs**・常駐 54〜78 MB。Python 側は `import transformers` だけで **13.1 s**・RSS 555 MB・符号化 17.9 µs。**前段を C# 化すると起動が 13 秒縮み、常駐が 500 MB 減る**。

---

## 0. 環境と参照の付け方

| 項目 | 値 |
|---|---|
| dotnet SDK | `10.0.400`（`dotnet --version`）／ランタイム `.NET 10.0.11` |
| lab venv | CPython `3.12.14`・`unicodedata.unidata_version = 15.0.0` |
| Python 側の版 | `sentencepiece 0.2.1` / `tokenizers 0.23.2` / `transformers 5.16.1` / `protobuf 7.36.1` |
| 対象 tokenizer | `[hf]/models--Aratako--Irodori-TTS-v4.1-Small/snapshots/2b28324d.../tokenizer/tokenizer.json`<br>6,718,495 B・sha256 `6a0734cf21c802169defaffe719bc2ef12bb9d0be37e54b61ed27aa89394723d` |
| v4-Small との異同 | **同一ファイル**（v4-Small 側の sha256 も `6a0734cf…`）＝どちらの checkpoint でも前段は同じ |
| 書いた場所 | `lab/bin/*.py`（5 本）・`lab/dotnet/TokProbe/`・`lab/dotnet/TokProbe20/`・`lab/out/*`・本ノート |

環境変数は 11 ノート §10 の一式をそのまま使用（`PYTHONPATH`＝upstream・`PYTHONDONTWRITEBYTECODE=1`・`HF_HUB_OFFLINE=1`・`PYTHONIOENCODING=utf-8`・`PYTHONUTF8=1`・`OMP_NUM_THREADS=8`）。editable install なし。

---

## 1. Web 調査（取得日時＝2026-09-04）

### 1-1. Microsoft.ML.Tokenizers

| 項目 | 値 | 出所 |
|---|---|---|
| 最新安定版 | **2.0.0**（2025-11-11 公開） | https://www.nuget.org/packages/Microsoft.ML.Tokenizers |
| 最新プレビュー | **3.0.0-preview.26160.2**（2026-03-12 公開） | 同上／`https://api.nuget.org/v3-flatcontainer/microsoft.ml.tokenizers/index.json` |
| ライセンス | **MIT** | 同上 |
| 対象 TFM | `net8.0` と `netstandard2.0` | 同上 |
| 依存（`net8.0`・**3.0.0-preview**） | **なし**（`<group targetFramework="net8.0" />` が空） | nupkg 内 `Microsoft.ML.Tokenizers.nuspec` を実読 |
| 依存（`net8.0`・2.0.0） | `Google.Protobuf >= 3.30.2` | 実ビルド出力に `Google.Protobuf.dll` 489,568 B が落ちる |
| DLL の実寸 | 3.0.0-preview `net8.0` **277,776 B** ／ 2.0.0 **325,896 B**（＋protobuf 489,568 B） | ビルド出力を実測 |

**対応トークナイザ種（`assembly.GetExportedTypes()` の実測・3.0.0-preview）**
`BertTokenizer` / `BpeTokenizer` / `CodeGenTokenizer` / `EnglishRobertaTokenizer` / `LlamaTokenizer` / `Phi2Tokenizer` / `SentencePieceTokenizer` / `TiktokenTokenizer` / `WordPieceTokenizer`。

**Unigram の由来（リリースノート逐語）**
- `dotnet/machinelearning` **v5.0.0**（2025-11-11）: `- **Introducing SentencePiece Unigram Tokenizer Model** ([#7390](https://github.com/dotnet/machinelearning/pull/7390))`、`- **Unigram tokenizer fixes** ([#7409](...))`、`- **Cleanup SentencePiece tokenizer** ([#7427](...))`
- **v6.0.0-preview1**（2026-03-12）: `- **Remove Google.Protobuf dependency from Microsoft.ML.Tokenizers** ([#7587](...))`

**`SentencePieceTokenizer.Create` が要求する入力（Microsoft Learn・逐語）**

> Creates an instance of SentencePieceTokenizer. The model stream should contain a SentencePiece model as specified in the following documentation: https://github.com/google/sentencepiece/blob/master/src/sentencepiece_model.proto.

```csharp
public static Microsoft.ML.Tokenizers.SentencePieceTokenizer Create(
    System.IO.Stream modelStream, bool addBeginningOfSentence = true, bool addEndOfSentence = false,
    System.Collections.Generic.IReadOnlyDictionary<string,int>? specialTokens = default);
```
> modelStream: The stream containing the SentencePiece **Bpe or Unigram** model.

（出所 https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.tokenizers.sentencepiecetokenizer.create?view=ml-dotnet-preview 。
実機の `2.0.0` / `3.0.0-preview.26160.2` の public static メソッドはこの 1 本だけ＝リフレクションで確認。）

**ModelProto から読む欄（`SentencePieceBaseModel.cs:18-59` 逐語・抜粋）**

```csharp
BeginningOfSentenceToken = modelProto.TrainerSpec.BosPiece ?? "<s>";
EndOfSentenceToken       = modelProto.TrainerSpec.EosPiece ?? "</s>";
UnknownToken             = modelProto.TrainerSpec.UnkPiece ?? "<unk>";
AddDummyPrefix           = modelProto.NormalizerSpec.AddDummyPrefix;
EscapeWhiteSpaces        = modelProto.NormalizerSpec.EscapeWhitespaces;
TreatWhitespaceAsSuffix  = modelProto.TrainerSpec.TreatWhitespaceAsSuffix;
ByteFallback             = modelProto.TrainerSpec.ByteFallback;
Normalizer = new SentencePieceNormalizer(
    modelProto.NormalizerSpec.PrecompiledCharsmap.Span,
    modelProto.NormalizerSpec.RemoveExtraWhitespaces, AddDummyPrefix, EscapeWhiteSpaces,
    modelProto.TrainerSpec.TreatWhitespaceAsSuffix, specialTokens);
```
種の分岐は `SentencePieceTokenizer.cs:29`：`TrainerSpec.Types.ModelType.Unigram => new SentencePieceUnigramModel(...)`、既定は
`The model type '{modelProto.TrainerSpec.ModelType}' is not supported.` を投げる。
**→ `byte_fallback` は正式に読む欄がある（＝設計上は対応）。実装に穴があるのは §4-2。**

**HF `tokenizer.json` を直接読む API の有無（断定）**
- **公開されている NuGet には無い。** `2.0.0` / `3.0.0-preview.26160.2` の `SentencePieceTokenizer` の public static は `Create(Stream,…)` だけ（実機リフレクションで確認）。
- **`main` ブランチには既に入っている**＝`public static SentencePieceTokenizer CreateFromTokenizerJson(Stream tokenizerJsonStream, bool addBeginningOfSentence = true, bool addEndOfSentence = false, IReadOnlyDictionary<string,int>? specialTokens = null)`（`src/Microsoft.ML.Tokenizers/Model/SentencePieceTokenizer.cs:562`）。
  追加コミット＝**`3f3c7465e8`／2026-07-16**（`Add public SentencePieceTokenizer factory methods for Unigram from vocab list and tokenizer.json`）。**最新 NuGet（2026-03-12 公開）より後**＝**未リリース**。
- その doc コメント逐語（要点）：読むのは `model.vocab` / `model.unk_id` / `model.byte_fallback` / `added_tokens`（`"special": true` のもの）/ `normalizer.precompiled_charsmap` / `pre_tokenizer` の `Metaspace` / `post_processor`。
  `The decoder section is not read.` ／ `remove_extra_whitespaces has no direct representation in tokenizer.json; it is deduced from …, defaulting to false when none are present, to match the Hugging Face fast-tokenizer runtime.`

### 1-2. 代替（HF tokenizers の Rust バインディング）

| パッケージ | 最新 | ライセンス | 中身の実測（win-x64） | 備考 |
|---|---|---|---|---|
| **Tokenizers.DotNet**（sappho876/sappho192・非公式） | `1.4.1` | **MIT**（本家 tokenizers は Apache-2.0） | managed `Tokenizers.DotNet.dll` **15,360 B** ＋ native `hf_tokenizers.dll` **3,961,652 B** | `new Tokenizer(vocabPath: …/tokenizer.json)`。encode/decode のみ。OS×arch ごとに別の `Tokenizers.DotNet.runtime.<rid>` を足す |
| **Tokenizers.HuggingFace**（lazy_engineer） | `3.23.1`（2026-05-12） | **Apache-2.0** | managed **302,080 B** ＋ native `tokenizers_proto.dll` **4,780,032 B** | 説明逐語 `.NET bindings for huggingface/tokenizers using protobufs for communication and C-ABI.`。`Tokenizer.FromFile("./tokenizer.json")`。`Google.Protobuf >= 3.34.1` 依存 |
| Microsoft.ML.Tokenizers | `2.0.0` / `3.0.0-preview` | MIT | **278〜326 KB の純マネージド 1 本**（3.0.0-preview は依存ゼロ） | native なし＝RID 別配布が要らない |
| **自前実装**（本便で作成） | — | — | **0 B**（自分のアセンブリに 171 行） | §4-3 |

**読み**＝Rust バインディングは動くが、**win-x64 だけで 4〜4.8 MB の native DLL** が乗り、RID ごとに配布物が増える。⒜専用インストーラの「配布物を小さく」という目的に逆行する。

---

## 2. `tokenizer.json` → SentencePiece `.model` 変換

`lab/bin/tok_json_to_spm.py`（116 行）。`sentencepiece.sentencepiece_model_pb2` で `ModelProto` を組む。

```bash
"$PY" "$LAB/bin/tok_json_to_spm.py" --out "$LAB/out/irodori_tok.model"
# -> {"bytes": 2035786, "vocab": 102400, "unk_id": 0, "byte_fallback": true,
#     "add_dummy_prefix": false, "escape_whitespaces": true,
#     "types": {"NORMAL": 102126, "BYTE": 256, "CONTROL": 6, "USER_DEFINED": 11, "UNKNOWN": 1}}
```

**tokenizer.json の実測（json で直読み）**

| 欄 | 値 |
|---|---|
| `model.type` / `unk_id` / `byte_fallback` | `Unigram` / `0` / `true` |
| `model.vocab` | 102400 件・`[piece, score]`・score の範囲 `-18.32311630249023`〜`0.0`・**重複片 0** |
| byte 片 | **id 15〜270**（`<0x00>`=15 … `<0xFF>`=270）・256 個・score 0.0 |
| `normalizer` | `null` |
| `pre_tokenizer` | `{"type":"Metaspace","replacement":"▁","prepend_scheme":"never","split":false}` |
| `added_tokens` | 18 件。`special:true` は id 0〜6（`<unk> <s> </s> <pad> <sep> <mask> <cls>`）、`special:false` は id 7〜14 と 102397〜102399 |
| 片の最長 | 通常片 **8 文字**（10/13/14/16/19 文字は added_tokens のみ） |

**書き込んだ `ModelProto`**

| 欄 | 値 | 根拠 |
|---|---|---|
| `trainer_spec.model_type` | `UNIGRAM` | `model.type` |
| `vocab_size` / `unk_id` / `bos_id` / `eos_id` / `pad_id` | 102400 / 0 / 1 / 2 / 3 | vocab の並び |
| `byte_fallback` | `true` | `model.byte_fallback` |
| `treat_whitespace_as_suffix` | `false` | Metaspace は前置系 |
| `normalizer_spec.name` | `"identity"` | `normalizer: null` |
| `add_dummy_prefix` | **`false`** | `prepend_scheme: "never"` |
| `remove_extra_whitespaces` | **`false`** | HF fast の実行時挙動に合わせる |
| `escape_whitespaces` | `true` | Metaspace の `▁` 置換 |
| `precompiled_charsmap` | 空 | `normalizer: null` |
| 片の型 | id 0＝`UNKNOWN`／id 1-6＝`CONTROL`／id 15-270＝`BYTE`／その他 added＝`USER_DEFINED`／残り＝`NORMAL` | — |

**踏んだ石**：`TrainerSpec` に `remove_extra_whitespaces` は**無い**（`AttributeError: Protocol message TrainerSpec has no "remove_extra_whitespaces" field.`）。この欄は `NormalizerSpec` 側だけ。

---

## 3. Python 照合（HF fast ↔ 変換 `.model`）

`lab/bin/tok_compare_py.py` ／ 60 文コーパスは `lab/bin/make_corpus.py` → `lab/out/corpus60.json`
（内訳＝日本語 10・記号/置換対象 10・数字英字全角半角 10・空白改行タブ 10・絵文字 10（**45 種全部入り 1 文＋前中後半 3 文**を含む）・結合文字/特殊 10）。

```bash
"$PY" "$LAB/bin/tok_compare_py.py" "$LAB/out/irodori_tok.model" "$LAB/out/tok_py.json"
# {"spm_vocab":102400,"hf_vocab":102400,"match_raw":60,"match_norm":60,"total":60, ...}
```

**結果＝生文 60/60 一致・`normalize_text` 後 60/60 一致。**

### 3-1. 敵対検分（`lab/bin/tok_stress_py.py`・`tok_stress_inputs.py`）

入力 **108,430 件**＝特殊トークン文字列 48 ＋ `<0xNN>` リテラル 256 ＋ 無作為 6,000（12 の文字プールから 1〜30 文字。ひらがな/カナ・CJK・ASCII・全角・一般句読点・絵文字・空白/制御・結合記号・ハングル・キリル・罫線・CJK 拡張B）＋ 全語彙片 102,126。

| `.model` の型づけ | sentencepiece で読めるか | HF との不一致（108,430 件中） |
|---|---|---|
| `irodori_tok`（byte=BYTE, 特殊=CONTROL） | ○ | **277**＝`<0xNN>` リテラル 256 ＋ 特殊トークン文字列 21 |
| `irodori_tok_hfcompat`（byte=BYTE, 特殊=USER_DEFINED） | ○ | **259**＝`<0xNN>` 256 ＋ `<unk>` の 3 件 |
| `irodori_tok_bytenormal`（byte=NORMAL） | **×**（後述） | — |

**不一致の型は 2 つだけ（断定）**

1. **CONTROL 型の片は Viterbi の trie から外れる**。`<s>` `</s>` `<pad>` `<sep>` `<mask>` `<cls>` `<unk>` を**文中にそのまま書いた**とき、HF は AddedVocabulary で切り出して id 1 等を返すが、sentencepiece は普通の文字列として分解する。
   例：`"前<s>後"` → HF `[1017, 1, 1159]` ／ spm `[1017, 366, 280, 341, 1159]`。
   **`USER_DEFINED` に変えれば一致する**（`<unk>` だけは `UNKNOWN` 型必須なので残る）。
2. **`<0x41>` のような byte 片の綴りをそのまま書いたとき**。HF は vocab 片として一発で当てる（id 15+0x41）が、sentencepiece は BYTE 型片を trie に入れないので `<`,`0x41`,`>` に割る。

**→ どちらも「利用者が `<s>` や `<0x41>` という綴りを読み上げ本文に書いたときだけ」起きる。読み上げ用途では実害なし（断定）。**
むしろ HF 側の挙動（本文中の `<s>` が BOS になる）は**読み上げソフトとしては危険**で、C# 実装で切り出さないほうが安全という見方もできる（判断は卓）。

---

## 4. .NET 照合

`lab/dotnet/TokProbe/`（`Microsoft.ML.Tokenizers 3.0.0-preview.26160.2`）と
`lab/dotnet/TokProbe20/`（同 `2.0.0` 安定版）。`dotnet new console` → `dotnet add package` のみ。**publish はしていない。**

```bash
cd lab/dotnet/TokProbe && dotnet build -c Release
dotnet run -c Release --no-build -- tok    <corpus60.json> <out.json> <model> [spm|self]
dotnet run -c Release --no-build -- stress <stress_inputs.json> <out.json> <model> [specials|self]
dotnet run -c Release --no-build -- nfkc | nfkcall | norm | edge | bench | info
```

### 4-1. 結果表

| 実装 | 版 | `.model` の型づけ | corpus 60 文（生／正規化後） | 敵対 108,430 件 |
|---|---|---|---|---|
| `SentencePieceTokenizer.Create` | 3.0.0-preview | byte=BYTE | **20 / 60 不一致** | **6,170 不一致** |
| 同上 | 2.0.0 | byte=BYTE | — | **6,170 不一致** |
| 同上 | 3.0.0-preview | byte=NORMAL | **0 / 60** | 21 不一致（特殊トークン文字列のみ） |
| 同上＋`specialTokens` 引数 | 3.0.0-preview | byte=NORMAL | **0 / 60** | **0 / 108,430** |
| 同上＋`specialTokens` 引数 | **2.0.0（安定版）** | byte=NORMAL | **0 / 60** | **0 / 108,430** |
| **自前 `HfUnigramTokenizer`** | — | `tokenizer.json` 直読み | **0 / 60** | **0 / 108,430** |

6,170 件の内訳（C# 側で分類・「その他 = 0」）：

```
内訳: 特殊トークン文字列=21  <0xNN>リテラル=256  byte id が +255=5893  その他=0
```

### 4-2. Microsoft.ML.Tokenizers のバグ（断定・未報告）

`src/Microsoft.ML.Tokenizers/Model/SentencePieceUnigramModel.cs`（`main`、当便が取得した写しは `lab/tmp/`）：

```csharp
// :45-66  ← BYTE 型の片は _vocab に入れず、MaxByteId だけ更新する
else if (modelProto.Pieces[i].Type == ModelProto.Types.SentencePiece.Types.Type.Byte)
{
    MaxByteId = i;          // ループを回りきると <0xFF> の index（=270）が残る
}
...
// :70
ByteCodeToIdOffset = _vocab.TryGetValue("<0x00>", out int id) ? id : MaxByteId;
```

`_vocab` には `Normal` / `UserDefined` / `Unused` しか入らない（`:47-56`）ので `TryGetValue("<0x00>")` は**必ず失敗**し、
`ByteCodeToIdOffset` に **byte 片の末尾 id（270）** が入る。正しくは先頭（15）。
符号化は `ids.Insert(IdsIndex, ByteCodeToIdOffset + normalizationSpan[Utf8Index + j]);`（`:1038`）なので、
**byte_fallback で出る id が全部 +255 ずれる**。実測：`"\n"` → HF `25`（=15+0x0A）／ML.NET `280`（=270+0x0A）。

- **BPE 経路には無い**。`SentencePieceBpeModel.cs:28-41` は**全片を** `_vocab` に入れてから同じ式を評価するので `<0x00>` が引ける。
- **`tokenizer.json` 経路（未リリース）にも無い**。`SentencePieceUnigramModel.cs:217-225` は byte 片を `Normal` として `_vocab` に入れ、さらに `<0x00>`〜`<0xFF>` が連続していることを検証して `InvalidDataException` を投げる作りになっている。
- **既知の issue は見つからない**（`repo:dotnet/machinelearning byte_fallback` の GitHub 検索で 0 件。取得 2026-09-04）。

**回避（実証済み）**＝変換時に byte 片を `NORMAL` 型で書く（`--byte-type normal`）。ML.NET は `_vocab` から `<0x00>`=15 を拾えるようになり一致する。
**代償**＝その `.model` は本家 sentencepiece が拒否する（逐語）：

```
RuntimeError: Internal: there are not 256 byte pieces although `byte_fallback` is true.
```

＝**Python と C# の両方が同じ 1 個の `.model` を読む、はできない。**（C# 専用の派生物になる。⒝案では Python 側は要らないので実害は無い。）

**特殊トークン 21 件の解消**＝`Create` の第 4 引数に渡すだけ。

```csharp
var sp = new Dictionary<string,int>{["<unk>"]=0,["<s>"]=1,["</s>"]=2,["<pad>"]=3,
                                    ["<sep>"]=4,["<mask>"]=5,["<cls>"]=6};
tok = SentencePieceTokenizer.Create(fs, addBeginningOfSentence:false, addEndOfSentence:false, specialTokens:sp);
```
→ **0 / 108,430**（2.0.0 安定版でも同じ）。

### 4-3. 自前 Viterbi（退路・実測 171 行）

`lab/dotnet/TokProbe/HfUnigramTokenizer.cs`＝**171 行（空行・コメント除き 149 行）**。NuGet 依存ゼロ、`System.Text.Json` だけで `tokenizer.json` を直読み。

要点（実コード）：

```csharp
// 語彙は Dictionary<string,int> ＋ .NET 9 の AlternateLookup<ReadOnlySpan<char>> で無割り当て検索
_lookup = _pieceToId.GetAlternateLookup<ReadOnlySpan<char>>();
...
// added_tokens は正規表現（長い順）で先に切り出す＝HF の AddedVocabulary 相当
// Metaspace: ' ' -> U+2581（prepend_scheme=never なので先頭付与なし）
float unkScore = _minScore - UnkPenalty;   // UnkPenalty = 10.0f（sentencepiece / HF と同値）
for (int i = 0; i < n; i++) {
    int charLen = char.IsHighSurrogate(s[i]) && … ? 2 : 1;
    bool hasSingle = false;
    for (int L = 1; L <= Math.Min(_maxPieceLen, n - i); L++) {
        if (!_lookup.TryGetValue(s.Slice(i, L), out int id)) continue;
        if (L == charLen) hasSingle = true;
        float cand = best[i] + _scores[id];
        if (cand > best[i+L]) { best[i+L] = cand; fromPos[i+L] = i; pieceAt[i+L] = id; }
    }
    if (!hasSingle) { /* 1 文字ぶんの unk ノードを unkScore で置く */ }
}
// 逆順に辿り、pieceAt < 0 の区間は UTF-8 バイトへ落として _byteBaseId + b を並べる
```

- 更新条件を **`>`（strict）** にしてある＝同点なら先に見つけた（短い）片が勝つ。HF の `update_best_path` と同じ取り決め。
- `_minScore` は全語彙の最小（**-18.32311630249023**）。sentencepiece の `min_score - 10.0` と一致する。
- **既知の未対応**＝孤立サロゲート（不正 UTF-16）を含む入力。HF は Rust の `&str` なので構造的に起きえない。C# では起きうる＝入力の衛生処理が要る（§5-2 と同じ話）。

**検証**＝`stress` で **0 / 108,430 不一致**（特殊トークン 48 件・`<0xNN>` 256 件・無作為 6,000 件・全語彙片 102,126 件を含む）。corpus 60 文も生文・正規化後とも **0 / 60**。

---

## 5. NFKC 照合（Python `unicodedata` ↔ .NET `String.Normalize(FormKC)`）

### 5-1. 全数照合の結果（断定）

| 範囲 | 件数 |
|---|---|
| 指定の 6 帯（U+2000-206F・U+3000-30FF・U+FF00-FFEF・U+2460-24FF・U+2100-214F・U+2500-257F） | 976 |
| 絵文字 45 種（04 ノート §1-6 の `EMOJI_PALETTE_ITEMS`） | 45 |
| 合成文字（基底 61 種 × 結合 11 種＝U+3099 濁点・U+309A 半濁点・U+0300/0301/0308/030A/0327・U+20DD・U+FE00・U+FE0F・U+200D） | 671 |
| **追加の全数掃き**＝BMP 全域（サロゲート除く）＋ U+1D400-1D7FF・U+1F100-1F2FF・U+1F300-1F9FF・U+2F800-2FA1F・U+1FBF0-1FBF9・U+10000-1007F・U+1E030-1E08F・U+1E900-1E95F・U+11380-113FF・U+16D40-16D7F・U+1E5D0-1E5FF・U+105C0-105FF・U+1FA70-1FAFF・U+10D40-10D8F | **68,218** |

**差分＝0 件。**（`lab/out/nfkc_diff.json`・`lab/out/nfkc_all_diff.json`。生データ `lab/out/nfkc_cs.json`・`lab/out/nfkc_cs_all.txt`）

版差の目印も一致した：Unicode 13.0 追加の U+1FBF0-1FBF9（セグメント数字）→ `0030`〜`0039`、
**Unicode 15.0 追加**の U+1E030（MODIFIER LETTER CYRILLIC SMALL A）→ `0430` が**両者一致**。
＝**.NET 10 の正規化データは少なくとも Unicode 15.0 相当**。04 ノート §1-3-3 の「版差でずれうる・未確認」は**「この機・この版では差が無い」で閉じてよい**（断定）。

参考の指紋：`CultureInfo.InvariantCulture.CompareInfo.Version.FullVersion = 31129` / `SortId = 00007999-0000-0000-0000-00000000007f`、
`System.Globalization.Invariant = False`、`UseNls`（AppContext スイッチ）は未設定。
※この数値が ICU の何版に対応するかは**未確認**（差分が 0 なので実害の判定には不要）。

### 5-2. **唯一の差＝U+FFFE で .NET だけ例外（断定）**

```
[FFFE ] -> ArgumentException: String contains invalid Unicode code points. (Parameter 'strInput')
      IsNormalized -> ArgumentException: String contains invalid Unicode code points. (Parameter 'source')
[FFFF ] -> OK  FFFF          （U+FFFF は通る）
[FDD0 ] -> OK  FDD0          （非文字 U+FDD0-FDEF も通る）
[D800 ] -> ArgumentException  （孤立サロゲート）
[DFFF ] -> ArgumentException
[0061 FFFE 0062 ] -> ArgumentException   （文中に 1 個混ざるだけで落ちる）
[FFFD ] -> OK / [0000 ] -> OK
```
Python は `unicodedata.normalize("NFKC","\ufffe")` を `"\ufffe"` のまま返す。
**移植した `normalize_text` にそのまま渡すと `ArgumentException` で落ちる**（実測：`normalize_text("あ\ufffeい")` が例外）。

**対処＝1 行**。`Normalize` の前に U+FFFE と孤立サロゲートを落とす／U+FFFD に置き換える。
読み上げ本文にこれが混じるのは「壊れたエンコードのファイルを読ませた」場合だけだが、**クラッシュ経路なので必ず塞ぐこと**。

---

## 6. `normalize_text` の C# 逐語移植

`lab/dotnet/TokProbe/IrodoriTextNormalization.cs`＝**110 行（実効 82 行）**。
`upstream/Irodori-TTS/irodori_tts/text_normalization.py:6-74` を行番号つきで写した。適用順は 04 ノート §1-1 の①〜⑤のまま。

```csharp
private static readonly (string Old, string New)[] SimpleReplaceMap = {
    ("\t",""), ("[n]",""), (@"\[n\]",""), ("\u3000",""), ("？","?"), ("！","!"),
    ("♥","♡"), ("●","○"), ("◯","○"), ("〇","○"),        // :7-16（挿入順＝Python の dict 反復順）
};
private static readonly Regex Re1 = new("[;▼♀♂《》≪≫①②③④⑤⑥]");                     // :20
private static readonly Regex Re2 = new("[\u02d7\u2010-\u2015\u2043\u2212\u23af\u23e4\u2500\u2501\u2e3a\u2e3b]"); // :21
private static readonly Regex Re3 = new("[\uff5e\u301C]");                          // :22
private static readonly Regex Re4 = new("…{3,}");                                    // :23
...
text = StripOuterBrackets(text);                    // :67
text = text.Normalize(NormalizationForm.FormKC);    // :69
text = text.Replace("...", "…"); text = text.Replace("..", "…");  // :71-72
```

（実ファイルは対象文字をリテラルで書いてある。上では読みやすさのため `\uXXXX` に置き換えたが、
実ファイルの非 ASCII 文字を全数ダンプして `text_normalization.py:20-22` の文字集合と一致することを確認済み
＝Re2 は U+02D7・U+2010〜U+2015・U+2043・U+2212・U+23AF・U+23E4・U+2500・U+2501・U+2E3A・U+2E3B、Re3 は U+FF5E・U+301C。）

**照合結果＝77 件（04 ノート §1-2 の実測表 17 行＋corpus 60 文）すべて Python と一致（0 不一致）。**
表の再現（C# の出力・逐語）：

| 入力 | C# 出力 | 04 ノート §1-2 |
|---|---|---|
| `「こんにちは…」` | `こんにちは…` | 一致 |
| `あ…………ん` | `あ……ん` | 一致 |
| `ＡＢＣ１２３` | `ABC123` | 一致 |
| `テスト～です` | `テストーです` | 一致 |
| `（笑）` | `笑` | 一致 |
| `え、そうなの？！` | `え、そうなの?!` | 一致 |
| `囁き👂です` | `囁き👂です` | 一致 |
| `ｱｲｳ` | `アイウ` | 一致 |
| `①②③テスト` | `テスト` | 一致 |
| `⑦テスト` | `7テスト` | 一致 |
| `Ⅲ章` | `III章` | 一致 |
| `㈱テスト` | `(株)テスト` | 一致 |
| `‥` | `…` | 一致 |
| `･･･` | `・・・` | 一致 |
| `；セミコロン` | `;セミコロン` | 一致 |
| `♀♂` | `` | 一致 |
| `こんにちは。。` | `こんにちは。。` | 一致 |

**移植で注意した点（断定）**

1. Python の `str` は**コードポイント**添字、C# の `string` は **UTF-16 単位**添字。`strip_outer_brackets` は括弧 5 対がすべて BMP なので**結果は同値**（`len(text)<2` の分岐も、サロゲート対 1 個だけの入力では両方とも「剥がさない」に落ちる）。ただし**同値である理由はコード上の偶然**なので、括弧表を増やすときは要再検証。
2. `\[n\]`（:9）は `re` ではなく `str.replace` に渡るリテラル 5 文字。C# では `@"\[n\]"` で写した（04 ノート §1-1 の注のとおりほぼデッドコード）。
3. §5-2 の U+FFFE 例外。**Python 版には無い落ち方**なので、移植版だけが落ちる。

---

## 7. 性能・配布物（同一機・CPU）

| 指標 | Python（現行） | C#（`Microsoft.ML.Tokenizers` 3.0.0-preview） | C#（自前 171 行） |
|---|---|---|---|
| 起動（import／ロード） | `import transformers` **13,141 ms** ＋ `from_pretrained` **684 ms** | `.model` ロード **373〜724 ms** | `tokenizer.json` ロード **71〜84 ms** |
| 常駐 | RSS **555 MB**（transformers 込み） | プロセス WS **54 MB**・マネージド +12 MB | +15 MB（同一プロセス内） |
| 符号化（corpus 60 文の平均） | HF fast **17.92 µs/文**／sentencepiece **6.78 µs/文** | **2.44〜3.65 µs/文** | **7.43 µs/文** |
| 符号化（敵対 108,430 件） | — | **187〜245 ms** | **189 ms**（1.74 µs/件） |
| `normalize_text` | **2.86 µs/文** | **1.06〜1.40 µs/文** | 同左 |
| 配布物（前段のコードぶん） | `transformers` 53 MB ＋ `tokenizers` (Rust) ＋ `sentencepiece` | **DLL 1 本 277,776 B・依存ゼロ** | **0 B**（自分のコード 171 行） |
| 配布物（データ） | `tokenizer.json` **6,718,495 B** | `.model` **2,035,786 B** | `tokenizer.json` 6,718,495 B（そのまま同梱） |

自前実装が corpus60 で ML.NET より遅いのは、素朴な「位置ごとに最長 8 文字ぶん辞書引き」だから（ML.NET は DoubleArrayTrie）。
短文が多い敵対セットでは逆に自前のほうが速い。**どちらも Python より速く、実務では差が問題にならない**（1 発声の合成は §11 ノート §5 のとおり秒オーダー）。

---

## 8. 停止域の確認

```bash
git -C upstream/Irodori-TTS        status --short --ignored   # -> 出力なし
git -C upstream/Irodori-TTS-Server status --short --ignored   # -> 出力なし
find upstream -name __pycache__ | wc -l                        # -> 0
sha256sum [hf]/…/tokenizer/tokenizer.json
#   6a0734cf21c802169defaffe719bc2ef12bb9d0be37e54b61ed27aa89394723d（着任時と同一）
```

- 書いたのは `lab/bin/`（`tok_json_to_spm.py`・`make_corpus.py`・`tok_compare_py.py`・`tok_stress_py.py`・`tok_stress_inputs.py`・`tok_diff.py`・`nfkc_diff.py`）、`lab/dotnet/TokProbe{,20}/`、`lab/tmp/`（ML.NET のソース写しと nupkg）、`lab/out/`（`corpus60.json`・`irodori_tok*.model`・`tok_*.json`・`nfkc_*`・`norm_*`・`stress_inputs.json`）、本ノートのみ。
- ネットは **NuGet の復元** と **GitHub / nuget.org / learn.microsoft.com の読取**のみ。`dotnet publish` は実行していない。外部へ何も送っていない。
- GPU 未使用。8088/7861 は起動も停止も送信もしていない。

---

## 9. 主席への注意

1. **「前段が出せない段」ではない。前段は最も安い段（断定）。**
   `normalize_text` 82 行＋トークナイザ 149 行＝**実効 231 行の C#** で、Python と **id 列が完全一致**（60 文＋108,430 件の敵対検分で 0 不一致）。
   BRIEF §4-2 の「部分移植の境界」は、04 ノート §6-1 の結論どおり**透かしの `torch.istft` に一点集約**したままでよい。前段は境界の外（＝出せる側）。

2. **⒝案の工数見積り＝前段は「1 便の 3 分の 1」以下。**
   当便が**実物を書いて検証まで済ませてある**（`lab/dotnet/TokProbe/HfUnigramTokenizer.cs`・`IrodoriTextNormalization.cs`）。
   製品に写すときの残作業は (a) U+FFFE/孤立サロゲートの衛生処理 1 行、(b) BOS(id=1) 先頭付与・EOS なし・`padding_side="right"`・`pad_id=3`・`max_text_len=256` のバッチ整形（04 ノート §1-4 の「符号化の実際の手順」を写すだけ）、(c) キャプション側は `normalize_text` を**通さない**（04 ノート §1-4 末尾）ことの再現。**いずれも十数行**。

3. **`Microsoft.ML.Tokenizers` 経路は「使えるが、罠を 1 つ知っていれば」。**
   - 使うなら **安定版 2.0.0 で足りる**（0/108,430 一致を確認済み）。純マネージド・MIT・native DLL 無し。3.0.0-preview なら `Google.Protobuf` 依存すら消える（DLL 1 本 278 KB）。
   - ただし **`.model` の byte 片を `NORMAL` 型で書く** という細工が要る（ML.NET の `ByteCodeToIdOffset` バグ回避・§4-2）。その `.model` は sentencepiece が読めない片道の派生物になる。
   - **推奨は自前 171 行のほう**。`tokenizer.json` をそのまま同梱でき、変換工程も NuGet 依存も要らず、特殊トークンの扱いまで HF と一致する。ML.NET を採るのは「自分でトークナイザを保守したくない」場合の選択で、その場合も**上のバグは上流に報告しておくべき**（GitHub issue は 0 件＝未報告）。
   - 参考：`main` には `SentencePieceTokenizer.CreateFromTokenizerJson`（`tokenizer.json` 直読み）が **2026-07-16 に入っている**が、最新 NuGet（2026-03-12 公開）には**まだ乗っていない**。次のリリースで乗れば `.model` 変換ごと不要になる＝**採用判断は「次のリリースを待てるか」に依存する**。

4. **NFKC の版差は、この機・この版では実害ゼロ（断定）。**
   68,218 コードポイントで 0 件差。04 ノート §1-3-3 と §6-6(b) の「未確認」を閉じてよい。
   **残る実害は U+FFFE 1 点だけ**で、これは版差ではなく .NET の入力検証（`ArgumentException`）。Python 版には無い落ち方＝**移植版だけがクラッシュする**ので、⒝案の受け入れ試験に必ず入れること。
   ※ただし「.NET の正規化データが将来 Unicode の版を上げても Python と一致し続ける」保証は無い。**製品では ①出荷時に本便のスクリプト（`lab/bin/nfkc_diff.py`）で全数照合を回帰試験にする か、②NFKC 表を自前で持つ**、のどちらかを勧める（①のほうが安い）。

5. **代替ライブラリは配布物で負ける。**
   Rust バインディング（`Tokenizers.DotNet` MIT ／ `Tokenizers.HuggingFace` Apache-2.0）は動くが、**win-x64 だけで native DLL が 3.96 MB / 4.78 MB**、しかも RID ごとに別パッケージ。⒜専用インストーラの「配布物を小さく」に逆行する。
   前段のためだけに ROCm/CUDA と別の「OS×arch 別バイナリ」の軸を増やすのは、BRIEF §4-7 の GPU 対応範囲の議論を無用に複雑にする。**採らないほうがよい**（推奨）。

6. **副産物＝⒝案の「起動の軽さ」の数字。**
   Python 側は前段を触るためだけに `import transformers` に **13.1 秒**・RSS **555 MB** を払っている（11 ノート §5 の「モデル読み込み 11.3 s」とは別の固定費）。
   前段を C# 化すると**この 13 秒と 500 MB がまるごと消える**。⒝案の利点は RTF だけでなく「起動の速さ」にもある、という材料として比較表に入れてよい。

7. **未確認として残したもの**：(a) `CompareInfo.Version.FullVersion = 31129` が ICU の何版に対応するか、(b) .NET の `String.Normalize` が Windows 上で ICU と NLS のどちらを引いているか（`UseNls` スイッチは未設定＝既定）、(c) 孤立サロゲートを含む入力での HF 側の挙動（Rust `&str` では表現できないため比較していない）、(d) `CreateFromTokenizerJson` が乗る NuGet リリースの時期、(e) ModernBERT テキストエンコーダの ONNX 化（担当外・13 ノート参照）。
