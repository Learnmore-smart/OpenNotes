# WinUI smoke driver: uses UI Automation to click the real chrome buttons and
# tab-strip controls inside a running OpenNotes.WinUI window.
# Usage: powershell -File tools\winui-uia-smoke.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

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

function Invoke-Element($el, [string]$label) {
    $invoke = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    Write-Output "invoked: $label"
}

$win = Get-Window
if ($win -eq $null) { Write-Output "FAIL: no OpenNotes window"; exit 1 }
Write-Output "window-found: $($win.Current.Name) hwnd=$($win.Current.NativeWindowHandle)"

$tabs = Find-AllByAutomationId $win "AppTab"
Write-Output "tab-count-initial=$($tabs.Count)"

# Click the real NewTabButton twice -> 3 tabs total.
$newTab = Find-ByAutomationId $win "NewTabButton"
if ($newTab -eq $null) { Write-Output "FAIL: NewTabButton not found"; exit 1 }
Invoke-Element $newTab "NewTabButton#1"
Start-Sleep -Milliseconds 300
Invoke-Element $newTab "NewTabButton#2"
Start-Sleep -Milliseconds 400
$tabs = Find-AllByAutomationId $win "AppTab"
Write-Output "tab-count-after-newx2=$($tabs.Count)"

# Close one tab via its own close button (visible only when >1 tab).
$closeBtn = Find-ByAutomationId $win "TabCloseButton"
if ($closeBtn -eq $null) { Write-Output "FAIL: TabCloseButton not found"; exit 1 }
Invoke-Element $closeBtn "TabCloseButton"
Start-Sleep -Milliseconds 400
$tabs = Find-AllByAutomationId $win "AppTab"
Write-Output "tab-count-after-close=$($tabs.Count)"

# Caption buttons inside the title-bar caption rect: proves the WinUI
# automatic passthrough keeps custom chrome clickable.
$minBtn = Find-ByAutomationId $win "MinimizeButton"
if ($minBtn -eq $null) { Write-Output "FAIL: MinimizeButton not found"; exit 1 }
Invoke-Element $minBtn "MinimizeButton"
Start-Sleep -Milliseconds 700
$win2 = Get-Window
if ($win2 -eq $null) {
    Write-Output "window-state-after-minimize=not-found(minimized)"
} else {
    $wp = $win2.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    Write-Output "window-state-after-minimize=$($wp.Current.WindowVisualState)"
}

$maxBtn = Find-ByAutomationId $win "MaximizeButton"
if ($maxBtn -eq $null) { Write-Output "FAIL: MaximizeButton not found"; exit 1 }
Invoke-Element $maxBtn "MaximizeButton"
Start-Sleep -Milliseconds 700
$win3 = Get-Window
if ($win3 -ne $null) {
    $wp = $win3.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    Write-Output "window-state-after-maximize=$($wp.Current.WindowVisualState)"
    Invoke-Element $maxBtn "MaximizeButton#restore"
    Start-Sleep -Milliseconds 700
    $win4 = Get-Window
    if ($win4 -ne $null) {
        $wp = $win4.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        Write-Output "window-state-after-restore=$($wp.Current.WindowVisualState)"
    }
}

# NavHomeButton: inside the AppTitleBar drag element (tests passthrough for children).
$homeBtn = Find-ByAutomationId $win "NavHomeButton"
if ($homeBtn -eq $null) { Write-Output "FAIL: NavHomeButton not found"; exit 1 }
Invoke-Element $homeBtn "NavHomeButton"
Start-Sleep -Milliseconds 300

Write-Output "UIA-SMOKE-DONE"
