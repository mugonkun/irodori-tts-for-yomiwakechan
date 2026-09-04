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

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\make-ledger.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cu130', 'cu126')]
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
}
$LocalTagOf = @{ 'cpu' = 'cpu'; 'cu130' = 'cu130'; 'cu126' = 'cu126' }

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
        [Parameter(Mandatory = $true)][string]$OutPath
    )
    $index = $TorchIndexOf[$VariantName]
    $uvArgs = @(
        'pip', 'compile', $reqInPath,
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

        $compiled = Invoke-YwkCompile -VariantName $v -OutPath (Join-Path $LogDir ('requirements-' + $v + '.txt'))
        $rows = Read-YwkCompiled -Path $compiled
        $removed = Get-YwkPruneSet -Rows $rows
        Write-YwkLog -Message ('resolved ' + $rows.Count + ' packages; pruning ' + $removed.Count)

        $pruneReport = New-Object System.Collections.Generic.List[string]
        $pruneReport.Add('# packages removed from the resolved set for variant ' + $v)
        $pruneReport.Add('# roots: ' + ($PrunedTransitive -join ', ') + ' -- everything else here became an orphan.')
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

            if ($r.version -match '\+') {
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
        generator   = 'build/make-ledger.ps1 (template only -- not yet resolved)'
        status      = 'template'
        python      = $PythonVersion
        python_tag  = $PythonTag
        platform_tag = $PlatformTag
        torch_index = 'https://stable.repo.amd.com/rocm/whl-next/'
        seed        = [ordered]@{
            note   = 'Facts from the running Radeon box, read-only (decisions.md 24). Convoy C fills this in.'
            torch  = 'torch==2.13.0+rocm10.0.0'
            torchaudio = 'torchaudio==2.11.0.2+rocm10.0.0'
            torch_extra = 'torch[device-gfx1151]'
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
