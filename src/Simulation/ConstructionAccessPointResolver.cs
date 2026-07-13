using System.Numerics;

namespace RtsDemo.Simulation;

public static class ConstructionAccessPointResolver
{
    public static Vector2 Resolve(
        StaticWorld world,
        IPathProvider pathProvider,
        SimRect bounds,
        Vector2 origin,
        float collisionRadius,
        float navigationRadius,
        float placementPadding,
        float arrivalTolerance)
    {
        var offset = collisionRadius + placementPadding +
                     arrivalTolerance + 1f;
        Vector2[] candidates =
        [
            new(bounds.Min.X - offset,
                Math.Clamp(origin.Y, bounds.Min.Y, bounds.Max.Y)),
            new(bounds.Max.X + offset,
                Math.Clamp(origin.Y, bounds.Min.Y, bounds.Max.Y)),
            new(Math.Clamp(origin.X, bounds.Min.X, bounds.Max.X),
                bounds.Min.Y - offset),
            new(Math.Clamp(origin.X, bounds.Min.X, bounds.Max.X),
                bounds.Max.Y + offset)
        ];

        var best = candidates[0];
        var bestCost = float.PositiveInfinity;
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (!world.IsDiscFree(candidate, collisionRadius))
                continue;
            var path = pathProvider.IsReady
                ? pathProvider.FindPath(origin, candidate, navigationRadius)
                : [origin, candidate];
            if (path.Length == 0)
                continue;
            var direct = world.IsSegmentFree(
                origin, candidate, collisionRadius);
            var cost = PathLength(path) + (direct ? 0f : 1_000_000f);
            if (cost >= bestCost)
                continue;
            best = candidate;
            bestCost = cost;
        }
        if (float.IsFinite(bestCost))
            return best;

        return candidates
            .OrderBy(candidate => Vector2.DistanceSquared(origin, candidate))
            .First();
    }

    private static float PathLength(ReadOnlySpan<Vector2> path)
    {
        var result = 0f;
        for (var index = 1; index < path.Length; index++)
            result += Vector2.Distance(path[index - 1], path[index]);
        return result;
    }
}
