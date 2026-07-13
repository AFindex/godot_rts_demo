using System.Numerics;

namespace RtsDemo.Simulation;

public static class ConstructionAccessPointResolver
{
    public static Vector2 ProjectFromCenter(
        SimRect bounds,
        Vector2 origin,
        float clearance)
    {
        var expanded = bounds.Expanded(clearance);
        var center = (expanded.Min + expanded.Max) * 0.5f;
        var half = (expanded.Max - expanded.Min) * 0.5f;
        var direction = origin - center;
        if (direction.LengthSquared() <= 0.0001f)
            return new Vector2(expanded.Min.X, center.Y);

        var xScale = MathF.Abs(direction.X) <= 0.0001f
            ? float.PositiveInfinity
            : half.X / MathF.Abs(direction.X);
        var yScale = MathF.Abs(direction.Y) <= 0.0001f
            ? float.PositiveInfinity
            : half.Y / MathF.Abs(direction.Y);
        return center + direction * MathF.Min(xScale, yScale);
    }

    public static Vector2 Resolve(
        StaticWorld world,
        IPathProvider pathProvider,
        SimRect bounds,
        Vector2 origin,
        float collisionRadius,
        float navigationRadius,
        float placementPadding,
        Func<Vector2, float, bool>? additionalDiscClearance = null)
    {
        var offset = collisionRadius + placementPadding + 1f;
        var center = (bounds.Min + bounds.Max) * 0.5f;
        Vector2[] candidates =
        [
            new(bounds.Min.X - offset, center.Y),
            new(bounds.Max.X + offset, center.Y),
            new(center.X, bounds.Min.Y - offset),
            new(center.X, bounds.Max.Y + offset)
        ];

        var best = candidates[0];
        var bestCost = float.PositiveInfinity;
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (!world.IsDiscFree(candidate, collisionRadius) ||
                additionalDiscClearance is not null &&
                !additionalDiscClearance(candidate, collisionRadius))
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

    public static Vector2 ResolveInteraction(
        StaticWorld world,
        IPathProvider pathProvider,
        SimRect bounds,
        Vector2 origin,
        float collisionRadius,
        float navigationRadius,
        float interactionPadding)
    {
        var clearance = collisionRadius + interactionPadding + 1f;
        var projected = ProjectFromCenter(bounds, origin, clearance);
        if (world.IsDiscFree(projected, collisionRadius))
        {
            var path = pathProvider.IsReady
                ? pathProvider.FindPath(origin, projected, navigationRadius)
                : [origin, projected];
            if (path.Length > 0)
                return projected;
        }
        return Resolve(
            world,
            pathProvider,
            bounds,
            origin,
            collisionRadius,
            navigationRadius,
            interactionPadding);
    }

    private static float PathLength(ReadOnlySpan<Vector2> path)
    {
        var result = 0f;
        for (var index = 1; index < path.Length; index++)
            result += Vector2.Distance(path[index - 1], path[index]);
        return result;
    }

}
