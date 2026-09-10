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

#### 段 B の記帳（2026-09-11・実装 1 席・Opus 5）

**入れた物**＝B-1（動かし方はアプリが決める＝`VariantRecommendation.Recommend` は**1 行も触らず**、
結果を名乗る純関数 `FirstRunViewModel.DecisionLineFor(variant, gpuName)` を足した。名は
`BandText.VariantName` の 4 語を借りる＝帯と同じ綴り。製品名は `DriverProbe` に**判定に使わない**
欄 `GpuName` を 1 つ足して運ぶ。`FirstRunVariantCombo` ほか 6 つは新設 `FirstRunAdvancedExpander`
（既定 閉）へ**要素ごと**移した＝下限未満の錠（裁定 126 B）は畳みの中でそのまま働く）・
B-2（見せる段を 3 つに＝`Title` は 4 値（お知らせ／これからすること／準備しています／使えます。）、
記録用に `TrailTitle`（内輪の 7 つ）を分けた。`StepNumberText` は `VisibleStepNumber`／
`VisibleStepCount` で綴り、完了は**番号を持たない**）・B-3（新設 `ViewModels/FirstRunProgress.cs`＝
`Overall`／`Line`／`Remaining` の純関数だけ。`ProgressText` は「42 %　あと 6 分ほど」に畳み、
速さ・件数・バイト数は `ProgressDetailText`（詳細の中）へ。等幅フォントを外した）・
B-4（要約 `FirstRunNoticesSummaryText` ＋〔全文を見る〕`FirstRunNoticesFullButton` →
`FirstRunNoticesExpander` の全文。**同意チェックと錠は 1 行も触らない**。
`NoticesAlreadyAccepted` が真なら段 1 を飛ばし、見せる段は 2 つになる＝
`acceptedNoticesSha256` は**読むだけ**）。

**憲章 §4-8 との突き合わせ**＝1 巡目は「いま何をしているか」の 1 行（新 id `FirstRunPhaseText`）に
「（3.2 GB / 5.3 GB・残り 4 分ほど）」を添え、憲章 §4-8 の見本を正とした。**検分で覆した**（下の是正 ⑹）＝
`v2-copy.md` §2 が段 3 の本文を「数の無い 5 文」と逐語で決めており、`v2-spec.md` §1-3 は
「**表示用の別入口を足すとその欠陥に戻る**」と名指しで禁じている。憲章 §4-8 の 25 行は
`v2-copy.md` §2 が上書きした製品の歩き見本（段 B-2 の「正は `v2-copy`」）なので、
**`FirstRunProgress.Gb` は削り**、利用者向けの面に出る数は⑴ 割合 ⑵ 丸めた残り
⑶ 散文の総量（約 5.3 GB＝`PlanLine` の定数）の 3 つだけになった。実数（`GiB`）は
`ProgressDetailText`／`FirstRunSizeText`＝詳細の中のまま。

**失敗の 1 行**＝W1〜W6・W8・W9・W11 と E-12 を 3 部品に言い直した（⑶ は釦＝
`NextButtonText` が「もう一度」に変わる）。**内輪の 1 行は捨てていない**＝新設の `Fail(display, log)`
が元の綴りを檔へ落とす（憲章 §5）。

**id**＝**退役 0**。新設 12＝`FirstRunNoticesSummaryText`・`FirstRunNoticesFullButton`・
`FirstRunNoticesExpander`・`FirstRunDecisionText`・`FirstRunPlanText`・`FirstRunPhaseText`・
`FirstRunProgressDetailText`・`FirstRunUacNoticeText`・`FirstRunDoneText`・`FirstRunAdvancedExpander`
＋（是正で 2 つ）`FirstRunNoticesUnreadableText`・`FirstRunWhyText`
（XAML 実測 149 → 161・重複 0）。錠は `AutomationIdsTests.段Bで新設したidが揃っている`。

**台本**＝`d-launch-probe.ps1`＝`$T` の `FetchRun`／`FetchModel` を廃し `Preparing`／`PhaseParts`／
`AppFile` を立て、`Finished`（使えます。）と `ToTry`（しゃべらせてみる）を差し替え。
段 3 の見分けは**題ではなく `FirstRunPhaseText`**（4 段が同じ題になったため）。
押下は **4**（全走）／**3**（失敗まで）。変種を選ぶ手は**押下に数えない**（道の外＝
`Open-YwkFold -Id 'FirstRunAdvancedExpander'` を先に撃つ）。2b の脚は
「お知らせを飛ばした回」（決裁 130 Q3）でも落ちないように割り直した。
`wizard-probe.ps1`＝段番号を `N / 3`（飛ばした回は `N / 2`・完了は空）で読み、`doing=` に
`FirstRunPhaseText` を足した（**判定は持たせない**）。窓題の後詰めは幹の前方一致に緩めた。

**実測**＝`dotnet test launcher -c Release --nologo` が **795 合格＋1 スキップ**（段 A の 781＋1・
追加 14・削除 0）／`dotnet build launcher -c Release` が **0 警告 0 エラー**／
`probe/d-launch-probe.ps1`・`wizard-probe.ps1` の構文解析 **0 エラー**・`-DryRun` が
「nothing was touched.」で終わる。**アプリは 1 度も起こしていない**（実射は段 H）。

**次の段へ申し送り**＝⑴ W10（「いまはやめられません。」）は `v2-spec.md` が「画面に出さない」と書くが、
釦は `IsBusy` でしか押せない＝画面には出ない道なので文字列だけ平語にして残した
⑵ W2／W3／W11／W7 の ⑶ にある〔入れ直す方法を見る〕〔ログを開く〕〔ドライバの入れ方を見る〕の**釦**は
足していない（使い方の檔を開く導線は段 F。いまは〔もう一度〕がその席に居る）
⑶ `docs/acceptance.md:30` の押下 5 → **4** の書き換えは別席。

#### 段 B の是正（2026-09-11・検分 3 席＝流れ／id と台本／憲章・所見 21 件・Opus 5）

**当て込み 21／21**（据え置き 0）。**画面に出る字が変わった所**が多いので、当てた物を残す。

| # | 何が壊れていたか | 当てた物 |
|---|---|---|
| ⑴ | `DecisionLineFor` が `cpu` を**無条件に**「対応する GPU が見つかりませんでした」と読んだ。`Recommend` は**版が読めていて下限未満**（GeForce ＋ 470.00 など）でも `cpu` を返す＝画面が事実の逆を名乗り、下限の数字も出なかった | 第 3 引数に `DriverProbe` を渡し、下限未満で落ちた回は**いまの版と必要な版**を入れた 1 行に分けた（`v2-spec.md` §3 段 2 の A4）。試験＝`ドライバが古くてcpuに落ちた回は数字ごと名乗る` |
| ⑵ | 同じく、**まだ撃っていない・列挙が落ちた**回は `Recommend` が「判らないことを勝手に決めない」で並びの先頭（`cu130`）を返すのに、画面は「CUDA 13.0 で動かします」と言い切った＝5.3 GB 落として門に断られる形 | 「確かめられませんでした。ひとまず 〈名〉 で準備します。…設定の『詳細』で動かし方を変えてください。」の枝を足した。試験＝`検分できなかった回は決めたと言わない` |
| ⑶ | `v2-copy.md` §2 段 2 の**頭の 1 文**（このパソコンに合わせて、動かし方を選びました。）が既知の 4 変種の枝から抜けていた＝決裁 130 Q1 が届けたい 1 文が画面に出ない | `DecisionLead` を定数にして 4 枝の頭に置いた（GPU 無しの枝は `v2-copy.md` の綴りのまま） |
| ⑷ | `RunVcRedistAsync` が `PhaseText` を「Microsoft の部品を確かめています…」で上書きしたまま**戻さない**枝が 2 つ（台帳が無くて `null` が返る回・成功の回）。次に書くのは `OnDownloadProgress` だけで、1 件も注文しない回は 1 度も撃たれない＝**台本の新しい錨 2 本（`必要な部品`）が実射で落ちる**（清潔導入は `-SkipVcRedistLedger`＝手は差さるが `null` が返る） | 3 つの出口すべてで `PhaseText = PhaseLine(Download)` に戻す。試験＝`vc_redistの段を抜けたら取得の1行に戻る`（`PhaseText` と `Message` の対を釘付け） |
| ⑸ | W1（お知らせが読めない）が**画面に着かない**＝差し替えの文は `FirstRunNoticesBox`（既定で閉じた畳みの中）にしか無く、要約と押せないチェックだけが見えていた | `NoticesUnreadable`／`NoticesReadable` を足し、要約・〔全文を見る〕・全文の畳み・同意チェックを**まとめて隠し**、新 id `FirstRunNoticesUnreadableText` に `NoticesMissingLine` を出す。`LoadNotices` が `Message` にも置く（押す前に着く）。錠（`CanAcceptNotices`・裁定 46）は 1 行も触らない |
| ⑹ | 「いま何をしているか」の 1 行に `（3.2 GB / 5.3 GB・残り 4 分ほど）` を添えていた（憲章 §4-8 の見本を正とした）＝`v2-spec.md` §1-3 が名指しで禁じた「表示用の別入口」 | `FirstRunProgress.Gb` を**削り**、`PhaseLine(step)` は 1 文だけを返す。実数は詳細の中（`GiB`）のまま。試験＝`実測のバイト数を綴る入口をここに持たない`（公開の静的手が `Line`／`Overall`／`Remaining` の 3 本だけであることを反射で釘付け） |
| ⑺ | W3 が `FetchPlanner.FormatBytes`（`GiB`）の 3 つ組を**画面**に出していた（憲章 §6-1＝画面は `GB` で通す） | 画面は `FreeSpaceLine`（E-09 の 3 部品・**約 12 GB** の 1 つだけ）。実数の 3 つ組は `FreeSpaceShortfall` のままログと `SizeText`（詳細の中）へ |
| ⑻ | W4／W5 が取り手・展開系の**生の理由**（`HTTP 404`・`sha256 が台帳と合わない`・`変種ディレクトリに檔が…`）を画面へ継いでいた。W2 も `runtime-cu130.json が読めません` を括弧で出していた | 画面は固定の ⑵（E-10 の 1 文／`ダウンロードしたファイルが壊れているかもしれません。`／E-03 の `必要なファイルの一覧が読めませんでした。`）。生の 1 行は `Fail` の第 2 引数＝**檔へ**。※ W5 の ⑵ は検分の言（「落とした物を組み立てられませんでした。」）だと ⑴ の言い直しになるので、**なぜ**を言う 1 文に替えた（`v2-copy.md` §3-2 の「⑵ は 1 行・技術語を出してよい」に沿う） |
| ⑼ | `Record` が `Trail` と**同時に `Message`** を書いていた＝畳みの**外**（`FirstRunMessageText`）に「変種＝…」「初回取得が終わりました。」「展開しました（… GiB）。」が出ていた（憲章 §6-1 の隠す語がそのまま利用者の目に） | `Record(line, show:false)` は `Trail` と檔だけ。画面に出すのは文言表が決めた 2 行（`RuntimeSoundSkipLine`／`InstallSkipLine`）と vc_redist を飛ばした 1 行だけ `show:true`。試験＝`記録の内輪の1行は画面に出ない` |
| ⑽ | 「取得系／展開系／モデルの取得系がまだ組み込まれていません（便 D…）」の 3 行が `Message` に直に載っていた（`v2-copy.md` §1-8 の :1000／:1189／:1251＝**まだできません。**）。うち `取得台帳が読めないので展開できません。` は**W2 の 2 本目の内部の 1 行**で、W 群の取りこぼしだった | 3 行は `Fail(NotYetLine, 元の 1 行)`・展開の台帳は W2 の 3 部品へ。1 巡目の申し送り ⑴（「段 C の仕事」）は**取り消す** |
| ⑾ | W7（起動が通らない）が**あらゆる理由**を E-12（時間がかかりすぎました）と名乗った＝口が埋まっている・ドライバが下限未満・子が exit 2 の回まで嘘になり、〔もう一度〕が永久に同じ所で落ちる | `StartFailureLine`（主窓が `StatusViewModel` の帯の ⑴＋⑵ を渡す）を通す＝文を組むのは `BandText.For` の 1 箇所だけ。渡らなかった回だけ E-12。試験＝`起動が通らない回は起こす側の理由を出す` |
| ⑿ | 段 2 に ⑶ **なぜ**が無かった（憲章 原則 3 の机上の検分・`v2-spec.md`:1172）。お知らせを飛ばす回（決裁 130 Q3）は「なぜ」を 1 度も読まない | 新 id `FirstRunWhyText`＝`WhyLine`（段 1 の 3 行目と同じ 1 文）。試験＝`段2は量と時間となぜを1画面に持つ` |
| ⒀ | 下限未満を選んでいる間の `Message` が `VariantRecommendation.BlockReason`（「変種」を綴る）そのままだった | 画面は `VariantBlockedLine`（E-02 の 3 部品・数字は残す・⑶ は畳みを開く 1 手）、内輪の 1 行は檔へ。あわせて `BlockReason` の代わりが無い側の尾を「**設定の「詳細」で動かし方を変えてください。**」に（`v2-copy.md` §1-8 の `VariantGate.cs:382-387` と同文） |
| ⒁ | `VariantNote` が「ROCm 版です」＝**3 つ目の版の名**（`v2-copy.md` §0 は `RTX（CUDA）`／`Radeon（ROCm）` の 2 つだけ）、「未保障」は帳面の語 | 「Radeon（ROCm）で動かします。…（Ryzen AI MAX+ 395 ／ Radeon 8060S で確認済み。ほかの Radeon では試していません）。」 |
| ⒂ | 釦 3 つ（戻る／次へ／やめる）が**どの段でも**見えていた（`v2-spec.md` §3 段 3＝〔やめる〕だけ・完了＝〔しゃべらせてみる〕だけ） | `Visibility` を Command の可否と同じ式に束ねた（`BackVisible`／`IsBusy`）。**要素も id も残す**＝台本は従来どおり解決できる |
| ⒃ | 完了の頁が「使えます。」を 2 度綴っていた（題＋直書きの `TextBlock`） | 直書きを落とした（題＝`TitleDone` の 1 箇所） |
| ⒄ | `NextAsync` の註が押下 **5**（変種を選ぶ手を含む旧い並び）のままだった | 押下 **4**（同意チェック・次へ・準備を始める・しゃべらせてみる）へ |
| ⒅ | `$T.Ledger`（台帳）が台本のどこからも読まれない字になっていた（`v2-plan.md`:307 ⑴ が「見直す」と書いた物） | **檔の中の綴り**として残し、「画面の錨に再び使わないこと」を註で釘付け（`$T.Fetch` も同じ） |

**実測（是正の後）**＝`dotnet test launcher -c Release --nologo` が **803 合格＋1 スキップ**
（段 B の 795＋1・追加 8・削除 0＝新設 `StageBCorrectionTests.cs` 6 本 ＋ 決めた 1 行の枝 2 本）／
`dotnet build launcher -c Release` が **0 警告 0 エラー**／`probe/d-launch-probe.ps1`・
`wizard-probe.ps1` の構文解析 **0 エラー**・`-DryRun` が「nothing was touched.」で終わる。
**アプリは 1 度も起こしていない**。

**段 F への申し送り（追加）**＝`probe/e-install-probe.ps1` の `Invoke-Stage3` で段 B が壊す行は
**3 本**である（下の F-3 の表に 3 本目を足した）＝⑴ 段番号 `1 / 7` ⑵ `FirstRunNoticesBox` の
バイト突き合わせ ⑶ **`:1206-1207`**＝`$T.NoticeTitle`（`第三者物の通知`）を `FirstRunStepTitle` に
当てている行。題は `お知らせ`（`FirstRunViewModel.TitleNotices`）に変わったので、
**3 本目を落とすと赤のまま残る**。

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

#### 段 C の記帳（2026-09-11・実装 1 席・Opus 5）

**入れた物**＝C-1（`ViewModels/UiStrings.cs` 新設＝**利用者に見える文字列の唯一の出所**。
`UiText`（数）は 1 字も触らない。`Views/*.xaml` 7 枚の可視属性を**全部** `{x:Static vm:UiStrings.…}` へ寄せた）・
C-2（`WordLintTests.cs` 新設＝4 本。**先に落として**から通した＝初回は隠す語 41 件・直書き 100 件超で赤）・
C-3（`StatusViewModel.StateLabel` の 6 語 → 帯の語。`enum ServerState` は据え置き）・
C-4（台本 `$T` の書き替え＝`Ready`／`Warming` を同語へ畳み、`Stopped`＝止まっています・`Failed`＝止まりました・
`Used`／`Reserved`／`Baked`／`LatentRef`／`PerVoice`／`Preset`／`ServerDown` を新しい綴りへ。
`Rebuild`／`ClearCache` は**鍵ごと廃した**。`state starts at stopped` は期待値ごと書き直した）・
**A-4／A-5 の積み残し**（`StatusAdvancedExpander`／`TryAdvancedExpander`／`VoicesAdvancedExpander`／
`AboutAdvancedExpander` の 4 枚と、設定 › 詳細への畳み込み）。

**行数**＝詳しい状態の詳細 **11 行**・設定の詳細 **7 行**（上限 12・憲章 原則 7）。
設定が 10 行でなく 7 行である内訳（是正で数え直した）＝⒜ 6 行目「更新のとき、新しくなった分だけ
取り直す」（`SettingsDifferentialUpdateCheck`）は**段 E の持ち物**でまだ無い ⒝ 9 行目「記録」は
ふだんの設定 の `SettingsOpenLogButton` 1 つに寄せた（同じ 1 手を 2 箇所に置かない）
⒞ 10 行目「変えた設定は、次にアプリを開いたときから効きます。」＋〔適用〕〔取り消し〕は
**畳みの外**（画面の下端）に置いた＝ふだんの設定 4 つを変えた利用者が、保存するのに
「詳細（上級者向け）」を開かねばならない形にしないため。**段 E が入れば 8 行**になる。

**id**＝新設 13（`StatusAdvancedExpander`・`TryAdvancedExpander`・`VoicesAdvancedExpander`・
`AboutAdvancedExpander`・`AboutGuideButton`・`AboutSaveLogButton`・`SettingsOpenLogButton`
＋仕様の表に無いが押す物・読む値の 4 つ＝`TryDetailText`・`VoicesCountText`・
`SettingsOpenVoicesDirButton`・`SettingsOpenDataDirButton`
＋是正で足した 2 つ＝`AboutSavedLogText`・`AboutOpenReportFolderButton`）。
**退役 7**（`SettingsShowMemoryCheck`・`SettingsWarmupStagesBox`・`SettingsWarmupVoicesBox`・
`SettingsEmptyCacheBox`・`SettingsEmptyCacheNoteText`・`SettingsReadyTimeoutBox`・`SettingsReadyTimeoutNoteText`）＝
`v2-copy.md` §4 末尾／`v2-spec.md` §2-5 の「入らなかったので消した物（移さない）」そのもの。
**台本はこの 7 つを 1 度も押さず 1 度も読んでいない**ので台本の書き替えは要らなかった。
`AutomationIdsTests` の下敷きを 139 → **132** に改め、退役が本当に消えていることを見る 1 本を足した。
**`settings.json` の鍵は 1 つも減らしていない。**

**据え置き・積み残し**（次の席へ）＝
⑴ `ReleaseFlavors.FlavorLabel`／`Decorate`／`WizardTitle` は**段 F の持ち物**なので触っていない＝
〔このアプリについて〕の「バージョン」は番号だけで、` － RTX（CUDA）` はまだ付かない。
**番号そのものもまだ `v1.1.0` である**（`launcher/Directory.Build.props` の `AppDisplayVersion`）＝
`v2-copy.md` §1-1 の 33 行目と §1-8 の :296／§8 は `v2.0.0` を書くので、この 2 行は**まだ満たしていない**。
版を繰り上げるのは `server/ywk_server.py` の `YWK_VERSION` と門 A-6 と対の仕事＝**段 F**（この檔の冒頭の並び）。
⑵ `Services/Ledger/RuntimeStamp.cs` は**段 E の席が触っている檔**なので触っていない＝
`StatusRebuildRuntimeText` に出る 1 行はまだ「実行系を組み直してください」と綴る。
⑶ `MainViewModel.RebuildRuntimeText` に添える量の 1 行（「取得キャッシュに原檔が …」）と
`CacheCleaner` の断り 3 本も同じ理由（`RuntimeStamp` と対で読む文）で据え置いた。
⑷ 詳しい状態の詳細に〔ログを開く〕は置いていない＝帯の `MainOpenLogButton` が常時見えており、
`MainOpenLogButton` を 2 度綴ると id が重複する。
⑸ ~~`StatusDeviceText` の値はまだ `cuda:0` を含みうる~~＝**是正で当て込んだ**（下の「是正」）。

**実測**＝`dotnet build launcher -c Release --no-incremental` が **0 警告 0 エラー**／
`dotnet test launcher -c Release --nologo` が **818 合格＋1 スキップ**（工事前 809＋1・追加 9・削除 0）／
契約テスト **372 passed**（`upstream/` clean）／`probe/d-launch-probe.ps1`・`probe/wizard-probe.ps1` は
構文 0 エラー・`-DryRun` が「nothing was touched.」で終わる。**アプリは 1 度も起こしていない**（実射は段 H）。

#### 段 C の是正（同日・検分 22 件・Opus 5）

**当て込んだ 22 件**（却下 0）。まとめると 5 つの筋である＝

⑴ **画面に出る文と記録に落とす文を割った**（`v2-copy.md` §1-8 の `VariantGate.cs:102-165`
「画面に出す文とログに落とす文を分ける」）＝`GateDecision` に `Trail` を、
`ServerStartResult` に `NoticeTrail` を足した。`StatusReasonText` の束縛先を内部の `Reason` から
**帯の言い直し** `BandReasonText` へ替え（`v2-spec.md` §2-2＝§2-1a の 3 部品を帯と共用する）、
`ComposeStartOutcome` は内部の理由を告知へ混ぜなくなった。これで
「…（裁定 83）」「実行系が見つかりません」「実行系を起こす段が …」が画面に載る道が全部閉じた。

⑵ **行ごとの当て込みで拾えなかった隠す語を潰した**＝`VoicesViewModel.DropLatentAsync` の
「参照潜在」2 本・`FirstRunViewModel` の「サーバを起こしています…」と「取得台帳」「実行系」・
`SettingsViewModel.Apply` の「暖機」2 本・`VoiceNameValidator` の 5 本（この檔は §1-8 に行が無かった）・
`SpeechRequestBuilder.DescribeError` の「サーバ」2 本。
`StatusDeviceText` は**製品名だけ**にした（`cuda:0`／`gfx1151` を落とす＝§1-2 の 40 行目・憲章 附録 5）。

⑶ **語の検分が本当に効くようにした**＝`WordLintTests` に ⑶ `ViewModels/*.cs` の文字列リテラルと
XAML の**要素の本文**を足した。1 巡目は UiStrings と可視属性しか見ておらず、上の 6 本が
「0 件」の報告の裏を素通りしていた。免除は `LogOnly` の名指し 1 枚（見分けの標識・記録の 1 行・
`UiText` の数の書式）で、**載っているのに 1 度も現れない綴りが在ると落ちる**（表を化石にしない）。

⑷ **台本が歩けなくなっていた 2 箇所を直した**＝節 2（設定と GPU）は `SettingsAdvancedExpander` を
開かずに `SettingsRefreshGpuButton` を押しており、`Invoke-ButtonById` が投げると本体に `catch` が
無いので**そこで走行ごと終わる**形だった。詳しい状態の 13 個も畳みの中なのに 1 度も開いていなかった。
助手 `Open-YwkStatusDetails` を足し、`TabStatus` を選ぶ 15 箇所を全部これに替えた。
門の段の期待値も直した（画面はもう `cu130` の綴りを出さない＝`Get-YwkVariantName` で読み替え）。
**同じ事故を次段で繰り返さないための錠**として `AutomationIdsTests` に
「台本が触る畳みの中の id は台本が開けるようになっている」を足した
（`.csproj` が `probe/*.ps1` と `ViewModels/*.cs` も試験の出力へ写す）。

⑸ **同じ操作に 2 つの名・2 つの動きを残さない**＝帯の ⑶ の札を `UiStrings.StatusRebuildButton`
（「動かすための一式を**入れ直す**」）に寄せた（`v2-spec.md` §2-2 の「新しくする」は copy に揃える）。
〔報告用のログを保存〕は〔ログを開く〕と同じ動きだったのを、**記録を 1 檔にまとめて路を出す**
（`v2-copy.md` §8・`v2-spec.md` §2-6）に直した＝新 id `AboutSavedLogText`／`AboutOpenReportFolderButton`。
`VoicesSelectedMemoryText`（`MiB` を綴る 1 行）は `VoicesAdvancedExpander` の中へ**要素ごと**移した
（`v2-copy.md` §1-8 の `UiText` の行＝「置き場を 詳細 に限る」）。

**是正でも残した物**（次の席へ）＝
⒜ `SpeechRequestBuilder.DescribeError` はまだ「HTTP 500」の番号と「話者」を綴る
（`v2-spec.md` §2-4 は「うまく作れませんでした。…」＋〔ログを開く〕を求める）。
番号を落とすと記録にも残らないので、落とす先（`AppendLog`）を足す仕事と対で直すこと。
⒝ 版の番号 `v1.1.0`（上の ⑴）。

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
  `.iss` の `AppMutex`（`:177`・註 `:178-190`）も**錠と同じ 2 つ**に割る
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

**`OldAppName` の枠は 1 つ**＝この見込みは**誤りだった**（是正・2026-09-11・medium 10）。
`[InstallDelete]` は行をいくつ書いてもよいので、`OldAppName`（v1.1.0 世代）と
**`OlderAppName`（v1.0 世代）**の 2 行を書いた。v1.0.0／v1.0.1／v1.0.2 は v1.1.0 と**同じ `AppId`**で
公開済みなので、v1.0.x → v2.0.0 と直に上げる機体（v1.1.0 を 1 度も通っていない機体）が実在する＝
**判っている欠落ではなく、塞いだ穴である**（§4 の 2 も同じ回に書き替えた）。
**`AppId`・`DefaultDirName`・資産の檔名は据え置き**（§0）。

#### F-2 文書と配布物

| 触る所 | 内容 |
|---|---|
| `installer/irodori-tts-ywk.iss:221` | `docs\install.md` の行を**消し**、`docs\guide.md` を足す。**`docs\radeon.md` の行も同じ回に消した**（是正・2026-09-11・medium 16＝548 行の調べの帳面で、`install.md` と同じ理由＝憲章 原則 8。利用者に要る 2 文は `guide.md` §1 に畳んだ）。`README.md` の行だけが据え置き＝配布物の `docs\` は**両版とも 2 檔** |
| 同 `:177`（`AppMutex`）と註 `:178-190` | **版ごとに割る**＝`Local\irodori-tts-ywk-launcher-cuda`／`…-radeon`（`App.xaml.cs:34` と同じ 2 語・段 E-3・裁定 133）。**註の 2 文が偽になる**＝`:183`「この錠は **両方の種で同じ**…裁定 109 でも変えない」と `:187`「同じ錠・同じ 127.0.0.1:18088」＝**書き直す**（錠は版ごと・つなぎ口だけが 1 つ） |
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
| 調整 | `probe/e-install-probe.ps1` の `Invoke-Stage3`（段 B で必ず落ちる **3 本**） | ⑴ **`:1204-1209`**＝`the step counter says 1 / 7`（`($number -replace '\s','') -eq '1/7'`）の期待値を新しい段数（`VisibleStepCount`＝`1 / 3`、同意済みなら `1 / 2`）へ。⑵ **`:1211-1223`**＝`FirstRunNoticesBox` の本文を `{app}\licensesirst-run-notices.md` のバイトと 1 字ずつ突き合わせる前に、`Invoke-ButtonById -Id 'FirstRunNoticesFullButton'`（**無ければ素通り**）を入れる＝B-4 で全文は畳みの中に入り、開くまで UIA から読めない。⑶ **`:1206-1207`**＝`step 1 is the third party notices step` が `$T.NoticeTitle`（`第三者物の通知`＝`:138`）を `FirstRunStepTitle` に当てている。段 B で題は **`お知らせ`**（`FirstRunViewModel.TitleNotices`）になった＝`$T.NoticeTitle` を新しい題に張り替える（段 B の是正の申し送り） |
| 確認 | `installer-build -All` の門 | **A-6**（exe の版が v2.0.0）・**B-2**（`guide.md` が増えて `install.md` が減る＝差は数 KB・帯 68.0〜76.0 MiB の中）・**B-3**（`SHA256SUMS.txt` の作り直し） |

**危険**＝⑴ `AppName` が変わると「アプリと機能」の表示名が変わる（`AppId` は同じなので上書き更新は通る）。
⑵ `e-install-probe` は `DisplayName` の**頭**と `.lnk` の名を突き合わせる（`:1049`・`:1061`）＝**両方**直す。
⑶ `docs\install.md` を配布物から外すと、**既に導入済みの機体の `{app}\docs\install.md` は残る**
（`[Files]` から消えても消去はされない）＝`[InstallDelete]` に `{app}\docs\install.md` の 1 行を足す。
**規模**＝C# 3 檔の定数・`.iss` 5 行・文書 5 檔・台本 1 檔。**小〜中**（危険は台本と門に集中）。
**検分の目**＝原則 8 の検分文＝「**リポを見たことがない人が最後まで読めるか**。引いている番号・檔名・測定値を
消しても意味が通るか」。`README`・`site`・`guide`・リリース文の 4 面に当てる。

#### 段 F の記帳（2026-09-11・実装 1 席・Opus 5）

**入れた物**＝F-1（版の名札＝`ReleaseFlavors.FlavorLabel` が `RTX（CUDA）`／`Radeon（ROCm）`・
`Decorate` の飾りを `（…）` から新設の `Separator`（` － `）へ・`WizardTitle` の幹を
`初回取得` → `はじめの準備` へ・`AboutViewModel.VersionText` は<b>版を引数に取る</b>形にして
`v2.0.0 － RTX（CUDA）` を返す。**主窓の隅は番号だけ**なので `MainWindow.xaml.cs` は
`AppVersion.Display` を直に読む）・
F-2（`.iss` の `MyAppName` 2 行と `OldAppName` 2 行〔＝**v1.1.0 世代の名**〕・`[Files]` の
`docs\install.md` → `docs\guide.md`・`[InstallDelete]` に `{app}\docs\install.md` の 1 行・
準備完了頁の `VariantText` 2 行も新しい名札へ）・
**版の繰り上げ v1.1.0 → v2.0.0**（`ben-e` §18 の段 1 の並びどおり＝`Directory.Build.props` の
`AppDisplayVersion`・`server/ywk_server.py` の `YWK_VERSION`・契約 ⑻ の見本・`launcher/README` の
settings 見本・`e-install-probe.ps1` の既定の setup 名・`.iss` と `installer-build.ps1` の註・
`ben-f-handoff.md` の現行値 3 箇所）・
**段 C／段 E の申し送り**（⑴ `RuntimeStamp` の 3 本と `CacheCleaner` の 3 本を `UiStrings` へ
⑵ 設定 › 詳細 の 6 行目 `SettingsDifferentialUpdateCheck`（既定 ON・鍵 `differentialUpdate`）を新設して
**8 行**に ⑶ その 1 行と段 E の `RuntimeDiff` を `MainViewModel.TryDifferentialAsync` で結んだ）。

**差分の道の形**（F の中心）＝丸ごと入れ直しの手前に 1 本の枝を置いた。選ぶ条件は 4 つとも
揃った回だけ＝⑴ 設定が ON ⑵ いま動く一式が在る ⑶ `.ledger.json` の写しが在る ⑷ 計画が
丸ごとへ落ちていない。**締め（`RuntimeDiff.VerifyAfterApply`）が偽なら、その場で丸ごとへ落とす**＝
差分の回は `RemovePartialOnFailure=false` で構えるので、黙って終わると新旧が混ざった樹が残る。
`.ledger.json` は締めが通ってから置く（嘘の写しを次の版に信じさせない）。
`ModelDiff` は**見積りだけ**を記録に残す（取得の道は既存のまま＝`v2-spec.md` §11-4）。

**台本**（`probe/e-install-probe.ps1`）＝⑴ `Get-ExpectedAppName` を「幹 ＋ ` － ` ＋ `RTX（CUDA）`／
`Radeon（ROCm）`」に組み直し、旧名は `Get-OldAppName`（`.lnk` の掃除の相手）として残した
⑵ **データ樹を版ごとに**＝`irodori-tts-ywk-cuda`／`-radeon`／旧い共有樹の **3 本**を退避・復元し、
`Reset-WorkDataDir` は樹を名指しで作る ⑶ **撤去の問いが 1 つも出ないこと**を段 7（無人＝
`Defaulting to No …` が **0 行**）と段 8（有人＝2 つの旧い問いの窓が **1 つも出ない**・
**答えない**）の両方で見る ⑷ 段 8 を 3 つに組み直した＝(a) その版の樹が丸ごと消え、
**樹の外に置いた檔は残る** (b) 旧い共有樹も道連れになる（もう一方が入っていないとき）
(c) もう一方の版を入れてある回は**向こうの樹も旧樹も 1 バイトも減らない**
⑸ 段 1 に「`docs\guide.md` が在り `install.md` が無い」を名指しで足した。

**site/index.html は 1 字も触っていない**＝段 E の文書席が `v2-copy.md` §9-3 の 4 の 2 件
（「2 つは別のアプリ・同時には開けない」・撤去の範囲）を既に当て込んでいた（実測で確認）。

**据え置き**（この席の持ち場ではない／次の席へ）＝
⑴ `decisions.md`（主席の持ち物）
⑵ `docs/release-notes/v2.0.0.md` は**既に在る**（`v2-copy.md` §7-2 の逐語）＝この席は触っていない
⑶ **画像 3 枚**（`site/img/step*.png`）は未撮影のまま（`v2-copy.md` §9-3 の 5）
⑷ **A-1 の記録値は動いた**（この計画の見込みは外れた）＝`docs\` は `build\out\app` を通らず、
`ywk_server.py` の版の字も長さが同じ（`1.1.0` → `2.0.0`）なのに **+801 B** ずれた。
出所は**段 D**＝`/ywk/status` に `requests.in_flight` を足したとき（決裁 130 Q4）に
`server/ywk_server.py` が 151,012 → 151,813 B へ伸びており、その回に A-1 を測り直していなかった。
段 F で `34,087,580` → **`34,088,381`** に据え直し、算術を台本の註に書いた（前席と同じ形）。
**動く物がもう 1 つ**＝B-2（`install.md` → `guide.md` の数 KB）＝帯の中。

> **測るときの落とし穴を 1 つ記帳する**＝`server\*.py` はこの樹では **LF**
> （`ywk_fetch_models.py`・`ywk_params.py`・`ywk_server.py`。`server\upstream\` は CRLF のまま）。
> 檔を書き戻すときに CRLF へ変える道具を通すと、この大きさの檔で **1 行 1 バイト＝3,651 B** 増え、
> **配布物と関係のない理由で A-1 が WARN を出す**。実際に 1 度出した（2026-09-11・段 F の 1 巡目）＝
> 直した上で測り直した値が上の `34,088,381` である。

**実測**＝`dotnet build launcher -c Release --no-incremental` が **0 警告 0 エラー**／
`dotnet test launcher -c Release --nologo` が **897 合格＋1 スキップ**（工事前 886＋1・追加 11・削除 0）／
契約テスト **372 passed**／`probe/*.ps1` は構文 0 エラー・`-DryRun` が「nothing was touched.」で終わる／
`build/installer-build.ps1 -All` は **20 門 0 失敗 WARN 0**。**アプリは 1 度も起こしていない**（実射は段 H）。

#### 段 F の是正（同日・検分 20 件の当て込み・Opus 5）

**一番大きい発見＝差分の道は 1 度も通っていなかった。** 配線（`TryDifferentialAsync`）は正しかったが、
差分の回に `WheelInstaller` の門が 3 つとも効いたままで、**どの設定でも必ず失敗して丸ごとへ落ちていた**。
897 本が緑だったのは、釘が **ON／OFF／写し無し／締めが落ちる回**しか見ておらず、
**「ON で、しかも通った」回の釘が 1 本も無かった**からである（medium 7）。3 つとも実射で確かめた。

| 塞ぎ | 実射の 1 行 | 手当て |
|---|---|---|
| 件数の突合 | `展開の件数が台帳と合わない（*.dist-info 4 件・台帳は 1 件）。` | `VerifyDistInfoCount`（新設・差分の回は偽）＝渡る台帳は切れ端で樹には全件が居る。締めは `VerifyAfterApply` が**全件**で撃つ |
| 埋め込み Python | `python の原檔が cache に無い（先に取得を済ませること）：python-3.12.10-embed-amd64.zip` | `SkipPythonEmbed`（新設・差分の回は真）＝取得計画に 1 件も無いのに要求していた。相手の樹には `python.exe` が既に在る |
| 古い畳み | 当てた後に `torch-1` と `torch-2` が並び `VerifyAfterApply=False` | `ReplaceSupersededDistInfo`（新設）＝当てる前に同じ名の `*.dist-info` を落とす。**版が上がった回**が一番ありふれた回である |

**同じ回に塞いだ 5 つ**＝
⑴ **落とす物も入れ替える物も無い回**（`diff.UpToDate`＝持ち物の一覧の檔だけが別物になった）は
0 件の当て込みをせず焼き印と写しを置いて終わる（medium 6）
⑵ **当て込みを始めたあとで落ちた回**は写し（`.ledger.json`）を**落とす**＝混ざる前の姿を名乗ったまま
残ると次の回の差分がその嘘を信じる（medium 4）
⑶ **裁定 91 の受け入れは写しを置かない**（`writeAppliedLedger: false`）＝件数しか見ていない樹に
「中身は全部この内容である」と名乗らせない（medium 5）
⑷ **版の欄は台帳も動いた回でも先に焼き直す**＝同梱の声の突き合わせ（12 檔 35 MB 級）が
毎起動走るのを止める。内容の側は古いままなので催促は続く（low 9）
⑸ **押す前の代金は差分の代金で綴る**＝丸ごとの計画で値を付けると 300 MB の更新に「4.2 GiB」と書く（low 8）。

**インストーラと文書**＝
⒜ `OlderAppName`（v1.0 世代）を足して旧名の近道を **2 世代とも**消す（medium 10・§4 の 2 を閉じた）
⒝ 門 B-1 の内部帳面の表に `install.md`／`radeon.md` を足し、**`docs\guide.md` が入っていること**も
同じ門で見る（medium 11＝行を足し戻しても 20 門が緑のままだった）
⒞ 準備完了頁の `ja.ReadyLabel2a/2b` 4 行を新しい名札へ（medium 15＝`DisableReadyPage` を立てていない
＝**利用者が読む**頁に「ROCm 版」「実行系」「cu130」が残っていた）
⒟ **`docs\radeon.md` を配布物から外した**（medium 16）＝`install.md` と同じ理由。
利用者に要る 2 文は `guide.md` §1 へ。配布物の `docs\` は**両版とも 2 檔**になった
⒠ 〔使い方を見る〕は置き場ではなく **`guide.md` の 1 檔**を選んで開く（low 19＝`v2-spec.md` §2-6 と
`.iss` の註が言っていたのは初めからこれである）
⒡ `e-install-probe` の段 6 が **2 つの `.lnk` と 2 つの docs** を植えて `[InstallDelete]` を実際に撃つ
（low 12＝`Get-OldAppName` に呼び手が付き、新設 `Get-OlderAppName` にも付いた）
⒢ `README.md` の 3 行（`install.md` は「配布物にも入る」・押下 5・檔の並びの表）を今の姿へ（low 14／18／20）
⒣ **`v2-copy.md` §4 と `v2-spec.md` §2-5／§7 を 8 行に揃えた**（medium 17）＝当て込んだ姿が正しく、
⑼⑽ は**畳みの外**に在る（記録はふだんの設定と〔このアプリについて〕・〔適用〕〔取り消し〕は畳みの下）。
**画面は 1 行も直していない。**

**是正後の門**＝`dotnet build launcher -c Release --no-incremental` **0 警告 0 エラー**／
`dotnet test launcher -c Release --nologo` **903 合格＋1 スキップ**（是正で +6・削除 0）／
契約テスト **372 passed**／台本は構文 0 エラー・`d-launch-probe -DryRun` が「nothing was touched.」／
`build/installer-build.ps1 -All` **20 門 0 失敗 WARN 0**（radeon の `Compressing:` は `radeon.md` を
外した分 111 → **110**・B-2 は **75,505,876 B**＝72.01 MiB で帯の中・A-1 は `34,088,381` のまま）。
**アプリは 1 度も起こしていない。**

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

#### 段 G の記帳（2026-09-11・版を切る前の最後の検分＝所見 30 件）

**検分の形**＝reviewer 4 席（story／scenarios／words／installer＋host）が憲章・`decisions.md` 124-133・
`v2-spec.md`／`v2-copy.md`／本檔を正典に、**編集せずに**所見を出し、是正席が 1 席で当て込んだ
（commit はしない・アプリは 1 度も起こしていない・`yomiwakechan2` と `decisions.md` は 1 字も触っていない）。
**決裁 130 と裁定 131／132／133 は 1 つも動かしていない。**

**当て込んだ 28 件**

| # | 所見 | 当て込んだ所 |
|---|---|---|
| high 1 | 憲章 §4-24 の E1（新しい版の差分取り直し）が**帯に出ない**＝導線が ⚙ の畳みの中だけだった | `BandContext.RebuildRuntimeLine` を新設し、`BandText.For` の `Ready`／`Warming` が**緑のまま**理由＋〔動かすための一式を入れ直す〕を返す。`StatusViewModel.ApplyRuntimeStamp` が帯を組み直す。詳しい状態 の 1 手は据え置き（`v2-spec.md` §2-1a の E1 に註を追記） |
| high 7 | はじめの準備を閉じた機体が**行き止まり**（帯＝灰＋「準備しています…」＋釦なし・詳しい状態の釦も消えていた） | `BandContext.FirstRunPending` を新設し、`Stopped` で E-01 の 3 部品（「まだ準備が終わっていません。」＋理由＋〔はじめの準備をする〕）を返す。`MainViewModel.RefreshAcquisition` の `NeedsFirstRun` の枝が `ApplyAcquisition(false)` で潰していたのを `true` に直した |
| high 8 | Radeon（ROCm）版が**見ていない AMD を名乗り、その括弧に NVIDIA の製品名**を入れていた | `FirstRunViewModel.WrongEditionFor` を新設（E-06b）。ROCm の枝は NVIDIA の名を 1 字も出さない |
| high 14 | 設定 › 詳細と はじめの準備の「動かし方」の一覧に**台帳の生の綴り**（`cu130`／`rocm-gfx1151`）が出ていた | `Views/VariantDisplayConverter.cs` を新設し、2 つの `ComboBox` に `ItemTemplate` を噛ませた。**束縛の値は台帳の綴りのまま**＝`settings.json` の `variant` も `AutomationId` も 1 字も動かない |
| high 15／medium 18／26／27 | setup の**完了頁と準備完了頁**に隠す語（取得・第三者物・変種・cu130）と 1024 進の綴り、しかも 10.4 MiB 古い実測が残っていた | `ja.ReadyLabel2a/2b`／`ja.FinishedLabel(NoIcons)` の 8 行を §6-1 の語彙と GB の丸めへ。本体の量は v2.0.0 の [Files] を足し直して **約 96 MB**（実数は `.iss` の註）。`e-install-probe.ps1` の門も `約 96 MB` ＋「GB が在り GiB が無い」に直した |
| medium 2／9 | 帯の**連携の 1 行が 1 つの値で凍っていた**（段 D が契約に足した `requests.in_flight` が届いていない・2 文が到達不能） | `StatusViewModel.ApplyHost(fieldPresent, busy)` を新設（`_hostSeen` は据わり）、`MainViewModel.ApplyStatusSample` が **`Try.HostBusy` の濾したあとの値**を配る。古い註（「まだ契約に無い」）を消した |
| medium 3 | 憲章 §4-21 後半の「配信中の × は 1 行だけ確かめる」が**無かった** | `UiStrings.ExitWhileHostBusy`（憲章の逐語）を新設し、`MainWindow.OnClosing` の**先頭**で `HostBusy` の回だけ YesNo（既定 No）を出す。「いいえ」は `e.Cancel` で**何も片づけずに**返る |
| medium 10 | 素の機体で「**見た上で 0 台**」が作れず、憲章 §7 の 1 行（`NoGpuDecisionLine`）が実機では 1 度も出ないまま 5.3 GB を落としていた | `GpuEnumerator` に `IGpuAdapterInfoSource`（既定＝`DxgiGpuAdapters`＝実行系が無くても答える）を差し、NVIDIA も AMD も 1 枚も居ない回だけ `FailureReason` を落とす |
| medium 11 | `AutoStartOffHint` が**設定の在り処を取り違えて**いた（印は ふだんの設定・詳細 › 読み上げの動作 には 3 釦しか無い） | 文を札そのもの（`UiStrings.SettingsAutoStart`）へ。`v2-spec.md` §2-5 末尾も同じ回に直した |
| medium 12 | 公式ページの「まちがえて入れても、開いたときにアプリが教えます」を**果たすコードが無かった** | E-06b を両向きで実装（ROCm 版＋NVIDIA／CUDA 版＋AMD）。**片方の会社の板しか居ないときだけ**言う＝両社が同居した機体で誤爆しない。⑶ は釦ではなく公式ページの案内（§9 ⒅ に記帳）。`site/index.html` の FAQ も「はじめの準備の画面で」と場所を足した |
| medium 16 | 空きが足りない機体の窓が隠す語だらけ（変種・初回取得・ランチャ・落とすバイト・GiB） | `NotifyFreeSpace` の本文と `PeakDiskText`／`FetchText` を GB の丸めと §6-1 の語彙へ（`PeakDiskBytes` は 1 バイトも動かさない） |
| medium 17 | `AppPublisher` が「＝decisions.md 1」を**発行元**として Windows の「設定 → アプリ」に出していた | 出典だけ落とす（理由の 2 語は残す） |
| medium 19 | `StatusRebuildRuntimeText`（**畳みの外**）が「取得キャッシュ」「原檔」「GiB」を出し、同じ物を 設定 › 詳細 では「一時ファイル」と呼んでいた | `UiStrings.Refetch*` 4 定数と `UiText.RoundedGigabytes` を新設（`Bytes`／`FetchPlanner.FormatBytes` は 1 字も触らない） |
| medium 20／low 21 | 導入先が置き場と重なった 2 つの `MsgBox` に「取得物」「話者」「台帳」「配布物」「樹」と檔名 2 つ | 両方とも §6-1・`v2-copy.md` §9-1 の語彙へ書き直し |
| medium 25 | `docs/ben-f-handoff.md` §1-2 が「**実装で確認した全 14 欄**」のまま＝段 D が足した `requests` が抜けて偽になっていた | 15 欄へ改め、逐語 JSON に `"requests":{"in_flight":0}` を足し、`docs/contract.md` :557-565 と同趣旨の 1 行（本体は読まなくてよい・欄が無ければ 0・`schema` は 1 のまま）を添えた |
| medium 28 | 配布物に入る `README.md` §2 が v1.x のまま（共有の置き場・旧い表示名・変種を選ぶ段・「サーバ起動」） | 4 箇所を v2.0 の姿へ（版ごとの樹・` － RTX（CUDA）`／` － Radeon（ROCm）`・アプリが決める・起動釦は無い） |
| low 5／6 | 憲章 §4-15（設定のふだんの 4 つ）と §4-12（主画面の並びと試聴）に**実装が合っていない**のに、`v2-spec.md` §9 が「合わせなかった所＝無し」と名乗っていた | **機能は足さず**、§9 に ⒄⒅ の 2 行を足して「無し」を訂正。音量と試聴は §4 の積み残しへ |
| low 13 | `docs/guide.md` §6 の見出しが**画面に出ない文**を引いていた（症状引きなのに引けない） | 実物の 3 つ（「グラフィックスが使えませんでした」「このパソコンには対応する GPU が…」「この版では使えません」）へ。`v2-copy.md` §5 の見本も同じ回に揃えた |
| low 22 | `Trail` の内輪の字は `v2-copy.md` §1-8 の決め（詳細へ・据え置き）だが、**憲章 §6-1 と `v2-spec.md` の検分文には例外が書かれていなかった** | `v2-spec.md` §10 原則 1 を「例外は 2 件」に改め、§9 ⒆ と `WordLintTests` の免除表の説明を実態（**畳みの中の ListBox として画面に出る記録**）へ。**憲章への 1 文の追記は決裁事項として下に申し送る** |
| low 23 | `d-launch-probe.ps1` の変種の一覧の門が、**どちらの道でも通る**作りで high 14 を隠していた | 註を実態に、探し語を `ROCm` に、catch の握り潰しを**失敗として記録する**形に。あわせて「行に台帳の綴りが無い」門を 1 つ足した |
| low 24 | `README.md` §0 が**棚ごと**（`docs/release-notes/`）を利用者に指していた＝旧語彙の面へ送り出していた | この版の 1 檔（`v2.0.0.md`）を指し、過去の版の棚は「ここから下は作る側の帳面です」の側へ移した |
| low 26 | `.iss` の註が本体の候補の順を**逆に**記録していた（本体は 2026-09-10 の裁定で cuda 先頭） | 順を直し、正本（本体 `EngineLaunchDefaults.IrodoriYwkCandidates()`）と「両版が入っている機体では RTX（CUDA）版が起きる」を 1 行添えた。綴り 2 つと `MyDirName` は据え置き |
| low 30 | 門 B-1 の内部帳面の網が**4 つの名の黒表**で、`ben-f-handoff.md` ほかを足せば 20 門緑のまま出ていた | 台帳の集合と同じ**白表**へ＝`docs\` に入った檔の集合が `{README.md, guide.md}` と一致しなければ落ちる |
| low 31 | `AppPaths.cs` と `v2-plan.md` の `AppMutex` の鏡の行番号が古い | 実測に合わせて `:177`（註 `:178-190`）へ。**この行番号は錠が黙って効かなくなる箇所を指す**ので載せ続ける |
| （指示） | `site/index.html` の**画像 3 枚の枠**が公開頁に出ていた | 破線の箱と `.shot` の CSS・使わなくなった色 2 つを消し、**差し替え口の `<figure><img …>` の 1 行は HTML の註として逐語で残した**。3 ステップの本文は画像が無くても読み通せる |

**卓へ回した 2 件（reviewer の求めどおり・この席では直さない）**

| # | 何を | なぜ回すか |
|---|---|---|
| ⑴ | **憲章 §6-1 に「詳細の畳みの中の記録は字を据え置く」の 1 文を足すか**（low 22） | `charter.md` は裁可済みの正本（決裁 130）である。`v2-copy.md` §1-8 の決めと憲章の文面が食い違っているので**どちらが正かの裁定が要る**。`v2-spec.md` と `WordLintTests` の 2 檔は先に実態へ揃えた |
| ⑵ | **憲章 §4-15 の「音量」と §4-12 の「その場で試聴」を入れるか**（low 5／6） | 版の直前に機能を足さない判断でこの回は見送り、§9 ⒄⒅ に逸脱として記帳した。入れるなら次の版の持ち場（§4 の積み残し） |

**段 G の門**＝`dotnet build launcher -c Release` **0 警告 0 エラー**／
`dotnet test` **929 合格＋1 スキップ**（段 F の 903 から **+26**＝新設 `StageGCorrectionTests` **24**（帯 10・連携 3・版の取り違え 5・一覧の札 5・× の確かめ 1）
＋ `GpuEnumerationTests` **+2**（0 台の意味を 3 つに割った）。既存で綴りを付け替えたのは
`RoundThreeCorrectionTests` の 1 本だけ。**削除 0**）／契約テスト **372 passed**／
`WordLintTests` 緑／台本は構文 0 エラー・`d-launch-probe -DryRun` が「nothing was touched.」／
`build/installer-build.ps1 -All` **20 門 0 失敗 WARN 0**。**アプリは 1 度も起こしていない。**

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
| 2 | ~~**`OldAppName` の枠が 1 つ**~~ | **塞いだ**（是正・2026-09-11・medium 10）＝枠ではなく行の並びだった。`OlderAppName`（v1.0 世代）を足して `[InstallDelete]` を 2 行にし、`e-install-probe` の段 6 が 2 つの名の `.lnk` を植えて**どちらも消える**ことを見る |
| 3 | **`docs/acceptance.md` D-5 の記録値** | 押下 5 → 4 になる。この計画の書き手の持ち場ではないので、実装席が工事後に直す |
| 4 | **CPU の見せ方** | GPU が在る機体では**一切出さない**（憲章 §7 末尾）。`RuntimeVariants.CudaReleaseChoices` に `cpu` は残す（詳細の中の選択肢）＝**一覧の作りは触らず、出す場所だけを絞る** |
| 5 | **段 E の実射が本機（Radeon）では足りない** | 上書き更新の射は**両方の機体**で要る（RTX 機の席・§2 段 H の 9） |
| 6 | **`MainStateText` の 3 語化と台本の `:1666`** | 自動起動の機体では「止まっています」を通らない＝台本の期待値を**経路ごと**に割る（§2 段 C-4） |
| 7 | **旧い共有樹を前提にした帳面が 2 つ残る** | `docs/design/ben-e-installer.md` §5-4（共有のデータ樹を道連れにしない）と `docs/install.md` の `%LOCALAPPDATA%\irodori-tts-ywk\` の記述は、裁定 133 で**規則ごと古くなった**。どちらもこの席の持ち場ではない（`install.md` は冒頭 2 行の断りだけを足した＝作る側の帳面・本文は据え置き）。**正しい形は `v2-spec.md` §11-8 と本檔 段 E-3／F-2 が持つ**＝実装席が段 F で 2 檔を直す |
