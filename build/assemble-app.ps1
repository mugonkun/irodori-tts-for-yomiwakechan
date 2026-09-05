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
        ledger/  licenses/            <- copied whole (first-run-notices.md included)
        voices/voices.json            <- {"<default>": {"no_ref": true}} (decisions.md 16)
        voices/voices.ywk.json        <- empty distributor-side speaker table (convoy D fills it)
        voices/presets.json           <- convoy P's preset ledger (the launcher reads it)
        voices/presets/*.wav          <- the 11 secondary reference voices (decisions.md 88 (5))

    licenses/first-run-notices.md IS copied (decisions.md 46). What stays out of the
    distributable is the third-party bytes (decisions.md 8), not the notice about them: the
    first-run UI has to show that text BEFORE it fetches the first byte, so the file has to be
    on disk by then. The postflight below fails the build if it is missing.

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
# Only .env: a stray dotenv inside a copied tree would silently re-point the server.
# first-run-notices.md is NOT excluded (decisions.md 46) -- the first-run UI shows it
# before it fetches a single third-party byte, so it has to ship with the app.
$ExcludeFiles = @('.env')

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

# These two files are written as LITERAL text, not through ConvertTo-Json, and the reason is a
# measurement: the two hosts pretty-print the same object differently, so the SAME sources gave two
# different distributable trees.
#   Windows PowerShell 5.1  voices.json 80 B / voices.ywk.json 596 B  (4-space, ':  ' after the key)
#   PowerShell 7.6.5        voices.json 50 B / voices.ywk.json 345 B  (2-space, ': ')
# Measured 2026-09-05 by writing the same [ordered] under each host back to back:
#   pwsh.exe  voices.json 50 B sha256-16 54D5FB65A572A56E / powershell.exe 80 B 1FE5488B44C33D26.
# Consequence while it stood: build/installer-build.ps1 had to carry TWO expected byte figures for
# the tree, and the setup.exe did not reproduce byte for byte across hosts (design 14-3 / 14-4).
# The shape of both files is fully known here, so there is nothing ConvertTo-Json is needed for.
# The bytes below are the PowerShell 7 shape, so the recorded tree figures do not move.
$jsonLf = "`n"
$voicesText = (@(
    '{',
    ('  "' + $defaultName + '": {'),
    '    "no_ref": true',
    '  }',
    '}'
) -join $jsonLf) + $jsonLf
$null = Write-YwkTextFile -Path (Join-Path $AppVoices 'voices.json') -Newline 'LF' -Text $voicesText

$ywkNote = 'distributor-side speaker table (display name, default caption, default parameters). Convoy D fills this in; the wrapper only reads it.'
$ywkText = (@(
    '{',
    '  "schema": 1,',
    ('  "note": "' + $ywkNote + '",'),
    '  "voices": {',
    ('    "' + $defaultName + '": {'),
    ('      "display_name": "' + $defaultName + '",'),
    '      "preset": true,',
    '      "no_ref": true,',
    '      "caption": null,',
    '      "defaults": {}',
    '    }',
    '  }',
    '}'
) -join $jsonLf) + $jsonLf
$null = Write-YwkTextFile -Path (Join-Path $AppVoices 'voices.ywk.json') -Newline 'LF' -Text $ywkText

# A literal is only safe if it is still JSON and still carries the reserved speaker. Both files are
# read back and parsed here, so a typo in the text above stops the build instead of shipping.
$voicesBytes = [int64](Get-Item -LiteralPath (Join-Path $AppVoices 'voices.json')).Length
$ywkBytes = [int64](Get-Item -LiteralPath (Join-Path $AppVoices 'voices.ywk.json')).Length
$voicesBack = ((Get-Content -LiteralPath (Join-Path $AppVoices 'voices.json') -Raw -Encoding UTF8) | ConvertFrom-Json)
$ywkBack = ((Get-Content -LiteralPath (Join-Path $AppVoices 'voices.ywk.json') -Raw -Encoding UTF8) | ConvertFrom-Json)
if ($voicesBack.PSObject.Properties.Name -notcontains $defaultName) {
    throw 'voices/voices.json was written without the reserved no-reference speaker (decisions.md 16).'
}
if ($ywkBack.voices.PSObject.Properties.Name -notcontains $defaultName) {
    throw 'voices/voices.ywk.json was written without the reserved no-reference speaker (decisions.md 16).'
}
if ([int]$ywkBack.schema -ne 1) {
    throw ('voices/voices.ywk.json has schema ' + $ywkBack.schema + ' (expected 1).')
}
Write-YwkLog -Message ('wrote voices/voices.json (' + $voicesBytes + ' B) and voices/voices.ywk.json (' +
    $ywkBytes + ' B) as literal text, host independent; parsed back OK ' +
    '(default speaker U+30C7 U+30D5 U+30A9 U+30EB U+30C8)')

# --------------------------------------------------------------------------- 4b. presets

# decisions.md 17, 37 and 88 (5): the eleven secondary reference wavs the seats generated in
# convoy P ship with the app. They are not third-party bytes (decisions.md 37) -- they are this
# project's own output -- so they are the one binary asset the distributable carries. Convoy A
# left this out; without it the launcher installs zero preset speakers and says nothing.
#
# The names below are load-bearing, not a convention. The launcher reads
#   <app>\voices\presets.json   (launcher Contracts/AppPaths.PresetsJsonPath)
#   <app>\voices\presets\*.wav  (launcher Contracts/AppPaths.PresetVoicesDir)
# and Services/Voices/PresetVoices.cs takes the speaker id from "display_name", the file name
# from "secondary.file", and skips every row whose "status" is not "done" (decisions.md 27 = the
# CeVIO row). Flattening the wavs into voices\ would still be found -- by the third fallback of
# PresetVoices.Discover -- but then the ascii file stems would become the speaker ids.

function Get-YwkJsonMember {
    <#
      .SYNOPSIS
        One property off a ConvertFrom-Json object, or $null when it is absent.
      .DESCRIPTION
        Set-StrictMode -Version Latest turns a missing property into a terminating error, and
        "this row has no secondary wav yet" is a normal state of voices/presets.json.
    #>
    param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object.PSObject.Properties.Name -notcontains $Name) { return $null }
    return $Object.$Name
}

$presetJsonSrc = Join-Path $RepoRoot 'voices\presets.json'
$presetDirSrc  = Join-Path $RepoRoot 'voices\presets'
$presetDirDst  = Join-Path $AppVoices 'presets'
$presetWanted  = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $presetJsonSrc)) {
    Write-YwkLog -Level 'WARN' -Message 'voices/presets.json is not there; the app tree will carry no preset speakers'
} else {
    Copy-Item -LiteralPath $presetJsonSrc -Destination (Join-Path $AppVoices 'presets.json') -Force
    $presetTable = Read-YwkJsonFile -Path $presetJsonSrc
    $presetRows = @(Get-YwkJsonMember -Object $presetTable -Name 'presets')
    $presetSkipped = 0
    foreach ($row in $presetRows) {
        $status = [string](Get-YwkJsonMember -Object $row -Name 'status')
        $secondary = Get-YwkJsonMember -Object $row -Name 'secondary'
        $file = [string](Get-YwkJsonMember -Object $secondary -Name 'file')
        if ($status -ne 'done' -or [string]::IsNullOrEmpty($file)) {
            $presetSkipped = $presetSkipped + 1
            continue
        }
        if (-not $presetWanted.Contains($file)) { $presetWanted.Add($file) }
    }
    Write-YwkLog -Message ('copied voices/presets.json: ' + $presetRows.Count + ' row(s), ' +
                           $presetWanted.Count + ' with status=done and a secondary wav, ' +
                           $presetSkipped + ' skipped (decisions.md 27)')
}

$presetCopied = 0
$presetBytes = [int64]0
if (Test-Path -LiteralPath $presetDirSrc) {
    $null = New-YwkDirectory -Path $presetDirDst
    foreach ($w in @(Get-ChildItem -LiteralPath $presetDirSrc -File | Sort-Object Name)) {
        if ($w.Extension.ToLowerInvariant() -ne '.wav') { continue }
        Copy-Item -LiteralPath $w.FullName -Destination (Join-Path $presetDirDst $w.Name) -Force
        $presetCopied = $presetCopied + 1
        $presetBytes = $presetBytes + [int64]$w.Length
        Write-YwkLog -Message ('  preset wav ' + $w.Name + ' ' + $w.Length + ' bytes')
    }
} elseif ($presetWanted.Count -gt 0) {
    Write-YwkLog -Level 'WARN' -Message 'voices/presets/ is not there, but presets.json lists secondary wavs'
}
Write-YwkLog -Message ('copied voices/presets/: ' + $presetCopied + ' wav file(s), ' + $presetBytes + ' bytes')

# A "done" row whose wav is not in the tree is a broken build, not a warning: the launcher would
# write a speaker table pointing at a file that never shipped.
$presetMissing = New-Object System.Collections.Generic.List[string]
foreach ($file in $presetWanted) {
    if (-not (Test-Path -LiteralPath (Join-Path $presetDirDst $file))) { $presetMissing.Add($file) }
}
if ($presetMissing.Count -gt 0) {
    throw ('voices/presets.json has status=done rows whose wav did not reach the app tree: ' +
           ($presetMissing -join ', '))
}

# --------------------------------------------------------------------------- 4c. bytecode sweep

# The copy filter above drops __pycache__ on the way IN ($ExcludeDirs). This sweeps the OUTPUT,
# which is a different question: build/out/app is a tree people RUN the server out of, and a run
# without PYTHONDONTWRITEBYTECODE leaves __pycache__ behind (the installer seat measured 3
# directories / 21 .pyc / 541,008 B in this tree on 2026-09-05). Those bytes are not part of the
# distributable -- the .iss excludes them again in [Files] -- but a tree that carries them makes
# the file count and the byte total of build/installer-build.ps1 gate A-1 lie, and leaves empty
# directories for [UninstallDelete]'s "dirifempty {app}" to trip over. The ruling is that the
# output side is swept here, so the gate can simply leave them out of its scan.
$pycacheDirs = @(Get-ChildItem -LiteralPath $AppDir -Recurse -Directory -Force -Filter '__pycache__' -ErrorAction SilentlyContinue)
$pycFiles = @(Get-ChildItem -LiteralPath $AppDir -Recurse -File -Force -Filter '*.pyc' -ErrorAction SilentlyContinue)
$pycBytes = [int64]0
foreach ($f in $pycFiles) { $pycBytes = $pycBytes + [int64]$f.Length }
foreach ($d in $pycacheDirs) {
    if (Test-Path -LiteralPath $d.FullName) {
        Remove-Item -LiteralPath $d.FullName -Recurse -Force
    }
}
foreach ($f in $pycFiles) {
    if (Test-Path -LiteralPath $f.FullName) {
        Remove-Item -LiteralPath $f.FullName -Force
    }
}
Write-YwkLog -Message ('swept the output tree: ' + $pycacheDirs.Count + ' __pycache__ dir(s), ' +
    $pycFiles.Count + ' .pyc file(s), ' + $pycBytes + ' bytes')

# --------------------------------------------------------------------------- 5. postflight

$post = Test-YwkUpstreamClean -RepoRoot $RepoRoot
foreach ($l in $post.Report) { Write-YwkLog -Message ('  ' + $l) }
if (-not $post.Ok) {
    throw 'upstream/ became dirty during assemble-app. This is a bug: patches must only touch the copy.'
}

# decisions.md 46: the notice ships. The first-run UI must be able to show what is about to be
# downloaded before it downloads it, and it can only do that from a file already on disk. An app
# tree without it is a build that would ask for a consent it cannot show.
$notice = Join-Path $AppDir 'licenses\first-run-notices.md'
if (-not (Test-Path -LiteralPath $notice)) {
    throw ('first-run-notices.md did not reach the app tree; the first-run UI shows it before anything is fetched (decisions.md 46): ' + $notice)
}
if ((Get-Item -LiteralPath $notice).Length -eq 0) {
    throw ('first-run-notices.md reached the app tree empty: ' + $notice)
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
    preset_wavs     = $presetCopied
    preset_bytes    = $presetBytes
    preset_expected = $presetWanted.ToArray()
    upstream_clean = $post.Ok
}
$null = Write-YwkJsonFile -Path (Join-Path $LogDir 'assemble-app.json') -Value $report

Write-YwkLog -Level 'STEP' -Message ('done: ' + $fileCount + ' files under ' + $AppDir + '; patches applied: ' + $applied.Count + '; preset wavs: ' + $presetCopied + '; upstream clean: ' + $post.Ok)
exit 0
