[CmdletBinding()]
param(
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $PSScriptRoot '..\bin\Debug\net8.0-windows\win-x64\OpenNotes.exe'
}
$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path

if (-not ('CrossPageKeyboardSmokeNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class CrossPageKeyboardSmokeNative
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
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    public const uint MouseMove = 0x0001;
    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
    public const uint KeyUp = 0x0002;
}
'@
}

$temporaryEnvironmentPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'OpenNotesCrossPageKeyboardSmoke_' + [guid]::NewGuid().ToString('N'))
$pdfPath = Join-Path $temporaryEnvironmentPath 'cross-page-keyboard-smoke.pdf'
$sidecarRoot = Join-Path $temporaryEnvironmentPath 'Caelum'
$process = $null

function New-TwoPagePdf([string]$path) {
    $pageOneContent = "0.96 0.96 0.96 rg`n0 0 612 792 re`nf`n"
    $pageTwoContent = "0.88 0.93 0.98 rg`n0 0 612 792 re`nf`n"
    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /ProcSet [/PDF] >> >>',
        "<< /Length $([System.Text.Encoding]::ASCII.GetByteCount($pageOneContent)) >>`nstream`n$pageOneContent`nendstream",
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 6 0 R /Resources << /ProcSet [/PDF] >> >>',
        "<< /Length $([System.Text.Encoding]::ASCII.GetByteCount($pageTwoContent)) >>`nstream`n$pageTwoContent`nendstream"
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
    [void][CrossPageKeyboardSmokeNative]::ShowWindow($hwnd, 5)
    [void][CrossPageKeyboardSmokeNative]::SetForegroundWindow($hwnd)
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $foreground = [CrossPageKeyboardSmokeNative]::GetForegroundWindow()
        if ($foreground -eq $hwnd) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    $foregroundProcessId = [uint32]0
    if ($foreground -ne [IntPtr]::Zero) {
        [void][CrossPageKeyboardSmokeNative]::GetWindowThreadProcessId(
            $foreground,
            [ref]$foregroundProcessId)
    }
    throw "REAL_SCREEN_INPUT_UNAVAILABLE operation='$operation' targetHwnd=$hwnd foregroundHwnd=$foreground foregroundPid=$foregroundProcessId"
}

function Send-PointerClick([IntPtr]$hwnd, [int]$x, [int]$y, [string]$operation) {
    Ensure-ForegroundWindow $hwnd $operation
    if (-not [CrossPageKeyboardSmokeNative]::SetCursorPos($x, $y)) {
        throw "REAL_SCREEN_POINTER_UNAVAILABLE operation='$operation' point=$x,$y"
    }
    [CrossPageKeyboardSmokeNative]::mouse_event([CrossPageKeyboardSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [CrossPageKeyboardSmokeNative]::mouse_event([CrossPageKeyboardSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [CrossPageKeyboardSmokeNative]::mouse_event([CrossPageKeyboardSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
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
    if (-not [CrossPageKeyboardSmokeNative]::SetCursorPos($startX, $startY)) {
        throw "REAL_SCREEN_POINTER_UNAVAILABLE operation='$operation' start=$startX,$startY"
    }
    [CrossPageKeyboardSmokeNative]::mouse_event([CrossPageKeyboardSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [CrossPageKeyboardSmokeNative]::mouse_event([CrossPageKeyboardSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    for ($step = 1; $step -le 36; $step++) {
        $progress = $step / 36.0
        $x = [int][Math]::Round($startX + (($endX - $startX) * $progress))
        $y = [int][Math]::Round($startY + (($endY - $startY) * $progress))
        if (-not [CrossPageKeyboardSmokeNative]::SetCursorPos($x, $y)) {
            throw "REAL_SCREEN_POINTER_UNAVAILABLE operation='$operation' step=$step point=$x,$y"
        }
        [CrossPageKeyboardSmokeNative]::mouse_event([CrossPageKeyboardSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 25
    }
    [CrossPageKeyboardSmokeNative]::mouse_event([CrossPageKeyboardSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    return 'screen-input'
}

function Send-Key([byte]$keyCode, [byte[]]$modifiers = @(), [string]$operation) {
    Ensure-ForegroundWindow $hwnd $operation
    foreach ($modifier in $modifiers) {
        [CrossPageKeyboardSmokeNative]::keybd_event($modifier, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 25
    }
    [CrossPageKeyboardSmokeNative]::keybd_event($keyCode, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 45
    [CrossPageKeyboardSmokeNative]::keybd_event($keyCode, 0, [CrossPageKeyboardSmokeNative]::KeyUp, [UIntPtr]::Zero)
    foreach ($modifier in ($modifiers | Select-Object -Reverse)) {
        [CrossPageKeyboardSmokeNative]::keybd_event($modifier, 0, [CrossPageKeyboardSmokeNative]::KeyUp, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 25
    }
    if ([CrossPageKeyboardSmokeNative]::GetForegroundWindow() -ne $hwnd) {
        throw "REAL_KEYBOARD_INPUT_UNAVAILABLE operation='$operation' foreground-changed-during-dispatch"
    }
    return 'os-keyboard-input'
}

function Find-FilePdfTile($mainWindow) {
    $tilesGrid = Find-DescendantByAutomationId $mainWindow 'TilesGrid'
    if ($null -eq $tilesGrid) { return $null }
    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    return @($tilesGrid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)) |
        Where-Object { $_.Current.Name -match '(?i)\.pdf' -and $_.Current.BoundingRectangle.Width -gt 150 } |
        Select-Object -First 1
}

function Invoke-ToolbarPointerClick([int]$processId, [IntPtr]$windowHandle, [string]$automationId) {
    $element = Find-DescendantByAutomationId (Find-MainWindow $processId) $automationId
    if ($null -eq $element) { throw "Toolbar control was not found: $automationId" }
    $rect = $element.Current.BoundingRectangle
    $x = [int][Math]::Round($rect.Left + ($rect.Width * 0.5))
    $y = [int][Math]::Round($rect.Top + ($rect.Height * 0.5))
    $mode = Send-PointerClick $windowHandle $x $y "toolbar:$automationId"
    Write-Host "TOOL_POINTER_CLICK id='$automationId' x=$x y=$y mode='$mode' rect=$rect"
    Start-Sleep -Milliseconds 250
    return $mode
}

function Get-PageRect([int]$processId, [int]$pageIndex) {
    $element = Find-DescendantByAutomationId (Find-MainWindow $processId) "PdfPageControl.$pageIndex"
    if ($null -eq $element -or $element.Current.IsOffscreen) { return $null }
    $rect = $element.Current.BoundingRectangle
    if ($rect.Width -lt 200 -or $rect.Height -lt 200) { return $null }
    return $rect
}

function Get-VisibleEditInPage([int]$processId, [System.Windows.Rect]$pageRect) {
    $mainWindow = Find-MainWindow $processId
    $editCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $edits = @($mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition))
    return $edits |
        Where-Object {
            $rect = $_.Current.BoundingRectangle
            -not $_.Current.IsOffscreen -and
                $rect.Width -gt 40 -and $rect.Height -gt 12 -and
                $rect.Left -ge ($pageRect.Left - 20) -and
                $rect.Top -ge ($pageRect.Top - 20) -and
                $rect.Left -lt ($pageRect.Right + 20) -and
                $rect.Top -lt ($pageRect.Bottom + 20)
        } |
        Sort-Object { $_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height } -Descending |
        Select-Object -First 1
}

function Get-ElementValue($element) {
    if ($null -eq $element) { return $null }
    try {
        $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        return $pattern.Current.Value
    }
    catch { return $null }
}

function Wait-TextOnPage([int]$processId, [int]$pageIndex, [string]$expectedText, [int]$timeoutSeconds = 10) {
    return Wait-Until {
        $pageRect = Get-PageRect $processId $pageIndex
        if ($null -eq $pageRect) { return $null }
        $edit = Get-VisibleEditInPage $processId $pageRect
        if ($null -ne $edit -and (Get-ElementValue $edit) -eq $expectedText) {
            return $edit
        }
        return $null
    } $timeoutSeconds
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

function Close-IsolatedOpenNotes([System.Diagnostics.Process]$targetProcess) {
    if ($null -eq $targetProcess) { return }
    try {
        if (-not $targetProcess.HasExited) {
            [void]$targetProcess.CloseMainWindow()
            if (-not $targetProcess.WaitForExit(5000)) {
                $targetProcess.Kill()
                [void]$targetProcess.WaitForExit()
            }
        }
    }
    finally {
        $targetProcess.Dispose()
    }
}

function Open-IsolatedPdf([System.Diagnostics.Process]$targetProcess) {
    $mainWindow = Wait-Until {
        if ($targetProcess.HasExited) {
            throw "OpenNotes exited during startup with code $($targetProcess.ExitCode)."
        }
        Find-MainWindow $targetProcess.Id
    } 20
    if ($null -eq $mainWindow) { throw 'OpenNotes main window was not found.' }
    $hwnd = [IntPtr]$mainWindow.Current.NativeWindowHandle
    Ensure-ForegroundWindow $hwnd 'open-library'
    $tile = Wait-Until { Find-FilePdfTile (Find-MainWindow $targetProcess.Id) } 15
    if ($null -eq $tile) { throw 'The isolated two-page PDF library tile was not found.' }
    $tileRect = $tile.Current.BoundingRectangle
    [void](Send-PointerClick $hwnd `
        ([int][Math]::Round($tileRect.Left + ($tileRect.Width * 0.5))) `
        ([int][Math]::Round($tileRect.Top + ($tileRect.Height * 0.5))) `
        'open-library-tile')
    $editor = Wait-Until { Find-DescendantByAutomationId (Find-MainWindow $targetProcess.Id) 'TextToolButton' } 60
    if ($null -eq $editor) { throw 'Editor toolbar did not load after opening the two-page PDF.' }
    return [pscustomobject]@{
        MainWindow = Find-MainWindow $targetProcess.Id
        Hwnd = $hwnd
    }
}

function Prepare-TwoPageViewport([System.Diagnostics.Process]$targetProcess, [IntPtr]$windowHandle) {
    $fitButton = Find-DescendantByAutomationId (Find-MainWindow $targetProcess.Id) 'FitPageButton'
    if ($null -ne $fitButton) {
        [void](Invoke-ToolbarPointerClick $targetProcess.Id $windowHandle 'FitPageButton')
    }
    for ($attempt = 0; $attempt -lt 12; $attempt++) {
        $viewer = Find-DescendantByAutomationId (Find-MainWindow $targetProcess.Id) 'PdfScrollViewer'
        $page0 = Get-PageRect $targetProcess.Id 0
        $page1 = Get-PageRect $targetProcess.Id 1
        if ($null -ne $viewer -and $null -ne $page0 -and $null -ne $page1) {
            $viewerRect = $viewer.Current.BoundingRectangle
            if ($page0.Top -ge ($viewerRect.Top - 8) -and
                $page1.Bottom -le ($viewerRect.Bottom + 8)) {
                Write-Output "TWO_PAGE_VIEW_READY viewerRect=$viewerRect page0=$page0 page1=$page1 zoomOuts=$attempt"
                return [pscustomobject]@{ Viewer = $viewerRect; Page0 = $page0; Page1 = $page1 }
            }
        }
        [void](Invoke-ToolbarPointerClick $targetProcess.Id $windowHandle 'ZoomOutButton')
        Start-Sleep -Milliseconds 350
    }
    throw 'Could not bring both runtime PdfPageControl surfaces into the same visible viewport.'
}

try {
    if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        throw 'SystemRoot must be available for the isolated WPF smoke test.'
    }

    New-Item -ItemType Directory -Path $temporaryEnvironmentPath -Force | Out-Null
    New-TwoPagePdf $pdfPath
    New-Item -ItemType Directory -Path $sidecarRoot -Force | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [System.IO.File]::WriteAllText(
        (Join-Path $sidecarRoot 'settings.json'),
        '{"WholeStrokeEraser":true,"PenOnlyMode":false}',
        $utf8)
    $recentEntry = [ordered]@{
        Id = ([guid]::NewGuid().ToString('N'))
        EntryType = 'file'
        ParentFolderId = ''
        DisplayName = [System.IO.Path]::GetFileName($pdfPath)
        IsNotebook = $false
        Path = $pdfPath
        PageCount = 2
        LastModifiedUtc = [System.IO.File]::GetLastWriteTimeUtc($pdfPath).ToString('o')
        LastOpenedUtc = [DateTime]::UtcNow.ToString('o')
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $sidecarRoot 'recent_files.json'),
        '[' + ($recentEntry | ConvertTo-Json -Depth 4 -Compress) + ']',
        $utf8)

    $process = Start-IsolatedOpenNotes
    $openState = Open-IsolatedPdf $process
    $hwnd = $openState.Hwnd
    $viewport = Prepare-TwoPageViewport $process $hwnd
    $expectedText = 'Cross page keyboard smoke'

    [void](Invoke-ToolbarPointerClick $process.Id $hwnd 'TextToolButton')
    $createX = [int][Math]::Round($viewport.Page0.Left + ($viewport.Page0.Width * 0.28))
    $createY = [int][Math]::Round($viewport.Page0.Top + ($viewport.Page0.Height * 0.38))
    [void](Send-PointerClick $hwnd $createX $createY 'create-cross-page-text')
    $textBox = Wait-Until {
        $pageRect = Get-PageRect $process.Id 0
        if ($null -eq $pageRect) { return $null }
        return Get-VisibleEditInPage $process.Id $pageRect
    } 10
    if ($null -eq $textBox) { throw 'The real pointer did not create a text box on page 1.' }
    $valuePattern = $textBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $valuePattern.SetValue($expectedText)
    Start-Sleep -Milliseconds 250
    if ((Get-ElementValue $textBox) -ne $expectedText) { throw 'The isolated text box did not accept its expected value.' }
    Write-Output "TEXT_BOX_CREATED page=0 value='$expectedText' rect=$($textBox.Current.BoundingRectangle)"

    # Alt+Arrow is intentionally allowed while the TextBox has focus and must
    # move the selected container, not the caret inside its text.
    [void]$textBox.SetFocus()
    $beforeNudgeRect = $textBox.Current.BoundingRectangle
    for ($i = 0; $i -lt 24; $i++) {
        [void](Send-Key 0x27 ([byte[]](0x12)) "text-box-alt-right-$i")
    }
    $nudgedTextBox = Wait-Until {
        $candidate = Wait-TextOnPage $process.Id 0 $expectedText 1
        if ($null -ne $candidate -and $candidate.Current.BoundingRectangle.Left -gt ($beforeNudgeRect.Left + 4)) {
            return $candidate
        }
        return $null
    } 8
    if ($null -eq $nudgedTextBox) {
        throw "Alt+Right did not move the text box before=$beforeNudgeRect"
    }
    Write-Output "KEYBOARD_NUDGE_COMPLETED before=$beforeNudgeRect after=$($nudgedTextBox.Current.BoundingRectangle)"

    $resizeHandle = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'TextResizeHandle.BottomRight'
    if ($null -eq $resizeHandle) { throw 'BottomRight text resize handle was not exposed to UIA.' }
    [void]$resizeHandle.SetFocus()
    $beforeKeyboardResize = (Wait-TextOnPage $process.Id 0 $expectedText 5).Current.BoundingRectangle
    for ($i = 0; $i -lt 4; $i++) {
        [void](Send-Key 0x27 ([byte[]](0x10)) "text-resize-shift-right-$i")
    }
    $resizedTextBox = Wait-Until {
        $candidate = Wait-TextOnPage $process.Id 0 $expectedText 1
        if ($null -ne $candidate -and $candidate.Current.BoundingRectangle.Width -gt ($beforeKeyboardResize.Width + 8)) {
            return $candidate
        }
        return $null
    } 8
    if ($null -eq $resizedTextBox) {
        throw "Shift+Right did not resize the focused BottomRight handle before=$beforeKeyboardResize"
    }
    Write-Output "KEYBOARD_RESIZE_COMPLETED before=$beforeKeyboardResize after=$($resizedTextBox.Current.BoundingRectangle)"

    $dragHandle = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'TextAnnotationDragHandle'
    if ($null -eq $dragHandle) { throw 'TextAnnotationDragHandle was not exposed to UIA.' }
    $dragRect = $dragHandle.Current.BoundingRectangle
    $sourceRect = (Wait-TextOnPage $process.Id 0 $expectedText 5).Current.BoundingRectangle
    $sourceCenterX = $sourceRect.Left + ($sourceRect.Width * 0.5)
    $sourceCenterY = $sourceRect.Top + ($sourceRect.Height * 0.5)
    $targetCenterX = $viewport.Page1.Left + ($viewport.Page1.Width * 0.36)
    $targetCenterY = $viewport.Page1.Top + ($viewport.Page1.Height * 0.36)
    $dragStartX = [int][Math]::Round($dragRect.Left + ($dragRect.Width * 0.5))
    $dragStartY = [int][Math]::Round($dragRect.Top + ($dragRect.Height * 0.5))
    $dragEndX = [int][Math]::Round($dragStartX + ($targetCenterX - $sourceCenterX))
    $dragEndY = [int][Math]::Round($dragStartY + ($targetCenterY - $sourceCenterY))
    [void](Send-PointerDrag $hwnd $dragStartX $dragStartY $dragEndX $dragEndY 'text-cross-page-drag')
    $targetTextBox = Wait-TextOnPage $process.Id 1 $expectedText 12
    $sourceTextBoxAfterDrag = Wait-TextOnPage $process.Id 0 $expectedText 2
    if ($null -eq $targetTextBox -or $null -ne $sourceTextBoxAfterDrag) {
        throw "Cross-page drag did not transfer the text box sourcePresent=$($null -ne $sourceTextBoxAfterDrag) targetPresent=$($null -ne $targetTextBox)"
    }
    Write-Output "TEXT_CROSS_PAGE_COMPLETED sourcePage=0 targetPage=1 targetRect=$($targetTextBox.Current.BoundingRectangle)"

    [void](Invoke-ToolbarPointerClick $process.Id $hwnd 'UndoButton')
    $undoSource = Wait-TextOnPage $process.Id 0 $expectedText 10
    $undoTarget = Wait-TextOnPage $process.Id 1 $expectedText 2
    if ($null -eq $undoSource -or $null -ne $undoTarget) {
        throw "Cross-page undo did not restore sourcePage=0 sourcePresent=$($null -ne $undoSource) targetPresent=$($null -ne $undoTarget)"
    }
    [void](Invoke-ToolbarPointerClick $process.Id $hwnd 'RedoButton')
    $redoTarget = Wait-TextOnPage $process.Id 1 $expectedText 10
    $redoSource = Wait-TextOnPage $process.Id 0 $expectedText 2
    if ($null -eq $redoTarget -or $null -ne $redoSource) {
        throw "Cross-page redo did not restore targetPage=1 sourcePresent=$($null -ne $redoSource) targetPresent=$($null -ne $redoTarget)"
    }
    Write-Output 'CROSS_PAGE_UNDO_REDO_COMPLETED undoSource=True redoTarget=True'

    $beforeSaveHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
    [void](Invoke-ToolbarPointerClick $process.Id $hwnd 'SavePdfButton')
    $afterSaveHash = Wait-Until {
        try {
            $candidateHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
            if ($candidateHash -ne $beforeSaveHash) { return $candidateHash }
        }
        catch { return $null }
        return $null
    } 15
    if ($null -eq $afterSaveHash) { throw 'Cross-page save did not update the isolated PDF.' }
    Write-Output "CROSS_PAGE_SAVE_COMPLETED hashChanged=True before=$beforeSaveHash after=$afterSaveHash"

    Close-IsolatedOpenNotes $process
    $process = $null
    $process = Start-IsolatedOpenNotes
    $reopenState = Open-IsolatedPdf $process
    $hwnd = $reopenState.Hwnd
    $reopenViewport = Prepare-TwoPageViewport $process $hwnd
    $reopenedTarget = Wait-TextOnPage $process.Id 1 $expectedText 15
    $reopenedSource = Wait-TextOnPage $process.Id 0 $expectedText 2
    if ($null -eq $reopenedTarget -or $null -ne $reopenedSource) {
        throw "Cross-page reopen did not preserve destination page sourcePresent=$($null -ne $reopenedSource) targetPresent=$($null -ne $reopenedTarget)"
    }
    Write-Output "CROSS_PAGE_REOPEN_COMPLETED targetPage=1 sourcePresent=False rect=$($reopenedTarget.Current.BoundingRectangle)"
    Write-Output 'CROSS_PAGE_KEYBOARD_SMOKE_RESULT=PASS'
}
catch {
    Write-Output 'CROSS_PAGE_KEYBOARD_SMOKE_RESULT=FAIL'
    throw
}
finally {
    Close-IsolatedOpenNotes $process
    if (Test-Path -LiteralPath $temporaryEnvironmentPath) {
        try {
            Remove-Item -LiteralPath $temporaryEnvironmentPath -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Failed to remove the exact cross-page smoke temporary directory: $($_.Exception.Message)"
        }
    }
    Write-Output "ISOLATED_ENV_CLEANED=$(-not (Test-Path -LiteralPath $temporaryEnvironmentPath))"
}
