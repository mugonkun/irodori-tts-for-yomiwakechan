# 29 — 複数 GPU 時の「GPU 指定」の実射（司令官機＝AMD 8060S・ROCm・GPU 1 枚）

> 席＝第 6 次サブ席（opus）／担当＝07 ノート（読解）と 20 ノート（GPU 基準）の上に、**GPU 指定の実挙動を実射で確定する**。実施 2026-09-04。
> 使ったポートは **8091 のみ**（8088／7861 は一度も起動していない・§6-1 で LISTEN ゼロを確認）。
> 稼働機ツリー（`C:/irodori-TTS-server`・`C:/IrodoriTTS`）・upstream clone・HF キャッシュは **1 檔も変わっていない**（§6-3 の `find -newer` で 0 件）。
> 当便が書いたのは `lab/bin/{29_torch_enum.py,29_case.py}`・`lab/out/29/*`・`lab/tmp/29/`・本ノートのみ。
> 相対パス `upstream/…` ＝ `C:/Users/mugonkun/source/repos/irodori-native-research/upstream/…`、`lab/…` ＝ 同 `irodori-native-research/lab/…`。

## 要点（10 行以内）

1. **GPU index 指定は「env に書くだけ」で確かに通る**。`IRODORI_MODEL_DEVICE=cuda:0` は 200・ウォーム 0.757 s（G2）＝07 §1-3 の断定を実射で確定。
2. **範囲外 index は起動時にも要求時にも「検査されない」**。落ちるのは重みを載せる瞬間＝`inference_runtime.py:657` `model = model.to(model_device)`。逐語＝`torch.AcceleratorError: CUDA error: invalid device ordinal / GPU device may be out of range, do you have enough GPUs?`（§1-2）。
3. **落ちる場所は `IRODORI_PRELOAD` が決める**。`true`＝**起動時に死ぬ**（uvicorn `Application startup failed. Exiting.`・**exit code 3**・ポートは開かない）。`false`＝**起動は成功し、最初の合成が 500**（§1-3）。中間はない。
4. **綴り違いは全部 torch が弾く**（`cuda:-1`／`cuda:abc`＝`Invalid device string`、`CUDA:0`／`gpu`＝`Expected one of cpu, cuda, ipu, …`、`"cuda:0 "`＝末尾空白のまま `Invalid device string: 'cuda:0 '`）。**`_resolve_device` が原文を返す**（`runtime.py:102`）ため空白も大文字もそのまま torch に届く。
5. **可視化変数は Windows ROCm でも効く。ただし効くのは `HIP_VISIBLE_DEVICES` と `CUDA_VISIBLE_DEVICES` の 2 本だけ**。`ROCR_VISIBLE_DEVICES` は **torch では「個数」にしか使われず、GPU を隠せない**（`=1` でも 8060S が見えたまま 200・F5）＝07 §5(c) の想定を**訂正**。
6. **GPU を隠したときの既定（auto）の挙動は精度で真っ二つ**。`bf16`＝**起動時に `ValueError: precision='bf16' currently requires CUDA or XPU device.` で死ぬ**（見えるエラー）。`fp32`＝**黙って CPU に落ちて 200 を返す**。実測＝2 文字の入力・10 steps で **266.974 s**（GPU なら 0.78 s）＝**約 340 倍遅い**のに `/health` は `"auto"` のまま（§2-2）。
7. **`/health` は設定の生 echo であって解決後の device ではない**。`"cuda:0 "`（末尾空白）や `"gpu"` がそのまま返る（§4）。**掴んだ GPU を HTTP から知る手段は無い**。
8. **起動ログにも device は出ない**。出るのは**合成 1 発ごとの** `[runtime] start synthesize model_device=… codec_device=…` のみ。しかもこれは `self.key.model_device`＝**env の文字列の echo**（`inference_runtime.py:1053`）＝`auto` のときは `cuda` としか出ず **index は分からない**（§4-2）。
9. **model と codec を別デバイスにするのは通る**（cuda:0＋cpu＝ウォーム 1.712 s／全 GPU の 0.777 s の 2.2 倍。cpu＋cuda:0 も 200）。**跨デバイスの受け渡しは 1 箇所だけ**＝`codec.py:266` `z = latent.transpose(1,2).contiguous().to(self.device, dtype=self.dtype)`（§3）。
10. **VRAM はプロセスを殺せば全部返る**＝アダプタ committed 3,868 MB →（サーバ稼働・2 射後）7,993 MB → kill 後 **3,865 MB**（§6-2）。`torch.cuda.get_device_properties(i)` には **`uuid` と `pci_bus_id` が両方ある**（ROCm でも）＝「掴んだ GPU の同定」は torch 側では可能（§5）。

---

## 0. 道具と再現手順

### 0-1 実行系

| 役 | 実体 |
|---|---|
| サーバ／torch | `C:/irodori-TTS-server/Irodori-TTS-Server/.venv-rocm/Scripts/python.exe`（`torch 2.13.0+rocm10.0.0` / `hip 7.15.26333` / `cuda None`）＝**読むだけ・実行のみ** |
| `irodori_tts` | `PYTHONPATH=upstream/Irodori-TTS`（トレースバックで確認済み＝`...\upstream\Irodori-TTS\irodori_tts\inference_runtime.py`） |
| `irodori_openai_tts` | `.venv-rocm` 経由で `C:\irodori-TTS-server\Irodori-TTS-Server\src\irodori_openai_tts\`（editable 導入）＝トレースバックの逐語で確認 |
| ドライバ | `lab/.venv/Scripts/python.exe`（stdlib のみ） |
| CWD | `lab/tmp/29`（`.env` を置いていない＝**全既定はコード既定** `config.py:18-72`。`voices/` はここに作られる） |

共通 env＝`PYTHONDONTWRITEBYTECODE=1` `HF_HUB_OFFLINE=1` `PYTHONIOENCODING=utf-8` `PYTHONUTF8=1` `IRODORI_EMPTY_CACHE_INTERVAL=0`。

### 0-2 道具

- `lab/bin/29_torch_enum.py`＝torch の列挙・`torch.device()`／`resolve_runtime_device()`／実確保を全部通して JSON 化（§5・§2-1）。
- `lab/bin/29_case.py`＝**1 ケース＝起動→`/health`→合成 1 発（200 ならもう 1 発）→`taskkill /T /F`→`netstat` 確認**を JSON に落とす。
  ```bash
  env IRODORI_PRELOAD=true IRODORI_MODEL_DEVICE=cuda:0 \
    lab/.venv/Scripts/python.exe lab/bin/29_case.py --tag G2_m_cuda0_c_auto
  # --steps N / --no-warm / --text "…" / --synth-timeout N
  ```
  起動＝`.venv-rocm/Scripts/python.exe -m irodori_openai_tts --host 127.0.0.1 --port 8091`（`cwd=lab/tmp/29`）。
- 合成要求（全ケース共通・逐語）＝
  `{"model":"irodori-tts","input":"こんにちは、読み分けちゃんのテストです。","irodori":{"caption":"落ち着いた女性の声で、丁寧に話す。","no_ref":true,"seed":1234}}`
- 生データ＝`lab/out/29/case_<tag>.json`（要求・応答・ヘッダ・stdout/stderr 末尾 6 KB を逐語で保存）、`case_<tag>.err.log`（uvicorn の stderr 全文）、`enum_<tag>.json`。

---

## 1. (1) 範囲外 index・綴り違いの実射

### 1-1 ケース×結果（全 19 走）

`起動`＝ポート 8091 が LISTEN したか。`exit3`＝uvicorn が `Application startup failed. Exiting.` で終了（終了コード **3**）。

| # | tag | `IRODORI_MODEL_DEVICE` / `_CODEC_DEVICE` | PRELOAD | 起動 | 起動所要 | `/health` の `model_device` | 合成 1 発目 | 所要 | 落ちた場所 |
|---|---|---|---|---|---|---|---|---|---|
| 基準 | `A_auto_preload` | （未設定＝auto / auto） | true | ○ | 26.652 s | `"auto"` | **200** | 2.648 s（2 発目 **0.792 s**） | — |
| 1 | `B1_cudaminus1_pre` | `cuda:-1` / — | true | **×** exit3 | 3.519 s | — | — | — | 起動時 `inference_runtime.py:56` |
| 2 | `B2_cudaabc_pre` | `cuda:abc` / — | true | **×** exit3 | 2.809 s | — | — | — | 同上 |
| 3 | `B3_CUDA0_pre` | `CUDA:0` / — | true | **×** exit3 | 3.518 s | — | — | — | 同上 |
| 4 | `B4_cuda0space_pre` | `"cuda:0 "`（末尾空白） / — | true | **×** exit3 | 3.519 s | — | — | — | 同上 |
| 5 | `B5_gpu_pre` | `gpu` / — | true | **×** exit3 | 3.519 s | — | — | — | 同上 |
| 6 | `B6_cuda1_pre` | `cuda:1` / `cuda:1` | true | **×** exit3 | **10.528 s** | — | — | — | 起動時 **`inference_runtime.py:657`**（重み移送） |
| 7 | `B7_cuda5_pre` | `cuda:5` / `cuda:5` | true | **×** exit3 | 10.527 s | — | — | — | 同上 |
| 8 | `B8_m0_c1_pre` | `cuda:0` / **`cuda:1`** | true | **×** exit3 | **15.433 s** | — | — | — | 起動時 **`codec.py:78`**（`inference_runtime.py:720` 経由） |
| 9 | `C1_cudaminus1_nopre` | `cuda:-1` / — | false | ○ | 3.520 s | **`"cuda:-1"`** | **500** | **0.134 s** | 初回要求時 |
| 10 | `C3_CUDA0_nopre` | `CUDA:0` / — | false | ○ | 2.814 s | **`"CUDA:0"`** | **500** | 0.126 s | 同上 |
| 11 | `C4_cuda0space_nopre` | `"cuda:0 "` / — | false | ○ | 2.111 s | **`"cuda:0 "`**（空白ごと返る） | **500** | 0.125 s | 同上 |
| 12 | `C5_gpu_nopre` | `gpu` / — | false | ○ | 2.813 s | **`"gpu"`** | **500** | 0.120 s | 同上 |
| 13 | `C6_cuda1_nopre` | `cuda:1` / `cuda:1` | false | ○ | 2.110 s | `"cuda:1"` | **500** | **6.070 s** | 初回要求時 `:657` |
| 14 | `C8_m0_c1_nopre` | `cuda:0` / `cuda:1` | false | ○ | 2.815 s | `"cuda:0"` / `"cuda:1"` | **500** | **11.279 s** | 初回要求時 `codec.py:78` |
| 15 | `G1_hip0_plus_cuda1` | `cuda:1` / `cuda:1` ＋ **`HIP_VISIBLE_DEVICES=0`** | false | ○ | 2.811 s | `"cuda:1"` | **500** | 5.995 s | 初回要求時 `:657` |
| 16 | `G2_m_cuda0_c_auto` | **`cuda:0`** / （未設定＝auto） | true | ○ | 23.141 s | `"cuda:0"` / `"auto"` | **200** | 2.588 s（2 発目 **0.757 s**） | — |
| 17 | `E0_m0_ccpu_bf16` | `cuda:0` / `cpu`（**精度は bf16 のまま**） | true | **×** exit3 | 2.809 s | — | — | — | 起動時 `inference_runtime.py:632`→`:310` |
| 18 | `E1_m_cuda0_c_cpu` | `cuda:0` / `cpu`（codec 精度 fp32） | true | ○ | 14.039 s | `"cuda:0"` / `"cpu"` | **200** | 2.561 s（2 発目 **1.718 s**） | — |
| 19 | `E2_m_cpu_c_cuda0` | `cpu`（fp32） / `cuda:0`（bf16） | true | ○ | 38.585 s | `"cpu"` / `"cuda:0"` | **200** | **168.392 s**（10 steps） | — |

### 1-2 逐語（stderr・PRELOAD=true のとき）

**範囲外 index（B6・`cuda:1`）**＝重みを載せる瞬間に落ちる。前段の `checkpoint resolved:` までは通っている。

```
INFO:irodori_openai_tts.runtime:checkpoint resolved: C:\Users\mugonkun\.cache\huggingface\hub\models--Aratako--Irodori-TTS-v4-Small\snapshots\4c92c7ee2bb15c19a97cf4e86d24fd6bf33b0135\model.safetensors
ERROR:    Traceback (most recent call last):
  File "...\starlette\routing.py", line 648, in lifespan
  File "...\contextlib.py", line 210, in __aenter__
  File "C:\irodori-TTS-server\Irodori-TTS-Server\src\irodori_openai_tts\app.py", line 115, in lifespan
    startup()
  File "C:\irodori-TTS-server\Irodori-TTS-Server\src\irodori_openai_tts\app.py", line 108, in startup
    runtime_manager.get()
  File "C:\irodori-TTS-server\Irodori-TTS-Server\src\irodori_openai_tts\runtime.py", line 48, in get
    self._runtime = InferenceRuntime.from_key(
  File "...\upstream\Irodori-TTS\irodori_tts\inference_runtime.py", line 657, in from_key
    model = model.to(model_device)
  File "...\torch\nn\modules\module.py", line 1369, in convert
    return t.to(
torch.AcceleratorError: CUDA error: invalid device ordinal
GPU device may be out of range, do you have enough GPUs?
For more detailed error information, run with CUDA_LOG_FILE=stderr
Device-side assertion tracking was not enabled by user.

ERROR:    Application startup failed. Exiting.
```

**codec だけ範囲外（B8・`model=cuda:0` / `codec=cuda:1`）**＝**モデルは cuda:0 に載りきってから** codec の読込で落ちる（起動 15.4 s＝B6 の 10.5 s より長い）。

```
  File "...\upstream\Irodori-TTS\irodori_tts\inference_runtime.py", line 720, in from_key
  File "...\upstream\Irodori-TTS\irodori_tts\codec.py", line 78, in load
    ...
torch.AcceleratorError: CUDA error: invalid device ordinal
```

**綴り違い（B1〜B5）**＝すべて `inference_runtime.py:56` `resolved = torch.device(device)` で即死。逐語は 4 種類：

| 入力 | RuntimeError の逐語 |
|---|---|
| `cuda:-1` | `Invalid device string: 'cuda:-1'` |
| `cuda:abc` | `Invalid device string: 'cuda:abc'` |
| `"cuda:0 "` | **`Invalid device string: 'cuda:0 '`**（末尾空白が引用符の内側に見える） |
| `CUDA:0` | `Expected one of cpu, cuda, ipu, xpu, mkldnn, opengl, opencl, ideep, hip, ve, fpga, maia, xla, lazy, vulkan, mps, meta, hpu, mtia, privateuseone device type at start of device string: CUDA` |
| `gpu` | 同上・末尾が `... device string: gpu` |

### 1-3 PRELOAD=false のときの HTTP 応答（逐語・全文）

いずれも **`500`**・`content-type: application/json`。`unhandled_exception_handler`（`app.py:158-160`）が `str(exc)` を素通しするので、**torch の生メッセージが改行込みで利用者に届く**。

```json
{"error":{"message":"Invalid device string: 'cuda:-1'","type":"server_error","param":null,"code":null}}
{"error":{"message":"Invalid device string: 'cuda:0 '","type":"server_error","param":null,"code":null}}
{"error":{"message":"Expected one of cpu, cuda, ipu, xpu, mkldnn, opengl, opencl, ideep, hip, ve, fpga, maia, xla, lazy, vulkan, mps, meta, hpu, mtia, privateuseone device type at start of device string: CUDA","type":"server_error","param":null,"code":null}}
{"error":{"message":"CUDA error: invalid device ordinal\nGPU device may be out of range, do you have enough GPUs?\nFor more detailed error information, run with CUDA_LOG_FILE=stderr\nDevice-side assertion tracking was not enabled by user.","type":"server_error","param":null,"code":null}}
```

**落ちる場所のまとめ（断定）**

| 誤りの種類 | PRELOAD=true | PRELOAD=false | 検出までの時間 |
|---|---|---|---|
| 綴り違い（`cuda:-1`・`CUDA:0`・`gpu`・末尾空白） | **起動時に死ぬ**（exit 3・ポート開かず） | 起動は成功・**初回要求で 500** | **0.12〜0.13 s**（モデル読込前） |
| 範囲外 index（`cuda:1`・`cuda:5`） | **起動時に死ぬ**（exit 3・起動 10.5 s＝重み読込後） | 同上・初回要求で 500 | **6.07 s**（model 側）／**11.28 s**（codec 側）＝**重みを読み終えてから** |
| bf16 × CPU（可視 GPU なし／codec=cpu） | **起動時に死ぬ**（exit 3・2.8〜4.9 s） | （未実射・**未確認**。`:628`/`:632` は同じ関門なので同じ形になるはず） | 2.8 s（読込前） |

⇒ **⒞ の薄い層は `IRODORI_PRELOAD=true` を既定にすべき**。false だと「サーバは立っているのに全部の合成が 500」という一番わかりにくい壊れ方になる（しかも範囲外 index では 1 発ごとに 6〜11 秒待たされる）。

---

## 2. (2) 可視化変数（`*_VISIBLE_DEVICES`）

### 2-1 torch 側の効き（`lab/out/29/enum_*.json`・`lab/tmp/29/mini.py`）

| 設定 | `_parse_visible_devices()` | `is_available()` | `device_count()` | `get_device_name(0)` | `list_available_runtime_devices()` | `default_runtime_device()` |
|---|---|---|---|---|---|---|
| （すべて未設定） | `[0,1,…,63]` | True | **1** | `AMD Radeon(TM) 8060S Graphics` | `["cuda","cpu"]` | `cuda` |
| `HIP_VISIBLE_DEVICES=` | `[]` | **False** | **0** | `RuntimeError: No CUDA GPUs are available` | `["cpu"]` | **`cpu`** |
| `HIP_VISIBLE_DEVICES=0` | `[0]` | True | 1 | 8060S | `["cuda","cpu"]` | `cuda` |
| `HIP_VISIBLE_DEVICES=1` | `[1]` | **False** | **0** | 同上エラー | `["cpu"]` | **`cpu`** |
| `HIP_VISIBLE_DEVICES=abc` | `[]` | False | 0 | — | — | — |
| `HIP_VISIBLE_DEVICES=-1` | `[]` | False | 0 | — | — | — |
| **`CUDA_VISIBLE_DEVICES=`** | `[]` | **False** | **0** | 同上エラー | `["cpu"]` | **`cpu`** |
| **`CUDA_VISIBLE_DEVICES=0`** | `[0]` | True | 1 | 8060S | — | — |
| **`CUDA_VISIBLE_DEVICES=1`** | `[1]` | **False** | **0** | 同上エラー | `["cpu"]` | **`cpu`** |
| `ROCR_VISIBLE_DEVICES=` | **`[0]`** | **True** | **1** | 8060S | `["cuda","cpu"]` | `cuda` |
| `ROCR_VISIBLE_DEVICES=0` | `[0]` | True | 1 | 8060S | — | — |
| **`ROCR_VISIBLE_DEVICES=1`** | **`[0]`** | **True** | **1** | **8060S（隠れない）** | `["cuda","cpu"]` | `cuda` |
| `ROCR_VISIBLE_DEVICES=0,1` | `[0,1]` | True | **1**（raw で切詰め） | 8060S | — | — |
| `CUDA=1` ＋ `HIP=0` | `[0]` | True | 1 | 8060S | — | — |
| `CUDA=0` ＋ `HIP=1` | `[1]` | **False** | **0** | — | — | — |
| `HIP=0,1` ＋ `ROCR=0` | **`RuntimeError`** | **True** | **`RuntimeError`** | — | — | — |

- **HIP が CUDA に勝つ**（`CUDA=1`＋`HIP=0` で見える／`CUDA=0`＋`HIP=1` で消える）。根拠＝`.venv-rocm/.../torch/cuda/__init__.py:894-895` `elif hip_devices is not None: var = hip_devices`。
- **`CUDA_VISIBLE_DEVICES` は HIP／ROCR がどちらも未設定なら素通しで効く**（`:872` `var = os.getenv("CUDA_VISIBLE_DEVICES")` が上書きされないため）。**07 §5(c) の「hip 分岐で上書きされる」は誤り＝訂正**。
- **`ROCR_VISIBLE_DEVICES` は torch では「個数」しか見ない**＝`:882-893` `rocr_count = len(rocr_devices.split(",")); … return list(range(rocr_count))`。だから `=1` でも `[0]`（＝1 枚見える）になり、**GPU を隠す用途には使えない**。本機では**ドライバ層でも隠れなかった**（`get_device_name(0)` が 8060S を返した）。
- **両方立てると壊れる**＝`HIP_VISIBLE_DEVICES=0,1` ＋ `ROCR_VISIBLE_DEVICES=0` で逐語 `RuntimeError: HIP_VISIBLE_DEVICES contains more devices than ROCR_VISIBLE_DEVICES`（`:887-889`）。しかも **`is_available()` は True を返すのに `device_count()` が例外を投げる**という食い違いが出る（`is_available` はこの python 側パースを通らないため）。**⒞ が「GPU があるか」を `is_available()` で判定すると騙される**。

### 2-2 サーバ既定（device 未指定＝auto）の挙動

| # | tag | 可視化変数 | 精度 | 起動 | 合成 | `/health` | 合成ログの `model_device=` |
|---|---|---|---|---|---|---|---|
| F1 | `F1_hipvd_empty_auto_bf16` | `HIP_VISIBLE_DEVICES=` | bf16 | **× exit3**（4.918 s） | — | — | — |
| F2 | `F2_cvd_empty_auto_bf16` | `CUDA_VISIBLE_DEVICES=` | bf16 | **× exit3**（4.918 s） | — | — | — |
| F3 | `F3_hipvd_1_auto_bf16` | `HIP_VISIBLE_DEVICES=1` | bf16 | **× exit3**（4.918 s） | — | — | — |
| F4 | `F4_cvd_1_auto_bf16` | `CUDA_VISIBLE_DEVICES=1` | bf16 | **× exit3**（4.919 s） | — | — | — |
| F5 | `F5_rocr_1_auto_bf16` | `ROCR_VISIBLE_DEVICES=1` | bf16 | ○（37.185 s） | **200** 3.958 s／2 発目 **1.200 s** | `"auto"` | **`cuda`**（＝隠れていない） |
| F6 | `F6_hipvd_0_auto_bf16` | `HIP_VISIBLE_DEVICES=0` | bf16 | ○（37.179 s） | **200** 3.948 s／2 発目 **1.184 s** | `"auto"` | `cuda` |
| F7 | `F7_hipvd_empty_auto_fp32` | `HIP_VISIBLE_DEVICES=` | **fp32** | ○（17.542 s） | **200** **266.974 s**（入力 `あ。`・10 steps） | **`"auto"`** | **`cpu`** |

**bf16 で GPU が消えたときの逐語（F1・F4 共通）**＝

```
  File "...\upstream\Irodori-TTS\irodori_tts\inference_runtime.py", line 628, in from_key
    model_dtype = resolve_runtime_dtype(
  File "...\upstream\Irodori-TTS\irodori_tts\inference_runtime.py", line 310, in resolve_runtime_dtype
    raise ValueError("precision='bf16' currently requires CUDA or XPU device.")
ValueError: precision='bf16' currently requires CUDA or XPU device.

ERROR:    Application startup failed. Exiting.
```

**fp32 のときは警告 1 行も出ない**（F7 の stderr に device 由来のログはゼロ）。合成ログの逐語＝

```
[runtime] start synthesize model_device=cpu model_precision=fp32 codec_device=cpu codec_precision=fp32 silentcipher_watermark=True mode=independent seconds=None steps=10 seed=1234 candidates=1 decode_mode=sequential
```

⇒ **配布物で「GPU を掴めていない」事故が一番危ないのは fp32 のとき**。起動も `/health` も 200 のまま、体感だけが数百倍遅くなる。**bf16 既定（20 §6-2 の結論）は、速度だけでなく「掴み損ねを起動時に爆発させる安全装置」としても正しい**。

---

## 3. (3) model と codec を別デバイスに

### 3-1 実射

| 組合せ | 精度 | 起動 | 合成 1 発目 | **2 発目（ウォーム）** | 段別（ウォーム・逐語） |
|---|---|---|---|---|---|
| `model=auto(cuda)` / `codec=auto(cuda)`（基準・A） | bf16/bf16 | 26.652 s | 200・2.648 s | **0.792 s** | pred 43.9 / rf 649.6 / **dec 49.7** / **wm 30.8** ms・ttd **0.777 s** |
| **`model=cuda:0` / `codec=cpu`**（E1） | bf16/**fp32** | 14.039 s | 200・2.561 s | **1.718 s** | pred 48.2 / rf 648.3 / **dec 764.4** / **wm 247.0** ms・ttd **1.712 s** |
| **`model=cpu` / `codec=cuda:0`**（E2） | **fp32**/bf16 | 38.585 s | 200・**168.392 s**（10 steps） | （未測・時間の都合） | pred **21539.8** / rf **144644.4** / dec 465.9 / wm 1707.5 ms・ttd **168.363 s** |
| `model=cuda:0` / `codec=cpu`（**精度を直さず bf16 のまま**・E0） | bf16/bf16 | **× exit3**（2.809 s） | — | — | `:632` → `:310` `ValueError: precision='bf16' currently requires CUDA or XPU device.` |

- **`codec=cpu` の代償は 2.2 倍**（0.777 → 1.712 s）。内訳は `decode_latent` が **49.7 → 764.4 ms**（15 倍）、`silentcipher_watermark` が **30.8 → 247.0 ms**（8 倍）。
  **透かし器が codec_device に張り付く**（`inference_runtime.py:619` `SilentCipherWatermarker(device=str(self.codec_device))`）ことが実測で確認できた＝07 §5 D6 の**裏取り**。
- **`model=cpu` は実用外**＝10 steps ですら 168 s。`predict_duration` だけで 21.5 s。
- **精度は device ごとに必ず組で直す**。`codec_device=cpu` にしたら `IRODORI_CODEC_PRECISION=fp32` が必須（E0 が実証）。

### 3-2 潜在の受け渡しはどこで `.to()` されるか（檔:行）

| 段 | 檔:行 | 逐語 | どの device に居るか |
|---|---|---|---|
| DiT の出力（潜在 `z`） | `inference_runtime.py:1382-1390` | `z = unpatchify_latent(z_patched, …)` | **`model_device`** |
| **跨デバイスの唯一の受け渡し** | **`codec.py:266`** | `z = latent.transpose(1, 2).contiguous().to(self.device, dtype=self.dtype)  # (B, D, T)` | `model_device` → **`codec_device`**（`self.device` は `codec.py:110` の `device=torch.device(device)`＝dataclass の欄） |
| デコード呼び出し（batch） | `inference_runtime.py:1395` | `audio_batch = self.codec.decode_latent(z).cpu()` | 出力は即 **CPU** |
| デコード呼び出し（sequential・既定） | `inference_runtime.py:1414` | `audio_i = self.codec.decode_latent(z[i : i + 1]).cpu()[0]` | 同上 |
| 透かし | `inference_runtime.py:1433-1436` → `watermark.py:71` | `vector.to(self.model.device)`（`self.model.device` は silentcipher が持つ**文字列** `str(codec_device)`） | CPU → **`codec_device`** → `watermark.py:76` `device="cpu"` で戻る |

⇒ **潜在は `codec.py:266` の 1 行だけで跨ぐ**。`.to()` は device と dtype を同時に直すので、**model が bf16・codec が fp32 でも成立する**（E1 が実証）。逆に言えば **⒞ が「model と codec を別 GPU に置く」設計を採っても、コード改造は 1 行も要らない**（実射は 1 枚機ゆえ `cuda:0`＋`cpu` でしか確かめていない＝**2 枚機での `cuda:0`＋`cuda:1` は未確認**）。

---

## 4. (4) `/health` と起動ログに出るデバイス情報

### 4-1 `/health` は「設定の生 echo」

`app.py:163-178` が返すのは `settings.*` そのもの。実射で確認した逐語（`model` 節のみ抜粋）：

| ケース | `/health` の `model` 節 |
|---|---|
| A（auto） | `"model_device":"auto","codec_device":"auto","model_precision":"bf16","codec_precision":"bf16","compile_model":false,"compile_dynamic":false` |
| C4（末尾空白） | `"model_device":"cuda:0 "` ← **空白ごと返る** |
| C5 | `"model_device":"gpu"` ← **torch が受け付けない文字列がそのまま 200 で返る** |
| C1 | `"model_device":"cuda:-1"` |
| G2 | `"model_device":"cuda:0","codec_device":"auto"` |
| E1 | `"model_device":"cuda:0","codec_device":"cpu","model_precision":"bf16","codec_precision":"fp32"` |
| F7（GPU を隠した CPU 落ち） | **`"model_device":"auto"`**（＝実体は cpu なのに分からない） |

`runtime` 節に出るのは `preload / loaded / loading / checkpoint / load_timeout / max_concurrent_synthesis / synthesis_wait_timeout` の 7 つだけ。
**index も GPU 名も precision の解決結果も出ない。`loaded:true` は「何かに載った」ことしか言わない。**

### 4-2 起動ログには device が 1 文字も出ない

成功走（F6）の startup 全文（警告行を除く・逐語）：

```
INFO:     Started server process [37512]
INFO:     Waiting for application startup.
INFO:irodori_openai_tts.app:voices directory: voices
INFO:irodori_openai_tts.app:preload enabled; loading runtime during startup
INFO:irodori_openai_tts.runtime:loading runtime
INFO:irodori_openai_tts.runtime:downloading checkpoint assets from hf://Aratako/Irodori-TTS-v4-Small
INFO:irodori_openai_tts.runtime:checkpoint download/cache lookup completed in 0.13s
INFO:irodori_openai_tts.runtime:checkpoint resolved: C:\Users\mugonkun\.cache\huggingface\hub\models--Aratako--Irodori-TTS-v4-Small\snapshots\4c92c7ee2bb15c19a97cf4e86d24fd6bf33b0135\model.safetensors
INFO:irodori_openai_tts.runtime:runtime loaded in 33.28s
INFO:     Application startup complete.
INFO:     Uvicorn running on http://127.0.0.1:8091 (Press CTRL+C to quit)
```

device が現れるのは **合成 1 発ごとの 1 行だけ**。しかもその値は `self.key.model_device`＝**env 文字列の echo**（`inference_runtime.py:1046-1056`。`self.model_device`（`torch.device`）ではない）：

| ケース | `[runtime] start synthesize …` の逐語（先頭部） |
|---|---|
| F6（`auto` ＋ HIP=0） | `model_device=cuda model_precision=bf16 codec_device=cuda codec_precision=bf16` |
| **G2**（`MODEL_DEVICE=cuda:0`・codec は auto） | **`model_device=cuda:0 … codec_device=cuda`** ← **codec だけ index 無し**＝「codec の env を忘れると別 GPU に載る」が目で見える |
| E1 | `model_device=cuda:0 … codec_device=cpu codec_precision=fp32` |
| F7 | `model_device=cpu model_precision=fp32 codec_device=cpu codec_precision=fp32` |

**結論（断定）**＝**本体側が「掴んだ GPU」を確認できる材料は、上流のままでは存在しない**。
- `/health`＝生 echo（`auto` は `auto` のまま）。
- 起動ログ＝device 無し。
- 合成ログ＝env の echo。`auto` は `cuda` としか出ず **index も GPU 名も出ない**。
- HTTP 応答ヘッダ（`x-irodori-seed` / `x-irodori-total-to-decode` / `x-irodori-messages`）にも device は無い（A の全ヘッダを `case_A_auto_preload.json` に保存済み・device 名の欄は 0 件）。

⇒ **確認できる代理指標は「合成の所要時間」だけ**（GPU 0.78 s vs CPU 267 s）。⒞ が「GPU で動いているか」を機械的に知るには、**(a) 起動時に 1 発焼いて所要を見る、(b) 層側が自分で `torch.cuda.get_device_name()` を叩く別プロセスを持つ、(c) 上流に `/health` の 1 行追加を出す**、のいずれかが要る。

---

## 5. (5) torch 側の列挙（ROCm 実測）

生データ＝`lab/out/29/enum_base.json`。

```
torch 2.13.0+rocm10.0.0 / hip 7.15.26333 / cuda None
torch.cuda.is_available() -> True
torch.cuda.device_count()  -> 1
torch.cuda.current_device() -> 0
torch.cuda.get_device_name(0) -> 'AMD Radeon(TM) 8060S Graphics'
torch.cuda.get_device_capability(0) -> (11, 5)
torch.cuda.mem_get_info(0) -> [106927665152, 107090132992]   # free, total（統合メモリ）
```

`torch.cuda.get_device_properties(0)` の `repr`（逐語）：

```
_CudaDeviceProperties(name='AMD Radeon(TM) 8060S Graphics', major=11, minor=5, gcnArchName='gfx1151', total_memory=102129MB, multi_processor_count=20, uuid=30303030-3031-3937-3030-303030303030, pci_bus_id=197, pci_device_id=0, pci_domain_id=0, L2_cache_size=2MB)
```

`dir()` に出た属性（`_` 始まり・メソッドを除く・全 22 個）と実値：

| 属性 | 値 | 属性 | 値 |
|---|---|---|---|
| `name` | `AMD Radeon(TM) 8060S Graphics` | **`uuid`** | **`30303030-3031-3937-3030-303030303030`** |
| `gcnArchName` | `gfx1151` | **`pci_bus_id`** | **`197`** |
| `major` / `minor` | `11` / `5` | `pci_device_id` / `pci_domain_id` | `0` / `0` |
| `total_memory` | `107090132992`（≒100 GiB） | `L2_cache_size` | `2097152` |
| `multi_processor_count` | `20` | `is_integrated` | **`1`** |
| `clock_rate` | `2900000` | `is_multi_gpu_board` | `0` |
| `memory_clock_rate` | `800000` | `memory_bus_width` | `512` |
| `max_threads_per_block` | `1024` | `max_threads_per_multi_processor` | `2048` |
| `regs_per_multiprocessor` | `196608` | `warp_size` | `32` |
| `shared_memory_per_block` | `65536` | `shared_memory_per_multiprocessor` | `65536` |

- **`uuid` と `pci_bus_id` は ROCm でも両方ある**（`uuid` は ASCII の `"0000019700000000"` を UUID 形式に並べたもの＝実質 pci_bus_id 由来だが、**同定に使える一意な文字列は取れる**）。
  ⇒ **⒞ が「利用者が選んだ GPU」を index ではなく名前／UUID で覚えたいなら、torch の口はある**（ただし列挙するには torch を起こす＝サーバと同じ 25〜38 秒の起動費を払うか、サーバに問い合わせる口を足すかの二択・**軽い列挙手段は未確認**）。
- `torch.cuda._device_count_amdsmi()` は **`-1`**（amdsmi が無い／Windows）＝`device_count()` は C++ 側の数を使っている。

**`torch.cuda.device(i)` と可視化の関係**（1 枚機でできる範囲）：

| 設定 | `torch.cuda.device(0)` | `torch.cuda.device(1)` |
|---|---|---|
| 未設定 | ○ `current_device()==0` | **× `AcceleratorError: CUDA error: invalid device ordinal`** |
| `HIP_VISIBLE_DEVICES=0` | ○ `current_device()==0` | **×** 同上 |
| `HIP_VISIBLE_DEVICES=`（空） | **× `RuntimeError: No CUDA GPUs are available`** | **×** 同上 |
| `HIP_VISIBLE_DEVICES=1` | **× 同上**（＝**0 番が消える**） | **×** 同上 |

⇒ **「隠すと 0 が消える」＝再番号付けが起きることの確認は取れた**（`HIP_VISIBLE_DEVICES=1` にすると、物理 1 番が無い本機ではプロセス内の見え方が空になる）。
**2 枚機で「物理 1 番がプロセス内 0 番になる」ことの直接確認は 1 枚機では不可能＝未確認**。ただし `_parse_visible_devices()` が `[1]` を返し `device_count()` がそれを raw 数で切り詰める（`torch/cuda/__init__.py:1074-1099`）実装は読めているので、2 枚機では `[1]` → 1 枚 → プロセス内 index 0 になる（**コードからの推論**）。

---

## 6. (6) 停止・ポート・VRAM

### 6-1 ポート

- **全 19 走で `taskkill /T /F /PID <launcher>` の後に 8091 の LISTENING がゼロ**（`case_*.json` の `post_netstat_8091` に `LISTENING` 行は 1 件も無い・`post_port_listening: false`）。残るのは TIME_WAIT だけ。
- **1 度だけ取りこぼした**（記録）＝`E2_m_cpu_c_cuda0` を最初に走らせた回、当席のコマンドが 10 分制限で打ち切られ、**PID 51476 が 8091 を LISTEN したまま残った**。直後に `taskkill /T /F /PID 51476` で回収し、`netstat` で消滅を確認した。以後は `--steps` / `--no-warm` を足して 10 分に収めた。
- **8088 と 7861 は本便で一度も起動していない**。最終確認（逐語）＝
  ```
  netstat -ano | grep -E ":(8088|8091|7861)\b" | grep -i listening   -> NO LISTEN on 8088/8091/7861
  ```

### 6-2 VRAM

`Get-Counter "\GPU Adapter Memory(*)\Total Committed"`（1 MB 超のみ・8060S は `luid_0x00000000_0x00011ec8_phys_0`）：

| 時点 | 8060S | 仮想ディスプレイ 2 台 |
|---|---|---|
| **起動前（idle）** | **3,868 MB** | 1 MB / 2,399 MB |
| **サーバ稼働・bf16・2 射後** | **7,993 MB**（**+4,125 MB**） | 1 MB / 2,399 MB |
| **`taskkill /T /F` の 8 秒後** | **3,865 MB**（**idle と同値**） | 1 MB / 2,399 MB |

⇒ **プロセスを殺せば VRAM は完全に返る**。この回の合成は 200・**2.662 s → 0.776 s**（20 §3.7 のウォーム 0.749〜0.773 s と一致）。
（+4.1 GB は 20 §2.5 の `max_memory_allocated` 3,079 MB より大きい＝allocator の reserved と HIP のコンテキストぶん。**内訳の切り分けは未実施**。）

### 6-3 保護対象ツリーの無改変

参照時刻＝`lab/bin/29_torch_enum.py` の mtime（`2026-09-04 17:27:28`・全サーバ走行より前）。

```bash
find C:/irodori-TTS-server -newer lab/bin/29_torch_enum.py -type f | wc -l   # -> 0
find C:/IrodoriTTS         -newer 同                        -type f | wc -l   # -> 0
find upstream/             -newer 同                        -type f | wc -l   # -> 0   （__pycache__ も 0）
find ~/.cache/huggingface  -newer 同                        -type f | wc -l   # -> 0
find C:/Users/mugonkun/.miopen -newer 同                    -type f          # -> 0 件
```

当便が作ったのは `lab/tmp/29/{mini.py, voices/}`（`voices/` は空・サーバが作った）と `lab/out/29/*`・`lab/bin/29_*.py`・本ノートのみ。

---

## 7. 07 ノート（読解便）に対する確定・訂正

| 07 の記述 | 本便の判定 |
|---|---|
| §1-3「`cuda:1` は通る（断定）」 | **確定**。`cuda:0` は 200・ウォーム 0.757 s（G2）。範囲外 index も**サーバ層では通り**、torch の `.to()` で落ちる |
| §5 D8「index の範囲検査なし＝`.to()` 時に `invalid device ordinal`」 | **確定**。逐語まで取得（§1-2）。落ちる行は **`inference_runtime.py:657`**（model）／**`codec.py:78`**（codec） |
| §1-3 注「`CUDA:1` や空白付きは torch 側で落ちる見込み・未確認」 | **確定**。`CUDA:0`＝`Expected one of cpu, cuda, …: CUDA`、`"cuda:0 "`＝`Invalid device string: 'cuda:0 '`（§1-2） |
| §5 D7「`runtime.py:99` vs `:102` の食い違い」 | **確定**（`/health` に `"cuda:0 "` が空白ごと出るのが動かぬ証拠・§4-1） |
| §5 D6「透かし器が codec_device に張り付く」 | **確定**。`codec=cpu` にすると wm が 30.8 → 247.0 ms（§3-1） |
| §5(c)「ROCm では `CUDA_VISIBLE_DEVICES` も hip 分岐で上書きされる」 | **訂正**。HIP／ROCR がどちらも未設定なら **`CUDA_VISIBLE_DEVICES` はそのまま効く**（§2-1）。効かないのは **`ROCR_VISIBLE_DEVICES`（個数しか見ない）** |
| §5(c) 注「A＋B 併用は `invalid device ordinal` になる（未実測）」 | **確定**。`HIP_VISIBLE_DEVICES=0` ＋ `IRODORI_MODEL_DEVICE=cuda:1` → 500・逐語同上（G1） |
| §1-1「resolve は `:608-609`」 | **補足**。`:608-609` は `__init__`。**`from_key` の実際の関門は `:626-627`（device）／`:628,:632`（dtype）**。トレースバックで確認 |
| §5(a)「CPU 強制には env 4 本」 | **確定**。`codec_device=cpu` ＋ `codec_precision=bf16` は起動時 `ValueError`（E0） |

---

## 8. 未確認（本便でも解けなかったもの）

1. **2 枚以上の GPU での実挙動**。本機は 1 枚＝「`cuda:1` が別 GPU に載る」「`HIP_VISIBLE_DEVICES=1` で物理 1 番がプロセス内 0 番になる」「codec だけ別 GPU」は**すべて未実射**。確かめられたのは「範囲外なら落ちる」側だけ。
2. **`torch.cuda.empty_cache()` の 0 番誤爆（07 D8/D1）**。1 枚機では 0 番＝目的の GPU なので**症状が出せない**。`IRODORI_EMPTY_CACHE_INTERVAL=0` で全走を回したため、`app.py:504-512` の経路自体を踏んでいない。
3. **bf16 × CPU を `PRELOAD=false` で踏んだときの HTTP 応答**（起動時と同じ `ValueError` が 500 で返るはず＝**未実射**）。
4. **CUDA（NVIDIA）機での同一挙動**。本ノートは全て ROCm 実測。`invalid device ordinal` の文言は torch 共通と思われるが、**NVIDIA での逐語は未確認**。
5. **`ROCR_VISIBLE_DEVICES` が 2 枚機で何をするか**。本機では隠せなかったが、これが「1 枚しかないから」なのか「Windows ROCm では効かない」のかは**切り分けていない**。
6. **VRAM +4,125 MB の内訳**（allocator reserved / HIP コンテキスト / MIOpen ワークスペースの比）。
7. **軽い GPU 列挙手段**（torch を起こさずに GPU 名・UUID を取る道）。本便は torch 経由でしか列挙していない。

---

## 9. 主席（Fable）への注意

1. **司令官の問い「複数 GPU 時の指定が可能か」への答えは “可能・ただし条件付き”**。条件は 3 つ＝**(a) Server を使う**（gradio には口が無い＝07 §5(b)）、**(b) `IRODORI_MODEL_DEVICE` と `IRODORI_CODEC_DEVICE` を必ず 2 本セットで書く**（G2 のログで codec だけ index 無しになるのが見える）、**(c) `IRODORI_PRELOAD=true` にする**（false だと誤りが「起動は成功・全合成 500」という最悪の形で出る・§1-3）。
2. **⒞ の薄い層がやるべき仕事は「env の白名簿検査」1 つに集約できる**。`^(cpu|cuda(:\d+)?)$` に合わない文字列（`gpu`・`CUDA:0`・末尾空白・`cuda:-1`）は**層で弾く**。通してしまうと torch の生メッセージ（`Expected one of cpu, cuda, ipu, xpu, mkldnn, …` の 20 語の羅列）が利用者に 500 で出る＝初心者向けの体験として成立しない（§1-3 の逐語）。
3. **範囲検査は層でやるしかない**。上流は index を一切検査せず、**重みを読み終えてから**落ちる（`cuda:1` で 6.07 s、codec 側なら 11.28 s）。層が `torch.cuda.device_count()` を知っていれば 0 秒で弾ける。
4. **「掴んだ GPU」を確認する材料は上流に無い**（§4 の結論）。⒞ が UI に「いま GPU で動いています」を出すなら、**上流へ `/health` の 1 行追加（`str(runtime.model_device)` と `get_device_name`）を出すのが最短**。出さないなら「起動時に 1 発焼いて所要で判定」しかない。**これは ⒞ の設計判断として司令官に上げる価値がある**（本便は設計を決めない）。
5. **配布物での最大の地雷は「fp32 で黙って CPU に落ちる」**（F7・**267 s / 340 倍**）。bf16 既定なら起動時に `ValueError: precision='bf16' currently requires CUDA or XPU device.` で爆発するので**むしろ安全**。20 §6-2 の「ROCm では bf16 必須」に、**「CUDA でも bf16 を既定にすべき理由がもう 1 つある」**を足せる。
6. **可視化変数で GPU を選ぶ方式（方式 A）は Windows でも成立する**が、**使ってよいのは `HIP_VISIBLE_DEVICES`（AMD）と `CUDA_VISIBLE_DEVICES`（NVIDIA・AMD 両方で効く）だけ**。`ROCR_VISIBLE_DEVICES` は罠（隠せない・HIP と併用すると `RuntimeError`）。**⒜ の起動 bat が 3 本とも立てるような書き方をしてはいけない**。
7. **`is_available()` は信用できない場面がある**＝`HIP=0,1` ＋ `ROCR=0` で `is_available()` が True を返しつつ `device_count()` が例外（§2-1）。層の健康診断は `device_count()` を使うこと。
8. **model／codec の分割は「コード改造ゼロで成立する」**（跨ぐのは `codec.py:266` の 1 行だけ）。ただし **`codec=cpu` の代償は 2.2 倍**（0.777 → 1.712 s）。VRAM を削る手段としては使えるが、**⒞ の RTF 目標には効かない**。`model=cpu` は 168 s＝論外。
9. **本便は 8091 のみ・8088／7861 は不起動・保護対象ツリーの改変ゼロ**（§6-3 の `find -newer` が全て 0 件）。**ただし E2 の 1 回だけコマンド打切りでサーバが残り、直後に回収した**（§6-1 に記録）。次便が長いケースを回すときは `--steps 10 --no-warm` か背景実行を最初から使うこと。
