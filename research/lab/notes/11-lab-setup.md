# 11 — lab 実験環境の構築と CPU 推論の基準測定

> 席＝読解サブ席（opus）／担当＝lab 環境と CPU 基準測定（第 2 便の ONNX 変換実験の土台）。
> 実施日 2026-09-04。GPU 未使用（torch は CPU ビルド）。稼働中の 8088/7861 には触れていない。
> upstream/ ・ yomiwakechan2/ ・ C:/IrodoriTTS/ ・ HF キャッシュは 1 バイトも変更していない（§9 で確認）。

## 要点（10 行以内）

1. `lab/.venv`（CPython 3.12.14・**1102 MB**）を作り、`PYTHONPATH` で upstream を読む形（editable install なし）で `irodori_tts` の import と CPU 合成に成功した。
2. **torch 2.10.0+cpu / torchaudio 2.10.0+cpu** が入る。`--index-url .../whl/cpu` の一発で解決（版制約の緩和は不要だった）。
3. **Windows の門は torchcodec**＝torchaudio 2.10 の `save` は torchcodec 専用で、torchcodec 0.10 は FFmpeg 共有ライブラリ 4〜8 を要求する。司令官機の ffmpeg は **9.0.1 の static build** なので DLL が無く `import torchcodec` 自体が落ちる。**合成そのものは成功し、`save_wav` だけが落ちる**。
4. 回避＝`lab/bin/infer_cpu.py`（自作シム）で `torchaudio.save` を soundfile 実装に差し替えてから upstream の `infer.py` を実行する。upstream は無改変。
5. 基準測定（短文 3.76 s・48 kHz・seed 1234・OMP=8）＝**num_steps 40 で合成 15.26 s（RTF 4.06）／10 で 6.73 s（RTF 1.79）**。モデル読み込みは別に約 11.3 s。
6. 長文（10.92 s）＝**40 で 40.29 s（RTF 3.69）／10 で 14.05 s（RTF 1.29）**。ピーク RSS は 6.3〜7.1 GB。
7. スレッドは 16 で頭打ち（8→16 で sample_rf 12.85→10.39 s、16→32 は変化なし）。**同一 seed・同一スレッド数なら wav がバイト一致するが、スレッド数を変えると一致しない**＝ONNX 照合の基準は「スレッド数も固定」で採る。
8. **onnx 系は素直に入らない**＝`dacvae`→`descript-audiotools` が `protobuf<3.20` を釘打ちし onnx（`protobuf>=4.25.1`）と衝突する。`--no-deps` で onnx/onnxscript/onnxruntime/onnx_ir を入れ protobuf を 7.36.1 に上げると**両立し、合成結果も bit 一致のまま**だった（§7）。
9. torch 2.10 の dynamo exporter は動く＝SDPA を含む小モジュールを **opset 20** で出力し、動的軸つきで onnxruntime 1.29（CPU）が一致再現した（最大差 7.2e-7）。
10. **Windows の落とし穴 2 つ目**＝exporter の進捗表示に絵文字が入るため cp932 コンソールで `UnicodeEncodeError`。`PYTHONIOENCODING=utf-8` が必須（§10 の環境変数一式に収録）。

---

## 1. upstream 側の前提（原文引用）

| 事実 | 根拠 |
|---|---|
| clone の HEAD | `upstream/Irodori-TTS` = `8224dafb46d0aba89209a8f905f1cb7e3299d9c1`（2026-08-11 "Update default model to v4.1-Small"）／`upstream/Irodori-TTS-Server` = `841fb7c6ec57729c56b9b75c0ef2562249b13a10`（2026-08-02） |
| 要求 Python | `upstream/Irodori-TTS/pyproject.toml:7` `requires-python = ">=3.10"`、`upstream/Irodori-TTS/.python-version` は `3.10` |
| torch 系の版 | `upstream/Irodori-TTS/pyproject.toml:21-23` `"torch>=2.10.0"` / `"torchaudio>=2.10.0"` / `"torchcodec>=0.10.0,<0.11.0"` |
| sentencepiece の釘 | `upstream/Irodori-TTS/pyproject.toml:19` `"sentencepiece>=0.1.99,<0.2"` |
| CPU/CUDA/ROCm/XPU の分岐 | `upstream/Irodori-TTS/pyproject.toml:31-57`（extras `cpu` / `cu128` / `rocm` / `xpu`）＋ `:96-114`（4 つの explicit index）。**rocm は `marker = "sys_platform == 'linux'"`**（`:76`, `:82`）＝Windows では ROCm extra から torch を引けない |
| dacvae の取得元 | `upstream/Irodori-TTS/pyproject.toml:94` `dacvae = { git = "https://github.com/facebookresearch/dacvae" }`（版指定なし＝**commit を釘打ちしていない**） |
| silentcipher の取得元 | `upstream/Irodori-TTS/pyproject.toml:20` `"silentcipher @ git+https://github.com/SesameAILabs/silentcipher.git@d46d7d0893a583d8968ab3a6626e2289faec9152"`（commit 釘あり） |

**依頼手順との差分（記録）**

- 手順では `sentencepiece==0.2.1` を指定されたが、これは **upstream の釘 `<0.2` に反する**。0.1.x に cp312 wheel が無いため 3.12 の lab では 0.2.1 を採用した（合成は正常）。製品化の際は「Python 3.10/3.11 なら upstream の釘のまま／3.12 以上なら 0.2 系に上げる必要がある」＝**upstream の pin は 3.12 で成立しない**ことを主席へ。
- `datasets` / `gradio` / `wandb` / `torchdata` は入れていない。`infer.py` 経路では一度も要求されなかった。
- `torchcodec` は入れて壊れることを確認したのち**アンインストール済み**（§4）。

## 2. 構築手順（そのまま再現できる形）

```bash
# 0) 変数
PY=C:/Users/mugonkun/source/repos/irodori-native-research/lab/.venv/Scripts/python.exe
LAB=C:/Users/mugonkun/source/repos/irodori-native-research/lab

# 1) venv（uv 0.12.7 / CPython 3.12.14 を取得）
uv venv --python 3.12 "$LAB/.venv"

# 2) CPU 版 torch（22.5 s・torch 108.4 MiB のみ DL）
uv pip install --python "$PY" --index-url https://download.pytorch.org/whl/cpu \
  "torch>=2.10,<2.11" "torchaudio>=2.10,<2.11"

# 3) 残りの依存（onnx 系は除く＝§7 の衝突のため後入れ）
uv pip install --python "$PY" \
  "dacvae @ git+https://github.com/facebookresearch/dacvae" \
  "silentcipher @ git+https://github.com/SesameAILabs/silentcipher.git@d46d7d0893a583d8968ab3a6626e2289faec9152" \
  "transformers>=5.12.1,<6" "sentencepiece==0.2.1" \
  safetensors huggingface-hub soundfile numba llvmlite pyyaml tqdm peft psutil

# 4) onnx 系（--no-deps ＋ 依存の手当て。§7 参照）
uv pip install --python "$PY" --no-deps onnx onnxscript onnxruntime onnx_ir
uv pip install --python "$PY" "protobuf>=6.31.1" ml_dtypes coloredlogs flatbuffers packaging

# 5) 動作確認
PYTHONPATH=C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS \
PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1 \
"$PY" -c "import irodori_tts, torch; print(torch.__version__)"
# -> torch 2.10.0+cpu ／ irodori_tts は upstream の __init__.py を指す（editable install なし）
```

手順 2 は **版制約の緩和なしで一発成立**した。所要 22.5 s、11 パッケージ。
手順 3 は 12.4 s。git 依存 2 件はソースからビルドされ wheel 化に成功。

## 3. 入ったもの（主要版と git commit）

| 種別 | パッケージ | 版 / commit |
|---|---|---|
| 実行系 | torch | `2.10.0+cpu` |
| 実行系 | torchaudio | `2.10.0+cpu` |
| 実行系 | transformers | `5.16.1` |
| 実行系 | tokenizers | `0.23.2` |
| 実行系 | sentencepiece | `0.2.1`（※upstream 釘は `<0.2`） |
| 実行系 | safetensors | `0.8.0` |
| 実行系 | huggingface-hub | `1.30.0` |
| 実行系 | soundfile | `0.14.0` |
| 実行系 | numba / llvmlite | `0.67.0` / `0.49.0` |
| 実行系 | peft | `0.20.0` |
| コーデック | **dacvae** | `1.0.0`（git commit **`414c20785fc3a28373073ea8ef7a1316eeeaca6e`**）※upstream は commit を釘打ちしていないので、この番号を第 2 便の基準にする |
| コーデック従属 | descript-audiotools | `0.7.2`（`protobuf<3.20` を要求＝§7 の衝突源） |
| 透かし | **silentcipher** | dist は `1.0.5`（git commit `d46d7d0893a583d8968ab3a6626e2289faec9152`＝upstream 釘と一致）。ただし `silentcipher.__version__` は `1.0.4` を返す＝メタデータと自己申告が不一致 |
| ONNX | onnx / onnx-ir / onnxscript / onnxruntime | `1.22.0` / `1.0.0` / `0.7.1` / `1.29.0` |
| ONNX 従属 | protobuf | `7.36.1`（既定解決の `3.19.6` から昇格） |
| 未導入 | torchcodec | §4。`0.10.0` を入れて import 不能を確認後、削除 |
| 未導入 | datasets / gradio / wandb / torchdata / torchao | `infer.py` 経路では不要 |

- venv 実測 **1102 MB**。内訳上位＝`torch` 431 MB・`llvmlite` 116 MB・`scipy` 87 MB・`transformers` 53 MB・`onnx` 44 MB・`onnxruntime` 40 MB・`sympy` 29 MB・`sklearn` 29 MB・`jedi` 27 MB・`matplotlib` 23 MB。
- `llvmlite`/`numba` と `scipy`/`sklearn`/`matplotlib`/`ipython` は **dacvae → descript-audiotools が引き込む**。推論に要るかは**未確認**。⒜専用インストーラの見積では「dacvae の依存を切れれば数百 MB 削れる」余地として主席へ。
- ONNX 系（onnx+onnxruntime+onnxscript ≒ 90 MB）は⒝案で torch を丸ごと外せれば大きな節約になる。**torch 431 MB → onnxruntime 40 MB**。

## 4. torchcodec ＝ Windows での最大の詰まり（逐語記録）

`infer.py` は合成を終えたあと `save_wav` で落ちた（`upstream/Irodori-TTS/infer.py:500` → `upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1535`）。

```
File "...\irodori_tts\inference_runtime.py", line 1535, in save_wav
    torchaudio.save(str(out_path), audio_cpu, sample_rate)
File "...\torchaudio\_torchcodec.py", line 248, in save_with_torchcodec
    raise ImportError(
ImportError: TorchCodec is required for save_with_torchcodec. Please install torchcodec to use this function.
```

`save_wav` の実体（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1530-1541`）は
`except RuntimeError:` で soundfile へ落ちる作りだが、**torchcodec 不在時に飛ぶのは `ImportError` なので捕まらない**＝upstream 側の取りこぼし。

`torchcodec==0.10.0` を入れて試すと今度は DLL で落ちる（逐語）:

```
OSError: Could not load this library: ...\site-packages\torchcodec\libtorchcodec_core5.dll
FileNotFoundError: Could not find module '...\libtorchcodec_core4.dll' (or one of its dependencies).
RuntimeError: Could not load libtorchcodec. Likely causes:
  1. FFmpeg is not properly installed in your environment. We support
     versions 4, 5, 6, 7, and 8, ...
```

司令官機の ffmpeg は `ffmpeg version 9.0.1-full_build-www.gyan.dev`（`--enable-static`・winget の Gyan.FFmpeg）で
**共有 DLL を持たず、かつ版 9 は torchcodec 0.10 の対応外（4〜8）**。`C:/Windows/System32` に `avutil*.dll` / `avcodec*.dll` は無い。

**⒜専用インストーラへの含意（断定）**＝Windows 配布では
(i) FFmpeg 4〜8 の **shared build の DLL 群を同梱して PATH に通す**か、
(ii) `torchaudio.save` を使わず soundfile（libsndfile 同梱・数 MB）で書く、のどちらかが要る。
(ii) の方が軽く、実際 lab はそれで通した。**上流に 1 行のパッチ（`except (RuntimeError, ImportError)`）を当てれば済む**規模＝⒜/⒞の工数見積で「上流への小パッチ 1 本」として計上できる。

### 回避シム `lab/bin/infer_cpu.py`（upstream 無改変）

`C:/Users/mugonkun/source/repos/irodori-native-research/lab/bin/infer_cpu.py`。やっていること＝
`torchaudio.save` を soundfile 実装に差し替え → `runpy.run_path(upstream/infer.py, run_name="__main__")` →
壁時計時間・`psutil` の `peak_wset`・出力 wav の SR/長さを JSON で出す。
`irodori_tts.inference_runtime` は `import torchaudio`（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:17`）してモジュール属性で呼ぶので、差し替えが効く。

```bash
"$PY" "$LAB/bin/infer_cpu.py" --json-out out/x.json -- <infer.py の引数...>
```

## 5. CPU 基準測定

共通＝`--hf-checkpoint Aratako/Irodori-TTS-v4.1-Small`・`--caption "落ち着いた女性の声で、丁寧に話す。"`・`--no-ref`・`--seed 1234`・`--model-device cpu --codec-device cpu`・fp32・`HF_HUB_OFFLINE=1`。
機体＝AMD Ryzen AI MAX+ 395（`nproc`=32）。出力は **48000 Hz / 1ch / PCM_16**（`sample_rate` は codec 由来＝`rt.codec.sample_rate = 48000`）。
※PCM_16 になるのはシムの `sf.write` 既定。upstream 本来の torchcodec 経路の bit 深度は**未確認**。

- 短文＝`こんにちは、読み分けちゃんのテストです。`（音声長 **3.76 s**・180480 frames）
- 長文＝`読み分けちゃん2 は、配信中の読み上げを担当するソフトです。今日は九番目のエンジンとして、彩り音声合成を試しています。`（音声長 **10.92 s**・524160 frames）

| 実験 | steps | OMP | predict_duration | sample_rf | decode_latent | watermark | **total_to_decode** | 壁時計(全体) | **RTF(合成)** | RTF(全体) | peak RSS |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `steps40_seed1234` | 40 | 8 | 1105 ms | 12848 ms | 1017 ms | 290 ms | **15.262 s** | 28.18 s | **4.06** | 7.49 | 6318 MB |
| `steps40_rep2` | 40 | 8 | 1265 ms | 12902 ms | 1041 ms | 302 ms | **15.512 s** | 28.03 s | **4.13** | 7.45 | 6318 MB |
| `steps10_seed1234` | 10 | 8 | 1285 ms | 4102 ms | 1034 ms | 301 ms | **6.725 s** | 19.25 s | **1.79** | 5.12 | 6323 MB |
| `steps40_t16` | 40 | 16 | 1273 ms | 10394 ms | 857 ms | 235 ms | **12.762 s** | 25.85 s | **3.39** | 6.87 | 6328 MB |
| `steps40_t32` | 40 | 32 | 1236 ms | 10395 ms | 835 ms | 224 ms | **12.692 s** | 25.71 s | **3.38** | 6.84 | 6323 MB |
| `long_steps40` | 40 | 8 | 1314 ms | 35306 ms | 2885 ms | 783 ms | **40.292 s** | 53.21 s | **3.69** | 4.87 | 7062 MB |
| `long_steps10` | 10 | 8 | 1317 ms | 9274 ms | 2688 ms | 766 ms | **14.047 s** | 26.75 s | **1.29** | 2.45 | 7093 MB |

- RTF(合成)＝`total_to_decode ÷ 音声長`（モデル読み込みを含まない＝常駐サーバでの実効値）。
- RTF(全体)＝プロセス起動〜wav 書き出しの壁時計 ÷ 音声長（毎回プロセスを起こす場合）。
- 生データ＝`lab/out/*.json`、音声＝`lab/out/*.wav`。段別時刻は `infer.py --show-timings`（既定 on・`upstream/Irodori-TTS/infer.py:339-346`）の出力そのもの。

### 読み取り（断定）

- **CPU では num_steps 40 は RTF 3.4〜4.1＝実時間の 3〜4 倍かかる。10 に落とすと 1.3〜1.8** まで下がるが、それでもリアルタイムには届かない。読み上げ用途の CPU フォールバックは「先読みして貯める」前提でしか成立しない。
- **`sample_rf`（RF Euler サンプリング）が支配的**。40 steps 短文で 12.85 s / 15.26 s ＝ **84%**。steps に対してほぼ線形（40→10 で 12.85→4.10 s、比 0.32）。⒝ONNX 化の効き目はここに集中する。
- **steps に依らない固定費が約 2.4 s ある**＝`predict_duration` 約 1.2〜1.3 s ＋ `decode_latent` 約 1.0 s ＋ `watermark` 約 0.3 s（短文）。steps=10 では固定費が全体の 36%。
  → **ONNX 化は sample_rf だけでは足りず、duration 予測とコーデック decode も移さないと頭打ちになる**（第 2 便の優先順の材料）。
- `predict_duration` は音声長にほぼ非依存（短文 1.1 s / 長文 1.3 s）＝入力テキスト長で決まる固定費。
- `decode_latent`（DACVAE）は音声長に比例（3.76 s→1.0 s、10.92 s→2.7〜2.9 s ＝ 約 0.26×音声長）。
- `silentcipher_watermark` は音声長に比例し **RTF 0.07 前後**を常に食う。`SamplingRequest`（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:201-246`）に無効化フラグは無く、`InferenceRuntime.__init__`（同 `:619`）で常時ロードされ、同 `:1433` の `if self.watermarker.ready:` で無条件に適用される。**API から切れない**＝⒞専用制御方式でも消せない固定費（切るならフォーク）。
- **モデル読み込みは 11.29 s**（`InferenceRuntime.from_key`）＋ `irodori_tts.inference_runtime` の import 3.07 s ＋ HF 解決 0.30 s。パラメータ **766.1 M**、checkpoint は fp32 の `model.safetensors` **3,064,295,596 B（2.85 GiB）**。
  → 初回起動の体感 15 秒弱。⒜インストーラの「初回だけ遅い」説明が要る。
- **メモリ**＝合成時ピーク RSS 6.3 GB（短文）／7.1 GB（長文）。ロード直後は 3.5 GB。**fp32 CPU で 8 GB 級の空きが要る**＝配布時の最低要件として主席へ。
- `latent_dim = 32`（`rt.model_cfg.latent_dim`）／`use_caption_condition = True`／`use_speaker_condition_resolved = True`（`--no-ref` を渡したので当便では speaker 条件は使っていない。`infer.py:411-421` が「speaker 対応 checkpoint は参照か `--no-ref` のどちらか必須」を強制する）。

### スレッドと再現性（断定）

- `OMP_NUM_THREADS` 8→16 で `sample_rf` 12.85→10.39 s（-19%）。16→32 は 10.39→10.40 s＝**16 で頭打ち**（16C/32T に一致）。
- sha256（`lab/out`）:

| wav | sha256 |
|---|---|
| `steps40_seed1234.wav` / `steps40_rep2.wav` | `678d2384f9017adadc9df98e9b4aa1a254218af30617590f0bea3dc7c09846bd` |
| `steps40_t16.wav` / `steps40_t32.wav` | `57cfff822532b63658cd6f79e63a51106910849d69bc5f11e8fcf7efa496fcd6` |
| `steps10_seed1234.wav` / `postonnx_steps10.wav` | `50b3f271f200ee31e09c0c56beb5594b020bb63033a7f42ec51e4dc1a9ec367e` |
| `long_steps40.wav` | `ccf53374dd1d2e2edb6a6e300a329d166242a6721823d2ea85792f690ca25a84` |
| `long_steps10.wav` | `9142235546bfb809bcf454f40016dddb49a6862606327d66dd1db852a135aea7` |

- 同一条件の繰り返しは **bit 一致（決定的）**。
- しかし OMP=8 と OMP=16/32 は**不一致**（並列縮約順の差）。
- protobuf 3.19→7.36 の昇格前後は **bit 一致**＝§7 の細工は数値に影響しない。
- **第 2 便への釘**＝ONNX 出力と torch 出力を突き合わせるときは、**seed だけでなく `OMP_NUM_THREADS` も固定**すること。基準は `OMP_NUM_THREADS=8`・`seed=1234`・`steps=40` の `678d2384…`。

## 6. デバイス選択がコード上どこで決まるか（問い 4 の下地）

| 事実 | 根拠 |
|---|---|
| 既定デバイスは「使えるものの先頭」 | `upstream/Irodori-TTS/irodori_tts/inference_runtime.py:80-93`。`list_available_runtime_devices()` が `cuda`→`mps`→`xpu`→`cpu` の順に積み、`default_runtime_device()` はその **先頭**を返す |
| CLI 既定はそれを採用 | `upstream/Irodori-TTS/infer.py:94`（`--model-device`）・`:106`（`--codec-device`）がともに `default=default_runtime_device()` |
| index 指定 | `cuda` のみ index を許す。`mps`/`xpu` は index 指定で `ValueError`（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:64-65`, `:70-71`） |
| 対応デバイスの閉じた集合 | 同 `:75-77` `Unsupported inference device=... Expected one of: cpu, cuda, mps, xpu.` |
| ROCm | コード上に専用分岐は**無い**＝ROCm は torch から `cuda` として見える前提。`upstream/Irodori-TTS/pyproject.toml:76`, `:82` で rocm index が `sys_platform == 'linux'` に限定 |
| bf16 は CPU で不可 | `upstream/Irodori-TTS/irodori_tts/inference_runtime.py:96-100` `list_available_runtime_precisions` は `cuda`/`xpu` のみ `["fp32","bf16"]`、他は `["fp32"]`。`resolve_runtime_dtype` も CPU の bf16 を `ValueError` にする → **CPU 経路で bf16 高速化は使えない** |

## 7. onnx 系の依存衝突（第 2 便が最初に踏む石）

素直に一括インストールすると解けない（逐語・抜粋）:

```
× No solution found when resolving dependencies:
  Because onnx>=1.23.0rc1 depends on protobuf>=6.31.1 and onnx>=1.18.0,<=1.22.0 depends on
  protobuf>=4.25.1, we can conclude that onnx>=1.18.0 depends on protobuf>=4.25.1.
  And because descript-audiotools>=0.7.2 depends on protobuf>=3.9.2,<3.20, we can conclude
  that descript-audiotools>=0.7.2 and onnx>=1.18.0 are incompatible.
  ...
  And because dacvae==1.0.0 depends on descript-audiotools>=0.7.2 and only dacvae==1.0.0 is
  available, we can conclude that all versions of dacvae and onnx>=1.14.0 are incompatible.
```

**解**（lab で成立を確認）:

```bash
uv pip install --python "$PY" --no-deps onnx onnxscript onnxruntime onnx_ir
uv pip install --python "$PY" "protobuf>=6.31.1" ml_dtypes coloredlogs flatbuffers packaging
```

- `onnxscript` は `onnx_ir` を要求するが `--no-deps` では入らない＝**`onnx_ir` も明示で入れる**（忘れると `ModuleNotFoundError: No module named 'onnx_ir'`）。
- 昇格後の全 import 検査（`torch`/`torchaudio`/`dacvae`/`audiotools`/`silentcipher`/`transformers`/`onnx`/`onnxruntime`/`onnxscript`/`google.protobuf`/`irodori_tts.codec`/`irodori_tts.inference_runtime`）**すべて OK**。
- 合成の再実行で **wav が bit 一致**（§5）。つまり `descript-audiotools` の `protobuf<3.20` 釘は推論経路では効いていない（tensorboard 経由の名残と見る＝**未確認**）。
- `onnxruntime.get_available_providers()` = `['AzureExecutionProvider', 'CPUExecutionProvider']`（CPU 版のため。DirectML/CUDA/ROCm EP は別 wheel）。

**主席への含意**＝⒝案では ONNX Runtime に載せ替えることで `dacvae`（＝`descript-audiotools`・`protobuf` の古釘・`numba`/`llvmlite` 116 MB）ごと依存が消える。配布物の削減幅が大きい。

## 8. ONNX exporter の素振り（第 2 便の前哨）

```python
class M(torch.nn.Module):
    def forward(self, x):
        return torch.nn.functional.scaled_dot_product_attention(x, x, x)

torch.onnx.export(M().eval(), (x,), path, dynamo=True,
                  dynamic_shapes={"x": {2: torch.export.Dim("T")}})
```

- **成功**。出力 opset = **20**（`domain: "" version: 20`）。
- onnxruntime 1.29 CPU で実行し torch と最大差 **7.15e-07**。動的軸（T=8→23）も通り、差は同じ。
- 経路は `torch.export.export(..., strict=False)` → decomposition → ONNX 変換の 3 段（exporter の進捗表示より）。
- 付随の記録＝`torchvision is not installed. Skipping torchvision::nms` 等の warning が 4 本出るが無害。
- **cp932 の罠**＝exporter は進捗に絵文字（U+2705）を出すため、既定コンソールで
  `UnicodeEncodeError: 'cp932' codec can't encode character '\u2705' in position 78: illegal multibyte sequence`
  が起き、**エクスポート成功後に落ちる**。`PYTHONIOENCODING=utf-8`（＋`PYTHONUTF8=1`）で解消。

## 9. 停止域の確認（無改変の証明）

```bash
git -C C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS status --short --ignored
# -> 出力なし（clean）
find . -name "__pycache__" -not -path "./.git/*" | wc -l   # -> 0
git -C .../upstream/Irodori-TTS-Server status --short --ignored   # -> 出力なし（clean）
```

- `PYTHONDONTWRITEBYTECODE=1` を全コマンドに付けたため **`__pycache__` は 1 つも生成されていない**。削除作業は不要だった。
- HF キャッシュは `HF_HUB_OFFLINE=1` で既存 snapshot のみ読んだ。追加ダウンロードなし。
  - `Aratako/Irodori-TTS-v4.1-Small` → `snapshots/2b28324dc263ed5e6638b3cf3dd94c82ead07b4b/model.safetensors`（2929 MB）
  - `Aratako/Irodori-TTS-v4-Small`（2929 MB・当便では未使用）
  - `Aratako/Semantic-DACVAE-Japanese-32dim` → `snapshots/47376ee24834d7a05a48ebabfe3cde29b3c5e214/weights.pth`（410 MB）
  - `sony/silentcipher` → `snapshots/a1c4d021905e0dc5b24be5f68db5fc4dba410ee1/`（68 MB。`16_khz/` と `44_1_khz/` の 2 系統）
  - ※`ckpt path or config path does not exist! Downloading the model from the Hugging Face Hub...` という silentcipher 側のメッセージが出るが、実際は**オフラインでキャッシュを解決**しており通信していない（`HF_HUB_OFFLINE=1` 下で成功したのが根拠）。
- `C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・yomiwakechan2 リポは読取のみ。8088/7861 には触れていない。GPU も使っていない（torch は CPU ビルド）。
- 当便が書いたのは `lab/.venv/`・`lab/bin/infer_cpu.py`・`lab/out/*`・`lab/notes/11-lab-setup.md` のみ。

## 10. 第 2 便（ONNX 実験席）向け・すぐ使える起動用の環境変数一式

```bash
# ---- Bash（この便で実際に使った形）----
export PY=C:/Users/mugonkun/source/repos/irodori-native-research/lab/.venv/Scripts/python.exe
export LAB=C:/Users/mugonkun/source/repos/irodori-native-research/lab
export PYTHONPATH=C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS
export PYTHONDONTWRITEBYTECODE=1   # upstream に __pycache__ を書かない（必須）
export HF_HUB_OFFLINE=1            # 既存 HF キャッシュだけ読む（必須）
export PYTHONIOENCODING=utf-8      # torch.onnx exporter の絵文字で cp932 落ちするのを防ぐ（必須）
export PYTHONUTF8=1
export PYTHONWARNINGS=ignore       # audiotools/pydub の SyntaxWarning 抑止（任意）
export OMP_NUM_THREADS=8           # 基準測定と同条件。照合時は必ず 8 に固定
export MKL_NUM_THREADS=8
```

```powershell
# ---- PowerShell ----
$env:PY="C:\Users\mugonkun\source\repos\irodori-native-research\lab\.venv\Scripts\python.exe"
$env:PYTHONPATH="C:\Users\mugonkun\source\repos\irodori-native-research\upstream\Irodori-TTS"
$env:PYTHONDONTWRITEBYTECODE="1"; $env:HF_HUB_OFFLINE="1"
$env:PYTHONIOENCODING="utf-8"; $env:PYTHONUTF8="1"; $env:PYTHONWARNINGS="ignore"
$env:OMP_NUM_THREADS="8"; $env:MKL_NUM_THREADS="8"
```

**基準となる合成コマンド（この 1 本で §5 の `steps40_seed1234` が再現する）**

```bash
"$PY" "$LAB/bin/infer_cpu.py" --json-out "$LAB/out/steps40_seed1234.json" -- \
  --hf-checkpoint Aratako/Irodori-TTS-v4.1-Small \
  --text "こんにちは、読み分けちゃんのテストです。" \
  --caption "落ち着いた女性の声で、丁寧に話す。" \
  --no-ref --seed 1234 \
  --model-device cpu --codec-device cpu \
  --num-steps 40 \
  --output-wav "$LAB/out/steps40_seed1234.wav"
# 期待値: total_to_decode = 15.3 s 前後 / 48000 Hz / 音声長 3.76 s /
#         sha256(wav) = 678d2384f9017adadc9df98e9b4aa1a254218af30617590f0bea3dc7c09846bd
```

**Python から直に触るとき（ONNX export 用の最短経路）**

```python
from irodori_tts.inference_runtime import (
    InferenceRuntime, RuntimeKey, SamplingRequest, download_hf_checkpoint,
)
ckpt = download_hf_checkpoint("Aratako/Irodori-TTS-v4.1-Small")   # 0.3 s（オフライン解決）
rt = InferenceRuntime.from_key(RuntimeKey(checkpoint=ckpt, model_device="cpu", codec_device="cpu"))
# 11.3 s。rt.model（TextToLatentRFDiT・766.1 M params）・rt.codec（DACVAECodec, sample_rate=48000）
# ・rt.tokenizer・rt.watermarker がここに揃う
```

**注意（第 2 便が踏みやすい石）**

1. `torchaudio.save` / `torchaudio.load` は Windows で使えない（torchcodec）。**soundfile を使う**。参照音声を読む実験（`--ref-wav`）も同じ理由で落ちる見込み（**未確認**）＝`upstream/Irodori-TTS/irodori_tts/inference_runtime.py` の wav 読み込み経路を先に読むこと。
2. `--model-precision bf16` は CPU で `ValueError`。CPU 実験は fp32 固定。
3. `torch.compile`（`--compile-model`・`upstream/Irodori-TTS/infer.py:217-222`）は**未検証**。Windows + CPU では Triton が無いため期待薄。
4. ピーク RSS 6〜7 GB。766 M パラメータを丸ごと export すると**さらに数 GB 積む**見込み＝export は段ごと（duration 予測 / RF backbone / DACVAE decoder）に切って行うこと。§5 の段別時刻から、効き目の順は **sample_rf ≫ decode_latent ≒ predict_duration > watermark**。
5. `lab/out/smoke_sdpa.onnx` と `smoke_sdpa.onnx.data`（0 B）は §8 の素振りの残骸。消してよい。
6. onnx 系を入れ直す場合は §7 の `--no-deps` 手順を踏むこと。素直に `uv pip install onnx` すると解決に失敗する。
