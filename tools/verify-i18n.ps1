[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Root
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$script:Root = [System.IO.Path]::GetFullPath($Root)
$script:Issues = New-Object System.Collections.ArrayList

function Add-Issue {
    param([string]$Message)

    [void]$script:Issues.Add($Message)
}

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title ==" -ForegroundColor Cyan
}

function Get-RelativePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($script:Root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($script:Root.Length).TrimStart([char[]]@('\', '/')) -replace '\\', '/'
    }

    return $fullPath -replace '\\', '/'
}

function Get-LineNumber {
    param(
        [string]$Text,
        [int]$Index
    )

    if ($Index -le 0) {
        return 1
    }

    return ([regex]::Matches($Text.Substring(0, $Index), "`n").Count + 1)
}

function Convert-CSharpString {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return $null
    }

    $builder = New-Object System.Text.StringBuilder
    for ($index = 0; $index -lt $Value.Length; $index++) {
        $current = $Value[$index]
        if ($current -ne '\') {
            [void]$builder.Append($current)
            continue
        }

        if (($index + 1) -ge $Value.Length) {
            [void]$builder.Append('\')
            continue
        }

        $index++
        $escape = $Value[$index]
        switch ($escape) {
            '0' { [void]$builder.Append([char]0); break }
            'a' { [void]$builder.Append([char]7); break }
            'b' { [void]$builder.Append([char]8); break }
            'f' { [void]$builder.Append([char]12); break }
            'n' { [void]$builder.Append("`n"); break }
            'r' { [void]$builder.Append("`r"); break }
            't' { [void]$builder.Append("`t"); break }
            'v' { [void]$builder.Append([char]11); break }
            '\' { [void]$builder.Append('\'); break }
            '"' { [void]$builder.Append('"'); break }
            'u' {
                if (($index + 4) -lt $Value.Length) {
                    $hex = $Value.Substring($index + 1, 4)
                    [int]$codePoint = 0
                    if ([int]::TryParse(
                        $hex,
                        [System.Globalization.NumberStyles]::HexNumber,
                        [System.Globalization.CultureInfo]::InvariantCulture,
                        [ref]$codePoint)) {
                        [void]$builder.Append([char]$codePoint)
                        $index += 4
                        break
                    }
                }

                [void]$builder.Append('\u')
                break
            }
            'x' {
                $remaining = $Value.Length - ($index + 1)
                $length = [Math]::Min(4, $remaining)
                $parsed = $false
                for ($hexLength = $length; $hexLength -ge 1; $hexLength--) {
                    $hex = $Value.Substring($index + 1, $hexLength)
                    [int]$codePoint = 0
                    if ([int]::TryParse(
                        $hex,
                        [System.Globalization.NumberStyles]::HexNumber,
                        [System.Globalization.CultureInfo]::InvariantCulture,
                        [ref]$codePoint)) {
                        [void]$builder.Append([char]$codePoint)
                        $index += $hexLength
                        $parsed = $true
                        break
                    }
                }

                if (-not $parsed) {
                    [void]$builder.Append('\x')
                }
                break
            }
            default { [void]$builder.Append($escape); break }
        }
    }

    return $builder.ToString()
}

function Get-Placeholders {
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        return @()
    }

    return @(
        [regex]::Matches($Text, '\{([0-9]+)\}') |
            ForEach-Object { $_.Groups[1].Value } |
            Sort-Object -Unique
    )
}

function Format-Set {
    param([AllowNull()][object[]]$Values)

    $items = @($Values)
    if ($items.Count -eq 0) {
        return '(none)'
    }

    return ($items -join ', ')
}

function Get-StringLiterals {
    param([string]$Line)

    $literals = New-Object System.Collections.ArrayList
    $normalPattern = '(?<!@)(?<!\\)"(?<value>(?:\\.|[^"\\])*)"'
    foreach ($match in ([regex]::Matches($Line, $normalPattern))) {
        [void]$literals.Add([pscustomobject]@{
                Raw   = $match.Value
                Value = $match.Groups['value'].Value
                Index = $match.Index
            })
    }

    $verbatimPattern = '@"(?<value>[^"]*)"'
    foreach ($match in ([regex]::Matches($Line, $verbatimPattern))) {
        [void]$literals.Add([pscustomobject]@{
                Raw   = $match.Value
                Value = $match.Groups['value'].Value
                Index = $match.Index
            })
    }

    return @($literals | Sort-Object Index)
}

function Get-VisibleStringLiterals {
    param(
        [string]$Line,
        [bool]$IsXaml
    )

    $literals = New-Object System.Collections.ArrayList
    if ($IsXaml) {
        $attributePattern = '(?i)\b(?:Text|Content|Header|ToolTip|Title|Watermark|PlaceholderText|AutomationProperties\.Name)\s*=\s*"(?<value>[^"]*)"'
        foreach ($match in ([regex]::Matches($Line, $attributePattern))) {
            [void]$literals.Add([pscustomobject]@{
                    Raw   = $match.Value
                    Value = $match.Groups['value'].Value
                    Index = $match.Index
                })
        }

        $elementTextPattern = '(?i)>\s*(?<value>[A-Za-z\u00C0-\u024F\u3400-\u9FFF][^<]*)</'
        foreach ($match in ([regex]::Matches($Line, $elementTextPattern))) {
            [void]$literals.Add([pscustomobject]@{
                    Raw   = $match.Value
                    Value = $match.Groups['value'].Value
                    Index = $match.Index
                })
        }
    }
    else {
        $propertyPattern = '(?i)\b(?:Text|Content|Header|ToolTip|Title|Watermark|PlaceholderText|Description|Caption|Label|Message)\s*=\s*(?<literal>\$?"(?:\\.|[^"\\])*"|@"[^"]*")'
        foreach ($match in ([regex]::Matches($Line, $propertyPattern))) {
            $raw = $match.Groups['literal'].Value
            $value = $raw
            if ($raw.StartsWith('@"')) {
                $value = $raw.Substring(2, $raw.Length - 3)
            }
            else {
                $value = $raw.TrimStart('$').Substring(1, $raw.TrimStart('$').Length - 2)
            }
            [void]$literals.Add([pscustomobject]@{
                    Raw   = $raw
                    Value = $value
                    Index = $match.Index
                })
        }

        $methodPattern = '(?i)\b(?:MessageBox\.Show|ShowToast|ShowDialog(?:Async)?|ShowErrorAsync|CreateMenuItem|Make(?:Shape|Filter)Button)\s*\('
        foreach ($method in ([regex]::Matches($Line, $methodPattern))) {
            $tail = $Line.Substring($method.Index)
            foreach ($literal in (Get-StringLiterals -Line $tail)) {
                [void]$literals.Add($literal)
            }
        }
    }

    return @($literals | Sort-Object Index)
}

function Test-PathLikeString {
    param([string]$Value)

    if ($Value -match '^(?i)(?:https?|mailto|file|pack|urn):') {
        return $true
    }

    if ($Value -match '^[A-Za-z]:[\\/]') {
        return $true
    }

    if ($Value -match '[\\/]') {
        return $true
    }

    if ($Value -match '(?i)(?:^|\s|\|)\*?\.[A-Za-z0-9]{1,8}(?:$|\s|\|)') {
        return $true
    }

    if ($Value -match '(?i)\.(?:json|pdf|png|jpe?g|gif|bmp|ico|dll|exe|cs|xaml|config|txt|zip)$') {
        return $true
    }

    return $false
}

function Test-PrivateUseOnly {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return $false
    }

    foreach ($character in $Value.ToCharArray()) {
        $code = [int][char]$character
        if (($code -lt 0xE000 -or $code -gt 0xF8FF) -and
            ($code -lt 0xF0000 -or $code -gt 0xFFFFD) -and
            ($code -lt 0x100000 -or $code -gt 0x10FFFD)) {
            return $false
        }
    }

    return $true
}

function Test-ContainsLetter {
    param([string]$Value)

    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsLetter($character)) {
            return $true
        }
    }

    return $false
}

function Test-VisibleContext {
    param(
        [string]$Line,
        [bool]$IsXaml
    )

    if ($IsXaml) {
        return ($Line -match '(?i)\b(?:Text|Content|Header|ToolTip|Title|Watermark|PlaceholderText|AutomationProperties\.Name)\s*=') -or
            ($Line -match '(?i)>\s*[A-Za-z\u00C0-\u024F\u3400-\u9FFF][^<]*</')
    }

    return ($Line -match '(?i)\b(?:Text|Content|Header|ToolTip|Title|Watermark|PlaceholderText|Description|Caption|Label|Message)\s*=') -or
        ($Line -match '(?i)\b(?:MessageBox\.Show|ShowToast|ShowDialog(?:Async)?|ShowErrorAsync|CreateMenuItem|Make(?:Shape|Filter)Button)\s*\(')
}

function Test-IgnoredVisibleLiteral {
    param(
        [string]$Value,
        [string]$Raw,
        [string]$Line,
        [bool]$IsXaml
    )

    $trimmed = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $true
    }

    if ($trimmed.StartsWith('{') -or $trimmed.StartsWith('#')) {
        return $true
    }

    if ($trimmed -match '^(?i)(?:&#x[0-9A-F]+;|&#[0-9]+;)+$') {
        return $true
    }

    if ($trimmed.Length -eq 1 -and $trimmed -match '^[A-Za-z]$') {
        return $true
    }

    if ($trimmed -match '^[dMyHhmsfFztKgy/ :.,-]+$') {
        return $true
    }

    if ($trimmed -match '^(?i)(?:OpenNotes|Caelum)$') {
        return $true
    }

    if (Test-PathLikeString -Value $trimmed) {
        return $true
    }

    if (Test-PrivateUseOnly -Value $trimmed) {
        return $true
    }

    if (-not (Test-ContainsLetter -Value $trimmed)) {
        return $true
    }

    if ($Line -match '(?i)(?:Console\.|Debug\.|Trace\.|Logger|\bLog(?:ger)?\b|Write(?:Line|Error|Verbose|Warning|Host|Debug)\b|throw\s+new|Exception|File\.|Directory\.|JsonSerializer|Serialize|Deserialize|Registry\.)') {
        return $true
    }

    if (-not $IsXaml -and $Line -match '(?i)\b(?:nameof|Argument(?:Null|OutOfRange)|StringComparison|Environment\.|Path\.)\b') {
        return $true
    }

    return $false
}

function Get-SourceFiles {
    param([string]$StartDirectory)

    $excludedDirectoryNames = @(
        '.ai', '.agents', '.arts', '.codegraph', '.codex', '.git', '.nuget',
        '.trae', '.vscode', '.vs', 'artifacts', 'bin', 'obj', 'publish',
        'tools', 'OpenNotes.Tests'
    )
    $pendingDirectories = New-Object System.Collections.Stack
    $pendingDirectories.Push($StartDirectory)

    while ($pendingDirectories.Count -gt 0) {
        $directory = [string]$pendingDirectories.Pop()
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -File -Force -ErrorAction SilentlyContinue)) {
            if ($file.Extension -in @('.cs', '.xaml') -and
                $file.Name -notmatch '\.g\.cs$' -and
                $file.Name -notmatch '^(?i)test_') {
                Write-Output $file
            }
        }

        foreach ($childDirectory in @(Get-ChildItem -LiteralPath $directory -Directory -Force -ErrorAction SilentlyContinue)) {
            if ($excludedDirectoryNames -notcontains $childDirectory.Name) {
                $pendingDirectories.Push($childDirectory.FullName)
            }
        }
    }
}

Write-Host "OpenNotes i18n verification" -ForegroundColor White
Write-Host "Root: $script:Root"

if (-not (Test-Path -LiteralPath $script:Root -PathType Container)) {
    Add-Issue "Repository root does not exist: $script:Root"
}

$catalogPath = Join-Path $script:Root 'Services\LocalizationService.cs'
$catalog = @{}
$catalogSource = $null

Write-Section 'Catalog'
if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
    Add-Issue "Missing catalog file: $(Get-RelativePath $catalogPath)"
}
else {
    $catalogSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $catalogPath
    $entryPattern = '(?m)^\s*\["(?<key>(?:\\.|[^"\\])*)"\]\s*=\s*\(\s*"(?<english>(?:\\.|[^"\\])*)"\s*,\s*"(?<chinese>(?:\\.|[^"\\])*)"\s*,\s*"(?<french>(?:\\.|[^"\\])*)"\s*\)\s*,?'
    [regex]$entryRegex = $entryPattern
    $entryMatches = @($entryRegex.Matches($catalogSource))
    $declaredEntries = @([regex]::Matches($catalogSource, '(?m)^\s*\["'))

    if ($entryMatches.Count -eq 0) {
        Add-Issue "No catalog entries could be parsed from $(Get-RelativePath $catalogPath)"
    }

    if ($declaredEntries.Count -ne $entryMatches.Count) {
        Add-Issue "Catalog parser read $($entryMatches.Count) complete entries but found $($declaredEntries.Count) entry declarations; check catalog syntax"
    }

    foreach ($entry in $entryMatches) {
        $key = Convert-CSharpString $entry.Groups['key'].Value
        $english = Convert-CSharpString $entry.Groups['english'].Value
        $chinese = Convert-CSharpString $entry.Groups['chinese'].Value
        $french = Convert-CSharpString $entry.Groups['french'].Value
        $lineNumber = Get-LineNumber -Text $catalogSource -Index $entry.Index

        if ([string]::IsNullOrWhiteSpace($key)) {
            Add-Issue "$(Get-RelativePath $catalogPath):$lineNumber has an empty localization key"
        }

        foreach ($language in @(
                [pscustomobject]@{ Name = 'English'; Value = $english },
                [pscustomobject]@{ Name = 'Chinese'; Value = $chinese },
                [pscustomobject]@{ Name = 'French'; Value = $french })) {
            if ([string]::IsNullOrWhiteSpace($language.Value)) {
                Add-Issue "$(Get-RelativePath $catalogPath):$lineNumber key '$key' has an empty $($language.Name) translation"
            }
        }

        $englishPlaceholders = @(Get-Placeholders $english)
        $chinesePlaceholders = @(Get-Placeholders $chinese)
        $frenchPlaceholders = @(Get-Placeholders $french)
        $placeholderSignature = @(
            (Format-Set $englishPlaceholders),
            (Format-Set $chinesePlaceholders),
            (Format-Set $frenchPlaceholders)
        )
        $uniquePlaceholderSignatures = @($placeholderSignature | Sort-Object -Unique)
        if ($uniquePlaceholderSignatures.Count -ne 1) {
            Add-Issue "$(Get-RelativePath $catalogPath):$lineNumber key '$key' has inconsistent placeholders (English: $(Format-Set $englishPlaceholders); Chinese: $(Format-Set $chinesePlaceholders); French: $(Format-Set $frenchPlaceholders))"
        }

        if ($catalog.ContainsKey($key)) {
            Add-Issue "$(Get-RelativePath $catalogPath):$lineNumber duplicates localization key '$key'"
        }
        else {
            $catalog[$key] = [pscustomobject]@{
                English = $english
                Chinese = $chinese
                French  = $french
            }
        }
    }
}

Write-Host "Catalog entries parsed: $($catalog.Count)"

Write-Section 'LocalizationService.Get/Format call keys'
$sourceFiles = @(Get-SourceFiles -StartDirectory $script:Root)

$callPattern = 'LocalizationService\s*\.\s*(?:Get|Format)\s*\(\s*(?:(?<argument>"(?:\\.|[^"\\])*")|(?<dynamic>[^,\)\r\n]+))'
[regex]$callRegex = $callPattern
$callCount = 0

foreach ($sourceFile in $sourceFiles) {
    $relativePath = Get-RelativePath $sourceFile.FullName
    try {
        $source = Get-Content -Raw -Encoding UTF8 -LiteralPath $sourceFile.FullName
    }
    catch {
        Add-Issue "Unable to read $relativePath for localization call scanning: $($_.Exception.Message)"
        continue
    }

    foreach ($call in @($callRegex.Matches($source))) {
        $callCount++
        $lineNumber = Get-LineNumber -Text $source -Index $call.Index
        if ($call.Groups['argument'].Success) {
            $rawKey = $call.Groups['argument'].Value
            $key = Convert-CSharpString $rawKey.Substring(1, $rawKey.Length - 2)
            if (-not $catalog.ContainsKey($key)) {
                Add-Issue "${relativePath}:$lineNumber calls LocalizationService.Get/Format with missing key '$key'"
            }
        }
        else {
            $dynamicValue = $call.Groups['dynamic'].Value.Trim()
            Add-Issue "${relativePath}:$lineNumber has a non-literal LocalizationService.Get/Format key '$dynamicValue'"
        }
    }
}

Write-Host "Localization calls checked: $callCount"

Write-Section 'Hard-coded visible strings'
$hardcodedCount = 0
foreach ($sourceFile in $sourceFiles) {
    if ($sourceFile.FullName -eq $catalogPath) {
        continue
    }

    $relativePath = Get-RelativePath $sourceFile.FullName
    $isXaml = $sourceFile.Extension -ieq '.xaml'
    try {
        $lines = @(Get-Content -Encoding UTF8 -LiteralPath $sourceFile.FullName)
    }
    catch {
        Add-Issue "Unable to read $relativePath for hard-coded string scanning: $($_.Exception.Message)"
        continue
    }

    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $line = [string]$lines[$lineIndex]
        $trimmedLine = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmedLine) -or
            $trimmedLine.StartsWith('//') -or
            $trimmedLine.StartsWith('/*') -or
            $trimmedLine.StartsWith('*') -or
            $trimmedLine.StartsWith('<!--') -or
            $trimmedLine.StartsWith('#')) {
            continue
        }

        if ($line -notmatch '(?i)LocalizationService\s*\.\s*(?:Get|Format)\s*\(') {
            foreach ($literal in (Get-VisibleStringLiterals -Line $line -IsXaml $isXaml)) {
                $value = Convert-CSharpString $literal.Value
                if (Test-IgnoredVisibleLiteral -Value $value -Raw $literal.Raw -Line $line -IsXaml $isXaml) {
                    continue
                }

                $hardcodedCount++
                $displayValue = ($value -replace "`r", '\\r' -replace "`n", '\\n' -replace "`t", '\\t').Trim()
                Add-Issue "${relativePath}:$($lineIndex + 1) contains hard-coded visible text '$displayValue'"
            }
        }
    }
}

Write-Host "Hard-coded visible strings found: $hardcodedCount"

if ($script:Issues.Count -eq 0) {
    Write-Host "`nPASS: i18n verification found no issues." -ForegroundColor Green
    exit 0
}

Write-Host "`nFAIL: $($script:Issues.Count) issue(s) found." -ForegroundColor Red
foreach ($issue in $script:Issues) {
    Write-Host " - $issue" -ForegroundColor Red
}
exit 1
