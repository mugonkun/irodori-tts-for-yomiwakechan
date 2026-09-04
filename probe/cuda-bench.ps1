<#
.SYNOPSIS
    Start the assembled wrapper on a GPU (or the CPU), run probe/cuda_bench.py against it,
    and always kill the process tree.

.DESCRIPTION
    Seat B (RTX 3090, remote): acceptance conditions B-4, B-5, B-6 of
    docs/design/ben-b-rtx-remote.md. Budgets live in docs/acceptance.md; the frozen case
    text and the condition table live in probe/bench_cases.json.

    What this script owns:
      1. staging the reference voices into a throwaway IRODORI_VOICES_DIR
         (the 30 s presets, plus a 10.0 s head-trim of the chosen one, plus voices.json
         with the no-reference alias and the Japanese display names),
      2. starting build/out/runtime-<variant>/python.exe -m ywk_server with the env of
         docs/design/ben-a-skeleton-and-build.md section 2 (the same set build/verify-runtime.ps1
         builds today, plus device / precision / HF_HOME),
      3. running probe/cuda_bench.py, which does every measurement and writes the JSON,
      4. killing the tree (taskkill /T /F), reading VRAM again 10 s later, and copying the
         results to build/out/probe-log/ and, when asked, to -ResultRoot (N: is fine).

    Output names carry a slug. Without -Label the slug is the variant, so B-4/B-6 keep
    writing cuda-bench-cu130.* and cuda-bench-cu126.*. With -Label the label goes INTO the
    names (cuda-bench-cu126-olddriver.json, wav-cu126-olddriver\, probe-log-cu126-olddriver\
    under -ResultRoot). That is what keeps the B-7 old-driver run from overwriting the B-6
    cu126 measurement it is supposed to be compared against.

    Every output of a run is deleted before the run starts (json, status, run.json, both
    server logs, the bench log and the wav directory of this slug). Otherwise a run that
    dies early leaves the previous run's numbers in place with a fresh timestamp beside
    them, and nothing downstream can tell which shot produced which number. That clearing
    happens AFTER the arguments and the preconditions are checked, so a bad -Variant or an
    unassembled runtime no longer deletes a good measurement it cannot replace, and the
    previous cuda-bench-<slug>.status.txt is kept one generation as .prev.status.txt.

    -CudaVisibleDevices is put into the child environment verbatim - index form ("0"),
    UUID form ("GPU-xxxxxxxx-...") or a list. decisions.md 34 asks seat B to fire the UUID
    route once; cuda_bench.py records what it asked for next to the uuid /ywk/status
    reports, in cuda_visible_devices_uuid.

    build/verify-runtime.ps1 is NOT touched and NOT called: the launch logic needed here is
    kept in this file on purpose so seat C can keep changing that script. Only the shared
    helpers of build/Common.ps1 are reused.

    Exit codes: 0 = every fired shot answered 200 with a wav. 3 = an operational failure
    (the server never became ready, a shot failed, the runtime is not assembled). 4 =
    -FailOnBudget was given and a budget was missed. A missed budget on its own is a
    measurement, not a script failure, so it does not change the exit code by default.

    On a non-zero exit the measurement JSON is kept on purpose - it is the evidence of what
    failed - but a cuda-bench-<variant>.status.txt saying EXIT=<n> is written next to it so
    nothing downstream can mistake a failed run for a good one.

    That status.txt also carries CHECKS=OK|FAIL|UNKNOWN|UNREADABLE|NO-JSON, taken from the
    checks[] array cuda_bench.py writes. Only ONE of those checks ("every fired shot answered
    200 with a wav") moves the exit code, so a /health, /params or /ywk/status that answered
    something other than 200 can sit inside an EXIT=0 run. Judge a run on EXIT=0 AND CHECKS=OK.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File probe\cuda-bench.ps1 -Variant cu130 -Device cuda:0 -Precision bf16 -HfHome C:\ywk\models -ResultRoot N:\temp_for_claudecode_agents\irodori-ywk\rtx
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File probe\cuda-bench.ps1 -Variant cpu -Device cpu -Precision fp32 -Quick
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File probe\cuda-bench.ps1 -Variant cu130 -Device cuda:0 -Precision bf16 -HfHome C:\ywk\models -CudaVisibleDevices GPU-01234567-89ab-cdef-0123-456789abcdef -Quick -Label uuidroute
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cu130', 'cu126', 'rocm-gfx1151')]
    [string]$Variant = 'cu130',
    [string]$Device = '',
    [ValidateSet('', 'fp32', 'bf16')]
    [string]$Precision = '',
    [string]$HfHome = '',
    [string]$CudaVisibleDevices = '',
    [int]$Port = 18090,
    [string]$OutRoot = '',
    [string]$ResultRoot = '',
    [string]$CasesPath = '',
    [string]$BenchPython = '',
    [string]$ReferencePresetId = '',
    [string]$Ref10Wav = '',
    [double]$Ref10Seconds = 10.0,
    [int]$WarmReps = 3,
    [int]$ReadyTimeoutSeconds = 600,
    [int]$ShotTimeoutSeconds = 1800,
    [string]$Label = '',
    [switch]$Quick,
    [switch]$NoPresetSweep,
    [switch]$FailOnBudget
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\build\Common.ps1')

$RepoRoot = Get-YwkRepoRoot
if ([string]::IsNullOrEmpty($OutRoot)) { $OutRoot = Join-Path $RepoRoot 'build\out' }
if ([string]::IsNullOrEmpty($CasesPath)) { $CasesPath = Join-Path $PSScriptRoot 'bench_cases.json' }

# --------------------------------------------------------------------------- slug
#
# The slug names every output of this run. Without -Label it is the bare variant, so the
# runbook's B-4 and B-6 keep reading cuda-bench-cu130.json and cuda-bench-cu126.json. With
# -Label it carries the label, so the B-7 old-driver run (-Label cu126-olddriver) writes
# cuda-bench-cu126-olddriver.* and CANNOT overwrite the B-6 cu126 measurement.
$LabelGiven = (-not [string]::IsNullOrEmpty($Label))
$Slug = $Variant
if ($LabelGiven) {
    $safe = ($Label -replace '[^A-Za-z0-9._-]', '-')
    $safe = $safe.Trim('-')
    if ([string]::IsNullOrEmpty($safe)) {
        $safe = 'label'
    }
    if ($safe -like ($Variant + '*')) {
        $Slug = $safe
    } else {
        $Slug = $Variant + '-' + $safe
    }
}

$RuntimeDir = Join-Path $OutRoot ('runtime-' + $Variant)
$AppDir     = Join-Path $OutRoot 'app'
$AppServer  = Join-Path $AppDir 'server'
$LogDir     = Join-Path $OutRoot 'probe-log'
$WorkDir    = Join-Path $OutRoot ('probe-work-' + $Slug)
$VoicesDir  = Join-Path $WorkDir 'voices'
$WavDir     = Join-Path $LogDir ('wav-' + $Slug)
$BenchScript = Join-Path $PSScriptRoot 'cuda_bench.py'
$PresetDir  = Join-Path $RepoRoot 'voices\presets'
# The transcript of this run. It is still OPEN while the rest of the script writes to it, so it
# is copied to -ResultRoot last of all, after the final line (see the end of this file).
$RunLog     = Join-Path $LogDir ('cuda-bench-' + $Slug + '.log')

$null = New-YwkDirectory -Path $LogDir
# Start-YwkLog is NOT called here. It truncates cuda-bench-<slug>.log, and a run that dies in
# the preconditions below would leave that transcript describing the failed attempt while the
# json and status.txt beside it still belonged to the previous, good run. It is opened once the
# preconditions have passed, together with the rest of this slug's clearing.

# --------------------------------------------------------------------------- defaults

if ([string]::IsNullOrEmpty($Device)) {
    if ($Variant -eq 'cpu') { $Device = 'cpu' } else { $Device = 'cuda:0' }
}
if ([string]::IsNullOrEmpty($Precision)) {
    if ($Device -like 'cpu*') { $Precision = 'fp32' } else { $Precision = 'bf16' }
}
if ([string]::IsNullOrEmpty($HfHome)) {
    if (-not [string]::IsNullOrEmpty($env:HF_HOME)) {
        $HfHome = $env:HF_HOME
    } else {
        $HfHome = Join-Path $env:USERPROFILE '.cache\huggingface'
    }
}
if ([string]::IsNullOrEmpty($Label)) {
    $Label = $Variant + '-' + ($Device -replace '[:\\/]', '') + '-' + $Precision
}
if ([string]::IsNullOrEmpty($BenchPython)) { $BenchPython = Join-Path $RuntimeDir 'python.exe' }

$JsonOut   = Join-Path $LogDir ('cuda-bench-' + $Slug + '.json')
$StatusOut = Join-Path $LogDir ('cuda-bench-' + $Slug + '.status.txt')
# The status of the run this one is about to overwrite, kept for exactly one generation. A run
# that is fired twice used to leave no trace at all of the first attempt's verdict.
$PrevStatusOut = Join-Path $LogDir ('cuda-bench-' + $Slug + '.prev.status.txt')
$ReportOut = Join-Path $LogDir ('cuda-bench-' + $Slug + '.run.json')
$StdoutLog = Join-Path $LogDir ('server-' + $Slug + '.out.log')
$StderrLog = Join-Path $LogDir ('server-' + $Slug + '.err.log')
$BenchLog  = Join-Path $LogDir ('cuda-bench-' + $Slug + '.bench.log')

# --------------------------------------------------------------------------- preconditions
#
# These run BEFORE the previous run's outputs are cleared. A typo in -Variant, a runtime that
# was never assembled or a missing HF_HOME used to throw only AFTER the clear, which deleted a
# good measurement and produced nothing to replace it with. Nothing is deleted until the run
# is known to be able to start.

$python = Join-Path $RuntimeDir 'python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    throw ('runtime not assembled: ' + $python + ' is missing. Run build\assemble-runtime.ps1 -Variant ' + $Variant + ' first.')
}
if (-not (Test-Path -LiteralPath (Join-Path $AppServer 'ywk_server.py'))) {
    throw ('app not assembled: ' + (Join-Path $AppServer 'ywk_server.py') + ' is missing. Run build\assemble-app.ps1 first.')
}
if (-not (Test-Path -LiteralPath $BenchScript)) {
    throw ('missing ' + $BenchScript)
}
if (-not (Test-Path -LiteralPath $CasesPath)) {
    throw ('missing ' + $CasesPath)
}
if (-not (Test-Path -LiteralPath $BenchPython)) {
    throw ('bench python not found: ' + $BenchPython)
}
if (-not (Test-Path -LiteralPath $HfHome)) {
    throw ('HF_HOME does not exist: ' + $HfHome + '. Fetch the models first (server\ywk_fetch_models.py --dest ...).')
}

$cases = Read-YwkJsonFile -Path $CasesPath
if ([string]::IsNullOrEmpty($ReferencePresetId)) {
    $ReferencePresetId = [string]$cases.reference_voices.primary_preset_id
}
$noRefVoice = [string]$cases.reference_voices.none.voice
$ref10Suffix = [string]$cases.reference_voices.ref10.voice_id_suffix

# --------------------------------------------------------------------------- open the transcript
#
# Everything above this line printed to the console only. From here the run is committed: the
# preconditions passed, so truncating this slug's transcript can no longer orphan a good
# measurement.
$null = Start-YwkLog -Path $RunLog
Write-YwkLog -Level 'STEP' -Message ('cuda-bench: variant=' + $Variant + ' device=' + $Device + ' precision=' + $Precision + ' port=' + $Port + ' quick=' + [bool]$Quick)
Write-YwkLog -Message ('label=' + $Label + '  slug=' + $Slug + ' (every output of this run is named after the slug)')
Write-YwkLog -Message ('hf_home=' + $HfHome)
Write-YwkLog -Message ('cases=' + $CasesPath)
if ([string]::IsNullOrEmpty($CudaVisibleDevices)) {
    Write-YwkLog -Message 'CUDA_VISIBLE_DEVICES: not set by this run (inherited, if anything)'
} else {
    Write-YwkLog -Message ('CUDA_VISIBLE_DEVICES=' + $CudaVisibleDevices + ' (passed through verbatim; decisions.md 34)')
}

# --------------------------------------------------------------------------- clear the slug
#
# Delete everything this run is about to write BEFORE it writes anything. A run that dies
# half way otherwise leaves the previous run's json next to a fresh status.txt, and no
# reader can tell that the numbers and the timestamp came from different runs.
#
# The one thing that is NOT deleted is the previous verdict: cuda-bench-<slug>.status.txt is
# renamed to cuda-bench-<slug>.prev.status.txt first. Fire the same slug twice and the first
# attempt's EXIT= line survives one generation instead of vanishing without a trace.
if (Test-Path -LiteralPath $StatusOut) {
    Copy-Item -LiteralPath $StatusOut -Destination $PrevStatusOut -Force
    Write-YwkLog -Message ('kept the previous verdict as ' + (Split-Path -Leaf $PrevStatusOut))
} elseif (Test-Path -LiteralPath $PrevStatusOut) {
    # There is no previous run to keep, so an older .prev.status.txt would be two generations
    # stale and would read as if it belonged to the run before this one.
    Remove-Item -LiteralPath $PrevStatusOut -Force
    Write-YwkLog -Message ('cleared the stale ' + (Split-Path -Leaf $PrevStatusOut) + ' (there was no previous status.txt to keep)')
}
$stale = @($JsonOut, $StatusOut, $ReportOut, $StdoutLog, $StderrLog, $BenchLog)
foreach ($f in $stale) {
    if (Test-Path -LiteralPath $f) {
        Remove-Item -LiteralPath $f -Force
        Write-YwkLog -Message ('cleared the previous run output ' + (Split-Path -Leaf $f))
    }
}
if (Test-Path -LiteralPath $WavDir) {
    Remove-Item -LiteralPath $WavDir -Recurse -Force
    Write-YwkLog -Message ('cleared the previous wav directory ' + $WavDir)
}

# --------------------------------------------------------------------------- helpers

function Get-YwkNvidiaSmiPath {
    [CmdletBinding()]
    param()
    $found = Get-Command 'nvidia-smi' -ErrorAction SilentlyContinue
    if ($null -ne $found) { return [string]$found.Source }
    $root = $env:SystemRoot
    if ([string]::IsNullOrEmpty($root)) { $root = 'C:\Windows' }
    $fallback = Join-Path $root 'System32\nvidia-smi.exe'
    if (Test-Path -LiteralPath $fallback) { return $fallback }
    return $null
}

function Get-YwkVramUsedMib {
    <#
      .SYNOPSIS
        memory.used of GPU 0 in MiB, or $null when there is no nvidia-smi (CPU machine).
    #>
    [CmdletBinding()]
    param()
    $exe = Get-YwkNvidiaSmiPath
    if ($null -eq $exe) { return $null }
    try {
        # 'console' can mis-decode non-ASCII under a redirected host: see the long note in
        # Invoke-YwkTreeKill. Harmless here -- the line is reduced to its digits below.
        $r = Invoke-YwkNative -FilePath $exe -Arguments @('--query-gpu=memory.used', '--format=csv,noheader') -TimeoutSeconds 60 -StdioEncoding 'console'
    } catch {
        return $null
    }
    if ($r.ExitCode -ne 0) { return $null }
    $line = (($r.StdOut -split "`n") | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 1)
    if ($null -eq $line) { return $null }
    $digits = ($line -replace '[^0-9]', '')
    if ([string]::IsNullOrEmpty($digits)) { return $null }
    return [int]$digits
}

function Invoke-YwkTreeKill {
    <#
      .SYNOPSIS
        Kill a process and everything it started. taskkill /T first, Stop-Process as the net.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Process)
    $report = New-Object System.Collections.Generic.List[string]
    if ($Process.HasExited) {
        $report.Add('already exited with code ' + $Process.ExitCode)
        return $report.ToArray()
    }
    $taskkill = Join-Path $env:SystemRoot 'System32\taskkill.exe'
    if (-not (Test-Path -LiteralPath $taskkill)) { $taskkill = 'taskkill.exe' }
    # -StdioEncoding 'console' asks Common.ps1 for [System.Console]::OutputEncoding, and that
    # is NOT always the console code page. Measured 2026-09-05 on this ja-JP machine: run from
    # a real console window it reports cp932 and the Japanese taskkill line lands in the log
    # intact; run with stdout redirected -- which is how a seat driving this through a tool
    # layer runs it -- [System.Console]::OutputEncoding reports utf-8 (cp 65001) while the
    # child still writes CP932 bytes, so every non-ASCII character of the taskkill line
    # becomes U+FFFD in cuda-bench-<slug>.log. That is a cosmetic defect of the transcript
    # only: nothing below reads $r.StdOut to decide anything. The kill is judged by
    # $r.ExitCode and by $Process.HasExited; Get-YwkVramUsedMib throws away every byte that is
    # not a digit; Invoke-YwkResultCopy judges robocopy by its exit code. Do not "fix" this by
    # forcing CP932 here: the same call is correct as-is under a real console, and pinning
    # 932 would be wrong on a machine that is not ja-JP.
    try {
        $r = Invoke-YwkNative -FilePath $taskkill -Arguments @('/PID', [string]$Process.Id, '/T', '/F') -TimeoutSeconds 60 -StdioEncoding 'console'
        $report.Add('taskkill /T /F exit=' + $r.ExitCode + ' ' + ($r.StdOut + ' ' + $r.StdErr).Trim())
    } catch {
        $report.Add('taskkill failed: ' + $_.Exception.Message)
    }
    $null = $Process.WaitForExit(20000)
    if (-not $Process.HasExited) {
        try {
            Stop-Process -Id $Process.Id -Force -ErrorAction Stop
            $report.Add('Stop-Process -Force sent')
        } catch {
            $report.Add('Stop-Process failed: ' + $_.Exception.Message)
        }
        $null = $Process.WaitForExit(15000)
    }
    if ($Process.HasExited) {
        $code = 'unknown'
        try {
            $Process.Refresh()
            $code = [string]$Process.ExitCode
        } catch {
            $code = 'unreadable (' + $_.Exception.GetType().Name + ')'
        }
        if ([string]::IsNullOrEmpty($code)) { $code = 'unknown' }
        $report.Add('exited with code ' + $code)
    } else {
        $report.Add('STILL ALIVE after taskkill and Stop-Process')
    }
    # Start-Process -PassThru often leaves ExitCode unreadable, so prove the kill the other
    # way: the pid must no longer be a live process.
    $still = Get-Process -Id $Process.Id -ErrorAction SilentlyContinue
    if ($null -eq $still) {
        $report.Add('pid ' + $Process.Id + ' is no longer a running process')
    } else {
        $report.Add('pid ' + $Process.Id + ' is STILL RUNNING (' + $still.ProcessName + ')')
    }
    return $report.ToArray()
}

function Invoke-YwkResultCopy {
    <#
      .SYNOPSIS
        Copy exactly the files THIS run produced to -ResultRoot. Not the whole log dir.
      .DESCRIPTION
        build\out\probe-log\ is shared: it holds every variant's bench output and seat C's
        rocm-warmup files as well. Robocopying the whole directory per run copied all of
        that under every variant's folder on N:, so the same numbers landed there several
        times over and a reader could not tell which copy the run had actually written.
        This copies the named outputs of this slug and nothing else.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string[]]$Files,
        [AllowEmptyString()][string]$WavSource,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    $null = New-YwkDirectory -Path $Destination
    $copied = New-Object System.Collections.Generic.List[string]
    $failed = New-Object System.Collections.Generic.List[string]
    foreach ($f in $Files) {
        if ([string]::IsNullOrEmpty($f)) { continue }
        if (-not (Test-Path -LiteralPath $f)) { continue }
        try {
            Copy-Item -LiteralPath $f -Destination (Join-Path $Destination (Split-Path -Leaf $f)) -Force
            $copied.Add((Split-Path -Leaf $f))
        } catch {
            $failed.Add((Split-Path -Leaf $f) + ': ' + $_.Exception.Message)
        }
    }
    $rcExit = 0
    if ((-not [string]::IsNullOrEmpty($WavSource)) -and (Test-Path -LiteralPath $WavSource)) {
        $wavDest = Join-Path $Destination (Split-Path -Leaf $WavSource)
        $null = New-YwkDirectory -Path $wavDest
        $robocopy = Join-Path $env:SystemRoot 'System32\robocopy.exe'
        if (-not (Test-Path -LiteralPath $robocopy)) { $robocopy = 'robocopy.exe' }
        $r = Invoke-YwkNative -FilePath $robocopy -Arguments @(
            $WavSource, $wavDest, '/E', '/R:2', '/W:2', '/NFL', '/NDL', '/NJH', '/NJS', '/NP'
        ) -TimeoutSeconds 3600 -StdioEncoding 'console'
        # 'console' can mis-decode non-ASCII under a redirected host: see the long note in
        # Invoke-YwkTreeKill. Harmless here -- the verdict is the exit code, and StdOut only
        # decorates the failure message.
        # robocopy: 0-7 are success, 8 and up are failures
        $rcExit = $r.ExitCode
        if ($rcExit -ge 8) {
            $failed.Add('robocopy of the wav dir exited ' + $rcExit + ': ' + ($r.StdOut + $r.StdErr).Trim())
        } else {
            $copied.Add((Split-Path -Leaf $WavSource) + '\')
        }
    }
    return @{
        Ok           = ($failed.Count -eq 0)
        Copied       = $copied.ToArray()
        Failed       = $failed.ToArray()
        RobocopyExit = $rcExit
    }
}

# --------------------------------------------------------------------------- stage voices

Write-YwkLog -Level 'STEP' -Message 'staging the reference voices'
if (Test-Path -LiteralPath $VoicesDir) {
    Remove-Item -LiteralPath $VoicesDir -Recurse -Force
}
$null = New-YwkDirectory -Path $VoicesDir
$null = New-YwkDirectory -Path $WavDir

$staged = New-Object System.Collections.Generic.List[object]
$aliases = [ordered]@{}
$aliases[$noRefVoice] = [ordered]@{ no_ref = $true }

$sweepPresets = @($cases.preset_sweep.presets)
$wanted = New-Object System.Collections.Generic.List[object]
if ($NoPresetSweep -or $Quick) {
    foreach ($p in $sweepPresets) {
        if ([string]$p.id -eq $ReferencePresetId) { $wanted.Add($p) }
    }
} else {
    foreach ($p in $sweepPresets) { $wanted.Add($p) }
}

foreach ($p in $wanted) {
    $id = [string]$p.id
    $src = Join-Path $PresetDir ($id + '.wav')
    if (-not (Test-Path -LiteralPath $src)) {
        Write-YwkLog -Level 'WARN' -Message ('preset wav missing, skipping: ' + $src)
        continue
    }
    $dst = Join-Path $VoicesDir ($id + '.wav')
    Copy-Item -LiteralPath $src -Destination $dst -Force
    $aliases[[string]$p.display_name] = ($id + '.wav')
    $staged.Add([ordered]@{ id = $id; file = ($id + '.wav'); kind = 'ref30'; source = $src })
}

# the 10 s reference: a head-trim of the shipped secondary wav, unless -Ref10Wav overrides it.
# build\out\preset-work\secondary\*_ref10_secondary.wav is NOT a 10 s clip - it is the secondary
# generated *from* the 10 s primary and is 25-36 s long (measured 2026-09-05) - so it is not used
# for this axis. See probe\bench_cases.json notes.
$ref10Id = $ReferencePresetId + $ref10Suffix
$ref10Dst = Join-Path $VoicesDir ($ref10Id + '.wav')
$ref10Src = $Ref10Wav
if ([string]::IsNullOrEmpty($ref10Src)) {
    $ref10Src = Join-Path $PresetDir ($ReferencePresetId + '.wav')
}
if (Test-Path -LiteralPath $ref10Src) {
    $trim = Invoke-YwkNative -FilePath $BenchPython -Arguments @(
        $BenchScript, '--trim-wav', $ref10Src, '--trim-out', $ref10Dst,
        '--trim-seconds', ([string]$Ref10Seconds)
    ) -WorkingDirectory $RepoRoot -Environment (New-YwkChildEnvironment) -TimeoutSeconds 300 -StdioEncoding 'utf8'
    if ($trim.ExitCode -ne 0) {
        throw ('the 10 s reference trim failed (exit ' + $trim.ExitCode + '): ' + $trim.StdErr.Trim())
    }
    Write-YwkLog -Message ('ref10: ' + $trim.StdOut.Trim())
    $staged.Add([ordered]@{ id = $ref10Id; file = ($ref10Id + '.wav'); kind = 'ref10'; source = $ref10Src })
} else {
    Write-YwkLog -Level 'WARN' -Message ('no source for the 10 s reference: ' + $ref10Src + ' (the ref10 conditions will be skipped)')
}

$voicesJson = Join-Path $VoicesDir 'voices.json'
$null = Write-YwkJsonFile -Path $voicesJson -Value $aliases -Depth 6
Write-YwkLog -Message ('staged ' + $staged.Count + ' wav(s) into ' + $VoicesDir + ' and wrote voices.json (' + $aliases.Count + ' alias(es))')

# --------------------------------------------------------------------------- environment

$childEnv = [ordered]@{
    'IRODORI_HOST'                    = '127.0.0.1'
    'IRODORI_PORT'                    = [string]$Port
    'IRODORI_HF_CHECKPOINT'           = 'Aratako/Irodori-TTS-v4.1-Small'
    'IRODORI_PRELOAD'                 = 'true'
    'IRODORI_EMPTY_CACHE_INTERVAL'    = '0'
    'IRODORI_ALLOW_NO_REF_VOICE'      = 'false'
    'IRODORI_DEFAULT_NUM_STEPS'       = '40'
    'IRODORI_DEFAULT_RESPONSE_FORMAT' = 'wav'
    'IRODORI_VOICES_DIR'              = $VoicesDir
    'IRODORI_VOICE_ALIASES_FILE'      = $voicesJson
    'IRODORI_MODEL_DEVICE'            = $Device
    'IRODORI_CODEC_DEVICE'            = $Device
    'IRODORI_MODEL_PRECISION'         = $Precision
    'IRODORI_CODEC_PRECISION'         = $Precision
    'HF_HOME'                         = $HfHome
    'HF_HUB_OFFLINE'                  = '1'
    'PYTHONUTF8'                      = '1'
    'PYTHONDONTWRITEBYTECODE'         = '1'
    'PYTHONUNBUFFERED'                = '1'
    'PYTHONIOENCODING'                = 'utf-8'
    # Explicitly OFF, not merely "not set". This machine's own shell may already export them
    # (seat C's Radeon work sets YWK_WARMUP_ON_START=1 to measure warm-up), and an inherited 1
    # would fire a hidden synthesis between ready and the first shot -- which is precisely the
    # shot run_cold and B-5's "unseen reference shape" measure. A warmed cold number is not a
    # cold number, and nothing in the JSON would say it had been warmed.
    'YWK_WARMUP_ON_START'             = '0'
    # Same reasoning for the reference-latent precompute. The wrapper DOES read this name:
    # server/ywk_server.py (seat C, delivery C(2), 2026-09-05) reads YWK_PRECOMPUTE_ON_START
    # and, when it is unset or empty, defaults to variant_is_rocm(); when it is set it uses
    # _truthy(raw). So '0' is not decoration on this machine - it is what keeps the reference
    # latent from being computed between ready and the first shot, which would turn run_cold
    # and B-5's unseen-reference-shape measure into warm numbers with nothing in the JSON to
    # say so. (An earlier revision of this comment said the wrapper did not read the name;
    # that was true of the wrapper as of the grep taken before delivery C(2) landed.)
    'YWK_PRECOMPUTE_ON_START'         = '0'
}
# IRODORI_DEFAULT_VOICE is left to the wrapper's own setdefault (decisions.md 45).

# decisions.md 34: the value goes on VERBATIM. Whatever the caller wrote - an index ("0"),
# a uuid ("GPU-xxxxxxxx-...") or a list - is what the driver is asked to honour; rewriting
# it here would destroy the very thing the experiment measures.
if (-not [string]::IsNullOrEmpty($CudaVisibleDevices)) {
    $childEnv['CUDA_VISIBLE_DEVICES'] = $CudaVisibleDevices
}

# $StdoutLog / $StderrLog / $BenchLog were named and deleted above, with the rest of this
# slug's previous outputs. PowerShell variable names are case insensitive, so the lines
# below that say $stdoutLog are these same three variables.

# Start-Process has no environment parameter on Windows PowerShell 5.1, so the variables go on
# this process (children inherit) and are restored in the finally block.
$saved = @{}
foreach ($k in $childEnv.Keys) {
    $saved[$k] = [System.Environment]::GetEnvironmentVariable($k)
    [System.Environment]::SetEnvironmentVariable($k, [string]$childEnv[$k])
}

$idleVram = Get-YwkVramUsedMib
Write-YwkLog -Message ('idle VRAM before start: ' + $(if ($null -eq $idleVram) { 'n/a (no nvidia-smi)' } else { [string]$idleVram + ' MiB' }))

$null = New-YwkDirectory -Path $WorkDir

$proc = $null
$killReport = @()
$benchExit = $null
$benchStdOut = ''
$benchStdErr = ''

try {
    Write-YwkLog -Level 'STEP' -Message ('launching ' + $python + ' -m ywk_server --host 127.0.0.1 --port ' + $Port)
    $proc = Start-Process -FilePath $python `
        -ArgumentList @('-m', 'ywk_server', '--host', '127.0.0.1', '--port', [string]$Port) `
        -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
    Write-YwkLog -Message ('server pid ' + $proc.Id)

    $benchArgs = New-Object System.Collections.Generic.List[string]
    $benchArgs.Add($BenchScript)
    $benchArgs.Add('--host');            $benchArgs.Add('127.0.0.1')
    $benchArgs.Add('--port');            $benchArgs.Add([string]$Port)
    $benchArgs.Add('--cases');           $benchArgs.Add($CasesPath)
    $benchArgs.Add('--json-out');        $benchArgs.Add($JsonOut)
    $benchArgs.Add('--wav-dir');         $benchArgs.Add($WavDir)
    $benchArgs.Add('--voices-dir');      $benchArgs.Add($VoicesDir)
    $benchArgs.Add('--reference-preset');$benchArgs.Add($ReferencePresetId)
    $benchArgs.Add('--warm-reps');       $benchArgs.Add([string]$WarmReps)
    $benchArgs.Add('--ready-timeout');   $benchArgs.Add([string]$ReadyTimeoutSeconds)
    $benchArgs.Add('--shot-timeout');    $benchArgs.Add([string]$ShotTimeoutSeconds)
    $benchArgs.Add('--label');           $benchArgs.Add($Label)
    $benchArgs.Add('--variant');         $benchArgs.Add($Variant)
    $benchArgs.Add('--device');          $benchArgs.Add($Device)
    $benchArgs.Add('--precision');       $benchArgs.Add($Precision)
    if ($null -ne $idleVram) {
        $benchArgs.Add('--idle-vram-mib'); $benchArgs.Add([string]$idleVram)
    }
    if ($Quick) { $benchArgs.Add('--quick') }
    if ($NoPresetSweep) { $benchArgs.Add('--no-preset-sweep') }
    if ($FailOnBudget) { $benchArgs.Add('--fail-on-budget') }

    Write-YwkLog -Level 'STEP' -Message ('running ' + $BenchPython + ' ' + (ConvertTo-YwkCommandLine -Arguments $benchArgs.ToArray()))
    $bench = Invoke-YwkNative -FilePath $BenchPython -Arguments $benchArgs.ToArray() `
        -WorkingDirectory $RepoRoot -Environment (New-YwkChildEnvironment) -TimeoutSeconds ($ReadyTimeoutSeconds + 7200) -StdioEncoding 'utf8'
    $benchExit = $bench.ExitCode
    $benchStdOut = $bench.StdOut
    $benchStdErr = $bench.StdErr
    $null = Write-YwkTextFile -Path $benchLog -Newline 'LF' -Text ($benchStdOut + "`n---- stderr ----`n" + $benchStdErr + "`n(exit " + $benchExit + ")`n")
    foreach ($line in ($benchStdOut -split "`n")) {
        $t = $line.TrimEnd()
        if ($t.Length -gt 0) { Write-YwkLog -Message $t }
    }
    if ($benchExit -ne 0) {
        Write-YwkLog -Level 'FAIL' -Message ('cuda_bench.py exited ' + $benchExit)
        if ($benchStdErr.Trim().Length -gt 0) {
            Write-YwkLog -Level 'FAIL' -Message ('stderr: ' + $benchStdErr.Trim())
        }
    }
} finally {
    if ($null -ne $proc) {
        Write-YwkLog -Level 'STEP' -Message ('killing the process tree of pid ' + $proc.Id)
        $killReport = Invoke-YwkTreeKill -Process $proc
        foreach ($line in $killReport) { Write-YwkLog -Message ('kill: ' + $line) }
    }
    foreach ($k in $childEnv.Keys) {
        [System.Environment]::SetEnvironmentVariable($k, $saved[$k])
    }
}

# --------------------------------------------------------------------------- after the kill

if (Test-Path -LiteralPath $JsonOut) {
    Write-YwkLog -Level 'STEP' -Message 'reading VRAM 10 s after the kill'
    $post = Invoke-YwkNative -FilePath $BenchPython -Arguments @(
        $BenchScript, '--post-kill', '--json-out', $JsonOut, '--wait-seconds', '10'
    ) -WorkingDirectory $RepoRoot -Environment (New-YwkChildEnvironment) -TimeoutSeconds 300 -StdioEncoding 'utf8'
    foreach ($line in ($post.StdOut -split "`n")) {
        $t = $line.TrimEnd()
        if ($t.Length -gt 0) { Write-YwkLog -Message $t }
    }
    if ($post.ExitCode -ne 0) {
        Write-YwkLog -Level 'WARN' -Message ('post-kill VRAM read failed (exit ' + $post.ExitCode + '): ' + $post.StdErr.Trim())
    }
} else {
    Write-YwkLog -Level 'WARN' -Message ('no measurement JSON at ' + $JsonOut + ' - skipping the post-kill VRAM read')
}

$exitCode = 3
if ($null -ne $benchExit) { $exitCode = [int]$benchExit }

# --------------------------------------------------------------------------- checks -> status
#
# cuda_bench.py's checks[] carries the pass/fail of GET /health, GET /params, GET /ywk/status
# and "every fired shot answered 200 with a wav". Only the LAST of those moves the exit code:
# a /params that answered 500 while every shot still returned a wav left EXIT=0 and the failure
# only visible to whoever opened the JSON and read the array. The exit code is deliberately not
# changed here (it means "every fired shot answered 200"), but the verdict is now printed where
# the runbook actually looks, so B-4 can be judged on "EXIT=0 AND CHECKS=OK".
$checksState = 'UNKNOWN'
$checksTotal = 0
$checksFailed = New-Object System.Collections.Generic.List[string]
if (Test-Path -LiteralPath $JsonOut) {
    try {
        $measured = Read-YwkJsonFile -Path $JsonOut
        $checkList = @()
        if (($measured.PSObject.Properties.Name) -contains 'checks') {
            $checkList = @($measured.checks)
        }
        $checksTotal = $checkList.Count
        foreach ($c in $checkList) {
            if (-not [bool]$c.ok) { $checksFailed.Add([string]$c.check) }
        }
        if ($checksTotal -eq 0) {
            $checksState = 'UNKNOWN'
        } elseif ($checksFailed.Count -eq 0) {
            $checksState = 'OK'
        } else {
            $checksState = 'FAIL'
        }
    } catch {
        # A JSON that cannot be parsed is itself a failure to report, not a reason to die
        # after the measurement has already been taken.
        $checksState = 'UNREADABLE'
        $checksFailed.Add('could not read ' + $JsonOut + ': ' + $_.Exception.Message)
    }
} else {
    $checksState = 'NO-JSON'
    $checksFailed.Add('no measurement JSON at ' + $JsonOut)
}
if ($checksState -eq 'OK') {
    Write-YwkLog -Message ('CHECKS=OK (' + $checksTotal + ' check(s) in the measurement JSON, none failed)')
} else {
    Write-YwkLog -Level 'WARN' -Message ('CHECKS=' + $checksState + ' (' + $checksTotal + ' check(s), ' + $checksFailed.Count + ' failed): ' + ($checksFailed -join ' | '))
}

$report = [ordered]@{
    generated        = Get-YwkTimestamp
    tool             = 'probe/cuda-bench.ps1'
    variant          = $Variant
    device           = $Device
    precision        = $Precision
    port             = $Port
    label            = $Label
    slug             = $Slug
    cuda_visible_devices = $CudaVisibleDevices
    quick            = [bool]$Quick
    preset_sweep     = (-not ([bool]$NoPresetSweep -or [bool]$Quick))
    hf_home          = $HfHome
    cases_path       = $CasesPath
    reference_preset = $ReferencePresetId
    ref10_seconds    = $Ref10Seconds
    voices_dir       = $VoicesDir
    staged_voices    = $staged.ToArray()
    idle_vram_mib    = $idleVram
    bench_exit_code  = $benchExit
    kill_report      = $killReport
    measurement_json = $JsonOut
    wav_dir          = $WavDir
    server_stdout    = $stdoutLog
    server_stderr    = $stderrLog
    bench_log        = $benchLog
    run_log          = $RunLog
    previous_status  = $PrevStatusOut
    checks_state     = $checksState
    checks_total     = $checksTotal
    checks_failed    = $checksFailed.ToArray()
    exit_code        = $exitCode
    ok               = ($exitCode -eq 0)
}
$null = Write-YwkJsonFile -Path $ReportOut -Value $report -Depth 8

# status.txt is NOT written here. It is written at the very bottom of this file, after
# everything that can still move $exitCode. The runbook judges every run by the EXIT= and
# CHECKS= lines of status.txt; written at this point, status.txt said EXIT=0 / OK=True for a
# run whose copy to -ResultRoot then failed and which therefore exited 3, so the file the
# runbook reads disagreed with the code the script returned. One writer, one moment, last.
function Write-YwkRunStatus {
    <#
      .SYNOPSIS
        Write status.txt for this run at the given exit code. Reads the rest from script scope.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][int]$Code)
    return (Write-YwkTextFile -Path $StatusOut -Newline 'CRLF' -Text (
        'EXIT=' + $Code + "`n" +
        'OK=' + [string]($Code -eq 0) + "`n" +
        'CHECKS=' + $checksState + "`n" +
        'CHECKS_TOTAL=' + $checksTotal + "`n" +
        'CHECKS_FAILED=' + ($checksFailed -join '; ') + "`n" +
        'VARIANT=' + $Variant + "`n" +
        'DEVICE=' + $Device + "`n" +
        'PRECISION=' + $Precision + "`n" +
        'LABEL=' + $Label + "`n" +
        'SLUG=' + $Slug + "`n" +
        'CUDA_VISIBLE_DEVICES=' + $CudaVisibleDevices + "`n" +
        'JSON=' + $JsonOut + "`n" +
        'GENERATED=' + (Get-YwkTimestamp) + "`n"
    ))
}

if (-not [string]::IsNullOrEmpty($ResultRoot)) {
    # The ONE route results take off this machine. The runbook does not robocopy
    # build\out\probe-log again afterwards: doing that put the same files under two names
    # on N: and made "which copy is the run's own?" unanswerable.
    $dest = Join-Path $ResultRoot ('probe-log-' + $Slug)
    Write-YwkLog -Level 'STEP' -Message ('copying this run''s outputs -> ' + $dest)
    # Two files are NOT in this list, for the same reason in two shapes: neither one has its
    # final content yet.
    #   cuda-bench-<slug>.log -- the transcript this script is still writing to, so a copy
    #     taken here is truncated at whatever line was last flushed.
    #   <slug>.status.txt     -- it does not exist yet; its EXIT= depends on whether THIS copy
    #     succeeds.
    # Both are written/copied on their own at the bottom of this file, in that order.
    # The try is not decoration: Invoke-YwkResultCopy creates $dest OUTSIDE its own try, so an
    # unreachable or unwritable result root throws, and with $ErrorActionPreference = 'Stop'
    # that would kill this script before status.txt existed at all. Turning it into EXIT=3
    # keeps the promise the runbook relies on: every finished run leaves a status.txt whose
    # EXIT= is the code this script returned.
    try {
        $copy = Invoke-YwkResultCopy -Files @(
            $JsonOut, $PrevStatusOut, $ReportOut, $StdoutLog, $StderrLog, $BenchLog
        ) -WavSource $WavDir -Destination $dest
        foreach ($n in $copy.Copied) { Write-YwkLog -Message ('  copied ' + $n) }
        if ($copy.Ok) {
            Write-YwkLog -Message ('result copy ok (' + $copy.Copied.Count + ' item(s)); the transcript follows after the last line')
        } else {
            foreach ($n in $copy.Failed) { Write-YwkLog -Level 'FAIL' -Message ('  ' + $n) }
            Write-YwkLog -Level 'FAIL' -Message 'the result copy did not complete'
            if ($exitCode -eq 0) { $exitCode = 3 }
        }
    } catch {
        Write-YwkLog -Level 'FAIL' -Message ('the result copy threw: ' + $_.Exception.Message)
        if ($exitCode -eq 0) { $exitCode = 3 }
    }
}

if ($exitCode -eq 0) {
    Write-YwkLog -Level 'STEP' -Message ('cuda-bench finished: EXIT=0  CHECKS=' + $checksState + '  ' + $JsonOut)
} else {
    Write-YwkLog -Level 'FAIL' -Message ('cuda-bench finished: EXIT=' + $exitCode + '  CHECKS=' + $checksState + '  see ' + $StatusOut + ' and ' + $JsonOut)
}

# The transcript, next to last: every Write-YwkLog above has already landed in it. Nothing is
# logged with Write-YwkLog after this point, because anything logged here would not be in the
# copy. What follows uses Write-Host only, which does not touch the transcript.
if ((-not [string]::IsNullOrEmpty($ResultRoot)) -and (Test-Path -LiteralPath $RunLog)) {
    $logDest = Join-Path (Join-Path $ResultRoot ('probe-log-' + $Slug)) (Split-Path -Leaf $RunLog)
    try {
        $null = New-YwkDirectory -Path (Split-Path -Parent $logDest)
        Copy-Item -LiteralPath $RunLog -Destination $logDest -Force
        Write-Host ('[cuda-bench] copied the complete transcript to ' + $logDest)
    } catch {
        Write-Host ('[cuda-bench] WARN could not copy the transcript to ' + $logDest + ': ' + $_.Exception.Message)
        if ($exitCode -eq 0) { $exitCode = 3 }
    }
}

# status.txt, last of all, at the exit code this script is actually about to return. Every
# statement that could still raise $exitCode to 3 is above this line.
$null = Write-YwkRunStatus -Code $exitCode
Write-Host ('[cuda-bench] wrote ' + $StatusOut + '  EXIT=' + $exitCode)
if (-not [string]::IsNullOrEmpty($ResultRoot)) {
    $statusDest = Join-Path (Join-Path $ResultRoot ('probe-log-' + $Slug)) (Split-Path -Leaf $StatusOut)
    try {
        $null = New-YwkDirectory -Path (Split-Path -Parent $statusDest)
        Copy-Item -LiteralPath $StatusOut -Destination $statusDest -Force
        Write-Host ('[cuda-bench] copied status.txt to ' + $statusDest)
    } catch {
        Write-Host ('[cuda-bench] WARN could not copy status.txt to ' + $statusDest + ': ' + $_.Exception.Message)
        if ($exitCode -eq 0) {
            $exitCode = 3
            $null = Write-YwkRunStatus -Code $exitCode
            Write-Host ('[cuda-bench] rewrote the local ' + $StatusOut + ' with EXIT=3; there is NO status.txt under ' + $statusDest)
        }
    }
}
exit $exitCode
