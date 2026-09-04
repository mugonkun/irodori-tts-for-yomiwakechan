# 便 E 設計書 — インストーラ（Inno Setup 6・per-user）（2026-09-05・設計サブ席 opus・統合筆記）

> 正典は `decisions.md`（1・8・9・12・22・25・37・46・51・64・80・83・86〜88）。
> 便 A の申し送り＝`installer/README.md`。導入手順＝`docs/install.md`（本設計で「案」を外した）。
> 受け入れ条件＝`docs/acceptance.md` の「導入」「サイズ」「ドライバ」行と §3。
> ランチャ側の事実＝`docs/design/ben-d-launcher.md`・`launcher/IrodoriTtsYwk.Launcher/Contracts/AppPaths.cs`。
>
> **本檔は 3 案（最小・寿命・検分）と 2 審査の統合である。**採った案・退けた案は §8 の表と各節の脚注に
> 逐語の根拠つきで書いた。**実射で確かめた事実と、読んだだけの推測は節ごとに書き分ける。**
>
> **本檔を書いた席は 1 檔もコードを書いていない**（書いたのは本檔・`docs/install.md`・`installer/README.md` の 3 檔だけ）。
> `launcher/`・`server/`・`build/`・`probe/`・`docs/design/ben-d-launcher.md` は便 D（2）が在飛行中＝読むだけで触っていない。

---

## 0. 範囲と範囲外

**範囲**＝`decisions.md` 8・12 の方式（初回取得・per-user・第三者バイナリを配布物に入れない）を、
**Inno Setup 6.7.3 の `.iss` 1 檔**と**`build/installer-build.ps1` 1 本**と**`probe/e-install-probe.ps1` 1 本**に落とす。
出す資産は**版ごとに 2 本**（CUDA 版・Radeon 版＝`decisions.md` 5）。

| # | 便 E が作る物 | 檔 | 置ける時期 |
|---|---|---|---|
| E-a | インストーラ台本 | `installer/irodori-tts-ywk.iss`（UTF-8 **BOM 付き**・CRLF） | いつでも（`installer/` は便 D の停止域の外） |
| E-b | 生産ライン | `build/installer-build.ps1`（ASCII・CRLF・PS 5.1／7 両対応） | **便 D（2）の飛行が着地してから** |
| E-c | 無人検分 | `probe/e-install-probe.ps1`（同上・日本語は `New-JpText`） | 同上 |
| E-d | 手順の確定 | `docs/install.md`（本設計で確定文にした） | 済 |

**範囲外**（＝やらないと決めた）

1. **ドライバ検査・`torch.cuda.is_available()` の検査・cu130 の門をインストーラに持たない**（§6）。
2. **vc_redist をインストーラで通さない**（§6）。per-user・非昇格を壊すため。
3. **オフライン導入・差分更新パッケージ・自動更新**（`decisions.md` 8・便 D の範囲外宣言）。
4. **コード署名・アイコン・ウィザード画像**＝素材と鍵が要る。無いまま設計を待たせない（§8 危険 7・10）。
5. **利用者データの移行（引っ越し）UI**＝ランチャ側が「読むだけ・移動は未対応」（`SettingsViewModel.cs:371` 逐語
   「データの置き場（読むだけ・**移動は未対応**）」）。手順 1 行だけ `docs/install.md` に置く（§5）。

**受け入れ条件**（`docs/acceptance.md` の該当行＋便 A の E-1〜E-4）

| # | 条件 | どこで測るか |
|---|---|---|
| E-1 | vc_redist 欠落機で**理由が読める形で止まる**（DLL 名を出す・黙って `ImportError` にしない） | §7 段 8（機体の裁定が要る＝Q-E3） |
| E-2 | 日本語・空白を含むユーザ名で全経路が通る | §7 段 9 |
| E-3 | 導入先が読取専用でも 1 周回る | §7 段 4 |
| E-4 | アンインストール後に取得物が意図どおり残る／消える | §7 段 6・7 |
| E-5 | **利用者操作 ≤ 6**（`docs/acceptance.md` 導入行） | §4・§7 段 2 |
| E-6 | **第三者バイナリ 0 件**（`decisions.md` 8・22。例外は .NET ランタイム＝裁定 51） | §3 門 B |
| E-7 | 導入で **UAC が出ない**（`PrivilegesRequired=lowest`）。※ アンインストールは別（§8 危険 5） | §7 段 1（実射済み＝下表 M-6） |

---

## 0-1 この機体で実射して確かめた 8 つ（**断定**・2026-09-05 07:17〜07:21）

検分案の席が使い捨ての `.iss` を ISCC で通して撃ち、**本席がその残骸（`install.log`・`install2〜4.log`・
`uninstall.log`・`uninst-answer.txt`・`e-probe-test.iss`）を開いて逐語で裏を取った**。
リポには 1 檔も入っていない（スクラッチパッドのみ）。

| # | 事実 | 逐語の出典 |
|---|---|---|
| M-1 | 起動した setup の pid には**窓が無い**。窓は子（`…setup.tmp`・`%TEMP%\is-*.tmp\`）が持つ | 実射（親 pid で UIA 子 0・EnumWindows 0） |
| M-2 | 子 pid なら UIA で見える＝`name='… セットアップ' class='TWizardForm'` | 実射 |
| M-3 | **`probe/common.ps1` の要素ヘルパは Inno に効かない**。`Find-ById` は `AutomationIdProperty` で引き（`probe/common.ps1:364`）、`Invoke-ButtonById` は `GetCurrentPattern([InvokePattern]::Pattern)` を撃つ（同 `:427`）。Inno の `TNewButton` は `GetSupportedPatterns()` が空で、`AutomationId` は毎回変わる HWND | 実射＋本席が `probe/common.ps1` の 357-372／417-431 行で実見 |
| M-4 | 押す道は **`SendMessage(hwnd, BM_CLICK=0x00F5)`**。これで `場所 → 準備完了 → 完了` の 3 頁を通し、檔が実際に落ちた | 実射 |
| M-5 | **歓迎頁は既定で出ない**＝`ISetup.chm`「[Setup]: DisableWelcomePage / Default value: **yes** / If this is set to yes, Setup will not show the Welcome wizard page.」 | CHM 逐語（本席が `topic_setup_disablewelcomepage.htm` で実見） |
| M-6 | 無人導入は `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR= /LOG=` で **exit 0**。ログ逐語＝`Setup version: Inno Setup version 6.7.3`／`Windows version: 10.0.26200`／**`User privileges: None`**／**`Administrative install mode: No`**／**`Install mode root key: HKEY_CURRENT_USER`**／`64-bit install mode: Yes`／`Detected previous administrative 64-bit install? No` | `install.log:2,5,8,9,10,11,33`（本席が実見） |
| M-7 | アンインストールも非昇格で通り **exit 0**。ログ逐語＝`Removed all? Yes`・途中 1 回 `Failed to delete directory (145). Will retry later.` | `uninstall.log:26,21` |
| M-8 | **抑制した TaskDialog は `Default` を返す**＝`Defaulting to No for suppressed message box (Yes/No):`（`uninstall.log:28`）／答え檔 `usPostUninstall returned 7 (IDYES=6 IDNO=7)`（`uninst-answer.txt`） | 実射＝無人でも「残す」に落ちる |

**M-8 が §5 の設計（2 段の問い・既定は残す）を無人で成立させる根拠**である。
CHM 逐語＝`SuppressibleTaskDialogMsgBox` は「If message boxes are being suppressed …, **Default is returned**」。

---

## 1. 配置（逐語の樹）

### 1-1 2 つの樹の専管をずらさない（本設計の中心）

| 樹 | 誰の物 | 導入 | 更新 | アンインストール |
|---|---|---|---|---|
| **アプリ樹** `{app}`＝`%LOCALAPPDATA%\Programs\irodori-tts-ywk\` | **インストーラの専管**。利用者は 1 檔も持たない | 丸ごと置く | **丸ごと入れ替える**（§1-3） | 丸ごと畳む |
| **データ樹** `%LOCALAPPDATA%\irodori-tts-ywk\` | **ランチャの専管** | **1 檔も作らない** | **1 檔も触らない** | **尋ねてからだけ**触る（§5） |

逐語の根拠＝`launcher/IrodoriTtsYwk.Launcher/Contracts/AppPaths.cs:216`
「**書き込み側のディレクトリを作る（導入先には 1 檔も作らない）**」、同 `:60-64`
「exe が置かれている場所」「配布樹の根（`server/`・`ledger/`・`licenses/`・`voices/`）。**読むだけ**」。

**この一線を守るだけで `installer/README.md` §3-4 の宿題（「更新配布の形＝実行系とモデルを分離」）は消える。**
モデルと実行系はデータ樹に在り、インストーラはデータ樹に触らないので、**更新でモデルの再取得は起きない**
（実装するものが無い）。

### 1-2 導入先と、そこに入る物（実測）

`{app}` の既定＝`{autopf}\irodori-tts-ywk`。`PrivilegesRequired=lowest` の下で `{autopf}` は `{userpf}` に落ちる
（CHM 逐語＝`autopf | commonpf | userpf`／「{userpf} The path to the current user's Program Files folder …
it will translate to the same directory as `{localappdata}\Programs`」）＝**`%LOCALAPPDATA%\Programs\irodori-tts-ywk\`**
＝`installer/README.md` §1 と `docs/design/ben-a-skeleton-and-build.md` §2 のとおり。

| 中身 | 出所 | 実測バイト（2026-09-05・この機体） |
|---|---|---|
| `IrodoriTtsYwk.Launcher.exe` | `build/out/launcher/win-x64/` | **69,588,729**（SelfContained 1 exe＝裁定 51） |
| `server\`（wrapper＋パッチ適用済みの上流の写し） | `build/out/app/server/` | 3,946,008 |
| `licenses\`（同梱分＋`first-run-notices.md`＝裁定 46） | `build/out/app/licenses/` | 155,094 |
| `voices\`（`presets\` 11 檔＋`presets.json`＋`voices.json`＋`voices.ywk.json`） | `build/out/app/voices/` | 32,622,995 |
| `ledger\`（**版ごとに間引く**＝§1-4） | `build/out/app/ledger/` | 387,063（7 檔＋README） |
| `docs\`（**明示列挙**＝§1-5） | リポ直下 | ≈70 KiB |
| （導入時に増える）`unins000.exe`＋`unins000.dat` | Inno | ≈4.4 MiB＋5.6 KiB（検分案の実測） |

配布樹の合計＝**108 檔・37,111,160 B**（本席が `find`／`du` で実測）。exe と足して **106,699,889 B（101.76 MiB）**、
アンインストーラ込みで **≈106 MiB**。ウィザードに出す数字はこれ。

### 1-3 `[InstallDelete]` を 4 行置く（**寿命案から採る**）

```
[InstallDelete]
Type: filesandordirs; Name: "{app}\server"
Type: filesandordirs; Name: "{app}\ledger"
Type: filesandordirs; Name: "{app}\licenses"
Type: filesandordirs; Name: "{app}\voices\presets"
```

CHM 逐語＝「[InstallDelete] section … its entries are processed as **the first step of installation**」。
理由 2 つ。

⒜ **Inno は「新版で消えた檔」を消さない。**上流の写しから `.py` が 1 檔減った版を上書きすると、
旧檔が `{app}\server\upstream\Irodori-TTS` に残り、`._pth` の 5 行（`docs/install.md` §3）経由で
**依然 import できる**。ledger・licenses・presets も同型（台帳の 1 檔が減っても残る）。

⒝ **種を跨いだ上書きで `ledger\` が混ざる。**`ViewModels/ReleaseFlavor.cs:32-34` の逐語＝
「取得台帳の檔名（拡張子なし）の並びから判る変種（純関数）。**`runtime-rocm-*` が 1 件でもあれば Radeon 版。**」
CUDA 版の `{app}\ledger` に `runtime-rocm-gfx1151.json` が 1 檔残るだけで、**cu130／cu126 の選択肢が UI から消える**。
`AvailableChoices` は `FirstRunViewModel.cs:109` と `SettingsViewModel.cs:66` の**両方**から呼ばれる（本席が実見）ので、
初回ウィザードも設定画面も揃って壊れる。**台帳ディレクトリを毎回まっさらにするのが唯一効く手当てである。**

⒜⒝ は種ごとに `AppId` と導入先を分ける（§1-4）ことでも大半は防げるが、
**同じ種の版跨ぎ**（⒜）は `[InstallDelete]` でしか防げない。

### 1-4 版は 2 本（種ごとに `AppId`・導入先・出力名を分ける）

`decisions.md` 5「**Radeon 版は別リリース**」に素直で、`ReleaseFlavors.Detect` が振る舞いを台帳で決める以上、
**1 本にすると CUDA 機で cu130／cu126 が UI から消える**（§1-3 ⒝）。

| | CUDA 版 | Radeon 版 |
|---|---|---|
| `AppId` | `{F228543A-DCF9-45A3-8826-7485C81E1757}` | `{ECA98712-1574-4D2A-A1FE-0FF5347BF185}` |
| `AppName` | `irodori-TTS for 読み分けちゃん`（＝`launcher/Directory.Build.props:35` の `<Product>` 逐語） | `irodori-TTS for 読み分けちゃん（Radeon 版）` |
| `DefaultDirName` | `{autopf}\irodori-tts-ywk` | `{autopf}\irodori-tts-ywk-radeon` |
| 同梱台帳 | `runtime-cu130` `runtime-cu126` `runtime-cpu` | `runtime-rocm-gfx1151` `runtime-cpu` |
| 同梱 docs | `README.md` `install.md` | ＋`radeon.md` |
| 出力 | `irodori-tts-ywk-setup-v0.1.0-cuda.exe` | `…-v0.1.0-radeon.exe` |

**GUID は一度決めたら永久に変えられない**（CHM＝`AppId` はアンインストール鍵の名を決める）＝
正典に載る性質の値なので、`decisions.md` への記帳を卓に求める（Q-E1）。
`PrivilegesRequiredOverridesAllowed` は**書かない**（既定＝空＝利用者が `/ALLUSERS` で昇格導入に切り替えられない）。

### 1-5 `docs\` は明示列挙する（**最小案から採る**）

`{#SourceDocs}\*.md` のような一括写しは**却下**。実測＝`docs/` 直下は
`acceptance.md`・`contract.md`・`install.md`・`preset-voices-listening.md`・`radeon.md` の 5 檔で、
**受け入れ条件と契約書という内部檔が利用者機へ出る**。
`build/assemble-app.ps1:79` の `$ExcludeDirs` に `'docs'` が在る（本席が実見）＝`build/out/app` に docs は無いので、
**インストーラ側でリポから直に写す**のが正しい。写すのは 2 檔（Radeon 版のみ 3 檔）だけ。

### 1-6 書かない節

- **`[Dirs]` は 1 行も書かない**＝`{app}` に空の器を用意する理由が無い（`AppPaths.cs:216` の逐語）。`[Files]` の `DestDir` が作る。
- **`[UninstallRun]` は 1 行も書かない**＝走らせる外部プログラムが無く、`installer/README.md:29`
  「**bat を使わない**（CRLF／UTF-8 の文字コード事故）」に触れる経路も作らない。消すのは `[Code]`。
- **`[Tasks]` を持たない**＝タスク頁が出ず、**実射した 3 頁**（M-4）のまま保てる。デスクトップアイコンは作らない。
- **`[Registry]` を書かない**＝本体（読み分けちゃん2）は `/health` で見つける（`decisions.md` 6）ので導入先を引く必要が無い。
  「もう一方の種が入っているか」の判定（§5-4）は Inno 自身が作る `HKCU\…\Uninstall\{もう一方の AppId}_is1` を読めば足りる
  （M-6 の逐語＝`Install mode root key: HKEY_CURRENT_USER`）。**新設の鍵を 1 本も持たない。**
- **`LicenseFile` を置かない**＝同意はランチャの `FirstRunStep.Notices` が取る（`decisions.md` 46）。
  インストーラにも置くと**同意の二重取り**になり、操作数を 1 食う。
- **`SetupIconFile`／`WizardImageFile` を指定しない**＝素材待ちを作らない（§0 範囲外 4）。

### 1-7 環境変数を 1 本も立てない（**禁止事項**）

インストーラは `YWK_LAUNCHER_APP_DIR`／`YWK_LAUNCHER_RUNTIME_DIR`／`YWK_LAUNCHER_DATA_DIR` を
**`setx` しない・`[Registry]` の `Environment` にも書かない**。逐語＝`AppPaths.cs:205`
`var developer = appOverride is not null || runtimeOverride is not null || dataOverride is not null;`
＝**1 本でも立てた瞬間に利用者の機体が「開発起動」を名乗る**（`DeveloperMode = true`）。
`[Code]` の `DataDir()` も env を見ずに `{localappdata}\irodori-tts-ywk` を固定で解く（§2）。

---

## 2. `installer/irodori-tts-ywk.iss` の骨（全文）

UTF-8 **BOM 付き**・CRLF（`.gitattributes:7` の `*.iss text eol=crlf`）。
**日本語はこの檔にだけ置く**（ps1 は ASCII のみ＝§3）。
版も路も書き写さず、**すべて `/D` で受ける**。

```
; installer/irodori-tts-ywk.iss -- 便 E。per-user・非昇格・第三者バイナリ 0（decisions.md 8）。
; 呼び方＝build/installer-build.ps1 が ISCC に /D を渡す。この檔に版・路・上流 pin を書き写さない。

#ifndef AppVersion
  #error AppVersion is not defined (build/installer-build.ps1 must pass /DAppVersion=)
#endif
#ifndef AppVersionNumeric
  #error AppVersionNumeric is not defined (AppVersion without the leading 'v')
#endif
#ifndef SrcApp
  #error SrcApp is not defined (build/out/app)
#endif
#ifndef SrcExe
  #error SrcExe is not defined (build/out/launcher/win-x64)
#endif
#ifndef Repo
  #error Repo is not defined (the repository root; docs are copied from here)
#endif
#ifndef OutDir
  #error OutDir is not defined (build/out/installer)
#endif
#ifndef Flavor
  #define Flavor "cuda"
#endif

#if Flavor == "radeon"
  #define MyAppId   "{ECA98712-1574-4D2A-A1FE-0FF5347BF185}"
  #define MyAppName "irodori-TTS for 読み分けちゃん（Radeon 版）"
  #define MyDirName "irodori-tts-ywk-radeon"
#elif Flavor == "cuda"
  #define MyAppId   "{F228543A-DCF9-45A3-8826-7485C81E1757}"
  #define MyAppName "irodori-TTS for 読み分けちゃん"
  #define MyDirName "irodori-tts-ywk"
#else
  #error Flavor must be cuda or radeon
#endif

; --- compile 時の門（組み立ての取りこぼしを「黙って通さない」）-----------------
#if !FileExists(SrcExe + "\IrodoriTtsYwk.Launcher.exe")
  #error the launcher exe is missing (run build/release-build.ps1 first)
#endif
#if !FileExists(SrcApp + "\server\ywk_server.py")
  #error build/out/app/server is missing (run build/assemble-app.ps1 first)
#endif
#if !FileExists(SrcApp + "\licenses\first-run-notices.md")
  #error licenses/first-run-notices.md is missing (decisions.md 46)
#endif
#if !FileExists(SrcApp + "\voices\presets.json")
  #error voices/presets.json is missing (decisions.md 88 (5))
#endif
#if Flavor == "radeon"
  #if !FileExists(SrcApp + "\ledger\runtime-rocm-gfx1151.json")
    #error ledger/runtime-rocm-gfx1151.json is missing (this is the Radeon release)
  #endif
#else
  #if !FileExists(SrcApp + "\ledger\runtime-cu130.json")
    #error ledger/runtime-cu130.json is missing (this is the CUDA release)
  #endif
  #if FileExists(SrcApp + "\ledger\runtime-rocm-gfx1151.json") && false
    ; 参考＝rocm 台帳は [Files] で写さない。混入は installer-build.ps1 の門 A が見る。
  #endif
#endif

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#AppVersionNumeric}
AppVerName={#MyAppName} {#AppVersion}
VersionInfoVersion={#AppVersionNumeric}
AppPublisher=irodori-tts-for-yomiwakechan（非公式・Aratako 氏とは無関係＝decisions.md 1）
DefaultDirName={autopf}\{#MyDirName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
; PrivilegesRequiredOverridesAllowed は書かない（既定＝空）＝昇格導入へ切り替えさせない
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex=Local\irodori-tts-ywk-launcher
; ↑ App.xaml.cs:32 の逐語 private const string MutexName = @"Local\irodori-tts-ywk-launcher";
;   CHM 逐語＝「mutex name comparison in Windows is case sensitive」＝1 字も変えない
RestartApplications=no
; CloseApplications は書かない（既定 yes のまま・頼らない＝§8 危険 6）
Uninstallable=yes
UninstallDisplayName={#MyAppName} {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
OutputDir={#OutDir}
OutputBaseFilename=irodori-tts-ywk-setup-{#AppVersion}-{#Flavor}
; ↑ CHM 逐語＝「Setting this to setup is not recommended: all executables named "setup.exe" are
;   shimmed by Windows application compatibility to load additional DLLs … can be hijacked.」

[Languages]
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[InstallDelete]
Type: filesandordirs; Name: "{app}\server"
Type: filesandordirs; Name: "{app}\ledger"
Type: filesandordirs; Name: "{app}\licenses"
Type: filesandordirs; Name: "{app}\voices\presets"

[Files]
Source: "{#SrcExe}\IrodoriTtsYwk.Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcApp}\server\*";   DestDir: "{app}\server";   Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__\*,*.pyc"
Source: "{#SrcApp}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SrcApp}\voices\*";   DestDir: "{app}\voices";   Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SrcApp}\ledger\README.md";         DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\models.json";       DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\python-embed.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\vc_redist.json";    DestDir: "{app}\ledger"; Flags: ignoreversion
#if Flavor == "radeon"
Source: "{#SrcApp}\ledger\runtime-rocm-gfx1151.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\runtime-cpu.json";          DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#Repo}\docs\radeon.md";                     DestDir: "{app}\docs";   Flags: ignoreversion
#else
Source: "{#SrcApp}\ledger\runtime-cu130.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\runtime-cu126.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\runtime-cpu.json";   DestDir: "{app}\ledger"; Flags: ignoreversion
#endif
Source: "{#Repo}\README.md";       DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#Repo}\docs\install.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\IrodoriTtsYwk.Launcher.exe"

[Run]
Filename: "{app}\IrodoriTtsYwk.Launcher.exe"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\server"
Type: dirifempty;     Name: "{app}"

[Messages]
; 歓迎頁は既定で出ない（M-5）＝WelcomeLabel2 に文言を置かない。準備完了頁と完了頁に置く。
ja.ReadyLabel2a=ここで入るのは本体（約 106 MiB）だけです。実行系とモデル（CUDA 版 cu130 で 5.26 GiB）は、最初にランチャを起動したときに取得します。%n%nインストールを続行するには「インストール」を、設定の確認や変更を行うには「戻る」をクリックしてください。
ja.ReadyLabel2b=ここで入るのは本体（約 106 MiB）だけです。実行系とモデル（CUDA 版 cu130 で 5.26 GiB）は、最初にランチャを起動したときに取得します。%n%nインストールを続行するには「インストール」をクリックしてください。
ja.FinishedLabel=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に起動すると、取得する第三者物の通知が出ます。同意すると取得が始まります（100 Mbps 級で 10 分ほど）。取得の途中では最大 11.56 GiB の空きが要ります。
ja.FinishedLabelNoIcons=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に起動すると、取得する第三者物の通知が出ます。同意すると取得が始まります（100 Mbps 級で 10 分ほど）。取得の途中では最大 11.56 GiB の空きが要ります。

[Code]

const
  { もう一方の種のアンインストール鍵（AppId + '_is1'）。データ樹を共有するので道連れを避ける（§5-4）。}
#if Flavor == "radeon"
  OtherFlavorKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F228543A-DCF9-45A3-8826-7485C81E1757}_is1';
#else
  OtherFlavorKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{ECA98712-1574-4D2A-A1FE-0FF5347BF185}_is1';
#endif
  { docs/acceptance.md 導入行の逐語＝取得中に要る空きは cu130 で 11.56 GiB }
  PeakDiskBytes = 12413511598;

function DataDir(): String;
begin
  { AppPaths.DataDir の既定と同じ場所を固定で解く。env（YWK_LAUNCHER_DATA_DIR）は見ない＝§1-7。}
  Result := ExpandConstant('{localappdata}\irodori-tts-ywk');
end;

function TwoLabels(const A, B: String): TArrayOfString;
begin
  SetArrayLength(Result, 2);
  Result[0] := A;
  Result[1] := B;
end;

function StartsWithDir(const Path, Prefix: String): Boolean;
begin
  Result := (Prefix <> '') and (Pos(Lowercase(AddBackslash(Prefix)), Lowercase(AddBackslash(Path))) = 1);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir: String;
  FreeBytes, TotalBytes: Int64;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    Exit;

  Dir := WizardDirValue;

  { ⑴ 非昇格では書けない場所を断る（ここで断らないと [Files] の途中で落ちる）。}
  if StartsWithDir(Dir, ExpandConstant('{commonpf}'))
     or StartsWithDir(Dir, ExpandConstant('{commonpf32}'))
     or StartsWithDir(Dir, ExpandConstant('{win}')) then
  begin
    MsgBox('この場所には管理者権限が要ります。このインストーラは管理者権限を求めない形（利用者ごとの導入）'
         + 'なので、別の場所を選んでください。既定は' + #13#10
         + ExpandConstant('{autopf}') + '\' + '{#MyDirName}' + ' です。', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  { ⑵ 空きの告知（**止めない**）。見るのは導入先ではなく **取得物が落ちるドライブ**＝§6-3。}
  if GetSpaceOnDisk64(DataDir(), FreeBytes, TotalBytes) then
    if FreeBytes < PeakDiskBytes then
      MsgBox('取得したものは ' + DataDir() + ' に入ります。'
           + 'このドライブの空きは約 ' + IntToStr(FreeBytes div 1073741824) + ' GiB です。' + #13#10
           + 'CUDA 版（cu130）の初回取得には途中で最大 11.56 GiB の空きが要ります'
           + '（落とすバイトは 5.26 GiB）。CPU 版なら 4.53 GiB で足ります。' + #13#10
           + '導入はこのまま続けられます。取得のときにランチャがもう一度確かめます。',
           mbInformation, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  R: Integer;
  Data: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  Data := DataDir();
  if not DirExists(Data) then
    Exit;

  { 1 段目＝取得物。もう一方の種がまだ入っていれば **尋ねない**（共有のデータ樹を道連れにしない）。}
  if not RegKeyExists(HKEY_CURRENT_USER, OtherFlavorKey) then
  begin
    R := SuppressibleTaskDialogMsgBox(
           '取得した実行系とモデルも削除しますか？',
           Data + ' に残っています。残しておくと、次に入れ直したときそのまま使えます。',
           mbConfirmation, MB_YESNO,
           TwoLabels('削除する' + #13#10 + '実行系・モデル・取得キャッシュ・ログ・MIOpen db を消します。',
                     '残す' + #13#10 + '何も消しません（既定）。'),
           0, IDNO);
    if R = IDYES then
    begin
      DelTree(Data + '\runtime', True, True, True);
      DelTree(Data + '\models',  True, True, True);
      DelTree(Data + '\cache',   True, True, True);
      DelTree(Data + '\logs',    True, True, True);
      DelTree(Data + '\miopen',  True, True, True);
    end;
  end;

  { 2 段目＝利用者の資産。docs/install.md §5 の逐語「消すには明示の選択が要る」の口。}
  R := SuppressibleTaskDialogMsgBox(
         '登録した話者と設定も削除しますか？',
         '追加した参照 wav・話者の名前・設定は利用者の資産です。既定では残します。',
         mbConfirmation, MB_YESNO,
         TwoLabels('削除する' + #13#10 + 'voices\ と settings.json を消します。元に戻せません。',
                   '残す' + #13#10 + '話者と設定を残します（既定）。'),
         0, IDNO);
  if R = IDYES then
  begin
    DelTree(Data + '\voices', True, True, True);
    DeleteFile(Data + '\settings.json');
  end;

  { 空になっていれば根も畳む（中身が残っていれば RemoveDir は失敗して何もしない）。}
  RemoveDir(Data);
end;
```

**`[Code]` の分量＝約 110 行**。3 案が置いた `TwoLabels` は M-8 で実射済みの形をそのまま使う。

---

## 3. `build/installer-build.ps1` の仕様

**作法**＝`build/release-build.ps1:3-4` の逐語「ASCII only. CRLF. Must parse and run on Windows PowerShell 5.1
and PowerShell 7+. / No ternary operator, no null-coalescing, no `'&&'` inside this file.」に**そのまま従う**。
`build/Common.ps1` を dot-source して `Get-YwkRepoRoot`・`Start-YwkLog`／`Write-YwkLog`・`Get-YwkSha256`・
`New-YwkDirectory`・`Invoke-YwkNative` を使う（実見で存在を確認済み）。**日本語は 1 文字も置かない。**

```
pwsh -NoProfile -ExecutionPolicy Bypass -File build/installer-build.ps1            # cuda
pwsh ... -File build/installer-build.ps1 -Flavor radeon
pwsh ... -File build/installer-build.ps1 -SkipAssemble -SkipPublish               # 直前の成果物を包むだけ
```

### 3-1 段（順序に理由がある）

| 段 | 何 | 理由 |
|---|---|---|
| 0 | **版を 1 箇所から読む**＝`launcher/Directory.Build.props` の `<AppDisplayVersion>` を `Select-String` で引き、**タグが 1 個でなければ throw**（`release-build.ps1:56-60` と同じ作法） | 版の定義は 1 つ。`.iss` に書き写さない |
| 0b | `$appVersion`（実値 `v0.1.0`）と `$appVersionNumeric`（`TrimStart('v')`＝`0.1.0`）の 2 つを作る | **罠**＝`AppDisplayVersion` は `v` 前置（`Directory.Build.props:31` 逐語 `<AppDisplayVersion>v0.1.0</AppDisplayVersion>`）。`.iss` の `AppVersion=` と `VersionInfoVersion=` は数字だけを受ける |
| 1 | `-SkipAssemble` でなければ `build/assemble-app.ps1` を回す | 配布樹（`build/out/app`）を作る。パッチ適用と上流 clean の判定もここ |
| 2 | `-SkipPublish` でなければ `build/release-build.ps1 -SkipZip` を回す | exe を作る。**上流 pin は exe に焼かれている**（`release-build.ps1:220` 逐語「the assembly carries … and both upstream pins」）＝**インストーラは pin を受け渡さない**（正本を 2 系統にしない） |
| 3 | **門 A（ISCC の前）** | §3-2 |
| 4 | ISCC を撃つ（`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`。無ければ exit 1 で「Inno Setup 6.7.3 を入れよ」と言う） | `/DAppVersion=` `/DAppVersionNumeric=` `/DFlavor=` `/DSrcApp=` `/DSrcExe=` `/DRepo=` `/DOutDir=` |
| 5 | **門 B（ISCC の後）** | §3-3 |
| 6 | sha256 とバイト数を `build/out/installer/SHA256SUMS.txt` と `build/out/installer-log/*.log` に落とす | 署名が無い以上、利用者が突合できる形を配る（§8 危険 7） |

`build/out/` は追跡外（`build/check-tree.ps1` の管掌）＝**コミットしない**（`decisions.md` 22）。

### 3-2 門 A（ISCC の前・5 本）

**なぜ要るか（実測）**＝組み立ての途中で ISCC を撃つと、**`EXIT=0`・「Successful compile」のまま中身の欠けた
setup が出来る**（検分案の実射＝プリセット 11 檔が丸ごと欠けた 65,021,007 B の setup）。
Inno は「Source が 0 件」を既定では止めない。

| 門 | 判定 | 現在値（2026-09-05・本席の実測） |
|---|---|---|
| A-1 | `build/out/app` の**檔数**と**総バイト**をログに書き、期待値から外れたら WARN、**0 件なら exit 1** | **108 檔・37,111,160 B** |
| A-2 | `voices\presets\*.wav` が **11 檔**・`voices\presets.json` が在る | 裁定 88 ⑸ の「配布樹にプリセット 11 檔＋presets.json が在る検査」と同じ玉 |
| A-3 | `licenses\first-run-notices.md` が在る（裁定 46）・種に応じた `ledger\runtime-*.json` が揃い、**もう一方の種の台帳が 0 件** | 種の混入は `.iss` の `[Files]` では防げない（写さないだけ）。ここで見る |
| A-4 | **拡張子の白名簿**で配布樹を走査し、載っていない檔が 1 つでもあれば exit 1 | 実測の全拡張子＝`.py .yaml .json .md .txt .toml .lock .template .example .wav` ＋ 拡張子なし（`LICENSE` `Dockerfile` `.gitignore` `.gitkeep` `.python-version` `.dockerignore`） |
| A-5 | 配布樹の**どの 1 檔も 4 MiB を超えない** | 最大は `voices/presets/vr2_tsukuyomi_ai.wav` の **3,433,004 B**（3.27 MiB）＝余裕 0.7 MiB |
| A-6 | exe の `VersionInfo.ProductVersion` が `$appVersion` と一致（不一致なら exit 1） | publish し忘れた古い exe を包まない。※ **未実射**＝ProductVersion が `v0.1.0` を返すか FileVersion 側かは実装席が段 1 で確かめて、returns する側に合わせる |

**A-4 は黒名簿ではなく白名簿にした。**理由＝`release-build.ps1:187-191` の `$forbidden` は**流用できない**。
その表は `'*.wav'` と `'voices.json'` を禁じるが、配布樹には
**自作 wav 11 檔**（`decisions.md` 37 の逐語「自作の生成物・配布物の同梱資産（11 本・32 MB）」）と
`voices/voices.json`・`voices/voices.ywk.json` が**正当に**入る（本席が実見）。
黒名簿を書き換えると「新しい形の第三者バイナリ」を取りこぼすので、**白名簿＋サイズ上限**の 2 枚で受ける。
これで `decisions.md` 8「第三者バイナリを配布物に入れない」を機械で担保できる
（例外は .NET ランタイム＝裁定 51。exe の中に焼かれているので配布樹の走査には掛からない）。

### 3-3 門 B（ISCC の後・3 本）

| 門 | 判定 |
|---|---|
| B-1 | ISCC の exit code が 0（1＝コンパイル失敗・2＝コマンドライン誤り） |
| B-2 | 出来た setup.exe のバイト数が**帯の中**。**初回ビルドで帯を確定する**＝検分案の実測は cuda 版 **85,349,219 B（81.40 MiB）**（1 標本・圧縮設定が本番と同一とは限らない）ので、暫定の帯は **75〜95 MiB**、初回の本番ビルド後に ±5 % へ締める。**空 setup（2,096,479 B）を配らない最後の網** |
| B-3 | sha256 を取り、`SHA256SUMS.txt` に「`<sha256>  <檔名>`」の 1 行で追記（既存の同名行は置換） |

### 3-4 やらないこと

- **上流 pin を `/D` で渡さない**（段 2 の理由）。
- **`build/` の既存檔を 1 行も書き換えない**（便 D（2）が在飛行中）。`installer-build.ps1` は**新設 1 檔**だけ。
- **`.iss` に版・路・pin を書き写さない**（すべて `/D`）。

---

## 4. 利用者の操作列（数える・≤ 6）

`docs/install.md` §1 の数え方＝**インストーラ全体で 1 操作**。

| # | 操作 | 誰が出すか | 内側のクリック |
|---|---|---|---|
| 1 | setup.exe を実行して最後まで進める | インストーラ | **3**（次へ／インストール／完了）＝M-4 で実射 |
| 2 | ランチャを起動する | — | **0**＝完了頁の「実行する」チェック（`[Run]` の `postinstall`・既定 ON）が起こす |
| 3 | 第三者物の通知に同意する | ランチャ `FirstRunStep.Notices` | 1 |
| 4 | 変種を選んで取得を始める | ランチャ `FirstRunStep.Variant` | 1 |
| 5 | 「起動」を押す | ランチャ | 1 |
| 6 | 読み分けちゃん2 側で配布版エンジンを有効にする | 本体 | 1 |

**合計 6＝`docs/acceptance.md` 導入行「利用者操作 ≤ 6」を満たす。**

**vc_redist の UAC 1 回の扱い（3 案で寿命案だけが挙げた縁）。**
`docs/acceptance.md` の表は昇格の同意を数に入れていないが、**入れれば 7 になる**。
本設計はこれを **手順 2 を 0 操作に畳むこと**で吸収する＝`[Run]` の既定 ON で「完了」ボタンがランチャを起こすので、
表の 6 行のうち 1 行が消え、**vc_redist の UAC を数に入れても 6 のまま**である。
これがインストーラ側で操作数を減らせる唯一の手であり、本設計が `[Tasks]`（デスクトップアイコン）を持たない理由でもある。

**更新導入は 2 クリック**（インストール／完了）＝CHM 逐語「[Setup]: DisableDirPage … Default value: **auto** …
if this is set to auto, at startup Setup will look in the registry to see if the same application is already
installed, and if so, it will **not** show the Select Destination Location wizard page」＝**書かない（既定のまま）**。

**時間**＝導入は数秒（空 setup の無人導入で **0.7 s** を実射・本番は 81 MiB の展開）。
「オンライン導入 ≤ 10 分（100 Mbps 級）」の予算はすべてランチャの取得が使う＝インストーラは 1 % も食わない。

---

## 5. 更新・修復・移行・アンインストールの挙動

### 5-1 更新

- `AppId` 固定・同じ `{app}` へ上書き。場所は尋ねない（CHM 逐語＝`UsePreviousAppDir` 「Default value: **yes**」
  「Setup will look in the registry to see if the same application is already installed, and if so, it will use
  the directory of the previous installation as the default directory」）。
- **差分パッケージは作らない**＝81 MiB の全量置換で足りる。
- **旧版で消えた檔は `[InstallDelete]` が消す**（§1-3）。
- **モデルと実行系は再取得されない**＝データ樹に触らないから（§1-1）。`installer/README.md` §3-4 の宿題はこれで閉じる。
- **走行中は `AppMutex` が弾く**（Setup と Uninstall の両方が起動時に見る＝CHM）。
  ※ `Local\` はセッション名前空間なので**別セッション（高速ユーザ切替・RDP）の個体を検出できるかは未実射＝推測のまま**。
  §7 段 5 で撃って記帳する。`CloseApplications` は既定 yes のまま**頼らない**（走行中のサーバの python.exe は
  `{app}` ではなくデータ樹の `runtime\` に居るので `[Files]` の檔を掴んでいないと見られる＝**推測**）。

### 5-2 修復

- **アプリ樹**＝**同じ setup.exe をもう一度走らせる**。Inno に「修復」の概念は無いので、`[InstallDelete]`＋`[Files]` の
  上書きが唯一の修復経路である。`docs/install.md` §5 にそう書いた。
- **データ樹**＝ランチャの「展開の段をやり直す」。逐語 2 本＝`HttpDownloader.cs`「既に在る＝検証済みなら 1 バイトも
  落とさない（cache/ の再利用）」・`WheelInstaller.cs`「組み直すときに既存の変種ディレクトリを消してから始める」。
  ⇒ **`cache\` を消すことは修復の保険を捨てることでもある**（Q-E2 の重み）。

### 5-3 移行（引っ越し）

**未対応**＝`SettingsViewModel.cs:371` の逐語「データの置き場（読むだけ・**移動は未対応**）」。
`docs/install.md` に手順を 1 行だけ置く＝**ランチャを止めてから `mklink /J "%LOCALAPPDATA%\irodori-tts-ywk" D:\ywk-data`**。
全経路が固定パスを通る（`AppPaths` は env を見なければ `{localappdata}\irodori-tts-ywk` 一択）ので誰も気づかない。
**非昇格で junction が張れるかは未実射**＝実射しない（Q-E3 の不可逆枠を食うため。寿命案の E-6 は退けた）。
※ アンインストール時の注意＝CHM の `DelTree` 逐語「This function will remove directories that are reparse points,
but it will **not** recursively delete files/directories inside them.」＝junction 利用者では**リンクだけ消えて実体は残る**。
それでよい（実体は利用者が置いた場所に在る）が、`docs/install.md` に 1 行書いた。

### 5-4 アンインストール

順＝`usAppMutexCheck`（走行中なら止まる）→ `usUninstall`（`[Files]` の逆再生）→ `usPostUninstall`（§2 の 2 段の問い）→ `usDone`。

1. **`{app}` は畳まれる。**`[UninstallDelete]` は名指しで 2 行だけ（`{app}\server` と `dirifempty {app}`）。
   **`{app}` を `filesandordirs` で丸ごと消す形は採らない**＝CHM 逐語の第 2 の理由
   「if the user happened to install the program in the wrong directory by mistake (for example, C:\WINDOWS)
   and then went to uninstall it there could be disastrous consequences. So again, **DON'T DO THIS!**」は
   樹ごと消す形にも掛かる。`{app}\server` は**こちらが作った枝**で利用者データが 1 檔も無く（§1-1）、
   `dirifempty` は中身があれば何もしない＝誤爆の的が無い。
   ※ 実測＝現在 `build/out/app` に `__pycache__` は **0 件**・`.pyc` **0 檔**（本席が `find` で確認）。
   `PYTHONDONTWRITEBYTECODE=1` を焼くので配布後に生える経路も見当たらない＝`[Files]` の `Excludes` だけで足りる公算が高い。
   `[UninstallDelete]` の 2 行は**保険**である。
2. **問いは 2 段**（1 段目＝取得物・2 段目＝利用者の資産）。**どちらも既定は「残す」**。
   無人（`/VERYSILENT /SUPPRESSMSGBOXES`）では M-8 のとおり**黙って残る**。
   `docs/install.md` §5 の逐語「**`voices\`（利用者の話者）は既定で残す**。消すには明示の選択が要る。」の
   **後半（明示の選択の口）を持つのが 2 段目**である＝最小案の「2 問目を落とす」は退けた。
3. **もう一方の種がまだ入っていれば 1 段目を出さない**（§2 の `OtherFlavorKey`）＝CUDA 版と Radeon 版は
   **同じ `%LOCALAPPDATA%\irodori-tts-ywk\` を共有する**ので、片方を消すともう片方の実行系が道連れになる。
4. **vc_redist は消さない**（他のアプリが使っている可能性がある＝`docs/install.md` §5-4）。
5. **アンインストールでは UAC が出ることがある**＝CHM 逐語「If the installation is running in non administrative
   install mode, but administrative privileges are available anyway then Setup or the [Code] section might still
   make use of these privileges. For this reason **the uninstaller will always be marked as requiring administrative
   privileges in this case**」。この機体・この口座では**出なかった**（M-7＝非昇格で exit 0）が、
   **口座次第で出る**＝檔の文言は「**導入では** UAC を出さない」に限定して書く（`docs/install.md` §0）。

### 5-5 巻き戻し

旧版の setup.exe を実行するだけ（同じ `AppId`・上書き）。データ樹は触られないので取得物は生き残る。

### 5-6 直せない穴（**便 D への申し送り・インストーラでは直せない**）

**台帳が変わった版を配っても、実行系は古いまま走る。**逐語＝`AppPaths.cs:162`
`hasPython ??= static dir => File.Exists(Path.Combine(dir, PythonExeName));`
＝変種ディレクトリの解決は **`python.exe` の在否しか見ない**。「どの台帳から組んだか」の刻印は 1 檔も無い。
よって torch を上げた版を配っても、利用者は**古い torch と新しい wrapper**の組み合わせで走る。
インストーラはデータ樹に触らない設計（§1-1）なので、**ここでは直せない**。
次善策（`[Code]` でデータ樹の `runtime\` を消す）も**採らない**＝消しても `firstRunCompleted=true` のままなので
ウィザードが出ず、`MainViewModel.cs` の逐語「変種の実行系がまだありません（展開の段をやり直してください）。」で
**利用者が出口を失う**。
**便 D への票**＝`settings.json` に `runtimeLedgerSha256` と `installedAppVersion` を焼き、
起動時に配布樹の台帳と食い違ったら「実行系を組み直す」1 手を出す（cache が在れば 1 バイトも落とさずに組み直せる）。

---

## 6. vc_redist・ドライバ・cu130 の門の線引き

### 6-1 インストーラは 3 つとも検査しない

| 何 | 誰がやるか | 逐語の根拠 |
|---|---|---|
| vc_redist | **ランチャ**（取得の段の先頭・`VcRedistInstaller`） | `decisions.md` 87 ⑷「**vc_redist の起動引数は台帳（`ledger/vc_redist.json` の `silent_args`＝`/install /quiet /norestart`）が正**」。通すには**管理者昇格が要る**＝`PrivilegesRequired=lowest`（M-6 の実測＝`User privileges: None`）を壊す |
| ドライバ閾値 | **ランチャ** | `decisions.md` 80「ランチャの variant 選択は `import torch` の成否ではなく **`torch.cuda.is_available()` と `device_count`** で決める」＝**実行系を取った後にしか撃てない**（導入時点で python は 1 檔も無い） |
| cu130 の門 | **ランチャ** | `decisions.md` 88 ⑴「起動前の門…`is_available()=False` か `device_count=0` なら**起動しない**で理由 1 行…**cu130 の CPU 転落は禁止**」 |

**`nvidia-smi` をインストーラから撃たない。**判定を 2 箇所に持つと閾値が腐り、
**CPU 変種でなら使える機体を導入時に弾く**ことになる（`decisions.md` 13＝CPU 合成は可）。

### 6-2 インストーラが語る数字（2 箇所だけ）

準備完了頁と完了頁の 2 つ（§2 の `[Messages]`）。**歓迎頁には置かない**（M-5＝出ない）。
数字の出典＝導入分は本席の実測（§1-2）、取得分は `docs/acceptance.md` 導入行の逐語
「台帳の総和は cu130 **5.26 GiB**（5,646,269,459 B）」・「取得中に要る空きは
`FetchPlan.EstimatedPeakDiskBytes`＝cu130 **11.56 GiB**」（`ledger/README.md` §7 の表と一致）。
**「約 5.4 GiB」は使わない**＝`ledger/README.md` §7 の逐語「以前ここに書いていた『約 5.4 GiB』は調査便の**見積の丸め**であって台帳の実値ではない」。

### 6-3 空き容量の門（2 点の是正）

⑴ **見るドライブは `WizardDirValue` ではなく `{localappdata}\irodori-tts-ywk`。**取得物はそこに落ちる。
導入先だけ別ドライブに移された機体では、導入先の空きを見ても意味が無い。
⑵ **閾値は 8 GiB ではなく 11.56 GiB**（`EstimatedPeakDiskBytes`）で、**警告のみ・止めない**
（cpu 変種なら 4.53 GiB で足りる＝`ledger/README.md` §7）。

---

## 7. 検分台本 `probe/e-install-probe.ps1` と機体の割り当て

### 7-1 台本の型

`probe/common.ps1` を dot-source（ASCII・PS 5.1／7 両対応・日本語は `New-JpText`）。
判定は「**ログ行＋檔＋レジストリ**」を主、UIA は頁の同定にだけ使う（従）。exit code＝失敗した段数（`d-launch-probe.ps1` と同形）。
PS 5.1 では `Get-Content` に **`-Encoding UTF8` を必ず付ける**（`decisions.md` 86 の逐語）。

**Inno 用に 4 関数だけ足す**（M-1〜M-4 から逆算・既存ヘルパは 1 行も変えない）。

| 足す関数 | 何をするか | なぜ既存では駄目か |
|---|---|---|
| `Get-SetupChildProcess` | `Win32_Process` の `ParentProcessId` を辿って `…setup.tmp` を掴む | **M-1**＝起動した pid には窓が無い |
| `Get-InnoWizard` | `Get-ProcessWindowHandles`（裁定 86 の UIA→`EnumWindows` 2 段）で `ClassName='TWizardForm'` を引く | M-1・M-2 |
| `Get-InnoTexts` | `TNewStaticText` の Name を連結して頁を同定する | ボタンの id が使えない |
| `Invoke-InnoButton` | `TNewButton` を Name で引き、`NativeWindowHandle` に **`SendMessage(BM_CLICK=0x00F5)`** | **M-3**＝`Find-ById`（`AutomationIdProperty`）も `Invoke-ButtonById`（`InvokePattern`）も Inno には効かない |

### 7-2 段（9 段）

| 段 | 撃つもの | 対応する条件 |
|---|---|---|
| 1 | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR= /LOG=` で導入 → `{app}` の檔数と sha256 突合 → `HKCU\…\Uninstall\{AppId}_is1` の 3 値 → ログ 3 行（`User privileges: None`／`Administrative install mode: No`／`Install mode root key: HKEY_CURRENT_USER`） | E-7・導入行 |
| 2 | **UI で 3 頁**（`BM_CLICK`）→ 段 1 と同じ結果になる・**頁が 3 枚である**ことを数える | **E-5**（操作 ≤ 6） |
| 3 | ランチャを起こし、初回ウィザード 1 段目に**通知が読める**（`{app}\licenses\first-run-notices.md` を開けている）＝WPF なので UIA が効く | `decisions.md` 46 |
| 4 | `{app}` に `icacls … /deny (OI)(CI)W` を掛けて 1 周（ランチャ起動→通知→変種選択まで） | **E-3** |
| 5 | ランチャ起動中に上書き導入を撃つ → **`AppMutex` が弾くか**（弾かなければ記帳して `CloseApplications` の要否を再判断） | 更新・§5-1 の推測を潰す |
| 6 | **わざと `{app}\ledger\runtime-rocm-gfx1151.json` を置いてから CUDA 版を上書き** → 消えることを見る | `[InstallDelete]` の実射（§1-3 ⒝） |
| 7 | `/VERYSILENT` でアンインストール → `{app}` 消滅・**データ樹が残る**（既定＝残す）・`uninstall.log` に `Defaulting to No for suppressed message box (Yes/No):` | **E-4**・M-8 |
| 8 | UI で「削除する」を 2 段とも選ぶ走 → `runtime`／`models`／`cache` が消え、**もう一方の種が入っている状態では 1 段目が出ない**ことも見る | E-4・§5-4 ⑶ |
| 9 | **日本語＋空白のユーザ名**（新規ローカルユーザ「テスト 太郎」）で段 1〜3 を再走 | **E-2** |

**E-1（vc_redist 欠落機）は上の 9 段に入れない**＝機体の裁定が要る（§7-3・Q-E3）。

### 7-3 機体と日付（**この機体は 2026-09-07（月）に失う**＝`decisions.md` 19）

| 日 | 機体 | 撃つもの | 昇格 |
|---|---|---|---|
| **09-05（今日）** | Radeon | `.iss` と `installer-build.ps1` を書き、**2 版の初回ビルド**（門 A・B の帯を実測で確定）＋段 1〜3・5〜8 | 不要 |
| **09-06（土）** | Radeon | 段 4（ACL）・**段 9＝ローカルユーザ「テスト 太郎」の作成** | **管理者 1 回**（Q-E3） |
| 09-06（土） | Radeon | **E-1＝Windows Sandbox**。実測＝この機体に `WindowsSandbox.exe` も `WindowsSandboxClient.exe` も**無い**＝`Containers-DisposableClientVM` の有効化に**管理者 1 回＋再起動**が要る。既存の `build/out/runtime-cpu`（1,020,237,457 B・取得済み）を持ち込めば**外部取得ゼロで撃てる** | **管理者 1 回＋再起動**（Q-E3） |
| 09-06〜07 | Radeon | **`docs/acceptance.md` §3-6（W-1＝本物の NVIDIA 無し機での CPU 合成）**＝この機体が最後の機会 | 不要（外部取得 2.57 GiB＝Q-E3） |
| **09-08 以降** | RTX 3090 | **Inno Setup 6.7.3 を公式インストーラで導入**（`Inno Setup 6\` 一式を N: で写す案は採らない＝`decisions.md` 22「ライセンスの結論を推測で断定しない」）。cu130／cu126 の実機で「導入→初回取得→起動」を時計付きで 1 周（≤ 10 分の実測）。裁定 88 ⑸ の `CUDA_DEVICE_ORDER=PCI_BUS_ID` | — |

**版を 6.7.3 に揃える根拠**＝この機体の実測ログ逐語 `Setup version: Inno Setup version 6.7.3`（`install.log:2`）。
版が違うと必須メッセージの増減で `MissingMessagesWarning` が出る。

**月曜までに終わらせる不可逆は 2 件だけ**＝⒜ **Radeon 版 setup の実射**（RTX 機に gfx1151 は無い）
⒝ **本物の NVIDIA 無し機での W-1**。他はすべて RTX 機で撃ち直せる。

**E-1 の代案**（Sandbox の昇格が下りないとき）＝RTX 機は「セットアップ直後・Windows ごと壊してよい」
（`decisions.md` 20）ので、**月曜に `where msvcp140.dll` を撃って無ければそのまま E-1 の機体にする**（1 回きり）。

---

## 8. 危険と対策

| # | 危険 | 断定／推測 | 対策 |
|---|---|---|---|
| 1 | 版跨ぎで**消えた檔が残り**、古い `.py` が `._pth` 経由で import される | 断定（Inno は消さない） | `[InstallDelete]` 4 行（§1-3 ⒜） |
| 2 | 種跨ぎで `ledger\` が混ざり **cu130／cu126 が UI から消える** | 断定（`ReleaseFlavor.cs:32-34` 逐語） | `[InstallDelete]`＋種ごとに `AppId`・導入先・出力名を分ける（§1-4） |
| 3 | 組み立ての途中で ISCC を撃ち、**黙って中身の欠けた setup**が出る（`EXIT=0`・「Successful compile」） | 断定（実測 65,021,007 B） | 門 A-1〜A-3・門 B-2（§3） |
| 4 | 内部檔（`acceptance.md`・`contract.md`）が利用者機へ出る | 断定（`docs/` 直下 5 檔を実見） | docs は明示列挙（§1-5） |
| 5 | 導入では UAC が出ないのに**アンインストールで出る** | 断定（CHM 逐語）＋この機体では出なかった（M-7） | 檔の文言を「**導入では** UAC を出さない」に限定（§5-4 ⑸）。RTX 機で再測 |
| 6 | `AppMutex` の `Local\` が別セッションのランチャを検出できない | **推測**（未実射） | §7 段 5 で撃って記帳。`CloseApplications` は既定のまま頼らない |
| 7 | 署名なし exe の SmartScreen | **断定しない**（未実射） | README と Release ノートに sha256 と「詳細情報→実行」の手順。`OutputBaseFilename` を `setup` にしない（CHM 逐語＝hijack） |
| 8 | `{app}` に `.pyc` が生えてアンインストールが残骸を残す | **実測では 0 件**（`PYTHONDONTWRITEBYTECODE=1`） | `[Files]` の `Excludes`＋`[UninstallDelete]` 2 行（保険） |
| 9 | 空き容量の見誤り（`{app}` と `%LOCALAPPDATA%` が別ドライブ） | 断定 | `{localappdata}` のドライブを **11.56 GiB** で見る・警告のみ（§6-3） |
| 10 | アイコン・画像・署名の素材待ちで設計が止まる | — | 3 つとも**指定しない**（既定アイコンで出す・§0 範囲外 4） |
| 11 | 台帳が変わっても実行系が古いまま走る | 断定（`AppPaths.cs:162` 逐語） | **インストーラでは直せない**＝便 D へ 3 行の票（§5-6） |
| 12 | **この機体が 09-07 に失われる** | 断定（`decisions.md` 19） | 不可逆 2 件を先に撃つ（§7-3）。RTX 機には公式版 Inno 6.7.3 |

### 8-1 3 案から**採らなかった**もの（理由つき）

| 退けた案 | 理由（逐語） |
|---|---|
| ようこそ頁に導入サイズを出す（`ja.WelcomeLabel2`） | CHM 逐語「DisableWelcomePage / **Default value: yes** / Setup will not show the Welcome wizard page.」＝**1 度も表示されない**。準備完了頁と完了頁に置く |
| `[UninstallDelete]` に `{app}` を `filesandordirs` で 1 行 | CHM 逐語の第 2 の理由（`C:\WINDOWS` に入れた利用者）は樹ごと消す形にも掛かる。`{app}\server`＋`dirifempty {app}` に差し替え |
| アンインストールの 2 問目（話者も消すか）を落とす | `docs/install.md` §5 の逐語「消すには**明示の選択が要る**」の口そのものが消える |
| `probe/common.ps1` の既存ヘルパで Inno を押す | **M-3**＝`Find-ById` は `AutomationIdProperty`（`:364`）、`Invoke-ButtonById` は `InvokePattern`（`:427`）＝どちらも Inno の `TNewButton` には効かない |
| 8 段を「今日この機体で全部撃つ」 | Sandbox が不在（管理者 1 回＋再起動）＝今日の予算に載らない。段 4・8・9 は 09-06 へ |
| junction 移行の実射（E-6） | 司令官が求めていない経路で、月曜までの不可逆枠を食う。手順 1 行だけ `docs/install.md` に置く |
| 「`Local\` は per-user 導入だから別セッションを止める必要が無い（断定）」 | 高速ユーザ切替・RDP を排除できていない＝**推測**に落とす |
| `CloseApplications=yes` を「`AppMutex` の二重底」と書く | CHM 逐語「Default value: **yes**」＝書いても既定と同じ。走行中の python.exe は `{app}` の檔を掴んでいない（推測）＝効き目が薄い |
| `release-build.ps1` の `$forbidden` を配布樹に流用 | `'*.wav'` と `'voices.json'` を禁じるが、配布樹には正当に入る（§3-2）。**白名簿＋サイズ上限**に建て直した |
| `Inno Setup 6\` 一式を N: で RTX 機へ写す | `decisions.md` 22「ライセンスの結論を推測で断定しない」＝再配布条項の法解釈を席が断定しない。公式インストーラで 6.7.3 を入れる |

---

## 9. 卓へ（3 件・推奨つき）

### Q-E1 版ごとに **2 本**でよいか（推奨＝2 本）

`irodori-tts-ywk-setup-v0.1.0-cuda.exe` と `-radeon.exe`。**`AppId` 別・導入先別**。
理由＝`decisions.md` 5「Radeon 版は別リリース」に素直で、`ReleaseFlavors.Detect` が台帳で振る舞いを決める以上、
**1 本にすると CUDA 機で cu130／cu126 が UI から消える**（§1-3 ⒝）。
あわせて 2 つ：⑴ `installer/README.md` §1 の「per-user のインストーラ **1 本**」は「per-user か per-machine か」の
意味と読んで **「版ごとに 1 本」に書き直した**（本設計で改訂済み・覆してよい）。
⑵ **`AppId` の GUID 2 個を `decisions.md` に記帳してよいか**＝GUID は一度決めたら永久に変えられない
（CHM＝アンインストール鍵の名を決める）＝正典に載る性質の値である。
値＝CUDA `{F228543A-DCF9-45A3-8826-7485C81E1757}`／Radeon `{ECA98712-1574-4D2A-A1FE-0FF5347BF185}`。

### Q-E2 「導入後 ≤ 7.0 GB」「実行系 ≤ 2.2 GiB」の**読み方**を決めてほしい（推奨＝落とすバイトで読む＋cu130 の展開後を実測してから条件値を裁定）

`docs/acceptance.md` サイズ行の原文＝「実行系 ≤ 2.2 GiB（cu130）＋モデル ≤ 3.4 GiB・1 資産 ≤ 2 GiB・**導入後 ≤ 7.0 GB**」。

- **落とすバイトで読めば満たす**＝cu130 の実行系 1,944.1 MiB（**1.90 GiB**）・モデル 3,570,982,039 B（**3.33 GiB**）
  （`ledger/README.md` §7）。
- **展開後で読むと破る**。本席の実測＝`build/out/runtime-rocm-gfx1151` は **4,636,458,599 B（4.32 GiB）**（台帳 1,465.5 MiB の 3.02 倍）、
  `build/out/runtime-cpu` は **1,020,237,457 B**（台帳 270.1 MiB の 3.60 倍）。`FetchPlanner.cs:103` の
  `ExpansionFactor = 3.3` もこれと整合する。
  ⇒ **Radeon 版の導入後は実測で `{app}` 0.107＋実行系 4.636＋モデル 3.571 ＝ 8.31 GB（cache を除いて）**＝
  **7.0 GB を 1.3 GB 超える。**
- **cu130 の展開後はこのリポに実測樹が無い**＝**本席は断定しない**。台帳から推すと 5.7〜6.8 GiB の帯で、
  この場合 cache を消しても 9〜10 GB になる。

**推奨**＝⑴ サイズ行を「**落とすバイト**で読む」と明記する（`docs/acceptance.md` は便 D（2）が在飛行中なので**本席は触っていない**）。
⑵ 「導入後 ≤ 7.0 GB」は **cu130 の展開後を 1 回実測してから**条件値を裁定する（RTX 機・月曜以降で足りる）。
⑶ 実測を待たずに効く手当てとして、**`cache\`（cu130 で 1.93 GiB）は「初回の合成が 200 で返った後」にランチャが消す**
（実装は便 D）。単純な「取得完了時に消す」だと、最初の起動が落ちた機体が 1.93 GiB を取り直す
（`HttpDownloader` の逐語「既に在る＝検証済みなら 1 バイトも落とさない」＝cache は修復の保険でもある＝§5-2）。
※ **アンインストーラ側では既に消す**（§2 の 1 段目）。

### Q-E3 この機体で撃つ**不可逆**と、要る**管理者昇格 2 回**を許可してほしい（推奨＝どちらも許可・W-1 は cu126 だけ）

月曜で失う機体でしか撃てないのは 2 件＝⒜ **Radeon 版 setup の実射** ⒝ **本物の NVIDIA 無し機での W-1**
（`docs/acceptance.md` §3-6）。⒝ は **cu126 だけ**に一票（外部取得 2.57 GiB。cu130 は
`decisions.md` 83 の「最初の合成で 0xC0000005」が既に取れており増える情報が薄い）。
あわせて**管理者昇格が各 1 回**要る＝⑴ 段 9 のローカルユーザ「テスト 太郎」の作成、
⑵ E-1 の **Windows Sandbox 機能の有効化（`Containers-DisposableClientVM`）＋再起動**
（実測＝この機体に `WindowsSandbox.exe` は**無い**）。**黙って撃ってよいかを裁定してほしい。**
⑵ が下りない場合の代案＝**月曜に RTX 機（セットアップ直後）で `msvcp140.dll` の在否を見て、無ければそのまま E-1 の機体にする**。

---

## 10. 実装席への指示

### 10-1 担当 path（**これ以外に 1 檔も触らない**）

| 席 | 書いてよい檔 |
|---|---|
| E-1（台本席） | `installer/irodori-tts-ywk.iss`（新規・UTF-8 **BOM 付き**・CRLF） |
| E-2（生産ライン席） | `build/installer-build.ps1`（新規・ASCII・CRLF・**便 D（2）の着地後**） |
| E-3（検分席） | `probe/e-install-probe.ps1`（新規・ASCII・CRLF・**便 D（2）の着地後**）。`probe/common.ps1` は**読むだけ**（足す 4 関数は `e-install-probe.ps1` の中に置く） |
| 記帳 | 実射の結果は本檔の末尾に節を足す（`docs/design/ben-e-installer.md`） |

### 10-2 停止域

- `launcher/`・`server/`・`build/` の**既存檔**・`probe/` の**既存檔**・`docs/design/ben-d-launcher.md` は**読むだけ**（便 D（2）が在飛行中）。
- `docs/acceptance.md` は**読むだけ**（便 D（2）が編集中）。値を動かす提案は本檔 §9 に書いて卓へ回す。
- `C:/IrodoriTTS/`・`C:/irodori-TTS-server/`・`yomiwakechan2`・**System32** に書かない。D:・E:・F: に触らない。
- **git commit しない。外部へ出ない**（GitHub push・HF・PyPI・Release は司令官の裁定まで）。
- 第三者バイナリを `build/out/` と `.venv-dev/` 以外のリポ内に置かない（`decisions.md` 22）。

### 10-3 受け入れ条件（実装席が自分で撃って通す）

| # | 条件 | 撃ち方 |
|---|---|---|
| I-1 | `installer-build.ps1 -Flavor cuda` と `-Flavor radeon` が **exit 0** で 2 本を出す | §3 |
| I-2 | 門 A の 6 本・門 B の 3 本が**わざと壊した樹**で `exit != 0` になる（プリセット 1 檔を退ける・rocm 台帳を混ぜる・4 MiB 超の檔を置く） | §3-2・§3-3 |
| I-3 | 無人導入 → `{app}` の檔数と sha256 が配布樹と 1 檔も違わない | §7 段 1 |
| I-4 | UI で 3 頁を `BM_CLICK` で通し、無人と同じ結果になる | §7 段 2 |
| I-5 | 通知がウィザード 1 段目に出る（`decisions.md` 46） | §7 段 3 |
| I-6 | 上書き導入で **rocm 台帳が消える**・走行中は `AppMutex` の挙動を記帳する | §7 段 5・6 |
| I-7 | アンインストール 2 通り（残す／消す）＋無人では**残る** | §7 段 7・8 |
| I-8 | 読取専用 `{app}`（E-3）と日本語＋空白のユーザ名（E-2）で 1 周 | §7 段 4・9 |
| I-9 | **ps1 は ASCII のみ・CRLF・PS 5.1 と 7 の両方で parse できる**（`release-build.ps1:3-4` の作法） | 両方で `-WhatIf` 相当の空走 |

### 10-4 着工の順（この機体の残り時間から）

1. `installer/irodori-tts-ywk.iss` を書く（**便 D（2）の着地を待たない**＝`installer/` は停止域の外）。
2. 便 D（2）が着地したら `build/installer-build.ps1` を置き、**2 版の初回ビルド**で門 B-2 の帯を実測で確定する。
3. `probe/e-install-probe.ps1` の段 1〜3・5〜8 を今日撃つ。
4. 09-06 に段 4・9（管理者 1 回）と E-1（Sandbox・管理者 1 回＋再起動）＝**Q-E3 の裁定を得てから**。
5. 09-06〜07 に W-1（cu126 だけ）＝**Q-E3 の裁定を得てから**。
6. 月曜以降、RTX 機に Inno 6.7.3 を公式インストーラで入れ、cu130／cu126 の 1 周を時計付きで撃つ。
