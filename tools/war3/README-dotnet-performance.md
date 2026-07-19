# War3 .NET performance capture

These scripts attach the official .NET EventPipe tools to a running Godot Mono
game. They do not modify the simulation while recording.

## Capture the rendered stress scene

1. Start **800-unit rendered stress** from the game's launch page and wait
   until the armies are visible.
2. From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/war3/Capture-War3DotnetProfile.ps1 `
  -TraceSeconds 30 -CounterSeconds 30 -IncludeGcVerbose
```

The script chooses the newest rendered, non-editor Godot process. When more
than one game is running, select the target explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File tools/war3/Capture-War3DotnetProfile.ps1 `
  -ProcessId 12345 -TraceSeconds 30 -CounterSeconds 30 -IncludeGcVerbose
```

Add `-IncludeGcDump` when a post-trace live-heap snapshot is needed. A gcdump
forces a managed GC and is therefore collected only after the CPU trace. It
must not be used as frame-time evidence.

For an unbiased EventPipe run, launch the workload with
`--war3-external-dotnet-profile`. This keeps stress gameplay, models,
animations, effects and rendering active while disabling the in-game
per-unit Stopwatch probes. The existing launch-page stress mode is unchanged.

## Output bundle

Each run creates `reports/dotnet/war3_<timestamp>_<label>_pid<pid>/` with:

- `managed.nettrace`: original EventPipe trace for PerfView or Visual Studio.
- `managed.speedscope.json`: flame graph input for speedscope.
- `managed.main-thread-hotspots.csv`: exclusive and inclusive main-thread CPU.
- `managed.main-thread-callers.csv`: hottest direct caller/callee edges.
- `managed.main-thread-project-origins.csv`: framework/native leaves attributed
  to the nearest method in this game's assembly.
- `managed.summary.md`: generated hotspot and runtime-counter report.
- `runtime-counters.json`: allocation, GC, CPU, exception and heap time series.
- `managed.gcdump` and `managed-gcdump-heapstat.txt`: optional live heap.
- `godot-processes.json`: process inventory proving which instance was sampled.

The analyzer deliberately uses the event-richest thread profile instead of
the all-thread `dotnet-trace topN` result. The latter is frequently dominated
by finalizer and EventPipe wait threads in Godot.

To re-run analysis without another capture:

```powershell
powershell -ExecutionPolicy Bypass -File tools/war3/Analyze-War3Speedscope.ps1 `
  -SpeedscopePath reports/dotnet/<capture>/managed.speedscope.json `
  -CountersPath reports/dotnet/<capture>/runtime-counters.json
```

Official tool references:

- <https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace>
- <https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-counters>
- <https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-gcdump>

