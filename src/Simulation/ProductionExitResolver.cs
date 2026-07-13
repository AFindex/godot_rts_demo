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
    public const int CandidateCount = 12;
    public const int MaximumReportedFriendlyBlockers = 32;

    public static ProductionExitResolution Resolve(
        SimRect bounds,
        float radius,
        Vector2? rally,
        int playerId,
        UnitStore units,
        CombatStore combat,
        StaticWorld world,
        Span<int> friendlyBlockers)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (friendlyBlockers.Length < MaximumReportedFriendlyBlockers)
            throw new ArgumentException(
                "Production exit blocker buffer is too small.",
                nameof(friendlyBlockers));

        Span<Vector2> candidates = stackalloc Vector2[CandidateCount];
        FillCandidates(bounds, radius, candidates);
        var bestAvailableScore = float.PositiveInfinity;
        var bestAvailableIndex = -1;
        var bestSoftScore = float.PositiveInfinity;
        var bestSoftIndex = -1;

        for (var candidateIndex = 0;
             candidateIndex < candidates.Length;
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
                ? Vector2.DistanceSquared(candidate, rally.Value)
                : candidateIndex;
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

    private static void FillCandidates(
        SimRect bounds,
        float radius,
        Span<Vector2> candidates)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var offset = radius + 6f;
        candidates[0] = new Vector2(bounds.Max.X + offset, center.Y);
        candidates[1] = new Vector2(bounds.Min.X - offset, center.Y);
        candidates[2] = new Vector2(center.X, bounds.Max.Y + offset);
        candidates[3] = new Vector2(center.X, bounds.Min.Y - offset);
        candidates[4] = new Vector2(
            bounds.Max.X + offset, bounds.Max.Y + offset);
        candidates[5] = new Vector2(
            bounds.Max.X + offset, bounds.Min.Y - offset);
        candidates[6] = new Vector2(
            bounds.Min.X - offset, bounds.Max.Y + offset);
        candidates[7] = new Vector2(
            bounds.Min.X - offset, bounds.Min.Y - offset);
        candidates[8] = new Vector2(
            bounds.Max.X + offset, bounds.Min.Y + bounds.Height * 0.25f);
        candidates[9] = new Vector2(
            bounds.Max.X + offset, bounds.Min.Y + bounds.Height * 0.75f);
        candidates[10] = new Vector2(
            bounds.Min.X - offset, bounds.Min.Y + bounds.Height * 0.25f);
        candidates[11] = new Vector2(
            bounds.Min.X - offset, bounds.Min.Y + bounds.Height * 0.75f);
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
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (!units.Alive[unit] ||
                !Overlaps(position, radius, units.Positions[unit], units.Radii[unit]))
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
        for (var unit = 0;
             unit < units.Count && count < MaximumReportedFriendlyBlockers;
             unit++)
        {
            if (!units.Alive[unit] || combat.Teams[unit] != playerId ||
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
        var minimum = leftRadius + rightRadius + 1f;
        return Vector2.DistanceSquared(left, right) < minimum * minimum;
    }
}
