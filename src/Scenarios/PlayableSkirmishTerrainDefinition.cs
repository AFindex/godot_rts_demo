using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Scenarios;

/// <summary>
/// Procedural test terrain for the playable 3D skirmish. It keeps established
/// gameplay routes valid while exercising independent surface, water, no-build
/// and cliff presentation data.
/// </summary>
public static class PlayableSkirmishTerrainDefinition
{
    private const float CellSize = 40f;
    private const float CliffLevelHeight = 48f;

    private static readonly TerrainSurfaceDefinition[] Surfaces =
    [
        new(0, "badlands", "Badlands Soil"),
        new(1, "rock", "Cliff Rock"),
        new(2, "sand", "Dry Sand"),
        new(3, "shallow-water", "Shallow Water"),
        new(4, "metal", "Installation Plate")
    ];

    public static TerrainMapSnapshot Create(NavigationMapSnapshot navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        var ground = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var builder = new TerrainMapBuilder(
            navigation.WorldBounds,
            CellSize,
            CliffLevelHeight,
            Surfaces,
            ground);

        builder.Paint(
            new SimRect(new Vector2(40f, 40f), new Vector2(1_060f, 560f)),
            ground with { SurfaceId = 2 });
        builder.Paint(
            new SimRect(new Vector2(2_140f, 1_240f), new Vector2(3_160f, 1_760f)),
            ground with { SurfaceId = 2 });
        builder.Paint(
            new SimRect(new Vector2(200f, 640f), new Vector2(680f, 1_160f)),
            ground with { SurfaceId = 4 });
        builder.Paint(
            new SimRect(new Vector2(2_520f, 640f), new Vector2(3_000f, 1_160f)),
            ground with { SurfaceId = 4 });

        // Ground units may cross shallow water, but structures cannot be placed
        // on it. Gameplay pathing is independent of the visual material key.
        var shallowWater = new TerrainCell(
            0, 3, TerrainPathing.ShallowWater, TerrainCellFlags.None);
        builder.Paint(
            new SimRect(new Vector2(1_200f, 520f), new Vector2(2_000f, 640f)),
            shallowWater);
        builder.Paint(
            new SimRect(new Vector2(1_200f, 1_160f), new Vector2(2_000f, 1_280f)),
            shallowWater);

        // Existing hard obstacles become raised cliff masses visually. They
        // remain rectangles too, so presentation migration cannot silently
        // alter the established playable map.
        var cliff = new TerrainCell(
            1,
            1,
            TerrainPathing.None,
            TerrainCellFlags.BlocksVision | TerrainCellFlags.BlocksCreep);
        foreach (var obstacle in navigation.Obstacles)
            builder.PaintContained(obstacle, cliff);

        return builder.Build();
    }
}
