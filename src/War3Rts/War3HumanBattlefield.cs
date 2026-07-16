using System.Numerics;
using RtsDemo.Simulation;

namespace War3Rts;

/// <summary>
/// Deterministic terrain composition for the formal Human-versus-Human map.
/// Gameplay cells, continuous relief and War3 ramp metadata are authored here;
/// resource and army composition remain in <see cref="War3HumanScenario"/>.
/// </summary>
public static class War3HumanBattlefield
{
    private const int Columns = 100;
    private const int Rows = 60;

    public static TerrainMapSnapshot Create(
        SimRect bounds,
        float cellSize,
        float cliffHeight)
    {
        TerrainSurfaceDefinition[] surfaces =
        [
            new(0, "sand", "Lordaeron Dirt"),
            new(1, "metal", "Lordaeron Dirt Rough"),
            new(2, "rock", "Lordaeron Rock"),
            new(3, "badlands", "Lordaeron Grass")
        ];
        var cells = new TerrainCell[Columns * Rows];
        for (var row = 0; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
        {
            var elevated = IsBasePlatform(column, row);
            cells[row * Columns + column] = new TerrainCell(
                elevated ? (byte)1 : (byte)0,
                SurfaceAt(column, row, elevated),
                TerrainPathing.Ground,
                TerrainCellFlags.Buildable);
        }

        // War3 ramps are authored on the lower level. A ramp cell plus its
        // low/high neighbours becomes the three-point W3E ramp strip used by
        // TerrainClassicRampLayout and the authoritative HeightAt query.
        for (var row = 26; row <= 33; row++)
        {
            cells[row * Columns + 36] = Ramp(
                TerrainRampDirection.NegativeX);
            cells[row * Columns + 63] = Ramp(
                TerrainRampDirection.PositiveX);
        }

        var fineHeightPoints = new float[(Columns + 1) * (Rows + 1)];
        for (var row = 0; row <= Rows; row++)
        for (var column = 0; column <= Columns; column++)
        {
            fineHeightPoints[row * (Columns + 1) + column] =
                FineHeight(column, row, cellSize);
        }

        if (!TerrainMapSnapshot.TryCreate(
                bounds,
                cellSize,
                cliffHeight,
                surfaces,
                cells,
                fineHeightPoints,
                out var terrain,
                out var validation) || terrain is null)
        {
            throw new InvalidOperationException(
                $"war3_rts terrain is invalid: {validation.FirstError}.");
        }
        return terrain;
    }

    private static TerrainCell Ramp(TerrainRampDirection direction) => new(
        0,
        0,
        TerrainPathing.Ground,
        TerrainCellFlags.Ramp,
        direction);

    private static bool IsBasePlatform(int column, int row) =>
        InRoundedRectangle(column, row, 4, 14, 35, 45, 5) ||
        InRoundedRectangle(column, row, 64, 14, 95, 45, 5);

    private static bool InRoundedRectangle(
        int column,
        int row,
        int minimumColumn,
        int minimumRow,
        int maximumColumn,
        int maximumRow,
        int cornerRadius)
    {
        if (column < minimumColumn || column > maximumColumn ||
            row < minimumRow || row > maximumRow)
        {
            return false;
        }
        var x = column < minimumColumn + cornerRadius
            ? minimumColumn + cornerRadius - column
            : column > maximumColumn - cornerRadius
                ? column - (maximumColumn - cornerRadius)
                : 0;
        var y = row < minimumRow + cornerRadius
            ? minimumRow + cornerRadius - row
            : row > maximumRow - cornerRadius
                ? row - (maximumRow - cornerRadius)
                : 0;
        return x * x + y * y <= cornerRadius * cornerRadius;
    }

    private static ushort SurfaceAt(int column, int row, bool elevated)
    {
        if (row is >= 26 and <= 33 && column is >= 17 and <= 82)
            return (ushort)((column + row) % 7 == 0 ? 1 : 0);
        if (elevated)
        {
            var homeColumn = column < 50 ? 20 : 80;
            var dx = Math.Abs(column - homeColumn);
            var dy = Math.Abs(row - 30);
            if (dx <= 7 && dy <= 7)
                return 0;
            return 3;
        }
        if (column is >= 42 and <= 57 &&
            (row is >= 7 and <= 16 || row is >= 43 and <= 52))
        {
            return 2;
        }
        if ((column + row * 3) % 29 <= 2)
            return 1;
        return 3;
    }

    private static float FineHeight(int column, int row, float cellSize)
    {
        var x = column * cellSize;
        var y = row * cellSize;
        var rolling = MathF.Sin(column * 0.17f) * 5f +
                      MathF.Cos(row * 0.23f) * 4f +
                      MathF.Sin((column + row) * 0.11f) * 3f +
                      Gaussian(x, y, 1_600f, 430f, 360f, 10f) -
                      Gaussian(x, y, 1_600f, 1_500f, 420f, 8f);

        // Keep both construction plateaus and their ramp shoulders level,
        // then blend into rolling low ground toward the centre battlefield.
        var insideVerticalBand = MathF.Min(y - 320f, 1_600f - y);
        var verticalStrength = SmoothStep(0f, 128f, insideVerticalBand);
        float horizontalStrength;
        if (x <= 1_216f || x >= 1_984f)
        {
            horizontalStrength = 1f;
        }
        else
        {
            var distanceFromShoulder = MathF.Min(
                x - 1_216f, 1_984f - x);
            horizontalStrength = 1f - SmoothStep(
                0f, 224f, distanceFromShoulder);
        }
        return rolling * (1f - horizontalStrength * verticalStrength);
    }

    private static float Gaussian(
        float x,
        float y,
        float centreX,
        float centreY,
        float radius,
        float amplitude)
    {
        var dx = x - centreX;
        var dy = y - centreY;
        return amplitude * MathF.Exp(-(dx * dx + dy * dy) /
                                     (2f * radius * radius));
    }

    private static float SmoothStep(float minimum, float maximum, float value)
    {
        var weight = Math.Clamp(
            (value - minimum) / MathF.Max(0.0001f, maximum - minimum),
            0f,
            1f);
        return weight * weight * (3f - 2f * weight);
    }
}
