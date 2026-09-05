; installer/irodori-tts-ywk.iss -- 便 E。per-user・非昇格・第三者バイナリ 0（decisions.md 8）。
; 設計＝docs/design/ben-e-installer.md §1・§2・§5・§6-3。裁定＝decisions.md 89・90・91。
; 是正＝同 §11-8（敵対検分の medium 6 件・low 4 件・2026-09-05・是正席 opus）。
; 呼び方＝build/installer-build.ps1 が ISCC に /D を 7 本渡す。この檔に版・路・上流 pin を書き写さない。
;   /DAppVersion=v0.1.0 /DAppVersionNumeric=0.1.0 /DFlavor=cuda|radeon
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
; ISCC 6.7.3 の実測（本席の実射・2026-09-05・scratchpad/ben-e1/xt の使い捨て 3 檔）＝
;   ⑴ wildcard が **1 檔も**当たらない ⇒ 「Error on line 9 in …\wild.iss: No files found matching
;      "…\src\empty\*"」「Compile aborted.」＝**exit 2**（ISCC が自分で止まる）。
;   ⑵ 名指しの Source が無い          ⇒ 「Error on line 10 in …\named.iss: Source file
;      "…\src\does-not-exist.json" does not exist.」「Compile aborted.」＝**exit 2**（同上）。
;   ⑶ wildcard が **一部だけ**当たる  ⇒ **止まらない**。voices\ から presets\ の枝ごと落とした樹を
;      「Source: …\voices\*; Flags: recursesubdirs createallsubdirs」で包んだ実測＝「Compressing:
;      …\voices\presets.json」「…\voices\voices.json」の 2 行だけで「Successful compile (0.688 sec).」
;      ＝**警告 0・exit 0 で wav 0 檔の setup が出た**。
; ⇒ **ここに要る門は ⑶ だけ**である。[Files] に名指しで書いてある檔（exe・ledger の各 json・docs\radeon.md）
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
; ⑶ の門 4（**本体**）＝voices\presets\*.wav を ISPP の FindFirst/FindNext で数え、11 でなければ止める。
; 11 の出所＝decisions.md 37 の逐語「自作の生成物・配布物の同梱資産（**11 本**・32 MB）」／
; 裁定 88 ⑸「配布樹にプリセット 11 檔＋presets.json が在る検査」。設計書 §3-2 の門 A-2 と同じ玉を
; .iss 側にも 1 枚張る（門 A-2＝build/installer-build.ps1 は E-2 席の持ち場で現時点では未実装）。
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
#if PresetWavCount != 11
  #pragma message "voices\presets\*.wav count = " + Str(PresetWavCount) + " (expected 11)"
  #error voices/presets/*.wav is not 11 files (see the message line above for the count found)
#endif

; --- 以下は「保険」＝[Files] に名指しで書いてあるので ⑵ で ISCC が自分で止まる ----------------
#if !FileExists(SrcExe + "\IrodoriTtsYwk.Launcher.exe")
  #error the launcher exe is missing (run build/release-build.ps1 first)
#endif
#if Flavor == "radeon"
  #if !FileExists(SrcApp + "\ledger\runtime-rocm-gfx1151.json")
    #error ledger/runtime-rocm-gfx1151.json is missing (this is the Radeon release)
  #endif
  #if !FileExists(Repo + "\docs\radeon.md")
    #error docs/radeon.md is missing (the Radeon release ships it)
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
AppPublisher=irodori-tts-for-yomiwakechan（非公式・Aratako 氏とは無関係＝decisions.md 1）
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
AppMutex=Local\irodori-tts-ywk-launcher
; ↑ launcher/IrodoriTtsYwk.Launcher/App.xaml.cs:32 の逐語
;   private const string MutexName = @"Local\irodori-tts-ywk-launcher";
;   CHM 逐語＝「mutex name comparison in Windows is case sensitive」＝1 字も変えない
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
Source: "{#Repo}\docs\radeon.md";                     DestDir: "{app}\docs";   Flags: ignoreversion
#else
Source: "{#SrcApp}\ledger\runtime-cu130.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\runtime-cu126.json"; DestDir: "{app}\ledger"; Flags: ignoreversion
Source: "{#SrcApp}\ledger\runtime-cpu.json";   DestDir: "{app}\ledger"; Flags: ignoreversion
#endif
; docs は明示列挙する（一括写しにすると acceptance.md・contract.md が利用者機へ出る＝設計書 §1-5）。
Source: "{#Repo}\README.md";       DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#Repo}\docs\install.md"; DestDir: "{app}\docs"; Flags: ignoreversion

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

[Messages]
; 歓迎頁は既定で出ない（DisableWelcomePage の既定＝yes）＝WelcomeLabel2 に文言を置かない。
; 数字は準備完了頁と完了頁の 2 箇所だけ（設計書 §6-2）。
; **種ごとに切り替える**（敵対検分 medium 3）＝Radeon 版の利用者に、その版が絶対に取得できない
; cu130 の数字（5.26 GiB／11.56 GiB）を出さない。数字の出所＝ledger/README.md §7 の表と、
; 本席が台帳 json から FetchPlanner.cs:54,57-59,65 の式を踏み直した実測（§11-8 に逐語）。
#if Flavor == "radeon"
ja.ReadyLabel2a=ここで入るのは本体（約 106 MiB）だけです。実行系とモデル（Radeon 版 rocm-gfx1151 で 4.79 GiB）は、最初にランチャを起動したときに取得します。%n%nインストールを続行するには「インストール」を、設定の確認や変更を行うには「戻る」をクリックしてください。
ja.ReadyLabel2b=ここで入るのは本体（約 106 MiB）だけです。実行系とモデル（Radeon 版 rocm-gfx1151 で 4.79 GiB）は、最初にランチャを起動したときに取得します。%n%nインストールを続行するには「インストール」をクリックしてください。
ja.FinishedLabel=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に起動すると、取得する第三者物の通知が出ます。同意すると取得が始まります（100 Mbps 級で 10 分ほど）。取得の途中では最大 9.55 GiB の空きが要ります（CPU 変種なら 4.53 GiB）。
ja.FinishedLabelNoIcons=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に起動すると、取得する第三者物の通知が出ます。同意すると取得が始まります（100 Mbps 級で 10 分ほど）。取得の途中では最大 9.55 GiB の空きが要ります（CPU 変種なら 4.53 GiB）。
#else
ja.ReadyLabel2a=ここで入るのは本体（約 106 MiB）だけです。実行系とモデル（CUDA 版は既定の cu130 で 5.26 GiB・cu126 なら 5.93 GiB）は、最初にランチャを起動したときに取得します。%n%nインストールを続行するには「インストール」を、設定の確認や変更を行うには「戻る」をクリックしてください。
ja.ReadyLabel2b=ここで入るのは本体（約 106 MiB）だけです。実行系とモデル（CUDA 版は既定の cu130 で 5.26 GiB・cu126 なら 5.93 GiB）は、最初にランチャを起動したときに取得します。%n%nインストールを続行するには「インストール」をクリックしてください。
ja.FinishedLabel=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に起動すると、取得する第三者物の通知が出ます。同意すると取得が始まります（100 Mbps 級で 10 分ほど）。取得の途中では最大 14.45 GiB の空きが要ります（既定の cu130 なら 11.56 GiB・CPU 変種なら 4.53 GiB）。
ja.FinishedLabelNoIcons=ご使用のコンピューターに [name] がセットアップされました。%n%n最初に起動すると、取得する第三者物の通知が出ます。同意すると取得が始まります（100 Mbps 級で 10 分ほど）。取得の途中では最大 14.45 GiB の空きが要ります（既定の cu130 なら 11.56 GiB・CPU 変種なら 4.53 GiB）。
#endif

[Code]

const
  { もう一方の種のアンインストール鍵（AppId + '_is1'）。データ樹を共有するので道連れを避ける（設計書 §5-4）。}
#if Flavor == "radeon"
  OtherFlavorKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F228543A-DCF9-45A3-8826-7485C81E1757}_is1';
  OtherFlavorName = 'CUDA 版';
#else
  OtherFlavorKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{ECA98712-1574-4D2A-A1FE-0FF5347BF185}_is1';
  OtherFlavorName = 'Radeon 版';
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
  PeakDiskText  = '9.55 GiB';
  FetchText     = '4.79 GiB';
  VariantText   = 'Radeon 版（rocm-gfx1151）';
#else
  PeakDiskBytes = 15515873265;
  PeakDiskText  = '14.45 GiB';
  FetchText     = 'cu130 で 5.26 GiB・cu126 で 5.93 GiB';
  VariantText   = 'CUDA 版（既定は cu130・いちばん食うのは cu126）';
#endif
  BytesPerGiB = 1073741824;

var
  SpaceNotified: Boolean;

function DataDir(): String;
begin
  { AppPaths.DataDir の既定と同じ場所を固定で解く。env（YWK_LAUNCHER_DATA_DIR）は見ない＝設計書 §1-7。
    1 本でも env を立てるとランチャが「開発起動」を名乗る（AppPaths.cs:205）。}
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

{ データ樹が junction（mklink /J＝reparse point）かどうかを見る 1 本。Inno に尋ねる関数が無いので
  Win32 を直に引く。Inno 6 の [Code] は Unicode なので W 版を名指しする。
  用途は 1 つだけ＝アンインストールの最後の RemoveDir(Data) を、junction には撃たないため（下）。}
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
  SuppressibleMsgBox('取得したものは ' + DataDir() + ' に入ります。'
       + 'このドライブの空きは約 ' + IntToStr(FreeBytes div BytesPerGiB) + ' GiB です。' + #13#10
       + VariantText + ' の初回取得には途中で最大 ' + PeakDiskText + ' の空きが要ります'
       + '（落とすバイトは ' + FetchText + '）。CPU 変種なら 4.53 GiB で足ります。' + #13#10
       + '導入はこのまま続けられます。取得のときにランチャがもう一度確かめます。',
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
    MsgBox('その場所は取得物と話者の置き場（' + DataDir() + '）と重なっています。'
         + 'ここに入れると、登録済みの話者台帳（voices\voices.json・voices\voices.ywk.json）が'
         + '配布物の雛形で上書きされ、元に戻せません。別の場所を選んでください。既定は' + #13#10
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

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  R: Integer;
  Data, Extra: String;
  OtherInstalled: Boolean;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  Data := DataDir();
  if not DirExists(Data) then
    Exit;

  OtherInstalled := RegKeyExists(HKEY_CURRENT_USER, OtherFlavorKey);

  { 1 段目＝取得物。もう一方の種がまだ入っていれば **尋ねない**（共有のデータ樹を道連れにしない）。
    無人（/SUPPRESSMSGBOXES）では既定＝IDNO が返る＝黙って残る。}
  if not OtherInstalled then
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
      { ※ junction（mklink /J）でデータ樹を別ドライブへ逃がした利用者では、**実体の中身が消える**。
        CHM 逐語（ISetup.chm topic_isxfunc_deltree）＝「This function will remove directories that are
        reparse points, but it will **not** recursively delete files/directories inside them.」＝
        この免除が当たるのは reparse point 自身（＝Data）であって Data\runtime 等ではない。
        ここは Data の **下** を名指しで消すので、junction を通り抜けて実体が消える。
        これは意図した挙動である（利用者が明示で「削除する」を選んだのだから消えるのが素直）＝
        docs/install.md §5-3 は 2026-09-05 にこの実装に合わせて直っており、いまは食い違っていない
        （旧注釈は「檔と食い違う・卓へ回した」と書いていたが、その票は既に閉じている）。
        ※ junction を張っての実射はしていない（D: は停止域）＝**推測**。
        junction 自身（＝Data）を畳まないことだけは実射で確かめた＝この手続きの最後の RemoveDir。}
      DelTree(Data + '\runtime', True, True, True);
      DelTree(Data + '\models',  True, True, True);
      DelTree(Data + '\cache',   True, True, True);
      DelTree(Data + '\logs',    True, True, True);
      DelTree(Data + '\miopen',  True, True, True);
    end;
  end;

  { 2 段目＝利用者の資産。docs/install.md §5 の逐語「消すには明示の選択が要る」の口。
    1 段目と違って **抑止はしない**＝docs/install.md §5-4 の 3 が「別に尋ねる」と例外なしで約束しており、
    抑止すると利用者が話者を消す唯一の口を失う。代わりに、もう一方の種が居るときは
    「そちらからも消える」ことを本文で告げる（敵対検分 medium 5 の ⑵ を採った。
    ⑴＝2 段目も抑止する案は卓の裁定待ち＝decisions.md 90 は 1 段目しか触れていない）。}
  Extra := '';
  if OtherInstalled then
    Extra := #13#10 + OtherFlavorName + 'がまだこの機体に入っています。話者と設定は 2 つの版が'
           + '同じ場所（' + Data + '）を共有しているので、ここで消すとそちらからも消えます。';
  R := SuppressibleTaskDialogMsgBox(
         '登録した話者と設定も削除しますか？',
         '追加した参照 wav・話者の名前・設定は利用者の資産です。既定では残します。' + Extra,
         mbConfirmation, MB_YESNO,
         TwoLabels('削除する' + #13#10 + 'voices\ と settings.json を消します。元に戻せません。',
                   '残す' + #13#10 + '話者と設定を残します（既定）。'),
         0, IDNO);
  if R = IDYES then
  begin
    DelTree(Data + '\voices', True, True, True);
    DeleteFile(Data + '\settings.json');
  end;

  { 空になっていれば根も畳む。ただし **junction には撃たない**。
    旧注釈は「中身が残っていれば RemoveDir は失敗して何もしない」と書いていたが、それは **本物の
    ディレクトリの話**であって junction には当たらない。実射（2026-09-05・非昇格・使い捨ての .iss を
    ISCC で通し、InitializeSetup で撃って Result := False で中止＝1 檔も導入せず）の逐語＝
      before RemoveDir: DirExists(link)=1
      RemoveDir returned: 1
      after  RemoveDir: DirExists(link)=0 / DirExists(real)=1 / FileExists(real\settings.json)=1
    ＝**中身 3 檔を抱えた junction に対して RemoveDir は成功し、路だけを消して実体を残した。**
    docs/install.md §5-3 が勧める mklink /J を張った機体では、これは「残す」を選んでも起き、
    次に入れ直すと空のデータ樹が C: に出来て旧樹が孤児になる。だから畳む前に reparse point を見る。
    ※ 非昇格で /J が張れることも同じ日に実測した（New-Item -ItemType Junction・elevated=False）。}
  if IsReparsePoint(Data) then
  begin
    Log('the data root is a reparse point (junction); leaving it in place: ' + Data);
    Exit;
  end;
  RemoveDir(Data);
end;
