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
            var returningWorkers = workers
                .Where(unit => simulation.Units.Alive[unit])
                .ToArray();
            var returnLeg = outbound.Arrived
                ? returningWorkers.Length > 0
                    ? RunLeg(simulation, returningWorkers, highGround)
                    : LegResult.NotRun("no surviving workers")
                : LegResult.NotRun("outbound failed");

            var playerTownHall = simulation.CreateGameplayBuildingOverview()
                .First(value => value.PlayerId == War3HumanScenario.PlayerId &&
                                value.Type.Id == War3HumanContent.TownHall);
            var workerPhysicalRadius = simulation.Units.Radii[
                runtime.PlayerWorkers[0]];
            var workerNavigationRadius = simulation.Units.NavigationRadii[
                runtime.PlayerWorkers[0]];

            // A peasant can finish a town-hall surface interaction in a point
            // that is valid for its physical body but lies inside navigation
            // clearance. It must be able to leave that surface on the next
            // ordinary/resource order.
            var blockedStartEscape = RunBlockedStartEscapeRegression(
                simulation,
                runtime.PlayerWorkers[0],
                new EconomyResourceNodeId(5),
                SurfaceClearanceStart(
                    playerTownHall.Bounds,
                    workerPhysicalRadius,
                    workerNavigationRadius,
                    top: false,
                    axialFactor: -0.25f));
            // Same building, top edge: the body is fully valid here, but the
            // navigation-clearance disc overlaps the town hall by 0.5 pixels.
            // The nearest grid anchor must be reachable from the escape point,
            // not merely be an empty cell on the other side of the footprint.
            var surfaceStartEscape = RunBlockedStartEscapeRegression(
                simulation,
                runtime.PlayerWorkers.First(unit =>
                    unit != runtime.PlayerWorkers[0] &&
                    simulation.Units.Alive[unit]),
                new EconomyResourceNodeId(1),
                SurfaceClearanceStart(
                    playerTownHall.Bounds,
                    workerPhysicalRadius,
                    workerNavigationRadius,
                    top: true,
                    axialFactor: 0.25f));

            // Reproduce the player report against the authored tree at
            // 1329,1583: its physical interaction point used to be accepted
            // even though the peasant's navigation clearance rejected it.
            var resourceApproach = RunResourceApproachRegression(
                simulation,
                runtime.PlayerWorkers[5],
                new EconomyResourceNodeId(5),
                new Vector2(1308f, 1657.1f));
            // Reproduce the intermittent right-click case: a peasant already
            // carrying lumber must first return it, so the apparent tree order
            // actually targets the town-hall boundary.
            var dropOffApproach = RunCarriedResourceDropOffRegression(
                simulation,
                runtime.PlayerWorkers[5],
                new EconomyResourceNodeId(14),
                new Vector2(1360.9f, 1999f));

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

            var passed = outbound.Arrived && returnLeg.Arrived &&
                         blockedStartEscape.Arrived &&
                         surfaceStartEscape.Arrived &&
                         resourceApproach.Arrived &&
                         dropOffApproach.Arrived && combatPassed;
            return new SelfTestResult(
                passed,
                $"bootstrap={bootstrapMode}, high={Point(highGround)}, " +
                $"farHigh={Point(farHighGround)}, " +
                $"workers={returningWorkers.Length}/{workers.Length}:" +
                $"{outbound}>{returnLeg}, escape={blockedStartEscape}, " +
                $"surfaceEscape={surfaceStartEscape}, " +
                $"resource={resourceApproach}, " +
                $"dropoff={dropOffApproach}, " +
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

    private static LegResult RunResourceApproachRegression(
        RtsSimulation simulation,
        int worker,
        EconomyResourceNodeId resource,
        Vector2 start)
    {
        simulation.IssuePlayerStop(War3HumanScenario.PlayerId, [worker]);
        simulation.Tick(TickSeconds);
        simulation.Units.Positions[worker] = start;
        simulation.Units.SlotTargets[worker] = start;
        simulation.Units.MoveGoals[worker] = start;

        var command = simulation.IssueGather(
            War3HumanScenario.PlayerId, worker, resource);
        var slot = simulation.Units.SlotTargets[worker];
        var slotFree = simulation.World.IsDiscFree(
            slot, simulation.Units.NavigationRadii[worker]);
        if (!command.Succeeded)
        {
            return new LegResult(
                false, 0, 0, $"command={command.Code}", start);
        }

        for (var tick = 1; tick <= 600; tick++)
        {
            simulation.Tick(TickSeconds);
            var state = simulation.Economy.Worker(worker).State;
            if (state is WorkerEconomyState.Gathering or
                WorkerEconomyState.WaitingForResource)
            {
                return new LegResult(
                    slotFree, tick, 0,
                    $"slot={Point(slot)},slotFree={slotFree},state={state}",
                    simulation.Units.Positions[worker]);
            }
            if (simulation.Units.MovementLegResults[worker] ==
                    UnitMovementLegResult.Unreachable ||
                simulation.Units.RecoveryStages[worker] ==
                    RecoveryStage.Unreachable)
            {
                return new LegResult(
                    false, tick, 0,
                    $"slot={Point(slot)},slotFree={slotFree}," +
                    $"state={state},leg=" +
                    $"{simulation.Units.MovementLegResults[worker]}",
                    simulation.Units.Positions[worker]);
            }
        }
        return new LegResult(
            false, 600, 0,
            $"slot={Point(slot)},slotFree={slotFree},timeout," +
            $"state={simulation.Economy.Worker(worker).State}",
            simulation.Units.Positions[worker]);
    }

    private static LegResult RunBlockedStartEscapeRegression(
        RtsSimulation simulation,
        int worker,
        EconomyResourceNodeId resource,
        Vector2 start)
    {
        simulation.IssuePlayerStop(War3HumanScenario.PlayerId, [worker]);
        simulation.Tick(TickSeconds);
        simulation.Units.Positions[worker] = start;
        simulation.Units.SlotTargets[worker] = start;
        simulation.Units.MoveGoals[worker] = start;
        var physicalFree = simulation.World.IsDiscFree(
            start, simulation.Units.Radii[worker]);
        var navigationFree = simulation.World.IsDiscFree(
            start, simulation.Units.NavigationRadii[worker]);
        var physicalRepair = simulation.World.ConstrainDisc(
            start, start, simulation.Units.Radii[worker] + 0.05f);
        var navigationRepair = simulation.World.ConstrainDisc(
            physicalRepair, physicalRepair,
            simulation.Units.NavigationRadii[worker] + 0.05f);
        var physicalRepairFree = simulation.World.IsDiscFree(
            physicalRepair, simulation.Units.Radii[worker]);
        var navigationRepairFree = simulation.World.IsDiscFree(
            navigationRepair, simulation.Units.NavigationRadii[worker]);
        var repairSegmentFree = simulation.World.IsSegmentFree(
            physicalRepair, navigationRepair,
            simulation.Units.Radii[worker]);
        var command = simulation.IssueGather(
            War3HumanScenario.PlayerId, worker, resource);
        var slot = simulation.Units.SlotTargets[worker];
        var slotFree = simulation.World.IsDiscFree(
            slot, simulation.Units.NavigationRadii[worker]);
        if (!command.Succeeded)
        {
            return new LegResult(
                false, 0, 0, $"command={command.Code}", start);
        }

        for (var tick = 1; tick <= 600; tick++)
        {
            simulation.Tick(TickSeconds);
            var state = simulation.Economy.Worker(worker).State;
            if (state is WorkerEconomyState.Gathering or
                WorkerEconomyState.WaitingForResource)
            {
                var end = simulation.Units.Positions[worker];
                simulation.IssuePlayerStop(
                    War3HumanScenario.PlayerId, [worker]);
                simulation.Tick(TickSeconds);
                return new LegResult(
                    !navigationFree && slotFree,
                    tick, 0,
                    $"start={Point(start)}/p{physicalFree}/n{navigationFree}," +
                    $"slot={Point(slot)}/free{slotFree},state={state}",
                    end);
            }
            if (simulation.Units.MovementLegResults[worker] ==
                    UnitMovementLegResult.Unreachable ||
                simulation.Units.RecoveryStages[worker] ==
                    RecoveryStage.Unreachable)
            {
                var blockers = DescribeBlockingGeometry(
                    simulation, start, simulation.Units.Radii[worker]);
                return new LegResult(
                    false, tick, 0,
                    $"start={Point(start)}/p{physicalFree}/n{navigationFree}," +
                    $"slot={Point(slot)}/free{slotFree},state={state}," +
                    $"leg={simulation.Units.MovementLegResults[worker]}," +
                    $"repair={Point(physicalRepair)}/p{physicalRepairFree}>" +
                    $"{Point(navigationRepair)}/n{navigationRepairFree}/" +
                    $"edge{repairSegmentFree}," +
                    $"blockers={blockers}",
                    simulation.Units.Positions[worker]);
            }
        }
        return new LegResult(
            false, 600, 0,
            $"start={Point(start)}/p{physicalFree}/n{navigationFree},timeout," +
            $"state={simulation.Economy.Worker(worker).State}",
            simulation.Units.Positions[worker]);
    }

    private static Vector2 SurfaceClearanceStart(
        SimRect bounds,
        float physicalRadius,
        float navigationRadius,
        bool top,
        float axialFactor)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var edgeOffset = (physicalRadius + navigationRadius) * 0.5f;
        return new Vector2(
            center.X + bounds.Width * axialFactor,
            top ? bounds.Min.Y - edgeOffset : bounds.Max.Y + edgeOffset);
    }

    private static string DescribeBlockingGeometry(
        RtsSimulation simulation,
        Vector2 position,
        float radius)
    {
        var staticBlockers = simulation.World.Obstacles
            .ToArray()
            .Select((bounds, index) => (bounds, index))
            .Where(value => value.bounds.OverlapsDisc(position, radius))
            .Select(value => $"s{value.index}:{Rect(value.bounds)}");
        var dynamicBlockers = simulation.World.DynamicOccupancy.Snapshot()
            .Where(value => value.Bounds.OverlapsDisc(position, radius))
            .Select(value => $"d{value.Id.Value}:{Rect(value.Bounds)}");
        var terrainBlocked = simulation.World.Terrain is not null &&
                             !simulation.World.Terrain.IsDiscTraversable(
                                 position, radius);
        return $"[{string.Join('|', staticBlockers.Concat(dynamicBlockers))}]" +
               $"/terrain={terrainBlocked}";
    }

    private static string Rect(SimRect value) =>
        $"{Point(value.Min)}-{Point(value.Max)}";

    private static LegResult RunCarriedResourceDropOffRegression(
        RtsSimulation simulation,
        int worker,
        EconomyResourceNodeId nextResource,
        Vector2 start)
    {
        const int maximumTicks = 600;
        var cargoPreparationTicks = 0;
        while (simulation.Economy.Worker(worker).CargoAmount <= 0 &&
               cargoPreparationTicks < 120)
        {
            simulation.Tick(TickSeconds);
            cargoPreparationTicks++;
        }
        var cargoBefore = simulation.Economy.Worker(worker).CargoAmount;
        if (cargoBefore <= 0)
        {
            return new LegResult(
                false, cargoPreparationTicks, 0,
                "could not prepare progressive lumber cargo",
                simulation.Units.Positions[worker]);
        }

        simulation.Units.Positions[worker] = start;
        simulation.Units.SlotTargets[worker] = start;
        simulation.Units.MoveGoals[worker] = start;
        var command = simulation.IssueGather(
            War3HumanScenario.PlayerId, worker, nextResource);
        var slot = simulation.Units.SlotTargets[worker];
        var slotFree = simulation.World.IsDiscFree(
            slot, simulation.Units.NavigationRadii[worker]);
        var initialVersion = simulation.Units.CommandVersions[worker];
        var initialState = simulation.Economy.Worker(worker).State;
        var initialGoal = simulation.Units.MovementGoalKinds[worker];
        if (!command.Succeeded ||
            initialState != WorkerEconomyState.ReturningCargo ||
            initialGoal != UnitMovementGoalKind.DropOffBoundary)
        {
            return new LegResult(
                false, 0, 0,
                $"command={command.Code},cargo={cargoBefore}," +
                $"state={initialState},goal={initialGoal}", start);
        }

        for (var tick = 1; tick <= maximumTicks; tick++)
        {
            simulation.Tick(TickSeconds);
            var state = simulation.Economy.Worker(worker);
            var commandDelta =
                simulation.Units.CommandVersions[worker] - initialVersion;
            if (commandDelta > 8)
            {
                return new LegResult(
                    false, tick, 0,
                    $"slot={Point(slot)},slotFree={slotFree}," +
                    $"command-reissue-loop={commandDelta},state={state.State}," +
                    $"leg={simulation.Units.MovementLegResults[worker]}",
                    simulation.Units.Positions[worker]);
            }
            if (state.CargoAmount == 0 &&
                state.State != WorkerEconomyState.ReturningCargo)
            {
                return new LegResult(
                    slotFree, tick, 0,
                    $"slot={Point(slot)},slotFree={slotFree}," +
                    $"cargo={cargoBefore}->0,commands={commandDelta}," +
                    $"state={state.State}",
                    simulation.Units.Positions[worker]);
            }
        }
        var finalState = simulation.Economy.Worker(worker);
        return new LegResult(
            false, maximumTicks, 0,
            $"slot={Point(slot)},slotFree={slotFree},timeout," +
            $"cargo={finalState.CargoAmount},state={finalState.State}," +
            $"commands=" +
            $"{simulation.Units.CommandVersions[worker] - initialVersion}",
            simulation.Units.Positions[worker]);
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
