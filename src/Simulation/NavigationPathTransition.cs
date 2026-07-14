using System.Numerics;

namespace RtsDemo.Simulation;

/// <summary>
/// Normalizes a path that starts or ends on an interaction surface. A unit may
/// use its physical body radius while leaving or entering that surface, but all
/// ordinary path segments keep the configured navigation radius.
/// </summary>
public static class NavigationPathTransition
{
    public static bool TryNormalize(
        StaticWorld world,
        ReadOnlySpan<Vector2> source,
        float navigationRadius,
        float? firstSegmentRadius,
        float? finalSegmentRadius,
        out Vector2[] path)
    {
        path = [];
        if (source.Length == 0)
            return false;
        if (IsNavigable(
                world,
                source,
                navigationRadius,
                firstSegmentRadius,
                finalSegmentRadius))
        {
            path = source.ToArray();
            return true;
        }
        if (!firstSegmentRadius.HasValue || source.Length < 3 ||
            world.IsDiscFree(source[0], navigationRadius))
        {
            return false;
        }

        for (var firstOrdinaryPoint = 1;
             firstOrdinaryPoint < source.Length;
             firstOrdinaryPoint++)
        {
            var point = source[firstOrdinaryPoint];
            if (!world.IsDiscFree(point, navigationRadius) ||
                !world.IsSegmentFree(
                    source[0], point, firstSegmentRadius.Value))
            {
                continue;
            }

            var candidate = new Vector2[
                source.Length - firstOrdinaryPoint + 1];
            candidate[0] = source[0];
            source[firstOrdinaryPoint..].CopyTo(candidate.AsSpan(1));
            if (!IsNavigable(
                    world,
                    candidate,
                    navigationRadius,
                    firstSegmentRadius,
                    finalSegmentRadius))
            {
                continue;
            }
            path = candidate;
            return true;
        }
        return false;
    }

    private static bool IsNavigable(
        StaticWorld world,
        ReadOnlySpan<Vector2> points,
        float navigationRadius,
        float? firstSegmentRadius,
        float? finalSegmentRadius)
    {
        for (var index = 1; index < points.Length; index++)
        {
            var radius = navigationRadius;
            if (firstSegmentRadius.HasValue && index == 1)
                radius = firstSegmentRadius.Value;
            if (finalSegmentRadius.HasValue && index == points.Length - 1)
                radius = MathF.Min(radius, finalSegmentRadius.Value);
            if (!world.IsSegmentFree(points[index - 1], points[index], radius))
                return false;
        }
        return points.Length > 0;
    }
}
