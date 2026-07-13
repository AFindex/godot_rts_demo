using System.Numerics;

namespace RtsDemo.Simulation;

public enum ConstructionBlockerKind : byte
{
    None,
    MovableFriendly,
    FriendlyHold,
    FriendlyEconomyTask,
    FriendlyAssignedBuilder,
    FriendlyOtherOrder,
    AuthorityAlly,
    AuthorityEnemy
}

public enum ConstructionBlockerAction : byte
{
    Commit,
    BeginEviction,
    Wait
}

public readonly record struct ConstructionBlockerPolicy(
    ConstructionBlockerAction MovableFriendly,
    ConstructionBlockerAction FriendlyHold,
    ConstructionBlockerAction FriendlyEconomyTask,
    ConstructionBlockerAction FriendlyAssignedBuilder,
    ConstructionBlockerAction FriendlyOtherOrder,
    ConstructionBlockerAction AuthorityAlly,
    ConstructionBlockerAction AuthorityEnemy)
{
    public static ConstructionBlockerPolicy ProjectDefault { get; } = new(
        ConstructionBlockerAction.BeginEviction,
        ConstructionBlockerAction.Wait,
        ConstructionBlockerAction.Wait,
        ConstructionBlockerAction.Wait,
        ConstructionBlockerAction.Wait,
        ConstructionBlockerAction.Wait,
        ConstructionBlockerAction.Wait);

    public ConstructionBlockerAction Resolve(ConstructionBlockerKind kind) =>
        kind switch
        {
            ConstructionBlockerKind.None => ConstructionBlockerAction.Commit,
            ConstructionBlockerKind.MovableFriendly => MovableFriendly,
            ConstructionBlockerKind.FriendlyHold => FriendlyHold,
            ConstructionBlockerKind.FriendlyEconomyTask => FriendlyEconomyTask,
            ConstructionBlockerKind.FriendlyAssignedBuilder =>
                FriendlyAssignedBuilder,
            ConstructionBlockerKind.FriendlyOtherOrder => FriendlyOtherOrder,
            ConstructionBlockerKind.AuthorityAlly => AuthorityAlly,
            ConstructionBlockerKind.AuthorityEnemy => AuthorityEnemy,
            _ => ConstructionBlockerAction.Wait
        };
}

public readonly record struct ConstructionBlocker(
    int Unit,
    ConstructionBlockerKind Kind);

public readonly record struct ConstructionEvictionAssignment(
    int Unit,
    Vector2 Target);

public enum ConstructionEvictionPlanCode : byte
{
    Clear,
    Planned,
    WaitForPolicy,
    InsufficientExitSlots,
    TooManyBlockers
}

public readonly record struct ConstructionEvictionPlan(
    ConstructionEvictionPlanCode Code,
    ConstructionBlockerKind WaitingKind,
    ConstructionEvictionAssignment[] Assignments)
{
    public bool CanIssue => Code == ConstructionEvictionPlanCode.Planned;
}

/// <summary>
/// Assigns stable, non-overlapping positions outside a construction footprint.
/// It does not mutate units or decide which blocker kinds may move.
/// </summary>
public static class ConstructionEvictionPlanner
{
    public const int MaximumBlockers = 64;
    private const int CandidateRings = 3;
    private const float ExitMargin = 4f;
    private const float SlotMargin = 4f;

    public static ConstructionEvictionPlan Plan(
        StaticWorld world,
        UnitStore units,
        SimRect footprint,
        float placementPadding,
        ReadOnlySpan<ConstructionBlocker> blockers,
        ConstructionBlockerPolicy policy)
    {
        if (!float.IsFinite(placementPadding) || placementPadding < 0f)
            throw new ArgumentOutOfRangeException(nameof(placementPadding));
        if (blockers.Length == 0)
        {
            return new ConstructionEvictionPlan(
                ConstructionEvictionPlanCode.Clear,
                ConstructionBlockerKind.None,
                []);
        }
        if (blockers.Length > MaximumBlockers)
        {
            return new ConstructionEvictionPlan(
                ConstructionEvictionPlanCode.TooManyBlockers,
                blockers[MaximumBlockers].Kind,
                []);
        }

        for (var index = 0; index < blockers.Length; index++)
        {
            var blocker = blockers[index];
            if (policy.Resolve(blocker.Kind) !=
                ConstructionBlockerAction.BeginEviction)
            {
                return new ConstructionEvictionPlan(
                    ConstructionEvictionPlanCode.WaitForPolicy,
                    blocker.Kind,
                    []);
            }
        }

        var assignments = new ConstructionEvictionAssignment[blockers.Length];
        for (var blockerIndex = 0;
             blockerIndex < blockers.Length;
             blockerIndex++)
        {
            var unit = blockers[blockerIndex].Unit;
            var radius = units.Radii[unit];
            var candidates = CreateCandidates(
                footprint, radius, placementPadding);
            var best = -1;
            var bestDistance = float.PositiveInfinity;
            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                if (!world.IsDiscFree(candidate, radius) ||
                    ConflictsWithStationaryUnit(
                        candidate, radius, unit, blockers, units) ||
                    ConflictsWithAssignment(
                        candidate, radius, assignments, blockerIndex, units))
                {
                    continue;
                }
                var distance = Vector2.DistanceSquared(
                    units.Positions[unit], candidate);
                if (distance >= bestDistance)
                    continue;
                best = candidateIndex;
                bestDistance = distance;
            }
            if (best < 0)
            {
                return new ConstructionEvictionPlan(
                    ConstructionEvictionPlanCode.InsufficientExitSlots,
                    ConstructionBlockerKind.MovableFriendly,
                    []);
            }
            assignments[blockerIndex] = new ConstructionEvictionAssignment(
                unit, candidates[best]);
        }

        return new ConstructionEvictionPlan(
            ConstructionEvictionPlanCode.Planned,
            ConstructionBlockerKind.None,
            assignments);
    }

    private static List<Vector2> CreateCandidates(
        SimRect footprint,
        float radius,
        float placementPadding)
    {
        var candidates = new List<Vector2>(128);
        var step = radius * 2f + SlotMargin;
        for (var ring = 0; ring < CandidateRings; ring++)
        {
            var expansion = radius + placementPadding + ExitMargin +
                            ring * step;
            var bounds = footprint.Expanded(expansion);
            AppendVerticalEdge(candidates, bounds.Min.X, bounds, step);
            AppendVerticalEdge(candidates, bounds.Max.X, bounds, step);
            AppendHorizontalEdge(candidates, bounds.Min.Y, bounds, step);
            AppendHorizontalEdge(candidates, bounds.Max.Y, bounds, step);
        }
        return candidates;
    }

    private static void AppendVerticalEdge(
        List<Vector2> output,
        float x,
        SimRect bounds,
        float step)
    {
        var center = (bounds.Min.Y + bounds.Max.Y) * 0.5f;
        output.Add(new Vector2(x, center));
        for (var offset = step;
             center - offset > bounds.Min.Y || center + offset < bounds.Max.Y;
             offset += step)
        {
            if (center - offset > bounds.Min.Y)
                output.Add(new Vector2(x, center - offset));
            if (center + offset < bounds.Max.Y)
                output.Add(new Vector2(x, center + offset));
        }
        output.Add(new Vector2(x, bounds.Min.Y));
        output.Add(new Vector2(x, bounds.Max.Y));
    }

    private static void AppendHorizontalEdge(
        List<Vector2> output,
        float y,
        SimRect bounds,
        float step)
    {
        var center = (bounds.Min.X + bounds.Max.X) * 0.5f;
        output.Add(new Vector2(center, y));
        for (var offset = step;
             center - offset > bounds.Min.X || center + offset < bounds.Max.X;
             offset += step)
        {
            if (center - offset > bounds.Min.X)
                output.Add(new Vector2(center - offset, y));
            if (center + offset < bounds.Max.X)
                output.Add(new Vector2(center + offset, y));
        }
    }

    private static bool ConflictsWithStationaryUnit(
        Vector2 candidate,
        float radius,
        int movingUnit,
        ReadOnlySpan<ConstructionBlocker> blockers,
        UnitStore units)
    {
        for (var other = 0; other < units.Count; other++)
        {
            if (other == movingUnit || !units.Alive[other] ||
                IsEvacuatingBlocker(other, blockers))
            {
                continue;
            }
            var minimum = radius + units.Radii[other] + SlotMargin;
            if (Vector2.DistanceSquared(candidate, units.Positions[other]) <
                minimum * minimum)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsEvacuatingBlocker(
        int unit,
        ReadOnlySpan<ConstructionBlocker> blockers)
    {
        for (var index = 0; index < blockers.Length; index++)
        {
            if (blockers[index].Unit == unit)
                return true;
        }
        return false;
    }

    private static bool ConflictsWithAssignment(
        Vector2 candidate,
        float radius,
        ConstructionEvictionAssignment[] assignments,
        int assignmentCount,
        UnitStore units)
    {
        for (var index = 0; index < assignmentCount; index++)
        {
            var assigned = assignments[index];
            var minimum = radius + units.Radii[assigned.Unit] + SlotMargin;
            if (Vector2.DistanceSquared(candidate, assigned.Target) <
                minimum * minimum)
            {
                return true;
            }
        }
        return false;
    }
}
