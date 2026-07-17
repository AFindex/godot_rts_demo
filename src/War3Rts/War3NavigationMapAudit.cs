using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using RtsDemo.Simulation;
using War3Rts.Maps;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

internal enum War3SimpleNavigationStatus : byte
{
    Reachable,
    WorldBoundaryBlocked,
    TerrainBlocked,
    StaticObstacleBlocked,
    DynamicObstacleBlocked,
    ClearanceBlocked,
    DisconnectedComponent,
    ProviderReturnedEmpty,
    ProviderReturnedInvalidPath
}

internal enum War3RuntimeNavigationStatus : byte
{
    PathReady,
    AlreadyReached,
    Unreachable,
    InvalidPath,
    CommandRejected,
    Timeout
}

internal enum War3NavigationComparison : byte
{
    AgreeReachable,
    AgreeUnreachable,
    RuntimeAdjustedUnreachableRequest,
    RuntimeFailureDespiteSimplePath,
    LowLevelProviderContractViolation,
    RuntimePathContractViolation,
    RuntimeCommandRejected,
    RuntimeTimeout
}

internal sealed class War3NavigationAuditSample
{
    public required int Node { get; init; }
    public required int Column { get; init; }
    public required int Row { get; init; }
    public required AuditPoint Target { get; init; }
    public required int Component { get; init; }
    public required byte TerrainCliffLevel { get; init; }
    public required string TerrainPathing { get; init; }
    public required bool TerrainRamp { get; init; }
    public required War3SimpleNavigationStatus SimpleStatus { get; set; }
    public required bool ProviderReturnedPath { get; set; }
    public required bool ProviderPathValid { get; set; }
    public required int ProviderPathPoints { get; set; }
    public required float ProviderPathLength { get; set; }
    public required string SimpleDetail { get; set; }
    public War3RuntimeNavigationStatus RuntimeStatus { get; set; }
    public string RuntimeCommand { get; set; } = string.Empty;
    public AuditPoint RuntimeAssignedTarget { get; set; }
    public float RuntimeAssignmentOffset { get; set; }
    public int RuntimeTicks { get; set; }
    public int RuntimePathPoints { get; set; }
    public bool RuntimePathValid { get; set; }
    public int RuntimeRouteWaypoints { get; set; }
    public int RuntimeStartRegion { get; set; } = -2;
    public int RuntimeGoalRegion { get; set; } = -2;
    public string RuntimeTopology { get; set; } = "Unavailable";
    public string RuntimeLegResult { get; set; } = string.Empty;
    public string RuntimeRecovery { get; set; } = string.Empty;
    public string RuntimeDetail { get; set; } = string.Empty;
    public War3NavigationComparison Comparison { get; set; }
    public bool Critical { get; set; }
}

internal readonly record struct AuditPoint(float X, float Y)
{
    public static AuditPoint From(NVector2 value) => new(value.X, value.Y);
}

internal sealed record War3NavigationMapAuditResult(
    bool Passed,
    int SampleCount,
    int CriticalCount,
    int SimpleReachableCount,
    int RuntimePathCount,
    int ProviderContractViolationCount,
    int RuntimeRegressionCount,
    string JsonPath,
    string CsvPath,
    string MarkdownPath,
    string ImagePath,
    double SimpleMilliseconds,
    double RuntimeMilliseconds);

/// <summary>
/// Dedicated, deterministic navigation audit for the authored Warcraft map.
/// It first calls the low-level provider for every sampled cell, then restores
/// the same complete runtime snapshot and issues a real peasant move for every
/// target. This deliberately stays out of normal game startup.
/// </summary>
internal static class War3NavigationMapAudit
{
    public const string EnableArgument = "--war3-navigation-audit";
    private const float TickSeconds = 1f / 30f;
    private const int DefaultRuntimeTickLimit = 12;
    private const int RenderScale = 4;
    private const int RenderGap = 16;
    private static string? _progressPath;

    public static void BeginProgress(
        War3MapRuntime map,
        string[] arguments)
        => BeginProgress(map.Metadata.Id, arguments);

    public static void BeginProgress(
        string mapId,
        string[] arguments)
    {
        var outputDirectory = ResolveOutputDirectory(arguments, mapId);
        Directory.CreateDirectory(outputDirectory);
        _progressPath = Path.Combine(
            outputDirectory, "war3_navigation_audit.progress.log");
        File.WriteAllText(
            _progressPath,
            $"{DateTime.UtcNow:O} phase=map_bootstrap status=begin" +
            System.Environment.NewLine);
    }

    public static void TraceBootstrap(string message) =>
        AppendProgress($"phase=map_bootstrap {message}");

    public static War3NavigationMapAuditResult Run(
        RtsSimulation simulation,
        War3HumanRuntime runtime,
        War3MapRuntime map,
        string[] arguments)
    {
        var outputDirectory = ResolveOutputDirectory(arguments, map.Metadata.Id);
        Directory.CreateDirectory(outputDirectory);
        if (_progressPath is null) BeginProgress(map, arguments);
        TraceBootstrap("status=audit_run_entered");
        var stride = ReadIntegerArgument(
            arguments, "--war3-navigation-audit-stride=", 1, 1, 32);
        var tickLimit = ReadIntegerArgument(
            arguments, "--war3-navigation-audit-ticks=",
            DefaultRuntimeTickLimit, 1, 240);
        var worker = runtime.PlayerWorkers[0];
        StopScenarioWorkers(simulation, runtime);
        simulation.Tick(TickSeconds);
        var origin = simulation.Units.Positions[worker];
        var movementClass = simulation.Units.MovementClasses[worker];
        var navigationRadius = simulation.Units.NavigationRadii[worker];
        var connectivity = simulation
            .GetNavigationConnectivitySnapshotForDiagnostics(movementClass)
            ?? throw new InvalidOperationException(
                "War3 navigation audit requires GridPathProvider connectivity.");
        var startNode = FindNearestWalkableNode(connectivity, origin);
        if (startNode < 0)
        {
            throw new InvalidOperationException(
                $"Audit worker u{worker} has no walkable navigation cell near " +
                $"{Point(origin)}.");
        }
        var startComponent = connectivity.ComponentAt(startNode);
        var samples = CreateSamples(
            map.Terrain, connectivity, stride);
        var total = samples.Count;
        GD.Print(
            $"WAR3_NAV_AUDIT begin map={map.Metadata.Id} worker={worker} " +
            $"origin={Point(origin)} class={movementClass} " +
            $"radius={navigationRadius:0.##} grid={connectivity.Columns}x" +
            $"{connectivity.Rows} samples={total} stride={stride}");

        var simpleTimer = Stopwatch.StartNew();
        RunSimpleSampling(
            simulation, connectivity, samples, origin,
            startComponent, navigationRadius);
        simpleTimer.Stop();
        PrintProgress("simple", total, total, simpleTimer.Elapsed.TotalSeconds);

        var runtimeTimer = Stopwatch.StartNew();
        RunRuntimeSampling(
            simulation, samples, worker, origin,
            navigationRadius, tickLimit);
        runtimeTimer.Stop();
        PrintProgress("runtime", total, total, runtimeTimer.Elapsed.TotalSeconds);

        var jsonPath = Path.Combine(outputDirectory, "war3_navigation_audit.json");
        var csvPath = Path.Combine(outputDirectory, "war3_navigation_audit.csv");
        var markdownPath = Path.Combine(outputDirectory, "war3_navigation_audit.md");
        var imagePath = Path.Combine(outputDirectory, "war3_navigation_audit.png");
        var summary = BuildSummary(samples);
        WriteJson(
            jsonPath, map, simulation, connectivity, samples,
            worker, origin, movementClass, navigationRadius,
            startNode, startComponent, stride, tickLimit,
            simpleTimer.Elapsed.TotalMilliseconds,
            runtimeTimer.Elapsed.TotalMilliseconds, summary);
        WriteCsv(csvPath, samples);
        WriteMarkdown(
            markdownPath, map, connectivity, samples,
            worker, origin, movementClass, navigationRadius,
            stride, tickLimit, summary,
            simpleTimer.Elapsed.TotalMilliseconds,
            runtimeTimer.Elapsed.TotalMilliseconds,
            jsonPath, csvPath, imagePath);
        RenderComparison(imagePath, connectivity, samples, startNode);

        var passed = summary.Critical == 0;
        GD.Print(
            $"WAR3_NAV_AUDIT {(passed ? "PASS" : "FAIL")} " +
            $"samples={total} simple_reachable={summary.SimpleReachable} " +
            $"runtime_paths={summary.RuntimePaths} critical={summary.Critical} " +
            $"provider_contract={summary.ProviderContractViolations} " +
            $"runtime_regressions={summary.RuntimeRegressions} " +
            $"simple_ms={simpleTimer.Elapsed.TotalMilliseconds:0.###} " +
            $"runtime_ms={runtimeTimer.Elapsed.TotalMilliseconds:0.###} " +
            $"report={markdownPath} image={imagePath}");
        AppendProgress(
            $"phase=complete status={(passed ? "pass" : "fail")} " +
            $"critical={summary.Critical}");
        return new War3NavigationMapAuditResult(
            passed,
            total,
            summary.Critical,
            summary.SimpleReachable,
            summary.RuntimePaths,
            summary.ProviderContractViolations,
            summary.RuntimeRegressions,
            jsonPath,
            csvPath,
            markdownPath,
            imagePath,
            simpleTimer.Elapsed.TotalMilliseconds,
            runtimeTimer.Elapsed.TotalMilliseconds);
    }

    private static void StopScenarioWorkers(
        RtsSimulation simulation,
        War3HumanRuntime runtime)
    {
        var playerStop = simulation.IssuePlayerStop(
            War3HumanScenario.PlayerId, runtime.PlayerWorkers);
        var enemyStop = runtime.EnemyWorkers.Length == 0
            ? new PlayerOrderCommandResult(PlayerOrderCommandCode.Success)
            : simulation.IssuePlayerStop(
                War3HumanScenario.EnemyId, runtime.EnemyWorkers);
        if (!playerStop.Succeeded || !enemyStop.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not prepare navigation audit workers: " +
                $"player={playerStop.Code}, enemy={enemyStop.Code}.");
        }
    }

    private static List<War3NavigationAuditSample> CreateSamples(
        TerrainMapSnapshot terrain,
        NavigationConnectivitySnapshot connectivity,
        int stride)
    {
        var result = new List<War3NavigationAuditSample>(
            (connectivity.Columns + stride - 1) / stride *
            ((connectivity.Rows + stride - 1) / stride));
        for (var row = 0; row < connectivity.Rows; row += stride)
        for (var column = 0; column < connectivity.Columns; column += stride)
        {
            var node = row * connectivity.Columns + column;
            var target = connectivity.CellCenter(node);
            var cell = terrain.TryCellAt(target, out var terrainColumn,
                    out var terrainRow)
                ? terrain.Cell(terrainColumn, terrainRow)
                : default;
            result.Add(new War3NavigationAuditSample
            {
                Node = node,
                Column = column,
                Row = row,
                Target = AuditPoint.From(target),
                Component = connectivity.IsWalkable(node)
                    ? connectivity.ComponentAt(node)
                    : -1,
                TerrainCliffLevel = cell.CliffLevel,
                TerrainPathing = cell.Pathing.ToString(),
                TerrainRamp = cell.IsRamp,
                SimpleStatus = War3SimpleNavigationStatus.ProviderReturnedEmpty,
                ProviderReturnedPath = false,
                ProviderPathValid = false,
                ProviderPathPoints = 0,
                ProviderPathLength = 0f,
                SimpleDetail = string.Empty
            });
        }
        return result;
    }

    private static void RunSimpleSampling(
        RtsSimulation simulation,
        NavigationConnectivitySnapshot connectivity,
        List<War3NavigationAuditSample> samples,
        NVector2 origin,
        int startComponent,
        float navigationRadius)
    {
        var staticObstacles = simulation.World.Obstacles.ToArray();
        var dynamicObstacles = simulation.World.DynamicOccupancy.Snapshot();
        var distance = FloodFill(
            simulation.World,
            connectivity,
            startComponent,
            origin,
            navigationRadius);
        var progressInterval = Math.Max(1, samples.Count / 20);
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var target = ToVector(sample.Target);
            var reachable = distance[sample.Node] >= 0;
            sample.ProviderReturnedPath = reachable;
            sample.ProviderPathValid = reachable;
            sample.ProviderPathPoints = reachable ? distance[sample.Node] + 1 : 0;
            sample.ProviderPathLength = reachable
                ? distance[sample.Node] * connectivity.CellSize
                : 0f;

            var blocked = ClassifyBlockedTarget(
                simulation.World, target, navigationRadius,
                staticObstacles, dynamicObstacles);
            if (blocked is { } blockedStatus)
            {
                sample.SimpleStatus = blockedStatus;
                sample.SimpleDetail =
                    "Exact endpoint is not standable for the audit peasant.";
            }
            else if (!connectivity.IsWalkable(sample.Node))
            {
                sample.SimpleStatus = War3SimpleNavigationStatus.ClearanceBlocked;
                sample.SimpleDetail =
                    "Clearance raster marks this cell as not walkable.";
            }
            else if (!reachable)
            {
                sample.SimpleStatus =
                    War3SimpleNavigationStatus.DisconnectedComponent;
                sample.SimpleDetail =
                    "Cell is standable but cannot be reached when every grid " +
                    "edge is checked with the same swept-disc contract as runtime A*.";
            }
            else
            {
                sample.SimpleStatus = War3SimpleNavigationStatus.Reachable;
                sample.SimpleDetail =
                    "Exact cell is reachable in the low-level clearance grid.";
            }

            if ((index + 1) % progressInterval == 0)
            {
                PrintProgress(
                    "simple", index + 1, samples.Count,
                    timer.Elapsed.TotalSeconds);
            }
        }
    }

    private static void RunRuntimeSampling(
        RtsSimulation simulation,
        List<War3NavigationAuditSample> samples,
        int worker,
        NVector2 origin,
        float navigationRadius,
        int tickLimit)
    {
        var progressInterval = Math.Max(1, samples.Count / 20);
        var deepDiagnosticInterval = Math.Max(1, samples.Count / 128);
        var timer = Stopwatch.StartNew();
        Span<int> selected = stackalloc int[1];
        selected[0] = worker;
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            ResetProbeWorker(simulation, worker, origin);
            var target = ToVector(sample.Target);
            var command = simulation.IssuePlayerMove(
                War3HumanScenario.PlayerId, selected, target);
            sample.RuntimeCommand = command.Code.ToString();
            if (!command.Succeeded)
            {
                sample.RuntimeStatus =
                    War3RuntimeNavigationStatus.CommandRejected;
                sample.RuntimeDetail = $"Player move rejected with {command.Code}.";
                FinalizeComparison(sample);
                continue;
            }

            var units = simulation.Units;
            var commandVersion = units.CommandVersions[worker];
            sample.RuntimeAssignedTarget = AuditPoint.From(
                units.SlotTargets[worker]);
            sample.RuntimeAssignmentOffset = NVector2.Distance(
                target, units.SlotTargets[worker]);
            var route = simulation.LastIssuedGroupRoute;
            sample.RuntimeRouteWaypoints = route.Waypoints.Length;
            sample.RuntimeTopology = DescribeRuntimeTopology(
                simulation,
                origin,
                units.SlotTargets[worker],
                navigationRadius,
                route.Waypoints.Length,
                out var startRegion,
                out var goalRegion);
            sample.RuntimeStartRegion = startRegion;
            sample.RuntimeGoalRegion = goalRegion;
            var resolved = false;
            for (var tick = 1; tick <= tickLimit; tick++)
            {
                simulation.Tick(TickSeconds);
                sample.RuntimeTicks = tick;
                var path = units.Paths[worker];
                if (path is not null &&
                    path.CommandVersion == commandVersion &&
                    !units.PathPending[worker])
                {
                    var assigned = units.SlotTargets[worker];
                    var validation = ValidatePath(
                        simulation.World, path.Points, origin,
                        assigned, navigationRadius);
                    sample.RuntimePathPoints = path.Points.Length;
                    sample.RuntimePathValid = validation.Valid;
                    sample.RuntimeStatus = validation.Valid
                        ? War3RuntimeNavigationStatus.PathReady
                        : War3RuntimeNavigationStatus.InvalidPath;
                    sample.RuntimeDetail = validation.Valid
                        ? "Runtime produced a valid path to its assigned slot."
                        : validation.Detail;
                    resolved = true;
                    break;
                }
                if (units.MovementLegResults[worker] ==
                        UnitMovementLegResult.Reached ||
                    units.Modes[worker] == UnitMoveMode.Arrived)
                {
                    sample.RuntimeStatus =
                        War3RuntimeNavigationStatus.AlreadyReached;
                    sample.RuntimePathValid = true;
                    sample.RuntimeDetail =
                        "Runtime completed the ground-point leg before sampling the path.";
                    resolved = true;
                    break;
                }
                if (units.MovementLegResults[worker] ==
                        UnitMovementLegResult.Unreachable ||
                    units.RecoveryStages[worker] == RecoveryStage.Unreachable)
                {
                    sample.RuntimeStatus =
                        War3RuntimeNavigationStatus.Unreachable;
                    sample.RuntimeDetail = sample.SimpleStatus ==
                            War3SimpleNavigationStatus.Reachable
                        ? route.Waypoints.Length == 0
                            ? "Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned " +
                              $"(topology={sample.RuntimeTopology}, " +
                              $"regions={sample.RuntimeStartRegion}->" +
                              $"{sample.RuntimeGoalRegion})."
                            : $"Runtime rejected a low-level reachable target after assigning " +
                              $"{route.Waypoints.Length} high-level waypoint(s)."
                        : $"Runtime agrees with simple classification " +
                          $"{sample.SimpleStatus}.";
                    if (sample.SimpleStatus ==
                            War3SimpleNavigationStatus.Reachable &&
                        index % deepDiagnosticInterval == 0)
                    {
                        sample.RuntimeDetail += " Deep probe: " +
                            DiagnoseRuntimeFailure(
                                simulation,
                                origin,
                                units.SlotTargets[worker],
                                route.Waypoints,
                                navigationRadius);
                    }
                    resolved = true;
                    break;
                }
            }
            if (!resolved)
            {
                sample.RuntimeStatus = War3RuntimeNavigationStatus.Timeout;
                sample.RuntimeDetail =
                    $"No path terminal state after {tickLimit} complete simulation ticks.";
            }
            sample.RuntimeLegResult =
                units.MovementLegResults[worker].ToString();
            sample.RuntimeRecovery = units.RecoveryStages[worker].ToString();
            FinalizeComparison(sample);

            if ((index + 1) % progressInterval == 0)
            {
                PrintProgress(
                    "runtime", index + 1, samples.Count,
                    timer.Elapsed.TotalSeconds);
            }
        }
    }

    private static void ResetProbeWorker(
        RtsSimulation simulation,
        int worker,
        NVector2 origin)
    {
        Span<int> selected = stackalloc int[1];
        selected[0] = worker;
        var stop = simulation.IssuePlayerStop(
            War3HumanScenario.PlayerId, selected);
        if (!stop.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not reset audit peasant u{worker}: {stop.Code}.");
        }
        var units = simulation.Units;
        units.Positions[worker] = origin;
        units.PreviousPositions[worker] = origin;
        units.Velocities[worker] = NVector2.Zero;
        units.PreferredVelocities[worker] = NVector2.Zero;
        units.NextVelocities[worker] = NVector2.Zero;
        units.SlotTargets[worker] = origin;
        units.MoveGoals[worker] = origin;
    }

    private static int[] FloodFill(
        StaticWorld world,
        NavigationConnectivitySnapshot snapshot,
        int startComponent,
        NVector2 origin,
        float navigationRadius)
    {
        var distance = new int[snapshot.NodeCount];
        Array.Fill(distance, -1);
        var start = FindNearestWalkableNode(snapshot, origin);
        if (start < 0 || snapshot.ComponentAt(start) != startComponent)
            return distance;
        var queue = new Queue<int>();
        distance[start] = 0;
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            var currentColumn = current % snapshot.Columns;
            var currentRow = current / snapshot.Columns;
            var offsets = NavigationConnectivityAnalyzer.NeighborOffsets;
            for (var offsetIndex = 0; offsetIndex < offsets.Length; offsetIndex++)
            {
                var offset = offsets[offsetIndex];
                var column = currentColumn + offset.Column;
                var row = currentRow + offset.Row;
                if ((uint)column >= (uint)snapshot.Columns ||
                    (uint)row >= (uint)snapshot.Rows)
                    continue;
                var neighbor = row * snapshot.Columns + column;
                if (distance[neighbor] >= 0 || !snapshot.IsWalkable(neighbor))
                    continue;
                if (offset.Column != 0 && offset.Row != 0)
                {
                    var horizontal =
                        currentRow * snapshot.Columns + column;
                    var vertical = row * snapshot.Columns + currentColumn;
                    if (!snapshot.IsWalkable(horizontal) ||
                        !snapshot.IsWalkable(vertical))
                        continue;
                }
                if (!world.IsSegmentFree(
                        snapshot.CellCenter(current),
                        snapshot.CellCenter(neighbor),
                        navigationRadius))
                    continue;
                distance[neighbor] = distance[current] + 1;
                queue.Enqueue(neighbor);
            }
        }
        return distance;
    }

    private static void FinalizeComparison(War3NavigationAuditSample sample)
    {
        var simpleReachable =
            sample.SimpleStatus == War3SimpleNavigationStatus.Reachable &&
            sample.ProviderPathValid;
        var providerViolation =
            sample.ProviderReturnedPath && !sample.ProviderPathValid;
        var runtimeSuccess = sample.RuntimeStatus is
            War3RuntimeNavigationStatus.PathReady or
            War3RuntimeNavigationStatus.AlreadyReached;
        if (runtimeSuccess &&
            sample.RuntimeTopology == "DifferentRegionsNoRoute")
        {
            sample.RuntimeTopology =
                "DifferentRegionsLowLevelFallback";
        }
        sample.Comparison = providerViolation
            ? War3NavigationComparison.LowLevelProviderContractViolation
            : sample.RuntimeStatus == War3RuntimeNavigationStatus.InvalidPath
                ? War3NavigationComparison.RuntimePathContractViolation
                : sample.RuntimeStatus ==
                    War3RuntimeNavigationStatus.CommandRejected
                    ? War3NavigationComparison.RuntimeCommandRejected
                    : sample.RuntimeStatus == War3RuntimeNavigationStatus.Timeout
                        ? War3NavigationComparison.RuntimeTimeout
                        : simpleReachable && runtimeSuccess
                            ? War3NavigationComparison.AgreeReachable
                            : simpleReachable
                                ? War3NavigationComparison
                                    .RuntimeFailureDespiteSimplePath
                                : runtimeSuccess
                                    ? War3NavigationComparison
                                        .RuntimeAdjustedUnreachableRequest
                                    : War3NavigationComparison.AgreeUnreachable;
        sample.Critical = sample.Comparison is
            War3NavigationComparison.LowLevelProviderContractViolation or
            War3NavigationComparison.RuntimePathContractViolation or
            War3NavigationComparison.RuntimeCommandRejected or
            War3NavigationComparison.RuntimeTimeout or
            War3NavigationComparison.RuntimeFailureDespiteSimplePath ||
            sample.SimpleStatus is
                War3SimpleNavigationStatus.ProviderReturnedEmpty or
                War3SimpleNavigationStatus.ProviderReturnedInvalidPath;
    }

    private static War3SimpleNavigationStatus? ClassifyBlockedTarget(
        StaticWorld world,
        NVector2 target,
        float radius,
        SimRect[] staticObstacles,
        DynamicFootprint[] dynamicObstacles)
    {
        if (!world.Bounds.Inset(radius).Contains(target))
            return War3SimpleNavigationStatus.WorldBoundaryBlocked;
        if (world.Terrain is not null &&
            !world.Terrain.IsDiscTraversable(target, radius))
            return War3SimpleNavigationStatus.TerrainBlocked;
        for (var index = 0; index < staticObstacles.Length; index++)
        {
            if (staticObstacles[index].OverlapsDisc(target, radius))
                return War3SimpleNavigationStatus.StaticObstacleBlocked;
        }
        for (var index = 0; index < dynamicObstacles.Length; index++)
        {
            if (dynamicObstacles[index].Bounds.OverlapsDisc(target, radius))
                return War3SimpleNavigationStatus.DynamicObstacleBlocked;
        }
        return null;
    }

    private static PathValidation ValidatePath(
        StaticWorld world,
        ReadOnlySpan<NVector2> path,
        NVector2 start,
        NVector2 goal,
        float radius)
    {
        if (path.Length == 0)
            return new(false, "Provider returned an empty path.");
        if (NVector2.DistanceSquared(path[0], start) > 1f)
        {
            return new(false,
                $"Path starts at {Point(path[0])}, not {Point(start)}.");
        }
        if (NVector2.DistanceSquared(path[^1], goal) > 1f)
        {
            return new(false,
                $"Path ends at {Point(path[^1])}, not {Point(goal)}.");
        }
        if (!world.IsDiscFree(goal, radius))
        {
            return new(false,
                $"Exact endpoint {Point(goal)} is not free for radius {radius:0.##}.");
        }
        for (var index = 1; index < path.Length; index++)
        {
            if (!world.IsSegmentFree(path[index - 1], path[index], radius))
            {
                return new(false,
                    $"Segment {index - 1}->{index} is blocked: " +
                    $"{Point(path[index - 1])}->{Point(path[index])}; " +
                    DescribeSegmentBlocker(
                        world, path[index - 1], path[index], radius));
            }
        }
        return new(true, "Path contract is valid.");
    }

    private static string DescribeSegmentBlocker(
        StaticWorld world,
        NVector2 from,
        NVector2 to,
        float radius)
    {
        if (!world.Bounds.Inset(radius).Contains(to))
            return "cause=world_boundary";
        if (world.Terrain is not null &&
            !world.Terrain.IsSegmentTraversable(from, to, radius))
            return "cause=terrain_transition";
        var obstacles = world.Obstacles;
        for (var index = 0; index < obstacles.Length; index++)
        {
            if (obstacles[index].IntersectsSweptDisc(from, to, radius))
            {
                return $"cause=static_obstacle obstacle={index} " +
                       $"bounds={Rect(obstacles[index])}";
            }
        }
        var footprints = world.DynamicOccupancy.Snapshot();
        for (var index = 0; index < footprints.Length; index++)
        {
            if (footprints[index].Bounds.IntersectsSweptDisc(
                    from, to, radius))
            {
                return $"cause=dynamic_building footprint=" +
                       $"{footprints[index].Id.Value} " +
                       $"bounds={Rect(footprints[index].Bounds)}";
            }
        }
        return "cause=unknown_world_constraint";
    }

    private static string DescribeRuntimeTopology(
        RtsSimulation simulation,
        NVector2 start,
        NVector2 goal,
        float navigationRadius,
        int waypointCount,
        out int startRegion,
        out int goalRegion)
    {
        startRegion = -2;
        goalRegion = -2;
        if (simulation.GroupRoutePlanner is
            PortalGraphRoutePlanner portalPlanner)
        {
            if (portalPlanner.Nodes.Length == 0)
                return "EmptyPortalGraph";
            return waypointCount == 0
                ? "PortalGraphNoRoute"
                : "PortalGraphRoute";
        }
        var topology = simulation.GroupRoutePlanner switch
        {
            DynamicTerrainTopologyRoutePlanner dynamicPlanner =>
                dynamicPlanner.CurrentTopology,
            TerrainTopologyRoutePlanner terrainPlanner =>
                terrainPlanner.Topology,
            _ => null
        };
        if (topology is null)
            return "Unavailable";
        var movementClass = MovementClearance.FromPhysicalRadius(
            navigationRadius).Class;
        var layer = topology.Layer(movementClass);
        startRegion = layer.RegionAt(start);
        goalRegion = layer.RegionAt(goal);
        if (startRegion < 0 || goalRegion < 0)
            return "EndpointOutsideRegion";
        if (startRegion == goalRegion)
            return waypointCount == 0
                ? "SameRegion"
                : "SameRegionUnexpectedRoute";
        return waypointCount == 0
            ? "DifferentRegionsNoRoute"
            : "DifferentRegionsWithRoute";
    }

    private static string DiagnoseRuntimeFailure(
        RtsSimulation simulation,
        NVector2 origin,
        NVector2 assignedTarget,
        ReadOnlySpan<NVector2> route,
        float navigationRadius)
    {
        var direct = simulation.FindNavigationPathForDiagnostics(
            origin, assignedTarget, navigationRadius);
        var directValidation = ValidatePath(
            simulation.World, direct, origin, assignedTarget, navigationRadius);
        if (direct.Length == 0)
        {
            return "The authoritative low-level provider also returned an " +
                   $"empty path (originFree=" +
                   $"{simulation.World.IsDiscFree(origin, navigationRadius)}, " +
                   $"targetFree=" +
                   $"{simulation.World.IsDiscFree(assignedTarget, navigationRadius)}, " +
                   $"directSegmentFree=" +
                   $"{simulation.World.IsSegmentFree(origin, assignedTarget, navigationRadius)}).";
        }
        if (directValidation.Valid)
        {
            return route.Length == 0
                ? "Runtime rejected an assigned target for which the low-level direct path is valid."
                : $"Runtime high-level route has {route.Length} waypoint(s), but the " +
                  "same assigned target has a valid low-level direct path.";
        }

        var current = origin;
        for (var index = 0; index <= route.Length; index++)
        {
            var target = index < route.Length ? route[index] : assignedTarget;
            var segment = simulation.FindNavigationPathForDiagnostics(
                current, target, navigationRadius);
            var validation = ValidatePath(
                simulation.World, segment, current, target, navigationRadius);
            if (!validation.Valid)
            {
                var label = index < route.Length
                    ? $"high-level segment {index}"
                    : "final assigned-target segment";
                return $"{label} failed: {validation.Detail}";
            }
            current = target;
        }
        return "Every raw high-level segment is valid in isolation, but runtime path assembly rejected the route.";
    }

    private static int FindNearestWalkableNode(
        NavigationConnectivitySnapshot snapshot,
        NVector2 point)
    {
        var baseColumn = Math.Clamp(
            (int)MathF.Floor(
                (point.X - snapshot.WorldBounds.Min.X) / snapshot.CellSize),
            0, snapshot.Columns - 1);
        var baseRow = Math.Clamp(
            (int)MathF.Floor(
                (point.Y - snapshot.WorldBounds.Min.Y) / snapshot.CellSize),
            0, snapshot.Rows - 1);
        var maximumRing = Math.Max(snapshot.Columns, snapshot.Rows);
        for (var ring = 0; ring <= maximumRing; ring++)
        {
            for (var row = baseRow - ring; row <= baseRow + ring; row++)
            for (var column = baseColumn - ring; column <= baseColumn + ring;
                 column++)
            {
                if ((uint)column >= (uint)snapshot.Columns ||
                    (uint)row >= (uint)snapshot.Rows ||
                    ring > 0 &&
                    Math.Abs(column - baseColumn) < ring &&
                    Math.Abs(row - baseRow) < ring)
                    continue;
                var node = row * snapshot.Columns + column;
                if (snapshot.IsWalkable(node)) return node;
            }
        }
        return -1;
    }

    private static AuditSummary BuildSummary(
        List<War3NavigationAuditSample> samples) => new(
        samples.Count(value => value.Critical),
        samples.Count(value =>
            value.SimpleStatus == War3SimpleNavigationStatus.Reachable &&
            value.ProviderPathValid),
        samples.Count(value => value.RuntimeStatus is
            War3RuntimeNavigationStatus.PathReady or
            War3RuntimeNavigationStatus.AlreadyReached),
        samples.Count(value =>
            value.ProviderReturnedPath && !value.ProviderPathValid),
        samples.Count(value =>
            value.Comparison ==
            War3NavigationComparison.RuntimeFailureDespiteSimplePath));

    private static void WriteJson(
        string path,
        War3MapRuntime map,
        RtsSimulation simulation,
        NavigationConnectivitySnapshot connectivity,
        List<War3NavigationAuditSample> samples,
        int worker,
        NVector2 origin,
        MovementClass movementClass,
        float navigationRadius,
        int startNode,
        int startComponent,
        int stride,
        int tickLimit,
        double simpleMilliseconds,
        double runtimeMilliseconds,
        AuditSummary summary)
    {
        var payload = new
        {
            generatedUtc = DateTime.UtcNow,
            map = new
            {
                map.Metadata.Id,
                mapHash = map.StableHashText,
                terrainHash = map.Terrain.StableHashText
            },
            audit = new
            {
                worker,
                origin = AuditPoint.From(origin),
                movementClass = movementClass.ToString(),
                navigationRadius,
                startNode,
                startComponent,
                stride,
                tickLimit,
                connectivity.Columns,
                connectivity.Rows,
                connectivity.CellSize,
                connectivity.ComponentCount,
                connectivity.WorldRevision,
                connectivity.Source,
                dynamicFootprints = simulation.World.DynamicOccupancy.Count,
                buildings = simulation.Construction.Count,
                simpleMilliseconds,
                runtimeMilliseconds
            },
            summary,
            simpleStatuses = CountBy(samples, value => value.SimpleStatus),
            runtimeStatuses = CountBy(samples, value => value.RuntimeStatus),
            runtimeTopologies = CountBy(samples, value => value.RuntimeTopology),
            comparisons = CountBy(samples, value => value.Comparison),
            components = connectivity.Components.ToArray(),
            samples
        };
        File.WriteAllText(path, JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
    }

    private static void WriteCsv(
        string path,
        List<War3NavigationAuditSample> samples)
    {
        var output = new StringBuilder(samples.Count * 220);
        output.AppendLine(
            "node,column,row,x,y,component,terrain_cliff,terrain_pathing," +
            "terrain_ramp,simple_status,provider_returned,provider_valid," +
            "provider_points,provider_length,runtime_status,command," +
            "assigned_x,assigned_y,assignment_offset,runtime_ticks," +
            "runtime_points,runtime_valid,route_waypoints,leg,recovery," +
            "start_region,goal_region,runtime_topology,comparison,critical," +
            "simple_detail,runtime_detail");
        foreach (var value in samples)
        {
            output.Append(value.Node).Append(',')
                .Append(value.Column).Append(',')
                .Append(value.Row).Append(',')
                .Append(F(value.Target.X)).Append(',')
                .Append(F(value.Target.Y)).Append(',')
                .Append(value.Component).Append(',')
                .Append(value.TerrainCliffLevel).Append(',')
                .Append(Csv(value.TerrainPathing)).Append(',')
                .Append(value.TerrainRamp).Append(',')
                .Append(value.SimpleStatus).Append(',')
                .Append(value.ProviderReturnedPath).Append(',')
                .Append(value.ProviderPathValid).Append(',')
                .Append(value.ProviderPathPoints).Append(',')
                .Append(F(value.ProviderPathLength)).Append(',')
                .Append(value.RuntimeStatus).Append(',')
                .Append(value.RuntimeCommand).Append(',')
                .Append(F(value.RuntimeAssignedTarget.X)).Append(',')
                .Append(F(value.RuntimeAssignedTarget.Y)).Append(',')
                .Append(F(value.RuntimeAssignmentOffset)).Append(',')
                .Append(value.RuntimeTicks).Append(',')
                .Append(value.RuntimePathPoints).Append(',')
                .Append(value.RuntimePathValid).Append(',')
                .Append(value.RuntimeRouteWaypoints).Append(',')
                .Append(value.RuntimeLegResult).Append(',')
                .Append(value.RuntimeRecovery).Append(',')
                .Append(value.RuntimeStartRegion).Append(',')
                .Append(value.RuntimeGoalRegion).Append(',')
                .Append(value.RuntimeTopology).Append(',')
                .Append(value.Comparison).Append(',')
                .Append(value.Critical).Append(',')
                .Append(Csv(value.SimpleDetail)).Append(',')
                .Append(Csv(value.RuntimeDetail)).AppendLine();
        }
        File.WriteAllText(path, output.ToString());
    }

    private static void WriteMarkdown(
        string path,
        War3MapRuntime map,
        NavigationConnectivitySnapshot connectivity,
        List<War3NavigationAuditSample> samples,
        int worker,
        NVector2 origin,
        MovementClass movementClass,
        float navigationRadius,
        int stride,
        int tickLimit,
        AuditSummary summary,
        double simpleMilliseconds,
        double runtimeMilliseconds,
        string jsonPath,
        string csvPath,
        string imagePath)
    {
        var output = new StringBuilder();
        output.AppendLine("# War3 遭遇战全图导航审计")
            .AppendLine()
            .AppendLine($"- 地图：`{map.Metadata.Id}` / `{map.StableHashText}`")
            .AppendLine($"- 农民：`u{worker}`，基地起点 `{Point(origin)}`")
            .AppendLine($"- 净空：`{movementClass}` / `{navigationRadius:0.##}`")
            .AppendLine($"- 网格：`{connectivity.Columns}×{connectivity.Rows}`，" +
                        $"cell `{connectivity.CellSize:0.##}`，stride `{stride}`")
            .AppendLine($"- 运行时每点最多 `{tickLimit}` 个完整模拟 Tick")
            .AppendLine($"- 采样：`{samples.Count}`，关键失败：`{summary.Critical}`")
            .AppendLine($"- 耗时：底层 `{simpleMilliseconds:0.###} ms`，" +
                        $"运行时 `{runtimeMilliseconds:0.###} ms`")
            .AppendLine()
            .AppendLine("## 汇总")
            .AppendLine()
            .AppendLine($"- 底层网格连通可达：{summary.SimpleReachable}")
            .AppendLine($"- 运行时获得路径：{summary.RuntimePaths}")
            .AppendLine($"- 底层返回不安全路径：{summary.ProviderContractViolations}")
            .AppendLine($"- 底层可达但运行时失败：{summary.RuntimeRegressions}")
            .AppendLine();
        AppendCountTable(
            output, "底层结果", CountBy(samples, value => value.SimpleStatus));
        AppendCountTable(
            output, "运行时结果", CountBy(samples, value => value.RuntimeStatus));
        AppendCountTable(
            output, "高层拓扑结果", CountBy(samples, value => value.RuntimeTopology));
        AppendCountTable(
            output, "对照结果", CountBy(samples, value => value.Comparison));
        var deepCauses = samples
            .Select(value => ExtractDeepCause(value.RuntimeDetail))
            .Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        if (deepCauses.Count > 0)
            AppendCountTable(output, "分布式深探针阻断原因", deepCauses);
        output.AppendLine("## 自动原因判断")
            .AppendLine();
        if (summary.ProviderContractViolations > 0)
        {
            output.AppendLine(
                $"- 有 {summary.ProviderContractViolations} 个点的底层 provider " +
                "返回了非空路径，但终点或某一段实际不可通。这是 provider 合约问题，" +
                "运行时随后拒绝这些路径是合理的。" );
        }
        if (summary.RuntimeRegressions > 0)
        {
            output.AppendLine(
                $"- 有 {summary.RuntimeRegressions} 个点底层网格连通，但完整运行时" +
                "仍失败。优先检查对应行的高层 route、slot 偏移和 RuntimeDetail。" );
        }
        var missingTopologyRoutes = samples.Count(value =>
            value.RuntimeTopology == "DifferentRegionsNoRoute" &&
            value.RuntimeStatus == War3RuntimeNavigationStatus.Unreachable);
        if (missingTopologyRoutes > 0)
        {
            var missingRouteRegressions = samples.Count(value =>
                value.RuntimeTopology == "DifferentRegionsNoRoute" &&
                value.Comparison ==
                    War3NavigationComparison.RuntimeFailureDespiteSimplePath);
            output.AppendLine(missingRouteRegressions > 0
                ? $"- 有 {missingTopologyRoutes} 个不同地形 region 的请求没有高层" +
                  $"坡道 waypoint，其中 {missingRouteRegressions} 个底层可达目标仍失败；" +
                  "这是需要继续补齐的拓扑覆盖缺口。"
                : $"- 有 {missingTopologyRoutes} 个不同地形 region 的不可站立目标没有" +
                  "高层坡道 waypoint；低层安全寻路没有穿越非法地形，结果与底层" +
                  "不可达分类一致，因此不构成路径正确性回归。");
        }
        var emptyPortalGraph = samples.Count(value =>
            value.RuntimeTopology == "EmptyPortalGraph");
        if (emptyPortalGraph > 0)
        {
            output.AppendLine(
                $"- 本次 {emptyPortalGraph} 个运行时请求全部接入了空的 Portal 图；" +
                "当前 War3MapRuntime.CreateNavigation() 没有生成任何 portal node、edge " +
                "或 choke。因此只要直线被地形挡住，高层规划器就不可能给出绕坡路线。" );
        }
        if (deepCauses.TryGetValue("terrain_transition", out var terrainProbes))
        {
            output.AppendLine(
                $"- 分布式深探针中有 {terrainProbes} 个失败路径包含" +
                " `terrain_transition`：网格只验证节点中心可站立，却没有验证相邻节点" +
                "之间的扫掠圆可穿越，A* 会选中跨悬崖的伪边，最后被运行时路径合约拒绝。" );
        }
        var adjusted = samples.Count(value => value.Comparison ==
            War3NavigationComparison.RuntimeAdjustedUnreachableRequest);
        if (adjusted > 0)
        {
            output.AppendLine(
                $"- 有 {adjusted} 个原请求点本身不可站立，但运行时槽位分配器把农民" +
                "改派到附近可走点并成功；这类不是连通性成功，而是目标修正。" );
        }
        if (summary.Critical == 0)
            output.AppendLine("- 未发现底层/运行时不一致或路径合约错误。");
        output.AppendLine()
            .AppendLine("## 关键失败样本（最多 80 个）")
            .AppendLine()
            .AppendLine("| 点 | 底层 | 运行时 | 对照 | 说明 |")
            .AppendLine("|---|---|---|---|---|");
        foreach (var value in samples.Where(sample => sample.Critical).Take(80))
        {
            output.Append("| ").Append(Point(ToVector(value.Target)))
                .Append(" | ").Append(value.SimpleStatus)
                .Append(" | ").Append(value.RuntimeStatus)
                .Append(" | ").Append(value.Comparison)
                .Append(" | ").Append(MarkdownCell(
                    value.SimpleDetail + " " + value.RuntimeDetail))
                .AppendLine(" |");
        }
        output.AppendLine()
            .AppendLine("## 俯视图颜色")
            .AppendLine()
            .AppendLine("左半图为底层网格连通性：绿色可达，蓝色地形阻挡，棕色静态" +
                        "阻挡，橙色动态建筑，紫色断开的连通分区，洋红色为 provider " +
                        "返回了不安全路径。右半图为完整运行时：绿色获得路径，红色不可达，" +
                        "黄色超时，洋红色路径合约错误，蓝色命令被拒绝。白色十字为基地起点。")
            .AppendLine()
            .AppendLine("## 文件")
            .AppendLine()
            .AppendLine($"- JSON：`{jsonPath}`")
            .AppendLine($"- CSV：`{csvPath}`")
            .AppendLine($"- 俯视图：`{imagePath}`");
        File.WriteAllText(path, output.ToString());
    }

    private static void RenderComparison(
        string path,
        NavigationConnectivitySnapshot connectivity,
        List<War3NavigationAuditSample> samples,
        int startNode)
    {
        var panelWidth = connectivity.Columns * RenderScale;
        var height = connectivity.Rows * RenderScale;
        var image = Image.CreateEmpty(
            panelWidth * 2 + RenderGap, height, false, Image.Format.Rgba8);
        image.Fill(new Color("10151a"));
        foreach (var sample in samples)
        {
            var y = (connectivity.Rows - 1 - sample.Row) * RenderScale;
            FillBlock(
                image,
                sample.Column * RenderScale,
                y,
                RenderScale,
                SimpleColor(sample));
            FillBlock(
                image,
                panelWidth + RenderGap + sample.Column * RenderScale,
                y,
                RenderScale,
                RuntimeColor(sample));
        }
        for (var x = panelWidth; x < panelWidth + RenderGap; x++)
        for (var y = 0; y < height; y++)
            image.SetPixel(x, y, new Color("e4e8ec"));
        var startColumn = startNode % connectivity.Columns;
        var startRow = startNode / connectivity.Columns;
        DrawStartMarker(image, startColumn, startRow, connectivity, 0);
        DrawStartMarker(
            image, startColumn, startRow, connectivity,
            panelWidth + RenderGap);
        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            throw new IOException(
                $"Could not save navigation audit PNG: {error}.");
        }
    }

    private static Color SimpleColor(War3NavigationAuditSample sample)
    {
        if (sample.ProviderReturnedPath && !sample.ProviderPathValid)
            return new Color("ff35d3");
        return sample.SimpleStatus switch
        {
            War3SimpleNavigationStatus.Reachable => new Color("3bd16f"),
            War3SimpleNavigationStatus.WorldBoundaryBlocked => new Color("2a2f36"),
            War3SimpleNavigationStatus.TerrainBlocked => new Color("2467b2"),
            War3SimpleNavigationStatus.StaticObstacleBlocked => new Color("75462c"),
            War3SimpleNavigationStatus.DynamicObstacleBlocked => new Color("ed8b2f"),
            War3SimpleNavigationStatus.ClearanceBlocked => new Color("59646d"),
            War3SimpleNavigationStatus.DisconnectedComponent => new Color("9c55d8"),
            _ => new Color("ff344f")
        };
    }

    private static Color RuntimeColor(War3NavigationAuditSample sample) =>
        sample.RuntimeStatus switch
        {
            War3RuntimeNavigationStatus.PathReady => new Color("3bd16f"),
            War3RuntimeNavigationStatus.AlreadyReached => new Color("8bea9f"),
            War3RuntimeNavigationStatus.Unreachable => new Color("e53d50"),
            War3RuntimeNavigationStatus.InvalidPath => new Color("ff35d3"),
            War3RuntimeNavigationStatus.CommandRejected => new Color("367ed8"),
            _ => new Color("ffd34d")
        };

    private static void FillBlock(
        Image image,
        int left,
        int top,
        int size,
        Color color)
    {
        for (var y = top; y < top + size; y++)
        for (var x = left; x < left + size; x++)
            image.SetPixel(x, y, color);
    }

    private static void DrawStartMarker(
        Image image,
        int column,
        int row,
        NavigationConnectivitySnapshot connectivity,
        int offsetX)
    {
        var centerX = offsetX + column * RenderScale + RenderScale / 2;
        var centerY = (connectivity.Rows - 1 - row) * RenderScale +
                      RenderScale / 2;
        for (var delta = -6; delta <= 6; delta++)
        {
            SetPixelSafe(image, centerX + delta, centerY, Colors.White);
            SetPixelSafe(image, centerX, centerY + delta, Colors.White);
        }
    }

    private static void SetPixelSafe(Image image, int x, int y, Color color)
    {
        if ((uint)x < (uint)image.GetWidth() &&
            (uint)y < (uint)image.GetHeight())
            image.SetPixel(x, y, color);
    }

    private static Dictionary<string, int> CountBy<T>(
        IEnumerable<War3NavigationAuditSample> samples,
        Func<War3NavigationAuditSample, T> selector) where T : notnull =>
        samples.GroupBy(selector)
            .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key.ToString() ?? string.Empty,
                group => group.Count(),
                StringComparer.Ordinal);

    private static void AppendCountTable(
        StringBuilder output,
        string title,
        Dictionary<string, int> counts)
    {
        output.Append("### ").AppendLine(title).AppendLine()
            .AppendLine("| 状态 | 数量 |")
            .AppendLine("|---|---:|");
        foreach (var value in counts)
            output.Append("| ").Append(value.Key).Append(" | ")
                .Append(value.Value).AppendLine(" |");
        output.AppendLine();
    }

    private static string ResolveOutputDirectory(
        string[] arguments,
        string mapId)
    {
        var configured = arguments.FirstOrDefault(value => value.StartsWith(
            "--war3-navigation-audit-output=",
            StringComparison.OrdinalIgnoreCase));
        if (configured is not null)
        {
            var value = configured.Split('=', 2)[1];
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Navigation audit output is empty.");
            return value.StartsWith("user://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
                ? ProjectSettings.GlobalizePath(value)
                : Path.GetFullPath(value);
        }
        return ProjectSettings.GlobalizePath(
            $"user://war3_navigation_audit/{mapId}");
    }

    private static int ReadIntegerArgument(
        string[] arguments,
        string prefix,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var value = arguments.FirstOrDefault(argument =>
            argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null) return defaultValue;
        var text = value[prefix.Length..];
        if (!int.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum || parsed > maximum)
        {
            throw new ArgumentOutOfRangeException(
                prefix, $"Expected an integer in [{minimum}, {maximum}].");
        }
        return parsed;
    }

    private static float PathLength(ReadOnlySpan<NVector2> path)
    {
        var result = 0f;
        for (var index = 1; index < path.Length; index++)
            result += NVector2.Distance(path[index - 1], path[index]);
        return result;
    }

    private static void PrintProgress(
        string phase,
        int completed,
        int total,
        double elapsedSeconds)
    {
        var message =
            $"phase={phase} completed={completed}/" +
            $"{total} percent={completed * 100d / total:0.0} " +
            $"elapsed_s={elapsedSeconds:0.###}";
        GD.Print($"WAR3_NAV_AUDIT_PROGRESS {message}");
        AppendProgress(message);
    }

    private static void AppendProgress(string message)
    {
        if (_progressPath is null) return;
        File.AppendAllText(
            _progressPath,
            $"{DateTime.UtcNow:O} {message}{System.Environment.NewLine}");
    }

    private static NVector2 ToVector(AuditPoint value) => new(value.X, value.Y);
    private static string Point(NVector2 value) =>
        $"{value.X:0.##},{value.Y:0.##}";
    private static string Rect(SimRect value) =>
        $"[{Point(value.Min)}..{Point(value.Max)}]";
    private static string ExtractDeepCause(string detail)
    {
        const string marker = "cause=";
        var start = detail.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += marker.Length;
        var end = start;
        while (end < detail.Length &&
               detail[end] is not ' ' and not ';' and not '.' and not ')')
            end++;
        return detail[start..end];
    }
    private static string F(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";
    private static string MarkdownCell(string value) =>
        value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private readonly record struct PathValidation(bool Valid, string Detail);
    private readonly record struct AuditSummary(
        int Critical,
        int SimpleReachable,
        int RuntimePaths,
        int ProviderContractViolations,
        int RuntimeRegressions);
}
