# installer-build.ps1 -- the installer production line (convoy E).
#
# ASCII only. CRLF. Must parse and run on Windows PowerShell 5.1 and PowerShell 7+.
# No ternary operator, no null-coalescing, no '&&' inside this file.
# (The house rule is release-build.ps1:3-4. Japanese lives in installer/irodori-tts-ywk.iss only.)
#
# Design  : docs/design/ben-e-installer.md section 3 (gate A, gate B), section 10-3 (I-1..I-9),
#           section 16 (A-7 was added by the corrective seat; design 3-2 still says "gate A 5").
# Canon   : decisions.md 8 (no third party bytes), 22, 37 (the 11 wavs are OUR output), 46,
#           88 (5), 89, 90 (two flavours, the two AppId GUIDs), 91 (seven /D switches).
#
# Stages (the order has reasons; see the design doc section 3-1):
#   0  the version, read from the ONE place that defines it (launcher/Directory.Build.props).
#      AppDisplayVersion carries a leading 'v'; the .iss wants both forms, so both are made here.
#   1  build/assemble-app.ps1            (skipped by -SkipAssemble) -> build/out/app
#   2  build/release-build.ps1 -SkipZip  (skipped by -SkipPublish)  -> build/out/launcher/win-x64
#   3  GATE A (7 checks) on the distributable tree, BEFORE ISCC
#   4  ISCC.exe with seven /D switches and /O
#   5  GATE B (3 checks) on the setup that came out
#   6  sha256 + bytes into build/out/installer/SHA256SUMS.txt and build/out/installer-log/*.log
#
# The exit code is THE NUMBER OF FAILED GATES (0 = all passed). With -All the two flavours are
# built in one run and the failures of both are summed (at most 2 x 10 = 20). A missing compiler or
# a missing input throws instead: that is not a gate, it is an unusable workshop -- and a throw
# leaves EXIT 64, never a gate count, so "exit 1" can only ever mean "one gate failed".
# Measured 2026-09-05 (the defect this rule closes): making SHA256SUMS.txt read only made the run
# die inside gate B-3 with EXIT=1 and no DONE line and no JSON report, which read exactly like one
# failed gate. The trap below also writes the report it got to, with died=true in it.
#
# build/out/ is not tracked (decisions.md 22 / build/check-tree.ps1): nothing here is committed.
#
# Usage:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File build/installer-build.ps1
#   pwsh ... -File build/installer-build.ps1 -Flavor radeon
#   pwsh ... -File build/installer-build.ps1 -All
#   pwsh ... -File build/installer-build.ps1 -All -SkipAssemble -SkipPublish   (wrap what is there)

[CmdletBinding()]
param(
    [ValidateSet('cuda', 'radeon')][string]$Flavor = 'cuda',
    [switch]$All,
    [switch]$SkipAssemble,
    [switch]$SkipPublish,
    [string]$OutDir = '',
    [string]$IsccPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# EXIT 64 = "this run died", as opposed to "n gates failed" (see the contract above). It is set
# before Common.ps1 is dot-sourced on purpose: a missing Common.ps1 is exactly the kind of broken
# workshop this code has to be distinguishable from a broken artefact.
$script:DiedExitCode = 64
trap {
    $msg = 'installer-build DIED (not a gate): ' + $_.Exception.Message
    try { Write-YwkLog -Level 'FAIL' -Message $msg } catch { Write-Host ('[FAIL] ' + $msg) }
    try { Write-Host ('  at ' + ($_.ScriptStackTrace -split "`r?`n")[0]) } catch { }
    # Leave the report a reader can open. Without it, a death looks like a run that never happened.
    try {
        if ((Test-Path -LiteralPath $logDir) -and (-not [string]::IsNullOrEmpty($runStamp))) {
            $diedPath = Join-Path $logDir ('installer-build-' + $runStamp + '.json')
            $null = Write-YwkJsonFile -Path $diedPath -Depth 8 -Value ([ordered]@{
                generated    = Get-YwkTimestamp
                died         = $true
                error        = [string]$_.Exception.Message
                gates        = $script:Gates.ToArray()
                failed_gates = $script:GateFailures
            })
            Write-Host ('  report (partial) = ' + $diedPath)
        }
    } catch { }
    Write-Host ('  EXIT ' + $script:DiedExitCode + ' = the run DIED. build/out/installer may hold a setup ' +
        'this run never recorded a sha256 for: nothing there can be shipped until a run finishes.')
    exit $script:DiedExitCode
}

. (Join-Path $PSScriptRoot 'Common.ps1')

# ------------------------------------------------------------------ constants (all measured)

# Distributable tree, measured 2026-09-05 by this seat, through this line (assemble-app then this
# scan).
#
# THE COUNT IS A FAILURE, THE BYTES ARE A WARN, and the difference is the point:
#   - a file count that moved means a file APPEARED or VANISHED. Vanishing is the accident this
#     gate exists for (see $RequiredAppFiles below), and appearing is something a human should
#     acknowledge by editing this line. Measured 2026-09-05 11:11 by holding back two wrapper files:
#     the run went through all nine gates and wrote a sha256 for a setup that cannot work, because
#     the count mismatch was only a WARN.
#   - the total bytes move for honest reasons all the time (one sentence in ledger/README.md moved
#     them by 555 B on 2026-09-05), so they are recorded and reported, not enforced.
#
# ONE byte figure, again, since 2026-09-05 11:2x: build/assemble-app.ps1 used to write
# voices/voices.json and voices/voices.ywk.json through ConvertTo-Json, which pretty-prints
# differently on the two hosts (5.1: 80 B / 596 B -> 37,111,715 B; 7.6.5: 50 B / 345 B ->
# 37,111,434 B), so the same sources gave two different trees and the setup.exe did not reproduce
# across hosts (design 14-3 / 14-4). Those two files are now written as literal text, and both hosts
# were measured writing 50 B / 345 B with the same sha256, tree = 108 files / 37,111,434 B.
$ExpectedAppFiles = 108
$ExpectedAppBytes = @([int64]37111434)

# Gate A-7. Files named ONE BY ONE, because their absence produces the worst artefact this line can
# make: a setup that installs, exits 0 and then does not work. Nothing else in gate A sees them --
# A-1 only counts, A-4 only judges extensions, and the .iss names exactly one file of server\
# (irodori-tts-ywk.iss, the "#if !FileExists(SrcApp + \"\\server\\ywk_server.py\")" gate).
# The reasons are quoted, not assumed:
#   python312._pth.template  AppPaths.cs:92 'public string PthTemplatePath => Path.Combine(ServerDir,
#                            "python312._pth.template");' -- without it the installed runtime cannot
#                            write its python312._pth and not one import path exists.
#   ywk_fetch_models.py      Ledger.cs:280 runs it as a child process of the variant's python.exe;
#                            without it no model is ever fetched.
#   ywk_server.py            the server the launcher starts.
#   ywk_params.py            imported by ywk_server.py.
# build/assemble-app.ps1 writes the same four names into build/out/assemble-log/assemble-app.json as
# "wrapper_files"; that file is read below as a SECOND sheet, so a wrapper file added there and not
# added here is still caught.
$RequiredAppFiles = @(
    'server\python312._pth.template',
    'server\ywk_fetch_models.py',
    'server\ywk_params.py',
    'server\ywk_server.py',
    'voices\presets.json',
    'licenses\first-run-notices.md'
)

# decisions.md 37 verbatim: "the project's own output, the assets that ship with the distributable
# (11 of them, 32 MB)"; decision 88 (5) asks for the same check. The .iss counts them at compile
# time too (installer/irodori-tts-ywk.iss, the ISPP FindFirst/FindNext gate) -- this is the second
# sheet of a two-sheet net, on purpose.
$ExpectedPresetWavs = 11

# Gate A-4. A WHITELIST, not the $forbidden blacklist of release-build.ps1:187-191: that list bans
# '*.wav' and 'voices.json', and the distributable carries both LEGITIMATELY (the 11 preset wavs are
# ours, decisions.md 37; voices.ywk.json is read by PresetVoices.cs:54). A blacklist also cannot see
# a NEW shape of third-party binary. Extensions measured over the whole tree on 2026-09-05.
$AllowedExtensions = @(
    '.py', '.yaml', '.json', '.md', '.txt', '.toml', '.lock', '.template', '.example', '.wav'
)
# Files with no extension at all, and dotfiles (whose whole name .NET reports as the extension).
$AllowedFileNames = @(
    'LICENSE', 'Dockerfile', '.gitignore', '.gitkeep', '.python-version', '.dockerignore'
)

# Gate A-5. One file may not exceed 4 MiB. The largest legitimate file in the tree is
# voices/presets/vr2_tsukuyomi_ai.wav at 3,433,004 B (3.27 MiB), so the cap has 0.7 MiB of room.
# Two exemptions are named, not inferred:
#   - the 11 preset wavs under voices\presets\  (decisions.md 37 -- our own output, not third party)
#   - build/out/launcher/win-x64/IrodoriTtsYwk.Launcher.exe (69.6 MB, self contained .NET =
#     decision 51; it is outside this tree, so it is never scanned -- named here for the reader)
$MaxFileBytes = [int64]4194304
$SizeExemptDirRelative = 'voices\presets'
$SizeExemptExtension = '.wav'
$SizeExemptExeName = 'IrodoriTtsYwk.Launcher.exe'

# Gate B-2. The band, CONFIRMED by measurement, not guessed. Four samples on 2026-09-05: the .iss
# seat's two hand-driven ISCC runs (cuda 84,984,627 B / radeon 84,999,119 B) and this seat's two
# runs through the whole line (cuda 84,986,197 B / radeon 85,000,689 B = 81.05 and 81.06 MiB).
# +/-5 % of that mean is 80,743,771-89,243,115 B, so the band below (77.0-85.0 MiB) is that band
# rounded to whole MiB. It is the last net that stops an empty setup (the .iss seat measured
# 2,096,479 B for one) from being shipped.
$SetupMinBytes = [int64]80740352   # 77.0 MiB
$SetupMaxBytes = [int64]89128960   # 85.0 MiB

# Ledgers. The .iss names them one by one per flavour (it cannot use a wildcard: a rocm ledger in a
# CUDA install makes ReleaseFlavor.cs:32-34 report "Radeon" and cu130/cu126 vanish from the UI).
$LedgerShared = @('README.md', 'models.json', 'python-embed.json', 'vc_redist.json')
$LedgerKnownRuntimes = @('runtime-cpu.json', 'runtime-cu126.json', 'runtime-cu130.json', 'runtime-rocm-gfx1151.json')

# ------------------------------------------------------------------ helpers

function Get-YwkFlavorRuntimeLedgers {
    <#
      .SYNOPSIS
        The runtime ledgers this flavour ships, in the order the .iss names them.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Name)
    if ($Name -eq 'radeon') {
        return @('runtime-rocm-gfx1151.json', 'runtime-cpu.json')
    }
    return @('runtime-cu130.json', 'runtime-cu126.json', 'runtime-cpu.json')
}

function Get-YwkPowerShellHost {
    <#
      .SYNOPSIS
        The interpreter this script is running under, so the child scripts run under the same one.
      .DESCRIPTION
        assemble-app.ps1 and release-build.ps1 end with "exit <n>"; they have to be children, not
        dot-sourced, or their exit would end this run. Running them under the SAME host is what
        makes acceptance condition I-9 (both 5.1 and 7) mean anything.
    #>
    [CmdletBinding()]
    param()
    $exe = 'powershell.exe'
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $exe = 'pwsh.exe'
    }
    $candidate = Join-Path $PSHOME $exe
    if (Test-Path -LiteralPath $candidate) {
        return $candidate
    }
    $cmd = Get-Command $exe -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        return $cmd.Source
    }
    throw ('cannot find the PowerShell host to run the child scripts with (' + $exe + ').')
}

function Get-YwkIsccPath {
    <#
      .SYNOPSIS
        ISCC.exe. Inno Setup 6.7.3 is a per-user install on this machine (decisions.md 89).
    #>
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Override = '')
    if (-not [string]::IsNullOrEmpty($Override)) {
        if (Test-Path -LiteralPath $Override) {
            return (Resolve-Path -LiteralPath $Override).Path
        }
        throw ('-IsccPath does not exist: ' + $Override)
    }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) {
            return (Resolve-Path -LiteralPath $c).Path
        }
    }
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        return $cmd.Source
    }
    throw ('ISCC.exe was not found. Install Inno Setup 6.7.3, or pass -IsccPath. Looked in: ' +
        ($candidates -join ' ; '))
}

function Remove-YwkSumsLine {
    <#
      .SYNOPSIS
        Drop every line of SHA256SUMS.txt that names one artefact. Returns how many went.
      .DESCRIPTION
        SHA256SUMS.txt is the only thing a user can check a signature-less setup against, so a line
        in it is a claim that the file exists and passed. When a build stops, the claim has to go.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SumsPath,
        [Parameter(Mandatory = $true)][string]$Name
    )
    if (-not (Test-Path -LiteralPath $SumsPath)) {
        return 0
    }
    $keep = New-Object System.Collections.Generic.List[string]
    $dropped = 0
    foreach ($line in @(Get-Content -LiteralPath $SumsPath -Encoding UTF8)) {
        $t = $line.Trim()
        if ($t.Length -eq 0) { continue }
        if ($t.EndsWith(' ' + $Name)) {
            $dropped = $dropped + 1
            continue
        }
        $keep.Add($t)
    }
    if ($keep.Count -eq 0) {
        # Nothing is left to claim. A 0 byte SHA256SUMS.txt still LOOKS like the file the user is
        # told to check against, so the file goes instead of being emptied.
        Remove-Item -LiteralPath $SumsPath -Force -ErrorAction SilentlyContinue
        return $dropped
    }
    $text = ((@($keep | Sort-Object) -join "`n") + "`n")
    $null = Write-YwkTextFile -Path $SumsPath -Newline 'LF' -Text $text
    return $dropped
}

function Get-YwkAppTreeFiles {
    <#
      .SYNOPSIS
        Every file of the distributable tree, with its path relative to the root.
      .DESCRIPTION
        __pycache__ directories and *.pyc files are left OUT of the scan by ruling: assemble-app.ps1
        sweeps them off the output side (its step 4c), and the .iss excludes them again in [Files].
        They appear in build/out/app only when someone RUNS the server out of the tree, which is a
        development act, not a property of the build -- failing gate A-4 on them would stop a build
        that is in fact clean.
      .OUTPUTS
        @{ Files = <hashtable[]>; Skipped = <int> }
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root)
    $rootFull = (Resolve-Path -LiteralPath $Root).Path
    $prefix = $rootFull.TrimEnd('\') + '\'
    $out = New-Object System.Collections.Generic.List[object]
    $skipped = 0
    foreach ($f in @(Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force)) {
        $rel = $f.FullName.Substring($prefix.Length)
        $drop = $false
        foreach ($part in ($rel -split '\\')) {
            if ($part -eq '__pycache__') { $drop = $true; break }
        }
        if (-not $drop) {
            if ($f.Extension.ToLowerInvariant() -eq '.pyc') { $drop = $true }
        }
        if ($drop) {
            $skipped = $skipped + 1
            continue
        }
        $out.Add(@{
            Rel    = $rel
            Full   = $f.FullName
            Name   = $f.Name
            Bytes  = [int64]$f.Length
            Ext    = $f.Extension.ToLowerInvariant()
        })
    }
    return @{ Files = $out.ToArray(); Skipped = $skipped }
}

# ------------------------------------------------------------------ paths and log

$repo    = Get-YwkRepoRoot
$srcApp  = Join-Path $repo 'build\out\app'
$srcExe  = Join-Path $repo 'build\out\launcher\win-x64'
$issPath = Join-Path $repo 'installer\irodori-tts-ywk.iss'
$props   = Join-Path $repo 'launcher\Directory.Build.props'
$exeName = 'IrodoriTtsYwk.Launcher.exe'

if ([string]::IsNullOrEmpty($OutDir)) {
    $OutDir = Join-Path $repo 'build\out\installer'
}
$logDir = Join-Path $repo 'build\out\installer-log'
$null = New-YwkDirectory -Path $OutDir
$null = New-YwkDirectory -Path $logDir

$runStamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$null = Start-YwkLog -Path (Join-Path $logDir ('installer-build-' + $runStamp + '.log'))

$script:Gates = New-Object System.Collections.Generic.List[object]
$script:GateFailures = 0

function Add-YwkGateResult {
    <#
      .SYNOPSIS
        Record one gate. Every failure adds 1 to the exit code.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FlavorName,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][bool]$Ok,
        [AllowEmptyString()][string]$Detail = ''
    )
    $script:Gates.Add([ordered]@{
        flavor = $FlavorName
        gate   = $Id
        title  = $Title
        ok     = $Ok
        detail = $Detail
    })
    if ($Ok) {
        Write-YwkLog -Message ('OK   ' + $FlavorName + ' ' + $Id + ' ' + $Title + ' -- ' + $Detail)
    } else {
        $script:GateFailures = $script:GateFailures + 1
        Write-YwkLog -Level 'FAIL' -Message ('NG   ' + $FlavorName + ' ' + $Id + ' ' + $Title + ' -- ' + $Detail)
    }
}

function Invoke-YwkChildScript {
    <#
      .SYNOPSIS
        Run one of the sibling build scripts as a child process and log its tail.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$HostExe,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [string[]]$ScriptArguments = @(),
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )
    $argv = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $ScriptArguments
    Write-YwkLog -Message ('run: ' + $HostExe + ' ' + ($argv -join ' '))
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-YwkNative -FilePath $HostExe -Arguments $argv -WorkingDirectory $WorkingDirectory
    $sw.Stop()
    $text = ($r.StdOut + "`n" + $r.StdErr)
    $lines = @($text -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
    $tail = @($lines | Select-Object -Last 12)
    foreach ($l in $tail) {
        Write-YwkLog -Message ('  | ' + $l)
    }
    Write-YwkLog -Message ('exit ' + $r.ExitCode + ' after ' +
        [math]::Round($sw.Elapsed.TotalSeconds, 2) + ' s (' + $lines.Count + ' output line(s))')
    return $r.ExitCode
}

Write-YwkLog -Level 'STEP' -Message ('installer-build: repo = ' + $repo)

foreach ($need in @($issPath, $props)) {
    if (-not (Test-Path -LiteralPath $need)) {
        throw ('a required input is missing: ' + $need)
    }
}

$flavors = @($Flavor)
if ($All) {
    $flavors = @('cuda', 'radeon')
}
Write-YwkLog -Message ('flavours = ' + ($flavors -join ', ') + ' ; out = ' + $OutDir)

# ------------------------------------------------------------------ 0. version

Write-YwkLog -Level 'STEP' -Message 'STEP 0: version (one definition, launcher/Directory.Build.props)'

$versionHits = @(Select-String -Path $props -Pattern '<AppDisplayVersion>(.+)</AppDisplayVersion>')
if ($versionHits.Count -ne 1) {
    throw ('launcher/Directory.Build.props has ' + $versionHits.Count +
        ' AppDisplayVersion tags (expected exactly 1 -- the version has one definition).')
}
$appVersion = $versionHits[0].Matches[0].Groups[1].Value
# The trap: AppDisplayVersion is 'v0.1.0' (Directory.Build.props:31). [Setup] AppVersion= and
# VersionInfoVersion= take digits only, so the .iss is handed BOTH forms as separate /D switches.
$appVersionNumeric = $appVersion.TrimStart('v')
Write-YwkLog -Message ('AppVersion = ' + $appVersion + ' / AppVersionNumeric = ' + $appVersionNumeric)

$hostExe = Get-YwkPowerShellHost
Write-YwkLog -Message ('child host = ' + $hostExe + ' (PSVersion ' + $PSVersionTable.PSVersion.ToString() + ')')

# ------------------------------------------------------------------ 1. assemble-app

if ($SkipAssemble) {
    Write-YwkLog -Level 'STEP' -Message 'STEP 1 skipped (-SkipAssemble): using build/out/app as it stands'
} else {
    Write-YwkLog -Level 'STEP' -Message 'STEP 1: build/assemble-app.ps1'
    $code = Invoke-YwkChildScript -HostExe $hostExe -ScriptPath (Join-Path $repo 'build\assemble-app.ps1') `
        -WorkingDirectory $repo
    if ($code -ne 0) {
        throw ('assemble-app.ps1 failed with exit code ' + $code + '; nothing was compiled.')
    }
}
if (-not (Test-Path -LiteralPath $srcApp)) {
    throw ('the distributable tree is missing: ' + $srcApp + ' (run without -SkipAssemble).')
}

# ------------------------------------------------------------------ 2. release-build -SkipZip

if ($SkipPublish) {
    Write-YwkLog -Level 'STEP' -Message 'STEP 2 skipped (-SkipPublish): using build/out/launcher/win-x64 as it stands'
} else {
    Write-YwkLog -Level 'STEP' -Message 'STEP 2: build/release-build.ps1 -SkipZip'
    # -SkipZip on purpose: the zip is the launcher-only artefact, and the installer is what we are
    # making here. The upstream pins are baked INTO the exe by this run (release-build.ps1:220), so
    # the installer never passes a pin of its own -- one canon, not two.
    $code = Invoke-YwkChildScript -HostExe $hostExe -ScriptPath (Join-Path $repo 'build\release-build.ps1') `
        -ScriptArguments @('-SkipZip') -WorkingDirectory $repo
    if ($code -ne 0) {
        throw ('release-build.ps1 failed with exit code ' + $code + '; nothing was compiled.')
    }
}
$exePath = Join-Path $srcExe $exeName
if (-not (Test-Path -LiteralPath $exePath)) {
    throw ('the launcher exe is missing: ' + $exePath + ' (run without -SkipPublish).')
}

# ------------------------------------------------------------------ tree scan (shared by gate A)

$scan = Get-YwkAppTreeFiles -Root $srcApp
$appFiles = @($scan.Files)
$appBytes = [int64]0
foreach ($f in $appFiles) {
    $appBytes = $appBytes + $f.Bytes
}
Write-YwkLog -Message ('distributable tree: ' + $appFiles.Count + ' file(s), ' + $appBytes + ' B (' +
    [math]::Round($appBytes / 1MB, 2) + ' MiB); ' + $scan.Skipped + ' __pycache__/.pyc file(s) left out of the scan')

$iscc = Get-YwkIsccPath -Override $IsccPath
Write-YwkLog -Message ('ISCC = ' + $iscc)

$sumsPath = Join-Path $OutDir 'SHA256SUMS.txt'
$built = New-Object System.Collections.Generic.List[object]

# ==================================================================== per flavour

foreach ($fl in $flavors) {

Write-YwkLog -Level 'STEP' -Message ('===== ' + $fl + ' =====')

$wantRuntimes = @(Get-YwkFlavorRuntimeLedgers -Name $fl)
$foreignRuntimes = @($LedgerKnownRuntimes | Where-Object { $wantRuntimes -notcontains $_ })
$failuresBefore = $script:GateFailures

$setupName = 'irodori-tts-ywk-setup-' + $appVersion + '-' + $fl + '.exe'
$setupPath = Join-Path $OutDir $setupName
if (Test-Path -LiteralPath $setupPath) {
    # Cleared BEFORE gate A, not just before ISCC. release-build.ps1's house rule is "a failed
    # inspection writes no archive"; the same has to hold here, or a run that stops at gate A
    # leaves yesterday's setup sitting in build/out/installer looking like today's.
    Remove-Item -LiteralPath $setupPath -Force
}

# ------------------------------------------------------------------ GATE A-1: count and bytes

$detail = $appFiles.Count.ToString() + ' file(s), ' + $appBytes + ' B (expected ' +
    $ExpectedAppFiles + ' / ' + ($ExpectedAppBytes -join ' or ') + ')'
if ($appFiles.Count -eq 0) {
    Add-YwkGateResult -FlavorName $fl -Id 'A-1' -Title 'distributable tree has the recorded file count' -Ok $false `
        -Detail ('build/out/app has no files at all: ' + $srcApp)
} else {
    if (-not ($ExpectedAppBytes -contains $appBytes)) {
        # The bytes are a WARN: they move when a licence or a ledger sentence moves.
        Write-YwkLog -Level 'WARN' -Message ('A-1 the tree bytes drifted from the recorded figure: ' + $detail)
    }
    $a1ok = ($appFiles.Count -eq $ExpectedAppFiles)
    $a1detail = $detail
    if (-not $a1ok) {
        $verb = 'lost'
        if ($appFiles.Count -gt $ExpectedAppFiles) { $verb = 'gained' }
        $a1detail = 'the tree ' + $verb + ' ' + [math]::Abs($appFiles.Count - $ExpectedAppFiles) +
            ' file(s): ' + $detail + '. Find out WHICH file (gate A-7 names the load-bearing ones), ' +
            'then either fix the tree or move $ExpectedAppFiles in build/installer-build.ps1 on purpose'
    }
    Add-YwkGateResult -FlavorName $fl -Id 'A-1' -Title 'distributable tree has the recorded file count' -Ok $a1ok -Detail $a1detail
}

# ------------------------------------------------------------------ GATE A-2: the 11 preset wavs

$presetDir = Join-Path $srcApp 'voices\presets'
$presetJson = Join-Path $srcApp 'voices\presets.json'
$presetWavs = @()
if (Test-Path -LiteralPath $presetDir) {
    $presetWavs = @(Get-ChildItem -LiteralPath $presetDir -File -Filter '*.wav' | Sort-Object Name)
}
$presetJsonOk = $false
if (Test-Path -LiteralPath $presetJson) {
    $presetJsonOk = ((Get-Item -LiteralPath $presetJson).Length -gt 0)
}
$a2ok = (($presetWavs.Count -eq $ExpectedPresetWavs) -and $presetJsonOk)
$a2detail = 'voices\presets\*.wav = ' + $presetWavs.Count + ' (expected ' + $ExpectedPresetWavs +
    '), voices\presets.json present = ' + $presetJsonOk
Add-YwkGateResult -FlavorName $fl -Id 'A-2' -Title 'preset speakers (decisions.md 37 / 88 (5))' -Ok $a2ok -Detail $a2detail

# ------------------------------------------------------------------ GATE A-3: the ledger set

# Two things, because both are ways a flavour goes wrong:
#   (1) the notice this flavour must ship (decisions.md 46) and every ledger the .iss names for it
#       are on disk -- a missing one would make ISCC abort, but with a message about a path, not
#       about a release;
#   (2) NO runtime ledger exists that the flavour rule does not know. A new runtime-*.json (say a
#       cu128) that nobody wired into installer/irodori-tts-ywk.iss would silently ship in NEITHER
#       flavour, or, wired wrong, mix a rocm ledger into a CUDA install -- and one stray
#       runtime-rocm-*.json in {app}\ledger is enough for ReleaseFlavor.cs:32-34 to call the whole
#       installation "Radeon" and take cu130/cu126 out of the UI. What actually SHIPPED is proved
#       from the compile log in gate B-1.
$a3problems = New-Object System.Collections.Generic.List[string]

$notice = Join-Path $srcApp 'licenses\first-run-notices.md'
if (-not (Test-Path -LiteralPath $notice)) {
    $a3problems.Add('licenses/first-run-notices.md is missing (decisions.md 46)')
} elseif ((Get-Item -LiteralPath $notice).Length -eq 0) {
    $a3problems.Add('licenses/first-run-notices.md is empty (decisions.md 46)')
}

$ledgerDir = Join-Path $srcApp 'ledger'
$ledgerNames = @()
if (Test-Path -LiteralPath $ledgerDir) {
    $ledgerNames = @(Get-ChildItem -LiteralPath $ledgerDir -File | ForEach-Object { $_.Name })
} else {
    $a3problems.Add('build/out/app/ledger is missing')
}
foreach ($need in ($LedgerShared + $wantRuntimes)) {
    if ($ledgerNames -notcontains $need) {
        $a3problems.Add('ledger/' + $need + ' is missing (this is the ' + $fl + ' release)')
    }
}
foreach ($name in $ledgerNames) {
    if ($name -like 'runtime-*.json') {
        if ($LedgerKnownRuntimes -notcontains $name) {
            $a3problems.Add('ledger/' + $name + ' is a runtime ledger the flavour rule does not know; ' +
                'wire it into installer/irodori-tts-ywk.iss and into $LedgerKnownRuntimes before it can ship')
        }
    }
}
$a3ok = ($a3problems.Count -eq 0)
$a3detail = 'ships ' + (($LedgerShared + $wantRuntimes) -join ' ') + ' ; must not ship ' +
    ($foreignRuntimes -join ' ')
if (-not $a3ok) {
    $a3detail = ($a3problems -join ' ; ')
}
Add-YwkGateResult -FlavorName $fl -Id 'A-3' -Title 'notice + the ledger set of this flavour' -Ok $a3ok -Detail $a3detail

# ------------------------------------------------------------------ GATE A-4: extension whitelist

$a4bad = New-Object System.Collections.Generic.List[string]
foreach ($f in $appFiles) {
    $ok = ($AllowedExtensions -contains $f.Ext)
    if (-not $ok) {
        foreach ($n in $AllowedFileNames) {
            if ($f.Name -eq $n) { $ok = $true; break }
        }
    }
    if (-not $ok) {
        $a4bad.Add($f.Rel + ' (' + $f.Bytes + ' B)')
    }
}
$a4ok = ($a4bad.Count -eq 0)
$a4detail = 'scanned ' + $appFiles.Count + ' file(s) against ' + $AllowedExtensions.Count +
    ' extension(s) + ' + $AllowedFileNames.Count + ' named file(s); 0 outside'
if ($appFiles.Count -eq 0) {
    # Honesty: a whitelist over nothing is not a pass, it is a gate that saw nothing. A-1 has already
    # failed and the run will stop, but the log is read top to bottom by people.
    $a4detail = $a4detail + ' -- THE TREE IS EMPTY, this gate inspected nothing (A-1 failed)'
}
if (-not $a4ok) {
    $shown = @($a4bad | Select-Object -First 10)
    $a4detail = $a4bad.Count.ToString() + ' file(s) are not on the whitelist: ' + ($shown -join ' ; ')
}
Add-YwkGateResult -FlavorName $fl -Id 'A-4' -Title 'no third party binary (extension whitelist, decisions.md 8)' -Ok $a4ok -Detail $a4detail

# ------------------------------------------------------------------ GATE A-5: 4 MiB per file

$a5bad = New-Object System.Collections.Generic.List[string]
$exempt = New-Object System.Collections.Generic.List[string]
$largestRel = ''
$largestBytes = [int64]0
foreach ($f in $appFiles) {
    $isExempt = $false
    if ($f.Ext -eq $SizeExemptExtension) {
        $dir = Split-Path -Parent $f.Rel
        if ($dir -eq $SizeExemptDirRelative) { $isExempt = $true }
    }
    if ($isExempt) {
        $exempt.Add($f.Rel)
        continue
    }
    if ($f.Bytes -gt $largestBytes) {
        $largestBytes = $f.Bytes
        $largestRel = $f.Rel
    }
    if ($f.Bytes -gt $MaxFileBytes) {
        $a5bad.Add($f.Rel + ' = ' + $f.Bytes + ' B')
    }
}
$a5ok = ($a5bad.Count -eq 0)
$a5detail = 'cap ' + $MaxFileBytes + ' B; largest non-exempt = ' + $largestRel + ' ' + $largestBytes +
    ' B; ' + $exempt.Count + ' named exemption(s) under ' + $SizeExemptDirRelative + '\ (decisions.md 37), ' +
    'plus ' + $SizeExemptExeName + ' which is outside this tree (decision 51)'
if ($appFiles.Count -eq 0) {
    $a5detail = $a5detail + ' -- THE TREE IS EMPTY, this gate inspected nothing (A-1 failed)'
}
if (-not $a5ok) {
    $a5detail = $a5bad.Count.ToString() + ' file(s) over ' + $MaxFileBytes + ' B: ' + (($a5bad | Select-Object -First 10) -join ' ; ')
}
Add-YwkGateResult -FlavorName $fl -Id 'A-5' -Title 'no file over 4 MiB (the wavs are named exemptions)' -Ok $a5ok -Detail $a5detail

# ------------------------------------------------------------------ GATE A-6: the exe carries this version

# Measured 2026-09-05: ProductVersion returns 'v0.1.0' (the InformationalVersion, leading v and all)
# while FileVersion returns '0.1.0.0'. So this compares against $appVersion, not $appVersionNumeric.
$vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
$productVersion = [string]$vi.ProductVersion
$exeBytes = [int64](Get-Item -LiteralPath $exePath).Length
$a6ok = ($productVersion -eq $appVersion)
$a6detail = $exeName + ' ProductVersion = ' + $productVersion + ' (want ' + $appVersion +
    '), FileVersion = ' + [string]$vi.FileVersion + ', ' + $exeBytes + ' B'
if (-not $a6ok) {
    $a6detail = $a6detail + ' -- a stale exe would be wrapped; publish again'
}
Add-YwkGateResult -FlavorName $fl -Id 'A-6' -Title 'the published exe carries this version' -Ok $a6ok -Detail $a6detail

# ------------------------------------------------------------------ GATE A-7: the named files

# The gate that stops "installs fine, does not work". See $RequiredAppFiles for why each name is on
# the list. Two sheets: the list above, and the wrapper_files array build/assemble-app.ps1 writes.
$a7problems = New-Object System.Collections.Generic.List[string]
$a7names = New-Object System.Collections.Generic.List[string]
foreach ($rel in $RequiredAppFiles) {
    $a7names.Add($rel)
}
$assembleReport = Join-Path $repo 'build\out\assemble-log\assemble-app.json'
$a7second = 'build/out/assemble-log/assemble-app.json is not there (run without -SkipAssemble to get the second sheet)'
if (Test-Path -LiteralPath $assembleReport) {
    $rep = $null
    try {
        $rep = ((Get-Content -LiteralPath $assembleReport -Raw -Encoding UTF8) | ConvertFrom-Json)
    } catch {
        $a7problems.Add('could not read ' + $assembleReport + ': ' + $_.Exception.Message)
    }
    if ($null -ne $rep) {
        $wrapper = @()
        if ($rep.PSObject.Properties.Name -contains 'wrapper_files') {
            $wrapper = @($rep.wrapper_files)
        }
        foreach ($w in $wrapper) {
            $rel = 'server\' + [string]$w
            if (-not $a7names.Contains($rel)) { $a7names.Add($rel) }
        }
        $reportedFiles = -1
        if ($rep.PSObject.Properties.Name -contains 'files') { $reportedFiles = [int]$rep.files }
        $a7second = 'assemble-app.json named ' + $wrapper.Count + ' wrapper file(s), files=' + $reportedFiles
        if ($reportedFiles -ne $appFiles.Count) {
            # Not a failure by itself: -SkipAssemble is a legal way to run, and then this report is
            # from an earlier assemble. It IS a reason to trust the second sheet less, so say so.
            Write-YwkLog -Level 'WARN' -Message ('A-7 assemble-app.json says ' + $reportedFiles +
                ' file(s) but the tree has ' + $appFiles.Count + ': the second sheet is from an earlier assemble')
        }
    }
}
foreach ($rel in $a7names) {
    $full = Join-Path $srcApp $rel
    if (-not (Test-Path -LiteralPath $full)) {
        $a7problems.Add($rel + ' is MISSING from the distributable tree')
    } elseif ((Get-Item -LiteralPath $full).Length -eq 0) {
        $a7problems.Add($rel + ' is present but empty (0 B)')
    }
}
$a7ok = ($a7problems.Count -eq 0)
$a7detail = 'all ' + $a7names.Count + ' named file(s) present and non-empty; ' + $a7second
if (-not $a7ok) {
    $a7detail = $a7problems.Count.ToString() + ' problem(s): ' + (($a7problems | Select-Object -First 10) -join ' ; ')
}
Add-YwkGateResult -FlavorName $fl -Id 'A-7' -Title 'the named load-bearing files are in the tree' -Ok $a7ok -Detail $a7detail

# ------------------------------------------------------------------ 4. ISCC

# Gate A is a STOP, not a report. A tree that failed A-4 or A-5 carries something that must not be
# distributed, and ISCC would compress it into a setup quite happily (the .iss can only see the
# handful of things its own #if gates name). So: no compile, no artefact, no sha256 line.
$aFailed = $script:GateFailures - $failuresBefore
if ($aFailed -gt 0) {
    Write-YwkLog -Level 'FAIL' -Message ('gate A failed ' + $aFailed + ' time(s) for ' + $fl +
        ': ISCC was NOT run and no setup was written. Gate B did not run.')
    $null = Remove-YwkSumsLine -SumsPath $sumsPath -Name $setupName
    $built.Add([ordered]@{
        flavor        = $fl
        setup         = $setupName
        path          = ''
        bytes         = [int64]0
        sha256        = ''
        iscc_exit     = -1
        iscc_seconds  = 0
        compressed    = 0
        iscc_warnings = 0
        app_files     = $appFiles.Count
        app_bytes     = $appBytes
        exe_bytes     = $exeBytes
        stopped_at    = 'gate A'
    })
    continue
}

$isccArgs = @(
    ('/DAppVersion=' + $appVersion),
    ('/DAppVersionNumeric=' + $appVersionNumeric),
    ('/DFlavor=' + $fl),
    ('/DSrcApp=' + $srcApp.TrimEnd('\')),
    ('/DSrcExe=' + $srcExe.TrimEnd('\')),
    ('/DRepo=' + $repo.TrimEnd('\')),
    ('/DOutDir=' + $OutDir.TrimEnd('\')),
    ('/O' + $OutDir.TrimEnd('\')),
    $issPath
)
Write-YwkLog -Level 'STEP' -Message ('STEP 4: ISCC ' + ($isccArgs -join ' '))
$sw = [System.Diagnostics.Stopwatch]::StartNew()
# 'console' decoding: ISCC is a console program, and an error message out of the .iss is Japanese
# in the console code page. PowerShell 7 would otherwise turn every CP932 byte into U+FFFD.
$isccRun = Invoke-YwkNative -FilePath $iscc -Arguments $isccArgs -WorkingDirectory $repo -StdioEncoding 'console'
$sw.Stop()
$isccSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 2)
$isccText = ($isccRun.StdOut + "`n" + $isccRun.StdErr)
$isccLines = @($isccText -split "`r?`n")
$compressing = @($isccLines | Where-Object { $_ -match '^\s*Compressing:' })
# The 'Compressing:' lines carry SOURCE PATHS, and a path is allowed to contain the word warning
# (C:\...\licenses\warning.txt would have counted as one). Count warnings on every OTHER line.
$isccWarnings = @($isccLines | Where-Object { ($_ -notmatch '^\s*Compressing:') -and ($_ -match '(?i)\bwarning\b') })
$isccErrors = @($isccLines | Where-Object { $_ -match '(?i)^\s*Error' })
$successLine = @($isccLines | Where-Object { $_ -match 'Successful compile' })

$isccLogPath = Join-Path $logDir ('iscc-' + $fl + '-' + $runStamp + '.log')
$null = Write-YwkTextFile -Path $isccLogPath -Newline 'CRLF' -Text $isccText
Write-YwkLog -Message ('ISCC exit ' + $isccRun.ExitCode + ' after ' + $isccSeconds + ' s; ' +
    $compressing.Count + ' Compressing: line(s); ' + $isccWarnings.Count + ' warning line(s); log = ' + $isccLogPath)
foreach ($l in @($isccErrors | Select-Object -First 6)) {
    Write-YwkLog -Level 'FAIL' -Message ('  | ' + $l.Trim())
}
foreach ($l in @($successLine | Select-Object -First 1)) {
    Write-YwkLog -Message ('  | ' + $l.Trim())
}

# ------------------------------------------------------------------ GATE B-1: the compile itself

# Three things at once, because they are one question -- "did THIS flavour really come out?":
#   (1) ISCC exited 0 and the setup is on disk;
#   (2) the compile log ships exactly the ledger set of this flavour and none of the other's
#       (this is the "no mixing" proof: it reads what was PUT IN the setup, not what was on disk);
#   (3) no __pycache__ / *.pyc and none of the internal docs (acceptance.md, contract.md) got in.
$b1problems = New-Object System.Collections.Generic.List[string]
if ($isccRun.ExitCode -ne 0) {
    $b1problems.Add('ISCC exited ' + $isccRun.ExitCode + ' (1 = command line, 2 = compile aborted); see ' + $isccLogPath)
}
if (-not (Test-Path -LiteralPath $setupPath)) {
    $b1problems.Add('the setup is not on disk: ' + $setupPath)
}
$shippedLedgers = New-Object System.Collections.Generic.List[string]
foreach ($line in $compressing) {
    $t = $line.Trim()
    if ($t -match '__pycache__') {
        $b1problems.Add('a __pycache__ path was compressed into the setup: ' + $t)
    }
    if ($t -match '(?i)\.pyc$') {
        $b1problems.Add('a .pyc was compressed into the setup: ' + $t)
    }
    if ($t -match '(?i)\\docs\\(acceptance|contract)\.md$') {
        $b1problems.Add('an internal doc was compressed into the setup: ' + $t)
    }
    $m = [regex]::Match($t, '(?i)\\ledger\\([^\\]+)$')
    if ($m.Success) {
        $shippedLedgers.Add($m.Groups[1].Value)
    }
}
if ($isccRun.ExitCode -eq 0) {
    foreach ($name in $foreignRuntimes) {
        if ($shippedLedgers -contains $name) {
            $b1problems.Add('the ledger of the other flavour was shipped: ' + $name)
        }
    }
    foreach ($need in ($LedgerShared + $wantRuntimes)) {
        if ($shippedLedgers -notcontains $need) {
            $b1problems.Add('a ledger of this flavour was NOT shipped: ' + $need)
        }
    }
}
$b1ok = ($b1problems.Count -eq 0)
$b1detail = 'exit 0, ' + $compressing.Count + ' file(s) compressed in ' + $isccSeconds + ' s, ledger = ' +
    (($shippedLedgers | Sort-Object) -join ' ') + ', ' + $isccWarnings.Count + ' warning(s)'
if (-not $b1ok) {
    $b1detail = ($b1problems -join ' ; ')
}
Add-YwkGateResult -FlavorName $fl -Id 'B-1' -Title 'ISCC exit 0, the setup exists, no flavour mixing' -Ok $b1ok -Detail $b1detail

# ------------------------------------------------------------------ GATE B-2: the size band

$setupBytes = [int64]0
if (Test-Path -LiteralPath $setupPath) {
    $setupBytes = [int64](Get-Item -LiteralPath $setupPath).Length
}
$b2ok = (($setupBytes -ge $SetupMinBytes) -and ($setupBytes -le $SetupMaxBytes))
$b2detail = $setupName + ' = ' + $setupBytes + ' B (' + [math]::Round($setupBytes / 1MB, 2) +
    ' MiB); band ' + $SetupMinBytes + '-' + $SetupMaxBytes + ' B (77.0-85.0 MiB)'
Add-YwkGateResult -FlavorName $fl -Id 'B-2' -Title 'the setup is inside the measured size band' -Ok $b2ok -Detail $b2detail

# ------------------------------------------------------------------ GATE B-3: sha256

# There is no code signature (design doc section 8, danger 7), so what the user gets to check with
# is this file. Format: "<sha256>  <name>", the sha256sum convention, LF, one line per artefact.
# The contract is "SHA256SUMS.txt tells the truth": a setup that failed B-1 or B-2 must NOT be in
# it, and any stale line for it goes. So this gate can pass while B-1/B-2 fail -- the exit code
# already counts those, and counting them again here would claim the sums file is wrong when it is
# in fact exactly right.
$b3ok = $false
$setupSha = ''
$b3detail = ''
if ($b1ok -and $b2ok) {
    $setupSha = Get-YwkSha256 -Path $setupPath
    $null = Remove-YwkSumsLine -SumsPath $sumsPath -Name $setupName
    $keep = New-Object System.Collections.Generic.List[string]
    if (Test-Path -LiteralPath $sumsPath) {
        foreach ($line in @(Get-Content -LiteralPath $sumsPath -Encoding UTF8)) {
            $t = $line.Trim()
            if ($t.Length -eq 0) { continue }
            $keep.Add($t)
        }
    }
    $keep.Add($setupSha + '  ' + $setupName)
    $sorted = @($keep | Sort-Object)
    $null = Write-YwkTextFile -Path $sumsPath -Newline 'LF' -Text (($sorted -join "`n") + "`n")
    $back = @(Get-Content -LiteralPath $sumsPath -Encoding UTF8 | Where-Object { $_.Trim().EndsWith(' ' + $setupName) })
    $b3ok = ($back.Count -eq 1)
    Write-YwkLog -Message ('sha256 ' + $setupSha + '  ' + $setupName)
    $b3detail = 'SHA256SUMS.txt has exactly 1 line for this setup (' + $sumsPath + ')'
    if (-not $b3ok) {
        $b3detail = 'could not record the sha256 of ' + $setupName + ' in ' + $sumsPath
    }
} else {
    $dropped = Remove-YwkSumsLine -SumsPath $sumsPath -Name $setupName
    # And the setup itself goes. release-build.ps1:16's house rule is "a failed inspection exits
    # non-zero and writes no zip"; leaving a setup that failed B-1 or B-2 on the shelf breaks it just
    # as badly as leaving a sha256 line for it. Measured 2026-09-05 11:08: a run that failed B-1 on a
    # planted .pyc left 84,986,434 B of shippable-looking setup.exe in build/out/installer, and a run
    # that failed B-2 left 96,413,230 B. Now the shelf carries only what passed.
    $rejectedBytes = [int64]0
    $rejectedGone = $true
    if (Test-Path -LiteralPath $setupPath) {
        $rejectedBytes = [int64](Get-Item -LiteralPath $setupPath).Length
        Remove-Item -LiteralPath $setupPath -Force -ErrorAction SilentlyContinue
        $rejectedGone = (-not (Test-Path -LiteralPath $setupPath))
    }
    $b3ok = $rejectedGone
    $b3detail = 'not recorded on purpose (B-1 or B-2 failed); ' + $dropped +
        ' stale line(s) removed from SHA256SUMS.txt; the rejected setup (' + $rejectedBytes +
        ' B) was deleted from ' + $OutDir
    if (-not $rejectedGone) {
        $b3detail = 'the rejected setup could NOT be deleted and is still on the shelf: ' + $setupPath
    }
}
Add-YwkGateResult -FlavorName $fl -Id 'B-3' -Title 'sha256 recorded in SHA256SUMS.txt' -Ok $b3ok -Detail $b3detail

$onShelf = ''
$stoppedAt = ''
if ($b1ok -and $b2ok) {
    $onShelf = $setupPath
} else {
    $stoppedAt = 'gate B (the setup was deleted, not shipped)'
}
$built.Add([ordered]@{
    flavor        = $fl
    setup         = $setupName
    path          = $onShelf
    bytes         = $setupBytes
    sha256        = $setupSha
    iscc_exit     = $isccRun.ExitCode
    iscc_seconds  = $isccSeconds
    compressed    = $compressing.Count
    iscc_warnings = $isccWarnings.Count
    app_files     = $appFiles.Count
    app_bytes     = $appBytes
    exe_bytes     = $exeBytes
    stopped_at    = $stoppedAt
})

}

# ==================================================================== report

$report = [ordered]@{
    generated          = Get-YwkTimestamp
    app_version        = $appVersion
    app_version_numeric = $appVersionNumeric
    host               = $hostExe
    ps_version         = $PSVersionTable.PSVersion.ToString()
    iscc               = $iscc
    out_dir            = $OutDir
    flavors            = $flavors
    skip_assemble      = [bool]$SkipAssemble
    skip_publish       = [bool]$SkipPublish
    gates              = $script:Gates.ToArray()
    artefacts          = $built.ToArray()
    failed_gates       = $script:GateFailures
}
$reportPath = Join-Path $logDir ('installer-build-' + $runStamp + '.json')
$null = Write-YwkJsonFile -Path $reportPath -Value $report -Depth 8

foreach ($b in $built) {
    # A run that stopped writes no path, no bytes and no sha256, so say WHY instead of printing three
    # empty fields (the JSON report has carried stopped_at all along; the log did not).
    if ([string]::IsNullOrEmpty($b['path'])) {
        Write-YwkLog -Level 'WARN' -Message ('artefact ' + $b['flavor'] + ' = NONE on the shelf; stopped at ' +
            $b['stopped_at'] + ' (' + $b['setup'] + ')')
    } else {
        Write-YwkLog -Message ('artefact ' + $b['flavor'] + ' = ' + $b['path'] + ' (' + $b['bytes'] + ' B, sha256 ' + $b['sha256'] + ')')
    }
}
Write-YwkLog -Message ('report = ' + $reportPath)

if ($script:GateFailures -gt 0) {
    Write-YwkLog -Level 'FAIL' -Message ('DONE with ' + $script:GateFailures + ' failed gate(s) out of ' +
        $script:Gates.Count + '.')
    exit $script:GateFailures
}
Write-YwkLog -Level 'STEP' -Message ('DONE: ' + $built.Count + ' installer(s), ' + $script:Gates.Count +
    ' gate(s), 0 failed.')
exit 0
