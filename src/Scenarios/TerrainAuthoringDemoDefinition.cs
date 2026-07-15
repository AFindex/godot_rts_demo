using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Scenarios;

public static class TerrainAuthoringDemoDefinition
{
    public const int Columns = 20;
    public const int Rows = 12;
    public const float CellSize = 32f;

    public static TerrainAuthoringDocument CreateDocument()
    {
        TerrainSurfaceDefinition[] surfaces =
        [
            new(0, "badlands", "Badlands Soil"),
            new(1, "rock", "Cliff Rock"),
            new(2, "metal", "Ramp Trim"),
            new(3, "shallow-water", "Shallow Water"),
            new(4, "vision-smoke", "Obstructing Smoke")
        ];
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var cells = Enumerable.Repeat(low, Columns * Rows).ToArray();
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 11; column < Columns; column++)
            {
                cells[row * Columns + column] = low with
                {
                    CliffLevel = 1,
                    SurfaceId = 1
                };
            }
        }
        for (var row = 4; row <= 7; row++)
        {
            cells[row * Columns + 10] = low with
            {
                SurfaceId = 2,
                Flags = TerrainCellFlags.Ramp,
                RampDirection = TerrainRampDirection.PositiveX
            };
        }
        for (var row = 9; row <= 10; row++)
        {
            for (var column = 2; column <= 7; column++)
            {
                cells[row * Columns + column] = low with
                {
                    SurfaceId = 3,
                    Pathing = TerrainPathing.ShallowWater,
                    Flags = TerrainCellFlags.None
                };
            }
        }
        for (var row = 8; row <= 10; row++)
        {
            for (var column = 15; column <= 18; column++)
            {
                cells[row * Columns + column] = cells[row * Columns + column] with
                {
                    SurfaceId = 4,
                    Flags = TerrainCellFlags.Buildable |
                            TerrainCellFlags.BlocksVision
                };
            }
        }
        TerrainAuthoringAnchor[] anchors =
        [
            new(0, TerrainAuthoringAnchorKind.Spawn, Center(3, 5), 8f),
            new(1, TerrainAuthoringAnchorKind.Resource, Center(16, 5), 8f),
            new(2, TerrainAuthoringAnchorKind.Objective, Center(13, 9), 8f)
        ];
        return new TerrainAuthoringDocument(
            new SimRect(Vector2.Zero,
                new Vector2(Columns * CellSize, Rows * CellSize)),
            CellSize,
            48f,
            surfaces,
            cells,
            anchors);
    }

    public static Vector2 Center(int column, int row) =>
        new((column + 0.5f) * CellSize, (row + 0.5f) * CellSize);
}
