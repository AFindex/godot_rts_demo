using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class StaticWorld
{
    private readonly SimRect[] _obstacles;
    private readonly DynamicOccupancyGrid _staticOccupancy;

    public StaticWorld(SimRect bounds, params SimRect[] obstacles)
        : this(bounds, terrain: null, obstacles)
    {
    }

    public StaticWorld(
        SimRect bounds,
        ITerrainMapQuery? terrain,
        params SimRect[] obstacles)
    {
        Bounds = bounds;
        if (terrain is not null && terrain.Bounds != bounds)
        {
            throw new ArgumentException(
                "Terrain bounds must match static world bounds.", nameof(terrain));
        }
        Terrain = terrain;
        _obstacles = obstacles;
        _staticOccupancy = new DynamicOccupancyGrid(bounds);
        for (var index = 0; index < obstacles.Length; index++)
            _staticOccupancy.Place(obstacles[index]);
        DynamicOccupancy = new DynamicOccupancyGrid(bounds);
    }

    public SimRect Bounds { get; }
    public ReadOnlySpan<SimRect> Obstacles => _obstacles;
    public ITerrainMapQuery? Terrain { get; }
    public DynamicOccupancyGrid DynamicOccupancy { get; }
    public int NavigationRevision => DynamicOccupancy.Revision;

    public bool IsDiscFree(Vector2 position, float radius)
    {
        if (!Bounds.Inset(radius).Contains(position))
        {
            return false;
        }

        if (Terrain is not null &&
            !Terrain.IsDiscTraversable(position, radius))
        {
            return false;
        }

        if (!_staticOccupancy.IsDiscFree(position, radius)) return false;

        return DynamicOccupancy.IsDiscFree(position, radius);
    }

    public bool IsSegmentFree(Vector2 from, Vector2 to, float radius)
    {
        if (!Bounds.Inset(radius).Contains(to))
        {
            return false;
        }

        if (Terrain is not null &&
            !Terrain.IsSegmentTraversable(from, to, radius))
        {
            return false;
        }

        if (!_staticOccupancy.IsSegmentFree(from, to, radius)) return false;

        return DynamicOccupancy.IsSegmentFree(from, to, radius);
    }

    public Vector2 ConstrainDisc(Vector2 previous, Vector2 proposed, float radius)
    {
        var allowed = Bounds.Inset(radius);
        proposed = allowed.Clamp(proposed);

        if (Terrain is not null &&
            !Terrain.IsDiscTraversable(proposed, radius))
        {
            proposed = ConstrainToTerrain(previous, proposed, radius);
        }

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

    private Vector2 ConstrainToTerrain(
        Vector2 previous,
        Vector2 proposed,
        float radius)
    {
        if (Terrain is null || !Terrain.IsDiscTraversable(previous, radius))
            return previous;
        var accepted = previous;
        var rejected = proposed;
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var candidate = Vector2.Lerp(accepted, rejected, 0.5f);
            if (Terrain.IsDiscTraversable(candidate, radius))
                accepted = candidate;
            else
                rejected = candidate;
        }
        return accepted;
    }
}
