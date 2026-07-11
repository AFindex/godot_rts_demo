using System.Numerics;

namespace RtsDemo.Simulation;

public interface ICombatMovementDriver
{
    void Chase(int unit, Vector2 target);
    void StopForAttack(int unit);
    void ResumeAttackMove(int unit, Vector2 target);
    void FinishEngagement(int unit, UnitCommandIntent intent);
    void Kill(int unit);
    bool IsBuildingTargetValid(int attacker, GameplayBuildingId building);
    SimRect BuildingTargetBounds(GameplayBuildingId building);
    bool DamageBuilding(GameplayBuildingId building, float damage);
}

/// <summary>
/// Deterministic fixed-tick AttackMove state machine. Target selection uses nearest
/// distance then stable unit ID; expensive acquisition is staggered over six ticks.
/// </summary>
public sealed class CombatSystem
{
    private const int AcquisitionTickStride = 6;
    private const float ChaseRepathSeconds = 0.2f;
    private const float ChaseRetargetDistance = 12f;

    private readonly UnitStore _units;
    private readonly CombatStore _combat;
    private readonly ICombatMovementDriver _movement;
    private readonly CombatEngagementSlotAllocator _slots;

    public CombatSystem(
        UnitStore units,
        CombatStore combat,
        ICombatMovementDriver movement,
        StaticWorld world)
    {
        _units = units;
        _combat = combat;
        _movement = movement;
        _slots = new CombatEngagementSlotAllocator(units, combat, world);
    }

    public void Update(float delta, long tick)
    {
        for (var unit = 0; unit < _units.Count; unit++)
        {
            if (!_units.Alive[unit])
            {
                continue;
            }

            _combat.CooldownRemaining[unit] = MathF.Max(
                0f, _combat.CooldownRemaining[unit] - delta);
            _combat.ChaseRepathRemaining[unit] = MathF.Max(
                0f, _combat.ChaseRepathRemaining[unit] - delta);

            var intent = _combat.CommandIntents[unit];
            if (intent is not (UnitCommandIntent.AttackMove or
                UnitCommandIntent.AttackTarget or UnitCommandIntent.Stop or
                UnitCommandIntent.Hold))
            {
                continue;
            }

            if (_combat.TargetKinds[unit] == CombatTargetKind.Building)
            {
                UpdateBuildingTarget(unit, intent, delta);
                continue;
            }

            var target = _combat.TargetUnits[unit];
            if (!IsValidTarget(unit, target))
            {
                if (target >= 0)
                {
                    Disengage(unit);
                }

                if (intent == UnitCommandIntent.AttackTarget)
                {
                    continue;
                }

                if ((tick + unit) % AcquisitionTickStride == 0)
                {
                    target = FindNearestTarget(unit);
                    if (target >= 0)
                    {
                        BeginEngagement(unit, target);
                    }
                }
                continue;
            }

            if (intent is not (UnitCommandIntent.Hold or UnitCommandIntent.AttackTarget) &&
                Vector2.DistanceSquared(
                    _units.Positions[target], _combat.EngagementOrigins[unit]) >
                _combat.LeashDistances[unit] * _combat.LeashDistances[unit])
            {
                Disengage(unit);
                continue;
            }

            if (_units.BlockedByNavigation[unit] &&
                _units.RecoveryStages[unit] == RecoveryStage.Unreachable)
            {
                Disengage(unit);
                continue;
            }

            var distance = Vector2.Distance(
                _units.Positions[unit], _units.Positions[target]);
            var range = _combat.AttackRanges[unit] +
                        _units.Radii[unit] + _units.Radii[target];
            if (distance <= range &&
                (intent == UnitCommandIntent.Hold || _slots.IsReady(unit)))
            {
                UpdateAttack(unit, target, delta);
            }
            else if (intent == UnitCommandIntent.Hold)
            {
                Disengage(unit);
            }
            else
            {
                UpdateChase(unit, target);
            }
        }
    }

    private void BeginEngagement(int unit, int target)
    {
        _combat.TargetUnits[unit] = target;
        _combat.TargetBuildings[unit] = -1;
        _combat.TargetKinds[unit] = CombatTargetKind.Unit;
        _combat.EngagementOrigins[unit] = _units.Positions[unit];
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.ChaseRepathRemaining[unit] = 0f;
        if (_combat.CommandIntents[unit] != UnitCommandIntent.Hold)
        {
            _slots.Assign(unit, target);
            UpdateChase(unit, target);
        }
    }

    private void UpdateChase(int unit, int target)
    {
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.WindupRemaining[unit] = 0f;
        var targetPosition = _slots.ResolveChaseTarget(unit, target);
        var targetStayedLocal = Vector2.DistanceSquared(
            targetPosition, _combat.LastChaseTargets[unit]) <=
            ChaseRetargetDistance * ChaseRetargetDistance;
        if (targetStayedLocal &&
            _units.Modes[unit] is UnitMoveMode.Moving or UnitMoveMode.WaitingForPath)
        {
            return;
        }

        if (_combat.ChaseRepathRemaining[unit] > 0f && targetStayedLocal)
        {
            return;
        }

        _combat.LastChaseTargets[unit] = targetPosition;
        _combat.ChaseRepathRemaining[unit] = ChaseRepathSeconds;
        _movement.Chase(unit, targetPosition);
    }

    private void UpdateAttack(int unit, int target, float delta)
    {
        _combat.Phases[unit] = CombatPhase.Attacking;
        _movement.StopForAttack(unit);

        if (_combat.WindupRemaining[unit] > 0f)
        {
            _combat.WindupRemaining[unit] -= delta;
            if (_combat.WindupRemaining[unit] <= 0f && IsValidTarget(unit, target))
            {
                ApplyHit(unit, target);
            }
            return;
        }

        if (_combat.CooldownRemaining[unit] <= 0f)
        {
            var windup = _combat.AttackWindupDurations[unit];
            if (windup <= 0f)
            {
                ApplyHit(unit, target);
            }
            else
            {
                _combat.WindupRemaining[unit] = windup;
            }
        }
    }

    private void ApplyHit(int attacker, int target)
    {
        _combat.CooldownRemaining[attacker] =
            _combat.AttackCooldownDurations[attacker];
        _combat.Health[target] = MathF.Max(
            0f, _combat.Health[target] - _combat.AttackDamage[attacker]);
        if (_combat.Health[target] > 0f)
        {
            return;
        }

        _units.Alive[target] = false;
        _combat.CommandIntents[target] = UnitCommandIntent.None;
        _combat.Phases[target] = CombatPhase.None;
        _combat.TargetUnits[target] = -1;
        _combat.TargetBuildings[target] = -1;
        _combat.TargetKinds[target] = CombatTargetKind.None;
        _slots.Release(target);
        _movement.Kill(target);
    }

    private void Disengage(int unit)
    {
        var intent = _combat.CommandIntents[unit];
        _combat.TargetUnits[unit] = -1;
        _combat.TargetBuildings[unit] = -1;
        _combat.TargetKinds[unit] = CombatTargetKind.None;
        _combat.Phases[unit] = CombatPhase.Searching;
        _combat.WindupRemaining[unit] = 0f;
        _combat.ChaseRepathRemaining[unit] = 0f;
        _slots.Release(unit);
        if (intent == UnitCommandIntent.AttackMove)
        {
            _movement.ResumeAttackMove(unit, _combat.AttackMoveGoals[unit]);
        }
        else
        {
            _movement.FinishEngagement(unit, intent);
        }
    }

    private int FindNearestTarget(int unit)
    {
        var best = -1;
        var bestDistanceSquared = float.PositiveInfinity;
        var intent = _combat.CommandIntents[unit];
        for (var candidate = 0; candidate < _units.Count; candidate++)
        {
            if (!IsValidTarget(unit, candidate))
            {
                continue;
            }

            var distanceSquared = Vector2.DistanceSquared(
                _units.Positions[unit], _units.Positions[candidate]);
            var acquisitionRange = intent == UnitCommandIntent.Hold
                ? _combat.AttackRanges[unit] +
                  _units.Radii[unit] + _units.Radii[candidate]
                : _combat.AcquisitionRanges[unit];
            if (distanceSquared > acquisitionRange * acquisitionRange)
            {
                continue;
            }
            if (distanceSquared < bestDistanceSquared ||
                (distanceSquared == bestDistanceSquared && candidate < best))
            {
                best = candidate;
                bestDistanceSquared = distanceSquared;
            }
        }

        return best;
    }

    private bool IsValidTarget(int unit, int target) =>
        (uint)target < (uint)_units.Count &&
        target != unit &&
        _units.Alive[target] &&
        _combat.Teams[target] != _combat.Teams[unit];

    private void UpdateBuildingTarget(
        int unit,
        UnitCommandIntent intent,
        float delta)
    {
        var building = new GameplayBuildingId(_combat.TargetBuildings[unit]);
        if (!_movement.IsBuildingTargetValid(unit, building))
        {
            Disengage(unit);
            return;
        }
        if (_units.BlockedByNavigation[unit] &&
            _units.RecoveryStages[unit] == RecoveryStage.Unreachable)
        {
            Disengage(unit);
            return;
        }
        var bounds = _movement.BuildingTargetBounds(building);
        var nearest = bounds.Clamp(_units.Positions[unit]);
        var distance = Vector2.Distance(_units.Positions[unit], nearest);
        var range = _combat.AttackRanges[unit] + _units.Radii[unit];
        if (distance <= range)
        {
            UpdateBuildingAttack(unit, building, delta);
            return;
        }
        if (intent == UnitCommandIntent.Hold)
        {
            Disengage(unit);
            return;
        }
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.WindupRemaining[unit] = 0f;
        var chaseTarget = ClosestOutsidePoint(
            bounds, _units.Positions[unit], _units.Radii[unit] + 2f);
        var stayedLocal = Vector2.DistanceSquared(
            chaseTarget, _combat.LastChaseTargets[unit]) <=
            ChaseRetargetDistance * ChaseRetargetDistance;
        if (stayedLocal && _units.Modes[unit] is
                UnitMoveMode.Moving or UnitMoveMode.WaitingForPath)
        {
            return;
        }
        if (_combat.ChaseRepathRemaining[unit] > 0f && stayedLocal)
        {
            return;
        }
        _combat.LastChaseTargets[unit] = chaseTarget;
        _combat.ChaseRepathRemaining[unit] = ChaseRepathSeconds;
        _movement.Chase(unit, chaseTarget);
    }

    private void UpdateBuildingAttack(
        int unit,
        GameplayBuildingId building,
        float delta)
    {
        _combat.Phases[unit] = CombatPhase.Attacking;
        _movement.StopForAttack(unit);
        if (_combat.WindupRemaining[unit] > 0f)
        {
            _combat.WindupRemaining[unit] -= delta;
            if (_combat.WindupRemaining[unit] <= 0f &&
                _movement.IsBuildingTargetValid(unit, building))
            {
                ApplyBuildingHit(unit, building);
            }
            return;
        }
        if (_combat.CooldownRemaining[unit] > 0f)
        {
            return;
        }
        var windup = _combat.AttackWindupDurations[unit];
        if (windup <= 0f)
        {
            ApplyBuildingHit(unit, building);
        }
        else
        {
            _combat.WindupRemaining[unit] = windup;
        }
    }

    private void ApplyBuildingHit(int attacker, GameplayBuildingId building)
    {
        _combat.CooldownRemaining[attacker] =
            _combat.AttackCooldownDurations[attacker];
        if (!_movement.DamageBuilding(
                building, _combat.AttackDamage[attacker]) ||
            !_movement.IsBuildingTargetValid(attacker, building))
        {
            Disengage(attacker);
        }
    }

    private static Vector2 ClosestOutsidePoint(
        SimRect bounds,
        Vector2 origin,
        float offset)
    {
        Vector2[] candidates =
        [
            new(bounds.Min.X - offset, Math.Clamp(origin.Y, bounds.Min.Y, bounds.Max.Y)),
            new(bounds.Max.X + offset, Math.Clamp(origin.Y, bounds.Min.Y, bounds.Max.Y)),
            new(Math.Clamp(origin.X, bounds.Min.X, bounds.Max.X), bounds.Min.Y - offset),
            new(Math.Clamp(origin.X, bounds.Min.X, bounds.Max.X), bounds.Max.Y + offset)
        ];
        return candidates.OrderBy(value =>
            Vector2.DistanceSquared(origin, value)).First();
    }
}
