using System.Numerics;

namespace RtsDemo.Simulation;

/// <summary>
/// Reassigns a bounded local set of existing destination reservations. The
/// rematch is atomic, deterministic and only accepted when at least three
/// units improve their aggregate travel cost without a large regression.
/// </summary>
public sealed class DestinationLocalRematcher
{
    public const int MaximumUnits = 24;

    private const int MinimumUnits = 8;
    private const int MaximumSettledCandidates = 4;
    private const int MinimumGroupSize = 48;
    private const int MinimumStallTicks = 90;
    private const int MinimumNearTicks = 180;
    private const int MaximumNearTicks = 720;
    private const float MaximumDistanceToSlot = 180f;
    private const float MaximumIndividualRegression = 18f;
    private const float MinimumSquaredCostImprovement = 144f;
    private const float EntryOrderPenalty = 225f;

    private readonly StaticWorld _world;

    public DestinationLocalRematcher(StaticWorld world)
    {
        _world = world;
    }

    public bool TryBuildPlan(
        UnitStore units,
        long tick,
        Span<int> rematchedUnits,
        Span<Vector2> rematchedTargets,
        out int rematchedCount)
    {
        if (rematchedUnits.Length < MaximumUnits ||
            rematchedTargets.Length < MaximumUnits)
        {
            throw new ArgumentException(
                $"Rematch buffers must contain at least {MaximumUnits} elements.");
        }

        rematchedCount = 0;
        Span<int> candidates = stackalloc int[MaximumUnits];
        foreach (var anchor in units.AliveUnits)
        {
            if (!IsAnchor(units, anchor, tick))
            {
                continue;
            }

            var candidateCount = SelectLocalCandidates(
                units, anchor, tick, candidates);
            if (candidateCount < MinimumUnits ||
                !TryAssign(
                    units,
                    candidates[..candidateCount],
                    rematchedUnits,
                    rematchedTargets,
                    out rematchedCount))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private int SelectLocalCandidates(
        UnitStore units,
        int anchor,
        long tick,
        Span<int> candidates)
    {
        candidates[0] = anchor;
        var count = 1;
        var groupId = units.MovementGroupIds[anchor];
        var anchorPosition = units.Positions[anchor];
        var anchorTarget = units.SlotTargets[anchor];
        var settledCandidates = 0;
        while (count < candidates.Length)
        {
            var bestUnit = -1;
            var bestScore = float.PositiveInfinity;
            foreach (var unit in units.AliveUnits)
            {
                if (units.MovementGroupIds[unit] != groupId ||
                    Contains(candidates[..count], unit) ||
                    !IsCandidate(units, unit, tick) ||
                    (units.Modes[unit] == UnitMoveMode.Arrived &&
                     settledCandidates >= MaximumSettledCandidates))
                {
                    continue;
                }

                var score = Vector2.DistanceSquared(
                                units.Positions[unit], anchorPosition) +
                            Vector2.DistanceSquared(
                                units.SlotTargets[unit], anchorTarget) * 0.35f;
                if (units.Modes[unit] == UnitMoveMode.Arrived)
                    score += 1_000_000f;
                if (score < bestScore - 0.0001f ||
                    (MathF.Abs(score - bestScore) <= 0.0001f && unit < bestUnit))
                {
                    bestScore = score;
                    bestUnit = unit;
                }
            }

            if (bestUnit < 0)
            {
                break;
            }

            candidates[count++] = bestUnit;
            if (units.Modes[bestUnit] == UnitMoveMode.Arrived)
                settledCandidates++;
        }

        return count;
    }

    private bool TryAssign(
        UnitStore units,
        ReadOnlySpan<int> candidates,
        Span<int> rematchedUnits,
        Span<Vector2> rematchedTargets,
        out int rematchedCount)
    {
        var count = candidates.Length;
        Span<Vector2> slots = stackalloc Vector2[MaximumUnits];
        Span<float> unitEntryRanks = stackalloc float[MaximumUnits];
        Span<float> slotDepthRanks = stackalloc float[MaximumUnits];
        for (var index = 0; index < count; index++)
        {
            slots[index] = units.SlotTargets[candidates[index]];
        }

        CalculateEntryRanks(
            units, candidates, slots[..count], unitEntryRanks, slotDepthRanks);

        Span<int> assignment = stackalloc int[MaximumUnits];
        AssignMinimumCost(
            units,
            candidates,
            slots[..count],
            unitEntryRanks,
            slotDepthRanks,
            assignment);

        var currentCost = 0f;
        var rematchedCost = 0f;
        rematchedCount = 0;
        for (var index = 0; index < count; index++)
        {
            var unit = candidates[index];
            var targetIndex = assignment[index];
            var currentDistance = Vector2.Distance(
                units.Positions[unit], units.SlotTargets[unit]);
            var rematchedDistance = Vector2.Distance(
                units.Positions[unit], slots[targetIndex]);
            currentCost += currentDistance * currentDistance;
            rematchedCost += rematchedDistance * rematchedDistance;

            if (targetIndex == index)
            {
                continue;
            }

            if (rematchedDistance > currentDistance + MaximumIndividualRegression ||
                !_world.IsSegmentFree(
                    units.Positions[unit], slots[targetIndex], units.Radii[unit]))
            {
                rematchedCount = 0;
                return false;
            }

            rematchedUnits[rematchedCount] = unit;
            rematchedTargets[rematchedCount] = slots[targetIndex];
            rematchedCount++;
        }

        if (rematchedCount < 3 ||
            currentCost - rematchedCost < MinimumSquaredCostImprovement)
        {
            rematchedCount = 0;
            return false;
        }

        return true;
    }

    private static void CalculateEntryRanks(
        UnitStore units,
        ReadOnlySpan<int> candidates,
        ReadOnlySpan<Vector2> slots,
        Span<float> unitRanks,
        Span<float> slotRanks)
    {
        var centroid = Vector2.Zero;
        for (var index = 0; index < candidates.Length; index++)
        {
            centroid += units.Positions[candidates[index]];
        }

        centroid /= candidates.Length;
        var approach = units.MoveGoals[candidates[0]] - centroid;
        if (approach.LengthSquared() <= 0.0001f)
        {
            approach = Vector2.UnitX;
        }
        else
        {
            approach = Vector2.Normalize(approach);
        }

        var minimumUnit = float.PositiveInfinity;
        var maximumUnit = float.NegativeInfinity;
        var minimumSlot = float.PositiveInfinity;
        var maximumSlot = float.NegativeInfinity;
        for (var index = 0; index < candidates.Length; index++)
        {
            unitRanks[index] = Vector2.Dot(units.Positions[candidates[index]], approach);
            slotRanks[index] = Vector2.Dot(slots[index], approach);
            minimumUnit = MathF.Min(minimumUnit, unitRanks[index]);
            maximumUnit = MathF.Max(maximumUnit, unitRanks[index]);
            minimumSlot = MathF.Min(minimumSlot, slotRanks[index]);
            maximumSlot = MathF.Max(maximumSlot, slotRanks[index]);
        }

        Normalize(unitRanks[..candidates.Length], minimumUnit, maximumUnit);
        Normalize(slotRanks[..candidates.Length], minimumSlot, maximumSlot);
    }

    private static void AssignMinimumCost(
        UnitStore units,
        ReadOnlySpan<int> candidates,
        ReadOnlySpan<Vector2> slots,
        ReadOnlySpan<float> unitRanks,
        ReadOnlySpan<float> slotRanks,
        Span<int> assignment)
    {
        var count = candidates.Length;
        Span<double> rowPotential = stackalloc double[MaximumUnits + 1];
        Span<double> columnPotential = stackalloc double[MaximumUnits + 1];
        Span<int> matchedRow = stackalloc int[MaximumUnits + 1];
        Span<int> previousColumn = stackalloc int[MaximumUnits + 1];
        Span<double> minimum = stackalloc double[MaximumUnits + 1];
        Span<bool> used = stackalloc bool[MaximumUnits + 1];

        for (var row = 1; row <= count; row++)
        {
            matchedRow[0] = row;
            var column = 0;
            minimum[..(count + 1)].Fill(double.PositiveInfinity);
            used[..(count + 1)].Clear();

            do
            {
                used[column] = true;
                var currentRow = matchedRow[column];
                var delta = double.PositiveInfinity;
                var nextColumn = 0;
                for (var candidateColumn = 1;
                     candidateColumn <= count;
                     candidateColumn++)
                {
                    if (used[candidateColumn])
                    {
                        continue;
                    }

                    var rankDifference =
                        unitRanks[currentRow - 1] - slotRanks[candidateColumn - 1];
                    var cost = Vector2.DistanceSquared(
                                   units.Positions[candidates[currentRow - 1]],
                                   slots[candidateColumn - 1]) +
                               rankDifference * rankDifference * EntryOrderPenalty;
                    var reducedCost = cost - rowPotential[currentRow] -
                                      columnPotential[candidateColumn];
                    if (reducedCost < minimum[candidateColumn])
                    {
                        minimum[candidateColumn] = reducedCost;
                        previousColumn[candidateColumn] = column;
                    }

                    if (minimum[candidateColumn] < delta)
                    {
                        delta = minimum[candidateColumn];
                        nextColumn = candidateColumn;
                    }
                }

                for (var candidateColumn = 0;
                     candidateColumn <= count;
                     candidateColumn++)
                {
                    if (used[candidateColumn])
                    {
                        rowPotential[matchedRow[candidateColumn]] += delta;
                        columnPotential[candidateColumn] -= delta;
                    }
                    else
                    {
                        minimum[candidateColumn] -= delta;
                    }
                }

                column = nextColumn;
            }
            while (matchedRow[column] != 0);

            do
            {
                var previous = previousColumn[column];
                matchedRow[column] = matchedRow[previous];
                column = previous;
            }
            while (column != 0);
        }

        for (var column = 1; column <= count; column++)
        {
            assignment[matchedRow[column] - 1] = column - 1;
        }
    }

    private static bool IsAnchor(UnitStore units, int unit, long tick) =>
        IsCandidate(units, unit, tick) &&
        units.DestinationNearTicks[unit] <= MaximumNearTicks &&
        (units.DestinationStallTicks[unit] >= MinimumStallTicks ||
         units.DestinationNearTicks[unit] >= MinimumNearTicks) &&
        HasMovingCohort(units, unit);

    private static bool IsCandidate(UnitStore units, int unit, long tick)
    {
        var distanceSquared = Vector2.DistanceSquared(
            units.Positions[unit], units.SlotTargets[unit]);
        return units.MovementGroupIds[unit] > 0 &&
               units.MovementGroupSizes[unit] >= MinimumGroupSize &&
               !units.DestinationOverflowed[unit] &&
               units.DestinationYieldPhases[unit] == DestinationYieldPhase.None &&
               units.SlotReflowCooldownTicks[unit] <= tick &&
               units.Modes[unit] is UnitMoveMode.Moving or UnitMoveMode.Arrived &&
               distanceSquared <= MaximumDistanceToSlot * MaximumDistanceToSlot;
    }

    private static bool HasMovingCohort(UnitStore units, int anchor)
    {
        var groupId = units.MovementGroupIds[anchor];
        var required = Math.Max(MinimumUnits, units.MovementGroupSizes[anchor] / 5);
        var moving = 0;
        foreach (var unit in units.AliveUnits)
        {
            if (units.MovementGroupIds[unit] == groupId &&
                units.Modes[unit] == UnitMoveMode.Moving &&
                !units.DestinationOverflowed[unit] &&
                units.DestinationYieldPhases[unit] == DestinationYieldPhase.None &&
                ++moving >= required)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(ReadOnlySpan<int> values, int value)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static void Normalize(Span<float> values, float minimum, float maximum)
    {
        var range = maximum - minimum;
        if (range <= 0.0001f)
        {
            values.Clear();
            return;
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = (values[index] - minimum) / range;
        }
    }
}
