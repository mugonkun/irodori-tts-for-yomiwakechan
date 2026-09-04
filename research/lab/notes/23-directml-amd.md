# 23 — ⒝（ONNX Runtime）を DirectML で GPU 実測（AMD Radeon 8060S）

> 席＝第 3 便サブ席（opus）／担当＝BRIEF §4-6 の「レイテンシ（RTF）」軸で⒝に欠けていた GPU 数値。実施 2026-09-04。
> 司令官の許可（同日）＝配信なし・PC 不使用・GPU 自由。**稼働機のサーバは起動していない**（8088/7861 は最後まで LISTEN なし）。
> 書いたのは `lab/bin/23_*.py`・`lab/bin/23_gpu_counters.ps1`・`lab/bin/40_loop_dml.py`・`lab/onnx/*_az0.onnx`・`lab/onnx/*_dml.onnx`・`lab/out/23_*`・本ノートのみ。
> `upstream/**`・`C:/irodori-TTS-server/**`・`C:/IrodoriTTS/**`・HF キャッシュは **0 檔も変わっていない**（§8 で証明）。相対パスの起点は `C:\Users\mugonkun\source\repos\irodori-native-research\`。

## 要点（10 行以内）

1. **⒝は DirectML で GPU に載る。端から端まで 2.815 s＝RTF 0.749**（短文 3.76 s・40 steps・fp32・常駐ウォーム）。ORT CPU 12.3 s（RTF 3.27）の **4.4 倍速**、CPU torch 15.26 s（RTF 4.06）の **5.4 倍速**。ただし **ROCm torch bf16 の 0.749 s（RTF 0.197）には 3.8 倍及ばない**。10 steps なら 1.036 s（RTF 0.276）。
2. **fp16 は DirectML で「壊れる」＝速度以前の問題**。`dit_step_fp16` を DML で走らせると v の absmax が **4.292 → 0.0721**（std 1.100→0.0308・NaN 無し）＝**別物の無音に近い出力**。40 steps の wav は torch 基準と **相関 0.161・rel L2 1.03**。`op_block_list` 版でも同一値で直らない。**⒝の本命は fp32 で確定**。
3. **fp32 の数値は文句なし**＝40 steps の最終潜在 max abs **5.29e-05**（rel L2 2.6e-06）、wav **2.31e-05**、**int16 で最大 1 LSB**（差のあるサンプル 1,410/180,480＝0.78 %）。13 §6 の ORT CPU（1 LSB・1,044 サンプル）と同水準＝**GPU にしても音は変わらない**。
4. **本便最大の発見①＝session を形状ごとに分けると DiT ループが 1.9 倍速くなる**。1 session だと B=3 を先に走らせた後の B=1 が **39.7→142.4 ms（3.6 倍）に劣化して戻らない**。session を 2 本に割ると 40 steps が **4.490 s → 2.388 s**。
5. **本便最大の発見②＝`Reshape` の `allowzero=1` が DML を殺す**。torch dynamo exporter が撒くこの属性を剥がすだけで、`encode_conditions_sdpa` は **セッション生成失敗（全 CPU 落ち）→ DML 209.5 ms**（CPU EP 570 ms）に、`speaker_encoder` は run 失敗→成功に変わる。剥がしは 10 行（`lab/bin/23_allowzero.py`）。
6. **本便最大の発見③＝DirectML は 1 次元 `ConvTranspose` を受けない**（最小再現あり）。DACVAE デコーダを `(N,C,L)→(N,C,1,L)` の 2 次元に挟み替える手術（`lab/bin/23_conv1d_to_2d.py`・4 ノード）で **DML 178.1 ms**（CPU EP 1101.8 ms・torch CPU 778 ms）。fp16 版は 127.7 ms。
7. **透かしグラフは onnxruntime-directml 1.24.4 で「ロードすらできない」**＝逐語 `Node (node_DFT_342) Op (DFT) [ShapeInferenceError] is_onesided and inverse attributes cannot be enabled at the same time`。同じ檔が CPU 版 **1.29.0 では通る**＝**版差の壁**。部品（STFT 0.42 + enc_c 8.56 + dec_c 28.34 ms）は DML で動くので、iSTFT を DFT 以外で組み直せば ~37 ms で載る（**未組立**）。
8. **17 (g) の 3 つの棘の実測**＝(1) memory pattern / parallel を切らなくても **1.24.4 はエラーを返さない**（4 通りとも同値・同速）＝docs の「must be disabled」は未強制。(2) **多重 Run は同一 session でも別 session でも壊れる**（6 走中 5 走で両スレッド例外・3 走で **segfault**）＝docs の「別 session なら同時 Run 可」は本機で成立しない。(3) opset 20 は全グラフが 20 なので上限に当たらない。
9. **DML のコールド費用は ROCm よりはるかに軽い**＝初回ステップ 218 ms（B=3）／175 ms（B=1）、初回ループ 2.647 s→ウォーム 2.388 s（**1.11 倍**）。ROCm の「初見形状で 3〜12 倍」（20 §2.2・§3.8）は DML では**観測されなかった**。
10. **GPU に載っている物証**＝負荷中に LUID `0x11ec8` の 3D エンジン **15.0〜55.7 %**、Dedicated Usage **2594.7→4225.7 MB（+1631 MB＝1.47 GB の dit_step 相当）**、終了後に復帰。他 2 アダプタは不動。

---

## 1. 環境と再現コマンド

| 項目 | 値 |
|---|---|
| GPU | AMD Radeon(TM) 8060S Graphics（gfx1151・ドライバ 32.0.31041.1004・20 §5.3 と同じ） |
| venv | `lab/.venv-dml`（**onnxruntime-directml 1.24.4** / numpy 2.5.2・20 §5.1 の構築のまま） |
| 照合用 venv | `lab/.venv`（torch 2.10.0+cpu / onnxruntime **1.29.0**・11 §3） |
| providers | `['DmlExecutionProvider','CPUExecutionProvider']`・`ort.get_device()='CPU-DML'` |
| session option | **`enable_mem_pattern=False` ＋ `execution_mode=ORT_SEQUENTIAL`**（ORT docs 準拠・§7 で実測すると 1.24.4 では強制されていない） |
| CPU EP 側 | `intra_op_num_threads=8 / inter_op=1`（11 §10・13 と同条件） |
| 条件 | 13 §1 と同一＝本文『こんにちは、読み分けちゃんのテストです。』／caption『落ち着いた女性の声で、丁寧に話す。』／`no_ref`／seed 1234／`latent_steps 94`（音声 3.76 s）／CFG independent text 3.0・caption 3.0・speaker 0.0・`cfg_min_t 0.5` |
| 入力 | `lab/tmp/cond_real.npz`・`lab/tmp/x0.npz`（13 §1 が作ったもの。**torch は照合側でしか使わない**） |

```bash
export LAB=C:/Users/mugonkun/source/repos/irodori-native-research/lab
export PYDML="$LAB/.venv-dml/Scripts/python.exe"          # onnxruntime-directml 1.24.4（torch 無し）
export PY="$LAB/.venv/Scripts/python.exe"                 # torch CPU + onnx + onnxruntime 1.29.0
export PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1 PYTHONIOENCODING=utf-8 PYTHONUTF8=1

# 段1 全グラフのベンチ（DML と CPU EP を同一プロセスで取る＝JSON に両方入る）
"$PYDML" "$LAB/bin/23_bench.py" --ep dml --group small  --json-out "$LAB/out/23_bench_dml_small.json"  --profile
"$PYDML" "$LAB/bin/23_bench.py" --ep dml --group dacvae --json-out "$LAB/out/23_bench_dml_dacvae.json" --profile
"$PYDML" "$LAB/bin/23_bench.py" --ep dml --group dit    --json-out "$LAB/out/23_bench_dml_dit.json"    --profile
"$PYDML" "$LAB/bin/23_bench.py" --ep dml --group cond   --json-out "$LAB/out/23_bench_dml_cond.json"   --profile
"$PYDML" "$LAB/bin/23_fallback_ops.py" dit_step.onnx    # CPU に落ちるノードの op 別内訳

# 落ちるグラフの逐語（ORT の C++ 例外が cp932 混じりで pybind の utf-8 デコードに失敗するため stderr を utf-16 で読む）
"$PYDML" "$LAB/bin/23_dml_err.py" dacvae_decoder_fp32.onnx 2> "$LAB/out/23_err_dacvae.bin"

# 回避策の 2 つの手術（どちらも lab/onnx に新しい .onnx を書くだけ・.onnx.data は共用）
"$PY" "$LAB/bin/23_allowzero.py" make                    # allowzero の最小再現モデル
"$PY" "$LAB/bin/23_allowzero.py" strip encode_conditions_sdpa.onnx dacvae_decoder_fp32.onnx dacvae_decoder_fp16.onnx speaker_encoder.onnx
"$PY" "$LAB/bin/23_conv1d_to_2d.py" dacvae_decoder_fp32_az0.onnx dacvae_decoder_fp32_dml.onnx
"$PY" "$LAB/bin/23_conv1d_to_2d.py" dacvae_decoder_fp16_az0.onnx dacvae_decoder_fp16_dml.onnx
"$PYDML" "$LAB/bin/23_probe_allowzero_min.py"; "$PYDML" "$LAB/bin/23_probe_convtranspose_min.py"
"$PYDML" "$LAB/bin/23_probe_cond.py"; "$PYDML" "$LAB/bin/23_probe_decoder.py"; "$PYDML" "$LAB/bin/23_probe_wm.py"

# 段2 DiT 全ループ（純 numpy + ORT・torch 不使用）
for m in "dit_step.onnx 40 dml_fp32_s40" "dit_step.onnx 10 dml_fp32_s10" \
         "dit_step_fp16.onnx 40 dml_fp16_s40" "dit_step_fp16.onnx 10 dml_fp16_s10"; do set -- $m
  "$PYDML" "$LAB/bin/40_loop_dml.py" --ep dml --model $1 --steps $2 --tag $3               # session 1 本
  "$PYDML" "$LAB/bin/40_loop_dml.py" --ep dml --model $1 --steps $2 --tag ${3}_2sess --sessions 2
done
"$PYDML" "$LAB/bin/40_loop_dml.py" --ep cpu --model dit_step.onnx --steps 40 --tag cpu_fp32_s40 --reps 1
"$PYDML" "$LAB/bin/23_shape_switch.py" dml dit_step.onnx          # 形状切替の罰
"$PYDML" "$LAB/bin/23_probe_fp16.py"; "$PYDML" "$LAB/bin/23_probe_fp16_block.py"

# 段2 の照合（torch 側・lab/.venv）
PYTHONPATH=C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS \
  OMP_NUM_THREADS=8 MKL_NUM_THREADS=8 "$PY" "$LAB/bin/23_decode_check.py"

# 段5 GPU カウンタ／棘
powershell -NoProfile -ExecutionPolicy Bypass -File "$LAB/bin/23_gpu_counters.ps1" \
  -OutJson "$LAB/out/23_gpu_counters.json" -Reps 12
"$PYDML" "$LAB/bin/23_dml_thorns.py"
for m in serial same two; do "$PYDML" "$LAB/bin/23_dml_run2.py" $m; done   # ← two は segfault する
"$PYDML" "$LAB/bin/23_e2e.py"                                             # 端から端までの合計
```

生データ＝`lab/out/23_bench_dml_{small,dacvae,dit,cond}.json`・`23_loop_*.json`・`23_latent_*.npz`・`23_decode_check.json`・`23_decoder_dml.json`・`23_watermark_dml.json`・`23_shape_switch_dml_dit_step.json`・`23_dml_thorns.json`・`23_fallback_dit_step.json`・`23_gpu_counters.json`・`23_e2e.json`・`23_err_*.bin`。
音＝`lab/out/23_torch_s{40,10}.wav`（本便が焼いた torch 基準）と `23_dml_fp{32,16}_s{40,10}[_2sess].wav`（8 檔）。

## 2. 段1＝13 グラフのベンチ（DML EP vs CPU EP・同一プロセス・同一 ORT 1.24.4）

40 回の中央値。`max abs diff` は同じ入力での CPU EP 出力との差。入力形状は主席の `lab/onnx/*.inputs.json` のまま（＝実合成と同形）。

| グラフ | 入力 | **DML 中央値** | DML 初回 | CPU EP 中央値 | **max abs diff** | 判定 |
|---|---|---:|---:|---:|---|---|
| `duration` | B=1 | **2.150 ms** | 241.98 | 7.122 | **0.0** | ○ 3.3 倍速・bit 一致 |
| `dit_step`（fp32） | B=3 | **80.588 ms** | 220.91 | 379.204 | **1.43e-05** | ○ **4.7 倍速** |
| `dit_step_fp16` | B=3 | 46.690 ms | 400.95 | 402.591 | **6.20**（出力 absmax 6.23） | ✕ **数値が壊れる**（§4） |
| `encode_conditions_sdpa` | B=1 | — | — | 575.787 | — | ✕ **session 生成で落ち CPU 全落ち** |
| `encode_conditions_sdpa_az0`（手術後） | B=1 | **209.5 ms** | 271.3 | 588.6（無手術は 570.1） | **2.86e-06** | ○ **2.8 倍速** |
| `dacvae_decoder_fp32` | (1,32,94) | — | — | 1108.53 | — | ✕ run で落ちる（`Reshape`） |
| `dacvae_decoder_fp32_dml`（手術後） | 同 | **178.1 ms** | 286.5 | 944.3 | **1.22e-06**（torch fp32 比） | ○ **6.2 倍速** |
| `dacvae_decoder_fp16` | 同 | — | — | 1113.27 | — | ✕ 同上 |
| `dacvae_decoder_fp16_dml`（手術後） | 同 | **127.7 ms** | 230.7 | 953.5 | 4.37e-03（波形 absmax 0.732） | ○ 誤差 0.6 % |
| `dacvae_encoder_fp32` / `fp16` | (1,1,180480) | — | — | 525.38 / 525.88 | — | ✕ run で落ちる（同じ `Reshape`） |
| `speaker_encoder` | S_ref=10 | — | — | 6.903 | — | ✕ run で落ちる（`node_view_5`） |
| `speaker_encoder_az0`（手術後） | 同 | **7.0 ms** | 101.8 | 7.0 | absmax 一致 3.99156（厳密差**未計測**） | △ 動くが速くならない |
| `sc_core_fp32` | (1,1,2049,83) | **31.589 ms** | 396.87 | 262.925 | 1.98e-05（absmax 4.42） | ○ **8.3 倍速** |
| `sc_enc_c_fp32` | 同 | **8.561 ms** | — | 26.896 | 5.52e-04（absmax 15.06） | ○ 3.1 倍速 |
| `sc_dec_c_fp32` | (1,96,2049,83) | **28.336 ms** | — | 224.424 | 2.24e-05（absmax 0.083） | ○ **7.9 倍速** |
| `sc_stft` | (1,167936) | **0.427 ms** | 53.96 | 8.521 | mag 7.63e-05／phase は 2π 巻き戻り（cos/sin で 3.64e-04） | ○ 20 倍速 |
| `sc_istft_slim` | — | — | — | — | — | ✕ **ロード不可**（§5） |
| `silentcipher_watermark_fp32` | — | — | — | — | — | ✕ **ロード不可**（§5） |
| `sc_istft_manual`（134 MB・参考） | — | — | — | 14.347 | — | △ ロードは通るが DML で run 落ち |

- **`providers_used` の fallback**＝`encode_conditions_sdpa`（無手術）だけが `['CPUExecutionProvider']` に丸ごと落ちた。それ以外は `['DmlExecutionProvider','CPUExecutionProvider']`。
- **部分 fallback の内訳**（`23_fallback_dit_step.json`・profiling 実測）＝`dit_step` は 1 回あたり **80 ノードが CPU EP**（`Slice 36 / Concat 32 / Gather 12`＝すべて形状計算の int64）で、費やす時間は **1.11 ms（80.6 ms の 1.4 %）**。`duration` は `Concat` 1 ノードだけ。**演算子が受け付けられずに落ちるという意味の fallback は無い。**
- DML 側で重い op（profiling・2 回分の合計 µs）＝`MatMul 67,160 / Mul 38,037 / Add 30,629 / Concat 9,826 / ReduceMean 8,538 / Transpose 6,836 / Reshape 6,478 / Sqrt 6,021 / QuickGelu 5,992`。**ORT が SiLU を `QuickGelu` に融合している**（CPU EP と同じ融合が DML でも効く）。
- **`Attention` 演算子は使われない**（13 §4-1 と同じ）。opset 20 なので DML の上限（17 (g)）に当たっていない。

### 2-1 DML が受けない演算子・属性（逐語）

`onnxruntime` の C++ 例外文字列に cp932 の Windows エラーメッセージが混ざるため、Python 側には
`UnicodeDecodeError: 'utf-8' codec can't decode byte 0x83 in position 345: invalid start byte` としてしか出てこない。
**逐語は stderr のログ（UTF-16LE）を読み直して採った**（`lab/bin/23_dml_err.py`）。

```
[E:onnxruntime:, sequential_executor.cc:572 onnxruntime::ExecuteKernel] Non-zero status code returned while
running Reshape node. Name:'node_view' Status Message:
E:\_work\1\s\onnxruntime\core\providers\dml\DmlExecutionProvider\src\MLOperatorAuthorImpl.cpp(2597)
\onnxruntime_pybind11_state.pyd!00007FF8273ED0BB: (caller: 00007FF827C97571) Exception(2) tid(9868)
8007023E {アプリケーション エラー}
```

```
[E:onnxruntime:, sequential_executor.cc:572 onnxruntime::ExecuteKernel] Non-zero status code returned while
running Reshape node. Name:'node_view_5' Status Message: ...MLOperatorAuthorImpl.cpp(2853)... Exception(1)
tid(b6c8) 80070057 パラメーターが間違っています。
```

```
[E:onnxruntime:, inference_session.cc:2626 onnxruntime::InferenceSession::Initialize::...::operator ()]
Exception during initialization: ...MLOperatorAuthorImpl.cpp(2853)... Exception(1) tid(9908) ...
*************** EP Error ***************
EP Error ... when using ['DmlExecutionProvider', 'CPUExecutionProvider']
Falling back to ['CPUExecutionProvider'] and retrying.
****************************************
```

```
Load model from ...\silentcipher_watermark_fp32.onnx failed:
Node (node_DFT_342) Op (DFT) [ShapeInferenceError] is_onesided and inverse attributes cannot be enabled at the same time
```

## 3. 回避策 2 つ（どちらも本便で実証済み）

### 3-1 `Reshape` の `allowzero=1` を剥がす（`lab/bin/23_allowzero.py`）

torch の dynamo exporter は `Reshape` に **`allowzero=1`** を付ける。実測の分布＝

| グラフ | Reshape 総数 | うち `allowzero=1` |
|---|---:|---:|
| `dit_step` / `dit_step_fp16` | 192 | **168** |
| `encode_conditions_sdpa` | 282 | **166** |
| `dacvae_decoder_fp32` | 61 | **57** |
| `speaker_encoder` | 80 | **64** |
| `duration` | 0 | 0 |
| `sc_core_fp32` | 7 | 0 |

- **最小再現は作れなかった**＝`Shape→Gather→Concat` で動的 shape を作る `Reshape(allowzero=0/1)` の 4 通りは **DML で全部通る**（`lab/out/23_min_reshape_az*.onnx`）。`dit_step` も 168 個持ったまま DML で走る。**つまり `allowzero` 単独が原因ではなく、他の条件と重なったときだけ落ちる。**
- しかし **実グラフでは剥がすと直る**＝`encode_conditions_sdpa`（166 個剥がす）は **session 生成失敗 → DML 209.5 ms** に、`speaker_encoder`（64 個）は **run 失敗 → 成功** に変わった。
- 剥がして安全な理由＝これらの shape は `Shape/Gather/Concat` から来るので **0 が混じらない**（`allowzero` は「shape に 0 が来たとき literal 0 と読むか、入力の同位置の次元を写すか」の切替でしかない）。
- **出力は変わらない**＝`encode_conditions_sdpa_az0` の CPU EP 出力は無手術版と bit 一致（本便の比較で 0.0）。

### 3-2 1 次元 `ConvTranspose` を 2 次元へ挟み替える（`lab/bin/23_conv1d_to_2d.py`）

- **最小再現あり**＝`ConvTranspose`（`x (1,4,16)`・`w (4,4,6)`・stride 3）は **DML で session 生成に失敗して CPU にフォールバック**、同じものを `(1,4,1,16)`／`w (4,4,1,6)`・stride `[1,3]` にすると **通る**（`lab/out/23_min_convtranspose_{1,2}d.onnx`）。**1 次元 `Conv` は通る**（デコーダの 78 個の `Conv` は DML で走っている＝失敗が最初の `ConvTranspose` で起きたことがその証拠）。
- DACVAE デコーダの `ConvTranspose` は 4 個。属性＝`strides [12]/[10]/[8]/…`・`pads [6,6]/[5,5]/[4,4]`・`dilations [1]`・`output_padding [0]`・`group 1`。
- 手術＝重み初期化子の `dims` を `[Ci,Co,K] → [Ci,Co,1,K]` に書き換え（**生バイトは不変なので external data はそのまま共用できる**）、`Unsqueeze(axis=2) → ConvTranspose2d → Squeeze(axis=2)` に置換。**`allowzero` 剥がしと併用して初めて通る**（先に `Reshape` で落ちるため）。
- 結果＝`dacvae_decoder_fp32_dml.onnx` が **DML 178.1 ms**（CPU EP 944.3 ms・無手術 CPU EP 1101.8 ms・torch CPU 778 ms／12 §2-2）。**副作用として CPU EP も 1101.8→944.3 ms に速くなる**。
- 誤差＝torch/ORT-CPU の fp32 出力に対し **1.22e-06**（波形 absmax 0.7321）＝12 §2-1 の 4.77e-07 と同じ桁。fp16 版は 4.37e-03。

## 4. 段2＝DiT 全ループを DML で（`lab/bin/40_loop_dml.py`）

`lab/bin/40_loop.py` の `ort_loop()` を **純 numpy + ORT** に写した（torch を一切 import しない）。t スケジュール・CFG バンドル・CFG 合成・Euler 更新は 13 §6-1 の写経そのまま。x0 と条件は `lab/tmp/*.npz` から読む。

### 4-1 所要（2 反復・rep0 がコールド／rep1 がウォーム）

| 実装 | steps | **ループ壁時計 (cold / warm)** | CFG 段 B=3 中央値 | 非 CFG 段 B=1 中央値 | 初回ステップ |
|---|---:|---:|---:|---:|---:|
| DML fp32・**session 1 本** | 40 | 4.786 / **4.490 s** | 77.9 ms | **145.2 ms** | 208.6 ms |
| DML fp32・**session 2 本** | 40 | 2.647 / **2.388 s** | 77.4 ms | **40.9 ms** | 217.9 ms |
| DML fp16・session 1 本 | 40 | 3.328 / 3.132 s | 47.7 ms | 107.8 ms | 175.8 ms |
| DML fp16・**session 2 本** | 40 | 1.742 / **1.527 s** | 46.9 ms | 29.1 ms | 179.6 ms |
| **ORT CPU EP（1.24.4）** | 40 | 10.295 s | 375.3 ms | 137.7 ms | 407.1 ms |
| （13 §6）ORT CPU 1.29.0 | 40 | 10.855 s | — | — | — |
| （13 §6）torch CPU（KV 有） | 40 | 13.099 s | — | — | — |
| DML fp32・session 2 本 | 10 | 0.867 / **0.609 s** | 77.2 ms | 44.2 ms | 218.1 ms |
| DML fp16・session 2 本 | 10 | 0.617 / **0.377 s** | 46.9 ms | 28.9 ms | 180.3 ms |
| ORT CPU EP（1.24.4） | 10 | 2.634 s | 380.4 ms | 135.9 ms | 432.8 ms |

- **session を分けると 40 steps が 4.490 → 2.388 s（1.88 倍）**。`sample_rf` 相当の値としてはこちらを使うこと。
- 対 ORT CPU（10.295 s）で **4.3 倍速**、対 torch CPU KV 有（13.099 s）で **5.5 倍速**。**ROCm torch bf16 の `sample_rf` 0.626 s（20 §2.3）には 3.8 倍及ばない。**
- **コールド費用は軽い**＝40 steps で 2.647→2.388 s（**+0.26 s／1.11 倍**）。ROCm の「初見形状で 3〜12 倍」（20 §2.2・§3.8）に相当するものは DML では出なかった。
- session 初期化＝`dit_step` fp32 **1.17 s**（1.47 GB 読込込み・CPU EP の 1.196 s とほぼ同じ）、fp16 0.77 s。**session を 2 本にすると読込も 2 回分**（fp32 で約 2.3 s・常駐なら起動時の一度きり）。

### 4-2 形状切替の罰（`lab/bin/23_shape_switch.py`・本便の発見①）

| 測り方 | B=1 中央値 | B=3 中央値 |
|---|---:|---:|
| **新品 session で B=1 だけ** | **39.73 ms**（初回 185.71） | — |
| **新品 session で B=3 だけ** | — | **75.53 ms**（初回 203.75） |
| 同一 session：B=3 → **その後 B=1** | **142.42 ms**（初回 149.69） | 75.70 ms |
| 同一 session：さらに B=3 に戻す → また B=1 | **140.99 ms** | 76.13 ms |

**断定**＝**DirectML の session は、一度大きい形状を見ると小さい形状の実行が 3.6 倍遅くなり、元に戻らない**。
CFG 区間（B=3・20 ステップ）と非 CFG 区間（B=1・20 ステップ）を 1 つの session で回すと、後半 20 ステップが丸ごと罰を受ける＝**40 steps で 2.0 秒の損**。
**回避＝バッチ形状ごとに session を分ける**（重みは 2 倍読むが、常駐なら起動時だけ）。17 (g) の「別 session なら Run を同時に呼べる」という記述とは別の理由で、**session を分ける動機がここにある**。

### 4-3 照合（`lab/bin/23_decode_check.py`・torch コーデックで復号）

基準＝同じ x0・同じ条件で torch の `sample_euler_rf_cfg`（KV 有）を回して `rt.codec.decode_latent` した波形（`lab/out/23_torch_s{40,10}.wav`）。

| 走 | 最終潜在 max abs | 潜在 rel L2 | **wav max abs** | wav rel L2 | wav 相関 | **int16 最大 LSB** | 差のあるサンプル |
|---|---:|---:|---:|---:|---:|---:|---|
| **DML fp32 40 steps** | **5.29e-05** | 2.61e-06 | **2.31e-05** | 3.99e-06 | **0.999999999992** | **1** | 1,410 / 180,480（0.78 %） |
| **DML fp32 10 steps** | **4.12e-05** | 3.25e-06 | **2.32e-05** | 9.17e-06 | 0.999999999958 | **1** | 3,826 / 180,480（2.1 %） |
| DML fp16 40 steps | **5.38** | **0.945** | **0.800** | **1.027** | **0.161** | **26,210** | 180,435 / 180,480 |
| DML fp16 10 steps | 5.26 | 0.990 | 0.927 | 1.029 | 0.156 | 30,387 | 180,433 |

（参考＝13 §6 の ORT CPU：40 steps で潜在 3.56e-05・wav 1.16e-05・**1 LSB**・1,044 サンプル／10 steps で **2 LSB**）

- **fp32 は 13 §6 と同水準、むしろ 10 steps は ORT CPU より良い（2 LSB → 1 LSB）**。潜在 absmax は 4.389797（torch と同じ桁）。
- **session 1 本と 2 本で最終潜在は完全一致**（`23_latent_*_2sess.npz` と `23_latent_*.npz` の diff が同値）＝**session 分割は数値を変えない**。
- **fp16 は「劣化」ではなく「別物」**。13 §8-2 の ORT CPU fp16（wav rel L2 9.23e-03・相関 0.9999575）とは桁が違う。

### 4-4 fp16 が DML で壊れる場所の切り分け（`lab/bin/23_probe_fp16.py`）

1 ステップ・実条件・B=1・t=0.999 の v（出力）:

| グラフ / EP | absmax | std | NaN | Inf | 先頭 4 値 |
|---|---:|---:|---:|---:|---|
| `dit_step`（fp32）/ CPU | 4.292441 | 1.100353 | 0 | 0 | 0.14463, −0.20088, 0.33726, −0.95125 |
| `dit_step`（fp32）/ **DML** | 4.292438 | 1.100353 | 0 | 0 | 0.14463, −0.20088, 0.33726, −0.95125 |
| `dit_step_fp16` / CPU | 4.292258 | 1.100175 | 0 | 0 | 0.14399, −0.20134, 0.33699, −0.95085 |
| **`dit_step_fp16` / DML** | **0.072083** | **0.030772** | **0** | **0** | 0.01345, 0.02156, 0.03421, −0.01478 |
| `dit_step_fp16_block_fix` / CPU | 4.293110 | 1.100196 | 0 | 0 | 0.14424, −0.20171, 0.33638, −0.95101 |
| **`dit_step_fp16_block_fix` / DML** | **0.072083** | **0.030772** | 0 | 0 | **0.01345, 0.02156, 0.03421, −0.01478**（同値） |

**断定**＝
1. **NaN も Inf も出ていない**＝オーバーフローで壊れているのではなく、**残差の本流がほぼ 0 に潰れている**（振幅 1/60）。
2. **13 §5-3 で作った `op_block_list` 版（RMSNorm/AdaLN/RoPE/Softmax を fp32 に残す）でも 1 ビットも変わらない**＝**壊している op はブロックリストの外**。
3. **fp16 が DML で全部だめなわけではない**＝`dacvae_decoder_fp16_dml` は DML で正しく動く（fp32 比 4.37e-03）。**DiT の fp16 グラフに特有の問題**。原因 op の特定は**未実施**（次便の課題。`Where`／`IsNaN`／`Cast` のいずれかを疑うのが筋）。
4. よって **DirectML で fp16 の速度（DiT 1 ステップ 80.6→46.7 ms・ループ 2.388→1.527 s）は現時点で取れない**。

## 5. 段3＝後段（デコーダ・透かし・条件符号化・duration）

| 段 | **DML** | ORT CPU EP 1.24.4 | 参考（torch CPU・12/13） | 参考（ROCm bf16 ウォーム・20 §2.4） |
|---|---:|---:|---:|---:|
| `encode_conditions`（1 回・az0） | **209.5 ms** | 570.1 ms | 951 ms（ORT 1.29 は 618 ms） | — |
| `duration` | **2.15 ms** | 7.12 ms | 30.4 ms | 34 ms |
| DiT 40 steps | **2,388 ms** | 10,295 ms | 13,099 ms | 626 ms |
| `decode_latent`（fp32・手術後） | **178.1 ms** | 1,101.8 ms（無手術） | **778 ms**（12 §2-2） | 55 ms |
| `decode_latent`（fp16・手術後） | **127.7 ms** | 953.5 ms | — | — |
| 透かし（部品合計・**未組立**） | **~37.3 ms**（STFT 0.42＋enc_c 8.56＋dec_c 28.34） | ~260 ms | 264 ms（12 §5-5）／ORT 1.29 で 331 ms（12 §5-4） | 32 ms |

- **`decode_latent` は DML で torch CPU の 4.4 倍速**（778 → 178.1 ms）。20 §2.4 が ROCm で見た「fp32 の decode が病的に遅い」現象は **DirectML では起きない**（fp32 のまま速い）。
- **透かしは 1.24.4 でグラフが載らない**（§2-1 の逐語）。同じ檔が **ORT 1.29.0 では載る**ことを本便で確認（`lab/.venv` で 3 檔とも `LOAD-OK`）。
  - ロード不可＝`silentcipher_watermark_fp32.onnx`・`sc_istft_slim.onnx`・`sc_istft_irfft.onnx`・`sc_istft.onnx`・`sc_full_static.onnx`。
  - ロード可＝`sc_stft.onnx`・`sc_enc_c_fp32.onnx`・`sc_dec_c_fp32.onnx`・`sc_core_fp32.onnx`・`sc_istft_manual.onnx`（ただし manual は DML の run で落ちる）。
  - ⇒ **透かしを DML に載せるなら、iSTFT を `DFT` 以外（行列版 or C# 実装）で組み直した 1 本を作り直す必要がある**（**未実施**）。12 §5-5 のとおり iSTFT は torch で 1.27 ms＝**C# 側に出すのが素直**。
- **`speaker_encoder`（参照音声を使う運用）は手術後 DML で動くが速くならない**（7.0 ms のまま）。もともと軽いので DML に置く意味は薄い。

## 6. 段4＝⒝の端から端まで（短文 3.76 s・`lab/out/23_e2e.json`）

合計＝`encode_conditions 1 回 ＋ duration ＋ DiT ＋ decode ＋ 透かし`（トークナイズ・正規化・リサンプル・末尾トリムは 1 ms 級なので除外）。

| 構成 | 40 steps | **RTF** | 10 steps | **RTF** |
|---|---:|---:|---:|---:|
| **⒝ DML fp32（透かしも DML・未組立）** | **2.815 s** | **0.749** | **1.036 s** | **0.276** |
| ⒝ DML fp32（透かしだけ CPU 331 ms） | 3.109 s | 0.827 | 1.330 s | 0.354 |
| ⒝ ORT CPU EP（1.24.4） | 12.305 s | 3.273 | 4.644 s | 1.235 |
| （13 §6）ORT CPU の DiT ループのみ | 10.855 s | — | 2.754 s | — |
| （11 §5）CPU torch 端から端まで | **15.262 s** | **4.06** | 6.725 s | 1.79 |
| （20 §2.3）**ROCm torch bf16 常駐ウォーム** | **0.749 s** | **0.197** | **0.309 s** | **0.081** |

**断定**＝
1. **⒝＋DirectML は「リアルタイムに届く」**（RTF 0.749 < 1.0）。CPU 前提の⒝（RTF 3.27〜4.06）とは別物になる。10 steps なら RTF 0.276＝**配信で使える速さ**。
2. **しかし ROCm torch bf16（RTF 0.197）には 3.8 倍負ける**。⒝を「速いから選ぶ」根拠には**まだならない**。⒝の売りは 20 §要点 10 の配布物の軽さ（wheel 23.9 MiB・7 パッケージ）と、ベンダに依らない 1 本の実装であって、速度ではない。
3. **fp16 が直れば 1.955 s（RTF 0.520）／10 steps 0.804 s（RTF 0.214）**まで来る計算（DiT 1.527 s・decode 127.7 ms で置換）＝**ROCm に肉薄する**。§4-4 の原因究明は⒝の評価を動かす価値がある。
4. **コールドの初回合成は約 3.90 s**（各段の初回値の和＝DiT ループ 2.647＋encode 0.2713＋duration 0.2420＋decode 0.2865＋透かし部品 0.451）＝ウォーム 2.815 s との差は **約 1.1 s**。ROCm の初回（20 §2.3 で 2.599 s vs ウォーム 0.749 s＝**+1.85 s**）より軽い。**⒜⒞で必須だった「起動直後にウォームアップ射を焼く」設計（20 §3.8）は、⒝＋DML では 1 秒の話に縮む**（形状ごとの罰が小さいため）。

## 7. 段6 前半＝17 (g) の 3 つの棘の実測（`lab/out/23_dml_thorns.json`）

### 棘1「memory pattern と parallel execution は無効にしないとエラーになる」→ **1.24.4 では強制されていない**

`duration.onnx`・実条件・20 回中央値:

| SessionOptions | providers | 中央値 | 出力 `log1p_frames` |
|---|---|---:|---|
| 既定（何も触らない） | Dml + CPU | 1.933 ms | 4.557378768920898 |
| `enable_mem_pattern=True` を明示 | Dml + CPU | 1.894 ms | 同値 |
| `execution_mode=ORT_PARALLEL` | Dml + CPU | 1.957 ms | 同値 |
| **docs 準拠（mem_pattern=False・SEQUENTIAL）** | Dml + CPU | 1.917 ms | 同値 |

**4 通りとも走り、値も速度も同じ**。17 (g) が引いた「these options must be disabled or an error will be returned」は、**少なくとも 1.24.4 のこのグラフでは観測できない**（ORT が内部で無視しているのだろうが、コードは未読＝**未確認**）。
**本便の全測定は docs 準拠の設定で取った**（安全側）。⒝の実装でも docs どおりに書くべき＝将来版で強制される可能性がある。

### 棘2「同一 session への多重 Run は不可／別 session なら同時可」→ **どちらも壊れる（本機）**

`lab/bin/23_dml_run2.py`。`duration.onnx` を 2 スレッド × 30 回。

| 走 | 直列 60 回 | 同一 session・2 スレッド | 別 session・2 スレッド |
|---|---|---|---|
| 予備 | 0.128 s（2.13 ms/回） | 0.088 s・両スレッド ok | 両スレッド例外・**プロセス segfault** |
| r1 | — | 両スレッド例外 | 両スレッド例外・**segfault** |
| r2 | — | 両スレッド例外・**segfault** | 両スレッド例外 |
| r3 | — | 両スレッド例外 | **開始直後に segfault**（出力すら出ない） |

**断定**＝**onnxruntime-directml 1.24.4 ＋ このドライバでは、DML EP に対する同時 Run は同一 session でも別 session でも安全でない**（6 走中 5 走で例外・3 走でプロセスごと落ちる）。
17 (g) が CONFIRMED とした「Multiple threads are permitted to call Run simultaneously if they operate on different inference session objects」は、**文書としては正しくても、この実装・この機体では成り立たない**。
例外の逐語は取れない（ORT の C++ 例外文字列が cp932 混じりで pybind の utf-8 デコードに失敗し、`UnicodeDecodeError: 'utf-8' codec can't decode byte 0x83 in position 181` としてしか出てこない。`log_severity_level=1` にしても stderr は 0 バイト）。
⇒ **⒝のサーバは合成を必ず直列化すること**（上流サーバの `max_concurrent_synthesis: 1`／20 §3.2 と同じ姿勢で十分）。健康診断の一射を合成と並行して打つ設計は**取れない**。

### 棘3「DirectML は opset 20 まで」→ **当たっていない（が天井は近い）**

`lab/onnx` の全 `.onnx` の `opset_import` は **すべて `"" : 20`**（13・12 の export が opset 20 で出ているため）。よって現状は上限に触れない。
ただし 13 §4-1 が挙げた「opset 23 の `Attention` 融合で速くする」道は **DirectML では塞がっている**（`Evaluating models which require a higher opset version is unsupported and will yield poor performance`）。CUDA/WinML 側の EP を使うときだけの選択肢になる。

## 8. 段5＝GPU に載っている物証と停止域の確認

### 8-1 perf カウンタ（`lab/bin/23_gpu_counters.ps1`・`lab/out/23_gpu_counters.json`）

`dit_step.onnx` の 40 steps ループを 12 反復（約 55 秒）走らせながら 3 回サンプルした。

| 時点 | GPU Engine（3D・>1 % のみ） | Dedicated Usage | Total Committed | Shared Usage |
|---|---|---:|---:|---:|
| before | `pid_2912_luid_…0x00011ec8…engtype_3d` 1.13 % | 2,594.7 MB | 3,407.3 MB | 569.0 MB |
| during1 | **`pid_34876_luid_…0x00011ec8…engtype_3d` 15.19 %** | **4,225.7 MB** | **5,051.9 MB** | 582.4 MB |
| during2 | 同 **55.66 %** | 4,225.7 MB | 5,051.9 MB | 582.4 MB |
| during3 | 同 15.02 % | 4,225.7 MB | 5,051.9 MB | 582.4 MB |
| after | `pid_2912_…` 1.44 % | 2,593.7 MB | 3,406.5 MB | 569.0 MB |

- **増分＝Dedicated +1,631.0 MB / Committed +1,644.6 MB**。`dit_step.onnx` の重み 1,467 MB＋作業領域とよく合う。
- 動いたのは **LUID `0x00000000_0x00011ec8_phys_0` の 1 つだけ**。他の 2 アダプタ（`0x1426e` 1.5 MB／`0x142a4` 2,399.4 MB＝仮想ディスプレイ）は before/during/after で不動。20 §5.3 が「DML が掴んでいる」と判定したのと同じ LUID。
- **「LUID 0x11ec8 = 8060S」という名前の対応は本便でも取っていない**（20 §5.3 と同じく**未確認**）。ただし (i) 3D エンジンを回すのはこの 1 つだけ、(ii) 増分が重みサイズと一致、(iii) 20 §5.3 の 2.7 TFLOPS 実測、の 3 点で実質確定してよい。
- Start-Process が返した PID（55764）は uv のラッパで、**GPU を回すのは子（34876）**＝20 §0.1 の「uv の base python を子プロセスで再実行する」構造がここでも出た。

### 8-2 外に書き換わった檔（記録）

| 檔 | 内容 |
|---|---|
| `C:/Users/mugonkun/AppData/Local/AMD/DxcCache/*.parc`（2 檔・新規） | **DirectML のシェーダキャッシュ**。ROCm の `~/.miopen`（20 §4.6）に相当する。利用者プロファイル配下＝保護対象外 |

⇒ **⒜配布物の含意**＝DirectML にも「初回だけコンパイル」のディスクキャッシュがある。ただし §4-1 のとおり **DML の初回費用は 0.2〜0.4 秒**なので、ROCm のように同梱を検討する必要はない。

### 8-3 停止域（無改変の証明）

```bash
git -C upstream/Irodori-TTS status --short --ignored                     # -> 出力なし
find upstream -name "__pycache__" -not -path "*/.git/*" | wc -l          # -> 0
find C:/irodori-TTS-server -newermt "2026-09-04 03:20" -type f | wc -l   # -> 0
find C:/IrodoriTTS         -newermt "2026-09-04 03:20" -type f | wc -l   # -> 0
find ~/.cache/huggingface  -newermt "2026-09-04 03:20" -type f | wc -l   # -> 0
netstat -ano | grep -E ":(8088|7861)\b.*LISTENING"                       # -> 出力なし（両方閉じたまま）
```

- `PYTHONDONTWRITEBYTECODE=1` / `HF_HUB_OFFLINE=1` を全コマンドに付けた。稼働機のサーバは**一度も起動していない**。外部へ何も公開していない。
- 書いたのは `lab/bin/`（`23_*.py` 21 本・`23_gpu_counters.ps1` 1 本・`40_loop_dml.py`）・`lab/onnx/`（手術版 6 本＝`*_az0.onnx` 4・`*_dml.onnx` 2。**いずれも `.onnx` 本体のみで、既存の `.onnx.data` を共用する**ので増分は合計 11.4 MB＝重み 2.9 GB の複製は起きていない）・`lab/out/23_*`（59 檔）・本ノート。
- **`lab/onnx` の既存 13 グラフと `.onnx.data` は 1 バイトも書き換えていない**（手術は新しい檔名に出した）。

## 9. 主席への注意（⒝の RTF 軸に書くべきこと）

1. **⒝の GPU 行に載せる数字は「DirectML・fp32・常駐ウォーム・session 2 本」＝短文 40 steps 2.815 s / RTF 0.749、10 steps 1.036 s / RTF 0.276**。CPU 行（12.3 s / RTF 3.27）と ROCm 行（0.749 s / RTF 0.197）の間に置く。**⒝は CPU の 4.4 倍速だが ROCm torch の 3.8 分の 1**、が比較表の一行になる。
2. **fp16 は本命にできない**。DirectML 上で `dit_step_fp16` は**数値が壊れる**（v の absmax 4.29→0.072・wav 相関 0.161・§4-4）。13 §要点 7「fp16 は GPU EP 前提で書け」という但し書きは、**DirectML については取り消し**が要る。`op_block_list` でも直らない。**「配布物を fp16 で半分に」を⒝の利点として書くなら、CUDA EP で検証してからにすること**（本機に NVIDIA が無いので**未確認**）。
3. **fp16 の誤差は「許容範囲かどうか」を論じる段階にない**＝劣化ではなく別の音（int16 で 26,210 LSB 差）。**聴取は不要**、直すか捨てるかの二択。原因 op の特定は 1 便で足りる見込み（`Where`／`IsNaN`／`Cast` を疑う）＝**やる価値はある**（直れば RTF 0.520・10 steps 0.214 で ROCm に肉薄する）。
4. **⒝の実装には「DirectML 専用の 2 つのグラフ後処理」が要る**＝(a) `Reshape` の `allowzero=1` を剥がす（`encode_conditions` は剥がさないと **DML に一切載らない**）、(b) 1 次元 `ConvTranspose` を 2 次元へ挟み替える（DACVAE デコーダは剥がしただけでは載らない）。どちらも `onnx` パッケージで 10〜30 行・**ビルド時に 1 回やればよい**（実行時コストゼロ）。**⒝の見積りに「export 後処理」という工程を 1 つ足すこと。**
5. **⒝＋DirectML は透かしを載せられない**（`onnxruntime-directml 1.24.4` が `DFT(inverse=1, onesided=1)` を**ロード時に**拒む・§2-1 逐語）。同じ檔は CPU 版 1.29.0 で通る＝**17 §(f) が挙げた「ORT CPU 1.29 と DirectML 1.24 の版ずれ」が、初めて実害として出た**。C# の `Microsoft.ML.OnnxRuntime.DirectML` も 1.24.4 なので**同じ壁に当たる**。回避＝iSTFT を `DFT` 以外で組み直す（部品は DML で動き、合計 ~37 ms）か、透かしだけ C#/CPU で書く（12 §5-5＝dec_c が 86 %）。
6. **DirectML は同時 Run が安全でない**（§7 棘2・6 走中 5 走で例外・3 走で segfault）。**同一 session でも別 session でも落ちた**＝17 (g) の「別 session なら可」は本機で不成立。⒝のサーバ設計に **「合成は必ず 1 本ずつ」** を明記すること。逆に**⒜⒞（torch/ROCm）にはこの制約が無い**ので、比較表の「同時実行」欄で⒝が一段落ちる。
7. **DirectML は形状ごとに session を分けるべき**（§4-2・本便の発見）。同一 session で B=3 の後に B=1 を回すと **3.6 倍遅くなって戻らない**。分ければ 40 steps が 4.490→2.388 s。**重みを 2 回読む代わりに 2 秒買う**という設計判断が⒝に必要＝この 1 行が⒝の RTF を決めている。
8. **DirectML の「初回だけ遅い」は ROCm より桁で軽い**（DiT ループ 2.647→2.388 s＝1.11 倍。ROCm は 20 §2.2・§3.8 で 3〜12 倍）。端から端までのコールドでも **3.90 s → ウォーム 2.815 s（+1.1 s）**、ROCm の +1.85 s より小さい。⒜⒞に必須だった「起動直後にウォームアップ射を数本焼く」設計（20 §3.8）は、⒝＋DML では **1 射で足りる**＝導入の説明が 1 段軽くなる。**⒝の利点としてここは書ける。**
9. **GPU メモリは 1.6 GB（fp32・dit_step 常駐）**。ROCm bf16 の 3.08 GB（20 §要点 6）の半分。ただし本便は DiT だけを常駐させた計測で、**⒝の全グラフ（DiT 1.47 GB＋条件 1.53 GB＋デコーダ 0.26 GB）を同時に常駐させたときの実測は未実施**。単純合計なら 3.3 GB 前後で ROCm と同等になる。
10. **未実施の札**＝(a) fp16 が DML で壊れる原因 op の特定、(b) 透かし 1 本の DFT 抜き再 export と DML 実測、(c) 全グラフ同時常駐での VRAM 実測、(d) 長文（10.92 s）での DML 実測、(e) `speaker_encoder` 手術版の厳密な数値照合、(f) CUDA EP との比較（NVIDIA 機なし）、(g) DirectML wheel の CPU EP が fp16 の `dit_step` を **1.29 の 8 分の 1 の時間で回した**件（本便実測 402.6 ms vs 13 §8-2 の 3,296 ms・B=3）の裏取り＝**ORT の版差の可能性が高いが未確認**。
