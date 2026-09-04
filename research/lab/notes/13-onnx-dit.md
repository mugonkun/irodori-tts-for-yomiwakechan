# 13 — 推論の芯（DiT 1 ステップ・duration・条件符号化）の ONNX 変換実験と ORT 全ループ照合

> 席＝第 2 便サブ席（opus）／担当＝BRIEF §4-2 の実証本丸。実施日 2026-09-04。CPU のみ・GPU 未使用。
> `upstream/**`・`yomiwakechan2/**`・`C:/IrodoriTTS/**`・`C:/irodori-TTS-server/**`・HF キャッシュは **1 バイトも変更していない**（§9 で確認）。
> 書いたのは `lab/bin/`・`lab/onnx/`・`lab/out/`・`lab/tmp/`・本ノートのみ。上流の書き換えは **すべて monkeypatch** で代替した。
> 引用の相対パスの起点は `C:\Users\mugonkun\source\repos\irodori-native-research\`。

## 要点（10 行以内）

1. **DiT 1 ステップは ONNX へ出た**。`torch.onnx.export(dynamo=True)` が **一発成功**（opset 20・2031 ノード・1.467 GB／external data）。動的軸は **B・S_lat・S_spk の 3 本とも実効**（S_spk=751 まで確認）。
2. **複素 RoPE の実数化（cos/sin）は torch 出力が bit 一致**（max diff **0.0**）。`irodori_tts.model.apply_rotary_emb` と `precompute_freqs_cis` の 2 関数を差し替えるだけ＝**⒝の技術リスクは実測でほぼ消えた**（03-arch §9-3 の見立てを上回る結果）。
3. **ORT CPU と torch の 1 ステップ差は max abs 2.1〜7.2e-6**（v の絶対値 4.3・rel L2 4e-7）。動的形状・別 t でも同水準。
4. **全 40 ステップを ORT で回した最終潜在の差は max abs 3.6e-5（rel L2 1.8e-6）、復号 wav の差は max abs 1.16e-5・相関 0.999999999997**（int16 に落とすと**最大 1 LSB**・10 ステップでも 2 LSB）。**⒝の数値的成立は実証された**。
5. **速い**。40 ステップ全ループ＝**torch 13.10 s → ORT 10.86 s（1.21×）**。1 ステップ（B=1）は torch 0.218 s → ORT 0.145 s（1.50×）。ORT は **KV キャッシュ無しでも torch の KV キャッシュ有りに並ぶ**（B=4：0.525 s vs 0.533 s）＝⒝第一版は「state 渡し」で十分。
6. **duration 予測器は bit 一致**（ORT と torch の差 **0.0**・87.4 MB）。ただし export には `_safe_attention_mask`（`model.py:440` の `if bool(has_any.all()):`）の**データ依存分岐の書き換えが必須**（逐語は §5）。
7. **条件符号化は丸ごと出た**。ModernBERT-ja-310m 骨格 ×2 ＋ 射影器 2 本 ＋ ReferenceLatentEncoder を **1 グラフ**で export 成功（`attn_implementation="sdpa"` のまま・1.527 GB・差 1.9e-6）。ORT 0.618 s vs torch 0.951 s（1.54×）。
8. **fp16 は CPU では罠**。サイズは半分（1.467 GB→0.732 GB）だが **ORT CPU で 9.2 倍遅い**（40 ステップ 10.65 s→97.96 s）。誤差は wav rel L2 9.2e-3。**RMSNorm/AdaLN/RoPE を fp32 に残しても誤差はほぼ同じ**（5.30e-3 vs 5.18e-3）＝op_block_list は効かない。
9. **C# に残る算術は実測でも「数十行」**＝t スケジュール 3 行・CFG 合成 3 行・Euler 更新 1 行・末尾トリム。写経した `lab/bin/40_loop.py` の**ループ本体は 35 行**。
10. **上流の無駄を 1 つ発見**＝`encode_conditions` は 1 合成につき **2 回**呼ばれる（`inference_runtime.py:1281` の duration 用 ＋ `rf.py:220` のサンプリング用）＝**ModernBERT が 4 回走る**。⒝なら 1 回に減らせる（torch 実測 0.951 s／回＝**1 合成あたり 0.95 s の削減余地**）。

---

## 1. 実験の条件（すべての数値の前提）

| 項目 | 値 |
|---|---|
| venv | `lab/.venv`（11-lab-setup.md §3 のまま。torch 2.10.0+cpu / onnxruntime 1.29.0 / onnx 1.22.0 / transformers 5.16.1） |
| 環境変数 | 11-lab-setup.md §10 の一式そのまま。`OMP_NUM_THREADS=8` / `MKL_NUM_THREADS=8` / `torch.set_num_threads(8)` / ORT `intra_op_num_threads=8, inter_op=1` |
| checkpoint | `Aratako/Irodori-TTS-v4.1-Small`（`InferenceRuntime.from_key`・cpu/fp32） |
| 本文 | `こんにちは、読み分けちゃんのテストです。` |
| caption | `落ち着いた女性の声で、丁寧に話す。` |
| 参照 | `no_ref`（＝`inference_runtime.py:872-887` の zeros 経路。S_spk=2） |
| duration | torch の予測値をそのまま使用＝`pred_frames 94.333` → **latent_steps 94**（＝3.76 s・11-lab-setup.md §5 の基準と一致） |
| 初期ノイズ | `torch.Generator(device="cpu").manual_seed(1234)` の `randn((1,94,32))`。torch 側・ORT 側とも**同じ x0** |
| CFG | independent・text 3.0 / caption 3.0 / **speaker 0.0**・`cfg_min_t 0.5`・`cfg_max_t 1.0`・t スケジュール linear |

**要注意の訂正（03-arch §3 への補足）**＝**`--no-ref` では CFG バッチ倍率は 4 ではなく 3**。
`inference_runtime.py:1130-1131` が `use_speaker_for_request = use_speaker_condition_resolved and not req.no_ref` を False にし、
`resolve_cfg_scales`（同 `:335-341`）が `cfg_scale_speaker` を **0.0 に落とす**ため、`rf.py:305-316` の `enabled_cfg_names` は `["text","caption"]` になる。
40 ステップのうち **CFG が入るのは t≥0.5 の 20 ステップ**（実測 `n_cfg_steps=20`／10 ステップなら 5）。参照音声を渡す運用ならバッチ 4・CFG も同様に半分。

**harness の妥当性確認**＝本便が組んだ経路で作った wav（`lab/out/13_steps40_torch.wav`）と、第 1 便の基準 wav（`lab/out/steps40_seed1234.wav`）の差は **max abs 0.0031・相関 0.99999524**。差の出所は**透かし SilentCipher と int16 量子化**（本便は透かしを掛けていない）。＝**基準と同じ音を再現できている**。

## 2. 再現コマンド

```bash
source C:/Users/mugonkun/source/repos/irodori-native-research/lab/bin/env.sh   # §10 の環境変数一式
"$PY" "$LAB/bin/10_rope_check.py"      # RoPE patch の等価性＋条件の作成（lab/tmp/cond_real.npz, x0.npz）
"$PY" "$LAB/bin/20_export_dit.py"      # DiT 1 ステップの export（lab/onnx/dit_step.onnx）
"$PY" "$LAB/bin/30_ort_check.py"       # ORT vs torch の 1 ステップ照合と所要
"$PY" "$LAB/bin/40_loop.py"            # 全ループ照合（40/10 ステップ・wav まで）
"$PY" "$LAB/bin/50_export_duration.py" # duration 予測器
"$PY" "$LAB/bin/60_export_cond.py"     # 条件符号化（ModernBERT 込み）と参照 encoder 単体
"$PY" "$LAB/bin/70_fp16.py"            # fp16 変換とサイズ
"$PY" "$LAB/bin/80_fp16_loop.py"       # fp16 の全ループ実害
"$PY" "$LAB/bin/85_fp16_block_fix.py"  # op_block_list 版の不具合回避と評価
"$PY" "$LAB/bin/90_dyn_spk.py"         # S_spk 動的軸の実効確認

# int16 での差（§6 の表）
"$PY" -c "import soundfile as sf,numpy as np
ia,_=sf.read(r'$LAB/out/13_steps40_torch.wav',dtype='int16'); ib,_=sf.read(r'$LAB/out/13_steps40_ort.wav',dtype='int16')
d=np.abs(ia.astype('i4')-ib.astype('i4')); print(int(d.max()), int((d>0).sum()), d.size)"
```

生データ＝`lab/out/13_*.json`（`13_rope_check` / `13_export_dit` / `13_ort_check` / `13_loop` / `13_duration` / `13_cond` / `13_fp16` / `13_fp16_loop` / `13_fp16_block` / `13_dyn_spk`）、
音＝`lab/out/13_steps{40,10}_{torch,ort}.wav` と `13_steps40_ort_fp16.wav`。共通ヘルパ＝`lab/bin/dit_common.py`。

## 3. upstream 無改変で当てた 3 つの monkeypatch

| # | 差し替え対象（upstream の檔:行） | 差し替え先（lab） | 必要な理由 | 数値影響 |
|---|---|---|---|---|
| P1 | `irodori_tts.model.precompute_freqs_cis`（`model.py:30-34`・`torch.complex`） | `dit_common.real_precompute_freqs_cis`＝`stack([cos, sin], -1)` で `(end, dim/2, 2)` の実数 | ONNX に複素型が無い | **0.0（bit 一致）** |
| P2 | `irodori_tts.model.apply_rotary_emb`（`model.py:37-42`・`view_as_complex`/複素乗算/`view_as_real`） | `dit_common.real_apply_rotary_emb`＝`x0*cos−x1*sin` / `x0*sin+x1*cos` | 同上 | **0.0（bit 一致）** |
| P3 | `irodori_tts.model._safe_attention_mask`（`model.py:429-449`・`if bool(has_any.all()):` が `model.py:440`） | `dit_common.export_safe_attention_mask`＝`x * has_any` と `logical_or(mask, logical_and(first_only, not(has_any)))` | `torch.export` がデータ依存分岐で落ちる（§5 に逐語） | **0.0（bit 一致）** |

- P1/P2 は `_rope_freqs`（`model.py:656-661` / `:904-909` / `:1603-1608`）がモジュール大域を引くので patch が効く。
  ただし**buffer `_freqs_cis_cache` を `torch.empty(0,0,complex64)` に戻してから焼き直す**必要がある（複素キャッシュが残っていると長さ判定で再構築されない）。
  焼く長さ＝DiT 1024（潜在 40 秒相当）・参照 encoder 4096。焼いた表は **ONNX の定数として埋まる**（`Cos`/`Sin` が各 1 個しか残らないのはそのため）。
- P3 は元の実装と**同値**（有効トークンが 1 つも無い行だけ x を 0 に、mask の先頭だけ True にする）。
  **`torch.where` で書くと ORT CPU が落ちる**＝`NotImplemented: Could not find an implementation for Where(16) node`（bool 入力の Where が未実装）。`Or`/`And`/`Not` と `Mul` で書き直して通した。
- **half-RoPE（`model.py:247-250` の `x.chunk(2, dim=-2)`）は patch の対象外**＝`_apply_rotary_half` が `apply_rotary_emb` を呼ぶだけなので、実数版に差し替えても自動的に正しい。03-arch §9-3 の「見落とすと静かに音が壊れる」落とし穴は、**この構造なら踏まない**。

## 4. DiT 1 ステップの export と照合（BRIEF §4-2 の本丸）

### 4-1 グラフの形（`lab/onnx/dit_step.onnx`）

`use_context_kv_cache=False` 相当＝**state を渡す形**（`rf.py:534-546` の非 CFG 分岐と同じ呼び方）。

| 入力 | 形 | 型 |
|---|---|---|
| `x_t` | (**B**, **S_lat**, 32) | float32 |
| `t` | (**B**,) | float32 |
| `text_state` | (**B**, 256, 512) | float32 |
| `text_mask` | (**B**, 256) | **bool** |
| `speaker_state` | (**B**, **S_spk**, 768) | float32 |
| `speaker_mask` | (**B**, **S_spk**) | **bool** |
| `caption_state` | (**B**, 512, 512) | float32 |
| `caption_mask` | (**B**, 512) | **bool** |
| 出力 `v` | (**B**, **S_lat**, 32) | float32 |

- opset **20**（domain ""）・**2031 ノード**・external data 1 本。export 所要 **29.6 s**。`dynamo=True` が `torch.export.export(..., strict=False)` で**一発成功**（strict へのフォールバックも不要）。
- 演算子ヒストグラム（上位）＝`Mul 441 / Add 339 / MatMul 338 / Reshape 192 / Concat 106 / ReduceMean 85 / Sqrt 85 / Reciprocal 85 / Transpose 60 / Unsqueeze 53 / Gather 50 / Slice 37 / Sigmoid 29 / Split 25 / Tanh 24 / Sub 24 / Shape 15 / Where 13 / Softmax 12 / IsNaN 12 / Gemm 3 / Cos 1 / Sin 1 / Expand 1`。
  - **`Attention` 演算子は使われない**＝SDPA は `MatMul + Softmax + Where + IsNaN` に**分解**される（opset 20 には `Attention` が無い。opset 23 なら融合を狙える＝**未確認**）。
  - `IsNaN` ×12 は SDPA の「全マスク行の NaN 救済」由来。ORT CPU では実行できている。
  - **複素型のノードは 1 つも無い**（P1/P2 が効いた証）。`ReduceMean/Sqrt/Reciprocal` が各 85 個＝RMSNorm（q_norm/k_norm/out_norm 等）と AdaLN の内訳。
- ORT のセッション初期化＝**1.26 s**（1.47 GB の読み込み込み）。

### 4-2 照合表（ORT CPU vs torch・実数 RoPE patch 後）

| 条件 | max abs | mean abs | rel L2 | 相関 |
|---|---|---|---|---|
| B=1・S_lat=94・t=0.999 | **2.62e-06** | 3.79e-07 | 4.50e-07 | 0.99999999999991 |
| B=4・S_lat=94・t=0.999 | **2.15e-06** | 3.56e-07 | 4.25e-07 | 0.99999999999991 |
| B=3・**S_lat=251**・t=0.31（動的軸） | **5.36e-06** | 6.40e-07 | 9.93e-07 | 0.99999999999953 |
| B=1・**S_spk=2**・t=0.7 | 4.59e-06 | 7.07e-07 | 1.38e-06 | 0.99999999999911 |
| B=1・**S_spk=64**・t=0.7 | 5.48e-06 | 7.59e-07 | 1.48e-06 | 0.99999999999894 |
| B=1・**S_spk=751**（参照 120 秒相当）・t=0.7 | **7.15e-06** | 1.01e-06 | 1.55e-06 | 0.99999999999885 |

v の絶対値の最大は 4.29・標準偏差 1.10。**差は fp32 の丸め誤差の範囲**。動的軸 3 本（B・S_lat・S_spk）は**すべて実効**。

### 4-3 所要（1 ステップ・OMP=8・中央値）

| 実装 | B=1 | B=3 | B=4 |
|---|---:|---:|---:|
| torch（state 渡し＝ONNX と同形） | 0.2175 s | — | 0.8037 s |
| torch（**KV キャッシュ有り**＝上流既定） | 0.1671 s | — | 0.5327 s |
| **ORT CPU fp32（state 渡し）** | **0.1416〜0.1453 s** | **0.3861 s** | **0.5253 s** |
| ORT CPU fp16 | 1.5458 s | 3.2958 s | — |

**読み取り（断定）**＝
- ORT は torch の**同形（state 渡し）に対して 1.50〜1.53 倍速い**。
- ORT の state 渡しは **torch の KV キャッシュ有りとほぼ同速**（B=4 で 0.525 vs 0.533）。
  ＝03-arch §3 が「移植は単純だが遅い」と見た **state 渡しの不利は、ORT の最適化が食い潰す**。⒝の第一版は **KV を入出力にしない設計（入力 6 本＋x_t/t）で足りる**。72 本の KV テンソルを IOBinding で回す設計は**要らない**。
- ただし `speaker_kv_scale`（声の強さの実験的つまみ・`rf.py:104-113`）を使いたい場合だけは KV 入力版が要る＝**⒝第一版では非対応と割り切る**のが素直。

## 5. 踏んだ石（逐語・檔:行つき）

### 5-1 `_safe_attention_mask` のデータ依存分岐（duration・AttentionPooling が該当）

`50_export_duration.py` の 1 回目（P3 なし）。

```
torch.onnx._internal.exporter._errors.TorchExportError: Failed to export the model with torch.export.
...
<class 'torch.fx.experimental.symbolic_shapes.GuardOnDataDependentSymNode'>: Could not guard on
data-dependent expression Eq(u0, 1) (unhinted: Eq(u0, 1)).  (Size-like symbols: none)
consider using data-dependent friendly APIs such as guard_or_false, guard_or_true and statically_known_true.
The following call raised this error:
  File "...\upstream\Irodori-TTS\irodori_tts\model.py", line 440, in _safe_attention_mask
    if bool(has_any.all()):
```

- **strict=False と strict=True の両方で落ちる**（exporter が自動で 2 回試して両方 ❌）。
- 呼び元は `model.py:471`（AttentionPooling）・`:527`（CrossAttentionPooling）・**`:1314`（DurationPredictor.forward）**。
  **DiT 1 ステップ（JointAttention）は通らない**＝§4 の export が patch 無しで成功したのはそのため。
- 03-arch §6-1 C の予告どおり。ただし**「トレースでその枝が焼かれる」ではなく、export そのものが落ちる**＝
  **書き換え必須**であって「注意して export すればよい」ではない。ここは 03-arch の見立ての訂正。

### 5-2 ORT CPU が bool の `Where` を持っていない

P3 を素直に `torch.where(has_any[:,None], mask, first_only)` で書いたとき、export は成功するが**セッション生成で落ちる**。

```
onnxruntime.capi.onnxruntime_pybind11_state.NotImplemented: [ONNXRuntimeError] : 9 : NOT_IMPLEMENTED :
Could not find an implementation for Where(16) node with name 'node_where_1'
```

回避＝`Or`/`And`/`Not`＋`Mul`（`dit_common.export_safe_attention_mask`）。**bool テンソルを分岐に使う書き換えでは ORT の kernel 有無を先に確かめること。**

### 5-3 `onnxruntime.transformers.float16` の同一 Cast 二重挿入

`op_block_list=["ReduceMean","Sqrt","Reciprocal","Cos","Sin","Softmax","Tanh"]` を指定して fp16 変換したとき。

```
onnxruntime.capi.onnxruntime_pybind11_state.Fail: [ONNXRuntimeError] : 1 : FAIL :
Load model from ...\dit_step_fp16_block.onnx failed:This is an invalid model.
Error: two nodes with same node name (mul_3_cast_to_fp32_node).
```

原因＝RoPE 表の `mul_3` が **`Cos` と `Sin` の 2 つのブロック対象**へ分岐するため、変換器が**同名・同入力・同出力の `Cast` を 2 回**挿入する。
ノード名だけ変えると今度は出力名が重複して `Duplicate definition of name (mul_3_cast_to_fp32)` になる。**正解は重複ノードを 1 つ削除する**（`85_fp16_block_fix.py`・削除 1 個で通る）。

### 5-4 落ちなかったもの（記録）

- `torch.onnx.export(..., dynamo=True)` は DiT・duration・条件符号化の **3 つとも strict=False で一発成功**。`dynamo=False` へのフォールバックは**一度も要らなかった**。
- `bool` テンソルを**グラフ入力**にするのは問題なし（`elem_type 9`）。
- `chunk`（`model.py:100` の `cond_embed.chunk(3,-1)`、`:247` の half-RoPE）は `Split` に落ちて問題なし。
- `patch_sequence_with_mask` の床除算 reshape（03-arch §6-1 F）は、**参照長を静的に渡す限り出る**。動的 `S_ref` は §7 の `speaker_encoder.onnx` で `Dim(min=2,max=1024)` として通った（patch 済みの `(B,S_ref/4,128)` を入力にする形＝patch 化はグラフ外）。
- ModernBERT の unpadding／sliding attention（03-arch §6-1 T）は **`attn_implementation="sdpa"` の既定のまま**通った（§7）。

## 6. 全ループ照合（写経した Euler ループ ＋ ORT ステップ）

`lab/bin/40_loop.py` の `ort_loop()`＝`rf.py:189-208`（t スケジュール）と `rf.py:459-582`（本体）の写経。
ステップ関数だけ ORT に差し替え、条件（text/speaker/caption state）は torch の `encode_conditions` の出力を使う。x0 は両者同一。

| num_steps | CFG 段数 | torch(KV有) | torch(KV無) | **ORT** | 最終潜在 max abs | 潜在 rel L2 | **wav max abs** | wav rel L2 | wav 相関 |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| **40** | 20 | 13.099 s | 16.732 s | **10.855 s** | **3.56e-05** | 1.84e-06 | **1.16e-05** | 2.37e-06 | **0.999999999997** |
| **10** | 5 | 4.037 s | 5.212 s | **2.754 s** | **5.47e-05** | 3.31e-06 | **4.47e-05** | 1.59e-05 | **0.9999999999** |

- 潜在の絶対値の最大は 4.39（40 steps）／4.31（10 steps）、wav の絶対値の最大は 0.711／0.719。
- **torch の KV キャッシュ有り／無しは bit 一致**（max abs **0.0**）＝上流の KV 事前射影は純粋な速度最適化で、数値を変えない。
- 40 ステップ全ループの ORT は torch(KV有) の **1.21 倍速**。1 ステップ単体の 1.50 倍に比べて縮むのは、
  ORT 側だけ **CFG 段で state 6 本（text 256×512・caption 512×512 ×3 枝）を毎ステップ食い直している**ため。
  ＝**KV 入力版を作れば全ループでもう一段速くなる余地がある（未実施）**。
- 復号は両者とも torch コーデック（`rt.codec.decode_latent`）。**wav の差 1.16e-5 は 16bit PCM の 1 LSB（3.05e-5）より小さい**。
- **int16（PCM_16）で書き出した実測**＝

  | num_steps | バイト一致 | int16 最大差 | 差のあるサンプル数 |
  |---:|---|---:|---|
  | 40 | いいえ | **1 LSB** | 1,044 / 180,480（0.58 %） |
  | 10 | いいえ | **2 LSB** | 6,341 / 180,480（3.5 %） |

  ＝**bit 一致はしないが、差は最下位ビット 1〜2 個**。読み分けちゃんの「同じ設定で同じ音」は**聴感上は成立し、sha256 一致では成立しない**。仕様に書くならこの区別が要る。

### 6-1 C# に残る算術（実測）

写経したループ本体は **35 行**。内訳＝

| 残るもの | 行数 | 出所 |
|---|---:|---|
| t スケジュール（linear） | 2 | `rf.py:189-206` |
| CFG バンドルの連結（state を 3 枝ぶん cat・**ループ外 1 回**） | 6 | `rf.py:325-356` |
| CFG 区間判定 `cfg_min_t <= t <= cfg_max_t` | 1 | `rf.py:464` |
| CFG 合成 `v = c0 + Σ s_i (c0 − c_i)` | 3 | `rf.py:480-483` |
| Euler 更新 `x += v*(t_next − t)` | 1 | `rf.py:582` |
| （グラフ外）末尾トリム `find_flattening_point` | 〜20 | `inference_runtime.py:150-184` |
| （グラフ外）duration の frames→latent_steps 変換（clamp と round） | 5 | `inference_runtime.py:1310-1316` |

**⒝の「部分移植」の境界は、実証しても 03-arch §9-1 の第一候補から動かなかった。**

## 7. duration 予測器と条件符号化

### 7-1 duration（`lab/onnx/duration.onnx`）

- 入力＝`text_state (B,256,512)` / `text_mask (B,256)` / `speaker_state (B,S_spk,768)` / `speaker_mask` / `caption_state (B,512,512)` / `caption_mask` / **`duration_features (B,14)`**（`build_duration_features` はグラフ外＝C# 側）/ `has_speaker (B,)` / `has_caption (B,)`。出力＝`log1p_frames (B,)`。
- **ORT と torch が bit 一致**（max abs **0.0**・`log1p_frames 4.557379245758057` → `frames 94.33330535888672` が両者完全一致）。
- 143 ノード・opset 20。演算子は `Mul/Add/Unsqueeze/MatMul/Cast/Gemm/Split/Sigmoid/ReduceMean/Sqrt/Reciprocal/Expand/Where/ReduceSum/Tanh/ReduceMax/And/Clip/Not/Or/Gather/Div/Squeeze/Softplus/Greater/Log` のみ＝**標準演算子だけ**。
- 所要＝**ORT 9.7 ms / torch 30.4 ms**（3.1×）。B=2 の動的軸も通り、B=1 と同値。
- **重要**＝第 1 便の段別時刻の `predict_duration 約 1.2 s`（11-lab-setup §5）は、**この 30 ms の頭ではなく、その前段の `encode_conditions`（ModernBERT ×2）が実体**。⒝で効かせるべきはそちら。

### 7-2 条件符号化（`lab/onnx/encode_conditions_sdpa.onnx`）

`encode_conditions` を**丸ごと 1 グラフ**で export（ModernBERT-ja-310m 骨格 ＋ text 射影器 ＋ caption 射影器 ＋ ReferenceLatentEncoder ＋ speaker_norm ＋ 平均トークン前置）。

| 項目 | 値 |
|---|---|
| 骨格クラス | `ModernBertModel`（transformers 5.16.1） |
| `attn_implementation` | **既定の `sdpa` のまま成功**（`set_attn_implementation("sdpa")` は OK・出力差 0.0）。`eager` は試す前に sdpa が通ったため**未実施** |
| export | `dynamo=True` で **36.4 s・成功**・3344 ノード |
| 入力 | `text_ids (B,256)` / `text_mask (B,256)` / `cap_ids (B,512)` / `cap_mask (B,512)` / `ref_latent (B,4,32)`（**S_ref は静的**） / `ref_mask (B,4)` |
| 出力 | `text_state (B,256,512)` / `speaker_state (B,2,768)` / `speaker_mask (B,2)` / `caption_state (B,512,512)` |
| ORT との差 | `text_state` **1.91e-06**（rel L2 5.6e-07）／`caption_state` **1.43e-06**（rel L2 3.2e-07）／`speaker_state`・`speaker_mask` は **0.0**（no_ref なので全ゼロ／全 False） |
| 所要 | **ORT 0.618 s / torch 0.951 s**（1.54×） |

- **参照 encoder 単体**（`lab/onnx/speaker_encoder.onnx`）も別に出した。入力＝`ref_patched (B,S_ref,128)` / `ref_mask (B,S_ref)`（**S_ref 動的・min 2 / max 1024**）、出力＝`speaker_state (B,S_ref+1,768)` / `speaker_mask`。ORT との差 **1.26e-05**（40 フレーム参照・rel L2 2.1e-06）。
  ＝**参照音声を使う運用でも、条件符号化を「骨格＋射影器（静的長）」と「参照 encoder（動的長）」の 2 グラフに割れる**。
- **speaker_inversion 経路**（03-arch §4）を採るなら、この 2 番目のグラフは丸ごと不要。**未実施**（`.speaker.safetensors` を持っていないため）。

## 8. サイズ表と fp16

### 8-1 本便が出した .onnx（external data 込み・実測バイト）

| グラフ | .onnx | .onnx.data | 合計 | 相当パラメータ数 |
|---|---:|---:|---:|---:|
| `dit_step.onnx`（fp32） | 2,854,376 | 1,464,401,920 | **1,467,256,296**（1.366 GiB） | 366,100,480 |
| `duration.onnx`（fp32） | 272,438 | 87,162,880 | **87,435,318**（83.4 MiB） | 21,790,720 |
| `encode_conditions_sdpa.onnx`（fp32） | 8,161,841 | 1,518,796,800 | **1,526,958,641**（1.422 GiB） | 379,699,200 |
| `speaker_encoder.onnx`（fp32・上に含まれる） | 1,425,295 | 243,765,248 | 245,190,543（233.8 MiB） | 60,941,312 |
| `dit_step_fp16.onnx` | 2,859,231 | 731,599,872 | **734,459,103**（700.4 MiB） | 同上（fp16） |
| `dit_step_fp16_block.onnx`（op_block_list 版・要修正） | 2,945,710 | 731,599,872 | 734,545,582 | 同上 |
| `dit_step_fp16_block_fix.onnx`（重複ノード除去後） | 2,947,317 | 731,599,872 | 734,547,189 | 同上 |

**⒝に要る 3 グラフの fp32 合計＝`dit_step` ＋ `duration` ＋ `encode_conditions` ＝ 3,081,650,255 B（2.870 GiB）。**
参照 checkpoint `model.safetensors` は 3,064,295,596 B（2.854 GiB）＝**+17.4 MB（+0.57 %）**。増分は RoPE の cos/sin 表を定数として焼いたぶんと ONNX のグラフ記述。
**「ONNX 化してもモデルの大きさは実質変わらない」**＝配布物の議論では torch 本体（431 MB）と dacvae 系依存が消えることが効く（11-lab-setup §7）。

※ `lab/onnx/` には**別席（コーデック／透かし担当）の成果**（`dacvae_*`・`sc_*`）も同居している。本便の管掌は上表の 7 本のみ。

### 8-2 fp16（`onnxruntime.transformers.float16.convert_float_to_float16`・`keep_io_types=True`）

| 版 | サイズ | 1 ステップ B=1 | 1 ステップ B=3 | fp32 との差（1 ステップ B=1） |
|---|---:|---:|---:|---|
| fp32 | 1,467,256,296 | 0.1416 s | 0.3861 s | — |
| fp16（block なし） | 734,459,103 | **1.5458 s** | **3.2958 s** | max abs 5.18e-03・rel L2 1.18e-03 |
| fp16（`ReduceMean/Sqrt/Reciprocal/Cos/Sin/Softmax/Tanh` を fp32 に残す） | 734,547,189 | 1.4026 s | — | max abs **5.30e-03**・rel L2 1.19e-03 |

**40 ステップ全ループの実害**（`80_fp16_loop.py`）＝

| 項目 | 値 |
|---|---|
| 所要 | fp32 **10.65 s** → fp16 **97.96 s**（**9.2 倍遅い**） |
| 最終潜在の差 | max abs 4.50e-02・rel L2 3.82e-03・相関 0.9999929 |
| 復号 wav の差 | max abs **4.55e-02**（wav 絶対値最大 0.711）・rel L2 **9.23e-03**・相関 0.9999575 |

**読み取り（断定）**＝
1. **ORT の CPUExecutionProvider に fp16 の演算 kernel は無い**（毎ノードで Cast して fp32 で計算する）＝**CPU で fp16 は使ってはならない**。サイズ半減の代償が 9 倍。
2. **`op_block_list` で RMSNorm/AdaLN/RoPE/Softmax を fp32 に残しても、誤差は改善しない**（5.18e-03 → 5.30e-03 と**むしろ微増**）。
   誤差の出所は**正規化系ではなく MatMul の重み側**＝ブロックリストは効かない。**03-arch §8 の「fp32 のまま残すのが安全」という助言は、この実測では支持されなかった。**
3. wav の rel L2 が 1 % 弱（0.92 %）＝40 ステップぶんの累積。**聴感の判定は未実施**。GPU EP（DirectML/CUDA）で fp16 を使うなら、この誤差水準を許容できるか**別途の聴取が要る**。
4. **INT8 動的量子化（`onnxruntime.quantization`）は未実施**。CPU なら fp16 より INT8 のほうが筋（ORT CPU に INT8 kernel はある）＝**次便の課題**。

## 9. 停止域の確認（無改変の証明）

```bash
git -C upstream/Irodori-TTS status --short --ignored          # -> 出力なし
git -C upstream/Irodori-TTS-Server status --short --ignored   # -> 出力なし
find upstream/Irodori-TTS -name "__pycache__" -not -path "*/.git/*" | wc -l   # -> 0
```

- `PYTHONDONTWRITEBYTECODE=1` を全コマンドに付けた＝`__pycache__` は 1 つも生成されていない。
- `HF_HUB_OFFLINE=1` で既存 snapshot のみ読んだ。追加ダウンロードなし（`ckpt path or config path does not exist! Downloading...` は silentcipher の誤解を招くメッセージで、実際はキャッシュ解決・11-lab-setup §9 と同じ）。
- 上流の書き換えは**すべて Python の monkeypatch**（§3）＝ファイルには触れていない。
- GPU 未使用（torch は CPU ビルド・ORT は CPUExecutionProvider のみ）。8088/7861 の起動・停止・HTTP 送信は一切していない。外部へ何も公開していない。
- 書いたのは `lab/bin/`（`dit_common.py`・`10_rope_check.py`・`20_export_dit.py`・`30_ort_check.py`・`40_loop.py`・`50_export_duration.py`・`60_export_cond.py`・`70_fp16.py`・`80_fp16_loop.py`・`85_fp16_block_fix.py`・`90_dyn_spk.py`・`env.sh` の 12 本）・`lab/onnx/`（本便ぶん 7 本＝§8-1）・`lab/out/13_*.json`（10 本）・`lab/out/13_*.wav`（5 本）・`lab/tmp/*.npz`（2 本）・本ノート。
  `lab/bin/` の他の檔（`tok_*`・`wm_*`・`export_{encoder,decoder,watermark}.py`・`server_contract.py` 等）と `lab/onnx/` の `dacvae_*`・`sc_*` は**別席の成果**＝本便は触れていない。

## 10. 主席への注意

1. **⒝の「部分移植」の境界は、実証しても動かなかった**。ONNX に入れるのは【条件符号化・duration・DiT ステップ】＝本便が 3 本とも出した。C# に残るのは【正規化・トークナイズ・t スケジュール・CFG 合成・Euler 更新・末尾トリム・（透かし）】。**ループ本体は 35 行**で写経できた＝⒝の「制御が Python に取られている」懸念は**実測で消えた**。
2. **⒝は「動くか」ではなく「どこまで速くするか」の問題に降りた**。40 ステップ全ループで **torch 13.1 s → ORT 10.9 s（1.21×）**、wav の差は 16bit の 1 LSB 未満。
   第 1 便の基準（`total_to_decode 15.262 s`・11-lab-setup §5）の `sample_rf` だけを差し替えると **13.27 s ＝ RTF 4.06 → 3.53**。
   条件符号化の 1 回化（下記 8）と duration の ONNX 化を足しても **RTF 3.2 前後**。⒝単体では**リアルタイムに届かない**＝CPU 前提なら「先読みして貯める」設計は⒜⒝⒞のどれでも要る。
3. **C# 側に残る算術の量は「数十行」で確定**（§6-1 の表）。ただし**⒝の真の重量物は算術ではなく、Unigram トークナイザ（03-arch §9-2b）とコーデック復号・透かし**（別席の管掌）。本便の範囲では**新しい阻害要因は 1 つも出なかった**。
4. **再現性（seed）**＝本便は「x0 を外から与える」形で照合した＝**ONNX グラフの中に乱数は 1 つも無い**（`randn` はすべてループ外）。よって⒝で seed の扱いは**完全に C# 側の自由**＝03-arch §9-10 の「自前 RNG に置き換える」は**設計上ノーコストで実行できる**。「同じ seed で同じ音」は **C# 内でだけ約束する**（Python 版とのビット一致は約束しない）と決めれば済む。
   なお **同じ x0 を与えても ORT と torch の wav は sha256 一致しない**（int16 で 1〜2 LSB ずれる・§6）＝仕様に「バイト一致」を書いてはならない。
5. **踏んだ石は 3 つだけ**（§5）＝(a) `model.py:440` の `if bool(has_any.all()):`（duration の export を**確実に殺す**・書き換え必須）、(b) ORT CPU に bool の `Where` が無い、(c) ORT の fp16 変換器が同名 Cast を二重挿入する。**いずれも回避策が確立済み**で、⒝の見積もりに追加の便は要らない。
6. **03-arch への訂正 2 件**＝(i) `--no-ref` の CFG バッチ倍率は **3**（4 ではない・§1）。(ii) `_safe_attention_mask` は「トレースで枝が焼かれる」のではなく **export が落ちる**（§5-1）。
7. **fp16 は CPU では禁じ手**（9.2 倍遅い・§8-2）。配布物サイズの議論で「fp16 で半分」を使うなら、**GPU EP 前提であることを明記**すること。CPU の軽量化は INT8（**未実施**）へ。
8. **上流の無駄を 1 つ発見**＝`encode_conditions` が 1 合成につき 2 回走る（`inference_runtime.py:1281` の duration 用と `rf.py:220` のサンプリング用）＝ModernBERT が 4 回。
   torch 実測で **1 回 0.951 s ＝ 2 回で 1.90 s、1 回化で 0.95 s 削れる**（基準 15.26 s の 6.2 %）。⒝／ORT なら 1 回 0.618 s。
   **⒞専用制御方式では、フォークせずに直せるかは疑問**（`sample_euler_rf_cfg` が内部で呼ぶため）＝**⒝が⒞より速い理由の 1 つ**として比較表に足せる。
9. **KV 入力版の ONNX は作っていない（未実施）**。全ループの伸びしろ（§6）と `speaker_kv_scale` 対応がここに残る。ただし**第一版には不要**と判定した。
10. **未実施の札**＝(a) INT8 動的量子化、(b) opset 23 の `Attention` 融合、(c) `attn_implementation="eager"` での ModernBERT export（sdpa が通ったので不要）、(d) speaker_inversion 経路の export、(e) fp16 音声の聴取判定、(f) 参照 wav を実際に渡した end-to-end 照合（`--ref-wav` は torchcodec の件で別途の手当てが要る・11-lab-setup §10 注意 1）。
