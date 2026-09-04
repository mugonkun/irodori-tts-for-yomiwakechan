<#
.SYNOPSIS
    Acceptance condition A-5: run the contract tests in .venv-dev and prove upstream/ stayed clean.

.DESCRIPTION
    tests/conftest.py imports the wrapper and both upstream trees straight off disk (read-only,
    no editable install). Two things therefore matter here and nowhere else:
      * PYTHONDONTWRITEBYTECODE=1 so no __pycache__ lands inside the submodules;
      * -p no:cacheprovider so pytest does not create .pytest_cache next to them.
    After the run the submodules are checked again with git status; a dirty tree fails the run
    even if every test passed.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\run-tests.ps1
#>
[CmdletBinding()]
param(
    [string]$VenvPath = '',
    [string]$TestPath = '',
    [string[]]$PytestArgs = @(),
    [int]$TimeoutSeconds = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$RepoRoot = Get-YwkRepoRoot
if ([string]::IsNullOrEmpty($VenvPath)) { $VenvPath = Join-Path $RepoRoot '.venv-dev' }
if ([string]::IsNullOrEmpty($TestPath)) { $TestPath = Join-Path $RepoRoot 'tests' }
$LogDir = Join-Path $RepoRoot 'build\out\test-log'
$null = New-YwkDirectory -Path $LogDir
$null = Start-YwkLog -Path (Join-Path $LogDir 'run-tests.log')
Write-YwkLog -Level 'STEP' -Message 'run-tests'

$venvPython = Join-Path $VenvPath 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    throw ($venvPython + ' is missing. Run build\dev-venv.ps1 first.')
}
if (-not (Test-Path -LiteralPath $TestPath)) {
    throw ($TestPath + ' does not exist. Seat S2 of convoy A delivers tests/.')
}

$before = Test-YwkUpstreamClean -RepoRoot $RepoRoot
if (-not $before.Ok) {
    foreach ($l in $before.Report) { Write-YwkLog -Level 'FAIL' -Message ('  ' + $l) }
    throw 'upstream/ is already dirty before the tests run.'
}

$env2 = New-YwkChildEnvironment -Extra @{
    'PYTHONDONTWRITEBYTECODE' = '1'
    'PYTEST_ADDOPTS'          = ''
    'IRODORI_API_KEY'         = ''
}
# an empty IRODORI_API_KEY would still be "set"; remove it instead
$env2.Remove('IRODORI_API_KEY')
$env2.Remove('PYTEST_ADDOPTS')

$pyArgs = @('-m', 'pytest', $TestPath, '-p', 'no:cacheprovider', '-q', '--color=no') + $PytestArgs
Write-YwkLog -Message ('running: python ' + ($pyArgs -join ' '))
$r = Invoke-YwkNative -FilePath $venvPython -Arguments $pyArgs -WorkingDirectory $RepoRoot -Environment $env2 -TimeoutSeconds $TimeoutSeconds

$transcript = $r.StdOut + "`n--- stderr ---`n" + $r.StdErr + "`n(exit " + $r.ExitCode + ")`n"
$null = Write-YwkTextFile -Path (Join-Path $LogDir 'pytest.log') -Newline 'LF' -Text $transcript
foreach ($l in ($r.StdOut -split "`n")) {
    if ($l.Trim().Length -gt 0) { Write-YwkLog -Message ('  ' + $l.TrimEnd()) }
}
if ($r.ExitCode -ne 0) {
    foreach ($l in ($r.StdErr -split "`n")) {
        if ($l.Trim().Length -gt 0) { Write-YwkLog -Level 'FAIL' -Message ('  ' + $l.TrimEnd()) }
    }
}

$after = Test-YwkUpstreamClean -RepoRoot $RepoRoot
foreach ($l in $after.Report) { Write-YwkLog -Message ('  ' + $l) }

$report = [ordered]@{
    generated      = Get-YwkTimestamp
    pytest_exit    = $r.ExitCode
    upstream_clean = $after.Ok
    log            = (Join-Path $LogDir 'pytest.log')
}
$null = Write-YwkJsonFile -Path (Join-Path $LogDir 'run-tests.json') -Value $report -Depth 6

if (-not $after.Ok) {
    Write-YwkLog -Level 'FAIL' -Message 'upstream/ became dirty during the test run'
    exit 1
}
if ($r.ExitCode -ne 0) {
    Write-YwkLog -Level 'FAIL' -Message ('pytest exited with ' + $r.ExitCode)
    exit $r.ExitCode
}
Write-YwkLog -Level 'STEP' -Message 'A-5: contract tests green, upstream/ still clean'
exit 0
