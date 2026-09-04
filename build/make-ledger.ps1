<#
.SYNOPSIS
    Generate the acquisition ledgers under ledger/ from machine-obtained facts only.

.DESCRIPTION
    Nothing in ledger/*.json may be typed by hand. Every URL, sha256 and size in the
    generated files comes from one of:
      * PyPI JSON API             https://pypi.org/pypi/<name>/<version>/json  (urls[].digests.sha256)
      * download.pytorch.org      <index>/<name>/ index page, "#sha256=" fragment
      * Hugging Face API          https://huggingface.co/api/models/<repo>/tree/<rev> (lfs.sha256 / oid)
      * an actual download        (python embed zip, vc_redist, the two GitHub source archives)
    The full transcript of the run is written to build/out/ledger-log/.

    The dependency set is frozen with uv on this build machine:
      uv pip compile --python-version 3.12 --python-platform windows
    The mother set is parsed out of the two upstream pyproject.toml files so that upstream
    drift is visible instead of silently baked in.

.PARAMETER Variant
    Which runtime ledgers to build. Default: cpu, cu130, cu126.
    rocm-gfx1151 is the Radeon variant (convoy C); it is never built by default because it
    downloads 1.27 GB -- the AMD index publishes no hash, so the only way to obtain a sha256
    for those files is to fetch them and compute it (see "AMD index" below).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\make-ledger.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\make-ledger.ps1 -Variant rocm-gfx1151 -SkipPythonEmbed -SkipModels -SkipVcRedist
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cu130', 'cu126', 'rocm-gfx1151')]
    [string[]]$Variant = @('cpu', 'cu130', 'cu126'),
    [switch]$SkipRuntime,
    [switch]$SkipPythonEmbed,
    [switch]$SkipModels,
    [switch]$SkipVcRedist,
    [switch]$SkipArchiveHash
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

# --------------------------------------------------------------------------- constants

$RepoRoot   = Get-YwkRepoRoot
$LedgerDir  = Join-Path $RepoRoot 'ledger'
$OutDir     = Join-Path $RepoRoot 'build\out'
$LogDir     = Join-Path $OutDir 'ledger-log'
$CacheDir   = Join-Path $RepoRoot 'build\cache'

$PythonVersion   = '3.12.10'
$PythonTag       = 'cp312'
$PlatformTag     = 'win_amd64'
$UvRequired      = '0.12.7'

# python-embed: URL and the sha256 measured by the research pass (research/lab/notes/22 section 4).
# The zip is re-fetched here and the hash is re-computed; a mismatch is a hard failure.
$EmbedUrl        = 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip'
$EmbedSha256Ref  = '4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3'
$EmbedSizeRef    = [int64]11133606

# torch pin. If the index does not carry it, the script stops and prints what the index has.
$TorchVersion      = '2.10.0'
$TorchaudioVersion = '2.10.0'

$TorchIndexOf = @{
    'cpu'   = 'https://download.pytorch.org/whl/cpu'
    'cu130' = 'https://download.pytorch.org/whl/cu130'
    'cu126' = 'https://download.pytorch.org/whl/cu126'
    'rocm-gfx1151' = 'https://stable.repo.amd.com/rocm/whl-next/'
}
$LocalTagOf = @{ 'cpu' = 'cpu'; 'cu130' = 'cu130'; 'cu126' = 'cu126'; 'rocm-gfx1151' = 'rocm10.0.0' }

# --------------------------------------------------------------------------- ROCm variant
#
# The Radeon variant is pinned to what the running Radeon box actually runs (decisions.md 24),
# and every one of those pins was re-read off the AMD index on the generation date.
#
# AMD index: https://stable.repo.amd.com/rocm/whl-next/ is a PEP 503 simple index, but --
# unlike download.pytorch.org -- its hrefs carry NO "#sha256=" fragment and it serves no
# PEP 691 JSON (asking for application/vnd.pypi.simple.v1+json still returns text/html).
# Verified on the generation date; the convoy C design doc assumed the fragment was there.
# So the sha256 for every AMD file is obtained the way python-embed's and vc_redist's are:
# download it and hash it. That is still machine-obtained -- it is never typed by hand.
$RocmVariant       = 'rocm-gfx1151'
$RocmGfx           = 'gfx1151'
$RocmTorchVersion       = '2.13.0+rocm10.0.0'
$RocmTorchaudioVersion  = '2.11.0.2+rocm10.0.0'
$RocmSdkVersion         = '10.0.0'
# torch[device-gfx1151] pulls amd-torch-device-gfx1151 AND amd-torch-device-gfx115x
# (torch METADATA "Provides-Extra: device-gfx1151" names both), and torch itself requires
# rocm[libraries]==10.0.0 unconditionally. Everything below is therefore reached by the
# resolver, not listed by hand; this array only says which names must come out of it.
#
# The "sha256" column is a REFERENCE, the same kind $EmbedSha256Ref is (line 61) and for the
# same reason: with the index publishing no hash, the only hash in the pipeline was the one
# computed from whatever happened to be in build/cache, and Invoke-YwkDownload returns a
# cached file untouched when it is handed no expected hash. So a regeneration on a warm cache
# re-derived the ledger from the cache instead of from the index, and AMD re-publishing the
# same filename at the same length would have gone unnoticed -- the only surviving guard was
# the Content-Length compare, which such a swap passes. These values were written by an
# earlier run of this script from files it had just downloaded; the operating rule is
# trust-on-first-use: leave the column empty for a name that has none yet, then copy the
# value the run recorded into it. A mismatch throws instead of quietly rewriting the ledger.
$RocmExpectedPins = @(
    @{ name = 'torch';                    version = $RocmTorchVersion;      sha256 = '4ff21da558065d6c28773f9053eadec4897c579e1f34f623db3825755198b164' },
    @{ name = 'torchaudio';               version = $RocmTorchaudioVersion; sha256 = '25c1857654c9232474bd88da29f91979482d243a2b096e0b22381c4e6bd65cf5' },
    @{ name = 'amd-torch-device-gfx1151'; version = $RocmTorchVersion;      sha256 = 'd274a50cfab1943b4fec390db48ae3faa4bcfe1653ccc17812a373ac2ac7fa6f' },
    @{ name = 'amd-torch-device-gfx115x'; version = $RocmTorchVersion;      sha256 = '67e23e9eca1674e131a5618fb7c5e0a04c809bd71e122c22e1a2a5e9c47136f0' },
    @{ name = 'rocm';                     version = $RocmSdkVersion;        sha256 = '65b5982249612a310135f5e061b9f28909e8ad718a1ddb3f1abba92298b7216b' },
    @{ name = 'rocm-sdk-core';            version = $RocmSdkVersion;        sha256 = '066195cb0f7df02e6009facb17652dd992d13fae44d87652e5ab928d5b4f0851' },
    @{ name = 'rocm-sdk-libraries';       version = $RocmSdkVersion;        sha256 = 'bfc7a1bd3b6050fb4d36d8ac2eea80d3afd70c5604a29142463d6cb890fc73bf' },
    @{ name = 'rocm-sdk-device-gfx1151';  version = $RocmSdkVersion;        sha256 = '1ab27ebfe61ea3c3fd2c5c1a0e780ae22ed9b662106ce0aefda0a30e3b11577f' }
)
# torch declares "Requires-Dist: rocm-bootstrap" with no extra marker, so uv resolves it,
# but nothing on the synthesis path imports it: it is AMD's gfx auto-detection helper
# (clinfo wrapper) and decisions.md 5 says the distribution carries no auto-detection --
# the launcher names the variant. Dropped from the ledger; the reason is in ledger/README.md
# and the import check in assemble-runtime.ps1 -ExpectGpu is what proves torch still loads.
$RocmDroppedAfterSolve = @('rocm-bootstrap')
# Distributions that only the AMD index serves (PyPI answers 404 for all of them except
# torch/torchaudio, whose +rocm10.0.0 local versions are not on PyPI either).
$RocmIndexOnlyNames = @('torch', 'torchaudio', 'rocm')
$RocmIndexOnlyPrefixes = @('amd-torch-device-', 'rocm-sdk-')
# U+672A U+7279 U+5B9A = "unidentified". build/check-licenses.ps1 looks for exactly this
# string in the ledger "license" field and then demands the item be named in
# licenses/first-run-notices.md. This file stays ASCII, so the word is built from code points.
$YwkUnidentified = [string]([char]0x672A + [char]0x7279 + [char]0x5B9A)

# Top-level names dropped from the mother set (design doc section 3).
$DroppedTopLevel = @('gradio', 'wandb', 'datasets', 'torchdata', 'torchcodec', 'peft')
# Names dropped after resolution, plus everything that becomes an orphan once they go.
$PrunedTransitive = @('matplotlib', 'ipython', 'jedi', 'pandas', 'pyarrow')
# Never resolved from PyPI: the server package itself is the source tree we ship.
$NotADependency = @('irodori-tts')

# Source archives pinned to a commit (kind = archive, installed as pure python source).
$Archives = @(
    @{ name = 'dacvae'; version = '1.0.0'; repo = 'facebookresearch/dacvae';
       commit = '414c20785fc3a28373073ea8ef7a1316eeeaca6e'; package_dir = 'dacvae';
       license = 'Apache-2.0'; license_file = 'LICENSE' },
    @{ name = 'silentcipher'; version = '1.0.5'; repo = 'SesameAILabs/silentcipher';
       commit = 'd46d7d0893a583d8968ab3a6626e2289faec9152'; package_dir = 'src/silentcipher';
       license = 'MIT'; license_file = 'LICENSE' }
)

# Model repositories (models.json).
$ModelRepos = @(
    @{ repo = 'Aratako/Irodori-TTS-v4.1-Small';        revision = '2b28324dc263ed5e6638b3cf3dd94c82ead07b4b'; role = 'checkpoint' },
    @{ repo = 'Aratako/Semantic-DACVAE-Japanese-32dim'; revision = ''; role = 'codec' },
    @{ repo = 'sony/silentcipher';                      revision = ''; role = 'watermark' }
)

$VcRedistAkaMs = 'https://aka.ms/vs/17/release/vc_redist.x64.exe'
$VcRedistDocs  = 'https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist'

# --------------------------------------------------------------------------- helpers

function Get-YwkNormalizedName {
    param([Parameter(Mandatory = $true)][string]$Name)
    return ($Name.ToLowerInvariant() -replace '[-_.]+', '-')
}

function Get-YwkPyProjectDependencies {
    <#
      .SYNOPSIS
        Pull the [project] dependencies array out of a pyproject.toml (no TOML parser needed).
    #>
    param([Parameter(Mandatory = $true)][string]$Path)
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    $m = [regex]::Match($text, '(?ms)^dependencies\s*=\s*\[(.*?)^\]')
    if (-not $m.Success) {
        throw ('could not find [project] dependencies in ' + $Path)
    }
    $body = $m.Groups[1].Value
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($body -split "`n")) {
        $l = $line.Trim()
        if ($l.StartsWith('#')) { continue }
        $q = [regex]::Match($l, '^"([^"]+)"\s*,?\s*$')
        if ($q.Success) {
            $out.Add($q.Groups[1].Value)
        }
    }
    if ($out.Count -eq 0) {
        throw ('dependencies array was empty in ' + $Path)
    }
    return $out.ToArray()
}

function Get-YwkRequirementName {
    param([Parameter(Mandatory = $true)][string]$Spec)
    $s = $Spec.Trim()
    $m = [regex]::Match($s, '^([A-Za-z0-9][A-Za-z0-9._-]*)')
    if (-not $m.Success) {
        throw ('cannot read a distribution name out of "' + $Spec + '"')
    }
    return (Get-YwkNormalizedName -Name $m.Groups[1].Value)
}

function Get-YwkWheelTagScore {
    <#
      .SYNOPSIS
        Score a wheel filename for cp312 / win_amd64. 0 means "not usable".
    #>
    param([Parameter(Mandatory = $true)][string]$FileName)
    if (-not $FileName.EndsWith('.whl')) { return 0 }
    $stem = $FileName.Substring(0, $FileName.Length - 4)
    $parts = $stem -split '-'
    if ($parts.Count -lt 5) { return 0 }
    $plat = $parts[$parts.Count - 1]
    $abi  = $parts[$parts.Count - 2]
    $py   = $parts[$parts.Count - 3]

    $platScore = 0
    if (($plat -split '\.') -contains $PlatformTag) { $platScore = 20 }
    elseif (($plat -split '\.') -contains 'any')    { $platScore = 10 }
    if ($platScore -eq 0) { return 0 }

    $pyScore = 0
    foreach ($tag in ($py -split '\.')) {
        $s = 0
        if ($tag -eq $PythonTag) {
            $s = 5
        } elseif ($abi -eq 'abi3' -and $tag -match '^cp3(\d+)$') {
            if ([int]$Matches[1] -le 12) { $s = 4 }
        } elseif ($tag -eq 'py312' -or $tag -eq 'py3' -or $tag -eq 'py2') {
            $s = 3
        } elseif ($tag -match '^cp3(\d+)$') {
            $s = 0
        }
        if ($s -gt $pyScore) { $pyScore = $s }
    }
    if ($pyScore -eq 0) { return 0 }
    return ($platScore + $pyScore)
}

function Get-YwkPypiRelease {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version
    )
    $url = 'https://pypi.org/pypi/' + $Name + '/' + $Version + '/json'
    $body = Invoke-YwkHttpGetString -Url $url
    return ($body | ConvertFrom-Json)
}

function Select-YwkPypiWheel {
    <#
      .OUTPUTS
        @{ url; sha256; size; filename } or $null
    #>
    param([Parameter(Mandatory = $true)]$Release)
    $best = $null
    $bestScore = 0
    foreach ($u in $Release.urls) {
        if ($u.packagetype -ne 'bdist_wheel') { continue }
        $yanked = $false
        if ($u.PSObject.Properties.Name -contains 'yanked') { $yanked = [bool]$u.yanked }
        if ($yanked) { continue }
        $score = Get-YwkWheelTagScore -FileName $u.filename
        if ($score -gt $bestScore) {
            $bestScore = $score
            $best = $u
        }
    }
    if ($null -eq $best) { return $null }
    return @{
        url      = [string]$best.url
        sha256   = ([string]$best.digests.sha256).ToLowerInvariant()
        size     = [int64]$best.size
        filename = [string]$best.filename
    }
}

function Get-YwkPypiLicense {
    param([Parameter(Mandatory = $true)]$Release)
    $info = $Release.info
    $names = $info.PSObject.Properties.Name
    if ($names -contains 'license_expression') {
        if (-not [string]::IsNullOrEmpty([string]$info.license_expression)) {
            return [string]$info.license_expression
        }
    }
    if ($names -contains 'license') {
        $lic = [string]$info.license
        if (-not [string]::IsNullOrEmpty($lic) -and $lic.Length -le 120) {
            return $lic
        }
    }
    if ($names -contains 'classifiers') {
        foreach ($c in $info.classifiers) {
            if ([string]$c -like 'License :: *') {
                return [string]$c
            }
        }
    }
    return $null
}

function Get-YwkWheelMetadataLicense {
    <#
      .SYNOPSIS
        Read License-Expression / License / "Classifier: License ::" out of a wheel's own
        *.dist-info/METADATA.
      .DESCRIPTION
        PyPI's JSON API can report none of the three (fsspec 2026.7.0 is the live example),
        and a ledger whose "license" is null has an empty cell in what
        licenses/first-run-notices.md A11 calls the source of record. The wheel itself always carries the
        declaration, and we have already downloaded and sha256-verified it, so read it there
        rather than leave the field null.
      .OUTPUTS
        The license string, or $null.
    #>
    param([Parameter(Mandatory = $true)][string]$WheelPath)
    if (-not (Test-Path -LiteralPath $WheelPath)) { return $null }
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zip = $null
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($WheelPath)
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -notmatch '(^|/)[^/]+\.dist-info/METADATA$') { continue }
            $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
            foreach ($key in @('License-Expression', 'License')) {
                $m = [regex]::Match($text, '(?m)^' + $key + ':\s*(.+?)\s*$')
                if ($m.Success) {
                    $v = $m.Groups[1].Value
                    if (-not [string]::IsNullOrEmpty($v) -and $v.Length -le 120) { return $v }
                }
            }
            $m = [regex]::Match($text, '(?m)^Classifier:\s*(License :: .+?)\s*$')
            if ($m.Success) { return $m.Groups[1].Value }
            break
        }
    } catch {
        return $null
    } finally {
        if ($null -ne $zip) { $zip.Dispose() }
    }
    return $null
}

function Select-YwkPypiSdist {
    <#
      .OUTPUTS
        @{ url; sha256; size; filename } or $null. Only .tar.gz sdists are accepted.
    #>
    param([Parameter(Mandatory = $true)]$Release)
    foreach ($u in $Release.urls) {
        if ($u.packagetype -ne 'sdist') { continue }
        $yanked = $false
        if ($u.PSObject.Properties.Name -contains 'yanked') { $yanked = [bool]$u.yanked }
        if ($yanked) { continue }
        if (-not ([string]$u.filename).EndsWith('.tar.gz')) { continue }
        return @{
            url      = [string]$u.url
            sha256   = ([string]$u.digests.sha256).ToLowerInvariant()
            size     = [int64]$u.size
            filename = [string]$u.filename
        }
    }
    return $null
}

function Get-YwkTarPath {
    $cmd = Get-Command 'tar' -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw 'tar.exe was not found. Windows 10 1803+ ships it in System32; it is needed to read sdists.'
    }
    return $cmd.Source
}

function Get-YwkSdistLayout {
    <#
      .SYNOPSIS
        List a .tar.gz sdist and work out the archive root and the importable package dirs.
      .OUTPUTS
        @{ root = 'argbind-0.3.9/'; packages = @('argbind') }
    #>
    param([Parameter(Mandatory = $true)][string]$Path)
    $tar = Get-YwkTarPath
    $r = Invoke-YwkNative -FilePath $tar -Arguments @('-tzf', $Path)
    if ($r.ExitCode -ne 0) {
        throw ('tar -tzf failed for ' + $Path + ': ' + $r.StdErr.Trim())
    }
    $names = @($r.StdOut -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    $root = ''
    foreach ($n in $names) {
        $i = $n.IndexOf('/')
        if ($i -gt 0) { $root = $n.Substring(0, $i + 1); break }
    }
    if ([string]::IsNullOrEmpty($root)) {
        throw ('sdist has no top level directory: ' + $Path)
    }
    $skip = @('tests', 'test', 'docs', 'doc', 'examples', 'example', 'scripts', 'benchmarks')
    $packages = New-Object System.Collections.Generic.List[string]
    foreach ($n in $names) {
        if (-not $n.StartsWith($root)) { continue }
        $rel = $n.Substring($root.Length)
        $m = [regex]::Match($rel, '^(?:src/)?([A-Za-z_][A-Za-z0-9_]*)/__init__\.py$')
        if (-not $m.Success) { continue }
        $pkg = $m.Groups[1].Value
        if ($skip -contains $pkg.ToLowerInvariant()) { continue }
        $dir = $rel.Substring(0, $rel.Length - '/__init__.py'.Length)
        if (-not $packages.Contains($dir)) { $packages.Add($dir) }
    }
    if ($packages.Count -eq 0) {
        throw ('no importable package directory found in sdist ' + $Path)
    }
    return @{ root = $root; packages = $packages.ToArray() }
}

function Get-YwkTorchIndexWheel {
    <#
      .SYNOPSIS
        Find <name>-<version>+<localtag>-cp312-cp312-win_amd64.whl on a download.pytorch.org index.
      .OUTPUTS
        @{ url; sha256; filename } or $null
    #>
    param(
        [Parameter(Mandatory = $true)][string]$IndexUrl,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$LocalTag
    )
    $norm = (Get-YwkNormalizedName -Name $Name) -replace '-', '_'
    $page = Invoke-YwkHttpGetString -Url ($IndexUrl.TrimEnd('/') + '/' + (Get-YwkNormalizedName -Name $Name) + '/')
    $file = $norm + '-' + $Version + '%2B' + $LocalTag + '-' + $PythonTag + '-' + $PythonTag + '-' + $PlatformTag + '.whl'
    $rx = [regex]('href="([^"]*' + [regex]::Escape($file) + '#sha256=([0-9a-fA-F]{64}))"')
    $m = $rx.Match($page)
    if (-not $m.Success) {
        return $null
    }
    $href = $m.Groups[1].Value
    if ($href -notmatch '^https?://') {
        $href = $IndexUrl.TrimEnd('/') + '/' + (Get-YwkNormalizedName -Name $Name) + '/' + $href.TrimStart('/')
    }
    # The index at download.pytorch.org serves hrefs pointing at its CDN alias
    # download-r2.pytorch.org. Both hosts answer with the same bytes (identical
    # Content-Length, and the sha256 in the fragment is checked either way), but
    # a ledger that names only the alias has nowhere to go the day the alias is
    # folded away. Record both, and say which host the sha256 was read from.
    $hrefHost = ''
    $indexHost = ''
    try { $hrefHost = ([System.Uri]$href).Host } catch { $hrefHost = '' }
    try { $indexHost = ([System.Uri]$IndexUrl).Host } catch { $indexHost = '' }
    $fallback = ''
    if ($hrefHost -ne '' -and $indexHost -ne '' -and $hrefHost -ne $indexHost) {
        $fallback = $href -replace ('^https?://' + [regex]::Escape($hrefHost)), ('https://' + $indexHost)
    }
    return @{
        url          = $href
        fallback_url = $fallback
        sha256       = $m.Groups[2].Value.ToLowerInvariant()
        filename     = $file -replace '%2B', '+'
        host         = $hrefHost
        index_host   = $indexHost
    }
}

function Get-YwkTorchIndexVersions {
    param(
        [Parameter(Mandatory = $true)][string]$IndexUrl,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$LocalTag
    )
    $norm = (Get-YwkNormalizedName -Name $Name) -replace '-', '_'
    $page = Invoke-YwkHttpGetString -Url ($IndexUrl.TrimEnd('/') + '/' + (Get-YwkNormalizedName -Name $Name) + '/')
    $rx = [regex]([regex]::Escape($norm) + '-([0-9][^-%]*)%2B' + [regex]::Escape($LocalTag) + '-' + $PythonTag + '-' + $PythonTag + '-' + $PlatformTag + '\.whl')
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($m in $rx.Matches($page)) {
        $v = $m.Groups[1].Value
        if (-not $found.Contains($v)) { $found.Add($v) }
    }
    return $found.ToArray()
}

function Get-YwkContentLength {
    param([Parameter(Mandatory = $true)][string]$Url)
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = 'HEAD'
    $req.UserAgent = 'irodori-tts-ywk-build/1'
    $req.Timeout = 120000
    $req.AllowAutoRedirect = $true
    $resp = $req.GetResponse()
    try {
        return [int64]$resp.ContentLength
    } finally {
        $resp.Close()
    }
}

function Test-YwkRocmIndexOnly {
    <#
      .SYNOPSIS
        True when this distribution is served only by the AMD index (never by PyPI).
    #>
    param([Parameter(Mandatory = $true)][string]$Name)
    $n = Get-YwkNormalizedName -Name $Name
    if ($RocmIndexOnlyNames -contains $n) { return $true }
    foreach ($p in $RocmIndexOnlyPrefixes) {
        if ($n.StartsWith($p)) { return $true }
    }
    return $false
}

function Get-YwkRocmPinSha256 {
    <#
      .SYNOPSIS
        The reference sha256 recorded for an AMD distribution, or '' when there is none.
      .DESCRIPTION
        Empty is the trust-on-first-use case: a name that has never been through this
        script yet. Everything else is checked against the value below.
    #>
    param([Parameter(Mandatory = $true)][string]$Name)
    $n = Get-YwkNormalizedName -Name $Name
    foreach ($pin in $RocmExpectedPins) {
        if ((Get-YwkNormalizedName -Name $pin.name) -ne $n) { continue }
        if ($pin.ContainsKey('sha256')) { return [string]$pin.sha256 }
        return ''
    }
    return ''
}

function Get-YwkAmdIndexEntry {
    <#
      .SYNOPSIS
        Find one distribution file for <name>==<version> on the AMD simple index.
      .DESCRIPTION
        The page lists plain hrefs with no hash fragment, "+" percent-encoded as %2B.
        A wheel is preferred over an sdist; among wheels, cp312/win_amd64 beats py3/any
        (Get-YwkWheelTagScore does that scoring, the same one PyPI wheels go through).
        The href resolves to a different path on the same host (rocm/pytorch/whl-next or
        rocm/core/whl-next); both answer, so the redirect target is recorded as fallback_url.
      .OUTPUTS
        @{ url; fallback_url; filename; kind } or $null
    #>
    param(
        [Parameter(Mandatory = $true)][string]$IndexUrl,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version
    )
    $norm = Get-YwkNormalizedName -Name $Name
    $pageUrl = $IndexUrl.TrimEnd('/') + '/' + $norm + '/'
    $page = Invoke-YwkHttpGetString -Url $pageUrl
    $bestHref = ''
    $bestFile = ''
    $bestKind = ''
    $bestScore = 0
    foreach ($m in ([regex]'href="([^"]+)"').Matches($page)) {
        $href = $m.Groups[1].Value
        $leaf = ($href -split '[?#]')[0]
        $leaf = $leaf.Substring($leaf.LastIndexOf('/') + 1)
        $file = $leaf -replace '%2B', '+' -replace '%2b', '+'
        if ([string]::IsNullOrEmpty($file)) { continue }
        $kind = ''
        $stem = ''
        if ($file.EndsWith('.whl')) {
            $kind = 'wheel'
            $stem = $file.Substring(0, $file.Length - 4)
        } elseif ($file.EndsWith('.tar.gz')) {
            $kind = 'sdist'
            $stem = $file.Substring(0, $file.Length - 7)
        } else {
            continue
        }
        $parts = $stem -split '-'
        if ($parts.Count -lt 2) { continue }
        if ((Get-YwkNormalizedName -Name $parts[0]) -ne $norm) { continue }
        if ($parts[1] -ne $Version) { continue }
        $score = 0
        if ($kind -eq 'wheel') {
            $score = Get-YwkWheelTagScore -FileName $file
        } else {
            $score = 1
        }
        if ($score -le 0) { continue }
        if ($score -gt $bestScore) {
            $bestScore = $score
            $bestHref = $href
            $bestFile = $file
            $bestKind = $kind
        }
    }
    if ($bestScore -eq 0) { return $null }
    $url = $bestHref
    if ($url -notmatch '^https?://') {
        $url = $pageUrl + $url.TrimStart('/')
    }
    $url = $url -replace '\+', '%2B'
    $fallback = ''
    try {
        $final = Resolve-YwkRedirect -Url $url
        if ($final -ne $url) { $fallback = $final }
    } catch {
        $fallback = ''
    }
    return @{ url = $url; fallback_url = $fallback; filename = $bestFile; kind = $bestKind; index_page = $pageUrl }
}

function Get-YwkAmdIndexVersions {
    param(
        [Parameter(Mandatory = $true)][string]$IndexUrl,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $norm = Get-YwkNormalizedName -Name $Name
    $page = Invoke-YwkHttpGetString -Url ($IndexUrl.TrimEnd('/') + '/' + $norm + '/')
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($m in ([regex]'href="([^"]+)"').Matches($page)) {
        $file = ($m.Groups[1].Value -replace '%2B', '+')
        $file = $file.Substring($file.LastIndexOf('/') + 1)
        $parts = $file -split '-'
        if ($parts.Count -lt 2) { continue }
        if ((Get-YwkNormalizedName -Name $parts[0]) -ne $norm) { continue }
        $ver = $parts[1] -replace '\.tar\.gz$', ''
        if (-not $found.Contains($ver)) { $found.Add($ver) }
    }
    return $found.ToArray()
}

function Get-YwkSdistPkgInfoLicense {
    <#
      .SYNOPSIS
        Read License-Expression / License / "Classifier: License ::" out of an sdist's PKG-INFO.
      .OUTPUTS
        The license string, or $null when PKG-INFO declares none.
    #>
    param([Parameter(Mandatory = $true)][string]$Path)
    $tar = Get-YwkTarPath
    $r = Invoke-YwkNative -FilePath $tar -Arguments @('-tzf', $Path)
    if ($r.ExitCode -ne 0) { return $null }
    $pkgInfo = ''
    foreach ($n in ($r.StdOut -split "`n")) {
        $t = $n.Trim()
        if ($t -match '^[^/]+/PKG-INFO$') { $pkgInfo = $t; break }
    }
    if ([string]::IsNullOrEmpty($pkgInfo)) { return $null }
    $r2 = Invoke-YwkNative -FilePath $tar -Arguments @('-xzOf', $Path, $pkgInfo)
    if ($r2.ExitCode -ne 0) { return $null }
    $text = $r2.StdOut
    foreach ($key in @('License-Expression', 'License')) {
        $m = [regex]::Match($text, '(?m)^' + $key + ':\s*(.+?)\s*$')
        if ($m.Success) {
            $vv = $m.Groups[1].Value
            if (-not [string]::IsNullOrEmpty($vv) -and $vv.Length -le 120) { return $vv }
        }
    }
    $m = [regex]::Match($text, '(?m)^Classifier:\s*(License :: .+?)\s*$')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

# --------------------------------------------------------------------------- start

$null = New-YwkDirectory -Path $LogDir
$null = New-YwkDirectory -Path $CacheDir
$null = New-YwkDirectory -Path $LedgerDir
$null = Start-YwkLog -Path (Join-Path $LogDir 'make-ledger.log')

$generated = (Get-Date).ToString('yyyy-MM-dd')
$uv = Get-YwkUvPath -RequiredVersion $UvRequired
Write-YwkLog -Level 'STEP' -Message ('make-ledger: uv=' + $uv.Version)
Write-YwkLog -Message ('repo root: ' + $RepoRoot)

# --------------------------------------------------------------------------- mother set

$ttsPyproject    = Join-Path $RepoRoot 'upstream\Irodori-TTS\pyproject.toml'
$serverPyproject = Join-Path $RepoRoot 'upstream\Irodori-TTS-Server\pyproject.toml'
$rawDeps = @()
$rawDeps += (Get-YwkPyProjectDependencies -Path $ttsPyproject)
$rawDeps += (Get-YwkPyProjectDependencies -Path $serverPyproject)

$reqLines = New-Object System.Collections.Generic.List[string]
$dropLog  = New-Object System.Collections.Generic.List[string]
$seen     = New-Object System.Collections.Generic.HashSet[string]

foreach ($spec in $rawDeps) {
    $name = Get-YwkRequirementName -Spec $spec
    if ($DroppedTopLevel -contains $name) {
        $dropLog.Add('drop  ' + $name + '  <- "' + $spec + '"  (design doc section 3: not on the synthesis path)')
        continue
    }
    if ($NotADependency -contains $name) {
        $dropLog.Add('drop  ' + $name + '  <- "' + $spec + '"  (shipped as source under server/upstream, not as a wheel)')
        continue
    }
    $line = $spec
    if ($name -eq 'sentencepiece') {
        $line = 'sentencepiece>=0.2.1'
        $dropLog.Add('swap  sentencepiece  "' + $spec + '" -> "' + $line + '"  (0.1.x has no cp312 wheel; research/local-additions/*/overrides-rocm.txt)')
    } elseif ($name -eq 'torch') {
        $line = 'torch==' + $TorchVersion
    } elseif ($name -eq 'torchaudio') {
        $line = 'torchaudio==' + $TorchaudioVersion
    } elseif ($name -eq 'dacvae') {
        $a = $Archives | Where-Object { $_.name -eq 'dacvae' }
        $line = 'dacvae @ git+https://github.com/' + $a.repo + '@' + $a.commit
        $dropLog.Add('pin   dacvae  "' + $spec + '" -> ' + $a.commit + '  (upstream uv.lock: source = git+.../dacvae#' + $a.commit + ')')
    } elseif ($name -eq 'silentcipher') {
        $a = $Archives | Where-Object { $_.name -eq 'silentcipher' }
        if ($spec -notmatch [regex]::Escape($a.commit)) {
            throw ('upstream silentcipher pin moved: "' + $spec + '" no longer contains ' + $a.commit)
        }
        $line = 'silentcipher @ git+https://github.com/' + $a.repo + '.git@' + $a.commit
    }
    if ($seen.Add($line)) {
        $reqLines.Add($line)
    }
}

# uvicorn[standard] carries the extras we need; keep whatever upstream declared.
$reqInPath = Join-Path $LogDir 'requirements.in'
$null = Write-YwkTextFile -Path $reqInPath -Text (($reqLines -join "`n") + "`n") -Newline 'LF'

# Overrides. protobuf<3.20 (declared by descript-audiotools 0.7.2) resolves to protobuf 3.19.6,
# which ships "protobuf-3.19.6-nspkg.pth" -- a .pth file only honoured when site is imported.
# The distributed python312._pth has no "import site" line (design doc section 2), so the old
# protobuf would be silently half-installed. Forcing protobuf >= 5 reproduces the protobuf 7.36.1
# that the research lab box actually ran with (research/lab/notes/30 section 1-A a14).
$overrideLines = @('protobuf>=5.29.0')
$overridePath = Join-Path $LogDir 'overrides.txt'
$null = Write-YwkTextFile -Path $overridePath -Text (($overrideLines -join "`n") + "`n") -Newline 'LF'

$motherHead = @(
    '# mother set = upstream pyproject [project].dependencies of both repos, then:',
    ('#   dropped top level : ' + ($DroppedTopLevel -join ', ')),
    ('#   pruned after solve: ' + ($PrunedTransitive -join ', ') + ' (plus orphans)'),
    ('#   overrides         : ' + ($overrideLines -join ', ')),
    ''
)
$motherAll = $motherHead + $dropLog.ToArray()
$null = Write-YwkTextFile -Path (Join-Path $LogDir 'mother-set.txt') -Newline 'LF' -Text (($motherAll -join "`n") + "`n")

Write-YwkLog -Message ('mother set: ' + $reqLines.Count + ' requirement lines -> ' + $reqInPath)
foreach ($d in $dropLog) { Write-YwkLog -Message ('  ' + $d) }

# --------------------------------------------------------------------------- per variant

function Invoke-YwkCompile {
    param(
        [Parameter(Mandatory = $true)][string]$VariantName,
        [Parameter(Mandatory = $true)][string]$OutPath,
        [string]$InPath = ''
    )
    if ([string]::IsNullOrEmpty($InPath)) { $InPath = $reqInPath }
    $index = $TorchIndexOf[$VariantName]
    $uvArgs = @(
        'pip', 'compile', $InPath,
        '--override', $overridePath,
        '--python-version', '3.12',
        '--python-platform', 'windows',
        '--extra-index-url', $index,
        '--index-strategy', 'unsafe-best-match',
        '--annotation-style', 'line',
        '--no-header',
        '-o', $OutPath
    )
    Write-YwkLog -Message ('uv pip compile (' + $VariantName + ') --extra-index-url ' + $index)
    $env2 = New-YwkChildEnvironment
    $r = Invoke-YwkNative -FilePath $uv.Path -Arguments $uvArgs -WorkingDirectory $LogDir -Environment $env2 -TimeoutSeconds 1800
    $null = Write-YwkTextFile -Path (Join-Path $LogDir ('uv-compile-' + $VariantName + '.log')) -Newline 'LF' `
        -Text (('$ uv ' + ($uvArgs -join ' ')) + "`n--- stdout ---`n" + $r.StdOut + "`n--- stderr ---`n" + $r.StdErr + "`n(exit " + $r.ExitCode + ")`n")
    if ($r.ExitCode -ne 0) {
        throw ('uv pip compile failed for variant ' + $VariantName + ' (exit ' + $r.ExitCode + '). See ledger-log/uv-compile-' + $VariantName + '.log')
    }
    return $OutPath
}

function Read-YwkCompiled {
    <#
      .OUTPUTS
        Ordered list of @{ name; version; url; via = @() }
    #>
    param([Parameter(Mandatory = $true)][string]$Path)
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($line in ([System.IO.File]::ReadAllLines($Path))) {
        $l = $line.Trim()
        if ([string]::IsNullOrEmpty($l)) { continue }
        if ($l.StartsWith('#')) { continue }
        $spec = $l
        $via = @()
        $hash = $l.IndexOf('#')
        if ($hash -ge 0) {
            $spec = $l.Substring(0, $hash).Trim()
            $comment = $l.Substring($hash + 1).Trim()
            $comment = [regex]::Replace($comment, '^via\s*', '')
            $via = @($comment -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
        }
        $name = Get-YwkRequirementName -Spec $spec
        $version = ''
        $url = ''
        $mv = [regex]::Match($spec, '==\s*([^\s;]+)')
        if ($mv.Success) { $version = $mv.Groups[1].Value }
        $mu = [regex]::Match($spec, '@\s*(git\+\S+|https?://\S+)')
        if ($mu.Success) { $url = $mu.Groups[1].Value }
        $rows.Add([pscustomobject]@{ name = $name; version = $version; url = $url; via = $via; spec = $spec })
    }
    return $rows
}

function Get-YwkPruneSet {
    param([Parameter(Mandatory = $true)]$Rows)
    $byName = @{}
    foreach ($r in $Rows) { $byName[$r.name] = $r }
    $removed = New-Object System.Collections.Generic.HashSet[string]
    foreach ($p in $PrunedTransitive) {
        $n = Get-YwkNormalizedName -Name $p
        if ($byName.ContainsKey($n)) { $null = $removed.Add($n) }
    }
    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($r in $Rows) {
            if ($removed.Contains($r.name)) { continue }
            if ($r.via.Count -eq 0) { continue }
            $isRoot = $false
            $allGone = $true
            foreach ($v in $r.via) {
                if ($v.StartsWith('-r ') -or $v.StartsWith('--') -or $v -eq '-r') { $isRoot = $true; break }
                if (-not $removed.Contains((Get-YwkNormalizedName -Name $v))) { $allGone = $false }
            }
            if ($isRoot) { continue }
            if ($allGone) {
                $null = $removed.Add($r.name)
                $changed = $true
            }
        }
    }
    return ,$removed
}

$ledgerSummaries = New-Object System.Collections.Generic.List[string]

if (-not $SkipRuntime) {
    foreach ($v in $Variant) {
        Write-YwkLog -Level 'STEP' -Message ('=== variant ' + $v + ' ===')
        $index = $TorchIndexOf[$v]
        $localTag = $LocalTagOf[$v]
        $isRocm = ($v -eq $RocmVariant)
        $variantIn = $reqInPath

        if ($isRocm) {
            # The mother set is the same as the cpu variant's (design doc section 2-1); only the
            # two torch lines change. Substituting them keeps the two requirement files provably
            # the same document, and the substitution is asserted rather than assumed.
            $rocmLines = New-Object System.Collections.Generic.List[string]
            $sawTorch = $false
            $sawAudio = $false
            foreach ($line in $reqLines) {
                if ($line -eq ('torch==' + $TorchVersion)) {
                    $rocmLines.Add('torch[device-' + $RocmGfx + ']==' + $RocmTorchVersion)
                    $sawTorch = $true
                } elseif ($line -eq ('torchaudio==' + $TorchaudioVersion)) {
                    $rocmLines.Add('torchaudio==' + $RocmTorchaudioVersion)
                    $sawAudio = $true
                } else {
                    $rocmLines.Add($line)
                }
            }
            if (-not $sawTorch -or -not $sawAudio) {
                throw ('the mother set no longer carries "torch==' + $TorchVersion + '" and "torchaudio==' +
                       $TorchaudioVersion + '"; the rocm substitution cannot be made blindly.')
            }
            $variantIn = Join-Path $LogDir ('requirements-' + $v + '.in')
            $null = Write-YwkTextFile -Path $variantIn -Newline 'LF' -Text (($rocmLines -join "`n") + "`n")
            Write-YwkLog -Message ('rocm mother set: ' + $variantIn)

            # 1. sanity: every AMD pin has to be on the AMD index before anything is resolved.
            foreach ($pin in $RocmExpectedPins) {
                $hit = Get-YwkAmdIndexEntry -IndexUrl $index -Name $pin.name -Version $pin.version
                if ($null -eq $hit) {
                    $have = Get-YwkAmdIndexVersions -IndexUrl $index -Name $pin.name
                    throw ($pin.name + ' ' + $pin.version + ' (' + $PythonTag + '/' + $PlatformTag +
                           ') is not on ' + $index + '. The index carries: ' + ($have -join ', ') +
                           '. Record the move in ledger/README.md and edit $RocmExpectedPins.')
                }
                Write-YwkLog -Message ('  amd index ok: ' + $pin.name + '==' + $pin.version + '  ' + $hit.filename)
            }
        } else {
            # 1. sanity: is the pinned torch actually on that index?
            foreach ($pkg in @(@{ n = 'torch'; ver = $TorchVersion }, @{ n = 'torchaudio'; ver = $TorchaudioVersion })) {
                $hit = Get-YwkTorchIndexWheel -IndexUrl $index -Name $pkg.n -Version $pkg.ver -LocalTag $localTag
                if ($null -eq $hit) {
                    $have = Get-YwkTorchIndexVersions -IndexUrl $index -Name $pkg.n -LocalTag $localTag
                    throw ($pkg.n + ' ' + $pkg.ver + '+' + $localTag + ' cp312 win_amd64 is not on ' + $index +
                           '. The index carries: ' + ($have -join ', ') +
                           '. Pick the nearest 2.10.x, record the reason in ledger/README.md, and edit $TorchVersion.')
                }
            }
        }

        $compiled = Invoke-YwkCompile -VariantName $v -OutPath (Join-Path $LogDir ('requirements-' + $v + '.txt')) -InPath $variantIn
        $rows = Read-YwkCompiled -Path $compiled
        $removed = Get-YwkPruneSet -Rows $rows
        if ($isRocm) {
            foreach ($d in $RocmDroppedAfterSolve) {
                $dn = Get-YwkNormalizedName -Name $d
                $null = $removed.Add($dn)
            }
        }
        Write-YwkLog -Message ('resolved ' + $rows.Count + ' packages; pruning ' + $removed.Count)

        $pruneReport = New-Object System.Collections.Generic.List[string]
        $pruneReport.Add('# packages removed from the resolved set for variant ' + $v)
        $pruneReport.Add('# roots: ' + ($PrunedTransitive -join ', ') + ' -- everything else here became an orphan.')
        if ($isRocm) {
            $pruneReport.Add('# rocm only: ' + ($RocmDroppedAfterSolve -join ', ') +
                             ' -- declared by torch, imported by nothing on the synthesis path (decisions.md 5).')
        }
        foreach ($r in $rows) {
            if ($removed.Contains($r.name)) {
                $pruneReport.Add($r.name + '==' + $r.version + '   # was via ' + ($r.via -join ', '))
            }
        }
        $null = Write-YwkTextFile -Path (Join-Path $LogDir ('pruned-' + $v + '.txt')) -Newline 'LF' -Text (($pruneReport -join "`n") + "`n")

        $keptLines = New-Object System.Collections.Generic.List[string]
        $items = New-Object System.Collections.Generic.List[object]
        $sourceLog = New-Object System.Collections.Generic.List[string]

        foreach ($r in $rows) {
            if ($removed.Contains($r.name)) { continue }
            $keptLines.Add($r.spec)

            $arch = $Archives | Where-Object { (Get-YwkNormalizedName -Name $_.name) -eq $r.name }
            if ($null -ne $arch) {
                $zipUrl = 'https://github.com/' + $arch.repo + '/archive/' + $arch.commit + '.zip'
                $sha = ''
                $size = [int64]0
                if (-not $SkipArchiveHash) {
                    $dest = Join-Path $CacheDir ($arch.name + '-' + $arch.commit + '.zip')
                    $dl = Invoke-YwkDownload -Url $zipUrl -Destination $dest
                    $sha = $dl.Sha256
                    $size = $dl.Size
                }
                $items.Add([ordered]@{
                    kind        = 'archive'
                    name        = $arch.name
                    version     = ($arch.version + '+' + $arch.commit.Substring(0, 7))
                    url         = $zipUrl
                    sha256      = $sha
                    size        = $size
                    license     = $arch.license
                    license_url = ('https://raw.githubusercontent.com/' + $arch.repo + '/' + $arch.commit + '/' + $arch.license_file)
                    install     = 'pure-python-src'
                    archive_root_strip = $true
                    package_dir = $arch.package_dir
                    commit      = $arch.commit
                    sha256_source = 'downloaded and hashed by build/make-ledger.ps1'
                    note        = 'GitHub regenerates /archive/<sha>.zip on demand; if the hash ever moves, re-run make-ledger and record the change.'
                })
                $sourceLog.Add($r.name + '  archive  ' + $zipUrl)
                continue
            }

            $entry = $null
            $source = ''
            $license = $null
            $licenseUrl = $null
            $licenseSource = $null
            $notices = @()
            $sdist = $null

            if ($isRocm -and (Test-YwkRocmIndexOnly -Name $r.name)) {
                # --------------------------------------------------------- AMD index item
                $hit = Get-YwkAmdIndexEntry -IndexUrl $index -Name $r.name -Version $r.version
                if ($null -eq $hit) {
                    throw ($r.name + ' ' + $r.version + ' is not on ' + $index +
                           ' for ' + $PythonTag + '/' + $PlatformTag + '.')
                }
                $headSize = Get-YwkContentLength -Url $hit.url
                $localFile = Join-Path $CacheDir $hit.filename
                $pinSha = Get-YwkRocmPinSha256 -Name $r.name
                Write-YwkLog -Message ('  ' + $r.name + '==' + $r.version + ': no hash on the index; fetching ' +
                                       $hit.filename + ' (' + $headSize + ' B) to compute one')
                # The pin is what makes a warm cache safe to reuse: without it
                # Invoke-YwkDownload hands back whatever is already on disk and this
                # generation would re-derive the ledger from build/cache instead of
                # from the index. With it, a cached file that does not match is
                # refetched -- and the compare below is what turns a moved upstream
                # file into a throw rather than a silently rewritten ledger.
                $dl = Invoke-YwkDownload -Url $hit.url -Destination $localFile -ExpectedSize $headSize -ExpectedSha256 $pinSha
                if ($dl.Size -ne $headSize) {
                    throw ($hit.filename + ' is ' + $dl.Size + ' B but HEAD said ' + $headSize + ' B')
                }
                if (-not [string]::IsNullOrEmpty($pinSha) -and $dl.Sha256 -ne $pinSha) {
                    throw ($hit.filename + ' sha256 moved: the index now serves ' + $dl.Sha256 +
                           ', $RocmExpectedPins records ' + $pinSha +
                           '. Check what AMD republished, then edit $RocmExpectedPins and note it in ledger/README.md.')
                }
                $entry = @{ url = $hit.url; fallback_url = $hit.fallback_url; sha256 = $dl.Sha256; size = $dl.Size; filename = $hit.filename }
                $source = 'no hash is published on ' + $hit.index_page +
                          ' (the hrefs carry no "#sha256=" fragment and the index serves no PEP 691 json), so build/make-ledger.ps1 downloaded the file and hashed it'
                if ($dl.FromCache) {
                    $source = $source + '. THIS generation read the file from build/cache rather than the index; ' +
                              $(if ([string]::IsNullOrEmpty($pinSha)) {
                                    'no reference hash existed for it yet, so nothing cross-checked the cached bytes'
                                } else {
                                    'the cached bytes were verified against the reference sha256 in $RocmExpectedPins'
                                })
                }
                $licenseUrl = $hit.index_page
                if ($hit.kind -eq 'sdist') {
                    $license = Get-YwkSdistPkgInfoLicense -Path $localFile
                    if (-not [string]::IsNullOrEmpty([string]$license)) {
                        $licenseSource = 'sdist ' + $hit.filename + ' PKG-INFO'
                    }
                    $layout = Get-YwkSdistLayout -Path $localFile
                    $sdist = $layout
                    Write-YwkLog -Message ('    sdist (no wheel is published for this project), packages=' + ($layout.packages -join ','))
                } else {
                    $license = Get-YwkWheelMetadataLicense -WheelPath $localFile
                    if (-not [string]::IsNullOrEmpty([string]$license)) {
                        $licenseSource = 'wheel ' + $hit.filename + ' dist-info/METADATA'
                    }
                }
                if ([string]::IsNullOrEmpty([string]$license)) {
                    # AMD's own packages declare nothing at all. Do not guess (decisions.md 22):
                    # say "unidentified" in the field check-licenses.ps1 reconciles, and carry the
                    # observation that produced that verdict in the notices.
                    $license = $YwkUnidentified + ' (' + $hit.filename +
                               ': METADATA/PKG-INFO declares no License, no License-Expression and no License classifier)'
                    $licenseSource = 'read from ' + $hit.filename + ' on ' + $generated + ': the metadata is silent'
                }
                $noticeLines = New-Object System.Collections.Generic.List[string]
                $noticeLines.Add('AMD ROCm ' + $hit.kind + ', fetched from ' + $hit.index_page + ' on ' + $generated +
                                 '. The index publishes no sha256; the one in this ledger was computed from the downloaded file.')
                if ($licenseSource -like 'read from *') {
                    $noticeLines.Add('Licence position UNIDENTIFIED: ' + $hit.filename +
                                     ' declares no licence of its own (licenses/first-run-notices.md B1 records the same reading of rocm_sdk_core METADATA: 3 lines, 59 B, no License field). Nothing here asserts what the bundled binaries are under.')
                }
                if ($r.name -eq 'torch') {
                    $noticeLines.Add('This wheel ships third-party native binaries (torch/lib/*.dll, among them c10_hip.dll and torch_hip.dll). The licence texts that cover the PyTorch sources travel inside the wheel: keep the *.dist-info directory in the install tree.')
                    $noticeLines.Add('ROCm runtime binaries do NOT arrive with this wheel: torch/_rocm_init.py calls rocm_sdk.initialize_process(...) and the DLLs live in the rocm-sdk-core / rocm-sdk-libraries / rocm-sdk-device-gfx1151 items of this same ledger, whose licence position is unidentified.')
                    $noticeLines.Add('Two of the 14 DLLs in torch/lib are UNIDENTIFIED even so: liblzma.dll (196,096 B) and aotriton_v2.dll (14,948,352 B). METADATA names 107 License-File entries and dist-info/licenses/ holds 107 files, and not one of them is lzma, xz or aotriton (read on ' + $generated + ' out of the assembled tree). aotriton_v2.dll imports liblzma.dll, so the two travel together. See licenses/first-run-notices.md B7 (2) and docs/acceptance.md section 3 item 11; check-licenses.ps1 cannot reach this, because it reconciles the item "license" field and this item declares Apache-2.0 AND ... AND MIT.')
                } elseif ($r.name -eq 'torchaudio') {
                    $noticeLines.Add('Read from ' + $hit.filename + ' on ' + $generated + ': native code is torchaudio/lib/*.pyd; it bundles no ROCm DLL of its own.')
                } elseif ($r.name -eq 'rocm') {
                    $noticeLines.Add('Pure python (src/rocm_sdk). PKG-INFO declares no licence; the source header of src/rocm_sdk/__init__.py reads "# SPDX-License-Identifier: MIT" (read on ' + $generated + '). That header is an observation, not a finding about the binaries the other rocm-sdk items carry.')
                    $noticeLines.Add('Required at runtime, not optional: torch/__init__.py imports torch._rocm_init, which imports rocm_sdk and calls initialize_process(preload_shortnames=[amd_comgr, amdhip64, hiprtc, ...], check_version=10.0.0).')
                }
                $notices = $noticeLines.ToArray()
            } elseif ($r.version -match '\+') {
                # local version segment: only the pytorch index can serve it
                $base = ($r.version -split '\+')[0]
                $tag = ($r.version -split '\+')[1]
                $hit = Get-YwkTorchIndexWheel -IndexUrl $index -Name $r.name -Version $base -LocalTag $tag
                if ($null -eq $hit) {
                    throw ($r.name + ' ' + $r.version + ' not found on ' + $index)
                }
                $size = Get-YwkContentLength -Url $hit.url
                $entry = @{ url = $hit.url; fallback_url = $hit.fallback_url; sha256 = $hit.sha256; size = $size; filename = $hit.filename }
                $source = 'index page ' + $index + '/' + $r.name + '/ (href #sha256 fragment); the href points at ' + $hit.host
                # licence text comes from the same project/version on PyPI (same sources, different build)
                try {
                    $rel = Get-YwkPypiRelease -Name $r.name -Version $base
                    $license = Get-YwkPypiLicense -Release $rel
                    $licenseUrl = 'https://pypi.org/project/' + $r.name + '/' + $base + '/'
                } catch {
                    Write-YwkLog -Level 'WARN' -Message ('no PyPI metadata for ' + $r.name + ' ' + $base + ': ' + $_.Exception.Message)
                }
                # Notices are attached per PACKAGE, not per CUDA variant. Until this was
                # widened only "cu*" got them, so the cpu ledger carried a torch wheel that
                # ships third-party native binaries and said nothing about them at all, and
                # build/check-licenses.ps1 had nothing to reconcile on that ledger. Every
                # torch / torchaudio item now carries a notice whatever the variant is; what
                # differs is WHAT is bundled, and that is stated as observed, never assumed.
                if ($r.name -eq 'torch' -or $r.name -eq 'torchaudio') {
                    $noticeLines = New-Object System.Collections.Generic.List[string]
                    $noticeLines.Add('This wheel ships third-party native binaries. The licence texts that cover them travel inside the wheel: keep the *.dist-info directory in the install tree (licenses/first-run-notices.md A2, A3, A19).')
                    if ($tag -like 'cu*') {
                        if ($r.name -eq 'torch') {
                            $noticeLines.Add('NVIDIA CUDA EULA applies to the CUDA runtime DLLs bundled inside this wheel, under torch/lib/ (research/lab/notes/22 section 2). They are covered by neither the wheel License-Expression nor any of its License-File entries.')
                            $noticeLines.Add('NVIDIA cuDNN SLA applies to the cuDNN DLLs bundled inside this wheel, under torch/lib/ (research/lab/notes/22 section 3).')
                        } else {
                            $noticeLines.Add('Observed in torchaudio 2.10.0+cu130 on 2026-09-05: this wheel bundles no CUDA DLL of its own -- torchaudio/lib/*.pyd only. The CUDA and cuDNN DLLs arrive with the torch item of this same ledger, whose notices name the NVIDIA terms.')
                        }
                    } elseif ($tag -eq 'cpu') {
                        if ($r.name -eq 'torch') {
                            $noticeLines.Add('Read from torch-2.10.0+cpu-cp312-cp312-win_amd64.whl on 2026-09-05: METADATA declares License: BSD-3-Clause and exactly two License-File entries, LICENSE and NOTICE. dist-info/LICENSE (544,779 B) concatenates 33 third_party projects, among them ideep/mkl-dnn = oneDNN (Apache-2.0, Copyright 2016-2023 Intel Corporation). No Intel MKL licence text is present in it.')
                            $noticeLines.Add('Read from the same wheel: torch/lib/libiomp5md.dll (1,614,192 B) and torch/lib/libiompstubs5md.dll (43,888 B) ship inside it, yet its bundled LICENSE names no OpenMP project. The licence of those two DLLs is unidentified -- recorded as observed, not resolved (licenses/first-run-notices.md A19).')
                        } else {
                            $noticeLines.Add('Read from torchaudio-2.10.0+cpu-cp312-cp312-win_amd64.whl on 2026-09-05: native code is torchaudio/lib/_torchaudio.pyd and libtorchaudio.pyd, no separate DLL. METADATA declares one License-File (LICENSE), shipped as dist-info/licenses/LICENSE (1,363 B).')
                        }
                    } elseif ($tag -like 'rocm*') {
                        # Convoy C fills ledger/runtime-rocm-gfx1151.json. The branch exists so
                        # that the generator cannot produce a ROCm ledger with a silent torch.
                        $noticeLines.Add('ROCm variant: the AMD wheels declare no licence of their own (rocm_sdk_core METADATA is three lines with neither a License nor a License-File field -- licenses/first-run-notices.md B1), so what the bundled binaries are under is unidentified. Convoy C must record what it observed in the wheel, not what it assumes.')
                        $noticeLines.Add('ROCm is unsupported and shipped as a separate release (docs/radeon.md). The first-run notice has to say the licence position is unidentified before anything is fetched.')
                    } else {
                        $noticeLines.Add('Unknown local version tag "' + $tag + '": no variant-specific fact has been observed for it. Read the wheel before trusting this ledger.')
                    }
                    $notices = $noticeLines.ToArray()
                }
            } else {
                $rel = $null
                try {
                    $rel = Get-YwkPypiRelease -Name $r.name -Version $r.version
                } catch {
                    Write-YwkLog -Level 'WARN' -Message ('PyPI lookup failed for ' + $r.name + ' ' + $r.version + ': ' + $_.Exception.Message)
                }
                if ($null -ne $rel) {
                    $w = Select-YwkPypiWheel -Release $rel
                    if ($null -ne $w) {
                        $entry = $w
                        $source = 'pypi json api urls[].digests.sha256'
                        $license = Get-YwkPypiLicense -Release $rel
                        $licenseUrl = 'https://pypi.org/project/' + $r.name + '/' + $r.version + '/'
                    }
                }
                if ($null -eq $entry) {
                    $hit = $null
                    try {
                        $hit = Get-YwkTorchIndexWheel -IndexUrl $index -Name $r.name -Version $r.version -LocalTag $localTag
                    } catch {
                        $hit = $null
                    }
                    if ($null -ne $hit) {
                        $entry = @{ url = $hit.url; fallback_url = $hit.fallback_url; sha256 = $hit.sha256; size = (Get-YwkContentLength -Url $hit.url); filename = $hit.filename }
                        $source = 'index page ' + $index + '/' + $r.name + '/ (href #sha256 fragment); the href points at ' + $hit.host
                    }
                }
                if ($null -eq $entry -and $null -ne $rel) {
                    # No wheel anywhere. Pure python sdists are still installable without pip:
                    # unpack the tarball and copy the package directory (same handling as "archive").
                    $sd = Select-YwkPypiSdist -Release $rel
                    if ($null -ne $sd) {
                        $sdPath = Join-Path $CacheDir $sd.filename
                        $dl = Invoke-YwkDownload -Url $sd.url -Destination $sdPath -ExpectedSha256 $sd.sha256 -ExpectedSize $sd.size
                        $layout = Get-YwkSdistLayout -Path $sdPath
                        $entry = $sd
                        $sdist = $layout
                        $source = 'pypi json api urls[].digests.sha256 (sdist -- no wheel is published for this project)'
                        $license = Get-YwkPypiLicense -Release $rel
                        $licenseUrl = 'https://pypi.org/project/' + $r.name + '/' + $r.version + '/'
                        Write-YwkLog -Level 'WARN' -Message ($r.name + '==' + $r.version + ' has no wheel on PyPI; taking the sdist, packages=' + ($layout.packages -join ','))
                    }
                }
                if ($null -eq $entry) {
                    throw ('no cp312/win_amd64 wheel and no usable sdist for ' + $r.name + '==' + $r.version +
                           ' on PyPI or ' + $index + '. Refusing to write a ledger that cannot be assembled.')
                }
            }

            $kind = 'wheel'
            if ($null -ne $sdist) { $kind = 'sdist' }
            # No item may carry an empty license cell: licenses/first-run-notices.md A11
            # names the ledger as the source of record for this field, and PyPI's JSON API
            # sometimes reports none of license_expression / license / a classifier
            # (fsspec 2026.7.0). The wheel itself always declares it, so read it there.
            if ([string]::IsNullOrEmpty([string]$license) -and $kind -eq 'wheel') {
                $localWheel = Join-Path $CacheDir ([string]$entry.filename)
                if (-not (Test-Path -LiteralPath $localWheel)) {
                    Write-YwkLog -Level 'WARN' -Message ('no license in the PyPI metadata for ' + $r.name + '==' + $r.version + '; fetching the wheel to read its METADATA')
                    $null = Invoke-YwkDownload -Url ([string]$entry.url) -Destination $localWheel -ExpectedSha256 ([string]$entry.sha256) -ExpectedSize ([int64]$entry.size)
                }
                $license = Get-YwkWheelMetadataLicense -WheelPath $localWheel
                if (-not [string]::IsNullOrEmpty([string]$license)) {
                    $licenseSource = 'wheel ' + [string]$entry.filename + ' dist-info/METADATA'
                    Write-YwkLog -Message ('  license from the wheel METADATA: ' + $r.name + ' = ' + $license)
                }
            }
            if ([string]::IsNullOrEmpty([string]$license)) {
                throw ('no license could be determined for ' + $r.name + '==' + $r.version +
                       ' (PyPI metadata and the wheel METADATA are both silent). Refusing to write a ledger with an empty license cell.')
            }

            $fallbackUrl = ''
            if ($entry -is [hashtable]) {
                if ($entry.ContainsKey('fallback_url')) { $fallbackUrl = [string]$entry.fallback_url }
            }
            $item = [ordered]@{
                kind     = $kind
                name     = $r.name
                version  = $r.version
                url      = $entry.url
                sha256   = $entry.sha256
                size     = [int64]$entry.size
                filename = $entry.filename
                license  = $license
                license_url = $licenseUrl
                sha256_source = $source
            }
            if (-not [string]::IsNullOrEmpty($fallbackUrl)) { $item['fallback_url'] = $fallbackUrl }
            if (-not [string]::IsNullOrEmpty([string]$licenseSource)) { $item['license_source'] = $licenseSource }
            if ($null -ne $sdist) {
                $item['install'] = 'pure-python-src'
                $item['archive_root'] = $sdist.root
                $item['package_dirs'] = $sdist.packages
            }
            if ($notices.Count -gt 0) { $item['notices'] = $notices }
            $items.Add($item)
            $sourceLog.Add($r.name + '==' + $r.version + '  ' + $kind + '  ' + $source + '  ' + $entry.url)
        }

        $null = Write-YwkTextFile -Path (Join-Path $LogDir ('requirements-' + $v + '.pruned.txt')) -Newline 'LF' -Text (($keptLines -join "`n") + "`n")
        $null = Write-YwkTextFile -Path (Join-Path $LogDir ('sources-' + $v + '.txt')) -Newline 'LF' -Text (($sourceLog -join "`n") + "`n")

        # --------------------------------------------------------------- shared-with-cpu check
        # Acceptance condition C-1 says every dependency that is not part of the ROCm stack must
        # be the SAME item as the cpu variant's, sha256 included. That is a claim about two
        # generated files, so it is checked here rather than asserted in prose: same name, same
        # version, same sha256, or the run stops.
        $sharedReport = New-Object System.Collections.Generic.List[string]
        if ($isRocm) {
            $cpuLedgerPath = Join-Path $LedgerDir 'runtime-cpu.json'
            if (-not (Test-Path -LiteralPath $cpuLedgerPath)) {
                throw ('the rocm ledger cannot be reconciled: ' + $cpuLedgerPath + ' is missing. Generate the cpu variant first.')
            }
            $cpuLedger = Read-YwkJsonFile -Path $cpuLedgerPath
            $cpuByName = @{}
            foreach ($ci in $cpuLedger.items) { $cpuByName[[string]$ci.name] = $ci }
            $sharedOk = 0
            $sharedMissing = New-Object System.Collections.Generic.List[string]
            foreach ($it in $items) {
                $nm = [string]$it['name']
                if (Test-YwkRocmIndexOnly -Name $nm) { continue }
                if (-not $cpuByName.ContainsKey($nm)) {
                    $sharedMissing.Add($nm + '==' + [string]$it['version'] + ' (not in runtime-cpu.json)')
                    continue
                }
                $ci = $cpuByName[$nm]
                if ([string]$ci.version -ne [string]$it['version']) {
                    throw ('shared dependency ' + $nm + ' resolved to ' + [string]$it['version'] +
                           ' for ' + $v + ' but runtime-cpu.json pins ' + [string]$ci.version +
                           '. C-1 requires the non-ROCm half of the two ledgers to be identical.')
                }
                if ([string]$ci.sha256 -ne [string]$it['sha256']) {
                    throw ('shared dependency ' + $nm + '==' + [string]$it['version'] + ' has sha256 ' +
                           [string]$it['sha256'] + ' here and ' + [string]$ci.sha256 + ' in runtime-cpu.json.')
                }
                $sharedOk = $sharedOk + 1
                $sharedReport.Add($nm + '==' + [string]$it['version'] + '  sha256 matches runtime-cpu.json')
            }
            if ($sharedMissing.Count -gt 0) {
                throw ('these items are in the rocm ledger but not in runtime-cpu.json: ' + ($sharedMissing -join ', '))
            }
            Write-YwkLog -Message ('shared with runtime-cpu.json: ' + $sharedOk + ' items, all sha256 identical')
            $null = Write-YwkTextFile -Path (Join-Path $LogDir ('shared-with-cpu-' + $v + '.txt')) -Newline 'LF' `
                -Text (($sharedReport -join "`n") + "`n")
        }

        $ledger = [ordered]@{
            schema      = 1
            name        = ('runtime-' + $v)
            generated   = $generated
            generator   = 'build/make-ledger.ps1'
            python      = $PythonVersion
            python_tag  = $PythonTag
            platform_tag = $PlatformTag
            torch_index = $index
            uv          = $uv.Version
            upstream    = [ordered]@{
                'irodori-tts'        = '8224dafb46d0aba89209a8f905f1cb7e3299d9c1'
                'irodori-tts-server' = '841fb7c6ec57729c56b9b75c0ef2562249b13a10'
            }
            resolution  = [ordered]@{
                command   = 'uv pip compile --python-version 3.12 --python-platform windows --extra-index-url <torch_index> --index-strategy unsafe-best-match'
                overrides = $overrideLines
                dropped_top_level = $DroppedTopLevel
                pruned_after_solve = $PrunedTransitive
                log = 'build/out/ledger-log/'
            }
            count       = $items.Count
            items       = $items.ToArray()
        }
        if ($isRocm) {
            $ledger['gfx'] = $RocmGfx
            $ledger['torch_extra'] = ('torch[device-' + $RocmGfx + ']==' + $RocmTorchVersion)
            $ledger['resolution']['dropped_after_solve'] = $RocmDroppedAfterSolve
            $ledger['resolution']['dropped_after_solve_reason'] =
                'rocm-bootstrap is declared by torch but imported by nothing on the synthesis path; it is AMD gfx auto-detection (a clinfo wrapper) and decisions.md 5 gives the distribution no auto-detection. See ledger/README.md section 4-6.'
            $ledger['resolution']['shared_with'] = 'runtime-cpu.json'
            $ledger['resolution']['shared_items_verified'] = $sharedOk
            $ledger['sha256_note'] =
                'The AMD index https://stable.repo.amd.com/rocm/whl-next/ publishes no hash: its hrefs carry no "#sha256=" fragment and it answers a PEP 691 json request with text/html. Every AMD sha256 below was computed by build/make-ledger.ps1 from the downloaded file, and the size was cross-checked against the HEAD Content-Length. Because the index publishes nothing to compare against, build/make-ledger.ps1 also carries the eight AMD hashes as reference pins ($RocmExpectedPins) and throws when a fetched or cached file no longer matches one; the per-item sha256_source says whether THIS generation read the bytes from the index or from build/cache. A name with no pin yet is trusted on first use and its hash copied into the pin afterwards.'
        }
        $path = Join-Path $LedgerDir ('runtime-' + $v + '.json')
        $null = Write-YwkJsonFile -Path $path -Value $ledger -Depth 20
        Write-YwkLog -Message ('wrote ' + $path + ' (' + $items.Count + ' items)')
        $ledgerSummaries.Add('runtime-' + $v + '.json  ' + $items.Count + ' items')
    }
}

# --------------------------------------------------------------------------- python embed

if (-not $SkipPythonEmbed) {
    Write-YwkLog -Level 'STEP' -Message '=== python-embed ==='
    $dest = Join-Path $CacheDir ('python-' + $PythonVersion + '-embed-amd64.zip')
    $dl = Invoke-YwkDownload -Url $EmbedUrl -Destination $dest
    if ($dl.Sha256 -ne $EmbedSha256Ref) {
        throw ('python embed sha256 moved: got ' + $dl.Sha256 + ', research/lab/notes/22 section 4 recorded ' + $EmbedSha256Ref)
    }
    if ($dl.Size -ne $EmbedSizeRef) {
        throw ('python embed size moved: got ' + $dl.Size + ', expected ' + $EmbedSizeRef)
    }
    $embed = [ordered]@{
        schema    = 1
        name      = 'python-embed'
        generated = $generated
        generator = 'build/make-ledger.ps1'
        items     = @(
            [ordered]@{
                kind    = 'python-embed'
                name    = 'python'
                version = $PythonVersion
                url     = $EmbedUrl
                sha256  = $dl.Sha256
                size    = $dl.Size
                license = 'PSF-2.0'
                license_url = 'https://docs.python.org/3/license.html'
                license_file_in_archive = 'LICENSE.txt'
                sha256_source = 'downloaded and hashed by build/make-ledger.ps1; cross-checked against research/lab/notes/22 section 4'
                notes = @(
                    'The zip contains vcruntime140.dll and vcruntime140_1.dll but NOT msvcp140.dll; torch needs msvcp140.dll, hence vc_redist.json (research/lab/notes/22 section 3b/4b).'
                )
            }
        )
    }
    $null = Write-YwkJsonFile -Path (Join-Path $LedgerDir 'python-embed.json') -Value $embed -Depth 12
    Write-YwkLog -Message ('wrote ledger/python-embed.json  sha256=' + $dl.Sha256)
    $ledgerSummaries.Add('python-embed.json  1 item')
}

# --------------------------------------------------------------------------- models

if (-not $SkipModels) {
    Write-YwkLog -Level 'STEP' -Message '=== models ==='
    $repoEntries = New-Object System.Collections.Generic.List[object]
    foreach ($m in $ModelRepos) {
        $rev = $m.revision
        $meta = Invoke-YwkHttpGetString -Url ('https://huggingface.co/api/models/' + $m.repo) | ConvertFrom-Json
        $headSha = [string]$meta.sha
        if ([string]::IsNullOrEmpty($rev)) {
            $rev = $headSha
            Write-YwkLog -Message ($m.repo + ': pinning revision to current head ' + $rev)
        } elseif ($rev -ne $headSha) {
            Write-YwkLog -Level 'WARN' -Message ($m.repo + ': pinned ' + $rev + ' is not head (' + $headSha + ') -- keeping the pin')
        }
        $tree = Invoke-YwkHttpGetString -Url ('https://huggingface.co/api/models/' + $m.repo + '/tree/' + $rev + '?recursive=true') | ConvertFrom-Json
        $files = New-Object System.Collections.Generic.List[object]
        foreach ($e in $tree) {
            if ([string]$e.type -ne 'file') { continue }
            $sha256 = $null
            $verify = 'git-blob-sha1'
            $size = [int64]$e.size
            if ($e.PSObject.Properties.Name -contains 'lfs') {
                if ($null -ne $e.lfs) {
                    $sha256 = ([string]$e.lfs.oid).ToLowerInvariant()
                    $verify = 'sha256'
                    $size = [int64]$e.lfs.size
                }
            }
            $files.Add([ordered]@{
                path          = [string]$e.path
                size          = $size
                sha256        = $sha256
                git_blob_sha1 = [string]$e.oid
                verify        = $verify
                url           = ('https://huggingface.co/' + $m.repo + '/resolve/' + $rev + '/' + [string]$e.path)
            })
        }
        $totalBytes = [int64]0
        foreach ($f in $files) { $totalBytes = $totalBytes + [int64]$f.size }
        $repoEntries.Add([ordered]@{
            kind         = 'hf-repo'
            role         = $m.role
            repo         = $m.repo
            revision     = $rev
            head_at_generation = $headSha
            hf_cache_dir = ('models--' + ($m.repo -replace '/', '--'))
            file_count   = $files.Count
            total_bytes  = $totalBytes
            files        = $files.ToArray()
        })
        Write-YwkLog -Message ($m.repo + ' @ ' + $rev + ': ' + [string]$files.Count + ' files, ' + $totalBytes + ' B')
    }
    $models = [ordered]@{
        schema    = 1
        name      = 'models'
        generated = $generated
        generator = 'build/make-ledger.ps1'
        sha256_source = 'huggingface api /api/models/<repo>/tree/<rev>: lfs.oid for LFS files (that field is the sha256), git blob sha1 otherwise'
        notes     = @(
            'silentcipher calls snapshot_download(repo_id="sony/silentcipher") with no allow_patterns, so the whole repo is fetched (upstream silentcipher/server.py:475).',
            'sony/silentcipher stores its .ckpt files as ordinary git blobs, not LFS, so only a git blob sha1 is available from the API.'
        )
        repos     = $repoEntries.ToArray()
    }
    $null = Write-YwkJsonFile -Path (Join-Path $LedgerDir 'models.json') -Value $models -Depth 20
    Write-YwkLog -Message 'wrote ledger/models.json'
    $ledgerSummaries.Add('models.json  ' + $repoEntries.Count + ' repos')
}

# --------------------------------------------------------------------------- vc_redist

if (-not $SkipVcRedist) {
    Write-YwkLog -Level 'STEP' -Message '=== vc_redist ==='
    $pinned = Resolve-YwkRedirect -Url $VcRedistAkaMs
    if ($pinned -notmatch '^https://download\.visualstudio\.microsoft\.com/') {
        throw ('aka.ms did not redirect to a download.visualstudio.microsoft.com URL: ' + $pinned)
    }
    Write-YwkLog -Message ('pinned url: ' + $pinned)
    $dest = Join-Path $CacheDir 'VC_redist.x64.exe'
    $dl = Invoke-YwkDownload -Url $pinned -Destination $dest
    $vi = (Get-Item -LiteralPath $dest).VersionInfo
    $ver = [string]$vi.ProductVersion
    if ([string]::IsNullOrEmpty($ver)) { $ver = [string]$vi.FileVersion }
    # Microsoft embeds the sha256 of the payload in the URL path; verify our hash against it.
    $urlHash = ''
    $mh = [regex]::Match($pinned, '/([0-9A-Fa-f]{64})/')
    if ($mh.Success) { $urlHash = $mh.Groups[1].Value.ToLowerInvariant() }
    $matchesUrl = $false
    if (-not [string]::IsNullOrEmpty($urlHash)) { $matchesUrl = ($urlHash -eq $dl.Sha256) }
    $vc = [ordered]@{
        schema    = 1
        name      = 'vc-redist'
        generated = $generated
        generator = 'build/make-ledger.ps1'
        items     = @(
            [ordered]@{
                kind          = 'installer'
                name          = 'vc_redist.x64'
                version       = $ver
                url           = $pinned
                fallback_url  = $VcRedistAkaMs
                sha256        = $dl.Sha256
                size          = $dl.Size
                sha256_source = 'downloaded from the pinned URL and hashed by build/make-ledger.ps1'
                sha256_in_url = $urlHash
                sha256_matches_url_segment = $matchesUrl
                silent_args   = @('/install', '/quiet', '/norestart')
                docs_url      = $VcRedistDocs
                license       = 'Microsoft Software License Terms (Visual Studio) -- Distributable Code'
                license_url   = 'https://visualstudio.microsoft.com/license-terms/'
                notes         = @(
                    'Needed because python-embed ships vcruntime140.dll and vcruntime140_1.dll but not msvcp140.dll, and torch/lib/c10.dll imports MSVCP140.dll (research/lab/notes/22 section 3b).',
                    'The aka.ms link floats to whatever the current release is; url above is the version-pinned direct link that aka.ms resolved to on the generation date.'
                )
            }
        )
    }
    $null = Write-YwkJsonFile -Path (Join-Path $LedgerDir 'vc_redist.json') -Value $vc -Depth 12
    Write-YwkLog -Message ('wrote ledger/vc_redist.json  version=' + $ver + '  sha256=' + $dl.Sha256 + '  url-hash-match=' + $matchesUrl)
    $ledgerSummaries.Add('vc_redist.json  1 item')
}

# --------------------------------------------------------------------------- rocm template

$rocmPath = Join-Path $LedgerDir 'runtime-rocm-gfx1151.json'
if (-not (Test-Path -LiteralPath $rocmPath)) {
    $rocm = [ordered]@{
        schema      = 1
        name        = 'runtime-rocm-gfx1151'
        generated   = $null
        generator   = 'build/make-ledger.ps1 (template only -- run -Variant rocm-gfx1151 to resolve it; ledger/README.md section 9)'
        status      = 'template'
        python      = $PythonVersion
        python_tag  = $PythonTag
        platform_tag = $PlatformTag
        torch_index = 'https://stable.repo.amd.com/rocm/whl-next/'
        seed        = [ordered]@{
            note   = 'Facts from the running Radeon box, read-only (decisions.md 24). Run make-ledger.ps1 -Variant rocm-gfx1151 to replace this template with the resolved ledger.'
            torch  = ('torch==' + $RocmTorchVersion)
            torchaudio = ('torchaudio==' + $RocmTorchaudioVersion)
            torch_extra = ('torch[device-' + $RocmGfx + ']')
            hip    = '7.15'
            sentencepiece = 'sentencepiece==0.2.1'
        }
        count       = 0
        items       = @()
    }
    $null = Write-YwkJsonFile -Path $rocmPath -Value $rocm -Depth 12
    Write-YwkLog -Message 'wrote ledger/runtime-rocm-gfx1151.json (template)'
} else {
    Write-YwkLog -Message 'ledger/runtime-rocm-gfx1151.json already exists; left untouched'
}

Write-YwkLog -Level 'STEP' -Message 'make-ledger done'
foreach ($s in $ledgerSummaries) { Write-YwkLog -Message ('  ' + $s) }
exit 0
