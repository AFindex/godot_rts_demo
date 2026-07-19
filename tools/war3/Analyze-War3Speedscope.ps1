param(
    [Parameter(Mandatory = $true)]
    [string]$SpeedscopePath,
    [string]$CountersPath = "",
    [int]$Top = 50,
    [int]$ThreadId = 0,
    [string]$OutputPrefix = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedSpeedscope = (Resolve-Path $SpeedscopePath).Path
if ([string]::IsNullOrWhiteSpace($OutputPrefix)) {
    $OutputPrefix = $resolvedSpeedscope -replace '\.speedscope\.json$', ''
}

$trace = Get-Content $resolvedSpeedscope -Raw | ConvertFrom-Json
if ($null -eq $trace.profiles -or $trace.profiles.Count -eq 0) {
    throw "Speedscope file has no profiles: $resolvedSpeedscope"
}

if ($ThreadId -gt 0) {
    $profile = $trace.profiles |
        Where-Object { $_.name -match "Thread \($ThreadId\)" } |
        Select-Object -First 1
    if ($null -eq $profile) {
        throw "Thread $ThreadId was not found in $resolvedSpeedscope"
    }
}
else {
    # The Godot main thread produces by far the richest sampled stack stream.
    # Selecting by event count avoids finalizer/wait threads that can remain
    # open for the whole trace and distort the all-thread dotnet-trace report.
    $profile = $trace.profiles |
        Sort-Object { if ($null -eq $_.events) { 0 } else { $_.events.Count } } -Descending |
        Select-Object -First 1
}

if ($profile.type -ne "evented") {
    throw "Unsupported Speedscope profile type '$($profile.type)'."
}

$exclusive = @{}
$inclusive = @{}
$callerEdges = @{}
$projectOrigins = @{}
$stack = [System.Collections.Generic.List[object]]::new()
$lastTimestamp = [double]$profile.startValue

foreach ($event in $profile.events) {
    $timestamp = [double]$event.at
    $delta = $timestamp - $lastTimestamp
    if ($delta -gt 0 -and $stack.Count -gt 0) {
        $depth = $stack.Count - 1
        $managedDepth = $depth
        $leaf = [int]$stack[$managedDepth].Frame
        # EventPipe's sampled-thread Speedscope conversion represents the
        # sample weight as an UNMANAGED_CODE_TIME leaf. Attribute that weight
        # to its managed caller so exclusive time names useful C# methods.
        if ($trace.shared.frames[$leaf].name -eq "UNMANAGED_CODE_TIME" -and
            $depth -gt 0) {
            $managedDepth--
            $leaf = [int]$stack[$managedDepth].Frame
        }
        $exclusive[$leaf] = [double]$exclusive[$leaf] + $delta
        if ($managedDepth -gt 0) {
            $caller = [int]$stack[$managedDepth - 1].Frame
            $edgeKey = "$leaf|$caller"
            $callerEdges[$edgeKey] = [double]$callerEdges[$edgeKey] + $delta

            # Attribute framework/native leaves to the nearest caller in this
            # game's assembly. This turns opaque Godot icall IDs and LINQ
            # internals into actionable War3 source-level origins.
            for ($originDepth = $managedDepth - 1;
                 $originDepth -ge 0;
                 $originDepth--) {
                $origin = [int]$stack[$originDepth].Frame
                $originName = [string]$trace.shared.frames[$origin].name
                if ($originName -match '^rts_demo_1!') {
                    $originKey = "$leaf|$origin"
                    $projectOrigins[$originKey] =
                        [double]$projectOrigins[$originKey] + $delta
                    break
                }
            }
        }
    }

    if ($event.type -eq "O") {
        $stack.Add([pscustomobject]@{
            Frame = [int]$event.frame
            Open = $timestamp
        })
    }
    elseif ($event.type -eq "C" -and $stack.Count -gt 0) {
        $entry = $stack[$stack.Count - 1]
        $stack.RemoveAt($stack.Count - 1)
        $frame = [int]$entry.Frame
        $inclusive[$frame] = [double]$inclusive[$frame] +
            ($timestamp - [double]$entry.Open)
    }
    $lastTimestamp = $timestamp
}

$duration = [double]$profile.endValue - [double]$profile.startValue
if ($duration -le 0) {
    throw "Speedscope profile duration is not positive."
}

$rows = foreach ($key in $exclusive.Keys) {
    $method = [string]$trace.shared.frames[[int]$key].name
    $method = $method -replace '^rts_demo_1!', ''
    $method = $method -replace '^GodotSharp!', ''
    $method = $method -replace '^System\.Private\.CoreLib\.il!', ''
    [pscustomobject]@{
        Method = $method
        ExclusiveMilliseconds = [math]::Round([double]$exclusive[$key], 3)
        ExclusivePercent = [math]::Round(
            100 * [double]$exclusive[$key] / $duration, 3)
        InclusiveMilliseconds = [math]::Round([double]$inclusive[$key], 3)
        InclusivePercent = [math]::Round(
            100 * [double]$inclusive[$key] / $duration, 3)
    }
}
$topRows = $rows |
    Sort-Object ExclusiveMilliseconds -Descending |
    Select-Object -First ([math]::Max(1, $Top))

$csvPath = "$OutputPrefix.main-thread-hotspots.csv"
$callerCsvPath = "$OutputPrefix.main-thread-callers.csv"
$originCsvPath = "$OutputPrefix.main-thread-project-origins.csv"
$markdownPath = "$OutputPrefix.summary.md"
$topRows | Export-Csv $csvPath -NoTypeInformation -Encoding utf8

$callerRows = foreach ($key in $callerEdges.Keys) {
    $parts = $key.Split('|')
    $callee = [string]$trace.shared.frames[[int]$parts[0]].name
    $caller = [string]$trace.shared.frames[[int]$parts[1]].name
    foreach ($prefix in @(
        '^rts_demo_1!',
        '^GodotSharp!',
        '^System\.Private\.CoreLib\.il!')) {
        $callee = $callee -replace $prefix, ''
        $caller = $caller -replace $prefix, ''
    }
    [pscustomobject]@{
        Callee = $callee
        Caller = $caller
        Milliseconds = [math]::Round([double]$callerEdges[$key], 3)
        Percent = [math]::Round(
            100 * [double]$callerEdges[$key] / $duration, 3)
    }
}
$topCallerRows = $callerRows |
    Sort-Object Milliseconds -Descending |
    Select-Object -First ([math]::Max(1, $Top))
$topCallerRows | Export-Csv $callerCsvPath -NoTypeInformation -Encoding utf8

$originRows = foreach ($key in $projectOrigins.Keys) {
    $parts = $key.Split('|')
    $leaf = [string]$trace.shared.frames[[int]$parts[0]].name
    $origin = [string]$trace.shared.frames[[int]$parts[1]].name
    foreach ($prefix in @(
        '^rts_demo_1!',
        '^GodotSharp!',
        '^System\.Private\.CoreLib\.il!')) {
        $leaf = $leaf -replace $prefix, ''
        $origin = $origin -replace $prefix, ''
    }
    [pscustomobject]@{
        Leaf = $leaf
        ProjectOrigin = $origin
        Milliseconds = [math]::Round([double]$projectOrigins[$key], 3)
        Percent = [math]::Round(
            100 * [double]$projectOrigins[$key] / $duration, 3)
    }
}
$topOriginRows = $originRows |
    Sort-Object Milliseconds -Descending |
    Select-Object -First ([math]::Max(1, $Top))
$topOriginRows | Export-Csv $originCsvPath -NoTypeInformation -Encoding utf8

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# War3 .NET sampled-stack summary")
$lines.Add("")
$lines.Add("- Source: ``$resolvedSpeedscope``")
$lines.Add("- Profile: ``$($profile.name)``")
$lines.Add("- Duration: $([math]::Round($duration, 1)) ms")
$lines.Add("- Events: $($profile.events.Count)")
$lines.Add("")
$lines.Add("| Rank | Exclusive | Inclusive | Method |")
$lines.Add("|---:|---:|---:|---|")
$rank = 1
foreach ($row in $topRows) {
    $escaped = $row.Method.Replace('|', '\|')
    $lines.Add(
        "| $rank | $($row.ExclusivePercent)% | " +
        "$($row.InclusivePercent)% | ``$escaped`` |")
    $rank++
}

$lines.Add("")
$lines.Add("## Hottest caller edges")
$lines.Add("")
$lines.Add("| Rank | Main-thread time | Caller | Callee |")
$lines.Add("|---:|---:|---|---|")
$rank = 1
foreach ($row in ($topCallerRows | Select-Object -First 30)) {
    $escapedCaller = $row.Caller.Replace('|', '\|')
    $escapedCallee = $row.Callee.Replace('|', '\|')
    $lines.Add(
        "| $rank | $($row.Percent)% | ``$escapedCaller`` | ``$escapedCallee`` |")
    $rank++
}

$lines.Add("")
$lines.Add("## Framework/native time by project origin")
$lines.Add("")
$lines.Add("| Rank | Main-thread time | Project origin | Leaf |")
$lines.Add("|---:|---:|---|---|")
$rank = 1
foreach ($row in ($topOriginRows | Select-Object -First 30)) {
    $escapedOrigin = $row.ProjectOrigin.Replace('|', '\|')
    $escapedLeaf = $row.Leaf.Replace('|', '\|')
    $lines.Add(
        "| $rank | $($row.Percent)% | ``$escapedOrigin`` | ``$escapedLeaf`` |")
    $rank++
}

if (-not [string]::IsNullOrWhiteSpace($CountersPath) -and
    (Test-Path $CountersPath)) {
    $counterData = Get-Content $CountersPath -Raw | ConvertFrom-Json
    $metricNames = @(
        'dotnet.gc.heap.total_allocated (By / 1 sec)',
        'dotnet.gc.pause.time (s / 1 sec)',
        'dotnet.process.cpu.time (s / 1 sec)',
        'dotnet.process.memory.working_set (By)',
        'dotnet.gc.collections ({collection} / 1 sec)',
        'dotnet.exceptions ({exception} / 1 sec)'
    )
    $lines.Add("")
    $lines.Add("## Runtime counters")
    $lines.Add("")
    $lines.Add("| Metric | Tags | Samples | Average | Maximum | Sum |")
    $lines.Add("|---|---|---:|---:|---:|---:|")
    foreach ($metricName in $metricNames) {
        $groups = $counterData.Events |
            Where-Object name -eq $metricName |
            Group-Object tags
        foreach ($group in $groups) {
            $measure = $group.Group.value |
                Measure-Object -Average -Maximum -Sum
            $lines.Add(
                "| $metricName | $($group.Name) | $($measure.Count) | " +
                "$([math]::Round($measure.Average, 3)) | " +
                "$([math]::Round($measure.Maximum, 3)) | " +
                "$([math]::Round($measure.Sum, 3)) |")
        }
    }
}

[System.IO.File]::WriteAllLines(
    $markdownPath,
    $lines,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "WAR3_DOTNET_ANALYSIS profile=$($profile.name) duration_ms=$([math]::Round($duration, 1))"
Write-Host "WAR3_DOTNET_ANALYSIS hotspots=$csvPath"
Write-Host "WAR3_DOTNET_ANALYSIS callers=$callerCsvPath"
Write-Host "WAR3_DOTNET_ANALYSIS origins=$originCsvPath"
Write-Host "WAR3_DOTNET_ANALYSIS summary=$markdownPath"
$topRows | Select-Object -First ([math]::Min(15, $topRows.Count)) |
    Format-Table ExclusivePercent,InclusivePercent,Method -AutoSize -Wrap
