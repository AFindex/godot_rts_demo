using System.Numerics;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;

namespace RtsDemo.Demos.War3;

public sealed record War3TerrainShowcasePreset(
    string Id,
    string DisplayName,
    string Purpose,
    TerrainMapSnapshot Terrain,
    TerrainVisualLayerMap VisualLayers);

/// <summary>
/// Dense visual fixtures for inspecting classic cliff ownership and War3
/// dual-grid transitions. These are presentation test maps, not gameplay
/// scenario resources, and intentionally put material boundaries across
/// height boundaries instead of arranging a plausible skirmish map.
/// </summary>
public static class War3TerrainStressPresetCatalog
{
    private const float CellSize = 32f;
    private const float CliffHeight = 48f;

    private static readonly TerrainSurfaceDefinition[] Surfaces =
    [
        new(0, "sand", "Lordaeron Dirt"),
        new(1, "metal", "Lordaeron Dirt Rough"),
        new(2, "rock", "Lordaeron Rock"),
        new(3, "badlands", "Lordaeron Grass")
    ];

    private static readonly War3TerrainShowcasePreset[] Values = CreateAll();

    public static ReadOnlySpan<War3TerrainShowcasePreset> Presets => Values;

    private static War3TerrainShowcasePreset[] CreateAll() =>
    [
        InterlockedRidges(),
        NestedArchipelago(),
        SerpentineCanyons(),
        SignatureMatrix(),
        MaterialWeave()
    ];

    private static War3TerrainShowcasePreset InterlockedRidges()
    {
        const int columns = 48;
        const int rows = 34;
        var canvas = new StressCanvas(columns, rows);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var horizontal = Math.Abs(
                    row - (7 + TriangleWave(column + 2, 18))) <= 2;
                var vertical = Math.Abs(
                    column - (27 + TriangleWave(row + 5, 14))) <= 2;
                var spur = Math.Abs(row - (29 - column / 3)) <= 1 &&
                           column is >= 12 and <= 38;
                byte level = horizontal || vertical || spur ? (byte)1 : (byte)0;
                if (horizontal && vertical ||
                    vertical && spur ||
                    horizontal && spur)
                {
                    level = 2;
                }
                if (level > 0 && (column * 3 + row * 5) % 29 <= 1)
                    level = 0;
                var surface = (ushort)(Math.Abs(
                    column / 5 + row / 4 + (horizontal ? 1 : 0)) % 4);
                canvas.Set(column, row, level, surface);
            }
        }
        return canvas.Preset(
            "interlocked-ridges",
            "交错山脊与凹角",
            "三条折线山脊互相穿插，包含 T/X 连接、孔洞、尖角和二层交点。",
            static (column, row) =>
                (byte)((column / 4 + row / 3 +
                        ((column + row) % 9 <= 1 ? 1 : 0)) % 4));
    }

    private static War3TerrainShowcasePreset NestedArchipelago()
    {
        const int columns = 46;
        const int rows = 36;
        var canvas = new StressCanvas(columns, rows);
        var centreX = (columns - 1) * 0.5f;
        var centreY = (rows - 1) * 0.5f;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var dx = (column - centreX) * 0.82f;
                var dy = row - centreY;
                var disturbedRadius = MathF.Sqrt(dx * dx + dy * dy) +
                                      MathF.Sin(column * 0.73f) * 1.2f +
                                      MathF.Cos(row * 0.61f) * 0.9f;
                byte level = disturbedRadius < 7f
                    ? (byte)2
                    : disturbedRadius < 13f
                        ? (byte)1
                        : (byte)0;

                // Two cuts turn the rings into concave islands and expose
                // opposing inside/outside cliff corners.
                var diagonalCut = MathF.Abs(
                    (row - centreY) - (column - centreX) * 0.31f) < 1.15f;
                var bentCut = MathF.Abs(
                    (column - centreX) + MathF.Sin(row * 0.38f) * 5f) < 1.1f;
                if (diagonalCut || bentCut)
                    level = 0;

                level = Math.Max(level,
                    SatelliteLevel(column, row, 7, 7, 5));
                level = Math.Max(level,
                    SatelliteLevel(column, row, 39, 8, 4));
                level = Math.Max(level,
                    SatelliteLevel(column, row, 8, 29, 4));
                level = Math.Max(level,
                    SatelliteLevel(column, row, 38, 29, 5));
                var surface = (ushort)((
                    (int)MathF.Floor(disturbedRadius / 3f) +
                    (column + 2 * row) / 7) % 4);
                canvas.Set(column, row, level, surface);
            }
        }
        return canvas.Preset(
            "nested-archipelago",
            "嵌套盆地与岛中岛",
            "扰动同心高地被两条沟槽切开，并由四组卫星岛形成内外双重悬崖。",
            (column, row) =>
            {
                var dx = column - centreX;
                var dy = row - centreY;
                var ring = (int)(MathF.Sqrt(dx * dx + dy * dy) / 3f);
                var wedge = (column + row * 2) / 6;
                return (byte)(Math.Abs(ring + wedge) % 4);
            });
    }

    private static War3TerrainShowcasePreset SerpentineCanyons()
    {
        const int columns = 52;
        const int rows = 34;
        var canvas = new StressCanvas(columns, rows);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var centre = 17f + MathF.Sin(column * 0.34f) * 8f;
                var distance = MathF.Abs(row - centre);
                byte level = distance <= 2f
                    ? (byte)0
                    : distance <= 4.5f
                        ? (byte)1
                        : (byte)2;

                var tributaryA = MathF.Abs(
                    row - (5f + column * 0.43f)) < 1.5f;
                var tributaryB = MathF.Abs(
                    row - (32f - column * 0.37f)) < 1.35f;
                if (tributaryA || tributaryB)
                    level = 0;
                else if (MathF.Abs(
                             row - (5f + column * 0.43f)) < 3f ||
                         MathF.Abs(
                             row - (32f - column * 0.37f)) < 2.8f)
                {
                    level = Math.Min(level, (byte)1);
                }

                var surface = (ushort)(Math.Abs(
                    column / 3 - row / 4 + (level == 1 ? 2 : 0)) % 4);
                canvas.Set(column, row, level, surface);
            }
        }
        return canvas.Preset(
            "serpentine-canyons",
            "蛇形峡谷与支流",
            "二层高原中切出蛇形主谷和交叉支谷，集中检查内凹脚边与窄岬顶部。",
            static (column, row) =>
            {
                if (column % 9 <= 1) return (byte)0;
                if (row % 7 <= 1) return (byte)3;
                return (byte)(Math.Abs(column - row * 2) % 3);
            });
    }

    private static War3TerrainShowcasePreset SignatureMatrix()
    {
        const int patch = 6;
        const int columns = patch * 8 + 2;
        const int rows = patch * 8 + 2;
        var canvas = new StressCanvas(columns, rows);
        var signatures = NormalizedCliffSignatures();
        for (var index = 0; index < signatures.Length; index++)
        {
            var originColumn = 2 + index % 8 * patch;
            var originRow = 2 + index / 8 * patch;
            var signature = signatures[index];
            var surface = (ushort)(index % 4);
            // Filename order is TL, TR, BR, BL.
            canvas.Set(originColumn, originRow + 1,
                Height(signature[0]), surface);
            canvas.Set(originColumn + 1, originRow + 1,
                Height(signature[1]), surface);
            canvas.Set(originColumn + 1, originRow,
                Height(signature[2]), surface);
            canvas.Set(originColumn, originRow,
                Height(signature[3]), surface);
        }
        return canvas.Preset(
            "signature-matrix",
            "64 种悬崖签名矩阵",
            "8×8 微型单元覆盖全部非平坦 CliffsABCD 四角签名，专门暴露角块和接缝错误。",
            static (column, row) =>
            {
                var patchColumn = Math.Max(0, column - 1) / patch;
                var patchRow = Math.Max(0, row - 1) / patch;
                var local = (column + row) % 3 == 0 ? 1 : 0;
                return (byte)((patchColumn + patchRow + local) % 4);
            });
    }

    private static War3TerrainShowcasePreset MaterialWeave()
    {
        const int columns = 48;
        const int rows = 36;
        var canvas = new StressCanvas(columns, rows);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var blockColumn = column / 6;
                var blockRow = row / 6;
                byte level = (byte)((blockColumn + blockRow) % 3);
                if ((column + row * 2) % 17 <= 1)
                    level = (byte)((level + 1) % 3);
                if (column is > 18 and < 30 && row is > 12 and < 24)
                {
                    var ring = Math.Min(
                        Math.Min(column - 19, 29 - column),
                        Math.Min(row - 13, 23 - row));
                    level = ring <= 1 ? (byte)2 : (byte)0;
                }
                var surface = (ushort)((column / 2 + row / 3 * 2) % 4);
                canvas.Set(column, row, level, surface);
            }
        }
        return canvas.Preset(
            "material-weave",
            "四层贴图编织场",
            "0/1/2 层棋盘、扰动缝和中心反转环叠加四种窄带材质，制造大量跨 cliff 过渡。",
            static (column, row) =>
            {
                if (column % 8 <= 1) return 0;
                if (row % 7 <= 1) return 3;
                if ((column + row) % 10 <= 1) return 2;
                return 1;
            });
    }

    private static byte SatelliteLevel(
        int column,
        int row,
        int centreColumn,
        int centreRow,
        int radius)
    {
        var dx = column - centreColumn;
        var dy = row - centreRow;
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared <= radius * radius / 3)
            return 2;
        return distanceSquared <= radius * radius ? (byte)1 : (byte)0;
    }

    private static int TriangleWave(int value, int period)
    {
        var wrapped = ((value % period) + period) % period;
        return wrapped <= period / 2 ? wrapped : period - wrapped;
    }

    private static string[] NormalizedCliffSignatures()
    {
        var result = new List<string>(64);
        for (var tl = 0; tl < 3; tl++)
        for (var tr = 0; tr < 3; tr++)
        for (var br = 0; br < 3; br++)
        for (var bl = 0; bl < 3; bl++)
        {
            Span<int> heights = [tl, tr, br, bl];
            if (!heights.Contains(0) || heights.IndexOfAnyExcept(0) < 0)
                continue;
            result.Add(string.Create(4, heights.ToArray(),
                static (characters, values) =>
                {
                    for (var index = 0; index < characters.Length; index++)
                        characters[index] = (char)('A' + values[index]);
                }));
        }
        return result.ToArray();
    }

    private static byte Height(char code) => (byte)(code - 'A');

    private sealed class StressCanvas
    {
        private readonly int _columns;
        private readonly int _rows;
        private readonly TerrainCell[] _cells;

        public StressCanvas(int columns, int rows)
        {
            _columns = columns;
            _rows = rows;
            _cells = Enumerable.Repeat(Cell(0, 3), columns * rows).ToArray();
        }

        public void Set(int column, int row, byte level, ushort surface)
        {
            _cells[row * _columns + column] = Cell(level, surface);
        }

        public War3TerrainShowcasePreset Preset(
            string id,
            string displayName,
            string purpose,
            Func<int, int, byte> visualLayer)
        {
            var terrain = TerrainMapSnapshot.TryCreate(
                new SimRect(Vector2.Zero,
                    new Vector2(_columns * CellSize, _rows * CellSize)),
                CellSize,
                CliffHeight,
                Surfaces,
                _cells,
                out var snapshot,
                out var validation)
                ? snapshot
                : throw new InvalidOperationException(
                    $"Stress preset {id} failed: {validation.FirstError}.");
            if (terrain is null)
                throw new InvalidOperationException($"Stress preset {id} is null.");

            var pointColumns = _columns + 1;
            var pointRows = _rows + 1;
            var layers = new byte[checked(pointColumns * pointRows)];
            var variations = new byte[layers.Length];
            for (var row = 0; row < pointRows; row++)
            {
                for (var column = 0; column < pointColumns; column++)
                {
                    var index = row * pointColumns + column;
                    layers[index] = (byte)(visualLayer(column, row) % 4);
                    variations[index] = Variation(column, row);
                }
            }
            var visual = TerrainVisualLayerMap.FromPoints(
                terrain, layers, variations);
            return new War3TerrainShowcasePreset(
                id, displayName, purpose, terrain, visual);
        }

        private static TerrainCell Cell(byte level, ushort surface) =>
            new(
                level,
                surface,
                TerrainPathing.Ground,
                TerrainCellFlags.Buildable);

        private static byte Variation(int column, int row)
        {
            unchecked
            {
                var hash = (uint)(column * 73856093) ^
                           (uint)(row * 19349663);
                hash ^= hash >> 13;
                return (byte)(hash % 17u);
            }
        }
    }
}
