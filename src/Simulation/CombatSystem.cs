using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct CombatBuildingDamageResult(
    bool Applied,
    bool Destroyed,
    float AppliedDamage,
    float RemainingHealth,
    float DamagePerAttack,
    int AttacksApplied,
    bool BonusApplied);

public readonly record struct CombatAutoTargetScore(
    int TargetUnit,
    float DistanceSquared,
    int Priority,
    bool WeaponBonusMatch,
    bool ArmedThreat,
    float TotalScore);

public interface ICombatMovementDriver
{
    void Chase(int unit, Vector2 target);
    void StopForAttack(int unit);
    void ResumeAttackMove(int unit, Vector2 target);
    void FinishEngagement(int unit, UnitCommandIntent intent);
    void Kill(int unit);
    bool IsBuildingTargetValid(int attacker, GameplayBuildingId building);
    bool IsBuildingAlive(GameplayBuildingId building);
    SimRect BuildingTargetBounds(GameplayBuildingId building);
    CombatBuildingDamageResult DamageBuilding(
        GameplayBuildingId building, CombatWeaponDamageSnapshot weapon);
    int WeaponUpgradeLevel(int team);
}

/// <summary>
/// Deterministic fixed-tick AttackMove state machine. Target selection uses nearest
/// distance then stable unit ID; expensive acquisition is staggered over six ticks.
/// </summary>
public sealed class CombatSystem
{
    private const int AcquisitionTickStride = 6;
    private const int RetargetTickStride = 12;
    private const float ChaseRepathSeconds = 0.2f;
    private const float ChaseRetargetDistance = 12f;
    private const float MinimumTargetLockSeconds = 0.75f;
    private const float PriorityScoreWeight = 12_000f;
    private const float WeaponBonusScoreWeight = 6_000f;
    private const float ArmedThreatScoreWeight = 3_000f;
    private const float RetargetImprovementMargin = 2_500f;

    private readonly UnitStore _units;
    private readonly CombatStore _combat;
    private readonly ICombatMovementDriver _movement;
    private readonly CombatEngagementSlotAllocator _slots;
    private readonly CombatEventStream _events;
    private readonly Func<int, int, bool> _canPerceiveTarget;
    public CombatProjectileSystem Projectiles { get; } = new();

    public CombatSystem(
        UnitStore units,
        CombatStore combat,
        ICombatMovementDriver movement,
        StaticWorld world,
        CombatEventStream events,
        Func<int, int, bool> canPerceiveTarget)
    {
        _units = units;
        _combat = combat;
        _movement = movement;
        _slots = new CombatEngagementSlotAllocator(units, combat, world);
        _events = events;
        _canPerceiveTarget = canPerceiveTarget;
    }

    public void Update(float delta, long tick)
    {
        Projectiles.Update(delta, ResolveProjectileTarget,
            value => ApplyProjectileImpact(value, tick),
            value => _events.Publish(
                tick, CombatEventKind.ProjectileExpired,
                value.AttackerUnit, value.TargetKind, value.TargetId,
                projectileId: value.Id,
                worldPosition: value.Position));
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
            _combat.TargetLockRemaining[unit] = MathF.Max(
                0f, _combat.TargetLockRemaining[unit] - delta);

            var intent = _combat.CommandIntents[unit];
            if (intent is not (UnitCommandIntent.AttackMove or
                UnitCommandIntent.AttackTarget or UnitCommandIntent.Stop or
                UnitCommandIntent.Hold))
            {
                continue;
            }

            if (_combat.TargetKinds[unit] == CombatTargetKind.Building)
            {
                UpdateBuildingTarget(unit, intent, delta, tick);
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
                    target = FindBestTarget(unit);
                    if (target >= 0)
                    {
                        BeginEngagement(unit, target);
                    }
                }
                continue;
            }

            if (intent != UnitCommandIntent.AttackTarget &&
                _combat.WindupRemaining[unit] <= 0f &&
                _combat.TargetLockRemaining[unit] <= 0f &&
                (tick + unit) % RetargetTickStride == 0)
            {
                var candidate = FindBestTarget(unit);
                var candidateScore = candidate >= 0 && candidate != target
                    ? TargetScore(unit, candidate)
                    : default;
                var currentScore = candidate >= 0 && candidate != target
                    ? TargetScore(unit, target)
                    : default;
                if (candidate >= 0 && candidate != target &&
                    HasSemanticTargetAdvantage(candidateScore, currentScore) &&
                    candidateScore.TotalScore + RetargetImprovementMargin <
                    currentScore.TotalScore)
                {
                    BeginEngagement(unit, candidate);
                    target = candidate;
                }
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
                (intent == UnitCommandIntent.Hold || _slots.IsReady(unit) ||
                 CanContinueMobileAttackMove(unit, intent)))
            {
                UpdateAttack(unit, target, delta, tick);
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
        _slots.Release(unit);
        _combat.TargetUnits[unit] = target;
        _combat.TargetBuildings[unit] = -1;
        _combat.TargetKinds[unit] = CombatTargetKind.Unit;
        _combat.EngagementOrigins[unit] = _units.Positions[unit];
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.ChaseRepathRemaining[unit] = 0f;
        _combat.WindupRemaining[unit] = 0f;
        _combat.TargetLockRemaining[unit] = MinimumTargetLockSeconds;
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

    private void UpdateAttack(int unit, int target, float delta, long tick)
    {
        _combat.Phases[unit] = CombatPhase.Attacking;
        var intent = _combat.CommandIntents[unit];

        if (_combat.WindupRemaining[unit] > 0f)
        {
            ApplyAttackMovement(
                unit, intent, _combat.CanMoveDuringWindup[unit]);
            _combat.WindupRemaining[unit] -= delta;
            if (_combat.WindupRemaining[unit] <= 0f && IsValidTarget(unit, target))
            {
                FireAtUnit(unit, target, tick);
            }
            return;
        }

        if (_combat.CooldownRemaining[unit] > 0f)
        {
            ApplyAttackMovement(
                unit, intent, _combat.CanMoveDuringCooldown[unit]);
            return;
        }

        var windup = _combat.AttackWindupDurations[unit];
        ApplyAttackMovement(
            unit, intent, _combat.CanMoveDuringWindup[unit]);
        if (windup <= 0f)
        {
            PublishAttackStarted(unit, CombatTargetKind.Unit, target, tick);
            FireAtUnit(unit, target, tick);
        }
        else
        {
            _combat.WindupRemaining[unit] = windup;
            PublishAttackStarted(unit, CombatTargetKind.Unit, target, tick);
        }
    }

    private void FireAtUnit(int attacker, int target, long tick)
    {
        _combat.CooldownRemaining[attacker] =
            _combat.AttackCooldownDurations[attacker];
        var weapon = Weapon(attacker);
        if (_combat.ProjectileSpeed[attacker] > 0f)
        {
            if (Projectiles.Launch(
                attacker, CombatTargetKind.Unit, target,
                _units.Positions[attacker], _combat.ProjectileSpeed[attacker],
                weapon, out var projectileId))
            {
                _events.Publish(tick, CombatEventKind.ProjectileLaunched,
                    attacker, CombatTargetKind.Unit, target,
                    projectileId: projectileId,
                    worldPosition: _units.Positions[attacker]);
            }
            else
            {
                _events.Publish(tick, CombatEventKind.ProjectileExpired,
                    attacker, CombatTargetKind.Unit, target,
                    projectileId: 0,
                    worldPosition: _units.Positions[attacker]);
            }
            return;
        }
        ApplyUnitWeapon(attacker, target, weapon, tick, -1);
    }

    private void ApplyUnitWeapon(
        int attacker,
        int target,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId,
        Vector2? impactPosition = null)
    {
        if (!IsValidTarget(attacker, target)) return;
        var result = CombatDamageResolver.Resolve(
            weapon,
            new CombatDefenseSnapshot(
                _combat.Armor[target], _combat.Attributes[target]),
            _combat.Health[target]);
        var damage = result.TotalDamage;
        _combat.Health[target] = result.RemainingHealth;
        var worldPosition = impactPosition ?? _units.Positions[target];
        _events.Publish(tick, CombatEventKind.Impact, attacker,
            CombatTargetKind.Unit, target, damage, _combat.Health[target],
            result.DamagePerAttack, result.AttacksApplied, result.BonusApplied,
            projectileId, worldPosition);
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
        _events.Publish(tick, CombatEventKind.TargetDestroyed, attacker,
            CombatTargetKind.Unit, target, damage, 0f,
            worldPosition: worldPosition);
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
        _combat.TargetLockRemaining[unit] = 0f;
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

    private int FindBestTarget(int unit)
    {
        var best = -1;
        var bestScore = float.PositiveInfinity;
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
            var score = TargetScore(unit, candidate).TotalScore;
            if (score < bestScore ||
                (score == bestScore && candidate < best))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    internal CombatAutoTargetScore PreviewAutoTargetScore(
        int attacker,
        int target)
    {
        if ((uint)attacker >= (uint)_units.Count ||
            (uint)target >= (uint)_units.Count)
            throw new ArgumentOutOfRangeException();
        return TargetScore(attacker, target);
    }

    private CombatAutoTargetScore TargetScore(int attacker, int target)
    {
        var distanceSquared = Vector2.DistanceSquared(
            _units.Positions[attacker], _units.Positions[target]);
        var priority = _combat.AutoTargetPriority[target];
        var bonusMatch = _combat.BonusVs[attacker] != CombatAttribute.None &&
                         (_combat.BonusVs[attacker] &
                          _combat.Attributes[target]) != 0;
        var armedThreat = _combat.AttackDamage[target] > 0f;
        var total = distanceSquared - priority * PriorityScoreWeight -
                    (bonusMatch ? WeaponBonusScoreWeight : 0f) -
                    (armedThreat ? ArmedThreatScoreWeight : 0f);
        return new CombatAutoTargetScore(
            target, distanceSquared, priority, bonusMatch, armedThreat, total);
    }

    private static bool HasSemanticTargetAdvantage(
        CombatAutoTargetScore candidate,
        CombatAutoTargetScore current) =>
        candidate.Priority > current.Priority ||
        candidate.WeaponBonusMatch && !current.WeaponBonusMatch ||
        candidate.ArmedThreat && !current.ArmedThreat;

    private bool IsValidTarget(int unit, int target) =>
        (uint)target < (uint)_units.Count &&
        target != unit &&
        _units.Alive[target] &&
        _combat.Teams[target] != _combat.Teams[unit] &&
        _canPerceiveTarget(_combat.Teams[unit], target);

    private void UpdateBuildingTarget(
        int unit,
        UnitCommandIntent intent,
        float delta,
        long tick)
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
            UpdateBuildingAttack(unit, building, intent, delta, tick);
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
        UnitCommandIntent intent,
        float delta,
        long tick)
    {
        _combat.Phases[unit] = CombatPhase.Attacking;
        if (_combat.WindupRemaining[unit] > 0f)
        {
            ApplyAttackMovement(
                unit, intent, _combat.CanMoveDuringWindup[unit]);
            _combat.WindupRemaining[unit] -= delta;
            if (_combat.WindupRemaining[unit] <= 0f &&
                _movement.IsBuildingTargetValid(unit, building))
            {
                FireAtBuilding(unit, building, tick);
            }
            return;
        }
        if (_combat.CooldownRemaining[unit] > 0f)
        {
            ApplyAttackMovement(
                unit, intent, _combat.CanMoveDuringCooldown[unit]);
            return;
        }
        var windup = _combat.AttackWindupDurations[unit];
        ApplyAttackMovement(
            unit, intent, _combat.CanMoveDuringWindup[unit]);
        if (windup <= 0f)
        {
            PublishAttackStarted(unit, CombatTargetKind.Building, building.Value, tick);
            FireAtBuilding(unit, building, tick);
        }
        else
        {
            _combat.WindupRemaining[unit] = windup;
            PublishAttackStarted(unit, CombatTargetKind.Building, building.Value, tick);
        }
    }

    private void FireAtBuilding(
        int attacker,
        GameplayBuildingId building,
        long tick)
    {
        _combat.CooldownRemaining[attacker] =
            _combat.AttackCooldownDurations[attacker];
        var weapon = Weapon(attacker);
        if (_combat.ProjectileSpeed[attacker] > 0f)
        {
            if (Projectiles.Launch(
                attacker, CombatTargetKind.Building, building.Value,
                _units.Positions[attacker], _combat.ProjectileSpeed[attacker],
                weapon, out var projectileId))
            {
                _events.Publish(tick, CombatEventKind.ProjectileLaunched,
                    attacker, CombatTargetKind.Building, building.Value,
                    projectileId: projectileId,
                    worldPosition: _units.Positions[attacker]);
            }
            else
            {
                _events.Publish(tick, CombatEventKind.ProjectileExpired,
                    attacker, CombatTargetKind.Building, building.Value,
                    projectileId: 0,
                    worldPosition: _units.Positions[attacker]);
            }
            return;
        }
        ApplyBuildingWeapon(attacker, building, weapon, tick, -1);
    }

    private void ApplyBuildingWeapon(
        int attacker,
        GameplayBuildingId building,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId,
        Vector2? impactPosition = null)
    {
        var worldPosition = impactPosition ?? Center(
            _movement.BuildingTargetBounds(building));
        var result = _movement.DamageBuilding(building, weapon);
        if (!result.Applied)
        {
            Disengage(attacker);
            return;
        }
        _events.Publish(tick, CombatEventKind.Impact, attacker,
            CombatTargetKind.Building, building.Value,
            result.AppliedDamage, result.RemainingHealth,
            result.DamagePerAttack, result.AttacksApplied, result.BonusApplied,
            projectileId, worldPosition);
        if (result.Destroyed)
        {
            _events.Publish(tick, CombatEventKind.TargetDestroyed, attacker,
                CombatTargetKind.Building, building.Value,
                result.AppliedDamage, 0f,
                worldPosition: worldPosition);
            Disengage(attacker);
        }
    }

    private void PublishAttackStarted(
        int attacker,
        CombatTargetKind targetKind,
        int targetId,
        long tick) =>
        _events.Publish(tick, CombatEventKind.AttackStarted, attacker,
            targetKind, targetId,
            worldPosition: _units.Positions[attacker]);

    private bool CanContinueMobileAttackMove(
        int unit,
        UnitCommandIntent intent) =>
        intent == UnitCommandIntent.AttackMove &&
        _combat.Phases[unit] == CombatPhase.Attacking &&
        (_combat.WindupRemaining[unit] > 0f
            ? _combat.CanMoveDuringWindup[unit]
            : _combat.CooldownRemaining[unit] > 0f &&
              _combat.CanMoveDuringCooldown[unit]);

    private void ApplyAttackMovement(
        int unit,
        UnitCommandIntent intent,
        bool allowed)
    {
        if (allowed && intent == UnitCommandIntent.AttackMove)
            _movement.ResumeAttackMove(unit, _combat.AttackMoveGoals[unit]);
        else
            _movement.StopForAttack(unit);
    }

    private CombatWeaponDamageSnapshot Weapon(int attacker) => new(
        _combat.AttackDamage[attacker],
        _combat.AttacksPerVolley[attacker],
        _combat.BonusVs[attacker],
        _combat.BonusDamage[attacker],
        _movement.WeaponUpgradeLevel(_combat.Teams[attacker]),
        _combat.BaseUpgradeDamage[attacker],
        _combat.BonusUpgradeDamage[attacker]);

    internal CombatDamageResult PreviewDamage(int attacker, int target)
    {
        if ((uint)attacker >= (uint)_units.Count ||
            (uint)target >= (uint)_units.Count ||
            !_units.Alive[attacker] || !_units.Alive[target])
            throw new ArgumentOutOfRangeException();
        return CombatDamageResolver.Resolve(
            Weapon(attacker),
            new CombatDefenseSnapshot(
                _combat.Armor[target], _combat.Attributes[target]),
            _combat.Health[target]);
    }

    private (bool Valid, Vector2 Position) ResolveProjectileTarget(
        CombatTargetKind kind,
        int targetId) => kind switch
    {
        CombatTargetKind.Unit when (uint)targetId < (uint)_units.Count &&
                                   _units.Alive[targetId] =>
            (true, _units.Positions[targetId]),
        CombatTargetKind.Building when _movement.IsBuildingAlive(
            new GameplayBuildingId(targetId)) =>
            (true, Center(_movement.BuildingTargetBounds(
                new GameplayBuildingId(targetId)))),
        _ => (false, default)
    };

    private void ApplyProjectileImpact(
        CombatProjectileSnapshot projectile,
        long tick)
    {
        if (projectile.TargetKind == CombatTargetKind.Unit)
            ApplyUnitWeapon(projectile.AttackerUnit, projectile.TargetId,
                projectile.Weapon, tick, projectile.Id, projectile.Position);
        else if (projectile.TargetKind == CombatTargetKind.Building)
            ApplyBuildingWeapon(projectile.AttackerUnit,
                new GameplayBuildingId(projectile.TargetId),
                projectile.Weapon, tick, projectile.Id, projectile.Position);
    }

    private static Vector2 Center(SimRect bounds) =>
        (bounds.Min + bounds.Max) * 0.5f;

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
