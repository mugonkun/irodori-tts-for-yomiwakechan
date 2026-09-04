<#
.SYNOPSIS
    Build build/out/app/ = the wrapper plus a patched copy of the two upstream trees.

.DESCRIPTION
    Acceptance condition A-2. The submodules under upstream/ are read-only: every patch is
    applied to the copy under build/out/app/server/upstream/, never to the submodule, and the
    run ends by asserting that "git status" in both submodules is still empty.

    Layout produced:
      build/out/app/
        server/                       <- server/*.py + python312._pth.template (the wrapper)
        server/upstream/Irodori-TTS/          <- copy of the 8224daf tree, patched
        server/upstream/Irodori-TTS-Server/   <- copy of the 841fb7c tree, patched
        ledger/  licenses/            <- copied whole, minus first-run-notices.md
        voices/voices.json            <- {"<default>": {"no_ref": true}} (decisions.md 16)
        voices/voices.ywk.json        <- empty distributor-side speaker table (convoy D fills it)

    licenses/first-run-notices.md is deliberately NOT copied: it says of itself that it is
    not part of the distributable (design doc section 1). It is what the first-run UI shows
    before any third-party byte is fetched, and convoy D carries that text.

    A non-zero exit leaves nothing behind: the trap at the top removes a half-built app/.

    Patches: every patches/*.patch is matched to the upstream copy it touches by reading its
    "+++ b/<path>" headers, checked with "git apply --check", then applied. A patch that does
    not apply cleanly stops the run.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\assemble-app.ps1
#>
[CmdletBinding()]
param(
    [string]$OutRoot = '',
    [switch]$SkipPatches
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

# Same house rule as assemble-runtime.ps1: a non-zero exit leaves no artefact.
# Set once build/out/app/ starts being filled; the trap removes it if anything
# below throws. Without it the postflight "upstream/ became dirty" throw -- the
# one case where the copy is known to be wrong -- would leave a tree that
# verify-runtime.ps1 and the installer would happily consume.
$script:YwkPartialAppDir = ''
trap {
    if (-not [string]::IsNullOrEmpty($script:YwkPartialAppDir)) {
        if (Test-Path -LiteralPath $script:YwkPartialAppDir) {
            Write-Host ('[assemble-app] removing the half-built ' + $script:YwkPartialAppDir)
            Remove-Item -LiteralPath $script:YwkPartialAppDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        $script:YwkPartialAppDir = ''
    }
    break
}

$RepoRoot = Get-YwkRepoRoot
if ([string]::IsNullOrEmpty($OutRoot)) { $OutRoot = Join-Path $RepoRoot 'build\out' }

$AppDir      = Join-Path $OutRoot 'app'
$AppServer   = Join-Path $AppDir 'server'
$AppUpstream = Join-Path $AppServer 'upstream'
$AppVoices   = Join-Path $AppDir 'voices'
$LogDir      = Join-Path $OutRoot 'assemble-log'

$null = New-YwkDirectory -Path $LogDir
$null = Start-YwkLog -Path (Join-Path $LogDir 'assemble-app.log')
Write-YwkLog -Level 'STEP' -Message 'assemble-app'

$git = Get-YwkGitPath

# Directory names that never ship. tests/docs/examples are dead weight in the distributable
# and "docs" in particular is the source we quote in ywk_params.py at design time only.
$ExcludeDirs = @('.git', '.github', 'tests', 'test', 'docs', 'examples', '__pycache__', '.ruff_cache', '.pytest_cache')
# first-run-notices.md says of itself, in its third line, that it is not part of
# the distributable (design doc section 1, licenses/README.md row 9): it is the
# notice the first-run UI shows *before* anything is fetched, and convoy D owns
# that text. Shipping the file made the licenses/ tree contradict itself.
$ExcludeFiles = @('.env', 'first-run-notices.md')

$Submodules = @(
    @{ name = 'Irodori-TTS';        commit = '8224dafb46d0aba89209a8f905f1cb7e3299d9c1' },
    @{ name = 'Irodori-TTS-Server'; commit = '841fb7c6ec57729c56b9b75c0ef2562249b13a10' }
)

# --------------------------------------------------------------------------- 0. preflight

$pre = Test-YwkUpstreamClean -RepoRoot $RepoRoot
foreach ($l in $pre.Report) { Write-YwkLog -Message ('  ' + $l) }
if (-not $pre.Ok) {
    throw 'upstream/ is not clean before the copy. Refusing to run; restore the submodules first.'
}

foreach ($s in $Submodules) {
    $path = Join-Path $RepoRoot ('upstream\' + $s.name)
    $r = Invoke-YwkNative -FilePath $git -Arguments @('-C', $path, 'rev-parse', 'HEAD')
    if ($r.ExitCode -ne 0) {
        throw ('git rev-parse failed in ' + $path)
    }
    $head = ($r.StdOut).Trim()
    if ($head -ne $s.commit) {
        throw ('upstream/' + $s.name + ' is at ' + $head + ' but decisions.md 3 pins ' + $s.commit)
    }
    Write-YwkLog -Message ('  upstream/' + $s.name + ' @ ' + $head + ' (pinned)')
}

# --------------------------------------------------------------------------- 1. copy

function Copy-YwkTreeFiltered {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    $src = (Resolve-Path -LiteralPath $Source).Path
    $null = New-YwkDirectory -Path $Destination
    $copied = 0
    $skipped = 0
    foreach ($f in (Get-ChildItem -LiteralPath $src -Recurse -File -Force)) {
        $rel = $f.FullName.Substring($src.Length).TrimStart('\')
        $parts = $rel -split '\\'
        # Every component is tested, the file name included: in a submodule ".git" is a FILE
        # (a gitfile pointing at ../../.git/modules/...), and copying it makes the copy look
        # like a broken repository to "git apply".
        $drop = $false
        for ($i = 0; $i -lt $parts.Count; $i++) {
            if ($ExcludeDirs -contains $parts[$i]) { $drop = $true; break }
        }
        if (-not $drop) {
            if ($ExcludeFiles -contains $parts[$parts.Count - 1]) { $drop = $true }
        }
        if ($drop) { $skipped = $skipped + 1; continue }
        $target = Join-Path $Destination $rel
        $tdir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $tdir)) {
            $null = New-Item -ItemType Directory -Path $tdir -Force
        }
        Copy-Item -LiteralPath $f.FullName -Destination $target -Force
        $copied = $copied + 1
    }
    return @{ Copied = $copied; Skipped = $skipped }
}

if (Test-Path -LiteralPath $AppDir) {
    Remove-Item -LiteralPath $AppDir -Recurse -Force
}
$null = New-YwkDirectory -Path $AppUpstream
# From here on the app directory is half-built; see the trap at the top.
$script:YwkPartialAppDir = $AppDir

foreach ($s in $Submodules) {
    $src = Join-Path $RepoRoot ('upstream\' + $s.name)
    $dst = Join-Path $AppUpstream $s.name
    $c = Copy-YwkTreeFiltered -Source $src -Destination $dst
    Write-YwkLog -Message ('copied ' + $s.name + ': ' + $c.Copied + ' files (' + $c.Skipped + ' skipped by the exclude list)')
    $null = Write-YwkTextFile -Path (Join-Path $dst 'UPSTREAM-COMMIT.txt') -Newline 'LF' -Text (
        ('repository: https://github.com/Aratako/' + $s.name) + "`n" +
        ('commit:     ' + $s.commit) + "`n" +
        "copied by:  build/assemble-app.ps1 (this tree is a patched copy; the submodule itself is untouched)`n")
}

# --------------------------------------------------------------------------- 2. patches

function Get-YwkPatchTargets {
    <#
      .SYNOPSIS
        Read the "+++ b/<path>" headers out of a unified diff.
    #>
    param([Parameter(Mandatory = $true)][string]$Path)
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $m = [regex]::Match($line, '^\+\+\+\s+(?:b/)?(\S+)')
        if ($m.Success) {
            $p = $m.Groups[1].Value
            if ($p -eq '/dev/null') { continue }
            if (-not $paths.Contains($p)) { $paths.Add($p) }
        }
    }
    return $paths.ToArray()
}

$patchDir = Join-Path $RepoRoot 'patches'
$applied = New-Object System.Collections.Generic.List[string]
if ($SkipPatches) {
    Write-YwkLog -Level 'WARN' -Message 'patches skipped by -SkipPatches'
} elseif (-not (Test-Path -LiteralPath $patchDir)) {
    Write-YwkLog -Level 'WARN' -Message 'patches/ does not exist yet -- nothing applied'
} else {
    $patches = @(Get-ChildItem -LiteralPath $patchDir -Filter '*.patch' -File | Sort-Object Name)
    if ($patches.Count -eq 0) {
        Write-YwkLog -Level 'WARN' -Message 'patches/ has no *.patch files yet -- nothing applied'
    }
    foreach ($p in $patches) {
        $targets = Get-YwkPatchTargets -Path $p.FullName
        if ($targets.Count -eq 0) {
            throw ('patch has no "+++" header: ' + $p.FullName)
        }
        $dest = $null
        foreach ($s in $Submodules) {
            $cand = Join-Path $AppUpstream $s.name
            $all = $true
            foreach ($t in $targets) {
                if (-not (Test-Path -LiteralPath (Join-Path $cand ($t -replace '/', '\')))) { $all = $false; break }
            }
            if ($all) { $dest = $cand; break }
        }
        if ($null -eq $dest) {
            throw ('cannot tell which upstream tree ' + $p.Name + ' belongs to; its targets are: ' + ($targets -join ', '))
        }
        $env2 = New-YwkChildEnvironment
        # "git apply" resolves the paths inside a diff against the root of the enclosing
        # repository, not against the current directory. Run from a subdirectory of a work
        # tree it silently SKIPS every target outside the current prefix ("Skipped patch
        # '...'") and still exits 0 -- for --check as well, so the check cannot catch it.
        # build/out/ sits inside this repository (ignored, but still inside the work tree),
        # so the copy must be addressed from the top level with --directory=<prefix>.
        $applyDir = $dest
        $extra = @()
        $tl = Invoke-YwkNative -FilePath $git -Arguments @('rev-parse', '--show-toplevel') -WorkingDirectory $dest -Environment $env2
        if ($tl.ExitCode -eq 0 -and $tl.StdOut.Trim().Length -gt 0) {
            $pfx = Invoke-YwkNative -FilePath $git -Arguments @('rev-parse', '--show-prefix') -WorkingDirectory $dest -Environment $env2
            $prefix = $pfx.StdOut.Trim()
            if ($pfx.ExitCode -eq 0 -and $prefix.Length -gt 0) {
                $applyDir = ($tl.StdOut.Trim() -replace '/', '\')
                $extra = @('--directory=' + $prefix)
                Write-YwkLog -Message ('  git apply from ' + $applyDir + ' with --directory=' + $prefix)
            }
        }
        $chk = Invoke-YwkNative -FilePath $git -Arguments (@('apply', '--check', '--verbose', '-p1') + $extra + @($p.FullName)) -WorkingDirectory $applyDir -Environment $env2
        if ($chk.ExitCode -ne 0) {
            Write-YwkLog -Level 'FAIL' -Message ('git apply --check failed for ' + $p.Name)
            Write-YwkLog -Level 'FAIL' -Message ($chk.StdErr.Trim())
            throw ('patch does not apply: ' + $p.FullName)
        }
        $app = Invoke-YwkNative -FilePath $git -Arguments (@('apply', '--verbose', '-p1') + $extra + @($p.FullName)) -WorkingDirectory $applyDir -Environment $env2
        if ($app.ExitCode -ne 0) {
            Write-YwkLog -Level 'FAIL' -Message ($app.StdErr.Trim())
            throw ('patch failed to apply after --check passed: ' + $p.FullName)
        }
        if (($app.StdErr + $app.StdOut) -match 'Skipped patch') {
            Write-YwkLog -Level 'FAIL' -Message (($app.StdErr + $app.StdOut).Trim())
            throw ('git apply skipped part of ' + $p.Name + ' (it exited 0 without writing); the copy is not patched')
        }
        # Prove the post-image is really on disk: --check --reverse succeeds only when every
        # hunk is already present. On an unpatched copy this exits 1.
        $rev = Invoke-YwkNative -FilePath $git -Arguments (@('apply', '--check', '--reverse', '-p1') + $extra + @($p.FullName)) -WorkingDirectory $applyDir -Environment $env2
        if ($rev.ExitCode -ne 0) {
            Write-YwkLog -Level 'FAIL' -Message ($rev.StdErr.Trim())
            throw ('patch reported success but the copy does not carry it: ' + $p.FullName)
        }
        Write-YwkLog -Message ('applied ' + $p.Name + ' to ' + (Split-Path -Leaf $dest) + ' (' + $targets.Count + ' files, reverse-check ok)')
        $applied.Add($p.Name)
    }
}

# --------------------------------------------------------------------------- 3. wrapper

$serverSrc = Join-Path $RepoRoot 'server'
if (-not (Test-Path -LiteralPath $serverSrc)) {
    throw ('missing ' + $serverSrc)
}
# *.py plus the non-python assets the installed tree needs. python312._pth.template
# is one of them: the ._pth on the user's machine carries absolute paths (design
# doc section 2), so it has to be rebuilt at install time -- and the launcher
# (convoy D) can only do that if the template ships next to the wrapper.
$wrapperAssets = @('python312._pth.template')
$wrapperFiles = @(
    Get-ChildItem -LiteralPath $serverSrc -File |
        Where-Object { $_.Extension -eq '.py' -or ($wrapperAssets -contains $_.Name) }
)
foreach ($f in $wrapperFiles) {
    Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $AppServer $f.Name) -Force
}
Write-YwkLog -Message ('copied ' + $wrapperFiles.Count + ' wrapper file(s) from server/')
if ($wrapperFiles.Count -eq 0) {
    Write-YwkLog -Level 'WARN' -Message 'server/ has no *.py yet (convoy A seat S2 writes ywk_server.py); the app tree is incomplete'
}
$missing = @()
foreach ($need in @('ywk_server.py', 'ywk_params.py')) {
    if (-not (Test-Path -LiteralPath (Join-Path $AppServer $need))) { $missing += $need }
}
if ($missing.Count -gt 0) {
    Write-YwkLog -Level 'WARN' -Message ('wrapper files not present yet: ' + ($missing -join ', '))
}
foreach ($need in $wrapperAssets) {
    if (-not (Test-Path -LiteralPath (Join-Path $AppServer $need))) {
        throw ('server/' + $need + ' did not reach the app tree; the installed runtime could not rebuild its ._pth')
    }
}

# --------------------------------------------------------------------------- 3b. ledger + licenses

# The installed tree carries ledger/ and licenses/ next to server/ (design doc section 2):
# the launcher reads the ledger to fetch the runtime and the models, and ywk_fetch_models.py
# looks for ../ledger/models.json relative to itself.
foreach ($side in @('ledger', 'licenses')) {
    $src = Join-Path $RepoRoot $side
    if (-not (Test-Path -LiteralPath $src)) {
        Write-YwkLog -Level 'WARN' -Message ($side + '/ does not exist yet; the app tree will not carry it')
        continue
    }
    $c = Copy-YwkTreeFiltered -Source $src -Destination (Join-Path $AppDir $side)
    Write-YwkLog -Message ('copied ' + $side + '/: ' + $c.Copied + ' files')
}

# --------------------------------------------------------------------------- 4. voices

$null = New-YwkDirectory -Path $AppVoices
# U+30C7 U+30D5 U+30A9 U+30EB U+30C8 = the reserved no-reference speaker (decisions.md 16).
# Built from code points so this ASCII-only script cannot be corrupted by a cp932 console.
$defaultName = [string]([char]0x30C7 + [char]0x30D5 + [char]0x30A9 + [char]0x30EB + [char]0x30C8)
$voices = [ordered]@{}
$voices[$defaultName] = [ordered]@{ no_ref = $true }
$null = Write-YwkJsonFile -Path (Join-Path $AppVoices 'voices.json') -Value $voices -Depth 6

$ywkTable = [ordered]@{
    schema  = 1
    note    = 'distributor-side speaker table (display name, default caption, default parameters). Convoy D fills this in; the wrapper only reads it.'
    voices  = [ordered]@{}
}
$ywkTable.voices[$defaultName] = [ordered]@{
    display_name = $defaultName
    preset       = $true
    no_ref       = $true
    caption      = $null
    defaults     = [ordered]@{}
}
$null = Write-YwkJsonFile -Path (Join-Path $AppVoices 'voices.ywk.json') -Value $ywkTable -Depth 8
Write-YwkLog -Message ('wrote voices/voices.json and voices/voices.ywk.json (default speaker U+30C7 U+30D5 U+30A9 U+30EB U+30C8)')

# --------------------------------------------------------------------------- 5. postflight

$post = Test-YwkUpstreamClean -RepoRoot $RepoRoot
foreach ($l in $post.Report) { Write-YwkLog -Message ('  ' + $l) }
if (-not $post.Ok) {
    throw 'upstream/ became dirty during assemble-app. This is a bug: patches must only touch the copy.'
}

$strayNotice = Join-Path $AppDir 'licenses\first-run-notices.md'
if (Test-Path -LiteralPath $strayNotice) {
    throw ('first-run-notices.md reached the app tree; it declares itself not part of the distributable: ' + $strayNotice)
}

# Everything landed and the submodules are still clean: the tree is finished, so
# the trap must not touch it if the report write below fails (a locked log file
# is not a reason to throw the build away).
$script:YwkPartialAppDir = ''

$fileCount = @(Get-ChildItem -LiteralPath $AppDir -Recurse -File -Force).Count
$report = [ordered]@{
    generated      = Get-YwkTimestamp
    app_dir        = $AppDir
    files          = $fileCount
    patches_applied = $applied.ToArray()
    wrapper_files  = @($wrapperFiles | ForEach-Object { $_.Name })
    wrapper_missing = $missing
    upstream_clean = $post.Ok
}
$null = Write-YwkJsonFile -Path (Join-Path $LogDir 'assemble-app.json') -Value $report

Write-YwkLog -Level 'STEP' -Message ('done: ' + $fileCount + ' files under ' + $AppDir + '; patches applied: ' + $applied.Count + '; upstream clean: ' + $post.Ok)
exit 0
