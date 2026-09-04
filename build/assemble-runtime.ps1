<#
.SYNOPSIS
    Build build/out/runtime-<variant>/ from the ledger alone. No git, no pip, no uv.

.DESCRIPTION
    Acceptance condition A-1. The only inputs are ledger/python-embed.json and
    ledger/runtime-<variant>.json; every byte that lands is checked against the sha256
    recorded there and a mismatch stops the run with a non-zero exit code and no artefact.

    What the three item kinds mean:
      wheel   the .whl is a zip: unpack it whole into site-packages (*.dist-info included,
              because transformers calls importlib.metadata.version -- research note 30 N-8).
              A "<name>-<version>.data" directory is merged: purelib/ and platlib/ move up
              into site-packages, scripts/ headers/ data/ are discarded (nothing on the
              synthesis path reads them and we do not want console scripts on PATH).
      sdist   a pure python project that publishes no wheel (argbind, randomname). The
              tarball is unpacked, the importable package directories named in the ledger
              are copied, and a minimal *.dist-info is written so importlib.metadata works.
      archive a commit-pinned GitHub source zip (dacvae, silentcipher). Same treatment.

    Downloads land in build/cache/ atomically (".part" then rename), resume with an HTTP
    Range request, and are reused on the next run.

.PARAMETER Variant
    cpu | cu130 | cu126 | rocm-gfx1151

.PARAMETER ExpectGpu
    Also require the assembled python to see its accelerator: torch.cuda.is_available() true,
    torch.version.hip non-null for a rocm variant (torch.version.cuda for a cu one), a
    readable device 0, and -- on ROCm -- gcnArchName equal to the ledger's "gfx" field, not
    merely non-empty: a gfx1151 ledger assembled against a gfx1100 card would otherwise pass.
    It also runs one bf16 matmul on the device. Reading properties proves the driver answers;
    only a kernel proves the ROCm libraries this variant carries can actually compute, and it
    is the same dtype decisions.md 5/36 fixes the Radeon build to. Measured under a second on
    gfx1151. Only pass it on a machine that has the GPU: the house rule is that a non-zero
    exit leaves no artefact, so a failed check removes the runtime directory. Everything it
    downloaded stays in build/cache with its sha256 verified, so re-running costs no bytes.

.PARAMETER NoVerifyCache
    DEVELOPMENT ONLY -- DO NOT USE FOR A PRODUCT BUILD. Accepts a file already in the cache
    on its name alone, skipping the sha256 check that acceptance condition A-1 rests on.
    Every item taken this way is logged at WARN and recorded in the run's report with
    verified=false, and the size recorded in the ledger is still checked, but a doctored
    archive of the right length would go straight in.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\assemble-runtime.ps1 -Variant cpu
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cu130', 'cu126', 'rocm-gfx1151')]
    [string]$Variant = 'cpu',
    [string]$OutRoot = '',
    [string]$CacheRoot = '',
    [switch]$Force,
    [switch]$ExpectGpu,
    [switch]$NoVerifyCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

# Set once the runtime directory starts being filled; the trap below removes it if the run
# dies half way (see section 1).
$script:YwkPartialRuntimeDir = ''
trap {
    if (-not [string]::IsNullOrEmpty($script:YwkPartialRuntimeDir)) {
        if (Test-Path -LiteralPath $script:YwkPartialRuntimeDir) {
            Write-Host ('[assemble-runtime] removing the half-built ' + $script:YwkPartialRuntimeDir)
            Remove-Item -LiteralPath $script:YwkPartialRuntimeDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        $script:YwkPartialRuntimeDir = ''
    }
    break
}

$RepoRoot  = Get-YwkRepoRoot
$LedgerDir = Join-Path $RepoRoot 'ledger'
if ([string]::IsNullOrEmpty($OutRoot))   { $OutRoot = Join-Path $RepoRoot 'build\out' }
if ([string]::IsNullOrEmpty($CacheRoot)) { $CacheRoot = Join-Path $RepoRoot 'build\cache' }

$RuntimeDir = Join-Path $OutRoot ('runtime-' + $Variant)
$SitePkgs   = Join-Path $RuntimeDir 'site-packages'
$AppDir     = Join-Path $OutRoot 'app'
$TmpRoot    = Join-Path $OutRoot 'tmp'
$LogDir     = Join-Path $OutRoot 'assemble-log'

$null = New-YwkDirectory -Path $LogDir
$null = New-YwkDirectory -Path $CacheRoot
$null = Start-YwkLog -Path (Join-Path $LogDir ('assemble-runtime-' + $Variant + '.log'))

Write-YwkLog -Level 'STEP' -Message ('assemble-runtime: variant=' + $Variant)

# Items installed without a sha256 check (-NoVerifyCache). Empty on a real build.
$script:YwkUnverified = New-Object System.Collections.Generic.List[string]
# *.pth files removed from site-packages (see Install-YwkWheel).
$script:YwkDroppedPth = New-Object System.Collections.Generic.List[string]

$ledgerPath = Join-Path $LedgerDir ('runtime-' + $Variant + '.json')
$ledger = Read-YwkJsonFile -Path $ledgerPath
if ($ledger.PSObject.Properties.Name -contains 'status') {
    if ([string]$ledger.status -eq 'template') {
        throw ($ledgerPath + ' is still a template (no items). Generate it first: build\make-ledger.ps1 -Variant ' +
               $Variant + ' -SkipPythonEmbed -SkipModels -SkipVcRedist (ledger/README.md section 9).')
    }
}
if ($ledger.items.Count -eq 0) {
    throw ($ledgerPath + ' has no items.')
}
$embed = Read-YwkJsonFile -Path (Join-Path $LedgerDir 'python-embed.json')

Write-YwkLog -Message ('ledger: ' + $ledgerPath + '  items=' + $ledger.items.Count + '  generated=' + $ledger.generated)

# --------------------------------------------------------------------------- helpers

function Get-YwkCachedItem {
    <#
      .SYNOPSIS
        Fetch one ledger item into the cache and hand back the verified local path.
    #>
    param([Parameter(Mandatory = $true)]$Item)
    $names = $Item.PSObject.Properties.Name
    $fileName = ''
    if ($names -contains 'filename') { $fileName = [string]$Item.filename }
    if ([string]::IsNullOrEmpty($fileName)) {
        $u = [string]$Item.url
        $leaf = ($u -split '[?#]')[0]
        $leaf = $leaf.Substring($leaf.LastIndexOf('/') + 1)
        if ([string]::IsNullOrEmpty($leaf)) { $leaf = ([string]$Item.name) + '.bin' }
        if ($names -contains 'commit') {
            if ($leaf -eq (([string]$Item.commit) + '.zip')) {
                $leaf = ([string]$Item.name) + '-' + $leaf
            }
        }
        $fileName = $leaf
    }
    $fileName = $fileName -replace '[<>:"/\\|?*]', '_'
    $dest = Join-Path $CacheRoot $fileName
    $expectSize = [int64]0
    if ($names -contains 'size') { $expectSize = [int64]$Item.size }
    $sha = ''
    if ($names -contains 'sha256') {
        if ($null -ne $Item.sha256) { $sha = [string]$Item.sha256 }
    }
    if ([string]::IsNullOrEmpty($sha)) {
        throw ('ledger item ' + $Item.name + ' has no sha256; refusing to install it')
    }
    if ($NoVerifyCache -and (Test-Path -LiteralPath $dest)) {
        # The one path that installs a third-party file without checking its
        # sha256. Say so loudly, check what can still be checked, and leave a
        # record in the report so a build made this way is recognisable later.
        $have = Get-YwkFileSize -Path $dest
        if ($expectSize -gt 0 -and $have -ne $expectSize) {
            throw ('-NoVerifyCache: ' + $fileName + ' is ' + $have + ' B but the ledger says ' + $expectSize + ' B')
        }
        Write-YwkLog -Level 'WARN' -Message ('  UNVERIFIED ' + $fileName + '  (-NoVerifyCache: sha256 not checked; never use this for a product build)')
        $null = $script:YwkUnverified.Add([string]$Item.name)
        return $dest
    }
    $urls = @([string]$Item.url)
    if ($names -contains 'fallback_url') {
        if ($null -ne $Item.fallback_url) { $urls += [string]$Item.fallback_url }
    }
    $dl = $null
    for ($u = 0; $u -lt $urls.Count; $u++) {
        try {
            $dl = Invoke-YwkDownload -Url $urls[$u] -Destination $dest -ExpectedSha256 $sha -ExpectedSize $expectSize
            break
        } catch {
            if ($u -eq ($urls.Count - 1)) { throw }
            # The sha256 is the same file either way, so a mirror is safe to try.
            Write-YwkLog -Level 'WARN' -Message ('  ' + $urls[$u] + ' failed; trying the fallback url')
        }
    }
    if ($dl.FromCache) {
        Write-YwkLog -Message ('  cache hit  ' + $fileName)
    } else {
        Write-YwkLog -Message ('  fetched    ' + $fileName + '  ' + $dl.Size + ' B')
    }
    return $dest
}

function Write-YwkMinimalDistInfo {
    param(
        [Parameter(Mandatory = $true)][string]$SitePackages,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version,
        [AllowEmptyString()][string]$License = '',
        [string[]]$TopLevel = @()
    )
    $safe = ($Name -replace '-', '_')
    $dir = Join-Path $SitePackages ($safe + '-' + $Version + '.dist-info')
    $null = New-YwkDirectory -Path $dir
    $meta = @(
        'Metadata-Version: 2.1',
        ('Name: ' + $Name),
        ('Version: ' + $Version)
    )
    if (-not [string]::IsNullOrEmpty($License)) {
        $meta += ('License: ' + $License)
    }
    $meta += ''
    $meta += ('Installed by build/assemble-runtime.ps1 from a source archive listed in the ledger.')
    $null = Write-YwkTextFile -Path (Join-Path $dir 'METADATA') -Newline 'LF' -Text (($meta -join "`n") + "`n")
    $null = Write-YwkTextFile -Path (Join-Path $dir 'WHEEL') -Newline 'LF' -Text "Wheel-Version: 1.0`nGenerator: irodori-tts-ywk assemble-runtime.ps1`nRoot-Is-Purelib: true`nTag: py3-none-any`n"
    $null = Write-YwkTextFile -Path (Join-Path $dir 'INSTALLER') -Newline 'LF' -Text "irodori-tts-ywk`n"
    $null = Write-YwkTextFile -Path (Join-Path $dir 'RECORD') -Newline 'LF' -Text ''
    if ($TopLevel.Count -gt 0) {
        $null = Write-YwkTextFile -Path (Join-Path $dir 'top_level.txt') -Newline 'LF' -Text (($TopLevel -join "`n") + "`n")
    }
    return $dir
}

function Install-YwkWheel {
    param(
        [Parameter(Mandatory = $true)][string]$WheelPath,
        [Parameter(Mandatory = $true)][string]$SitePackages
    )
    $n = Expand-YwkZip -ZipPath $WheelPath -Destination $SitePackages
    # merge "<name>-<version>.data"
    $dataDirs = Get-ChildItem -LiteralPath $SitePackages -Directory -Filter '*.data' -ErrorAction SilentlyContinue
    if ($null -ne $dataDirs) {
        foreach ($d in $dataDirs) {
            foreach ($sub in @('purelib', 'platlib')) {
                $p = Join-Path $d.FullName $sub
                if (Test-Path -LiteralPath $p) {
                    $moved = Move-YwkDirectoryContents -Source $p -Destination $SitePackages
                    Write-YwkLog -Message ('    merged ' + $d.Name + '/' + $sub + ' (' + $moved + ' files)')
                }
            }
            foreach ($sub in @('scripts', 'headers', 'data')) {
                $p = Join-Path $d.FullName $sub
                if (Test-Path -LiteralPath $p) {
                    Remove-Item -LiteralPath $p -Recurse -Force
                    Write-YwkLog -Message ('    dropped ' + $d.Name + '/' + $sub)
                }
            }
            $left = Get-ChildItem -LiteralPath $d.FullName -Force -ErrorAction SilentlyContinue
            if ($null -eq $left -or @($left).Count -eq 0) {
                Remove-Item -LiteralPath $d.FullName -Recurse -Force
            } else {
                Write-YwkLog -Level 'WARN' -Message ('    ' + $d.Name + ' still has entries: ' + (@($left | ForEach-Object { $_.Name }) -join ', '))
            }
        }
    }
    # Drop the .pth files wheels leave in site-packages. The ._pth has no
    # "import site" (design doc section 2), so site.py never runs and a .pth is
    # never read -- it is a file that looks like it does something and does not.
    # ledger/README.md section 4-4 states that rule; this is what enforces it.
    # (setuptools ships distutils-precedence.pth, which would otherwise promise a
    # distutils shim that cannot load: python 3.12 removed distutils itself.)
    foreach ($pth in @(Get-ChildItem -LiteralPath $SitePackages -File -Filter '*.pth' -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $pth.FullName -Force
        $null = $script:YwkDroppedPth.Add($pth.Name)
        Write-YwkLog -Message ('    dropped inert ' + $pth.Name + ' (no "import site" in the ._pth)')
    }
    return $n
}

function Install-YwkSourceTree {
    <#
      .SYNOPSIS
        Unpack an archive/sdist into a scratch dir and copy the listed package directories
        into site-packages, then write a minimal dist-info.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Item,
        [Parameter(Mandatory = $true)][string]$SitePackages
    )
    $names = $Item.PSObject.Properties.Name
    $scratch = Join-Path $TmpRoot (([string]$Item.name) + '-' + [System.Guid]::NewGuid().ToString('N').Substring(0, 8))
    $null = New-YwkDirectory -Path $scratch
    try {
        if ($Path.ToLowerInvariant().EndsWith('.zip')) {
            $null = Expand-YwkZip -ZipPath $Path -Destination $scratch
        } else {
            $tar = Get-Command 'tar' -ErrorAction SilentlyContinue
            if ($null -eq $tar) {
                throw 'tar.exe is required to unpack sdists (Windows 10 1803+ ships it in System32).'
            }
            $r = Invoke-YwkNative -FilePath $tar.Source -Arguments @('-xzf', $Path, '-C', $scratch)
            if ($r.ExitCode -ne 0) {
                throw ('tar -xzf failed for ' + $Path + ': ' + $r.StdErr.Trim())
            }
        }
        # the single top-level directory inside the archive
        $roots = @(Get-ChildItem -LiteralPath $scratch -Directory)
        if ($roots.Count -ne 1) {
            throw ('expected exactly one top level directory in ' + $Path + ', found ' + $roots.Count)
        }
        $root = $roots[0].FullName

        $pkgDirs = @()
        if ($names -contains 'package_dirs') {
            $pkgDirs = @($Item.package_dirs)
        } elseif ($names -contains 'package_dir') {
            $pkgDirs = @([string]$Item.package_dir)
        } else {
            throw ('ledger item ' + $Item.name + ' has neither package_dir nor package_dirs')
        }

        $tops = New-Object System.Collections.Generic.List[string]
        foreach ($pd in $pkgDirs) {
            $src = Join-Path $root ($pd -replace '/', '\')
            if (-not (Test-Path -LiteralPath $src)) {
                throw ('package directory "' + $pd + '" is missing from ' + $Path)
            }
            $leaf = Split-Path -Leaf $src
            $dst = Join-Path $SitePackages $leaf
            if (Test-Path -LiteralPath $dst) {
                Remove-Item -LiteralPath $dst -Recurse -Force
            }
            Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
            $tops.Add($leaf)
            Write-YwkLog -Message ('    copied ' + $pd + ' -> site-packages/' + $leaf)
        }

        $lic = ''
        if ($names -contains 'license') {
            if ($null -ne $Item.license) { $lic = [string]$Item.license }
        }
        $null = Write-YwkMinimalDistInfo -SitePackages $SitePackages -Name ([string]$Item.name) `
            -Version ([string]$Item.version) -License $lic -TopLevel $tops.ToArray()
    } finally {
        if (Test-Path -LiteralPath $scratch) {
            Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# --------------------------------------------------------------------------- 1. clean

if (Test-Path -LiteralPath $RuntimeDir) {
    if (-not $Force) {
        Write-YwkLog -Level 'WARN' -Message ('removing existing ' + $RuntimeDir + ' (use -Force to silence this note)')
    }
    Remove-Item -LiteralPath $RuntimeDir -Recurse -Force
}
$null = New-YwkDirectory -Path $RuntimeDir
$null = New-YwkDirectory -Path $SitePkgs
$null = New-YwkDirectory -Path $TmpRoot
# From here on the runtime directory is half-built. If anything below throws (a sha256
# mismatch above all) the trap at the top of this script deletes it again, so a failed run
# never leaves something that a later run -- or the launcher -- could mistake for a finished
# runtime. The house rule: a non-zero exit leaves no artefact.
$script:YwkPartialRuntimeDir = $RuntimeDir

# --------------------------------------------------------------------------- 2. embedded python

Write-YwkLog -Level 'STEP' -Message 'unpacking the embedded python'
$embedItem = $embed.items[0]
$embedZip = Get-YwkCachedItem -Item $embedItem
$n = Expand-YwkZip -ZipPath $embedZip -Destination $RuntimeDir
Write-YwkLog -Message ('  ' + $n + ' files from ' + [System.IO.Path]::GetFileName($embedZip))

$pythonExe = Join-Path $RuntimeDir 'python.exe'
if (-not (Test-Path -LiteralPath $pythonExe)) {
    throw ('python.exe is missing from the embedded python zip: ' + $embedZip)
}

# --------------------------------------------------------------------------- 3. ._pth

$templatePath = Join-Path $RepoRoot 'server\python312._pth.template'
if (-not (Test-Path -LiteralPath $templatePath)) {
    throw ('missing ' + $templatePath)
}
$template = [System.IO.File]::ReadAllText($templatePath, [System.Text.Encoding]::UTF8)
$pth = $template.Replace('@RUNTIME_DIR@', (ConvertTo-YwkPosixPath -Path $RuntimeDir)).Replace('@APP_DIR@', (ConvertTo-YwkPosixPath -Path $AppDir))
if ($pth -match '@[A-Z_]+@') {
    throw ('unresolved placeholder left in python312._pth: ' + $Matches[0])
}
if ($pth -match '(?m)^\s*import\s+site\s*$') {
    throw 'the ._pth template must not contain "import site" (design doc section 2)'
}
foreach ($old in (Get-ChildItem -LiteralPath $RuntimeDir -Filter '*._pth' -File)) {
    Remove-Item -LiteralPath $old.FullName -Force
}
$pthPath = Join-Path $RuntimeDir 'python312._pth'
$null = Write-YwkTextFile -Path $pthPath -Text $pth -Newline 'CRLF'
Write-YwkLog -Message ('  wrote ' + $pthPath)
foreach ($line in ($pth -replace "`r`n", "`n").TrimEnd("`n") -split "`n") {
    Write-YwkLog -Message ('    | ' + $line)
}

# --------------------------------------------------------------------------- 4. items

Write-YwkLog -Level 'STEP' -Message ('installing ' + $ledger.items.Count + ' ledger items into site-packages')
$installed = 0
$bytes = [int64]0
foreach ($item in $ledger.items) {
    $kind = [string]$item.kind
    Write-YwkLog -Message ($item.name + '==' + $item.version + '  [' + $kind + ']')
    $local = Get-YwkCachedItem -Item $item
    $bytes = $bytes + (Get-YwkFileSize -Path $local)
    if ($kind -eq 'wheel') {
        $null = Install-YwkWheel -WheelPath $local -SitePackages $SitePkgs
    } elseif ($kind -eq 'sdist' -or $kind -eq 'archive') {
        Install-YwkSourceTree -Path $local -Item $item -SitePackages $SitePkgs
    } else {
        throw ('unknown ledger item kind "' + $kind + '" for ' + $item.name)
    }
    $installed = $installed + 1
}

if (Test-Path -LiteralPath $TmpRoot) {
    Remove-Item -LiteralPath $TmpRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# --------------------------------------------------------------------------- 5. checks

# No *.pth may survive in site-packages: with no "import site" in the ._pth they
# are never read, so one left behind is a silent lie (ledger/README.md 4-4).
# Install-YwkWheel drops them as it goes; this is the machine check that the rule
# actually held for every item in the ledger.
$strayPth = @(Get-ChildItem -LiteralPath $SitePkgs -File -Filter '*.pth' -ErrorAction SilentlyContinue)
if ($strayPth.Count -gt 0) {
    throw ('*.pth left in site-packages (never read without "import site"): ' + (@($strayPth | ForEach-Object { $_.Name }) -join ', '))
}
if ($script:YwkDroppedPth.Count -gt 0) {
    Write-YwkLog -Message ('dropped ' + $script:YwkDroppedPth.Count + ' inert *.pth: ' + ($script:YwkDroppedPth -join ', '))
}
if ($script:YwkUnverified.Count -gt 0) {
    Write-YwkLog -Level 'WARN' -Message ('UNVERIFIED BUILD: ' + $script:YwkUnverified.Count + ' item(s) installed without a sha256 check (-NoVerifyCache): ' + ($script:YwkUnverified -join ', '))
}

# --------------------------------------------------------------------------- 5b. import check
#
# A tree of unpacked zips is not yet a runtime. The check below is the first moment the
# assembled python is asked to do anything, and it is what turns "the bytes landed" into
# "torch loads out of this tree" -- the claim the ROCm variant rests on, because the ROCm
# stack resolves its DLLs through torch/_rocm_init.py -> rocm_sdk.initialize_process(), a
# path no amount of sha256 checking exercises. With -ExpectGpu it also has to see the device.

Write-YwkLog -Level 'STEP' -Message 'import check on the assembled python'

$probePy = Join-Path $LogDir ('import-check-' + $Variant + '.py')
$probeLines = @(
    'import json, os, sys',
    'out = {"python": sys.version.split()[0], "executable": sys.executable, "path_entries": len(sys.path)}',
    'mods = {}',
    'for name in ("torch", "torchaudio", "soundfile", "transformers", "numpy"):',
    '    try:',
    '        m = __import__(name)',
    '        mods[name] = getattr(m, "__version__", "?")',
    '    except Exception as exc:',
    '        mods[name] = "IMPORT FAILED: %s: %s" % (type(exc).__name__, exc)',
    'out["modules"] = mods',
    'try:',
    '    import torch',
    '    out["torch_version"] = torch.__version__',
    '    out["torch_version_cuda"] = torch.version.cuda',
    '    out["torch_version_hip"] = getattr(torch.version, "hip", None)',
    '    out["cuda_is_available"] = bool(torch.cuda.is_available())',
    '    out["device_count"] = int(torch.cuda.device_count())',
    '    devs = []',
    '    for i in range(torch.cuda.device_count()):',
    '        p = torch.cuda.get_device_properties(i)',
    '        devs.append({',
    '            "index": i,',
    '            "name": getattr(p, "name", None),',
    '            "gcn_arch_name": getattr(p, "gcnArchName", None),',
    '            "total_memory": int(getattr(p, "total_memory", 0)),',
    '            "multi_processor_count": int(getattr(p, "multi_processor_count", 0)),',
    '        })',
    '    out["devices"] = devs',
    '    if torch.cuda.is_available() and torch.cuda.device_count() > 0:',
    '        # One real kernel. Reading device properties only proves the driver',
    '        # answers; this is the first and only thing that runs arithmetic on',
    '        # the card, in the dtype decisions.md 5/36 fixes the build to.',
    '        try:',
    '            a = torch.randn(64, 64, dtype=torch.bfloat16, device="cuda:0")',
    '            total = (a @ a).float().sum().item()',
    '            out["matmul_bf16"] = "ok" if total == total else "nan"',
    '        except Exception as exc:',
    '            out["matmul_bf16"] = "%s: %s" % (type(exc).__name__, exc)',
    'except Exception as exc:',
    '    out["torch_error"] = "%s: %s" % (type(exc).__name__, exc)',
    'print("YWK-IMPORT-CHECK " + json.dumps(out, ensure_ascii=True))'
)
$null = Write-YwkTextFile -Path $probePy -Newline 'LF' -Text (($probeLines -join "`n") + "`n")

$probeEnv = New-YwkChildEnvironment -Extra @{
    'HF_HUB_OFFLINE'   = '1'
    'PYTHONIOENCODING' = 'utf-8'
}
$probeRun = Invoke-YwkNative -FilePath $pythonExe -Arguments @($probePy) -WorkingDirectory $LogDir -Environment $probeEnv -TimeoutSeconds 900
$null = Write-YwkTextFile -Path (Join-Path $LogDir ('import-check-' + $Variant + '.log')) -Newline 'LF' `
    -Text ('$ ' + $pythonExe + ' ' + $probePy + "`n--- stdout ---`n" + $probeRun.StdOut + "`n--- stderr ---`n" + $probeRun.StdErr + "`n(exit " + $probeRun.ExitCode + ")`n")

$probeJson = ''
foreach ($line in ($probeRun.StdOut -split "`n")) {
    $t = $line.Trim()
    if ($t.StartsWith('YWK-IMPORT-CHECK ')) { $probeJson = $t.Substring('YWK-IMPORT-CHECK '.Length) }
}
if ([string]::IsNullOrEmpty($probeJson)) {
    throw ('the import check produced no result (exit ' + $probeRun.ExitCode + '). See ' +
           (Join-Path $LogDir ('import-check-' + $Variant + '.log')))
}
try {
    $probe = $probeJson | ConvertFrom-Json
} catch {
    throw ('the import check emitted a line that is not json: ' + $probeJson)
}
$null = Write-YwkTextFile -Path (Join-Path $LogDir ('import-check-' + $Variant + '.json')) -Newline 'LF' -Text ($probeJson + "`n")

foreach ($p in $probe.modules.PSObject.Properties) {
    Write-YwkLog -Message ('  import ' + $p.Name + ' = ' + [string]$p.Value)
}
$importFailures = New-Object System.Collections.Generic.List[string]
foreach ($p in $probe.modules.PSObject.Properties) {
    if ([string]$p.Value -like 'IMPORT FAILED*') {
        $importFailures.Add($p.Name + ': ' + [string]$p.Value)
    }
}
if ($importFailures.Count -gt 0) {
    throw ('the assembled runtime cannot import: ' + ($importFailures -join ' | '))
}

$hip = $null
if ($probe.PSObject.Properties.Name -contains 'torch_version_hip') { $hip = $probe.torch_version_hip }
$cudaVer = $null
if ($probe.PSObject.Properties.Name -contains 'torch_version_cuda') { $cudaVer = $probe.torch_version_cuda }
$isAvail = $false
if ($probe.PSObject.Properties.Name -contains 'cuda_is_available') { $isAvail = [bool]$probe.cuda_is_available }
$devCount = 0
if ($probe.PSObject.Properties.Name -contains 'device_count') { $devCount = [int]$probe.device_count }
Write-YwkLog -Message ('  torch=' + [string]$probe.torch_version + '  hip=' + [string]$hip +
                       '  cuda=' + [string]$cudaVer + '  is_available=' + $isAvail + '  device_count=' + $devCount)
if ($probe.PSObject.Properties.Name -contains 'devices') {
    foreach ($d in $probe.devices) {
        Write-YwkLog -Message ('  device ' + [string]$d.index + ': name=' + [string]$d.name +
                               '  gcnArchName=' + [string]$d.gcn_arch_name +
                               '  total_memory=' + [string]$d.total_memory)
    }
}
if ($probe.PSObject.Properties.Name -contains 'matmul_bf16') {
    Write-YwkLog -Message ('  bf16 matmul on device 0: ' + [string]$probe.matmul_bf16)
}

if ($ExpectGpu) {
    $gpuFailures = New-Object System.Collections.Generic.List[string]
    if ($Variant -like 'rocm*') {
        if ([string]::IsNullOrEmpty([string]$hip)) {
            $gpuFailures.Add('torch.version.hip is null -- this is not a ROCm build of torch')
        }
    } elseif ($Variant -like 'cu*') {
        if ([string]::IsNullOrEmpty([string]$cudaVer)) {
            $gpuFailures.Add('torch.version.cuda is null -- this is not a CUDA build of torch')
        }
    } else {
        $gpuFailures.Add('-ExpectGpu makes no sense for variant "' + $Variant + '"')
    }
    if (-not $isAvail) {
        $gpuFailures.Add('torch.cuda.is_available() is False')
    }
    if ($devCount -lt 1) {
        $gpuFailures.Add('torch.cuda.device_count() is ' + $devCount)
    } else {
        $d0 = @($probe.devices)[0]
        if ([string]::IsNullOrEmpty([string]$d0.name)) {
            $gpuFailures.Add('device 0 reports no name')
        }
        if ($Variant -like 'rocm*') {
            # The ledger names the architecture it was solved for ("gfx": "gfx1151"),
            # and every AMD wheel in it -- amd-torch-device-gfx1151,
            # rocm-sdk-device-gfx1151 -- is that architecture's code object and no
            # other. A merely non-empty gcnArchName let a gfx1151 tree pass on a
            # gfx1100 card, i.e. the one mistake this check exists to catch went
            # through it. A ledger with no "gfx" field keeps the old rule.
            $wantGfx = ''
            if ($ledger.PSObject.Properties.Name -contains 'gfx') { $wantGfx = [string]$ledger.gfx }
            $haveGfx = [string]$d0.gcn_arch_name
            if ([string]::IsNullOrEmpty($haveGfx)) {
                $gpuFailures.Add('device 0 reports no gcnArchName')
            } elseif (-not [string]::IsNullOrEmpty($wantGfx)) {
                # gcnArchName carries the target features too ("gfx1151:xnack-").
                $baseGfx = ($haveGfx -split ':')[0]
                if ($baseGfx -ne $wantGfx) {
                    $gpuFailures.Add('device 0 is ' + $haveGfx + ' but ' + $ledgerPath +
                                     ' was solved for ' + $wantGfx)
                }
            }
        }
    }
    # The kernel. Absent means torch never got as far as running it (is_available
    # false), which the checks above already recorded.
    $matmul = ''
    if ($probe.PSObject.Properties.Name -contains 'matmul_bf16') { $matmul = [string]$probe.matmul_bf16 }
    if ($matmul -ne 'ok') {
        $gpuFailures.Add('a bf16 matmul on device 0 did not run: ' +
                         $(if ([string]::IsNullOrEmpty($matmul)) { '(not attempted)' } else { $matmul }))
    }
    if ($gpuFailures.Count -gt 0) {
        throw ('-ExpectGpu: ' + ($gpuFailures -join ' | '))
    }
    Write-YwkLog -Message ('  -ExpectGpu satisfied (bf16 matmul on device 0: ' + $matmul + ')')
}

# --------------------------------------------------------------------------- 6. report

# Everything landed: the directory is no longer half-built, so the trap must not touch it.
$script:YwkPartialRuntimeDir = ''

$distInfos = @(Get-ChildItem -LiteralPath $SitePkgs -Directory -Filter '*.dist-info' -ErrorAction SilentlyContinue)
$spBytes = [int64]0
foreach ($f in (Get-ChildItem -LiteralPath $RuntimeDir -Recurse -File -Force)) {
    $spBytes = $spBytes + $f.Length
}

$report = [ordered]@{
    variant       = $Variant
    generated     = Get-YwkTimestamp
    ledger        = ('ledger/runtime-' + $Variant + '.json')
    ledger_generated = [string]$ledger.generated
    items         = $installed
    dist_info_dirs = $distInfos.Count
    downloaded_bytes = $bytes
    runtime_bytes = $spBytes
    runtime_dir   = $RuntimeDir
    pth           = $pthPath
    verified      = ($script:YwkUnverified.Count -eq 0)
    unverified_items = $script:YwkUnverified.ToArray()
    dropped_pth   = $script:YwkDroppedPth.ToArray()
    expect_gpu    = [bool]$ExpectGpu
    import_check  = $probe
}
$null = Write-YwkJsonFile -Path (Join-Path $LogDir ('assemble-runtime-' + $Variant + '.json')) -Value $report

Write-YwkLog -Level 'STEP' -Message ('done: ' + $installed + ' items, ' + $distInfos.Count + ' dist-info dirs, ' + [math]::Round($spBytes / 1MB, 1) + ' MB on disk')
if ($distInfos.Count -lt $installed) {
    Write-YwkLog -Level 'WARN' -Message 'fewer dist-info directories than items -- check the log above'
}
exit 0
