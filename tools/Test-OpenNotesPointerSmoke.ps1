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
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    public const uint MouseMove = 0x0001;
    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
    public const uint WmMouseMove = 0x0200;
    public const uint WmLeftDown = 0x0201;
    public const uint WmLeftUp = 0x0202;
}
'@
}

$temporaryEnvironmentPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'OpenNotesPointerSmoke_' + [guid]::NewGuid().ToString('N'))
$pdfPath = Join-Path $temporaryEnvironmentPath 'pointer-smoke.pdf'
$sidecarRoot = Join-Path $temporaryEnvironmentPath 'Caelum'
$process = $null

function New-MinimalPdf([string]$path) {
    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << >> >>',
        "<< /Length 0 >>`nstream`n`nendstream"
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

function Invoke-UiAutomationElement($element) {
    if ($null -eq $element) { throw 'The requested UI Automation element was not found.' }
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    [void]$pattern.Invoke()
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

function Find-FilePdfTile($mainWindow) {
    $tilesGrid = Find-DescendantByAutomationId $mainWindow 'TilesGrid'
    if ($null -eq $tilesGrid) { return $null }

    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $buttons = @($tilesGrid.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $buttonCondition))
    return $buttons |
        Where-Object {
            $rect = $_.Current.BoundingRectangle
            $rect.Width -gt 200 -and $rect.Height -gt 200
        } |
        Select-Object -Skip 1 -First 1
}

function Get-VisibleEdit($mainWindow, [System.Windows.Rect]$viewerRect) {
    $editCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $edits = @($mainWindow.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $editCondition))
    return $edits |
        Where-Object {
            $rect = $_.Current.BoundingRectangle
            -not $_.Current.IsOffscreen -and
                $rect.Width -gt 80 -and $rect.Height -gt 20 -and
                $rect.Left -ge $viewerRect.Left -and
                $rect.Top -ge $viewerRect.Top -and
                $rect.Right -le $viewerRect.Right -and
                $rect.Bottom -le $viewerRect.Bottom
        } |
        Sort-Object { $_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height } -Descending |
        Select-Object -First 1
}

function Get-EditValue($element) {
    if ($null -eq $element) { return $null }
    try {
        $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        return $pattern.Current.Value
    }
    catch {
        return $null
    }
}

function Get-ToggleState([int]$processId, [string]$automationId) {
    $element = Find-DescendantByAutomationId (Find-MainWindow $processId) $automationId
    if ($null -eq $element) { throw "Toggle control was not found: $automationId" }
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    return $pattern.Current.ToggleState.ToString()
}

function Get-PdfInkAnnotationCount([string]$path) {
    $ascii = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($path))
    return [System.Text.RegularExpressions.Regex]::Matches(
        $ascii, '/Subtype\s*/Ink(?:\s|/|>)').Count
}

function Invoke-ToolbarPointerClick([int]$processId, [IntPtr]$hwnd, [string]$automationId) {
    $element = Find-DescendantByAutomationId (Find-MainWindow $processId) $automationId
    if ($null -eq $element) { throw "Toolbar control was not found: $automationId" }
    $rect = $element.Current.BoundingRectangle
    $clickPoint = [System.Windows.Point]::new(0, 0)
    if ($element.TryGetClickablePoint([ref]$clickPoint)) {
        $x = [int][Math]::Round($clickPoint.X)
        $y = [int][Math]::Round($clickPoint.Y)
    }
    else {
        $x = [int][Math]::Round($rect.Left + ($rect.Width * 0.5))
        $y = [int][Math]::Round($rect.Top + ($rect.Height * 0.5))
    }
    $mode = Send-PointerClick $hwnd $x $y
    Write-Output "TOOL_POINTER_CLICK id='$automationId' x=$x y=$y mode='$mode' rect=$rect"
    Start-Sleep -Milliseconds 300
}

function Send-PointerClick([IntPtr]$hwnd, [int]$x, [int]$y) {
    if ([PointerSmokeNative]::SetCursorPos($x, $y)) {
        [PointerSmokeNative]::mouse_event([PointerSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        [PointerSmokeNative]::mouse_event([PointerSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [PointerSmokeNative]::mouse_event([PointerSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
        return 'screen-input'
    }

    $clientPoint = [PointerSmokeNative+POINT]::new()
    $clientPoint.X = $x
    $clientPoint.Y = $y
    if (-not [PointerSmokeNative]::ScreenToClient($hwnd, [ref]$clientPoint)) {
        throw "POINTER_INJECTION_UNAVAILABLE requested=$x,$y"
    }
    $lParam = [IntPtr](($clientPoint.Y -shl 16) -bor ($clientPoint.X -band 0xffff))
    if (-not [PointerSmokeNative]::PostMessage($hwnd, [PointerSmokeNative]::WmMouseMove, [IntPtr]::Zero, $lParam) -or
        -not [PointerSmokeNative]::PostMessage($hwnd, [PointerSmokeNative]::WmLeftDown, [IntPtr]1, $lParam)) {
        throw "POINTER_INJECTION_UNAVAILABLE requested=$x,$y"
    }
    Start-Sleep -Milliseconds 80
    if (-not [PointerSmokeNative]::PostMessage($hwnd, [PointerSmokeNative]::WmLeftUp, [IntPtr]::Zero, $lParam)) {
        throw "POINTER_INJECTION_UNAVAILABLE requested=$x,$y"
    }
    return 'window-message'
}

function Send-PointerDrag(
    [IntPtr]$hwnd,
    [int]$startX,
    [int]$startY,
    [int]$endX,
    [int]$endY) {
    if ([PointerSmokeNative]::SetCursorPos($startX, $startY)) {
        [PointerSmokeNative]::mouse_event([PointerSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        [PointerSmokeNative]::mouse_event([PointerSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        for ($step = 1; $step -le 12; $step++) {
            $progress = $step / 12.0
            $x = [int][Math]::Round($startX + (($endX - $startX) * $progress))
            $y = [int][Math]::Round($startY + (($endY - $startY) * $progress))
            if (-not [PointerSmokeNative]::SetCursorPos($x, $y)) {
                throw "POINTER_DRAG_INTERRUPTED requested=$x,$y"
            }
            [PointerSmokeNative]::mouse_event([PointerSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 30
        }
        [PointerSmokeNative]::mouse_event([PointerSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
        return 'screen-input'
    }

    $startClient = [PointerSmokeNative+POINT]::new()
    $startClient.X = $startX
    $startClient.Y = $startY
    $endClient = [PointerSmokeNative+POINT]::new()
    $endClient.X = $endX
    $endClient.Y = $endY
    if (-not [PointerSmokeNative]::ScreenToClient($hwnd, [ref]$startClient) -or
        -not [PointerSmokeNative]::ScreenToClient($hwnd, [ref]$endClient)) {
        throw "POINTER_DRAG_UNAVAILABLE requested=$startX,$startY to $endX,$endY"
    }

    function ConvertTo-MouseLParam([int]$x, [int]$y) {
        return [IntPtr](($y -shl 16) -bor ($x -band 0xffff))
    }

    $startLParam = ConvertTo-MouseLParam $startClient.X $startClient.Y
    if (-not [PointerSmokeNative]::PostMessage(
            $hwnd,
            [PointerSmokeNative]::WmMouseMove,
            [IntPtr]::Zero,
            $startLParam) -or
        -not [PointerSmokeNative]::PostMessage(
            $hwnd,
            [PointerSmokeNative]::WmLeftDown,
            [IntPtr]1,
            $startLParam)) {
        throw "POINTER_DRAG_UNAVAILABLE requested=$startX,$startY to $endX,$endY"
    }
    Start-Sleep -Milliseconds 100
    for ($step = 1; $step -le 12; $step++) {
        $progress = $step / 12.0
        $x = [int][Math]::Round($startClient.X + (($endClient.X - $startClient.X) * $progress))
        $y = [int][Math]::Round($startClient.Y + (($endClient.Y - $startClient.Y) * $progress))
        [void][PointerSmokeNative]::PostMessage(
            $hwnd,
            [PointerSmokeNative]::WmMouseMove,
            [IntPtr]1,
            (ConvertTo-MouseLParam $x $y))
        Start-Sleep -Milliseconds 30
    }
    [void][PointerSmokeNative]::PostMessage(
        $hwnd,
        [PointerSmokeNative]::WmLeftUp,
        [IntPtr]::Zero,
        (ConvertTo-MouseLParam $endClient.X $endClient.Y))
    return 'window-message'
}

function Write-UiAutomationSnapshot($mainWindow) {
    $all = @($mainWindow.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition))
    foreach ($element in $all) {
        $id = $element.Current.AutomationId
        if (-not [string]::IsNullOrWhiteSpace($id) -or $element.Current.ControlType.ProgrammaticName -eq 'ControlType.Edit') {
            $rect = $element.Current.BoundingRectangle
            Write-Output "CONTROL_SNAPSHOT type='$($element.Current.ControlType.ProgrammaticName)' id='$id' name='$($element.Current.Name)' rect=$rect"
        }
    }
}

function Write-ErrorDialogSnapshot([int]$processId) {
    $windows = Get-ProcessWindows $processId |
        Where-Object { $_.Current.Name -ne 'OpenNotes' }
    foreach ($window in $windows) {
        Write-Output "SECONDARY_WINDOW type='$($window.Current.ControlType.ProgrammaticName)' name='$($window.Current.Name)'"
        $textCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)
        $texts = @($window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants, $textCondition))
        foreach ($text in $texts) {
            if (-not [string]::IsNullOrWhiteSpace($text.Current.Name)) {
                Write-Output "SECONDARY_TEXT name='$($text.Current.Name)'"
            }
        }
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        throw 'SystemRoot must be available for the isolated WPF smoke test.'
    }

    New-Item -ItemType Directory -Path $temporaryEnvironmentPath -Force | Out-Null
    New-MinimalPdf $pdfPath
    $nowUtc = [DateTime]::UtcNow
    $recentEntry = [ordered]@{
        Id = ([guid]::NewGuid().ToString('N'))
        EntryType = 'file'
        ParentFolderId = ''
        DisplayName = [System.IO.Path]::GetFileName($pdfPath)
        IsNotebook = $false
        Path = $pdfPath
        PageCount = 0
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
        '[' + ($recentEntry | ConvertTo-Json -Depth 4) + ']',
        $utf8)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedExecutablePath
    $startInfo.WorkingDirectory = Split-Path -Parent $resolvedExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables['LOCALAPPDATA'] = $temporaryEnvironmentPath
    $startInfo.EnvironmentVariables['APPDATA'] = $temporaryEnvironmentPath
    $startInfo.EnvironmentVariables['OPENNOTES_DATA_ROOT'] = $temporaryEnvironmentPath
    $startInfo.EnvironmentVariables['SystemRoot'] = $env:SystemRoot
    [void]$startInfo.EnvironmentVariables.Remove('WINDIR')
    $process = [System.Diagnostics.Process]::Start($startInfo)

    $mainWindow = Wait-Until {
        if ($process.HasExited) { throw "OpenNotes exited during startup with code $($process.ExitCode)." }
        Find-MainWindow $process.Id
    } 20
    if ($null -eq $mainWindow) { throw 'OpenNotes main window was not found.' }
    Write-Output "MAIN_WINDOW name='$($mainWindow.Current.Name)' pid=$($process.Id)"

    $fileTile = Wait-Until { Find-FilePdfTile (Find-MainWindow $process.Id) } 15
    if ($null -eq $fileTile) { throw 'The pre-seeded PDF library tile was not found.' }
    Invoke-UiAutomationElement $fileTile
    Write-Output 'OPEN_REQUESTED via=library-tile'

    $textTool = Wait-Until {
        $window = Find-MainWindow $process.Id
        Find-DescendantByAutomationId $window 'TextToolButton'
    } 60
    if ($null -eq $textTool) { throw 'TextToolButton was not found after opening the PDF.' }
    $initialTextState = Get-ToggleState $process.Id 'TextToolButton'
    Write-Output "TEXT_TOOL_STATE_BEFORE_POINTER=$initialTextState"
    $hwnd = [IntPtr]$mainWindow.Current.NativeWindowHandle
    [void][PointerSmokeNative]::ShowWindow($hwnd, 5)
    $foregroundSet = [PointerSmokeNative]::SetForegroundWindow($hwnd)
    [uint32]$foregroundPid = 0
    [void][PointerSmokeNative]::GetWindowThreadProcessId(
        [PointerSmokeNative]::GetForegroundWindow(),
        [ref]$foregroundPid)
    Write-Output "FOREGROUND_TARGET_SET=$foregroundSet foregroundPid=$foregroundPid targetPid=$($process.Id)"
    Start-Sleep -Milliseconds 300
    $toolRect = $textTool.Current.BoundingRectangle
    $toolX = [int][Math]::Round($toolRect.Left + ($toolRect.Width * 0.5))
    $toolY = [int][Math]::Round($toolRect.Top + ($toolRect.Height * 0.5))
    $toolDispatchMode = Send-PointerClick $hwnd $toolX $toolY
    Write-Output "TEXT_TOOL_POINTER_CLICK x=$toolX y=$toolY mode='$toolDispatchMode'"
    Start-Sleep -Milliseconds 500
    $textTool = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'TextToolButton'
    $toggle = $textTool.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $stateAfterFirstPointer = $toggle.Current.ToggleState.ToString()
    Write-Output "TEXT_TOOL_STATE_AFTER_POINTER=$stateAfterFirstPointer"
    for ($retry = 1; $retry -le 2 -and $stateAfterFirstPointer -ne 'On'; $retry++) {
        $toolRect = $textTool.Current.BoundingRectangle
        $toolX = [int][Math]::Round($toolRect.Left + ($toolRect.Width * 0.5))
        $toolY = [int][Math]::Round($toolRect.Top + ($toolRect.Height * 0.5))
        $toolDispatchMode = Send-PointerClick $hwnd $toolX $toolY
        Write-Output "TEXT_TOOL_POINTER_RETRY attempt=$retry x=$toolX y=$toolY mode='$toolDispatchMode'"
        Start-Sleep -Milliseconds 500
        $textTool = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'TextToolButton'
        $toggle = $textTool.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $stateAfterFirstPointer = $toggle.Current.ToggleState.ToString()
        Write-Output "TEXT_TOOL_STATE_AFTER_RETRY attempt=$retry state=$stateAfterFirstPointer"
    }
    if ($toggle.Current.ToggleState.ToString() -ne 'On') {
        throw 'The real pointer click did not activate TextToolButton.'
    }

    Invoke-ToolbarPointerClick $process.Id $hwnd 'PenToolButton'
    $penAfter = Get-ToggleState $process.Id 'PenToolButton'
    $textAfterPen = Get-ToggleState $process.Id 'TextToolButton'
    Write-Output "TOOL_POINTER_STATE_AFTER_PEN pen='$penAfter' text='$textAfterPen'"
    Invoke-ToolbarPointerClick $process.Id $hwnd 'TextToolButton'
    $penAfterText = Get-ToggleState $process.Id 'PenToolButton'
    $textAfterText = Get-ToggleState $process.Id 'TextToolButton'
    Write-Output "TOOL_POINTER_STATE_AFTER_TEXT pen='$penAfterText' text='$textAfterText'"
    if ($penAfterText -ne 'Off' -or $textAfterText -ne 'On') {
        throw 'Toolbar pointer clicks did not execute the expected tool-switch handlers.'
    }

    $viewer = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'PdfScrollViewer'
    if ($null -eq $viewer) { throw 'PdfScrollViewer was not found.' }
    $viewerRect = $viewer.Current.BoundingRectangle
    if ($viewerRect.Width -lt 300 -or $viewerRect.Height -lt 200) { throw "PdfScrollViewer has unusable bounds: $viewerRect" }

    Invoke-ToolbarPointerClick $process.Id $hwnd 'PenToolButton'
    $penState = Get-ToggleState $process.Id 'PenToolButton'
    if ($penState -ne 'On') { throw "PenToolButton was not active for the real drawing smoke: $penState" }
    $strokeStartX = [int][Math]::Round($viewerRect.Left + ($viewerRect.Width * 0.34))
    $strokeStartY = [int][Math]::Round($viewerRect.Top + ($viewerRect.Height * 0.56))
    $strokeEndX = $strokeStartX + 220
    $strokeEndY = $strokeStartY + 34
    $strokeDispatchMode = Send-PointerDrag $hwnd $strokeStartX $strokeStartY $strokeEndX $strokeEndY
    Write-Output "PEN_POINTER_DRAG start=$strokeStartX,$strokeStartY end=$strokeEndX,$strokeEndY mode='$strokeDispatchMode' viewerRect=$viewerRect"
    Start-Sleep -Milliseconds 500
    $undoAfterStroke = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'UndoButton'
    $saveAfterStroke = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'SavePdfButton'
    Write-Output "PEN_POINTER_AFTER_DRAG undoEnabled=$($null -ne $undoAfterStroke -and $undoAfterStroke.Current.IsEnabled) saveEnabled=$($null -ne $saveAfterStroke -and $saveAfterStroke.Current.IsEnabled)"
    $inkCountBeforeStrokeSave = Get-PdfInkAnnotationCount $pdfPath
    $hashBeforeStrokeSave = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
    $saveButton = Wait-Until {
        $candidate = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'SavePdfButton'
        if ($null -ne $candidate -and $candidate.Current.IsEnabled) { return $candidate }
        return $null
    } 10
    if ($null -eq $saveButton) { throw 'SavePdfButton was not enabled after the real pen stroke.' }
    Invoke-ToolbarPointerClick $process.Id $hwnd 'SavePdfButton'
    $strokeSavedHash = Wait-Until {
        try {
            $candidateHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
            if ($candidateHash -ne $hashBeforeStrokeSave) { return $candidateHash }
        }
        catch { return $null }
        return $null
    } 15
    if ($null -eq $strokeSavedHash) { throw 'The real pen stroke did not update the isolated PDF.' }
    $inkCountAfterStroke = Get-PdfInkAnnotationCount $pdfPath
    if ($inkCountAfterStroke -le $inkCountBeforeStrokeSave) {
        throw "The saved PDF did not contain a new /Ink annotation before=$inkCountBeforeStrokeSave after=$inkCountAfterStroke"
    }
    Write-Output "PEN_POINTER_DRAW_COMPLETED mode='$strokeDispatchMode' inkBefore=$inkCountBeforeStrokeSave inkAfter=$inkCountAfterStroke"

    Invoke-ToolbarPointerClick $process.Id $hwnd 'EraserToolButton'
    $eraserState = Get-ToggleState $process.Id 'EraserToolButton'
    if ($eraserState -ne 'On') { throw "EraserToolButton was not active for the real eraser smoke: $eraserState" }
    $eraseX = [int][Math]::Round(($strokeStartX + $strokeEndX) * 0.5)
    $eraseY = [int][Math]::Round(($strokeStartY + $strokeEndY) * 0.5)
    $eraseDispatchMode = Send-PointerClick $hwnd $eraseX $eraseY
    Start-Sleep -Milliseconds 700
    $inkCountBeforeEraseSave = Get-PdfInkAnnotationCount $pdfPath
    $hashBeforeEraseSave = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
    $saveButton = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'SavePdfButton'
    if ($null -eq $saveButton -or -not $saveButton.Current.IsEnabled) {
        throw 'SavePdfButton was not enabled after the real whole-stroke erase.'
    }
    Invoke-ToolbarPointerClick $process.Id $hwnd 'SavePdfButton'
    $eraseSavedHash = Wait-Until {
        try {
            $candidateHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
            if ($candidateHash -ne $hashBeforeEraseSave) { return $candidateHash }
        }
        catch { return $null }
        return $null
    } 15
    if ($null -eq $eraseSavedHash) { throw 'The real whole-stroke erase did not update the isolated PDF.' }
    $inkCountAfterErase = Get-PdfInkAnnotationCount $pdfPath
    if ($inkCountAfterErase -ge $inkCountAfterStroke) {
        throw "The saved PDF still contains the erased /Ink annotation before=$inkCountAfterStroke after=$inkCountAfterErase"
    }
    Write-Output "ERASER_POINTER_COMPLETED mode='$eraseDispatchMode' inkBefore=$inkCountBeforeEraseSave inkAfter=$inkCountAfterErase"

    Invoke-ToolbarPointerClick $process.Id $hwnd 'TextToolButton'
    Start-Sleep -Milliseconds 300
    $clickX = [int][Math]::Round($viewerRect.Left + ($viewerRect.Width * 0.50))
    $clickY = [int][Math]::Round($viewerRect.Top + ($viewerRect.Height * 0.42))
    $cursor = [PointerSmokeNative+POINT]::new()
    [void][PointerSmokeNative]::GetCursorPos([ref]$cursor)
    Write-Output "POINTER_SCREEN size=$([PointerSmokeNative]::GetSystemMetrics(0))x$([PointerSmokeNative]::GetSystemMetrics(1)) before=$($cursor.X),$($cursor.Y)"
    Write-Output "POINTER_CLICK x=$clickX y=$clickY viewerRect=$viewerRect"
    $dispatchMode = Send-PointerClick $hwnd $clickX $clickY
    Write-Output "POINTER_DISPATCH mode='$dispatchMode' physicalCursorAvailable=$([PointerSmokeNative]::GetCursorPos([ref]$cursor))"
    [void][PointerSmokeNative]::GetCursorPos([ref]$cursor)
    Write-Output "POINTER_AFTER x=$($cursor.X) y=$($cursor.Y)"

    $textBox = Wait-Until {
        Get-VisibleEdit (Find-MainWindow $process.Id) $viewerRect
    } 10
    if ($null -eq $textBox) {
        Write-UiAutomationSnapshot (Find-MainWindow $process.Id)
        if ($dispatchMode -eq 'window-message') {
            throw 'POINTER_MESSAGE_FALLBACK_NO_TEXT_BOX window-message-dispatch-did-not-reach-WPF-hit-testing'
        }
        throw 'A text box was not created by the physical pointer click.'
    }
    Write-Output "TEXT_BOX_CREATED rect=$($textBox.Current.BoundingRectangle)"

    $handleIds = @(
        'TextResizeHandle.TopLeft', 'TextResizeHandle.Top', 'TextResizeHandle.TopRight',
        'TextResizeHandle.Left', 'TextResizeHandle.Right',
        'TextResizeHandle.BottomLeft', 'TextResizeHandle.Bottom', 'TextResizeHandle.BottomRight'
    )
    $missing = @()
    foreach ($handleId in $handleIds) {
        $handle = Find-DescendantByAutomationId (Find-MainWindow $process.Id) $handleId
        if ($null -eq $handle -or $handle.Current.IsOffscreen) {
            $missing += $handleId
        }
        else {
            Write-Output "RESIZE_HANDLE_FOUND id='$handleId' rect=$($handle.Current.BoundingRectangle)"
        }
    }
    if ($missing.Count -gt 0) {
        Write-UiAutomationSnapshot (Find-MainWindow $process.Id)
        throw "RESIZE_HANDLES_MISSING ids='$($missing -join ',')'"
    }

    $bottomRightHandle = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'TextResizeHandle.BottomRight'
    $beforeResizeRect = $textBox.Current.BoundingRectangle
    $handleRect = $bottomRightHandle.Current.BoundingRectangle
    $dragStartX = [int][Math]::Round($handleRect.Left + ($handleRect.Width * 0.5))
    $dragStartY = [int][Math]::Round($handleRect.Top + ($handleRect.Height * 0.5))
    $dragEndX = $dragStartX + 120
    $dragEndY = $dragStartY + 72
    $dragDispatchMode = Send-PointerDrag $hwnd $dragStartX $dragStartY $dragEndX $dragEndY
    Write-Output "TEXT_RESIZE_DRAG start=$dragStartX,$dragStartY end=$dragEndX,$dragEndY mode='$dragDispatchMode'"

    $expandedTextBox = Wait-Until {
        $candidate = Get-VisibleEdit (Find-MainWindow $process.Id) $viewerRect
        if ($null -eq $candidate) { return $null }
        $rect = $candidate.Current.BoundingRectangle
        if ($rect.Width -gt ($beforeResizeRect.Width + 30) -and
            $rect.Height -gt ($beforeResizeRect.Height + 20)) {
            return $candidate
        }
        return $null
    } 10
    if ($null -eq $expandedTextBox) {
        Write-UiAutomationSnapshot (Find-MainWindow $process.Id)
        throw "TEXT_RESIZE_DRAG_NO_GEOMETRY_CHANGE before=$beforeResizeRect"
    }
    $expandedRect = $expandedTextBox.Current.BoundingRectangle
    Write-Output "TEXT_BOX_RESIZED before=$beforeResizeRect after=$expandedRect"

    $undoButton = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'UndoButton'
    if ($null -eq $undoButton -or -not $undoButton.Current.IsEnabled) {
        throw 'UndoButton was not enabled after the real text-box resize.'
    }
    Invoke-ToolbarPointerClick $process.Id $hwnd 'UndoButton'
    $restoredTextBox = Wait-Until {
        $candidate = Get-VisibleEdit (Find-MainWindow $process.Id) $viewerRect
        if ($null -eq $candidate) { return $null }
        $rect = $candidate.Current.BoundingRectangle
        if ([Math]::Abs($rect.Width - $beforeResizeRect.Width) -le 4 -and
            [Math]::Abs($rect.Height - $beforeResizeRect.Height) -le 4) {
            return $candidate
        }
        return $null
    } 10
    if ($null -eq $restoredTextBox) {
        Write-UiAutomationSnapshot (Find-MainWindow $process.Id)
        throw 'Undo did not restore the original text-box rectangle.'
    }
    Write-Output "TEXT_RESIZE_UNDO restored=True rect=$($restoredTextBox.Current.BoundingRectangle)"

    $redoButton = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'RedoButton'
    if ($null -eq $redoButton -or -not $redoButton.Current.IsEnabled) {
        throw 'RedoButton was not enabled after undoing the real text-box resize.'
    }
    Invoke-ToolbarPointerClick $process.Id $hwnd 'RedoButton'
    $redoneTextBox = Wait-Until {
        $candidate = Get-VisibleEdit (Find-MainWindow $process.Id) $viewerRect
        if ($null -eq $candidate) { return $null }
        $rect = $candidate.Current.BoundingRectangle
        if ($rect.Width -gt ($beforeResizeRect.Width + 30) -and
            $rect.Height -gt ($beforeResizeRect.Height + 20)) {
            return $candidate
        }
        return $null
    } 10
    if ($null -eq $redoneTextBox) {
        Write-UiAutomationSnapshot (Find-MainWindow $process.Id)
        throw 'Redo did not restore the resized text-box rectangle.'
    }
    Write-Output "TEXT_RESIZE_REDO restored=True rect=$($redoneTextBox.Current.BoundingRectangle)"

    # Leave the isolated document at its original geometry before cleanup.
    Invoke-ToolbarPointerClick $process.Id $hwnd 'UndoButton'
    $finalTextBox = Wait-Until {
        $candidate = Get-VisibleEdit (Find-MainWindow $process.Id) $viewerRect
        if ($null -eq $candidate) { return $null }
        $rect = $candidate.Current.BoundingRectangle
        if ([Math]::Abs($rect.Width - $beforeResizeRect.Width) -le 4 -and
            [Math]::Abs($rect.Height - $beforeResizeRect.Height) -le 4) {
            return $candidate
        }
        return $null
    } 10
    if ($null -eq $finalTextBox) {
        throw 'Final cleanup undo did not restore the original text-box rectangle.'
    }

    $valuePattern = $finalTextBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $valuePattern.SetValue('Pointer smoke persistence')
    Start-Sleep -Milliseconds 300
    $enteredText = Get-EditValue $finalTextBox
    if ($enteredText -ne 'Pointer smoke persistence') {
        throw "Text entry did not reach the live text box. value='$enteredText'"
    }
    Write-Output "TEXT_BOX_VALUE_ENTERED value='$enteredText'"

    $hashBeforeSave = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
    $saveButton = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'SavePdfButton'
    if ($null -eq $saveButton -or -not $saveButton.Current.IsEnabled) {
        throw 'SavePdfButton was not enabled during the real save/reopen smoke.'
    }
    Invoke-ToolbarPointerClick $process.Id $hwnd 'SavePdfButton'
    $savedHash = Wait-Until {
        try {
            $candidateHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
            if ($candidateHash -ne $hashBeforeSave) { return $candidateHash }
        }
        catch {
            return $null
        }
        return $null
    } 15
    if ($null -eq $savedHash) {
        Write-ErrorDialogSnapshot $process.Id
        throw 'SavePdfButton did not change the isolated PDF contents.'
    }
    Write-Output "PDF_SAVE_COMPLETED hashChanged=True before=$hashBeforeSave after=$savedHash"

    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(5000)) {
        $process.Kill()
        [void]$process.WaitForExit()
    }
    $process.Dispose()
    $process = $null
    Write-Output 'EDITOR_PROCESS_CLOSED_FOR_REOPEN=True'

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $mainWindow = Wait-Until {
        if ($process.HasExited) { throw "OpenNotes exited during reopen with code $($process.ExitCode)." }
        Find-MainWindow $process.Id
    } 20
    if ($null -eq $mainWindow) { throw 'OpenNotes main window was not found after save/reopen.' }
    $fileTile = Wait-Until { Find-FilePdfTile (Find-MainWindow $process.Id) } 15
    if ($null -eq $fileTile) { throw 'The saved PDF library tile was not found after reopen.' }
    Invoke-UiAutomationElement $fileTile
    $textTool = Wait-Until {
        $window = Find-MainWindow $process.Id
        Find-DescendantByAutomationId $window 'TextToolButton'
    } 60
    if ($null -eq $textTool) { throw 'TextToolButton was not found after reopening the saved PDF.' }
    $viewer = Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'PdfScrollViewer'
    if ($null -eq $viewer) { throw 'PdfScrollViewer was not found after save/reopen.' }
    $viewerRect = $viewer.Current.BoundingRectangle
    $reopenedTextBox = Wait-Until {
        Get-VisibleEdit (Find-MainWindow $process.Id) $viewerRect
    } 15
    if ($null -eq $reopenedTextBox) {
        Write-UiAutomationSnapshot (Find-MainWindow $process.Id)
        throw 'The saved text box was not visible after reopening the PDF.'
    }
    $reopenedValue = Get-EditValue $reopenedTextBox
    if ($reopenedValue -ne 'Pointer smoke persistence') {
        throw "The saved text value did not survive reopen. value='$reopenedValue'"
    }
    Write-Output "PDF_REOPEN_COMPLETED textValuePreserved=True value='$reopenedValue' rect=$($reopenedTextBox.Current.BoundingRectangle)"

    Write-Output 'POINTER_RESIZE_UNDO_REDO_RESULT=PASS'
    Write-Output 'POINTER_SAVE_REOPEN_RESULT=PASS'
    Write-Output 'POINTER_SMOKE_RESULT=PASS'
}
finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                [void]$process.CloseMainWindow()
                if (-not $process.WaitForExit(5000)) {
                    $process.Kill()
                    [void]$process.WaitForExit()
                }
            }
        }
        catch {
            Write-Warning "Failed to close isolated OpenNotes process cleanly: $($_.Exception.Message)"
        }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $temporaryEnvironmentPath) {
        Remove-Item -LiteralPath $temporaryEnvironmentPath -Recurse -Force
    }
    Write-Output "ISOLATED_ENV_CLEANED=$(-not (Test-Path -LiteralPath $temporaryEnvironmentPath))"
}
