using System.Numerics;

namespace RtsDemo.Simulation;

public enum ProductionExitStatus : byte
{
    Available,
    SoftBlockedByFriendly,
    HardBlocked
}

public readonly record struct ProductionExitResolution(
    ProductionExitStatus Status,
    Vector2 Position,
    int FriendlyBlockerCount);

/// <summary>
/// Resolves a bounded set of production exits. Static geometry and enemy units
/// are hard blockers. Friendly-only occupancy is reported separately so the
/// simulation may issue deterministic, system-derived evacuation orders.
/// </summary>
public static class ProductionExitResolver
{
    public const int MaximumReportedFriendlyBlockers = 32;

    public static ProductionExitResolution Resolve(
        SimRect bounds,
        float radius,
        Vector2? rally,
        int playerId,
        UnitStore units,
        CombatStore combat,
        StaticWorld world,
        Span<int> friendlyBlockers,
        Func<Vector2, Vector2, float, float>? pathCost = null)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (friendlyBlockers.Length < MaximumReportedFriendlyBlockers)
            throw new ArgumentException(
                "Production exit blocker buffer is too small.",
                nameof(friendlyBlockers));

        var candidates = FillCandidates(bounds, radius);
        var bestAvailableScore = float.PositiveInfinity;
        var bestAvailableIndex = -1;
        var bestSoftScore = float.PositiveInfinity;
        var bestSoftIndex = -1;

        for (var candidateIndex = 0;
             candidateIndex < candidates.Count;
             candidateIndex++)
        {
            var candidate = candidates[candidateIndex];
            if (!world.IsDiscFree(candidate, radius))
                continue;
            var hasFriendly = false;
            var hasEnemy = false;
            ClassifyUnitOccupancy(
                candidate, radius, playerId, units, combat,
                ref hasFriendly, ref hasEnemy);
            var score = rally.HasValue
                ? pathCost?.Invoke(candidate, rally.Value, radius) ??
                  Vector2.Distance(candidate, rally.Value)
                : candidateIndex;
            if (!float.IsFinite(score))
                continue;
            if (!hasFriendly && !hasEnemy && score < bestAvailableScore)
            {
                bestAvailableScore = score;
                bestAvailableIndex = candidateIndex;
            }
            else if (hasFriendly && !hasEnemy && score < bestSoftScore)
            {
                bestSoftScore = score;
                bestSoftIndex = candidateIndex;
            }
        }

        if (bestAvailableIndex >= 0)
        {
            return new ProductionExitResolution(
                ProductionExitStatus.Available,
                candidates[bestAvailableIndex],
                0);
        }
        if (bestSoftIndex < 0)
        {
            return new ProductionExitResolution(
                ProductionExitStatus.HardBlocked, default, 0);
        }

        var blockerCount = CopyFriendlyBlockers(
            candidates[bestSoftIndex], radius, playerId,
            units, combat, friendlyBlockers);
        return new ProductionExitResolution(
            ProductionExitStatus.SoftBlockedByFriendly,
            candidates[bestSoftIndex],
            blockerCount);
    }

    private static List<Vector2> FillCandidates(
        SimRect bounds,
        float radius)
    {
        var numeric = InteractionGeometry.NumericTolerance(
            (bounds.Min + bounds.Max) * 0.5f, bounds);
        var spacing = MathF.Max(radius * 2f, 8f);
        return InteractionGeometry.SampleRoundedRectangleBoundary(
            bounds, radius + numeric, spacing);
    }

    private static void ClassifyUnitOccupancy(
        Vector2 position,
        float radius,
        int playerId,
        UnitStore units,
        CombatStore combat,
        ref bool hasFriendly,
        ref bool hasEnemy)
    {
        foreach (var unit in units.AliveUnits)
        {
            if (!Overlaps(position, radius, units.Positions[unit], units.Radii[unit]))
            {
                continue;
            }
            if (combat.Teams[unit] == playerId)
                hasFriendly = true;
            else
                hasEnemy = true;
        }
    }

    private static int CopyFriendlyBlockers(
        Vector2 position,
        float radius,
        int playerId,
        UnitStore units,
        CombatStore combat,
        Span<int> output)
    {
        var count = 0;
        foreach (var unit in units.AliveUnits)
        {
            if (count >= MaximumReportedFriendlyBlockers) break;
            if (combat.Teams[unit] != playerId ||
                !Overlaps(position, radius, units.Positions[unit], units.Radii[unit]))
            {
                continue;
            }
            output[count++] = unit;
        }
        return count;
    }

    private static bool Overlaps(
        Vector2 left,
        float leftRadius,
        Vector2 right,
        float rightRadius)
    {
        var minimum = leftRadius + rightRadius +
                      InteractionGeometry.NumericTolerance(
                          left, new SimRect(right, right));
        return Vector2.DistanceSquared(left, right) < minimum * minimum;
    }
}
