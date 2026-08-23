[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Root,

    [string]$WebsiteDirectory = 'website',

    [string]$WorkflowPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$script:Root = [System.IO.Path]::GetFullPath($Root)
$script:WebsiteRoot = [System.IO.Path]::GetFullPath((Join-Path $script:Root $WebsiteDirectory))
$script:Issues = New-Object System.Collections.ArrayList
$script:ResourceReferences = New-Object System.Collections.ArrayList

function Add-Issue {
    param([string]$Message)

    [void]$script:Issues.Add($Message)
}

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "== $Title ==" -ForegroundColor Cyan
}

function Test-PathWithin {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$CandidatePath
    )

    $base = [System.IO.Path]::GetFullPath($BasePath).TrimEnd([char[]]@('\', '/'))
    $candidate = [System.IO.Path]::GetFullPath($CandidatePath)
    return $candidate.Equals($base, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith($base + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith($base + '/', [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (Test-PathWithin -BasePath $script:Root -CandidatePath $fullPath) {
        return $fullPath.Substring($script:Root.Length).TrimStart([char[]]@('\', '/')) -replace '\\', '/'
    }

    return $fullPath -replace '\\', '/'
}

function Get-WebsiteRelativePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (Test-PathWithin -BasePath $script:WebsiteRoot -CandidatePath $fullPath) {
        return $fullPath.Substring($script:WebsiteRoot.Length).TrimStart([char[]]@('\', '/')) -replace '\\', '/'
    }

    return $fullPath -replace '\\', '/'
}

function Test-IsExternalReference {
    param([string]$Reference)

    return $Reference -match '^(?i)(?:https?:|mailto:|tel:|data:|javascript:|//)'
}

function Get-ReferencePath {
    param([string]$Reference)

    $clean = $Reference.Trim()
    $queryIndex = $clean.IndexOf('?')
    if ($queryIndex -ge 0) {
        $clean = $clean.Substring(0, $queryIndex)
    }

    $fragmentIndex = $clean.IndexOf('#')
    if ($fragmentIndex -ge 0) {
        $clean = $clean.Substring(0, $fragmentIndex)
    }

    return $clean
}

function Add-ResourceReference {
    param(
        [string]$BaseFile,
        [string]$Reference,
    [string]$Kind
    )

    $trimmedReference = $Reference.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmedReference) -or $trimmedReference -eq '#') {
        return
    }

    # Template literals are assembled at runtime (the demo probes optional
    # artwork by filename), so there is no single file path for the static
    # checker to resolve here.
    if ($trimmedReference -match '\$\{') {
        return
    }

    $resourcePath = Get-ReferencePath -Reference $trimmedReference
    if ([string]::IsNullOrWhiteSpace($resourcePath)) {
        return
    }

    $isExternal = Test-IsExternalReference -Reference $resourcePath
    $isAsset = $Kind -ne 'html-href'
    if ($isExternal) {
        if ($isAsset) {
            Add-Issue "$(Get-RelativePath $BaseFile) has an external $Kind '$trimmedReference'; website assets must use relative paths"
        }
        return
    }

    if ($resourcePath.StartsWith('/')) {
        Add-Issue "$(Get-RelativePath $BaseFile) has a root-relative $Kind '$trimmedReference'; use a path relative to the website file"
        return
    }

    $candidatePath = $null
    try {
        $candidatePath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $BaseFile) $resourcePath))
    }
    catch {
        Add-Issue "$(Get-RelativePath $BaseFile) has an invalid $Kind '$trimmedReference': $($_.Exception.Message)"
        return
    }

    if (-not (Test-PathWithin -BasePath $script:WebsiteRoot -CandidatePath $candidatePath)) {
        Add-Issue "$(Get-RelativePath $BaseFile) resource '$trimmedReference' escapes the website directory"
        return
    }

    $pathExists = Test-Path -LiteralPath $candidatePath -PathType Leaf
    if ($Kind -eq 'html-href') {
        # HTML navigation may intentionally target a directory, such as './'
        # at the website root. Resource files still require a regular file.
        $pathExists = $pathExists -or (Test-Path -LiteralPath $candidatePath -PathType Container)
    }

    if (-not $pathExists) {
        Add-Issue "$(Get-RelativePath $BaseFile) references missing $Kind '$trimmedReference'"
    }

    [void]$script:ResourceReferences.Add([pscustomobject]@{
            File      = $BaseFile
            Reference = $trimmedReference
            Path      = $resourcePath
            Kind      = $Kind
        })
}

function Get-RegexReferences {
    param(
        [string]$Content,
        [string]$Pattern,
        [string]$GroupName
    )

    $regex = [regex]$Pattern
    return @(
        $regex.Matches($Content) |
            ForEach-Object { $_.Groups[$GroupName].Value }
    )
}

function Get-DocumentedPlaceholderNames {
    param([string]$ReadmePath)

    if (-not (Test-Path -LiteralPath $ReadmePath -PathType Leaf)) {
        return @()
    }

    $readme = Get-Content -Raw -Encoding UTF8 -LiteralPath $ReadmePath
    $pattern = '(?i)(?<![A-Za-z0-9_-])(?<name>[A-Za-z0-9][A-Za-z0-9._-]*\.(?:svg|png|jpe?g|gif|webp|avif))(?![A-Za-z0-9_-])'
    return @(
        [regex]::Matches($readme, $pattern) |
            ForEach-Object { $_.Groups['name'].Value } |
            Sort-Object -Unique
    )
}

Write-Host "OpenNotes website verification" -ForegroundColor White
Write-Host "Root: $script:Root"
Write-Host "Website: $(Get-RelativePath $script:WebsiteRoot)"

if (-not (Test-Path -LiteralPath $script:Root -PathType Container)) {
    Add-Issue "Repository root does not exist: $script:Root"
}

Write-Section 'Required website files'
$requiredFiles = @(
    (Join-Path $WebsiteDirectory 'index.html'),
    (Join-Path $WebsiteDirectory '404.html'),
    (Join-Path $WebsiteDirectory 'theme.css'),
    (Join-Path $WebsiteDirectory 'content.js'),
    (Join-Path $WebsiteDirectory 'demo.js'),
    (Join-Path $WebsiteDirectory '.nojekyll'),
    (Join-Path $WebsiteDirectory 'assets\favicon.svg'),
    (Join-Path $WebsiteDirectory 'assets\favicon.ico'),
    (Join-Path $WebsiteDirectory 'assets\favicon-96x96.png'),
    (Join-Path $WebsiteDirectory 'assets\apple-touch-icon.png'),
    (Join-Path $WebsiteDirectory 'assets\web-app-manifest-192x192.png'),
    (Join-Path $WebsiteDirectory 'assets\web-app-manifest-512x512.png'),
    (Join-Path $WebsiteDirectory 'assets\site.webmanifest'),
    (Join-Path $WebsiteDirectory 'assets\opennotes-logo.png'),
    (Join-Path $WebsiteDirectory 'assets\placeholders\README.md')
)

$existingRequiredFiles = New-Object System.Collections.ArrayList
foreach ($relativeFile in $requiredFiles) {
    $fullPath = Join-Path $script:Root $relativeFile
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        [void]$existingRequiredFiles.Add((Get-Item -LiteralPath $fullPath -Force))
        Write-Host "OK   $relativeFile" -ForegroundColor Green
    }
    else {
        Add-Issue "Missing required website file: $relativeFile"
        Write-Host "MISS $relativeFile" -ForegroundColor Red
    }
}

if (-not (Test-Path -LiteralPath $script:WebsiteRoot -PathType Container)) {
    Add-Issue "Missing website directory: $(Get-RelativePath $script:WebsiteRoot)"
}

Write-Section 'GitHub Pages workflow'
$workflowFiles = @()
$workflowsDirectory = Join-Path $script:Root '.github\workflows'
if (Test-Path -LiteralPath $workflowsDirectory -PathType Container) {
    $workflowFiles = @(
        Get-ChildItem -LiteralPath $workflowsDirectory -File -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in @('.yml', '.yaml') }
    )
}

$selectedWorkflow = $null
if (-not [string]::IsNullOrWhiteSpace($WorkflowPath)) {
    $requestedWorkflow = [System.IO.Path]::GetFullPath((Join-Path $script:Root $WorkflowPath))
    if (-not (Test-PathWithin -BasePath $workflowsDirectory -CandidatePath $requestedWorkflow)) {
        Add-Issue "Workflow path must be under .github/workflows: $WorkflowPath"
    }
    elseif (-not (Test-Path -LiteralPath $requestedWorkflow -PathType Leaf)) {
        Add-Issue "Missing requested website workflow: $WorkflowPath"
    }
    else {
        $selectedWorkflow = Get-Item -LiteralPath $requestedWorkflow
    }
}
else {
    foreach ($workflowFile in $workflowFiles) {
        $workflowContent = Get-Content -Raw -Encoding UTF8 -LiteralPath $workflowFile.FullName
        if ($workflowContent -match '(?i)(?:website[\\/]|path\s*:\s*["'']?\.?[\\/]?website(?:[\\/\s"'']|$)|upload-pages-artifact|deploy-pages|actions-gh-pages)') {
            if ($null -eq $selectedWorkflow) {
                $selectedWorkflow = $workflowFile
            }
            else {
                Write-Host "INFO Multiple website-related workflows found; validating $($selectedWorkflow.Name) and $($workflowFile.Name)" -ForegroundColor Yellow
            }
        }
    }

    if ($null -eq $selectedWorkflow) {
        Add-Issue "No workflow under .github/workflows explicitly references the website directory"
    }
}

if ($null -ne $selectedWorkflow) {
    $workflowRelativePath = Get-RelativePath $selectedWorkflow.FullName
    Write-Host "Workflow: $workflowRelativePath" -ForegroundColor Green
    $workflowContent = Get-Content -Raw -Encoding UTF8 -LiteralPath $selectedWorkflow.FullName

    if ($workflowContent -notmatch '(?i)(?:website[\\/]|path\s*:\s*["'']?\.?[\\/]?website(?:[\\/\s"'']|$)|working-directory\s*:\s*["'']?\.?[\\/]?website(?:[\\/\s"'']|$))') {
        Add-Issue "$workflowRelativePath does not set a website path"
    }

    if ($workflowContent -notmatch '(?i)(?:actions/(?:upload-pages-artifact|deploy-pages)@|peaceiris/actions-gh-pages@)') {
        Add-Issue "$workflowRelativePath does not use a GitHub Pages deployment action"
    }

    if ($workflowContent -notmatch '(?i)website[\\/]') {
        Add-Issue "$workflowRelativePath does not include a website/** path trigger or source path"
    }
}

Write-Section 'Relative HTML/CSS/JS resources'
$resourceFiles = @($existingRequiredFiles | Where-Object { $_.Extension -in @('.html', '.css', '.js') })
foreach ($resourceFile in $resourceFiles) {
    $content = Get-Content -Raw -Encoding UTF8 -LiteralPath $resourceFile.FullName
    switch -Regex ($resourceFile.Extension.ToLowerInvariant()) {
        '\.html' {
            $attributePattern = '(?i)\b(?<attribute>src|href|poster|data-src)\s*=\s*["''](?<reference>[^"'']+)["'']'
            foreach ($match in ([regex]::Matches($content, $attributePattern))) {
                $attribute = $match.Groups['attribute'].Value.ToLowerInvariant()
                $reference = $match.Groups['reference'].Value
                $kind = if ($attribute -eq 'src' -or $attribute -eq 'poster' -or $attribute -eq 'data-src') { 'html-asset' } else { 'html-href' }
                if ($kind -eq 'html-href' -and $reference -match '(?i)\.(?:css|js|svg|png|jpe?g|gif|webp|ico)(?:[?#]|$)') {
                    $kind = 'html-asset'
                }
                Add-ResourceReference -BaseFile $resourceFile.FullName -Reference $reference -Kind $kind
            }
        }
        '\.css' {
            $urlPattern = '(?i)url\(\s*["'']?(?<reference>[^)"'']+)["'']?\s*\)'
            foreach ($match in ([regex]::Matches($content, $urlPattern))) {
                Add-ResourceReference -BaseFile $resourceFile.FullName -Reference $match.Groups['reference'].Value -Kind 'css-url'
            }
            $importPattern = '(?i)@import\s*["''](?<reference>[^"'']+)["'']'
            foreach ($match in ([regex]::Matches($content, $importPattern))) {
                Add-ResourceReference -BaseFile $resourceFile.FullName -Reference $match.Groups['reference'].Value -Kind 'css-import'
            }
        }
        '\.js' {
            $assetPatterns = @(
                '(?i)(?:src|href|url|path|asset|image|icon|poster|fetch)\s*(?:=|:|\()\s*["''](?<reference>[^"'']+)["'']',
                '(?i)["''](?<reference>(?:\./|\.\./|assets/)[^"'']+\.(?:css|js|svg|png|jpe?g|gif|webp|ico|woff2?)(?:[?#][^"'']*)?)["'']'
            )
            foreach ($assetPattern in $assetPatterns) {
                foreach ($match in ([regex]::Matches($content, $assetPattern))) {
                    Add-ResourceReference -BaseFile $resourceFile.FullName -Reference $match.Groups['reference'].Value -Kind 'js-asset'
                }
            }
        }
    }
}

Write-Host "Resource references checked: $($script:ResourceReferences.Count)"

Write-Section 'Key website copy'
$indexPath = Join-Path $script:WebsiteRoot 'index.html'
$contentPath = Join-Path $script:WebsiteRoot 'content.js'
$notFoundPath = Join-Path $script:WebsiteRoot '404.html'
$indexContent = if (Test-Path -LiteralPath $indexPath -PathType Leaf) { Get-Content -Raw -Encoding UTF8 -LiteralPath $indexPath } else { '' }
$contentScript = if (Test-Path -LiteralPath $contentPath -PathType Leaf) { Get-Content -Raw -Encoding UTF8 -LiteralPath $contentPath } else { '' }
$notFoundContent = if (Test-Path -LiteralPath $notFoundPath -PathType Leaf) { Get-Content -Raw -Encoding UTF8 -LiteralPath $notFoundPath } else { '' }
$siteCopy = "$indexContent`n$contentScript"

$copyChecks = @(
    [pscustomobject]@{ Name = 'OpenNotes brand'; Pattern = '(?i)OpenNotes' },
    [pscustomobject]@{ Name = 'live-folio hero thesis'; Pattern = '(?i)Open a PDF\.\s*Leave a trace\.' },
    [pscustomobject]@{ Name = 'PDF product positioning'; Pattern = '(?i)PDF' },
    [pscustomobject]@{ Name = 'Windows product positioning'; Pattern = '(?i)Windows' },
    [pscustomobject]@{ Name = 'annotation/notebook feature copy'; Pattern = '(?i)(?:annotat|notebook|handwriting|library)' }
)

foreach ($copyCheck in $copyChecks) {
    if ($siteCopy -match $copyCheck.Pattern) {
        Write-Host "OK   $($copyCheck.Name)" -ForegroundColor Green
    }
    else {
        Add-Issue "Missing key website copy: $($copyCheck.Name)"
        Write-Host "MISS $($copyCheck.Name)" -ForegroundColor Red
    }
}

if ($indexContent -notmatch '(?i)data-i18n') {
    Add-Issue 'website/index.html has no data-i18n markers for localized page copy'
}

foreach ($requiredSection in @('method', 'workspace', 'evidence', 'download')) {
    if ($indexContent -notmatch ('id="' + [regex]::Escape($requiredSection) + '"')) {
        Add-Issue "website/index.html is missing the redesigned '$requiredSection' section"
    }
}

if ($indexContent -notmatch 'class="[^"]*live-folio[^"]*"') {
    Add-Issue 'website/index.html must make the live folio the hero visual anchor'
}

if ($indexContent -notmatch '<img[^>]+class="brand-mark-image"[^>]+src="assets/favicon-96x96\.png"') {
    Add-Issue 'website/index.html must use the supplied optimized OpenNotes favicon PNG as the header brand mark'
}

foreach ($document in @(
        [pscustomobject]@{ Name = 'website/index.html'; Content = $indexContent },
        [pscustomobject]@{ Name = 'website/404.html'; Content = $notFoundContent }
    )) {
    foreach ($requiredLink in @(
            'rel="icon"[^>]+href="assets/favicon\.svg"',
            'rel="icon"[^>]+href="assets/favicon\.ico"',
            'rel="apple-touch-icon"[^>]+href="assets/apple-touch-icon\.png"',
            'rel="manifest"[^>]+href="assets/site\.webmanifest"'
        )) {
        if ($document.Content -notmatch $requiredLink) {
            Add-Issue "$($document.Name) is missing required OpenNotes brand metadata matching '$requiredLink'"
        }
    }
}

Write-Section 'Interactive annotation preview'
$expectedResizeDirections = @('nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w')
$resizeDirections = @(
    [regex]::Matches($indexContent, 'data-demo-resize="(?<direction>nw|n|ne|e|se|s|sw|w)"') |
        ForEach-Object { $_.Groups['direction'].Value } |
        Sort-Object -Unique
)

if ($resizeDirections.Count -ne $expectedResizeDirections.Count -or
    @($expectedResizeDirections | Where-Object { $resizeDirections -notcontains $_ }).Count -gt 0) {
    Add-Issue "website/index.html must expose exactly eight resize directions (found: $($resizeDirections -join ', '))"
}
else {
    Write-Host 'OK   eight-direction text box resizing handles' -ForegroundColor Green
}

$demoPath = Join-Path $script:WebsiteRoot 'demo.js'
$demoContent = if (Test-Path -LiteralPath $demoPath -PathType Leaf) { Get-Content -Raw -Encoding UTF8 -LiteralPath $demoPath } else { '' }
foreach ($requiredDemoToken in @('setPointerCapture', 'keyboardResize', 'syncResizeHandles', 'resizeDirections')) {
    if ($demoContent -notmatch [regex]::Escape($requiredDemoToken)) {
        Add-Issue "website/demo.js is missing interactive resize behavior token '$requiredDemoToken'"
    }
}

$dragGripCount = @([regex]::Matches($indexContent, 'data-demo-drag')).Count
if ($dragGripCount -ne 1) {
    Add-Issue "website/index.html must expose one text drag grip (found $dragGripCount)"
}

foreach ($requiredDragToken in @('beginTextDrag', 'continueTextDrag', 'clampTextPosition')) {
    if ($demoContent -notmatch [regex]::Escape($requiredDragToken)) {
        Add-Issue "website/demo.js is missing direct text movement token '$requiredDragToken'"
    }
}

foreach ($requiredDragKey in @('demo.drag', 'demo.dragging', 'demo.dragged')) {
    if ($contentScript -notmatch ('"' + [regex]::Escape($requiredDragKey) + '"\s*:')) {
        Add-Issue "website/content.js is missing localized text movement key '$requiredDragKey'"
    }
}

$undoButtonCount = @([regex]::Matches($indexContent, 'data-demo-undo')).Count
if ($undoButtonCount -ne 1) {
    Add-Issue "website/index.html must expose one undo control (found $undoButtonCount)"
}

foreach ($requiredUndoToken in @('undoStack', 'undoMarks', 'updateUndoControls')) {
    if ($demoContent -notmatch [regex]::Escape($requiredUndoToken)) {
        Add-Issue "website/demo.js is missing undo behavior token '$requiredUndoToken'"
    }
}

foreach ($requiredUndoKey in @('demo.undo', 'demo.undone', 'demo.undoEmpty')) {
    if ($contentScript -notmatch ('"' + [regex]::Escape($requiredUndoKey) + '"\s*:')) {
        Add-Issue "website/content.js is missing localized undo key '$requiredUndoKey'"
    }
}

if ($indexContent -match 'https://github\.com/Learnmore-smart/Windows-Notes') {
    Add-Issue 'website/index.html still contains a legacy Windows-Notes GitHub URL; use Learnmore-smart/OpenNotes'
}

if ($contentScript -notmatch '(?i)\b(?:en|english)\b' -or
    $contentScript -notmatch '(?i)\b(?:zh|zh-cn|chinese)\b|中文' -or
    $contentScript -notmatch '(?i)\b(?:fr|fr-fr|french)\b|fran') {
    Add-Issue 'website/content.js must contain English, Simplified Chinese, and French copy'
}

Write-Section 'Website translation coverage'
$localeKeySets = @{}
$localeBlockPattern = '(?ms)^\s*(?<locale>en|zh|fr):\s*\{(?<body>.*?)^\s*\}\s*,?\s*(?=^\s*(?:en|zh|fr):|^\s*\};)'
foreach ($localeBlock in [regex]::Matches($contentScript, $localeBlockPattern)) {
    $locale = $localeBlock.Groups['locale'].Value
    $keys = @(
        [regex]::Matches($localeBlock.Groups['body'].Value, '(?m)^\s*"(?<key>[^"]+)"\s*:') |
            ForEach-Object { $_.Groups['key'].Value } |
            Sort-Object -Unique
    )
    $localeKeySets[$locale] = $keys
    Write-Host "$locale catalog keys: $($keys.Count)"
}

if ($localeKeySets.Keys.Count -ne 3) {
    Add-Issue 'website/content.js did not expose exactly en, zh, and fr catalogs'
}
else {
    $baselineLocale = 'en'
    foreach ($locale in @('zh', 'fr')) {
        $missing = @($localeKeySets[$baselineLocale] | Where-Object { $localeKeySets[$locale] -notcontains $_ })
        $extra = @($localeKeySets[$locale] | Where-Object { $localeKeySets[$baselineLocale] -notcontains $_ })
        if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
            Add-Issue "website/content.js $locale catalog differs from en (missing: $($missing -join ', '); extra: $($extra -join ', '))"
        }
    }

    $allHtml = ''
    foreach ($htmlFile in @(Get-ChildItem -LiteralPath $script:WebsiteRoot -File -Filter '*.html' -Force -ErrorAction SilentlyContinue)) {
        $allHtml += "`n" + (Get-Content -Raw -Encoding UTF8 -LiteralPath $htmlFile.FullName)
    }
    $htmlKeys = @(
        [regex]::Matches($allHtml, 'data-i18n(?:-html|-content|-aria-label)?="(?<key>[^"]+)"') |
            ForEach-Object { $_.Groups['key'].Value } |
            Sort-Object -Unique
    )
    foreach ($key in $htmlKeys) {
        if ($localeKeySets[$baselineLocale] -notcontains $key) {
            Add-Issue "website HTML references missing translation key '$key'"
        }
    }
}

Write-Section 'Placeholder filenames'
$placeholderDirectory = Join-Path $script:WebsiteRoot 'assets\placeholders'
$placeholderReadme = Join-Path $placeholderDirectory 'README.md'
$documentedPlaceholders = @(Get-DocumentedPlaceholderNames -ReadmePath $placeholderReadme)

if ($documentedPlaceholders.Count -eq 0) {
    Add-Issue 'website/assets/placeholders/README.md does not document any image placeholder filename'
}
else {
    Write-Host "Documented placeholder filenames: $($documentedPlaceholders -join ', ')"
}

foreach ($placeholderName in $documentedPlaceholders) {
    if ($placeholderName -match '[\\/\s]') {
        Add-Issue "Placeholder filename '$placeholderName' contains a path separator or whitespace"
    }
}

$actualPlaceholderFiles = @()
if (Test-Path -LiteralPath $placeholderDirectory -PathType Container) {
    $actualPlaceholderFiles = @(
        Get-ChildItem -LiteralPath $placeholderDirectory -File -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne 'README.md' }
    )
}

foreach ($actualFile in $actualPlaceholderFiles) {
    if ($documentedPlaceholders -notcontains $actualFile.Name) {
        Add-Issue "Placeholder asset '$($actualFile.Name)' is not documented in website/assets/placeholders/README.md"
    }
}

foreach ($reference in @($script:ResourceReferences | Where-Object { $_.Path -match '(?i)(?:^|/)assets/placeholders/[^/]+$' })) {
    $placeholderName = ($reference.Path -split '/')[-1]
    if ($documentedPlaceholders -notcontains $placeholderName) {
        Add-Issue "$(Get-RelativePath $reference.File) references placeholder '$placeholderName' without documenting its filename in README.md"
    }
}

Write-Host "Placeholder assets present: $($actualPlaceholderFiles.Count)"

if ($script:Issues.Count -eq 0) {
    Write-Host "`nPASS: website verification found no issues." -ForegroundColor Green
    exit 0
}

Write-Host "`nFAIL: $($script:Issues.Count) issue(s) found." -ForegroundColor Red
foreach ($issue in $script:Issues) {
    Write-Host " - $issue" -ForegroundColor Red
}
exit 1
