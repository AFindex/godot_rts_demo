using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class DestinationSlotAllocator
{
    private const int ExactAssignmentLimit = 96;
    private readonly StaticWorld _world;

    public DestinationSlotAllocator(StaticWorld world)
    {
        _world = world;
    }

    public Dictionary<int, Vector2> Allocate(
        UnitStore units,
        ReadOnlySpan<int> unitIndices,
        Vector2 requestedTarget)
    {
        if (unitIndices.IsEmpty)
        {
            return new Dictionary<int, Vector2>();
        }

        var maximumRadius = 0f;
        var maximumNavigationRadius = 0f;
        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            maximumRadius = MathF.Max(maximumRadius, units.Radii[unit]);
            maximumNavigationRadius = MathF.Max(
                maximumNavigationRadius, units.NavigationRadii[unit]);
        }

        var target = _world.Bounds
            .Inset(maximumNavigationRadius + 2f)
            .Clamp(requestedTarget);
        var spacing = maximumRadius * 2f + 4f;

        var selectedMembership = new bool[units.Count];
        for (var index = 0; index < unitIndices.Length; index++)
            selectedMembership[unitIndices[index]] = true;
        var ignoreExternalReservations =
            unitIndices.Length > ExactAssignmentLimit;
        var candidates = GenerateCandidates(
            units,
            selectedMembership,
            target,
            spacing,
            maximumRadius,
            maximumNavigationRadius,
            unitIndices.Length,
            ignoreExternalReservations);
        if (candidates.Count < unitIndices.Length)
        {
            // Destination reservations are advisory crowd organization, not
            // static navigation blockers. Near a map edge or a popular rally
            // point they can temporarily cover every candidate. Relax them
            // before rejecting an otherwise legal command; collision and
            // destination-yield logic still serialize the eventual arrival.
            candidates = GenerateCandidates(
                units,
                selectedMembership,
                target,
                spacing,
                maximumRadius,
                maximumNavigationRadius,
                unitIndices.Length,
                ignoreExternalReservations: true);
        }
        if (candidates.Count < unitIndices.Length)
        {
            throw new InvalidOperationException(
                $"Could not allocate {unitIndices.Length} statically legal destination slots near {target}.");
        }

        var selectedSlots = candidates
            .OrderBy(point => Vector2.DistanceSquared(point, target))
            .Take(unitIndices.Length)
            .ToArray();

        var orderedUnits = unitIndices.ToArray();
        Array.Sort(orderedUnits);
        var assignment = orderedUnits.Length <= ExactAssignmentLimit
            ? AssignMinimumDistance(units, orderedUnits, selectedSlots)
            : AssignMonotoneLargeGroup(units, orderedUnits, selectedSlots);

        var result = new Dictionary<int, Vector2>(unitIndices.Length);
        for (var i = 0; i < orderedUnits.Length; i++)
        {
            result.Add(orderedUnits[i], selectedSlots[assignment[i]]);
        }

        return result;
    }

    private List<Vector2> GenerateCandidates(
        UnitStore units,
        bool[] selectedMembership,
        Vector2 center,
        float spacing,
        float physicalRadius,
        float navigationRadius,
        int required,
        bool ignoreExternalReservations)
    {
        var result = new List<Vector2>(required * 2);
        var maxRing = Math.Max(4, (int)MathF.Ceiling(MathF.Sqrt(required)) + 8);
        var rowHeight = spacing * 0.8660254f;

        for (var row = -maxRing; row <= maxRing; row++)
        {
            var offset = (Math.Abs(row) & 1) == 1 ? spacing * 0.5f : 0f;
            for (var column = -maxRing; column <= maxRing; column++)
            {
                var point = center + new Vector2(column * spacing + offset, row * rowHeight);
                if (_world.IsDiscFree(point, navigationRadius) &&
                    (ignoreExternalReservations ||
                     !ConflictsWithExistingReservation(
                         units, selectedMembership, point, physicalRadius)))
                {
                    result.Add(point);
                }
            }
        }

        return result;
    }

    private static bool ConflictsWithExistingReservation(
        UnitStore units,
        bool[] selectedMembership,
        Vector2 candidate,
        float candidateRadius)
    {
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (!units.Alive[unit])
            {
                continue;
            }
            if (selectedMembership[unit])
            {
                continue;
            }

            var minimumDistance = candidateRadius + units.Radii[unit] + 2f;
            if (Vector2.DistanceSquared(candidate, units.SlotTargets[unit]) <
                minimumDistance * minimumDistance)
            {
                return true;
            }

            if ((units.Modes[unit] is
                     UnitMoveMode.Idle or UnitMoveMode.Arrived or UnitMoveMode.Hold) &&
                Vector2.DistanceSquared(candidate, units.Positions[unit]) <
                    minimumDistance * minimumDistance)
            {
                return true;
            }

            if (units.DestinationYieldPhases[unit] != DestinationYieldPhase.None &&
                Vector2.DistanceSquared(
                    candidate, units.DestinationYieldReturnTargets[unit]) <
                minimumDistance * minimumDistance)
            {
                return true;
            }

            if (units.DestinationYieldPhases[unit] != DestinationYieldPhase.None &&
                Vector2.DistanceSquared(candidate, units.DestinationYieldPoints[unit]) <
                minimumDistance * minimumDistance)
            {
                return true;
            }
        }

        return false;
    }

    private static int[] AssignMonotoneLargeGroup(
        UnitStore units,
        int[] unitIndices,
        Vector2[] slots)
    {
        var count = unitIndices.Length;
        var unitOrder = Enumerable.Range(0, count).ToArray();
        var slotOrder = Enumerable.Range(0, count).ToArray();
        Array.Sort(unitOrder, (left, right) =>
        {
            var leftPosition = units.Positions[unitIndices[left]];
            var rightPosition = units.Positions[unitIndices[right]];
            var vertical = leftPosition.Y.CompareTo(rightPosition.Y);
            if (vertical != 0) return vertical;
            var horizontal = leftPosition.X.CompareTo(rightPosition.X);
            return horizontal != 0 ? horizontal : left.CompareTo(right);
        });
        Array.Sort(slotOrder, (left, right) =>
        {
            var vertical = slots[left].Y.CompareTo(slots[right].Y);
            if (vertical != 0) return vertical;
            var horizontal = slots[left].X.CompareTo(slots[right].X);
            return horizontal != 0 ? horizontal : left.CompareTo(right);
        });

        var assignment = new int[count];
        for (var rank = 0; rank < count; rank++)
            assignment[unitOrder[rank]] = slotOrder[rank];
        return assignment;
    }

    private static int[] AssignMinimumDistance(
        UnitStore units,
        int[] unitIndices,
        Vector2[] slots)
    {
        var count = unitIndices.Length;
        var rowPotential = new double[count + 1];
        var columnPotential = new double[count + 1];
        var matchedRow = new int[count + 1];
        var previousColumn = new int[count + 1];

        for (var row = 1; row <= count; row++)
        {
            matchedRow[0] = row;
            var column = 0;
            var minimum = new double[count + 1];
            Array.Fill(minimum, double.PositiveInfinity);
            var used = new bool[count + 1];

            do
            {
                used[column] = true;
                var currentRow = matchedRow[column];
                var delta = double.PositiveInfinity;
                var nextColumn = 0;
                for (var candidateColumn = 1; candidateColumn <= count; candidateColumn++)
                {
                    if (used[candidateColumn])
                    {
                        continue;
                    }

                    var cost = Vector2.DistanceSquared(
                        units.Positions[unitIndices[currentRow - 1]],
                        slots[candidateColumn - 1]);
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

                for (var candidateColumn = 0; candidateColumn <= count; candidateColumn++)
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

        var assignment = new int[count];
        for (var column = 1; column <= count; column++)
        {
            assignment[matchedRow[column] - 1] = column - 1;
        }

        return assignment;
    }
}
