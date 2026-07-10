using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class ClearanceBakeLayerSnapshot
{
    private readonly byte[] _walkableBits;
    private readonly int[] _componentIds;
    private readonly NavigationComponentSummary[] _components;

    internal ClearanceBakeLayerSnapshot(
        MovementClass movementClass,
        float navigationRadius,
        byte[] walkableBits,
        int[] componentIds,
        NavigationComponentSummary[] components)
    {
        MovementClass = movementClass;
        NavigationRadius = navigationRadius;
        _walkableBits = walkableBits;
        _componentIds = componentIds;
        _components = components;
    }

    public MovementClass MovementClass { get; }
    public float NavigationRadius { get; }
    public int NodeCount => _componentIds.Length;
    public int ComponentCount => _components.Length;
    public ReadOnlySpan<NavigationComponentSummary> Components => _components;

    public bool IsWalkable(int node) =>
        (_walkableBits[node >> 3] & (1 << (node & 7))) != 0;

    public int ComponentAt(int node) => _componentIds[node];

    internal ReadOnlySpan<byte> WalkableBits => _walkableBits;
    internal ReadOnlySpan<int> ComponentIds => _componentIds;
}

public readonly record struct ClearanceBakeChunk(
    int Id,
    int Column,
    int Row,
    int MinimumCellColumn,
    int MinimumCellRow,
    int MaximumCellColumnExclusive,
    int MaximumCellRowExclusive,
    SimRect WorldBounds);

/// <summary>
/// Versioned, engine-independent static clearance bake. Dynamic revisions may
/// safely fall back to NavigationConnectivityAnalyzer until chunk updates exist.
/// </summary>
public sealed class ClearanceBakeSnapshot
{
    public const int CurrentFormatVersion = 1;
    public const int DefaultChunkSizeCells = 16;

    private readonly ClearanceBakeLayerSnapshot[] _layers;
    private readonly byte[] _canonicalBytes;

    internal ClearanceBakeSnapshot(
        int formatVersion,
        ulong sourceNavigationHash,
        SimRect worldBounds,
        float cellSize,
        int columns,
        int rows,
        int chunkSizeCells,
        ClearanceBakeLayerSnapshot[] layers,
        byte[] canonicalBytes)
    {
        FormatVersion = formatVersion;
        SourceNavigationHash = sourceNavigationHash;
        WorldBounds = worldBounds;
        CellSize = cellSize;
        Columns = columns;
        Rows = rows;
        ChunkSizeCells = chunkSizeCells;
        _layers = layers;
        _canonicalBytes = canonicalBytes;
        StableHash = ClearanceBakeCodec.ComputeStableHash(canonicalBytes);
    }

    public int FormatVersion { get; }
    public ulong SourceNavigationHash { get; }
    public string SourceNavigationHashText => SourceNavigationHash.ToString("X16");
    public SimRect WorldBounds { get; }
    public float CellSize { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int NodeCount => Columns * Rows;
    public int ChunkSizeCells { get; }
    public int ChunkColumns => (Columns + ChunkSizeCells - 1) / ChunkSizeCells;
    public int ChunkRows => (Rows + ChunkSizeCells - 1) / ChunkSizeCells;
    public int ChunkCount => ChunkColumns * ChunkRows;
    public ReadOnlySpan<ClearanceBakeLayerSnapshot> Layers => _layers;
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    public ClearanceBakeLayerSnapshot Layer(MovementClass movementClass) =>
        _layers[(int)movementClass];

    public ClearanceBakeChunk Chunk(int id)
    {
        if ((uint)id >= (uint)ChunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var column = id % ChunkColumns;
        var row = id / ChunkColumns;
        var minimumCellColumn = column * ChunkSizeCells;
        var minimumCellRow = row * ChunkSizeCells;
        var maximumCellColumn = Math.Min(
            Columns, minimumCellColumn + ChunkSizeCells);
        var maximumCellRow = Math.Min(
            Rows, minimumCellRow + ChunkSizeCells);
        var minimum = WorldBounds.Min + new Vector2(
            minimumCellColumn * CellSize,
            minimumCellRow * CellSize);
        var maximum = Vector2.Min(
            WorldBounds.Max,
            WorldBounds.Min + new Vector2(
                maximumCellColumn * CellSize,
                maximumCellRow * CellSize));
        return new ClearanceBakeChunk(
            id,
            column,
            row,
            minimumCellColumn,
            minimumCellRow,
            maximumCellColumn,
            maximumCellRow,
            new SimRect(minimum, maximum));
    }

    public int[] FindIntersectingChunks(SimRect worldArea)
    {
        if (!IsFinite(worldArea.Min) || !IsFinite(worldArea.Max) ||
            worldArea.Width < 0f || worldArea.Height < 0f ||
            !WorldBounds.Intersects(worldArea))
        {
            return [];
        }

        var clippedMinimum = Vector2.Max(WorldBounds.Min, worldArea.Min);
        var clippedMaximum = Vector2.Min(WorldBounds.Max, worldArea.Max);
        var minimumCellColumn = CellColumn(clippedMinimum.X);
        var minimumCellRow = CellRow(clippedMinimum.Y);
        var maximumCellColumn = CellColumn(clippedMaximum.X);
        var maximumCellRow = CellRow(clippedMaximum.Y);
        var minimumChunkColumn = minimumCellColumn / ChunkSizeCells;
        var maximumChunkColumn = maximumCellColumn / ChunkSizeCells;
        var minimumChunkRow = minimumCellRow / ChunkSizeCells;
        var maximumChunkRow = maximumCellRow / ChunkSizeCells;
        var result = new int[
            (maximumChunkColumn - minimumChunkColumn + 1) *
            (maximumChunkRow - minimumChunkRow + 1)];
        var write = 0;
        for (var row = minimumChunkRow; row <= maximumChunkRow; row++)
        {
            for (var column = minimumChunkColumn;
                 column <= maximumChunkColumn;
                 column++)
            {
                result[write++] = row * ChunkColumns + column;
            }
        }

        return result;
    }

    public bool IsCompatible(
        StaticWorld world,
        float cellSize,
        float navigationRadius)
    {
        if (world.NavigationRevision != 0 || WorldBounds != world.Bounds ||
            MathF.Abs(CellSize - cellSize) > 0.0001f)
        {
            return false;
        }

        var clearance = MovementClearance.FromPhysicalRadius(navigationRadius);
        return MathF.Abs(
            Layer(clearance.Class).NavigationRadius - navigationRadius) <= 0.0001f;
    }

    public NavigationConnectivitySnapshot CreateConnectivitySnapshot(
        MovementClass movementClass,
        int worldRevision = 0)
    {
        var layer = Layer(movementClass);
        var walkable = new bool[NodeCount];
        var componentIds = new int[NodeCount];
        for (var node = 0; node < NodeCount; node++)
        {
            walkable[node] = layer.IsWalkable(node);
            componentIds[node] = layer.ComponentAt(node);
        }

        return new NavigationConnectivitySnapshot(
            WorldBounds,
            CellSize,
            Columns,
            Rows,
            layer.NavigationRadius,
            worldRevision,
            NavigationConnectivitySource.StaticBake,
            walkable,
            componentIds,
            layer.Components.ToArray());
    }

    public static ClearanceBakeSnapshot Build(
        NavigationMapSnapshot navigation,
        float cellSize = 16f,
        int chunkSizeCells = DefaultChunkSizeCells)
    {
        if (!float.IsFinite(cellSize) || cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        if (chunkSizeCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeCells));
        }

        var analyzer = new NavigationConnectivityAnalyzer(
            navigation.CreateWorld(), cellSize);
        var layers = new ClearanceBakeLayerSnapshot[3];
        for (var classIndex = 0; classIndex < layers.Length; classIndex++)
        {
            var movementClass = (MovementClass)classIndex;
            var radius = MovementClearance.ForClass(
                movementClass).NavigationRadius;
            layers[classIndex] = FromConnectivity(
                movementClass, analyzer.Analyze(radius));
        }

        var columns = Math.Max(
            1, (int)MathF.Ceiling(navigation.WorldBounds.Width / cellSize));
        var rows = Math.Max(
            1, (int)MathF.Ceiling(navigation.WorldBounds.Height / cellSize));
        var canonical = ClearanceBakeCodec.Serialize(
            CurrentFormatVersion,
            navigation.StableHash,
            navigation.WorldBounds,
            cellSize,
            columns,
            rows,
            chunkSizeCells,
            layers);
        return new ClearanceBakeSnapshot(
            CurrentFormatVersion,
            navigation.StableHash,
            navigation.WorldBounds,
            cellSize,
            columns,
            rows,
            chunkSizeCells,
            layers,
            canonical);
    }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> data,
        out ClearanceBakeSnapshot? snapshot,
        out ClearanceBakeValidationResult validation) =>
        ClearanceBakeCodec.TryDeserialize(data, out snapshot, out validation);

    private static ClearanceBakeLayerSnapshot FromConnectivity(
        MovementClass movementClass,
        NavigationConnectivitySnapshot connectivity)
    {
        var bits = new byte[(connectivity.NodeCount + 7) / 8];
        var componentIds = new int[connectivity.NodeCount];
        for (var node = 0; node < connectivity.NodeCount; node++)
        {
            if (connectivity.IsWalkable(node))
            {
                bits[node >> 3] |= (byte)(1 << (node & 7));
            }

            componentIds[node] = connectivity.ComponentAt(node);
        }

        return new ClearanceBakeLayerSnapshot(
            movementClass,
            connectivity.NavigationRadius,
            bits,
            componentIds,
            connectivity.Components.ToArray());
    }

    private int CellColumn(float worldX) => Math.Clamp(
        (int)MathF.Floor((worldX - WorldBounds.Min.X) / CellSize),
        0,
        Columns - 1);

    private int CellRow(float worldY) => Math.Clamp(
        (int)MathF.Floor((worldY - WorldBounds.Min.Y) / CellSize),
        0,
        Rows - 1);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
