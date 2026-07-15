using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Scenarios;

/// <summary>
/// Production-stack terrain traversal fixture used by the dedicated 3D demo.
/// It owns authored inputs and expected geographic regions, while the demo
/// owns only presentation and observation.
/// </summary>
public static class TerrainTraversalScenario
{
    public const float CellSize = 40f;
    public const float CliffLevelHeight = 52f;
    public const int RampFirstRow = 7;
    public const int RampLastRow = 10;
    public const int RampColumn = 12;

    public static readonly SimRect Bounds = new(
        Vector2.Zero, new Vector2(1_280f, 720f));
    public static readonly SimRect ShallowWater = new(
        new Vector2(120f, 500f), new Vector2(400f, 660f));
    public static readonly SimRect ShallowWaterBuildingFootprint = new(
        new Vector2(216f, 536f), new Vector2(304f, 624f));
    public static readonly Vector2 Destination = new(1_035f, 150f);

    public static TerrainTraversalRuntime Prepare()
    {
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                Bounds,
                [], [], [], [],
                out var navigation,
                out var validation) || navigation is null)
        {
            throw new InvalidOperationException(
                $"Terrain traversal navigation failed: {validation.FirstError}.");
        }

        var terrain = CreateTerrain();
        var clearance = ClearanceBakeSnapshot.Build(
            navigation, terrain, cellSize: 16f);
        var topology = TerrainNavigationTopologyBuilder.Build(
            terrain, clearance);
        var world = navigation.CreateWorld(terrain);
        var pathProvider = new GridPathProvider(world, 16f, clearance);
        var simulation = new RtsSimulation(
            world,
            pathProvider,
            capacity: 64,
            groupRoutePlanner: topology.CreateRoutePlanner(),
            chokeController: topology.CreateChokeController(),
            clearanceBake: clearance);

        var units = new int[18];
        for (var index = 0; index < units.Length; index++)
        {
            var row = index / 6;
            var column = index % 6;
            units[index] = simulation.AddUnit(
                new Vector2(125f + column * 30f, 115f + row * 31f),
                team: 1,
                combatProfile: CombatProfileSnapshot.Standard,
                radius: 8f,
                maxSpeed: 142f,
                acceleration: 840f);
        }

        simulation.IssueMove(units, Destination);
        var placement = BuildingPlacementValidator.ValidateStatic(
            world,
            ShallowWaterBuildingFootprint,
            new BuildingPlacementRules(
                MovementClass.Medium,
                PreserveConnectivity: false));

        return new TerrainTraversalRuntime(
            navigation,
            terrain,
            clearance,
            topology,
            simulation,
            units,
            placement);
    }

    public static bool IsInsideRampCorridor(Vector2 position, float margin = 0f)
    {
        var minimumY = RampFirstRow * CellSize - margin;
        var maximumY = (RampLastRow + 1) * CellSize + margin;
        return position.Y >= minimumY && position.Y <= maximumY;
    }

    public static TerrainMapSnapshot CreateTerrain()
    {
        TerrainSurfaceDefinition[] surfaces =
        [
            new(0, "badlands", "Low Ground"),
            new(1, "rock", "High Ground"),
            new(2, "shallow-water", "Shallow Water"),
            new(3, "metal", "Ramp")
        ];
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var high = new TerrainCell(
            1, 1, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var builder = new TerrainMapBuilder(
            Bounds, CellSize, CliffLevelHeight, surfaces, low);

        // The high plateau begins at the ramp column. Every boundary cell is
        // a real cliff except the four-cell-wide continuous ramp.
        for (var row = 0; row < builder.Rows; row++)
        {
            for (var column = RampColumn; column < builder.Columns; column++)
                builder.SetCell(column, row, high);
        }
        var ramp = new TerrainCell(
            0,
            3,
            TerrainPathing.Ground,
            TerrainCellFlags.Ramp,
            TerrainRampDirection.PositiveX);
        for (var row = RampFirstRow; row <= RampLastRow; row++)
            builder.SetCell(RampColumn, row, ramp);

        var water = new TerrainCell(
            0, 2, TerrainPathing.ShallowWater, TerrainCellFlags.None);
        builder.Paint(ShallowWater, water);
        return builder.Build();
    }
}

public sealed record TerrainTraversalRuntime(
    NavigationMapSnapshot Navigation,
    TerrainMapSnapshot Terrain,
    ClearanceBakeSnapshot Clearance,
    TerrainNavigationTopologySnapshot Topology,
    RtsSimulation Simulation,
    int[] Units,
    StaticPlacementResult ShallowWaterPlacement);
