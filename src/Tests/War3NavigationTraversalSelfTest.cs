using System.Numerics;
using RtsDemo.Simulation;
using War3Rts;
using War3Rts.Maps;

namespace RtsDemo.Tests;

public static class War3NavigationTraversalSelfTest
{
    private const float TickSeconds = 1f / 30f;
    private const int MaximumTicksPerLeg = 2_400;

    public static SelfTestResult Run()
    {
        try
        {
            var entry = War3MapCatalog.Enumerate().Single(value =>
                value.Manifest.Id == War3MapCodec.DefaultMapId);
            if (!War3OfflineMapCache.TryLoadMap(
                    entry, out var offlineCache, out var map,
                    out var cacheReason) ||
                offlineCache is null || map is null)
            {
                return new SelfTestResult(false,
                    $"offline map cache unavailable: {cacheReason}");
            }
            var navigation = map.CreateNavigation();
            if (!offlineCache.TryLoadClearance(
                    navigation, map.Terrain, out var bake,
                    out cacheReason) || bake is null)
            {
                return new SelfTestResult(false,
                    $"offline clearance cache unavailable: {cacheReason}");
            }
            var world = navigation.CreateWorld(map.Terrain);
            var topology = TerrainNavigationTopologyBuilder.Build(
                map.Terrain, bake);
            var simulation = new RtsSimulation(
                world,
                new GridPathProvider(
                    world,
                    War3OfflineMapCache.BattlefieldPathCellSize,
                    bake),
                War3HumanScenario.Capacity,
                topology.CreateRoutePlanner(),
                topology.CreateChokeController(),
                bake);
            var buildings = War3HumanContent.CreateBuildingCatalog();
            var production = War3HumanContent.CreateProductionCatalog();
            var technologies = War3HumanContent.CreateTechnologyCatalog();
            War3HumanRuntime runtime;
            string bootstrapMode;
            if (offlineCache.TryRestoreBootstrap(
                    simulation,
                    map,
                    navigation,
                    bake,
                    buildings,
                    production,
                    technologies,
                    out var restoredRuntime,
                    out cacheReason) && restoredRuntime is not null)
            {
                runtime = restoredRuntime;
                bootstrapMode = "hit";
            }
            else
            {
                world = navigation.CreateWorld(map.Terrain);
                simulation = new RtsSimulation(
                    world,
                    new GridPathProvider(
                        world,
                        War3OfflineMapCache.BattlefieldPathCellSize,
                        bake),
                    War3HumanScenario.Capacity,
                    topology.CreateRoutePlanner(),
                    topology.CreateChokeController(),
                    bake);
                runtime = War3HumanScenario.Prepare(
                    simulation, buildings, production, technologies, map);
                bootstrapMode = $"fallback:{cacheReason}";
            }
            simulation.WarmPathingCaches();

            var workers = runtime.PlayerWorkers;
            simulation.IssuePlayerStop(
                War3HumanScenario.PlayerId, runtime.PlayerWorkers);
            simulation.IssuePlayerStop(
                War3HumanScenario.EnemyId, runtime.EnemyWorkers);
            simulation.Tick(TickSeconds);
            var highGround = map.PlayerSpawn + new Vector2(105f, -45f);
            var farHighGround = map.EnemySpawn + new Vector2(-105f, -45f);
            var outbound = RunLeg(simulation, workers, farHighGround);
            var returnLeg = outbound.Arrived
                ? RunLeg(simulation, workers, highGround)
                : LegResult.NotRun("outbound failed");

            var combatResults = new List<string>();
            var combatPassed = true;
            var combatTypeIds = new[]
            {
                War3HumanContent.Footman,
                War3HumanContent.Knight,
                War3HumanContent.MortarTeam,
                War3HumanContent.SiegeEngine
            };
            for (var index = 0; index < combatTypeIds.Length; index++)
            {
                var combatWorld = navigation.CreateWorld(map.Terrain);
                var combatSimulation = new RtsSimulation(
                    combatWorld,
                    new GridPathProvider(
                        combatWorld,
                        War3OfflineMapCache.BattlefieldPathCellSize,
                        bake),
                    War3HumanScenario.Capacity,
                    topology.CreateRoutePlanner(),
                    topology.CreateChokeController(),
                    bake);
                var combatRuntime = War3HumanScenario.Prepare(
                    combatSimulation,
                    buildings,
                    production,
                    technologies,
                    map);
                combatSimulation.IssuePlayerStop(
                    War3HumanScenario.PlayerId,
                    combatRuntime.PlayerWorkers);
                combatSimulation.IssuePlayerStop(
                    War3HumanScenario.EnemyId,
                    combatRuntime.EnemyWorkers);
                combatSimulation.Tick(TickSeconds);
                combatSimulation.WarmPathingCaches();
                var type = production.UnitType(combatTypeIds[index]);
                var start = FindOpenHighGroundStart(
                    combatSimulation,
                    highGround,
                    type.Movement.NavigationRadius,
                    type.Movement.PhysicalRadius,
                    index);
                var unit = combatSimulation.AddUnit(
                    start, type, War3HumanScenario.PlayerId);
                var typeOutbound = RunLeg(
                    combatSimulation, [unit], farHighGround);
                var typeReturn = typeOutbound.Arrived
                    ? RunLeg(combatSimulation, [unit], start)
                    : LegResult.NotRun("outbound failed");
                combatPassed &= typeOutbound.Arrived && typeReturn.Arrived;
                combatResults.Add(
                    $"{type.Name}/{type.Movement.MovementClass}/" +
                    $"r{type.Movement.NavigationRadius:0.#}/" +
                    $"start={Point(start)}:" +
                    $"{typeOutbound}>{typeReturn}");
            }

            var passed = outbound.Arrived && returnLeg.Arrived && combatPassed;
            return new SelfTestResult(
                passed,
                $"bootstrap={bootstrapMode}, high={Point(highGround)}, " +
                $"farHigh={Point(farHighGround)}, " +
                $"workers={outbound}>{returnLeg}, " +
                $"combat=[{string.Join(";", combatResults)}]");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }

    private static LegResult RunLeg(
        RtsSimulation simulation,
        int[] units,
        Vector2 target)
    {
        var first = units[0];
        var startFree = simulation.World.IsDiscFree(
            simulation.Units.Positions[first],
            simulation.Units.NavigationRadii[first]);
        var startStatus = $"startFree={startFree}";
        var command = simulation.IssuePlayerMove(
            War3HumanScenario.PlayerId, units, target);
        if (!command.Succeeded)
            return new LegResult(false, 0, 0,
                $"{startStatus},{command.Code}",
                simulation.Units.Positions[units[0]]);

        var routeWaypoints = simulation.LastIssuedGroupRoute.Waypoints.Length;
        for (var tick = 1; tick <= MaximumTicksPerLeg; tick++)
        {
            simulation.Tick(TickSeconds);
            if (units.All(unit =>
                    simulation.Units.MovementLegResults[unit] ==
                        UnitMovementLegResult.Reached ||
                    simulation.Units.Modes[unit] == UnitMoveMode.Arrived))
            {
                return new LegResult(
                    true, tick, routeWaypoints, $"{startStatus},Reached",
                    simulation.Units.Positions[units[0]]);
            }
            var failed = units.FirstOrDefault(unit =>
                simulation.Units.MovementLegResults[unit] ==
                    UnitMovementLegResult.Unreachable ||
                simulation.Units.RecoveryStages[unit] ==
                    RecoveryStage.Unreachable, -1);
            if (failed >= 0)
            {
                var position = simulation.Units.Positions[failed];
                var slotTarget = simulation.Units.SlotTargets[failed];
                var directPath = simulation.FindNavigationPathForDiagnostics(
                    position,
                    slotTarget,
                    simulation.Units.NavigationRadii[failed]);
                var route = simulation.Units.RouteWaypoints[failed];
                return new LegResult(
                    false, tick, routeWaypoints,
                    $"{startStatus},u{failed}:" +
                    $"{simulation.Units.MovementLegResults[failed]}/" +
                    $"{simulation.Units.RecoveryStages[failed]}," +
                    $"requested={Point(target)},slot={Point(slotTarget)}," +
                    $"direct={DescribePath(simulation, failed, directPath)}," +
                    $"route=[{string.Join('|', route.Select(Point))}]",
                    position);
            }
        }
        return new LegResult(
            false,
            MaximumTicksPerLeg,
            routeWaypoints,
            $"{startStatus},timeout/arrived=" +
            $"{units.Count(unit => simulation.Units.Modes[unit] == UnitMoveMode.Arrived)}" +
            $"/{units.Length},mode={simulation.Units.Modes[first]}," +
            $"leg={simulation.Units.MovementLegResults[first]}," +
            $"recovery={simulation.Units.RecoveryStages[first]}," +
            $"speed={simulation.Units.Velocities[first].Length():0.#}," +
            $"path={simulation.Units.Paths[first]?.Cursor ?? -1}/" +
            $"{simulation.Units.Paths[first]?.Points.Length ?? 0}",
            simulation.Units.Positions[units[0]]);
    }

    private static string Point(Vector2 value) =>
        $"{value.X:0.#},{value.Y:0.#}";

    private static string DescribePath(
        RtsSimulation simulation,
        int unit,
        Vector2[] path)
    {
        var radius = simulation.Units.NavigationRadii[unit];
        var points = path.Select((point, index) =>
        {
            var free = simulation.World.IsDiscFree(point, radius);
            var edgeFree = index == 0 || simulation.World.IsSegmentFree(
                path[index - 1], point, radius);
            return $"{Point(point)}/p{free}/e{edgeFree}";
        });
        return $"{path.Length}[{string.Join('|', points)}]";
    }

    private static Vector2 FindOpenHighGroundStart(
        RtsSimulation simulation,
        Vector2 center,
        float navigationRadius,
        float physicalRadius,
        int ordinal)
    {
        for (var ring = 1; ring <= 8; ring++)
        {
            for (var row = -ring; row <= ring; row++)
            {
                for (var column = -ring; column <= ring; column++)
                {
                    if (Math.Max(Math.Abs(column), Math.Abs(row)) != ring)
                        continue;
                    var candidate = center + new Vector2(
                        column * 24f,
                        row * 24f + ordinal * 3f);
                    if (simulation.World.IsDiscFree(
                            candidate, navigationRadius) &&
                        IsSeparatedFromUnits(
                            simulation, candidate, physicalRadius))
                        return candidate;
                }
            }
        }
        throw new InvalidOperationException(
            $"No open high-ground start near {Point(center)} for radius " +
            $"{navigationRadius:0.#}.");
    }

    private static bool IsSeparatedFromUnits(
        RtsSimulation simulation,
        Vector2 candidate,
        float radius)
    {
        for (var unit = 0; unit < simulation.Units.Count; unit++)
        {
            if (!simulation.Units.Alive[unit])
                continue;
            var minimum = radius + simulation.Units.Radii[unit] + 8f;
            if (Vector2.DistanceSquared(
                    candidate,
                    simulation.Units.Positions[unit]) < minimum * minimum)
            {
                return false;
            }
        }
        return true;
    }

    private readonly record struct LegResult(
        bool Arrived,
        int Ticks,
        int RouteWaypoints,
        string Status,
        Vector2 Position)
    {
        public static LegResult NotRun(string status) =>
            new(false, 0, 0, status, default);

        public override string ToString() =>
            $"{Status}@{Point(Position)}/t{Ticks}/w{RouteWaypoints}";
    }
}
