using System.Text;
using RtsDemo.Simulation;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Presentation-only dual grid. Gameplay cells remain authoritative while
/// visual tilepoints form a (columns + 1) x (rows + 1) control raster. Each
/// rendered cell derives War3 atlas masks from its four surrounding points.
/// </summary>
public sealed class TerrainVisualLayerMap
{
    public const int CurrentFormatVersion = 1;
    public const byte MaximumLayer = 3;
    public const byte MaximumVariation = 16;
    private const int Magic = 0x4C565452;

    private readonly byte[] _pointLayers;
    private readonly byte[] _pointVariations;
    private readonly byte[] _canonicalBytes;

    private TerrainVisualLayerMap(
        TerrainMapSnapshot source,
        byte[] pointLayers,
        byte[] pointVariations)
    {
        SourceTerrainHash = source.StableHashText;
        PointColumns = source.Columns + 1;
        PointRows = source.Rows + 1;
        _pointLayers = pointLayers;
        _pointVariations = pointVariations;
        _canonicalBytes = BuildCanonicalBytes();
        StableHash = ComputeStableHash(_canonicalBytes);
    }

    public int FormatVersion => CurrentFormatVersion;
    public string SourceTerrainHash { get; }
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");
    public int PointColumns { get; }
    public int PointRows { get; }
    public int PointCount => _pointLayers.Length;
    public int CellColumns => PointColumns - 1;
    public int CellRows => PointRows - 1;
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;

    public static TerrainVisualLayerMap FromTerrain(
        TerrainMapSnapshot terrain,
        IRts3DTerrainDualGridMaterialProvider provider)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(provider);
        var pointColumns = terrain.Columns + 1;
        var pointRows = terrain.Rows + 1;
        var layers = new byte[checked(pointColumns * pointRows)];
        var variations = new byte[layers.Length];
        var fallbackLayer = ResolveFallbackLayer(terrain, provider);
        for (var row = 0; row < pointRows; row++)
        {
            for (var column = 0; column < pointColumns; column++)
            {
                var index = row * pointColumns + column;
                layers[index] = ResolvePointLayer(
                    terrain, provider, column, row, fallbackLayer);
                variations[index] = DeterministicVariation(column, row);
            }
        }
        return FromPoints(terrain, layers, variations);
    }

    /// <summary>
    /// Creates an authored visual grid. W3E import and the future terrain
    /// editor use this path instead of inferring tilepoints from gameplay cells.
    /// </summary>
    public static TerrainVisualLayerMap FromPoints(
        TerrainMapSnapshot source,
        ReadOnlySpan<byte> pointLayers,
        ReadOnlySpan<byte> pointVariations)
    {
        ArgumentNullException.ThrowIfNull(source);
        var expected = checked((source.Columns + 1) * (source.Rows + 1));
        if (pointLayers.Length != expected || pointVariations.Length != expected)
        {
            throw new ArgumentException(
                $"Visual grid requires {expected} points, got " +
                $"{pointLayers.Length}/{pointVariations.Length}.");
        }
        if (pointLayers.ContainsAnyExceptInRange((byte)0, MaximumLayer))
            throw new ArgumentOutOfRangeException(nameof(pointLayers));
        if (pointVariations.ContainsAnyExceptInRange(
                (byte)0, MaximumVariation))
            throw new ArgumentOutOfRangeException(nameof(pointVariations));
        return new TerrainVisualLayerMap(
            source, pointLayers.ToArray(), pointVariations.ToArray());
    }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> data,
        TerrainMapSnapshot source,
        out TerrainVisualLayerMap? visualMap,
        out TerrainVisualLayerValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadInt32() != Magic)
            {
                return Failure(
                    TerrainVisualLayerErrorCode.InvalidHeader,
                    "Terrain visual layer magic header is invalid.",
                    out visualMap, out validation);
            }
            var version = reader.ReadInt32();
            if (version != CurrentFormatVersion)
            {
                return Failure(
                    TerrainVisualLayerErrorCode.UnsupportedFormatVersion,
                    $"Expected visual layer format {CurrentFormatVersion}, got {version}.",
                    out visualMap, out validation);
            }
            var sourceHash = reader.ReadString();
            var columns = reader.ReadInt32();
            var rows = reader.ReadInt32();
            var count = reader.ReadInt32();
            var expected = checked((source.Columns + 1) * (source.Rows + 1));
            if (columns != source.Columns + 1 || rows != source.Rows + 1 ||
                count != expected)
            {
                return Failure(
                    TerrainVisualLayerErrorCode.InvalidDimensions,
                    "Visual layer dimensions do not match the source terrain.",
                    out visualMap, out validation);
            }
            if (!string.Equals(
                    sourceHash, source.StableHashText,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    TerrainVisualLayerErrorCode.SourceTerrainMismatch,
                    "Visual layer source hash does not match the terrain.",
                    out visualMap, out validation);
            }
            var layers = reader.ReadBytes(count);
            var variations = reader.ReadBytes(count);
            if (layers.Length != count || variations.Length != count)
                throw new EndOfStreamException("Visual point arrays are truncated.");
            if (stream.Position != stream.Length)
            {
                return Failure(
                    TerrainVisualLayerErrorCode.TrailingPayload,
                    "Visual layer payload contains trailing bytes.",
                    out visualMap, out validation);
            }
            if (((ReadOnlySpan<byte>)layers).ContainsAnyExceptInRange(
                    0, MaximumLayer))
            {
                return Failure(
                    TerrainVisualLayerErrorCode.InvalidLayer,
                    $"Visual layers must be in the range 0..{MaximumLayer}.",
                    out visualMap, out validation);
            }
            if (((ReadOnlySpan<byte>)variations).ContainsAnyExceptInRange(
                    0, MaximumVariation))
            {
                return Failure(
                    TerrainVisualLayerErrorCode.InvalidVariation,
                    $"Visual variations must be in the range 0..{MaximumVariation}.",
                    out visualMap, out validation);
            }
            visualMap = new TerrainVisualLayerMap(source, layers, variations);
            validation = TerrainVisualLayerValidationResult.Valid;
            return true;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or
                OverflowException or ArgumentException)
        {
            return Failure(
                TerrainVisualLayerErrorCode.InvalidPayload,
                $"Visual layer payload is invalid: {exception.Message}",
                out visualMap, out validation);
        }
    }

    public byte PointLayer(int column, int row)
    {
        ValidatePoint(column, row);
        return _pointLayers[row * PointColumns + column];
    }

    public byte PointVariation(int column, int row)
    {
        ValidatePoint(column, row);
        return _pointVariations[row * PointColumns + column];
    }

    public TerrainVisualCell Cell(int column, int row)
    {
        if ((uint)column >= (uint)CellColumns ||
            (uint)row >= (uint)CellRows)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        var bottomLeft = PointLayer(column, row);
        var bottomRight = PointLayer(column + 1, row);
        var topRight = PointLayer(column + 1, row + 1);
        var topLeft = PointLayer(column, row + 1);
        Span<byte> corners =
            [bottomLeft, bottomRight, topRight, topLeft];
        var baseLayer = corners[0];
        for (var index = 1; index < corners.Length; index++)
            baseLayer = Math.Min(baseLayer, corners[index]);

        uint packedMasks = (uint)(15 << (baseLayer * 4));
        for (byte layer = 0; layer < 4; layer++)
        {
            if (layer == baseLayer)
                continue;
            byte mask = 0;
            if (bottomRight == layer) mask |= 1;
            if (bottomLeft == layer) mask |= 2;
            if (topRight == layer) mask |= 4;
            if (topLeft == layer) mask |= 8;
            packedMasks |= (uint)mask << (layer * 4);
        }
        return new TerrainVisualCell(
            (ushort)packedMasks,
            PointVariation(column, row));
    }

    private static byte ResolveFallbackLayer(
        TerrainMapSnapshot terrain,
        IRts3DTerrainDualGridMaterialProvider provider)
    {
        var fallback = int.MaxValue;
        foreach (var surface in terrain.Surfaces)
        {
            if (provider.TryGetDualGridLayer(surface, out var layer) &&
                (uint)layer < 4u)
            {
                fallback = Math.Min(fallback, layer);
            }
        }
        return fallback == int.MaxValue ? (byte)0 : (byte)fallback;
    }

    private static byte ResolvePointLayer(
        TerrainMapSnapshot terrain,
        IRts3DTerrainDualGridMaterialProvider provider,
        int pointColumn,
        int pointRow,
        byte fallbackLayer)
    {
        Span<int> counts = stackalloc int[4];
        var maximumHeight = float.NegativeInfinity;
        var heightTolerance = MathF.Max(
            0.001f, terrain.CliffLevelHeight * 0.16f);

        // First find the highest connected ground corner. At a cliff seam the
        // high-side visual material owns the shared tilepoint; the cliff face
        // remains a separate batch and lower terrain cannot take over its rim.
        for (var offsetRow = -1; offsetRow <= 0; offsetRow++)
        {
            for (var offsetColumn = -1; offsetColumn <= 0; offsetColumn++)
            {
                var column = pointColumn + offsetColumn;
                var row = pointRow + offsetRow;
                if ((uint)column >= (uint)terrain.Columns ||
                    (uint)row >= (uint)terrain.Rows)
                {
                    continue;
                }
                var cell = terrain.Cell(column, row);
                if (!provider.TryGetDualGridLayer(
                        terrain.Surface(cell.SurfaceId), out var layer) ||
                    (uint)layer >= 4u)
                {
                    continue;
                }
                maximumHeight = MathF.Max(maximumHeight,
                    terrain.CellCornerHeight(
                        column, row,
                        pointColumn > column,
                        pointRow > row));
            }
        }

        if (!float.IsFinite(maximumHeight))
            return fallbackLayer;

        for (var offsetRow = -1; offsetRow <= 0; offsetRow++)
        {
            for (var offsetColumn = -1; offsetColumn <= 0; offsetColumn++)
            {
                var column = pointColumn + offsetColumn;
                var row = pointRow + offsetRow;
                if ((uint)column >= (uint)terrain.Columns ||
                    (uint)row >= (uint)terrain.Rows)
                {
                    continue;
                }
                var cell = terrain.Cell(column, row);
                if (!provider.TryGetDualGridLayer(
                        terrain.Surface(cell.SurfaceId), out var layer) ||
                    (uint)layer >= 4u)
                {
                    continue;
                }
                var height = terrain.CellCornerHeight(
                    column, row,
                    pointColumn > column,
                    pointRow > row);
                if (maximumHeight - height <= heightTolerance)
                    counts[layer]++;
            }
        }

        var preferredColumn = Math.Clamp(pointColumn, 0, terrain.Columns - 1);
        var preferredRow = Math.Clamp(pointRow, 0, terrain.Rows - 1);
        var preferredSurface = terrain.Surface(
            terrain.Cell(preferredColumn, preferredRow).SurfaceId);
        var preferredLayer = -1;
        if (!provider.TryGetDualGridLayer(
                preferredSurface, out preferredLayer))
        {
            preferredLayer = -1;
        }
        var selected = fallbackLayer;
        var selectedCount = -1;
        for (byte layer = 0; layer < 4; layer++)
        {
            if (counts[layer] > selectedCount ||
                counts[layer] == selectedCount && layer == preferredLayer)
            {
                selected = layer;
                selectedCount = counts[layer];
            }
        }
        return selected;
    }

    private static byte DeterministicVariation(int column, int row)
    {
        unchecked
        {
            var hash = (uint)(column * 73856093) ^ (uint)(row * 19349663);
            hash ^= hash >> 13;
            hash *= 1274126177u;
            return WeightedVariation(hash % 570u);
        }
    }

    // Mirrors the classic extended-tile distribution used by current HiveWE:
    // 0/16/0 have weights 85 each, followed by the authored 10/4/1 and
    // 85/10/4/1 groups. Variation 16 intentionally selects transition tile 15.
    private static byte WeightedVariation(uint roll)
    {
        ReadOnlySpan<(byte Variation, ushort Weight)> chances =
        [
            (0, 85), (16, 85), (0, 85),
            (1, 10), (2, 4), (3, 1),
            (4, 85), (5, 10), (6, 4), (7, 1),
            (8, 85), (9, 10), (10, 4), (11, 1),
            (12, 85), (13, 10), (14, 4), (15, 1)
        ];
        foreach (var (variation, weight) in chances)
        {
            if (roll < weight)
                return variation;
            roll -= weight;
        }
        return 0;
    }

    private void ValidatePoint(int column, int row)
    {
        if ((uint)column >= (uint)PointColumns ||
            (uint)row >= (uint)PointRows)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }
    }

    private byte[] BuildCanonicalBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(CurrentFormatVersion);
        writer.Write(SourceTerrainHash);
        writer.Write(PointColumns);
        writer.Write(PointRows);
        writer.Write(_pointLayers.Length);
        writer.Write(_pointLayers);
        writer.Write(_pointVariations);
        writer.Flush();
        return stream.ToArray();
    }

    private static ulong ComputeStableHash(ReadOnlySpan<byte> bytes)
    {
        const ulong offset = 14695981039346656037ul;
        const ulong prime = 1099511628211ul;
        var hash = offset;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= prime;
        }
        return hash;
    }

    private static bool Failure(
        TerrainVisualLayerErrorCode code,
        string message,
        out TerrainVisualLayerMap? visualMap,
        out TerrainVisualLayerValidationResult validation)
    {
        visualMap = null;
        validation = new TerrainVisualLayerValidationResult(
            [new TerrainVisualLayerValidationIssue(code, -1, message)]);
        return false;
    }
}

internal static class TerrainVisualLayerSpanExtensions
{
    public static bool ContainsAnyExceptInRange(
        this ReadOnlySpan<byte> values,
        byte minimum,
        byte maximum)
    {
        foreach (var value in values)
        {
            if (value < minimum || value > maximum)
                return true;
        }
        return false;
    }
}

public readonly record struct TerrainVisualCell(
    ushort PackedLayerMasks,
    byte BaseVariation)
{
    public byte LayerMask(int layer)
    {
        if ((uint)layer >= 4u)
            throw new ArgumentOutOfRangeException(nameof(layer));
        return (byte)((PackedLayerMasks >> (layer * 4)) & 15);
    }
}

public enum TerrainVisualLayerErrorCode
{
    None,
    InvalidHeader,
    UnsupportedFormatVersion,
    InvalidPayload,
    TrailingPayload,
    MissingResourceAsset,
    DeclaredHashMismatch,
    SourceTerrainMismatch,
    InvalidDimensions,
    InvalidPointCount,
    InvalidLayer,
    InvalidVariation
}

public readonly record struct TerrainVisualLayerValidationIssue(
    TerrainVisualLayerErrorCode Code,
    int ElementIndex,
    string Message);

public sealed class TerrainVisualLayerValidationResult(
    TerrainVisualLayerValidationIssue[] issues)
{
    public static TerrainVisualLayerValidationResult Valid { get; } = new([]);
    public TerrainVisualLayerValidationIssue[] Issues { get; } = issues;
    public bool IsValid => Issues.Length == 0;
    public TerrainVisualLayerErrorCode FirstError => IsValid
        ? TerrainVisualLayerErrorCode.None
        : Issues[0].Code;
}
