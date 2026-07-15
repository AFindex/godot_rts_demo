using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Scenarios;

/// <summary>
/// Production-stack fixture for temporary blockers on parallel terrain routes.
/// The fixture uses only public simulation commands for footprint changes and
/// unit movement; presentation and verification do not rebuild navigation.
/// </summary>
public static class TerrainDynamicTopologyScenario
{
    public const float CellSize = 32f;
    public const int TransitionColumn = 7;
    public const int NearRampFirstRow = 2;
    public const int NearRampLastRow = 4;
    public const int FarRampFirstRow = 11;
    public const int FarRampLastRow = 13;
    public static readonly Vector2 Start = Center(1, 3);
    public static readonly Vector2 Goal = Center(14, 6);
    public static readonly Vector2 BlockerCenter = new(168f, 104f);

    public static TerrainDynamicTopologyRuntime Prepare(int capacity = 64)
    {
        var terrain = CreateTerrain();
        var navigation = CreateNavigation(terrain.Bounds);
        var clearance = ClearanceBakeSnapshot.Build(
            navigation, terrain, cellSize: 8f);
        var authoredTopology = TerrainNavigationTopologyBuilder.Build(
            terrain, clearance);
        var world = navigation.CreateWorld(terrain);
        var routePlanner = authoredTopology.CreateDynamicRoutePlanner(
            world, terrain, clearance);
        var simulation = new RtsSimulation(
            world,
            new GridPathProvider(world, 8f, clearance),
            capacity,
            groupRoutePlanner: routePlanner,
            chokeController: authoredTopology.CreateChokeController(),
            clearanceBake: clearance);
        return new TerrainDynamicTopologyRuntime(
            navigation,
            terrain,
            clearance,
            authoredTopology,
            routePlanner,
            simulation);
    }

    public static SimRect Footprint(
        BuildingFootprintClass footprintClass,
        Vector2? center = null)
    {
        var size = BuildingFootprintProfile.For(footprintClass).Size;
        var target = center ?? BlockerCenter;
        return new SimRect(target - size * 0.5f, target + size * 0.5f);
    }

    public static int[] SpawnWave(
        RtsSimulation simulation,
        int count,
        float verticalOffset = 0f)
    {
        var units = new int[count];
        for (var index = 0; index < count; index++)
        {
            var column = index % 4;
            var row = index / 4;
            units[index] = simulation.AddUnit(
                Start + new Vector2(
                    -30f + column * 20f,
                    verticalOffset - 22f + row * 20f),
                team: 1,
                combatProfile: CombatProfileSnapshot.Standard,
                radius: 8f,
                maxSpeed: 142f,
                acceleration: 840f);
        }
        simulation.IssueMove(units, Goal + new Vector2(0f, verticalOffset));
        return units;
    }

    private static TerrainMapSnapshot CreateTerrain()
    {
        const int columns = 18;
        const int rows = 16;
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var cells = Enumerable.Repeat(low, columns * rows).ToArray();
        for (var row = 0; row < rows; row++)
        {
            for (var column = TransitionColumn + 1;
                 column < columns;
                 column++)
            {
                cells[row * columns + column] = low with { CliffLevel = 1 };
            }
        }
        foreach (var row in Enumerable.Range(
                     NearRampFirstRow,
                     NearRampLastRow - NearRampFirstRow + 1).Concat(
                     Enumerable.Range(
                         FarRampFirstRow,
                         FarRampLastRow - FarRampFirstRow + 1)))
        {
            cells[row * columns + TransitionColumn] = low with
            {
                Flags = TerrainCellFlags.Ramp,
                RampDirection = TerrainRampDirection.PositiveX
            };
        }

        if (!TerrainMapSnapshot.TryCreate(
                new SimRect(
                    Vector2.Zero,
                    new Vector2(columns * CellSize, rows * CellSize)),
                CellSize,
                48f,
                [new TerrainSurfaceDefinition(0, "dynamic", "Dynamic Route")],
                cells,
                out var terrain,
                out var validation) || terrain is null)
        {
            throw new InvalidOperationException(
                $"Dynamic terrain fixture failed: {validation.FirstError}.");
        }
        return terrain;
    }

    private static NavigationMapSnapshot CreateNavigation(SimRect bounds)
    {
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                bounds,
                [], [], [], [],
                out var navigation,
                out var validation) || navigation is null)
        {
            throw new InvalidOperationException(
                $"Dynamic navigation fixture failed: {validation.FirstError}.");
        }
        return navigation;
    }

    private static Vector2 Center(int column, int row) =>
        new((column + 0.5f) * CellSize, (row + 0.5f) * CellSize);
}

public sealed record TerrainDynamicTopologyRuntime(
    NavigationMapSnapshot Navigation,
    TerrainMapSnapshot Terrain,
    ClearanceBakeSnapshot Clearance,
    TerrainNavigationTopologySnapshot AuthoredTopology,
    DynamicTerrainTopologyRoutePlanner RoutePlanner,
    RtsSimulation Simulation);
