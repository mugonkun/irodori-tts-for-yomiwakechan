<#
.SYNOPSIS
    Radeon (ROCm) live-fire probe: cold MIOpen db -> warmup -> real shots -> restart -> presets.

.DESCRIPTION
    Convoy C, design doc docs/design/ben-c-radeon.md section 2-4. This script MEASURES; it
    does not build anything. It drives the runtime that build\assemble-runtime.ps1 -Variant
    rocm-gfx1151 produced and the payload that build\assemble-app.ps1 produced, and writes one
    machine-written JSON to build\out\probe-log\rocm-warmup-<stamp>.json.

    Two server processes are started, in this order:

      A (cold)   -DataDir is wiped first, so MIOpen's user db starts EMPTY. Measures
                 spawn -> GET /health 200 (acceptance C-4, "the first run on a machine"),
                 then POST /ywk/warmup with the 4/8/12 s stages plus one shot per warmup
                 speaker (C-5's shot list), then real shots: a warmed speaker's first and
                 second shot, and an UNWARMED speaker's first and second shot (C-6, the
                 "14.8 s" of decisions.md 40).
      B (warm)   killed and restarted against the SAME -DataDir, so only the on-disk MIOpen
                 db survives. Measures ready again, then the same warmed speaker with NO
                 warmup at all (C-7: does the db carry across processes?), then the priority
                 check (C-5: a real request must not wait behind more than one warmup shot),
                 then one shot per preset speaker (C-8).

    GPU memory is sampled from the Windows performance counter set "GPU Process Memory",
    filtered to the server's pid, every -GpuSampleMs. That counter is what Windows itself
    reports for the process; torch's own allocator figures are not visible from outside the
    process, so the numbers here are the process's GPU memory as the OS sees it, and the
    report says so. If the counter set is not there the field is null, not a guess.

    Stop-band: only -Port (default 18091) is opened; 8088 and 7861 are never touched. The
    Hugging Face cache is read with HF_HUB_OFFLINE=1. voices\presets\*.wav are COPIED out
    to build\out\app\voices\ -- voices\ itself is read-only here.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File probe\rocm-warmup-probe.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File probe\rocm-warmup-probe.ps1 -KeepDataDir
#>
[CmdletBinding()]
param(
    [ValidateSet('rocm-gfx1151', 'cpu', 'cu130', 'cu126')]
    [string]$Variant = 'rocm-gfx1151',
    [int]$Port = 18091,
    [string]$Device = '',
    [ValidateSet('', 'fp32', 'bf16')]
    [string]$Precision = '',
    [string]$HfHome = '',
    [string]$DataDir = '',
    # Keep whatever MIOpen db is already in -DataDir (default: wipe it, which is the point
    # of the cold run). Use this to re-measure the warm side without paying the cold one.
    [switch]$KeepDataDir,
    [string]$WarmupStages = '4,8,12',
    [string]$PriorityStages = '5,7,9,11',
    [int]$WarmupVoiceCount = 3,
    [int]$ReadyTimeoutSeconds = 900,
    [int]$GpuSampleMs = 400,
    [string]$OutRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# PowerShell 7.4 turns a native command's non-zero exit into a terminating error while
# ErrorActionPreference is Stop. taskkill.exe legitimately returns non-zero (the process had
# already gone), and every exit code that matters here is checked by hand, so switch it off.
# On Windows PowerShell 5.1 this is just an unused variable.
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot '..\build\Common.ps1')

$RepoRoot = Get-YwkRepoRoot
if ([string]::IsNullOrEmpty($OutRoot)) { $OutRoot = Join-Path $RepoRoot 'build\out' }
$RuntimeDir = Join-Path $OutRoot ('runtime-' + $Variant)
$AppDir     = Join-Path $OutRoot 'app'
$AppServer  = Join-Path $AppDir 'server'
$AppVoices  = Join-Path $AppDir 'voices'
$LogDir     = Join-Path $OutRoot 'probe-log'
$WorkDir    = Join-Path $OutRoot 'probe-work-rocm'

$null = New-YwkDirectory -Path $LogDir
$null = New-YwkDirectory -Path $WorkDir
$Stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
# Per-run, so a second run cannot overwrite the first one's raw step files.
$StepDir    = Join-Path $WorkDir ('steps-' + $Stamp)
$null = New-YwkDirectory -Path $StepDir
$null = Start-YwkLog -Path (Join-Path $LogDir ('rocm-warmup-' + $Stamp + '.log'))

if ([string]::IsNullOrEmpty($Device)) {
    if ($Variant -eq 'cpu') { $Device = 'cpu' } else { $Device = 'cuda:0' }
}
if ([string]::IsNullOrEmpty($Precision)) {
    if ($Device -eq 'cpu') { $Precision = 'fp32' } else { $Precision = 'bf16' }
}
if ($Variant -like 'rocm*' -and $Device -ne 'cpu' -and $Precision -ne 'bf16') {
    throw ('-Variant ' + $Variant + ' with -Precision ' + $Precision +
           ': the Radeon variant is bf16-only (decisions.md 5 and 36).')
}
if ([string]::IsNullOrEmpty($HfHome)) { $HfHome = Join-Path $env:USERPROFILE '.cache\huggingface' }
if ([string]::IsNullOrEmpty($DataDir)) { $DataDir = Join-Path $WorkDir 'data' }

$python = Join-Path $RuntimeDir 'python.exe'
foreach ($needed in @($python, (Join-Path $AppServer 'ywk_server.py'))) {
    if (-not (Test-Path -LiteralPath $needed)) {
        throw ($needed + ' is missing. Run build\assemble-runtime.ps1 -Variant ' + $Variant +
               ' and build\assemble-app.ps1 first.')
    }
}
if (-not (Test-Path -LiteralPath $HfHome)) {
    throw ('the Hugging Face cache is not at ' + $HfHome + ' (read only, HF_HUB_OFFLINE=1).')
}

# The client is stdlib-only and must NOT import torch, so it runs on the dev venv when there
# is one; the assembled runtime's python is the fallback.
$clientPython = Join-Path $RepoRoot '.venv-dev\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $clientPython)) { $clientPython = $python }
$probePy = Join-Path $PSScriptRoot 'rocm-warmup-probe.py'
if (-not (Test-Path -LiteralPath $probePy)) { throw ($probePy + ' is missing.') }

Write-YwkLog -Level 'STEP' -Message ('rocm-warmup-probe: variant=' + $Variant + ' port=' + $Port +
                                     ' device=' + $Device + ' precision=' + $Precision +
                                     ' dataDir=' + $DataDir + ' keepDataDir=' + [bool]$KeepDataDir)

$base = 'http://127.0.0.1:' + $Port

# --------------------------------------------------------------------------- helpers

function Invoke-YwkHttpGet {
    param([Parameter(Mandatory = $true)][string]$Url, [int]$TimeoutSeconds = 15)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = 'GET'
    $req.Timeout = $TimeoutSeconds * 1000
    $req.ReadWriteTimeout = $TimeoutSeconds * 1000
    $req.UserAgent = 'irodori-tts-ywk-rocm-probe/1'
    try {
        $resp = [System.Net.HttpWebResponse]$req.GetResponse()
    } catch {
        $sw.Stop()
        return @{ Status = 0; Body = ''; ElapsedMs = $sw.ElapsedMilliseconds }
    }
    try {
        $status = [int]$resp.StatusCode
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream(), [System.Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $reader.Dispose()
    } finally {
        $resp.Close()
    }
    $sw.Stop()
    return @{ Status = $status; Body = $text; ElapsedMs = $sw.ElapsedMilliseconds }
}

function Get-YwkUtcNow {
    return [DateTime]::UtcNow.ToString('o')
}

$GpuSamplerScript = {
    param([int]$TargetPid, [string]$Path, [int]$IntervalMs)
    $ErrorActionPreference = 'SilentlyContinue'
    $counters = @(
        '\GPU Process Memory(*)\Local Usage',
        '\GPU Process Memory(*)\Non Local Usage',
        '\GPU Process Memory(*)\Shared Usage',
        '\GPU Process Memory(*)\Total Committed'
    )
    $prefix = 'pid_' + $TargetPid + '_'
    while ($true) {
        $stamp = [DateTime]::UtcNow.ToString('o')
        $localB = [double]0
        $nonLocalB = [double]0
        $sharedB = [double]0
        $committedB = [double]0
        $seen = 0
        try {
            $set = Get-Counter -Counter $counters -ErrorAction Stop
            foreach ($sample in $set.CounterSamples) {
                if (-not ($sample.InstanceName -like ($prefix + '*'))) { continue }
                $seen = $seen + 1
                $p = ([string]$sample.Path).ToLowerInvariant()
                $v = [double]$sample.CookedValue
                if ($p.EndsWith('\non local usage')) { $nonLocalB = $nonLocalB + $v }
                elseif ($p.EndsWith('\local usage')) { $localB = $localB + $v }
                elseif ($p.EndsWith('\shared usage')) { $sharedB = $sharedB + $v }
                elseif ($p.EndsWith('\total committed')) { $committedB = $committedB + $v }
            }
        } catch {
            $seen = -1
        }
        $line = $stamp + ',' + $seen + ',' + $localB + ',' + $nonLocalB + ',' + $sharedB + ',' + $committedB
        try { Add-Content -LiteralPath $Path -Value $line -Encoding ASCII } catch { }
        Start-Sleep -Milliseconds $IntervalMs
    }
}

function Get-YwkGpuPeak {
    <#
      .SYNOPSIS
        Peak of the sampled GPU memory rows whose stamp falls in [From, To].
      .OUTPUTS
        @{ samples; local_peak_bytes; nonlocal_peak_bytes; shared_peak_bytes;
           committed_peak_bytes; local_last_bytes } -- every field null when nothing was sampled.
    #>
    param([string]$Path, [string]$From, [string]$To)
    $empty = [ordered]@{
        samples = 0
        local_peak_bytes = $null
        nonlocal_peak_bytes = $null
        shared_peak_bytes = $null
        committed_peak_bytes = $null
        local_last_bytes = $null
    }
    if ([string]::IsNullOrEmpty($Path) -or -not (Test-Path -LiteralPath $Path)) { return $empty }
    $fromT = [DateTime]::Parse($From, [System.Globalization.CultureInfo]::InvariantCulture,
                               [System.Globalization.DateTimeStyles]::RoundtripKind)
    $toT = [DateTime]::Parse($To, [System.Globalization.CultureInfo]::InvariantCulture,
                             [System.Globalization.DateTimeStyles]::RoundtripKind)
    $n = 0
    $lp = [double]0; $np = [double]0; $sp = [double]0; $cp = [double]0; $last = $null
    foreach ($line in (Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue)) {
        $parts = $line -split ','
        if ($parts.Count -lt 6) { continue }
        $t = [DateTime]::Parse($parts[0], [System.Globalization.CultureInfo]::InvariantCulture,
                               [System.Globalization.DateTimeStyles]::RoundtripKind)
        if ($t -lt $fromT -or $t -gt $toT) { continue }
        if ([int]$parts[1] -le 0) { continue }
        $n = $n + 1
        $lv = [double]$parts[2]; $nv = [double]$parts[3]; $sv = [double]$parts[4]; $cv = [double]$parts[5]
        if ($lv -gt $lp) { $lp = $lv }
        if ($nv -gt $np) { $np = $nv }
        if ($sv -gt $sp) { $sp = $sv }
        if ($cv -gt $cp) { $cp = $cv }
        $last = $lv
    }
    if ($n -eq 0) { return $empty }
    return [ordered]@{
        samples = $n
        local_peak_bytes = [int64]$lp
        nonlocal_peak_bytes = [int64]$np
        shared_peak_bytes = [int64]$sp
        committed_peak_bytes = [int64]$cp
        local_last_bytes = [int64]$last
    }
}

$script:StepIndex = 0
function Invoke-YwkProbeStep {
    <#
      .SYNOPSIS
        Run one sub-command of rocm-warmup-probe.py and return @{ json; file; from; to; exit }.
    #>
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string[]]$Arguments)
    $script:StepIndex = $script:StepIndex + 1
    $file = Join-Path $StepDir (('{0:d2}' -f $script:StepIndex) + '-' + $Name + '.json')
    $argv = @($probePy, '--out', $file) + $Arguments
    Write-YwkLog -Message ('step ' + $Name + ': ' + $clientPython + ' ' + ($argv -join ' '))
    $from = Get-YwkUtcNow
    # The client's own stdout must not leak into this function's return value (a bare native
    # call writes to the success stream, and the caller would get an array, not the hashtable).
    $said = & $clientPython @argv 2>&1
    $code = $LASTEXITCODE
    $to = Get-YwkUtcNow
    foreach ($line in @($said)) {
        if ($null -ne $line -and -not [string]::IsNullOrWhiteSpace([string]$line)) {
            Write-YwkLog -Message ('  ' + [string]$line)
        }
    }
    if ($code -ne 0) { throw ('probe step ' + $Name + ' exited ' + $code) }
    $json = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    return @{ json = $json; file = $file; from = $from; to = $to; exit = $code }
}

# --------------------------------------------------------------------------- server control

$childEnvNames = @(
    'IRODORI_HOST', 'IRODORI_PORT', 'IRODORI_HF_CHECKPOINT', 'IRODORI_EMPTY_CACHE_INTERVAL',
    'IRODORI_ALLOW_NO_REF_VOICE', 'IRODORI_DEFAULT_NUM_STEPS', 'IRODORI_DEFAULT_RESPONSE_FORMAT',
    'IRODORI_VOICES_DIR', 'IRODORI_VOICE_ALIASES_FILE', 'IRODORI_MODEL_DEVICE', 'IRODORI_CODEC_DEVICE',
    'IRODORI_MODEL_PRECISION', 'IRODORI_CODEC_PRECISION', 'IRODORI_PRELOAD',
    'YWK_VARIANT', 'YWK_DATA_DIR', 'YWK_WARMUP_ON_START',
    'HF_HOME', 'HF_HUB_OFFLINE',
    'PYTHONUTF8', 'PYTHONDONTWRITEBYTECODE', 'PYTHONUNBUFFERED', 'PYTHONIOENCODING'
)
$savedEnv = @{}
foreach ($k in $childEnvNames) { $savedEnv[$k] = [System.Environment]::GetEnvironmentVariable($k) }

function Set-YwkChildEnv {
    $values = [ordered]@{
        'IRODORI_HOST'                    = '127.0.0.1'
        'IRODORI_PORT'                    = [string]$Port
        'IRODORI_HF_CHECKPOINT'           = 'Aratako/Irodori-TTS-v4.1-Small'
        'IRODORI_EMPTY_CACHE_INTERVAL'    = '0'
        'IRODORI_ALLOW_NO_REF_VOICE'      = 'false'
        'IRODORI_DEFAULT_NUM_STEPS'       = '40'
        'IRODORI_DEFAULT_RESPONSE_FORMAT' = 'wav'
        'IRODORI_VOICES_DIR'              = $AppVoices
        'IRODORI_VOICE_ALIASES_FILE'      = (Join-Path $AppVoices 'voices.json')
        'IRODORI_MODEL_DEVICE'            = $Device
        'IRODORI_CODEC_DEVICE'            = $Device
        'IRODORI_MODEL_PRECISION'         = $Precision
        'IRODORI_CODEC_PRECISION'         = $Precision
        'IRODORI_PRELOAD'                 = 'true'
        'YWK_VARIANT'                     = $Variant
        'YWK_DATA_DIR'                    = $DataDir
        # The probe drives the warmup by hand so every shot is timed; the on-start warmup
        # would fire before the first measurement.
        'YWK_WARMUP_ON_START'             = '0'
        'HF_HOME'                         = $HfHome
        'HF_HUB_OFFLINE'                  = '1'
        'PYTHONUTF8'                      = '1'
        'PYTHONDONTWRITEBYTECODE'         = '1'
        'PYTHONUNBUFFERED'                = '1'
        'PYTHONIOENCODING'                = 'utf-8'
    }
    foreach ($k in $values.Keys) { [System.Environment]::SetEnvironmentVariable($k, [string]$values[$k]) }
    return $values
}

$script:Servers = New-Object System.Collections.Generic.List[object]

function Start-YwkServer {
    <#
      .SYNOPSIS
        Start one wrapper, wait for GET /health 200, and start its GPU sampler.
      .OUTPUTS
        @{ proc; job; pid; ready_ms; spawn_utc; ready_utc; stdout; stderr; gpu_csv; status }
    #>
    param([Parameter(Mandatory = $true)][string]$Label)
    $stdoutLog = Join-Path $LogDir ('server-' + $Stamp + '-' + $Label + '.out.log')
    $stderrLog = Join-Path $LogDir ('server-' + $Stamp + '-' + $Label + '.err.log')
    $gpuCsv    = Join-Path $LogDir ('gpu-' + $Stamp + '-' + $Label + '.csv')
    foreach ($f in @($stdoutLog, $stderrLog, $gpuCsv)) {
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
    }

    Write-YwkLog -Message ('launching server ' + $Label + ': ' + $python + ' -m ywk_server --port ' + $Port)
    $spawnUtc = Get-YwkUtcNow
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $python `
        -ArgumentList @('-m', 'ywk_server', '--host', '127.0.0.1', '--port', [string]$Port) `
        -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
    $entry = [ordered]@{
        label = $Label; proc = $proc; job = $null; pid = $proc.Id
        stdout = $stdoutLog; stderr = $stderrLog; gpu_csv = $gpuCsv
    }
    $script:Servers.Add($entry)

    $job = Start-Job -ScriptBlock $GpuSamplerScript -ArgumentList @($proc.Id, $gpuCsv, $GpuSampleMs)
    $entry['job'] = $job

    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { throw ('server ' + $Label + ' exited early with code ' + $proc.ExitCode + '. See ' + $stderrLog) }
        $r = Invoke-YwkHttpGet -Url ($base + '/health') -TimeoutSeconds 10
        if ($r.Status -eq 200) { $ready = $true; break }
        Start-Sleep -Milliseconds 250
    }
    $sw.Stop()
    if (-not $ready) { throw ('server ' + $Label + ': GET /health did not answer 200 within ' + $ReadyTimeoutSeconds + ' s. See ' + $stderrLog) }
    $readyUtc = Get-YwkUtcNow
    $st = Invoke-YwkHttpGet -Url ($base + '/ywk/status') -TimeoutSeconds 30
    Write-YwkLog -Message ('server ' + $Label + ' ready in ' + $sw.ElapsedMilliseconds + ' ms (pid ' + $proc.Id + ')')
    return @{
        entry = $entry; proc = $proc; pid = $proc.Id; ready_ms = [int64]$sw.ElapsedMilliseconds
        spawn_utc = $spawnUtc; ready_utc = $readyUtc; stdout = $stdoutLog; stderr = $stderrLog
        gpu_csv = $gpuCsv; status_body = $st.Body
    }
}

function Stop-YwkServer {
    param([Parameter(Mandatory = $true)]$Server)
    $entry = $Server.entry
    if ($null -ne $entry['job']) {
        try { Stop-Job -Job $entry['job'] -ErrorAction SilentlyContinue } catch { }
        try { Remove-Job -Job $entry['job'] -Force -ErrorAction SilentlyContinue } catch { }
        $entry['job'] = $null
    }
    $proc = $entry['proc']
    if ($null -ne $proc -and -not $proc.HasExited) {
        Write-YwkLog -Message ('killing server ' + $entry['label'] + ' pid ' + $proc.Id + ' (tree)')
        # taskkill /T so a child interpreter cannot outlive the parent and keep the GPU.
        & taskkill.exe /PID $proc.Id /T /F | Out-Null
        $null = $proc.WaitForExit(20000)
        if (-not $proc.HasExited) {
            try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch { }
            $null = $proc.WaitForExit(10000)
        }
    }
}

function Get-YwkWarmupShotLines {
    param([string]$Path)
    $out = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $Path)) { return $out.ToArray() }
    foreach ($line in (Get-Content -LiteralPath $Path -Encoding UTF8 -ErrorAction SilentlyContinue)) {
        if ($line -match 'warmup shot ') { $out.Add($line) }
    }
    return $out.ToArray()
}

# --------------------------------------------------------------------------- run

$report = [ordered]@{
    generated   = Get-YwkTimestamp
    script      = 'probe/rocm-warmup-probe.ps1'
    variant     = $Variant
    port        = $Port
    device      = $Device
    precision   = $Precision
    hf_home     = $HfHome
    data_dir    = $DataDir
    data_dir_wiped = (-not [bool]$KeepDataDir)
    runtime_dir = $RuntimeDir
    app_dir     = $AppDir
    gpu_sample_ms = $GpuSampleMs
    gpu_counter = 'GPU Process Memory (Windows performance counter), filtered to the server pid'
    warmup_stages = $WarmupStages
    priority_stages = $PriorityStages
    phases      = [ordered]@{}
}
$failures = New-Object System.Collections.Generic.List[string]
$serverA = $null
$serverB = $null

try {
    # ---------------------------------------------------------------- 0. voices
    $prep = Invoke-YwkProbeStep -Name 'prepare' -Arguments @(
        'prepare',
        '--presets-json', (Join-Path $RepoRoot 'voices\presets.json'),
        '--presets-dir', (Join-Path $RepoRoot 'voices\presets'),
        '--app-voices', $AppVoices
    )
    $presetIds = @()
    foreach ($row in @($prep.json.presets)) { $presetIds = $presetIds + $row.id }
    if ($presetIds.Count -lt 1) { throw 'no preset voices were prepared.' }
    Write-YwkLog -Message ('prepared ' + $presetIds.Count + ' preset voice(s): ' + ($presetIds -join ', '))
    $report.phases['prepare'] = $prep.json

    $warmVoices = @()
    for ($i = 0; $i -lt [Math]::Min($WarmupVoiceCount, $presetIds.Count); $i++) { $warmVoices = $warmVoices + $presetIds[$i] }
    $coldVoice = $presetIds[$presetIds.Count - 1]
    if ($warmVoices -contains $coldVoice) { throw 'the unwarmed speaker is also in the warmup list; raise the preset count or lower -WarmupVoiceCount.' }
    Write-YwkLog -Message ('warmup speakers: ' + ($warmVoices -join ', ') + '  |  unwarmed speaker: ' + $coldVoice)

    # ---------------------------------------------------------------- 1. cold db
    if (-not $KeepDataDir) {
        if (Test-Path -LiteralPath $DataDir) {
            Write-YwkLog -Message ('wiping ' + $DataDir + ' (cold MIOpen db)')
            Remove-Item -LiteralPath $DataDir -Recurse -Force
        }
    }
    $null = New-YwkDirectory -Path $DataDir
    $miopenBefore = @(Get-ChildItem -LiteralPath $DataDir -Recurse -File -ErrorAction SilentlyContinue)
    Write-YwkLog -Message ('data dir holds ' + $miopenBefore.Count + ' file(s) before the cold run')

    $null = Set-YwkChildEnv
    $serverA = Start-YwkServer -Label 'cold'
    $report.phases['server_cold'] = [ordered]@{
        label = 'cold'
        pid = $serverA.pid
        ready_ms = $serverA.ready_ms
        spawn_utc = $serverA.spawn_utc
        ready_utc = $serverA.ready_utc
        data_dir_files_before = $miopenBefore.Count
        gpu_after_ready = (Get-YwkGpuPeak -Path $serverA.gpu_csv -From $serverA.spawn_utc -To $serverA.ready_utc)
        status = $serverA.status_body
    }

    $warm = Invoke-YwkProbeStep -Name 'warmup-cold' -Arguments @(
        'warmup', '--base', $base, '--stages', $WarmupStages, '--voices', ($warmVoices -join ','), '--label', 'cold'
    )
    $report.phases['warmup_cold'] = [ordered]@{
        result = $warm.json
        gpu = (Get-YwkGpuPeak -Path $serverA.gpu_csv -From $warm.from -To $warm.to)
        started_utc = $warm.from
        ended_utc = $warm.to
    }

    # Four shots of the same body, not two: docs/radeon.md section 5 item 6 asks whether the
    # "third shot regression" (19.4 s) of the original survey shows up over HTTP as well.
    $shotsCold = Invoke-YwkProbeStep -Name 'shots-cold' -Arguments @(
        'shots', '--base', $base, '--label', 'cold',
        '--shot', ('warmed_first=' + $warmVoices[0]),
        '--shot', ('warmed_second=' + $warmVoices[0]),
        '--shot', ('warmed_third=' + $warmVoices[0]),
        '--shot', ('warmed_fourth=' + $warmVoices[0]),
        '--shot', ('unwarmed_first=' + $coldVoice),
        '--shot', ('unwarmed_second=' + $coldVoice),
        '--shot', 'no_ref_first=@no_ref',
        '--shot', 'no_ref_second=@no_ref',
        '--shot', 'no_ref_third=@no_ref',
        '--shot', 'no_ref_fourth=@no_ref'
    )
    $report.phases['shots_cold'] = [ordered]@{
        result = $shotsCold.json
        gpu = (Get-YwkGpuPeak -Path $serverA.gpu_csv -From $shotsCold.from -To $shotsCold.to)
    }

    $coldEndUtc = Get-YwkUtcNow
    $report.phases['server_cold']['gpu_whole_life'] = (Get-YwkGpuPeak -Path $serverA.gpu_csv -From $serverA.spawn_utc -To $coldEndUtc)
    Stop-YwkServer -Server $serverA
    $report.phases['server_cold']['warmup_log_lines'] = (Get-YwkWarmupShotLines -Path $serverA.stderr)
    $miopenAfter = @(Get-ChildItem -LiteralPath $DataDir -Recurse -File -ErrorAction SilentlyContinue)
    $miopenBytes = 0
    foreach ($f in $miopenAfter) { $miopenBytes = $miopenBytes + $f.Length }
    $report.phases['server_cold']['data_dir_files_after'] = $miopenAfter.Count
    $report.phases['server_cold']['data_dir_bytes_after'] = $miopenBytes
    $report.phases['server_cold']['data_dir_listing'] = @($miopenAfter | ForEach-Object { $_.FullName.Substring($DataDir.Length).TrimStart('\') + ' ' + $_.Length })
    Write-YwkLog -Message ('data dir holds ' + $miopenAfter.Count + ' file(s) / ' + $miopenBytes + ' B after the cold run')

    # ---------------------------------------------------------------- 2. restart, same db
    $serverB = Start-YwkServer -Label 'warm'
    $report.phases['server_warm'] = [ordered]@{
        label = 'warm'
        pid = $serverB.pid
        ready_ms = $serverB.ready_ms
        spawn_utc = $serverB.spawn_utc
        ready_utc = $serverB.ready_utc
        gpu_after_ready = (Get-YwkGpuPeak -Path $serverB.gpu_csv -From $serverB.spawn_utc -To $serverB.ready_utc)
        status = $serverB.status_body
    }

    $shotsWarm = Invoke-YwkProbeStep -Name 'shots-persist' -Arguments @(
        'shots', '--base', $base, '--label', 'persist',
        '--shot', ('warmed_first=' + $warmVoices[0]),
        '--shot', ('warmed_second=' + $warmVoices[0]),
        '--shot', ('warmed_third=' + $warmVoices[0]),
        '--shot', ('warmed_fourth=' + $warmVoices[0]),
        '--shot', ('unwarmed_first=' + $coldVoice),
        '--shot', ('unwarmed_second=' + $coldVoice),
        '--shot', 'no_ref_first=@no_ref',
        '--shot', 'no_ref_second=@no_ref'
    )
    $report.phases['shots_persist'] = [ordered]@{
        result = $shotsWarm.json
        gpu = (Get-YwkGpuPeak -Path $serverB.gpu_csv -From $shotsWarm.from -To $shotsWarm.to)
    }

    $prio = Invoke-YwkProbeStep -Name 'priority' -Arguments @(
        'priority', '--base', $base, '--stages', $PriorityStages, '--voice', $warmVoices[0]
    )
    $report.phases['priority'] = [ordered]@{
        result = $prio.json
        gpu = (Get-YwkGpuPeak -Path $serverB.gpu_csv -From $prio.from -To $prio.to)
    }

    # Twice around the 11 speakers. The first pass is each reference's FIRST sighting on this
    # machine, so it pays MIOpen's search (that is the number to quote for "a new speaker");
    # the second pass is the steady state acceptance C-8 asks about (RTF < 0.5).
    $presets = Invoke-YwkProbeStep -Name 'presets-first' -Arguments @(
        'presets', '--base', $base, '--ids', ($presetIds -join ','), '--label', 'first'
    )
    $report.phases['presets'] = [ordered]@{
        result = $presets.json
        gpu = (Get-YwkGpuPeak -Path $serverB.gpu_csv -From $presets.from -To $presets.to)
    }

    $presets2 = Invoke-YwkProbeStep -Name 'presets-second' -Arguments @(
        'presets', '--base', $base, '--ids', ($presetIds -join ','), '--label', 'second'
    )
    $report.phases['presets_second'] = [ordered]@{
        result = $presets2.json
        gpu = (Get-YwkGpuPeak -Path $serverB.gpu_csv -From $presets2.from -To $presets2.to)
    }

    $warmEndUtc = Get-YwkUtcNow
    $report.phases['server_warm']['gpu_whole_life'] = (Get-YwkGpuPeak -Path $serverB.gpu_csv -From $serverB.spawn_utc -To $warmEndUtc)
    Stop-YwkServer -Server $serverB
    $report.phases['server_warm']['warmup_log_lines'] = (Get-YwkWarmupShotLines -Path $serverB.stderr)
} finally {
    foreach ($entry in $script:Servers) {
        if ($null -ne $entry['job']) {
            try { Stop-Job -Job $entry['job'] -ErrorAction SilentlyContinue } catch { }
            try { Remove-Job -Job $entry['job'] -Force -ErrorAction SilentlyContinue } catch { }
            $entry['job'] = $null
        }
        $p = $entry['proc']
        if ($null -ne $p -and -not $p.HasExited) {
            Write-YwkLog -Level 'WARN' -Message ('killing leftover server pid ' + $p.Id)
            & taskkill.exe /PID $p.Id /T /F | Out-Null
            $null = $p.WaitForExit(20000)
        }
    }
    foreach ($k in $childEnvNames) { [System.Environment]::SetEnvironmentVariable($k, $savedEnv[$k]) }
}

# --------------------------------------------------------------------------- report

$reportPath = Join-Path $LogDir ('rocm-warmup-' + $Stamp + '.json')
$report['failures'] = $failures.ToArray()
$null = Write-YwkJsonFile -Path $reportPath -Value $report -Depth 20
Write-YwkLog -Level 'STEP' -Message ('wrote ' + $reportPath)

Write-YwkLog -Message ('ready cold=' + $report.phases['server_cold']['ready_ms'] + ' ms  ready warm=' + $report.phases['server_warm']['ready_ms'] + ' ms')
exit 0
