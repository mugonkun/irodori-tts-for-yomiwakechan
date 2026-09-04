# 31 — 参照音声（サンプルボイス）を当てたときのコスト

> 席＝第 7 次サブ席（opus）／担当＝司令官の問い「**異なるサンプルボイス（参照音声 10〜30 秒）を当てたとき、
> レスポンスは大きく落ちるか**」。実施 2026-09-04。
> これまでの速度実測（`13`・`20`・`23`・`24`・`25`・`27`・`28`）は**全部 `--no-ref`（CFG バッチ 3）**で、参照音声つきは未測定だった。本便がそこを埋める。
> **GPU は使っていない**（別席が 8091 で GPU 実射中）。実測は `lab/.venv`（CPU・torch 2.10.0+cpu・fp32）。8088／7861／8091 には触れていない。
> 上流は**読むだけ**（monkeypatch は torchaudio.load/save のシム 1 つだけ・§7）。引用の相対パスの起点は
> `C:/Users/mugonkun/source/repos/irodori-native-research/`。行番号は `grep -n`／`sed -n` の実測。

## 要点（10 行以内）

1. **答え＝「レスポンスは落ちるが、落ち方は二層に分かれる」**。⑴ **参照の符号化＝参照秒数に比例する固定費**（CPU で 10 s→1.17 s／30 s→3.35 s）、⑵ **DiT の CFG 枝が 3→4 に増える＝サンプリングが CPU 実測で +20.6 %**（**参照秒数にはほぼ依らない**＝10 s と 30 s の差はさらに +3.2 pt だけ・§5-5）。GPU では ⑴ が小さくなるので、効くのは主に ⑵。
2. **CPU 実測（fp32・40 steps・短文）＝`--no-ref` 14.53 s → 参照 10 s 17.46 s（+20 %）→ 参照 30 s 19.81 s（+36 %）**（min of 3・§5）。RTF 3.86 → 5.20 → 6.04。
3. **参照コストの 97 % は DACVAE encoder**（10 s＝1,122 ms／30 s＝3,289 ms）。**ReferenceLatentEncoder（8 層）は 26／61 ms＝2 %**、audiotools のラウドネス正規化は 9／65 ms＝**無視できる**。「参照 encoder が重い」という直観は**外れ**。
4. **BRIEF の前提を 1 つ訂正**＝「DiT は speaker トークン数固定で無関係」は**誤り**。joint attention の鍵は `cat([self, text, speaker, caption])`（`model.py:401-403`）で、speaker トークンは no-ref の **2 個**から 10 s で **63 個**・30 s で **188 個**に増える（鍵長 864→925→1,050＝+7 %／+22 %）。ただし影響は CFG 枝増より小さい。
5. **参照 encoder の出力トークン数は 2 ではない**。`[B,2,768]`（`13` ノート）は **`--no-ref` 限定の形**。実測式＝`floor(round(sec×25)/4)+1`（10 s→63・30 s→188・120 s→751）。pooling は無い（時間平均トークンを 1 個**前置**するだけ）。
6. **キャッシュは無い**。同じ voice を続けて使っても**要求ごとに全段が走る**。`runtime_memoized`（bench JSON）は `InferenceRuntime.from_key` のメモ＝**モデル読み込みの話で参照とは無関係**。さらに悪く、**チャンク分割すると 1 チャンクごとに参照を符号化し直す**（`app.py:657`／`:710`）。
7. **1 合成につき参照 encoder は 2 回走る**（duration 用 `inference_runtime.py:1281` ＋ サンプリング用 `rf.py:220`）。実測でも `predict_duration` が +31 ms（10 s）／+74 ms（30 s）増え、参照 encoder 1 回ぶん（26／61 ms）と一致。
8. **逃げ道は 2 本ある**＝⑴ **`ref_latent`（`.pt`／`.pth`）を事前計算して voices に置く**（`voices.py:184-185`）＝**コストの 97 % が消える**、⑵ **`ref_embed`（`*.speaker.safetensors`）＝Speaker Inversion**（`voices.py:177-179`）＝**参照 encoder も消え、CFG 枝 4 は残る**。⒞（専用制御方式）はここを握るのが最も効く。
9. **GPU の見積り（理論値・28 と 27 から）**＝CUDA EP（ONNX）1.094 s／RTF 0.291 → **1.21〜1.27 s／0.32〜0.34**。torch bf16（3090・`27`）0.808 s → **0.95〜1.05 s／RTF 0.25〜0.28**（参照符号化の GPU 時間込みの見立て）。**どちらも RTF < 0.5 を割らない＝配信用途の裁定を覆さない**。
10. **`speaker_cfg_scale=0` を明示すれば枝は落ちて batch 3 に戻る**（`rf.py:271` の 1 行）。声の似せ具合と引き換えに DiT の増分だけを消せる＝読み分けちゃんの「速度優先」プリセットの材料。

---

## 1. 参照音声の経路（檔:行つき）

### 1-1. Server 側＝どう voice を指定するか

| 指定の仕方 | 実装 | 備考 |
|---|---|---|
| **檔名（stem）** | `voices.py:168-186` `_scan_voice_files` | `voices_dir`（既定 `Path("voices")`＝**プロセスのカレント相対**・`config.py:41`）を**要求ごとに `iterdir()`**（キャッシュ無し）。`alice.wav` → `voice:"alice"` |
| 拡張子で役割が決まる | `voices.py:11-22`, `:177-185` | 音声（`.wav .flac .mp3 .m4a .ogg .opus .aac .webm`）→ `ref_wav`／`.pt .pth` → **`ref_latent`**／`*.speaker.safetensors` → **`ref_embed`** |
| **アップロード** | `POST /v1/audio/voices`（`app.py:237-256`）multipart `file` ＋任意 `voice_id`。201・重複 409 | **中身を検証しない**（拡張子と非空のみ・`voices.py:119-127`）＝壊れた wav は合成時に初めて落ちる |
| alias | `voices.json`（`voices.py:188-196`・`:250-254`） | スキャン結果を**上書き**。`ref_wavs`（複数クリップ）を組めるのはここだけ |
| **パス直指定** | `irodori.ref_wav` を送ると **voice 解決を丸ごと迂回**（`app.py:418-459`・`14` ノート 0f） | この場合 `voice_id` は文字列 `"request"` 固定。同一機なら upload 不要 |

- **caption は要らない**。voice（参照音声）だけで 200 が返る。caption は独立の欄で、参照と**併用**もできる（話者は参照から・語り口は caption から・`Irodori-TTS-Server/README.md:190-233`／`06` §3-6）。
- 逆に **voice も要らない**（`irodori.no_ref=true` だけで 200・`14` ノート T-1）。
- **`VoiceSpec` は参照のパスしか持てない**（`voices.py:28-36`）＝caption も cfg もサーバ側に保存できない（`05` §5-2）。
- サーバは**前処理を何もしない**（パスを渡すだけ・`06` §3-5）。

### 1-2. `irodori_tts` 側＝段の並びと形（実測の形を併記）

`InferenceRuntime.synthesize` → `_load_reference_latent`（`inference_runtime.py:836-991`）→ `encode_conditions`（`model.py:1720`）。

| # | 段 | 檔:行 | 入力形 | 出力形 | 10 s の実測 | 30 s の実測 |
|---|---|---|---|---|---|---|
| R0 | wav 読み | `inference_runtime.py:1515-1527` `_load_audio` | path | `(C,T)` ＋ sr | `(1,480000)` 48 kHz | `(1,1440000)` 48 kHz |
| R1 | **モノ化** | `codec.py:198-199` `waveform.mean(dim=1, keepdim=True)` | `(B,C,T)` | `(B,1,T)` | — | — |
| R2 | **48 kHz へリサンプル** | `codec.py:200-201` `torchaudio.functional.resample` | `(B,1,T)` | `(B,1,T')` | 素材が 48 k なので不発 | 同左 |
| R3 | ラウドネス正規化 −16 dB | `codec.py:133-173` `_normalize_loudness`（audiotools）／既定 `ref_normalize_db=-16.0`（`inference_runtime.py:210`） | `(T,)` | `(T,)` | **9.4 ms** | **65.3 ms** |
| R4 | **DACVAE encoder** | `codec.py:246-251`（`deterministic_encode=True` なら `encoder` → `quantizer.in_proj(z).chunk(2)` の**mean 側だけ**） | `(B,1,T)` | `(B,T_ref,32)` **25 fps** | `(1,250,32)`・**1,122 ms** | `(1,750,32)`・**3,289 ms** |
| R5 | patchify（latent_patch_size=1＝恒等） | `codec.py:14-25`／呼び `inference_runtime.py:976-979` | `(B,T,32)` | 同左 | `(1,250,32)` | `(1,750,32)` |
| R6 | **speaker patch（4）** | `model.py:114-148` `patch_sequence_with_mask`／呼び `model.py:1802-1806` | `(B,T,32)` | `(B,T//4,128)` | `(1,62,128)` | `(1,187,128)` |
| R7 | **ReferenceLatentEncoder** | `model.py:880-920`（`in_proj` `:887` → **`x/6.0`** `:913` → **8 層 `TextBlock`** `:889-898`） | `(B,S,128)` | `(B,S,768)` | **26.4 ms** | **61.3 ms** |
| R8 | RMSNorm（`speaker_norm`） | `model.py:1808` | 同左 | 同左 | R7 に含む | 同左 |
| R9 | **時間平均トークンを 1 個前置** | `model.py:1809`／定義 `:1611-1624` | `(B,S,768)` | `(B,S+1,768)` | **`(1,63,768)`** | **`(1,188,768)`** |

- **`TextBlock`（`model.py:610-624`）は素の全結合 self-attention**＝`SelfAttention`＋SwiGLU。**pooling は無い**（`AttentionPooling`／`CrossAttentionPooling` は duration 予測器の中だけ）。
- **出力トークン数＝`floor(round(sec×25)/4)+1`**。`--no-ref` だけが `2`（zeros 4 フレーム → patch 4 → 1 → +1・`inference_runtime.py:871-887`）＝`13` ノートの `[B,2,768]` は**この経路限定の形**。120 s なら **751**（`13` §4-2 で動的軸として確認済み）。

### 1-3. 参照の長さ制限（**檔:行つき・断定**）

| 論点 | 事実 | 檔:行 |
|---|---|---|
| 上限 | `max_ref_seconds`。既定は**チェックポイント値**＝v4.1-Small は **120.0 s**（本便でも `default_max_ref_seconds=120.0` を実測） | `inference_runtime.py:842-846`・`:483-490`／メタ `ref_max_seconds:120.0`（`05` §1-3） |
| **単一 wav** | 秒で**先に波形を切る**。`wav = wav[:, :max_ref_samples]` | `inference_runtime.py:937-944` |
| その warning（逐語） | `warning: reference audio exceeds max_ref_seconds (120.0s). Trimming from XX.XXs to YY.YYs.` | `inference_runtime.py:940-943` |
| **複数 wav** | 波形では切らず、**1 本ずつ符号化して潜在を連結**し、合計が上限を超えたら**その時点でループを打ち切る**（`break`）＝最後の 1 本は丸ごと符号化してしまう | `inference_runtime.py:955-963` |
| 連結後の切り詰め | 潜在で頭から切る＋warning | `inference_runtime.py:966-974` |
| **ランダム切り出し** | **無い**。切るのは常に**頭から**（`[:max_ref_samples]` / `[:, :max_ref_latent_steps]`） | 上記 |
| 上限の無効化 | `max_ref_seconds` に **0 以下**を渡すと上限そのものが消える | `inference_runtime.py:892-893`（`if max_ref_seconds > 0:` の外） |
| 下限 | 明示の秒下限は無い。ただし **patch 後が 0 だと `ValueError`**＝実質 **4 潜在フレーム＝0.16 s 以上**が要る | `model.py:141-145`・`inference_runtime.py:980-983` |
| 上流の推奨 | **同一話者の短いクリップを複数・合計 30 秒程度で似せ効果はほぼ飽和**。1 本の長尺は「受け付けるが未評価」（逐語＝`A single uninterrupted long recording is accepted by inference, but that input format has not been evaluated and may behave differently.`） | `docs/parameters.md:59-63`／`README.md:150-154` |

→ **司令官の「10〜30 秒」は上流の推奨レンジのど真ん中**で、上限 120 s に対しては 1/4 以下＝**切り詰めも警告も起きない**。

---

## 2. 長さに比例する段はどれか（形状で示す）

短文（S_lat=94）・fp32 の場合。

| 段 | 計算量 | 長さ依存 | 形の根拠 | CPU 実測（10 s → 30 s） |
|---|---|---|---|---|
| R4 DACVAE encoder | **O(L)** | **比例** | Conv1d だけ（`dacvae/model/dacvae.py:122-150` の `Encoder`＝`NormConv1d`＋`EncoderBlock`×4）。**encoder に LSTM は無い**（LSTM は透かし枝のみ・`18` §1-l'） | **1,122 → 3,289 ms（2.93 倍／長さ 3 倍）** |
| R3 audiotools 正規化 | O(L) | 比例だが**微小** | ITU-R BS.1770 のフィルタ＋二乗平均 | 9.4 → 65.3 ms |
| R7 ReferenceLatentEncoder | **O(S²·d + S·d²)** | S=62／187 では **d² 項が支配＝ほぼ線形** | 8 層 `TextBlock`（`model.py:889-898`）＝self-attention（`model.py:193` の SDPA・`is_causal=False`）＋SwiGLU。60.5 M パラメータ（`03` §5） | **26.4 → 61.3 ms（2.32 倍）** |
| R9 平均トークン | O(S) | 比例だが無視できる | `model.py:1617-1620` | — |
| **DiT 1 ステップ** | **鍵長に比例**（クエリは S_lat 固定） | **弱く比例する**（＝BRIEF の前提の訂正） | joint attention は `k = torch.cat(context_k, dim=1)`（`model.py:401`）・`attn_mask = torch.cat(context_masks, dim=1)`（`:403`）＝**鍵長 ＝ S_lat + 256(text) + (S_spk+1) + 512(caption)** | 鍵長 **864 → 925（+7.1 %）→ 1,050（+21.5 %）** |
| DiT の KV 射影 | O(S_spk·d²)・**ループ外 1 回** | 比例するが 1 回だけ | `model.py:252-310` `project_context_kv`／`rf.py:413-423` `build_context_kv_cache` | 実測では分離していない（`sample_rf` に含む） |
| duration 予測器 | 参照長に**依らない** | — | `AttentionPooling`（`model.py:471`）で speaker を 1 本に潰す | `predict_duration` +31 → +74 ms（＝R7 の 2 回目ぶん） |
| decode／透かし | **出力音声長**に比例（参照とは無関係） | — | `11` §5 | 参照の有無で不変（§5 の表） |

**読み取り（断定）**
- **参照秒数に効く段は R3〜R7 だけ**で、その 97 % が **R4（DACVAE encoder）**。
- **DiT は「speaker トークン数固定で無関係」ではない**が、鍵長の増分（+7〜22 %）は attention 部分にしか効かず、ブロックの費用は MLP／射影が支配するので、実測の増分は §5 のとおり CFG 枝増より小さい。
- **クエリ側（S_lat）は参照と無関係**＝参照を長くしても DiT の 2 次項は増えない（鍵だけが伸びる非対称構造・`03` §2 の指摘どおり）。

---

## 3. キャッシュ（同じ voice を続けて使うとき）

**結論＝再利用しない。1 要求ごとに §1-2 の全段（R0〜R9）が走る。**

| 層 | 何をキャッシュするか | 檔:行 |
|---|---|---|
| `irodori_tts` | **参照潜在のメモ化は 0 件**。`_load_reference_latent` は `synthesize` のたびに呼ばれる | `inference_runtime.py:1231`（`synthesize` 本体）／`grep -rn "lru_cache\|memo"` で推論経路に該当なし |
| Server `runtime.py` | **`InferenceRuntime` そのもの**を 1 つ抱えるだけ（二重チェック＋`threading.Lock`）。参照には触らない。`unload` 経路も無い | `runtime.py:31-66`・`:28` |
| Server `voices.py` | **要求ごとに `voices_dir` を `iterdir()`**（キャッシュ無し）。返すのは**パスだけ** | `voices.py:168-186` |
| Server `app.py` | 参照に関する保持は無い。`ref_wav` は文字列のまま `SamplingRequest` へ | `app.py:858-859` |
| bench JSON の `runtime_memoized` | **`InferenceRuntime.from_key` のメモ**（キット側の monkeypatch）＝「2 発目以降はモデル読み込みを飛ばす」という意味。**参照の符号化とは無関係** | `lab/kit-cuda/bench_infer.py:195-206`／`21` ノート（`runtime_memoized: true` の説明） |

**さらに悪い 2 点（断定）**

1. **チャンク分割すると 1 チャンクごとに全段が走る。** 非ストリーム `app.py:657`・SSE `app.py:710` がともに `chunk_request = replace(sampling_request, text=chunk)`＝**text しか差し替えない**ので `ref_wav` はそのまま。既定 `chunk_min_chars=80` で長文を 4 チャンクに割れば、**参照の符号化が 4 回**走る（CPU・参照 30 s なら 3.35 s×4＝13.4 s の丸損）。
2. **1 合成につき参照 encoder（R6〜R9）は 2 回走る。** `encode_conditions` が duration 用（`inference_runtime.py:1281`）とサンプリング用（`rf.py:220`）で**2 回**呼ばれる（`13` §10-8 の指摘）。本便の実測でも `predict_duration` が参照ありで **+31 ms（10 s）／+74 ms（30 s）** 増え、R7 単体の 26.4／61.3 ms と一致する＝**この 2 回化は参照でも再現している**。

**逃げ道（⒞の設計に直結）**

| 手 | 効き | 檔:行 |
|---|---|---|
| **`ref_latent`（`.pt`／`.pth`）を事前計算して `voices/` に置く** | **R0〜R4 が消える＝参照コストの 97 %**（CPU で 30 s なら 3.35 s → 0.06 s 級）。残るのは `torch.load` と R6〜R9 | `voices.py:184-185`（拡張子で `ref_latent` に解決）／`inference_runtime.py:903-924`（latent 経路は `torch.load(weights_only=True)`＝`:906` だけ） |
| **`ref_embed`（`*.speaker.safetensors`＝Speaker Inversion）** | **R0〜R9 が丸ごと消える**（`encode_conditions` が参照 encoder をバイパス）。ただし **CFG 枝 4 は残る**（`cfg_scale_speaker>0` のまま） | `voices.py:177-179`／`model.py:1794-1800`／`inference_runtime.py:993-1035` |
| **⒞の層で潜在を自前キャッシュ** | 上と同じ効き。サーバ無改造で可能（層が `ref_latent` のパスを送るだけ） | — |
| `chunking_enabled:false` | チャンク数ぶんの重複を消す（代わりに初音が遅くなる・`14` T-6） | `app.py:555` |

---

## 4. CFG バッチ（参照ありで 4 になるか・理論値）

### 4-1. 枝が 4 になる条件（檔:行）

```
has_text_cfg    = cfg_scale_text > 0                                  # rf.py:264
has_caption_cfg = use_caption_condition and cfg_scale_caption > 0 ... # rf.py:265-270
has_speaker_cfg = cfg_scale_speaker > 0                               # rf.py:271
enabled_cfg_names = [text?, speaker?, caption?]                       # rf.py:305-316
cfg_batch_mult = len(independent_bundles)   # = 1 + len(enabled)      # rf.py:341
```

| 既定値 | 値 | 檔:行 |
|---|---|---|
| `cfg_scale_text` | 3.0 | `rf.py:129`・`inference_runtime.py:224`・`infer.py:229` |
| `cfg_scale_caption` | 3.0 | `rf.py:130`・`inference_runtime.py:225` |
| **`cfg_scale_speaker`** | **5.0** | `rf.py:131`・`inference_runtime.py:226`・`infer.py:231` |
| `cfg_guidance_mode` | `independent` | `rf.py:132` |
| `cfg_min_t` / `cfg_max_t` | 0.5 / 1.0 | `rf.py:133-134` |

- **`--no-ref` では 3**＝`use_speaker_for_request = use_speaker_condition_resolved and not req.no_ref`（`inference_runtime.py:1130-1131`）が False → `resolve_cfg_scales` が **`speaker_val = 0.0`** に落とし info を返す（`inference_runtime.py:336-341`・実測の逐語＝`info: speaker conditioning is disabled for this checkpoint or request; ignoring cfg_scale_speaker.`）。
- **参照ありでは 4**（本便の実測でもこの info が消える）。
- **`speaker_cfg_scale` を 0 にすると分岐が省略される**＝`rf.py:271` の `has_speaker_cfg = cfg_scale_speaker > 0` が False になり、枝が立たない。**speaker uncond 側の zeros 生成（`rf.py:252-253`）は走るが、DiT には流れない**＝**batch 3 に戻る**。
- CFG が入るのは `cfg_min_t <= t <= cfg_max_t`（`rf.py:464`）＝**40 steps なら 20 ステップ**（`13` §1 の実測 `n_cfg_steps=20`）。残り 20 は **batch 1**（`rf.py:534-546`）。

### 4-2. 理論値（`28` の CUDA EP 段時間・`27` の torch bf16 から）

**`28` の実測（fp32・短文 S_lat=94・warm）**＝CFG ステップ **30.5 ms（B=3）**・非 CFG ステップ **16.4 ms（B=1）**・DiT ループ **964 ms**（20×30.5 + 20×16.4 = 938 ms とほぼ一致）。

B=4 の 1 ステップを 2 通りで挟む：

| 見積り方 | 式 | B=4 の CFG ステップ | DiT ループ（20+20） | 増分 |
|---|---|---|---|---|
| **下限**（枝の限界費用で外挿） | 枝 1 本の限界＝`(30.5 − 16.4)/2 = 7.05 ms` → `30.5 + 7.05` | **37.6 ms** | **1,080 ms** | **+116 ms（+12.0 %）** |
| **上限**（計算律速＝バッチに線形） | `30.5 × 4/3` | **40.7 ms** | **1,142 ms** | **+178 ms（+18.5 %）** |

（参考＝CPU ORT の 1 ステップ実測比 B4/B3 は **1.361**（`13` §4-3 の 0.5253/0.3861）＝上限側にかなり近い。
**本便の CPU torch 実測（S_lat を揃えた §5-5）も +20.6 %**＝上限側。**GPU の小バッチはカーネル起動が支配する**ので下限側に寄ると読むのが素直。）

| 経路 | 参照なしの実測 | 参照ありの理論値 | RTF |
|---|---|---|---|
| **ONNX CUDA EP**（`28`・3090・fp32・40 steps） | 端から端まで **1.094 s／RTF 0.291**（DiT ループ 964 ms） | DiT ループ **1,080〜1,142 ms** → 端から端まで **1.21〜1.27 s**（＋参照符号化） | **0.32〜0.34** |
| **torch bf16**（`27`・3090・SSD・warm） | `sample_rf` **705.7 ms**・warm 合計 **0.808 s／RTF 0.215** | `sample_rf` **790〜833 ms** → 合計 **0.89〜0.94 s**（＋参照符号化） | **0.24〜0.25** |
| 上に参照符号化を足す（GPU・**未実測の外挿**） | — | GPU の DACVAE encoder は `decode_latent 22.4 ms／3.76 s` から外挿して **10 s 参照で 60〜100 ms** 級 | **0.25〜0.28**（bf16）／**0.34〜0.37**（CUDA EP） |

**読み取り（断定）**＝**GPU では参照ありでも RTF は 0.5 を割らない**。⒜（torch bf16・CUDA）の裁定も、⒝の判断則（DirectML RTF≤0.5 不合格・`28` §要点 4）も**参照音声を足しても覆らない**。
**未確認**＝GPU 上の DACVAE encoder の実所要（本便は GPU を使えないため外挿）。§6 の台本で埋める。

---

## 5. CPU 実測（`lab/.venv`・fp32・40 steps・seed 1234）

### 5-1. 条件

- 機体＝AMD Ryzen AI MAX+ 395（`nproc`=32）・`OMP_NUM_THREADS=8`・`torch.set_num_threads(8)`（`11` §5 の基準と同じ）。
- checkpoint＝`Aratako/Irodori-TTS-v4.1-Small`・`model_device=cpu`／`codec_device=cpu`・fp32。
- 本文＝`こんにちは、読み分けちゃんのテストです。`／caption＝`落ち着いた女性の声で、丁寧に話す。`（`11` §5 と同一）。
- 参照音声＝`lab/out/long_steps40.wav`（10.92 s・本便の合成物）を**繰り返して 10.0 s／30.0 s に整えたもの**（`lab/tmp/31_ref_10s.wav`・`31_ref_30s.wav`）。**声質は問わない＝時間だけを測る**。
- **本機は別席の GPU 実射で CPU が混んでいた**（実測中 1 プロセスが約 23 スレッド占有）。そのため **rep を外側・参照長を内側に回して交互に実行**（`--interleave`）し、**3 走の最小値**を採った（他プロセスの負荷は足し算にしか働かないので、最小値が最も素直）。生データは全走分を JSON に残してある。

### 5-2. 端から端まで（min of 3・ミリ秒）

正本＝`lab/out/31_ref_cpu_natural.json`。

| 参照 | 音声長 | prepare_reference | predict_duration | **sample_rf** | decode_latent | watermark | **合計** | **RTF** |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| **なし（`--no-ref`）** | 3.76 s | **0.0** | 974.6 | **12,405.8** | 836.3 | 300.1 | **14.527 s** | **3.86** |
| **10 s** | 3.36 s | **1,169.9** | 1,006.1 | **13,983.0** | 797.6 | 267.5 | **17.461 s** | **5.20** |
| **30 s** | 3.28 s | **3,350.5** | 1,048.5 | **14,323.1** | 749.3 | 237.2 | **19.811 s** | **6.04** |

**cold／warm（3 走の生値・秒）**

| 参照 | rep1（cold） | rep2 | rep3 |
|---|---:|---:|---:|
| なし | 26.409※ | 14.527 | 14.845 |
| 10 s | 17.461 | 18.186 | 18.165 |
| 30 s | 19.811 | 20.035 | 19.985 |

※ rep1 の `--no-ref` は `predict_duration` が **12,456 ms**（warm は 975 ms）＝**プロセス最初の 1 回だけ ModernBERT のウォームアップを丸ごと被る**。参照の有無とは無関係の一度きりの費用（`11` §5 の「モデル読み込み後の初回」に相当）。**cold/warm の差はここに集中し、参照音声は cold/warm 差をほとんど作らない**。

### 5-3. 段別の読み取り（断定）

1. **`prepare_reference` は参照秒数にきれいに比例**＝10 s で 1,170 ms（**117 ms／参照 1 秒**）・30 s で 3,351 ms（**112 ms／参照 1 秒**）。`--no-ref` は 0.0〜0.1 ms。
2. **`sample_rf` は +12.7 %（10 s）／+15.5 %（30 s）**。ただし**参照ありのほうが S_lat が小さい**（94 → 84 → 82・後述 4）＝**尺が短くなったぶんが増分を隠している**。
   S_lat を揃えた対照（§5-5）では **+20.6 %／+23.8 %**。§4-2 の理論値（CFG 枝 3→4 で +12〜18 %）の上限側に、**speaker 鍵長の増**が上乗せされた形で整合する。
3. **`predict_duration` は +31 ms（10 s）／+74 ms（30 s）**＝§1-2 の R7（26.4／61.3 ms）とほぼ一致＝**duration 経路でも参照 encoder が丸ごと 1 回走っている**ことの実測証拠。
4. **参照を当てると予測尺が短くなった**＝`predicted duration frames` が **94.3（no-ref）→ 83.8（10 s）→ 82.4（30 s）**（逐語メッセージ）。`build_duration_features` の `has_speaker` と speaker 条件が duration 予測器に入るため（`model.py:1340-1362`）。**同じ本文でも参照の有無で出力の長さが変わる**＝読み分けちゃんの尺合わせに効く（`duration_scale` で補正が要る）。
5. **`decode_latent` と `silentcipher_watermark` は参照の影響を受けない**（音声長にのみ比例）。参照ありで数値が小さいのは音声が短くなったせい。

### 5-4. 参照 encoder 単体（分解測定・min of 4）

同 JSON の `ref_encode_micro`。

| 参照 | wav | 潜在 | 参照 encoder 入力 | speaker_state | `_load_audio` | **DACVAE encode（−16 dB 正規化込み）** | 同（正規化なし） | **audiotools 正規化ぶん** | **ReferenceLatentEncoder** |
|---|---|---|---|---|---:|---:|---:|---:|---:|
| **10 s** | `(1,480000)` 48 k | `(1,250,32)` | `(1,62,128)` | **`(1,63,768)`** | 1.8 ms | **1,121.9 ms** | 1,112.5 ms | **9.4 ms** | **26.4 ms** |
| **30 s** | `(1,1440000)` 48 k | `(1,750,32)` | `(1,187,128)` | **`(1,188,768)`** | 4.0 ms | **3,288.7 ms** | 3,223.4 ms | **65.3 ms** | **61.3 ms** |

- **参照コストの内訳＝DACVAE encoder（正規化込み）97.6 %（10 s）／98.1 %（30 s）**。**参照 encoder（8 層 Transformer）は 2.3 %／1.8 %**。
- **audiotools のラウドネス正規化は 0.8 %／2.0 %**＝`ref_normalize_db=None` にしても**ほとんど速くならない**（音質は変わる）。
- 参考＝正規化の有無で潜在が変わる量は `max abs 0.073`（10 s）／`0.050`（30 s）＝**音は確かに変わる**（速度のためだけに切る意味は無い）。
- 分解測定の **R0＋R4** の合計（1,124／3,293 ms）が §5-2 の `prepare_reference`（1,170／3,351 ms）とほぼ一致（差は patchify とデバイス転送）＝**段の切り分けは正しい**。
  なお **R6〜R9（参照 encoder）は `prepare_reference` に入らない**＝`encode_conditions` の中なので `predict_duration` と `sample_rf` に分かれて計上される（§5-3 の 3）。

### 5-5. S_lat を固定した対照（duration 予測をバイパス）

`--seconds 3.76` で出力尺を固定し（`SamplingRequest.seconds`＝duration 予測器を丸ごとバイパス・`inference_runtime.py:1245-1258`）、
**参照の有無で S_lat が変わる効果を消した**もの。**S_lat=94 で 3 条件とも同一**＝DiT の増分だけが見える。
正本＝`lab/out/31_ref_cpu_fixed.json`（rep1 は他プロセスの負荷を丸被りしたので rep2 を採る＝生値は JSON に全部残っている）。

| 参照 | S_lat | prepare_reference | **sample_rf** | 対 no-ref | decode | watermark | **合計** | **RTF** |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| **なし** | 94 | 0.1 ms | **12,675.8 ms** | — | 823.8 | 292.3 | **13.793 s** | **3.67** |
| **10 s** | 94 | 1,146.9 ms | **15,284.6 ms** | **+20.6 %** | 804.4 | 269.8 | **17.507 s** | **4.66** |
| **30 s** | 94 | 3,384.8 ms | **15,686.0 ms** | **+23.8 %** | 800.0 | 283.7 | **20.235 s** | **5.38** |

**読み取り（断定）**

1. **S_lat を揃えると DiT の増分は +20.6 %（10 s）／+23.8 %（30 s）**。うち **+20.6 pt が CFG 枝 3→4 ぶん**（参照秒数に依らない）、**残り +3.2 pt が speaker 鍵長 63→188 ぶん**（10 s→30 s で鍵長 925→1,050＝+13.5 %）。
   ＝**「参照を長くしたぶんの DiT の重さ」はごく小さく、効くのは「参照を当てたこと自体」**。
2. CPU の +20.6 % は §4-2 の上限側（計算律速・+18.5 %）に近い。**CPU は計算律速・GPU の小バッチはカーネル起動律速**なので、GPU では下限側（+12 %）に寄ると読むのが素直。
3. **副産物＝`seconds` を明示すると `predict_duration` が丸ごと消える**（no-ref で 14.527 s → 13.793 s＝**−0.73 s**）。duration 用の `encode_conditions`（ModernBERT ×2 ＋ 参照 encoder）を 1 回飛ばせるため（`inference_runtime.py:1245-1258` の分岐）。**尺を自分で決められる用途なら CPU で 5 % 弱、GPU でも `predict_duration 38 ms`（`27`）が浮く**。ただし `seconds` を送ると Server のチャンク分割が黙って無効化される（`14` T-7）＝併用に注意。

---

## 6. GPU 実測の台本

### 6-1. 台本＝`lab/bin/ref_bench.py`（本便が書いた・CPU で動作確認済み・GPU では走らせていない）

```
--device        cpu / cuda / cuda:N / xpu（既定 cpu）
--codec-device  既定＝--device と同じ
--precision     fp32 / bf16（既定 fp32）
--ref           参照秒数を並べる。0 は --no-ref（既定 "0 10 30"）
--steps         Euler ステップ数（既定 40）
--reps          各条件の反復（既定 2。rep1=cold・rep2 以降=warm）
--seconds       出力尺を固定して duration 予測をバイパス（S_lat を揃えたいとき）
--interleave    rep を外側・参照長を内側に回す（混んだ機では必須）
--micro-reps    参照符号化の分解測定の反復（既定 3）
--skip-synth / --skip-micro
--len           short / long（11 §5 と同じ本文）
--ref-src       参照素材 wav（既定 lab/out/long_steps40.wav・足りない長さは繰り返しで作る）
--threads       CPU の torch.set_num_threads（既定 8）
--tag / --out   JSON 出力
```

測るもの＝**A) 端から端まで**（`SamplingResult.stage_timings` をそのまま採る＝`prepare_reference` / `predict_duration` / `sample_rf` / `decode_latent` / `silentcipher_watermark`）と、**B) 参照符号化の分解**（`_load_audio` ／ `encode_waveform`（正規化あり・なし）／ `ReferenceLatentEncoder`）。
GPU では `torch.cuda.synchronize` を挟み、VRAM ピーク（`max_memory_allocated`）も採る。**上流は import するだけ**（`torchaudio.load/save` を soundfile へ差し替えるシムのみ・§7）。

### 6-2. 主席が `.venv-rocm` で回すための 1 行

```bash
PYTHONPATH=C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1 PYTHONUTF8=1 C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe C:/Users/mugonkun/source/repos/irodori-native-research/lab/bin/ref_bench.py --device cuda --codec-device cuda --precision bf16 --ref 0 10 30 --steps 40 --reps 3 --interleave --tag rocm_bf16_s40_short --out C:/Users/mugonkun/source/repos/irodori-native-research/lab/out/31_ref_rocm_bf16.json
```

（ROCm でも `--device` の値は **`cuda`**＝`06` §2-3・`compose.rocm.yaml:12-13`。fp32 で採るなら `--precision fp32`。
S_lat を揃えた対照が要るなら `--seconds 3.76 --skip-micro` を足してもう 1 本。
**8091 で別席が走っている間は回さないこと**＝数値が濁る。）

RTX 3090 機（`kit-ssd`）で回すなら `--device cuda --precision bf16` のまま python を 3090 側の venv に差し替えるだけ。ONNX（CUDA EP／DirectML）側の参照コストは本台本では測れない＝`26` のキットに参照経路を足す別便が要る（§8-5）。

---

## 7. 停止域の確認（無改変の証明）

```bash
git -C upstream/Irodori-TTS status --short --ignored          # -> 出力なし
git -C upstream/Irodori-TTS-Server status --short --ignored   # -> 出力なし
find upstream -name "__pycache__" -not -path "*/.git/*"       # -> 出力なし
```

- 書いたのは以下のみ＝`lab/bin/ref_bench.py`／`lab/out/31_ref_cpu_natural.json`（§5-2・§5-4 の正本）／`lab/out/31_ref_cpu_fixed.json`（§5-5 の正本）／
  `lab/out/31_ref_cpu_fp32_s40.json`（**第 1 走・機体が別席の負荷で混んでいた時間帯の値＝参考にしない**）／`lab/tmp/31_ref_10s.wav`・`31_ref_30s.wav`（参照素材）／
  `lab/tmp/31_run_natural.log`・`31_run_fixed.log`・`31_smoke.json`／本ノート。
- 上流の書き換えは**すべて monkeypatch**＝`torchaudio.load` / `torchaudio.save` を soundfile 実装に差し替えただけ（`ref_bench.py` 内）。理由＝torchaudio 2.10 の load/save は torchcodec 専用で、lab venv には torchcodec が無い。**`_load_audio`（`inference_runtime.py:1518`）は `except RuntimeError` しか持たないので `ImportError` が貫通する**＝`18` §3-e の指摘を**本便でも再現**（逐語＝`ImportError: TorchCodec is required for load_with_torchcodec. Please install torchcodec to use this function.`）。
- **GPU は 1 度も使っていない**（torch は CPU ビルド）。**8088／7861／8091 の起動・停止・HTTP 送信は一切なし**。外部へ何も公開していない（`HF_HUB_OFFLINE=1`）。
- `C:/IrodoriTTS/`・yomiwakechan2 は**読んでもいない**。`C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe` は **`ls` で存在確認を 1 回しただけ**（§6-2 の 1 行が通ることの確認）＝起動していない。
  HF キャッシュは checkpoint／コーデックの読み込みのみ（`HF_HUB_OFFLINE=1`・書き込みなし）。

---

## 8. 主席への注意

1. **司令官の問いへの答えは「落ちるが、GPU なら裁定は覆らない」**。CPU では +20 %（10 s）／+36 %（30 s）と目に見えて落ちるが、GPU（torch bf16・3090）の見積りは **RTF 0.215 → 0.25〜0.28**。**⒜の推奨も⒝の判断則不合格も、参照音声を足して再計算しても変わらない**。ただし**参照ありの GPU 実測は未了**＝§6-2 の 1 行で埋めてほしい。
2. **報告の速度表に「`--no-ref` の値である」と注記が要る**。`13`・`20`・`23`・`24`・`25`・`27`・`28` の全数値は CFG バッチ **3**（参照なし）。実運用（キャラごとの参照音声）は **4**＝**DiT ループが GPU 理論値で +12〜18 %・CPU 実測で +20.6 %**。
   比較表のレイテンシ欄に「**参照音声つきは DiT が +12〜18 %（GPU 見積り）＋ 参照秒数に比例する符号化の固定費**」を添えること。
3. **BRIEF の前提 2 件を訂正**＝⑴「参照 encoder の出力トークン数＝2」は **`--no-ref` 限定**（実運用は 10 s で 63・30 s で 188）。⑵「DiT は speaker トークン数固定で無関係」は**誤り**（鍵長が +7〜22 % 伸びる）。ただし②の影響は CFG 枝増より小さい。
4. **⒞（専用制御方式）の値打ちが 1 つ増えた**＝**参照潜在の事前計算とキャッシュ**。`ref_latent`（`.pt`）を voices に置くだけで参照コストの **97 %** が消える（サーバ無改造で可能）。`06` §8 の「⒞で自前に建てる必要があるもの」に **(5) 参照潜在のキャッシュ** を足すべき。読み分けちゃんの「声の登録」時に 1 回だけ符号化して `.pt` を吐く設計が素直。
5. **チャンク分割との掛け算に注意**＝`replace(sampling_request, text=chunk)` は text しか差し替えないので、**チャンク数ぶん参照を符号化し直す**。長文＋参照 30 s＋CPU フォールバックは最悪の組み合わせ（4 チャンクで 13.4 s の丸損）。⒞は「1 発話 1 要求＋`chunking_enabled:false`」を既定にするのが安全（`05` §6-3 の助言と同じ向き）。
6. **`speaker_cfg_scale=0` は「速度優先プリセット」の材料になる**（`rf.py:271`）。参照は当てたまま CFG 枝だけ 3 に戻せる＝DiT の増分は消え、参照符号化の固定費だけが残る。**声の似せ具合は落ちる**ので、聴感の判定は別席へ。
7. **参照を当てると予測尺が短くなる**（94.3 → 83.8 → 82.4 フレーム）。読み分けちゃんの尺合わせ／字幕同期を作るなら、**参照の有無で同じ本文の長さが変わる**ことを仕様に書くこと。
8. **未実施／未確認の札**＝(a) **GPU 上の参照符号化の実所要**（§4-2 の GPU 値は外挿）、(b) ONNX 経路（CUDA EP／DirectML）で参照ありの端から端まで（`26` のキットは `--no-ref` 前提＝`data/` の条件が S_spk=2 で焼かれている）、(c) **複数クリップ（`ref_wavs`）の実測**（1 本ずつ符号化するので合計秒数に比例するはずだが未測）、(d) `ref_latent` 経路の実測（理屈上 97 % 減だが未測）、(e) `ref_embed`（Speaker Inversion）経路（`.speaker.safetensors` を持っていない・`13` §10-d と同じ札）、(f) 参照 120 s（上限）での実測、(g) **音の検分は一切していない**（本便は時間だけ）。

---

## 追記（主席・Fable）＝司令官機 ROCm での GPU 実測（8060S・bf16・40 steps・seed 1234・3 走 interleave・最小値）

実行＝§6-2 の 1 行を `--ref-src lab/out/refvoices/vv_zundamon_30s.wav`（VOICEVOX ずんだもん・24 kHz）で短文・長文の 2 本。生データ＝`lab/out/31_ref_rocm_bf16_short.json`・`31_ref_rocm_bf16_long.json`（ログ同名 .log）。8091 の別席は終了後・GPU は本走のみ（CPU では第 8 次席が動作確認中）。

| 本文 | 参照 | 出力 s | min total s／RTF | prepare_reference ms | predict_duration | sample_rf | decode | watermark | VRAM ピーク MB |
|---|---|---|---|---|---|---|---|---|---|
| 短文 | なし | 3.80 | 0.922／0.243 | 0.1 | 55.5 | 638.2 | 58.5 | 34.5 | 2,307 |
| 短文 | 10 s | 3.16 | 1.048／0.332 | 136.1 | 38.2 | 652.6 | 49.5 | 30.6 | 2,323 |
| 短文 | 30 s | 3.20 | 1.239／0.387 | 328.2 | 46.1 | 778.7 | 51.4 | 29.3 | 3,030 |
| 長文 | なし | 10.96 | 1.753／0.160 | 0.2 | 41.8 | 1,452.3 | 165.9 | 85.2 | 2,942 |
| 長文 | 10 s | 10.72 | 2.079／0.194 | 131.9 | 56.0 | 1,644.0 | 160.5 | 83.3 | 2,916 |
| 長文 | 30 s | 10.88 | 2.360／0.217 | 324.8 | 48.8 | 1,730.3 | 169.2 | 82.7 | 3,030 |

cold（rep 1）＝短文 なし 2.626／10 s 1.492／30 s 4.140 s、長文 なし 5.331／10 s **19.401**／30 s 12.984 s＝参照の長さごとに初見形状の罰（DACVAE encoder の新しい入力長）。

参照符号化の分解（median・ms）＝10 s: load 1.0／encode_waveform(norm −16 dB) 123.9（正規化なし 94.0・audiotools 正規化 31.2）／ReferenceLatentEncoder 9.4。30 s: load 2.6／encode 306.7（正規化なし 258.5・正規化 57.7）／参照 encoder 9.4。＝**GPU でも符号化は秒に比例し、DACVAE encoder が 9 割**（CPU の 97 % と同じ構図）。

読み＝⑴ 端から端までの増分は短文 +14 %／+34 %、長文 +19 %／+35 %（時間）。RTF は最悪 0.387（短文 30 s）で 0.5 を割らない。⑵ sample_rf の増分は短文 +2 %／+22 %・長文 +13 %／+19 %＝CFG 枝 4 の影響は小バッチ（短文）では薄く、鍵長の増分（30 s＝+22 %）が短文でも効く。⑶ 短文は参照ありで出力が 3.80→3.16 s に縮む（§8 の予測尺の変化）＝RTF の上昇の一部は分母が減った分。⑷ 未確認＝3090（CUDA）での同条件・実機 VOICEVOX 5 話者（`32` のキット）。

訂正（検分席の指摘）＝上の表の段値は `summary_min`（段ごとに独立の最小＝cold 走の値が混ざる）から転記した。warm 走だけ（各条件の最良 warm 走）で採り直すと、短文の sample_rf は なし 760.5／10 s 768.8（+1 %）／30 s 781.5（+3 %）、predict_duration は 64.4／60.8／46.1、watermark 36.8／30.9／29.3。長文は表のとおり（1,452.3／1,644.0／1,730.3）。「短文でも鍵長の増分（+22 %）が効く」は撤回＝短文の DiT 増分は +1〜3 % で、CFG 枝 4 も鍵長の増分も小バッチのカーネル起動時間に埋もれる。VRAM ピークの +700 MB は短文だけ（長文は 2,944→3,027 MB＝+83 MB）。
