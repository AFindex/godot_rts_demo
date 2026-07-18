using System.Numerics;
using System.Diagnostics;

namespace RtsDemo.Simulation;

public interface IPathProvider
{
    bool IsReady { get; }
    Vector2[] FindPath(Vector2 start, Vector2 goal, float navigationRadius);
}

public sealed class UnitPath
{
    public UnitPath(Vector2[] points, int commandVersion)
    {
        Points = points;
        CommandVersion = commandVersion;
        Cursor = points.Length > 1 ? 1 : 0;
    }

    public Vector2[] Points { get; }
    public int CommandVersion { get; }
    public int Cursor { get; set; }
}

public readonly record struct PathRequest(int UnitIndex, int CommandVersion);

public sealed class StraightLinePathProvider : IPathProvider
{
    public bool IsReady => true;

    public Vector2[] FindPath(
        Vector2 start,
        Vector2 goal,
        float navigationRadius) => [start, goal];
}

public sealed class ValidatingFallbackPathProvider :
    IPathProvider,
    IClearanceBakeReloadTarget
{
    private readonly IPathProvider _primary;
    private readonly IPathProvider _fallback;
    private readonly StaticWorld _world;

    public ValidatingFallbackPathProvider(
        IPathProvider primary,
        IPathProvider fallback,
        StaticWorld world)
    {
        _primary = primary;
        _fallback = fallback;
        _world = world;
    }

    public bool IsReady => _primary.IsReady || _fallback.IsReady;

    public Vector2[] FindPath(
        Vector2 start,
        Vector2 goal,
        float navigationRadius)
    {
        var primaryPath = _primary.FindPath(start, goal, navigationRadius);
        if (PathIsFree(primaryPath, navigationRadius))
        {
            return primaryPath;
        }

        return _fallback.FindPath(start, goal, navigationRadius);
    }

    private bool PathIsFree(ReadOnlySpan<Vector2> path, float navigationRadius)
    {
        if (path.Length < 2)
        {
            return false;
        }

        for (var index = 1; index < path.Length; index++)
        {
            if (!_world.IsSegmentFree(
                    path[index - 1], path[index], navigationRadius))
            {
                return false;
            }
        }

        return true;
    }

    ClearanceBakeCommitValidation IClearanceBakeReloadTarget.ValidateClearanceBake(
        ClearanceBakeSnapshot candidate) =>
        _fallback is IClearanceBakeReloadTarget target
            ? target.ValidateClearanceBake(candidate)
            : new ClearanceBakeCommitValidation(
                ClearanceBakeCommitCode.UnsupportedPathProvider,
                "Fallback provider does not support Bake replacement.");

    void IClearanceBakeReloadTarget.CommitClearanceBake(
        ClearanceBakeSnapshot candidate)
    {
        ((IClearanceBakeReloadTarget)_fallback).CommitClearanceBake(candidate);
    }
}

public sealed class GridPathProvider : IPathProvider, IClearanceBakeReloadTarget
{
    private readonly StaticWorld _world;
    private readonly NavigationConnectivityAnalyzer _connectivityAnalyzer;
    private ClearanceBakeSnapshot? _staticBake;
    private IncrementalNavigationConnectivityUpdater? _incrementalUpdater;
    private readonly NavigationConnectivitySnapshot?[] _snapshots =
        new NavigationConnectivitySnapshot?[3];
    private readonly NavigationConnectivitySnapshot?[] _diagnosticSnapshots =
        new NavigationConnectivitySnapshot?[3];
    private readonly EdgeValidationCache?[] _edgeValidationCaches =
        new EdgeValidationCache?[3];
    private float[] _searchCosts = [];
    private int[] _searchParents = [];
    private int[] _searchVisitedGenerations = [];
    private int[] _searchClosedGenerations = [];
    private int _searchGeneration;
    private readonly PriorityQueue<int, (float Score, int Node)> _searchOpen = new();
    private readonly Dictionary<PathQueryKey, Vector2[]> _completedPathCache =
        new(256);
    private readonly Queue<PathQueryKey> _completedPathCacheOrder = new(256);
    private int _completedPathCacheRevision = -1;

    public GridPathProvider(
        StaticWorld world,
        float cellSize = 16f,
        ClearanceBakeSnapshot? staticBake = null)
    {
        _world = world;
        _connectivityAnalyzer = new NavigationConnectivityAnalyzer(world, cellSize);
        _staticBake = staticBake;
        if (staticBake is not null &&
            staticBake.WorldBounds == world.Bounds &&
            MathF.Abs(staticBake.CellSize - cellSize) <= 0.0001f)
        {
            _incrementalUpdater = new IncrementalNavigationConnectivityUpdater(
                world, staticBake);
        }
    }

    public bool IsReady => true;
    public int IncrementalConnectivityUpdates { get; private set; }
    public int IncrementalConnectivityResampledCells { get; private set; }
    public int ClearanceBakeReloads { get; private set; }
    public int FullConnectivityRebuilds { get; private set; }
    public double LastConnectivityRefreshMilliseconds { get; private set; }
    public double LastDirectCheckMilliseconds { get; private set; }
    public double LastSearchMilliseconds { get; private set; }
    public double LastSimplificationMilliseconds { get; private set; }
    public int LastExpandedNodes { get; private set; }
    public int LastRawPathPoints { get; private set; }
    public int LastSimplifiedPathPoints { get; private set; }
    public int LastEdgeCacheInvalidatedStates { get; private set; }
    public int LastEdgeCacheFullClears { get; private set; }
    public int LastCompletedPathCacheHits { get; private set; }
    public ulong ClearanceBakeHash => _staticBake?.StableHash ?? 0UL;

    public void WarmConnectivitySnapshots()
    {
        _ = GetConnectivitySnapshot(
            MovementClearance.ForClass(MovementClass.Small));
        _ = GetConnectivitySnapshot(
            MovementClearance.ForClass(MovementClass.Medium));
        _ = GetConnectivitySnapshot(
            MovementClearance.ForClass(MovementClass.Large));
    }

    public void ResetPathDiagnostics()
    {
        LastConnectivityRefreshMilliseconds = 0d;
        LastDirectCheckMilliseconds = 0d;
        LastSearchMilliseconds = 0d;
        LastSimplificationMilliseconds = 0d;
        LastExpandedNodes = 0;
        LastRawPathPoints = 0;
        LastSimplifiedPathPoints = 0;
        LastEdgeCacheInvalidatedStates = 0;
        LastEdgeCacheFullClears = 0;
        LastCompletedPathCacheHits = 0;
    }

    /// <summary>
    /// Returns the same connectivity snapshot used by pathfinding. This is an
    /// explicit diagnostics hook: callers may trigger an incremental refresh
    /// after a navigation revision, so it must stay out of normal UI updates.
    /// </summary>
    public NavigationConnectivitySnapshot GetConnectivitySnapshotForDiagnostics(
        MovementClass movementClass)
    {
        if (!Enum.IsDefined(movementClass))
            throw new ArgumentOutOfRangeException(nameof(movementClass));
        var clearance = MovementClearance.ForClass(movementClass);
        var runtime = GetConnectivitySnapshot(clearance);
        if (runtime.Source !=
            NavigationConnectivitySource.IncrementalRuntimePathfinding)
            return runtime;
        var cached = _diagnosticSnapshots[(int)movementClass];
        if (cached is not null &&
            cached.WorldRevision == _world.NavigationRevision)
            return cached;
        cached = _connectivityAnalyzer.Analyze(clearance.NavigationRadius);
        _diagnosticSnapshots[(int)movementClass] = cached;
        return cached;
    }

    /// <summary>
    /// Finds a nearby clearance-safe grid anchor that a physically valid unit
    /// can reach while leaving an obstacle's navigation margin. This replaces
    /// hundreds of radial world probes in the common construction/production
    /// displacement case while preserving the exact physical segment check.
    /// </summary>
    public bool TryFindStartEscape(
        Vector2 start,
        Vector2 goal,
        float physicalRadius,
        float navigationRadius,
        out Vector2 escape)
    {
        escape = default;
        var clearance = MovementClearance.FromPhysicalRadius(navigationRadius);
        navigationRadius = clearance.NavigationRadius;
        var snapshot = GetConnectivitySnapshot(clearance);
        var baseColumn = Math.Clamp(
            (int)MathF.Floor(
                (start.X - snapshot.WorldBounds.Min.X) / snapshot.CellSize),
            0,
            snapshot.Columns - 1);
        var baseRow = Math.Clamp(
            (int)MathF.Floor(
                (start.Y - snapshot.WorldBounds.Min.Y) / snapshot.CellSize),
            0,
            snapshot.Rows - 1);
        for (var ring = 0; ring <= 4; ring++)
        {
            var best = default(Vector2);
            var bestGoalDistance = float.PositiveInfinity;
            var bestNode = int.MaxValue;
            for (var row = baseRow - ring; row <= baseRow + ring; row++)
            {
                for (var column = baseColumn - ring;
                     column <= baseColumn + ring;
                     column++)
                {
                    if ((uint)column >= (uint)snapshot.Columns ||
                        (uint)row >= (uint)snapshot.Rows ||
                        ring > 0 &&
                        Math.Abs(column - baseColumn) < ring &&
                        Math.Abs(row - baseRow) < ring)
                        continue;
                    var node = row * snapshot.Columns + column;
                    if (!IsNodeFree(snapshot, node)) continue;
                    var candidate = snapshot.CellCenter(node);
                    if (!_world.IsSegmentFree(
                            start, candidate, physicalRadius))
                        continue;
                    var goalDistance = Vector2.DistanceSquared(candidate, goal);
                    if (goalDistance > bestGoalDistance ||
                        goalDistance == bestGoalDistance && node >= bestNode)
                        continue;
                    best = candidate;
                    bestGoalDistance = goalDistance;
                    bestNode = node;
                }
            }
            if (!float.IsFinite(bestGoalDistance)) continue;
            escape = best;
            return true;
        }
        return false;
    }

    public Vector2[] FindPath(
        Vector2 start,
        Vector2 goal,
        float navigationRadius)
    {
        var clearance = MovementClearance.FromPhysicalRadius(navigationRadius);
        navigationRadius = clearance.NavigationRadius;
        RefreshCompletedPathCacheRevision();
        var queryKey = new PathQueryKey(
            start, goal, navigationRadius, _world.NavigationRevision);
        if (_completedPathCache.TryGetValue(queryKey, out var cachedPath))
        {
            LastCompletedPathCacheHits++;
            LastRawPathPoints += cachedPath.Length;
            LastSimplifiedPathPoints += cachedPath.Length;
            return cachedPath;
        }
        var directCheckStart = Stopwatch.GetTimestamp();
        var direct = _world.IsSegmentFree(start, goal, navigationRadius);
        LastDirectCheckMilliseconds +=
            Stopwatch.GetElapsedTime(directCheckStart).TotalMilliseconds;
        if (direct)
        {
            LastRawPathPoints += 2;
            LastSimplifiedPathPoints += 2;
            return CacheCompletedPath(queryKey, [start, goal]);
        }

        var snapshot = GetConnectivitySnapshot(clearance);
        var edgeValidation = GetEdgeValidationCache(clearance, snapshot);
        var searchStart = Stopwatch.GetTimestamp();
        var startNode = FindNearestFreeNode(
            start, snapshot, navigationRadius);
        var goalNode = FindNearestFreeNode(
            goal, snapshot, navigationRadius);
        if (startNode < 0 || goalNode < 0 ||
            snapshot.ComponentAt(startNode) != snapshot.ComponentAt(goalNode))
        {
            LastSearchMilliseconds +=
                Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
            return CacheCompletedPath(queryKey, []);
        }

        while (true)
        {
            var generation = BeginSearch(snapshot.NodeCount);
            _searchVisitedGenerations[startNode] = generation;
            _searchCosts[startNode] = 0f;
            _searchParents[startNode] = -1;
            _searchOpen.Enqueue(
                startNode,
                (Heuristic(startNode, goalNode, snapshot.Columns), startNode));
            var retryWithRejectedEdge = false;
            while (_searchOpen.TryDequeue(out var current, out _))
            {
                if (_searchClosedGenerations[current] == generation)
                    continue;

                if (current == goalNode)
                {
                    if (!ValidateParentTransitions(
                            startNode,
                            goalNode,
                            _searchParents,
                            snapshot,
                            edgeValidation,
                            navigationRadius))
                    {
                        retryWithRejectedEdge = true;
                        break;
                    }
                    LastSearchMilliseconds +=
                        Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
                    var simplifyStart = Stopwatch.GetTimestamp();
                    var path = BuildPath(
                        start, goal, goalNode, _searchParents, snapshot,
                        navigationRadius);
                    LastSimplificationMilliseconds +=
                        Stopwatch.GetElapsedTime(simplifyStart).TotalMilliseconds;
                    return CacheCompletedPath(queryKey, path);
                }

                _searchClosedGenerations[current] = generation;
                LastExpandedNodes++;
                var currentColumn = current % snapshot.Columns;
                var currentRow = current / snapshot.Columns;
                var offsets = NavigationConnectivityAnalyzer.NeighborOffsets;
                for (var offsetIndex = 0;
                     offsetIndex < offsets.Length;
                     offsetIndex++)
                {
                    var offset = offsets[offsetIndex];
                    var column = currentColumn + offset.Column;
                    var row = currentRow + offset.Row;
                    if ((uint)column >= (uint)snapshot.Columns ||
                        (uint)row >= (uint)snapshot.Rows)
                        continue;

                    var neighbor = row * snapshot.Columns + column;
                    if (_searchClosedGenerations[neighbor] == generation ||
                        !IsNodeFree(snapshot, neighbor))
                        continue;

                    if (offset.Column != 0 && offset.Row != 0 &&
                        (!IsNodeFree(
                             snapshot,
                             currentRow * snapshot.Columns + column) ||
                         !IsNodeFree(
                             snapshot,
                             row * snapshot.Columns + currentColumn)))
                        continue;

                    if (IsTransitionKnownBlocked(
                            edgeValidation, current, offsetIndex))
                        continue;

                    var stepCost = offset.Column == 0 || offset.Row == 0
                        ? 1f
                        : 1.41421356f;
                    var candidate = _searchCosts[current] + stepCost;
                    if (_searchVisitedGenerations[neighbor] == generation &&
                        candidate >= _searchCosts[neighbor] - 0.0001f)
                        continue;

                    _searchVisitedGenerations[neighbor] = generation;
                    _searchCosts[neighbor] = candidate;
                    _searchParents[neighbor] = current;
                    _searchOpen.Enqueue(
                        neighbor,
                        (candidate + Heuristic(
                            neighbor, goalNode, snapshot.Columns), neighbor));
                }
            }
            if (retryWithRejectedEdge) continue;
            LastSearchMilliseconds +=
                Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
            return CacheCompletedPath(queryKey, []);
        }
    }

    private void RefreshCompletedPathCacheRevision()
    {
        var revision = _world.NavigationRevision;
        if (_completedPathCacheRevision == revision) return;
        _completedPathCache.Clear();
        _completedPathCacheOrder.Clear();
        _completedPathCacheRevision = revision;
    }

    private Vector2[] CacheCompletedPath(
        PathQueryKey key,
        Vector2[] path)
    {
        const int capacity = 256;
        if (_completedPathCache.ContainsKey(key))
        {
            _completedPathCache[key] = path;
            return path;
        }
        while (_completedPathCache.Count >= capacity &&
               _completedPathCacheOrder.TryDequeue(out var expired))
            _completedPathCache.Remove(expired);
        _completedPathCache.Add(key, path);
        _completedPathCacheOrder.Enqueue(key);
        return path;
    }

    private static bool IsTransitionKnownBlocked(
        EdgeValidationCache cache,
        int from,
        int offsetIndex)
    {
        var offsetCount =
            NavigationConnectivityAnalyzer.NeighborOffsets.Length;
        return cache.States[from * offsetCount + offsetIndex] == 1;
    }

    private bool ValidateParentTransitions(
        int startNode,
        int goalNode,
        int[] parents,
        NavigationConnectivitySnapshot snapshot,
        EdgeValidationCache cache,
        float navigationRadius)
    {
        for (var node = goalNode; node != startNode;)
        {
            var parent = parents[node];
            if (parent < 0) return false;
            var offsetIndex = TransitionOffsetIndex(
                parent, node, snapshot.Columns);
            if (offsetIndex < 0 ||
                !IsTransitionFree(
                    snapshot,
                    cache,
                    parent,
                    node,
                    offsetIndex,
                    navigationRadius))
                return false;
            node = parent;
        }
        return true;
    }

    private static int TransitionOffsetIndex(int from, int to, int columns)
    {
        var fromColumn = from % columns;
        var fromRow = from / columns;
        var toColumn = to % columns;
        var toRow = to / columns;
        var deltaColumn = toColumn - fromColumn;
        var deltaRow = toRow - fromRow;
        var offsets = NavigationConnectivityAnalyzer.NeighborOffsets;
        for (var index = 0; index < offsets.Length; index++)
        {
            if (offsets[index].Column == deltaColumn &&
                offsets[index].Row == deltaRow)
                return index;
        }
        return -1;
    }

    private int BeginSearch(int nodeCount)
    {
        if (_searchCosts.Length < nodeCount)
        {
            _searchCosts = new float[nodeCount];
            _searchParents = new int[nodeCount];
            _searchVisitedGenerations = new int[nodeCount];
            _searchClosedGenerations = new int[nodeCount];
            _searchGeneration = 0;
        }
        if (_searchGeneration == int.MaxValue)
        {
            Array.Clear(_searchVisitedGenerations);
            Array.Clear(_searchClosedGenerations);
            _searchGeneration = 0;
        }
        _searchOpen.Clear();
        return ++_searchGeneration;
    }

    private Vector2[] BuildPath(
        Vector2 start,
        Vector2 goal,
        int goalNode,
        int[] parents,
        NavigationConnectivitySnapshot snapshot,
        float navigationRadius)
    {
        var reversed = new List<Vector2>(32) { goal };
        for (var node = goalNode; node >= 0; node = parents[node])
        {
            reversed.Add(snapshot.CellCenter(node));
        }

        reversed.Add(start);
        reversed.Reverse();
        LastRawPathPoints += reversed.Count;
        var simplified = new List<Vector2>(reversed.Count) { reversed[0] };
        var anchor = 0;
        while (anchor < reversed.Count - 1)
        {
            var furthest = anchor + 1;
            var blocked = -1;
            for (var step = 2; anchor + step < reversed.Count; step *= 2)
            {
                var candidate = anchor + step;
                if (!_world.IsSegmentFree(
                        reversed[anchor], reversed[candidate], navigationRadius))
                {
                    blocked = candidate;
                    break;
                }
                furthest = candidate;
                if (step > int.MaxValue / 2)
                    break;
            }

            var last = reversed.Count - 1;
            if (blocked < 0 && furthest < last)
            {
                if (_world.IsSegmentFree(
                        reversed[anchor], reversed[last], navigationRadius))
                    furthest = last;
                else
                    blocked = last;
            }

            if (blocked > furthest + 1)
            {
                var minimum = furthest + 1;
                var maximum = blocked - 1;
                while (minimum <= maximum)
                {
                    var candidate = minimum + (maximum - minimum) / 2;
                    if (_world.IsSegmentFree(
                            reversed[anchor], reversed[candidate],
                            navigationRadius))
                    {
                        furthest = candidate;
                        minimum = candidate + 1;
                    }
                    else
                    {
                        maximum = candidate - 1;
                    }
                }
            }
            simplified.Add(reversed[furthest]);
            anchor = furthest;
        }

        LastSimplifiedPathPoints += simplified.Count;
        return simplified.ToArray();
    }

    private int FindNearestFreeNode(
        Vector2 point,
        NavigationConnectivitySnapshot snapshot,
        float navigationRadius)
    {
        var baseColumn = Math.Clamp(
            (int)MathF.Floor(
                (point.X - snapshot.WorldBounds.Min.X) / snapshot.CellSize),
            0,
            snapshot.Columns - 1);
        var baseRow = Math.Clamp(
            (int)MathF.Floor(
                (point.Y - snapshot.WorldBounds.Min.Y) / snapshot.CellSize),
            0,
            snapshot.Rows - 1);
        var requiresDirectConnection =
            _world.IsDiscFree(point, navigationRadius);
        for (var ring = 0; ring <= 4; ring++)
        {
            var bestNode = -1;
            var bestDistanceSquared = float.PositiveInfinity;
            for (var row = baseRow - ring; row <= baseRow + ring; row++)
            {
                for (var column = baseColumn - ring; column <= baseColumn + ring; column++)
                {
                    if ((uint)column >= (uint)snapshot.Columns ||
                        (uint)row >= (uint)snapshot.Rows ||
                        (ring > 0 && Math.Abs(column - baseColumn) < ring &&
                         Math.Abs(row - baseRow) < ring))
                    {
                        continue;
                    }

                    var node = row * snapshot.Columns + column;
                    if (!IsNodeFree(snapshot, node))
                    {
                        continue;
                    }
                    var center = snapshot.CellCenter(node);
                    if (requiresDirectConnection &&
                        !_world.IsSegmentFree(
                            point, center, navigationRadius))
                    {
                        // A free cell on the opposite side of a nearby wall is
                        // not a valid endpoint anchor. Returning it makes A*
                        // produce a valid grid route with an impossible first
                        // or final segment.
                        continue;
                    }
                    var distanceSquared = Vector2.DistanceSquared(point, center);
                    if (distanceSquared > bestDistanceSquared ||
                        distanceSquared == bestDistanceSquared &&
                        node >= bestNode)
                    {
                        continue;
                    }
                    bestNode = node;
                    bestDistanceSquared = distanceSquared;
                }
            }
            if (bestNode >= 0)
            {
                return bestNode;
            }
        }

        return -1;
    }

    private static bool IsNodeFree(
        NavigationConnectivitySnapshot snapshot,
        int node) => snapshot.IsWalkable(node);

    private EdgeValidationCache GetEdgeValidationCache(
        MovementClearanceProfile clearance,
        NavigationConnectivitySnapshot snapshot)
    {
        var classIndex = (int)clearance.Class;
        var cache = _edgeValidationCaches[classIndex];
        if (cache is null || cache.NodeCount != snapshot.NodeCount ||
            MathF.Abs(cache.NavigationRadius -
                      clearance.NavigationRadius) > 0.0001f)
        {
            cache = new EdgeValidationCache(
                snapshot.WorldRevision,
                snapshot.NodeCount,
                clearance.NavigationRadius,
                new byte[snapshot.NodeCount *
                         NavigationConnectivityAnalyzer.NeighborOffsets.Length]);
            _edgeValidationCaches[classIndex] = cache;
        }
        else if (cache.WorldRevision != snapshot.WorldRevision)
        {
            Array.Clear(cache.States);
            cache.WorldRevision = snapshot.WorldRevision;
            LastEdgeCacheFullClears++;
            LastEdgeCacheInvalidatedStates += cache.States.Length;
        }
        return cache;
    }

    private bool IsTransitionFree(
        NavigationConnectivitySnapshot snapshot,
        EdgeValidationCache cache,
        int from,
        int to,
        int offsetIndex,
        float navigationRadius)
    {
        var offsetCount =
            NavigationConnectivityAnalyzer.NeighborOffsets.Length;
        var edgeIndex = from * offsetCount + offsetIndex;
        var state = cache.States[edgeIndex];
        if (state != 0) return state == 2;
        var free = _world.IsSegmentFree(
            snapshot.CellCenter(from),
            snapshot.CellCenter(to),
            navigationRadius);
        cache.States[edgeIndex] = free ? (byte)2 : (byte)1;
        cache.States[to * offsetCount + ReverseOffset(offsetIndex)] =
            free ? (byte)2 : (byte)1;
        return free;
    }

    private static int ReverseOffset(int offsetIndex) => offsetIndex switch
    {
        0 => 2,
        1 => 3,
        2 => 0,
        3 => 1,
        4 => 6,
        5 => 7,
        6 => 4,
        7 => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(offsetIndex))
    };

    private NavigationConnectivitySnapshot GetConnectivitySnapshot(
        MovementClearanceProfile clearance)
    {
        var classIndex = (int)clearance.Class;
        var snapshot = _snapshots[classIndex];
        if (snapshot is not null &&
            snapshot.WorldRevision == _world.NavigationRevision &&
            MathF.Abs(
                snapshot.NavigationRadius - clearance.NavigationRadius) <= 0.0001f)
        {
            return snapshot;
        }

        if (snapshot is not null &&
            _incrementalUpdater is not null &&
            MathF.Abs(
                snapshot.NavigationRadius - clearance.NavigationRadius) <= 0.0001f &&
            _world.DynamicOccupancy.TryGetChangesSince(
                snapshot.WorldRevision, out var changedBounds))
        {
            var refreshStart = Stopwatch.GetTimestamp();
            var update = _incrementalUpdater.UpdateForPathfinding(
                snapshot, changedBounds);
            snapshot = update.Snapshot;
            for (var changeIndex = 0;
                 changeIndex < changedBounds.Length;
                 changeIndex++)
            {
                InvalidateChangedEdges(
                    classIndex, snapshot, clearance.NavigationRadius,
                    changedBounds[changeIndex]);
            }
            IncrementalConnectivityUpdates++;
            IncrementalConnectivityResampledCells += update.ResampledCells;
            LastConnectivityRefreshMilliseconds =
                Stopwatch.GetElapsedTime(refreshStart).TotalMilliseconds;
        }
        else
        {
            var refreshStart = Stopwatch.GetTimestamp();
            snapshot = _staticBake is not null &&
                       _staticBake.IsCompatible(
                           _world,
                           _connectivityAnalyzer.CellSize,
                           clearance.NavigationRadius)
                ? _staticBake.CreateConnectivitySnapshot(clearance.Class)
                : _connectivityAnalyzer.Analyze(clearance.NavigationRadius);
            FullConnectivityRebuilds++;
            LastConnectivityRefreshMilliseconds =
                Stopwatch.GetElapsedTime(refreshStart).TotalMilliseconds;
        }
        _snapshots[classIndex] = snapshot;
        return snapshot;
    }

    private void InvalidateChangedEdges(
        int classIndex,
        NavigationConnectivitySnapshot snapshot,
        float navigationRadius,
        SimRect changedBounds)
    {
        var cache = _edgeValidationCaches[classIndex];
        if (cache is null || cache.NodeCount != snapshot.NodeCount ||
            MathF.Abs(cache.NavigationRadius - navigationRadius) > 0.0001f)
            return;

        var affected = changedBounds.Expanded(
            navigationRadius + snapshot.CellSize * 1.5f);
        var minimumColumn = Math.Clamp(
            (int)MathF.Floor(
                (affected.Min.X - snapshot.WorldBounds.Min.X) /
                snapshot.CellSize),
            0,
            snapshot.Columns - 1);
        var maximumColumn = Math.Clamp(
            (int)MathF.Floor(
                (affected.Max.X - snapshot.WorldBounds.Min.X) /
                snapshot.CellSize),
            0,
            snapshot.Columns - 1);
        var minimumRow = Math.Clamp(
            (int)MathF.Floor(
                (affected.Min.Y - snapshot.WorldBounds.Min.Y) /
                snapshot.CellSize),
            0,
            snapshot.Rows - 1);
        var maximumRow = Math.Clamp(
            (int)MathF.Floor(
                (affected.Max.Y - snapshot.WorldBounds.Min.Y) /
                snapshot.CellSize),
            0,
            snapshot.Rows - 1);
        var offsets = NavigationConnectivityAnalyzer.NeighborOffsets;
        var offsetCount = offsets.Length;
        var invalidated = 0;
        for (var row = minimumRow; row <= maximumRow; row++)
        {
            for (var column = minimumColumn;
                 column <= maximumColumn;
                 column++)
            {
                var node = row * snapshot.Columns + column;
                for (var offsetIndex = 0;
                     offsetIndex < offsetCount;
                     offsetIndex++)
                {
                    var edgeIndex = node * offsetCount + offsetIndex;
                    if (cache.States[edgeIndex] != 0)
                    {
                        cache.States[edgeIndex] = 0;
                        invalidated++;
                    }
                    var offset = offsets[offsetIndex];
                    var neighborColumn = column + offset.Column;
                    var neighborRow = row + offset.Row;
                    if ((uint)neighborColumn >= (uint)snapshot.Columns ||
                        (uint)neighborRow >= (uint)snapshot.Rows)
                        continue;
                    var neighbor =
                        neighborRow * snapshot.Columns + neighborColumn;
                    var reverseIndex = neighbor * offsetCount +
                                       ReverseOffset(offsetIndex);
                    if (cache.States[reverseIndex] == 0) continue;
                    cache.States[reverseIndex] = 0;
                    invalidated++;
                }
            }
        }
        cache.WorldRevision = snapshot.WorldRevision;
        LastEdgeCacheInvalidatedStates += invalidated;
    }

    private static float Heuristic(int from, int to, int columns)
    {
        var fromColumn = from % columns;
        var fromRow = from / columns;
        var toColumn = to % columns;
        var toRow = to / columns;
        var deltaColumn = Math.Abs(fromColumn - toColumn);
        var deltaRow = Math.Abs(fromRow - toRow);
        var diagonal = Math.Min(deltaColumn, deltaRow);
        return diagonal * 1.41421356f + Math.Abs(deltaColumn - deltaRow);
    }

    ClearanceBakeCommitValidation IClearanceBakeReloadTarget.ValidateClearanceBake(
        ClearanceBakeSnapshot candidate) =>
        ClearanceBakeReloadValidator.Validate(
            _staticBake,
            candidate,
            _world,
            _connectivityAnalyzer.CellSize);

    void IClearanceBakeReloadTarget.CommitClearanceBake(
        ClearanceBakeSnapshot candidate)
    {
        _staticBake = candidate;
        _incrementalUpdater = new IncrementalNavigationConnectivityUpdater(
            _world, candidate);
        Array.Clear(_snapshots);
        Array.Clear(_diagnosticSnapshots);
        Array.Clear(_edgeValidationCaches);
        _completedPathCache.Clear();
        _completedPathCacheOrder.Clear();
        _completedPathCacheRevision = -1;
        ClearanceBakeReloads++;
    }

    private sealed class EdgeValidationCache(
        int worldRevision,
        int nodeCount,
        float navigationRadius,
        byte[] states)
    {
        public int WorldRevision { get; set; } = worldRevision;
        public int NodeCount { get; } = nodeCount;
        public float NavigationRadius { get; } = navigationRadius;
        public byte[] States { get; } = states;
    }

    private readonly record struct PathQueryKey(
        Vector2 Start,
        Vector2 Goal,
        float NavigationRadius,
        int WorldRevision);

}
