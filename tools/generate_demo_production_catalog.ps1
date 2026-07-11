[CmdletBinding()]
param(
    [string]$GodotExe =
        "F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
& $GodotExe --headless --path $projectRoot -- --generate-demo-production-catalog
if ($LASTEXITCODE -ne 0) {
    throw "Production catalog generation failed with exit code $LASTEXITCODE."
}
