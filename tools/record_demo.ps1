[CmdletBinding()]
param(
    [ValidateSet("3d-encounter", "terrain-traversal", "terrain-vision", "terrain-authoring")]
    [string]$Demo = "3d-encounter",
    [ValidateRange(1, 60)] [int]$Fps = 30,
    [ValidateRange(1, 70)] [int]$Crf = 32,
    [ValidateRange(0, 13)] [int]$EncoderPreset = 8,
    [string]$GodotExe = "F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$outputDirectory = Join-Path $projectRoot "test_videos\$stamp"
$configuration = switch ($Demo) {
    "3d-encounter" {
        @{
            Scene = "res://demo/3d/RtsEncounter3D.tscn"
            Argument = "--demo-3d-recording"
            ResultPrefix = "RTS_3D_DEMO_"
            PassPrefix = "RTS_3D_DEMO_PASS "
        }
    }
    "terrain-traversal" {
        @{
            Scene = "res://demo/3d/TerrainTraversal3D.tscn"
            Argument = "--terrain-demo-recording"
            ResultPrefix = "RTS_TERRAIN_DEMO_"
            PassPrefix = "RTS_TERRAIN_DEMO_PASS "
        }
    }
    "terrain-vision" {
        @{
            Scene = "res://demo/3d/TerrainVisionCombat3D.tscn"
            Argument = "--terrain-vision-demo-recording"
            ResultPrefix = "RTS_TERRAIN_VISION_DEMO_"
            PassPrefix = "RTS_TERRAIN_VISION_DEMO_PASS "
        }
    }
    "terrain-authoring" {
        @{
            Scene = "res://demo/terrain/TerrainAuthoringWorkspace.tscn"
            Argument = "--terrain-authoring-demo-recording"
            ResultPrefix = "RTS_TERRAIN_AUTHORING_DEMO_"
            PassPrefix = "RTS_TERRAIN_AUTHORING_DEMO_PASS "
        }
    }
}
$scene = $configuration.Scene
if (-not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    throw "Godot executable not found: $GodotExe"
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$capturePath = Join-Path $outputDirectory "$Demo.capture.avi"
$moviePath = Join-Path $outputDirectory "$Demo.webm"
$logPath = Join-Path $outputDirectory "$Demo.log"

& $GodotExe --path $projectRoot --write-movie $capturePath `
    --fixed-fps $Fps --disable-vsync --log-file $logPath `
    $scene -- $configuration.Argument
$exitCode = $LASTEXITCODE
$resultLine = if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    Get-Content -LiteralPath $logPath |
        Where-Object { $_.StartsWith($configuration.ResultPrefix) } |
        Select-Object -Last 1
}
if ($exitCode -ne 0 -or $null -eq $resultLine -or
    -not $resultLine.StartsWith($configuration.PassPrefix)) {
    throw "Demo recording failed (exit=$exitCode): $resultLine"
}

$compression = & (Join-Path $PSScriptRoot "compress_test_video.ps1") `
    -InputPath $capturePath -OutputPath $moviePath `
    -Crf $Crf -Preset $EncoderPreset -DeleteSource
$manifest = [ordered]@{
    created_at = (Get-Date).ToString("o")
    fps = $Fps
    codec = "AV1 (libsvtav1)"
    container = "WebM"
    crf = $Crf
    preset = $EncoderPreset
    godot_executable = $GodotExe
    requested_cases = @($Demo)
    available_cases = @($Demo)
    cases = @([ordered]@{
        id = $Demo
        file = Split-Path -Leaf $moviePath
        log = Split-Path -Leaf $logPath
        bytes = (Get-Item -LiteralPath $moviePath).Length
        source_bytes = $compression.SourceBytes
        codec = "av1"
        container = "webm"
        crf = $Crf
        preset = $EncoderPreset
        exit_code = $exitCode
        encode_error = $null
        result = $resultLine.ToString()
    })
}
$manifest | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $outputDirectory "manifest.json") -Encoding utf8
& (Join-Path $PSScriptRoot "generate_test_video_showcase.ps1") | Out-Null
Write-Host "RTS_DEMO_RECORD PASS demo=$Demo output=$moviePath"
