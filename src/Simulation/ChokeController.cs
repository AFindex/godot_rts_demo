using System.Numerics;

namespace RtsDemo.Simulation;

public enum ChokePhase : byte
{
    None,
    Approaching,
    Traversing,
    Exiting
}

public readonly record struct ChokeDefinition(
    int Id,
    Vector2 A,
    Vector2 B,
    float Width,
    float ApproachDistance)
{
    public Vector2 Axis => Vector2.Normalize(B - A);
    public Vector2 Normal
    {
        get
        {
            var axis = Axis;
            return new Vector2(-axis.Y, axis.X);
        }
    }
    public float Length => Vector2.Distance(A, B);
}

public sealed class ChokeController
{
    private readonly ChokeDefinition[] _definitions;
    private readonly ChokeTrafficCoordinator _trafficCoordinator;

    public ChokeController(params ChokeDefinition[] definitions)
    {
        _definitions = definitions;
        _trafficCoordinator = new ChokeTrafficCoordinator(definitions.Length);
        for (var i = 0; i < definitions.Length; i++)
        {
            if (definitions[i].Id != i || definitions[i].Length <= 0f ||
                definitions[i].Width <= 0f)
            {
                throw new ArgumentException("Choke IDs must be dense and geometry must be valid.");
            }
        }
    }

    public ReadOnlySpan<ChokeDefinition> Definitions => _definitions;
    public ReadOnlySpan<ChokeTrafficSnapshot> TrafficSnapshots =>
        _trafficCoordinator.Snapshots;

    public void AssignForMove(
        UnitStore units,
        ReadOnlySpan<int> unitIndices,
        Vector2 groupStart,
        Vector2 groupGoal,
        ReadOnlySpan<int> routeChokeIds)
    {
        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            units.ActiveChokeIds[unit] = -1;
            units.ChokeDirections[unit] = 0;
            units.ChokeLaneOffsets[unit] = 0f;
            units.ChokePhases[unit] = ChokePhase.None;
            units.ChokeAdmitted[unit] = false;
            units.ChokeQueueRanks[unit] = -1;
            units.ChokeWaitTicks[unit] = 0;
        }

        var chokeId = routeChokeIds.IsEmpty ? -1 : routeChokeIds[0];
        if (chokeId < 0)
        {
            for (var index = 0; index < _definitions.Length; index++)
            {
                var candidate = _definitions[index];
                var candidateAxis = candidate.Axis;
                var candidateStartAlong = Vector2.Dot(groupStart - candidate.A, candidateAxis);
                var candidateGoalAlong = Vector2.Dot(groupGoal - candidate.A, candidateAxis);
                if ((candidateStartAlong < 0f && candidateGoalAlong > candidate.Length) ||
                    (candidateStartAlong > candidate.Length && candidateGoalAlong < 0f))
                {
                    chokeId = index;
                    break;
                }
            }
        }

        if ((uint)chokeId >= (uint)_definitions.Length)
        {
            return;
        }

        var choke = _definitions[chokeId];
        var axis = choke.Axis;
        var startAlong = Vector2.Dot(groupStart - choke.A, axis);
        var goalAlong = Vector2.Dot(groupGoal - choke.A, axis);
        var direction = startAlong < 0f && goalAlong > choke.Length
            ? (sbyte)1
            : startAlong > choke.Length && goalAlong < 0f
                ? (sbyte)-1
                : (sbyte)0;
        if (direction == 0)
        {
            return;
        }

        var ordered = unitIndices.ToArray();
        var normal = choke.Normal;
        Array.Sort(ordered, (left, right) =>
        {
            var leftValue = Vector2.Dot(units.Positions[left] - choke.A, normal);
            var rightValue = Vector2.Dot(units.Positions[right] - choke.A, normal);
            var compare = leftValue.CompareTo(rightValue);
            return compare != 0 ? compare : left.CompareTo(right);
        });

        var largestRadius = 0f;
        for (var i = 0; i < ordered.Length; i++)
        {
            largestRadius = MathF.Max(largestRadius, units.Radii[ordered[i]]);
        }

        var laneSpacing = largestRadius * 2f + 3f;
        var laneCount = Math.Max(1, (int)MathF.Floor(
            (choke.Width - largestRadius * 2f) / laneSpacing) + 1);
        for (var rank = 0; rank < ordered.Length; rank++)
        {
            var lane = Math.Min(laneCount - 1, rank * laneCount / ordered.Length);
            var offset = laneCount == 1
                ? 0f
                : -((laneCount - 1) * laneSpacing) * 0.5f + lane * laneSpacing;
            var unit = ordered[rank];
            units.ActiveChokeIds[unit] = chokeId;
            units.ChokeDirections[unit] = direction;
            units.ChokeLaneOffsets[unit] = offset;
        }
    }

    public void ApplyPreferredVelocities(UnitStore units)
    {
        for (var unit = 0; unit < units.Count; unit++)
        {
            UpdatePhase(units, unit);
        }

        _trafficCoordinator.Update(units, _definitions);

        for (var unit = 0; unit < units.Count; unit++)
        {
            var chokeId = units.ActiveChokeIds[unit];
            var direction = units.ChokeDirections[unit];
            var phase = units.ChokePhases[unit];
            if ((uint)chokeId >= (uint)_definitions.Length || direction == 0 ||
                units.Modes[unit] != UnitMoveMode.Moving || phase == ChokePhase.None)
            {
                continue;
            }

            var choke = _definitions[chokeId];
            var travelAxis = choke.Axis * direction;
            var entry = direction > 0 ? choke.A : choke.B;
            var exit = direction > 0 ? choke.B : choke.A;
            var lanePoint = entry + choke.Normal * units.ChokeLaneOffsets[unit];
            Vector2 desiredPoint;
            if (phase == ChokePhase.Approaching && !units.ChokeAdmitted[unit])
            {
                var laneSpacing = units.Radii[unit] * 2f + 4f;
                var laneCount = Math.Max(1, (int)MathF.Floor(
                    (choke.Width - units.Radii[unit] * 2f) / laneSpacing) + 1);
                var queueRow = Math.Max(0, units.ChokeQueueRanks[unit]) / laneCount;
                var queueDistance = 18f + queueRow * (units.Radii[unit] * 2f + 7f);
                desiredPoint = lanePoint - travelAxis * queueDistance;
            }
            else switch (phase)
            {
                case ChokePhase.Approaching:
                    desiredPoint = lanePoint + travelAxis * 18f;
                    break;
                case ChokePhase.Traversing:
                {
                    var laneError = Vector2.Dot(lanePoint - units.Positions[unit], choke.Normal);
                    desiredPoint = units.Positions[unit] + travelAxis * 55f +
                                   choke.Normal * laneError;
                    break;
                }
                default:
                    desiredPoint = exit + travelAxis * 95f +
                                   choke.Normal * units.ChokeLaneOffsets[unit];
                    break;
            }

            var delta = desiredPoint - units.Positions[unit];
            if (delta.LengthSquared() > 0.0001f)
            {
                units.PreferredVelocities[unit] = Vector2.Normalize(delta) *
                                                  units.MaxSpeeds[unit];
            }
            else
            {
                units.PreferredVelocities[unit] = Vector2.Zero;
            }
        }
    }

    private void UpdatePhase(UnitStore units, int unit)
    {
        var chokeId = units.ActiveChokeIds[unit];
        var direction = units.ChokeDirections[unit];
        if ((uint)chokeId >= (uint)_definitions.Length || direction == 0 ||
            units.Modes[unit] != UnitMoveMode.Moving)
        {
            units.ChokePhases[unit] = ChokePhase.None;
            return;
        }

        var choke = _definitions[chokeId];
        var travelAxis = choke.Axis * direction;
        var entry = direction > 0 ? choke.A : choke.B;
        var along = Vector2.Dot(units.Positions[unit] - entry, travelAxis);
        if (along < -choke.ApproachDistance)
        {
            units.ChokePhases[unit] = ChokePhase.None;
        }
        else if (along <= 0f)
        {
            units.ChokePhases[unit] = ChokePhase.Approaching;
        }
        else if (along <= choke.Length)
        {
            units.ChokePhases[unit] = ChokePhase.Traversing;
        }
        else if (along <= choke.Length + 70f)
        {
            units.ChokePhases[unit] = ChokePhase.Exiting;
        }
        else
        {
            units.ChokePhases[unit] = ChokePhase.None;
            units.ActiveChokeIds[unit] = -1;
            units.ChokeDirections[unit] = 0;
            units.ChokeAdmitted[unit] = false;
            units.ChokeQueueRanks[unit] = -1;
            units.ChokeWaitTicks[unit] = 0;
        }
    }

    public void ConstrainSolvedVelocities(UnitStore units)
    {
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (units.ChokePhases[unit] == ChokePhase.Approaching &&
                !units.ChokeAdmitted[unit])
            {
                var waitingChoke = _definitions[units.ActiveChokeIds[unit]];
                var waitingDirection = units.ChokeDirections[unit];
                var waitingTravelAxis = waitingChoke.Axis * waitingDirection;
                var waitingEntry = waitingDirection > 0 ? waitingChoke.A : waitingChoke.B;
                var along = Vector2.Dot(
                    units.Positions[unit] - waitingEntry,
                    waitingTravelAxis);
                var stoppingDistance = MathF.Max(0f, -along - 2f);
                var maximumForwardSpeed = MathF.Sqrt(
                    2f * units.Accelerations[unit] * stoppingDistance);
                var solved = units.NextVelocities[unit];
                var waitingForwardSpeed = MathF.Min(
                    Vector2.Dot(solved, waitingTravelAxis),
                    maximumForwardSpeed);
                var lateral = solved - waitingTravelAxis *
                    Vector2.Dot(solved, waitingTravelAxis);
                units.NextVelocities[unit] = waitingTravelAxis * waitingForwardSpeed + lateral;
                continue;
            }

            if (units.ChokePhases[unit] != ChokePhase.Traversing)
            {
                continue;
            }

            var choke = _definitions[units.ActiveChokeIds[unit]];
            var direction = units.ChokeDirections[unit];
            var travelAxis = choke.Axis * direction;
            var entry = direction > 0 ? choke.A : choke.B;
            var lanePoint = entry + choke.Normal * units.ChokeLaneOffsets[unit];
            var laneError = Vector2.Dot(lanePoint - units.Positions[unit], choke.Normal);
            var forwardSpeed = MathF.Max(0f, Vector2.Dot(units.NextVelocities[unit], travelAxis));
            var lateralSpeed = Math.Clamp(laneError * 4f, -28f, 28f);
            units.NextVelocities[unit] = travelAxis * forwardSpeed +
                                         choke.Normal * lateralSpeed;
        }
    }

    public void ConstrainPositions(UnitStore units)
    {
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (units.ChokeAdmitted[unit] ||
                units.ChokePhases[unit] != ChokePhase.Approaching)
            {
                continue;
            }

            var chokeId = units.ActiveChokeIds[unit];
            var direction = units.ChokeDirections[unit];
            if ((uint)chokeId >= (uint)_definitions.Length || direction == 0)
            {
                continue;
            }

            var choke = _definitions[chokeId];
            var travelAxis = choke.Axis * direction;
            var entry = direction > 0 ? choke.A : choke.B;
            var along = Vector2.Dot(units.Positions[unit] - entry, travelAxis);
            if (along > -1.5f)
            {
                units.Positions[unit] -= travelAxis * (along + 1.5f);
            }

            var forwardSpeed = Vector2.Dot(units.Velocities[unit], travelAxis);
            if (forwardSpeed > 0f)
            {
                units.Velocities[unit] -= travelAxis * forwardSpeed;
            }
        }
    }
}
