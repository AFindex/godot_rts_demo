[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$videoRoot = Join-Path $projectRoot "test_videos"
$outputPath = Join-Path $videoRoot "showcase\videos.json"
$catalogPath = Join-Path $projectRoot "src\Tests\VisualTestCatalog.cs"

$displayNames = @{}
if (Test-Path -LiteralPath $catalogPath) {
    $catalogSource = Get-Content -LiteralPath $catalogPath -Raw
    $pattern = 'VisualTestSession\(\s*"([^"]+)"\s*,\s*"([^"]+)"'
    foreach ($match in [regex]::Matches($catalogSource, $pattern)) {
        $displayNames[$match.Groups[1].Value] = $match.Groups[2].Value
    }
}

$recordings = [System.Collections.Generic.List[object]]::new()
$manifestFiles = Get-ChildItem -LiteralPath $videoRoot -Directory |
    Where-Object { $_.Name -match '^\d{8}_\d{6}$' } |
    ForEach-Object { Get-Item -LiteralPath (Join-Path $_.FullName "manifest.json") -ErrorAction SilentlyContinue } |
    Where-Object { $null -ne $_ }

foreach ($manifestFile in $manifestFiles) {
    $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
    $batch = $manifestFile.Directory.Name
    $createdAt = $manifest.created_at

    foreach ($case in @($manifest.cases)) {
        if ([string]::IsNullOrWhiteSpace($case.file)) { continue }
        $videoPath = Join-Path $manifestFile.Directory.FullName $case.file
        if (-not (Test-Path -LiteralPath $videoPath -PathType Leaf)) { continue }

        $result = [string]$case.result
        $status = if ($result -match '^RTS_VISUAL_TEST_PASS') {
            "passed"
        }
        elseif ($result -match '^RTS_VISUAL_TEST_FAIL') {
            "failed"
        }
        elseif ([int]$case.exit_code -eq 0) {
            "passed"
        }
        else {
            "unknown"
        }

        $metrics = $result
        if ($metrics -match ':\s*(.+)$') { $metrics = $Matches[1] }

        $recordings.Add([pscustomobject][ordered]@{
            id = [string]$case.id
            display_name = if ($displayNames.ContainsKey([string]$case.id)) {
                $displayNames[[string]$case.id]
            } else { $null }
            batch = $batch
            created_at = $createdAt
            video = "../$batch/$($case.file)"
            log = if ([string]::IsNullOrWhiteSpace($case.log)) { $null } else {
                "../$batch/$($case.log)"
            }
            bytes = [int64](Get-Item -LiteralPath $videoPath).Length
            fps = [int]$manifest.fps
            codec = if ($null -ne $manifest.codec) { [string]$manifest.codec } else { [string]$case.codec }
            crf = if ($null -ne $case.crf) { [int]$case.crf } else { [int]$manifest.crf }
            status = $status
            result = $result
            metrics = $metrics
        })
    }
}

$ordered = @($recordings | Sort-Object @{ Expression = { [DateTimeOffset]$_.created_at }; Descending = $true }, id)
$latestIds = @($ordered | Select-Object -ExpandProperty id -Unique)
$payload = [ordered]@{
    generated_at = (Get-Date).ToString("o")
    project = "Godot RTS Demo"
    recording_count = $ordered.Count
    case_count = $latestIds.Count
    recordings = $ordered
}

$payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outputPath -Encoding utf8
Write-Host "Test video showcase index updated: $outputPath ($($ordered.Count) recordings, $($latestIds.Count) cases)"
