[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$version = "8.1.2"
$archiveName = "ffmpeg-$version-full_build.7z"
$archiveUrl =
    "https://github.com/GyanD/codexffmpeg/releases/download/$version/$archiveName"
$expectedSha256 =
    "0fff188997a499b5382e0f66e845d4556c48c54f0113ebed4853d556dbdd7059"
$cacheRoot = Join-Path $PSScriptRoot ".cache\ffmpeg\$version"
$archivePath = Join-Path $cacheRoot $archiveName
$extractRoot = Join-Path $cacheRoot "extracted"
$absoluteCacheRoot = [IO.Path]::GetFullPath($cacheRoot)
$absoluteExtractRoot = [IO.Path]::GetFullPath($extractRoot)
if (-not $absoluteExtractRoot.StartsWith(
    $absoluteCacheRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe FFmpeg extraction target: $absoluteExtractRoot"
}

$existing = Get-ChildItem -LiteralPath $extractRoot -Filter ffmpeg.exe -Recurse `
    -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $existing) {
    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
            $expectedSha256) {
        Write-Host "Downloading pinned FFmpeg $version full build..."
        Invoke-WebRequest -UseBasicParsing -Uri $archiveUrl -OutFile $archivePath
    }

    $actualSha256 =
        (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        throw "FFmpeg archive SHA-256 mismatch: $actualSha256"
    }

    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    Write-Host "Extracting FFmpeg $version..."
    & tar -xf $archivePath -C $extractRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to extract FFmpeg archive with bsdtar."
    }
    $existing = Get-ChildItem -LiteralPath $extractRoot -Filter ffmpeg.exe -Recurse |
        Select-Object -First 1
}

if ($null -eq $existing) {
    throw "FFmpeg executable was not found after extraction."
}
$encoderList = & $existing.FullName -hide_banner -encoders 2>&1
if ($LASTEXITCODE -ne 0 -or -not ($encoderList -match "libsvtav1")) {
    throw "Pinned FFmpeg build does not expose the libsvtav1 encoder."
}

$existing.FullName
