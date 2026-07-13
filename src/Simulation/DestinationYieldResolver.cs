using System.Numerics;

namespace RtsDemo.Simulation;

/// <summary>
/// Selects a settled blocker and a temporary side point that opens a local
/// destination corridor while preserving the blocker's original reservation.
/// </summary>
public sealed class DestinationYieldResolver
{
    private const int MinimumGroupSize = 8;
    private const int MinimumStallTicks = 120;
    private const int MinimumNearTicks = 240;
    private const int MaximumNearTicks = 720;
    private static readonly float[] RadialWeights = [0.35f, 0.55f, 0.8f, 1.1f];

    private readonly StaticWorld _world;

    public DestinationYieldResolver(StaticWorld world)
    {
        _world = world;
    }

    public bool TryFindYield(
        UnitStore units,
        ReadOnlySpan<CombatTargetKind> combatTargets,
        long tick,
        out int blockedUnit,
        out int blockerUnit,
        out Vector2 yieldPoint)
    {
        for (var candidate = 0; candidate < units.Count; candidate++)
        {
            if (!units.Alive[candidate] ||
                combatTargets[candidate] != CombatTargetKind.None ||
                units.Modes[candidate] != UnitMoveMode.Moving ||
                units.PathPending[candidate] ||
                units.MovementGroupSizes[candidate] < MinimumGroupSize ||
                units.DestinationOverflowed[candidate] ||
                units.DestinationYieldPhases[candidate] != DestinationYieldPhase.None ||
                units.DestinationNearTicks[candidate] > MaximumNearTicks ||
                (units.DestinationStallTicks[candidate] < MinimumStallTicks &&
                 units.DestinationNearTicks[candidate] < MinimumNearTicks) ||
                !TryFindBlocker(
                    units, combatTargets, candidate, tick, out var blocker) ||
                !TryFindYieldPoint(units, candidate, blocker, out yieldPoint))
            {
                continue;
            }

            blockedUnit = candidate;
            blockerUnit = blocker;
            return true;
        }

        blockedUnit = -1;
        blockerUnit = -1;
        yieldPoint = Vector2.Zero;
        return false;
    }

    private static bool TryFindBlocker(
        UnitStore units,
        ReadOnlySpan<CombatTargetKind> combatTargets,
        int blockedUnit,
        long tick,
        out int blockerUnit)
    {
        var position = units.Positions[blockedUnit];
        var target = units.SlotTargets[blockedUnit];
        var segment = target - position;
        var lengthSquared = segment.LengthSquared();
        blockerUnit = -1;
        if (lengthSquared <= 0.0001f)
        {
            return false;
        }

        var bestScore = float.PositiveInfinity;
        for (var other = 0; other < units.Count; other++)
        {
            if (other == blockedUnit || !units.Alive[other] ||
                combatTargets[other] != CombatTargetKind.None ||
                !IsSettledBlocker(units, other) ||
                units.DestinationOverflowed[other] ||
                units.DestinationYieldPhases[other] != DestinationYieldPhase.None ||
                units.DestinationYieldCooldownTicks[other] > tick)
            {
                continue;
            }

            var offset = units.Positions[other] - position;
            var projection = Vector2.Dot(offset, segment) / lengthSquared;
            if (projection <= -0.1f || projection >= 1.15f)
            {
                continue;
            }

            var closest = position + segment * Math.Clamp(projection, 0f, 1f);
            var lateralSquared = Vector2.DistanceSquared(units.Positions[other], closest);
            var clearance = units.Radii[blockedUnit] + units.Radii[other] + 3f;
            var maximumLateral = clearance * 2.4f;
            if (lateralSquared > maximumLateral * maximumLateral)
            {
                continue;
            }

            var score = lateralSquared + MathF.Abs(projection - 0.55f) * 24f;
            if (score < bestScore - 0.0001f ||
                (MathF.Abs(score - bestScore) <= 0.0001f && other < blockerUnit))
            {
                bestScore = score;
                blockerUnit = other;
            }
        }

        return blockerUnit >= 0;
    }

    private bool TryFindYieldPoint(
        UnitStore units,
        int blockedUnit,
        int blockerUnit,
        out Vector2 yieldPoint)
    {
        var blockerPosition = units.Positions[blockerUnit];
        var corridor = units.SlotTargets[blockedUnit] - units.Positions[blockedUnit];
        if (corridor.LengthSquared() <= 0.0001f)
        {
            corridor = DeterministicDirection(blockedUnit, blockerUnit);
        }
        else
        {
            corridor = Vector2.Normalize(corridor);
        }

        var outward = blockerPosition - units.MoveGoals[blockedUnit];
        if (outward.LengthSquared() <= 0.0001f)
        {
            outward = -corridor;
        }
        else
        {
            outward = Vector2.Normalize(outward);
        }

        var tangent = new Vector2(-corridor.Y, corridor.X);
        var baseDistance = MathF.Max(
            48f,
            (units.Radii[blockedUnit] + units.Radii[blockerUnit] + 4f) * 2.8f);
        var bestScore = float.PositiveInfinity;
        yieldPoint = Vector2.Zero;

        var preferredSide = ((blockedUnit ^ blockerUnit) & 1) == 0 ? 1f : -1f;
        for (var distanceIndex = 0; distanceIndex < 2; distanceIndex++)
        {
            var offsetDistance = baseDistance * (1f + distanceIndex * 0.35f);
            for (var option = 0; option < RadialWeights.Length * 2; option++)
            {
                var side = (option & 1) == 0 ? preferredSide : -preferredSide;
                var radialWeight = RadialWeights[option / 2];
                var direction = Vector2.Normalize(tangent * side + outward * radialWeight);
                var candidate = blockerPosition + direction * offsetDistance;
                if (!_world.IsDiscFree(candidate, units.Radii[blockerUnit]) ||
                    !_world.IsSegmentFree(
                        blockerPosition, candidate, units.Radii[blockerUnit]) ||
                    HasConflict(units, blockerUnit, candidate))
                {
                    continue;
                }

                var corridorAlignment = MathF.Abs(Vector2.Dot(direction, corridor));
                var distanceFromBlocked = Vector2.DistanceSquared(
                    candidate, units.Positions[blockedUnit]);
                var score = Vector2.DistanceSquared(candidate, blockerPosition) +
                            corridorAlignment * 400f - distanceFromBlocked * 0.04f;
                if (score < bestScore)
                {
                    bestScore = score;
                    yieldPoint = candidate;
                }
            }
        }

        return float.IsFinite(bestScore);
    }

    private static bool HasConflict(
        UnitStore units,
        int blockerUnit,
        Vector2 candidate)
    {
        for (var other = 0; other < units.Count; other++)
        {
            if (other == blockerUnit || !units.Alive[other])
            {
                continue;
            }

            var minimumDistance = units.Radii[blockerUnit] + units.Radii[other] + 2f;
            var minimumDistanceSquared = minimumDistance * minimumDistance;
            if (Vector2.DistanceSquared(candidate, units.Positions[other]) <
                    minimumDistanceSquared ||
                Vector2.DistanceSquared(candidate, units.SlotTargets[other]) <
                    minimumDistanceSquared)
            {
                return true;
            }

            if (units.DestinationYieldPhases[other] != DestinationYieldPhase.None &&
                Vector2.DistanceSquared(
                    candidate, units.DestinationYieldReturnTargets[other]) <
                minimumDistanceSquared)
            {
                return true;
            }

            if (units.DestinationYieldPhases[other] != DestinationYieldPhase.None &&
                Vector2.DistanceSquared(candidate, units.DestinationYieldPoints[other]) <
                minimumDistanceSquared)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSettledBlocker(UnitStore units, int unit) =>
        units.Modes[unit] == UnitMoveMode.Arrived ||
        (!units.PathPending[unit] &&
         Vector2.DistanceSquared(units.Positions[unit], units.SlotTargets[unit]) <=
         18f * 18f);

    private static float DeterministicAngle(int blockedUnit, int blockerUnit)
    {
        var hash = unchecked(
            (uint)(blockedUnit * 73856093) ^ (uint)(blockerUnit * 19349663));
        return hash / (float)uint.MaxValue * MathF.Tau;
    }

    private static Vector2 DeterministicDirection(int left, int right)
    {
        var angle = DeterministicAngle(left, right);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}
