using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class StaticWorld
{
    private readonly SimRect[] _obstacles;

    public StaticWorld(SimRect bounds, params SimRect[] obstacles)
    {
        Bounds = bounds;
        _obstacles = obstacles;
        DynamicOccupancy = new DynamicOccupancyGrid(bounds);
    }

    public SimRect Bounds { get; }
    public ReadOnlySpan<SimRect> Obstacles => _obstacles;
    public DynamicOccupancyGrid DynamicOccupancy { get; }
    public int NavigationRevision => DynamicOccupancy.Revision;

    public bool IsDiscFree(Vector2 position, float radius)
    {
        if (!Bounds.Inset(radius).Contains(position))
        {
            return false;
        }

        for (var i = 0; i < _obstacles.Length; i++)
        {
            if (_obstacles[i].OverlapsDisc(position, radius))
            {
                return false;
            }
        }

        return DynamicOccupancy.IsDiscFree(position, radius);
    }

    public bool IsSegmentFree(Vector2 from, Vector2 to, float radius)
    {
        if (!Bounds.Inset(radius).Contains(to))
        {
            return false;
        }

        for (var i = 0; i < _obstacles.Length; i++)
        {
            if (_obstacles[i].IntersectsSweptDisc(from, to, radius))
            {
                return false;
            }
        }

        return DynamicOccupancy.IsSegmentFree(from, to, radius);
    }

    public Vector2 ConstrainDisc(Vector2 previous, Vector2 proposed, float radius)
    {
        var allowed = Bounds.Inset(radius);
        proposed = allowed.Clamp(proposed);

        for (var i = 0; i < _obstacles.Length; i++)
        {
            var obstacle = _obstacles[i];
            if (!obstacle.OverlapsDisc(proposed, radius))
            {
                continue;
            }
            proposed = obstacle.ConstrainDiscOutside(
                previous, proposed, radius);
        }

        proposed = DynamicOccupancy.ConstrainDisc(previous, proposed, radius);
        return allowed.Clamp(proposed);
    }
}
