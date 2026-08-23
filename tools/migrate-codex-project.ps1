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
$historicalRoots = @(
    'D:\Noah\文档\Coding\1. Open-Source\Caelum',
    'D:\Noah\文档\Coding\2. 开源的项目\Caelum'
)

if (-not [string]::IsNullOrWhiteSpace($OldPath) -and
    -not ($historicalRoots | Where-Object { $_.Equals($OldPath.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) })) {
    throw "Unsupported historical root '$OldPath'. The full migration owns these roots: $($historicalRoots -join '; ')"
}

function Resolve-SqlitePath {
    if (-not [string]::IsNullOrWhiteSpace($SqlitePath)) {
        return (Get-Item -LiteralPath $SqlitePath -ErrorAction Stop).FullName
    }
    return (Get-Command sqlite3.exe -ErrorAction Stop).Source
}

function Get-BlockingProcesses {
    @(
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.ProcessName -in @('ChatGPT', 'codex', 'codex-code-mode-host', 'Antigravity', 'Antigravity IDE') -or
                $_.ProcessName -like 'codex-command-runner-*'
            }
    )
}

if ($ValidateOnly) {
    $blocking = @(Get-BlockingProcesses)
    if ($blocking.Count -gt 0) {
        throw "Close Codex and Antigravity before validation. Running: $((($blocking.ProcessName | Sort-Object -Unique) -join ', '))"
    }

    $stateDb = Join-Path $StateRoot 'state_5.sqlite'
    if (-not (Test-Path -LiteralPath $stateDb -PathType Leaf)) {
        throw "Codex state database not found: $stateDb"
    }
    $sqlite = Resolve-SqlitePath
    $newQ = "'$($NewRoot.Replace("'", "''"))'"
    $oldQ = ($historicalRoots | ForEach-Object { "'$($_.Replace("'", "''"))'" }) -join ','
    $canonicalIds = @(& $sqlite -readonly $stateDb "SELECT DISTINCT project_id FROM project_roots WHERE replace(path,'\\?\','') COLLATE NOCASE=$newQ;" | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
    $oldRootRows = [int](& $sqlite -readonly $stateDb "SELECT count(*) FROM project_roots WHERE replace(path,'\\?\','') COLLATE NOCASE IN ($oldQ);")
    $oldThreadRows = [int](& $sqlite -readonly $stateDb "SELECT count(*) FROM threads WHERE replace(cwd,'\\?\','') COLLATE NOCASE IN ($oldQ);")
    [pscustomobject]@{
        StateRoot = $StateRoot
        NewRoot = $NewRoot
        CanonicalProjectIds = @($canonicalIds)
        OldRootRows = $oldRootRows
        OldThreadRows = $oldThreadRows
        CodexProcessesClosed = $true
        SqlitePath = $sqlite
    } | Format-List
    exit 0
}

$migrationScript = Join-Path $PSScriptRoot 'Migrate-AssistantConversations.ps1'
if (-not (Test-Path -LiteralPath $migrationScript -PathType Leaf)) {
    throw "Full migration script was not found: $migrationScript"
}

$parameters = @{
    CodexHome = $StateRoot
    NewRoot = $NewRoot
}
if (-not [string]::IsNullOrWhiteSpace($BackupRoot)) { $parameters.BackupRoot = $BackupRoot }
if (-not [string]::IsNullOrWhiteSpace($SqlitePath)) { $parameters.SqlitePath = $SqlitePath }

& $migrationScript @parameters
exit $LASTEXITCODE
