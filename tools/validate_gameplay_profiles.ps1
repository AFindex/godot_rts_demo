[CmdletBinding()]
param(
    [string]$GodotExe =
        "F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if (-not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    throw "Godot executable not found: $GodotExe"
}

& $GodotExe --headless --path $projectRoot -- --validate-gameplay-profiles
if ($LASTEXITCODE -ne 0) {
    throw "Gameplay profile validation failed with exit code $LASTEXITCODE."
}
