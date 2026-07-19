using System.Numerics;

namespace RtsDemo.Simulation;

/// <summary>
/// Reserves stable target-relative attack positions. Reservations are expressed as
/// angle/radius pairs so moving targets do not force a global slot reallocation.
/// </summary>
public sealed class CombatEngagementSlotAllocator
{
    private const int AngularCandidates = 32;
    private const float SlotPadding = 2f;

    private readonly UnitStore _units;
    private readonly CombatStore _combat;
    private readonly StaticWorld _world;
    private List<int>?[] _unitsByTarget;
    private int[] _activeTargets;
    private bool[] _targetActive;
    private int _activeTargetCount;

    public CombatEngagementSlotAllocator(
        UnitStore units,
        CombatStore combat,
        StaticWorld world)
    {
        _units = units;
        _combat = combat;
        _world = world;
        _unitsByTarget = new List<int>?[units.Capacity];
        _activeTargets = new int[units.Capacity];
        _targetActive = new bool[units.Capacity];
    }

    public void RebuildIndex()
    {
        if (_unitsByTarget.Length < _units.Capacity)
        {
            Array.Resize(ref _unitsByTarget, _units.Capacity);
            Array.Resize(ref _activeTargets, _units.Capacity);
            Array.Resize(ref _targetActive, _units.Capacity);
        }
        for (var index = 0; index < _activeTargetCount; index++)
        {
            var target = _activeTargets[index];
            _unitsByTarget[target]!.Clear();
            _targetActive[target] = false;
        }
        _activeTargetCount = 0;
        for (var unit = 0; unit < _units.Count; unit++)
        {
            if (!_units.Alive[unit] || !_combat.HasAttackSlots[unit]) continue;
            var target = _combat.TargetUnits[unit];
            if ((uint)target >= (uint)_units.Count) continue;
            AddToTargetIndex(unit, target);
        }
    }

    public void Assign(int unit, int target)
    {
        var origin = _units.Positions[target];
        var approach = _units.Positions[unit] - origin;
        var preferredAngle = approach.LengthSquared() > 0.0001f
            ? MathF.Atan2(approach.Y, approach.X)
            : DeterministicAngle(unit, target);
        var radius = DesiredRadius(unit, target);
        var preferredIndex = NormalizeIndex((int)MathF.Round(
            preferredAngle / MathF.Tau * AngularCandidates));

        for (var offset = 0; offset < AngularCandidates; offset++)
        {
            var signedOffset = offset == 0
                ? 0
                : (offset + 1) / 2 * ((offset & 1) == 1 ? 1 : -1);
            var index = NormalizeIndex(preferredIndex + signedOffset);
            var angle = index * MathF.Tau / AngularCandidates;
            var candidate = origin + Direction(angle) * radius;
            if (_world.IsDiscFree(candidate, _units.NavigationRadii[unit]) &&
                !Conflicts(unit, target, candidate))
            {
                Store(unit, angle, radius, candidate);
                return;
            }
        }

        _combat.HasAttackSlots[unit] = false;
    }

    public Vector2 Refresh(int unit, int target)
    {
        if (!_combat.HasAttackSlots[unit])
        {
            Assign(unit, target);
        }

        if (!_combat.HasAttackSlots[unit])
        {
            return _units.Positions[target];
        }

        var targetPosition = _units.Positions[target];
        var slot = targetPosition +
                   Direction(_combat.AttackSlotAngles[unit]) *
                   _combat.AttackSlotRadii[unit];
        _combat.AttackSlotTargets[unit] = slot;
        return slot;
    }

    public Vector2 ResolveChaseTarget(int unit, int target)
    {
        var slot = Refresh(unit, target);
        if (!_combat.HasAttackSlots[unit] || !TargetBlocksSegment(unit, target, slot))
        {
            return slot;
        }

        var center = _units.Positions[target];
        var relative = _units.Positions[unit] - center;
        var currentRadius = relative.Length();
        var currentAngle = currentRadius > 0.001f
            ? MathF.Atan2(relative.Y, relative.X)
            : _combat.AttackSlotAngles[unit];
        var orbitRadius = _combat.AttackSlotRadii[unit] +
                          MathF.Max(5f, _units.Radii[unit] * 0.6f);
        if (currentRadius > orbitRadius + 8f)
        {
            var approach = center + Direction(currentAngle) * orbitRadius;
            return _world.IsDiscFree(approach, _units.NavigationRadii[unit])
                ? approach
                : slot;
        }

        var angleDelta = NormalizeAngle(_combat.AttackSlotAngles[unit] - currentAngle);
        var step = Math.Clamp(angleDelta, -MathF.PI / 5f, MathF.PI / 5f);
        var staging = center + Direction(currentAngle + step) * orbitRadius;
        return _world.IsDiscFree(staging, _units.NavigationRadii[unit])
            ? staging
            : slot;
    }

    public void Release(int unit)
    {
        if (_combat.HasAttackSlots[unit])
            RemoveFromTargetIndex(unit, _combat.TargetUnits[unit]);
        _combat.HasAttackSlots[unit] = false;
    }

    public bool IsReady(int unit)
    {
        if (!_combat.HasAttackSlots[unit])
        {
            return false;
        }

        var tolerance = _combat.PositioningKinds[unit] == CombatPositioningKind.Melee
            ? MathF.Max(9f, _units.Radii[unit])
            : MathF.Max(9f, _units.Radii[unit]);
        var toleranceSquared = tolerance * tolerance;
        if (Vector2.DistanceSquared(
                _units.Positions[unit], _combat.AttackSlotTargets[unit]) <=
            toleranceSquared)
        {
            return true;
        }

        return TryImproveAssignment(unit) &&
               Vector2.DistanceSquared(
                   _units.Positions[unit], _combat.AttackSlotTargets[unit]) <=
               toleranceSquared;
    }

    private float DesiredRadius(int unit, int target)
    {
        var contactRadius = _units.Radii[unit] + _units.Radii[target];
        return _combat.PositioningKinds[unit] == CombatPositioningKind.Melee
            ? contactRadius + 1f
            : contactRadius + MathF.Max(5f, _combat.AttackRanges[unit] * 0.72f);
    }

    private bool Conflicts(int unit, int target, Vector2 candidate)
    {
        if ((uint)target >= (uint)_unitsByTarget.Length ||
            _unitsByTarget[target] is not { } occupants)
            return false;
        for (var index = 0; index < occupants.Count; index++)
        {
            var other = occupants[index];
            if (other == unit || !_units.Alive[other] ||
                !_combat.HasAttackSlots[other] ||
                _combat.TargetUnits[other] != target)
            {
                continue;
            }

            var minimumDistance =
                _units.Radii[unit] + _units.Radii[other] + SlotPadding;
            var otherSlot = _units.Positions[target] +
                            Direction(_combat.AttackSlotAngles[other]) *
                            _combat.AttackSlotRadii[other];
            if (Vector2.DistanceSquared(candidate, otherSlot) <
                minimumDistance * minimumDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool TargetBlocksSegment(int unit, int target, Vector2 destination)
    {
        var start = _units.Positions[unit];
        var segment = destination - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared < 0.0001f)
        {
            return false;
        }

        var center = _units.Positions[target];
        var projection = Math.Clamp(
            Vector2.Dot(center - start, segment) / lengthSquared, 0f, 1f);
        var closest = start + segment * projection;
        var clearance = _units.Radii[unit] + _units.Radii[target] + SlotPadding;
        return projection > 0.02f && projection < 0.98f &&
               Vector2.DistanceSquared(closest, center) < clearance * clearance;
    }

    private bool TryImproveAssignment(int unit)
    {
        var target = _combat.TargetUnits[unit];
        var bestOther = -1;
        var bestImprovement = 4f * 4f;
        for (var other = 0; other < _units.Count; other++)
        {
            if (other == unit || !_units.Alive[other] ||
                _combat.TargetUnits[other] != target ||
                !_combat.HasAttackSlots[other] ||
                _combat.Phases[other] == CombatPhase.Attacking)
            {
                continue;
            }

            var currentCost = Vector2.DistanceSquared(
                                  _units.Positions[unit],
                                  _combat.AttackSlotTargets[unit]) +
                              Vector2.DistanceSquared(
                                  _units.Positions[other],
                                  _combat.AttackSlotTargets[other]);
            var swappedCost = Vector2.DistanceSquared(
                                  _units.Positions[unit],
                                  _combat.AttackSlotTargets[other]) +
                              Vector2.DistanceSquared(
                                  _units.Positions[other],
                                  _combat.AttackSlotTargets[unit]);
            var improvement = currentCost - swappedCost;
            if (improvement > bestImprovement ||
                (improvement == bestImprovement && other < bestOther))
            {
                bestOther = other;
                bestImprovement = improvement;
            }
        }

        if (bestOther < 0)
        {
            return false;
        }

        (_combat.AttackSlotAngles[unit], _combat.AttackSlotAngles[bestOther]) =
            (_combat.AttackSlotAngles[bestOther], _combat.AttackSlotAngles[unit]);
        (_combat.AttackSlotRadii[unit], _combat.AttackSlotRadii[bestOther]) =
            (_combat.AttackSlotRadii[bestOther], _combat.AttackSlotRadii[unit]);
        Refresh(unit, target);
        Refresh(bestOther, target);
        return true;
    }

    private void Store(int unit, float angle, float radius, Vector2 target)
    {
        _combat.AttackSlotAngles[unit] = angle;
        _combat.AttackSlotRadii[unit] = radius;
        _combat.AttackSlotTargets[unit] = target;
        _combat.HasAttackSlots[unit] = true;
        AddToTargetIndex(unit, _combat.TargetUnits[unit]);
    }

    private void AddToTargetIndex(int unit, int target)
    {
        if ((uint)target >= (uint)_unitsByTarget.Length) return;
        var occupants = _unitsByTarget[target];
        if (occupants is null)
        {
            occupants = [];
            _unitsByTarget[target] = occupants;
        }
        if (!_targetActive[target])
        {
            _activeTargets[_activeTargetCount++] = target;
            _targetActive[target] = true;
        }
        var insert = occupants.BinarySearch(unit);
        if (insert >= 0) return;
        occupants.Insert(~insert, unit);
    }

    private void RemoveFromTargetIndex(int unit, int target)
    {
        if ((uint)target >= (uint)_unitsByTarget.Length ||
            _unitsByTarget[target] is not { } occupants)
            return;
        var index = occupants.BinarySearch(unit);
        if (index >= 0) occupants.RemoveAt(index);
    }

    private static int NormalizeIndex(int index)
    {
        var normalized = index % AngularCandidates;
        return normalized < 0 ? normalized + AngularCandidates : normalized;
    }

    private static Vector2 Direction(float angle) =>
        new(MathF.Cos(angle), MathF.Sin(angle));

    private static float DeterministicAngle(int unit, int target)
    {
        var value = unchecked((uint)(unit * 73856093) ^ (uint)(target * 19349663));
        return value / (float)uint.MaxValue * MathF.Tau;
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }
        while (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }
        return angle;
    }
}
