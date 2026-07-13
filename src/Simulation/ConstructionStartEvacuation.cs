using System.Numerics;

namespace RtsDemo.Simulation;

public static class ConstructionStartEvacuation
{
    private const float ExitMargin = 4f;

    public static bool TryFindExit(
        StaticWorld world,
        SimRect footprint,
        Vector2 position,
        float unitRadius,
        float placementPadding,
        out Vector2 exit)
    {
        if (!float.IsFinite(unitRadius) || unitRadius <= 0f ||
            !float.IsFinite(placementPadding) || placementPadding < 0f)
            throw new ArgumentOutOfRangeException(nameof(unitRadius));

        var expanded = footprint.Expanded(
            unitRadius + placementPadding + ExitMargin);
        Vector2[] candidates =
        [
            new(expanded.Min.X, Math.Clamp(position.Y, expanded.Min.Y, expanded.Max.Y)),
            new(expanded.Max.X, Math.Clamp(position.Y, expanded.Min.Y, expanded.Max.Y)),
            new(Math.Clamp(position.X, expanded.Min.X, expanded.Max.X), expanded.Min.Y),
            new(Math.Clamp(position.X, expanded.Min.X, expanded.Max.X), expanded.Max.Y)
        ];
        var allowed = world.Bounds.Inset(unitRadius + ExitMargin);
        var ordered = candidates
            .Select((value, index) => new
            {
                Position = allowed.Clamp(value),
                Distance = Vector2.DistanceSquared(position, value),
                Index = index
            })
            .OrderBy(value => value.Distance)
            .ThenBy(value => value.Index);
        foreach (var candidate in ordered)
        {
            if (!world.IsDiscFree(candidate.Position, unitRadius))
                continue;
            exit = candidate.Position;
            return true;
        }
        exit = default;
        return false;
    }
}
