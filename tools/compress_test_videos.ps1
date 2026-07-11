[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot "..\test_videos"),

    [ValidateRange(1, 70)]
    [int]$Crf = 32,

    [ValidateRange(0, 13)]
    [int]$Preset = 8,

    [switch]$KeepSource
)

$ErrorActionPreference = "Stop"
$videoRoot = (Resolve-Path -LiteralPath $Root).Path
$inputs = @(Get-ChildItem -LiteralPath $videoRoot -Filter *.avi -File -Recurse |
    Sort-Object FullName)
if ($inputs.Count -eq 0) {
    Write-Host "No AVI recordings found under $videoRoot"
    return
}

$results = New-Object System.Collections.Generic.List[object]
foreach ($input in $inputs) {
    $output = [IO.Path]::ChangeExtension($input.FullName, ".webm")
    $result = & (Join-Path $PSScriptRoot "compress_test_video.ps1") `
        -InputPath $input.FullName -OutputPath $output -Crf $Crf -Preset $Preset `
        -DeleteSource:(-not $KeepSource)
    $results.Add($result)

    $manifestPath = Join-Path $input.Directory.FullName "manifest.json"
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        foreach ($case in $manifest.cases) {
            if ($case.file -eq $input.Name) {
                $case.file = [IO.Path]::GetFileName($output)
                $case.bytes = $result.OutputBytes
                $case | Add-Member -NotePropertyName codec -NotePropertyValue "av1" -Force
                $case | Add-Member -NotePropertyName container -NotePropertyValue "webm" -Force
                $case | Add-Member -NotePropertyName crf -NotePropertyValue $Crf -Force
                $case | Add-Member -NotePropertyName preset -NotePropertyValue $Preset -Force
            }
        }
        $manifest | ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath $manifestPath -Encoding utf8
    }
}

$sourceBytes = ($results | Measure-Object SourceBytes -Sum).Sum
$outputBytes = ($results | Measure-Object OutputBytes -Sum).Sum
$report = [ordered]@{
    created_at = (Get-Date).ToString("o")
    codec = "AV1 (libsvtav1)"
    container = "WebM"
    crf = $Crf
    preset = $Preset
    files = $results.Count
    source_bytes = $sourceBytes
    output_bytes = $outputBytes
    ratio = if ($sourceBytes -eq 0) { 0.0 } else { $outputBytes / $sourceBytes }
    saved_bytes = $sourceBytes - $outputBytes
}
$reportPath = Join-Path $videoRoot "compression_report.json"
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host (
    "Compressed {0} recordings: {1:N0} -> {2:N0} bytes ({3:P1})" -f
    $results.Count, $sourceBytes, $outputBytes, $report.ratio)
