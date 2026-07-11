[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [string]$OutputPath,

    [ValidateRange(1, 70)]
    [int]$Crf = 32,

    [ValidateRange(0, 13)]
    [int]$Preset = 8,

    [switch]$DeleteSource
)

$ErrorActionPreference = "Stop"
$inputFile = (Resolve-Path -LiteralPath $InputPath).Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = [IO.Path]::ChangeExtension($inputFile, ".webm")
}
$outputFile = [IO.Path]::GetFullPath($OutputPath)
if ($inputFile -eq $outputFile) {
    throw "Input and output paths must differ."
}

$ffmpeg = (& (Join-Path $PSScriptRoot "get_ffmpeg.ps1") | Select-Object -Last 1)
$ffprobe = Join-Path (Split-Path -Parent $ffmpeg) "ffprobe.exe"
if (-not (Test-Path -LiteralPath $ffprobe -PathType Leaf)) {
    throw "ffprobe.exe was not found beside ffmpeg.exe."
}

$inputProbe = & $ffprobe -v error -count_frames -select_streams v:0 `
    -show_entries stream=width,height,nb_read_frames -of json $inputFile |
    ConvertFrom-Json
if ($null -eq $inputProbe.streams -or $inputProbe.streams.Count -ne 1) {
    throw "Input does not contain exactly one video stream: $inputFile"
}

$outputDirectory = Split-Path -Parent $outputFile
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$temporaryFile = Join-Path $outputDirectory `
    (([IO.Path]::GetFileNameWithoutExtension($outputFile)) + ".partial.webm")
Remove-Item -LiteralPath $temporaryFile -Force -ErrorAction SilentlyContinue

Write-Host "AV1 CRF $Crf preset $Preset`: $inputFile"
& $ffmpeg -y -hide_banner -loglevel warning -i $inputFile `
    -map 0:v:0 -an -c:v libsvtav1 -preset $Preset -crf $Crf `
    -pix_fmt yuv420p -fps_mode passthrough -svtav1-params "tune=0" `
    -map_metadata -1 -map_chapters -1 $temporaryFile
if ($LASTEXITCODE -ne 0 -or
    -not (Test-Path -LiteralPath $temporaryFile -PathType Leaf)) {
    throw "FFmpeg failed to encode $inputFile"
}

$outputProbe = & $ffprobe -v error -count_frames -select_streams v:0 `
    -show_entries stream=codec_name,width,height,nb_read_frames -of json $temporaryFile |
    ConvertFrom-Json
$inputStream = $inputProbe.streams[0]
$outputStream = $outputProbe.streams[0]
$sameFrameCount =
    $inputStream.nb_read_frames -eq "N/A" -or $outputStream.nb_read_frames -eq "N/A" -or
    [int64]$inputStream.nb_read_frames -eq [int64]$outputStream.nb_read_frames
if ($outputStream.codec_name -ne "av1" -or
    $inputStream.width -ne $outputStream.width -or
    $inputStream.height -ne $outputStream.height -or
    -not $sameFrameCount) {
    Remove-Item -LiteralPath $temporaryFile -Force
    throw "Encoded video validation failed for $inputFile"
}

Move-Item -LiteralPath $temporaryFile -Destination $outputFile -Force
$sourceBytes = (Get-Item -LiteralPath $inputFile).Length
$outputBytes = (Get-Item -LiteralPath $outputFile).Length
if ($DeleteSource) {
    Remove-Item -LiteralPath $inputFile -Force
}

[pscustomobject]@{
    Input = $inputFile
    Output = $outputFile
    SourceBytes = $sourceBytes
    OutputBytes = $outputBytes
    Ratio = if ($sourceBytes -eq 0) { 0.0 } else { $outputBytes / $sourceBytes }
    Codec = "av1"
    Container = "webm"
    Crf = $Crf
    Preset = $Preset
    Frames = $outputStream.nb_read_frames
}
