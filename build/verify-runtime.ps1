<#
.SYNOPSIS
    Start the assembled runtime, prove the wrapper answers, and always kill the child.

.DESCRIPTION
    Acceptance conditions A-3 and (with -WithModel) A-4.

    Without -WithModel the server runs with IRODORI_PRELOAD=false, so it must answer within
    seconds and must report runtime.loaded = false:
        GET /health       200
        GET /params       200 and under -ParamsBudgetMs (default 100 ms)
        GET /ywk/status   200, and its "variant" field really is -Variant

    With -WithModel the /ywk/status body is read, not merely saved: device.actual and
    device.precision have to be -Device / -Precision, and on a rocm variant device.name
    must be non-empty, device.hip (torch.version.hip) non-null and device.gcn_arch equal to
    the architecture in the variant tag (rocm-gfx1151 -> gfx1151). That is acceptance
    condition C-3; without these the run passed on a wrapper answering device.actual: null.
    On a cu* / cpu variant device.hip must be null instead -- a hip version there is a ROCm
    torch under the wrong label.

    With -WithModel the model is loaded from the existing Hugging Face cache. HF_HOME is
    pointed at -HfHome (default %USERPROFILE%\.cache\huggingface) and HF_HUB_OFFLINE=1 is set,
    so nothing is downloaded and nothing in that cache is written (decisions.md 22). The run
    then posts short syntheses at num_steps=4 and checks the answers really are RIFF/WAVE.

    The three shots -WithModel makes, and why each one is there:
      1. voice = the default speaker name (U+30C7 U+30D5 U+30A9 U+30EB U+30C8) -- no reference.
      2. no "voice" field at all -- decisions.md 45: /params names that same speaker as the default
         and required:false, so a body that simply omits the field must be a 200 too. Convoy A
         carried this over; it is checked here.
      3. voice = a reference wav written from shot 1 -- the only shot that opens an audio file,
         which is the path patches/0001 fixes (torchaudio raises ImportError without torchcodec).

    -Device / -Precision default off the variant: cpu -> cpu/fp32, everything else -> cuda:0
    and bf16 (decisions.md 5 and 36 make bf16 mandatory on Radeon; the wrapper enforces it).

    While the wrapper is not in place yet (seat S2 delivers server/ywk_server.py), pass
    -HealthOnly: only GET /health is required and the other two routes are reported, not enforced.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\verify-runtime.ps1 -Variant cpu
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\verify-runtime.ps1 -Variant cpu -WithModel
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\verify-runtime.ps1 -Variant rocm-gfx1151 -WithModel
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cu130', 'cu126', 'rocm-gfx1151')]
    [string]$Variant = 'cpu',
    [int]$Port = 18089,
    [switch]$WithModel,
    [switch]$HealthOnly,
    [int]$StartupTimeoutSeconds = 90,
    [int]$ReadyTimeoutSeconds = 300,
    [int]$ParamsBudgetMs = 100,
    [string]$Device = '',
    # Upstream resolve_runtime_dtype accepts fp32 and bf16 only
    # (upstream/Irodori-TTS/irodori_tts/inference_runtime.py:304-312), and bf16 there requires
    # a CUDA or XPU device -- ROCm's torch reports device type "cuda", so it qualifies.
    [ValidateSet('', 'fp32', 'bf16')]
    [string]$Precision = '',
    [string]$HfHome = '',
    [string]$OutRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$RepoRoot = Get-YwkRepoRoot
if ([string]::IsNullOrEmpty($OutRoot)) { $OutRoot = Join-Path $RepoRoot 'build\out' }
$RuntimeDir = Join-Path $OutRoot ('runtime-' + $Variant)
$AppDir     = Join-Path $OutRoot 'app'
$AppServer  = Join-Path $AppDir 'server'
$AppVoices  = Join-Path $AppDir 'voices'
$LogDir     = Join-Path $OutRoot 'verify-log'
$WorkDir    = Join-Path $OutRoot ('verify-work-' + $Variant)

$null = New-YwkDirectory -Path $LogDir
$null = Start-YwkLog -Path (Join-Path $LogDir ('verify-runtime-' + $Variant + '.log'))

# Variant defaults. The variant, not the caller, decides what "the obvious device" is; an
# explicit -Device / -Precision always wins so a Radeon box can still be driven on the cpu.
if ([string]::IsNullOrEmpty($Device)) {
    if ($Variant -eq 'cpu') { $Device = 'cpu' } else { $Device = 'cuda:0' }
}
if ([string]::IsNullOrEmpty($Precision)) {
    if ($Device -eq 'cpu') { $Precision = 'fp32' } else { $Precision = 'bf16' }
}
if ($Device -eq 'cpu' -and $Precision -ne 'fp32') {
    throw ('-Device cpu with -Precision ' + $Precision + ': the wrapper ties cpu to fp32 (design doc section 4-6).')
}
if ($Variant -like 'rocm*' -and $Device -ne 'cpu' -and $Precision -ne 'bf16') {
    throw ('-Variant ' + $Variant + ' with -Precision ' + $Precision +
           ': the Radeon variant is bf16-only (decisions.md 5 and 36); the wrapper exits 2 on anything else.')
}
if ([string]::IsNullOrEmpty($HfHome)) {
    $HfHome = Join-Path $env:USERPROFILE '.cache\huggingface'
}

Write-YwkLog -Level 'STEP' -Message ('verify-runtime: variant=' + $Variant + ' port=' + $Port +
                                     ' device=' + $Device + ' precision=' + $Precision +
                                     ' withModel=' + [bool]$WithModel)

$python = Join-Path $RuntimeDir 'python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    throw ('runtime not assembled: ' + $python + ' is missing. Run build\assemble-runtime.ps1 -Variant ' + $Variant + ' first.')
}
if (-not (Test-Path -LiteralPath (Join-Path $AppServer 'ywk_server.py'))) {
    throw ('app not assembled: ' + (Join-Path $AppServer 'ywk_server.py') + ' is missing. Run build\assemble-app.ps1 first.')
}

$null = New-YwkDirectory -Path $WorkDir
$null = New-YwkDirectory -Path $AppVoices

# --------------------------------------------------------------------------- http helpers

function Invoke-YwkProbe {
    <#
      .OUTPUTS
        @{ Status; Body; Bytes; ElapsedMs; ContentType }
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [string]$Method = 'GET',
        [AllowEmptyString()][string]$JsonBody = '',
        [int]$TimeoutSeconds = 600
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = $Method
    $req.Timeout = $TimeoutSeconds * 1000
    $req.ReadWriteTimeout = $TimeoutSeconds * 1000
    $req.UserAgent = 'irodori-tts-ywk-verify/1'
    $req.Accept = '*/*'
    if (-not [string]::IsNullOrEmpty($JsonBody)) {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($JsonBody)
        $req.ContentType = 'application/json; charset=utf-8'
        $req.ContentLength = $bytes.Length
        $rs = $req.GetRequestStream()
        try { $rs.Write($bytes, 0, $bytes.Length) } finally { $rs.Dispose() }
    }
    $resp = $null
    $status = 0
    $ctype = ''
    $data = New-Object byte[] 0
    try {
        $resp = [System.Net.HttpWebResponse]$req.GetResponse()
    } catch {
        $ex = $_.Exception
        $depth = 0
        while ($null -ne $ex -and $depth -lt 6) {
            if ($ex -is [System.Net.WebException]) { break }
            $ex = $ex.InnerException
            $depth = $depth + 1
        }
        if ($null -eq $ex -or $null -eq $ex.Response) {
            $sw.Stop()
            return @{ Status = 0; Body = ('transport error: ' + $_.Exception.Message); Bytes = $data; ElapsedMs = $sw.ElapsedMilliseconds; ContentType = '' }
        }
        $resp = [System.Net.HttpWebResponse]$ex.Response
    }
    try {
        $status = [int]$resp.StatusCode
        $ctype = [string]$resp.ContentType
        $ms = New-Object System.IO.MemoryStream
        try {
            $resp.GetResponseStream().CopyTo($ms)
            $data = $ms.ToArray()
        } finally {
            $ms.Dispose()
        }
    } finally {
        $resp.Close()
    }
    $sw.Stop()
    $text = ''
    if ($data.Length -gt 0 -and $ctype -notlike 'audio/*') {
        $text = [System.Text.Encoding]::UTF8.GetString($data)
    }
    return @{ Status = $status; Body = $text; Bytes = $data; ElapsedMs = $sw.ElapsedMilliseconds; ContentType = $ctype }
}

function Test-YwkRiffWave {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    if ($Bytes.Length -lt 44) { return $false }
    $riff = [System.Text.Encoding]::ASCII.GetString($Bytes, 0, 4)
    $wave = [System.Text.Encoding]::ASCII.GetString($Bytes, 8, 4)
    return ($riff -eq 'RIFF' -and $wave -eq 'WAVE')
}

# --------------------------------------------------------------------------- environment

$defaultName = [string]([char]0x30C7 + [char]0x30D5 + [char]0x30A9 + [char]0x30EB + [char]0x30C8)
$voicesJson = Join-Path $AppVoices 'voices.json'
if (-not (Test-Path -LiteralPath $voicesJson)) {
    $v = [ordered]@{}
    $v[$defaultName] = [ordered]@{ no_ref = $true }
    $null = Write-YwkJsonFile -Path $voicesJson -Value $v -Depth 6
}

$childEnv = [ordered]@{
    'IRODORI_HOST'                  = '127.0.0.1'
    'IRODORI_PORT'                  = [string]$Port
    'IRODORI_HF_CHECKPOINT'         = 'Aratako/Irodori-TTS-v4.1-Small'
    'IRODORI_EMPTY_CACHE_INTERVAL'  = '0'
    'IRODORI_ALLOW_NO_REF_VOICE'    = 'false'
    'IRODORI_DEFAULT_NUM_STEPS'     = '40'
    'IRODORI_DEFAULT_RESPONSE_FORMAT' = 'wav'
    'IRODORI_VOICES_DIR'            = $AppVoices
    'IRODORI_VOICE_ALIASES_FILE'    = $voicesJson
    'IRODORI_MODEL_DEVICE'          = $Device
    'IRODORI_CODEC_DEVICE'          = $Device
    'YWK_VARIANT'                   = $Variant
    'PYTHONUTF8'                    = '1'
    'PYTHONDONTWRITEBYTECODE'       = '1'
    'PYTHONUNBUFFERED'              = '1'
    'PYTHONIOENCODING'              = 'utf-8'
}
if ($WithModel) {
    if (-not (Test-Path -LiteralPath $HfHome)) {
        throw ('-WithModel needs a Hugging Face cache at ' + $HfHome + ' (read only). It is not there.')
    }
    $childEnv['HF_HOME'] = $HfHome
    $childEnv['HF_HUB_OFFLINE'] = '1'
    $childEnv['IRODORI_PRELOAD'] = 'true'
    $childEnv['IRODORI_MODEL_PRECISION'] = $Precision
    $childEnv['IRODORI_CODEC_PRECISION'] = $Precision
} else {
    $childEnv['HF_HUB_OFFLINE'] = '1'
    $childEnv['IRODORI_PRELOAD'] = 'false'
}

$stdoutLog = Join-Path $LogDir ('server-' + $Variant + '.out.log')
$stderrLog = Join-Path $LogDir ('server-' + $Variant + '.err.log')
foreach ($f in @($stdoutLog, $stderrLog)) {
    if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
}

# Start-Process has no environment parameter on Windows PowerShell 5.1, so the variables are
# set on this process (children inherit) and restored in the finally block.
$saved = @{}
foreach ($k in $childEnv.Keys) {
    $saved[$k] = [System.Environment]::GetEnvironmentVariable($k)
    [System.Environment]::SetEnvironmentVariable($k, [string]$childEnv[$k])
}

$proc = $null
$results = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]
$base = 'http://127.0.0.1:' + $Port

function Add-YwkResult {
    param([string]$Name, [bool]$Ok, [string]$Detail)
    $results.Add([ordered]@{ check = $Name; ok = $Ok; detail = $Detail })
    if ($Ok) {
        Write-YwkLog -Message ('PASS  ' + $Name + '  ' + $Detail)
    } else {
        Write-YwkLog -Level 'FAIL' -Message ('FAIL  ' + $Name + '  ' + $Detail)
        $failures.Add($Name + ': ' + $Detail)
    }
}

function Get-YwkField {
    <#
      .SYNOPSIS
        One property off a ConvertFrom-Json object, or $null when it is absent.
      .DESCRIPTION
        Set-StrictMode -Version Latest turns a missing property into a
        terminating error, and a missing field is exactly what these checks have
        to be able to report as a FAIL rather than die on.
    #>
    param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object.PSObject.Properties.Name -notcontains $Name) { return $null }
    return $Object.$Name
}

try {
    Write-YwkLog -Message ('launching ' + $python + ' -m ywk_server --host 127.0.0.1 --port ' + $Port)
    $proc = Start-Process -FilePath $python `
        -ArgumentList @('-m', 'ywk_server', '--host', '127.0.0.1', '--port', [string]$Port) `
        -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog

    $limit = $StartupTimeoutSeconds
    if ($WithModel) { $limit = $ReadyTimeoutSeconds }
    $deadline = (Get-Date).AddSeconds($limit)
    $health = $null
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) {
            throw ('the server exited early with code ' + $proc.ExitCode + '. See ' + $stderrLog)
        }
        $r = Invoke-YwkProbe -Url ($base + '/health') -TimeoutSeconds 10
        if ($r.Status -eq 200) { $health = $r; break }
        Start-Sleep -Milliseconds 700
    }
    if ($null -eq $health) {
        throw ('GET /health did not answer 200 within ' + $limit + ' s. See ' + $stderrLog)
    }
    Add-YwkResult -Name 'GET /health' -Ok $true -Detail ('200 in ' + $health.ElapsedMs + ' ms')

    if (-not $HealthOnly) {
        $p = Invoke-YwkProbe -Url ($base + '/params') -TimeoutSeconds 30
        Add-YwkResult -Name 'GET /params status' -Ok ($p.Status -eq 200) -Detail ('status=' + $p.Status)
        # measured a second time: the first call pays for the lazily built descriptor table
        $p2 = Invoke-YwkProbe -Url ($base + '/params') -TimeoutSeconds 30
        Add-YwkResult -Name 'GET /params latency' -Ok ($p2.ElapsedMs -le $ParamsBudgetMs) -Detail ([string]$p2.ElapsedMs + ' ms (budget ' + $ParamsBudgetMs + ' ms)')

        $s = Invoke-YwkProbe -Url ($base + '/ywk/status') -TimeoutSeconds 30
        Add-YwkResult -Name 'GET /ywk/status' -Ok ($s.Status -eq 200) -Detail ('status=' + $s.Status)

        # --------------------------------------------------------------- C-3
        #
        # Reading the body back is the whole of C-3. Until this block existed the
        # run reported "200" for /ywk/status and wrote the body to a file, which
        # means a wrapper answering device.actual = null -- or "cpu" on a machine
        # that was supposed to be on the GPU -- still finished with every check
        # PASS and exit code 0. The values a human then read out of
        # ywk-status-<variant>.json were never a judgement the script made.
        # device.actual is null until the model is in, so the five device checks
        # only run under -WithModel; the variant echo is checked either way.
        if ($s.Status -eq 200) {
            $status = $null
            try {
                $status = $s.Body | ConvertFrom-Json
            } catch {
                Add-YwkResult -Name '/ywk/status is JSON' -Ok $false -Detail $_.Exception.Message
            }
            if ($null -ne $status) {
                Add-YwkResult -Name '/ywk/status.variant' `
                    -Ok ((Get-YwkField -Object $status -Name 'variant') -eq $Variant) `
                    -Detail ('variant=' + (Get-YwkField -Object $status -Name 'variant') + ' (want ' + $Variant + ')')
                if ($WithModel) {
                    $dev = Get-YwkField -Object $status -Name 'device'
                    $actual = Get-YwkField -Object $dev -Name 'actual'
                    $devName = Get-YwkField -Object $dev -Name 'name'
                    $prec = Get-YwkField -Object $dev -Name 'precision'
                    $hip = Get-YwkField -Object $dev -Name 'hip'
                    $gcn = Get-YwkField -Object $dev -Name 'gcn_arch'
                    Add-YwkResult -Name '/ywk/status.device.actual' `
                        -Ok ([string]$actual -eq $Device) `
                        -Detail ('actual=' + [string]$actual + ' (want ' + $Device + ')')
                    Add-YwkResult -Name '/ywk/status.device.precision' `
                        -Ok ([string]$prec -eq $Precision) `
                        -Detail ('precision=' + [string]$prec + ' (want ' + $Precision + ')')
                    if ($Variant -like 'rocm*') {
                        # rocm-gfx1151 -> gfx1151. The variant tag is the only
                        # place the expected architecture is written down, so
                        # the answer has to agree with it, not merely be present.
                        $wantGcn = $Variant.Substring($Variant.IndexOf('-') + 1)
                        Add-YwkResult -Name '/ywk/status.device.name' `
                            -Ok (-not [string]::IsNullOrEmpty([string]$devName)) `
                            -Detail ('name=' + [string]$devName)
                        Add-YwkResult -Name '/ywk/status.device.hip' `
                            -Ok (-not [string]::IsNullOrEmpty([string]$hip)) `
                            -Detail ('torch.version.hip=' + [string]$hip)
                        Add-YwkResult -Name '/ywk/status.device.gcn_arch' `
                            -Ok ([string]$gcn -eq $wantGcn) `
                            -Detail ('gcn_arch=' + [string]$gcn + ' (want ' + $wantGcn + ')')
                    } elseif ($Variant -like 'cu*') {
                        Add-YwkResult -Name '/ywk/status.device.name' `
                            -Ok (-not [string]::IsNullOrEmpty([string]$devName)) `
                            -Detail ('name=' + [string]$devName)
                        # A CUDA variant that answers a hip version is a ROCm
                        # torch wearing the wrong label -- the mismatch the
                        # wrapper now warns about on stderr.
                        Add-YwkResult -Name '/ywk/status.device.hip is null' `
                            -Ok ([string]::IsNullOrEmpty([string]$hip)) `
                            -Detail ('torch.version.hip=' + [string]$hip)
                    } else {
                        Add-YwkResult -Name '/ywk/status.device.hip is null' `
                            -Ok ([string]::IsNullOrEmpty([string]$hip)) `
                            -Detail ('torch.version.hip=' + [string]$hip)
                    }
                }
            }
        }

        $v = Invoke-YwkProbe -Url ($base + '/v1/audio/voices') -TimeoutSeconds 30
        Add-YwkResult -Name 'GET /v1/audio/voices' -Ok ($v.Status -eq 200) -Detail ('status=' + $v.Status)

        $null = Write-YwkTextFile -Path (Join-Path $LogDir ('params-' + $Variant + '.json')) -Newline 'LF' -Text $p.Body
        $null = Write-YwkTextFile -Path (Join-Path $LogDir ('ywk-status-' + $Variant + '.json')) -Newline 'LF' -Text $s.Body
    }

    if ($WithModel) {
        $body = '{"model":"irodori-tts","input":"' + [char]0x30C6 + [char]0x30B9 + [char]0x30C8 + '","response_format":"wav","voice":"' + $defaultName + '","irodori":{"num_steps":4}}'
        Write-YwkLog -Message ('POST /v1/audio/speech (num_steps=4, ' + $Device + ', ' + $Precision + ')')
        $sp = Invoke-YwkProbe -Url ($base + '/v1/audio/speech') -Method 'POST' -JsonBody $body -TimeoutSeconds 1200
        if ($sp.Status -ne 200) {
            Add-YwkResult -Name 'POST /v1/audio/speech' -Ok $false -Detail ('status=' + $sp.Status + ' body=' + $sp.Body)
        } else {
            $wavPath = Join-Path $LogDir ('speech-' + $Variant + '.wav')
            [System.IO.File]::WriteAllBytes($wavPath, $sp.Bytes)
            $isRiff = Test-YwkRiffWave -Bytes $sp.Bytes
            Add-YwkResult -Name 'POST /v1/audio/speech' -Ok $true -Detail ('200, ' + $sp.Bytes.Length + ' bytes in ' + $sp.ElapsedMs + ' ms -> ' + $wavPath)
            Add-YwkResult -Name 'response is RIFF/WAVE' -Ok $isRiff -Detail ('first 12 bytes: ' + ([System.BitConverter]::ToString($sp.Bytes[0..11])))

            # Second shot: the SAME body with the "voice" field removed altogether.
            # decisions.md 45 -- /params calls request.voice default (the same name) and
            # required:false, so a body that leaves it out has to come back 200 as well;
            # the wrapper burns IRODORI_DEFAULT_VOICE for exactly this. Upstream on its own
            # answers 400 here, so this shot is the one that proves the wrapper's setdefault
            # is really in the assembled tree. Carried over from convoy A (A-2).
            $bodyNoVoice = '{"model":"irodori-tts","input":"' + [char]0x30C6 + [char]0x30B9 + [char]0x30C8 + '","response_format":"wav","irodori":{"num_steps":4}}'
            Write-YwkLog -Message 'POST /v1/audio/speech with no "voice" field (decisions.md 45)'
            $sp0 = Invoke-YwkProbe -Url ($base + '/v1/audio/speech') -Method 'POST' -JsonBody $bodyNoVoice -TimeoutSeconds 1200
            if ($sp0.Status -ne 200) {
                Add-YwkResult -Name 'POST /v1/audio/speech (voice omitted)' -Ok $false -Detail ('status=' + $sp0.Status + ' body=' + $sp0.Body)
            } else {
                $wav0 = Join-Path $LogDir ('speech-novoice-' + $Variant + '.wav')
                [System.IO.File]::WriteAllBytes($wav0, $sp0.Bytes)
                Add-YwkResult -Name 'POST /v1/audio/speech (voice omitted)' -Ok $true -Detail ('200, ' + $sp0.Bytes.Length + ' bytes in ' + $sp0.ElapsedMs + ' ms -> ' + $wav0)
                Add-YwkResult -Name 'voice-omitted answer is RIFF/WAVE' -Ok (Test-YwkRiffWave -Bytes $sp0.Bytes) -Detail ('first 12 bytes: ' + ([System.BitConverter]::ToString($sp0.Bytes[0..11])))
            }

            # Third shot, this time THROUGH a reference voice. The no-ref shot above never
            # opens an audio file, so it cannot tell whether patches/0001 is really on the
            # copy: torchaudio 2.10 raises ImportError without torchcodec and the upstream
            # fallback only catches RuntimeError. Reading a reference wav is exactly the path
            # that patch fixes, so this is the check that proves both the patch and the
            # pruning of torchcodec/ffmpeg. The reference is the wav we just made (no asset
            # from outside), stored under a Japanese file name so the non-ASCII path is
            # exercised end to end as well.
            if ($isRiff) {
                $refName = [string]([char]0x691C + [char]0x5206)
                $refWav = Join-Path $AppVoices ($refName + '.wav')
                [System.IO.File]::WriteAllBytes($refWav, $sp.Bytes)
                $body2 = '{"model":"irodori-tts","input":"' + [char]0x53C2 + [char]0x7167 + [char]0x30C6 + [char]0x30B9 + [char]0x30C8 + '","response_format":"wav","voice":"' + $refName + '","irodori":{"num_steps":4}}'
                Write-YwkLog -Message 'POST /v1/audio/speech with a reference voice (patches/0001 path)'
                $sp2 = Invoke-YwkProbe -Url ($base + '/v1/audio/speech') -Method 'POST' -JsonBody $body2 -TimeoutSeconds 1200
                if ($sp2.Status -ne 200) {
                    Add-YwkResult -Name 'POST /v1/audio/speech (reference voice)' -Ok $false -Detail ('status=' + $sp2.Status + ' body=' + $sp2.Body)
                } else {
                    $wav2 = Join-Path $LogDir ('speech-ref-' + $Variant + '.wav')
                    [System.IO.File]::WriteAllBytes($wav2, $sp2.Bytes)
                    Add-YwkResult -Name 'POST /v1/audio/speech (reference voice)' -Ok $true -Detail ('200, ' + $sp2.Bytes.Length + ' bytes in ' + $sp2.ElapsedMs + ' ms -> ' + $wav2)
                    Add-YwkResult -Name 'reference answer is RIFF/WAVE' -Ok (Test-YwkRiffWave -Bytes $sp2.Bytes) -Detail ('first 12 bytes: ' + ([System.BitConverter]::ToString($sp2.Bytes[0..11])))
                }
                Remove-Item -LiteralPath $refWav -Force -ErrorAction SilentlyContinue
            }
        }
    }
} finally {
    if ($null -ne $proc) {
        if (-not $proc.HasExited) {
            Write-YwkLog -Message ('killing pid ' + $proc.Id)
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            } catch {
                Write-YwkLog -Level 'WARN' -Message ('Stop-Process failed: ' + $_.Exception.Message)
            }
            $null = $proc.WaitForExit(15000)
        }
        if (-not $proc.HasExited) {
            Write-YwkLog -Level 'WARN' -Message 'the server process is still alive after Stop-Process'
        }
    }
    foreach ($k in $childEnv.Keys) {
        [System.Environment]::SetEnvironmentVariable($k, $saved[$k])
    }
}

$report = [ordered]@{
    generated  = Get-YwkTimestamp
    variant    = $Variant
    port       = $Port
    with_model = [bool]$WithModel
    health_only = [bool]$HealthOnly
    device     = $Device
    precision  = $Precision
    hf_home    = $HfHome
    checks     = $results.ToArray()
    failures   = $failures.ToArray()
    stdout_log = $stdoutLog
    stderr_log = $stderrLog
}
$null = Write-YwkJsonFile -Path (Join-Path $LogDir ('verify-runtime-' + $Variant + '.json')) -Value $report -Depth 10

if ($failures.Count -gt 0) {
    Write-YwkLog -Level 'FAIL' -Message ([string]$failures.Count + ' check(s) failed')
    exit 1
}
Write-YwkLog -Level 'STEP' -Message ('all ' + $results.Count + ' checks passed')
exit 0
