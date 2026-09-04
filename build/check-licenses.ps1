<#
.SYNOPSIS
    Acceptance condition A-6: reconcile licenses/README.md with what is actually on disk.

.DESCRIPTION
    licenses/README.md carries an index table whose first column is a backticked path
    relative to licenses/. This script demands a 1:1 match between that table and the real
    files, refuses empty files, proves the two upstream MIT copies are byte-identical to the
    submodules they claim to copy, and checks that every "notices" line the ledgers carry has
    a mention in licenses/first-run-notices.md.

    It reads only; it never edits licenses/ and never decides a licensing question. Where the
    index says a question is open, that stays open -- the script only checks bookkeeping.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\check-licenses.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$RepoRoot    = Get-YwkRepoRoot
$LicenseDir  = Join-Path $RepoRoot 'licenses'
$LedgerDir   = Join-Path $RepoRoot 'ledger'
$VoicesDir   = Join-Path $RepoRoot 'voices'
$LogDir      = Join-Path $RepoRoot 'build\out\check-log'
$null = New-YwkDirectory -Path $LogDir
$null = Start-YwkLog -Path (Join-Path $LogDir 'check-licenses.log')
Write-YwkLog -Level 'STEP' -Message 'check-licenses'

$problems = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $LicenseDir)) {
    throw ('licenses/ does not exist (' + $LicenseDir + '). Seat S3 of convoy A delivers it.')
}
$readme = Join-Path $LicenseDir 'README.md'
if (-not (Test-Path -LiteralPath $readme)) {
    throw ('licenses/README.md is missing; there is no index to reconcile.')
}

# --------------------------------------------------------------------------- files on disk

$actual = New-Object System.Collections.Generic.List[string]
foreach ($f in (Get-ChildItem -LiteralPath $LicenseDir -Recurse -File)) {
    $rel = $f.FullName.Substring($LicenseDir.Length).TrimStart('\') -replace '\\', '/'
    $actual.Add($rel)
    if ($f.Length -eq 0) {
        $problems.Add('empty file: licenses/' + $rel)
    }
}
Write-YwkLog -Message ('files under licenses/: ' + $actual.Count)

# --------------------------------------------------------------------------- index table

# Only the table under the "## 1." heading is the index of real files. Later sections quote
# Hugging Face repository ids in the same backticked-first-column shape, so an unbounded scan
# would mistake "Aratako/Irodori-TTS-v4.1-Small" for a path.
$inSection = $false
$sawSection = $false
$indexed = New-Object System.Collections.Generic.List[string]
foreach ($line in [System.IO.File]::ReadAllLines($readme)) {
    $t = $line.Trim()
    if ($t -match '^##\s') {
        $inSection = ($t -match '^##\s+1[.\s]')
        if ($inSection) { $sawSection = $true }
        continue
    }
    if (-not $inSection) { continue }
    if (-not $t.StartsWith('|')) { continue }
    $cells = @($t.Trim('|') -split '\|')
    if ($cells.Count -lt 2) { continue }
    $first = $cells[0].Trim()
    $m = [regex]::Match($first, '^`([^`]+)`$')
    if (-not $m.Success) { continue }
    $p = $m.Groups[1].Value.Trim()
    if ($p -notmatch '[./]') { continue }
    $p = $p -replace '^licenses/', ''
    if (-not $indexed.Contains($p)) { $indexed.Add($p) }
}
if (-not $sawSection) {
    $problems.Add('licenses/README.md has no "## 1." index section; there is no table to reconcile')
}
Write-YwkLog -Message ('rows in the index table: ' + $indexed.Count)

foreach ($p in $indexed) {
    if (-not $actual.Contains($p)) {
        $problems.Add('indexed but missing on disk: licenses/' + $p)
    }
}
foreach ($p in $actual) {
    if (-not $indexed.Contains($p)) {
        $problems.Add('on disk but not in the index table: licenses/' + $p)
    }
}
if ($problems.Count -eq 0) {
    Write-YwkLog -Message 'PASS  the index table and the files match 1:1'
}

# --------------------------------------------------------------------------- required set

$required = @(
    'README.md',
    'irodori-tts/LICENSE',
    'irodori-tts-server/LICENSE',
    'irodori-tts-v4.1-small/LICENSE.md',
    'semantic-dacvae-japanese-32dim/LICENSE.md',
    'silentcipher/LICENSE',
    'dacvae/LICENSE',
    'irodori-tts-for-yomiwakechan/LICENSE',
    'first-run-notices.md'
)
foreach ($p in $required) {
    if (-not $actual.Contains($p)) {
        $problems.Add('required by the design doc section 1 but absent: licenses/' + $p)
    }
}

# --------------------------------------------------------------------------- upstream copies

$pairs = @(
    @{ copy = 'irodori-tts/LICENSE';        origin = 'upstream\Irodori-TTS\LICENSE' },
    @{ copy = 'irodori-tts-server/LICENSE'; origin = 'upstream\Irodori-TTS-Server\LICENSE' }
)
foreach ($p in $pairs) {
    $c = Join-Path $LicenseDir ($p.copy -replace '/', '\')
    $o = Join-Path $RepoRoot $p.origin
    if (-not (Test-Path -LiteralPath $c)) { continue }
    if (-not (Test-Path -LiteralPath $o)) {
        $warnings.Add('cannot compare licenses/' + $p.copy + ': ' + $p.origin + ' is not checked out')
        continue
    }
    $a = [System.IO.File]::ReadAllText($c) -replace "`r`n", "`n"
    $b = [System.IO.File]::ReadAllText($o) -replace "`r`n", "`n"
    if ($a -ne $b) {
        $problems.Add('licenses/' + $p.copy + ' is not a verbatim copy of ' + $p.origin)
    } else {
        Write-YwkLog -Message ('PASS  licenses/' + $p.copy + ' matches ' + $p.origin + ' (newline-insensitive)')
    }
}

# --------------------------------------------------------------------------- MIT / Apache text

$mustContain = @(
    @{ file = 'irodori-tts/LICENSE';                       needle = 'Permission is hereby granted' },
    @{ file = 'irodori-tts-server/LICENSE';                needle = 'Permission is hereby granted' },
    @{ file = 'silentcipher/LICENSE';                      needle = 'Permission is hereby granted' },
    @{ file = 'irodori-tts-for-yomiwakechan/LICENSE';      needle = 'Permission is hereby granted' },
    @{ file = 'dacvae/LICENSE';                            needle = 'Apache License' },
    @{ file = 'irodori-tts-v4.1-small/LICENSE.md';         needle = 'Permission is hereby granted' },
    @{ file = 'semantic-dacvae-japanese-32dim/LICENSE.md'; needle = 'Permission is hereby granted' }
)
foreach ($m in $mustContain) {
    $f = Join-Path $LicenseDir ($m.file -replace '/', '\')
    if (-not (Test-Path -LiteralPath $f)) { continue }
    $text = [System.IO.File]::ReadAllText($f)
    if ($text -notmatch [regex]::Escape($m.needle)) {
        $problems.Add('licenses/' + $m.file + ' does not contain the expected phrase "' + $m.needle + '"')
    }
}

# Ethical Restrictions must be reproduced with the v4.1-Small weights (design doc section 1).
$eth = Join-Path $LicenseDir 'irodori-tts-v4.1-small\LICENSE.md'
if (Test-Path -LiteralPath $eth) {
    $t = [System.IO.File]::ReadAllText($eth)
    if ($t -notmatch 'Ethical') {
        $problems.Add('licenses/irodori-tts-v4.1-small/LICENSE.md has no Ethical Restrictions section')
    }
}

# --------------------------------------------------------------------------- ledger notices

$notesFile = Join-Path $LicenseDir 'first-run-notices.md'
if (Test-Path -LiteralPath $notesFile) {
    $notesText = [System.IO.File]::ReadAllText($notesFile)
    $topics = New-Object System.Collections.Generic.HashSet[string]

    # Two independent reasons an item has to be named in the notices:
    #   (a) it carries an explicit "notices" array (NVIDIA EULA / cuDNN SLA today);
    #   (b) its own declared licence is copyleft-family or unidentified -- soxr
    #       (LGPL-2.1-or-later) sat in all three runtime ledgers and in none of the
    #       notices, and (a) alone could never have caught it: make-ledger only
    #       attaches "notices" to the cu* torch wheels, so on the cpu ledger the
    #       reconciliation matched nothing at all and still passed.
    # U+672A U+7279 U+5B9A = "unidentified".
    $unidentified = [string]([char]0x672A + [char]0x7279 + [char]0x5B9A)
    $copyleftHits = New-Object System.Collections.Generic.List[string]
    foreach ($lf in (Get-ChildItem -LiteralPath $LedgerDir -Filter '*.json' -File)) {
        $l = Read-YwkJsonFile -Path $lf.FullName
        if (-not ($l.PSObject.Properties.Name -contains 'items')) { continue }
        foreach ($it in $l.items) {
            $itemName = [string]$it.name
            if ($it.PSObject.Properties.Name -contains 'notices') {
                $null = $topics.Add($itemName)
            }
            # (c) every item must declare a licence at all: the ledger is what
            #     first-run-notices.md A11 calls the source of record for this.
            $lic = ''
            if ($it.PSObject.Properties.Name -contains 'license') {
                if ($null -ne $it.license) { $lic = [string]$it.license }
            }
            if ([string]::IsNullOrWhiteSpace($lic)) {
                $problems.Add('ledger/' + $lf.Name + ': item "' + $itemName + '" has no license (the ledger is the source of record -- first-run-notices.md A11)')
                continue
            }
            $upper = $lic.ToUpperInvariant()
            $isCopyleft = ($upper -match 'GPL') -or ($upper -match 'MPL') -or ($lic -match [regex]::Escape($unidentified))
            if ($isCopyleft) {
                $null = $topics.Add($itemName)
                if (-not $copyleftHits.Contains($itemName + ' = ' + $lic)) {
                    $copyleftHits.Add($itemName + ' = ' + $lic)
                }
            }
        }
    }
    foreach ($n in @('torch', 'vc_redist.x64', 'python')) {
        $null = $topics.Add($n)
    }
    if ($copyleftHits.Count -gt 0) {
        Write-YwkLog -Message ('copyleft/unidentified items that must be named in the notices: ' + ($copyleftHits -join '; '))
    }
    foreach ($n in $topics) {
        $needle = $n
        if ($n -eq 'vc_redist.x64') { $needle = 'vc_redist' }
        if ($notesText -notmatch [regex]::Escape($needle)) {
            $problems.Add('licenses/first-run-notices.md never mentions "' + $needle + '", which the ledger flags with notices')
        }
    }
    foreach ($needle in @('CUDA', 'cuDNN', 'libsndfile')) {
        if ($notesText -notmatch [regex]::Escape($needle)) {
            $warnings.Add('licenses/first-run-notices.md does not mention ' + $needle)
        }
    }
    # decisions.md 46 reversed this check. The file used to declare itself "not part of the
    # distributable"; that sentence was about the third-party BYTES (decisions.md 8), not about
    # the notice, and build/assemble-app.ps1 excluded the file on the strength of it. The notice
    # now ships, because the first-run UI has to show it before it fetches anything.
    # U+914D U+5E03 U+7269 U+306B U+5165 U+308C U+308B = "put into the distributable"
    $shipped   = [string]([char]0x914D + [char]0x5E03 + [char]0x7269 + [char]0x306B + [char]0x5165 + [char]0x308C + [char]0x308B)
    # U+914D U+5E03 U+7269 U+306B U+306F U+5165 U+308C U+306A U+3044 = "not put into the distributable"
    $notShipped = [string]([char]0x914D + [char]0x5E03 + [char]0x7269 + [char]0x306B + [char]0x306F + [char]0x5165 + [char]0x308C + [char]0x306A + [char]0x3044)
    if ($notesText -match [regex]::Escape($notShipped)) {
        $problems.Add('licenses/first-run-notices.md still calls itself "not part of the distributable"; decisions.md 46 says it ships and build/assemble-app.ps1 now copies it')
    }
    if ($notesText -notmatch ('part of the distributable|' + [regex]::Escape($shipped))) {
        $problems.Add('licenses/first-run-notices.md does not declare that it is shipped with the app and shown by the first-run UI before anything is fetched (decisions.md 46)')
    }
}

# --------------------------------------------------------------------------- README sha256

# The index quotes a sha256 for the files it calls verbatim copies. Check it against the bytes
# on disk. This is what catches a line-ending change: ".gitattributes" now pins these files to
# LF ("licenses/*/LICENSE -text"), but before that a re-clone on a machine with
# core.autocrlf=true turned them into CRLF, silently invalidating every hash the index quotes --
# while the byte-identical comparison above still passed, because it normalises newlines first.
$hashRows = 0
$inSection = $false
foreach ($line in [System.IO.File]::ReadAllLines($readme)) {
    $t = $line.Trim()
    if ($t -match '^##\s') { $inSection = ($t -match '^##\s+1[.\s]'); continue }
    if (-not $inSection) { continue }
    if (-not $t.StartsWith('|')) { continue }
    $cells = @($t.Trim('|') -split '\|')
    if ($cells.Count -lt 2) { continue }
    $m = [regex]::Match($cells[0].Trim(), '^`([^`]+)`$')
    if (-not $m.Success) { continue }
    $rel = ($m.Groups[1].Value.Trim() -replace '^licenses/', '')
    $hashes = [regex]::Matches($t, '(?<![0-9a-fA-F])([0-9a-f]{64})(?![0-9a-fA-F])')
    if ($hashes.Count -eq 0) { continue }
    $f = Join-Path $LicenseDir ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $f)) { continue }
    $actualHash = Get-YwkSha256 -Path $f
    $matched = $false
    foreach ($h in $hashes) {
        if ($h.Groups[1].Value.ToLowerInvariant() -eq $actualHash) { $matched = $true }
    }
    $hashRows = $hashRows + 1
    if (-not $matched) {
        $quoted = @($hashes | ForEach-Object { $_.Groups[1].Value })
        $problems.Add('licenses/' + $rel + ' is ' + $actualHash + ' but the index quotes ' + ($quoted -join ', ') +
                      ' (a CRLF checkout does exactly this; see .gitattributes)')
    }
}
Write-YwkLog -Message ('index rows carrying a sha256: ' + $hashRows)

# --------------------------------------------------------------------------- preset voices

# voices/presets/*.wav ships inside the distributable, so every one of them needs a row saying
# where it came from and on whose confirmation. voices/presets.json is that record (convoy P
# writes it; licenses/README.md section 6-3 points at it rather than copying the table, which
# would go stale while convoy P is still generating).
$presetsJson = Join-Path $VoicesDir 'presets.json'
$presetDir   = Join-Path $VoicesDir 'presets'
if (Test-Path -LiteralPath $presetDir) {
    $wavs = @(Get-ChildItem -LiteralPath $presetDir -File -Filter '*.wav' -ErrorAction SilentlyContinue)
    if ($wavs.Count -gt 0) {
        if (-not (Test-Path -LiteralPath $presetsJson)) {
            $problems.Add('voices/presets/ has ' + $wavs.Count + ' wav file(s) but voices/presets.json does not exist; nothing records where they came from')
        } else {
            $pj = Read-YwkJsonFile -Path $presetsJson
            $shipped = @{}
            $unfetched = New-Object System.Collections.Generic.List[string]
            if ($pj.PSObject.Properties.Name -contains 'presets') {
                foreach ($row in $pj.presets) {
                    $names = $row.PSObject.Properties.Name
                    $file = ''
                    if ($names -contains 'secondary') {
                        if ($null -ne $row.secondary) {
                            if ($row.secondary.PSObject.Properties.Name -contains 'file') { $file = [string]$row.secondary.file }
                        }
                    }
                    if ([string]::IsNullOrEmpty($file)) { continue }
                    $shipped[$file] = $row
                    $by = ''
                    $at = ''
                    if ($names -contains 'rights_confirmed_by') { $by = [string]$row.rights_confirmed_by }
                    if ($names -contains 'rights_confirmed_at') { $at = [string]$row.rights_confirmed_at }
                    if ([string]::IsNullOrWhiteSpace($by) -or [string]::IsNullOrWhiteSpace($at)) {
                        $problems.Add('voices/presets.json: ' + $file + ' has no rights_confirmed_by / rights_confirmed_at')
                    }
                    $fetched = $false
                    if ($names -contains 'rights_source_fetched') { $fetched = [bool]$row.rights_source_fetched }
                    if (-not $fetched) { $unfetched.Add($file) }
                }
            }
            foreach ($w in $wavs) {
                if (-not $shipped.ContainsKey($w.Name)) {
                    $problems.Add('voices/presets/' + $w.Name + ' is not listed in voices/presets.json (licenses/README.md section 6-3)')
                }
            }
            Write-YwkLog -Message ('preset voices: ' + $wavs.Count + ' wav, ' + $shipped.Count + ' rows in voices/presets.json')
            if ($unfetched.Count -gt 0) {
                $warnings.Add('voices/presets.json: rights_source_fetched is false for ' + $unfetched.Count +
                              ' voice(s) -- licenses/README.md section 6-2 must be filled in before a Release: ' + ($unfetched -join ', '))
            }
        }
    }
}

# --------------------------------------------------------------------------- report

$report = [ordered]@{
    generated = Get-YwkTimestamp
    files     = $actual.ToArray()
    indexed   = $indexed.ToArray()
    problems  = $problems.ToArray()
    warnings  = $warnings.ToArray()
}
$null = Write-YwkJsonFile -Path (Join-Path $LogDir 'check-licenses.json') -Value $report -Depth 8

foreach ($w in $warnings) { Write-YwkLog -Level 'WARN' -Message $w }
if ($problems.Count -gt 0) {
    foreach ($p in $problems) { Write-YwkLog -Level 'FAIL' -Message $p }
    Write-YwkLog -Level 'FAIL' -Message ('A-6 FAILED: ' + [string]$problems.Count + ' problem(s)')
    exit 1
}
Write-YwkLog -Level 'STEP' -Message ('A-6 PASSED: ' + [string]$actual.Count + ' license files, all indexed and non-empty')
exit 0
