<#
.SYNOPSIS
    Acceptance condition A-7: no third-party binary, model weight or wheel is tracked by git,
    and both upstream submodules are untouched and on their pinned commits.

.DESCRIPTION
    Everything third-party is fetched at first run from the ledger; nothing of it belongs in
    the repository (decisions.md 8 and 22). This script is the guard that says so out loud.
    It reads only "git ls-files", so it judges what is TRACKED, not what happens to be sitting
    in the working tree (build/out/ and build/cache/ are ignored by design).

    Exit code 0 = clean, 1 = at least one violation. Nothing is ever deleted or rewritten.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\check-tree.ps1
#>
[CmdletBinding()]
param(
    [int]$MaxTrackedFileKB = 4096,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$RepoRoot = Get-YwkRepoRoot
$LogDir = Join-Path $RepoRoot 'build\out\check-log'
$null = New-YwkDirectory -Path $LogDir
$null = Start-YwkLog -Path (Join-Path $LogDir 'check-tree.log')
Write-YwkLog -Level 'STEP' -Message 'check-tree'

$git = Get-YwkGitPath

# Executable / library / archive / model formats. A tracked file with one of these suffixes is
# a violation on its face: none of them can be authored by this repository.
$BannedExt = @(
    '.whl', '.exe', '.dll', '.pyd', '.so', '.dylib', '.lib', '.a', '.obj',
    '.zip', '.7z', '.gz', '.tgz', '.tar', '.xz', '.bz2', '.msi', '.cab', '.nupkg',
    '.safetensors', '.ckpt', '.pt', '.pth', '.onnx', '.npz', '.npy', '.bin', '.gguf', '.pb', '.h5',
    '.msix', '.appx', '.jar', '.class', '.pdb'
)
# Paths that are allowed to be large even though they are not third-party binaries (none right
# now; convoy P adds preset reference voices under voices/). What those voices are, where they
# came from and whose terms apply is recorded in voices/presets.json and indexed by
# licenses/README.md section 6 -- this gate does not decide that question and must not claim to:
# section 6 says in as many words that no engine's terms have been fetched yet.
$LargeAllowPrefix = @()

$violations = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

# --------------------------------------------------------------------------- tracked files

$r = Invoke-YwkNative -FilePath $git -Arguments @('-C', $RepoRoot, 'ls-files')
if ($r.ExitCode -ne 0) {
    throw ('git ls-files failed: ' + $r.StdErr.Trim())
}
$tracked = @($r.StdOut -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
Write-YwkLog -Message ('tracked files: ' + $tracked.Count)

$binaryHits = New-Object System.Collections.Generic.List[string]
$bigHits = New-Object System.Collections.Generic.List[string]

foreach ($rel in $tracked) {
    $ext = [System.IO.Path]::GetExtension($rel).ToLowerInvariant()
    if ($BannedExt -contains $ext) {
        $binaryHits.Add($rel)
        continue
    }
    $full = Join-Path $RepoRoot ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) { continue }
    # git ls-files also lists the two submodule gitlinks; those are directories here.
    $fi = Get-Item -LiteralPath $full -Force
    if ($fi.PSIsContainer) { continue }
    $kb = [math]::Round($fi.Length / 1KB, 1)
    if ($kb -gt $MaxTrackedFileKB) {
        $allowed = $false
        foreach ($pfx in $LargeAllowPrefix) {
            if ($rel.StartsWith($pfx)) { $allowed = $true; break }
        }
        if (-not $allowed) {
            $bigHits.Add($rel + '  (' + $kb + ' KB)')
        }
    }
}

if ($binaryHits.Count -gt 0) {
    foreach ($h in $binaryHits) { $violations.Add('third-party binary tracked: ' + $h) }
} else {
    Write-YwkLog -Message 'PASS  no tracked file has a binary/model/wheel extension'
}
if ($bigHits.Count -gt 0) {
    foreach ($h in $bigHits) { $warnings.Add('tracked file over ' + $MaxTrackedFileKB + ' KB: ' + $h) }
} else {
    Write-YwkLog -Message ('PASS  no tracked file is larger than ' + $MaxTrackedFileKB + ' KB')
}

# ------------------------------------------------------- what the next commit would track
#
# "git ls-files" only sees what is already committed. While a convoy is being built its whole
# output is still untracked, so the test above can pass on a tree that is one "git add ." away
# from carrying a wheel or a model. Judge the untracked-but-not-ignored files by the same
# rules: those are exactly the ones the next commit would pick up. (Ignored files never can
# be, so they stay out of this.)

$u = Invoke-YwkNative -FilePath $git -Arguments @('-c', 'core.quotepath=false', '-C', $RepoRoot, 'status', '--porcelain', '--untracked-files=all')
if ($u.ExitCode -ne 0) {
    throw ('git status failed: ' + $u.StdErr.Trim())
}
$pending = New-Object System.Collections.Generic.List[string]
foreach ($line in ($u.StdOut -split "`n")) {
    $t = $line.TrimEnd()
    if ($t.Length -lt 4) { continue }
    if (-not $t.StartsWith('?? ')) { continue }
    $rel = $t.Substring(3).Trim()
    if ($rel.StartsWith('"') -and $rel.EndsWith('"')) { $rel = $rel.Substring(1, $rel.Length - 2) }
    if ($rel.Length -gt 0) { $pending.Add($rel) }
}
Write-YwkLog -Message ('untracked, not ignored (the next commit would take these): ' + $pending.Count)

$pendingBinary = New-Object System.Collections.Generic.List[string]
foreach ($rel in $pending) {
    $ext = [System.IO.Path]::GetExtension($rel).ToLowerInvariant()
    if ($BannedExt -contains $ext) {
        $pendingBinary.Add($rel)
        continue
    }
    $full = Join-Path $RepoRoot ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) { continue }
    $fi = Get-Item -LiteralPath $full -Force
    if ($fi.PSIsContainer) { continue }
    $kb = [math]::Round($fi.Length / 1KB, 1)
    if ($kb -gt $MaxTrackedFileKB) {
        $allowed = $false
        foreach ($pfx in $LargeAllowPrefix) {
            if ($rel.StartsWith($pfx)) { $allowed = $true; break }
        }
        if (-not $allowed) {
            $warnings.Add('untracked file over ' + $MaxTrackedFileKB + ' KB, it would be committed: ' + $rel + '  (' + $kb + ' KB)')
        }
    }
}
if ($pendingBinary.Count -gt 0) {
    foreach ($h in $pendingBinary) { $violations.Add('third-party binary would be committed: ' + $h) }
} else {
    Write-YwkLog -Message 'PASS  no untracked file has a binary/model/wheel extension either'
}

# --------------------------------------------------------------------------- build output is ignored

$ignoredHits = 0
foreach ($mustBeIgnored in @('build/out', 'build/cache', '.venv-dev')) {
    $hit = @($tracked | Where-Object { $_ -eq $mustBeIgnored -or $_.StartsWith($mustBeIgnored + '/') })
    $hit2 = @($pending | Where-Object { $_ -eq $mustBeIgnored -or $_.StartsWith($mustBeIgnored + '/') })
    if ($hit.Count -gt 0) {
        $ignoredHits = $ignoredHits + 1
        $violations.Add($mustBeIgnored + ' is tracked (' + $hit.Count + ' files); it must stay ignored')
    }
    if ($hit2.Count -gt 0) {
        $ignoredHits = $ignoredHits + 1
        $violations.Add($mustBeIgnored + ' is not ignored (' + $hit2.Count + ' files would be committed)')
    }
}
if ($ignoredHits -eq 0) {
    Write-YwkLog -Message 'PASS  build/out, build/cache and .venv-dev are neither tracked nor committable'
}

# --------------------------------------------------------------------------- submodules

$Submodules = @(
    @{ name = 'Irodori-TTS';        commit = '8224dafb46d0aba89209a8f905f1cb7e3299d9c1' },
    @{ name = 'Irodori-TTS-Server'; commit = '841fb7c6ec57729c56b9b75c0ef2562249b13a10' }
)
$clean = Test-YwkUpstreamClean -RepoRoot $RepoRoot
foreach ($l in $clean.Report) { Write-YwkLog -Message ('  ' + $l) }
if (-not $clean.Ok) {
    $violations.Add('upstream/ is not clean (see the report above)')
}
foreach ($s in $Submodules) {
    $path = Join-Path $RepoRoot ('upstream\' + $s.name)
    if (-not (Test-Path -LiteralPath $path)) {
        $violations.Add('upstream/' + $s.name + ' is missing')
        continue
    }
    $h = Invoke-YwkNative -FilePath $git -Arguments @('-C', $path, 'rev-parse', 'HEAD')
    $head = ($h.StdOut).Trim()
    if ($head -ne $s.commit) {
        $violations.Add('upstream/' + $s.name + ' is at ' + $head + ', pinned is ' + $s.commit)
    } else {
        Write-YwkLog -Message ('PASS  upstream/' + $s.name + ' @ ' + $s.commit)
    }
}

$sm = Invoke-YwkNative -FilePath $git -Arguments @('-C', $RepoRoot, 'submodule', 'status')
Write-YwkLog -Message ('git submodule status:')
foreach ($l in ($sm.StdOut -split "`n")) {
    if ($l.Trim().Length -gt 0) { Write-YwkLog -Message ('  ' + $l.Trim()) }
}
foreach ($l in ($sm.StdOut -split "`n")) {
    $t = $l.TrimEnd()
    if ($t.Length -eq 0) { continue }
    if ($t.StartsWith('+') -or $t.StartsWith('-') -or $t.StartsWith('U')) {
        $violations.Add('submodule not at the recorded commit: ' + $t.Trim())
    }
}

# --------------------------------------------------------------------------- report

$report = [ordered]@{
    generated  = Get-YwkTimestamp
    tracked    = $tracked.Count
    violations = $violations.ToArray()
    warnings   = $warnings.ToArray()
}
$null = Write-YwkJsonFile -Path (Join-Path $LogDir 'check-tree.json') -Value $report -Depth 8

foreach ($w in $warnings) { Write-YwkLog -Level 'WARN' -Message $w }
if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-YwkLog -Level 'FAIL' -Message $v }
    Write-YwkLog -Level 'FAIL' -Message ('A-7 FAILED: ' + [string]$violations.Count + ' violation(s)')
    exit 1
}
Write-YwkLog -Level 'STEP' -Message 'A-7 PASSED: no third-party binary, model or wheel is tracked; submodules clean and pinned'
exit 0
