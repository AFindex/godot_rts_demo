using RtsDemo.Simulation;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Presentation ownership map between the gameplay-cell ground mesh and
/// Warcraft's centre-sampled cliff tiles. A classic tile spans four quarters
/// of four neighbouring gameplay cells, so clipping whole gameplay cells
/// would remove too much ground while keeping them intact causes the plateau
/// surface to overhang the irregular cliff model.
/// </summary>
public sealed class TerrainClassicCliffSeamMap
{
    public const byte BottomLeft = 1;
    public const byte BottomRight = 2;
    public const byte TopRight = 4;
    public const byte TopLeft = 8;

    private readonly byte[] _groundQuadrantMasks;
    private readonly bool[] _coveredClassicTiles;
    private readonly TerrainClassicCliffTile[] _tiles;

    private TerrainClassicCliffSeamMap(
        int columns,
        int rows,
        byte[] groundQuadrantMasks,
        bool[] coveredClassicTiles,
        TerrainClassicCliffTile[] tiles)
    {
        Columns = columns;
        Rows = rows;
        _groundQuadrantMasks = groundQuadrantMasks;
        _coveredClassicTiles = coveredClassicTiles;
        _tiles = tiles;
        CoveredGroundQuadrants = groundQuadrantMasks.Sum(
            static mask => System.Numerics.BitOperations.PopCount(mask));
    }

    public int Columns { get; }
    public int Rows { get; }
    public int CoveredClassicTiles => _tiles.Length;
    public int CoveredGroundQuadrants { get; }
    public ReadOnlySpan<TerrainClassicCliffTile> Tiles => _tiles;

    public static TerrainClassicCliffSeamMap Build(
        TerrainMapSnapshot terrain,
        IEnumerable<TerrainClassicCliffTile> resolvedTiles)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(resolvedTiles);
        var tiles = resolvedTiles
            .OrderBy(static tile => tile.Row)
            .ThenBy(static tile => tile.Column)
            .ToArray();
        var masks = new byte[checked(terrain.Columns * terrain.Rows)];
        var tileColumns = Math.Max(0, terrain.Columns - 1);
        var tileRows = Math.Max(0, terrain.Rows - 1);
        var covered = new bool[checked(tileColumns * tileRows)];
        foreach (var tile in tiles)
        {
            if ((uint)tile.Column >= (uint)tileColumns ||
                (uint)tile.Row >= (uint)tileRows)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolvedTiles),
                    $"Classic cliff tile {tile.Column},{tile.Row} is outside the terrain.");
            }
            covered[tile.Row * tileColumns + tile.Column] = true;

            // The model footprint is centre-to-centre. Its facade and cap own
            // only the quadrants above the tile's base level. Base-level
            // ground remains the watertight floor behind open cliff geometry.
            AddRaisedMask(tile.Column, tile.Row, TopRight, tile.BaseLevel);
            AddRaisedMask(
                tile.Column + 1, tile.Row, TopLeft, tile.BaseLevel);
            AddRaisedMask(
                tile.Column, tile.Row + 1, BottomRight, tile.BaseLevel);
            AddRaisedMask(
                tile.Column + 1, tile.Row + 1, BottomLeft, tile.BaseLevel);
        }
        return new TerrainClassicCliffSeamMap(
            terrain.Columns, terrain.Rows, masks, covered, tiles);

        void AddMask(int column, int row, byte mask)
        {
            if ((uint)column < (uint)terrain.Columns &&
                (uint)row < (uint)terrain.Rows)
            {
                masks[row * terrain.Columns + column] |= mask;
            }
        }

        void AddRaisedMask(
            int column,
            int row,
            byte mask,
            byte baseLevel)
        {
            if ((uint)column < (uint)terrain.Columns &&
                (uint)row < (uint)terrain.Rows &&
                terrain.Cell(column, row).CliffLevel > baseLevel)
            {
                AddMask(column, row, mask);
            }
        }
    }

    public byte GroundQuadrantMask(int column, int row)
    {
        ValidateCell(column, row);
        return _groundQuadrantMasks[row * Columns + column];
    }

    public bool CoversClassicTile(int column, int row)
    {
        var tileColumns = Math.Max(0, Columns - 1);
        var tileRows = Math.Max(0, Rows - 1);
        return (uint)column < (uint)tileColumns &&
               (uint)row < (uint)tileRows &&
               _coveredClassicTiles[row * tileColumns + column];
    }

    /// <summary>
    /// Applies the classic cliff ground-tile priority to a runtime copy of an
    /// authored dual-grid map. The authored resource remains unchanged. A
    /// centre-sampled classic tile maps exactly to the visual control point at
    /// (column + 1, row + 1); expanding that ownership to a 3x3 point block
    /// pushes the cliff material a full cell beyond the model footprint.
    /// </summary>
    public TerrainVisualLayerMap BuildGroundTransitionMap(
        TerrainMapSnapshot terrain,
        TerrainVisualLayerMap source,
        Func<TerrainSurfaceDefinition, int?> resolveGroundLayer,
        out int changedPoints)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resolveGroundLayer);
        if (terrain.Columns != Columns || terrain.Rows != Rows ||
            source.CellColumns != Columns || source.CellRows != Rows)
        {
            throw new ArgumentException(
                "Classic cliff seam map dimensions do not match the visual grid.");
        }

        var layers = new byte[source.PointCount];
        var variations = new byte[source.PointCount];
        for (var row = 0; row < source.PointRows; row++)
        {
            for (var column = 0; column < source.PointColumns; column++)
            {
                var index = row * source.PointColumns + column;
                layers[index] = source.PointLayer(column, row);
                variations[index] = source.PointVariation(column, row);
            }
        }

        var changed = new HashSet<int>();
        foreach (var tile in _tiles)
        {
            var layer = resolveGroundLayer(
                terrain.Surface(tile.UpperSurfaceId));
            if (layer is not int groundLayer ||
                groundLayer < 0 ||
                groundLayer > TerrainVisualLayerMap.MaximumLayer)
                continue;
            var pointColumn = tile.Column + 1;
            var pointRow = tile.Row + 1;
            var index = pointRow * source.PointColumns + pointColumn;
            var replacement = (byte)groundLayer;
            if (layers[index] == replacement) continue;
            layers[index] = replacement;
            changed.Add(index);
        }
        changedPoints = changed.Count;
        return changedPoints == 0
            ? source
            : TerrainVisualLayerMap.FromPoints(terrain, layers, variations);
    }

    private void ValidateCell(int column, int row)
    {
        if ((uint)column >= (uint)Columns || (uint)row >= (uint)Rows)
            throw new ArgumentOutOfRangeException(nameof(column));
    }
}
