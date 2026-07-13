using System.Numerics;

namespace RtsDemo.Simulation;

/// <summary>
/// Finds locally beneficial destination-slot exchanges after units have changed
/// order while travelling. It never creates or removes reservations; it only
/// exchanges two slots that already belong to the same move command.
/// </summary>
public sealed class DestinationSlotReflow
{
    private const float MinimumDistanceToReflow = 7f;
    private const float MaximumDistanceToReflow = 140f;
    private const float MinimumSquaredCostImprovement = 64f;
    private const float MaximumIndividualRegression = 10f;
    private const int MinimumGroupSize = 8;

    private readonly StaticWorld _world;

    public DestinationSlotReflow(StaticWorld world)
    {
        _world = world;
    }

    public bool TryFindSwap(
        UnitStore units,
        ReadOnlySpan<CombatTargetKind> combatTargets,
        long tick,
        out int firstUnit,
        out int secondUnit)
    {
        firstUnit = -1;
        secondUnit = -1;
        var bestImprovement = MinimumSquaredCostImprovement;

        for (var first = 0; first < units.Count; first++)
        {
            if (!IsCandidate(
                    units, combatTargets, first, tick,
                    out var firstDistanceSquared))
            {
                continue;
            }

            var groupId = units.MovementGroupIds[first];
            for (var second = first + 1; second < units.Count; second++)
            {
                if (units.MovementGroupIds[second] != groupId ||
                    !IsCandidate(
                        units, combatTargets, second, tick,
                        out var secondDistanceSquared))
                {
                    continue;
                }

                var firstToSecondSlotSquared = Vector2.DistanceSquared(
                    units.Positions[first], units.SlotTargets[second]);
                var secondToFirstSlotSquared = Vector2.DistanceSquared(
                    units.Positions[second], units.SlotTargets[first]);
                var currentCost = firstDistanceSquared + secondDistanceSquared;
                var swappedCost = firstToSecondSlotSquared + secondToFirstSlotSquared;
                var improvement = currentCost - swappedCost;
                if (improvement <= bestImprovement)
                {
                    continue;
                }

                var currentWorst = MathF.Sqrt(MathF.Max(
                    firstDistanceSquared, secondDistanceSquared));
                var swappedWorst = MathF.Sqrt(MathF.Max(
                    firstToSecondSlotSquared, secondToFirstSlotSquared));
                if (swappedWorst > currentWorst + MaximumIndividualRegression ||
                    !_world.IsSegmentFree(
                        units.Positions[first],
                        units.SlotTargets[second],
                        units.Radii[first]) ||
                    !_world.IsSegmentFree(
                        units.Positions[second],
                        units.SlotTargets[first],
                        units.Radii[second]))
                {
                    continue;
                }

                bestImprovement = improvement;
                firstUnit = first;
                secondUnit = second;
            }
        }

        return firstUnit >= 0;
    }

    private static bool IsCandidate(
        UnitStore units,
        ReadOnlySpan<CombatTargetKind> combatTargets,
        int unit,
        long tick,
        out float distanceSquared)
    {
        distanceSquared = Vector2.DistanceSquared(
            units.Positions[unit], units.SlotTargets[unit]);
        if (combatTargets[unit] != CombatTargetKind.None ||
            units.MovementGroupIds[unit] <= 0 ||
            units.MovementGroupSizes[unit] < MinimumGroupSize ||
            units.DestinationOverflowed[unit] ||
            units.DestinationYieldPhases[unit] != DestinationYieldPhase.None ||
            units.SlotReflowCooldownTicks[unit] > tick ||
            units.Modes[unit] is not (UnitMoveMode.Moving or UnitMoveMode.Arrived) ||
            units.Modes[unit] == UnitMoveMode.Arrived &&
            !HasMovingCohort(units, unit))
        {
            return false;
        }

        return distanceSquared >= MinimumDistanceToReflow * MinimumDistanceToReflow &&
               distanceSquared <= MaximumDistanceToReflow * MaximumDistanceToReflow;
    }

    private static bool HasMovingCohort(UnitStore units, int unit)
    {
        var groupId = units.MovementGroupIds[unit];
        for (var candidate = 0; candidate < units.Count; candidate++)
        {
            if (units.Alive[candidate] &&
                units.MovementGroupIds[candidate] == groupId &&
                units.Modes[candidate] == UnitMoveMode.Moving)
                return true;
        }
        return false;
    }
}
