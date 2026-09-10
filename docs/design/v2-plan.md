# v2.0 工事計画（実装の段取り・工学席の檔）

> **位置づけ**＝`docs/design/charter.md`（2026-09-11 裁可・正本）が**思想**、`docs/design/v2-spec.md` が**画面と文言**、
> `docs/design/v2-copy.md` が**利用者に届く文**。この檔は**工事の順序と落とし所**だけを書く。
> ここは作る側の帳面なので内輪の語（変種・実行系・台帳・潜在・門）を**そのまま使う**。
> 利用者に届く面へこの檔の語を写さないこと（憲章 原則 8）。
>
> **決裁 130 で spec の既定案からずれた点が 1 つある**＝**Q3 の同意チェックは残る**。
> 初回の 1 度だけ通し、同じ版の通知は二度と出さない（`CanAcceptNotices`／`acceptedNoticesSha256` は据え置き）。
> `v2-spec.md` §3 段 1・§7 B-7・§8 Q3 の「チェックを外す」は**この計画では採らない**。
>
> **タブは 3 枚＋歯車**（憲章 §4-13・決裁 130）＝`v2-spec.md` §2-0／§9 も**同じ形に揃った**（「5 枚のまま」の逸脱は撤回済み）。
> `TabStatus`／`TabAbout` の **`AutomationId` は 1 字も消さない**＝歯車の中の層へ**要素ごと移す**（§2 段 A・A-3）。
>
> **報告の受け皿は決まった**＝X の `@yomiwakechan`（裁定 131・2026-09-11）。リポが public になったこと（裁定 120）とは別の決めで、
> **GitHub の Issues は受け皿にしない**。公式ページの脚・〔このアプリについて〕・使い方の「困ったときは」・リリース文の
> **4 面に同じ 1 行**を置く（文は `v2-copy.md` が正）。
>
> **更新の差分（段 E）の正本は `v2-spec.md` §11 である。**名前（`PresetSync.Plan`／`RuntimeDiff.Plan`／`ModelDiff.Plan`）・
> データ樹の写しの路（`runtime\<variant>\.ledger.json`）・台帳の新しい欄（`preset_md5`）は**そちらの綴りを使う**。

**いまの実測（工事前）**＝xUnit **743＋skip 1**・契約テスト **369**・`installer-build -All` **20 門**・最新版 **v1.1.0**。
**工事後の版**＝`v2.0.0`（`launcher/Directory.Build.props` の `AppDisplayVersion` と `server/ywk_server.py` の `YWK_VERSION` の 2 箇所）。

---

## §0 この工事で 1 文字も動かない物（全段共通の錠）

| 錠 | 実物 | 破ったときに起きること |
|---|---|---|
| **本体との契約** | `docs/contract.md` ⑴〜⑼ の既存の口・欄・`code`・`schema` 番号 | 読み分けちゃん2 が話せなくなる。**欄の追加だけは互換**（⑻）＝段 D はその 1 件のみ |
| **`AppId`（GUID 2 本）** | `installer/irodori-tts-ywk.iss:47`／`:52` | 上書き更新が「別アプリの新規導入」になり 2 本並ぶ |
| **導入先・資産の檔名** | `irodori-tts-ywk-cuda`／`-radeon`・`irodori-tts-ywk-setup-vX.Y.Z-cuda.exe` | 更新路・`site/index.html` の JS・`probe/e-install-probe.ps1`・`N:` の受け渡しが全部外れる |
| **取得台帳** | `ledger/*.json` の檔名と中身（`runtime-rocm-gfx1151.json` は版の判定にも使う＝`ReleaseFlavor.cs:34-53`） | 版の誤判定・取得の停止 |
| **同梱の声** | `voices/presets/*.wav`・`voices/presets.json` の `id`／`secondary.file` | 話者 id が表示名そのもの（契約 ⑷ 4-1）＝利用者の台帳が迷子になる |
| **内部の識別子** | `settings.json` の既存の鍵・`variant`／`precision` の値・env `YWK_VARIANT`・`enum ReleaseFlavor`・`RuntimeVariants.*`・ログの語 | 既存機体の設定が読めない・契約テストが落ちる |
| **既存の `AutomationId` 139 個** | `launcher/README.md` §7-3 の表・`Views/*.xaml` | 無人検分（`probe/d-launch-probe.ps1`・`wizard-probe.ps1`・`e-install-probe.ps1`）が落ちる |

> **錠から外れた物が 1 つある＝データ樹の路**（`decisions.md` 133・2026-09-11）。
> `%LOCALAPPDATA%\irodori-tts-ywk\`（両版で共有）は**もう固定ではない**＝**版ごとに分ける**
> （`…\irodori-tts-ywk-cuda\`／`…\irodori-tts-ywk-radeon\`）。**単一起動の錠（Mutex）も版ごとに割る。**
> **導入先・`AppId`・資産の檔名・台帳名は上の表のとおり据え置き**（動くのは**データ樹と Mutex の 2 つだけ**）。
> 旧い共有樹が在る機体は、**初回の v2.0 起動で版の樹へ移す**（段 E-3）。
> `settings.json` の `dataDir` と env `YWK_LAUNCHER_DATA_DIR` の明示指定は**従来どおり優先**＝鍵は 1 つも減らさない。
>
> **`AutomationId` の規則**＝**改名しない・削除しない。移すだけ。新設には新しい id を付ける。**
> **畳み（`Expander`）と隠した層の中は、開くまで UIA から見えない**（WPF は展開まで実体化しない）。
> ゆえに**台本に「開く 1 手」を先に足してから**要素を畳みへ移す（順を逆にすると無人走行が測る前に落ちる）。

---

## §1 段の並びと「緑のまま」の定義

```
段 A 画面の骨（配置だけ・文言は触らない）      ← 台本の先行改修 A-0 が前提
段 B はじめの準備（自動決定・3 段・1 本のバー・同意 1 度）
段 C 文言（UiStrings の新設・語の検分試験・台本の語表）
段 D 本体が使っている間は譲る（契約に欄 1 つ）
段 E 更新の差分取り直し（裁定 115 の穴）
段 F 名札・文書・インストーラ（RTX（CUDA）／Radeon（ROCm）・install.md の分離）
段 G 検分（契約・xUnit・20 門・無人検分）
段 H RTX 機の実射台本
```

**「緑のまま」の定義**＝各段の終わりで ⑴ `dotnet build` 0 警告 ⑵ xUnit 0 失敗（skip 1 のまま）
⑶ 契約テスト 0 失敗 ⑷ `probe/d-launch-probe.ps1 -DryRun` が構文と id 解決で落ちない、の 4 つが揃うこと。
**⑷ が段 A・B・C の順序を縛る**＝**台本 → 実装**の順でしか進めない。

**直列でなければならない所**＝A-0 → A → C（語）／B の畳み → 台本の段番号 → B の実装／D の server → contract → 契約テスト → ランチャ。
**並行してよい所**＝E（更新の差分）は A〜C と独立・F の文書側（`copy.md` 由来）は A〜C と独立・
F の名札（`FlavorLabel`）は A〜C の後ならいつでも。

---

## §2 段ごとの工事

### 段 A — 画面の骨（タブ 3 枚＋歯車・帯・詳細の畳み・起動釦の撤去）

**この段は配置だけを替える。文字列は 1 つも替えない**（替えるのは段 C）。理由＝台本は `MainStateText` の
**本文**を語で突き合わせる（`probe/d-launch-probe.ps1:426`・`:506`・`:1666`・`:1931`・`:1935`）ので、
配置と語を同時に動かすと落ちた原因が分けられない。

#### A-0（先行・台本だけを直す・実装は入れない）

| 直す所 | 内容 |
|---|---|
| `probe/d-launch-probe.ps1` | ⑴ **`Open-YwkFold -Root <win> -Id <expander>` を 1 本作る**（**要素が無ければ黙って真を返す**）＝開けるのは `StatusAdvancedExpander` だけでなく **`SettingsAdvancedExpander`・`TryAdvancedExpander`・`VoicesAdvancedExpander`・`FirstRunAdvancedExpander` の 5 つ**。⑵ 同じ形の `Open-YwkGear`（`MainGearButton` が在れば押す）を `Select-Tab -TabId 'TabStatus'`／`'TabAbout'` の**前**に置く。⑶ `Find-ByNameLike -Text $T.Rebuild`（`:1166`・`:1177`）と `$T.ClearCache`（`:1441`）を **id 引き**（**`StatusRebuildRuntimeButton`**／`SettingsClearCacheButton`）へ替える＝札の文字に二度と縛られない |
| **畳みを開く 1 手を入れる行**（A-1／A-5 で釦が畳みの中へ入るため。入れないと `Invoke-ButtonById` が「要素が無い」で投げ、最初の restart で無人走行が死ぬ） | `MainStartButton`＝**`:424`・`:505`・`:1351`・`:1878`**／`MainStopButton`＝**`:2260`**／`MainFirstRunButton`＝**`:1830`**／`SettingsClearCacheButton`＝**`:1438`** の **7 箇所**。いずれも直前に `Select-Tab -TabId 'TabSettings'` → `Open-YwkFold -Id 'SettingsAdvancedExpander'` を置く |
| `probe/wizard-probe.ps1` | **助手は読み込ませない**（この台本は「ウィザードの窓が開いているか」を答える 1 行の**読み取り専用**で、`common.ps1` を dot-source せず、押す関数を 1 つも持たない＝`:1-4`・自前の `Get-TextById`）。直すのは **`:72-74` が読む 3 つ**（`FirstRunStepTitle`／`FirstRunStepNumber`／`FirstRunMessageText`）の**出力の読み方だけ**＝段番号の綴りが `3 / 7` から `3 / 3` に変わる。**判定は持たせない** |

**要点**＝この 3 つは**いまの実装でも通る**書き方にする（`-TimeoutSeconds 1` で探して無ければ素通り）。
だから A-0 単独で走らせて緑を確認してから A へ入れる。

#### A-1 起動釦の撤去と移設

| 触る所 | 内容 |
|---|---|
| `Views/MainWindow.xaml:37-51` | 上の `Border`（`#F3F3F3` の帯）を**削る**。中の 3 釦は要素ごと `SettingsView` へ移す |
| `Views/SettingsView.xaml` | 「詳細（上級者向け）」の畳みの中に「読み上げの動作」の枠を作り、`MainStartButton`／`MainStopButton`／`MainFirstRunButton` を**そのままの id で**置く |
| 束縛の付け替え | `SettingsView` の `DataContext` は `SettingsViewModel` なので、`Command="{Binding Status.StartCommand}"` は当たらない。`{Binding DataContext.Status.StartCommand, RelativeSource={RelativeSource AncestorType=Window}}` に替える（`Stop` も同じ） |
| `MainFirstRunButton` の `Click` | いまは `MainWindow.xaml.cs` の `OnFirstRunClick`。`SettingsView.xaml.cs` に `public event EventHandler? FirstRunRequested;` を足し、`MainWindow.xaml.cs` が構築時に購読して既存の `OnFirstRunClick` を呼ぶ（**ウィザードは窓が要る仕事なので窓側に残す**） |
| `MainHeaderText` | 要素と id は残す。開発ビルドのときだけ本文を入れる（`AppVersion` が開発版のとき） |

**振る舞いは変わらない**＝`MainWindow.xaml.cs:134` が `AutoStartServer`（既定 `true`＝`Contracts/LauncherSettings.cs:247`）で
既に自動起動している。**釦を消すだけである。**
**`autoStartServer:false` を明示している機体**からは、帯が「止まっています」＋〔設定を開く〕で詳細へ辿れる導線を必ず出す。

#### A-2 状態の帯（新設・タブの上・常時 1 行）

新設 id＝`MainBandStateText`・`MainBandReasonText`・`MainBandActionButton`・`MainOpenLogButton`・`MainGearButton`。
`StatusViewModel` に**純関数と読み取り専用の欄**を足す（既存の欄は 1 つも消さない）＝

| 追加 | 形 | 註 |
|---|---|---|
| `BandLabel(ServerState state, bool runtimeLoaded)` | `static string` | **3 語**（準備しています…／使えます／止まりました）。**`StateLabel` は段 A では触らない**（6 語のまま `MainStateText` に残る＝台本の錨） |
| `BandSeverity` | `enum`（Neutral／Ok／Bad） | 丸の色。色そのものは XAML の `Style` |
| `BandReason` | `string?` | 失敗の平語 1 行（`v2-spec.md` §2-1 の表）。**元の理由文字列は捨てない**＝ログへは従来の文を落とす |
| `BandAction` | `(string Label, ICommand Command)?` | 1 手。既存の `StartCommand`／`RebuildRuntimeCommand`／ウィザード起動を**流用**する |
| `ActivityLine` | `static string` | 「詳しい状態」の小さい 1 行（新しい HTTP の口は要らない） |

`MainOpenLogButton`＝`explorer.exe /select,<当日のログ>`（`Views/MainWindow.xaml.cs` 側＝窓が要る仕事）。
**檔が無い回は釦を出さない**（`IsEnabled=false` ではなく `Visibility`）。

#### A-3 タブ 3 枚＋歯車（`TabStatus`／`TabAbout` は歯車の中へ移す）

```
MainTabs（id 据え置き）
  ├ TabItem AutomationId="TabTry"      Header「発話テスト」   ← 先頭に置く（既定タブ）
  ├ TabItem AutomationId="TabVoices"   Header「話者」→ 段 C で「声」
  └ TabItem AutomationId="TabSettings" Header「設定」
MainDetailOverlay（新 id・既定は非表示・MainGearButton で開閉）
  └ MainDetailTabs（新 id）
       ├ TabItem AutomationId="TabStatus" Header「状態」→ 段 C で「詳しい状態」
       └ TabItem AutomationId="TabAbout"  Header「このアプリについて」
     ＋ MainDetailCloseButton（新 id）
```

- **別窓にはしない。**同じ窓の中の層にする＝UIA の root が主窓のままなので、台本は
  「歯車を押す 1 手」を足すだけで済む（別窓にすると `Get-Window` の作り直しが要る）。
- **既定で選ばれるタブは `TabTry`**＝XAML の並びで先頭に置くだけ（`SelectedIndex` を書かない）。
  `MainWindow.xaml.cs` に添字で選ぶ箇所が無いことを確かめる。
- `StatusView`／`AboutView` の `DataContext` の束縛（`{Binding Status}`／`{Binding About}`）は**そのまま**。

#### A-4 「詳しい状態」の畳み（`StatusAdvancedExpander`・既定 閉・**12 行**）

畳みの**外**＝⒜ 大きな 1 行（`StatusStateText`）＋活動の 1 行 ⒝ 準備中の進捗（`StatusRebuildProgressText`／
`StatusRebuildProgressBar`／`StatusRebuildCancelButton`） ⒞ 理由 1 行（`StatusReasonText`）＋
`StatusAcquisitionText`／`StatusAcquireButton`／`StatusRebuildRuntimeText`／`StatusRebuildRuntimeButton`。

畳みの**中は 11 行**（上限 12 行・憲章 原則 7）。**要素は 1 つも消さない**＝入り切らない物は
**行を畳んで同居させる**（消してよいのは「行」であって `AutomationId` ではない）。

| # | 行 | 収める id |
|---|---|---|
| 1 | 使っているグラフィックス | `StatusGpuText`（UUID の下 6 桁は文から落とす＝`UiText.UuidTail` の呼び側だけ替える） |
| 2 | 動かし方 | `StatusVariantText` |
| 3 | 実際に載った device | `StatusDeviceText` |
| 4 | 準備運転 | `StatusWarmupText` |
| 5 | 声の下ごしらえ | `StatusPrecomputeText` |
| 6 | メモリ | `StatusMemoryPanel`／`StatusMemoryText`（既定 OFF のまま。**声の合計の GB を出すのはこの行だけ**） |
| 7 | 下ごしらえの置き場 | `StatusLatentCacheText` |
| 8 | 声ごとのメモリ | `StatusVoiceMemoryText` |
| 9 | 食い違いの告知 | `StatusUpstreamMismatchText`＋`StatusGpuMismatchText`（**同じ行に 2 つ**） |
| 10 | 起動の告知 | `StatusNoticesText`（裁定 88 の「畳まない・言い換えない」は**この行の中で**守る） |
| 11 | ログ | `StatusLogBox`（本文は残す＝台本が読む。高さは 6 行に固定） |

**「つなぎ口」の行は置かない**＝`StatusEndpointText` の要素と id は残すが、**番号を常時は出さない**。
**18088 を出す面は 1 つだけ**＝埋まって起動できないときの帯（憲章 原則 5 の唯一の例外）＝
`v2-spec.md` §2-2・`v2-copy.md` §4 と**同じ 1 文**で 3 檔とも書く。
**`MainEndpointText` は本文を空にする**＝台本の **`:1662-1665`**（`endpoint shows the private port`）は空にすると必ず落ちるので、
**段 A に入る前に**「`MainEndpointText` が空であること」＋「`http://127.0.0.1:$Port/health` が 200 を返すこと」に割り直す（`v2-spec.md` §7 B-3）。

**「動かない」に直結する告知だけは畳みの外**（⒞ の下）に出す＝`StatusSettingsPendingText` はここ。

#### A-5 設定・発話テスト・声の畳み

| 画面 | 新設 id | ふだん出す物 | 畳みへ入れる物 |
|---|---|---|---|
| `SettingsView.xaml` | `SettingsAdvancedExpander`・`SettingsOpenLogButton` | `SettingsAutoStartCheck`・`SettingsWarmupCheck`・`SettingsVoicesDirText`＋フォルダを開く・ログを開く | **`v2-copy.md` §4／`v2-spec.md` §2-5 の 10 行**（＋ A-1 で移した 3 釦は 5 行目の「読み上げの動作」）。**入らない物は移さずに消す**（憲章 原則 7 は「詳細」1 枚に掛かる＝設定の詳細も同じ錠で数える）＝GPU の UUID・`SettingsShowMemoryCheck`・`SettingsWarmupStagesBox`／`SettingsWarmupVoicesBox`・`SettingsEmptyCacheBox`／`SettingsEmptyCacheNoteText`・`SettingsReadyTimeoutBox`／`SettingsReadyTimeoutNoteText`・つなぎ口の行。**`settings.json` の鍵は 1 つも減らさない** |
| `TryView.xaml` | `TryAdvancedExpander` | `TryVoiceCombo`・`TryCaptionBox`・`TrySpeedBox`・`TryStepsPreset10`／`TryStepsPreset40` | `TryStepsBox`・`TryCfgTextBox`・`TryCfgCaptionBox`・`TryCfgSpeakerBox`・`TrySeedBox`・RTF と ms の内訳 |
| `VoicesView.xaml` | `VoicesAdvancedExpander` | 一覧（列を 5→3）・追加の 3 欄・試聴／停止／削除／更新 | `VoicesPrecomputeButton`・`VoicesRestorePresetsButton` |

**`VoicesGrid` の列を減らすときの注意**＝台本は `-CellText` で**値**を引く（`:1744`・`:1754`・`:2156`）ので
列の**枚数**には縛られないが、`$T.Preset`（種別の値「プリセット」）を含む列を消してはならない。

**段 A の試験**

| 種類 | 檔 | 内容 |
|---|---|---|
| 追加 | `launcher/IrodoriTtsYwk.Launcher.Tests/AutomationIdsTests.cs`（新設） | `Views/*.xaml` を読み、**既存 139 個の id が全部生きていること**と、新設 id が重複していないことを見る。XAML は `.csproj` の `<None Include="..\IrodoriTtsYwk.Launcher\Views\*.xaml" CopyToOutputDirectory="PreserveNewest" LinkBase="Views" />` で試験の出力へ写す（`AppContext.BaseDirectory` から読む＝リポの路を探し歩かない） |
| 追加 | `ViewModelsTests.cs` の `StatusViewModelTests`（:768〜） | `BandLabel` の 6 状態→3 語・`BandSeverity`・`BandAction` の有無（**`StateLabel` の 6 本（:776-781）はこの段では触らない**） |
| 素通り | 契約テスト 369 | HTTP の口も JSON の欄も触らないので 1 本も動かない |

**危険**＝⑴ 束縛の付け替え漏れ（釦が押せるのに何も起きない）＝`SettingsViewModel` に `Status` は無い。
⑵ 畳みの中は UIA から見えない＝A-0 を先に入れる。⑶ `MainDetailOverlay` を `Visibility.Collapsed` ではなく
`Hidden` にすると UIA には見え続ける＝**`Collapsed` を使い、台本は歯車を押す**。
**規模**＝XAML 5 檔・`.xaml.cs` 3 檔・`StatusViewModel` に約 80 行・試験 2 檔。**中**（画面の作り替えの本体）。
**検分の目**＝「**ふだんの起動で押す釦は〔しゃべらせる〕1 つだけか**」（憲章 原則 2 の検分文）と
「**詳細は 12 行以内か**」（原則 7 の検分文）。この 2 つだけを当てる。

#### 段 A の記帳（2026-09-11・実装 1 席＋検分 3 席＋是正・Opus 5）

**入れた物**＝A-0（台本の助手 5 本＝`Open-YwkFold`／`Open-YwkGear`／`Close-YwkGear`／`Select-YwkTab`／
`Open-YwkRunControls`。18 箇所の `Select-Tab` を `Select-YwkTab` へ通し、釦を出す 1 手を 7 箇所に置き、
`$T.Rebuild`／`$T.ClearCache` の名前引きを id 引きへ替えた）・A-1（上の起動／停止の帯を廃し、3 釦を
**要素ごと** 設定 › 詳細へ。`MainHeaderText` は要素と id を残し本文は開発ビルドだけ）・
A-2（純関数 `ViewModels/BandText.cs` の `BandText.For`＝3 語と失敗の 3 部品・`HostLine`・
〔ログを開く〕）・A-3（タブ 3 枚＋歯車の層 `MainDetailOverlay`。`TabStatus`／`TabAbout` は要素ごと中へ）。

**A-4／A-5 は入れていない**（`SettingsAdvancedExpander` だけは A-1 の移設先として作った）＝
`StatusAdvancedExpander`／`TryAdvancedExpander`／`VoicesAdvancedExpander` と 12 行／10 行への畳み込みは
**次の便**。理由＝A-5 の「入らない物は移さずに消す」が §0 の錠・`v2-spec.md` §2-0 の「削除 0」・
緑の定義（139 id 生存）と正面から衝突するので**決めが要る**。台本の `Open-YwkFold` は 5 つとも受けるので、
後から入れても台本は直さなくてよい。
**A-4 のうち `MainEndpointText` の本文を空にする 1 件だけは入れた**＝台本 `:1789` を先に
「the endpoint is not printed on the main screen」へ割り直した（私設の口が本当に答えるかは、
下の `the port answers /health 200 within 10 s of the press` が引き続き見る）。

**id**＝139 は 1 つも改名・削除していない（移設のみ）。新設 10＝`MainBandStateText`・`MainBandReasonText`・
`MainBandActionButton`・`MainBandHostText`・`MainOpenLogButton`・`MainGearButton`・`MainDetailOverlay`・
`MainDetailTabs`・`MainDetailCloseButton`・`SettingsAdvancedExpander`（XAML 実測 139 → 149・重複 0）。
錠は `AutomationIdsTests.cs`（生存・重複・新設・**写しの 7 檔を名前で釘付け**＋`.csproj` の
`CleanCopiedViews` が毎度の建てで写しの棚を掃除する＝古い写しで素通りさせない）。

**検分の所見 13 件**（重複 2 組を畳んで 11 件）＝**10 件当て込み・1 件は段 C へ申し送り**。
当て込んだ物＝⑴ 帯が `RuntimeVariants.ShortDisplayName` を呼ぶのをやめ、
帯専用の 4 語（CUDA 13.0／CUDA 12.6／ROCm／CPU・知らない綴りは「いまの動かし方」）にした
＝`gfx1151`・「CUDA 版」が主画面に出る道を塞いだ ⑵ D2 の ⑵ から `runtime.error` の生の 1 行を落とし、
`v2-copy.md` §3-2 E-07 の固定文にした（元の 1 行はログと `StatusReasonText` に残る）
⑶ D6 の ⑵ に終了コードの数を戻した（`BandContext.ExitCode`・判らない回は括弧ごと落とす）
⑷ D7 は理由の行を出さない（見出しと同じ文を 2 度並べない）⑸ 門の断り（B1／B3）でも勧める先を渡す
（`MainViewModel.RecommendedAlternative`＝勧める先が自分自身なら null）⑹ Failed から停めた回も
「止まっています」＋〔もう一度動かす〕（`wasRunning = IsRunning || IsFailed`）
⑺ 〔ログを開く〕を `Status.LogText` の変化で見直す＝**起こす前に断った回**（サーバの記録行が 1 行も来ない）でも釦が出る
⑻ 帯に報告の受け皿の薄字 1 行（裁定 131・釦と一緒に畳む）⑼ `MainEndpointText` の本文を空に
⑽ `AutomationId` 生存の錠が bin に残った古い写しで素通りする穴を塞いだ（上記）。
**据え置き 1 件**＝移した 3 釦の札（サーバ停止／サーバ起動／初回取得をやり直す）は段 A の錠
（「文字列は 1 つも替えない」）に従い触らず、**C-3 の表へ申し送った**。

**実測**＝`dotnet test launcher -c Release --nologo` が **781 合格＋1 スキップ**（工事前 743＋1・
追加 38・削除 0）／契約テスト **369 passed**（HTTP の口も JSON の欄も触っていない）／
`dotnet build launcher -c Release --no-incremental` が **0 警告 0 エラー**／
`probe/d-launch-probe.ps1` の構文解析 **0 エラー**・`-DryRun` が「nothing was touched.」で終わる。
**アプリは 1 度も起こしていない**（実射は段 H）。

---

### 段 B — はじめの準備（自動決定・見える 3 段・1 本のバー・同意は 1 度）

**`enum FirstRunStep` の 7 値は 1 つも触らない**（`FirstRunViewModel.cs:20-42`）。替えるのは
**見せ方（表示用の対応表）と進捗の合成**だけである。

#### B-1 動かし方をアプリが決める（決裁 130 Q1）

- 判定は **`VariantRecommendation.Recommend`（`Services/Gpu/VariantRecommendation.cs:71`）そのまま**。
  **新しい判定は 1 行も書かない。**下限未満の錠（`IsBelowMinimum`:50・`BlockReason`:119・`StartRefusalReason`:150）も据え置き。
- `FirstRunViewModel` に `public string DecisionLine` を足す＝
  `GpuEnumerator` の製品名 ＋ `RuntimeVariants.ShortDisplayName(recommended)` から 1 行を組む
  （**文面は `v2-copy.md` が正**。この計画は差し込み口だけを決める）。
- `FirstRunVariantCombo`・`FirstRunVariantNameText`・`FirstRunVariantNoteText`・`FirstRunDriverText`・
  `FirstRunVariantBlockText`・見積りの内訳は、新設の `FirstRunAdvancedExpander`（既定 閉）へ**そのまま**入れる。
  **下限未満は選べない**（裁定 126 B）の錠は畳みの中で従来どおり働く。
- 設定側の変更口は `SettingsVariantCombo`（既存）＝**詳細の中にもう在る**（段 A-5 で畳みへ入った）。

#### B-2 見える段を 3 つに畳む

| 見せる段 | 中の `FirstRunStep` | 題（案・正は `v2-copy.md`） |
|---|---|---|
| 1 / 3 | `Notices` | はじめに |
| 2 / 3 | `Variant` | 準備の内容を確かめる |
| 3 / 3 | `Download`／`Install`／`Models`／`Start` | 準備しています |
| （番号なし） | `Done` | **使えます。**（釦は〔**しゃべらせてみる**〕＝憲章 §4-10） |

`FirstRunViewModel.StepNumberText`（:255）を**この対応表**で書き直す。`Title`（4 値）は同じ文字列を返す。
**内部の 7 段は据え置き**＝働く段は現行でも自動で次へ進む（裁定 94 ⑴）。

#### B-3 1 本の進捗バー（純関数を 1 本足す）

`ViewModels/FirstRunProgress.cs`（新設・純関数のみ）＝

```csharp
public static double Overall(FirstRunStep step, double fraction);  // 重み Download .55 / Models .30 / Install .10 / Start .05
```

`FirstRunViewModel.ProgressFraction`（:394）はこの関数を通した値を出す（段ごとに 0 へ戻らない）。
`ProgressText`（:387）は「42 %　あと 6 分ほど」の 1 行に畳み、
**速さ・檔数・バイト数・ETA の内訳と `FirstRunTrailList` は畳みの中**へ。等幅フォントを外す。

#### B-4 通知は「要約＋〔全文を見る〕」・**同意は初回に 1 度だけ**（決裁 130 Q3）

- **`FirstRunAcceptCheck` は残す。**`CanAcceptNotices`（:283）と `acceptedNoticesSha256` の記録（:456）と
  先へ進めない錠（:606）は**1 行も触らない**。
- 新設 `FirstRunNoticesFullButton`＝押すと畳みが開いて `FirstRunNoticesBox`（**要素ごと据え置き**）の全文が出る。
- **同じ版の通知は二度と出さない**＝`FirstRunViewModel` に `NoticesAlreadyAccepted`
  （`settings.AcceptedNoticesSha256` と読み込んだ通知文の sha256 が一致）を足し、真なら `Notices` の段を飛ばす。
  そのとき見える段は 2 つになるので、`StepNumberText` は `VisibleStepCount`（3 か 2）で綴る。
- **押下の数**＝初回 4（チェック → 次へ → 準備を始める → **しゃべらせてみる**）／2 回目以降の「やり直す」は 3。
  受け入れ条件 D-5（≤ 6）は満たす。`docs/acceptance.md:30` の記録値（5 押下）は工事後に**別席が**更新する。

**段 B の試験**

| 種類 | 檔 | 内容 |
|---|---|---|
| 追加 | `FirstRunProgressTests.cs`（新設・**5 本**） | 段の境目で連続すること・0 と 1・逆行しないこと・未知の段は直前の値・`fraction` が範囲外でも 0〜1 に収まること |
| 調整 | `ViewModelsTests.cs` の `FirstRunViewModelTests`（:1261〜） | `StepNumberText` の期待値（`3 / 7` → `3 / 3`）・`NextButtonText`・`NoticesAlreadyAccepted` で段が 1 つ減ること |
| 据え置き | `Decision126Tests.cs`／`Decision126BelowMinimumTests.cs`／`Decision126CorrectionTests.cs` | **判定を触らないので期待値は動かない。**畳みの中で錠が働くことを 1 本足す |
| 台本 | `probe/d-launch-probe.ps1`（**段 B より先に直す**） | ⑴ `:1097`・`:1114`＝`'3 / 7'` と `$T.FetchRun`（取得（実行系））を新しい題と番号へ。`:1059` の `$T.Finished`・`:1079` の `$T.Ledger` も見直す ⑵ **`:1021`・`:1028`・`:1031`**＝`FirstRunVariantCombo` を `Get-ComboItemNames`／`Select-ComboItemById` で触る 3 行。B-1 でこの combo は `FirstRunAdvancedExpander`（既定 閉）の中へ入るので、**`Open-YwkFold -Id 'FirstRunAdvancedExpander'` を先に撃つ**か、`-Variant cu130`／`rocm` の道を「**設定 › 詳細で動かし方を変えて起こし直す**」へ振り替える（これを外すと RTX 機の無人走行の段取りが成立しない） ⑶ **`:1069`**＝`the whole first run costs five presses`（`-eq 5`）を **`-eq 4`** へ（B-4 の押下 4） ⑷ **`:1099`**＝`the presses up to the failed step are four`（`-eq 4`）を **`-eq 3`** へ（選ぶ段が消えるため） |

**危険**＝⑴ 段番号の対応表と台本の期待値がずれると無人走行が**取得の最中に**落ちる（30 分の走行が無駄になる）＝
**台本を先に直す**。⑵ `Notices` を飛ばす経路で `acceptedNoticesSha256` を**上書きしない**こと（読むだけ）。
⑶ 重み 55/30/10/5 は**見せ方**であって実測ではない＝憲章に数を書かない（脚註と帳面へ）。
**規模**＝`FirstRunViewModel` に約 60 行・新設 1 檔（30 行）・XAML 1 檔の組み替え・試験 6 本・台本 5 箇所。**中**。
**検分の目**＝「**初回の道の上に、利用者が答えを持っていない問いが 1 つでも出ていないか**」（原則 4 の検分文）と
「**釦を押す前の 1 画面に GB・分・理由の 3 つが全部載っているか**」（原則 3 の検分文）。

---

### 段 C — 文言（`UiStrings` を正本にする・語の検分試験・台本の語表）

#### C-1 `ViewModels/UiStrings.cs`（新設）＝**利用者に見える文字列の唯一の出所**

- `UiText.cs`（数の書式・**1 字も触らない**＝`Bytes` の `GiB` は是正済みの決め事）とは**別の檔**にする。
  `UiStrings` は**文**、`UiText` は**数**。
- 形＝`public static class UiStrings { public const string BandReady = "使えます"; … }` の平らな定数群と、
  分岐がある物だけ純関数（`BandLabel`・`FailureLine(code)`）。
- **XAML の `Text=`／`Content=`／`Header=`／`ToolTip=` に直書きされている日本語を、この段で `{x:Static}` 参照へ寄せる。**
  実測＝`Views/*.xaml` の直書きのうち内輪の語を含むのは **41 箇所**（変種 5・実行系 6・台帳 2・潜在 7・暖機 4・
  サーバ 4・取得 8・接続先 1・話者 4）。
- **寄せない物**＝ログへ落とす文・例外の文・JSON の鍵・契約の欄名（§3 の表）。

#### C-2 語の検分試験（`WordLintTests.cs`・新設）

```
① UiStrings の public な文字列（リフレクション）
② 試験の出力へ写した Views/*.xaml の Text= / Content= / Header= / ToolTip= の値
   ↑ この 2 つに、隠す語（憲章 §6-1）が 1 つでも現れたら FAIL
```

- **隠す語の表**＝変種・実行系・取得台帳・台帳・潜在・焼き印／焼く・暖機・サーバ・ポート・接続先・
  裁定・`decisions.md`・wrapper・上流・門・便・配布版／配布物・逐語・試し撃ち・`sha256`・`GiB`・`RTF`。
- **例外（試験の中で明示的に許す）**＝⑴ `TryAdvancedExpander`／`StatusAdvancedExpander` の中の `RTF`・`GiB`
  ⑵ つなぎ口が埋まったときの帯の `18088`（憲章 原則 5 の唯一の例外） ⑶ 残す語（`CUDA`・`ROCm`・`cu126`・`cu130`・
  `GPU`・`VRAM`・`NVIDIA`・`AMD`・`Radeon`・`GeForce`・`RTX`・`bf16`・`FP32`・`Windows`・`wav`・`Python`）は**弾かない**。
- **試験の範囲を狭く保つ**のが肝＝`Services/` と `Contracts/` の文字列（ログ・JSON の鍵・`"sha256"` の鍵名＝
  `Contracts/Ledger.cs:139`）を舐めると**内部の識別子まで巻き込む**。**舐めるのは ① と ② だけ。**

#### C-3 状態の語（6 → 3）と失敗理由の平語化

| 触る所 | いま | これから |
|---|---|---|
| `StatusViewModel.StateLabel`（:508-517） | 停止／起動中／読込中／待機／暖機中／失敗 | 準備しています…／使えます／止まりました（**内部の `ServerState` 6 値は据え置き**） |
| `StatusViewModel.EndpointText`（:420・:584） | `http://127.0.0.1:18088` | 帯からは消し、詳細の中の 1 行に限る |
| `VariantGate.cs:149`・`:165`・`:382` | 「cpu の変種を選んでください」「設定で別の変種を選んでください」 | 「設定の詳細で動かし方を変えてください」（**判定は触らない・文だけ**） |
| `VariantRecommendation.cs:163`・`:165` | 「取得からやり直して別の変種を選んでください」 | 「はじめの準備をやり直して…」 |
| `ServerStateMachine.cs:92-93` | 「ポート … は既に使われています」 | **この 1 件だけ番号を残す**（原則 5 の例外）。文は平語へ |
| `WarmupCoordinator.cs`（暖機 13・潜在 11・焼 7） | 画面へ出る 4 本だけ平語へ（準備運転／声の下ごしらえ） | **ログへ出る文は従来のまま**＝画面用と記録用を分ける |
| `GpuEnumerator.cs:376`・`TorchProbeRunner.cs:106` | 「実行系もまだ取得できていません」 | 「動かすための一式がまだ入っていません」 |
| `CacheCleaner.cs:229`・`:235`・`RuntimeStamp.cs:122` | 「取得台帳」「配布物」 | 画面に出る側だけ平語へ（**檔とログの文はそのまま**） |
| `SettingsView.xaml:213`／`:217`／`:221`（**段 A で移した 3 釦**・申し送り） | サーバ停止／サーバ起動／初回取得をやり直す | **いったん止める**／**もう一度動かす**／**はじめの準備をやり直す**（`v2-copy.md` §1-1 の 39／42／45 行目）。**急ぎの理由**＝段 A の帯が既に「もう一度動かす」「はじめの準備をやり直す」を綴っているので、段 C まで**同じ操作に 2 つの名が同時に画面へ出る**。台本は id 引き（`Invoke-ButtonById`）なので札を替えても落ちない |
| `Contracts/RuntimeVariants.cs:105-113`（`ShortDisplayName`・申し送り） | `rocm-gfx1151`＝「Radeon gfx1151」／`cuda`＝「CUDA 版」 | `v2-copy.md` §1-8 の値へ。**帯は段 A でこの関数を離れている**（`BandText.VariantName` が CUDA 13.0／CUDA 12.6／ROCm／CPU の 4 語を持つ）＝残る呼び手は `VariantRecommendation` の 3 箇所 |

#### C-4 台本の語表（`probe/d-launch-probe.ps1:124-176` の `$T`）

**C-3 より先に**直す。直す鍵＝

| 鍵 | いま | これから | 使っている行 |
|---|---|---|---|
| `Ready` | 待機 | 使えます | `:426`・`:1931` |
| `Warming` | 暖機中 | 使えます（同語に畳む＝`($T.Ready + '|' + $T.Warming)` は成立する） | `:426`・`:1931` |
| `Stopped` | 停止 | 準備しています／止まっています（**2 つに割る**＝`:1666` は起動直後を見ているので「準備しています」側） | `:1666` |
| `Failed` | 失敗 | 止まりました | `:506`・`:1361`・`:1935` |
| `Starting`／`Loading` | 起動中／読込中 | 準備しています（3 語に畳んだので**同語**） | — |
| `FetchRun`／`FetchModel` | 取得（実行系）／取得（モデル） | 段 B の新しい題 | `:1097`・`:1114` |
| `Rebuild`／`ClearCache` | 実行系を組み直す／取得キャッシュを消す | **鍵ごと廃す**（A-0 で id 引きへ移した） | `:1166`・`:1177`・`:1441` |
| `ToTry` | 発話テストへ | **しゃべらせてみる**（憲章 §4-10 の完了釦。**タブの札「発話テスト」は据え置き**＝決裁 130 Q4／裁定 116 が名指しで決めたのは**タブの札**であって完了釦ではない） | — |

**危険**＝`:1666`「state starts at stopped」は**起動釦があった時代の想定**である。自動起動の機体では
起動直後から「準備しています」になる＝**この 1 本は期待値ごと書き直す**（`autoStartServer:false` で起こす回だけ
「止まっています」を見る、に分ける）。
**規模**＝新設 2 檔（`UiStrings.cs` 約 150 行・`WordLintTests.cs` 約 120 行）・既存 12 檔の文字列差し替え・台本 1 檔。**中〜大**（数は多いが 1 つ 1 つは浅い）。
**検分の目**＝原則 1 の検分文（2 段）を**語ごとに**当てる＝⑴ 検索して他所の解説が出るか ⑵ その場の判断に直結するか。
`WordLintTests` が落ちない限り、この検分は自動で回る。

---

### 段 D — 本体が使っている間は譲る（決裁 130 Q4・契約に欄 1 つ）

**この段だけが契約に触る。追加は互換なので `schema` は上げない**（`docs/contract.md` ⑻）。

#### D-1 wrapper（`server/ywk_server.py`）

- 数は**既に在る**＝`_pending_real_requests`（:1876 前後）と `pending_real_requests()`（:1891 前後）。
  合成の handler は `_real_request_in_flight` で包まれている（:1143・:1895-1925）＝**新しい計数は書かない。**
- `ywk_status()`（:1720-1721）の戻りに **1 欄**足す＝

```json
"requests": {"in_flight": 0}
```

  **平らな `in_flight` にしない**理由＝top-level の名は本体の読み手が「何の in flight か」を取り違える。
  `warmup`・`precompute` と同じく**名詞の下に置く**のが既存の形に揃う。
- **絶対パスも秘密も載らない**（整数 1 つ）＝`ywk_scrub_errors` の規則に触れない。

#### D-2 契約の檔（`docs/contract.md` ⑹）

- ⑹ の「上の見本に**足した欄が 6 つある**」の箇条（`docs/contract.md:529`）を **7 つ**にし、`requests.in_flight` の行を足す＝
  「**`requests.in_flight`（`int`）＝いま走っている本物の `POST /v1/audio/speech` の数。暖機・事前計算は数えない
  （⑺ 7-2 の優先度判定と同じ計数）。欄が無い個体（v1.1.0 以前）は 0 と読む。**」
- ⑻ に「**この追加は `schema` を上げない**」の 1 行を足す（既存の規則の再掲）。

#### D-3 契約テスト（`tests/contract/test_status.py`・**+3 本**＝369 → 372）

| 本 | 内容 |
|---|---|
| 1 | 何も走っていないとき `payload["requests"]["in_flight"] == 0` |
| 2 | `_real_request_in_flight` を 1 つ握った状態で 1 になる（`test_warmup.py` が同じ計数を握る作法を持っているので、その作法を借りる） |
| 3 | 握りを離すと 0 に戻る（`release` は冪等＝2 度呼んでも負にならない） |

#### D-4 ランチャ側

| 触る所 | 内容 |
|---|---|
| `Contracts/WrapperResponses.cs` | `public sealed record StatusRequests { [JsonPropertyName("in_flight")] public int? InFlight { get; init; } }` を足し、`StatusResponse` に `[JsonPropertyName("requests")] public StatusRequests? Requests` を足す。**null は「欄が無い＝0」**として扱う |
| `ViewModels/StatusViewModel.ApplyStatus`（:778） | `HostBusy`（`bool`）を立てる。**2 秒ごとの既存の poll をそのまま使う**＝新しい問い合わせは足さない |
| `ViewModels/MainViewModel` | `Status.HostBusy` を `Try.HostBusy` へ渡す（既存の受け渡しと同じ道） |
| `ViewModels/TryViewModel` | `HostBusy` を足し、`SynthesizeCommand` の `CanExecute`（:64）に `&& !HostBusy` を足す。`ConcurrencyNotice`（:95）は**本体からの要求が走っている間だけ**出す 1 文に短縮し、`TryConcurrencyText`（既存 id）へ束ねる |

**危険**＝⑴ **2 秒の遅れ**＝poll の隙に押せてしまう射がある。**これは案内であって錠ではない**＝
1 プロセス 1 合成の直列（契約 ⑶ 3-4）は**変えない**ので、すり抜けても読み上げが少し待たされるだけで壊れない。
⑵ 古い個体（v1.1.0 の wrapper に新しいランチャ）で欄が無い＝**null を 0 と読む**ので釦は押せるまま＝退行しない。
⑶ 逆（新しい wrapper に古いランチャ）＝欄を読まないだけ。**両方向で互換。**
**規模**＝python 3 行・contract.md 2 箇所・契約テスト 3 本・C# 4 檔に約 40 行・xUnit 4 本
（`ViewModelsTests.cs` の `TryViewModelTests`（:952〜）に 2 本、`WrapperClientTests.cs` に 2 本＝欄の有無の両方）。**小**。
**検分の目**＝原則 6 の検分文（⑴ 何が起きたか ⑵ なぜか ⑶ 次の 1 手）が、押せない釦の脇の 1 行に揃っているか。

---

### 段 E — 更新の差分取り直し（裁定 115 の穴・憲章 §4-24 の必須要件）

**穴の正体**＝上書き導入は**アプリ樹**を新しくするが、**利用者データ樹は触らない**。ゆえに
⑴ 同梱の声の wav が新版で差し替わっても、利用者の `<data>\voices\refs\` には旧い wav が残る
（`PresetVoices.InstallIfFirstRun` は `presets_installed` の印で 0 件を返し、`InstallNew` は**増えた id しか足さない**、
`Restore` は**在る id を飛ばす**＝**中身の更新だけが誰の持ち場でもない**）。
⑵ 動かす一式は `RuntimeStamp`（`Services/Ledger/RuntimeStamp.cs`）が食い違いを見つけるが、
出せる手は「**組み直す**」＝**全部落とし直す**しかない。

#### E-1 同梱の声の差分（**中身が変わった wav だけ**を写し直す）

> **名前・路・欄名は `v2-spec.md` §11 が正本**＝純関数は **`PresetSync.Plan`**、台帳の新しい欄は **`preset_md5`**（1 行につき 1 つ）。
> この計画は差し込み口と工数だけを書く（別の名を立てない）。

- 手がかりは**既に台帳に在る**＝`voices/presets.json` の各行の `secondary.md5` と `secondary.size_bytes`。
  **ただしランチャ側にそれを読む道が無い**＝`PresetVoice` は
  `record PresetVoice(string Id, string FileName, string SourcePath, string? Caption)`
  （`Services/Voices/PresetVoices.cs`）で md5 の欄を持たず、`launcher/` の C# 全体に MD5 の実装も参照も 1 つも無い。**だから 2 行足す**＝
  - **`PresetVoice` に `string? SourceMd5`（と `long? SourceSizeBytes`）を足し、`presets.json` を読む所で `secondary.md5`／`size_bytes` を拾う。**
  - **md5 を計る小さな純関数を 1 本足す**（`System.Security.Cryptography.MD5.HashData`）。
    `AnalysisLevel=latest` の樹で「0 警告」を掲げているので、**`CA5351` を落とす註つきの `#pragma`** を添える
    （**用途は完全性の照合であって暗号ではない**）。
  - **実測は「その版の初回」だけ**＝2 度目からは台帳に記録した `preset_md5` と正本の `secondary.md5` の**文字列比較だけ**で済む。
- `Services/Voices/PresetVoices.cs` に **`PresetSync.Plan` を通す道**を足す（純関数＋檔写し）＝
  ⑴ `settings.InstalledAppVersionFor(variant)` が今の版と違う回**だけ**走る（毎起動 35 MB を読ませない）
  ⑵ 台帳の行が `preset`（`origin=preset`）で、`file` が指すデータ樹の wav の md5 が
  **正本の `secondary.md5` と違う**なら写し直す
  ⑶ 写したら `ref_latent` を落とし、`.pt` と sidecar を消す（`VoiceStore.DeleteLatentFiles`＝**焼き直しは
  wrapper の起動時の事前計算がやる**＝裁定 78 ⑴。`MigrateRenamed` と同じ後始末）
  ⑷ 台帳（`voices.ywk.json`）に **`preset_md5`** を書き足して次回の判定を軽くする。
- **利用者が触った wav を上書きしない**＝プリセットの参照 wav は `PresetVoices.CopyReference` 以外が書かない
  （利用者が声を足すと**別の id** になる）ので、`origin=preset` の行だけを相手にする限り上書きは安全。
  ただし ⑷ の記録が在る回は「**記録と一致する＝こちらが写した物**」を追加の条件にする（`v2-spec.md` §11-2 の枝 b／c）。
- 呼ぶ場所＝`Services/LauncherComposition.PrepareVoices` の ⑸（`InstallNew`）の**後**。
  順（改名 → 初回展開 → 増えた分 → **中身の更新**）を崩さない。

#### E-2 動かす一式とモデルの差分

> **正本は `v2-spec.md` §11-3／§11-4**＝純関数は **`RuntimeDiff.Plan`**、データ樹の写しは
> **`runtime\<variant>\.ledger.json`**（`RuntimeStamp.Burn` と同じ回で置く）。

- **適用した台帳を残す**＝展開に成功した時点で `<data>\runtime\<variant>\.ledger.json` に
  その回の `ledger/runtime-<変種>.json` を**丸ごと写す**（いまは sha256 だけを `settings.runtimeLedgers` に持つ）。
- `RuntimeDiff.Plan(oldLedger, newLedger)`（新設・**純関数**）＝新旧 2 つの台帳の `item` を
  `(name, sha256)` で突き合わせ、`Added`／`Changed`／`Removed` の 3 つに分ける。
- `FetchPlanner` に差分の口を足す＝**変わった物だけ**を取得の対象にする。
  **`Removed` は展開先から消さない**＝`LedgerItem`（`Contracts/Ledger.cs:125-190`）が持つのは
  `kind`・`name`・`version`・`url`・`sha256`・`size`・`filename`・`package_dir(s)` などで、
  **その item が展開先へ書いた檔の一覧（RECORD 相当）は残らない**＝site-packages から wheel 1 本ぶんの檔を安全に抜く手が無い。
  **ログに 1 行残すだけ**にし、消えた item が 1 つでも在れば（あるいは差分が台帳の 6 割を超えれば）**丸ごと組み直しへ落とす**
  （`v2-spec.md` §11-3 の枝 4 と同じ）。
- **差分の回は `WheelInstaller` を `CleanBeforeInstall = false`・`RemovePartialOnFailure = false` で構える。**
  既定はどちらも `true`（`Services/Ledger/WheelInstaller.cs:52`・`:55`＝`:87` で変種ディレクトリを丸ごと `Directory.Delete`、
  `:537-551` で失敗時に丸ごと `Delete`）＝そのまま流すと ⑴ 頭で既存の一式が消えて差分の意味が無くなり、
  ⑵ 差分の 1 檔が落ちただけで動いていた一式が丸ごと失われる（全部落とし直すより悪い）。
- **ゆえに差分が途中で落ちた機体は「新旧が混ざった樹」で残る**＝締めに `RuntimeStamp.LooksComplete` と
  `WheelInstaller.ExpectedDistInfoCount` を**必ず撃ち**、合わなければ**その場で丸ごと組み直しへ落とす**。
  **`.ledger.json` は展開が全部終わってから書く**（途中で落ちたら次回は全差分をもう 1 度）。
- ウィザードは新しい段を作らない＝`FirstRunStep.Download`／`Install` を**差分の量**で走らせ、
  題だけ「新しくなった分を用意しています」に替える（見える段は 1 つのまま）。
- **モデル（`ledger/models.json`）は取得の道を 1 行も替えない**＝`server/ywk_fetch_models.py` の
  `hf_hub_download(cache_dir=hub_cache)` は huggingface_hub の cache（`blobs/<etag>` で中身を共有する）を使うので、
  **revision が動いても中身が同じ檔はネットに出ない＝差分は既に効いている**。要るのは 3 つだけ＝
  ⑴ 更新で `HF_HOME`（利用者データ樹）を消さない ⑵ `refs/main` を新しい revision で書き直す（既にやる）
  ⑶ **落ちる量の見積り**＝`sha256` が変わった檔の `size` の和（`ModelDiff.Plan` は**見積り専用の純関数**）。
  **見積りに使える `sha256` は LFS の 10 檔だけ**（落ちるバイトのほぼ全部）で、残る 12 檔（`.gitattributes`・`README.md` 等の非 LFS）は
  `sha256` が `null`＝`size` と git blob sha1 で見るか、**毎回 `hf_hub_download` に任せる**（小さいので実害が無い）。
- **全部落とし直す道は残す**＝`StatusRebuildRuntimeButton`（詳しい状態・畳みの外）。差分が壊れた機体の逃げ道である。

#### E-3 置き場は版ごと（裁定 133）― データ樹・Mutex・移送・削除

> **正本は `v2-spec.md` §11-8。**ここは差し込み口と工数だけを書く。
> **路の綴りを増やさない**＝画面もログも `AppPaths` の値を通す（直書きの `irodori-tts-ywk` を 0 箇所にする）。

- **データ樹の既定を flavor で分ける**＝`Contracts/AppPaths.cs`。いまは `DataDirName = "irodori-tts-ywk"` の
  定数 1 つを `Resolve`（`:196-203`）が `%LOCALAPPDATA%` の下に組んでいる。ここを
  **`ReleaseFlavor` から `irodori-tts-ywk-cuda`／`irodori-tts-ywk-radeon` を組む**形にする。
  - 定数は**足す**＝`DataDirNameCuda`／`DataDirNameRadeon` と、**旧共有樹を指す `LegacyDataDirName = "irodori-tts-ywk"`**
    （移送に要るので綴りは残す）。
  - **明示指定は従来どおり優先**＝`settings.json` の `dataDir`・env `YWK_LAUNCHER_DATA_DIR`。**鍵は 1 つも減らさない。**
  - `%LOCALAPPDATA%` が取れない機体の逃げ枝（`UserProfile\.<名>`）も同じ形で分ける。
  - `AppPaths` を作る所（`AppServices`・`LauncherComposition`・台本の期待値）に **flavor を渡す 1 本の道**を通す。
- **単一起動の錠を版ごとに割る**＝`App.xaml.cs:34` の `Local\irodori-tts-ywk-launcher` **と `:37` の
  `ActivateEventName`（`Local\irodori-tts-ywk-launcher-activate`）の 2 つ**を `…-cuda`／`…-radeon` へ割る。
  `.iss` の `AppMutex`（`:151`・註 `:152-159`）も**錠と同じ 2 つ**に割る
  （`MyAppName` を分けている所と同じ分岐で綴る＝**綴りの正本は 1 箇所**）。
  **2 個目の起動が 1 つ目の窓を前に出す合図は、版の中でだけ働く**（別アプリを起こし直さない）。
  ※ **合図の口を割り忘れると、錠だけ割れて事故になる**＝合図は `EventResetMode.AutoReset` の 1 本
  （`App.xaml.cs:155` で作り `:187` で開く）なので、両版が同時に走っていると **CUDA の 2 個目の起動が
  Radeon の窓を前に出す**。錠と合図は**必ず同じ組**で割ること。
- **2 つを同時に開いたとき**＝後から起きた側が **18088 の塞がり**で止まり、帯は `v2-spec.md` §2-1a の
  **C1／D1**（＝`v2-copy.md` §3-2 の **E-05**）＝`ServerBindFailure` のいまの道をそのまま通る。
  **新しい文も新しい判定も足さない。**
- **旧い共有樹の移送**（**1 度だけ**・起動前の安い検査より**手前**・§11-8）＝
  - **純関数 `LegacyDataMigration.Plan`**＝⑴ 版の樹が無い（または空）⑵ 旧共有樹 `%LOCALAPPDATA%\irodori-tts-ywk\` が在る
    ⑶ 明示指定が無い、の 3 つが揃う回だけ「移す」を返す。
  - **同じボリューム＝改名**（`Directory.Move`・秒で終わる）／**別ボリューム、または旧樹を片方が既に持っていった機体＝写す**。
  - **写した回は旧樹を消さない**（両版が入っていた機体で、もう一方が同じ樹を要る）。
  - **失敗したら移送しなかったことにして**新しい樹で始める＝**利用者の声を失わない**。ログに 1 行だけ残し、画面には出さない。
  - 移送を通った樹は `.ledger.json` が無く `preset_md5` も無い＝**E-1 の枝 e と E-2 の「不在なら丸ごと」がそのまま効く**
    （移送のために新しい枝を足さない）。
- **削除は `.iss` の持ち場**＝問い 2 つを廃し、**版のデータ樹を丸ごと消す**（段 F-2）。
- **試験**＝`AppPathsTests`（既定が flavor で分かれる・明示指定が勝つ・旧名が解決に出ない）と
  `LegacyDataMigrationTests`（改名／写し／版の樹が在る回は何もしない／明示指定の回は何もしない）＝**約 8 本**。
- **危険**＝⑴ **移送で利用者の声を失う事故**（旧樹を消さないことで受ける）⑵ 既存機体の設定の行方＝
  `settings.json` は移送で一緒に動く。移送しなかった機体（明示指定つき）は**いままでの場所のまま**で正しい。
  ⑶ `AppPaths` を作る所を 1 つでも取り違えると、**同じ起動の中で 2 つの樹を見る**＝台本と xUnit で釘付けにする。
- **規模**＝`AppPaths` に定数 2 つと分岐（約 30 行）・移送 1 檔（約 90 行）・`App.xaml.cs` **2 行**（錠と合図）・
  `.iss` 2 行・試験 約 8 本。**中**。

**段 E の試験**

| 種類 | 檔 | 内容 |
|---|---|---|
| 追加 | `PresetSyncTests.cs`（新設・**約 10 本**） | `v2-spec.md` §11-2 の 5 枝 × 境目（md5 が空・台帳に欄が無い・消された id・改名の直後）＝md5 一致→写さない／不一致→写す／`.pt` が消える／利用者が足した id は触らない／正本が読めない回は何もしない／版が同じ回は走らない |
| 追加 | `RuntimeDiffTests.cs`（新設・**約 7 本**） | 4 枝＋6 割の歯止め＋`.ledger.json` 不在＝追加・変更・**削除（消さない）**・同一・空の台帳・`sha256` 欠落（**取らない**＝`FetchPlanner.cs:252` の規則を守る）・順序不同 |
| 追加 | `ModelDiffTests.cs`（新設・**約 3 本**） | 見積りの和・`sha256` が `null` の檔を飛ばすこと・空の台帳 |
| 調整 | `NewPresetsAndAcquisitionTests.cs`／`PresetRenameTests.cs`／`CorrectionSeatVoicesTests.cs` | `PrepareVoices` の呼び順に 1 段増えるので、既存の期待値（足した数・改名の数）に**影響が無いこと**を確かめる 2 本を足す |
| 調整 | `RuntimeInstallerTests.cs`／`LedgerServiceTests.cs` | `.ledger.json` を書くこと・読めない回は従来どおり「組み直す」へ落ちること・差分の回は `CleanBeforeInstall=false` で呼ばれること |
| 追加 | `AppPathsTests.cs`／`LegacyDataMigrationTests.cs`（**約 8 本**・E-3） | データ樹の既定が `cuda`／`radeon` で分かれる・`dataDir` と env の明示指定が勝つ・旧共有樹からの改名と写し・版の樹が在る回と明示指定の回は**何もしない**・移送に失敗した回は**旧樹を消さない** |

**危険**（この段が一番大きい）＝⑴ **利用者の声を消す事故**は取り返しがつかない＝`origin=preset` の行以外に触らない・
`file` が `refs/` の外を指す行は飛ばす、を**試験で釘付け**にする。⑵ 差分の取得が途中で切れた機体は
「新旧が混ざった一式」になる＝上の 2 つの錠（`CleanBeforeInstall=false` と締めの数え直し）で受ける。
⑶ 実射でしか判らない＝**段 H の「上書き更新」の射**が唯一の検分。
**変えない物**＝`ledger/*.json` の形・`voices/presets.json` の形（**読むだけ**）・`voices.json`（上流が読む別名表）の書き方・
`settings.json` の既存の鍵・`ywk_fetch_models.py` の取得の道。
**足すのは `voices.ywk.json` の 1 欄（`preset_md5`）と、データ樹の新しい 1 檔（`.ledger.json`）だけ。**
**ただし E-3 は別枠**＝データ樹の路と単一起動の錠の綴りが**版ごとに分かれ**、旧共有樹からの移送が 1 度だけ走る（裁定 133）。
差分（E-1／E-2）は**移送のあとの樹**を相手にするので、E-3 → E-1／E-2 の順を崩さない。
**規模**＝新設 3 檔（約 220 行）・`PresetVoices` に約 90 行**＋ md5 の純関数と `PresetVoice` の欄**（約 30 行）・
`FetchPlanner`／`WheelInstaller` の構え替えに約 60 行・試験 **約 20 本**。**大**。
**検分の目**＝「**同じ版で 2 度目に開いたとき、1 バイトも落とさないか**」と
「**利用者が足した声と設定が残るか**」。この 2 つを実射の合否条件にする。

---

### 段 F — 名札・文書・インストーラ

#### F-1 版の名札（`RTX（CUDA）`／`Radeon（ROCm）`・題の飾りは ` － `）

| 触る所 | いま | これから |
|---|---|---|
| `ViewModels/ReleaseFlavor.cs:92-96` `FlavorLabel` | `CUDA 版`／`ROCm 版` | `RTX（CUDA）`／`Radeon（ROCm）` |
| 同 `:99-104` `Decorate` | `baseName + "（" + label + "）"` | `baseName + " － " + label`（括弧の二重を避ける） |
| 同 `:110` `WizardTitle` | `Decorate("初回取得", …)` | `Decorate("はじめの準備", …)` |
| `Contracts/RuntimeVariants.cs` `ShortDisplayName(CudaLabel)`（`:98-104` の註） | `CUDA 版` | **`CUDA（版を指定しない既定）`**＝ここは「このドライバ（537.58）では 〈名〉 は動きません」の文中に差す**動かし方の短い名**であって版の名札ではない。**`RTX（CUDA）` を入れない**（同じ文で「動かし方」と「版」の名が混ざる）。**値の綴り `cuda` は据え置き** |
| `Services/Gpu/VariantGate.cs:361-363`（`Label`） | `"CUDA 版"`（`ReleaseFlavors` とは別の literal・`:366` の助詞継ぎを通って断りの 1 行に出る） | **`"CUDA（版を指定しない既定）"`**＝上と同じ理由 |
| `ViewModels/FirstRunViewModel.cs:352` | `ROCm 版です（Radeon の GPU 向け）…` | 段 2 の「決めた結果を告げる 1 行」へ差し替え（文は `v2-copy.md` §2 段 2 が正） |
| `installer/irodori-tts-ywk.iss:48`／`:53` `MyAppName` | `…（ROCm 版）`／`…（CUDA 版）` | `… － Radeon（ROCm）`／`… － RTX（CUDA）` |
| 同 `:49`／`:54` `OldAppName` | 旧々名 | **v1.1.0 の名**（`…（ROCm 版）`／`…（CUDA 版）`）＝`[InstallDelete]`（`:189`）が旧い近道を 1 本消す |

> **規則**＝**版の名札を綴るのは `ReleaseFlavors.FlavorLabel` の 1 箇所だけ**である。
> それ以外の `cuda`／`rocm` の綴りは識別子であり、`ShortDisplayName`／`VariantGate.Label` は**動かし方の名**である。

**`OldAppName` の枠は 1 つしかない**＝v1.0 世代の名（`irodori-TTS for 読み分けちゃん`）の近道は**掃除できない**。
その機体は既に 1 度更新を通っているので実害は薄いが、**判っている欠落として記帳する**。
**`AppId`・`DefaultDirName`・資産の檔名は据え置き**（§0）。

#### F-2 文書と配布物

| 触る所 | 内容 |
|---|---|
| `installer/irodori-tts-ywk.iss:221` | `docs\install.md` の行を**消し**、`docs\guide.md` を足す（`docs\radeon.md` と `README.md` の行は据え置き） |
| 同 `:151`（`AppMutex`）と註 `:152-159` | **版ごとに割る**＝`Local\irodori-tts-ywk-launcher-cuda`／`…-radeon`（`App.xaml.cs:34` と同じ 2 語・段 E-3・裁定 133）。**註の 2 文が偽になる**＝`:155`「この錠は **両方の種で同じ**…裁定 109 でも変えない」と `:159`「同じ錠・同じ 127.0.0.1:18088」＝**書き直す**（錠は版ごと・つなぎ口だけが 1 つ） |
| 同 `:41-42`（内部識別子の錠の註） | `AppMutex` と **データ樹の名（`irodori-tts-ywk`）** を「1 つも変えない」側から**外す**（裁定 133 で両方とも版ごとに割れた）。Flavor の id・`/DFlavor=`・`AppId` の GUID・setup の檔名・台帳名は据え置き |
| 同 `:291-296`（`DataDir()`・路の文字列は `:295`） | **版ごとの樹を返す**＝`…\irodori-tts-ywk-cuda`／`…-radeon`（`AppPaths` の新しい既定と**同じ場所**を固定で解く。env は見ないまま） |
| 同 `:345`（導入前の断り） | 「取得したものは `<版の樹>` に入ります」の路が版ごとになるだけ（文はそのまま） |
| 同 `:405-470`（`CurUninstallStepChanged`） | **問いを 2 つとも廃す**（`:424-425`「取得した実行系とモデルも削除しますか？」・`:460` の 2 段目）＝**版のデータ樹を丸ごと消す。問わない**（裁定 132 → 133 で 2 つ目も廃止）。無人（`/SUPPRESSMSGBOXES`）でも同じ。**もう一方の版の樹には触れない**＝共有が無くなったので「道連れにしない」場合分け（`ben-e` §5-4）は**規則ごと廃止**。**置き場の外には 1 檔も触れない**（利用者が声を追加するときに選んだ元の wav は `voices\refs\` の写しだけを消す＝`VoiceStore` の形をそのまま守る）。**旧い共有樹 `%LOCALAPPDATA%\irodori-tts-ywk\` も、もう一方の版が入っていないときだけ一緒に消す**＝移送は**初回の v2.0 起動**でしか走らない（`v2-spec.md` §11-8・段 E-3）ので、⑴ v1.1.0 に上書き導入して**一度も開かずに**撤去した機体 ⑵ 移送に失敗して旧樹が残った機体（`v2-spec.md` §11-8 ⑶）では、**約 8 GB が丸ごと居残る**＝裁定 132 が起こされた当の状態。判定には既にある `OtherFlavorKey`（`:418` の `RegKeyExists`）をそのまま使う（もう一方が入っていれば**触らない**＝向こうがまだ移送で要る） |
| 同 `:383-385`（導入先が置き場と重なる断り） | 版ごとの樹で判定する（文はそのまま） |
| `build/installer-build.ps1:105-113` の註 | 「.iss が docs\install.md を 1 檔ずつ名指す」の 1 行を新しい檔名へ。**`$ExpectedAppFiles`／`$ExpectedAppBytes` は動かない**＝`docs\` は `build\out\app` を通らない |
| `docs/install.md` | **作る側の帳面へ戻す**（1 字も替えない・配布物から外す・利用者に指さない） |
| `docs/guide.md` | **新設**（本文は `v2-copy.md` が正） |
| `README.md` §0・§1 | 配布ページと「これは何をするアプリか」を新しい名札と平語へ。**`decisions.md` を利用者に読ませる導線を消す**（憲章 §3 根 4） |
| `site/index.html` | 見出し・釦の札（`id="dl-cuda"`／`dl-rocm` は**据え置き**）・「困ったら」の行き先（`install.md` → `guide.md`）・「必要なもの」の数（空き 15 GB → **12 GB**）・脚の 1 行を **X の @yomiwakechan（https://x.com/yomiwakechan ）**に揃える（いまの `site/index.html:202` はリポジトリを指している＝裁定 131 で **GitHub の Issues は使わない**と決まった）。同じ 1 行を〔このアプリについて〕（`AboutSaveLogButton` の隣）・`guide.md` の「困ったときは」の末尾・リリース文にも置く。**CSS と JS（資産名の末尾 `-cuda.exe`／`-radeon.exe` で拾う所）は据え置き** |
| `docs/release-notes/v2.0.0.md` | 新設（15 行以内・ひな型は `v2-spec.md` §5-2） |
| `launcher/README.md` §7-3 | `AutomationId` の表に新設 id を足す（**既存行は消さない**） |

#### F-3 試験と台本

| 種類 | 檔 | 内容 |
|---|---|---|
| 調整 | `ViewModelsTests.cs:172-173`（`FlavorLabel`）・`:180-181`（`AppTitle`）・`:188-189`（`WizardTitle`） | 期待値を新しい 2 語と ` － ` へ。**この 6 本だけが名札を釘付けしている** |
| 調整 | `probe/e-install-probe.ps1:139-146`・`:210-222` | `Get-ExpectedAppName` を「幹 ＋ ` － ` ＋ `RTX（CUDA）`／`Radeon（ROCm）`」に組み直す（`AppStem` は使い回す・`ParenOpen`／`Edition` の組み立ては廃す） |
| 調整 | `probe/e-install-probe.ps1` の `Invoke-Stage3`（段 B で必ず落ちる 2 本） | ⑴ **`:1204-1209`**＝`the step counter says 1 / 7`（`($number -replace '\s','') -eq '1/7'`）の期待値を新しい段数（`VisibleStepCount`＝`1 / 3`、同意済みなら `1 / 2`）へ。⑵ **`:1211-1223`**＝`FirstRunNoticesBox` の本文を `{app}\licensesirst-run-notices.md` のバイトと 1 字ずつ突き合わせる前に、`Invoke-ButtonById -Id 'FirstRunNoticesFullButton'`（**無ければ素通り**）を入れる＝B-4 で全文は畳みの中に入り、開くまで UIA から読めない |
| 確認 | `installer-build -All` の門 | **A-6**（exe の版が v2.0.0）・**B-2**（`guide.md` が増えて `install.md` が減る＝差は数 KB・帯 68.0〜76.0 MiB の中）・**B-3**（`SHA256SUMS.txt` の作り直し） |

**危険**＝⑴ `AppName` が変わると「アプリと機能」の表示名が変わる（`AppId` は同じなので上書き更新は通る）。
⑵ `e-install-probe` は `DisplayName` の**頭**と `.lnk` の名を突き合わせる（`:1049`・`:1061`）＝**両方**直す。
⑶ `docs\install.md` を配布物から外すと、**既に導入済みの機体の `{app}\docs\install.md` は残る**
（`[Files]` から消えても消去はされない）＝`[InstallDelete]` に `{app}\docs\install.md` の 1 行を足す。
**規模**＝C# 3 檔の定数・`.iss` 5 行・文書 5 檔・台本 1 檔。**小〜中**（危険は台本と門に集中）。
**検分の目**＝原則 8 の検分文＝「**リポを見たことがない人が最後まで読めるか**。引いている番号・檔名・測定値を
消しても意味が通るか」。`README`・`site`・`guide`・リリース文の 4 面に当てる。

---

### 段 G — 検分

| 物 | いま | 工事後の見込み | 走らせ方 |
|---|---|---|---|
| 契約テスト | 369 | **372**（段 D の +3） | `build/run-tests.ps1` |
| xUnit | 743＋skip 1 | **+43 前後**（A 2・B 6・C（語の検分）1・D 4・E 約 28＝差分 20 ＋ 置き場と移送 8・F 0＝期待値の付け替えのみ） | 同上 |
| `installer-build -All` | 20 門 0 失敗 WARN 0 | **20 門のまま**（門は増やさない）。A-1 の記録値は**動かない**・**A-6 の期待値は `v2.0.0`**・B-2 は帯の中 | `build/installer-build.ps1 -All` |
| `probe/d-launch-probe.ps1` | 無人走行 | **A-0 の 3 つの助手 ＋ 段 B の段番号 ＋ 段 C の語表**を当てた版で 1 周 | 本機（Radeon）で 1 周・RTX 機で 1 周 |
| `probe/wizard-probe.ps1` | ウィザードの窓が開いているかを答える**読み取り専用の 1 行**（`common.ps1` を読み込まず、押す関数を持たない） | **判定は持たせない**＝`:72-74` が読む 3 つ（`FirstRunStepTitle`／`FirstRunStepNumber`／`FirstRunMessageText`）の**出力の読み方だけ**を段番号の新しい綴りに合わせる | 同上 |
| **ウィザードの検分先** | — | 「見える 3 段・同意 1 度・1 本のバー」は **`d-launch-probe.ps1` の h 節（`:986-1130`）**と **`e-install-probe.ps1` の `Invoke-Stage3`（`:1190-1235`）**が受け持つ | 段 B・段 F の後 |
| `probe/e-install-probe.ps1` | 導入と削除 | `AppName` の新しい期待値・`{app}\docs\guide.md` が在り `install.md` が無いこと・**データ樹が版ごとの名になっていること**・**削除で問いが 1 つも出ず、その版の樹が丸ごと消えること**・**旧い共有樹 `%LOCALAPPDATA%\irodori-tts-ywk\` を置いてから導入→（開かずに）撤去し、それも消えていること**（もう一方の版を入れてある回は**残る**ことも同じ門で見る）（裁定 132／133・段 E-3／F-2） | 段 F の後 |
| **語の検分** | 無し | `WordLintTests` が**自動で**回る（§段 C-2） | xUnit に同居 |

**手で見る 8 つ**（憲章 §2 の検分文をそのまま順に当てる）＝
① 画面の語を検索して他所の解説が出るか／その場の判断に直結するか
② ふだんの起動で押す釦は 1 つか
③ 釦の前の 1 画面に GB・分・理由が全部あるか
④ 初回の道に「答えを持っていない問い」が無いか
⑤ 利用者が連携のために何かすると思わないか
⑥ エラーの文に 3 部品が揃っているか
⑦ 詳細は 12 行以内か
⑧ リポを知らない人が文書を最後まで読めるか。

---

### 段 H — RTX 機の実射台本（清潔導入から更新まで）

| # | 射 | 合否 |
|---|---|---|
| 1 | 旧版を消し、データ樹も消してから v2.0.0 の setup（RTX（CUDA））を実行 | 管理者権限を求めない・完了で自動的にアプリが開く・「アプリと機能」の表示名が `… － RTX（CUDA）` |
| 2 | はじめの準備を通す | 見える段が 3 つ・**選ぶ段が 1 つも出ない**・1 本のバーが逆行しない・UAC は 1 度だけ・押下 4 |
| 3 | 途中で〔あとにする〕→ 開き直す | 続きから戻る・最初からにならない |
| 4 | 〔しゃべらせる〕1 押し | 1〜2 秒で鳴る・所要は「1.7 秒」の 1 行だけ（RTF も ms も出ない） |
| 5 | 読み分けちゃん2 を起こし、こちら側で**何もしない** | エンジン一覧に出る・帯の右が「読み分けちゃん2 から使えます」 |
| 6 | 読み上げの最中に〔しゃべらせる〕を押しに行く | **押せない**・1 行の理由が出る（段 D） |
| 7 | 窓の × | 数秒で消える・子 python 0・VRAM が返る・トレイに何も残らない |
| 8 | 歯車 → 詳しい状態 → 詳細を開く | 12 行以内・生ログはこの中だけ・〔ログを開く〕で檔の場所が開く |
| 9 | v2.0.0 を**上書き**で入れ直す（同梱の声の wav を 1 本差し替えた版で） | **設定と自分で追加した声が残る**・開いたときに**その 1 本だけ**取り直す（全部を落とし直さない＝段 E） |
| 10 | 設定 › 詳細で動かし方を `CUDA 12.6` に落とす | 先に「5 GB 前後を 10 分ほど落とす」と告げる・落として動く |
| 11 | 削除 | **問いは 1 つも出ない**・自分の置き場の物（一式・取り込んだ声・設定・記録）がすべて消える・**声を追加するときに選んだ元の wav は残る**（憲章 §4-25＝裁定 132／133）・**もう一方の版を入れてあるなら、その置き場は 1 檔も減らない** |
| 12 | 旧い共有樹（`%LOCALAPPDATA%\irodori-tts-ywk\`）が在る機体で v2.0 を開く（**開発機だけ**） | **版の樹へ移る**（`…-cuda\`）・追加した声と設定が残る・移送のあとは 5.3 GB を落とし直さない・失敗した回は**旧樹が消えていない**（段 E-3・裁定 133 ⑸） |

**記録**＝所要・押下の数・VRAM の返り・差分で落ちたバイト数。**これらは帳面の側の数**で、利用者向けの文には書かない。

---

## §3 語の棚卸し（工学側に残す／利用者側で替える）

**実測（この工事の前・`launcher/IrodoriTtsYwk.Launcher` の C# の文字列リテラルのみ・註と `///` は除く）**

| 語 | 文字列リテラルの数 | **残す**（工学側＝ログ・例外・JSON の鍵・帳面） | **替える**（利用者の目に入る面） |
|---|---|---|---|
| サーバ | 43 | `Contracts/IServerProcess.cs:301`・`ProcessTree.cs`・`ServerProcess.cs`・`HealthPoller.cs:111`＝**ログと例外**。`UiText.NotRunning` の註 | `TryViewModel`（4）・`VoiceRow`（3）・`StatusViewModel`（1）・`SettingsViewModel`（2）＝**画面**。帯・釦から全廃 |
| 取得 | 75 | `LedgerReader`・`ModelFetcher`・`HttpDownloader`・`FetchPlanner`＝**ログと檔名** | `FirstRunViewModel`（22）・`MainViewModel`（11）・`Views/FirstRunWizard.xaml.cs`（3）＝「はじめの準備」「ダウンロード」へ |
| 実行系 | 32 | `RuntimeStamp`（4）・`CacheCleaner`（4）・`ModelFetcher`（1）＝**ログ** | `MainViewModel`（10）・`FirstRunViewModel`（6）・`GpuEnumerator:376`・`TorchProbeRunner:106`・`VariantGate`（2）＝「動かすための一式」へ |
| 台帳／取得台帳 | 41／18 | `LedgerReader`（11）・`WheelInstaller`（6）・`VoiceStore`（5）・`Ledger.cs` の鍵名＝**全部残す** | `FirstRunViewModel`（5）・`SettingsViewModel`（1）・`CacheCleaner:229`・`:235`＝画面に出る 8 本だけ替える |
| 潜在／焼 | 23／19 | `WarmupCoordinator`（11／7）の**ログ側**・`MemoryEstimate`（2）の註 | `VoicesViewModel`（7／3）・`VoicesView.xaml.cs`（2／2）・`StatusViewModel`（3）＝「声の下ごしらえ」へ |
| 暖機 | 20 | `WarmupCoordinator` のログ・`WarmupStagesText` の内部 | 画面の 4 本＝「準備運転」（詳細の中）へ |
| 変種 | 24 | `VariantGate`（5）・`RuntimeVariants`／`ReleaseFlavors` の註・`settings.json` の鍵 `variant` | `SettingsViewModel`（5）・`MainViewModel`（3）・`VariantRecommendation`（4）・`VariantGate:149`／`:165`／`:382`＝「動かし方」へ |
| ポート／接続先 | 6／1 | `ServerProcess:723`・`ServerStateMachine:92-93` の**ログ** | 画面には**平時は 1 行も出さない**。**帯は埋まったときの 1 件のみ番号を出す** |
| 話者 | 36 | `VoiceStore`（6）・`VoiceNameValidator`（3）・`SpeechRequestBuilder`（4）＝**内部と契約の語** | `VoicesViewModel`（12）・タブの札・列の見出し＝「声」へ |
| 配布版／配布物 | 3／7 | `RuntimeStamp:122`・`CacheCleaner:235`＝ログ | `VoiceRow:80`・`:114`・`FirstRunViewModel:452`・`HealthPoller:111`＝「このアプリ」へ |
| 上流 | 5 | `AboutViewModel:45`（開発ビルドの札）・`VoiceStore:409` の註 | `AboutViewModel:30`＝「元になった Irodori-TTS」へ |
| 裁定 | 1 | — | `VariantGate:122`「（裁定 83）」＝**画面から消す**（理由は残し出典を消す） |
| `sha256` | 14 | `Contracts/Ledger.cs:139`・`:155`・`:191`＝**JSON の鍵。1 字も触らない**。`LedgerReader`・`HttpDownloader`・`WheelInstaller`・`VoiceStore` は**ログ** | ウィザードの本文＝「壊れていないか確かめながら入れます」へ |
| `GiB`／`RTF` | 2／1 | `UiText.cs:53`・`:138`・`FetchPlanner.cs:293`＝**書式関数。触らない** | **置き場を「詳細」の中に限る**（`v2-spec.md` §1-3） |

**`Views/*.xaml` の直書き**＝内輪の語を含む可視属性は **41 箇所**（変種 5・実行系 6・台帳 2・潜在 7・暖機 4・
サーバ 4・取得 8・接続先 1・話者 4）。**XML の註に在る 33 箇所は替えない**（作る側の帳面である）。

**替えない檔（語彙が資産である）**＝`decisions.md`・`docs/contract.md`・`docs/acceptance.md`・`docs/install.md`・
`docs/design/ben-*.md`・`ledger/README.md`・`licenses/README.md`・`probe/*.ps1` の註・`build/*.ps1` の註。
**ただし「利用者に見せる札を原文のまま引いている註」は新しい札に直す**（`launcher/README.md` §7・§11・
`docs/acceptance.md` の導入行・`probe/README.md`）。

---

## §4 積み残しと、決まっていないと進めない事実

| # | 件 | いまの扱い |
|---|---|---|
| 1 | **報告の受け皿** | **決まった**＝X の `@yomiwakechan`（https://x.com/yomiwakechan ・裁定 131・2026-09-11）。**GitHub の Issues は使わない**（開発者の画面なので利用者に指さない）。公式ページの脚・〔このアプリについて〕（`AboutSaveLogButton` の隣）・`guide.md` の「困ったときは」の末尾・リリース文の **4 面に同じ 1 行**（段 F-2）。〔報告用のログを保存〕は檔の場所を出し、その隣にこの 1 行を添える |
| 2 | **`OldAppName` の枠が 1 つ** | v1.0 世代の近道は掃除できない（§2 段 F-1）。判っている欠落として記帳する |
| 3 | **`docs/acceptance.md` D-5 の記録値** | 押下 5 → 4 になる。この計画の書き手の持ち場ではないので、実装席が工事後に直す |
| 4 | **CPU の見せ方** | GPU が在る機体では**一切出さない**（憲章 §7 末尾）。`RuntimeVariants.CudaReleaseChoices` に `cpu` は残す（詳細の中の選択肢）＝**一覧の作りは触らず、出す場所だけを絞る** |
| 5 | **段 E の実射が本機（Radeon）では足りない** | 上書き更新の射は**両方の機体**で要る（RTX 機の席・§2 段 H の 9） |
| 6 | **`MainStateText` の 3 語化と台本の `:1666`** | 自動起動の機体では「止まっています」を通らない＝台本の期待値を**経路ごと**に割る（§2 段 C-4） |
| 7 | **旧い共有樹を前提にした帳面が 2 つ残る** | `docs/design/ben-e-installer.md` §5-4（共有のデータ樹を道連れにしない）と `docs/install.md` の `%LOCALAPPDATA%\irodori-tts-ywk\` の記述は、裁定 133 で**規則ごと古くなった**。どちらもこの席の持ち場ではない（`install.md` は冒頭 2 行の断りだけを足した＝作る側の帳面・本文は据え置き）。**正しい形は `v2-spec.md` §11-8 と本檔 段 E-3／F-2 が持つ**＝実装席が段 F で 2 檔を直す |
