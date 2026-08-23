[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [int]$StartupTimeoutSeconds = 15,
    [switch]$SaveAndReopen
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $PSScriptRoot '..\bin\Debug\net8.0-windows\win-x64\OpenNotes.exe'
}
$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$temporaryEnvironmentPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'OpenNotesUiAutomation_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryEnvironmentPath | Out-Null
$process = $null

function Get-ProcessWindows([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $condition = [System.Windows.Automation.AndCondition]::new(
        $processCondition, $windowCondition)
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Subtree, $condition))
}

function Find-DescendantByAutomationId($element, [string]$automationId) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-UiAutomationElement($element) {
    if ($null -eq $element) {
        throw 'The requested UI Automation element was not found.'
    }

    $pattern = $element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    [void]$pattern.Invoke()
}

function Find-MainWindow([int]$processId) {
    return Get-ProcessWindows $processId |
        Where-Object { $_.Current.Name -eq 'OpenNotes' } |
        Select-Object -First 1
}

function Find-SettingsWindow([int]$processId) {
    foreach ($candidate in (Get-ProcessWindows $processId)) {
        if ($candidate.Current.ClassName -ne 'Window' -or
            $candidate.Current.Name -eq 'OpenNotes') {
            continue
        }

        if ($null -ne (Find-DescendantByAutomationId $candidate 'LanguageComboBox')) {
            return $candidate
        }
    }

    return $null
}

function Select-ListItem([int]$processId, [string]$namePattern) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $items = @($root.FindAll([System.Windows.Automation.TreeScope]::Subtree, $itemCondition) |
        Where-Object {
            $_.Current.ProcessId -eq $processId -and
            $_.Current.Name -match $namePattern
        })

    foreach ($item in $items) {
        $supportedPatterns = @($item.GetSupportedPatterns() |
            ForEach-Object { $_.ProgrammaticName })
        if ($supportedPatterns -contains 'SelectionItemPatternIdentifiers.Pattern') {
            $selection = $item.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern)
            [void]$selection.Select()
            return $item
        }
    }

    return $null
}

function Wait-Until([scriptblock]$condition, [int]$timeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $result = & $condition
        if ($null -ne $result) {
            return $result
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    return $null
}

function Get-SelectedComboName($comboBox) {
    if ($null -eq $comboBox) { return $null }

    try {
        $selection = $comboBox.GetCurrentPattern(
            [System.Windows.Automation.SelectionPattern]::Pattern)
        $items = @($selection.Current.GetSelection())
        if ($items.Count -gt 0) {
            return $items[0].Current.Name
        }
    }
    catch { }

    return $comboBox.Current.Name
}

function Close-OpenNotesProcess {
    param([System.Diagnostics.Process]$TargetProcess)

    if ($null -eq $TargetProcess) { return }
    if (-not $TargetProcess.HasExited) {
        [void]$TargetProcess.CloseMainWindow()
        if (-not $TargetProcess.WaitForExit(3000)) {
            $TargetProcess.Kill()
            [void]$TargetProcess.WaitForExit()
        }
    }
    $TargetProcess.Dispose()
}

try {
    if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        throw 'SystemRoot must be available for the isolated WPF smoke test.'
    }

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

    $moreButton = Find-DescendantByAutomationId $mainWindow 'MoreButton'
    Invoke-UiAutomationElement $moreButton
    Start-Sleep -Milliseconds 400

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $menuItemCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)
    $settingsMenuItem = @($root.FindAll(
        [System.Windows.Automation.TreeScope]::Subtree, $menuItemCondition) |
        Where-Object {
            $_.Current.ProcessId -eq $process.Id -and
            $_.Current.AutomationId -eq 'SettingsMenuItem'
        }) | Select-Object -First 1
    Invoke-UiAutomationElement $settingsMenuItem

    $settingsWindow = Wait-Until { Find-SettingsWindow $process.Id } $StartupTimeoutSeconds
    if ($null -eq $settingsWindow) {
        throw 'The settings window was not found through UI Automation.'
    }
    Write-Output "SETTINGS_BEFORE title='$($settingsWindow.Current.Name)'"

    $languageComboBox = Find-DescendantByAutomationId $settingsWindow 'LanguageComboBox'
    $languageExpandCollapse = $languageComboBox.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $languageExpandCollapse.Expand()
    Start-Sleep -Milliseconds 250
    # Keep the primary selector ASCII so Windows PowerShell can read this UTF-8 script
    # even when it falls back to its legacy source encoding.
    $languageOption = Select-ListItem $process.Id 'Fran|French|法语|法文'
    if ($null -eq $languageOption) {
        throw 'The French language option did not expose SelectionItemPattern.'
    }
    Write-Output "LANGUAGE_PREVIEW_SELECTED='$($languageOption.Current.Name)'"
    try { $languageExpandCollapse.Collapse() } catch { }

    $settingsAfterLanguage = Wait-Until {
        $candidate = Find-SettingsWindow $process.Id
        if ($null -ne $candidate -and
            (Find-DescendantByAutomationId $candidate 'CancelButton').Current.Name -match 'Annuler|Cancel|取消') {
            $candidate
        }
    } $StartupTimeoutSeconds
    if ($null -eq $settingsAfterLanguage) {
        throw 'The settings window did not refresh after the language preview.'
    }
    Write-Output "SETTINGS_AFTER_LANGUAGE title='$($settingsAfterLanguage.Current.Name)' cancel='$((Find-DescendantByAutomationId $settingsAfterLanguage 'CancelButton').Current.Name)'"

    $themeComboBox = Find-DescendantByAutomationId $settingsAfterLanguage 'ThemeComboBox'
    $themeExpandCollapse = $themeComboBox.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $themeExpandCollapse.Expand()
    Start-Sleep -Milliseconds 250
    $themeOption = Select-ListItem $process.Id 'Sombre|Dark|深色'
    if ($null -eq $themeOption) {
        throw 'The dark theme option did not expose SelectionItemPattern.'
    }
    Write-Output "THEME_PREVIEW_SELECTED='$($themeOption.Current.Name)'"
    try { $themeExpandCollapse.Collapse() } catch { }

    Start-Sleep -Milliseconds 700
    $settingsAfterTheme = Find-SettingsWindow $process.Id
    if ($null -eq $settingsAfterTheme) {
        throw 'The settings window was not available after the theme preview.'
    }

    if ($SaveAndReopen) {
        $saveButton = Find-DescendantByAutomationId $settingsAfterTheme 'SaveButton'
        if ($null -eq $saveButton -or -not $saveButton.Current.IsEnabled) {
            throw 'The settings Save button was not available for persistence smoke.'
        }
        Write-Output "SAVE_TARGET name='$($saveButton.Current.Name)' enabled=$($saveButton.Current.IsEnabled)"
        Invoke-UiAutomationElement $saveButton

        $settingsClosed = Wait-Until {
            if ($null -eq (Find-SettingsWindow $process.Id)) { return $true }
            return $null
        } 5
        if ($null -eq $settingsClosed) {
            throw 'The settings window did not close after Save.'
        }
        Write-Output 'SAVE_COMPLETED settingsClosed=True'

        Close-OpenNotesProcess $process
        $process = $null
        Write-Output 'MAIN_PROCESS_CLOSED_FOR_SETTINGS_REOPEN=True'

        $process = [System.Diagnostics.Process]::Start($startInfo)
        $reopenedMainWindow = Wait-Until {
            if ($process.HasExited) {
                throw "OpenNotes exited during settings persistence reopen with code $($process.ExitCode)."
            }
            Find-MainWindow $process.Id
        } $StartupTimeoutSeconds
        if ($null -eq $reopenedMainWindow) {
            throw 'The OpenNotes main window was not found after settings persistence reopen.'
        }

        $reopenedMoreButton = Find-DescendantByAutomationId $reopenedMainWindow 'MoreButton'
        Invoke-UiAutomationElement $reopenedMoreButton
        Start-Sleep -Milliseconds 400
        $reopenedSettingsMenuItem = @($root.FindAll(
            [System.Windows.Automation.TreeScope]::Subtree, $menuItemCondition) |
            Where-Object {
                $_.Current.ProcessId -eq $process.Id -and
                $_.Current.AutomationId -eq 'SettingsMenuItem'
            }) | Select-Object -First 1
        Invoke-UiAutomationElement $reopenedSettingsMenuItem

        $reopenedSettingsWindow = Wait-Until {
            Find-SettingsWindow $process.Id
        } $StartupTimeoutSeconds
        if ($null -eq $reopenedSettingsWindow) {
            throw 'The settings window was not found after the settings persistence reopen.'
        }

        $reopenedLanguage = Get-SelectedComboName (
            Find-DescendantByAutomationId $reopenedSettingsWindow 'LanguageComboBox')
        $reopenedTheme = Get-SelectedComboName (
            Find-DescendantByAutomationId $reopenedSettingsWindow 'ThemeComboBox')
        Write-Output "PERSISTED_LANGUAGE='$reopenedLanguage'"
        Write-Output "PERSISTED_THEME='$reopenedTheme'"
        if ($reopenedLanguage -notmatch 'Fran|French|法语|法文') {
            throw "French language selection was not persisted. selected='$reopenedLanguage'"
        }
        if ($reopenedTheme -notmatch 'Sombre|Dark|深色') {
            throw "Dark theme selection was not persisted. selected='$reopenedTheme'"
        }

        $reopenedCancelButton = Find-DescendantByAutomationId $reopenedSettingsWindow 'CancelButton'
        Invoke-UiAutomationElement $reopenedCancelButton
        $remainingReopenedSettings = Wait-Until {
            if ($null -eq (Find-SettingsWindow $process.Id)) { return $true }
            return $null
        } 5
        if ($null -eq $remainingReopenedSettings) {
            throw 'The reopened settings window did not close after Cancel.'
        }
        Write-Output 'PERSISTENCE_REOPEN_COMPLETED settingsClosed=True'
        Write-Output 'UI_AUTOMATION_PERSISTENCE_RESULT=PASS'
        return
    }

    $cancelButton = Find-DescendantByAutomationId $settingsAfterTheme 'CancelButton'
    Write-Output "CANCEL_TARGET name='$($cancelButton.Current.Name)' enabled=$($cancelButton.Current.IsEnabled) offscreen=$($cancelButton.Current.IsOffscreen) hwnd=$($cancelButton.Current.NativeWindowHandle)"
    Invoke-UiAutomationElement $cancelButton
    $remainingSettings = Find-SettingsWindow $process.Id
    $closeDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while ($null -ne $remainingSettings -and [DateTime]::UtcNow -lt $closeDeadline) {
        Start-Sleep -Milliseconds 250
        $remainingSettings = Find-SettingsWindow $process.Id
    }
    if ($null -ne $remainingSettings) {
        foreach ($window in (Get-ProcessWindows $process.Id)) {
            Write-Output "WINDOW_AFTER_CANCEL name='$($window.Current.Name)' class='$($window.Current.ClassName)' offscreen=$($window.Current.IsOffscreen)"
        }
        throw 'The settings window did not close after Cancel.'
    }
    Write-Output 'CANCEL_COMPLETED remainingSettings=0'
    Write-Output 'UI_AUTOMATION_RESULT=PASS'
}
finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                [void]$process.CloseMainWindow()
                if (-not $process.WaitForExit(3000)) {
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
}
