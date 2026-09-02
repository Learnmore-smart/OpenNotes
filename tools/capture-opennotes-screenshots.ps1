# Automated screenshot capture for OpenNotes website evidence showcase
[CmdletBinding()]
param(
    [string]$ExecutablePath = 'bin\Debug\net8.0-windows\win-x64\OpenNotes.exe'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'OpenNotesEditorAutomationIds.ps1')

$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$rawDir = Join-Path $PSScriptRoot '..\artifacts\raw-screenshots'
New-Item -ItemType Directory -Force -Path $rawDir | Out-Null

if (-not ('PointerSmokeNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class PointerSmokeNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    public const uint MouseMove = 0x0001;
    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
}
'@
}

function Capture-Rect([System.Windows.Rect]$rect, [string]$filePath) {
    $x = [int][Math]::Max(0, [Math]::Round($rect.X))
    $y = [int][Math]::Max(0, [Math]::Round($rect.Y))
    $w = [int][Math]::Round($rect.Width)
    $h = [int][Math]::Round($rect.Height)

    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)), [System.Drawing.CopyPixelOperation]::SourceCopy)
    $g.Dispose()
    $bmp.Save($filePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "Saved screenshot: $filePath ($w x $h)"
}

function New-SamplePdf([string]$path, [int]$pageCount = 3) {
    $objects = New-Object System.Collections.Generic.List[string]
    $kids = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $pageCount; $i++) {
        $pageObjNum = 3 + ($i * 2)
        $kids.Add("$pageObjNum 0 R")
    }

    $catalog = '<< /Type /Catalog /Pages 2 0 R >>'
    $pages = "<< /Type /Pages /Kids [$($kids -join ' ')] /Count $pageCount >>"
    $objects.Add($catalog)
    $objects.Add($pages)

    for ($i = 0; $i -lt $pageCount; $i++) {
        $contentObjNum = 3 + ($i * 2) + 1
        $page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents $contentObjNum 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >> /F2 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>"
        
        $streamContent = @"
BT
/F1 20 Tf
50 730 Td
(OPENNOTES RESEARCH & SYSTEM ARCHITECTURE) Tj
/F1 13 Tf
0 -34 Td
(1. Core Pipeline & Performance Evaluation) Tj
/F2 11 Tf
0 -22 Td
(OpenNotes provides high-performance document rendering with native Windows Ink integration.) Tj
0 -16 Td
(The annotation pipeline processes stylus and touch inputs with sub-frame responsiveness.) Tj
0 -16 Td
(Vector strokes are rasterized directly to the presentation canvas with crisp sub-pixel fidelity.) Tj
0 -26 Td
(2. Memory Footprint and Page Virtualization) Tj
/F2 11 Tf
0 -18 Td
(Document pages are virtualized in a two-tier cache hierarchy for instant scrolling.) Tj
0 -16 Td
(Background thumbnail generation runs concurrently without impacting interactive pen strokes.) Tj
0 -26 Td
(3. Sub-pixel Inking Coordinates & Layer Management) Tj
0 -18 Td
(Physical device points are mapped into PDF coordinate space via device-independent matrices.) Tj
0 -16 Td
(All annotations maintain exact scale and position across arbitrary zoom levels and orientations.) Tj
ET
0.88 0.88 0.92 rg
50 360 512 1.5 re f
0.15 0.45 0.85 rg
50 315 200 32 re f
BT
/F1 11 Tf
1 1 1 rg
60 326 Td
(SYSTEM ARCHITECTURE OK) Tj
ET
"@
        $stream = "<< /Length $($streamContent.Length) >>`nstream`n$streamContent`nendstream"
        $objects.Add($page)
        $objects.Add($stream)
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append("%PDF-1.4`n")
    $offsets = [System.Collections.Generic.List[int]]::new()
    for ($index = 0; $index -lt $objects.Count; $index++) {
        $offsets.Add([System.Text.Encoding]::ASCII.GetByteCount($builder.ToString()))
        [void]$builder.Append("$($index + 1) 0 obj`n$($objects[$index])`nendobj`n")
    }

    $xrefOffset = [System.Text.Encoding]::ASCII.GetByteCount($builder.ToString())
    [void]$builder.Append("xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n")
    foreach ($offset in $offsets) {
        [void]$builder.AppendFormat("{0:0000000000} 00000 n `n", $offset)
    }
    [void]$builder.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xrefOffset`n%%EOF`n")
    [System.IO.File]::WriteAllBytes($path, [System.Text.Encoding]::ASCII.GetBytes($builder.ToString()))
}

function Wait-Until([scriptblock]$condition, [int]$timeoutSeconds = 15) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $result = & $condition
        if ($null -ne $result) { return $result }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

function Find-MainWindow([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $windowCondition)
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Subtree, $condition) |
        Where-Object { $_.Current.Name -eq 'OpenNotes' }) | Select-Object -First 1
}

function Find-DescendantByAutomationId($element, [string]$automationId) {
    if ($null -eq $element) { return $null }
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Element($element) {
    if ($null -eq $element) { return }
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Mouse-Drag([int]$startX, [int]$startY, [int]$endX, [int]$endY, [int]$steps = 15) {
    [PointerSmokeNative]::SetCursorPos($startX, $startY)
    Start-Sleep -Milliseconds 50
    [PointerSmokeNative]::mouse_event([PointerSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    for ($i = 1; $i -le $steps; $i++) {
        $p = $i / [double]$steps
        $cx = [int][Math]::Round($startX + ($endX - $startX) * $p)
        $cy = [int][Math]::Round($startY + ($endY - $startY) * $p)
        [PointerSmokeNative]::SetCursorPos($cx, $cy)
        Start-Sleep -Milliseconds 20
    }
    [PointerSmokeNative]::mouse_event([PointerSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
}

function Mouse-Click([int]$x, [int]$y) {
    [PointerSmokeNative]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 60
    [PointerSmokeNative]::mouse_event([PointerSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [PointerSmokeNative]::mouse_event([PointerSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
}

Write-Output "--- Starting OpenNotes Light Session ---"
$tempEnv = Join-Path ([System.IO.Path]::GetTempPath()) ('OpenNotesArt_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempEnv | Out-Null
$samplePdf = Join-Path $tempEnv 'OpenNotes_Research.pdf'
New-SamplePdf $samplePdf 3

$libFolder = Join-Path $tempEnv 'Caelum\Library'
New-Item -ItemType Directory -Force -Path $libFolder | Out-Null
Copy-Item $samplePdf (Join-Path $libFolder 'OpenNotes_Research.pdf') -Force

$settingsFolder = Join-Path $tempEnv 'Caelum'
New-Item -ItemType Directory -Force -Path $settingsFolder | Out-Null
$settingsJson = @{
    Language = 0 # English
    Theme = "Light"
    Backdrop = "Neutral"
} | ConvertTo-Json
Set-Content -Path (Join-Path $settingsFolder 'settings.json') -Value $settingsJson -Encoding UTF8

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $resolvedExecutablePath
$psi.WorkingDirectory = Split-Path -Parent $resolvedExecutablePath
$psi.UseShellExecute = $false
$psi.EnvironmentVariables['LOCALAPPDATA'] = $tempEnv
$psi.EnvironmentVariables['APPDATA'] = $tempEnv
$psi.EnvironmentVariables['OPENNOTES_DATA_ROOT'] = $tempEnv
$psi.EnvironmentVariables['SystemRoot'] = $env:SystemRoot
$proc = [System.Diagnostics.Process]::Start($psi)

$win = Wait-Until { Find-MainWindow $proc.Id } 20
if ($null -eq $win) { throw "Could not find OpenNotes main window" }

$hwnd = [IntPtr]$win.Current.NativeWindowHandle
[PointerSmokeNative]::ShowWindow($hwnd, 9) # SW_RESTORE
[PointerSmokeNative]::MoveWindow($hwnd, 40, 40, 1600, 1000, $true)
[PointerSmokeNative]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 1000

# Open document tile
$tile = Wait-Until {
    $w = Find-MainWindow $proc.Id
    $all = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
    $all | Where-Object { $_.Current.Name -match 'OpenNotes_Research' } | Select-Object -First 1
} 15

if ($null -ne $tile) {
    Invoke-Element $tile
    Start-Sleep -Seconds 2
}

# Wait for Editor toolbar
$penTool = Wait-Until { Find-DescendantByAutomationId (Find-MainWindow $proc.Id) $EditorAutomationIds.Pen } 20
$viewer = Find-DescendantByAutomationId (Find-MainWindow $proc.Id) $EditorAutomationIds.PdfScrollViewer
$vRect = $viewer.Current.BoundingRectangle

# Activate Pen and draw
Invoke-Element $penTool
Start-Sleep -Milliseconds 300

$midX = [int]($vRect.Left + $vRect.Width * 0.46)
$midY = [int]($vRect.Top + $vRect.Height * 0.38)

# Title underline
Mouse-Drag ($midX - 180) ($midY - 80) ($midX + 220) ($midY - 80) 20
# Margin notes
Mouse-Drag ($midX - 230) ($midY - 20) ($midX - 220) ($midY - 5) 8
Mouse-Drag ($midX - 220) ($midY - 5) ($midX - 200) ($midY - 30) 12

# Highlighter
$hiTool = Find-DescendantByAutomationId (Find-MainWindow $proc.Id) $EditorAutomationIds.Highlighter
if ($null -ne $hiTool) {
    Invoke-Element $hiTool
    Start-Sleep -Milliseconds 300
    Mouse-Drag ($midX - 170) ($midY + 10) ($midX + 180) ($midY + 10) 20
}

# Reactivate Pen
Invoke-Element $penTool
Start-Sleep -Milliseconds 300

# 1. Hero Editor capture
$winRect = (Find-MainWindow $proc.Id).Current.BoundingRectangle
Capture-Rect $winRect (Join-Path $rawDir 'hero_raw.png')

# 2. Ink detail closeup
$inkArea = [System.Windows.Rect]::new($winRect.Left + 160, $winRect.Top + 35, 1150, 780)
Capture-Rect $inkArea (Join-Path $rawDir 'ink_raw.png')

# 3. Create text box
$textTool = Find-DescendantByAutomationId (Find-MainWindow $proc.Id) $EditorAutomationIds.Text
Invoke-Element $textTool
Start-Sleep -Milliseconds 300

$tbClickX = [int]($vRect.Left + $vRect.Width * 0.52)
$tbClickY = [int]($vRect.Top + $vRect.Height * 0.58)
Mouse-Click $tbClickX $tbClickY
Start-Sleep -Milliseconds 400

[System.Windows.Forms.SendKeys]::SendWait("Verified sub-pixel vector ink pipeline.{ENTER}Real-time stylus tracking enabled.")
Start-Sleep -Milliseconds 500

# Textbox closeup
$tbArea = [System.Windows.Rect]::new($tbClickX - 240, $tbClickY - 170, 850, 560)
Capture-Rect $tbArea (Join-Path $rawDir 'textbox_raw.png')

try { $proc.Kill() } catch {}
Start-Sleep -Milliseconds 500

Write-Output "--- Starting OpenNotes Dark Session ---"
$tempEnvDark = Join-Path ([System.IO.Path]::GetTempPath()) ('OpenNotesDark_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempEnvDark | Out-Null
$samplePdfDark = Join-Path $tempEnvDark 'OpenNotes_Research.pdf'
New-SamplePdf $samplePdfDark 3

$libFolderDark = Join-Path $tempEnvDark 'Caelum\Library'
New-Item -ItemType Directory -Force -Path $libFolderDark | Out-Null
Copy-Item $samplePdfDark (Join-Path $libFolderDark 'OpenNotes_Research.pdf') -Force

$settingsFolderDark = Join-Path $tempEnvDark 'Caelum'
New-Item -ItemType Directory -Force -Path $settingsFolderDark | Out-Null
$settingsJsonDark = @{
    Language = 0 # English
    Theme = "Dark"
    Backdrop = "Slate"
} | ConvertTo-Json
Set-Content -Path (Join-Path $settingsFolderDark 'settings.json') -Value $settingsJsonDark -Encoding UTF8

$psiDark = [System.Diagnostics.ProcessStartInfo]::new()
$psiDark.FileName = $resolvedExecutablePath
$psiDark.WorkingDirectory = Split-Path -Parent $resolvedExecutablePath
$psiDark.UseShellExecute = $false
$psiDark.EnvironmentVariables['LOCALAPPDATA'] = $tempEnvDark
$psiDark.EnvironmentVariables['APPDATA'] = $tempEnvDark
$psiDark.EnvironmentVariables['OPENNOTES_DATA_ROOT'] = $tempEnvDark
$psiDark.EnvironmentVariables['SystemRoot'] = $env:SystemRoot
$procDark = [System.Diagnostics.Process]::Start($psiDark)

$winDark = Wait-Until { Find-MainWindow $procDark.Id } 20
$hwndDark = [IntPtr]$winDark.Current.NativeWindowHandle
[PointerSmokeNative]::ShowWindow($hwndDark, 9)
[PointerSmokeNative]::MoveWindow($hwndDark, 40, 40, 1600, 1000, $true)
[PointerSmokeNative]::SetForegroundWindow($hwndDark)
Start-Sleep -Milliseconds 1000

$tileDark = Wait-Until {
    $w = Find-MainWindow $procDark.Id
    $all = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
    $all | Where-Object { $_.Current.Name -match 'OpenNotes_Research' } | Select-Object -First 1
} 15

if ($null -ne $tileDark) {
    Invoke-Element $tileDark
    Start-Sleep -Seconds 2
}

$penToolDark = Wait-Until { Find-DescendantByAutomationId (Find-MainWindow $procDark.Id) $EditorAutomationIds.Pen } 20
$viewerDark = Find-DescendantByAutomationId (Find-MainWindow $procDark.Id) $EditorAutomationIds.PdfScrollViewer
$vRectDark = $viewerDark.Current.BoundingRectangle

Invoke-Element $penToolDark
Start-Sleep -Milliseconds 300

$midXDark = [int]($vRectDark.Left + $vRectDark.Width * 0.46)
$midYDark = [int]($vRectDark.Top + $vRectDark.Height * 0.38)
Mouse-Drag ($midXDark - 180) ($midYDark - 80) ($midXDark + 220) ($midYDark - 80) 20
Mouse-Drag ($midXDark - 230) ($midYDark - 20) ($midXDark - 220) ($midYDark - 5) 8
Mouse-Drag ($midXDark - 220) ($midYDark - 5) ($midXDark - 200) ($midYDark - 30) 12

$winRectDark = (Find-MainWindow $procDark.Id).Current.BoundingRectangle
Capture-Rect $winRectDark (Join-Path $rawDir 'dark_theme_raw.png')

try { $procDark.Kill() } catch {}
Write-Output "All screenshots captured successfully!"
