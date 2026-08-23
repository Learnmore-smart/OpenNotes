[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PdfPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$resolvedPdfPath = (Resolve-Path -LiteralPath $PdfPath).Path
$temporaryOutputPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'OpenNotesThirdPartyViewerSmoke_' + [guid]::NewGuid().ToString('N'))
$inputHashBefore = (Get-FileHash -LiteralPath $resolvedPdfPath -Algorithm SHA256).Hash
$edgeProcessIdsBefore = @()
$edgeProcessTrackingAvailable = $false
try {
    $edgeProcessIdsBefore = @(Get-Process msedge -ErrorAction Stop | Select-Object -ExpandProperty Id)
    $edgeProcessTrackingAvailable = $true
}
catch {
    Write-Warning "Could not capture the pre-check Edge PID set; Edge child cleanup will be skipped: $($_.Exception.Message)"
}

function Resolve-PopplerTool([string]$name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    $taskUserProfile = $env:USERPROFILE
    if (-not [string]::IsNullOrWhiteSpace($taskUserProfile)) {
        $bundled = Join-Path $taskUserProfile (
            '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\poppler\Library\bin\' + $name)
        if (Test-Path -LiteralPath $bundled) { return (Resolve-Path -LiteralPath $bundled).Path }
    }
    return $null
}

function Resolve-EdgePath {
    $candidates = @(
        'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
        'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    $command = Get-Command msedge.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    return $null
}

function Invoke-ExternalTool([string]$filePath, [string[]]$arguments, [int]$timeoutMs = 30000) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $filePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    # Windows PowerShell 5.1 does not expose an initialized ArgumentList on
    # ProcessStartInfo. Quote each complete argument so PDF paths and Edge
    # profile/screenshot paths containing spaces remain a single argument.
    $startInfo.Arguments = (($arguments | ForEach-Object {
        '"' + $_.Replace('"', '\"') + '"'
    }) -join ' ')

    $child = [System.Diagnostics.Process]::Start($startInfo)
    $stdoutTask = $child.StandardOutput.ReadToEndAsync()
    $stderrTask = $child.StandardError.ReadToEndAsync()
    if (-not $child.WaitForExit($timeoutMs)) {
        try { $child.Kill() } catch { }
        throw "External viewer process timed out: $filePath"
    }
    [System.Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask))
    $result = [pscustomobject]@{
        ExitCode = $child.ExitCode
        Stdout = $stdoutTask.Result
        Stderr = $stderrTask.Result
    }
    $child.Dispose()
    return $result
}

function Get-PngInfo([string]$path) {
    $bitmap = [System.Drawing.Bitmap]::new($path)
    try {
        if ($bitmap.Width -lt 1 -or $bitmap.Height -lt 1) {
            throw "Rendered PNG has invalid dimensions: $($bitmap.Width)x$($bitmap.Height)"
        }
        $samplePoints = @(
            [System.Drawing.Point]::new(0, 0),
            [System.Drawing.Point]::new([Math]::Max(0, $bitmap.Width - 1), [Math]::Max(0, $bitmap.Height - 1)),
            [System.Drawing.Point]::new([int]($bitmap.Width / 2), [int]($bitmap.Height / 2))
        )
        $samples = foreach ($point in $samplePoints) {
            $pixel = $bitmap.GetPixel($point.X, $point.Y)
            "$($pixel.R),$($pixel.G),$($pixel.B)"
        }
        return [pscustomobject]@{
            Width = $bitmap.Width
            Height = $bitmap.Height
            Samples = ($samples -join ';')
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $temporaryOutputPath -Force | Out-Null
    $pdfInfoPath = Resolve-PopplerTool 'pdfinfo.exe'
    $pdfToPpmPath = Resolve-PopplerTool 'pdftoppm.exe'
    $edgePath = Resolve-EdgePath
    if ($null -eq $pdfInfoPath -or $null -eq $pdfToPpmPath) {
        throw 'Bundled Poppler pdfinfo.exe/pdftoppm.exe was not found.'
    }
    if ($null -eq $edgePath) {
        throw 'Microsoft Edge executable was not found.'
    }

    $info = Invoke-ExternalTool $pdfInfoPath @($resolvedPdfPath)
    if ($info.ExitCode -ne 0) {
        throw "pdfinfo failed exit=$($info.ExitCode) stderr=$($info.Stderr.Trim())"
    }
    $pageMatch = [System.Text.RegularExpressions.Regex]::Match($info.Stdout, '(?m)^Pages:\s*(\d+)\s*$')
    if (-not $pageMatch.Success -or [int]$pageMatch.Groups[1].Value -lt 1) {
        throw "pdfinfo did not report a positive page count: $($info.Stdout.Trim())"
    }
    $pageCount = [int]$pageMatch.Groups[1].Value
    Write-Output "PDFINFO_RESULT=PASS pages=$pageCount"

    $popplerPrefix = Join-Path $temporaryOutputPath 'page'
    $render = Invoke-ExternalTool $pdfToPpmPath @(
        '-png', '-r', '96', '-f', '1', '-singlefile', $resolvedPdfPath, $popplerPrefix)
    $popplerPng = "$popplerPrefix.png"
    if ($render.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $popplerPng)) {
        throw "pdftoppm failed exit=$($render.ExitCode) stderr=$($render.Stderr.Trim())"
    }
    $pngInfo = Get-PngInfo $popplerPng
    if ((Get-Item -LiteralPath $popplerPng).Length -le 0) {
        throw 'pdftoppm produced an empty PNG.'
    }
    Write-Output "POPPLER_RENDER_RESULT=PASS png=$popplerPng size=$($pngInfo.Width)x$($pngInfo.Height) samples=$($pngInfo.Samples)"

    $edgeProfilePath = Join-Path $temporaryOutputPath 'edge-profile'
    $edgeScreenshotPath = Join-Path $temporaryOutputPath 'edge-page.png'
    $pdfUri = ([Uri]::new($resolvedPdfPath)).AbsoluteUri
    $edge = Invoke-ExternalTool $edgePath @(
        '--headless=new',
        '--disable-gpu',
        '--no-first-run',
        '--no-default-browser-check',
        "--user-data-dir=$edgeProfilePath",
        "--screenshot=$edgeScreenshotPath",
        '--window-size=1280,1000',
        $pdfUri) 45000
    if ($edge.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $edgeScreenshotPath)) {
        throw "Edge headless PDF open failed exit=$($edge.ExitCode) stderr=$($edge.Stderr.Trim()) stdout=$($edge.Stdout.Trim())"
    }
    $edgePngInfo = Get-PngInfo $edgeScreenshotPath
    if ((Get-Item -LiteralPath $edgeScreenshotPath).Length -le 0) {
        throw 'Edge headless produced an empty screenshot.'
    }
    Write-Output "EDGE_HEADLESS_RESULT=PASS screenshot=$edgeScreenshotPath size=$($edgePngInfo.Width)x$($edgePngInfo.Height)"

    $inputHashAfter = (Get-FileHash -LiteralPath $resolvedPdfPath -Algorithm SHA256).Hash
    if ($inputHashAfter -ne $inputHashBefore) {
        throw "Input PDF changed during third-party validation before=$inputHashBefore after=$inputHashAfter"
    }
    Write-Output "PDF_INPUT_UNCHANGED=True sha256=$inputHashBefore"
    Write-Output 'THIRD_PARTY_VIEWER_SMOKE_RESULT=PASS'
}
catch {
    Write-Output 'THIRD_PARTY_VIEWER_SMOKE_RESULT=FAIL'
    throw
}
finally {
    if ($edgeProcessTrackingAvailable) {
        $knownEdgeIds = [System.Collections.Generic.HashSet[int]]::new()
        foreach ($edgeId in $edgeProcessIdsBefore) { [void]$knownEdgeIds.Add([int]$edgeId) }
        $newEdgeProcesses = @(Get-Process msedge -ErrorAction SilentlyContinue |
            Where-Object { -not $knownEdgeIds.Contains([int]$_.Id) })
        foreach ($edgeProcess in $newEdgeProcesses) {
            try {
                if (-not $edgeProcess.HasExited) {
                    $edgeProcess.Kill()
                    [void]$edgeProcess.WaitForExit(3000)
                }
            }
            catch {
                Write-Warning "Failed to close an Edge process created by this check pid=$($edgeProcess.Id): $($_.Exception.Message)"
            }
        }
        Write-Output "EDGE_CHILDREN_CLEANED=$($newEdgeProcesses.Count)"
    }
    if (Test-Path -LiteralPath $temporaryOutputPath) {
        try {
            Remove-Item -LiteralPath $temporaryOutputPath -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Failed to remove the exact third-party viewer temporary directory: $($_.Exception.Message)"
        }
    }
    Write-Output "VIEWER_TEMP_CLEANED=$(-not (Test-Path -LiteralPath $temporaryOutputPath))"
}
