# probe/common.ps1 -- shared UI Automation driver for the convoy D launcher probes.
#
# ASCII only. CRLF. Must parse and run on Windows PowerShell 5.1 and PowerShell 7+.
# No ternary operator, no null-coalescing, no '&&' inside this file.
#
# Dot-source it:   . (Join-Path $PSScriptRoot 'common.ps1')
#
# Shape follows the host project driver (yomiwakechan2/probe/ai-multi-p7/common.ps1): UIAutomationClient
# is loaded once, elements are located by AutomationId, and a WPF TabItem must be selected before its
# content exists (TabControl realises tab content lazily -- an id inside an unselected tab is NOT found).
#
# House rules this file enforces:
#   * The launcher is always started in developer mode with a private data directory, so the real
#     %LOCALAPPDATA%\irodori-tts-ywk of the operator is never touched.
#   * The private port must not be 8088 / 7861 / 18088 (8088 and 7861 belong to the machine's own
#     servers; 18088 is the shipped default the host app connects to).
#   * Stop-Launcher always tree-kills and then proves that no python child survived.
#
# Japanese literals are forbidden in this file (ASCII rule), so callers build them from code points
# with New-JpText: New-JpText 0x30C6,0x30B9,0x30C8   ->  a 3 character katakana string.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if (-not ('Ywk.NativeProbe' -as [type])) {
    # EnumWindows is here because UI Automation does NOT always list a process's owned windows as
    # children of RootElement (measured 2026-09-05: the launcher's own modal FirstRunWizard is a
    # visible top level HWND that RootElement.FindAll(Children, ProcessId) never returns -- the same
    # blindness the common file dialog showed in the integration run). Walking Win32 and turning the
    # HWND into an element with AutomationElement.FromHandle is the road that always works.
    Add-Type -Namespace Ywk -Name NativeProbe -MemberDefinition @'
public delegate bool EnumWindowsProc(System.IntPtr hWnd, System.IntPtr lParam);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, System.IntPtr lParam);
[DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(System.IntPtr hWnd, out int processId);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr hWnd);
'@
}
$null = [Ywk.NativeProbe]::SetProcessDPIAware()

$script:AE = [System.Windows.Automation.AutomationElement]
$script:TS = [System.Windows.Automation.TreeScope]

# Ports the probes must never bind or disturb.
$script:ForbiddenPorts = @(8088, 7861, 18088)

# ------------------------------------------------------------------ paths

function Get-YwkProbeRepoRoot {
    <#
      .SYNOPSIS
        Absolute path of the repository root (the parent of probe/).
    #>
    [CmdletBinding()]
    param()
    $here = Split-Path -Parent $PSCommandPath
    if ([string]::IsNullOrEmpty($here)) {
        $here = $PSScriptRoot
    }
    return (Resolve-Path -LiteralPath (Join-Path $here '..')).Path
}

function New-JpText {
    <#
      .SYNOPSIS
        Build a non-ASCII string from code points so this file stays ASCII.
      .EXAMPLE
        New-JpText 0x30C6,0x30B9,0x30C8
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true, Position = 0)][int[]]$CodePoint)
    $sb = New-Object System.Text.StringBuilder
    foreach ($cp in $CodePoint) {
        $null = $sb.Append([char]$cp)
    }
    return $sb.ToString()
}

# ------------------------------------------------------------------ fixture

function New-LauncherFixture {
    <#
      .SYNOPSIS
        Create a private data directory with a settings.json that pins variant, port and start-up
        behaviour, so the probe never depends on (or disturbs) the operator's real settings.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$DataDir,
        [Parameter(Mandatory = $true)][string]$Variant,
        [Parameter(Mandatory = $true)][int]$Port,
        [bool]$AutoStartServer = $false,
        [bool]$FirstRunCompleted = $true,
        [bool]$Warmup = $false,
        [bool]$Fresh = $true,
        # HF_HOME. The private data directory has no models, so a probe that must actually load the
        # runtime points this at the cache the machine already has (read only -- HF_HUB_OFFLINE=1).
        # Empty = leave it unset, i.e. <data>\models, which is what a fresh install sees.
        [string]$HfHome = ''
    )
    if ($script:ForbiddenPorts -contains $Port) {
        throw ("Port " + $Port + " is reserved (8088/7861 belong to the machine, 18088 is the shipped default).")
    }
    if ($Fresh -and (Test-Path -LiteralPath $DataDir)) {
        Remove-Item -LiteralPath $DataDir -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $DataDir -Force

    $settings = [ordered]@{
        schema            = 1
        variant           = $Variant
        port              = $Port
        autoStartServer   = $AutoStartServer
        firstRunCompleted = $FirstRunCompleted
        warmup            = $Warmup
        showMemoryPanel   = $true
    }
    if (-not [string]::IsNullOrEmpty($HfHome)) {
        $settings['hfHome'] = $HfHome
    }
    $json = $settings | ConvertTo-Json -Depth 6
    $path = Join-Path $DataDir 'settings.json'
    [System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("[fixture] " + $path)
    return (Resolve-Path -LiteralPath $DataDir).Path
}

# ------------------------------------------------------------------ process

function Start-Launcher {
    <#
      .SYNOPSIS
        Start the launcher exe in developer mode (app tree / runtime root / data dir from env).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [Parameter(Mandatory = $true)][string]$AppDir,
        [string]$RuntimeRoot = '',
        [Parameter(Mandatory = $true)][string]$DataDir
    )
    if (-not (Test-Path -LiteralPath $Exe)) {
        throw ("Launcher exe is missing: " + $Exe)
    }

    $savedApp     = $env:YWK_LAUNCHER_APP_DIR
    $savedRuntime = $env:YWK_LAUNCHER_RUNTIME_DIR
    $savedData    = $env:YWK_LAUNCHER_DATA_DIR
    try {
        $env:YWK_LAUNCHER_APP_DIR  = $AppDir
        $env:YWK_LAUNCHER_DATA_DIR = $DataDir
        if ([string]::IsNullOrEmpty($RuntimeRoot)) {
            Remove-Item Env:\YWK_LAUNCHER_RUNTIME_DIR -ErrorAction SilentlyContinue
        } else {
            $env:YWK_LAUNCHER_RUNTIME_DIR = $RuntimeRoot
        }
        $proc = Start-Process -FilePath $Exe -PassThru
    } finally {
        $env:YWK_LAUNCHER_APP_DIR     = $savedApp
        $env:YWK_LAUNCHER_RUNTIME_DIR = $savedRuntime
        $env:YWK_LAUNCHER_DATA_DIR    = $savedData
    }
    Write-Host ("[start] pid=" + $proc.Id + " exe=" + $Exe)
    return $proc
}

function Get-StrayPython {
    <#
      .SYNOPSIS
        python.exe processes whose command line matches any of the given patterns (empty = all).
    #>
    [CmdletBinding()]
    param([string[]]$Pattern = @())
    $all = @(Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue)
    if ($Pattern.Count -eq 0) { return $all }
    $hits = @()
    foreach ($p in $all) {
        $cmd = $p.CommandLine
        if ([string]::IsNullOrEmpty($cmd)) { continue }
        foreach ($pat in $Pattern) {
            if ($cmd -like $pat) { $hits += $p; break }
        }
    }
    return $hits
}

function Stop-Launcher {
    <#
      .SYNOPSIS
        Tree-kill the launcher and prove nothing it spawned survived.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Proc,
        [string[]]$StrayCommandLinePattern = @()
    )
    if ($null -ne $Proc) {
        try {
            & taskkill.exe /PID $Proc.Id /T /F 2>&1 | ForEach-Object { Write-Host ("[stop] " + $_) }
        } catch {
            Write-Host ("[stop] taskkill: " + $_.Exception.Message)
        }
        for ($i = 0; $i -lt 40; $i++) {
            $alive = Get-Process -Id $Proc.Id -ErrorAction SilentlyContinue
            if ($null -eq $alive) { break }
            Start-Sleep -Milliseconds 250
        }
    }
    $strays = @(Get-StrayPython -Pattern $StrayCommandLinePattern)
    Write-Host ("[stop] stray python = " + $strays.Count)
    return $strays
}

function Test-PortListening {
    <#
      .SYNOPSIS
        True when something is listening on the given TCP port (netstat -- works on 5.1 with no modules).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][int]$Port)
    $needle = ':' + $Port
    $rows = @(& netstat.exe -ano -p TCP | Select-String -SimpleMatch $needle | Select-String -SimpleMatch 'LISTENING')
    return ($rows.Count -gt 0)
}

function Assert-QuietPorts {
    <#
      .SYNOPSIS
        Report which of the ports this repo must not disturb are listening.
    #>
    [CmdletBinding()]
    param([int[]]$Port = $script:ForbiddenPorts)
    $busy = @()
    foreach ($p in $Port) {
        if (Test-PortListening -Port $p) { $busy += $p }
    }
    Write-Host ("[ports] reserved busy = " + ($busy -join ', '))
    return $busy
}

# ------------------------------------------------------------------ windows

function Get-ProcessWindows {
    <#
      .SYNOPSIS
        Top level windows of the process, by UI Automation first and by Win32 as the fallback.
      .DESCRIPTION
        RootElement.FindAll(Children, ProcessId) is fast but incomplete: the launcher's own modal
        FirstRunWizard is a visible top level HWND that it never returns (measured 2026-09-05 with
        EnumWindows -- title "first run" was there while UIA listed only MainWindow). So every HWND
        the process owns is walked as well and turned into an element with FromHandle, and the two
        lists are merged on the native handle.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Proc)

    $found = @()
    $seen = @{}

    $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::ProcessIdProperty, $Proc.Id)
    foreach ($el in @([System.Windows.Automation.AutomationElement]::RootElement.FindAll($script:TS::Children, $cond))) {
        $handle = 0
        try { $handle = [int]$el.Current.NativeWindowHandle } catch { $handle = 0 }
        if (-not $seen.ContainsKey($handle)) {
            $seen[$handle] = $true
            $found += $el
        }
    }

    foreach ($handle in (Get-ProcessWindowHandles -Proc $Proc)) {
        if ($seen.ContainsKey([int]$handle)) { continue }
        $seen[[int]$handle] = $true
        try {
            $el = [System.Windows.Automation.AutomationElement]::FromHandle([System.IntPtr]$handle)
        } catch {
            $el = $null
        }
        if ($null -ne $el) { $found += $el }
    }

    return @($found)
}

function Get-ProcessWindowHandles {
    <#
      .SYNOPSIS
        Every visible top level HWND owned by the process (Win32 EnumWindows).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Proc)

    $wanted = [int]$Proc.Id
    $handles = New-Object System.Collections.ArrayList
    $callback = [Ywk.NativeProbe+EnumWindowsProc] {
        param([System.IntPtr]$hWnd, [System.IntPtr]$lParam)
        $owner = 0
        $null = [Ywk.NativeProbe]::GetWindowThreadProcessId($hWnd, [ref]$owner)
        if (($owner -eq $wanted) -and [Ywk.NativeProbe]::IsWindowVisible($hWnd)) {
            $null = $handles.Add($hWnd)
        }
        return $true
    }
    $null = [Ywk.NativeProbe]::EnumWindows($callback, [System.IntPtr]::Zero)
    return @($handles)
}

function Get-MainWindow {
    <#
      .SYNOPSIS
        The launcher main window (AutomationId 'MainWindow'). Waits for it to appear.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Proc,
        [int]$TimeoutSeconds = 60
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in Get-ProcessWindows $Proc) {
            if ($w.Current.AutomationId -eq 'MainWindow') {
                $null = [Ywk.NativeProbe]::SetForegroundWindow([System.IntPtr]$w.Current.NativeWindowHandle)
                Write-Host ("[window] " + $w.Current.Name)
                return $w
            }
        }
        Start-Sleep -Milliseconds 400
    }
    throw "MainWindow was not found"
}

function Get-DialogWindow {
    <#
      .SYNOPSIS
        A modal window of the process other than the main window (file dialog, message box).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Proc,
        [int]$TimeoutSeconds = 20
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in Get-ProcessWindows $Proc) {
            if ($w.Current.AutomationId -ne 'MainWindow') { return $w }
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

# ------------------------------------------------------------------ elements

function Find-ById {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [int]$TimeoutSeconds = 10
    )
    $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::AutomationIdProperty, $Id)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        $el = $Root.FindFirst($script:TS::Descendants, $cond)
        if ($null -ne $el) { return $el }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 300
    }
}

function Find-ByName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Type = '',
        [int]$TimeoutSeconds = 10
    )
    $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::NameProperty, $Name)
    if (-not [string]::IsNullOrEmpty($Type)) {
        $ct = [System.Windows.Automation.ControlType]::$Type
        $cond = New-Object System.Windows.Automation.AndCondition(
            $cond,
            (New-Object System.Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, $ct)))
    }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        $el = $Root.FindFirst($script:TS::Descendants, $cond)
        if ($null -ne $el) { return $el }
        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 300
    }
}

function Select-Tab {
    <#
      .SYNOPSIS
        Select a TabItem by AutomationId. MUST be called before touching anything inside that tab:
        WPF creates the content of a TabItem only when it is selected.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Window,
        [Parameter(Mandatory = $true)][string]$TabId
    )
    $tab = Find-ById -Root $Window -Id $TabId -TimeoutSeconds 20
    if ($null -eq $tab) { throw ("Tab not found: " + $TabId) }
    $pattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
    Start-Sleep -Milliseconds 700
    return $tab
}

function Invoke-ButtonById {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [int]$TimeoutSeconds = 10
    )
    $el = Find-ById -Root $Root -Id $Id -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $el) { throw ("Button not found: " + $Id) }
    if (-not $el.Current.IsEnabled) { throw ("Button is disabled: " + $Id) }
    $pattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 400
    return $el
}

function Set-TextById {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text,
        [int]$TimeoutSeconds = 10
    )
    $el = Find-ById -Root $Root -Id $Id -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $el) { throw ("Text box not found: " + $Id) }
    $pattern = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($Text)
    Start-Sleep -Milliseconds 250
    return $el
}

function Set-ToggleById {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][bool]$On
    )
    $el = Find-ById -Root $Root -Id $Id -TimeoutSeconds 10
    if ($null -eq $el) { throw ("Toggle not found: " + $Id) }
    $tp = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $guard = 0
    while ((($tp.Current.ToggleState -eq 'On') -ne $On) -and ($guard -lt 3)) {
        $tp.Toggle()
        Start-Sleep -Milliseconds 250
        $guard++
    }
    return $el
}

function Select-ComboItemById {
    <#
      .SYNOPSIS
        Expand a ComboBox and select the ListItem whose Name equals (or contains) the given text.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$ItemText,
        [switch]$Contains
    )
    $combo = Find-ById -Root $Root -Id $Id -TimeoutSeconds 15
    if ($null -eq $combo) { throw ("Combo not found: " + $Id) }
    $ec = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand()
    Start-Sleep -Milliseconds 600
    $item = $null
    if ($Contains) {
        $ct = [System.Windows.Automation.ControlType]::ListItem
        $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, $ct)
        foreach ($candidate in @($combo.FindAll($script:TS::Descendants, $cond))) {
            if ($candidate.Current.Name -like ('*' + $ItemText + '*')) { $item = $candidate; break }
        }
    } else {
        $item = Find-ByName -Root $combo -Name $ItemText -Type 'ListItem' -TimeoutSeconds 5
    }
    if ($null -eq $item) {
        try { $ec.Collapse() } catch { }
        throw ("Combo item not found in " + $Id + ": " + $ItemText)
    }
    $name = $item.Current.Name
    $sp = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $sp.Select()
    Start-Sleep -Milliseconds 400
    try { $ec.Collapse() } catch { }
    return $name
}

function Get-ComboItemNames {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id
    )
    $combo = Find-ById -Root $Root -Id $Id -TimeoutSeconds 15
    if ($null -eq $combo) { throw ("Combo not found: " + $Id) }
    $ec = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand()
    Start-Sleep -Milliseconds 600
    $ct = [System.Windows.Automation.ControlType]::ListItem
    $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, $ct)
    $names = @()
    foreach ($item in @($combo.FindAll($script:TS::Descendants, $cond))) {
        $names += $item.Current.Name
    }
    try { $ec.Collapse() } catch { }
    return $names
}

# ------------------------------------------------------------------ reading

function Get-ElValue {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Element)
    try {
        return ($Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    } catch {
        return $null
    }
}

function Get-ElText {
    <#
      .SYNOPSIS
        Best-effort text of an element: ValuePattern first (TextBox), then Name (TextBlock).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Element)
    $v = Get-ElValue -Element $Element
    if (-not [string]::IsNullOrEmpty($v)) { return $v }
    try {
        return ($Element.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)).DocumentRange.GetText(-1)
    } catch {
        return $Element.Current.Name
    }
}

function Get-TextById {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [int]$TimeoutSeconds = 10
    )
    $el = Find-ById -Root $Root -Id $Id -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $el) { return $null }
    return (Get-ElText -Element $el)
}

function Wait-ForTextById {
    <#
      .SYNOPSIS
        Poll one element until its text matches the regex. Returns ok / elapsed / text / failed.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [int]$TimeoutSeconds = 120,
        [string]$FailPattern = ''
    )
    $start = Get-Date
    $deadline = $start.AddSeconds($TimeoutSeconds)
    $last = ''
    while ($true) {
        $text = Get-TextById -Root $Root -Id $Id -TimeoutSeconds 2
        if ($null -ne $text) { $last = $text }
        if ($last -match $Pattern) {
            return [pscustomobject]@{ ok = $true; elapsed = ((Get-Date) - $start).TotalSeconds; text = $last; failed = $false }
        }
        if ((-not [string]::IsNullOrEmpty($FailPattern)) -and ($last -match $FailPattern)) {
            return [pscustomobject]@{ ok = $false; elapsed = ((Get-Date) - $start).TotalSeconds; text = $last; failed = $true }
        }
        if ((Get-Date) -ge $deadline) {
            return [pscustomobject]@{ ok = $false; elapsed = ((Get-Date) - $start).TotalSeconds; text = $last; failed = $false }
        }
        Start-Sleep -Milliseconds 500
    }
}

function Get-GridRows {
    <#
      .SYNOPSIS
        Every DataItem row of a DataGrid with the text of its cells.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [int]$TimeoutSeconds = 15
    )
    $grid = Find-ById -Root $Root -Id $Id -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $grid) { throw ("Grid not found: " + $Id) }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $script:AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    $cellCond = New-Object System.Windows.Automation.PropertyCondition(
        $script:AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $rows = @()
    foreach ($row in @($grid.FindAll($script:TS::Descendants, $cond))) {
        $cells = @()
        foreach ($cell in @($row.FindAll($script:TS::Descendants, $cellCond))) {
            $cells += $cell.Current.Name
        }
        $rows += [pscustomobject]@{ name = $row.Current.Name; cells = $cells }
    }
    return $rows
}

function Select-GridRow {
    <#
      .SYNOPSIS
        Select the DataGrid row that has a cell (or a row name) equal to the given text.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$CellText,
        [int]$TimeoutSeconds = 15
    )
    $grid = Find-ById -Root $Root -Id $Id -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $grid) { throw ("Grid not found: " + $Id) }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $script:AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    $cellCond = New-Object System.Windows.Automation.PropertyCondition(
        $script:AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($row in @($grid.FindAll($script:TS::Descendants, $cond))) {
        $match = ($row.Current.Name -eq $CellText)
        if (-not $match) {
            foreach ($cell in @($row.FindAll($script:TS::Descendants, $cellCond))) {
                if ($cell.Current.Name -eq $CellText) { $match = $true; break }
            }
        }
        if ($match) {
            $sp = $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $sp.Select()
            Start-Sleep -Milliseconds 400
            return $true
        }
    }
    return $false
}

function Complete-FileDialog {
    <#
      .SYNOPSIS
        Type a path into the common file dialog the launcher opened and press its default button.
        The Vista style IFileDialog exposes the file name edit as AutomationId '1148' and the accept
        button as '1'; both are matched by id so the dialog's display language does not matter.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Proc,
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$TimeoutSeconds = 20
    )
    $dialog = Get-DialogWindow -Proc $Proc -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $dialog) { throw "File dialog did not appear" }

    $edit = Find-ById -Root $dialog -Id '1148' -TimeoutSeconds 5
    if ($null -eq $edit) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            $script:AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
        $edit = $dialog.FindFirst($script:TS::Descendants, $cond)
    }
    if ($null -eq $edit) { throw "File dialog has no file name edit box" }
    ($edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($Path)
    Start-Sleep -Milliseconds 400

    $accept = Find-ById -Root $dialog -Id '1' -TimeoutSeconds 5
    if ($null -eq $accept) { throw "File dialog has no accept button" }
    ($accept.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 900
    Write-Host ("[dialog] accepted " + $Path)
    return $true
}

function Complete-MessageBox {
    <#
      .SYNOPSIS
        Press a button (by AutomationId, '1' = OK, '2' = Cancel) on a modal message box.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Proc,
        [string]$ButtonId = '1',
        [int]$TimeoutSeconds = 15
    )
    $dialog = Get-DialogWindow -Proc $Proc -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $dialog) { return $false }
    $btn = Find-ById -Root $dialog -Id $ButtonId -TimeoutSeconds 5
    if ($null -eq $btn) { return $false }
    ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 600
    return $true
}

function Write-UiaTree {
    <#
      .SYNOPSIS
        Dump the control view of an element to a file (control type / id / name / value).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$OutFile,
        [int]$MaxDepth = 40
    )
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# uia dump ' + (Get-Date).ToString('o'))

    $stack = New-Object System.Collections.Generic.Stack[object]
    $stack.Push([pscustomobject]@{ el = $Root; depth = 0 })
    while ($stack.Count -gt 0) {
        $node = $stack.Pop()
        if ($node.depth -gt $MaxDepth) { continue }
        $current = $null
        try { $current = $node.el.Current } catch { continue }
        $pad = ' ' * ($node.depth * 2)
        $type = $current.ControlType.ProgrammaticName.Replace('ControlType.', '')
        $value = Get-ElValue -Element $node.el
        $extra = ''
        if (-not [string]::IsNullOrEmpty($value)) {
            $extra = " value='" + ($value -replace "\r?\n", ' / ') + "'"
        }
        $lines.Add($pad + '[' + $type + "] id='" + $current.AutomationId + "' name='" +
            ($current.Name -replace "\r?\n", ' / ') + "' enabled=" + $current.IsEnabled + $extra)

        $children = @()
        $child = $walker.GetFirstChild($node.el)
        while ($null -ne $child) {
            $children += $child
            $child = $walker.GetNextSibling($child)
        }
        for ($i = $children.Count - 1; $i -ge 0; $i--) {
            $stack.Push([pscustomobject]@{ el = $children[$i]; depth = $node.depth + 1 })
        }
    }
    $dir = Split-Path -Parent $OutFile
    if ((-not [string]::IsNullOrEmpty($dir)) -and (-not (Test-Path -LiteralPath $dir))) {
        $null = New-Item -ItemType Directory -Path $dir -Force
    }
    [System.IO.File]::WriteAllLines($OutFile, $lines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("[dump] " + $OutFile + " (" + $lines.Count + " lines)")
    return $OutFile
}
