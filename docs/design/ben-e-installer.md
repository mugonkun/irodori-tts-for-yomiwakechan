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
| **アプリ樹** `{app}`＝`%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda\`（ROCm 版は `…-radeon\`＝**裁定 109**・§17-2） | **インストーラの専管**。利用者は 1 檔も持たない | 丸ごと置く | **丸ごと入れ替える**（§1-3） | 丸ごと畳む |
| **データ樹** `%LOCALAPPDATA%\irodori-tts-ywk\` | **ランチャの専管** | **1 檔も作らない** | **1 檔も触らない** | **尋ねてからだけ**触る（§5） |

逐語の根拠＝`launcher/IrodoriTtsYwk.Launcher/Contracts/AppPaths.cs:216`
「**書き込み側のディレクトリを作る（導入先には 1 檔も作らない）**」、同 `:60-64`
「exe が置かれている場所」「配布樹の根（`server/`・`ledger/`・`licenses/`・`voices/`）。**読むだけ**」。

**この一線を守るだけで `installer/README.md` §3-4 の宿題（「更新配布の形＝実行系とモデルを分離」）は消える。**
モデルと実行系はデータ樹に在り、インストーラはデータ樹に触らないので、**更新でモデルの再取得は起きない**
（実装するものが無い）。

### 1-2 導入先と、そこに入る物（実測）

`{app}` の既定＝`{autopf}\{#MyDirName}`＝**cuda は `irodori-tts-ywk-cuda`・radeon は `irodori-tts-ywk-radeon`**
（**裁定 109**・§17-2。改訂前の cuda は版なしの `irodori-tts-ywk` だった）。
`PrivilegesRequired=lowest` の下で `{autopf}` は `{userpf}` に落ちる
（CHM 逐語＝`autopf | commonpf | userpf`／「{userpf} The path to the current user's Program Files folder …
it will translate to the same directory as `{localappdata}\Programs`」）＝
**`%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda\`**（ROCm 版は `…\Programs\irodori-tts-ywk-radeon\`）
＝`installer/README.md` §1 と `docs/design/ben-a-skeleton-and-build.md` §2 のとおり（両檔とも裁定 109 で改訂済み）。

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

> **追記＝裁定 109（2026-09-07）＝5 行目**。`Type: files; Name: "{autoprograms}\{#OldAppName}.lnk"`。
> 表示名を改めたので、改名の前に入れた機体では旧名のスタートメニューの近道が残る（Inno は
> `[Icons]` の名が変わっても旧名の `.lnk` を消さない）。詳細＝§17。

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

> **改訂＝裁定 109（2026-09-07）**。`AppName` の 2 つと CUDA 側の `DefaultDirName` が変わった。
> 下の表は**改訂後**の値である（改訂前の値と理由は §17 に置いた）。

| | CUDA 版 | ROCm 版（内部の Flavor id は `radeon`） |
|---|---|---|
| `AppId` | `{F228543A-DCF9-45A3-8826-7485C81E1757}` | `{ECA98712-1574-4D2A-A1FE-0FF5347BF185}` |
| `AppName` | `irodori-TTS for 読み分けちゃん（CUDA 版）` | `irodori-TTS for 読み分けちゃん（ROCm 版）` |
| `DefaultDirName` | `{autopf}\irodori-tts-ywk-cuda` | `{autopf}\irodori-tts-ywk-radeon` |
| 同梱台帳 | `runtime-cu130` `runtime-cu126` `runtime-cpu` | `runtime-rocm-gfx1151` `runtime-cpu` |
| 同梱 docs | `README.md` `install.md` | ＋`radeon.md` |
| 出力 | `irodori-tts-ywk-setup-v0.1.0-cuda.exe` | `…-v0.1.0-radeon.exe` |

※ `launcher/Directory.Build.props` の `<Product>irodori-TTS for 読み分けちゃん</Product>` は
**版を足さない**（exe は 1 本で両リリースを兼ねるので、アセンブリの属性に片方の版を焼けない）。
`AppName` はそこから版の名札を足した文字列＝ランチャ側の
`ReleaseFlavors.AppTitle(ReleaseFlavor)`（`launcher/IrodoriTtsYwk.Launcher/ViewModels/ReleaseFlavor.cs`）が
出す文字列と**1 字も違わない**。

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

; 表示名と CUDA 側の既定の導入先は裁定 109（2026-09-07）で改めた＝§17。
; OldAppName は改名前の AppName の逐語で、[InstallDelete] が旧名の近道を 1 本消すために要る。
#if Flavor == "radeon"
  #define MyAppId    "{ECA98712-1574-4D2A-A1FE-0FF5347BF185}"
  #define MyAppName  "irodori-TTS for 読み分けちゃん（ROCm 版）"
  #define OldAppName "irodori-TTS for 読み分けちゃん（Radeon 版）"
  #define MyDirName  "irodori-tts-ywk-radeon"
#elif Flavor == "cuda"
  #define MyAppId    "{F228543A-DCF9-45A3-8826-7485C81E1757}"
  #define MyAppName  "irodori-TTS for 読み分けちゃん（CUDA 版）"
  #define OldAppName "irodori-TTS for 読み分けちゃん"
  #define MyDirName  "irodori-tts-ywk-cuda"
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
; 裁定 109＝旧名のスタートメニューの近道を 1 本消す（§1-3 の追記・§17）。
Type: files; Name: "{autoprograms}\{#OldAppName}.lnk"

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
| A-2 | `voices\presets\*.wav` が **12 檔**・`voices\presets.json` が在る（**裁定 118**） | 裁定 88 ⑸ の「配布樹にプリセット 11 檔＋presets.json が在る検査」と同じ玉（**11 は当時の逐語＝歴史**。裁定 118 で 1 本増えたので門は 12） |
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
| 1 | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=` で導入（**`/DIR` は渡さない**＝`.iss` の `DefaultDirName` そのものを実射する。台本の逐語＝`probe/e-install-probe.ps1` の頭 "No /DIR is passed, so the default of the .iss is what gets exercised."）→ `{app}` の檔数と sha256 突合 → `HKCU\…\Uninstall\{AppId}_is1` の 3 値（**`InstallLocation` が版ごとの既定＝`…-cuda\`／`…-radeon\`・`DisplayName` がその版の `AppName` で始まる**＝**裁定 109**）→ **新しい近道が `{autoprograms}\<AppName>.lnk` 1 本**（旧名の近道は `[InstallDelete]` が消す）→ ログ 3 行（`User privileges: None`／`Administrative install mode: No`／`Install mode root key: HKEY_CURRENT_USER`） | E-7・導入行・裁定 109 |
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
---

## 11. 台本席（便 E（1））の記帳（2026-09-05 08:14〜08:26・Radeon 機・実装サブ席 opus）

> 本節は**この席が実際に撃った物だけ**を書く。読んだだけ・推したものは「推測」と明記する。
> 書いた檔は `installer/irodori-tts-ywk.iss` **1 檔**と本節だけ（設計書 §10-1・§10-2 の担当 path）。
> `launcher/`・`server/`・`build/`・`probe/`・`docs/acceptance.md`・`docs/design/ben-d-launcher.md` は
> **読むだけ**（便 D（2）が在飛行中）。`git commit` していない。外部へ出ていない。
> **`%LOCALAPPDATA%\Programs\` に本導入していない**（試しの導入はスクラッチパッドの下・終わったら畳んだ）。
> **ランチャは 1 度も起動していない**（`[Run]` は `skipifsilent` なので `/VERYSILENT` では走らない）。

### 11-1 置いた檔（**断定**・実測）

| 檔 | バイト | 形 |
|---|---|---|
| `installer/irodori-tts-ywk.iss` | **15,283** | UTF-8 **BOM 付き**（先頭 3 バイト＝`239,187,191`＝`EF BB BF`）・**CRLF 282 対・単独 LF 0 個**・283 行 |

`.gitattributes:7` の `*.iss text eol=crlf` と `installer/README.md` §1 の「UTF-8 **BOM 付き**」に合わせた。
日本語（`[Messages]`・`MsgBox`・`SuppressibleTaskDialogMsgBox` の文言）が**アンインストールのログに逐語で出た**
（§11-4 の `Defaulting to No …` の 2 行）＝BOM が効いていることは実射で裏が取れている。

### 11-2 `.iss` の要点（設計書 §2 の骨との対応）

- **`/D` は 7 本**（`AppVersion`・`AppVersionNumeric`・`Flavor`・`SrcApp`・`SrcExe`・`Repo`・`OutDir`＝裁定 91）。
  7 本とも `#ifndef` → `#error` で受け、**版も路も上流 pin も檔に書き写していない**。`Flavor` だけ既定 `"cuda"`。
- **`AppId` は `Flavor` で切り替え**＝CUDA `{F228543A-DCF9-45A3-8826-7485C81E1757}`／
  Radeon `{ECA98712-1574-4D2A-A1FE-0FF5347BF185}`（裁定 90）。`AppId={{#MyAppId}` の書き方で
  **アンインストール鍵の名が `{ECA98712-…-0FF5347BF185}_is1` になることを実射で確かめた**（§11-4）。
- `PrivilegesRequired=lowest`・`PrivilegesRequiredOverridesAllowed` は**書かない**・
  `DefaultDirName={autopf}\irodori-tts-ywk`（Radeon 版は `…-radeon`）・`MinVersion=10.0`・
  `ArchitecturesAllowed=x64compatible`。
- **`AppMutex=Local\irodori-tts-ywk-launcher`**＝`launcher/IrodoriTtsYwk.Launcher/App.xaml.cs:32` の逐語
  `private const string MutexName = @"Local\irodori-tts-ywk-launcher";` を 1 字も変えずに写した
  （本席が実見。CHM 逐語＝mutex 名の比較は大小文字を区別する）。
- `Compression=lzma2/max`・`SolidCompression=yes`・`WizardStyle=modern`・
  `OutputBaseFilename=irodori-tts-ywk-setup-{#AppVersion}-{#Flavor}`（`setup` にしない＝CHM の hijack の逐語）。
- `[Languages]` は `ja` 1 本（`compiler:Languages\Japanese.isl`）。**警告 0 で通った**＝6.7.3 の
  `Japanese.isl` に対して必須メッセージの過不足が無い。
- `[InstallDelete]` 4 行・`[Files]`（`docs` は明示列挙＝Radeon 版のみ `radeon.md` を足す 3 檔）・
  `[Icons]` 1 行（`{autoprograms}`）・`[Run]`（`postinstall skipifsilent`）・`[UninstallDelete]` 2 行。
- `[Code]` は設計書 §2 のまま＝`DataDir()`・`TwoLabels`・`StartsWithDir`・`NextButtonClick`（場所の門＋空きの告知）・
  `CurUninstallStepChanged`（2 段の問い・既定 No・もう一方の種が居れば 1 段目を出さない）。
- **compile 時の門を 1 本足した**（設計書 §2 は 4 本＋種別 1 本）＝CUDA 版で `runtime-cu126.json` の在否、
  Radeon 版で `{#Repo}\docs\radeon.md` の在否。`[Files]` に名指しで書いてある檔が消えたときに
  **ISCC が自分で止まる**（`#error`）ようにするため。

### 11-3 ISCC の逐語（**断定**・2 版・警告 0・exit 0）

道具＝`C:\Users\mugonkun\AppData\Local\Programs\Inno Setup 6\ISCC.exe`（1,456,272 B）。
冒頭の逐語＝`Compiler engine version: Inno Setup 6.7.3` ／ `Non-commercial use only`。
`/O` はスクラッチパッド（`…\scratchpad\ben-e1\out`）。`/D` は 7 本を手で渡した
（`AppVersion=v0.1.0`・`AppVersionNumeric=0.1.0`＝`launcher/Directory.Build.props:31` の
`<AppDisplayVersion>v0.1.0</AppDisplayVersion>` から v を剥いだ値）。

| 版 | 逐語 | 終了コード | 檔数（`Compressing:` 行） | 出た exe のバイト | sha256 |
|---|---|---|---|---|---|
| cuda | `Successful compile (12.297 sec).` | **0** | **110** | **84,984,160**（81.05 MiB） | `569f885c9c74c3d0c6af813d241010188a9d78625cf15a8c462625200be6f426` |
| radeon | `Successful compile (12.438 sec).` | **0** | **110** | **84,998,686**（81.06 MiB） | `bdfe280d4b02f5381fe16011a739e931d1cad852cd5d19e6e096862d2ec9789c` |

- **警告は 2 版とも 0 行**（`warning`／`error` を大小無視で引いて 0 件）。
- **110 檔の内訳**＝ランチャ exe 1 ＋ 配布樹 106（cuda は `runtime-rocm-gfx1151.json` を、radeon は
  `runtime-cu130.json`／`runtime-cu126.json` を写さない）＋ docs（cuda 2 檔・radeon 3 檔）。
  配布樹の実測＝**108 檔・37,111,160 B**（`.pyc` を除く）で、設計書 §1-2 の数字と 1 バイトも違わない。
- **`.pyc` と `__pycache__` は 1 檔も入っていない**（`Compressing:` 行を `pycache|\.pyc` で引いて 0 件）。
  ※ 本席が撃った時点の `build/out/app` には `__pycache__` が **3 つ・`.pyc` が 21 檔（541,008 B）**
  生えていた（設計書 §5-4 の「実測＝0 件」は本席の時点では**古い**。便 D（2）がサーバを走らせた跡と見られる＝**推測**）。
  **`[Files]` の `Excludes` が実際に効いた**ことがこれで裏取りできた。
- **サイズの帯**＝門 B-2（設計書 §3-3）の暫定帯 75〜95 MiB の中。**2 版とも 81.0 MiB**なので、
  帯を ±5 % に締めるなら **77〜85 MiB** が実測に基づく提案（生産ライン席＝E-2 へ）。
  ※ この 2 本は `/O` でスクラッチパッドに出しただけで、`build/out/installer` には 1 檔も置いていない。

### 11-4 試しの導入 1 周の逐語（**断定**・Radeon 版・スクラッチパッドの下）

撃った物＝`irodori-tts-ywk-setup-v0.1.0-radeon.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
/DIR=<scratch>\ben-e1\app-radeon /LOG=<scratch>\ben-e1\install-radeon.log`。

**導入**＝**exit 0**・**3.08 s**。ログ 612 行。求められた 3 行の逐語（`install-radeon.log`）＝

```
2026-09-05 08:21:04.863   Setup version: Inno Setup version 6.7.3
2026-09-05 08:21:04.863   Windows version: 10.0.26200
2026-09-05 08:21:04.863   User privileges: None
2026-09-05 08:21:04.865   Administrative install mode: No
2026-09-05 08:21:04.865   Install mode root key: HKEY_CURRENT_USER
2026-09-05 08:21:04.865   64-bit install mode: Yes
2026-09-05 08:21:07.368   Detected previous administrative 64-bit install? No
2026-09-05 08:21:07.368   Creating new uninstall key: HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\{ECA98712-1574-4D2A-A1FE-0FF5347BF185}_is1
2026-09-05 08:21:07.370   Installation process succeeded.
```

⇒ **E-7（導入で UAC が出ない）は本席の走でも再現**（`User privileges: None`）。

**`{app}` の実測**＝**112 檔・22 ディレクトリ・111,137,002 B**（106.0 MiB）。内訳（本席が `find` で実測）＝

| 枝 | 檔 | バイト | 設計書 §1-2 との照合 |
|---|---|---|---|
| `IrodoriTtsYwk.Launcher.exe` | 1 | **69,608,415** | 設計書は 69,588,729＝**便 D（2）が再発行して 19,686 B 増えている**（本席は読むだけ） |
| `server\` | 73 | **3,946,008** | 一致 |
| `licenses\` | 13 | **155,094** | 一致 |
| `voices\`（`presets\` 11 檔を含む） | 14 | **32,622,995** | 一致 |
| `ledger\` | **6** | 221,167 | Radeon 版の間引き＝`README.md` `models.json` `python-embed.json` `vc_redist.json` `runtime-rocm-gfx1151.json` `runtime-cpu.json`（**cu130／cu126 は 1 檔も無い**） |
| `docs\` | 3 | 70,596 | `README.md` `install.md` `radeon.md` のみ＝**`acceptance.md`・`contract.md` は出ていない** |
| `unins000.exe`／`unins000.dat` | 2 | 4,442,820／69,907 | Inno が作る |

- **`__pycache__` は 0 ディレクトリ・`.pyc` は 0 檔**（導入後の樹で確認）。
- **sha256 の突合＝110 / 110 一致・不一致 0**（`unins000.*` の 2 檔を除く全檔を配布樹・リポの docs・
  発行済み exe と 1 檔ずつ突き合わせた）。⇒ 受け入れ条件 **I-3 は本席の走で満たしている**。
- HKCU のアンインストール鍵（`{ECA98712-…}_is1`）の値＝
  `DisplayName=irodori-TTS for 読み分けちゃん（Radeon 版） v0.1.0`／`DisplayVersion=0.1.0`／
  `Publisher=irodori-tts-for-yomiwakechan（非公式・Aratako 氏とは無関係＝decisions.md 1）`／
  `InstallLocation=<scratch>\ben-e1\app-radeon\`／`UninstallString="<scratch>\ben-e1\app-radeon\unins000.exe"`。
  **CUDA 版の鍵は最後まで作られていない**（`KEY_CUDA=False`）。
- スタートメニューに `irodori-TTS for 読み分けちゃん（Radeon 版）.lnk` が 1 本だけ出来た（`{autoprograms}`）。

**アンインストール**＝`unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=…`＝
**親 exit 0・1.10 s**。ログ 167 行。末尾の逐語（`uninstall-radeon.log`）＝

```
2026-09-05 08:22:15.833   Failed to delete directory (145). Will retry later.
2026-09-05 08:22:16.344   Deleting directory: …\ben-e1\app-radeon
2026-09-05 08:22:16.354   Uninstallation process succeeded.
2026-09-05 08:22:16.354   Removed all? Yes
2026-09-05 08:22:16.354   Need to restart Windows? No
2026-09-05 08:22:16.356   Defaulting to No for suppressed message box (Yes/No):
                          C:\Users\mugonkun\AppData\Local\irodori-tts-ywk に残っています。残しておくと、次に入れ直したときそのまま使えます。
2026-09-05 08:22:16.356   Defaulting to No for suppressed message box (Yes/No):
                          追加した参照 wav・話者の名前・設定は利用者の資産です。既定では残します。
2026-09-05 08:22:16.356   Log closed.
```

ここから取れた**断定**が 4 つ。

1. **2 段の問いが両方とも出て、両方とも既定の「残す」に落ちた**（M-8 の再現）。1 段目が出たのは
   もう一方の種（CUDA 版）の鍵が無かったから＝`RegKeyExists(HKEY_CURRENT_USER, OtherFlavorKey)` の
   分岐が**期待どおり False に落ちている**。※ **もう一方の種が居るときに 1 段目が消えること自体は未実射**＝
   設計書 §7 段 8（検分席＝E-3 の持ち場）。
2. **日本語の文言がそのままログに出た**＝`.iss` の UTF-8 BOM と `Japanese.isl` の組が実際に効いている。
3. **アンインストールでも `User privileges: None`／`Administrative install mode: No`／
   `Install mode root key: HKEY_CURRENT_USER`**（M-7 の再現）＝この機体・この口座では UAC が出ない。
   ※ 設計書 §5-4 ⑸ の但し書き（口座次第で出る）は**覆さない**＝1 機体 1 口座の標本 1 個。
4. `Failed to delete directory (145)`（＝`ERROR_DIR_NOT_EMPTY`）が 2 行出たあと、
   0.5 s 後の再試行で消えて `Removed all? Yes` に至る（M-7 と同型）。

**消えたことの確認**（**断定**）＝アンインストール鍵 `{ECA98712-…}_is1` **無し**・
`{app}`（`…\ben-e1\app-radeon`）**無し**・スタートメニューの `.lnk` **無し**。

**データ樹に触っていないことの確認**（**断定**）＝`%LOCALAPPDATA%\irodori-tts-ywk`（`miopen`・`models`・`voices`）は
導入前・導入後・アンインストール後の 3 時点で **28 檔・3,571,678,193 B のまま 1 バイトも動いていない**。
`[Code]` 末尾の `RemoveDir(Data)` は中身が在るので何もしていない（設計どおり）。

### 11-5 設計書 §2 からの差分（**3 件**）と理由

| # | §2 の骨 | 置いた檔 | 理由 |
|---|---|---|---|
| ⒜ | `Excludes: "__pycache__\*,*.pyc"` | **`Excludes: "__pycache__,*.pyc"`** | **実射で確かめた**＝`__pycache__\*` は「Source の根からの相対パス」に当たるので**入れ子の `__pycache__` に当たらず**、`createallsubdirs` が**空の `__pycache__` を掘る**。使い捨ての樹（`a\y.py`＋`a\__pycache__\x.pyc`＋`__pycache__\z.pyc`）を 2 通りの `Excludes` で包んで無人導入した実測＝`__pycache__\*` 版の導入後は `__pycache__` と `a\__pycache__` の**空 2 つが残る**、`__pycache__` 版は `a\y.py` **1 檔だけ**。名前で引く形（区切りを含めない）ならディレクトリごと落ちる。`.pyc` は `*.pyc` が全階層で捕まえるので**檔は 2 通りとも 0 件**＝差は「空の器」だけだが、`[UninstallDelete]` の `dirifempty {app}` を空振りさせる芽なので潰した |
| ⒝ | `if GetSpaceOnDisk64(DataDir(), FreeBytes, TotalBytes)` | **`GetSpaceOnDisk64(ExpandConstant('{localappdata}'), …)`**（文言に出す路は `DataDir()` のまま） | **実射で確かめた**＝`GetSpaceOnDisk64` は**まだ無いディレクトリに `False` を返す**。使い捨ての `.iss`（`InitializeSetup` で撃って `False` を返し何も導入しない形）の実測＝`…\Local`→`True free=1159695134720`、`…\Local\irodori-tts-ywk`（在る）→`True`、`…\Local\irodori-tts-ywk-does-not-exist-e1probe`→**`False`**。§2 のままだと**まっさらな機体＝いちばん警告が要る機体で告知が黙って消える**。ドライブは同じなので数字は変わらない |
| ⒞ | `#if FileExists(SrcApp + "\ledger\runtime-rocm-gfx1151.json") && false` の中に `;` 注釈を置く形 | **素の `;` 注釈 1 行**にした。あわせて **compile 時の門を 2 本足した**（CUDA 版の `runtime-cu126.json`・Radeon 版の `{#Repo}\docs\radeon.md`） | 常に偽の `#if` は死んだ枝で、読む側に「ここで何か見ている」と誤読させる。種の混入を見るのは設計書 §3-2 の**門 A-3（`installer-build.ps1`）**であって `.iss` ではない、と注釈で名指しした。足した 2 本は「`[Files]` に名指しで書いてある檔が消えたら ISCC が自分で止まる」ための門で、§2 の 4 本＋種別 1 本と同じ型 |

**あわせて 1 件の書き足し**（差分ではなく作法）＝`[Code]` の `{ … }` 注釈に**波括弧の定数を書かない**。
Pascal の注釈は入れ子にならないので `{ … {commonpf} … }` と書くと注釈が `{commonpf}` の `}` で閉じ、
残りが式として読まれてコンパイルが落ちる。§2 の骨には無い落とし穴だが、§2 の文章を注釈に写すと踏む。

**採らなかった指示 1 件**＝依頼文は `[UninstallRun]` を挙げていたが、**設計書 §1-6 の
「`[UninstallRun]` は 1 行も書かない」に従った**（走らせる外部プログラムが無く、`installer/README.md:29`
「bat を使わない」に触れる経路を作らない）。消す仕事は `[UninstallDelete]` 2 行と `[Code]` が持つ。

### 11-6 卓への票（4 件）

1. **門 B-2 のサイズ帯を 77〜85 MiB に締めてよい**（§3-3 の暫定 75〜95 MiB）。実測＝cuda 84,984,160 B・
   radeon 84,998,686 B の 2 標本。ただし**本番の帯は `installer-build.ps1` が出した 2 本で確定すべき**
   （本席の 2 本は `/D` を手で渡した走で、`assemble-app`／`release-build` を回していない）。
2. **設計書 §5-4 ⑴ の「`build/out/app` に `__pycache__` は 0 件」は本席の時点で古い**＝
   `__pycache__` 3 つ・`.pyc` 21 檔（541,008 B）が生えていた。`[Files]` の `Excludes` が受け止めたので
   配布物には 1 檔も出ていないが、**門 A-4（拡張子の白名簿）に `.pyc` が載っていないと配布樹の走査が
   exit 1 になる**。生産ライン席（E-2）へ＝門 A-4 は `__pycache__` を**走査から除く**か、
   `assemble-app` の出力を掃除する側に寄せるかを決める要がある（本席は `build/` に触れないので提起のみ）。
3. **設計書 §1-2 のランチャ exe のバイトが 69,588,729 → 69,608,415 に動いた**（便 D（2）の再発行）。
   §1-2 の表と「106,699,889 B（101.76 MiB）」は**便 D（2）の着地後に測り直す**のがよい。
   本席の実測＝`{app}` は **111,137,002 B（106.0 MiB・アンインストーラ込み）**。
4. **`AppId` の GUID は実射で「永久の値」になった**＝この機体の HKCU に
   `{ECA98712-1574-4D2A-A1FE-0FF5347BF185}_is1` が実際に作られ、消えた。裁定 90 の記帳のとおりで齟齬なし。

### 11-7 まだ撃っていない（＝この席の外）

段 2（UI の 3 頁）・段 3（通知）・段 5（`AppMutex`）・段 6（`[InstallDelete]` の種跨ぎ）・
段 7・8（アンインストール 2 通り・もう一方の種が居るときの 1 段目）＝**検分席（E-3）の持ち場**。
`build/installer-build.ps1` は**生産ライン席（E-2）**＝便 D（2）の着地後。

---

## 11-8 是正席（便 E（1）是正）の記帳（2026-09-05 08:5x〜09:0x・Radeon 機・実装サブ席 opus）

> 敵対検分の **medium 6 件・low 4 件**を受けた是正。書いた檔は `installer/irodori-tts-ywk.iss` **1 檔**と本節だけ。
> `launcher/`・`server/`・`build/`・`probe/`・`docs/acceptance.md`・`docs/design/ben-d-launcher.md`・
> `docs/install.md` は**読むだけ**。`git commit` していない。外部へ出ていない。
> **`%LOCALAPPDATA%\Programs\` に本導入していない**（試しの導入はすべて `/DIR=` でスクラッチパッドの下・
> 終わったら `/VERYSILENT` で畳み、レジストリの `Uninstall` 鍵が消えたことを確かめた）。
> **ランチャは 1 度も起動していない**（`[Run]` は `skipifsilent`）。

### 11-8-1 檔の形（**断定**・実測）

| | 台本席（§11-1） | 是正後 |
|---|---|---|
| `installer/irodori-tts-ywk.iss` | 15,283 B・283 行 | **26,955 B・421 行** |
| 形 | UTF-8 BOM 付き・CRLF 282 対・単独 LF 0 | **UTF-8 BOM 付き（`EF BB BF`）・CRLF 421 対・単独 LF 0** |

### 11-8-2 ISCC の挙動を実射で確かめ直した（**旧注釈は誤りだった**）

使い捨ての `.iss` 3 檔（`scratchpad/ben-e1/xt/`）を ISCC 6.7.3 で撃った逐語。

| # | 形 | 逐語 | 終了コード |
|---|---|---|---|
| ⑴ | 空ディレクトリへの wildcard | `Error on line 9 in …\wild.iss: No files found matching "…\src\empty\*"` ／ `Compile aborted.` | **2** |
| ⑵ | 名指しの Source が無い | `Error on line 10 in …\named.iss: Source file "…\src\does-not-exist.json" does not exist.` ／ `Compile aborted.` | **2** |
| ⑶ | wildcard が**一部だけ**当たる（`voices\` から `presets\` の枝ごと落とした樹） | `   Compressing: …\voices\presets.json` ／ `   Compressing: …\voices\voices.json` ／ `Successful compile (0.688 sec). Resulting Setup program filename is:` | **0**（警告 0・**wav 0 檔のまま setup が出た**） |

⇒ **`.iss:44-45` の旧注釈**「Inno は『Source が 0 件』を既定では止めない＝EXIT=0・Successful compile のまま
中身の欠けた setup が出る（設計書 §3-2 の実測＝65,021,007 B）」は **⑴⑵ で反証された**。
正しい言い方は「**wildcard が 1 檔も当たらなければ ISCC は exit 2 で止まる。止まらないのは wildcard が
一部だけ当たる場合**」である。**設計書 §3-2 冒頭と §8 危険 3 の同じ主張も訂正が要る**（→ §11-8-6 の卓へ ⑴）。

### 11-8-3 直した 6 件（medium）と 3 件（low）

| # | 何を直したか | 実射の裏 |
|---|---|---|
| M-1 | **`voices\presets\*.wav` を数える compile 時の門を新設**（ISPP の `FindFirst`／`FindNext`／`FindClose`）。11 でなければ `#pragma message` で数を出してから `#error`。11 の出所＝`decisions.md` 37 の逐語「自作の生成物・配布物の同梱資産（**11 本**・32 MB）」／裁定 88 ⑸ | **わざと壊した樹 2 通りで実射**＝⒜ `presets\` 丸ごと無し→`Line 89: voices\presets\*.wav count = 0 (expected 11)`／`Error on line 90 in …\irodori-tts-ywk.iss: voices/presets/*.wav is not 11 files (see the message line above for the count found)`／`Compile aborted.`／**exit 2** ⒝ 11 檔のうち 10 檔→`count = 10 (expected 11)`／**exit 2**。正しい樹では `count` が 11 で `#pragma message` すら出ない |
| M-2 | **`.iss:44-45` の注釈を実測に書き直した**。あわせて 8 本の門を「⑶ の門（wildcard が一部だけ当たる＝**ここに要る門**）」と「保険（`[Files]` に名指しで書いてあるので ⑵ で ISCC が自分で止まる）」に**名札で分けた** | §11-8-2 の ⑴⑵⑶ |
| M-3 | **`[Messages]` 4 本と `[Code]` の `PeakDiskBytes` を `#if Flavor` で二重化**。Radeon 版＝取得 4.79 GiB・要る空き 9.55 GiB（`PeakDiskBytes = 10252286678`）。CUDA 版＝既定 cu130 で 5.26 GiB・cu126 で 5.93 GiB、要る空きは**その版が選ばせる最大＝cu126 の 14.45 GiB**（`PeakDiskBytes = 15515873265`。cu130 の 11.56 GiB＝12,410,119,910 も本文に併記） | 台帳から式を踏み直した**実測**（下表）。Int64 で桁落ちしないことを使い捨ての `.iss` で実射＝`PeakRadeon=10252286678 GiB=9`／`PeakCuda=15515873265 GiB=14`／`free=1159160840192 total=2047236632576` |
| M-4 | **場所の門にデータ樹を足した**（両向き＝`StartsWithDir(Dir, DataDir()) or StartsWithDir(DataDir(), Dir)`）。設計書 §1-1「インストーラはデータ樹に 1 檔も作らず、更新でも 1 檔も触らない」を機械で守る 1 本 | 使い捨ての `.iss` で門の式だけを 8 通りに撃った逐語（下表） |
| M-5 | **アンインストール 2 段目**は抑止せず、もう一方の種が居るときだけ本文に 1 行足す形にした（敵対検分の ⑵ を採用）。⑴＝2 段目も抑止する案を採らなかった理由＝`docs/install.md` §5-4 の 3 が「別に尋ねる」と**例外なしで**約束しており、抑止すると利用者が話者を消す唯一の口を失う | **2 種を同居させて実射**（下の 11-8-5） |
| M-6 | junction 利用者の挙動は**実装を変えず**、`[Code]` に**実装が本当にしていること**を CHM 逐語つきで書いた＝「`DelTree` の reparse point 免除が当たるのは `Data` 自身であって `Data\runtime` ではない。ここは `Data` の**下**を名指しで消すので junction を通り抜けて実体が消える」。**`docs/install.md` §5-3 の但し書きのほうが実装と食い違う**＝卓へ（→ §11-8-6 ⑶） | 未実射（機体の D: は停止域）＝**推測のまま**。ただし CHM 逐語と `.iss` の行の突合は断定 |
| L-1 | **空きの告知が更新導入で沈黙する穴**を塞いだ＝`NotifyFreeSpace` を切り出し、`NextButtonClick(wpSelectDir)` と **`PrepareToInstall`** の両方から呼ぶ（1 周に 1 回だけ＝`SpaceNotified`）。**無人（`WizardSilent`）では出さない**＝`/VERYSILENT` の走を MsgBox で止めない | 無人導入が **exit 0・2.85 s** で通った（`PrepareToInstall` が例外を投げていない）。UI での発火は**未実射**＝この機体の空きが 1,159,160,840,192 B（1.05 TiB）で閾値に掛からない |
| L-2 | **手作りの `PeakDiskBytes = 12413511598` を廃した**。台帳から導いた値に差し替え、導き方（`FetchPlanner.cs:54,57-59,65` の式）を注釈に書いた | 下表 |
| L-4 | **`DefaultGroupName` を消した**（`[Icons]` が `{group}` を 1 度も使わず `{autoprograms}` を直に指すので誰も読まない）。`DisableProgramGroupPage=yes` は**残した**＝これは効いている（消すと「スタートメニューフォルダーを選ぶ」頁が出て、実射した 3 頁が 4 頁になる）。その旨を注釈に書いた | 2 版とも**警告 0・exit 0** で通り、スタートメニューに `.lnk` が 1 本だけ出来て消えた |

**台帳から踏み直した「取得中に要る空き」（本席の実測・`build/out/app/ledger/*.json` の `size` の総和）**

| 変種 | 落とすバイト | 要る空き（`EstimatedPeakDiskBytes`） | GiB | `ledger/README.md`:297-300 |
|---|---|---|---|---|
| `cpu` | 3,891,000,522 | **4,862,463,481** | 4.5285 | 4.53 GiB ✓ |
| `cu130` | 5,646,269,459 | **12,410,119,910** | 11.5578 | 11.56 GiB ✓ |
| `cu126` | 6,368,537,681 | **15,515,873,265** | 14.4503 | 14.45 GiB ✓ |
| `rocm-gfx1151` | 5,144,447,777 | **10,252,286,678** | 9.5482 | 9.55 GiB ✓ |

式＝`CacheBytes`（python-embed 11,133,606 ＋ vc_redist 25,635,768 ＋ runtime）＋
`(python-embed + runtime) * 3.3`（`FetchPlanner.ExpansionFactor`）＋ `ModelBytes` 3,570,982,039。
**旧値 12,413,511,598 は cu130 の実値と 3,391,688 B ずれる**（このリポのどこからも導けない数字だった）。

**場所の門の実射（使い捨ての `.iss`・`InitializeSetup` で門の式だけを撃って何も導入しない形）**

```
DataDir=C:\Users\mugonkun\AppData\Local\irodori-tts-ywk
REFUSED (data tree)   <-  C:\Users\mugonkun\AppData\Local\irodori-tts-ywk
REFUSED (data tree)   <-  C:\Users\mugonkun\AppData\Local\irodori-tts-ywk\app
REFUSED (data tree)   <-  C:\Users\mugonkun\AppData\Local
ALLOWED               <-  C:\Users\mugonkun\AppData\Local\Programs\irodori-tts-ywk
ALLOWED               <-  C:\Users\mugonkun\AppData\Local\Programs\irodori-tts-ywk-radeon
ALLOWED               <-  C:\Users\mugonkun\AppData\Local\irodori-tts-ywk-radeon
REFUSED (admin place) <-  C:\Program Files (x86)\irodori-tts-ywk
ALLOWED               <-  C:\ywk
```

⇒ **両向き**（導入先がデータ樹の中／データ樹が導入先の中）を捕まえ、**似た名前の枝（`…-radeon`）は誤爆しない**。
2 種の既定の導入先はどちらも通る。※ **UI で実際に断られる走は未実射**＝設計書 §7 段 2 の持ち場（検分席 E-3）。

### 11-8-4 退けた 1 件（**rejected**・根拠つき）

**「`[Files]` の `voices\*` から `voices.json`・`voices.ywk.json` を `Excludes` で除く」（medium 4 の後半・low 3 の前半）は退けた。**

逐語＝`launcher/IrodoriTtsYwk.Launcher/Services/Voices/PresetVoices.cs:53-54`

```
        var fromTable = FromTable(paths.PresetsJsonPath, appVoices)
            ?? FromTable(Path.Combine(appVoices, "voices.ywk.json"), appVoices);
```

同 `:61-62`

```
        var scanned = FromDirectory(paths.PresetVoicesDir);
        return scanned.Count > 0 ? scanned : FromDirectory(appVoices);
```

（`appVoices` は同 `:42` の逐語 `var appVoices = Path.Combine(paths.AppDir, "voices");`＝**配布樹側の `voices\`**。）
⇒ **`{app}\voices\voices.ywk.json` は死に檔ではない**。`presets.json` が 0 件のときの落ち先として**実際に読まれる**。
除くと落ち先を 1 枚失う。所見の「誰も読まない死に檔」は `voices.ywk.json` については**偽**である。

`{app}\voices\voices.json`（80 B）だけは読み手が見当たらない（`AppDir` 側の `voices.json` を引くコードは grep で 0 件。
`paths.VoicesJsonPath` はすべて `DataDir` 側＝`AppPaths.cs:104`）。それでも残した理由は 2 つ＝
⑴ **衝突の的そのものは `[Code]` の門（M-4）が消した**（`{app}` をデータ樹に重ねる経路が断たれた）
⑵ 除くと配布檔数が **110 → 109** に動き、受け入れ条件 I-3（sha256 突合）と設計書 §3-2 の門 A-1（檔数）の
期待値を巻き添えにする。**費用が便益を上回る。**
※ 所見の後半「`docs/install.md` のアプリ樹の表に 2 檔が載っていない」は**そのとおり**＝卓へ（→ §11-8-6 ⑷）。

### 11-8-5 ISCC と試しの導入の逐語（**断定**・2 版・警告 0・exit 0）

道具＝`C:\Users\mugonkun\AppData\Local\Programs\Inno Setup 6\ISCC.exe`。逐語＝`Compiler engine version: Inno Setup 6.7.3`。
`/D` は 7 本を手で渡した（`AppVersion=v0.1.0`・`AppVersionNumeric=0.1.0`）。`/O` はスクラッチパッド。

| 版 | 逐語 | 終了コード | `Compressing:` 行 | バイト | sha256 |
|---|---|---|---|---|---|
| cuda | `Successful compile (11.859 sec).` | **0** | **110** | **84,984,627**（81.05 MiB） | `423bf8f4e38aafa3ddffec904e69de84d6ec010797fdec04d11bffed140bea77` |
| radeon | `Successful compile (12.000 sec).` | **0** | **110** | **84,999,119**（81.06 MiB） | `a9767fdd91d8810bc1763b4266460cf59564b4a29ae5f127ea9d06cd3cc49399` |

- **警告は 2 版とも 0 行**（`warning`／`error` を大小無視で引いて 0 件）。**檔数は台本席の 110 から動いていない**
  （`Excludes` を足していないので当然）。バイトは台本席の 84,984,160／84,998,686 から **+467／+433 B**＝`.iss` が
  太った分（注釈と `[Code]`）。**門 B-2 の帯 77〜85 MiB の中**。

**試しの導入 1 周（Radeon 版・スクラッチパッドの下）**

- 導入＝`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=<scratch>\ben-e1\app-final /LOG=…`＝**exit 0・2.85 s**。
  ログ逐語＝`Setup version: Inno Setup version 6.7.3`／`Windows version: 10.0.26200`／
  **`User privileges: None`**／**`Administrative install mode: No`**／**`Install mode root key: HKEY_CURRENT_USER`**／
  `64-bit install mode: Yes`／`Creating new uninstall key: HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\{ECA98712-1574-4D2A-A1FE-0FF5347BF185}_is1`／`Installation process succeeded.`
- `{app}` の実測＝**112 檔・22 ディレクトリ・111,138,146 B**。**sha256 の突合＝110 / 110 一致・不一致 0**
  （`unins000.*` の 2 檔を除く全檔を配布樹・リポの docs・発行済み exe と 1 檔ずつ）。
  `ledger\` は 6 檔（`runtime-cu130`／`runtime-cu126` は 1 檔も無い）・`docs\` は 3 檔（`acceptance.md`・`contract.md` は出ていない）・
  `voices\presets\*.wav` は **11 檔**・`__pycache__` 0・`.pyc` 0。
- アンインストール＝`unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=…`＝**exit 0・0.98 s**。
  逐語＝`Uninstallation process succeeded.`／`Removed all? Yes`／`Need to restart Windows? No`／
  `Defaulting to No for suppressed message box (Yes/No):` **2 本**（1 段目・2 段目とも既定の「残す」に落ちた）。
- **消えたことの確認**＝`{ECA98712-…}_is1` **無し**・`{F228543A-…}_is1` **無し**・`{app}` **無し**・`.lnk` **無し**・
  `%LOCALAPPDATA%\Programs\irodori*` **0 件**・HKCU の `Uninstall` に `DisplayName` が `*irodori*` の鍵 **0 件**。
- **データ樹に触っていないことの確認**＝`%LOCALAPPDATA%\irodori-tts-ywk` は導入前・導入後・アンインストール後の
  3 時点で **28 檔・3,571,678,193 B のまま 1 バイトも動いていない**。

**2 種を同居させた走（設計書 §7 段 8 の一部を実射で埋めた）**

CUDA 版と Radeon 版を**両方**スクラッチパッドへ入れた（両方 exit 0・`KEY_CUDA=True`・`KEY_RADEON=True`）あと、
**Radeon 版だけ**を `/VERYSILENT /SUPPRESSMSGBOXES` で畳んだログの末尾の逐語＝

```
2026-09-05 09:02:05.756   Removed all? Yes
2026-09-05 09:02:05.756   Need to restart Windows? No
2026-09-05 09:02:05.757   Defaulting to No for suppressed message box (Yes/No):
                          追加した参照 wav・話者の名前・設定は利用者の資産です。既定では残します。
                          CUDA 版がまだこの機体に入っています。話者と設定は 2 つの版が同じ場所（C:\Users\mugonkun\AppData\Local\irodori-tts-ywk）を共有しているので、ここで消すとそちらからも消えます。
2026-09-05 09:02:05.758   Log closed.
```

ここから取れた**断定**が 3 つ。

1. **`Defaulting to No` が 1 本しか出ていない**＝**1 段目（取得物）が出ていない**。
   `RegKeyExists(HKEY_CURRENT_USER, OtherFlavorKey)` の抑止が**もう一方の種が居るときに実際に効く**ことを
   初めて実射で確かめた（台本席は「未実射」と記帳していた＝設計書 §11-4 の 1）。
2. **2 段目には新しい 1 行がそのまま出た**＝M-5 の手当てが効いている。
3. そのあと CUDA 版を畳んだ走では `Defaulting to No` が **2 本**に戻った（Radeon 版の鍵が消えているので 1 段目が復活）。
   最後に鍵・`{app}`・`.lnk` がすべて消え、データ樹は **28 檔・3,571,678,193 B のまま**。

### 11-8-6 卓へ（**本席の停止域の外＝檔の訂正が要る 4 件**）

1. **設計書 §3-2 冒頭と §8 危険 3 の「Inno は『Source が 0 件』を既定では止めない」は誤り**（§11-8-2 の ⑴⑵ で反証）。
   正しくは「wildcard が**一部だけ**当たるときに止まらない」。`.iss` 側の注釈は本席が直したが、設計書は書けない。
   ついでに §3-2 の門 A-2 は「`voices\presets\*.wav` が 11 檔」を見る玉で、**`.iss` にも同じ玉を張った**（M-1）＝
   生産ライン席（E-2）は門 A-2 を「二重の網の 2 枚目」として書けばよい。
2. **設計書 §6-3 ⑵ の閾値「11.56 GiB」は種を分ける前の値**。本席は `.iss` を
   「その版が選ばせる変種の**最大**」（radeon 9.55 GiB／cuda 14.45 GiB）に据えた＝「最大 … の空きが要ります」を
   嘘にしないため。**「既定の変種で読む（radeon 9.55／cuda 11.56）」に戻す裁定なら定数 1 本の差し替えで済む**
   （`PeakDiskBytes` の CUDA 側を `12410119910` に）。
3. **`docs/install.md` §5-3 の但し書き「junction だけが消えて実体（`D:\ywk-data`）は残る」は実装と食い違う**（M-6）。
   推奨＝**檔のほうを直す**（「junction 越しでも実体ごと消える。残したいなら『残す』を選ぶこと」）。
   実装を檔に合わせる案（reparse point を判って `Data` ごと `DelTree(Data, True, False, False)`）は、
   利用者が明示で「削除する」を選んだのに何も消えない形になるので採らなかった。
4. **`docs/install.md` §1 の括弧書き「11.56 GiB を割っていたら告知する」は種別に直す要がある**（M-3 と同じ玉）。
   あわせて同 §2 のアプリ樹の表に `voices\voices.json`・`voices\voices.ywk.json` の 2 檔が載っていない
   （後者は `PresetVoices.cs:54` が読む生きた檔＝§11-8-4）。
5. **アンインストール 2 段目を「抑止する」に倒す裁定もありうる**（敵対検分 medium 5 の ⑴）。
   本席は `docs/install.md` §5-4 の 3 の約束（例外なしで「別に尋ねる」）に素直な ⑵ を採った。
   倒すなら `.iss` の `if OtherInstalled then` を 2 段目にも被せる 2 行で済む。`decisions.md` 90 は 1 段目しか触れていない。

### 11-8-7 まだ撃っていない（＝この席の外）

- **UI の走**（3 頁・場所の門が実際に断る画面・空きの告知の画面）＝設計書 §7 段 2・検分席（E-3）。
  ※ 空きの告知はこの機体では**閾値に掛からない**（空き 1,159,160,840,192 B）＝空きを絞った機体か、
  定数を一時的に上げた使い捨ての版でしか撃てない。
- **更新導入で `PrepareToInstall` 経由の告知が出ること**（L-1）＝同上。無人では設計どおり**出さない**。
- **junction を張っての走**（M-6）＝機体の D: が停止域。
- 段 3（通知）・段 5（`AppMutex`）・段 6（`[InstallDelete]` の種跨ぎ）・段 9（日本語＋空白のユーザ名）＝E-3。
- `build/installer-build.ps1`（門 A 6 本・門 B 3 本）＝生産ライン席（E-2）・便 D（2）の着地後。

---

## 12. 生産ライン席（便 E（2））の記帳（2026-09-05 09:4x〜09:5x・Radeon 機・実装サブ席 opus）

> 本節は**この席が実際に撃った物だけ**を書く。読んだだけ・推したものは「推測」と明記する。
> 書いた檔は `build/installer-build.ps1`（新規）・`build/assemble-app.ps1`（1 点）・`docs/install.md`（§5-3 の 1 段落）・本節の 4 つだけ。
> `launcher/`・`server/`・`probe/`・`installer/`・`upstream/`・`docs/acceptance.md` は**読むだけ**。
> `git commit` していない。外部へ 1 バイトも取りに行っていない。
> **`%LOCALAPPDATA%\Programs\` に導入していない**（本席は 1 度も setup.exe を実行していない＝検分席 E-3 の持ち場）。
> **利用者データ樹 `%LOCALAPPDATA%\irodori-tts-ywk` は 28 檔・3,571,678,193 B のまま**（本節の全作業の前後で実測・1 バイトも動いていない）。
> 削除の走を 1 度も撃っていないので退避も不要だった（`build/out/app` の退避は §12-5 で実施）。

### 12-1 置いた檔（**断定**・実測）

| 檔 | バイト | 行 | 形 |
|---|---|---|---|
| `build/installer-build.ps1`（新規） | **36,457** | 800 | **ASCII のみ（非 ASCII 0 バイト）・CRLF 800 対・単独 LF 0** |
| `build/assemble-app.ps1`（1 点の追記） | 24,726（+1,332） | 484 | 同上（ASCII・CRLF・単独 LF 0） |
| `docs/install.md`（§5-3 の 1 段落） | 20,074（+644） | 230 | UTF-8 BOM 無し・LF（元の形のまま） |

`build/installer-build.ps1` は `release-build.ps1:3-4` の作法（ASCII only／CRLF／PS 5.1 と 7 の両対応／
三項演算子・null 合体・`&&` を使わない）にそのまま従い、`build/Common.ps1` を dot-source して
`Get-YwkRepoRoot`・`Start-YwkLog`／`Write-YwkLog`・`Get-YwkSha256`・`New-YwkDirectory`・
`Invoke-YwkNative`・`Write-YwkTextFile`／`Write-YwkJsonFile` を使う。日本語は 1 文字も置いていない。

### 12-2 門の一覧（**9 本**＝設計書 §3-2・§3-3 の実装）

`-Flavor cuda|radeon`（`-All` で 2 本）。**終了コード＝失敗した門の数**（`-All` では 2 版の合計）。
道具が無い（ISCC 不在・配布樹が無い）のは門ではなく `throw`（exit 1）＝「壊れた作業場」であって「壊れた成果物」ではない。

| 門 | 何を見るか | 逐語（正常時・cuda） |
|---|---|---|
| **A-1** | 配布樹の檔数と総バイトを記録。記録値から外れたら **WARN**、**0 件なら NG** | `108 file(s), 37111715 B (expected 108 / 37111715)` |
| **A-2** | `voices\presets\*.wav` が **11 檔**・`voices\presets.json` が在って空でない | `voices\presets\*.wav = 11 (expected 11), voices\presets.json present = True` |
| **A-3** | `licenses\first-run-notices.md`（裁定 46）＋**種別の台帳の組が揃う**＋**流儀の知らない `runtime-*.json` が 1 檔も無い** | `ships README.md models.json python-embed.json vc_redist.json runtime-cu130.json runtime-cu126.json runtime-cpu.json ; must not ship runtime-rocm-gfx1151.json` |
| **A-4** | **拡張子の白名簿**で配布樹を走査（`__pycache__`／`.pyc` は走査から除く＝裁定） | `scanned 108 file(s) against 10 extension(s) + 6 named file(s); 0 outside` |
| **A-5** | **1 檔 4 MiB 上限**。例外は**名指し**＝`voices\presets\*.wav` 11 檔（裁定 37）とランチャ exe（樹の外・裁定 51） | `cap 4194304 B; largest non-exempt = server\upstream\Irodori-TTS\uv.lock 1532654 B; 11 named exemption(s)` |
| **A-6** | 発行済み exe の版が `AppDisplayVersion` と一致 | `ProductVersion = v0.1.0 (want v0.1.0), FileVersion = 0.1.0.0, 69610930 B` |
| **B-1** | ISCC が exit 0・setup が在る・**compile ログの `Compressing:` 行が「混入 0」を実証**（もう一方の種の台帳・`__pycache__`／`.pyc`・`acceptance.md`／`contract.md` が 1 行も無く、この版の台帳が全部在る） | `exit 0, 110 file(s) compressed in 11.89 s, ledger = models.json python-embed.json README.md runtime-cpu.json runtime-cu126.json runtime-cu130.json vc_redist.json, 0 warning(s)` |
| **B-2** | setup のバイトが帯の中 | `= 84986386 B (81.05 MiB); band 80740352-89128960 B (77.0-85.0 MiB)` |
| **B-3** | sha256 を `SHA256SUMS.txt` に 1 行（`<sha256>  <檔名>`・LF・名前で置換） | `SHA256SUMS.txt has exactly 1 line for this setup` |

**白名簿の中身（実測から）**＝拡張子 10 種 `.py .yaml .json .md .txt .toml .lock .template .example .wav`
＋名指し 6 檔 `LICENSE` `Dockerfile` `.gitignore` `.gitkeep` `.python-version` `.dockerignore`。
（`.env.example` は `.example` で受ける。`release-build.ps1:187-191` の `$forbidden` は**流用していない**＝
`'*.wav'` と `'voices.json'` を禁じるが配布樹には正当に入るため＝設計書 §3-2 のとおり。）

### 12-3 2 版の実測（**断定**・`installer-build.ps1` が出した本番の 2 本）

道具＝`C:\Users\mugonkun\AppData\Local\Programs\Inno Setup 6\ISCC.exe`（`Inno Setup 6 Command-Line Compiler`）。
`/D` は **7 本**（`AppVersion=v0.1.0`・`AppVersionNumeric=0.1.0`・`Flavor`・`SrcApp`・`SrcExe`・`Repo`・`OutDir`）＋`/O`。
出力＝`build/out/installer/`。

| 版 | 檔名 | バイト | MiB | sha256 | ISCC 逐語 | `Compressing:` | 警告 |
|---|---|---|---|---|---|---|---|
| cuda | `irodori-tts-ywk-setup-v0.1.0-cuda.exe` | **84,986,386** | 81.05 | `0aadd31e9ef425542c5ddfd4507ae477ecc2aa2229d87581883868c546ea83bd` | `Successful compile (11.828 sec).` | **110** | **0** |
| radeon | `irodori-tts-ywk-setup-v0.1.0-radeon.exe` | **85,000,879** | 81.06 | `f122ef7b4ef1a60a24a30c327b00ea6069d184453955527d4e6bc5a2af74b576` | `Successful compile (11.984 sec).` | **110** | **0** |

**所要**（PS 5.1・`-All -SkipAssemble -SkipPublish`）＝**24 s**（09:50:36→09:51:00）。
**全段を回した走**（PS 5.1・`-All`）＝**35 s**（09:43:20→09:43:55）＝assemble-app **1.25 s**・
release-build -SkipZip **8.87 s**・ISCC cuda **11.968 s**・ISCC radeon **11.890 s**。

**入力の実測**＝配布樹 **108 檔・37,111,715 B**（設計書 §1-2 の 37,111,160 B から動いた＝便 D（2）が
`ledger/README.md` §7 を書き換えたため＝裁定 92 ⑽。**檔数は 108 のまま**）／
ランチャ exe **69,610,930 B**（`ProductVersion=v0.1.0`・`FileVersion=0.1.0.0`）。

**台帳の突合（B-1 の逐語・compile ログから）**

| 版 | `{app}\ledger` に入った檔 | 入らなかった檔 | `{app}\docs` |
|---|---|---|---|
| cuda | `README.md` `models.json` `python-embed.json` `vc_redist.json` `runtime-cu130.json` `runtime-cu126.json` `runtime-cpu.json`（7 檔） | **`runtime-rocm-gfx1151.json` 0 件** | `README.md` `install.md`（2 檔） |
| radeon | 同前 4 檔＋`runtime-rocm-gfx1151.json` `runtime-cpu.json`（6 檔） | **`runtime-cu130.json`／`runtime-cu126.json` 0 件** | ＋`radeon.md`（3 檔） |

`acceptance.md`・`contract.md` は 2 版とも **0 行**。`__pycache__`／`.pyc` も 2 版とも **0 行**。

**あわせて取れた断定 1 つ＝Inno の setup は同じ入力に対してバイト再現する。**
`install.md` を直す前の入力で **4 走**（PS 5.1 で 2 回・PS 7 で 2 回）撃って、
cuda は毎回 `84,986,197 B` / `6bd5a1add128c706de832e58d8176be58bcae820c346212f39ee728f948e292e`、
radeon は毎回 `85,000,689 B` / `fd50e6dafa748423847291ae0fad1caea217b5a75078d964abaaa21793efcab1` で
**1 バイトも動かなかった**。`install.md` を 644 B 増やした後の走だけが +189／+190 B 動いて上表の値になった。
⇒ 署名が無い（設計書 §8 危険 7）以上、**利用者に配れるのは sha256 だけ**だが、その sha256 は
「同じ檔から誰が撃っても同じ」＝突合に意味がある。※ 別機体・別版の ISCC での再現は**未実射**＝推測。

### 12-4 サイズ帯を実測で確定した（設計書 §3-3 B-2・§11-6 の 1 への回答）

**帯＝80,740,352〜89,128,960 B（77.0〜85.0 MiB）に確定**して `installer-build.ps1` の
`$SetupMinBytes`／`$SetupMaxBytes` に書いた。根拠＝**4 標本**。

| 標本 | cuda | radeon |
|---|---|---|
| 台本席（`/D` を手渡し・§11-3） | 84,984,160 | 84,998,686 |
| 是正席（同・§11-8-5） | 84,984,627 | 84,999,119 |
| 本席・生産ライン（`install.md` 直し前） | 84,986,197 | 85,000,689 |
| 本席・生産ライン（**最終**） | **84,986,386** | **85,000,879** |

4 標本の平均 84,993,468 B の ±5 % ＝ 80,743,795〜89,243,141 B。これを MiB の丸めで受けたのが上の帯で、
**暫定帯 75〜95 MiB は締めた**（§11-6 の 1 の提案どおり。ただし提案は「本番の走で確定すべき」と条件を付けていたので、
本席の 2 本＝`assemble-app`／`release-build` を通した走で確定した）。
帯の下限の意味＝**空 setup（台本席の実測 2,096,479 B）を配らない最後の網**。

### 12-5 I-2 の逐語（**わざと壊した樹で門が落ちる**・6 通り）

**壊す前に `build/out/app` を丸ごと退避した**（`Rename-Item` → `build/out/app.e2-backup`・その写しを働かせた）。
退避＝**108 檔・37,111,715 B**、戻した後＝**108 檔・37,111,715 B**（1 バイトも違わない）。
撃ち方＝`powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\installer-build.ps1 -Flavor <種> -SkipAssemble -SkipPublish`。

| # | 壊し方 | 落ちた門 | 逐語 | 終了コード |
|---|---|---|---|---|
| ⒜ | プリセット 1 檔（`vr2_yoshida.wav`）を退ける | **A-2** | `NG   cuda A-2 preset speakers (decisions.md 37 / 88 (5)) -- voices\presets\*.wav = 10 (expected 11), voices\presets.json present = True` | **1** |
| ⒝1 | rocm 台帳を混ぜる（`runtime-rocm-gfx1200.json` を置く） | **A-3** | `NG   cuda A-3 ... ledger/runtime-rocm-gfx1200.json is a runtime ledger the flavour rule does not know; wire it into installer/irodori-tts-ywk.iss and into $LedgerKnownRuntimes before it can ship` | **1** |
| ⒝2 | cu130／cu126 を抜いた樹（＝Radeon の樹）を cuda として包む | **A-3** | `NG   cuda A-3 ... ledger/runtime-cu130.json is missing (this is the cuda release) ; ledger/runtime-cu126.json is missing (this is the cuda release)` | **1** |
| ⒞ | 4 MiB 超の檔（`voices\oversize-sample.wav` 5,242,880 B・拡張子は白名簿の中） | **A-5** | `NG   radeon A-5 ... 1 file(s) over 4194304 B: voices\oversize-sample.wav = 5242880 B` | **1** |
| ⒟ | 第三者バイナリの形（`server\torch_cpu.dll` 2,048 B） | **A-4** | `NG   cuda A-4 no third party binary (extension whitelist, decisions.md 8) -- 1 file(s) are not on the whitelist: server\torch_cpu.dll (2048 B)` | **1** |
| ⒠ | ⒜と⒟を同時に | **A-2＋A-4** | `DONE with 2 failed gate(s) out of 6.` | **2** |
| ⒡ | 配布樹を空にする | **A-1＋A-2＋A-3** | `NG   cuda A-1 distributable tree is not empty -- build/out/app has no files at all: ...` | **3** |

⇒ **終了コード＝失敗した門の数**が 1／2／3 で実際に出た（**断定**）。
どの走でも `gate A failed <n> time(s) for <種>: ISCC was NOT run and no setup was written. Gate B did not run.`
が出て、**setup は 1 檔も作られず、`SHA256SUMS.txt` の当該行も消えた**（⒜ の後の実測＝
`build/out/installer` に radeon の 1 本だけが残り、`SHA256SUMS.txt` は 106 B の 1 行だけ）。

### 12-6 I-9（PS 5.1 と 7 の両方で実走）

| | parse | 実走（`-All`） | 終了コード |
|---|---|---|---|
| Windows PowerShell **5.1.26100.9168** | OK | 全段（assemble→publish→門→ISCC×2）35 s ／ `-SkipAssemble -SkipPublish` 25 s・24 s | **0** |
| PowerShell **7.6.5** | OK | `-SkipAssemble -SkipPublish` 25 s・24 s | **0** |

parse は `[System.Management.Automation.Language.Parser]::ParseFile` を両ホストで撃って **エラー 0**。
`build/assemble-app.ps1`（本席が 1 点足した檔）も両ホストで parse OK。
**子スクリプトは自分と同じホストで起こす**（`Get-YwkPowerShellHost`＝`$PSVersionTable.PSVersion.Major -ge 6` で
`pwsh.exe`／`powershell.exe` を選ぶ）ので、I-9 は「入口だけ 2 版」ではなく**線全体が 2 版**である。
2 版の出した setup の sha256 は**同一**（§12-3）。

### 12-7 `build/assemble-app.ps1` に足した 1 点（出力側の掃除）

段 4b（presets）と段 5（postflight）の間に **段 4c**を新設し、`build/out/app` の
`__pycache__` ディレクトリと `*.pyc` を消して件数・バイトを記帳する。逐語＝
`swept the output tree: 0 __pycache__ dir(s), 0 .pyc file(s), 0 bytes`。

これは `$ExcludeDirs`（**入り口**の濾し器）とは別の問いである＝`build/out/app` は**人がサーバを走らせる樹**で、
`PYTHONDONTWRITEBYTECODE` の無い走が `__pycache__` を置いていく（台本席の実測＝**3 ディレクトリ・21 檔・541,008 B**＝§11-3）。
配布物には `[Files]` の `Excludes` が入れないが、**門 A-1 の檔数と総バイトが嘘になる**うえ、
`[UninstallDelete]` の `dirifempty {app}` を空振りさせる芽になる。**§11-6 の 2（生産ライン席への票）はこれで閉じた**＝
掃除は `assemble-app`（出力側）が持ち、**門 A-4／A-1 は `__pycache__`／`.pyc` を走査から除く**（両方入れた＝二重）。

### 12-8 `docs/install.md` の直し（§11-8-6 ⑶ の票への回答）

§5-3 の但し書きを **`.iss` の `[Code]` の実装の真実**に合わせた。旧文＝
「junction だけが消えて実体（`D:\ywk-data`）は残る（Inno の `DelTree` は reparse point の中までは消さない）。実体は手で消すこと。」
＝**実装と食い違っていた**。新文の骨＝

- アンインストーラが名指しで消すのは `%LOCALAPPDATA%\irodori-tts-ywk` の**下**（`runtime\`・`models\`・`cache\`・`logs\`・`miopen\`）。
- CHM の `DelTree` の免除（reparse point の中までは消さない）が当たるのは **junction 自身だけ**なので、
  その下を名指しで消す実装は **junction を通り抜けて実体の中身を消す**。
- 話者と設定の 2 問目（`voices\`・`settings.json`）も同じ。**残したいときは既定の「残す」を選ぶ。手で消す必要は無い。**
- **junction を張った機体での実射はまだ無い**（CHM の逐語と `.iss` の行からの断定）と明記した。

是正席の推奨（「檔のほうを直す＝利用者が明示で『削除する』を選んだのだから消えるほうが素直」）をそのまま採った。
`.iss` は 1 行も触っていない。

### 12-9 設計書 §3 からの差分（**3 件**）と理由

| # | §3 の仕様 | 置いた実装 | 理由 |
|---|---|---|---|
| ⒜ | 門 A-3＝「種に応じた `ledger\runtime-*.json` が揃い、**もう一方の種の台帳が 0 件**」 | A-3 は⑴組が揃う⑵**流儀の知らない `runtime-*.json` が 0 件**の 2 つを見る。**「混入 0」の実証は B-1 が compile ログで行う** | **配布樹では成り立たない条件だった**＝`assemble-app.ps1:299-307` は `ledger/` を**丸ごと**写すので、`build/out/app/ledger` には常に 4 つの `runtime-*.json` が在る（本席が実測＝cpu・cu126・cu130・rocm-gfx1151）。「もう一方の種の台帳が 0 件」を配布樹に課すと**正常な樹で必ず落ちる**。実際に効く問いは 2 つで、⑴「この版が名指しで写す檔が在るか」は ISCC の前に、⑵「実際に何が入ったか」は ISCC の**後**（`Compressing:` 行）にしか答えられない。⑵ を B-1 に足した＝**設計の狙い（種の混入を機械で止める）は落としていない** |
| ⒝ | §3-1 の段の並び（門 A → ISCC → 門 B） | **門 A の失敗は「停止」**＝1 本でも落ちたら ISCC を撃たず、setup を作らず、`SHA256SUMS.txt` の当該行も消す | §3 は並びしか書いていないが、報告だけにすると **A-4／A-5 が落ちた樹（＝第三者バイナリが入っている樹）をそのまま包んだ setup が `build/out/installer` に残る**。`release-build.ps1:16` の家法「A failed inspection exits non-zero and **writes no zip**」と同じ形に揃えた |
| ⒞ | 門 A-6＝「exe の `VersionInfo.ProductVersion` が `$appVersion` と一致（※ **未実射**＝ProductVersion が `v0.1.0` を返すか FileVersion 側かは実装席が確かめる）」 | **`ProductVersion` で正しい**と実測で確定 | 実測＝`ProductVersion=v0.1.0`（`InformationalVersion` そのまま・**v 前置**）／`FileVersion=0.1.0.0`。よって比べる相手は `$appVersion`（v 付き）であって `$appVersionNumeric` ではない。檔の注釈にこの実測を書いた |

**あわせて 1 件の作法**＝門 B-3 の契約を「`SHA256SUMS.txt` は嘘をつかない」にした＝
B-1 か B-2 が落ちた setup は**記録せず、古い行があれば消す**。このとき B-3 自体は**通す**（落ちた門は B-1／B-2 が既に数えており、
ここで二重に数えると「行の檔が壊れている」という別の話になってしまうため）。

**採らなかった指示 0 件**（§3-4 の「やらないこと」3 つ＝上流 pin を `/D` で渡さない・`build/` の既存檔を書き換えない・
`.iss` に版や路を書き写さない、はすべて守った。※ `build/assemble-app.ps1` の 1 点だけは**卓の明示の指示**で足した）。

### 12-10 卓へ（5 件）

1. **設計書 §3-2 の門 A-3 の文言を直す要がある**＝「もう一方の種の台帳が 0 件」は**配布樹には課せない**（§12-9 ⒜）。
   正しい言い方は「⑴ この版が写す台帳が配布樹に揃う（ISCC 前）／⑵ 実際に入った台帳にもう一方の種が 0 件（ISCC 後・compile ログ）」。
2. **門 B-2 のサイズ帯は 77.0〜85.0 MiB（80,740,352〜89,128,960 B）で確定**（4 標本・§12-4）。§3-3 の暫定 75〜95 MiB を差し替えてよい。
3. **設計書 §1-2 の数字を更新する要がある**＝配布樹 **108 檔・37,111,715 B**（旧 37,111,160 B）・
   ランチャ exe **69,610,930 B**（旧 69,588,729 B）。`{app}` の合計は本席では未実測（導入していない＝E-3 の持ち場）。
4. **「setup はバイト再現する」を設計書 §8 危険 7（署名なし）の対策欄に足せる**＝4 走・2 ホストで sha256 が 1 ビットも動かなかった（§12-3）。
   ※ 別機体・別版 ISCC での再現は未実射。
5. **`docs/install.md` §1 の「11.56 GiB を割っていたら告知する」は種別に直す要がある**（§11-8-6 ⑷ の残り）。
   本席の担当 path は §5-3 だけなので**触っていない**。同 §2 のアプリ樹の表に `voices\voices.json`・`voices\voices.ywk.json` が
   載っていない件も同じ（残置）。

### 12-11 まだ撃っていない（＝この席の外）

- **setup.exe を 1 度も実行していない**＝導入・アンインストール・UI の走・`{app}` の sha256 突合（I-3〜I-8）は**検分席 E-3** の持ち場。
- **門 B-1 の「混入」枝が実際に落ちる走**＝落とすには `.iss` の `[Files]` を壊す要があり、`installer/` は本席の担当 path の外。
  **正の向き（2 版とも台帳の組がぴたり一致・もう一方の種 0 件）でしか確かめていない**（§12-3 の表）。
- **門 B-2 が実際に落ちる走**＝帯の外の setup を作る手が無い（同上）。**未実射**。
- **門 A-6 が落ちる走**＝古い exe を作る手が無い。**未実射**（正の向きは実測）。
- **PS 5.1／7 以外のホスト・別機体での再現**＝この機体 1 台だけ。

---

## 13. 検分席（便 E（2））の記帳（2026-09-05 09:40〜10:06・Radeon 機・実装サブ席 opus）

> 本節は**この席が実際に撃った物だけ**を書く。読んだだけ・推したものは「推測」と明記する。
> 書いた檔は `probe/e-install-probe.ps1`（新規）・`probe/README.md`（便 E 節）・本節の 3 つだけ。
> `probe/common.ps1` は**読むだけ**（1 行も変えていない）＝設計書 §10-1 の担当 path のとおり、
> Inno 用の 4 関数は `e-install-probe.ps1` の中に置いた。
> `launcher/`・`server/`・`build/`・`installer/`・`upstream/`・`docs/acceptance.md`・`docs/install.md` は**読むだけ**。
> `git commit` していない。外部へ 1 バイトも取りに行っていない。**管理者昇格を 1 度もしていない。**
> **`%LOCALAPPDATA%\Programs\` には本当に導入した**（設計書 §7 が求める本物の場所の検分＝`/DIR=` を渡さない）。
> **走の終わりに毎回アンインストールし、Uninstall 鍵・`{app}`・`.lnk` が 0 件であることを段として数えた。**
> **利用者データ樹 `%LOCALAPPDATA%\irodori-tts-ywk`（28 檔・3,571,678,193 B）は 1 バイトも動いていない**（§13-6）。

### 13-1 置いた檔（**断定**・実測）

| 檔 | バイト | 行 | 形 |
|---|---|---|---|
| `probe/e-install-probe.ps1`（新規） | **76,079** | **1,544** | **ASCII のみ（非 ASCII 0 バイト）・BOM 無し・CRLF 1,544 対・単独 LF 0** |

`d-launch-probe.ps1` と同形＝`probe/common.ps1` を dot-source・`Set-StrictMode -Version Latest`・
`$ErrorActionPreference='Stop'`・**exit code は失敗した段の数**・日本語は 1 文字も置かず `New-JpText` で
コードポイントから組む（`Japanese.isl` の逐語・`.iss` の逐語・ランチャの逐語の 3 系統で 11 本）。
**PS 5.1 と 7 の両方で parse し、両方で実走した**（§13-4 ⑵）＝受け入れ条件 **I-9 は満たす**。

### 13-2 撃った段（設計書 §7-2 の 9 段のうち **8 段**）

| 段 | 撃ったか | この席の結果 |
|---|---|---|
| 1 無人導入 | ✔ | exit 0・2.66〜3.06 s・ログ 612 行・`{app}` の **sha256 突合 110 / 110**（I-3） |
| 2 UI で 3 頁 | ✔ | `BM_CLICK` で 3 頁・**頁は 3 枚**・無人と**同じ樹**（I-4・E-5） |
| 3 通知 | ✔ | ウィザード 1 段目に `first-run-notices.md` の **16,509 字がそのまま**（I-5・裁定 46） |
| 4 読取専用の `{app}` | ✔（**任意段 `-IncludeAcl`・前倒しで撃った**） | 読取専用でも 1 周回る（**E-3 は満たす**）。**管理者昇格は要らなかった**（§13-7 ⑸） |
| 5 `AppMutex` | ✔ | **弾く**。逐語は §13-5 |
| 6 `[InstallDelete]` | ✔ | 種跨ぎの `runtime-rocm-gfx1151.json` と植えた 3 檔が**全部消えた**（I-6） |
| 7 無人アンインストール | ✔ | `{app}`・鍵・`.lnk` 消滅／データ樹**そのまま**／`Defaulting to No` **2 本**（E-4・I-7） |
| 8 UI アンインストール | ✔（**3 走**＝8a・8b・8c） | §13-4 ⑺ の表（E-4・I-7・§5-4 ⑶） |
| 9 日本語＋空白のユーザ名 | **撃っていない** | ローカルユーザの作成に**管理者昇格**が要る＝裁定 90 でこの席は撃てない（Q-E3 の裁定待ち） |

### 13-3 撃った setup.exe（**断定**）

**最終の走は生産ライン席（便 E（2）・§12）が `build/out/installer/` に出した本物 2 本**に対して撃った。
台本の既定の解決先がそこなので `-SetupCuda`／`-SetupRadeon` を渡していない。

| 版 | バイト | sha256（`build/out/installer/SHA256SUMS.txt` の逐語） |
|---|---|---|
| cuda | **84,986,386** | `0aadd31e9ef425542c5ddfd4507ae477ecc2aa2229d87581883868c546ea83bd` |
| radeon | **85,000,879** | `f122ef7b4ef1a60a24a30c327b00ea6069d184453955527d4e6bc5a2af74b576` |

**副産物の断定 1 つ**＝台本の作り込み中、本席は同じ `.iss` を同じ `/D` 7 本で ISCC に手渡して
スクラッチパッドに 2 本焼いた（`Successful compile (12.875 sec).`／`(11.844 sec).`・**exit 0**・
`Compressing:` **110 行**・`warning`／`error` **0 行**）。その 2 本の sha256 が
**生産ライン席が別に焼いた 2 本と 1 ビットも違わなかった**。⇒ **この `.iss` に対する ISCC 6.7.3 の出力は
（同じ入力樹なら）バイト再現する。** ※ 標本は 1 機体・同日・2 対＝**一般化はしない**。

**もう 1 つの断定＝I-3 は生きた網である。**作り込みの途中（09:51:58）に、09:47 頃に焼いた setup で
段 1 を撃ったところ **sha256 が 4 檔ずれた**。逐語＝

```
[tree] MISMATCH voices\voices.json         app=54D5FB65A572A56E/50       src=1FE5488B44C33D26/80
[tree] MISMATCH voices\voices.ywk.json     app=699E22CC3E911A08/345      src=D53C652E7D8FE540/596
[tree] MISMATCH docs\install.md            app=49AB8F070219A93F/19430    src=EA3165FB8EC6CC2C/20074
[tree] MISMATCH irodorittsywk.launcher.exe app=997F69686F0DB3EC/69610933 src=0DAABCAD1839274E/69610930
```

＝**焼いてから突合するまでの 4 分間に `build/out` が 4 檔動いていた**（便 D（2）と生産ライン席が在飛行中）。
檔の**集合**（110 檔・欠け 0・余り 0）は合っていて**中身だけ**が違う＝「古い setup を配る」事故そのものである。
台本には `-AllowSourceDrift` を置いた（**sha256 の食い違いだけを警告に落とす**。檔の欠け・余りは
その旗が立っていても必ず失敗にする）。**既定は off**＝本番の走で黙って通らない。

### 13-4 実射の逐語（**断定**）

**⑴ 最終の走**（`build/out/installer/` の 2 本・`-Flavor radeon -IncludeAcl`・PowerShell 7.6.5）

```
=== 0 failure(s) of 77 ===
EXIT=0 sec=61.67
logs: build/out/probe-log/e-install-20260905-100423
```

**77 段すべて PASS。**内訳＝走前の 3（予約ポート・他のランチャ・機体に irodori 導入なし）＋
段 1 の 12 ＋段 2 の 10 ＋段 3 の 6 ＋段 4 の 8 ＋段 5 の 6 ＋段 6 の 6 ＋段 7 の 8 ＋段 8 の 10 ＋
teardown の 5（鍵 0・`{app}` 0・`.lnk` 0・予約ポート・データ樹の復元）＋走前後の照合 3。

**⑵ 版とホストを変えた走**（同じ台本）

| 走 | ホスト | 段 | 結果 | 所要 |
|---|---|---|---|---|
| 既定（1・2・3・5・6・7・8） | **PowerShell 7.6.5** | 69 | **0 failure** | 60.7 s |
| `-Flavor cuda -Steps 1,3,7` | **Windows PowerShell 5.1**（5.1.26100.9168） | 34 | **0 failure** | 13.36 s |
| `-Steps 1 -IncludeAcl` | 7.6.5 | 28 | **0 failure** | — |
| 最終（既定＋`-IncludeAcl`） | 7.6.5 | **77** | **0 failure** | 61.67 s |

**⑶ 段 1 の逐語**（radeon・`build/out/installer` の本物）

```
[install] irodori-tts-ywk-setup-v0.1.0-radeon.exe exit=0 sec=2.79 log=612 lines
2026-09-05 10:04:24.529   User privileges: None
2026-09-05 10:04:24.531   Administrative install mode: No
2026-09-05 10:04:24.531   Install mode root key: HKEY_CURRENT_USER
[key] DisplayName=irodori-TTS for 読み分けちゃん（Radeon 版） v0.1.0
[key] DisplayVersion=0.1.0
[key] UninstallString="C:\Users\mugonkun\AppData\Local\Programs\irodori-tts-ywk-radeon\unins000.exe"
InstallLocation=C:\Users\mugonkun\AppData\Local\Programs\irodori-tts-ywk-radeon
新しい .lnk = ...\Start Menu\Programs\irodori-TTS for 読み分けちゃん（Radeon 版）.lnk（1 本だけ）
[tree] files=112 bytes=111113006 expected=110 matched=110
```

⇒ **E-7 は再現**（`User privileges: None`）。`{app}` は **112 檔・111,113,006 B**（`unins000.exe`／`.dat` を含む）で、
**sha256 突合は 110 / 110・欠け 0・余り 0**（**I-3**）。cuda 版は **112 檔・111,138,720 B**・同じく **110 / 110**。
`__pycache__` 0・`.pyc` 0・`docs\` に `acceptance.md`／`contract.md` **無し**も段として数えた。

**⑷ 段 2 の逐語**（UI の 3 頁）

```
[PASS] the started pid spawned the *.tmp child that owns the window (M-1) :: children=1
[PASS] the wizard window (ClassName TWizardForm) was found (M-2)
[PASS] page 1 is the destination page (no welcome page: M-5) :: after 0.04 s
[inno] click 次へ(N)
[PASS] page 2 is the ready page :: after 0.06 s
[inno] click インストール(I)
[PASS] page 3 is the finished page :: after 0.03 s
[inno] click 完了(F)
pages=select-dir -> ready -> finished     (3 枚)
files=112 bytes=111113006 missing=0 extra=0 mismatch=0
```

⇒ **M-1〜M-5 は全部この席の走でも再現した。頁は 3 枚**（歓迎頁もスタートメニューフォルダ頁も出ない）＝
設計書 §4 の「利用者操作 1・内側のクリック 3」が実物で立った（**E-5**）。
UIA は**頁の同定にしか使っていない**（`TNewStaticText` の文字列）。ボタンは全部
`SendMessage(hwnd, BM_CLICK=0x00F5)`。**`AutomationId` は 1 度も使っていない**（M-3）。

**手当て 1 つ**＝完了頁の `[Run]` のチェック（`postinstall`・既定 ON）は**押す前に外す**。
外さないと「完了」がランチャを起こしてしまい、**検分がポートを固定する前に**利用者データ樹に書きに行く。
外れたことは「完了の後にランチャのプロセスが 0 である」で数えた。

**⑸ 段 3 の逐語**（通知＝裁定 46）

```
[wizard] title=初回取得の前に（第三者物の通知） number=1 / 7
notices box = 16,509 chars / {app}\licenses\first-run-notices.md = 16,509 chars（一致）
FirstRunAcceptCheck enabled=True   (= CanAcceptNotices = 通知文を本当に読めた)
port 18097 は LISTENING でない      (= サーバは 1 度も起きていない)
```

⇒ **配布物の通知文が、本物の導入先から、そのまま 1 段目に出る**（**I-5**）。
起こす前に身代わりのデータ樹へ `settings.json`（`port=18097`・`autoStartServer=false`・
`firstRunCompleted=false`）を置いてあるので、**8088／7861／18088 は走の前後とも静かなまま**。

**⑹ 段 6 の逐語**（`[InstallDelete]`）

CUDA 版を入れた `{app}` にわざと 4 檔植えた＝`ledger\runtime-rocm-gfx1151.json`（種の混入そのもの）・
`server\zz-e2-stale.py`・`licenses\zz-e2-stale.md`・`voices\presets\zz-e2-stale.wav`。
同じ CUDA 版を上書き導入すると **4 檔とも消えた**（`survivors=0`）。突合も `missing=0 extra=0 mismatch=0`。

⇒ 設計書 §1-3 ⒜⒝ の手当ては**実物で効く**。**cu130／cu126 が UI から消える事故は塞がっている**（**I-6**）。

**新しい断定 1 つ**＝**Inno 6.7.3 は `[InstallDelete]` の仕事をログに 1 行も書かない**。
上書き導入のログ 627 行を `Deleting directory:` で引いて **0 件**（消えたのは檔だけ）。
⇒ **この段の証拠は檔しか無い**。「ログ行＋檔＋レジストリ」で見る設計書 §7-1 のうち、
**この段だけはログが使えない**＝台本は檔を植えて数える形にした。**後続の席への申し送り。**

**⑺ 段 8 の逐語**（UI の 2 段の問い・3 走）

3 走とも `unins000.exe /VERYSILENT /NORESTART /LOG=` で撃った。**`/SUPPRESSMSGBOXES` は渡していない**＝
`/VERYSILENT` は「開始の確認」と進捗窓だけを落とし、**メッセージボックスは落とさない**ので、
`SuppressibleTaskDialogMsgBox` の 2 段が**本物の TaskDialog として出る**（M-8 の既定 IDNO に落ちない）。
その 2 枚が検分の対象そのものである。

| 走 | 1 段目 | 2 段目 | 結果（**断定**） |
|---|---|---|---|
| **8a** | **削除する** | **残す** | `runtime`／`models`／`cache`／`logs`／`miopen` が消え、**`voices\` と `settings.json` は残った**。`{app}`・鍵も消滅 |
| **8b** | **削除する** | **削除する** | 上に加えて **`voices\` と `settings.json` も消え**、空になった根を `RemoveDir(Data)` が**畳んだ**（`root exists = False`） |
| **8c** | **出ない** | 残す | もう一方の種（CUDA 版）を入れた状態＝**問いが 1 枚しか出なかった**。データ樹は 1 檔も動かず |

8a・8b の 2 枚の逐語（UIA が読んだ全文）＝

```
アンインストール | 取得した実行系とモデルも削除しますか？ |
C:\Users\mugonkun\AppData\Local\irodori-tts-ywk に残っています。残しておくと、次に入れ直したときそのまま使えます。 |
削除する | 残す | アンインストール

アンインストール | 登録した話者と設定も削除しますか？ |
追加した参照 wav・話者の名前・設定は利用者の資産です。既定では残します。 | 削除する | 残す | アンインストール
```

8c の 2 段目（もう一方の種が居るときだけ足される 1 行が**実物で出た**）＝

```
アンインストール | 登録した話者と設定も削除しますか？ |
追加した参照 wav・話者の名前・設定は利用者の資産です。既定では残します。
CUDA 版がまだこの機体に入っています。話者と設定は 2 つの版が同じ場所（C:\Users\mugonkun\AppData\Local\irodori-tts-ywk）を共有しているので、ここで消すとそちらからも消えます。 | 削除する | 残す | アンインストール
```

⇒ **§5-4 ⑵⑶ と是正席の M-5 の手当ては、無人（§11-8-5）だけでなく UI でも効く**（**I-7**）。

**⑻ 段 4 の逐語**（読取専用の `{app}`＝**E-3**・任意段を前倒しで撃った）

```
icacls <app> /deny <user>:(OI)(CI)(WD,AD,WEA,WA,DC,DE)
  processed file: C:\Users\mugonkun\AppData\Local\Programs\irodori-tts-ywk-radeon
  Successfully processed 1 files; Failed processing 0 files
[PASS] {app} really is read only now (a write into it is refused) :: could write = False
[PASS] the first run wizard window opened / title=初回取得の前に（第三者物の通知） / 1 / 7
[PASS] the notices box holds the bytes of {app}\licenses\first-run-notices.md :: 16509 == 16509
```

⇒ **読取専用の導入先でもランチャは起き、通知を読み、1 段目まで通る**（**E-3 は満たす**）。
`icacls` は**非昇格で通った**（`Failed processing 0 files`）＝**この段に管理者昇格は要らない**（§13-7 ⑸）。
ACE は `finally` で `icacls <app> /remove:d <user>` により外し、そのあとのアンインストールも exit 0。

### 13-5 `AppMutex` の実測（**この席の中心・設計書 §8 危険 6 の「推測」を半分潰した**）

ランチャ（`{app}\IrodoriTtsYwk.Launcher.exe`・**本物の導入先から起動**・同じログオンセッション）が走っている
最中に、**同じ setup を上書き導入した**。

**⑴ 無人（`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`）＝弾いた。exit 1・0.29 s・ログ 20 行。**
ログの末尾の逐語（`install-mutex-silent-radeon.log`）＝

```
2026-09-05 10:04:38.898   Created temporary directory: C:\Users\mugonkun\AppData\Local\Temp\is-...tmp
2026-09-05 10:04:38.904   Defaulting to Cancel for suppressed message box (OK/Cancel):
                          セットアップは実行中の irodori-TTS for 読み分けちゃん（Radeon 版） を検出しました。

                          開いているアプリケーションをすべて閉じてから「OK」をクリックしてください。「キャンセル」をクリックすると、セットアップを終了します。
2026-09-05 10:04:38.904   Got EAbort exception.
2026-09-05 10:04:38.904   Deinitializing Setup.
2026-09-05 10:04:38.905   Log closed.
```

**⑵ UI（`/NORESTART` だけ）＝同じ文言のメッセージボックスが出た。**逐語（`install-mutex-ui-radeon.log`）＝

```
2026-09-05 09:53:10.564   Message box (OK/Cancel):
                          セットアップは実行中の irodori-TTS for 読み分けちゃん（Radeon 版） を検出しました。
                          ...
2026-09-05 09:53:11.184   User chose Cancel.
2026-09-05 09:53:11.185   Got EAbort exception.
2026-09-05 09:53:11.185   Deinitializing Setup.
```

UIA が読んだ窓の全文＝`セットアップ | OK | キャンセル | セットアップは実行中の irodori-TTS for 読み分けちゃん（Radeon 版） を検出しました。…`

**ここから取れた断定が 4 つ。**

1. **`AppMutex` は効く**（同じログオンセッション・per-user 導入・`Local\` 名前空間）。
   `.iss` の `AppMutex=Local\irodori-tts-ywk-launcher` と `App.xaml.cs:32` の
   `private const string MutexName = @"Local\irodori-tts-ywk-launcher";` が**実物で噛み合っている**。
2. **弾く位置は「導入が始まる前」**＝ログに `Starting the installation process.` が**1 行も出ない**
   （台本が段として数えている）。`{app}` は 1 檔も触られない＝**中途半端な樹が残る経路が無い**。
3. **出る文言は `SetupAppRunningError`（`Japanese.isl` の逐語）であって `CloseApplications` の頁ではない。**
   ログに `RestartManager` の行（`Found N files to register with RestartManager.` 以下）が**1 行も出ない**＝
   **RestartManager は走る前に中断されている**。⇒ **`AppMutex` を置いている限り `CloseApplications`（既定 yes）は
   到達不能**である。設計書 §8 危険 6 の「`CloseApplications` は既定のまま頼らない」は**正しかった**が、
   理由は「効き目が薄い」ではなく「**そこまで到達しない**」に書き換わる。
   ※ 参考＝ランチャが**居ない**ときの上書き導入では
   `RestartManager found no applications using one of our files.`（段 6 のログの逐語）＝
   `{app}` の檔を掴んでいる者は誰も居ない。
4. **無人の既定は Cancel**＝`Defaulting to Cancel for suppressed message box (OK/Cancel):`。
   M-8（アンインストールの `Yes/No` は `No`）と**同じ型で、既定の向きが違う**（`OK/Cancel` は `Cancel`）。
   ⇒ **無人の更新導入は、ランチャが走っていると黙って exit 1 で落ちる。**
   自動更新を将来作るなら「先に落としてから撃つ」が要る＝**便 D への申し送り**。

**まだ推測のまま**＝**別セッション**（高速ユーザ切替・RDP）の個体を `Local\` が検出できるかは**未実射**
（この機体で 2 つ目のログオンセッションを作るには管理者が要る＝裁定 90）。設計書 §8 危険 6 の
但し書きは**残す**。

### 13-6 データ樹の退避と復元の逐語（**この席の停止域の要**）

台本は**壊す段（3・4・5・6・7・8。段 2 も完了頁の `[Run]` があるので含む）に入る前に必ず**次をする。

```
[data] before: files=28 bytes=3571678193
[data] moved aside -> C:\Users\mugonkun\AppData\Local\irodori-tts-ywk.e2-backup
```

＝`Rename-Item` で**同じ親の下**へ名前を変えるだけ（1 バイトも読み書きしない・3.4 GB の複写をしない）。
退避先の名が既に在れば**何もせず throw する**（上書きしない）。そのうえで、代わりに
**形だけの身代わり樹**（`runtime\rocm-gfx1151\`・`models\hub\`・`cache\`・`logs\`・`miopen\`・
`voices\ref\`・`voices\latents\` の目印檔＋`voices\voices.json`・`voices\voices.ywk.json`＋`settings.json`＝
**10 檔・734 B**）を置く。段 8 の「消える／残る」は**この身代わりに対して**測る
（3.4 GB の複写はしない＝形が同じなら結論は同じで、時間だけが違う）。

走の終わりは `finally` で必ず＝身代わりを消す → 退避を `irodori-tts-ywk` に戻す → **前後を数えて段にする**。

```
[data] restored -> C:\Users\mugonkun\AppData\Local\irodori-tts-ywk
[PASS] the operator data tree came back byte for byte ::
       before files=28 bytes=3571678193 :: after files=28 bytes=3571678193
```

**この席が撃った走は 8 本（最終の 77 段を含む）。8 本すべてでこの段が PASS した。**
走り終えた機体の実測（本節を書く直前）＝

```
Programs\irodori*               = 0
HKCU の Uninstall の irodori 鍵  = 0
スタートメニューの irodori .lnk  = 0
%LOCALAPPDATA%\irodori*         = irodori-tts-ywk のみ（e2-backup は残っていない）
データ樹                         = 28 檔・3,571,678,193 B
走っているランチャ                = 0 ／ 迷子の python = 0
8088 / 7861 / 18088 / 18097     = どれも LISTENING でない
```

**守りは 3 枚**＝⑴ 退避が済んでいない状態でデータ樹に届く段は `Assert-WorkTree` が**例外で止める**
（`-DataDirBackup:$false` はその全段を拒む）⑵ 身代わりが消せないときは**身代わりの方を改名して逃がし**、
**利用者の樹を必ず先に戻す** ⑶ 戻せなかったときは**手で戻す 1 行を画面に出す**
（`Rename-Item -LiteralPath "...\irodori-tts-ywk.e2-backup" -NewName "irodori-tts-ywk"`）。
※ ⑵⑶ は**発火しなかった**＝**未実射**。

### 13-7 卓へ（**本席の停止域の外＝設計書の訂正が要る 5 件**）

1. **§7-2 の段 4 の書き方 `icacls … /deny (OI)(CI)W` は誤りである。**実測＝`icacls` の簡易権利 `W` は
   `FILE_GENERIC_WRITE` で **`SYNCHRONIZE` を含む**ため、これを拒むと **exe が起動できなくなる**
   （`Start-Process` が「アクセスが拒否されました」で落ち、ランチャが 1 度も走らない）。
   それは「読取専用の導入先」ではなく「**開けない導入先**」で、E-3 の検分にならない。
   正しくは **`/deny <user>:(OI)(CI)(WD,AD,WEA,WA,DC,DE)`**（書込・追記・EA・属性・子の削除・削除を名指し）。
   台本はこちらで書いてあり、**「本当に書けないこと」を檔 1 つ作って確かめてから**先へ進む。
2. **§7-2 の段 8 の書き方は 1 走に見えるが、実際は 3 走が要る。**⑴ 1 段目だけ「削除する」
   （＝`runtime`／`models`／`cache` が消え **`voices` と `settings` は残る**）⑵ **2 段とも「削除する」**
   （＝`voices` と `settings` も消え、根が畳まれる）⑶ もう一方の種が居る（＝1 段目が出ない）。
   ⑴ と ⑵ は**結果が違う**ので、どちらか 1 つでは「消えるべき物が消え、残るべき物が残る」を言えない。
3. **§7-1 の「判定はログ行＋檔＋レジストリ」は段 6 では成り立たない。**Inno 6.7.3 は
   **`[InstallDelete]` をログに 1 行も書かない**（627 行を引いて 0 件）。段 6 の証拠は**檔だけ**である。
4. **§8 危険 6 の書き方を更新できる。**`CloseApplications` は「効き目が薄い」のではなく
   **`AppMutex` が先に中断するので到達しない**（`RestartManager` の行がログに 1 行も出ない）。
   一方で**新しい事実**＝**無人の更新導入はランチャが走っていると exit 1 で黙って落ちる**
   （`Defaulting to Cancel …（OK/Cancel）`）＝**便 D への申し送り**。
   **別セッションの検出は未実射のまま**（管理者が要る）＝但し書きは残す。
5. **Q-E3 の「管理者昇格 2 回」は 2 回のままだが、内訳が 1 つ減った。**§7-3 は 09-06 に
   「段 4（ACL）・段 9（ローカルユーザ）」で管理者 1 回としているが、**段 4 は非昇格で通った**（本日実射済み）。
   残る昇格は ⑴ **段 9 のローカルユーザ作成** ⑵ **E-1 の Windows Sandbox 有効化＋再起動** の 2 つだけである。

**あわせて台本の作法の申し送り 1 件（生産ライン席 E-2 と後続の検分席へ）**＝
**複数値の引数を `[int[]]` で受けてはいけない。**実測＝Windows PowerShell 5.1 に `-File` 経由で
`-Steps 1,3,7` を渡すと `"1,3,7"` が**桁区切り付きの整数 `137` 1 個**に化け、**何も撃たずに exit 0** で終わる
（7 では配列になる）。**カンマ区切りの `[string]` で受けて自分で split する**。
`build/installer-build.ps1` に同じ形の引数があれば同じ穴が開いている（本席は `build/` を読むだけ）。

### 13-8 まだ撃っていない（＝この席の外・または裁定待ち）

- **段 9（日本語＋空白のユーザ名「テスト 太郎」）**＝ローカルユーザの作成に**管理者昇格**（裁定 90・Q-E3）。
- **E-1（vc_redist 欠落機）**＝Windows Sandbox の有効化に**管理者＋再起動**（Q-E3）。
- **別セッション（高速ユーザ切替・RDP）での `AppMutex`**＝同上（§13-5 の 4 の但し書き）。
- **junction を張ったデータ樹でのアンインストール**（§11-8 の M-6）＝機体の `D:` が停止域。**未実射のまま**。
- **空きの告知が実際に出る画面**（§11-8-7）＝この機体の空きが 1 TiB 級で閾値に掛からない。**未実射のまま**。
- **場所の門が UI で実際に断る画面**（§11-8-3 の M-4）＝台本は既定の導入先しか撃っていない。
  撃つには「場所の頁に別の路を打ち込んで『次へ』」が要り、**`SendMessage` が自分の開かせた MsgBox で
  自分を待つ**形になる（台本の `Invoke-InnoButton -Async` はそのために置いてあるが、この段は**書いていない**）。
  **未実射**。
- **別機体での再現**＝この機体 1 台・この口座 1 つだけ。RTX 機（09-08 以降）で撃ち直す。

---

## 14. 統合席（便 E（2））の記帳（2026-09-05 10:15〜10:29・Radeon 機・実装サブ席 opus）

> 本節は**この席が実際に撃った物だけ**を書く。読んだだけ・推したものは「推測」と明記する。
> 触った檔は `build/installer-build.ps1`（1 箇所）と本節の 2 つだけ。
> `installer/`・`probe/`・`launcher/`・`server/`・`build/` の他の檔・`docs/install.md`・`docs/acceptance.md` は**読むだけ**。
> `git commit` していない。外部へ 1 バイトも取りに行っていない。**管理者昇格を 1 度もしていない。**
> **利用者データ樹は 1 バイトも動いていない**（走の前後とも 28 檔・3,571,678,193 B＝§14-7）。
>
> **この席の中心は 2 つ**＝⑴ **配布樹はビルドしたホストで変わる**（§14-3）
> ⑵ **setup.exe は全段ビルドではバイト再現しない**（§14-4）。どちらも §12・§13 の記帳の訂正が要る。

### 14-1 撃った 2 本（**断定**・最終ビルド 10:25:06〜10:25:39・pwsh 7.6.5・`-All`）

```
[10:25:14] INFO distributable tree: 108 file(s), 37111434 B (35.39 MiB); 0 __pycache__/.pyc file(s) left out of the scan
[10:25:26] INFO ISCC exit 0 after 12.08 s; 110 Compressing: line(s); 0 warning line(s)
[10:25:26] INFO   | Successful compile (12.265 sec).
[10:25:39] INFO ISCC exit 0 after 11.93 s; 110 Compressing: line(s); 0 warning line(s)
[10:25:39] STEP DONE: 2 installer(s), 18 gate(s), 0 failed.
EXITCODE=0 sec=33.91
```

| 版 | バイト | sha256 |
|---|---|---|
| cuda | **84,986,366** | `24bdfc4c289d2694f0c1cc3de8d8ba275ffc0eac0c24c0f96e55218cccf151f4` |
| radeon | **85,000,859** | `a69d22a3fc84d85b6d28362a42dd50d7e5b3db2db5f8da7d2d6dea08eb01272b` |

`build/out/installer` で `sha256sum -c SHA256SUMS.txt` ＝ **2 本とも `OK`**。
門は **18 本すべて OK・警告 0 行**（`WARN` も 0＝§14-3 の直しの後）。**I-1 は再現した。**

### 14-2 §12・§13 の値から動いた理由（**この席が撃った時点の事実**）

§12 は cuda 84,986,386／radeon 85,000,879、§13 はその 2 本を検分した。本席の 2 本は**別の sha256** である。
理由は 2 つあり、**どちらも「中身が変わった」ではない**（§14-3・§14-4）。
**檔の中身は 108 檔すべて §12 の時と同じ**（sha256 の目録で照合）。

### 14-3 **配布樹はビルドしたホストで変わる**（**断定**・この席の第 1 の発見）

`build/installer-build.ps1 -All` の初手が `A-1` で警告を出した。

```
[10:15:19] WARN A-1 the tree drifted from the recorded figures: 108 file(s), 37111434 B (expected 108 / 37111715)
```

**281 B の差**。`build/assemble-app.ps1` を 2 つのホストで交互に走らせて突き止めた（10:17〜10:18）。

| ホスト | `voices\voices.json` | `voices\voices.ywk.json` | 配布樹の合計 |
|---|---|---|---|
| **Windows PowerShell 5.1**（5.1.26100.9168） | **80 B** `1FE5488B44C33D26…` | **596 B** `D53C652E7D8FE540…` | **37,111,715 B** |
| **PowerShell 7.6.5** | **50 B** `54D5FB65A572A56E…` | **345 B** `699E22CC3E911A08…` | **37,111,434 B** |

**残り 106 檔は 1 バイトも違わない**（sha256 の目録を 2 本取って `Compare-Object`＝差はこの 2 檔だけ）。
同じホストで 2 度走らせた目録は **完全一致**＝**ホストの差だけ**である。

**原因**＝`build/assemble-app.ps1:317,331` がこの 2 檔を `Write-YwkJsonFile`（＝`ConvertTo-Json`）で書く。
**PS 5.1 の `ConvertTo-Json` の整形が PS 7 と違う**（コロンの後に空白 2 つ・閉じ括弧をぶら下げ揃えにする）。
PS 5.1 が書いた実物の逐語＝

```
{
    "デフォルト":  {
                  "no_ref":  true
              }
}
```

**この 4 つの sha256 は、§13-3 が「drift」として記帳した MISMATCH 行の値そのものである。**

```
[tree] MISMATCH voices\voices.json         app=54D5FB65A572A56E/50       src=1FE5488B44C33D26/80
[tree] MISMATCH voices\voices.ywk.json     app=699E22CC3E911A08/345      src=D53C652E7D8FE540/596
```

⇒ **§13-3 の「4 分間に `build/out` が 4 檔動いていた」は、4 檔のうち 2 檔については誤診である。**
その 2 檔は時間で動いたのではなく、**setup を焼いたホストと突合したホストが違っただけ**である
（残る 2 檔＝`docs\install.md` 19,430→20,074・exe 69,610,933→69,610,930 は本物の時間の動き）。

**独立の第 2 の目撃**＝検分台本が置く身代わりデータ樹が **PS 7 で 734 B・PS 5.1 で 755 B**（21 B 差）。
`e-install-probe.ps1` も `settings.json` を `ConvertTo-Json` で書くので、**同じ根から同じ形で出た**。
（身代わりなので害は無い。根が 1 つであることの傍証として記帳する。）

**本席が直した 1 箇所**（担当 path の中）＝`build/installer-build.ps1` の A-1 の期待値を
**実測した 2 つの値の集合**にし、根拠を檔の中に逐語で書いた。

```
$ExpectedAppFiles = 108
$ExpectedAppBytes = @([int64]37111715, [int64]37111434)
```

判定は `-not ($ExpectedAppBytes -contains $appBytes)` に、報告行は `(expected 108 / 37111715 or 37111434)` に変えた。
**これは対症であって治療ではない**＝治療は `build/assemble-app.ps1` 側（本席の停止域）＝卓へ ⑴。
あわせて、旧注釈の「281 B は便 D が `ledger/README.md` §7 を書き換えたため」を**消した**＝
**その断定は誤りだった**（`ledger/README.md` は 26,563 B・06:59:54 から 1 バイトも動いていないことを実測）。

### 14-4 **setup.exe は全段ビルドではバイト再現しない**（**断定**・この席の第 2 の発見）

§12 は卓へ「**setup はバイト再現する**（5 走・2 ホストで sha256 が 1 ビットも動かなかった）」と申し送り、
**§8 危険 7（署名なし）の対策欄に足してよい**と書いた。**本席の実測はこれを半分否定する。**

**⑴ 同じ入力を触らずに 3 走（`-SkipAssemble -SkipPublish`）＝完全に同一**

```
run 1: 84986366 B  2497DAC77FDB874ACBB702C2030E889B6625974040E056C8F0FD99708DD3B810
run 2: 84986366 B  2497DAC77FDB874ACBB702C2030E889B6625974040E056C8F0FD99708DD3B810
run 3: 84986366 B  2497DAC77FDB874ACBB702C2030E889B6625974040E056C8F0FD99708DD3B810
```

⇒ **ISCC 6.7.3 とこの `.iss` は、入力が 1 バイトも動かなければ決定的である**（§12 の見立ては**ここでは正しい**）。

**⑵ 全段（`-All`）で走らせると毎回違う**

| 走 | cuda | radeon |
|---|---|---|
| 10:15 | 84,986,365 B | 85,000,879 相当（別 sha） |
| 10:19 | 84,986,366 B `316b3328…` | 85,000,860 B |
| 全段 1 | 84,986,366 B `64A4EDEE…` | 85,000,860 B `DF7B98D6…` |
| 全段 2 | 84,986,366 B `170778C7…` | **85,000,859 B** `9578F4F4…` |
| 最終 | 84,986,366 B `24bdfc4c…` | 85,000,859 B `a69d22a3…` |

**⑶ 発行 exe は犯人ではない**＝`release-build.ps1 -SkipZip` を撃ち直しても
`0DAABCAD1839274E71AF656851638DA70ADAA69A3D5A2FAC20F9520C1ABE7E39`・69,610,930 B で**不動**
（この値は §13-3 が `src=0DAABCAD1839274E/69610930` と記帳した値と同じ）。**.NET の発行は決定的である。**

**⑷ 犯人＝檔の更新時刻**（切り分けの実射）

```
H1（再組み立ての前） = 170778C7C72080BEF9446771E669C4E03394BB0E9DC0FFD03FAF92BE2DAB9E3F  84986366 B
content manifest identical = True                <- 108 檔すべて sha256 が同一
mtimes changed on 6 file(s):
   server\upstream\Irodori-TTS-Server\UPSTREAM-COMMIT.txt
   server\upstream\Irodori-TTS\UPSTREAM-COMMIT.txt
   server\upstream\Irodori-TTS\irodori_tts\codec.py
   server\upstream\Irodori-TTS\irodori_tts\inference_runtime.py
   voices\voices.json
   voices\voices.ywk.json
H2（再組み立ての後） = 8D21C3ED70C3F255988E5500D581BDDEC2EAF17A49F289639979E8F3A9EE1649  84986368 B
DIFFERENT -> mtimes ALONE change the setup bytes
```

⇒ **断定**＝**Inno は檔ごとの更新時刻を setup に格納する**ので、
**中身が 1 バイトも違わない配布樹でも、更新時刻が違えば setup は別物になる**（サイズすら 2 B 動いた）。
`assemble-app.ps1` は毎走この **6 檔を書き直す**（写しではなく生成物＝パッチ済み 2 檔・pin 2 檔・生成 json 2 檔）ので、
**全段ビルドは決して同じ setup を出さない。**

⇒ **§12 が卓へ回した ⑷ は、条件を付けなければ檔に書けない。**正しい言い方＝
**「同じ入力樹（更新時刻まで同じ）に対して ISCC は決定的」**であって、
**「誰でも同じ版から再ビルドすれば同じ sha256 になる」ではない。**
`SHA256SUMS.txt` が担保するのは **「配ったこの 1 本」の同一性**であって、**再現ビルドによる検証ではない**。
（§8 危険 7 の対策としては**それで十分**＝利用者が落とした檔と配った檔を突合できる。
だが「第三者が再ビルドして確かめられる」とは**書けない**。）

### 14-5 検分台本の実射（**断定**・本物の 2 本に対して）

**⑴ 既定の走**（`probe/e-install-probe.ps1`・段 1・2・3・5・6・7・8・`-Flavor radeon`・**PowerShell 7.6.5**）

```
=== 0 failure(s) of 69 ===
PROBE EXITCODE=0 sec=60.26
logs: build/out/probe-log/e-install-20260905-102557
```

**69 段すべて PASS。**逐語の要点＝

```
[install] irodori-tts-ywk-setup-v0.1.0-radeon.exe exit=0 sec=2.82 log=612 lines
2026-09-05 10:25:57.825   User privileges: None
2026-09-05 10:25:57.827   Administrative install mode: No
2026-09-05 10:25:57.827   Install mode root key: HKEY_CURRENT_USER
[tree] files=112 bytes=111112725 expected=110 matched=110
[PASS] stage 1: every file of {app} matches the source sha256 (I-3) :: matched=110/110 mismatch=0
[PASS] stage 2: exactly three wizard pages (E-5) :: pages=select-dir -> ready -> finished
[PASS] stage 3: the notices box holds the bytes of {app}\licenses\first-run-notices.md :: shown=16509 chars, file=16509 chars
[install] ... exit=1 sec=0.3 log=20 lines        <- 段 5：走行中の上書き
[PASS] stage 5: AppMutex refuses the overwrite before the install starts :: EAbort=True started=False RestartManager=False
[PASS] stage 6: [InstallDelete] removed every planted file :: survivors=0
[PASS] stage 6: RECORDED -- [InstallDelete] leaves no trace in the install log :: "Deleting directory:" lines = 0
[PASS] stage 7: both questions fell to their default No (M-8) :: Defaulting-to-No lines = 2
[PASS] stage 8c: question 1 did NOT appear while the other flavour is installed :: questions asked = 1
[PASS] the operator data tree came back byte for byte :: before files=28 bytes=3571678193 :: after files=28 bytes=3571678193
```

**⑵ PS 5.1 の走**（`-Flavor cuda -Steps 1,7`・**Windows PowerShell 5.1**（5.1.26100.9168））

```
=== 0 failure(s) of 28 ===
PS5.1 PROBE EXITCODE=0 sec=10.03
logs: build/out/probe-log/e-install-20260905-102716
[tree] files=112 bytes=111138439 expected=110 matched=110
[PASS] stage 1: every file of {app} matches the source sha256 (I-3) :: matched=110/110 mismatch=0
```

**ParseFile のエラーは両ホストとも 0**（`installer-build.ps1`・`e-install-probe.ps1` とも）＝**I-9 は再現した。**

**⑶ この席が足した事実＝突合が本当に 110/110 で通った**

§13 は作り込みの途中で 4 檔の MISMATCH を踏んだ（§13-3）。本席は
**「配布樹と setup を 1 走で作り、その間 `build/out` に誰も触らない」**手順で撃ち、
**2 版とも `missing=0 extra=0 mismatch=0`** を取った。⇒ **I-3・I-4 は本物の 2 本で成立する。**
`{app}` の実測＝radeon **112 檔・111,112,725 B**／cuda **112 檔・111,138,439 B**（`unins000.exe`・`.dat` を含む）。

### 14-6 直した 1 点（**担当 path の中・これだけ**）

| 檔 | 直し | 形（実測） |
|---|---|---|
| `build/installer-build.ps1` | A-1 の期待値を実測 2 値の集合にし、根因（`ConvertTo-Json` のホスト差）を逐語で注釈。誤りだった旧注釈（`ledger/README.md` §7 のせいという断定）を削除 | **37,406 B・811 行・ASCII のみ（非 ASCII 0）・CRLF 810 対・単独 LF 0・BOM 無し** |

`probe/e-install-probe.ps1` は **1 行も直していない**（76,079 B・1,544 行・そのまま 0 failure で通った）。
`build/assemble-app.ps1`・`installer/irodori-tts-ywk.iss`・`docs/install.md` にも触れていない。

### 14-7 走り終えた機体の実測（**断定**・走の前と後で突合）

| 見た物 | 走の前（10:14） | 走の後（10:29） |
|---|---|---|
| 利用者データ樹 `%LOCALAPPDATA%\irodori-tts-ywk` | **28 檔・3,571,678,193 B** | **28 檔・3,571,678,193 B**（一致） |
| `%LOCALAPPDATA%\irodori*` | `irodori-tts-ywk` のみ | 同じ（**`.e2-backup` は残っていない**） |
| `%LOCALAPPDATA%\Programs\irodori*` | **0** | **0** |
| HKCU の Uninstall（`F228543A` / `ECA98712`） | **0** | **0** |
| スタートメニューの irodori `.lnk` | **0** | **0** |
| 8088 / 7861 / 18088 / 18097 / 18098 / 18099 | **どれも LISTENING 0** | **どれも LISTENING 0** |
| ランチャ／python／setup・unins のプロセス | **0 / 0 / 0** | **0 / 0 / 0** |
| `build/out` の `*e2-*` の残骸 | **0** | **0** |

**新しい小さな事実 1 つ**＝**アンインストーラは `%TEMP%` に `is-*-uninstall.tmp` を 1 つ残す**。
本席の最後の走（10:27:24）が残した `is-K8AGJT8J5W-uninstall.tmp`＝**2 檔・4,443,076 B**
（`_unins.tmp`・`_unins-done.tmp`）。**本席が消した。**
残る `is-*.tmp` は **10 件で、すべて 10:15 より前**（4 件は 07:19〜07:20＝検分案席の使い捨て `.iss` の残骸、
6 件は VS Code などの無関係な古い物）＝**本席の残骸は 0 件**。
※ 何走のうち何走が残すかは**数えていない**＝**推測しない**。

### 14-8 卓へ（**本席の停止域の外＝檔の訂正が要る 5 件**）

1. **【最重要】`build/assemble-app.ps1` が配布樹をホスト非依存に書くよう直す要がある。**
   `voices/voices.json`・`voices/voices.ywk.json` を `ConvertTo-Json` の整形任せにしているため、
   **PS 5.1 で組むか PS 7 で組むかで配布樹が 281 B 変わり、setup も `SHA256SUMS.txt` も変わる。**
   直し方は 2 つ（本席は撃っていない＝**推測**）＝⒜ この 2 檔を整形済みの固定文字列として書く
   ⒝ `Common.ps1` の `Write-YwkJsonFile` に自前の整形を持たせる。
   **あわせて「配布に出す setup をどのホストで焼くか」を手順に固定するべきである**（今は暗黙）。
2. **§12 の卓へ ⑷（「setup はバイト再現する」）を条件付きに直してから檔に載せてほしい。**
   実測＝**入力を触らなければ ISCC は決定的**だが、**全段ビルドは毎回違う setup を出す**
   （`assemble-app` が 6 檔の更新時刻を毎走更新し、Inno がそれを格納するため）。
   §8 危険 7 に書けるのは「**配ったこの 1 本と手元の檔を突合できる**」までで、
   「**第三者が再ビルドして確かめられる**」とは書けない。
3. **§13-3 の「drift」の診断を半分訂正してほしい。**4 檔の MISMATCH のうち
   **`voices.json`・`voices.ywk.json` の 2 檔は時間の動きではなくホストの差**である
   （sha256 が本席の実測値と完全に一致）。残り 2 檔（`docs\install.md`・exe）は本物の時間の動き。
4. **§1-2 の数字が古い。**発行 exe＝**69,610,930 B**（檔に載る 69,588,729 は古い）。
   導入後の `{app}` の実測＝radeon **111,112,725 B**・cuda **111,138,439 B**（112 檔・`unins000` 込み）＝
   §1-2 の「106,699,889 B（101.76 MiB）」「アンインストーラ込みで ≈106 MiB」は**測り直しが要る**。
   ※ ウィザードの `[Messages]` は「約 106 MiB」と言っており、実測は **106.0 MiB**（111,112,725 B）＝**文言は直さなくてよい**。
5. **`build/installer-build.ps1` の A-1 の 2 値受けは対症である。**1 が直れば
   `$ExpectedAppBytes` は 1 値に戻すべきで、**戻す責任は次の席にある**（本席は檔にその旨を注釈した）。

### 14-9 まだ撃っていない（＝この席の外・または裁定待ち）

- **段 4（読取専用の `{app}`）**＝本席は `-IncludeAcl` を渡していない（§13-4 ⑻ で実射済みのため撃ち直していない）。
- **段 9（日本語＋空白のユーザ名）・E-1（vc_redist 欠落機）・別セッションの `AppMutex`**＝
  すべて**管理者昇格**が要る（裁定 90・Q-E3）＝§13-8 のまま**動いていない**。
- **junction を張ったデータ樹でのアンインストール**・**空きの告知の画面**・**場所の門が UI で断る画面**＝
  §13-8 のまま**未実射**。
- **`assemble-app.ps1` を直した後の再現**＝本席は直していないので**未実射**。
- **別機体・別版 ISCC での再現**＝この機体 1 台・Inno 6.7.3 だけ。**§14-3・§14-4 の断定はこの機体の実測**であり、
  他機体への一般化はしていない（ただし §14-4 は Inno の仕様に由来するので**他機体でも起きると推測する**）。

---

## 15. E2E 席（便 E（2））の記帳（2026-09-05 10:43〜10:52・Radeon 機・実装サブ席 opus）

> 本節は**この席が実際に撃った物だけ**を書く。読んだだけ・推したものは「推測」と明記する。
> 触った檔は**本節**とスクラッチパッド（`…/scratchpad/ben-e2/e2e/`）の 3 本の ps1 だけ。
> `installer/`・`probe/`・`launcher/`・`server/`・`build/`・`docs/install.md`・`docs/acceptance.md` は**読むだけ**。
> `git commit` していない。**管理者昇格を 1 度もしていない。**8088／7861／18088 は 1 度も起こしていない
> （使った私設ポートは 18097・18098・18099 の 3 本だけ）。
> **外部への取得を撃ったのはこの席である**（台帳の URL＝pytorch／PyPI／HF。実測は §15-6）。
>
> **この席の中心は 3 つ**＝
> ⑴ **本物の NVIDIA 無し機で cu126 の初回取得は「起動の段」で止まる**（門が正しく断る＝§15-2）。
> ⑵ **acceptance §3 の 6（W-1）は「はい」で埋まった**——ただし製品の路ではなく、
>    `IRODORI_MODEL_DEVICE=cpu` で wrapper を直に起こした経路で（§15-4）。
> ⑶ **Radeon 版は 4 分弱で導入から 1 射まで通った**（0 failure・§15-5）。

### 15-0 撃った 4 走（一覧）

| 走 | 何 | 時刻 | 所要 | 結末 |
|---|---|---|---|---|
| 1 | CUDA 版 setup → 初回取得（cu126）→ 起動 | 10:43:51〜10:45:19 | **88.33 s** | **起動の段で失敗**（門が断った）・取得と展開は成功 |
| 2 | Radeon 版 setup → 初回取得（rocm-gfx1151）→ 起動 → 1 射 | 10:46:19〜10:49:00 | **162.95 s** | **0 failure** |
| 2b | Radeon 版を入れ直して「試し撃ち」を製品の路で 2 射 | 10:50:14〜10:51:2x | ≈75 s | **成功**（§15-5-2） |
| W-1 | cu126 の実行系を `device=cpu` で直に起こして 1 射 | 10:51:29〜10:52:0x | ≈40 s | **200・wav**（§15-4） |

台本＝`…/scratchpad/ben-e2/e2e/e2e-first-run.ps1`（34,512 B・817 行・ASCII のみ・CRLF・**PS 5.1 と 7 の両方で ParseFile エラー 0**）、
`…/w1-cu126-cpu.ps1`（10,782 B）、`…/try-shot.ps1`（9,100 B）。3 本とも `probe/common.ps1` を dot-source して
UIA の手（`Find-ById`・`Set-ToggleById`・`Select-ComboItemById`・`Invoke-ButtonById`・`Get-TextById`）を借りている
（`probe/common.ps1` と `probe/e-install-probe.ps1` は**読んだだけ・1 バイトも直していない**）。
なま値＝`…/scratchpad/ben-e2/e2e/log/` の下（`summary.json`・`wizard-progress.log`・`install.log`・`uninstall.log`・
`ywk-status.json`・`server-stderr.log`・出力 wav 2 本）。

**ランチャは env を 1 本も立てずに起こした**（`YWK_LAUNCHER_APP_DIR`／`_RUNTIME_DIR`／`_DATA_DIR` を子から消してから
`Start-Process`）＝設計書 §1-7・`AppPaths.cs:205` のとおり、**開発起動ではない本物の per-user の路**である。

### 15-1 走 1＝CUDA 版・cu126（**断定**・逐語）

**⑴ 導入**（`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`・`/DIR` は付けない＝`.iss` の既定を試す）

```
[install] irodori-tts-ywk-setup-v0.1.0-cuda.exe exit=0 sec=2.74 log=612 lines
[install] 2026-09-05 10:43:51.923   User privileges: None
[install] 2026-09-05 10:43:51.925   Administrative install mode: No
[install] 2026-09-05 10:43:51.925   Install mode root key: HKEY_CURRENT_USER
[install] {app} = C:\Users\mugonkun\AppData\Local\Programs\irodori-tts-ywk
[install] {app} files=112 bytes=111138439
```

setup は 84,986,366 B（§14-1 の本番の 1 本）。**E-7（UAC が出ない）は本物の場所への導入でも再現した。**
`{app}` の実測は §14-5 ⑵ の cuda と **1 バイトも違わない**（112 檔・111,138,439 B）。

**⑵ 設定**＝走の前に `%LOCALAPPDATA%\irodori-tts-ywk\settings.json` を置いた
（`port=18097`・`autoStartServer=false`・`firstRunCompleted=false`・`warmup=false`・`showMemoryPanel=true`・
`readyTimeoutSeconds=600`。**`variant` は書かない**＝ウィザードの既定を見るため）。
※ `readyTimeoutSeconds` の出荷既定は 0（＝変種の既定）で、600 はこの席が置いた値である（上限を上げただけで
測った ready の秒には影響しない）。

**⑶ ウィザード**（窓は起動 1.24 s で自分から開いた＝`MainWindow.OnLoaded` の `NeedsFirstRun`）

```
[wizard] step = 1 / 7  初回取得の前に（第三者物の通知）
[wizard] notices shown = 16509 chars, file = 16509 chars
[wizard] accept enabled = True
[wizard] step = 2 / 7  実行系の種類を選ぶ
[wizard] variant choices = cu130 | cu126 | cpu
[wizard] picked   = cu126
[wizard] name     = CUDA 12.6（ドライバ 528.33 以上）
[wizard] note     = NVIDIA の GPU で動きます。ドライバの版が下限に届いているか下の行を確かめてください。
[wizard] driver   = ドライバの版が読めませんでした（CUDA 12.6（ドライバ 528.33 以上） は 528.33 以上が要ります）。
[wizard] size     = 取得 5.93 GiB・必要な空き 14.45 GiB（実行系 2.58 GiB・モデル 3.33 GiB・vc_redist 24.4 MiB）
```

- **通知は檔と 1 文字も違わずに出た**（16,509 chars＝§14-5 ⑴ の I-5 と同値）＝裁定 46。
- **`ReleaseFlavors.Detect` は CUDA 版で cu130／cu126／cpu の 3 択を出した**＝設計書 §1-3 ⒝ の狙いどおり。
- **ドライバ検査の逐語が「読めませんでした」である**＝本物の NVIDIA 無し機（`nvidia-smi` が無い）の姿。
  **止めずに勧めるだけ**（裁定 4）＝この段では先へ進めた。

**⑷ 段ごとの時計**（`FirstRunStepNumber` の変わり目で採った）

| 段 | 逐語 | 所要 | 実測 |
|---|---|---|---|
| 3/7 取得（実行系） | `実行系を取得しました（102 件）。` | **21.42 s** | torch 2.41 GB を 100.4〜183.3 MB/s で |
| 4/7 展開 | `展開しました（25049 檔・4.33 GB）。` | **36.31 s** | — |
| 5/7 取得（モデル） | `モデルを取得しました。` | **5.67 s** | **再取得 0**（既存 HF キャッシュ＝§15-1 ⑹） |
| 6/7 起動の確認 | `サーバを起こせませんでした（状態タブの理由を見てください）。` | **4.64 s** | **失敗** |

取得の逐語（`wizard-progress.log` から抜粋・29 行）＝

```
10:44:01 [3/7] 検証：pyaml　26.6 KB / 26.6 KB
10:44:02 [3/7] 接続：torch　0 B / 2.41 GB
10:44:03 [3/7] 取得：torch　102.6 MB / 2.41 GB　100.4 MB/s　残り 24 秒
10:44:12 [3/7] 取得：torch　1.61 GB / 2.41 GB　183.3 MB/s　残り 5 秒
10:44:18 [3/7] 検証：torch　2.41 GB / 2.41 GB
10:44:20 [3/7] 完了：torch　2.41 GB / 2.41 GB
```

**受け入れ条件 D-5 の「bytes／ETA」は実弾で出た**（速度と残り秒まで）。

**⑸ 起動の段で門が断った（この走の結論）**

```
[final] state   = 失敗
[final] reason  = cu126 はこの機体で GPU を見られません（is_available=False・device_count=0・torch cuda 12.6）。cpu の変種に切り替えてください。
[torch] torch 2.10.0+cu126 cuda 12.6 hip None is_available False device_count 0
[serve] port 18097 listening = False
```

`[torch]` の行は、ウィザードが展開した `%LOCALAPPDATA%\irodori-tts-ywk\runtime\cu126\python.exe` を
**この席が直に叩いて採った**（門が見たのと同じ検分）。**`import torch` は通る**——落ちるのは
`is_available()` だけである。

**⑹ 後始末**＝アンインストールは exit 0・`Removed all? Yes`・
`Defaulting to No for suppressed message box (Yes/No):` が **2 行**（M-8＝2 つの問いがどちらも「残す」に落ちた）。
`{app}`・Uninstall 鍵・`.lnk` は 3 つとも消えた。**データ樹は消えていない。**

### 15-2 **門の挙動を読み違えていた**（**この席の第 1 の発見**・断定）

依頼文（と便 E の通し設計の前提）は「**cu126 は検分が読めなくても注意 1 行で起こす**設計なので、
この機体では `is_available=False` で起動し CPU で合成する筈」だった。**これは誤りである。**
`Services/Gpu/VariantGate.cs` の `Decide` は上から順に、

1. ⑴ `cpu` 変種＝門を通さない。
2. ⑵ `probe is not null && probe.Observed && !probe.GpuUsable` → **`Allow=false`**（変種を問わない）。
3. ⑵-b `RequiresObservedProbe(name) && (probe is null || !probe.Observed)` → cu130／cuda だけ `Allow=false`。
4. ⑶ ドライバ閾。⑷ 注意 1 行（`probe is null` か `!probe.Observed` のとき）。

**「注意 1 行でそのまま起こす」は ⑵-b をすり抜けた後の枝**＝**検分が読めなかったとき**にしか効かない。
この機体では検分は**読めた**（`Observed=True`・`Available=False`・`DeviceCount=0`）ので、
**2 番目の枝が先に当たり、cu126 も止まる**。`ServerProcess.StartAsync` はその `Reason` で `Fail` を返し、
ウィザードの `RunStartAsync` は偽で返る（＝`FirstRunCompleted` は焼かれない）。

**これは実装の瑕疵ではなく、裁定 88 ⑴ の逐語（「`is_available()=False` か `device_count=0` なら**起動しない**で
理由 1 行と勧める変種を出す」）そのままの挙動である。**本席は 1 檔も直していない。
帰結だけが動く＝**裁定 90 ⑴ の W-1「cu126 の CUDA 版インストーラをこの機体に入れて初回取得 → CPU で 200 が返るか」は、
製品の路では最後まで走れない**（門が「cpu の変種に切り替えてください」で止める）。§15-4 で別経路から埋めた。

### 15-3 走 2＝Radeon 版・rocm-gfx1151（**断定**・0 failure）

```
[install] irodori-tts-ywk-setup-v0.1.0-radeon.exe exit=0 sec=2.71 log=612 lines
[install] {app} = C:\Users\mugonkun\AppData\Local\Programs\irodori-tts-ywk-radeon
[install] {app} files=112 bytes=111112725
[wizard] variant choices = rocm-gfx1151 | cpu
[wizard] name     = Radeon gfx1151（未保障・bf16 固定）
[wizard] note     = Radeon（ROCm）版です。精度は bf16 に固定されます（未保障・gfx1151 で確認済み）。
[wizard] driver   = ドライバの下限はありません。
[wizard] size     = 取得 4.79 GiB・必要な空き 9.55 GiB（実行系 1.44 GiB・モデル 3.33 GiB・vc_redist 24.4 MiB）
```

setup は 85,000,859 B・`{app}` は 112 檔 111,112,725 B（§14-5 ⑴ の radeon と **1 バイトも違わない**）。
**2 つの版は AppId も導入先も別**で、同じ日に同じ機体へ順に入って干渉しなかった（設計書 §1-4）。

| 段 | 逐語 | 所要 |
|---|---|---|
| 3/7 取得 | `実行系を取得しました（108 件）。` | **16.45 s** |
| 4/7 展開 | `展開しました（26523 檔・4.25 GB）。` | **27.25 s** |
| 5/7 モデル | `モデルを取得しました。` | **3.81 s** |
| 6/7 起動 | `サーバが起動しました。` | **39.26 s** |
| 7/7 完了 | `初回取得が終わりました。` | — |

**ウィザードの記録（`Trail`）8 行が全部読めた**（**断定**・UIA で採った逐語）＝

```
通知に同意しました。
変種＝Radeon gfx1151（未保障・bf16 固定） を選びました。
msvcp140.dll は既に在る（14.51.36247.0）ので飛ばす。
実行系を取得しました（108 件）。
展開しました（26523 檔・4.25 GB）。
モデルを取得しました。
サーバが起動しました。
初回取得が終わりました。
```

**3 行目が vc_redist の実測である**＝この機体は `System32\msvcp140.dll` を持つので**1 バイトも落とさず飛ばした**
（版まで名乗る）。見積り行は 24.4 MiB を数に入れていたが、**注文には入っていない**（取得は 108 件＝
python-embed 1 ＋ wheel 107）。設計書 §6-1「インストーラは検査しない・ランチャが判定する」の路が実弾で通った。

**⚠ 走 2 の「取得 16.45 s」は clean な初回取得ではない**（**断定**・honest な但し書き）。
`cache/` は走 1 の 102 檔（2,771,919,874 B）を抱えたままで、走 2 が終わって 110 檔（4,139,511,669 B）＝
**新しく落ちたのは 8 檔・1,367,591,795 B だけ**である（差分の実測。8 檔＝`rocm_sdk_core` 758,050,981・
`amd_torch_device_gfx115x` 187,800,925・`rocm_sdk_device_gfx1151` 139,346,623・`rocm_sdk_libraries` 116,831,755・
`torch-2.13.0+rocm10.0.0` 113,440,240・`amd_torch_device_gfx1151` 50,010,016・`torchaudio-…+rocm` 2,086,475・
`rocm-10.0.0.tar.gz` 24,780）。**残る 100 檔（180,238,175 B）は走 1 の cache に当たって「取得済み」で通った**
（`FetchPlanner` は檔名＋版で当てる）。⇒ **走 2 の実効の線速は 1,367,591,795 B / 16.45 s ＝ 83.1 MB/s**、
走 1 は 2,771,919,874 B / 21.42 s ＝ **129.4 MB/s**（検証の時間込み）。**まっさらな機体の走 2 はもっと掛かる。**

**起動後**＝

```
[torch] torch 2.13.0+rocm10.0.0 cuda None hip 7.15.26333 is_available True device_count 1
[serve] port 18098 listening = True
[http] POST /v1/audio/speech -> 200  bytes=145964  RIFF/WAVE=True
[memory] {"device":"cuda:0","allocated":2065810944,"reserved":2382364672,"max":3905996800,
          "gpu_total":107090132992,"gpu_free":103972442112,"gpu_used":3117690880,
          "latents":{...7 名...},"latents_total":686962,"sampled_at":"2026-09-05T01:48:15.01Z"}
```

wav の実物＝**48,000 Hz・モノラル・16 bit・データ 145,920 B＝1.52 s**。
アンインストールは exit 0・鍵と `{app}` と `.lnk` が消え・**データ樹は残った**。

### 15-4 **W-1（acceptance §3 の 6）を埋めた**（**この席の第 2 の発見**・断定）

問い（原文）＝「**CUDA wheel を入れた NVIDIA 無し機で CPU 合成が 200 で返るか**」。
門が製品の路を塞ぐので（§15-2）、**ウィザードが実際に展開した cu126 の実行系**に対して、
`ServerEnvironment.Build` が `device=cpu` のときに作るのと同じ env を組み、wrapper を直に起こした
（私設ポート **18099**・`{app}\server` は CUDA 版 setup を入れ直して用意し、走の後に消した）。

wrapper の逐語（`server-stderr.log`）＝

```
ywk_server 0.1.0 upstream=8224daf/841fb7c variant=cuda device=cpu/cpu precision=fp32
INFO:irodori_openai_tts.runtime:checkpoint download/cache lookup completed in 0.41s
INFO:irodori_openai_tts.runtime:runtime loaded in 10.36s
ywk_server: device actual=cpu dtype=float32 name=None uuid=None pci_bus_id=None
INFO:     Uvicorn running on http://127.0.0.1:18099 (Press CTRL+C to quit)
INFO:irodori_openai_tts.app:speech synthesis started: model=irodori-tts voice=デフォルト format=wav chars=16
INFO:irodori_openai_tts.app:irodori runtime: [runtime] start synthesize model_device=cpu model_precision=fp32 codec_device=cpu codec_precision=fp32 silentcipher_watermark=True mode=independent seconds=None steps=10 seed=random candidates=1 decode_mode=sequential
INFO:irodori_openai_tts.app:irodori runtime: info: predicted duration frames=79.2, scale=1.000, using_frames=79 (3.160s).
INFO:irodori_openai_tts.app:irodori runtime: [runtime] tokenize_text: 1.5 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] prepare_reference: 0.1 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] predict_duration: 1089.3 ms
INFO:irodori_openai_tts.app:irodori runtime: [runtime] sample_rf: 2495.2 ms
```

| 測った物 | 実測 |
|---|---|
| `/health` 200 ＋ `runtime.loaded=true` まで | **14.16 s**（うち `runtime loaded in` は **10.36 s**） |
| `/ywk/status.device` | `configured cpu`／`codec_configured cpu`／**`actual cpu`**／`precision fp32`・name/uuid/pci_bus_id は null |
| `POST /v1/audio/speech`（本文 16 字・`num_steps` 10・話者は既定） | **200** |
| 返った本体 | **303,404 B**・先頭 `RIFF`＋`WAVE`・48,000 Hz モノラル 16 bit・データ 303,360 B＝**3.16 s** |
| HTTP の所要 | **4.56 s** ⇒ **RTF_http ≈ 1.44** |

**⇒ 答え＝はい。本物の NVIDIA 無し機で、cu126 の wheel の torch は CPU で読み込め、合成は 200 と wav を返す。**
裁定 83 の代用測定（RTX 3090・537.58・`CUDA_VISIBLE_DEVICES=-1` で cu126 は 2 射とも 200）と**同じ結論**で、
今度は**代用ではない**（この機体に NVIDIA のドライバは 1 本も無い）。
※ この経路は**製品の路ではない**（ランチャは `device=cuda:0` を渡すので門が止める）。
　acceptance §3 の 6 に書ける文は §15-8 に置いた。

### 15-5 「試す」の 1 射（製品の路・Radeon 版）

#### 15-5-1 走 2 の中の 1 射（**採り損ねた**・正直な記帳）

走 2 の台本は `TryResultText` が**空でなくなったら**読む作りだったが、この欄の初期値は
`UiText.Missing`（em ダッシュ 1 文字）で**空ではない**。よって 2 秒後の 1 回目の標本で抜けてしまい、
**所要／RTF／seed の行を 1 度も読めていない**（読めたのは `合成しています…` だけ）。
**台本の欠陥であって製品の欠陥ではない**（同じ走の生 HTTP は 200 で wav を返している＝§15-3）。

#### 15-5-2 走 2b＝入れ直して製品の路で 2 射（**断定**）

初回取得は済んでいるので（`settings.json` に `firstRunCompleted: true`・
`acceptedNoticesSha256: 01700d5ad9b2960050286e9daf3d70527e3aacf7a6609466ceb2c126dbc538a7`）、
Radeon 版を入れ直し → ランチャ起動 → **「サーバ起動」を押す** → 待機 → 「試し撃ち」で 2 射した。

```
[shot] state = 待機  after 31.64 s
[shot] device  = cuda:0／AMD Radeon(TM) 8060S Graphics（gfx1151）／bf16
[shot] variant = Radeon gfx1151（未保障・bf16 固定）
[shot 1] result  = 所要 6.71 秒／出力 3.20 秒／RTF 2.10／seed 6283283498982061075
[shot 2] result  = 所要 267 ms／出力 3.20 秒／RTF 0.08／seed 2756786406996769807
[shot ?] message = 再生中（音量を −16 dBFS 相当に揃えています）。
[shot] memory band = 使用量 1.92 GB／占有量 2.36 GB／GPU 全体 2.96 GB / 99.74 GB（最大 3.01 GB）
[shot] latent = ON（焼いた話者 11 名・合計 1.1 MB）
[shot] status.device = {"configured":"cuda:0","codec_configured":"cuda:0","actual":"cuda:0","precision":"bf16",
                        "name":"AMD Radeon(TM) 8060S Graphics","uuid":"30303030-3031-3937-3030-303030303030",
                        "pci_bus_id":"197","hip":"7.15.26333","gcn_arch":"gfx1151"}
```

- **ready 31.64 s**（MIOpen db は温・`docs/radeon.md` §7 の 26.1〜29.0 s と同じ帯）。
- **プロセスの 1 射目は RTF 2.10**（6.71 s で 3.20 s 出力）、**2 射目は RTF 0.08**（267 ms）。
  便 C（2）の「プロセスで最初の 1 発だけは潜在でも RTF 0.726〜0.848」より**重い**＝
  ⑴ 本文が違う（16 字・出力 3.20 s）⑵ 導入直後で `__pycache__` が無く import が冷えている、
  のどちらか（**この席では切り分けていない＝推測**）。**2 射目以降は帯に戻る。**
- **記憶帯は 4 つの数を全部出した**（裁定 87 ⑴）＝「未対応」でもダッシュでもない。
- **参照潜在は 11 名すべて焼けていた**（初回取得の起動の段で `YWK_PRECOMPUTE_ON_START` が効く＝裁定 65）。

### 15-6 時計と回線（受け入れ条件「オンライン導入 ≤ 10 分（100 Mbps 級）」の材料）

**回線の実測**＝ウィザードが出した瞬間速度は **100.4〜183.3 MB/s（≈0.80〜1.47 Gbps）**（torch 2.41 GB の 16 s の間）。
段まるごとの実効は **走 1＝129.4 MB/s（≈1.04 Gbps・2,771,919,874 B を 21.42 s・sha256 の検証込み）**、
**走 2＝83.1 MB/s（≈0.66 Gbps・1,367,591,795 B を 16.45 s）**。
「100 Mbps 級」の 7〜13 倍の線であり、**この機体の実測をそのまま条件の判定に使えない**。

| 段 | CUDA 版（cu126） | Radeon 版（rocm-gfx1151） |
|---|---|---|
| setup（無人） | **2.74 s** | **2.71 s** |
| ウィザードが開くまで | 1.24 s | 1.20 s |
| 通知＋変種（人の操作） | 3.4 s | 3.4 s |
| 取得（実行系） | **21.42 s**（102 檔・2,771,919,874 B） | **16.45 s**（108 檔・**うち新規 8 檔 1,367,591,795 B**） |
| 展開 | **36.31 s** | **27.25 s** |
| モデル | **5.67 s** | **3.81 s** |
| 起動 | 4.64 s（失敗） | **39.26 s** |
| **導入〜完了の合計** | —（失敗） | **97.84 s** |

**100 Mbps（12.5 MB/s）に引き直した推定**（**推測**・この機体では未実射）＝

- **モデルを取り直す機体**（本当のまっさら）＝cu126 5.93 GiB → 落とすだけで **≈8.49 分**、
  rocm 4.79 GiB → **≈6.86 分**。展開（36.3／27.3 s）と起動（rocm の実測 39.3 s で代用）は回線に依らないので足すと
  **cu126 ≈9.7 分・rocm ≈8.0 分**＝**≤ 10 分はぎりぎり満たす**。
- **この走の実態**（モデルが既に在った）＝落ちたのは cu126 2,771,919,874 B＝**≈3.70 分**・
  rocm は cache が効いて 1,367,591,795 B＝**≈1.82 分**（cache が空なら 1,547,829,970 B＝**≈2.06 分**）。

⇒ **条件「オンライン導入 ≤ 10 分（100 Mbps 級）」は、この機体の実測では判定できない**（速すぎる）。
　100 Mbps を本当に測るなら帯域を絞る要があり、**本席は絞っていない**。上の引き直しは算術であって実測ではない。

### 15-7 **利用者操作は 9 だった**（**この席の第 3 の発見**・断定・卓へ）

走 2（完走した方）で数えた**ウィザードの押下は 9 回**＝

| # | 操作 | 出所 |
|---|---|---|
| 1 | 同意のチェックを付ける | `FirstRunAcceptCheck` |
| 2 | 「同意して次へ」 | `FirstRunNextButton`（Notices） |
| 3 | 変種を選ぶ | `FirstRunVariantCombo` |
| 4 | 「この構成で取得を始める」 | Next（Variant） |
| 5 | 「次へ」（取得 → 展開） | Next（Download） |
| 6 | 「次へ」（展開 → モデル） | Next（Install） |
| 7 | 「次へ」（モデル → 起動） | Next（Models） |
| 8 | 「次へ」（起動 → 完了） | Next（Start） |
| 9 | 「試し撃ちへ」 | Next（Done） |

setup は無人で撃ったので**その 3 クリックは数に入っていない**（対話で撃てば ＋3）。
設計書 §4 の表は「取得 → 展開 → モデル → 起動」を**1 操作に畳んで**数えているが、
`FirstRunViewModel.NextAsync` は**段ごとに「次へ」を要る**（`case Download: Step = Install; await RunStepAsync(...)` の形）。
**§4 の 6 は実装と合っていない。**

これは**設計書側か実装側のどちらかを直す話**であり、`launcher/` は本席の停止域なので**直していない**。
手当ての案 2 つ（**未実射＝推測**）＝
⒜ 段が成功したら自動で次の段へ進み、**失敗した段でだけ止める**（＝押すのは 4 回で済む＝1・2・3・4 と、
　最後の「試し撃ちへ」で 5）。⒝ `docs/acceptance.md` の数え方を「押下」ではなく「利用者が判断する場面」に改める
（同意・変種・起動の 3 つ＝§4 の表の意図はこちらだと読める）。

### 15-8 acceptance に書ける文（**卓が採るなら**の下書き）

**§3 の 6（W-1）**——「未確認」を外せる。

> 6 | ~~CUDA wheel を入れた NVIDIA 無し機で CPU 合成が 200 で返るか（`36` W-1）~~ **埋まった（便 E（2）・2026-09-05）**
> | **本物の NVIDIA 無し機（Radeon 8060S・NVIDIA ドライバ 0 本）で実射**＝CUDA 版 setup を入れて初回取得で
> cu126 を選び、torch 2.10.0+cu126 が **25,049 檔・4.33 GB** 展開されたところまでは通る。
> `import torch` は成功し `torch.cuda.is_available()=False`・`device_count()=0`。
> ⑴ **ランチャは cu126 を起こさない**＝変種の門（裁定 88 ⑴）が
> 「cu126 はこの機体で GPU を見られません（is_available=False・device_count=0・torch cuda 12.6）。
> cpu の変種に切り替えてください。」で止める（**設計どおり**）。
> ⑵ **その実行系を `IRODORI_MODEL_DEVICE=cpu` で直に起こせば合成は通る**＝`runtime loaded in 10.36s`・
> `device actual=cpu dtype=float32`・`POST /v1/audio/speech` が **200**・**303,404 B の RIFF/WAVE**
> （48 kHz モノラル 16 bit・3.16 s）・HTTP 所要 4.56 s（RTF 1.44・fp32・10 steps）。
> 裁定 83 の代用測定と同じ結論。**なま値**＝`build/out/probe-log/`（本走はスクラッチパッド）。 | **埋まった**

**導入行**——「利用者操作 ≤ 6」は**満たしていない**（§15-7）。

> 導入 | 利用者操作 ≤ 6 …（略）… | **未達（便 E（2）実測）**＝初回取得ウィザードだけで **9 押下**
> （同意 1・同意して次へ 1・変種 1・取得開始 1・次へ 4・試し撃ちへ 1）。setup を対話で撃てば ＋3。
> 段ごとに「次へ」が要るのは `FirstRunViewModel.NextAsync` の作りで、設計書 §4 の「4 段で 1 操作」は成り立たない。

**サイズ行**——「導入後 ≤ 7.0 GB」の材料（**実測**）＝

| 変種 | 落としたバイト | 展開後（`runtime/<変種>`） | 倍率 |
|---|---|---|---|
| cu126 | 2,760,786,268（2.571 GiB） | **4,667,092,442 B（4.35 GiB）**（26,057 檔） | **1.69** |
| rocm-gfx1151 | 1,536,696,364（1.431 GiB） | **4,578,533,100 B（4.26 GiB）**（27,533 檔） | **2.98** |

`{app}` ＋ モデル ＋ 実行系 ＋ 話者を足すと（**cache を消した後**）＝
**cu126 8,383,192,608 B（8.38 GB・7.81 GiB）**／**rocm 8,294,607,552 B（8.29 GB・7.72 GiB）**。
**どちらも「導入後 ≤ 7.0 GB」を超える。**cache を残せば cu126 11,155,112,482 B・rocm 9,662,199,347 B。
Q-E2 の「cu130 の展開後を RTX 機で測ってから裁定し直す」はそのまま生きるが、
**cu126 と rocm の実測は本節で埋まった**（cu126 の倍率 1.69 は `FetchPlanner.ExpansionFactor=3.3` より**ずっと小さい**
＝`.whl` の中身が既に非圧縮の DLL だから。rocm の 2.98 は 3.3 に近い）。

### 15-9 機体の前後（**断定**・後片付けの証拠）

**導入 0・listen 0・残骸 0**（走り終えた後の実測）＝

```
%LOCALAPPDATA%\Programs\irodori*                     0 件
HKCU\...\Uninstall\{F228543A-...}_is1 / {ECA98712-...}_is1   0 件
スタートメニューの irodori*.lnk                        0 件
IrodoriTtsYwk.Launcher のプロセス                      0
ywk_server を抱えた python.exe                        0
8088 / 7861 / 18088 / 18097 / 18098 / 18099 の LISTENING  0
```

**データ樹**＝`%LOCALAPPDATA%\irodori-tts-ywk`・**53,769 檔・16,990,584,628 B**（残した）

| 枝 | 走の前（10:43:51） | 走の後（10:52） | 差 |
|---|---|---|---|
| `models` | 25 檔 **3,571,256,318 B** | 30 檔 **3,571,274,837 B** | **＋18,519 B**（`refs/main` 等の小檔だけ。**モデル本体は 1 バイトも取り直していない**） |
| `miopen` | 3 檔 **421,875 B** | 3 檔 **485,049 B** | ＋63,174 B（db が育った＝`cache` 417,792 ＋ `db` 67,257） |
| `voices` | 0 檔 **0 B** | 35 檔 **33,686,890 B** | ＋33,686,890 B（`refs/` 11 檔 32,571,364＋`latents/` 22 檔 1,110,478＋`voices.json` 965＋`voices.ywk.json` 4,083） |
| `runtime` | 無し | 53,590 檔 **9,245,625,542 B** | cu126 26,057 檔 **4,667,092,442 B** ／ rocm-gfx1151 27,533 檔 **4,578,533,100 B** |
| `cache` | 無し | 110 檔 **4,139,511,669 B** | 取得した `.whl`／`.zip`／`.tar.gz`（両変種ぶん） |
| `logs` | 無し | 0 檔 0 B | — |
| `settings.json` | 無し | 1 檔 **641 B** | ランチャが焼いた（`firstRunCompleted: true`・`variant: rocm-gfx1151`・`port: 18098`） |

**`models`／`miopen` の中身は壊していない**（増えただけ）。**`voices` は 0 檔から増えた**＝
`LauncherComposition.PrepareVoices` が配布樹のプリセット 11 檔を写し、起動の段の事前計算が潜在を焼いた
＝**製品が自分で書いた物**であり、この席が置いた物ではない。
**退避（`irodori-tts-ywk.e2-backup`）は取っていない**＝この走に**削除の段が 1 つも無い**からである
（アンインストールは 2 回とも `/SUPPRESSMSGBOXES` で「残す」に落ちた＝§15-1 ⑹）。
C: の空き＝走の前 1,158,011,039,744 B → 走の後 1,144,217,219,072 B（**−13.79 GB**）。

**残した物のバイト**（次の便の材料＝依頼文の許し）＝`runtime` **9,245,625,542 B**・`cache` **4,139,511,669 B**。
`cache` を消せば 4.14 GB 戻る（**消していない**）。

### 15-10 卓へ（5 件・本席の停止域の外）

1. **【最重要】裁定 90 ⑴ の W-1 の書き方を直す要がある。**「cu126 の CUDA 版インストーラを入れて初回取得 →
   CPU で 200 が返るか」は、**製品の路では成立しない**（門が起動を断る＝設計どおり）。
   実測で言えるのは「**取得と展開は通る・起動は断られる・その実行系を CPU で起こせば 200 と wav が返る**」の 3 つ。
   `docs/acceptance.md` §3 の 6 の書き換え案は §15-8。
2. **利用者操作 ≤ 6 は未達（実測 9）。**設計書 §4 の表を直すか、`FirstRunViewModel` を「成功した段は自動で進む」に
   変えるか、条件の数え方を改めるか。**launcher/ は本席の停止域。**
3. **「導入後 ≤ 7.0 GB」は cu126 でも rocm でも超える**（**8.38／8.29 GB**・cache を消した後の実測）。
   Q-E2 の裁定に **cu126 1.69 倍・rocm 2.98 倍**という実測 2 点を足せる。
   `FetchPlanner.ExpansionFactor=3.3` は **cu126 では過大**（見積りの「必要な空き 14.45 GiB」は実際には要らなかった）。
4. **「オンライン導入 ≤ 10 分（100 Mbps 級）」はこの機体では判定できない**（実測 ≈1.29 Gbps）。
   算術での引き直しは §15-6（cu126 ≈9.7 分・rocm ≈8.0 分＝**推測**）。本当に測るなら帯域を絞る走が要る。
5. **初回取得のキャッシュ（`cache/` 4.14 GB）を誰が消すか**は決まっていない。
   裁定 90 の「cache は初回の合成が 200 で返った後にランチャが消す（便 D（3）へ）」は**まだ実装されていない**
   （走の後も 110 檔 4,139,511,669 B が残っている＝実測）。

### 15-11 まだ撃っていない（＝この席の外・または限界）

- **E-1（vc_redist 欠落機）**＝撃っていない。実測できたのは「在る機体で飛ばす」路だけ
  （`msvcp140.dll は既に在る（14.51.36247.0）ので飛ばす。`）。裁定 90 ⑶ のまま未確認。
- **cpu 変種の初回取得**＝撃っていない。門が勧める先（`cpu の変種に切り替えてください`）を実際に選ぶ走は未了。
- **まっさらな機体の初回取得**＝**していない**。`models`（3.33 GiB）は走の前から在ったので、
  モデルの段は 5.67 s／3.81 s で済んでいる。**モデルを本当に落とす走はこの席では 1 度も走っていない。**
- **走 1 と走 2 はデータ樹を共有した**（`voices`・`miopen`・`cache` を走 2 が引き継いだ）＝
  2 版の同居と干渉の検分ではない（同居させたのは `{app}` ではなくデータ樹だけ）。
- **対話での導入**（3 クリック）＝撃っていない。すべて `/VERYSILENT`。
- **帯域を絞った走**・**別機体での再現**＝していない。この機体 1 台・この回線 1 本の実測である。

---

## 16. 是正席（便 E（2）是正）の記帳（2026-09-05 11:20〜11:55・Radeon 機・実装サブ席 opus）

> 本節は**この席が実際に撃った物だけ**を書く。読んだだけ・推したものは「推測」と明記する。
> 書いた檔は `build/installer-build.ps1`・`build/assemble-app.ps1`・`probe/e-install-probe.ps1`・
> `installer/irodori-tts-ywk.iss`・`docs/install.md`・`installer/README.md`・本節の 7 つだけ。
> `launcher/`・`server/`・`upstream/`・`research/`・`tools/`・`docs/acceptance.md`・`probe/common.ps1` は**読むだけ**。
> `git commit` していない。**外部へ 1 バイトも取りに行っていない。**管理者昇格を 1 度もしていない。
> 8088・7861・18088 を 1 度も起こしていない（走の出口の実測＝4 ポートとも `listening = False`）。
> **利用者データ樹**＝走の前 **53,769 檔・16,990,584,628 B**／走の後 **53,769 檔・16,990,584,628 B**
> （名前と長さの一覧も突合して**差 0**）。壊す走はすべて台本の退避（`irodori-tts-ywk.e2-backup` への `Rename-Item`）の下で撃った。

### 16-1 置いた檔（**断定**・実測）

| 檔 | バイト | 行 | 形 |
|---|---|---|---|
| `build/installer-build.ps1` | 46,881 | 975 | ASCII のみ・CRLF 975 対・単独 LF 0 |
| `build/assemble-app.ps1` | 27,153 | 524 | 同上 |
| `probe/e-install-probe.ps1` | 89,839 | 1,761 | 同上（日本語は `New-JpText` の型のまま） |
| `installer/irodori-tts-ywk.iss` | 29,727 | 458 | UTF-8 **BOM 付き**・CRLF 458 対・単独 LF 0 |
| `docs/install.md` | 21,002 | 237 | UTF-8 BOM 無し・LF |
| `installer/README.md` | 11,679 | 106 | 同上 |

parse は ps1 3 本を **PS 5.1（5.1.26100.9168）と PS 7.6.5 の両方**で `[Parser]::ParseFile` に掛けて **errors=0**。
`.iss` は ISCC 6.7.3 が 2 版とも `Successful compile`。

### 16-2 直した high 3 件（下の 2 節。16-2-2 は所見 2 本を 1 本の直しで閉じた）

#### 16-2-1 「門を 9 本とも通って exit 0、しかし動かない setup」（`build/installer-build.ps1`）

**再現（本席の実射・11:30:57）**＝`server\python312._pth.template` と `server\ywk_fetch_models.py` を退けて
`-Flavor cuda -SkipAssemble -SkipPublish`。**旧実装では**門 A-1 が drift を WARN にしか落とさず、
`.iss` が名指しで見る `server\` の檔は `ywk_server.py` の 1 本だけなので、9 本すべて `OK` で
`DONE: 1 installer(s), 9 gate(s), 0 failed.`（審査席の実測）。

**直し 2 本**。⑴ **A-1 の檔数不一致を WARN から失敗に**（総バイトの drift は WARN のまま＝
licenses や ledger の 1 文が動くだけで落ちる門にはしない）。⑵ **門 A-7 を新設**＝
「名指しの檔が配布樹に在って空でない」。名指しは 6 つ＝`server\python312._pth.template`（`AppPaths.cs:92`
逐語 `public string PthTemplatePath => Path.Combine(ServerDir, "python312._pth.template");`）・
`server\ywk_fetch_models.py`（`Ledger.cs:280`＝変種の python で子プロセス実行する檔）・`server\ywk_server.py`・
`server\ywk_params.py`・`voices\presets.json`・`licenses\first-run-notices.md`。
**2 枚目の網**＝`build/out/assemble-log/assemble-app.json` の `wrapper_files`（実測＝4 件）を読み、
名簿に無い wrapper 檔も見る。同檔の `files` が配布樹の檔数と食い違えば「その報告書は前の走のもの」と WARN で言う。

**釘（同じ壊し方をもう一度・11:30:57・0.86 s）逐語**

```
FAIL NG   cuda A-1 distributable tree has the recorded file count -- the tree lost 2 file(s): 106 file(s), 37094855 B (expected 108 / 37111434). Find out WHICH file (gate A-7 names the load-bearing ones), then either fix the tree or move $ExpectedAppFiles in build/installer-build.ps1 on purpose
WARN A-7 assemble-app.json says 108 file(s) but the tree has 106: the second sheet is from an earlier assemble
FAIL NG   cuda A-7 the named load-bearing files are in the tree -- 2 problem(s): server\python312._pth.template is MISSING from the distributable tree ; server\ywk_fetch_models.py is MISSING from the distributable tree
FAIL gate A failed 2 time(s) for cuda: ISCC was NOT run and no setup was written. Gate B did not run.
FAIL DONE with 2 failed gate(s) out of 7.
EXIT=2   setup-on-disk=False   SHA256SUMS.txt に cuda 行 0 本
```

**あわせて空の配布樹でも撃ち直した**（`build/out/app` を退避して空の樹を置いた・11:44:40）＝
`A-1・A-2・A-3・A-7` の **4 本**が落ちて **EXIT=4**、`A-4`／`A-5` は
`-- THE TREE IS EMPTY, this gate inspected nothing (A-1 failed)` を detail に足して通る（low の 6 への手当て）。
退避した樹は戻して **108 檔・37,111,434 B**（1 バイトも違わない）。

#### 16-2-2 「アンインストールの完了を待たずにデータ樹を見る」（`probe/e-install-probe.ps1`）

**機構**＝Inno のアンインストーラは `_iu*.tmp` へ自分を写して**親が先に落ちる**。`{app}` は `usUninstall` で
既に消えている。ところが `.iss` の 2 つの問いは `usPostUninstall`＝**その後**。だから `$p.HasExited` でも
`{app}` の消滅でも早すぎる。**待つべき事象はアンインストールログの最終行 `Log closed.`**（`usPostUninstall` と
`DeinitializeUninstall` の後に書かれる）。

**直し**＝`Wait-UninstallFinished`（新設）＝ログを**共有読み**（`FileShare::ReadWrite | Delete`）で poll し、
最終の非空行が `Log closed.` で終わるまで待つ（上限 90 s）。あわせて
`User chose (Yes|No).` の行数を数え、**問うた数と一致するか**を段 8a／8b／8c の新しい釘に立てた
（`asked` は台本が窓を見た数・`answered` は**製品が記録した答えの数**＝別の証拠である）。

**釘（本席の走の中で毎回記帳される実測）**＝`[uninst-ui]` 行に
`(at the parent exit the log had N line(s) = what the old code judged on)` を出すようにした。
最終の全段走（11:48）の逐語＝

```
[uninst-ui] asked=2 answered=2 logClosed=True sec=1.85 log=173 lines (at the parent exit the log had 173 line(s) = what the old code judged on)
[uninst-ui] asked=2 answered=2 logClosed=True sec=1.78 log=173 lines (at the parent exit the log had 172 line(s) = what the old code judged on)
[uninst-ui] asked=1 answered=1 logClosed=True sec=13.12 log=171 lines (at the parent exit the log had 169 line(s) = what the old code judged on)
```

＝**同じ 1 走の中で、8a は間に合い・8b は 1 行・8c は 2 行足りない**。審査席が見た「167 か 169 かが勝敗と一致する」
不安定は、これが正体である（間に合うこともある＝だから 3 走中 1 走は緑だった）。
`-Steps 8` を **3 走**撃って `=== 0 failure(s) of 22 ===` ／ `0 of 22` ／ `0 of 22`＝**3 走とも緑**（各 41 s）。

**high 2 件目（8a／8c の釘が空砲）も同じ 1 本の直しで実弾になった**＝
「まだ在る」を見る段は、待ちが入った今は**削除が起きた後**の観測である。
それに加えて `logClosed` と `answered = asked` を段 8a／8b／8c に 1 本ずつ足したので、
待ちが効かなかった走は**必ず赤**になる（`0 failure(s) of N` の N も 3 本増えた）。

### 16-3 直した medium 6 件

| # | 檔 | 直し | 釘（逐語・実射） |
|---|---|---|---|
| ⑴ | `installer-build.ps1` | 門 B が落ちたら**弾いた setup.exe を棚から消す**（`Remove-YwkSumsLine` の隣） | B-1 を落とした走（`voices\__pycache__\sneaky.cpython-312.pyc`・11:31:32）＝`OK cuda B-3 ... the rejected setup (84986434 B) was deleted from ...` ／ `setup-on-disk=False`。B-2 を落とした走（プリセット 1 檔を 12 MiB の非圧縮に差し替え・11:36:07）＝`= 96413113 B (91.95 MiB); band 80740352-89128960 B` → `the rejected setup (96413113 B) was deleted` ／ `1 stale line(s) removed` ／ `setup-on-disk=False` |
| ⑵ | `irodori-tts-ywk.iss` | `RemoveDir(Data)` の前に **reparse point かを見る**（`GetFileAttributesW` を external 宣言して `$400` を見る `IsReparsePoint`）。junction なら畳まず `Log(...)` を残す | 使い捨ての `.iss` を ISCC で通して非昇格で実射（`InitializeSetup` で撃って `Result := False`＝1 檔も導入せず・鍵も作らず）。**対照（守りを外した版）**＝`GUARD FAILED: RemoveDir(link) returned 1` ／ `after: DirExists(link)=0 DirExists(real)=1 FileExists(real\settings.json)=1`＝**路が消えた**。**是正版**＝`IsReparsePoint(link)=1` ／ `IsReparsePoint(real)=0` ／ `IsReparsePoint(missing)=0` ／ `GUARD: RemoveDir was NOT fired at the junction` ／ `after: DirExists(link)=1 DirExists(real)=1`。※ 本物の樹での回帰＝段 8b の `the empty data root was folded by RemoveDir` が最終走でも **PASS**＝**普通のディレクトリは今も畳む** |
| ⑶ | `assemble-app.ps1` | `voices/voices.json` と `voices/voices.ywk.json` を `ConvertTo-Json` でなく**リテラルの UTF-8 テキスト**で書く（読み戻して `ConvertFrom-Json` で検算し、既定話者と `schema` を確かめてから通す） | **再現**（同じ入力を 2 ホストで `Write-YwkJsonFile` に渡した実測）＝`5.1.26100.9168 ConvertTo-Json voices.json = 80 B 1FE5488B44C33D26` ／ `7.6.5 ... = 50 B 54D5FB65A572A56E`。**釘**＝是正後に `assemble-app.ps1` を 2 ホストで背中合わせに走らせた実測＝`after-51 files=108 bytes=37111434 voices.json=50B 54D5FB65A572A56E voices.ywk.json=345B 699E22CC3E911A08` ／ `after-7 files=108 bytes=37111434 voices.json=50B 54D5FB65A572A56E voices.ywk.json=345B 699E22CC3E911A08`＝**1 バイトも違わない**。よって `installer-build.ps1` の `$ExpectedAppBytes` の **2 値を 1 値（37,111,434）に戻した** |
| ⑷ | `installer-build.ps1` | 例外死に **exit 64**（門の本数と衝突しない番号）。trap は途中までの門を持った**部分報告書**（`died=true`）も書く | `SHA256SUMS.txt` に読取専用を立てて実射（11:31:47）＝`OK cuda B-1` ／ `OK cuda B-2` の後に `FAIL installer-build DIED (not a gate): Exception calling "WriteAllText" ... is denied.` ／ `at at Write-YwkTextFile, ...Common.ps1: line 197` ／ `report (partial) = ...installer-build-20260905-113147.json` ／ **`EXIT=64`**。報告書の中身＝`died=True failed_gates=0 gates=9`。対照＝`throw` は素の PowerShell では **5.1 も 7 も exit 1**（実測）＝旧契約は門 1 本と見分けが付かなかった |
| ⑸ | `irodori-tts-ywk.iss` | `[Files]` の `Excludes: "__pycache__,*.pyc"` を **`licenses\*` と `voices\*` にも**付ける（3 行が同じ形になる） | **再現**（11:31:32・付ける前）＝`111 Compressing: line(s)`（良品 110）で `NG cuda B-1 ... a __pycache__ path was compressed into the setup: ...\voices\__pycache__\sneaky.cpython-312.pyc`。**釘**（11:35:31・付けた後・`voices\` と `licenses\` の 2 箇所に .pyc を置いて）＝`ISCC exit 0 after 11.79 s; 110 Compressing: line(s); 0 warning line(s)` ／ `OK cuda B-1`＝**1 檔も入らなかった** |
| ⑹ | `e-install-probe.ps1` | `needWork` の並びに **1 を足す**（据え付ける段はすべて teardown で取り外され、取り外しがデータ樹に届く唯一の操作である） | `-Steps 1` を実射（17.7 s）＝`[data] before: files=53769 bytes=16990584628` ／ `[data] moved aside -> ...irodori-tts-ywk.e2-backup` ／ `[data] restored -> ...` ／ `=== 0 failure(s) of 20 ===`＝**退避が取られるようになった**（旧実装では 1 檔も退避せずに本物の樹の上で導入と取り外しを撃っていた） |

**medium の残り 2 件も直した。**

- **前据え付け（段 3／5／`-IncludeAcl`）を try/catch で包んだ。**釘＝`-Steps 3 -SetupRadeon C:\zz-no-such-setup.exe`（13.0 s）＝
  `[FAIL] the pre-install for stages 3/5/-IncludeAcl threw :: setup.exe is missing: C:\zz-no-such-setup.exe` ／
  `[FAIL] stage 3 threw :: Cannot bind argument to parameter 'AppDir' because it is an empty string.` ／
  `=== summary ===` ／ `=== 2 failure(s) of 10 ===`・**EXITCODE=2**。
  旧実装は `=== summary ===` も failure 行も出さずに PowerShell の総称の 1 で終わっていた。
- **`Restore-DataTree` を finally の中の finally に移した。**teardown の点検 3 本（HKCU の読み・`Programs` の走査・
  `.lnk` の走査）を内側の `try` に入れ、その `finally` で復元する。**アンインストールの 2 本は外側のまま**＝
  データ樹に届く操作は「身代わりが置かれている間」に撃たねばならないので、順は変えていない。
  `Stop-AllLaunchers` も try/catch に入れた。

### 16-4 直した low（17 件のうち 12 件・残りは §16-7 と下の但し書き）

1. 門 B-1 の detail の `, 0 warning(s)` を**リテラルから実数へ**（`$isccWarnings.Count`）。
2. ISCC の警告を数える正規表現から **`Compressing:` 行を除いた**（路に "warning" の語が入っただけで 1 本と数えていた）。
3. 門 A で止めた走の締めの log 行を
   `artefact cuda = NONE on the shelf; stopped at gate A (irodori-tts-ywk-setup-v0.1.0-cuda.exe)` に（路・バイト・sha256 の空欄を並べない）。
   門 B で弾いた走も同じ形で `stopped at gate B (the setup was deleted, not shipped)` と言う。
4. 空の樹で `A-4`／`A-5` が `OK` と書く件＝detail に `-- THE TREE IS EMPTY, this gate inspected nothing (A-1 failed)` を足した。
5. `Remove-YwkSumsLine` が最後の 1 行を落としたとき、**0 バイトの `SHA256SUMS.txt` を残さず檔ごと消す**。
6. `.iss` の 1 問目の注釈から「`docs/install.md` §5-3 は実装と食い違う＝卓へ回した」という**古い記述を外した**（§12-8 で既に閉じた票である）。
7. 台本の入口「the machine starts with no irodori install」に **`%LOCALAPPDATA%\Programs\irodori*` の走査**を足した（前走の残骸を teardown で初めて咎める形をやめた）。
8. teardown のポート点検に **この走の私設ポート（既定 18097）**を足した。逐語＝
   `teardown: the reserved ports and this run private port are still quiet :: busy =  ; private 18097 listening = False`。

**あわせて台本の釘を 5 本、緩いところから締めた。**

- 「データ樹が戻った」の判定に **名前と長さの一覧**（`Get-DataTreeSnapshot` が作りながら誰も読んでいなかった `$names`）を使う＝
  最終走の逐語 `before files=53769 bytes=16990584628 :: after files=53769 bytes=16990584628 :: name/length differences=0`。
- 段 7 の「スタートメニューの .lnk が消えた」を**名指し**に＝`irodori .lnk before=1 after=0 (start menu total 14 -> 13)`。
- 段 2 の「準備完了頁にサイズの文がある」を `*106*` から**句そのもの**に＝
  `looking for "約 106 MiB" and a GiB figure; found size=True gib=True`。
- `Add-ExpectedFile` が源の檔を見つけられなかったとき、**期待が黙って小さくなる**のをやめた（`missing` に立てる）。
- 段 6・7・8a・8b・8c の**黙った早期 `return`** を、`{app} was found after the install` の**赤い記帳**に置き換えた
  （母数が黙って減るのを止めるため。通る走では 1 本も増えない）。

**直さなかった low が 5 件ある。**⑴〜⑷＝設計書 §3-2 の見出しと 2 つの条件文、および E2E 席の数字（§15-1）＝
**本席の担当 path の外**なので §16-7 に回した。⑸＝`Add-Step ... $true` の **`RECORDED` の 3 本**は**そのまま残した**。
名前が既に `RECORDED --` と言っており、これは「観測を記帳する行」であって釘ではない。
赤くなる条件を後から発明すると、測っていない期待を製品に課すことになる。
母数が 3 だけ膨らむ件は、この段落を読み替えの拠り所にしてほしい。

### 16-5 退けた所見（**0 件**）

**high 3 件・medium 8 件・low 17 件のうち、本席が再現を試みたものはすべて再現した。**
※ 「`Restore-DataTree` の手前が裸」（medium）だけは**構造の指摘**であって、現に落ちた走は本席でも 0＝
審査席の記述どおり。直しは入れた（16-3 末尾）。
※ 設計書 §3-2 の 3 件（見出しの「5 本」・A-3 の条件文・A-1 の「0 件なら exit 1」）と E2E 席の数字の件は
**檔が本席の担当 path の外**（§16-7 へ）。

### 16-6 最終の 2 射（**断定**・逐語）

**⑴ `installer-build.ps1 -All`（全段・pwsh 7.6.5・11:47:16〜11:47:50・34.04 s・EXIT=0）**

```
INFO distributable tree: 108 file(s), 37111434 B (35.39 MiB); 0 __pycache__/.pyc file(s) left out of the scan
OK   cuda A-1 distributable tree has the recorded file count -- 108 file(s), 37111434 B (expected 108 / 37111434)
OK   cuda A-2 preset speakers (decisions.md 37 / 88 (5)) -- voices\presets\*.wav = 11 (expected 11), voices\presets.json present = True
OK   cuda A-3 notice + the ledger set of this flavour -- ships README.md models.json python-embed.json vc_redist.json runtime-cu130.json runtime-cu126.json runtime-cpu.json ; must not ship runtime-rocm-gfx1151.json
OK   cuda A-4 no third party binary (extension whitelist, decisions.md 8) -- scanned 108 file(s) against 10 extension(s) + 6 named file(s); 0 outside
OK   cuda A-5 no file over 4 MiB (the wavs are named exemptions) -- cap 4194304 B; largest non-exempt = server\upstream\Irodori-TTS\uv.lock 1532654 B; 11 named exemption(s)
OK   cuda A-6 the published exe carries this version -- ProductVersion = v0.1.0 (want v0.1.0), FileVersion = 0.1.0.0, 69610930 B
OK   cuda A-7 the named load-bearing files are in the tree -- all 6 named file(s) present and non-empty; assemble-app.json named 4 wrapper file(s), files=108
INFO ISCC exit 0 after 12.26 s; 110 Compressing: line(s); 0 warning line(s)
OK   cuda B-1 ... exit 0, 110 file(s) compressed in 12.26 s, ledger = models.json python-embed.json README.md runtime-cpu.json runtime-cu126.json runtime-cu130.json vc_redist.json, 0 warning(s)
OK   cuda B-2 ... = 84986833 B (81.05 MiB); band 80740352-89128960 B (77.0-85.0 MiB)
INFO sha256 990733546d9eb18d6b2d25efe17d3d66085a427d7de2d15daae7af979cbbc11a  irodori-tts-ywk-setup-v0.1.0-cuda.exe
OK   radeon A-1 ... 108 file(s), 37111434 B
OK   radeon A-3 ... ships ... runtime-rocm-gfx1151.json runtime-cpu.json ; must not ship runtime-cu126.json runtime-cu130.json
INFO ISCC exit 0 after 11.9 s; 110 Compressing: line(s); 0 warning line(s)
OK   radeon B-2 ... = 85001304 B (81.06 MiB)
INFO sha256 7116a5f9cf0dec227cbe7744cbf2952f7bcd842d9f78857591f9fe37a1555eaf  irodori-tts-ywk-setup-v0.1.0-radeon.exe
STEP DONE: 2 installer(s), 20 gate(s), 0 failed.
```

`build/out/installer` の実測＝`irodori-tts-ywk-setup-v0.1.0-cuda.exe 84,986,833 B` ／
`irodori-tts-ywk-setup-v0.1.0-radeon.exe 85,001,304 B` ／ `SHA256SUMS.txt 210 B`（2 行・LF）。
**門は 9 本から 10 本になった**（A-7 の新設）ので `-All` の記帳は `20 gate(s)`。

**⑵ `e-install-probe.ps1`（既定＝`-Steps 1,2,3,5,6,7,8`・`-Flavor radeon`・pwsh 7.6.5・71.4 s）**

```
=== 0 failure(s) of 72 ===        EXITCODE=0
[PASS] the machine starts with no irodori install (no HKCU key and nothing under %LOCALAPPDATA%\Programs) :: stale {app} dir(s) = 0
[data] before: files=53769 bytes=16990584628
[data] moved aside -> C:\Users\mugonkun\AppData\Local\irodori-tts-ywk.e2-backup
[PASS] stage 2: the ready page carries the size sentence from [Messages] :: looking for "約 106 MiB" and a GiB figure; found size=True gib=True
[PASS] stage 7: the start menu shortcut named irodori is gone :: irodori .lnk before=1 after=0 (start menu total 14 -> 13)
[PASS] stage 8a: the uninstaller ran to the end and both answers reached it :: logClosed=True answered=2 asked=2 log=173 lines
[PASS] stage 8b: the uninstaller ran to the end and both answers reached it :: logClosed=True answered=2 asked=2 log=173 lines
[PASS] stage 8c: the uninstaller ran to the end and the one answer reached it :: logClosed=True answered=1 asked=1 log=171 lines
[PASS] teardown: no irodori uninstall key is left in HKCU :: left =
[PASS] teardown: no {app} is left under %LOCALAPPDATA%\Programs :: left = 0
[PASS] teardown: no start menu shortcut is left :: left = 0
[data] restored -> C:\Users\mugonkun\AppData\Local\irodori-tts-ywk
[PASS] the operator data tree came back byte for byte (count, bytes and every name) :: before files=53769 bytes=16990584628 :: after files=53769 bytes=16990584628 :: name/length differences=0
[PASS] teardown: the reserved ports and this run private port are still quiet :: busy =  ; private 18097 listening = False
```

**母数が 69 から 72 に増えた**のは、段 8a／8b／8c に「最後まで走ったか」の釘を 1 本ずつ足したためである。

**走り終えた機体の実測（後片付けの証拠）**＝データ樹 `53,769 檔・16,990,584,628 B`（走の前と同一）／
`irodori-tts-ywk.e2-backup` **無し**／`%LOCALAPPDATA%\Programs\irodori*` **0 件**／
Uninstall 鍵 2 つとも **無し**／スタートメニューの irodori `.lnk` **0 件**／
ランチャのプロセス **0**／8088・7861・18088・18097 は **4 つとも listening = False**／
`build/out/app` **108 檔・37,111,434 B**。

### 16-7 卓へ（**本席の停止域の外＝設計書の訂正が要る 5 件**）

1. **§3-2 の見出し「門 A（ISCC の前・5 本）」は、いま「7 本」である。**実装＝A-1〜A-7、
   `installer/README.md` は本席が「門 A（**7 本**）」に直した。設計書の見出しと表は他席の節なので触っていない。
2. **§3-2 の門 A-1 の行「期待値から外れたら WARN、0 件なら exit 1」も直す要がある。**
   実装＝**檔数の不一致は失敗**・**総バイトの drift は WARN**・**0 件も失敗**。終了コードは「落ちた門の本数」。
3. **§3-2 の門 A-3 の条件「もう一方の種の台帳が 0 件」**は §12-10 の 1 で既に指摘済み（配布樹には課せない）。**未反映**。
4. **§13-3／§14-3／§14-4 の「配布樹はビルドしたホストで変わる」の原因側が消えた**（16-3 ⑶）。
   配布樹は 2 ホストで **1 バイトも違わない**。ただし**全段ビルドでは setup がなお再現しない**
   （ランチャ exe を publish し直すと数バイト動く＝§14-4 の残り半分）。本席の実測でも同じ入力・同じ日で
   `84,986,833` と `84,986,835` の 2 B 差が出た（間に `-All` の publish が挟まっている）。
5. **`docs/install.md` §5-3 の「非昇格で `/J` が張れるかは未実射」は埋まった**
   （本席が `C:` の中で実測・`elevated=False`・`LinkType=Junction`）。`D:` 相手の実射は停止域のまま。
   §5-3 の本文は本席が直した（担当 path の中）＝「junction 自身はどちらを選んでも残る」。

### 16-8 まだ撃っていない（＝この席の外・または限界）

- **段 4（`-IncludeAcl`）・段 9（日本語ユーザ名）**＝撃っていない（前者は既定で走らず、後者は利用者作成に管理者が要る＝裁定 90）。
- **junction を張った本物のデータ樹でのアンインストール**＝**畳まないこと**だけを使い捨ての `.iss` で実射した。
  1 問目・2 問目の削除が junction を通り抜けることは **CHM と実装からの断定＝推測のまま**（`D:` は停止域）。
- **門 B-1 の「種の混入」枝が落ちる走**＝`.iss` の `[Files]` を壊す要があるので撃っていない（正の向きのみ実測）。
- **門 A-6 が落ちる走**＝古い exe を作る手が無い（正の向きのみ実測）。
- **別機体・別版 ISCC での再現**＝この機体 1 台だけ。
- **最終の 2 射は pwsh 7.6.5 のみ。**PS 5.1 は parse と `assemble-app` の実走までで、
  最終の全段 `-All` は 5.1 で撃ち直していない（§12-6 の I-9 は旧実装での実測）。

---

## 17. 改訂＝裁定 109（2026-09-07）＝版の表示名と既定の導入先

**この節は設計の改訂であって、§11〜§16 の実射の記帳ではない。**§11 以降は「そのとき何が起きたか」の
記録なので書き換えていない（旧名・旧路のまま残っている行がある＝当時の事実である）。
機械が読む正の値は §1-4 の表と `installer/irodori-tts-ywk.iss` である。

### 17-1 司令官の下命（逐語）

> 「CUDA版とROCm版の区別」インストーラーが別のはずだが、アプリ名称も(CUDA版)(ROCm版)としてデフォルト
> インストールフォルダも分けたい。yomiwakechanからは無いが、Python無し環境での利用で両方インストール
> したい人も居ると思われる。Windowタイトルも別に分ける。本PCにインストールされている版はそのままでよい。

### 17-2 変えたもの（3 つだけ）

| | 改訂前 | 改訂後 |
|---|---|---|
| `AppName`（cuda） | `irodori-TTS for 読み分けちゃん` | `irodori-TTS for 読み分けちゃん（CUDA 版）` |
| `AppName`（radeon） | `irodori-TTS for 読み分けちゃん（Radeon 版）` | `irodori-TTS for 読み分けちゃん（ROCm 版）` |
| `DefaultDirName`（cuda） | `{autopf}\irodori-tts-ywk` | `{autopf}\irodori-tts-ywk-cuda` |

`AppName` は `AppVerName`・`UninstallDisplayName`（＝「アプリと機能」の表示名）・`[Icons]` の近道の名・
`[Run]` の `{cm:LaunchProgram,…}` に一斉に効く。ランチャ側も同じ文字列を出す（§17-4）。

**`DefaultDirName`（radeon）は 1 字も変えていない。** 本席の機体に入っている ROCm 版
（`…\Programs\irodori-tts-ywk-radeon`・鍵 `{ECA98712…}_is1`）はそのままでよい＝司令官の逐語
「本PCにインストールされている版はそのままでよい」。

### 17-3 CUDA 側の路を改めた理由＝**本体に合わせる側はこちらだった**

本体（読み分けちゃん2）の `UI/Services/Launch/EngineLaunchDefaults.cs` は配布版のランチャを

```
%LOCALAPPDATA%\Programs\irodori-tts-ywk-radeon\IrodoriTtsYwk.Launcher.exe
%LOCALAPPDATA%\Programs\irodori-tts-ywk-cuda\IrodoriTtsYwk.Launcher.exe
```

の順に探しており、素の `Programs\irodori-tts-ywk` は**候補に入っていない**
（本体側の試験 `IrodoriYwkCompositionTests.cs` が「候補に `-radeon` と `-cuda` が在ること」と
「素の `Programs\irodori-tts-ywk` が**無い**こと」の両方を釘付けしている）。
＝改訂前の CUDA 版は**本体の自動発見に引っかからなかった**。この改訂はその食い違いを閉じる
＝ただし**閉じるのは新しく入れた機体だけ**である（既存の導入は §17-5 のとおり旧路に留まる）。

### 17-4 内部の識別子は 1 つも変えていない

`Flavor` の id（`cuda`／`radeon`）・`/DFlavor=`・setup の檔名（`…-cuda.exe`／`…-radeon.exe`）・
`AppId` の GUID 2 つ・`AppMutex`（`Local\irodori-tts-ywk-launcher`）・ランチャの錠と合図の名・
enum `ReleaseFlavor.Radeon`・台帳名（`runtime-rocm-gfx1151`）・データ樹の名（`irodori-tts-ywk`）・
`launcher/Directory.Build.props` の `<Product>`。**利用者に見せる版の名だけ**が「ROCm 版」である
（`Radeon` は道具の名＝gfx1151・Radeon 8060S・`docs/radeon.md` として残る）。

ランチャ（exe は 1 本で両リリースを兼ねる）は版を**樹から読む**＝
`ReleaseFlavors.Detect(ReleaseFlavors.LedgerNames(paths.LedgerDir))`。
`launcher/IrodoriTtsYwk.Launcher/ViewModels/ReleaseFlavor.cs` に純関数を足した：

| 関数 | 出す文字列（ROCm 版の例） | 差す先 |
|---|---|---|
| `AppTitle(flavor)` | `irodori-TTS for 読み分けちゃん（ROCm 版）` | 主窓の `Title`（`MainWindow.xaml.cs`）・「このアプリについて」の見出し（`AboutView.xaml.cs`） |
| `WizardTitle(flavor)` | `初回取得（ROCm 版）` | 初回取得ウィザードの `Title`（`FirstRunWizard.xaml.cs`） |
| `TrayText(flavor, 版, 状態)` | `irodori-TTS（ROCm 版） v0.1.0／待機` | トレイの吹き出し（`App.xaml.cs` の `UpdateTray`） |
| `VariantNote`（`FirstRunViewModel`） | `ROCm 版です（Radeon の GPU 向け）。精度は bf16 に固定されます（未保障・gfx1151 で確認済み）。` | 初回取得ウィザードの変種選択の頁 |

吹き出しは Win32 の 63 字の枠が在るので、**組み立てを純関数に出して xUnit が全状態×両版で釘付けする**
（`ReleaseFlavorTests`）。実測の最長は 30 字なので枠には 33 字の余りがあり、版と版数の間の半角空白は
改訂前（`irodori-TTS v0.1.0／待機`）どおり残してある。
樹が読めない機体では `DetectFrom` が例外を出さずに CUDA 版として振る舞う（`ReleaseFlavorTests` が
`null`／空白／在りもしない路の 3 つで釘付けする）。
XAML 側の `Title` は**設計時（デザイナ）用の見本**であって落ち先ではない＝実行時は必ず code-behind が
版つきに差し替えるので、素の幹が利用者の目に入る経路は無い。
なお `RuntimeVariants` の `Radeon gfx1151（未保障・bf16 固定）` は**変種＝道具の名**なので据え置き
（版の名ではない）。

### 17-5 改名の引き継ぎ＝旧名の近道を 1 本消す

Inno は `[Icons]` の名が変わっても**旧名の `.lnk` を消さない**（消えるのはアンインストールのときで、
しかも消す名は「いま入っている版が書き込んだ名」）＝改名の前に入れた機体では更新導入で新旧 2 本が並ぶ。
`[InstallDelete]` に 1 行足して塞いだ（§1-3 の追記）。
**導入先は直さない**＝Inno の `UsePreviousAppDir`（既定 yes）で既存の導入はその場所に上書きされる。
`DefaultDirName` の改訂が効くのは**新規の機体だけ**である。

司令官の逐語「本PCにインストールされている版はそのままでよい」が免じたのは**本席の機体の ROCm 版**
であって、他機の CUDA 版まで免じたものではない。だから残る欠けを名指しで書き留める：

- **改訂前に CUDA 版を入れてある機体は、上書き更新しても `%LOCALAPPDATA%\Programs\irodori-tts-ywk\`
  に留まる**＝本体（読み分けちゃん2）の候補は `…-radeon\` と `…-cuda\` の 2 つだけで、
  素の路は本体側の試験が候補から除くことまで釘付けしている＝**自動発見は改訂後も直らない**。
- 手当ては 2 つ＝⑴ **いったんアンインストールしてから新しい setup を入れ直す**
  （データ樹 `%LOCALAPPDATA%\irodori-tts-ywk\` は共有＝話者・設定・モデルは残る）。
  ⑵ 入れ直さずに、本体の元栓のパス欄で exe の路を手で指す（保存値が正なので推定は上書きしない）。
- 利用者向けの写し＝`docs/install.md` §2 の但し書き・§4 の「本体が配布版のランチャを自動で見つけない」の行・§5-1。

### 17-6 同居の意味（**変えていない・書き留めるだけ**）

- 2 つの版は `AppId` も導入先も別なので**同じ機体に同居できる**。
- **データ樹（`%LOCALAPPDATA%\irodori-tts-ywk\`）は共有**する（§2・§5-4）。版ごとには分けない
  ＝実行系は変種ごとの枝に入るので混ざらず、話者と設定は 2 版で 1 つの資産である。
- **同時に走るランチャは 1 個体だけ**＝錠（`Local\irodori-tts-ywk-launcher`）もポート（`127.0.0.1:18088`）も
  2 版で共有する。2 個目は 1 個目の窓を前に出して静かに退く（`App.xaml.cs` の `SignalExistingInstance`）。
- **既知の癖（直さない）**＝`.iss` の `AppMutex` も共有なので、**もう一方の版のランチャが走っている最中に
  導入すると弾かれる**。そのとき Inno が出す `SetupAppRunningError` が名乗るのは
  **これから入れる側の `AppName`** である（走っているのは向こうの版なのに「ROCm 版が検出されました」と
  読める）。**導入だけではない**＝Inno の CHM 逐語は "a mutex which Setup **and Uninstall** should
  check" なので、**撤去も同じ錠を見る**（CUDA 版を撤去しようとして、走っているのは ROCm 版のランチャ
  なのに断られる、が同じ頻度で起きる）。害は無い（どちらにせよランチャを終了すれば通る）ので、
  錠を分けてまで直さない＝錠を分けると「両版のランチャが同時に走ってポートを取り合う」という
  本物の事故が開く。
- **両版を入れた機体で本体（読み分けちゃん2）が自動で選ぶのは ROCm 版**である
  （候補順が `…-radeon\` → `…-cuda\` で、実在する最初の 1 つを採る）。CUDA 機に両方入れると
  本体の一括起動が ROCm 版を起こす形になるので、本体側で路を手で指すか、本体の候補順の話として
  卓へ回す（**このリポの範囲外**＝ここでは書き留めるだけ）。

### 17-7 この改訂で触った檔

`installer/irodori-tts-ywk.iss`／
`launcher/IrodoriTtsYwk.Launcher/ViewModels/ReleaseFlavor.cs`・`ViewModels/FirstRunViewModel.cs`・`App.xaml.cs`・
`Views/MainWindow.xaml`＋`.cs`・`Views/AboutView.xaml`＋`.cs`・`Views/FirstRunWizard.xaml`＋`.cs`／
`launcher/IrodoriTtsYwk.Launcher.Tests/ViewModelsTests.cs`（`ReleaseFlavorTests` に 7 本足して **610 本**）／
`probe/e-install-probe.ps1`（段 1 に「`DisplayName` がこの版の `AppName` で始まる」と
「近道が `<AppName>.lnk` である」の 2 本を足し、cuda の期待路を `irodori-tts-ywk-cuda` に改めた）／
`probe/rtx-e1-runbook.md`（E-1 台本の直書きの路を `…-cuda\` に改めた）／
`licenses/first-run-notices.md`（§B の見出しと末尾の 1 文が**リリース**を名乗る所を「ROCm 版」に改めた
＝配布物に入り初回取得ウィザードが逐語で出す檔なので、窓題と食い違わせない）／
`docs/install.md`・`README.md`・`installer/README.md`・`docs/radeon.md`・
`docs/design/ben-a-skeleton-and-build.md` §2・`docs/design/ben-d-launcher.md` §26・この設計書。

**卓へ回す票**＝`licenses/first-run-notices.md` は `build/out/app` に入る檔なので、この改訂で
配布樹の総バイトが **37,125,643 B → 37,125,639 B（−4 B）**に動く。`build/installer-build.ps1` の
`$ExpectedAppBytes` は**本席の持ち場ではない**ので触っていない。門 A-1 の**檔数は FAILURE・バイトは
WARN**（同 `:513-515` の逐語「The bytes are a WARN: they move when a licence or a ledger sentence
moves.」）なので、放っておいても走行は止まらないが、記帳を新しい実測値に直すかは主席の裁量である。

**未実射**＝この改訂は setup を組み直していない（`build/installer-build.ps1` は主席が回す）。
段 1 の新しい 2 本と、改名の引き継ぎ（旧名の `.lnk` が消えること）は**まだ実弾で見ていない**。

## 18. v1.0.0 のリリース（裁定 117・2026-09-09）＝切り方の手順の記帳（主席）

司令官の指示（逐語）＝「**簡単なリリースページを作ってv1.0としてpublish頼めるかな。**」。正典＝`decisions.md` 117。
Release＝https://github.com/mugonkun/irodori-tts-for-yomiwakechan/releases/tag/v1.0.0（repo は private）。

**切り方（この便で通した順・次の版も同じ順で）**

1. **版を 1 箇所で上げる**＝`launcher/Directory.Build.props` の `<AppDisplayVersion>`（`v` 前置・唯一の定義・
   `installer-build.ps1` が機械読取）と、対の `server/ywk_server.py` の `YWK_VERSION`（`/ywk/status.version`）。
   現行の値を名指す記述（契約 **⑻ 版と互換**の見本〔⑹ は `GET /ywk/status` の可視化で、版は`"<配布版の版>"` の placeholder なので触らない〕・`launcher/README.md` の settings 見本・`probe/e-install-probe.ps1` の
   既定の setup 名・`.iss` と `installer-build.ps1` の註）も同じ回で。設計書の実測の記帳と Tests の fixture は動かさない。
2. **リリース文**＝`docs/release-notes/v<版>.md`（GitHub 風味・`##` から・事実は repo の檔から引く・sha256 と
   バイトの欄は `<sha256>`／`<bytes>` の placeholder にして組んだ後に埋める）。`.iss` の docs は明示列挙なので
   利用者機には出ない。
3. **旧い成果物を退かす**＝`build/out/installer/` の前の版の setup 2 本と `SHA256SUMS.txt` を外へ移す。
   門 B-3 は「行末が同じ檔名の行」だけを置き換えるので、残すと一覧が 4 行になり `sha256sum -c` が割れる。
4. **組む**＝`build/installer-build.ps1 -All`（20 門・WARN 0）。A-6 が exe の `ProductVersion` を新版と突き合わせる。
   A-1 の記録値は配布樹のバイトが動いたときだけ直す（exe は樹の外＝裁定 51・この回は動かず 33,294,832）。
5. **placeholder を埋める**＝`SHA256SUMS.txt` と実檔の大きさから（scratchpad `fill_release_notes_117.py` の型）。
6. **commit → 注釈つき tag `v<版>` → push（main と tag）**。
7. **Release**＝`gh release create v<版> <cuda setup> <radeon setup> SHA256SUMS.txt --title "irodori-TTS for 読み分けちゃん v<版>"
   --notes-file docs/release-notes/v<版>.md --latest --verify-tag`。`gh release view v<版> --json assets,isDraft,isPrerelease` で
   3 資産が `uploaded`・draft／pre-release が偽であることを検める。
8. **N: の引き渡し木**（`rtx-handoff\installer`）を新版に差し替え、`sha256sum -c` で一致を検める。
9. `decisions.md` に 1 条・この設計書に記帳。

**この回の実測**＝xUnit 649＋1 skip・契約テスト 361・門 20／0 失敗・setup cuda 82,741,884 B `ce817787…`／
radeon 82,756,787 B `6a65eed2…`・tag は commit `5b0568a`・公開 2026-09-09 07:00:59Z（16:00 JST）。
**未実射**＝v1.0.0 の setup を RTX 機で撃っていない。本機の導入済み Radeon 版は v0.1.0（裁定 115）のまま。

## 19. v1.0.1 のリリース（裁定 118・2026-09-10）＝§18 の手順を 2 度目に通した記帳（主席）

司令官の指示（逐語）＝「**話者追加。"シャンパンコール(ホスクラ)" 参照ボイスファイル実体は"c:\yomiwakesozai\hostclub.wav"。既に2次生成なのでこのまま取り込んで配布ページ更新まで頼む。**」。正典＝`decisions.md` 118。
Release＝https://github.com/mugonkun/irodori-tts-for-yomiwakechan/releases/tag/v1.0.1（repo は private のまま）。

**§18 の手順との差**（手順そのものは変えていない）

- 段 1＝版の繰り上げに加えて**配布樹の中身が動いた**（同梱 wav 1 本＋`voices/presets.json`＋`licenses/README.md`）ので、A-1 の記録値を **108 檔／33,294,832 B → 109 檔／34,087,580 B** に、A-2 と `.iss` の `PresetWavCount` を **11 → 12** に直した。件数は WARN ではなく失敗になる門なので、wav を足すときは `$ExpectedAppFiles` も必ず動かす。
- 段 4＝A-1 は**算で先に置いた値が実測と一致**した（33,294,832 + 787,244〔wav〕+ 3,687〔台帳〕+ 1,817〔licenses/README.md〕）。`docs\` は配布樹に入らない（`install.md` だけ `.iss` の明示列挙）ので文書の編集はこの数を動かさない。
- 段 5＝scratchpad `fill_release_notes_118.py`（表の行を檔名で特定して、その行の placeholder だけを置き換える型）。
- 段 3 の退避先＝scratchpad `installer-v1.0.0-final`（N: の旧 3 檔も同じ下の `n-old\`）。

**この回の実測**＝xUnit 649＋1 skip・契約テスト **369**（新設 8）・門 20／0 失敗・WARN 0・setup cuda **83,262,330 B** `42e8dacf…`／radeon **83,277,211 B** `16101a49…`・tag は commit `fd3d936`・公開 2026-09-10 09:21:13Z（18:21 JST）・Latest・N: `sha256sum -c` OK。
**未実射**＝v1.0.1 の setup を RTX 機でも本機でも撃っていない（本機の導入済み Radeon 版は v0.1.0＝裁定 115 のまま・走っている個体は止めない）。新話者で合成を撃っていない・席は音を聴いていない・録音の出所は未確認（裁定 118）。

## 20. v1.0.2 のリリース（裁定 121・2026-09-10）＝§18 の手順を 3 度目に通した記帳（主席）

司令官の報告（RTX 機・v1.0.1・逐語）＝「**話者一覧にシャンパンコールがないね。初回起動から、モデルダウンロードへの導線を追加してほしい。単に、サーバー起動失敗となるから。**」。正典＝`decisions.md` 121・ランチャ側の設計＝`ben-d` §29。Release＝https://github.com/mugonkun/irodori-tts-for-yomiwakechan/releases/tag/v1.0.2（Latest・repo は public）。

**§18 の手順との差**＝無し。配布樹（`build/out/app`）は `server/ywk_server.py` の版の字だけが動き長さは同じなので A-1 は不動（109 檔／34,087,580 B）。変わったのは配布樹の外の exe（69,637,254 → 69,641,096 B）だけ。

**この回の実測**＝xUnit **674**＋1 skip（新設 25）・契約テスト 369・門 20／0 失敗・WARN 0・setup cuda **83,264,616 B** `6c60b396…`／radeon **83,279,491 B** `885b7ed5…`・tag は commit `7fc16e6`・公開 2026-09-10 11:19:42Z（20:19 JST）・N: `sha256sum -c` OK。
**引き渡し木に添えた物**＝`wizard-probe.ps1`（scratchpad 由来・repo には入れていない）＝UIA で `FirstRunWizard` の窓を探し `session: probe=<n> launcher=<n> same=<bool>` と `launcher=pid <n> wizard=OPEN|none …` を返す読むだけの台本。本機の実測＝wizard=none／EXIT 1（窓ありの側は RTX 機の ⒝ が最初の実射）。
**RTX 機の実射**＝別席が ⒜⒝⒞ を撃ち 3 段とも合格（裁定 122・20:3x）。`wizard-probe.ps1` は最初の実射で誤報した（窓は MainWindow の子孫＝`Descendants` で拾う・直して `probe/wizard-probe.ps1` に置いた）。**未実射**＝v1.0.2 を本機で撃っていない（走っている個体は止めない・許可待ち）。

## 21. v1.1.0 のリリース（裁定 124〜126・2026-09-10）＝§18 の手順を 4 度目に通した記帳（主席）

正典＝`decisions.md` 124（司令官の製品評価・逐語）・125（配布ページの中身）・126（v1.1.0 の設計と実測）。Release＝https://github.com/mugonkun/irodori-tts-for-yomiwakechan/releases/tag/v1.1.0・**配布ページ**＝https://mugonkun.github.io/irodori-tts-for-yomiwakechan/（`gh-pages` 枝の根・`site/index.html` の写し・GitHub Pages を API で有効化＝`source.branch=gh-pages, path=/`・`.nojekyll` を添えた）。

**§18 の手順との差**＝⑴ 段 2 のリリース文は裁定 125 で sha256／バイトの欄を持たない（`SHA256SUMS.txt` は Release に置くだけ）ので段 5 の穴埋めが無い ⑵ 段 6 のあとに **gh-pages の更新**が入る＝`site/index.html` が変わった回だけ、scratchpad に `git worktree add --orphan -b gh-pages`（初回）／`git worktree add <path> gh-pages`（2 度目から）で枝を出し `index.html` を写して commit・push（`main` は触らない・`site/README.md` の手順と同じ結果）。⑶ 配布樹は `server/ywk_server.py` の版の字だけ（長さ同じ）＝A-1 不動。ランチャ exe は WinForms（トレイ）を外したぶん小さくなる。

**この回の実測**＝xUnit **743**＋1 skip・契約テスト 369・門 20／0 失敗・WARN 0・setup cuda **75,475,640 B** `ef3f11d8…`／radeon **75,490,540 B** `2688b09e…`・tag は commit `f4cec17`・公開 2026-09-10 13:57:37Z（22:57 JST）・Latest・N: `sha256sum -c` OK。
**未実射**＝本機・RTX 機とも v1.1.0 を撃っていない（RTX 機の 2 段の検分＝既存の cu130 の扱い → cu126 で 1 射して × で終了し VRAM が返るか、は別席の実射待ち・裁定 126 ⑽）。

## 22. 段 E（v2.0）＝置き場を版ごとに割り、撤去の問いを 0 にした記帳（実装席・2026-09-11）

正典＝`decisions.md` 132（撤去の既定）・133（削除と置き場の規則）。設計の正本＝`docs/design/charter.md` §4-24／§4-25／§5・`docs/design/v2-spec.md` §11（11-1〜11-8）・`docs/design/v2-plan.md` 段 E（E-1／E-2／E-3）と段 F-2。**この §22 は、その 2 檔の設計を実装に落としたときの実測と、判っている欠落の記帳である。**

### 22-1 §5-4 の規則は廃止した（この檔の中で最も大きい訂正）

`ben-e` §5-4 の「**共有のデータ樹を道連れにしない**」（＝もう一方の種が入っていれば取得物を消さない）は、**規則ごと無効**である。裁定 133 ⑶ で RTX（CUDA）版と Radeon（ROCm）版は**別アプリ**と徹底され、**データ樹も設定も声も共有しない**ことになったので、道連れが起きる場面そのものが消えた。§5-4 を読むときは、この §22 と `v2-spec.md` §11-8 を正本とすること。

### 22-2 `.iss` の差分（4 箇所）

| 何 | 前 | 後 |
|---|---|---|
| データ樹の綴り | `DataDir()` が `{localappdata}\irodori-tts-ywk` を固定で返す | **版ごと**＝`#define MyDataDirName` を新設し `{localappdata}\irodori-tts-ywk-cuda`／`…-radeon`。綴りの正本は `launcher/…/Contracts/AppPaths.cs` の `DataDirNameCuda`／`DataDirNameRadeon` |
| 単一起動の錠 | `AppMutex=Local\irodori-tts-ywk-launcher`（**両版で 1 つ**） | `AppMutex=Local\irodori-tts-ywk-launcher-{#Flavor}`＝`AppPaths.SingleInstanceMutexName` の逐語。ランチャ側は錠と**合図の口**（`…-activate-cuda`／`…-radeon`）を同じ組で割った |
| 撤去の問い | `SuppressibleTaskDialogMsgBox` が 2 つ（取得物・話者と設定。既定はどちらも IDNO） | **0 つ。**`WipeTree(DataDir())` が版の樹を丸ごと消す。`TwoLabels`／`OtherFlavorName` は使い手が消えたので落とした |
| 旧い共有樹 | 触れる口が無かった | `LegacyDataDir()` を新設。**もう一方の版が入っていないときだけ**一緒に消す（判定は既存の `OtherFlavorKey` の `RegKeyExists`） |

**`WipeTree` の形**＝ふつうのディレクトリは、**すぐ下の枝を先に `WipeTree` 自身へ渡してから** `DelTree(Dir, True, True, True)`（この檔で既に実績のある形）。**junction のときだけ** `DelTree(Dir + '\*', False, True, True)` で中身だけを落として路を残す＝路を丸ごと撃つと CHM の免除（reparse point 自身は消すが中までは消さない）が当たって**中身が残ったまま路だけ消える**からである。

**枝へ降りる理由（是正・2026-09-11）**＝免除は**根だけの話ではない**。`<data>\models` だけを別ドライブへ逃がした樹（3 GB のモデルだけを退かす、いちばん自然な手）は、根がふつうのディレクトリなので `DelTree(Dir, True, True, True)` 1 手に当たり、**入れ子の junction は環だけ消えて実体が D:\ に孤児として残る**＝裁定 132 が起こされた当の症状が、置き場所を変えて再現する。だから枝の名を `FindFirst`／`FindNext` で**先に読み切ってから**（消しながら走査しない）1 段ずつ降り、入れ子の junction は上の「中身だけ落とす」枝に当たらせる。空になった環は最後の `DelTree` が畳む（**根の環だけは残す**＝入れ直しに `mklink /J` を張り直さなくてよい）。

**`NextButtonClick` の関門は 3 つになった**（是正・2026-09-11）＝⑴ 非昇格で書けない場所 ⑵ 版のデータ樹 ⑶ **旧い共有樹**。⑶ が要る理由＝⑵ は版ごとの路しか見ないので、**まだ移送していない機体**では約 8 GB が居る `%LOCALAPPDATA%\irodori-tts-ywk\` が素通りする。そこへ入れると、初回起動の移送が「自分の exe が入ったままの樹」を `Directory.Move` しようとして錠で落ち、撤去の `WipeTree(Legacy)` が残りを消す。

**`[UninstallDelete]` にデータ樹を書かなかった理由**＝`filesandordirs` を junction に撃つと、まさにその「路だけ消えて実体が孤児になる」が起きる。データ樹の削除は junction を見分けられる `[Code]` 側（`CurUninstallStepChanged` → `WipeTree`）に 1 本化した。

### 22-3 ランチャ側で足した物（`.iss` と綴りを揃える先）

| 檔 | 何 |
|---|---|
| `Contracts/AppPaths.cs` | `DataDirNameCuda`／`DataDirNameRadeon`／`LegacyDataDirName`／`FlavorId`／`SingleInstanceMutexName`／`ActivateEventName`／`FlavorDetermined`。`Resolve` は版を受け取る純関数のまま・`FromEnvironment` が版を 3 段で決め、`settings.json` の `dataDir` もここで拾う（§22-3-1 ⑵⑼） |
| `Services/Ledger/LegacyDataMigration.cs` | 旧共有樹 → 版の樹の移送（純関数 `Plan` ＋ 薄い殻 `Run`）。`EnsureDataDirectories` の頭で 1 度だけ走る＝`settings.json` を読むより**前** |
| `Services/Voices/PresetSync.cs` | 同梱の声の中身の差分（§11-2 の 5 枝）＋ md5 の純関数 |
| `Services/Ledger/RuntimeDiff.cs` | 一式の差分（§11-3 の 4 枝＋歯止め）＋差分の回の `WheelInstaller` の構え |
| `Services/Models/ModelDiff.cs` | モデルの差分（§11-4＝**見積り専用**・檔を動かさない） |
| `Services/Ledger/RuntimeStamp.cs` | `.ledger.json`（適用済みの台帳の写し）の読み書き。`Burn` と**同じ回**で置く（`writeAppliedLedger: false` で写しだけ抑えられる＝§22-4 の 8） |

#### 22-3-1 検分（Opus 5）を受けて当て込んだ是正（2026-09-11）

| # | 何が壊れていたか | 当てた物 |
|---|---|---|
| ⑴ | 写しの移送が**途中で落ちると再開しない**＝半端に入った版の樹を `Plan` が「もう使われている」と読んで `None` を返す＝欠けた声のまま残り、残りは旧樹に取り残される（両版の機体は約 8 GB を版ごとに写すので ENOSPC は現実の話） | `CopyToStaging`＝兄弟の `<版の樹>.migrating` へ写してから `Directory.Move` 1 手。落ちた回は残骸を畳むので**版の樹は空のまま**＝次も `Copy` が出る。加えて `FitsOnDestination`（樹のバイトの和 ＋ 1 割 vs `DriveInfo.AvailableFreeSpace`）で、足りなければ**旧樹に 1 檔も触らずに帰る** |
| ⑵ | 版の判定が**黙って CUDA に倒れる**＝`ledger/` が 1 度読めないだけで、Radeon 機がデータ樹・錠・**移送の行き先**まで CUDA 側を掴む（裁定 109 のころは窓題を間違えるだけだった） | `ReleaseFlavors.TryDetectFrom`（読めたときだけ真）＋ `TryDetectFromDirectoryName`（`.iss` の `MyDirName` は版ごと）＋ 既に在る版の樹（片方だけ在るとき）の 3 段。どれも黙ったら `AppPaths.FlavorDetermined=false` で、**移送を断る** |
| ⑶ | 上書きの**行き先が、md5 を測った檔ではなかった**＝実測は `refs\<台帳の file>`、写しは `refs\<正本の檔名>`。配布側が `secondary.file` だけ改名した版で**別の話者の参照 wav を潰す**うえ、当の行は古い wav のまま `preset_md5` だけ新しくなり枝 d で二度と見直されない | `PresetSyncItem.FileName` を**台帳の `file`**（`IsInsideReferences` を通った側）から採る。正本の檔名にも同じ関門を通す（`..\..\x.wav` を名乗る `presets.json` を弾く） |
| ⑷ | `Burn` が `.ledger.json` を**無条件で**置く＝裁定 91 の受け入れ路（`LooksComplete` しか見ていない・**どの台帳で組んだか判らない**樹）でも「中身は全部この sha256 だ」と名乗る＝次の版の差分がその嘘を信じて混ざった樹を残す | `Burn(..., bool writeAppliedLedger = true)`。呼び手の付け替えは段 C／F（§22-4 の 8） |
| ⑸ | 「檔が無い」と「檔が読めない」が同じ `null` になり、`null` は**上書きの写し**だった＝再生器が掴んでいるだけの wav が配布側の音に戻る（枝 c が守っている当の事故） | 測り手の締めを `measured ?? (File.Exists(path) ? Different : null)` に。`Different` は 16 進 32 桁にならないので必ず枝 c へ落ちる。`PresetSync.Plan` の約束も註に書いた |
| ⑹ | 台帳の保存が落ちると**新しい wav ＋ 古い `ref_latent`** が残る＝事前計算は ref_embed を持つ行を飛ばすので**新しい録音が永久に鳴らない**。次の起動は枝 e に落ち、`Record` は `PresetMd5` しか書かないので自力で治らない | 順を組み直した＝⑴ `ref_latent` を落として保存 → ⑵ wav を写す → ⑶ `.pt` を消す → ⑷ `preset_md5` を書いて保存。**どこで落ちても自力で治る側**に倒れる |
| ⑺ | `ToFetchPlan` と `NewInstaller` が**別々の入口**＝差分の計画を既定の `WheelInstaller` に流すと、頭で樹が空になってから数本だけ入る＝**動いていた一式が数本の wheel だけになる**。歯止めは註しか無かった | `ToDifferentialRun(variant, diff) -> (FetchPlan, WheelInstaller)` 1 本に畳み、2 つは `private` に。丸ごとの計画を渡したら `InvalidOperationException` |
| ⑻ | §11-3 が必須と書く**締め**（`LooksComplete` ＋ `ExpectedDistInfoCount`）の継ぎ目が無い＝段 F が知らずに飛ばせる | `RuntimeDiff.VerifyAfterApply(paths, ledger, variant)`（偽＝丸ごとへ落とす）。**`.ledger.json` はこれが真を返してから置く** |
| ⑼ | CANON の「`settings.json` の `dataDir` は勝つ」が**実装に存在しなかった**（欄は読んで書き戻すだけで、誰も使わない）＝註だけが嘘をついていた | `FromEnvironment` が版の樹（無ければ旧い共有樹）の `settings.json` を 1 度覗いて、値が在れば置き場ごと差し替える（`EnsureDataDirectories` より前）。env と違い `DeveloperMode` は立てない |
| ⑽ | 撤去の `WipeTree` が**根の junction しか見ない**／`NextButtonClick` が**旧い共有樹を守らない**／移送が**版の樹の 1 段下の junction を消す** | §22-2 の `WipeTree`／`NextButtonClick` の項と、`LegacyDataMigration.HasReparsePointChild`（枝に junction が在れば移送しない） |
| ⑾ | 写しで移した機体は**旧樹 ＋ 版の樹 2 本＝同じ物を 3 本**抱えるのに、誰も告げない | 写しの後に**旧樹が食っている量を 1 行だけ**ログに残す。**launcher からは消さない**＝「向こうの樹に檔が在る」は「向こうも移送を済ませた」の証明ではない（まっさらに作った樹でも真になる）＝取り返しのつかない削除を推量で撃たない。旧樹は最後の撤去が畳む |

### 22-4 判っている欠落（この席では埋められない）

1. **ISCC を通していない。**この席は台本を撃てないので、`.iss` は**読んでの検分だけ**である（`#define` の綴り・`{#Flavor}` の展開・`begin`／`end` の対・Pascal の注釈に `{` を入れていないこと・`and` と `<>` の優先順位の括弧）。**compile と実射は主席・段 F の持ち場。**
2. **撤去の実射をしていない。**「問いが 1 つも出ないこと」「版の樹が丸ごと消えること」「旧い共有樹が（もう一方の版が無い機体でだけ）消えること」は `probe/e-install-probe.ps1` の門で見る（`v2-plan.md` §2 段 F の表）。
3. **junction 越しの中身の削除**は相変わらず未実射（CHM の原文と `.iss` の行からの断定）。`D:` は停止域のまま。
4. **移送（旧共有樹 → 版の樹）の実射をしていない。**単体試験（`LegacyDataMigrationTests`）は改名・写し・「版の樹が使われていれば何もしない」「明示指定なら何もしない」「失敗したら旧樹を残す」を釘付けしたが、**開発機の実樹（約 8 GB）で撃った例はまだ無い**（`v2-plan.md` §2 段 H の 12）。
5. **`docs/install.md` は §5 だけ直した。**§2 の置き場の表はまだ旧い共有樹の綴りである＝**段 F の持ち場**。
6. **`SettingsDifferentialUpdateCheck` の設定の鍵を足していない**＝この席は `settings.json` の鍵を増やさない約束（契約と JSON の鍵は据え置き）なので、差分の当て込みは「版が変わった回は当てる」を既定にしてある。チェックの札と鍵は段 F の持ち場。
7. **`probe/e-install-probe.ps1` をこの版に当てて撃ってはならない**（**是正・2026-09-11・危険**）。台本の `$script:DataDirRoot`（`:169`）は**旧い共有樹** `%LOCALAPPDATA%\irodori-tts-ywk` を固定で指したままで、`Reset-WorkDataDir`／`-DataDirBackup` の退避（`:653-707`）も、`-DataDirBackup` を切ったときの安全側の拒否（`:751`）も、段 8 の `still there:`／`lost:` の突き合わせ（`:1560-1563`・`:1624`）も**全部その路**を見ている。この版の撤去は **`…-cuda\`／`…-radeon\` を問い無しで丸ごと消す**ので、⑴ 台本が守っている路と ⑵ 実際に消える路が**食い違い**、しかも旧い二重の守り（正しい路の退避 ＋ 2 つの問いが既定 IDNO）が**同時に消えている**＝**実データ樹を持つ機体で走らせると声と設定が消える。** 段 F が直すまでは撃たない（`v2-plan.md` §2 段 F の表・probe の行）。直す中身＝⒜ `$DataDirRoot` を版ごとに（既に版で分けている `ProgramsRoot` の `switch` と同じ形で）⒝ 旧い共有樹を**2 本目の守り／突き合わせ先**として足す ⒞ 段 8 の判定を「版の樹が丸ごと消えた・MsgBox は 1 つも出ない・旧い共有樹はもう一方の版が無いときだけ消える」に組み直す ⒟ 段 5 のログの綴りを `Local\irodori-tts-ywk-launcher-<flavour>` へ。
8. **`MainViewModel.CheckRuntimeStamp`（`:826`）の `Burn` に `writeAppliedLedger: false` を渡すのは段 C／F の持ち場**（`ViewModels/` は本席の持ち場ではない）。`RuntimeStamp.Burn` は既定を真のままにしてあるので、**渡すまでは裁定 91 の受け入れ路が `.ledger.json` を書く**＝素性の知れない樹に「中身は全部この sha256 だ」と名乗らせてしまう。`FirstRunViewModel.cs:1813` と `MainViewModel.cs:1024`（どちらも `install.Ok` の後）は既定のままでよい。
9. **版の見分けが 3 段とも黙る機体では、移送そのものを止める**ようにした（`AppPaths.FlavorDetermined`）。止まったことは**画面に出ない**＝旧い共有樹はそのまま残り、次の起動でやり直せるが、**利用者は「声が 1 つも無い」画面を見る**。⑴ `ledger/` が読めず ⑵ 導入先が版を名乗らず ⑶ 版の樹が両方在るか両方無い、の 3 つが同時に起きる回だけの話だが、実射はしていない。

### 22-5 この回の実測

- `dotnet test launcher -c Release`＝**877 合格＋1 skip**（段 E の前は 809＋1 skip＝**+68 本**。検分の是正で **+17 本**）。
- `dotnet build launcher -c Release`＝**0 警告**（`AnalysisLevel=latest` の樹＝`PresetSync.Md5OfFile` の `CA5351` は用途を書いた `#pragma` で落としてある）。
- 契約テスト（`tests/contract`）＝**372 合格**（§11 は HTTP の口に一切関わらないので**1 本も触っていない**）。
- 配布樹に足した**檔数**＝**0**（`.iss` の変更は配布樹の外）＝門 **A-1 の件数（`$ExpectedAppFiles`）は動かない＝失敗にならない**。
- **ただしバイトは動く**（是正・2026-09-11 の訂正）＝この段は檔を足していないが、**新しい 5 檔がランチャ exe にコンパイルされて入る**。`IrodoriTtsYwk.Launcher.exe` は**配布樹の中**なので `$appBytes` が `$ExpectedAppBytes` と食い違い、`build/installer-build.ps1:537` が `WARN A-1 the tree bytes drifted from the recorded figure` を出す。**この回は WARN 0 にならない。** 直し方は裁定 114／116 と同じ＝**主席が組んだ実測で `$ExpectedAppBytes` を記録し直す**（件数は WARN ではなく失敗になる門なので、そちらだけは動かさないこと）。

### 22-6 設計の読みを 1 つ記帳する（§11-3 の「量」）

`v2-spec.md` §11-3 の歯止め＝「**落ちる量が台帳全体の 6 割を超えるなら、差分をやめて丸ごと組み直す**」の「量」を、実装は **item の件数**で読んだ（`RuntimeDiff.RebuildFraction`・試験は 4/5 で丸ごと・3/5 で差分を釘付け）。§11-3 の他の「量」（**押す前に量を告げる**＝`FormatBytes`）は**バイト**なので、ここだけ読みが違う。

**そう読んだ理由**＝この歯止めの目的は「**半端に混ざった樹を作らない**」で、混ざりの度合いは**入れ替わる item の数**に比例する。バイトで読むと、4 GB の `torch` 1 本だけが動いた版が 9 割を超えて丸ごとに落ちる＝樹はほとんど混ざらないのに 5 GB を落とし直すことになり、§4-24 が消したかった当のものが戻ってくる。

**判っている逆の穴**＝小さい pure-python wheel が 61 本（`runtime-cu130.json` は 101 item）動くと、数十 MB の差分のために丸ごと（約 4 GB）へ落ちる。**主席は最初の実 upstream 更新でここに当たる。**そのときはバイト側の割合（`Fetch` の和 ÷ `Installable` の和）を**もう 1 本の条件として足す**（どちらかが超えたら丸ごと、ではなく**両方超えたら丸ごと**）のが素直だが、この席では設計の変更に当たるので**読みを記帳するだけ**にした。
