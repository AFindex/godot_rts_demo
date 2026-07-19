param(
    [int]$ProcessId = 0,
    [int]$CounterSeconds = 10,
    [int]$TraceSeconds = 30,
    [string]$Label = "interactive",
    [switch]$IncludeHeadless,
    [switch]$IncludeGcVerbose,
    [switch]$IncludeGcDump,
    [switch]$SkipCounters
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $projectRoot
try {
    dotnet tool restore | Out-Host

    $allGodot = @(Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -like 'Godot*' -and
            $_.Name -notlike '*console*'
        })

    if ($ProcessId -le 0) {
        $candidates = @($allGodot | Where-Object {
            $_.CommandLine -notmatch '(?:^|\s)--editor(?:\s|$)' -and
            ($IncludeHeadless -or $_.CommandLine -notmatch '(?:^|\s)--headless(?:\s|$)')
        } | Sort-Object CreationDate -Descending)
        if ($candidates.Count -eq 0 -and -not $IncludeHeadless) {
            throw "No rendered non-editor Godot process was found. Start the War3 stress case or pass -IncludeHeadless."
        }
        if ($candidates.Count -eq 0) {
            throw "No attachable Godot process was found."
        }
        $target = $candidates[0]
        $ProcessId = [int]$target.ProcessId
    }
    else {
        $target = $allGodot |
            Where-Object ProcessId -eq $ProcessId |
            Select-Object -First 1
        if ($null -eq $target) {
            throw "Godot process $ProcessId was not found."
        }
    }

    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $safeLabel = $Label -replace '[^A-Za-z0-9_.-]', '_'
    $outputDirectory = Join-Path $projectRoot `
        "reports\dotnet\war3_${stamp}_${safeLabel}_pid$ProcessId"
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

    $processSnapshot = @($allGodot | ForEach-Object {
        $runtime = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
        [pscustomobject]@{
            ProcessId = $_.ProcessId
            Name = $_.Name
            CreationDate = $_.CreationDate
            CpuSeconds = if ($null -eq $runtime) { 0 } else { $runtime.CPU }
            WorkingSetBytes = if ($null -eq $runtime) { 0 } else { $runtime.WorkingSet64 }
            IsTarget = $_.ProcessId -eq $ProcessId
            CommandLine = $_.CommandLine
        }
    })
    $processSnapshot |
        ConvertTo-Json -Depth 4 |
        Set-Content (Join-Path $outputDirectory "godot-processes.json") -Encoding utf8

    Write-Host "WAR3_DOTNET_CAPTURE target_pid=$ProcessId label=$safeLabel"
    Write-Host "WAR3_DOTNET_CAPTURE command=$($target.CommandLine)"
    $counterPath = Join-Path $outputDirectory "runtime-counters.json"
    $counterProcess = $null
    $counterStdout = Join-Path $outputDirectory "dotnet-counters.stdout.txt"
    $counterStderr = Join-Path $outputDirectory "dotnet-counters.stderr.txt"
    if (-not $SkipCounters) {
        $counterDuration = [TimeSpan]::FromSeconds($CounterSeconds).ToString("c")
        $counterArguments = @(
            "dotnet-counters", "collect",
            "--process-id", $ProcessId,
            "--duration", $counterDuration,
            "--refresh-interval", 1,
            "--format", "json",
            "--output", $counterPath,
            "--counters", "System.Runtime"
        )
    }

    $tracePath = Join-Path $outputDirectory "managed.nettrace"
    $profiles = "dotnet-sampled-thread-time,dotnet-common"
    if ($IncludeGcVerbose) {
        $profiles += ",gc-verbose"
    }
    $traceDuration = [TimeSpan]::FromSeconds($TraceSeconds).ToString("c")
    $traceArguments = @(
        "dotnet-trace", "collect",
        "--process-id", $ProcessId,
        "--duration", $traceDuration,
        "--profile", $profiles,
        "--format", "Speedscope",
        "--output", $tracePath
    )

    # Stress scenes can have a short fixed lifetime. Start the CPU trace first
    # and collect counters concurrently so neither session consumes the other
    # session's capture window.
    $dotnetPath = (Get-Command dotnet).Source
    $traceStdout = Join-Path $outputDirectory "dotnet-trace.stdout.txt"
    $traceStderr = Join-Path $outputDirectory "dotnet-trace.stderr.txt"
    $traceProcess = Start-Process `
        -FilePath $dotnetPath `
        -ArgumentList $traceArguments `
        -WorkingDirectory $projectRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $traceStdout `
        -RedirectStandardError $traceStderr `
        -PassThru
    if (-not $SkipCounters) {
        $counterProcess = Start-Process `
            -FilePath $dotnetPath `
            -ArgumentList $counterArguments `
            -WorkingDirectory $projectRoot `
            -WindowStyle Hidden `
            -RedirectStandardOutput $counterStdout `
            -RedirectStandardError $counterStderr `
            -PassThru
    }

    $traceProcess.WaitForExit()
    if ($null -ne $counterProcess) {
        $counterProcess.WaitForExit()
    }
    Get-Content $traceStdout -ErrorAction SilentlyContinue | Out-Host
    Get-Content $traceStderr -ErrorAction SilentlyContinue | Out-Host
    if ($null -ne $counterProcess) {
        Get-Content $counterStdout -ErrorAction SilentlyContinue | Out-Host
        Get-Content $counterStderr -ErrorAction SilentlyContinue | Out-Host
    }
    # PowerShell occasionally loses ExitCode after redirected child-process
    # handles close, despite the trace being fully flushed. The output artifact
    # is authoritative; a missing or empty trace is the actual failure.
    if (-not (Test-Path $tracePath) -or
        (Get-Item $tracePath).Length -eq 0) {
        throw "dotnet-trace did not produce a trace. See $traceStderr"
    }
    if ($null -ne $counterProcess -and
        (-not (Test-Path $counterPath) -or (Get-Item $counterPath).Length -eq 0)) {
        Write-Warning "dotnet-counters did not produce data; the CPU trace is still usable."
    }

    if ($IncludeGcDump -and
        $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
        $gcDumpPath = Join-Path $outputDirectory "managed.gcdump"
        & dotnet dotnet-gcdump collect `
            --process-id $ProcessId `
            --timeout 30 `
            --output $gcDumpPath | Out-Host
        if ($LASTEXITCODE -eq 0 -and (Test-Path $gcDumpPath)) {
            & dotnet dotnet-gcdump report $gcDumpPath |
                Set-Content `
                    (Join-Path $outputDirectory "managed-gcdump-heapstat.txt") `
                    -Encoding utf8
        }
        else {
            Write-Warning "dotnet-gcdump could not capture the target; the CPU trace is still usable."
        }
    }

    $speedscopePath = Join-Path $outputDirectory "managed.speedscope.json"
    $exclusivePath = Join-Path $outputDirectory "dotnet-top-exclusive.txt"
    $inclusivePath = Join-Path $outputDirectory "dotnet-top-inclusive.txt"
    dotnet dotnet-trace report $tracePath topN --number 100 |
        Set-Content $exclusivePath -Encoding utf8
    dotnet dotnet-trace report $tracePath topN --number 100 --inclusive |
        Set-Content $inclusivePath -Encoding utf8

    $analysisArguments = @{
        SpeedscopePath = $speedscopePath
        OutputPrefix = Join-Path $outputDirectory "managed"
    }
    if (-not $SkipCounters -and (Test-Path $counterPath)) {
        $analysisArguments.CountersPath = $counterPath
    }
    & (Join-Path $PSScriptRoot "Analyze-War3Speedscope.ps1") @analysisArguments

    Write-Host "WAR3_DOTNET_CAPTURE complete=$outputDirectory"
}
finally {
    Pop-Location
}
