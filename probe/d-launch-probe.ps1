# probe/d-launch-probe.ps1 -- unattended UI Automation probe for the convoy D launcher (acceptance D-7).
#
# ASCII only. CRLF. Windows PowerShell 5.1 and PowerShell 7+.
#
# What it drives, through the real window, in order (the "six operations" of acceptance D-7):
#   1  start the launcher in developer mode with a private data directory and a private port
#   2  settings tab: enumerate GPUs, pick one, apply     (D-2: the UUID is what gets saved)
#   3  start the server and wait for Ready               (D-6: <= 120 s)
#      PORT FIRST (decisions 105): the wrapper binds and loads the model in the BACKGROUND, so
#      /health must answer 200 within 10 s of the press while /ywk/status.runtime.loaded is still
#      false, and loaded turns true later (Radeon 35-70 s, 3090 20-28 s). Both are measured.
#   4  read the log band                                 (D-4: banner / uvicorn / runtime loaded / device actual)
#   5  try tab: synthesize one short line and play it
#   6  voices tab: add one reference wav under a Japanese name, then synthesize with it (D-3)
#   7  stop the server and prove no python child survived
#
# Round two (convoy D (2), decisions 87 / 88) adds, in the same window:
#   a  the memory band carries numbers          (decisions 87 (1) / 67 (3): allocated / reserved /
#                                                gpu_used / gpu_total, never a bare 0 for a null)
#   b  the latent cache line says ON/OFF and how many voices are baked   (decisions 67 (1))
#   c  one voice's estimate flips from the wav coefficient to the real .pt size (decisions 67 (2))
#   d  THE GATE: a GPU variant that cannot see a GPU is refused before any child is started, with a
#      one line reason and the variant to switch to      (decisions 88 (1)(2), 80, 83)
#   e  LIVENESS: killing the wrapper from outside turns the band to "failed" with a reason in <= 3 s,
#      and a kill during a synthesis is told at once instead of waiting for the HTTP deadline
#                                                        (decisions 88 (3))
#   f  the remove button's blocked reason is readable through UI Automation   (low 14)
#   g  the 11 preset voices arrive from the app tree      (decisions 88 (5), 17)
#
# Round three (convoy D (3), decisions 92 "low left for D (3)" / 94 (1)) adds five more:
#   h  the first run wizard is counted in PRESSES (decisions 94 (1): 9 today, 5 once every
#      step that succeeded walks on by itself) and a FAILED step is proved to stop it
#   i  the download cache is still there when the first shot is fired and GONE once that shot
#      came back 200 -- read in BYTES               (decisions 90 Q-E2 (3), 94 (3))
#   j  settings.json carries runtimeLedgers.<variant> / installedAppVersions.<variant> (a table
#      PER VARIANT since the correction seat of convoy D (3) round three -- one pair of scalars for
#      the whole file made switching variants raise a false "rebuild the runtime"), and a ledger
#      that no longer matches makes the window offer to rebuild the runtime   (decisions 91)
#   k  a runtime whose python.exe cannot be started is told in <= 10 s with a one line reason
#      (design 20-5 (1): the window used to sit there for 60 s saying nothing at all)
#   l  the "delete the download cache" button really empties it      (decisions 90 Q-E2 (3))
#
# h / j / k / l each need their own window (settings are read when the launcher composes them),
# load no model, open no port and fetch NOTHING: -RoundThreeOnly runs just those four.
#
# It never touches 8088 / 7861 / 18088, never writes to %LOCALAPPDATA%\irodori-tts-ywk, and always
# tree-kills what it started (even on failure -- the teardown is in a finally block).
#
# Usage:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File probe/d-launch-probe.ps1
#   pwsh ... -File probe/d-launch-probe.ps1 -Port 18096 -Variant cpu -ReadyTimeoutSeconds 300
#   pwsh ... -File probe/d-launch-probe.ps1 -BadDeviceOnly     (D-1 only: cuda:9 must be told in <= 15 s)
#   pwsh ... -File probe/d-launch-probe.ps1 -GateOnly          (the variant gate only, no model load)
#   pwsh ... -File probe/d-launch-probe.ps1 -RoundThreeOnly    (h/j/k/l only: no model, no server)
#   pwsh ... -File probe/d-launch-probe.ps1 -DryRun            (print the plan, touch nothing)
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
    # The gate run (step d) needs its own port, data directory and runtime root: the variant it
    # starts with is NOT the one this machine can run, and the window must be a fresh instance
    # because the variant is read when the launcher composes its settings.
    [int]$GatePort = 18099,
    [string]$GateVariant = 'cu130',
    [switch]$SkipSynthesis,
    [switch]$SkipGate,
    [switch]$SkipLiveness,
    [switch]$BadDeviceOnly,
    [switch]$GateOnly,
    [switch]$KeepDataDir,
    # ---- round three (decisions 92 / 94) ----
    # Each of these three runs alone and none of them ever listens, so 18097 is shared; the bad
    # python run is the only one that presses "start the server", so it gets a port of its own.
    [int]$WizardPort = 18097,
    [int]$LedgerPort = 18097,
    [int]$CachePort = 18097,
    [int]$BadPythonPort = 18098,
    # design 20-5 (1): the window was silent for 60 s. The budget the seat asked for is 10 s.
    [double]$BadPythonBudgetSeconds = 10,
    [switch]$SkipRoundThree,
    [switch]$RoundThreeOnly,
    # E2E SEAT ONLY: -WizardFullRun really fetches (4.7 GB over the wire). Without it step h
    # runs offline against a ledger that cannot be planned from, and counts the presses up to
    # the failure instead of up to the end.
    [switch]$WizardFullRun,
    [int]$WizardFullRunTimeoutSeconds = 1800,
    [switch]$DryRun
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

    # ---- round two (decisions 87 / 88) ----
    Used       = New-JpText 0x4F7F, 0x7528, 0x91CF                   # "used"      = memory.allocated
    Reserved   = New-JpText 0x5360, 0x6709, 0x91CF                   # "reserved"  = memory.reserved
    Whole      = New-JpText 0x5168, 0x4F53                           # "whole"     = gpu_used/gpu_total
    Baked      = New-JpText 0x713C, 0x3044, 0x305F, 0x8A71, 0x8005   # "baked voices"
    LatentRef  = New-JpText 0x6F5C, 0x5728, 0x53C2, 0x7167           # "latent reference"
    Measured   = New-JpText 0x5B9F, 0x6E2C                           # "measured"
    Provision  = New-JpText 0x5B9F, 0x6E2C, 0x524D, 0x306E, 0x6982, 0x7B97  # "estimate, not measured"
    PerVoice   = New-JpText 0x540D, 0x3042, 0x305F, 0x308A           # "per voice"
    Preset     = New-JpText 0x30D7, 0x30EA, 0x30BB, 0x30C3, 0x30C8   # "preset" (the kind column)
    ServerDown = New-JpText 0x30B5, 0x30FC, 0x30D0, 0x304C, 0x843D, 0x3061, 0x307E, 0x3057, 0x305F
    CannotSee  = New-JpText 0x3092, 0x898B, 0x3089, 0x308C, 0x307E, 0x305B, 0x3093  # "cannot see"
    Unsupported = New-JpText 0x672A, 0x5BFE, 0x5FDC                  # "not supported"
    Dash       = New-JpText 0x2014                                   # the em dash used for "unknown"
    # A line long enough that a 40 step shot is still running when the kill lands (e-2).
    LongLine   = (New-JpText 0x3053, 0x3093, 0x306B, 0x3061, 0x306F, 0x3002) * 20

    # ---- round three (decisions 92 / 94) ----
    Estimate   = New-JpText 0x6982, 0x7B97                           # "estimate" (the word itself)
    Ledger     = New-JpText 0x53F0, 0x5E33                           # "ledger"
    Fetch      = New-JpText 0x53D6, 0x5F97                           # "fetch"  (the word alone)
    # The FULL titles of the two fetch steps. "fetch" alone is a substring of BOTH of them
    # ("fetch (runtime)" is step 3, "fetch (models)" is step 5), so a -like '*fetch*' test passes
    # even when the wizard walked two pages past the step that failed. Match the whole title.
    FetchRun   = New-JpText 0x53D6, 0x5F97, 0xFF08, 0x5B9F, 0x884C, 0x7CFB, 0xFF09   # "fetch (runtime)"
    FetchModel = New-JpText 0x53D6, 0x5F97, 0xFF08, 0x30E2, 0x30C7, 0x30EB, 0xFF09   # "fetch (models)"
    Finished   = New-JpText 0x5B8C, 0x4E86                           # "done"   (wizard step 7 title)
    ToTry      = New-JpText 0x8A66, 0x3057, 0x6483, 0x3061, 0x3078   # "to the try screen" (last press)
    # "rebuild the runtime" -- the one move decisions 91 asks for when the ledger stops matching.
    Rebuild    = New-JpText 0x5B9F, 0x884C, 0x7CFB, 0x3092, 0x7D44, 0x307F, 0x76F4, 0x3059
    # "delete the download cache" -- the button of decisions 90 Q-E2 (3).
    ClearCache = New-JpText 0x53D6, 0x5F97, 0x30AD, 0x30E3, 0x30C3, 0x30B7, 0x30E5, 0x3092, 0x6D88, 0x3059
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
#
# PORT FIRST (decisions 105) does NOT move this one: the device check runs before the upstream
# package is even imported (ywk_server.resolve_devices_and_precision), i.e. before uvicorn binds,
# so cuda:9 still exits 2 in milliseconds and the port is never opened. What DID change is the
# other half of the acceptance row: a failure the pre-flight cannot see (a missing checkpoint, a
# device the driver refuses only at load time) no longer kills the process. It binds, answers
# /health 200, and puts the reason in /ywk/status.runtime.error, where the launcher reads it into
# the state band. The launcher does NOT kill such a child (decisions 105 (1), design seat's
# correction of convoy G): whether the error arrives while StartAsync is still waiting for ready
# or on the post-Ready watch, the child stays bound and answering so the main app's scan can read
# the reason; only the stop button (enabled in Failed) closes the port. "Stop it before you are
# told to" (correction 2026-09-05) is now limited to children that do not answer (timeout, bind
# failure read from the log). This step starts the wrapper DIRECTLY (no launcher), so nobody
# kills it here either: it stays up until this function does. Both endings are accepted here,
# and the one line reason is required either way.
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

    # POLL, do not block (correction seat, convoy G). The acceptance row is "the reason is told
    # inside 15 s", and the clock has to stop AT the reason -- a blocking WaitForExit(15000) on the
    # still-alive branch only returns after the whole 15 000 ms, so the <= 15 s check could never
    # pass for the ending decisions 105 introduced. Ask both questions every 500 ms and break on
    # whichever comes first: the pre-flight's exit, or a non-empty /ywk/status.runtime.error.
    $exited = $false
    $statusError = ''
    $portOpen = $false
    while ($sw.Elapsed.TotalSeconds -le 15) {
        if ($proc.HasExited) { $exited = $true; break }
        if (Test-PortListening -Port $Port) {
            $portOpen = $true
            try {
                $statusError = [string](Invoke-RestMethod `
                        -Uri ('http://127.0.0.1:' + $Port + '/ywk/status') -TimeoutSec 2).runtime.error
            } catch {
                $statusError = ''
            }
            if (-not [string]::IsNullOrWhiteSpace($statusError)) { break }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $exited) { $exited = $proc.HasExited }
    $sw.Stop()

    if (-not $exited) {
        $portOpen = Test-PortListening -Port $Port
        Write-Host ('[D-1] still running: port open = ' + $portOpen +
            ' runtime.error = ' + $statusError)
        try { $proc.Kill($true) } catch { }
        $null = $proc.WaitForExit(5000)
    }

    $stderr = $stderrTask.Result
    $stdout = $stdoutTask.Result
    $code = $null
    try { $code = $proc.ExitCode } catch { $code = $null }
    $lines = @($stderr -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $reason = ''
    if (-not [string]::IsNullOrWhiteSpace($statusError)) { $reason = $statusError }
    elseif ($lines.Count -gt 0) { $reason = $lines[-1] }
    Write-Host ('[D-1] exit=' + $code + ' seconds=' + [math]::Round($sw.Elapsed.TotalSeconds, 2))
    foreach ($l in $lines) { Write-Host ('[D-1] stderr: ' + $l) }
    if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Host ('[D-1] stdout: ' + $stdout.Trim()) }

    # Two endings are acceptable (decisions 105):
    #   pre-flight  -- exit 2, one line on stderr, the port never opened (this is cuda:9's path);
    #   load time   -- still alive and bound, with the reason in /ywk/status.runtime.error.
    # Either way the reason has to be there inside 15 s, and $sw was stopped AT the reason (the
    # poll above breaks on it), not at the end of the budget.
    $told = (-not [string]::IsNullOrWhiteSpace($reason)) -and ($sw.Elapsed.TotalSeconds -le 15)
    $ok = $told -and (($exited -and ($code -eq 2)) -or ((-not $exited) -and ($statusError -ne '')))
    Add-Step 'D-1 out-of-range GPU' $ok (
        'exit=' + $code + ' alive=' + (-not $exited) + ' port=' + $portOpen +
        ' in ' + [math]::Round($sw.Elapsed.TotalSeconds, 2) + ' s :: ' + $reason)
}

# ================================================================= round two helpers

function Get-WrapperPids {
    <#
      .SYNOPSIS
        pids of the wrapper children this run owns (python.exe -m ywk_server --port <this port>).
      .DESCRIPTION
        The liveness step (decisions 88 (3)) has to kill the child FROM OUTSIDE the launcher, the
        way a driver reset or an access violation does. Matching on the port as well as the module
        keeps the probe from touching another seat's wrapper on the same machine.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][int]$Port)
    $hits = @()
    foreach ($p in @(Get-StrayPython -Pattern @('*ywk_server*'))) {
        $cmd = $p.CommandLine
        if ([string]::IsNullOrEmpty($cmd)) { continue }
        if ($cmd -like ('*--port ' + $Port + '*')) { $hits += $p }
    }
    return $hits
}

function Get-HelpTextById {
    <#
      .SYNOPSIS
        AutomationProperties.HelpText of an element (low 14: the reason a button cannot be pressed
        must be readable without looking at the screen).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [int]$TimeoutSeconds = 10
    )
    $el = Find-ById -Root $Root -Id $Id -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $el) { return $null }
    return $el.Current.HelpText
}

function Wait-ForPattern {
    <#
      .SYNOPSIS
        Poll one element until its text matches, sampling fast (the liveness budget is 3 seconds).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [double]$TimeoutSeconds = 10,
        [int]$IntervalMilliseconds = 150
    )
    $start = Get-Date
    $deadline = $start.AddSeconds($TimeoutSeconds)
    $last = ''
    while ($true) {
        $text = Get-TextById -Root $Root -Id $Id -TimeoutSeconds 2
        if ($null -ne $text) { $last = $text }
        if ($last -match $Pattern) {
            return [pscustomobject]@{ ok = $true; elapsed = ((Get-Date) - $start).TotalSeconds; text = $last }
        }
        if ((Get-Date) -ge $deadline) {
            return [pscustomobject]@{ ok = $false; elapsed = ((Get-Date) - $start).TotalSeconds; text = $last }
        }
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
}

function Start-AndWaitReady {
    <#
      .SYNOPSIS
        Press "start the server" and wait for the band to say waiting (or warming).
      .DESCRIPTION
        Used by the liveness steps, which have to bring the server back after they killed it. The
        first start is measured inline (it also collects the log tail for D-4); this one only needs
        the verdict.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Window,
        [int]$TimeoutSeconds = 120
    )
    $null = Invoke-ButtonById -Root $Window -Id 'MainStartButton'
    $ready = Wait-ForPattern -Root $Window -Id 'MainStateText' `
        -Pattern ($T.Ready + '|' + $T.Warming) -TimeoutSeconds $TimeoutSeconds -IntervalMilliseconds 400
    Write-Host ('[restart] state=' + $ready.text + ' after ' + [math]::Round($ready.elapsed, 1) + ' s')
    return $ready
}

function Stop-WrapperChild {
    <#
      .SYNOPSIS
        Kill the wrapper child from OUTSIDE the launcher (decisions 88 (3) / 83) and return the pids.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][int]$Port)
    $children = @(Get-WrapperPids -Port $Port)
    foreach ($child in $children) {
        Write-Host ('[kill] pid=' + $child.ProcessId)
        & taskkill.exe /PID $child.ProcessId /T /F 2>&1 | ForEach-Object { Write-Host ('[kill] ' + $_) }
    }
    return $children
}

function New-GateRuntimeRoot {
    <#
      .SYNOPSIS
        A private runtime root whose <GateVariant> directory is a junction onto an interpreter that
        really cannot see a GPU.
      .DESCRIPTION
        The gate (decisions 88 (1)) refuses a GPU variant when torch answers is_available()=False and
        device_count=0. To drive that on this machine the variant has to RESOLVE (a missing python.exe
        fails earlier, with "the runtime is not there yet") while the interpreter it resolves to has a
        CPU-only torch. runtime-cpu is exactly that: measured on 2026-09-05 it answers
        {"available":false,"count":0} in 1.6 s. A junction costs nothing and needs no elevation; the
        build tree is only ever read.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Variant,
        [Parameter(Mandatory = $true)][string]$Target
    )
    if (Test-Path -LiteralPath $Root) {
        Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
    }
    $null = New-Item -ItemType Directory -Path $Root -Force
    $link = Join-Path $Root $Variant
    $null = New-Item -ItemType Junction -Path $link -Value $Target
    Write-Host ('[gate] ' + $link + ' -> ' + $Target)
    return $link
}

function Invoke-GateProbe {
    <#
      .SYNOPSIS
        decisions 88 (1)(2) -- the variant gate: a GPU variant that cannot see a GPU is refused
        BEFORE any wrapper child is started, with a one line reason and the variant to switch to.
      .DESCRIPTION
        This is a separate launcher instance on its own private port and data directory, because the
        variant is read when the launcher composes its settings (changing it in a running window only
        takes effect at the next start), and because the Radeon flavour does not even offer the CUDA
        variants in the settings combo (decisions 5).
        What must hold: state goes to "failed", the reason names the variant that cannot see the GPU
        and the one to switch to, NO python -m ywk_server was started, and the port never listened.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Variant,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$DataDir,
        [Parameter(Mandatory = $true)][string]$RuntimeRoot,
        [string]$ExpectSuggestion = ''
    )

    $proc = $null
    try {
        $null = New-LauncherFixture -DataDir $DataDir -Variant $Variant -Port $Port -HfHome $HfHome
        $proc = Start-Launcher -Exe $Exe -AppDir $AppDir -RuntimeRoot $RuntimeRoot -DataDir $DataDir
        $win = Get-MainWindow -Proc $proc -TimeoutSeconds 60
        Write-Host ('[gate] variant = ' + (Get-TextById -Root $win -Id 'StatusVariantText'))

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Invoke-ButtonById -Root $win -Id 'MainStartButton'
        $failed = Wait-ForPattern -Root $win -Id 'MainStateText' -Pattern $T.Failed -TimeoutSeconds 60 -IntervalMilliseconds 200
        $sw.Stop()

        $reason = Get-TextById -Root $win -Id 'StatusReasonText'
        $notices = Get-TextById -Root $win -Id 'StatusNoticesText'
        if ($null -eq $reason) { $reason = '' }
        if ($null -eq $notices) { $notices = '' }
        $band = ($reason + ' ' + $notices).Trim()
        Write-Host ('[gate] state   = ' + $failed.text + ' after ' + [math]::Round($sw.Elapsed.TotalSeconds, 2) + ' s')
        Write-Host ('[gate] reason  = ' + $reason)
        Write-Host ('[gate] notices = ' + $notices)
        # The log band is the only place a refusal that never reached the state band would show.
        foreach ($line in @((Get-TextById -Root $win -Id 'StatusLogBox') -split "`r?`n")) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { Write-Host ('[gate] log     = ' + $line) }
        }

        Add-Step 'the gate refuses a variant that cannot see a GPU' $failed.ok (
            'state=' + $failed.text + ' in ' + [math]::Round($sw.Elapsed.TotalSeconds, 2) + ' s')
        Add-Step 'the refusal names the variant and what it cannot do' (
            ($band -like ('*' + $Variant + '*')) -and ($band -like ('*' + $T.CannotSee + '*'))) $band
        if (-not [string]::IsNullOrEmpty($ExpectSuggestion)) {
            Add-Step 'the refusal suggests the variant this machine can run' (
                $band -like ('*' + $ExpectSuggestion + '*')) ('expected ' + $ExpectSuggestion)
        }

        # The whole point of the gate (decisions 83): nothing is started, so nothing can die on the
        # first synthesis with an access violation. The torch probe python is a different command
        # line (ywk_gpu_probe-<8>.py) and is gone by now.
        $children = @(Get-WrapperPids -Port $Port)
        Add-Step 'the gate started no wrapper child' ($children.Count -eq 0) ('children=' + $children.Count)
        Add-Step 'the gate never opened the port' (-not (Test-PortListening -Port $Port)) ('port ' + $Port)
    } finally {
        if ($null -ne $proc) {
            $strays = @(Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*'))
            Add-Step 'the gate run leaves nothing behind' ($strays.Count -eq 0) ('strays=' + $strays.Count)
        }
        if ((-not $KeepDataDir) -and (Test-Path -LiteralPath $DataDir)) {
            Remove-Item -LiteralPath $DataDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ((-not $KeepDataDir) -and (Test-Path -LiteralPath $RuntimeRoot)) {
            # Remove the junction itself, never what it points at (the build tree is read only here).
            foreach ($child in @(Get-ChildItem -LiteralPath $RuntimeRoot -Force -ErrorAction SilentlyContinue)) {
                try { $child.Delete() } catch { }
            }
            Remove-Item -LiteralPath $RuntimeRoot -Force -Recurse -ErrorAction SilentlyContinue
        }
    }
}

# ================================================================= round three helpers (92 / 94)

function Get-DirBytes {
    <#
      .SYNOPSIS
        Total bytes under a directory (0 when it is not there).
      .DESCRIPTION
        The cache steps (i / l) are read in BYTES on purpose: "the cache is gone" has to stay true
        when the sweep leaves the empty directory behind, and has to stay false when it deletes the
        names it knows and leaves a 2 GiB .part.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return [long]0 }
    $total = [long]0
    foreach ($f in @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue)) {
        $total += [long]$f.Length
    }
    return $total
}

function New-CacheFixture {
    <#
      .SYNOPSIS
        Put a few harmless files into <data>\cache and return their total size in bytes.
      .DESCRIPTION
        This probe never downloads anything (only the E2E seat is allowed to fetch), so the cache the
        launcher is asked to delete has to be made by hand. The three names are the shapes the fetcher
        really leaves behind -- a verified original, a half written .part, and one nested directory --
        so a sweep that only knows one of the three is caught.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$CacheDir,
        [int]$KiB = 64
    )
    $null = New-Item -ItemType Directory -Path $CacheDir -Force
    $blob = New-Object byte[] ($KiB * 1024)
    $sub = Join-Path $CacheDir 'ywk-probe-sub'
    $null = New-Item -ItemType Directory -Path $sub -Force
    [System.IO.File]::WriteAllBytes((Join-Path $CacheDir 'ywk-probe-cache-1.whl'), $blob)
    [System.IO.File]::WriteAllBytes((Join-Path $CacheDir 'ywk-probe-cache-2.whl.part'), $blob)
    [System.IO.File]::WriteAllBytes((Join-Path $sub 'ywk-probe-cache-3.bin'), $blob)
    $bytes = Get-DirBytes -Path $CacheDir
    Write-Host ('[cache] seeded ' + $CacheDir + ' with ' + $bytes + ' B in 3 files')
    return $bytes
}

function Wait-ForDirEmpty {
    <#
      .SYNOPSIS
        Wait until a directory holds 0 bytes; return the last reading and how long it took.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [double]$TimeoutSeconds = 30
    )
    $start = Get-Date
    while ($true) {
        $bytes = Get-DirBytes -Path $Path
        if ($bytes -eq 0) {
            return [pscustomobject]@{ ok = $true; bytes = $bytes; elapsed = ((Get-Date) - $start).TotalSeconds }
        }
        if (((Get-Date) - $start).TotalSeconds -ge $TimeoutSeconds) {
            return [pscustomobject]@{ ok = $false; bytes = $bytes; elapsed = ((Get-Date) - $start).TotalSeconds }
        }
        Start-Sleep -Milliseconds 500
    }
}

function Get-Sha256Hex {
    <#
      .SYNOPSIS
        Lower case sha256 hex of a file, without Get-FileHash.
      .DESCRIPTION
        MEASURED 2026-09-05 on this machine: Windows PowerShell 5.1 started from a pwsh session
        inherits PowerShell 7's PSModulePath, Microsoft.PowerShell.Utility does not auto load, and
        Get-FileHash dies with "the term Get-FileHash is not recognized". build/Common.ps1 already
        carries the same fallback for the same reason; probe/ cannot dot-source that file, so the
        shape is repeated here.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if ($null -ne (Get-Command 'Get-FileHash' -ErrorAction SilentlyContinue)) {
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        try {
            $bytes = $sha.ComputeHash($fs)
        } finally {
            $fs.Dispose()
        }
    } finally {
        $sha.Dispose()
    }
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-ElementLabel {
    <#
      .SYNOPSIS
        One printable line for an element (type / AutomationId / Name).
      .DESCRIPTION
        The round three screen is being written by another seat while this file is being written, so
        the steps that cannot know an AutomationId yet find their element by name and PRINT what they
        found. The id in that line is what the integration seat pins afterwards.
    #>
    [CmdletBinding()]
    param($Element)
    if ($null -eq $Element) { return '<not found>' }
    $type = '?'
    $id = ''
    $name = ''
    try { $type = $Element.Current.ControlType.ProgrammaticName.Replace('ControlType.', '') } catch { }
    try { $id = [string]$Element.Current.AutomationId } catch { }
    try { $name = ([string]$Element.Current.Name) -replace "`r?`n", ' / ' } catch { }
    return ('[' + $type + "] id='" + $id + "' name='" + $name + "'")
}

function Find-ByNameLike {
    <#
      .SYNOPSIS
        The first element under Root whose Name CONTAINS the text (id unknown).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Text,
        [double]$TimeoutSeconds = 5
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $true_ = [System.Windows.Automation.Condition]::TrueCondition
    while ($true) {
        foreach ($el in @($Root.FindAll($script:TS::Descendants, $true_))) {
            $name = ''
            try { $name = [string]$el.Current.Name } catch { continue }
            if ($name -like ('*' + $Text + '*')) { return $el }
        }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 400
    }
}

function Find-ByIdAny {
    <#
      .SYNOPSIS
        The first element that matches any of the candidate AutomationIds.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string[]]$Id,
        [int]$TimeoutSeconds = 1
    )
    foreach ($candidate in $Id) {
        $el = Find-ById -Root $Root -Id $candidate -TimeoutSeconds $TimeoutSeconds
        if ($null -ne $el) { return $el }
    }
    return $null
}

function Invoke-Element {
    <#
      .SYNOPSIS
        Press an element found by scan (InvokePattern, then Toggle, then SelectionItem).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Element)
    try {
        ($Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        Start-Sleep -Milliseconds 400
        return $true
    } catch { }
    try {
        ($Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
        Start-Sleep -Milliseconds 400
        return $true
    } catch { }
    return $false
}

function Close-StrayDialog {
    <#
      .SYNOPSIS
        Close any window of the process that is not the main window, and say what it was.
      .DESCRIPTION
        Step k suspects the shell's own "this app can't run on your PC" box (design 20-5 (1)), and an
        unattended run must never be parked on a modal nobody will press. Whatever is closed here is
        printed, because IF something is closed that is itself the finding.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Proc)
    $closed = @()
    foreach ($w in @(Get-ProcessWindows -Proc $Proc)) {
        $id = ''
        $name = ''
        try { $id = [string]$w.Current.AutomationId } catch { }
        try { $name = [string]$w.Current.Name } catch { }
        if ($id -eq 'MainWindow') { continue }
        Write-Host ('[dialog] ' + $name + ' (id=' + $id + ')')
        $closed += $name
        try {
            ($w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
        } catch {
            Write-Host ('[dialog] close failed: ' + $_.Exception.Message)
        }
    }
    return @($closed)
}

function Read-SettingsDoc {
    <#
      .SYNOPSIS
        settings.json as an object (null when it is not there).
      .DESCRIPTION
        -Encoding UTF8 is not optional: Windows PowerShell 5.1 reads a BOM-less file with the
        machine's ANSI code page (probe/README section 2, convoy D).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Get-JsonMember {
    <#
      .SYNOPSIS
        One member of a parsed JSON object, or null when the member is not there.
    #>
    [CmdletBinding()]
    param($Doc, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Doc) { return $null }
    if ($Doc.PSObject.Properties.Name -notcontains $Name) { return $null }
    return $Doc.$Name
}

function Get-RuntimeStamp {
    <#
      .SYNOPSIS
        The stamp of ONE variant out of settings.json (decisions 91).
      .DESCRIPTION
        The stamp used to be a single pair of scalars for the whole file
        (runtimeLedgerSha256 / installedAppVersion). A machine with two runtimes built then cried
        "rebuild the runtime" the moment the variant was switched, because the one stored sha256 was
        compared against the OTHER variant's ledger. Since the correction seat of convoy D (3) round
        three the file carries a table per variant -- runtimeLedgers / installedAppVersions -- so the
        probe reads one level down.
    #>
    [CmdletBinding()]
    param(
        $Doc,
        [Parameter(Mandatory = $true)][string]$Member,
        [Parameter(Mandatory = $true)][string]$Variant
    )
    $table = Get-JsonMember -Doc $Doc -Name $Member
    return (Get-JsonMember -Doc $table -Name $Variant)
}

function Remove-PrivateTree {
    <#
      .SYNOPSIS
        Remove a scratch tree that may contain junctions, WITHOUT following them.
      .DESCRIPTION
        Remove-Item -Recurse walks into a junction on Windows PowerShell 5.1, and what these junctions
        point at is build/out/app. So every reparse point is deleted as a link first (DirectoryInfo
        .Delete() removes the junction, never the target), exactly the way the gate teardown does.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root)
    if (-not (Test-Path -LiteralPath $Root)) { return }
    foreach ($child in @(Get-ChildItem -LiteralPath $Root -Force -ErrorAction SilentlyContinue)) {
        $isLink = $false
        try { $isLink = (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) } catch { }
        if ($isLink) {
            try { $child.Delete() } catch { Write-Host ('[tree] junction not removed: ' + $child.FullName) }
            continue
        }
        Remove-Item -LiteralPath $child.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
}

function New-PrivateAppTree {
    <#
      .SYNOPSIS
        A private app tree whose ledger directory this probe is allowed to break.
      .DESCRIPTION
        Steps h and j both need a distribution tree they can change; build/out/app is read only here.
        server/ licenses/ voices/ are JUNCTIONS onto the real tree (no elevation, nothing written
        through them) and only ledger/ is a real directory of copies.
        -BreakRuntimeLedger renames the first item's "sha256" key, which is the cheapest honest
        failure the first run can have WITHOUT A SOCKET: LedgerReader marks the item unverifiable,
        FetchPlanner throws LedgerException ("an item with no sha256" -- Services/Ledger/
        FetchPlanner.cs:192), FirstRunViewModel.TryPlan catches it and the download step returns false
        with a one line reason (ViewModels/FirstRunViewModel.cs:637-643).
        -SkipVcRedistLedger leaves vc_redist.json out, and with no vc_redist ledger the launcher skips
        that step altogether (MainViewModel.cs:161-166 returns null, FirstRunViewModel.cs:700-703
        treats null as "this step does not exist"). That is what keeps a machine whose msvcp140.dll is
        below the ledger minimum from pulling 24 MiB during a probe run.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$SourceAppDir,
        [Parameter(Mandatory = $true)][string]$Variant,
        [switch]$BreakRuntimeLedger,
        [switch]$SkipVcRedistLedger
    )
    Remove-PrivateTree -Root $Root
    $null = New-Item -ItemType Directory -Path $Root -Force
    foreach ($name in @('server', 'licenses', 'voices')) {
        $target = Join-Path $SourceAppDir $name
        if (Test-Path -LiteralPath $target) {
            $null = New-Item -ItemType Junction -Path (Join-Path $Root $name) -Value $target
        }
    }
    $ledgerDir = Join-Path $Root 'ledger'
    $null = New-Item -ItemType Directory -Path $ledgerDir -Force
    $copied = 0
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $SourceAppDir 'ledger') -Filter '*.json' -File -ErrorAction SilentlyContinue)) {
        if ($SkipVcRedistLedger -and ($file.Name -eq 'vc_redist.json')) { continue }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $ledgerDir $file.Name) -Force
        $copied++
    }
    Write-Host ('[tree] private app tree = ' + $Root + ' (ledger json = ' + $copied + ')')

    if ($BreakRuntimeLedger) {
        $path = Join-Path $ledgerDir ('runtime-' + $Variant + '.json')
        if (-not (Test-Path -LiteralPath $path)) {
            Write-Host ('[tree] nothing to break: ' + $path)
        } else {
            $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
            $marker = '"sha256"'
            $at = $text.IndexOf($marker, [System.StringComparison]::Ordinal)
            if ($at -lt 0) {
                Write-Host ('[tree] no sha256 key in ' + $path)
            } else {
                $text = $text.Substring(0, $at) + '"sha256_removed_by_the_probe"' + $text.Substring($at + $marker.Length)
                [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
                Write-Host ('[tree] broke ' + $path + ' (the first item has no sha256 any more)')
            }
        }
    }
    return $Root
}

function Edit-LedgerBytes {
    <#
      .SYNOPSIS
        Change a ledger without making it invalid (step j: the sha256 of the FILE must move).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $marker = '"generated"'
    $at = $text.IndexOf($marker, [System.StringComparison]::Ordinal)
    if ($at -lt 0) { return $false }
    $text = $text.Substring(0, $at) + '"probe_touched": true, ' + $text.Substring($at)
    [System.IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ('[tree] the ledger bytes moved: ' + $Path)
    return $true
}

function Get-VariantComboToken {
    <#
      .SYNOPSIS
        An ASCII fragment of the display name of a variant (RuntimeVariants.DisplayName).
      .DESCRIPTION
        The wizard's combo shows display names ("Radeon gfx1151 ..." / "CUDA 13.0 ..."), so the probe
        picks the row by a fragment it can write in ASCII instead of by the id.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Variant)
    if ($Variant -like '*gfx*') { return 'gfx1151' }
    if ($Variant -eq 'cu130') { return '13.0' }
    if ($Variant -eq 'cu126') { return '12.6' }
    if ($Variant -eq 'cpu') { return 'CPU' }
    return $Variant
}

# ================================================================= h. the wizard, counted in presses

$script:WizardPresses = 0

function Invoke-WizardPress {
    <#
      .SYNOPSIS
        Press one thing in the wizard AND count it (decisions 94 (1) counts presses, not steps).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$What
    )
    $script:WizardPresses++
    Write-Host ('[wizard] press ' + $script:WizardPresses + ' = ' + $What)
    return (Invoke-ButtonById -Root $Root -Id $Id -TimeoutSeconds 15)
}

function Invoke-WizardPressProbe {
    <#
      .SYNOPSIS
        decisions 94 (1) -- COUNT THE PRESSES of the first run wizard and prove a failed step stops it.
      .DESCRIPTION
        The acceptance line is "operator presses <= 6" and convoy E (2) measured NINE:
        consent box, "agree and continue", the variant, "start fetching with this build", then FOUR
        times "next" (fetch / unpack / models / start), then "to the try screen". The ruling is that
        every step which SUCCEEDS walks on by itself and only a step that FAILED stops, which takes
        the count to FIVE.
        This probe pulls nothing. The wizard is driven against a private app tree whose runtime ledger
        cannot be planned from, so the fetch step fails on the spot with a one line reason, and what
        is measured is (a) the presses up to the failure and (b) that the failed step really does stop
        the wizard where it is. The other half of the ruling -- that four presses have disappeared
        from a run where every step succeeds -- can only be measured in a run that really fetches, so
        it lives behind -WizardFullRun and belongs to the E2E seat.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Variant,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$DataDir,
        [Parameter(Mandatory = $true)][string]$AppTreeRoot,
        [switch]$FullRun
    )

    $script:WizardPresses = 0
    $proc = $null
    try {
        $null = New-LauncherFixture -DataDir $DataDir -Variant $Variant -Port $Port -FirstRunCompleted $false
        $cacheDir = Join-Path $DataDir 'cache'
        if ($FullRun) {
            $null = New-PrivateAppTree -Root $AppTreeRoot -SourceAppDir $AppDir -Variant $Variant
        } else {
            $null = New-PrivateAppTree -Root $AppTreeRoot -SourceAppDir $AppDir -Variant $Variant `
                -BreakRuntimeLedger -SkipVcRedistLedger
        }

        $proc = Start-Launcher -Exe $Exe -AppDir $AppTreeRoot -RuntimeRoot $RuntimeRoot -DataDir $DataDir
        $null = Get-MainWindow -Proc $proc -TimeoutSeconds 60
        $wizard = Get-DialogWindow -Proc $proc -TimeoutSeconds 30
        $wizardId = ''
        if ($null -ne $wizard) { $wizardId = [string]$wizard.Current.AutomationId }
        Add-Step 'the wizard opens by itself when the first run was never done' (
            $wizardId -eq 'FirstRunWizard') ('window id = ' + $wizardId)
        if ($wizardId -ne 'FirstRunWizard') { return }

        Write-Host ('[wizard] step = ' + (Get-TextById -Root $wizard -Id 'FirstRunStepTitle') +
            '  (' + (Get-TextById -Root $wizard -Id 'FirstRunStepNumber') + ')')
        Write-Host ('[wizard] size = ' + (Get-TextById -Root $wizard -Id 'FirstRunSizeText'))

        # press 1 -- the consent box (decisions 46: it is only usable when the notices really loaded)
        $script:WizardPresses++
        Write-Host ('[wizard] press ' + $script:WizardPresses + ' = the consent box')
        $null = Set-ToggleById -Root $wizard -Id 'FirstRunAcceptCheck' -On $true

        # press 2 -- "agree and continue"
        $null = Invoke-WizardPress -Root $wizard -Id 'FirstRunNextButton' -What 'agree and continue'
        Start-Sleep -Milliseconds 800

        # press 3 -- the variant
        $choices = @(Get-ComboItemNames -Root $wizard -Id 'FirstRunVariantCombo')
        Write-Host ('[wizard] variants = ' + ($choices -join ' | '))
        $script:WizardPresses++
        Write-Host ('[wizard] press ' + $script:WizardPresses + ' = the variant')
        $token = Get-VariantComboToken -Variant $Variant
        $picked = ''
        try {
            $picked = Select-ComboItemById -Root $wizard -Id 'FirstRunVariantCombo' -ItemText $token -Contains
        } catch {
            if ($choices.Count -ge 1) {
                $picked = Select-ComboItemById -Root $wizard -Id 'FirstRunVariantCombo' -ItemText $choices[0]
            }
        }
        Write-Host ('[wizard] picked = ' + $picked)

        # press 4 -- "start fetching with this build"
        $null = Invoke-WizardPress -Root $wizard -Id 'FirstRunNextButton' -What 'start fetching with this build'

        if ($FullRun) {
            # E2E seat only. Nothing below presses anything: reaching the last step without a press is
            # the whole proof that the four "next" presses are gone.
            $done = Wait-ForPattern -Root $wizard -Id 'FirstRunStepTitle' -Pattern ([regex]::Escape($T.Finished)) `
                -TimeoutSeconds $WizardFullRunTimeoutSeconds -IntervalMilliseconds 1000
            Write-Host ('[wizard] last step = ' + $done.text + ' after ' + [math]::Round($done.elapsed, 1) + ' s')
            Add-Step 'every step that succeeded walked on without a press' $done.ok (
                'reached ' + $done.text + ' in ' + [math]::Round($done.elapsed, 1) + ' s with ' +
                $script:WizardPresses + ' presses')
            if ($done.ok) {
                $null = Invoke-WizardPress -Root $wizard -Id 'FirstRunNextButton' -What 'to the try screen'
                Start-Sleep -Seconds 2
            }
            Add-Step 'the whole first run costs five presses' ($script:WizardPresses -eq 5) (
                'presses = ' + $script:WizardPresses + ' (was 9 in convoy E (2), the acceptance line is 6)')
            $settings = Read-SettingsDoc -Path (Join-Path $DataDir 'settings.json')
            Add-Step 'the finished first run is written down' (
                [bool](Get-JsonMember -Doc $settings -Name 'firstRunCompleted')) (
                'firstRunCompleted = ' + (Get-JsonMember -Doc $settings -Name 'firstRunCompleted'))
            return
        }

        # ---- offline shape: the fetch step must fail here, at once, with a reason ----
        $failed = Wait-ForPattern -Root $wizard -Id 'FirstRunMessageText' -Pattern ([regex]::Escape($T.Ledger)) `
            -TimeoutSeconds 30 -IntervalMilliseconds 300
        $title = Get-TextById -Root $wizard -Id 'FirstRunStepTitle'
        $number = Get-TextById -Root $wizard -Id 'FirstRunStepNumber'
        $message = Get-TextById -Root $wizard -Id 'FirstRunMessageText'
        if ($null -eq $title) { $title = '' }
        if ($null -eq $number) { $number = '' }
        if ($null -eq $message) { $message = '' }
        Write-Host ('[wizard] step    = ' + $title + '  (' + $number + ')')
        Write-Host ('[wizard] message = ' + $message)

        Add-Step 'a ledger that cannot be planned from fails the fetch step with a reason' (
            $failed.ok -and ($message -like ('*' + $T.Ledger + '*'))) (
            $message + ' [' + [math]::Round($failed.elapsed, 2) + ' s]')
        # The title must be the WHOLE "fetch (runtime)" and the counter must be step 3, not just
        # "contains fetch": the models step is called "fetch (models)", so a substring test stays
        # green after the wizard walked past the step that failed (proved with a deliberate break).
        Add-Step 'the failed step keeps the wizard where it failed' (
            ($title -eq $T.FetchRun) -and ($number -eq '3 / 7')) (
            'step = ' + $title + ' (' + $number + ')')
        Add-Step 'the presses up to the failed step are four' ($script:WizardPresses -eq 4) (
            'presses = ' + $script:WizardPresses +
            ' (consent, agree, variant, start; the fifth is "to the try screen")')
        Add-Step 'the wizard fetched nothing at all' ((Get-DirBytes -Path $cacheDir) -eq 0) (
            'cache = ' + [string](Get-DirBytes -Path $cacheDir) + ' B')

        # press 5 -- "next" on a failed step retries THAT step; it must not walk on to the next one.
        $null = Invoke-WizardPress -Root $wizard -Id 'FirstRunNextButton' -What 'next, on the failed step'
        Start-Sleep -Seconds 2
        $title2 = Get-TextById -Root $wizard -Id 'FirstRunStepTitle'
        $number2 = Get-TextById -Root $wizard -Id 'FirstRunStepNumber'
        if ($null -eq $title2) { $title2 = '' }
        if ($null -eq $number2) { $number2 = '' }
        Write-Host ('[wizard] step after the retry = ' + $title2 + '  (' + $number2 + ')')
        Add-Step 'pressing next on a failed step retries it instead of walking on' (
            ($title2 -eq $T.FetchRun) -and ($number2 -eq '3 / 7')) (
            'step = ' + $title2 + ' (' + $number2 + ')')

        try {
            ($wizard.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
        } catch {
            Write-Host ('[wizard] close failed: ' + $_.Exception.Message)
        }
        Start-Sleep -Milliseconds 800
        $settings = Read-SettingsDoc -Path (Join-Path $DataDir 'settings.json')
        $completed = Get-JsonMember -Doc $settings -Name 'firstRunCompleted'
        Add-Step 'a first run that failed is not written down as done' (-not [bool]$completed) (
            'firstRunCompleted = ' + $completed)
    } catch {
        Add-Step 'the wizard press count runs to the end' $false $_.Exception.Message
    } finally {
        if ($null -ne $proc) {
            $strays = @(Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*'))
            Add-Step 'the wizard run leaves nothing behind' ($strays.Count -eq 0) ('strays=' + $strays.Count)
        }
        if (-not $KeepDataDir) {
            Remove-PrivateTree -Root $AppTreeRoot
            if (Test-Path -LiteralPath $DataDir) {
                Remove-Item -LiteralPath $DataDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# ================================================================= j. the runtime ledger stamp

function Test-RebuildOffer {
    <#
      .SYNOPSIS
        Is the window offering to rebuild the runtime (decisions 91)? Returns the element or null.
      .DESCRIPTION
        The AutomationId is not published yet (the launcher seat is writing the screen while this file
        is written), so the offer is looked up by the candidate ids first and by its words second, and
        whatever is found is printed with its real id.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Window,
        [double]$TimeoutSeconds = 15
    )
    $ids = @(
        'StatusRebuildRuntimeButton', 'StatusRebuildButton', 'StatusLedgerMismatchText',
        'StatusRuntimeMismatchText', 'MainRebuildRuntimeButton', 'SettingsRebuildRuntimeButton')
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        $el = Find-ByIdAny -Root $Window -Id $ids -TimeoutSeconds 1
        if ($null -ne $el) { return $el }
        $el = Find-ByNameLike -Root $Window -Text $T.Rebuild -TimeoutSeconds 1
        if ($null -ne $el) { return $el }
        if ((Get-Date) -ge $deadline) { break }
        Start-Sleep -Milliseconds 500
    }
    # WPF builds the content of a TabItem only when it is selected, so "not on the status page" is
    # not "not there": sweep the settings page once before saying no. This also makes the NEGATIVE
    # step (a matching ledger offers nothing) mean what it says.
    try {
        $null = Select-Tab -Window $Window -TabId 'TabSettings'
        $el = Find-ByIdAny -Root $Window -Id $ids -TimeoutSeconds 1
        if ($null -eq $el) { $el = Find-ByNameLike -Root $Window -Text $T.Rebuild -TimeoutSeconds 1 }
        $null = Select-Tab -Window $Window -TabId 'TabStatus'
        return $el
    } catch {
        Write-Host ('[ledger] the settings page could not be swept: ' + $_.Exception.Message)
        return $null
    }
}

function Invoke-LedgerStampProbe {
    <#
      .SYNOPSIS
        decisions 91 -- settings.json carries runtimeLedgerSha256 / installedAppVersion, and a ledger
        that no longer matches makes the window offer to rebuild the runtime.
      .DESCRIPTION
        Three windows on a private app tree (AppPaths only ever reads it, so the copy costs three
        junctions and a handful of json):
          1  a data directory with no stamps at all: the launcher must write them.
          2  the stamps in place and the ledger untouched: the offer must NOT be on screen.
          3  the ledger bytes moved: the offer must appear.
        No server is started and no port is opened in any of the three.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Variant,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$DataDir,
        [Parameter(Mandatory = $true)][string]$AppTreeRoot
    )

    $proc = $null
    $settingsPath = Join-Path $DataDir 'settings.json'
    $ledgerPath = Join-Path $AppTreeRoot ('ledger\runtime-' + $Variant + '.json')
    try {
        $null = New-LauncherFixture -DataDir $DataDir -Variant $Variant -Port $Port
        $null = New-PrivateAppTree -Root $AppTreeRoot -SourceAppDir $AppDir -Variant $Variant

        # ---- 1. the stamps are written -------------------------------------------------
        $proc = Start-Launcher -Exe $Exe -AppDir $AppTreeRoot -RuntimeRoot $RuntimeRoot -DataDir $DataDir
        $win = Get-MainWindow -Proc $proc -TimeoutSeconds 60
        $version = Get-TextById -Root $win -Id 'MainVersionText'
        $sha = $null
        $stampedVersion = $null
        $waited = 0
        while ($waited -lt 20) {
            $doc = Read-SettingsDoc -Path $settingsPath
            $sha = Get-RuntimeStamp -Doc $doc -Member 'runtimeLedgers' -Variant $Variant
            $stampedVersion = Get-RuntimeStamp -Doc $doc -Member 'installedAppVersions' -Variant $Variant
            if ((-not [string]::IsNullOrWhiteSpace([string]$sha)) -and
                (-not [string]::IsNullOrWhiteSpace([string]$stampedVersion))) { break }
            Start-Sleep -Seconds 1
            $waited++
        }
        Write-Host ('[ledger] window version   = ' + $version)
        Write-Host ('[ledger] runtimeLedgers.' + $Variant + '        = ' + $sha)
        Write-Host ('[ledger] installedAppVersions.' + $Variant + ' = ' + $stampedVersion)
        Add-Step 'settings.json is stamped with the runtime ledger sha256 of THIS variant' (
            ([string]$sha) -match '^[0-9a-fA-F]{64}$') (
            'runtimeLedgers.' + $Variant + ' = ' + $sha + ' after ' + $waited + ' s')
        Add-Step 'settings.json is stamped with the version that was installed' (
            -not [string]::IsNullOrWhiteSpace([string]$stampedVersion)) (
            'installedAppVersions.' + $Variant + ' = ' + $stampedVersion + ' :: window says ' + $version)

        # The stamp of a variant that was never built must NOT be there: one pair of scalars for the
        # whole file is what made switching variants raise a false "rebuild the runtime" (convoy D (3)
        # round three, high). Pick a name this release does not carry.
        $otherVariant = 'cu126'
        if ($Variant -eq 'cu126') { $otherVariant = 'cpu' }
        $otherSha = Get-RuntimeStamp -Doc (Read-SettingsDoc -Path $settingsPath) `
            -Member 'runtimeLedgers' -Variant $otherVariant
        Add-Step 'the stamp of a variant that was never built is not written' (
            [string]::IsNullOrWhiteSpace([string]$otherSha)) (
            'runtimeLedgers.' + $otherVariant + ' = ' + $otherSha)

        $offerBefore = Test-RebuildOffer -Window $win -TimeoutSeconds 3
        Add-Step 'a ledger that still matches offers nothing' ($null -eq $offerBefore) (
            Get-ElementLabel $offerBefore)

        $null = Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*')
        $proc = $null

        # If the launcher did not stamp anything, put a stamp there by hand so the COMPARISON can
        # still be measured, and say so in the detail (the two are different findings).
        $guessed = $false
        if (([string]$sha) -notmatch '^[0-9a-fA-F]{64}$') {
            if (Test-Path -LiteralPath $ledgerPath) {
                $sha = Get-Sha256Hex -Path $ledgerPath
                $null = Set-RuntimeStamp -Path $settingsPath -Member 'runtimeLedgers' `
                    -Variant $Variant -Value $sha
                $guessed = $true
                Write-Host ('[ledger] the probe stamped runtimeLedgers.' + $Variant + ' = ' + $sha +
                    ' by hand')
            }
        }

        # ---- 2. the stamp matches: no offer --------------------------------------------
        $proc = Start-Launcher -Exe $Exe -AppDir $AppTreeRoot -RuntimeRoot $RuntimeRoot -DataDir $DataDir
        $win = Get-MainWindow -Proc $proc -TimeoutSeconds 60
        Start-Sleep -Seconds 2
        $quiet = Test-RebuildOffer -Window $win -TimeoutSeconds 5
        Add-Step 'the same ledger, the same stamp, and still no offer' ($null -eq $quiet) (
            'guessed stamp = ' + $guessed + ' :: ' + (Get-ElementLabel $quiet))
        $null = Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*')
        $proc = $null

        # ---- 3. the ledger moved: the offer appears ------------------------------------
        $moved = Edit-LedgerBytes -Path $ledgerPath
        Add-Step 'the probe could break the ledger it copied' $moved $ledgerPath
        $proc = Start-Launcher -Exe $Exe -AppDir $AppTreeRoot -RuntimeRoot $RuntimeRoot -DataDir $DataDir
        $win = Get-MainWindow -Proc $proc -TimeoutSeconds 60
        $offer = Test-RebuildOffer -Window $win -TimeoutSeconds 20
        Write-Host ('[ledger] offer   = ' + (Get-ElementLabel $offer))
        Write-Host ('[ledger] notices = ' + (Get-TextById -Root $win -Id 'StatusNoticesText' -TimeoutSeconds 3))
        Write-Host ('[ledger] reason  = ' + (Get-TextById -Root $win -Id 'StatusReasonText' -TimeoutSeconds 3))
        Add-Step 'a ledger that no longer matches offers to rebuild the runtime' ($null -ne $offer) (
            Get-ElementLabel $offer)
    } catch {
        Add-Step 'the ledger stamp probe runs to the end' $false $_.Exception.Message
    } finally {
        if ($null -ne $proc) {
            $strays = @(Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*'))
            Add-Step 'the ledger stamp run leaves nothing behind' ($strays.Count -eq 0) ('strays=' + $strays.Count)
        }
        if (-not $KeepDataDir) {
            Remove-PrivateTree -Root $AppTreeRoot
            if (Test-Path -LiteralPath $DataDir) {
                Remove-Item -LiteralPath $DataDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# ================================================================= k. a python.exe that cannot start

function Invoke-BadPythonProbe {
    <#
      .SYNOPSIS
        design 20-5 (1) -- a runtime whose python.exe is not an executable must be told at once.
      .DESCRIPTION
        The finding convoy D (2) left open: pressing "start the server" against such a runtime left
        the window silent for SIXTY SECONDS (state "stopped", not one line in the log band, no child,
        no port), while the same materials fed straight to ServerLaunchPlan/ServerProcess came back in
        8 ms with the reason. The GPU enumeration that runs first (MainViewModel.StartServerAsync's
        first await) never returned, and AsyncRelayCommand swallows what it does not catch.
        So: a runtime root whose <variant>\python.exe is a TEXT FILE, one press, and a stopwatch. The
        budget is 10 s. Any window that is not the main window is closed and reported -- the seat
        suspected the shell's own "this app can't run on your PC" box.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Variant,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$DataDir,
        [Parameter(Mandatory = $true)][string]$RuntimeRootPath,
        [double]$BudgetSeconds = 10
    )

    $proc = $null
    try {
        Remove-PrivateTree -Root $RuntimeRootPath
        $dir = Join-Path $RuntimeRootPath ('runtime-' + $Variant)
        $null = New-Item -ItemType Directory -Path $dir -Force
        $fake = Join-Path $dir 'python.exe'
        [System.IO.File]::WriteAllText($fake,
            "this is not an executable" + [System.Environment]::NewLine,
            (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ('[badpython] ' + $fake + ' = ' + (Get-Item -LiteralPath $fake).Length + ' B of text')

        $null = New-LauncherFixture -DataDir $DataDir -Variant $Variant -Port $Port
        $proc = Start-Launcher -Exe $Exe -AppDir $AppDir -RuntimeRoot $RuntimeRootPath -DataDir $DataDir
        $win = Get-MainWindow -Proc $proc -TimeoutSeconds 60
        Write-Host ('[badpython] variant = ' + (Get-TextById -Root $win -Id 'StatusVariantText'))

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Invoke-ButtonById -Root $win -Id 'MainStartButton'
        $told = $false
        $state = ''
        $reason = ''
        $log = ''
        $dialogs = @()
        while ($sw.Elapsed.TotalSeconds -lt 60) {
            $state = [string](Get-TextById -Root $win -Id 'MainStateText' -TimeoutSeconds 2)
            $reason = [string](Get-TextById -Root $win -Id 'StatusReasonText' -TimeoutSeconds 2)
            $log = [string](Get-TextById -Root $win -Id 'StatusLogBox' -TimeoutSeconds 2)
            if (($state -match $T.Failed) -or (-not [string]::IsNullOrWhiteSpace($reason))) {
                $told = $true
                break
            }
            $dialogs += @(Close-StrayDialog -Proc $proc)
            Start-Sleep -Milliseconds 200
        }
        $sw.Stop()
        $seconds = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        Write-Host ('[badpython] state  = ' + $state + ' after ' + $seconds + ' s')
        Write-Host ('[badpython] reason = ' + $reason)
        foreach ($line in @($log -split "`r?`n")) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { Write-Host ('[badpython] log    = ' + $line) }
        }
        if ($dialogs.Count -gt 0) {
            Write-Host ('[badpython] windows closed = ' + (($dialogs | Select-Object -Unique) -join ' | '))
        }

        Add-Step 'a runtime that cannot be started is told inside the budget' (
            $told -and ($sw.Elapsed.TotalSeconds -le $BudgetSeconds)) (
            'told = ' + $told + ' in ' + $seconds + ' s (budget ' + $BudgetSeconds +
            ' s, design 20-5 (1) measured 60 s of silence)')
        Add-Step 'the refusal carries a one line reason' (
            -not [string]::IsNullOrWhiteSpace($reason)) $reason
        Add-Step 'no modal of the shell was left on the screen' ($dialogs.Count -eq 0) (
            'windows closed = ' + $dialogs.Count)
        $children = @(Get-WrapperPids -Port $Port)
        Add-Step 'the failed start left no child behind' ($children.Count -eq 0) ('children=' + $children.Count)
        Add-Step 'the failed start never opened the port' (-not (Test-PortListening -Port $Port)) ('port ' + $Port)
    } catch {
        Add-Step 'the bad python probe runs to the end' $false $_.Exception.Message
    } finally {
        if ($null -ne $proc) {
            $strays = @(Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*'))
            Add-Step 'the bad python run leaves nothing behind' ($strays.Count -eq 0) ('strays=' + $strays.Count)
        }
        if (-not $KeepDataDir) {
            Remove-PrivateTree -Root $RuntimeRootPath
            if (Test-Path -LiteralPath $DataDir) {
                Remove-Item -LiteralPath $DataDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# ================================================================= l. the delete-the-cache button

function Invoke-CacheButtonProbe {
    <#
      .SYNOPSIS
        decisions 90 Q-E2 (3) -- the "delete the download cache" button really empties it.
      .DESCRIPTION
        The cache is what the acceptance line "<= 9.0 GB after unpacking" is measured WITHOUT
        (cu126 2.77 GB / rocm 1.37 GB), so there has to be a way to take it away by hand as well as
        the automatic sweep after the first shot (step i). No server is started here: the button only
        has to walk the data directory.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Variant,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$DataDir
    )

    $proc = $null
    try {
        $null = New-LauncherFixture -DataDir $DataDir -Variant $Variant -Port $Port
        $cacheDir = Join-Path $DataDir 'cache'
        $seeded = New-CacheFixture -CacheDir $cacheDir

        $proc = Start-Launcher -Exe $Exe -AppDir $AppDir -RuntimeRoot $RuntimeRoot -DataDir $DataDir
        $win = Get-MainWindow -Proc $proc -TimeoutSeconds 60

        $ids = @(
            'SettingsClearCacheButton', 'SettingsClearDownloadCacheButton', 'SettingsCacheClearButton',
            'SettingsDeleteCacheButton', 'StatusClearCacheButton', 'MainClearCacheButton')
        $button = $null
        foreach ($tab in @('TabSettings', 'TabStatus')) {
            $null = Select-Tab -Window $win -TabId $tab
            $button = Find-ByIdAny -Root $win -Id $ids -TimeoutSeconds 1
            if ($null -eq $button) { $button = Find-ByNameLike -Root $win -Text $T.ClearCache -TimeoutSeconds 2 }
            if ($null -ne $button) {
                Write-Host ('[cache] found on ' + $tab + ' :: ' + (Get-ElementLabel $button))
                break
            }
        }
        Add-Step 'the delete-the-download-cache button is on screen' ($null -ne $button) (
            Get-ElementLabel $button)
        if ($null -eq $button) { return }

        Add-Step 'the button can be pressed while the cache holds bytes' (
            [bool]$button.Current.IsEnabled) ('enabled = ' + $button.Current.IsEnabled +
            ', cache = ' + [string]$seeded + ' B')

        $pressed = Invoke-Element -Element $button
        Add-Step 'the button answers a press' $pressed (Get-ElementLabel $button)
        # A confirmation is allowed (this deletes files) -- answer it and say that it was there.
        $confirmed = Complete-MessageBox -Proc $proc -ButtonId '1' -TimeoutSeconds 2
        if ($confirmed) { Write-Host '[cache] a confirmation box was answered with the first button' }

        $gone = Wait-ForDirEmpty -Path $cacheDir -TimeoutSeconds 20
        $sweep = [string]$seeded + ' B -> ' + [string]$gone.bytes + ' B in ' +
            [string][math]::Round($gone.elapsed, 2) + ' s'
        Write-Host ('[cache] ' + $sweep)
        Add-Step 'pressing it empties the download cache' $gone.ok $sweep
        Add-Step 'the data directory itself survives the sweep' (Test-Path -LiteralPath $DataDir) $DataDir
    } catch {
        Add-Step 'the cache button probe runs to the end' $false $_.Exception.Message
    } finally {
        if ($null -ne $proc) {
            $strays = @(Stop-Launcher -Proc $proc -StrayCommandLinePattern @('*ywk_server*'))
            Add-Step 'the cache button run leaves nothing behind' ($strays.Count -eq 0) ('strays=' + $strays.Count)
        }
        if ((-not $KeepDataDir) -and (Test-Path -LiteralPath $DataDir)) {
            Remove-Item -LiteralPath $DataDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Set-SettingsValue {
    <#
      .SYNOPSIS
        Write one member into settings.json (UTF-8, no BOM -- the launcher writes it that way).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Value
    )
    $doc = Read-SettingsDoc -Path $Path
    if ($null -eq $doc) { throw ('settings.json is not there: ' + $Path) }
    $doc | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
    $json = $doc | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
    return $Path
}

function Set-RuntimeStamp {
    <#
      .SYNOPSIS
        Write ONE variant's entry into a per-variant table in settings.json (decisions 91).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Member,
        [Parameter(Mandatory = $true)][string]$Variant,
        [Parameter(Mandatory = $true)][string]$Value
    )
    $doc = Read-SettingsDoc -Path $Path
    if ($null -eq $doc) { throw ('settings.json is not there: ' + $Path) }
    $table = Get-JsonMember -Doc $doc -Name $Member
    if ($null -eq $table) { $table = New-Object psobject }
    $table | Add-Member -NotePropertyName $Variant -NotePropertyValue $Value -Force
    $doc | Add-Member -NotePropertyName $Member -NotePropertyValue $table -Force
    $json = $doc | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
    return $Path
}

# ================================================================= preflight

Write-Host '=== preflight ==='
Write-Host ('repo        = ' + $repo)
Write-Host ('exe         = ' + $Exe)
Write-Host ('app dir     = ' + $AppDir)
Write-Host ('runtime     = ' + $RuntimeRoot)
Write-Host ('data dir    = ' + $DataDir)
Write-Host ('variant     = ' + $Variant + '   port = ' + $Port)

if ($DryRun) {
    # -DryRun is the empty run: it opens no window, starts nothing, writes nothing, and only
    # prints what the round three steps WOULD make. Use it to check the script on a machine
    # whose launcher is being rebuilt by another seat.
    Write-Host ''
    Write-Host '=== dry run: the plan ==='
    Write-Host ('h  wizard presses   port=' + $WizardPort + '  data=' + $DataDir + '-wizard')
    Write-Host ('                    app tree=' + (Join-Path $env:TEMP 'ywk-d-launch-probe-app') +
        '  (junctions to server/ licenses/ voices/, a broken copy of ledger/runtime-' + $Variant + '.json)')
    Write-Host ('                    full run=' + [bool]$WizardFullRun + '  (a full run FETCHES: E2E seat only)')
    Write-Host ('j  ledger stamp     port=' + $LedgerPort + '  data=' + $DataDir + '-ledger')
    Write-Host ('                    app tree=' + (Join-Path $env:TEMP 'ywk-d-launch-probe-app-ledger'))
    Write-Host ('k  bad python.exe   port=' + $BadPythonPort + '  data=' + $DataDir + '-badpython')
    Write-Host ('                    runtime root=' + (Join-Path $env:TEMP 'ywk-d-launch-probe-badruntime') +
        '  budget=' + $BadPythonBudgetSeconds + ' s')
    Write-Host ('l  cache button     port=' + $CachePort + '  data=' + $DataDir + '-cache')
    Write-Host ('i  cache after 200  cache=' + (Join-Path $DataDir 'cache') + '  (seeded, then read in bytes)')
    Write-Host ''
    Write-Host 'nothing was touched.'
    exit 0
}

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

# ================================================================= d. the variant gate (88 (1)(2))
#
# Run before the long one: it costs about 5 seconds, loads no model, opens no port, and if the gate
# is broken there is no point measuring anything else.
# -RoundThreeOnly is the quick run of the four windows convoy D (3) added, so it skips the gate too.
if ((-not $SkipGate) -and (-not $RoundThreeOnly)) {
    Write-Host ''
    Write-Host '=== d. the variant gate (decisions 88 (1)(2)) ==='
    Add-Step 'the gate port is free' (-not (Test-PortListening -Port $GatePort)) ('port ' + $GatePort)
    $gateData = $DataDir + '-gate'
    $gateRoot = Join-Path $env:TEMP 'ywk-d-launch-probe-gate-runtime'
    $cpuRuntime = Join-Path $RuntimeRoot 'runtime-cpu'
    if (-not (Test-Path -LiteralPath (Join-Path $cpuRuntime 'python.exe'))) {
        $cpuRuntime = Join-Path $RuntimeRoot 'cpu'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $cpuRuntime 'python.exe'))) {
        Add-Step 'the gate has an interpreter without a GPU to point at' $false (
            'no cpu runtime under ' + $RuntimeRoot)
    } else {
        $null = New-GateRuntimeRoot -Root $gateRoot -Variant $GateVariant -Target $cpuRuntime
        Invoke-GateProbe -Variant $GateVariant -Port $GatePort -DataDir $gateData `
            -RuntimeRoot $gateRoot -ExpectSuggestion $Variant
    }

}

if ($GateOnly) {
    Write-Host ''
    Write-Host ('=== ' + $script:Failures.Count + ' failure(s) of ' + $script:Steps.Count + ' ===')
    foreach ($f in $script:Failures) { Write-Host ('  ' + $f) }
    exit $script:Failures.Count
}

# ================================================================= round three (92 / 94)
#
# Four windows, one after the other. None of them loads a model, none of them opens a port, and
# none of them fetches anything (-WizardFullRun is the one exception and it belongs to the E2E
# seat). Each stage is wrapped in its own try/catch, so a screen that has not been written yet
# goes red on its own line instead of taking the run down.
if (-not $SkipRoundThree) {
    Write-Host ''
    Write-Host '=== h. the first run wizard, counted in presses (decisions 94 (1)) ==='
    Invoke-WizardPressProbe -Variant $Variant -Port $WizardPort -DataDir ($DataDir + '-wizard') `
        -AppTreeRoot (Join-Path $env:TEMP 'ywk-d-launch-probe-app') -FullRun:$WizardFullRun

    Write-Host ''
    Write-Host '=== j. the runtime ledger stamp (decisions 91) ==='
    Invoke-LedgerStampProbe -Variant $Variant -Port $LedgerPort -DataDir ($DataDir + '-ledger') `
        -AppTreeRoot (Join-Path $env:TEMP 'ywk-d-launch-probe-app-ledger')

    Write-Host ''
    Write-Host '=== k. a python.exe that cannot be started (design 20-5 (1)) ==='
    Invoke-BadPythonProbe -Variant $Variant -Port $BadPythonPort -DataDir ($DataDir + '-badpython') `
        -RuntimeRootPath (Join-Path $env:TEMP 'ywk-d-launch-probe-badruntime') `
        -BudgetSeconds $BadPythonBudgetSeconds

    Write-Host ''
    Write-Host '=== l. the delete-the-download-cache button (decisions 90 Q-E2 (3)) ==='
    Invoke-CacheButtonProbe -Variant $Variant -Port $CachePort -DataDir ($DataDir + '-cache')
}

if ($RoundThreeOnly) {
    Write-Host ''
    Write-Host ('=== ' + $script:Failures.Count + ' failure(s) of ' + $script:Steps.Count + ' ===')
    foreach ($f in $script:Failures) { Write-Host ('  ' + $f) }
    exit $script:Failures.Count
}

$null = New-LauncherFixture -DataDir $DataDir -Variant $Variant -Port $Port -HfHome $HfHome

# i (decisions 90 Q-E2 (3) / 94 (3)): the download cache is what the acceptance line "<= 9.0 GB
# after unpacking" is measured WITHOUT, and the ruling is that the launcher takes it away after
# the first synthesis that came back 200. This run downloads nothing, so the cache it must
# sweep is seeded by hand and read in BYTES -- before the shot and after it.
$script:CacheDir = Join-Path $DataDir 'cache'
$script:CacheSeedBytes = [long]0
if (-not $SkipSynthesis) {
    $script:CacheSeedBytes = New-CacheFixture -CacheDir $script:CacheDir
}

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

    # ========================================================= g. the presets arrive (88 (5))
    #
    # build/assemble-app.ps1 now copies voices/presets.json and voices/presets/*.wav into the app
    # tree, so a fresh data directory must end up with every status=done preset in three places:
    # the launcher's own table (voices.ywk.json, with the mark), the list on screen, and -- this is
    # the part that was missing -- the alias table the wrapper reads (voices.json). Without the
    # third one the presets are visible and unusable: no /v1/audio/voices row, no synthesis.
    Write-Host ''
    Write-Host '=== g. the preset voices arrive from the app tree (decisions 88 (5)) ==='
    # decisions 92: THE LITERAL 11 IS GONE. What the tree must carry is every status=done row of its
    # own presets.json -- the number moves the day convoy P finishes another speaker, and a probe with
    # 11 written into it would go red on a correct tree (and, worse, stay green on a tree that lost a
    # row while gaining one). So the expected count is read out of the same file, and the two ways a
    # row can be unusable are counted apart: no secondary wav in the table, and a wav the table names
    # that is not in the tree.
    $presetsJson = Join-Path $AppDir 'voices\presets.json'
    $presetNames = @()
    $presetDone = 0
    $presetNoWav = @()
    $presetMissing = @()
    if (Test-Path -LiteralPath $presetsJson) {
        $presetDoc = Get-Content -LiteralPath $presetsJson -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($row in @($presetDoc.presets)) {
            if ($row.status -ne 'done') { continue }
            $presetDone++
            if ($null -eq $row.secondary) { $presetNoWav += [string]$row.id; continue }
            if ([string]::IsNullOrWhiteSpace([string]$row.secondary.file)) {
                $presetNoWav += [string]$row.id
                continue
            }
            $presetNames += [string]$row.display_name
            $wav = Join-Path $AppDir ('voices\presets\' + [string]$row.secondary.file)
            if (-not (Test-Path -LiteralPath $wav)) { $presetMissing += [string]$row.secondary.file }
        }
    }
    Write-Host ('[presets] status=done rows = ' + $presetDone +
        ', with a secondary wav = ' + $presetNames.Count)
    Add-Step 'the app tree carries every status=done preset of its own presets.json' (
        ($presetDone -ge 1) -and ($presetNames.Count -eq $presetDone)) (
        'status=done = ' + $presetDone + ', with a secondary wav = ' + $presetNames.Count +
        ', without one = ' + ($presetNoWav -join ', '))
    Add-Step 'every preset wav the table names is in the app tree' ($presetMissing.Count -eq 0) (
        'missing = ' + ($presetMissing -join ', '))

    $ywkTable = Join-Path $DataDir 'voices\voices.ywk.json'
    $marked = $false
    $inTable = 0
    if (Test-Path -LiteralPath $ywkTable) {
        $ywkDoc = Get-Content -LiteralPath $ywkTable -Raw -Encoding UTF8 | ConvertFrom-Json
        # The mark is snake_case on disk (Contracts/IVoiceStore.cs: [JsonPropertyName("presets_installed")]).
        if ($ywkDoc.PSObject.Properties.Name -contains 'presets_installed') {
            $marked = [bool]$ywkDoc.presets_installed
        }
        foreach ($name in $presetNames) {
            if ($ywkDoc.voices.PSObject.Properties.Name -contains $name) { $inTable++ }
        }
    }
    Add-Step 'PresetsInstalled is marked and every preset is in the table' (
        $marked -and ($inTable -eq $presetNames.Count) -and ($presetNames.Count -gt 0)) (
        'presetsInstalled=' + $marked + ' in table=' + $inTable + '/' + $presetNames.Count)

    # The alias table the wrapper reads (contract (4) 4-1).
    $aliasPath = Join-Path $DataDir 'voices\voices.json'
    $inAlias = 0
    if (Test-Path -LiteralPath $aliasPath) {
        $aliasDoc = Get-Content -LiteralPath $aliasPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($name in $presetNames) {
            if ($aliasDoc.PSObject.Properties.Name -contains $name) { $inAlias++ }
        }
    }
    Add-Step 'every preset also reaches the alias table the wrapper reads' (
        ($inAlias -eq $presetNames.Count) -and ($presetNames.Count -gt 0)) (
        'in voices.json = ' + $inAlias + '/' + $presetNames.Count)

    $null = Select-Tab -Window $win -TabId 'TabVoices'
    $presetRows = @(Get-GridRows -Root $win -Id 'VoicesGrid')
    $presetKind = @($presetRows | Where-Object { $_.cells -contains $T.Preset })
    Write-Host ('[presets] rows on screen = ' + $presetRows.Count + '  marked preset = ' + $presetKind.Count)
    Add-Step 'the list shows the default voice plus every preset' (
        ($presetRows.Count -eq ($presetNames.Count + 1)) -and ($presetKind.Count -eq $presetNames.Count)) (
        'rows=' + $presetRows.Count + ' preset rows=' + $presetKind.Count)

    # ========================================================= f. the remove button says why (low 14)
    #
    # The always-present "default" voice cannot be removed. The reason has to be readable without
    # looking at the screen: the same sentence is on the button's HelpText and on the line under it.
    $null = Select-GridRow -Root $win -Id 'VoicesGrid' -CellText $T.DefVoice
    $blocked = Get-TextById -Root $win -Id 'VoicesRemoveBlockedText'
    $help = Get-HelpTextById -Root $win -Id 'VoicesRemoveButton'
    $removeBtn = Find-ById -Root $win -Id 'VoicesRemoveButton' -TimeoutSeconds 10
    $removeEnabled = $true
    if ($null -ne $removeBtn) { $removeEnabled = [bool]$removeBtn.Current.IsEnabled }
    Write-Host ('[voices] remove blocked = ' + $blocked)
    Write-Host ('[voices] remove helptext = ' + $help)
    Add-Step 'the blocked remove says why, in the same words, through UIA' (
        (-not [string]::IsNullOrWhiteSpace($blocked)) -and ($blocked -eq $help) -and
        ($blocked -like ('*' + $T.DefVoice + '*'))) ([string]$blocked)
    Add-Step 'the blocked remove button is not pressable' (-not $removeEnabled) (
        'enabled=' + $removeEnabled)
    $null = Select-Tab -Window $win -TabId 'TabStatus'

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
    # PORT FIRST (decisions 105): the same loop also records the FIRST /health 200 and whether the
    # runtime was still loading at that moment. Before 105 the port stayed shut for the whole load
    # (20-70 s) and this measurement did not exist; now it is the acceptance row (<= 3 s).
    $seenLog = New-Object System.Collections.Generic.HashSet[string]
    $startWait = Get-Date
    $healthAt = $null
    $healthWhileLoading = $false
    $loadedAt = $null
    $ready = $null
    while ($true) {
        if ($null -eq $healthAt) {
            try {
                $h = Invoke-WebRequest -Uri ('http://127.0.0.1:' + $Port + '/health') `
                    -TimeoutSec 2 -UseBasicParsing
                if ($h.StatusCode -eq 200) { $healthAt = ((Get-Date) - $startWait).TotalSeconds }
            } catch {
                $healthAt = $null
            }
        }
        if (($null -ne $healthAt) -and ($null -eq $loadedAt)) {
            # KEEP ASKING until the answer is known (correction seat, convoy G). Reading
            # runtime.loaded once, in the same turn that first saw /health 200, latched an
            # unrecoverable $false whenever that single call timed out. The window is a state, not
            # an instant: any turn that answers loaded=false BEFORE $loadedAt is set is proof the
            # port was open while the model was still loading.
            try {
                $rt = (Invoke-RestMethod `
                        -Uri ('http://127.0.0.1:' + $Port + '/ywk/status') -TimeoutSec 5).runtime
                if ([bool]$rt.loaded) {
                    $loadedAt = ((Get-Date) - $startWait).TotalSeconds
                } else {
                    $healthWhileLoading = $true
                }
            } catch {
                $loadedAt = $null
            }
        }

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

    # ------------------------------------------------- port first (decisions 105)
    #
    # The acceptance row: /health 200 within 10 s of the press (measured 8.9 s from the exe, decisions 106; the wrapper binds and loads in the
    # background), and runtime.loaded true later. The polling loop above is a 400 ms one that also
    # reads two UI Automation properties per turn, so the measurement is coarse by design -- it is
    # here to catch "the port stayed shut for the whole load", not to time anything to the ms.
    Write-Host ('[105] /health 200 after ' +
        (& { if ($null -eq $healthAt) { 'never' } else { [math]::Round($healthAt, 1).ToString() + ' s' } }) +
        '  (loading at that moment = ' + $healthWhileLoading + ')  runtime.loaded after ' +
        (& { if ($null -eq $loadedAt) { 'never' } else { [math]::Round($loadedAt, 1).ToString() + ' s' } }))
    Add-Step 'the port answers /health 200 within 10 s of the press' (
        ($null -ne $healthAt) -and ($healthAt -le 10.0)) (
        'first 200 at ' + (& { if ($null -eq $healthAt) { 'never' } else { [math]::Round($healthAt, 2).ToString() + ' s' } }))
    # Two ways to see the same thing, and either is enough: a turn that answered loaded=false while
    # the port was already up, or a $loadedAt that is later than $healthAt. The second is the escape
    # hatch for a load that finished inside one 400 ms turn (correction seat, convoy G).
    $loadedAfterHealth = ($null -ne $healthAt) -and ($null -ne $loadedAt) -and ($loadedAt -gt $healthAt)
    Add-Step 'the model was still loading when the port first answered' (
        $healthWhileLoading -or $loadedAfterHealth) (
        'saw loaded=false with the port up = ' + $healthWhileLoading +
        ', loaded after the first 200 = ' + $loadedAfterHealth)
    Add-Step 'runtime.loaded turns true later' ($null -ne $loadedAt) (
        'loaded at ' + (& { if ($null -eq $loadedAt) { 'never' } else { [math]::Round($loadedAt, 1).ToString() + ' s' } }) +
        ' (limit ' + $ReadyTimeoutSeconds + ' s)')

    # ------------------------------------------------- the answer names the child that gave it
    #
    # Correction seat, convoy D (2): a stranger that already holds the port answers in this very
    # shape. (Decisions 105 shortened the window -- our own child now binds within a second instead
    # of after the 20-28 s load -- but it did not close it: the stranger keeps the port and our
    # child dies on bind.) /ywk/status therefore carries pid, and the launcher drops any sample
    # whose pid is not the child it started. Here the two must agree -- if they ever do not, the
    # band is showing someone else's GPU memory.
    $statusPid = $null
    try {
        $statusPid = (Invoke-RestMethod -Uri ('http://127.0.0.1:' + $Port + '/ywk/status') -TimeoutSec 10).pid
    } catch {
        $statusPid = $null
    }
    $childPids = @(@(Get-WrapperPids -Port $Port) | ForEach-Object { [int]$_.ProcessId })
    Add-Step 'the status answer names the pid of the child we started' (
        ($null -ne $statusPid) -and ($childPids -contains [int]$statusPid)) (
        'status.pid=' + $statusPid + ' children=' + ($childPids -join ','))

    Start-Sleep -Seconds 3
    Write-Host ('[status] device  = ' + (Get-TextById -Root $win -Id 'StatusDeviceText'))
    Write-Host ('[status] gpu     = ' + (Get-TextById -Root $win -Id 'StatusGpuText'))
    Write-Host ('[status] variant = ' + (Get-TextById -Root $win -Id 'StatusVariantText'))
    Write-Host ('[status] memory  = ' + (Get-TextById -Root $win -Id 'StatusMemoryText'))
    Write-Host ('[status] warmup  = ' + (Get-TextById -Root $win -Id 'StatusWarmupText'))

    # ========================================================= a. the memory band (87 (1) / 67 (3))
    #
    # The band reads used = memory.allocated, reserved = memory.reserved, whole = gpu_used/gpu_total
    # (decisions 87 (1)). What is checked here is that all four numbers really arrived: a null must
    # print as an em dash, never as 0 B, so a band that still says "not supported" or carries a dash
    # is a failure, not a formatting choice.
    Write-Host ''
    Write-Host '=== a. the GPU memory band carries numbers (decisions 87 (1) / 67 (3)) ==='
    $mem = Wait-ForPattern -Root $win -Id 'StatusMemoryText' -Pattern ($T.Used + ' \d') -TimeoutSeconds 20 -IntervalMilliseconds 500
    Write-Host ('[memory] ' + $mem.text)
    $memOk = $mem.ok `
        -and ($mem.text -match ($T.Used + ' \d')) `
        -and ($mem.text -match ($T.Reserved + ' \d')) `
        -and ($mem.text -match ('GPU ' + $T.Whole + ' \d')) `
        -and ($mem.text -notlike ('*' + $T.Unsupported + '*')) `
        -and ($mem.text -notlike ('*' + $T.Dash + '*'))
    Add-Step 'the memory band shows allocated, reserved and the whole card' $memOk ([string]$mem.text)

    # ========================================================= b. the latent cache line (67 (1))
    $cache = Get-TextById -Root $win -Id 'StatusLatentCacheText'
    if ($null -eq $cache) { $cache = '' }
    Write-Host ('[latent] ' + $cache)
    Add-Step 'the latent cache line says ON/OFF and how many voices are baked' (
        (($cache -like 'ON*') -or ($cache -like 'OFF*')) -and
        ($cache -match ($T.Baked + ' \d'))) $cache

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
    # Still the upstream's own line (runtime.py) -- decisions 105 only moved it AFTER the uvicorn
    # line, it did not remove it. The wrapper adds its own "runtime load started" ahead of both.
    Add-Step 'log has the runtime load started line' ($log -match 'runtime load started') 'ywk_server: runtime load started ...'
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

        # i (decisions 90 Q-E2 (3) / 94 (3)) -- the cache must survive UNTIL the first 200.
        # Sweeping it at start up would be wrong: the .part files are what lets an interrupted first
        # run carry on from where it stopped, and a run that never synthesised has proved nothing.
        $cacheBeforeShot = Get-DirBytes -Path $script:CacheDir
        Add-Step 'the download cache is still there when the first shot is fired' (
            $cacheBeforeShot -eq $script:CacheSeedBytes) (
            'cache = ' + [string]$cacheBeforeShot + ' B of the ' + [string]$script:CacheSeedBytes +
            ' B that were seeded')

        $shot1 = Invoke-Shot -Window $win -TimeoutSeconds 120
        Write-Host ('[try] result  = ' + $shot1.result)
        Write-Host ('[try] message = ' + $shot1.message)
        Add-Step 'one shot with the default voice returns audio' $shot1.ok (
            [string]$shot1.result + ' / ' + [string]$shot1.message)

        if ($shot1.ok) {
            $swept = Wait-ForDirEmpty -Path $script:CacheDir -TimeoutSeconds 30
            Write-Host ('[cache] after the first 200 = ' + [string]$swept.bytes + ' B after ' +
                [string][math]::Round($swept.elapsed, 2) + ' s')
            Add-Step 'the first shot that came back 200 takes the download cache away' $swept.ok (
                [string]$script:CacheSeedBytes + ' B -> ' + [string]$swept.bytes + ' B in ' +
                [string][math]::Round($swept.elapsed, 2) + ' s')
        }

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

            # ================================================= c. the estimate turns into a measurement
            #
            # decisions 67 (2) / 87 (1): while a voice is a wav reference the screen may only show a
            # coefficient, and it must say so ("estimate, before measurement"). Once the .pt is baked
            # (decisions 65 / 76) the same line has to switch to the REAL size out of
            # /ywk/status.memory.latents[<voice id>]. Adding a voice starts that bake by itself
            # (VoicesViewModel.AddAsync posts the precompute for the new voice), so this step only
            # has to wait for it and refresh the list -- latent/latent_stale come from /ywk/voices.
            $null = Select-GridRow -Root $win -Id 'VoicesGrid' -CellText $T.NewVoice
            $memBefore = Get-TextById -Root $win -Id 'VoicesSelectedMemoryText'
            if ($null -eq $memBefore) { $memBefore = '' }
            Write-Host ('[memory] per voice before = ' + $memBefore)

            $memAfter = ''
            $bakeStart = Get-Date
            for ($i = 0; $i -lt 30; $i++) {
                $null = Invoke-ButtonById -Root $win -Id 'VoicesRefreshButton'
                Start-Sleep -Milliseconds 1500
                $null = Select-GridRow -Root $win -Id 'VoicesGrid' -CellText $T.NewVoice
                $memAfter = Get-TextById -Root $win -Id 'VoicesSelectedMemoryText'
                if ($null -eq $memAfter) { $memAfter = '' }
                if (($memAfter -like ('*' + $T.LatentRef + '*')) -and ($memAfter -like ('*' + $T.Measured + '*'))) { break }
                Start-Sleep -Milliseconds 1500
            }
            $bakeSeconds = ((Get-Date) - $bakeStart).TotalSeconds
            Write-Host ('[memory] per voice after  = ' + $memAfter + '   (' + [math]::Round($bakeSeconds, 1) + ' s)')

            # decisions 92 -- TIGHTEN THIS. Until convoy D (3) the step passed on any line that
            # carried either word, so a screen that said "measured" with nothing baked passed, and any
            # number at all passed for the coefficient. What the line has to carry now is the WORD
            # (estimate) and the COEFFICIENT itself: MemoryEstimate.WavReferenceBytes = 751,619,276 B
            # and UiText.Bytes renders it as "716.8 MB" (751619276 / 1024 / 1024 = 716.80). The unit is
            # matched loosely on purpose -- the GB/GiB spelling of UiText is one of the low items
            # convoy D (3) may still change, and the FIGURE is what this step is about.
            $coefficient = '716\.8\s*M(i)?B'
            $beforeIsEstimate = ($memBefore -like ('*' + $T.PerVoice + '*')) -and
                ($memBefore -like ('*' + $T.Estimate + '*')) -and
                ($memBefore -like ('*' + $T.Provision + '*')) -and
                ($memBefore -match $coefficient)
            # If the bake landed while the list was being read, the measured line is fine here too --
            # what must never appear is a bare number with no word for which of the two it is.
            $beforeIsMeasured = ($memBefore -like ('*' + $T.PerVoice + '*')) -and
                ($memBefore -like ('*' + $T.LatentRef + '*')) -and
                ($memBefore -like ('*' + $T.Measured + '*')) -and
                ($memBefore -notlike ('*' + $T.Provision + '*'))
            Add-Step 'the per voice line says "estimate" and carries the wav coefficient until the .pt exists' (
                $beforeIsEstimate -or $beforeIsMeasured) (
                $memBefore + '  [wanted ' + $coefficient + ' or a measured line]')
            Add-Step 'the baked voice switches to the measured .pt size' (
                ($memAfter -like ('*' + $T.LatentRef + '*')) -and
                ($memAfter -like ('*' + $T.Measured + '*')) -and
                ($memAfter -notlike ('*' + $T.Provision + '*')) -and
                ($memAfter -notmatch $coefficient) -and
                ($memAfter -match '\d')) (
                $memAfter + ' in ' + [math]::Round($bakeSeconds, 1) + ' s')

            $null = Select-Tab -Window $win -TabId 'TabStatus'
            $cacheAfter = Get-TextById -Root $win -Id 'StatusLatentCacheText'
            if ($null -eq $cacheAfter) { $cacheAfter = '' }
            Write-Host ('[latent] after the bake = ' + $cacheAfter)
            $bakedCount = 0
            if ($cacheAfter -match ($T.Baked + ' (\d+)')) { $bakedCount = [int]$Matches[1] }
            Add-Step 'the latent cache line counts the baked voices' ($bakedCount -ge 1) (
                'baked = ' + $bakedCount + ' :: ' + $cacheAfter)
            $null = Select-Tab -Window $win -TabId 'TabVoices'

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

    # ============================================================= e. liveness (decisions 88 (3))
    #
    # decisions 83: a wrapper can pass /health, answer /params and then vanish on the first
    # synthesis with 0xC0000005 -- no python traceback, no server log, just ConnectionResetError on
    # the client. So the launcher must watch the CHILD (Process.Exited), not only the /ywk/status
    # sample. Two shapes are driven here, each with a real external kill:
    #   e-1  idle: the band must say "failed" with a one line reason within 3 seconds
    #   e-2  mid synthesis: the try screen must say the server went down at once, instead of sitting
    #        on the HTTP deadline (which is the ready timeout, 120 s by default)
    if (-not $SkipLiveness) {
        Write-Host ''
        Write-Host '=== e-1. the child is killed from outside while idle (decisions 88 (3)) ==='
        $again = Start-AndWaitReady -Window $win -TimeoutSeconds $ReadyTimeoutSeconds
        Add-Step 'the server comes back after a stop' $again.ok (
            'ready in ' + [math]::Round($again.elapsed, 1) + ' s, state=' + $again.text)

        if ($again.ok) {
            $killed = @(Stop-WrapperChild -Port $Port)
            Add-Step 'the probe found the wrapper child to kill' ($killed.Count -ge 1) (
                'children=' + $killed.Count)
            $dead = Wait-ForPattern -Root $win -Id 'MainStateText' -Pattern $T.Failed `
                -TimeoutSeconds 10 -IntervalMilliseconds 150
            $deadReason = Get-TextById -Root $win -Id 'StatusReasonText'
            if ($null -eq $deadReason) { $deadReason = '' }
            Write-Host ('[liveness] state=' + $dead.text + ' after ' + [math]::Round($dead.elapsed, 2) + ' s')
            Write-Host ('[liveness] reason=' + $deadReason)
            Add-Step 'a killed child turns the band to failed within 3 s' (
                $dead.ok -and ($dead.elapsed -le 3.0)) (
                'failed in ' + [math]::Round($dead.elapsed, 2) + ' s (limit 3 s), state=' + $dead.text)
            Add-Step 'the failure carries a one line reason' (
                -not [string]::IsNullOrWhiteSpace($deadReason)) $deadReason
            Add-Step 'the port is released when the child dies' (
                -not (Test-PortListening -Port $Port)) ('port ' + $Port)
        }

        Write-Host ''
        Write-Host '=== e-2. the child is killed during a synthesis (decisions 88 (3)) ==='
        $third = Start-AndWaitReady -Window $win -TimeoutSeconds $ReadyTimeoutSeconds
        Add-Step 'the server comes back after a crash' $third.ok (
            'ready in ' + [math]::Round($third.elapsed, 1) + ' s, state=' + $third.text)

        if ($third.ok -and (-not $SkipSynthesis)) {
            $null = Select-Tab -Window $win -TabId 'TabTry'
            $null = Select-ComboItemById -Root $win -Id 'TryVoiceCombo' -ItemText $T.DefVoice
            $null = Set-TextById -Root $win -Id 'TryInputBox' -Text $T.LongLine
            # 40 steps on a long line: the shot must still be running when the kill lands.
            $null = Invoke-ButtonById -Root $win -Id 'TryStepsPreset40'
            $null = Invoke-ButtonById -Root $win -Id 'TrySynthesizeButton'
            Start-Sleep -Milliseconds 1200

            $shotKill = [System.Diagnostics.Stopwatch]::StartNew()
            $killed2 = @(Stop-WrapperChild -Port $Port)
            $told = Wait-ForPattern -Root $win -Id 'TryMessageText' -Pattern ([regex]::Escape($T.ServerDown)) `
                -TimeoutSeconds 20 -IntervalMilliseconds 150
            $shotKill.Stop()
            Write-Host ('[liveness] try message = ' + $told.text)
            Add-Step 'the probe killed the child mid synthesis' ($killed2.Count -ge 1) (
                'children=' + $killed2.Count)
            # The HTTP deadline is the ready timeout; being told inside 20 s proves the try screen
            # is cut by the state machine and not by the socket.
            Add-Step 'a synthesis is told at once that the server went down' (
                $told.ok -and ($told.elapsed -lt [math]::Min(20, $ReadyTimeoutSeconds))) (
                'told in ' + [math]::Round($told.elapsed, 2) + ' s (deadline was ' + $ReadyTimeoutSeconds + ' s) :: ' + $told.text)
            $null = Select-Tab -Window $win -TabId 'TabStatus'
        }
    }
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
