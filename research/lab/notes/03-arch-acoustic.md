# 03 — 推論の芯（音響モデル側）の構成と ONNX 化の可否

読解席（opus）／2026-09-04／担当＝BRIEF §4-2 の材料。**読むだけ**で作成（上流 clone・`C:\IrodoriTTS\`・HF キャッシュは 1 バイトも変更していない）。
引用の相対パスの起点は `C:\Users\mugonkun\source\repos\irodori-native-research\`。venv 内・HF キャッシュ内の檔は絶対パスで引く。

## 要点（10 行以内）

1. 芯は **Rectified-Flow DiT**（`TextToLatentRFDiT`・12 層・model_dim 1280・20 head）＋ **DACVAE 32 次元潜在**（48 kHz／hop 1920＝**潜在 25 フレーム/秒**）。音素化は無い（生テキスト→Unigram トークン）。
2. テキストと caption は**同一の事前学習 ModernBERT-ja-310m**（`sbintuitions/modernbert-ja-310m`・rev `77675fc9…`）を共有し、別々の射影器で 512 次元へ落とす。参照音声は**コーデック潜在→ReferenceLatentEncoder（8 層）→ prefix トークン列**として joint-attention に入る（speaker embedding 1 本ではない）。
3. **サンプリングループは純 Python**（`rf.py:459` の `for i in range(num_steps)`）。1 ステップ＝`forward_with_encoded_conditions` 1 回。条件符号化と KV 射影はループ外で 1 回きり。**「1 ステップ関数を export して C# で回す」は構造的に成立する**（データ依存の分岐・早期停止がループ内に無い）。
4. CFG は **independent（既定）**＝ text/speaker/caption を各々 uncond に落とした枝を**バッチ方向に連結**して 1 回で流す。既定 3 枝有効なら**バッチ 4 倍**。式は `v = v0 + Σ_i s_i·(v0 − v_i)`（`rf.py:483`）。
5. ONNX の**最大の障害は複素数 RoPE**（`model.py:39-41` の `view_as_complex` / `view_as_real`）。ONNX に複素型は無い＝**実数 cos/sin へ書き換えれば出せる**（数学的に等価・重みは不変）。
6. 次の障害は**透かし SilentCipher**＝`torch.stft` / `torch.istft`（`silentcipher/stft.py:22,36`）。**ISTFT に対応する ONNX 演算子が無い**＝ここは ONNX の外（C# 実装 or 省略）になる。
7. DACVAE デコーダは Conv1d/ConvTranspose1d＋Snake＋`weight_norm`。**export 前に `remove_weight_norm` と `forward_no_conv` のモジュール差し替えを済ませれば出せる**見込み。LSTM は透かし枝のみ＝復号経路に乗らない。
8. 段別暫定判定＝**そのまま出せる**：duration 予測器・射影器・DACVAE 復号（前処理後）。**書き換えれば出せる**：DiT ステップ・参照潜在 encoder・ModernBERT。**出せない／外に出す**：Unigram トークナイザ・SilentCipher・音量正規化（audiotools）・末尾トリム（Python ループ）。
9. 諸元＝**766,052,385 パラメータ・全部 F32**・`model.safetensors` 3,064,295,596 B（＝2.854 GiB＝BRIEF の「2.86GB」と一致）。潜在 32 次元・latent_patch_size 1・speaker_patch_size 4。
10. 精度＝**既定は fp32**。bf16 は **CUDA/XPU 限定**（`inference_runtime.py:304-313` で ROCm/CPU は例外）。`torch.autocast` は**訓練側だけ**で、推論経路では使われていない。

---

## 1. 推論パイプラインの段構成

`infer.py → InferenceRuntime.synthesize`（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1036`）の実行順。

| # | 段 | クラス／関数 | 檔:行 | 入力形 | 出力形 | 動的軸 |
|---|---|---|---|---|---|---|
| 0 | テキスト正規化 | `normalize_text` | `irodori_tts/text_normalization.py:60` | str | str | — |
| 1 | トークン化（本文） | `PretrainedTextTokenizer.batch_encode` | `irodori_tts/tokenizer.py:81` | list[str] | ids `(B,256)` long ／ mask `(B,256)` bool | **なし**（`padding="max_length"` で 256 固定・`tokenizer.py:114`） |
| 1' | トークン化（caption） | 同上 | `inference_runtime.py:1211` | str | `(B,512)` | なし（512 固定） |
| 2 | 参照音声→潜在 | `DACVAECodec.encode_waveform` | `irodori_tts/codec.py:177` | wav `(B,1,T)` 48k | latent `(B,T_ref,32)` | T_ref 可変（≤120 s＝3000） |
| 2' | 参照潜在の patch 化 | `patchify_latent` / `patch_sequence_with_mask` | `codec.py:14` / `model.py:114` | `(B,T_ref,32)` | `(B,T_ref/4,128)` | 可変 |
| 3 | 条件符号化 | `TextToLatentRFDiT.encode_conditions` | `model.py:1720` | ids/mask/ref | text `(B,256,512)`・speaker `(B,S_spk+1,768)`・caption `(B,512,512)` | S_spk 可変 |
| 3a | └ テキスト骨格 | `PretrainedTextBackbone` (ModernBERT) | `model.py:675`（forward `:771`） | `(B,256)` | `(B,256,768)` | なし |
| 3b | └ 射影器（text/caption 各 1） | `PretrainedConditionProjector` | `model.py:803`（forward `:852`） | `(B,S,768)` | `(B,S,512)` | なし |
| 3c | └ 参照潜在 encoder | `ReferenceLatentEncoder`（8×`TextBlock`） | `model.py:880` | `(B,S_spk,128)` | `(B,S_spk,768)` | 可変 |
| 3d | └ 平均トークン前置 | `_prepend_masked_mean_token` | `model.py:1611` | `(B,S,768)` | `(B,S+1,768)` | 可変 |
| 4 | duration 予測 | `predict_duration_log_frames` → `DurationPredictor` | `model.py:2045` / `model.py:985`（forward `:1288`） | text_state `(B,256,512)`＋aux `(B,14)` | `log1p(frames)` `(B,)` | なし |
| 4' | └ 補助特徴 14 次元 | `build_duration_features` | `irodori_tts/duration.py:105` | str | `(B,14)` f32 | なし |
| 5 | 条件 KV 射影（1 回） | `build_context_kv_cache` | `model.py:2021` | 各 state | 12 層×6 テンソル | S_spk 可変 |
| 6 | **RF Euler サンプリング** | `sample_euler_rf_cfg` | `irodori_tts/rf.py:116`（ループ `:459`） | — | `x_t (B,S_lat,32)` | S_lat 可変 |
| 6a | └ 1 ステップ | `forward_with_encoded_conditions` → 12×`DiffusionBlock` | `model.py:1829` / `model.py:924` | `x_t (B,S_lat,32)`, `t (B,)` | `v (B,S_lat,32)` | B（CFG 倍）・S_lat |
| 7 | unpatch | `unpatchify_latent` | `codec.py:28` | `(B,S_lat,32)` | 同左（patch=1 なので恒等） | — |
| 8 | 末尾トリム点探索 | `find_flattening_point` | `inference_runtime.py:150` | `(T,32)` | int | Python for ループ |
| 9 | コーデック復号 | `DACVAECodec.decode_latent` → `DACVAE.decode` | `codec.py:256` / `dacvae/model/dacvae.py:724` | `(B,32,T)` | `(B,1,T*1920)` | T 可変 |
| 10 | 透かし | `SilentCipherWatermarker.encode_batch` | `irodori_tts/watermark.py:79` | wav 48k | wav 48k | — |

補足＝**音素化・G2P は存在しない**。テキストは正規化後そのまま Unigram トークナイザに入る。感情注釈は 55 個の絵文字（`duration.py:9-66` の `ALLOWED_ANNOTATION_EMOJIS`）で、duration 特徴の 1 次元にも使われる。

潜在の時間分解能＝DACVAE `encoder_rates [2,8,10,12]` の積＝**hop 1920**、`sample_rate 48000`（`weights.pth` の `metadata.kwargs` を pickle ヘッダから直接読み出して確認・後述 §5）。よって **25 フレーム/秒**。`max_latent_steps 750`＝30 秒。

## 2. Transformer の中身

| 項目 | 実装 | 檔:行 | 備考 |
|---|---|---|---|
| attention 実装 | **`F.scaled_dot_product_attention`**（自前 softmax は無い） | `model.py:193`（SelfAttention）、`:406`（JointAttention）、`:477`（AttentionPooling）、`:533`（CrossAttentionPooling） | `is_causal=False` 固定。全て非因果 |
| 位置符号 | **RoPE・複素数演算あり** | `model.py:30-41` | `precompute_freqs_cis` が `torch.complex(cos,sin)`（`:34`）、`apply_rotary_emb` が `view_as_complex`（`:39`）→ 複素乗算（`:40`）→ `view_as_real`（`:41`） |
| RoPE 適用範囲（DiT） | **half-RoPE＝ヘッドの半分だけ回す** | `model.py:247-250` `_apply_rotary_half`：`x.chunk(2, dim=-2)`（dim=-2 は head 軸）→ 前半のみ回転→ concat | README の "half-RoPE"（`README.md:44`）の実体 |
| RoPE 適用範囲（text/speaker encoder） | 全ヘッド | `model.py:181-182` | `SelfAttention` は `apply_rotary_emb` を素で使う |
| RoPE テーブル | 非永続 buffer にキャッシュ、**forward 内で長さ不足なら作り直す** | `model.py:652-661`（TextEncoder）、`:900-909`（ReferenceLatentEncoder）、`:1576-1608`（DiT） | `theta=10000.0` 既定。`if cache.device != device or cache.shape[0] < seq_len:` は**Python 条件分岐**（トレース時に焼き込まれる） |
| 正規化 | 自前 **RMSNorm**（fp32 に上げて計算） | `model.py:57-68` | `nn.LayerNorm` は使わない |
| Q/K 正規化 | RMSNorm を `(heads, head_dim)` 形で | `model.py:169-170` / `:243-244` | attention 前に q,k を正規化 |
| AdaLN | **LowRankAdaLN**（Echo 風・低ランク＋残差ゲート） | `model.py:72-111` | `cond_embed.chunk(3)` → shift/scale/gate を各々 down(rank192)→SiLU→up して**元に足し戻す**。gate は `tanh`（`:110`）。出力射影は zero-init |
| timestep 埋め込み | `get_timestep_embedding`（正弦・`1000.0*exp(...)` 係数） | `model.py:45-53` | dim=512 → `cond_module`（Linear-SiLU-Linear-SiLU-Linear）で `model_dim*3=3840` へ（`model.py:1555-1561`） |
| GQA | **無し**（MHA・q/k/v とも heads=20） | `model.py:228-230` | KV ヘッド削減は実装されていない |
| KV キャッシュ | **自己回帰用のものは無い**。あるのは**条件側 KV の事前射影**（サンプリング全ステップで再利用） | `model.py:252-310` `project_context_kv`、`model.py:2021` `build_context_kv_cache` | 1 層あたり `(k_text,v_text,k_spk,v_spk,k_cap,v_cap)` の 6 本 |
| mask の作り方 | bool の `(B,S)` を `mask[:, None, None, :]` にして SDPA の `attn_mask` へ | `model.py:184-186`、`:398-400` | joint 側は self/text/speaker/caption の mask を `torch.cat(dim=1)` して 1 本に（`model.py:396-399`）。K も V も同様に cat（`:394-395`） |
| einops 等 | **不使用**（`grep -rn "einops\|rearrange"` で 0 件） | — | 素の `reshape` / `transpose` / `chunk` のみ |
| ゲート付き出力 | attention 出力に `sigmoid(gate(x))` を掛ける | `model.py:201`、`:414` | Echo 由来 |
| MLP | SwiGLU（w1/w3 → SiLU 積 → w2） | `model.py:418-426` | hidden = `int(1280*2.875)=3680` |

DiT ブロック 1 層の流れ（`model.py:949-983`）＝`h,g = attention_adaln(x, cond)` → `x += g * JointAttention(h, …)` → `h,g = mlp_adaln(x, cond)` → `x += g * SwiGLU(h)`。

**joint attention の鍵長**＝self(S_lat) + text(256) + speaker(S_spk+1) + caption(512)。参照 120 秒なら S_spk=750 で鍵 1600 超に対しクエリは S_lat（5 秒なら 125）＝**非対称に鍵が長い**。KV 事前射影が効く理由。

## 3. サンプリングループと「1 ステップ export」の可否

### ループの位置と 1 ステップの中身

- ループ本体＝`upstream/Irodori-TTS/irodori_tts/rf.py:459` `for i in range(num_steps):` — **純 Python**。`@torch.inference_mode()`（`rf.py:115`）。
- ループ**外**で 1 回だけ行うもの＝条件符号化（`rf.py:220`）・uncond 側の zeros 生成（`rf.py:231-262`）・CFG 枝のバッチ連結（`rf.py:351-356`）・KV 射影（`rf.py:413-438`）・speaker KV スケール（`rf.py:439-456`）。
- ループ**内**＝(a) `tt = torch.full((B,), t)`（`:462`）、(b) CFG 判定（`:464`）、(c) `forward_with_encoded_conditions` 1〜2 回、(d) CFG 合成、(e) 任意の temporal rescale（`:547`）、(f) speaker KV の巻き戻し（`:556-580`）、(g) **Euler 更新 `x_t = x_t + v*(t_next - t)`（`rf.py:582`）**。

### CFG（既定＝independent）

`cfg_guidance_mode` 既定 `"independent"`（`rf.py:132`）。有効枝は `cfg_scale_* > 0` で決まる（`rf.py:264-316`）。既定値は text 3.0 / caption 3.0 / speaker 5.0（`rf.py:129-131`・`inference_runtime.py:224-226`・`infer.py:229-231`）＝**3 枝とも有効なら `cfg_batch_mult = 4`**（`rf.py:341`）。

- **バッチを 4 倍に連結**して 1 回で流す（`rf.py:467-479`）。text 枝は text_state だけ zeros・text_mask だけ zeros にし、speaker/caption は cond のまま（`rf.py:325-338`）＝**各条件の独立 CFG**。
- 合成式（`rf.py:480-483`）＝
  `v = v_cond + Σ_{name∈{text,speaker,caption}} s_name · (v_cond − v_{name-uncond})`
- 他モード＝`joint`（uncond 1 枝・全 scale 一致必須・`rf.py:496-516`／バッチ 2 倍）、`alternating`（ステップごとに 1 条件ずつ・`rf.py:517-531`／バッチ 2 倍）。
- **CFG 適用区間**＝`cfg_min_t <= t <= cfg_max_t`（既定 0.5〜1.0・`rf.py:133-134`）。**t は schedule から決まるので、どのステップで CFG が入るかは実行前に確定できる**（データ依存でない）。ただし CFG on/off でバッチ倍率と使う KV が切り替わる（`rf.py:465` vs `:534`）。

### t スケジュール（`rf.py:189-208`）

```
init_scale = 0.999
linear: u = linspace(0, 1, num_steps+1)
sway  : u = linspace(0, 1, num_steps+1);  u = u + sway_coeff * (cos(0.5*pi*u) + u - 1);  u = clamp(u, 0, 1)
t_schedule = (1 - u) * 0.999          # t は 0.999 → 0 へ単調減少
```
既定 `t_schedule_mode="linear"`・`sway_coeff=-1.0`（`rf.py:144-145`）。`num_steps` 既定 **40**（`rf.py:128`・`infer.py:173`）。sway は「負で雑音側（早いステップ）を密に」（`rf.py:197-198` の原文コメント）。README は sway＋`--num-steps 6` を高速推論例として挙げる（`README.md:275-290`）。
**厳密単調減少チェックが `torch.all(...).item()` で入る**（`rf.py:207`）＝スケジュール生成時のみ。C# 側で同じ式を再現すればよい。

### ノイズ生成

`rf.py:158-163`：
```
rng, rng_device = _make_rng(seed=seed, device=device)          # torch.Generator(device).manual_seed(seed)
x_t = torch.randn((B, sequence_length, latent_dim), device=rng_device, dtype=dtype, generator=rng)
```
`truncation_factor` 指定時は `x_t *= factor`（`rf.py:164-165`）。seed 未指定なら `secrets.randbits(63)`（`inference_runtime.py:1175`）。
**注意＝`torch.randn` の乱数列は device/backend 依存**。C# で「PyTorch と同じ seed → 同じ音」を再現するのは、CPU の Philox/MT 実装まで写さない限り不可能。**再現性を売りにするなら「seed は自前の RNG に置き換える」と割り切るのが安全**（音質には影響しない）。

### データ依存の制御流・早期停止

- ループ内に**早期停止は無い**。必ず `num_steps` 回まわる。
- ループ内でテンソル値に依存する Python 分岐は **`t.item()` の比較だけ**（`rf.py:464`, `:81`）。t はスケジュール由来＝**データ依存ではない**。
- 生成後の末尾トリム（`find_flattening_point`・`inference_runtime.py:150-184`）は**潜在値に依存する Python for ループ**。ただし**サンプリングの外**＝C# で素直に書ける。

### 判定：「1 ステップ関数を 1 回 export し、ループを外（C#）で回す」

**成立する。** 推奨の切り分けは以下。

| グラフ | 中身 | 呼ぶ回数 | 動的軸 |
|---|---|---|---|
| G1 `encode_conditions` | ModernBERT ×2 呼び（text/caption）＋射影器 ×2 ＋ ReferenceLatentEncoder ＋ 平均トークン前置 | 1 | S_spk |
| G2 `duration` | DurationPredictor | 1 | — |
| G3 `dit_step` | `forward_with_encoded_conditions`（12 ブロック） | `num_steps` | batch, S_lat |
| G4 `codec_decode` | `quantizer.out_proj` ＋ Decoder | 1 | T |
| G5 `codec_encode`（参照音声を wav で渡す場合のみ） | Encoder ＋ `quantizer.in_proj` の mean 側 | 参照 1 本につき 1 | T |

C# 側に残るのは＝t スケジュール生成・CFG 合成の 4 行・Euler 更新 1 行・末尾トリム・（必要なら）speaker KV スケール。**いずれも数十行の算術**。

**設計上の分岐点が 1 つ**＝G3 に条件側 KV を渡すか、state を渡すか。
- **KV を渡す**（`use_context_kv_cache=True` 相当・既定）＝12 層 × 6 本 ＝ **72 個の入出力テンソル**。参照 120 秒・CFG 4 倍だと speaker KV だけで数百 MB。ORT の IOBinding で常駐させれば毎ステップのコピーは不要。`speaker_kv_scale`（声の強さの実験的つまみ）を使うにはこちらが要る。
- **state を渡す**（`use_context_kv_cache=False`・`inference_runtime.py:234` の `context_kv_cache: bool = True` を False に）＝入力は text/speaker/caption の state と mask だけ（6 本）。毎ステップ `wk_*/wv_*` を再計算する分だけ遅いが、**移植は圧倒的に単純**。⒝の第一版はこちらを推す。

## 4. 条件付け

### caption（テキスト）と本文テキスト

- **同一の事前学習骨格を共有**。`text_encoder_type: pretrained`（`configs/train_v4_small.yaml:12`）→ `PretrainedTextBackbone` を 1 個だけ持ち（`model.py:1467-1472`）、text 用と caption 用に**別々の `PretrainedConditionProjector`** を掛ける（`model.py:1473-1481` と `:1504-1511`）。
- 骨格＝`sbintuitions/modernbert-ja-310m`、revision `77675fc96a7e445e982e2ba90246b816efc74ec6`（`configs/train_v4_small.yaml:10-11`）。チェックポイント同梱の `text_encoder_config_json` メタデータ実測＝`{"architectures":["ModernBertForMaskedLM"], "vocab_size":102400, "hidden_size":768, "intermediate_size":3072, "num_hidden_layers":25, "num_attention_heads":12, "max_position_embeddings":8192, "local_attention":128, "global_attn_every_n_layers":3, "rope_parameters":{"sliding_attention":{"rope_theta":10000.0},"full_attention":{"rope_theta":160000.0}}}`（HF キャッシュの `model.safetensors` ヘッダより逐語）。
- **骨格の重みはチェックポイントに同梱されている**（`load_pretrained_backbone_weights=not use_pretrained_text_encoder` ＝ pretrained なら **HF から落とさず state_dict から読む**・`inference_runtime.py:649-650`）。落とす必要があるのは**トークナイザだけ**で、それも checkpoint の `tokenizer/` サブディレクトリに同梱されている（HF キャッシュ実測：`tokenizer.json` 6.7 MB ＋ `tokenizer_config.json`）。→ **⒜⒝で HF 接続なしに動かせる**（ライセンス章の材料）。
- 射影器 `residual_mlp`＝`Linear(768→512)` ＋ `RMSNorm → Linear(768→1024) → SiLU → Linear(1024→512)` を足す（`model.py:836-850` と forward `:864-877`）。
- caption 空文字なら**マスクを全部落とす**（`inference_runtime.py:1215-1216` `caption_mask.zero_()`）＝caption 無条件。

### 参照音声の注入

**speaker embedding 1 本ではなく、prefix のトークン列**：
1. wav → `DACVAECodec.encode_waveform`（`codec.py:177`）→ `(B,T_ref,32)`。deterministic_encode なら VAE のサンプリングを飛ばして**平均だけ**使う（`codec.py:249-251`）。
2. `patch_sequence_with_mask(patch_size=4)`（`model.py:114` / 呼び `model.py:1802-1806`）→ `(B,T_ref/4,128)`。
3. `ReferenceLatentEncoder`（`model.py:880`）＝`Linear(128→768)` → **`x = x / 6.0`**（`model.py:913`・定数スケール）→ 8 層 `TextBlock`（全ヘッド RoPE）。
4. `RMSNorm`（`speaker_norm`）→ `_prepend_masked_mean_token` で**時間平均トークンを先頭に 1 個足す**（`model.py:1809`, 定義 `:1611-1625`）。
5. できた `(B,S_spk+1,768)` が joint attention の speaker context（`model.py:387-393`）。
- 参照長の上限＝チェックポイントメタデータ `"ref_max_seconds":120.0`。超過は前から詰めてトリム（`inference_runtime.py:966-972`）。複数 wav は**連結**（`inference_runtime.py:958-964`）。
- `no_ref=True` なら **zeros の潜在＋全 False の mask** を作る（`inference_runtime.py:872-887`）＝「参照なし」も同じ経路。

### speaker_inversion / voice design

- `SpeakerInversionEmbedding`（`irodori_tts/speaker_inversion.py:39`）＝`(num_tokens, speaker_dim=768)` の**学習可能パラメータそのもの**。forward は expand するだけ（`:91-97`）。
- 有効なとき `encode_conditions` は**参照 encoder を丸ごとバイパス**して、この埋め込みを speaker context にする（`model.py:1794-1800`）。`_prepend_masked_mean_token` も通さない。
- 外部から `.speaker.safetensors`（キー `speaker_embedding`）で渡せる（`speaker_inversion.py:11-12`, `inference_runtime.py:993` `_load_speaker_embedding_condition`）。
- **⒝にとっては朗報**＝speaker_inversion 経路なら G1 のうち ReferenceLatentEncoder と DACVAE encoder が要らない。「声」は 768 次元 × N トークンの小さな行列 1 枚＝**読み分けちゃんの声プリセットとして配りやすい**。
- `speaker_uncond_mode`＝`"mask"`（既定）と `"noise"`（`speaker_inversion.py:10`, `rf.py:127`, `:240-253`）。noise 側は `torch.randn` を使う＝再現性は seed 依存。
- voice design ＝ caption だけで声を作る使い方（`gradio_app_voicedesign.py`）。モデル構造は同一で、参照を `no_ref` にして caption を効かせる形。

## 5. v4-Small の諸元

`config_json`（`model.safetensors` メタデータ・**逐語**）と `configs/train_v4_small.yaml` は一致。

| 項目 | 値 | 出所 |
|---|---|---|
| latent_dim | **32** | yaml:7 ／ メタ `"latent_dim":32` |
| latent_patch_size | 1 | yaml:8 |
| model_dim | 1280 | yaml:17 |
| num_layers | 12 | yaml:18 |
| num_heads | 20（head_dim 64） | yaml:19 |
| mlp_ratio | 2.875（hidden 3680） | yaml:20 |
| text_dim / caption_dim | 512 / 512 | yaml:22, 34 |
| speaker_dim / layers / heads / patch | 768 / 8 / 12 / **4** | yaml:23-26 |
| timestep_embed_dim | 512 | yaml:27 |
| adaln_rank | 192 | yaml:28 |
| text_vocab_size | 102400（Unigram・実測 vocab 102400） | yaml:9 |
| duration_* | hidden 1024・layers 3・aux 14・arch `token_sum_dual_adarn_zero_no_aux` | yaml:36-45 |
| max_text_len / max_caption_len | 256 / 512 | メタ `config_json` |
| ref_max_seconds | 120.0 | メタ `config_json` |

**パラメータ数（safetensors ヘッダを直接読んだ実測・714 テンソル）**

| ブロック | パラメータ | 割合 |
|---|---:|---:|
| `blocks.*`（DiT 12 層） | 358,440,960 | 46.8 % |
| `pretrained_text_backbone.*`（ModernBERT-ja-310m） | 314,611,968 | 41.1 % |
| `speaker_encoder.*` | 60,506,880 | 7.9 % |
| `duration_predictor.*` | 21,783,809 | 2.8 % |
| `cond_module.*` | 7,208,960 | 0.9 % |
| `text_encoder.*`（射影器） | 1,706,752 | 0.2 % |
| `caption_encoder.*`（射影器） | 1,706,752 | 0.2 % |
| その他（in/out proj, norms） | 85,304 | — |
| **合計** | **766,052,385** | 100 % |

**dtype は全部 F32**（`{'F32': 766052385}`）。766,052,385 × 4 B ＋ ヘッダ 86,048 ＋ 8 ＝ **3,064,295,596 B**＝ファイルサイズと**バイト一致**。= 2.854 GiB。BRIEF の「モデル 2.86GB」は GiB 表記として整合。

**コーデック側**＝`Aratako/Semantic-DACVAE-Japanese-32dim` の `weights.pth` **429,620,065 B**。pickle の `metadata.kwargs` 実測＝`encoder_dim 64 / encoder_rates [2,8,10,12] / latent_dim 1024 / decoder_dim 1536 / decoder_rates [12,10,8,2] / n_codebooks 16 / codebook_size 1024 / codebook_dim 32 / quantizer_dropout False / sample_rate 48000`。→ hop 1920・25 フレーム/秒・**VAE ボトルネックの潜在次元 32**（モデル側 `latent_dim: 32` と一致）。

**⒝の配布物見積もりの材料**＝DiT ＋ 参照 encoder ＋ duration ＋ 射影器 ＝ 約 449 M パラメータ。ModernBERT 骨格 315 M は分離可能（別グラフ）。fp16 に落とせば合計 1.53 GB、INT8 なら約 0.77 GB。コーデックは fp32 で 0.43 GB（fp16 で 0.21 GB）。SilentCipher の 44.1k チェックポイントは HF キャッシュ実測で `enc_c.ckpt`/`dec_c.ckpt`/`dec_m_0.ckpt` の 3 本。

## 6. ONNX 化の段別判定

### 6-1 問題になりそうな演算の一覧（檔:行つき）

| # | 事象 | 檔:行 | 深刻度 | 対処 |
|---|---|---|---|---|
| A | **複素数 RoPE**：`torch.complex`／`view_as_complex`／複素乗算／`view_as_real` | `model.py:34`, `:39`, `:40`, `:41` | **高**（ONNX に複素型なし） | 実数版に書き換え：`x_even*cos − x_odd*sin`, `x_even*sin + x_odd*cos`。**数学的に等価・重み不変**。cos/sin テーブルはグラフ入力にするか、max_len ぶん定数で持って Slice |
| B | **RoPE キャッシュの forward 内更新**：`if cache.device != device or cache.shape[0] < seq_len:` → buffer 再代入 | `model.py:657-661`, `:905-909`, `:1604-1608` | 中 | export 前に十分長いテーブルを焼いておく（`_rope_freqs(max_len, device)` を 1 回呼ぶ）か、テーブルをグラフ入力にする |
| C | **`bool(has_any.all())` によるデータ依存分岐**（空 mask の救済） | `model.py:442`（`_safe_attention_mask`）。呼び元＝`model.py:1314`（DurationPredictor.forward）, `:471`, `:526` | 中 | 実運用では BOS が必ず立つので分岐は常に「全部有効」側。トレースでその枝が焼かれる。**export 時に mask が全 True でないケースを作らないこと**。安全側にするなら救済ロジックを `torch.where` に書き換え |
| D | **in-place / 論理索引代入**：`x[~has_any] = 0`, `mask[~has_any, 0] = True`, `mask[dropout_mask] = False` | `model.py:446-447`, `:1716-1717`, `:1743`, `:1774` | 低 | C と同じ枝。推論経路では `*_condition_dropout` は常に None（訓練専用）＝到達しない |
| E | **`mul_` による KV の in-place スケール** | `rf.py:111-112` | 低 | ループ外・C# 側で普通の乗算にすればよい |
| F | **動的形状の床除算 reshape**：`usable = (seq_len // patch) * patch` → reshape | `model.py:142-147`（`patch_sequence_with_mask`）, `codec.py:22-24` | 中 | 参照長は G1 の外（前処理）で 4 の倍数に切ってから渡せば消える |
| G | **`.item()`／Python スカラー化** | `rf.py:81`, `:207`, `:269`, `:464` | 低 | 全部ループ外か、スケジュール由来。C# へ移す |
| H | **`torch.compile` 依存** | `inference_runtime.py:260-278` | なし | 既定 False（`RuntimeKey.compile_model: bool = False`・`inference_runtime.py:197`）。ONNX 化時は無効のまま |
| I | **bf16 autocast** | `train.py:1943`, `:3799` のみ | なし | **推論経路に autocast は無い**。`model.py:860` の `torch.is_autocast_enabled` は「有効でなければ dtype を合わせる」防御コードで、推論では常に「有効でない」側 |
| J | **`weight_norm`（parametrization）** | `dacvae/nn/layers.py:14, 48-50, 54, 117` | 中 | export 前に `torch.nn.utils.remove_weight_norm` を全 Conv に適用。トレースなら定数畳み込みされる可能性もあるが、明示的に外すのが安全 |
| K | **forward 中のモジュール差し替え**：`self.pre[-1] = nn.Identity()` を try/finally で戻す | `dacvae/model/dacvae.py:333-338`（`forward_no_conv`） | 中 | export 前に差し替えを**確定**させた `nn.Module` を作って export（元に戻す `finally` を通さない） |
| L | **irodori 側の monkey patch**：`decoder.alpha = 0.0` ＋ `decoder.watermark = _watermark_passthrough` | `codec.py:83-96` | 中 | この patch 後の挙動を export 対象にする＝DACVAE 内蔵の透かし枝（LSTM 含む）は**復号経路から外れる** |
| M | **Snake 活性化**：`x + (alpha+1e-9).reciprocal() * sin(alpha*x)^2` と 3D reshape | `dacvae/nn/layers.py:21-23` | 低 | ONNX 標準演算のみ（Sin/Pow/Reciprocal/Add/Reshape）＝そのまま出る |
| N | **LSTM**（`LSTMBlock`） | `dacvae/model/dacvae.py:108-120` | なし（経路外） | encoder には無い（`Encoder` は Conv のみ・`dacvae.py:122-150`）。LSTM は `wm_model` の post/decoder_block のみ＝L により未到達 |
| O | **`torch.stft` / `torch.istft`** | `silentcipher/stft.py:22`, `:36` | **高** | ONNX に `STFT` はあるが **ISTFT に対応する演算子が無い**。ONNX 化不可 |
| P | **VAE のランダムサンプリング**：`torch.randn_like` | `dacvae/nn/bottleneck.py:40`（`_vae_sample`） | なし（経路外） | irodori は `deterministic_encode=True` 既定で mean だけ使う（`codec.py:249-251`）＝randn を通らない |
| Q | **`audiotools` によるラウドネス正規化**（参照音声の前処理） | `codec.py:153-174` | 中 | ONNX の外。C# で ITU-R BS.1770 相当を実装するか、正規化を切る（`normalize_db=None`） |
| R | **`torchaudio.functional.resample`** | `codec.py:201`（参照が 48k でない場合）, `silentcipher/server.py:292,347` | 低 | C# 側のリサンプラで代替 |
| S | **カスタム autograd／独自 C++ 演算子** | — | **なし**（`grep` で 0 件） | — |
| T | **ModernBERT の unpadding／sliding attention** | transformers 5.12 の `modeling_modernbert` | 中 | `attn_implementation="eager"` or `"sdpa"` で export するのが定石。sliding window mask は静的長 256/512 なので定数化できる |

### 6-2 段ごとの暫定判定

| 段 | 判定 | 理由 |
|---|---|---|
| 0 テキスト正規化 | **ONNX の外**（C# で書き直す） | 正規表現＋NFKC。74 行（`text_normalization.py`）＝移植は軽い |
| 1 トークナイザ | **ONNX の外**。移植コスト**中〜高** | Unigram（SentencePiece 系）・`byte_fallback: true`・Metaspace(`prepend_scheme: never`, `split: false`)・vocab 102400。`tokenizer.json` の Unigram を .NET でそのまま食えるかは**未確認**（`Microsoft.ML.Tokenizers` の対応範囲を要検証）。最悪は Viterbi 自前実装 |
| 2 DACVAE encoder（参照 wav→潜在） | **書き換えれば出せる** | J（weight_norm 除去）だけ。deterministic_encode で `in_proj` の mean 側だけ使う（`codec.py:249-251`）＝`chunk` の前半。**speaker_inversion で運用するならこの段自体が不要** |
| 3a ModernBERT 骨格 | **書き換えれば出せる**（T） | 標準 HF モデル。入力長が 256/512 固定＝静的形状で出せる。315 M |
| 3b 射影器 | **そのまま出せる** | Linear/RMSNorm/SiLU のみ |
| 3c 参照潜在 encoder | **書き換えれば出せる**（A, B, F） | RoPE 実数化＋長さの事前整形 |
| 4 duration 予測器 | **そのまま出せる**（C に注意） | Linear/SwiGLU/RMSNorm/softplus/log1p のみ（`model.py:1370-1375`）。`_safe_attention_mask` の分岐だけ確認 |
| 5 条件 KV 射影 | **そのまま出せる** | Linear ＋ reshape ＋ RMSNorm |
| 6a **DiT 1 ステップ** | **書き換えれば出せる**（A, B が必須） | それ以外は SDPA / Linear / RMSNorm / SiLU / tanh / sigmoid / chunk / cat。ONNX 標準の範囲 |
| 6 ループ・CFG・Euler | **C# 側**（そもそも出す必要が無い） | §3 の通り |
| 8 末尾トリム | **C# 側** | Python for ループ（`inference_runtime.py:177-183`）。標準偏差と平均の窓判定＝数行 |
| 9 DACVAE decoder | **書き換えれば出せる**（J, K, L） | Conv1d/ConvTranspose1d/Snake/ELU/Tanh。差し替えを固定してから export |
| 10 SilentCipher 透かし | **出せない**（O） | `istft` 相当の ONNX 演算子が無い。C# 実装するか、透かしを外す（外すと上流の設計意図から外れる＝**卓の判断事項**） |
| Q 参照の音量正規化 | **ONNX の外** | audiotools 依存。切ることも可能（`ref_normalize_db=None`） |

**⒝の「部分移植」の境界**（第一候補）＝
**ONNX に入れるのは 2〜9（RoPE 実数化・weight_norm 除去・デコーダ差し替え固定の 3 手当てのうえで）／C# に残すのは 0・1・6 のループ・8・10**。

## 7. 量子化（torchao）

- 実装＝`irodori_tts/quantization.py`。バックエンドは **torchao 固定**（`quantization.py:12` `QUANTIZATION_BACKEND = "torchao"`）、`torchao>=0.16,<0.17`（`quantization.py:50` の原文メッセージ／`pyproject.toml:33` など）。
- 種類（`quantization.py:14-18`）＝`int8_weight_only` / `int8_dynamic_activation_int8_weight` / `int4_weight_only`（group_size 既定 128・packing `tile_packed_to_4d`・`:19-21`, `:95-99`）/ `float8_weight_only` / `float8_dynamic_activation_float8_weight`。CLI 名は `int8-weight-only` 他（`:30-36`）。
- 対象＝`nn.Linear` のみ。`core` プロファイルは ModernBERT 層と 4 種 blocks の `.attention.`/`.attn.`/`.mlp.` だけ（`quantization.py:142-155`）、`all-linear` は全 Linear（`:158-159`）。**Conv は対象外＝DACVAE コーデックは量子化されない**。
- 保存＝`torchao.prototype.safetensors` で平坦化し、metadata キー `irodori_quantization_json` に JSON を書く（`quantization.py:10`, `:225-266`）。読むにも torchao が要る（`:41-52`）。

**ONNX へ持ち越せるか＝持ち越せない（そのままでは）。**
理由＝torchao の量子化テンソルは PyTorch のサブクラス表現で、ONNX の `QuantizeLinear/DequantizeLinear` 表現ではない。`int4` の `tile_packed_to_4d` は torchao 独自のパッキング。**⒝で量子化したいなら、fp32/fp16 で ONNX を出してから ONNX Runtime 側の量子化（`onnxruntime.quantization`・INT8 動的量子化 or MatMulNBits の INT4）を別途かける**のが筋。torchao の重みを直接持ち込む道は無い（**未確認だが、逆変換ツールは上流に存在しない**＝`grep` で ONNX 関連コードは 0 件）。
なお**上流に ONNX 出力の実装は一切ない**（`grep -rn "onnx" --include=*.py` で 0 件）＝⒝は完全に自前。

## 8. 精度（bf16 / fp32）

| 箇所 | 内容 | 檔:行 |
|---|---|---|
| 既定 | **fp32**（`RuntimeKey.model_precision = "fp32"` / `codec_precision = "fp32"`） | `inference_runtime.py:191`, `:193` ／ CLI 既定も fp32（`infer.py:100`, `:111`） |
| 選択肢 | `fp32` / `bf16` のみ。**fp16 は無い** | `resolve_runtime_dtype`（`inference_runtime.py:304-313`） |
| bf16 の制約 | **原文逐語**：`raise ValueError("precision='bf16' currently requires CUDA or XPU device.")` | `inference_runtime.py:310-311` |
| → ROCm / CPU | `device.type` が `cuda`/`xpu` 以外なら bf16 不可。**ROCm の torch は `device.type == "cuda"` を名乗る**ので実際は通る見込み（**未確認**＝実機検証していない） | `inference_runtime.py:308-311` |
| 重みの型変換 | `_move_inference_module` が**パラメータとバッファを直接 dtype 変換**（`param.data = param.data.to(...)`）。複素の RoPE バッファは `is_floating_point()` が False なので dtype 変換されず device 移動のみ | `inference_runtime.py:280-302` |
| autocast | **推論経路では使わない**。`torch.autocast` の出現は `train.py:1943` と `train.py:3799` だけ | — |
| fp32 に固定的に上げる箇所 | `RMSNorm`（`model.py:66` `x = x.float()`）、`LowRankAdaLN`（`model.py:106`）、`apply_rotary_emb`（`model.py:39` `x.float()`）、`softplus`（`model.py:1373`）、duration 出力（`model.py:2091` `pred.float()`） | 上記 |
| 出力を入力 dtype に戻す | `forward_with_encoded_conditions` 末尾 `x.to(dtype=x_t.dtype)` | `model.py:1883` |

**⒝への含意**＝ONNX に出すなら fp32 が素直。ORT の fp16 変換は後段で掛けられるが、上の「fp32 に上げる」箇所（RMSNorm・AdaLN・RoPE）は fp16 でも fp32 のまま残すのが安全（ORT の `float16 converter` の `op_block_list` で指定）。

## 9. 主席への注意（⒝で「部分移植」になる境界の候補）

1. **境界の第一候補**＝「ONNX に入れる＝コーデック encode/decode・条件符号化・duration・DiT ステップ／C# に残す＝正規化・トークナイズ・サンプリングループ・末尾トリム・透かし」。ループを外に出すのは**素直に成立する**（§3）。ここが⒝の最大の朗報。
2. **⒝を殺しかねない 2 点**＝(a) **SilentCipher の ISTFT**（§6-1 O）。透かしは上流の設計意図（`README.md:296` 原文：`Generated audio is passed through [SilentCipher](https://github.com/sony/silentcipher) watermarking automatically when the dependency and model files are available.`）で、外すか自前実装かは**卓の判断**。(b) **Unigram トークナイザの .NET 移植**（§6-2 段 1）。`tokenizer.json` の Unigram＋byte_fallback を .NET でそのまま読める保証は**取れていない（未確認）**＝⒝の見積もりでは 1 便を別枠で見ておくべき。
3. **RoPE 実数化は「等価な書き換え」で、重みは 1 バイトも変わらない**。数値差は fp32 なら丸め誤差程度。⒝の技術リスクとしては低く見てよい。ただし half-RoPE（ヘッドの半分だけ回す・`model.py:247-250`）を見落とすと**静かに音が壊れる**＝実装時の第一の落とし穴。
4. **speaker_inversion 経路を採ると⒝がかなり軽くなる**（DACVAE encoder と ReferenceLatentEncoder が丸ごと不要・§4）。「声」＝`(N, 768)` の小行列 1 枚。読み分けちゃんの「声の値」欄との相性を⒞の設計と合わせて検討する価値がある。
5. **テキスト/caption の系列長は 256/512 に固定パディングされる**（`tokenizer.py:114` `padding="max_length"`）＝ONNX の動的軸は**潜在長・参照長・バッチ（CFG 倍率）の 3 つだけ**。静的形状最適化が効きやすい。
6. **CFG バッチ倍率が推論中に変わる**（`cfg_min_t=0.5` を跨ぐと 4→1・`rf.py:464`）。バッチ軸を動的にしておけば 1 グラフで足りるが、KV を入力にする設計だと**cond 用と CFG 用で別の KV 束**が要る（`rf.py:413-423`）。ここは移植時の仕様バグの温床。
7. **ModernBERT 骨格 315 M はチェックポイントに同梱**（`inference_runtime.py:649-650`）＝⒜⒝とも **HF 接続なしで骨格を用意できる**。落とすのはトークナイザだけで、それも checkpoint 側の `tokenizer/` に入っている（HF キャッシュ実測）。ライセンス章（§4-1）で「同梱できるもの」を切り分けるとき、**ModernBERT-ja-310m の重みが Irodori の MIT チェックポイントに焼き込まれている**点は追加で確認が要る（sbintuitions のライセンスが Irodori の MIT 宣言に飲み込まれているのか）。**これは音響席の管掌外＝ライセンス席へ回す。**
8. **torchao の量子化は ONNX へ持ち越せない**（§7）。⒝で軽量化するなら ORT 側の量子化を別便で。⒜なら torchao 量子化がそのまま使える＝**配布物サイズの議論では⒜と⒝で使える手が違う**点を比較表に反映すること。
9. **上流に ONNX 関連コードは 1 行も無い**（`grep -rn "onnx"` 0 件）＝⒝は全て自前・上流追随のたびに再検証が要る。保守の軸で⒜⒞より明確に重い。
10. **seed 再現性**＝`torch.randn` のビット列は C# で再現できない（§3）。「同じ seed で同じ音」を読み分けちゃんの仕様に入れるなら、⒝では**自前 RNG に置き換える**と最初に決めておくこと。
