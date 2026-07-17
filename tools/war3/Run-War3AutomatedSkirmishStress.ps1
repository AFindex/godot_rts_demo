param(
    [string]$Godot = "godot",
    [int]$WarmupSeconds = 2,
    [int]$SampleSeconds = 60,
    [int]$TicksPerPhysicsFrame = 4,
    [double]$TickSpikeMilliseconds = 8,
    [double]$FrameSpikeMilliseconds = 25,
    [switch]$Rendered
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$reportDirectory = Join-Path $projectRoot "reports"
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$reportPath = Join-Path $reportDirectory "war3_auto_skirmish_$stamp.log"

$godotArguments = @()
if (-not $Rendered) {
    $godotArguments += "--headless"
}
$godotArguments += @(
    "--path", $projectRoot,
    "res://war3_rts/War3Rts.tscn",
    "--",
    "--war3-auto-skirmish-stress",
    "--war3-auto-skirmish-ticks-per-frame=$TicksPerPhysicsFrame",
    "--war3-profile-warmup=$WarmupSeconds",
    "--war3-profile-seconds=$SampleSeconds",
    "--war3-auto-skirmish-spike-ms=$TickSpikeMilliseconds",
    "--war3-profile-spike-ms=$FrameSpikeMilliseconds"
)

Write-Host "WAR3 automated skirmish stress report: $reportPath"
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$writer = [System.IO.StreamWriter]::new($reportPath, $false, $utf8WithoutBom)
try {
    & $Godot @godotArguments 2>&1 | ForEach-Object {
        $line = $_.ToString()
        $writer.WriteLine($line)
        Write-Output $line
    }
    $exitCode = $LASTEXITCODE
}
finally {
    $writer.Dispose()
}
$ErrorActionPreference = $previousErrorActionPreference
Write-Host "WAR3 automated skirmish stress exit code: $exitCode"
exit $exitCode
