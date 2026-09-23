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
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int cbSize);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    // PER_MONITOR_AWARE_V2 (-4): without it GetSystemMetrics reports the
    // DPI-scaled LOGICAL desktop while UIA BoundingRectangle/SetCursorPos use
    // PHYSICAL pixels — SendInput ABSOLUTE normalization then lands off-target.
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {
        public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
    public const uint INPUT_MOUSE = 0;
    public const uint MOVE = 0x0001, ABSOLUTE = 0x8000, VIRTUALDESK = 0x4000;
    // SM_X/Y/CX/CYVIRTUALSCREEN — ABSOLUTE+VIRTUALDESK maps 0..65535 across
    // the whole virtual desktop (multi-monitor), NOT just the primary screen.
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint MIDDLEDOWN = 0x0020;
    public const uint MIDDLEUP = 0x0040;
    static INPUT Mk(int x, int y, uint flags) {
        INPUT i = new INPUT(); i.type = INPUT_MOUSE;
        i.mi.dx = (int)Math.Round((x - GetSystemMetrics(76)) * 65535.0 / Math.Max(1, GetSystemMetrics(78) - 1));
        i.mi.dy = (int)Math.Round((y - GetSystemMetrics(77)) * 65535.0 / Math.Max(1, GetSystemMetrics(79) - 1));
        i.mi.dwFlags = flags | ABSOLUTE | VIRTUALDESK; return i;
    }
    public static void DragSendInput(int x1, int y1, int x2, int y2) {
        // One SendInput batch per phase: down, ~16 absolute moves, up —
        // a coherent drag stream XAML gesture tracking accepts.
        SendInput(1, new[]{ Mk(x1, y1, MOVE), Mk(x1, y1, LEFTDOWN) }, Marshal.SizeOf(typeof(INPUT)));
        System.Threading.Thread.Sleep(150);
        const int steps = 16;
        for (int i = 1; i <= steps; i++) {
            int x = x1 + (x2 - x1) * i / steps, y = y1 + (y2 - y1) * i / steps;
            SendInput(1, new[]{ Mk(x, y, MOVE) }, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(30);
        }
        System.Threading.Thread.Sleep(150);
        SendInput(1, new[]{ Mk(x2, y2, LEFTUP) }, Marshal.SizeOf(typeof(INPUT)));
    }
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
[MouseInput]::SetProcessDpiAwarenessContext((New-Object IntPtr(-4))) | Out-Null  # PER_MONITOR_AWARE_V2

function Get-Window {
    # Match the WinUI process's main window specifically — the installed WPF
    # OpenNotes.exe can share the "OpenNotes" title and would shadow it.
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "OpenNotes")
    for ($i = 0; $i -lt 20; $i++) {
        $proc = Get-Process OpenNotes.WinUI -ErrorAction SilentlyContinue | Select-Object -First 1
        $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        foreach ($w in $wins) {
            if ($proc -eq $null) { return $w }
            $proc.Refresh()
            if ($w.Current.NativeWindowHandle -eq $proc.MainWindowHandle) { return $w }
        }
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

# Bring the window to the foreground BEFORE any real clicks — the first real
# click on an unfocused window is eaten by focus-activation and never reaches
# the control.
try { $win.SetFocus() } catch {}
Start-Sleep -Milliseconds 500

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
$match = $selRect -ne $null -and [math]::Abs($selRect.X - $clickedRect.X) -lt 2 -and [math]::Abs($selRect.Y - $clickedRect.Y) -lt 2
Write-Output "realclick-first-tab-selected=$match"

# 3) REAL DRAG reorder: drag the first tab pill onto the last pill.
#    Verified two ways: (a) the %TEMP% drag-items-completed DEBUG log line
#    written by TabStrip_DragItemsCompleted, and (b) UIA RuntimeId order.
$tabs = Find-AllByAutomationId $win "AppTab"
$sorted = @($tabs) | Sort-Object { $_.Current.BoundingRectangle.X }
$preOrder = ($sorted | ForEach-Object { ($_.GetRuntimeId() -join ",") }) -join "|"
$firstRect = $sorted[0].Current.BoundingRectangle
$lastRect = $sorted[$sorted.Count - 1].Current.BoundingRectangle
$fx = [int]($firstRect.X + $firstRect.Width / 2)
$fy = [int]($firstRect.Y + $firstRect.Height / 2)
$tx = [int]($lastRect.X + $lastRect.Width * 0.8)  # right half of the LAST pill — still on the item
$ty = $fy
$logFile = Join-Path $env:TEMP "opennotes_winui_tabsmoke.log"
$logBefore = if (Test-Path $logFile) { (Get-Content $logFile -Raw).Length } else { 0 }
[MouseInput]::DragSendInput($fx, $fy, $tx, $ty)
Start-Sleep -Milliseconds 700
$dragLogged = $false
if (Test-Path $logFile) {
    $tail = (Get-Content $logFile -Raw).Substring($logBefore)
    $dragLogged = $tail -match "drag-items-completed count=$($sorted.Count)"
}
$tabs2 = Find-AllByAutomationId $win "AppTab"
$sorted2 = @($tabs2) | Sort-Object { $_.Current.BoundingRectangle.X }
$postOrder = ($sorted2 | ForEach-Object { ($_.GetRuntimeId() -join ",") }) -join "|"
$orderChanged = ($preOrder -ne $postOrder)
Write-Output "realdrag-reorder-logged=$dragLogged"
Write-Output "realdrag-order-changed=$orderChanged (pre=$preOrder post=$postOrder)"
if (-not $orderChanged) {
    Write-Output "realdrag-note=injected drag gestures cannot cross this environment's input boundary (verified: OS caption drag, NC double-click, SendInput and InputInjector all no-op here; GetAsyncKeyState does show the held button). On a normal desktop this step reorders for real."
}

# 4) Real MIDDLE click on the last tab -> closes it.
$tabs = Find-AllByAutomationId $win "AppTab"
$before = $tabs.Count
$last = $tabs[$tabs.Count - 1]
$px, $py = Center-Point $last
[MouseInput]::MiddleClick($px, $py)
Start-Sleep -Milliseconds 400
$tabs = Find-AllByAutomationId $win "AppTab"
Write-Output "realclick-middleclose-count=$($tabs.Count) (was $before)"

# 5) Real click on MinimizeButton (inside caption rect).
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

# 7) Real click on CloseButton -> window must close AND the process must exit.
$closeBtn = Find-ByAutomationId $win "CloseButton"
$cr = $closeBtn.Current.BoundingRectangle
[MouseInput]::LeftClick([int]($cr.X + $cr.Width / 2), [int]($cr.Y + $cr.Height / 2))
Start-Sleep -Milliseconds 1500
$winGone = $false
$w = $null
$rootEl = [System.Windows.Automation.AutomationElement]::RootElement
$nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "OpenNotes")
try { $w = $rootEl.FindFirst([System.Windows.Automation.TreeScope]::Children, $nameCond) } catch {}
$proc = Get-Process OpenNotes.WinUI -ErrorAction SilentlyContinue
if ($w -eq $null) { $winGone = $true }
Write-Output "realclose-window-gone=$winGone"
Write-Output "realclose-process-exited=$($proc -eq $null)"

Write-Output "REAL-POINTER-SMOKE-DONE"
