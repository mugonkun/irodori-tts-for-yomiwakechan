<#
.SYNOPSIS
    Radeon (ROCm) live-fire probe for the reference-latent cache (decisions.md 65)
    and for the empty_cache_interval material (decisions.md 62, docs/radeon.md 7-7).

.DESCRIPTION
    Convoy C (2). This script MEASURES; it builds nothing. It drives the runtime that
    build\assemble-runtime.ps1 -Variant rocm-gfx1151 produced and the payload that
    build\assemble-app.ps1 produced, and writes one machine-written JSON to
    build\out\probe-log\rocm-latent-<stamp>.json.

    One server process per run, in this order:

      0 prepare      voices\presets\*.wav are COPIED to build\out\app\voices\ and
                     voices.json is written with the default speaker plus the 11 presets,
                     each pointing at its wav. <voices>\latents\ is wiped unless
                     -KeepLatents, so a run really encodes rather than reusing.
      1 server       spawn -> GET /health 200, with IRODORI_EMPTY_CACHE_INTERVAL set to
                     -EmptyCacheInterval. The on-start warmup and the on-start precompute
                     are both switched off so every millisecond below is driven by hand.
      2 precompute   POST /ywk/voices/precompute {"all":true}, polled at 50 ms so each
                     speaker's own ms is captured, then the .pt files are counted on disk
                     and GET /ywk/voices is read back for latent / latent_stale.
                     Skipped entirely with -NoLatents (the wav-reference control run).
      3 presets      the same 11 speakers, same body, same order as docs/radeon.md 7-6,
                     -Passes times around. RTF = elapsed / wav seconds.

    Per shot the report carries: elapsed ms, RTF, the GPU Process Memory window for that
    shot (Windows performance counter, filtered to the server pid -- NOT torch's allocator),
    and the upstream's own stage timings (prepare_reference / sample_rf / decode_latent),
    parsed out of the server's stderr log by counting "[runtime] start synthesize" lines.
    Nothing is filled in by hand: a value that could not be measured stays null.

    Stop-band: only -Port (default 18092) is opened; 8088 and 7861 are never touched.
    The Hugging Face cache is read with HF_HUB_OFFLINE=1. voices\ itself is read-only.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File probe\rocm-latent-probe.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File probe\rocm-latent-probe.ps1 -EmptyCacheInterval 10
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File probe\rocm-latent-probe.ps1 -NoLatents -Label control
#>
[CmdletBinding()]
param(
    [ValidateSet('rocm-gfx1151', 'cpu', 'cu130', 'cu126')]
    [string]$Variant = 'rocm-gfx1151',
    [int]$Port = 18092,
    [string]$Device = '',
    [ValidateSet('', 'fp32', 'bf16')]
    [string]$Precision = '',
    [string]$HfHome = '',
    # Default: the same data dir convoy C left warm, so the MIOpen db is NOT cold here.
    [string]$DataDir = '',
    [switch]$WipeDataDir,
    # 0 = never release the allocator cache (what the distribution bakes today);
    # 10 = the upstream default (config.py:39).
    [int]$EmptyCacheInterval = 0,
    [int]$Passes = 2,
    # Do not precompute: the control run that still serves references from wav.
    [switch]$NoLatents,
    # Keep whatever <voices>\latents\ already holds (default: wipe, so the run encodes).
    [switch]$KeepLatents,
    [string]$Label = '',
    [int]$ReadyTimeoutSeconds = 900,
    [int]$GpuSampleMs = 400,
    [string]$OutRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# PowerShell 7.4 turns a native command's non-zero exit into a terminating error while
# ErrorActionPreference is Stop; taskkill.exe legitimately returns non-zero.
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot '..\build\Common.ps1')

$RepoRoot = Get-YwkRepoRoot
if ([string]::IsNullOrEmpty($OutRoot)) { $OutRoot = Join-Path $RepoRoot 'build\out' }
$RuntimeDir = Join-Path $OutRoot ('runtime-' + $Variant)
$AppDir     = Join-Path $OutRoot 'app'
$AppServer  = Join-Path $AppDir 'server'
$AppVoices  = Join-Path $AppDir 'voices'
$LatentsDir = Join-Path $AppVoices 'latents'
$AliasFile  = Join-Path $AppVoices 'voices.json'
$LogDir     = Join-Path $OutRoot 'probe-log'
$WorkDir    = Join-Path $OutRoot 'probe-work-rocm'

$null = New-YwkDirectory -Path $LogDir
$null = New-YwkDirectory -Path $WorkDir
$Stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
if ([string]::IsNullOrEmpty($Label)) { $Label = 'ec' + $EmptyCacheInterval }
$StepDir = Join-Path $WorkDir ('latent-steps-' + $Stamp)
$null = New-YwkDirectory -Path $StepDir
$null = Start-YwkLog -Path (Join-Path $LogDir ('rocm-latent-' + $Stamp + '.log'))

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

$clientPython = Join-Path $RepoRoot '.venv-dev\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $clientPython)) { $clientPython = $python }
$probePy = Join-Path $PSScriptRoot 'rocm-warmup-probe.py'
if (-not (Test-Path -LiteralPath $probePy)) { throw ($probePy + ' is missing.') }

$base = 'http://127.0.0.1:' + $Port

Write-YwkLog -Level 'STEP' -Message ('rocm-latent-probe: variant=' + $Variant + ' port=' + $Port +
                                     ' device=' + $Device + ' precision=' + $Precision +
                                     ' emptyCacheInterval=' + $EmptyCacheInterval +
                                     ' passes=' + $Passes + ' latents=' + (-not [bool]$NoLatents) +
                                     ' dataDir=' + $DataDir + ' label=' + $Label)

# --------------------------------------------------------------------------- helpers

function Invoke-YwkHttpGet {
    param([Parameter(Mandatory = $true)][string]$Url, [int]$TimeoutSeconds = 15)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = 'GET'
    $req.Timeout = $TimeoutSeconds * 1000
    $req.ReadWriteTimeout = $TimeoutSeconds * 1000
    $req.UserAgent = 'irodori-tts-ywk-rocm-latent-probe/1'
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

function Get-YwkUtcNow { return [DateTime]::UtcNow.ToString('o') }

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

function Get-YwkGpuRows {
    <#
      .SYNOPSIS
        Every sampled row whose stamp falls in [From, To], as @{ t; local; nonlocal; shared; committed }.
    #>
    param([string]$Path, [string]$From, [string]$To)
    $rows = New-Object System.Collections.Generic.List[object]
    if ([string]::IsNullOrEmpty($Path) -or -not (Test-Path -LiteralPath $Path)) { return $rows }
    $fromT = [DateTime]::Parse($From, [System.Globalization.CultureInfo]::InvariantCulture,
                               [System.Globalization.DateTimeStyles]::RoundtripKind)
    $toT = [DateTime]::Parse($To, [System.Globalization.CultureInfo]::InvariantCulture,
                             [System.Globalization.DateTimeStyles]::RoundtripKind)
    foreach ($line in (Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue)) {
        $parts = $line -split ','
        if ($parts.Count -lt 6) { continue }
        $t = [DateTime]::Parse($parts[0], [System.Globalization.CultureInfo]::InvariantCulture,
                               [System.Globalization.DateTimeStyles]::RoundtripKind)
        if ($t -lt $fromT -or $t -gt $toT) { continue }
        if ([int]$parts[1] -le 0) { continue }
        $rows.Add([ordered]@{
            t = $parts[0]
            local = [int64][double]$parts[2]
            nonlocal = [int64][double]$parts[3]
            shared = [int64][double]$parts[4]
            committed = [int64][double]$parts[5]
        })
    }
    return $rows
}

function Get-YwkGpuPeak {
    param([string]$Path, [string]$From, [string]$To)
    $empty = [ordered]@{
        samples = 0
        local_peak_bytes = $null
        nonlocal_peak_bytes = $null
        shared_peak_bytes = $null
        committed_peak_bytes = $null
        local_first_bytes = $null
        local_last_bytes = $null
    }
    $rows = @(Get-YwkGpuRows -Path $Path -From $From -To $To)
    if ($rows.Count -eq 0) { return $empty }
    $lp = [int64]0; $np = [int64]0; $sp = [int64]0; $cp = [int64]0
    foreach ($r in $rows) {
        if ($r.local -gt $lp) { $lp = $r.local }
        if ($r.nonlocal -gt $np) { $np = $r.nonlocal }
        if ($r.shared -gt $sp) { $sp = $r.shared }
        if ($r.committed -gt $cp) { $cp = $r.committed }
    }
    return [ordered]@{
        samples = $rows.Count
        local_peak_bytes = $lp
        nonlocal_peak_bytes = $np
        shared_peak_bytes = $sp
        committed_peak_bytes = $cp
        local_first_bytes = $rows[0].local
        local_last_bytes = $rows[$rows.Count - 1].local
    }
}

function Get-YwkStageTimings {
    <#
      .SYNOPSIS
        The upstream's own per-shot stage timings, parsed from the server's stderr.
      .DESCRIPTION
        Every synthesis writes "[runtime] start synthesize ..." and then one line per stage
        ("[runtime] prepare_reference: 1332.1 ms"). A new "start synthesize" opens a new
        group, so the Nth group is the Nth synthesis this process ran. A stage the upstream
        did not print stays null; nothing here is inferred.
    #>
    param([string]$Path)
    $out = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path -LiteralPath $Path)) { return $out }
    $current = $null
    $index = 0
    foreach ($line in (Get-Content -LiteralPath $Path -Encoding UTF8 -ErrorAction SilentlyContinue)) {
        if ($line -match '\[runtime\] start synthesize') {
            if ($null -ne $current) { $out.Add($current) }
            $index = $index + 1
            $current = [ordered]@{
                index = $index
                tokenize_text_ms = $null
                prepare_reference_ms = $null
                predict_duration_ms = $null
                sample_rf_ms = $null
                unpatchify_latent_ms = $null
                decode_latent_ms = $null
                silentcipher_watermark_ms = $null
                total_to_decode_s = $null
            }
            continue
        }
        if ($null -eq $current) { continue }
        if ($line -match '\[runtime\] ([a-z_]+)[^:]*: ([0-9.]+) (ms|s)\s*$') {
            $stage = $Matches[1]
            $value = [double]$Matches[2]
            $unit = $Matches[3]
            switch ($stage) {
                'tokenize_text'          { if ($unit -eq 'ms') { $current['tokenize_text_ms'] = $value } }
                'prepare_reference'      { if ($unit -eq 'ms') { $current['prepare_reference_ms'] = $value } }
                'predict_duration'       { if ($unit -eq 'ms') { $current['predict_duration_ms'] = $value } }
                'sample_rf'              { if ($unit -eq 'ms') { $current['sample_rf_ms'] = $value } }
                'unpatchify_latent'      { if ($unit -eq 'ms') { $current['unpatchify_latent_ms'] = $value } }
                'decode_latent'          { if ($unit -eq 'ms') { $current['decode_latent_ms'] = $value } }
                'silentcipher_watermark' { if ($unit -eq 'ms') { $current['silentcipher_watermark_ms'] = $value } }
                'total_to_decode'        { if ($unit -eq 's') { $current['total_to_decode_s'] = $value } }
                default { }
            }
        }
    }
    if ($null -ne $current) { $out.Add($current) }
    return $out
}

$script:StepIndex = 0
function Invoke-YwkProbeStep {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string[]]$Arguments)
    $script:StepIndex = $script:StepIndex + 1
    $file = Join-Path $StepDir (('{0:d2}' -f $script:StepIndex) + '-' + $Name + '.json')
    $argv = @($probePy, '--out', $file) + $Arguments
    Write-YwkLog -Message ('step ' + $Name + ': ' + $clientPython + ' ' + ($argv -join ' '))
    $from = Get-YwkUtcNow
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
    'YWK_VARIANT', 'YWK_DATA_DIR', 'YWK_WARMUP_ON_START', 'YWK_PRECOMPUTE_ON_START',
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
        'IRODORI_EMPTY_CACHE_INTERVAL'    = [string]$EmptyCacheInterval
        'IRODORI_ALLOW_NO_REF_VOICE'      = 'false'
        'IRODORI_DEFAULT_NUM_STEPS'       = '40'
        'IRODORI_DEFAULT_RESPONSE_FORMAT' = 'wav'
        'IRODORI_VOICES_DIR'              = $AppVoices
        'IRODORI_VOICE_ALIASES_FILE'      = $AliasFile
        'IRODORI_MODEL_DEVICE'            = $Device
        'IRODORI_CODEC_DEVICE'            = $Device
        'IRODORI_MODEL_PRECISION'         = $Precision
        'IRODORI_CODEC_PRECISION'         = $Precision
        'IRODORI_PRELOAD'                 = 'true'
        'YWK_VARIANT'                     = $Variant
        'YWK_DATA_DIR'                    = $DataDir
        # Both on-start jobs are driven by hand here so every millisecond is timed.
        'YWK_WARMUP_ON_START'             = '0'
        'YWK_PRECOMPUTE_ON_START'         = '0'
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
    param([Parameter(Mandatory = $true)][string]$ServerLabel)
    $stdoutLog = Join-Path $LogDir ('server-latent-' + $Stamp + '-' + $ServerLabel + '.out.log')
    $stderrLog = Join-Path $LogDir ('server-latent-' + $Stamp + '-' + $ServerLabel + '.err.log')
    $gpuCsv    = Join-Path $LogDir ('gpu-latent-' + $Stamp + '-' + $ServerLabel + '.csv')
    foreach ($f in @($stdoutLog, $stderrLog, $gpuCsv)) {
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
    }

    Write-YwkLog -Message ('launching server ' + $ServerLabel + ': ' + $python + ' -m ywk_server --port ' + $Port)
    $spawnUtc = Get-YwkUtcNow
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $python `
        -ArgumentList @('-m', 'ywk_server', '--host', '127.0.0.1', '--port', [string]$Port) `
        -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
    $entry = [ordered]@{
        label = $ServerLabel; proc = $proc; job = $null; pid = $proc.Id
        stdout = $stdoutLog; stderr = $stderrLog; gpu_csv = $gpuCsv
    }
    $script:Servers.Add($entry)

    $job = Start-Job -ScriptBlock $GpuSamplerScript -ArgumentList @($proc.Id, $gpuCsv, $GpuSampleMs)
    $entry['job'] = $job

    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { throw ('server ' + $ServerLabel + ' exited early with code ' + $proc.ExitCode + '. See ' + $stderrLog) }
        $r = Invoke-YwkHttpGet -Url ($base + '/health') -TimeoutSeconds 10
        if ($r.Status -eq 200) { $ready = $true; break }
        Start-Sleep -Milliseconds 250
    }
    $sw.Stop()
    if (-not $ready) { throw ('server ' + $ServerLabel + ': GET /health did not answer 200 within ' + $ReadyTimeoutSeconds + ' s. See ' + $stderrLog) }
    $readyUtc = Get-YwkUtcNow
    $st = Invoke-YwkHttpGet -Url ($base + '/ywk/status') -TimeoutSeconds 30
    Write-YwkLog -Message ('server ' + $ServerLabel + ' ready in ' + $sw.ElapsedMilliseconds + ' ms (pid ' + $proc.Id + ')')
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
        & taskkill.exe /PID $proc.Id /T /F | Out-Null
        $null = $proc.WaitForExit(20000)
        if (-not $proc.HasExited) {
            try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch { }
            $null = $proc.WaitForExit(10000)
        }
    }
}

# --------------------------------------------------------------------------- run

$report = [ordered]@{
    generated   = Get-YwkTimestamp
    script      = 'probe/rocm-latent-probe.ps1'
    label       = $Label
    variant     = $Variant
    port        = $Port
    device      = $Device
    precision   = $Precision
    empty_cache_interval = $EmptyCacheInterval
    latents     = (-not [bool]$NoLatents)
    passes      = $Passes
    hf_home     = $HfHome
    data_dir    = $DataDir
    data_dir_wiped = [bool]$WipeDataDir
    latents_wiped = (-not [bool]$KeepLatents)
    runtime_dir = $RuntimeDir
    app_dir     = $AppDir
    gpu_sample_ms = $GpuSampleMs
    gpu_counter = 'GPU Process Memory (Windows performance counter), filtered to the server pid'
    phases      = [ordered]@{}
}
$failures = New-Object System.Collections.Generic.List[string]
$server = $null

try {
    # ---------------------------------------------------------------- 0. voices
    if (-not $KeepLatents) {
        if (Test-Path -LiteralPath $LatentsDir) {
            Write-YwkLog -Message ('wiping ' + $LatentsDir)
            Remove-Item -LiteralPath $LatentsDir -Recurse -Force
        }
    }
    $prep = Invoke-YwkProbeStep -Name 'prepare' -Arguments @(
        'prepare',
        '--presets-json', (Join-Path $RepoRoot 'voices\presets.json'),
        '--presets-dir', (Join-Path $RepoRoot 'voices\presets'),
        '--app-voices', $AppVoices
    )
    $presetIds = @()
    foreach ($row in @($prep.json.presets)) { $presetIds = $presetIds + $row.id }
    if ($presetIds.Count -lt 1) { throw 'no preset voices were prepared.' }
    Write-YwkLog -Message ('prepared ' + $presetIds.Count + ' preset voice(s)')
    $report.phases['prepare'] = $prep.json

    if ($WipeDataDir -and (Test-Path -LiteralPath $DataDir)) {
        Write-YwkLog -Message ('wiping ' + $DataDir + ' (cold MIOpen db)')
        Remove-Item -LiteralPath $DataDir -Recurse -Force
    }
    $null = New-YwkDirectory -Path $DataDir
    $dbBefore = @(Get-ChildItem -LiteralPath $DataDir -Recurse -File -ErrorAction SilentlyContinue)
    $dbBytesBefore = 0
    foreach ($f in $dbBefore) { $dbBytesBefore = $dbBytesBefore + $f.Length }

    # ---------------------------------------------------------------- 1. server
    $null = Set-YwkChildEnv
    $server = Start-YwkServer -ServerLabel $Label
    $report.phases['server'] = [ordered]@{
        label = $Label
        pid = $server.pid
        ready_ms = $server.ready_ms
        spawn_utc = $server.spawn_utc
        ready_utc = $server.ready_utc
        data_dir_files_before = $dbBefore.Count
        data_dir_bytes_before = $dbBytesBefore
        gpu_after_ready = (Get-YwkGpuPeak -Path $server.gpu_csv -From $server.spawn_utc -To $server.ready_utc)
        status = $server.status_body
    }

    # ---------------------------------------------------------------- 2. precompute
    if (-not $NoLatents) {
        $pc = Invoke-YwkProbeStep -Name 'precompute' -Arguments @(
            'precompute', '--base', $base, '--label', $Label,
            '--latents-dir', $LatentsDir, '--aliases-file', $AliasFile
        )
        $report.phases['precompute'] = [ordered]@{
            result = $pc.json
            started_utc = $pc.from
            ended_utc = $pc.to
            gpu = (Get-YwkGpuPeak -Path $server.gpu_csv -From $pc.from -To $pc.to)
        }
        if ([string]$pc.json.final.state -ne 'done') {
            $failures.Add('precompute ended in state ' + [string]$pc.json.final.state)
        }
    } else {
        $vs = Invoke-YwkProbeStep -Name 'voices' -Arguments @('voices', '--base', $base, '--label', $Label)
        $report.phases['voices_no_latents'] = $vs.json
    }

    # ---------------------------------------------------------------- 3. presets
    $passList = New-Object System.Collections.Generic.List[object]
    for ($pass = 1; $pass -le $Passes; $pass++) {
        $step = Invoke-YwkProbeStep -Name ('presets-' + $pass) -Arguments @(
            'presets', '--base', $base, '--ids', ($presetIds -join ','), '--label', ('pass' + $pass)
        )
        $rows = New-Object System.Collections.Generic.List[object]
        foreach ($shot in @($step.json.shots)) {
            $gpu = $null
            if ($null -ne $shot.started_utc -and $null -ne $shot.ended_utc) {
                $gpu = Get-YwkGpuPeak -Path $server.gpu_csv -From $shot.started_utc -To $shot.ended_utc
            }
            $rows.Add([ordered]@{
                name = $shot.name
                http_status = $shot.http_status
                ms = $shot.ms
                bytes = $shot.bytes
                wav_seconds = $shot.wav_seconds
                rtf = $shot.rtf
                started_utc = $shot.started_utc
                ended_utc = $shot.ended_utc
                gpu = $gpu
            })
        }
        $passList.Add([ordered]@{
            pass = $pass
            started_utc = $step.from
            ended_utc = $step.to
            count = $step.json.count
            count_200 = $step.json.count_200
            rtf_min = $step.json.rtf_min
            rtf_max = $step.json.rtf_max
            shots = $rows.ToArray()
            gpu = (Get-YwkGpuPeak -Path $server.gpu_csv -From $step.from -To $step.to)
        })
    }
    $report.phases['presets'] = $passList.ToArray()

    # ---------------------------------------------------------------- 4. close
    $endUtc = Get-YwkUtcNow
    $report.phases['server']['gpu_whole_life'] = (Get-YwkGpuPeak -Path $server.gpu_csv -From $server.spawn_utc -To $endUtc)
    $report.phases['server']['gpu_series'] = @(Get-YwkGpuRows -Path $server.gpu_csv -From $server.spawn_utc -To $endUtc)
    $st = Invoke-YwkHttpGet -Url ($base + '/ywk/status') -TimeoutSeconds 30
    $report.phases['server']['status_end'] = $st.Body
    Stop-YwkServer -Server $server
    $report.phases['server']['stage_timings'] = @(Get-YwkStageTimings -Path $server.stderr)
    $report.phases['server']['stderr_log'] = $server.stderr
    $dbAfter = @(Get-ChildItem -LiteralPath $DataDir -Recurse -File -ErrorAction SilentlyContinue)
    $dbBytesAfter = 0
    foreach ($f in $dbAfter) { $dbBytesAfter = $dbBytesAfter + $f.Length }
    $report.phases['server']['data_dir_files_after'] = $dbAfter.Count
    $report.phases['server']['data_dir_bytes_after'] = $dbBytesAfter
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

$reportPath = Join-Path $LogDir ('rocm-latent-' + $Stamp + '.json')
$report['failures'] = $failures.ToArray()
$null = Write-YwkJsonFile -Path $reportPath -Value $report -Depth 20
Write-YwkLog -Level 'STEP' -Message ('wrote ' + $reportPath)
exit 0
