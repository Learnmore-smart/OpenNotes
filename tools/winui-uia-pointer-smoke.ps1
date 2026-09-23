# Real-pointer smoke: moves the physical mouse and sends real clicks at UIA
# bounding rectangles. Unlike InvokePattern.Invoke(), this exercises the real
# nonclient caption-rect passthrough for title-bar buttons.
# Prereq: OpenNotes.WinUI.exe running with a visible window.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class MouseInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint MIDDLEDOWN = 0x0020;
    public const uint MIDDLEUP = 0x0040;
    public static void LeftClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }
    public static void MiddleClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

function Get-Window {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "OpenNotes")
    for ($i = 0; $i -lt 20; $i++) {
        $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($w -ne $null) { return $w }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Find-ByAutomationId($parent, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-AllByAutomationId($parent, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Center-Point($el) {
    $r = $el.Current.BoundingRectangle
    return [int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)
}

$win = Get-Window
if ($win -eq $null) { Write-Output "FAIL: no OpenNotes window"; exit 1 }

# Restore to Normal first (in case a previous run minimized it).
$wp = $win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
if ($wp.Current.WindowVisualState -ne "Normal") {
    $wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Start-Sleep -Milliseconds 800
    $win = Get-Window
}
$wp.Current.WindowVisualState | ForEach-Object { Write-Output "window-state-start=$_" }

$tabs = Find-AllByAutomationId $win "AppTab"
Write-Output "realclick-tab-count-start=$($tabs.Count)"

# 1) Real click on NewTabButton -> +1 tab.
$px, $py = Center-Point (Find-ByAutomationId $win "NewTabButton")
[MouseInput]::LeftClick($px, $py)
Start-Sleep -Milliseconds 400
$tabs = Find-AllByAutomationId $win "AppTab"
Write-Output "realclick-tab-count-after-new=$($tabs.Count)"

# 2) Real click on the FIRST tab (leftmost by rect) -> it must become selected.
$tabs = Find-AllByAutomationId $win "AppTab"
$sorted = @($tabs) | Sort-Object { $_.Current.BoundingRectangle.X }
$first = $sorted[0]
$clickedRect = $first.Current.BoundingRectangle
$px, $py = Center-Point $first
[MouseInput]::LeftClick($px, $py)
Start-Sleep -Milliseconds 400
$strip = Find-ByAutomationId $win "TabStrip"
$selPattern = $strip.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
$sel = $selPattern.Current.GetSelection()
$selRect = if ($sel.Count -gt 0) { $sel[0].Current.BoundingRectangle } else { $null }
$match = $selRect -ne $null -and [math]::Abs($selRect.X - $clickedRect.X) -lt 1 -and [math]::Abs($selRect.Y - $clickedRect.Y) -lt 1
Write-Output "realclick-first-tab-selected=$match"

# 3) Real MIDDLE click on the last tab -> closes it.
$tabs = Find-AllByAutomationId $win "AppTab"
$before = $tabs.Count
$last = $tabs[$tabs.Count - 1]
$px, $py = Center-Point $last
[MouseInput]::MiddleClick($px, $py)
Start-Sleep -Milliseconds 400
$tabs = Find-AllByAutomationId $win "AppTab"
Write-Output "realclick-middleclose-count=$($tabs.Count) (was $before)"

# 4) Real click on MinimizeButton (inside caption rect).
$px, $py = Center-Point (Find-ByAutomationId $win "MinimizeButton")
[MouseInput]::LeftClick($px, $py)
Start-Sleep -Milliseconds 900
$win2 = Get-Window
if ($win2 -eq $null) {
    Write-Output "realclick-minimize-state=window-gone"
} else {
    $wp2 = $win2.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    Write-Output "realclick-minimize-state=$($wp2.Current.WindowVisualState)"
    $wp2.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Start-Sleep -Milliseconds 900
    $win2 = Get-Window
}

# 5) Real click on MaximizeButton -> maximized; click again -> normal.
$px, $py = Center-Point (Find-ByAutomationId $win "MaximizeButton")
[MouseInput]::LeftClick($px, $py)
Start-Sleep -Milliseconds 900
$wp3 = (Get-Window).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
Write-Output "realclick-maximize-state=$($wp3.Current.WindowVisualState)"
# Re-fetch: the button moved when the window filled the screen.
$px, $py = Center-Point (Find-ByAutomationId (Get-Window) "MaximizeButton")
[MouseInput]::LeftClick($px, $py)
Start-Sleep -Milliseconds 900
$wp4 = (Get-Window).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
Write-Output "realclick-restore-state=$($wp4.Current.WindowVisualState)"

# 6) Real click on NavHomeButton (child INSIDE the SetTitleBar drag element).
$px, $py = Center-Point (Find-ByAutomationId $win "NavHomeButton")
[MouseInput]::LeftClick($px, $py)
Start-Sleep -Milliseconds 300
Write-Output "realclick-navhome-done"

Write-Output "REAL-POINTER-SMOKE-DONE"
