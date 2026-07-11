using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct NavigationComponentSummary(
    int Id,
    int CellCount,
    SimRect Bounds);

public enum NavigationConnectivitySource : byte
{
    RuntimeAnalysis,
    StaticBake,
    IncrementalRuntimeAnalysis
}

/// <summary>
/// Immutable-by-contract sampled navigation topology for one clearance radius.
/// Pathfinding, placement validation and editor diagnostics share this shape.
/// </summary>
public sealed class NavigationConnectivitySnapshot
{
    private readonly bool[] _walkable;
    private readonly int[] _componentIds;
    private readonly NavigationComponentSummary[] _components;

    internal NavigationConnectivitySnapshot(
        SimRect worldBounds,
        float cellSize,
        int columns,
        int rows,
        float navigationRadius,
        int worldRevision,
        NavigationConnectivitySource source,
        bool[] walkable,
        int[] componentIds,
        NavigationComponentSummary[] components)
    {
        WorldBounds = worldBounds;
        CellSize = cellSize;
        Columns = columns;
        Rows = rows;
        NavigationRadius = navigationRadius;
        WorldRevision = worldRevision;
        Source = source;
        _walkable = walkable;
        _componentIds = componentIds;
        _components = components;
    }

    public SimRect WorldBounds { get; }
    public float CellSize { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int NodeCount => _walkable.Length;
    public float NavigationRadius { get; }
    public int WorldRevision { get; }
    public NavigationConnectivitySource Source { get; }
    public int ComponentCount => _components.Length;
    public ReadOnlySpan<NavigationComponentSummary> Components => _components;

    public bool IsWalkable(int node) => _walkable[node];

    public int ComponentAt(int node) => _componentIds[node];

    public Vector2 CellCenter(int node)
    {
        var column = node % Columns;
        var row = node / Columns;
        return WorldBounds.Min +
               new Vector2((column + 0.5f) * CellSize, (row + 0.5f) * CellSize);
    }

    public SimRect CellBounds(int node)
    {
        var center = CellCenter(node);
        var halfSize = new Vector2(CellSize * 0.5f);
        return new SimRect(
            Vector2.Max(WorldBounds.Min, center - halfSize),
            Vector2.Min(WorldBounds.Max, center + halfSize));
    }
}

public sealed class NavigationConnectivityAnalyzer
{
    private static readonly (int Column, int Row)[] NeighborOffsetValues =
    [
        (1, 0), (0, 1), (-1, 0), (0, -1),
        (1, 1), (-1, 1), (-1, -1), (1, -1)
    ];

    private readonly StaticWorld _world;
    private readonly int _columns;
    private readonly int _rows;

    public NavigationConnectivityAnalyzer(
        StaticWorld world,
        float cellSize = 16f)
    {
        if (!float.IsFinite(cellSize) || cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        _world = world;
        CellSize = cellSize;
        _columns = Math.Max(
            1, (int)MathF.Ceiling(world.Bounds.Width / cellSize));
        _rows = Math.Max(
            1, (int)MathF.Ceiling(world.Bounds.Height / cellSize));
    }

    public float CellSize { get; }

    internal static ReadOnlySpan<(int Column, int Row)> NeighborOffsets =>
        NeighborOffsetValues;

    public NavigationConnectivitySnapshot Analyze(
        float navigationRadius,
        SimRect? additionalObstacle = null)
    {
        if (!float.IsFinite(navigationRadius) || navigationRadius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(navigationRadius));
        }

        if (additionalObstacle is { } obstacle &&
            (!IsFinite(obstacle.Min) || !IsFinite(obstacle.Max) ||
             obstacle.Width <= 0f || obstacle.Height <= 0f))
        {
            throw new ArgumentException(
                "Additional obstacle must be finite and non-empty.",
                nameof(additionalObstacle));
        }

        var nodeCount = _columns * _rows;
        var walkable = new bool[nodeCount];
        var expandedAdditionalObstacle = additionalObstacle?.Expanded(
            navigationRadius);
        for (var node = 0; node < nodeCount; node++)
        {
            var center = CellCenter(node);
            walkable[node] =
                _world.IsDiscFree(center, navigationRadius) &&
                !(expandedAdditionalObstacle?.Contains(center) ?? false);
        }

        return BuildSnapshot(
            _world.Bounds,
            CellSize,
            _columns,
            _rows,
            navigationRadius,
            _world.NavigationRevision,
            NavigationConnectivitySource.RuntimeAnalysis,
            walkable);
    }

    internal static NavigationConnectivitySnapshot BuildSnapshot(
        SimRect worldBounds,
        float cellSize,
        int columns,
        int rows,
        float navigationRadius,
        int worldRevision,
        NavigationConnectivitySource source,
        bool[] walkable)
    {
        if (walkable.Length != columns * rows)
        {
            throw new ArgumentException(
                "Walkability must match the declared connectivity grid.",
                nameof(walkable));
        }

        var nodeCount = walkable.Length;
        var componentIds = new int[nodeCount];
        Array.Fill(componentIds, -1);
        var queue = new int[nodeCount];
        var componentCells = new List<int>();
        var componentBounds = new List<SimRect>();
        for (var seed = 0; seed < nodeCount; seed++)
        {
            if (!walkable[seed] || componentIds[seed] >= 0)
            {
                continue;
            }

            var component = componentCells.Count;
            var read = 0;
            var write = 0;
            var minimum = new Vector2(float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity);
            queue[write++] = seed;
            componentIds[seed] = component;
            while (read < write)
            {
                var current = queue[read++];
                var cellBounds = CellBounds(
                    worldBounds, cellSize, columns, current);
                minimum = Vector2.Min(minimum, cellBounds.Min);
                maximum = Vector2.Max(maximum, cellBounds.Max);
                var currentColumn = current % columns;
                var currentRow = current / columns;
                var offsets = NeighborOffsets;
                for (var offsetIndex = 0;
                     offsetIndex < offsets.Length;
                     offsetIndex++)
                {
                    var offset = offsets[offsetIndex];
                    var column = currentColumn + offset.Column;
                    var row = currentRow + offset.Row;
                    if ((uint)column >= (uint)columns ||
                        (uint)row >= (uint)rows)
                    {
                        continue;
                    }

                    var neighbor = row * columns + column;
                    if (!walkable[neighbor] || componentIds[neighbor] >= 0)
                    {
                        continue;
                    }

                    if (offset.Column != 0 && offset.Row != 0 &&
                        (!walkable[currentRow * columns + column] ||
                         !walkable[row * columns + currentColumn]))
                    {
                        continue;
                    }

                    componentIds[neighbor] = component;
                    queue[write++] = neighbor;
                }
            }

            componentCells.Add(write);
            componentBounds.Add(new SimRect(minimum, maximum));
        }

        var components = new NavigationComponentSummary[componentCells.Count];
        for (var index = 0; index < components.Length; index++)
        {
            components[index] = new NavigationComponentSummary(
                index, componentCells[index], componentBounds[index]);
        }

        return new NavigationConnectivitySnapshot(
            worldBounds,
            cellSize,
            columns,
            rows,
            navigationRadius,
            worldRevision,
            source,
            walkable,
            componentIds,
            components);
    }

    private Vector2 CellCenter(int node)
    {
        var column = node % _columns;
        var row = node / _columns;
        return _world.Bounds.Min +
               new Vector2((column + 0.5f) * CellSize, (row + 0.5f) * CellSize);
    }

    private static SimRect CellBounds(
        SimRect worldBounds,
        float cellSize,
        int columns,
        int node)
    {
        var column = node % columns;
        var row = node / columns;
        var center = worldBounds.Min +
                     new Vector2(
                         (column + 0.5f) * cellSize,
                         (row + 0.5f) * cellSize);
        var halfSize = new Vector2(cellSize * 0.5f);
        return new SimRect(
            Vector2.Max(worldBounds.Min, center - halfSize),
            Vector2.Min(worldBounds.Max, center + halfSize));
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}

public readonly record struct IncrementalConnectivityUpdate(
    NavigationConnectivitySnapshot Snapshot,
    int[] DirtyChunkIds,
    int ResampledCells,
    int ChangedCells)
{
    public float ResampledRatio => Snapshot.NodeCount == 0
        ? 0f
        : (float)ResampledCells / Snapshot.NodeCount;
}

/// <summary>
/// Reuses walkability outside dirty bake chunks, then globally relabels
/// components so the result remains equivalent to a full runtime analysis.
/// </summary>
public sealed class IncrementalNavigationConnectivityUpdater
{
    private readonly StaticWorld _world;
    private readonly ClearanceBakeSnapshot _layout;

    public IncrementalNavigationConnectivityUpdater(
        StaticWorld world,
        ClearanceBakeSnapshot layout)
    {
        if (world.Bounds != layout.WorldBounds)
        {
            throw new ArgumentException(
                "World and clearance bake must share bounds.", nameof(layout));
        }
        _world = world;
        _layout = layout;
    }

    public IncrementalConnectivityUpdate Update(
        NavigationConnectivitySnapshot previous,
        SimRect changedWorldArea)
    {
        Validate(previous, changedWorldArea);
        var dirtyChunks = _layout.FindIntersectingChunks(
            changedWorldArea.Expanded(previous.NavigationRadius));
        if (dirtyChunks.Length == 0)
        {
            throw new ArgumentException(
                "Changed area must intersect the navigation world.",
                nameof(changedWorldArea));
        }

        var walkable = new bool[previous.NodeCount];
        for (var node = 0; node < walkable.Length; node++)
        {
            walkable[node] = previous.IsWalkable(node);
        }

        var resampled = 0;
        var changed = 0;
        for (var dirtyIndex = 0; dirtyIndex < dirtyChunks.Length; dirtyIndex++)
        {
            var chunk = _layout.Chunk(dirtyChunks[dirtyIndex]);
            for (var row = chunk.MinimumCellRow;
                 row < chunk.MaximumCellRowExclusive;
                 row++)
            {
                for (var column = chunk.MinimumCellColumn;
                     column < chunk.MaximumCellColumnExclusive;
                     column++)
                {
                    var node = row * previous.Columns + column;
                    var current = _world.IsDiscFree(
                        previous.CellCenter(node), previous.NavigationRadius);
                    changed += current == walkable[node] ? 0 : 1;
                    walkable[node] = current;
                    resampled++;
                }
            }
        }

        var snapshot = NavigationConnectivityAnalyzer.BuildSnapshot(
            previous.WorldBounds,
            previous.CellSize,
            previous.Columns,
            previous.Rows,
            previous.NavigationRadius,
            _world.NavigationRevision,
            NavigationConnectivitySource.IncrementalRuntimeAnalysis,
            walkable);
        return new IncrementalConnectivityUpdate(
            snapshot, dirtyChunks, resampled, changed);
    }

    private void Validate(
        NavigationConnectivitySnapshot previous,
        SimRect changedWorldArea)
    {
        if (previous.WorldBounds != _layout.WorldBounds ||
            previous.Columns != _layout.Columns ||
            previous.Rows != _layout.Rows ||
            MathF.Abs(previous.CellSize - _layout.CellSize) > 0.0001f)
        {
            throw new ArgumentException(
                "Previous connectivity must match the bake chunk layout.",
                nameof(previous));
        }
        if (previous.WorldRevision != _world.NavigationRevision - 1)
        {
            throw new ArgumentException(
                "Incremental connectivity requires exactly one world revision.",
                nameof(previous));
        }
        if (!float.IsFinite(changedWorldArea.Min.X) ||
            !float.IsFinite(changedWorldArea.Min.Y) ||
            !float.IsFinite(changedWorldArea.Max.X) ||
            !float.IsFinite(changedWorldArea.Max.Y) ||
            changedWorldArea.Width <= 0f || changedWorldArea.Height <= 0f)
        {
            throw new ArgumentException(
                "Changed area must be finite and non-empty.",
                nameof(changedWorldArea));
        }
    }
}

public readonly record struct ConnectivityPreservationReport(
    bool Preserved,
    int BaselineComponentCount,
    int CandidateComponentCount,
    int SplitComponentCount,
    int DisconnectedCellCount,
    int FirstSplitComponentId);

public static class NavigationConnectivityComparer
{
    public static ConnectivityPreservationReport Compare(
        NavigationConnectivitySnapshot baseline,
        NavigationConnectivitySnapshot candidate)
    {
        if (baseline.WorldBounds != candidate.WorldBounds ||
            baseline.Columns != candidate.Columns ||
            baseline.Rows != candidate.Rows ||
            MathF.Abs(baseline.CellSize - candidate.CellSize) > 0.0001f ||
            MathF.Abs(
                baseline.NavigationRadius - candidate.NavigationRadius) > 0.0001f)
        {
            throw new ArgumentException(
                "Connectivity snapshots must share grid and clearance settings.");
        }

        var firstCandidateByBaseline = new int[baseline.ComponentCount];
        var survivingCellsByBaseline = new int[baseline.ComponentCount];
        var largestFragmentByBaseline = new int[baseline.ComponentCount];
        var split = new bool[baseline.ComponentCount];
        Array.Fill(firstCandidateByBaseline, -1);

        var fragmentCounts = new Dictionary<(int Baseline, int Candidate), int>();
        for (var node = 0; node < baseline.NodeCount; node++)
        {
            if (!baseline.IsWalkable(node) || !candidate.IsWalkable(node))
            {
                continue;
            }

            var baselineComponent = baseline.ComponentAt(node);
            var candidateComponent = candidate.ComponentAt(node);
            survivingCellsByBaseline[baselineComponent]++;
            if (firstCandidateByBaseline[baselineComponent] < 0)
            {
                firstCandidateByBaseline[baselineComponent] = candidateComponent;
            }
            else if (firstCandidateByBaseline[baselineComponent] !=
                     candidateComponent)
            {
                split[baselineComponent] = true;
            }

            var key = (baselineComponent, candidateComponent);
            fragmentCounts.TryGetValue(key, out var count);
            count++;
            fragmentCounts[key] = count;
            largestFragmentByBaseline[baselineComponent] = Math.Max(
                largestFragmentByBaseline[baselineComponent], count);
        }

        var splitCount = 0;
        var disconnectedCells = 0;
        var firstSplit = -1;
        for (var component = 0;
             component < baseline.ComponentCount;
             component++)
        {
            if (!split[component])
            {
                continue;
            }

            splitCount++;
            firstSplit = firstSplit < 0 ? component : firstSplit;
            disconnectedCells += survivingCellsByBaseline[component] -
                                 largestFragmentByBaseline[component];
        }

        return new ConnectivityPreservationReport(
            splitCount == 0,
            baseline.ComponentCount,
            candidate.ComponentCount,
            splitCount,
            disconnectedCells,
            firstSplit);
    }
}

public sealed class BuildingConnectivityGuard
{
    private readonly StaticWorld _world;
    private readonly NavigationConnectivityAnalyzer _analyzer;
    private readonly ClearanceBakeSnapshot? _staticBake;
    private readonly NavigationConnectivitySnapshot?[] _baselineByClass =
        new NavigationConnectivitySnapshot?[3];

    public BuildingConnectivityGuard(
        StaticWorld world,
        float cellSize = 16f,
        ClearanceBakeSnapshot? staticBake = null)
    {
        _world = world;
        _analyzer = new NavigationConnectivityAnalyzer(world, cellSize);
        _staticBake = staticBake;
    }

    public ConnectivityPreservationReport Evaluate(
        SimRect footprint,
        MovementClass movementClass)
    {
        var clearance = MovementClearance.ForClass(movementClass);
        var classIndex = (int)movementClass;
        var baseline = _baselineByClass[classIndex];
        if (baseline is null ||
            baseline.WorldRevision != _world.NavigationRevision ||
            MathF.Abs(
                baseline.NavigationRadius - clearance.NavigationRadius) > 0.0001f)
        {
            baseline = _staticBake is not null &&
                       _staticBake.IsCompatible(
                           _world,
                           _analyzer.CellSize,
                           clearance.NavigationRadius)
                ? _staticBake.CreateConnectivitySnapshot(movementClass)
                : _analyzer.Analyze(clearance.NavigationRadius);
            _baselineByClass[classIndex] = baseline;
        }

        var candidate = _analyzer.Analyze(
            clearance.NavigationRadius, footprint);
        return NavigationConnectivityComparer.Compare(baseline, candidate);
    }
}
