using System.Numerics;
using RtsDemo.Simulation;
using War3Rts.Pcg;

namespace War3Rts;

/// <summary>
/// Deterministic terrain composition for the formal Human-versus-Human map.
/// Gameplay cells, continuous relief and War3 ramp metadata are authored here;
/// resource and army composition remain in <see cref="War3HumanScenario"/>.
/// </summary>
public static class War3HumanBattlefield
{
    private const int Columns = 200;
    private const int Rows = 120;

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
        for (var row = 55; row <= 64; row++)
        {
            cells[row * Columns + 60] = Ramp(
                TerrainRampDirection.NegativeX);
            cells[row * Columns + 139] = Ramp(
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
        InRoundedRectangle(column, row, 20, 40, 59, 79, 7) ||
        InRoundedRectangle(column, row, 140, 40, 179, 79, 7);

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
        var disturbance = PcgHashNoise.Fractal01(
            column * 0.13f, row * 0.13f, 0x5741_5233u);
        if (row is >= 55 and <= 64 && column is >= 35 and <= 164)
            return (ushort)(disturbance > 0.77f ? 1 : 0);
        if (elevated)
        {
            var homeColumn = column < 100 ? 40 : 160;
            var dx = Math.Abs(column - homeColumn);
            var dy = Math.Abs(row - 60);
            if (dx <= 10 && dy <= 10)
                return 0;
            return (ushort)(disturbance > 0.82f ? 1 : 3);
        }
        var nearNeutralMine =
            Ellipse(column, row, 75, 22, 12, 9) ||
            Ellipse(column, row, 125, 22, 12, 9) ||
            Ellipse(column, row, 75, 97, 12, 9) ||
            Ellipse(column, row, 125, 97, 12, 9);
        if (nearNeutralMine && disturbance > 0.24f)
        {
            return 2;
        }
        if (disturbance > 0.70f)
            return 1;
        return 3;
    }

    private static bool Ellipse(
        int column,
        int row,
        int centerColumn,
        int centerRow,
        int radiusX,
        int radiusY)
    {
        var x = (column - centerColumn) / (float)radiusX;
        var y = (row - centerRow) / (float)radiusY;
        return x * x + y * y <= 1f;
    }

    private static float FineHeight(int column, int row, float cellSize)
    {
        var x = column * cellSize;
        var y = row * cellSize;
        var rolling = MathF.Sin(column * 0.17f) * 5f +
                      MathF.Cos(row * 0.23f) * 4f +
                      MathF.Sin((column + row) * 0.11f) * 3f +
                      MathF.Sin((column - row) * 0.071f) * 2.5f +
                      Gaussian(x, y, 3_200f, 720f, 520f, 11f) -
                      Gaussian(x, y, 3_200f, 3_120f, 620f, 9f) +
                      Gaussian(x, y, 2_560f, 2_850f, 460f, 6f) -
                      Gaussian(x, y, 3_920f, 1_080f, 430f, 5f);

        // Keep both construction plateaus and their ramp shoulders level,
        // then blend into rolling low ground toward the centre battlefield.
        var insideVerticalBand = MathF.Min(y - 1_152f, 2_688f - y);
        var verticalStrength = SmoothStep(0f, 128f, insideVerticalBand);
        float horizontalStrength;
        if (x <= 2_016f || x >= 4_384f)
        {
            horizontalStrength = 1f;
        }
        else
        {
            var distanceFromShoulder = MathF.Min(
                x - 2_016f, 4_384f - x);
            horizontalStrength = 1f - SmoothStep(
                0f, 320f, distanceFromShoulder);
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
