# launcher/ — ランチャ UI（**便 D**・便 A では本 README のみ）

> **便 A の時点で、このディレクトリには実装が無い。**便 D が入る前の申し送りだけを置く。
> 正典＝`decisions.md`。契約＝`docs/contract.md`。受け入れ条件＝`docs/acceptance.md`。

## 1. 何を作るか

**WPF（.NET）のランチャ**。この機体に .NET SDK 10.0.400 がある（`decisions.md` 25）。

ランチャは 3 つの役目を持つ。

1. **初回取得**＝台帳（`ledger/*.json`）を読み、実行系（埋め込み Python＋wheel）とモデルと
   `vc_redist.x64.exe` を取得し、sha256 で検証して `%LOCALAPPDATA%\irodori-tts-ywk\` に展開する。
   進捗は `server/ywk_fetch_models.py` の**進捗 JSON 行**を読んで出す。
2. **サーバの親**＝env を組んで
   `runtime\<variant>\python.exe -m ywk_server --host 127.0.0.1 --port 18088` を子プロセスで起こし、
   stderr を取り込んで状態を判定する。**bat は使わない**（文字コード事故の反面教師＝
   `research/local-additions/`）。**窓を閉じればアプリが終わり、子もツリー kill される**
   （裁定 124＝トレイには常駐しない・§13）。
3. **単体 TTS の UI**＝利用者が自環境で GPU・CUDA 版・パラメータ・参照ボイスを試す
   （配信外の読み上げテスト＝`decisions.md` 15）。**本体からの読み上げ司令は、ここで指定した
   GPU・CUDA 版を暗黙に使う**（device は Server プロセスの env でしか決まらない＝1 プロセス 1 デバイス）。

**上流の `gradio_app.py` は使わない**＝device の choices が型名だけ（`cuda:1` を選べない）・env を
1 欄も読まない・CLI に device 引数が無い（`research/lab/notes/36` P-4）。
gradio を依存から外すと配布サイズも 43〜82 MB 減る。

## 2. 決まっていること

| # | 事項 | 出典 |
|---|---|---|
| 1 | 起動主体は**配布版**＝本体は `/health` で見つけるだけ | `decisions.md` 6 |
| 2 | ポート **18088**・bind `127.0.0.1`・**api_key なし** | `decisions.md` 2 |
| 3 | GPU は **UUID（と PCI bus id）で保存**し、起動時に index へ解決する（列挙 ≤ 5 s）。index は再起動で変わりうる | `docs/acceptance.md`「GPU」行 |
| 4 | 精度は **device 連動**（GPU→bf16・CPU→fp32）。上級者設定で上書き可（Radeon 版を除く） | `decisions.md` 7・`docs/radeon.md` |
| 5 | 焼く既定＝**`preload=false`**（裁定 105＝bind してから裏でモデルを載せる。裁定 7 の `preload=true` を覆した）・`empty_cache_interval=0`・`allow_no_ref_voice=false`＋alias「デフォルト」・v4.1-Small 決め打ち・`voices_dir` は絶対パス・ready 待ち 120 s | `decisions.md` 7・105 |
| 6 | **`voices/` と `voices.json` の所有者はランチャ**（上流の登録 API 4 口は使わない＝日本語名が 400 になる） | `docs/contract.md` ⑷ |
| 7 | 話者メタ（表示名・caption 既定・既定パラメータ）は**配布版の台帳** `voices/voices.ywk.json`（上流に置き場が無い） | 同 |
| 8 | 既定 steps は **40**（上流既定）。UI のプリセットで 10 を選べる。**本体の指定が来たら必ず勝つ** | `decisions.md` 10 |
| 9 | CPU 合成は**UI にだけ**露出し、配信用途では非推奨と明記する | `decisions.md` 13 |
| 10 | 透かしは**既定 ON**・切る経路を持たない | `decisions.md` 9 |
| 11 | 状態判定は stderr の **3 行**＝`ywk_server <版> upstream=…`／`Uvicorn running on`／`runtime loaded in`（裁定 105 で 3 行目は**bind の後**に出る＝順序が変わっただけで行は同じ。wrapper は手前に `runtime load started` も出す） | 設計書 §4-1・§25・`docs/acceptance.md`「ログ」行 |
| 12 | **422 の本文 echo をログに流さない**（1 発 5 KB＋絶対パス） | `docs/acceptance.md`「ログ」行 |
| 13 | 参照ボイス欄には **Ethical Restrictions 1（No Impersonation）の但し書き**を出す | `README.md` §4 |
| 14 | Radeon 版は **bf16 固定＋起動時暖機**（段の 3 射＋自然尺 1 射）・`/ywk/status.warmup` で進捗 | `docs/radeon.md` |

## 3. 決まっていないこと（便 D で決める）

1. 画面の構成・話者台帳（`voices.ywk.json`）の書式。
2. 「デフォルト」を削除・改名できるようにするか（`research/lab/notes/37` §4-6 の ξ）。
3. CUDA 版の切替の実装＝**V-1** site-packages 2 本を `._pth` の 1 行で切替（+3,937 MB・切替 0 s）／
   **V-2** 都度 DL（1,781／2,470 MiB）／**V-3** cu130 のみ＋手順書。
   **`._pth` 書換での切替は未実射**（`research/lab/notes/36` W-7）。
4. 終了時の扱い（本体の元栓は「自分が起こした個体だけ落とす」流儀＝本体 §9 ⑤。配布版が常駐主体に
   なるとこの前提が変わる）。
5. 話者 100 件級の一覧の所要（上流は毎要求 `iterdir()`＝キャッシュ無し・**未実測**）。

## 4. 落とし穴（実射で分かっているもの）

| # | 事実 | 効き |
|---|---|---|
| 1 | **`._pth` があると `PYTHONPATH` は無視される**（`PYTHON*`／`IRODORI_*`／`HF_*` の env は効く） | **パスは `._pth` の専管・設定は env**。混ぜない |
| 2 | `IRODORI_VOICES_DIR` は**絶対パス必須**（既定は CWD 相対で起動時に `mkdir`） | 読取専用の導入先だと起動で落ちる |
| 3 | **CPU×bf16 は起動時 `ValueError`**／**fp32×GPU 不在は黙って CPU に落ちて 200**（340 倍遅い） | 精度の device 連動は安全装置。黙った転落を塞ぐ |
| 4 | 上流は device 文字列を検査せず、モデルを載せてから **6〜11 s 後に落ちる** | ランチャ（と wrapper）が起動前に 0 s で弾く |
| 5 | `voices.json` が壊れると **一覧だけ 500・`/health` は 200** | 配布版は 500 を返さず「0 件＋理由」を返す |
| 6 | 同一 stem の `.wav` は `.pt`／`.speaker.safetensors` に**黙って勝つ** | 事前計算潜在は別 stem か `voices.json` 別名で置く |
| 7 | `voice` と `irodori.no_ref` を同時に送ると**参照が黙って落ちる** | 契約で同時指定を 400 にした（`docs/contract.md` ⑷） |
| 8 | 合成は 1 プロセス 1 本の直列（`max_concurrent_synthesis` 既定 1） | 配信中に UI で試し撃ちすると読み上げが待たされる。UI に警告を出す |

## 5. 停止域

- `upstream/` は読むだけ（`__pycache__` を作らない＝`PYTHONDONTWRITEBYTECODE=1`）。
- `C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・`yomiwakechan2` に書かない。
- 第三者バイナリをリポ内（`build/out/` と `.venv-dev/` 以外）に置かない。

---

## 6. 骨組みが入った（2026-09-05・便 D・骨組み席）

**§3「決まっていないこと」のうち 3 件が決まった**（設計書 `docs/design/ben-d-launcher.md` §9 に逐語）。
残り 2 件（「デフォルト」の削除可否・話者 100 件級の所要）は実装席へ持ち越し。

### 6-1 いま在る物

| 物 | 中身 |
|---|---|
| `IrodoriTtsYwk.sln` | 2 プロジェクト（本体＋テスト） |
| `Directory.Build.props` | net10.0-windows・nullable・**ImplicitUsings=disable**・版の一元定義（`AppDisplayVersion`） |
| `IrodoriTtsYwk.Launcher/App.xaml(.cs)` | 単一起動（Mutex）・2 個目の起動で窓を前に出す合図・終了時にツリー kill（裁定 124 でトレイは消えた＝§13） |
| `IrodoriTtsYwk.Launcher/AppServices.cs` | **組み立ての 1 箇所**。実装席はここへ自分の実装を差す |
| `IrodoriTtsYwk.Launcher/Views/MainWindow.xaml(.cs)` | **空の枠**（`MainTabs` に TabItem を足す） |
| `IrodoriTtsYwk.Launcher/Contracts/` | 共有の型 9 群（下） |
| `IrodoriTtsYwk.Launcher.Tests/` | 純ロジック 46 本（実機・GPU・HTTP に触れない） |

### 6-2 共有の契約（`IrodoriTtsYwk.Launcher.Contracts`）

| # | 型 | 何を約束するか |
|---|---|---|
| ⑴ | `LedgerFile`・`LedgerItem`・`ModelsLedger`・`VcRedistLedger` | `ledger/*.json` と 1 対 1。`Urls` が url→fallback_url の順を持つ |
| ⑵ | `IDownloader`（`DownloadRequest`／`DownloadProgress`／`DownloadResult`） | Range 再開・sha256・`.part`→Rename・進捗（bytes／ETA） |
| ⑶ | `IRuntimeInstaller`・`IPthWriter`・`PthTemplate` | wheel／sdist／archive／python-embed の展開と `._pth`（`PthTemplate.Render` は純関数） |
| ⑷ | `IServerProcess`・`ServerState`・`ServerLogParser`・`ServerExitCodes`・`ServerEnvironment` | 状態機械・stderr の行読み・exit 2／3 の理由 1 行・env の組み立て |
| ⑸ | `IWrapperClient`・`WrapperResult<T>`・応答の型一式 | `/health`・`/params`・`/ywk/status`・`/ywk/voices`・`/v1/audio/speech`・`/ywk/warmup`・`/ywk/voices/precompute`（**口が無ければ `Available=false`**） |
| ⑹ | `IGpuEnumerator`・`GpuInfo`・`GpuResolver`・`IDriverCheck`・`DriverRequirement` | UUID 保存→index 解決・ドライバの下限（cu130 ≥ 580／cu126 ≥ 560.76） |
| ⑺ | `IVoiceStore`・`IVoicesJsonWriter`・`VoiceIds` | `voices.ywk.json` と上流の `voices.json`・「デフォルト」の常在と並び |
| ⑻ | `LauncherSettings`・`ISettingsStore`・`JsonSettingsStore` | `settings.json`（原子的保存・壊れていても既定で立ち上がる） |
| ⑼ | `AppPaths`・`RuntimeVariants`・`AppVersion` | 導入先と利用者データの分割・変種の 2 系統の名前・版と上流 pin |

### 6-3 実装席への申し送り

1. **`AppServices` に差す**。`Server`・`Wrapper`・`Downloader`・`RuntimeInstaller`・`GpuEnumerator`・
   `VoiceStore`・`VoicesJsonWriter` が空いている。差し替えは起動の 1 度だけ。
2. **純関数はもう在る**＝`ServerEnvironment.Build`・`ServerLogParser.Classify`／`NextState`・
   `PthTemplate.Render`・`GpuResolver.ResolveIndex`・`DriverRequirement.Check`・`VoiceIds.Order`。
   同じ判断を 3 席で 3 回書かない。
3. **テストは純ロジックだけ**（継ぎ目は public コンストラクタ・`InternalsVisibleTo` は使わない）。
4. **煙試験は私設ポート**（18094 以降）で、終わったら必ずツリー kill。8088・7861 は起こさない。

```powershell
dotnet build launcher\IrodoriTtsYwk.sln --nologo     # 警告 0
dotnet test  launcher\IrodoriTtsYwk.sln --nologo     # 46 本
```

---

## 7. 画面が入った（2026-09-05・便 D・L3 席＝Views／ViewModels）

**§3「決まっていないこと」の 1（画面の構成）が決まった。**残る 2 件（「デフォルト」の削除可否・
話者 100 件級の所要）は据え置き＝前者は**削除できない**方に倒した（契約 ⑷ 4-2 の常在に合わせた・
`VoiceRow.CanRemove` が偽）。

### 7-1 樹

```
launcher/IrodoriTtsYwk.Launcher/
  Mvvm/          ObservableObject.cs・RelayCommand.cs（自前・依存追加なし）
  ViewModels/    MainViewModel（束ねる）・StatusViewModel・VoicesViewModel・TryViewModel・
                 SettingsViewModel・AboutViewModel・FirstRunViewModel
                 ＋純関数の道具＝UiText・MemoryEstimate・ReleaseFlavor・LogTail・
                   SpeechRequestBuilder・VoiceRow(Builder)・VoiceNameValidator・
                   WarmupStagesText・NumericInput
  Audio/         IAudioPlayer・NAudioPlayer（NAudio・MIT）・WavInfo・WavGain（どちらも純関数）
  Views/         MainWindow（5 タブ）・StatusView・VoicesView・TryView・SettingsView・
                 AboutView・FirstRunWizard（窓）
```

### 7-2 決めた 4 つ（後続が前提にしてよい）

1. **ViewModel は WPF の型に触れない**。`Dispatcher` も持たない＝**スレッド跨ぎの marshal は View の
   仕事**（`MainWindow` が `Dispatcher.BeginInvoke` で ViewModel を呼ぶ）。おかげで ViewModel は
   xUnit から素で作れる（継ぎ目は public コンストラクタ・`InternalsVisibleTo` は使わない）。
   `CommandManager.RequerySuggested` も使わない（可否は `RaiseCanExecuteChanged` を明示で叩く）。
2. **選択肢は配布樹が決める**（`ReleaseFlavors`）＝`ledger/runtime-rocm-*.json` を持つ配布物は
   Radeon 版で、CUDA の選択肢を出さない（裁定 5）。さらに**台帳が実在する変種だけ**に絞る＝
   取得の始まらない選択肢を見せない。版の種類を exe に焼かないので、同じ exe が両リリースで動く。
3. **「既定に戻す」は欄を空にすることで表す**（`NumericInput`・`SpeechRequestBuilder`）＝
   空欄は `null`（載せない）・`0` は `0`（載せる）。上流は範囲検査を持たないので、
   歩数・読み速さ・seed は**撃つ前に 0 s で弾く**（`SpeechRequestBuilder.Build`）。
4. **試聴・再生は wav だけ**＝`NAudio.WinMM` と `NAudio.Core` に `Mp3FileReader`／`AudioFileReader`
   が入っていない（2026-09-05 に nupkg の型一覧を機械で確認）。依存を増やさないので、参照として
   受ける 8 拡張子のうち **wav 以外の試聴が「未対応」**になる（登録・合成には影響しない）。
   再生の音量は裁定 52 のとおり **−16 dBFS 相当へ揃える**が、**檔は 1 バイトも書き換えない**
   （`WavGain.ComputeGain` は純関数・持ち上げは山が 1.0 を超えない上限で頭打ち）。

### 7-3 UIA の名前（受け入れ条件 D-7・`probe/d-launch-probe.ps1` が使う）

主要コントロールに `AutomationProperties.AutomationId` を振ってある。実測（この機体・2026-09-05・
私設データディレクトリ・**サーバも GPU も起こさずに**窓だけ立てて `UIAutomationClient` で読んだ）＝

| 画面 | 拾える id（抜粋） |
|---|---|
| 窓 | `MainWindow`・`MainTabs`・`MainStateText`・`MainEndpointText`（**本文は空**）・`MainVersionText`・`MainHeaderText`（開発ビルドだけ本文が入る）　＋ **v2.0 段 A の新設 10**＝`MainBandStateText`・`MainBandReasonText`・`MainBandActionButton`・`MainBandHostText`・`MainOpenLogButton`・`MainGearButton`・`MainDetailOverlay`・`MainDetailTabs`・`MainDetailCloseButton`・`SettingsAdvancedExpander`。**`MainStartButton`・`MainStopButton`・`MainFirstRunButton` は 設定 › 詳細 へ要素ごと移した**（id は 1 字も替えていない） |
| タブ | `TabTry`（**先頭＝既定**）・`TabVoices`・`TabSettings`　＋ 歯車の層の中に `TabStatus`・`TabAbout`（`MainGearButton` を押してから探すこと） |
| 詳しい状態 | 畳みの外＝`StatusStateText`・`StatusReasonText`・`StatusGpuMismatchText`・`StatusSettingsPendingText`。**`StatusAdvancedExpander`（新設・既定は閉）の中**＝`StatusGpuText`・`StatusDeviceText`・`StatusVariantText`・`StatusWarmupText`・`StatusPrecomputeText`・`StatusMemoryPanel`／`StatusMemoryText`・`StatusLatentCacheText`・`StatusVoiceMemoryText`・`StatusUpstreamMismatchText`・`StatusNoticesText`・`StatusLogBox`・`StatusEndpointText`（**要素と id は残すが本文を常時は出さない**＝18088 を出す面は帯 1 つだけ） |
| 声 | `VoicesGrid`（列は 5→**4**＝1 名あたりのメモリの列を消した）・`VoicesBrowseButton`・`VoicesSourcePathBox`・`VoicesNewNameBox`・`VoicesNewCaptionBox`・`VoicesAddButton`・`VoicesPreviewButton`・`VoicesStopPreviewButton`・`VoicesRemoveButton`・`VoicesRefreshButton`・`VoicesPreviewBlockedText`・`VoicesRemoveBlockedText`・`VoicesMessageText`・**`VoicesCountText`**（新設・一覧の下の薄字＝数は出さない）。**`VoicesAdvancedExpander`（新設）の中**＝**`VoicesSelectedMemoryText`**（是正・段 C の検分で**要素ごと**ここへ移した＝この 1 行は `MiB`／`GiB` を綴るので、置き場は 詳細 に限る＝`v2-copy.md` §1-8 の `UiText` の行）・`VoicesPrecomputeButton`・`VoicesRestorePresetsButton` |
| 発話テスト | ふだん＝`TryInputBox`・`TryInputLengthText`・`TryVoiceCombo`・`TryStepsPreset10`／`TryStepsPreset40`（札は「はやい」「きれい」）・`TryCaptionBox`・`TrySpeedBox`・`TrySynthesizeButton`・`TryReplayButton`・`TryStopButton`・`TrySaveButton`・`TryResultText`・`TryMessageText`・`TryConcurrencyText`。**`TryAdvancedExpander`（新設）の中**＝`TryStepsBox`・`TryCfgTextBox`・`TryCfgCaptionBox`・`TryCfgSpeakerBox`・`TrySeedBox`・**`TryDetailText`**（新設＝所要 ms・RTF・seed の内訳） |
| 設定 | ふだんの 4 つ＝`SettingsAutoStartCheck`・`SettingsWarmupCheck`・`SettingsVoicesDirText`＋**`SettingsOpenVoicesDirButton`**・**`SettingsOpenLogButton`**（3 つとも新設）。**`SettingsAdvancedExpander` の中**＝`SettingsGpuCombo`／`SettingsRefreshGpuButton`／`SettingsGpuMessageText`／`SettingsDriverText`・`SettingsVariantCombo`／`SettingsVariantNameText`／`SettingsVariantBlockText`・`SettingsPrecisionCombo`／`SettingsPrecisionNoteText`・`SettingsPrecomputeCheck`／`SettingsPrecomputeNoteText`・`MainStopButton`／`MainStartButton`／`MainFirstRunButton`・`SettingsDataDirText`＋**`SettingsOpenDataDirButton`**（新設）／`SettingsAppDirText`（`SettingsModelDirText`・`SettingsRuntimeRootText` は「データ」に畳んで伏せた・id は据え置き）・`SettingsClearCacheButton`／`SettingsCacheMessageText`・`SettingsPortBox`／`SettingsPortNoteText`（**伏せてある**）。畳みの外の下端＝`SettingsApplyButton`／`SettingsRevertButton`／`SettingsMessageText`。**退役 7**＝`SettingsShowMemoryCheck`・`SettingsWarmupStagesBox`・`SettingsWarmupVoicesBox`・`SettingsEmptyCacheBox`・`SettingsEmptyCacheNoteText`・`SettingsReadyTimeoutBox`・`SettingsReadyTimeoutNoteText` |
| このアプリについて | 畳みの外＝`AboutTitleText`・`AboutDisclaimerText`・`AboutVersionText`・`AboutWatermarkText`・`AboutEthicsText`・`AboutLicensesDirText`・**`AboutGuideButton`**／**`AboutSaveLogButton`**（新設）＋**`AboutSavedLogText`**／**`AboutOpenReportFolderButton`**（是正・段 C の検分で新設＝〔報告用のログを保存〕を押した後だけ出る 1 行と、その檔を開く釦）。**`AboutAdvancedExpander`（新設）の中**＝`AboutUpstreamText`・`AboutNoticesPathText`・`AboutLicenseList` |
| 初回取得（v2.0＝はじめの準備） | `FirstRunWizard`・`FirstRunStepTitle`・`FirstRunStepNumber`・`FirstRunNoticesBox`・`FirstRunAcceptCheck`・`FirstRunVariantCombo`・`FirstRunVariantNoteText`・`FirstRunDriverText`・`FirstRunSizeText`・`FirstRunProgressBar`／`FirstRunProgressText`・`FirstRunTrailList`・`FirstRunBackButton`／`FirstRunNextButton`／`FirstRunCancelButton`・`FirstRunMessageText`　＋ **v2.0 段 B の新設 12**＝`FirstRunNoticesSummaryText`・`FirstRunNoticesFullButton`・`FirstRunNoticesExpander`・`FirstRunDecisionText`・`FirstRunPlanText`・`FirstRunPhaseText`・`FirstRunProgressDetailText`・`FirstRunUacNoticeText`・`FirstRunDoneText`・`FirstRunAdvancedExpander`・`FirstRunNoticesUnreadableText`（お知らせの全文が読めない回だけ出る 1 行）・`FirstRunWhyText`（段 2 の「なぜ落ちるのか」） |

**タブの中身は選ぶまで作られない**（WPF の `TabControl` は遅延生成）＝台本は `SelectionItemPattern`
でタブを選んでから、そのタブの id を探すこと。**畳み（`Expander`）の中も同じ**＝開くまで実体が無いので
UIA からは見えない。はじめの準備で動かし方を自分で選ぶ（`FirstRunVariantCombo`）には、
先に `Open-YwkFold -Root <wizard> -Id 'FirstRunAdvancedExpander'` を撃つこと（v2.0 段 B＝
選ぶ段そのものは廃した＝アプリがドライバから決める。**id は 1 つも消していない・移しただけ**）。

**釦は出せる段にだけ出す**（v2.0 段 B の是正）＝`FirstRunBackButton` は戻れる段（＝`BackVisible`）、
`FirstRunCancelButton` は走っている間（＝`IsBusy`）だけ**見える**。要素と id はどの段でも在るが、
`Visibility` が畳まれている間は UIA から拾えない＝台本は「その段で出るはずの釦」だけを探すこと。
同じく `FirstRunNoticesSummaryText`・`FirstRunNoticesFullButton`・`FirstRunNoticesExpander`・
`FirstRunAcceptCheck` は**お知らせの全文が読めた回だけ**見え、読めない回は代わりに
`FirstRunNoticesUnreadableText` が出る。

**「発話テスト」のタブは裁定 116（2026-09-09）まで「試し撃ち」と名乗っていた**＝変えたのは利用者の目に入る札
（タブの Header・ウィザードの完了文と「しゃべらせてみる」の札・声の一覧のツールチップ）だけで、id（`TabTry`・`Try*`）と
内部名（`TryView`・`TryViewModel`・設定の `lastTest*`）は据え置き。台本は名前ではなく id で掴むこと。
**v2.0 段 C で札がもう 1 度動いた**（下の §14）＝タブ「話者」→「**声**」・「状態」→「**詳しい状態**」・
釦「合成して再生」→「**しゃべらせる**」・「wav に保存…」→「**音声ファイルに保存…**」ほか。
**id と内部名は 1 つも動いていない。**

### 7-4 ライセンスの索引（裁定 51 の記帳先）

exe に焼かれる第三者物の許諾文は **`licenses/dotnet/`**（`licenses/README.md` §1 の索引表にも
1 対 1 で載っている＝`build/check-licenses.ps1` が突合する）。

| 檔 | 中身 |
|---|---|
| `licenses/dotnet/README.md` | 何が焼かれ、どの版で、どこから写したかの逐語（＋欠落の記帳） |
| `licenses/dotnet/dotnet-runtime-LICENSE.txt` | .NET ランタイム（`Microsoft.NETCore.App.Runtime.win-x64`）の MIT |
| `licenses/dotnet/windowsdesktop-runtime-LICENSE.txt` | WPF／WinForms ランタイムの MIT |
| `licenses/dotnet/dotnet-runtime-THIRD-PARTY-NOTICES.txt` | .NET ランタイムが同梱する第三者物の通知 |

**NAudio 2.2.1（MIT）の全文は未取得**＝nupkg が SPDX の名乗りと `licenseUrl` しか持たない
（`licenses/dotnet/README.md` §3 に欠落として記帳済み・卓の裁定待ち）。**L3 席は NAudio の版も
参照の仕方も変えていない**（`NAudio.WinMM` 2.2.1 のまま・`WaveOutEvent`／`WaveFileReader`／
`VolumeSampleProvider` の 3 型だけを使う）。

### 7-5 検分（この機体・2026-09-05）

```powershell
dotnet build launcher\IrodoriTtsYwk.sln --nologo   # 警告 0・エラー 0
dotnet test  launcher\IrodoriTtsYwk.sln --nologo   # 356 本（うち L3 が 130 本）
```

窓の煙試験は**私設のデータディレクトリ**（`YWK_LAUNCHER_DATA_DIR`）に
`autoStartServer:false`・`port:18094` の `settings.json` を置き、`YWK_LAUNCHER_RUNTIME_DIR` を
**渡さずに**（＝`python.exe` が見つからず子プロセスが起きない形で）立てた。**8088・18088・7861 を
含めどのポートも開いていないこと**を `Get-NetTCPConnection` で確認し、終わったらツリー kill した。

## 9. 画面の 2 巡目（2026-09-05・便 D（2）・画面席 LB）

> §8 は起動席（LB は §9 を使う）。**この節も実装の逐語**であって §1〜§7 の設計を覆さない。

### 9-1 GPU メモリの帯（裁定 87 ⑴・67 ⑵⑶）

`/ywk/status.memory` の欄が確定したので `Contracts/WrapperResponses.MemoryStatus` を
**11 欄**（`device`・`allocated`・`reserved`・`max`・`gpu_total`・`gpu_free`・`gpu_used`・
`latents`・`latents_total`・`sampled_at`・`error`）に合わせた。画面の読み替えは

| 画面の語 | 欄 | 出所 |
|---|---|---|
| **使用量** | `allocated` | torch の allocator（**このプロセス**） |
| **占有量** | `reserved` | 同上 |
| **GPU 全体** | ~~`gpu_used` / `gpu_total`~~ | ~~`mem_get_info`（**カード全体・他プロセス込み**）~~ |

〔**裁定 110（2026-09-08）で撤回**＝⑴ 状態帯は `gpu_used`／`gpu_total` を**出さなくなった**・
⑵「カード全体（他プロセス込み）」という読み自体が撤回された（Windows の ROCm では
**自プロセス相当**で、他プロセスを含まない）。いまの帯は
**使用量／占有量＝torch の allocator の中**（頭に `torch ` と名乗る）＋
**このプロセス／GPU 全体＝Windows の GPU 計数（PDH）と DXGI の専用メモリ**（共有メモリは含まない・
TTS が使っている GPU ごとに 1 組）である。設計は `docs/design/ben-d-launcher.md` §27、
JSON の欄そのものは互換のため据え置き（`docs/contract.md` ⑹ 6-1）。〕

`null` は `—`（0 と混ぜない）・`device` が `cpu` なら「CPU（GPU メモリなし）」・欄ごと無ければ
「未対応」。`sampled_at` は**文字列のまま**持つ（地域設定で揺らさない）。

### 9-2 概算は「1 名あたり」が主役（low 13）

状態帯は `1 名あたり「<話者>」＝…（全員分＝全部を同時に載せたときの上限 …）`。
**焼いてある話者は `memory.latents[<id>]` の実サイズ**（「実測」と書く）で、焼いていなければ
係数 0.70 GB に「実測前の概算」を添える。話者一覧の「メモリ（1 名あたり）」列も同じ 1 行である。

### 9-3 潜在キャッシュ（裁定 67 ⑴）

設定の `SettingsPrecomputeCheck`（三値＝中間は変種の既定）を状態帯が
`潜在キャッシュ ON／OFF（焼いた話者 n 名・合計 m MB）`（`StatusLatentCacheText`）で受ける。
件数と合計は `memory.latents` が正本で、口が無ければ一覧の `latent` の数だけを名乗る（合計は `—`）。

### 9-4 窓は `/ywk/status` を叩かない（low 3）

`MainWindow` の `DispatcherTimer` を落とし、`IServerProcess.LatestStatus`／`StatusSampled` を
読むだけにした。**状態の書き手は状態機械 1 本**＝`Warming` の出入りも窓は書かない
（`MainViewModel.ApplyStatusSample` は描くだけ）。

### 9-5 試聴は wav だけ・削除は Command（low 11・14）

`VoiceRow.CanPreview` は `.wav` のときだけ真。押せない理由（`PreviewBlockedReason`）は
ボタンの `AutomationProperties.HelpText`・`ToolTip`・1 行の表示（`VoicesPreviewBlockedText`）の
3 箇所に同じ文言で出る。削除も `RemoveCommand` に寄せ（確認窓は View が
`VoicesViewModel.ConfirmRemove` に差す）、理由は `VoicesRemoveBlockedText` から読める。
一覧の見出しは **「表示名（＝話者 id）」と「参照 wav の檔名」**（`HeaderTemplate` の `ToolTip` 付き）。

### 9-6 実測（この機体・rocm-gfx1151・私設ポート 18098・**8088／7861／18088 は無傷**）

```
[ready] ok=True text=待機 seconds=26.53
[band] StatusDeviceText      = cuda:0／AMD Radeon(TM) 8060S Graphics（gfx1151）／bf16
[band] StatusMemoryText      = 使用量 1.73 GB／占有量 2.07 GB／GPU 全体 2.33 GB / 99.74 GB（最大 3.01 GB）
[band] StatusLatentCacheText = ON（焼いた話者 0 名・合計 0 B）
[band] StatusVoiceMemoryText = 1 名あたり「デフォルト」＝参照なし（増えません）（全員分＝全部を同時に載せたときの上限 7.70 GB）
[stop] private port listening = False / python 残骸 0
```

## 10. 統合の 2 巡目（2026-09-05・便 D（2）・統合席）

> §8 は起動席・§9 は画面席。**この節も実装の逐語**であって §1〜§7 の設計を覆さない。
> 詳細と実射の逐語は `docs/design/ben-d-launcher.md` §19。

### 10-1 席と席の間に落ちていた 3 つ

1. **窓が変種の門に入力を渡していなかった**＝`MainViewModel.StartServerAsync` は
   `ServerLaunchPlan.Build` を通すようになり、`Variant`（台帳の綴り）・`DriverVersion`
   （`GpuEnumerator.DriverVersionOf`）・`InstalledVariants`（`VariantGate.DetectInstalled`）を載せる。
   載せる前は門が env の `YWK_VARIANT`（cu130 と cu126 が `cuda` に畳まれた名）で代用していたので、
   **断れても勧める先が `cpu` に落ちた**。実射＝`cu130 はこの機体で GPU を見られません
   （is_available=False・device_count=0）。rocm-gfx1151 か cpu の変種に切り替えてください。`
2. **写したプリセットが上流の別名表に入っていなかった**＝`LauncherComposition.PrepareVoices`
   （新設・**テストの継ぎ目は public**）が、移送→初回展開→**`voices.json` の書き出し**まで通す。
   `PresetVoices` が書くのはランチャの台帳（`voices.ywk.json`）だけなので、これが無いと
   **11 名は画面に見えるのに合成できない**（`GET /v1/audio/voices` にも試し撃ちの候補にも出ない）。
3. **走行中で断られた焼きを出し直していなかった**＝`VoicesViewModel` が
   **409 `ywk_precompute_running`**（機械可読な code で判定・文言では見ない）で断られた話者を覚え、
   `MainViewModel.ApplyStatusSample` が配る `/ywk/status.precompute` が走行中でなくなった標本で
   **1 度だけ**出し直す。`rocm-*` は ready の直後にプリセット 11 名を焼くので、その最中に足した
   話者はここを通らないと永久に「未」のままだった。

### 10-2 無人検分は 57 段（`probe/d-launch-probe.ps1`）

1 巡目の 31 段に、裁定 87・88 の 26 段を足した＝**変種の門**（別個体・私設ポート 18099・
子プロセス 0）・**プリセット話者の 4 箇所突合**（数は台帳の `status: done` を数える＝
いまは **12 名**〔裁定 118〕。台本に数は焼いていない）・**削除の押せない理由（`HelpText` と表示が同文言）**・
**GPU メモリの 4 数**・**潜在キャッシュの ON/OFF と件数**・**概算 → 実測の切り替わり**・
**死活 2 種**（待機中の kill／合成中の kill）。`-GateOnly` で門だけ 6 秒で撃てる。

```
pwsh -File probe/d-launch-probe.ps1 -Port 18094 -GatePort 18099          # 0 failure(s) of 57
pwsh -File probe/d-launch-probe.ps1 -Exe build/out/launcher/win-x64/IrodoriTtsYwk.Launcher.exe `
     -Port 18095 -GatePort 18099 -DataDir $env:TEMP\ywk-d-launch-probe-exe   # 0 failure(s) of 57
```

### 10-3 検分（この機体・2026-09-05）

```powershell
dotnet build launcher\IrodoriTtsYwk.sln --nologo   # 警告 0・エラー 0
dotnet test  launcher\IrodoriTtsYwk.sln --nologo   # 476 本（統合席の新規 9 本を含む）
pwsh -File build\release-build.ps1                 # exe 69,608,415 B（66.4 MiB）・EXIT=0
```

実射の逐語＝起動 → 待機 **27.6 s**（2 度目以降 23.2 s）・門の拒否 **2.95 s**・
待機中の kill から「失敗」まで **0.31 s**・合成中の kill から「サーバが落ちました」まで **0.39 s**・
`使用量 1.74 GB／占有量 2.06 GB／GPU 全体 2.36 GB / 99.74 GB（最大 3.64 GB）`・
`ON（焼いた話者 12 名・合計 1.2 MB）`・`1 名あたり＝潜在参照（実測 113.4 KB）`。
**8088・7861・18088 は一度も起こしていない**（私設ポートは 18094／18095／18099 だけ）。

## 11. ランチャの 3 巡目（2026-09-05・便 D（3）・ランチャ席）

> **この節も実装の逐語**であって §1〜§7 の設計を覆さない。触ったのは `launcher/**` だけ
> （`server/`・`build/`・`probe/`・`ledger/`・`docs/`（設計書 §21 を除く）には 1 行も触っていない）。

### 11-1 入った物（裁定番号つき）

| # | 裁定 | 何が変わったか |
|---|---|---|
| ⑴ | 94 ⑴ | **初回取得ウィザードの自動進行**＝取得→展開→モデル→起動は成功したら自動で次へ。押下は **5**（同意チェック・同意して次へ・変種・取得を始める・発話テストへ）。失敗した段だけで止まり、文言は「もう一度」＋理由 1 行。段の出入りは `Trail` に `― <段の名>` として残る。「中断」は各段で効く（`FirstRunViewModel.AdvanceAsync`） |
| ⑵ | 90 Q-E2 ⑶ | **取得キャッシュの削除**＝⒜ 初回取得を通した後、「発話テスト」で 1 射 200 が返ったら `<data>\cache` の中身を残らず消す（置き場自身は残す・消したバイトをログと Trail に）⒝ 設定に手動の「取得キャッシュを消す（n GiB）」⒞ **消す前の関門**＝`python.exe` の在否と `settings.runtimeLedgers[<変種>]` と配布樹の台帳の一致（どちらか欠ければ 1 バイトも消さない）。**名前で選ばない**のは、前の台帳の原檔・打ち切った `.part`・名前が変わった檔が残って「展開後（cache 削除後）」の実測が合わなくなるからである。ただし**関門を通していない変種だけが名指す原檔は残す**（是正・便 D（3）の 3 巡目＝実機の cache 4.14 GB のうち 2.41 GiB が cu126 専用で、rocm の関門を通しただけの掃除がそれを消していた。`CacheCleaner.ProtectedFileNames`）（`Services/Ledger/CacheCleaner.cs`） |
| ⑶ | 91 | **`settings.json` に `runtimeLedgers` と `installedAppVersions`（変種ごとの表）**（展開が通った直後にその変種の欄だけ焼く）。起動時に配布樹の台帳と突き合わせ、食い違えば状態帯に 1 行と「実行系を組み直す」1 手（cache から再展開・cache が無ければ足りない檔だけ取得から。**押す前に取り直す量を 1 行で名乗り**、走っている間は帯と「やめる」を出す）。**版だけが動いたときは 1 手を出さず焼き直して黙る**。**焼き印を押すのは `site-packages\*.dist-info` の件数が台帳と合う樹だけ**（組みかけの樹に押すと、掃除の関門が自分で作った値と突き合わせて通ってしまう）（`Services/Ledger/RuntimeStamp.cs`・`MainViewModel.RebuildRuntimeAsync`） |
| ⑷ | 94 ⑶ | **展開係数を変種ごとの実測に**＝cu126 **1.69**・rocm-* **2.98**・cpu **3.60**・cu130 は未実測で **3.3**。見積りの 1 行は「（展開は実測 2.98 倍）」「（展開は推定 3.3 倍）」と出し分ける（`FetchPlanner.ExpansionFactorFor`） |
| ⑸ | 92 の low 6 | ⒜ `UiText.Bytes` の綴りを **GiB／MiB／KiB** に（1024 進なのに GB と綴っていた）⒝ 「（最大 …）」を**使用量の隣**へ・`memory.error` は「（一部の欄が読めませんでした：…）」⒞ 「未対応」と **「—（サーバが動いていません）」** の出し分け ⒟ `MemoryStatus.Latents` が JSON の `null` でも落ちない（`EffectiveLatents`）⒠ `MainViewModel` の Failed 2 箇所を `IServerProcess.ReportPreflightFailure` 経由に ⒡ 「GPU メモリの欄」は「適用」した瞬間に効く（`StatusViewModel.MemoryPanelVisible` へ束縛）⒢ 409 の待ち行列は**受け取られるまで落とさない**（3 回落ちたら諦めて理由を残す） |
| ⑹ | §20-5 ⑴ | **窓が 60 秒黙る**の手当て＝`AsyncRelayCommand` の受け口を**全例外**に（型の数え上げをやめた）・`ProcessRunner` と `ServerProcess` に**「起こす」段そのものの期限 3 s**（`Process.Start` を別スレッドへ逃がして待ち、見限った個体は後から起きたら殺す。1 度の「サーバ起動」で同じ `python.exe` を 3 回起こす＝窓の列挙・門の検分・子なので、3 つとも止まって 9 s＝無人検分の budget 10 s の内側）・`MainViewModel` は列挙が落ちても理由 1 行を残して先へ進む・`Status`／`Voices`／`Try`／`Settings`／`FirstRun` の各手の `Faulted` を画面へ繋いだ |

### 11-2 UIA の名前（§7-3 への追加）

| 画面 | 足した id |
|---|---|
| 状態 | `StatusRebuildRuntimeText`・`StatusRebuildRuntimeButton`（裁定 91 の 1 行と 1 手＝食い違っていないときは**木に出ない**。焼き印の無い樹は起動時にいまの台帳で焼き直して黙る）・`StatusRebuildProgressText`・`StatusRebuildProgressBar`・`StatusRebuildCancelButton`（組み直しが走っている間だけ木に出る＝是正・便 D（3）の 3 巡目） |
| 設定 | `SettingsClearCacheButton`（文言は `取得キャッシュを消す（n GiB）`／空なら `（空です）`）・`SettingsCacheMessageText` |

`StatusMemoryPanel` は `Visibility` を `StatusViewModel.MemoryPanelVisible` に束縛したので、
設定で外して「適用」を押すと**その場で木から消える**（以前は次の起動まで残った）。

### 11-3 `settings.json` に足した 2 欄（契約 ⑻）

```json
{ "runtimeLedgers":       { "rocm-gfx1151": "<ledger/runtime-rocm-gfx1151.json の sha256（小文字 hex 64 字）>",
                            "cu126":        "<ledger/runtime-cu126.json の sha256>" },
  "installedAppVersions": { "rocm-gfx1151": "v1.1.0", "cu126": "v1.1.0" } }
```

**鍵は変種**（是正・便 D（3）の 3 巡目）。1 巡目は `runtimeLedgerSha256`／`installedAppVersion` の
**1 組しか無かった**が、実行系は変種ごとに在るので、両方組んである機体で設定の変種を切り替えただけで
「実行系を組み直してください」の偽警告が出て 1 手が押せるようになり、押せば健全な実行系を消して
数 GiB を取り直した（裁定 88 ⑴ が勧める「cu126 → cpu へ切り替える」導線がそのまま落ちる）。
取得キャッシュの関門（`CacheCleaner.Blocked`）も同じ値を見るので、切り替えた瞬間に掃除まで止まった。

- どの変種の欄も**無くてよい**（古い `settings.json`・台本で組んだ樹は「いつ組んだか判らない」＝黙る）。
- **1 巡目の 2 欄は読まない**（変種の名を持たないので、いまの変種の欄へ畳むと同じ穴を作り直す）。
  焼き印を失った樹は次の起動でいまの台帳から焼き直されるので、誰も 4 GB をやり直さない。
- 焼くのは**その変種の欄だけ**（`LauncherSettings.SetRuntimeStamp`）。
- `docs/contract.md` ⑻ の欄の表は本席の担当パス外なので**卓が 2 行足すこと**
  （裁定 95 の票は 1 巡目の綴りで書かれているので、**この形で**足すこと）。

### 11-4 検分（この機体・2026-09-05）

```powershell
dotnet build launcher\IrodoriTtsYwk.sln --nologo --no-incremental   # 0 個の警告・0 エラー
dotnet test  launcher\IrodoriTtsYwk.sln --nologo                    # 554 本（499 → +55）
```

新規の釘は `IrodoriTtsYwk.Launcher.Tests/RoundThreeTests.cs` の 1 檔にまとめてある。
既存檔で直したのは**綴りと文言が動いた 5 本**と、`IServerProcess` に口が増えた偽物 1 つだけ。
**実 GPU・実ポート・子プロセス・外への取得には 1 つも触れていない**
（起こすのは「起こせない実行檔」1 本で、それも起きない）。

## 12. 増えたプリセットと取得への導線（2026-09-10・裁定 121）

司令官の報告（逐語）＝「**話者一覧にシャンパンコールがないね。**」「**初回起動から、モデルダウンロードへの
導線を追加してほしい。単に、サーバー起動失敗となるから。**」設計＝`docs/design/ben-d-launcher.md` §29。

### 12-1 同梱の話者は「増えた分だけ」起動で入る

- 台帳 `voices.ywk.json` に **`presets_installed_ids`**（この台帳へ 1 度でも差し出したプリセット id）が増えた。
  `presets_installed` の印は据え置き（意味も変わらない）。
- 起動は `PresetVoices.InstallIfFirstRun`（初回展開）に続けて **`PresetVoices.InstallNew`**（増分）を通す。
  足すのは「配布樹の `voices/presets.json`（`status: done`）に居て、`presets_installed_ids` にも台帳にも
  居ない id」だけ＝**利用者が消した 1 名は起動では戻らない**（戻す口は話者画面の
  「同梱のプリセットを入れ直す」＝`PresetVoices.Restore` のまま）。
- 足した回は状態帯のログに `同梱の話者を N 名足しました：名1・名2` が 1 行出る
  （窓より前に走るので `LauncherComposition.Note` の控えに積み、主窓の構築が流す）。
- **`presets_installed_ids` の無い台帳**（≦ v1.0.1）は、いま台帳に居るプリセットで種を蒔く＝
  そこで消してあったプリセットは**1 度だけ**戻る（次の起動からは戻らない）。v1.0.1 は public 化
  （裁定 120）以後 Latest なので、該当するのは開発機だけではない＝**利用者に見せる文にも書く**
  （`docs/release-notes/v1.0.2.md`・`docs/install.md`）。
- **改名（裁定 108）は記録も一緒に改める**＝`MigrateRenamed` は `presets_installed_ids` の旧い id を
  新しい id へ移す。移さないと新しい名が「まだ差し出していない」と読まれ、利用者がその 1 名を消した
  次の起動で戻ってしまう。同じ理由で `InstallNew` は**足した話者が 0 名でも記録が増えた回は台帳を書く**。

### 12-2 実行系／モデルが無ければ取得へ導く

- **`Services/Models/AcquisitionCheck.cs`**（新設）＝`Check(paths, settings)` が
  `RuntimeReady`（変種の `python.exe`）・`ModelsReady`（`HF_HOME/hub` の snapshot と `refs/main`）・
  `MissingModelFiles`・`Summary` を返す。**檔の在否と長さだけ**（通信も python も無し・sha256 も取らない）で、
  在否の規則は `server/ywk_fetch_models.py` の `_resolve_cached`／`refs_main_path` と同じ物を使い、
  台帳が長さを名乗る檔は `FileInfo.Length` も突き合わせる（途中で切れた檔を「揃っている」と読まない）。
  `ledger/models.json` が読めない配布樹では「揃っている」と答える（材料が無いのに急かさない）。
- 主窓は `firstRunCompleted` が真でも `MainViewModel.NeedsAcquisition` が真なら**自動起動をやめて**
  ウィザードを出し、Trail とログに `実行系／モデルが揃っていないため、取得からやり直します（不足＝…）` を残す。
- 「サーバ起動」の事前検査は、実行系が無い断りにもモデルが無い断りにも
  `上の「初回取得をやり直す」から取得してください。` を付ける。
- 状態帯は、取得が未了だと読める失敗のときだけ `取得が未了です。` と「取得へ進む」を出す
  （`StatusViewModel.IsAcquisitionFailure`＝事前検査の 1 文か、`モデルの読込に失敗＝` の理由に
  `OSError`／`does not appear to have a file`／`offline`／`not found`／`No such file`／`LocalEntryNotFound`）。
  押下は窓が繋ぐ（`StatusView.AcquireRequested` → `MainWindow.ShowFirstRun`）＝ViewModel は WPF に触れない。
  **檔を見た結果は事前検査の断りに勝つ**＝取得を済ませてウィザードを閉じれば、`Failed` の理由が残っていても
  帯は下がる（個体がモデルを読めずに落ちた回は残す）。判断は設定の「適用」でも引き直す（変種が動くため）。
- **ウィザードは揃っている実行系を落とし直さない**＝`FirstRunViewModel.RuntimeLooksSound()`（`python.exe` が在る・
  展開の件数が台帳と合う・展開に使った台帳が配布樹と同じ）が真なら取得と展開の段を飛ばしてモデルの段へ行く。
  取得キャッシュは 1 射目で空にする（裁定 90 Q-E2 ⑶）ので、飛ばさないと数 GiB の再取得と健全な樹の
  作り直しになる。

### 12-3 UIA の名前（§7-3 への追加）

| 画面 | 足した id |
|---|---|
| 状態 | `StatusAcquisitionText`・`StatusAcquireButton`（取得が未了だと読める失敗のときだけ木に出る） |

### 12-4 検分（この機体・2026-09-10）

```powershell
dotnet test launcher -c Release --nologo    # 674 合格・1 skip（総数 675・新設 25 本）
```

**窓は立てていない**（UIA 実射なし＝走っている個体は止めない）。`installer-build.ps1` も走らせていない。

## 13. ただの TTS エンジンにする（2026-09-10・裁定 124・126＝v1.1.0）

司令官の評価（逐語・RTX 3090・ドライバ 537.58・v1.1.0 の前の版を清潔導入した後）＝
「**このアプリは開発者向けではない。ただのTTSエンジンとしてvoiceroid2のような動作を期待している。
アプリ終了でサーバーを落とし、VRAMを開放せよ。**」設計＝`docs/design/ben-d-launcher.md` §30。

### 13-1 窓を閉じたらアプリが終わる（裁定 6 の反転）

- `App.xaml` の `ShutdownMode` を **`OnLastWindowClose`** にし、`MainWindow.OnClosing` は
  **取り消さない**（隠さない）＝鳴っている音を止め、見張りの購読を外して閉じる。
- 終了の後始末は **`Services/Server/ShutdownSequence`**（WPF に触れない＝xUnit から撃てる）＝
  `StopServerTree(server, 15 秒)` が **どの状態からでも** `StopAsync`（ツリー kill）を待ち切り、
  `DisposeAsync` まで通してから返る。期限切れは理由 1 行を返して諦める（終われないアプリを作らない）。
- **トレイは無くなった**＝`NotifyIcon`・トレイのメニュー（開く／サーバ起動／停止／終了）・
  `ReleaseFlavors.TrayText`／`App.StateLabel` は消えた。`.csproj` の `UseWindowsForms` も落としてある
  （WinForms を 1 行も使わない＝発行の 1 exe がその分小さくなる。**配布樹の外**なので A-1 は動かない）。
  最小化は**タスクバー**へ。単一起動の錠と「2 個目の起動で 1 個目の窓を前に出す」合図はそのまま。
- 本体（読み分けちゃん2）の一括起動は従来どおり引数なしでこの exe を起こしてよい（裁定 103）＝
  窓が出たまま残り、閉じるのは利用者である。

### 13-2 ドライバに合わない変種は選べない

- **`Services/Gpu/VariantRecommendation`**（新設・純関数）＝`Recommend`（ドライバの帯から勧める変種）・
  `IsBelowMinimum`（下限に届かない GPU 変種か）・`BlockReason`（断る 1 行）。下限の定義は
  `DriverRequirement`（cu130 ≥ 580.00・cu126 ≥ 528.33）1 箇所のまま＝門（`VariantGate`）と同じ数字。
- 初回取得ウィザードは窓が開いたところで **`RefreshDriverAsync`** を 1 度撃ち（`nvidia-smi` 経路）、
  **まだ利用者が選んでいなければ**勧める変種へ選び直す。下限に届かない変種を選んでいる間は
  「この構成で取得を始める」が**押せない**（`CanGoNext`）＝理由 1 行が変種の段に出る。
- 設定頁の「適用」も同じ規則で断る（`SettingsViewModel.VariantBlockReason`）。
  **`cpu` はいつでも選べる**・**ドライバの版が読めない機体では止めない**（読めなかったと名乗るだけ）。
- **もう保存されている変種がドライバに合わないときは、断って終わらせず取得へ連れて行く**（裁定 126 ⑽）。
  前の版（v1.0.2）で `cu130` のまま取得を通した機体には `settings.json` の `variant` と
  その実行系が残るので、選ばせない規則だけでは直らない＝起動のたびに断られ続ける。
  そこで `MainViewModel.StartServerAsync` は、GPU の列挙で読めたドライバの版が
  保存された変種の下限に届かないとき（`VariantRecommendation.IsBelowMinimum`）、
  **起こす前に**次の 4 つを出す。
  1. サーバを起こさない。
  2. 理由 1 行を状態帯とログ檔へ（`VariantRecommendation.StartRefusalReason`＝
     「このドライバ（537.58）では CUDA 13.0 は動きません（下限 580.00）。CUDA 12.6 に切り替えて取得します。」）。
  3. 状態帯に `取得が未了です。` と「取得へ進む」（§12-2 と同じ仕掛け＝`StatusViewModel.ApplyAcquisition`）。
  4. **初回取得ウィザードをこの走行で 1 度だけ自動で開く**（`MainViewModel.WizardRequested` →
     `MainWindow` が開く＝ViewModel は WPF に触れない）。開いたウィザードには理由が Trail に入り、
     勧める変種が初期値に入る（`FirstRunViewModel.Preselect`）。**選び直す口は開いたまま**＝
     利用者が自分で選べばそちらが正本になる（裁定 4）。取得が終われば普通の路
     （実行系を取り寄せ、モデルは在るので飛ばし、「起動の確認」）へ続く。
  - **この扱いは下限未満の 1 つだけ**＝GPU が見つからない・検分が読めないといった門の断りは
    今まで通り理由 1 行で終わり、ウィザードは開かない。**ドライバの版が読めない機体も今まで通り**。
  - **取得を始めずにウィザードを閉じても「取得へ進む」は消えない**＝閉じた後の見直しが見るのは
    檔（`AcquisitionCheck`）だけで、そこにはドライバが写らない（この機体では実行系もモデルも
    「揃っている」）。断ったドライバの版を覚えておき、変種が下限未満のままの間は状態帯の 1 手を
    出し続ける。変種を替えて取得を通せば自分で消える。
  - **ウィザードは渡されたドライバの版を捨てない**＝ウィザード自身の検分は `nvidia-smi` の 1 本
    だけなので、落ちた回・期限切れの回に版を失うと勧める変種が断られた方へ戻ってしまう。
    自分が読めなかった回だけ主窓の版を使う（読めた回はそちらが新しい）。

### 13-3 ログ檔・`voices.json`・GPU の焼き付け

- **ログ檔**＝状態帯に出た 1 行**と初回取得ウィザードの 1 行**は `<データ樹>\logs\launcher-<yyyyMMdd>.log`（UTF-8・日ごとに 1 檔・
  `HH:mm:ss 本文`）にも落ちる（`Services/Logging/LauncherLogFile`＝1 行ごとに開いて足して閉じる・
  **IO の失敗は投げない**）。継ぎ目は `StatusViewModel.LogSink` と `FirstRunViewModel.LogSink` の 2 つで、
  差さっていなければ檔には残らない（ウィザード側は清潔導入で詰まる回＝状態帯を 1 度も通らない回のために在る）。
- **`voices.json` は起動のたびに書く**（`LauncherComposition.PrepareVoices`）＝サーバを止めている間に
  台帳を直した回も別名表に届く。書き手は既に在る檔を読み直して `ref_latent` を残す（契約 ⑷ 4-3）。
- **GPU の焼き付け**＝GPU 変種の起動が通った回に、設定に `gpuUuid` が無ければ解決した個体を
  `SettingsDefaults.ApplyGpu` で焼いて保存する（既に選んである設定は上書きしない）。

### 13-4 検分（この機体・2026-09-10）

```powershell
dotnet test launcher -c Release --nologo    # 743 合格・1 skip（総数 744。裁定 126＝新設 45 本＋是正 14 本＋⑽ の 12 本）
```

**窓は立てていない**（UIA 実射なし）。`installer-build.ps1` も走らせていない＝
VRAM が実際に返ることは**プロセスを殺す意味**でしか確かめていない（実機の実射は未了）。

---

## 14. 文言を 1 箇所に集めた（2026-09-11・v2.0 段 C＋段 A の A-4／A-5）

**裁可された正本**＝`docs/design/charter.md`（憲章）・`docs/design/v2-copy.md`（文字）・
`docs/design/v2-spec.md`（画面）・`docs/design/v2-plan.md`（入れる順）。この節はその当て込みの記帳である。

### 14-1 `ViewModels/UiStrings.cs`＝利用者に見える文字列の唯一の出所

- **`UiStrings` は文・`UiText` は数**。`UiText`（`GiB`・`ms`・桁区切り・`Bytes`）は**1 字も触っていない**＝
  同じ値が記録と画面で違って見えない、という決め事はそのまま。
- `Views/*.xaml` の `Text=`／`Content=`／`Header=`／`ToolTip=`／`AutomationProperties.Name=`／
  `…HelpText=` は**すべて `{x:Static vm:UiStrings.…}`**。**画面に日本語を直に書かない。**
- **寄せない物**＝ログへ落とす文・例外の文・JSON の鍵・契約の欄名（`v2-plan.md` §3 の表）。
  工学側の語彙は資産なので触らない。

### 14-2 `WordLintTests.cs`＝語の検分が試験として毎度回る

舐めるのは **3 つ**（是正・段 C の検分で ⑶ を足した）＝⑴ `UiStrings` の public な文字列（リフレクション）
⑵ 試験の出力へ写した `Views/*.xaml` の可視属性**と要素の本文**（XML の註は除く）
⑶ 同じく写した `ViewModels/*.cs` の文字列リテラル（`//`・`///` の行は読まない）。
⑶ が無かった 1 巡目は「ViewModel が組み立てて束縛に載せる 1 行」が 1 つも見えず、
`VariantGate`／`VoicesViewModel`／`FirstRunViewModel`／`SettingsViewModel` の隠す語 6 本が
「0 件」の報告の裏を素通りしていた。**免除は `LogOnly` の名指し 1 枚だけ**（見分けの標識・
記録へ落とす 1 行・`UiText` の数の書式）＝新しい直書きは必ずここで落ちる。
この 2 つに憲章 §6-1 の隠す語（変種・実行系・台帳・潜在・焼き・暖機・サーバ・ポート・接続先・裁定・
配布版／配布物・逐語・試し撃ち・`sha256`・`GiB`・`RTF`・`18088` ほか）が 1 つでも現れたら落ちる。
**技術語は弾かない**（CUDA・ROCm・GPU・NVIDIA・AMD・Radeon・bf16・FP32・Windows・wav・Python）。
`Services/` と `Contracts/` は**舐めない**＝舐めると内部の識別子（`Ledger.cs` の `"sha256"` の鍵名ほか）を巻き込む。

### 14-3 状態の語は 6 → 3（内部の `ServerState` は据え置き）

`StatusViewModel.StateLabel` が `BandText` の語を返すようになった＝
**準備しています…／使えます／止まっています／止まりました**（丸は灰・緑・赤の 3 色）。
`enum ServerState` の 6 値・状態機械・契約は 1 つも動いていない＝**畳んだのは表示だけ**である。
台本（`probe/d-launch-probe.ps1`）の `$T` も同じ回に直した＝`Ready`／`Warming` は同語に畳み、
`Stopped` は「止まっています」、`Failed` は「止まりました」。
**`state starts at stopped` の 1 本は期待値ごと書き直した**＝起動釦が無くなった機体は開いた瞬間に
準備が走るので「止まっています」を通らない（いまは「赤で開かないこと」を見る）。
`$T.Rebuild`／`$T.ClearCache` は**鍵ごと廃した**（段 A-0 が id 引きへ移した）。

### 14-4 畳み 4 枚と、消した 7 件

| 画面 | 畳み | ふだん出す物 | 畳みの中 |
|---|---|---|---|
| 詳しい状態 | `StatusAdvancedExpander` | 大きな 1 行・止まった理由・準備が要る／一式を入れ直す・食い違いの告知 | **11 行**（上限 12＝憲章 原則 7） |
| 発話テスト | `TryAdvancedExpander` | 声・話し方の指示・速さ・品質の 2 択 | 歩数・3 つの効き・乱数の種・所要 ms と RTF |
| 声 | `VoicesAdvancedExpander` | 一覧（列 4）・一覧の下の薄字・追加の 3 欄・試聴／停止／削除／更新 | 1 人あたりの量・下ごしらえ・最初から入っている声を入れ直す |
| 設定 | `SettingsAdvancedExpander` | 4 つ（自動で読み上げ・よく使う声を先に準備・声のファイルの場所・ログを開く）＋**畳みの外の下端**に〔適用〕〔取り消し〕 | **7 行**（上限 12・是正で数え直した） |

**設定の 詳細 が 7 行であること**（是正・段 C の検分で数え直した）＝グラフィックス／動かし方／
音質と速さ／声の下ごしらえ／読み上げの動作／ファイルの場所／一時ファイル。`v2-copy.md` §4 の
10 行との差は 3 つ＝⒜ 6 行目「更新のとき、新しくなった分だけ取り直す」は**段 E の持ち物**でまだ無い
⒝ 9 行目「記録」は ふだんの設定 の `SettingsOpenLogButton` 1 つに寄せた ⒞ 10 行目「変えた設定は…」＋
〔適用〕〔取り消し〕は**畳みの外**に置いた（ふだんの 4 つを保存するのに「詳細」を開かせないため）。
段 E が入れば 8 行になる。

**入らなかったので消した 7 件**（移さずに消す＝原則 7 の検分文）＝
`SettingsShowMemoryCheck`・`SettingsWarmupStagesBox`・`SettingsWarmupVoicesBox`・
`SettingsEmptyCacheBox`／`SettingsEmptyCacheNoteText`・`SettingsReadyTimeoutBox`／`SettingsReadyTimeoutNoteText`。
**`settings.json` の鍵は 1 つも減らしていない**＝手で書けば従来どおり効く（`WarmupStagesText` の
検分もそのまま生きている）。台本はこの 7 つを 1 度も押さず 1 度も読んでいないので、台本の書き替えは要らなかった。
錠は `AutomationIdsTests`（生存 132・重複 0・段 A／B／C の新設・**退役 7 が本当に消えている**・
**畳みの中の id を台本が開けるか**）。最後の 1 本は是正・段 C の検分で足した＝
畳んだ `Expander` の中は **UI Automation から見えない**ので、id が在るだけでは台本は触れない。
台本の `-Id '…'` を全部拾い、その id が `IsExpanded="False"` の畳みの中に居るなら、
台本が同じ畳みを `Open-YwkFold` で開いているかを見る。

### 14-5 検分（この機体・2026-09-11・窓は 1 度も立てていない）

```powershell
dotnet build launcher -c Release --no-incremental   # 0 警告 0 エラー
dotnet test  launcher -c Release --nologo           # 818 合格・1 skip（工事前 809＋1）
powershell -File build\run-tests.ps1                 # 契約テスト 372 passed・upstream/ clean
powershell -File probe\d-launch-probe.ps1 -DryRun    # 構文 0 エラー・「nothing was touched.」で終わる
```

**`installer-build.ps1` は走らせていない**（この席の持ち場ではない）。**アプリは 1 度も起こしていない**（実射は段 H）。

### 14-6 是正（同日・検分 22 件の当て込み）

**画面に出る文と記録に落とす文を割った**＝門（`VariantGate`）の告知は
`GateDecision.Notices`（画面・利用者の言葉）と `GateDecision.Trail`（記録・検分の観測）に分かれ、
`ServerStartResult.NoticeTrail` で窓まで届く。`StatusReasonText` の束縛先は内部の `Reason` から
**帯の言い直し** `BandReasonText` に替えた（`v2-spec.md` §2-2＝§2-1a の 3 部品を帯と共用する）＝
「実行系を起こす段が …」「…（裁定 83）」が画面に載る道を塞いだ。
`StatusViewModel.ComposeStartOutcome` も内部の理由を告知へ混ぜなくなった。

**「いま動いている場所」は製品名だけ**＝`cuda:0` と `gfx1151` の綴りを落とした
（`v2-copy.md` §1-2 の 40 行目・憲章 附録 5）。名が読めない回は `GPU`／`CPU（GPU を使いません）`。

**〔適用〕が退役した欄で止まらなくなった**＝準備運転の段・使う声が読めない `settings.json` でも
元の値を持ち越して先へ進み、理由は記録へ落ちる（`SettingsViewModel.Log`）。
`WarmupStagesText` の文言も鍵の名（`warmupStages`／`warmupVoices`）で綴り直した＝記録専用だから。

**〔報告用のログを保存〕が〔ログを開く〕と別の仕事になった**＝`AppPaths.LogDir` の記録を
新しい順に 5 日ぶん 1 檔（`report-<日時>.log`）へまとめ、「記録をまとめました。〈路〉」を
`AboutSavedLogText` に出し、脇の `AboutOpenReportFolderButton` でその檔を開く（`v2-copy.md` §8）。

**帯の 1 手の札を 1 つに寄せた**＝`BandText` の ⑶ が `UiStrings.StatusRebuildButton`
（「動かすための一式を**入れ直す**」）を引く。`v2-spec.md` §2-2 の「新しくする」は copy に揃える。

**声の名付けの 5 行と、下ごしらえを外せなかった 2 行を `UiStrings` へ**＝
`VoiceNameValidator` は `VoicesMessageText` に直に出るのに `v2-copy.md` §1-8 に行が無く、
行ごとの当て込みで拾えていなかった（話者・参照・檔が残っていた）。

**台本の穴を 2 つ塞いだ**（`probe/d-launch-probe.ps1`）＝
⑴ 節 2（設定と GPU）が `SettingsAdvancedExpander` を開かずに `SettingsRefreshGpuButton` を押していた＝
`Invoke-ButtonById` が投げ、本体に `catch` が無いので**そこで走行ごと終わる**形だった。
⑵ 詳しい状態の 13 個が `StatusAdvancedExpander` の中へ移ったのに、台本は 1 度も開いていなかった＝
新しい助手 `Open-YwkStatusDetails`（タブ＋畳み）を足し、`TabStatus` を選ぶ 15 箇所を全部これに替えた。
併せて門の段の期待値も直した＝画面はもう台帳の綴り（`cu130`）を出さないので、
`Get-YwkVariantName` で「CUDA 13.0」に読み替え、「見られません」→「**見つけられません**」に。
