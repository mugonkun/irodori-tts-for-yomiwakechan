# install.md — 導入手順（**確定**・便 E 設計 2026-09-05）

> **この檔は確定文である。**便 A の時点では「案」だったが、便 D（ランチャ）と便 E（インストーラ）の設計で
> 画面・文言・操作数・アンインストールの挙動が決まった。設計の全文＝`docs/design/ben-e-installer.md`。
> 正典＝`decisions.md` 8・12・46・80・83・87・88。受け入れ条件＝`docs/acceptance.md` の「導入」「サイズ」
> 「ドライバ」「起動」「起動失敗」の各行。事実の出典＝`research/`（調査便の写し）と、便 B・C・D・E の実射。
>
> **この檔は配布物に入る**（`%LOCALAPPDATA%\Programs\irodori-tts-ywk\docs\install.md`）。

---

## 0. 方式（確定）

- **実行系（torch と依存 wheel）もモデルも初回取得**する。**第三者バイナリ（wheel・exe・dll・
  モデル）を配布物に入れない**（`decisions.md` 8）。Release 資産＝ランチャ＋取得台帳＋自作分で **約 81 MiB**。
  ※ 唯一の例外は **.NET ランタイム**（MIT・ランチャ exe に焼き込み＝`decisions.md` 51）。
- **オフライン導入は用意しない**（同）。
- **per-user 導入**＝**導入で管理者権限（UAC）を求めない**。実測＝インストーラのログ逐語
  `User privileges: None` ／ `Administrative install mode: No` ／ `Install mode root key: HKEY_CURRENT_USER`。
  ※ **アンインストールでは UAC が出ることがある**（Inno の仕様＝管理者権限が使える口座では
  アンインストーラが常に「管理者を要する」と印される）。導入で出ないことと混同しない。
- **リリースは版ごとに 2 本**（`decisions.md` 5）。
  - `irodori-tts-ywk-setup-v<版>-cuda.exe`＝**CUDA 版**（cu130／cu126／CPU を選べる）
  - `irodori-tts-ywk-setup-v<版>-radeon.exe`＝**Radeon 版**（rocm-gfx1151／CPU）
  - 2 本は**導入先も別**（`…\irodori-tts-ywk` と `…\irodori-tts-ywk-radeon`）で、**同じ機体に同居できる**。
    ただし取得物の置き場（`%LOCALAPPDATA%\irodori-tts-ywk\`）は**共有**する。
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
| 1 | インストーラを実行する（中は 3 クリック＝次へ／インストール／完了） | `%LOCALAPPDATA%\Programs\irodori-tts-ywk\` に本体（ランチャ exe・`server/`・`ledger/`・`licenses/`・`voices/`・`docs/`）が入る。**約 106 MiB**。 | 数秒 |
| 2 | ランチャを起動する | **完了頁の「実行する」チェック（既定 ON）が起こすので、追加の操作は要らない。** | — |
| 3 | 通知に同意する | 初回セットアップの 1 段目に**ライセンス通知**（初回取得で入る第三者物の一覧＝`licenses/first-run-notices.md`。**手順 1 で一緒に入る**＝`decisions.md` 46）が出る。 | — |
| 4 | 実行系の種類を選んで「取得を始める」を押す | 検出した GPU を UUID つきで一覧表示。CUDA 版の既定は cu130、ドライバが 580 未満なら cu126 を**勧める**（自動では切り替えない＝`decisions.md` 4・80）。実行系とモデル＝**cu130 で 5.26 GiB**を取得して `%LOCALAPPDATA%\irodori-tts-ywk\` に展開する。`vc_redist.x64.exe`（≈24.4 MiB）もここで通す（**UAC が 1 回出る**）。 | 100 Mbps 級で **≤ 10 分** |
| 5 | 「起動」を押す | `127.0.0.1:18088` で常駐する。**ポートは数秒で開き（`/health` 200）、モデルはその裏で載る**（裁定 105）＝状態帯は「起動中」→**「読込中」**→「待機」と進む。合成が撃てるのは「待機」（`runtime.loaded=true`）から。 | `/health` 200 まで **≤ 10 s**（実測 8.9 s＝`decisions.md` 106）・「待機」まで **≤ 120 s**（3090・SSD なら ≤ 60 s） |
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
| rocm-gfx1151（Radeon 版） | 4.79 GiB | 9.55 GiB |
| cpu | 3.62 GiB | 4.53 GiB |

（`ledger/README.md` §7。「要る空き」は展開が済むまで原檔と展開後が同時に在るため。
インストーラは導入先を選ぶ頁で `%LOCALAPPDATA%` のドライブの空きを見て、11.56 GiB を割っていたら**告知する**
＝**止めはしない**。CPU 変種なら 4.53 GiB で足りる。）

---

## 2. 何がどこに入るか

**2 つの樹があり、専管が分かれている。**

| 場所 | 中身 | 誰の物 | 消してよいか |
|---|---|---|---|
| `%LOCALAPPDATA%\Programs\irodori-tts-ywk\`（**アプリ樹**） | ランチャ exe（69,588,729 B）・`server\`（wrapper＋**パッチ適用済みの上流の写し**＝MIT・数 MB のテキスト）・`ledger\`・`licenses\`・`voices\presets\`（プリセット話者 11 檔・32,571,364 B）・`docs\`・`unins000.exe` | **インストーラの専管**（利用者の檔は 1 つも置かれない） | アンインストーラが消す |
| `%LOCALAPPDATA%\irodori-tts-ywk\runtime\<variant>\` | 埋め込み Python ＋ site-packages（cu130／cu126／cpu／rocm-gfx1151 のいずれか） | ランチャの専管 | 消せる（次回起動で取り直し） |
| `%LOCALAPPDATA%\irodori-tts-ywk\models\` | モデル（`HF_HOME`）＝checkpoint 2.86 GiB・コーデック 410 MB・透かし 65 MiB・tokenizer 6 MiB | 同 | 消せる（同上・**3.33 GiB の取り直し**） |
| `%LOCALAPPDATA%\irodori-tts-ywk\cache\` | 取得中の `.part` と検証済みの原檔（cu130 で ≈1.93 GiB） | 同 | 消せる（**消すと修復が遅くなる**＝§5-2） |
| `%LOCALAPPDATA%\irodori-tts-ywk\voices\` | 利用者の話者（参照 wav・`refs\`）・`latents\`・`voices.json`・`voices.ywk.json` | 同 | **利用者の資産＝既定では消さない** |
| `%LOCALAPPDATA%\irodori-tts-ywk\logs\`・`miopen\` | ログ・MIOpen db（Radeon 版のみ） | 同 | 消せる |
| `%LOCALAPPDATA%\irodori-tts-ywk\settings.json` | 設定（GPU の UUID・CUDA 版・精度など） | 同 | 消せる（初期設定に戻る） |

**インストーラはデータ樹（`%LOCALAPPDATA%\irodori-tts-ywk\`）に 1 檔も作らず、更新でも 1 檔も触らない。**
だから**版を上げてもモデルと実行系は取り直しにならない**（実装で担保するのではなく、置き場が分かれていることで満たされる）。
逐語＝`launcher/IrodoriTtsYwk.Launcher/Contracts/AppPaths.cs:216`
「書き込み側のディレクトリを作る（**導入先には 1 檔も作らない**）」。

**導入後のディスク使用量**＝アプリ樹 **約 106 MiB**（実測）＋データ樹。
データ樹の実測（この開発機・Radeon 版の材料）＝実行系 `runtime-rocm-gfx1151` が **4,636,458,599 B（4.32 GiB）**、
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
| 状態帯が「**読込中**」から進まない／「**モデルの読込に失敗＝…**」と出る | ポート先行（裁定 105）なのでプロセスは死なず、モデルの読込だけが失敗している | 帯に出ている**理由 1 行**がそのまま原因である（checkpoint 不在・GPU が掴めない等）。**起動の途中で失敗したときはランチャが子を落とす**ので、帯の理由だけが残る＝そのままモデルを取り直すか変種を選び直して「サーバ起動」を押し直す。**待機に上がった後で失敗が出たとき**はサーバが生きたままなので、**先に「サーバ停止」で止めてから**やり直す（どちらでも「サーバ停止」は押せる）。※ `/health` は 200 のままなので、**「200 が返る＝使える」ではない**＝合否は `/ywk/status` の `runtime.loaded`／`runtime.error` で見る |
| GPU を選んだのに合成が異常に遅い | GPU が掴めず CPU に落ちている | 配布版はこれを**塞ぐ**設計（`cuda` 指定で `torch.cuda.is_available()` が偽なら起動前に exit 2）。それでも遅いときは `/ywk/status` の `device.actual` を見る |
| 「CUDA device requested but torch.cuda.is_available() is False.」 | ドライバ不足か CUDA 版の不一致 | ドライバを更新するか、**cu126 版を選び直す**（cu130 は ≥ 580／cu126 は ≥ 528.33。**cu126 は 537.58 で実測済み**＝`decisions.md` 80） |
| 起動して `/health` が 200 になるのに、最初の合成でランチャが「サーバが落ちました（exit −1073741819）」と出す | cu130 の実行系が GPU を見られない機体で走った | **cu126 か CPU の変種に切り替える**（`decisions.md` 83。cu130 の CPU 転落は配布版が禁じている） |
| 合成は通るが 1 発目だけ極端に遅い | 初見形状のカーネル生成（**Radeon のみ**） | `docs/radeon.md` を見る。CUDA 機では該当する罰は観測されていない |
| 話者一覧に「デフォルト」しか出ない | `voices\` が空 | ランチャで参照 wav を追加する（wav 選択＋名付けの 2 操作） |
| 本体が配布版を見つけない | ポート違い | 配布版は **18088**。本体側は接続先の改修が要る（`docs/contract.md` ⑼ D-9） |
| ランチャの選択肢に cu130／cu126 が出ない（CUDA 版なのに） | `ledger\` に `runtime-rocm-*.json` が混ざっている | **入れ直す**（インストーラは導入の最初の段で `ledger\` を作り直す）。混ざると Radeon 版と判定される（`ReleaseFlavor.Detect`） |
| 導入の途中で「管理者権限が要ります」と断られる | Program Files（全ユーザ）や Windows フォルダを選んだ | 既定の `%LOCALAPPDATA%\Programs\irodori-tts-ywk` を使う |

---

## 5. 更新・修復・アンインストール

### 5-1 更新

**同じ setup.exe の新しい版を実行するだけ。**導入先は尋ねられない（同じ版が入っていると場所の頁が出ない）＝**2 クリック**。

- アプリ樹は**丸ごと入れ替わる**（`server\`・`ledger\`・`licenses\`・`voices\presets\` は導入の最初の段で消してから置き直す
  ＝旧版で消えた檔が残らない）。
- **データ樹には触らない**＝**モデルと実行系の取り直しは起きない**。
- **ランチャが走っていると弾かれる**（「…が実行中です」と出る）。ランチャを終了してからやり直す。

### 5-2 修復

| 壊れた場所 | 直し方 |
|---|---|
| アプリ樹（`server\` の檔が消えた・`ledger\` が壊れた） | **同じ setup.exe をもう一度実行する。**Inno に「修復」の項目は無く、これが修復経路である |
| データ樹（実行系が壊れた） | ランチャで**展開の段をやり直す**。`cache\` に検証済みの原檔が残っていれば**1 バイトも落とし直さない**。だから `cache\` は「取得の残骸」ではなく**修復の保険**である |
| 設定がおかしい | `settings.json` を消す（初期設定に戻る）。話者は消えない |

### 5-3 データの引っ越し（未対応・手作業）

ランチャに引っ越し機能は**無い**（設定画面の「データの置き場」は**読むだけ**）。
別ドライブへ移したいときは、**ランチャを終了してから**次の 1 手で junction を張る。

```
move "%LOCALAPPDATA%\irodori-tts-ywk" D:\ywk-data
mklink /J "%LOCALAPPDATA%\irodori-tts-ywk" D:\ywk-data
```

※ 非昇格（管理者でない普通の窓）で `/J` が張れることは**実射で確かめた**（2026-09-05・`C:` の中で測った。
`D:` 側は本席の停止域なので、別ドライブ相手の実射はまだ無い）。
※ この形でアンインストールから「取得物も削除する」を選ぶと、**junction を通り抜けて実体（`D:\ywk-data`）の中身が消える**。
アンインストーラが名指しで消すのは `%LOCALAPPDATA%\irodori-tts-ywk` の**下**（`runtime\`・`models\`・`cache\`・`logs\`・`miopen\`）で、
Inno の `DelTree` の「reparse point の中までは消さない」免除が当たるのは junction **自身**だけだからである
（`installer/irodori-tts-ywk.iss` の `CurUninstallStepChanged`）。話者と設定を消す 2 問目（`voices\`・`settings.json`）も同じ。
**実体を残したいときは既定の「残す」を選ぶこと。**手で消す必要は無い。
※ **junction 自身（`%LOCALAPPDATA%\irodori-tts-ywk` という路）は、どちらを選んでも残る。**
アンインストーラは最後にデータ樹の根を畳もうとするが、**そこが junction なら畳まない**
（`installer/irodori-tts-ywk.iss` の `IsReparsePoint`）。だから入れ直すときに `mklink /J` を張り直す必要は無い。
2026-09-05 より前の版はここで junction を消していた（実体は残るが**路が消える**ので、入れ直すと空のデータ樹が
`C:` に出来て旧樹が孤児になった）＝**是正済み**。
※ 中身の削除（1 問目・2 問目）を junction 越しに実射した例はまだ無い（CHM の `DelTree` の逐語と `.iss` の行からの断定）。
畳まないことだけは実射で確かめた（`RemoveDir` を junction に撃つと路が消えること・撃たなければ残ることの両方）。

### 5-4 アンインストール

1. 「アプリと機能」からアンインストーラを実行する＝`%LOCALAPPDATA%\Programs\irodori-tts-ywk\` が消える。
   **ランチャが走っていると弾かれる**（終了してからやり直す）。
2. **取得物（実行系・モデル・取得キャッシュ・ログ・MIOpen db）を消すかどうかを尋ねる。既定は「残す」。**
   ※ **もう一方の種（CUDA 版と Radeon 版）がまだ入っているときは尋ねない**＝取得物は共有なので道連れにしない。
3. **話者（`voices\`）と設定（`settings.json`）を消すかどうかを別に尋ねる。既定は「残す」。**
   利用者の資産なので、消すには**明示の選択が要る**。
4. **無人アンインストール**（`/VERYSILENT /SUPPRESSMSGBOXES`）では、2 つとも**黙って「残す」に落ちる**（実射で確認）。
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
| 6 | `torch/lib` の未特定ライセンス 3 件（zlibwapi・libiomp5md／libiompstubs5md・liblzma／aotriton_v2） | `docs/acceptance.md` §3 の 1・8・11／司令官の裁定（`decisions.md` 22・64） |
