[CmdletBinding()]
param(
    [string[]]$Case = @("all"),

    [ValidateRange(1, 60)]
    [int]$Fps = 30,

    [string]$GodotExe =
        "F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$outputDirectory = Join-Path $projectRoot "test_videos\$stamp"

if (-not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    throw "Godot executable not found: $GodotExe"
}

$catalogOutput = & $GodotExe --headless --path $projectRoot -- --list-visual-tests 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Unable to query the visual test catalog.`n$($catalogOutput -join [Environment]::NewLine)"
}

$catalogLine = $catalogOutput |
    Where-Object { $_.ToString().StartsWith("RTS_VISUAL_TEST_CASES ") } |
    Select-Object -Last 1
if ($null -eq $catalogLine) {
    throw "Godot did not return RTS_VISUAL_TEST_CASES."
}

$allCases = $catalogLine.ToString().Substring("RTS_VISUAL_TEST_CASES ".Length).Split(',')
if ($Case -contains "all") {
    $selectedCases = $allCases
}
else {
    $unknownCases = @($Case | Where-Object { $allCases -notcontains $_ })
    if ($unknownCases.Count -gt 0) {
        throw "Unknown cases '$($unknownCases -join ', ')'. Available cases: $($allCases -join ', ')"
    }

    $selectedCases = @($Case | Select-Object -Unique)
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$manifestCases = @()
$failedCases = @()
foreach ($caseId in $selectedCases) {
    $moviePath = Join-Path $outputDirectory "$caseId.avi"
    $logPath = Join-Path $outputDirectory "$caseId.log"
    Write-Host "Recording $caseId -> $moviePath"

    & $GodotExe `
        --path $projectRoot `
        --write-movie $moviePath `
        --fixed-fps $Fps `
        --disable-vsync `
        --log-file $logPath `
        -- `
        --visual-test $caseId

    $exitCode = $LASTEXITCODE
    $movieExists = Test-Path -LiteralPath $moviePath -PathType Leaf
    $resultLine = if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        Get-Content -LiteralPath $logPath |
            Where-Object { $_ -match '^RTS_VISUAL_TEST_(PASS|FAIL) ' } |
            Select-Object -Last 1
    }
    else {
        $null
    }

    if ($exitCode -ne 0 -or -not $movieExists) {
        $failedCases += $caseId
    }

    $manifestCases += [ordered]@{
        id = $caseId
        file = if ($movieExists) { Split-Path -Leaf $moviePath } else { $null }
        log = if (Test-Path -LiteralPath $logPath) { Split-Path -Leaf $logPath } else { $null }
        bytes = if ($movieExists) { (Get-Item -LiteralPath $moviePath).Length } else { 0 }
        exit_code = $exitCode
        result = if ($null -ne $resultLine) { $resultLine.ToString() } else { $null }
    }
}

$manifest = [ordered]@{
    created_at = (Get-Date).ToString("o")
    fps = $Fps
    godot_executable = $GodotExe
    requested_cases = $Case
    available_cases = $allCases
    cases = $manifestCases
}
$manifestPath = Join-Path $outputDirectory "manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "All requested recordings completed: $outputDirectory"
if ($failedCases.Count -gt 0) {
    throw "Some visual tests failed: $($failedCases -join ', '). All available videos were preserved."
}
