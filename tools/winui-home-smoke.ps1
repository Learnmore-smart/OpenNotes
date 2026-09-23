# WinUI HomePage smoke: seeds a library under a throwaway OPENNOTES_DATA_ROOT,
# launches the packaged OpenNotes.WinUI.exe, then drives UI Automation (plus a
# real right-click for the context-menu check) to prove the Task 5 port:
#   - tiles render (add tile, folder tile, file tiles)
#   - folder opens -> breadcrumb + nested tile -> NavigateUpButton returns
#   - SearchBox filters tiles
#   - right-click on a file tile opens the MenuFlyout context menu
#   - SelectButton shows the selection action bar (Done hides it)
#   - clicking a file tile navigates to the EditorPage stub
#   - MoreButton flyout opens
# Usage: powershell -File tools\winui-home-smoke.ps1
# Prereq: Debug build exists under OpenNotes.WinUI\bin\x64\Debug\...\win-x64.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class HomeSmokeMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    public struct POINT { public int X; public int Y; }
    // Returns the top-level window that owns the given screen point.
    public static IntPtr TopLevelWindowAt(int x, int y) {
        POINT p; p.X = x; p.Y = y;
        return GetAncestor(WindowFromPoint(p), 2); // GA_ROOT
    }
    public static void Minimize(IntPtr hwnd) { ShowWindow(hwnd, 6); } // SW_MINIMIZE
    public static void Restore(IntPtr hwnd) { ShowWindow(hwnd, 9); } // SW_RESTORE
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;
    public static void RaiseToTop(IntPtr hwnd) {
        // HWND_TOP (0): brings the window above the driving console so real
        // pointer input actually lands on it. Unlike SetForegroundWindow this
        // is not blocked by the foreground-permission rules.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
    public static void LeftClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }
    public static void RightClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(RIGHTUP, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

$script:results = New-Object System.Collections.Generic.List[string]
$script:minimizedHwnds = New-Object System.Collections.Generic.List[IntPtr]
function Check([bool]$ok, [string]$label) {
    $mark = if ($ok) { "PASS" } else { "FAIL" }
    $script:results.Add("$mark $label")
    Write-Output "$mark $label"
}

function Get-Window {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "OpenNotes")
    for ($i = 0; $i -lt 40; $i++) {
        $proc.Refresh()
        $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        # Another process can own a same-titled window — only drive ours.
        if ($w -ne $null) {
            try {
                if ($w.Current.NativeWindowHandle -eq $proc.MainWindowHandle) { return $w }
            } catch {}
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

function Wait-ForAutomationId($parent, [string]$id, [int]$tries = 20) {
    for ($i = 0; $i -lt $tries; $i++) {
        $el = Find-ByAutomationId $parent $id
        if ($el -ne $null) { return $el }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Center-Point($el) {
    $r = $el.Current.BoundingRectangle
    return [int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)
}

function Invoke-Element($el) {
    $invoke = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
}

# The clickable icon Button inside a tile (InvokePattern works without
# window focus — real mouse clicks do not).
function Invoke-TileButton($tileEl) {
    $btnCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $btn = $tileEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    if ($btn -eq $null) { return $false }
    Invoke-Element $btn
    return $true
}

# Real pointer input lands on whatever window is topmost at the point —
# if another top-level window covers the target (console, explorer, etc.)
# minimize it until the app owns the click point. (HWND_TOP alone cannot
# outrank a TOPMOST window; minimizing the cover always works.)
function Clear-ClickPoint($win, [int]$px, [int]$py) {
    $appHwnd = [IntPtr]$win.Current.NativeWindowHandle
    for ($i = 0; $i -lt 10; $i++) {
        try { $owner = [HomeSmokeMouse]::TopLevelWindowAt($px, $py) } catch { return }
        if ($owner -eq $appHwnd) { return }
        if ($owner -eq [IntPtr]::Zero) { return }
        if (-not $script:minimizedHwnds.Contains($owner)) { $script:minimizedHwnds.Add($owner) }
        [HomeSmokeMouse]::Minimize($owner)
        Start-Sleep -Milliseconds 300
    }
}

# Finds a live flyout menu (ControlType.Menu) anywhere on the desktop.
function Find-OpenMenu([int]$tries = 8) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $menuCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Menu)
    for ($i = 0; $i -lt $tries; $i++) {
        $menu = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $menuCond)
        if ($menu -ne $null) { return $menu }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

# Windows eats the activating click: first land a harmless click on the
# window's own chrome so the app is foreground, THEN send the real input.
function Focus-AppWindow($win) {
    try { [HomeSmokeMouse]::RaiseToTop([IntPtr]$win.Current.NativeWindowHandle) } catch {}
    try { $win.SetFocus() } catch {}
    try {
        [HomeSmokeMouse]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
    } catch {}
    $title = Find-ByAutomationId $win "AppTitleBar"
    if ($title -ne $null) {
        $r = $title.Current.BoundingRectangle
        [HomeSmokeMouse]::LeftClick([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
        Start-Sleep -Milliseconds 300
    }
}

# ── 1) Seed library under a throwaway data root ──────────────────────────────

$dataRoot = Join-Path $env:TEMP ("opennotes_homesmoke_" + [Guid]::NewGuid().ToString("N"))
$caelumDir = Join-Path $dataRoot "Caelum"
New-Item -ItemType Directory -Force -Path $caelumDir | Out-Null

$docsDir = Join-Path $dataRoot "docs"
New-Item -ItemType Directory -Force -Path $docsDir | Out-Null

$pdfBytes = [System.Text.Encoding]::ASCII.GetBytes(
    "%PDF-1.4`n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj`n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj`n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj`ntrailer<</Root 1 0 R>>`n")

$pdfA = Join-Path $docsDir "smoke-a.pdf"
$pdfB = Join-Path $docsDir "smoke-b.pdf"
$pdfNested = Join-Path $docsDir "nested.pdf"
[System.IO.File]::WriteAllBytes($pdfA, $pdfBytes)
[System.IO.File]::WriteAllBytes($pdfB, $pdfBytes)
[System.IO.File]::WriteAllBytes($pdfNested, $pdfBytes)

$now = (Get-Date).ToUniversalTime().ToString("o")
$folderId = "smokefolder0000000000000000000001"
$entries = @(
    [ordered]@{ Id = $folderId; EntryType = "folder"; ParentFolderId = ""; DisplayName = "SmokeFolder";
                IsNotebook = $false; Path = ""; PageCount = 0; LastModifiedUtc = $now; LastOpenedUtc = $now; Color = "#3B82F6" },
    [ordered]@{ Id = "smokefilea0000000000000000000001"; EntryType = "file"; ParentFolderId = ""; DisplayName = "";
                IsNotebook = $false; Path = $pdfA; PageCount = 0; LastModifiedUtc = $now; LastOpenedUtc = $now; Color = "" },
    [ordered]@{ Id = "smokefileb0000000000000000000001"; EntryType = "file"; ParentFolderId = ""; DisplayName = "";
                IsNotebook = $false; Path = $pdfB; PageCount = 0; LastModifiedUtc = $now; LastOpenedUtc = $now; Color = "" },
    [ordered]@{ Id = "smokefilen0000000000000000000001"; EntryType = "file"; ParentFolderId = $folderId; DisplayName = "";
                IsNotebook = $false; Path = $pdfNested; PageCount = 0; LastModifiedUtc = $now; LastOpenedUtc = $now; Color = "" }
)
$entries | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $caelumDir "recent_files.json") -Encoding UTF8
Write-Output "seeded-library=$caelumDir files=$docsDir"

# ── 2) Launch the app against the seeded root ────────────────────────────────

$exe = Join-Path $PSScriptRoot "..\OpenNotes.WinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\OpenNotes.WinUI.exe"
if (-not (Test-Path $exe)) {
    Write-Output "FAIL app-exe-missing $exe"
    exit 1
}
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["OPENNOTES_DATA_ROOT"] = $dataRoot
$proc = [System.Diagnostics.Process]::Start($psi)
Write-Output "launched pid=$($proc.Id)"

$win = Get-Window
Check ($win -ne $null) "window-found"
if ($win -eq $null) { $proc.Kill(); exit 1 }
try { $win.SetFocus() } catch {}
Start-Sleep -Milliseconds 1200

try {
    # ── 3) Tiles render ───────────────────────────────────────────────────
    $addTile = Wait-ForAutomationId $win "HomeTile_Add"
    Check ($addTile -ne $null) "add-tile-rendered"
    $folderTile = Wait-ForAutomationId $win "HomeTile_SmokeFolder"
    Check ($folderTile -ne $null) "folder-tile-rendered"
    $tileA = Find-ByAutomationId $win "HomeTile_smoke-a.pdf"
    $tileB = Find-ByAutomationId $win "HomeTile_smoke-b.pdf"
    Check ($tileA -ne $null) "file-tile-a-rendered"
    Check ($tileB -ne $null) "file-tile-b-rendered"
    Check ((Wait-ForAutomationId $win "HomeTitleTextBlock" 5) -ne $null) "home-title-rendered"
    $search0 = Wait-ForAutomationId $win "SearchBox" 5
    Check ($search0 -ne $null -and -not $search0.Current.IsOffscreen) "home-toolbar-visible-on-home"

    # ── 4) Right-click context menu on a file tile ────────────────────────
    $menu = $null
    if ($tileA -ne $null) {
        for ($attempt = 0; $attempt -lt 3 -and $menu -eq $null; $attempt++) {
            Focus-AppWindow $win
            $tileNow = Find-ByAutomationId $win "HomeTile_smoke-a.pdf"
            if ($tileNow -eq $null) { break }
            $px, $py = Center-Point $tileNow
            Clear-ClickPoint $win $px $py
            [HomeSmokeMouse]::RightClick($px, $py)
            $menu = Find-OpenMenu 6
        }
    }
    Check ($menu -ne $null) "context-menu-opened"
    if ($menu -ne $null) {
        $miCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::MenuItem)
        $items = $menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $miCond)
        $names = @($items | ForEach-Object { $_.Current.Name }) -join "|"
        Write-Output "context-menu-item-count=$($items.Count) items=$names"
        Check ($items.Count -ge 5) "context-menu-has-items"
        [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
        Start-Sleep -Milliseconds 400
    }

    # ── 5) Folder navigation + breadcrumb + navigate-up ──────────────────
    $folderTile2 = Find-ByAutomationId $win "HomeTile_SmokeFolder"
    if ($folderTile2 -ne $null) {
        Check (Invoke-TileButton $folderTile2) "folder-tile-invoked"
        Check ((Wait-ForAutomationId $win "FolderBreadcrumb" 8) -ne $null) "breadcrumb-visible"
        $navUp = Wait-ForAutomationId $win "NavigateUpButton" 8
        Check ($navUp -ne $null) "navigate-up-visible"
        Check ((Wait-ForAutomationId $win "HomeTile_nested.pdf" 8) -ne $null) "nested-file-tile-rendered"

        if ($navUp -ne $null) {
            Invoke-Element $navUp
            Check ((Wait-ForAutomationId $win "HomeTile_SmokeFolder" 10) -ne $null) "navigate-up-returns-to-root"
            Check ((Find-ByAutomationId $win "FolderBreadcrumb") -eq $null) "breadcrumb-hidden-at-root"
        }
    } else {
        Check $false "folder-navigation (folder tile missing)"
    }

    # ── 6) Search filtering ───────────────────────────────────────────────
    $search = Find-ByAutomationId $win "SearchBox"
    Check ($search -ne $null) "searchbox-present"
    if ($search -ne $null) {
        try {
            $vp = $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            $vp.SetValue("smoke-a")
            Start-Sleep -Milliseconds 700
            Check ((Find-ByAutomationId $win "HomeTile_smoke-a.pdf") -ne $null) "search-keeps-matching-tile"
            Check ((Find-ByAutomationId $win "HomeTile_smoke-b.pdf") -eq $null) "search-hides-nonmatching-tile"
            $vp.SetValue("")
            Start-Sleep -Milliseconds 700
        } catch {
            Check $false "search-valuepattern-usable ($($_.Exception.Message))"
        }
    }

    # ── 7) Selection mode shows the action bar ────────────────────────────
    $selectBtn = Find-ByAutomationId $win "SelectButton"
    if ($selectBtn -ne $null) {
        Invoke-Element $selectBtn
        $bar = Wait-ForAutomationId $win "SelectionActionBar" 6
        if ($bar -eq $null) { $bar = Wait-ForAutomationId $win "SelectionSummary" 4 }
        Check ($bar -ne $null) "selection-bar-visible"
        $done = Wait-ForAutomationId $win "DoneSelectionButton" 6
        if ($done -ne $null) {
            Invoke-Element $done
            Start-Sleep -Milliseconds 700
            Check ((Find-ByAutomationId $win "SelectionSummary") -eq $null) "done-hides-selection-bar"
        } else {
            Invoke-Element $selectBtn
            Check $false "done-button-present"
        }
    } else {
        Check $false "select-button-present"
    }

    # ── 8) File tile opens the EditorPage stub ────────────────────────────
    $tileA2 = Find-ByAutomationId $win "HomeTile_smoke-a.pdf"
    if ($tileA2 -ne $null) {
        Check (Invoke-TileButton $tileA2) "file-tile-invoked"
        $editorPath = Wait-ForAutomationId $win "EditorPagePath" 12
        Check ($editorPath -ne $null) "editor-stub-navigated"
        if ($editorPath -ne $null) {
            Check ($editorPath.Current.Name -like "*smoke-a.pdf*") "editor-path-shows-pdf"
        }
        $navHome = Find-ByAutomationId $win "NavHomeButton"
        if ($navHome -ne $null) { Invoke-Element $navHome; Start-Sleep -Milliseconds 700 }
        Check ((Wait-ForAutomationId $win "HomeTile_SmokeFolder" 8) -ne $null) "navhome-returns-to-library"
    } else {
        Check $false "editor-navigation (tile missing)"
    }

    # ── 9) MoreButton flyout exists ───────────────────────────────────────
    $more = Find-ByAutomationId $win "MoreButton"
    Check ($more -ne $null) "more-button-present"
    $menu = $null
    if ($more -ne $null) {
        Invoke-Element $more
        $menu = Find-OpenMenu 8
        Check ($menu -ne $null) "more-flyout-opened"
        if ($menu -ne $null) {
            $miCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::MenuItem)
            $items = $menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $miCond)
            Write-Output "more-flyout-item-count=$($items.Count)"
            Check ($items.Count -ge 3) "more-flyout-has-items"
            [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
            Start-Sleep -Milliseconds 300
        }
    }
}
finally {
    try { $proc.Kill() } catch {}
    # Restore any foreign windows Clear-ClickPoint minimized out of the way.
    foreach ($hwnd in $script:minimizedHwnds) {
        try { [HomeSmokeMouse]::Restore($hwnd) } catch {}
    }
    # The seeded library root is throwaway — remove it.
    try { Remove-Item -Recurse -Force $dataRoot -ErrorAction SilentlyContinue } catch {}
}

$failCount = @($script:results | Where-Object { $_ -like "FAIL*" }).Count
Write-Output "HOME-SMOKE-RESULTS=$($script:results.Count) fails=$failCount"
Write-Output "HOME-SMOKE-DONE"
exit ($(if ($failCount -gt 0) { 1 } else { 0 }))
