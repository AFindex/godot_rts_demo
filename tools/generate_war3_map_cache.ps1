[CmdletBinding()]
param(
    [string]$MapId = "lordaeron_crossroads",
    [string]$GodotExe =
        "F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if (-not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    throw "Godot executable not found: $GodotExe"
}
if ([string]::IsNullOrWhiteSpace($MapId)) {
    throw "MapId must not be empty."
}

& $GodotExe `
    --headless `
    --path $projectRoot `
    res://war3_rts/War3Rts.tscn `
    -- `
    --war3-bake-map-cache `
    "--war3-map=$MapId"
if ($LASTEXITCODE -ne 0) {
    throw "War3 map cache generation failed with exit code $LASTEXITCODE."
}

$cachePath = Join-Path $projectRoot "war3_rts\maps\$MapId\map.w3cache.json"
if (-not (Test-Path -LiteralPath $cachePath -PathType Leaf)) {
    throw "War3 map cache was not written: $cachePath"
}
Write-Host "War3 map cache saved: $cachePath"
