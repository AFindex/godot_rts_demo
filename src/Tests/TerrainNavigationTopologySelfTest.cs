using System.Numerics;
using RtsDemo.Scenarios;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public readonly record struct TerrainNavigationTopologyTestResult(
    bool Passed,
    string Summary);

public static class TerrainNavigationTopologySelfTest
{
    public static TerrainNavigationTopologyTestResult Run()
    {
        try
        {
            var traversal = TerrainTraversalScenario.Prepare();
            var ramp = traversal.Topology.Ramps[0];
            var automatic = traversal.Topology.Ramps.Length == 1 &&
                            ramp.Width == 160f &&
                            ramp.MovementMask == TerrainTopologyMovementMask.All &&
                            traversal.Topology.Layers.ToArray().All(
                                layer => layer.RegionCount == 2 &&
                                         layer.Connection(0).IsTraversable) &&
                            traversal.Simulation.LastIssuedGroupRoute.Waypoints.Length == 2 &&
                            traversal.Simulation.LastIssuedGroupRoute.ChokeIds.SequenceEqual([0]) &&
                            traversal.Topology.CreateChokeController()
                                .Definitions[0].Width == 160f;

            var multiTerrain = CreateMultiLevelTerrain();
            var multiNavigation = EmptyNavigation(multiTerrain.Bounds);
            var multiBake = ClearanceBakeSnapshot.Build(
                multiNavigation, multiTerrain, cellSize: 8f);
            var multi = TerrainNavigationTopologyBuilder.Build(
                multiTerrain, multiBake);
            var multiPlanner = multi.CreateRoutePlanner();
            var forward = multiPlanner.Plan(
                Center(2, 3, 32f), Center(15, 3, 32f), 8f);
            var reverse = multiPlanner.Plan(
                Center(15, 3, 32f), Center(2, 3, 32f), 8f);
            var sameRegion = multiPlanner.Plan(
                Center(1, 1, 32f), Center(3, 6, 32f), 8f);
            var multiLevel = multi.Ramps.Length == 2 &&
                             multi.Layer(MovementClass.Medium).RegionCount == 3 &&
                             multi.Ramps.ToArray().All(value =>
                                 value.Width == 96f &&
                                 value.MovementMask == TerrainTopologyMovementMask.All) &&
                             forward.Waypoints.Length == 4 &&
                             forward.ChokeIds.SequenceEqual([0, 1]) &&
                             reverse.Waypoints.Length == 4 &&
                             reverse.ChokeIds.SequenceEqual([1, 0]) &&
                             sameRegion.Waypoints.Length == 0 &&
                             sameRegion.ChokeIds.Length == 0;

            var repeated = TerrainNavigationTopologyBuilder.Build(
                multiTerrain, multiBake);
            var stable = repeated.StableHash == multi.StableHash &&
                         repeated.StableHashText == multi.StableHashText;

            var blockedNavigation = EmptyNavigation(
                multiTerrain.Bounds,
                new SimRect(
                    new Vector2(5f * 32f, 2f * 32f),
                    new Vector2(6f * 32f, 5f * 32f)));
            var blockedBake = ClearanceBakeSnapshot.Build(
                blockedNavigation, multiTerrain, cellSize: 8f);
            var blocked = TerrainNavigationTopologyBuilder.Build(
                multiTerrain, blockedBake);
            var blockedRoute = blocked.CreateRoutePlanner().Plan(
                Center(2, 3, 32f), Center(15, 3, 32f), 8f);
            var staticBlocker = blocked.Ramps[0].MovementMask ==
                                    TerrainTopologyMovementMask.None &&
                                blockedRoute.Waypoints.Length == 0 &&
                                blockedRoute.ChokeIds.Length == 0;

            var multiRuntime = RunMultiRampSimulation(
                multiNavigation, multiTerrain, multiBake, multi);

            var narrowTerrain = CreateNarrowRampTerrain();
            var narrowNavigation = EmptyNavigation(narrowTerrain.Bounds);
            var narrowBake = ClearanceBakeSnapshot.Build(
                narrowNavigation, narrowTerrain, cellSize: 4f);
            var narrow = TerrainNavigationTopologyBuilder.Build(
                narrowTerrain, narrowBake);
            var narrowRamp = narrow.Ramps[0];
            var narrowPlanner = narrow.CreateRoutePlanner();
            var narrowStart = Center(2, 3, 20f);
            var narrowGoal = Center(6, 3, 20f);
            var mediumRoute = narrowPlanner.Plan(
                narrowStart, narrowGoal, MovementClearance.MediumNavigationRadius);
            var largeRoute = narrowPlanner.Plan(
                narrowStart, narrowGoal, MovementClearance.LargeNavigationRadius);
            var classWidth = narrowRamp.Width == 20f &&
                             narrowRamp.Supports(MovementClass.Small) &&
                             narrowRamp.Supports(MovementClass.Medium) &&
                             !narrowRamp.Supports(MovementClass.Large) &&
                             mediumRoute.Waypoints.Length == 2 &&
                             mediumRoute.ChokeIds.SequenceEqual([0]) &&
                             largeRoute.Waypoints.Length == 0 &&
                             largeRoute.ChokeIds.Length == 0;

            var mismatchRejected = false;
            try
            {
                TerrainNavigationTopologyBuilder.Build(
                    multiTerrain, narrowBake);
            }
            catch (ArgumentException)
            {
                mismatchRejected = true;
            }

            var passed = automatic && multiLevel && multiRuntime.Passed &&
                         stable && staticBlocker && classWidth &&
                         mismatchRejected;
            var summary =
                $"automatic={automatic}[regions=" +
                $"{string.Join('/', traversal.Topology.Layers.ToArray().Select(value => value.RegionCount))}," +
                $"width={ramp.Width:F0},route=" +
                $"{traversal.Simulation.LastIssuedGroupRoute.Waypoints.Length}], " +
                $"multi={multiLevel}[regions=" +
                $"{multi.Layer(MovementClass.Medium).RegionCount}," +
                $"route={forward.Waypoints.Length},chokes=" +
                $"{string.Join(',', forward.ChokeIds)}], " +
                $"runtime={multiRuntime.Passed}[seen=" +
                $"{multiRuntime.SeenFirst}/{multiRuntime.SeenSecond}," +
                $"arrived={multiRuntime.Arrived},state={multiRuntime.Detail}], " +
                $"blocked={staticBlocker}[mask={blocked.Ramps[0].MovementMask}], " +
                $"width={classWidth}[mask={narrowRamp.MovementMask}], " +
                $"stable={stable}/{multi.StableHashText}, mismatch={mismatchRejected}";
            return new TerrainNavigationTopologyTestResult(passed, summary);
        }
        catch (Exception exception)
        {
            return new TerrainNavigationTopologyTestResult(
                false,
                $"exception={exception.GetType().Name}: {exception.Message}");
        }
    }

    private static TerrainMapSnapshot CreateMultiLevelTerrain()
    {
        const int columns = 18;
        const int rows = 8;
        const float cellSize = 32f;
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var cells = Enumerable.Repeat(low, columns * rows).ToArray();
        for (var row = 0; row < rows; row++)
        {
            for (var column = 6; column < columns; column++)
                cells[row * columns + column] = low with { CliffLevel = 1 };
            for (var column = 12; column < columns; column++)
                cells[row * columns + column] = low with { CliffLevel = 2 };
        }
        for (var row = 2; row <= 4; row++)
        {
            cells[row * columns + 5] = low with
            {
                Flags = TerrainCellFlags.Ramp,
                RampDirection = TerrainRampDirection.PositiveX
            };
            cells[row * columns + 11] = low with
            {
                CliffLevel = 1,
                Flags = TerrainCellFlags.Ramp,
                RampDirection = TerrainRampDirection.PositiveX
            };
        }
        return CreateTerrain(columns, rows, cellSize, cells);
    }

    private static MultiRampRuntimeResult RunMultiRampSimulation(
        NavigationMapSnapshot navigation,
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot bake,
        TerrainNavigationTopologySnapshot topology)
    {
        var world = navigation.CreateWorld(terrain);
        var pathProvider = new GridPathProvider(world, 8f, bake);
        var simulation = new RtsSimulation(
            world,
            pathProvider,
            capacity: 16,
            groupRoutePlanner: topology.CreateRoutePlanner(),
            chokeController: topology.CreateChokeController(),
            clearanceBake: bake);
        int[] units =
        [
            simulation.AddUnit(Center(1, 2, 32f), radius: 8f),
            simulation.AddUnit(Center(1, 3, 32f), radius: 8f),
            simulation.AddUnit(Center(1, 4, 32f), radius: 8f)
        ];
        simulation.IssueMove(units, Center(16, 3, 32f));
        var seenFirst = units.Any(unit =>
            simulation.Units.ActiveChokeIds[unit] == 0);
        var seenSecond = false;
        for (var tick = 0; tick < 1_400; tick++)
        {
            simulation.Tick(1f / 60f);
            seenFirst |= units.Any(unit =>
                simulation.Units.ActiveChokeIds[unit] == 0);
            seenSecond |= units.Any(unit =>
                simulation.Units.ActiveChokeIds[unit] == 1);
        }
        var arrived = units.Count(unit =>
            simulation.Units.Positions[unit].X >= 470f &&
            simulation.Units.Modes[unit] != UnitMoveMode.Moving &&
            simulation.Units.Modes[unit] != UnitMoveMode.WaitingForPath);
        var detail = string.Join(';', units.Select(unit =>
            $"u{unit}@{simulation.Units.Positions[unit].X:F0}," +
            $"{simulation.Units.Positions[unit].Y:F0}/" +
            $"{simulation.Units.Modes[unit]}/" +
            $"c{simulation.Units.ActiveChokeIds[unit]}/" +
            $"{simulation.Units.ChokePhases[unit]}/" +
            $"a{simulation.Units.ChokeAdmitted[unit]}/" +
            $"w{simulation.Units.ChokeWaitTicks[unit]}"));
        return new MultiRampRuntimeResult(
            seenFirst && seenSecond && arrived == units.Length,
            seenFirst,
            seenSecond,
            arrived,
            detail);
    }

    private static TerrainMapSnapshot CreateNarrowRampTerrain()
    {
        const int columns = 9;
        const int rows = 7;
        const float cellSize = 20f;
        var blocked = new TerrainCell(
            0, 0, TerrainPathing.None, TerrainCellFlags.None);
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var cells = Enumerable.Repeat(blocked, columns * rows).ToArray();
        for (var column = 0; column < columns; column++)
        {
            cells[3 * columns + column] = low with
            {
                CliffLevel = (byte)(column >= 5 ? 1 : 0)
            };
        }
        cells[3 * columns + 4] = low with
        {
            Flags = TerrainCellFlags.Ramp,
            RampDirection = TerrainRampDirection.PositiveX
        };
        return CreateTerrain(columns, rows, cellSize, cells);
    }

    private static TerrainMapSnapshot CreateTerrain(
        int columns,
        int rows,
        float cellSize,
        TerrainCell[] cells)
    {
        var created = TerrainMapSnapshot.TryCreate(
            new SimRect(Vector2.Zero,
                new Vector2(columns * cellSize, rows * cellSize)),
            cellSize,
            48f,
            [new TerrainSurfaceDefinition(0, "test", "Test")],
            cells,
            out var terrain,
            out var validation);
        if (!created || terrain is null)
            throw new InvalidOperationException(
                $"Test terrain failed: {validation.FirstError}.");
        return terrain;
    }

    private static NavigationMapSnapshot EmptyNavigation(
        SimRect bounds,
        params SimRect[] obstacles)
    {
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            bounds,
            obstacles, [], [], [],
            out var navigation,
            out var validation);
        if (!created || navigation is null)
            throw new InvalidOperationException(
                $"Test navigation failed: {validation.FirstError}.");
        return navigation;
    }

    private static Vector2 Center(
        int column,
        int row,
        float cellSize) =>
        new((column + 0.5f) * cellSize, (row + 0.5f) * cellSize);

    private readonly record struct MultiRampRuntimeResult(
        bool Passed,
        bool SeenFirst,
        bool SeenSecond,
        int Arrived,
        string Detail);
}
