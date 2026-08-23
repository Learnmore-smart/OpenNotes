<#
.SYNOPSIS
    Compatibility wrapper for the complete OpenNotes Codex/Antigravity metadata migration.

.DESCRIPTION
    This entry point never terminates Codex and never implements a partial database-only
    repair. Normal execution delegates to migrate-codex-project.ps1, which delegates to
    the full backup-safe migration. Close the desktop applications yourself first.
#>
[CmdletBinding()]
param(
    [Alias('CodexHome')]
    [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex'),
    [string]$OldPath = 'D:\Noah\文档\Coding\2. 开源的项目\Caelum',
    [Alias('NewPath')]
    [string]$NewRoot = 'D:\Noah\文档\Coding\1. Open-Source\OpenNotes',
    [string]$BackupRoot,
    [string]$SqlitePath,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$compatibilityScript = Join-Path $PSScriptRoot 'migrate-codex-project.ps1'
if (-not (Test-Path -LiteralPath $compatibilityScript -PathType Leaf)) {
    throw "Compatibility migration script was not found: $compatibilityScript"
}

$parameters = @{
    StateRoot = $StateRoot
    OldPath = $OldPath
    NewRoot = $NewRoot
}
if (-not [string]::IsNullOrWhiteSpace($BackupRoot)) { $parameters.BackupRoot = $BackupRoot }
if (-not [string]::IsNullOrWhiteSpace($SqlitePath)) { $parameters.SqlitePath = $SqlitePath }
if ($ValidateOnly) { $parameters.ValidateOnly = $true }

& $compatibilityScript @parameters
exit $LASTEXITCODE
