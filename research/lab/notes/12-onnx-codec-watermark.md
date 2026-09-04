# 12 — 後段（DACVAE コーデック・SilentCipher 透かし）の ONNX 変換実験

> 席＝第 2 便サブ席（opus）／担当＝BRIEF §4-2 の実証（後段）。実施日 2026-09-04。
> CPU のみ（torch 2.10.0+cpu）。稼働中の 8088/7861 には触れていない。
> 書いたのは `lab/bin/*.py`・`lab/onnx/*`・`lab/out/*.json`・本ノートのみ。
> `upstream/` は clean（`git status --short --ignored` が空・`__pycache__` 0 件）。HF キャッシュは `HF_HUB_OFFLINE=1` で読取のみ。

## 要点（10 行以内）

1. **DACVAE デコーダは `dynamo=True` の一発で出た**。潜在 (B,32,T)→波形 (B,1,T×1920)、動的軸 B・T とも通る。opset 20・**249.75 MB**（fp32）。ORT CPU と torch の最大差 **4.77e-7（SNR 128.7 dB）**。
2. **`@torch.jit.script` の Snake は dynamo 経路で問題にならなかった**（note 03 §6-1 M・note 04 §2-7 の「未確認」を解消）。`Sin/Pow/Mul/Add/Reshape` に素直に落ちる。
3. `remove_weight_norm`（旧 API）の fold は **bit 一致**（fold 前後の torch 出力が完全一致）。31 個すべて fold できた。
4. **エンコーダ（deterministic mean 経路）も出た**。104.95 MB、最大差 3.96e-5（SNR 109.7 dB）。pad はグラフ外で `1920` の倍数へ、で成立。
5. **速度は ORT CPU のほうが遅い**（decoder: torch 778 ms vs ORT 1215 ms＝**1.56 倍遅い**）。ORT の op プロファイルで **`Sin` が 29%**＝Snake が ORT に不利。⒝の動機を「速度」に置くと後段では逆効果。
6. fp16 変換（`onnxruntime.transformers.float16`）は通る＝decoder **125.07 MB**（SNR 69.5 dB）。ただし **CPU EP では速くならない**（1324 ms）。サイズ半減の手としてのみ有効。
7. **note 03/04 の「`torch.istft` は ONNX 化不可」は半分外れ**＝torch 2.10 の dynamo exporter は **`DFT` op を使って出す**（`onnx.checker` も通る）。落ちるのは **ORT のロード時**で、逐語 `Type 'tensor(int32)' of input parameter (val_125) of operator (ScatterND) ... is invalid`。
8. **iSTFT を手書きに差し替えれば壁は消える**。hop=n_fft/2 を使った 50% overlap-add 版（表ゼロ・**38,896 B**）は torch.istft と **torch 上で bit 一致**、ORT で 1.5e-7。
9. **透かし全経路が ONNX 1 本（2.73 MB）で出た**。ORT の出力は upstream の `encode_wav` と最大差 **1.45e-6（SNR 118.8 dB）**、`decode_wav` で **"IRDTS"・confidence 1.0** を復元。C# に残るのは 48k↔44.1k リサンプル・pad・payload の one-hot だけ。
10. **石**＝silentcipher の `enc_c`/`dec_c` は upstream が **`.eval()` を呼ばない**（`server.py:461-466`）＝BatchNorm がバッチ統計モードのまま。忠実に出すには BN を手書きのバッチ統計版に置き換える必要がある（`.eval()` にすると出力が SNR 57 dB ぶん変わる。ただし透かしは両方とも復号できる）。

---

## 0. 参照の付け方と再現の環境

- `upstream/...` ＝ `C:\Users\mugonkun\source\repos\irodori-native-research\upstream\...`
- `[lab-venv]/...` ＝ `C:\Users\mugonkun\source\repos\irodori-native-research\lab\.venv\Lib\site-packages\...`（**note 04 の `[venv]` は稼働機の別 venv。本便は lab venv を読み書きに使った**。dacvae は同じ commit `414c2078`、silentcipher は同じ commit `d46d7d08`）
- `[hf]/...` ＝ `C:\Users\mugonkun\.cache\huggingface\hub\...`

環境変数は note 11 §10 のまま：

```bash
export PY=C:/Users/mugonkun/source/repos/irodori-native-research/lab/.venv/Scripts/python.exe
export LAB=C:/Users/mugonkun/source/repos/irodori-native-research/lab
export PYTHONPATH=C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS
export PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1
export PYTHONIOENCODING=utf-8 PYTHONUTF8=1 PYTHONWARNINGS=ignore
export OMP_NUM_THREADS=8 MKL_NUM_THREADS=8
```

**再現コマンド（この順に実行すれば本ノートの数字が全部出る）**

```bash
"$PY" "$LAB/bin/export_decoder.py"        # 段 1・2   -> lab/out/onnx_decoder.json
"$PY" "$LAB/bin/export_encoder.py"        # 段 3      -> lab/out/onnx_encoder.json
"$PY" "$LAB/bin/bench_and_fp16.py"        # 段 2・4   -> lab/out/onnx_bench_fp16.json
"$PY" "$LAB/bin/wm_probe.py"              # 段 5 前提 -> lab/out/wm_probe.json
"$PY" "$LAB/bin/export_watermark.py"      # 段 5(a)(b)(c) -> lab/out/onnx_watermark.json
"$PY" "$LAB/bin/wm_istft_workaround.py"   # 段 5(b) 詰め  -> lab/out/onnx_istft_workaround.json
"$PY" "$LAB/bin/wm_istft_slim.py"         # 段 5(b) 追補  -> lab/out/onnx_istft_slim.json
"$PY" "$LAB/bin/wm_full_onnx.py"          # 段 5(b) 結論  -> lab/out/onnx_wm_full.json
```

共通の照合入力＝note 11 §5 の基準 wav `lab/out/steps40_seed1234.wav`（48 kHz・180,480 サンプル＝**3.76 s**・sha256 `678d2384…`）。
潜在は `codec.encode_waveform(wav, 48000, normalize_db=None)` で作った実潜在（**(1, 94, 32)**・`lab/out/latent_steps40.npy` に保存・abs.max 4.639）。
※`normalize_db=None` にしたのは、既定の −16 dB 正規化が `audiotools` の ITU-R BS.1770 に落ちてグラフ外の話になるため。デコーダの照合には影響しない。

---

## 1. DACVAE デコーダの export（段 1）

### 1-1. export 用ラッパ（`lab/bin/export_decoder.py:30-63`）

upstream 無改変のまま、**実行される層だけを平坦な `nn.Sequential` に組み直した**。
`DecoderBlock.forward`（`[lab-venv]/dacvae/model/dacvae.py:229-234`）は毎回 `nn.Sequential` を forward 内で作るので、そのままだと export に不利。組み直しの内訳（実測 29 層）：

| # | 層 | 出所 |
|---|---|---|
| 1 | `quantizer.out_proj`（NormConv1d 32→1024 k=1） | `dacvae/nn/bottleneck.py:25` |
| 2 | `decoder.model[0]`（NormConv1d 1024→1536 k=7） | `dacvae/model/dacvae.py:426` |
| 3-8 | `decoder.model[1].block[0,1,4,5,8,9]` = Snake1d → NormConvTranspose1d(stride 12) → ResidualUnit(d=1) → ResidualUnit(d=3) → ResidualUnit(d=9) → Identity | 同 `:229-234` の `j % 2 == 0` チャンク |
| 9-14 | `decoder.model[2]` 同型（stride 10） | 同 |
| 15-20 | `decoder.model[3]` 同型（stride 8） | 同 |
| 21-26 | `decoder.model[4]` 同型（stride 2） | 同 |
| 27-29 | `wm_model.encoder_block.pre[0,1,2]` = Snake1d(96) → NormConv1d(96→1 k=7) → Tanh | `forward_no_conv`（`dacvae.py:326-332`）の実体。`pre[3]` を Identity に置く代わりに、最初から積まない |

**この組み直しが note 04 §2-4 の読みと完全に一致していることを実測で確認**（`pad_mode="auto"` の層＝index 2,3,6,7,10,11 は 1 つも入らない＝データ依存の形状演算 `math.ceil` は実行経路に現れない）。

### 1-2. weight_norm の fold

`torch.nn.utils.remove_weight_norm`（旧 API・`weight_g`/`weight_v`）を全 Conv に掛けた。

| 項目 | 値 |
|---|---|
| fold できた Conv1d/ConvTranspose1d | **31 個**（weight_norm を持たない Conv は 0 個） |
| fold 前（upstream の `codec.decode_latent`）と fold 後の出力差 | **max abs 0.0 ＝ bit 一致** |
| fold 後のパラメータ数 | **65,331,553**（fp32 で 261,326,212 B） |

### 1-3. export の結果

```python
torch.onnx.export(wrapper, (latent_bdt,), path,
                  input_names=["latent"], output_names=["wav"],
                  dynamic_shapes={"latent": {0: Dim("B",1,8), 2: Dim("T",8,4096)}},
                  dynamo=True)
```

- **`dynamo=True` で成功**（fallback の `dynamo=False` は使わなかった）。export 所要 **6.29 s**（encoder は 6.72 s、透かし全経路は 6.81 s）。
- opset **20**（domain `""`）。
- op ヒストグラム＝`Mul 62 / Reshape 61 / Add 41 / Sin 29 / Pow 29 / Conv 27 / Concat 9 / ConvTranspose 4 / Shape 2 / Squeeze 1 / Tanh 1`。
  **`Snake` は `Sin`+`Pow`+`Mul`+`Add`+`Reshape` に落ちるだけ＝カスタム op なし**。`@torch.jit.script`（`dacvae/nn/layers.py:18`）は dynamo 経路で**素通り**した（note 03 §6-1 M・note 04 §2-7 の「未確認」を解消。eager 版への差し替えは不要）。
- `ResidualUnit.shortcut` の `pad = (x.shape[-1] - y.shape[-1]) // 2`（`dacvae.py:71-77`）は、`pad_mode="none"` で長さが変わらないため symbolic に `0` と決まり、`if pad > 0` は静的に False。**動的軸でも guard に落ちない**。

| 檔 | サイズ |
|---|---:|
| `lab/onnx/dacvae_decoder_fp32.onnx` | 461,501 B |
| `lab/onnx/dacvae_decoder_fp32.onnx.data` | 261,423,104 B |
| **合計** | **261,884,605 B ＝ 249.75 MB** |

---

## 2. デコーダの照合と所要（段 2）

### 2-1. 照合表（入力＝実潜在 (1,32,94)。ORT CPU・intra_op=8）

| 比較 | max abs diff | 参照の max abs | 相対最大差 | SNR |
|---|---:|---:|---:|---:|
| fold 後 torch vs upstream `decode_latent` | **0.0** | 0.7321 | 0.0 | ∞（bit 一致） |
| **ORT vs torch（fold 後）** | **4.768e-07** | 0.7321 | 6.514e-07 | **128.75 dB** |
| ORT vs upstream `decode_latent` | 4.768e-07 | 0.7321 | 6.514e-07 | 128.75 dB |
| 動的軸 T=47（半分） | 4.619e-07 | 0.6638 | 6.959e-07 | 128.72 dB |
| 動的軸 B=2, T=47 | 5.215e-07 | 0.6638 | 7.857e-07 | 128.45 dB |

→ **動的長・動的バッチとも通り、誤差は fp32 の丸め相当**（断定）。

### 2-2. 所要（warmup 1 回＋5 回の中央値。同じ潜在・音声長 3.76 s）

| スレッド | torch `decode_latent` | ORT decode | torch `encode_waveform` | ORT encode |
|---|---:|---:|---:|---:|
| **8** | **777.91 ms**（RTF 0.207） | **1214.50 ms**（RTF 0.323） | 440.05 ms | 533.95 ms |
| 16 | 620.34 ms（RTF 0.165） | 1203.40 ms（RTF 0.320） | 353.94 ms | 506.02 ms |

- **ORT CPU は torch CPU より遅い**（decode で 1.56 倍、encode で 1.21 倍）。**断定**。
- ORT はスレッドを 8→16 にしてもほとんど速くならない（1214→1203 ms）。torch は 778→620 ms（−20%）。
- 参考＝note 11 §5 の `decode_latent` 段別時刻は 1017 ms（プロセス全体の初回・warmup なし）。本便の 778 ms は warmup 後の定常値。

### 2-3. なぜ遅いか（ORT の op プロファイル・decoder・8 スレッド・3 回分の合計）

| op | 合計 μs | 呼び数 | 割合 |
|---|---:|---:|---:|
| **Sin** | 1,163,051 | 87 | **29.0 %** |
| Conv | 1,130,376 | 78 | 28.2 % |
| ConvTranspose | 1,067,843 | 12 | 26.7 % |
| Add | 259,656 | 123 | 6.5 % |
| Mul | 243,149 | 186 | 6.1 % |
| Pow | 121,007 | 87 | 3.0 % |
| FusedConv | 12,082 | 3 | 0.3 % |
| Reshape | 8,410 | 183 | 0.2 % |

→ **Snake 活性化（`Sin`+`Pow`）だけで 32%**。torch 側は `@torch.jit.script` で融合されているのに対し、ORT は素の `Sin` を 1 op として回すのでここで負ける（断定：プロファイル実測）。
→ 生データ＝`lab/out/ortprof_dec*.json`。

---

## 3. DACVAE エンコーダの export と照合（段 3）

グラフ＝`encoder` → `quantizer.in_proj` → `chunk(2, dim=1)` の前半（＝mean）。
`upstream/Irodori-TTS/irodori_tts/codec.py:249-251` を逐語で写した（`_pad` はグラフ外）。

| 項目 | 値 |
|---|---|
| 入力 | `wav (B, 1, 1920×T)`。動的軸は `{0: Dim("B"), 2: 1920*Dim("T")}` |
| 出力 | `latent (B, 32, T)` |
| pad | **グラフ外**。`(0, hop - n % hop)` の `reflect`（`dacvae/model/dacvae.py:707-713`）。本便の基準 wav は 180,480 = 94×1920 で pad 不要 |
| export | **`dynamo=True` で成功**（opset 20） |
| weight_norm fold | 31 個・fold 前後 **bit 一致** |
| パラメータ | 27,345,536（fp32 109,382,144 B） |
| 檔サイズ | `dacvae_encoder_fp32.onnx` 606,452 B ＋ `.data` 109,445,120 B ＝ **110,051,572 B ＝ 104.95 MB** |
| op ヒストグラム | `Mul 58 / Add 41 / Conv 31 / Reshape 29 / Sin 29 / Pow 29 / Concat 5 / Shape 1 / Slice 1` |

| 比較 | max abs diff | 参照の max abs | 相対最大差 | SNR |
|---|---:|---:|---:|---:|
| ORT vs torch（同一長） | 3.961e-05 | 4.6387 | 8.539e-06 | 109.67 dB |
| 動的軸（1920×40 = 76,800 サンプル） | 3.964e-05 | 4.6386 | 8.545e-06 | 108.92 dB |

- 誤差はデコーダより 2 桁大きいが、潜在の絶対値レンジが 4.64 なので相対では 8.5e-6。原因は深い Conv スタックの累積丸め（**推測**、切り分けは未実施）。
- 参考＝ONNX encoder → ONNX decoder の往復（VAE の再構成品質）は入力 wav に対し **SNR 19.40 dB**。VAE なので厳密可逆でないのは想定どおり（note 04 §2-7）。

---

## 4. ONNX のサイズと fp16 変換（段 4）

`onnxconverter_common` は lab venv に無い。**`onnxruntime.transformers.float16.convert_float_to_float16`（onnxruntime 1.29 同梱）が使えた**ので、そちらで実施した。
`keep_io_types=True`（入出力は fp32 のまま）・`disable_shape_infer=True`。

| モデル | fp32 | fp16 | 比 | ORT fp16 vs torch fp32 | fp16 の ORT 所要（8 スレッド） |
|---|---:|---:|---:|---|---:|
| DACVAE decoder | **249.75 MB**（261,884,605 B） | **125.07 MB**（131,144,024 B） | 0.501 | max abs 4.868e-04 / SNR **69.54 dB** | 1324.34 ms（fp32 は 1214.50 ms） |
| DACVAE encoder | **104.95 MB**（110,051,572 B） | **52.74 MB**（55,304,933 B） | 0.503 | max abs 3.349e-03 / SNR **64.12 dB** | 575.86 ms（fp32 は 533.95 ms） |

- **fp16 は CPU EP で速くならない**（むしろ 9% 遅い）＝ORT CPU が fp16 kernel を持たず Cast で往復するため（**推測**、Cast の内訳は未計測）。**サイズ半減の手としてだけ有効**。
- 出力 SNR 64〜70 dB＝16 bit PCM（理論 96 dB）より粗い。**波形として聴感上どうかは未確認**（聴取していない）。GPU（DirectML/CUDA EP）で速くなるかも**未確認**（CPU 版 wheel しか無い・note 11 §7）。

### 4-1. 「使っていない透かし枝」が ONNX から落ちること（ライセンス章の材料）

| 区分 | パラメータ | fp32 バイト |
|---|---:|---:|
| DACVAE 全体（`weights.pth` の state_dict） | 107,375,875 | 429,503,500（檔は 429,620,065 B） |
| encoder | 27,288,704 | 109,154,816 |
| quantizer.in_proj / out_proj | 65,664 / 34,816 | 262,656 / 139,264 |
| decoder.model（`pad_mode="auto"` の未実行層を含む） | 70,658,208 | 282,632,832 |
| **`decoder.wm_model`（facebook/dacvae-watermarked 由来の透かし枝）** | **9,328,483** | **37,313,932** |
| └ うち実行経路に残るヘッド（`pre[0]`+`pre[1]`） | **770** | 3,080 |
| └ **ONNX に入らない分** | **9,327,713** | **37,310,852 ＝ 35.58 MB** |

→ **⒝で ONNX に出すと、`facebook/dacvae-watermarked` の透かし枝（LSTM・MsgProcessor・WatermarkDecoderBlock）は 1 バイトも成果物に入らない**（断定：export ラッパが `wm_model.encoder_block.pre[0..2]` の 770 パラメータしか触っていない）。
decoder 側の合計 80,021,507 パラメータ（320.1 MB）に対し ONNX は 65,331,553（261.3 MB）＝**約 59 MB 小さい**（未実行の `pad_mode="auto"` 層も落ちるため）。**BRIEF §4-1 の「同梱できるもの／させるもの」の切り分けで使える材料**。

---

## 5. SilentCipher（段 5）

### 5-0. 前提の実測（`lab/bin/wm_probe.py` → `lab/out/wm_probe.json`）

| 項目 | 値 | 根拠 |
|---|---|---|
| ロード所要 | 0.77 s | `silentcipher.get_model(model_type="44.1k", device="cpu")` |
| hparams（44.1k） | `SR 44100 / N_FFT 4096 / HOP_LENGTH 2048 / message_band_size 1024 / message_dim 5 / message_len 21 / message_sdr 47 / n_messages 1 / ensure_negative_message true / utterance_level_normalization true / frame_level_normalization false / no_normalization false / enc_n_layers 3 / dec_c_n_layers 4` | `[hf]/models--sony--silentcipher/.../44_1_khz/73999_iteration/hparams.yaml` |
| パラメータ | enc_c **43,968** / dec_c **499,012** / dec_m[0] **2,378,773** | 実測 |
| BatchNorm2d の数 | enc_c **3** / dec_c **4** | 実測 |
| **`training` フラグ** | **enc_c=True / dec_c=True / dec_m[0]=True** | `server.py:461-466` に `.eval()` が**無い** |
| running 統計 | 1 回の `encode_wav` で `num_batches_tracked` が **74000 → 74001**、`running_mean` が最大 0.0356 動く | 実測 |
| 同一入力の再現性 | 2 回目の出力と **bit 一致** | training=True の BN は running 統計を使わない（バッチ統計で正規化）ため |
| `.eval()` にしたときの差 | max abs 1.472e-03 / **SNR 57.09 dB** | 実測 |
| 透かしの強さ（出力 − 入力） | max abs 2.581e-03 / SNR 51.72 dB | 実測 |
| 復号（`decode_wav`） | train・eval のどちらの出力でも `{'messages': [[73,82,68,84,83]], 'confidences': [1.0], 'status': True}` | 実測 |

**断定**＝upstream は `enc_c`/`dec_c` を **training モードのまま**推論している。BatchNorm がバッチ統計で正規化する＝**発話ごとに正規化係数が変わる**。
**含意**＝⒝で「ONNX に出す」とき、(i) 忠実にやるなら BN をグラフ内でバッチ統計計算する形に書き換える（本便はこれを採った）、(ii) `.eval()` して Conv に畳み込むなら**出力が SNR 57 dB ぶん upstream と違う**（ただし透かしは confidence 1.0 で復号できる）。どちらを採るかは卓の判断。

### 5-1. (a) enc_c / dec_c の export と照合

`nn.BatchNorm2d` を、`mean = x.mean((0,2,3))` / `var = x.var((0,2,3), unbiased=False)` を**グラフ内で計算する** `BatchStatBN2d`（`lab/bin/export_watermark.py:34-53`）に置換した。

| 対象 | export | 檔サイズ | 置換前 torch との差（torch 上） | ORT vs torch |
|---|---|---:|---|---|
| `enc_c`（carrier→carrier_enc ＋ transform_message） | **dynamo=True 成功** | **263,177 B ＝ 0.25 MB** | carrier_enc max abs 5.951e-04（SNR 127.5 dB）／msg_enc **bit 一致** | carrier_enc max abs **1.851**（max 133.59・rel 1.39e-2・**SNR 61.88 dB**・mean abs 4.30e-05）／msg_enc bit 一致 |
| `dec_c`（merged→message_info） | **dynamo=True 成功** | **2,104,179 B ＝ 2.01 MB** | max abs 7.451e-08（SNR 133.6 dB） | max abs 2.548e-04（max 0.0944・**SNR 74.23 dB**） |
| `core`（enc_c＋dec_c＋正規化＋relu＝STFT と iSTFT の間ぜんぶ） | **dynamo=True 成功** | **2,391,713 B ＝ 2.28 MB** | — | max abs 4.064e-04（max 177.16・rel **2.294e-06**・**SNR 117.73 dB**） |

- **BN を置換せず training モードのまま export しても dynamo=True は通った**（`lab/onnx/sc_enc_c_asis.onnx`）。ただし ONNX の `BatchNormalization` は training_mode 指定になるため、ORT での挙動は**未確認**（本便は置換版を正とした）。
- **`enc_c` 単体の ORT 差が 1.85 と大きい**が、mean abs は 4.3e-5＝**ごく少数の外れ値**。`core` まで通すと相対 2.3e-6 に収まるので、下流に伝播していない（断定：core・最終波形の照合値による）。原因はバッチ統計 BN の分散計算経路の差（**推測**、切り分けは未実施）。

**`h[:, :, self.message_band_size:, :] = 0`（`[lab-venv]/silentcipher/model.py:62`）は export で通る**（断定）。
dec_c の op ヒストグラム＝`Mul 16 / ReduceMean 9 / Conv 8 / Add 8 / Sigmoid 4 / Sub 4 / Reshape 4 / Sqrt 4 / Reciprocal 4 / Transpose 3 / Slice 2 / Shape 2 / Pow 2 / Div 2 / Abs 1 / Expand 1 / Gather 1 / Range 1 / Unsqueeze 1 / **ScatterND 1**`。
→ スライス代入は **`Range`+`Expand`+`Slice`+`ScatterND`** の 4 op に展開される。**動くが重い**。note 04 §3-3 の助言どおり「定数マスクの乗算」に書き換えれば `Mul` 1 個で済む（**未実施**）。

### 5-2. (b) 透かし全経路の export ＝ どこで落ちるか（逐語）

| 試し | 結果 |
|---|---|
| `torch.stft` 単体 | **dynamo=True 成功**。ONNX の **`STFT` op（opset 20）**。檔 39,237 B。ORT 実行 OK。magnitude は max abs 1.734e-05（SNR 134.3 dB） |
| ↑ の phase | **max abs 6.2832（＝2π）・SNR 14.40 dB**。`atan2` の分枝の取り方が ORT と torch で違う点がある（虚部がほぼ 0 で実部が負の bin）。**後段が `cos`/`sin` を掛けるだけなので実害なし**（最終波形の照合で SNR 119 dB が出ることが根拠） |
| `torch.istft` 単体 | **export は dynamo=True で成功**（0.69 s・`DFT 1 / ScatterND 2 / Range 2 / …`）。**`onnx.checker.check_model` も通る**（＝ONNX 仕様上は妥当なモデル） |
| ↑ を ORT でロード | **失敗（逐語）**：<br>`onnxruntime.capi.onnxruntime_pybind11_state.InvalidGraph: [ONNXRuntimeError] : 10 : INVALID_GRAPH : Load model from ...\sc_istft.onnx failed:This is an invalid model. Type Error: Type 'tensor(int32)' of input parameter (val_125) of operator (ScatterND) in node (node_ScatterND_130) is invalid.` |
| 全経路（stft→core→istft）を dynamo=True・静的形状 | **export 成功**（`sc_full_static.onnx` 7,045 KB）。**ORT ロードで同じエラー**：`Type 'tensor(int32)' of input parameter (val_395) of operator (ScatterND) in node (node_ScatterND_400) is invalid.` |
| 全経路を dynamo=False（TorchScript 経路） | **失敗（逐語）**：<br>`File "...\torch\onnx\_internal\torchscript_exporter\utils.py", line 682, in _optimize_graph`<br>`    _C._jit_pass_erase_number_types(graph)`<br>`RuntimeError: Unknown number type: complex`<br>（`aten::istft` の直前に `%4437 : complex = prim::Constant[value={(0,1)}]()` が立つ。`stft.py:35` の `+ 1j*` が原因） |
| 全経路を dynamo=True・動的形状（当便の第 1 版） | 失敗。ただし**当席の指定ミス**：`Expected shape[1] = 165816 of input Tensor to be of the form 2048*NHOP`（165,816 は 2048 の倍数でない）。pad をグラフ外に出せば解消する |

**訂正（note 03 §6-1 O・note 04 §3-3 への上書き）**＝
「ONNX に ISTFT に対応する演算子が無い」は **torch 2.10 の dynamo exporter については当たらない**。
exporter は `DFT` op と overlap-add の `ScatterND` に分解して出す。**壁は ONNX 仕様ではなく ORT の `ScatterND` 型サポート**（int32 の data を受けない）にある。**断定**（逐語エラーが根拠）。

### 5-3. (b) 壁の越え方＝iSTFT を手書きに差し替える

| 実装 | 檔サイズ | torch.istft との差（torch 上） | ORT との差 | ORT 所要 |
|---|---:|---|---|---:|
| `ManualISTFT`（irfft を 2 枚の実行列 matmul、overlap-add を単位行列 ConvTranspose1d） | **134,431,443 B ＝ 128.20 MB** | max abs 2.533e-07（SNR 129.5 dB） | max abs 2.533e-07（SNR 128.3 dB） | — |
| `ManualISTFT_irfft`（irfft は `torch.fft.irfft`＝ONNX `DFT`、overlap-add は単位行列） | 67,191,153 B ＝ 64.08 MB | max abs 1.499e-07 | max abs 1.499e-07 | — |
| **`SlimISTFT`（`DFT` ＋ hop=n_fft/2 を使った Slice/Add の overlap-add＝表ゼロ）** | **38,896 B ＝ 0.04 MB** | **max abs 0.0 ＝ bit 一致** | max abs 1.499e-07（SNR 129.7 dB） | **17.13 ms** |

- `ManualISTFT` の 128 MB の内訳＝IDFT 行列 2 枚（2049×4096 fp32 ×2 ＝ 67.1 MB）＋ overlap-add 用の単位行列（4096×4096 fp32 ＝ 67.1 MB）。**素朴に書くとこうなる**。
- `SlimISTFT`（`lab/bin/wm_istft_slim.py:26-52`）は **hop = n_fft/2 = 2048** を使い、overlap-add を「フレーム t の後半 + フレーム t+1 の前半」を `Slice`+`Add` で書いた＝**表が要らない**。op は `Slice 5 / Mul 3 / Unsqueeze 2 / Cos 1 / Sin 1 / Concat 1 / DFT 1 / Squeeze 1 / Add 1 / Div 1 / Transpose 1 / Reshape 1` の 12 種のみ。動的軸（T=41）も通った。
- **C# に iSTFT を書くなら表は 1 バイトも要らない**（同じ理屈。hann 窓 4096 点と、その二乗の 50% 重なり和 2048 点だけ）。

### 5-4. (b) 結論＝透かし全経路が ONNX 1 本で出る

`lab/bin/wm_full_onnx.py`。グラフ＝**power 正規化 → `torch.stft` → enc_c/dec_c → SlimISTFT → power 復元**。

| 項目 | 値 |
|---|---|
| 入力 | `y_pad (1, 2048×K)`（44.1 kHz・n_fft の倍数へ pad 済み）／`msg (1,1,5,T)`（payload の one-hot）／`orig_power (1,)` |
| 出力 | `y_wm (1, 2048×(K-1))`（44.1 kHz） |
| export | **dynamo=True 成功**・opset 20 |
| 檔 | `lab/onnx/silentcipher_watermark_fp32.onnx` 298,487 B ＋ `.data` 2,559,828 B ＝ **2,858,315 B ＝ 2.73 MB** |
| op | `Mul 36 / Add 21 / ReduceMean 16 / Conv 14 / Sqrt 11 / Reshape 11 / Sub 9 / Sigmoid 7 / Reciprocal 7 / Div 6 / Transpose 6 / Pow 6 / Slice 5 / Concat 4 / Unsqueeze 4 / Gather 3 / Expand 3 / Where 3 / Shape 2 / Squeeze 2 / Pad 2 / Tile 2 / **STFT 1** / Equal 1 / Cast 1 / Atan 1 / Greater 1 / Less 1 / IsNaN 1 / MatMul 1 / Abs 1 / ScatterND 1 / Neg 1 / Relu 1 / Cos 1 / Sin 1 / **DFT 1**` |
| **ORT vs グラフの torch 実行** | max abs 1.445e-06（SNR 118.76 dB） |
| **ORT の出力（48 kHz へ戻した後）vs upstream `Model.encode_wav`** | **max abs 1.451e-06 / rel 2.033e-06 / SNR 118.78 dB** |
| **`decode_wav` による検証** | `{'messages': [[73, 82, 68, 84, 83]], 'confidences': [1.0], 'status': True}` ＝ **"IRDTS" を confidence 1.0 で復元** |
| ORT 所要（8 スレッド・3 回平均） | **331.34 ms**（torch の `encode_wav` は 263.99 ms） |
| 出力 wav | `lab/out/wm_full_onnx.wav` |

**残る `ScatterND` は dec_c のスライス代入由来（float 型）で、ORT は受け入れる**。拒まれたのは `torch.istft` 由来の int32 の `ScatterND` だけ、という切り分けが確定した。

**グラフ外（C# 側）に残るもの**＝
① 48000→44100 と 44100→48000 のリサンプル、② `n_fft=4096` の倍数への右パディング、③ payload → 40 bit → 2bit×20 シンボル → `(1,1,5,T)` の one-hot タイル（`server.py:65-100`・**payload 固定なので定数表に焼ける**）、④ 元の長さへの切り詰め。
なお `orig_power = mean(y²)` はグラフ入力にしたが、グラフ内に入れることもできる（本便は C# 側の自由度のため外に出した）。

### 5-5. (c) 透かし 1 回の所要と内訳（torch CPU・8 スレッド・3.76 s の音声）

`upstream/Irodori-TTS/irodori_tts/watermark.py:70-75` → `[lab-venv]/silentcipher/server.py:283-351` を逐語で段に切って計測（3 回の中央値）。
段別の合計（267.52 ms）が `encode_wav` 全体（263.99 ms）と一致し、手写しの出力は `encode_wav` と **bit 一致**（max abs 0.0）＝計測の写しが正しいことの裏取り。

| 段 | ms | 割合 | 出所 |
|---|---:|---:|---|
| resample 48000→44100 | **0.78** | 0.3 % | `server.py:292`（`torchaudio.functional.resample`） |
| power 正規化 | 0.09 | 0.0 % | `server.py:301` |
| STFT（n_fft 4096・hop 2048） | 1.16 | 0.4 % | `stft.py:20-31` |
| letters_encoding（one-hot） | 0.23 | 0.1 % | `server.py:65-100` |
| **enc_c** | **30.11** | 11.3 % | `server.py:317` |
| transform_message | 0.35 | 0.1 % | `server.py:318` |
| cat（96ch へ連結） | 3.34 | 1.2 % | `server.py:320` |
| **dec_c** | **229.04** | **85.6 %** | `server.py:322` |
| 正規化＋relu | 0.18 | 0.1 % | `server.py:324-335` |
| iSTFT | 1.27 | 0.5 % | `stft.py:33-39` |
| power 復元 | 0.05 | 0.0 % | `server.py:344` |
| resample 44100→48000 | **0.92** | 0.3 % | `server.py:347` |
| **合計** | **267.52** | 100 % | |
| （参考）`encode_wav` 全体・3 回の中央値 | **263.99** | | RTF **0.0702** |

**断定**＝
- **透かしの費用の 86% は `dec_c`（CarrierDecoder・Conv2d 4 層・96→96→96→1 ch・2049×83 の格子）**。enc_c は 11%。
- **48k↔44.1k のリサンプルは合計 1.70 ms＝全体の 0.6%＝無視できる**。note 04 §4 が「1 発声ごとに 2 回」と挙げた往復リサンプルは、**レイテンシの観点では問題にならない**（C# で書き直すときの精度の問題としては残る）。
- note 11 §5 の `silentcipher_watermark` 段（短文 290〜302 ms）とも整合。

---

## 6. 成果物一覧（`lab/onnx/` のうち当便が作ったもの）

> ※`dit_step*.onnx` / `duration.onnx` / `encode_conditions_sdpa.onnx` / `speaker_encoder.onnx` は**別席（前段・DiT 担当）の成果**で当便は触っていない。

| 檔 | バイト | MB | 中身 |
|---|---:|---:|---|
| `dacvae_decoder_fp32.onnx` (+`.data`) | 261,884,605 | 249.75 | 潜在(B,32,T)→波形(B,1,T×1920) |
| `dacvae_decoder_fp16.onnx` (+`.data`) | 131,144,024 | 125.07 | 同・fp16（IO は fp32） |
| `dacvae_encoder_fp32.onnx` (+`.data`) | 110,051,572 | 104.95 | 波形(B,1,1920×T)→潜在 mean(B,32,T) |
| `dacvae_encoder_fp16.onnx` (+`.data`) | 55,304,933 | 52.74 | 同・fp16 |
| **`silentcipher_watermark_fp32.onnx`** (+`.data`) | **2,858,315** | **2.73** | **透かし全経路 1 本**（power 正規化〜iSTFT〜power 復元） |
| `sc_core_fp32.onnx` (+`.data`) | 2,391,713 | 2.28 | STFT と iSTFT の間だけ（hybrid 構成用） |
| `sc_dec_c_fp32.onnx` (+`.data`) | 2,104,179 | 2.01 | dec_c 単体 |
| `sc_enc_c_fp32.onnx` (+`.data`) | 263,177 | 0.25 | enc_c 単体（BN 置換版） |
| `sc_enc_c_asis.onnx` (+`.data`) | 218,479 | 0.21 | enc_c（training BN のまま。ORT 未検証） |
| `sc_stft.onnx` (+`.data`) | 39,237 | 0.04 | `torch.stft`（ONNX `STFT` op） |
| **`sc_istft_slim.onnx`** (+`.data`) | **38,896** | **0.04** | 表ゼロの iSTFT（`DFT`＋Slice/Add） |
| `sc_istft.onnx` (+`.data`) | 157,093 | 0.15 | `torch.istft` そのまま（**ORT がロード拒否**・失敗の証拠として保存） |
| `sc_istft_manual.onnx` (+`.data`) | 134,431,443 | 128.20 | matmul+単位行列版（比較用） |
| `sc_istft_irfft.onnx` (+`.data`) | 67,191,153 | 64.08 | DFT+単位行列版（比較用） |
| `sc_full_static.onnx` (+`.data`) | 7,044,994 | 6.72 | 全経路（`torch.istft` 版）。**ORT ロード拒否**・失敗の証拠 |

**⒝で「後段」を配るときの最小構成**＝`dacvae_decoder_fp32.onnx`（249.75 MB／fp16 なら 125.07 MB）＋ `silentcipher_watermark_fp32.onnx`（2.73 MB）＝ **252.5 MB（fp16 で 127.8 MB）**。
参照 wav をその場で潜在にするなら `dacvae_encoder_fp32.onnx`（104.95 MB／fp16 52.74 MB）を足す。note 04 §2-6 のとおり**参照潜在を事前に焼けば不要**。
対する Python 側の同等物＝`weights.pth` 429,620,065 B ＋ silentcipher の `enc_c.ckpt` 184,765 B ＋ `dec_c.ckpt` 2,008,794 B（`dec_m_0.ckpt` 9,554,818 B と `opt.ckpt` 23,448,174 B は埋め込みには不要）。

---

## 7. 踏んだ石（檔:行つき・再現する人向け）

1. **`copy.deepcopy` が weight_norm のモジュールで落ちる**（逐語）
   `RuntimeError: Only Tensors created explicitly by the user (graph leaves) support the deepcopy protocol at the moment. If you were attempting to deepcopy a module, this may be because of a torch.nn.utils.weight_norm usage`（`[lab-venv]/torch/_tensor.py:145`）。
   旧 API の `weight_norm` は `__init__` 時に通常モードで `setattr(module, 'weight', compute_weight(module))` するので `.weight` が**非 leaf**になる。**1 度でも `inference_mode` の forward を通すと leaf に置き換わって通る**ため、**実行順によって出たり出なかったりする**（当席は 1 本目では踏まず、2 本目で踏んだ）。
   対処＝deepcopy の前に `.weight` を `detach()` する（`lab/bin/onnx_common.py:66-84` の `detach_wn_cache`）。

2. **`torch.export.Dim` の名前が sympy と衝突する**（逐語）
   `TypeError: unsupported operand type(s) for *: 'function' and 'int'`（`[lab-venv]/torch/export/dynamic_shapes.py:227` の `sympify(self.__name__)`）。
   `Dim("N")` は sympy の関数 `N` に解決されるため、`2048 * Dim("N")` のような**導出次元**を作った瞬間に落ちる。**`N`/`S`/`E`/`I`/`O`/`C`/`Q` などの 1 文字名を避ける**（当席は `NHOP` に改名して解消）。

3. **`torch.istft` は ONNX に出るが ORT が拒む**（§5-2 の逐語）。`onnx.checker` は通るので「出力できた＝使える」ではない。**必ず ORT のセッション生成まで確かめること**。

4. **`dynamo=False`（TorchScript 経路）は複素数で死ぬ**（逐語）`RuntimeError: Unknown number type: complex`（`torch/onnx/_internal/torchscript_exporter/utils.py:682`）。`stft.py:35` の `+ 1j*` が原因。**後段の export は dynamo=True 一択**。

5. **silentcipher が `.eval()` されない**（§5-0）。何も考えずに `.eval()` すると upstream と違う波形になる。

6. **所要の測り方**＝`torch.inference_mode()`（か `no_grad`）の外で forward を回すと autograd グラフを積んで **1.6 倍遅く見える**（当席の 1 本目がこれで、`bench_and_fp16.py` で取り直した）。ノートの数字は warmup 1 回＋5 回の中央値。

7. `torch.onnx.export` の進捗に絵文字が入る件（note 11 §8）は本便でも再現。`PYTHONIOENCODING=utf-8` は必須。

---

## 8. 主席への注意

1. **BRIEF §4-2 の後段の答え（断定）**＝**DACVAE の encoder・decoder と SilentCipher の透かしは、いずれも ONNX へ出せる。後段に「出せない段」は無い。**
   note 03 §6-2・note 04 §3-3 の「SilentCipher は出せない（ISTFT が無い）」は **torch 2.10 では成立しない**＝exporter は `DFT` op で出す。実際の壁は ORT の `ScatterND` 型サポートで、**iSTFT を 12 op の手書き（表ゼロ・39 KB）に差し替えれば消える**。透かし全経路を **2.73 MB の ONNX 1 本**にして、upstream の `encode_wav` と SNR 118.8 dB で一致させ、`decode_wav` で "IRDTS" を confidence 1.0 で復元できることまで実証した。**⒝の「部分移植」の境界は後段には無い**（境界は前段のトークナイザと、別席が扱う DiT 側に移る）。

2. **ただし ⒝ の売りを「速度」にすると後段では逆に遅くなる**（断定）。ORT CPU は torch CPU より decoder で **1.56 倍**、透かしで **1.26 倍**遅い。理由は Snake の `Sin`（ORT 実行時間の 29%）と、ORT が Conv/ConvTranspose で torch(MKL-DNN) に勝てないこと。
   **⒝の正しい売りは「配布物と依存の削減」**＝torch 431 MB ＋ dacvae ＋ descript-audiotools ＋ librosa/numba/llvmlite 116 MB ＋ pydub ＋ scipy が丸ごと消えて、ONNX Runtime 40 MB ＋ 後段 252 MB（fp16 なら 128 MB）になる。速度は「GPU EP に載せてから」の話（DirectML/CUDA EP は**未確認**＝CPU 版 wheel しか手元に無い）。

3. **C# 側に残る後段の作業は 4 つだけ**（見積の材料）。
   ① **48000↔44100 のリサンプル**（`torchaudio.functional.resample` 既定＝`lowpass_filter_width=6, rolloff=0.99, sinc_interp_hann`。**厳密一致は未確認**。所要は往復で 1.70 ms＝速度は問題にならず、問題は係数の一致だけ）。
   ② **pad**（DACVAE encoder は hop 1920 の倍数へ reflect、透かしは n_fft 4096 の倍数へ zero）。
   ③ **payload の one-hot タイル**（`server.py:65-100`。payload 固定なので **20 シンボル×5 次元の定数表 1 枚**に焼ける）。
   ④ **末尾トリムと出力長の切り詰め**。
   STFT/iSTFT は**グラフに入れてしまえるので C# に書く必要はない**（本便の 2.73 MB がそれ）。書きたければ書けるが（hann 4096・hop 2048・50% overlap＝表ゼロ）、両取りできる。

4. **透かしの BatchNorm が「学習モードのまま」という上流の状態**（§5-0）は、⒝でも⒜でも効いてくる。忠実に写すなら BN をバッチ統計計算のままグラフに残す（本便の形・SNR 118.8 dB 一致）。`.eval()` して Conv に畳み込むと **upstream と SNR 57 dB ぶん違う波形**になる（透かし自体は両方 confidence 1.0 で復号できる）。**「⒜⒞と⒝でバイト一致の音を出す」を要件にするなら、この 1 点を仕様に書いておく必要がある**。

5. **ライセンス章への材料**（§4-1）＝**ONNX 化すると `facebook/dacvae-watermarked` 由来の透かし枝（9,327,713 パラメータ＝35.58 MB）が成果物から完全に落ちる**。BRIEF §2-2 の「メタデータ apache-2.0 と本文 SAM License の矛盾」が掛かる重みが、⒝の配布物には**そもそも入らない**（残るのは最終ヘッドの 770 パラメータのみ）。⒜（`weights.pth` を丸ごと配る）とは状況が違う＝**比較表のライセンス軸で⒝に有利に効く**。判断は卓。

6. **fp16 はサイズにだけ効く**（decoder 249.75→125.07 MB・SNR 69.5 dB／encoder 104.95→52.74 MB・SNR 64.1 dB）。CPU では速くならない。`onnxconverter_common` は不要で、**onnxruntime 同梱の `onnxruntime.transformers.float16` で足りた**（依存を増やさずに済む）。INT8 は未実施。

7. **未確認として残したもの**＝
   (a) fp16 出力の聴感（聴取していない）、(b) GPU EP（DirectML/CUDA/ROCm）での速度、(c) `torchaudio` のリサンプル係数を C# で厳密再現できるか、(d) `sc_enc_c_asis.onnx`（training BN をそのまま出したもの）が ORT で動くか、(e) `enc_c` 単体の ORT 外れ値 1.85 の原因、(f) dec_c のスライス代入を定数マスク乗算に置き換えたときの速度差、(g) ORT の Snake を `com.microsoft` の融合 op や自作 op で速くできるか、(h) INT8 量子化のサイズと誤差。
