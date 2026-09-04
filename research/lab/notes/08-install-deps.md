# 08 導入の重さ — 依存・配布物の大きさ・python-embed 化の可否

読解席（opus）／2026-09-04／BRIEF §4-5・§4-7 の材料。**読むだけで実施**（上流 clone・稼働機 venv・HF キャッシュへの書き込みは 0）。

**根拠パスの基点** — 断りのない相対パスは `C:\Users\mugonkun\source\repos\irodori-native-research\` から。
それ以外は次の略号を使う。

| 略号 | 実パス |
|---|---|
| `SP:` | `C:\irodori-TTS-server\Irodori-TTS-Server\.venv-rocm\Lib\site-packages\`（稼働機 ROCm venv・読むだけ） |
| `YWK:` | `C:\Users\mugonkun\source\repos\yomiwakechan2\` |
| `HF:` | `C:\Users\mugonkun\.cache\huggingface\hub\` |

---

## 要点（10 行以内）

1. 上流の導入は「git＋uv だけ」で 4〜5 手だが、**Windows で `uv sync --extra rocm` を打つと torch が PyPI の CPU 版に落ちる**（`upstream/Irodori-TTS/pyproject.toml:76` の `marker = "sys_platform == 'linux'"`／`upstream/Irodori-TTS-Server/uv.lock:99`）。前回の「Windows で ROCm が効かず CPU 合成」はこの一行が原因。
2. 推論に本当に要るのは torch・torchaudio・transformers・safetensors・huggingface_hub・soundfile・dacvae(→audiotools)・silentcipher・sentencepiece・tokenizers・numpy/scipy 程度。**gradio・wandb・datasets・peft・torchdata・numba/llvmlite・matplotlib・IPython・flask はサーバ経路で一度も import されない**（実測・§2 表）。
3. ただし `dacvae` は import した瞬間 `audiotools` を引き（`SP:dacvae/__init__.py:8`）、そこから scipy・julius・ffmpy・tensorboard・randomname まで芋づるで乗る。`silentcipher` は使いもしない `librosa` を import する（`SP:silentcipher/server.py:11`）。
4. 稼働機 ROCm venv の実測＝**4,760 MB**（うち **ROCm 固有 ≈ 3,456 MB**）。CPU venv は **1,241 MB**。CUDA cu128 Windows は lock にサイズが無く**未計測**＝第 10 席（10-gpu-landscape.md）へ委ねる。
5. モデル初回取得は **3.5 GB**＝`model.safetensors` 3,064,295,596 B ＋ codec `weights.pth` 429,620,065 B ＋ `sony/silentcipher` 68 MB ＋ tokenizer 6.7 MB。
6. **torchcodec は稼働機で壊れている**（FFmpeg 9.0.1 は torchcodec 0.10 の対応外＝4〜8）。`torchaudio.load/save` は RuntimeError を投げ、irodori 側の `except RuntimeError` が soundfile に落とすので**動いてはいる**（`upstream/Irodori-TTS/irodori_tts/inference_runtime.py:1517-1527`）。実音声 I/O は libsndfile。
7. ⒜python-embed 化は**成立の見立て**。soundfile は libsndfile を同梱、torch の DLL 探索は `os.add_dll_directory` で venv 非依存、numba/llvmlite は不使用。uv は利用者機に**不要**にできる。
8. 残る障害は 3 つ＝**MSVC ランタイム（vcruntime140/msvcp140/vcruntime140_1）**・**`._pth` の `import site` 必須**（protobuf の nspkg .pth が tensorboard 経路で要る）・**FFmpeg（mp3/opus を出すなら）**。
9. 現 venv は**再配置不能**（`pyvenv.cfg` の `home` と `__editable__…pth` が絶対パス）＝「venv を zip して配る」は不可。embed 配置に組み直す必要がある。
10. ⒜で消える詰まり所＝uv 導入・extra 選択・torch index・sentencepiece ビルド失敗・bat の CRLF・`uv run` が旧 venv を掴む。**残る**＝GPU ドライバ、3.5 GB の初回取得、ROCm を選ぶなら別配布。

---

## 1. 初心者の導入手順（README どおり・1 行 1 操作）

### 1-A. 本体（Irodori-TTS＝gradio・7861）

`upstream/Irodori-TTS/README.md:49-86, 167-190`

| # | 操作 | 必要な道具 | 詰まり所 |
|---|---|---|---|
| 1 | `git clone https://github.com/Aratako/Irodori-TTS.git`（README:52） | git | git 未導入。README に「git を入れよ」の記載は**無い** |
| 2 | `cd Irodori-TTS`（README:53） | — | — |
| 3 | `uv sync --extra cu128`（README:54・既定として提示） | uv・ネット | **uv 未導入**（README に導入手順は無い）。extra は 4 択（cpu / cu128 / rocm / xpu・README:61-71）で**排他**（pyproject.toml:62-70）＝初心者はどれを選ぶか判断できない |
| 3' | ROCm を選ぶ場合 `uv sync --extra rocm`（README:65） | 同上 | **README:64 が「AMD ROCm on Linux/WSL」と明記**。Windows で打つと torch は PyPI 版（＝CPU）に落ちる（pyproject.toml:76／uv.lock:99）＝**無言で CPU 化** |
| 4 | `uv run --no-sync python gradio_app_voicedesign.py --server-name 0.0.0.0 --server-port 7861`（README:184） | — | `--no-sync` を落とすと **再 sync が走り、選んだ backend extra が外れる**（README:80-82 が明示） |
| 5 | 初回起動でモデル自動取得（§5） | ネット 3.5 GB・空き 15 GB | 進捗が分かりにくい・HF 障害時に無言で待つ |

- 本体 README には **Requirements 節が無い**（`grep -n "^## " README.md` に該当節なし）。Python の版は `upstream/Irodori-TTS/.python-version`＝`3.10`（CRLF・`cat -A` で `3.10^M$`）と `pyproject.toml:7` `requires-python = ">=3.10"` からしか読めない。
- ffmpeg・MSVC の要求も本体 README には**書かれていない**（`grep -n "ffmpeg\|FFmpeg\|MSVC\|Visual"` は 0 件）。

### 1-B. サーバ（Irodori-TTS-Server＝OpenAI 互換・8088）

`upstream/Irodori-TTS-Server/README.md:20-82`

| # | 操作 | 必要な道具 | 詰まり所 |
|---|---|---|---|
| 0 | 前提を揃える | README:24-26 が逐語で **「- Python 3.10」「- uv」「- FFmpeg for compressed audio formats」** | Python 3.10 の明示（`.python-version`＝`3.10`）。**ROCm 版 torch は 3.11〜3.14 のみ**（YWK:probe/irodori-tts-rocm-setup-guide.md:61-62）＝**3.10 では ROCm 化できない**という矛盾 |
| 1 | `git clone https://github.com/Aratako/Irodori-TTS-Server.git`（README:39） | git | 同上 |
| 2 | `cd Irodori-TTS-Server`（README:40） | — | — |
| 3 | `uv sync --extra cu128`（README:41） | uv・ネット | 依存に `irodori-tts @ git+https://github.com/Aratako/Irodori-TTS.git`（pyproject.toml:11）＝**git が無いと sync 自体が落ちる** |
| 4 | `cp .env.example .env`（README:42） | — | Windows に `cp` は無い（`copy`）。README は bash 前提 |
| 5 | `uv run --no-sync python -m irodori_openai_tts --host 0.0.0.0 --port 8088`（README:79） | — | 同上（`--no-sync` 必須） |
| 6 | `curl http://localhost:8088/health`（README:87） | curl | — |

### 1-C. 稼働機で実際に踏んだ詰まり（ROCm 化・実績）

`YWK:probe/irodori-tts-rocm-setup-guide.md`（全 183 行・本機で全手順実行済み）

| 詰まり | 一次根拠 | 内容 |
|---|---|---|
| uv 未導入 | 同:36 `winget install astral-sh.uv` ／ 同:39「ターミナルをいったん閉じて開き直す」 | PATH が反映されない |
| Python 版が合わない | 同:57 `uv venv .venv-rocm --python 3.12` ／ 同:61-62「**Python は 3.12 を指定すること**（ROCm 版 torch は 3.11〜3.14 のみ対応。サーバ付属の古い箱は 3.10 なので使い回せない）」 | **箱を分けるしかない** |
| torch index | 同:69 `uv pip install … --extra-index-url https://stable.repo.amd.com/rocm/whl-next/ --index-strategy unsafe-best-match "torch[device-gfx1151]==2.13.0+rocm10.0.0" …` | 上流の `pytorch-rocm` index（pyproject.toml:108 `https://download.pytorch.org/whl/rocm7.1`）では Windows 用が無い＝**AMD 公式 index に差し替え**。`[device-gfx1151]` の型番も手打ち |
| sentencepiece ビルド失敗 | 同:83-90 `overrides-rocm.txt` に「`sentencepiece==0.2.1`」／逐語「**古い sentencepiece が Python 3.12 に対応しておらずエラーになるのを避けるための差し替え**」 | 上流の制約は `sentencepiece>=0.1.99,<0.2`（`upstream/Irodori-TTS/pyproject.toml:19`・`requirements.txt:11`）。実測でも CPU 箱(3.10)＝`sentencepiece-0.1.99.dist-info`／ROCm 箱(3.12)＝`sentencepiece-0.2.1.dist-info` |
| overrides を要する | 同:99 `uv pip install … --overrides overrides-rocm.txt … -e .` ／ `local-additions/Irodori-TTS-Server/overrides-rocm.txt:1-3` | サーバの `torch>=2.10.0,<2.11.0`（pyproject.toml:16）を 2.13.0+rocm10.0.0 で**押し切る**必要 |
| `uv run` が旧 venv を掴む | 同:126「**注意: `uv run` で起動してはいけない**（古い CPU の箱の方を掴んでしまう）」／`local-additions/Irodori-TTS-Server/irodori-server-gpu.bat:3` | 起動は `.venv-rocm\Scripts\python.exe` 直指定（bat:11） |
| bat の CRLF/文字コード | `local-additions/Irodori-TTS-Server/irodori-server-gpu.bat:4` 逐語「**This file must stay CRLF + ASCII: UTF-8/LF broke cmd parsing and silently dropped the set lines.**」 | UTF-8/LF だと `set` 行が**無言で落ちる**（＝bf16 指定が効かず遅くなる） |
| ドライバ | 同:27-29「AMD のグラフィックドライバ（Adrenalin）が入っていること」／同:159 | 手順 8 で `False` が出たらドライバ更新＋再起動 |
| 容量 | 同:25-26「合計 5〜6GB くらいダウンロード」「ディスク空き 15GB 以上」 | — |

---

## 2. 推論経路の依存木（実測）

### 2-A. 測り方（再現手順）

稼働機 ROCm venv の python で、`sys.modules` のトップレベル名の差分を段ごとに取った。**書き込み無し**（`PYTHONDONTWRITEBYTECODE=1`・スクリプトは scratchpad）。8088/7861 はいずれも停止中（`netstat -ano | grep -E ":8088|:7861"` が 0 件）を確認済み。

- STEP A＝`import irodori_openai_tts.app`（＝サーバ起動時に必ず通る）
- STEP B＝`from dacvae import DACVAE`（＝codec 読み込み時・`upstream/Irodori-TTS/irodori_tts/codec.py:60,65`）
- STEP C＝`import silentcipher`（＝runtime 生成時・`irodori_tts/watermark.py:35`）
- STEP D＝`from transformers import AutoTokenizer/AutoConfig/AutoModel`（＝`irodori_tts/tokenizer.py:40`・`model.py:688`）
- STEP E＝`import soundfile`（すでに A で入る＝差分 0）

### 2-B. 分類表

| パッケージ | 分類 | 根拠 |
|---|---|---|
| **torch** | サーバ起動時 | `upstream/Irodori-TTS-Server/src/irodori_openai_tts/app.py:15`・STEP A |
| **torchaudio** | サーバ起動時 | `…/audio.py:11`・`irodori_tts/inference_runtime.py:17`・`codec.py:8`・STEP A |
| **soundfile** | サーバ起動時 | `…/audio.py:9`・STEP A（`inference_runtime.py:1519,1537`・`codec.py:273` は fallback の遅延 import） |
| **huggingface_hub** | サーバ起動時 | `codec.py:9`・STEP A |
| safetensors / numpy / pydantic / fastapi / starlette / uvicorn / orjson / rich / tqdm / yaml / filelock | サーバ起動時 | STEP A |
| **dacvae** | **合成時（モデル読み込み時）** | `codec.py:60,65`（`try: from dacvae import DACVAE`）・STEP B |
| **audiotools**(descript-audiotools) | **推移的（dacvae が引く）** | `SP:dacvae/__init__.py:8` `import audiotools`（トップレベル・回避不能）・STEP B |
| **scipy** | **推移的（audiotools が引く）** | `SP:audiotools/core/loudness.py:5` `import scipy`・STEP B。※transformers も引く |
| **tensorboard** | **推移的（audiotools が引く）** | `SP:audiotools/ml/decorators.py:23` `from torch.utils.tensorboard import SummaryWriter`・STEP B に `tensorboard`/`absl` 出現。**訓練用と思われがちだが合成経路に乗る** |
| julius / ffmpy / flatten_dict / randomname / einops / importlib_resources / psutil / fsspec | 推移的（audiotools・dacvae） | STEP B |
| **silentcipher** | **合成時（runtime 生成時）** | `irodori_tts/watermark.py:35`・呼び元 `inference_runtime.py:619` `self.watermarker = SilentCipherWatermarker(...)`・STEP C |
| **librosa** | **推移的（silentcipher が引く・だが未使用）** | `SP:silentcipher/server.py:11` `import librosa`。**`librosa.` の呼び出しは server.py に 1 件も無い**（grep 0 件）＝完全な死荷重 |
| pydub / lazy_loader / soxr / joblib | 推移的（silentcipher・librosa） | STEP C |
| **transformers** | **合成時（tokenizer/text-encoder 読み込み時）** | `irodori_tts/tokenizer.py:40`・`model.py:688-689,711`・STEP D |
| **sentencepiece** | 推移的（transformers が引く） | STEP D（`AutoTokenizer` の import だけで入る）。irodori 側に直接 import は無い |
| **tokenizers** | 推移的（transformers） | STEP D |
| **pandas** | **推移的（transformers）** | STEP D。irodori/server に直接 import 無し（grep 0 件） |
| **pyarrow** | **推移的（transformers）** | STEP D。同上 |
| **scikit-learn (sklearn)** | **推移的（transformers）** | STEP D。同上 |
| PIL / accelerate / narwhals / sympy / regex | 推移的（transformers） | STEP D |
| **gradio** | **サーバ経路では不使用**（本体 7861 のみ起動時） | `upstream/Irodori-TTS/gradio_app.py:8`・`gradio_app_voicedesign.py:8`・`irodori_tts/gradio_emoji_palette.py:6`。STEP A–E に**現れない** |
| **wandb** | **訓練のみ** | `upstream/Irodori-TTS/train.py:3013`（関数内 `import wandb`）。STEP A–E に現れない |
| **datasets** | **訓練/前処理のみ** | `upstream/Irodori-TTS/prepare_manifest.py:20`。STEP A–E に現れない |
| **peft** | **LoRA 適用時のみ（遅延）** | `irodori_tts/lora.py:137`（`from peft import ...` は関数内）。STEP A–E に現れない |
| **torchdata** | **訓練のみ** | `upstream/Irodori-TTS/train.py:21-22`。STEP A–E に現れない |
| **torchcodec** | **導入されるが稼働機では import 不能**（§4-F） | irodori/server に直接 import 無し。`SP:torchaudio/_torchcodec.py:82,246` 経由の遅延 import のみ。STEP A–E に現れない |
| **numba** | **不使用** | 両コードベースに import 無し（grep 0 件）。librosa が遅延（lazy_loader）で持つだけで STEP C にも現れない |
| **llvmlite** | **不使用（numba の下）** | 同上 |
| **matplotlib** | **不使用** | `SP:audiotools/core/display.py:118` 等の関数内 import のみ。STEP A–E に現れない |
| **ipython (IPython)** | **不使用** | `SP:audiotools/core/playback.py:32`・`SP:audiotools/post.py:9`。`post.py`/`preference.py` は `audiotools/__init__.py` から読まれない（`__init__.py:2-10`）＝STEP B にも現れない |
| **flask** | **不使用（silentcipher の宣言依存）** | `SP:silentcipher-1.0.5.dist-info/METADATA:21` `Requires-Dist: Flask>=2.2.5`。import する側は `SP:sentry_sdk/integrations/flask.py` だけ（遅延） |

### 2-C. dacvae / silentcipher の 1 段先（宣言依存の原文）

- `SP:dacvae-1.0.0.dist-info/METADATA:18-25`
  `argbind>=0.3.7` / **`descript-audiotools>=0.7.2`** / `einops` / `huggingface-hub` / `numpy` / `torch` / `torchaudio` / `tqdm`
- `SP:descript_audiotools-0.7.2.dist-info/METADATA:19-40`
  `argbind` `numpy` `soundfile` `pyloudnorm` `importlib-resources` `scipy` `torch` `julius` `torchaudio` `ffmpy` **`ipython`** `rich` **`matplotlib`** **`librosa`** `pystoi` `torch-stoi` `flatten-dict` `markdown2` `randomname` `protobuf (<3.20,>=3.9.2)` **`tensorboard`** `tqdm`
  → **audiotools が ipython・matplotlib・librosa・tensorboard を宣言依存で引く**のが確定。ただし実 import は tensorboard のみ（§2-B）。
  → `protobuf<3.20` の固定は `SP:protobuf-3.19.6-nspkg.pth` を site-packages に置く＝**embed 配布で `import site` が要る理由**（§4-C）。
- `SP:silentcipher-1.0.5.dist-info/METADATA:14-23`
  `torch>=2.4.0` `torchaudio>=2.4.0` **`librosa>=0.10.0`** `numpy>=1.25.2` `SoundFile>=0.12.1` `scipy>=1.11.4` `pyaml>=24.4.0` **`Flask>=2.2.5`** `pydub>=0.25.1` `huggingface-hub>=0.23.4`

### 2-D. transformers の import 面積（実測）

`from transformers import AutoTokenizer` **だけ**で追加で乗るもの＝
`PIL, accelerate, joblib, narwhals, numpy, pandas, pyarrow, regex, safetensors, scipy, sentencepiece, sklearn, sympy, tokenizers, torch`。
`AutoModel/AutoConfig` を足しても**差分 0**。＝transformers 5.x は最初の一発で pandas・pyarrow・sklearn まで持ってくる（合計 ≈ 320 MB 相当）。ONNX 化（⒝）でここが丸ごと落とせるかは第 2 席の材料。

---

## 3. 稼働機 venv の実測

### 3-A. 全体

| venv | Python | torch | 実サイズ(合計バイト) | du -sm | dist-info 数 |
|---|---|---|---|---|---|
| `C:\irodori-TTS-server\Irodori-TTS-Server\.venv-rocm`（**8088 の実体**） | 3.12 | 2.13.0+rocm10.0.0 | **4,760 MB** | 4,868 MB | 164 |
| `C:\IrodoriTTS\Irodori-TTS\.venv-rocm`（7861 の実体） | 3.12 | 2.13.0+rocm10.0.0 | 4,764 MB | 4,873 MB | — |
| `C:\irodori-TTS-server\Irodori-TTS-Server\.venv`（旧 CPU） | 3.10 | 2.10.0（CPU） | **1,242 MB** | 1,346 MB | 159 |
| `C:\IrodoriTTS\Irodori-TTS\.venv`（旧 CPU） | 3.10 | 2.10.0（CPU） | 1,241 MB | 1,335 MB | 151 |
| uv 管理の素の Python 3.12（venv の親） | — | — | **63 MB**（うち `Lib` 30 MB） | — | — |

- 版の確認＝`.venv/pyvenv.cfg`（`version_info = 3.10`）／`.venv-rocm/pyvenv.cfg`（`version_info = 3.12`）、`torch-2.10.0.dist-info` vs `torch-2.13.0+rocm10.0.0.dist-info`。
- **venv 本体は自己完結でない**＝`Scripts` は 4 MB のみ、標準ライブラリは `pyvenv.cfg:1` `home = C:\Users\mugonkun\AppData\Roaming\uv\python\cpython-3.12-windows-x86_64-none` を参照。

### 3-B. 主要パッケージ（`.venv-rocm` / `du -m`）

| パッケージ | MB | ネイティブ拡張 | 備考 |
|---|---:|---|---|
| `_rocm_sdk_core` | 2,132 | .dll 11 | **ROCm 固有** |
| `_rocm_sdk_libraries` | 1,028 | .dll 49 | **ROCm 固有** |
| `torch` | 739 | .pyd/.dll 15 | 内訳は §3-C |
| `llvmlite`(+`.libs`) | 117 | 2 | **不使用**（§2-B） |
| `scipy`(+`.libs`) | 120 | 107 | 使用 |
| `pyarrow`(+`.libs`) | 86 | 33 | transformers 経由 |
| `wandb` | 81 | — | **不使用** |
| `gradio` | 77 | — | **サーバでは不使用** |
| `transformers` | 58 | — | 使用 |
| `pandas`(+`.libs`) | 46 | 46 | transformers 経由 |
| `sympy` | 42 | — | torch 経由 |
| `sklearn` | 31 | 71 | transformers 経由 |
| `jedi` | 27 | — | **不使用**（IPython 系） |
| `numpy`(+`.libs`) | 47 | 21 | 使用 |
| `matplotlib` | 23 | 8 | **不使用** |
| `PIL` | 15 | 8 | transformers 経由 |
| `numba` | 14 | 14 | **不使用** |
| `grpc` | 13 | 1 | tensorboard 経由 |
| `fontTools` | 12 | 6 | matplotlib 経由・**不使用** |
| `torchaudio` | 10 | 3 | 使用 |
| `tensorboard` | 10 | — | audiotools 経由で**使用** |
| `hf_xet` | 10 | 1 | HF ダウンロード高速化 |
| `tokenizers` | 8 | 1 | 使用 |
| `networkx` | 8 | — | torch 経由 |
| `torchcodec` | 7 | .dll 10 / .pyd 5 | **稼働機では import 不能**（§4-F） |
| `peft` | 4 | — | LoRA 時のみ |
| `datasets` / `IPython` / `sentencepiece` / `accelerate` / `_soundfile_data` | 各 3 | sentencepiece 1・`_soundfile_data` 1 | `_soundfile_data/libsndfile_x64.dll`＝2,364,416 B |
| `torchdata` / `flask` / `llvmlite.libs` | 各 ≤1 | — | **不使用** |

**site-packages 合計＝4,860 MB（du）**。

### 3-C. ROCm 固有分の切り分け

| 区分 | MB | 根拠 |
|---|---:|---|
| `_rocm_sdk_core` | 2,132 | du |
| `_rocm_sdk_libraries` | 1,028 | du |
| `torch/lib/aotriton.images/amd-gfx115x/**` | 180 | du。`SP:amd_torch_device_gfx115x-2.13.0+rocm10.0.0.dist-info/RECORD:7-14` がこの配置を宣言（attn_fwd.zip 42 MB 等） |
| `torch/.kpack/torch_gfx1151.kpack` | 48 | `SP:amd_torch_device_gfx1151-…/RECORD:7`（50,247,134 B） |
| `torch/lib/torch_hip.dll` | 68 | `ls -l` |
| `rocm_sdk` / `rocm_sdk_core` / `rocm_sdk_libraries` / `rocm_bootstrap`（Python 側） | ≤4 | du |
| **ROCm 固有 小計** | **≈ 3,456** | |
| 参考：`torch/lib/torch_cpu.dll` | 214 | GPU 版でも要る |
| 参考：`torch/lib/torch_cpu.lib` | 28 | **リンク用・実行時不要**（配布から落とせる） |
| 参考：`torch/include` | 65 | **ヘッダ・実行時不要** |

ROCm 初期化は `SP:torch/_rocm_init.py:2-6`（`rocm_sdk.initialize_process(preload_shortnames=['amd_comgr','amdhip64','hiprtc','hipblas',…], check_version='10.0.0')`）が `SP:torch/__init__.py:158-164` から呼ばれる＝**torch を import しただけで rocm_sdk の DLL 群を先読みする**。

### 3-D. ネイティブ拡張（`.pyd`/`.dll`）を持つパッケージ

`scipy`(106) `sklearn`(71) `_rocm_sdk_libraries`(49) `pandas`(45) `pyarrow`(31) `numpy`(19) `torchcodec`(15) `torch`(15) `numba`(14) `_rocm_sdk_core`(11) `matplotlib`(8) `PIL`(8) `fontTools`(6) `aiohttp`(4) `torchaudio`(3) `pyarrow.libs`(2) `numpy.libs`(2) `httptools`(2) `charset_normalizer`(2) ／ 各 1 個＝`yarl` `yaml` `xxhash` `websockets` `watchfiles` `tokenizers` `soxr` `sentencepiece` `scipy.libs` `safetensors` `regex` `pydantic_core` `psutil` `propcache` `pandas.libs` `orjson` `multidict` `msgpack` `markupsafe` `llvmlite.libs` `llvmlite` `kiwisolver` `hf_xet` `grpc` `frozenlist` `contourpy` `_soundfile_data` ／ 単体 `.pyd`＝`_cffi_backend` `_brotli`。

→ **ABI 固定**。embed 配布は「Python 3.12・win_amd64」で 1 セットに固定して作り置きすればよい（利用者機でのビルドは 0 件）。

---

## 4. python-embed 化の可否

### 結論（見立て）

**成立する見込みが高い。** 埋め込み配布＝`python-3.12.x-embed-amd64.zip` を展開し、`python312._pth` に `import site` を書き、こちらで解決済みの site-packages を丸ごと同梱、起動は `python.exe -m irodori_openai_tts …`。
**uv は利用者機に不要**（wheel の解決・展開は我々の build 機で済ませる）。ただし下記 A〜H を潰す必要がある。

| # | 論点 | 判定 | 根拠 |
|---|---|---|---|
| A | **venv は再配置できない** | 現状のまま zip 配布は**不可** | `.venv-rocm/pyvenv.cfg:1` `home = C:\Users\mugonkun\AppData\Roaming\uv\python\…`（絶対）／`SP:__editable__.irodori_tts_server-0.1.0.pth`＝`C:\irodori-TTS-server\Irodori-TTS-Server\src`（絶対）。`Scripts\*.exe` も絶対 python を埋める。→ **embed 配置に組み直す**（venv を使わない・`irodori_tts` と `irodori_openai_tts` は素の package として置く） |
| B | **torch の DLL 探索** | ○（venv 非依存） | `SP:torch/__init__.py:169-242`。探すのは `os.path.dirname(__file__)/lib`（＝同梱の `torch/lib`）と `sys.exec_prefix\Library\bin` 等。`os.add_dll_directory`（同:242）で解決し、無ければ `PATH` に足す（同:276）。embed でも `torch/lib` は package 相対なので通る |
| C | **MSVC ランタイム** | **要対処** | `SP:torch/__init__.py:245-248` が `vcruntime140.dll` `msvcp140.dll` `vcruntime140_1.dll` を `ctypes.CDLL` で読み、失敗すると同:253-254 が逐語で「**Microsoft Visual C++ Redistributable is not installed, this may lead to the DLL load failure.** It can be downloaded at https://aka.ms/vs/17/release/vc_redist.x64.exe」と出す。→ インストーラで **VC++ 再頒布可能パッケージを入れる／DLL を同梱**（同梱可否の可否判断は卓）。※embed zip がどの DLL を同梱するかは**未確認**（実物を持っていない） |
| D | **`._pth` と `site`** | **要対処** | embed 既定は `site` 無効＝`.pth` が処理されない。`SP:protobuf-3.19.6-nspkg.pth` が `google` 名前空間を作る（tensorboard→protobuf 経路で要る・§2-C）ので、`python312._pth` に `import site` を書く必要がある。`SP:distutils-precedence.pth` も同様。※`._pth` の具体挙動は Python 公式仕様に依る＝**一次確認は未取得** |
| E | **soundfile / libsndfile** | ○ 自己完結 | `SP:_soundfile_data/libsndfile_x64.dll`（2,364,416 B）を wheel が同梱。外部 DLL 不要 |
| F | **torchcodec と FFmpeg** | **落としてよい（現に落ちている）** | `SP:torchcodec/_core/ops.py:50` が FFmpeg major 8→7→6→5→4 の順に `libtorchcodec_core{N}.dll` を試し、同:97-105 が `shutil.which("ffmpeg")` の隣を `os.add_dll_directory` する。**稼働機の ffmpeg は 9.0.1**（`which ffmpeg`＝`…Gyan.FFmpeg…/ffmpeg-9.0.1-full_build/bin/ffmpeg`）＝対応外。実測で `import torchcodec` は **RuntimeError**（`Could not load libtorchcodec.`）、`torchaudio.save` も **RuntimeError**。→ irodori 側 `inference_runtime.py:1518` / `codec.py:272` / `…/audio.py:47` の `except RuntimeError` が soundfile に落として**動く**。**mp3/opus は soundfile(libsndfile) で出せる**（`…/audio.py:44-50, 72-91`）。aac だけは torchaudio→ffmpeg（同:54-69）＝**aac を諦めれば FFmpeg は不要**。<br>※注意：`torchaudio._torchcodec` は ImportError を ImportError のまま投げる箇所がある（`SP:torchaudio/_torchcodec.py:83-86, 247-250`）＝**別環境では ImportError になり得て、irodori の `except RuntimeError` を素通りする**。embed 配布では torchcodec を同梱しない設計にするなら、この経路の実挙動を 1 度確かめること |
| G | **numba / llvmlite** | ○ 落とせる | §2-B のとおり import されない。落とせば **117 MB** 減。落とさない場合、numba は書込可能なキャッシュ ディレクトリを要る（embed だと Program Files 配下で詰まる典型）＝落とすのが安全 |
| H | **hf_xet** | ○（退避策あり） | ネイティブ .pyd 1 個・10 MB。失敗時は `HF_HUB_DISABLE_XET`（`SP:huggingface_hub/constants.py:340`）で無効化できる |
| I | **sentencepiece** | ○ | 3.12 用 wheel（`sentencepiece-0.2.1.dist-info`）が実在＝ビルド不要。**上流の `<0.2` 制約は我々の build 機で外す**（`overrides-rocm.txt:3` と同じ操作） |

### 削れる見込み（サーバ経路で一度も import されないもの）

`gradio 77` + `wandb 81` + `llvmlite(+libs) 117` + `numba 14` + `jedi 27` + `matplotlib 23` + `fontTools 12` + `torchcodec 7` + `peft 4` + `datasets 3` + `IPython 3` + `torchdata/flask ≤2` ≈ **370 MB**（＋各々の小さな連れ）。
加えて実行時不要な `torch/include 65` + `torch/lib/*.lib 28` ≈ **93 MB**。
→ **ROCm 版 4,760 MB → 概ね 4,300 MB 前後**、**CPU 版 1,241 MB → 概ね 800 MB 前後**の見立て（**未検証**＝実際に外して起動確認はしていない）。

---

## 5. モデルの初回取得

### 5-A. どのコードが何を取るか

| # | 取得元 | 呼ぶコード | 檔名 | 実測サイズ |
|---|---|---|---|---|
| 1 | HF `Aratako/Irodori-TTS-v4-Small`（サーバ既定） | `inference_runtime.py:554,567-572` `snapshot_download(repo_id=…, allow_patterns=["model.safetensors","tokenizer/*"])` | `model.safetensors` | **3,064,295,596 B（2.854 GiB / 3.06 GB）** |
| 1' | 同上 | 同上（`allow_patterns` の `tokenizer/*`） | `tokenizer/tokenizer.json` `tokenizer/tokenizer_config.json` | 6,718,495 B ＋ 小 |
| 2 | HF `Aratako/Semantic-DACVAE-Japanese-32dim` | `codec.py:72` `hf_hub_download(repo_id=location, filename="weights.pth")` | `weights.pth` | **429,620,065 B（409.7 MiB）** |
| 3 | HF `sony/silentcipher` | `SP:silentcipher/server.py:475` `snapshot_download(repo_id="sony/silentcipher")`（**allow_patterns 無し＝リポ全部**） | `44_1_khz/73999_iteration/{enc_c,dec_c,dec_m_0,opt}.ckpt` ＋ 16 kHz 一式 ＋ `config.json` `README.md` | **68 MB**（うち **`opt.ckpt` 23,448,174 B ×2 は推論に不要**） |
| 4 | HF `sbintuitions/sarashina2.2-0.5b`（トークナイザの**退路**） | `config.py:19` `text_tokenizer_repo: str = "sbintuitions/sarashina2.2-0.5b"` → `inference_runtime.py:666-673` | — | **取得されていない**（HF キャッシュに存在しない）＝v4 は同梱 tokenizer で足りる |

**合計（v4-Small 1 本）≈ 3.5 GB。** HF キャッシュ実測（`du -sm HF:`）＝`Irodori-TTS-v4-Small` 2,929 MB／`Irodori-TTS-v4.1-Small` 2,929 MB／`Semantic-DACVAE-Japanese-32dim` 410 MB／`sony/silentcipher` 68 MB。**7861 と 8088 で別チェックポイントを使っているため 2.86 GB が 2 本並存**している（README 記載どおり）。

### 5-B. ローカル指定・オフライン

| 手段 | 有無 | 根拠 |
|---|---|---|
| `IRODORI_CHECKPOINT`（ローカル `model.safetensors`） | **あり** | `upstream/Irodori-TTS-Server/src/irodori_openai_tts/config.py:22` `checkpoint: str \| None = None` ＋ `env_prefix="IRODORI_"`（同:12）。README:554 逐語「Local checkpoint path. **Takes precedence over `IRODORI_HF_CHECKPOINT`**; keep a bundled `tokenizer/` beside the checkpoint or above its variant subfolder.」 |
| tokenizer のローカル解決 | **あり（自動）** | `inference_runtime.py:581-589` `_resolve_tokenizer_source()`＝`checkpoint.parent/tokenizer` か `checkpoint.parent.parent/tokenizer` に `tokenizer_config.json` があればそれを使い、`local_files_only=True` で読む（同:673, 693） |
| codec のローカルパス | **あり** | `codec.py:67-73`＝`repo_id` が既存パスなら（同:70 の `not Path(location).exists()` が偽）DL を飛ばしてそのまま `DACVAE.load(location)`（同:78）。`hf://` 接頭辞も剥がす（同:68-69）。サーバ側は `IRODORI_CODEC_REPO`（`config.py:24`・既定 `Aratako/Semantic-DACVAE-Japanese-32dim`） |
| silentcipher のローカル指定 | **サーバからは不可** | `watermark.py:43` は `silentcipher.get_model(model_type=model_type, device=device)` のみ＝`ckpt_path`/`config_path` を渡さない。既定は `'../Models/44_1_khz/73999_iteration'`（`SP:silentcipher/server.py:469`）で、無ければ必ず `snapshot_download` に落ちる（同:472-477）。**失敗しても致命傷ではない**＝`watermark.py:44-50` が warning を出して透かし無しで続行 |
| オフライン切替 | **コードには無い**（HF 側の環境変数のみ） | `grep -rn "HF_HOME\|HF_HUB_OFFLINE\|TRANSFORMERS_OFFLINE"` は両リポで**0 件**。`SP:huggingface_hub/constants.py:192` `HF_HUB_OFFLINE = _is_true(os.environ.get("HF_HUB_OFFLINE") or os.environ.get("TRANSFORMERS_OFFLINE"))` |
| `HF_HOME` の扱い | **既定は `~/.cache/huggingface`／環境変数で移動可** | `SP:huggingface_hub/constants.py:157-168` 逐語 `default_home = os.path.join(os.path.expanduser("~"), ".cache")` → `HF_HOME = … os.getenv("HF_HOME", os.path.join(os.getenv("XDG_CACHE_HOME", default_home), "huggingface"))` → `default_cache_path = os.path.join(HF_HOME, "hub")`。`HF_HUB_CACHE`（同:175-179）でも上書き可 |

→ ⒜の実装では **`HF_HOME` を専用フォルダに固定**して 3.5 GB の置き場を管理し、**同梱するなら `IRODORI_CHECKPOINT`＋隣の `tokenizer/`＋`IRODORI_CODEC_REPO`＝ローカルパス**で HF を一切叩かない構成にできる（silentcipher だけは取得 or 透かし無し）。※同梱の可否はライセンス（第 1 席）の裁定次第。

---

## 6. 配布物サイズの概算表

| 区分 | Python 実行系 | ライブラリ（site-packages） | モデル | 合計 | 確度 |
|---|---:|---:|---:|---:|---|
| **ROCm / Windows（稼働機・実測）** | 63 MB（uv 管理 3.12） | **4,760 MB** | 3,500 MB | **≈ 8.3 GB** | **実測** |
| ROCm / Windows（不要分を削った見立て） | 〜30 MB（embed zip・**未確認**） | ≈ 4,300 MB | 3,500 MB | ≈ 7.8 GB | 見立て |
| **CPU のみ / Windows（実測）** | 63 MB | **1,241 MB** | 3,500 MB | **≈ 4.8 GB** | **実測**（`.venv`・torch 2.10.0 CPU・py3.10） |
| CPU のみ（不要分を削った見立て） | 〜30 MB | ≈ 800 MB | 3,500 MB | ≈ 4.3 GB | 見立て |
| **CUDA cu128 / Windows** | — | **未計測** | 3,500 MB | **未計測** | **第 10 席（10-gpu-landscape.md）の web 調査に委ねる** |

- CUDA が測れない理由＝`upstream/Irodori-TTS-Server/uv.lock:5357-5379` の cu128 wheel 行には **`size = ` フィールドが無い**（`download-r2.pytorch.org` の index はサイズを返さない）。PyPI 経由の素の `torch-2.10.0-cp310-cp310-win_amd64.whl` は `uv.lock:5219` に `size = 113720931`（108 MB）とあるが、これは **CUDA 無しの Windows 既定 wheel**であって cu128 の代用にならない。
- 参考：lock には `nvidia-*` が 15 個（`uv.lock:2944-3095`）あるが、いずれも `sys_platform == 'linux'` 側の marker（例 `uv.lock:5337`）＝**Windows の cu128 は DLL を torch wheel 内に抱える**見込み（**未確認**）。第 10 席への具体的な問い＝「`torch-2.10.0+cu128-cp3XX-win_amd64.whl` の実バイト数」「展開後の `torch/lib` の大きさ」。
- 比較の物差し（実測・`torch/lib` の展開後）＝**CPU 版 319 MB / ROCm 版 543 MB**。CPU venv には `nvidia-*`・`triton`・`cuda*` のディレクトリが **1 つも無い**（`ls | grep -i` が 0 件）＝現行 `.venv` は CUDA 抜きと確定。
- モデル 3,500 MB は §5 の実測（2,929＋410＋68＋α）。**7861 と 8088 で別 checkpoint を使うなら +2,929 MB**。

### GPU 対応範囲のコスト（§4-7 への一次材料）

| 軸 | CUDA のみ | ROCm も対応 |
|---|---|---|
| 配布物 | 未計測（第 10 席） | **+3,456 MB**（`_rocm_sdk_core` 2,132・`_rocm_sdk_libraries` 1,028・aotriton 180・kpack 48・torch_hip 68） |
| 導入手順の分岐 | 1 本 | **2 本に割れる**。上流の `--extra rocm` は `sys_platform=='linux'` 限定（`pyproject.toml:76`）＝Windows では使えず、AMD 公式 index＋`[device-gfxXXXX]` 型番の手打ち＋overrides が要る（`YWK:probe/irodori-tts-rocm-setup-guide.md:69, 99`） |
| GPU 型番依存 | 少ない | **強い**＝`amd_torch_device_gfx1151`／`gfx115x` という**型番専用パッケージ**が実在（`SP:amd_torch_device_gfx1151-…/RECORD:7`）。他機種は別 wheel＝1 本の配布物で全 AMD は賄えない |
| Python の版 | 3.10 で通る（上流既定） | **3.11〜3.14 のみ**（`YWK:…rocm-setup-guide.md:61-62`）＝上流の `.python-version`＝3.10 と両立しない |

---

## 7. 主席への注意（⒜で消える／残る）

### ⒜専用インストーラで**消える**詰まり所

| 詰まり | 消える理由 |
|---|---|
| uv を入れる・ターミナル再起動 | 依存解決は build 機で完了。利用者機に uv も pip も要らない |
| `--extra` の 4 択（cpu/cu128/rocm/xpu） | 配布物を機種別に固定＝選択肢を消す |
| torch index の指定（`--extra-index-url` / `--index-strategy unsafe-best-match`） | 同上 |
| `sentencepiece<0.2` のビルド失敗 | build 機で 0.2.1 に上げて焼き込む（`overrides-rocm.txt:3` と同じ操作） |
| `overrides-rocm.txt` を手で作る | 同上 |
| `uv run` が旧 `.venv` を掴む | venv を使わない（`python.exe -m …` 直起動）＝二重箱が発生しない |
| bat の CRLF/UTF-8 事故（`irodori-server-gpu.bat:4`） | 起動を bat に頼らない（exe ランチャ or C# から直起動） |
| Windows なのに `cp .env.example .env`（README:42） | .env を同梱・GUI で設定 |
| Python 3.10 と ROCm(3.11+) の版の矛盾 | embed の Python 版をこちらが決める |

### ⒜でも**残る**詰まり所

| 残る | 理由・大きさ |
|---|---|
| **GPU ドライバ**（AMD Adrenalin / NVIDIA） | 利用者機の管轄。ROCm は `torch.cuda.is_available()` が `False` になる典型（`YWK:…rocm-setup-guide.md:112-113, 159`） |
| **MSVC 再頒布可能パッケージ** | `SP:torch/__init__.py:245-254`。同梱可否は要判断 |
| **3.5 GB の初回取得**（or 同梱で 3.5 GB 太る） | §5。同梱はライセンス裁定（第 1 席）待ち。`HF_HOME` を専用フォルダに固定する設計は要る |
| **ディスク空き** | 実測で venv 4.76 GB ＋ モデル 3.5 GB。ROCm 版なら 9 GB 前後を要求することになる |
| **GPU 型番別の配布**（ROCm を採る場合） | `amd_torch_device_gfx1151` の存在（§6）＝機種ごとに別パッケージ |
| **aac 出力**（FFmpeg 依存） | `…/audio.py:54-69`。wav/flac/mp3/opus は soundfile で出せる＝**aac を切れば FFmpeg 不要**にできる |
| **上流追随** | torch の版が上がるたびに 164 個の wheel を再解決＋再検証。ROCm を持つと 2 系統になる |

### 主席が報告で使いやすい「一撃の事実」

- **Windows × ROCm が CPU に落ちるのは仕様どおり**＝`upstream/Irodori-TTS/pyproject.toml:76`（`{ index = "pytorch-rocm", extra = "rocm", marker = "sys_platform == 'linux'" }`）と `upstream/Irodori-TTS-Server/uv.lock:99`（win32×rocm extra → PyPI の素の torch）。前回プローブの「CPU 合成だった経緯」の一次根拠はここ。
- **torchcodec は現在も壊れたまま動いている**（FFmpeg 9.0.1 が対応外・RuntimeError→soundfile fallback）。＝配布物から torchcodec を外す判断は現状追認にすぎない。
- **稼働機 venv は絶対パスを 2 箇所に埋めている**（`pyvenv.cfg:1`・`__editable__…pth`）＝「今の箱をコピーして配る」は最初から不可能。⒜は「作り直し」であって「移送」ではない。

### 未確認（推測の札）

- `python-3.12.x-embed-amd64.zip` の実サイズ・同梱 DLL（vcruntime140 の有無）・`._pth` の細部＝**実物未取得**。§4-C/D と §6 の「〜30 MB」は公称値の記憶に基づく推測。
- 不要パッケージ 370 MB を実際に外して起動できるかは**未検証**（外して試すには別 venv が要り、今回は作っていない）。
- cu128 Windows の配布サイズ＝**未計測**。第 10 席へ。
- `sentencepiece 0.1.99` に cp312 wheel が無いという「原因」は、稼働機の症状記録（`YWK:…rocm-setup-guide.md:89-90`）からの推定。上流 PyPI の一次確認は取っていない。
