using System.Numerics;

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
    public ulong ClearanceBakeHash => _staticBake?.StableHash ?? 0UL;

    public Vector2[] FindPath(
        Vector2 start,
        Vector2 goal,
        float navigationRadius)
    {
        var clearance = MovementClearance.FromPhysicalRadius(navigationRadius);
        navigationRadius = clearance.NavigationRadius;
        if (_world.IsSegmentFree(start, goal, navigationRadius))
        {
            return [start, goal];
        }

        var snapshot = GetConnectivitySnapshot(clearance);
        var startNode = FindNearestFreeNode(start, snapshot);
        var goalNode = FindNearestFreeNode(goal, snapshot);
        if (startNode < 0 || goalNode < 0 ||
            snapshot.ComponentAt(startNode) != snapshot.ComponentAt(goalNode))
        {
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
                return BuildPath(
                    start, goal, goalNode, parents, snapshot, navigationRadius);
            }

            closed[current] = true;
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
        var simplified = new List<Vector2>(reversed.Count) { reversed[0] };
        var anchor = 0;
        while (anchor < reversed.Count - 1)
        {
            var next = reversed.Count - 1;
            while (next > anchor + 1 &&
                   !_world.IsSegmentFree(
                       reversed[anchor], reversed[next], navigationRadius))
            {
                next--;
            }

            simplified.Add(reversed[next]);
            anchor = next;
        }

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
            _world.DynamicOccupancy.TryGetSingleChangeSince(
                snapshot.WorldRevision, out var changedBounds))
        {
            var update = _incrementalUpdater.Update(snapshot, changedBounds);
            snapshot = update.Snapshot;
            IncrementalConnectivityUpdates++;
            IncrementalConnectivityResampledCells += update.ResampledCells;
        }
        else
        {
            snapshot = _staticBake is not null &&
                       _staticBake.IsCompatible(
                           _world,
                           _connectivityAnalyzer.CellSize,
                           clearance.NavigationRadius)
                ? _staticBake.CreateConnectivitySnapshot(clearance.Class)
                : _connectivityAnalyzer.Analyze(clearance.NavigationRadius);
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
