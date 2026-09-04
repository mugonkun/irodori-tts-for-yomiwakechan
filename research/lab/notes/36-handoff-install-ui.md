# 36 — 配布版 irodori-TTS への引き継ぎ（要求①②③⑤⑥＝導入・UI・単体 TTS・CPU・暗黙 GPU）

> 席＝第 10 次サブ席（opus）／担当＝本体（yomiwakechan2）の暫定要求のうち **① Python 不要の導入／② 独立配布パッケージ＋UI／③ VOICEVOX のような単体 TTS／⑤ CPU 合成可・遅さは不問／⑥ UI で GPU・CUDA 版・パラメータ・参照ボイスを試す（本体司令は UI で選んだ GPU／CUDA 版を暗黙に使う）**。
> 実施 2026-09-04。**設計は決めない**＝事実（檔:行・ノート番号）と選択肢・受け入れ条件の数値だけ。
> 相対パスの基点＝`C:/Users/mugonkun/source/repos/irodori-native-research/`。yomiwakechan2 はリポ根 `C:/Users/mugonkun/source/repos/yomiwakechan2/`（C# だけ `yomiwakechan2/` が 1 段挟まる）。
> 停止域＝upstream clone・yomiwakechan2・稼働機・HF キャッシュは **1 バイトも変えていない**（§8）。8088／7861 は起動していない。GPU は使っていない。実射は `lab/.venv`（CPU）の TestClient のみ（サーバプロセスは立てていない＝8093 も未使用）。

## 要点（10 行以内）

1. **①は実射で成立済み**（`30` §0-3）。条件は 4 つ＝vc_redist・絶対パス `IRODORI_VOICES_DIR`・`*.dist-info` 同梱・**パスは `._pth`／設定は env**。配布 DL は cu130 で **≈5.4 GiB**（実行系 ≈2.1＋モデル ≈3.3）。
2. **②の UI 素材は上流に 1 つだけある＝`gradio_app.py`（660 行）**。**device と precision の Dropdown を持ち、`get_cached_runtime` で再起動なしに載せ替える**（`inference_runtime.py:1485-1499`）＝⑥の「試す」用途に唯一近い既製品。**ただし choices は型名のみ（`cuda`/`cpu`・index 不可）・env を 1 欄も読まない・CLI に device 引数なし**（`gradio_app.py:80-89,413-435,640-646`）。
3. **Server は UI を持たない**（html/static/templates 0 件・ルート 7 本）。**UI と Server は別プロセス**にせざるを得ない（gradio は自前で uvicorn を持つ）が、**同一 site-packages を共有できる**（両方 `python.exe -m` で起動・`._pth` は 1 本）。
4. **③＝VOICEVOX 型に足りないのは「話者列挙」だけ**。VOICEVOX＝`GET /version`→`GET /speakers`→`POST /audio_query`→`POST /synthesis`（2 段）。Irodori＝`GET /health`→（列挙なし）→`POST /v1/audio/speech`（1 段）。**`GET /v1/audio/voices` が /speakers の座に嵌まる**が、現アダプタは**使っていない**（擬似キャラ 1 件固定・`IrodoriCapabilityBuilder.cs:26-45`）。
5. **「参照ボイス＋名付け＝話者」は Server 側で成立する（本便で実射）**＝日本語名は **ファイル直置き・`voices.json` の alias なら列挙も解決も通る**が、**`POST /v1/audio/voices`（upload API）は 400**（`voices.py:24,151-155` の `^[A-Za-z0-9_-]+$`）。**「デフォルト」＝`{"デフォルト":{"no_ref":true}}` の alias 1 行で成立**（§4-2）。
6. **⑤の最大の落とし穴＝本体の現行既定 `bf16` は CPU 機で 1 発も通らない**。`resolve_runtime_dtype` が **`ValueError: precision='bf16' currently requires CUDA or XPU device.`**（`inference_runtime.py:306-310`・本便実射）。`IrodoriConstants.cs:181-186` が「CPU×bf16 は未測・動作見込み」と書いた懸念は**外れ＝硬い失敗**（§6-1）。
7. **⑤＝CUDA wheel は NVIDIA 無し機でも import は通る**（司令官機に cu130 を実際に入れて `torch.cuda.is_available()=False`・`21` §4）。**ただし CUDA wheel で CPU 合成まで通した実射は無い**（当時 `-SkipBench`）＝**未確認**（§6-2）。
8. **⑥の「暗黙の GPU」は成立する**＝GPU は **Server プロセスの env 2 本**（`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`・`config.py:27-28`）で決まり、**要求 JSON に GPU 欄は要らない**（1 プロセス 1 デバイス・`runtime.py:28,48`）。**ただし「いま何で動いているか」は HTTP から取れない**（`/health` は設定の生 echo・`29` §4）＝**新リポが埋めるべき穴**。
9. **CUDA 版の切替（cu130／cu126）は「site-packages の差し替え」**＝差は実質 `torch/` だけ（cu130 2,649.5 MB／cu126 3,937 MB・`21` §4・`24`）。venv 2 本持ちは **+3.9 GB**、差し替えは **DL 1.78 GiB／2.47 GiB を都度**。速度は同値（`27`）＝選択軸はサイズとドライバ要件だけ。
10. **起動・停止・ready・ログ**＝CLI は `--host/--port/--reload` の 3 つだけ・停止は **kill のみ**・ready は「TCP 開通」＋「`runtime.loaded=true`」の 2 段・**タイムアウト 30 s では足りない**（CPU 28.16 s）・ログは **stderr が本命**（`30` §2-2〜2-5）。

---

## 0. 本便の実射（一次データ）

- 道具＝`lab/.venv/Scripts/python.exe`（CPython 3.12.14・torch 2.10.0+cpu・**NVIDIA 無し**）＋ `fastapi.testclient.TestClient`。**サーバプロセスは立てていない**。
- 檔＝`lab/bin/36_voice_naming.py`（本便が作成）／出力＝`lab/out/36-voice-naming.json`／作業＝`lab/tmp/36/`。
- 環境＝`PYTHONPATH="upstream/Irodori-TTS;upstream/Irodori-TTS-Server/src"`・`PYTHONDONTWRITEBYTECODE=1`・`HF_HUB_OFFLINE=1`・`PYTHONUTF8=1`。`IRODORI_VOICES_DIR` は `lab/tmp/36/voices`（絶対パス）・`IRODORI_PRELOAD=false`（モデルは 1 バイトも読んでいない）。

---

## 1. 要求① Python を知らなくても導入できる事

### 事実

| 事実 | 檔:行／ノート |
|---|---|
| 埋め込み Python **3.12.10**（zip 11,133,606 B・展開 22,501,299 B・35 檔）を展開し `python312._pth` に site-packages ほか 3 パスを書くだけで、**`import site` 無し**に torch・transformers・soundfile・Server が読め、`python.exe -m irodori_openai_tts --port 8092` が起動して **`/health` 200**。`PRELOAD=true` でも `runtime loaded in 28.16s` | `30` §0-2・§0-3 |
| 壊れる前提が**ほぼ無い**＝`sys.executable` 再実行・`multiprocessing`・`subprocess`（ffmpeg 以外）・`pkg_resources` が **grep 0 件** | `30` §1-A |
| **`._pth` があると `PYTHONPATH` は無視される**（`isolated=1／no_site=1／ignore_environment=1`）。ただし `PYTHONUTF8`・`PYTHONIOENCODING`・`PYTHONDONTWRITEBYTECODE`・`IRODORI_*`・`HF_*` は**効く** | `30` §1-B・N-2 |
| Python 3.12 embed は **3.12.10 が最後のバイナリ**。**3.13.9 embed（10.4 MiB）が存在**＝退路 | `17` (k)・報告 §5-2 |

### 成立するか

**成立する（実射済み）。** 「⒜は見込み」から「実射で成立」へ変わった（報告 §10-1）。

### 制約・落とし穴（数値つき）

| # | 穴 | 帰趨 | 数値 |
|---|---|---|---|
| 1 | **`msvcp140.dll`**（embed zip に無い・torch が要求＝`torch/__init__.py:237-240`） | `vc_redist.x64.exe` 同梱＋導入 1 段 | **25 MiB**。**欠落機での実挙動は未確認**（`30` V-5） |
| 2 | **`voices_dir` が CWD 相対・起動時に mkdir**（`config.py:41`・`voices.py:58-61`・`app.py:104`） | **絶対パスの `IRODORI_VOICES_DIR` が必須**。読取専用の導入先だと**起動で落ちる** | — |
| 3 | **`*.dist-info` の同梱が要る**（transformers が `importlib.metadata.version()` を呼ぶ） | 「package フォルダだけコピー」は壊れる | `30` N-8 |
| 4 | **`.env` も CWD 相対**（`config.py:13`）／**`IRODORI_CORS_ORIGINS` は JSON 配列必須**（カンマ区切りは `SettingsError` で**起動即死**） | CWD 固定か env 直注入 | `30` N-3 |
| 5 | `__pycache__` は**書けなくても沈黙して続行**（CPython `_bootstrap_external.py:1232-1244`）＝Program Files 配下でも致命でないが毎回コンパイル | — | `30` §1-A a13 |
| 6 | **torchcodec を外すなら上流パッチ 2 箇所**＝`inference_runtime.py:1518`（**Server の参照音声経路**）と `:1536` の `except RuntimeError` が `ImportError` を取りこぼす | 外さないか、参照音声を soundfile 固定にするか、パッチを版ごとに再当て | `18` §3-e・報告 §5-2 |
| 7 | **モデル 3.3 GiB の取得導線**＝重み 2,922 MiB＋コーデック 410 MiB＋透かし 65 MiB＋tokenizer 6 MiB。既定 checkpoint は **v4-Small のまま**（`config.py:23`）＝**配布側が v4.1 に決め打つ**必要 | 音声認識内包便の型（sha256・原子的着地・`%LOCALAPPDATA%`・`yomiwakechan2/decisions.md:8549-8554`）を写経 | 報告 §5-3・`18` §7 |
| 8 | **`licenses/` 7 種**が配布側の義務（CUDA EULA・cuDNN SLA・MSVC・DirectML MSLT・LGPL 申し出ほか）＋ Aratako 3 repo に LICENSE 檔が無い＝**MIT 全文を自作同梱** | 配布物に `licenses/` を切る | 報告 §1-3・§1-1 |
| 9 | **配布経路**＝実行系だけで **≈2.1 GiB**＝GitHub Releases の 1 資産上限（2 GiB）超 | 分割・外部ストレージ・段階取得のいずれか | 報告 §5-2 U-8 |
| 10 | **起動直後・初見形状のスパイク**は**残る**（同文 12 連射で 1 発目 40.8 s・3 発目 19.4 s・定常 4.1 s） | 常駐＋起動時ウォームアップ射で緩和。根治は未確認 | `19` §2-2・報告 §5-1 |

### 配布サイズ（DL・一次でバイト一致）

| 区分 | 実行系 DL | モデル DL | 合計 DL | 導入後ディスク |
|---|---|---|---|---|
| cu130（embed 10.6＋torch 2.10.0+cu130 **1,780.9 MiB**＋非 torch ≈250＋vc_redist 25） | **≈2.1 GiB** | ≈3.3 GiB | **≈5.4 GiB** | venv **3,191.6 MB**（`torch/` 2,649.5・`torch/lib` 2,554.1）＋モデル ≈3.5 GB ≈ **6.9 GB** |
| cu126（torch **2,469.9 MiB**） | ≈2.7 GiB | ≈3.3 GiB | ≈6.0 GiB | venv **4,478 MB**（`torch/` 3,937 MB・`24`）＋モデル ≈ 8.0 GB |
| CPU のみ（torch **108.4 MiB**） | ≈0.4 GiB | ≈3.3 GiB | **≈3.7 GiB** | venv 1.24 GB（削って ≈0.75 GB）＋モデル ≈ 4.3〜4.8 GB |

- 削れる分＝**≈500 MiB**（gradio・wandb・llvmlite/numba・jedi・matplotlib・torchcodec・peft・datasets・IPython ≈370＋pandas/pyarrow ≈130）＋`torch/include`・`*.lib` 93 MiB。**削って起動する確認は未実施**（`30` V-10）。**②で UI に gradio を使うなら gradio 43〜82 MB は削れない**（`lab/notes/venv-sizes.txt`）。
- **wav 固定なら FFmpeg 不要**（wav/flac/pcm は soundfile で完結・aac だけ外部プロセス・`audio.py:44-69`）。

### 導入手順の長さ

上流 guide の **21 操作のうち 16 が同梱で消え、残り 6**＝ドライバ確認・空き容量・VC++ 再頒布・動作確認・停止・CPU 復帰（**vc_redist をインストーラが黙って通せれば 5**）。消える主因は「配布側が torch を固定する」こと＝Windows の `--extra rocm` 無言 CPU 化（`pyproject.toml:76`／Server `uv.lock:1871,1874` の `sys_platform == 'linux'` マーカー）が構造的に消える（報告 §5-1）。

### 本体（yomiwakechan2）が決めること

- **モデル 3.3 GiB を同梱するか初回取得にするか**（ライセンス上はどちらも可・差はサイズと導線＝報告 §0-2 の 1）。
- **cu130 既定／cu126 退路**は裁定済み（報告 §0-2 の 6）＝**配布物のどちら側に何を積むか**（§5）。
- **本体は Server を起こすのか、配布アプリが常駐するのか**（§7）。

### 新リポが満たす受け入れ条件（数値）

| # | 条件 |
|---|---|
| A-1 | **Python が入っていない Windows 機**で、インストーラ実行後の**利用者操作 ≤ 6 手**（vc_redist を黙って通せれば ≤ 5）で `/health` 200 に到達する。 |
| A-2 | 初回 DL 合計 **≤ 5.5 GiB**（cu130・モデル込み）／実行系単体 **≤ 2.2 GiB**。CPU 版は **≤ 3.8 GiB**。 |
| A-3 | 導入先を **`C:\Program Files\` 配下（書込不可）** に置いても起動する＝`IRODORI_VOICES_DIR`・`HF_HOME`・作業フォルダが**すべて書込可の絶対パス**を指す。`__pycache__` 不可でも起動する。 |
| A-4 | `licenses/` に **7 種**＋Aratako 3 repo 分の MIT 全文が実在し、UI から開ける。 |
| A-5 | vc_redist 欠落機で**理由が読める形で止まる**（DLL 名を出す）＝黙って `ImportError` を出さない。**現状未確認**（`30` V-5）＝新リポの検分項目。 |
| A-6 | 起動時ウォームアップ射を 1 発撃ち、**初発話の 40 秒級スパイクを利用者に見せない**。 |

---

## 2. 要求② 本体から独立した配布パッケージ＋UI を持つアプリ

### 事実（UI の素材）

| 素材 | 実体 | 判定 |
|---|---|---|
| **`upstream/Irodori-TTS/gradio_app.py`（660 行）** | **device／precision の Dropdown を持つ**（`:413-435`）。`Load Model`／`Unload Model` ボタン（`:439-440`・`:387-389`）。参照音声は `gr.File`（`:458`）・Speaker Inversion も `gr.File`（`:467`）。パラメータは Number/Dropdown で一通り（`t_schedule_mode`・`cfg_guidance_mode` 等） | **⑥の用途に最も近い既製品** |
| 同・device の中身 | `list_available_runtime_devices()`＝**`["cuda","cpu"]` のような型名のリスト**（`inference_runtime.py:80-89`）。**index（`cuda:1`）は返らない**。`gr.Dropdown` に `allow_custom_value` の指定は無い＝**利用者が `cuda:1` と打てない** | **⑥の複数 GPU には不足** |
| 同・precision | `list_available_runtime_precisions(device)`＝cuda/xpu なら `["fp32","bf16"]`・**それ以外は `["fp32"]` のみ**（`:96-100`）。device 変更で choices が差し替わる（`:56-63`・`:614`） | **⑤の CPU×bf16 事故を UI が構造的に防いでいる**＝新リポが写すべき挙動 |
| 同・載せ替え | **`get_cached_runtime(key)` が旧 runtime を `unload()` して作り直す**（`inference_runtime.py:1485-1499`）＝**プロセス再起動なしに device／precision／checkpoint を替えられる** | **Server にはこの機能が無い**（`runtime.py:28,31-34,48-61`＝1 スロット・要求ごとに device を選べない） |
| 同・env | **env を 1 欄も読まない**。CLI は `--server-name/--server-port/--share/--debug` の 4 つで **device 引数ゼロ**（`gradio_app.py:640-646`） | **⑥の「暗黙の GPU」を gradio 側では持てない**＝別 UI か改造 |
| **`gradio_app_voicedesign.py`（678 行）** | 参照音声なしの Voice Design 特化 | 参考 |
| **Server 側の UI** | **無い**＝`upstream/Irodori-TTS-Server/` に html/static/templates が 0 件。ルートは 7 本（`app.py:165,203,218,237,259,272,299,314`） | **UI は新リポが建てる** |

### 成立するか

**成立する。ただし「UI」の実体は 3 択で、どれも Server とは別プロセス。**

| 案 | 中身 | 成立の根拠 | 費用・制約 |
|---|---|---|---|
| U-a | **上流 gradio をそのまま同梱** | 上流無改変で動く（稼働機 7861 の実績） | gradio **43〜82 MB** が配布に載る。**device は型名のみ・env を読まない**＝⑥の「UI で選んだ GPU を本体司令が暗黙に使う」を**満たせない**（UI の選択が Server に伝わらない） |
| U-b | **gradio を改造して env を書かせる／index を足す** | `list_available_runtime_devices` を差し替え・`allow_custom_value=True` で index を通す（`resolve_runtime_device` は `cuda:N` を通す＝`inference_runtime.py:59-62`） | **上流パッチの保守**が版ごとに増える |
| U-c | **UI を新規に建て（C#／Web いずれでも）、UI が「設定→env→Server 起動・再起動」を担う** | **設定の入口は env だけ**（`config.py:12-72` の 50 欄・`30` §2-1）＋CLI 3 つ（`__main__.py:17-21`）＝**UI が env を組んで子プロセスを起こす以外に口が無い** | Server の起動・停止・ready・ログ取り込みを UI が実装（§7）。gradio を配布から外せる（≈43〜82 MB 減） |

**⇒「UI が『設定→env→Server 再起動』を担う」という構図の根拠**＝⑴ Server は**要求ごとに device／precision を選べない**（`runtime.py:48-61` の `RuntimeKey` は設定から 1 回だけ組む）。⑵ **device／precision を替える唯一の手段はプロセス再起動**（`06` §5-4「モデル切替＝無い・再起動のみ」）。⑶ **設定の入口は env だけ**。⑷ **停止は kill のみ**（`30` §2-4）。この 4 つが揃うので、**「UI が env を組む → Server を起こし直す → ready を待つ」以外の構図が原文上ありえない**。

### 制約・落とし穴

- **UI と Server は別プロセスだが、同一 site-packages を共有できる**＝`._pth` は 1 本で足りる（gradio も Server も `python.exe -m` で起動）。**ただし両方を同時に起こすとモデルが 2 セット載る**（gradio の `get_cached_runtime` と Server の `RuntimeManager` は別プロセス＝VRAM・RAM が 2 倍）。VRAM 実測＝bf16 で **+4,128 MB**（3,868→7,993 MB・`29` §6-2）。
- **UI が Server を再起動する間、本体からの読み上げは落ちる**＝`/health` は接続拒否（`PRELOAD=true` なら**読込完了までポートが開かない**・実測 26 s）。
- **gradio は Server 依存に含まれる**＝Server の `pyproject.toml:11` が `irodori-tts @ git+…` を引き、その `pyproject.toml:11` が `gradio>=5.0.0`＝**「Server 土台・gradio 非同梱」は能動的に削る作業**であって既定ではない。

### 本体が決めること

- **UI をどこまで本体の面と重ねるか**（本体にも「声の値」欄・元栓の精度 2 択が既にある＝`IrodoriConstants.cs:190-232`）。二重になる欄（精度・device）の**どちらが正**か。
- **UI が Server を常駐させるのか、本体の元栓が起こすのか**（§7）。

### 新リポが満たす受け入れ条件（数値）

| # | 条件 |
|---|---|
| B-1 | UI から **device・precision・checkpoint・CUDA 版**を選び直したあと、**Server が再起動して `/health` の `runtime.loaded=true` に戻るまで ≤ 60 s**（CPU 実測 28.16 s＋プロセス起動＋余裕）。**30 s は不可**（`30` N-5）。 |
| B-2 | UI が選んだ値は**永続**し、**本体からの司令は要求 JSON に device を含めずに**その値で動く（＝⑥の「暗黙」）。 |
| B-3 | UI と Server が**同時にモデルを載せない**（UI は試聴時だけ Server 経由で撃つ、または UI が載せている間は Server を止める）。同時に載せると VRAM **+4,128 MB**（bf16 実測）。 |
| B-4 | UI 自体の追加サイズ **≤ 100 MB**（gradio 流用なら 43〜82 MB が上限の目安）。 |
| B-5 | UI は `licenses/`（7 種）と「いま何で動いているか」（§6）を必ず表示できる。 |

---

## 3. 要求③ VOICEVOX のような単体 TTS として扱える事

### 事実＝本体側が VOICEVOX に対して行っていること（読むだけ）

| 段 | VOICEVOX（`yomiwakechan2/Engines/Voicevox/`） | Irodori（同 `Engines/Irodori/`） |
|---|---|---|
| 接続先 | **`http://127.0.0.1:50021` 固定**（`VoicevoxConstants.cs:23`。「将来ポートを設定化する際はここだけ」） | **`http://127.0.0.1:8088` 固定**（`IrodoriConstants.cs:36`。**`localhost` 禁止**＝IPv6 先行で毎要求 +1.2〜2.0 s） |
| 発見 | **`GET /version`（5 s）→ `GET /speakers`（15 s）**（`VoicevoxDiscovery.cs:50-52`）。話者 0 件なら失敗 | **`GET /health` の 1 段だけ（5 s）**。**body は解釈しない**（`IrodoriDiscovery.cs:64-66`・裁定 10） |
| 話者列挙 | `VoicevoxSpeakersParser.Parse` → キャラ×スタイル | **無い**＝**固定の擬似キャラ 1 件**（`IrodoriCapabilityBuilder.cs:26-45`）。「列挙すべき話者が存在しない」と明記 |
| 合成 | **2 段**＝`POST /audio_query?text=…&speaker=N` → `POST /synthesis?speaker=N` → wav（`VoicevoxApiClient.cs:40-58`） | **1 段**＝`POST /v1/audio/speech` → wav（`IrodoriApiClient.cs:53-63`） |
| パラメータ | `/audio_query` 応答 JSON を書き換えて `/synthesis` へ（`VoicevoxQueryModifier`）。**記述子は本体が持つ 6 本の静的表**（`VoicevoxConstants.cs:103-118`） | **要求 JSON の `irodori` ネストに直接**（`IrodoriSpeechRequest.cs`）。記述子は本体が持つ **5 本**（プロンプト／シード／参照音声／話速／音量） |
| エンジンを起動するか | **しない**（未起動＝理由つき失敗）。起動は**元栓**が担う | **同じ**（`IrodoriDiscovery.cs:17-20`） |
| 音量 | API の `volumeScale` | **API に無い**＝アダプタ内の wav 乗算（`IrodoriVolumeScaler.cs`・VOICEPEAK と同型） |

### 対応表（Irodori Server が VOICEVOX ENGINE の座に嵌まるか）

| VOICEVOX の口 | Irodori Server の対応物 | 判定 |
|---|---|---|
| `GET /version` | **`GET /health`**（認証不要の唯一のルート・`app.py:165`・モデルを読まない） | **○**（版文字列は返らない＝`/v1/models` が `{"id":"irodori-tts"}` 1 件） |
| `GET /speakers` | **`GET /v1/audio/voices`**（`app.py:218-234`）＝`{"object":"list","data":[{"id","object","ref_wav","ref_wavs","ref_latent","ref_latents","ref_embed","no_ref"}]}` | **○ 形はある。現アダプタは使っていない**（`IrodoriApiClient.cs:17` 逐語「voices の GET／PUT／DELETE・/v1/models は使わない」） |
| `POST /audio_query` | **無い**（1 段） | **－**（2 段は不要＝本体側で分岐が減る） |
| `POST /synthesis` | **`POST /v1/audio/speech`** | **○** |
| 話者 ID | VOICEVOX＝**整数の styleId** | Irodori＝**文字列の `voice`**（`voices.py:24` の制約は upload 経路のみ・§4-2） |
| 固定ポート | 50021 | **8088**（`config.py:19` の既定＝Server の既定と本体の定数が一致） |

**⇒ 単体 TTS として扱うのに足りないのは「話者列挙を本体が使う」ことだけ。口は既にある。**

### 制約・落とし穴

- **`GET /v1/audio/voices` は認証つき**（`app.py:218` に `Depends(require_auth)`）。`/health` だけが無認証＝**列挙段を足すと `IRODORI_API_KEY` 設定時に発見の形が変わる**（`14` §9）。
- **`/v1/audio/voices` は要求ごとにディレクトリを走査**（キャッシュ無し・`voices.py:168-186`）＝起動後に足したファイルが即反映される代わりに、voices が巨大だと遅い（**未計測**）。
- **`/v1/models` は常に 1 件**・モデル切替は無い（`06` §5-4）。
- **VOICEVOX と違い「エンジンの版」を名乗る口が無い**＝配布アプリが版を名乗るなら**自前の口**（例：`/health` に版を足す）か、UI 側の表示だけ。

### 本体が決めること

- **`GET /v1/audio/voices` を発見の第 2 段に足すか**（＝擬似キャラ 1 件をやめて話者列挙に移るか）。これは**裁定 4／6 とその司令官オーバーライド（2026-08-30）の再改訂**にあたる（`IrodoriConstants.cs:44-56`）。
- **話者名の器**＝本体の `CharacterCapability.DisplayName` に何を出すか（voice_id そのままか、別名表を持つか）。

### 新リポが満たす受け入れ条件（数値）

| # | 条件 |
|---|---|
| C-1 | 配布アプリを起こすだけで、**本体の既存 `IrodoriDiscovery`（`GET /health` 5 s）が無改造で成功する**＝互換の下限。 |
| C-2 | `GET /v1/audio/voices` が **話者 N 件を 15 s 以内**に返す（VOICEVOX の `/speakers` タイムアウトと同値）。voices 100 件でも満たすこと（走査コストは**未計測**＝新リポの検分項目）。 |
| C-3 | 「参照ボイスなし」が **必ず 1 件**（名前「デフォルト」）列挙に出る。 |
| C-4 | ポートは **8088 既定**（本体の `IrodoriConstants.cs:36` と一致）で、**変更可能**（`--port`／`IRODORI_PORT`）。 |
| C-5 | `/v1/audio/speech` の応答は **wav・48 kHz・mono**（既存の実測どおり）＝本体の再生経路を変えさせない。 |

---

## 4. 「参照ボイスファイル＋名付け＝話者」「参照なし＝デフォルト」（③④の境目・本便の実射）

### 4-1 実射の条件

`lab/tmp/36/voices/` に `ずんだもん.wav`・`alice.wav`（中身はダミー 16 B＝列挙は中身を見ない）と `voices.json`＝`{"デフォルト":{"no_ref":true},"四国めたん":"alice.wav","空白 入り":"alice.wav"}` を置き、TestClient で叩いた。生データ＝`lab/out/36-voice-naming.json`。

### 4-2 結果（逐語）

| 試したこと | 結果 | 檔:行 |
|---|---|---|
| `GET /v1/audio/voices` | **6 件**＝`alice`／`none`／`ok_name-1`／**`ずんだもん`**（`ref_wav` にフルパス）／**`デフォルト`**（`no_ref:true`）／**`空白 入り`**／`四国めたん`。**日本語名も空白入りもそのまま列挙される** | `app.py:218-234`・`voices.py:63-70,168-186` |
| `resolve("ずんだもん")` | **成功**＝`voice_id="ずんだもん"`・`ref_wav="…/voices/ずんだもん.wav"` | `voices.py:72-94` |
| `resolve("四国めたん")`（alias） | **成功**＝`ref_wav="…/voices/alice.wav"`（alias は相対パスを `voices_dir` 基準で解決・`voices.py:250-254`） | 同 |
| `resolve("空白 入り")` | **成功**（空白入りの名前も通る） | 同 |
| **`resolve("デフォルト")`** | **成功**＝`no_ref=true`・`ref_wav=null`＝**「参照なしの話者名『デフォルト』」が alias 1 行で成立** | `voices.py:66-67,84-85` |
| `resolve("デフォルト2")`（未登録） | `KeyError: "Unknown voice='デフォルト2'. Put a reference audio file in …, add an alias file, or use voice='none'."` → **400** | `voices.py:92` |
| **`POST /v1/audio/voices` に `voice_id="ずんだもん2"`** | **400**＝逐語 `voice_id must contain only ASCII letters, numbers, underscores, or hyphens.` | **`voices.py:24,151-155`**（`VOICE_ID_PATTERN = ^[A-Za-z0-9_-]+$`） |
| 同・`voice_id="空白 入り2"` | **400**（同文） | 同 |
| 同・`voice_id="ok_name-1"`（2 回目） | **409**＝`Voice 'ok_name-1' already exists. Use PUT to replace it.` | `voices.py:129-133` |

### 4-3 読み取り（断定）

1. **話者名の日本語化は成立する。ただし upload API 経由では成立しない。** 走査経路（`_scan`＝`path.stem` をそのまま voice_id にする）と alias 経路（`voices.json` のキー）は **`validate_voice_id` を通らない**。検査は `POST`／`PUT` の 1 経路だけ（`voices.py:125`）。
2. **⇒「名付け」を持つなら、新リポは `voices.json` を書く**（またはファイル名を日本語にして置く）。**現行の本体アダプタ `IrodoriVoiceRegistry` は upload API を使い、名前を `ywk-<sha256 先頭 12 桁>` にすることで ASCII 制約を回避している**（`IrodoriVoiceRegistry.cs:16-24`）＝**「名付け」を持ち込むと現行の器と正面から衝突する**。
3. **`no_ref` の別名は自由に作れる**＝`NO_REF_IDS`（`none/no_ref/no-ref/null/text-only`・`voices.py:23`）は**ASCII 固定**だが、alias `{"デフォルト":{"no_ref":true}}` は**その手前で当たる**（`resolve` は alias を `NO_REF_IDS` 判定の**後**に見るが、alias が `no_ref=true` の `VoiceSpec` を返すので結果は同じ）。
4. **`IRODORI_ALLOW_NO_REF_VOICE=false` にすると `none` は 400 になるが、alias の `{"no_ref":true}` は生き残る**（`voices.py:68-69,80-81` は `none` の合成と 5 語の受理だけを塞ぐ）＝**未実射**（本便は既定 true でのみ確認）。

### 4-4 新リポが満たす受け入れ条件（数値）

| # | 条件 |
|---|---|
| D-1 | UI で「参照 wav を選び、日本語の名前を付ける」→ **`/v1/audio/voices` の列挙に同じ日本語名で出る**まで **≤ 3 s**（ファイル走査のみ・モデル読込を伴わない）。 |
| D-2 | 参照ボイス 0 件のときも **必ず「デフォルト」1 件**が列挙に出る。 |
| D-3 | 名前の重複・不正文字（パス区切り・改行）を**UI が弾く**＝ファイル名／JSON キーとして安全な集合に落とす。**Server 側は走査経路を検査しない**＝新リポの責務。 |
| D-4 | 参照 wav の推奨は **10〜30 s**（上流推奨「同一話者の短いクリップ複数・合計 30 s で飽和」＝`upstream/Irodori-TTS/docs/parameters.md:59-63`）。上限 **120 s**（超過は**頭から**切り詰め＋warning）。 |

---

## 5. 要求⑤ CPU 合成はできてもいい（遅さはケアしない）

### 5-1 事実＝「CPU でも起動する」には精度の切替が要る（本便の実射で確定）

| 与えた組み | 戻り | 檔:行 |
|---|---|---|
| `device=cpu` / `fp32` | **OK**＝`torch.float32` | `inference_runtime.py:304-306` |
| **`device=cpu` / `bf16`** | **`ValueError: precision='bf16' currently requires CUDA or XPU device.`** | **`inference_runtime.py:306-310`**（逐語） |
| `device=cuda` / `fp32`（NVIDIA 無し機） | **`ValueError: CUDA device requested but torch.cuda.is_available() is False.`** | `inference_runtime.py:59-61` |
| `device=auto` / `fp32` | **OK**＝`cpu` / `float32` | `inference_runtime.py:80-93` |
| `list_available_runtime_devices()`（当機） | **`["cpu"]`** | 同 `:80-89` |
| `list_available_runtime_precisions("cpu")` | **`["fp32"]`**（＝bf16 は選択肢に出ない） | 同 `:96-100` |

### 5-2 落ち方（`29` §2-2＝司令官機・GPU を隠したとき）

- **bf16＝起動時に見える形で落ちる**（`ValueError`）。`PRELOAD=true` なら `Application startup failed. Exiting.`・exit 3・**ポートは開かない**（`29` §1-3）。`false` なら**全合成が 500**。
- **fp32＝黙って CPU に落ちて 200**。2 文字・10 steps で **266.974 s**（GPU 0.78 s の約 340 倍）、`/health` は `"auto"` のまま。**＝bf16 既定は速度だけでなく安全装置**。

### 5-3 **本体の現行既定は CPU 機で 1 発も通らない**（引き渡すべき訂正）

`yomiwakechan2/Engines/Irodori/IrodoriConstants.cs:224-232` の `PrecisionEnvironment()` は、**fp32 を明示したときだけ何も設定せず、bf16・空・欠落・未知値はすべて `IRODORI_MODEL_PRECISION=bf16`＋`IRODORI_CODEC_PRECISION=bf16` を設定する**（既定＝bf16）。同檔 `:181-186` は「**CPU 環境 × bf16 の実挙動は未測**（torch の CPU 側 bfloat16 は動作見込み＝落ちずに走ると見ている）」と書いている。
→ **本便の実射でこの懸念は解消し、結論は逆**＝**CPU では例外で硬く落ちる**（`resolve_runtime_dtype` が device 型で弾く＝torch の bfloat16 が動くかどうか以前の話）。**既定のまま CPU 機で使うと、`PRELOAD=true` なら起動失敗、`false` なら全合成 500。**

### 5-4 CPU の速度（参考・「遅さはケアしない」の実際の値）

| 条件 | 値 | 出所 |
|---|---|---|
| fp32・40 steps・短文・参照なし | 端から端まで **14.53 s**・**RTF 3.86** | `31` §5-2 |
| 同＋参照 10 s／30 s | **17.46 s（+20 %）／19.81 s（+36 %）**・RTF 5.20／6.04 | 同 |
| 10 steps | **RTF 1.79**（実時間に**届かない**） | 報告 §10-4 の 14 |
| 実機 probe（本体側の記録） | **約 8.5 s ＋ 0.48 s/字**（200 字で約 104 秒） | `IrodoriConstants.cs:170-174`（probe P-4） |
| 埋め込み python・CPU・PRELOAD | **モデル読込 28.16 s**（26 s はポートが開かない） | `30` §0-3 |

### 5-5 CUDA wheel は GPU 無し機で動くか

- **入る・import できる・`torch.cuda.is_available()=False` になる**＝司令官機（NVIDIA 無し）に `torch 2.10.0+cu130` を実際に入れた実測（`21` §4）。venv 3,191.6 MB・`c_torch` 38.8 s。`--compile-model` は `ValueError: CUDA device requested but torch.cuda.is_available() is False.`。
- **ただし「CUDA wheel で CPU 合成まで通した」実射は無い**（当該走行は `-SkipBench`）。**断定しない**＝新リポの検分項目（受け入れ条件 E-3）。
- 一般に torch の CUDA wheel は CUDA ランタイム・cuDNN の DLL を同梱し（`torch/lib` 2,554.1 MB）遅延ロードするため CPU 実行は成立する見込みだが、**本調査は原文でも実射でも裏を取っていない**。

### 5-6 本体が決めること

- **元栓の精度 2 択の既定を device 連動にするか**（＝`cpu` を選んだら自動で fp32・`IrodoriConstants.cs:224-232` の改訂）。
- **fp32 の黙った CPU 転落を層で塞ぐか**（報告 §10-4 の 12）。

### 5-7 新リポが満たす受け入れ条件（数値）

| # | 条件 |
|---|---|
| E-1 | **GPU が 1 枚も無い機で、UI／ランチャが自動で `fp32` に落として起動する**＝利用者が精度を触らずに合成が 200 で返る。 |
| E-2 | GPU を後から抜いた／ドライバが壊れたときも、**500 の連発ではなく理由が読める 1 行**（「GPU が見つからないので CPU・fp32 で起動しました」）を UI とログに出す。 |
| E-3 | **CUDA 版の配布物を NVIDIA 無し機に入れて、CPU・fp32 で `/v1/audio/speech` が 200 を返す**（**現状未実射**＝新リポの受け入れ試験に入れる）。 |
| E-4 | CPU での合成タイムアウトは **本文 1 字あたり ≥ 0.5 s ＋ 固定 10 s** を見込む（実測 8.5 s＋0.48 s/字）。200 字で **≥ 110 s**。本体の現行値（`SynthesisTimeoutPerChar` 系）と突き合わせること。 |
| E-5 | ready 待ちは **≥ 60 s**（CPU 読込 28.16 s＋起動＋余裕）。**30 s は不可**。 |

---

## 6. 要求⑥ UI で GPU／CUDA 版／パラメータ／参照ボイスを試す・本体司令は UI の選択を暗黙に使う

### 6-1 「暗黙の GPU」は原文上そうならざるを得ない

| 事実 | 檔:行 |
|---|---|
| **device は Server プロセスの env 2 本でしか決まらない**＝`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`（既定 `auto`） | `config.py:27-28` |
| **要求 JSON に device 欄は無い**（`SpeechRequest`／`IrodoriOptions` 44 欄に device は無い） | `app.py:37-95`・`06` §1-3 |
| **CLI にも無い**（`--host/--port/--reload` の 3 つ） | `__main__.py:17-21` |
| **1 プロセス 1 デバイス**＝`RuntimeManager` は 1 スロット・`RuntimeKey` は設定から 1 回だけ | `runtime.py:28,31-34,48-61`・`inference_runtime.py:1487-1496` |
| gradio は env を読まない＝**gradio で選んだ device は Server に伝わらない** | `gradio_app.py:640-646` |

**⇒ 本体の要求 JSON に GPU 欄は不要（＝「暗黙」で正しい）。** 本体が送るのはパラメータだけでよい。

### 6-2 ただし「いま何で動いているか」が取れない（新リポが埋める穴）

| 手段 | 取れるか | 檔:行 |
|---|---|---|
| `GET /health` | **取れない**＝`model.model_device` は**設定値の生 echo**（`"auto"` も `"cuda:0 "` も `"gpu"` もそのまま） | `app.py:173`・`29` §4-1 |
| 起動ログ | **device が 1 文字も出ない** | `29` §4-2 |
| 合成ごとの `[runtime] start synthesize model_device=…` | **env 文字列の echo**＝`auto` は `cuda` としか出ず **index 不明** | `inference_runtime.py:1053` |
| 合成速度で推定 | 可能だが間接（CPU 転落は約 340 倍） | `29` §2-2 |

**⇒ 新リポの UI／API が埋めるべき穴（選択肢・決めない）**
① 配布アプリが**起動前に別プロセスで torch 列挙**して実体を確定し、UI と自前の口に出す（費用＝`import torch` が支配的＝**3090 SSD 1.2〜1.8 s／embed+CPU torch 2.65〜4.13 s**・`device_count()` 自体は 0.2 ms・素の embed python は 133〜315 ms）。
② **`nvidia-smi -L`**（Windows で `C:\WINDOWS\system32\nvidia-smi.EXE`・PATH 上・**0.04 s**・`33` 追記）＝速いが **NVIDIA 専用**。
③ Server に `/health` 1 行を足す提案を上流へ（報告 §10-4 の 10）。
④ 起動時に 1 発焼いて所要で判定（CPU 転落なら桁が違う）。

### 6-3 GPU の同定＝index で覚えると壊れる

| 番号体系 | 順序 | 原文 |
|---|---|---|
| CUDA の `cuda:N` | **`CUDA_DEVICE_ORDER` 既定 `FASTEST_FIRST`**（速さ順）。`PCI_BUS_ID` で固定できる | NVIDIA CUDA Programming Guide 5.2 |
| `nvidia-smi` の Index | ドライバの自然列挙順。NVIDIA 自身が **「enumeration ordering is not guaranteed to be consistent between reboots」** と明記 | nvidia-smi manual |
| DirectML の `device_id` | `IDXGIFactory::EnumAdapters` 順（**CUDA と別体系**） | ORT DirectML EP docs |

- **安定鍵＝UUID か PCI bus ID**。**CUDA ビルド（3090・torch 2.10.0+cu130）でも `uuid`・`pci_bus_id`／`pci_device_id`／`pci_domain_id` が全部取れる**（`33` 追記）。torch の `uuid` `19adfe89-…` は `nvidia-smi -L` の `GPU-19adfe89-…` と一致（接頭辞の差だけ）。AMD 側でも取れる（`29` §5）。
- **範囲外 index は 6〜11 秒かけて落ちる**＝`inference_runtime.py:657` の `model.to()`（6.07 s 後）／`codec.py:78`（11.28 s 後）。逐語＝`torch.AcceleratorError: CUDA error: invalid device ordinal / GPU device may be out of range, do you have enough GPUs?`。**層で `trim → ^(cpu|cuda(:\d+)?)$ → device_count()` を通せば 0 秒で弾ける**（`29`・報告 §10-2）。
- **綴り違いは沈黙しない**が形が悪い＝`CUDA:0`（大文字）・`gpu`・`rocm`・**`"cuda:0 "`（末尾空白）**はすべて torch の例外→500（`runtime.py:102` が原文を無加工で返す）。
- **`IRODORI_CODEC_DEVICE` を忘れると codec だけ 0 番に残る**（`config.py:28`）。model と codec を別デバイスにするのは**成立する**（`cuda:0`＋`cpu` でウォーム 1.712 s＝全 GPU 0.777 s の 2.2 倍・跨ぎは `codec.py:266` の 1 行だけ・`29` §3）。

### 6-4 CUDA 版（cu130／cu126）の切替＝venv 2 つか差し替えか

| 案 | 中身 | ディスク | 所要 | 制約 |
|---|---|---|---|---|
| **V-1 site-packages 2 本** | `site-packages-cu130/` と `-cu126/` を並べ、**`._pth` の 1 行を書き換えて再起動** | **+3,937 MB**（cu126 の `torch/` 実測・`24`）／両方で **≈7.1 GB** | 切替 **0 s**（`._pth` 書換＋再起動）＝**最速** | 配布 DL が **≈4.25 GiB**（1,780.9＋2,469.9 MiB）に膨らむ。`._pth` は**パス専管**なので env では切り替えられない（`30` N-2） |
| **V-2 差し替え（都度 DL）** | 選んだ版の `torch`／`torchaudio` だけ入れ替える | 常に 1 本（cu130 3,191.6 MB／cu126 4,478 MB） | **DL 1,780.9 MiB（cu130）／2,469.9 MiB（cu126）**＋展開。当機の回線で `c_torch` **38.8 s**（cu130・展開込み） | 回線に依存。オフライン導入と両立しない |
| **V-3 既定 1 本＋退路の手動手順** | cu130 のみ配布し、cu126 は手順書 | cu130 のみ | — | 裁定済みの形（報告 §0-2 の 6）に最も近い |

- **速度では決まらない**＝3090 の SSD 再走で **cu126 0.213／cu130 0.215（短文 bf16 RTF）**＝同値（`27`）。差はサイズとドライバ要件のみ（cu126＝Windows 最小ドライバ **≥560.76**／cu130＝OS 非依存表で **≥580**・Windows 固有値は原文に無し）。「cu126 なら 527.41 で足りる」は**誤り**（`17` (j)）。
- `cu128` は**袋小路**（索引の最終 torch 2.11.0）。両索引に torch **2.10.0** が実在＝上流の pin はどちらでも通る。

### 6-5 本体が決めること

- **GPU の同定を index で持つか UUID／PCI で持つか**（報告 §10-4 の 9）。
- **本体の「声の値」欄に device を出すか**（現状は元栓に精度 2 択だけ・`IrodoriConstants.cs:190-232`）。

### 6-6 新リポが満たす受け入れ条件（数値）

| # | 条件 |
|---|---|
| F-1 | UI が **GPU を実体（UUID または PCI bus ID）で保存**し、再起動後に index へ解決し直す。列挙の所要 **≤ 5 s**（`import torch` 1.2〜4.4 s ＋ 余裕）。NVIDIA 機では `nvidia-smi -L`（0.04 s）を先に使ってよい。 |
| F-2 | UI で選んだ device は **`IRODORI_MODEL_DEVICE` と `IRODORI_CODEC_DEVICE` の 2 本**に必ず同時に載る。片方だけは不可。 |
| F-3 | 範囲外・綴り違いを **UI 側で 0 秒で弾く**（`trim` → `^(cpu\|cuda(:\d+)?)$` → `device_count()` で範囲検査）。**上流は 6〜11 秒かけて落とす**。 |
| F-4 | **`IRODORI_PRELOAD=true` を既定**にし、掴み損ねを**起動時に**爆発させる（`false` だと全合成 500 で気付きにくい）。 |
| F-5 | 本体の要求 JSON に **device 欄を足さない**（＝⑥の「暗黙」）。代わりに「いま何で動いているか」を **UI と 1 つの口**で返す（`/health` では取れない＝新リポの追加）。 |
| F-6 | CUDA 版の切替後、**RTF が短文 bf16 で 0.25 以下**（3090 実測 0.213〜0.215）であることを UI の試し撃ちで確認できる。 |

---

## 7. 起動・停止・ready・ログ＝本体が Server をどう起動するか（選択肢と材料・決めない）

### 7-1 事実

| 論点 | 事実 | 檔:行／ノート |
|---|---|---|
| 起動 | `<install>/python/python.exe -m irodori_openai_tts --host 127.0.0.1 --port <port>`。**埋め込み python でこの形が通る**（実射）。`--reload` は**使わない**（uvicorn が spawn で子を立てる） | `30` §2-2 |
| ready | **2 段**＝⑴ TCP 開通（`PRELOAD=true` は**読込完了までポートが開かない**＝実測 26 s refused → 200）⑵ `/health` の `runtime.loaded=true`。`checkpoint` が `null`→実パスが第二の目印 | `30` §2-3 |
| 停止 | **kill のみ**（`shutdown` を名乗るルートは 0 件・ルート 7 本）。Ctrl+C／SIGTERM は uvicorn の graceful だが、合成は `run_in_executor` の**非デーモンスレッド**＝**進行中の合成が終わるまで終われない見込み**（**未実測**） | `30` §2-4 |
| 切断 | `asyncio.shield`（`app.py:781`）は **SSE 経路だけ**＝**切断しても合成は走り切る**。中止の粒度は**チャンク単位が上限** | `06` §5-3 |
| ログ | `INFO:irodori_openai_tts.*` は **stderr**／uvicorn の access ログは **stdout**／`silentcipher/server.py:473` の生 `print` は flush 無しで**順序が狂う** ⇒ **親が見張るなら stderr が本命**。目印＝`Uvicorn running on http://…`（起動）・`runtime loaded in %.2fs`（読込完了） | `30` §2-5・N-6 |
| VRAM | kill で完全解放（3,868 → 7,993 → 3,865 MB） | `29` §6-2 |

### 7-2 本体の既存の元栓（読むだけ・引き継ぎ材料）

`yomiwakechan2/Engines/Irodori/IrodoriConstants.cs` は**すでに Server を起こす器を持っている**＝`LaunchKind.ExePath`（`:73`）／パス欄はフォルダも受け中の `Scripts\python.exe` を自動解決（`:157-160`）／標準の起動オプション **`-m irodori_openai_tts --host 0.0.0.0 --port 8088`**（`:152-153`）／作業フォルダ欄／**精度 2 択の env 注入**（`:224-232`）／**発射直後の即死猶予 3 s**（`LaunchEarlyExitGrace`）／**生存プローブ `GET /health` 3 s**（`LaunchProbeTimeout`）／**完了検知 120 s**（`LaunchCompletionTimeout`＝「PRELOAD 環境で API 開通まで bf16 約 31 秒」を根拠に）。

### 7-3 選択肢（決めない）

| # | 形 | 成立の根拠 | 事実として付く条件 |
|---|---|---|---|
| G-1 | **本体の元栓が配布アプリの Server を起こす**（現行の延長） | 器が既にある（§7-2）。パスは配布物のインストール先＝**裁定 16「既定パス候補なし」の改訂**が要る（報告 §0-2 の 4） | 本体が env を組む＝GPU／CUDA 版の選択が**本体側の欄**になる＝**⑥の「UI で選ぶ」と衝突**する（どちらが正かの裁定が要る） |
| G-2 | **配布アプリが常駐し（トレイ等）、Server を自分で起こす。本体は `GET /health` で見つけるだけ** | 本体の `IrodoriDiscovery` は `/health` 1 段で閉じる（`IrodoriDiscovery.cs:64-66`）＝**本体無改造で成立** | 本体の元栓の irodori 行は**不要になる**（または「配布アプリの exe を起こす」1 行に変わる）。**⑥の「UI で選んだ GPU を暗黙に使う」と最も素直に噛み合う** |
| G-3 | **配布アプリが Server を内包（同一プロセス）** | **不可**＝UI（gradio）と Server は別々に uvicorn を持つ。同一プロセスに載せるのは新規実装 | — |
| G-4 | **本体が起こし、UI は別に手で起こす** | 成立するが**設定の二重管理**（UI の選択が本体の起動 env に伝わらない） | — |

### 7-4 新リポが満たす受け入れ条件（数値）

| # | 条件 |
|---|---|
| G-a | 起動要求から **`runtime.loaded=true` まで ≤ 60 s**（GPU）／**≤ 90 s**（CPU）。本体の現行 `LaunchCompletionTimeout=120 s` の内側。 |
| G-b | 起動失敗（範囲外 GPU・精度不整合・checkpoint 不在）は **≤ 15 s で理由 1 行**に落ちる（`PRELOAD=true` なら `model.to()` の 6.07 s／`codec.py:78` の 11.28 s が上限）。 |
| G-c | 停止は **kill で 1 s 以内**にポートと VRAM が解放される（実測どおり）。進行中の合成を待つ挙動は**未実測**＝新リポが確定させる。 |
| G-d | ログは **stderr を本命**に取り込み、`Uvicorn running on` と `runtime loaded in` の 2 行で状態を判定する。stdout の順序は信用しない（`PYTHONUNBUFFERED=1` で直るかは**未実測**＝`30` V-7）。 |
| G-e | ポート衝突時に**別ポートへ退避**するか、理由つきで止まる（8088 は稼働機の既存 Server と衝突しうる）。 |

---

## 8. 停止域の確認（無改変の証明）

```bash
git -C upstream/Irodori-TTS        status --short --ignored   # -> 出力なし
git -C upstream/Irodori-TTS-Server status --short --ignored   # -> 出力なし
find upstream -name "__pycache__" -not -path "*/.git/*"       # -> 0 件
netstat -ano | grep -E ":(8088|7861|8093)\b" | grep LISTENING # -> 無し
```

- 全実行に `PYTHONDONTWRITEBYTECODE=1`・`HF_HUB_OFFLINE=1` を付けた。**HF への通信は 0**（モデルは 1 バイトも読んでいない＝`PRELOAD=false`・合成は 1 度も走らせていない）。
- 本便が書いたのは **`lab/bin/36_voice_naming.py`・`lab/out/36-voice-naming.json`・`lab/tmp/36/**`・本ノート**のみ。yomiwakechan2 は読むだけ（`Engines/Voicevox/`・`Engines/Irodori/` の 8 檔）。GPU は使っていない。**サーバプロセスは 1 本も立てていない**（TestClient のみ）。

---

## 9. 主席への注意

### 9-1 本便で**新たに確定**したこと（報告に足せる）

| # | 事実 | どこに効く |
|---|---|---|
| **P-1** | **日本語の話者名は「ファイル直置き」と `voices.json` の alias では通る（列挙・解決とも実射）が、`POST /v1/audio/voices` は 400**（`voices.py:24,151-155`）。**空白入りも通る／upload では 400** | 要求「参照ボイス＋名付け＝話者」の**実装経路が 1 本に決まる**＝新リポは `voices.json` を書く。**現行 `IrodoriVoiceRegistry`（`ywk-<sha12>` で upload）とは正面から衝突** |
| **P-2** | **`{"デフォルト":{"no_ref":true}}` の alias 1 行で「参照なし＝話者名デフォルト」が成立**（列挙にも出る・`no_ref=true` で解決） | 要求「参照ボイスなしは話者名『デフォルト』」は**Server 無改造で満たせる** |
| **P-3** | **`resolve_runtime_dtype` は CPU×bf16 を `ValueError` で硬く弾く**（逐語 `precision='bf16' currently requires CUDA or XPU device.`・`inference_runtime.py:306-310`）＝**本体の `IrodoriConstants.cs:181-186` の「CPU×bf16 は未測・動作見込み」は外れ** | **既定 bf16 のままでは CPU 機で 1 発も通らない**（`PRELOAD=true` で起動失敗／`false` で全合成 500）。要求⑤の受け入れ条件に直結 |
| **P-4** | **`gradio_app.py` は device／precision の Dropdown を持ち、`get_cached_runtime` で再起動なしに載せ替える**（`:413-435`・`inference_runtime.py:1485-1499`）。ただし **choices は型名のみ（index 不可）・env を読まない・CLI に device 引数なし** | 要求⑥の「UI で試す」に**唯一近い既製品**だが、**そのままでは「UI の選択を本体が暗黙に使う」を満たせない** |
| **P-5** | **`list_available_runtime_precisions(device)` が cpu では `["fp32"]` しか返さない**（`:96-100`）＝**上流 UI は device↔precision を構造的に連動させている** | 新リポの UI／ランチャが写すべき挙動（E-1） |
| **P-6** | **Server は gradio を推移的に引く**（Server `pyproject.toml:11` → Irodori-TTS `pyproject.toml:11` `gradio>=5.0.0`）＝「Server 土台・gradio 非同梱」は**能動的に削る作業**。gradio は venv で **43〜82 MB** | 報告 §5-2 の「不要 ≈500 MiB」の内訳の読み方（②で UI に gradio を使うなら削れない） |
| **P-7** | **`GET /v1/audio/voices` は認証つき**（`app.py:218`）＝**`/health` だけが無認証**。列挙段を発見に足すと `IRODORI_API_KEY` 設定時に発見の形が変わる | 要求③の受け入れ条件（C-2）と本体の `IrodoriDiscovery` 改訂 |
| **P-8** | **Server に UI 資産は 0 件**（html/static/templates なし・ルート 7 本） | 要求②「UI を持つアプリ」は**新リポが建てる**が確定 |

### 9-2 **未確認**（本便で埋められなかった）

| # | 事項 | 理由 |
|---|---|---|
| W-1 | **CUDA wheel を入れた NVIDIA 無し機で CPU 合成が 200 で返るか** | `21` §4 は `-SkipBench`＝import と `is_available()=False` までしか実射が無い。**一般論では動く見込みだが原文の裏は無い＝断定しない** |
| W-2 | `IRODORI_ALLOW_NO_REF_VOICE=false` のとき alias の `{"no_ref":true}` が生き残るか | 本便は既定 `true` でのみ実射（コード上は生き残る＝`voices.py:66-67,80-81`） |
| W-3 | voices が 100 件級のときの `/v1/audio/voices` の所要（要求ごとに `iterdir()`） | 未計測（`06` §3-1 も未計測のまま） |
| W-4 | 日本語ファイル名の参照 wav で**実際に合成が通るか**（`_load_audio` のパス扱い） | 本便は列挙・解決までで、合成は走らせていない（CPU 14.5 s／射・GPU 不使用の縛り） |
| W-5 | UI と Server を同時に起こしたときの VRAM 実測 | 推計（bf16 で 1 セット +4,128 MB＝`29` §6-2）にとどまる |
| W-6 | gradio を埋め込み python で起動できるか | `30` は Server のみ実射。gradio の依存（`gradio_client`・`safehttpx` 等）が `._pth` 環境で読めるかは未実射 |
| W-7 | `._pth` の書き換えで site-packages を切り替える（V-1）実射 | 未実施 |
| W-8 | vc_redist 欠落機での挙動（`30` V-5）・削った ≈500 MiB 配置での起動（同 V-10）・`.env` と env の優先順（同 V-8）・Ctrl+C の待ち（同 V-9） | 前便から未解消 |

### 9-3 主席への進言（材料としてのみ）

1. **要求①②③⑤⑥はすべて「成立する」**。門は 1 つも閉じない。閉じかけるのは **⑤の既定 bf16**（P-3）だけで、これは**本体側の定数 1 つの改訂**（`IrodoriConstants.cs:224-232`）か**新リポのランチャが device↔precision を連動させる**かで解ける。
2. **⑥の「暗黙」は原文上そうならざるを得ない**（§6-1）＝本体の要求 JSON に GPU 欄を足す提案は**不要**。代わりに**「いま何で動いているか」の口が上流に無い**（§6-2）＝**新リポが必ず埋める穴**として起票を推す。
3. **③は「話者列挙を使うかどうか」の 1 点に縮む**（§3）。口（`GET /v1/audio/voices`）は既にあり、日本語名も通る（P-1・P-2）＝**Server 無改造で要求の話者モデルが成立する**。衝突するのは**本体の現行アダプタの器**（`IrodoriVoiceRegistry` の `ywk-<sha12>`）だけ＝**卓の裁定 1 件**（裁定 4／6 の司令官オーバーライドの再改訂）。
4. **②の UI は「gradio 流用」と「新規」の二択で、費用差は 43〜82 MB と保守**。ただし**gradio 流用では⑥の「暗黙」が成立しない**（P-4）＝**流用するなら改造が前提**。
5. **⑥の CUDA 版切替は「site-packages 2 本（+3.9 GB・切替 0 s）」対「都度 DL（1.78／2.47 GiB）」**（§6-4）。**速度では決まらない**（cu126／cu130 同値）＝卓の裁定はサイズとドライバ要件だけ。
