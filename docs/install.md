# install.md — 導入手順（**確定**・便 E 設計 2026-09-05）

> **この檔は作る側の帳面である**（保守と検分のための記録＝憲章 原則 8）。**利用者に指し示さない。**
> 利用者向けの入れ方・使い方・困ったときの手引きは **`docs/guide.md`** にある（本文は下記のまま据え置き）。
> ※ **§5 は裁定 132／133 に合わせて書き直した**（段 E・2026-09-11）＝データ樹は版ごと・錠も版ごと・
> 撤去の問いは 0・道連れの規則は廃止。**§2 の置き場の表はまだ旧い共有樹の綴りのままである**
> （`%LOCALAPPDATA%\irodori-tts-ywk\`）＝**段 F で直す**。
> 現行の正本は `docs/design/v2-spec.md` §11-8 と `docs/design/v2-plan.md` 段 E-3／F-2。

> **この檔は確定文である。**便 A の時点では「案」だったが、便 D（ランチャ）と便 E（インストーラ）の設計で
> 画面・文言・操作数・アンインストールの挙動が決まった。設計の全文＝`docs/design/ben-e-installer.md`。
> 正典＝`decisions.md` 8・12・46・80・83・87・88。受け入れ条件＝`docs/acceptance.md` の「導入」「サイズ」
> 「ドライバ」「起動」「起動失敗」の各行。事実の出典＝`research/`（調査便の写し）と、便 B・C・D・E の実射。
>
> **v1.1.0 までの配布物にはこの檔が入っている**（CUDA 版なら
> `%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda\docs\install.md`・ROCm 版なら
> `…\Programs\irodori-tts-ywk-radeon\docs\install.md`＝`decisions.md` 109）。
> **v2.0 で配布物から外し、代わりに `docs/guide.md` を入れる**（`docs/design/charter.md` 根 7・`docs/design/v2-plan.md` 段 F-2）。

---

## 0. 方式（確定）

- **実行系（torch と依存 wheel）もモデルも初回取得**する。**第三者バイナリ（wheel・exe・dll・
  モデル）を配布物に入れない**（`decisions.md` 8）。Release 資産＝ランチャ＋取得台帳＋自作分で **約 81 MiB**。
  ※ 唯一の例外は **.NET ランタイム**（MIT・ランチャ exe に焼き込み＝`decisions.md` 51）。
- **オフライン導入は用意しない**（同）。
- **per-user 導入**＝**導入で管理者権限（UAC）を求めない**。実測＝インストーラのログにそのまま
  `User privileges: None` ／ `Administrative install mode: No` ／ `Install mode root key: HKEY_CURRENT_USER`。
  ※ **アンインストールでは UAC が出ることがある**（Inno の仕様＝管理者権限が使える口座では
  アンインストーラが常に「管理者を要する」と印される）。導入で出ないことと混同しない。
- **リリースは版ごとに 2 本**（`decisions.md` 5・**表示名と導入先は `decisions.md` 109**）。
  - `irodori-tts-ywk-setup-v<版>-cuda.exe`＝**CUDA 版**（cu130／cu126／CPU を選べる）
    ＝アプリ名は「**irodori-TTS for 読み分けちゃん（CUDA 版）**」
  - `irodori-tts-ywk-setup-v<版>-radeon.exe`＝**ROCm 版**（rocm-gfx1151／CPU）
    ＝アプリ名は「**irodori-TTS for 読み分けちゃん（ROCm 版）**」
    ※ 檔名の `radeon` は**内部の識別子**なので変えていない（`decisions.md` 5 以来の名）。
  - 2 本は**導入先も別**（`…\Programs\irodori-tts-ywk-cuda` と `…\Programs\irodori-tts-ywk-radeon`）で、
    **同じ機体に同居できる**。Python 無しの機体で両方を試したい人のために、名も置き場も分けてある。
  - **取得物と話者の置き場も版ごとである**（`%LOCALAPPDATA%\irodori-tts-ywk-cuda\`／`…-radeon\`＝
    `decisions.md` 133 ⑶・2026-09-11 に改めた。**それより前は 1 本を共有していた**）。
    実行系は変種ごとの枝（`runtime\cu130\`・`runtime\rocm-gfx1151\` …）に入るので、その中でも混ざらない。
    ※ 旧い共有樹（`%LOCALAPPDATA%\irodori-tts-ywk\`）が在る機体は、v2.0 の初回起動で**版の樹へ移る**。
  - **単一起動の錠も版ごと**（`Local\irodori-tts-ywk-launcher-cuda`／`…-radeon`＝同 133 ⑷）＝
    2 つの版は互いを起こし直さない。同じ版の 2 個目を起こしたときだけ、**先に走っている方の窓が
    前に出る**（2 個目は静かに退く）。
  - **つなぎ口（`127.0.0.1:18088`）だけは 1 つ**なので、2 つの版を同時に開くと**後から起きた側が
    塞がりで止まる**（その理由を 1 行で出す）。
  - 導入・撤去で弾かれるのは**同じ版のランチャが走っているとき**だけである（インストーラの
    `AppMutex` も版ごとに割った）。**導入だけでなくアンインストールでも同じ錠を見る**
    （Inno の説明の原文＝"a mutex which Setup **and Uninstall** should check"）。
    **走っているランチャを終了すれば通る。**
  - **両方を入れた機体で読み分けちゃん2 が自動で選ぶのは ROCm 版**である。本体の候補順が
    `…\Programs\irodori-tts-ywk-radeon\` → `…\Programs\irodori-tts-ywk-cuda\` で、
    **実在する最初の 1 つ**を採るからである（本体側 `UI/Services/Launch/EngineLaunchDefaults.cs`）。
    CUDA 版を起こさせたいときは、本体の元栓のパス欄に
    `…\Programs\irodori-tts-ywk-cuda\IrodoriTtsYwk.Launcher.exe` を**手で指す**
    （保存値が正なので推定は上書きしない）。
- **署名は無い**。Release ノートに載せた **sha256** と突き合わせてから実行すること。
  SmartScreen が出たら「詳細情報」→「実行」（※ 実際に出るかは未実射）。
- 取得したものは**すべて sha256 で検証**する。台帳（`ledger/*.json`）が URL と sha256 の唯一の正本で、
  ランチャ（便 D）とビルド台本（`build/assemble-runtime.ps1`）は**同じ檔を読む**。
- **Python は利用者の機体に入れない**＝python.org の埋め込み Python（embeddable package）を
  アプリのフォルダに展開して使う。これは python.org が公式に案内する用法である
  （"The embedded distribution is a ZIP file containing a minimal Python environment. It is intended
  for acting as part of another application" ／ "third-party packages should be treated as part of the
  application (\"vendoring\")"＝`research/lab/notes/22-runtime-license.md` 表 4c）。

---

## 1. 利用者から見た流れ（**確定**・6 操作）

| # | 操作 | 何が起きるか | 目安 |
|---|---|---|---|
| 1 | インストーラを実行する（中は 3 クリック＝次へ／インストール／完了） | `%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda\`（ROCm 版は `…-radeon\`）に本体（ランチャ exe・`server/`・`ledger/`・`licenses/`・`voices/`・`docs/`）が入る。**約 106 MiB**。 | 数秒 |
| 2 | ランチャを起動する | **完了頁の「実行する」チェック（既定 ON）が起こすので、追加の操作は要らない。** | — |
| 3 | 通知に同意する | 初回セットアップの 1 段目に**ライセンス通知**（初回取得で入る第三者物の一覧＝`licenses/first-run-notices.md`。**手順 1 で一緒に入る**＝`decisions.md` 46）が出る。 | — |
| 4 | 実行系の種類を選んで「取得を始める」を押す | 検出した GPU を UUID つきで一覧表示。CUDA 版はドライバを見て**勧める種類が先に選ばれる**（580 以上＝cu130・528.33 以上＝cu126・それ未満＝CPU）。選び直す口は開いているが、**そのドライバでは動かない種類を選んでいる間は「この構成で取得を始める」が押せない**（理由が 1 行出る＝`decisions.md` 126。CPU はいつでも選べる）。**前の版で選んだ種類がこの機体のドライバに合わない場合は、次の起動でランチャがサーバを起こさずにこの画面を自動で開く**（理由 1 行つき・勧める種類が選ばれた状態＝`decisions.md` 126 ⑽。§4 の表も見る）。実行系とモデル＝**cu130 で 5.26 GiB**を取得して `%LOCALAPPDATA%\irodori-tts-ywk\` に展開する。`vc_redist.x64.exe`（≈24.4 MiB）もここで通す（**UAC が 1 回出る**）。 | 100 Mbps 級で **≤ 10 分** |
| 5 | 「起動」を押す | `127.0.0.1:18088` で待ち受ける（**アプリの窓を閉じるまで**＝閉じるとサーバも一緒に落ち、GPU のメモリが返る＝`decisions.md` 124）。**ポートは数秒で開き（`/health` 200）、モデルはその裏で載る**（裁定 105）＝状態帯は「起動中」→**「読込中」**→「待機」と進む。合成が撃てるのは「待機」（`runtime.loaded=true`）から。 | `/health` 200 まで **≤ 10 s**（実測 8.9 s＝`decisions.md` 106）・「待機」まで **≤ 120 s**（3090・SSD なら ≤ 60 s） |
| 6 | 読み分けちゃん2 側で配布版エンジンを有効にする | 本体が `/health` で見つける。 | — |

**手数の根拠**＝上流の導入手引き 21 操作のうち **16 が同梱と初回取得で消え、残り 6**
（`research/lab/notes/36-handoff-install-ui.md`）。
**手順 2 が 0 操作になるので、手順 4 の vc_redist の UAC 1 回を数に入れても合計は 6 のまま**である。
消える主因は「配布側が torch の版を固定すること」で、Windows で `--extra rocm` が無言で CPU 版を
入れてしまう事故（上流 `pyproject.toml:76`／Server `uv.lock:1871,1874` の `sys_platform == 'linux'`
マーカー）が構造的に消える。

**取得のバイト数と、取得中に要る空きは別物である。**

| 変種 | 落とすバイト | 取得中に要る空き |
|---|---|---|
| cu130（CUDA 版の既定） | **5.26 GiB** | **11.56 GiB** |
| cu126 | 5.93 GiB | 14.45 GiB |
| rocm-gfx1151（ROCm 版） | 4.79 GiB | 9.55 GiB |
| cpu | 3.62 GiB | 4.53 GiB |

（`ledger/README.md` §7。「要る空き」は展開が済むまで原檔と展開後が同時に在るため。
インストーラは導入先を選ぶ頁で `%LOCALAPPDATA%` のドライブの空きを見て、11.56 GiB を割っていたら**告知する**
＝**止めはしない**。CPU 変種なら 4.53 GiB で足りる。）

---

## 2. 何がどこに入るか

**2 つの樹があり、専管が分かれている。**

| 場所 | 中身 | 誰の物 | 消してよいか |
|---|---|---|---|
| `%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda\`（**アプリ樹**・ROCm 版は `…-radeon\`） | ランチャ exe（69,588,729 B）・`server\`（wrapper＋**パッチ適用済みの上流の写し**＝MIT・数 MB のテキスト）・`ledger\`・`licenses\`・`voices\presets\`（プリセット話者 12 檔・29,526,288 B＝**裁定 118** 時点の実測）・`docs\`・`unins000.exe` | **インストーラの専管**（利用者の檔は 1 つも置かれない） | アンインストーラが消す |
| `%LOCALAPPDATA%\irodori-tts-ywk\runtime\<variant>\` | 埋め込み Python ＋ site-packages（cu130／cu126／cpu／rocm-gfx1151 のいずれか） | ランチャの専管 | 消せる（次回起動で取り直し） |
| `%LOCALAPPDATA%\irodori-tts-ywk\models\` | モデル（`HF_HOME`）＝checkpoint 2.86 GiB・コーデック 410 MB・透かし 65 MiB・tokenizer 6 MiB | 同 | 消せる（同上・**3.33 GiB の取り直し**） |
| `%LOCALAPPDATA%\irodori-tts-ywk\cache\` | 取得中の `.part` と検証済みの原檔（cu130 で ≈1.93 GiB） | 同 | 消せる（**消すと修復が遅くなる**＝§5-2） |
| `%LOCALAPPDATA%\irodori-tts-ywk\voices\` | 利用者の話者（参照 wav・`refs\`）・`latents\`・`voices.json`・`voices.ywk.json` | 同 | **利用者の資産＝既定では消さない** |
| `%LOCALAPPDATA%\irodori-tts-ywk\logs\`・`miopen\` | ログ・MIOpen db（ROCm 版のみ） | 同 | 消せる |
| `%LOCALAPPDATA%\irodori-tts-ywk\settings.json` | 設定（GPU の UUID・変種＝`cu130`／`cu126`／`cpu`／`rocm-gfx1151`・精度など） | 同 | 消せる（初期設定に戻る） |

※ 表のアプリ樹の路は**新しく入れた機体の既定**である。**`decisions.md` 109 より前に入れた CUDA 版**は
`…\Programs\irodori-tts-ywk\`（版なし）のままで、上書き更新でも動かない（§5-1）。

**インストーラはデータ樹（`%LOCALAPPDATA%\irodori-tts-ywk\`）に 1 檔も作らず、更新でも 1 檔も触らない。**
だから**版を上げてもモデルと実行系は取り直しにならない**（実装で担保するのではなく、置き場が分かれていることで満たされる）。
原文＝`launcher/IrodoriTtsYwk.Launcher/Contracts/AppPaths.cs:216`
「書き込み側のディレクトリを作る（**導入先には 1 檔も作らない**）」。

**導入後のディスク使用量**＝アプリ樹 **約 106 MiB**（実測）＋データ樹。
データ樹の実測（この開発機・ROCm 版の材料）＝実行系 `runtime-rocm-gfx1151` が **4,636,458,599 B（4.32 GiB）**、
モデルが 3,570,982,039 B（3.33 GiB）＝**合計 約 8.3 GB**（`cache\` を除く）。
※ `docs/acceptance.md` サイズ行の「導入後 ≤ 7.0 GB」は**落とすバイト**を前提にした値で、
**展開後の実測とは合わない**。cu130 の展開後はまだ実測していない。条件値の扱いは卓の裁定待ち
（`docs/design/ben-e-installer.md` §9 Q-E2）。

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
  `IRODORI_HF_CHECKPOINT=Aratako/Irodori-TTS-v4.1-Small`・**`IRODORI_PRELOAD=false`**（裁定 105
  ＝bind してから裏の糸でモデルを載せる。`true` にすると読込が終わるまでポートが開かない）・
  `IRODORI_EMPTY_CACHE_INTERVAL=0`・`IRODORI_ALLOW_NO_REF_VOICE=false`・
  `IRODORI_DEFAULT_VOICE=デフォルト`（`decisions.md` 45＝`voice` を省いた要求が参照なし合成になる）・
  `IRODORI_VOICES_DIR=<絶対パス>`・`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`・
  `PYTHONUTF8=1`・`PYTHONDONTWRITEBYTECODE=1`・`PYTHONUNBUFFERED=1`・`HF_HOME=<models>`・
  `HF_HUB_OFFLINE=1`（初回取得後）。
- 状態判定は stderr の **3 行**＝`ywk_server <版> upstream=…`／`Uvicorn running on`／
  `runtime loaded in`。**422 の本文 echo はログに流さない**（1 発 5 KB＋絶対パス）。
- **インストーラは `YWK_LAUNCHER_APP_DIR`／`YWK_LAUNCHER_RUNTIME_DIR`／`YWK_LAUNCHER_DATA_DIR` を 1 本も立てない。**
  1 本でも立っているとランチャが「開発起動」を名乗る（`AppPaths.cs:205`）。

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

### 3-2 インストーラが**やらない**こと（3 つ）

| 何 | 誰がやるか | なぜ |
|---|---|---|
| vc_redist を通す | **ランチャ**（取得の段の先頭・引数は `ledger/vc_redist.json` の `silent_args`＝`/install /quiet /norestart`＝`decisions.md` 87 ⑷） | 通すには管理者昇格が要る＝導入を非昇格に保てなくなる |
| ドライバの版を検査する | **ランチャ**（cu130 ≥ 580.00／cu126 ≥ 528.33＝`decisions.md` 88 ⑵） | 判定を 2 箇所に持つと閾値が腐り、CPU 変種でなら使える機体を導入時に弾く |
| GPU が見えるかを検査する | **ランチャ**（`torch.cuda.is_available()` と `device_count`＝`decisions.md` 80・88 ⑴） | **実行系を取った後にしか撃てない**（導入時点で python は 1 檔も無い） |

---

## 4. うまくいかないとき

| 症状 | 原因 | どうする |
|---|---|---|
| 起動直後に落ちて「`msvcp140.dll` が見つかりません」 | vc_redist 未導入 | ランチャの「VC++ 再頒布を導入」を押す。※ **欠落機での実挙動は未確認**（`30` V-5）＝便 E の検分項目 E-1 |
| 状態帯が「**読込中**」から進まない／「**モデルの読込に失敗＝…**」と出る | ポート先行（裁定 105）なのでプロセスは死なず、モデルの読込だけが失敗している | 帯に出ている**理由 1 行**がそのまま原因である（checkpoint 不在・GPU が掴めない等）。**起動の途中で失敗したときはランチャが子を落とす**ので、帯の理由だけが残る＝そのままモデルを取り直すか変種を選び直して「サーバ起動」を押し直す。**待機に上がった後で失敗が出たとき**はサーバが生きたままなので、**先に「サーバ停止」で止めてから**やり直す（どちらでも「サーバ停止」は押せる）。※ `/health` は 200 のままなので、**「200 が返る＝使える」ではない**＝合否は `/ywk/status` の `runtime.loaded`／`runtime.error` で見る。**モデルが見つからない類の理由のときは、帯に「取得が未了です。」と「取得へ進む」が出る**（押すと初回取得のウィザードが開く＝`decisions.md` 121） |
| GPU を選んだのに合成が異常に遅い | GPU が掴めず CPU に落ちている | 配布版はこれを**塞ぐ**設計（`cuda` 指定で `torch.cuda.is_available()` が偽なら起動前に exit 2）。それでも遅いときは `/ywk/status` の `device.actual` を見る |
| 「CUDA device requested but torch.cuda.is_available() is False.」 | ドライバ不足か CUDA 版の不一致 | ドライバを更新するか、**cu126 版を選び直す**（cu130 は ≥ 580／cu126 は ≥ 528.33。**cu126 は 537.58 で実測済み**＝`decisions.md` 80） |
| 起動して `/health` が 200 になるのに、最初の合成でランチャが「サーバが落ちました（exit −1073741819）」と出す | cu130 の実行系が GPU を見られない機体で走った | **cu126 か CPU の変種に切り替える**（`decisions.md` 83。cu130 の CPU 転落は配布版が禁じている） |
| 起動すると「このドライバ（537.58）では CUDA 13.0 は動きません（下限 580.00）。CUDA 12.6 に切り替えて取得します。」と出て、**初回取得のウィザードが勝手に開く** | 前の版（v1.0.2 以前）で選んだ実行系の種類が、この機体のドライバの下限に届いていない。設定に残った値は選び直すまで直らない | **そのまま進めてよい**（v1.1.0 以降＝`decisions.md` 126 ⑽）。勧める種類（この例では CUDA 12.6）が最初から選ばれた状態でウィザードが開くので、通知に同意して「この構成で取得を始める」を押すと、その実行系だけを取り寄せて（**モデルは既に在れば飛ばす**）「起動の確認」まで進む。**別の種類を選び直してもよい**（自動では切り替えない）。閉じてしまっても、状態帯の「**取得へ進む**」でいつでも開き直せる。ドライバを新しくする道もある（cu130 は ≥ 580.00） |
| 合成は通るが 1 発目だけ極端に遅い | 初見形状のカーネル生成（**Radeon のみ**） | `docs/radeon.md` を見る。CUDA 機では該当する罰は観測されていない |
| 話者一覧に「デフォルト」しか出ない | `voices\` が空 | ランチャで参照 wav を追加する（wav 選択＋名付けの 2 操作） |
| 更新したのに、新しい版で**増えた同梱の話者**が一覧に出ない | **v1.0.2 より前**の版は、1 度でも話者台帳を作った利用者に後から足したプリセットを配らなかった（`decisions.md` 121） | **v1.0.2 以降は起動のたびに増えた分だけ自動で入る**（消した話者は戻らない。ただし**前の版から上げた最初の 1 回だけ**は、以前に消した同梱の話者が 1 度だけ戻ることがある＝前の版の台帳は「消した」と「まだ差し出していない」を書き分けていないため。2 回目以降の起動では戻らない）。それより前の版では話者画面の「**同梱のプリセットを入れ直す**」を押す |
| 本体が配布版のランチャを**自動で見つけない** | 導入先が本体の候補と合っていない（**`decisions.md` 109 より前に入れた CUDA 版**は `…\Programs\irodori-tts-ywk\` のままである＝Inno の `UsePreviousAppDir` で上書き更新しても場所は動かない） | 本体が見るのは `…-radeon\` と `…-cuda\` の 2 つだけ。**いったんアンインストールしてから新しい setup を入れ直す**（データ樹は共有なので話者・設定・モデルは残る＝§5-1）か、本体の元栓のパス欄に exe を**手で指す** |
| 本体が配布版につながらない | ポート違い | 配布版は **18088**（本体側は**便 F で対応済み**＝`decisions.md` 107。契約は `docs/contract.md` ⑼ D-9） |
| ランチャの選択肢に cu130／cu126 が出ない（CUDA 版なのに） | `ledger\` に `runtime-rocm-*.json` が混ざっている | **入れ直す**（インストーラは導入の最初の段で `ledger\` を作り直す）。混ざると ROCm 版と判定される（`ReleaseFlavor.Detect`） |
| 導入の途中で「管理者権限が要ります」と断られる | Program Files（全ユーザ）や Windows フォルダを選んだ | 既定の `%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda`（ROCm 版は `…-radeon`）を使う |
| どちらの版を入れたのか分からなくなった | 名も置き場も版で分かれている（`decisions.md` 109） | 「アプリと機能」の表示名（`…（CUDA 版）`／`…（ROCm 版）`）・ランチャの窓題・「このアプリについて」の見出しの 3 つが同じ名を出す |

---

## 5. 更新・修復・アンインストール

> **この §5 は裁定 132／133（2026-09-11）に揃えてある。**要点は 4 つ＝
> ⑴ **データ樹は版ごと**＝`%LOCALAPPDATA%\irodori-tts-ywk-cuda\`／`…-radeon\`（RTX（CUDA）版と
> Radeon（ROCm）版は**別アプリ**で、データ樹も設定も声も共有しない）。
> ⑵ **単一起動の錠も版ごと**＝`Local\irodori-tts-ywk-launcher-cuda`／`…-radeon`（もう一方の版が
> 走っていても、こちらの導入・撤去は弾かれない）。
> ⑶ **撤去は、その版の樹を丸ごと消す。問いは 1 つも出さない。**置き場の外には 1 檔も触れない。
> ⑷ **旧い共有樹**（`%LOCALAPPDATA%\irodori-tts-ywk\`）は v2.0 の初回起動で版の樹へ**移る**。
> 移送を通っていない機体のために、撤去でも面倒を見る（§5-4 の 4）。

### 5-1 更新

**同じ setup.exe の新しい版を実行するだけ。**導入先は尋ねられない（同じ版が入っていると場所の頁が出ない）＝**2 クリック**。

- アプリ樹は**丸ごと入れ替わる**（`server\`・`ledger\`・`licenses\`・`voices\presets\` は導入の最初の段で消してから置き直す
  ＝旧版で消えた檔が残らない）。
- **データ樹には触らない**＝**モデルと実行系の取り直しは起きない**。
  ※ ただし **同梱の声・動かすための一式・声のデータが新しくなった版**では、開いたときに
  **その差分だけ**を取り直す（`docs/design/v2-spec.md` §11＝更新の差分取り直し）。
  同梱の声は黙って写し直し（1 秒級・ネット不要）、一式とモデルは**押すまで落とさない**。
  **自分で差し替えた同梱の声は上書きしない**（そのまま残し、1 行だけ告げる）。
- **ランチャが走っていると弾かれる**（「…が実行中です」と出る）。ランチャを終了してからやり直す。
  ※ **弾かれるのは同じ版のランチャが走っているときだけ**（錠は版ごと＝裁定 133 ⑷）。
  もう一方の版が走っていても、こちらの導入は通る。
- ※ **`decisions.md` 109 より前に入れた CUDA 版**を上書き更新しても、導入先は旧名
  `…\Programs\irodori-tts-ywk\` のままである（Inno の `UsePreviousAppDir`＝既定 yes）。
  新しい既定 `…-cuda\` にしたい／**本体の自動発見に乗せたい**ときは、**いったんアンインストール
  してから入れ直す**（データ樹は別なのでモデルの取り直しは起きない＝§2）。
  入れ直さないなら、本体側でランチャの路を手で指す（§4）。

### 5-2 修復

| 壊れた場所 | 直し方 |
|---|---|
| アプリ樹（`server\` の檔が消えた・`ledger\` が壊れた） | **同じ setup.exe をもう一度実行する。**Inno に「修復」の項目は無く、これが修復経路である |
| データ樹（実行系が壊れた） | ランチャで**展開の段をやり直す**。`cache\` に検証済みの原檔が残っていれば**1 バイトも落とし直さない**。だから `cache\` は「取得の残骸」ではなく**修復の保険**である |
| データ樹（**モデルが消えた**・古いデータ樹を引き継いだ） | **ランチャが起動時に気づいて初回取得のウィザードを出す**（`decisions.md` 121。それより前の版は「サーバ起動」が失敗するだけだった）。閉じてしまっても、状態帯の「**取得へ進む**」か頭の「**初回取得をやり直す**」で同じ所へ戻れる。**実行系が既に組み上がっていれば取得と展開は飛ばし、モデルだけ取り直す**（モデルも既に取れている分は落とし直さない） |
| 設定がおかしい | `settings.json` を消す（初期設定に戻る）。話者は消えない |
| **何が起きたのか分からない** | `%LOCALAPPDATA%\irodori-tts-ywk-cuda\logs\launcher-<年月日>.log`（ROCm 版は `…-radeon\`）を見る（`decisions.md` 126 から・路は版ごと＝裁定 133 ⑶）。アプリの状態欄に出た行と、初回取得の画面に出た行が、そのまま日ごとに 1 檔で残る（UTF-8）。報告に添えるならこの檔である |

### 5-3 データの引っ越し（未対応・手作業）

ランチャに引っ越し機能は**無い**（設定画面の「データの置き場」は**読むだけ**）。
別ドライブへ移したいときは、**ランチャを終了してから**次の 1 手で junction を張る。

```
move "%LOCALAPPDATA%\irodori-tts-ywk-cuda" D:\ywk-data
mklink /J "%LOCALAPPDATA%\irodori-tts-ywk-cuda" D:\ywk-data
```

（ROCm 版なら `…-radeon` に読み替える。**版ごとに 1 本ずつ**張る＝2 つの版はデータ樹を共有しない。）

※ 非昇格（管理者でない普通の窓）で `/J` が張れることは**実射で確かめた**（2026-09-05・`C:` の中で測った。
`D:` 側は本席の停止域なので、別ドライブ相手の実射はまだ無い）。
※ **アンインストールでは、junction を通り抜けて実体（`D:\ywk-data`）の中身が消える。**
裁定 133 ⑴ で「その版の樹を丸ごと消す・問わない」になったので、**残す選択肢はもう無い**。
Inno の `DelTree` の「reparse point の中までは消さない」免除が当たるのは junction **自身**だけなので、
`.iss` は junction のときだけ中身を名指しで消す（`installer/irodori-tts-ywk.iss` の `WipeTree`）。
**実体を残したいときは、アンインストールの前に junction を外して樹を退避すること。**
※ **junction 自身（`%LOCALAPPDATA%\irodori-tts-ywk-cuda` という路）は残る。**
`WipeTree` は根が reparse point なら畳まない（同 `IsReparsePoint`）＝入れ直すときに `mklink /J` を
張り直す必要は無い。2026-09-05 より前の版はここで junction を消していた（実体は残るが**路が消える**ので、
入れ直すと空のデータ樹が `C:` に出来て旧樹が孤児になった）＝**是正済み**。
※ **樹の一部だけ（たとえば `models\` だけ）を逃がす形も同じ扱いになる。**
`WipeTree` はすぐ下の枝へ 1 段ずつ降りるので、入れ子の junction も「中身を消して空の環にする」に当たり、
実体が別ドライブに孤児として残ることはない（是正・2026-09-11）。
※ 中身の削除を junction 越しに実射した例はまだ無い（CHM の `DelTree` の原文と `.iss` の行からの断定）。
畳まないことだけは実射で確かめた（`RemoveDir` を junction に撃つと路が消えること・撃たなければ残ることの両方）。
※ **`settings.json` の `dataDir` に路を書く手もある**（`%LOCALAPPDATA%\irodori-tts-ywk-cuda\settings.json`。
ランチャを終了してから書き、樹の中身は自分で移すこと）。指した回は⑴ その路が置き場になり
⑵ 旧い版からの引っ越しは**走らない**（利用者が置き場を決めているので、勝手に動かさない）。
設定画面の「データの置き場」は**いまも読むだけ**＝書き替えは檔を直に編む手だけである。
**入れ直しでは上書きされない**（インストーラはデータ樹に 1 檔も触らない）。

### 5-4 アンインストール

**裁定 132／133 ⑴＝このアプリが自分の置き場に持っている物をすべて消す。問いは 1 つも出さない。**
司令官の逐語（裁定 132）＝「**アンインストールで数Gバイト残るのは普通にアプリの範囲を逸脱している。**」

1. 「アプリと機能」からアンインストーラを実行する。表示名は「**irodori-TTS for 読み分けちゃん（CUDA 版）
   v<版>**」／「**…（ROCm 版）v<版>**」で、消えるのは**その版のアプリ樹とその版のデータ樹**である。
   - アプリ樹＝`%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda\`／`…-radeon\`
   - データ樹＝`%LOCALAPPDATA%\irodori-tts-ywk-cuda\`／`…-radeon\`
   **弾かれるのは同じ版のランチャが走っているときだけ**（錠は版ごと＝裁定 133 ⑷。Inno は導入と撤去の
   両方で同じ `AppMutex` を見る）。**もう一方の版が走っていても撤去は通る。**
2. **問いは出ない。**データ樹の中身は全部消える＝ダウンロードした一式（`runtime\`）・モデル（`models\`）・
   取り込んだ声の写し（`voices\refs\`）・下ごしらえ（`voices\latents\`）・`settings.json`・`logs\`・
   取得キャッシュ（`cache\`）・MIOpen の db（`miopen\`）。
   ※ 裁定 132 の「取得した実行系とモデルも削除しますか」と、その次の「登録した話者と設定も削除しますか」は
   **どちらも廃止**した（133 ⑴）。`/SUPPRESSMSGBOXES` はもう効き所が無い（無人でも有人でも同じ道）。
3. **置き場の外には 1 檔も触れない**（裁定 133 ⑵）。
   声を追加するときに利用者が選んだ**元の wav** は利用者の檔で、アプリは `voices\refs\` に**写しを取り込んで**
   使う（`VoiceStore.AddVoice` が写し、以後は写しの檔名しか台帳に持たない）。**元の檔は削除でも更新でも触らない。**
   消えるのは写しのほうだけである。
4. **もう一方の版（CUDA 版／ROCm 版）の樹には触れない**（共有が無くなったので「道連れ」の場合分けごと廃止＝
   旧 `docs/design/ben-e-installer.md` §5-4 の規則は無効）。
   ただし**旧い共有樹 `%LOCALAPPDATA%\irodori-tts-ywk\`** は別＝**もう一方の版が入っていないときだけ**一緒に消す。
   v2.0 の初回起動で版の樹へ移るはずの樹だが、⑴ v1.1.0 に上書き導入して**一度も開かずに**撤去した機体、
   ⑵ 移送に失敗して旧樹が残った機体では移送が走っておらず、放っておくと**約 8 GB が丸ごと居残る**。
   もう一方の版が入っている回は**触らない**（向こうがまだ移送で要る）。判定は `.iss` の `OtherFlavorKey`。
5. vc_redist は**消さない**（他のアプリが使っている可能性がある）。
6. **アンインストールでは UAC が出ることがある**（§0）。導入で出ないことと混同しない。

---

## 6. 残っている未確認（正直な欠落）

| # | 何 | いつ埋まるか |
|---|---|---|
| 1 | **vc_redist 欠落機での実挙動**（`30` V-5）＝理由が読める形で止まるか | 便 E の検分 E-1（Windows Sandbox か、まっさらな RTX 機） |
| 2 | 署名なし exe で SmartScreen が実際に出るか・出たときの操作数 | 便 E（実射していない） |
| 3 | 別セッション（高速ユーザ切替・RDP）で走っているランチャを、導入時の検査が捕まえるか | 便 E の検分（`AppMutex` は `Local\` 名前空間） |
| 4 | **cu130 の実行系を展開したあとの実サイズ**（「導入後 ≤ 7.0 GB」の判定に要る） | RTX 機（月曜以降） |
| 5 | 台帳が変わった版を配ったとき、古い実行系のまま走ってしまう（変種の解決は `python.exe` の在否しか見ない） | 便 D への申し送り（`docs/design/ben-e-installer.md` §5-6） |
| 6 | `torch/lib` の未特定ライセンス 3 件（zlibwapi・libiomp5md／libiompstubs5md・liblzma／aotriton_v2） | `docs/acceptance.md` §3 の 1・8・11／本 repo の裁定（`decisions.md` 22・64） |
