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
        LastDirectCheckMilliseconds = 0d;
        LastSearchMilliseconds = 0d;
        LastSimplificationMilliseconds = 0d;
        LastExpandedNodes = 0;
        LastRawPathPoints = 0;
        LastSimplifiedPathPoints = 0;
    }

    public Vector2[] FindPath(
        Vector2 start,
        Vector2 goal,
        float navigationRadius)
    {
        var clearance = MovementClearance.FromPhysicalRadius(navigationRadius);
        navigationRadius = clearance.NavigationRadius;
        var directCheckStart = Stopwatch.GetTimestamp();
        var direct = _world.IsSegmentFree(start, goal, navigationRadius);
        LastDirectCheckMilliseconds +=
            Stopwatch.GetElapsedTime(directCheckStart).TotalMilliseconds;
        if (direct)
        {
            LastRawPathPoints += 2;
            LastSimplifiedPathPoints += 2;
            return [start, goal];
        }

        var snapshot = GetConnectivitySnapshot(clearance);
        var searchStart = Stopwatch.GetTimestamp();
        var startNode = FindNearestFreeNode(start, snapshot);
        var goalNode = FindNearestFreeNode(goal, snapshot);
        if (startNode < 0 || goalNode < 0 ||
            snapshot.ComponentAt(startNode) != snapshot.ComponentAt(goalNode))
        {
            LastSearchMilliseconds +=
                Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
            return [];
        }

        var nodeCount = snapshot.NodeCount;
        var costs = new float[nodeCount];
        var parents = new int[nodeCount];
        var closed = new bool[nodeCount];
        Array.Fill(costs, float.PositiveInfinity);
        Array.Fill(parents, -1);
        costs[startNode] = 0f;

        var open = new PriorityQueue<int, (float Score, int Node)>();
        open.Enqueue(
            startNode,
            (Heuristic(startNode, goalNode, snapshot.Columns), startNode));
        while (open.TryDequeue(out var current, out _))
        {
            if (closed[current])
            {
                continue;
            }

            if (current == goalNode)
            {
                LastSearchMilliseconds +=
                    Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
                var simplifyStart = Stopwatch.GetTimestamp();
                var path = BuildPath(
                    start, goal, goalNode, parents, snapshot, navigationRadius);
                LastSimplificationMilliseconds +=
                    Stopwatch.GetElapsedTime(simplifyStart).TotalMilliseconds;
                return path;
            }

            closed[current] = true;
            LastExpandedNodes++;
            var currentColumn = current % snapshot.Columns;
            var currentRow = current / snapshot.Columns;
            var offsets = NavigationConnectivityAnalyzer.NeighborOffsets;
            for (var offsetIndex = 0; offsetIndex < offsets.Length; offsetIndex++)
            {
                var offset = offsets[offsetIndex];
                var column = currentColumn + offset.Column;
                var row = currentRow + offset.Row;
                if ((uint)column >= (uint)snapshot.Columns ||
                    (uint)row >= (uint)snapshot.Rows)
                {
                    continue;
                }

                var neighbor = row * snapshot.Columns + column;
                if (closed[neighbor] || !IsNodeFree(snapshot, neighbor))
                {
                    continue;
                }

                if (offset.Column != 0 && offset.Row != 0 &&
                    (!IsNodeFree(
                         snapshot, currentRow * snapshot.Columns + column) ||
                     !IsNodeFree(
                         snapshot, row * snapshot.Columns + currentColumn)))
                {
                    continue;
                }

                var stepCost = offset.Column == 0 || offset.Row == 0 ? 1f : 1.41421356f;
                var candidate = costs[current] + stepCost;
                if (candidate >= costs[neighbor] - 0.0001f)
                {
                    continue;
                }

                costs[neighbor] = candidate;
                parents[neighbor] = current;
                open.Enqueue(
                    neighbor,
                    (candidate + Heuristic(
                        neighbor, goalNode, snapshot.Columns), neighbor));
            }
        }

        LastSearchMilliseconds +=
            Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
        return [];
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
        NavigationConnectivitySnapshot snapshot)
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
        for (var ring = 0; ring <= 4; ring++)
        {
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
                    if (IsNodeFree(snapshot, node))
                    {
                        return node;
                    }
                }
            }
        }

        return -1;
    }

    private static bool IsNodeFree(
        NavigationConnectivitySnapshot snapshot,
        int node) => snapshot.IsWalkable(node);

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
            var update = _incrementalUpdater.Update(snapshot, changedBounds);
            snapshot = update.Snapshot;
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
        ClearanceBakeReloads++;
    }

}
