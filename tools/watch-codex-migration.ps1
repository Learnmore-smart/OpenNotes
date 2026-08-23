[CmdletBinding()]
param(
    [int]$PollMilliseconds = 2000,
    [int]$StabilitySeconds = 5,
    [string]$LogPath = (Join-Path $env:TEMP 'opennotes-codex-migration-watch.log')
)

$ErrorActionPreference = 'Stop'
$processNames = @('ChatGPT', 'codex', 'codex-code-mode-host', 'Antigravity', 'Antigravity IDE')
$launcher = Join-Path $PSScriptRoot 'launch-codex-migration.ps1'

function Get-BlockingProcesses {
    @(
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -in $processNames -or $_.ProcessName -like 'codex-command-runner-*' }
    )
}

function Write-WatchLog([string]$Message) {
    $timestamp = [DateTimeOffset]::Now.ToString('o')
    Add-Content -LiteralPath $LogPath -Value "[$timestamp] $Message" -Encoding UTF8
}

if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw "Migration launcher was not found: $launcher"
}

New-Item -ItemType File -Path $LogPath -Force | Out-Null
Write-WatchLog "Watcher started. Waiting for: $($processNames -join ', ')"

while ($true) {
    $blocking = Get-BlockingProcesses
    if ($blocking.Count -eq 0) {
        Start-Sleep -Seconds $StabilitySeconds
        if ((Get-BlockingProcesses).Count -eq 0) { break }
    }
    Start-Sleep -Milliseconds $PollMilliseconds
}

Write-WatchLog 'All guarded processes stayed closed; starting the full migration launcher.'
$childOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher *>&1)
$childExitCode = $LASTEXITCODE
foreach ($line in $childOutput) {
    Add-Content -LiteralPath $LogPath -Value ([string]$line) -Encoding UTF8
}

if ($childExitCode -ne 0) {
    Write-WatchLog "Migration launcher failed with exit code $childExitCode. The launcher/main script owns rollback."
    exit $childExitCode
}

Write-WatchLog 'Migration launcher completed successfully.'
exit 0
