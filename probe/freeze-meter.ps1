# probe/freeze-meter.ps1 -- "does the window look frozen?" measured, read-only, once a second.
#
# Usage:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File probe/freeze-meter.ps1
#   pwsh ... -File probe/freeze-meter.ps1 -Seconds 900 -OutCsv build\out\probe-log\freeze.csv
#   pwsh ... -File probe/freeze-meter.ps1 -StopWhenWizardCloses      (ends when the wizard goes away)
#   pwsh ... -File probe/freeze-meter.ps1 -DryRun                    (print the plan, sample nothing)
#
# ASCII only. CRLF. Windows PowerShell 5.1 and PowerShell 7+. It presses NOTHING and writes only its
# own CSV: run it in the same logon session as the launcher, beside a first run you drive by hand.
#
# WHAT IT MEASURES (decisions 137 -- the owner's acceptance line, verbatim, is
# "the user must not see it as frozen"; the three numbers below are how that line was made testable):
#   (1) the longest stretch in which NONE of FirstRunProgressText / FirstRunPhaseText /
#       FirstRunStepTitle changed                                    -- must be <= 5 s
#   (2) how many samples had IsHungAppWindow(hwnd) = true, on the wizard HWND and on the main HWND
#       (user32; "hung" means the window stopped pumping messages for ~5 s)  -- must be 0
#   (3) a visibly moving element counts as "not frozen" even if (1) is exceeded. That is why the
#       lines this meter reads now carry numbers that move on their own: the file counter of the
#       "checking new files" stage ("n / 25,300" plus the elapsed seconds) and the elapsed seconds of
#       the ready wait ("(N seconds)"). The CSV keeps every sampled line, so a reviewer can see
#       which number moved.
#
# TWO RULES THIS METER OBEYS, both learned from its own first draft (the review of v2.0.1(2)):
#   a. The MAIN BAND IS NOT PART OF THE VERDICT. It runs a second counter of its own
#      ("preparing... (N s)"), so folding it into the same OR made criterion (1) unfalsifiable:
#      the joined signal changed every second no matter what the wizard did. The band's own longest
#      stretch is still measured and printed -- as a separate number.
#   b. ONLY WORKING STAGES ARE JUDGED. A page waiting for a human (the notices page with its consent
#      box, the done page, a stage parked on a failure with a "try again" button) is static BY
#      DESIGN and must not be scored. "Working" here = the wizard shows a phase line AND its cancel
#      button is there and enabled -- that button is bound to IsBusy for BOTH its visibility and its
#      command, so it is absent from the UIA tree unless a stage is actually running. Idle stretches
#      are counted separately and printed, never judged.
#
# Exit code: 0 = criterion met, 1 = a stretch longer than -MaxUnchangedSeconds or a hung sample,
#            2 = the launcher (or its window) was never found.

[CmdletBinding()]
param(
    [int]$Seconds = 900,
    [int]$IntervalMilliseconds = 1000,
    [string]$OutCsv = '',
    [string]$ProcessName = 'IrodoriTtsYwk.Launcher',
    [double]$MaxUnchangedSeconds = 5,
    [switch]$StopWhenWizardCloses,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if (-not ('Ywk.FreezeProbe' -as [type])) {
    # IsHungAppWindow is the only honest answer to "is the window pumping messages?" -- a screenshot
    # cannot tell a slow repaint from a dead message loop. It lives in user32 and is not in
    # common.ps1's Ywk.NativeProbe (that type is already compiled by the time this file runs), so
    # this meter carries its own. It reads; it never posts anything to the window.
    Add-Type -Namespace Ywk -Name FreezeProbe -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool IsHungAppWindow(System.IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindow(System.IntPtr hWnd);
'@
}

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

# The ids this meter watches. The first three are the wizard's and carry the verdict; the fourth is
# the main band, measured but NOT judged (see rule a. above). The fifth is read for its enabled
# state only -- it is the "is a stage actually working?" gate (rule b.).
$WizardIds = @('FirstRunStepTitle', 'FirstRunPhaseText', 'FirstRunProgressText')
$BandId = 'MainBandStateText'
$BusyId = 'FirstRunCancelButton'

# Re-finding a window means walking the desktop subtree, which is expensive; once a second is far
# more often than windows appear or close.
$RediscoverSeconds = 5

if ([string]::IsNullOrEmpty($OutCsv)) {
    $repo = Split-Path -Parent $PSScriptRoot
    $dir = Join-Path $repo 'build\out\probe-log'
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $OutCsv = Join-Path $dir ('freeze-meter-' + $stamp + '.csv')
}

if ($DryRun) {
    Write-Output ('[plan] process   = ' + $ProcessName)
    Write-Output ('[plan] judged    = ' + ($WizardIds -join ', ') + '  (working stages only)')
    Write-Output ('[plan] measured  = ' + $BandId + '  (reported, not judged)')
    Write-Output ('[plan] busy gate = ' + $BusyId + '.IsEnabled')
    Write-Output ('[plan] every     = ' + $IntervalMilliseconds + ' ms for ' + $Seconds + ' s')
    Write-Output ('[plan] csv       = ' + $OutCsv + '  (written row by row, UTF-8 with BOM)')
    Write-Output ('[plan] threshold = ' + $MaxUnchangedSeconds + ' s unchanged, 0 hung samples')
    Write-Output 'nothing was touched.'
    exit 0
}

function Get-ElementById {
    param($Root, [string]$Id, $Scope = $null)
    if ($null -eq $Root) { return $null }
    if ($null -eq $Scope) { $Scope = $TS::Descendants }
    try {
        $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $Id)
        return $Root.FindFirst($Scope, $cond)
    } catch {
        return $null
    }
}

function Get-WindowById {
    <#
      .SYNOPSIS
        A top level window of one process, cheapest path first (common.ps1's order).
      .DESCRIPTION
        Children-of-RootElement AND ProcessId is one level deep; MainWindowHandle -> FromHandle is
        one call; the Descendants walk of the whole desktop is the last resort. Every search is
        AND-ed with the process id so another program exposing the same AutomationId is never read.
    #>
    param([string]$Id, $Proc)
    if ($null -eq $Proc) { return $null }

    $byId = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $Id)
    $byPid = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $Proc.Id)
    $cond = New-Object System.Windows.Automation.AndCondition($byId, $byPid)

    try {
        $hit = $AE::RootElement.FindFirst($TS::Children, $cond)
        if ($null -ne $hit) { return $hit }
    } catch { }

    try {
        $handle = [System.IntPtr]$Proc.MainWindowHandle
        if ($handle -ne [System.IntPtr]::Zero) {
            $el = $AE::FromHandle($handle)
            if (($null -ne $el) -and ([string]$el.Current.AutomationId -eq $Id)) { return $el }
        }
    } catch { }

    try {
        return $AE::RootElement.FindFirst($TS::Descendants, $cond)
    } catch {
        return $null
    }
}

function Get-TextOf {
    param($Root, [string]$Id)
    $el = Get-ElementById -Root $Root -Id $Id
    if ($null -eq $el) { return '' }
    try { return [string]$el.Current.Name } catch { return '?' }
}

function Get-EnabledOf {
    param($Root, [string]$Id)
    $el = Get-ElementById -Root $Root -Id $Id
    if ($null -eq $el) { return $false }
    try { return [bool]$el.Current.IsEnabled } catch { return $false }
}

function Get-HandleOf {
    param($Element)
    if ($null -eq $Element) { return [System.IntPtr]::Zero }
    try { return [System.IntPtr]$Element.Current.NativeWindowHandle } catch { return [System.IntPtr]::Zero }
}

function Find-Windows {
    param([string]$Name)
    $found = [ordered]@{ Wizard = $null; Main = $null; Pids = @() }
    $procs = @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return $found }
    $found.Pids = @($procs | ForEach-Object { $_.Id })

    foreach ($proc in $procs) {
        if ($null -eq $found.Main) { $found.Main = Get-WindowById -Id 'MainWindow' -Proc $proc }
        if ($null -eq $found.Wizard) {
            # The wizard is an OWNED window: UI Automation parents it under MainWindow, not under
            # RootElement, so a Children scan of the desktop never sees it (measured on the RTX seat
            # 2026-09-10). Its owner's children are one level deep, so look there first.
            if ($null -ne $found.Main) {
                $found.Wizard = Get-ElementById -Root $found.Main -Id 'FirstRunWizard' -Scope $TS::Children
            }
            if ($null -eq $found.Wizard) {
                $found.Wizard = Get-WindowById -Id 'FirstRunWizard' -Proc $proc
            }
        }
    }

    return $found
}

$csvDir = Split-Path -Parent $OutCsv
if (-not [string]::IsNullOrEmpty($csvDir)) { $null = New-Item -ItemType Directory -Path $csvDir -Force }

$windows = Find-Windows -Name $ProcessName
if (($windows.Pids.Count -eq 0) -or (($null -eq $windows.Wizard) -and ($null -eq $windows.Main))) {
    Write-Output ('freeze-meter: launcher=absent window=none csv=' + $OutCsv)
    exit 2
}

Write-Output ('freeze-meter: pid ' + ($windows.Pids -join ',') +
    '  wizard=' + [bool]($null -ne $windows.Wizard) +
    '  main=' + [bool]($null -ne $windows.Main) +
    '  every ' + $IntervalMilliseconds + ' ms for ' + $Seconds + ' s')
Write-Output ('freeze-meter: csv = ' + $OutCsv)

function Format-Csv {
    param([string]$Value)
    if ($null -eq $Value) { return '""' }
    return '"' + ($Value -replace '"', '""') + '"'
}

# The CSV is written ROW BY ROW (review of v2.0.1(2)): d-launch-probe kills this process when the
# wizard is still open at its finally, and an operator may press Ctrl-C during a long stall -- which
# is precisely the run whose samples matter. A BOM is written on every host because Excel reads a
# BOM-less CSV in the ANSI code page and the text columns are Japanese.
$encoding = New-Object System.Text.UTF8Encoding $true
$writer = New-Object System.IO.StreamWriter($OutCsv, $false, $encoding)
$writer.AutoFlush = $true
$writer.WriteLine('iso,elapsed_s,working,step_title,phase,progress,band,wizard_hung,main_hung,' +
    'changed_step,changed_phase,changed_progress,changed_band')

$watch = [System.Diagnostics.Stopwatch]::StartNew()
$deadline = $watch.Elapsed.TotalSeconds + $Seconds
$hungWizard = 0
$hungMain = 0
$samples = 0
$wizardSeen = $false
$wasWorking = $false
$idleLongest = 0.0
$idleSince = 0.0
$lastFind = 0.0

# Per id: the last value seen, when it last changed, and the longest still stretch so far.
$ids = @($WizardIds + $BandId)
$last = @{}
$lastChangeAt = @{}
$longest = @{}
$longestAt = @{}
foreach ($id in $ids) {
    $last[$id] = [string]$null
    $lastChangeAt[$id] = 0.0
    $longest[$id] = 0.0
    $longestAt[$id] = ''
}

try {
    while ($watch.Elapsed.TotalSeconds -lt $deadline) {
        # Look again only for a window that may still appear: once the wizard has been seen and has
        # gone, searching for it again is a desktop walk per sample that finds nothing.
        $wanted = ($null -eq $windows.Main) -or (($null -eq $windows.Wizard) -and (-not $wizardSeen))
        if ($wanted -and (($watch.Elapsed.TotalSeconds - $lastFind) -ge $RediscoverSeconds)) {
            $lastFind = $watch.Elapsed.TotalSeconds
            $windows = Find-Windows -Name $ProcessName
        }

        # The clock is read AFTER the search: a desktop walk can take seconds, and a sample stamped
        # before it would under-report the stretch it belongs to.
        $now = $watch.Elapsed.TotalSeconds

        $wizardHandle = Get-HandleOf -Element $windows.Wizard
        $mainHandle = Get-HandleOf -Element $windows.Main
        $wizardAlive = ($wizardHandle -ne [System.IntPtr]::Zero) -and [Ywk.FreezeProbe]::IsWindow($wizardHandle)
        if ($wizardAlive) { $wizardSeen = $true }
        if ($StopWhenWizardCloses -and $wizardSeen -and (-not $wizardAlive)) {
            Write-Output 'freeze-meter: the wizard closed -- stopping.'
            break
        }

        $values = @{}
        $values[$WizardIds[0]] = Get-TextOf -Root $windows.Wizard -Id $WizardIds[0]
        $values[$WizardIds[1]] = Get-TextOf -Root $windows.Wizard -Id $WizardIds[1]
        $values[$WizardIds[2]] = Get-TextOf -Root $windows.Wizard -Id $WizardIds[2]
        $values[$BandId] = Get-TextOf -Root $windows.Main -Id $BandId

        # "A stage is working" = it names a phase AND its cancel button is live. That button is
        # bound to IsBusy twice over (Visibility and Command), so a missing or disabled button is
        # an idle page. Pages awaiting a human fail both halves.
        $busy = $false
        if ($wizardAlive) { $busy = Get-EnabledOf -Root $windows.Wizard -Id $BusyId }
        $working = $wizardAlive -and $busy -and (-not [string]::IsNullOrEmpty($values[$WizardIds[1]]))

        $wizardHung = $false
        $mainHung = $false
        if ($wizardAlive) { $wizardHung = [Ywk.FreezeProbe]::IsHungAppWindow($wizardHandle) }
        if (($mainHandle -ne [System.IntPtr]::Zero) -and [Ywk.FreezeProbe]::IsWindow($mainHandle)) {
            $mainHung = [Ywk.FreezeProbe]::IsHungAppWindow($mainHandle)
        }
        if ($wizardHung) { $hungWizard++ }
        if ($mainHung) { $hungMain++ }

        $changed = @{}
        foreach ($id in $ids) {
            $changed[$id] = ($samples -eq 0) -or ($values[$id] -ne $last[$id])
            if ($changed[$id]) {
                $last[$id] = $values[$id]
                $lastChangeAt[$id] = $now
            }
        }

        if ($working -and (-not $wasWorking)) {
            # A stage just started working: the idle stretch before it is not its fault.
            foreach ($id in $WizardIds) { $lastChangeAt[$id] = $now }
            $idleLongest = [math]::Max($idleLongest, $now - $idleSince)
        }
        if ((-not $working) -and $wasWorking) { $idleSince = $now }
        $wasWorking = $working

        # The verdict watches the three wizard ids, and only while a stage is working.
        if ($working) {
            foreach ($id in $WizardIds) {
                $still = $now - $lastChangeAt[$id]
                if ($still -gt $longest[$id]) {
                    $longest[$id] = $still
                    $longestAt[$id] = $values[$id]
                }
            }
        }

        # The band is measured all the time and judged never (it ticks a counter of its own).
        $stillBand = $now - $lastChangeAt[$BandId]
        if ($stillBand -gt $longest[$BandId]) {
            $longest[$BandId] = $stillBand
            $longestAt[$BandId] = $values[$BandId]
        }

        $writer.WriteLine(((Get-Date).ToString('s') + ',' + [math]::Round($now, 2) + ',' +
            [int]$working + ',' +
            (Format-Csv $values[$WizardIds[0]]) + ',' + (Format-Csv $values[$WizardIds[1]]) + ',' +
            (Format-Csv $values[$WizardIds[2]]) + ',' + (Format-Csv $values[$BandId]) + ',' +
            [int]$wizardHung + ',' + [int]$mainHung + ',' +
            [int]$changed[$WizardIds[0]] + ',' + [int]$changed[$WizardIds[1]] + ',' +
            [int]$changed[$WizardIds[2]] + ',' + [int]$changed[$BandId]))
        $samples++

        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
} finally {
    $watch.Stop()
    $writer.Flush()
    $writer.Close()
    $writer.Dispose()
}

if ((-not $wasWorking) -and ($samples -gt 0)) {
    $idleLongest = [math]::Max($idleLongest, $watch.Elapsed.TotalSeconds - $idleSince)
}

$worst = 0.0
$worstId = ''
foreach ($id in $WizardIds) {
    if ($longest[$id] -gt $worst) {
        $worst = $longest[$id]
        $worstId = $id
    }
}

# The detail goes FIRST so the summary is genuinely the last line this script prints (v2-plan H).
foreach ($id in $WizardIds) {
    Write-Output ('freeze-meter: ' + $id + ' stood still ' + [math]::Round($longest[$id], 1) +
        ' s at -- ' + $longestAt[$id])
}
Write-Output ('freeze-meter: ' + $BandId + ' stood still ' + [math]::Round($longest[$BandId], 1) +
    ' s at -- ' + $longestAt[$BandId] + '  (measured, not judged)')
Write-Output ('freeze-meter: pages awaiting a human stood still up to ' +
    [math]::Round($idleLongest, 1) + ' s  (by design, not judged)')

$ok = ($worst -le $MaxUnchangedSeconds) -and (($hungWizard + $hungMain) -eq 0)
$verdict = 'FAIL'
if ($ok) { $verdict = 'PASS' }
$worstNote = ''
if ($worstId -ne '') { $worstNote = ', ' + $worstId }
Write-Output ('freeze-meter: ' + $verdict +
    '  samples=' + $samples +
    '  longest_unchanged=' + [math]::Round($worst, 1) + ' s (limit ' + $MaxUnchangedSeconds + ' s' +
    $worstNote + ')' +
    '  band_unchanged=' + [math]::Round($longest[$BandId], 1) + ' s' +
    '  idle=' + [math]::Round($idleLongest, 1) + ' s' +
    '  hung_wizard=' + $hungWizard +
    '  hung_main=' + $hungMain +
    '  csv=' + $OutCsv)
if ($ok) { exit 0 }
exit 1
