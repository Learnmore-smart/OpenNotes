[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$PdfPath,
    [int]$StartupTimeoutSeconds = 20,
    [int]$EditorTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'OpenNotesEditorAutomationIds.ps1')

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $PSScriptRoot '..\bin\Debug\net8.0-windows\win-x64\OpenNotes.exe'
}
$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([string]::IsNullOrWhiteSpace($PdfPath)) {
    throw 'Pass -PdfPath explicitly so the smoke script does not scan a broad user directory.'
}
$resolvedPdfPath = (Resolve-Path -LiteralPath $PdfPath).Path

if (-not ('OpenNotes.NativeSmokeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class NativeSmokeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr hDlg, int controlId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetWindowText(IntPtr hWnd, string text);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
'@
}

$temporaryEnvironmentPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'OpenNotesEditorSmoke_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryEnvironmentPath | Out-Null
$process = $null
$sidecarRoot = Join-Path $temporaryEnvironmentPath 'Caelum'

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

function Find-MainWindow([int]$processId) {
    return Get-ProcessWindows $processId |
        Where-Object { $_.Current.Name -eq 'OpenNotes' } |
        Select-Object -First 1
}

function Find-AddPdfTile($mainWindow) {
    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $buttons = @($mainWindow.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $buttonCondition))
    return $buttons |
        Where-Object {
            $rect = $_.Current.BoundingRectangle
            [string]::IsNullOrWhiteSpace($_.Current.Name) -and
                $rect.Width -gt 200 -and $rect.Height -gt 200
        } |
        Select-Object -First 1
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

function Find-FirstProcessMenuItem([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $menuCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Subtree, $menuCondition) |
        Where-Object { $_.Current.ProcessId -eq $processId }) |
        Select-Object -First 1
}

function Get-NativeDialogHandle([int]$processId) {
    $handles = New-Object 'System.Collections.Generic.List[System.IntPtr]'
    $callback = [NativeSmokeMethods+EnumWindowsProc] {
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        [uint32]$windowProcessId = 0
        [void][NativeSmokeMethods]::GetWindowThreadProcessId($hWnd, [ref]$windowProcessId)
        if ($windowProcessId -eq [uint32]$lParam.ToInt32()) {
            $className = New-Object System.Text.StringBuilder 128
            [void][NativeSmokeMethods]::GetClassName($hWnd, $className, $className.Capacity)
            if ($className.ToString() -eq '#32770') {
                [void]$handles.Add($hWnd)
            }
        }
        return $true
    }
    [void][NativeSmokeMethods]::EnumWindows($callback, [IntPtr]$processId)
    return $handles | Select-Object -First 1
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

function Write-UiAutomationSnapshot([int]$processId) {
    foreach ($window in (Get-ProcessWindows $processId)) {
        Write-Output "WINDOW_SNAPSHOT name='$($window.Current.Name)' class='$($window.Current.ClassName)' automationId='$($window.Current.AutomationId)'"
        $all = @($window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition))
        $shown = 0
        foreach ($element in $all) {
            if ($shown -ge 120) { break }
            $id = $element.Current.AutomationId
            $name = $element.Current.Name
            if (-not [string]::IsNullOrWhiteSpace($id) -or -not [string]::IsNullOrWhiteSpace($name)) {
                Write-Output "CONTROL_SNAPSHOT type='$($element.Current.ControlType.ProgrammaticName)' id='$id' name='$name'"
                $shown++
            }
        }
    }
}

function Invoke-ToolAndReport([int]$processId, [string]$automationId) {
    $mainWindow = Find-MainWindow $processId
    $control = Find-DescendantByAutomationId $mainWindow $automationId
    if ($null -eq $control) { throw "Tool control was not found: $automationId" }

    $toggled = $false
    try {
        $togglePattern = $control.GetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern)
        $togglePattern.Toggle()
        $toggled = $true
    }
    catch { }
    if (-not $toggled) {
        Invoke-UiAutomationElement $control
    }
    Start-Sleep -Milliseconds 120

    $state = 'not-toggle'
    try {
        $refreshedControl = Find-DescendantByAutomationId (Find-MainWindow $processId) $automationId
        $togglePattern = $refreshedControl.GetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern)
        $state = $togglePattern.Current.ToggleState.ToString()
    }
    catch { }
    Write-Output "TOOL_INVOKED id='$automationId' toggleState='$state'"
}

function Invoke-EditorCommandAndReport([int]$processId, [string]$automationId) {
    $mainWindow = Find-MainWindow $processId
    $control = Find-DescendantByAutomationId $mainWindow $automationId
    if ($null -eq $control) { throw "Editor command control was not found: $automationId" }
    Invoke-UiAutomationElement $control
    Start-Sleep -Milliseconds 120
    Write-Output "EDITOR_COMMAND_INVOKED id='$automationId'"
}

try {
    if (-not (Test-Path -LiteralPath $resolvedPdfPath -PathType Leaf)) {
        throw "PDF was not found: $resolvedPdfPath"
    }
    if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        throw 'SystemRoot must be available for the isolated WPF smoke test.'
    }

    $nowUtc = [DateTime]::UtcNow
    $recentEntry = [ordered]@{
        Id = ([guid]::NewGuid().ToString('N'))
        EntryType = 'file'
        ParentFolderId = ''
        DisplayName = [System.IO.Path]::GetFileName($resolvedPdfPath)
        IsNotebook = $false
        Path = $resolvedPdfPath
        PageCount = 0
        LastModifiedUtc = [System.IO.File]::GetLastWriteTimeUtc($resolvedPdfPath).ToString('o')
        LastOpenedUtc = $nowUtc.ToString('o')
    }
    New-Item -ItemType Directory -Path $sidecarRoot -Force | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding -ArgumentList $false
    $recentJson = '[' + ($recentEntry | ConvertTo-Json -Depth 4) + ']'
    [System.IO.File]::WriteAllText((Join-Path $sidecarRoot 'recent_files.json'), $recentJson, $utf8)
    Write-Output "PRESEEDED_ENTRY path='$resolvedPdfPath' exists=$([System.IO.File]::Exists($resolvedPdfPath))"

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
        if ($process.HasExited) {
            throw "OpenNotes exited during startup with code $($process.ExitCode)."
        }
        Find-MainWindow $process.Id
    } $StartupTimeoutSeconds
    if ($null -eq $mainWindow) {
        throw "OpenNotes main window was not found within $StartupTimeoutSeconds seconds."
    }
    Write-Output "MAIN_WINDOW name='$($mainWindow.Current.Name)' pid=$($process.Id)"

    $fileTile = Wait-Until {
        if ($process.HasExited) {
            throw "OpenNotes exited before the PDF library tile was available with code $($process.ExitCode)."
        }
        Find-FilePdfTile (Find-MainWindow $process.Id)
    } 15
    if ($null -eq $fileTile) { throw 'The pre-seeded PDF library tile was not found through UI Automation.' }
    Write-Output "FILE_TILE rect=$($fileTile.Current.BoundingRectangle)"
    Invoke-UiAutomationElement $fileTile
    Write-Output "OPEN_REQUESTED pdf='$resolvedPdfPath' via=library-tile"

    $editorTool = Wait-Until {
        if ($process.HasExited) {
            throw "OpenNotes exited while loading the PDF with code $($process.ExitCode)."
        }
        $currentMain = Find-MainWindow $process.Id
        Find-DescendantByAutomationId $currentMain $EditorAutomationIds.Text
    } $EditorTimeoutSeconds
    if ($null -eq $editorTool) {
        Write-UiAutomationSnapshot $process.Id
        throw 'The editor Text tool was not exposed after opening the PDF.'
    }
    Write-Output "EDITOR_TOOL_FOUND id='$($editorTool.Current.AutomationId)' name='$($editorTool.Current.Name)'"

    $requiredAutomationIds = @(
        $EditorAutomationIds.Undo, $EditorAutomationIds.Redo, $EditorAutomationIds.Pen, $EditorAutomationIds.Highlighter,
        $EditorAutomationIds.HiddenInk, $EditorAutomationIds.Sticky, $EditorAutomationIds.Eraser, $EditorAutomationIds.Shape,
        $EditorAutomationIds.Laser, $EditorAutomationIds.Ruler, $EditorAutomationIds.Select, $EditorAutomationIds.Text,
        $EditorAutomationIds.Save, $EditorAutomationIds.PageJump, $EditorAutomationIds.SidebarPages,
        $EditorAutomationIds.SidebarOutline, $EditorAutomationIds.SidebarBookmarks, $EditorAutomationIds.SidebarCollapse,
        $EditorAutomationIds.PdfScrollViewer,
        "$($EditorAutomationIds.SidebarPagePrefix)1"
    )
    # Keep this list explicit. A control belongs here only when the production
    # EditorPage contract promises it on every loaded PDF; optional future
    # surfaces must be listed separately and may not hide a required omission.
    $optionalAutomationIds = @()

    foreach ($automationId in $requiredAutomationIds) {
        $control = Find-DescendantByAutomationId (Find-MainWindow $process.Id) $automationId
        if ($null -eq $control) {
            Write-Output "EDITOR_CONTROL_MISSING id='$automationId'"
            throw "Required editor control missing: $automationId"
        }
        Write-Output "EDITOR_CONTROL id='$automationId' enabled=$($control.Current.IsEnabled)"
    }

    $pageJumpControl = Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.PageJump
    try {
        $valuePattern = $pageJumpControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $pageValue = $valuePattern.Current.Value
        if ([string]::IsNullOrWhiteSpace($pageValue)) { throw 'The page jump ValuePattern returned an empty value.' }
        if ($pageValue -ne '1') { throw "The initial page jump ValuePattern must expose page 1 (value='$pageValue')." }
        if ([string]::IsNullOrWhiteSpace($pageJumpControl.Current.Name) -or
            [string]::IsNullOrWhiteSpace($pageJumpControl.Current.HelpText)) {
            throw 'The page jump UIA Name/HelpText metadata is empty.'
        }
        Write-Output "PAGE_JUMP_UIA value='$pageValue' name='$($pageJumpControl.Current.Name)'"

        # The isolated fixture used by Wave4 is multi-page.  Exercise the
        # actual ValuePattern commit path so a required field that only looks
        # discoverable cannot make the smoke pass.
        $valuePattern.SetValue('2')
        Start-Sleep -Milliseconds 250
        $committedPageValue = (Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.PageJump).GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($committedPageValue -notmatch '2') {
            throw "The page jump did not commit page 2 through ValuePattern (value='$committedPageValue')."
        }
        Write-Output "PAGE_JUMP_COMMITTED value='$committedPageValue'"
    }
    catch {
        throw "Page jump UIA contract failed: $($_.Exception.Message)"
    }

    foreach ($sidebarCommand in @(
        $EditorAutomationIds.SidebarPages, $EditorAutomationIds.SidebarBookmarks,
        $EditorAutomationIds.SidebarOutline)) {
        Invoke-EditorCommandAndReport $process.Id $sidebarCommand
    }

    $outlinePageTwoId = "$($EditorAutomationIds.SidebarOutlinePrefix)Page.2"
    $outlinePageTwo = Wait-Until {
        Find-DescendantByAutomationId (Find-MainWindow $process.Id) $outlinePageTwoId
    } 5
    if ($null -eq $outlinePageTwo) {
        throw "Required multi-page outline item missing: $outlinePageTwoId"
    }
    Write-Output "EDITOR_CONTROL id='$outlinePageTwoId' enabled=$($outlinePageTwo.Current.IsEnabled)"
    try {
        $supportedOutlinePatterns = @($outlinePageTwo.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName })
        Write-Output "OUTLINE_SELECTION_PATTERNS id='$outlinePageTwoId' patterns='$($supportedOutlinePatterns -join ',')' class='$($outlinePageTwo.Current.ClassName)' controlType='$($outlinePageTwo.Current.ControlType.ProgrammaticName)'"
        # Exercise Invoke before Selection so both patterns are checked on a
        # freshly realized fallback item.  Reset the page field first so the
        # Invoke result cannot be masked by the earlier PageJump commit.
        $pageJumpForOutline = Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.PageJump
        $pageJumpForOutline.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('1')
        Start-Sleep -Milliseconds 150

        $outlineInvokeId = "$outlinePageTwoId.Invoke"
        $outlineInvokeControl = Wait-Until {
            Find-DescendantByAutomationId (Find-MainWindow $process.Id) $outlineInvokeId
        } 5
        if ($null -eq $outlineInvokeControl) {
            throw "Required fallback outline Invoke button missing: $outlineInvokeId"
        }
        $outlineInvoke = $outlineInvokeControl.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)
        $outlineInvoke.Invoke()
        Start-Sleep -Milliseconds 150
        $invokePageValue = (Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.PageJump).GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($invokePageValue -ne '2') {
            throw "Outline page 2 InvokePattern did not reach page 2 (value='$invokePageValue')."
        }
        Write-Output "OUTLINE_INVOKE_INVOKED id='$outlineInvokeId' page='$invokePageValue'"

        $outlinePageTwo = Find-DescendantByAutomationId (Find-MainWindow $process.Id) $outlinePageTwoId
        $outlineSelection = $outlinePageTwo.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $outlineSelection.Select()
        Start-Sleep -Milliseconds 150
        $selectionPageValue = (Find-DescendantByAutomationId (Find-MainWindow $process.Id) $EditorAutomationIds.PageJump).GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($selectionPageValue -ne '2') {
            throw "Outline page 2 SelectionItemPattern did not reach page 2 (value='$selectionPageValue')."
        }
        Write-Output "OUTLINE_SELECTION_INVOKED id='$outlinePageTwoId' page='$selectionPageValue'"
    }
    catch {
        throw "Outline page 2 SelectionItem/InvokePattern failed: $($_.Exception.Message)"
    }
    Invoke-EditorCommandAndReport $process.Id $EditorAutomationIds.SidebarCollapse

    foreach ($automationId in $optionalAutomationIds) {
        $control = Find-DescendantByAutomationId (Find-MainWindow $process.Id) $automationId
        if ($null -eq $control) {
            Write-Output "OPTIONAL_CONTROL_MISSING id='$automationId'"
            continue
        }
        Write-Output "OPTIONAL_EDITOR_CONTROL id='$automationId' enabled=$($control.Current.IsEnabled)"
    }

    foreach ($automationId in @(
        $EditorAutomationIds.Pen, $EditorAutomationIds.Highlighter, $EditorAutomationIds.HiddenInk,
        $EditorAutomationIds.Eraser, $EditorAutomationIds.Shape, $EditorAutomationIds.Laser, $EditorAutomationIds.Ruler,
        $EditorAutomationIds.Select, $EditorAutomationIds.Text)) {
        Invoke-ToolAndReport $process.Id $automationId
    }

    $sidecarFiles = @()
    if (Test-Path -LiteralPath $sidecarRoot) {
        $sidecarFiles = @(Get-ChildItem -LiteralPath $sidecarRoot -Recurse -File | Select-Object -ExpandProperty FullName)
    }
    Write-Output "ISOLATED_SIDECAR_ROOT path='$sidecarRoot' exists=$(Test-Path -LiteralPath $sidecarRoot) files=$($sidecarFiles.Count)"
    Write-Output 'EDITOR_SMOKE_RESULT=PASS'
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
