param(
    [string]$CodexHome = (Join-Path $env:USERPROFILE '.codex'),
    [string[]]$GeminiRoots = @(
        (Join-Path $env:USERPROFILE '.gemini\antigravity'),
        (Join-Path $env:USERPROFILE '.gemini\antigravity-ide'),
        (Join-Path $env:USERPROFILE '.gemini\antigravity-cli')
    ),
    [string[]]$AntigravityRoamingRoots = @(
        (Join-Path $env:APPDATA 'Antigravity'),
        (Join-Path $env:APPDATA 'Antigravity IDE')
    ),
    [string]$NewRoot = 'D:\Noah\文档\Coding\1. Open-Source\OpenNotes',
    [string]$NewProjectId = '',
    [string]$BackupRoot,
    [string]$SqlitePath,
    [switch]$SkipCodex,
    [switch]$SkipAntigravity,
    [switch]$SkipProcessCheck
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$oldRoots = @(
    'D:\Noah\文档\Coding\1. Open-Source\Caelum',
    'D:\Noah\文档\Coding\2. 开源的项目\Caelum'
)
$knownOldProjectIds = @(
    'fc720e52-224f-4685-b49e-cf409a93714a',
    'local-c7c1b7bb7f2a4e29d4558765c5b1083b'
)

function Get-BlockingProcesses {
    return @(
        Get-Process -ErrorAction SilentlyContinue | Where-Object {
            ((-not $SkipCodex) -and ($_.ProcessName -in @('codex', 'codex-code-mode-host', 'ChatGPT') -or $_.ProcessName -like 'codex-command-runner-*')) -or
            ((-not $SkipAntigravity) -and $_.ProcessName -in @('Antigravity', 'Antigravity IDE'))
        }
    )
}

function Assert-NoBlockingProcesses {
    if ($SkipProcessCheck) { return }
    $blocking = @(Get-BlockingProcesses)
    if ($blocking.Count -gt 0) {
        throw "Close Codex and Antigravity before migrating their metadata. Running: $((($blocking.ProcessName | Sort-Object -Unique) -join ', '))"
    }
}

Assert-NoBlockingProcesses

if ([string]::IsNullOrWhiteSpace($SqlitePath)) {
    $SqlitePath = (Get-Command sqlite3.exe -ErrorAction Stop).Source
} else {
    $SqlitePath = (Get-Item -LiteralPath $SqlitePath -ErrorAction Stop).FullName
}
if (-not (Test-Path -LiteralPath $SqlitePath -PathType Leaf)) {
    throw "SQLite executable was not found: $SqlitePath"
}
$sqlite = $SqlitePath
$sqliteHash = (Get-FileHash -LiteralPath $sqlite -Algorithm SHA256).Hash.ToLowerInvariant()
$sqliteVersionOutput = @(& $sqlite '--version' 2>&1)
if ($LASTEXITCODE -ne 0 -or $sqliteVersionOutput.Count -eq 0) {
    throw "Could not verify SQLite executable: $sqlite"
}
$sqliteVersion = [string]$sqliteVersionOutput[0]

$migrationMutex = [Threading.Mutex]::new($false, 'Local\OpenNotes.CaelumAssistantMigration')
$mutexHeld = $false
try {
    $mutexHeld = $migrationMutex.WaitOne(0)
} catch [Threading.AbandonedMutexException] {
    $mutexHeld = $true
}
if (-not $mutexHeld) {
    $migrationMutex.Dispose()
    throw 'Another OpenNotes assistant metadata migration is already running.'
}

if (-not $BackupRoot) {
    $BackupRoot = Join-Path (Split-Path -Parent $CodexHome) 'migration-backups'
}
$runStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runBackup = Join-Path $BackupRoot "caelum-to-opennotes-$runStamp-$([guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Force -Path $runBackup | Out-Null
$manifestEntries = [System.Collections.Generic.List[object]]::new()
$backedUpSources = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$removedCodexProjectIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$affectedThreadIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$stats = [ordered]@{ CodexRollouts = 0; CodexDatabaseRows = 0; CodexProjectsRemoved = 0; AntigravityConversations = 0; AntigravityWorkspaceFiles = 0; AntigravityStateValues = 0 }

function Convert-BytesToHex([byte[]]$Bytes) {
    return -join ($Bytes | ForEach-Object { $_.ToString('x2') })
}

function Convert-HexToBytes([string]$Hex) {
    if (($Hex.Length % 2) -ne 0) { throw 'Invalid hexadecimal string.' }
    $bytes = [byte[]]::new($Hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16) }
    return $bytes
}

function Get-ByteHashHex([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (Convert-BytesToHex ($sha.ComputeHash($Bytes))) }
    finally { $sha.Dispose() }
}

function Replace-OrdinalIgnoreCase([string]$InputText, [string]$OldValue, [string]$NewValue) {
    $pattern = '(?<![A-Za-z0-9._-])' + [regex]::Escape($OldValue) + '(?=$|[^A-Za-z0-9._-])'
    return [regex]::Replace($InputText, $pattern, { param($match) $NewValue }, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Add-BackupManifestEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [AllowNull()][string]$Backup,
        [Parameter(Mandatory = $true)][bool]$ExistsBefore,
        [AllowNull()][string]$BeforeHash
    )

    if ($ExistsBefore -and [string]::IsNullOrWhiteSpace($BeforeHash)) {
        $BeforeHash = Get-Sha256 $Source
    }
    $manifestEntries.Add([ordered]@{
        source = $Source
        backup = $Backup
        exists_before = $ExistsBefore
        sha256_before = $BeforeHash
    })
}

function Backup-File([string]$Path, [string]$Category, [switch]$SqliteDatabase) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    if (-not $backedUpSources.Add($Path)) { return }
    Assert-NoBlockingProcesses
    $mainBeforeHash = Get-Sha256 $Path
    $pathHash = (Get-ByteHashHex ([Text.Encoding]::UTF8.GetBytes($Path))).Substring(0,12)
    $categoryDir = Join-Path $runBackup $Category
    New-Item -ItemType Directory -Force -Path $categoryDir | Out-Null
    $destination = Join-Path $categoryDir "$pathHash-$([IO.Path]::GetFileName($Path))"
    if ($SqliteDatabase) {
        $sidecarRecords = [System.Collections.Generic.List[object]]::new()
        foreach ($suffix in @('-wal', '-shm')) {
            $sidecar = $Path + $suffix
            $sidecarExists = Test-Path -LiteralPath $sidecar -PathType Leaf
            # Keep the snapshot outside SQLite's destination sidecar naming scheme; .backup may manage
            # files named <destination>-wal/<destination>-shm while it opens the backup database.
            $sidecarDestination = $destination + '.snapshot' + $suffix
            $sidecarHash = if ($sidecarExists) { Get-Sha256 $sidecar } else { $null }
            if ($sidecarExists) {
                Copy-Item -LiteralPath $sidecar -Destination $sidecarDestination -Force
            } else {
                $sidecarDestination = $null
            }
            $sidecarRecords.Add([pscustomobject]@{
                Source = $sidecar
                Backup = $sidecarDestination
                ExistsBefore = $sidecarExists
                BeforeHash = $sidecarHash
            })
        }
        foreach ($record in $sidecarRecords) {
            Add-BackupManifestEntry -Source $record.Source -Backup $record.Backup -ExistsBefore $record.ExistsBefore -BeforeHash $record.BeforeHash
        }
        $sqliteDestination = $destination.Replace("'", "''")
        & $sqlite -readonly $Path ".backup '$sqliteDestination'"
        if ($LASTEXITCODE -ne 0) { throw "SQLite backup failed: $Path" }
        Add-BackupManifestEntry -Source $Path -Backup $destination -ExistsBefore $true -BeforeHash $mainBeforeHash
    } else {
        Copy-Item -LiteralPath $Path -Destination $destination -Force
        Add-BackupManifestEntry -Source $Path -Backup $destination -ExistsBefore $true -BeforeHash $mainBeforeHash
    }
}

function Write-AtomicUtf8([string]$Path, [string]$Content) {
    Assert-NoBlockingProcesses
    $temporary = Join-Path ([IO.Path]::GetDirectoryName($Path)) ('.' + [IO.Path]::GetFileName($Path) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
    $replaceBackup = $temporary + '.replace-backup'
    [IO.File]::WriteAllText($temporary, $Content, $utf8NoBom)
    try { [IO.File]::Replace($temporary, $Path, $replaceBackup) }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        if (Test-Path -LiteralPath $replaceBackup) { Remove-Item -LiteralPath $replaceBackup -Force }
    }
}

function Convert-RootString([string]$Value) {
    $withoutPrefix = if ($Value.StartsWith('\\?\')) { $Value.Substring(4) } else { $Value }
    foreach ($old in $oldRoots) {
        if ($withoutPrefix.Equals($old, [StringComparison]::OrdinalIgnoreCase)) { return $NewRoot }
    }
    return $null
}

function Get-UriPairs {
    $pairs = [System.Collections.Generic.List[object]]::new()
    foreach ($old in $oldRoots) {
        $makeUris = {
            param([string]$Path)
            $segments = $Path.Replace('\','/').Split('/')
            $driveLetter = $segments[0].Substring(0,1).ToLowerInvariant()
            $encodedTail = ($segments[1..($segments.Length-1)] | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
            return @("file:///$driveLetter`:/$encodedTail", "file:///$driveLetter%3A/$encodedTail")
        }
        $oldUris = @(& $makeUris $old)
        $newUris = @(& $makeUris $NewRoot)
        foreach ($pair in @(
            @($oldUris[0], $newUris[0]),
            @($oldUris[1], $newUris[1])
        )) {
            $pairs.Add([pscustomobject]@{ Old=[string]$pair[0]; New=[string]$pair[1] })
        }
    }
    return $pairs
}
$uriPairs = @(Get-UriPairs)

function Convert-LinkString([string]$Value) {
    $root = Convert-RootString $Value
    if ($null -ne $root) { return $root }
    foreach ($pair in $uriPairs) {
        if ($Value.Equals($pair.Old, [StringComparison]::OrdinalIgnoreCase)) { return $pair.New }
    }
    return $null
}

function Test-IsNewLinkString([string]$Value) {
    if ($Value.Equals($NewRoot, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    foreach ($pair in $uriPairs) {
        if ($Value.Equals($pair.New, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Convert-StructuralText([string]$Value) {
    $updated = $Value
    foreach ($pair in $uriPairs) {
        $updated = Replace-OrdinalIgnoreCase $updated $pair.Old $pair.New
    }
    foreach ($old in $oldRoots) {
        $extended = '\\?\' + $old
        $extendedEscaped = $extended.Replace('\','\\')
        $updated = Replace-OrdinalIgnoreCase $updated $extended $NewRoot
        $updated = Replace-OrdinalIgnoreCase $updated $extendedEscaped ($NewRoot.Replace('\','\\'))
        $updated = Replace-OrdinalIgnoreCase $updated $old $NewRoot
        $updated = Replace-OrdinalIgnoreCase $updated ($old.Replace('\','/')) ($NewRoot.Replace('\','/'))
        $updated = Replace-OrdinalIgnoreCase $updated ($old.Replace('\','\\')) ($NewRoot.Replace('\','\\'))
        $updated = Replace-OrdinalIgnoreCase $updated ($old.Replace('\','\\\\')) ($NewRoot.Replace('\','\\\\'))
    }
    return $updated
}

function Sql-Quote([string]$Value) { return "'" + $Value.Replace("'", "''") + "'" }

function Invoke-Sqlite([string]$Database, [string]$Sql) {
    Assert-NoBlockingProcesses
    $sqlFile = Join-Path ([IO.Path]::GetTempPath()) ("opennotes-sql-" + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($sqlFile, $Sql, $utf8NoBom)
    try {
        $readCommand = '.read ' + $sqlFile.Replace('\','/')
        $output = & $sqlite $Database '.bail on' $readCommand
        if ($LASTEXITCODE -ne 0) { throw "SQLite command failed: $Database" }
        return $output
    } finally {
        if (Test-Path -LiteralPath $sqlFile) { Remove-Item -LiteralPath $sqlFile -Force }
    }
}

function Test-SqliteTable([string]$Database, [string]$Table) {
    $result = @(& $sqlite -readonly $Database "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$(Sql-Quote $Table);")
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect SQLite schema: $Database" }
    return (@($result | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })[-1] -eq '1')
}

function Test-SqliteColumn([string]$Database, [string]$Table, [string]$Column) {
    $result = @(& $sqlite -readonly $Database "SELECT count(*) FROM pragma_table_info($(Sql-Quote $Table)) WHERE name=$(Sql-Quote $Column);")
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect SQLite schema: $Database" }
    return (@($result | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })[-1] -eq '1')
}

function Get-SqliteScalar([string]$Database, [string]$Sql) {
    $result = @(& $sqlite -readonly $Database $Sql)
    if ($LASTEXITCODE -ne 0) { throw "SQLite validation query failed: $Database" }
    $values = @($result | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
    if ($values.Count -eq 0) { return '' }
    return [string]$values[-1]
}

function Read-Varint([byte[]]$Data, [ref]$Offset) {
    [uint64]$value = 0
    $shift = 0
    while ($Offset.Value -lt $Data.Length -and $shift -lt 70) {
        $b = $Data[$Offset.Value]
        $Offset.Value++
        $value = $value -bor ([uint64]($b -band 0x7f) -shl $shift)
        if (($b -band 0x80) -eq 0) { return $value }
        $shift += 7
    }
    throw 'Invalid protobuf varint.'
}

function Write-Varint([IO.Stream]$Stream, [uint64]$Value) {
    do {
        $part = [byte]($Value -band 0x7f)
        $Value = $Value -shr 7
        if ($Value -ne 0) { $part = $part -bor 0x80 }
        $Stream.WriteByte($part)
    } while ($Value -ne 0)
}

function Convert-ProtobufMessage([byte[]]$Data) {
    $output = [IO.MemoryStream]::new()
    $offset = 0
    $changed = $false
    try {
        while ($offset -lt $Data.Length) {
            $key = Read-Varint $Data ([ref]$offset)
            if (($key -shr 3) -eq 0) { throw 'Invalid protobuf field number.' }
            Write-Varint $output $key
            switch ([int]($key -band 7)) {
                0 {
                    $value = Read-Varint $Data ([ref]$offset)
                    Write-Varint $output $value
                }
                1 {
                    if ($offset + 8 -gt $Data.Length) { throw 'Truncated fixed64.' }
                    $output.Write($Data, $offset, 8); $offset += 8
                }
                2 {
                    $length = [int](Read-Varint $Data ([ref]$offset))
                    if ($length -lt 0 -or $offset + $length -gt $Data.Length) { throw 'Truncated length-delimited field.' }
                    $payload = [byte[]]::new($length)
                    [Array]::Copy($Data, $offset, $payload, 0, $length); $offset += $length
                    $replacementBytes = $null
                    try {
                        $text = [Text.UTF8Encoding]::new($false, $true).GetString($payload)
                        $replacement = Convert-LinkString $text
                        if ($null -ne $replacement) { $replacementBytes = $utf8NoBom.GetBytes($replacement) }
                    } catch {}
                    if ($null -eq $replacementBytes -and $payload.Length -gt 0) {
                        $nested = Convert-ProtobufMessage $payload
                        if ($nested.Valid -and $nested.Changed) { $replacementBytes = $nested.Bytes }
                    }
                    if ($null -ne $replacementBytes) { $payload = [byte[]]$replacementBytes; $changed = $true }
                    Write-Varint $output ([uint64]$payload.Length)
                    $output.Write($payload, 0, $payload.Length)
                }
                5 {
                    if ($offset + 4 -gt $Data.Length) { throw 'Truncated fixed32.' }
                    $output.Write($Data, $offset, 4); $offset += 4
                }
                default { throw 'Unsupported protobuf wire type.' }
            }
        }
        return [pscustomobject]@{ Valid=$true; Changed=$changed; Bytes=$output.ToArray() }
    } catch {
        return [pscustomobject]@{ Valid=$false; Changed=$false; Bytes=$Data }
    } finally {
        $output.Dispose()
    }
}

try {
if (-not $SkipCodex) {
    $canonicalDb = Join-Path $CodexHome 'state_5.sqlite'
    if (Test-Path -LiteralPath $canonicalDb -PathType Leaf) {
        # Snapshot the database and its WAL/SHM sidecars before any SQLite read can checkpoint or rewrite them.
        Backup-File $canonicalDb 'codex-databases' -SqliteDatabase
    }
    if ([string]::IsNullOrWhiteSpace($NewProjectId)) {
        if ((Test-Path -LiteralPath $canonicalDb) -and (Test-SqliteTable $canonicalDb 'project_roots')) {
            $newRootQ = Sql-Quote $NewRoot
            $canonicalRows = @(& $sqlite -readonly $canonicalDb "SELECT DISTINCT project_id FROM project_roots WHERE replace(path,'\\?\','') COLLATE NOCASE=$newRootQ;")
            $canonicalIds = @($canonicalRows | ForEach-Object { ([string]$_).Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
            if ($canonicalIds.Count -eq 1) {
                $NewProjectId = [string]$canonicalIds[0]
            } elseif ($canonicalIds.Count -gt 1) {
                throw "Multiple current OpenNotes project IDs found in ${canonicalDb}: $($canonicalIds -join ', ')"
            }
        }
        if ([string]::IsNullOrWhiteSpace($NewProjectId)) {
            throw "No canonical project ID was found for the current OpenNotes root '$NewRoot'. Supply -NewProjectId only for an isolated fixture or an explicitly verified database."
        }
    }
    $rolloutRoots = @((Join-Path $CodexHome 'sessions'), (Join-Path $CodexHome 'archived_sessions'))
    foreach ($root in $rolloutRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.jsonl' | ForEach-Object {
            $path = $_.FullName
            $content = [IO.File]::ReadAllText($path)
            $newline = $content.IndexOf("`n")
            $firstEnd = if ($newline -ge 0) { $newline } else { $content.Length }
            $firstLine = $content.Substring(0, $firstEnd).TrimEnd("`r")
            try { $meta = $firstLine | ConvertFrom-Json } catch { return }
            if ($meta.type -ne 'session_meta') { return }
            $mapped = Convert-RootString ([string]$meta.payload.cwd)
            if ($null -eq $mapped) { return }
            if ($meta.payload.id) { [void]$affectedThreadIds.Add([string]$meta.payload.id) }
            Backup-File $path 'codex-rollouts'
            $oldEscaped = ([string]$meta.payload.cwd).Replace('\', '\\')
            $newEscaped = $mapped.Replace('\', '\\')
            $updatedFirst = $firstLine.Replace($oldEscaped, $newEscaped)
            if ($updatedFirst -eq $firstLine) { throw "Could not update rollout cwd without reserializing: $path" }
            $suffix = if ($newline -ge 0) { $content.Substring($newline) } else { '' }
            $bodyHashBefore = Get-ByteHashHex ($utf8NoBom.GetBytes($suffix))
            Write-AtomicUtf8 $path ($updatedFirst + $suffix)
            $after = [IO.File]::ReadAllText($path)
            $afterNewline = $after.IndexOf("`n")
            $afterSuffix = if ($afterNewline -ge 0) { $after.Substring($afterNewline) } else { '' }
            $bodyHashAfter = Get-ByteHashHex ($utf8NoBom.GetBytes($afterSuffix))
            if ($bodyHashBefore -ne $bodyHashAfter) { throw "Conversation body changed: $path" }
            $stats.CodexRollouts++
        }
    }

    $globalStatePath = Join-Path $CodexHome '.codex-global-state.json'
    if (Test-Path -LiteralPath $globalStatePath) {
        Backup-File $globalStatePath 'codex-state'
        $state = [IO.File]::ReadAllText($globalStatePath, [Text.Encoding]::UTF8) | ConvertFrom-Json
        $promptInvariant = if ($state.'prompt-history') { $state.'prompt-history' | ConvertTo-Json -Depth 100 -Compress } else { $null }
        $projects = $state.'local-projects'
        $oldProjectIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($id in $knownOldProjectIds) { [void]$oldProjectIds.Add($id) }
        if ($projects) {
            foreach ($property in @($projects.PSObject.Properties)) {
                $roots = @($property.Value.rootPaths)
                if ($roots | Where-Object { $null -ne (Convert-RootString ([string]$_)) }) { [void]$oldProjectIds.Add($property.Name) }
            }
            $newProject = $projects.PSObject.Properties[$NewProjectId].Value
            if (-not $newProject) {
                $newProject = [pscustomobject]@{ id=$NewProjectId; name='Caelum'; rootPaths=@($NewRoot); createdAt=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); updatedAt=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
                $projects.PSObject.Properties.Add([psnoteproperty]::new($NewProjectId, $newProject))
            } else {
                $newProject.name = 'Caelum'; $newProject.rootPaths = @($NewRoot)
                $updatedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
                if ($newProject.PSObject.Properties['updatedAt']) { $newProject.updatedAt = $updatedAt }
                else { $newProject.PSObject.Properties.Add([psnoteproperty]::new('updatedAt', $updatedAt)) }
            }
            foreach ($id in $oldProjectIds) {
                if ($id -ne $NewProjectId) {
                    if ($projects.PSObject.Properties[$id]) { [void]$removedCodexProjectIds.Add([string]$id) }
                    $projects.PSObject.Properties.Remove($id)
                }
            }
        }
        if ($state.'project-order') {
            $state.'project-order' = @($state.'project-order' | Where-Object { -not $oldProjectIds.Contains([string]$_) -and $_ -ne $NewProjectId }) + @($NewProjectId)
        }
        $assignments = $state.'thread-project-assignments'
        if (-not $assignments) {
            $assignments = [pscustomobject]@{}
            $state.PSObject.Properties.Add([psnoteproperty]::new('thread-project-assignments', $assignments))
        }
        foreach ($property in @($assignments.PSObject.Properties)) {
            if ($oldProjectIds.Contains([string]$property.Value.projectId)) { $property.Value.projectId = $NewProjectId; $property.Value.projectKind = 'local' }
        }
        foreach ($threadId in $affectedThreadIds) {
            $existing = $assignments.PSObject.Properties[$threadId]
            if ($existing) { $existing.Value.projectId = $NewProjectId; $existing.Value.projectKind = 'local' }
            else { $assignments.PSObject.Properties.Add([psnoteproperty]::new($threadId, [pscustomobject]@{ projectKind='local'; projectId=$NewProjectId })) }
        }
        foreach ($propertyName in @('active-workspace-roots','electron-saved-workspace-roots')) {
            if ($state.$propertyName) {
                $mapped = @($state.$propertyName | ForEach-Object { $value=Convert-RootString ([string]$_); if($null -ne $value){$value}else{$_} } | Select-Object -Unique)
                $state.$propertyName = $mapped
            }
        }
        foreach ($propertyName in @('thread-workspace-root-hints','thread-writable-roots')) {
            $container = $state.$propertyName
            if (-not $container) { continue }
            foreach ($property in @($container.PSObject.Properties)) {
                $property.Value = @($property.Value | ForEach-Object { $value=Convert-RootString ([string]$_); if($null -ne $value){$value}else{$_} } | Select-Object -Unique)
            }
        }
        if ($null -ne $promptInvariant -and $promptInvariant -ne ($state.'prompt-history' | ConvertTo-Json -Depth 100 -Compress)) { throw 'Codex prompt history changed in memory.' }
        Write-AtomicUtf8 $globalStatePath ($state | ConvertTo-Json -Depth 100 -Compress)
        $knownOldProjectIds = @($oldProjectIds)
    }

    $primaryDb = Join-Path $CodexHome 'state_5.sqlite'
    if ((Test-Path -LiteralPath $primaryDb) -and (Test-SqliteTable $primaryDb 'threads')) {
        Backup-File $primaryDb 'codex-databases' -SqliteDatabase
        $now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        $newQ = Sql-Quote $NewRoot; $projectQ = Sql-Quote $NewProjectId
        $oldPathList = (($oldRoots + $NewRoot | ForEach-Object { Sql-Quote $_ }) -join ',')
        $primaryOldProjectIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($knownId in $knownOldProjectIds) { [void]$primaryOldProjectIds.Add([string]$knownId) }
        if (Test-SqliteTable $primaryDb 'project_roots') {
            $legacyPathList = (($oldRoots | ForEach-Object { Sql-Quote $_ }) -join ',')
            $rootProjectRows = & $sqlite -readonly $primaryDb "SELECT DISTINCT project_id FROM project_roots WHERE replace(path,'\\?\','') COLLATE NOCASE IN ($legacyPathList);"
            foreach ($rootProjectRow in $rootProjectRows) {
                if (-not [string]::IsNullOrWhiteSpace([string]$rootProjectRow)) {
                    [void]$primaryOldProjectIds.Add([string]$rootProjectRow)
                }
            }
        }
        [void]$primaryOldProjectIds.Remove($NewProjectId)
        $oldIdList = (($primaryOldProjectIds | ForEach-Object { Sql-Quote $_ }) -join ',')
        if (-not $oldIdList) { $oldIdList = "''" }
        if (Test-SqliteTable $primaryDb 'projects' -and $oldIdList -ne "''") {
            $existingPrimaryProjectIds = & $sqlite -readonly $primaryDb "SELECT id FROM projects WHERE id IN ($oldIdList);"
            foreach ($existingPrimaryProjectId in $existingPrimaryProjectIds) {
                if (-not [string]::IsNullOrWhiteSpace([string]$existingPrimaryProjectId)) {
                    [void]$removedCodexProjectIds.Add([string]$existingPrimaryProjectId)
                }
            }
        }
        $sql = "BEGIN IMMEDIATE; INSERT INTO projects(id,name,metadata,position,created_at_ms,updated_at_ms) VALUES($projectQ,'Caelum','{}',0,$now,$now) ON CONFLICT(id) DO UPDATE SET name='Caelum',updated_at_ms=$now; UPDATE threads SET cwd=$newQ,project_id=$projectQ WHERE replace(cwd,'\\?\','') COLLATE NOCASE IN ($oldPathList) OR project_id IN ($oldIdList); DELETE FROM project_roots WHERE project_id IN ($oldIdList) OR project_id=$projectQ; INSERT INTO project_roots(project_id,position,path) VALUES($projectQ,0,$newQ); DELETE FROM projects WHERE id IN ($oldIdList); COMMIT;"
        Invoke-Sqlite $primaryDb $sql | Out-Null
        $stats.CodexDatabaseRows += [int](& $sqlite -readonly $primaryDb "SELECT count(*) FROM threads WHERE cwd=$newQ AND project_id=$projectQ;")
        if ((& $sqlite -readonly $primaryDb 'PRAGMA integrity_check;') -ne 'ok') { throw "Integrity check failed: $primaryDb" }
    }

    $legacyDb = Join-Path $CodexHome 'sqlite\state_5.sqlite'
    if ((Test-Path -LiteralPath $legacyDb) -and (Test-SqliteTable $legacyDb 'threads')) {
        $legacyHasCwd = Test-SqliteColumn $legacyDb 'threads' 'cwd'
        $legacyHasProjectId = Test-SqliteColumn $legacyDb 'threads' 'project_id'
        if (-not $legacyHasCwd) { throw "Legacy Codex database has no threads.cwd column: $legacyDb" }
        Backup-File $legacyDb 'codex-databases' -SqliteDatabase
        $newQ=Sql-Quote $NewRoot; $projectQ=Sql-Quote $NewProjectId; $oldPathList=(($oldRoots + $NewRoot | ForEach-Object { Sql-Quote $_ }) -join ',')
        $setClause = "cwd=$newQ"
        if ($legacyHasProjectId) { $setClause += ",project_id=$projectQ" }
        Invoke-Sqlite $legacyDb "BEGIN IMMEDIATE; UPDATE threads SET $setClause WHERE replace(cwd,'\\?\','') COLLATE NOCASE IN ($oldPathList); COMMIT;" | Out-Null
        if ((& $sqlite -readonly $legacyDb 'PRAGMA integrity_check;') -ne 'ok') { throw "Integrity check failed: $legacyDb" }
    }

    $catalogDb = Join-Path $CodexHome 'sqlite\codex-dev.db'
    if ((Test-Path -LiteralPath $catalogDb) -and (Test-SqliteTable $catalogDb 'local_thread_catalog')) {
        Backup-File $catalogDb 'codex-databases' -SqliteDatabase
        $newQ=Sql-Quote $NewRoot; $projectQ=Sql-Quote $NewProjectId; $oldPathList=(($oldRoots + $NewRoot | ForEach-Object { Sql-Quote $_ }) -join ','); $oldIdList=(($knownOldProjectIds | ForEach-Object { Sql-Quote $_ }) -join ','); if(-not $oldIdList){$oldIdList="''"}
        Invoke-Sqlite $catalogDb "BEGIN IMMEDIATE; UPDATE local_thread_catalog SET cwd=$newQ,project_id=$projectQ WHERE replace(cwd,'\\?\','') COLLATE NOCASE IN ($oldPathList) OR project_id IN ($oldIdList); COMMIT;" | Out-Null
        if ((& $sqlite -readonly $catalogDb 'PRAGMA integrity_check;') -ne 'ok') { throw "Integrity check failed: $catalogDb" }
    }
}

if (-not $SkipAntigravity) {
    foreach ($root in $GeminiRoots) {
        $conversationRoot = Join-Path $root 'conversations'
        if (-not (Test-Path -LiteralPath $conversationRoot)) { continue }
        Get-ChildItem -LiteralPath $conversationRoot -File -Filter '*.db' | ForEach-Object {
            $database = $_.FullName
            if (-not (Test-SqliteTable $database 'trajectory_metadata_blob')) { return }
            $rows = & $sqlite -readonly $database "SELECT id,hex(data) FROM trajectory_metadata_blob;"
            $changedRows = [System.Collections.Generic.List[object]]::new()
            foreach ($row in $rows) {
                $parts = $row -split '\|',2
                if ($parts.Count -ne 2 -or -not $parts[1]) { continue }
                $bytes = Convert-HexToBytes $parts[1]
                $converted = Convert-ProtobufMessage $bytes
                if (-not ($converted.Valid -and $converted.Changed)) { continue }
                $idQ=Sql-Quote $parts[0]; $hex=Convert-BytesToHex ([byte[]]$converted.Bytes)
                $changedRows.Add([pscustomobject]@{ Id=$idQ; Hex=$hex })
            }
            if ($changedRows.Count -eq 0) { return }
            Backup-File $database 'antigravity-conversations' -SqliteDatabase
            $statements = @('BEGIN IMMEDIATE')
            foreach ($changedRow in $changedRows) {
                $statements += "UPDATE trajectory_metadata_blob SET data=X'$($changedRow.Hex)' WHERE id=$($changedRow.Id)"
            }
            $statements += 'COMMIT'
            Invoke-Sqlite $database (($statements -join ';') + ';') | Out-Null
            if ((& $sqlite -readonly $database 'PRAGMA integrity_check;') -ne 'ok') { throw "Integrity check failed: $database" }
            $stats.AntigravityConversations += $changedRows.Count
        }
    }

    $globalKeys = @('history.recentlyOpenedPathsList','terminal.history.entries.dirs','vscode.git','vscode.github')
    $workspaceKeys = @('workbench.explorer.treeViewState','debug.selectedroot','terminal.integrated.environmentVariableCollectionsV2','scm:view:visibleRepositories','memento/workbench.parts.editor','history.entries','memento/workbench.editors.files.textFileEditor','scm.viewState2')
    foreach ($root in $AntigravityRoamingRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $workspaceDatabases = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $workspaceStorage = Join-Path $root 'User\workspaceStorage'
        if (Test-Path -LiteralPath $workspaceStorage) {
            Get-ChildItem -LiteralPath $workspaceStorage -Recurse -File -Filter 'workspace.json' | ForEach-Object {
                $jsonPath=$_.FullName; $raw=[IO.File]::ReadAllText($jsonPath, [Text.Encoding]::UTF8)
                try { $workspace=$raw|ConvertFrom-Json } catch { return }
                $mapped=Convert-LinkString ([string]$workspace.folder)
                $stateDb=Join-Path $_.DirectoryName 'state.vscdb'
                if ($null -ne $mapped) {
                    Backup-File $jsonPath 'antigravity-workspaces'
                    $workspace.folder=$mapped
                    Write-AtomicUtf8 $jsonPath ($workspace|ConvertTo-Json -Depth 20)
                    if(Test-Path -LiteralPath $stateDb){[void]$workspaceDatabases.Add($stateDb)}
                    $stats.AntigravityWorkspaceFiles++
                } elseif (Test-IsNewLinkString ([string]$workspace.folder)) {
                    if(Test-Path -LiteralPath $stateDb){[void]$workspaceDatabases.Add($stateDb)}
                }
            }
        }
        $stateTargets = [System.Collections.Generic.List[object]]::new()
        $globalDb=Join-Path $root 'User\globalStorage\state.vscdb'
        if(Test-Path -LiteralPath $globalDb){$stateTargets.Add([pscustomobject]@{Path=$globalDb;Keys=$globalKeys})}
        foreach($db in $workspaceDatabases){$stateTargets.Add([pscustomobject]@{Path=$db;Keys=$workspaceKeys})}
        foreach($target in $stateTargets){
            if(-not (Test-SqliteTable $target.Path 'ItemTable')){continue}
            $changedRows=[System.Collections.Generic.List[object]]::new()
            foreach($key in $target.Keys){
                $keyQ=Sql-Quote $key; $hex=& $sqlite -readonly $target.Path "SELECT hex(value) FROM ItemTable WHERE key=$keyQ;"
                if(-not $hex){continue}
                $value=$utf8NoBom.GetString((Convert-HexToBytes ($hex -join '')))
                $updated=Convert-StructuralText $value
                if($updated -ne $value){$changedRows.Add([pscustomobject]@{Key=$key;Value=$updated})}
            }
            if($changedRows.Count -eq 0){continue}
            Backup-File $target.Path 'antigravity-state' -SqliteDatabase
            $statements=@('BEGIN IMMEDIATE')
            foreach($row in $changedRows){
                $valueHex=Convert-BytesToHex ($utf8NoBom.GetBytes([string]$row.Value))
                $statements += "UPDATE ItemTable SET value=CAST(X'$valueHex' AS TEXT) WHERE key=$(Sql-Quote $row.Key)"
            }
            $statements += 'COMMIT'
            Invoke-Sqlite $target.Path (($statements -join ';') + ';') | Out-Null
            if ((& $sqlite -readonly $target.Path 'PRAGMA integrity_check;') -ne 'ok') { throw "Integrity check failed: $($target.Path)" }
            $stats.AntigravityStateValues += $changedRows.Count
        }
    }
}

foreach ($entry in $manifestEntries) {
    if (Test-Path -LiteralPath $entry.source) { $entry.sha256_after = Get-Sha256 $entry.source }
}
$validation = [ordered]@{
    status = 'passed'
    primary_old_root_project_roots = 0
    primary_old_root_threads = 0
    primary_old_project_rows = 0
    primary_unassigned_current_root_threads = 0
    legacy_old_root_threads = 0
    catalog_old_root_threads = 0
    catalog_unassigned_current_root_threads = 0
    global_old_project_records = 0
    global_old_project_references = 0
    global_old_root_paths = 0
    rollout_old_root_headers = 0
}
$oldPathSql = (($oldRoots | ForEach-Object { Sql-Quote $_ }) -join ',')
$validationPrimaryDb = Join-Path $CodexHome 'state_5.sqlite'
if ((Test-Path -LiteralPath $validationPrimaryDb) -and (Test-SqliteTable $validationPrimaryDb 'threads')) {
    $validation.primary_old_root_threads = [int](Get-SqliteScalar $validationPrimaryDb "SELECT count(*) FROM threads WHERE replace(cwd,'\\?\','') COLLATE NOCASE IN ($oldPathSql);")
    if (Test-SqliteColumn $validationPrimaryDb 'threads' 'project_id') {
        $validation.primary_unassigned_current_root_threads = [int](Get-SqliteScalar $validationPrimaryDb "SELECT count(*) FROM threads WHERE replace(cwd,'\\?\','') COLLATE NOCASE=$(Sql-Quote $NewRoot) AND (project_id IS NULL OR project_id='');")
    }
    if (Test-SqliteTable $validationPrimaryDb 'project_roots') {
        $validation.primary_old_root_project_roots = [int](Get-SqliteScalar $validationPrimaryDb "SELECT count(*) FROM project_roots WHERE replace(path,'\\?\','') COLLATE NOCASE IN ($oldPathSql);")
    }
    if (Test-SqliteTable $validationPrimaryDb 'projects' -and $primaryOldProjectIds) {
        $validationOldIds = @($primaryOldProjectIds | Where-Object { [string]$_ -ne $NewProjectId })
        if ($validationOldIds.Count -gt 0) {
            $validationOldIdSql = (($validationOldIds | ForEach-Object { Sql-Quote ([string]$_) }) -join ',')
            $validation.primary_old_project_rows = [int](Get-SqliteScalar $validationPrimaryDb "SELECT count(*) FROM projects WHERE id IN ($validationOldIdSql);")
        }
    }
}
$validationLegacyDb = Join-Path $CodexHome 'sqlite\state_5.sqlite'
if ((Test-Path -LiteralPath $validationLegacyDb) -and (Test-SqliteTable $validationLegacyDb 'threads') -and (Test-SqliteColumn $validationLegacyDb 'threads' 'cwd')) {
    $validation.legacy_old_root_threads = [int](Get-SqliteScalar $validationLegacyDb "SELECT count(*) FROM threads WHERE replace(cwd,'\\?\','') COLLATE NOCASE IN ($oldPathSql);")
}
$validationCatalogDb = Join-Path $CodexHome 'sqlite\codex-dev.db'
if ((Test-Path -LiteralPath $validationCatalogDb) -and (Test-SqliteTable $validationCatalogDb 'local_thread_catalog')) {
    $validation.catalog_old_root_threads = [int](Get-SqliteScalar $validationCatalogDb "SELECT count(*) FROM local_thread_catalog WHERE replace(cwd,'\\?\','') COLLATE NOCASE IN ($oldPathSql);")
    if (Test-SqliteColumn $validationCatalogDb 'local_thread_catalog' 'project_id') {
        $validation.catalog_unassigned_current_root_threads = [int](Get-SqliteScalar $validationCatalogDb "SELECT count(*) FROM local_thread_catalog WHERE replace(cwd,'\\?\','') COLLATE NOCASE=$(Sql-Quote $NewRoot) AND (project_id IS NULL OR project_id='');")
    }
}
if ($state) {
    $globalProjects = $state.'local-projects'
    if ($globalProjects) {
        foreach ($property in @($globalProjects.PSObject.Properties)) {
            $isOldId = $false
            if ($oldProjectIds) { $isOldId = $oldProjectIds.Contains([string]$property.Name) -and ([string]$property.Name -ne $NewProjectId) }
            $isOldRoot = $false
            foreach ($rootPath in @($property.Value.rootPaths)) {
                if ($null -ne (Convert-RootString ([string]$rootPath))) { $isOldRoot = $true; $validation.global_old_root_paths++ }
            }
            if ($isOldId -or $isOldRoot) { $validation.global_old_project_records++ }
        }
    }
    foreach ($projectId in @($state.'project-order')) {
        if ($oldProjectIds -and $oldProjectIds.Contains([string]$projectId) -and [string]$projectId -ne $NewProjectId) { $validation.global_old_project_references++ }
    }
    $updatedAssignments = $state.'thread-project-assignments'
    if ($updatedAssignments) {
        foreach ($assignment in @($updatedAssignments.PSObject.Properties)) {
            $projectId = [string]$assignment.Value.projectId
            if ($oldProjectIds -and $oldProjectIds.Contains($projectId) -and $projectId -ne $NewProjectId) { $validation.global_old_project_references++ }
        }
    }
    foreach ($propertyName in @('active-workspace-roots','electron-saved-workspace-roots')) {
        foreach ($rootPath in @($state.$propertyName)) {
            if ($null -ne (Convert-RootString ([string]$rootPath))) { $validation.global_old_root_paths++ }
        }
    }
}
foreach ($root in @((Join-Path $CodexHome 'sessions'), (Join-Path $CodexHome 'archived_sessions'))) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    foreach ($rollout in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.jsonl')) {
        $lines = [IO.File]::ReadAllLines($rollout.FullName)
        if ($lines.Count -eq 0) { continue }
        try { $rolloutMeta = $lines[0] | ConvertFrom-Json } catch { continue }
        if ($rolloutMeta.type -eq 'session_meta' -and $null -ne (Convert-RootString ([string]$rolloutMeta.payload.cwd))) {
            $validation.rollout_old_root_headers++
        }
    }
}
$validationFailures = @($validation.GetEnumerator() | Where-Object { $_.Key -ne 'status' -and [int]$_.Value -gt 0 })
if ($validationFailures.Count -gt 0) {
    throw "Migration validation failed: $((($validationFailures | ForEach-Object { $_.Key + '=' + $_.Value }) -join ', '))"
}
$stats.CodexProjectsRemoved = $removedCodexProjectIds.Count
$manifest = [ordered]@{
    created_at = [DateTimeOffset]::UtcNow.ToString('o')
    new_root = $NewRoot
    new_project_id = $NewProjectId
    sqlite = [ordered]@{ path = $sqlite; sha256 = $sqliteHash; version = $sqliteVersion }
    validation = $validation
    stats = $stats
    files = $manifestEntries
}
$manifestPath = Join-Path $runBackup 'manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 20), $utf8NoBom)
Write-Output ($manifest | ConvertTo-Json -Depth 20 -Compress)
}
catch {
    $rollbackErrors = [System.Collections.Generic.List[string]]::new()
    $restoredSources = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $manifestEntries) {
        if (-not $restoredSources.Add([string]$entry.source)) { continue }
        try {
            if ($entry.exists_before -eq $false) {
                if (Test-Path -LiteralPath $entry.source -PathType Leaf) {
                    Remove-Item -LiteralPath $entry.source -Force
                }
                continue
            }
            if ([string]::IsNullOrWhiteSpace([string]$entry.backup) -or -not (Test-Path -LiteralPath $entry.backup -PathType Leaf)) {
                throw "Backup file is missing: $($entry.backup)"
            }
            Copy-Item -LiteralPath $entry.backup -Destination $entry.source -Force
        }
        catch {
            $rollbackErrors.Add("$($entry.source): $($_.Exception.Message)")
        }
    }
    if ($rollbackErrors.Count -gt 0) {
        throw "Migration failed and rollback also failed: $($rollbackErrors -join ' | ')"
    }
    throw
}
finally {
    if ($mutexHeld) {
        try { $migrationMutex.ReleaseMutex() } finally {
            $migrationMutex.Dispose()
            $mutexHeld = $false
        }
    }
}
