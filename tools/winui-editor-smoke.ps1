# WinUI EditorPage smoke: seeds a library with a real 3-page PDF under a
# throwaway OPENNOTES_DATA_ROOT, launches the packaged OpenNotes.WinUI.exe,
# navigates into the editor through a file tile, then drives UI Automation to
# prove the Task 6 shell contract:
#   - PagesContainer + rendered PdfPageControl surfaces exist
#   - full toolbar AutomationId set is discoverable
#   - three-tab sidebar (Pages / Outline / Bookmarks) + 228/32 DIP margin
#     contract via the DEBUG pages-margin-left HelpText probe
#   - collapse button + DEBUG narrow-layout simulation (window floors at 560)
#   - page navigator: Editor.PageJump value + prev/next invoke
#   - zoom in -> ZoomLabel 110%
#   - bookmark toggle on/off
#   - Ctrl+F search panel -> query -> results
#   - right-click context menu with the pinned Editor.ContextMenu.* ids
#   - tab close returns to Home and the process stays alive
# Usage: powershell -File tools\winui-editor-smoke.ps1
# Prereq: Debug build exists under OpenNotes.WinUI\bin\x64\Debug\...\win-x64.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class EditorSmokeMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    public struct POINT { public int X; public int Y; }
    public static IntPtr TopLevelWindowAt(int x, int y) {
        POINT p; p.X = x; p.Y = y;
        IntPtr w = WindowFromPoint(p);
        if (w == IntPtr.Zero) return IntPtr.Zero;
        IntPtr root = GetAncestor(w, 2); // GA_ROOT
        return root == IntPtr.Zero ? w : root;
    }
    public static void RaiseToTop(IntPtr hWnd) {
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040); // NOSIZE|NOMOVE|SHOWWINDOW
    }
    public static void Minimize(IntPtr hWnd) { ShowWindow(hWnd, 6); }
    public static void Restore(IntPtr hWnd) { ShowWindow(hWnd, 9); }
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
function Check([bool]$ok, [string]$label) {
    $mark = if ($ok) { "PASS" } else { "FAIL" }
    $script:results.Add("$mark $label")
    Write-Output "$mark $label"
}

$script:minimizedHwnds = New-Object System.Collections.Generic.List[IntPtr]
function Get-Window {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "OpenNotes")
    for ($i = 0; $i -lt 40; $i++) {
        $proc.Refresh()
        $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        # Another process can own a same-titled window - only drive ours.
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

function Invoke-TileButton($tileEl) {
    $btnCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $btn = $tileEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    if ($btn -eq $null) { return $false }
    Invoke-Element $btn
    return $true
}

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

# Real pointer input lands on whatever window is topmost at the point -
# minimize foreign covers until the app owns the click point.
function Clear-ClickPoint($win, [int]$px, [int]$py) {
    $appHwnd = [IntPtr]$win.Current.NativeWindowHandle
    for ($i = 0; $i -lt 10; $i++) {
        try { $owner = [EditorSmokeMouse]::TopLevelWindowAt($px, $py) } catch { return }
        if ($owner -eq $appHwnd) { return }
        if ($owner -eq [IntPtr]::Zero) { return }
        if (-not $script:minimizedHwnds.Contains($owner)) { $script:minimizedHwnds.Add($owner) }
        [EditorSmokeMouse]::Minimize($owner)
        Start-Sleep -Milliseconds 300
    }
}

function Focus-AppWindow($win) {
    try { [EditorSmokeMouse]::RaiseToTop([IntPtr]$win.Current.NativeWindowHandle) } catch {}
    try { $win.SetFocus() } catch {}
    try {
        [EditorSmokeMouse]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
    } catch {}
    $title = Find-ByAutomationId $win "AppTitleBar"
    if ($title -ne $null) {
        $r = $title.Current.BoundingRectangle
        [EditorSmokeMouse]::LeftClick([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
        Start-Sleep -Milliseconds 300
    }
}

function Get-PageMargin($win) {
    $pc = Find-ByAutomationId $win "PagesContainer"
    if ($pc -eq $null) { return $null }
    # DEBUG seam: HelpText carries "pages-margin-left=<n>".
    $help = $pc.Current.HelpText
    if ($help -match "pages-margin-left=([0-9.]+)") { return [double]$Matches[1] }
    return $null
}

function Get-JumpValue($win) {
    # Prefer the DEBUG HelpText probe: the WinUI TextBox UIA Value can lag a
    # programmatic Text rewrite while HelpText always reflects the page the
    # scroll landed on.
    $group = Find-ByAutomationId $win "Editor.PageJumpGroup"
    if ($group -ne $null) {
        $help = $group.Current.HelpText
        if ($help -match "current-page=(\d+)") { return $Matches[1] }
    }
    $jump = Find-ByAutomationId $win "Editor.PageJump"
    if ($jump -eq $null) { return $null }
    try {
        $vp = $jump.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $v = $vp.Current.Value
        if ($v -ne $null) { return $v }
    } catch { }
    try {
        $tp = $jump.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
        return $tp.DocumentRange.GetText(64)
    } catch { return $null }
}

# ── 1) Seed library: a real 3-page PDF with extractable text ────────────────

$dataRoot = Join-Path $env:TEMP ("opennotes_editorsmoke_" + [Guid]::NewGuid().ToString("N"))
$caelumDir = Join-Path $dataRoot "Caelum"
New-Item -ItemType Directory -Force -Path $caelumDir | Out-Null
$docsDir = Join-Path $dataRoot "docs"
New-Item -ItemType Directory -Force -Path $docsDir | Out-Null

$pdfParts = New-Object System.Collections.Generic.List[string]
$pdfParts.Add("%PDF-1.4`n")
$pdfParts.Add("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj`n")
$pdfParts.Add("2 0 obj<</Type/Pages/Kids[3 0 R 4 0 R 5 0 R]/Count 3>>endobj`n")
$pages = @("ALPHA page one", "BRAVO page two", "CHARLIE page three")
for ($i = 0; $i -lt 3; $i++) {
    $pageObj = $i + 3
    $contentObj = $i + 7
    $pdfParts.Add("$pageObj 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 6 0 R>>>>/Contents $contentObj 0 R>>endobj`n")
}
$pdfParts.Add("6 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj`n")
for ($i = 0; $i -lt 3; $i++) {
    $contentObj = $i + 7
    $stream = "BT /F1 24 Tf 72 700 Td ($($pages[$i])) Tj ET"
    $pdfParts.Add("$contentObj 0 obj<</Length $($stream.Length)>>stream`n$stream`nendstream`nendobj`n")
}
$pdfParts.Add("trailer<</Root 1 0 R>>`n")
$pdfPath = Join-Path $docsDir "smoke-editor.pdf"
[System.IO.File]::WriteAllBytes($pdfPath, [System.Text.Encoding]::ASCII.GetBytes(($pdfParts -join "")))

$now = (Get-Date).ToUniversalTime().ToString("o")
$entries = @(
    [ordered]@{ Id = "smokeeditorfile00000000000000001"; EntryType = "file"; ParentFolderId = ""; DisplayName = "";
                IsNotebook = $false; Path = $pdfPath; PageCount = 0; LastModifiedUtc = $now; LastOpenedUtc = $now; Color = "" }
)
# PS 5.1 serializes a single-element array as a bare object; force array shape.
("[" + ($entries | ConvertTo-Json -Depth 4) + "]") | Set-Content -Path (Join-Path $caelumDir "recent_files.json") -Encoding UTF8
Write-Output "seeded-library=$caelumDir pdf=$pdfPath"

# ── 2) Launch the app ───────────────────────────────────────────────────────

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
Start-Sleep -Milliseconds 1500

try {
    # ── 3) Open the file through its tile ──────────────────────────────────
    $tile = Wait-ForAutomationId $win "HomeTile_smoke-editor.pdf" 15
    Check ($tile -ne $null) "file-tile-rendered"
    if ($tile -ne $null) { Check (Invoke-TileButton $tile) "file-tile-invoked" }

    # Editor surface comes up; rendering is async so give the bitmap pass a beat.
    $pages = Wait-ForAutomationId $win "PagesContainer" 20
    Check ($pages -ne $null) "pages-container-present"
    Start-Sleep -Milliseconds 1500

    $page0 = Wait-ForAutomationId $win "PdfPageControl.0" 15
    Check ($page0 -ne $null) "page-control-rendered"

    $margin = Get-PageMargin $win
    Write-Output "sidebar-margin-expanded=$margin"
    Check ($margin -ne $null -and [math]::Abs($margin - 228) -lt 2) "sidebar-margin-228-expanded"

    # ── 4) Toolbar AutomationId sweep ──────────────────────────────────────
    $toolbarIds = @(
        "Editor.UndoButton", "Editor.RedoButton", "Editor.PenToolButton",
        "Editor.HighlighterToolButton", "HiddenInkToolButton",
        "Editor.StickyNoteToolButton", "Editor.EraserToolButton",
        "Editor.ShapeToolButton", "Editor.LaserToolButton",
        "Editor.RulerToolButton", "Editor.SelectToolButton",
        "Editor.TextToolButton", "Editor.SavePdfButton",
        "Editor.VersionHistoryButton", "Editor.PenOnlyButton",
        "Editor.ZoomOutButton", "Editor.ZoomLabel", "Editor.ZoomInButton",
        "Editor.RotatePageButton", "Editor.PageJump", "Editor.PageJumpGroup",
        "Editor.PreviousPageButton", "Editor.NextPageButton", "Editor.ToolbarOverflow"
    )
    foreach ($id in $toolbarIds) {
        Check ((Wait-ForAutomationId $win $id 6) -ne $null) "toolbar-id-$id"
    }

    # ── 5) Sidebar chrome + tab switching ──────────────────────────────────
    foreach ($id in @("DocumentSidebar", "Editor.Sidebar.Pages", "Editor.Sidebar.Outline",
                      "Editor.Sidebar.Bookmarks", "Editor.Sidebar.Collapse",
                      "Editor.Sidebar.Thumbnails")) {
        Check ((Wait-ForAutomationId $win $id 6) -ne $null) "sidebar-id-$id"
    }

    # Pages rail: realized thumbnail row carries the per-page AutomationId.
    Check ((Wait-ForAutomationId $win "Editor.Sidebar.Page.1" 10) -ne $null) "sidebar-page-1-row"

    # Outline tab -> tree surface becomes visible.
    $outlineBtn = Find-ByAutomationId $win "Editor.Sidebar.Outline"
    if ($outlineBtn -ne $null) { Invoke-Element $outlineBtn; Start-Sleep -Milliseconds 500 }
    Check ((Wait-ForAutomationId $win "Editor.Sidebar.Outline.Tree" 8) -ne $null) "outline-tree-visible"

    # Bookmarks tab -> list + toggle visible.
    $bookmarksBtn = Find-ByAutomationId $win "Editor.Sidebar.Bookmarks"
    if ($bookmarksBtn -ne $null) { Invoke-Element $bookmarksBtn; Start-Sleep -Milliseconds 500 }
    Check ((Wait-ForAutomationId $win "Editor.Sidebar.Bookmarks.List" 8) -ne $null) "bookmarks-list-visible"

    $bookmarkToggle = Wait-ForAutomationId $win "Editor.Sidebar.BookmarkToggle" 8
    Check ($bookmarkToggle -ne $null) "bookmark-toggle-visible"
    if ($bookmarkToggle -ne $null) {
        $toggle = $bookmarkToggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $toggle.Toggle()
        Start-Sleep -Milliseconds 500
        $bookmarkToggle = Find-ByAutomationId $win "Editor.Sidebar.BookmarkToggle"
        $toggle = $bookmarkToggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        Check ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) "bookmark-toggle-on"
        $toggle.Toggle()
        Start-Sleep -Milliseconds 500
        Check ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off) "bookmark-toggle-off"
    }

    # Back to Pages.
    $pagesBtn = Find-ByAutomationId $win "Editor.Sidebar.Pages"
    if ($pagesBtn -ne $null) { Invoke-Element $pagesBtn; Start-Sleep -Milliseconds 400 }
    Check ((Wait-ForAutomationId $win "Editor.Sidebar.Thumbnails" 8) -ne $null) "pages-tab-restored"

    # ── 6) Collapse/expand: 228 <-> 32 DIP contract ───────────────────────
    $collapseBtn = Wait-ForAutomationId $win "Editor.Sidebar.Collapse" 8
    Check ($collapseBtn -ne $null) "collapse-button-present"
    if ($collapseBtn -ne $null) {
        Invoke-Element $collapseBtn
        Start-Sleep -Milliseconds 500
        $margin = Get-PageMargin $win
        Write-Output "sidebar-margin-collapsed=$margin"
        Check ($margin -ne $null -and [math]::Abs($margin - 32) -lt 2) "sidebar-margin-32-collapsed"
        Invoke-Element (Find-ByAutomationId $win "Editor.Sidebar.Collapse")
        Start-Sleep -Milliseconds 500
        $margin = Get-PageMargin $win
        Check ($margin -ne $null -and [math]::Abs($margin - 228) -lt 2) "sidebar-margin-228-reexpanded"
    }

    # DEBUG narrow-layout simulation (window can't shrink below 560 DIP).
    $narrow = Find-ByAutomationId $win "Editor.DebugSidebarNarrow"
    if ($narrow -ne $null) {
        Invoke-Element $narrow
        Start-Sleep -Milliseconds 500
        $margin = Get-PageMargin $win
        Write-Output "sidebar-margin-narrow=$margin"
        Check ($margin -ne $null -and [math]::Abs($margin - 32) -lt 2) "narrow-layout-margin-32"
        Invoke-Element (Find-ByAutomationId $win "Editor.DebugSidebarNarrow")
        Start-Sleep -Milliseconds 500
        $margin = Get-PageMargin $win
        Check ($margin -ne $null -and [math]::Abs($margin - 228) -lt 2) "narrow-layout-restored-228"
    } else {
        Write-Output "skip narrow-layout (release build lacks the DEBUG seam)"
    }

    # ── 7) Page navigator ──────────────────────────────────────────────────
    $jumpValue = Get-JumpValue $win
    Write-Output "page-jump-initial=$jumpValue"
    Check ($jumpValue -eq "1") "page-jump-starts-at-1"

    $next = Find-ByAutomationId $win "Editor.NextPageButton"
    if ($next -ne $null) { Invoke-Element $next; Start-Sleep -Milliseconds 700 }
    $jumpValue = Get-JumpValue $win
    Check ($jumpValue -eq "2") "next-page-advances-to-2"

    $prev = Find-ByAutomationId $win "Editor.PreviousPageButton"
    if ($prev -ne $null) { Invoke-Element $prev; Start-Sleep -Milliseconds 700 }
    $jumpValue = Get-JumpValue $win
    Check ($jumpValue -eq "1") "prev-page-returns-to-1"

    # Editable jump: set the box to 3 and commit with Enter.
    $jumpBox = Find-ByAutomationId $win "Editor.PageJump"
    if ($jumpBox -ne $null) {
        try {
            Focus-AppWindow $win
            $jumpBox = Find-ByAutomationId $win "Editor.PageJump"
            $jumpBox.SetFocus()
            Start-Sleep -Milliseconds 300
            $vp = $jumpBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            $vp.SetValue("3")
            Start-Sleep -Milliseconds 300
            $commitSeam = Find-ByAutomationId $win "Editor.DebugCommitJump"
            if ($commitSeam -ne $null) {
                Invoke-Element $commitSeam
            } else {
                [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
            }
            # UIA ValuePattern can lag the deferred ChangeView application;
            # poll until the indicator reflects the landed page.
            $jumpValue = $null
            for ($i = 0; $i -lt 12 -and $jumpValue -ne "3"; $i++) {
                Start-Sleep -Milliseconds 300
                $jumpValue = Get-JumpValue $win
            }
            Write-Output "page-jump-after-commit=$jumpValue"
            Check ($jumpValue -eq "3") "page-jump-to-3"
        } catch {
            Check $false "page-jump-editable ($($_.Exception.Message))"
        }
    }

    # ── 8) Zoom ────────────────────────────────────────────────────────────
    $zoomIn = Find-ByAutomationId $win "Editor.ZoomInButton"
    if ($zoomIn -ne $null) { Invoke-Element $zoomIn; Start-Sleep -Milliseconds 600 }
    $zoomLabel = Find-ByAutomationId $win "Editor.ZoomLabel"
    Check ($zoomLabel -ne $null -and $zoomLabel.Current.Name -eq "110%") "zoom-in-label-110"
    $zoomOut = Find-ByAutomationId $win "Editor.ZoomOutButton"
    if ($zoomOut -ne $null) { Invoke-Element $zoomOut; Start-Sleep -Milliseconds 600 }

    # ── 9) Search panel ────────────────────────────────────────────────────
    $searchSeam = Find-ByAutomationId $win "Editor.DebugOpenSearch"
    if ($searchSeam -ne $null) {
        Invoke-Element $searchSeam
    } else {
        Focus-AppWindow $win
        [System.Windows.Forms.SendKeys]::SendWait("^f")
    }
    $searchPanel = Wait-ForAutomationId $win "PdfSearchPanel" 8
    Check ($searchPanel -ne $null -and -not $searchPanel.Current.IsOffscreen) "search-panel-opens"
    $searchBox = Wait-ForAutomationId $win "PdfSearchTextBox" 8
    if ($searchBox -ne $null) {
        try {
            $vp = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            $vp.SetValue("BRAVO")
            Start-Sleep -Milliseconds 1200
            $resultsList = Find-ByAutomationId $win "PdfSearchResultsListBox"
            $count = 0
            if ($resultsList -ne $null) {
                $itemCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::ListItem)
                $count = $resultsList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond).Count
            }
            Write-Output "search-result-count=$count"
            Check ($count -ge 1) "search-finds-bravo"
            $status = Find-ByAutomationId $win "PdfSearchStatus"
            Check ($status -ne $null -and $status.Current.Name -like "1*") "search-status-one-result"
        } catch {
            Check $false "search-usable ($($_.Exception.Message))"
        }
    }
    $searchClose = Find-ByAutomationId $win "PdfSearchCloseButton"
    if ($searchClose -ne $null) { Invoke-Element $searchClose; Start-Sleep -Milliseconds 400 }

    # ── 10) Page context menu (right-click on the first page) ──────────────
    $page0 = Find-ByAutomationId $win "PdfPageControl.0"
    if ($page0 -ne $null) {
        $ctxSeam = Find-ByAutomationId $win "Editor.DebugOpenContextMenu"
        if ($ctxSeam -ne $null) {
            Invoke-Element $ctxSeam
        } else {
            Focus-AppWindow $win
            $page0 = Find-ByAutomationId $win "PdfPageControl.0"
            $px, $py = Center-Point $page0
            Clear-ClickPoint $win $px $py
            [EditorSmokeMouse]::RightClick($px, $py)
        }
        $menu = Find-OpenMenu 8
        Check ($menu -ne $null) "page-context-menu-opened"
        if ($menu -ne $null) {
            $rotate = Find-ByAutomationId $menu "Editor.ContextMenu.RotateCurrentPage"
            Check ($rotate -ne $null) "context-menu-rotate-item"
            $png1x = Find-ByAutomationId $menu "Editor.ContextMenu.ExportCurrentPagePng1x"
            Check ($png1x -ne $null) "context-menu-png-item"
            [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
            Start-Sleep -Milliseconds 400
        }
    } else {
        Check $false "page-context-menu (page control missing)"
    }

    # ── 11) Close the editor tab -> Home returns, process alive ────────────
    $closeBtn = Find-ByAutomationId $win "TabCloseButton"
    if ($closeBtn -ne $null) {
        Invoke-Element $closeBtn
        Start-Sleep -Milliseconds 900
        Check ((Wait-ForAutomationId $win "HomeTile_Add" 8) -ne $null) "tab-close-returns-home"
    }
    Check (-not $proc.HasExited) "process-alive-after-close"
}
finally {
    try { $proc.Kill() } catch {}
    foreach ($h in $script:minimizedHwnds) {
        try { [EditorSmokeMouse]::Restore($h) } catch {}
    }
    try { Remove-Item -Recurse -Force $dataRoot -ErrorAction SilentlyContinue } catch {}
}

$failCount = @($script:results | Where-Object { $_ -like "FAIL*" }).Count
Write-Output "EDITOR-SMOKE-RESULTS=$($script:results.Count) fails=$failCount"
Write-Output "EDITOR-SMOKE-DONE"
exit ($(if ($failCount -gt 0) { 1 } else { 0 }))
