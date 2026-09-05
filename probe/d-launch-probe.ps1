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
# It never touches 8088 / 7861 / 18088, never writes to %LOCALAPPDATA%\irodori-tts-ywk, and always
# tree-kills what it started (even on failure -- the teardown is in a finally block).
#
# Usage:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File probe/d-launch-probe.ps1
#   pwsh ... -File probe/d-launch-probe.ps1 -Port 18096 -Variant cpu -ReadyTimeoutSeconds 300
#   pwsh ... -File probe/d-launch-probe.ps1 -BadDeviceOnly     (D-1 only: cuda:9 must fail in <= 15 s)
#   pwsh ... -File probe/d-launch-probe.ps1 -GateOnly          (the variant gate only, no model load)
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

# ================================================================= d. the variant gate (88 (1)(2))
#
# Run before the long one: it costs about 5 seconds, loads no model, opens no port, and if the gate
# is broken there is no point measuring anything else.
if (-not $SkipGate) {
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

    # ========================================================= g. the presets arrive (88 (5))
    #
    # build/assemble-app.ps1 now copies voices/presets.json and voices/presets/*.wav into the app
    # tree, so a fresh data directory must end up with every status=done preset in three places:
    # the launcher's own table (voices.ywk.json, with the mark), the list on screen, and -- this is
    # the part that was missing -- the alias table the wrapper reads (voices.json). Without the
    # third one the presets are visible and unusable: no /v1/audio/voices row, no synthesis.
    Write-Host ''
    Write-Host '=== g. the preset voices arrive from the app tree (decisions 88 (5)) ==='
    $presetsJson = Join-Path $AppDir 'voices\presets.json'
    $presetNames = @()
    if (Test-Path -LiteralPath $presetsJson) {
        $presetDoc = Get-Content -LiteralPath $presetsJson -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($row in @($presetDoc.presets)) {
            if ($row.status -ne 'done') { continue }
            if ($null -eq $row.secondary) { continue }
            if ([string]::IsNullOrWhiteSpace([string]$row.secondary.file)) { continue }
            $presetNames += [string]$row.display_name
        }
    }
    Write-Host ('[presets] app tree rows with a secondary wav = ' + $presetNames.Count)
    Add-Step 'the app tree carries the preset table' ($presetNames.Count -eq 11) (
        'status=done rows = ' + $presetNames.Count)

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

    # ------------------------------------------------- the answer names the child that gave it
    #
    # Correction seat, convoy D (2): preload=true loads the model BEFORE uvicorn binds (20-28 s on
    # this machine), so during startup a stranger can hold the port and answer in this very shape.
    # /ywk/status therefore carries pid, and the launcher drops any sample whose pid is not the
    # child it started. Here the two must agree -- if they ever do not, the band is showing someone
    # else's GPU memory.
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

            # Before the bake the line must SAY it is a coefficient ("estimate, before measurement").
            # If the bake already landed while the list was being read, the measured line is fine too
            # -- what must never appear is a bare number with no word for which of the two it is.
            Add-Step 'the per voice line names itself an estimate until the .pt exists' (
                ($memBefore -like ('*' + $T.PerVoice + '*')) -and
                (($memBefore -like ('*' + $T.Provision + '*')) -or
                 ($memBefore -like ('*' + $T.Measured + '*')))) $memBefore
            Add-Step 'the baked voice switches to the measured .pt size' (
                ($memAfter -like ('*' + $T.LatentRef + '*')) -and
                ($memAfter -like ('*' + $T.Measured + '*')) -and
                ($memAfter -notlike ('*' + $T.Provision + '*'))) (
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
