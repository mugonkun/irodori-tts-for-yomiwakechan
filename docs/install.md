# install.md — 導入手順（**案**・便 E で確定）

> **この檔はまだ案である。**インストーラ（便 E）とランチャ（便 D）の実装で確定する。
> 便 A の時点で確定しているのは**方式**（初回取得・per-user・第三者バイナリを配布物に入れない）と
> **数値の根拠**だけで、画面と文言は決まっていない。
> 正典＝`decisions.md` 8・12。受け入れ条件＝`docs/acceptance.md` の「導入」「サイズ」「ドライバ」
> 「起動」「起動失敗」の各行。事実の出典＝`research/`（調査便の写し）。

---

## 0. 方式（確定）

- **実行系（torch と依存 wheel）もモデルも初回取得**する。**第三者バイナリ（wheel・exe・dll・
  モデル）を配布物に入れない**（`decisions.md` 8）。Release 資産＝ランチャ＋取得台帳＋自作分で数十 MB。
- **オフライン導入は用意しない**（同）。
- **per-user 導入**＝管理者権限を求めない。
- 取得したものは**すべて sha256 で検証**する。台帳（`ledger/*.json`）が URL と sha256 の唯一の正本で、
  ランチャ（便 D）とビルド台本（`build/assemble-runtime.ps1`）は**同じ檔を読む**。
- **Python は利用者の機体に入れない**＝python.org の埋め込み Python（embeddable package）を
  アプリのフォルダに展開して使う。これは python.org が公式に案内する用法である
  （"The embedded distribution is a ZIP file containing a minimal Python environment. It is intended
  for acting as part of another application" ／ "third-party packages should be treated as part of the
  application (\"vendoring\")"＝`research/lab/notes/22-runtime-license.md` 表 4c）。

---

## 1. 利用者から見た流れ（**案**・目標 6 操作以内）

| # | 操作 | 何が起きるか | 目安 |
|---|---|---|---|
| 1 | インストーラを実行する | `%LOCALAPPDATA%\Programs\irodori-tts-ywk\` に本体（ランチャ exe・`server/`・`ledger/`・`licenses/`・`voices/`・docs）が入る。数十 MB。 | 数秒 |
| 2 | ランチャを起動する | 初回セットアップ画面が出る。**ライセンス通知**（初回取得で入る第三者物の一覧＝`licenses/first-run-notices.md`）を表示して同意を取る。 | — |
| 3 | GPU と CUDA 版を選ぶ | 検出した GPU を UUID つきで一覧表示。既定 cu130、検出ドライバが 580 未満なら cu126 を**勧める**（自動では切り替えない＝`decisions.md` 4）。 | 列挙 ≤ 5 s |
| 4 | 「取得して導入」を押す | 実行系 ≈2.1 GiB ＋ モデル ≈3.3 GiB ＝ **≈5.4 GiB** を取得して `%LOCALAPPDATA%\irodori-tts-ywk\` に展開する。`vc_redist.x64.exe`（≈25 MB）もここで通す。 | 100 Mbps 級で **≤ 10 分** |
| 5 | 「起動」を押す | `127.0.0.1:18088` で常駐する。準備完了（`runtime.loaded=true`）まで待つ。 | **≤ 120 s**（3090・SSD なら ≤ 60 s） |
| 6 | 読み分けちゃん2 側で配布版エンジンを有効にする | 本体が `/health` で見つける。 | — |

**手数の根拠**＝上流の導入手引き 21 操作のうち **16 が同梱と初回取得で消え、残り 6**
（ドライバ確認・空き容量・VC++ 再頒布・動作確認・停止・CPU 復帰）。**vc_redist をインストーラが
黙って通せれば 5**（`research/lab/notes/36-handoff-install-ui.md`）。
消える主因は「配布側が torch の版を固定すること」で、Windows で `--extra rocm` が無言で CPU 版を
入れてしまう事故（上流 `pyproject.toml:76`／Server `uv.lock:1871,1874` の `sys_platform == 'linux'`
マーカー）が構造的に消える。

---

## 2. 何がどこに入るか

| 場所 | 中身 | 消してよいか |
|---|---|---|
| `%LOCALAPPDATA%\Programs\irodori-tts-ywk\` | ランチャ exe・`server\`（wrapper＋**パッチ適用済みの上流の写し**＝MIT・数 MB のテキスト）・`ledger\`・`licenses\`・`voices\`（プリセット話者）・docs | アンインストーラが消す |
| `%LOCALAPPDATA%\irodori-tts-ywk\runtime\<variant>\` | 埋め込み Python ＋ site-packages（cu130／cu126／cpu／rocm-gfx1151 のいずれか） | 消せる（次回起動で取り直し） |
| `%LOCALAPPDATA%\irodori-tts-ywk\models\` | モデル（`HF_HOME`）＝checkpoint 2.86 GiB・コーデック 410 MB・透かし 65 MiB・tokenizer 6 MiB | 消せる（同上・**3.3 GiB の取り直し**） |
| `%LOCALAPPDATA%\irodori-tts-ywk\voices\` | 利用者の話者（参照 wav）と `voices.json`・`voices.ywk.json` | **利用者の資産＝消さない** |
| `%LOCALAPPDATA%\irodori-tts-ywk\logs\` | ログ | 消せる |
| `%LOCALAPPDATA%\irodori-tts-ywk\settings.json` | 設定（GPU の UUID・CUDA 版・精度など） | 消せる（初期設定に戻る） |

**導入後のディスク使用量 ≈6.9 GB**（venv 3,191.6 MB ＋ モデル ≈3.5 GB の実測＝
`research/lab/notes/27`・`36` §配布サイズ）。

---

## 3. 動く仕組み（保守する人向け）

- 起動は**ランチャが env を組んで
  `runtime\<variant>\python.exe -m ywk_server --host 127.0.0.1 --port 18088` を子プロセスで起こす**。
  **bat は使わない**（CRLF／UTF-8 の文字コード事故の反面教師＝`research/local-additions/`）。
- **パスは `._pth` の専管・設定は env**。`runtime\<variant>\python312._pth` は 5 行・絶対パス・
  forward slash・`import site` なし：

  ```
  python312.zip
  .
  <runtime>/site-packages
  <app>/server
  <app>/server/upstream/Irodori-TTS
  <app>/server/upstream/Irodori-TTS-Server/src
  ```

  **`._pth` があると `PYTHONPATH` は無視される**（`PYTHON*`／`IRODORI_*`／`HF_*` の env は効く）＝
  `research/lab/notes/30-embed-and-gpu-control-facts.md` §0-3 の実射。
- 既定 env はランチャが載せ、wrapper が `os.environ.setdefault` で焼く（ランチャが上書きできる）。
  主なもの＝`IRODORI_HOST=127.0.0.1`・`IRODORI_PORT=18088`・
  `IRODORI_HF_CHECKPOINT=Aratako/Irodori-TTS-v4.1-Small`・`IRODORI_PRELOAD=true`・
  `IRODORI_EMPTY_CACHE_INTERVAL=0`・`IRODORI_ALLOW_NO_REF_VOICE=false`・
  `IRODORI_VOICES_DIR=<絶対パス>`・`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`・
  `PYTHONUTF8=1`・`PYTHONDONTWRITEBYTECODE=1`・`PYTHONUNBUFFERED=1`・`HF_HOME=<models>`・
  `HF_HUB_OFFLINE=1`（初回取得後）。
- 状態判定は stderr の **3 行**＝`ywk_server <版> upstream=…`／`Uvicorn running on`／
  `runtime loaded in`。**422 の本文 echo はログに流さない**（1 発 5 KB＋絶対パス）。

### 3-1 導入で必ず要る 4 条件（実射で確定している）

1. **`msvcp140.dll`**（vc_redist ≈25 MiB）＝埋め込み Python に無く、torch が要求する
   （`torch/lib/c10.dll` の PE インポートテーブル直読＝`research/lab/notes/22` 表 3b）。
   埋め込み Python が同梱するのは `vcruntime140.dll`（120,400 B）と `vcruntime140_1.dll`（49,776 B）だけ。
2. **`IRODORI_VOICES_DIR` を絶対パスで渡す**＝上流の既定は CWD 相対の `Path("voices")` で、
   起動時に `mkdir` する（`config.py:41`・`voices.py:58-61`）＝読取専用の導入先だと**起動で落ちる**。
3. **`*.dist-info` を site-packages に同梱する**＝`transformers` が
   `importlib.metadata.version()` を呼ぶ。wheel を展開するとき `dist-info` を捨てない。
4. **`__pycache__` の書込失敗は沈黙して続行する**（性能だけ落ちる）＝読取専用の導入先でも動くが、
   `PYTHONDONTWRITEBYTECODE=1` を焼いておく。

---

## 4. うまくいかないとき（**案**・便 E で文言を確定）

| 症状 | 原因 | どうする |
|---|---|---|
| 起動直後に落ちて「`msvcp140.dll` が見つかりません」 | vc_redist 未導入 | ランチャの「VC++ 再頒布を導入」を押す。※ **欠落機での実挙動は未確認**（`30` V-5）＝便 E の検分項目 |
| GPU を選んだのに合成が異常に遅い | GPU が掴めず CPU に落ちている | 配布版はこれを**塞ぐ**設計（`cuda` 指定で `torch.cuda.is_available()` が偽なら起動前に exit 2）。それでも遅いときは `/ywk/status` の `device.actual` を見る |
| 「CUDA device requested but torch.cuda.is_available() is False.」 | ドライバ不足か CUDA 版の不一致 | ドライバを更新するか、cu126 版を選び直す（cu130 は ≥ 580／cu126 は ≥ 560.76） |
| 合成は通るが 1 発目だけ極端に遅い | 初見形状のカーネル生成（**Radeon のみ**） | `docs/radeon.md` を見る。CUDA 機では該当する罰は観測されていない |
| 話者一覧に「デフォルト」しか出ない | `voices\` が空 | ランチャで参照 wav を追加する（wav 選択＋名付けの 2 操作） |
| 本体が配布版を見つけない | ポート違い | 配布版は **18088**。本体側は接続先の改修が要る（`docs/contract.md` ⑼ D-9） |

---

## 5. アンインストール（**案**）

1. 「アプリと機能」からアンインストーラを実行＝`%LOCALAPPDATA%\Programs\irodori-tts-ywk\` が消える。
2. **取得物（runtime・models）を消すかどうかは尋ねる**（≈6.9 GB）。
3. **`voices\`（利用者の話者）は既定で残す**。消すには明示の選択が要る。
4. vc_redist は**消さない**（他のアプリが使っている可能性がある）。

---

## 6. 便 E への申し送り

- 画面・文言・同意の取り方（ライセンス通知の出し方）は**未確定**。`licenses/first-run-notices.md` を
  そのまま出すのか要約するのかも含めて便 E で決める。
- **ドライバ検査の実装**（cu130 ≥ 580／cu126 ≥ 560.76）＝告知の文言と、検査に失敗したときに
  合成を撃たせない止め方。
- **取得の再開・中断**（5.4 GiB の途中で切れたとき）。
- **cu126 を「古いドライバ向け」と謳えるか**は U-14 の実射待ち（便 B）＝**それまで謳わない**。
- Inno Setup 6 はこの機体にある（`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`＝`decisions.md` 25）。
