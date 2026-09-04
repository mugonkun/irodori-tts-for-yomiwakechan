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
- `vc_redist`＝`ledger/vc_redist.json` の直リンク→sha256→`/install /passive /norestart`（管理者昇格が要る＝UAC・利用者操作 1 回）。既に `msvcp140.dll` が System32 にあれば飛ばす（便 B の U-8 の逐語で判定文言を決める）。

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
