[CmdletBinding()]
param(
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'OpenNotesEditorAutomationIds.ps1')

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $PSScriptRoot '..\bin\Debug\net8.0-windows\win-x64\OpenNotes.exe'
}
$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path

if (-not ('AdvancedPointerSmokeNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class AdvancedPointerSmokeNative
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
    public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    public const uint MouseMove = 0x0001;
    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
    public const uint KeyUp = 0x0002;
    public const uint WmMouseMove = 0x0200;
    public const uint WmLeftDown = 0x0201;
    public const uint WmLeftUp = 0x0202;
}
'@
}

$temporaryEnvironmentPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'OpenNotesAdvancedPointerSmoke_' + [guid]::NewGuid().ToString('N'))
$pdfPath = Join-Path $temporaryEnvironmentPath 'advanced-pointer-smoke.pdf'
$sidecarRoot = Join-Path $temporaryEnvironmentPath 'Caelum'
$process = $null
$clipboardSnapshot = $null
$clipboardSnapshotCaptured = $false

function New-AdvancedPdf([string]$path) {
    # A dark page makes the opaque Hidden Ink mask and its timed reveal
    # observable through a screen pixel. It is still a minimal, valid PDF.
    $content = "0 0 0 rg`n0 0 612 792 re`nf`n"
    $contentLength = [System.Text.Encoding]::ASCII.GetByteCount($content)
    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /ProcSet [/PDF] >> >>',
        "<< /Length $contentLength >>`nstream`n$content`nendstream"
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
            $state = Get-ToggleState $processId $automationId
            if ($state -eq $expected) { return $state }
        }
        catch { return $null }
        return $null
    } $timeoutSeconds
}

function ConvertTo-MouseLParam([int]$x, [int]$y) {
    return [IntPtr](($y -shl 16) -bor ($x -band 0xffff))
}

function Ensure-ForegroundWindow([IntPtr]$hwnd, [string]$operation) {
    [void][AdvancedPointerSmokeNative]::ShowWindow($hwnd, 5)
    [void][AdvancedPointerSmokeNative]::SetForegroundWindow($hwnd)
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $foreground = [AdvancedPointerSmokeNative]::GetForegroundWindow()
        if ($foreground -eq $hwnd) {
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    $foregroundProcessId = [uint32]0
    if ($foreground -ne [IntPtr]::Zero) {
        [void][AdvancedPointerSmokeNative]::GetWindowThreadProcessId(
            $foreground,
            [ref]$foregroundProcessId)
    }
    throw "REAL_SCREEN_INPUT_UNAVAILABLE operation='$operation' targetHwnd=$hwnd foregroundHwnd=$foreground foregroundPid=$foregroundProcessId"
}

function Send-PointerClick([IntPtr]$hwnd, [int]$x, [int]$y) {
    Ensure-ForegroundWindow $hwnd 'pointer-click'
    if ([AdvancedPointerSmokeNative]::SetCursorPos($x, $y)) {
        [AdvancedPointerSmokeNative]::mouse_event([AdvancedPointerSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        [AdvancedPointerSmokeNative]::mouse_event([AdvancedPointerSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [AdvancedPointerSmokeNative]::mouse_event([AdvancedPointerSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
        return 'screen-input'
    }

    $clientPoint = [AdvancedPointerSmokeNative+POINT]::new()
    $clientPoint.X = $x
    $clientPoint.Y = $y
    if (-not [AdvancedPointerSmokeNative]::ScreenToClient($hwnd, [ref]$clientPoint)) {
        throw "POINTER_INJECTION_UNAVAILABLE requested=$x,$y"
    }
    $lParam = ConvertTo-MouseLParam $clientPoint.X $clientPoint.Y
    if (-not [AdvancedPointerSmokeNative]::PostMessage($hwnd, [AdvancedPointerSmokeNative]::WmMouseMove, [IntPtr]::Zero, $lParam) -or
        -not [AdvancedPointerSmokeNative]::PostMessage($hwnd, [AdvancedPointerSmokeNative]::WmLeftDown, [IntPtr]1, $lParam)) {
        throw "POINTER_INJECTION_UNAVAILABLE requested=$x,$y"
    }
    Start-Sleep -Milliseconds 80
    if (-not [AdvancedPointerSmokeNative]::PostMessage($hwnd, [AdvancedPointerSmokeNative]::WmLeftUp, [IntPtr]::Zero, $lParam)) {
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
    Ensure-ForegroundWindow $hwnd 'pointer-drag'
    if ([AdvancedPointerSmokeNative]::SetCursorPos($startX, $startY)) {
        [AdvancedPointerSmokeNative]::mouse_event([AdvancedPointerSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        [AdvancedPointerSmokeNative]::mouse_event([AdvancedPointerSmokeNative]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        for ($step = 1; $step -le 20; $step++) {
            $progress = $step / 20.0
            $x = [int][Math]::Round($startX + (($endX - $startX) * $progress))
            $y = [int][Math]::Round($startY + (($endY - $startY) * $progress))
            if (-not [AdvancedPointerSmokeNative]::SetCursorPos($x, $y)) {
                throw "POINTER_DRAG_INTERRUPTED requested=$x,$y"
            }
            [AdvancedPointerSmokeNative]::mouse_event([AdvancedPointerSmokeNative]::MouseMove, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 30
        }
        [AdvancedPointerSmokeNative]::mouse_event([AdvancedPointerSmokeNative]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
        return 'screen-input'
    }

    $startClient = [AdvancedPointerSmokeNative+POINT]::new()
    $startClient.X = $startX
    $startClient.Y = $startY
    $endClient = [AdvancedPointerSmokeNative+POINT]::new()
    $endClient.X = $endX
    $endClient.Y = $endY
    if (-not [AdvancedPointerSmokeNative]::ScreenToClient($hwnd, [ref]$startClient) -or
        -not [AdvancedPointerSmokeNative]::ScreenToClient($hwnd, [ref]$endClient)) {
        throw "POINTER_DRAG_UNAVAILABLE requested=$startX,$startY to $endX,$endY"
    }

    $startLParam = ConvertTo-MouseLParam $startClient.X $startClient.Y
    if (-not [AdvancedPointerSmokeNative]::PostMessage($hwnd, [AdvancedPointerSmokeNative]::WmMouseMove, [IntPtr]::Zero, $startLParam) -or
        -not [AdvancedPointerSmokeNative]::PostMessage($hwnd, [AdvancedPointerSmokeNative]::WmLeftDown, [IntPtr]1, $startLParam)) {
        throw "POINTER_DRAG_UNAVAILABLE requested=$startX,$startY to $endX,$endY"
    }
    Start-Sleep -Milliseconds 100
    for ($step = 1; $step -le 20; $step++) {
        $progress = $step / 20.0
        $x = [int][Math]::Round($startClient.X + (($endClient.X - $startClient.X) * $progress))
        $y = [int][Math]::Round($startClient.Y + (($endClient.Y - $startClient.Y) * $progress))
        [void][AdvancedPointerSmokeNative]::PostMessage(
            $hwnd,
            [AdvancedPointerSmokeNative]::WmMouseMove,
            [IntPtr]1,
            (ConvertTo-MouseLParam $x $y))
        Start-Sleep -Milliseconds 30
    }
    [void][AdvancedPointerSmokeNative]::PostMessage(
        $hwnd,
        [AdvancedPointerSmokeNative]::WmLeftUp,
        [IntPtr]::Zero,
        (ConvertTo-MouseLParam $endClient.X $endClient.Y))
    return 'window-message'
}

function Assert-ScreenInput([string]$mode, [string]$operation) {
    if ($mode -ne 'screen-input') {
        throw "REAL_SCREEN_POINTER_REQUIRED operation='$operation' mode='$mode'"
    }
}

function Invoke-ToolbarPointerClick([int]$processId, [IntPtr]$hwnd, [string]$automationId) {
    $element = Find-DescendantByAutomationId (Find-MainWindow $processId) $automationId
    if ($null -eq $element) { throw "Toolbar control was not found: $automationId" }
    Ensure-ForegroundWindow $hwnd "toolbar:$automationId"
    Start-Sleep -Milliseconds 120
    $rect = $element.Current.BoundingRectangle
    $x = [int][Math]::Round($rect.Left + ($rect.Width * 0.5))
    $y = [int][Math]::Round($rect.Top + ($rect.Height * 0.5))
    $mode = Send-PointerClick $hwnd $x $y
    # Keep the diagnostic visible without putting it into the function's
    # return pipeline. Callers compare the returned mode exactly.
    Write-Host "TOOL_POINTER_CLICK id='$automationId' x=$x y=$y mode='$mode' rect=$rect"
    Start-Sleep -Milliseconds 350
    return $mode
}

function Send-KeyChord([byte]$keyCode) {
    Ensure-ForegroundWindow $hwnd "keyboard:0x$('{0:X2}' -f $keyCode)"
    Start-Sleep -Milliseconds 100
    [AdvancedPointerSmokeNative]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [AdvancedPointerSmokeNative]::keybd_event($keyCode, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [AdvancedPointerSmokeNative]::keybd_event($keyCode, 0, [AdvancedPointerSmokeNative]::KeyUp, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [AdvancedPointerSmokeNative]::keybd_event(0x11, 0, [AdvancedPointerSmokeNative]::KeyUp, [UIntPtr]::Zero)
    if ([AdvancedPointerSmokeNative]::GetForegroundWindow() -ne $hwnd) {
        throw "REAL_KEYBOARD_INPUT_UNAVAILABLE key=0x$('{0:X2}' -f $keyCode) foreground-changed-during-dispatch"
    }
    return 'os-keyboard-input'
}

function Get-PdfText([string]$path) {
    return [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($path))
}

function Get-PdfTokenCount([string]$path, [string]$token) {
    $pdfText = Get-PdfText $path
    return [System.Text.RegularExpressions.Regex]::Matches(
        $pdfText, [System.Text.RegularExpressions.Regex]::Escape($token)).Count
}

function Get-PdfInkAnnotationCount([string]$path) {
    $pdfText = Get-PdfText $path
    return [System.Text.RegularExpressions.Regex]::Matches(
        $pdfText, '/Subtype\s*/Ink(?:\s|/|>)').Count
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

function Get-PageSurfaceRect($mainWindow, [System.Windows.Rect]$viewerRect, [int]$pageIndex = 0) {
    $pageControl = Find-DescendantByAutomationId $mainWindow (Get-EditorPageAutomationId $pageIndex)
    if ($null -ne $pageControl) {
        $pageControlRect = $pageControl.Current.BoundingRectangle
        if (-not $pageControl.Current.IsOffscreen -and
            $pageControlRect.Width -gt 250 -and $pageControlRect.Height -gt 250) {
            return $pageControlRect
        }
    }

    $imageCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Image)
    $images = @($mainWindow.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $imageCondition))
    $candidate = $images |
        Where-Object {
            $rect = $_.Current.BoundingRectangle
            -not $_.Current.IsOffscreen -and
                $rect.Width -gt 250 -and $rect.Height -gt 250 -and
                $rect.Left -ge ($viewerRect.Left - 8) -and
                $rect.Top -ge ($viewerRect.Top - 8) -and
                $rect.Right -le ($viewerRect.Right + 8) -and
                $rect.Bottom -le ($viewerRect.Bottom + 8)
        } |
        Sort-Object { $_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height } -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "RENDERED_PAGE_SURFACE_NOT_FOUND viewerRect=$viewerRect"
    }
    return $candidate.Current.BoundingRectangle
}

function Get-ColorDistance([System.Drawing.Color]$left, [System.Drawing.Color]$right) {
    return [Math]::Abs($left.R - $right.R) +
        [Math]::Abs($left.G - $right.G) +
        [Math]::Abs($left.B - $right.B)
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

function Save-IsolatedDocument([int]$processId, [IntPtr]$windowHandle, [string]$label) {
    $beforeHash = (Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash
    $saveButton = Wait-Until {
        $candidate = Find-DescendantByAutomationId (Find-MainWindow $processId) $EditorAutomationIds.Save
        if ($null -ne $candidate -and $candidate.Current.IsEnabled) { return $candidate }
        return $null
    } 10
    if ($null -eq $saveButton) {
        Write-ErrorDialogSnapshot $processId
        throw "SavePdfButton was not enabled for '$label'."
    }
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
    if ($null -eq $afterHash) {
        Write-ErrorDialogSnapshot $processId
        throw "The isolated PDF did not change after '$label'."
    }
    Write-Output "PDF_SAVE_COMPLETED label='$label' hashChanged=True before=$beforeHash after=$afterHash"
    return $afterHash
}

try {
    if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        throw 'SystemRoot must be available for the isolated WPF smoke test.'
    }

    try {
        $clipboardSnapshot = [System.Windows.Clipboard]::GetDataObject()
        $clipboardSnapshotCaptured = $true
    }
    catch {
        $clipboardSnapshot = $null
        Write-Warning "Could not capture the pre-smoke clipboard: $($_.Exception.Message)"
    }

    New-Item -ItemType Directory -Path $temporaryEnvironmentPath -Force | Out-Null
    New-AdvancedPdf $pdfPath
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

    $hwnd = [IntPtr]$mainWindow.Current.NativeWindowHandle
    Ensure-ForegroundWindow $hwnd 'editor-startup'
    Start-Sleep -Milliseconds 300

    $fileTile = Wait-Until { Find-FilePdfTile (Find-MainWindow $process.Id) } 15
    if ($null -eq $fileTile) { throw 'The pre-seeded PDF library tile was not found.' }
    $tileRect = $fileTile.Current.BoundingRectangle
    $tileMode = Send-PointerClick $hwnd `
        ([int][Math]::Round($tileRect.Left + ($tileRect.Width * 0.5))) `
        ([int][Math]::Round($tileRect.Top + ($tileRect.Height * 0.5)))
    Assert-ScreenInput $tileMode 'open-library-tile'
    Write-Output "OPEN_REQUESTED via=screen-pointer mode='$tileMode' rect=$tileRect"

    $textTool = Wait-Until {
        Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.Text
    } 60
    if ($null -eq $textTool) { throw 'Text tool was not found after opening the PDF.' }
    $viewer = Wait-Until {
        Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.PdfScrollViewer
    } 15
    if ($null -eq $viewer) { throw 'PdfScrollViewer was not found.' }
    $viewerRect = $viewer.Current.BoundingRectangle
    if ($viewerRect.Width -lt 300 -or $viewerRect.Height -lt 200) {
        throw "PdfScrollViewer has unusable bounds: $viewerRect"
    }
    $pageRect = Get-PageSurfaceRect (Find-MainWindow $process.Id) $viewerRect
    Write-Output "EDITOR_SURFACE_READY viewerRect=$viewerRect pageRect=$pageRect"

    # Shape: toolbar pointer -> real screen drag -> PDF-owned /Ink marker.
    $shapeMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.Shape
    Assert-ScreenInput $shapeMode 'activate-shape'
    $shapeState = Wait-ToggleState $process.Id $EditorAutomationIds.Shape 'On' 5
    if ($null -eq $shapeState) {
        Write-Output "SHAPE_TOOL_STATE_AFTER_POINTER='$((Get-ToggleState $process.Id $EditorAutomationIds.Shape))'"
        $shapeMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.Shape
        Assert-ScreenInput $shapeMode 'activate-shape-retry'
        $shapeState = Wait-ToggleState $process.Id $EditorAutomationIds.Shape 'On' 5
    }
    if ($null -eq $shapeState) {
        throw 'ShapeToolButton did not become active after bounded real-pointer retries.'
    }
    $shapeStartX = [int][Math]::Round($pageRect.Left + ($pageRect.Width * 0.25))
    $shapeStartY = [int][Math]::Round($pageRect.Top + ($pageRect.Height * 0.30))
    $shapeEndX = [int][Math]::Round($pageRect.Left + ($pageRect.Width * 0.50))
    $shapeEndY = [int][Math]::Round($pageRect.Top + ($pageRect.Height * 0.40))
    $shapeProbe = Get-ScreenPixel $shapeStartX $shapeStartY
Write-Output "SHAPE_SCREEN_GEOMETRY start=$shapeStartX,$shapeStartY end=$shapeEndX,$shapeEndY rgb=$($shapeProbe.R),$($shapeProbe.G),$($shapeProbe.B) state=$((Get-ToggleState $process.Id $EditorAutomationIds.Shape))"
    # The shape button owns an options popup. A first page click may only close
    # that popup, so dismiss it with a harmless tap before the real drag.
    $shapeDismissMode = Send-PointerClick $hwnd $shapeStartX $shapeStartY
    Assert-ScreenInput $shapeDismissMode 'dismiss-shape-popup'
    Start-Sleep -Milliseconds 250
Write-Output "SHAPE_TOOL_STATE_AFTER_DISMISS='$((Get-ToggleState $process.Id $EditorAutomationIds.Shape))'"
    $shapeDragMode = Send-PointerDrag $hwnd $shapeStartX $shapeStartY $shapeEndX $shapeEndY
    Assert-ScreenInput $shapeDragMode 'shape-drag'
    Start-Sleep -Milliseconds 500
    $normalInkBeforeShape = Get-PdfTokenCount $pdfPath 'wna_ink_'
    Save-IsolatedDocument $process.Id $hwnd 'shape-drag'
    $normalInkAfterShape = Get-PdfTokenCount $pdfPath 'wna_ink_'
    if ($normalInkAfterShape -le $normalInkBeforeShape) {
        throw "Shape drag did not persist a normal owned ink annotation before=$normalInkBeforeShape after=$normalInkAfterShape"
    }
    Write-Output "SHAPE_DRAG_COMPLETED mode='$shapeDragMode' normalInkBefore=$normalInkBeforeShape normalInkAfter=$normalInkAfterShape"

    # Shape undo/redo are observed through the saved PDF, not private state.
    $undoMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.Undo
    Assert-ScreenInput $undoMode 'shape-undo'
    Start-Sleep -Milliseconds 500
    Save-IsolatedDocument $process.Id $hwnd 'shape-undo'
    $normalInkAfterShapeUndo = Get-PdfTokenCount $pdfPath 'wna_ink_'
    if ($normalInkAfterShapeUndo -ne $normalInkBeforeShape) {
        throw "Shape undo did not remove the owned ink annotation expected=$normalInkBeforeShape actual=$normalInkAfterShapeUndo"
    }

    $redoMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.Redo
    Assert-ScreenInput $redoMode 'shape-redo'
    Start-Sleep -Milliseconds 500
    Save-IsolatedDocument $process.Id $hwnd 'shape-redo'
    $normalInkAfterShapeRedo = Get-PdfTokenCount $pdfPath 'wna_ink_'
    if ($normalInkAfterShapeRedo -le $normalInkBeforeShape) {
        throw "Shape redo did not restore the owned ink annotation actual=$normalInkAfterShapeRedo"
    }
    Write-Output "SHAPE_UNDO_REDO_COMPLETED undoRestored=$normalInkAfterShapeUndo redoRestored=$normalInkAfterShapeRedo"
    Write-Output 'ADVANCED_SHAPE_RESULT=PASS'

    # Hidden Ink: use a black page and sample the real screen before/after the
    # opaque mask is clicked. The marker count separately proves persistence.
    $hiddenMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.HiddenInk
    Assert-ScreenInput $hiddenMode 'activate-hidden-ink'
    if ($null -eq (Wait-ToggleState $process.Id $EditorAutomationIds.HiddenInk 'On' 5)) {
        throw "Hidden Ink did not become active state='$((Get-ToggleState $process.Id $EditorAutomationIds.HiddenInk))'."
    }
    $hiddenStartX = [int][Math]::Round($pageRect.Left + ($pageRect.Width * 0.38))
    $hiddenStartY = [int][Math]::Round($pageRect.Top + ($pageRect.Height * 0.56))
    $hiddenEndX = [int][Math]::Round($pageRect.Left + ($pageRect.Width * 0.62))
    $hiddenEndY = $hiddenStartY
    $hiddenMidX = [int][Math]::Round(($hiddenStartX + $hiddenEndX) / 2)
    $hiddenMidY = $hiddenStartY
    $basePixel = Get-ScreenPixel $hiddenMidX $hiddenMidY
    if (($basePixel.R + $basePixel.G + $basePixel.B) -gt 240) {
        throw "Hidden Ink pixel probe was not on the dark PDF page at $hiddenMidX,$hiddenMidY rgb=$($basePixel.R),$($basePixel.G),$($basePixel.B)"
    }
    $hiddenDragMode = Send-PointerDrag $hwnd $hiddenStartX $hiddenStartY $hiddenEndX $hiddenEndY
    Assert-ScreenInput $hiddenDragMode 'hidden-ink-drag'
    Start-Sleep -Milliseconds 500
    $maskedPixel = Get-ScreenPixel $hiddenMidX $hiddenMidY
    $maskDistance = Get-ColorDistance $maskedPixel $basePixel
    if ($maskDistance -lt 180) {
        throw "Hidden Ink mask was not visible at $hiddenMidX,$hiddenMidY rgb=$($maskedPixel.R),$($maskedPixel.G),$($maskedPixel.B)"
    }
    $hiddenBeforeSave = Get-PdfTokenCount $pdfPath 'wna_hidden_'
    Save-IsolatedDocument $process.Id $hwnd 'hidden-ink-drag'
    $hiddenAfterSave = Get-PdfTokenCount $pdfPath 'wna_hidden_'
    if ($hiddenAfterSave -le $hiddenBeforeSave) {
        throw "Hidden Ink did not persist its wna_hidden_ marker before=$hiddenBeforeSave after=$hiddenAfterSave"
    }
    $revealMode = Send-PointerClick $hwnd $hiddenMidX $hiddenMidY
    Assert-ScreenInput $revealMode 'hidden-ink-reveal-click'
    Start-Sleep -Milliseconds 700
    $revealedPixel = Get-ScreenPixel $hiddenMidX $hiddenMidY
    $revealDistance = Get-ColorDistance $revealedPixel $basePixel
    if ($revealDistance -ge [Math]::Max(60, $maskDistance * 0.45)) {
        throw "Hidden Ink reveal did not approach the base page pixel at $hiddenMidX,$hiddenMidY baseRgb=$($basePixel.R),$($basePixel.G),$($basePixel.B) maskedRgb=$($maskedPixel.R),$($maskedPixel.G),$($maskedPixel.B) revealedRgb=$($revealedPixel.R),$($revealedPixel.G),$($revealedPixel.B)"
    }
    Start-Sleep -Milliseconds 3400
    $restoredMaskPixel = Get-ScreenPixel $hiddenMidX $hiddenMidY
    $restoreDistance = Get-ColorDistance $restoredMaskPixel $maskedPixel
    if ($restoreDistance -ge [Math]::Max(80, $maskDistance * 0.55)) {
        throw "Hidden Ink reveal timer did not restore the mask rgb=$($restoredMaskPixel.R),$($restoredMaskPixel.G),$($restoredMaskPixel.B) maskRgb=$($maskedPixel.R),$($maskedPixel.G),$($maskedPixel.B)"
    }
    Write-Output "HIDDEN_INK_COMPLETED drawMode='$hiddenDragMode' revealMode='$revealMode' markerBefore=$hiddenBeforeSave markerAfter=$hiddenAfterSave baseRgb=$($basePixel.R),$($basePixel.G),$($basePixel.B) maskedRgb=$($maskedPixel.R),$($maskedPixel.G),$($maskedPixel.B) revealedRgb=$($revealedPixel.R),$($revealedPixel.G),$($revealedPixel.B) restoredRgb=$($restoredMaskPixel.R),$($restoredMaskPixel.G),$($restoredMaskPixel.B) maskDistance=$maskDistance revealDistance=$revealDistance restoreDistance=$restoreDistance"
    Write-Output 'ADVANCED_HIDDEN_INK_RESULT=PASS'

    # Selection/copy/paste: real marquee, OS keyboard Ctrl+C, clipboard JSON,
    # real blank-page click anchor, OS keyboard Ctrl+V, then saved PDF count.
    $selectMode = Invoke-ToolbarPointerClick $process.Id $hwnd $EditorAutomationIds.Select
    Assert-ScreenInput $selectMode 'activate-select'
    if ($null -eq (Wait-ToggleState $process.Id $EditorAutomationIds.Select 'On' 5)) {
        throw "Select tool did not become active state='$((Get-ToggleState $process.Id $EditorAutomationIds.Select))'."
    }
    $selectionStartX = [int][Math]::Round($shapeStartX - 40)
    $selectionStartY = [int][Math]::Round($shapeStartY - 40)
    $selectionEndX = [int][Math]::Round($shapeEndX + 40)
    $selectionEndY = [int][Math]::Round($shapeEndY + 40)
    $selectionDismissMode = Send-PointerClick $hwnd $selectionStartX $selectionStartY
    Assert-ScreenInput $selectionDismissMode 'dismiss-selection-popup'
    Start-Sleep -Milliseconds 250
    $selectionDragMode = Send-PointerDrag $hwnd $selectionStartX $selectionStartY $selectionEndX $selectionEndY
    Assert-ScreenInput $selectionDragMode 'selection-marquee'
    Start-Sleep -Milliseconds 700
    try {
        [System.Windows.Clipboard]::Clear()
    }
    catch {
        throw "Clipboard clear failed before Ctrl+C: $($_.Exception.Message)"
    }
    $copyKeyboardMode = Send-KeyChord 0x43
    Write-Output "SELECTION_COPY_KEYBOARD mode='$copyKeyboardMode'"
    $clipboardText = Wait-Until {
        try {
            if ([System.Windows.Clipboard]::ContainsText()) {
                $candidateText = [System.Windows.Clipboard]::GetText()
                if (-not [string]::IsNullOrWhiteSpace($candidateText)) {
                    return $candidateText
                }
            }
        }
        catch { return $null }
        return $null
    } 5
    if ([string]::IsNullOrWhiteSpace($clipboardText)) {
        throw 'Ctrl+C did not place annotation JSON on the clipboard.'
    }
    try {
        $clipboardJson = $clipboardText | ConvertFrom-Json
        $copiedStrokes = @($clipboardJson.Pages.'0'.Strokes)
    }
    catch {
        throw "Clipboard content after Ctrl+C was not OpenNotes annotation JSON: $($_.Exception.Message)"
    }
    if ($copiedStrokes.Count -lt 1) {
        throw 'Selection copy JSON did not contain the shape stroke.'
    }
    $lineShape = @($copiedStrokes | Where-Object { @($_.Points).Count -eq 2 }) | Select-Object -First 1
    if ($null -eq $lineShape) {
        throw "Selection copy JSON did not contain the expected two-point line shape copiedStrokes=$($copiedStrokes.Count)"
    }
    $linePoints = @($lineShape.Points)
    $lineWidth = [Math]::Abs([double]$linePoints[1][0] - [double]$linePoints[0][0])
    $lineHeight = [Math]::Abs([double]$linePoints[1][1] - [double]$linePoints[0][1])
    if ($lineWidth -lt 20 -and $lineHeight -lt 20) {
        throw "Selection copy JSON line shape geometry was degenerate width=$lineWidth height=$lineHeight"
    }
    Write-Output "SELECTION_COPY_COMPLETED clipboardJson=True copiedStrokes=$($copiedStrokes.Count) linePoints=$($linePoints.Count) lineWidth=$lineWidth lineHeight=$lineHeight"

    $pasteX = [int][Math]::Round($pageRect.Left + ($pageRect.Width * 0.72))
    $pasteY = [int][Math]::Round($pageRect.Top + ($pageRect.Height * 0.72))
    $anchorMode = Send-PointerClick $hwnd $pasteX $pasteY
    Assert-ScreenInput $anchorMode 'paste-anchor'
    Start-Sleep -Milliseconds 250
    $pasteKeyboardMode = Send-KeyChord 0x56
    Write-Output "SELECTION_PASTE_KEYBOARD mode='$pasteKeyboardMode' anchor=$pasteX,$pasteY"
    Start-Sleep -Milliseconds 700
    $normalInkBeforePaste = Get-PdfTokenCount $pdfPath 'wna_ink_'
    Save-IsolatedDocument $process.Id $hwnd 'selection-copy-paste'
    $normalInkAfterPaste = Get-PdfTokenCount $pdfPath 'wna_ink_'
    if ($normalInkAfterPaste -ne ($normalInkBeforePaste + 1)) {
        throw "Ctrl+V did not persist a copied annotation before=$normalInkBeforePaste after=$normalInkAfterPaste"
    }
    Write-Output "SELECTION_COPY_PASTE_COMPLETED selectionMode='$selectionDragMode' normalInkBefore=$normalInkBeforePaste normalInkAfter=$normalInkAfterPaste"
    Write-Output 'ADVANCED_SELECTION_CLIPBOARD_RESULT=PASS'
    Write-Output 'ADVANCED_POINTER_SMOKE_RESULT=PASS'
}
catch {
    Write-Output 'ADVANCED_POINTER_SMOKE_RESULT=FAIL'
    throw
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
    if ($clipboardSnapshotCaptured -and $null -ne $clipboardSnapshot) {
        try {
            [System.Windows.Clipboard]::SetDataObject($clipboardSnapshot, $true)
            Write-Output 'CLIPBOARD_RESTORED=True'
        }
        catch {
            Write-Warning "Failed to restore the pre-smoke clipboard: $($_.Exception.Message)"
        }
    }
    elseif (-not $clipboardSnapshotCaptured) {
        Write-Output 'CLIPBOARD_RESTORED=False reason=capture-failed'
    }
    if (Test-Path -LiteralPath $temporaryEnvironmentPath) {
        try {
            Remove-Item -LiteralPath $temporaryEnvironmentPath -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Failed to remove the exact advanced smoke temporary directory: $($_.Exception.Message)"
        }
    }
    Write-Output "ISOLATED_ENV_CLEANED=$(-not (Test-Path -LiteralPath $temporaryEnvironmentPath))"
}
