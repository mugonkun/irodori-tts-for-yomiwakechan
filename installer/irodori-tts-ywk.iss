; installer/irodori-tts-ywk.iss -- 便 E。per-user・非昇格・第三者バイナリ 0（decisions.md 8）。
; 設計＝docs/design/ben-e-installer.md §1・§2・§5・§6-3。裁定＝decisions.md 89・90・91。
; 是正＝同 §11-8（敵対検分の medium 6 件・low 4 件・2026-09-05・是正席 opus）。
; 呼び方＝build/installer-build.ps1 が ISCC に /D を 7 本渡す。この檔に版・路・上流 pin を書き写さない。
;   /DAppVersion=v2.0.2 /DAppVersionNumeric=2.0.2 /DFlavor=cuda|radeon
;   /DSrcApp=<build/out/app> /DSrcExe=<build/out/launcher/win-x64> /DRepo=<リポの根> /DOutDir=<出力先>
; 檔の形＝UTF-8 BOM 付き・CRLF（.gitattributes:7 の *.iss text eol=crlf）。日本語はこの檔にだけ置く。

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

; --- 種ごとの値（AppId の GUID は永久＝decisions.md 90 に記帳済み）-----------------
; 裁定 109（2026-09-07）＝司令官の逐語「『CUDA版とROCm版の区別』インストーラーが別のはずだが、
; アプリ名称も(CUDA版)(ROCm版)としてデフォルトインストールフォルダも分けたい。yomiwakechan からは
; 無いが、Python無し環境での利用で両方インストールしたい人も居ると思われる。Windowタイトルも別に分ける。」
;   ⑴ 表示名は **両方**に版を足す。v1.1.0 までは「…（CUDA 版）」「…（ROCm 版）」だった＝
;       **v2.0 段 F-1 で「… － RTX（CUDA）」「… － Radeon（ROCm）」に改めた**（所有者の指示
;       2026-09-10・憲章 §6-2 附録 4＝併記が正。利用者は箱に書いてある語＝RTX／Radeon で選ぶ）。
;       逐語の正本は launcher/IrodoriTtsYwk.Launcher/ViewModels/ReleaseFlavor.cs の FlavorLabel／
;       Decorate（全角ダッシュの前後に半角空白 1 つ）＝窓題と 1 字も違えない。
;   ⑵ 既定の導入先は CUDA 版を irodori-tts-ywk → **irodori-tts-ywk-cuda** に改める。
;       本体（読み分けちゃん2）の EngineLaunchDefaults は既に
;       Programs\irodori-tts-ywk-cuda → Programs\irodori-tts-ywk-radeon の順で探しており、
;       素の irodori-tts-ywk は **候補に入っていない**＝こちらが本体に合わせる側である。
;       ※ 順は司令官裁定 2026-09-10 で **cuda 先頭に覆った**（便 F 時点の radeon 先頭＝開発機の実在順）。
;         正本＝本体 UI/Services/Launch/EngineLaunchDefaults.cs の IrodoriYwkCandidates()。
;         両版が入っている機体では **RTX（CUDA）版が起きる**（是正・段 G・low 26）。
;         綴り 2 つ（irodori-tts-ywk-cuda／-radeon）と MyDirName は 1 字も動かさない。
;       radeon 側の路は 1 字も変えない（本席の機体にも本体にも既に焼かれている）。
;   ⑶ **内部の識別子は 1 つも変えない**＝Flavor の id（cuda／radeon）・/DFlavor=・AppId の GUID・
;       setup の檔名（…-cuda.exe／…-radeon.exe）・台帳名（runtime-rocm-gfx1151）。英語の #error 文の
;       "the Radeon release" も道具の名（gfx1151 の機体）として残す＝**利用者に見せる版の名だけ**が
;       「ROCm 版」である。
;   ⑷ OldAppName／OlderAppName＝**過去の版の AppName の逐語**。[InstallDelete] が旧名の近道を消す。
;       OldAppName  ＝v1.1.0 世代（…（CUDA 版）／…（ROCm 版））
;       OlderAppName＝v1.0 世代（cuda は版なしの「irodori-TTS for 読み分けちゃん」・
;                     radeon は「…（Radeon 版）」）
;       **2 世代とも消す**（是正・2026-09-11・medium 10）＝v1.0.0／v1.0.1／v1.0.2 は **同じ AppId**で
;       公開済みなので、v1.0.x から v2.0.0 へ直に上げる機体（v1.1.0 を 1 度も通っていない機体）が実在する。
;       旧注釈の「枠は 1 つしかない」は **誤り**だった＝[InstallDelete] は行をいくつ書いてもよく、
;       下の 2 行は 1 つの枠を奪い合ってなどいない。判っている欠落ではなく、**塞いだ**穴である。
; 裁定 133（2026-09-11）＝**RTX（CUDA）と Radeon（ROCm）は別アプリ**＝データ樹も設定も声も共有しない。
;   ⑴ データ樹の名を **版ごと**に割る（MyDataDirName）＝%LOCALAPPDATA%\irodori-tts-ywk-cuda\／…-radeon\。
;       正本は launcher/IrodoriTtsYwk.Launcher/Contracts/AppPaths.cs の DataDirNameCuda／DataDirNameRadeon。
;   ⑵ AppMutex も **版ごと**（下の [Setup]）＝同 AppPaths.SingleInstanceMutexName の逐語。
;   ⑶ 旧い共有樹（%LOCALAPPDATA%\irodori-tts-ywk\）は移送の元としてだけ綴りが残る（LegacyDataDirName）。
#if Flavor == "radeon"
  #define MyAppId       "{ECA98712-1574-4D2A-A1FE-0FF5347BF185}"
  #define MyAppName     "irodori-TTS for 読み分けちゃん － Radeon（ROCm）"
  #define OldAppName    "irodori-TTS for 読み分けちゃん（ROCm 版）"
  #define OlderAppName  "irodori-TTS for 読み分けちゃん（Radeon 版）"
  #define MyDirName     "irodori-tts-ywk-radeon"
  #define MyDataDirName "irodori-tts-ywk-radeon"
#elif Flavor == "cuda"
  #define MyAppId       "{F228543A-DCF9-45A3-8826-7485C81E1757}"
  #define MyAppName     "irodori-TTS for 読み分けちゃん － RTX（CUDA）"
  #define OldAppName    "irodori-TTS for 読み分けちゃん（CUDA 版）"
  #define OlderAppName  "irodori-TTS for 読み分けちゃん"
  #define MyDirName     "irodori-tts-ywk-cuda"
  #define MyDataDirName "irodori-tts-ywk-cuda"
#else
  #error Flavor must be cuda or radeon
#endif
; 旧い共有樹（≦ v1.1.0 の既定）＝**両版で 1 本**だった。移送（v2.0 の初回起動・AppPaths の
; LegacyDataDirName）を通していない機体では約 8 GB が居残るので、撤去のときだけ面倒を見る（下）。
#define LegacyDataDirName "irodori-tts-ywk"

; --- compile 時の門（組み立ての取りこぼしを「黙って通さない」）-----------------
; ISCC 6.7.3 の実測（本席の実射・2026-09-05・scratchpad/ben-e1/xt の使い捨て 3 檔）＝
;   ⑴ wildcard が **1 檔も**当たらない ⇒ 「Error on line 9 in …\wild.iss: No files found matching
;      "…\src\empty\*"」「Compile aborted.」＝**exit 2**（ISCC が自分で止まる）。
;   ⑵ 名指しの Source が無い          ⇒ 「Error on line 10 in …\named.iss: Source file
;      "…\src\does-not-exist.json" does not exist.」「Compile aborted.」＝**exit 2**（同上）。
;   ⑶ wildcard が **一部だけ**当たる  ⇒ **止まらない**。voices\ から presets\ の枝ごと落とした樹を
;      「Source: …\voices\*; Flags: recursesubdirs createallsubdirs」で包んだ実測＝「Compressing:
;      …\voices\presets.json」「…\voices\voices.json」の 2 行だけで「Successful compile (0.688 sec).」
;      ＝**警告 0・exit 0 で wav 0 檔の setup が出た**。
; ⇒ **ここに要る門は ⑶ だけ**である。[Files] に名指しで書いてある檔（exe・ledger の各 json・docs の 2 檔）
;    の #if は ⑵ で ISCC が自分で止めるので **保険**（読む側への名札）であって、無くても止まる。
; ※ 旧注釈「Inno は『Source が 0 件』を既定では止めない＝EXIT=0・Successful compile のまま中身の欠けた
;    setup が出る」は **誤り**＝⑴⑵ で反証した。設計書 §3-2 冒頭と §8 危険 3 の同じ主張も訂正が要る（卓へ）。

; ⑶ の門 1（server\* の wildcard は 1 檔でも在れば通る）＝wrapper の本体を名指しで見る。
#if !FileExists(SrcApp + "\server\ywk_server.py")
  #error build/out/app/server is missing (run build/assemble-app.ps1 first)
#endif
; ⑶ の門 2（licenses\* の wildcard は同上）＝裁定 46 の通知文を名指しで見る。
#if !FileExists(SrcApp + "\licenses\first-run-notices.md")
  #error licenses/first-run-notices.md is missing (decisions.md 46)
#endif
; ⑶ の門 3（voices\* の wildcard は同上）＝プリセットの台帳を名指しで見る。
; **これだけでは足りない**＝presets.json は voices\ 直下に在り presets\ の中ではないので、
; presets\ の枝が丸ごと落ちてもこの門は満たされる（＝⑶ の実測そのもの）。次の門 4 が本体である。
#if !FileExists(SrcApp + "\voices\presets.json")
  #error voices/presets.json is missing (decisions.md 88 (5))
#endif
; ⑶ の門 4（**本体**）＝voices\presets\*.wav を ISPP の FindFirst/FindNext で数え、12 でなければ止める。
; 11 の出所＝decisions.md 37 の逐語「自作の生成物・配布物の同梱資産（**11 本**・32 MB）」／
; 裁定 88 ⑸「配布樹にプリセット 11 檔＋presets.json が在る検査」＝**ここまでが歴史**。
; **12 の出所＝裁定 118**（2026-09-10・司令官が録音を直に渡した 1 名
; `ext_hostclub_champagne.wav`＝台帳では engine "external"）＝いまの規則はこちら。
; 設計書 §3-2 の門 A-2 と同じ玉を .iss 側にも 1 枚張る
; （門 A-2＝build/installer-build.ps1 は E-2 席の持ち場で現時点では未実装）。
#define PresetWavCount 0
#define PresetFindHandle
#define PresetFindResult
#sub CountOnePresetWav
  #expr PresetWavCount = PresetWavCount + 1
#endsub
#expr PresetFindHandle = FindFirst(SrcApp + "\voices\presets\*.wav", 0)
#if PresetFindHandle
  #for {PresetFindResult = 1; PresetFindResult; PresetFindResult = FindNext(PresetFindHandle)} CountOnePresetWav
  #expr FindClose(PresetFindHandle)
#endif
#if PresetWavCount != 12
  #pragma message "voices\presets\*.wav count = " + Str(PresetWavCount) + " (expected 12)"
  #error voices/presets/*.wav is not 12 files (see the message line above for the count found)
#endif

; --- 以下は「保険」＝[Files] に名指しで書いてあるので ⑵ で ISCC が自分で止まる ----------------
#if !FileExists(SrcExe + "\IrodoriTtsYwk.Launcher.exe")
  #error the launcher exe is missing (run build/release-build.ps1 first)
#endif
#if Flavor == "radeon"
  #if !FileExists(SrcApp + "\ledger\runtime-rocm-gfx1151.json")
    #error ledger/runtime-rocm-gfx1151.json is missing (this is the Radeon release)
  #endif
#else
  #if !FileExists(SrcApp + "\ledger\runtime-cu130.json")
    #error ledger/runtime-cu130.json is missing (this is the CUDA release)
  #endif
  #if !FileExists(SrcApp + "\ledger\runtime-cu126.json")
    #error ledger/runtime-cu126.json is missing (this is the CUDA release)
  #endif
#endif
; rocm 台帳が CUDA 版の配布樹に紛れていないかは [Files] では見られない（写さないだけ）。
; 混入の検査は build/installer-build.ps1 の門 A-3（設計書 §3-2）が持つ。

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#AppVersionNumeric}
AppVerName={#MyAppName} {#AppVersion}
VersionInfoVersion={#AppVersionNumeric}
AppPublisher=irodori-tts-for-yomiwakechan（非公式・Aratako 氏とは無関係）
; ↑ この値は「設定 → アプリ」と「プログラムと機能」の**発行元**として利用者の目に入る＝
;   出典（裁定の番号）は出さない（憲章 §6-1・是正・段 G・medium 17）。理由の 2 語は残す。
DefaultDirName={autopf}\{#MyDirName}
DisableProgramGroupPage=yes
; ↑ これは効いている（既定＝auto なら「スタートメニューフォルダーを選ぶ」頁が出て、実射した 3 頁が 4 頁になる）。
;   DefaultGroupName は **書かない**＝[Icons] が {group} を 1 度も使わず {autoprograms} を直に指すので
;   グループ名は誰も読まない。書くと「グループを作っている」と誤読させる（敵対検分 low 4）。
PrivilegesRequired=lowest
; PrivilegesRequiredOverridesAllowed は書かない（既定＝空）＝昇格導入へ切り替えさせない
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex=Local\irodori-tts-ywk-launcher-{#Flavor}
; ↑ launcher/IrodoriTtsYwk.Launcher/Contracts/AppPaths.cs の逐語
;   public const string SingleInstanceNameBase = @"Local\irodori-tts-ywk-launcher";
;   SingleInstanceMutexName(flavor) => SingleInstanceNameBase + "-" + FlavorId(flavor);
;   FlavorId＝cuda／radeon＝この檔の /DFlavor= と **同じ綴り**（綴りの正本は AppPaths ただ 1 箇所）。
;   CHM 逐語＝「mutex name comparison in Windows is case sensitive」＝1 字も変えない。
; ↑ **裁定 133 ⑷ で版ごとに割った**（旧＝両版で 1 つ）。RTX（CUDA）と Radeon（ROCm）は別アプリなので
;   互いを起こし直さない。ランチャ側は錠と**合図の口**（…-activate-cuda／…-radeon）を必ず同じ組で
;   割ってある（片方だけ割ると、CUDA 版の 2 個目の起動が Radeon 版の窓を前に出す）。
;   これで直った古い癖＝もう一方の版のランチャが走っている最中にこちらを導入／撤去すると、Inno が
;   SetupAppRunningError を出して断り、しかもその文は **こちらの版の AppName** を名乗っていた。
;   いまは自分の版の個体だけを見る。
;   CHM 逐語＝「Specifies the name of a mutex which Setup **and Uninstall** should check」＝
;   **撤去でも同じ錠を見る**（＝自分の版のランチャが走っていれば撤去も断られる。これは正しい）。
; ↑ 2 つの版を同時に開いた回は、後から起きた側が **つなぎ口 18088 の塞がり**で止まる（裁定 133 ⑷）。
;   つなぎ口だけが 1 つで、錠は版ごとである。
RestartApplications=no
; CloseApplications は書かない（既定 yes のまま・頼らない＝設計書 §8 危険 6）
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
; Inno は「新版で消えた檔」を消さない。上流の写しから減った .py が ._pth 経由で import され続け、
; 種を跨いだ上書きでは ledger\ が混ざって cu130／cu126 が UI から消える（設計書 §1-3）。
Type: filesandordirs; Name: "{app}\server"
Type: filesandordirs; Name: "{app}\ledger"
Type: filesandordirs; Name: "{app}\licenses"
Type: filesandordirs; Name: "{app}\voices\presets"
; 旧名のスタートメニューの近道を 1 本消す（裁定 109 の改名）。
; Inno は [Icons] の名が変わっても **旧名の .lnk を消さない**（消えるのはアンインストールのときだけ・
; しかも消す名は「いま入っている版が書き込んだ名」）＝更新導入では新旧 2 本が並ぶ。
; 既存の導入は UsePreviousAppDir（既定 yes）で **場所はそのまま**なので、直すのは近道の名だけでよい。
; **2 世代ぶん書く**（是正・2026-09-11・medium 10）＝ここは「枠」ではなく行の並びで、何行でも書ける。
; v1.0.x は v1.1.0 と同じ AppId で公開済みなので、v1.0.x → v2.0.0 と直に上げた機体
; （v1.1.0 を 1 度も通っていない機体）には v1.0 世代の名の近道が残り、新旧 2 本が並ぶ。
;   OldAppName  ＝v1.1.0 世代（cuda「…（CUDA 版）」／radeon「…（ROCm 版）」）
;   OlderAppName＝v1.0 世代（cuda は版なし／radeon「…（Radeon 版）」）
Type: files; Name: "{autoprograms}\{#OldAppName}.lnk"
Type: files; Name: "{autoprograms}\{#OlderAppName}.lnk"
; 配布物から外した檔（v2.0 段 F-2）＝[Files] から消しても既に入っている機体では**消えない**。
; docs\install.md と docs\radeon.md は作る側の帳面へ戻したので、利用者機の写しをここで落とす。
; radeon.md（548 行）は調べの帳面＝リポの檔名・番号・研究の控えが利用者機に出ていた＝
; install.md と**同じ理由**で外した（憲章 原則 8・是正 2026-09-11・medium 16）。
; 利用者に要る 2 文（確かめたのは gfx1151 の 1 機種だけ・ほかは測っていない）は docs\guide.md §1 に畳んだ。
Type: files; Name: "{app}\docs\install.md"
Type: files; Name: "{app}\docs\radeon.md"

[Files]
Source: "{#SrcExe}\IrodoriTtsYwk.Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcApp}\server\*";   DestDir: "{app}\server";   Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__,*.pyc"
Source: "{#SrcApp}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__,*.pyc"
Source: "{#SrcApp}\voices\*";   DestDir: "{app}\voices";   Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__,*.pyc"
; ↑ Excludes は 3 行とも同じ形にする。server\ だけに付けていたのは誤りだった＝
;   `build/out/app/voices/__pycache__/sneaky.cpython-312.pyc` を置いて実射したら（2026-09-05 11:31）
;   ISCC は警告も出さずに **setup へ入れた**（`111 Compressing:` 行・良品は 110）。
;   配布樹は人がサーバを走らせる樹でもあるので、bytecode はどの枝にも落ちうる。
;   網の 2 枚目＝build/installer-build.ps1 の門 B-1（compile ログの `Compressing:` 行を読む）。
; ↑ voices.json（80 B）と voices.ywk.json（596 B）も入る。**除かない**＝voices.ywk.json は
;   launcher/IrodoriTtsYwk.Launcher/Services/Voices/PresetVoices.cs:54 の逐語
;   「?? FromTable(Path.Combine(appVoices, "voices.ywk.json"), appVoices)」で **配布樹側が読まれる**
;   （presets.json が 0 件のときの落ち先）。利用者の台帳と同名だが、{app} をデータ樹に重ねる経路は
;   [Code] の NextButtonClick が断つので衝突しない（敵対検分 medium 4 の手当て）。
Source: "{#SrcApp}\ledger\README.md";         DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\models.json";       DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\python-embed.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\vc_redist.json";    DestDir: "{app}\ledger"; Flags: ignoreversion
#if Flavor == "radeon"
Source: "{#SrcApp}\ledger\runtime-rocm-gfx1151.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\runtime-cpu.json";          DestDir: "{app}\ledger"; Flags: ignoreversion
#else
Source: "{#SrcApp}\ledger\runtime-cu130.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\runtime-cu126.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\runtime-cpu.json";   DestDir: "{app}\ledger"; Flags: ignoreversion
#endif
; docs は明示列挙する（一括写しにすると acceptance.md・contract.md が利用者機へ出る＝設計書 §1-5）。
Source: "{#Repo}\README.md";      DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#Repo}\docs\guide.md";  DestDir: "{app}\docs"; Flags: ignoreversion
; ↑ **v2.0 段 F-2 で install.md を guide.md に替えた**＝docs\install.md は**作る側の帳面**へ戻した
;   （リポの番号・檔名・実測を引く文が利用者機に出ていた＝憲章 原則 8）。
;   利用者向けの 1 檔は docs\guide.md ただ 1 つで、〔このアプリについて〕の〔使い方を見る〕
;   （AboutGuideButton）が開くのもこれである（MainWindow.xaml.cs の OpenGuide が**その 1 檔を選んで**開く）。
; ↑ **docs\radeon.md も同じ回に外した**（是正・2026-09-11・medium 16）＝install.md と同じ理由である。
;   これで Radeon 版の {app}\docs も README.md と guide.md の 2 檔＝**両版で同じ構え**になった。
;   既に入っている機体の {app}\docs\install.md ／ {app}\docs\radeon.md は [Files] から消しても
;   **消えない**ので、上の [InstallDelete] の 2 行が消す。

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\IrodoriTtsYwk.Launcher.exe"

[Run]
Filename: "{app}\IrodoriTtsYwk.Launcher.exe"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; {app} を filesandordirs で丸ごと消す形は採らない（CHM 逐語の「DON'T DO THIS!」）。
; {app}\server はこちらが作った枝で利用者の檔が 1 つも無い。dirifempty は中身が在れば何もしない。
Type: filesandordirs; Name: "{app}\server"
Type: dirifempty;     Name: "{app}"
; ↑ **データ樹はここに書かない**（裁定 132／133 ⑴ でも同じ）。理由は junction である＝
;   filesandordirs を reparse point に撃つと **路だけが消えて実体（D:\ywk-data 等）が孤児になる**。
;   データ樹は下の CurUninstallStepChanged が、junction を見分けたうえで **中身から** 消す。

[Messages]
; 歓迎頁は既定で出ない（DisableWelcomePage の既定＝yes）＝WelcomeLabel2 に文言を置かない。
; 数字は準備完了頁と完了頁の 2 箇所だけ（設計書 §6-2）。
; **種ごとに切り替える**（敵対検分 medium 3）＝ROCm 版の利用者に、その版が絶対に取得できない
; cu130 の数字（5.26 GiB／11.56 GiB）を出さない。数字の出所＝ledger/README.md §7 の表と、
; 本席が台帳 json から FetchPlanner.cs:54,57-59,65 の式を踏み直した実測（§11-8 に逐語）。
; **名札は v2.0 段 F の綴り**（是正・2026-09-11・medium 15）＝この頁は DisableReadyPage を立てて
; いないので**利用者が読む**。窓題と近道が「… － RTX（CUDA）」なのに本文が「CUDA 版」では食い違う。
; **段 G（2026-09-11）で 4 行とも書き直した**（high 15・medium 18／26／27）＝段 F が替えたのは
; ReadyLabel2a／2b の名札だけで、⑴ 完了頁の 4 行には「取得」「第三者物」「変種」「cu130」が、
; ⑵ 準備完了頁には 1024 進の綴り（MiB／GiB）が残っていた。どちらも利用者が読む頁である
; （憲章 §6-1＝画面は GB で通す・詳細と帳面は GiB のまま／v2-copy.md §9-2＝cu130・cu126 は出さない）。
;   ・本体の量＝**約 96 MB**（旧「約 106 MiB」は 2026-09-05 の実測で、exe が 69,610,930 B・
;     配布樹が 37,111,434 B だったころの値。v2.0.0 は exe 61,779,808 B（門 A-6）・樹 34,088,381 B（門 A-1）＝
;     [Files] を足し直すと cuda 95,795,232 B・radeon 95,727,882 B、これに unins000 の対（実測 4,390,361 B）を
;     足して 100,185,593 B／100,118,243 B＝**95.5 MiB**。丸めて 96 MB。実数はこの註と帳面に残す）。
;   ・ダウンロードの量と空き＝台帳の実測（ledger/README.md §7）を GB の札に替えただけで、数は動かさない＝
;     RTX（CUDA）5.26 GiB→**約 5.3 GB**・Radeon（ROCm）4.79 GiB→**約 4.8 GB**、
;     空きは 14.45 GiB→**最大 15 GB ほど**・9.55 GiB→**最大 10 GB ほど**（4 通りの内訳は帳面へ）。
#if Flavor == "radeon"
ja.ReadyLabel2a=ここで入るのはアプリ本体（約 96 MB）だけです。動かすための一式と声のデータ（約 4.8 GB）は、最初にこのアプリを開いたときにダウンロードします。%n%nインストールを続行するには「インストール」を、設定の確認や変更を行うには「戻る」をクリックしてください。
ja.ReadyLabel2b=ここで入るのはアプリ本体（約 96 MB）だけです。動かすための一式と声のデータ（約 4.8 GB）は、最初にこのアプリを開いたときにダウンロードします。%n%nインストールを続行するには「インストール」をクリックしてください。
ja.FinishedLabel=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に開くと、お知らせが 1 つ出ます。同意すると、はじめの準備（約 4.8 GB のダウンロード・10 分ほど）が始まります。途中で最大 10 GB ほどの空きが要ります。
ja.FinishedLabelNoIcons=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に開くと、お知らせが 1 つ出ます。同意すると、はじめの準備（約 4.8 GB のダウンロード・10 分ほど）が始まります。途中で最大 10 GB ほどの空きが要ります。
#else
ja.ReadyLabel2a=ここで入るのはアプリ本体（約 96 MB）だけです。動かすための一式と声のデータ（約 5.3 GB）は、最初にこのアプリを開いたときにダウンロードします。%n%nインストールを続行するには「インストール」を、設定の確認や変更を行うには「戻る」をクリックしてください。
ja.ReadyLabel2b=ここで入るのはアプリ本体（約 96 MB）だけです。動かすための一式と声のデータ（約 5.3 GB）は、最初にこのアプリを開いたときにダウンロードします。%n%nインストールを続行するには「インストール」をクリックしてください。
ja.FinishedLabel=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に開くと、お知らせが 1 つ出ます。同意すると、はじめの準備（約 5.3 GB のダウンロード・10 分ほど）が始まります。途中で最大 15 GB ほどの空きが要ります。
ja.FinishedLabelNoIcons=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に開くと、お知らせが 1 つ出ます。同意すると、はじめの準備（約 5.3 GB のダウンロード・10 分ほど）が始まります。途中で最大 15 GB ほどの空きが要ります。
#endif

[Code]

const
  { もう一方の種のアンインストール鍵（AppId + '_is1'）。
    裁定 133 ⑶ でデータ樹の共有が無くなったので、いま見る用途は **1 つだけ**＝
    旧い共有樹（%LOCALAPPDATA%\irodori-tts-ywk\）を道連れにしてよいかの判定である
    （向こうがまだ移送で要るなら触らない）。}
#if Flavor == "radeon"
  OtherFlavorKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F228543A-DCF9-45A3-8826-7485C81E1757}_is1';
#else
  OtherFlavorKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{ECA98712-1574-4D2A-A1FE-0FF5347BF185}_is1';
#endif
  { 取得中に要る空き＝FetchPlan.EstimatedPeakDiskBytes（FetchPlanner.cs:54,57-59,65）＝
    CacheBytes ＋ (python-embed + runtime) * ExpansionFactor(3.3) ＋ ModelBytes。
    本席が build/out/app/ledger/*.json の size を足し直した実測（2026-09-05）＝
      cpu 4,862,463,481（4.53 GiB）／cu130 12,410,119,910（11.56 GiB）／
      cu126 15,515,873,265（14.45 GiB）／rocm-gfx1151 10,252,286,678（9.55 GiB）。
    ledger/README.md:297-300 の表（4.53／11.56／14.45／9.55 GiB）と 1 桁も食い違わない。
    ここに据えるのは **その版が選ばせる変種の最大**＝「最大 … の空きが要ります」を嘘にしないため
    （敵対検分 medium 3）。設計書 §6-3 ⑵ は種を分ける前の値（cu130 の 11.56 GiB）なので卓へ回した。
    旧値 12413511598 はこのリポのどこからも導けない手作りの数字だった（同 low 2）。}
#if Flavor == "radeon"
  PeakDiskBytes = 10252286678;
  PeakDiskText  = '10 GB';
  FetchText     = '約 4.8 GB';
  VariantText   = 'Radeon（ROCm）';
#else
  PeakDiskBytes = 15515873265;
  PeakDiskText  = '15 GB';
  FetchText     = '約 5.3 GB';
  VariantText   = 'RTX（CUDA）';
#endif
  BytesPerGiB = 1073741824;
  { 画面に出す桁は 10 進で丸める（憲章 §6-1＝画面は GB・詳細と帳面は GiB のまま）＝
    上の PeakDiskBytes は 1 バイトも動かさない（空きの判定はバイトで行う）。 }
  BytesPerGB = 1000000000;

var
  SpaceNotified: Boolean;

function DataDir(): String;
begin
  { AppPaths.DataDir の既定と同じ場所を固定で解く。**版ごと**である（裁定 133 ⑶）＝
    MyDataDirName は AppPaths.DataDirNameCuda／DataDirNameRadeon の逐語。
    env（YWK_LAUNCHER_DATA_DIR）と settings.json の dataDir は見ない＝設計書 §1-7。
    1 本でも env を立てるとランチャが「開発起動」を名乗る。}
  Result := ExpandConstant('{localappdata}\' + '{#MyDataDirName}');
end;

function LegacyDataDir(): String;
begin
  { ≦ v1.1.0 が両版で分け合っていた **旧い共有樹**（AppPaths.LegacyDataDirName の逐語）。
    v2.0 の初回起動で版の樹へ移る（AppPaths.EnsureDataDirectories → LegacyDataMigration）が、
    ⑴ v1.1.0 に上書き導入して **一度も開かずに** 撤去した機体 ⑵ 移送に失敗して旧樹が残った機体
    では移送が走っていない＝約 8 GB が丸ごと居残る（裁定 132 が起こされた当の状態）。}
  Result := ExpandConstant('{localappdata}\' + '{#LegacyDataDirName}');
end;

function StartsWithDir(const Path, Prefix: String): Boolean;
begin
  Result := (Prefix <> '') and (Pos(Lowercase(AddBackslash(Prefix)), Lowercase(AddBackslash(Path))) = 1);
end;

{ データ樹が junction（mklink /J＝reparse point）かどうかを見る 1 本。Inno に尋ねる関数が無いので
  Win32 を直に引く。Inno 6 の [Code] は Unicode なので W 版を名指しする。
  用途は 1 つだけ＝撤去のときの WipeTree が、junction を丸ごと撃たないため（下）。}
function GetFileAttributesW(lpFileName: String): DWORD;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function IsReparsePoint(const Path: String): Boolean;
var
  Attr: DWORD;
begin
  Attr := GetFileAttributesW(Path);
  { $FFFFFFFF = INVALID_FILE_ATTRIBUTES（引けなかった）／$400 = FILE_ATTRIBUTE_REPARSE_POINT。}
  Result := (Attr <> $FFFFFFFF) and ((Attr and $400) <> 0);
end;

procedure NotifyFreeSpace();
var
  FreeBytes, TotalBytes: Int64;
begin
  { 空きの告知（**止めない**）。見るのは導入先ではなく **取得物が落ちるドライブ**＝設計書 §6-3。
    問い合わせ先を DataDir() ではなく localappdata 直下にしてある＝GetSpaceOnDisk64 は
    「まだ無いディレクトリ」に False を返す（台本席が実射）。まっさらな機体＝いちばん警告が要る機体で
    黙ってしまうのを避ける。ドライブは同じなので数字は変わらない。
    **1 周に 1 回だけ**＝場所の頁と、その頁が出ない経路（更新導入＝DisableDirPage の既定 auto で
    wpSelectDir 頁ごと出ない）の両方から呼ぶ（敵対検分 low 1）。
    無人（/SILENT・/VERYSILENT）では **出さない**＝無人の走を MsgBox で止めないため。}
  if SpaceNotified then
    Exit;
  SpaceNotified := True;
  if WizardSilent then
    Exit;
  if not GetSpaceOnDisk64(ExpandConstant('{localappdata}'), FreeBytes, TotalBytes) then
    Exit;
  if FreeBytes >= PeakDiskBytes then
    Exit;
  { 綴りは §6-1 の語彙（是正・段 G・medium 16）＝まっさらな機体でいちばん出やすい窓である。
    「取得」「変種」「初回取得」「ランチャ」「落とすバイト」「GiB」を落とし、内訳は帳面へ回した。 }
  SuppressibleMsgBox('ダウンロードした物は ' + DataDir() + ' に入ります。'
       + 'このドライブの空きは約 ' + IntToStr(FreeBytes div BytesPerGB) + ' GB です。' + #13#10
       + 'はじめの準備には途中で最大 ' + PeakDiskText + ' ほどの空きが要ります'
       + '（' + VariantText + ' でダウンロードするのは ' + FetchText + '）。' + #13#10
       + 'このまま入れられます。準備を始めるときに、アプリがもう一度確かめます。',
       mbInformation, MB_OK, IDOK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    Exit;

  Dir := WizardDirValue;

  { ⑴ 非昇格では書けない場所を断る（ここで断らないと [Files] の途中で落ちる）。
    commonpf・commonpf32・win の 3 定数が非昇格でも例外なく展開できることは台本席が実射で確かめた。
    ※ Pascal の注釈は入れ子にならないので、この中に波括弧の定数を書かない。}
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

  { ⑵ **データ樹に重ねさせない**（敵対検分 medium 4）。設計の中心＝設計書 §1-1
    「インストーラはデータ樹に 1 檔も作らず、更新でも 1 檔も触らない」を機械で守る 1 本。
    重ねると [Files] の voices\ が利用者の voices.json（AppPaths.cs:104）と
    voices.ywk.json（同 :107）を 80 B・596 B の雛形で黙って上書きし、登録済みの話者が全部消える。
    復旧路も無い（LauncherComposition.cs:89-91＝voices.json は voices.ywk.json から作り直すだけ）。
    両向きを見る＝導入先がデータ樹の中／データ樹が導入先の中。}
  if StartsWithDir(Dir, DataDir()) or StartsWithDir(DataDir(), Dir) then
  begin
    { 綴りは §6-1・v2-copy.md §9-1 の語彙（是正・段 G・medium 20）＝「取得物」「話者」「台帳」
      「配布物」と檔名 2 つを落とす（憲章 原則 6＝檔名は画面に出さず記録へ）。 }
    MsgBox('その場所は、このアプリがダウンロードした物と声を置く場所（' + DataDir() + '）と重なっています。'
         + 'ここに入れると、追加した声の一覧が新品の状態で上書きされ、元に戻せません。'
         + '別の場所を選んでください。既定は' + #13#10
         + ExpandConstant('{autopf}') + '\' + '{#MyDirName}' + ' です。', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  { ⑶ **旧い共有樹にも重ねさせない**（是正・2026-09-11）。⑵ は版ごとの樹しか見ないので、
    **まだ移送していない機体**（v1.1.0 から上げた機体＝声も設定も約 8 GB が旧樹に居る）では
    その路が素通りしてしまう。そこへ入れると ⑴ 初回起動の移送が「自分の exe が入ったままの樹」を
    Directory.Move しようとして錠で失敗し ⑵ 撤去のときの WipeTree(Legacy) が残りを消す。}
  if StartsWithDir(Dir, LegacyDataDir()) or StartsWithDir(LegacyDataDir(), Dir) then
  begin
    { 同・是正・段 G・low 21＝「樹」（配布樹／データ樹と同じ内輪の言い方）と「話者」を落とす。 }
    MsgBox('その場所は以前の版の置き場（' + LegacyDataDir() + '）と重なっています。'
         + 'そこには前に追加した声と設定が残っていることがあり、ここに入れると'
         + '引き継ぎが通らなくなります。別の場所を選んでください。既定は' + #13#10
         + ExpandConstant('{autopf}') + '\' + '{#MyDirName}' + ' です。', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  NotifyFreeSpace();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { 更新導入では場所の頁が出ない（CHM 逐語＝DisableDirPage の既定 auto＝同じアプリが入っていれば
    「Select Destination Location」頁を出さない）＝NextButtonClick(wpSelectDir) が呼ばれない。
    いちばん多い経路で告知が沈黙するのを避ける（敵対検分 low 1）。ここは無人でも必ず通る。}
  Result := '';
  NotifyFreeSpace();
end;

{ 置き場を 1 本、**中身から**消す（junction を通り抜けて実体を消し、路そのものは junction のときだけ残す）。
  CHM 逐語（ISetup.chm topic_isxfunc_deltree）＝「This function will remove directories that are
  reparse points, but it will **not** recursively delete files/directories inside them.」＝
  この免除が当たるのは reparse point **自身**であって、その下の檔ではない。だから
  ⑴ ふつうのディレクトリは DelTree(Dir, True, True, True)（＝既に実績のある形）で丸ごと消し、
  ⑵ junction のときだけ DelTree(Dir + '\*', False, True, True)（IsDir=False の wildcard）で
  **中身だけ**を落として路を残す。
  ※ junction 自身に RemoveDir を撃つと **路だけが消えて実体が孤児になる**ことは実射で確かめた
    （2026-09-05・非昇格・使い捨ての .iss を ISCC で通し InitializeSetup で撃って中止）＝
      before RemoveDir: DirExists(link)=1 / RemoveDir returned: 1
      after  RemoveDir: DirExists(link)=0 / DirExists(real)=1 / FileExists(real\settings.json)=1
    docs/install.md §5-3 が勧める mklink /J を張った機体で、入れ直すと空のデータ樹が C: に出来る。
  ※ **置き場の外には 1 檔も触れない**（裁定 133 ⑵）＝声を追加するときに利用者が選んだ元の wav は
    利用者の檔で、アプリは voices\refs\ に写しを取り込んで使う（VoiceStore はその形を守る）。
    ここで消えるのは写しだけである。}
{ ※ **入れ子の junction にも当たる**（是正・2026-09-11）。CHM の免除は reparse point 自身にしか
  当たらないので、根だけを見て DelTree を撃つと `<data>\models` だけを別ドライブへ逃がした樹
  （3 GB のモデルだけを退かす、いちばん自然な手）は **環が消えて実体が孤児になる**＝
  裁定 132 が起こされた当の症状が、撤去のあとの D:\ に数 GB として残る。だから
  **すぐ下の枝を先にこの手続き自身へ渡す**＝入れ子の junction は上の枝で「中身だけ落とす」に当たり、
  空になった環は最後の DelTree(Dir, ...) が畳む（根の環だけは残す＝下）。
  枝の名は **先に読み切ってから** 撃つ＝消しながら FindNext を続けない。}
procedure WipeTree(const Dir: String);
var
  FindRec: TFindRec;
  Names: TArrayOfString;
  Count, I: Integer;
begin
  if not DirExists(Dir) then
    Exit;

  if IsReparsePoint(Dir) then
  begin
    { 路を丸ごと撃つと免除が当たって **中身が残ったまま路だけ消える**。だから中身を名指しで落とし、
      路（junction 自身）は残す＝入れ直しに mklink /J を張り直さなくてよい。}
    DelTree(Dir + '\*', False, True, True);
    Log('the tree is a reparse point (junction); emptied it but left the link: ' + Dir);
    Exit;
  end;

  Count := 0;
  SetArrayLength(Names, 0);
  if FindFirst(AddBackslash(Dir) + '*', FindRec) then
  begin
    try
      repeat
        { $10 = FILE_ATTRIBUTE_DIRECTORY（上の $400 と同じで、数で書く）。}
        if (FindRec.Name <> '.') and (FindRec.Name <> '..')
           and ((FindRec.Attributes and $10) <> 0) then
        begin
          SetArrayLength(Names, Count + 1);
          Names[Count] := AddBackslash(Dir) + FindRec.Name;
          Count := Count + 1;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  for I := 0 to Count - 1 do
    WipeTree(Names[I]);

  DelTree(Dir, True, True, True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Legacy: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  { 裁定 132／133 ⑴＝**このアプリが自分の置き場に持っている物をすべて消す。問わない。**
    消える物＝ダウンロードした一式（runtime\）・モデル（models\）・取り込んだ声の写し（voices\refs\）・
    下ごしらえ（voices\latents\）・settings.json・logs\・取得キャッシュ（cache\）・MIOpen の db。
    司令官の逐語（裁定 132）＝「アンインストールで数Gバイト残るのは普通にアプリの範囲を逸脱している。」
    問いは **1 つも出さない**（裁定 132 の「追加した声と設定を消しますか」も 133 ⑴ で廃止）＝
    無人（/SUPPRESSMSGBOXES）でも有人でも同じ道を通る＝**/SUPPRESSMSGBOXES はもう効かない**。
    **もう一方の版の樹には触れない**＝共有が無くなったので「道連れにしない」場合分けは規則ごと廃止した
    （旧 ben-e §5-4）。}
  WipeTree(DataDir());

  { 旧い共有樹（≦ v1.1.0）＝**もう一方の版が入っていないときだけ**一緒に消す（裁定 133 ⑸・
    v2-spec.md §11-8）。向こうが入っている回は触らない＝向こうがまだ移送で要る。
    判定は既にある OtherFlavorKey の RegKeyExists をそのまま使う。}
  Legacy := LegacyDataDir();
  if (CompareText(Legacy, DataDir()) <> 0)
     and DirExists(Legacy)
     and not RegKeyExists(HKEY_CURRENT_USER, OtherFlavorKey) then
  begin
    Log('removing the legacy shared data tree (the other flavour is not installed): ' + Legacy);
    WipeTree(Legacy);
  end;
end;
