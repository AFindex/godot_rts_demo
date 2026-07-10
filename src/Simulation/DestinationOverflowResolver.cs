using System.Numerics;

namespace RtsDemo.Simulation;

/// <summary>
/// Detects units that are locally stalled behind settled agents around the
/// same destination and moves their unreachable reservation to a unique outer slot.
/// This is a bounded terminal fallback, not a replacement for normal slot flow.
/// </summary>
public sealed class DestinationOverflowResolver
{
    private const int MinimumGroupSize = 32;
    private const int MinimumStallTicks = 150;
    private const int MaximumNearGoalTicks = 600;
    private const float MinimumTrackedDistance = 9f;
    private const float MaximumTrackedDistance = 180f;
    private const float MaximumTrackedExitDistance = 260f;
    private const float ProgressDistance = 3f;
    private const int OverflowRings = 6;

    private readonly StaticWorld _world;

    public DestinationOverflowResolver(StaticWorld world)
    {
        _world = world;
    }

    public void UpdateStallTracking(UnitStore units)
    {
        for (var unit = 0; unit < units.Count; unit++)
        {
            var distance = Vector2.Distance(
                units.Positions[unit], units.SlotTargets[unit]);
            if (units.Modes[unit] == UnitMoveMode.Arrived &&
                units.MovementGroupIds[unit] > 0 &&
                units.MovementGroupSizes[unit] >= MinimumGroupSize &&
                !units.DestinationOverflowed[unit])
            {
                units.DestinationStallTicks[unit] = 0;
                units.DestinationBestDistances[unit] = distance;
                continue;
            }

            var maximumDistance = units.DestinationNearTicks[unit] > 0
                ? MaximumTrackedExitDistance
                : MaximumTrackedDistance;
            var minimumDistance = units.DestinationNearTicks[unit] > 0
                ? 0f
                : MinimumTrackedDistance;
            var insideTerminalRegion =
                units.Modes[unit] is UnitMoveMode.Moving or UnitMoveMode.WaitingForPath &&
                units.ChokePhases[unit] == ChokePhase.None &&
                units.MovementGroupIds[unit] > 0 &&
                units.MovementGroupSizes[unit] >= MinimumGroupSize &&
                !units.DestinationOverflowed[unit] &&
                distance >= minimumDistance &&
                distance <= maximumDistance;
            if (!insideTerminalRegion)
            {
                units.DestinationStallTicks[unit] = 0;
                units.DestinationNearTicks[unit] = 0;
                units.DestinationBestDistances[unit] = distance;
                continue;
            }

            units.DestinationNearTicks[unit]++;
            if (units.Modes[unit] != UnitMoveMode.Moving || units.PathPending[unit])
            {
                continue;
            }

            var best = units.DestinationBestDistances[unit];
            if (best <= 0f || distance <= best - ProgressDistance)
            {
                units.DestinationBestDistances[unit] = distance;
                units.DestinationStallTicks[unit] = 0;
                continue;
            }

            units.DestinationStallTicks[unit]++;
        }
    }

    public bool TryFindOverflowAssignment(
        UnitStore units,
        out int unit,
        out Vector2 overflowTarget)
    {
        for (var candidateUnit = 0; candidateUnit < units.Count; candidateUnit++)
        {
            var hardTimeout = units.DestinationNearTicks[candidateUnit] >=
                              MaximumNearGoalTicks;
            if ((units.DestinationStallTicks[candidateUnit] < MinimumStallTicks &&
                 !hardTimeout) ||
                units.DestinationOverflowed[candidateUnit] ||
                units.Modes[candidateUnit] != UnitMoveMode.Moving ||
                (!hardTimeout && !IsBlockedBySettledGroup(units, candidateUnit)) ||
                !TryFindOverflowTarget(units, candidateUnit, out overflowTarget))
            {
                continue;
            }

            unit = candidateUnit;
            return true;
        }

        unit = -1;
        overflowTarget = Vector2.Zero;
        return false;
    }

    private bool TryFindOverflowTarget(
        UnitStore units,
        int unit,
        out Vector2 target)
    {
        var groupId = units.MovementGroupIds[unit];
        var groupGoal = units.MoveGoals[unit];
        var largestRadius = units.Radii[unit];
        for (var candidate = 0; candidate < units.Count; candidate++)
        {
            if (units.MovementGroupIds[candidate] == groupId ||
                (units.MovementGroupIds[candidate] > 0 &&
                 Vector2.DistanceSquared(units.MoveGoals[candidate], groupGoal) <= 2f * 2f))
            {
                largestRadius = MathF.Max(largestRadius, units.Radii[candidate]);
            }
        }

        var spacing = largestRadius * 2f + 4f;
        var goalPopulation = 0;
        for (var candidate = 0; candidate < units.Count; candidate++)
        {
            if (units.MovementGroupIds[candidate] > 0 &&
                Vector2.DistanceSquared(units.MoveGoals[candidate], groupGoal) <= 2f * 2f)
            {
                goalPopulation++;
            }
        }

        var groupSize = Math.Max(
            Math.Max(MinimumGroupSize, units.MovementGroupSizes[unit]),
            goalPopulation);
        var coreRadius = spacing * (MathF.Sqrt(groupSize) * 0.62f + 1.5f);
        var angleOffset = DeterministicAngle(groupId);
        var bestScore = float.PositiveInfinity;
        target = Vector2.Zero;

        for (var ring = 0; ring < OverflowRings; ring++)
        {
            var radius = coreRadius + ring * spacing;
            var samples = Math.Max(24, (int)MathF.Ceiling(MathF.Tau * radius / spacing));
            for (var sample = 0; sample < samples; sample++)
            {
                var angle = angleOffset + sample * MathF.Tau / samples;
                var point = groupGoal + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                if (!_world.IsDiscFree(point, units.Radii[unit]) ||
                    !_world.IsSegmentFree(
                        units.Positions[unit], point, units.Radii[unit]) ||
                    HasReservationOrOccupancyConflict(units, unit, point))
                {
                    continue;
                }

                var score = Vector2.DistanceSquared(units.Positions[unit], point) +
                            ring * spacing * spacing;
                if (score < bestScore)
                {
                    bestScore = score;
                    target = point;
                }
            }
        }

        return float.IsFinite(bestScore);
    }

    private static bool HasReservationOrOccupancyConflict(
        UnitStore units,
        int unit,
        Vector2 candidate)
    {
        for (var other = 0; other < units.Count; other++)
        {
            if (other == unit)
            {
                continue;
            }

            var minimumDistance = units.Radii[unit] + units.Radii[other] + 2f;
            var minimumDistanceSquared = minimumDistance * minimumDistance;
            if (Vector2.DistanceSquared(candidate, units.SlotTargets[other]) <
                    minimumDistanceSquared ||
                Vector2.DistanceSquared(candidate, units.Positions[other]) <
                    minimumDistanceSquared)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlockedBySettledGroup(UnitStore units, int unit)
    {
        var position = units.Positions[unit];
        var target = units.SlotTargets[unit];
        var segment = target - position;
        var segmentLengthSquared = segment.LengthSquared();
        if (segmentLengthSquared <= 0.0001f)
        {
            return false;
        }

        var nearbyBlockers = 0;
        for (var other = 0; other < units.Count; other++)
        {
            if (other == unit ||
                !IsSettledBlocker(units, other))
            {
                continue;
            }

            var offset = units.Positions[other] - position;
            var projection = Vector2.Dot(offset, segment) / segmentLengthSquared;
            var closest = position + segment * Math.Clamp(projection, 0f, 1f);
            var clearance = units.Radii[unit] + units.Radii[other] + 2.5f;
            if (projection > 0.02f && projection < 1.08f &&
                Vector2.DistanceSquared(units.Positions[other], closest) <
                clearance * clearance)
            {
                return true;
            }

            var nearbyDistance = clearance * 2.2f;
            if (projection > -0.15f &&
                Vector2.DistanceSquared(units.Positions[other], position) <
                nearbyDistance * nearbyDistance)
            {
                nearbyBlockers++;
            }
        }

        return nearbyBlockers >= 2;
    }

    private static bool IsSettledBlocker(UnitStore units, int unit) =>
        units.Modes[unit] == UnitMoveMode.Arrived ||
        (!units.PathPending[unit] &&
         Vector2.DistanceSquared(units.Positions[unit], units.SlotTargets[unit]) <=
         18f * 18f);

    private static float DeterministicAngle(int groupId)
    {
        var hash = unchecked((uint)groupId * 2654435761u);
        return hash / (float)uint.MaxValue * MathF.Tau;
    }
}
