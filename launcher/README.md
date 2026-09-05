# launcher/ — ランチャ UI（**便 D**・便 A では本 README のみ）

> **便 A の時点で、このディレクトリには実装が無い。**便 D が入る前の申し送りだけを置く。
> 正典＝`decisions.md`。契約＝`docs/contract.md`。受け入れ条件＝`docs/acceptance.md`。

## 1. 何を作るか

**WPF（.NET）のランチャ**。この機体に .NET SDK 10.0.400 がある（`decisions.md` 25）。

ランチャは 3 つの役目を持つ。

1. **初回取得**＝台帳（`ledger/*.json`）を読み、実行系（埋め込み Python＋wheel）とモデルと
   `vc_redist.x64.exe` を取得し、sha256 で検証して `%LOCALAPPDATA%\irodori-tts-ywk\` に展開する。
   進捗は `server/ywk_fetch_models.py` の**進捗 JSON 行**を読んで出す。
2. **常駐**＝env を組んで
   `runtime\<variant>\python.exe -m ywk_server --host 127.0.0.1 --port 18088` を子プロセスで起こし、
   stderr を取り込んで状態を判定する。**bat は使わない**（文字コード事故の反面教師＝
   `research/local-additions/`）。
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
| 5 | 焼く既定＝`preload=true`・`empty_cache_interval=0`・`allow_no_ref_voice=false`＋alias「デフォルト」・v4.1-Small 決め打ち・`voices_dir` は絶対パス・ready 待ち 120 s | `decisions.md` 7 |
| 6 | **`voices/` と `voices.json` の所有者はランチャ**（上流の登録 API 4 口は使わない＝日本語名が 400 になる） | `docs/contract.md` ⑷ |
| 7 | 話者メタ（表示名・caption 既定・既定パラメータ）は**配布版の台帳** `voices/voices.ywk.json`（上流に置き場が無い） | 同 |
| 8 | 既定 steps は **40**（上流既定）。UI のプリセットで 10 を選べる。**本体の指定が来たら必ず勝つ** | `decisions.md` 10 |
| 9 | CPU 合成は**UI にだけ**露出し、配信用途では非推奨と明記する | `decisions.md` 13 |
| 10 | 透かしは**既定 ON**・切る経路を持たない | `decisions.md` 9 |
| 11 | 状態判定は stderr の **3 行**＝`ywk_server <版> upstream=…`／`Uvicorn running on`／`runtime loaded in` | 設計書 §4-1・`docs/acceptance.md`「ログ」行 |
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
| 4 | 上流は device 文字列を検査せず、`preload=true` なら **6〜11 s 後に落ちる** | ランチャ（と wrapper）が起動前に 0 s で弾く |
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
| `IrodoriTtsYwk.Launcher/App.xaml(.cs)` | 単一起動（Mutex）・トレイ（開く／サーバ起動／停止／終了）・終了時にツリー kill |
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
| 窓 | `MainWindow`・`MainTabs`・`MainStartButton`・`MainStopButton`・`MainFirstRunButton`・`MainStateText`・`MainEndpointText`・`MainVersionText`・`MainHeaderText` |
| タブ | `TabStatus`・`TabVoices`・`TabTry`・`TabSettings`・`TabAbout` |
| 状態 | `StatusStateText`・`StatusReasonText`・`StatusGpuText`・`StatusVariantText`・`StatusEndpointText`・`StatusDeviceText`・`StatusWarmupText`・`StatusPrecomputeText`・`StatusMemoryPanel`／`StatusMemoryText`・**`StatusLatentCacheText`**・`StatusVoiceMemoryText`・**`StatusNoticesText`**・`StatusLogBox`・`StatusUpstreamMismatchText` |
| 話者 | `VoicesGrid`・`VoicesBrowseButton`・`VoicesSourcePathBox`・`VoicesNewNameBox`・`VoicesNewCaptionBox`・`VoicesAddButton`・`VoicesPreviewButton`・`VoicesStopPreviewButton`・`VoicesRemoveButton`・`VoicesRefreshButton`・`VoicesPrecomputeButton`・`VoicesSelectedMemoryText`・**`VoicesPreviewBlockedText`**・**`VoicesRemoveBlockedText`**・`VoicesMessageText` |
| 試し撃ち | `TryInputBox`・`TryInputLengthText`・`TryVoiceCombo`・`TryStepsPreset10`／`TryStepsPreset40`／`TryStepsBox`・`TryCaptionBox`・`TryCfgTextBox`／`TryCfgCaptionBox`／`TryCfgSpeakerBox`・`TrySpeedBox`・`TrySeedBox`・`TrySynthesizeButton`・`TryReplayButton`・`TryStopButton`・`TrySaveButton`・`TryResultText`・`TryMessageText` |
| 設定 | `SettingsGpuCombo`・`SettingsRefreshGpuButton`・`SettingsDriverText`・`SettingsVariantCombo`・`SettingsPrecisionCombo`・`SettingsPortBox`・`SettingsWarmupCheck`／`SettingsWarmupStagesBox`／`SettingsWarmupVoicesBox`・`SettingsPrecomputeCheck`・`SettingsEmptyCacheBox`・`SettingsAutoStartCheck`・`SettingsShowMemoryCheck`・`SettingsReadyTimeoutBox`・`SettingsDataDirText`／`SettingsModelDirText`／`SettingsVoicesDirText`／`SettingsRuntimeRootText`／`SettingsAppDirText`・`SettingsApplyButton`／`SettingsRevertButton`・`SettingsMessageText` |
| このアプリについて | `AboutDisclaimerText`・`AboutVersionText`・`AboutUpstreamText`・`AboutWatermarkText`・`AboutEthicsText`・`AboutLicensesDirText`・`AboutNoticesPathText`・`AboutLicenseList` |
| 初回取得 | `FirstRunWizard`・`FirstRunStepTitle`・`FirstRunStepNumber`・`FirstRunNoticesBox`・`FirstRunAcceptCheck`・`FirstRunVariantCombo`・`FirstRunVariantNoteText`・`FirstRunDriverText`・`FirstRunSizeText`・`FirstRunProgressBar`／`FirstRunProgressText`・`FirstRunTrailList`・`FirstRunBackButton`／`FirstRunNextButton`／`FirstRunCancelButton`・`FirstRunMessageText` |

**タブの中身は選ぶまで作られない**（WPF の `TabControl` は遅延生成）＝台本は `SelectionItemPattern`
でタブを選んでから、そのタブの id を探すこと。

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
| **GPU 全体** | `gpu_used` / `gpu_total` | `mem_get_info`（**カード全体・他プロセス込み**） |

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
子プロセス 0）・**プリセット 11 名の 4 箇所突合**・**削除の押せない理由（`HelpText` と表示が同文言）**・
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
| ⑴ | 94 ⑴ | **初回取得ウィザードの自動進行**＝取得→展開→モデル→起動は成功したら自動で次へ。押下は **5**（同意チェック・同意して次へ・変種・取得を始める・試し撃ちへ）。失敗した段だけで止まり、文言は「もう一度」＋理由 1 行。段の出入りは `Trail` に `― <段の名>` として残る。「中断」は各段で効く（`FirstRunViewModel.AdvanceAsync`） |
| ⑵ | 90 Q-E2 ⑶ | **取得キャッシュの削除**＝⒜ 初回取得を通した後、「試し撃ち」で 1 射 200 が返ったら `<data>\cache` の中身を残らず消す（置き場自身は残す・消したバイトをログと Trail に）⒝ 設定に手動の「取得キャッシュを消す（n GiB）」⒞ **消す前の関門**＝`python.exe` の在否と `settings.runtimeLedgerSha256` と配布樹の台帳の一致（どちらか欠ければ 1 バイトも消さない）。**名前で選ばない**のは、前の台帳の原檔・打ち切った `.part`・名前が変わった檔が残って「展開後（cache 削除後）」の実測が合わなくなるからである（`Services/Ledger/CacheCleaner.cs`） |
| ⑶ | 91 | **`settings.json` に `runtimeLedgerSha256` と `installedAppVersion`**（展開が通った直後に焼く）。起動時に配布樹の台帳と突き合わせ、食い違えば状態帯に 1 行と「実行系を組み直す」1 手（cache から再展開・cache が無ければ足りない檔だけ取得から）（`Services/Ledger/RuntimeStamp.cs`・`MainViewModel.RebuildRuntimeAsync`） |
| ⑷ | 94 ⑶ | **展開係数を変種ごとの実測に**＝cu126 **1.69**・rocm-* **2.98**・cpu **3.60**・cu130 は未実測で **3.3**。見積りの 1 行は「（展開は実測 2.98 倍）」「（展開は推定 3.3 倍）」と出し分ける（`FetchPlanner.ExpansionFactorFor`） |
| ⑸ | 92 の low 6 | ⒜ `UiText.Bytes` の綴りを **GiB／MiB／KiB** に（1024 進なのに GB と綴っていた）⒝ 「（最大 …）」を**使用量の隣**へ・`memory.error` は「（一部の欄が読めませんでした：…）」⒞ 「未対応」と **「—（サーバが動いていません）」** の出し分け ⒟ `MemoryStatus.Latents` が JSON の `null` でも落ちない（`EffectiveLatents`）⒠ `MainViewModel` の Failed 2 箇所を `IServerProcess.ReportPreflightFailure` 経由に ⒡ 「GPU メモリの欄」は「適用」した瞬間に効く（`StatusViewModel.MemoryPanelVisible` へ束縛）⒢ 409 の待ち行列は**受け取られるまで落とさない**（3 回落ちたら諦めて理由を残す） |
| ⑹ | §20-5 ⑴ | **窓が 60 秒黙る**の手当て＝`AsyncRelayCommand` の受け口を**全例外**に（型の数え上げをやめた）・`ProcessRunner` と `ServerProcess` に**「起こす」段そのものの期限 3 s**（`Process.Start` を別スレッドへ逃がして待ち、見限った個体は後から起きたら殺す。1 度の「サーバ起動」で同じ `python.exe` を 3 回起こす＝窓の列挙・門の検分・子なので、3 つとも止まって 9 s＝無人検分の budget 10 s の内側）・`MainViewModel` は列挙が落ちても理由 1 行を残して先へ進む・`Status`／`Voices`／`Try`／`Settings`／`FirstRun` の各手の `Faulted` を画面へ繋いだ |

### 11-2 UIA の名前（§7-3 への追加）

| 画面 | 足した id |
|---|---|
| 状態 | `StatusRebuildRuntimeText`・`StatusRebuildRuntimeButton`（裁定 91 の 1 行と 1 手＝食い違っていないときは**木に出ない**。焼き印の無い樹は起動時にいまの台帳で焼き直して黙る） |
| 設定 | `SettingsClearCacheButton`（文言は `取得キャッシュを消す（n GiB）`／空なら `（空です）`）・`SettingsCacheMessageText` |

`StatusMemoryPanel` は `Visibility` を `StatusViewModel.MemoryPanelVisible` に束縛したので、
設定で外して「適用」を押すと**その場で木から消える**（以前は次の起動まで残った）。

### 11-3 `settings.json` に足した 2 欄（契約 ⑻）

```json
{ "runtimeLedgerSha256": "<ledger/runtime-<変種>.json の sha256（小文字 hex 64 字）>",
  "installedAppVersion": "v0.1.0" }
```

どちらも **null を許す**（古い `settings.json`・台本で組んだ樹は「いつ組んだか判らない」＝黙る）。
`docs/contract.md` ⑻ の欄の表は本席の担当パス外なので**卓が 2 行足すこと**。

### 11-4 検分（この機体・2026-09-05）

```powershell
dotnet build launcher\IrodoriTtsYwk.sln --nologo --no-incremental   # 0 個の警告・0 エラー
dotnet test  launcher\IrodoriTtsYwk.sln --nologo                    # 554 本（499 → +55）
```

新規の釘は `IrodoriTtsYwk.Launcher.Tests/RoundThreeTests.cs` の 1 檔にまとめてある。
既存檔で直したのは**綴りと文言が動いた 5 本**と、`IServerProcess` に口が増えた偽物 1 つだけ。
**実 GPU・実ポート・子プロセス・外への取得には 1 つも触れていない**
（起こすのは「起こせない実行檔」1 本で、それも起きない）。
