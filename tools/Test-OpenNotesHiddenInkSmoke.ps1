[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'OpenNotesEditorAutomationIds.ps1')

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $PSScriptRoot '..\bin\Debug\net8.0-windows\win-x64\OpenNotes.exe'
}
$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path

if (-not ('HiddenInkSmokeNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HiddenInkSmokeNative
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    public const uint MouseMove = 0x0001;
    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
}
'@
}

$temporaryEnvironmentPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'OpenNotesHiddenInkSmoke_' + [guid]::NewGuid().ToString('N'))
$pdfPath = Join-Path $temporaryEnvironmentPath 'hidden-ink-smoke.pdf'
$sidecarRoot = Join-Path $temporaryEnvironmentPath 'Caelum'
$process = $null

function New-DarkPdf([string]$path) {
    $content = "0 0 0 rg`n0 0 612 792 re`nf`n"
    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /ProcSet [/PDF] >> >>',
        "<< /Length $([System.Text.Encoding]::ASCII.GetByteCount($content)) >>`nstream`n$content`nendstream"
    )

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

function Get-ProcessWindows([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $windowCondition)
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Subtree, $condition))
}

function Find-MainWindow([int]$processId) {
    return Get-ProcessWindows $processId |
        Where-Object { $_.Current.Name -eq 'OpenNotes' } |
        Select-Object -First 1
}

function Find-DescendantByAutomationId($element, [string]$automationId) {
    if ($null -eq $element) { return $null }
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-Until([scriptblock]$condition, [int]$timeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $result = & $condition
        if ($null -ne $result) { return $result }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

function Ensure-ForegroundWindow([IntPtr]$hwnd, [string]$operation) {
    [void][HiddenInkSmokeNative]::ShowWindow($hwnd, 5)
    [void][HiddenInkSmokeNative]::SetForegroundWindow($hwnd)
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $foreground = [HiddenInkSmokeNative]::GetForegroundWindow()
        if ($foreground -eq $hwnd) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    $foregroundProcessId = [uint32]0
    if ($foreground -ne [IntPtr]::Zero) {
        [void][HiddenInkSmokeNative]::GetWindowThreadProcessId(
            $foreground,
            [ref]$foregroundProcessId)
    }
    throw "REAL_SCREEN_INPUT_UNAVAILABLE operation='$operation' targetHwnd=$hwnd foregroundHwnd=$foreground foregroundPid=$foregroundProcessId"
}

function Send-PointerClick([IntPtr]$hwnd, [int]$x, [int]$y, [string]$operation) {
    Ensure-ForegroundWindow $hwnd $operation
    if (-not [HiddenInkSmokeNative]::SetCursorPos($x, $y)) {
        throw "REAL_SCREEN_POINTER_UNAVAILABLE operation='$operation' point=$x,$y"
    }
    [HiddenInkSmokeNative]::mouse_event([HiddenInkSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [HiddenInkSmokeNative]::mouse_event([HiddenInkSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [HiddenInkSmokeNative]::mouse_event([HiddenInkSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    return 'screen-input'
}

function Send-PointerDrag(
    [IntPtr]$hwnd,
    [int]$startX,
    [int]$startY,
    [int]$endX,
    [int]$endY,
    [string]$operation) {
    Ensure-ForegroundWindow $hwnd $operation
    if (-not [HiddenInkSmokeNative]::SetCursorPos($startX, $startY)) {
        throw "REAL_SCREEN_POINTER_UNAVAILABLE operation='$operation' start=$startX,$startY"
    }
    [HiddenInkSmokeNative]::mouse_event([HiddenInkSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [HiddenInkSmokeNative]::mouse_event([HiddenInkSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    for ($step = 1; $step -le 28; $step++) {
        $progress = $step / 28.0
        $x = [int][Math]::Round($startX + (($endX - $startX) * $progress))
        $y = [int][Math]::Round($startY + (($endY - $startY) * $progress))
        if (-not [HiddenInkSmokeNative]::SetCursorPos($x, $y)) {
            throw "REAL_SCREEN_POINTER_UNAVAILABLE operation='$operation' step=$step point=$x,$y"
        }
        [HiddenInkSmokeNative]::mouse_event([HiddenInkSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 30
    }
    [HiddenInkSmokeNative]::mouse_event([HiddenInkSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    return 'screen-input'
}

function Assert-ScreenInput([string]$mode, [string]$operation) {
    if ($mode -ne 'screen-input') {
        throw "REAL_SCREEN_POINTER_REQUIRED operation='$operation' mode='$mode'"
    }
}

function Find-FilePdfTile($mainWindow) {
    $tilesGrid = Find-DescendantByAutomationId $mainWindow 'TilesGrid'
    if ($null -eq $tilesGrid) { return $null }
    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $buttons = @($tilesGrid.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $buttonCondition))
    $namedPdf = $buttons |
        Where-Object { $_.Current.Name -match '(?i)\.pdf' -and $_.Current.BoundingRectangle.Width -gt 200 } |
        Select-Object -First 1
    if ($null -ne $namedPdf) { return $namedPdf }

    return $buttons |
        Where-Object {
            $rect = $_.Current.BoundingRectangle
            $rect.Width -gt 200 -and $rect.Height -gt 200
        } |
        Select-Object -Skip 1 -First 1
}

function Get-ToggleState([int]$processId, [string]$automationId) {
    $element = Find-DescendantByAutomationId (Find-MainWindow $processId) $automationId
    if ($null -eq $element) { throw "Toggle control was not found: $automationId" }
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    return $pattern.Current.ToggleState.ToString()
}

function Wait-ToggleState([int]$processId, [string]$automationId, [string]$expected, [int]$timeoutSeconds = 5) {
    return Wait-Until {
        try {
            if ((Get-ToggleState $processId $automationId) -eq $expected) { return $expected }
        }
        catch { return $null }
        return $null
    } $timeoutSeconds
}

function Invoke-ToolbarPointerClick([int]$processId, [IntPtr]$windowHandle, [string]$automationId) {
    $element = Find-DescendantByAutomationId (Find-MainWindow $processId) $automationId
    if ($null -eq $element) { throw "Toolbar control was not found: $automationId" }
    $rect = $element.Current.BoundingRectangle
    $x = [int][Math]::Round($rect.Left + ($rect.Width * 0.5))
    $y = [int][Math]::Round($rect.Top + ($rect.Height * 0.5))
    $mode = Send-PointerClick $windowHandle $x $y "toolbar:$automationId"
    Write-Host "TOOL_POINTER_CLICK id='$automationId' x=$x y=$y mode='$mode' rect=$rect"
    Start-Sleep -Milliseconds 350
    return $mode
}

function Get-PageRect([int]$processId) {
    $page = Find-DescendantByAutomationId (Find-MainWindow $processId) (Get-EditorPageAutomationId 0)
    if ($null -eq $page -or $page.Current.IsOffscreen) { return $null }
    $rect = $page.Current.BoundingRectangle
    if ($rect.Width -lt 250 -or $rect.Height -lt 250) { return $null }
    return $rect
}

function Get-ScreenPixel([int]$x, [int]$y) {
    $bitmap = [System.Drawing.Bitmap]::new(1, 1)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($x, $y, 0, 0, [System.Drawing.Size]::new(1, 1))
        return $bitmap.GetPixel(0, 0)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Get-ColorDistance([System.Drawing.Color]$left, [System.Drawing.Color]$right) {
    return [Math]::Abs($left.R - $right.R) +
        [Math]::Abs($left.G - $right.G) +
        [Math]::Abs($left.B - $right.B)
}

function Get-HiddenInkMarkerCount([string]$path) {
    $ascii = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($path))
    return [System.Text.RegularExpressions.Regex]::Matches(
        $ascii, 'wna_hidden_').Count
}

function Save-IsolatedDocument([int]$processId, [IntPtr]$windowHandle, [string]$label) {
    $beforeHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
    $saveButton = Wait-Until {
        $candidate = Find-DescendantByAutomationId (Find-MainWindow $processId) $EditorAutomationIds.Save
        if ($null -ne $candidate -and $candidate.Current.IsEnabled) { return $candidate }
        return $null
    } 10
    if ($null -eq $saveButton) { throw "SavePdfButton was not enabled for '$label'." }
    $mode = Invoke-ToolbarPointerClick $processId $windowHandle $EditorAutomationIds.Save
    Assert-ScreenInput $mode "save:$label"
    $afterHash = Wait-Until {
        try {
            $candidateHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
            if ($candidateHash -ne $beforeHash) { return $candidateHash }
        }
        catch { return $null }
        return $null
    } 15
    if ($null -eq $afterHash) { throw "The isolated PDF did not change after '$label'." }
    Write-Output "PDF_SAVE_COMPLETED label='$label' hashChanged=True before=$beforeHash after=$afterHash"
}

function Start-IsolatedOpenNotes {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedExecutablePath
    $startInfo.WorkingDirectory = Split-Path -Parent $resolvedExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables['LOCALAPPDATA'] = $temporaryEnvironmentPath
    $startInfo.EnvironmentVariables['APPDATA'] = $temporaryEnvironmentPath
    $startInfo.EnvironmentVariables['OPENNOTES_DATA_ROOT'] = $temporaryEnvironmentPath
    $startInfo.EnvironmentVariables['SystemRoot'] = $env:SystemRoot
    [void]$startInfo.EnvironmentVariables.Remove('WINDIR')
    return [System.Diagnostics.Process]::Start($startInfo)
}

function Close-IsolatedOpenNotes([System.Diagnostics.Process]$child) {
    if ($null -eq $child) { return }
    try {
        if (-not $child.HasExited) {
            [void]$child.CloseMainWindow()
            if (-not $child.WaitForExit(5000)) {
                $child.Kill()
                [void]$child.WaitForExit()
            }
        }
    }
    catch {
        Write-Warning "Failed to close isolated OpenNotes process cleanly: $($_.Exception.Message)"
    }
    $child.Dispose()
}

try {
    if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        throw 'SystemRoot must be available for the isolated WPF smoke test.'
    }

    New-Item -ItemType Directory -Path $temporaryEnvironmentPath -Force | Out-Null
    New-DarkPdf $pdfPath
    $nowUtc = [DateTime]::UtcNow
    $recentEntry = [ordered]@{
        Id = ([guid]::NewGuid().ToString('N'))
        EntryType = 'file'
        ParentFolderId = ''
        DisplayName = [System.IO.Path]::GetFileName($pdfPath)
        IsNotebook = $false
        Path = $pdfPath
        PageCount = 1
        LastModifiedUtc = [System.IO.File]::GetLastWriteTimeUtc($pdfPath).ToString('o')
        LastOpenedUtc = $nowUtc.ToString('o')
    }
    New-Item -ItemType Directory -Path $sidecarRoot -Force | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [System.IO.File]::WriteAllText(
        (Join-Path $sidecarRoot 'settings.json'),
        '{"WholeStrokeEraser":true,"PenOnlyMode":false}',
        $utf8)
    [System.IO.File]::WriteAllText(
        (Join-Path $sidecarRoot 'recent_files.json'),
        '[' + ($recentEntry | ConvertTo-Json -Depth 4 -Compress) + ']',
        $utf8)

    $process = Start-IsolatedOpenNotes
    $mainWindow = Wait-Until {
        if ($process.HasExited) { throw "OpenNotes exited during startup with code $($process.ExitCode)." }
        Find-MainWindow $process.Id
    } 20
    if ($null -eq $mainWindow) { throw 'OpenNotes main window was not found.' }
    $hwnd = [IntPtr]$mainWindow.Current.NativeWindowHandle
    Ensure-ForegroundWindow $hwnd 'editor-startup'

    $fileTile = Wait-Until { Find-FilePdfTile (Find-MainWindow $process.Id) } 15
    if ($null -eq $fileTile) { throw 'The pre-seeded Hidden Ink PDF tile was not found.' }
    $tileRect = $fileTile.Current.BoundingRectangle
    $tileMode = Send-PointerClick $hwnd `
        ([int][Math]::Round($tileRect.Left + ($tileRect.Width * 0.5))) `
        ([int][Math]::Round($tileRect.Top + ($tileRect.Height * 0.5))) `
        'open-library-tile'
    Assert-ScreenInput $tileMode 'open-library-tile'

    $hiddenTool = Wait-Until {
        Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.HiddenInk
    } 60
    if ($null -eq $hiddenTool) { throw 'Hidden Ink tool was not found after opening the PDF.' }
    $pdfViewer = Wait-Until {
        Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.PdfScrollViewer
    } 20
    if ($null -eq $pdfViewer) { throw 'PdfScrollViewer was not found after opening the PDF.' }
    $pageRect = Wait-Until { Get-PageRect $process.Id } 20
    if ($null -eq $pageRect) { throw 'PdfPageControl.0 did not expose a usable visible page surface.' }
    Write-Output "EDITOR_SURFACE_READY pageRect=$pageRect"

    $hiddenMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.HiddenInk
    Assert-ScreenInput $hiddenMode 'activate-hidden-ink'
    if ($null -eq (Wait-ToggleState $process.Id $EditorAutomationIds.HiddenInk 'On' 5)) {
        throw "Hidden Ink tool did not become active state='$((Get-ToggleState $process.Id $EditorAutomationIds.HiddenInk))'."
    }

    $startX = [int][Math]::Round($pageRect.Left + ($pageRect.Width * 0.34))
    $endX = [int][Math]::Round($pageRect.Left + ($pageRect.Width * 0.66))
    $lineY = [int][Math]::Round($pageRect.Top + ($pageRect.Height * 0.56))
    $midX = [int][Math]::Round(($startX + $endX) / 2)
    $basePixel = Get-ScreenPixel $midX $lineY
    if (($basePixel.R + $basePixel.G + $basePixel.B) -gt 240) {
        throw "Hidden Ink base pixel was not on the dark PDF page at $midX,$lineY rgb=$($basePixel.R),$($basePixel.G),$($basePixel.B)"
    }

    $drawMode = Send-PointerDrag $hwnd $startX $lineY $endX $lineY 'hidden-ink-draw'
    Assert-ScreenInput $drawMode 'hidden-ink-draw'
    Start-Sleep -Milliseconds 600
    $maskedPixel = Get-ScreenPixel $midX $lineY
    $maskDistance = Get-ColorDistance $maskedPixel $basePixel
    if ($maskDistance -lt 180) {
        throw "Hidden Ink mask was not visible at $midX,$lineY rgb=$($maskedPixel.R),$($maskedPixel.G),$($maskedPixel.B)"
    }
    Write-Output "HIDDEN_INK_DRAW_COMPLETED mode='$drawMode' baseRgb=$($basePixel.R),$($basePixel.G),$($basePixel.B) maskedRgb=$($maskedPixel.R),$($maskedPixel.G),$($maskedPixel.B) maskDistance=$maskDistance"

    $markerBeforeSave = Get-HiddenInkMarkerCount $pdfPath
    Save-IsolatedDocument $process.Id $hwnd 'hidden-ink-draw'
    $markerAfterSave = Get-HiddenInkMarkerCount $pdfPath
    if ($markerAfterSave -le $markerBeforeSave) {
        throw "Hidden Ink did not persist its wna_hidden_ marker before=$markerBeforeSave after=$markerAfterSave"
    }

    $revealMode = Send-PointerClick $hwnd $midX $lineY 'hidden-ink-reveal'
    Assert-ScreenInput $revealMode 'hidden-ink-reveal'
    Start-Sleep -Milliseconds 700
    $revealedPixel = Get-ScreenPixel $midX $lineY
    $revealDistance = Get-ColorDistance $revealedPixel $basePixel
    if ($revealDistance -ge [Math]::Max(60, $maskDistance * 0.45)) {
        throw "Hidden Ink reveal did not approach the base page pixel baseRgb=$($basePixel.R),$($basePixel.G),$($basePixel.B) maskedRgb=$($maskedPixel.R),$($maskedPixel.G),$($maskedPixel.B) revealedRgb=$($revealedPixel.R),$($revealedPixel.G),$($revealedPixel.B)"
    }
    Start-Sleep -Milliseconds 3400
    $restoredPixel = Get-ScreenPixel $midX $lineY
    $restoreDistance = Get-ColorDistance $restoredPixel $maskedPixel
    if ($restoreDistance -ge [Math]::Max(80, $maskDistance * 0.55)) {
        throw "Hidden Ink reveal timer did not restore the mask restoredRgb=$($restoredPixel.R),$($restoredPixel.G),$($restoredPixel.B) maskRgb=$($maskedPixel.R),$($maskedPixel.G),$($maskedPixel.B)"
    }
    Write-Output "HIDDEN_INK_REVEAL_COMPLETED mode='$revealMode' revealDistance=$revealDistance restoreDistance=$restoreDistance"

    $eraserMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.Eraser
    Assert-ScreenInput $eraserMode 'activate-eraser'
    if ($null -eq (Wait-ToggleState $process.Id $EditorAutomationIds.Eraser 'On' 5)) {
        throw "Eraser tool did not become active state='$((Get-ToggleState $process.Id $EditorAutomationIds.Eraser))'."
    }
    # Close the eraser size popup with a harmless page click, then make a real
    # eraser gesture through the center of the mask.
    $dismissMode = Send-PointerClick $hwnd $pageRect.Left $pageRect.Top 'dismiss-eraser-popup'
    Assert-ScreenInput $dismissMode 'dismiss-eraser-popup'
    Start-Sleep -Milliseconds 250
    $eraseMode = Send-PointerDrag $hwnd ($midX - 25) $lineY ($midX + 25) $lineY 'hidden-ink-erase'
    Assert-ScreenInput $eraseMode 'hidden-ink-erase'
    Start-Sleep -Milliseconds 600
    $erasedPixel = Get-ScreenPixel $midX $lineY
    $eraseDistance = Get-ColorDistance $erasedPixel $basePixel
    if ($eraseDistance -ge [Math]::Max(60, $maskDistance * 0.45)) {
        throw "Hidden Ink eraser did not remove the mask at $midX,$lineY baseRgb=$($basePixel.R),$($basePixel.G),$($basePixel.B) erasedRgb=$($erasedPixel.R),$($erasedPixel.G),$($erasedPixel.B)"
    }
    Save-IsolatedDocument $process.Id $hwnd 'hidden-ink-erase'
    $markerAfterErase = Get-HiddenInkMarkerCount $pdfPath
    if ($markerAfterErase -ne $markerBeforeSave) {
        throw "Hidden Ink eraser did not remove the saved marker expected=$markerBeforeSave actual=$markerAfterErase"
    }
    Write-Output "HIDDEN_INK_ERASE_COMPLETED mode='$eraseMode' eraseDistance=$eraseDistance markerAfterErase=$markerAfterErase"

    $undoButton = Wait-Until {
        $candidate = Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.Undo
        if ($null -ne $candidate -and $candidate.Current.IsEnabled) { return $candidate }
        return $null
    } 10
    if ($null -eq $undoButton) { throw 'UndoButton was not enabled after Hidden Ink erase.' }
    $undoMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.Undo
    Assert-ScreenInput $undoMode 'hidden-ink-erase-undo'
    Start-Sleep -Milliseconds 600
    $restoredByUndoPixel = Get-ScreenPixel $midX $lineY
    if ((Get-ColorDistance $restoredByUndoPixel $basePixel) -lt 180) {
        throw 'Hidden Ink undo did not restore a visible opaque mask.'
    }
    Save-IsolatedDocument $process.Id $hwnd 'hidden-ink-erase-undo'
    $markerAfterUndo = Get-HiddenInkMarkerCount $pdfPath
    if ($markerAfterUndo -le $markerBeforeSave) {
        throw "Hidden Ink undo did not restore the saved marker expected>$markerBeforeSave actual=$markerAfterUndo"
    }

    Close-IsolatedOpenNotes $process
    $process = $null
    Start-Sleep -Milliseconds 700

    $process = Start-IsolatedOpenNotes
    $mainWindow = Wait-Until {
        if ($process.HasExited) { throw "OpenNotes exited during reopen with code $($process.ExitCode)." }
        Find-MainWindow $process.Id
    } 20
    if ($null -eq $mainWindow) { throw 'OpenNotes main window was not found after Hidden Ink restart.' }
    $hwnd = [IntPtr]$mainWindow.Current.NativeWindowHandle
    Ensure-ForegroundWindow $hwnd 'reopen-startup'
    $fileTile = Wait-Until { Find-FilePdfTile (Find-MainWindow $process.Id) } 15
    if ($null -eq $fileTile) { throw 'The Hidden Ink PDF tile was not found after restart.' }
    $tileRect = $fileTile.Current.BoundingRectangle
    $tileMode = Send-PointerClick $hwnd `
        ([int][Math]::Round($tileRect.Left + ($tileRect.Width * 0.5))) `
        ([int][Math]::Round($tileRect.Top + ($tileRect.Height * 0.5))) `
        'reopen-library-tile'
    Assert-ScreenInput $tileMode 'reopen-library-tile'
    $pageRect = Wait-Until { Get-PageRect $process.Id } 60
    if ($null -eq $pageRect) { throw 'PdfPageControl.0 did not expose a page after Hidden Ink restart.' }
    Start-Sleep -Milliseconds 800
    $reopenedPixel = Get-ScreenPixel $midX $lineY
    $reopenMaskDistance = Get-ColorDistance $reopenedPixel $basePixel
    if ($reopenMaskDistance -lt 180) {
        throw "Hidden Ink mask was not visible after restart/reopen baseRgb=$($basePixel.R),$($basePixel.G),$($basePixel.B) reopenedRgb=$($reopenedPixel.R),$($reopenedPixel.G),$($reopenedPixel.B)"
    }
    Write-Output "HIDDEN_INK_REOPEN_COMPLETED marker=$markerAfterUndo reopenMaskDistance=$reopenMaskDistance"

    $revealAgainMode = Send-PointerClick $hwnd $midX $lineY 'hidden-ink-reveal-after-reopen'
    Assert-ScreenInput $revealAgainMode 'hidden-ink-reveal-after-reopen'
    Start-Sleep -Milliseconds 700
    $revealedAgainPixel = Get-ScreenPixel $midX $lineY
    if ((Get-ColorDistance $revealedAgainPixel $basePixel) -ge [Math]::Max(60, $reopenMaskDistance * 0.45)) {
        throw 'Hidden Ink did not reveal after restart/reopen.'
    }
    Start-Sleep -Milliseconds 3400
    $restoredAgainPixel = Get-ScreenPixel $midX $lineY
    if ((Get-ColorDistance $restoredAgainPixel $reopenedPixel) -ge [Math]::Max(80, $reopenMaskDistance * 0.55)) {
        throw 'Hidden Ink timer did not restore the mask after restart/reopen.'
    }
    Write-Output 'HIDDEN_INK_TIMER_AFTER_REOPEN=PASS'
    Write-Output 'HIDDEN_INK_SMOKE_RESULT=PASS'
}
catch {
    Write-Output 'HIDDEN_INK_SMOKE_RESULT=FAIL'
    throw
}
finally {
    Close-IsolatedOpenNotes $process
    if ($KeepArtifacts) {
        Write-Output "HIDDEN_INK_ARTIFACTS=$temporaryEnvironmentPath"
        Write-Output 'ISOLATED_ENV_CLEANED=False reason=keep-artifacts'
    }
    elseif (Test-Path -LiteralPath $temporaryEnvironmentPath) {
        try {
            Remove-Item -LiteralPath $temporaryEnvironmentPath -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Failed to remove the exact Hidden Ink smoke temporary directory: $($_.Exception.Message)"
        }
    }
    if (-not $KeepArtifacts) {
        Write-Output "ISOLATED_ENV_CLEANED=$(-not (Test-Path -LiteralPath $temporaryEnvironmentPath))"
    }
}
