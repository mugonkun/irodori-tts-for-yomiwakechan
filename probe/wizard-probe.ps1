# wizard-probe.ps1 -- "Is the first-run wizard window open right now?" in one line, without a screen.
# Read-only: touches nothing, presses nothing. Run in the interactive user session (same session as the launcher).
#   pwsh -NoProfile -ExecutionPolicy Bypass -File wizard-probe.ps1
#   pwsh ... -File wizard-probe.ps1 -Meter -MeterSeconds 900   (hand off to probe/freeze-meter.ps1)
# Exit code: 0 = wizard window found, 1 = launcher running but no wizard, 2 = launcher not running.
#            With -Meter the exit code is the freeze meter's own (0 = the 137 criterion was met).
#
# THE FREEZE METER (decisions 137). This probe answers "is the wizard open" in one shot. The question
# the RTX seat has to answer during a first run is the other one -- "does it LOOK frozen?" -- and that
# needs a sample every second for minutes, not one line. That meter lives in its own read-only file,
# probe/freeze-meter.ps1: it samples FirstRunProgressText / FirstRunPhaseText / FirstRunStepTitle and
# MainBandStateText once a second into a CSV, calls IsHungAppWindow on the wizard and main HWNDs, and
# prints the longest unchanged stretch -- judged on the three WIZARD lines, and only while a stage is
# working (the band ticks a counter of its own; pages awaiting a human are static by design, so both
# are printed as separate numbers instead). -Meter here simply starts it on the same window.
[CmdletBinding()]
param(
    [int]$Port = 18088,
    [switch]$Meter,
    [int]$MeterSeconds = 900
)

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
        # The id is the anchor. The title is only a fallback and it is DECORATED at run time
        # (ReleaseFlavors.WizardTitle), so match its stem, not the whole string -- and accept both
        # the old stem and the v2.0 one.
        if ($aid -eq 'FirstRunWizard' -or $name -like '初回取得*' -or $name -like 'はじめの準備*') {
            $wizard = $w
        }
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
if ($Meter) {
    # Read-only hand off: the meter finds the windows itself and stops when the wizard closes.
    $meterScript = Join-Path $PSScriptRoot 'freeze-meter.ps1'
    Write-Output ('meter: ' + $meterScript + ' -Seconds ' + $MeterSeconds + ' -StopWhenWizardCloses')
    & $meterScript -Seconds $MeterSeconds -StopWhenWizardCloses
    exit $LASTEXITCODE
}
if ($null -ne $wizard) {
    $step = Get-TextById $wizard 'FirstRunStepTitle'
    # v2.0 stage B: the counter reads "N / 3" (it was "N / 7"). The four working steps -- fetch,
    # unpack, models, start -- are all shown as one "preparing" page, 3 / 3. It reads "N / 2" on a
    # seat that already agreed to THIS version of the notices (decisions 130 Q3 skips that page),
    # and it is EMPTY on the last page ("it works." carries no number). This line only prints what
    # it read: the judging belongs to d-launch-probe.ps1, never here.
    $num = Get-TextById $wizard 'FirstRunStepNumber'
    if ([string]::IsNullOrWhiteSpace($num)) { $num = '-' }
    # The one-line "what is happening" is the only thing that says WHICH working step is running.
    $phase = Get-TextById $wizard 'FirstRunPhaseText'
    $msg = Get-TextById $wizard 'FirstRunMessageText'
    Write-Output ("launcher=pid {0} wizard=OPEN step=""{1}"" ({2}) doing=""{3}"" message=""{4}"" listen{5}={6}" -f $pidText, $step, $num, $phase, $msg, $Port, $listen)
    Write-Output ('windows: ' + ($titles -join ' | '))
    exit 0
}
Write-Output ("launcher=pid {0} wizard=none listen{1}={2}" -f $pidText, $Port, $listen)
Write-Output ('windows: ' + ($titles -join ' | '))
exit 1
