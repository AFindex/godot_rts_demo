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

public sealed class ValidatingFallbackPathProvider : IPathProvider
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

    public bool IsReady => _primary.IsReady && _fallback.IsReady;

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
}

public sealed class GridPathProvider : IPathProvider
{
    private static readonly (int Column, int Row)[] NeighborOffsets =
    [
        (1, 0), (0, 1), (-1, 0), (0, -1),
        (1, 1), (-1, 1), (-1, -1), (1, -1)
    ];

    private readonly StaticWorld _world;
    private readonly float _cellSize;
    private readonly int _columns;
    private readonly int _rows;
    private readonly ClearanceGridSnapshot[] _snapshots =
        [new(), new(), new()];

    public GridPathProvider(StaticWorld world, float cellSize = 20f)
    {
        _world = world;
        _cellSize = cellSize;
        _columns = Math.Max(1, (int)MathF.Ceiling(world.Bounds.Width / cellSize));
        _rows = Math.Max(1, (int)MathF.Ceiling(world.Bounds.Height / cellSize));
    }

    public bool IsReady => true;

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

        var snapshot = _snapshots[(int)clearance.Class];
        EnsureGridSnapshot(snapshot, navigationRadius);
        var startNode = FindNearestFreeNode(start, snapshot);
        var goalNode = FindNearestFreeNode(goal, snapshot);
        if (startNode < 0 || goalNode < 0 ||
            snapshot.Components[startNode] != snapshot.Components[goalNode])
        {
            return [];
        }

        var nodeCount = _columns * _rows;
        var costs = new float[nodeCount];
        var parents = new int[nodeCount];
        var closed = new bool[nodeCount];
        Array.Fill(costs, float.PositiveInfinity);
        Array.Fill(parents, -1);
        costs[startNode] = 0f;

        var open = new PriorityQueue<int, (float Score, int Node)>();
        open.Enqueue(startNode, (Heuristic(startNode, goalNode), startNode));
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
            var currentColumn = current % _columns;
            var currentRow = current / _columns;
            for (var offsetIndex = 0; offsetIndex < NeighborOffsets.Length; offsetIndex++)
            {
                var offset = NeighborOffsets[offsetIndex];
                var column = currentColumn + offset.Column;
                var row = currentRow + offset.Row;
                if ((uint)column >= (uint)_columns || (uint)row >= (uint)_rows)
                {
                    continue;
                }

                var neighbor = row * _columns + column;
                if (closed[neighbor] || !IsNodeFree(snapshot, neighbor))
                {
                    continue;
                }

                if (offset.Column != 0 && offset.Row != 0 &&
                    (!IsNodeFree(snapshot, currentRow * _columns + column) ||
                     !IsNodeFree(snapshot, row * _columns + currentColumn)))
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
                open.Enqueue(neighbor, (candidate + Heuristic(neighbor, goalNode), neighbor));
            }
        }

        return [];
    }

    private Vector2[] BuildPath(
        Vector2 start,
        Vector2 goal,
        int goalNode,
        int[] parents,
        ClearanceGridSnapshot snapshot,
        float navigationRadius)
    {
        var reversed = new List<Vector2>(32) { goal };
        for (var node = goalNode; node >= 0; node = parents[node])
        {
            reversed.Add(NodeCenter(node));
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
        ClearanceGridSnapshot snapshot)
    {
        var baseColumn = Math.Clamp(
            (int)MathF.Floor((point.X - _world.Bounds.Min.X) / _cellSize),
            0,
            _columns - 1);
        var baseRow = Math.Clamp(
            (int)MathF.Floor((point.Y - _world.Bounds.Min.Y) / _cellSize),
            0,
            _rows - 1);
        for (var ring = 0; ring <= 4; ring++)
        {
            for (var row = baseRow - ring; row <= baseRow + ring; row++)
            {
                for (var column = baseColumn - ring; column <= baseColumn + ring; column++)
                {
                    if ((uint)column >= (uint)_columns || (uint)row >= (uint)_rows ||
                        (ring > 0 && Math.Abs(column - baseColumn) < ring &&
                         Math.Abs(row - baseRow) < ring))
                    {
                        continue;
                    }

                    var node = row * _columns + column;
                    if (IsNodeFree(snapshot, node))
                    {
                        return node;
                    }
                }
            }
        }

        return -1;
    }

    private static bool IsNodeFree(ClearanceGridSnapshot snapshot, int node) =>
        snapshot.Walkable[node];

    private void EnsureGridSnapshot(
        ClearanceGridSnapshot snapshot,
        float navigationRadius)
    {
        if (snapshot.CachedRevision == _world.NavigationRevision &&
            MathF.Abs(snapshot.NavigationRadius - navigationRadius) <= 0.0001f &&
            snapshot.Walkable.Length == _columns * _rows)
        {
            return;
        }

        var nodeCount = _columns * _rows;
        snapshot.Walkable = new bool[nodeCount];
        snapshot.Components = new int[nodeCount];
        Array.Fill(snapshot.Components, -1);
        for (var node = 0; node < nodeCount; node++)
        {
            snapshot.Walkable[node] =
                _world.IsDiscFree(NodeCenter(node), navigationRadius);
        }

        var queue = new int[nodeCount];
        var component = 0;
        for (var seed = 0; seed < nodeCount; seed++)
        {
            if (!snapshot.Walkable[seed] || snapshot.Components[seed] >= 0)
            {
                continue;
            }

            var read = 0;
            var write = 0;
            queue[write++] = seed;
            snapshot.Components[seed] = component;
            while (read < write)
            {
                var current = queue[read++];
                var currentColumn = current % _columns;
                var currentRow = current / _columns;
                for (var offsetIndex = 0; offsetIndex < NeighborOffsets.Length; offsetIndex++)
                {
                    var offset = NeighborOffsets[offsetIndex];
                    var column = currentColumn + offset.Column;
                    var row = currentRow + offset.Row;
                    if ((uint)column >= (uint)_columns || (uint)row >= (uint)_rows)
                    {
                        continue;
                    }

                    var neighbor = row * _columns + column;
                    if (!snapshot.Walkable[neighbor] ||
                        snapshot.Components[neighbor] >= 0)
                    {
                        continue;
                    }

                    if (offset.Column != 0 && offset.Row != 0 &&
                        (!snapshot.Walkable[currentRow * _columns + column] ||
                         !snapshot.Walkable[row * _columns + currentColumn]))
                    {
                        continue;
                    }

                    snapshot.Components[neighbor] = component;
                    queue[write++] = neighbor;
                }
            }

            component++;
        }

        snapshot.CachedRevision = _world.NavigationRevision;
        snapshot.NavigationRadius = navigationRadius;
    }

    private Vector2 NodeCenter(int node)
    {
        var column = node % _columns;
        var row = node / _columns;
        return _world.Bounds.Min +
               new Vector2((column + 0.5f) * _cellSize, (row + 0.5f) * _cellSize);
    }

    private float Heuristic(int from, int to)
    {
        var fromColumn = from % _columns;
        var fromRow = from / _columns;
        var toColumn = to % _columns;
        var toRow = to / _columns;
        var deltaColumn = Math.Abs(fromColumn - toColumn);
        var deltaRow = Math.Abs(fromRow - toRow);
        var diagonal = Math.Min(deltaColumn, deltaRow);
        return diagonal * 1.41421356f + Math.Abs(deltaColumn - deltaRow);
    }

    private sealed class ClearanceGridSnapshot
    {
        public bool[] Walkable = [];
        public int[] Components = [];
        public int CachedRevision = -1;
        public float NavigationRadius;
    }
}
