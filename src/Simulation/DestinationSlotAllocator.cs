using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class DestinationSlotAllocator
{
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
        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            maximumRadius = MathF.Max(maximumRadius, units.Radii[unit]);
        }

        var target = _world.Bounds.Inset(maximumRadius + 2f).Clamp(requestedTarget);
        var spacing = maximumRadius * 2f + 4f;

        var candidates = GenerateCandidates(
            units,
            unitIndices,
            target,
            spacing,
            maximumRadius,
            unitIndices.Length);
        if (candidates.Count < unitIndices.Length)
        {
            throw new InvalidOperationException(
                $"Could not allocate {unitIndices.Length} legal destination slots near {target}.");
        }

        var selectedSlots = candidates
            .OrderBy(point => Vector2.DistanceSquared(point, target))
            .Take(unitIndices.Length)
            .ToArray();

        var orderedUnits = unitIndices.ToArray();
        Array.Sort(orderedUnits);
        var assignment = AssignMinimumDistance(units, orderedUnits, selectedSlots);

        var result = new Dictionary<int, Vector2>(unitIndices.Length);
        for (var i = 0; i < orderedUnits.Length; i++)
        {
            result.Add(orderedUnits[i], selectedSlots[assignment[i]]);
        }

        return result;
    }

    private List<Vector2> GenerateCandidates(
        UnitStore units,
        ReadOnlySpan<int> selectedUnits,
        Vector2 center,
        float spacing,
        float radius,
        int required)
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
                if (_world.IsDiscFree(point, radius) &&
                    !ConflictsWithExistingReservation(
                        units, selectedUnits, point, radius))
                {
                    result.Add(point);
                }
            }
        }

        return result;
    }

    private static bool ConflictsWithExistingReservation(
        UnitStore units,
        ReadOnlySpan<int> selectedUnits,
        Vector2 candidate,
        float candidateRadius)
    {
        for (var unit = 0; unit < units.Count; unit++)
        {
            var isSelected = false;
            for (var selectedIndex = 0; selectedIndex < selectedUnits.Length; selectedIndex++)
            {
                if (selectedUnits[selectedIndex] == unit)
                {
                    isSelected = true;
                    break;
                }
            }

            if (isSelected)
            {
                continue;
            }

            var minimumDistance = candidateRadius + units.Radii[unit] + 2f;
            if (Vector2.DistanceSquared(candidate, units.SlotTargets[unit]) <
                minimumDistance * minimumDistance)
            {
                return true;
            }
        }

        return false;
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
