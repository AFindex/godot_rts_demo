using RtsDemo.Simulation;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Warcraft III corner-ramp reconstruction and CliffTrans selection. One
/// directional gameplay ramp cell expands to the three W3E height samples
/// (outside low, ramp low, inside high). CliffTrans models are emitted only
/// along the two lateral borders of a ramp strip; the walkable centre remains
/// the shared terrain mesh.
/// </summary>
public sealed class TerrainClassicRampLayout
{
    private readonly TerrainClassicRampTile[] _tiles;
    private readonly TerrainClassicRampSurfaceTile[] _surfaceTiles;
    private readonly TerrainClassicRampSurfaceTile[] _transitionUnderlayTiles;
    private readonly bool[] _mappedRampCells;
    private readonly bool[] _coveredClassicTiles;

    private TerrainClassicRampLayout(
        int columns,
        int rows,
        TerrainClassicRampTile[] tiles,
        TerrainClassicRampSurfaceTile[] surfaceTiles,
        TerrainClassicRampSurfaceTile[] transitionUnderlayTiles,
        bool[] mappedRampCells,
        bool[] coveredClassicTiles,
        TerrainClassicRampDiagnostics diagnostics)
    {
        Columns = columns;
        Rows = rows;
        _tiles = tiles;
        _surfaceTiles = surfaceTiles;
        _transitionUnderlayTiles = transitionUnderlayTiles;
        _mappedRampCells = mappedRampCells;
        _coveredClassicTiles = coveredClassicTiles;
        Diagnostics = diagnostics;
    }

    public int Columns { get; }
    public int Rows { get; }
    public ReadOnlySpan<TerrainClassicRampTile> Tiles => _tiles;
    public ReadOnlySpan<TerrainClassicRampSurfaceTile> SurfaceTiles =>
        _surfaceTiles;
    public ReadOnlySpan<TerrainClassicRampSurfaceTile> TransitionUnderlayTiles =>
        _transitionUnderlayTiles;
    public TerrainClassicRampDiagnostics Diagnostics { get; }

    public static TerrainClassicRampLayout Build(
        TerrainMapSnapshot terrain,
        Func<string, bool> hasMesh)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(hasMesh);
        var rampFlags = new bool[checked(terrain.Columns * terrain.Rows)];
        var mappedRampCells = new bool[rampFlags.Length];
        var authoredRampCells = 0;
        var invalidRampCells = 0;

        for (var row = 0; row < terrain.Rows; row++)
        for (var column = 0; column < terrain.Columns; column++)
        {
            var cell = terrain.Cell(column, row);
            if (!cell.IsRamp)
                continue;
            authoredRampCells++;
            var (dx, dy) = Direction(cell.RampDirection);
            var lowColumn = column - dx;
            var lowRow = row - dy;
            var highColumn = column + dx;
            var highRow = row + dy;
            if (!InBounds(lowColumn, lowRow) ||
                !InBounds(highColumn, highRow) ||
                terrain.Cell(lowColumn, lowRow).CliffLevel != cell.CliffLevel ||
                terrain.Cell(highColumn, highRow).CliffLevel !=
                cell.CliffLevel + 1)
            {
                invalidRampCells++;
                continue;
            }
            mappedRampCells[Index(column, row)] = true;
            rampFlags[Index(lowColumn, lowRow)] = true;
            rampFlags[Index(column, row)] = true;
            rampFlags[Index(highColumn, highRow)] = true;
        }

        var tiles = new List<TerrainClassicRampTile>();
        var surfaceTiles = new Dictionary<
            (int Column, int Row),
            TerrainClassicRampSurfaceTile>();
        var transitionUnderlayTiles = new Dictionary<
            (int Column, int Row),
            TerrainClassicRampSurfaceTile>();
        var covered = new bool[checked(
            Math.Max(0, terrain.Columns - 1) *
            Math.Max(0, terrain.Rows - 1))];
        var candidates = 0;
        var rejectedPatterns = 0;

        // War3 keeps the ground for a full ramp entrance and raises every
        // low endpoint of the changing tile by half a cliff level. Because
        // that endpoint is shared, the preceding flat tile becomes the first
        // half of a two-tile slope.
        var rampPointLevels = new float[rampFlags.Length];
        for (var row = 0; row < terrain.Rows; row++)
        for (var column = 0; column < terrain.Columns; column++)
        {
            rampPointLevels[Index(column, row)] =
                terrain.Cell(column, row).CliffLevel;
        }
        for (var row = 0; row + 1 < terrain.Rows; row++)
        for (var column = 0; column + 1 < terrain.Columns; column++)
        {
            if (!FullRampTile(column, row))
                continue;
            var bl = Index(column, row);
            var br = Index(column + 1, row);
            var tl = Index(column, row + 1);
            var tr = Index(column + 1, row + 1);
            // Read only authored layer heights. War3 writes the +0.5 result
            // to a separate final-height buffer; reading a value raised by a
            // previous tile cascades 0.5 into 1.0 and collapses the intended
            // two-tile ramp into one sloped tile.
            var blLevel = terrain.Cell(column, row).CliffLevel;
            var brLevel = terrain.Cell(column + 1, row).CliffLevel;
            var tlLevel = terrain.Cell(column, row + 1).CliffLevel;
            var trLevel = terrain.Cell(column + 1, row + 1).CliffLevel;
            if (MathF.Abs(blLevel - trLevel) < 0.001f &&
                MathF.Abs(tlLevel - brLevel) < 0.001f)
            {
                continue;
            }
            var baseLevel = MathF.Min(
                MathF.Min(blLevel, brLevel),
                MathF.Min(tlLevel, trLevel));
            RaiseLow(bl, blLevel);
            RaiseLow(br, brLevel);
            RaiseLow(tl, tlLevel);
            RaiseLow(tr, trLevel);

            void RaiseLow(int index, float authoredLevel)
            {
                if (MathF.Abs(authoredLevel - baseLevel) < 0.001f)
                    rampPointLevels[index] = baseLevel + 0.5f;
            }
        }
        for (var row = 0; row + 1 < terrain.Rows; row++)
        for (var column = 0; column + 1 < terrain.Columns; column++)
        {
            if (!FullRampTile(column, row))
                continue;
            var bottomLeft = rampPointLevels[Index(column, row)];
            var bottomRight = rampPointLevels[Index(column + 1, row)];
            var topRight = rampPointLevels[Index(column + 1, row + 1)];
            var topLeft = rampPointLevels[Index(column, row + 1)];
            var minimum = MathF.Min(
                MathF.Min(bottomLeft, bottomRight),
                MathF.Min(topLeft, topRight));
            var maximum = MathF.Max(
                MathF.Max(bottomLeft, bottomRight),
                MathF.Max(topLeft, topRight));
            if (maximum - minimum < 0.001f)
                continue;
            AddSurfaceTile(column, row);
        }

        for (var row = 0; row + 1 < terrain.Rows; row++)
        for (var column = 0; column + 1 < terrain.Columns; column++)
        {
            // HiveWE/War3 checks the 1x2 form first and skips the 2x1 form
            // when both could claim the same origin.
            if (row + 2 < terrain.Rows &&
                TryVerticalSignature(column, row, out var verticalSignature,
                    out var verticalBase))
            {
                if (hasMesh(verticalSignature))
                {
                    candidates++;
                    AddTile(
                        column, row, verticalSignature, verticalBase,
                        TerrainClassicRampAxis.Vertical, 1, 2);
                    continue;
                }
                rejectedPatterns++;
            }
            if (column + 2 < terrain.Columns &&
                TryHorizontalSignature(column, row, out var horizontalSignature,
                    out var horizontalBase))
            {
                if (hasMesh(horizontalSignature))
                {
                    candidates++;
                    AddTile(
                        column, row, horizontalSignature, horizontalBase,
                        TerrainClassicRampAxis.Horizontal, 2, 1);
                }
                else
                {
                    rejectedPatterns++;
                }
            }
        }

        return new TerrainClassicRampLayout(
            terrain.Columns,
            terrain.Rows,
            tiles.ToArray(),
            surfaceTiles.Values
                .OrderBy(static tile => tile.Row)
                .ThenBy(static tile => tile.Column)
                .ToArray(),
            transitionUnderlayTiles.Values
                .OrderBy(static tile => tile.Row)
                .ThenBy(static tile => tile.Column)
                .ToArray(),
            mappedRampCells,
            covered,
            new TerrainClassicRampDiagnostics(
                authoredRampCells,
                authoredRampCells - invalidRampCells,
                invalidRampCells,
                candidates,
                tiles.Count,
                rejectedPatterns));

        bool TryVerticalSignature(
            int column,
            int row,
            out string signature,
            out byte baseLevel)
        {
            var bl = (column, row);
            var br = (column + 1, row);
            var tl = (column, row + 1);
            var tr = (column + 1, row + 1);
            var ttl = (column, row + 2);
            var ttr = (column + 1, row + 2);
            var ae = Math.Min(Level(bl), Level(ttl));
            var cf = Math.Min(Level(br), Level(ttr));
            if (Level(tl) != ae || Level(tr) != cf ||
                Flag(bl) != Flag(tl) || Flag(bl) != Flag(ttl) ||
                Flag(br) != Flag(tr) || Flag(br) != Flag(ttr) ||
                Flag(bl) == Flag(br))
            {
                signature = string.Empty;
                baseLevel = 0;
                return false;
            }
            baseLevel = Math.Min(ae, cf);
            signature = Signature(ttl, ttr, br, bl, baseLevel);
            return true;
        }

        bool TryHorizontalSignature(
            int column,
            int row,
            out string signature,
            out byte baseLevel)
        {
            var bl = (column, row);
            var br = (column + 1, row);
            var brr = (column + 2, row);
            var tl = (column, row + 1);
            var tr = (column + 1, row + 1);
            var trr = (column + 2, row + 1);
            var ae = Math.Min(Level(bl), Level(brr));
            var bf = Math.Min(Level(tl), Level(trr));
            if (Level(br) != ae || Level(tr) != bf ||
                Flag(bl) != Flag(br) || Flag(bl) != Flag(brr) ||
                Flag(tl) != Flag(tr) || Flag(tl) != Flag(trr) ||
                Flag(bl) == Flag(tl))
            {
                signature = string.Empty;
                baseLevel = 0;
                return false;
            }
            baseLevel = Math.Min(ae, bf);
            signature = Signature(tl, trr, brr, bl, baseLevel);
            return true;
        }

        string Signature(
            (int Column, int Row) first,
            (int Column, int Row) second,
            (int Column, int Row) third,
            (int Column, int Row) fourth,
            byte baseLevel) =>
            string.Create(4, new[] { first, second, third, fourth },
                (characters, points) =>
                {
                    for (var index = 0; index < points.Length; index++)
                    {
                        var point = points[index];
                        var delta = Level(point) - baseLevel;
                        characters[index] = Flag(point)
                            ? (char)('L' - delta * 4)
                            : (char)('A' + delta);
                    }
                });

        void AddTile(
            int column,
            int row,
            string signature,
            byte baseLevel,
            TerrainClassicRampAxis axis,
            int footprintColumns,
            int footprintRows)
        {
            tiles.Add(new TerrainClassicRampTile(
                column,
                row,
                signature,
                baseLevel,
                axis,
                ResolveUpperSurface(column, row, footprintColumns,
                    footprintRows)));
            for (var localRow = 0; localRow < footprintRows; localRow++)
            for (var localColumn = 0;
                 localColumn < footprintColumns;
                 localColumn++)
            {
                var tileColumn = column + localColumn;
                var tileRow = row + localRow;
                covered[tileRow * (terrain.Columns - 1) + tileColumn] = true;
                transitionUnderlayTiles[(tileColumn, tileRow)] =
                    SurfaceTile(tileColumn, tileRow);
            }
        }

        void AddSurfaceTile(int column, int row)
        {
            surfaceTiles[(column, row)] =
                SurfaceTile(column, row);
        }

        TerrainClassicRampSurfaceTile SurfaceTile(int column, int row) =>
            new(
                column,
                row,
                rampPointLevels[Index(column, row)],
                rampPointLevels[Index(column + 1, row)],
                rampPointLevels[Index(column + 1, row + 1)],
                rampPointLevels[Index(column, row + 1)]);

        ushort ResolveUpperSurface(
            int column,
            int row,
            int footprintColumns,
            int footprintRows)
        {
            var bestLevel = byte.MinValue;
            var bestSurface = terrain.Cell(column, row).SurfaceId;
            for (var y = 0; y <= footprintRows; y++)
            for (var x = 0; x <= footprintColumns; x++)
            {
                var cell = terrain.Cell(column + x, row + y);
                if (cell.CliffLevel <= bestLevel)
                    continue;
                bestLevel = cell.CliffLevel;
                bestSurface = cell.SurfaceId;
            }
            return bestSurface;
        }

        byte Level((int Column, int Row) point) =>
            terrain.Cell(point.Column, point.Row).CliffLevel;

        bool Flag((int Column, int Row) point) =>
            rampFlags[Index(point.Column, point.Row)];

        bool FullRampTile(int column, int row) =>
            rampFlags[Index(column, row)] &&
            rampFlags[Index(column + 1, row)] &&
            rampFlags[Index(column, row + 1)] &&
            rampFlags[Index(column + 1, row + 1)];

        int Index(int column, int row) => row * terrain.Columns + column;

        bool InBounds(int column, int row) =>
            (uint)column < (uint)terrain.Columns &&
            (uint)row < (uint)terrain.Rows;
    }

    public bool IsRampCellMapped(int column, int row) =>
        (uint)column < (uint)Columns &&
        (uint)row < (uint)Rows &&
        _mappedRampCells[row * Columns + column];

    public bool CoversClassicTile(int column, int row)
    {
        var tileColumns = Math.Max(0, Columns - 1);
        var tileRows = Math.Max(0, Rows - 1);
        return (uint)column < (uint)tileColumns &&
               (uint)row < (uint)tileRows &&
               _coveredClassicTiles[row * tileColumns + column];
    }

    private static (int X, int Y) Direction(
        TerrainRampDirection direction) => direction switch
    {
        TerrainRampDirection.PositiveX => (1, 0),
        TerrainRampDirection.NegativeX => (-1, 0),
        TerrainRampDirection.PositiveY => (0, 1),
        TerrainRampDirection.NegativeY => (0, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };
}

public enum TerrainClassicRampAxis : byte
{
    Horizontal,
    Vertical
}

public readonly record struct TerrainClassicRampTile(
    int Column,
    int Row,
    string Signature,
    byte BaseLevel,
    TerrainClassicRampAxis Axis,
    ushort UpperSurfaceId,
    byte CliffStyleId = 0);

public readonly record struct TerrainClassicRampSurfaceTile(
    int Column,
    int Row,
    float BottomLeftLevel,
    float BottomRightLevel,
    float TopRightLevel,
    float TopLeftLevel);

public readonly record struct TerrainClassicRampDiagnostics(
    int AuthoredRampCells,
    int MappedRampCells,
    int InvalidRampCells,
    int CandidateTransitions,
    int SelectedTransitions,
    int RejectedPatterns);
