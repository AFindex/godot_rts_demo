using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct PortalNode(int Id, Vector2 Position, string Name);

public readonly record struct PortalEdge(
    int FromNode,
    int ToNode,
    float Width,
    int ChokeId = -1);

public readonly record struct GroupRoutePlan(
    Vector2[] Waypoints,
    int[] ChokeIds,
    float Cost)
{
    public static GroupRoutePlan Empty { get; } = new([], [], 0f);
}

public interface IGroupRoutePlanner
{
    GroupRoutePlan Plan(Vector2 start, Vector2 goal, float agentRadius);
}

/// <summary>
/// Optional notification for route planners that cache data derived from
/// dynamic world occupancy. The simulation sends the exact changed bounds
/// after a hard footprint is committed or removed.
/// </summary>
public interface IGroupRouteNavigationChangeSink
{
    void OnNavigationChanged(SimRect changedBounds);

    void OnNavigationStateRestored();
}

public sealed class PortalGraphRoutePlanner : IGroupRoutePlanner
{
    private const float AttachmentSlack = 80f;

    private readonly StaticWorld _world;
    private readonly PortalNode[] _nodes;
    private readonly PortalEdge[] _edges;

    public PortalGraphRoutePlanner(
        StaticWorld world,
        PortalNode[] nodes,
        PortalEdge[] edges)
    {
        _world = world;
        _nodes = nodes;
        _edges = edges;
        ValidateGraph();
    }

    public ReadOnlySpan<PortalNode> Nodes => _nodes;
    public ReadOnlySpan<PortalEdge> Edges => _edges;

    public GroupRoutePlan Plan(Vector2 start, Vector2 goal, float agentRadius)
    {
        if (_world.IsSegmentFree(start, goal, agentRadius))
        {
            return GroupRoutePlan.Empty;
        }

        var startIndex = _nodes.Length;
        var goalIndex = _nodes.Length + 1;
        var totalNodes = _nodes.Length + 2;
        var costs = new float[totalNodes];
        var parents = new int[totalNodes];
        var open = new bool[totalNodes];
        var closed = new bool[totalNodes];
        var startAttachments = BuildAttachmentMask(start, agentRadius);
        var goalAttachments = BuildAttachmentMask(goal, agentRadius);
        Array.Fill(costs, float.PositiveInfinity);
        Array.Fill(parents, -1);
        costs[startIndex] = 0f;
        open[startIndex] = true;

        while (true)
        {
            var current = SelectBestOpen(open, closed, costs, goal, start, goalIndex);
            if (current < 0)
            {
                return GroupRoutePlan.Empty;
            }

            if (current == goalIndex)
            {
                break;
            }

            open[current] = false;
            closed[current] = true;
            VisitNeighbors(
                current,
                startIndex,
                goalIndex,
                start,
                goal,
                agentRadius,
                startAttachments,
                goalAttachments,
                costs,
                parents,
                open,
                closed);
        }

        var routeIndices = new List<int>(_nodes.Length);
        for (var cursor = parents[goalIndex]; cursor >= 0 && cursor != startIndex;
             cursor = parents[cursor])
        {
            routeIndices.Add(cursor);
        }

        routeIndices.Reverse();
        var waypoints = new Vector2[routeIndices.Count];
        for (var i = 0; i < routeIndices.Count; i++)
        {
            waypoints[i] = _nodes[routeIndices[i]].Position;
        }

        var chokeIds = CollectChokeIds(startIndex, goalIndex, parents);
        return new GroupRoutePlan(waypoints, chokeIds, costs[goalIndex]);
    }

    private void VisitNeighbors(
        int current,
        int startIndex,
        int goalIndex,
        Vector2 start,
        Vector2 goal,
        float radius,
        bool[] startAttachments,
        bool[] goalAttachments,
        float[] costs,
        int[] parents,
        bool[] open,
        bool[] closed)
    {
        if (current == startIndex)
        {
            for (var node = 0; node < _nodes.Length; node++)
            {
                if (startAttachments[node])
                {
                    Relax(current, node, Vector2.Distance(start, _nodes[node].Position),
                        costs, parents, open, closed);
                }
            }

            return;
        }

        if (current >= _nodes.Length)
        {
            return;
        }

        var currentPosition = _nodes[current].Position;
        if (goalAttachments[current])
        {
            Relax(current, goalIndex, Vector2.Distance(currentPosition, goal),
                costs, parents, open, closed);
        }

        for (var edgeIndex = 0; edgeIndex < _edges.Length; edgeIndex++)
        {
            var edge = _edges[edgeIndex];
            if (edge.Width < radius * 2f + 2f)
            {
                continue;
            }

            var neighbor = edge.FromNode == current
                ? edge.ToNode
                : edge.ToNode == current
                    ? edge.FromNode
                    : -1;
            if (neighbor < 0)
            {
                continue;
            }

            var neighborPosition = _nodes[neighbor].Position;
            if (!_world.IsSegmentFree(currentPosition, neighborPosition, radius))
            {
                continue;
            }

            var narrownessPenalty = edge.Width < 160f
                ? (160f - edge.Width) * 0.35f
                : 0f;
            Relax(
                current,
                neighbor,
                Vector2.Distance(currentPosition, neighborPosition) + narrownessPenalty,
                costs,
                parents,
                open,
                closed);
        }
    }

    private bool[] BuildAttachmentMask(Vector2 point, float radius)
    {
        var distances = new float[_nodes.Length];
        var closest = float.PositiveInfinity;
        for (var node = 0; node < _nodes.Length; node++)
        {
            if (!_world.IsSegmentFree(point, _nodes[node].Position, radius))
            {
                distances[node] = float.PositiveInfinity;
                continue;
            }

            distances[node] = Vector2.Distance(point, _nodes[node].Position);
            closest = MathF.Min(closest, distances[node]);
        }

        var result = new bool[_nodes.Length];
        if (!float.IsFinite(closest))
        {
            return result;
        }

        for (var node = 0; node < _nodes.Length; node++)
        {
            result[node] = distances[node] <= closest + AttachmentSlack;
        }

        return result;
    }

    private int[] CollectChokeIds(int startIndex, int goalIndex, int[] parents)
    {
        var result = new List<int>();
        var child = goalIndex;
        var parent = parents[child];
        while (parent >= 0)
        {
            if (parent < _nodes.Length && child < _nodes.Length)
            {
                for (var edgeIndex = 0; edgeIndex < _edges.Length; edgeIndex++)
                {
                    var edge = _edges[edgeIndex];
                    var matches = (edge.FromNode == parent && edge.ToNode == child) ||
                                  (edge.FromNode == child && edge.ToNode == parent);
                    if (matches && edge.ChokeId >= 0 && !result.Contains(edge.ChokeId))
                    {
                        result.Add(edge.ChokeId);
                    }
                }
            }

            child = parent;
            if (child == startIndex)
            {
                break;
            }

            parent = parents[child];
        }

        result.Reverse();
        return result.ToArray();
    }

    private int SelectBestOpen(
        bool[] open,
        bool[] closed,
        float[] costs,
        Vector2 goal,
        Vector2 start,
        int goalIndex)
    {
        var best = -1;
        var bestScore = float.PositiveInfinity;
        for (var node = 0; node < open.Length; node++)
        {
            if (!open[node] || closed[node])
            {
                continue;
            }

            var position = node == _nodes.Length
                ? start
                : node == goalIndex
                    ? goal
                    : _nodes[node].Position;
            var score = costs[node] + Vector2.Distance(position, goal);
            if (score < bestScore - 0.0001f ||
                (MathF.Abs(score - bestScore) <= 0.0001f && node < best))
            {
                best = node;
                bestScore = score;
            }
        }

        return best;
    }

    private static void Relax(
        int current,
        int neighbor,
        float edgeCost,
        float[] costs,
        int[] parents,
        bool[] open,
        bool[] closed)
    {
        if (closed[neighbor])
        {
            return;
        }

        var candidate = costs[current] + edgeCost;
        if (candidate < costs[neighbor] - 0.0001f ||
            (MathF.Abs(candidate - costs[neighbor]) <= 0.0001f && current < parents[neighbor]))
        {
            costs[neighbor] = candidate;
            parents[neighbor] = current;
            open[neighbor] = true;
        }
    }

    private void ValidateGraph()
    {
        for (var i = 0; i < _nodes.Length; i++)
        {
            if (_nodes[i].Id != i)
            {
                throw new ArgumentException("Portal node IDs must be dense and match array indices.");
            }
        }

        for (var i = 0; i < _edges.Length; i++)
        {
            var edge = _edges[i];
            if ((uint)edge.FromNode >= (uint)_nodes.Length ||
                (uint)edge.ToNode >= (uint)_nodes.Length ||
                edge.FromNode == edge.ToNode || edge.Width <= 0f)
            {
                throw new ArgumentException($"Invalid portal edge at index {i}.");
            }
        }
    }
}
