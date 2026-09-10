# wizard-probe.ps1 -- "Is the first-run wizard window open right now?" in one line, without a screen.
# Read-only: touches nothing, presses nothing. Run in the interactive user session (same session as the launcher).
#   pwsh -NoProfile -ExecutionPolicy Bypass -File wizard-probe.ps1
# Exit code: 0 = wizard window found, 1 = launcher running but no wizard, 2 = launcher not running.
[CmdletBinding()]
param([int]$Port = 18088)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

$procs = @(Get-Process -Name 'IrodoriTtsYwk.Launcher' -ErrorAction SilentlyContinue)
$listen = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue).Count
if ($procs.Count -eq 0) {
    Write-Output ("launcher=absent wizard=n/a listen{0}={1}" -f $Port, $listen)
    exit 2
}
$pids = @($procs | ForEach-Object { $_.Id })
# Session check: UIA only sees windows of the caller's own logon session. A "no window" result from
# another session would be indistinguishable from "minimised to the tray", so print both ids.
$mySession = (Get-Process -Id $PID).SessionId
$theirSessions = @($procs | ForEach-Object { $_.SessionId } | Sort-Object -Unique)
Write-Output ("session: probe={0} launcher={1} same={2}" -f $mySession, ($theirSessions -join ','), (($theirSessions.Count -eq 1) -and ($theirSessions[0] -eq $mySession)))

function Get-TextById($root, [string]$id) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $el = $root.FindFirst($TS::Descendants, $c)
    if ($null -eq $el) { return $null }
    try { return [string]$el.Current.Name } catch { return '?' }
}

$wizard = $null
$titles = New-Object System.Collections.Generic.List[string]
# 1) top-level windows of the launcher process(es), by process id
foreach ($procId in $pids) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, [int]$procId)
    foreach ($w in @($AE::RootElement.FindAll($TS::Children, $cond))) {
        $name = ''
        $aid = ''
        try { $name = [string]$w.Current.Name } catch { }
        try { $aid = [string]$w.Current.AutomationId } catch { }
        $titles.Add(('[{0}] name="{1}" id="{2}"' -f $procId, $name, $aid))
        if ($aid -eq 'FirstRunWizard' -or $name -eq '初回取得') { $wizard = $w }
    }
}
# 2) fallback A: the process main window handle (RootElement children can miss windows in odd sessions)
if ($null -eq $wizard) {
    foreach ($p in $procs) {
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
            $w = $AE::FromHandle($p.MainWindowHandle)
            $aid = ''
            try { $aid = [string]$w.Current.AutomationId } catch { }
            $titles.Add(('[main handle {0}] id="{1}"' -f $p.Id, $aid))
            if ($aid -eq 'FirstRunWizard') { $wizard = $w }
        }
    }
}
# 3) fallback B: the wizard is an OWNED window, and UIA parents it under the launcher's MainWindow, not under
#    RootElement -- measured on the RTX machine 2026-09-10 (TreeWalker: FirstRunWizard -> MainWindow -> RootElement),
#    so a Children scan of RootElement never sees it. Descendants does. This branch is the one that actually finds it.
if ($null -eq $wizard) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'FirstRunWizard')
    $found = $AE::RootElement.FindFirst($TS::Descendants, $c)
    if ($null -ne $found) { $wizard = $found }
}

$pidText = ($pids -join ',')
if ($null -ne $wizard) {
    $step = Get-TextById $wizard 'FirstRunStepTitle'
    $num = Get-TextById $wizard 'FirstRunStepNumber'
    $msg = Get-TextById $wizard 'FirstRunMessageText'
    Write-Output ("launcher=pid {0} wizard=OPEN step=""{1}"" ({2}) message=""{3}"" listen{4}={5}" -f $pidText, $step, $num, $msg, $Port, $listen)
    Write-Output ('windows: ' + ($titles -join ' | '))
    exit 0
}
Write-Output ("launcher=pid {0} wizard=none listen{1}={2}" -f $pidText, $Port, $listen)
Write-Output ('windows: ' + ($titles -join ' | '))
exit 1
