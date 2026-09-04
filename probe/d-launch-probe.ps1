# probe/d-launch-probe.ps1 -- unattended UI Automation probe for the convoy D launcher (acceptance D-7).
#
# ASCII only. CRLF. Windows PowerShell 5.1 and PowerShell 7+.
#
# What it drives, through the real window, in order (the "six operations" of acceptance D-7):
#   1  start the launcher in developer mode with a private data directory and a private port
#   2  settings tab: enumerate GPUs, pick one, apply     (D-2: the UUID is what gets saved)
#   3  start the server and wait for Ready               (D-6: <= 120 s)
#   4  read the log band                                 (D-4: banner / uvicorn / runtime loaded / device actual)
#   5  try tab: synthesize one short line and play it
#   6  voices tab: add one reference wav under a Japanese name, then synthesize with it (D-3)
#   7  stop the server and prove no python child survived
#
# It never touches 8088 / 7861 / 18088, never writes to %LOCALAPPDATA%\irodori-tts-ywk, and always
# tree-kills what it started (even on failure -- the teardown is in a finally block).
#
# Usage:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File probe/d-launch-probe.ps1
#   pwsh ... -File probe/d-launch-probe.ps1 -Port 18096 -Variant cpu -ReadyTimeoutSeconds 300
#   pwsh ... -File probe/d-launch-probe.ps1 -BadDeviceOnly     (D-1 only: cuda:9 must fail in <= 15 s)
#
# Exit code 0 = every step passed. Non-zero = the number of failed steps.

[CmdletBinding()]
param(
    [int]$Port = 18094,
    [string]$Variant = 'rocm-gfx1151',
    [string]$Exe = '',
    [string]$AppDir = '',
    [string]$RuntimeRoot = '',
    [string]$DataDir = '',
    [string]$SourceWav = '',
    [string]$HfHome = '',
    [int]$ReadyTimeoutSeconds = 120,
    [switch]$SkipSynthesis,
    [switch]$BadDeviceOnly,
    [switch]$KeepDataDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

$repo = Get-YwkProbeRepoRoot
if ([string]::IsNullOrEmpty($Exe)) {
    $Exe = Join-Path $repo 'launcher\IrodoriTtsYwk.Launcher\bin\Debug\net10.0-windows\IrodoriTtsYwk.Launcher.exe'
}
if ([string]::IsNullOrEmpty($AppDir))      { $AppDir = Join-Path $repo 'build\out\app' }
if ([string]::IsNullOrEmpty($RuntimeRoot)) { $RuntimeRoot = Join-Path $repo 'build\out' }
if ([string]::IsNullOrEmpty($DataDir))     { $DataDir = Join-Path $env:TEMP 'ywk-d-launch-probe' }
if ([string]::IsNullOrEmpty($SourceWav))   { $SourceWav = Join-Path $repo 'voices\presets\vr2_tsukuyomi_ai.wav' }
# The private data directory holds no models. Point HF_HOME at the cache the machine already has so
# the probe loads the runtime instead of trying to fetch 3.3 GiB (HF_HUB_OFFLINE=1 keeps it read only).
if ([string]::IsNullOrEmpty($HfHome))      { $HfHome = Join-Path $env:USERPROFILE '.cache\huggingface' }

$outDir = Join-Path $repo 'build\out\probe-log'
$null = New-Item -ItemType Directory -Path $outDir -Force

# Japanese literals are built from code points so this file stays ASCII (see common.ps1 New-JpText).
$T = [ordered]@{
    Ready      = New-JpText 0x5F85, 0x6A5F                     # "waiting" = ServerState.Ready
    Stopped    = New-JpText 0x505C, 0x6B62                     # "stopped"
    Failed     = New-JpText 0x5931, 0x6557                     # "failed"
    Starting   = New-JpText 0x8D77, 0x52D5, 0x4E2D             # "starting"
    Loading    = New-JpText 0x8AAD, 0x8FBC, 0x4E2D             # "loading"
    Warming    = New-JpText 0x6696, 0x6A5F, 0x4E2D             # "warming"
    DefVoice   = New-JpText 0x30C7, 0x30D5, 0x30A9, 0x30EB, 0x30C8   # the always-present no_ref voice
    NewVoice   = New-JpText 0x30C6, 0x30B9, 0x30C8, 0x8A71, 0x8005   # the voice this probe adds
    Sentence   = New-JpText 0x3042, 0x3044, 0x3046, 0x3002           # a very short line to synthesize
    Caption    = New-JpText 0x843D, 0x3061, 0x7740, 0x3044, 0x3066   # default caption for the new voice
}

$script:Failures = @()
$script:Steps = @()

function Invoke-Shot {
    <#
      .SYNOPSIS
        Press "synthesize and play" and wait until the result line CHANGES.
        The result line keeps the previous shot until the new one lands, so a probe that only waits
        for "non empty" reads the previous shot and calls a still running synthesis a success.
    #>
    param($Window, [int]$TimeoutSeconds = 150)
    $before = Get-TextById -Root $Window -Id 'TryResultText'
    if ($null -eq $before) { $before = '' }
    $null = Invoke-ButtonById -Root $Window -Id 'TrySynthesizeButton'
    $start = Get-Date
    $deadline = $start.AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $now = Get-TextById -Root $Window -Id 'TryResultText' -TimeoutSeconds 2
        if ($null -eq $now) { $now = '' }
        if (($now -ne $before) -and (-not [string]::IsNullOrWhiteSpace($now))) {
            $msg = Get-TextById -Root $Window -Id 'TryMessageText' -TimeoutSeconds 2
            return [pscustomobject]@{
                ok = $true; result = $now; message = $msg
                elapsed = ((Get-Date) - $start).TotalSeconds
            }
        }
    }
    return [pscustomobject]@{
        ok = $false
        result = (Get-TextById -Root $Window -Id 'TryResultText')
        message = (Get-TextById -Root $Window -Id 'TryMessageText')
        elapsed = ((Get-Date) - $start).TotalSeconds
    }
}

function Add-Step {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    $mark = 'PASS'
    if (-not $Ok) {
        $mark = 'FAIL'
        $script:Failures += ($Name + ': ' + $Detail)
    }
    $script:Steps += [pscustomobject]@{ step = $Name; ok = $Ok; detail = $Detail }
    Write-Host ('[' + $mark + '] ' + $Name + '  ' + $Detail)
}

# ================================================================= D-1 (no window needed)
#
# Acceptance D-1: an out-of-range GPU must be reported in <= 15 s with a one line reason.
# The UI cannot produce cuda:9 (the settings screen only offers GPUs that were enumerated), so this
# step talks to the wrapper the way ServerProcess does -- same exe, same module, same env -- and
# checks the wrapper's own pre-flight: exit code 2 and one line on stderr.
function Invoke-BadDeviceProbe {
    param([string]$Variant, [int]$Port, [string]$DataDir)

    $runtimeDir = Join-Path $RuntimeRoot ('runtime-' + $Variant)
    if (-not (Test-Path -LiteralPath $runtimeDir)) {
        $runtimeDir = Join-Path $RuntimeRoot $Variant
    }
    $python = Join-Path $runtimeDir 'python.exe'
    if (-not (Test-Path -LiteralPath $python)) {
        Add-Step 'D-1 out-of-range GPU' $false ('python.exe not found under ' + $RuntimeRoot)
        return
    }

    $serverDir = Join-Path $AppDir 'server'
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $python
    $psi.WorkingDirectory = $serverDir
    $null = $psi.ArgumentList.Add('-m')
    $null = $psi.ArgumentList.Add('ywk_server')
    $null = $psi.ArgumentList.Add('--host')
    $null = $psi.ArgumentList.Add('127.0.0.1')
    $null = $psi.ArgumentList.Add('--port')
    $null = $psi.ArgumentList.Add([string]$Port)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $psi.Environment['IRODORI_MODEL_DEVICE'] = 'cuda:9'
    $psi.Environment['IRODORI_CODEC_DEVICE'] = 'cuda:9'
    $psi.Environment['YWK_VARIANT'] = 'rocm-gfx1151'
    $psi.Environment['YWK_DATA_DIR'] = $DataDir
    $psi.Environment['HF_HOME'] = (Join-Path $DataDir 'models')
    $psi.Environment['HF_HUB_OFFLINE'] = '1'
    $psi.Environment['IRODORI_VOICES_DIR'] = (Join-Path $DataDir 'voices')
    $psi.Environment['IRODORI_VOICE_ALIASES_FILE'] = (Join-Path $DataDir 'voices\voices.json')
    $psi.Environment['PYTHONUNBUFFERED'] = '1'
    $psi.Environment['PYTHONUTF8'] = '1'
    $psi.Environment['PYTHONIOENCODING'] = 'utf-8'

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $exited = $proc.WaitForExit(20000)
    $sw.Stop()
    if (-not $exited) {
        try { $proc.Kill($true) } catch { }
        Add-Step 'D-1 out-of-range GPU' $false 'the wrapper did not exit within 20 s'
        return
    }
    $stderr = $stderrTask.Result
    $stdout = $stdoutTask.Result
    $code = $proc.ExitCode
    $lines = @($stderr -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $reason = ''
    if ($lines.Count -gt 0) { $reason = $lines[-1] }
    Write-Host ('[D-1] exit=' + $code + ' seconds=' + [math]::Round($sw.Elapsed.TotalSeconds, 2))
    foreach ($l in $lines) { Write-Host ('[D-1] stderr: ' + $l) }
    if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Host ('[D-1] stdout: ' + $stdout.Trim()) }

    $ok = ($code -eq 2) -and ($sw.Elapsed.TotalSeconds -le 15) -and ($lines.Count -ge 1)
    Add-Step 'D-1 out-of-range GPU' $ok ('exit=' + $code + ' in ' + [math]::Round($sw.Elapsed.TotalSeconds, 2) + ' s :: ' + $reason)
}

# ================================================================= preflight

Write-Host '=== preflight ==='
Write-Host ('repo        = ' + $repo)
Write-Host ('exe         = ' + $Exe)
Write-Host ('app dir     = ' + $AppDir)
Write-Host ('runtime     = ' + $RuntimeRoot)
Write-Host ('data dir    = ' + $DataDir)
Write-Host ('variant     = ' + $Variant + '   port = ' + $Port)

$busy = @(Assert-QuietPorts)
Add-Step 'reserved ports are quiet' ($busy.Count -eq 0) ('busy = ' + ($busy -join ', '))
Add-Step 'private port is free' (-not (Test-PortListening -Port $Port)) ('port ' + $Port)

if ($BadDeviceOnly) {
    $null = New-LauncherFixture -DataDir $DataDir -Variant $Variant -Port $Port -HfHome $HfHome
    Invoke-BadDeviceProbe -Variant $Variant -Port $Port -DataDir $DataDir
    if ((-not $KeepDataDir) -and (Test-Path -LiteralPath $DataDir)) {
        Remove-Item -LiteralPath $DataDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host ''
    Write-Host ('=== ' + $script:Failures.Count + ' failure(s) ===')
    foreach ($f in $script:Failures) { Write-Host ('  ' + $f) }
    exit $script:Failures.Count
}

Write-Host ('hf home     = ' + $HfHome)
$null = New-LauncherFixture -DataDir $DataDir -Variant $Variant -Port $Port -HfHome $HfHome

$proc = $null
try {
    # ============================================================= 1. window
    Write-Host ''
    Write-Host '=== 1. window ==='
    $proc = Start-Launcher -Exe $Exe -AppDir $AppDir -RuntimeRoot $RuntimeRoot -DataDir $DataDir
    $win = Get-MainWindow -Proc $proc -TimeoutSeconds 60
    Add-Step 'main window is up' ($null -ne $win) $win.Current.Name

    $state0 = Get-TextById -Root $win -Id 'MainStateText'
    $endpoint = Get-TextById -Root $win -Id 'MainEndpointText'
    $version = Get-TextById -Root $win -Id 'MainVersionText'
    Write-Host ('[ui] state=' + $state0 + '  endpoint=' + $endpoint + '  version=' + $version)
    Add-Step 'endpoint shows the private port' ($endpoint -like ('*:' + $Port)) $endpoint
    Add-Step 'state starts at stopped' ($state0 -eq $T.Stopped) $state0

    # ============================================================= 2. settings / GPU (D-2)
    Write-Host ''
    Write-Host '=== 2. settings and GPU enumeration (D-2) ==='
    $null = Select-Tab -Window $win -TabId 'TabSettings'
    $swGpu = [System.Diagnostics.Stopwatch]::StartNew()
    $null = Invoke-ButtonById -Root $win -Id 'SettingsRefreshGpuButton'
    $gpuNames = @()
    for ($i = 0; $i -lt 40; $i++) {
        $gpuNames = @(Get-ComboItemNames -Root $win -Id 'SettingsGpuCombo')
        if ($gpuNames.Count -gt 0) { break }
        Start-Sleep -Milliseconds 500
    }
    $swGpu.Stop()
    foreach ($n in $gpuNames) { Write-Host ('[gpu] ' + $n) }
    Add-Step 'GPU enumeration returns at least one device' ($gpuNames.Count -ge 1) (
        'count=' + $gpuNames.Count + ' in ' + [math]::Round($swGpu.Elapsed.TotalSeconds, 2) + ' s')
    Write-Host ('[gpu] driver = ' + (Get-TextById -Root $win -Id 'SettingsDriverText'))

    if ($gpuNames.Count -ge 1) {
        $picked = Select-ComboItemById -Root $win -Id 'SettingsGpuCombo' -ItemText $gpuNames[0]
        Write-Host ('[gpu] picked ' + $picked)
        $null = Invoke-ButtonById -Root $win -Id 'SettingsApplyButton'
        Start-Sleep -Milliseconds 800
        Write-Host ('[settings] ' + (Get-TextById -Root $win -Id 'SettingsMessageText'))

        # -Encoding UTF8 is not optional: Windows PowerShell 5.1 reads a BOM-less file with the
        # machine's ANSI code page, which turns every Japanese name in these files into mojibake
        # (this probe must pass on 5.1 and on 7).
        $saved = Get-Content -LiteralPath (Join-Path $DataDir 'settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $uuid = ''
        if ($saved.PSObject.Properties.Name -contains 'gpuUuid') { $uuid = [string]$saved.gpuUuid }
        Write-Host ('[settings] gpuUuid = ' + $uuid + '   port = ' + $saved.port)
        # D-2: what is persisted is the UUID, never the index.
        Add-Step 'the GPU is persisted as a UUID' (-not [string]::IsNullOrWhiteSpace($uuid)) ('gpuUuid=' + $uuid)
        Add-Step 'the private port survived apply' ([int]$saved.port -eq $Port) ('port=' + $saved.port)

        # The status band shows what the settings hold. If applying does not refresh it, the band
        # keeps saying "not chosen" right after the operator chose a GPU (found on this machine).
        $bandGpu = Get-TextById -Root $win -Id 'MainStateText' -TimeoutSeconds 2
        $null = Select-Tab -Window $win -TabId 'TabStatus'
        $bandGpu = Get-TextById -Root $win -Id 'StatusGpuText' -TimeoutSeconds 5
        Write-Host ('[status] gpu band = ' + $bandGpu)
        Add-Step 'the status band follows the applied GPU' (
            ($null -ne $bandGpu) -and ($bandGpu -match 'UUID')) ([string]$bandGpu)
        $null = Select-Tab -Window $win -TabId 'TabSettings'
    }

    # ============================================================= 2b. first run wizard (D-5 ports)
    #
    # The wizard is NOT run to completion here: finishing it means pulling 4.7 GB over the wire and
    # this run must not. What is checked is that the product path actually reaches the pieces the
    # wizard needs, which is what the correction seat found missing (2026-09-05):
    #   * the size line comes from FetchPlanner.Plan, so it names the free space the run needs.
    #     Before the fix the line summed two ledgers, hid 35 MiB of queued downloads and never
    #     mentioned free space at all (9.55 GiB on the Radeon build).
    #   * the consent box is only usable when licenses/first-run-notices.md was actually read
    #     (decisions 46): with no notices file there is no sha256 to record, so consenting would
    #     write null into acceptedNoticesSha256.
    Write-Host ''
    Write-Host '=== 2b. first run wizard: the size line and the consent gate (D-5) ==='
    $null = Select-Tab -Window $win -TabId 'TabStatus'
    $null = Invoke-ButtonById -Root $win -Id 'MainFirstRunButton'
    $wizard = Get-DialogWindow -Proc $proc -TimeoutSeconds 20
    if ($null -eq $wizard) {
        Add-Step 'the first run wizard opens' $false 'no dialog window appeared'
    } else {
        Add-Step 'the first run wizard opens' $true ([string]$wizard.Current.Name)

        $noticesPath = Join-Path $AppDir 'licenses\first-run-notices.md'
        $haveNotices = Test-Path -LiteralPath $noticesPath
        $accept = Find-ById -Root $wizard -Id 'FirstRunAcceptCheck' -TimeoutSeconds 10
        $acceptEnabled = $false
        if ($null -ne $accept) { $acceptEnabled = [bool]$accept.Current.IsEnabled }
        Write-Host ('[wizard] notices file = ' + $haveNotices + '   accept enabled = ' + $acceptEnabled)
        Add-Step 'consent is possible only when the notices file was read' (
            $acceptEnabled -eq $haveNotices) (
            'notices=' + $haveNotices + ' enabled=' + $acceptEnabled)

        if ($acceptEnabled) {
            $null = Set-ToggleById -Root $wizard -Id 'FirstRunAcceptCheck' -On $true
            $null = Invoke-ButtonById -Root $wizard -Id 'FirstRunNextButton'
            Start-Sleep -Milliseconds 800

            $sizeText = Get-TextById -Root $wizard -Id 'FirstRunSizeText' -TimeoutSeconds 10
            Write-Host ('[wizard] size line = ' + $sizeText)
            # New-JpText 0x5FC5,0x8981,0x306A,0x7A7A,0x304D = "the free space it needs"
            $freeSpaceLabel = New-JpText 0x5FC5, 0x8981, 0x306A, 0x7A7A, 0x304D
            Add-Step 'the size line names the free space the run needs' (
                ($null -ne $sizeText) -and ($sizeText -like ('*' + $freeSpaceLabel + '*'))) (
                [string]$sizeText)
        }

        # Close without starting a download (this run pulls nothing).
        try {
            $wp = $wizard.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
            $wp.Close()
        } catch {
            Write-Host ('[wizard] close failed: ' + $_.Exception.Message)
        }
        Start-Sleep -Milliseconds 500
        $stillOpen = Get-DialogWindow -Proc $proc -TimeoutSeconds 2
        Add-Step 'the wizard closes without pulling anything' ($null -eq $stillOpen) (
            'dialog gone = ' + ($null -eq $stillOpen))
    }

    # ============================================================= 3. start (D-6)
    Write-Host ''
    Write-Host '=== 3. start the server (D-6) ==='
    $null = Select-Tab -Window $win -TabId 'TabStatus'
    $null = Invoke-ButtonById -Root $win -Id 'MainStartButton'

    # Watch the state AND keep every log line that passes through the 20 line tail. The tail is a
    # window, not a transcript: on this machine the upstream emits about 20 lines of MIOpen / pydub /
    # torch warnings after the banner, so by the time Ready is reached the banner has scrolled out.
    # A probe that read the tail once would call the banner missing when the operator did see it.
    $seenLog = New-Object System.Collections.Generic.HashSet[string]
    $startWait = Get-Date
    $ready = $null
    while ($true) {
        $tail = Get-TextById -Root $win -Id 'StatusLogBox' -TimeoutSeconds 2
        if ($null -ne $tail) {
            foreach ($line in @($tail -split "`r?`n")) {
                if (-not [string]::IsNullOrWhiteSpace($line)) { $null = $seenLog.Add($line) }
            }
        }
        $state = Get-TextById -Root $win -Id 'MainStateText' -TimeoutSeconds 2
        if ($null -eq $state) { $state = '' }
        $spent = ((Get-Date) - $startWait).TotalSeconds
        if ($state -match ($T.Ready + '|' + $T.Warming)) {
            $ready = [pscustomobject]@{ ok = $true; elapsed = $spent; text = $state; failed = $false }
            break
        }
        if ($state -match $T.Failed) {
            $ready = [pscustomobject]@{ ok = $false; elapsed = $spent; text = $state; failed = $true }
            break
        }
        if ($spent -ge $ReadyTimeoutSeconds) {
            $ready = [pscustomobject]@{ ok = $false; elapsed = $spent; text = $state; failed = $false }
            break
        }
        Start-Sleep -Milliseconds 400
    }
    Write-Host ('[start] state=' + $ready.text + ' after ' + [math]::Round($ready.elapsed, 1) + ' s')
    if (-not $ready.ok) {
        Write-Host ('[start] reason = ' + (Get-TextById -Root $win -Id 'StatusReasonText'))
    }
    Add-Step 'the server reaches Ready inside the deadline' $ready.ok (
        'ready in ' + [math]::Round($ready.elapsed, 1) + ' s (limit ' + $ReadyTimeoutSeconds + ' s), state=' + $ready.text)
    Add-Step 'the private port is listening' (Test-PortListening -Port $Port) ('port ' + $Port)

    Start-Sleep -Seconds 3
    Write-Host ('[status] device  = ' + (Get-TextById -Root $win -Id 'StatusDeviceText'))
    Write-Host ('[status] gpu     = ' + (Get-TextById -Root $win -Id 'StatusGpuText'))
    Write-Host ('[status] variant = ' + (Get-TextById -Root $win -Id 'StatusVariantText'))
    Write-Host ('[status] memory  = ' + (Get-TextById -Root $win -Id 'StatusMemoryText'))
    Write-Host ('[status] warmup  = ' + (Get-TextById -Root $win -Id 'StatusWarmupText'))

    # ============================================================= 4. log lines (D-4)
    Write-Host ''
    Write-Host '=== 4. log band (D-4) ==='
    $tail = Get-TextById -Root $win -Id 'StatusLogBox'
    if ($null -eq $tail) { $tail = '' }
    foreach ($line in @($tail -split "`r?`n")) {
        if (-not [string]::IsNullOrWhiteSpace($line)) { $null = $seenLog.Add($line) }
    }
    $log = ($seenLog -join "`n")
    Write-Host ('[log] ' + $seenLog.Count + ' distinct lines passed through the tail while starting')
    foreach ($line in @($tail -split "`r?`n")) { Write-Host ('[tail] ' + $line) }
    # D-4 also demands that a 422 body echo never reaches the log band.
    Add-Step 'no request body echo in the log' (-not ($log -match '"input"\s*:')) 'no 422 echo'
    # And the launcher must not fill its own log with its 2 second /ywk/status watch.
    $noise = @($seenLog | Where-Object { $_ -match '"GET /ywk/status' })
    Add-Step 'the status watch does not flood the log' ($noise.Count -eq 0) ('access log lines = ' + $noise.Count)
    Add-Step 'log has the wrapper banner'    ($log -match 'ywk_server \d') 'ywk_server <version> upstream=...'
    Add-Step 'log has the uvicorn line'      ($log -match 'Uvicorn running on') 'Uvicorn running on ...'
    Add-Step 'log has the runtime loaded line' ($log -match 'runtime loaded in') 'runtime loaded in ...'
    Add-Step 'log has the device actual line'  ($log -match 'device actual=') 'ywk_server: device actual=...'

    if (-not $SkipSynthesis) {
        # ========================================================= 5. one shot with the default voice
        Write-Host ''
        Write-Host '=== 5. try tab, default voice ==='
        $null = Select-Tab -Window $win -TabId 'TabTry'
        $null = Set-TextById -Root $win -Id 'TryInputBox' -Text $T.Sentence
        $null = Invoke-ButtonById -Root $win -Id 'TryStepsPreset10'
        $voices0 = @(Get-ComboItemNames -Root $win -Id 'TryVoiceCombo')
        Write-Host ('[try] voices = ' + ($voices0 -join ' | '))
        Add-Step 'the default voice is always offered' ($voices0 -contains $T.DefVoice) ($voices0 -join ' | ')
        $null = Select-ComboItemById -Root $win -Id 'TryVoiceCombo' -ItemText $T.DefVoice

        $shot1 = Invoke-Shot -Window $win -TimeoutSeconds 120
        Write-Host ('[try] result  = ' + $shot1.result)
        Write-Host ('[try] message = ' + $shot1.message)
        Add-Step 'one shot with the default voice returns audio' $shot1.ok (
            [string]$shot1.result + ' / ' + [string]$shot1.message)

        # ========================================================= 6. add a voice, shoot with it (D-3)
        Write-Host ''
        Write-Host '=== 6. voices tab, add one reference wav (D-3) ==='
        $null = Select-Tab -Window $win -TabId 'TabVoices'
        $before = @(Get-GridRows -Root $win -Id 'VoicesGrid')
        Write-Host ('[voices] rows before = ' + $before.Count)

        if (-not (Test-Path -LiteralPath $SourceWav)) {
            Add-Step 'a preset wav is available to add' $false $SourceWav
        } else {
            # Operation 1 of 2: choose the wav.
            #
            # The screen offers two ways to do this and the probe takes the typed one. The common
            # file dialog that the "choose..." button opens is created (Win32 EnumWindows sees the
            # #32770 window) but it is NOT reachable through UI Automation from an unattended
            # session -- RootElement's children never list it -- so a probe that opened it could
            # never close it and would hang the run. Pasting the path into the box is the same two
            # operations for the user and is the path this probe can prove end to end.
            $null = Set-TextById -Root $win -Id 'VoicesSourcePathBox' -Text (Resolve-Path -LiteralPath $SourceWav).Path
            # operation 2 of 2: name it (Japanese)
            $null = Set-TextById -Root $win -Id 'VoicesNewNameBox' -Text $T.NewVoice
            $null = Set-TextById -Root $win -Id 'VoicesNewCaptionBox' -Text $T.Caption
            $null = Invoke-ButtonById -Root $win -Id 'VoicesAddButton'
            Start-Sleep -Seconds 2
            Write-Host ('[voices] message = ' + (Get-TextById -Root $win -Id 'VoicesMessageText'))

            $after = @(Get-GridRows -Root $win -Id 'VoicesGrid')
            foreach ($row in $after) { Write-Host ('[voices] row: ' + ($row.cells -join ' | ')) }
            $named = @($after | Where-Object { $_.cells -contains $T.NewVoice })
            Add-Step 'the new voice shows up in the list without a restart' ($named.Count -eq 1) (
                'rows ' + $before.Count + ' -> ' + $after.Count)

            $aliases = Join-Path $DataDir 'voices\voices.json'
            $aliasText = ''
            if (Test-Path -LiteralPath $aliases) { $aliasText = Get-Content -LiteralPath $aliases -Raw -Encoding UTF8 }
            Add-Step 'voices.json carries the Japanese display name' ($aliasText -match [regex]::Escape($T.NewVoice)) $aliases
            Add-Step 'voices.json keeps the no_ref default voice' ($aliasText -match 'no_ref') 'no_ref'

            # ===================================================== 7. one shot with the new voice
            Write-Host ''
            Write-Host '=== 7. try tab, the voice we just added ==='
            $null = Select-Tab -Window $win -TabId 'TabTry'
            $voices1 = @(Get-ComboItemNames -Root $win -Id 'TryVoiceCombo')
            Write-Host ('[try] voices = ' + ($voices1 -join ' | '))
            if ($voices1 -contains $T.NewVoice) {
                $null = Select-ComboItemById -Root $win -Id 'TryVoiceCombo' -ItemText $T.NewVoice
                $null = Set-TextById -Root $win -Id 'TryInputBox' -Text $T.Sentence
                # The first shot against an unseen reference shape pays decode_latent once, so allow
                # more time here than for the no_ref shot (convoy C measured up to about 15 s on ROCm).
                $shot2 = Invoke-Shot -Window $win -TimeoutSeconds 180
                Write-Host ('[try] result  = ' + $shot2.result)
                Write-Host ('[try] message = ' + $shot2.message)
                Add-Step 'one shot with the Japanese named voice' $shot2.ok (
                    [string]$shot2.result + ' / ' + [string]$shot2.message)
            } else {
                Add-Step 'the new voice is offered in the try tab' $false ($voices1 -join ' | ')
            }
        }
    }

    # ============================================================= 8. dump and stop
    Write-Host ''
    Write-Host '=== 8. stop ==='
    $null = Select-Tab -Window $win -TabId 'TabStatus'
    $null = Write-UiaTree -Root $win -OutFile (Join-Path $outDir 'd-launch-probe-tree.txt')

    $null = Invoke-ButtonById -Root $win -Id 'MainStopButton'
    $stopped = Wait-ForTextById -Root $win -Id 'MainStateText' -Pattern $T.Stopped -TimeoutSeconds 30
    Write-Host ('[stop] state=' + $stopped.text + ' after ' + [math]::Round($stopped.elapsed, 1) + ' s')
    Add-Step 'the server stops on request' $stopped.ok ('stopped in ' + [math]::Round($stopped.elapsed, 1) + ' s')
    Add-Step 'the private port is released' (-not (Test-PortListening -Port $Port)) ('port ' + $Port)
} finally {
    Write-Host ''
    Write-Host '=== teardown ==='
    $strays = @()
    if ($null -ne $proc) {
        $strays = @(Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*'))
    }
    foreach ($s in $strays) { Write-Host ('[teardown] stray: ' + $s.ProcessId + ' ' + $s.CommandLine) }
    Add-Step 'no wrapper process survived' ($strays.Count -eq 0) ('strays=' + $strays.Count)
    $busyAfter = @(Assert-QuietPorts)
    Add-Step 'reserved ports are still quiet' ($busyAfter.Count -eq 0) ('busy = ' + ($busyAfter -join ', '))
    if ((-not $KeepDataDir) -and (Test-Path -LiteralPath $DataDir)) {
        Remove-Item -LiteralPath $DataDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host ('[teardown] removed ' + $DataDir)
    }
}

Write-Host ''
Write-Host '=== summary ==='
foreach ($s in $script:Steps) {
    $mark = 'PASS'
    if (-not $s.ok) { $mark = 'FAIL' }
    Write-Host ('  ' + $mark + '  ' + $s.step)
}
Write-Host ('=== ' + $script:Failures.Count + ' failure(s) of ' + $script:Steps.Count + ' ===')
foreach ($f in $script:Failures) { Write-Host ('  ' + $f) }
exit $script:Failures.Count
