using Godot;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime;

public sealed class GodotPathProvider : IPathProvider, IDisposable
{
    private readonly Rid _map;
    private readonly Rid _region;
    private readonly NavigationRegion2D _regionNode;
    private readonly int _polygonCount;
    private readonly Vector2 _probePoint;
    private bool _disposed;
    private int _emptyPathLogCount;

    public GodotPathProvider(Node2D parent, StaticWorld world, float navigationRadius)
    {
        var polygon = BuildPolygon(world, navigationRadius);
        _probePoint = ToGodot((world.Bounds.Min + world.Bounds.Max) * 0.5f);
        _polygonCount = polygon.GetPolygonCount();
        var vertices = polygon.GetVertices();
        if (vertices.Length > 0)
        {
            var minimum = vertices[0];
            var maximum = vertices[0];
            for (var i = 1; i < vertices.Length; i++)
            {
                minimum = new Vector2(
                    MathF.Min(minimum.X, vertices[i].X),
                    MathF.Min(minimum.Y, vertices[i].Y));
                maximum = new Vector2(
                    MathF.Max(maximum.X, vertices[i].X),
                    MathF.Max(maximum.Y, vertices[i].Y));
            }

            GD.Print($"RTS_NAV_MESH vertices={vertices.Length} bounds={minimum}..{maximum}");
        }
        _map = parent.GetWorld2D().NavigationMap;
        NavigationServer2D.MapSetCellSize(_map, 1f);
        NavigationServer2D.MapSetActive(_map, true);

        _regionNode = new NavigationRegion2D
        {
            Name = "RuntimeNavigationRegion",
            NavigationPolygon = polygon
        };
        parent.AddChild(_regionNode);
        _region = _regionNode.GetRid();
        NavigationServer2D.RegionSetEnabled(_region, true);
        NavigationServer2D.RegionSetTransform(_region, Transform2D.Identity);
        NavigationServer2D.RegionSetMap(_region, _map);
        NavigationServer2D.RegionSetNavigationPolygon(_region, polygon);
    }

    public bool IsReady { get; private set; }

    public bool TryMarkSynchronized()
    {
        var iteration = NavigationServer2D.MapGetIterationId(_map);
        var regionCount = NavigationServer2D.MapGetRegions(_map).Count;
        var surfaceReady = false;
        if (iteration > 0)
        {
            var closestProbe = NavigationServer2D.MapGetClosestPoint(_map, _probePoint);
            surfaceReady = closestProbe.DistanceSquaredTo(_probePoint) < 64f * 64f;
        }
        IsReady = _polygonCount > 0 && iteration > 0 && regionCount > 0 && surfaceReady;
        if (IsReady)
        {
            GD.Print($"RTS_NAV_READY=True polygons={_polygonCount} " +
                     $"regions={regionCount} iteration={iteration}");
        }

        return IsReady;
    }

    public NVector2[] FindPath(
        NVector2 start,
        NVector2 goal,
        float navigationRadius)
    {
        if (!IsReady)
        {
            return [];
        }

        var path = NavigationServer2D.MapGetPath(
            _map,
            ToGodot(start),
            ToGodot(goal),
            optimize: true);
        if (path.Length == 0 && _emptyPathLogCount < 8)
        {
            var closestStart = NavigationServer2D.MapGetClosestPoint(_map, ToGodot(start));
            var closestGoal = NavigationServer2D.MapGetClosestPoint(_map, ToGodot(goal));
            GD.Print($"RTS_NAV_EMPTY start={start} closestStart={closestStart} " +
                     $"goal={goal} closestGoal={closestGoal}");
            _emptyPathLogCount++;
        }

        var result = new NVector2[path.Length];
        for (var i = 0; i < path.Length; i++)
        {
            result[i] = ToNumerics(path[i]);
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(_regionNode))
        {
            _regionNode.QueueFree();
        }
        _disposed = true;
    }

    private static NavigationPolygon BuildPolygon(StaticWorld world, float radius)
    {
        var polygon = new NavigationPolygon();
        polygon.CellSize = 1f;
        polygon.AgentRadius = radius;
        var sourceGeometry = new NavigationMeshSourceGeometryData2D();

        var outer = world.Bounds;
        polygon.AddOutline(
        [
            ToGodot(outer.Min),
            new Vector2(outer.Min.X, outer.Max.Y),
            ToGodot(outer.Max),
            new Vector2(outer.Max.X, outer.Min.Y)
        ]);

        foreach (var obstacle in world.Obstacles)
        {
            sourceGeometry.AddObstructionOutline(
            [
                ToGodot(obstacle.Min),
                new Vector2(obstacle.Min.X, obstacle.Max.Y),
                ToGodot(obstacle.Max),
                new Vector2(obstacle.Max.X, obstacle.Min.Y)
            ]);
        }

        NavigationServer2D.BakeFromSourceGeometryData(polygon, sourceGeometry);
        return polygon;
    }

    public static NVector2 ToNumerics(Vector2 value) => new(value.X, value.Y);
    public static Vector2 ToGodot(NVector2 value) => new(value.X, value.Y);
}
