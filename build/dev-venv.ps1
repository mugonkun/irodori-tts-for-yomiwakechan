<#
.SYNOPSIS
    Create .venv-dev: the development and test box. NOT part of the distributable.

.DESCRIPTION
    The shipped runtime is built by assemble-runtime.ps1 from the ledger and contains no pip,
    no uv and no venv. This script is the other thing: an ordinary uv virtualenv on Python 3.12
    holding exactly the versions the cpu ledger pins, plus pytest and httpx, so the contract
    tests (build/run-tests.ps1) can run against the same dependency set the users will get.

    The versions come from build/out/ledger-log/requirements-cpu.pruned.txt, which
    build/make-ledger.ps1 writes at the same moment it writes ledger/runtime-cpu.json, so the
    two cannot drift. Install is --no-deps: the file is already the complete closure, and
    resolving again would drag matplotlib / ipython / jedi back in.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\dev-venv.ps1
#>
[CmdletBinding()]
param(
    [string]$VenvPath = '',
    [string]$Variant = 'cpu',
    [switch]$Recreate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$RepoRoot = Get-YwkRepoRoot
if ([string]::IsNullOrEmpty($VenvPath)) { $VenvPath = Join-Path $RepoRoot '.venv-dev' }
$LogDir = Join-Path $RepoRoot 'build\out\dev-venv-log'
$null = New-YwkDirectory -Path $LogDir
$null = Start-YwkLog -Path (Join-Path $LogDir 'dev-venv.log')
Write-YwkLog -Level 'STEP' -Message ('dev-venv: ' + $VenvPath)

$uv = Get-YwkUvPath -RequiredVersion '0.12.7'
Write-YwkLog -Message ('uv: ' + $uv.Version)

$reqPath = Join-Path $RepoRoot ('build\out\ledger-log\requirements-' + $Variant + '.pruned.txt')
$fallback = Join-Path $RepoRoot 'build\dev-requirements.txt'
$noDeps = $true
if (-not (Test-Path -LiteralPath $reqPath)) {
    if (-not (Test-Path -LiteralPath $fallback)) {
        throw ($reqPath + ' is missing. Run build\make-ledger.ps1 -Variant ' + $Variant + ' first.')
    }
    # Fresh clone: no ledger log yet. build/dev-requirements.txt is the smaller fallback set
    # that still lets tests/contract/ run; it carries its own index directives and is a range
    # spec, not a closure, so this path must resolve dependencies.
    Write-YwkLog -Level 'WARN' -Message ($reqPath + ' not found; falling back to build\dev-requirements.txt (run make-ledger.ps1 for the exact ledger set)')
    $reqPath = $fallback
    $noDeps = $false
}
$torchIndex = 'https://download.pytorch.org/whl/' + $Variant

if ((Test-Path -LiteralPath $VenvPath) -and $Recreate) {
    Write-YwkLog -Message 'removing the existing .venv-dev (-Recreate)'
    Remove-Item -LiteralPath $VenvPath -Recurse -Force
}

$env2 = New-YwkChildEnvironment

if (-not (Test-Path -LiteralPath (Join-Path $VenvPath 'Scripts\python.exe'))) {
    Write-YwkLog -Message 'uv venv --python 3.12'
    $r = Invoke-YwkNative -FilePath $uv.Path -Arguments @('venv', $VenvPath, '--python', '3.12') -WorkingDirectory $RepoRoot -Environment $env2 -TimeoutSeconds 1200
    $null = Write-YwkTextFile -Path (Join-Path $LogDir 'uv-venv.log') -Newline 'LF' -Text ($r.StdOut + "`n" + $r.StdErr + "`n(exit " + $r.ExitCode + ")`n")
    if ($r.ExitCode -ne 0) {
        throw ('uv venv failed (exit ' + $r.ExitCode + '): ' + $r.StdErr.Trim())
    }
}
$venvPython = Join-Path $VenvPath 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    throw ('uv venv did not produce ' + $venvPython)
}

Write-YwkLog -Message ('uv pip install -r ' + $reqPath + '  (no-deps=' + $noDeps + ')')
$args1 = @('pip', 'install', '--python', $venvPython)
if ($noDeps) {
    $args1 += @('--no-deps', '--extra-index-url', $torchIndex, '--index-strategy', 'unsafe-best-match')
}
$args1 += @('-r', $reqPath)
$r1 = Invoke-YwkNative -FilePath $uv.Path -Arguments $args1 -WorkingDirectory $RepoRoot -Environment $env2 -TimeoutSeconds 3600
$null = Write-YwkTextFile -Path (Join-Path $LogDir 'uv-install-ledger.log') -Newline 'LF' -Text ($r1.StdOut + "`n" + $r1.StdErr + "`n(exit " + $r1.ExitCode + ")`n")
if ($r1.ExitCode -ne 0) {
    Write-YwkLog -Level 'FAIL' -Message ($r1.StdErr.Trim())
    throw ('uv pip install of the ledger set failed (exit ' + $r1.ExitCode + ')')
}

# Test-only tools. These never ship: they are not in any ledger.
$testTools = @('pytest>=8.0.0', 'httpx>=0.27.0')
Write-YwkLog -Message ('uv pip install ' + ($testTools -join ' ') + '  (test only, not distributed)')
$args2 = @('pip', 'install', '--python', $venvPython) + $testTools
$r2 = Invoke-YwkNative -FilePath $uv.Path -Arguments $args2 -WorkingDirectory $RepoRoot -Environment $env2 -TimeoutSeconds 1800
$null = Write-YwkTextFile -Path (Join-Path $LogDir 'uv-install-testtools.log') -Newline 'LF' -Text ($r2.StdOut + "`n" + $r2.StdErr + "`n(exit " + $r2.ExitCode + ")`n")
if ($r2.ExitCode -ne 0) {
    Write-YwkLog -Level 'FAIL' -Message ($r2.StdErr.Trim())
    throw ('uv pip install of the test tools failed (exit ' + $r2.ExitCode + ')')
}

# Smoke: the box must be able to import what the contract tests import.
$check = Invoke-YwkNative -FilePath $venvPython -Environment $env2 -Arguments @(
    '-c',
    'import sys,torch,fastapi,httpx,pytest,transformers,soundfile;print(sys.version.split()[0]);print(torch.__version__);print(transformers.__version__)'
) -TimeoutSeconds 600
if ($check.ExitCode -ne 0) {
    Write-YwkLog -Level 'FAIL' -Message ($check.StdErr.Trim())
    throw 'the dev venv cannot import the test dependencies'
}
foreach ($l in ($check.StdOut -split "`n")) {
    if ($l.Trim().Length -gt 0) { Write-YwkLog -Message ('  ' + $l.Trim()) }
}

$report = [ordered]@{
    generated    = Get-YwkTimestamp
    venv         = $VenvPath
    python       = $venvPython
    from         = $reqPath
    torch_index  = $torchIndex
    test_tools   = $testTools
    distributed  = $false
}
$null = Write-YwkJsonFile -Path (Join-Path $LogDir 'dev-venv.json') -Value $report -Depth 8
Write-YwkLog -Level 'STEP' -Message 'dev-venv ready (development only; it is not part of any release)'
exit 0
