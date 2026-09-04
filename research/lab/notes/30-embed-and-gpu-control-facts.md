# 30 — 埋め込み Python 配布の実現性（実機試験）と、本体（C#）が握る起動制御・GPU 指定の事実

> 席＝第 6 次サブ席（opus）／担当＝⒜「python なし環境で動くパッケージ化」の**実現性の細目**と、**本体側が握る制御の事実**。
> 実施 2026-09-04。設計・UI・実装は決めない＝事実と檔:行／URL だけ。
> **本便は python-embed を実機で組んで Server を起動し、`/health` まで通した**（§0）。⒜の「成立する見込み」は**実射で成立に変わった**。
> 相対パスの基点＝`C:/Users/mugonkun/source/repos/irodori-native-research/`。`SP:`＝`lab/.venv/Lib/site-packages/`。
> 停止域＝upstream clone・`C:/IrodoriTTS`・`C:/irodori-TTS-server`・HF キャッシュは 1 バイトも変えていない（§7）。8088/7861 は起動していない。

## 要点（10 行以内）

1. **埋め込み Python は実際に動いた**＝`python-3.12.10-embed-amd64.zip` を展開し `python312._pth` に site-packages を書くだけで、**`import site` 無し**で torch 2.10.0+cpu・transformers・soundfile・Server が全部入り、`python.exe -m irodori_openai_tts --port 8092` が起動し `/health` が 200 を返した（§0-2・§0-3）。
2. **⒜が壊す前提はほぼ無い**＝Server と `irodori_tts` は `sys.executable` 再実行・multiprocessing・subprocess（ffmpeg 以外）・`pkg_resources` を**1 箇所も使わない**（grep 0 件・§1）。`importlib.metadata` は `quantization.py:243`（量子化 checkpoint の**保存**時のみ）だけ。
3. **`._pth` があると `sys.flags.isolated=1 / no_site=1 / ignore_environment=1` になり `PYTHONPATH` は無視される**（実測）。だが **`PYTHONUTF8`・`PYTHONIOENCODING`・`PYTHONDONTWRITEBYTECODE`・`PYTHONWARNINGS`・`IRODORI_*`・`HF_*` は効いた**（実測・§1-B）＝**パスだけが `._pth` 専管**。
4. **CWD 相対が 2 つ残る**＝`.env`（`config.py:13`）と `voices_dir` 既定 `Path("voices")`（`config.py:41`）。後者は**起動時に `mkdir` する**（`voices.py:58-60`・app.py:104）＝**書込不可フォルダに置くと起動で落ちる**。絶対パスの env で殺せる。
5. **`__pycache__` は書かれる**（実測）。書けない場所でも**沈黙して続行**（CPython 3.12 `importlib/_bootstrap_external.py:1232-1244` 逐語「Could be a permission error, read-only filesystem: just forget about writing the data.」）＝Program Files 配下でも致命ではないが毎回コンパイルし直す。
6. **本体が握れるのは環境変数だけ**＝CLI は `--host/--port/--reload` の 3 つのみ（`__main__.py:17-21`）。設定は `IRODORI_` 接頭辞の **50 欄**（主席訂正＝config.py:18-72 を数え直すと 50・§2-1 の表も 50 行）（§2-1 の全表）で、**名前は大小無視**（`irodori_model_device` でも効く・実測）。**`IRODORI_CORS_ORIGINS` は JSON 配列必須**＝カンマ区切りは `SettingsError` で**起動即死**（実測・14 の U-6 を閉じる）。
7. **ready 検知は 2 段**＝`PRELOAD=true` だと**モデルを読み終わるまでポートが開かない**（実測＝26 秒間 connection refused ののち 200・ログ `runtime loaded in 28.16s`）。`PRELOAD=false` なら 4〜5 秒で 200 だが `runtime.loaded=false`。**「接続できる」＋「`runtime.loaded=true`」の両方**が要る（§2-3）。
8. **停止は kill しかない**＝graceful 用のエンドポイントは無い（ルートは 7 本・§2-4）。`asyncio.shield`（`app.py:781`）は **SSE 経路だけ**で、クライアント切断から合成中のチャンクを守る＝**切断しても合成は最後まで走る**。
9. **多 GPU は「1 プロセス 1 デバイス」**＝`RuntimeKey` の単一キャッシュ（`inference_runtime.py:1487-1496`）＋ `RuntimeManager` が 1 スロット（`runtime.py:28,48`）＝**2 枚使うならプロセス 2 本・ポート 2 本**。
10. **CUDA の index 順は既定で `FASTEST_FIRST`＝nvidia-smi 順とも DXGI 順とも一致しない**（NVIDIA 原文・§3-2）。揃える手段は **`CUDA_DEVICE_ORDER=PCI_BUS_ID`** か **UUID 照合**。torch は `get_device_properties(i).uuid` を持つ（ROCm 実測で `pci_bus_id` も持つ・§3-4）。

---

## 0. 実験の作り（再現手順・本便の一次データ）

### 0-1. 使った道具

| 物 | 実体 | 出所 |
|---|---|---|
| 埋め込み Python | `lab/tmp/embed-test/py312/`（**35 檔・展開 22,501,299 B**） | `lab/tmp/rt-license/python-3.12.10-embed-amd64.zip`（11,133,606 B・`22` §4 が取得済み）を `unzip` |
| ライブラリ | `lab/.venv/Lib/site-packages`（**11 ノート §3 の CPU 箱**・torch 2.10.0+cpu / transformers 5.16.1 / protobuf 7.36.1） | 既存。**embed からは `._pth` で参照しただけ**（コピーしていない） |
| コード | `upstream/Irodori-TTS`／`upstream/Irodori-TTS-Server/src` | 既存 clone・**無改変** |
| 作業ディレクトリ | `lab/tmp/embed-test/work/`（`.env` 無し＝全既定はコード既定） | 本便が作成 |

### 0-2. `python312._pth`（既定と、本便が書いたもの）

**既定（zip 同梱・実測 80 B・5 行・CRLF）逐語**（`cat -A` で確認）:

```
python312.zip
.

# Uncomment to run site.main() automatically
#import site
```

**本便が書き換えた形（`import site` は書いていない）**:

```
python312.zip
.
C:/Users/mugonkun/source/repos/irodori-native-research/lab/.venv/Lib/site-packages
C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS
C:/Users/mugonkun/source/repos/irodori-native-research/upstream/Irodori-TTS-Server/src
```

→ **絶対パスも forward slash も通る**（実測）。`sys.path` は**この 5 行がそのまま**になった（§1-B の実測 1）。

### 0-3. 起動の実射（`lab/tmp/embed-test/work/`・ポート 8092）

> **8091 は本便の着任時点で別席のプロセス（PID 51476・`uv` 管理 python・17:34:33 起動）が掴んでいた**ため、
> 衝突を避けて **8092** を使った。8091 には触れていない（kill もしていない）。8092 は使用後に開放済み（§7）。

```bash
E=lab/tmp/embed-test/py312/python.exe ; cd lab/tmp/embed-test/work
export PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1 PYTHONUTF8=1
export IRODORI_PRELOAD=false IRODORI_VOICES_DIR="$PWD/voices" \
       IRODORI_MODEL_DEVICE=cpu IRODORI_CODEC_DEVICE=cpu
"$E" -m irodori_openai_tts --host 127.0.0.1 --port 8092 > server.log 2>&1 &
curl -sS --retry 40 --retry-delay 1 --retry-connrefused http://127.0.0.1:8092/health
```

**結果＝200**。`/health` 本文（逐語・抜粋）＝`"model_device":"cpu","codec_device":"cpu"` / `"runtime":{"preload":false,"loaded":false,"loading":false,"checkpoint":null,...}` / `"voices":{"dir":"C:\\...\\lab\\tmp\\embed-test\\work\\voices","dir_exists":true,"files":0}`。
**ログ（逐語・全文）**：

```
INFO:     Started server process [56008]
INFO:     Waiting for application startup.
INFO:irodori_openai_tts.app:voices directory: C:\...\lab\tmp\embed-test\work\voices
INFO:irodori_openai_tts.app:preload disabled; runtime will load on first speech request
INFO:     Application startup complete.
INFO:     Uvicorn running on http://127.0.0.1:8092 (Press CTRL+C to quit)
INFO:     127.0.0.1:60370 - "GET /health HTTP/1.1" 200 OK
```

**`IRODORI_PRELOAD=true`・`IRODORI_HF_CHECKPOINT=Aratako/Irodori-TTS-v4.1-Small` で再走した結果（同じ埋め込み python）**：

```
INFO:     Started server process [5800]
INFO:     Waiting for application startup.
INFO:irodori_openai_tts.app:voices directory: C:\...\work\voices
INFO:irodori_openai_tts.app:preload enabled; loading runtime during startup
INFO:irodori_openai_tts.runtime:loading runtime
INFO:irodori_openai_tts.runtime:downloading checkpoint assets from hf://Aratako/Irodori-TTS-v4.1-Small
INFO:irodori_openai_tts.runtime:checkpoint download/cache lookup completed in 0.97s
INFO:irodori_openai_tts.runtime:checkpoint resolved: C:\Users\...\models--Aratako--Irodori-TTS-v4.1-Small\snapshots\2b28324...\model.safetensors
[codec] dacvae: hf://Aratako/Semantic-DACVAE-Japanese-32dim -> C:\Users\...\weights.pth
INFO:irodori_openai_tts.runtime:runtime loaded in 28.16s
INFO:     Application startup complete.
INFO:     Uvicorn running on http://127.0.0.1:8092 (Press CTRL+C to quit)
ckpt path or config path does not exist! Downloading the model from the Hugging Face Hub...
INFO:     127.0.0.1:53362 - "GET /health HTTP/1.1" 200 OK
```

- **26 秒間は接続そのものが拒否**（curl が `Failed to connect ... Could not connect to server` を 12 回）→ その後 200。**＝ロード中に応答は返らない**（§2-3）。
- **最後から 2 行目の `ckpt path or config path...` は順序が狂っている**＝`silentcipher/server.py:473` の `print()` に `flush` が無く、stdout がパイプでブロックバッファされるため。`codec.py:73` は `flush=True` 付きなので正しい位置に出る。**⇒ C# が stdout を読むなら順序を信用してはいけない**（§2-5）。

---

## 1. (a) 埋め込みで壊れる前提の有無

### 1-A. コード側（grep で全走査・対象＝`upstream/Irodori-TTS/irodori_tts/` と `upstream/Irodori-TTS-Server/src/`）

| # | 論点 | 使っているか | 檔:行（無いものは grep 0 件と明記） | ⒜への影響 | 確度 |
|---|---|---|---|---|---|
| a1 | `sys.executable` の再実行 | **無し** | `grep -rn "sys\.executable"` → **0 件** | 影響なし | 断定 |
| a2 | `multiprocessing` / spawn | **無し**（推論・サーバ経路） | `grep -rn "multiprocessing\|ProcessPool\|spawn\|fork"` → **0 件** | 影響なし。ただし**`--reload` と `--workers>1` は uvicorn 側が spawn する**（`SP:uvicorn/_subprocess.py:18` `spawn = multiprocessing.get_context("spawn")`・`SP:uvicorn/main.py:614-621`）＝**`--reload` を使わなければ 1 プロセスのまま**（`server.run()`・同 :621） | 断定 |
| a3 | `subprocess`（ffmpeg 以外） | **無し** | 唯一＝`Irodori-TTS-Server/src/irodori_openai_tts/audio.py:4,141,168`（`shutil.which("ffmpeg")` → `subprocess.run`）。**`aac` 出力のときだけ**（`audio.py:54-69`） | **wav/flac/mp3/opus/pcm は soundfile 完結＝FFmpeg 不要**（`08` §4-F・報告 §5-2） | 断定 |
| a4 | `importlib.metadata` | **1 箇所のみ** | `irodori_tts/quantization.py:3` `import importlib.metadata` ／ 同 `:243` `importlib.metadata.version("torchao")` | `quantization.py` は `inference_runtime.py:26` で**モジュール読込時に import される**が、`:243` は**量子化 checkpoint の保存関数の中**＝推論経路で `torchao` の dist-info は要らない（実射で torchao 未導入のまま起動成立） | 断定 |
| a5 | `pkg_resources` | **無し** | `grep -rn "pkg_resources"` → **0 件** | 影響なし | 断定 |
| a6 | `os.environ` / `getenv` | **無し**（両リポの推論経路） | `grep -rn "os\.environ\|os\.getenv"` → **0 件**。env を読むのは pydantic-settings（`config.py:10-16`）と依存側（huggingface_hub 等）だけ | **本体は env でしか触れない**（§2）ことの裏 | 断定 |
| a7 | `__file__` 依存のパス | **1 箇所** | `irodori_tts/codec.py:62` `local_repo = Path(__file__).resolve().parents[2] / "dacvae"` | package を `<root>/irodori_tts/codec.py` に置くと `<root>/../dacvae` を見る。**存在しなければ素通り**（実射で問題なし） | 断定 |
| a8 | `cwd` 依存 | **2 つ** | (i) `.env`＝`config.py:13` `env_file=".env"`（**CWD 相対**）／(ii) `voices_dir` 既定＝`config.py:41` `Path("voices")`（CWD 相対）。**起動時に `mkdir(parents=True, exist_ok=True)`**（`voices.py:58-60` ← `app.py:104` `voice_registry.ensure_dir()`） | **ランチャは CWD を固定するか、`IRODORI_VOICES_DIR` を絶対パスで渡すこと**。書込不可 CWD だと `startup()` で例外→起動失敗 | 断定（実射で `voices` が作られた） |
| a9 | ログ檔の位置 | **檔に書かない** | `__main__.py:12-15` `logging.basicConfig(level=INFO, format="%(levelname)s:%(name)s:%(message)s")`＝**ハンドラ既定＝stderr**。`FileHandler` は両リポに **0 件** | ログは**親プロセスがパイプで取る**しかない（§2-5） | 断定 |
| a10 | HF キャッシュ位置 | **コードは触らない** | `grep -rn "HF_HOME\|HF_HUB_OFFLINE\|TRANSFORMERS_OFFLINE"` → 両リポ **0 件**。決めるのは `SP:huggingface_hub/constants.py:157-168`（`default_home = ~/.cache` → `HF_HOME` → `HF_HOME/hub`）・`:175-179`（`HF_HUB_CACHE`）・`:192`（`HF_HUB_OFFLINE`） | **`HF_HOME` を env で専用フォルダに固定できる**（`08` §5-B と同じ） | 断定 |
| a11 | torch の DLL 探索 | **package 相対＋`os.add_dll_directory`** | `SP:torch/__init__.py:183`（`dll_paths`）・`:234` `os.add_dll_directory(dll_path)`・`:268` 失敗時に `os.environ["PATH"]` へ前置 | **venv 非依存**＝embed でもそのまま通る（実射で torch import 成功） | 断定 |
| a12 | MSVC ランタイム | **`msvcp140.dll` が別途要る** | `SP:torch/__init__.py:237-240` が `vcruntime140.dll` / **`msvcp140.dll`** / `vcruntime140_1.dll` を `ctypes.CDLL`。`:245` 逐語「Microsoft Visual C++ Redistributable is not installed, this may lead to the DLL load failure.」／embed zip 実測＝`vcruntime140.dll` 120,400 B・`vcruntime140_1.dll` 49,776 B は**在り**、**`msvcp140.dll` は無し**（`22` §4b・本便も 35 檔の一覧で再確認） | **`vc_redist.x64.exe`（≒25 MB）＋導入 1 段が残る**（報告 §5-1 のとおり） | 断定 |
| a13 | `__pycache__` の書込 | **書く** | 実測＝embed python で `modx.py` を import したら `__pycache__/modx.cpython-312.pyc` が生成。書けない場合の挙動＝CPython 3.12 `Lib/importlib/_bootstrap_external.py:1232-1244` 逐語「# Could be a permission error, read-only filesystem: just forget about writing the data.」 | **致命ではない**が、読取専用に置くと起動が毎回遅い。`PYTHONDONTWRITEBYTECODE=1` で止められる（embed でも効く＝§1-B） | 断定 |
| a14 | 依存側の `.pth` | **この構成では不要** | 実測＝`import site` 無しで torch/transformers/soundfile/Server が全部通った。lab 箱の `.pth` は `_virtualenv.pth`（venv 専用）・`coloredlogs.pth`（`COLOREDLOGS_AUTO_INSTALL` が無ければ no-op）・`distutils-precedence.pth`（setuptools shim）の 3 つだけ | `08` §4-D が挙げた `protobuf-3.19.6-nspkg.pth` は **protobuf 7.36 では存在しない**＝**`import site` 必須という見立ては、protobuf を 4 以上にすれば消える** | 断定（この依存集合で） |
| a15 | 依存側の `importlib.metadata` | **要る**＝`*.dist-info` を同梱すること | `SP:transformers/utils/import_utils.py:69` `importlib.metadata.version(distribution_name)`・`SP:transformers/utils/versions.py:101`。transformers 12 檔・huggingface_hub 2 檔で使用 | **package ディレクトリだけコピーしては駄目。`*.dist-info/` も一緒に運ぶ** | 断定 |

### 1-B. `._pth` の実挙動（本便の実測・python.org の原文つき）

原文＝python.org「Using Python on Windows」§4.2 The embeddable package（`https://docs.python.org/3/using/windows.html#the-embeddable-package`・2026-09-04 取得）逐語：
「**The embedded distribution is a ZIP file containing a minimal Python environment. It is intended for acting as part of another application, rather than being directly accessed by end-users.**」
「**Tcl/tk (including all dependents, such as Idle), pip and the Python documentation are not included.**」
「**A default `._pth` file is included, which further restricts the default search paths**」
「**Third-party packages should be installed by the application installer alongside the embedded distribution. Using pip to manage dependencies as for a regular Python installation is not supported with this distribution ... In general, third-party packages should be treated as part of the application ("vendoring")**」
§4.8 Finding modules（同ページ `#finding-modules`）逐語：「**This will ignore paths listed in the registry and environment variables, and also ignore `site` unless `import site` is listed.**」

| 実測 | 結果（逐語） | 効き方 |
|---|---|---|
| `sys.path`（既定 `._pth`） | `['...\\py312\\python312.zip', '...\\py312']` | `._pth` の行がそのまま。**それ以外は一切足されない** |
| `sys.flags` | `isolated 1 / no_site 1 / ignore_environment 1` | `._pth` の存在だけで isolated 相当になる |
| `PYTHONPATH=C:/nonexistent` を設定 | `sys.path` に**現れない**。`os.environ['PYTHONPATH']` では**読める** | **`PYTHONPATH` は使えない＝パスは全部 `._pth` に書く**（11 §10 の env 一式のうち `PYTHONPATH` だけは移植不能） |
| `PYTHONDONTWRITEBYTECODE=1` | `sys.dont_write_bytecode True` | **効く** |
| `PYTHONUTF8=1` | `sys.flags.utf8_mode 1` | **効く**（cp932 事故＝11 §8・21 §2-2 の対策は embed でも同じ手が使える） |
| `PYTHONIOENCODING=utf-8` | `sys.stdout.encoding utf-8` | **効く** |
| `PYTHONWARNINGS=ignore` | `warnings.filters[0] == ('ignore', None, <class 'Warning'>, None, 0)` | **効く** |
| `HF_HUB_OFFLINE=1` / `IRODORI_*` | `os.environ` から普通に読める | **効く**（§2 の制御面はそのまま成立） |
| `pip` / `tkinter` / `ensurepip` | `find_spec` が **3 つとも None** | 公式どおり。**利用者機で pip は使えない＝依存解決は build 機で完結**（`08` §7 の見立てを実射で確認） |
| 絶対パス・forward slash を `._pth` に書く | 通った | — |
| `import site` 無しで torch/Server を import | **成功**（`import torch 2.65〜4.13 s`／`import irodori_openai_tts.app 1.96 s`） | `08` §4-D の「`import site` 必須」は**この依存集合では不要**に更新 |

---

## 2. (b) 本体（C#）が握る制御面

### 2-1. 設定の入口＝環境変数だけ（`config.py:10-77` の全欄）

- 接頭辞＝`IRODORI_`（`config.py:12`）／`.env` は**CWD 相対**（`:13`）／未知の欄は無視（`:15` `extra="ignore"`）。
- **env 名は大小無視**（実測＝`irodori_model_device=cuda:1` が効いた）。pydantic-settings の既定 `case_sensitive=False`。
- **CLI で渡せるのは `--host` `--port` `--reload` の 3 つだけ**（`__main__.py:17-21`）。`--host/--port` は env 既定を上書きする（同 :18-19）。

| env 名 | 檔:行 | 既定 | 型・注意 |
|---|---|---|---|
| `IRODORI_HOST` | `config.py:18` | `0.0.0.0` | `--host` が上書き |
| `IRODORI_PORT` | `:19` | `8088` | `--port` が上書き |
| `IRODORI_API_KEY` | `:20` | `None` | 完全一致 `Bearer <key>`（`14` §9） |
| `IRODORI_CHECKPOINT` | `:22` | `None` | ローカル `model.safetensors`。**`HF_CHECKPOINT` より優先**（`runtime.py:81-85`）。無ければ `FileNotFoundError` |
| `IRODORI_HF_CHECKPOINT` | `:23` | `Aratako/Irodori-TTS-v4-Small` | **v4 のまま**＝配布側で v4.1 に決め打つ必要（報告 §5-2） |
| `IRODORI_CODEC_REPO` | `:24` | `Aratako/Semantic-DACVAE-Japanese-32dim` | 既存パスならローカル解決（`codec.py:67-73`） |
| `IRODORI_MODEL_NAME` | `:25` | `irodori-tts` | `model` 欄の一致検査に使われる |
| **`IRODORI_MODEL_DEVICE`** | `:27` | `auto` | §3・§4 |
| **`IRODORI_CODEC_DEVICE`** | `:28` | `auto` | **忘れると 0 番に残る** |
| `IRODORI_MODEL_PRECISION` | `:29` | `fp32` | `bf16` は cuda/xpu のみ |
| `IRODORI_CODEC_PRECISION` | `:30` | `fp32` | 同上 |
| `IRODORI_CODEC_DETERMINISTIC_ENCODE` | `:31` | `True` | |
| `IRODORI_CODEC_DETERMINISTIC_DECODE` | `:32` | `True` | |
| `IRODORI_COMPILE_MODEL` | `:33` | `False` | **触らない**（`TritonMissing`／`cl` 不在＝`21` §3・`27` 要点 6） |
| `IRODORI_COMPILE_DYNAMIC` | `:34` | `False` | |
| **`IRODORI_PRELOAD`** | `:35` | `False` | §2-3 |
| `IRODORI_MODEL_LOAD_TIMEOUT` | `:36` | `300.0` | 超過で 503 |
| `IRODORI_MAX_CONCURRENT_SYNTHESIS` | `:37` | `1` | |
| `IRODORI_SYNTHESIS_WAIT_TIMEOUT` | `:38` | `300.0` | |
| `IRODORI_EMPTY_CACHE_INTERVAL` | `:39` | `10` | `0` で無効（多 GPU 誤爆の元＝`07` §2-1） |
| **`IRODORI_VOICES_DIR`** | `:41` | `Path("voices")` | **CWD 相対・起動時に mkdir**（§1-A a8） |
| `IRODORI_VOICE_ALIASES_FILE` | `:42` | `None` | 既定は `voices_dir/voices.json`（`voices.py:191`） |
| `IRODORI_DEFAULT_VOICE` | `:43` | `None` | |
| `IRODORI_ALLOW_NO_REF_VOICE` | `:44` | `True` | **no-ref を禁止できない**（`14` T-11） |
| `IRODORI_DEFAULT_RESPONSE_FORMAT` | `:46` | `wav` | |
| `IRODORI_DEFAULT_NUM_STEPS` | `:47` | `40` | |
| `IRODORI_DEFAULT_T_SCHEDULE_MODE` | `:48` | `linear` | |
| `IRODORI_DEFAULT_SWAY_COEFF` | `:49` | `-1.0` | |
| `IRODORI_DEFAULT_DURATION_SCALE` | `:50` | `1.0` | |
| `IRODORI_DEFAULT_MIN_SECONDS` | `:51` | `0.5` | |
| `IRODORI_DEFAULT_MAX_SECONDS` | `:52` | `30.0` | |
| `IRODORI_DEFAULT_CFG_SCALE_TEXT` | `:53` | `3.0` | |
| `IRODORI_DEFAULT_CFG_SCALE_SPEAKER` | `:54` | `5.0` | |
| `IRODORI_DEFAULT_CFG_GUIDANCE_MODE` | `:55` | `independent` | |
| `IRODORI_DEFAULT_CFG_MIN_T` | `:56` | `0.5` | |
| `IRODORI_DEFAULT_CFG_MAX_T` | `:57` | `1.0` | |
| `IRODORI_DEFAULT_CONTEXT_KV_CACHE` | `:58` | `True` | |
| `IRODORI_DEFAULT_MAX_REF_SECONDS` | `:59` | `None` | `_explicit_option` の 3 欄の 1 つ |
| `IRODORI_DEFAULT_REF_NORMALIZE_DB` | `:60` | `-16.0` | 同上 |
| `IRODORI_DEFAULT_REF_ENSURE_MAX` | `:61` | `True` | |
| `IRODORI_DEFAULT_TRIM_TAIL` | `:62` | `True` | |
| `IRODORI_DEFAULT_TAIL_WINDOW_SIZE` | `:63` | `20` | |
| `IRODORI_DEFAULT_TAIL_STD_THRESHOLD` | `:64` | `0.05` | |
| `IRODORI_DEFAULT_TAIL_MEAN_THRESHOLD` | `:65` | `0.1` | |
| `IRODORI_DEFAULT_NUM_CANDIDATES` | `:66` | `1` | |
| `IRODORI_DEFAULT_DECODE_MODE` | `:67` | `sequential` | |
| `IRODORI_DEFAULT_CHUNKING_ENABLED` | `:68` | `True` | |
| `IRODORI_DEFAULT_CHUNK_MIN_CHARS` | `:69` | `80` | |
| `IRODORI_DEFAULT_FIRST_SENTENCE_CHUNK_MIN_CHARS` | `:70` | `None` | 初音を早くするノブ（`14` T-6） |
| **`IRODORI_CORS_ORIGINS`** | `:72` | `[]` | **JSON 配列必須**（下記） |

**`IRODORI_CORS_ORIGINS` の実測（`14` U-6 を閉じる）**

| 与えた値 | 結果 |
|---|---|
| `["http://a.example","http://b.example"]` | `['http://a.example', 'http://b.example']`＝**成功** |
| `http://a.example,http://b.example` | **`pydantic_settings.exceptions.SettingsError: error parsing value for field "cors_origins" from source "EnvSettingsSource"`**（`SP:pydantic_settings/sources/base.py:610`）＝**`Settings()` の生成で例外＝`app.py:98` の import 時に落ちる＝起動即死** |

**device 値の前後空白（実測）**＝`IRODORI_MODEL_DEVICE=' cuda:1 '` → `Settings.model_device` は `' cuda:1 '` のまま、`RuntimeManager._resolve_device` の戻りも **`' cuda:1 '`**（`runtime.py:99` は `strip().lower()` した `raw` で auto 判定するが `:102` は `str(value)` を無加工で返す）→ `torch.device(' cuda:1 ')` で落ちる。**空白だけの値は `raw==""` になり `auto` 扱い**（＝`default_runtime_device()`）。

### 2-2. 起動コマンド

```
<install>/python/python.exe -m irodori_openai_tts --host 127.0.0.1 --port <port>
```

- 埋め込み python でこの形が**通る**（§0-3 の実射）。`python.exe -m` は `._pth` の `.`（＝exe と同じフォルダ）と `._pth` に書いた site-packages から解決する。
- `--reload` は**使わない**（uvicorn が spawn で子プロセスを立てる＝`SP:uvicorn/main.py:614-616`・`SP:uvicorn/_subprocess.py:18,51`）。既定の `reload=False`・`workers=1` なら `server.run()` の**単一プロセス**（同 `:621`）。

### 2-3. 準備完了（ready）の検知

| 段 | 見るもの | 実測 |
|---|---|---|
| 1 | **TCP が繋がるか** | `PRELOAD=true` では**モデル読込が終わるまでポートが開かない**＝uvicorn は lifespan（`app.py:113-116` → `startup()` → `runtime_manager.get()`）を**bind より前**に走らせる。実測で 26 秒間 `Could not connect to server`、その後 200。ログ `runtime loaded in 28.16s`（CPU・v4.1-Small・埋め込み python） |
| 2 | **`/health` の `runtime.loaded`** | `PRELOAD=false` なら 4〜5 秒で 200 を返すが `{"loaded":false,"loading":false,"checkpoint":null}`＝**まだ読んでいない**。初回合成で読み込む（`app.py:110` の逐語ログ「preload disabled; runtime will load on first speech request」） |
| 3 | **`runtime.checkpoint`** | `null` → 実パスに変わるのが第二の目印（`runtime.py:46,68-70`・`14` §7d） |
| 補 | 認証 | `/health` は `require_auth` を持たない唯一のルート（`app.py:165`）＝鍵の設定前でもポーリング可 |
| 罠 | `model_device` の echo | `/health` の `model.model_device` は**設定値の echo**（`app.py:173`）＝`auto` は `auto` のまま。**実際に載ったデバイスは HTTP から分からない**（報告 §4-2・`14` U-2）。分かるのは**ログ**（`runtime loaded in %.2fs` の前後）と合成速度だけ |

**PRELOAD の設計上の含意（事実のみ）**＝`preload=true` は「ポートが開いた＝即使える」を保証するが、**読込に失敗すると起動そのものが落ちる**（`app.py:106-108` は例外を握らない＝`14` T-10）。`preload=false` は「ポートは早く開くが最初の 1 発が 11〜28 秒遅い」。

### 2-4. 停止手段

| 事実 | 根拠 |
|---|---|
| **graceful 用のエンドポイントは無い** | ルートは 7 本のみ＝`GET /health`(`app.py:165`) / `GET /v1/models`(:203) / `GET /v1/audio/voices`(:218) / `POST /v1/audio/voices`(:237) / `GET,PUT,DELETE /v1/audio/voices/{id}`(:259,:272,:299) / `POST /v1/audio/speech`(:314)。`shutdown` を名乗る語は 0 件 |
| `asyncio.shield` は **SSE 経路だけ** | `app.py:777-784` `_run_stream_blocking`＝`asyncio.create_task(...)` を `await asyncio.shield(task)`。`CancelledError` を受けたら **`await task` で完了を待ってから** 再送出。**＝クライアントが切っても、走っているチャンクの合成は最後まで走る** |
| 非ストリーム経路には shield が無い | `app.py:352-366` は素の `await`。切断時にハンドラが実際に cancel されるかは **`14` U-4 のまま未確認** |
| **プロセス kill 時** | `SIGKILL`／`taskkill /F` なら lifespan の後処理は走らない（`lifespan` は `yield` の後に何も書いていない＝`app.py:116`）。GPU メモリ・HF キャッシュは OS が回収する副作用のみ |
| **Ctrl+C / SIGTERM** | uvicorn の graceful shutdown。ただし合成は `loop.run_in_executor(None, ...)`（`app.py:469-471`）＝**既定 ThreadPoolExecutor の非デーモンスレッド**で走るので、**進行中の合成が終わるまでインタプリタが終われない**（CPython の `threading._register_atexit` による）＝**確度＝コード上の推論・未実測** |
| 実射での停止 | 本便は Windows の `kill -9`（＝TerminateProcess）で落とし、ポートは即開放された（§7） |

### 2-5. stdout / stderr の形式

| 出所 | 形式（逐語例） | 行き先 |
|---|---|---|
| `logging.basicConfig`（`__main__.py:12-15`） | `INFO:irodori_openai_tts.runtime:runtime loaded in 28.16s`（`%(levelname)s:%(name)s:%(message)s`） | **stderr**（basicConfig 既定） |
| uvicorn の既定ロガー | `INFO:     Uvicorn running on http://127.0.0.1:8092 (Press CTRL+C to quit)` | **stderr**（`SP:uvicorn/config.py:100` `"stream": "ext://sys.stderr"`） |
| uvicorn の access ログ | `INFO:     127.0.0.1:60370 - "GET /health HTTP/1.1" 200 OK` | **stdout**（同 `:105` `"ext://sys.stdout"`） |
| irodori の生 `print` | `[codec] dacvae: hf://... -> C:\...\weights.pth`（`codec.py:73`・**`flush=True` 付き**） | stdout |
| silentcipher の生 `print` | `ckpt path or config path does not exist! Downloading the model from the Hugging Face Hub...`（`SP:silentcipher/server.py:473,485`・**flush 無し**） | stdout・**バッファされて順序が狂う**（§0-3 の実射で最後から 2 行目に出た） |
| ランタイム内メッセージ | `app.py:819` `logger.info("irodori runtime: %s", message)`＝`X-Irodori-Messages` と同文 | stderr |

**＝親（C#）が見張るなら stderr の `INFO:irodori_openai_tts.*` 行が本命**。「起動できた」の目印は `Uvicorn running on http://...`、「ロード終わった」は `runtime loaded in %.2fs`。**stdout は順序を信用しない**（`PYTHONUNBUFFERED=1` を渡せば直る＝embed でも `PYTHON*` は効く・§1-B。**未実測**）。

### 2-6. 1 プロセス 1 デバイス（多 GPU＝プロセス複数・ポート複数）の根拠

| 檔:行 | 内容 |
|---|---|
| `Irodori-TTS-Server/src/irodori_openai_tts/runtime.py:28` | `self._runtime: InferenceRuntime | None = None`＝**マネージャは 1 スロット** |
| 同 `:31-34` | `get()` は `self._runtime is not None` なら即返す＝**device 違いの 2 本目を持てない** |
| 同 `:48-61` | `RuntimeKey` を**設定から 1 回だけ**組む（要求ごとに device を選べない） |
| `Irodori-TTS/irodori_tts/inference_runtime.py:1487-1496` | `get_cached_runtime()`＝**単一キャッシュ**。`RuntimeKey` が違えば旧 runtime を捨てて作り直す（`unload` は Server から呼ばれない＝`07` 要点 6） |
| `app.py:98-100` | `settings` / `runtime_manager` / `voice_registry` は**モジュール読込時の 1 個**＝プロセス内で 1 セット |
| `app.py:31,37` `_synthesis_semaphore` | 同時合成はセマフォ 1（既定 `max_concurrent_synthesis=1`）でさらに直列化 |

→ **2 枚の GPU を同時に使うなら、`IRODORI_MODEL_DEVICE`/`IRODORI_CODEC_DEVICE` と `--port` を変えたプロセスを 2 本立てる**。振り分けは本体側の仕事（報告 §4-2 の「複数 GPU で 2 サーバ」と同じ）。

---

## 3. (c) GPU の列挙と index の安定性

### 3-1. 起動前に GPU 一覧を得る手段の比較

| 手段 | 何が返るか | 所要（実測） | 前提 | 確度 |
|---|---|---|---|---|
| **`nvidia-smi -L`** | 逐語仕様＝「**List each of the NVIDIA GPUs in the system, along with their UUIDs.**」（`https://docs.nvidia.com/deploy/nvidia-smi/index.html`・2026-09-04）。出力例＝`GPU 0: NVIDIA GeForce RTX 3090 (UUID: GPU-xxxxxxxx-....)` | **未計測**（本機・3090 機とも NVIDIA 無し／3090 機は司令官の手元） | ドライバ導入済み。**Windows での実行檔の所在は原文に記載が無い**（マニュアルは「64bit versions of Windows starting with Windows Server 2008 R2」を支援とだけ書く）＝`C:\Windows\System32\nvidia-smi.exe` は**未確認** | 仕様＝断定／所在・所要＝**未確認** |
| **`nvidia-smi --query-gpu=... --format=csv`** | 逐語＝「Information about GPU. Pass comma separated list of properties you want to query.」例 `--query-gpu=pci.bus_id,persistence_mode`。`--format=csv` は**必須**（「csv - comma separated values (MANDATORY)」）。`noheader`・`nounits` が付けられる | 同上 | 同上 | 仕様＝断定 |
| **埋め込み python ＋ torch で探す** | `torch.cuda.device_count()` / `get_device_name(i)` / `get_device_properties(i)` | **`import torch` が支配的**＝3090 機 cu130・ローカル SSD で **1.8 s**（`27` 表 2 `d_torch_guard`）／NAS 越しだと **311〜563 s**（同）／本便の埋め込み python＋CPU torch で **2.65〜4.13 s**／稼働機 ROCm torch で **4.373 s**。`device_count()` は import 後 **0.071 s**、`get_device_name(0)` は **0.002 s**（本便実測） | 配布物一式が要る（≒2 GB） | 断定（実測） |
| **素の埋め込み python の起動だけ** | — | `python.exe -c "pass"` ＝ **133〜315 ms**（本便実測・3 回）／lab venv の python は 199〜236 ms | — | 断定（実測） |
| **C# の DXGI 列挙**（`IDXGIFactory::EnumAdapters`） | アダプタ名・LUID・VRAM。**DirectML の `device_id` と同じ順**（§3-3） | 未計測（本便は C# を書いていない） | Windows のみ。**CUDA の index とは別物**（§3-2） | 仕様＝断定／所要＝未確認 |

→ **「一覧を出すだけ」なら torch を起こすのは高い**（1.8〜4.4 秒＋2 GB の展開物）。`nvidia-smi` は速いが AMD には無い。**AMD 側の同等＝`amd-smi` / `rocm-smi`**（本機にあるかは**未確認**）。

### 3-2. CUDA の index 順は既定で `FASTEST_FIRST`＝**nvidia-smi 順とも DXGI 順とも一致しない**

原文＝NVIDIA CUDA Programming Guide 5.2 CUDA Environment Variables
（`https://docs.nvidia.com/cuda/cuda-programming-guide/05-appendices/environment-variables.html`・2026-09-04 取得）

| 変数 | 値 | 説明（原文の要旨・逐語部は「」） | 既定 |
|---|---|---|---|
| `CUDA_VISIBLE_DEVICES` | 「A comma-separated sequence of GPU identifiers.」 | 識別子は **①`nvidia-smi` の 0 始まり序数** ②**`nvidia-smi -L` 形式の GPU UUID 文字列（短縮可）** ③ MIG の `MIG-<GPU-UUID>/<gi>/<ci>` | 未設定＝全 GPU 可視／空文字＝**0 台** |
| **`CUDA_DEVICE_ORDER`** | `FASTEST_FIRST`＝「Devices enumerated from fastest to slowest using a heuristic」／`PCI_BUS_ID`＝「Devices enumerated by PCI bus ID in ascending order」。「PCI bus IDs can be obtained via `nvidia-smi --query-gpu=name,pci.bus_id`」 | — | **`FASTEST_FIRST`** |

nvidia-smi 側の原文（`https://docs.nvidia.com/deploy/nvidia-smi/index.html`・2026-09-04）逐語：
「**The specified id may be the GPU's 0-based index in the natural enumeration returned by the driver, the GPU's board serial number, the GPU's UUID, or the GPU's PCI bus ID (as domain:bus:device.function in hex).**」
「**It is recommended that users desiring consistency use either UUID or PCI bus ID, since device enumeration ordering is not guaranteed to be consistent between reboots and board serial number might be shared between multiple GPUs on the same board.**」

**⇒ 事実**：
1. `cuda:0` / `cuda:1` の番号は **CUDA ランタイムの列挙順**で、既定は速さ順（`FASTEST_FIRST`）。
2. `nvidia-smi` の Index は**ドライバの自然列挙順**で、NVIDIA 自身が「**再起動をまたいで一定である保証は無い**」と書いている。
3. DirectML の `device_id` は **DXGI の列挙順**（§3-3）。
→ **3 つの番号体系が別々**であり、原文が示す「揃える手段」は **(i) `CUDA_DEVICE_ORDER=PCI_BUS_ID` で CUDA 側を PCI 順に固定** か **(ii) UUID / PCI bus ID で照合**の 2 つ。

### 3-3. DirectML（⒝）の `device_id` の意味

原文＝ONNX Runtime docs, DirectML Execution Provider（`https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html`・2026-09-04 取得）逐語の要旨：
「**The device ID corresponds to the enumeration order of hardware adapters as given by `IDXGIFactory::EnumAdapters`.**」／API＝`OrtStatus* OrtSessionOptionsAppendExecutionProvider_DML(OrtSessionOptions* options, int device_id);`／
多 GPU 注意＝「in systems with multiple GPU's, the primary display (GPU 0) is often not the most performant one, particularly on laptops with dual adapters where battery lifetime is preferred over performance」。

- **＝DXGI 順であって CUDA 順でも nvidia-smi 順でもない**。しかも「0 番＝主ディスプレイ＝最速とは限らない」と原文が明言。
- 本機での傍証＝`20` §5・`23` §要点 10＝DirectML EP の **provider options からアダプタ名は取れない**（`{}` が返る）。掴んだ GPU の確認は**性能カウンタの LUID**（`0x11ec8` の 3D エンジン使用率と Dedicated Usage の増分）でしか取れなかった。

### 3-4. torch 側で index を実体に結び付ける手（本便の実測）

稼働機 ROCm venv（`torch 2.13.0+rocm10.0.0`・読むだけ）で `torch.cuda.get_device_properties(0)` の属性を列挙（逐語）：

```
['L2_cache_size','clock_rate','gcnArchName','is_integrated','is_multi_gpu_board','major',
 'max_threads_per_block','max_threads_per_multi_processor','memory_bus_width','memory_clock_rate',
 'minor','multi_processor_count','name','pci_bus_id','pci_device_id','pci_domain_id',
 'regs_per_multiprocessor','shared_memory_per_block','shared_memory_per_multiprocessor',
 'total_memory','uuid','warp_size']
uuid = 30303030-3031-3937-3030-303030303030 ／ name = AMD Radeon(TM) 8060S Graphics
```

- **ROCm ビルドでは `uuid` と `pci_bus_id`/`pci_device_id`/`pci_domain_id` の両方が取れる**＝`nvidia-smi`／DXGI との照合材料が torch 側にある。
- **CUDA ビルドの属性**は本機に無いので stub で確認＝`SP:torch/_C/__init__.pyi:12394-12411` `class _CudaDeviceProperties` に **`uuid: str` は在るが `pci_bus_id` は無い**＝**CUDA では UUID 照合、ROCm では UUID か PCI で照合**、という非対称になる見込み（**stub 由来・CUDA 実機で未確認**）。
- torch は `CUDA_VISIBLE_DEVICES` に **UUID 文字列を書く形も解する**＝`SP:torch/cuda/__init__.py:895-924` `_raw_device_uuid_nvml()`、`:927-948` `_transform_uuid_to_ordinals()`、`:1000-1004`（NVML 経路）。AMD 側は `:859-892` `_raw_device_uuid_amdsmi()`（コメント逐語「Lower-case to match expected HIP_VISIBLE_DEVICES uuid input」）。

### 3-5. ROCm 側の可視化変数と再番号付け

原文＝ROCm Documentation「ROCm environment variables」（`https://rocm.docs.amd.com/en/latest/reference/env-variables.html`・2026-09-04 取得）

| 変数 | 説明 | 既定 | 推奨 OS |
|---|---|---|---|
| `HIP_VISIBLE_DEVICES` | 「Device indices exposed to HIP applications.」（例 `0,2`） | 未設定 | **Windows** |
| `ROCR_VISIBLE_DEVICES` | 「A list of device indices or UUIDs that will be exposed to applications.」（例 `0,GPU-DEADBEEFDEADBEEF`） | 無し | **Linux** |

- **再番号付け**＝ROCm の GPU isolation の文書は「GPU indices are taken **post ROCR_VISIBLE_DEVICES reordering**」＝**絞ったあと 0 から振り直される**（`https://rocm.docs.amd.com/en/docs-6.4.3/conceptual/gpu-isolation.html` 経由の記述・**本便は当該ページの全文取得に失敗（timeout / 404）＝確度は中**）。`CUDA_VISIBLE_DEVICES` の再番号付けと同じ性質。
- **torch 側の実装**＝`SP:torch/cuda/__init__.py:755-780` `_parse_visible_devices()` 逐語：`torch.version.hip` が真なら `HIP_VISIBLE_DEVICES` と `ROCR_VISIBLE_DEVICES` を読み、**両方あれば `HIP_VISIBLE_DEVICES` が優先**（同 :775 コメント「HIP_VISIBLE_DEVICES is preferred over ROCR_VISIBLE_DEVICES」）。HIP の方が ROCR より多いと `RuntimeError`（:772-774）。**`CUDA_VISIBLE_DEVICES` は非 HIP のときだけ**（:757・`var` の初期値）。
  ※ただし ROCm 版 torch でも `var = os.getenv("CUDA_VISIBLE_DEVICES")` を最初に読み、`HIP_/ROCR_` が無ければ**それを使う**（:757 と :779-780 の流れ）＝**AMD でも `CUDA_VISIBLE_DEVICES` は効きうる**（報告 §4-2 の記述と整合。ただし HIP ランタイム自体が見る変数ではない＝torch のカウント計算だけの話**＝確度中**）。

---

## 4. (d) NVIDIA と AMD で同じ「N 番目」指定が通るか

| 事項 | NVIDIA (CUDA) | AMD (ROCm・Windows) | Intel (XPU) | 根拠 |
|---|---|---|---|---|
| torch の device 文字列 | `cuda:N` | **`cuda:N`**（ROCm も `cuda` を名乗る） | `xpu`（**index 不可**） | `inference_runtime.py:59-62`（cuda は index 保持）・`:69-71` 逐語 `XPU device index is not supported. Use 'xpu'.`（`07` §1-4） |
| `IRODORI_MODEL_DEVICE` に書く値 | `cuda` / `cuda:1` | **`cuda` / `cuda:1`**（`rocm` と書くと torch の `RuntimeError`＝`14` §6-2） | `xpu` のみ | `14` §6-1・§6-2 の実測表 |
| ROCm 分岐のコード | — | **0 行**（`07` 要点 5）。上流自身も `compose.rocm.yaml:12` で `IRODORI_MODEL_DEVICE: cuda` | — | `07` §1-4 |
| 可視 GPU 制限の env | `CUDA_VISIBLE_DEVICES` | **`HIP_VISIBLE_DEVICES`**（Windows 推奨）／`ROCR_VISIBLE_DEVICES`（Linux） | `ZE_AFFINITY_MASK` 等（**本便は未調査**） | §3-5 |
| index の意味 | CUDA ランタイム順（既定 `FASTEST_FIRST`） | HIP の列挙順（絞ったあと 0 から振り直し） | — | §3-2・§3-5 |
| bf16 | 可 | 可 | 可 | `inference_runtime.py:96-100` |

→ **「device 文字列の `cuda:N`」は NVIDIA と AMD で同じ書き方が通る**（＝本体の UI が 1 本で済む）。
→ **違うのは「可視 GPU を絞る env の名前」だけ**（`CUDA_VISIBLE_DEVICES` vs `HIP_VISIBLE_DEVICES`）。
→ **Intel Arc（xpu）は index を拒否する**＝「N 番目」の概念が無い（`07` §1-4・`inference_runtime.py:70-71`）。

---

## 5. (e) 配布物の中で GPU 指定がどこに置かれるか（**選択肢を挙げるだけ・決めない**）

| # | 置き場 | 成立の根拠 | 事実として付く条件 |
|---|---|---|---|
| E1 | **ランチャ（本体 C#）が子プロセスに環境変数を注入** | 設定の入口は env だけ（§2-1）。前例＝`yomiwakechan2/Engines/Irodori/IrodoriConstants.cs:216-230` が既に precision を env 注入している（報告 §4-2） | `MODEL_DEVICE` と `CODEC_DEVICE` の**2 本**が要る（`config.py:27-28`）。値の白名簿検査は**本体側の仕事**（サーバは素通し＝`runtime.py:102`） |
| E2 | **配布物に `.env` を同梱** | `config.py:13` `env_file=".env"`＝**CWD 相対**。`14` §0-3 で「CWD に `.env` を置かなければ全部コード既定」を確認済み | CWD を固定しないと読まれない。env（プロセス環境）と併用した場合の優先順は pydantic-settings の規則（env > .env）＝**本便未実測** |
| E3 | **可視 GPU を絞る env（方式 A）＋ device は `auto`** | `SP:torch/cuda/__init__.py:755-780`。上流無改変で gradio 側にも効く（報告 §4-2） | ベンダで変数名が変わる（§4）。**E1/E3 は排他**（両方やると二重の番号付け） |
| E4 | **起動 bat / cmd に `set` を書く** | — | **`local-additions/Irodori-TTS-Server/irodori-server-gpu.bat:4` 逐語「This file must stay CRLF + ASCII: UTF-8/LF broke cmd parsing and silently dropped the set lines.」**＝無言で落ちた前例あり。yomiwakechan2 側は**bat 経由を不採用と裁定済み**（`docs/log.md:5503-5505`・`decisions.md:8305-8306`＝報告 §5-1） |
| E5 | **サーバの CLI 引数** | **不可**＝`__main__.py:17-21` に device 引数が無い | — |

---

## 6. 主席への注意

### 6-1. 本便で**新たに確定**したこと（報告に足せる）

| # | 事実 | どこに効く |
|---|---|---|
| N-1 | **埋め込み Python で Server が実際に起動し `/health` 200 を返した**（`import site` 無し・`._pth` だけ） | 報告 §5-2「python-embed 化の可否（成立・条件つき）」の「条件つき」のうち **`._pth` と `site` の項は『`import site` は不要』に更新**できる（protobuf を 4 以上にすれば `nspkg.pth` が消えるため）。U-8 の「embed の `._pth` 細部」は**解消** |
| N-2 | **`._pth` があると `PYTHONPATH` が無視される**（`sys.flags.ignore_environment=1`）。ただし `PYTHONUTF8`／`PYTHONIOENCODING`／`PYTHONDONTWRITEBYTECODE`／`PYTHONWARNINGS`／`IRODORI_*`／`HF_*` は**効く** | ⒜のランチャ設計＝**パスは `._pth`、それ以外は env** という切り分けが事実として立つ |
| N-3 | **`IRODORI_CORS_ORIGINS` は JSON 配列必須**。カンマ区切りは `SettingsError` で**起動即死** | `14` U-6 を閉じる。⒜のインストーラが `.env` を書くなら JSON で書く必要 |
| N-4 | **env 名は大小無視**／**device 値の前後空白は素通し**（`' cuda:1 '` のまま torch へ）／**空白だけなら `auto` 扱い** | `14` T-10 の白名簿検査に「trim してから検査」を足す根拠 |
| N-5 | **PRELOAD=true ではモデル読込が終わるまでポートが開かない**（実測 26 秒 refused → 200・`runtime loaded in 28.16s`） | 本体の ready 待ちは「接続リトライ」＋「`runtime.loaded`」の 2 段。**タイムアウトは 30 秒では足りない**（CPU で 28 秒・NAS なら更に） |
| N-6 | **stdout はバッファされて順序が狂う**（`silentcipher/server.py:473` に flush 無し）／**access ログは stdout・他は stderr** | C# のログ取り込みは **stderr の `INFO:irodori_openai_tts.*` を本命**にする |
| N-7 | **`voices` ディレクトリは起動時に mkdir される**（CWD 相対） | 読取専用の導入先だと**起動で落ちる**＝`IRODORI_VOICES_DIR` を絶対パスで渡すのが要件 |
| N-8 | **`*.dist-info` の同梱が要る**（transformers が `importlib.metadata.version()` を使う） | 配布物を「package フォルダだけコピー」にすると壊れる |
| N-9 | **`CUDA_DEVICE_ORDER` の既定は `FASTEST_FIRST`**＝CUDA の index は nvidia-smi 順とも DXGI 順とも一致しない。nvidia-smi 自身が「index は再起動をまたいで一定の保証が無い」と明記 | 「GPU を N 番で覚える」設計は**壊れる**。原文が示す安定な鍵は **UUID か PCI bus ID**。torch も `get_device_properties(i).uuid` を持つ |
| N-10 | **DirectML の `device_id` は `IDXGIFactory::EnumAdapters` 順**（原文）＝⒜（torch/CUDA）と⒝（ORT/DML）で**番号体系が違う** | ⒜と⒝で「GPU 選択」の意味が変わる＝本体の器を共通化するなら UUID/LUID など**実体で持つ**必要がある |

### 6-2. **未確認**（本便で埋められなかった）

| # | 事項 | 理由 |
|---|---|---|
| V-1 | **多 GPU 実機での `cuda:N` の実効・範囲外 index の文言** | 本機・3090 機とも 1 枚（報告 U-3 のまま） |
| V-2 | `nvidia-smi.exe` の Windows での**所在**と `-L` の所要 | NVIDIA 機が手元に無い。マニュアルに Windows のパス記載なし |
| V-3 | **CUDA ビルドの `_CudaDeviceProperties` に `pci_bus_id` があるか** | stub（`torch/_C/__init__.pyi:12394-12411`）には無い。ROCm 実機には在った＝**実機確認は 3090 機で 1 射** |
| V-4 | ROCm の「post ROCR_VISIBLE_DEVICES reordering」の**一次ページ全文** | `rocm.docs.amd.com` の gpu-isolation ページが timeout / 404。env-variables ページは取得できた |
| V-5 | **`msvcp140.dll` が無い機で本当に torch が落ちるか** | 本機は VC++ 再頒布が入っている（torch が動く）＝欠落状態を作れない |
| V-6 | 書込不可フォルダでの `__pycache__` 失敗の実挙動 | `icacls` の deny 設定に失敗（CPython のソースで「沈黙して続行」は確認済み） |
| V-7 | `PYTHONUNBUFFERED=1` で stdout の順序が直るか | 未実測（`PYTHON*` が効くことは確認済み） |
| V-8 | `.env` とプロセス env の**優先順** | 未実測（pydantic-settings の規則では env が勝つはず） |
| V-9 | graceful shutdown（Ctrl+C）で**進行中の合成が終わるまで待つか** | コード上の推論（非デーモンの既定 ThreadPoolExecutor）。実測していない |
| V-10 | 不要 ≈500 MiB を外した embed 配置で起動するか | 本便は lab の CPU 箱をそのまま参照した＝**削っていない**（報告 U-8 の残り） |

---

## 7. 停止域の確認（無改変の証明）

```bash
git -C upstream/Irodori-TTS        status --short --ignored   # -> 出力なし
git -C upstream/Irodori-TTS-Server status --short --ignored   # -> 出力なし
find upstream -name "__pycache__" -not -path "*/.git/*"       # -> 0 件
netstat -ano | grep -E ":(8088|7861|8092)\b" | grep LISTENING # -> 無し
```

- 全コマンドに `PYTHONDONTWRITEBYTECODE=1`・`HF_HUB_OFFLINE=1` を付けた。**HF への通信は 0**（既存 snapshot を読んだだけ）。
- **8091 は本便の着任時点で別席のプロセス（PID 51476・`AppData\Roaming\uv\python\...\python.exe -m irodori_openai_tts --host 127.0.0.1 --port 8091`・17:34:33 起動）が掴んでいた**。本便はそれに触れず **8092** を使い、使用後に `kill -9` して開放を確認した（`8092 free`）。
- 本便が書いたのは **`lab/tmp/embed-test/**`（py312 展開・work/・pycache-test/）と本ノート**のみ。`lab/.venv` へのパッケージ追加も行っていない（**参照しただけ**）。
- 稼働機 venv（`C:/irodori-TTS-server/.../.venv-rocm/Scripts/python.exe`）は **§3-4 の属性列挙 1 回だけ読取実行**。GPU は列挙のみで合成していない。

### 再検分用に残した物

| 物 | パス | 使い方 |
|---|---|---|
| 展開済み埋め込み Python | `lab/tmp/embed-test/py312/`（`python312._pth` は本便が書き換えた形のまま） | `python.exe -m irodori_openai_tts --port <8092 以外の空きポート>` で即再現できる |
| 実射の作業ディレクトリ | `lab/tmp/embed-test/work/`（`server.log`・`preload.log`・`health.json`・`health2.json`・`voices/`） | §0-3 の逐語ログの正本 |
| `__pycache__` 試験 | `lab/tmp/embed-test/pycache-test/` | §1-A a13 |

---

## 主席追記（2026-09-04・3090 実射で解消した未確認）

- **V-2 解消**＝`nvidia-smi` は Windows では `C:\WINDOWS\system32\nvidia-smi.EXE`（PATH 上）。`-L` の所要 0.04 s（`33` 追記・`lab/out/3090-results-ssd-ref/gpu_props.json`）。
- **V-3 解消**＝CUDA ビルド（torch 2.10.0+cu130）の `_CudaDeviceProperties` にも `uuid` と `pci_bus_id`／`pci_device_id`／`pci_domain_id` が実在（stub の欠落は stub 側の不備）。§3-4 の「非対称になる見込み」は撤回＝**NVIDIA でも AMD でも UUID・PCI の両方で照合できる**。
