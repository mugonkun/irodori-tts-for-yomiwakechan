# 便 D 設計書 — ランチャ（WPF／.NET）MVP（2026-09-05・設計席 Fable・草稿）

> 正典は `decisions.md`（6・12〜16・34・43・45〜47）。事実の出典は便 A の成果（`docs/contract.md`・`ledger/`・`server/ywk_server.py` の起動の型）・便 C の実測（`docs/radeon.md`）・本体の生産ライン（`research/` の ywk2-tooling 読解＝`yomiwakechan2/yomiwakechan2/UI/Services/Launch/EngineLaunchSeats.cs`・`IrodoriServerProcessRegistry.cs`・`probe/ai-multi-p7/common.ps1`＝読むだけ・檔は写さない）。
> 機体＝RTX 3090 機（移行後・2026-09-07 以降）。便 B の実測が入ってから着工。本檔は草稿＝便 B・C の結果で §3 の数値と §5 の暖機既定を確定する。

## 0. 目的と範囲

**目的**（decisions 15）＝利用者が自環境で GPU・CUDA 版・パラメータ・参照ボイスを試す UI。本体（yomiwakechan2）からの読み上げ司令は、ここで選んだ GPU・CUDA 版を暗黙に使う。配布版が常駐して Server（wrapper）を起こし、本体は `/health` で見つけるだけ（G-2）。

**MVP の範囲**（依頼文 §4 D）＝常駐（タスクトレイ）・設定→env→wrapper の起動／停止・GPU 列挙（UUID）・ドライバ検査・話者台帳→`voices.json`・暖機・ログ 2〜3 行で状態判定・**初回取得**（台帳から埋め込み Python＋wheel＋モデル＋vc_redist を取る＝便 E のインストーラは「ランチャを置くだけ」にする）・試し撃ち（本文＋話者＋主要パラメータ→再生／保存）。
**範囲外**＝先読み UI（契約は予約のまま）・複数 GPU の同時起動（1 プロセス 1 デバイス）・自動更新。

**受け入れ条件**（引き継ぎ §4 の「起動失敗」「GPU」「話者」「ログ」行・`docs/acceptance.md`）
| # | 条件 |
|---|---|
| D-1 | 起動失敗＝範囲外 GPU・精度不整合・checkpoint 不在は ≤ 15 s で理由 1 行を UI に出す（wrapper の exit 2 と stderr 1 行目を拾う） |
| D-2 | GPU は UUID で保存し起動時に index へ解決（列挙 ≤ 5 s）。device は env 2 本（MODEL／CODEC）に同時に載る。範囲外・綴り違いは UI で 0 s で弾く |
| D-3 | 話者＝wav 選択＋名付けの 2 操作で追加・再起動なしで一覧に反映・日本語名で合成 200。「デフォルト」が常在（先頭）・`none` 0 件 |
| D-4 | ログ＝stderr を取り込み `ywk_server <版>`・`Uvicorn running on`・`runtime loaded in`（＋`ywk_server: device actual=`）で状態判定。422 の本文 echo をログに流さない（wrapper 側で既に潰している） |
| D-5 | 初回取得＝台帳（`ledger/*.json`）の順＝vc_redist → python-embed → runtime-<変種> → models。sha256 検証・原子的着地・再開・進捗（bytes／ETA）・失敗時の理由と再試行。利用者操作 ≤ 6・オンライン 100 Mbps 級で ≤ 10 分（CUDA 版 ≈5.5 GB） |
| D-6 | UI 起動→`/health` 200＋`runtime.loaded=true` まで ≤ 120 s（3090・SSD なら ≤ 60 s）。ready 待ち ≥ 120 s |
| D-7 | UIA 無人検分（`probe/common.ps1` の型＝本体の `ai-multi-p7/common.ps1` を倣う・檔はコピーしない）で主要 6 操作が通る |
| D-8 | 敵対検分 3 席（起動・取得・UI）の是正済み |

## 1. 構成

```
launcher/
  IrodoriTtsYwk.Launcher/            WPF・net10.0-windows・WinExe・SelfContained publish（.NET ランタイム非同梱の裁定 8 と衝突しない形＝SelfContained は自作分＋MS ランタイムを 1 exe に焼く。第三者物の「取得台帳で取る」原則の例外として decisions に記帳が要る→§7）
    App.xaml(.cs)                     単一起動（Mutex）・トレイ常駐・終了時に wrapper をツリー kill
    Services/
      Ledger/  LedgerReader.cs・FetchPlanner.cs・Downloader.cs（HTTP・Range 再開・sha256・.part→Rename）・WheelInstaller.cs（zip 展開・.data/purelib 合流・sdist は System.Formats.Tar）・PthWriter.cs（python312._pth.template → 絶対パス）
      Models/  ModelFetcher.cs（runtime の python.exe で server/ywk_fetch_models.py を子プロセス実行・JSON 行の進捗を読む・refs/main は台本が書く）
      Gpu/     GpuEnumerator.cs（① nvidia-smi -L（0.04 s・NVIDIA）② 無ければ runtime の python で torch 列挙 1 回（1.2〜4 s）・UUID／名前／VRAM／index）・DriverCheck.cs（nvidia-smi の Driver Version・cu130 ≥ 580／cu126 ≥ 560.76 の閾＝決定 4・不足なら合成を撃たずに告知）
      Server/  ServerProcess.cs（env 組み立て→python.exe -m ywk_server →stderr 読み→状態機械 Starting/Ready/Failed/Stopped）・HealthPoller.cs（/health・/ywk/status）・ProcessTree.cs（起こした個体だけツリー kill＝本体の IrodoriServerProcessRegistry の型）
      Voices/  VoiceStore.cs（配布版の台帳 voices.ywk.json＝表示名・wav・caption 既定・既定パラメータ・preset フラグ）→ VoicesJsonWriter.cs（上流 voices.json＝ASCII 檔名＋日本語 alias・「デフォルト」= no_ref・原子的書き換え）
      Warmup/  WarmupClient.cs（POST /ywk/warmup・状態表示・既定＝§5）
      Settings/ Settings.cs（%LOCALAPPDATA%\irodori-tts-ywk\settings.json・GPU は UUID・変種・精度（上級者）・ポート（既定 18088）・暖機の既定・話者一覧の並び）
    Views/   MainWindow（状態・GPU・変種・起動／停止・ログ末尾）・VoicesView（一覧・追加・名付け・試聴）・TryView（本文・話者・steps プリセット 10/40・cfg・caption・seed→合成→再生／保存）・FirstRunWizard（通知文 first-run-notices.md の表示→変種の選択（ドライバ検出で cu126 を勧める・自動切替はしない）→取得→完了）・SettingsView・AboutView（非公式・ライセンス）
    Properties/PublishProfiles/win-x64.pubxml
  IrodoriTtsYwk.Launcher.Tests/       xUnit・純ロジック（台帳の計画・env 組み立て・状態機械・voices.json の生成・UUID→index 解決）＝本体の「テストの継ぎ目は public コンストラクタ」の流儀
probe/
  common.ps1（UIA・本体の型を倣う）・d-launch-probe.ps1（主要 6 操作）
build/
  release-build.ps1（本体の型＝publish→混入検分→pdb 退避→版刻印 zip・submodule pin を AssemblyMetadata に焼いて検分）
```

## 2. 起動の型（env と状態機械）

- env（`docs/design/ben-a-skeleton-and-build.md` §2・wrapper が setdefault するので**ランチャが載せるのは差分だけ**）＝`IRODORI_MODEL_DEVICE`／`IRODORI_CODEC_DEVICE`（`cuda:N`・UUID→index 解決の結果）・`YWK_VARIANT`（cuda／rocm-gfx1151）・`YWK_DATA_DIR`・`HF_HOME`・`HF_HUB_OFFLINE=1`（取得完了後）・`IRODORI_VOICES_DIR`／`IRODORI_VOICE_ALIASES_FILE`・`IRODORI_PORT`・上級者設定の精度（既定は wrapper の device 連動に任せる）・`PYTHONUNBUFFERED=1`。
- 状態機械＝`Stopped → Starting（stderr 1 行目 ywk_server <版>…）→ Listening（Uvicorn running on）→ Ready（runtime loaded in ＋ /health.runtime.loaded=true）→ Warming（/ywk/status.warmup.state=running）→ Ready`。`Failed`＝exit code 2（wrapper の事前検査）／3（上流の startup 失敗）／stderr の最終行を理由 1 行に。ready 待ち 120 s（CPU 変種は 300 s）。
- 停止＝ツリー kill（上流に shutdown 路は無い）。終了時・変種／GPU／ポート変更時に再起動。
- 本体との併存＝8088 の既存 Server とは無関係（別ポート）。本体は `/health`→`/ywk/status`→`/params`→`/v1/audio/voices` で発見（契約 ⑵）。

## 3. GPU と変種

- 列挙＝NVIDIA なら `nvidia-smi -L`（`GPU-<uuid>` と名前）＋`--query-gpu=index,uuid,name,memory.total,driver_version`。AMD／不明なら runtime の python で `torch.cuda.get_device_properties(i)`（uuid・name・total_memory・gcnArchName）。保存は UUID（decisions 34）。起動時に index へ解決＝見つからなければ「前回の GPU が見つからない」告知と選び直し。
- `CUDA_VISIBLE_DEVICES` に UUID を渡す経路は便 B の実射（B-4 で `CUDA_VISIBLE_DEVICES=GPU-xxxx` を 1 回試す）で採否を決める。採れれば index の揺れが消える。
- 変種＝cu130（既定）／cu126（ドライバ < 580 のとき勧める）／rocm-gfx1151（Radeon 版のリリースだけが持つ・UI の CUDA 版の選択肢には出さない＝decisions 5）／cpu（上級者・「遅い」の注記）。変種の切替＝別ディレクトリの runtime（`runtime\<variant>\`）を並置し `._pth` を書き分ける＝切替 0 s・容量は足し算（cu130 3.2 GB＋cu126 4.5 GB）。
- ドライバ検査＝cu130 ≥ 580／cu126 ≥ 560.76（便 B の U-14 の結果で cu126 の下限の謳い方を更新）。不足なら合成を撃たずに告知（受け入れ条件「ドライバ」行）。

## 4. 話者

- 配布版の台帳 `voices.ywk.json`（`{"schema":1,"voices":{"<id>":{"display_name","file","caption","params":{"num_steps":40,...},"preset":true|false,"origin":"preset|user","added_at"}}}`＝便 A の assemble-app が置く雛形と同形）。
- 追加＝wav（8 拡張子）を選ぶ→名前を付ける（日本語可）→ `voices\<ascii-id>.wav` に写し（id は内容 sha256 先頭 12＝本体の `ywk-<sha12>` の型を倣う）→ `voices.json` を原子的に書き換え（`{"<表示名>":{"ref_wav":"<ascii-id>.wav"}}`＋`{"デフォルト":{"no_ref":true}}`）。上流は毎要求 `iterdir()` なので再起動不要。**登録 API は使わない**（ASCII 限定・decisions 16）。
- プリセット 11（＋弦巻マキ）＝`voices/presets/*.wav` を初回に `voices\` へ写し、台帳に `preset:true`。削除は利用者の意思で可（再インストールで戻る）。
- 参照 wav の長さ＝10〜30 s を勧める文言（上流の推奨・120 s 上限で切り詰め警告）。

## 5. 暖機（便 C の結果で確定）

- Radeon 版＝既定 ON（no_ref 3 段＋最近使った話者 1〜3 名）。起動時に `POST /ywk/warmup`。`/ywk/status.warmup` を状態欄に出す。
- CUDA 版＝便 B の「未見の参照形状の罰」の実測で決める（罰が無ければ既定 OFF・cold 1 発だけ）。
- MIOpen db の置き場は wrapper が `YWK_DATA_DIR` から決める（ランチャは触らない）。

## 6. 初回取得（FirstRunWizard）

1. 通知（`licenses/first-run-notices.md` を表示・同意）→ 2. 変種の選択（ドライバ検出・容量の見積り・「CPU（遅い）」）→ 3. 取得（台帳順・並列 2 本まで・進捗・中断／再開）→ 4. 展開（wheel＝zip・sdist＝tar・`._pth`）→ 5. モデル（`ywk_fetch_models.py` を runtime の python で・JSON 行の進捗）→ 6. 起動して `/health`→完了（試し撃ちへ誘導）。
- 検証＝sha256 は全檔・不一致は破棄して再取得（最大 5 回）・`fallback_url` を持つ item は url→fallback の順。
- `vc_redist`＝`ledger/vc_redist.json` の直リンク→sha256→**`/install /quiet /norestart`**（台帳の `silent_args` が正・旧稿の `/passive` は誤り＝裁定 87 ⑷。実装 `Services/Ledger/VcRedistInstaller.cs` は既に台帳を採っている）。管理者昇格が要る＝UAC・利用者操作 1 回。既に `msvcp140.dll` が System32 にあれば飛ばす（便 B の U-8 の逐語で判定文言を決める）。

## 7. 卓へ（便 D 着工前に裁定が要る）

1. **ランチャの .NET ランタイム**＝SelfContained publish（1 exe・≈70 MB・MS ランタイムを自作分と一緒に焼く）か、FDD＋インストーラで公式 URL から取得（本体の型・「第三者バイナリを配布物に入れない」と整合するが利用者操作が 1 つ増える）か。推奨＝**SelfContained**（.NET ランタイムは MIT・再配布条項が明確・decisions 8 の趣旨（法解釈が立つ第三者物を避ける）に反しない）＝decisions に「例外」として記帳する。
2. **ポート衝突時**＝18088 が塞がっていたら次を探す（18089…）か止まるか。推奨＝止まって告知（本体の定数と食い違うため）。
3. **試し撃ちの再生**＝本体と同じ NAudio（MIT）で再生・音量は再生時に −16 dBFS 相当に揃える（decisions 38）。
4. **UI の言語**＝日本語のみ。

## 8. 追記（2026-09-05・裁定の反映）

- 裁定 51＝**SelfContained publish（1 exe）**で確定。`licenses/` に .NET の MIT と第三者通知を置く。
- 裁定 52＝ポート 18088 が塞がっていたら止まって告知。再生は NAudio（MIT）・再生時に −16 dBFS 相当へ揃える。UI は日本語のみ。
- 裁定 65＝**Radeon 版は参照潜在キャッシュを持つ**＝話者登録時（プリセットの初回展開・利用者の追加）に wrapper の `POST /ywk/voices/precompute` を叩き、`/ywk/voices` の `latent`／`latent_stale` を一覧に出す。CUDA 版は既定 OFF（設定で ON にできる）。
- 裁定 67＝**Radeon 版の UI**＝⑴ 潜在キャッシュの ON／OFF（設定）⑵ 参照ボイスごとの消費メモリの概算（wav 参照＝+0.7 GB 級・潜在参照＝増えない・出力 1 フレーム ≈3.5 MB・係数は便 C（2）の実測で確定）⑶ GPU メモリの使用量と占有量（wrapper の `/ywk/status.memory`＝torch の allocated／reserved／max・便 C（2）の後に wrapper へ足す。OS 側の GPU Process Memory は性能カウンタから読む）を常時表示。CUDA 版も同じ欄。
- 裁定 69＝`empty_cache_interval` は設定項目（初期値 0）。便 D はこの Radeon 機で着工＝UIA 検分と CUDA 固有の段は RTX 移行後。
- 便 C の実測から＝起動 26〜29 s・暖機 12.6〜51.8 s（機体初回 80.8 s）・暖機中の本物の待ちは走行中 1 射分（既定段で最大 ≈12 s）・`/ywk/status.warmup` を状態欄に出す。状態機械の `Warming` は `state=running` で表す。
- 便 A（2）・便 C から＝wrapper の起動ログ 3 行（`ywk_server <版> upstream=…`・`Uvicorn running on`・`ywk_server: device actual=…`）＋上流の `runtime loaded in`。exit 2＝wrapper の事前検査（理由 1 行）・exit 3＝上流の startup 失敗。`YWK_VARIANT`・`YWK_DATA_DIR`・`YWK_WARMUP_ON_START`・`YWK_WARMUP_VOICES`・`YWK_PRECOMPUTE_ON_START` の env。

## 9. 骨組み席の記帳（2026-09-05・便 D・opus サブ席）

`launcher/` に解が入った。**この節は実装の逐語であって、上の §1〜§8 の設計を覆すものではない**。

### 9-1 樹（実物）

```
launcher/
  IrodoriTtsYwk.sln            classic .sln（.NET 10 の既定は .slnx なので -f sln で作った）
  Directory.Build.props        net10.0-windows・nullable・**ImplicitUsings=disable**・版の一元定義（AppDisplayVersion=v0.1.0）
  IrodoriTtsYwk.Launcher/      WinExe・UseWPF・UseWindowsForms（トレイ）・NAudio.WinMM 2.2.1
    App.xaml(.cs)              単一起動（Mutex）＋トレイ（開く／サーバ起動／停止／終了）＋終了時に Stop
    AppServices.cs             組み立ての 1 箇所（3 席がここへ実装を差す）
    Views/MainWindow.xaml(.cs) **空の枠**（TabControl に 3 席が TabItem を足す）
    Contracts/                 共有の型（§9-2）
    Properties/PublishProfiles/win-x64.pubxml
  IrodoriTtsYwk.Launcher.Tests/ xUnit 2.9.3・純ロジックのみ・46 本
```

**`ImplicitUsings` は切ってある**。`UseWPF` と `UseWindowsForms` を同時に立てると、暗黙 using に入る
`System.Windows.Forms` と明示 using の `System.Windows` で `Application`・`MessageBox`・`Point` が
曖昧参照になる。using は 1 檔ずつ書く。

### 9-2 決めた 3 つ（3 席が前提にしてよい）

1. **場所は 2 つの根に割れる**（`Contracts/AppPaths`）＝⑴ **導入先**（exe の隣・**読むだけ**）に
   `server/`・`ledger/`・`licenses/`・`voices/presets/` ⑵ **利用者データ**
   （`%LOCALAPPDATA%\irodori-tts-ywk`）に `runtime/<変種>/`・`voices/`・`models/`（＝`HF_HOME`）・
   `cache/`・`logs/`・`settings.json`。導入先が Program Files でも壊れない形で、
   **`server/ywk_server.py` の `apply_env_defaults` と同じ場所**に揃えてある（テストで釘）。
   開発モード＝`YWK_LAUNCHER_APP_DIR`／`YWK_LAUNCHER_RUNTIME_DIR`／`YWK_LAUNCHER_DATA_DIR`。
   変種ディレクトリは `<root>\<変種>`（配布）→ `<root>\runtime-<変種>`（`build/out`）→ `<root>` の順に探す。
2. **変種の名前は 2 系統**（`Contracts/RuntimeVariants`）＝**台帳の綴り**（`cu130`／`cu126`／`cpu`／
   `rocm-gfx1151`＝`ledger/runtime-<変種>.json` と `runtime\<変種>\`・**設定に保存するのはこちら**）と、
   **`YWK_VARIANT` のラベル**（`cuda`／`cpu`／`rocm-gfx1151`＝cu130 と cu126 は**どちらも `cuda`**）。
   混ぜると `/ywk/status.variant` の突合が嘘になる。
3. **ランチャが env に載せるのは差分だけ**（`Contracts/ServerEnvironment`）＝wrapper が `setdefault` で
   焼く 6 欄（`IRODORI_HF_CHECKPOINT`・`IRODORI_PRELOAD`・`IRODORI_ALLOW_NO_REF_VOICE`・
   `IRODORI_DEFAULT_VOICE`・`IRODORI_DEFAULT_NUM_STEPS`・`IRODORI_DEFAULT_RESPONSE_FORMAT`）は
   **載せ直さない**（二重定義を作らない）。載せるのは場所・変種・device 2 本・精度（rocm は載せない）・
   `empty_cache_interval`・暖機／事前計算・Python の作法 4 本。

### 9-3 発行の実測（この機体・2026-09-05）

```
dotnet publish launcher/IrodoriTtsYwk.Launcher/IrodoriTtsYwk.Launcher.csproj -p:PublishProfile=win-x64 \
  -p:UpstreamIrodoriTts=8224daf -p:UpstreamIrodoriTtsServer=841fb7c
```

**1 exe・69,479,003 B**（≈66 MiB＝§7 の「≈70 MB」の見積りどおり）＋ pdb 55,304 B。
上流 pin は **publish のときだけ** `AssemblyMetadata` に焼かれる（Debug ビルドの dll には
`8224daf` の文字列が 0 件であることを機械で確認）。着地は `build/out/launcher/win-x64/`（git 管理外）。

**裁定 51 の記帳先＝`licenses/dotnet/`**（.NET／WPF ランタイム 10.0.11 の MIT 全文＋第三者通知）。
`licenses/README.md` §1 の索引に 4 行足した（`build/check-licenses.ps1` は表と実檔の 1:1 を要求する＝
足さないと A-6 が落ちる）。**NAudio 2.2.1 の許諾全文は未取得**＝`licenses/dotnet/README.md` §3 に欠落として記帳。

### 9-4 まだ無い物（3 席と後続へ）

- `Services/`（Downloader・RuntimeInstaller・ServerProcess・WrapperClient・GpuEnumerator・VoiceStore）は
  **契約（interface）だけ**が在る。既定は `NullServerProcess`＝「サーバ起動」を押すと理由 1 行が出る。
- `probe/common.ps1`・`probe/d-launch-probe.ps1`・`build/release-build.ps1` は未着手（裁定 69 ⑶＝
  UIA 検分は RTX 移行後）。
- トレイと窓のアイコンは `SystemIcons.Application`（専用 ico は便 E の資産）。

## 10. 取得席（L1）の記帳（2026-09-05・便 D・opus サブ席）

`launcher/IrodoriTtsYwk.Launcher/Services/Ledger/`（8 檔）と `Services/Models/ModelFetcher.cs` に
台帳・取得・展開・vc_redist・モデルの実装が入った。**§6 の設計を実装したものであり、覆していない**。

### 10-1 入った物

| 檔 | 役 |
|---|---|
| `Ledger/LedgerReader.cs` | 台帳 4 種を読む＋検分（count・sha256・license・kind・package_dir）。`WithPythonEmbed` で埋め込み Python を先頭に足す |
| `Ledger/FetchPlanner.cs` | **vc_redist → python-embed → runtime-<変種> → models** の計画・容量見積り・`DownloadRequest` への変換（すべて純関数） |
| `Ledger/HttpDownloader.cs` | Range 再開・`.part`→Rename・sha256（`SHA256.HashDataAsync`）・`fallback_url`・最大 5 回・並列 2 本・進捗（bytes／ETA）・中断 |
| `Ledger/ArchiveExtractor.cs` | zip／`tar.gz`（**.NET の `System.Formats.Tar`**＝`tar.exe` に依らない）・zip slip 防御 |
| `Ledger/MinimalDistInfo.cs` | sdist／archive の最小 `*.dist-info`（利用者機に pip は無い） |
| `Ledger/WheelInstaller.cs` | `IRuntimeInstaller`。wheel の `.data` 合流・`*.pth` 破棄・sdist／archive の `package_dir(s)` |
| `Ledger/PthWriter.cs` | `python312._pth`（UTF-8 BOM なし・CRLF・古い `*._pth` を消してから書く） |
| `Ledger/VcRedistInstaller.cs` | System32 の `msvcp140.dll` 判定 → 取得 → sha256 → UAC 昇格で silent 実行 |
| `Models/ModelFetcher.cs` | 変種の `python.exe` で `server/ywk_fetch_models.py` を子プロセス実行・stdout の JSON 行を進捗に・終了コード 0/2/3/4 |

`AppServices.Downloader`／`AppServices.RuntimeInstaller` は**既定でこの実装を返す**（型は null 許容の
まま＝`is null` で見ている呼び手を壊さない）。`WheelInstaller` は変種の台帳に埋め込み Python の item が
無ければ**配布樹の `ledger/python-embed.json` を自分で読む**ので、呼び手は `WithPythonEmbed` を通しても
通さなくても組める。

### 10-2 実走の裏取り（2026-09-05・この機体・**ネットに 1 バイトも出していない**）

`build/cache/` の実檔（便 A が落とした 101 item）だけを使い、外へ出る HTTP を投げる handler で塞いだ
状態で `runtime-cpu` を scratch へ組み、`python.exe` で import を通した。

```
PLAN variant=cpu steps=106 total=3.62 GiB cache=305.2 MiB models=3.33 GiB / peak-disk=4.53 GiB
VERIFY 102 cached files against the ledger sha256 (no network)
VERIFY cache-hit=102/102 failed=0 in 0.2s
INSTALL ok=True in 19.1s :: 25012 檔・903.3 MiB・dist-info 101 件・捨てた .pth 1 件
DROPPED-PTH distutils-precedence.pth
$ python.exe -c "import torch, torchaudio, soundfile, transformers, argbind, randomname, dacvae, silentcipher"
python 3.12.10 / torch 2.10.0+cpu / torchaudio 2.10.0+cpu / transformers 5.16.1 / OK
```

**`build/assemble-runtime.ps1` の成果物との突合**（`build/out/runtime-cpu` と檔ごと sha256・
`__pycache__` を除く）＝**両側 25,012 檔・欠落 0・余分 0・中身が違うのは 9 檔だけ**：

1. `python312._pth`（差すディレクトリが違うので当然）
2. `argbind`／`randomname`／`dacvae`／`silentcipher` の `dist-info` の `METADATA` と `WHEEL`（計 8 檔）
   ＝**「誰が置いたか」の 1 行だけ**が違う（`Generator: irodori-tts-ywk assemble-runtime.ps1` に対し
   `Generator: irodori-tts-ywk IrodoriTtsYwk.Launcher`）。`importlib.metadata` が読む `Name`／`Version`
   は同一。台本を名乗る嘘をつかないための意図的な差で、`MinimalDistInfo.Generator` の 1 箇所に閉じてある。

**`cache/` の檔名は台本と 1 字も違わない**（`Contracts/Ledger.EffectiveFileName` に
`Get-YwkCachedItem` の規則を入れた＝GitHub の `/archive/<sha>.zip` は `<name>-<sha>.zip` に前置する。
入れる前は `dacvae`・`silentcipher` の 2 件が別名になり、台本の落とした檔を「無い」と見て取り直していた）。

## 11. 起動席（L2）の記帳（2026-09-05・便 D・opus サブ席）

`launcher/IrodoriTtsYwk.Launcher/Services/` に **Server・Gpu・Voices・Settings** が入った。
**この節は実装の逐語であって、§1〜§8 の設計を覆すものではない。**

### 11-1 樹（足した物）

```
Services/
  LauncherComposition.cs    AppServices に差す 1 箇所（App.OnStartup が呼ぶ・**トレイと窓を作る前**）
  Server/
    ServerProcess.cs        IServerProcess の実装（＋ ServerLaunchPlan＝起動材料の純関数）
    ServerStateMachine.cs   状態機械の本体（**子プロセスを持たない**）＋ ServerBindFailure（裁定 52）
    ProcessTree.cs          ILaunchedProcess／LaunchedProcess／LaunchedProcessRegistry（ツリー kill）
    TcpPortProbe.cs         起こす前の bind 検査（IPortProbe）
    HealthPoller.cs         IReadinessProbe＝/ywk/status →（読めなければ）/health
  Http/WrapperClient.cs     IWrapperClient の実装（HttpMessageHandler が継ぎ目）
  Gpu/
    GpuEnumerator.cs        ① nvidia-smi ② torch 列挙（＋ IProcessRunner／ProcessRunner）
    NvidiaSmiParser.cs      -L と --query-gpu の読み（**純関数**）
    TorchGpuProbe.cs        列挙の台本（ASCII の Python 文字列）と読み（**純関数**）
  Voices/
    VoiceStore.cs           voices.ywk.json の読み書き・追加・削除・sha256
    VoicesJsonWriter.cs     上流の別名表 voices.json（**書くときに読み直す**）
    PresetVoices.cs         プリセットの初回展開（配布樹は読むだけ）
    WarmupCoordinator.cs    /ywk/warmup と /ywk/voices/precompute（**無ければ「未対応」**）
  Settings/SettingsDefaults.cs  変種で変わる既定（**純関数**）
```

テストは `IrodoriTtsYwk.Launcher.Tests/` に 5 檔・**97 本**（状態機械・env 組み立て・GPU 列挙とドライバ・
話者と voices.json・HTTP）。**実機・実 GPU・実ポートに触れない**（外の実行檔は `IProcessRunner`、
HTTP は `HttpMessageHandler` の偽物）。

### 11-2 実機で判った 3 つ（この機体・rocm-gfx1151・私設ポート 18095〜18097）

1. **`/ywk/status.device.pci_bus_id` は数で来る**。ROCm の torch は
   `get_device_properties(i).pci_bus_id` を**整数**（`197`）で返し、wrapper は `_cuda_device_info` で
   そのまま載せる。契約 ⑹ の逐語はこの欄の型を書いていない。`string` 決め打ちで読むと
   **`/ywk/status` の応答 1 本が丸ごと読めなくなる**（実測＝「応答の本文が読めませんでした」）。
   ⇒ ランチャ側に `TolerantStringConverter` を入れて数・真偽も飲む。
   **卓へ**＝wrapper が `str()` を掛けるか、契約に型を書くかは便 C（2）の領分。
2. **`runtime loaded in` が `Uvicorn running on` より先に出ることがある**（実測 3 回中 3 回）。
   上流は startup event でモデルを載せ、uvicorn の 1 行はその後に出る。**後戻りしない**規律
   （`ServerLogParser.NextState`）のおかげで `Ready → Listening` の逆走は起きないが、
   「Listening を見てから Ready を待つ」実装にしてはいけない。
3. **停止は「殺してから待つ」順でなければならない**。個体の所有者は
   `LaunchedProcessRegistry` で、`Dispose` すると `Process` ハンドルごと解放されるため、
   その後に `WaitForExitAsync` を呼ぶと `InvalidOperationException: No process is associated
   with this object` で落ちる（実測）。⇒ `StopAsync` は ⑴ 自分の handle で `Kill(entireProcessTree)`
   ⑵ 抜けるまで待って終了コードを読む ⑶ 台帳を `Dispose`（自然死を見て何もしない）の順。

### 11-3 実測（2026-09-05・この機体・GPU は便 C（2）と共有中）

| 段 | 値 |
|---|---|
| GPU 列挙（torch 経路・`nvidia-smi` 不在） | **2.89〜3.47 s**（受け入れ条件 D-2 の ≤ 5 s 内）・`gfx1151`・UUID `30303030-…` |
| spawn → Ready | **30.2 s**（`runtime loaded in 26.98s`）＝便 C の 26〜29 s と整合 |
| 合成 1 射（「あ。」・num_steps 10・参照なし） | **5.36 s**・99,884 B・`audio/wav` |
| 停止（ツリー kill → 抜けるまで） | **1 秒未満**・残骸 0（`tasklist` で確認） |
| `/ywk/status.memory` | **欄がまだ無い**（便 C（2）が足す最中＝`MemoryStatus` は null で返り、落ちない） |

**8088・7861 は起こしていない。**私設ポート 18095／18096／18097 だけを使い、終わるたびにツリー kill した。
便 B・便 C の走行（`runtime-cpu`・ポート 18090）には触れていない（**起こした個体だけを落とす**台帳の
おかげで、同時に走っていた他席の python 2 個体は無傷だった＝実測）。

### 11-4 決めた 4 つ（他席が前提にしてよい）

1. **ready の判定は 2 系統の合流**＝ログ（`runtime loaded in`）と `/ywk/status.runtime.loaded`。
   どちらか早い方で Ready になる。`Warming` は `/ywk/status` の `warmup.state=running`
   （＋`precompute.state=running`）だけが立て、暖機が終われば `Ready` に戻る。
2. **ポートは起こす前に検める**（`TcpPortProbe`＝`ExclusiveAddressUse` で 1 回 bind）。
   検査と起動の間の窓は uvicorn の bind 失敗行（`ServerBindFailure.Detect`）で塞ぐ。
   **どちらも「止まって告知」**＝次のポートを探さない（裁定 52）。
3. **`bool` の設定欄に「未設定」は無い**ので、**変種を選んだ瞬間に既定を書き込む**
   （`SettingsDefaults.ApplyVariant`）。`rocm-*` は暖機 ON・精度は捨てる・CPU 変種は GPU の UUID を捨てる。
   事前計算だけは `null` のまま置く（`PrecomputeOnStartDefault` が wrapper の未設定時の解決と
   同じ規則＝**二重定義を作らない**）。
4. **`voices.json` は書くときに読み直す**（契約 ⑷ 4-3）。wrapper が事前計算で書いた
   `ref_latent` を、ランチャの上書きで消さない（`VoicesJsonWriter.ReadExistingLatents`）。

## 12. 画面席（L3・Views／ViewModels）の記帳（2026-09-05・便 D・opus サブ席）

§1 の `Views/` 行が実物になった。**この節は実装の逐語であって §1〜§8 の設計を覆さない**。
番号は席ごとに取った（L1 が 10・L2 が 11 を想定）。

### 12-1 入った物

| 層 | 檔 | 何を持つか |
|---|---|---|
| `Mvvm/` | `ObservableObject`・`RelayCommand`／`AsyncRelayCommand` | 自前の `INotifyPropertyChanged` と `ICommand`（**依存追加なし**） |
| `ViewModels/` | `MainViewModel` | 5 画面を束ね、**横の繋がりだけ**を配る（話者一覧が変われば試し撃ちの候補と概算メモリが動く） |
| | `StatusViewModel` | 状態・理由 1 行・GPU（UUID 下 6 桁）・変種・接続先・device 実測・暖機・事前計算・**GPU メモリ**・ログ末尾 20 行 |
| | `VoicesViewModel`・`VoiceRow(Builder)`・`VoiceNameValidator` | 一覧・追加（2 操作）・削除・試聴・`latent`／`latent_stale`・概算メモリ |
| | `TryViewModel`・`SpeechRequestBuilder`・`NumericInput` | 本文・話者・steps（10／40／数値）・cfg 3 種・caption・seed・speed→合成→再生／保存・所要／出力秒／RTF／seed |
| | `SettingsViewModel`・`WarmupStagesText` | GPU・変種・精度・ポート・暖機・潜在キャッシュ・`empty_cache_interval`・置き場（**写しの上で編集し「適用」で書く**） |
| | `AboutViewModel` | 非公式の断り・版・上流 pin・透かし・参照ボイスの但し書き・許諾の所在 |
| | `FirstRunViewModel`・`ReleaseFlavor`・`UiText`・`MemoryEstimate`・`LogTail` | 7 段のウィザードと、5 画面が共有する純関数の道具 |
| `Audio/` | `IAudioPlayer`・`NAudioPlayer`・`WavInfo`・`WavGain` | 再生（裁定 52）と、頭読み・利得の**純関数** |
| `Views/` | `MainWindow`（5 タブ）・`StatusView`・`VoicesView`・`TryView`・`SettingsView`・`AboutView`・`FirstRunWizard` | XAML＋窓が要る仕事だけ（檔選択・保存・確認・`Dispatcher`・時計） |

### 12-2 決めた 5 つ

1. **ViewModel は WPF の型に触れず `Dispatcher` も持たない**。子プロセスの事象を UI スレッドへ渡すのは
   `MainWindow` の仕事である。これで ViewModel が xUnit から素で作れる（**L3 のテスト 130 本**）。
2. **選択肢は配布樹が決める**（`ReleaseFlavors`）＝`ledger/runtime-rocm-*.json` を持てば Radeon 版で
   CUDA の選択肢を出さない（裁定 5）。さらに**台帳が実在する変種だけ**に絞る。版の種類を exe に焼かない。
3. **「既定に戻す」は欄を空にする**（`NumericInput`）＝空欄は `null`（載せない）・`0` は `0`（載せる）。
   上流に範囲検査が無いので、歩数（1〜120）・読み速さ（0.25〜4.0）・seed は**撃つ前に 0 s で弾く**。
   `speed` と `irodori.duration_scale` は同時に送らず、`irodori.seconds` は本番に載せない（テストで釘）。
4. **`/ywk/status.memory` が無ければ「未対応」と出す**（裁定 67 ⑶）。参照ボイスごとの概算は
   `MemoryEstimate`（wav 参照 0.7 GiB・潜在 0・出力 1 フレーム 3.5 MiB）に閉じ、**「概算」と明示する**。
   **秒→フレームの換算率は席が知らない**ので `ForOutputFrames` は<b>フレーム数</b>を受ける
   （推測の frame rate を掛けて実測と桁で食い違う欄を作らない）。
5. **「デフォルト」は削除できない**（§3「決まっていないこと」2 の決着）＝`VoiceRow.CanRemove` が偽。
   `none`・`no-ref` 等の別名は一覧に出さない（同じ意味の話者が 2 つ見える状態を作らない）。

### 12-3 実測（この機体・2026-09-05・**サーバも GPU も起こさず**）

`YWK_LAUNCHER_DATA_DIR` を私設の temp に向け、`autoStartServer:false`・`port:18094` の
`settings.json` を置き、`YWK_LAUNCHER_RUNTIME_DIR` を**渡さずに**（`python.exe` が見つからない形で）
窓だけ立てた。`UIAutomationClient` で読めた（受け入れ条件 D-7 の下ごしらえ）＝

- 窓 `MainWindow`（`irodori-TTS for 読み分けちゃん`）と 5 タブ（`TabStatus`／`TabVoices`／`TabTry`／
  `TabSettings`／`TabAbout`）。**タブの中身は選ぶまで作られない**（`TabControl` の遅延生成）＝
  台本は `SelectionItemPattern` で選んでから id を探す。
- 状態帯＝`StatusStateText`「停止」・`StatusVariantText`「Radeon gfx1151（未保障・bf16 固定）」・
  `StatusEndpointText`「http://127.0.0.1:18094」・**`StatusMemoryText`「未対応」**（wrapper が居ないため）。
- 初回取得＝`MainFirstRunButton` を `InvokePattern` で押すと `FirstRunWizard` が出て
  `FirstRunStepNumber`「1 / 7」・`FirstRunNextButton`「同意して次へ」・`FirstRunAcceptCheck` を拾えた。
- **8088・18088・18094・7861 のどれも listen していない**ことを `Get-NetTCPConnection` で確認し、
  終わったら `taskkill /T /F`（残り 0 個体）。id の一覧は `launcher/README.md` §7-3。

### 12-4 卓へ／後続へ

1. **wav 以外の試聴が「未対応」**＝`NAudio.WinMM` と `NAudio.Core` に `Mp3FileReader`／
   `AudioFileReader` が無い（nupkg の型一覧を機械で確認）。依存を増やすなら NAudio のメタパッケージ
   （＋許諾文の追記）が要る。**登録と合成には影響しない**（参照 wav は上流が読む）。
2. **概算メモリの係数は仮**＝`MemoryEstimate.IsProvisional` が真の間、画面は「実測前の概算」と出す。
   便 C（2）の実測が出たら定数 3 つと `IsProvisional` を落とす（1 檔で済む）。
3. **`/ywk/status.memory` の欄名は仮のまま**（`allocated`／`reserved`／`max`／`latents`）。
   便 C（2）の綴りで確定したら `Contracts/WrapperResponses.MemoryStatus` を直せば画面は追随する。
4. **ポート衝突（裁定 52）の告知は文言だけ**＝bind 失敗を検出するのは起動席の仕事で、
   検出されれば `ServerStartResult.FailureReason` がそのまま `StatusReasonText` に出る。
5. **モデル取得の段は口だけ**＝`FirstRunViewModel.ModelFetcher`（`IProgress<string>` を受ける
   デリゲート）に取得席が `ywk_fetch_models.py` の子プロセスを差す。差さっていない間は
   「実装待ち」と告げて止まる（黙って成功したことにしない）。

## 13. 統合席の記帳（2026-09-05・便 D・opus サブ席）

3 席（L1 取得・L2 起動・L3 画面）の解を 1 つに合わせ、**この機体で実際に窓を動かして**受け入れ条件
D-1〜D-6 を撃った。`probe/common.ps1`・`probe/d-launch-probe.ps1`・`build/release-build.ps1` が入り、
§9-4 の「まだ無い物」は全部埋まった。**この節も実装の逐語であって §1〜§8 の設計を覆さない。**

### 13-1 入った物

| 檔 | 役 |
|---|---|
| `probe/common.ps1` | UIA の共通ドライバ（本体 `probe/ai-multi-p7/common.ps1` の型・**檔は写していない**）。ASCII 限定なので日本語は `New-JpText 0x5F85,0x6A5F` のように**符号位置から組む** |
| `probe/d-launch-probe.ps1` | 無人検分の本体（受け入れ条件 D-7）。**27 段**を撃って 1 段でも落ちたら非ゼロ終了。`-BadDeviceOnly` で D-1 だけを 3 秒で撃てる |
| `build/release-build.ps1` | publish（SelfContained 1 exe）→ 混入検分 6 項 → pdb 退避 → 版刻印 zip |
| `launcher/…Tests/IntegrationSeatTests.cs` | 実機で見つけた穴を釘付けする 8 本（合計 **370 本**が緑） |

### 13-2 実機で見つけて直した 4 つ（**机上の 356 本は全部緑だった**）

1. **「数え直す」で選んだ GPU が保存されなかった**（裁定 34・受け入れ条件 D-2 に反する）。
   `SettingsViewModel.RefreshGpusAsync` は `_selectedGpu` に**欄だけ**入れて写し（`_draft.GpuUuid`）を
   触らなかったので、画面には GPU が選ばれて見えるのに「適用」は灰色のままで、
   `settings.json` の `gpuUuid` は空だった（＝起動のたびに index 0 へ落ちる）。
   ⇒ 保存済みの UUID と違う個体を選び直したときだけ写しに書き、**未保存の印**を立てる。
   「前回の GPU が見つからない」の告知は**書き換える前の**名前と UUID で出す（釘 4 本）。
2. **「適用」しても状態帯が古い値のままだった**（GPU＝「未選択」）。画面どうしは互いを知らないので、
   横の繋がりは束ねる側が配る規律（§12-2 ⑴）に従い、`SettingsViewModel.Applied` を
   `MainViewModel` が拾って `Status.ApplySettings` を呼ぶ 1 行を足した。
3. **末尾 20 行が自分の足音で埋まった**。窓は 2 秒ごとに `/ywk/status` を読み、uvicorn がその 1 本ごとに
   access log を吐くので、**約 40 秒で受け入れ条件 D-4 の 3 行が画面から押し出される**（実測）。
   ⇒ `ServerLogParser.IsLauncherPollNoise`（**純関数**）を足し、`LogTail` が
   **200 で返った見張りの GET だけ**を落とす。合成・422・500・暖機は 1 行も落とさない。
4. **`GpuInfo` の record 既定の `ToString()` が UI Automation に漏れていた**。`ComboBox` は
   `DisplayMemberPath="Label"` で正しく描くが、**項目の Automation 名は `ToString()` から作られる**ので、
   無人検分は `GpuInfo { Uuid = …, Name = … }` を読んでいた（実測）。⇒ `ToString()` を `Label` に揃えた。

### 13-3 直さずに形を変えた 1 つ

**参照 wav の場所を貼り付けられるようにした**（`VoicesSourcePathBox` の `IsReadOnly` を外した）。
「選ぶ…」が開く共通檔窓は**作られてはいる**（Win32 の `EnumWindows` は `#32770` を見る）が、
**UI Automation からは見えない**＝`RootElement` の子に 1 度も現れない（`InvokePattern`・実マウス・
キーボードのどれで開けても同じ・.NET の crash も 0 件）。無人の台本がこれを開くと**閉じられない窓**で
走行が止まるので、検分は貼り付けの路を通す。利用者にとっても操作数は同じ 2 つ（檔を指す・名前を付ける）。
**卓へ**＝共通檔窓を UIA で駆動したい場面（便 E のインストーラ検分など）があるなら、この事実を先に知っておくこと。

### 13-4 実測（この機体・rocm-gfx1151・私設ポート 18094／18096・**8088 と 7861 は無傷**）

| 段 | 値 | 条件 |
|---|---|---|
| D-1 範囲外 GPU（`cuda:9`） | **exit 2・1.65〜1.8 s**・理由 1 行 | ≤ 15 s |
| D-2 GPU 列挙（torch 経路） | **2.2 s**・UUID `30303030-…`・`gfx1151` | ≤ 5 s |
| D-2 UUID の保存 → index 解決 | `settings.json` に UUID・起動ログが `device actual=cuda:0` | — |
| D-6 「サーバ起動」→ 待機 | **27.5〜28.9 s**（`runtime loaded in 23.9〜24.2s`） | ≤ 120 s |
| D-4 ログ 3 行＋device 実測 | 4 行とも出る・access log の混入 **0 行**・422 の echo **0 行** | — |
| 試し撃ち（参照なし・10 歩） | **所要 5.4〜5.6 s／出力 1.36 s／RTF 4.0** | — |
| D-3 話者追加（2 操作・日本語名） | 一覧に即反映（**再起動なし**）・`ywk-c962284a59da.wav`・事前計算が自動で走る | — |
| 試し撃ち（追加した話者・10 歩） | **所要 4.65 s／出力 3.60 s／RTF 1.29** | — |
| 停止 → ツリー kill | **1 秒未満**・python 残骸 **0**・ポート解放 | — |
| D-5 取得→展開→`._pth`→import | 下の 13-6 | — |

`/ywk/status.memory` は**まだ無い**ので状態帯は「未対応」（裁定 67 ⑶＝便 C（2）待ち）。

### 13-5 `build/release-build.ps1` の実走（2026-09-05）

```
[STEP 0]  version = v0.1.0（launcher/Directory.Build.props の唯一の定義を機械読取）
          upstream = 8224dafb…（Irodori-TTS）／841fb7c6…（Irodori-TTS-Server）
          ＝**台帳から読む**（`ledger/runtime-*.json` の `upstream` が全変種で一致することを検分）
[STEP 1]  dotnet publish -p:PublishProfile=win-x64 -p:UpstreamIrodoriTts=8224daf -p:UpstreamIrodoriTtsServer=841fb7c
[STEP 2a] Microsoft.NETCore.App.Runtime.win-x64 = 10.0.11／WindowsDesktop = 10.0.11
          ＝`licenses/dotnet/README.md` の表と突合（違えば止まる＝許諾文が成果物より古いまま配られない）
[STEP 2b] (b) exe 在り (c) 1 檔だけ（exe と pdb 以外が出たら失敗） (d) 第三者物・利用者データ 0 件
          (e) 上流 pin と表示版が焼かれている (f) 大きさが 40〜200 MiB
[STEP 3]  pdb 退避 1 件 → build/out/launcher/pdb-v0.1.0-<刻>
[STEP 4]  build/out/launcher/irodori-tts-ywk-launcher-v0.1.0-<刻>.zip
DONE      exe **69,588,729 B（66.4 MiB）**・zip 64,019,555 B（61.1 MiB）・所要 11.2 s・EXIT=0
```

**発行した 1 exe そのものを無人検分に通した**（`-Exe build/out/launcher/win-x64/IrodoriTtsYwk.Launcher.exe
-SkipSynthesis`）＝**21 段すべて緑**・状態帯の版が
`版 v0.1.0／Irodori-TTS 8224daf／Irodori-TTS-Server 841fb7c` になる（Debug ビルドは「開発ビルド」）。
起動→待機 27.5 s・ツリー kill で残骸 0＝**自己展開の 1 exe でも Debug と同じ数字が出る**。

**pin の焼き込みの検分は中間アセンブリを読む**＝`PublishSingleFile` は管理アセンブリを exe の中へ
（圧縮して）埋めるので成果物からは読めない。埋められた当の檔は
`launcher/…/obj/Release/net10.0-windows/win-x64/IrodoriTtsYwk.Launcher.dll` で、そこを
**バイト走査**する（`MetadataLoadContext` の package が無い機体でも動く＝素の機体で止まらない）。

### 13-6 D-5（初回取得）の煙試験＝**取得 13.0 KiB だけ**で全段を通した

`build/cache/`（便 A が落とした実檔）を種に scratch のキャッシュを作り、**台帳のうち最も小さい 3 件を
わざと抜いて**本当に落とさせた。残り 99 件は sha256 で検分するだけ（1 バイトも落とさない）。

```
LEDGER   runtime-cpu items=101 problems=0
PLAN     取得 3.62 GiB・必要な空き 4.53 GiB
         VcRedist 1 / 24.4 MiB・PythonEmbed 1 / 10.6 MiB・Runtime 101 / 270.1 MiB・Models 3 / 3.33 GiB
FETCH    ok=102/102  cache-hit=99  downloaded=3（**13.0 KiB**）in 0.4s
         annotated_doc-0.0.5 / ffmpy-1.0.0 / tensorboard_data_server-0.7.2（すべて sha256 一致・attempts=1）
INSTALL  ok=True in 10.8s :: **25,012 檔・903.3 MiB・dist-info 101 件・捨てた .pth 1 件**
PTH      python312.zip / . / <runtime>/site-packages / <app>/server / …/Irodori-TTS / …/Irodori-TTS-Server/src
IMPORT   exit=0  python 3.12.10 / torch 2.10.0+cpu cuda None / torchaudio 2.10.0+cpu / transformers 5.16.1 / OK
```

**§10-2 の L1 の実走と 1 檔も違わない**（25,012 檔・903.3 MiB・`distutils-precedence.pth` 1 件）。
違うのは「3 件を実際に HTTP で取った」ことだけで、`.part`→改名・sha256・原子的着地が実弾で通った。
モデル（3.33 GiB）と vc_redist（UAC）はこの煙試験の外＝`FetchPlanOptions` で外している。

### 13-7 卓へ／後続へ

1. **共通檔窓（`OpenFileDialog`）は UIA から見えない**（13-3）。便 E のインストーラ検分を UIA で書くなら、
   窓を開かせない路を先に用意すること。
2. **`/ywk/status.memory` と概算メモリの係数**は便 C（2）待ち（§11-2・§12-4）。欄が入ったら
   `Contracts/WrapperResponses.MemoryStatus` と `MemoryEstimate` の定数 3 つを直せば画面は追随する。
3. **`Contracts/IWrapperClient` の位置レコード 3 つに `[JsonPropertyName]` が無い**（§11 の open issue）。
   いまは `WrapperClient.JsonOptions` の `SnakeCaseLower` が救っているが、snake_case にならない綴りを
   足した日に黙って null になる。**契約側に属性を書くのが筋**（骨組み席の檔なので統合席も触っていない）。
4. **UIA 検分は RTX 移行後にもう一度**（裁定 69 ⑶）。台本は変種とポートを引数で受けるので
   `-Variant cu130 -Port 18094 -ReadyTimeoutSeconds 120` で同じ 27 段が撃てる。CUDA 機では
   `nvidia-smi` 経路（0.04 s）に落ちるので、D-2 の所要はここより速くなるはずである。
5. **`probe/d-launch-probe.ps1` は `HF_HOME` を機体の既存キャッシュへ向ける**
   （`%USERPROFILE%\.cache\huggingface`・`HF_HUB_OFFLINE=1` で読むだけ）。
   向けないと私設のデータ置き場にモデルが無く、`exit 3`＝「モデルの読み込みに失敗した」で止まる（実測）。
   **初回取得を通していない機体で D-6 を撃つときは、まず取得を済ませること。**


## 14. 是正席の記帳（2026-09-05・便 D・opus サブ席）

敵対検分 3 席（起動・取得・UI）の所見 18 件のうち **high 7・medium 11 を全部直した**（受け入れ条件 D-8）。
**この節も実装の逐語であって §1〜§8 の設計を覆さない**。覆る形になった 2 つ（§14-3）は別に記す。

### 14-1 直した 18 件

| # | 所見 | 直し方 |
|---|---|---|
| 1 | **待機が listen を待っていない**（`runtime loaded in` 1 行で Ready） | `ServerLogParser.NextState` から `RuntimeLoaded => Ready` を外し、印（`ServerStateMachine.RuntimeLoadedSeen`）だけ立てる。Ready は `/health`・`/ywk/status` の `runtime.loaded=true` の標本だけが立てる（契約 ⑵）。`ApplyReadiness` の返りも `reachable && (Ready or Warming)` にした |
| 2 | **期限切れで子を殺さず、止める口も無い** | `StartAsync` が Ok=false で返る全経路（期限切れ・ログの bind 失敗・取消）で `KillChildAsync`（ツリー kill・状態機械には触れない）を通す。保険に `StatusViewModel.CanStop`＝`IsRunning ‖ HasProcess` を足し、トレイの停止も同じ判定にした |
| 3 | **裸の `10048` を bind 失敗と読む** | 標識から外し、`bind`／`socket`／`WSAEADDRINUSE`／`ソケット`／`アドレス` と同居する行に限った。加えて `Detect` を掛けるのは `State < Listening` の間だけにした |
| 4 | **nvidia-smi の index を torch の index として使う** | 子の env と torch 列挙の台本の両方に `CUDA_DEVICE_ORDER=PCI_BUS_ID` を載せて 2 つの index 空間を揃え、Ready の後に `/ywk/status.device.uuid` と保存 UUID を突合して状態帯に 1 行出す（`StatusViewModel.GpuMismatch`）。保存 GPU が列挙に見つからないときは**黙って cuda:0 に落とさず止まって告知**する |
| 5 | **「届かなくなった」の枝が無い** | 連続 3 標本（≈6 秒）到達不能なら Ready／Warming から `Listening` へ落とし、理由 1 行を運ぶ。**殺しはしない**（プロセスが生きている間は告知だけ） |
| 6 | **走行中の「適用」で状態帯が嘘になる** | 起動時の設定の写し（`StatusViewModel.BeginRun`）から描き、食い違う間は「設定は次回の起動から有効（いま走っているのは …）」を出す |
| 7 | **fallback で `.part` が壊れる** | offset の算出を `foreach (var url in urls)` の**内側**へ移し、「1 バイトも増えなかった」の判定も url 単位にした |
| 8 | **モデルを 1 檔も取らず vc_redist を走らせない** | `MainViewModel.CreateFirstRun` が `ModelFetcher`（変種の python で `ywk_fetch_models.py`）と `VcRedistRunner`（判定→取得→sha256→UAC）を差す。vc_redist は取得の段の**先頭**で通し、その結果を `FetchPlanOptions(SkipVcRedist:)` に渡す |
| 9 | **cache の原檔を展開前に検めない** | `WheelInstaller.VerifiedCachedPathAsync` が台帳の sha256 と突き合わせ、`sha256` を持たない item は `LedgerException` で止める（`assemble-runtime.ps1` の 146〜148 行と同じ規律） |
| 10 | **検分が dist-info を数えるだけ** | `ExpectedDistInfoCount`（wheel＋sdist＋archive）と突合し、`Files=0`・`python.exe` 不在・`._pth` 不在も失敗にする |
| 11 | **同じ話者が一覧に 2 件出る**（走査由来の幽霊 id） | 参照 wav を `voices_dir` 直下から **`voices/refs/`** へ逃がし（`.pt` を `latents/` に逃がしたのと同じ理由）、`voices.json` は `{"ref_wav":"refs/<id>.wav"}` の相対パスにした。旧い置き場の檔は `VoiceStore.MigrateReferences` が起動時に 1 度移す |
| 12 | **`voices/presets.json` を読めていない** | `presets`（**配列**）を読む路を足し、話者 id は `display_name`・檔は `secondary.file`・`status != done` は飛ばす。「台帳は在るが展開を通した印が無い」を検出して 1 度だけ入れ直す（`VoicesYwkFile.PresetsInstalled`） |
| 13 | **削除が `.pt` と sidecar を消さない** | `IWrapperClient.DropLatentAsync`（`DELETE /ywk/voices/{id}/latent`）を足し、削除を ⑴ 潜在を外す ⑵ 台帳と檔 ⑶ `voices.json` の順にした。サーバが止まっていても `VoiceStore.LatentStem`（wrapper の `latent_stem` と同じ規則）で場所が決まるので取りこぼさない。呼ばれていなかった `SetLatent` は消した |
| 14 | **失敗しても次の段へ進み完了を焼く** | 段ごとに成否を持ち（`RunXxxAsync` が bool を返す）、失敗した段では「次へ」が「やり直す」になって**進まない**。`FirstRunCompleted` は起動が成功したときだけ焼く |
| 15 | **通知文が読めなくても同意できる** | `CanAcceptNotices`（`_noticesSha256 is not null`）を同意の印と「次へ」の両方の条件にし、XAML の `IsEnabled` にも束縛した |
| 16 | **台帳に無い話者の削除が成功を名乗る** | `RemoveVoiceDetailed` が `WasKnown` を返し、`VoiceRow.IsInTable`／`CanRemove`／`KindText`（「サーバ側」）で行そのものを区別する |
| 17 | **読めない形式を謳っている** | `VoiceIds.WavExtensions` を実測で読める 5 種（`.wav`・`.mp3`・`.flac`・`.ogg`・`.opus`）に絞り、画面の文言・檔窓の絞り・検分を同じ定数から作る |
| 18 | **消したプリセットが戻らない** | 話者画面に「同梱のプリセットを入れ直す」（`PresetVoices.Restore`＝既に在る id は飛ばす）を足し、削除の確認文をプリセットとそれ以外で分けた |

### 14-2 実測（この機体・rocm-gfx1151・私設ポート 18094／18095／18096・**8088 と 7861 は無傷**）

```
dotnet build launcher/IrodoriTtsYwk.sln --nologo   ->  0 warnings / 0 errors
dotnet test  launcher/IrodoriTtsYwk.sln --nologo   ->  401 passed / 0 failed  (was 370)
pwsh -File probe/d-launch-probe.ps1 -Port 18094    ->  31 / 31 PASS  EXIT=0   (was 28 steps)
  起動 -> 待機 28.8 s（runtime loaded in 24.13s）・GPU 列挙 2.23 s
  試し撃ち（参照なし・10 歩）  所要 5.52 s／出力 1.36 s／RTF 4.06
  試し撃ち（追加した話者）      所要 4.55 s／出力 3.60 s／RTF 1.26
  停止 0 s・python 残骸 0・ポート解放
  初回取得の見積り（新）= 取得 4.79 GiB・必要な空き 9.55 GiB
                          （実行系 1.44 GiB・モデル 3.33 GiB・vc_redist 24.4 MiB）
```

**幽霊行が消えたことの実射**（上流 `VoiceRegistry.list()` を組んだ実行系で直に撃った・PY-EXIT=0）＝
話者 1 名を追加した利用者データに対し

```
  id=テスト話者      no_ref=False ref_wav=...\voices\refs\ywk-c962284a59da.wav
  id=デフォルト      no_ref=True  ref_wav=None
count = 2
```

＝**2 件**（是正前は同じ状態で 5 件＝話者 2 名それぞれが日本語名と ASCII 幹の 2 行になっていた）。

### 14-3 実機で判った 2 つ（後続が前提にしてよい）

1. **UI Automation は自プロセスの持ち窓を列挙しないことがある**。`RootElement.FindAll(Children, ProcessId)` は
   ランチャ自身の modal `FirstRunWizard`（`Owner` を持つ WPF 窓）を**1 度も返さない**のに、Win32 の
   `EnumWindows` は同じ瞬間に `title=初回取得 vis=True` を返す（実測）。§13-3 の「共通檔窓が UIA から
   見えない」と同じ盲点である。⇒ `probe/common.ps1` の `Get-ProcessWindows` を
   **UIA →（見つからなければ）Win32 の HWND 列挙＋`AutomationElement.FromHandle`** の 2 段にした
   （`Get-ProcessWindowHandles`）。便 E のインストーラ検分もこの型を使うこと。
2. **`Get-Content -Raw` に `-Encoding UTF8` を付けないと PS 5.1 で日本語が壊れる**。BOM 無しの
   `voices.json`／`settings.json` を機体の ANSI 頁で読むので、`テスト話者` の突合が
   **5.1 でだけ**落ちる（7 では通る＝檔頭の「5.1／7 両対応」に反していた）。台本の 2 箇所を直した。

### 14-4 卓へ／後続へ

1. **`build/assemble-app.ps1` が `voices/presets/*.wav` と `voices/presets.json` を写していない**
   （`grep -n "voices\|presets" build/assemble-app.ps1` の当たりは `voices.json` と `voices.ywk.json` を
   書く行だけ）。**便 A へ票**＝写す行が入るまで、配布樹からはプリセットが 0 件のままである。
   ランチャ側は「見つけた回にだけ印を立てる」ので、檔が入った版に更新した時点で 12 名が入る
   （`PresetVoices` の `PresetsInstalled`）。担当パス外なので席は触っていない。
2. **`CUDA_DEVICE_ORDER=PCI_BUS_ID` の効きは RTX 機で実射すること**（裁定 69 ⑶）。この機体は
   Radeon 単騎なので 2 台構成の index の揺れは再現できていない。状態帯の突合 1 行
   （`StatusGpuMismatchText`）が RTX 機で黙ったままなら揃っている、という読み方をする。
3. **`Contracts/IWrapperClient` の位置レコードに `[JsonPropertyName]` が無い**件（§13-7 ⑶）は
   今回も直していない（骨組み席の檔）。足した `DropLatentResult` も同じ流儀に揃えてある＝
   `SnakeCaseLower` が救っている状態なので、直すときは 4 つまとめて直すこと。
4. **ready 待ちの期限切れは子を殺すようになった**（所見 2）。モデル読込が遅い機体で
   `ReadyTimeout` を短く設定すると、載りかけの個体を毎回落とすことになる。既定は 120 s
   （CPU 300 s）のままで、短くするのは検分の台本だけにすること。

---

## 15. wrapper 席（便 D（2））の記帳（2026-09-05・opus サブ席）

担当＝`server/ywk_server.py`・`tests/contract/**`・`docs/contract.md`・`docs/radeon.md`・本節。
**`launcher/` は 1 檔も触っていない**（裁定 87 ⑶ の `[JsonPropertyName]` と ⑷ の vc_redist、
裁定 88 の門・死活・low 14 件はランチャ側の席の担当）。`build/`・`probe/`・`upstream/`・
`voices/`・`tools/` も触っていない。commit／push もしていない。

### 15-1 足した欄の逐語（裁定 87 ⑴）

`GET /ywk/status` に **`memory`** を足した。**単位はすべてバイト**・**`schema` は上げていない**
（欄を足しただけ＝契約 ⑻）。健全なときの形＝**この 10 欄だけ**（`docs/contract.md` ⑹ **6-1** に転記）：

```json
{"device":"cuda:0"|"cpu"|null,"allocated":int|null,"reserved":int|null,"max":int|null,
 "gpu_total":int|null,"gpu_free":int|null,"gpu_used":int|null,
 "latents":{"<話者 id>":int,…},"latents_total":int,"sampled_at":"<ISO 8601・UTC・末尾 Z>"}
```

- `allocated`／`reserved`／`max`＝torch の allocator（`memory_allocated`／`memory_reserved`／
  `max_memory_allocated`）＝**このプロセス**。
- `gpu_total`／`gpu_free`＝`torch.cuda.mem_get_info`（**ROCm も同じ口**）・
  `gpu_used = gpu_total − gpu_free`＝**カード全体（他プロセス込み）**。
- `latents`＝`latents/<stem>.pt` の**実サイズ**を**話者 id で引いた表**。**焼いていない話者は行ごと出ない**
  （`0` と「まだ焼いていない」を混ぜない）。`latents_total` はその合計。
- **device が `cpu` か未読込のとき数値 6 欄は `null`**（`0` ではない）。**そのときも `latents` は出る**。
- **失敗しても `/ywk/status` は 200 のまま**＝`memory` の中に **`error` が 1 行**増えるだけ
  （絶対パスは `<path>` に畳む）。`error` は**失敗したときだけ現れる**。
  allocator が取れて `mem_get_info` だけ落ちた場合は、**取れた欄は残る**。

裁定 87 ⑵＝**`device.pci_bus_id` に `str()` を掛けた**（`None` は `None` のまま）。
理由＝**ROCm の `get_device_properties(i).pci_bus_id` は `int`**（この機体で `197`）で CUDA 側は文字列＝
そのまま出すと本体・ランチャの同じ 1 欄が機体によって 2 型になる。契約 ⑹ の逐語を `string|null` に直した。

### 15-2 実射の逐語（この機体・rocm-gfx1151・**私設ポート 18097**・2026-09-05 06:59）

`build/assemble-app.ps1` で `build/out/app` を更新（**108 檔**・patches 1・**preset wav 11 檔 32,571,364 B**・
`upstream/` clean）してから、`build/out/runtime-rocm-gfx1151/python.exe -m ywk_server`
（cwd=`build/out/app`・`HF_HUB_OFFLINE=1`・`IRODORI_MODEL_DEVICE=cuda:0`・bf16・
`IRODORI_VOICES_DIR`＝`%TEMP%\ywk-bend2-w\voices` に**プリセット 1 名だけ**＝
`voices/presets/vr2_akane_west.wav` を `琴葉茜.wav` として写した 3,352,364 B）で 1 回だけ起こした。

- **ready 26.2 s**（spawn→`runtime.loaded`）。`rocm-*` は ready 直後に事前計算 `all` が自動で走る＝
  `precompute` は `{"state":"done","done":1,"total":1,"built":1,"last":{"id":"琴葉茜","frames":873,"ms":3693.9},"elapsed_s":3.687}`。
- **`device`**（逐語）：

```json
{"configured":"cuda:0","codec_configured":"cuda:0","actual":"cuda:0","precision":"bf16","name":"AMD Radeon(TM) 8060S Graphics","uuid":"30303030-3031-3937-3030-303030303030","pci_bus_id":"197","hip":"7.15.26333","gcn_arch":"gfx1151"}
```

- **`memory`**（逐語）：

```json
{"device":"cuda:0","allocated":1853884416,"reserved":2212495360,"max":4002721792,"gpu_total":107090132992,"gpu_free":104554733568,"gpu_used":2535399424,"latents":{"琴葉茜":113405},"latents_total":113405,"sampled_at":"2026-09-04T21:59:47.025Z"}
```

- **一覧**＝2 件（`デフォルト`＋`琴葉茜`・後者は `latent:true`／`latent_stale:false`）。
  焼けた実体＝`latents/ywk-0513cb458e53.pt` **113,405 B** ＋ `ywk-0513cb458e53.json` 567 B
  （**話者名が全部非 ASCII だと stem は `ywk-<sha256 12 桁>` になる**＝`latent_stem` の設計どおり）。
- **所要**＝`GET /ywk/status` を 20 回叩いて **min 1.7 ms／median 2.0 ms／max 13.5 ms**（HTTP 込み・
  `memory` を足した後の値）＝2 秒間隔の polling に耐える。
- 終了＝`taskkill /T /F` → **8088・7861・18088・18097〜18099 の LISTENING 0**・**python.exe の残骸 0**。
  **8088 と 7861 は最後まで起こしていない。**

### 15-3 実機で判った 4 つ（画面席・統合席が前提にしてよい）

1. **`gpu_total` はこの機体で 107,090,132,992 B（99.7 GiB）**＝gfx1151 は**共有メモリ**なので、
   `gpu_used／gpu_total` の帯は「VRAM の残り」ではなく**共有プールの残り**を意味する。
   離散 GPU（RTX）では素直に VRAM になる＝**同じ 1 本の帯で意味が変わる**ので、
   ランチャの表示は `gcn_arch`（Radeon なら非 null）で言い分けるか、割合ではなく実数で出すこと。
2. **`gpu_used`（2,535,399,424＝2.36 GiB）と OS の `GPU Process Memory`（`docs/radeon.md` §7-7 の
   読込直後 3.37 GiB）は一致しない**。前者はドライバの `mem_get_info`、後者は Windows の別勘定＝
   **測っている物が違う**。**片方でもう片方を否定しない**（§7-7 の注意書きと同じ）。
3. **`max > reserved` は普通に起きる**（実射で `max` 4,002,721,792 ＞ `reserved` 2,212,495,360）。
   `max` は**プロセス起動以来のピーク**・`reserved` は**現在**なので、
   ランチャは `allocated ≤ reserved ≤ max` を**不変条件にしてはいけない**。
   実射で成り立っていたのは `allocated < reserved < gpu_used` のほう。
4. **`latents` の鍵は話者 id そのもの**（日本語可）で、**焼いていない話者は行が無い**。
   ランチャは「行が無い＝未計算」と読み、`0 B` と描かないこと（裁定 67 ⑵ の
   「潜在参照＝増えない／wav 参照＝+0.7 GB 級」の出し分けはこの有無で決まる）。
   `sampled_at` は **UTC・末尾 `Z`**（この実射は JST 06:59 に対して `21:59Z`）。

### 15-4 釘（契約テスト）

`tests/contract/test_memory.py` を新設＝**22 本**（`FakeRuntime`＝**モデルは 1 度も読まない**。
GPU は `torch.cuda` の 5 つの入口を差し替えて模擬する）。内訳＝形（10 欄の逐語・cpu で null・
未読込で null・`sampled_at`・絶対パス 0 件）5 本／GPU 模擬（数値・`gpu_used` はカード全体・
index 省略）3 本／`latents`（話者 id で引ける・焼いていない話者は載せない・合計・cpu でも読める）4 本／
`pci_bus_id`（`str` になる・不明なら null）2 本／失敗（1 行で 200 のまま・残りの status は無傷・
`error` に絶対パス 0 件・読めない檔は飛ばす）5 本／速さと空リスト 3 本。

**`build/run-tests.ps1` の逐語**＝`338 passed, 2 warnings in 6.13s`・`pytest_exit 0`・
`upstream/Irodori-TTS: clean`・`upstream/Irodori-TTS-Server: clean`・
`STEP A-5: contract tests green, upstream/ still clean`＝**既存 316 本＋新規 22 本が全部緑**。

### 15-5 卓へ／後続へ

1. **`memory.error` は失敗したときだけ現れる欄**にした（裁定 87 ⑴ の逐語を健全形として守り、
   足す欄を 1 つに留めるため）。C# 側は `string? Error` で受ければ「欄が無い」も `null` になる＝
   ランチャの位置レコードは**欄を 11 個持ってよい**（裁定 87 ⑶ で `[JsonPropertyName]` を書く際、
   `memory` の 11 欄も同じ流儀に揃えること）。
2. **`build/assemble-app.ps1` のプリセット写し（裁定 88 ⑸）は既に入っていた**＝この便の実射で
   `preset wav 11 檔 32,571,364 B` を配布樹に写す行が動いているのを確認した（席は `build/` を触っていない）。
3. **CUDA 機での `memory` は未実射**（この機体は Radeon 単騎）。`mem_get_info` は CUDA でも同じ口だが、
   `gpu_total` が離散 VRAM になる（上の 1）ことの確認は便 E（RTX 機）へ。

## 18. 工具席（便 D（2））の記帳（2026-09-05・opus サブ席）

`build/` と台帳の票（裁定 88 ⑸・87 ⑷・low 10）と、便 E への申し送り（裁定 86）を処理した。
**この節も実装の逐語であって §1〜§8 の設計を覆さない**。覆した 1 行（§6 の vc_redist）は §18-4 に記す。
`launcher/`・`server/` には 1 行も触っていない。

### 18-1 触った檔（6 檔）

| 檔 | 何を |
|---|---|
| `build/assemble-app.ps1` | §4b「presets」を新設＝`voices/presets.json` と `voices/presets/*.wav` を配布樹へ写す（裁定 88 ⑸） |
| `build/verify-runtime.ps1` | 静的検査を 1 本追加＝「配布樹にプリセットが在り、`size_bytes` と `md5` が `presets.json` の記載と合う」 |
| `ledger/README.md` | §7 の「約 5.4 GiB」を台帳の実値に直し、**落とすバイト**と**要る空き**を別の欄にした（low 10） |
| `docs/design/ben-d-launcher.md` | §6 の `/passive` を `/quiet` に（裁定 87 ⑷）＋本節 |
| `docs/acceptance.md` | 「導入」行に注記 1 行（落とすバイトと要る空きは別物） |
| `probe/README.md` | 便 D 節を新設し、便 E の前提 2 行（UIA と PS 5.1 の頁）を残した（裁定 86） |

**触っていない物**＝`launcher/`・`server/`・`upstream/`・`research/`・`voices/*.wav`（読んだだけ）・`tools/`・
`probe/rocm-*`・`probe/cuda-*`・`probe/rtx-*`・`build/make-handoff.ps1`・`build/make-ledger.ps1`・
`build/assemble-runtime.ps1`・`build/Common.ps1`・`ledger/*.json`（読んだだけ）・`decisions.md`。
`build/check-tree.ps1`・`build/check-licenses.ps1` は**実走しただけで 1 行も直していない**。
git の commit／push はしていない。第三者バイナリは 1 檔も足していない。外へ 1 バイトも出していない。

### 18-2 裁定 88 ⑸＝配布樹に写したもの（実走の逐語）

```
powershell -NoProfile -ExecutionPolicy Bypass -File build\assemble-app.ps1
[06:56:10] INFO copied voices/presets.json: 12 row(s), 11 with status=done and a secondary wav, 1 skipped (decisions.md 27)
[06:56:10] INFO   preset wav co_kana_naisho.wav 3194924 bytes
[06:56:10] INFO   preset wav co_mana_isshoukenmei.wav 2895404 bytes
[06:56:10] INFO   preset wav co_ofutonp_kiza.wav 2937644 bytes
[06:56:10] INFO   preset wav co_ofutonp_normal_v2.wav 2526764 bytes
[06:56:10] INFO   preset wav co_tsukuyomi.wav 2711084 bytes
[06:56:10] INFO   preset wav vr2_akane_west.wav 3352364 bytes
[06:56:10] INFO   preset wav vr2_tsukuyomi_ai.wav 3433004 bytes
[06:56:10] INFO   preset wav vr2_tsukuyomi_shota.wav 3141164 bytes
[06:56:10] INFO   preset wav vr2_yoshida.wav 2384684 bytes
[06:56:10] INFO   preset wav vv_chibishikijii.wav 3041324 bytes
[06:56:10] INFO   preset wav vv_mochiko_sexy.wav 2953004 bytes
[06:56:10] INFO copied voices/presets/: 11 wav file(s), 32571364 bytes
[06:56:10] STEP done: 108 files under ...\build\out\app; patches applied: 1; preset wavs: 11; upstream clean: True
EXIT=0  elapsed=1.39 s
```

**11 檔・32,571,364 B（31.06 MiB）**。着地は **`build/out/app/voices/presets/`**、台帳は
**`build/out/app/voices/presets.json`**（50,955 B・原檔をそのまま写す）。
`build/out/app` の檔数は **108**（今回足したのは 12 檔＝wav 11 ＋ `presets.json` 1）。upstream は前後とも clean。

**ランチャの読む path と一致することを読んで確かめた**（`launcher/` は触らずに読むだけ）＝
`Contracts/AppPaths.PresetsJsonPath` ＝ `Path.Combine(AppDir, "voices", "presets.json")`（`AppPaths.cs:87`）、
`Contracts/AppPaths.PresetVoicesDir` ＝ `Path.Combine(AppDir, "voices", "presets")`（同 `:84`）。
`Services/Voices/PresetVoices.FromPresetsJson` は⑴ トップレベルの配列 `presets` を読み ⑵ `status != "done"` の行を飛ばし
⑶ 話者 id を `display_name`・檔名を `secondary.file` に取り ⑷ 実体を `PresetVoicesDir` に、無ければ `<app>/voices/` に探す。
**写した形はこの 4 つとも満たす**（12 行のうち `status=done` は 11・skipped 1＝裁定 27 の CeVIO 弦巻マキ）。
**申し送りは無い＝食い違いは無かった。**
これで §14-4 ⑴（「`build/assemble-app.ps1` が `voices/presets/*.wav` と `voices/presets.json` を写していない」）の票は**閉じる**。

写した檔の一覧（配布樹での檔名・バイト・`presets.json` の `display_name`）＝

| # | 檔 | バイト | 表示名（＝話者 id） |
|---|---|---|---|
| 1 | `vv_mochiko_sexy.wav` | 2,953,004 | もち子さん |
| 2 | `vv_chibishikijii.wav` | 3,041,324 | ちび式じい |
| 3 | `co_tsukuyomi.wav` | 2,711,084 | つくよみちゃん |
| 4 | `co_kana_naisho.wav` | 3,194,924 | KANA（ないしょばなし） |
| 5 | `co_mana_isshoukenmei.wav` | 2,895,404 | MANA（いっしょうけんめい） |
| 6 | `co_ofutonp_kiza.wav` | 2,937,644 | おふとんP（きざ） |
| 7 | `co_ofutonp_normal_v2.wav` | 2,526,764 | おふとんP（のーまるv2） |
| 8 | `vr2_akane_west.wav` | 3,352,364 | 琴葉茜（関西弁） |
| 9 | `vr2_yoshida.wav` | 2,384,684 | 吉田くん |
| 10 | `vr2_tsukuyomi_ai.wav` | 3,433,004 | 月読アイ |
| 11 | `vr2_tsukuyomi_shota.wav` | 3,141,164 | 月読ショウタ |
| — | （`cevio_maki_en`＝`status: skipped`・二次 wav なし） | — | 弦巻マキ（英語）＝裁定 27 |

表示名は `voices/presets.json` の `display_name` の逐語で、写す側（`assemble-app.ps1`）は
**檔名しか見ていない**（表示名を解釈するのはランチャ）。

### 18-3 足した検査（釘）＝`build/verify-runtime.ps1`

サーバを起こす**前**に走る静的検査を 1 本足した（`-HealthOnly` でも `-WithModel` でも必ず通る）。
`voices/presets.json` に **sha256 の欄は無い**（`secondary` が持つのは `md5`・`size_bytes`・`duration_s`…）ので、
**`size_bytes` と `md5` の両方**を突き合わせる。対象行の規則はランチャと同じ＝`status == "done"` かつ
`secondary.file` が空でないこと。

```
build\verify-runtime.ps1 -Variant rocm-gfx1151 -Port 18097
[06:56:37] INFO PASS  app tree carries the preset voices  presets.json rows=12 status=done=11 verified=11 (size + md5) under ...\build\out\app\voices\presets
... GET /health 200 in 52 ms / GET /params 2 ms / /ywk/status.variant=rocm-gfx1151 / GET /v1/audio/voices 200
[06:56:40] STEP all 7 checks passed        EXIT=0  elapsed=3.68 s
```

**釘が本当に効くことも撃った**（1 檔だけ改名して再走・私設ポート 18098）＝

```
[06:56:54] FAIL FAIL  app tree carries the preset voices  presets.json rows=12 status=done=11 verified=10 (size + md5) ...; bad: vv_mochiko_sexy.wav: not in the app tree
[06:56:56] FAIL 1 check(s) failed        EXIT=1
```

改名は直後に戻した（配布樹は 11 檔・32,571,364 B に復した）。
**検査の本数が 1 本増える**＝裁定 63 の「rocm 17 checks・cpu 15 checks」は `-WithModel` 込みの数なので
**rocm 18・cpu 16** になる。`-WithModel` なしのこの走行は 7 checks（うち 1 本が新設）。

### 18-4 §6 の 1 行の訂正（裁定 87 ⑷）

§6 の `vc_redist` の行を `/install /passive /norestart` → **`/install /quiet /norestart`** に直した。
根拠＝`ledger/vc_redist.json` の `silent_args`（逐語で `["/install","/quiet","/norestart"]`）。
実装側は既に台帳を正本にしている（`Services/Ledger/VcRedistInstaller.cs:250` が `item.SilentArgs` を使う）ので、
**直したのは設計書の側だけ**で挙動は変わらない。同檔の doc コメント（`:172`）が「設計書は `/passive`＝食い違っている」と
書いているが、`launcher/` は担当外なので触っていない＝**その 1 行は今や古い**（申し送り）。

### 18-5 low 10＝`ledger/README.md` §7 の数字の根拠

**足し直した値**（`ledger/*.json` の `items[].size` と `models.json` の `repos[].total_bytes` を素直に合計）＝

```
python-embed  1 item     11,133,606 B
vc_redist     1 item     25,635,768 B
models        22 files 3,570,982,039 B  (Irodori-TTS-v4.1-Small 3,071,026,631 / Semantic-DACVAE 429,626,712 / silentcipher 70,328,696)
runtime-cpu          101 items   283,249,109 B
runtime-cu130        101 items 2,038,518,046 B
runtime-cu126        101 items 2,760,786,268 B
runtime-rocm-gfx1151 107 items 1,536,696,364 B
```

| 変種 | 落とすバイト | GiB | 要る空き（`FetchPlan.EstimatedPeakDiskBytes`） |
|---|---|---|---|
| `cpu` | 3,891,000,522 B | 3.62 GiB | 4.53 GiB |
| `cu130` | **5,646,269,459 B** | **5.26 GiB** | **11.56 GiB** |
| `cu126` | 6,368,537,681 B | 5.93 GiB | 14.45 GiB |
| `rocm-gfx1151` | 5,144,447,777 B | 4.79 GiB | 9.55 GiB |

「要る空き」は `CacheBytes ＋ (python-embed＋runtime) × 3.3 ＋ ModelBytes`
（`Services/Ledger/FetchPlanner.cs` の `ExpansionFactor = 3.3`）。この式で出した `cpu` 4.53 GiB・
`rocm-gfx1151` 9.55 GiB は §13-6・§14-2 の実走の 1 行と**桁も小数 2 桁も一致する**＝式の写し違いは無い。
**「約 5.4 GiB」は調査便の見積の丸め**であって台帳の実値ではない＝cu130 は 5.26 GiB。
`docs/acceptance.md` の「導入」行にも注記 1 行を足した（サイズ行の「導入後 ≤ 7.0 GB」は**展開後**の話で、
この 2 つのどちらとも別の量である＝混ぜないこと）。

### 18-6 `check-tree` と `check-licenses` の実走（両方 EXIT=0）

```
build\check-tree.ps1
tracked files: 394 / untracked, not ignored: 3
PASS  no tracked file has a binary/model/wheel extension
PASS  no tracked file is larger than 4096 KB
PASS  no untracked file has a binary/model/wheel extension either
PASS  build/out, build/cache and .venv-dev are neither tracked nor committable
PASS  upstream/Irodori-TTS @ 8224dafb46d0aba89209a8f905f1cb7e3299d9c1
PASS  upstream/Irodori-TTS-Server @ 841fb7c6ec57729c56b9b75c0ef2562249b13a10
STEP  A-7 PASSED        EXIT=0  elapsed=1.60 s   （warnings 0）

build\check-licenses.ps1
files under licenses/: 13 / rows in the index table: 13
PASS  the index table and the files match 1:1
PASS  licenses/irodori-tts/LICENSE matches upstream\Irodori-TTS\LICENSE (newline-insensitive)
PASS  licenses/irodori-tts-server/LICENSE matches upstream\Irodori-TTS-Server\LICENSE (newline-insensitive)
INFO  preset voices: 11 wav, 11 rows in voices/presets.json
WARN  voices/presets.json: rights_source_fetched is false for 11 voice(s) -- licenses/README.md section 6-2 must be filled in before a Release
STEP  A-6 PASSED: 13 license files, all indexed and non-empty        EXIT=0  elapsed=0.70 s
```

`check-licenses` の WARN 1 件は**この便より前からある物**（裁定 18＝規約原文は席が未取得・司令官の確認のみ）で、
プリセットを配布樹に写したこととは無関係＝**Release の前に `licenses/README.md` §6-2 を埋める**という既知の宿題。
`check-licenses` は既に `voices/presets.json` の 11 行と `voices/presets/` の 11 檔を数えていた（**リポ側**を見る）。
今回足した検査はそこではなく**配布樹側**（`build/out/app/voices/presets/`）を見る＝重なっていない。

### 18-7 卓へ／後続へ

1. **`build/check-tree.ps1` の `$LargeAllowPrefix` は空のまま**＝`voices/presets/*.wav`（1 檔 2.3〜3.3 MB）は
   既定の 4,096 KB を**超えないので警告にならない**（実走で warnings 0）。閾値を下げる日が来たら
   `voices/` を許可接頭辞に足すこと（`.wav` は `$BannedExt` に入っていない＝裁定 37 のとおり自作の生成物）。
2. **`VcRedistInstaller.cs:172` の doc コメントが古くなった**（§18-4）。`launcher/` を触る次の席が
   「設計書は `/passive`」の 2 行を消すこと。挙動は変わらない。
3. **裁定 63 の検査本数の逐語が 1 増える**（§18-3）。`docs/` に本数を書いている箇所は無いので直しは不要だが、
   `decisions.md` 63 の「rocm 17 checks・cpu 15 checks」は `-WithModel` 込みで **18／16** になる。
4. **プリセットの `md5` は `voices/presets.json` の逐語**（`secondary.md5`）で、リポの `voices/presets/` の
   実檔と 11/11 一致することを別途 python で確かめた（席の手計算ではない）。`sha256` の欄を
   `presets.json` に足すなら便 P の担当＝足された日に `verify-runtime.ps1` の突合を sha256 に寄せてよい。

## 17. 画面席（便 D（2））の記帳（2026-09-05・opus サブ席）

裁定 87 ⑴・67 ⑴⑵⑶・88 ⑵⑶ と low 3・11・12・13・14 を画面側で処理した。
**この節も実装の逐語であって §1〜§8 の設計を覆さない。**触ったのは
`ViewModels/`・`Views/`・`Contracts/WrapperResponses.cs`（`MemoryStatus` だけ）・
`IrodoriTtsYwk.Launcher.Tests/`・`launcher/README.md` §9 の 5 つで、
**`Services/` と `server/` と `build/` には 1 行も触っていない**（起動席 LA と同時進行だったため、
`IServerProcess.LatestStatus`／`StatusSampled`・`ServerStartResult.Notices` は
**公開されるのを待ってから**繋いだ＝仮実装は 1 つも足していない）。

### 17-1 直した物（担当パスの中だけ）

| # | 何 | どこ |
|---|---|---|
| ⑴ | `MemoryStatus` を裁定 87 ⑴ の **11 欄**に合わせた（`device`・`allocated`・`reserved`・`max`・`gpu_total`・`gpu_free`・`gpu_used`・`latents`・`latents_total`・`sampled_at`・`error`）。全欄に `[JsonPropertyName]`・数値はすべて null 許容（欠けを 0 と読まない）。派生は `IsCpu`・`HasNumbers`・`EffectiveGpuUsed`（`total − free` から起こす）・`EffectiveLatentsTotal`・`LatentBytesFor(id)` | `Contracts/WrapperResponses.cs` |
| ⑵ | 状態帯の GPU メモリを**使用量＝allocated・占有量＝reserved・GPU 全体＝gpu_used／gpu_total**に（裁定 67 ⑶）。`null` は `—`・`cpu` は「CPU（GPU メモリなし）」・欄ごと無ければ「未対応」・`error` は 1 行添える | `StatusViewModel.DescribeMemory` |
| ⑶ | 概算の主役を**1 名あたり**にした（裁定 67 ⑵・low 13）。焼いてあれば `memory.latents[id]` の**実サイズ**、無ければ係数＋「実測前の概算」。全員分は「（全員分＝全部を同時に載せたときの上限 …）」と明記して括弧に落とした | `StatusViewModel.DescribeVoiceMemory`・`MemoryEstimate`・`VoiceRow.MemoryText` |
| ⑷ | 状態帯に**潜在キャッシュ ON／OFF（焼いた話者 n 名・合計 m MB）**を新設（裁定 67 ⑴）。ON／OFF は走行中の個体の実効値（`RunningSettings.PrecomputeOnStart`）・件数と合計は `memory.latents` が正本 | `StatusViewModel.DescribeLatentCache`・`StatusView.xaml` |
| ⑸ | 窓の `DispatcherTimer` を落とし、`IServerProcess.LatestStatus`／`StatusSampled` を読むだけにした（low 3）。`Warming` の出入りを窓が書いていた 2 箇所を消し、**状態の書き手は状態機械 1 本**にした | `MainWindow.xaml.cs`・`MainViewModel.ApplyStatusSample` |
| ⑹ | 合成中にサーバの子が消えたら **HTTP の期限を待たずに**「サーバが落ちました（exit −1073741819）」＋理由 1 行（裁定 88 ⑶）。走っている射は `CancellationToken` で切る | `TryViewModel.NotifyServerFailed`／`DescribeServerDown` |
| ⑺ | 門の告知（`ServerStartResult.Notices`）と断られた理由 1 行を**そのまま**状態帯に出す（裁定 88 ⑴⑵・`StatusNoticesText`）。理由が `StatusReasonText` に出ていれば二重に出さない | `StatusViewModel.ApplyStartOutcome` |
| ⑻ | `CanPreview` は **`.wav` のときだけ**真（low 11）。押せない理由 1 行は `HelpText`・`ToolTip`・表示の 3 箇所に同じ文言 | `VoiceRow.PreviewBlockedReason`・`VoicesView.xaml` |
| ⑼ | `/ywk/voices` が Ok でなければ `_live=null` にして「サーバの話者一覧が読めませんでした（HTTP nnn）。台帳だけで一覧を出しています。」（low 12）＝古い写しを黙って見せない | `VoicesViewModel.RefreshAsync`／`DescribeVoicesFailure` |
| ⑽ | 列見出しを**「表示名（＝話者 id）」と「参照 wav の檔名」**に（low 14・`HeaderTemplate` の `ToolTip` で見出しの既定の見た目を落とさない）。削除は `Command="{Binding RemoveCommand}"` に寄せ、確認窓は View が `VoicesViewModel.ConfirmRemove` に差す。押せない理由は `VoicesRemoveBlockedText` と `HelpText` から UIA で読める | `VoicesView.xaml(.cs)`・`VoiceRow.RemoveBlockedReason` |

### 17-2 表示の逐語（実窓・この機体・rocm-gfx1151・私設ポート 18098）

発行なしの Debug exe を私設データディレクトリで 1 回起こし、`UIAutomationClient` で読んだ
（**8088・7861・18088 は起こしていない**・終わりにツリー kill・python 残骸 0・私設ポートの listen 0）。

```
[ready] ok=True text=待機 seconds=26.53
[band] StatusStateText       = 待機
[band] StatusDeviceText      = cuda:0／AMD Radeon(TM) 8060S Graphics（gfx1151）／bf16
[band] StatusMemoryText      = 使用量 1.73 GB／占有量 2.07 GB／GPU 全体 2.33 GB / 99.74 GB（最大 3.01 GB）
[band] StatusLatentCacheText = ON（焼いた話者 0 名・合計 0 B）
[band] StatusVoiceMemoryText = 1 名あたり「デフォルト」＝参照なし（増えません）（全員分＝全部を同時に載せたときの上限 7.70 GB）
[band] StatusPrecomputeText  = 完了（0 / 0 件）
[band] StatusWarmupText      = 未実施
[band] StatusNoticesText     =            （告知なし＝空）
[voices] VoicesSelectedMemoryText = 1 名あたり＝参照なし（増えません）
[voices] VoicesPreviewBlockedText = 「デフォルト」は参照なしの話者なので試聴する音がありません。
[voices] VoicesRemoveBlockedText  = 「デフォルト」は一覧に常在するので消せません。
[voices] row = VoiceRow { Id = 琴葉茜（関西弁）, FileName = vr2_akane_west.wav, KindText = プリセット,
                          LatentText = 未, MemoryText = wav 参照（概算 +716.8 MB・実測前の概算） }
```

**`使用量 < 占有量 < GPU 全体` の 3 段が実物で成り立っている**（1.73 / 2.07 / 2.33 GB）＝
allocator の値とカード全体の値を混ぜていないことの実射である。`GPU 全体` の分母 99.74 GB は
この機体の共有メモリ（裁定 62 の「共有 107 GB」＝`mem_get_info` が返す値）。

### 17-3 釘（`IrodoriTtsYwk.Launcher.Tests/RoundTwoUiTests.cs`・新規 31 本）

| 群 | 本数 | 何を押さえるか |
|---|---|---|
| `RoundTwoMemoryStatusTests` | 4 | 裁定 87 ⑴ の逐語 JSON が 11 欄とも読める／`gpu_used` 欠けを `total − free` で起こす／`latents_total` 欠けを表から足す／cpu と未読込は数値が null で `latents` だけ |
| `RoundTwoStatusBandTests` | 16 | 使用量→占有量→GPU 全体の順／`null` は `—`（`0 B` を出さない）／cpu の 1 行／欄ごと無ければ「未対応」／`error` の添え方／潜在キャッシュ ON・OFF と件数・合計／設定の ON/OFF が帯に出る／1 名あたりが主役で全員分は括弧／焼いた話者は実サイズ／選び替えで主役が変わる／**status 標本は状態を動かさない**（low 3）／門の告知と理由の畳み方／停止で帯が落ちる |
| `RoundTwoVoicesTests` | 8 | 試聴は wav だけ（mp3 の理由 1 行）／押せない試聴は理由を残す／一覧が読めなければ台帳だけ（HTTP 500 の逐語）／削除の可否は Command が決め理由が読める／確認を断れば 1 檔も消えない／`latents` が行に載る／同じ表なら一覧を組み直さない |
| `RoundTwoTryTests` | 3 | 落ちた理由は終了コードつき 1 行／**合成中の消失は期限を待たずに告げる**（取消で切る）／走っていなくても事実は残る |

既存檔で直した釘は 1 本だけ＝`ViewModelsTests.概算メモリはwav参照の数で決まる`
（主役が「1 名あたり」に変わったので、上限の値を見るように改めた）。

### 17-4 卓へ／後続へ

1. **プリセット 11 名が「焼いた話者 0 名」のまま起動する**（裁定 78 ⑴ の申し送りが未実装）。
   実射＝新しいデータ置き場で起こすと `StatusPrecomputeText = 完了（0 / 0 件）`・一覧は 11 名とも
   `LatentText = 未`。wrapper の起動時 all は **ready 時点の `voices.json`** を見るので、
   プリセットの初回展開分は誰も焼かない。直すなら「ランチャが起動後に明示で
   `POST /ywk/voices/precompute {"all":true}` を 1 度叩く」＝**画面席は入れなかった**。
   理由＝新規データ置き場での起動が毎回 11 名 × 11〜16 s（裁定 76）の焼きに入り、
   `probe/d-launch-probe.ps1` の D-3／D-6 が `暖機中` を跨ぐことになるため、
   統合席・検分席と歩調を合わせるべき変更だと判断した。**当面は「参照潜在を焼く」を押せば焼ける**
   （押した後の帯は `ON（焼いた話者 n 名・合計 m MB）` に変わる）。
2. **`memory.latents` の鍵は話者 id そのもの**（実射＝`{"琴葉茜（関西弁）": …}` を期待する形）。
   一覧の行は id で引いている＝wrapper が stem（sha256 12 桁）を鍵にする日が来たら、
   `MemoryStatus.LatentBytesFor` の 1 箇所を直せば画面は追随する。
3. **CUDA 機での帯は未実射**（この機体は Radeon 単騎）。`gpu_total` が離散 VRAM になる形の目視は
   便 E（RTX 機）で。`MemoryEstimate` の係数 3 つ（wav 0.70 GB・潜在 0・出力 1 フレーム 3.5 MiB）は
   **まだ `IsProvisional=true`** ＝画面は「実測前の概算」と名乗り続ける。焼いた話者だけは
   実サイズに置き換わるので、係数が効くのは wav 参照の行だけになった。
4. **窓は `/ywk/status` を叩かなくなった**（low 3）＝ログ末尾 20 行に混ざる access log は
   見張り 1 本分になる。`ServerLogParser.IsLauncherPollNoise` はそのまま残してある
   （見張りの GET も 200 で落とす対象なので、消すと §13-2 ⑶ の穴が開く）。
## 16. 起動席（便 D（2））の記帳（2026-09-05・opus サブ席）

`Services/Gpu/`・`Services/Server/`・`Services/Ledger/`・`Contracts/`（`WrapperResponses.cs` を除く）に
**変種の門**（裁定 88 ⑴⑵）・**死活**（裁定 88 ⑶）・**公開する口**・low 1〜9 が入った。
**この節も実装の逐語であって §1〜§8 の設計を覆さない。**

### 16-1 公開した口（逐語＝画面席・統合席はこの綴りで書いてよい）

| 檔 | 口 | 形 |
|---|---|---|
| `Contracts/IServerProcess.cs` | `ServerStartResult.Notices` | `IReadOnlyList<string>`・**既定は空で null にならない**（位置引数ではなく `init` の欄＝1 巡目の呼び手を壊さない）。「起こしたが伝える」1 行の列＝いまの住人は「未実測の帯」1 種 |
| 同 | `IServerProcess.LatestStatus` | `StatusResponse?`・**見張り 1 本が採った最新の `/ywk/status`**（1 度も読めていなければ null） |
| 同 | `IServerProcess.StatusSampled` | `event EventHandler<StatusResponse>?`・標本を読むたび。**UI スレッドではない**＝窓は `Dispatcher` へ渡してから描く |
| 同 | `ServerStartRequest.Variant`／`.DriverVersion`／`.InstalledVariants` | 門の入力（`init` の欄・既定は空）。`Variant` が空なら env の `YWK_VARIANT` で代用する |
| `Services/Gpu/VariantGate.cs` | `VariantGate.Decide(variant, probe, driverVersion, installedVariants)` | **純関数** → `GateDecision(Allow, Reason, SuggestedVariant, Notices)` |
| 同 | `VariantGate.IsUnmeasuredBand(variant, driverVersion)`／`.DetectInstalled(ledgerDir)`／`.ProbeTimeout` | 未実測の帯の判定／台帳の在否で「組んである変種」を数える／門の検分の期限（15 s） |
| `Services/Gpu/TorchProbeRunner.cs` | `ITorchProbe.ProbeAsync(pythonExe, timeout, ct)` | 台本を撃つ手を 1 本に寄せた（列挙と門が同じ物を使う。台本の一時檔は**毎回別名**＝同時に走っても掴み合わない） |
| `Services/Gpu/TorchGpuProbe.cs` | `TorchProbeResult.Available`／`.DeviceCount`／`.Observed`／`.GpuUsable` | 台本が `is_available` と `device_count` を**別々に**載せるようになった。`available` を載せない旧い出力は台数から読む |
| `Contracts/IGpuEnumerator.cs` | `DriverRequirement.Cu130Minimum="580.00"`／`Cu126Minimum="528.33"`／`Cu126MeasuredMinimum="537.58"` | 裁定 88 ⑵（560.76 から改めた）。**閾の定義箇所はここ 1 つ** |
| `Contracts/IRuntimeInstaller.cs` | `InstallResult.DataResidue` | 畳めなかった `<name>-<ver>.data`（`init` の欄・既定は空） |
| `Services/Ledger/VcRedistInstaller.cs` | `VcRedistResult.NeedsUserDecision`・`VcRedistInstaller.StateProbe` | 「確認が要る」の印と、System32 の観測を差す継ぎ目 |

**`ViewModels/`・`Views/` は 1 行も触っていない**（画面席が同時に編集中）。`Contracts/WrapperResponses.cs`・
`server/`・`upstream/`・`research/`・`voices/`・`tools/`・`probe/rocm-*`・`probe/cuda-*`・`probe/rtx-*`・
`build/make-handoff.ps1` も触っていない。git commit／push はしていない。

### 16-2 門の判断表（裁定 88 ⑴⑵・`VariantGate.Decide` の全枝）

判断の順は **⑴ cpu なら素通し → ⑵ 検分（`is_available`／`device_count`）→ ⑶ ドライバの閾 → ⑷ 注意**。
**閾より検分を先に見る**＝実際に撃った観測のほうが表の値より強い（裁定 80 の 537.58 では cu126 が
通り、同じ機体で cu130 は数えられない＝表の閾だけでは 2 つを区別できない）。

| # | 変種 | 検分 | driver_version | 結末 | 勧め | 理由・注意（逐語） |
|---|---|---|---|---|---|---|
| 1 | `cu130` | `is_available=False`・`count=0` | 読めない（AMD 機） | **起こさない** | `rocm-gfx1151` | `cu130 はこの機体で GPU を見られません（is_available=False・device_count=0・torch cuda 13.0）。rocm-gfx1151 か cpu の変種に切り替えてください。` |
| 2 | `cu130` | `is_available=False`・`count=0` | `537.58` | **起こさない** | `cu126` | `cu130 はこの機体で GPU を見られません（is_available=False・device_count=0・ドライバ 537.58・torch cuda 13.0）。cu126 か cpu の変種に切り替えてください。` |
| 3 | `cu126` | 使える（1 台） | `537.58` | 起こす | — | 注意なし（裁定 80＝実射で通した版そのもの） |
| 4 | `cu126` | 使える（1 台） | `530` | 起こす | — | 注意 1 行＝`ドライバ 530 は未実測の帯です（cu126 は 537.58 まで実射で確かめてあり、下限の 528.33 は NVIDIA の表からの値で未実測）。合成はできますが、落ちるようなら cpu の変種を選んでください。` |
| 5 | `cu130` | 使える（1 台） | `591.86` | 起こす | — | 注意なし（便 B の本測定の機体） |
| 6 | `cu126` | `is_available=False`・`count=0` | 読めない（NVIDIA 無し） | **起こさない** | `cpu` | `cu126 はこの機体で GPU を見られません（…）。cpu の変種に切り替えてください。` |
| 7 | `cpu` | 何であれ | 何であれ | 起こす | — | **門を通さない**（見る GPU が無い） |
| 8 | `cu126` | 使える（1 台） | `520.00` | **起こさない** | `cpu` | `ドライバ 520.00 は cu126 の下限 528.33 に届きません。cpu の変種に切り替えてください。` |
| 9 | `rocm-gfx1151` | 撃てなかった（`probe=null`） | 読めない | 起こす | — | 注意 1 行＝`rocm-gfx1151 の GPU 検分ができませんでした（実行系が見つかりません）。そのまま起こしますが、合成が落ちるようなら cpu の変種を選んでください。` |
| 10 | `rocm-gfx1151` | `error`（torch が落ちた） | 読めない | 起こす | — | 注意 1 行に `ImportError: …` をそのまま添える |
| 11 | `rocm-gfx1151` | `is_available=False` | 読めない | **起こさない** | `cpu` | 同じ物は勧めない（`rocm` が自分で落ちたら `rocm` を勧めない） |

**勧め方**＝⑴ `driver_version` が読めれば、組んである GPU 変種のうち閾を満たす物を cu130 → cu126 の順
⑵ 読めなければ NVIDIA が居ない機体＝`rocm-gfx1151` が組んであればそれ ⑶ どれも無ければ `cpu`。
**いま落ちた変種は勧めない**。畳んだ名 `cuda` で落ちたときは cu130／cu126 の**どちらも勧めない**
（どちらが落ちたのか分からないため＝呼ぶ側が `Variant` に台帳の綴りを載せれば分かる＝16-5 ⑴）。

**cu130 の CPU 転落は禁止**（裁定 83）＝門で止める。`/health` が 200 でも最初の合成で
`0xC0000005` で消える形は、ランチャからは「起動成功→合成で沈黙」にしか見えない。

### 16-3 実測（この機体・Radeon gfx1151・**サーバも GPU も起こさず**・停止域に触れず）

**門の検分（実行系を実際に撃った）**＝`build/out/runtime-*/python.exe` に台本を渡しただけ
（`HF_HUB_OFFLINE=1`・`CUDA_DEVICE_ORDER=PCI_BUS_ID`・ポートは 1 つも開けていない）。

```
runtime-rocm-gfx1151  EXIT=0  1,738 ms
  {"ok":true,"torch":"2.13.0+rocm10.0.0","cuda":null,"hip":"7.15.26333",
   "available":true,"count":1,"devices":[{"index":0,"name":"AMD Radeon(TM) 8060S Graphics",
   "uuid":"30303030-3031-3937-3030-303030303030","total_memory":107090132992,
   "pci_bus_id":"197","gcn_arch":"gfx1151"}],"error":null}
runtime-cpu           EXIT=0  1,645 ms
  {"ok":true,"torch":"2.10.0+cpu","cuda":null,"hip":null,
   "available":false,"count":0,"devices":[],"error":null}
```

⇒ **門が起動に足す代金はこの機体で 1.6〜1.7 s**（§11-3 の列挙 2.89〜3.47 s より速い＝台本は同じだが、
こちらは `nvidia-smi` を先に試す 2 段を通らない）。起動全体が 27〜29 s なので受け入れ条件 D-6
（≤ 120 s）にも D-1（≤ 15 s）にも余裕がある。
**`runtime-cpu` の行が「GPU 変種なら門が止める形」の実物**＝`available=false`・`count=0`。
`cpu` 変種そのものは門を通さないので、この観測で止まることはない。

**死活（偽の子プロセス＝`cmd.exe`・`ping` の宛先は `127.0.0.1`）**

| 段 | 値 |
|---|---|
| 待機の後に子が消えてから `StateChanged(Failed)` が届くまで | **4 ms**（見張りの間隔をわざと **30,000 ms** にした走行＝標本では気づけない） |
| そのときの理由 1 行 | `モデルの読み込みに失敗した。 ywk_server: the runtime went away`（`ServerExitCodes.Describe(3)` ＋ stderr の末尾 1 行） |
| 即死（exit 2）の結末 | `Ok=false`・`ExitCode=2`・`起動前の検査で止まった（設定を見直してください）。 ywk_server: IRODORI_MODEL_DEVICE=cuda:9 is out of range` |
| ready 待ちの取消（low 1） | **29 ms** で `Ok=false`・`State=Stopped`・`起動を中止しました。`・**孤児 0**（pid は片付いた） |
| 自分で止めた個体の断末魔 | `Stopped` のまま（0.7 s 待っても `Failed` に覆らない） |

**検分**＝`dotnet build launcher/IrodoriTtsYwk.sln --nologo` → **0 warnings / 0 errors**（1.21 s）。
`dotnet test launcher/IrodoriTtsYwk.sln --nologo` → **467 passed / 0 failed**（3 s・§14-2 の 401 から
+66・うち起動席の新規 `RoundTwoServicesTests.cs` が 30 本）。
**8088・7861・18088 は 1 度も起こしていない**（`Get-NetTCPConnection` で 0 件・python 残骸 0・
テストの偽の子は 2 秒で自然死し、走行後の数は 0）。

### 16-4 直した low 1〜9（起動席の担当分）

| # | 何が壊れていたか | 直し方（釘の題目） |
|---|---|---|
| 1 | ready 待ちの取消が **`OperationCanceledException` を投げ直していた**（契約の「例外は投げず結末で返す」に反する） | ツリー kill → `ApplyStopped` → `Ok=false`・`起動を中止しました。` で返す（`low1_取消は子を落として起動を中止しましたで返る`） |
| 2 | 即死の理由を **`WaitForExit(500)`** の後に組んでいた＝非同期の読みの完了を待たないので stderr の最終行を取りこぼす | 別スレッドで**引数なし** `WaitForExit()` を待ち、`StderrDrainGrace`（500 ms）で打ち切る。註も書き直した（`死活_即死はstderrを読み切ってから理由を組む`） |
| 3 | 窓が `DispatcherTimer` で `/ywk/status` を別に叩いていた | `LatestStatus`／`StatusSampled` を公開して**見張り 1 本**に寄せた（口は 16-1・窓側の撤去は画面席 §17 が実施） |
| 4 | tar の檔名を **`TrimStart('.', '/')`** していた＝⒜ `.gitignore` が `gitignore` になる ⒝ `../evil` が `evil` に均されて zip slip の検出を素通りする | `TrimStart('/')` だけにし、外を指す名は `ResolveInside` に投げさせる（`low4_点で始まる檔名は名を保つ`・`low4_展開先の外を指すtarは投げる`） |
| 5 | 206 を受けたら **`Content-Range` を見ずに** `FileMode.Append` で書き足していた＝範囲を丸めた CDN の 206 で `.part` に穴／重なりができる | `Content-Range.From == offset` を確かめ、違えば `.part` を捨てて 0 から（`low5_206の位置が頼んだ所と違えば捨てて0から取り直す`） |
| 6 | `sha256` の無い item は **cache hit の判定で長さも見ていなかった**＝途中で切れた檔がそのまま展開へ渡る | ⒜ cache hit で `ExpectedSize` を突き合わせる ⒝ `FetchPlanner.Plan` が sha256 の無い item を計画の段で弾く（`LedgerReader.Validate` は既に弾いていた＝釘を足した） |
| 7 | `VcRedistAction.Unknown`（System32 が読めなかった）を **`Install` に落として** UAC を出していた | 「確認が要る」結果（`Ok=false`・`NeedsUserDecision=true`）で返し、**1 バイトも落とさず窓も出さない**（裁定 87 ⑷）。引数が台帳の `silent_args` であることにも釘を打った |
| 8 | 組み上げが **変種ディレクトリを消してから** item ごとに sha256 を検めていた＝101 件目で cache が欠けると動いていた実行系が丸ごと失われる | 消す前に**全 item を一巡**して cache の在否と sha256 を確かめる（`low8_原檔が欠けていたら既存の実行系を消さない`） |
| 9 | 畳めなかった `<name>-<ver>.data` が **黙って残っていた** | `InstallResult.DataResidue` に載せて **Fail**（`low9_畳めなかったdataの残骸は理由に載せて落とす`） |

**裁定 87 ⑶**＝`Contracts/IWrapperClient.cs` の位置レコード 4 つ（`WarmupStartResult`・
`PrecomputeStartResult`・`CancelResult`・`DropLatentResult`）に `[property: JsonPropertyName]` を書いた。
釘は**命名方針を外した素の `JsonSerializerOptions`** で読む（`契約_位置レコード4つは綴りを自分で名乗る`）＝
`SnakeCaseLower` が救っている状態を頼りにしない。

**既存の釘のうち 5 本を書き換えた**（閾と台帳の前提が変わったため）＝
`ContractsTests.cu126の下限は560_76` → `cu126の下限は528_33`、
`GpuEnumerationTests.ドライバの閾は変種ごとに決まる` の cu126 の 3 行（528.33／528.32／537.58）、
`ViewModelsTests` の初回取得の合成台帳 4 箇所に `sha256` を足した（sha256 の無い台帳は計画の段で
弾くようになったため＝low 6）。**それ以外の既存檔には触っていない。**

### 16-5 卓へ／後続へ

1. **`ViewModels/MainViewModel.StartServerAsync` は `ServerStartRequest` を直に組んでいる**ので、
   いまの門は env の `YWK_VARIANT`（`cuda`／`cpu`／`rocm-gfx1151`）で変種を代用している。
   Radeon 版は綴りが一致するので精度は落ちないが、**CUDA 版では cu130 と cu126 が畳まれる**＝
   門は止められるが「どちらを勧めるか」が `cpu` に落ちる。**画面席・統合席へ**＝
   `Variant = _settings.Variant`・`DriverVersion = GpuEnumerator.DriverVersionOf(gpus.Gpus)`・
   `InstalledVariants = VariantGate.DetectInstalled(_paths.LedgerDir)` を要求に載せる（3 行）か、
   `ServerLaunchPlan.Build(..., driverVersion, installedVariants)` を通すこと。
   **RTX 機（便 E）で門の 2・5 行目を実射するときの前提**でもある。
2. **`528.33` は未実測**（NVIDIA の CUDA 12.x minor version compatibility の Windows 下限＝表の値）。
   実射で確かめてあるのは **537.58 だけ**（裁定 80）。この 2 つの間は「未実測の帯」として合成を許し
   1 行だけ告げる。閾を動かすなら `DriverRequirement` の 3 定数の 1 箇所で足りる。
3. **設計書 §6 の `vc_redist` の引数 `/install /passive /norestart` は台帳と食い違う**
   （`ledger/vc_redist.json` は `/install /quiet /norestart`＝裁定 87 ⑷ で台帳が正と決着）。
   実装は台帳を読んでおり釘も打ったが、**§6 の本文は席の担当外なので直していない**＝
   設計席が 1 語直すこと。
4. **門は起動のたびに torch を 1 回 import する**（この機体で 1.6〜1.7 s）。
   `probe/d-launch-probe.ps1` の D-1（範囲外 GPU＝§13-4 で 1.65〜1.8 s）は**その分だけ延びる**が、
   受け入れ条件の ≤ 15 s には収まる見込みである（検分席は実測すること）。短くしたいなら
   `ITorchProbe` を差し替えて列挙の結果を使い回す路が在る（口は開けてある）。
5. **`ProcessStartInfo` を作る手が継ぎ目になった**（`ServerProcess` の 6 番目の引数）。
   死活の釘が偽の子プロセスを起こすためだけの物で、実機では既定の `BuildStartInfo` が使われる。
   便 E の検分でここに何かを差す必要は無い。
6. **`TorchGpuProbe.ScriptFileName` は固定名のまま残してある**が、実際に書き出す檔は
   `ywk_gpu_probe-<8 桁>.py` になった（列挙と門が同時に走りうるため）。
   `%TEMP%` に旧い固定名の檔が残っている機体があれば、それは 1 巡目の残骸である（無害・2 KB）。

## 19. 統合席（便 D（2））の記帳（2026-09-05・opus サブ席）

4 席（wrapper W・起動 LA・画面 LB・工具 T）の解を 1 つに合わせ、**この機体で実際に窓を動かして**
裁定 87・88 を撃った。**この節も実装の逐語であって §1〜§8 の設計を覆さない。**
触った檔＝`launcher/IrodoriTtsYwk.Launcher/ViewModels/MainViewModel.cs`・同
`ViewModels/VoicesViewModel.cs`・同 `Services/LauncherComposition.cs`・
`launcher/IrodoriTtsYwk.Launcher.Tests/RoundTwoIntegrationTests.cs`（新設）・
`probe/d-launch-probe.ps1`・`launcher/README.md` §10・本節。

### 19-1 割れていた 3 つ（**机上の 467 本は全部緑だった**）

4 席の解はどれも単体では正しく、割れていたのは**席と席の間**である。

| # | 何が起きていたか | 直し方（釘） |
|---|---|---|
| 1 | **窓が門に入力を渡していなかった**（起動席 §16-5 ⑴ の申し送り）。`MainViewModel.StartServerAsync` が `ServerStartRequest` を直に組んでいたので `Variant`・`DriverVersion`・`InstalledVariants` が空のまま＝門は env の `YWK_VARIANT`（cu130 と cu126 が `cuda` に畳まれた名）で代用し、**断ることはできても勧める先が `cpu` に落ちる**（この機体なら `rocm-gfx1151` を勧めるべき場面で） | 組み立てを `ServerLaunchPlan.Build`（起動席が用意した 1 箇所）に寄せ、`DriverVersion = GpuEnumerator.DriverVersionOf(gpus.Gpus)`・`InstalledVariants = VariantGate.DetectInstalled(_paths.LedgerDir)` を載せた。釘＝`窓の起動要求は門の入力3つを載せる`・`載せた変種のおかげで門はcu130を断ってcu126を勧められる`・`この機体の形ならcu130を断ってrocmを勧める` |
| 2 | **写したプリセットが上流の別名表に 1 行も入らなかった**。`PresetVoices.InstallIfFirstRun` が書くのはランチャの台帳（`voices.ywk.json`）だけで、`voices.json` を書く路は「旧い置き場からの移送が起きたとき」しか通らない＝**新品の利用者データでは 11 名が画面に見えるのに合成できない**（`GET /v1/audio/voices` にも試し撃ちの候補にも出ない）。裁定 78 ⑴ の「ランチャは話者を voices.json に書いた後に…」の前半が抜けていた | `LauncherComposition.PrepareVoices(paths, store, writer)` を新設（**テストの継ぎ目は public**）＝移送→初回展開→**写したなら（または別名表が無いなら）`voices.json` を書く**。釘 3 本＝`初回に写したプリセットは上流の別名表にも載る`・`二度目は写さないが別名表は消えたままにしない`・`別名表が既に在れば焼いた潜在を消さない` |
| 3 | **走行中で断られた焼きを二度と出し直さなかった**。⑵ を直した結果 `rocm-*` は ready の直後にプリセット 11 名の焼きを自分で始めるようになり、**その最中に話者を足すと** `POST /ywk/voices/precompute` が **409 `ywk_precompute_running`** で断られる。ランチャは理由を出したきり忘れるので、足した話者は「未」のまま置き去りになった（**実射の逐語**＝`事前計算を始められませんでした：事前計算が走行中（id=476df2759149）…` → 119.5 s 待っても `1 名あたり＝wav 参照（概算 +716.8 MB・実測前の概算）`） | `VoicesViewModel` に待ち行列を足し、**機械可読な code**（`ywk_precompute_running`・文言では判定しない＝契約 ⑶ 3-3）で断られたときだけ覚える。`MainViewModel.ApplyStatusSample` が `/ywk/status.precompute` を `Voices.ApplyPrecompute` へ配り、**走行が終わった標本で 1 度だけ**出し直す（窓は HTTP を持たない＝low 3 のまま）。釘 3 本＝`走行中で断られた焼きは口が空いたら出し直す`・`覚えていない焼きは標本が来ても出さない`・`口が無い個体では覚えない` |

⑵ と ⑶ で、**画面席 §17-4 ⑴（プリセット 11 名が「焼いた話者 0 名」のまま起動する）の票は閉じる**＝
是正後の実射は `ON（焼いた話者 12 名・合計 1.2 MB）`（プリセット 11 ＋ 検分が足した 1 名）。

### 19-2 台本に足した段（`probe/d-launch-probe.ps1`・**31 段 → 57 段**）

| 段 | 何を撃つか | 裁定 |
|---|---|---|
| **d** 変種の門 | `settings.json` の変種を `cu130` にした**別個体**を私設ポート 18099 で起こし、「サーバ起動」を押す→ **失敗**・理由 1 行・勧める変種・**子プロセス 0**・**ポートは一度も開かない**（5 段） | 88 ⑴⑵・80・83 |
| **g** プリセット | 配布樹の `presets.json`（`status=done` 11 行）→ ランチャの台帳（`presets_installed` の印）→ **上流の別名表** → 一覧の 12 行（うち種別「プリセット」11）の 4 箇所を突き合わせる（4 段） | 88 ⑸・17 |
| **f** 削除の理由 | 「デフォルト」を選ぶ→`VoicesRemoveBlockedText` と**ボタンの `HelpText` が同一文言**・ボタンは押せない（2 段） | low 14 |
| **a** メモリの帯 | `使用量 … 占有量 … GPU 全体 … / …` の 4 数がすべて出る（「未対応」でも「—」でもない）（1 段） | 87 ⑴・67 ⑶ |
| **b** 潜在キャッシュ | `ON／OFF（焼いた話者 n 名・合計 …）`（1 段）と、焼いた後に n が増えること（1 段） | 67 ⑴ |
| **c** 概算→実測 | 足した話者の 1 行が `wav 参照（概算 +716.8 MB・実測前の概算）` から **`潜在参照（実測 113.4 KB）`** に変わる（2 段） | 67 ⑵・87 ⑴ |
| **e** 死活 | ⑴ 停止→起こし直し ⑵ **待機中に外から子を kill** → 帯が「失敗」＋理由 1 行（**3 秒以内**）・ポート解放 ⑶ 起こし直し ⑷ **合成中に外から kill** → HTTP の期限（120 s）を待たずに「サーバが落ちました」（8 段） | 88 ⑶・83 |

台本の作法＝`-GateOnly`（門だけ 6 秒で撃つ）・`-SkipGate`／`-SkipLiveness`・`-GatePort`／`-GateVariant` を足した。
門の段は **`<私設 runtime 根>\cu130` を `build/out/runtime-cpu` への junction にする**（`New-GateRuntimeRoot`）＝
門は「変種が解決でき、しかもその実行系が GPU を見られない」形でしか撃てず、この機体で作れる唯一の形が
CPU 変種の torch だからである（起動席 §16-3 の実測＝`available=false`・`count=0`）。
配布樹は読むだけで、junction は teardown で消す（**指す先には触れない**）。
日本語の文字列は檔頭の作法どおり符号位置から組んである
（**台本は ASCII のみ・CRLF・PowerShell 5.1 と 7 の両方で parse を通した**）。

### 19-3 実射（この機体・rocm-gfx1151・私設ポート 18094／18099・**8088・7861・18088 は一度も起こしていない**）

```
pwsh -File probe/d-launch-probe.ps1 -Port 18094 -GatePort 18099
=== 0 failure(s) of 57 ===        EXIT=0
```

**門（裁定 88 ⑴⑵）**

```
[gate] <TEMP>\ywk-d-launch-probe-gate-runtime\cu130 -> build\out\runtime-cpu
[gate] variant = CUDA 13.0（既定・ドライバ 580 以上）
[gate] state   = 失敗 after 2.95 s
[gate] reason  = cu130 はこの機体で GPU を見られません（is_available=False・device_count=0）。rocm-gfx1151 か cpu の変種に切り替えてください。
[PASS] the gate started no wrapper child  children=0
[PASS] the gate never opened the port  port 18099
```

＝**2.95 s**（GPU 列挙の torch 1 回＋門の torch 1 回）で断り、**`ywk_server` は 1 個体も起きない**。
起動席 §16-3 の見積り（門が足す代金 1.6〜1.7 s）と整合する。

**状態帯（裁定 87 ⑴・67 ⑴⑵⑶）**

```
[start] state=暖機中 after 27.6 s
[status] device  = cuda:0／AMD Radeon(TM) 8060S Graphics（gfx1151）／bf16
[status] memory  = 使用量 1.74 GB／占有量 2.06 GB／GPU 全体 2.36 GB / 99.74 GB（最大 3.64 GB）
[latent] ON（焼いた話者 0 名・合計 0 B）        （ready の直後・焼きはこれから）
[memory] per voice before = 1 名あたり＝wav 参照（概算 +716.8 MB・実測前の概算）
[memory] per voice after  = 1 名あたり＝潜在参照（実測 113.4 KB）   (22.8 s)
[latent] after the bake = ON（焼いた話者 12 名・合計 1.2 MB）
```

`113.4 KB` は wrapper 席 §15-2 の実射（`latents/ywk-….pt` **113,405 B**）と同じ値＝
**画面が出しているのは係数ではなく `memory.latents[<話者 id>]` そのもの**である。
`使用量 < 占有量 < GPU 全体`（1.74 < 2.06 < 2.36 GB）も実物で成り立っている。

**プリセット（裁定 88 ⑸）**

```
[presets] app tree rows with a secondary wav = 11
[PASS] PresetsInstalled is marked and every preset is in the table  presetsInstalled=True in table=11/11
[PASS] every preset also reaches the alias table the wrapper reads  in voices.json = 11/11
[presets] rows on screen = 12  marked preset = 11
[try] voices = デフォルト | KANA（ないしょばなし） | MANA（いっしょうけんめい） | おふとんP（きざ） |
               おふとんP（のーまるv2） | ちび式じい | つくよみちゃん | もち子さん | 吉田くん |
               琴葉茜（関西弁） | 月読アイ | 月読ショウタ
[tail] INFO:ywk_server:precompute on start: id=049d6bcc49ec total=11
```

最後の 1 行が 19-1 ⑵ を直した証拠である＝**wrapper の起動時 all が 11 名を見つけている**
（直す前は `voices.json` が「デフォルト」1 行だったので誰も焼かれなかった）。

**死活（裁定 88 ⑶）**

```
[restart]  state=待機 after 23.2 s
[kill]     pid=39096                       （taskkill /T /F ＝ランチャの外から）
[liveness] state=失敗 after 0.31 s
[liveness] reason=サーバが異常終了した（終了コード 1）。 INFO:ywk_server:precompute 12/12 id=琴葉茜（関西弁） state=reused frames=873 ms=4
[restart]  state=待機 after 23.2 s
[kill]     pid=34372                       （40 歩・120 字の合成の最中に）
[liveness] try message = サーバが落ちました（exit 1）。 サーバが異常終了した（終了コード 1）。 …
[PASS] a synthesis is told at once that the server went down  told in 0.39 s (deadline was 120 s)
```

＝**待機中の kill は 0.31 s で「失敗」**（要求は 3 秒以内）・**合成中の kill は 0.39 s で「サーバが落ちました」**
（HTTP の期限 120 s を待たない）。起動席 §16-3 の机上の実測（子が消えてから `StateChanged(Failed)` まで 4 ms）に
UIA の読みと `Dispatcher` の marshal が乗った値である。**2 度とも起こし直しは 23.2 s で通った**
（1 度目の 27.6 s より速いのは MIOpen db と潜在が温まっているため）。

**そのほかの逐語**（1 巡目 §13-4・§14-2 と比べられる値）

| 段 | 便 D（1） | 便 D（2） |
|---|---|---|
| 起動 → 待機 | 27.5〜28.9 s | **27.6 s**（2 度目以降 23.2 s） |
| GPU 列挙 | 2.2 s | **2.23 s** |
| 試し撃ち（参照なし・10 歩） | 所要 5.52 s／RTF 4.06 | **所要 5.97 s／出力 1.36 s／RTF 4.39** |
| 試し撃ち（足した話者・10 歩） | 所要 4.55 s／RTF 1.26 | **所要 4.78 s／出力 3.60 s／RTF 1.33** |
| 初回取得の見積り | 取得 4.79 GiB・必要な空き 9.55 GiB | **同じ**（実行系 1.44 GiB・モデル 3.33 GiB・vc_redist 24.4 MiB） |
| 停止 | 0 s・残骸 0 | **0 s・残骸 0** |
| 段の数 | 31 | **57** |

### 19-4 発行した 1 exe（受け入れ条件 D-7 を発行物でも撃つ）

```
build/release-build.ps1
[STEP 0]  version = v0.1.0 / upstream 8224dafb…（Irodori-TTS）・841fb7c6…（Irodori-TTS-Server）
[STEP 2a] Microsoft.NETCore.App.Runtime.win-x64 = 10.0.11 / WindowsDesktop = 10.0.11
[STEP 2b] produced 2 file(s): IrodoriTtsYwk.Launcher.exe, IrodoriTtsYwk.Launcher.pdb
          (e) the assembly carries v0.1.0 and both upstream pins
          (f) IrodoriTtsYwk.Launcher.exe = 69,608,415 B (66.4 MiB)
[STEP 4]  irodori-tts-ywk-launcher-v0.1.0-20260905-0750.zip = 64,038,990 B (61.1 MiB)
DONE      EXIT=0  elapsed=13 s
```

**その exe に同じ台本を通した**（`-Exe build/out/launcher/win-x64/IrodoriTtsYwk.Launcher.exe
-Port 18095 -GatePort 18099 -DataDir %TEMP%\ywk-d-launch-probe-exe`）＝**57 段すべて緑・EXIT=0**。
1 巡目（§13-5）は 21 段だったので、**発行物で 57 段を通したのはこれが最初**である。

```
[ui]       state=停止  endpoint=http://127.0.0.1:18095
           version=版 v0.1.0／Irodori-TTS 8224daf／Irodori-TTS-Server 841fb7c   （Debug は「開発ビルド」）
[gate]     state = 失敗 after 3.15 s（Debug は 2.95 s）
[start]    state=暖機中 after 27.4 s
[status]   memory = 使用量 1.74 GB／占有量 2.06 GB／GPU 全体 2.36 GB / 99.74 GB（最大 3.64 GB）
[memory]   per voice after = 1 名あたり＝潜在参照（実測 113.4 KB）   (23.2 s)
[latent]   after the bake  = ON（焼いた話者 12 名・合計 1.2 MB）
[try]      所要 6.19 秒／出力 1.36 秒／RTF 4.55（参照なし・10 歩）
[try]      所要 4.65 秒／出力 3.60 秒／RTF 1.29（足した話者・10 歩）
[restart]  state=待機 after 23.2 s（2 回とも）
[liveness] state=失敗 after 0.31 s ／ try message = サーバが落ちました（exit 1）。… told in 0.36 s
=== 0 failure(s) of 57 ===        EXIT=0
```

**自己展開の 1 exe でも Debug と同じ数字が出る**（§13-5 の所見のとおり）＝起動 27.4 s（Debug 27.6 s）・
門 3.15 s（同 2.95 s）・死活 0.31 s（同 0.31 s）・潜在の実サイズ 113.4 KB（同一）。
版と上流 pin は**発行のときだけ**焼かれるので、状態帯の版行は exe でだけ `v0.1.0` を名乗る。
1 巡目（§13-5）の exe 69,588,729 B に対して **69,608,415 B**（+19,686 B＝この便で足した分）。

### 19-5 触っていない物

`server/`・`tests/`・`upstream/`・`research/`・`voices/`・`tools/`・`ledger/`・`licenses/`・
`probe/common.ps1`・`probe/rocm-*`・`probe/cuda-*`・`probe/rtx-*`・`build/**`（**実走しただけで 1 行も直していない**）・
`decisions.md`・`docs/contract.md`・`docs/acceptance.md`・`docs/radeon.md`・本檔の §1〜§18。
`launcher/` の中でも `Services/Gpu/`・`Services/Server/`・`Services/Ledger/`・`Services/Voices/`・
`Contracts/`・`Views/`・`Audio/`・`Mvvm/` は 1 行も触っていない
（直したのは ViewModel 2 檔と組み立て 1 檔だけ）。
git の commit／push はしていない。第三者バイナリは 1 檔も足していない。外へ 1 バイトも出していない
（実射は `HF_HUB_OFFLINE=1`・HF は `%USERPROFILE%\.cache\huggingface` の既存キャッシュのみ）。

### 19-6 卓へ／後続へ

1. **門の「ドライバが読める枝」は未実射**。この機体で撃てたのは「NVIDIA が居ない機体で cu130 を断り
   `rocm-gfx1151` を勧める」形だけで、`driver_version` が読める枝（537.58 で cu130 を断って cu126 を勧める／
   591.86 で cu130 を通す＝§16-2 の 2・5 行目）は**釘（純関数）でしか押さえていない**。
   便 E（RTX 機）で `-GateVariant cu130 -GatePort 18099` を撃つこと。台本はそのまま使える。
2. **プリセット 11 名の焼きは起動のたびに走る**（2 度目以降は `state=reused` で 1 名 4 ms＝実測）。
   初回だけ ready の後に約 40 秒の GPU 仕事が乗る＝その間の合成は**走行中の 1 件分だけ待つ**
   （裁定 60 と同じ形）。CUDA 変種は事前計算が既定 OFF なので影響しない。
   利用者に見せる文言（`いま別の事前計算が走っているので、終わり次第この n 名を焼きます。`）は
   この待ちを説明するためのものである。
3. **`_pendingPrecompute` は個体の寿命だけ持つ**（`settings.json` にも台帳にも書かない）。
   焼く前にサーバが落ちれば忘れる＝次の起動で wrapper の起動時 all が拾う。
   拾わない形（CUDA 変種で事前計算を ON にしていて、走行中に足した話者がある個体を落とした）だけは
   「参照潜在を焼く」を押す必要がある。
4. **`empty_cache_interval`（裁定 69 ⑵）は台本で撃っていない**。設定の欄は在り（`SettingsEmptyCacheBox`）、
   env に載ることは机上の釘で押さえてあるが、0 と 10 の**実挙動の差**は裁定 77 のとおり
   「見える標本が無い」ままなので、段を足す価値は薄いと判断した。
5. **共通檔窓は相変わらず UIA から見えない**（§13-3・§14-3）。今回足した 26 段も 1 つも窓を開けていない。
6. **`VcRedistInstaller.cs:172` の古い doc コメント**（工具席 §18-7 ⑵＝「設計書は `/passive`」）は
   **今回も直していない**＝`Services/` は他席の担当で、統合の実射に関係しない 2 行だからである。
   `launcher/` を触る次の席が消すこと。
## 20. 是正席（便 D（2））の記帳（2026-09-05・opus サブ席）

敵対検分 3 席の **high 1 件・medium 8 件**を直し、釘（xUnit **23 本**）を足し、**この機体で撃ち直した**。
**この節も実装の逐語**であって §1〜§19 の設計を覆さない。**他席の節（§9〜§19）は 1 行も触っていない**
（§15-1 と §11-2 に残る「CUDA 側は文字列」の 1 行は他席の記帳なので直さず、20-1 ⑼ に訂正を書く）。

### 20-1 直した表（重い順）

| # | 重み | 何が壊れていたか（是正前の実射） | 直し方 | 釘（`RoundTwoCorrectionTests.cs`） |
|---|---|---|---|---|
| ⑴ | **high** | **門の検分が「読めなかった」cu130 をそのまま起こしていた**＝`Decide("cu130", 期限切れ／実行系なし, driver=null)` が `Allow=True`＋注意 1 行。裁定 83 の禁止形（起動は健全に見え、最初の合成で `0xC0000005` でプロセスが消える）へ **`is_available` を 1 度も観測しないまま**到達できた | `VariantGate` に⑵-b の枝＝**`cu130` と、cu130 かもしれない畳んだ名 `cuda` だけ**は検分が読めなければ **起こさない**（`RequiresObservedProbe`）。`cu126`・`rocm-*` は従来どおり注意 1 行で起こす（cu126 は GPU を隠しても 200 が返る＝裁定 83） | `門_cu130は検分が撃てなければ起こさない`／`門_cu130は検分が読めなければ起こさない`／`門_cu126とrocmは検分が読めなくても注意1行で起こす` |
| ⑵ | medium | **畳んだ名 `cuda` で入るとドライバの閾も「未実測の帯」も丸ごと落ちた**（`DriverRequirement.Minimum("cuda")` が `null`＝実射でドライバ 500.00 でも `Allow=True`）。註（`IServerProcess.Variant`・`MainViewModel:245`）は「勧める変種が粗くなる」としか書いていなかった | `Minimum` が畳んだ名を **cu130 の閾（580.00）**で見る＝どちらの実行系か判らない以上、厳しい方に倒す。註 3 箇所を実挙動（閾も注意も掛かる／読めなければ起こさない）に直した | `門_畳んだ名cudaにもドライバの閾が掛かる`／`門_畳んだ名cudaも検分が読めなければ起こさない` |
| ⑶ | medium | **門の検分が `OperationCanceledException` 以外で落ちると `StartAsync` が例外を投げた**（契約＝「例外は投げず結末で返す」に反する）。実路＝`GpuEnumerator` の期限切れ枝の `Kill(entireProcessTree: true)` が `Win32Exception` を投げる。呼び手の `AsyncRelayCommand` はこれを捕らない＝**「サーバ起動」が黙って何もしないボタン**になる | `DecideGateAsync` の catch を全例外へ広げ、理由 1 行を持った「読めなかった検分」に落として同じ純関数へ渡す。`ProcessRunner` の `Kill` も `Win32Exception`／`AggregateException` を握る | `門の検分が落ちてもStartAsyncは投げない`（2 型） |
| ⑷ | medium | **「応答が消えた個体を降ろす」までが註の 3 倍**（註「約 6 秒」・実測 **18.0 s**）。1 標本が `/ywk/status` →（読めなければ）`/health` の 2 本で、誰も居ない相手には 1 本 2.0 s かかっていた | ⒜ **応答が 1 つも返っていない相手（`StatusCode==0`）には 2 本目を撃たない** ⒝ `WrapperClient` に **接続の期限 1.5 s**（`SocketsHttpHandler.ConnectTimeout`）＝期限切れの 1 行は「繋がりません」に寄せる。註は実測に書き直した | `HealthPoller`／`WrapperClient` の逐語（下の 20-2 に実測） |
| ⑸ | medium | **`FirstRunViewModel.TryPlan`／`EstimateSizeText` が `LedgerException` を素通しした**＝low 6 の直しが投げるようになったのに檔頭の約束（「読めなければ null」「不明と名乗る」）のまま。呼ばれるのは**構築時と `Variant` の setter＝UI スレッドの束縛経路**で、`App.xaml.cs` に受け口は無く `AsyncRelayCommand` も捕らない＝**台帳が 1 件でも欠けた日に窓ごと落ちる** | `TryPlan` が `LedgerException` を捕って `null`＋理由 1 行を返す（`out string? failureReason` の版を足した）。見積りは「不明（取得台帳が読めません：…）」 | `初回_sha256の無い台帳でも計画は投げずに理由を返す`／`初回_sha256の無い台帳でも見積りは投げずに不明と名乗る` |
| ⑹ | medium | **取得の本文の読みに期限が 1 つも無い**＝`HttpClient.Timeout=InfiniteTimeSpan` で読み回しは呼び手の token だけを見る。呼び手の CTS にも期限が無い＝**返さない相手に当たると利用者が「中止」を押すまで永久に止まる**（実射＝45 秒待っても返らない） | `HttpDownloader.IdleTimeout`（既定 **60 s**）＝**進むたびに押し直す無通信の期限**。打ち切りは `IOException` に落とすので `.part` は残り、次の試行が `Range` で続きから取る | `取得_本文が進まなければ無通信の期限で打ち切る`（`IdleTimeout=1 s` で 1 s 台・`.part` が残ることまで） |
| ⑺ | medium | **裁定 87 ⑷ の「Unknown は利用者に問う」に答える口が無かった**＝`VcRedistResult.NeedsUserDecision` は launcher/ の中で**どこからも読まれておらず**、System32 が読めない機体では初回取得が**先へ進めなかった**（飛ばす口も入れる口も無い） | `FirstRunViewModel.AskVcRedist`（窓が差す 2 択）＋`VcRedistInstaller.AssumeInstallWhenUnknown`。**「飛ばす」で先へ進み**、**「入れる」と答えたときだけ**入れ直す。窓は `FirstRunWizard` の「はい＝入れる／いいえ＝飛ばす／キャンセル＝答えない」 | `vc_redist_判らないときは飛ばす選択で先へ進める`／`vc_redist_入れると答えたときだけ入れ直す`／`vc_redist_問う口が無ければ勝手に入れない`／`vc_redist_入れると答えたら判らなくても導入まで行く` |
| ⑻ | medium | **見張りが「自分の子か」を確かめない**＝wrapper は `preload=true` で**モデルを載せてから bind する**（20〜28 s）ので、その窓の間に他人が同じポートを握るとランチャは**他人の `/ywk/status` を読んで「待機」**を出し、状態帯（裁定 67 ⑶）に他人の数字を出す。第 2 の網（uvicorn の bind 失敗行）も `State < Listening` で切っていたので**子の bind 失敗を無視**した | ⒜ wrapper の `/ywk/status` に **`pid`** を 1 欄足し（契約 ⑹）、ランチャは自分の子の pid と突合して**違う標本を採らない**（`Ready` にも上げない・ログは pid 1 つにつき 1 行） ⒝ bind 失敗の検知を切る条件を「状態」から**「自分の子の `Uvicorn running on` を見たか」**に変えた | `死活_他人のpidの応答は自分の物として採らない`／`死活_pidの無い応答は別人と読まない`／`死活_自分の子のuvicornの行を見るまでbind失敗を拾い続ける`／`死活_自分の子がlistenした後は走行中の行で落とさない` |
| ⑼ | medium | **契約 ⑹ の「torch が返す型は機体で違う＝CUDA 側は文字列」が事実に反する**（裁定 87 ⑵ 自身は「ROCm は int」としか言っていない＝席が足した推測が正典に入っていた） | `docs/contract.md` ⑹ を実測に直した＝**CUDA でも ROCm でも `int`**（gfx1151 実射 `197`・RTX 実射 `1`＝裁定 75 ⑷・同じ C++ binding 1 つ・型 stub は属性を宣言していない・torch 自身が `f"{bus:02x}"` で整数として扱う）。`tests/contract/test_memory.py` の檔頭も直した。**`str()` を掛ける実装は正しいので触っていない** | 既存の `test_pci_bus_id_is_a_string`（模擬 `FakeProps.pci_bus_id = 1` は正しいので不変） |

**ついでに直した low（同じ檔を触ったもの）**

| 檔 | 直し |
|---|---|
| `Contracts/IGpuEnumerator.cs` | `Minimum` が**大小と前後の空白**を受ける（同じ門の `IsUnmeasuredBand` は `OrdinalIgnoreCase` なのにここだけ厳密一致だった）／`TryCompare` が**2 節の版を小数として比べる**（`537.58` と `537.6` の順序が逆になっていた＝実射 `1` → `-1`。3 節以上は従来どおり節ごとの整数） |
| `Services/Gpu/VariantGate.cs` | 勧め先が **`cpu` を組んでいない配布**（台帳が rocm だけ）で cpu を勧めない（**判らない＝空のときは従来どおり cpu**）／理由 1 行の**絶対パスを畳む**（`FoldPaths`＝.NET の例外文がフルパスを持つ）／`CUDA 版 の` の二重の切れ目を直す |
| `Services/Server/ServerProcess.cs` | 門の告知（「未実測の帯」）を**子を起こし損ねた経路でも落とさない**／**落ちた個体の `LatestStatus` を掃く** |
| `Services/Ledger/VcRedistInstaller.cs` | §19-6 ⑹ の宿題＝「設計書は `/passive`＝食い違っている」の古い註を裁定 87 ⑷ の決着（**台帳が正**）に直した |

### 20-2 実測（この機体・rocm-gfx1151・**私設ポート 18098／18099 だけ**・8088／7861／18088 は一度も起こしていない）

**是正前 → 是正後**（同じ道具・同じ機体で撃ち直した）

| 何 | 是正前 | 是正後 |
|---|---|---|
| 門＝cu130・検分が読めない（`probe=null`／期限切れ／実行系を起こせない） | `Allow=True`＋注意 1 行（**起こす**） | `Allow=False`・理由 1 行・勧め先つき（driver 591.86 なら **cu126**・読めなければ **cpu**） |
| 門＝畳んだ名 `cuda`・ドライバ 500.00 | `Allow=True`（閾も注意も無し） | `Allow=False`＝`ドライバ 500.00 は CUDA 版の下限 580.00 に届きません。cpu の変種に切り替えてください。` |
| 門の検分が `Win32Exception`／`InvalidOperationException` を投げる | **`StartAsync` が投げた**（契約違反） | `Ok=False`・`State=Failed`・理由 1 行（`GPU の検分そのものが落ちました：…`） |
| 到達不能な 1 標本（誰も居ない 127.0.0.1） | **4,118／4,013／4,013 ms** | **1,546／1,502／1,501 ms** |
| 応答が消えてから `Ready → Listening` へ降ろすまで | **18,048 ms**（註は「約 6 秒」） | **10,546 ms**（註を実測に書き直した） |
| 他人が同じポートで答えている（pid 不一致） | 0.59 s で **待機**＝他人の `memory`（`allocated=1`）を採る | **待機に上げない**・標本を採らない（`LatestStatus=null`）・理由 1 行＝`ポート 18097 には別の個体（pid 999999）が答えています。…` |
| 本文を返さない相手からの取得 | **45 秒待っても返らない** | `IdleTimeout=3 s` の実射で **3,050 ms** で `Ok=False`＝`本文が 3 秒のあいだ 1 バイトも進まなかったので打ち切った`（`.part` は残る・既定は 60 s） |
| sha256 の無い台帳での見積り | **`LedgerException` が UI スレッドへ抜ける** | `不明（取得台帳が読めません：取得台帳に sha256 の無い item がある…）` |

**実行系の実射**（`build/out/runtime-rocm-gfx1151/python.exe`・`HF_HUB_OFFLINE=1`・ポートは 1 つも開けていない）＝
`prop pci_bus_id = 197  type=int`／`prop gcnArchName = 'gfx1151'  type=str`／`torch 2.13.0+rocm10.0.0 hip 7.15.26333`。
裏づけ＝`torch/numa/binding.py` が同じ属性を `f"{bus:02x}"` で整数として扱う（ROCm 固有ではない共通コード）・
`torch/_C/__init__.pyi` の `_CudaDeviceProperties` は `pci_bus_id` を宣言していない。

**無人検分の実射**（`probe/d-launch-probe.ps1`・Debug の窓と発行した 1 exe の 2 回）＝
起動 → 待機 **26.6 s／26.8 s**（2 度目以降 22.8 s／23.2 s）・門の拒否 **2.94 s／2.98 s**・
待機中の kill から「失敗」まで **0.32 s**・合成中の kill から「サーバが落ちました」まで **0.39 s**・
`使用量 2.21 GB／占有量 3.02 GB／GPU 全体 3.46 GB / 99.74 GB（最大 3.64 GB）`・
**`status.pid=11020 children=11020`**（新しい段＝答えたのが自分の子であることの突合）。

### 20-3 検分（撃ち直した全部）

```
dotnet build launcher\IrodoriTtsYwk.sln --nologo --no-incremental   0 warnings / 0 errors
dotnet test  launcher\IrodoriTtsYwk.sln --nologo                    499 passed（476 → +23）
build\run-tests.ps1                                                  339 passed・upstream clean・EXIT=0
build\check-tree.ps1                                                 EXIT=0
build\check-licenses.ps1                                             EXIT=0（13 檔）
build\assemble-app.ps1                                               EXIT=0（108 檔・プリセット 11 wav）
probe\d-launch-probe.ps1 -Port 18098 -GatePort 18099                 **0 failure(s) of 58**（57 段＋pid の 1 段）
  同上 -Exe build\out\launcher\win-x64\IrodoriTtsYwk.Launcher.exe    **0 failure(s) of 58**（発行した 1 exe でも）
build\release-build.ps1                                              EXIT=0・exe 69,610,933 B（66.4 MiB・§10-3 の 69,608,415 B から +2,518 B）
```

### 20-4 触っていない物

`upstream/`・`research/`・`voices/`・`tools/`・`ledger/`・`licenses/`・`probe/common.ps1`・
`probe/rocm-*`・`probe/cuda-*`・`probe/rtx-*`・`build/make-handoff.ps1`・`decisions.md`・
`docs/acceptance.md`・`docs/radeon.md`・**本檔の §1〜§19**。`build/*.ps1` は**実走しただけ**で 1 行も直していない。
`server/ywk_server.py` は **`/ywk/status` に `pid` を 1 欄足しただけ**（他の路は 1 行も触っていない）。
git の commit／push はしていない。第三者バイナリは 1 檔も足していない。外へ 1 バイトも出していない。

### 20-5 卓へ／後続へ（便 D（3）・便 E）

1. **未解決で置く 1 つ**＝**起こせない `python.exe` を指した実行系で「サーバ起動」を押すと、窓が 60 秒
   何も出さない**（状態は `停止` のまま・**ログ帯にも 1 行も出ない**・子も 0・ポートも開かない）。
   模型の側は健全＝同じ材料で `ServerLaunchPlan.Build` → `ServerProcess.StartAsync` を直に撃つと
   **8 ms** で `Failed`＋理由 1 行を返す（`実行系を起こせませんでした：… is not a valid application …`）。
   **どこで止まっているかの手掛かり**＝その 3 回の走行のあと `%TEMP%` に
   `ywk_gpu_probe-<8 桁>.py` が **3 檔残っていた**（1,269 B・9:05／9:10／9:13）。この檔は
   `TorchProbeRunner.ProbeAsync` の `finally` が消すので、**`IProcessRunner.RunAsync` が返って
   いない**＝GPU 列挙（`MainViewModel.StartServerAsync` の最初の await）で止まっている。
   同じ材料を**コンソールから**撃つと `Process.Start` が `Win32Exception`（`is not a valid
   application for this OS platform`）を投げて **18 ms** で返るので、**窓（WPF・メッセージポンプ）
   の側でだけ**起きる形である（システムの「このアプリは PC で実行できません」の窓が出ている疑い）。
   是正席の担当（門）ではないので直していないが、**`AsyncRelayCommand` が捕らない例外は静かに消える**
   （`RelayCommand.cs:100-119`）ことは medium ⑶ と同根なので、便 D（3）で
   ⒜ `Faulted` に全例外を回す受け口 ⒝ `ProcessRunner.RunAsync` に「起こす」段そのものの期限
   （いまの期限は `WaitForExitAsync` にしか掛かっていない）を足すことを勧める。
   台本にこの段を足す試みは**取り下げた**（無人検分で 60 秒待って 2 段が赤くなるだけなので、
   原因が判るまで台本に入れない）。門そのものの釘は xUnit 側にある。
2. **`pid` の突合は「欄が在るときだけ」**＝古い個体・上流の素の Server では `pid` が来ないので突合しない。
   便 E で古い配布樹と新しいランチャを混ぜても壊れない。
3. **`WrapperClient.ConnectTimeout=1.5 s` は loopback 前提の値**である。本体（読み分けちゃん2）が
   別機から叩く路は無い（bind は `127.0.0.1` 固定＝裁定 2）ので問題にならないが、値を動かすときは
   死活の 3 標本（約 10.5 秒）が連動することを憶えておくこと。
4. **`HttpDownloader.IdleTimeout` の既定 60 s は「遅い回線」を殺さない値**として置いた
   （1 MiB の読みが 60 秒進まなければ打ち切る）。5 回の試行 × 2 本の url で最悪 10 分で諦める。
5. **`probe/README.md` の「31 段」は 2 巡目の実物（58 段）と食い違う**。是正席の触ってよい path に
   入っていないので直していない（統合席の 57 段の時点で既に古い）＝檔を持つ席が 1 語直すこと。
6. **門の「ドライバが読める枝」は相変わらず未実射**（§19-6 ⑴ のまま）。今回足した `cu130` の
   「検分が読めない」枝も RTX 機で 1 度撃つ価値がある（`-GateVariant cu130`）。


---

## 22. 検分席（便 D（3））の記帳（2026-09-05・opus サブ席）

裁定 92 が便 D（3）へ残した「`d-launch-probe` の段 c／g の締め」を締め、裁定 94 ⑴・裁定 91・裁定 90 Q-E2 ⑶ と
本檔 §20-5 ⑴ を撃つ**新しい段 5 つ**を足した。**ランチャ席が同時に画面を書いているので、この席は窓を 1 枚も
開けていない**（同じ Debug exe を掴むと相手の `dotnet build` を壊す）。撃ったのは**構文検分・空走・下拵えの実走**の
3 つで、**実走は統合席に委ねる**。他席の節（§1〜§21）は 1 行も触っていない。

### 22-1 触った檔（3 檔）

| 檔 | 何をしたか |
|---|---|
| `probe/d-launch-probe.ps1` | 段 c／g を締め、段 **h・i・j・k・l** を足した（檔内の function は **10 → 32**＝段の本体 4 本＋道具 18 本。ASCII・CRLF・PS 5.1／7）。既存の段は 1 つも消していない |
| `probe/README.md` | 便 D の「31 段」を実物に直し（§20-5 ⑸ の宿題）、便 D（3）の段と switch を書き、後続への申し送りを 2 つ足した |
| `docs/design/ben-d-launcher.md` | 本節（§22）の追記のみ |

`probe/e-install-probe.ps1` は**裁定どおり 1 行も触っていない**（読み合わせの結果は §22-6）。
`probe/common.ps1`・`launcher/`・`build/`・`server/`・`ledger/`・`voices/` は読むだけ。

### 22-2 裁定 92 の締め＝段 c と段 g

**段 c（1 名あたりの欄）＝「概算」の語と係数 716.8 MB の一致まで締めた。**

- 締める前の条件は `名あたり` を含み、かつ `実測前の概算` **または** `実測` を含む、だけだった。
  ⇒ **何も焼いていないのに「実測」と書く画面が通る**し、**係数がいくつでも通る**。
- 締めた後は 2 枝のどちらかを要求する＝⒜ **概算の枝**＝`名あたり`＋**`概算`**＋`実測前の概算`＋
  **`716\.8\s*M(i)?B`** ⒝ **既に焼けていた枝**＝`名あたり`＋`潜在参照`＋`実測`＋`実測前の概算` を含まない。
- **716.8 の出所**＝`ViewModels/MemoryEstimate.cs` の `WavReferenceBytes = 751619276`（0.7 GiB）を
  `UiText.Bytes` に通すと **1 GiB 未満なので MB の枝**に落ちて `751619276 / 1024 / 1024 = 716.80` ＝
  **`716.8 MB`**。単位の綴りだけ緩く見る（`MB`／`MiB` の両方を許す）＝**裁定 92 が便 D（3）に残した low に
  「`UiText` の GB/GiB」が入っており、綴りは動きうるが数字は動かない**からである。
- 焼けた後の欄も締めた＝`潜在参照`＋`実測`＋`実測前の概算` 無し＋**係数と一致しない**＋数字を含む。
  （以前は「係数のままでも `潜在参照（実測 …）` と書けば通る」形だった。）

**段 g（プリセット）＝直値 11 を捨てた。**

- 期待値は `build/out/app/voices/presets.json` の **`status=done` の実数**から作る
  （実測＝12 行のうち done 11・skipped 1＝`cevio_maki_en` は `secondary` を持たない）。
  ⇒ 便 P が 1 名足した日に**正しい樹で赤くなる**ことも、**1 行落ちた樹で緑のまま**になることも無くなる。
- 段を 1 本足した＝**表が名指しした wav が配布樹に在る**（`voices/presets/<secondary.file>`）。
  「表には在るが檔が無い」＝一覧に出て合成できない、を数える段が今まで無かった。
- `status=done` なのに `secondary.file` を持たない行は id を並べて詳細に出す（数と名前の両方を残す）。

### 22-3 足した段（h・i・j・k・l）

**外への取得は 1 バイトもしない。**そのために私設の配布樹（`server/`・`licenses/`・`voices/` は
`build/out/app` への **junction**、`ledger/` だけ本物の複製）を作る道具を入れた＝`New-PrivateAppTree`。

| 段 | 何を撃つか | どう「取得しない」を担保したか |
|---|---|---|
| **h** | **初回取得ウィザードの押下数**（裁定 94 ⑴）。押下＝⑴ 同意チェック ⑵「同意して次へ」⑶ 変種 ⑷「この構成で取得を始める」⑸「試し撃ちへ」 | 私設樹の `ledger/runtime-<変種>.json` の**最初の item の `sha256` の鍵名だけ**を書き換える。`LedgerReader` が「sha256 が無い」と数え、`FetchPlanner.cs:192` が `LedgerException` を投げ、`FirstRunViewModel.TryPlan` がそれを捕って**socket を 1 本も開かずに**取得の段が false になる。さらに **`vc_redist.json` を写さない**＝`MainViewModel.cs:161-166` が null を返し `FirstRunViewModel.cs:700-703` が「その段は無い」と読むので、**msvcp140.dll が台帳の下限に届かない機体でも 24 MiB を落としに行かない** |
| **i** | **試し撃ち 200 の後に取得キャッシュが消える**（裁定 90 Q-E2 ⑶・94 ⑶）。**バイトで読む** | 落とさないので**種を播く**（`New-CacheFixture`＝原檔・`.part`・入れ子の 3 檔 196,608 B）。段は 2 本＝**1 射を撃つ前は残っていること**（起動時に掃くのは誤り＝`.part` は続きから取るための物）と、**200 の後に 0 バイトになること**（30 s まで待つ） |
| **j** | `settings.json` の **`runtimeLedgerSha256`／`installedAppVersion`** が焼かれ、**台帳を壊すと「実行系を組み直す」が出る**（裁定 91） | 窓を 3 枚順に起こす＝⑴ 印の無いデータ樹→**ランチャが焼くか**（20 s 待つ）⑵ 印が合っている→**出さないこと** ⑶ 私設台帳の**バイトを動かす**（`"probe_touched": true` を 1 つ足す＝檔は正しい JSON のまま sha256 だけ動く）→**出ること**（20 s 待つ）。⑴ でランチャが焼かなかったときは台本が自分で正しい sha256 を書いて⑵⑶ を続け、詳細に `guessed stamp = True` と残す＝**「焼く」と「見比べる」を別々の所見にする** |
| **k** | **起こせない `python.exe`** を指した実行系で「サーバ起動」→ **10 s 以内**に理由 1 行（§20-5 ⑴ の再現） | 私設の実行系の根に `runtime-<変種>\python.exe` を**テキスト檔**で置くだけ（26 B）。押下から状態帯の `失敗` か `StatusReasonText` の非空までを stopwatch で測り、**10 s を予算**にする（旧＝60 s 黙る）。走の間、主窓でない窓は**閉じて名前を記録する**＝§20-5 ⑴ が疑った「このアプリは PC で実行できません」の窓が出ていたら、それ自体が所見になる。子 0・ポート閉のままも数える |
| **l** | **「取得キャッシュを消す」ボタン**（裁定 90 Q-E2 ⑶） | 種を播いてから押し、20 s 以内に 0 バイトになることと、**データ樹そのものは残る**ことを数える。確認の窓が出たら 1 つ目のボタンで答え、出たことを印字する |

**押下 5 の残り半分**＝「成功した段が自動で次へ進む」ことは、**本当に取得する走でしか測れない**。
`-WizardFullRun` に隔離した（**E2E 席専用**・既定 off・完了の頁まで**何も押さずに**着くこと自体が証拠で、
その後の 1 押し＝「試し撃ちへ」で合計 5 になる）。既定の（取得しない）走で数えるのは
**失敗段までの 4 押し**と、**失敗段で「次へ」を押しても先へ進まない**ことの 2 つである。

**switch**＝`-DryRun`（空走＝窓を開かず段の下拵えを印字して exit 0）・`-RoundThreeOnly`（h／j／k／l だけ＝
門も模型も鯖も無し）・`-SkipRoundThree`・`-WizardFullRun`。私設ポートは **18097**（h／j／l＝1 度も listen しない）と
**18098**（k＝押すが開かない）。**8088・7861・18088 には触れていない**。

### 22-4 この席が実際に撃った物（窓は 1 枚も開けていない）

```
[構文] PowerShell 7  Parser::ParseFile                     PS7 parse OK
[構文] Windows PowerShell 5.1.26100.9168 同上              PS51 parse OK
[名寄せ] AST の CommandAst 全部を Get-Command で解決        every command resolves（檔内の function 32 本）
[空走] pwsh   -File probe/d-launch-probe.ps1 -DryRun       EXIT=0（'nothing was touched.'）
[空走] powershell.exe 同上                                 EXIT=0
[実走] 段の下拵え 12 本を AST で抜いて直に撃つ（窓なし・7 と 5.1 の両方で同じ出力）
```

下拵えの実走の逐語（`New-CacheFixture`／`New-PrivateAppTree`／`Edit-LedgerBytes`／`Remove-PrivateTree` ほか）＝

```
[cache] seeded ...\ywk-d3-helper-test\cache with 196608 B in 3 files
not empty yet -> ok=False bytes=196608      gone -> ok=True bytes=0
[tree] private app tree = ...\app (ledger json = 6)          <- vc_redist.json は写していない
[tree] broke ...\ledger\runtime-rocm-gfx1151.json (the first item has no sha256 any more)
junctions = 3   notices = True   presets = True
items = 107  count field = 107                                <- count と items は食い違わせない
item 1 has sha256 = False   has the renamed key = True        <- 壊れているのは 1 件だけ
items still holding a sha256 = 106
sha moved = True  still valid json = True  probe_touched = True
BOM free = True                                               <- settings.json は BOM 無しのまま
tree gone = True   build/out/app still has 4 entries (was 4)   licenses = 10 entries
```

**最後の 2 行が停止域の証明**である＝junction を含む私設樹を消した後も配布樹は無傷。

**段の数**＝`Add-Step` の呼び場所は檔全体で **102**（うち h 12・j 8・k 7・l 7＝例外の受け口と
`-WizardFullRun` 専用を含む）。既定の走で実際に数えられるのは **88 前後**を見込む
（便 D（2）の 58＋h 8＋j 7＋k 6＋l 6＋本流 3）。**確定値は統合席の実走の `=== N failure(s) of M ===` で置き換えること。**

**実測で判った 5.1 の落とし穴**＝**`pwsh` から `powershell.exe` を起こすと `PSModulePath` が 7 の物のまま入り、
`Microsoft.PowerShell.Utility` が自動読込されず `Get-FileHash` が「認識されません」で落ちる**
（この機体で再現）。`build/Common.ps1:100-119` が既に同じ理由で .NET の `SHA256` へ落ちる形を持っていたので、
`probe/` にも同じ形を入れた（`Get-Sha256Hex`）。**`probe/e-install-probe.ps1:461,483` は素のままである**
＝7 で撃つ限り無害だが、5.1 で撃つなら同じ手当てが要る（担当外なので触っていない）。

### 22-5 ランチャ席・統合席への申し送り（**新しい画面の名前を合わせてほしい**）

§21 がまだ無い時点で書いたので、**新しい部品は候補の `AutomationId` を順に引き、駄目なら文言で拾い、
拾った要素の実 id を必ず印字する**形にした（`Get-ElementLabel`）。実走の印字を見て、以下を**確定させて**ほしい。

| 何 | 台本が引く `AutomationId` の候補（この順） | 文言での拾い（保険） |
|---|---|---|
| 「取得キャッシュを消す」ボタン | `SettingsClearCacheButton`／`SettingsClearDownloadCacheButton`／`SettingsCacheClearButton`／`SettingsDeleteCacheButton`／`StatusClearCacheButton`／`MainClearCacheButton` | 名前に **`取得キャッシュを消す`** を含む要素（設定頁 → 状態頁の順に探す） |
| 「実行系を組み直す」の申し出 | `StatusRebuildRuntimeButton`／`StatusRebuildButton`／`StatusLedgerMismatchText`／`StatusRuntimeMismatchText`／`MainRebuildRuntimeButton`／`SettingsRebuildRuntimeButton` | 名前に **`実行系を組み直す`** を含む要素（状態頁で見つからなければ**設定頁を 1 度捲ってから**「無い」と言う＝WPF は選ばれていない `TabItem` の中身を作らないので、捲らずに「無い」と数えると**申し出が設定頁に在るときに嘘の緑**になる） |

**台本が当てにしている文言**（変えるなら台本の `$T` も一緒に動かすこと）＝
`概算`・`実測前の概算`・`実測`・`潜在参照`・`名あたり`（段 c）／`台帳`（段 h の失敗の理由）／
ウィザードの頁の題の `取得`・`完了`（段 h）／`失敗`（段 k）。
**`FirstRunStepTitle`・`FirstRunStepNumber`・`FirstRunMessageText`・`FirstRunNextButton`・
`FirstRunAcceptCheck`・`FirstRunVariantCombo` は今の綴りのまま使っている。**

**押下の数え方**（裁定 94 ⑴ を台本がどう数えるか）＝**同意チェック・「同意して次へ」・変種の選択・
「この構成で取得を始める」・「試し撃ちへ」の 5 つを押下 1 ずつ**とし、一覧を開いて中身を読む操作は数えない。
自動進行の実装で**変種の頁が消える**（既定を焼いて頁ごと飛ばす）なら押下は 4 になる＝**それは緩和ではなく
仕様変更**なので、台本の期待値ではなく卓の裁定を先に動かすこと。

### 22-6 `probe/e-install-probe.ps1` 段 3 の読み合わせ（**変更なしでよい**）

段 3（`Invoke-Stage3`・`e-install-probe.ps1:1140-1187`）が新 UI でも通るかを檔で確かめた。読むのは
**`FirstRunWizard`（窓）・`FirstRunStepTitle`・`FirstRunStepNumber`・`FirstRunNoticesBox`・`FirstRunAcceptCheck`**
の 5 つで、すべて**通知の頁**の物である。裁定 94 ⑴ の自動進行が触るのは**取得・展開・モデル・起動の 4 段**
＝通知の頁の部品ではない。よって**そのまま通る**、が結論。ただし**1 行だけ危ない**＝

- **`e-install-probe.ps1:1158-1159` の「頁の数えが `1 / 7`」**。`FirstRunStepNumber` は
  `((int)Step + 1) / ((int)FirstRunStep.Done + 1)` なので、**自動進行を「頁を畳む」形で実装すると分母が動き、
  この 1 段だけが赤くなる**（通知の読みそのものは無事）。**頁は畳まず、成功した段で自動的に次へ進める**なら
  分母は 7 のままで、`e-install-probe.ps1` は 1 行も直さなくてよい。畳むなら**この 1 行の期待値**を
  併せて直すこと（檔を持つ席の仕事＝この席は触っていない）。
- キャッシュの掃除を**起動時**に移すと、段 8c の `Test-DataBranch -Kept @(… 'cache' …)`（`:1597`）が
  「lost: cache」で赤くなりうる。**いまの段 8c はランチャを 1 度も起こさないので無事**だが、
  **掃除は「最初の 200 の後」に結び付けたまま**にしておくのが安全である（裁定 90 Q-E2 ⑶ の文言どおり）。

### 22-7 触っていない物

`upstream/`・`research/`・`voices/*.wav`・`tools/`・`probe/rocm-*`・`probe/cuda-*`・`probe/rtx-*`・
`probe/common.ps1`・`probe/e-install-probe.ps1`・`build/`・`server/`・`ledger/`・`licenses/`・
`launcher/`（読むだけ）・`decisions.md`・`docs/acceptance.md`・`docs/contract.md`・**本檔の §1〜§21**。
**利用者データ樹 `%LOCALAPPDATA%\irodori-tts-ywk`（17 GB）は退避すらしていない＝1 バイトも触っていない**
（窓を 1 枚も起こしていないので触れる走が無い）。ポートは 1 つも開けていない（`Assert-QuietPorts` に届く前に
`-DryRun` が抜ける）。管理者昇格なし・再起動なし。git の commit／push なし。第三者バイナリは 1 檔も足していない。
外へ 1 バイトも出していない。

### 22-8 卓へ／後続へ

1. **h・i・j・k・l は「まだ無い画面」を撃つ段である。**ランチャ席の実装が入る前に走らせれば
   **h の押下 4 と失敗の停止は通り**、**i・j・l は赤**、**k は §20-5 ⑴ の 60 s をそのまま再現して赤**になる。
   赤の中身が「要素が無い」なのか「振る舞いが違う」なのかは詳細に出る（`<not found>` か、拾った実 id か）。
   **統合席は実装の後に撃ち、赤が残ったら 22-5 の表で名前を合わせてから直すこと。**
2. **`-WizardFullRun` は外部取得（4.7 GB）を起こす。**この席の停止域では撃てないので、
   **押下 5 の確定は E2E 席（便 E の実走）に委ねる**。走らせるなら `-WizardFullRunTimeoutSeconds`
   （既定 1800）が完了までの上限で、`-Variant` に合わせて回線の実測から見積もること。
3. **段 i の「起動時に掃かない」は仕様の確認でもある。**`.part` を残すのは中断した初回取得を
   続きから取り直すためで（§20-1 ⑹）、**掃除を起動時に寄せるとその設計が崩れる**。
   台本は「1 射の前は残っている」を**段として**数えるので、実装がそこを変えたら赤で気付く。
4. **段 j の⑴ が赤で⑵⑶ が緑なら、「焼く手」だけが無い**＝`settings.json` に印を書く場所
   （初回取得の完了時か、起動時か）を決めれば済む。詳細の `guessed stamp = True` がその印である。
5. **段 k の予算 10 s は `-BadPythonBudgetSeconds` で動く。**§20-5 ⑴ の勧め（⒜ `AsyncRelayCommand` の
   全例外の受け口 ⒝ `ProcessRunner` の「起こす」段そのものの期限）が入れば **8 ms 級**で返るはずで、
   10 s は**十分に緩い**値として置いた（GPU 列挙の実測 2.22 s を含んでも余る）。
6. **`probe/README.md` の「31 段」は直した**（§20-5 ⑸ の宿題）。以後は**段数を檔に書かず**、
   `=== N failure(s) of M ===` を読む規約にした。

---

## 21. ランチャ席（便 D（3））の記帳（2026-09-05・opus サブ席）

裁定 **90 Q-E2 ⑶・91・92 の low 6 件・94 ⑴⑶** と、設計書 **§20-5 ⑴**（未解決で置いた 1 つ）を
実装した。**この節も実装の逐語**であって §1〜§8 の設計を覆さない。**他席の節（§9〜§20・§22）は
1 行も触っていない**。触った path＝`launcher/**` と本檔の本節だけ。

### 21-1 直した表（裁定ごと）

| # | 裁定 | 何が変わったか | 檔 |
|---|---|---|---|
| ⑴ | **94 ⑴** | **初回取得の自動進行**＝取得→展開→モデル→起動は成功したら自動で次へ進み、**失敗した段でだけ止まる**（文言は「もう一度」＋理由 1 行）。押下は **9 → 5**（同意チェック・同意して次へ・変種・取得を始める・試し撃ちへ）。段の出入りは `Trail` に `― <段の名>` として残す（自動でもどこまで進んだかが読める）。「中断」は各段で効く（取消は失敗として扱い、そこで止まる）。`Back` は失敗の札を下ろす | `FirstRunViewModel.AdvanceAsync`／`NextAsync`／`NextButtonText` |
| ⑵ | **90 Q-E2 ⑶** | **取得キャッシュの削除**＝⒜ `firstRunCompleted` が真の機体で「試し撃ち」が **200** を返したら `<data>\cache` を空にする（1 起動につき 1 度・消したバイトを**ログと Trail** に）⒝ 設定の手動ボタン「**取得キャッシュを消す（n GiB）**」（空なら押せない）⒞ **関門**＝`runtime\<変種>\python.exe` が在り、かつ `settings.runtimeLedgerSha256` が配布樹の台帳と一致するときだけ消す。どちらか欠ければ **1 バイトも消さず**理由 1 行 | `Services/Ledger/CacheCleaner.cs`（新設）・`MainViewModel.ClearCacheAfterFirstShot`・`SettingsViewModel.ClearCache`・`TryViewModel.Succeeded` |
| ⑶ | **91** | **`settings.json` に `runtimeLedgerSha256` と `installedAppVersion`**＝展開が通った直後に焼く。起動時に配布樹の台帳と突き合わせ、**台帳が違えば**状態帯に 1 行と「**実行系を組み直す**」1 手（cache から再展開・足りない原檔だけ取得から）。**版だけ違うときは急かさない**（「そのまま使えます」）。**焼き印の無い樹（便 D（2）以前・台本で組んだ樹）は起動時にいまの台帳で焼き直して黙る**＝正しく組んである機体に 4 GB の展開をやり直させない | `Services/Ledger/RuntimeStamp.cs`（新設）・`MainViewModel.CheckRuntimeStamp`／`RebuildRuntimeAsync`・`StatusViewModel.RebuildRuntimeCommand` |
| ⑷ | **94 ⑶** | **展開係数を変種ごとの実測に**（`ExpansionFactor` の一律 3.3 を廃止）＝`cu126` **1.69**・`rocm-*` **2.98**・`cpu` **3.60**（実測）・`cu130` **3.3**（未実測）。見積りの 1 行が「（展開は**実測** 2.98 倍）」「（展開は**推定** 3.3 倍）」と出し分ける | `FetchPlanner.ExpansionFactorFor`／`ExpansionFactor` レコード・`FetchPlan.Summary` |
| ⑸ | **92 の low 6** | 下の 21-2 の表 | 6 檔 |
| ⑹ | **§20-5 ⑴** | **窓が 60 秒黙る**の手当て＝⒜ `AsyncRelayCommand` の受け口を**全例外**に（型の数え上げをやめた）＋**`Faulted` を画面に繋いだ**（1 巡目・2 巡目は `Faulted` を**誰も購読していなかった**＝捕っても消えていた）⒝ `ProcessRunner`／`ServerProcess` に**「起こす」段そのものの期限 3 s**（`Process.Start` を別スレッドへ逃がして待ち、見限った個体は後から起きたら殺してから捨てる）⒞ `MainViewModel` は GPU 列挙が落ちても理由 1 行を残して先へ進む（門は「読めなかった検分」として扱える） | `Mvvm/RelayCommand.cs`・`Services/Gpu/GpuEnumerator.cs`・`Services/Server/ServerProcess.cs`・`MainViewModel.StartServerAsync` |

**low 6 件（裁定 92 が便 D（3）へ残した画面席の檔）**

| # | 何が壊れていたか | 直し方 |
|---|---|---|
| ⑴ | **`UiText.Bytes` が 1024 で割りながら `GB`／`MB`／`KB` と綴っていた**＝同じ配布物の中で `FetchPlanner.FormatBytes`（`GiB`）と状態帯（`GB`）が**同じ進法を別の名**で出していた | 綴りを **`GiB`／`MiB`／`KiB`** に（**数字は 1 つも動かしていない**） |
| ⑵ | **「（最大 …）」が行末**＝`GPU 全体 3.46 GB / 99.74 GB（最大 3.64 GB）` と並び、直前の分数に掛かる数に見えた（`max` は `max_memory_allocated`＝**使用量の山**）。`memory.error` は `「：」＋逐語` だけで、**出ている数字そのものの説明**に見えた | `使用量 1.73 GiB（最大 3.01 GiB）／占有量 …／GPU 全体 …` に。error は `（一部の欄が読めませんでした：<逐語>）` |
| ⑵' | **「未対応」1 語で 2 つの事情を出していた** | **`未対応`**＝答えた個体にその口が無い／**`—（サーバが動いていません）`**＝誰も答えていない、に分けた（`DescribeMemory` の第 2 引数＝`/ywk/status` が返ってきているか） |
| ⑶ | **`"latents": null` で NRE**＝`System.Text.Json` は**明示の null を初期化子より優先**するので、欄が来ない応答と `null` が来た応答で挙動が割れ、後者は `LatentCount` の 1 語で落ちた（描き直しは標本ごと＝**2 秒で窓が落ちる**） | 受けを `null` 許容にし、読みは `EffectiveLatents`（空表）に落とす |
| ⑷ | **`MainViewModel` が状態機械を通さず `Failed` を書く 2 箇所**（`python.exe` が無い／保存した GPU が居ない）＝`IServerProcess.State` は `Stopped` のままで、窓を開き直すと理由が消え、「サーバ起動」もまた押せた | `IServerProcess.ReportPreflightFailure(string)` を足し、**機械へ通してから**その現在値を配る（`MainViewModel.FailBeforeStart`） |
| ⑸ | **「GPU メモリの欄」の適用が次回起動まで効かない**＝窓が `StatusView.SetMemoryPanelVisible` を**構築時とウィザードを閉じたときだけ**叩いていた | `StatusViewModel.MemoryPanelVisible` に束縛（`ApplySettings` が正本）。窓から `SetMemoryPanelVisible` を落とした |
| ⑹ | **409 の待ち行列の取りこぼし**＝出し直す**前に** `_pendingPrecompute.Clear()` していたので、出し直しが 409 以外（サーバが落ちた・期限切れ・停止直後）で落ちると**覚えていた話者が消えた**＝直したはずの筋（§19）がそのまま残っていた | **受け取られるまで落とさない**（`PrecomputeOutcome` を返す）・**出し直しは二重に飛ばさない**（in-flight の札）・**409 以外で 3 回落ちたら諦めて理由を残す**（標本ごとに叩き続けない） |

### 21-2 実測（この機体・rocm-gfx1151・**ポートは 1 つも開けていない**）

**⑴ 見積り（裁定 94 ⑶ の効き）**＝配布樹の台帳の実値から算術で出した（外へ 1 バイトも出していない）。

| 変種 | 落とすバイト | 旧（一律 3.3） | 新（変種ごと） | 係数 | 差 |
|---|---|---|---|---|---|
| `cu130` | 5.26 GiB | 11.56 GiB | **11.56 GiB** | 推定 3.3 | ±0 |
| `cu126` | 5.93 GiB | 14.45 GiB | **10.29 GiB** | 実測 1.69 | **−4.16 GiB** |
| `cpu` | 3.62 GiB | 4.53 GiB | **4.61 GiB** | 実測 3.60 | +0.08 GiB |
| `rocm-gfx1151` | 4.79 GiB | 9.55 GiB | **9.09 GiB** | 実測 2.98 | −0.46 GiB |

**cu126 の 14.45 GiB は実測の 1.7 倍を要求していた**（便 E（2）の実測＝展開後 8.38 GB）。
`cpu` だけ増えるのは実測 3.60 が丸めの 3.3 より大きいからで、**低めに出さない側へ倒す**方針は変わらない。

**⑵ 起こせない `python.exe`**（無人検分の段 k の材料＝テキスト檔に `python.exe` の名を付けた物）＝
**コンソールから** `Process.Start` を撃つと **16.2 ms** で `Win32Exception`
（`The specified executable is not a valid application for this OS platform.`）＝
§20-5 ⑴ の「18 ms」と同じ。**窓の側だけで 60 秒黙る**という形は変わっていないので、
**期限は「窓でも止まらない」ことの保険**として置いた（3 s × 3 回＝9 s < 段 k の budget 10 s）。
**なぜ 3 回か**＝1 度の「サーバ起動」で同じ `python.exe` を**⑴ 窓の GPU 列挙 ⑵ 変種の門の検分
⑶ サーバの子**と 3 回起こすからである（`MainViewModel` → `GpuEnumerator` → `TorchProbeRunner`／
`ServerProcess.DecideGateAsync` → `ServerProcess` の spawn）。

**⑶ 検分**

```
dotnet build launcher\IrodoriTtsYwk.sln --nologo --no-incremental   0 個の警告 / 0 エラー
dotnet test  launcher\IrodoriTtsYwk.sln --nologo                    554 passed（499 → +55）
  うち RoundThreeTests.cs                                            55 passed（144 ms）
build\check-tree.ps1                                                 A-7 PASSED（4 KB 超・第三者バイナリ 0・submodule clean）
```

### 21-3 釘（`IrodoriTtsYwk.Launcher.Tests/RoundThreeTests.cs`・新規 1 檔・55 本）

| 群 | 本数 | 何を押さえるか |
|---|---|---|
| `RoundThreeWizardTests` | 6 | 押下 **5** で完了まで通る／失敗した段で止まり「もう一度」＋理由／もう一度で直れば**そこから先も自動**／段の進みが `Trail` に残る／取消は先へ進めない／展開が通ると焼き印が檔にも落ちる |
| `RoundThreeStampAndCacheTests` | 10 | 台帳が違えば組み直す 1 行／合っていれば黙る（sha256 の大小は問わない）／版だけなら急かさない／実行系が無い・焼き印が無い・台帳が読めないときは黙る／台帳 1 字で sha256 が動く／**置き場の中身を残さず消す**（入れ子も `.part` も・置き場自身は残す）／`python.exe` が無ければ 1 檔も消さない／sha256 が食い違えば 1 檔も消さない／量は入れ子まで数える／設定の手動ボタンは量を出して消し、空なら押せなくなる |
| `RoundThreeExpansionTests` | 4（＋5 例） | 変種ごとの実測係数／大小と綴りの揺れ／「実測 2.98 倍」「推定 3.3 倍」の出し分け／必要な空きが変種ごとに変わる |
| `RoundThreeLowSixTests` | 12（＋4 例） | GiB の綴り／最大は使用量の隣／未対応と「動いていません」の出し分け／error の文言／帯は答えの有無で文言を変える／`latents: null` で落ちない／GPU メモリの欄は適用の瞬間に効く／**手は全例外を受ける**（`Win32Exception`）／取消は失敗にしない／文言の無い例外は型名／起こせない実行檔は期限より早く理由 1 行／期限は 3 s（3 回で 9 s < 10 s） |
| `RoundThreePrecomputeQueueTests` | 3 | **出し直しが落ちても覚えたまま**／3 回落ちたら諦めて理由／走行中の標本では出し直さない |
| `RoundThreeMainViewModelTests` | 10 | 事前検査の断り 2 種が**状態機械を通る**／列挙が落ちても理由がログに残る／手が落ちても理由 1 行／台帳が違えば 1 手が出て、直せば消える／**焼き印の無い樹は起動時に焼き直して黙る**／実行系が無い樹には押さない／試し撃ちの 200 で cache が消える／初回取得未了なら消さない |
| `RoundThreeRebuildTests` | 3 | cache が揃っていれば**取得へ出ない**／足りない檔だけ取り直す／展開系が無ければ焼き印を動かさず理由を残す |

**既存檔で直した釘は 5 本＋偽物 1 つだけ**（綴りと文言が動いた物＝`ViewModelsTests`（UiText の 3 例と 2 本）・
`RoundTwoUiTests`（帯 3 本）・`CorrectionSeatVoicesTests`（「やり直す」→「もう一度」）・
`LedgerServiceTests`（係数）・`WrapperClientTests`（`Latents` が null 許容になった）と、
`IServerProcess` に口が増えた `RoundTwoIntegrationTests.CapturingServer`）。
`RoundTwoIntegrationTests` には `[Collection]` を 1 行足した（`AppServices` を触る檔を並走させない）。

### 21-4 触っていない物

`upstream/`・`research/`・`voices/`・`tools/`・`ledger/`・`licenses/`・`server/`・`build/`・`probe/`・
`installer/`・`decisions.md`・`docs/contract.md`・`docs/acceptance.md`・**本檔の §1〜§20 と §22**。
`build/check-tree.ps1` は**実走しただけ**。git の commit／push はしていない。第三者バイナリは 1 檔も
足していない。**外へ 1 バイトも出していない**（偽の取得系は檔を 1 つも落とさない）。
起こした子プロセスは **`Process.Start` が例外で返る 1 回**（テキスト檔・16.2 ms）だけで、
**8088・7861・18088 は一度も起こしていない**。

### 21-5 卓へ／後続へ

1. **`docs/contract.md` ⑻ に 2 行足すこと**（本席の担当パス外）＝`runtimeLedgerSha256`（小文字 hex 64 字・
   **展開に使った**台帳の sha256・null 可）と `installedAppVersion`（`v0.1.0` の形・null 可）。
   `schema` は上げていない（欄を足すだけ＝契約 ⑻ の規律）。
2. **裁定 90 の「台帳の item の檔」を「置き場ごと」に読み替えた**（本席の判断・卓の追認が要る）。
   理由＝`<data>\cache` に書くのは取得系だけだが、**そこに残るのは今の台帳が名指す檔だけではない**
   （⒜ 前の台帳の原檔 ⒝ 打ち切った `.part` ⒞ 名前が変わった檔）。名前で選ぶとこの 3 つが残り、
   受け入れ条件の「展開後（**cache 削除後**）≤ 9.0 GB」の実測が合わない。**関門**（`python.exe` の在否と
   台帳の sha256 の一致）を通った後に置き場ごと空にする形にしてある＝「取り直しが要る機体からは
   1 バイトも取らない」という 裁定 90 ⑶ ⒞ の主旨は保っている。`CacheCleaner.FileNames` は
   **削除には使っていない**（今の台帳の物が何件かを言うためだけに残した）。
3. **「起こす」段の期限を 3 s にした理由**（§20-5 ⑴ の勧めは「例 10 s」）＝1 度の押下で同じ
   `python.exe` を 3 回起こすので、10 s では無人検分の段 k（budget 10 s）に収まらない。
   `CreateProcess` は健全な機体で ms の仕事なので 3 s でも 100 倍以上の余裕があるが、
   **AV が重い機体で健全な `python.exe` の spawn が 3 秒を超えたら**「起こす段が返らない」と
   誤って告げる。値を動かすときは `ProcessRunner.DefaultStartTimeout` の 1 箇所
   （`ServerProcess.SpawnTimeout` はこれを見ている）と、段 k の `-BadPythonBudgetSeconds` を
   一緒に見ること。
4. **`UiText.Bytes` の綴りが変わったので、状態帯の見た目が `2.21 GB` → `2.21 GiB` になる**
   （数字は 1 つも動いていない）。§17-2・§20-2・§19 に載っている実射の逐語は**当時の綴りのまま**で
   正しい（本席は他席の節を書き換えない）。無人検分の台本は単位を見ていないので影響しない。
5. **`memory.error` の添え方を変えた**＝`（一部の欄が読めませんでした：<逐語>）`。wrapper の 1 行は
   畳んでいないので、契約 ⑹ の `error` の意味は変えていない。
6. **未実射で残る**（実窓・実 GPU は統合席と E2E 席の担当）＝⒜ 自動進行の**本物の 1 周**（取得 4.79 GiB を
   本当に落とす走＝E2E 席）⒝ 段 k（起こせない `python.exe`）を**窓で**撃ったときの実測（期限が本当に
   効くか・シェルの窓が出ないか）⒞ 段 j（焼き印）の 3 窓 ⒟ 段 l（手動ボタン）⒠ cu130 の展開後サイズ
   （RTX 機・実測が出たら `FetchPlanner.ExpansionFactorFor` の `cu130` を実測へ移すこと＝1 行）。
7. **`FirstRunViewModel.Note` は「ウィザードがまだ開いていれば」Trail に足す口**である
   （`MainViewModel` が最後に作った 1 個体を持つ）。試し撃ちが**ウィザードを閉じた後**なら
   ログにだけ残る＝裁定 90 の「Trail と log に」は**開いていれば両方**という形で満たしている。

## 23. 統合席（便 D（3））の記帳（2026-09-05・opus サブ席）

> 本節は**この席が実際に撃った物だけ**を書く。読んだだけ・推したものは「推測」と明記する。
> 触った path＝`launcher/**`（下の 23-2 の 4 檔）と**本節**とスクラッチパッドだけ。
> `probe/`・`build/`・`installer/`・`server/`・`ledger/`・`licenses/`・`voices/`・`decisions.md`・
> `docs/contract.md`・`docs/acceptance.md`・**本檔の §1〜§22** は**読むだけ／実走しただけ**。
> `git commit`／`push` はしていない。第三者バイナリは 1 檔も足していない。**管理者昇格 0 回・再起動 0 回**。
> **8088・7861・18088 は一度も起こしていない**（使った私設ポートは 18094・18097・18098・18099）。
> **外部への取得を撃ったのはこの席である**（裁定どおり＝E2E の 1 走だけ・実測は 23-6）。
> 利用者データ樹（**16,990,584,628 B・53,769 檔**）は E2E の前に `Rename-Item` で
> `irodori-tts-ywk.d3-backup` へ退避し、走り終えてから戻した＝**名と長さの差 0**（23-8）。

### 23-1 撃った物と結末（一覧）

| # | 撃った物 | 結末 | 所要 |
|---|---|---|---|
| ⑴ | `dotnet build launcher\IrodoriTtsYwk.sln --nologo --no-incremental` | **0 個の警告・0 エラー** | 3.4 s |
| ⑵ | `dotnet test launcher\IrodoriTtsYwk.sln --nologo` | **失敗 0・合格 559**（便 D（3）ランチャ席の 554 ＋ 本席の 5） | 3 s |
| ⑶ | `build\run-tests.ps1` | **339 passed**・`upstream/` 2 本とも clean・**EXIT=0** | 12 s |
| ⑷ | `build\check-tree.ps1` | **A-7 PASSED**・EXIT=0（本席の編集の後にもう 1 度撃って同じ） | 1 s |
| ⑸ | `build\check-licenses.ps1` | **A-6 PASSED**（13 檔）・EXIT=0（同上） | 1 s |
| ⑹ | `build\assemble-app.ps1` | **108 檔**・プリセット 11 wav 32,571,364 B・upstream clean | 7 s |
| ⑺ | `probe\d-launch-probe.ps1 -RoundThreeOnly`（**実装の直後・1 回目**） | **1 failure of 29**＝段 k の「シェルの窓」（23-2） | ≈1.2 分 |
| ⑻ | 同上（**直した後・2 回目**） | **0 failure of 29**（段 k は 1.25 s → **0.44 s**・窓 0 枚） | ≈1.3 分 |
| ⑼ | `probe\d-launch-probe.ps1`（**全段・実 ROCm・私設 18094／18097〜18099**） | **0 failure of 88** | ≈3.7 分 |
| ⑽ | `build\installer-build.ps1 -All` | **DONE: 2 installer(s), 20 gate(s), 0 failed.** | **42 s**（13:13:43→13:14:25） |
| ⑾ | `probe\e-install-probe.ps1`（既定の段 1・2・3・5・6・7・8） | **0 failure of 72**・データ樹はバイトで戻った | ≈1.3 分 |
| ⑿ | **E2E＝まっさらな機体の初回取得（CUDA 版 setup・cpu 変種・本物の取得）** | **0 failure**・**押下 5**・cache 294,382,715 B → **0 B** | **199.57 s** |
| ⒀ | 製品の路の「試す」（10 steps／40 steps・別々の走） | **RTF 1.53／3.33**（どちらも 200） | 各 1 分 |

**⑺ の 1 件が本席の見つけた割れである**（他は全部 1 発で緑）。

### 23-2 直した割れ＝**ハードエラーの窓が `Process.Start` を止めていた**（設計書 §20-5 ⑴ の正体）

**⑴ 見つかり方**＝検分席が置いた段 k（起こせない `python.exe` を実窓で撃つ）を実装の直後に走らせたら、
段は緑だが**「シェルの窓を残していない」だけが赤**だった。台本が閉じた窓の逐語＝

```
[dialog] サポートされていない 16 ビット アプリケーション (id=)
[dialog] サポートされていない 16 ビット アプリケーション (id=)
[dialog] サポートされていない 16 ビット アプリケーション (id=)
[badpython] state  = 失敗 after 1.25 s
[FAIL] no modal of the shell was left on the screen  windows closed = 3
```

**3 枚**なのは 1 度の「サーバ起動」で同じ `python.exe` を 3 回起こすから（§21-5 の 3）。
検分席が「§20-5 ⑴ が疑った窓が出ていたらそれ自体が所見」と書いた、その所見である。

**⑵ 実測でつかまえた機序**（コンソールから 3 通り・スクラッチパッドの `hardtest.ps1`／`hardtest2.ps1`）＝

| 撃ち方 | プロセスのエラーモード | `Process.Start` | 出た窓 |
|---|---|---|---|
| そのまま（PowerShell から） | **0x8003**（`SEM_FAILCRITICALERRORS`＋`SEM_NOGPFAULTERRORBOX`＋`SEM_NOOPENFILEERRORBOX`） | **18.9 ms** で `Win32Exception` | **0 枚** |
| `SetErrorMode(0)` してから | **0x0000** | **返ってこない**（40 秒で打ち切っても返らず・窓を閉じるまで待つ） | **1 枚**（「サポートされていない 16 ビット アプリケーション」） |
| `SetErrorMode(0)`＋`SetThreadErrorMode(0x8001)` | 0x0000（スレッドだけ 0x8001） | **12.7 ms** で `Win32Exception` | **0 枚** |

**これが §20-5 ⑴ の「窓は 60 秒黙る／コンソールは 18 ms で返る」の差の正体である**＝
WPF のメッセージポンプの問題ではなく、**撃った個体のエラーモードの差**であった
（PowerShell は 0x8003 を持っているので窓が出ず、素の WPF アプリは 0 なので出る）。
そして**窓が出ている間 `Process.Start` は返らない**＝待つ側にいくら期限を掛けても、
窓を誰かが押すまでその子は生きたまま残る。1 巡目・2 巡目の「60 秒」は、
**無人検分の台本が 200 ms ごとに窓を閉じていたから 60 秒で済んでいた**とも読める。

**⑶ 直し方**（`launcher/**` の 4 檔）

| 檔 | 何をしたか |
|---|---|
| `Services/Gpu/GpuEnumerator.cs` | `ProcessRunner.StartAsync(Process)` と `ProcessRunner.StartWithoutHardErrorBox(Process)` を足した＝`SetThreadErrorMode(SEM_FAILCRITICALERRORS｜SEM_NOOPENFILEERRORBOX)` を掛けてから `Start` を撃ち、**必ず元へ戻す**。`RunAsync` の「起こす」段をこの口に差し替えた |
| `Services/Server/ServerProcess.cs` | 子の spawn を `Task.Run(process.Start)` から `ProcessRunner.StartAsync(process)` へ |
| `Services/Models/ModelFetcher.cs` | モデル取得の子も同じ口へ（`StartWithoutHardErrorBox`＝ここは子を待つ呼び手なので同じスレッドで撃つ） |
| `IrodoriTtsYwk.Launcher.Tests/RoundThreeIntegrationTests.cs`（新設） | 釘 5 本（下） |

**なぜスレッド単位か**（`SetErrorMode` ではなく `SetThreadErrorMode`）＝プロセスのエラーモードは
**起こした子が引き継ぐ**。窓を出さない約束を wrapper の python にまで広げたくない。
`Task.Run` のスレッドはプールの使い回しなので、戻すのは `finally` で行う。

**`VcRedistInstaller` は触っていない**＝あれは `UseShellExecute=true`＋`Verb=runas` で、
出る窓は UAC であり、この機序ではない（触ると UAC の路を動かすことになる）。

**⑷ 直した後の実測**（同じ段 k・同じ材料）

```
[badpython] state  = 失敗 after 0.44 s      （1.25 s から）
[PASS] no modal of the shell was left on the screen  windows closed = 0
[PASS] the failed start left no child behind  children=0
[PASS] the failed start never opened the port  port 18098
```

**「起こす」段の期限 3 s（§21-5 の 3）は残す**＝窓を止めたので普通は 8〜20 ms で返るが、
`SetThreadErrorMode` が効かない OS／効かない種類のハードエラーが在ったときに、
**返らない `Start` を待ち続けない**保険はやはり要る。値は動かしていない。

**⑸ 釘 5 本**（`RoundThreeIntegrationTests.cs`）＝⒜ 起こせない実行檔は窓を待たずに `Win32Exception`
（3 s 未満）⒝ **掛けるのはスレッドのエラーモードで、プロセスの物は動かさない**（`GetErrorMode` が前後で不変）
⒞ 起こせた個体でもスレッドのエラーモードは元へ戻る ⒟ 別スレッドで撃つ口（`StartAsync`）も同じ始末をする
⒠ 列挙の口（`RunAsync`）は**例外を投げず**理由 1 行で返る。
※ 宿主（`dotnet test`）のエラーモードは 0x8003 なので、**この釘は「窓が出るか」ではなく「掛け方が正しいか」**
を押さえる（窓そのものは無人検分の段 k が実窓で撃つ＝上の ⑷）。

### 23-3 無人検分（`probe/d-launch-probe.ps1`）＝**88 段が全部緑**

検分席の見込み（88 前後）は当たった＝**`=== 0 failure(s) of 88 ===`**（EXIT=0）。
新しい段（h・i・j・k・l）の逐語で、この便の裁定が実窓で立っていることが読める。

```
h  [wizard] press 1..4 → [PASS] the presses up to the failed step are four
   [wizard] message = 取得台帳が読めないので取得を始められません（… sha256 の無い item がある …：absl-py）。
   [PASS] a first run that failed is not written down as done  firstRunCompleted = False
i  [cache] seeded … 196608 B in 3 files → [PASS] the cache is still there when the shot is fired
   [PASS] the download cache is empty once the shot came back 200
j  [ledger] runtimeLedgerSha256 = ceec8560c678f456120ef720b73615a98da00b3830e5a6c276a736b37a04a80f
   [ledger] installedAppVersion = v0.1.0
   [PASS] a launcher that finds no stamp writes one   guessed stamp = False
   [PASS] the same ledger, the same stamp, and still no offer
   [ledger] offer = [Button] id='StatusRebuildRuntimeButton' name='実行系を組み直す'
k  [badpython] state = 失敗 after 0.44 s ・ windows closed = 0        （23-2）
l  [cache] found on TabSettings :: [Button] id='SettingsClearCacheButton' name='取得キャッシュを消す（192.0 KiB）'
   [cache] 196608 B -> 0 B in 0 s ・ 押した後の名は '取得キャッシュを消す（空です）'
```

**名前は 1 つも食い違わなかった**（検分席の §22-5 の候補表の第 1 候補がそのまま当たった）＝
`StatusRebuildRuntimeButton`・`SettingsClearCacheButton`。表を直す必要はない。

本流（実 ROCm・私設 18094）の実測＝

```
[gpu] 0: AMD Radeon(TM) 8060S Graphics（99.7 GB）           列挙は 1 回
[wizard] size line = 取得 4.79 GiB・必要な空き 9.09 GiB（展開は実測 2.98 倍）
                     （実行系 1.44 GiB・モデル 3.33 GiB・vc_redist 24.4 MiB）      ← 裁定 94 ⑶
[start] state=待機 after 31 s                                                     ← D-6（≤ 120 s）
[status] memory = 使用量 1.78 GiB（最大 3.64 GiB）／占有量 2.06 GiB／GPU 全体 2.36 GiB / 99.74 GiB
                                                                                  ← 裁定 92 low ⑴⑵（GiB・最大の置き場）
[latent] ON（焼いた話者 0 名・合計 0 B）
[try] result = 所要 7.31 秒／出力 1.36 秒／RTF 5.37（1 射目）／所要 4.75 秒／出力 3.64 秒／RTF 1.30（日本語名の話者）
[gate] cu130 → 失敗 after 3.39 s・「cu130 はこの機体で GPU を見られません（is_available=False・device_count=0）。
       rocm-gfx1151 か cpu の変種に切り替えてください。」                          ← 裁定 88 ⑴
```

**この走で `8088`・`7861`・`18088` は一度も起こしていない**（台本の preflight と teardown が両端で数える）。

### 23-4 生産ライン（`build/installer-build.ps1 -All`）＝**2 版・門 20 本・0 failed**

```
DONE: 2 installer(s), 20 gate(s), 0 failed.
artefact cuda   = irodori-tts-ywk-setup-v0.1.0-cuda.exe    85,000,444 B
                  sha256 0718df38d2b67a7cb41274e3bfb23d0aff55d1ab6d31967c45c649da1333097a
artefact radeon = irodori-tts-ywk-setup-v0.1.0-radeon.exe  85,014,911 B
                  sha256 5794f634d447617352a23ad8f4f4ed13eaa4dc6a3bd901315fd58c843f178afe
発行 exe        = build\out\launcher\win-x64\IrodoriTtsYwk.Launcher.exe  69,621,034 B
```

便 E（2）の最終の 2 本（84,986,833 B／85,001,303 B・§14-1）との差は **+13,611 B／+13,608 B**＝
本席の直し（23-2）と便 D（3）の他席の実装が焼き込まれた分である。
**サイズ帯の門（B-2＝77.0〜85.0 MiB）は 81.06／81.08 MiB で通っている**。

### 23-5 検分台本（`probe/e-install-probe.ps1`）＝**72 段が全部緑**

`=== 0 failure(s) of 72 ===`（EXIT=0・なま値は `build/out/probe-log/e-install-20260905-131443`）。
**段 3（通知の頁）は 1 行も直さずに通った**＝検分席の読み合わせ（§22-6）どおり、
裁定 94 ⑴ の自動進行は**頁を畳まない**ので `FirstRunStepNumber` の分母は 7 のままである。
**段 8c（`Test-DataBranch -Kept … 'cache' …`）も緑**＝キャッシュの掃除を「最初の 200 の後」に
結び付けたまま（起動時に掃かない）ので、§22-6 の危惧は現実になっていない。
利用者データ樹（**16.99 GB**）は台本自身が退避して戻し、
`[PASS] the operator data tree came back byte for byte (count, bytes and every name)`。

### 23-6 E2E＝**まっさらな機体の初回取得（CUDA 版 setup・cpu 変種・本物の取得）**

台本＝スクラッチパッドの `ben-d3/e2e/e2e-d3-cpu.ps1`（便 E（2）の E2E 台本を写して 3 点だけ直した＝
⒜「もう一度」の綴り ⒝ **働く段では「次へ」を押さない**（押されたらそれ自体が所見）
⒞ キャッシュを射の前後でバイトで読む）。**利用者データ樹は空**（models も無い）から始めた。

**⑴ 導入**（`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`・`/DIR` は付けない）

```
[install] irodori-tts-ywk-setup-v0.1.0-cuda.exe exit=0 sec=2.9 log=616 lines
[install] User privileges: None ／ Administrative install mode: No ／ Install mode root key: HKEY_CURRENT_USER
[install] {app} = C:\Users\mugonkun\AppData\Local\Programs\irodori-tts-ywk
[install] {app} files=112 bytes=111,149,939
```

**UAC は 0 回**（便 E（2）§15-1 と同じ・`{app}` は exe の分だけ +11,500 B）。

**⑵ 設定は「ウィザードの前に」データ樹へ置いた**（`AppPaths` の読む場所の確認）＝

```
[settings] C:\Users\mugonkun\AppData\Local\irodori-tts-ywk\settings.json  port=18097 variant= firstRunCompleted=False
[serve] port 18097 listening = True   （＝置いた 18097 で立った。既定の 18088 は一度も開いていない）
```

**⑶ ウィザード**（窓は起動 1.4 s で自分から開いた）

```
[wizard] step = 1 / 7  初回取得の前に（第三者物の通知）
[wizard] notices shown = 16509 chars, file = 16509 chars      ← 裁定 46（1 文字も違わない）
[wizard] step = 2 / 7  実行系の種類を選ぶ
[wizard] variant choices = cu130 | cu126 | cpu                ← CUDA 版の 3 択
[wizard] picked   = cpu
[wizard] name     = CPU（遅い・配信用途では非推奨）
[wizard] note     = CPU は GPU の数百分の一の速さです。配信用途では勧めません（試すためだけの選択肢です）。
[wizard] driver   = ドライバの下限はありません。
[wizard] size     = 取得 3.62 GiB・必要な空き 4.61 GiB（展開は実測 3.6 倍）
                    （実行系 280.7 MiB・モデル 3.33 GiB・vc_redist 24.4 MiB）
```

**⑷ 段ごとの時計**（`FirstRunStepNumber` の変わり目・**押下は 1 度も挟んでいない**）

| 段 | 逐語（Trail） | 所要 |
|---|---|---|
| 3/7 取得（実行系） | `msvcp140.dll は既に在る（14.51.36247.0）ので飛ばす。`／`実行系を取得しました（102 件）。` | **2.96 s** |
| 4/7 展開 | `展開しました（25012 檔・903.3 MiB）。` | **25.58 s** |
| 5/7 取得（モデル） | `モデルを取得しました。` | **73.19 s**（**本物の 3.33 GiB**） |
| 6/7 起動の確認 | `サーバが起動しました。` | **20.93 s**（CPU で ready まで） |
| 7/7 完了 | `初回取得が終わりました。` | — |

**働く段の 4 つは自動で繋がった**＝Trail に `― 取得（実行系）`／`― 展開`／`― 取得（モデル）`／
`― 起動の確認` の 4 行が残り（裁定 94 ⑴ の「どこまで進んだかは自動でも読める」）、
**働く段で「次へ」が押せる状態になったことは 1 度も無い**（`presses OFFERED inside the work steps = 0`）。

**⑸ 押下＝5**（**裁定 94 ⑴ の目標を実窓で満たした**）

```
[wizard] operator PRESSES counted = 5  (accept / agree / variant / start the fetch / to the try screen)
```

便 E（2）の実測は **9**（§15-7）＝**4 押下減った**。数え方は緩めていない（押下のまま）。

**⑹ 取得キャッシュ**（裁定 90 Q-E2 ⑶）

```
[cache] before the shot: files=102 bytes=294,382,715      ← 1 射の前は残っている（.part の続きの為）
[http]  POST /v1/audio/speech -> 200  bytes=142,124  RIFF/WAVE=True
[cache] after the shot:  files=0   bytes=0  (waited 0 s)  ← 200 の後に空
```

**置き場そのものは残った**（`<data>\cache` は在る）。データ樹の終端＝
`models 3,571,273,952 B ／ runtime 966,099,983 B ／ voices 32,576,389 B ／ cache 0 ／ logs 0 檔`
＝**4,569,951,086 B（4.26 GiB）**。

**⑺ 展開の係数の実測**（裁定 94 ⑶ の材料）＝**ランチャが展開した cpu の樹は 3.23 倍**である。

| 何 | 檔 | バイト | 対 台帳 |
|---|---|---|---|
| 台帳（`ledger/runtime-cpu.json`） | 101 item | 283,249,109 B（270.1 MiB） | 1.00 |
| ランチャが cache へ落とした原檔 | 102 | 294,382,715 B（280.7 MiB） | 1.04 |
| **ランチャが展開した `<data>\runtime\cpu`** | **25,262** | **950,608,342 B（906.6 MiB）** | **3.23**（原檔比） |
| `build\out\runtime-cpu`（`assemble-runtime.ps1` が組む樹） | 28,373 | 1,020,237,457 B（973.0 MiB） | **3.60** |

`FetchPlanner` の cpu の係数 **3.60 は「台本が組む樹」の実測**であり、
**ランチャ自身が展開する樹はそれより小さい**（`WheelInstaller` が `MinimalDistInfo` で書くぶん
3,111 檔・69.6 MiB 少ない）。よって見積りは **0.10 GiB ほど多め**に出る＝
**低めに出さない側**なので実害は無い（23-9 の 3 に票として残す）。
（差は **3,111 檔・69,629,115 B＝66.4 MiB**。）

**⑻ 停止とアンインストール**

```
[stop] stray python = 0 ／ [stop] port 18097 still listening = False
[uninstall] exit=0 sec=1.02 log=171 lines
[uninstall] key gone = True  {app} gone = True  lnk gone = True
[uninstall] Removed all? Yes
[uninstall] Defaulting to No for suppressed message box (Yes/No):   ×2   ← M-8（2 つの問いが「残す」に落ちた）
```

**⑼ 時計の総和**＝`preflight` から `uninstalled` まで **199.57 s**。
うち**ウィザードの働く段が 123.64 s**（取得 2.96＋展開 25.58＋モデル 73.19＋起動 20.93）。
**モデル 3,571,273,952 B を 73.19 s** ＝ **48.8 MB/s**（この機体の回線・実測）。
受け入れ条件「オンライン導入 ≤ 10 分（100 Mbps 級）」への引き直しは
**12.5 MB/s なら cpu 変種で ≈6.0 分**（3.62 GiB＝3,886,946,406 B ÷ 12.5 MB/s＝311 s ＋
展開 25.6 s ＋ 起動 20.9 s ＋ 導入 2.9 s ＝ 360 s）＝**推測**（帯域を絞った走は撃っていない）。

### 23-7 「試す」の 1 射（製品の路・CPU）

E2E の中の 1 射は 200 で返り cache も消えたが、台本が結果の行を**プレースホルダの `—` のまま**読んで
しまい RTF が採れなかった（便 E（2）§15-5 と同じ罠＝台本の瑕疵であって製品の瑕疵ではない）。
そこで**同じデータ樹に対して導入→1 射→アンインストールを 2 回**撃ち直した（`try-shot.ps1`）。

| 走 | steps | 状態 | 逐語 |
|---|---|---|---|
| A | **10** | 待機 after **15.34 s** | `所要 4.84 秒／出力 3.16 秒／RTF 1.53／seed 204573932289323369` |
| B | **40**（設定の既定） | 待機 after **15.37 s** | `所要 10.51 秒／出力 3.16 秒／RTF 3.33／seed 1377071445224504460` |

- **受け入れ条件「CPU（fp32・OMP=8）＝RTF ≤ 4.0」は 40 steps の本物の路で満たした**（3.33）。
  便 E（2）の W-1（`IRODORI_MODEL_DEVICE=cpu` で wrapper を直に起こした 10 steps・RTF 1.44）と
  **10 steps 同士で 1.53 対 1.44** ＝同じ帯である。
- 状態帯の逐語＝`device = cpu／fp32`・`variant = CPU（遅い・配信用途では非推奨）`・
  **`memory band = CPU（GPU メモリなし）`**（裁定 92 low ⑵' の出し分け＝「未対応」でも
  「—（サーバが動いていません）」でもない第 3 の枝が CPU で出ることを実窓で確認した）・
  `latent = OFF（焼いた話者 0 名・合計 0 B）`。
- `settings.json` は **`runtimeLedgerSha256: "0ac4f002ce31a677be05326301b813790962aff2f4ab5399e7edf1208a022c26"`**
  と **`installedAppVersion: "v0.1.0"`** を持っていた（裁定 91＝展開の直後に焼かれ、
  2 度目・3 度目の起動でも「実行系を組み直す」は出なかった）。

### 23-8 後片付けの証拠（走の前と後で突合）

| 何 | 前 | 後 |
|---|---|---|
| 利用者データ樹 | **53,769 檔・16,990,584,628 B** | **53,769 檔・16,990,584,628 B**（**名と長さの差 0**） |
| `%LOCALAPPDATA%` の `irodori*` | `irodori-tts-ywk` の 1 つ | `irodori-tts-ywk` の 1 つ（退避物は残っていない） |
| アンインストール鍵（HKCU） | 0 | **0** |
| `%LOCALAPPDATA%\Programs\irodori-tts-ywk` | 無し | **無し** |
| スタートメニューの `*irodori*.lnk` | 0 | **0** |
| listen（8088／7861／18088／18094／18097／18098／18099） | 0 | **0** |
| `IrodoriTtsYwk.Launcher` / `ywk_server` の python | 0 / 0 | **0 / 0** |

突合の生データ＝スクラッチパッドの `ben-d3/data-tree-before.txt`／`data-tree-after.txt`
（53,769 行・`<相対パス>|<長さ>` を並べ替えて `Compare-Object`＝**差 0 件**）。

### 23-9 卓へ／後続へ

1. **`docs/contract.md` ⑻ の 2 行はまだ書かれていない**（§21-5 の 1 と同じ票）＝
   `runtimeLedgerSha256`（小文字 hex 64 字・null 可）と `installedAppVersion`（`v0.1.0` の形・null 可）。
   本席の担当パス外なので触っていない。**E2E で実物が出た**（23-7 の逐語）ので、書ける。
2. **`docs/acceptance.md` の導入行は「押下 5」で埋められる**（本席の担当パス外）＝
   実測＝**まっさらな機体・CUDA 版 setup・cpu 変種で 5 押下**（23-6 ⑸）。
   便 E（2）が「未達（9 押下）」と書いた行を、**この走の逐語で置き換えてよい**。
   併せて**サイズ行**＝cpu 変種の展開後（cache 削除後）は **4.26 GiB**＝「≤ 9.0 GB」を満たす。
   **CPU の速度行**（RTF ≤ 4.0）も**製品の路で 3.33**（23-7）＝満たす。
3. **展開係数の分母がずれている**（`FetchPlanner.ExpansionFactorFor`）＝表の値は
   **`assemble-runtime.ps1` が組む樹**の実測で、**ランチャが展開する樹はそれより小さい**
   （cpu で 3.60 対 **3.23**・檔数で 28,373 対 25,262）。**低めに出さない側**なので急ぎではないが、
   実測を入れ替えるなら**ランチャが展開した樹**を分子にすること。rocm・cu126 も同じずれを持つはず
   （**推測**＝本席が測ったのは cpu だけである）。
4. **`SetThreadErrorMode` が効かない機体は未確認**（**限界**）＝この機体では 3 通りの撃ち方で
   確かめたが、AV や group policy がハードエラーの窓を別経路で出す機体は撃っていない。
   段 k は「窓 0 枚」を数え続けるので、そこが赤くなったらこの仮定が崩れたということである。
5. **cu130 の展開後サイズは未実測のまま**（RTX 機・§21-5 の 6 ⒠ をそのまま引き継ぐ）。
6. **E2E の台本が結果の行を `—` のまま読む罠**は便 E（2）から 2 度目である。
   `probe/d-launch-probe.ps1` の段 5・7 は `Wait-ForResultText` 相当を持っていて掛からないが、
   スクラッチパッドの E2E 台本は毎回この手当てが要る（`try-shot.ps1` の待ち方が正しい形）。
