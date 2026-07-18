using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct InteractionApproachResolution(
    bool Found,
    Vector2 Target,
    float PathLength);

public static class ConstructionAccessPointResolver
{
    public static Vector2 ProjectFromCenter(
        SimRect bounds,
        Vector2 origin,
        float clearance) =>
        InteractionGeometry.ProjectFromCenter(bounds, origin, clearance);

    public static InteractionApproachResolution ResolveAvailableEndpoint(
        StaticWorld world,
        SimRect bounds,
        Vector2 origin,
        float collisionRadius,
        float interactionPadding = 0f,
        Func<Vector2, float, bool>? additionalDiscClearance = null,
        Vector2? excludedTarget = null)
    {
        var numericClearance =
            InteractionGeometry.NumericTolerance(origin, bounds) * 0.25f;
        var clearance = collisionRadius + interactionPadding + numericClearance;
        var projected = ProjectFromCenter(bounds, origin, clearance);
        if (!IsExcluded(projected, excludedTarget) &&
            EndpointIsAvailable(
                world, projected, collisionRadius, additionalDiscClearance))
        {
            return new InteractionApproachResolution(
                true, projected, Vector2.Distance(origin, projected));
        }

        var candidates = InteractionGeometry.SampleRoundedRectangleBoundary(
            bounds,
            clearance,
            MathF.Max(collisionRadius * 2f, 8f));
        var best = default(Vector2);
        var bestDistanceSquared = float.PositiveInfinity;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var distanceSquared = Vector2.DistanceSquared(origin, candidate);
            if (IsExcluded(candidate, excludedTarget) ||
                distanceSquared >= bestDistanceSquared ||
                !EndpointIsAvailable(
                    world, candidate, collisionRadius,
                    additionalDiscClearance))
                continue;
            best = candidate;
            bestDistanceSquared = distanceSquared;
        }
        return float.IsFinite(bestDistanceSquared)
            ? new InteractionApproachResolution(
                true, best, MathF.Sqrt(bestDistanceSquared))
            : new InteractionApproachResolution(
                false, default, float.PositiveInfinity);
    }

    public static InteractionApproachResolution ResolveAvailableCircleEndpoint(
        StaticWorld world,
        Vector2 center,
        float interactionRadius,
        Vector2 origin,
        float collisionRadius,
        Vector2? excludedTarget = null)
    {
        var pointBounds = new SimRect(center, center);
        var numericClearance =
            InteractionGeometry.NumericTolerance(center, pointBounds) * 0.25f;
        var targetRadius = interactionRadius + collisionRadius + numericClearance;
        var direction = origin - center;
        if (direction.LengthSquared() <= 0f)
            direction = -Vector2.UnitX;
        else
            direction = Vector2.Normalize(direction);
        var projected = center + direction * targetRadius;
        if (!IsExcluded(projected, excludedTarget) &&
            EndpointIsAvailable(world, projected, collisionRadius, null))
        {
            return new InteractionApproachResolution(
                true, projected, Vector2.Distance(origin, projected));
        }

        var maximumSpacing = MathF.Max(collisionRadius * 2f, 8f);
        var candidateCount = Math.Max(
            8,
            (int)MathF.Ceiling(MathF.Tau * targetRadius / maximumSpacing));
        var originAngle = MathF.Atan2(direction.Y, direction.X);
        var best = default(Vector2);
        var bestDistanceSquared = float.PositiveInfinity;
        for (var index = 1; index < candidateCount; index++)
        {
            var angle = originAngle + MathF.Tau * index / candidateCount;
            var candidate = center +
                new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * targetRadius;
            var distanceSquared = Vector2.DistanceSquared(origin, candidate);
            if (IsExcluded(candidate, excludedTarget) ||
                distanceSquared >= bestDistanceSquared ||
                !EndpointIsAvailable(world, candidate, collisionRadius, null))
                continue;
            best = candidate;
            bestDistanceSquared = distanceSquared;
        }
        return float.IsFinite(bestDistanceSquared)
            ? new InteractionApproachResolution(
                true, best, MathF.Sqrt(bestDistanceSquared))
            : new InteractionApproachResolution(
                false, default, float.PositiveInfinity);
    }

    public static InteractionApproachResolution Resolve(
        StaticWorld world,
        IPathProvider pathProvider,
        SimRect bounds,
        Vector2 origin,
        float collisionRadius,
        float navigationRadius,
        float interactionPadding,
        Func<Vector2, float, bool>? additionalDiscClearance = null)
    {
        var numericClearance =
            InteractionGeometry.NumericTolerance(origin, bounds) * 0.25f;
        var clearance = collisionRadius + interactionPadding + numericClearance;
        var projected = ProjectFromCenter(bounds, origin, clearance);

        if (TryMeasureCandidate(
                world, pathProvider, origin, projected,
                collisionRadius, navigationRadius,
                additionalDiscClearance,
                endpoint => InteractionGeometry.DiscTouchesRectangle(
                    endpoint,
                    collisionRadius,
                    bounds,
                    interactionPadding),
                out var projectedLength))
        {
            return new InteractionApproachResolution(
                true, projected, projectedLength);
        }

        var candidates = InteractionGeometry.SampleRoundedRectangleBoundary(
            bounds,
            clearance,
            MathF.Max(collisionRadius * 2f, 8f));
        var rankedCandidates = new CandidateLowerBound[candidates.Count];
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            rankedCandidates[index] = new CandidateLowerBound(
                candidate,
                Vector2.Distance(origin, candidate),
                index);
        }
        Array.Sort(rankedCandidates, CandidateLowerBoundComparer.Instance);

        var best = default(Vector2);
        var bestLength = float.PositiveInfinity;
        for (var index = 0; index < rankedCandidates.Length; index++)
        {
            var ranked = rankedCandidates[index];
            // Straight-line distance is a strict lower bound for every path to
            // this endpoint. Once it cannot beat the best measured path, no
            // later (farther) boundary candidate can improve the result. This
            // preserves the exact shortest reachable endpoint while avoiding
            // a full A* query for every point around a building footprint.
            if (ranked.Distance >= bestLength)
                break;
            var candidate = ranked.Point;
            if (Vector2.DistanceSquared(candidate, projected) <=
                numericClearance * numericClearance)
            {
                continue;
            }
            if (!TryMeasureCandidate(
                    world, pathProvider, origin, candidate,
                    collisionRadius, navigationRadius,
                    additionalDiscClearance,
                    endpoint => InteractionGeometry.DiscTouchesRectangle(
                        endpoint,
                        collisionRadius,
                        bounds,
                        interactionPadding),
                    out var length) ||
                length >= bestLength)
            {
                continue;
            }
            best = candidate;
            bestLength = length;
        }
        return float.IsFinite(bestLength)
            ? new InteractionApproachResolution(true, best, bestLength)
            : new InteractionApproachResolution(
                false, default, float.PositiveInfinity);
    }

    public static InteractionApproachResolution ResolveInteraction(
        StaticWorld world,
        IPathProvider pathProvider,
        SimRect bounds,
        Vector2 origin,
        float collisionRadius,
        float navigationRadius,
        float interactionPadding) =>
        Resolve(
            world,
            pathProvider,
            bounds,
            origin,
            collisionRadius,
            navigationRadius,
            interactionPadding);

    public static InteractionApproachResolution ResolveCircle(
        StaticWorld world,
        IPathProvider pathProvider,
        Vector2 center,
        float interactionRadius,
        Vector2 origin,
        float collisionRadius,
        float navigationRadius)
    {
        var pointBounds = new SimRect(center, center);
        var numericClearance =
            InteractionGeometry.NumericTolerance(center, pointBounds) * 0.25f;
        var targetRadius =
            interactionRadius + collisionRadius + numericClearance;
        var direction = origin - center;
        if (direction.LengthSquared() <= 0f)
            direction = -Vector2.UnitX;
        else
            direction = Vector2.Normalize(direction);
        var projected = center + direction * targetRadius;
        if (TryMeasureCandidate(
                world, pathProvider, origin, projected,
                collisionRadius, navigationRadius, null,
                endpoint => Vector2.DistanceSquared(endpoint, center) <=
                    (targetRadius + InteractionGeometry.NumericTolerance(
                        endpoint, pointBounds)) *
                    (targetRadius + InteractionGeometry.NumericTolerance(
                        endpoint, pointBounds)),
                out var projectedLength))
        {
            return new InteractionApproachResolution(
                true, projected, projectedLength);
        }

        var maximumSpacing = MathF.Max(collisionRadius * 2f, 8f);
        var candidateCount = Math.Max(
            8,
            (int)MathF.Ceiling(
                MathF.Tau * targetRadius / maximumSpacing));
        var originAngle = MathF.Atan2(direction.Y, direction.X);
        var best = default(Vector2);
        var bestLength = float.PositiveInfinity;
        for (var index = 1; index < candidateCount; index++)
        {
            var angle = originAngle + MathF.Tau * index / candidateCount;
            var candidate = center +
                new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * targetRadius;
            if (!TryMeasureCandidate(
                    world, pathProvider, origin, candidate,
                    collisionRadius, navigationRadius, null,
                    endpoint => Vector2.DistanceSquared(endpoint, center) <=
                        (targetRadius + InteractionGeometry.NumericTolerance(
                            endpoint, pointBounds)) *
                        (targetRadius + InteractionGeometry.NumericTolerance(
                            endpoint, pointBounds)),
                    out var length) || length >= bestLength)
                continue;
            best = candidate;
            bestLength = length;
        }
        return float.IsFinite(bestLength)
            ? new InteractionApproachResolution(true, best, bestLength)
            : new InteractionApproachResolution(
                false, default, float.PositiveInfinity);
    }

    private static bool TryMeasureCandidate(
        StaticWorld world,
        IPathProvider pathProvider,
        Vector2 origin,
        Vector2 candidate,
        float collisionRadius,
        float navigationRadius,
        Func<Vector2, float, bool>? additionalDiscClearance,
        Predicate<Vector2> endpointAccepted,
        out float length)
    {
        length = float.PositiveInfinity;
        if (!world.IsDiscFree(candidate, collisionRadius) ||
            additionalDiscClearance is not null &&
            !additionalDiscClearance(candidate, collisionRadius))
        {
            return false;
        }

        var path = pathProvider.IsReady
            ? pathProvider.FindPath(origin, candidate, navigationRadius)
            : world.IsSegmentFree(origin, candidate, collisionRadius)
                ? [origin, candidate]
                : [];
        if (path.Length == 0)
            return false;

        if (!endpointAccepted(path[^1]) ||
            !NavigationPathTransition.TryNormalize(
                world,
                path,
                navigationRadius,
                !world.IsDiscFree(origin, navigationRadius)
                    ? collisionRadius
                    : null,
                collisionRadius,
                out path))
            return false;
        length = PathLength(path);
        return true;
    }

    private static bool EndpointIsAvailable(
        StaticWorld world,
        Vector2 candidate,
        float collisionRadius,
        Func<Vector2, float, bool>? additionalDiscClearance) =>
        world.IsDiscFree(candidate, collisionRadius) &&
        (additionalDiscClearance is null ||
         additionalDiscClearance(candidate, collisionRadius));

    private static bool IsExcluded(Vector2 candidate, Vector2? excludedTarget) =>
        excludedTarget.HasValue &&
        Vector2.DistanceSquared(candidate, excludedTarget.Value) <= 1f;

    private static float PathLength(ReadOnlySpan<Vector2> path)
    {
        var result = 0f;
        for (var index = 1; index < path.Length; index++)
            result += Vector2.Distance(path[index - 1], path[index]);
        return result;
    }

    private readonly record struct CandidateLowerBound(
        Vector2 Point,
        float Distance,
        int Ordinal);

    private sealed class CandidateLowerBoundComparer :
        IComparer<CandidateLowerBound>
    {
        public static CandidateLowerBoundComparer Instance { get; } = new();

        public int Compare(CandidateLowerBound left, CandidateLowerBound right)
        {
            var distance = left.Distance.CompareTo(right.Distance);
            return distance != 0
                ? distance
                : left.Ordinal.CompareTo(right.Ordinal);
        }
    }
}
