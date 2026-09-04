# release-build.ps1 -- the launcher production line (convoy D).
#
# ASCII only. CRLF. Must parse and run on Windows PowerShell 5.1 and PowerShell 7+.
# No ternary operator, no null-coalescing, no '&&' inside this file.
#
# Stages (the shape follows the host project's tools/release-build.ps1):
#   0  read the version from the ONE place that defines it (launcher/Directory.Build.props)
#   0b read the upstream pins from the ledgers (they are the canon -- the csproj holds no copy)
#   0c read the .NET runtime version that will actually be baked in, and check it against
#      licenses/dotnet/README.md (a licence text older than the artefact must not ship)
#   1  dotnet publish (self contained single file, decision 51), into a directory made from scratch
#   2  machine inspection of the output -- every item is a hard condition
#   3  stash the pdb out of the artefact
#   4  zip, stamped with the version
#
# A failed inspection exits non-zero and writes no zip. Nothing here is best effort.
#
# Usage:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File build/release-build.ps1
#   pwsh ... -File build/release-build.ps1 -SkipZip        (inspect only, leave no archive)

[CmdletBinding()]
param(
    [switch]$SkipZip,
    [switch]$KeepPublishDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Common.ps1')

$repo       = Get-YwkRepoRoot
$project    = Join-Path $repo 'launcher\IrodoriTtsYwk.Launcher\IrodoriTtsYwk.Launcher.csproj'
$props      = Join-Path $repo 'launcher\Directory.Build.props'
$publishDir = Join-Path $repo 'build\out\launcher\win-x64'
$objDir     = Join-Path $repo 'launcher\IrodoriTtsYwk.Launcher\obj\Release\net10.0-windows\win-x64'
$outRoot    = Join-Path $repo 'build\out\launcher'
$licenseDoc = Join-Path $repo 'licenses\dotnet\README.md'
$exeName    = 'IrodoriTtsYwk.Launcher.exe'

$null = New-YwkDirectory -Path $outRoot
$null = Start-YwkLog -Path (Join-Path $repo ('build\out\release-log\release-build-' +
    (Get-Date).ToString('yyyyMMdd-HHmmss') + '.log'))

$failures = New-Object System.Collections.Generic.List[string]
function Add-Failure([string]$Message) {
    $failures.Add($Message)
    Write-YwkLog $Message -Level FAIL
}

# ---------------------------------------------------------------- 0. version

Write-YwkLog 'STEP 0: version, upstream pins and runtime version' -Level STEP

$versionHits = @(Select-String -Path $props -Pattern '<AppDisplayVersion>(.+)</AppDisplayVersion>')
if ($versionHits.Count -ne 1) {
    throw ('launcher/Directory.Build.props has ' + $versionHits.Count +
        ' AppDisplayVersion tags (expected exactly 1 -- the version has one definition).')
}
$appVersion = $versionHits[0].Matches[0].Groups[1].Value
Write-YwkLog ('version = ' + $appVersion + ' (read from Directory.Build.props)')

# ---------------------------------------------------------------- 0b. upstream pins

# The pins live in the ledgers (decision 3). The csproj deliberately holds no copy: release-build
# reads them here and passes them to publish, so there is exactly one place to change them.
$pins = @{}
foreach ($ledger in @(Get-ChildItem (Join-Path $repo 'ledger') -Filter 'runtime-*.json' -File)) {
    $data = Get-Content -LiteralPath $ledger.FullName -Raw | ConvertFrom-Json
    if (-not ($data.PSObject.Properties.Name -contains 'upstream')) { continue }
    foreach ($name in @('irodori-tts', 'irodori-tts-server')) {
        $value = [string]$data.upstream.$name
        if ([string]::IsNullOrWhiteSpace($value)) {
            Add-Failure ('ledger/' + $ledger.Name + ' has no upstream.' + $name + '.')
            continue
        }
        if (-not $pins.ContainsKey($name)) {
            $pins[$name] = @{}
        }
        $pins[$name][$value] = $ledger.Name
    }
}
foreach ($name in @('irodori-tts', 'irodori-tts-server')) {
    if (-not $pins.ContainsKey($name)) {
        Add-Failure ('no ledger declares upstream.' + $name + '.')
    } elseif ($pins[$name].Keys.Count -ne 1) {
        Add-Failure ('the ledgers disagree about upstream.' + $name + ': ' +
            (($pins[$name].Keys) -join ', '))
    }
}
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host ('NG: ' + $f) }
    throw 'the upstream pins are not consistent; nothing was published.'
}
$pinTts       = @($pins['irodori-tts'].Keys)[0]
$pinTtsServer = @($pins['irodori-tts-server'].Keys)[0]
$shortTts       = $pinTts.Substring(0, 7)
$shortTtsServer = $pinTtsServer.Substring(0, 7)
Write-YwkLog ('upstream Irodori-TTS        = ' + $pinTts)
Write-YwkLog ('upstream Irodori-TTS-Server = ' + $pinTtsServer)

# ---------------------------------------------------------------- 1. publish

Write-YwkLog 'STEP 1: dotnet publish (self contained, single file)' -Level STEP

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

$publishArgs = @(
    'publish', $project,
    '-p:PublishProfile=win-x64',
    ('-p:UpstreamIrodoriTts=' + $shortTts),
    ('-p:UpstreamIrodoriTtsServer=' + $shortTtsServer),
    '--nologo'
)
Write-YwkLog ('dotnet ' + ($publishArgs -join ' '))
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw ('dotnet publish failed with exit code ' + $LASTEXITCODE + '.')
}
if (-not (Test-Path -LiteralPath $publishDir)) {
    throw ('the publish directory is missing: ' + $publishDir)
}

# ---------------------------------------------------------------- 0c. runtime version vs licences
#
# Deliberately after publish: obj\...\*.deps.json names the runtime pack that was actually baked in,
# which is what the licence texts have to match. Before publish it may be stale.

Write-YwkLog 'STEP 2a: the baked .NET runtime version against licenses/dotnet/README.md' -Level STEP

$depsPath = Join-Path $objDir 'IrodoriTtsYwk.Launcher.deps.json'
if (-not (Test-Path -LiteralPath $depsPath)) {
    Add-Failure ('(a) the intermediate deps.json is missing (cannot read the runtime version): ' + $depsPath)
} else {
    $depsText = Get-Content -LiteralPath $depsPath -Raw
    $licenseText = ''
    if (Test-Path -LiteralPath $licenseDoc) {
        $licenseText = Get-Content -LiteralPath $licenseDoc -Raw
    } else {
        Add-Failure ('(a) licenses/dotnet/README.md is missing (decision 51 has no record).')
    }
    foreach ($pack in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $m = [regex]::Match($depsText, 'runtimepack\.' + [regex]::Escape($pack) + '/([0-9][0-9A-Za-z\.\-]*)"')
        if (-not $m.Success) {
            Add-Failure ('(a) ' + $pack + ' is not named in deps.json (is this really a self contained publish?).')
            continue
        }
        $baked = $m.Groups[1].Value
        Write-YwkLog ($pack + ' = ' + $baked)
        if (-not [string]::IsNullOrEmpty($licenseText)) {
            # The licence table must name this exact version, otherwise the texts we ship are older
            # (or newer) than the runtime we bake in.
            $needle = '`' + $pack + '`'
            $row = @($licenseText -split "`r?`n" | Where-Object { $_.Contains($pack) })
            $named = @($row | Where-Object { $_.Contains($baked) })
            if ($named.Count -eq 0) {
                Add-Failure ('(a) licenses/dotnet/README.md does not record ' + $pack + ' ' + $baked +
                    ' (update the table and the licence texts, then publish again).')
            }
        }
    }
}

# ---------------------------------------------------------------- 2. inspection

Write-YwkLog 'STEP 2b: machine inspection of the published tree' -Level STEP

$exePath = Join-Path $publishDir $exeName
if (-not (Test-Path -LiteralPath $exePath)) {
    Add-Failure ('(b) ' + $exeName + ' is missing.')
}

# (c) single file: nothing but the exe and its pdb may be produced. A loose dll means the single
#     file bundle broke, and a loose anything else means something got copied in by accident.
$produced = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File)
$unexpected = @($produced | Where-Object { ($_.Name -ne $exeName) -and ($_.Extension -ne '.pdb') })
foreach ($file in $unexpected) {
    Add-Failure ('(c) unexpected file in the artefact: ' + $file.FullName)
}
Write-YwkLog ('produced ' + $produced.Count + ' file(s): ' + (($produced | ForEach-Object { $_.Name }) -join ', '))

# (d) no third party binary and no user data (decision 8 and 22). These are the shapes that would
#     mean the fetch ledger had been bypassed and something was shipped instead.
$forbidden = @(
    '*.whl', '*.safetensors', '*.pt', '*.pth', '*.onnx', '*.bin',
    'python*.exe', 'vc_redist*', '*.zip', '*.tar.gz', '*.msi',
    'settings.json', 'voices.json', '*.wav', '*.log', '*.tmp', '*.user'
)
foreach ($pattern in $forbidden) {
    foreach ($hit in @(Get-ChildItem -LiteralPath $publishDir -Recurse -File -Filter $pattern)) {
        Add-Failure ('(d) third party or user data in the artefact: ' + $hit.FullName)
    }
}

# (e) the upstream pins really are baked in. With PublishSingleFile the managed assembly is
#     embedded (and compressed) inside the exe, so it cannot be read from the artefact; the exact
#     assembly that got embedded is the intermediate one, and that is what we read here.
$objDll = Join-Path $objDir 'IrodoriTtsYwk.Launcher.dll'
if (-not (Test-Path -LiteralPath $objDll)) {
    Add-Failure ('(e) the intermediate assembly is missing (cannot verify the upstream pins): ' + $objDll)
} else {
    # Custom attribute blobs store their strings as UTF-8, so a byte scan is enough and needs no
    # package (MetadataLoadContext is not guaranteed to be in the nuget cache of a fresh machine).
    $bytes = [System.IO.File]::ReadAllBytes($objDll)
    $flat = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
    foreach ($pair in @(
        @{ key = 'UpstreamIrodoriTts'; value = $shortTts },
        @{ key = 'UpstreamIrodoriTtsServer'; value = $shortTtsServer })) {
        if (-not $flat.Contains($pair.key)) {
            Add-Failure ('(e) AssemblyMetadata ' + $pair.key + ' was not baked in.')
        }
        if (-not $flat.Contains($pair.value)) {
            Add-Failure ('(e) the upstream pin ' + $pair.value + ' was not baked in.')
        }
    }
    if ($flat.Contains($appVersion)) {
        Write-YwkLog ('(e) the assembly carries ' + $appVersion + ' and both upstream pins')
    } else {
        Add-Failure ('(e) the assembly does not carry the display version ' + $appVersion + '.')
    }
}

# (f) size. A self contained WPF single file is about 66 MiB (measured 2026-09-05). Far outside
#     that range means the profile changed under us (trimmed, framework dependent, or bloated).
$exeBytes = 0
if (Test-Path -LiteralPath $exePath) {
    $exeBytes = (Get-Item -LiteralPath $exePath).Length
    $exeMiB = [math]::Round($exeBytes / 1MB, 1)
    Write-YwkLog ('(f) ' + $exeName + ' = ' + $exeBytes + ' B (' + $exeMiB + ' MiB)')
    if (($exeBytes -lt 40MB) -or ($exeBytes -gt 200MB)) {
        Add-Failure ('(f) the exe is ' + $exeMiB + ' MiB, outside the expected 40-200 MiB for a self contained WPF single file.')
    }
}

if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host ('NG: ' + $f) -ForegroundColor Red }
    Write-YwkLog ('inspection failed with ' + $failures.Count + ' finding(s); no archive was written.') -Level FAIL
    exit $failures.Count
}
Write-YwkLog 'inspection passed: (b) exe / (c) single file / (d) no third party or user data / (e) pins baked / (f) size' -Level INFO

# ---------------------------------------------------------------- 3. pdb stash

Write-YwkLog 'STEP 3: move the pdb out of the artefact' -Level STEP

$stamp = (Get-Date).ToString('yyyyMMdd-HHmm')
$pdbStash = Join-Path $outRoot ('pdb-' + $appVersion + '-' + $stamp)
$pdbFiles = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File -Filter '*.pdb')
if ($pdbFiles.Count -eq 0) {
    Write-YwkLog 'no pdb was produced (nothing to stash).' -Level WARN
} else {
    $null = New-YwkDirectory -Path $pdbStash
    foreach ($pdb in $pdbFiles) {
        Move-Item -LiteralPath $pdb.FullName -Destination (Join-Path $pdbStash $pdb.Name) -Force
    }
    Write-YwkLog ('stashed ' + $pdbFiles.Count + ' pdb -> ' + $pdbStash)
}
$leftover = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File -Filter '*.pdb')
if ($leftover.Count -gt 0) {
    Write-YwkLog 'a pdb is still in the artefact; no archive was written.' -Level FAIL
    exit 1
}

# ---------------------------------------------------------------- 4. archive

if ($SkipZip) {
    Write-YwkLog 'STEP 4 skipped (-SkipZip): the artefact is left in place.' -Level STEP
    Write-YwkLog ('artefact = ' + $exePath)
    exit 0
}

Write-YwkLog 'STEP 4: version stamped archive' -Level STEP

$zipPath = Join-Path $outRoot ('irodori-tts-ywk-launcher-' + $appVersion + '-' + $stamp + '.zip')
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
$zipBytes = (Get-Item -LiteralPath $zipPath).Length
Write-YwkLog ('archive = ' + $zipPath + ' (' + $zipBytes + ' B / ' +
    [math]::Round($zipBytes / 1MB, 1) + ' MiB)')

if (-not $KeepPublishDir) {
    Write-YwkLog ('the artefact stays at ' + $publishDir + ' (build/out is not tracked).')
}

Write-YwkLog ('DONE: ' + $appVersion + ' / upstream ' + $shortTts + '+' + $shortTtsServer +
    ' / exe ' + $exeBytes + ' B') -Level STEP
exit 0
