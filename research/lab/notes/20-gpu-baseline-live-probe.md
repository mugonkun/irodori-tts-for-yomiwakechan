# 20 — ROCm(GPU) 基準測定・稼働サーバ実射・DirectML venv の準備

> 席＝第 2 便 b サブ席（opus）／担当＝この機体（Radeon 8060S・ROCm）での GPU 実験 3 本。
> 実施 2026-09-04。司令官の許可（同日）＝「調査中は配信もしないし PC も触らない／稼働中の irodori-TTS-server は自由に落としてよい」。
> **稼働機（`C:/irodori-TTS-server/Irodori-TTS-Server`・`C:/IrodoriTTS/`）は 1 バイトも変更していない**（§4.6 で `find -newermt` により 0 件を証明）。
> 当便が書いたのは `lab/bin/{infer_gpu.py,run_gpu_case.sh,gpu_inproc.py,live_probe.py,dml_check.py,dml_adapter_probe.py}`・`lab/out/*`・`lab/.venv-dml/`・本ノートのみ。
> 相対パス `upstream/…`＝`C:/Users/mugonkun/source/repos/irodori-native-research/upstream/…`、`lab/…`＝同 `irodori-native-research/lab/…`。

## 要点（10 行以内）

1. **GPU は効いている**＝`.venv-rocm` の torch は `2.13.0+rocm10.0.0` / `hip 7.15.26333` / `cuda_available=True` / `AMD Radeon(TM) 8060S Graphics`（§1）。
2. **infer.py（1 実行＝1 プロセス）の GPU 値は「常駐サーバの姿」ではない**＝毎プロセス数秒〜数十秒の一度きり費用が乗る。3 回まわしても 1→2→3 でほぼ改善しない（§2.2）。
3. **同一プロセスで 2 発目を打つと化ける**＝bf16 短文 40 steps は **2.599 s →0.749 s（RTF 0.197）**、fp32 短文は 19.77 s→3.79 s（RTF 1.01）（§2.3）。これが常駐サーバの実効値で、8088 の実射 0.75 s と一致した。
4. **fp32 GPU の `decode_latent` は病的に遅い**＝コールド 16.5 s／ウォームでも 1.98 s（CPU は 1.02 s）。**bf16 だと 0.054 s＝37 倍速**。ROCm/MIOpen の fp32 経路の問題（§2.4）。
5. **CPU 基準（11 §5）との比**＝短文 40 steps で CPU 15.26 s → GPU bf16 ウォーム **0.749 s＝20.4 倍速**（RTF 4.06 → **0.197**）。長文 40 steps は 40.29 s → 1.729 s（23.3 倍・RTF 3.69 → 0.158）。
6. **VRAM ピーク**＝bf16 は **3.08 GB**（短文・長文とも同じ）、fp32 は 4.51 GB（短文）／5.90 GB（長文）。bf16 は形状に依らず一定＝配布の最低要件が読みやすい（§2.5）。
7. **`voice` は必須ではない**＝`irodori.no_ref=true` を付ければ voice 省略で **200**（01 §要点 7 の「未確認」を実射で確定）。付けないと 400『No voice was provided and IRODORI_DEFAULT_VOICE is not set.』（§3.3）。
8. **SSE の初音は 0.72〜1.09 s**（`first_sentence_chunk_min_chars=1`）。最初のチャンク「こんにちは。」で **2.84 秒ぶんの音声**が返るので、以後 0.93 s で次が来る＝**再生が途切れない**（§3.5）。非ストリーム全文は 1.05〜1.34 s。
9. **`seed` を指定すると全チャンクが同じ seed になる**（seed=42 で index 0/1 とも 42）。未指定なら**チャンクごとに別の乱数 seed**（§3.5）。
10. **DirectML は使える**＝`lab/.venv-dml`（onnxruntime-directml **1.24.4**）で `DmlExecutionProvider` が出て、`smoke_sdpa.onnx` の CPU EP との最大差 **4.8e-07**（§5）。ただし **provider options からアダプタ名は取れない**（`{}` が返る）＝掴んだ GPU の確認は実測スループット（CPU の 3.2 倍・2.7 TFLOPS）と perf カウンタで代替した。

---

## 0. 前提確認（実施前）

```bash
netstat -ano | grep -E ":(8088|7861)\b"
# -> NO LISTEN on 8088/7861 （どちらも起動していない状態から始めた）
```

```bash
PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1 \
"C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe" -c \
"import torch,sys; print('torch',torch.__version__); print('cuda_avail',torch.cuda.is_available()); print('name',torch.cuda.get_device_name(0)); print('hip',torch.version.hip)"
```

逐語:

```
torch 2.13.0+rocm10.0.0
cuda_avail True
name AMD Radeon(TM) 8060S Graphics
hip 7.15.26333
cuda None
py 3.12.14 (main, Aug 25 2026, 14:01:42) [MSC v.1944 64 bit (AMD64)]
```

### 0.1 実行系の素性（重要な確認）

| 事実 | 根拠（実測） |
|---|---|
| `.venv-rocm` に入っている `irodori_tts` は **upstream clone と同一 commit** | `.venv-rocm/Lib/site-packages/irodori_tts-0.1.0.dist-info/direct_url.json` = `{"url":"https://github.com/Aratako/Irodori-TTS.git","vcs_info":{"vcs":"git","commit_id":"8224dafb46d0aba89209a8f905f1cb7e3299d9c1"}}`。`diff -rq` の差分は `__pycache__` のみ |
| `PYTHONPATH` が site-packages に勝つ | `irodori_tts.__file__` → `...\irodori-native-research\upstream\Irodori-TTS\irodori_tts\__init__.py`（PYTHONPATH 指定時）／指定なしなら `.venv-rocm\Lib\site-packages\...`。**§2 の測定は upstream clone のコードで走っている**（＝サーバと同一コード） |
| `.venv-rocm` は**自己完結していない** | `.venv-rocm/pyvenv.cfg` の `home = C:\Users\mugonkun\AppData\Roaming\uv\python\cpython-3.12-windows-x86_64-none`。実際に起動すると `python.exe`（PID 34716）が **uv 管理の base python を子プロセスで再実行**（PID 57792・`CommandLine` は `AppData\Roaming\uv\python\...\python.exe`）。⇒ **⒜専用インストーラでは uv の python を持ち去られると venv が死ぬ**＝埋め込み python を同梱する設計が要る |
| `torchcodec 0.10.0` は `.venv-rocm` に**入っている** | `uv pip list` に `torchcodec 0.10.0`。ただしサーバは `torchaudio.save` を使わず自前の `audio.py` で wav を組むので 11 §4 の門は 8088 経路では踏まない |

## 1. 実験 1 の道具＝`lab/bin/infer_gpu.py`

`lab/bin/infer_cpu.py` の写し。同じ「`torchaudio.save` → soundfile シム」に加えて、

- `torch.cuda.max_memory_allocated()` / `max_memory_reserved()` / `mem_get_info()`
- `torch.version.hip` / `torch.version.cuda` / `torch.cuda.get_device_name(0)`
- `InferenceRuntime.from_key` をクラスに直接パッチしてモデル読込時間を計測（import 形式に依らない）
- `infer.py` の `[timing]` 行を stdout の tee で横取りして JSON に収める（`upstream/Irodori-TTS/infer.py:34-38`）
- 出力 wav の sha256

を JSON に出す。**upstream は無改変**（`runpy.run_path` で `infer.py` を `__main__` として走らせるだけ）。

### 再現コマンド（1 条件 3 回）

```bash
# lab/bin/run_gpu_case.sh <prec> <steps> <short|long> <n>
cd C:/Users/mugonkun/source/repos/irodori-native-research/lab/bin
bash run_gpu_case.sh bf16 40 short 3
```

中身の環境（11 §10 と同じ＋実行系だけ差し替え）:

```bash
PY=C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe   # 読むだけ
export PYTHONPATH=C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS
export PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1 PYTHONIOENCODING=utf-8 PYTHONUTF8=1
export PYTHONWARNINGS=ignore OMP_NUM_THREADS=8 MKL_NUM_THREADS=8
"$PY" "$LAB/bin/infer_gpu.py" --json-out out/gpu_<tag>.json --tag <tag> -- \
  --hf-checkpoint Aratako/Irodori-TTS-v4.1-Small \
  --text "<短文 or 長文>" --caption "落ち着いた女性の声で、丁寧に話す。" \
  --no-ref --seed 1234 --model-device cuda --codec-device cuda \
  --model-precision <fp32|bf16> --codec-precision <fp32|bf16> \
  --num-steps <40|10> --output-wav out/gpu_<tag>.wav
```

条件は 11 §5 と同一（短文＝`こんにちは、読み分けちゃんのテストです。`／長文＝`読み分けちゃん2 は、配信中の…試しています。`・caption・`--no-ref`・`--seed 1234`）。

## 2. 実験 1 の結果

### 2.1 全 24 走（{fp32,bf16}×{40,10}×{短文,長文}×3 回）

生データ＝`lab/out/gpu_*.json`（24 檔）・音声＝`lab/out/gpu_*.wav`。

| tag | load s | pred ms | sample_rf ms | decode ms | wm ms | **ttd s** | audio s | **RTF合成** | wall s | RTF全体 | VRAM alloc MB | peak RSS MB | sha256(先頭16) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| fp32_s40_short_r1 | 19.69 | 484 | 1511 | 16477 | 918 | **19.393** | 3.76 | **5.158** | 39.44 | 10.49 | 4513 | 6340 | `1df13402f3cc4299` |
| fp32_s40_short_r2 | 20.07 | 479 | 1516 | 16534 | 917 | **19.449** | 3.76 | **5.173** | 39.89 | 10.61 | 4513 | 6331 | `1df13402f3cc4299` |
| fp32_s40_short_r3 | 19.72 | 480 | 1511 | 16547 | 938 | **19.478** | 3.76 | **5.180** | 39.56 | 10.52 | 4513 | 6340 | `1df13402f3cc4299` |
| fp32_s10_short_r1 | 20.02 | 489 | 487 | 16549 | 927 | 18.454 | 3.76 | 4.908 | 38.84 | 10.33 | 4513 | 6335 | `195d416d712f2251` |
| fp32_s10_short_r2 | 20.59 | 501 | 501 | 16562 | 917 | 18.484 | 3.76 | 4.916 | 39.44 | 10.49 | 4513 | 6328 | `195d416d712f2251` |
| fp32_s10_short_r3 | 20.06 | 524 | 484 | 16520 | 924 | 18.454 | 3.76 | 4.908 | 38.87 | 10.34 | 4513 | 6340 | `195d416d712f2251` |
| fp32_s40_long_r1 | 19.98 | 490 | 3452 | 52454 | 4525 | 60.924 | 10.92 | 5.579 | 81.27 | 7.44 | 5899 | 6341 | `b3d2f41a84c50091` |
| fp32_s40_long_r2 | 22.22 | 538 | 4592 | 53758 | 1704 | 60.595 | 10.92 | 5.549 | 83.27 | 7.62 | 5899 | 6341 | `b3d2f41a84c50091` |
| fp32_s40_long_r3 | 23.89 | 560 | 3678 | 51607 | 1108 | 56.956 | 10.92 | 5.216 | 81.26 | 7.44 | 5899 | 6339 | `b3d2f41a84c50091` |
| fp32_s10_long_r1 | 22.49 | 530 | 1000 | 49438 | 1082 | 52.053 | 10.92 | 4.767 | 74.97 | 6.87 | 5899 | 6332 | `3f382a038e1a9a10` |
| fp32_s10_long_r2 | 21.96 | 517 | 1008 | 49527 | 1132 | 52.187 | 10.92 | 4.779 | 74.55 | 6.83 | 5899 | 6341 | `3f382a038e1a9a10` |
| fp32_s10_long_r3 | 26.55 | 844 | 1142 | 56437 | 1118 | 59.546 | 10.92 | 5.453 | 86.50 | 7.92 | 5899 | 6329 | `3f382a038e1a9a10` |
| **bf16_s40_short_r1** | 33.39 | 1040 | 1010 | 577 | 1588 | **4.218** | 3.80 | **1.110** | 38.13 | 10.04 | **3079** | 6395 | `6cd1c122614c6bce` |
| bf16_s40_short_r2 | 32.53 | 988 | 1053 | 523 | 1486 | 4.053 | 3.80 | 1.067 | 37.12 | 9.77 | 3079 | 6401 | `6cd1c122614c6bce` |
| bf16_s40_short_r3 | 28.93 | 692 | 805 | 439 | 1179 | 3.118 | 3.80 | 0.821 | 32.53 | 8.56 | 3079 | 6406 | `6cd1c122614c6bce` |
| bf16_s10_short_r1 | 28.72 | 1160 | 357 | 558 | 1511 | 3.597 | 3.80 | 0.947 | 32.85 | 8.65 | 3079 | 6406 | `c2e6f3a434688215` |
| bf16_s10_short_r2 | 28.85 | 706 | 236 | 405 | 1095 | 2.446 | 3.80 | 0.644 | 31.75 | 8.36 | 3079 | 6406 | `c2e6f3a434688215` |
| bf16_s10_short_r3 | 29.14 | 675 | 261 | 419 | 1184 | 2.543 | 3.80 | 0.669 | 32.09 | 8.45 | 3079 | 6406 | `c2e6f3a434688215` |
| bf16_s40_long_r1 | 38.51 | 696 | 1308 | **8372** | 1315 | 11.694 | 10.96 | 1.067 | 50.64 | 4.62 | 3079 | 6395 | `c174d69128bf303a` |
| bf16_s40_long_r2 | 21.71 | 654 | 1306 | 705 | 1106 | 3.774 | 10.96 | 0.344 | 25.89 | 2.36 | 3079 | 6399 | `c174d69128bf303a` |
| bf16_s40_long_r3 | 21.53 | 653 | 1276 | 707 | 1146 | 3.786 | 10.96 | 0.345 | 25.72 | 2.35 | 3079 | 6406 | `c174d69128bf303a` |
| bf16_s10_long_r1 | 23.43 | 647 | 378 | 718 | 1164 | 2.910 | 10.96 | 0.266 | 26.74 | 2.44 | 3079 | 6401 | `a127175e49b17598` |
| bf16_s10_long_r2 | 21.75 | 645 | 375 | 719 | 1130 | 2.871 | 10.96 | 0.262 | 25.01 | 2.28 | 3079 | 6406 | `a127175e49b17598` |
| bf16_s10_long_r3 | 22.59 | 664 | 374 | 710 | 1112 | 2.864 | 10.96 | 0.261 | 25.85 | 2.36 | 3079 | 6397 | `a127175e49b17598` |

- `pred`=`predict_duration`／`decode`=`decode_latent`／`wm`=`silentcipher_watermark`。`tokenize_text`・`prepare_reference`・`unpatchify_latent` は合計 2 ms 未満なので省いた（生 JSON にはある）。
- `ttd`=`total_to_decode`（モデル読込を含まない）。`RTF合成`=`ttd ÷ 音声長`。`RTF全体`=プロセス起動〜wav 書出しの壁時計 ÷ 音声長。
- `VRAM alloc`=`torch.cuda.max_memory_allocated()`。reserved は fp32 4680/6178 MB・bf16 3178 MB（`bf16_s40_long_r1` だけ 3370 MB）。
- `mem_get_info()` は `device_total_mb = 102129`（≒100 GiB）を返す＝**iGPU の統合メモリなので「VRAM 4GB」は実質の壁ではない**（Win32_VideoController の `AdapterRAM 4293918720` は表示値にすぎない）。

### 2.2 「1 回目＝コールド／2〜3 回目＝ウォーム」は **成立しなかった**（断定）

依頼の想定に反し、**同じ条件を 3 回まわしてもプロセスが違えば毎回コールド**である。

- fp32 は 3 走すべてほぼ同値（短文 40 steps で 19.393 / 19.449 / 19.478 s＝ばらつき 0.4%）。`decode_latent` は 16477 / 16534 / 16547 ms で改善なし。
- bf16 は r1 だけやや遅い（短文 40 steps 4.218 → 4.053 → 3.118 s）が、これは MIOpen のディスク db（後述）への書き込みが r1 に集中しただけで、**桁は変わらない**。
- 例外は `bf16_s40_long_r1` の `decode_latent 8372 ms → r2/r3 で 705/707 ms`＝**この形状を初めて見た走だけ 12 倍**。同時に `model_load 38.51 s → 21.7 s` も落ちている。

**機構**＝MIOpen のユーザ db は `C:/Users/mugonkun/.miopen/`（`cache/3.6.0.8d1ae90e/gfx1151_20.ukdb` 1.96 MB ＋ `db/gfx1151_20.HIP.3_6_0_8d1ae90e.ufdb.txt` 612 KB・合計 2.5 MB）に**ディスク持続**する。当便の実験でこの 3 檔の mtime が `2026-09-04 03:08` に更新された。
⇒ **コンパイル済みカーネルはプロセスを跨いで残る**が、**それ以外の一度きり費用（アルゴリズム選択・ワークスペース確保・hipBLASLt の初期化）はプロセス内でしか残らない**。01 §1.4 ②「プロセス内キャッシュのみ」は**半分正しい**＝カーネル本体は残り、選択は残らない、と改める。

**主席への含意**＝⒜⒞のどちらでも「毎回プロセスを起こす」設計（CLI をショットごとに叩く等）は**絶対に取れない**。常駐サーバは必須。

### 2.3 常駐サーバの実効値＝同一プロセス内の 2 発目以降（`lab/bin/gpu_inproc.py`）

`InferenceRuntime` を 1 回だけ作って同じ `SamplingRequest` を 3 回投げた。生データ＝`lab/out/gpu_inproc_{fp32_s40,bf16_s40,bf16_s10}.json`。

```bash
"C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe" \
  lab/bin/gpu_inproc.py --precision bf16 --steps 40 --len both --reps 3 \
  --json-out lab/out/gpu_inproc_bf16_s40.json
```

| 条件 | 走 | pred ms | sample_rf ms | decode ms | wm ms | **ttd s** | 音声 s | **RTF** |
|---|---|---|---|---|---|---|---|---|
| fp32 40 短文 | 1（コールド） | 538 | 1610 | 16641 | 981 | 19.773 | 3.76 | 5.259 |
| fp32 40 短文 | 2 | 97 | 1678 | **1977** | 33 | **3.789** | 3.76 | **1.008** |
| fp32 40 短文 | 3 | 101 | 1772 | 1983 | 36 | 3.895 | 3.76 | 1.036 |
| fp32 40 長文 | 1（コールド） | 101 | 3778 | 49115 | 121 | 53.119 | 10.92 | 4.864 |
| fp32 40 長文 | 2 | 94 | 3811 | **5858** | 80 | **9.845** | 10.92 | **0.902** |
| fp32 40 長文 | 3 | 103 | 3789 | 5786 | 80 | 9.762 | 10.92 | 0.894 |
| **bf16 40 短文** | 1（コールド） | 609 | 625 | 372 | 990 | 2.599 | 3.80 | 0.684 |
| **bf16 40 短文** | 2 | 34 | 626 | **55** | 32 | **0.749** | 3.80 | **0.197** |
| **bf16 40 短文** | 3 | 37 | 619 | 54 | 31 | 0.743 | 3.80 | 0.196 |
| bf16 40 長文 | 1 | 38 | 1233 | 611 | 107 | 1.991 | 10.96 | 0.182 |
| bf16 40 長文 | 2 | 40 | 1438 | 164 | 86 | **1.729** | 10.96 | **0.158** |
| bf16 40 長文 | 3 | 44 | 1428 | 168 | 83 | 1.724 | 10.96 | 0.157 |
| bf16 10 短文 | 1 | 598 | 205 | 378 | 961 | 2.144 | 3.80 | 0.564 |
| bf16 10 短文 | 2 | 36 | 185 | 54 | 33 | **0.309** | 3.80 | **0.081** |
| bf16 10 短文 | 3 | 41 | 202 | 53 | 31 | 0.328 | 3.80 | 0.086 |
| bf16 10 長文 | 1 | 41 | 365 | 578 | 107 | 1.093 | 10.96 | 0.100 |
| bf16 10 長文 | 2 | 40 | 340 | 145 | 88 | **0.613** | 10.96 | **0.056** |
| bf16 10 長文 | 3 | 38 | 342 | 144 | 82 | 0.607 | 10.96 | 0.055 |

**この表の 2〜3 走目が、8088 の実射（§3.7 で 0.75 s）と一致した値**である。ウォーム bf16 短文 40 steps `0.749 s` ⇔ 8088 実射 `0.741〜0.752 s`。**⒞の見積りはこの列を使うこと。**

### 2.4 CPU 基準（11 §5）・probe（01 §1.5）との並置

| 条件 | CPU fp32（11 §5） | GPU fp32 ウォーム | GPU bf16 ウォーム | bf16 の CPU 比 | probe 8088 実測 |
|---|---|---|---|---|---|
| 短文 40 steps | **15.262 s / RTF 4.06** | 3.789 s / RTF 1.008 | **0.749 s / RTF 0.197** | **20.4 倍速** | bf16 10 字 **0.769〜0.782 s**（01 §1.5） |
| 短文 10 steps | 6.725 s / RTF 1.79 | （未測） | **0.309 s / RTF 0.081** | 21.8 倍速 | — |
| 長文 40 steps | 40.292 s / RTF 3.69 | 9.845 s / RTF 0.902 | **1.729 s / RTF 0.158** | 23.3 倍速 | — |
| 長文 10 steps | 14.047 s / RTF 1.29 | （未測） | **0.613 s / RTF 0.056** | 22.9 倍速 | — |

- **当便の GPU bf16 ウォーム 0.749 s は、probe の 0.77 s（01 §1.5・別プロセス・別チェックポイント）と 3% 以内で一致**した＝二重に裏が取れた。
- **bf16 は fp32 の 5.1〜5.7 倍速**（3.789→0.749 / 9.845→1.729）。01 §1.5 の「4.8 倍速」とも整合。

**段別の読み取り（断定）**

| 段 | CPU 短文40 | GPU fp32 ウォーム | GPU bf16 ウォーム | 所見 |
|---|---|---|---|---|
| `predict_duration` | 1105 ms | 97 ms | **34 ms** | GPU で 32 倍速。もはや無視できる |
| `sample_rf` | 12848 ms | 1678 ms | **626 ms** | **bf16 ウォームでは全体の 84%＝ここが唯一の主役**（0.626/0.749） |
| `decode_latent` | 1017 ms | **1977 ms** | **55 ms** | fp32 GPU は **CPU より遅い**。bf16 で 36 倍速 |
| `silentcipher_watermark` | 290 ms | 33 ms | **32 ms** | GPU では 4% 程度。CPU での「RTF 0.07 の固定費」は GPU では消える |

- **fp32 の `decode_latent` 異常**＝コールド 16.5 s／ウォーム 1.98 s。CPU（1.02 s）より遅い。走行中に MIOpen が
  `MIOpen: Warning [IsEnoughWorkspace] [EvaluateInvokers] Solver <GemmFwdRest>, workspace required: 485130240, provided ptr: 0000000000000000 size: 0`
  を大量に吐く＝**ワークスペース 0 で渡されたため GEMM ベースの遅い solver に落ちている**（DACVAE の畳み込み）。bf16 では出ない。
  ⇒ **ROCm を採るなら bf16 は「速いから選ぶ」のではなく「fp32 が壊れているから必須」**。⒜⒞の既定は bf16 で固定すべき。
- **⒝ONNX 化の優先順が CPU と GPU で変わる**＝CPU は `sample_rf ≫ decode_latent ≒ predict_duration > watermark`（11 §5）だったが、**GPU bf16 ウォームでは `sample_rf`(626 ms) ≫ `decode_latent`(55) ≒ `predict_duration`(34) ≒ `watermark`(32)**。ONNX 化の投資先は **sample_rf(RF DiT) 一点**でよい。

### 2.5 VRAM・決定性・出力の同一性

- **VRAM ピーク（`max_memory_allocated`）**＝bf16 は短文・長文とも **3079 MB で一定**。fp32 は短文 4513 MB／長文 5899 MB＝**音声長で増える**。⇒ bf16 は「3.1 GB あれば長さに依らず動く」と言い切れる＝⒜の最低要件として使える。
- **決定性**＝`--seed 1234` 固定で **fp32・bf16 とも 3 走すべて wav が sha256 一致**（表の sha256 列）。**bf16 でも決定的**（01 §1.5 の probe 所見と一致）。
- ただし **fp32 と bf16 は別の音声**＝音声長が短文 3.76 s（fp32）vs **3.80 s**（bf16）、長文 10.92 vs **10.96** s。**`predict_duration` の出力が精度で変わる**＝bf16/fp32 の A/B は「同じ音の劣化比較」ではなく「別の生成」。sha256 も当然すべて別。
- steps 40→10 でも音声長は不変（fp32 3.76／bf16 3.80）＝duration 予測は steps に依らない。
- GPU 走でも **peak RSS は 6.3〜6.4 GB**（CPU 走 6.3〜7.1 GB とほぼ同じ）＝**GPU にしても RAM は減らない**。統合メモリ機なので当然だが、dGPU 機での見積りには使えない（**未確認**）。

## 3. 実験 2＝稼働機サーバ（8088）の実射

### 3.1 起動（bat は使わない・`pause` で止まるため）

```powershell
$log="C:\Users\mugonkun\source\repos\irodori-native-research\lab\out\server-stdout.log"
$err="C:\Users\mugonkun\source\repos\irodori-native-research\lab\out\server-stderr.log"
$env:IRODORI_PRELOAD="true"; $env:IRODORI_EMPTY_CACHE_INTERVAL="0"
$env:IRODORI_MODEL_PRECISION="bf16"; $env:IRODORI_CODEC_PRECISION="bf16"
$env:PYTHONDONTWRITEBYTECODE="1"; $env:HF_HUB_OFFLINE="1"; $env:PYTHONIOENCODING="utf-8"
$p = Start-Process -FilePath "C:\irodori-TTS-server\Irodori-TTS-Server\.venv-rocm\Scripts\python.exe" `
  -ArgumentList "-m","irodori_openai_tts","--host","0.0.0.0","--port","8088" `
  -WorkingDirectory "C:\irodori-TTS-server\Irodori-TTS-Server" `
  -RedirectStandardOutput $log -RedirectStandardError $err -PassThru -WindowStyle Hidden
$p.Id   # -> 34716
```

- `irodori-server-gpu.bat` の 4 つの `set` と `cd /d` を再現。**bat 自体は実行していない**（末尾 `pause` で止まるため）。
- **依頼手順からの逸脱 2 つ（記録）**＝`PYTHONDONTWRITEBYTECODE=1` と `HF_HUB_OFFLINE=1` を追加した。前者は稼働機に `.pyc` を 1 檔も足さないため、後者は HF への通信を封じるため。どちらも合成の挙動を変えない（§4.6 で無改変を証明）。
- **`-RedirectStandardOutput` は uvicorn の INFO ログを拾わない**＝logging は stderr に出る。`server-stdout.log` に入るのは `print` 由来の 4 行だけだった。**逐語ログは `lab/out/server-stderr.log` にある**。

### 3.2 P7＝`/health` 全文（起動直後・逐語）

```json
{
  "status": "ok",
  "model": {
    "id": "irodori-tts",
    "hf_checkpoint": "Aratako/Irodori-TTS-v4-Small",
    "model_device": "auto",
    "codec_device": "auto",
    "model_precision": "bf16",
    "codec_precision": "bf16",
    "compile_model": false,
    "compile_dynamic": false
  },
  "runtime": {
    "preload": true,
    "loaded": true,
    "loading": false,
    "checkpoint": "C:\\Users\\mugonkun\\.cache\\huggingface\\hub\\models--Aratako--Irodori-TTS-v4-Small\\snapshots\\4c92c7ee2bb15c19a97cf4e86d24fd6bf33b0135\\model.safetensors",
    "load_timeout": 300.0,
    "max_concurrent_synthesis": 1,
    "synthesis_wait_timeout": 300.0
  },
  "voices": { "dir": "voices", "dir_exists": true, "files": 0 },
  "defaults": {
    "response_format": "wav",
    "chunking_enabled": true,
    "chunk_min_chars": 80,
    "first_sentence_chunk_min_chars": null
  }
}
```

- **チェックポイントの食い違い（重要）**＝サーバは **`Aratako/Irodori-TTS-v4-Small`**、§2 の基準測定は依頼どおり **v4.1-Small**。01 §1.0 が記録した「7861 と 8088 で版が違う」がまだ残っている。数字が一致したので実害は無かったが、**§2 と §3 の値は厳密には別チェックポイント**（札）。
- **`model_device` は `"auto"` のまま**＝`/health` からは「実際に cuda を掴んだか」が**分からない**。`upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py:173` は `settings.model_device` をそのまま返し、解決は `.../runtime.py:98-102` の `_resolve_device` が `default_runtime_device()` を呼ぶだけ。
  ⇒ **⒞の薄い層は「GPU で動いているか」を /health では検査できない**。ログを読むか、自前で健康診断の一射を打つしかない。

### 3.3 P7'＝起動ログの逐語（`lab/out/server-stderr.log`）

```
INFO:     Started server process [57792]
INFO:     Waiting for application startup.
INFO:irodori_openai_tts.app:voices directory: voices
INFO:irodori_openai_tts.app:preload enabled; loading runtime during startup
INFO:irodori_openai_tts.runtime:loading runtime
INFO:irodori_openai_tts.runtime:downloading checkpoint assets from hf://Aratako/Irodori-TTS-v4-Small
INFO:irodori_openai_tts.runtime:checkpoint download/cache lookup completed in 0.07s
INFO:irodori_openai_tts.runtime:checkpoint resolved: C:\Users\mugonkun\.cache\huggingface\hub\models--Aratako--Irodori-TTS-v4-Small\snapshots\4c92c7ee2bb15c19a97cf4e86d24fd6bf33b0135\model.safetensors
...(weight_norm の FutureWarning ／ MIOpen: Warning [IsEnoughWorkspace] ×4)...
INFO:irodori_openai_tts.runtime:runtime loaded in 25.41s
INFO:     Application startup complete.
INFO:     Uvicorn running on http://0.0.0.0:8088 (Press CTRL+C to quit)
```

**依頼が求めた `model_device=` 行は、起動時ではなく合成ごとに出る**（逐語・P8 の 1 射より）:

```
INFO:irodori_openai_tts.app:speech synthesis started: model=irodori-tts voice=request format=wav chars=20
INFO:irodori_openai_tts.app:irodori runtime: [runtime] start synthesize model_device=cuda model_precision=bf16 codec_device=cuda codec_precision=bf16 silentcipher_watermark=True mode=independent seconds=None steps=40 seed=random candidates=1 decode_mode=sequential
INFO:irodori_openai_tts.app:irodori runtime: [runtime] tokenize_text: 0.6 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] prepare_reference: 0.2 ms
INFO:irodori_openai_tts.app:irodori runtime: info: predicted duration frames=82.0, scale=1.000, using_frames=82 (3.280s).
INFO:irodori_openai_tts.app:irodori runtime: [runtime] predict_duration: 48.4 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] sample_rf: 624.1 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] unpatchify_latent: 0.0 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] decode_latent (sequential): 45.3 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] silentcipher_watermark: 28.7 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] total_to_decode: 0.752 s
INFO:irodori_openai_tts.app:irodori runtime: [runtime] done synthesize
INFO:irodori_openai_tts.app:speech synthesis completed: elapsed=0.76s audio_seconds=3.28 bytes=314924 seed=8677491137018921653
```

**`model_device=cuda` を実測で確認**。段別も §2.3 の in-process bf16 ウォーム（pred 34 / rf 626 / dec 55 / wm 32 ms）とほぼ一致。

### 3.4 P0（想定外・拾い物）・P1・P2

道具＝`lab/bin/live_probe.py`（`urllib` のみ・**127.0.0.1 固定**・`voices` の CRUD は一切叩いていない）。生データ＝`lab/out/live_*.json`。

| 射 | 要求（抜粋） | ステータス | 壁時計 | 本文／ヘッダ（逐語） |
|---|---|---|---|---|
| **P0** | `{"model":"irodori","input":"こんにちは、…","irodori":{caption,no_ref:true}}` | **400** | 0.044 s | `{"error":{"message":"Unsupported model 'irodori'. Use 'irodori-tts'.","type":"invalid_request_error","param":null,"code":null}}` |
| **P1** | `{"model":"irodori-tts","input":"こんにちは、読み分けちゃんのテストです。","irodori":{"caption":"落ち着いた女性の声で、丁寧に話す。","no_ref":true}}`（**voice 欄なし**） | **200** | 2.757 s | `content-type: audio/wav` / `content-disposition: attachment; filename="speech.wav"` / `x-irodori-seed: 8612080282112996860` / `x-irodori-total-to-decode: 2.705878` / `x-irodori-messages: info: speaker conditioning is disabled for this checkpoint or request; ignoring cfg_scale_speaker. \| info: seed not specified; using random seed 8612080282112996860. \| info: predicted duration frames=82.0, scale=1.000, using_frames=82 (3.280s).` / 314,924 B ＝ PCM16・1ch・48000 Hz・**3.28 s** |
| **P2** | 同上から `no_ref` を外す | **400** | 0.020 s | `{"error":{"message":"'No voice was provided and IRODORI_DEFAULT_VOICE is not set.'","type":"invalid_request_error","param":null,"code":null}}` |

- **P1 が 200 ＝ `voice` は必須ではない**。01 §要点 7・§1.10 U-2 の「未確認」を**実射で確定**した。`irodori.no_ref=true` を送れば voice レジストリを迂回する（`upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py:439-455`）。
  ⇒ **⒞（および現行アダプタ）は `voice` を送らなくてよい**＝利用者に voice 登録を強いる必要がない。導入の重さ（BRIEF §4-5）が 1 段減る。
- P2 の文言は **repr のシングルクォート込み**（`"'No voice was provided...'"`）＝アダプタ側で文字列一致を取るなら注意。
- P0 は依頼に無い拾い物だが、**`model` は `irodori-tts` 固定**（`/v1/models` の id）。誤ると 0.04 s で 400。

### 3.5 P3・P5（非ストリーム）と P6（2 回ずつ）

本文＝`こんにちは。今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。`（**40 字**）・`voice:"none"`・caption 同上・`no_ref:true`。

| 回 | 射 | 条件 | ステータス | 壁時計 | `x-irodori-total-to-decode` | 音声長 | bytes |
|---|---|---|---|---|---|---|---|
| 1 | P3 | 既定 chunking（`chunk_min_chars=80`） | 200 | **6.948 s** | 6.903346 | 7.40 s | 710,444 |
| 1 | P5 | `chunking_enabled:false` | 200 | 1.053 s | 1.036086 | 7.40 s | 710,444 |
| 2 | P3 | 既定 chunking | 200 | **1.203 s** | 1.179002 | 7.40 s | 710,444 |
| 2 | P5 | `chunking_enabled:false` | 200 | 1.337 s | 1.311090 | 7.40 s | 710,444 |

- **既定 chunking では分割が起きない**＝本文 40 字 < `chunk_min_chars` 80。`x-irodori-messages` は 4 射とも `predicted duration frames=185.1, scale=1.000, using_frames=185 (7.400s).` の**1 回だけ**＝**P3 と P5 はバイト数まで同一挙動**。
  ⇒ 01 §1.7 の「200 字の実測は分割済みの直列合成」の裏返し＝**80 字未満の文は既定では 1 発**。読み分けちゃんの 1 コメントは通常 80 字未満なので、**既定設定のままでは chunking は一度も効かない**。
- 1 回目の P3 が 6.948 s、以降 1.05〜1.34 s＝**この音声長（185 フレーム）を初めて見た射だけ 5〜6 倍**（01 §1.4 ①「形状チューニング」の再現）。ウォーム RTF は `1.179/7.40 = 0.159`。

### 3.6 P4＝SSE ストリーム（`first_sentence_chunk_min_chars=1`）

要求（逐語・seed 指定時）:

```json
{"model":"irodori-tts","input":"こんにちは。今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。",
 "voice":"none","stream_format":"sse",
 "irodori":{"caption":"落ち着いた女性の声で、丁寧に話す。","no_ref":true,
            "chunking_enabled":true,"chunk_min_chars":80,"first_sentence_chunk_min_chars":1,"seed":42}}
```

応答ヘッダ（逐語）＝`content-type: text/event-stream; charset=utf-8` / `cache-control: no-cache` / `x-accel-buffering: no` / `transfer-encoding: chunked`。**`x-irodori-seed` 等はストリームには付かない**（seed はイベント内）。

| 走 | seed | **初 audio_chunk** | chunk0 | chunk1 | done | 合計 |
|---|---|---|---|---|---|---|
| 1（コールド） | 未指定 | **0.987 s** | t=0.987 idx=0 seed=1261651210874899946 ttd=0.934 text=`こんにちは。`(6字) wav **2.84 s** / 272,684 B | t=8.880 idx=1 seed=4632214187121108236 **ttd=7.895** text=`今日は良い天気ですね、散歩に行きましょう。それから買い物もしましょう。`(35字) wav **5.92 s** / 568,364 B | 8.884 s | 8.884 s |
| 2 | **42** | **0.724 s** | t=0.724 idx=0 **seed=42** ttd=0.696 同上 | t=1.649 idx=1 **seed=42** ttd=0.915 同上 | 1.653 s | 1.654 s |
| 3 | 未指定 | **1.092 s** | t=1.092 idx=0 seed=3447896497605741418 ttd=1.045 | t=2.018 idx=1 seed=8391952333308436479 ttd=0.915 | 2.021 s | 2.022 s |
| 4 | **42** | **0.721 s** | t=0.721 idx=0 **seed=42** ttd=0.694 | t=1.661 idx=1 **seed=42** ttd=0.930 | 1.665 s | 1.665 s |

**読み取り（断定）**

- **`first_sentence_chunk_min_chars=1` は効く**＝先頭文だけ `こんにちは。` で切れ、残り 35 字が tail として 1 チャンク（`upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py:615,624-627`）。
- **初音までは 0.72〜1.09 s**。非ストリーム全文（ウォーム 1.20〜1.34 s）比で **0.4〜0.6 秒の短縮**。単体では小さいが、
  **chunk0 が返す音声は 2.84 秒ぶん**あるので、**再生を始めてから chunk1（+0.93 s）が届くまでに 1.9 秒の余裕**がある＝**本文が長くなるほど「初音が固定で 0.7 秒」になる**のが効く。⒞の「先読み・初音短縮」はここに乗る。
- **合計は SSE の方が遅い**（1.65 s vs 1.20 s）＝チャンクごとに `predict_duration`・`watermark` の固定費を払い直すため。かつ**チャンク合計の音声長は 2.84+5.92=8.76 s** で、1 発合成の 7.40 s より **1.36 秒長い**（各チャンクが独立に長さ予測するため）。**分割すると喋りが伸びる**＝耳検分は**未測**（音の切れ目・不自然さは聴いていない）。
- **`seed` を指定すると全チャンクが同じ seed で回る**（idx 0/1 とも 42）。未指定ならチャンクごとに別の乱数 seed。
  ⇒ ⒞で「1 発言のなかで声色を揃えたい」なら **seed 指定が必須**。逆に**同じ seed を全チャンクに使うことによる副作用**（同じ抑揚の反復）は**未確認**。
- コールド走（走 1）では chunk1 の `ttd` が **7.895 s**＝5.92 s という初見の形状を踏んだ。**SSE は「初音は速いが、未見の形状に当たったチャンクで長時間止まる」**＝配信では途中で無音が空く危険。`first_sentence_chunk_min_chars` で先頭を短くすると**先頭の形状が固定化しやすくなる**という利点は副次的にある（**未検証の推測ではなく、走 2〜4 で先頭 chunk が常に 0.69〜1.05 s に収まったのは事実**）。

### 3.7 P8＝短文 5 連射（ウォーム定常）と追加の steps 掃引

短文＝`こんにちは、読み分けちゃんのテストです。`（20 字）・`voice:"none"`・caption 同上・`no_ref:true`。音声長は 5 射とも **3.28 s**・314,924 B。

| 射 | 壁時計 | `x-irodori-total-to-decode` |
|---|---|---|
| 1 | 1.006 s | 0.952568 |
| 2 | 1.016 s | 1.009611 |
| 3 | **0.773 s** | 0.766666 |
| 4 | **0.749 s** | 0.741397 |
| 5 | **0.759 s** | 0.752468 |

- **ウォーム定常 0.749〜0.773 s**。probe の bf16 実測 **0.769〜0.782 s**（01 §1.5）と**一致**。§2.3 の in-process 値 0.749 s とも一致＝三重に裏が取れた。
- **RTF（合成）= 0.752 / 3.28 = 0.229**。HTTP 込みの壁時計でも 0.229〜0.236。

追加（依頼外・RTF 軸の材料として）＝`num_steps` を変えたウォーム 3 連射。生データ＝`lab/out/live_P9_steps.json`。

| num_steps | ttd（3 射） | ウォーム定常 | **RTF** | 音声長 |
|---|---|---|---|---|
| 40（既定） | 0.907 / 0.758 / 0.758 | **0.758 s** | **0.231** | 3.28 s（不変） |
| 20 | 0.487 / 0.474 / 0.499 | **0.487 s** | **0.148** | 3.28 s |
| 10 | 0.380 / 0.362 / 0.346 | **0.362 s** | **0.110** | 3.28 s |

40→10 で **2.1 倍速**（固定費 ≈ 0.16 s ＋ steps 比例分 0.015 s/step）。音質差は**未測**（当席は聴取不能）。

### 3.8 P6 の総括＝「1 回目は捨てる」が全射で成立

P1（2.757 s → §3.7 の定常 0.75 s）・P3（6.948 → 1.203 s）・P4（8.884 → 1.654 s）のいずれも**その音声長を初めて見た射だけ 3〜6 倍**。`IRODORI_EMPTY_CACHE_INTERVAL=0` 下でも消えない（01 §1.4 ③' はスパイクの再発を消すだけで、初見形状のコストは別物）。
⇒ **⒞の設計に「起動直後に代表的な長さを数本焼く（ウォームアップ射）」を入れる価値がある**。ただし形状は音声長（潜在フレーム数）ごとなので、**全長を焼くことはできない**（01 §1.4 ②'）。

## 4. 停止と無改変の証明

### 4.1 停止

```powershell
taskkill /T /F /PID 34716
# SUCCESS: The process with PID 43696 (child process of PID 34716) has been terminated.
# SUCCESS: The process with PID 57792 (child process of PID 34716) has been terminated.
# SUCCESS: The process with PID 34716 (child process of PID 19648) has been terminated.
```

- 8088 を LISTEN していたのは **PID 57792**（`Start-Process` が返した 34716 の子。`Get-CimInstance Win32_Process` で親子を確認済み）。`/T` で木ごと落ちた。

### 4.2 port が閉じたことの確認

```powershell
netstat -ano | Select-String ":8088\s.*LISTENING"   # -> NONE
Get-Process -Id 34716,57792 -ErrorAction SilentlyContinue  # -> both gone
```

```bash
netstat -ano | grep -E ":(8088|7861)\b.*LISTENING"   # -> BOTH CLOSED
```

### 4.3 稼働機に檔が増えていないこと

```bash
# 実験前後で同じコマンドを取り、diff
find C:/irodori-TTS-server/Irodori-TTS-Server -maxdepth 2 -not -path "*/.venv*" -not -path "*/.git/*" | sort
# -> lab/out/server_tree_before.txt と server_tree_after.txt が完全一致（"TREE IDENTICAL"）

ls -la C:/irodori-TTS-server/Irodori-TTS-Server/voices/
# -> .gitkeep のみ（2 B・mtime Aug 29 16:38）＝当便は voices に 1 檔も足していない

git -C C:/irodori-TTS-server/Irodori-TTS-Server status --short --ignored
# -> ?? irodori-server-gpu.bat / ?? irodori-server-gpu.bat.bak-lf / ?? overrides-rocm.txt
#    !! .venv-rocm/ !! .venv/ !! src/irodori_openai_tts/__pycache__/ !! src/irodori_tts_server.egg-info/
#    （いずれも当便の前から存在。mtime は §4.6 のとおり全て 2026-09-04 03:00 より前）
```

### 4.4 `voices` upload API を使っていないこと

`lab/bin/live_probe.py` が叩くのは `GET /health` と `POST /v1/audio/speech` のみ（同檔 50・138 行）。`/v1/audio/voices` は一度も呼んでいない。`/health` の `voices.files` は最後まで **0**。

### 4.5 HF キャッシュを増やしていないこと

```bash
find ~/.cache/huggingface -newermt "2026-09-04 00:00" -type f | wc -l   # -> 0
```

`HF_HUB_OFFLINE=1` を全走に付けたため、追加ダウンロードは 0。使ったのは既存 snapshot のみ
（`Aratako/Irodori-TTS-v4.1-Small` = §2、`Aratako/Irodori-TTS-v4-Small` snapshot `4c92c7ee…` = §3、`Semantic-DACVAE-Japanese-32dim` snapshot `47376ee2…`）。

### 4.6 保護対象ツリーの改変ゼロ（決定的な証明）

```bash
find C:/irodori-TTS-server -newermt "2026-09-04 03:00" -type f | wc -l   # -> 0  （.venv-rocm 込みの全走査）
find C:/IrodoriTTS         -newermt "2026-09-04 02:40" -type f | wc -l   # -> 0
```

**サーバを起動・合成 20 射以上・停止まで行って、`C:/irodori-TTS-server` 配下の檔が 1 つも変わっていない**（`PYTHONDONTWRITEBYTECODE=1` が効き `.pyc` も増えていない）。

**唯一書き換わった外部の檔**＝`C:/Users/mugonkun/.miopen/`（保護対象外・利用者プロファイル）:

| 檔 | サイズ | mtime |
|---|---|---|
| `cache/3.6.0.8d1ae90e/gfx1151_20.ukdb` | 1,961,984 B | 2026-09-04 03:08:25 |
| `db/gfx1151_20.HIP.3_6_0_8d1ae90e.ufdb.txt` | 612,150 B | 同 |
| `db/gfx1151_20.HIP.3_6_0_8d1ae90e.ufdb.txt.time` | 15 B | 同 |

⇒ **⒜専用インストーラの含意**＝ROCm 版は「初回だけ遅い」の一部がこの db の育ちに依存する。**配布物に db を同梱すれば初回体験を改善できる可能性**があるが、`gfx1151` 固有＝GPU ごとに別物（**同梱の可否は未検証**）。

## 5. 実験 3＝DirectML 用 venv の準備（第 3 便の土台）

### 5.1 構築（そのまま再現できる形）

```bash
cd C:/Users/mugonkun/source/repos/irodori-native-research
uv venv --python 3.12 lab/.venv-dml
# Using CPython 3.12.14 / Creating virtual environment at: lab/.venv-dml
uv pip install --python lab/.venv-dml/Scripts/python.exe onnxruntime-directml numpy
```

逐語（解決 129 ms・DL 23.9 MiB・導入 1.39 s・**7 パッケージ**）:

```
 + flatbuffers==25.12.19
 + mpmath==1.3.0
 + numpy==2.5.2
 + onnxruntime-directml==1.24.4
 + packaging==26.3
 + protobuf==7.36.1
 + sympy==1.14.0
```

- **onnxruntime-directml 1.24.4**（依頼の想定どおり）。11 §3 の CPU 版 `onnxruntime 1.29.0` とは**別系列・別版**＝DirectML 版は CPU 版より版が古い（**上流追随のコストとして主席へ**）。
- **wheel 単体 23.9 MiB**。venv 全体は 7 パッケージ＝⒝案の配布物見積りの下限材料（torch 431 MB に対して圧倒的に軽い）。

### 5.2 DmlExecutionProvider の確認と数値一致（`lab/bin/dml_check.py`）

```bash
"lab/.venv-dml/Scripts/python.exe" lab/bin/dml_check.py     # -> lab/out/dml_check.json
```

| 項目 | 値 |
|---|---|
| `onnxruntime.__version__` | `1.24.4` |
| `get_available_providers()` | **`['DmlExecutionProvider', 'CPUExecutionProvider']`** |
| `ort.get_device()` | `CPU-DML` |
| session の providers | `['DmlExecutionProvider','CPUExecutionProvider']` |
| session 初期化 | 0.162 s |
| **`get_provider_options()`** | **`{'DmlExecutionProvider': {}, 'CPUExecutionProvider': {}}`＝空。アダプタ名は取れない** |

`lab/out/smoke_sdpa.onnx`（11 §8 の残骸・入力 `x` `[1,4,T,16]` fp32・出力 `scaled_dot_product_attention` 同形・opset 20）を DML EP と CPU EP で走らせた:

| 形状 | CPU s | DML 初回 s | DML ウォーム s | **max abs diff** | DML の自己再現 |
|---|---|---|---|---|---|
| `[1,4,8,16]` | 0.0006 | 0.0821 | **0.0008** | **4.768e-07** | bit 一致 |
| `[1,4,23,16]`（動的軸 T=23） | 0.0003 | 0.0344 | **0.0009** | **9.537e-07** | bit 一致 |

- **動的軸つきで CPU EP と一致**（11 §8 が torch↔ORT CPU で得た 7.15e-07 と同じ桁）。**DML EP でも動的形状が通る**＝⒝の前提が 1 つ立った。
- **DML の初回は 34〜82 ms かかる**（カーネルコンパイル）が 2 発目から 0.8〜0.9 ms。**ROCm ほど酷い「初見形状で数十秒」は観測されなかった**（ただし小さいモデルでの話＝**大きいモデルでは未確認**）。

### 5.3 DML が掴んだアダプタ（`lab/bin/dml_adapter_probe.py`）

`get_provider_options()` が空なので**名前では確認できない**。この機体には仮想ディスプレイが同居している:

```powershell
Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM,DriverVersion,DriverDate
```

| Name | AdapterRAM | DriverVersion | DriverDate |
|---|---|---|---|
| **AMD Radeon(TM) 8060S Graphics** | 4,293,918,720 | **32.0.31041.1004** | 2026/08/17 |
| MS Idd Device | — | 10.52.39.100 | 2019/09/05 |
| MS Idd Device | — | 10.52.13.68 | 2019/09/05 |
| MS Idd Device | — | 10.50.47.889 | 2019/09/05 |

そこで **2048×2048 の fp32 MatMul（17.18 GFLOP/回）のスループット**で代替した（`lab/out/matmul2048.onnx` を lab/.venv の onnx で生成 → .venv-dml で実行）:

| EP | 秒/回 | **GFLOPS** |
|---|---|---|
| CPUExecutionProvider | 0.02044 | 840.4 |
| DmlExecutionProvider（既定） | 0.00673 | **2550.9** |
| DML `device_id=0` | 0.00622 | 2761.0 |
| DML `device_id=1` | 0.00610 | 2818.0 |
| DML `device_id=2` | 0.00589 | 2917.3 |
| DML `device_id=3` | 0.00611 | 2810.1 |

- **CPU EP との数値差** max abs `4.73e-04`（相対 `1.75e-06`）＝行列積の累積誤差の範囲。
- **`device_id` を 0〜3 のどれにしても同じ性能**＝ORT の DML EP は**ハードウェアアダプタしか列挙していない**（MS Idd Device は D3D12 の計算アダプタとして出てこない）。範囲外の id は既定にフォールバックしていると見る（**ORT のコードは未読＝未確認**）。
- 25 秒の連続実行（`--loop 25`）で **3,910 回**＝持続 **2,687 GFLOPS**。その間の perf カウンタ:

```powershell
(Get-Counter "\GPU Engine(*)\Utilization Percentage").CounterSamples | Where-Object { $_.CookedValue -gt 1 }
# InstanceName : pid_57592_luid_0x00000000_0x00011ec8_phys_0_eng_0_engtype_3d
# CookedValue  : 37.02
(Get-Counter "\GPU Adapter Memory(*)\Total Committed").CounterSamples
# luid_0x00000000_0x00011ec8_phys_0 -> 3403 MB   ← 動いているのはこれだけ
# luid_0x00000000_0x0001426e_phys_0 ->    1 MB
# luid_0x00000000_0x000142a4_phys_0 -> 2399 MB
```

**判定**＝(i) 3 つのアダプタのうち **1 つだけ**が DML プロセスの GPU エンジンを回し、(ii) その実効 2.7 TFLOPS は CPU の 3.2 倍、(iii) 仮想ディスプレイに 3D エンジンは無い。⇒ **DML は 8060S を掴んでいる**と実質確定してよい。ただし**「LUID 0x11ec8 = 8060S」という名前の対応は取っていない＝厳密には未確認**。第 3 便で名前まで要るなら DXGI 列挙（`IDXGIFactory::EnumAdapters` の `Description`）を C#/ctypes で読むのが最短。

## 6. 主席への注意（⒜⒝⒞の評価に効く数字）

1. **RTF 軸の正しい基準は「常駐・ウォーム・bf16」**＝短文 **0.197**／長文 **0.158**（40 steps）。CLI 1 発の値（RTF 0.8〜5.6）を比較表に載せてはいけない。CPU 基準（11 §5 の 4.06 / 3.69）との比は **20〜23 倍**。
2. **ROCm では bf16 が必須**（速度の選択肢ではない）＝fp32 は `decode_latent` が CPU より遅い（ウォームで 1.98 s vs CPU 1.02 s）。⒜⒞は bf16 既定で設計すること。**CUDA でも同じかは未確認**（NVIDIA 機なし）。
3. **⒝ONNX 化の投資先は `sample_rf`（RF DiT）一点**＝GPU bf16 ウォームで 626/749 ms＝84%。CPU 時代の「decode と duration も要る」（11 §5）は GPU では当たらない。
4. **`voice` は不要**（`no_ref:true` で 200・実射確定）。⒜の導入手順から「voice を登録させる」段を落とせる。
5. **SSE の初音は 0.72〜1.09 s で固定**、先頭チャンクが 2.84 秒ぶんの音声を返す＝⒞の「先読み」は成立する。ただし**合計は 0.4 秒遅くなり、喋りが 1.36 秒伸びる**（チャンクごとに長さ予測が独立）＝**耳検分は未測**。ここは司令官の聴取が要る。
6. **`seed` 指定で全チャンクが同一 seed**＝声色を揃えるなら必須のノブ。⒞の API 設計に入れること。
7. **VRAM は bf16 で 3.08 GB 一定**（長さに依らない）。fp32 は 4.5〜5.9 GB。dGPU 8 GB 機なら bf16 で余裕、fp32 は長文で苦しい。**ただし本機は統合メモリ（`mem_get_info` は 100 GiB を返す）＝dGPU での実測ではない**。
8. **`/health` は「実際に GPU を掴んだか」を答えない**（`model_device: "auto"` のまま）。⒞の薄い層は自前で健康診断の一射を打つ必要がある。ログ（stderr）には `model_device=cuda` が毎射出る。
9. **サーバの起動は 25.4 s**（PRELOAD・bf16・v4-Small）。01 §1.5 の 31.2 s より速い（同じ機体・別日）。⒜の「初回は 30 秒待たせる」説明は依然要る。
10. **DirectML は動く**（1.24.4・数値一致 4.8e-07・動的軸 OK・wheel 23.9 MiB）。⒝の実行基盤としての一次障壁は無い。**ただし ONNX Runtime CPU 版は 1.29 で DirectML 版は 1.24＝版が揃わない**＝上流追随の保守コストとして比較表に載せること。
11. **サーバは 8088 も 7861 も落としたままにしてある**（§4.2）。司令官が配信に戻る前に `irodori-server-gpu.bat` を起こす必要がある。
