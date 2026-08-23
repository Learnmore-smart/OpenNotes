[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Migrate-AssistantConversations.ps1'
$resultPath = Join-Path $PSScriptRoot 'codex-migration-run.log'

"started $(Get-Date -Format o)" | Set-Content -LiteralPath $resultPath -Encoding UTF8
try {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath *>&1
    $exitCode = $LASTEXITCODE
    $output | Set-Content -LiteralPath $resultPath -Encoding UTF8
    if ($exitCode -ne 0) {
        exit $exitCode
    }
}
catch {
    $_ | Out-String | Set-Content -LiteralPath $resultPath -Encoding UTF8
    exit 1
}
exit 0
