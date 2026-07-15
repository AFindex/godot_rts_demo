using RtsDemo.Simulation;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Presentation-only Warcraft cliff layout. Gameplay cells are interpreted as
/// height samples at their centres, so every 2x2 cell neighbourhood becomes a
/// classic 128x128 cliff-model tile without changing authoritative heights.
/// </summary>
public sealed class TerrainCliffMeshLayout
{
    private readonly TerrainClassicCliffTile[] _tiles;

    private TerrainCliffMeshLayout(
        TerrainClassicCliffTile[] tiles,
        TerrainCliffMeshDiagnostics diagnostics)
    {
        _tiles = tiles;
        Diagnostics = diagnostics;
        StableHash = ComputeStableHash(tiles);
    }

    public ReadOnlySpan<TerrainClassicCliffTile> Tiles => _tiles;
    public TerrainCliffMeshDiagnostics Diagnostics { get; }
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    public static TerrainCliffMeshLayout Build(
        TerrainMapSnapshot terrain,
        Func<string, int> variationCount)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(variationCount);
        var tiles = new List<TerrainClassicCliffTile>();
        var candidates = 0;
        var flat = 0;
        var rampFallback = 0;
        var unsupportedHeight = 0;
        var missingAsset = 0;

        for (var row = 0; row + 1 < terrain.Rows; row++)
        {
            for (var column = 0; column + 1 < terrain.Columns; column++)
            {
                var bottomLeft = terrain.Cell(column, row);
                var bottomRight = terrain.Cell(column + 1, row);
                var topRight = terrain.Cell(column + 1, row + 1);
                var topLeft = terrain.Cell(column, row + 1);
                Span<TerrainCell> cells =
                    [bottomLeft, bottomRight, topRight, topLeft];
                var minimum = cells[0].CliffLevel;
                var maximum = cells[0].CliffLevel;
                for (var index = 1; index < cells.Length; index++)
                {
                    minimum = Math.Min(minimum, cells[index].CliffLevel);
                    maximum = Math.Max(maximum, cells[index].CliffLevel);
                }
                if (minimum == maximum)
                {
                    flat++;
                    continue;
                }
                candidates++;
                if (((ReadOnlySpan<TerrainCell>)cells).ContainsAnyRamp())
                {
                    rampFallback++;
                    continue;
                }
                if (maximum - minimum > 2)
                {
                    unsupportedHeight++;
                    continue;
                }

                var signature = string.Create(4, (cells.ToArray(), minimum),
                    static (characters, state) =>
                    {
                        // Warcraft filename order is TL, TR, BR, BL.
                        characters[0] = HeightCode(state.Item1[3], state.minimum);
                        characters[1] = HeightCode(state.Item1[2], state.minimum);
                        characters[2] = HeightCode(state.Item1[1], state.minimum);
                        characters[3] = HeightCode(state.Item1[0], state.minimum);
                    });
                var variations = variationCount(signature);
                if (variations <= 0)
                {
                    missingAsset++;
                    continue;
                }
                var variation = DeterministicVariation(
                    column, row, signature, variations);
                tiles.Add(new TerrainClassicCliffTile(
                    column,
                    row,
                    signature,
                    variation,
                    minimum,
                    maximum,
                    ResolveUpperSurface(cells, maximum)));
            }
        }

        return new TerrainCliffMeshLayout(
            tiles.ToArray(),
            new TerrainCliffMeshDiagnostics(
                candidates,
                tiles.Count,
                flat,
                rampFallback,
                unsupportedHeight,
                missingAsset));
    }

    private static char HeightCode(TerrainCell cell, byte minimum) =>
        (char)('A' + cell.CliffLevel - minimum);

    private static int DeterministicVariation(
        int column,
        int row,
        string signature,
        int count)
    {
        unchecked
        {
            var hash = (uint)(column * 73856093) ^
                       (uint)(row * 19349663);
            foreach (var character in signature)
            {
                hash ^= character;
                hash *= 16777619u;
            }
            hash ^= hash >> 13;
            return (int)(hash % (uint)count);
        }
    }

    private static ushort ResolveUpperSurface(
        ReadOnlySpan<TerrainCell> cells,
        byte maximum)
    {
        Span<ushort> surfaces = stackalloc ushort[4];
        Span<byte> counts = stackalloc byte[4];
        Span<byte> firstCorners = stackalloc byte[4];
        var surfaceCount = 0;
        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index].CliffLevel != maximum)
                continue;
            var surface = cells[index].SurfaceId;
            var slot = -1;
            for (var candidate = 0; candidate < surfaceCount; candidate++)
            {
                if (surfaces[candidate] == surface)
                {
                    slot = candidate;
                    break;
                }
            }
            if (slot < 0)
            {
                slot = surfaceCount++;
                surfaces[slot] = surface;
                firstCorners[slot] = (byte)index;
            }
            counts[slot]++;
        }
        var selected = 0;
        for (var slot = 1; slot < surfaceCount; slot++)
        {
            if (counts[slot] > counts[selected] ||
                counts[slot] == counts[selected] &&
                firstCorners[slot] < firstCorners[selected])
            {
                selected = slot;
            }
        }
        return surfaces[selected];
    }

    private static ulong ComputeStableHash(
        ReadOnlySpan<TerrainClassicCliffTile> tiles)
    {
        const ulong offset = 14695981039346656037ul;
        const ulong prime = 1099511628211ul;
        var hash = offset;
        foreach (var tile in tiles)
        {
            Add(tile.Column);
            Add(tile.Row);
            foreach (var character in tile.Signature) Add(character);
            Add(tile.Variation);
            Add(tile.BaseLevel);
            Add(tile.MaximumLevel);
            Add(tile.UpperSurfaceId);
        }
        return hash;

        void Add(int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= prime;
            }
        }
    }
}

internal static class TerrainCliffMeshCellSpanExtensions
{
    public static bool ContainsAnyRamp(this ReadOnlySpan<TerrainCell> cells)
    {
        foreach (var cell in cells)
        {
            if (cell.IsRamp) return true;
        }
        return false;
    }
}

public readonly record struct TerrainClassicCliffTile(
    int Column,
    int Row,
    string Signature,
    int Variation,
    byte BaseLevel,
    byte MaximumLevel,
    ushort UpperSurfaceId);

public readonly record struct TerrainCliffMeshDiagnostics(
    int CandidateTiles,
    int SelectedTiles,
    int FlatTiles,
    int RampFallbackTiles,
    int UnsupportedHeightTiles,
    int MissingAssetTiles);
