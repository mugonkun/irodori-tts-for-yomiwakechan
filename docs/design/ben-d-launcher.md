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
