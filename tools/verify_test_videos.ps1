[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot "..\test_videos")
)

$ErrorActionPreference = "Stop"
$videoRoot = (Resolve-Path -LiteralPath $Root).Path
$unexpected = @(
    Get-ChildItem -LiteralPath $videoRoot -File -Recurse |
        Where-Object { $_.Extension -eq ".avi" -or $_.Name -like "*.partial.webm" }
)
if ($unexpected.Count -gt 0) {
    throw "Uncompressed or partial recordings remain: $($unexpected.FullName -join ', ')"
}

$ffmpeg = (& (Join-Path $PSScriptRoot "get_ffmpeg.ps1") | Select-Object -Last 1)
$ffprobe = Join-Path (Split-Path -Parent $ffmpeg) "ffprobe.exe"
$videos = @(Get-ChildItem -LiteralPath $videoRoot -Filter *.webm -File -Recurse)
foreach ($video in $videos) {
    $probe = & $ffprobe -v error -count_frames -select_streams v:0 `
        -show_entries stream=codec_name,width,height,nb_read_frames -of json `
        $video.FullName | ConvertFrom-Json
    if ($probe.streams.Count -ne 1 -or
        $probe.streams[0].codec_name -ne "av1" -or
        $probe.streams[0].width -le 0 -or $probe.streams[0].height -le 0 -or
        $probe.streams[0].nb_read_frames -eq "N/A" -or
        [int64]$probe.streams[0].nb_read_frames -le 0) {
        throw "Invalid AV1 recording: $($video.FullName)"
    }
}

$manifestCases = 0
$manifests = @(Get-ChildItem -LiteralPath $videoRoot -Filter manifest.json -File -Recurse)
foreach ($manifestFile in $manifests) {
    $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
    foreach ($case in $manifest.cases) {
        if ($null -eq $case.file) {
            continue
        }
        $manifestCases++
        $videoPath = Join-Path $manifestFile.Directory.FullName $case.file
        if ([IO.Path]::GetExtension($videoPath) -ne ".webm" -or
            -not (Test-Path -LiteralPath $videoPath -PathType Leaf) -or
            (Get-Item -LiteralPath $videoPath).Length -ne $case.bytes) {
            throw "Manifest does not match encoded video: $videoPath"
        }
    }
}

Write-Host (
    "RTS_VIDEO_VERIFY PASS videos={0} manifests={1} case_refs={2} codec=av1" -f
    $videos.Count, $manifests.Count, $manifestCases)
