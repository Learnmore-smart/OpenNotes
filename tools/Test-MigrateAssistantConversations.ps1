$ErrorActionPreference = 'Stop'

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message`nExpected: $Expected`nActual:   $Actual"
    }
}

function Encode-Varint([int]$Value) {
    $bytes = [System.Collections.Generic.List[byte]]::new()
    do {
        $part = $Value -band 0x7f
        $Value = $Value -shr 7
        if ($Value -ne 0) { $part = $part -bor 0x80 }
        $bytes.Add([byte]$part)
    } while ($Value -ne 0)
    return $bytes.ToArray()
}

function Convert-BytesToHex([byte[]]$Bytes) {
    return -join ($Bytes | ForEach-Object { $_.ToString('x2') })
}

function Convert-HexToBytes([string]$Hex) {
    $bytes = [byte[]]::new($Hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16) }
    return $bytes
}

$migrationScript = Join-Path $PSScriptRoot 'Migrate-AssistantConversations.ps1'
if (-not (Test-Path -LiteralPath $migrationScript)) {
    throw "Migration script is missing: $migrationScript"
}

    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("opennotes-migration-test-" + [guid]::NewGuid().ToString('N'))
    $codexHome = Join-Path $fixtureRoot '.codex'
    $geminiRoot = Join-Path $fixtureRoot 'antigravity-ide'
    $geminiCliRoot = Join-Path $fixtureRoot 'antigravity-cli'
    $antigravityRoaming = Join-Path $fixtureRoot 'Antigravity IDE'
$sessionDir = Join-Path $codexHome 'sessions\2026\08\20'
$archiveDir = Join-Path $codexHome 'archived_sessions'
    $sqliteDir = Join-Path $codexHome 'sqlite'
    $conversationDir = Join-Path $geminiRoot 'conversations'
    $conversationCliDir = Join-Path $geminiCliRoot 'conversations'
$workspaceStorageDir = Join-Path $antigravityRoaming 'User\workspaceStorage\old-workspace-id'
$globalStorageDir = Join-Path $antigravityRoaming 'User\globalStorage'

try {
    New-Item -ItemType Directory -Force -Path $sessionDir, $archiveDir, $sqliteDir, $conversationDir, $conversationCliDir, $workspaceStorageDir, $globalStorageDir | Out-Null

    $oldCurrent = 'D:\Noah\文档\Coding\1. Open-Source\Caelum'
    $oldLegacy = 'D:\Noah\文档\Coding\2. 开源的项目\Caelum'
    $newRoot = 'D:\Noah\文档\Coding\1. Open-Source\OpenNotes'
    $oldProject = 'fc720e52-224f-4685-b49e-cf409a93714a'
    $legacyProject = 'local-c7c1b7bb7f2a4e29d4558765c5b1083b'
    $databaseOnlyProject = 'db-only-old-root-project'
    $newProject = '382d995a-37b1-436c-90ed-54b109d76606'

    $bodyLine = '{"type":"response_item","payload":{"text":"Conversation body mentions D:\\\\Noah\\\\Caelum and must not change."}}'
    $sessionPath = Join-Path $sessionDir 'rollout-test-current.jsonl'
    $archivePath = Join-Path $archiveDir 'rollout-test-legacy.jsonl'
    [IO.File]::WriteAllText($sessionPath, "{`"type`":`"session_meta`",`"payload`":{`"id`":`"thread-current`",`"cwd`":`"$($oldCurrent.Replace('\','\\'))`"}}`n$bodyLine`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($archivePath, "{`"type`":`"session_meta`",`"payload`":{`"id`":`"thread-legacy`",`"cwd`":`"$($oldLegacy.Replace('\','\\'))`"}}`n$bodyLine`n", [Text.UTF8Encoding]::new($false))

    $stateDb = Join-Path $codexHome 'state_5.sqlite'
    & sqlite3 $stateDb "CREATE TABLE threads(id TEXT PRIMARY KEY,cwd TEXT,project_id TEXT); CREATE TABLE projects(id TEXT PRIMARY KEY,name TEXT,metadata TEXT,position INTEGER,created_at_ms INTEGER,updated_at_ms INTEGER); CREATE TABLE project_roots(project_id TEXT,position INTEGER,path TEXT,PRIMARY KEY(project_id,position)); INSERT INTO projects VALUES('$oldProject','Caelum','{}',1,1,1),('$databaseOnlyProject','Caelum','{}',2,2,2),('$newProject','OpenNotes','{}',0,0,0); INSERT INTO project_roots VALUES('$oldProject',0,'$oldCurrent'),('$databaseOnlyProject',0,'$oldLegacy'),('$newProject',0,'$newRoot'); INSERT INTO threads VALUES('thread-current','\\?\$oldCurrent','$oldProject'),('thread-legacy','$oldLegacy',NULL);"
    $stateWal = $stateDb + '-wal'
    $stateShm = $stateDb + '-shm'
    [IO.File]::WriteAllBytes($stateWal, [byte[]](1..16))
    [IO.File]::WriteAllBytes($stateShm, [byte[]](17..32))
    $legacyDb = Join-Path $sqliteDir 'state_5.sqlite'
    & sqlite3 $legacyDb "CREATE TABLE threads(id TEXT PRIMARY KEY,cwd TEXT); INSERT INTO threads VALUES('thread-legacy','$oldLegacy');"
    $catalogDb = Join-Path $sqliteDir 'codex-dev.db'
    & sqlite3 $catalogDb "CREATE TABLE local_thread_catalog(host_id TEXT,thread_id TEXT,cwd TEXT,project_id TEXT,PRIMARY KEY(host_id,thread_id)); INSERT INTO local_thread_catalog VALUES('local','thread-current','$oldCurrent','$oldProject');"

    $globalState = [ordered]@{
        'prompt-history' = @{ global = @("Do not rewrite this body: $oldCurrent") }
        'local-projects' = [ordered]@{
            $legacyProject = @{ id=$legacyProject; name='Caelum'; rootPaths=@($oldLegacy) }
            $oldProject = @{ id=$oldProject; name='Caelum'; rootPaths=@($oldCurrent) }
            $newProject = @{ id=$newProject; name='OpenNotes'; rootPaths=@($newRoot) }
        }
        'project-order' = @($legacyProject,$oldProject,$newProject)
        'thread-project-assignments' = [ordered]@{
            'thread-current' = @{ projectKind='local'; projectId=$oldProject }
            'thread-legacy' = @{ projectKind='local'; projectId=$legacyProject }
        }
        'thread-workspace-root-hints' = [ordered]@{
            'thread-current' = @($oldCurrent)
            'thread-legacy' = @($oldLegacy)
        }
        'active-workspace-roots' = @($oldCurrent,$newRoot)
    }
    $globalStatePath = Join-Path $codexHome '.codex-global-state.json'
    [IO.File]::WriteAllText($globalStatePath, ($globalState | ConvertTo-Json -Depth 20 -Compress), [Text.UTF8Encoding]::new($false))

    $oldUri = 'file:///d:/Noah/%E6%96%87%E6%A1%A3/Coding/1.%20Open-Source/Caelum'
    $payload = [Text.Encoding]::UTF8.GetBytes($oldUri)
    $proto = [byte[]](@(0x0a) + (Encode-Varint $payload.Length) + $payload)
    $conversationDb = Join-Path $conversationDir 'conversation.db'
    & sqlite3 $conversationDb "CREATE TABLE trajectory_metadata_blob(id TEXT PRIMARY KEY,data BLOB); INSERT INTO trajectory_metadata_blob VALUES('main',X'$(Convert-BytesToHex $proto)'),('main-2',X'$(Convert-BytesToHex $proto)');"
    $conversationCliDb = Join-Path $conversationCliDir 'conversation.db'
    & sqlite3 $conversationCliDb "CREATE TABLE trajectory_metadata_blob(id TEXT PRIMARY KEY,data BLOB); INSERT INTO trajectory_metadata_blob VALUES('cli-main',X'$(Convert-BytesToHex $proto)');"

    $workspaceJson = Join-Path $workspaceStorageDir 'workspace.json'
    [IO.File]::WriteAllText($workspaceJson, "{`"folder`":`"file:///d%3A/Noah/%E6%96%87%E6%A1%A3/Coding/1.%20Open-Source/Caelum`"}", [Text.UTF8Encoding]::new($false))
    $workspaceDb = Join-Path $workspaceStorageDir 'state.vscdb'
    $quotedWorkspaceState = "{`"resourceJSON`":{`"fsPath`":`"$($oldCurrent.Replace('\','\\'))`"}}"
    $quotedWorkspaceHex = Convert-BytesToHex ([Text.Encoding]::UTF8.GetBytes($quotedWorkspaceState))
    $quadEscapedOld = $oldCurrent.Replace('\','\\\\')
    $forwardOld = $oldCurrent.Replace('\','/')
    $extendedOld = '\\?\' + $oldCurrent
    $largeWorkspaceState = ('x' * 40000) + $quotedWorkspaceState + " fsPath=$quadEscapedOld path=/$forwardOld/docs/spec.md extended=$extendedOld\docs sibling=$oldCurrent-archive"
    $largeWorkspaceHex = Convert-BytesToHex ([Text.Encoding]::UTF8.GetBytes($largeWorkspaceState))
    "CREATE TABLE ItemTable(key TEXT UNIQUE,value BLOB); INSERT INTO ItemTable VALUES('debug.selectedroot',CAST(X'$quotedWorkspaceHex' AS TEXT)); INSERT INTO ItemTable VALUES('memento/workbench.parts.editor',CAST(X'$largeWorkspaceHex' AS TEXT)); INSERT INTO ItemTable VALUES('terminal.integrated.bufferState','body mentions Caelum and must stay');" | & sqlite3 $workspaceDb
    $globalDb = Join-Path $globalStorageDir 'state.vscdb'
    & sqlite3 $globalDb "CREATE TABLE ItemTable(key TEXT UNIQUE,value BLOB); INSERT INTO ItemTable VALUES('history.recentlyOpenedPathsList','file:///d%3A/Noah/%E6%96%87%E6%A1%A3/Coding/1.%20Open-Source/Caelum');"

    $sessionHashBeforeRollback = (Get-FileHash -Algorithm SHA256 -LiteralPath $sessionPath).Hash
    $archiveHashBeforeRollback = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
    $globalHashBeforeRollback = (Get-FileHash -Algorithm SHA256 -LiteralPath $globalStatePath).Hash
    $stateWalHashBeforeRollback = (Get-FileHash -Algorithm SHA256 -LiteralPath $stateWal).Hash
    $stateShmHashBeforeRollback = (Get-FileHash -Algorithm SHA256 -LiteralPath $stateShm).Hash
    & sqlite3 $catalogDb "DROP TABLE local_thread_catalog; CREATE TABLE local_thread_catalog(host_id TEXT,thread_id TEXT,cwd TEXT,PRIMARY KEY(host_id,thread_id));"
    $migrationExitCode = 0
    try {
        & $migrationScript -CodexHome $codexHome -GeminiRoots @($geminiRoot,$geminiCliRoot) -AntigravityRoamingRoots @($antigravityRoaming) -NewRoot $newRoot -SkipProcessCheck 2>$null | Out-Null
        $migrationExitCode = $LASTEXITCODE
    }
    catch {
        $migrationExitCode = 1
    }
    Assert-Equal $true ($migrationExitCode -ne 0) 'Migration failure was not surfaced.'
    Assert-Equal $sessionHashBeforeRollback (Get-FileHash -Algorithm SHA256 -LiteralPath $sessionPath).Hash 'Rollout file was not restored after rollback.'
    Assert-Equal $archiveHashBeforeRollback (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash 'Archived rollout was not restored after rollback.'
    Assert-Equal $globalHashBeforeRollback (Get-FileHash -Algorithm SHA256 -LiteralPath $globalStatePath).Hash 'Global Codex state was not restored after rollback.'
    Assert-Equal $stateWalHashBeforeRollback (Get-FileHash -Algorithm SHA256 -LiteralPath $stateWal).Hash 'Primary Codex WAL sidecar was not restored after rollback.'
    Assert-Equal $stateShmHashBeforeRollback (Get-FileHash -Algorithm SHA256 -LiteralPath $stateShm).Hash 'Primary Codex SHM sidecar was not restored after rollback.'
    Assert-Equal '2' (& sqlite3 -readonly $stateDb "SELECT count(*) FROM threads WHERE cwd LIKE '%Caelum%';") 'Primary Codex database was not restored after rollback.'
    Assert-Equal '1' (& sqlite3 -readonly $legacyDb "SELECT count(*) FROM threads WHERE cwd='$oldLegacy';") 'Legacy Codex database was not restored after rollback.'
    Remove-Item -LiteralPath $catalogDb -Force
    & sqlite3 $catalogDb "CREATE TABLE local_thread_catalog(host_id TEXT,thread_id TEXT,cwd TEXT,project_id TEXT,PRIMARY KEY(host_id,thread_id)); INSERT INTO local_thread_catalog VALUES('local','thread-current','$oldCurrent','$oldProject');"

    & $migrationScript -CodexHome $codexHome -GeminiRoots @($geminiRoot,$geminiCliRoot) -AntigravityRoamingRoots @($antigravityRoaming) -NewRoot $newRoot -SkipProcessCheck | Out-Null

    $sessionLines = [IO.File]::ReadAllLines($sessionPath)
    Assert-Equal $bodyLine $sessionLines[1] 'Conversation body changed.'
    Assert-Equal $newRoot (($sessionLines[0] | ConvertFrom-Json).payload.cwd) 'Current rollout cwd was not migrated.'
    Assert-Equal $newRoot ((([IO.File]::ReadAllLines($archivePath)[0]) | ConvertFrom-Json).payload.cwd) 'Archived rollout cwd was not migrated.'

    Assert-Equal '2' (& sqlite3 -readonly $stateDb "SELECT count(*) FROM threads WHERE cwd='$newRoot' AND project_id='$newProject';") 'Primary Codex thread rows were not consolidated.'
    Assert-Equal '0' (& sqlite3 -readonly $stateDb "SELECT count(*) FROM projects WHERE id='$oldProject';") 'Old Codex project remains.'
    Assert-Equal '0' (& sqlite3 -readonly $stateDb "SELECT count(*) FROM projects WHERE id='$databaseOnlyProject';") 'Database-only old-root project remains.'
    Assert-Equal '1' (& sqlite3 -readonly $stateDb "SELECT count(*) FROM projects WHERE id='$newProject' AND name='Caelum';") 'Caelum project is missing.'
    Assert-Equal '1' (& sqlite3 -readonly $legacyDb "SELECT count(*) FROM threads WHERE cwd='$newRoot';") 'Legacy Codex database was not migrated.'
    Assert-Equal "$newRoot|$newProject" (& sqlite3 -readonly $catalogDb "SELECT cwd,project_id FROM local_thread_catalog;") 'Codex desktop catalog was not migrated.'

    $updatedState = [IO.File]::ReadAllText($globalStatePath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal "Do not rewrite this body: $oldCurrent" $updatedState.'prompt-history'.global[0] 'Prompt history was rewritten.'
    Assert-Equal $newProject $updatedState.'thread-project-assignments'.'thread-current'.projectId 'Current thread assignment was not consolidated.'
    Assert-Equal $newProject $updatedState.'thread-project-assignments'.'thread-legacy'.projectId 'Legacy thread assignment was not consolidated.'
    Assert-Equal 'Caelum' $updatedState.'local-projects'.$newProject.name 'Canonical project was not renamed to the compatibility sidebar name.'
    Assert-Equal $false ($null -ne $updatedState.'local-projects'.$oldProject) 'Old project remains in global state.'
    Assert-Equal $false ($null -ne $updatedState.'local-projects'.$legacyProject) 'Legacy project remains in global state.'

    foreach ($conversationId in @('main', 'main-2')) {
        $hex = & sqlite3 -readonly $conversationDb "SELECT hex(data) FROM trajectory_metadata_blob WHERE id='$conversationId';"
        $updatedProtoText = [Text.Encoding]::UTF8.GetString((Convert-HexToBytes $hex))
        Assert-Equal $true ($updatedProtoText.Contains('/OpenNotes')) "Antigravity conversation metadata was not migrated: $conversationId"
        Assert-Equal $false ($updatedProtoText.Contains('/Caelum')) "Antigravity metadata still contains the old folder: $conversationId"
    }
    $cliHex = & sqlite3 -readonly $conversationCliDb "SELECT hex(data) FROM trajectory_metadata_blob WHERE id='cli-main';"
    $updatedCliProtoText = [Text.Encoding]::UTF8.GetString((Convert-HexToBytes $cliHex))
    Assert-Equal $true ($updatedCliProtoText.Contains('/OpenNotes')) 'Antigravity CLI conversation metadata was not migrated.'
    Assert-Equal $false ($updatedCliProtoText.Contains('/Caelum')) 'Antigravity CLI metadata still contains the old folder.'
    Assert-Equal $true (([IO.File]::ReadAllText($workspaceJson, [Text.Encoding]::UTF8)).Contains('/OpenNotes')) 'Antigravity workspace.json was not migrated.'
    $updatedWorkspaceState = & sqlite3 -readonly $workspaceDb "SELECT value FROM ItemTable WHERE key='debug.selectedroot';"
    Assert-Equal $true ($updatedWorkspaceState.Contains('OpenNotes')) 'Antigravity workspace state was not migrated.'
    Assert-Equal $true ($updatedWorkspaceState.Contains('"resourceJSON"')) 'Antigravity workspace JSON quoting was corrupted.'
    Assert-Equal $true ((& sqlite3 -readonly $workspaceDb "SELECT value FROM ItemTable WHERE key='memento/workbench.parts.editor';").Contains('OpenNotes')) 'Large Antigravity editor state was not migrated.'
    $updatedEditorState = & sqlite3 -readonly $workspaceDb "SELECT value FROM ItemTable WHERE key='memento/workbench.parts.editor';"
    Assert-Equal $true ($updatedEditorState.Contains("extended=$newRoot\docs")) 'Extended-path prefix was not normalized before migration.'
    Assert-Equal $true ($updatedEditorState.Contains("sibling=$oldCurrent-archive")) 'Path-boundary guard rewrote a Caelum-archive sibling value.'
    Assert-Equal '0' (& sqlite3 -readonly $workspaceDb "SELECT count(*) FROM ItemTable WHERE key='memento/workbench.parts.editor' AND instr(CAST(value AS TEXT),'Caelum')>0 AND instr(CAST(value AS TEXT),'Caelum-archive')=0;") 'Nested Antigravity editor paths still reference Caelum.'
    Assert-Equal 'body mentions Caelum and must stay' (& sqlite3 -readonly $workspaceDb "SELECT value FROM ItemTable WHERE key='terminal.integrated.bufferState';") 'Antigravity terminal body was rewritten.'
    Assert-Equal $true ((& sqlite3 -readonly $globalDb "SELECT value FROM ItemTable WHERE key='history.recentlyOpenedPathsList';").Contains('/OpenNotes')) 'Antigravity recent workspace link was not migrated.'

    & sqlite3 $workspaceDb "UPDATE ItemTable SET value=CAST(X'$quotedWorkspaceHex' AS TEXT) WHERE key='debug.selectedroot';"
    & $migrationScript -CodexHome $codexHome -GeminiRoots @($geminiRoot,$geminiCliRoot) -AntigravityRoamingRoots @($antigravityRoaming) -NewRoot $newRoot -NewProjectId $newProject -SkipCodex -SkipProcessCheck | Out-Null
    Assert-Equal $true ((& sqlite3 -readonly $workspaceDb "SELECT value FROM ItemTable WHERE key='debug.selectedroot';").Contains('OpenNotes')) 'Interrupted Antigravity workspace-state migration did not resume after workspace.json was already updated.'

    $manifest = Get-ChildItem -LiteralPath (Join-Path $fixtureRoot 'migration-backups') -Recurse -File -Filter 'manifest.json' | Select-Object -First 1
    Assert-Equal $true ($null -ne $manifest) 'Backup manifest was not created.'
    $manifestData = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
    Assert-Equal 'passed' ([string]$manifestData.validation.status) 'Migration validation status was not recorded as passed.'
    Assert-Equal 0 ([int]$manifestData.validation.rollout_old_root_headers) 'Validation reported old rollout roots after migration.'
    Assert-Equal 0 ([int]$manifestData.validation.primary_unassigned_current_root_threads) 'Validation reported unassigned current-root primary threads.'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string]$manifestData.sqlite.path)) 'SQLite executable path was not recorded.'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string]$manifestData.sqlite.sha256)) 'SQLite executable hash was not recorded.'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string]$manifestData.sqlite.version)) 'SQLite executable version was not recorded.'
    Assert-Equal 3 ([int]$manifestData.stats.AntigravityConversations) 'IDE and CLI Antigravity databases were not all migrated.'
    $primaryWalBackup = @($manifestData.files | Where-Object { $_.source -like '*\.codex\state_5.sqlite-wal' -and $_.exists_before }) | Select-Object -First 1
    Assert-Equal $true ($null -ne $primaryWalBackup -and [string]$primaryWalBackup.backup -like '*.snapshot-wal') 'Primary WAL snapshot did not use a SQLite-safe backup name.'
    Write-Output 'PASS: assistant conversation metadata migration fixtures'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        [GC]::Collect(); [GC]::WaitForPendingFinalizers()
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
