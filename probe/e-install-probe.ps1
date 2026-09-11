# probe/e-install-probe.ps1 -- unattended installer probe for convoy E.
#
# ASCII only. CRLF. Windows PowerShell 5.1 and PowerShell 7+.
# No ternary operator, no null-coalescing, no '&&' inside this file.
# Japanese literals are built from code points with New-JpText (probe/common.ps1) -- see $T below.
#
# Design: docs/design/ben-e-installer.md section 7 (the nine stages), section 10-3 (I-1..I-9).
# The file under test: installer/irodori-tts-ywk.iss, compiled by ISCC into a setup.exe.
#
# The stages it drives (design 7-2). 4 is opt in (-IncludeAcl); 9 is NOT here (a new local user needs
# an elevation, and decisions 90 forbids this seat from elevating):
#
#   1  silent install   /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=
#      -> exit 0, the three log lines (User privileges: None / Administrative install mode: No /
#         Install mode root key: HKEY_CURRENT_USER), the HKCU uninstall key, InstallLocation and the
#         DisplayName and the one new start menu .lnk all carrying THIS flavour's name (decisions 109),
#         and every file of {app} matched against the source trees by sha256          (E-7, I-3)
#   2  UI install: the three wizard pages driven with SendMessage(BM_CLICK)
#      -> the same tree as stage 1, and the page count is 3                            (E-5, I-4)
#   3  the launcher's first run wizard shows the notices read from
#      {app}\licenses\first-run-notices.md                                             (decisions 46, I-5)
#   4  (-IncludeAcl) {app} made read only with icacls, then stage 3 again              (E-3)
#   5  an overwrite install while the launcher is running: what AppMutex actually does,
#      silent first and then through the UI, both recorded verbatim                    (I-6)
#   6  a stale {app}\ledger\runtime-rocm-gfx1151.json (plus five planted marker files, two of them
#      the docs v2.0 F-2 removed, and the two past-generation start menu .lnk names) is wiped
#      by [InstallDelete] when the CUDA setup is installed over the top                (I-6)
#   7  silent uninstall -> {app} gone, key gone, .lnk gone, and THIS flavour's data tree gone
#      whole. Decisions 132/133 (v2.0 stage F-2) dropped both [Code] questions, so the log must
#      NOT carry 'Defaulting to No for suppressed message box (Yes/No):' any more     (E-4, I-7)
#   8  UI uninstall, three runs, none of which may raise a question at all:
#      (a) alone -> this flavour's tree is gone whole, a file planted OUTSIDE it survives,
#      (b) with the legacy shared tree %LOCALAPPDATA%\irodori-tts-ywk present and the other
#          flavour absent -> the legacy tree goes too (decisions 133 (5)),
#      (c) with the other flavour installed -> the other tree AND the legacy tree survive (E-4, I-7)
#
# THE OPERATOR'S DATA TREES ARE NEVER THE THING UNDER TEST.
# Decisions 133 (v2.0 stage E-3) split them per flavour, so there are THREE names to protect:
# %LOCALAPPDATA%\irodori-tts-ywk-cuda, ...-radeon and the legacy shared ...\irodori-tts-ywk that
# releases up to v1.1.0 used for both. With -DataDirBackup (the default) the probe renames each of
# the three that exists to <name>.e2-backup inside the same parent BEFORE anything runs, puts a small
# stand-in tree of marker files in place of the flavour under test, and renames the real ones back in
# the finally block. Every stage that can reach the data tree (3, 4, 5, 6, 7, 8) is
# refused when that rename did not happen, and every stage that INSTALLS (1 and 2 included) demands
# the backup as well, because the teardown uninstalls whatever a stage installed and an uninstall is
# the operation that reaches the data tree. The stand-in is a SHAPE (runtime\ models\ cache\ logs\
# miopen\ voices\ settings.json), not a byte copy: the real tree is 3.4 GB and copying it proves
# nothing the shape does not.
#
# The installs go to the real per-user place ({autopf}\irodori-tts-ywk-cuda and
# {autopf}\irodori-tts-ywk-radeon = %LOCALAPPDATA%\Programs\...). No /DIR is passed, so the default of
# the .iss is what gets exercised. The cuda directory was plain 'irodori-tts-ywk' until decisions 109
# renamed it to match the candidates the host app (yomiwakechan2, EngineLaunchDefaults) probes; the
# radeon one never changed. Both flavours may sit on one machine at once (different AppId, different
# directory) and since decisions 133 they share NOTHING: each keeps its own data tree
# (%LOCALAPPDATA%\irodori-tts-ywk-cuda / ...-radeon), its own single instance mutex and its own
# settings, and uninstalling one must not cost the other a single byte.
# The finally block uninstalls whatever is still installed and proves the key, {app} and the .lnk are gone.
#
# It never elevates, never binds 8088 / 7861 / 18088, and the launcher it starts is pinned to a private
# port (default 18097) with autoStartServer=false, so no server is ever started.
#
# Usage:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File probe/e-install-probe.ps1
#   pwsh ... -File probe/e-install-probe.ps1 -Flavor cuda -Steps 1,7
#   pwsh ... -File probe/e-install-probe.ps1 -SetupRadeon <path> -SetupCuda <path>
#   pwsh ... -File probe/e-install-probe.ps1 -IncludeAcl          (adds stage 4)
#   pwsh ... -File probe/e-install-probe.ps1 -AllowSourceDrift    (setup older than build/out)
#
# Exit code 0 = every step passed. Non-zero = the number of failed steps (d-launch-probe.ps1 shape).

[CmdletBinding()]
param(
    # The two setup.exe under test. Default = build/out/installer, then the E(1) scratch pad.
    [string]$SetupCuda = '',
    [string]$SetupRadeon = '',
    # The flavour used by the single flavour stages (1, 2, 3, 4, 5, 7, 8). Stage 6 is CUDA by design.
    [ValidateSet('cuda', 'radeon')][string]$Flavor = 'radeon',
    # A comma separated list, NOT an int[]: PowerShell 5.1 turns "1,3,7" passed through -File into
    # the single integer 137 (thousands separators), which silently runs nothing. Measured 2026-09-05.
    [string]$Steps = '1,2,3,5,6,7,8',
    [switch]$IncludeAcl,
    # OFF is only legal when no data tree exists at all; the stages that touch it refuse otherwise.
    [bool]$DataDirBackup = $true,
    [int]$Port = 18097,
    # Where the expected bytes come from (the same trees the .iss was compiled against).
    [string]$SrcApp = '',
    [string]$SrcExe = '',
    [string]$Repo = '',
    [string]$OutDir = '',
    # Report sha256 mismatches instead of counting them: for a setup.exe compiled before the current
    # publish of build/out. A missing or extra FILE is always a failure, drift or not.
    [switch]$AllowSourceDrift,
    [switch]$KeepWorkDataDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

# ------------------------------------------------------------------ constants

# decisions 90 / design 1-4: the two AppId are permanent (they name the uninstall key).
$script:AppIdCuda   = '{F228543A-DCF9-45A3-8826-7485C81E1757}'
$script:AppIdRadeon = '{ECA98712-1574-4D2A-A1FE-0FF5347BF185}'
$script:UninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'

# M-4: Inno's TNewButton has no InvokePattern and its AutomationId is a per-run HWND, so the only
# road that works is SendMessage(hwnd, BM_CLICK). See design 7-1.
$script:BM_CLICK = 0x00F5
$script:WM_CLOSE = 0x0010

if (-not ('Ywk.InnoProbe' -as [type])) {
    Add-Type -Namespace Ywk -Name InnoProbe -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Auto)]
public static extern System.IntPtr SendMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
[DllImport("user32.dll", CharSet = CharSet.Auto)]
public static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
'@
}

# Japanese literals from code points (this file stays ASCII). The wizard strings are the verbatim
# values of Inno Setup 6.7.3's Languages\Japanese.isl; the rest are the verbatim values of the .iss.
$T = [ordered]@{
    # Japanese.isl: WizardSelectDir / WizardReady / FinishedHeadingLabel
    PageDir      = New-JpText 0x30A4,0x30F3,0x30B9,0x30C8,0x30FC,0x30EB,0x5148,0x306E,0x6307,0x5B9A
    PageReady    = New-JpText 0x30A4,0x30F3,0x30B9,0x30C8,0x30FC,0x30EB,0x6E96,0x5099,0x5B8C,0x4E86
    PageFinished = New-JpText 0x30BB,0x30C3,0x30C8,0x30A2,0x30C3,0x30D7,0x30A6,0x30A3,0x30B6,0x30FC,0x30C9,0x306E,0x5B8C,0x4E86
    # Japanese.isl: ButtonNext / ButtonInstall / ButtonFinish / ButtonCancel / ButtonOK is 'OK'
    BtnNext      = New-JpText 0x6B21,0x3078
    BtnInstall   = New-JpText 0x30A4,0x30F3,0x30B9,0x30C8,0x30FC,0x30EB
    BtnFinish    = New-JpText 0x5B8C,0x4E86
    BtnCancel    = New-JpText 0x30AD,0x30E3,0x30F3,0x30BB,0x30EB
    # Japanese.isl: SetupAppRunningError -- 'Setup has detected <app> running' (its verb, U+691C U+51FA)
    Detected     = New-JpText 0x691C,0x51FA,0x3057,0x307E,0x3057,0x305F
    # .iss [Code] CurUninstallStepChanged, question 1 marker: 'runtime and models' (U+5B9F..U+30EB)
    AskRuntime   = New-JpText 0x5B9F,0x884C,0x7CFB,0x3068,0x30E2,0x30C7,0x30EB
    # .iss [Code] question 2 marker: 'voices and settings' (U+8A71..U+5B9A)
    AskVoices    = New-JpText 0x8A71,0x8005,0x3068,0x8A2D,0x5B9A
    # ^ both markers are kept ONLY so stage 7 and 8 can prove the two questions are GONE (decisions
    #   132/133 removed them from CurUninstallStepChanged). Nothing answers them any more.
    # .iss [Messages] ja.ReadyLabel2a/2b: the 'about' of 'about 96 MB' (U+7D04). The full phrase the
    # ready page must carry is built from this plus ASCII: '<U+7D04> 96 MB'.
    # Stage G (2026-09-11) re-based both halves of this sentence: the {app} figure moved 106 MiB ->
    # 96 MB (v2.0.0 ships a 61,779,808 B exe and a 34,088,381 B tree, so [Files] + the unins000 pair
    # comes to 100,185,593 B / 100,118,243 B = 95.5 MiB), and the units on this page are now the
    # decimal ones the charter 6-1 demands of a user-read screen ('GB', not 'GiB').
    ReadySize    = New-JpText 0x7D04
    # launcher FirstRunViewModel.Title(Notices) marker: 'the third party notices'
    NoticeTitle  = New-JpText 0x7B2C,0x4E09,0x8005,0x7269,0x306E,0x901A,0x77E5
    # .iss [Setup] AppName (decisions 109, RENAMED by v2.0 stage F-1), split into the stem and the
    # flavour label that follows it:
    #   'irodori-TTS for ' + <7 kana/kanji> + ' ' + U+FF0D + ' ' + 'RTX' | 'Radeon'
    #                      + U+FF08 + 'CUDA' | 'ROCm' + U+FF09
    # The separator is a HALFWIDTH SPACE, U+FF0D (fullwidth hyphen-minus) and a halfwidth space --
    # the verbatim of ReleaseFlavors.Separator (ViewModels/ReleaseFlavor.cs). The old form
    # '...(CUDA <edition>)' now lives only in the .iss OldAppName, whose .lnk [InstallDelete] removes.
    # Get-ExpectedAppName below joins them; the DisplayName in HKCU and the start menu .lnk name are
    # both matched against the result.
    AppStem      = 'irodori-TTS for ' + (New-JpText 0x8AAD,0x307F,0x5206,0x3051,0x3061,0x3083,0x3093)
    Separator    = ' ' + (New-JpText 0xFF0D) + ' '
    ParenOpen    = New-JpText 0xFF08
    ParenClose   = New-JpText 0xFF09
    Edition      = New-JpText 0x7248
}

# ------------------------------------------------------------------ defaults

$repoRoot = Get-YwkProbeRepoRoot
if ([string]::IsNullOrEmpty($Repo))   { $Repo = $repoRoot }
if ([string]::IsNullOrEmpty($SrcApp)) { $SrcApp = Join-Path $Repo 'build\out\app' }
if ([string]::IsNullOrEmpty($SrcExe)) { $SrcExe = Join-Path $Repo 'build\out\launcher\win-x64' }
if ([string]::IsNullOrEmpty($OutDir)) { $OutDir = Join-Path $Repo 'build\out\probe-log' }
$null = New-Item -ItemType Directory -Path $OutDir -Force

function Resolve-Setup {
    param([string]$Given, [string]$FlavorName)
    if (-not [string]::IsNullOrEmpty($Given)) { return $Given }
    $name = 'irodori-tts-ywk-setup-v2.0.3-' + $FlavorName + '.exe'
    $built = Join-Path (Join-Path $Repo 'build\out\installer') $name
    if (Test-Path -LiteralPath $built) { return $built }
    return $built
}
$SetupCuda   = Resolve-Setup -Given $SetupCuda   -FlavorName 'cuda'
$SetupRadeon = Resolve-Setup -Given $SetupRadeon -FlavorName 'radeon'

# decisions 133 (v2.0 stage E-3): the data tree is per flavour, and the shared name that releases
# up to v1.1.0 used is now only a migration source. All three are the operator's, all three are
# parked before anything destructive runs, and $script:DataDirRoot is the one under test.
$script:DataDirNameCuda   = 'irodori-tts-ywk-cuda'
$script:DataDirNameRadeon = 'irodori-tts-ywk-radeon'
$script:DataDirNameLegacy = 'irodori-tts-ywk'
$script:DataDirNames  = @($script:DataDirNameCuda, $script:DataDirNameRadeon, $script:DataDirNameLegacy)
$script:LegacyDataDir = Join-Path $env:LOCALAPPDATA $script:DataDirNameLegacy
$script:ProgramsRoot  = Join-Path $env:LOCALAPPDATA 'Programs'

function Get-DataDirName {
    <#
      .SYNOPSIS
        AppPaths.DataDirNameCuda / DataDirNameRadeon, verbatim (= the .iss MyDataDirName).
    #>
    param([string]$FlavorName)
    if ($FlavorName -eq 'radeon') { return $script:DataDirNameRadeon }
    return $script:DataDirNameCuda
}

function Get-DataDirRoot {
    param([string]$FlavorName)
    return (Join-Path $env:LOCALAPPDATA (Get-DataDirName -FlavorName $FlavorName))
}

# The tree of the flavour the single flavour stages drive. Stages that touch both name theirs.
$script:DataDirRoot = Get-DataDirRoot -FlavorName $Flavor
$script:StartMenuDir  = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$script:Stamp         = (Get-Date).ToString('yyyyMMdd-HHmmss')
$script:LogDir        = Join-Path $OutDir ('e-install-' + $script:Stamp)
$null = New-Item -ItemType Directory -Path $script:LogDir -Force

# The stage list, parsed once (see the -Steps note in param).
$StepList = @()
foreach ($s in ($Steps -split ',')) {
    $s = $s.Trim()
    if (-not [string]::IsNullOrEmpty($s)) { $StepList += [int]$s }
}

$script:Failures = @()
$script:Results    = @()
$script:BackupTaken = $false
$script:DataBefore  = $null

function Add-Step {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    $mark = 'FAIL'
    if ($Ok) { $mark = 'PASS' }
    Write-Host ('[' + $mark + '] ' + $Name + ' :: ' + $Detail)
    if (-not $Ok) { $script:Failures += ($Name + ': ' + $Detail) }
    $script:Results += [pscustomobject]@{ step = $Name; ok = $Ok; detail = $Detail }
}

function Get-AppId {
    param([string]$FlavorName)
    if ($FlavorName -eq 'radeon') { return $script:AppIdRadeon }
    return $script:AppIdCuda
}

function Get-SetupPath {
    param([string]$FlavorName)
    if ($FlavorName -eq 'radeon') { return $SetupRadeon }
    return $SetupCuda
}

function Get-ExpectedAppName {
    <#
      .SYNOPSIS
        The .iss [Setup] AppName of one flavour, verbatim (decisions 109, renamed by v2.0 stage F-1).
        cuda   -> irodori-TTS for <7 chars> - RTX(CUDA)          [dash and parentheses are full width]
        radeon -> irodori-TTS for <7 chars> - Radeon(ROCm)
        The same string is the UninstallDisplayName stem and the start menu shortcut name, and the
        verbatim of ReleaseFlavors.AppTitle (the launcher window title) -- one string, four places.
    #>
    param([string]$FlavorName)
    $product = 'RTX'
    $tech = 'CUDA'
    if ($FlavorName -eq 'radeon') {
        $product = 'Radeon'
        $tech = 'ROCm'
    }
    return ($T.AppStem + $T.Separator + $product + $T.ParenOpen + $tech + $T.ParenClose)
}

function Get-OldAppName {
    <#
      .SYNOPSIS
        The .iss OldAppName of one flavour = the v1.1.0 name, whose start menu .lnk [InstallDelete]
        removes on an overwrite install. Never the name of anything this probe installs.
        Called by Invoke-Stage6, which plants the .lnk and proves it is gone afterwards.
    #>
    param([string]$FlavorName)
    $tag = 'CUDA '
    if ($FlavorName -eq 'radeon') { $tag = 'ROCm ' }
    return ($T.AppStem + $T.ParenOpen + $tag + $T.Edition + $T.ParenClose)
}

function Get-OlderAppName {
    <#
      .SYNOPSIS
        The .iss OlderAppName of one flavour = the v1.0 generation name (是正 2026-09-11, medium 10).
        cuda   -> the bare stem, no edition at all
        radeon -> the stem plus (Radeon <edition>)
        v1.0.x shipped under the SAME AppId as v1.1.0, so a machine can arrive at v2.0.0 straight
        from v1.0.x and carry that .lnk; [InstallDelete] now names both generations.
    #>
    param([string]$FlavorName)
    if ($FlavorName -eq 'radeon') {
        return ($T.AppStem + $T.ParenOpen + 'Radeon ' + $T.Edition + $T.ParenClose)
    }
    return $T.AppStem
}

function Get-ExpectedAppDir {
    <#
      .SYNOPSIS
        The .iss DefaultDirName of one flavour under {autopf} (= %LOCALAPPDATA%\Programs).
        decisions 109 renamed the cuda one from 'irodori-tts-ywk' to 'irodori-tts-ywk-cuda' so that it
        matches the candidates the host app (yomiwakechan2, EngineLaunchDefaults) already probes.
    #>
    param([string]$FlavorName)
    if ($FlavorName -eq 'radeon') { return (Join-Path $script:ProgramsRoot 'irodori-tts-ywk-radeon') }
    return (Join-Path $script:ProgramsRoot 'irodori-tts-ywk-cuda')
}

# ------------------------------------------------------------------ Inno helpers (design 7-1)
# The four functions the design asks for live HERE, not in probe/common.ps1: common.ps1 is the convoy
# D launcher driver and is read only for this seat (design 10-1).

function Get-ProcessFamilyId {
    <#
      .SYNOPSIS
        Every process id reachable from a root pid by ParentProcessId, plus any *.tmp process.
      .DESCRIPTION
        M-1 (design 0-1): the pid returned by Start-Process for an Inno setup.exe owns NO window.
        The window belongs to a child that runs from %TEMP%\is-*.tmp\<name>.tmp. The uninstaller does
        the same with _iu*.tmp. The *.tmp sweep is the safety net for the moment the parent has already
        exited and the parent chain is broken.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][int]$RootId)
    $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Select-Object ProcessId, ParentProcessId, Name)
    $ids = @([int]$RootId)
    $grew = $true
    while ($grew) {
        $grew = $false
        foreach ($p in $all) {
            $pid2 = [int]$p.ProcessId
            $par  = [int]$p.ParentProcessId
            if (($ids -contains $par) -and (-not ($ids -contains $pid2))) {
                $ids += $pid2
                $grew = $true
            }
        }
    }
    foreach ($p in $all) {
        if ($p.Name -like '*.tmp') {
            $pid3 = [int]$p.ProcessId
            if (-not ($ids -contains $pid3)) { $ids += $pid3 }
        }
    }
    return $ids
}

function Get-SetupChildProcess {
    <#
      .SYNOPSIS
        The *.tmp child of an Inno setup/uninstall process (M-1). Empty array while none exists yet.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][int]$RootId, [int]$TimeoutSeconds = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        $hits = @()
        foreach ($id in @(Get-ProcessFamilyId -RootId $RootId)) {
            if ($id -eq $RootId) { continue }
            $p = Get-Process -Id $id -ErrorAction SilentlyContinue
            if ($null -ne $p) { $hits += $p }
        }
        if ($hits.Count -gt 0) { return $hits }
        if ((Get-Date) -ge $deadline) { return @() }
        Start-Sleep -Milliseconds 200
    }
}

function Get-FamilyWindow {
    <#
      .SYNOPSIS
        Every top level window owned by the process family of a root pid.
      .DESCRIPTION
        Uses common.ps1's Get-ProcessWindows, which is the two stage road decisions 86 asks for
        (UI Automation first, Win32 EnumWindows + AutomationElement.FromHandle as the fallback).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][int]$RootId)
    $out = @()
    foreach ($id in @(Get-ProcessFamilyId -RootId $RootId)) {
        $fake = [pscustomobject]@{ Id = $id }
        $windows = @()
        try { $windows = @(Get-ProcessWindows -Proc $fake) } catch { $windows = @() }
        foreach ($w in $windows) { $out += $w }
    }
    return $out
}

function Get-InnoWizard {
    <#
      .SYNOPSIS
        The Inno wizard window (ClassName TWizardForm) of a setup/uninstall process family (M-2).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$RootId,
        [string]$ClassName = 'TWizardForm',
        [int]$TimeoutSeconds = 60
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        foreach ($w in @(Get-FamilyWindow -RootId $RootId)) {
            $cn = ''
            try { $cn = [string]$w.Current.ClassName } catch { $cn = '' }
            if ($cn -eq $ClassName) { return $w }
        }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 300
    }
}

function Get-InnoTexts {
    <#
      .SYNOPSIS
        Every readable string of an Inno window (TNewStaticText / TNewButton / TNewEdit / labels).
        The page is identified by these, never by an AutomationId: Inno's ids are per-run HWNDs (M-3).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Window)
    $texts = @()
    try { $texts += [string]$Window.Current.Name } catch { }
    $all = @()
    try {
        $all = @($Window.FindAll($script:TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
    } catch {
        $all = @()
    }
    foreach ($el in $all) {
        $name = ''
        try { $name = [string]$el.Current.Name } catch { $name = '' }
        if (-not [string]::IsNullOrWhiteSpace($name)) { $texts += $name }
        $val = Get-ElValue -Element $el
        if (-not [string]::IsNullOrWhiteSpace($val)) { $texts += [string]$val }
    }
    return $texts
}

function Invoke-InnoButton {
    <#
      .SYNOPSIS
        Press an Inno button by its caption with SendMessage(hwnd, BM_CLICK) -- M-3 / M-4.
      .DESCRIPTION
        Find-ById (AutomationIdProperty, common.ps1:364) and Invoke-ButtonById (InvokePattern,
        common.ps1:427) do NOT work on Inno: TNewButton reports no supported pattern and its
        AutomationId is the HWND, which changes every run. The button is matched on Name and the click
        is a window message. -Async posts instead of sending, for a button that opens a modal (a blocking
        SendMessage would deadlock the probe against the modal it just opened).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Window,
        [Parameter(Mandatory = $true)][string]$NamePattern,
        [int]$TimeoutSeconds = 20,
        [switch]$Async
    )
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $script:AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        $buttons = @()
        try { $buttons = @($Window.FindAll($script:TS::Descendants, $cond)) } catch { $buttons = @() }
        foreach ($b in $buttons) {
            $name = ''
            $enabled = $false
            $handle = 0
            try {
                $name = [string]$b.Current.Name
                $enabled = [bool]$b.Current.IsEnabled
                $handle = [int]$b.Current.NativeWindowHandle
            } catch {
                continue
            }
            if (($name -match $NamePattern) -and $enabled -and ($handle -ne 0)) {
                $top = 0
                try { $top = [int]$Window.Current.NativeWindowHandle } catch { $top = 0 }
                if ($top -ne 0) { $null = [Ywk.NativeProbe]::SetForegroundWindow([System.IntPtr]$top) }
                Write-Host ('[inno] click ' + $name.Replace("`r", ' ').Replace("`n", ' '))
                if ($Async) {
                    $null = [Ywk.InnoProbe]::PostMessage([System.IntPtr]$handle, $script:BM_CLICK,
                        [System.IntPtr]::Zero, [System.IntPtr]::Zero)
                } else {
                    $null = [Ywk.InnoProbe]::SendMessage([System.IntPtr]$handle, $script:BM_CLICK,
                        [System.IntPtr]::Zero, [System.IntPtr]::Zero)
                }
                return $name
            }
        }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 300
    }
}

function Wait-InnoPage {
    <#
      .SYNOPSIS
        Wait until the texts of a window match a pattern; returns ok / elapsed / texts.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Window,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [int]$TimeoutSeconds = 60
    )
    $start = Get-Date
    $deadline = $start.AddSeconds($TimeoutSeconds)
    $last = @()
    while ($true) {
        $last = @(Get-InnoTexts -Window $Window)
        $joined = ($last -join ' | ')
        if ($joined -match $Pattern) {
            return [pscustomobject]@{ ok = $true; elapsed = ((Get-Date) - $start).TotalSeconds; texts = $last }
        }
        if ((Get-Date) -ge $deadline) {
            return [pscustomobject]@{ ok = $false; elapsed = ((Get-Date) - $start).TotalSeconds; texts = $last }
        }
        Start-Sleep -Milliseconds 400
    }
}

function Wait-InnoDialog {
    <#
      .SYNOPSIS
        Wait for any window of the process family whose texts match a pattern (a message box, or the
        TaskDialog the uninstaller raises from [Code]). Returns the element or $null.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$RootId,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [int]$TimeoutSeconds = 60
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        foreach ($w in @(Get-FamilyWindow -RootId $RootId)) {
            $texts = @(Get-InnoTexts -Window $w)
            if (($texts -join ' | ') -match $Pattern) { return $w }
        }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 300
    }
}

# ------------------------------------------------------------------ file / registry helpers

function Get-TreeFileMap {
    <#
      .SYNOPSIS
        relative path (lower case) -> @{ full; length; sha256 } for every file under a root.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$Prefix = '',
        [string[]]$ExcludeName = @(),
        [switch]$SkipPycache
    )
    $map = @{}
    if (-not (Test-Path -LiteralPath $Root)) { return $map }
    $full = (Resolve-Path -LiteralPath $Root).Path
    foreach ($f in @(Get-ChildItem -LiteralPath $full -Recurse -File -Force)) {
        $rel = $f.FullName.Substring($full.Length).TrimStart('\')
        if ($SkipPycache) {
            if ($rel -like '*__pycache__*') { continue }
            if ($rel -like '*.pyc') { continue }
        }
        if ($ExcludeName -contains $f.Name) { continue }
        $key = $rel
        if (-not [string]::IsNullOrEmpty($Prefix)) { $key = (Join-Path $Prefix $rel) }
        $map[$key.ToLowerInvariant()] = [pscustomobject]@{
            full   = $f.FullName
            length = $f.Length
            sha256 = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
        }
    }
    return $map
}

$script:ExpectedSourceMissing = @()

function Add-ExpectedFile {
    param([hashtable]$Map, [string]$SourcePath, [string]$RelPath)
    if (-not (Test-Path -LiteralPath $SourcePath)) {
        # A named source that is not there used to make the expectation SMALLER and say nothing, so
        # {app} could lose the same file and still match. Record it; Compare-AppTree fails on it.
        if ($script:ExpectedSourceMissing -notcontains $SourcePath) {
            $script:ExpectedSourceMissing += $SourcePath
        }
        return
    }
    $item = Get-Item -LiteralPath $SourcePath
    $Map[$RelPath.ToLowerInvariant()] = [pscustomobject]@{
        full   = $item.FullName
        length = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
    }
}

function Get-ExpectedAppMap {
    <#
      .SYNOPSIS
        What {app} must hold for a flavour, taken from the trees the .iss was compiled against.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$FlavorName)
    $map = @{}
    Add-ExpectedFile -Map $map -SourcePath (Join-Path $SrcExe 'IrodoriTtsYwk.Launcher.exe') `
        -RelPath 'IrodoriTtsYwk.Launcher.exe'
    foreach ($branch in @('server', 'licenses', 'voices')) {
        $sub = Get-TreeFileMap -Root (Join-Path $SrcApp $branch) -Prefix $branch -SkipPycache
        foreach ($k in $sub.Keys) { $map[$k] = $sub[$k] }
    }
    $ledger = @('README.md', 'models.json', 'python-embed.json', 'vc_redist.json', 'runtime-cpu.json')
    if ($FlavorName -eq 'radeon') {
        $ledger += 'runtime-rocm-gfx1151.json'
    } else {
        $ledger += 'runtime-cu130.json'
        $ledger += 'runtime-cu126.json'
    }
    foreach ($name in $ledger) {
        Add-ExpectedFile -Map $map -SourcePath (Join-Path (Join-Path $SrcApp 'ledger') $name) `
            -RelPath ('ledger\' + $name)
    }
    Add-ExpectedFile -Map $map -SourcePath (Join-Path $Repo 'README.md')      -RelPath 'docs\README.md'
    # v2.0 stage F-2: docs\install.md left the distributable (it is the maker's ledger again) and
    # docs\guide.md took its place. Stage 1 also proves install.md is NOT there any more, because
    # [Files] going quiet is not enough on a machine that already had it ([InstallDelete] is).
    Add-ExpectedFile -Map $map -SourcePath (Join-Path $Repo 'docs\guide.md')  -RelPath 'docs\guide.md'
    # docs\radeon.md left the distributable in the same stage and for the same reason as install.md
    # (是正 2026-09-11, medium 16): 548 lines of research ledger on a user machine. {app}\docs is
    # now README.md + guide.md on BOTH flavours, so there is no per-flavour docs branch any more.
    return $map
}

function Compare-AppTree {
    <#
      .SYNOPSIS
        {app} against the expected map. Returns missing / extra / mismatch / matched / bytes.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$AppDir, [Parameter(Mandatory = $true)][string]$FlavorName)
    $script:ExpectedSourceMissing = @()
    $expected = Get-ExpectedAppMap -FlavorName $FlavorName
    $actual = Get-TreeFileMap -Root $AppDir -ExcludeName @('unins000.exe', 'unins000.dat')
    $missing = @()
    $extra = @()
    $mismatch = @()
    $matched = 0
    foreach ($k in $expected.Keys) {
        if (-not $actual.ContainsKey($k)) { $missing += $k; continue }
        if ($actual[$k].sha256 -ne $expected[$k].sha256) {
            $mismatch += ($k + ' app=' + $actual[$k].sha256.Substring(0, 16) + '/' + $actual[$k].length +
                ' src=' + $expected[$k].sha256.Substring(0, 16) + '/' + $expected[$k].length)
        } else {
            $matched++
        }
    }
    foreach ($k in $actual.Keys) {
        if (-not $expected.ContainsKey($k)) { $extra += $k }
    }
    $allFiles = @()
    if (Test-Path -LiteralPath $AppDir) {
        $allFiles = @(Get-ChildItem -LiteralPath $AppDir -Recurse -File -Force)
    }
    $bytes = 0
    foreach ($f in $allFiles) { $bytes += $f.Length }
    # A source file the .iss names but that is not on disk shrinks the expectation instead of
    # failing it, so it is carried out as a MISSING entry of its own kind: the comparison must not
    # come back green because it stopped expecting something.
    foreach ($src in $script:ExpectedSourceMissing) {
        $missing += ('(source not on disk, so nothing was expected of it) ' + $src)
    }
    return [pscustomobject]@{
        expectedCount = $expected.Count
        actualCount   = $actual.Count
        totalFiles    = $allFiles.Count
        totalBytes    = $bytes
        matched       = $matched
        missing       = $missing
        extra         = $extra
        mismatch      = $mismatch
        sourceMissing = @($script:ExpectedSourceMissing)
    }
}

function Get-UninstallKeyValue {
    param([Parameter(Mandatory = $true)][string]$FlavorName)
    $path = Join-Path $script:UninstallRoot ((Get-AppId -FlavorName $FlavorName) + '_is1')
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return (Get-ItemProperty -LiteralPath $path)
}

function Get-StartMenuLink {
    if (-not (Test-Path -LiteralPath $script:StartMenuDir)) { return @() }
    return @(Get-ChildItem -LiteralPath $script:StartMenuDir -Filter '*.lnk' -File -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName })
}

function Get-DataTreeSnapshot {
    param([string]$Root = '')
    if ([string]::IsNullOrEmpty($Root)) { $Root = $script:DataDirRoot }
    if (-not (Test-Path -LiteralPath $Root)) {
        return [pscustomobject]@{ exists = $false; files = 0; bytes = 0; names = @() }
    }
    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue)
    $bytes = 0
    $names = @()
    $base = (Resolve-Path -LiteralPath $Root).Path
    foreach ($f in $files) {
        $bytes += $f.Length
        $names += ($f.FullName.Substring($base.Length).TrimStart('\') + '|' + $f.Length)
    }
    return [pscustomobject]@{ exists = $true; files = $files.Count; bytes = $bytes; names = ($names | Sort-Object) }
}

# ------------------------------------------------------------------ the data tree (backup / stand-in)

function Backup-DataTree {
    <#
      .SYNOPSIS
        Rename all three data trees (cuda, radeon and the legacy shared one) out of the way.
        Nothing destructive runs until this returns true. Refuses to touch anything if any of the
        three backup names is already taken -- a half done park is worse than no park at all.
    #>
    [CmdletBinding()]
    param()
    foreach ($name in $script:DataDirNames) {
        $backup = Join-Path $env:LOCALAPPDATA ($name + '.e2-backup')
        if (Test-Path -LiteralPath $backup) {
            throw ('The backup name already exists, refusing to run: ' + $backup)
        }
    }
    $script:DataBefore = @{}
    foreach ($name in $script:DataDirNames) {
        $root = Join-Path $env:LOCALAPPDATA $name
        $snap = Get-DataTreeSnapshot -Root $root
        $script:DataBefore[$name] = $snap
        if ($snap.exists) {
            Write-Host ('[data] before ' + $name + ': files=' + $snap.files + ' bytes=' + $snap.bytes)
            Rename-Item -LiteralPath $root -NewName ($name + '.e2-backup') -Force
            Write-Host ('[data] moved aside -> ' + $root + '.e2-backup')
        } else {
            Write-Host ('[data] no ' + $name + ' tree existed; nothing to move aside')
        }
    }
    $script:BackupTaken = $true
    return $true
}

function Restore-DataTree {
    <#
      .SYNOPSIS
        Put all three of the operator's trees back. Runs in the finally block and must never throw.
    #>
    [CmdletBinding()]
    param()
    if (-not $script:BackupTaken) { return }
    foreach ($name in $script:DataDirNames) {
        $root = Join-Path $env:LOCALAPPDATA $name
        $backupPath = $root + '.e2-backup'
        if (Test-Path -LiteralPath $root) {
            if ($KeepWorkDataDir) {
                $kept = Join-Path $env:LOCALAPPDATA ($name + '.e2-work-' + $script:Stamp)
                try {
                    Rename-Item -LiteralPath $root -NewName (Split-Path -Leaf $kept) -Force
                    Write-Host ('[data] stand-in kept at ' + $kept)
                } catch {
                    Write-Host ('[data] could not keep the stand-in: ' + $_.Exception.Message)
                }
            } else {
                for ($i = 0; $i -lt 10; $i++) {
                    try {
                        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction Stop
                        break
                    } catch {
                        Start-Sleep -Milliseconds 400
                    }
                }
            }
        }
        if (Test-Path -LiteralPath $root) {
            # The stand-in refuses to go. Move it: the operator's tree must come back either way.
            $parked = $name + '.e2-stuck-' + $script:Stamp
            try {
                Rename-Item -LiteralPath $root -NewName $parked -Force
                Write-Host ('[data] stand-in parked as ' + $parked)
            } catch {
                Write-Host ('[data] COULD NOT clear the stand-in: ' + $_.Exception.Message)
            }
        }
        if (Test-Path -LiteralPath $backupPath) {
            try {
                Rename-Item -LiteralPath $backupPath -NewName $name -Force
                Write-Host ('[data] restored -> ' + $root)
            } catch {
                Write-Host ''
                Write-Host '*** AN OPERATOR DATA TREE IS STILL PARKED. RESTORE IT BY HAND: ***'
                Write-Host ('    Rename-Item -LiteralPath "' + $backupPath + '" -NewName "' + $name + '"')
                Write-Host ''
                continue
            }
        }
        Test-RestoredTree -Name $name
    }
}

function Test-RestoredTree {
    <#
      .SYNOPSIS
        One tree came back exactly as it was (count, bytes and every name). Never throws.
    #>
    param([string]$Name)
    $root = Join-Path $env:LOCALAPPDATA $Name
    $after = Get-DataTreeSnapshot -Root $root
    if (($null -ne $script:DataBefore) -and $script:DataBefore.ContainsKey($Name)) {
        $before = $script:DataBefore[$Name]
        if (-not $before.exists -and -not $after.exists) { return }
        $same = ($after.files -eq $before.files) -and ($after.bytes -eq $before.bytes)
        # The names are compared too, not just the two totals. Get-DataTreeSnapshot has always built
        # the "<relative path>|<length>" list; nothing read it, so a rename or a swap of two files of
        # equal size passed as "byte for byte". Compare-Object over the two sorted lists is the whole
        # fix, and it is what makes the step's name true.
        $diff = @()
        try {
            $diff = @(Compare-Object -ReferenceObject @($before.names) -DifferenceObject @($after.names))
        } catch {
            $diff = @()
        }
        $same = $same -and ($diff.Count -eq 0)
        $shown = ''
        if ($diff.Count -gt 0) {
            $shown = ' ; first differences: ' + ((@($diff | Select-Object -First 5 |
                ForEach-Object { $_.SideIndicator + ' ' + $_.InputObject })) -join ' ; ')
        }
        Add-Step ('the operator data tree ' + $Name +
            ' came back byte for byte (count, bytes and every name)') $same (
            'before files=' + $before.files + ' bytes=' + $before.bytes +
            ' :: after files=' + $after.files + ' bytes=' + $after.bytes +
            ' :: name/length differences=' + $diff.Count + $shown)
    }
}

function Assert-WorkTree {
    <#
      .SYNOPSIS
        Throw unless the operator's tree is parked. Every stage that can reach the data tree calls this.
    #>
    [CmdletBinding()]
    param()
    if (-not $script:BackupTaken) {
        throw 'This stage touches the %LOCALAPPDATA%\irodori-tts-ywk* trees and -DataDirBackup was off.'
    }
}

function Reset-WorkDataDir {
    <#
      .SYNOPSIS
        A stand-in data tree for ONE flavour: the SHAPE the uninstaller wipes whole (decisions
        132/133 -- installer/irodori-tts-ywk.iss, CurUninstallStepChanged, WipeTree(DataDir())).
        There are no questions any more, so there is no longer a "question 1 half" and a
        "question 2 half": runtime\ models\ cache\ logs\ miopen\ voices\ and settings.json all go
        together, and nothing outside this root is touched.
        -Root builds the legacy shared tree (%LOCALAPPDATA%\irodori-tts-ywk) instead.
    #>
    [CmdletBinding()]
    param([string]$FlavorName = '', [string]$Root = '')
    Assert-WorkTree
    if ([string]::IsNullOrEmpty($Root)) {
        if ([string]::IsNullOrEmpty($FlavorName)) { $FlavorName = $Flavor }
        $Root = Get-DataDirRoot -FlavorName $FlavorName
    }
    if (Test-Path -LiteralPath $Root) {
        Remove-Item -LiteralPath $Root -Recurse -Force
    }
    $marker = 'e2 stand-in marker. The real tree is parked as <name>.e2-backup.'
    $dirs = @('runtime\rocm-gfx1151', 'models\hub', 'cache', 'logs', 'miopen', 'voices\ref', 'voices\latents')
    foreach ($d in $dirs) {
        $full = Join-Path $Root $d
        $null = New-Item -ItemType Directory -Path $full -Force
        [System.IO.File]::WriteAllText((Join-Path $full 'e2-marker.txt'), $marker,
            (New-Object System.Text.UTF8Encoding($false)))
    }
    foreach ($f in @('voices\voices.json', 'voices\voices.ywk.json')) {
        [System.IO.File]::WriteAllText((Join-Path $Root $f), '{"schema":1,"voices":[]}',
            (New-Object System.Text.UTF8Encoding($false)))
    }
    # decisions 90 stop domain: the launcher must never bind 8088 / 7861 / 18088, and must not start a
    # server at all. autoStartServer=false + firstRunCompleted=false = the wizard opens and nothing runs.
    if ($script:ForbiddenPorts -contains $Port) {
        throw ('Port ' + $Port + ' is reserved.')
    }
    $settings = [ordered]@{
        schema            = 1
        variant           = 'rocm-gfx1151'
        port              = $Port
        autoStartServer   = $false
        firstRunCompleted = $false
        warmup            = $false
        showMemoryPanel   = $true
    }
    [System.IO.File]::WriteAllText((Join-Path $Root 'settings.json'),
        ($settings | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
    $snap = Get-DataTreeSnapshot -Root $Root
    Write-Host ('[data] stand-in ready at ' + $Root + ': files=' + $snap.files + ' bytes=' + $snap.bytes)
    return $snap
}

# ------------------------------------------------------------------ install / uninstall

function Install-Silent {
    <#
      .SYNOPSIS
        /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=. No /DIR: the .iss default is the thing tested.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Setup, [Parameter(Mandatory = $true)][string]$LogName)
    if (-not (Test-Path -LiteralPath $Setup)) { throw ('setup.exe is missing: ' + $Setup) }
    $log = Join-Path $script:LogDir $LogName
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Setup -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG=' + $log))
    $sw.Stop()
    $code = -1
    if ($null -ne $p.ExitCode) { $code = [int]$p.ExitCode }
    $lines = @()
    if (Test-Path -LiteralPath $log) { $lines = @(Get-Content -LiteralPath $log -Encoding UTF8) }
    Write-Host ('[install] ' + (Split-Path -Leaf $Setup) + ' exit=' + $code +
        ' sec=' + [math]::Round($sw.Elapsed.TotalSeconds, 2) + ' log=' + $lines.Count + ' lines')
    return [pscustomobject]@{
        exit = $code; seconds = $sw.Elapsed.TotalSeconds; log = $log; lines = $lines
    }
}

function Read-SharedTextLines {
    <#
      .SYNOPSIS
        Read a text file that another process is still writing to. Empty array when it cannot be read.
      .DESCRIPTION
        Get-Content takes a share mode that an Inno uninstaller's open log file can refuse. This is
        used to POLL a log while its writer still owns it, so it has to tolerate every failure.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $fs = $null
    try {
        $share = ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
        $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
        $sr = New-Object System.IO.StreamReader($fs)
        $text = $sr.ReadToEnd()
        $sr.Close()
        return @($text -split "`r?`n")
    } catch {
        return @()
    } finally {
        if ($null -ne $fs) { try { $fs.Dispose() } catch { } }
    }
}

function Wait-UninstallFinished {
    <#
      .SYNOPSIS
        Block until an Inno uninstall log says 'Log closed.' Returns @{ closed; lines; seconds }.
      .DESCRIPTION
        THE RACE THIS CLOSES (measured 2026-09-05 11:10-11:12, three runs).
        An Inno uninstaller copies itself to %TEMP%\_iu*.tmp and the process that was started EXITS
        FIRST, so $p.HasExited goes true within a second or two. {app} is already gone by then as
        well (usUninstall), so a second wait on {app} also falls straight through. But the two
        questions of installer/irodori-tts-ywk.iss run in usPostUninstall, which is AFTER both --
        so the probe was reading the data tree while the uninstaller had not yet acted on the
        answers. Verbatim: stage 8b printed 'log=167 lines' and failed with 'still there: voices ;
        still there: settings.json', while the log's own last two lines were
          line 168 : 2026-09-05 11:11:36.381   User chose Yes.
          line 169 : 2026-09-05 11:11:36.383   Log closed.
        Three samples: 167 lines -> red, 169 lines -> green. The read length WAS the verdict.
        The mirror image is worse: stage 8a and 8c assert that things are STILL THERE, so reading
        early made them pass no matter what the product did. 'Log closed.' is the last line Inno
        writes, after usPostUninstall and after DeinitializeUninstall, so it is the one event that
        means "everything this uninstaller was going to do to the data tree, it has done".
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [int]$TimeoutSeconds = 90
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lines = @()
    while ($true) {
        $lines = @(Read-SharedTextLines -Path $LogPath)
        $nonEmpty = @($lines | Where-Object { $_.Trim().Length -gt 0 })
        if ($nonEmpty.Count -gt 0) {
            if ($nonEmpty[$nonEmpty.Count - 1].Trim().EndsWith('Log closed.')) {
                $sw.Stop()
                return [pscustomobject]@{ closed = $true; lines = $lines; seconds = $sw.Elapsed.TotalSeconds }
            }
        }
        if ((Get-Date) -ge $deadline) {
            $sw.Stop()
            return [pscustomobject]@{ closed = $false; lines = $lines; seconds = $sw.Elapsed.TotalSeconds }
        }
        Start-Sleep -Milliseconds 200
    }
}

function Uninstall-Silent {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$AppDir, [Parameter(Mandatory = $true)][string]$LogName)
    $unins = Join-Path $AppDir 'unins000.exe'
    if (-not (Test-Path -LiteralPath $unins)) { throw ('unins000.exe is missing: ' + $unins) }
    $log = Join-Path $script:LogDir $LogName
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $unins -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG=' + $log))
    $sw.Stop()
    $code = -1
    if ($null -ne $p.ExitCode) { $code = [int]$p.ExitCode }
    for ($i = 0; $i -lt 60; $i++) {
        if (-not (Test-Path -LiteralPath $AppDir)) { break }
        Start-Sleep -Milliseconds 250
    }
    $lines = @()
    if (Test-Path -LiteralPath $log) { $lines = @(Get-Content -LiteralPath $log -Encoding UTF8) }
    Write-Host ('[uninstall] exit=' + $code + ' sec=' + [math]::Round($sw.Elapsed.TotalSeconds, 2) +
        ' log=' + $lines.Count + ' lines')
    return [pscustomobject]@{
        exit = $code; seconds = $sw.Elapsed.TotalSeconds; log = $log; lines = $lines
    }
}

function Get-InstalledAppDir {
    param([Parameter(Mandatory = $true)][string]$FlavorName)
    $key = Get-UninstallKeyValue -FlavorName $FlavorName
    if ($null -eq $key) { return '' }
    $loc = ''
    try { $loc = [string]$key.InstallLocation } catch { $loc = '' }
    return $loc.TrimEnd('\')
}

function Remove-Installed {
    <#
      .SYNOPSIS
        Uninstall a flavour if its key is there. Used by the teardown and by every stage that needs a
        clean machine to start from.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$FlavorName, [string]$LogName = '')
    $dir = Get-InstalledAppDir -FlavorName $FlavorName
    if ([string]::IsNullOrEmpty($dir)) { return $false }
    if (-not (Test-Path -LiteralPath $dir)) { return $false }
    if ([string]::IsNullOrEmpty($LogName)) {
        $LogName = 'cleanup-' + $FlavorName + '-' + [System.Guid]::NewGuid().ToString('N').Substring(0, 6) + '.log'
    }
    $null = Uninstall-Silent -AppDir $dir -LogName $LogName
    return $true
}

function Get-LauncherProcess {
    return @(Get-Process -Name 'IrodoriTtsYwk.Launcher' -ErrorAction SilentlyContinue)
}

function Stop-AllLaunchers {
    foreach ($p in @(Get-LauncherProcess)) {
        try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { }
    }
    for ($i = 0; $i -lt 40; $i++) {
        if (@(Get-LauncherProcess).Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
}

function Start-InstalledLauncher {
    <#
      .SYNOPSIS
        Start {app}\IrodoriTtsYwk.Launcher.exe as a real per-user install would: NO env override.
        design 1-7 / AppPaths.cs:205 -- one YWK_LAUNCHER_* variable makes the machine call itself a
        developer run, so the three are cleared for the child and restored afterwards.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$AppDir)
    Assert-WorkTree
    $exe = Join-Path $AppDir 'IrodoriTtsYwk.Launcher.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw ('launcher exe is missing: ' + $exe) }
    $savedApp     = $env:YWK_LAUNCHER_APP_DIR
    $savedRuntime = $env:YWK_LAUNCHER_RUNTIME_DIR
    $savedData    = $env:YWK_LAUNCHER_DATA_DIR
    try {
        Remove-Item Env:\YWK_LAUNCHER_APP_DIR     -ErrorAction SilentlyContinue
        Remove-Item Env:\YWK_LAUNCHER_RUNTIME_DIR -ErrorAction SilentlyContinue
        Remove-Item Env:\YWK_LAUNCHER_DATA_DIR    -ErrorAction SilentlyContinue
        $proc = Start-Process -FilePath $exe -PassThru
    } finally {
        $env:YWK_LAUNCHER_APP_DIR     = $savedApp
        $env:YWK_LAUNCHER_RUNTIME_DIR = $savedRuntime
        $env:YWK_LAUNCHER_DATA_DIR    = $savedData
    }
    Write-Host ('[launcher] pid=' + $proc.Id + ' exe=' + $exe)
    return $proc
}

function Get-LauncherWindowById {
    param([Parameter(Mandatory = $true)]$Proc, [Parameter(Mandatory = $true)][string]$Id, [int]$TimeoutSeconds = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        $windows = @()
        try { $windows = @(Get-ProcessWindows -Proc $Proc) } catch { $windows = @() }
        foreach ($w in $windows) {
            $aid = ''
            try { $aid = [string]$w.Current.AutomationId } catch { $aid = '' }
            if ($aid -eq $Id) { return $w }
        }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 400
    }
}

# ------------------------------------------------------------------ the stages

function Invoke-Stage1 {
    param([string]$FlavorName)
    Write-Host ''
    Write-Host '=== stage 1. silent install (design 7-2 stage 1) ==='
    $null = Remove-Installed -FlavorName $FlavorName
    $lnkBefore = @(Get-StartMenuLink)
    $r = Install-Silent -Setup (Get-SetupPath -FlavorName $FlavorName) -LogName ('install-' + $FlavorName + '.log')
    Add-Step ('stage 1: silent install exits 0 (' + $FlavorName + ')') ($r.exit -eq 0) (
        'exit=' + $r.exit + ' sec=' + [math]::Round($r.seconds, 2))

    $joined = ($r.lines -join "`n")
    foreach ($needle in @('User privileges: None', 'Administrative install mode: No',
                          'Install mode root key: HKEY_CURRENT_USER')) {
        $hit = @($r.lines | Select-String -SimpleMatch $needle)
        $line = ''
        if ($hit.Count -gt 0) { $line = $hit[0].Line.Trim() }
        Add-Step ('stage 1: the log says "' + $needle + '" (E-7)') ($hit.Count -ge 1) $line
    }
    Add-Step 'stage 1: the log says "Installation process succeeded."' (
        $joined -like '*Installation process succeeded.*') ''

    $key = Get-UninstallKeyValue -FlavorName $FlavorName
    Add-Step ('stage 1: HKCU uninstall key ' + (Get-AppId -FlavorName $FlavorName) + '_is1 exists') (
        $null -ne $key) ''
    $appDir = ''
    $wantName = Get-ExpectedAppName -FlavorName $FlavorName
    if ($null -ne $key) {
        $appDir = ([string]$key.InstallLocation).TrimEnd('\')
        $expectedDir = Get-ExpectedAppDir -FlavorName $FlavorName
        Add-Step 'stage 1: InstallLocation is the per-user default ({autopf} = %LOCALAPPDATA%\Programs)' (
            $appDir -eq $expectedDir) ('InstallLocation=' + $appDir + ' expected=' + $expectedDir)
        Write-Host ('[key] DisplayName=' + $key.DisplayName)
        Write-Host ('[key] DisplayVersion=' + $key.DisplayVersion)
        Write-Host ('[key] UninstallString=' + $key.UninstallString)
        # decisions 109: the two flavours must be told apart in 'Apps and features'. The .iss writes
        # UninstallDisplayName = '<AppName> <AppVersion>', so the name is the head of the DisplayName.
        $displayName = [string]$key.DisplayName
        Add-Step 'stage 1: DisplayName starts with this flavour AppName (decisions 109)' (
            $displayName.StartsWith($wantName, [System.StringComparison]::Ordinal)) (
            'DisplayName=' + $displayName + ' expected head=' + $wantName)
    }

    $lnkAfter = @(Get-StartMenuLink)
    $newLnk = @($lnkAfter | Where-Object { $lnkBefore -notcontains $_ })
    Add-Step 'stage 1: exactly one new start menu shortcut' ($newLnk.Count -eq 1) (
        'new=' + $newLnk.Count + ' :: ' + ($newLnk -join ' ; '))
    # decisions 109: [Icons] Name is '{autoprograms}\<AppName>', so the .lnk carries the flavour too.
    $lnkName = ''
    if ($newLnk.Count -eq 1) { $lnkName = [System.IO.Path]::GetFileName($newLnk[0]) }
    Add-Step 'stage 1: the new shortcut is named "<AppName>.lnk" (decisions 109)' (
        $lnkName -eq ($wantName + '.lnk')) ('lnk=' + $lnkName + ' expected=' + $wantName + '.lnk')

    if (-not [string]::IsNullOrEmpty($appDir)) {
        $cmp = Compare-AppTree -AppDir $appDir -FlavorName $FlavorName
        Write-Host ('[tree] files=' + $cmp.totalFiles + ' bytes=' + $cmp.totalBytes +
            ' expected=' + $cmp.expectedCount + ' matched=' + $cmp.matched)
        foreach ($m in $cmp.missing)  { Write-Host ('[tree] MISSING  ' + $m) }
        foreach ($m in $cmp.extra)    { Write-Host ('[tree] EXTRA    ' + $m) }
        foreach ($m in $cmp.mismatch) { Write-Host ('[tree] MISMATCH ' + $m) }
        Add-Step 'stage 1: the file set of {app} is exactly what the sources hold (I-3)' (
            ($cmp.missing.Count -eq 0) -and ($cmp.extra.Count -eq 0)) (
            'expected=' + $cmp.expectedCount + ' actual=' + $cmp.actualCount +
            ' missing=' + $cmp.missing.Count + ' extra=' + $cmp.extra.Count)
        $hashOk = ($cmp.mismatch.Count -eq 0)
        if ($AllowSourceDrift -and (-not $hashOk)) {
            Write-Host '[tree] -AllowSourceDrift: the sha256 mismatches above are reported, not counted'
            $hashOk = $true
        }
        Add-Step 'stage 1: every file of {app} matches the source sha256 (I-3)' $hashOk (
            'matched=' + $cmp.matched + '/' + $cmp.expectedCount + ' mismatch=' + $cmp.mismatch.Count)
        $pyc = @(Get-ChildItem -LiteralPath $appDir -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { ($_.Name -like '*.pyc') -or ($_.Name -eq '__pycache__') })
        Add-Step 'stage 1: no __pycache__ and no .pyc reached {app}' ($pyc.Count -eq 0) (
            'hits=' + $pyc.Count)
        $internal = @(Get-ChildItem -LiteralPath (Join-Path $appDir 'docs') -File -ErrorAction SilentlyContinue |
            Where-Object { ($_.Name -eq 'acceptance.md') -or ($_.Name -eq 'contract.md') })
        Add-Step 'stage 1: the internal docs (acceptance.md / contract.md) did not ship' (
            $internal.Count -eq 0) ('hits=' + $internal.Count)
        # v2.0 stage F-2: the one user facing document is docs\guide.md. install.md went back to
        # being the maker's ledger, so it must not be in {app}\docs at all. Named on purpose --
        # Compare-AppTree would call it "extra" and bury it in a count.
        $guide = Join-Path (Join-Path $appDir 'docs') 'guide.md'
        $oldGuide = Join-Path (Join-Path $appDir 'docs') 'install.md'
        $rocmDoc = Join-Path (Join-Path $appDir 'docs') 'radeon.md'
        Add-Step 'stage 1: {app}\docs\guide.md shipped and install.md / radeon.md did not (v2.0 F-2)' (
            (Test-Path -LiteralPath $guide) -and (-not (Test-Path -LiteralPath $oldGuide)) -and
            (-not (Test-Path -LiteralPath $rocmDoc))) (
            'guide.md=' + (Test-Path -LiteralPath $guide) +
            ' install.md=' + (Test-Path -LiteralPath $oldGuide) +
            ' radeon.md=' + (Test-Path -LiteralPath $rocmDoc))
    }
    return $appDir
}

function Invoke-Stage2 {
    param([string]$FlavorName)
    Write-Host ''
    Write-Host '=== stage 2. UI install, three pages with BM_CLICK (design 7-2 stage 2, E-5) ==='
    $null = Remove-Installed -FlavorName $FlavorName
    $setup = Get-SetupPath -FlavorName $FlavorName
    $log = Join-Path $script:LogDir ('install-ui-' + $FlavorName + '.log')
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $setup -PassThru -ArgumentList @('/NORESTART', ('/LOG=' + $log))
    $pages = @()
    $ok = $true
    try {
        $kids = @(Get-SetupChildProcess -RootId $proc.Id -TimeoutSeconds 30)
        Add-Step 'stage 2: the started pid spawned the *.tmp child that owns the window (M-1)' (
            $kids.Count -ge 1) ('children=' + $kids.Count)
        $wiz = Get-InnoWizard -RootId $proc.Id -TimeoutSeconds 60
        Add-Step 'stage 2: the wizard window (ClassName TWizardForm) was found (M-2)' ($null -ne $wiz) ''
        if ($null -eq $wiz) { return }

        $p1 = Wait-InnoPage -Window $wiz -Pattern ([regex]::Escape($T.PageDir)) -TimeoutSeconds 30
        if ($p1.ok) { $pages += 'select-dir' }
        Add-Step 'stage 2: page 1 is the destination page (no welcome page: M-5)' $p1.ok (
            'after ' + [math]::Round($p1.elapsed, 2) + ' s')
        $null = Invoke-InnoButton -Window $wiz -NamePattern ([regex]::Escape($T.BtnNext))

        $p2 = Wait-InnoPage -Window $wiz -Pattern ([regex]::Escape($T.PageReady)) -TimeoutSeconds 30
        if ($p2.ok) { $pages += 'ready' }
        Add-Step 'stage 2: page 2 is the ready page' $p2.ok ('after ' + [math]::Round($p2.elapsed, 2) + ' s')
        $readyText = (($p2.texts) -join ' | ')
        # '*106*' was too loose to mean anything: any three digits anywhere on the page passed it,
        # including a path or a version. Match the phrase the .iss actually writes, and demand the
        # second half of the sentence (the fetch size) as well -- that pair IS the promise
        # design 6-2 makes to the user on this page.
        # Stage G: the page now rounds to GB (charter 6-1: the screen says GB, the ledger keeps GiB),
        # so this gate looks for 'GB' and REFUSES a 'GiB' anywhere on the page -- that way the gate
        # catches a relapse instead of locking the stale 1024-based spelling in (medium 18 / 26).
        $wantSize = $T.ReadySize + ' 96 MB'
        $sizeOk = ($readyText -like ('*' + $wantSize + '*')) -and
                  ($readyText -like '*GB*') -and -not ($readyText -like '*GiB*')
        Add-Step 'stage 2: the ready page carries the size sentence from [Messages]' $sizeOk (
            'looking for "' + $wantSize + '" and a GB figure with no GiB; found size=' +
            ($readyText -like ('*' + $wantSize + '*')) + ' gb=' + ($readyText -like '*GB*') +
            ' gib=' + ($readyText -like '*GiB*'))
        $null = Invoke-InnoButton -Window $wiz -NamePattern ([regex]::Escape($T.BtnInstall))

        $p3 = Wait-InnoPage -Window $wiz -Pattern ([regex]::Escape($T.PageFinished)) -TimeoutSeconds 180
        if ($p3.ok) { $pages += 'finished' }
        Add-Step 'stage 2: page 3 is the finished page' $p3.ok ('after ' + [math]::Round($p3.elapsed, 2) + ' s')

        # The [Run] entry is postinstall and checked by default. Clear it so the probe decides when the
        # launcher starts (stage 3 does that on purpose, with the port pinned first).
        $cbCond = New-Object System.Windows.Automation.PropertyCondition(
            $script:AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
        $boxes = @()
        try { $boxes = @($wiz.FindAll($script:TS::Descendants, $cbCond)) } catch { $boxes = @() }
        foreach ($b in $boxes) {
            $h = 0
            try { $h = [int]$b.Current.NativeWindowHandle } catch { $h = 0 }
            if ($h -ne 0) {
                Write-Host ('[inno] uncheck ' + $b.Current.Name)
                $null = [Ywk.InnoProbe]::SendMessage([System.IntPtr]$h, $script:BM_CLICK,
                    [System.IntPtr]::Zero, [System.IntPtr]::Zero)
            }
        }
        $null = Invoke-InnoButton -Window $wiz -NamePattern ([regex]::Escape($T.BtnFinish)) -Async
    } finally {
        for ($i = 0; $i -lt 60; $i++) {
            if ($proc.HasExited) { break }
            Start-Sleep -Milliseconds 500
        }
        if (-not $proc.HasExited) {
            $ok = $false
            try { & taskkill.exe /PID $proc.Id /T /F 2>&1 | Out-Null } catch { }
        }
        $sw.Stop()
        Stop-AllLaunchers
    }
    Add-Step 'stage 2: the wizard closed on its own after the finish button' $ok (
        'sec=' + [math]::Round($sw.Elapsed.TotalSeconds, 2))
    Add-Step 'stage 2: exactly three wizard pages (E-5: the installer is one operation)' (
        $pages.Count -eq 3) ('pages=' + ($pages -join ' -> '))
    Add-Step 'stage 2: the launcher was NOT started by the finished page (the run box was cleared)' (
        @(Get-LauncherProcess).Count -eq 0) ('launchers=' + @(Get-LauncherProcess).Count)

    $key = Get-UninstallKeyValue -FlavorName $FlavorName
    Add-Step 'stage 2: the UI install wrote the same uninstall key as the silent one' ($null -ne $key) ''
    if ($null -ne $key) {
        $appDir = ([string]$key.InstallLocation).TrimEnd('\')
        $cmp = Compare-AppTree -AppDir $appDir -FlavorName $FlavorName
        foreach ($m in $cmp.missing)  { Write-Host ('[tree] MISSING  ' + $m) }
        foreach ($m in $cmp.extra)    { Write-Host ('[tree] EXTRA    ' + $m) }
        $hashOk = ($cmp.mismatch.Count -eq 0)
        if ($AllowSourceDrift) { $hashOk = $true }
        Add-Step 'stage 2: the UI install produced the same tree as the silent one (I-4)' (
            ($cmp.missing.Count -eq 0) -and ($cmp.extra.Count -eq 0) -and $hashOk) (
            'files=' + $cmp.totalFiles + ' bytes=' + $cmp.totalBytes +
            ' missing=' + $cmp.missing.Count + ' extra=' + $cmp.extra.Count +
            ' mismatch=' + $cmp.mismatch.Count)
    }
}

function Invoke-Stage3 {
    param([string]$FlavorName, [string]$AppDir, [string]$Label = 'stage 3')
    Write-Host ''
    Write-Host ('=== ' + $Label + '. the first run wizard shows the notices (decisions 46) ===')
    Assert-WorkTree
    $null = Reset-WorkDataDir -FlavorName $FlavorName
    $proc = $null
    try {
        $proc = Start-InstalledLauncher -AppDir $AppDir
        $wiz = Get-LauncherWindowById -Proc $proc -Id 'FirstRunWizard' -TimeoutSeconds 60
        Add-Step ($Label + ': the first run wizard window opened (UIA sees WPF)') ($null -ne $wiz) ''
        if ($null -eq $wiz) { return }

        $title = Get-TextById -Root $wiz -Id 'FirstRunStepTitle' -TimeoutSeconds 15
        $number = Get-TextById -Root $wiz -Id 'FirstRunStepNumber' -TimeoutSeconds 15
        Write-Host ('[wizard] title=' + $title + ' number=' + $number)
        Add-Step ($Label + ': step 1 is the third party notices step') (
            ($null -ne $title) -and ($title -like ('*' + $T.NoticeTitle + '*'))) ('title=' + $title)
        Add-Step ($Label + ': the step counter says 1 / 7') (
            ($null -ne $number) -and ($number -replace '\s', '') -eq '1/7') ('number=' + $number)

        $shown = Get-TextById -Root $wiz -Id 'FirstRunNoticesBox' -TimeoutSeconds 20
        $file = Join-Path $AppDir 'licenses\first-run-notices.md'
        $onDisk = ''
        if (Test-Path -LiteralPath $file) {
            $onDisk = [System.IO.File]::ReadAllText($file, (New-Object System.Text.UTF8Encoding($false)))
        }
        $a = ''
        $b = ''
        if ($null -ne $shown) { $a = ($shown -replace "`r", '') }
        $b = ($onDisk -replace "`r", '')
        Add-Step ($Label + ': the notices box holds the bytes of {app}\licenses\first-run-notices.md') (
            ($a.Length -gt 0) -and ($a.TrimEnd() -eq $b.TrimEnd())) (
            'shown=' + $a.Length + ' chars, file=' + $b.Length + ' chars')

        $chk = Find-ById -Root $wiz -Id 'FirstRunAcceptCheck' -TimeoutSeconds 10
        $enabled = $false
        if ($null -ne $chk) { $enabled = [bool]$chk.Current.IsEnabled }
        Add-Step ($Label + ': the accept box is enabled, i.e. the notices really loaded (CanAcceptNotices)') (
            $enabled) ('enabled=' + $enabled)

        Add-Step ($Label + ': no server was started (the private port is quiet)') (
            -not (Test-PortListening -Port $Port)) ('port ' + $Port)
    } finally {
        if ($null -ne $proc) { $null = Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*') }
        Stop-AllLaunchers
    }
}

function Invoke-Stage4 {
    param([string]$FlavorName, [string]$AppDir)
    Write-Host ''
    Write-Host '=== stage 4. a read only {app} (design 7-2 stage 4, E-3) -- opt in ==='
    # icacls is the one ACL tool the stop domain allows (decisions 90). The deny ACE is for the current
    # user only and it is removed in the finally block.
    #
    # NOT '(OI)(CI)W'. Measured 2026-09-05 on this machine: icacls's simple right W is FILE_GENERIC_WRITE,
    # which INCLUDES SYNCHRONIZE, and denying SYNCHRONIZE makes the exe impossible to start at all --
    # Start-Process died with 'An error occurred trying to start process ... Access is denied.' before the
    # launcher ever ran. That is not a read only directory, it is an unopenable one, and it would have made
    # E-3 look like a failure of the launcher. The four specific write rights below (write data, append
    # data, write EA, write attributes) plus delete and delete-child leave read and execute untouched.
    # design 7-2 stage 4 spells the recipe as 'icacls ... /deny (OI)(CI)W' -- that line needs correcting.
    $me = $env:USERNAME
    $applied = $false
    try {
        $out = & icacls.exe $AppDir '/deny' ($me + ':(OI)(CI)(WD,AD,WEA,WA,DC,DE)') 2>&1
        foreach ($l in $out) { Write-Host ('[icacls] ' + $l) }
        $applied = $true
        Add-Step 'stage 4: the deny-write ACE was applied to {app}' $applied ''
        $probe = Join-Path $AppDir 'e2-acl-write-probe.txt'
        $couldWrite = $true
        try {
            [System.IO.File]::WriteAllText($probe, 'x')
        } catch {
            $couldWrite = $false
        }
        if ($couldWrite -and (Test-Path -LiteralPath $probe)) {
            Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        }
        Add-Step 'stage 4: {app} really is read only now (a write into it is refused)' (-not $couldWrite) (
            'could write = ' + $couldWrite)
        Invoke-Stage3 -FlavorName $FlavorName -AppDir $AppDir -Label 'stage 4'
    } finally {
        if ($applied) {
            $out2 = & icacls.exe $AppDir '/remove:d' $me 2>&1
            foreach ($l in $out2) { Write-Host ('[icacls] ' + $l) }
        }
    }
}

function Invoke-Stage5 {
    param([string]$FlavorName, [string]$AppDir)
    Write-Host ''
    Write-Host '=== stage 5. an overwrite install while the launcher runs: AppMutex (design 5-1) ==='
    Assert-WorkTree
    $null = Reset-WorkDataDir -FlavorName $FlavorName
    $setup = Get-SetupPath -FlavorName $FlavorName
    $proc = $null
    try {
        $proc = Start-InstalledLauncher -AppDir $AppDir
        $wiz = Get-LauncherWindowById -Proc $proc -Id 'FirstRunWizard' -TimeoutSeconds 60
        Add-Step 'stage 5: the launcher is up and holding Local\irodori-tts-ywk-launcher-<flavour>' (
            $null -ne $wiz) ''

        # (a) silent. /SUPPRESSMSGBOXES makes the AppMutex message box return its default.
        $r = Install-Silent -Setup $setup -LogName ('install-mutex-silent-' + $FlavorName + '.log')
        $joined = ($r.lines -join "`n")
        $blocked = ($r.exit -ne 0) -or ($joined -notlike '*Installation process succeeded.*')
        foreach ($needle in @('Mutex', 'mutex', 'is currently running', 'Defaulting to',
                              'Got EAbort exception', 'Deinitializing Setup')) {
            foreach ($hit in @($r.lines | Select-String -SimpleMatch $needle)) {
                Write-Host ('[mutex] ' + $hit.Line.Trim())
            }
        }
        Add-Step 'stage 5: RECORDED -- silent overwrite while the launcher runs' $true (
            'exit=' + $r.exit + ' blocked=' + $blocked +
            ' succeeded-line=' + ($joined -like '*Installation process succeeded.*'))
        # AppMutex is checked long before RestartManager, so CloseApplications never gets a turn:
        # 'Starting the installation process.' must not be in the log at all.
        Add-Step 'stage 5: AppMutex refuses the overwrite before the install starts (design 5-1)' (
            ($joined -like '*Got EAbort exception.*') -and
            ($joined -notlike '*Starting the installation process.*')) (
            'EAbort=' + ($joined -like '*Got EAbort exception.*') +
            ' started=' + ($joined -like '*Starting the installation process.*') +
            ' RestartManager=' + ($joined -like '*RestartManager*'))

        # (b) the same thing through the UI, to read the words the user is shown.
        $log2 = Join-Path $script:LogDir ('install-mutex-ui-' + $FlavorName + '.log')
        $ui = Start-Process -FilePath $setup -PassThru -ArgumentList @('/NORESTART', ('/LOG=' + $log2))
        $seen = ''
        try {
            $dlg = Wait-InnoDialog -RootId $ui.Id -Pattern ([regex]::Escape($T.Detected)) -TimeoutSeconds 45
            if ($null -ne $dlg) {
                $seen = ((Get-InnoTexts -Window $dlg) -join ' | ')
                Write-Host ('[mutex] dialog: ' + $seen)
                $null = Invoke-InnoButton -Window $dlg -NamePattern ([regex]::Escape($T.BtnCancel)) -Async
            } else {
                $w = Get-InnoWizard -RootId $ui.Id -TimeoutSeconds 20
                if ($null -ne $w) {
                    $seen = ((Get-InnoTexts -Window $w) -join ' | ')
                    Write-Host ('[mutex] no refusal; the wizard opened: ' + $seen)
                }
            }
        } finally {
            for ($i = 0; $i -lt 30; $i++) {
                if ($ui.HasExited) { break }
                Start-Sleep -Milliseconds 400
            }
            if (-not $ui.HasExited) {
                foreach ($id in @(Get-ProcessFamilyId -RootId $ui.Id)) {
                    try { & taskkill.exe /PID $id /T /F 2>&1 | Out-Null } catch { }
                }
            }
        }
        $uiLines = @()
        if (Test-Path -LiteralPath $log2) { $uiLines = @(Get-Content -LiteralPath $log2 -Encoding UTF8) }
        $uiJoined = ($uiLines -join "`n")
        Add-Step 'stage 5: RECORDED -- the words the UI shows for a running app (AppMutex / CloseApplications)' (
            $true) ('dialog matched SetupAppRunningError = ' + ($seen -like ('*' + $T.Detected + '*')))
        Add-Step 'stage 5: the UI shows SetupAppRunningError, NOT the CloseApplications page' (
            ($uiJoined -like '*Message box (OK/Cancel):*') -and
            ($uiJoined -notlike '*RestartManager*')) (
            'msgbox=' + ($uiJoined -like '*Message box (OK/Cancel):*') +
            ' RestartManager=' + ($uiJoined -like '*RestartManager*'))
    } finally {
        if ($null -ne $proc) { $null = Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*') }
        Stop-AllLaunchers
    }
    # The overwrite may have half written {app}; put a known tree back for the stages that follow.
    $null = Remove-Installed -FlavorName $FlavorName
    $r2 = Install-Silent -Setup $setup -LogName ('install-after-mutex-' + $FlavorName + '.log')
    Add-Step 'stage 5: a clean reinstall after the mutex run exits 0' ($r2.exit -eq 0) ('exit=' + $r2.exit)
    return (Get-InstalledAppDir -FlavorName $FlavorName)
}

function Invoke-Stage6 {
    Write-Host ''
    Write-Host '=== stage 6. [InstallDelete] wipes a stale ledger and stale branches (design 1-3) ==='
    $null = Remove-Installed -FlavorName 'cuda'
    $r = Install-Silent -Setup $SetupCuda -LogName 'install-stage6-a.log'
    Add-Step 'stage 6: the CUDA install exits 0' ($r.exit -eq 0) ('exit=' + $r.exit)
    $appDir = Get-InstalledAppDir -FlavorName 'cuda'
    if ([string]::IsNullOrEmpty($appDir)) {
        # A silent 'return' here used to take every remaining check of this stage off the
        # denominator without a word: '0 failure(s) of N' with a smaller N reads like a pass.
        Add-Step 'stage 6: {app} was found after the install' $false (
            'the HKCU key gave no InstallLocation; the rest of stage 6 did not run')
        return
    }

    # The one that matters: ReleaseFlavor.Detect calls a tree with any runtime-rocm-* a Radeon release,
    # which takes cu130 / cu126 out of the launcher's UI (design 1-3 (b)).
    $stale = Join-Path $appDir 'ledger\runtime-rocm-gfx1151.json'
    Copy-Item -LiteralPath (Join-Path $SrcApp 'ledger\runtime-rocm-gfx1151.json') -Destination $stale -Force
    $planted = @($stale)
    # docs\install.md and docs\radeon.md: the two files v2.0 stage F-2 took OUT of [Files]. On a
    # machine that already has them, [Files] going quiet changes nothing -- the .iss lines that
    # remove them are in [InstallDelete], and this is the only path where that matters
    # (stage 1 covers a FRESH install, where [Files] alone is sufficient). 是正 2026-09-11, low 12.
    foreach ($rel in @('server\zz-e2-stale.py', 'licenses\zz-e2-stale.md',
                       'voices\presets\zz-e2-stale.wav', 'docs\install.md', 'docs\radeon.md')) {
        $p = Join-Path $appDir $rel
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $p)
        [System.IO.File]::WriteAllText($p, 'e2 stale marker', (New-Object System.Text.UTF8Encoding($false)))
        $planted += $p
    }
    $allThere = $true
    foreach ($p in $planted) { if (-not (Test-Path -LiteralPath $p)) { $allThere = $false } }
    Add-Step 'stage 6: six stale files were planted in {app}' $allThere ('planted=' + $planted.Count)

    # The start menu shortcut of an OLDER generation (是正 2026-09-11, medium 10 / low 12). Inno does
    # not remove a renamed .lnk on its own, so v2.0 names both past generations in [InstallDelete].
    # Planted under the CURRENT user's start menu = {autoprograms} for a per-user install.
    $programs = $script:StartMenuDir
    $null = New-Item -ItemType Directory -Force -Path $programs
    $oldLnk = Join-Path $programs ((Get-OldAppName -FlavorName 'cuda') + '.lnk')
    $olderLnk = Join-Path $programs ((Get-OlderAppName -FlavorName 'cuda') + '.lnk')
    foreach ($lnk in @($oldLnk, $olderLnk)) {
        [System.IO.File]::WriteAllText($lnk, 'e2 stale shortcut', (New-Object System.Text.UTF8Encoding($false)))
    }
    Add-Step 'stage 6: the two past-generation .lnk names were planted in the start menu' (
        (Test-Path -LiteralPath $oldLnk) -and (Test-Path -LiteralPath $olderLnk)) (
        'old=' + (Split-Path -Leaf $oldLnk) + ' older=' + (Split-Path -Leaf $olderLnk))

    $r2 = Install-Silent -Setup $SetupCuda -LogName 'install-stage6-b.log'
    Add-Step 'stage 6: the overwrite install exits 0' ($r2.exit -eq 0) ('exit=' + $r2.exit)
    $survivors = @()
    foreach ($p in $planted) { if (Test-Path -LiteralPath $p) { $survivors += $p } }
    Add-Step 'stage 6: [InstallDelete] removed the stale runtime-rocm-gfx1151.json (design 1-3 (b))' (
        -not (Test-Path -LiteralPath $stale)) ('still there = ' + (Test-Path -LiteralPath $stale))
    Add-Step 'stage 6: [InstallDelete] removed every planted file (server / licenses / voices\presets / docs)' (
        $survivors.Count -eq 0) ('survivors=' + $survivors.Count + ' :: ' + ($survivors -join ' ; '))
    $lnkSurvivors = @()
    foreach ($lnk in @($oldLnk, $olderLnk)) { if (Test-Path -LiteralPath $lnk) { $lnkSurvivors += $lnk } }
    Add-Step 'stage 6: [InstallDelete] removed both past-generation start menu shortcuts (medium 10)' (
        $lnkSurvivors.Count -eq 0) ('survivors=' + $lnkSurvivors.Count + ' :: ' +
        (($lnkSurvivors | ForEach-Object { Split-Path -Leaf $_ }) -join ' ; '))
    foreach ($lnk in $lnkSurvivors) { Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue }
    # Measured 2026-09-05: Inno 6.7.3 writes NOTHING to the log for [InstallDelete] entries. The
    # file system above is the only evidence there is, which is why this stage plants files instead of
    # reading the log. Recorded so a later reader does not go looking for a line that never existed.
    $del = @($r2.lines | Select-String -SimpleMatch 'Deleting directory:')
    Add-Step 'stage 6: RECORDED -- [InstallDelete] leaves no trace in the install log' $true (
        '"Deleting directory:" lines = ' + $del.Count + ' (the proof is the file system, not the log)')

    $cmp = Compare-AppTree -AppDir $appDir -FlavorName 'cuda'
    $hashOk = ($cmp.mismatch.Count -eq 0)
    if ($AllowSourceDrift) { $hashOk = $true }
    Add-Step 'stage 6: after the overwrite {app} is exactly the CUDA tree again' (
        ($cmp.missing.Count -eq 0) -and ($cmp.extra.Count -eq 0) -and $hashOk) (
        'missing=' + $cmp.missing.Count + ' extra=' + $cmp.extra.Count + ' mismatch=' + $cmp.mismatch.Count)
    $null = Remove-Installed -FlavorName 'cuda' -LogName 'uninstall-stage6.log'
}

function Invoke-Stage7 {
    param([string]$FlavorName)
    Write-Host ''
    Write-Host '=== stage 7. silent uninstall (design 7-2 stage 7, E-4, decisions 132/133) ==='
    Assert-WorkTree
    $null = Remove-Installed -FlavorName 'cuda'
    $null = Remove-Installed -FlavorName 'radeon'
    $root = Get-DataDirRoot -FlavorName $FlavorName
    $before = Reset-WorkDataDir -FlavorName $FlavorName
    $r0 = Install-Silent -Setup (Get-SetupPath -FlavorName $FlavorName) -LogName ('install-stage7-' + $FlavorName + '.log')
    Add-Step 'stage 7: the install under test exits 0' ($r0.exit -eq 0) ('exit=' + $r0.exit)
    $appDir = Get-InstalledAppDir -FlavorName $FlavorName
    if ([string]::IsNullOrEmpty($appDir)) {
        Add-Step 'stage 7: {app} was found after the install' $false (
            'the HKCU key gave no InstallLocation; the rest of stage 7 did not run')
        return
    }
    $lnkBefore = @(Get-StartMenuLink)

    $r = Uninstall-Silent -AppDir $appDir -LogName ('uninstall-stage7-' + $FlavorName + '.log')
    Add-Step 'stage 7: the silent uninstall exits 0' ($r.exit -eq 0) (
        'exit=' + $r.exit + ' sec=' + [math]::Round($r.seconds, 2))
    $joined = ($r.lines -join "`n")
    Add-Step 'stage 7: the log says "Removed all? Yes"' ($joined -like '*Removed all? Yes*') ''
    # decisions 132/133 dropped both [Code] questions, so /SUPPRESSMSGBOXES has nothing left to
    # suppress. The old expectation was exactly TWO of these lines; the new one is ZERO. A question
    # that came back would show up here before it showed up in the data tree.
    $def = @($r.lines | Select-String -SimpleMatch 'Defaulting to No for suppressed message box (Yes/No):')
    foreach ($d in $def) { Write-Host ('[uninst] ' + $d.Line.Trim()) }
    Add-Step 'stage 7: the uninstall asked NOTHING (decisions 132/133)' ($def.Count -eq 0) (
        'Defaulting-to-No lines = ' + $def.Count + ' (expected 0)')

    Add-Step 'stage 7: {app} is gone' (-not (Test-Path -LiteralPath $appDir)) ('appDir=' + $appDir)
    Add-Step 'stage 7: the HKCU uninstall key is gone' (
        $null -eq (Get-UninstallKeyValue -FlavorName $FlavorName)) ''
    $lnkAfter = @(Get-StartMenuLink)
    # Name the shortcut instead of watching a total. The old form ("fewer .lnk than before") turns
    # green when ANY other .lnk in the start menu disappears during the run, and stays green if ours
    # survives while another app adds one.
    $lnkOursBefore = @($lnkBefore | Where-Object { (Split-Path -Leaf $_) -like '*irodori*' })
    $lnkOursAfter = @($lnkAfter | Where-Object { (Split-Path -Leaf $_) -like '*irodori*' })
    Add-Step 'stage 7: the start menu shortcut named irodori is gone' (
        ($lnkOursBefore.Count -gt 0) -and ($lnkOursAfter.Count -eq 0)) (
        'irodori .lnk before=' + $lnkOursBefore.Count + ' after=' + $lnkOursAfter.Count +
        ' (start menu total ' + $lnkBefore.Count + ' -> ' + $lnkAfter.Count + ')')

    # decisions 132: the old rule was "the data tree is untouched (default = keep)". The commander's
    # verbatim killed it -- "leaving several GB behind after an uninstall is plainly outside what an
    # app may do" -- so the new rule is the opposite: THIS flavour's tree is gone, whole and unasked.
    $after = Get-DataTreeSnapshot -Root $root
    Add-Step 'stage 7: this flavour data tree is gone whole, unasked (decisions 132/133)' (
        -not $after.exists) (
        'root=' + $root + ' before files=' + $before.files + ' bytes=' + $before.bytes +
        ' :: after exists=' + $after.exists + ' files=' + $after.files + ' bytes=' + $after.bytes)
    $otherRoot = Get-DataDirRoot -FlavorName (Get-OtherFlavor -FlavorName $FlavorName)
    Add-Step 'stage 7: the OTHER flavour data tree was never created by this run' (
        -not (Get-DataTreeSnapshot -Root $otherRoot).exists) ('other root=' + $otherRoot)
}

function Get-OtherFlavor {
    param([string]$FlavorName)
    if ($FlavorName -eq 'cuda') { return 'radeon' }
    return 'cuda'
}

function Invoke-UninstallUi {
    <#
      .SYNOPSIS
        Drive the uninstaller through its UI and prove NO question window appears.
        Returns .asked = the number of TaskDialog questions that showed up (the expectation is 0).
      .DESCRIPTION
        /VERYSILENT is passed and /SUPPRESSMSGBOXES is NOT. That combination used to be what raised
        the two [Code] questions instead of letting them fall to their IDNO default (M-8), and it is
        kept for exactly that reason: decisions 132/133 (v2.0 stage F-2) removed both from
        CurUninstallStepChanged, so if one ever came back it would be raised HERE, in the open,
        rather than silently defaulting. The stage now waits the short timeout on each old marker
        and expects to see neither. **Nothing is answered** -- an answered question would leave a
        green run standing in front of a red product.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$AppDir,
        [Parameter(Mandatory = $true)][string]$LogName
    )
    $unins = Join-Path $AppDir 'unins000.exe'
    $log = Join-Path $script:LogDir $LogName
    $asked = 0
    $seen = @()
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $unins -PassThru -ArgumentList @('/VERYSILENT', '/NORESTART', ('/LOG=' + $log))
    try {
        foreach ($pair in @(
                @{ marker = $T.AskRuntime; label = 'the retired question 1 (runtime and models)' },
                @{ marker = $T.AskVoices;  label = 'the retired question 2 (voices and settings)' })) {
            $dlg = Wait-InnoDialog -RootId $p.Id -Pattern ([regex]::Escape($pair.marker)) -TimeoutSeconds 12
            if ($null -eq $dlg) {
                Write-Host ('[uninst-ui] ' + $pair.label + ' did NOT appear (correct)')
                continue
            }
            $asked++
            $seen += $pair.label
            Write-Host ('[uninst-ui] ' + $pair.label + ' APPEARED: ' +
                ((Get-InnoTexts -Window $dlg) -join ' | '))
            # Close it so the uninstall can finish, but do not choose: the step fails on $asked.
            $null = Invoke-InnoButton -Window $dlg -NamePattern '.' -TimeoutSeconds 15 -Async
        }
    } finally {
        # $p.HasExited is NOT the end of the uninstall: the _iu*.tmp copy outlives its parent, and
        # the data tree is wiped in usPostUninstall, after {app} is already gone. Waiting on
        # either of those reads the data tree mid flight. Wait-UninstallFinished is the real fence.
        for ($i = 0; $i -lt 120; $i++) {
            if ($p.HasExited) { break }
            Start-Sleep -Milliseconds 400
        }
        # Recorded on purpose, every run: how much of the log existed at the moment the old code
        # stopped waiting. It is the size of the race, in lines, measured live instead of argued.
        $linesAtExit = @(Read-SharedTextLines -Path $log | Where-Object { $_.Trim().Length -gt 0 }).Count
        $fin = Wait-UninstallFinished -LogPath $log -TimeoutSeconds 90
        $logClosed = $fin.closed
        if (-not $p.HasExited) {
            foreach ($id in @(Get-ProcessFamilyId -RootId $p.Id)) {
                try { & taskkill.exe /PID $id /T /F 2>&1 | Out-Null } catch { }
            }
        }
        $sw.Stop()
    }
    for ($i = 0; $i -lt 60; $i++) {
        if (-not (Test-Path -LiteralPath $AppDir)) { break }
        Start-Sleep -Milliseconds 250
    }
    $lines = @()
    if (Test-Path -LiteralPath $log) { $lines = @(Get-Content -LiteralPath $log -Encoding UTF8) }
    # 'User chose Yes.' / 'User chose No.' was the uninstaller's own record of an answer reaching
    # SuppressibleTaskDialogMsgBox. There is nothing left to answer, so this list must be empty too
    # -- it is the second, independent witness that the questions are gone.
    $chose = @($lines | Where-Object { $_ -match 'User chose (Yes|No)\.' })
    foreach ($c in $chose) { Write-Host ('[uninst-ui] ' + $c.Trim()) }
    Write-Host ('[uninst-ui] asked=' + $asked + ' answered=' + $chose.Count + ' logClosed=' + $logClosed +
        ' sec=' + [math]::Round($sw.Elapsed.TotalSeconds, 2) + ' log=' + $lines.Count + ' lines' +
        ' (at the parent exit the log had ' + $linesAtExit + ' line(s) = what the old code judged on)')
    return [pscustomobject]@{
        asked = $asked; answered = $chose.Count; logClosed = $logClosed; seen = $seen
        linesAtExit = $linesAtExit
        seconds = $sw.Elapsed.TotalSeconds; lines = $lines
    }
}

function Test-DataBranch {
    <#
      .SYNOPSIS
        -Gone must not exist under $Root, -Kept must. Returns the complaints (empty = pass).
    #>
    param([string]$Root, [string[]]$Gone, [string[]]$Kept)
    $bad = @()
    foreach ($g in $Gone) {
        if (Test-Path -LiteralPath (Join-Path $Root $g)) { $bad += ('still there: ' + $g) }
    }
    foreach ($k in $Kept) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $k))) { $bad += ('lost: ' + $k) }
    }
    return $bad
}

$script:WholeTree = @('runtime', 'models', 'cache', 'logs', 'miopen', 'voices', 'settings.json')

function New-OutsideMarker {
    <#
      .SYNOPSIS
        One file OUTSIDE every data tree, standing in for "the wav the user picked when adding a
        voice" (decisions 133 (2) -- the app copies it into voices\refs\ and never owns the original).
        The uninstall must not reach it. Returns the path.
    #>
    param()
    $dir = Join-Path $script:LogDir 'outside'
    $null = New-Item -ItemType Directory -Path $dir -Force
    $path = Join-Path $dir 'the-users-own-source.wav'
    [System.IO.File]::WriteAllText($path, 'not the app to delete',
        (New-Object System.Text.UTF8Encoding($false)))
    return $path
}

function Invoke-Stage8 {
    param([string]$FlavorName)
    Write-Host ''
    Write-Host '=== stage 8. UI uninstall: no questions, the flavour tree goes whole (decisions 132/133) ==='
    Assert-WorkTree
    $other = Get-OtherFlavor -FlavorName $FlavorName
    $root = Get-DataDirRoot -FlavorName $FlavorName
    $otherRoot = Get-DataDirRoot -FlavorName $other
    $legacy = $script:LegacyDataDir

    # 8a: this flavour alone. The whole tree goes, unasked, and a file outside it survives.
    $null = Remove-Installed -FlavorName 'cuda'
    $null = Remove-Installed -FlavorName 'radeon'
    $null = Reset-WorkDataDir -FlavorName $FlavorName
    $outside = New-OutsideMarker
    $r = Install-Silent -Setup (Get-SetupPath -FlavorName $FlavorName) -LogName 'install-stage8a.log'
    Add-Step 'stage 8a: install exits 0' ($r.exit -eq 0) ('exit=' + $r.exit)
    $appDir = Get-InstalledAppDir -FlavorName $FlavorName
    if (-not [string]::IsNullOrEmpty($appDir)) {
        $u = Invoke-UninstallUi -AppDir $appDir -LogName 'uninstall-stage8a.log'
        Add-Step 'stage 8a: the uninstall asked NOTHING (decisions 132/133)' (
            ($u.asked -eq 0) -and ($u.answered -eq 0)) (
            'questions raised=' + $u.asked + ' answers logged=' + $u.answered +
            ' :: ' + ($u.seen -join ' ; '))
        # Without this step the ones below are blanks fired at a tree nobody has touched yet:
        # 'Log closed.' is the only event that means the uninstaller is done with the data tree.
        Add-Step 'stage 8a: the uninstaller ran to the end (the log closed)' $u.logClosed (
            'logClosed=' + $u.logClosed + ' log=' + $u.lines.Count + ' lines')
        $bad = @(Test-DataBranch -Root $root -Gone $script:WholeTree -Kept @())
        Add-Step 'stage 8a: every branch of this flavour data tree is gone' ($bad.Count -eq 0) (
            $bad -join ' ; ')
        Add-Step 'stage 8a: the data root itself is gone' (-not (Test-Path -LiteralPath $root)) (
            'root=' + $root + ' exists=' + (Test-Path -LiteralPath $root))
        Add-Step 'stage 8a: a file OUTSIDE the data tree was not touched (decisions 133 (2))' (
            Test-Path -LiteralPath $outside) ('outside=' + $outside)
        Add-Step 'stage 8a: {app} and the key are gone' (
            (-not (Test-Path -LiteralPath $appDir)) -and
            ($null -eq (Get-UninstallKeyValue -FlavorName $FlavorName))) ''
    } else {
        Add-Step 'stage 8a: {app} was found after the install' $false (
            'the HKCU key gave no InstallLocation; the uninstall of this sub stage did not run')
    }

    # 8b: the legacy shared tree (<= v1.1.0) is present and the other flavour is NOT installed.
    # decisions 133 (5): it goes too, because on a machine that upgraded and was never opened the
    # migration never ran and ~8 GB would otherwise sit there for ever.
    $null = Reset-WorkDataDir -FlavorName $FlavorName
    $null = Reset-WorkDataDir -Root $legacy
    $r = Install-Silent -Setup (Get-SetupPath -FlavorName $FlavorName) -LogName 'install-stage8b.log'
    Add-Step 'stage 8b: install exits 0' ($r.exit -eq 0) ('exit=' + $r.exit)
    $appDir = Get-InstalledAppDir -FlavorName $FlavorName
    if (-not [string]::IsNullOrEmpty($appDir)) {
        $u = Invoke-UninstallUi -AppDir $appDir -LogName 'uninstall-stage8b.log'
        Add-Step 'stage 8b: the uninstall asked NOTHING' (($u.asked -eq 0) -and ($u.answered -eq 0)) (
            'questions raised=' + $u.asked + ' answers logged=' + $u.answered)
        Add-Step 'stage 8b: the uninstaller ran to the end (the log closed)' $u.logClosed (
            'logClosed=' + $u.logClosed + ' log=' + $u.lines.Count + ' lines')
        Add-Step 'stage 8b: this flavour data tree is gone' (-not (Test-Path -LiteralPath $root)) (
            'root=' + $root)
        Add-Step 'stage 8b: the legacy shared tree went with it (decisions 133 (5))' (
            -not (Test-Path -LiteralPath $legacy)) ('legacy=' + $legacy)
    } else {
        Add-Step 'stage 8b: {app} was found after the install' $false (
            'the HKCU key gave no InstallLocation; the uninstall of this sub stage did not run')
    }

    # 8c: the other flavour is installed too. Its tree must not lose a byte, and the legacy tree
    # must survive as well -- the other flavour still needs it to migrate from.
    $null = Reset-WorkDataDir -FlavorName $FlavorName
    $otherBefore = Reset-WorkDataDir -FlavorName $other
    $legacyBefore = Reset-WorkDataDir -Root $legacy
    $rA = Install-Silent -Setup (Get-SetupPath -FlavorName $FlavorName) -LogName 'install-stage8c-a.log'
    $rB = Install-Silent -Setup (Get-SetupPath -FlavorName $other)      -LogName 'install-stage8c-b.log'
    Add-Step 'stage 8c: both flavours installed side by side' (($rA.exit -eq 0) -and ($rB.exit -eq 0)) (
        $FlavorName + '=' + $rA.exit + ' ' + $other + '=' + $rB.exit)
    $appDir = Get-InstalledAppDir -FlavorName $FlavorName
    if (-not [string]::IsNullOrEmpty($appDir)) {
        $u = Invoke-UninstallUi -AppDir $appDir -LogName 'uninstall-stage8c.log'
        Add-Step 'stage 8c: the uninstall asked NOTHING' (($u.asked -eq 0) -and ($u.answered -eq 0)) (
            'questions raised=' + $u.asked + ' answers logged=' + $u.answered)
        Add-Step 'stage 8c: the uninstaller ran to the end (the log closed)' $u.logClosed (
            'logClosed=' + $u.logClosed + ' log=' + $u.lines.Count + ' lines')
        Add-Step 'stage 8c: this flavour data tree is gone' (-not (Test-Path -LiteralPath $root)) (
            'root=' + $root)
        $otherAfter = Get-DataTreeSnapshot -Root $otherRoot
        Add-Step 'stage 8c: the OTHER flavour data tree did not lose a byte (decisions 133 (1))' (
            $otherAfter.exists -and ($otherAfter.files -eq $otherBefore.files) -and
            ($otherAfter.bytes -eq $otherBefore.bytes)) (
            'other=' + $otherRoot + ' before files=' + $otherBefore.files +
            ' bytes=' + $otherBefore.bytes + ' :: after files=' + $otherAfter.files +
            ' bytes=' + $otherAfter.bytes)
        $legacyAfter = Get-DataTreeSnapshot -Root $legacy
        Add-Step 'stage 8c: the legacy shared tree survived (the other flavour still needs it)' (
            $legacyAfter.exists -and ($legacyAfter.files -eq $legacyBefore.files)) (
            'legacy=' + $legacy + ' before files=' + $legacyBefore.files +
            ' :: after files=' + $legacyAfter.files)
    } else {
        Add-Step 'stage 8c: {app} was found after the install' $false (
            'the HKCU key gave no InstallLocation; the uninstall of this sub stage did not run')
    }
    $null = Remove-Installed -FlavorName $other -LogName 'uninstall-stage8c-other.log'
}

# ------------------------------------------------------------------ run

Write-Host '=== probe/e-install-probe.ps1 ==='
Write-Host ('[cfg] flavor=' + $Flavor + ' steps=' + ($StepList -join ',') + ' includeAcl=' + $IncludeAcl)
Write-Host ('[cfg] setup cuda   = ' + $SetupCuda)
Write-Host ('[cfg] setup radeon = ' + $SetupRadeon)
Write-Host ('[cfg] srcApp=' + $SrcApp)
Write-Host ('[cfg] srcExe=' + $SrcExe)
Write-Host ('[cfg] logs  =' + $script:LogDir)

$busyStart = @(Assert-QuietPorts)
Add-Step 'the reserved ports are quiet before the run' ($busyStart.Count -eq 0) (
    'busy = ' + ($busyStart -join ', '))
Add-Step 'no launcher of the operator is running' (@(Get-LauncherProcess).Count -eq 0) (
    'launchers = ' + @(Get-LauncherProcess).Count)
# The registry key is not the whole answer: a run that died mid flight can leave {app} behind with
# no key, and then the FIRST time anyone notices is the teardown -- which books the leftovers of the
# previous run as a failure of this one. Look at both, here, where it is still the truth.
$startDirs = @(Get-ChildItem -LiteralPath $script:ProgramsRoot -Filter 'irodori*' -ErrorAction SilentlyContinue)
Add-Step 'the machine starts with no irodori install (no HKCU key and nothing under %LOCALAPPDATA%\Programs)' (
    ($null -eq (Get-UninstallKeyValue -FlavorName 'cuda')) -and
    ($null -eq (Get-UninstallKeyValue -FlavorName 'radeon')) -and
    ($startDirs.Count -eq 0)) (
    'stale {app} dir(s) = ' + $startDirs.Count + ' :: ' +
    ((@($startDirs | ForEach-Object { $_.Name })) -join ' ; '))

# EVERY stage that installs is on this list, stage 1 included, and the reason is the teardown, not
# the stage: whatever a stage installs, the finally block below uninstalls, and an uninstall is the
# one operation in this file that can reach the operator's data tree. Stage 1 used to be left out,
# which made a '-Steps 1' run drive a real install AND a real uninstall over the operator's real
# 16.99 GB tree with no backup taken -- the only thing standing between that run and the tree was
# the 0, IDNO default of two SuppressibleTaskDialogMsgBox calls in the .iss. That is a file away
# from a disaster, not a safeguard. Stage 2 is here for the same reason plus one of its own: the
# finished page's [Run] check box can start the launcher, which writes into the data tree.
$needWork = $false
foreach ($s in @(1, 2, 3, 4, 5, 6, 7, 8)) { if ($StepList -contains $s) { $needWork = $true } }
if ($IncludeAcl) { $needWork = $true }

try {
    if ($needWork) {
        if (-not $DataDirBackup) {
            throw 'The chosen stages touch the data tree; -DataDirBackup must stay on.'
        }
        $null = Backup-DataTree
    }

    # Every stage is guarded: one stage throwing must be a recorded FAIL, not a run that loses the
    # summary of the stages that already passed. The teardown below runs either way.
    $appDir = ''
    if ($StepList -contains 1) {
        try { $appDir = Invoke-Stage1 -FlavorName $Flavor }
        catch { Add-Step 'stage 1 threw' $false $_.Exception.Message }
    }
    if ($StepList -contains 2) {
        try { Invoke-Stage2 -FlavorName $Flavor }
        catch { Add-Step 'stage 2 threw' $false $_.Exception.Message }
        $appDir = Get-InstalledAppDir -FlavorName $Flavor
    }
    if (($StepList -contains 3) -or ($StepList -contains 5) -or $IncludeAcl) {
        # Guarded like every stage: this was the one piece of the run that was not, and a throw here
        # (measured with a -SetupRadeon that does not exist) skipped 'exit $script:Failures.Count'
        # altogether -- no '=== summary ===', no '=== N failure(s) of M ===', and PowerShell's own
        # exit 1, which is indistinguishable from one failed step.
        try {
            if ([string]::IsNullOrEmpty($appDir)) {
                $null = Install-Silent -Setup (Get-SetupPath -FlavorName $Flavor) -LogName ('install-pre-' + $Flavor + '.log')
                $appDir = Get-InstalledAppDir -FlavorName $Flavor
            }
        } catch {
            Add-Step 'the pre-install for stages 3/5/-IncludeAcl threw' $false $_.Exception.Message
            $appDir = ''
        }
    }
    if ($StepList -contains 3) {
        try { Invoke-Stage3 -FlavorName $Flavor -AppDir $appDir }
        catch { Add-Step 'stage 3 threw' $false $_.Exception.Message }
    }
    if ($IncludeAcl) {
        try { Invoke-Stage4 -FlavorName $Flavor -AppDir $appDir }
        catch { Add-Step 'stage 4 threw' $false $_.Exception.Message }
    }
    if ($StepList -contains 5) {
        try { $appDir = Invoke-Stage5 -FlavorName $Flavor -AppDir $appDir }
        catch { Add-Step 'stage 5 threw' $false $_.Exception.Message }
    }
    if ($StepList -contains 6) {
        try { Invoke-Stage6 }
        catch { Add-Step 'stage 6 threw' $false $_.Exception.Message }
    }
    if ($StepList -contains 7) {
        try { Invoke-Stage7 -FlavorName $Flavor }
        catch { Add-Step 'stage 7 threw' $false $_.Exception.Message }
    }
    if ($StepList -contains 8) {
        try { Invoke-Stage8 -FlavorName $Flavor }
        catch { Add-Step 'stage 8 threw' $false $_.Exception.Message }
    }
} finally {
    Write-Host ''
    Write-Host '=== teardown ==='
    try { Stop-AllLaunchers } catch { Write-Host ('[teardown] Stop-AllLaunchers failed: ' + $_.Exception.Message) }
    foreach ($f in @('cuda', 'radeon')) {
        try {
            if (Remove-Installed -FlavorName $f -LogName ('teardown-' + $f + '.log')) {
                Write-Host ('[teardown] uninstalled ' + $f)
            }
        } catch {
            Write-Host ('[teardown] uninstall of ' + $f + ' failed: ' + $_.Exception.Message)
        }
    }
    # THE INSPECTIONS BELOW GO IN THEIR OWN try/finally so that Restore-DataTree runs even if one of
    # them throws. It is the last thing in the run that matters and it used to be the last thing in
    # the block: three naked calls (Get-ItemProperty under $ErrorActionPreference = 'Stop',
    # Get-ChildItem, Get-StartMenuLink) stood between the operator's parked tree and its way home.
    # The uninstalls above stay FIRST, and outside this try: they are the operation that can reach a
    # data tree, so they must run while the stand-in, not the operator's tree, is in place.
    try {
        $leftKeys = @()
        foreach ($f in @('cuda', 'radeon')) {
            if ($null -ne (Get-UninstallKeyValue -FlavorName $f)) { $leftKeys += $f }
        }
        Add-Step 'teardown: no irodori uninstall key is left in HKCU' ($leftKeys.Count -eq 0) (
            'left = ' + ($leftKeys -join ', '))
        $leftDirs = @(Get-ChildItem -LiteralPath $script:ProgramsRoot -Filter 'irodori*' -ErrorAction SilentlyContinue)
        Add-Step 'teardown: no {app} is left under %LOCALAPPDATA%\Programs' ($leftDirs.Count -eq 0) (
            'left = ' + $leftDirs.Count)
        $leftLnk = @(Get-StartMenuLink | Where-Object { (Split-Path -Leaf $_) -like '*irodori*' })
        Add-Step 'teardown: no start menu shortcut is left' ($leftLnk.Count -eq 0) ('left = ' + $leftLnk.Count)
    } finally {
        Restore-DataTree
    }
    $busyEnd = @(Assert-QuietPorts)
    # The private port the launcher of this run was pinned to is checked here too: Assert-QuietPorts
    # only knows the three forbidden ports (probe/common.ps1:49), so a launcher or a ywk_server that
    # survived stage 5 still holding 18097 used to leave the run green.
    $portBusy = @()
    try {
        $conn = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
        if ($conn.Count -gt 0) { $portBusy += $Port }
    } catch { }
    Add-Step 'teardown: the reserved ports and this run private port are still quiet' (
        ($busyEnd.Count -eq 0) -and ($portBusy.Count -eq 0)) (
        'busy = ' + ($busyEnd -join ', ') + ' ; private ' + $Port + ' listening = ' + ($portBusy.Count -gt 0))
}

Write-Host ''
Write-Host '=== summary ==='
foreach ($s in $script:Results) {
    $mark = 'PASS'
    if (-not $s.ok) { $mark = 'FAIL' }
    Write-Host ('  ' + $mark + '  ' + $s.step)
}
Write-Host ('=== ' + $script:Failures.Count + ' failure(s) of ' + $script:Results.Count + ' ===')
foreach ($f in $script:Failures) { Write-Host ('  ' + $f) }
Write-Host ('=== logs: ' + $script:LogDir + ' ===')
exit $script:Failures.Count
