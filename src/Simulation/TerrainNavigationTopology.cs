using System.Numerics;

namespace RtsDemo.Simulation;

[Flags]
public enum TerrainTopologyMovementMask : byte
{
    None = 0,
    Small = 1 << 0,
    Medium = 1 << 1,
    Large = 1 << 2,
    All = Small | Medium | Large
}

public readonly record struct TerrainNavigationRegion(
    int Id,
    byte CliffLevel,
    int SampleCount,
    SimRect Bounds);

public readonly record struct TerrainRampPortal(
    int Id,
    int FirstColumn,
    int FirstRow,
    int LastColumn,
    int LastRow,
    byte LowerCliffLevel,
    TerrainRampDirection Direction,
    Vector2 LowerMouth,
    Vector2 UpperMouth,
    float Width,
    float ApproachDistance,
    TerrainTopologyMovementMask MovementMask)
{
    public float Length => Vector2.Distance(LowerMouth, UpperMouth);

    public bool Supports(MovementClass movementClass) =>
        (MovementMask & TerrainNavigationTopologySnapshot.MaskFor(
            movementClass)) != 0;
}

public readonly record struct TerrainRampRegionConnection(
    int RampId,
    int LowerRegionId,
    int UpperRegionId)
{
    public bool IsTraversable => LowerRegionId >= 0 && UpperRegionId >= 0;
}

/// <summary>
/// Clearance-specific regions derived from the static bake. Ramp samples are
/// deliberately excluded, so a ramp is the explicit edge between two regions
/// instead of silently merging their cliff layers into one component.
/// </summary>
public sealed class TerrainNavigationLayerSnapshot
{
    private readonly int[] _sampleRegionIds;
    private readonly TerrainNavigationRegion[] _regions;
    private readonly TerrainRampRegionConnection[] _rampConnections;

    internal TerrainNavigationLayerSnapshot(
        MovementClass movementClass,
        float navigationRadius,
        SimRect worldBounds,
        float sampleCellSize,
        int columns,
        int rows,
        int[] sampleRegionIds,
        TerrainNavigationRegion[] regions,
        TerrainRampRegionConnection[] rampConnections)
    {
        MovementClass = movementClass;
        NavigationRadius = navigationRadius;
        WorldBounds = worldBounds;
        SampleCellSize = sampleCellSize;
        Columns = columns;
        Rows = rows;
        _sampleRegionIds = sampleRegionIds;
        _regions = regions;
        _rampConnections = rampConnections;
    }

    public MovementClass MovementClass { get; }
    public float NavigationRadius { get; }
    public SimRect WorldBounds { get; }
    public float SampleCellSize { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int RegionCount => _regions.Length;
    public ReadOnlySpan<TerrainNavigationRegion> Regions => _regions;
    public ReadOnlySpan<TerrainRampRegionConnection> RampConnections =>
        _rampConnections;

    public int RegionAt(Vector2 position)
    {
        if (!WorldBounds.Contains(position)) return -1;
        var column = Math.Clamp(
            (int)MathF.Floor(
                (position.X - WorldBounds.Min.X) / SampleCellSize),
            0,
            Columns - 1);
        var row = Math.Clamp(
            (int)MathF.Floor(
                (position.Y - WorldBounds.Min.Y) / SampleCellSize),
            0,
            Rows - 1);
        return _sampleRegionIds[row * Columns + column];
    }

    public TerrainRampRegionConnection Connection(int rampId)
    {
        if ((uint)rampId >= (uint)_rampConnections.Length)
            throw new ArgumentOutOfRangeException(nameof(rampId));
        return _rampConnections[rampId];
    }

    internal ReadOnlySpan<int> SampleRegionIds => _sampleRegionIds;
}

/// <summary>
/// Immutable high-level terrain topology. It is derived data: authoritative
/// movement still comes from TerrainMapSnapshot and ClearanceBakeSnapshot.
/// </summary>
public sealed class TerrainNavigationTopologySnapshot
{
    private readonly TerrainNavigationLayerSnapshot[] _layers;
    private readonly TerrainRampPortal[] _ramps;

    internal TerrainNavigationTopologySnapshot(
        ulong sourceTerrainHash,
        ulong sourceClearanceHash,
        int sourceWorldRevision,
        TerrainNavigationLayerSnapshot[] layers,
        TerrainRampPortal[] ramps)
    {
        SourceTerrainHash = sourceTerrainHash;
        SourceClearanceHash = sourceClearanceHash;
        SourceWorldRevision = sourceWorldRevision;
        _layers = layers;
        _ramps = ramps;
        StableHash = ComputeStableHash();
    }

    public ulong SourceTerrainHash { get; }
    public string SourceTerrainHashText => SourceTerrainHash.ToString("X16");
    public ulong SourceClearanceHash { get; }
    public string SourceClearanceHashText => SourceClearanceHash.ToString("X16");
    public int SourceWorldRevision { get; }
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");
    public ReadOnlySpan<TerrainRampPortal> Ramps => _ramps;
    public ReadOnlySpan<TerrainNavigationLayerSnapshot> Layers => _layers;

    public TerrainNavigationLayerSnapshot Layer(MovementClass movementClass) =>
        _layers[(int)movementClass];

    public TerrainTopologyRoutePlanner CreateRoutePlanner() => new(this);

    public DynamicTerrainTopologyRoutePlanner CreateDynamicRoutePlanner(
        StaticWorld world,
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot clearance) =>
        new(world, terrain, clearance, this);

    public ChokeController CreateChokeController() => new(
        _ramps.Select(ramp => new ChokeDefinition(
            ramp.Id,
            ramp.LowerMouth,
            ramp.UpperMouth,
            ramp.Width,
            ramp.ApproachDistance)).ToArray());

    internal static TerrainTopologyMovementMask MaskFor(
        MovementClass movementClass) => movementClass switch
        {
            MovementClass.Small => TerrainTopologyMovementMask.Small,
            MovementClass.Medium => TerrainTopologyMovementMask.Medium,
            MovementClass.Large => TerrainTopologyMovementMask.Large,
            _ => TerrainTopologyMovementMask.None
        };

    private ulong ComputeStableHash()
    {
        using var stream = new MemoryStream(512);
        using var writer = new BinaryWriter(stream);
        writer.Write(SourceTerrainHash);
        writer.Write(SourceClearanceHash);
        writer.Write(_ramps.Length);
        foreach (var ramp in _ramps)
        {
            writer.Write(ramp.Id);
            writer.Write(ramp.FirstColumn);
            writer.Write(ramp.FirstRow);
            writer.Write(ramp.LastColumn);
            writer.Write(ramp.LastRow);
            writer.Write(ramp.LowerCliffLevel);
            writer.Write((byte)ramp.Direction);
            writer.Write(ramp.LowerMouth.X);
            writer.Write(ramp.LowerMouth.Y);
            writer.Write(ramp.UpperMouth.X);
            writer.Write(ramp.UpperMouth.Y);
            writer.Write(ramp.Width);
            writer.Write(ramp.ApproachDistance);
            writer.Write((byte)ramp.MovementMask);
        }
        writer.Write(_layers.Length);
        foreach (var layer in _layers)
        {
            writer.Write((byte)layer.MovementClass);
            writer.Write(layer.NavigationRadius);
            writer.Write(layer.RegionCount);
            foreach (var regionId in layer.SampleRegionIds)
                writer.Write(regionId);
            foreach (var region in layer.Regions)
            {
                writer.Write(region.Id);
                writer.Write(region.CliffLevel);
                writer.Write(region.SampleCount);
                writer.Write(region.Bounds.Min.X);
                writer.Write(region.Bounds.Min.Y);
                writer.Write(region.Bounds.Max.X);
                writer.Write(region.Bounds.Max.Y);
            }
            foreach (var connection in layer.RampConnections)
            {
                writer.Write(connection.RampId);
                writer.Write(connection.LowerRegionId);
                writer.Write(connection.UpperRegionId);
            }
        }
        writer.Flush();
        var hash = 14695981039346656037UL;
        foreach (var value in stream.GetBuffer().AsSpan(0, (int)stream.Length))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}

public static class TerrainNavigationTopologyBuilder
{
    private static readonly (int Column, int Row)[] Neighbors =
    [
        (1, 0), (0, 1), (-1, 0), (0, -1),
        (1, 1), (-1, 1), (-1, -1), (1, -1)
    ];

    public static TerrainNavigationTopologySnapshot Build(
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot clearance)
        => Build(terrain, clearance, runtimeConnectivity: null, 0);

    internal static TerrainNavigationTopologySnapshot Build(
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot clearance,
        NavigationConnectivitySnapshot[]? runtimeConnectivity,
        int sourceWorldRevision)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(clearance);
        if (terrain.Bounds != clearance.WorldBounds)
            throw new ArgumentException(
                "Terrain and clearance bake bounds must match.",
                nameof(clearance));
        if (clearance.SourceTerrainHash != terrain.StableHash)
            throw new ArgumentException(
                "Clearance bake must be built from the supplied terrain.",
                nameof(clearance));
        if (runtimeConnectivity is not null)
        {
            if (runtimeConnectivity.Length != 3)
                throw new ArgumentException(
                    "Runtime connectivity must contain all movement classes.",
                    nameof(runtimeConnectivity));
            for (var classIndex = 0;
                 classIndex < runtimeConnectivity.Length;
                 classIndex++)
            {
                ValidateRuntimeConnectivity(
                    clearance,
                    (MovementClass)classIndex,
                    runtimeConnectivity[classIndex],
                    sourceWorldRevision);
            }
        }

        var rampGroups = DiscoverRampGroups(terrain);
        var layers = new TerrainNavigationLayerSnapshot[3];
        for (var classIndex = 0; classIndex < layers.Length; classIndex++)
        {
            layers[classIndex] = BuildLayer(
                terrain,
                clearance,
                (MovementClass)classIndex,
                rampGroups,
                runtimeConnectivity?[classIndex]);
        }

        var ramps = new TerrainRampPortal[rampGroups.Length];
        for (var rampId = 0; rampId < ramps.Length; rampId++)
        {
            var source = rampGroups[rampId];
            var mask = TerrainTopologyMovementMask.None;
            for (var classIndex = 0; classIndex < layers.Length; classIndex++)
            {
                if (layers[classIndex].Connection(rampId).IsTraversable)
                {
                    mask |= TerrainNavigationTopologySnapshot.MaskFor(
                        (MovementClass)classIndex);
                }
            }
            ramps[rampId] = new TerrainRampPortal(
                rampId,
                source.FirstColumn,
                source.FirstRow,
                source.LastColumn,
                source.LastRow,
                source.LowerCliffLevel,
                source.Direction,
                source.LowerMouth,
                source.UpperMouth,
                source.Width,
                source.ApproachDistance,
                mask);
        }

        return new TerrainNavigationTopologySnapshot(
            terrain.StableHash,
            clearance.StableHash,
            sourceWorldRevision,
            layers,
            ramps);
    }

    private static TerrainNavigationLayerSnapshot BuildLayer(
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot clearance,
        MovementClass movementClass,
        RampGroup[] ramps,
        NavigationConnectivitySnapshot? runtimeConnectivity)
    {
        var bakeLayer = clearance.Layer(movementClass);
        var nodeCount = clearance.NodeCount;
        var eligible = new bool[nodeCount];
        var cliffLevels = new byte[nodeCount];
        for (var node = 0; node < nodeCount; node++)
        {
            if (!IsWalkable(bakeLayer, runtimeConnectivity, node)) continue;
            var center = SampleCenter(clearance, node);
            if (!terrain.TryCellAt(center, out var column, out var row))
                continue;
            var cell = terrain.Cell(column, row);
            if (cell.IsRamp ||
                (cell.Pathing & (TerrainPathing.Ground |
                                 TerrainPathing.ShallowWater)) == 0)
            {
                continue;
            }
            eligible[node] = true;
            cliffLevels[node] = cell.CliffLevel;
        }

        var regionIds = new int[nodeCount];
        Array.Fill(regionIds, -1);
        var queue = new int[nodeCount];
        var regions = new List<TerrainNavigationRegion>();
        for (var seed = 0; seed < nodeCount; seed++)
        {
            if (!eligible[seed] || regionIds[seed] >= 0) continue;
            var regionId = regions.Count;
            var level = cliffLevels[seed];
            var read = 0;
            var write = 0;
            var minimum = new Vector2(float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity);
            queue[write++] = seed;
            regionIds[seed] = regionId;
            while (read < write)
            {
                var current = queue[read++];
                var bounds = SampleBounds(clearance, current);
                minimum = Vector2.Min(minimum, bounds.Min);
                maximum = Vector2.Max(maximum, bounds.Max);
                var currentColumn = current % clearance.Columns;
                var currentRow = current / clearance.Columns;
                foreach (var offset in Neighbors)
                {
                    var column = currentColumn + offset.Column;
                    var row = currentRow + offset.Row;
                    if ((uint)column >= (uint)clearance.Columns ||
                        (uint)row >= (uint)clearance.Rows)
                    {
                        continue;
                    }
                    var neighbor = row * clearance.Columns + column;
                    if (!eligible[neighbor] || regionIds[neighbor] >= 0 ||
                        cliffLevels[neighbor] != level)
                    {
                        continue;
                    }
                    if (offset.Column != 0 && offset.Row != 0)
                    {
                        var horizontal = currentRow * clearance.Columns + column;
                        var vertical = row * clearance.Columns + currentColumn;
                        if (!eligible[horizontal] || !eligible[vertical] ||
                            cliffLevels[horizontal] != level ||
                            cliffLevels[vertical] != level)
                        {
                            continue;
                        }
                    }
                    regionIds[neighbor] = regionId;
                    queue[write++] = neighbor;
                }
            }
            regions.Add(new TerrainNavigationRegion(
                regionId,
                level,
                write,
                new SimRect(minimum, maximum)));
        }

        var connections = new TerrainRampRegionConnection[ramps.Length];
        var rampVisitMarks = new int[nodeCount];
        var requiredWidth = MovementClearance.ForClass(
            movementClass).RequiredWidth;
        for (var rampId = 0; rampId < ramps.Length; rampId++)
        {
            var ramp = ramps[rampId];
            var lower = ramp.Width >= requiredWidth
                ? FindNearestRegion(
                    clearance,
                    regions,
                    regionIds,
                    ramp.LowerMouth,
                    ramp.LowerCliffLevel,
                    terrain.CellSize * 1.75f)
                : -1;
            var upper = ramp.Width >= requiredWidth
                ? FindNearestRegion(
                    clearance,
                    regions,
                    regionIds,
                    ramp.UpperMouth,
                    (byte)(ramp.LowerCliffLevel + 1),
                    terrain.CellSize * 1.75f)
                : -1;
            if (lower < 0 || upper < 0 || !HasRampConnection(
                    terrain,
                    clearance,
                    bakeLayer,
                    runtimeConnectivity,
                    ramp,
                    lower,
                    upper,
                    regionIds,
                    rampVisitMarks,
                    queue,
                    rampId + 1))
            {
                lower = -1;
                upper = -1;
            }
            connections[rampId] = new TerrainRampRegionConnection(
                rampId, lower, upper);
        }

        return new TerrainNavigationLayerSnapshot(
            movementClass,
            bakeLayer.NavigationRadius,
            clearance.WorldBounds,
            clearance.CellSize,
            clearance.Columns,
            clearance.Rows,
            regionIds,
            regions.ToArray(),
            connections);
    }

    private static void ValidateRuntimeConnectivity(
        ClearanceBakeSnapshot clearance,
        MovementClass movementClass,
        NavigationConnectivitySnapshot runtime,
        int sourceWorldRevision)
    {
        var bakeLayer = clearance.Layer(movementClass);
        if (runtime.WorldBounds != clearance.WorldBounds ||
            runtime.Columns != clearance.Columns ||
            runtime.Rows != clearance.Rows ||
            MathF.Abs(runtime.CellSize - clearance.CellSize) > 0.0001f ||
            MathF.Abs(runtime.NavigationRadius -
                      bakeLayer.NavigationRadius) > 0.0001f ||
            runtime.WorldRevision != sourceWorldRevision)
        {
            throw new ArgumentException(
                $"Runtime connectivity for {movementClass} does not match " +
                "the clearance layout or world revision.",
                nameof(runtime));
        }
    }

    private static bool IsWalkable(
        ClearanceBakeLayerSnapshot bakeLayer,
        NavigationConnectivitySnapshot? runtimeConnectivity,
        int node) => runtimeConnectivity?.IsWalkable(node) ??
                     bakeLayer.IsWalkable(node);

    private static RampGroup[] DiscoverRampGroups(TerrainMapSnapshot terrain)
    {
        var visited = new bool[terrain.CellCount];
        var result = new List<RampGroup>();
        for (var row = 0; row < terrain.Rows; row++)
        {
            for (var column = 0; column < terrain.Columns; column++)
            {
                var index = row * terrain.Columns + column;
                var seed = terrain.Cell(column, row);
                if (visited[index] || !seed.IsRamp) continue;
                var cells = new List<(int Column, int Row)>();
                var queue = new Queue<(int Column, int Row)>();
                queue.Enqueue((column, row));
                visited[index] = true;
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    cells.Add(current);
                    foreach (var offset in PerpendicularOffsets(seed.RampDirection))
                    {
                        var nextColumn = current.Column + offset.Column;
                        var nextRow = current.Row + offset.Row;
                        if ((uint)nextColumn >= (uint)terrain.Columns ||
                            (uint)nextRow >= (uint)terrain.Rows)
                        {
                            continue;
                        }
                        var nextIndex = nextRow * terrain.Columns + nextColumn;
                        var next = terrain.Cell(nextColumn, nextRow);
                        if (visited[nextIndex] || !next.IsRamp ||
                            next.RampDirection != seed.RampDirection ||
                            next.CliffLevel != seed.CliffLevel)
                        {
                            continue;
                        }
                        visited[nextIndex] = true;
                        queue.Enqueue((nextColumn, nextRow));
                    }
                }
                result.Add(CreateRampGroup(terrain, seed, cells));
            }
        }
        return result.ToArray();
    }

    private static RampGroup CreateRampGroup(
        TerrainMapSnapshot terrain,
        TerrainCell seed,
        List<(int Column, int Row)> cells)
    {
        cells.Sort((left, right) =>
        {
            var primary = seed.RampDirection is
                TerrainRampDirection.PositiveX or TerrainRampDirection.NegativeX
                ? left.Row.CompareTo(right.Row)
                : left.Column.CompareTo(right.Column);
            return primary != 0
                ? primary
                : left.Column != right.Column
                    ? left.Column.CompareTo(right.Column)
                    : left.Row.CompareTo(right.Row);
        });
        var lowerSum = Vector2.Zero;
        var upperSum = Vector2.Zero;
        foreach (var cell in cells)
        {
            var travel = DirectionOffset(seed.RampDirection);
            var lowerColumn = cell.Column - travel.Column;
            var lowerRow = cell.Row - travel.Row;
            var upperColumn = cell.Column + travel.Column;
            var upperRow = cell.Row + travel.Row;
            if ((uint)lowerColumn >= (uint)terrain.Columns ||
                (uint)lowerRow >= (uint)terrain.Rows ||
                (uint)upperColumn >= (uint)terrain.Columns ||
                (uint)upperRow >= (uint)terrain.Rows ||
                terrain.Cell(lowerColumn, lowerRow).CliffLevel !=
                    seed.CliffLevel ||
                terrain.Cell(upperColumn, upperRow).CliffLevel !=
                    seed.CliffLevel + 1)
            {
                throw new ArgumentException(
                    $"Ramp cell [{cell.Column},{cell.Row}] does not connect " +
                    "its declared adjacent cliff levels.",
                    nameof(terrain));
            }
            lowerSum += CellCenter(terrain, lowerColumn, lowerRow);
            upperSum += CellCenter(terrain, upperColumn, upperRow);
        }
        var first = cells[0];
        var last = cells[^1];
        var width = cells.Count * terrain.CellSize;
        return new RampGroup(
            first.Column,
            first.Row,
            last.Column,
            last.Row,
            seed.CliffLevel,
            seed.RampDirection,
            lowerSum / cells.Count,
            upperSum / cells.Count,
            width,
            MathF.Max(terrain.CellSize * 2.5f, width * 0.75f),
            cells.ToArray());
    }

    private static int FindNearestRegion(
        ClearanceBakeSnapshot clearance,
        List<TerrainNavigationRegion> regions,
        int[] regionIds,
        Vector2 point,
        byte cliffLevel,
        float maximumDistance)
    {
        var bestRegion = -1;
        var bestDistance = maximumDistance * maximumDistance;
        var minimumColumn = SampleColumn(
            clearance, point.X - maximumDistance);
        var maximumColumn = SampleColumn(
            clearance, point.X + maximumDistance);
        var minimumRow = SampleRow(
            clearance, point.Y - maximumDistance);
        var maximumRow = SampleRow(
            clearance, point.Y + maximumDistance);
        for (var row = minimumRow; row <= maximumRow; row++)
        {
            for (var column = minimumColumn;
                 column <= maximumColumn;
                 column++)
            {
                var node = row * clearance.Columns + column;
                var region = regionIds[node];
                if (region < 0 || regions[region].CliffLevel != cliffLevel)
                    continue;
                var distance = Vector2.DistanceSquared(
                    point, SampleCenter(clearance, node));
                if (distance < bestDistance - 0.0001f ||
                    MathF.Abs(distance - bestDistance) <= 0.0001f &&
                    (bestRegion < 0 || region < bestRegion))
                {
                    bestDistance = distance;
                    bestRegion = region;
                }
            }
        }
        return bestRegion;
    }

    private static bool HasRampConnection(
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot clearance,
        ClearanceBakeLayerSnapshot layer,
        NavigationConnectivitySnapshot? runtimeConnectivity,
        RampGroup ramp,
        int lowerRegion,
        int upperRegion,
        int[] regionIds,
        int[] visitMarks,
        int[] queue,
        int visitStamp)
    {
        var minimumColumn = ramp.Cells.Min(value => value.Column);
        var maximumColumn = ramp.Cells.Max(value => value.Column);
        var minimumRow = ramp.Cells.Min(value => value.Row);
        var maximumRow = ramp.Cells.Max(value => value.Row);
        var travel = DirectionOffset(ramp.Direction);
        minimumColumn = Math.Min(
            minimumColumn,
            ramp.Cells.Min(value => Math.Min(
                value.Column - travel.Column,
                value.Column + travel.Column)));
        maximumColumn = Math.Max(
            maximumColumn,
            ramp.Cells.Max(value => Math.Max(
                value.Column - travel.Column,
                value.Column + travel.Column)));
        minimumRow = Math.Min(
            minimumRow,
            ramp.Cells.Min(value => Math.Min(
                value.Row - travel.Row,
                value.Row + travel.Row)));
        maximumRow = Math.Max(
            maximumRow,
            ramp.Cells.Max(value => Math.Max(
                value.Row - travel.Row,
                value.Row + travel.Row)));
        var minimum = terrain.CellBounds(
            minimumColumn, minimumRow).Min;
        var maximum = terrain.CellBounds(
            maximumColumn, maximumRow).Max;
        var firstSampleColumn = SampleColumn(clearance, minimum.X);
        var lastSampleColumn = SampleColumn(
            clearance, maximum.X - 0.0001f);
        var firstSampleRow = SampleRow(clearance, minimum.Y);
        var lastSampleRow = SampleRow(
            clearance, maximum.Y - 0.0001f);
        var read = 0;
        var write = 0;
        for (var sampleRow = firstSampleRow;
             sampleRow <= lastSampleRow;
             sampleRow++)
        {
            for (var sampleColumn = firstSampleColumn;
                 sampleColumn <= lastSampleColumn;
                 sampleColumn++)
            {
                var node = sampleRow * clearance.Columns + sampleColumn;
                if (!IsWalkable(layer, runtimeConnectivity, node) ||
                    regionIds[node] != lowerRegion)
                {
                    continue;
                }
                visitMarks[node] = visitStamp;
                queue[write++] = node;
            }
        }

        while (read < write)
        {
            var current = queue[read++];
            if (regionIds[current] == upperRegion) return true;
            var currentColumn = current % clearance.Columns;
            var currentRow = current / clearance.Columns;
            foreach (var offset in Neighbors)
            {
                var column = currentColumn + offset.Column;
                var row = currentRow + offset.Row;
                if (column < firstSampleColumn || column > lastSampleColumn ||
                    row < firstSampleRow || row > lastSampleRow)
                {
                    continue;
                }
                var neighbor = row * clearance.Columns + column;
                if (visitMarks[neighbor] == visitStamp ||
                    !IsRampSearchNode(
                        terrain,
                        clearance,
                        layer,
                        runtimeConnectivity,
                        ramp,
                        lowerRegion,
                        upperRegion,
                        regionIds,
                        neighbor))
                {
                    continue;
                }
                if (offset.Column != 0 && offset.Row != 0)
                {
                    var horizontal = currentRow * clearance.Columns + column;
                    var vertical = row * clearance.Columns + currentColumn;
                    if (!IsRampSearchNode(
                            terrain,
                            clearance,
                            layer,
                            runtimeConnectivity,
                            ramp,
                            lowerRegion,
                            upperRegion,
                            regionIds,
                            horizontal) ||
                        !IsRampSearchNode(
                            terrain,
                            clearance,
                            layer,
                            runtimeConnectivity,
                            ramp,
                            lowerRegion,
                            upperRegion,
                            regionIds,
                            vertical))
                    {
                        continue;
                    }
                }
                visitMarks[neighbor] = visitStamp;
                queue[write++] = neighbor;
            }
        }
        return false;
    }

    private static bool IsRampSearchNode(
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot clearance,
        ClearanceBakeLayerSnapshot layer,
        NavigationConnectivitySnapshot? runtimeConnectivity,
        RampGroup ramp,
        int lowerRegion,
        int upperRegion,
        int[] regionIds,
        int node)
    {
        if (!IsWalkable(layer, runtimeConnectivity, node)) return false;
        if (regionIds[node] == lowerRegion || regionIds[node] == upperRegion)
            return true;
        var center = SampleCenter(clearance, node);
        if (!terrain.TryCellAt(center, out var column, out var row))
            return false;
        for (var index = 0; index < ramp.Cells.Length; index++)
        {
            if (ramp.Cells[index] == (column, row)) return true;
        }
        return false;
    }

    private static (int Column, int Row)[] PerpendicularOffsets(
        TerrainRampDirection direction) => direction switch
        {
            TerrainRampDirection.PositiveX or TerrainRampDirection.NegativeX =>
                [(0, -1), (0, 1)],
            TerrainRampDirection.PositiveY or TerrainRampDirection.NegativeY =>
                [(-1, 0), (1, 0)],
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

    private static (int Column, int Row) DirectionOffset(
        TerrainRampDirection direction) => direction switch
        {
            TerrainRampDirection.PositiveX => (1, 0),
            TerrainRampDirection.NegativeX => (-1, 0),
            TerrainRampDirection.PositiveY => (0, 1),
            TerrainRampDirection.NegativeY => (0, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

    private static Vector2 CellCenter(
        TerrainMapSnapshot terrain,
        int column,
        int row)
    {
        var bounds = terrain.CellBounds(column, row);
        return (bounds.Min + bounds.Max) * 0.5f;
    }

    private static Vector2 SampleCenter(
        ClearanceBakeSnapshot clearance,
        int node)
    {
        var column = node % clearance.Columns;
        var row = node / clearance.Columns;
        return clearance.WorldBounds.Min + new Vector2(
            (column + 0.5f) * clearance.CellSize,
            (row + 0.5f) * clearance.CellSize);
    }

    private static SimRect SampleBounds(
        ClearanceBakeSnapshot clearance,
        int node)
    {
        var center = SampleCenter(clearance, node);
        var half = new Vector2(clearance.CellSize * 0.5f);
        return new SimRect(
            Vector2.Max(clearance.WorldBounds.Min, center - half),
            Vector2.Min(clearance.WorldBounds.Max, center + half));
    }

    private static int SampleColumn(
        ClearanceBakeSnapshot clearance,
        float worldX) => Math.Clamp(
        (int)MathF.Floor(
            (worldX - clearance.WorldBounds.Min.X) / clearance.CellSize),
        0,
        clearance.Columns - 1);

    private static int SampleRow(
        ClearanceBakeSnapshot clearance,
        float worldY) => Math.Clamp(
        (int)MathF.Floor(
            (worldY - clearance.WorldBounds.Min.Y) / clearance.CellSize),
        0,
        clearance.Rows - 1);

    private sealed record RampGroup(
        int FirstColumn,
        int FirstRow,
        int LastColumn,
        int LastRow,
        byte LowerCliffLevel,
        TerrainRampDirection Direction,
        Vector2 LowerMouth,
        Vector2 UpperMouth,
        float Width,
        float ApproachDistance,
        (int Column, int Row)[] Cells);
}

/// <summary>
/// Keeps the immutable authored terrain topology separate from temporary
/// world occupancy. Hard footprints resample only intersecting clearance
/// chunks; region ids and ramp connections are then deterministically
/// relabelled from the updated walkability.
/// </summary>
public sealed class DynamicTerrainTopologyRoutePlanner :
    IGroupRoutePlanner,
    IGroupRouteNavigationChangeSink
{
    private readonly StaticWorld _world;
    private readonly TerrainMapSnapshot _terrain;
    private readonly ClearanceBakeSnapshot _clearance;
    private readonly IncrementalNavigationConnectivityUpdater _updater;
    private readonly NavigationConnectivityAnalyzer _analyzer;
    private readonly NavigationConnectivitySnapshot[] _connectivity =
        new NavigationConnectivitySnapshot[3];
    private TerrainNavigationTopologySnapshot _topology;
    private TerrainTopologyRoutePlanner _planner;

    internal DynamicTerrainTopologyRoutePlanner(
        StaticWorld world,
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot clearance,
        TerrainNavigationTopologySnapshot initialTopology)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(clearance);
        ArgumentNullException.ThrowIfNull(initialTopology);
        if (world.Bounds != terrain.Bounds || world.Bounds != clearance.WorldBounds)
            throw new ArgumentException(
                "World, terrain and clearance bounds must match.");
        if (initialTopology.SourceTerrainHash != terrain.StableHash ||
            initialTopology.SourceClearanceHash != clearance.StableHash)
        {
            throw new ArgumentException(
                "Initial topology must match the supplied terrain and clearance.",
                nameof(initialTopology));
        }

        _world = world;
        _terrain = terrain;
        _clearance = clearance;
        _updater = new IncrementalNavigationConnectivityUpdater(world, clearance);
        _analyzer = new NavigationConnectivityAnalyzer(world, clearance.CellSize);
        if (world.NavigationRevision == 0)
        {
            for (var classIndex = 0; classIndex < _connectivity.Length; classIndex++)
            {
                _connectivity[classIndex] = clearance.CreateConnectivitySnapshot(
                    (MovementClass)classIndex);
            }
            _topology = initialTopology;
        }
        else
        {
            AnalyzeFullWorld();
            _topology = TerrainNavigationTopologyBuilder.Build(
                terrain, clearance, _connectivity, world.NavigationRevision);
            FullRebuilds++;
        }
        _planner = _topology.CreateRoutePlanner();
    }

    public TerrainNavigationTopologySnapshot CurrentTopology => _topology;
    public int IncrementalUpdates { get; private set; }
    public int FullRebuilds { get; private set; }
    public int TotalResampledCells { get; private set; }
    public int LastResampledCells { get; private set; }
    public int LastChangedCells { get; private set; }
    public int[] LastDirtyChunkIds { get; private set; } = [];
    public double LastRefreshMilliseconds { get; private set; }

    public GroupRoutePlan Plan(Vector2 start, Vector2 goal, float agentRadius)
    {
        EnsureCurrent(changedBounds: null);
        return _planner.Plan(start, goal, agentRadius);
    }

    public void OnNavigationChanged(SimRect changedBounds) =>
        EnsureCurrent(changedBounds);

    public void OnNavigationStateRestored()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        AnalyzeFullWorld();
        FullRebuilds++;
        LastResampledCells = _connectivity.Sum(value => value.NodeCount);
        LastChangedCells = -1;
        LastDirtyChunkIds = Enumerable.Range(
            0, _clearance.ChunkCount).ToArray();
        _topology = TerrainNavigationTopologyBuilder.Build(
            _terrain,
            _clearance,
            _connectivity,
            _world.NavigationRevision);
        _planner = _topology.CreateRoutePlanner();
        timer.Stop();
        LastRefreshMilliseconds = timer.Elapsed.TotalMilliseconds;
    }

    private void EnsureCurrent(SimRect? changedBounds)
    {
        if (_topology.SourceWorldRevision == _world.NavigationRevision)
            return;

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var sourceRevision = _topology.SourceWorldRevision;
        var canUpdateIncrementally = sourceRevision < _world.NavigationRevision &&
            _connectivity.All(value => value.WorldRevision == sourceRevision);
        SimRect[] areas;
        if (changedBounds is not null &&
            _world.NavigationRevision - sourceRevision == 1)
            areas = [changedBounds.Value];
        else if (!canUpdateIncrementally ||
                 !_world.DynamicOccupancy.TryGetChangesSince(
                     sourceRevision, out areas))
            areas = [];

        if (canUpdateIncrementally && areas.Length > 0)
        {
            var dirtyChunks = new SortedSet<int>();
            var resampled = 0;
            var changed = 0;
            for (var classIndex = 0;
                 classIndex < _connectivity.Length;
                 classIndex++)
            {
                var update = _updater.Update(
                    _connectivity[classIndex], areas);
                _connectivity[classIndex] = update.Snapshot;
                resampled += update.ResampledCells;
                changed += update.ChangedCells;
                dirtyChunks.UnionWith(update.DirtyChunkIds);
            }
            IncrementalUpdates++;
            LastResampledCells = resampled;
            LastChangedCells = changed;
            TotalResampledCells += resampled;
            LastDirtyChunkIds = dirtyChunks.ToArray();
        }
        else
        {
            AnalyzeFullWorld();
            FullRebuilds++;
            LastResampledCells = _connectivity.Sum(value => value.NodeCount);
            LastChangedCells = -1;
            LastDirtyChunkIds = Enumerable.Range(
                0, _clearance.ChunkCount).ToArray();
        }

        _topology = TerrainNavigationTopologyBuilder.Build(
            _terrain,
            _clearance,
            _connectivity,
            _world.NavigationRevision);
        _planner = _topology.CreateRoutePlanner();
        timer.Stop();
        LastRefreshMilliseconds = timer.Elapsed.TotalMilliseconds;
    }

    private void AnalyzeFullWorld()
    {
        for (var classIndex = 0;
             classIndex < _connectivity.Length;
             classIndex++)
        {
            var movementClass = (MovementClass)classIndex;
            _connectivity[classIndex] = _analyzer.Analyze(
                _clearance.Layer(movementClass).NavigationRadius);
        }
    }
}

/// <summary>
/// Plans only the high-level sequence of ramp mouths. Region-internal routing
/// remains the responsibility of the production GridPathProvider.
/// </summary>
public sealed class TerrainTopologyRoutePlanner : IGroupRoutePlanner
{
    private readonly TerrainNavigationTopologySnapshot _topology;

    public TerrainTopologyRoutePlanner(
        TerrainNavigationTopologySnapshot topology)
    {
        _topology = topology;
    }

    public TerrainNavigationTopologySnapshot Topology => _topology;

    public GroupRoutePlan Plan(Vector2 start, Vector2 goal, float agentRadius)
    {
        var movementClass = MovementClearance.FromPhysicalRadius(
            agentRadius).Class;
        var layer = _topology.Layer(movementClass);
        var startRegion = ResolveRegion(layer, start);
        var goalRegion = ResolveRegion(layer, goal);
        if (startRegion < 0 || goalRegion < 0 || startRegion == goalRegion)
            return GroupRoutePlan.Empty;

        var ramps = _topology.Ramps;
        var endpointCount = ramps.Length * 2;
        var startNode = endpointCount;
        var goalNode = endpointCount + 1;
        var totalNodes = endpointCount + 2;
        var costs = new float[totalNodes];
        var parents = new int[totalNodes];
        var open = new bool[totalNodes];
        var closed = new bool[totalNodes];
        Array.Fill(costs, float.PositiveInfinity);
        Array.Fill(parents, -1);
        costs[startNode] = 0f;
        open[startNode] = true;

        while (true)
        {
            var current = SelectBest(open, closed, costs);
            if (current < 0) return GroupRoutePlan.Empty;
            if (current == goalNode) break;
            open[current] = false;
            closed[current] = true;
            for (var neighbor = 0; neighbor < totalNodes; neighbor++)
            {
                if (neighbor == current || closed[neighbor] ||
                    !TryConnection(
                        layer,
                        movementClass,
                        start,
                        goal,
                        startRegion,
                        goalRegion,
                        current,
                        neighbor,
                        startNode,
                        goalNode,
                        out var edgeCost))
                {
                    continue;
                }
                var candidate = costs[current] + edgeCost;
                if (candidate < costs[neighbor] - 0.0001f ||
                    MathF.Abs(candidate - costs[neighbor]) <= 0.0001f &&
                    current < parents[neighbor])
                {
                    costs[neighbor] = candidate;
                    parents[neighbor] = current;
                    open[neighbor] = true;
                }
            }
        }

        var nodes = new List<int>();
        for (var cursor = parents[goalNode]; cursor >= 0 && cursor != startNode;
             cursor = parents[cursor])
        {
            nodes.Add(cursor);
        }
        nodes.Reverse();
        var waypoints = nodes.Select(EndpointPosition).ToArray();
        var chokeIds = new List<int>();
        var previous = startNode;
        foreach (var node in nodes.Append(goalNode))
        {
            if (previous < endpointCount && node < endpointCount &&
                previous / 2 == node / 2 && previous != node)
            {
                chokeIds.Add(previous / 2);
            }
            previous = node;
        }
        return new GroupRoutePlan(waypoints, chokeIds.ToArray(), costs[goalNode]);
    }

    private bool TryConnection(
        TerrainNavigationLayerSnapshot layer,
        MovementClass movementClass,
        Vector2 start,
        Vector2 goal,
        int startRegion,
        int goalRegion,
        int from,
        int to,
        int startNode,
        int goalNode,
        out float cost)
    {
        cost = 0f;
        if (from == goalNode || to == startNode) return false;
        if (from == startNode)
        {
            if (to >= startNode || EndpointRegion(layer, to) != startRegion)
                return false;
            cost = Vector2.Distance(start, EndpointPosition(to));
            return true;
        }
        if (to == goalNode)
        {
            if (from >= startNode || EndpointRegion(layer, from) != goalRegion)
                return false;
            cost = Vector2.Distance(EndpointPosition(from), goal);
            return true;
        }
        if (from >= startNode || to >= startNode) return false;
        var fromRamp = from / 2;
        var toRamp = to / 2;
        if (fromRamp == toRamp)
        {
            if (from == to || !_topology.Ramps[fromRamp].Supports(movementClass))
                return false;
            var connection = layer.Connection(fromRamp);
            if (!connection.IsTraversable) return false;
            cost = Vector2.Distance(EndpointPosition(from), EndpointPosition(to));
            return true;
        }
        if (EndpointRegion(layer, from) != EndpointRegion(layer, to))
            return false;
        cost = Vector2.Distance(EndpointPosition(from), EndpointPosition(to));
        return true;
    }

    private int ResolveRegion(
        TerrainNavigationLayerSnapshot layer,
        Vector2 point)
    {
        var direct = layer.RegionAt(point);
        if (direct >= 0) return direct;
        var bestRegion = -1;
        var bestDistance = float.PositiveInfinity;
        for (var rampId = 0; rampId < _topology.Ramps.Length; rampId++)
        {
            var connection = layer.Connection(rampId);
            if (!connection.IsTraversable) continue;
            var ramp = _topology.Ramps[rampId];
            var axis = Vector2.Normalize(ramp.UpperMouth - ramp.LowerMouth);
            var normal = new Vector2(-axis.Y, axis.X);
            var relative = point - ramp.LowerMouth;
            var along = Vector2.Dot(relative, axis);
            var lateral = MathF.Abs(Vector2.Dot(relative, normal));
            if (along < 0f || along > ramp.Length ||
                lateral > ramp.Width * 0.5f)
            {
                continue;
            }
            var lowerDistance = Vector2.DistanceSquared(point, ramp.LowerMouth);
            if (lowerDistance < bestDistance)
            {
                bestDistance = lowerDistance;
                bestRegion = connection.LowerRegionId;
            }
            var upperDistance = Vector2.DistanceSquared(point, ramp.UpperMouth);
            if (upperDistance < bestDistance)
            {
                bestDistance = upperDistance;
                bestRegion = connection.UpperRegionId;
            }
        }
        return bestRegion;
    }

    private int EndpointRegion(
        TerrainNavigationLayerSnapshot layer,
        int endpoint)
    {
        var connection = layer.Connection(endpoint / 2);
        return (endpoint & 1) == 0
            ? connection.LowerRegionId
            : connection.UpperRegionId;
    }

    private Vector2 EndpointPosition(int endpoint)
    {
        var ramp = _topology.Ramps[endpoint / 2];
        return (endpoint & 1) == 0 ? ramp.LowerMouth : ramp.UpperMouth;
    }

    private static int SelectBest(
        bool[] open,
        bool[] closed,
        float[] costs)
    {
        var best = -1;
        var bestCost = float.PositiveInfinity;
        for (var node = 0; node < open.Length; node++)
        {
            if (!open[node] || closed[node]) continue;
            if (costs[node] < bestCost - 0.0001f ||
                MathF.Abs(costs[node] - bestCost) <= 0.0001f && node < best)
            {
                best = node;
                bestCost = costs[node];
            }
        }
        return best;
    }
}
