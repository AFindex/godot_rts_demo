[CmdletBinding()]
param(
    [string]$DotnetExe = "dotnet"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputDirectory = Join-Path $projectRoot "benchmark_results"
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"

$benchmarkProject = Join-Path $projectRoot "benchmarks\RtsBenchmark.csproj"
$output = & $DotnetExe run `
    --project $benchmarkProject `
    --configuration Release 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
$jsonLine = $output |
    Where-Object { $_.ToString().StartsWith("RTS_BENCHMARK_JSON ") } |
    Select-Object -Last 1
if ($null -eq $jsonLine) {
    throw "Benchmark did not produce RTS_BENCHMARK_JSON."
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$json = $jsonLine.ToString().Substring("RTS_BENCHMARK_JSON ".Length)
$parsed = $json | ConvertFrom-Json
$formatted = $parsed | ConvertTo-Json -Depth 6
$timestampedPath = Join-Path $outputDirectory "$stamp.json"
$latestPath = Join-Path $outputDirectory "latest.json"
$formatted | Set-Content -LiteralPath $timestampedPath -Encoding utf8
$formatted | Set-Content -LiteralPath $latestPath -Encoding utf8

Write-Host "Benchmark report saved: $timestampedPath"
if ($exitCode -ne 0) {
    throw "Benchmark correctness checks failed with exit code $exitCode."
}
