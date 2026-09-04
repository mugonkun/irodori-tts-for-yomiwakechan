<#
.SYNOPSIS
    Build the RTX 3090 machine's handoff kit on N: (bundles, build cache, uv, runbook, ledger,
    MANIFEST.json) and prove every byte that landed there.

.DESCRIPTION
    Seat B, step 1 of docs/design/ben-b-rtx-remote.md ("what the design seat prepares").
    The RTX machine has git and Python and nothing else: no gh auth, no uv, no wheels. This
    script puts everything it needs in one folder on N: and writes a manifest the remote seat
    verifies before it trusts a single byte (probe/rtx-remote-runbook.md step 1-3).

    What lands in -Destination:
      repo.bundle                     git bundle of main with its full history
      upstream/*.bundle               the two upstream submodules, all refs, from .git/modules
      build-cache/                    the wheels/sdists/archives named by the cu130 + cu126
                                      ledgers that are already in build/cache, plus the embedded
                                      Python zip and VC_redist.x64.exe. torch and torchaudio are
                                      NOT here (1.87 / 2.59 GB - the RTX machine fetches those
                                      itself, design section 1), and neither are the rocm or cpu
                                      wheels, which the RTX machine has no use for.
      uv/uv.exe + the two uv LICENSEs from N:\irodori-native-research\kit-ssd\uv
      runbook/rtx-remote-runbook.md   read this first
      ledger/*.json + ledger/README.md
      README.txt                      one screen: what this is, what to read first
      MANIFEST.json                   every file above with size + sha256, the repo HEAD sha,
                                      the two submodule shas, and the working tree's status

    Everything is staged locally first (build/out/handoff-stage), hashed there, robocopied to
    -Destination, and then hashed AGAIN at the destination. A single mismatch fails the run and
    MANIFEST.json is not written - a handoff folder without a manifest is by definition not
    blessed, which is how "a non-zero exit leaves no artifact" is honoured here.

    Nothing outside -Destination and build/out/handoff-stage is written. The repository, the
    submodules and N:\irodori-native-research are read only.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\make-handoff.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\make-handoff.ps1 -Destination D:\tmp\handoff -SkipUv
#>
[CmdletBinding()]
param(
    [string]$Destination = 'N:\temp_for_claudecode_agents\irodori-ywk\rtx-handoff',
    [string]$StageRoot = '',
    [string]$CacheRoot = '',
    [string]$UvSource = 'N:\irodori-native-research\kit-ssd\uv',
    [string[]]$LedgerVariants = @('cu130', 'cu126'),
    [switch]$IncludeAllCache,
    [switch]$SkipUv,
    [switch]$StageOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$RepoRoot = Get-YwkRepoRoot
if ([string]::IsNullOrEmpty($StageRoot)) { $StageRoot = Join-Path $RepoRoot 'build\out\handoff-stage' }
if ([string]::IsNullOrEmpty($CacheRoot)) { $CacheRoot = Join-Path $RepoRoot 'build\cache' }

# One timestamp for the whole run. README.txt used to stamp itself when it was written and
# MANIFEST.json when IT was written, which put two different times (47 s apart, measured
# 2026-09-05) on one handoff folder and gave the remote seat two answers to "when was this
# built?". Both now print this value.
#
# ONE Get-Date, converted twice. generated_utc and generated_local were previously read from
# two different clock reads minutes apart (the local one at manifest time, at the very end of
# the run), so subtracting the offset did not give back the other field and the pair read as a
# contradiction. These two are now the same instant by construction.
$BuiltAtInstant = Get-Date
$BuiltAt = $BuiltAtInstant.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$BuiltAtLocal = $BuiltAtInstant.ToString('yyyy-MM-ddTHH:mm:sszzz')

$LogDir = Join-Path $RepoRoot 'build\out\handoff-log'
$null = New-YwkDirectory -Path $LogDir
$null = Start-YwkLog -Path (Join-Path $LogDir 'make-handoff.log')
Write-YwkLog -Level 'STEP' -Message ('make-handoff: destination=' + $Destination)
Write-YwkLog -Message ('stage=' + $StageRoot)
Write-YwkLog -Message ('cache=' + $CacheRoot)

$git = Get-YwkGitPath

function Invoke-YwkGit {
    <#
      .SYNOPSIS
        Run git and fail loudly. Returns trimmed stdout.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )
    $r = Invoke-YwkNative -FilePath $git -Arguments $Arguments -WorkingDirectory $RepoRoot -TimeoutSeconds 3600 -StdioEncoding 'utf8'
    if ($r.ExitCode -ne 0 -and -not $AllowFailure) {
        throw ('git ' + ($Arguments -join ' ') + ' failed (exit ' + $r.ExitCode + '): ' + $r.StdErr.Trim())
    }
    return $r
}

# --------------------------------------------------------------------------- repo facts

$headSha = (Invoke-YwkGit -Arguments @('rev-parse', 'HEAD')).StdOut.Trim()
$headBranch = (Invoke-YwkGit -Arguments @('rev-parse', '--abbrev-ref', 'HEAD')).StdOut.Trim()
$headLine = (Invoke-YwkGit -Arguments @('log', '-1', '--format=%H|%ad|%an|%s', '--date=iso')).StdOut.Trim()
$statusRaw = (Invoke-YwkGit -Arguments @('status', '--porcelain')).StdOut
$statusLines = @()
foreach ($l in ($statusRaw -split "`n")) {
    $t = $l.TrimEnd()
    if ($t.Length -gt 0) { $statusLines += $t }
}
Write-YwkLog -Message ('HEAD = ' + $headSha + '  branch = ' + $headBranch)
Write-YwkLog -Message ('HEAD line = ' + $headLine)
if ($statusLines.Count -gt 0) {
    Write-YwkLog -Level 'WARN' -Message ('the working tree has ' + $statusLines.Count + ' uncommitted change(s); the bundle carries only what is COMMITTED')
    foreach ($l in $statusLines) { Write-YwkLog -Level 'WARN' -Message ('  ' + $l) }
}

$subPaths = @('upstream/Irodori-TTS', 'upstream/Irodori-TTS-Server')
$subFacts = New-Object System.Collections.Generic.List[object]
foreach ($sub in $subPaths) {
    $winPath = Join-Path $RepoRoot ($sub -replace '/', '\')
    $sha = (Invoke-YwkNative -FilePath $git -Arguments @('-C', $winPath, 'rev-parse', 'HEAD') -StdioEncoding 'utf8').StdOut.Trim()
    $dirty = (Invoke-YwkNative -FilePath $git -Arguments @('-C', $winPath, 'status', '--porcelain') -StdioEncoding 'utf8').StdOut.Trim()
    $subFacts.Add([ordered]@{
        path  = $sub
        sha   = $sha
        clean = [string]::IsNullOrEmpty($dirty)
        bundle = 'upstream\' + (Split-Path -Leaf $sub) + '.bundle'
    })
    Write-YwkLog -Message ('submodule ' + $sub + ' = ' + $sha + '  clean=' + [string]([string]::IsNullOrEmpty($dirty)))
}

# --------------------------------------------------------------------------- stage

if (Test-Path -LiteralPath $StageRoot) {
    Write-YwkLog -Message ('clearing the stage ' + $StageRoot)
    Remove-Item -LiteralPath $StageRoot -Recurse -Force
}
$null = New-YwkDirectory -Path $StageRoot
$null = New-YwkDirectory -Path (Join-Path $StageRoot 'upstream')
$null = New-YwkDirectory -Path (Join-Path $StageRoot 'build-cache')
$null = New-YwkDirectory -Path (Join-Path $StageRoot 'ledger')
$null = New-YwkDirectory -Path (Join-Path $StageRoot 'runbook')

# ------------------------------------------------------------------ (a) bundles

$repoBundle = Join-Path $StageRoot 'repo.bundle'
Write-YwkLog -Level 'STEP' -Message ('git bundle create repo.bundle ' + $headBranch + ' HEAD (full history)')
# 'HEAD' is not decoration. A bundle made with the branch name alone records refs/heads/<branch>
# and no HEAD, and then `git clone that.bundle` says "remote HEAD refers to nonexistent ref,
# unable to checkout" and leaves an EMPTY working tree on an unborn branch. Measured here on
# 2026-09-05 before this argument was added.
$null = Invoke-YwkGit -Arguments @('bundle', 'create', $repoBundle, $headBranch, 'HEAD')
$verify = Invoke-YwkGit -Arguments @('bundle', 'verify', $repoBundle)
Write-YwkLog -Message ('repo.bundle verify: ' + ($verify.StdOut + ' ' + $verify.StdErr).Trim())
$heads = (Invoke-YwkGit -Arguments @('bundle', 'list-heads', $repoBundle)).StdOut
if ($heads -notmatch [regex]::Escape($headSha)) {
    throw ('repo.bundle does not carry HEAD ' + $headSha + '. list-heads said: ' + $heads.Trim())
}
if ($heads -notmatch '(?m)\bHEAD\s*$') {
    throw ('repo.bundle has no HEAD ref, so `git clone` would check out nothing. list-heads said: ' + $heads.Trim())
}
Write-YwkLog -Message ('repo.bundle carries ' + $headSha + ' and a HEAD ref')

# Prove the clone really works rather than trusting the bundle format: this is the one step the
# remote seat cannot recover from on its own.
$cloneCheck = Join-Path $LogDir 'clone-check'
if (Test-Path -LiteralPath $cloneCheck) { Remove-Item -LiteralPath $cloneCheck -Recurse -Force }
Write-YwkLog -Message ('smoke test: git clone repo.bundle -> ' + $cloneCheck)
$cl = Invoke-YwkNative -FilePath $git -Arguments @('clone', '--quiet', $repoBundle, $cloneCheck) -TimeoutSeconds 1800 -StdioEncoding 'utf8'
if ($cl.ExitCode -ne 0) {
    throw ('the smoke-test clone of repo.bundle failed (exit ' + $cl.ExitCode + '): ' + $cl.StdErr.Trim())
}
$clonedSha = (Invoke-YwkNative -FilePath $git -Arguments @('-C', $cloneCheck, 'rev-parse', 'HEAD') -TimeoutSeconds 300 -StdioEncoding 'utf8').StdOut.Trim()
$clonedMarker = Join-Path $cloneCheck 'decisions.md'
if ($clonedSha -ne $headSha) {
    throw ('the smoke-test clone landed on ' + $clonedSha + ', not ' + $headSha)
}
if (-not (Test-Path -LiteralPath $clonedMarker)) {
    throw ('the smoke-test clone has no working tree (decisions.md is missing). The bundle would give the RTX machine an empty checkout.')
}
Write-YwkLog -Message ('smoke test ok: the clone checks out ' + $clonedSha + ' with a working tree')
try {
    Remove-Item -LiteralPath $cloneCheck -Recurse -Force
} catch {
    # git leaves read-only pack files on Windows; a leftover under build/out/handoff-log is noise,
    # not a failure, and the next run clears it.
    Write-YwkLog -Level 'WARN' -Message ('could not delete the smoke-test clone: ' + $_.Exception.Message)
}

foreach ($fact in $subFacts) {
    $winPath = Join-Path $RepoRoot ($fact.path -replace '/', '\')
    $out = Join-Path $StageRoot $fact.bundle
    Write-YwkLog -Level 'STEP' -Message ('git bundle create ' + $fact.bundle + ' --all')
    $r = Invoke-YwkNative -FilePath $git -Arguments @('-C', $winPath, 'bundle', 'create', $out, '--all') -TimeoutSeconds 3600 -StdioEncoding 'utf8'
    if ($r.ExitCode -ne 0) {
        throw ('git bundle create for ' + $fact.path + ' failed (exit ' + $r.ExitCode + '): ' + $r.StdErr.Trim())
    }
    $lh = Invoke-YwkNative -FilePath $git -Arguments @('-C', $winPath, 'bundle', 'list-heads', $out) -TimeoutSeconds 600 -StdioEncoding 'utf8'
    if ($lh.StdOut -notmatch [regex]::Escape($fact.sha)) {
        throw ($fact.bundle + ' does not carry the pinned commit ' + $fact.sha + '. list-heads said: ' + $lh.StdOut.Trim())
    }
    Write-YwkLog -Message ($fact.bundle + ' carries the pinned ' + $fact.sha)
}

# ------------------------------------------------------------------ (b) build cache

Write-YwkLog -Level 'STEP' -Message 'selecting the cache files'

function Get-YwkLedgerFileName {
    <#
      .SYNOPSIS
        The name assemble-runtime.ps1 gives a ledger item inside the cache. Same rule, on purpose:
        a different rule here would silently ship files under names the assembler never looks for.
    #>
    [CmdletBinding()]
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
    return ($fileName -replace '[<>:"/\\|?*]', '_')
}

$wantNames = New-Object 'System.Collections.Generic.HashSet[string]'
$ledgerFiles = New-Object System.Collections.Generic.List[string]
foreach ($v in $LedgerVariants) {
    $ledgerFiles.Add('runtime-' + $v + '.json')
}
$ledgerFiles.Add('python-embed.json')
$ledgerFiles.Add('vc_redist.json')
foreach ($lf in $ledgerFiles) {
    $path = Join-Path $RepoRoot ('ledger\' + $lf)
    if (-not (Test-Path -LiteralPath $path)) {
        Write-YwkLog -Level 'WARN' -Message ('ledger not found, skipping: ' + $lf)
        continue
    }
    $j = Read-YwkJsonFile -Path $path
    $itemList = @()
    if (($j.PSObject.Properties.Name) -contains 'items') {
        $itemList = @($j.items)
    } else {
        $itemList = @($j)
    }
    foreach ($it in $itemList) {
        if (($it.PSObject.Properties.Name) -notcontains 'url') { continue }
        $null = $wantNames.Add((Get-YwkLedgerFileName -Item $it))
    }
}
Write-YwkLog -Message ('the cu ledgers name ' + $wantNames.Count + ' distinct file(s)')

# torch and torchaudio are deliberately not shipped: 1.87 GB (cu130) and 2.59 GB (cu126) each,
# and design section 1 says the RTX machine fetches them directly from download.pytorch.org.
$excludePrefixes = @('torch-', 'torchaudio-')

$cacheStage = Join-Path $StageRoot 'build-cache'
$copiedCache = New-Object System.Collections.Generic.List[object]
$skippedCache = New-Object System.Collections.Generic.List[object]
$cacheItems = @()
if (Test-Path -LiteralPath $CacheRoot) {
    $cacheItems = @(Get-ChildItem -LiteralPath $CacheRoot -File)
} else {
    Write-YwkLog -Level 'WARN' -Message ('no cache directory at ' + $CacheRoot)
}
foreach ($f in $cacheItems) {
    $take = $false
    $why = ''
    if ($IncludeAllCache) {
        $take = $true
        $why = '-IncludeAllCache'
    } elseif ($wantNames.Contains($f.Name)) {
        $take = $true
        $why = 'named by a cu ledger'
    } else {
        $why = 'not named by the cu130/cu126/python-embed/vc_redist ledgers'
    }
    if ($take) {
        foreach ($p in $excludePrefixes) {
            if ($f.Name.StartsWith($p)) {
                $take = $false
                $why = 'excluded: the RTX machine fetches ' + $p.TrimEnd('-') + ' itself (design section 1)'
                break
            }
        }
    }
    if ($take) {
        Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $cacheStage $f.Name) -Force
        $copiedCache.Add([ordered]@{ name = $f.Name; size = [int64]$f.Length })
    } else {
        $skippedCache.Add([ordered]@{ name = $f.Name; size = [int64]$f.Length; reason = $why })
    }
}
Write-YwkLog -Message ('cache: copied ' + $copiedCache.Count + ', skipped ' + $skippedCache.Count)
foreach ($s in $skippedCache) {
    Write-YwkLog -Message ('  skip ' + $s.name + '  (' + $s.reason + ')')
}

$missingFromCache = New-Object System.Collections.Generic.List[string]
foreach ($n in $wantNames) {
    $isExcluded = $false
    foreach ($p in $excludePrefixes) {
        if ($n.StartsWith($p)) { $isExcluded = $true; break }
    }
    if ($isExcluded) { continue }
    if (-not (Test-Path -LiteralPath (Join-Path $cacheStage $n))) {
        $missingFromCache.Add($n)
    }
}
if ($missingFromCache.Count -gt 0) {
    Write-YwkLog -Level 'WARN' -Message ($missingFromCache.Count + ' ledger file(s) are not in the cache; the RTX machine will fetch them itself:')
    foreach ($n in $missingFromCache) { Write-YwkLog -Level 'WARN' -Message ('  ' + $n) }
}

# ------------------------------------------------------------------ (c) uv

$uvFiles = @()
if ($SkipUv) {
    Write-YwkLog -Level 'WARN' -Message '-SkipUv: uv.exe is not shipped (build\dev-venv.ps1 will have nothing to run on the RTX machine)'
} elseif (-not (Test-Path -LiteralPath $UvSource)) {
    throw ('uv source not found: ' + $UvSource + '. Pass -UvSource or -SkipUv.')
} else {
    $null = New-YwkDirectory -Path (Join-Path $StageRoot 'uv')
    foreach ($name in @('uv.exe', 'LICENSE-uv-APACHE', 'LICENSE-uv-MIT')) {
        $src = Join-Path $UvSource $name
        if (-not (Test-Path -LiteralPath $src)) {
            throw ('missing from the uv kit: ' + $src)
        }
        Copy-Item -LiteralPath $src -Destination (Join-Path $StageRoot ('uv\' + $name)) -Force
        $uvFiles += $name
    }
    Write-YwkLog -Message ('uv: copied ' + ($uvFiles -join ', ') + ' from ' + $UvSource)
}

# ------------------------------------------------------------------ (d) runbook + ledger

$runbookSrc = Join-Path $RepoRoot 'probe\rtx-remote-runbook.md'
if (-not (Test-Path -LiteralPath $runbookSrc)) {
    throw ('missing ' + $runbookSrc + ' - the remote seat has nothing to read without it')
}
Copy-Item -LiteralPath $runbookSrc -Destination (Join-Path $StageRoot 'runbook\rtx-remote-runbook.md') -Force

foreach ($f in (Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'ledger') -File)) {
    Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $StageRoot ('ledger\' + $f.Name)) -Force
}
Write-YwkLog -Message 'copied the runbook and the whole ledger directory'

# A git bundle carries only COMMITTED history. The bench tools may still be uncommitted when this
# runs (seats work in parallel and this one does not commit), so ship them beside the bundle as
# well; the runbook tells the remote seat to drop these into the clone if the clone lacks them.
$null = New-YwkDirectory -Path (Join-Path $StageRoot 'probe')
$probeLoose = @('bench_cases.json', 'cuda_bench.py', 'cuda-bench.ps1', 'rtx-remote-runbook.md')
$probeShipped = New-Object System.Collections.Generic.List[object]
foreach ($name in $probeLoose) {
    $src = Join-Path $RepoRoot ('probe\' + $name)
    if (-not (Test-Path -LiteralPath $src)) {
        throw ('missing ' + $src)
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $StageRoot ('probe\' + $name)) -Force
    # in the bundle only when the file is tracked AND has no pending change
    $tracked = Invoke-YwkGit -Arguments @('ls-files', '--error-unmatch', ('probe/' + $name)) -AllowFailure
    $pending = (Invoke-YwkGit -Arguments @('status', '--porcelain', '--', ('probe/' + $name))).StdOut.Trim()
    $probeShipped.Add([ordered]@{
        name      = $name
        tracked   = ($tracked.ExitCode -eq 0)
        pending   = $pending
        in_bundle = (($tracked.ExitCode -eq 0) -and [string]::IsNullOrEmpty($pending))
    })
}
Write-YwkLog -Message ('shipped ' + $probeShipped.Count + ' probe file(s) loose beside the bundle')
foreach ($p in $probeShipped) {
    Write-YwkLog -Message ('  probe\' + $p.name + '  in_bundle=' + $p.in_bundle + '  git=' + $p.pending)
}

# EVERY element that concatenates a variable is wrapped in its own parentheses. Without
# them PowerShell binds ',' tighter than '+', so
#     'a: ', 'b ' + $x + ' c', 'd'
# parses as ('a: ','b ') + $x + (' c','d') -- five array elements, not three - and the
# -join below then put $headBranch on a line of its own. That is exactly what the shipped
# README.txt said before this comment existed:
#       repo.bundle        git bundle of
#     main
#      with its full history
$readme = @(
    'irodori-tts-for-yomiwakechan -- RTX 3090 handoff kit',
    ('built ' + $BuiltAt + ' from repo HEAD ' + $headSha),
    '',
    'READ FIRST:  runbook\rtx-remote-runbook.md',
    '',
    'Contents',
    ('  repo.bundle        git bundle of ' + $headBranch + ' with its full history'),
    '  upstream\*.bundle  the two pinned upstream submodules',
    '  build-cache\       the wheels the cu130/cu126 ledgers name and this machine already had',
    '                     (torch and torchaudio are NOT here: the RTX machine fetches those)',
    '  uv\uv.exe          uv 0.12.7 for build\dev-venv.ps1 (contract tests only)',
    '  ledger\            the acquisition ledgers, for reading without cloning',
    '  runbook\           the script the remote seat follows, verbatim',
    '  probe\             the bench tools, loose (repo.bundle only carries COMMITTED history;',
    '                     see MANIFEST.json probe_loose[].in_bundle)',
    '  MANIFEST.json      every file above with its size and sha256',
    '  verify-handoff.ps1 checks this whole folder against MANIFEST.json',
    '',
    'Verify before you trust any of it (runbook step 1-3):',
    '  powershell -NoProfile -ExecutionPolicy Bypass -File <this folder>\verify-handoff.ps1',
    'It prints RESULT=OK or RESULT=FAIL. Do not proceed on FAIL.',
    '',
    'repo.bundle carries COMMITTED history only. MANIFEST.json repo.working_tree_status lists',
    'what was pending when this kit was built, and verify-handoff.ps1 splits that list two ways:',
    'an UNTRACKED file is not in the bundle at all; a TRACKED file with a pending edit IS in the',
    'bundle, at its older committed content. The bench tools are shipped loose in probe\ for',
    'exactly that reason.',
    '',
    'Stop zones on the RTX machine: do not touch D: E: F:. Do not run anything from N:',
    '(copy to C:\ywk first). Do not push anywhere. Do not git commit -- results go to',
    'N:\temp_for_claudecode_agents\irodori-ywk\rtx\ (decisions.md 21, 22).'
) -join "`r`n"
$null = Write-YwkTextFile -Path (Join-Path $StageRoot 'README.txt') -Newline 'CRLF' -Text ($readme + "`r`n")

# ------------------------------------------------------------------ verify-handoff.ps1
#
# Shipped as a file rather than as a one-liner in the runbook on purpose: the remote seat may be
# driving the shell through a wrapper that expands '$' before PowerShell ever sees it, and this
# is the one check that has to work before anything else exists. Run with -File and there is not
# a single '$' on the command line.

$verifyScript = @'
# verify-handoff.ps1 -- generated by build/make-handoff.ps1. Do not edit here; edit the generator.
# Hash every file this folder's MANIFEST.json names and compare. Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File <this folder>\verify-handoff.ps1
# Runs on Windows PowerShell 5.1 and on PowerShell 7. ASCII only. No '$' on the command line.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-HandoffSha256 {
    # The same implementation as build/Common.ps1 Get-YwkSha256, embedded on purpose: this
    # file must stand alone on a machine that has no clone yet. Get-FileHash lives in
    # Microsoft.PowerShell.Utility; if the host cannot autoload it (a stripped or foreign
    # PSModulePath -- for instance a Windows PowerShell 5.1 started by pwsh 7 -- or a
    # constrained host), fall back to the .NET provider rather than die with
    # "The term 'Get-FileHash' is not recognized". Everything here rests on this hash.
    param([string]$Path)
    if ($null -ne (Get-Command 'Get-FileHash' -ErrorAction SilentlyContinue)) {
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        try {
            $bytes = $sha.ComputeHash($fs)
        } finally {
            $fs.Dispose()
        }
    } finally {
        $sha.Dispose()
    }
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

$root = $PSScriptRoot
$manifestPath = Join-Path $root 'MANIFEST.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    Write-Host ('MANIFEST.json is missing from ' + $root + ' -- this handoff folder is NOT blessed.')
    Write-Host 'RESULT=FAIL'
    exit 2
}
# ReadAllText with an explicit UTF8 encoding, NOT Get-Content -Raw: on a ja-JP Windows
# PowerShell 5.1 Get-Content decodes with the ANSI code page (CP932) and any non-ASCII
# byte in the manifest comes back mangled -- badly enough to break ConvertFrom-Json.
$m = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$bad = New-Object System.Collections.Generic.List[string]
$known = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($f in $m.files) {
    $null = $known.Add([string]$f.path)
    $p = Join-Path $root $f.path
    if (-not (Test-Path -LiteralPath $p)) {
        $bad.Add($f.path + '  MISSING')
        continue
    }
    $size = (Get-Item -LiteralPath $p).Length
    if ($size -ne $f.size) {
        $bad.Add($f.path + '  SIZE ' + $size + ' expected ' + $f.size)
    }
    $h = Get-HandoffSha256 -Path $p
    if ($h -ne $f.sha256) {
        $bad.Add($f.path + '  SHA256 MISMATCH')
    }
}
# The check above is one-directional: it proves every file the manifest names is here and
# intact, and says nothing about files that are here and the manifest does NOT name. A stale
# file left behind by an earlier, different handoff reads exactly like a blessed one to anyone
# browsing the folder. These are listed as EXTRA and warned about; they are not a FAIL, because
# nothing the runbook uses depends on them, and make-handoff.ps1 robocopies with /PURGE so a
# folder it wrote should have none.
# MANIFEST.json itself is expected to be absent from files[]: it is written after the stage is
# hashed, so it cannot contain its own hash.
$extra = New-Object System.Collections.Generic.List[string]
$rootPrefix = (Resolve-Path -LiteralPath $root).Path
if (-not $rootPrefix.EndsWith('\')) { $rootPrefix = $rootPrefix + '\' }
foreach ($f in (Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
    $rel = $f.FullName.Substring($rootPrefix.Length)
    if ($rel -eq 'MANIFEST.json') { continue }
    if (-not $known.Contains($rel)) {
        $extra.Add($rel + '  (' + $f.Length + ' B)')
    }
}
Write-Host ('handoff root  : ' + $root)
# PowerShell 7's ConvertFrom-Json turns an ISO-8601 string into a [datetime] and then
# prints it in the current culture ("09/04/2026 17:59:29"), while 5.1 leaves it a string.
# Force the one form back, so both shells print the same instant as README.txt does.
$built = $m.generated_utc
if ($built -is [datetime]) { $built = $built.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
Write-Host ('built         : ' + $built)
Write-Host ('repo HEAD     : ' + $m.repo.head + '  branch ' + $m.repo.branch)
Write-Host ('working tree  : clean=' + $m.repo.working_tree_clean + '  (the bundle carries only COMMITTED history)')
# git status --porcelain lines, split by what they actually mean for the clone. Printing them
# all as "NOT in the bundle" was wrong for half of them: a tracked file with a pending edit IS
# in the bundle - at its last committed content - and the clone will hand the remote seat that
# older version silently. An untracked file is genuinely absent.
foreach ($u in $m.repo.working_tree_status) {
    $line = [string]$u
    $code = ''
    if ($line.Length -ge 2) { $code = $line.Substring(0, 2) }
    $name = $line
    if ($line.Length -gt 3) { $name = $line.Substring(3) }
    if ($code -eq '??') {
        Write-Host ('  NOT in the bundle at all (untracked): ' + $name)
    } else {
        Write-Host ('  in the bundle at its COMMITTED content, which is OLDER than this machine''s copy (' + $code.Trim() + '): ' + $name)
    }
}
foreach ($s in $m.repo.submodules) {
    Write-Host ('submodule     : ' + $s.path + ' = ' + $s.sha)
}
foreach ($p in $m.probe_loose.files) {
    Write-Host ('probe loose   : ' + $p.name + '  in_bundle=' + $p.in_bundle)
}
Write-Host ('files checked : ' + $m.files.Count)
Write-Host ('bad           : ' + $bad.Count)
foreach ($b in $bad) {
    Write-Host ('  ' + $b)
}
Write-Host ('extra         : ' + $extra.Count + '  (present here, not named by MANIFEST.json)')
foreach ($e in $extra) {
    Write-Host ('  WARN EXTRA: ' + $e)
}
if ($extra.Count -gt 0) {
    Write-Host 'The extra files above are NOT part of this handoff. Do not use them; report them.'
}
if ($bad.Count -gt 0) {
    Write-Host 'RESULT=FAIL'
    exit 1
}
Write-Host 'RESULT=OK'
exit 0
'@
$null = Write-YwkTextFile -Path (Join-Path $StageRoot 'verify-handoff.ps1') -Newline 'CRLF' -Text ($verifyScript + "`r`n")
Write-YwkLog -Message 'wrote verify-handoff.ps1 into the stage'

# --------------------------------------------------------------------------- hash the stage

Write-YwkLog -Level 'STEP' -Message 'hashing the stage'
$stagePrefix = (Resolve-Path -LiteralPath $StageRoot).Path
if (-not $stagePrefix.EndsWith('\')) { $stagePrefix = $stagePrefix + '\' }
$stageFiles = New-Object System.Collections.Generic.List[object]
foreach ($f in (Get-ChildItem -LiteralPath $StageRoot -Recurse -File | Sort-Object FullName)) {
    $rel = $f.FullName.Substring($stagePrefix.Length)
    $stageFiles.Add([ordered]@{
        path   = $rel
        size   = [int64]$f.Length
        sha256 = (Get-YwkSha256 -Path $f.FullName)
    })
}
$totalBytes = 0
foreach ($f in $stageFiles) { $totalBytes = $totalBytes + [int64]$f.size }
Write-YwkLog -Message ('stage: ' + $stageFiles.Count + ' file(s), ' + $totalBytes + ' B')

if ($StageOnly) {
    Write-YwkLog -Level 'STEP' -Message ('-StageOnly: stopping with the stage at ' + $StageRoot)
    exit 0
}

# --------------------------------------------------------------------------- copy and re-hash

$destParent = Split-Path -Parent $Destination
if (-not (Test-Path -LiteralPath $destParent)) {
    throw ('the destination parent does not exist: ' + $destParent + ' (is N: mapped?)')
}
$null = New-YwkDirectory -Path $Destination

Write-YwkLog -Level 'STEP' -Message ('robocopy ' + $StageRoot + ' -> ' + $Destination)
$robocopy = Join-Path $env:SystemRoot 'System32\robocopy.exe'
if (-not (Test-Path -LiteralPath $robocopy)) { $robocopy = 'robocopy.exe' }
$rc = Invoke-YwkNative -FilePath $robocopy -Arguments @(
    $StageRoot, $Destination, '/E', '/PURGE', '/R:2', '/W:5', '/NFL', '/NDL', '/NJH', '/NJS', '/NP'
) -TimeoutSeconds 7200 -StdioEncoding 'console'
$null = Write-YwkTextFile -Path (Join-Path $LogDir 'robocopy.log') -Newline 'LF' -Text ($rc.StdOut + "`n" + $rc.StdErr + "`n(exit " + $rc.ExitCode + ")`n")
if ($rc.ExitCode -ge 8) {
    throw ('robocopy failed (exit ' + $rc.ExitCode + '): ' + ($rc.StdOut + ' ' + $rc.StdErr).Trim())
}
Write-YwkLog -Message ('robocopy exit ' + $rc.ExitCode + ' (0-7 = success)')

Write-YwkLog -Level 'STEP' -Message 'hashing what landed at the destination'
$manifestFiles = New-Object System.Collections.Generic.List[object]
$mismatches = New-Object System.Collections.Generic.List[string]
foreach ($f in $stageFiles) {
    $destFile = Join-Path $Destination $f.path
    if (-not (Test-Path -LiteralPath $destFile)) {
        $mismatches.Add($f.path + ': MISSING at the destination')
        continue
    }
    $size = Get-YwkFileSize -Path $destFile
    $sha = Get-YwkSha256 -Path $destFile
    if ($size -ne [int64]$f.size) {
        $mismatches.Add($f.path + ': size ' + $size + ' at the destination, ' + $f.size + ' in the stage')
    }
    if ($sha -ne [string]$f.sha256) {
        $mismatches.Add($f.path + ': sha256 differs between the stage and the destination')
    }
    $manifestFiles.Add([ordered]@{
        path   = $f.path
        size   = $size
        sha256 = $sha
    })
}
if ($mismatches.Count -gt 0) {
    foreach ($m in $mismatches) { Write-YwkLog -Level 'FAIL' -Message $m }
    throw ($mismatches.Count + ' file(s) did not survive the copy intact. MANIFEST.json was NOT written, so the handoff folder is not blessed.')
}
Write-YwkLog -Message ('all ' + $manifestFiles.Count + ' file(s) match between the stage and ' + $Destination)

# --------------------------------------------------------------------------- (e) MANIFEST.json

function Write-YwkAsciiJsonFile {
    <#
      .SYNOPSIS
        Write an object as PURE ASCII JSON (every non-ASCII character as \uXXXX), LF, no BOM.
      .DESCRIPTION
        MANIFEST.json carries repo.head_line, which is a git subject line and on this project
        is Japanese. Written as UTF-8 it is read on the RTX machine by Windows PowerShell 5.1
        on a ja-JP install, where Get-Content -Raw decodes with the ANSI code page (CP932): the
        Japanese comes back mangled and the mangling can eat a closing quote, which kills
        ConvertFrom-Json outright. probe/cuda_bench.py already writes its measurement JSON with
        ensure_ascii=True for exactly this reason; this is the same defence for the one file the
        remote seat must read before anything else exists.

        With every byte ASCII, CP932, UTF-8 and UTF-16 all decode the file to the same text, so
        it no longer matters whether the reader remembered -Encoding UTF8.

        Only characters above 126 are escaped. Control characters inside strings are already
        escaped by ConvertTo-Json, and the newlines BETWEEN tokens are structure, not content.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value,
        [int]$Depth = 12
    )
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrEmpty($dir)) {
        $null = New-YwkDirectory -Path $dir
    }
    $json = ($Value | ConvertTo-Json -Depth $Depth)
    $json = $json -replace "`r`n", "`n"
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $json.ToCharArray()) {
        $code = [int][char]$ch
        if ($code -gt 126) {
            $null = $sb.Append('\u')
            $null = $sb.Append($code.ToString('x4'))
        } else {
            $null = $sb.Append([string]$ch)
        }
    }
    $text = $sb.ToString()
    if (-not $text.EndsWith("`n")) { $text = $text + "`n" }
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $text, $enc)
    return $Path
}

$manifest = [ordered]@{
    schema        = 1
    tool          = 'build/make-handoff.ps1'
    purpose       = 'RTX 3090 handoff kit for seat B (docs/design/ben-b-rtx-remote.md). Read runbook/rtx-remote-runbook.md first.'
    generated_utc = $BuiltAt
    generated_local = $BuiltAtLocal
    generated_note = 'generated_utc is the one timestamp of this run; README.txt prints the same value, and generated_local is that same instant in the building machine''s time zone.'
    built_on      = [ordered]@{
        computer_name = $env:COMPUTERNAME
        powershell    = $PSVersionTable.PSVersion.ToString()
        git           = (Invoke-YwkGit -Arguments @('--version')).StdOut.Trim()
    }
    repo          = [ordered]@{
        head            = $headSha
        branch          = $headBranch
        head_line       = $headLine
        working_tree_clean = ($statusLines.Count -eq 0)
        working_tree_status = $statusLines
        note            = 'repo.bundle carries only COMMITTED history. Read working_tree_status two ways, by the two-character code at the head of each line: ?? means untracked = the file is NOT in the bundle at all (probe_loose carries the ones that matter); any other code (M, A, D, R, ...) means the file IS tracked and IS in the bundle, but at its last COMMITTED content, which is OLDER than this machine''s copy - the clone will hand you that older version silently. verify-handoff.ps1 prints the same split.'
        submodules      = $subFacts.ToArray()
    }
    destination   = $Destination
    stage         = $StageRoot
    cache_selection = [ordered]@{
        policy   = 'files named by ledger/runtime-{cu130,cu126}.json + python-embed.json + vc_redist.json that are already in build/cache, minus torch and torchaudio'
        source   = $CacheRoot
        ledgers  = $ledgerFiles.ToArray()
        include_all_cache = [bool]$IncludeAllCache
        copied   = $copiedCache.Count
        skipped  = $skippedCache.ToArray()
        not_in_cache_rtx_will_fetch = $missingFromCache.ToArray()
    }
    uv            = [ordered]@{
        source  = $UvSource
        skipped = [bool]$SkipUv
        files   = $uvFiles
    }
    probe_loose   = [ordered]@{
        note  = 'probe\ holds the bench tools loose beside repo.bundle. in_bundle=false means the clone will NOT have that file and it must be copied from here (runbook step 1-7).'
        files = $probeShipped.ToArray()
    }
    verification  = 'Hash every file under this folder with SHA256 and compare with files[].sha256. The runbook step 1-3 does exactly that.'
    totals        = [ordered]@{ files = $manifestFiles.Count; bytes = $totalBytes }
    files         = $manifestFiles.ToArray()
}
$manifestPath = Join-Path $Destination 'MANIFEST.json'
$null = Write-YwkAsciiJsonFile -Path $manifestPath -Value $manifest -Depth 8
$null = Write-YwkAsciiJsonFile -Path (Join-Path $StageRoot 'MANIFEST.json') -Value $manifest -Depth 8
Write-YwkLog -Message ('wrote ' + $manifestPath + ' (pure ASCII: every non-ASCII character as \uXXXX)')
# Prove it rather than assume it: one non-ASCII byte here is the failure this escaping exists
# to prevent, and it would only be discovered on the RTX machine.
$manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
$nonAscii = 0
foreach ($b in $manifestBytes) {
    if ([int]$b -gt 127) { $nonAscii = $nonAscii + 1 }
}
if ($nonAscii -gt 0) {
    throw ('MANIFEST.json has ' + $nonAscii + ' non-ASCII byte(s) after escaping; the ja-JP CP932 reader on the RTX machine would mangle it.')
}
Write-YwkLog -Message ('MANIFEST.json is ' + $manifestBytes.Length + ' B, 0 non-ASCII bytes')

Write-YwkLog -Level 'STEP' -Message ('handoff ready: ' + $manifestFiles.Count + ' file(s), ' + $totalBytes + ' B, repo HEAD ' + $headSha)
exit 0
