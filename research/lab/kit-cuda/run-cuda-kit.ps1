<#
  run-cuda-kit.ps1 -- self-contained CUDA verification kit for Irodori-TTS.

  Runs on Windows PowerShell 5.1 and PowerShell 7+.
  This file is deliberately ASCII-only: PowerShell 5.1 reads BOM-less files as ANSI,
  so all Japanese text lives in cases.json / summary-header.md / README-kit.md instead.

  Nothing is written outside KitRoot (venvs, uv cache, managed python, HF cache, results).
  No administrator rights required.

  Usage (see README-kit.md, Japanese):
    powershell -ExecutionPolicy Bypass -File .\run-cuda-kit.ps1 -Variant cu130
    powershell -ExecutionPolicy Bypass -File .\run-cuda-kit.ps1 -Variant cu126
#>
[CmdletBinding()]
param(
    [ValidateSet('cu126', 'cu130', 'cpu')]
    [string]$Variant = 'cu130',
    [string]$KitRoot = '',
    [switch]$Offline,
    [string]$HfHome = '',
    [string]$Checkpoint = 'Aratako/Irodori-TTS-v4.1-Small',
    [string]$Device = '',
    [int]$Repeats = 3,
    [int]$Threads = 8,
    [switch]$Quick,
    [switch]$SkipBench,
    [switch]$SkipOnnx
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$script:StartedAt = Get-Date

# ---------------------------------------------------------------- paths

if ([string]::IsNullOrWhiteSpace($KitRoot)) {
    if ($PSScriptRoot) { $KitRoot = $PSScriptRoot } else { $KitRoot = (Get-Location).Path }
}
$KitRoot = [System.IO.Path]::GetFullPath($KitRoot)

$ResultsDir = Join-Path $KitRoot 'results'
$LogsDir = Join-Path $ResultsDir 'logs'
$WavDir = Join-Path $ResultsDir 'wav'
$SrcDir = Join-Path $KitRoot 'src'
$WheelDir = Join-Path $KitRoot 'wheels'
$OnnxDir = Join-Path $KitRoot 'onnx'
$VenvDir = Join-Path $KitRoot (".venv-" + $Variant)
$UvCache = Join-Path $KitRoot 'uv-cache'
$UvPython = Join-Path $KitRoot 'python'
if ([string]::IsNullOrWhiteSpace($HfHome)) { $HfHome = Join-Path $KitRoot 'hf' }

foreach ($d in @($ResultsDir, $LogsDir, $WavDir, $SrcDir, $UvCache, $UvPython, $HfHome, $OnnxDir)) {
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Write-TextFile([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, $Utf8NoBom)
}
function Write-JsonFile([string]$Path, $Obj) {
    Write-TextFile $Path ($Obj | ConvertTo-Json -Depth 12)
}

# ---------------------------------------------------------------- helpers

$script:Steps = New-Object System.Collections.ArrayList

function Add-Step($Name, $Status, $Seconds, $Detail) {
    $null = $script:Steps.Add([pscustomobject]@{
            name    = $Name
            status  = $Status
            seconds = [math]::Round($Seconds, 2)
            detail  = $Detail
        })
    Write-Host ("[kit] {0,-28} {1,-8} {2,7:N1}s  {3}" -f $Name, $Status, $Seconds, $Detail)
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Body)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host ("[kit] >>> " + $Name)
    try {
        $detail = & $Body
        $sw.Stop()
        $st = 'ok'
        if (([string]$detail).StartsWith('RESULT-FAIL')) { $st = 'result-FAIL' }
        Add-Step $Name $st $sw.Elapsed.TotalSeconds ([string]$detail)
        return $true
    }
    catch {
        $sw.Stop()
        $msg = $_.Exception.Message
        $trace = ($_ | Out-String)
        Write-TextFile (Join-Path $LogsDir ("FAIL_" + ($Name -replace '[^A-Za-z0-9_.-]', '_') + ".txt")) $trace
        Add-Step $Name 'FAIL' $sw.Elapsed.TotalSeconds $msg
        return $false
    }
}

function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments, [string]$LogName, [switch]$Quiet)
    $lines = New-Object System.Collections.ArrayList
    & $Exe @Arguments 2>&1 | ForEach-Object {
        $s = if ($null -eq $_) { '' } else { $_.ToString() }
        if (-not $Quiet) { Write-Host ("    " + $s) }
        $null = $lines.Add($s)
    }
    $code = $LASTEXITCODE
    $text = ($lines -join [Environment]::NewLine)
    if ($LogName) { Write-TextFile (Join-Path $LogsDir $LogName) ("> $Exe $($Arguments -join ' ')`r`n`r`n" + $text) }
    return [pscustomobject]@{ ExitCode = $code; Output = $text }
}

function Get-DirMB([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    $s = (Get-ChildItem -LiteralPath $p -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if ($null -eq $s) { return 0.0 }
    return [math]::Round($s / 1MB, 1)
}

function Get-FreeGB([string]$path) {
    try {
        $qual = [System.IO.Path]::GetPathRoot($path).TrimEnd('\').TrimEnd(':')
        $d = Get-PSDrive -Name $qual -ErrorAction Stop
        return [math]::Round($d.Free / 1GB, 1)
    }
    catch { return $null }
}

function Find-Uv {
    $local = Join-Path $KitRoot 'uv\uv.exe'
    if (Test-Path -LiteralPath $local) { return $local }
    $c = Get-Command uv -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return $null
}

function Find-NvidiaSmi {
    $c = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $p = Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'
    if (Test-Path -LiteralPath $p) { return $p }
    return $null
}

# ---------------------------------------------------------------- environment

$env:UV_CACHE_DIR = $UvCache
$env:UV_PYTHON_INSTALL_DIR = $UvPython
$env:UV_NO_CONFIG = '1'
$env:HF_HOME = $HfHome
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONWARNINGS = 'ignore'
$env:OMP_NUM_THREADS = "$Threads"
$env:MKL_NUM_THREADS = "$Threads"
if ($Offline) { $env:HF_HUB_OFFLINE = '1' } else { Remove-Item Env:\HF_HUB_OFFLINE -ErrorAction SilentlyContinue }

if ([string]::IsNullOrWhiteSpace($Device)) {
    if ($Variant -eq 'cpu') { $Device = 'cpu' } else { $Device = 'cuda' }
}

Write-Host ""
Write-Host "=============================================================="
Write-Host ("  Irodori-TTS CUDA kit   variant={0}  device={1}" -f $Variant, $Device)
Write-Host ("  KitRoot : {0}" -f $KitRoot)
Write-Host ("  HF_HOME : {0}   offline={1}" -f $HfHome, [bool]$Offline)
Write-Host "=============================================================="
Write-Host ""

# ---------------------------------------------------------------- (a) env.json

$envInfo = [ordered]@{
    started_at        = $script:StartedAt.ToString('s')
    variant           = $Variant
    device            = $Device
    kit_root          = $KitRoot
    checkpoint        = $Checkpoint
    offline           = [bool]$Offline
    repeats           = $Repeats
    threads           = $Threads
    quick             = [bool]$Quick
    powershell        = ($PSVersionTable.PSVersion.ToString())
    powershell_edition = ($PSVersionTable.PSEdition)
    os_version        = [System.Environment]::OSVersion.VersionString
    os_caption        = $null
    os_build          = $null
    machine           = [System.Environment]::MachineName
    processor_count   = [System.Environment]::ProcessorCount
    is_64bit_os       = [System.Environment]::Is64BitOperatingSystem
    free_gb_kitroot   = (Get-FreeGB $KitRoot)
    uv_path           = $null
    uv_version        = $null
    nvidia_smi_path   = $null
    nvidia_smi        = $null
    nvidia_smi_query  = $null
    ram_total_gb      = $null
}

try {
    $rp = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $envInfo.os_caption = (Get-ItemProperty -Path $rp -Name ProductName -ErrorAction Stop).ProductName
    $cb = (Get-ItemProperty -Path $rp -ErrorAction Stop)
    $envInfo.os_build = ("{0}.{1}" -f $cb.CurrentBuild, $cb.UBR)
    $envInfo.os_display = $cb.DisplayVersion
}
catch { $envInfo.os_caption = "registry read failed: $($_.Exception.Message)" }

try {
    $cs = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop
    $envInfo.ram_total_gb = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    $envInfo.cpu_name = (Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop | Select-Object -First 1).Name
}
catch { $envInfo.ram_total_gb = "cim failed: $($_.Exception.Message)" }

$smi = Find-NvidiaSmi
$envInfo.nvidia_smi_path = $smi
if ($smi) {
    $r = Invoke-Native $smi @() 'nvidia-smi.log' -Quiet
    $envInfo.nvidia_smi = $r.Output
    $r2 = Invoke-Native $smi @('--query-gpu=index,name,driver_version,memory.total,compute_cap,pcie.link.gen.max', '--format=csv') 'nvidia-smi-query.log' -Quiet
    $envInfo.nvidia_smi_query = $r2.Output
    Write-Host $r2.Output
}
else {
    $envInfo.nvidia_smi = 'nvidia-smi not found (no NVIDIA driver on this machine?)'
    Write-Host "[kit] WARNING: nvidia-smi not found."
}

$uv = Find-Uv
$envInfo.uv_path = $uv
if ($uv) {
    $r = Invoke-Native $uv @('--version') 'uv-version.log' -Quiet
    $envInfo.uv_version = $r.Output.Trim()
}
Write-JsonFile (Join-Path $ResultsDir 'env.json') $envInfo
Add-Step 'a_env' 'ok' 0 ("free=$($envInfo.free_gb_kitroot)GB uv=$($envInfo.uv_version)")

if (-not $uv) {
    Write-Host ""
    Write-Host "[kit] FATAL: uv was not found."
    Write-Host "[kit]   Install it once with:   winget install --id astral-sh.uv -e"
    Write-Host "[kit]   Then CLOSE and REOPEN the terminal and run this script again."
    Write-Host "[kit]   (Or drop uv.exe into  $KitRoot\uv\uv.exe  and re-run.)"
    Add-Step 'uv_missing' 'FAIL' 0 'uv not installed'
    Write-JsonFile (Join-Path $ResultsDir 'steps.json') $script:Steps
    exit 2
}

if ($envInfo.free_gb_kitroot -ne $null -and $envInfo.free_gb_kitroot -lt 25) {
    Write-Host ("[kit] WARNING: only {0} GB free on the kit drive. 25 GB or more is recommended." -f $envInfo.free_gb_kitroot)
}

# ---------------------------------------------------------------- unpack upstream source

$IrodoriSrc = Join-Path $SrcDir 'Irodori-TTS'
$null = Invoke-Step 'unpack_source' {
    $zip = Get-ChildItem -LiteralPath $KitRoot -Filter 'Irodori-TTS-*.zip' -File | Select-Object -First 1
    if (-not $zip) { throw "Irodori-TTS-*.zip not found in $KitRoot" }
    if (-not (Test-Path -LiteralPath (Join-Path $IrodoriSrc 'infer.py'))) {
        Expand-Archive -LiteralPath $zip.FullName -DestinationPath $SrcDir -Force
    }
    if (-not (Test-Path -LiteralPath (Join-Path $IrodoriSrc 'infer.py'))) {
        throw "infer.py missing after extracting $($zip.Name)"
    }
    return $zip.Name
}
$env:IRODORI_SRC = $IrodoriSrc
$env:PYTHONPATH = $IrodoriSrc

# ---------------------------------------------------------------- (b) venv

$Py = Join-Path $VenvDir 'Scripts\python.exe'
$null = Invoke-Step 'b_venv' {
    if (-not (Test-Path -LiteralPath $Py)) {
        $r = Invoke-Native $uv @('venv', '--python', '3.12', $VenvDir) 'uv-venv.log'
        if ($r.ExitCode -ne 0) { throw "uv venv failed (exit $($r.ExitCode)). See logs/uv-venv.log" }
    }
    if (-not (Test-Path -LiteralPath $Py)) { throw "python.exe missing in $VenvDir" }
    return $VenvDir
}
if (-not (Test-Path -LiteralPath $Py)) {
    Write-JsonFile (Join-Path $ResultsDir 'steps.json') $script:Steps
    exit 3
}

# ---------------------------------------------------------------- (c) torch

$TorchIndex = "https://download.pytorch.org/whl/$Variant"
$installInfo = [ordered]@{
    torch_index      = $TorchIndex
    torch_spec_tried = $null
    torch_spec_used  = $null
    fallback_used    = $false
    packages         = $null
    sizes_mb         = [ordered]@{}
    uv_cache_mb_after_torch = $null
    uv_cache_mb_final = $null
}

$null = Invoke-Step 'c_torch' {
    $pinned = @('uv_pip_placeholder')
    $args1 = @('pip', 'install', '--python', $Py, '--index-url', $TorchIndex, 'torch>=2.10,<2.11', 'torchaudio>=2.10,<2.11')
    $installInfo.torch_spec_tried = 'torch>=2.10,<2.11 / torchaudio>=2.10,<2.11'
    $r = Invoke-Native $uv $args1 'uv-torch-pinned.log'
    if ($r.ExitCode -ne 0) {
        Write-Host "[kit] pinned torch 2.10 not available on this index -- falling back to latest."
        $installInfo.fallback_used = $true
        $args2 = @('pip', 'install', '--python', $Py, '--index-url', $TorchIndex, 'torch', 'torchaudio')
        $r = Invoke-Native $uv $args2 'uv-torch-latest.log'
        if ($r.ExitCode -ne 0) { throw "torch install failed on $TorchIndex (exit $($r.ExitCode)). See logs/uv-torch-latest.log" }
        $installInfo.torch_spec_used = 'torch (latest on index) / torchaudio (latest on index)'
    }
    else {
        $installInfo.torch_spec_used = 'torch>=2.10,<2.11 / torchaudio>=2.10,<2.11'
    }
    $installInfo.uv_cache_mb_after_torch = (Get-DirMB $UvCache)
    return $installInfo.torch_spec_used
}

# ---------------------------------------------------------------- (d) remaining deps

$null = Invoke-Step 'd_deps' {
    # Pin torch/torchaudio to the exact build that step (c) installed, and keep the torch index
    # ahead of PyPI, so the dependency resolve cannot swap in the PyPI (CPU) torch wheel.
    # (Without this the guard below had to reinstall torch = ~9 min wasted per variant.)
    $tj0 = Join-Path $ResultsDir 'torch-before-deps.json'
    $null = Invoke-Native $Py @((Join-Path $KitRoot 'torch_check.py'), $tj0) 'torch-check-before-deps.log' -Quiet
    $cfile = Join-Path $ResultsDir 'torch-constraints.txt'
    $extra = @()
    if (Test-Path -LiteralPath $tj0) {
        $i0 = Get-Content -LiteralPath $tj0 -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($i0.ok -and $i0.v) {
            $tv = [string]$i0.v
            $lines = @("torch==$tv")
            try {
                $ta = & $Py -c "import torchaudio,sys;sys.stdout.write(torchaudio.__version__)" 2>$null
                if ($ta) { $lines += "torchaudio==$ta" }
            } catch { }
            [System.IO.File]::WriteAllLines($cfile, $lines)
            $extra = @('--constraint', $cfile, '--extra-index-url', $TorchIndex, '--index-strategy', 'unsafe-best-match')
            Write-Host "[kit] deps: constraining $($lines -join ' / ') with $TorchIndex ahead of PyPI"
        }
    }
    $depArgs = @(
        'pip', 'install', '--python', $Py,
        '--find-links', $WheelDir) + $extra + @(
        'dacvae', 'silentcipher',
        'transformers>=5.12.1,<6', 'sentencepiece==0.2.1',
        'safetensors', 'huggingface-hub', 'soundfile', 'numba', 'llvmlite',
        'pyyaml', 'tqdm', 'peft', 'psutil'
    )
    $r = Invoke-Native $uv $depArgs 'uv-deps.log'
    if ($r.ExitCode -ne 0) { throw "dependency install failed (exit $($r.ExitCode)). See logs/uv-deps.log" }
    return 'ok'
}

# guard: make sure the dependency resolve did not replace the CUDA torch with the PyPI CPU wheel
$null = Invoke-Step 'd_torch_guard' {
    $tj = Join-Path $ResultsDir 'torch.json'
    $r = Invoke-Native $Py @((Join-Path $KitRoot 'torch_check.py'), $tj) 'torch-check.log' -Quiet
    if (-not (Test-Path -LiteralPath $tj)) { throw "torch import failed (exit $($r.ExitCode)). See logs/torch-check.log" }
    $info = Get-Content -LiteralPath $tj -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $info.ok) { throw "torch import failed: $($info.error)" }
    $installInfo.torch_version = $info.v
    $installInfo.torch_version_cuda = $info.cuda
    $installInfo.torch_cuda_available = $info.avail
    $installInfo.torch_cudnn = $info.cudnn
    $installInfo.torch_device_names = $info.device_names
    if ($Variant -ne 'cpu' -and ($info.v -notmatch [regex]::Escape($Variant))) {
        Write-Host "[kit] torch is $($info.v) -- reinstalling from $TorchIndex to restore the $Variant build."
        $r2 = Invoke-Native $uv @('pip', 'install', '--python', $Py, '--index-url', $TorchIndex, '--reinstall-package', 'torch', '--reinstall-package', 'torchaudio', 'torch', 'torchaudio') 'uv-torch-repair.log'
        if ($r2.ExitCode -ne 0) { throw "torch repair install failed. See logs/uv-torch-repair.log" }
        $installInfo.torch_repaired = $true
        $r3 = Invoke-Native $Py @((Join-Path $KitRoot 'torch_check.py'), $tj) 'torch-check-after-repair.log' -Quiet
        $info = Get-Content -LiteralPath $tj -Raw -Encoding UTF8 | ConvertFrom-Json
        $installInfo.torch_version = $info.v
        $installInfo.torch_version_cuda = $info.cuda
        $installInfo.torch_cuda_available = $info.avail
    }
    if ($Variant -ne 'cpu' -and -not $info.avail) {
        Write-Host "[kit] WARNING: torch.cuda.is_available() is False. Update the NVIDIA driver (cu130 needs 580+, cu126 needs 560.76+) and re-run."
    }
    return "$($info.v) cuda=$($info.cuda) available=$($info.avail)"
}

# ---------------------------------------------------------------- (e) sizes

$null = Invoke-Step 'e_sizes' {
    $sp = Join-Path $VenvDir 'Lib\site-packages'
    $installInfo.sizes_mb.venv_total = (Get-DirMB $VenvDir)
    $installInfo.sizes_mb.site_packages = (Get-DirMB $sp)
    $installInfo.sizes_mb.torch = (Get-DirMB (Join-Path $sp 'torch'))
    $installInfo.sizes_mb.torch_lib = (Get-DirMB (Join-Path $sp 'torch\lib'))
    $installInfo.sizes_mb.torchaudio = (Get-DirMB (Join-Path $sp 'torchaudio'))
    $installInfo.sizes_mb.transformers = (Get-DirMB (Join-Path $sp 'transformers'))
    $installInfo.uv_cache_mb_final = (Get-DirMB $UvCache)
    $top = Get-ChildItem -LiteralPath $sp -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        [pscustomobject]@{ name = $_.Name; mb = (Get-DirMB $_.FullName) }
    } | Sort-Object -Property mb -Descending | Select-Object -First 15
    $installInfo.site_packages_top15 = $top
    $r = Invoke-Native $uv @('pip', 'list', '--python', $Py, '--format', 'json') 'uv-pip-list.log' -Quiet
    try { $installInfo.packages = ($r.Output -split "`n" | Where-Object { $_ -match '^\s*\[' } | Select-Object -Last 1 | ConvertFrom-Json) } catch {}
    Write-JsonFile (Join-Path $ResultsDir 'install.json') $installInfo
    return ("venv=$($installInfo.sizes_mb.venv_total)MB torch=$($installInfo.sizes_mb.torch)MB dl_cache=$($installInfo.uv_cache_mb_final)MB")
}
Write-JsonFile (Join-Path $ResultsDir 'install.json') $installInfo

# ---------------------------------------------------------------- (f) synthesis bench

$CasesJson = Join-Path $KitRoot 'cases.json'
$BenchPy = Join-Path $KitRoot 'bench_infer.py'

function Invoke-Bench {
    param([string]$Label, [string]$Case, [string]$Precision, [int]$Steps, [int]$Reps, [string[]]$Extra)
    $json = Join-Path $ResultsDir ("bench_{0}_{1}.json" -f $Variant, $Label)
    $wav = Join-Path $WavDir ("{0}_{1}.wav" -f $Variant, $Label)
    $a = @(
        $BenchPy,
        '--json-out', $json,
        '--label', $Label,
        '--repeats', "$Reps",
        '--src', $IrodoriSrc,
        '--cases-json', $CasesJson,
        '--case', $Case,
        '--'
    )
    $a += @(
        '--hf-checkpoint', $Checkpoint,
        '--no-ref', '--seed', '1234',
        '--model-device', $Device, '--codec-device', $Device,
        '--model-precision', $Precision, '--codec-precision', $Precision,
        '--num-steps', "$Steps",
        '--output-wav', $wav
    )
    if ($Extra) { $a += $Extra }
    $r = Invoke-Native $Py $a ("bench_" + $Label + ".log")
    return [pscustomobject]@{ ExitCode = $r.ExitCode; Json = $json }
}

if (-not $SkipBench) {
    $matrix = @()
    if ($Quick) {
        $matrix += , @('fp32', 10, 'short')
        $matrix += , @('bf16', 10, 'short')
        $reps = 1
    }
    else {
        foreach ($prec in @('fp32', 'bf16')) {
            foreach ($st in @(40, 10)) {
                foreach ($tx in @('short', 'long')) {
                    $matrix += , @($prec, $st, $tx)
                }
            }
        }
        $reps = $Repeats
    }
    foreach ($m in $matrix) {
        $prec = $m[0]; $st = $m[1]; $tx = $m[2]
        $label = "{0}_steps{1}_{2}" -f $prec, $st, $tx
        $null = Invoke-Step ("f_bench_" + $label) {
            $res = Invoke-Bench -Label $label -Case $tx -Precision $prec -Steps $st -Reps $reps
            if (-not (Test-Path -LiteralPath $res.Json)) { throw "bench $label produced no json (exit=$($res.ExitCode)); see logs/bench_$label.log" }
            $j = (Get-Content -LiteralPath $res.Json -Raw -Encoding UTF8 | ConvertFrom-Json)
            if ($res.ExitCode -ne 0) {
                # a synthesis that refuses to run is a RESULT (e.g. bf16 on cpu), not a broken script
                $e = ''
                $r0 = @($j.runs)[0]
                if ($r0) { $e = [string]$r0.error }
                return ("RESULT-FAIL: " + $e)
            }
            return ("cold={0}s warm_med={1}s rtf={2}" -f $j.cold_total_to_decode_s, $j.warm_median_total_to_decode_s, $j.warm_median_rtf_synth)
        }
    }
}
else {
    Add-Step 'f_bench' 'skipped' 0 '-SkipBench'
}

# ---------------------------------------------------------------- (g) torch.compile

$null = Invoke-Step 'g_compile' {
    $json = Join-Path $ResultsDir 'compile.json'
    $wav = Join-Path $WavDir ("{0}_compile.wav" -f $Variant)
    $a = @(
        $BenchPy, '--json-out', $json, '--label', 'compile_fp32_steps10_short',
        '--repeats', '1', '--src', $IrodoriSrc, '--cases-json', $CasesJson, '--case', 'short', '--',
        '--hf-checkpoint', $Checkpoint, '--no-ref', '--seed', '1234',
        '--model-device', $Device, '--codec-device', $Device,
        '--model-precision', 'fp32', '--codec-precision', 'fp32',
        '--num-steps', '10', '--compile-model', '--output-wav', $wav
    )
    $r = Invoke-Native $Py $a 'compile.log'
    if ($r.ExitCode -ne 0) {
        # a failure here is a RESULT, not a script error: record it verbatim and carry on
        $obj = [ordered]@{
            ok = $false
            exit_code = $r.ExitCode
            note = 'torch.compile run failed. Verbatim output kept in results/logs/compile.log and below.'
            output_tail = ($r.Output.Substring([math]::Max(0, $r.Output.Length - 8000)))
        }
        if (-not (Test-Path -LiteralPath $json)) { Write-JsonFile $json $obj }
        else {
            $existing = Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json
            $existing | Add-Member -NotePropertyName 'kit_compile_note' -NotePropertyValue $obj -Force
            Write-JsonFile $json $existing
        }
        return "compile FAILED (recorded, exit=$($r.ExitCode))"
    }
    return 'compile ok'
}

# ---------------------------------------------------------------- (h) optional ONNX benches

$onnxFiles = @(Get-ChildItem -LiteralPath $OnnxDir -Filter '*.onnx' -File -ErrorAction SilentlyContinue)
if ($SkipOnnx -or $onnxFiles.Count -eq 0) {
    Add-Step 'h_onnx' 'skipped' 0 ("models=" + $onnxFiles.Count + " skip=" + [bool]$SkipOnnx)
}
else {
    $ortPy = Join-Path $KitRoot '.venv-ort-cuda\Scripts\python.exe'
    $null = Invoke-Step 'h_onnx_cuda' {
        if (-not (Test-Path -LiteralPath $ortPy)) {
            $r = Invoke-Native $uv @('venv', '--python', '3.12', (Join-Path $KitRoot '.venv-ort-cuda')) 'uv-venv-ort-cuda.log'
            if ($r.ExitCode -ne 0) { throw "uv venv (ort-cuda) failed" }
        }
        $r = Invoke-Native $uv @('pip', 'install', '--python', $ortPy, 'onnxruntime-gpu[cuda,cudnn]', 'numpy') 'uv-ort-cuda.log'
        if ($r.ExitCode -ne 0) {
            Write-Host '[kit] extras [cuda,cudnn] not accepted -- installing the nvidia wheels explicitly.'
            $r = Invoke-Native $uv @('pip', 'install', '--python', $ortPy, 'onnxruntime-gpu', 'numpy',
                'nvidia-cuda-runtime-cu13', 'nvidia-cuda-nvrtc-cu13', 'nvidia-cudnn-cu13',
                'nvidia-cublas-cu13', 'nvidia-cufft-cu13', 'nvidia-curand-cu13') 'uv-ort-cuda-explicit.log'
            if ($r.ExitCode -ne 0) {
                $r = Invoke-Native $uv @('pip', 'install', '--python', $ortPy, 'onnxruntime-gpu', 'numpy') 'uv-ort-cuda-bare.log'
                if ($r.ExitCode -ne 0) { throw 'onnxruntime-gpu install failed (all three attempts)' }
            }
        }
        $r = Invoke-Native $ortPy @((Join-Path $KitRoot 'bench_onnx.py'), '--onnx-dir', $OnnxDir, '--ep', 'cuda', '--json-out', (Join-Path $ResultsDir 'onnx_cuda.json')) 'onnx-cuda.log'
        if ($r.ExitCode -ne 0) { throw "bench_onnx cuda exit=$($r.ExitCode)" }
        return ("ort-cuda venv=" + (Get-DirMB (Join-Path $KitRoot '.venv-ort-cuda')) + 'MB')
    }
    $dmlPy = Join-Path $KitRoot '.venv-ort-dml\Scripts\python.exe'
    $null = Invoke-Step 'h_onnx_dml' {
        if (-not (Test-Path -LiteralPath $dmlPy)) {
            $r = Invoke-Native $uv @('venv', '--python', '3.12', (Join-Path $KitRoot '.venv-ort-dml')) 'uv-venv-ort-dml.log'
            if ($r.ExitCode -ne 0) { throw "uv venv (ort-dml) failed" }
        }
        $r = Invoke-Native $uv @('pip', 'install', '--python', $dmlPy, 'onnxruntime-directml', 'numpy') 'uv-ort-dml.log'
        if ($r.ExitCode -ne 0) { throw 'onnxruntime-directml install failed' }
        $r = Invoke-Native $dmlPy @((Join-Path $KitRoot 'bench_onnx.py'), '--onnx-dir', $OnnxDir, '--ep', 'dml', '--json-out', (Join-Path $ResultsDir 'onnx_dml.json')) 'onnx-dml.log'
        if ($r.ExitCode -ne 0) { throw "bench_onnx dml exit=$($r.ExitCode)" }
        return ("ort-dml venv=" + (Get-DirMB (Join-Path $KitRoot '.venv-ort-dml')) + 'MB')
    }
}

# ---------------------------------------------------------------- (i) summary.md

Write-JsonFile (Join-Path $ResultsDir 'steps.json') $script:Steps

$null = Invoke-Step 'i_summary' {
    $sb = New-Object System.Text.StringBuilder
    $hdr = Join-Path $KitRoot 'summary-header.md'
    if (Test-Path -LiteralPath $hdr) {
        $null = $sb.AppendLine((Get-Content -LiteralPath $hdr -Raw -Encoding UTF8))
    }
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("# Irodori-TTS CUDA kit -- results (variant: $Variant)")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("generated: " + (Get-Date).ToString('s') + "   elapsed: " + ("{0:N1}" -f ((Get-Date) - $script:StartedAt).TotalMinutes) + " min")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("Legend: step status ``ok`` = ran; ``result-FAIL`` = the run itself refused (a measurement, e.g. bf16 on CPU); ``FAIL`` = the script hit a problem.")
    $null = $sb.AppendLine("")

    # --- machine
    $null = $sb.AppendLine("## 1. Machine")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("| key | value |")
    $null = $sb.AppendLine("|---|---|")
    $null = $sb.AppendLine("| OS | $($envInfo.os_caption) $($envInfo.os_display) build $($envInfo.os_build) |")
    $null = $sb.AppendLine("| PowerShell | $($envInfo.powershell) ($($envInfo.powershell_edition)) |")
    $null = $sb.AppendLine("| CPU | $($envInfo.cpu_name) ($($envInfo.processor_count) logical) |")
    $null = $sb.AppendLine("| RAM | $($envInfo.ram_total_gb) GB |")
    $null = $sb.AppendLine("| free disk (kit drive) | $($envInfo.free_gb_kitroot) GB |")
    $null = $sb.AppendLine("| uv | $($envInfo.uv_version) |")
    $null = $sb.AppendLine("| OMP_NUM_THREADS | $Threads |")
    $null = $sb.AppendLine("| checkpoint | $Checkpoint |")
    $null = $sb.AppendLine("")
    if ($envInfo.nvidia_smi_query) {
        $null = $sb.AppendLine('```')
        $null = $sb.AppendLine($envInfo.nvidia_smi_query.Trim())
        $null = $sb.AppendLine('```')
        $null = $sb.AppendLine("")
    }

    # --- install
    $null = $sb.AppendLine("## 2. Install (torch index: $TorchIndex)")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("| key | value |")
    $null = $sb.AppendLine("|---|---|")
    $null = $sb.AppendLine("| torch spec used | $($installInfo.torch_spec_used) |")
    $null = $sb.AppendLine("| fallback to latest | $($installInfo.fallback_used) |")
    $null = $sb.AppendLine("| torch version | $($installInfo.torch_version) |")
    $null = $sb.AppendLine("| torch.version.cuda | $($installInfo.torch_version_cuda) |")
    $null = $sb.AppendLine("| torch.cuda.is_available() | $($installInfo.torch_cuda_available) |")
    $null = $sb.AppendLine("| venv total MB | $($installInfo.sizes_mb.venv_total) |")
    $null = $sb.AppendLine("| site-packages MB | $($installInfo.sizes_mb.site_packages) |")
    $null = $sb.AppendLine("| torch/ MB | $($installInfo.sizes_mb.torch) |")
    $null = $sb.AppendLine("| torch/lib MB | $($installInfo.sizes_mb.torch_lib) |")
    $null = $sb.AppendLine("| torch cuDNN version | $($installInfo.torch_cudnn) |")
    $null = $sb.AppendLine("| uv cache MB (UNPACKED wheels, not the download size) | $($installInfo.uv_cache_mb_final) |")
    $null = $sb.AppendLine("| uv cache MB right after torch | $($installInfo.uv_cache_mb_after_torch) |")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("Download size is the wheel size, not the cache size: torch 2.10.0 win_amd64 cp312 is 1,867,405,006 B (1781 MiB) for +cu130 and 2,589,881,452 B (2470 MiB) for +cu126 (HEAD on download.pytorch.org, 2026-09-04).")
    $null = $sb.AppendLine("")
    if ($installInfo.site_packages_top15) {
        $null = $sb.AppendLine("Top site-packages directories (MB): " +
            (($installInfo.site_packages_top15 | ForEach-Object { "$($_.name) $($_.mb)" }) -join ' / '))
        $null = $sb.AppendLine("")
    }

    # --- bench
    $null = $sb.AppendLine("## 3. Synthesis bench")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("RTF = total_to_decode / audio_seconds (model load excluded; this is the resident-server figure).")
    $null = $sb.AppendLine("cold = first synthesis in the process (includes CUDA shape tuning); warm = median of the rest.")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("| precision | steps | text | audio s | cold total_to_decode s | warm median s | warm RTF | cold RTF | VRAM peak MiB | wav sha256 (warm) | status |")
    $null = $sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|")
    $benchFiles = @(Get-ChildItem -LiteralPath $ResultsDir -Filter ("bench_" + $Variant + "_*.json") -File -ErrorAction SilentlyContinue | Sort-Object Name)
    foreach ($bf in $benchFiles) {
        try {
            $j = Get-Content -LiteralPath $bf.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $lbl = $j.label
            $parts = $lbl -split '_'
            $prec = $parts[0]
            $steps = ($parts[1] -replace 'steps', '')
            $txt = $parts[2]
            $okRuns = @($j.runs | Where-Object { $_.ok })
            $warmRuns = @($okRuns | Where-Object { $_.kind -eq 'warm' })
            $audio = if ($okRuns.Count -gt 0) { $okRuns[0].audio_seconds } else { '' }
            $vram = ($okRuns | ForEach-Object { $_.cuda_max_memory_allocated_mib } | Measure-Object -Maximum).Maximum
            $sha = if ($warmRuns.Count -gt 0) { "$($warmRuns[-1].wav_sha256)".Substring(0, [math]::Min(16, "$($warmRuns[-1].wav_sha256)".Length)) } else { '' }
            $status = if ($j.ok) { 'ok' } else { 'FAIL' }
            $err = ''
            if (-not $j.ok) {
                $first = @($j.runs)[0]
                if ($first) { $err = ($first.error -replace '\|', '/') }
                if ($err.Length -gt 90) { $err = $err.Substring(0, 90) + '...' }
                $status = "measured-FAIL: $err"
            }
            $null = $sb.AppendLine("| $prec | $steps | $txt | $audio | $($j.cold_total_to_decode_s) | $($j.warm_median_total_to_decode_s) | $($j.warm_median_rtf_synth) | $($j.cold_rtf_synth) | $vram | $sha | $status |")
        }
        catch {
            $null = $sb.AppendLine("| ? | ? | ? | | | | | | | | unreadable: $($bf.Name) |")
        }
    }
    $null = $sb.AppendLine("")

    # --- stage timings of the warm runs
    $null = $sb.AppendLine("### 3-1. Stage timings (ms, warm run 2 of each case)")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("| case | predict_duration | sample_rf | decode_latent | watermark |")
    $null = $sb.AppendLine("|---|---|---|---|---|")
    foreach ($bf in $benchFiles) {
        try {
            $j = Get-Content -LiteralPath $bf.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $w = @($j.runs | Where-Object { $_.ok -and $_.kind -eq 'warm' }) | Select-Object -First 1
            if ($null -eq $w) { $w = @($j.runs | Where-Object { $_.ok }) | Select-Object -First 1 }
            if ($null -ne $w -and $null -ne $w.stage_timings_ms) {
                $st = $w.stage_timings_ms
                $names = @($st.PSObject.Properties.Name)
                $pd = ($names | Where-Object { $_ -match 'duration' } | Select-Object -First 1)
                $sr = ($names | Where-Object { $_ -match 'sample' } | Select-Object -First 1)
                $dl = ($names | Where-Object { $_ -match 'decode' } | Select-Object -First 1)
                $wm = ($names | Where-Object { $_ -match 'water' } | Select-Object -First 1)
                $null = $sb.AppendLine("| $($j.label) | $(if($pd){$st.$pd}) | $(if($sr){$st.$sr}) | $(if($dl){$st.$dl}) | $(if($wm){$st.$wm}) |")
            }
        }
        catch {}
    }
    $null = $sb.AppendLine("")

    # --- compile
    $null = $sb.AppendLine("## 4. torch.compile (--compile-model)")
    $null = $sb.AppendLine("")
    $cj = Join-Path $ResultsDir 'compile.json'
    if (Test-Path -LiteralPath $cj) {
        try {
            $c = Get-Content -LiteralPath $cj -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($c.ok -eq $true) {
                $null = $sb.AppendLine("SUCCEEDED. total_to_decode = $($c.cold_total_to_decode_s) s (cold, steps=10, short text).")
            }
            else {
                $null = $sb.AppendLine("FAILED. See results/compile.json and results/logs/compile.log for the verbatim error.")
                $r0 = @($c.runs)[0]
                if ($r0 -and $r0.error) {
                    $null = $sb.AppendLine("")
                    $null = $sb.AppendLine('```')
                    $null = $sb.AppendLine([string]$r0.error)
                    $null = $sb.AppendLine('```')
                }
            }
        }
        catch { $null = $sb.AppendLine("compile.json unreadable.") }
    }
    else { $null = $sb.AppendLine("not run.") }
    $null = $sb.AppendLine("")

    # --- onnx
    $null = $sb.AppendLine("## 5. ONNX Runtime EP bench")
    $null = $sb.AppendLine("")
    $anyOnnx = $false
    foreach ($ep in @('cuda', 'dml')) {
        $of = Join-Path $ResultsDir ("onnx_" + $ep + ".json")
        if (-not (Test-Path -LiteralPath $of)) { continue }
        $anyOnnx = $true
        try {
            $o = Get-Content -LiteralPath $of -Raw -Encoding UTF8 | ConvertFrom-Json
            $null = $sb.AppendLine("### EP = $ep  (onnxruntime $($o.onnxruntime_version))")
            $null = $sb.AppendLine("")
            $null = $sb.AppendLine("providers available: " + ($o.available_providers -join ', '))
            $null = $sb.AppendLine("")
            $null = $sb.AppendLine("| model | MB | median ms | max abs diff vs CPU EP | note |")
            $null = $sb.AppendLine("|---|---|---|---|---|")
            foreach ($m in $o.models) {
                $note = ''
                foreach ($k in @('session_error', 'run_error', 'cpu_session_error', 'cpu_run_error', 'ep_fallback', 'fatal')) {
                    if ($m.$k) { $note = "$k : " + ([string]$m.$k); break }
                }
                if ($note.Length -gt 120) { $note = $note.Substring(0, 120) + '...' }
                $note = $note -replace '\|', '/'
                $mb = if ($m.bytes) { [math]::Round($m.bytes / 1MB, 1) } else { '' }
                $null = $sb.AppendLine("| $($m.model) | $mb | $($m.median_ms) | $($m.max_abs_diff_vs_cpu_ep) | $note |")
            }
            $null = $sb.AppendLine("")
        }
        catch { $null = $sb.AppendLine("onnx_$ep.json unreadable.") }
    }
    if (-not $anyOnnx) { $null = $sb.AppendLine("not run (no *.onnx in the kit's onnx\ folder, or -SkipOnnx).") }
    $null = $sb.AppendLine("")

    # --- steps
    $null = $sb.AppendLine("## 6. Steps")
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("| step | status | seconds | detail |")
    $null = $sb.AppendLine("|---|---|---|---|")
    foreach ($s in $script:Steps) {
        $d = ([string]$s.detail) -replace '\|', '/'
        if ($d.Length -gt 140) { $d = $d.Substring(0, 140) + '...' }
        $null = $sb.AppendLine("| $($s.name) | $($s.status) | $($s.seconds) | $d |")
    }
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("Raw data: results/env.json, results/install.json, results/bench_*.json, results/compile.json, results/onnx_*.json, results/steps.json, results/logs/*.log")

    Write-TextFile (Join-Path $ResultsDir 'summary.md') $sb.ToString()
    return (Join-Path $ResultsDir 'summary.md')
}

Write-JsonFile (Join-Path $ResultsDir 'steps.json') $script:Steps

$fails = @($script:Steps | Where-Object { $_.status -eq 'FAIL' })
$rfails = @($script:Steps | Where-Object { $_.status -eq 'result-FAIL' })
Write-Host ""
Write-Host "=============================================================="
Write-Host ("  done in {0:N1} min.  script failures: {1}   measured failures (a result, not a bug): {2}" -f ((Get-Date) - $script:StartedAt).TotalMinutes, $fails.Count, $rfails.Count)
Write-Host ("  summary : {0}" -f (Join-Path $ResultsDir 'summary.md'))
Write-Host ("  zip this folder and bring it back: {0}" -f $ResultsDir)
Write-Host "=============================================================="
exit 0
