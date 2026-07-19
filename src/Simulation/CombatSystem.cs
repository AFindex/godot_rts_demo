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

internal readonly record struct CombatUpdateProfile(
    double ProjectileMilliseconds,
    double UnitLoopMilliseconds,
    double TargetSearchMilliseconds,
    int TargetSearches);

public interface ICombatMovementDriver
{
    void Chase(int unit, Vector2 target);
    void Retreat(int unit, Vector2 target);
    void StopForAttack(int unit);
    void ResumeAttackMove(int unit, Vector2 target);
    void FinishEngagement(int unit, UnitCommandIntent intent);
    void Kill(int unit);
    bool IsBuildingTargetValid(int attacker, GameplayBuildingId building);
    bool IsBuildingAlive(GameplayBuildingId building);
    SimRect BuildingTargetBounds(GameplayBuildingId building);
    GameplayBuildingId FindAutoBuildingTarget(int attacker, float acquisitionRange);
    bool TryResolveBuildingChaseTarget(
        int attacker, GameplayBuildingId building, out Vector2 target);
    CombatBuildingDamageResult DamageBuilding(
        GameplayBuildingId building, CombatWeaponDamageSnapshot weapon);
    ReadOnlySpan<GameplayBuildingSnapshot> CombatBuildingOverview();
    bool IsCombatObjectTargetValid(int attacker, CombatObjectId target);
    CombatObjectSnapshot CombatObject(CombatObjectId target);
    bool TryResolveCombatObjectChaseTarget(
        int attacker, CombatObjectId target, out Vector2 position);
    CombatObjectDamageResult DamageCombatObject(
        CombatObjectId target, CombatWeaponDamageSnapshot weapon);
    CombatObjectSnapshot[] CombatObjectOverview();
    int WeaponUpgradeLevel(int team);
    int TechnologyLevel(int team, int technologyId);
}

/// <summary>
/// Deterministic fixed-tick AttackMove state machine. Target selection uses nearest
/// distance then stable unit ID; expensive acquisition is staggered over six ticks.
/// </summary>
public sealed class CombatSystem
{
    private const int TargetSearchProfilingStride = 32;
    private const int AcquisitionTickStride = 6;
    private const int RetargetTickStride = 12;
    private const float ChaseRepathSeconds = 0.2f;
    private const float ChaseRetargetDistance = 12f;
    private const float MinimumTargetLockSeconds = 0.75f;
    private const float PriorityScoreWeight = 12_000f;
    private const float WeaponBonusScoreWeight = 6_000f;
    private const float ArmedThreatScoreWeight = 3_000f;
    private const float RetargetImprovementMargin = 2_500f;
    // A claim is intentionally expressed in the same squared-distance units
    // as the base score. One existing claimant is roughly equivalent to
    // looking 48 world units farther, so nearby enemies still win while a
    // whole formation no longer dog-piles the same front-most unit.
    private const float TargetClaimScoreWeight = 48f * 48f;
    private const float TargetOverkillScoreWeight = 32f;
    private const float SaturatedTargetSearchExpansion = 160f;
    private const int MaximumAutoTargetClaims = 4;

    private readonly UnitStore _units;
    private readonly CombatStore _combat;
    private readonly ICombatMovementDriver _movement;
    private readonly CombatEngagementSlotAllocator _slots;
    private readonly CombatEventStream _events;
    private readonly Func<int, int, bool> _canPerceiveTarget;
    private readonly Func<int, int, bool> _isHostileTarget;
    private readonly Func<int, bool> _canAttack;
    private readonly Func<int, bool> _canTakeDamage;
    private readonly Func<int, AbilityCombatModifier> _abilityModifier;
    private readonly Func<int, CombatTargetLayer> _unitTargetLayer;
    private readonly Func<int, int, bool> _hasTechnology;
    private readonly Func<CombatTargetKind, int, (bool Valid, Vector2 Position)>
        _resolveProjectileTarget;
    private readonly Action<CombatProjectileSnapshot> _applyProjectileImpact;
    private readonly Action<CombatProjectileSnapshot> _expireProjectile;
    private readonly SpatialHash _spatialHash;
    private int[] _targetCandidates;
    private int[] _targetClaimCounts;
    private int[] _targetClaimRanks;
    private float[] _targetCommittedDamage;
    private CombatTargetLayer[] _searchTargetLayers;
    private CombatTargetLayer[] _searchAttackableLayers;
    private CombatAttribute[] _searchGroundBonuses;
    private CombatAttribute[] _searchAirBonuses;
    private int[] _searchPerceptionTeams;
    private bool[] _searchPerceptionResults;
    private double _targetSearchMilliseconds;
    private int _targetSearches;
    private int _profiledTargetSearches;
    private long _projectileUpdateTick;
    public CombatProjectileSystem Projectiles { get; } = new();
    internal bool ProfilingEnabled { get; set; }
    internal CombatUpdateProfile LastUpdateProfile { get; private set; }

    public CombatSystem(
        UnitStore units,
        CombatStore combat,
        ICombatMovementDriver movement,
        StaticWorld world,
        CombatEventStream events,
        Func<int, int, bool> canPerceiveTarget,
        Func<int, int, bool> isHostileTarget,
        Func<int, bool> canAttack,
        Func<int, bool> canTakeDamage,
        Func<int, AbilityCombatModifier> abilityModifier,
        Func<int, CombatTargetLayer> unitTargetLayer,
        SpatialHash spatialHash)
    {
        _units = units;
        _combat = combat;
        _movement = movement;
        _slots = new CombatEngagementSlotAllocator(units, combat, world);
        _events = events;
        _canPerceiveTarget = canPerceiveTarget;
        _isHostileTarget = isHostileTarget;
        _canAttack = canAttack;
        _canTakeDamage = canTakeDamage;
        _abilityModifier = abilityModifier;
        _unitTargetLayer = unitTargetLayer;
        _spatialHash = spatialHash;
        _targetCandidates = new int[units.Capacity];
        _targetClaimCounts = new int[units.Capacity];
        _targetClaimRanks = new int[units.Capacity];
        _targetCommittedDamage = new float[units.Capacity];
        _searchTargetLayers = new CombatTargetLayer[units.Capacity];
        _searchAttackableLayers = new CombatTargetLayer[units.Capacity];
        _searchGroundBonuses = new CombatAttribute[units.Capacity];
        _searchAirBonuses = new CombatAttribute[units.Capacity];
        _searchPerceptionTeams = new int[units.Capacity];
        _searchPerceptionResults = new bool[units.Capacity];
        _hasTechnology = HasTechnology;
        _resolveProjectileTarget = ResolveProjectileTarget;
        _applyProjectileImpact = ApplyCurrentProjectileImpact;
        _expireProjectile = ExpireCurrentProjectile;
    }

    public void Update(float delta, long tick)
    {
        var updateStart = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        _targetSearchMilliseconds = 0d;
        _targetSearches = 0;
        _profiledTargetSearches = 0;
        RebuildTargetPressure();
        _slots.RebuildIndex();
        _projectileUpdateTick = tick;
        Projectiles.Update(
            delta,
            _resolveProjectileTarget,
            _applyProjectileImpact,
            _expireProjectile);
        PrepareTargetSearchCache();
        var projectileEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        foreach (var unit in _units.AliveUnits)
        {
            _combat.CooldownRemaining[unit] = MathF.Max(
                0f, _combat.CooldownRemaining[unit] - delta);
            _combat.ChaseRepathRemaining[unit] = MathF.Max(
                0f, _combat.ChaseRepathRemaining[unit] - delta);
            _combat.TargetLockRemaining[unit] = MathF.Max(
                0f, _combat.TargetLockRemaining[unit] - delta);

            if (_units.MovementGoalKinds[unit] ==
                    UnitMovementGoalKind.ProductionExit &&
                _units.MovementLegResults[unit] ==
                    UnitMovementLegResult.InProgress)
                continue;

            if (!_canAttack(unit))
            {
                if (_combat.TargetKinds[unit] != CombatTargetKind.None)
                    Disengage(unit);
                continue;
            }

            var intent = _combat.CommandIntents[unit];
            if (intent is not (UnitCommandIntent.None or
                UnitCommandIntent.AttackMove or UnitCommandIntent.AttackTarget or
                UnitCommandIntent.Stop or UnitCommandIntent.Hold))
            {
                continue;
            }

            if (_combat.TargetKinds[unit] == CombatTargetKind.Building)
            {
                UpdateBuildingTarget(unit, intent, delta, tick);
                continue;
            }
            if (_combat.TargetKinds[unit] == CombatTargetKind.Object)
            {
                UpdateCombatObjectTarget(unit, intent, delta, tick);
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
                    target = ProfiledFindBestTarget(unit);
                    if (target >= 0)
                    {
                        BeginEngagement(unit, target);
                    }
                    else
                    {
                        var acquisition = intent == UnitCommandIntent.Hold
                            ? _combat.AttackRanges[unit] + _units.Radii[unit]
                            : _combat.AcquisitionRanges[unit];
                        var building = CanTargetLayer(
                                unit, CombatTargetLayer.Building)
                            ? _movement.FindAutoBuildingTarget(unit, acquisition)
                            : new GameplayBuildingId(-1);
                        if (building.Value >= 0)
                            BeginBuildingEngagement(unit, building, tick);
                    }
                }
                continue;
            }

            if (!TrySelectTargetWeapon(unit, target))
            {
                Disengage(unit);
                continue;
            }

            if (intent != UnitCommandIntent.AttackTarget &&
                _combat.WindupRemaining[unit] <= 0f &&
                _combat.TargetLockRemaining[unit] <= 0f &&
                (tick + unit) % RetargetTickStride == 0)
            {
                var candidate = ProfiledFindBestTarget(unit);
                var candidateScore = candidate >= 0 && candidate != target
                    ? TargetScore(unit, candidate)
                    : default;
                var currentScore = candidate >= 0 && candidate != target
                    ? TargetScore(unit, target)
                    : default;
                var saturationRebalance = _units.AliveCount > 256 &&
                    candidate >= 0 && candidate != target &&
                    _targetClaimRanks[unit] >=
                    AutoTargetClaimCapacity(unit, target) &&
                    TargetClaimsExcludingAttacker(unit, candidate) <
                    AutoTargetClaimCapacity(unit, candidate) &&
                    candidateScore.TotalScore < currentScore.TotalScore;
                if (candidate >= 0 && candidate != target &&
                    (saturationRebalance ||
                     HasSemanticTargetAdvantage(candidateScore, currentScore) &&
                     candidateScore.TotalScore + RetargetImprovementMargin <
                     currentScore.TotalScore))
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

            var distanceSquared = Vector2.DistanceSquared(
                _units.Positions[unit], _units.Positions[target]);
            var range = _combat.AttackRanges[unit] +
                        _units.Radii[unit] + _units.Radii[target];
            var minimumRange = _combat.MinimumAttackRanges[unit] +
                               _units.Radii[unit] + _units.Radii[target];
            range += InteractionGeometry.NumericTolerance(
                _units.Positions[unit],
                new SimRect(
                    _units.Positions[target],
                    _units.Positions[target]));
            if (minimumRange > 0f &&
                distanceSquared < minimumRange * minimumRange)
            {
                if (intent == UnitCommandIntent.Hold)
                    Disengage(unit);
                else
                    RetreatFromUnit(
                        unit,
                        target,
                        minimumRange - MathF.Sqrt(distanceSquared));
            }
            else if (distanceSquared <= range * range)
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
        if (ProfilingEnabled)
        {
            var updateEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            LastUpdateProfile = new CombatUpdateProfile(
                ElapsedMilliseconds(updateStart, projectileEnd),
                ElapsedMilliseconds(projectileEnd, updateEnd),
                _profiledTargetSearches == 0
                    ? 0d
                    : _targetSearchMilliseconds * _targetSearches /
                      _profiledTargetSearches,
                _targetSearches);
        }
    }

    private int ProfiledFindBestTarget(int unit)
    {
        if (!ProfilingEnabled)
            return FindBestTarget(unit);
        _targetSearches++;
        if ((_targetSearches - 1) % TargetSearchProfilingStride != 0)
            return FindBestTarget(unit);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var target = FindBestTarget(unit);
        _targetSearchMilliseconds += ElapsedMilliseconds(
            start, System.Diagnostics.Stopwatch.GetTimestamp());
        _profiledTargetSearches++;
        return target;
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d / System.Diagnostics.Stopwatch.Frequency;

    private void ApplyCurrentProjectileImpact(
        CombatProjectileSnapshot projectile) =>
        ApplyProjectileImpact(projectile, _projectileUpdateTick);

    private void ExpireCurrentProjectile(
        CombatProjectileSnapshot projectile) =>
        _events.Publish(
            _projectileUpdateTick,
            CombatEventKind.ProjectileExpired,
            projectile.AttackerUnit,
            projectile.TargetKind,
            projectile.TargetId,
            projectileId: projectile.Id,
            worldPosition: projectile.Position);

    private void BeginBuildingEngagement(
        int unit,
        GameplayBuildingId building,
        long tick)
    {
        if (!_combat.TrySelectWeapon(
                unit, CombatTargetLayer.Building,
                _hasTechnology))
            return;
        _slots.Release(unit);
        ReleaseTargetClaim(unit);
        _combat.TargetUnits[unit] = -1;
        _combat.TargetBuildings[unit] = building.Value;
        _combat.TargetKinds[unit] = CombatTargetKind.Building;
        _combat.EngagementOrigins[unit] = _units.Positions[unit];
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.ChaseRepathRemaining[unit] = 0f;
        _combat.WindupRemaining[unit] = 0f;
        _combat.TargetLockRemaining[unit] = MinimumTargetLockSeconds;
        UpdateBuildingTarget(
            unit,
            _combat.CommandIntents[unit],
            delta: 0f,
            tick);
    }

    private void BeginEngagement(int unit, int target)
    {
        if (!TrySelectTargetWeapon(unit, target)) return;
        _slots.Release(unit);
        ReplaceTargetClaim(unit, target);
        _combat.TargetUnits[unit] = target;
        _combat.TargetBuildings[unit] = -1;
        _combat.TargetKinds[unit] = CombatTargetKind.Unit;
        _combat.EngagementOrigins[unit] = _units.Positions[unit];
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.ChaseRepathRemaining[unit] = 0f;
        _combat.WindupRemaining[unit] = 0f;
        _combat.TargetLockRemaining[unit] = MinimumTargetLockSeconds;
        if (_combat.CommandIntents[unit] != UnitCommandIntent.Hold &&
            RequiresAttackSlot(unit))
        {
            _slots.Assign(unit, target);
        }
        if (_combat.CommandIntents[unit] != UnitCommandIntent.Hold)
            UpdateChase(unit, target);
    }

    private void UpdateChase(int unit, int target)
    {
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.WindupRemaining[unit] = 0f;
        var targetPosition = RequiresAttackSlot(unit)
            ? _slots.ResolveChaseTarget(unit, target)
            : _units.Positions[target];
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

    private bool RequiresAttackSlot(int unit) =>
        _combat.PositioningKinds[unit] == CombatPositioningKind.Melee;

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

        if (!UnitFacing.IsWithin(
                _units.Facings[unit],
                _units.Positions[target] - _units.Positions[unit],
                _combat.AttackHalfAngles[unit]))
        {
            _combat.Phases[unit] = CombatPhase.Chasing;
            _movement.StopForAttack(unit);
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
        if (!IsDamageableTarget(attacker, target)) return;
        var adjustment = AbilityAttackAdjustmentFor(
            attacker, target, weapon.AttackType, tick, projectileId);
        if (adjustment.Multiplier <= 0f)
        {
            _events.Publish(tick, CombatEventKind.Impact, attacker,
                CombatTargetKind.Unit, target, 0f, _combat.Health[target],
                projectileId: projectileId,
                worldPosition: impactPosition ?? _units.Positions[target]);
            return;
        }
        weapon = ApplyAbilityAdjustment(weapon, adjustment);
        var result = CombatDamageResolver.Resolve(
            weapon,
            UnitDefense(target),
            _combat.Health[target]);
        var damage = result.TotalDamage;
        _combat.Health[target] = result.RemainingHealth;
        var worldPosition = impactPosition ?? _units.Positions[target];
        _events.Publish(tick, CombatEventKind.Impact, attacker,
            CombatTargetKind.Unit, target, damage, _combat.Health[target],
            result.DamagePerAttack, result.AttacksApplied, result.BonusApplied,
            projectileId, worldPosition);
        if (_combat.Health[target] <= 0f)
        {
            KillUnit(attacker, target, damage, tick, worldPosition);
        }
        ApplyAreaDamage(attacker, CombatTargetKind.Unit, target,
            worldPosition, weapon, tick, projectileId);
        ApplyWeaponPropagation(attacker, CombatTargetKind.Unit, target,
            worldPosition, weapon, tick, projectileId);
    }

    private void Disengage(int unit)
    {
        var intent = _combat.CommandIntents[unit];
        ReleaseTargetClaim(unit);
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
        // Preserve the established small/medium-match traversal exactly;
        // large battles are where the O(attackers * all units) scan dominates.
        if (_units.AliveCount <= 256)
            return FindBestTargetFullScan(unit);

        var best = -1;
        var bestScore = float.PositiveInfinity;
        var expandedBest = -1;
        var expandedBestScore = float.PositiveInfinity;
        var saturatedFallback = -1;
        var saturatedFallbackScore = float.PositiveInfinity;
        var intent = _combat.CommandIntents[unit];
        if (_targetCandidates.Length < _units.Count)
            Array.Resize(ref _targetCandidates, _units.Capacity);
        var baseQueryRadius = intent == UnitCommandIntent.Hold
            ? _combat.AttackRanges[unit] +
              _units.Radii[unit] +
              _spatialHash.MaximumRadius
            : _combat.AcquisitionRanges[unit];
        var queryRadius = baseQueryRadius +
                          (intent == UnitCommandIntent.Hold
                              ? 0f
                              : SaturatedTargetSearchExpansion);
        var candidateCount = _spatialHash.Query(
            _units.Positions[unit], queryRadius, unit,
            _targetCandidates.AsSpan(0, _units.AliveCount));
        for (var index = 0; index < candidateCount; index++)
        {
            var candidate = _targetCandidates[index];
            if (!IsValidSearchTarget(unit, candidate))
            {
                continue;
            }

            var distanceSquared = Vector2.DistanceSquared(
                _units.Positions[unit], _units.Positions[candidate]);
            var acquisitionRange = intent == UnitCommandIntent.Hold
                ? _combat.AttackRanges[unit] +
                  _units.Radii[unit] + _units.Radii[candidate]
                : _combat.AcquisitionRanges[unit];
            var insideNormalRange =
                distanceSquared <= acquisitionRange * acquisitionRange;
            var expandedRange = acquisitionRange +
                                (intent == UnitCommandIntent.Hold
                                    ? 0f
                                    : SaturatedTargetSearchExpansion);
            if (distanceSquared > expandedRange * expandedRange)
            {
                continue;
            }
            var score = SearchTargetScore(unit, candidate).TotalScore;
            if (TargetClaimsExcludingAttacker(unit, candidate) >=
                AutoTargetClaimCapacity(unit, candidate))
            {
                if (insideNormalRange &&
                    (score < saturatedFallbackScore ||
                    score == saturatedFallbackScore &&
                    candidate < saturatedFallback))
                {
                    saturatedFallback = candidate;
                    saturatedFallbackScore = score;
                }
                continue;
            }
            if (!insideNormalRange)
            {
                if (score < expandedBestScore ||
                    score == expandedBestScore && candidate < expandedBest)
                {
                    expandedBest = candidate;
                    expandedBestScore = score;
                }
                continue;
            }
            if (score < bestScore ||
                (score == bestScore && candidate < best))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best >= 0
            ? best
            : expandedBest >= 0
                ? expandedBest
                : saturatedFallback;
    }

    private int FindBestTargetFullScan(int unit)
    {
        var best = -1;
        var bestScore = float.PositiveInfinity;
        var intent = _combat.CommandIntents[unit];
        foreach (var candidate in _units.AliveUnits)
        {
            if (!IsValidSearchTarget(unit, candidate))
                continue;
            var distanceSquared = Vector2.DistanceSquared(
                _units.Positions[unit], _units.Positions[candidate]);
            var acquisitionRange = intent == UnitCommandIntent.Hold
                ? _combat.AttackRanges[unit] +
                  _units.Radii[unit] + _units.Radii[candidate]
                : _combat.AcquisitionRanges[unit];
            if (distanceSquared > acquisitionRange * acquisitionRange)
                continue;
            var score = SearchTargetScore(unit, candidate).TotalScore;
            if (score < bestScore ||
                score == bestScore && candidate < best)
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
        return TargetScore(
            attacker,
            target,
            ResolveWeaponBonusAttributes(attacker, TargetLayer(target)));
    }

    private CombatAutoTargetScore SearchTargetScore(int attacker, int target)
    {
        var layer = _searchTargetLayers[target];
        var bonus = layer switch
        {
            CombatTargetLayer.GroundUnit => _searchGroundBonuses[attacker],
            CombatTargetLayer.AirUnit => _searchAirBonuses[attacker],
            _ => ResolveWeaponBonusAttributes(attacker, layer)
        };
        return TargetScore(attacker, target, bonus);
    }

    private CombatAutoTargetScore TargetScore(
        int attacker,
        int target,
        CombatAttribute bonusVs)
    {
        var distanceSquared = Vector2.DistanceSquared(
            _units.Positions[attacker], _units.Positions[target]);
        var priority = _combat.AutoTargetPriority[target];
        var bonusMatch = bonusVs != CombatAttribute.None &&
                         (bonusVs &
                          _combat.Attributes[target]) != 0;
        var armedThreat = _combat.AttackDamage[target] > 0f;
        var claims = _targetClaimCounts[target];
        var committedDamage = _targetCommittedDamage[target];
        if (_combat.TargetKinds[attacker] == CombatTargetKind.Unit &&
            _combat.TargetUnits[attacker] == target)
        {
            claims = Math.Max(0, claims - 1);
            committedDamage = MathF.Max(
                0f, committedDamage - _combat.AttackDamage[attacker]);
        }
        var overkill = MathF.Max(
            0f, committedDamage - _combat.Health[target]);
        var saturationPenalty = claims * TargetClaimScoreWeight +
                                overkill * TargetOverkillScoreWeight;
        var total = distanceSquared - priority * PriorityScoreWeight -
                    (bonusMatch ? WeaponBonusScoreWeight : 0f) -
                    (armedThreat ? ArmedThreatScoreWeight : 0f) +
                    saturationPenalty;
        return new CombatAutoTargetScore(
            target, distanceSquared, priority, bonusMatch, armedThreat, total);
    }

    private void PrepareTargetSearchCache()
    {
        if (_searchTargetLayers.Length < _units.Count)
        {
            var capacity = _units.Capacity;
            Array.Resize(ref _searchTargetLayers, capacity);
            Array.Resize(ref _searchAttackableLayers, capacity);
            Array.Resize(ref _searchGroundBonuses, capacity);
            Array.Resize(ref _searchAirBonuses, capacity);
            Array.Resize(ref _searchPerceptionTeams, capacity);
            Array.Resize(ref _searchPerceptionResults, capacity);
        }

        foreach (var unit in _units.AliveUnits)
        {
            _searchTargetLayers[unit] = _unitTargetLayer(unit);
            _searchPerceptionTeams[unit] = int.MinValue;
            ResolveSearchWeaponProfile(
                unit,
                out _searchAttackableLayers[unit],
                out _searchGroundBonuses[unit],
                out _searchAirBonuses[unit]);
        }
    }

    private void ResolveSearchWeaponProfile(
        int unit,
        out CombatTargetLayer targetLayers,
        out CombatAttribute groundBonus,
        out CombatAttribute airBonus)
    {
        targetLayers = CombatTargetLayer.None;
        groundBonus = CombatAttribute.None;
        airBonus = CombatAttribute.None;
        var profiles = _combat.WeaponProfiles[unit];
        for (var index = 0; index < profiles.Length; index++)
        {
            var profile = profiles[index];
            if (!profile.EnabledByDefault &&
                (profile.RequiredTechnologyId < 0 ||
                 !HasTechnology(unit, profile.RequiredTechnologyId)))
                continue;
            var newlySupported = profile.TargetLayers & ~targetLayers;
            if ((newlySupported & CombatTargetLayer.GroundUnit) != 0)
                groundBonus = profile.BonusVs;
            if ((newlySupported & CombatTargetLayer.AirUnit) != 0)
                airBonus = profile.BonusVs;
            targetLayers |= profile.TargetLayers;
        }
    }

    private bool IsValidSearchTarget(int unit, int target)
    {
        if ((uint)target >= (uint)_units.Count || target == unit ||
            !_units.Alive[target] || !_canTakeDamage(target) ||
            !_isHostileTarget(_combat.Teams[unit], _combat.Teams[target]) ||
            (_searchAttackableLayers[unit] & _searchTargetLayers[target]) == 0)
            return false;

        var team = _combat.Teams[unit];
        if (_searchPerceptionTeams[target] == team)
            return _searchPerceptionResults[target];
        var result = _canPerceiveTarget(team, target);
        _searchPerceptionTeams[target] = team;
        _searchPerceptionResults[target] = result;
        return result;
    }

    private int TargetClaimsExcludingAttacker(int attacker, int target)
    {
        var claims = _targetClaimCounts[target];
        if (_combat.TargetKinds[attacker] == CombatTargetKind.Unit &&
            _combat.TargetUnits[attacker] == target)
            claims--;
        return Math.Max(0, claims);
    }

    private int AutoTargetClaimCapacity(int attacker, int target)
    {
        // Four simultaneous auto-attackers are enough to preserve focus fire
        // while keeping an 800-unit front distributed. If every visible enemy
        // is at capacity, FindBestTarget deterministically falls back to the
        // least costly saturated target, so units never lose a valid target.
        return MaximumAutoTargetClaims;
    }

    private void RebuildTargetPressure()
    {
        if (_targetClaimCounts.Length < _units.Count)
        {
            Array.Resize(ref _targetClaimCounts, _units.Capacity);
            Array.Resize(ref _targetClaimRanks, _units.Capacity);
            Array.Resize(ref _targetCommittedDamage, _units.Capacity);
        }
        foreach (var target in _units.AliveUnits)
        {
            _targetClaimCounts[target] = 0;
            _targetClaimRanks[target] = -1;
            _targetCommittedDamage[target] = 0f;
        }
        foreach (var attacker in _units.AliveUnits)
        {
            if (_combat.TargetKinds[attacker] != CombatTargetKind.Unit)
                continue;
            var target = _combat.TargetUnits[attacker];
            if ((uint)target >= (uint)_units.Count || !_units.Alive[target])
                continue;
            _targetClaimRanks[attacker] = _targetClaimCounts[target];
            _targetClaimCounts[target]++;
            _targetCommittedDamage[target] += _combat.AttackDamage[attacker];
        }
    }

    private void ReplaceTargetClaim(int attacker, int target)
    {
        ReleaseTargetClaim(attacker);
        if ((uint)target >= (uint)_units.Count) return;
        _targetClaimCounts[target]++;
        _targetCommittedDamage[target] += _combat.AttackDamage[attacker];
    }

    private void ReleaseTargetClaim(int attacker)
    {
        if (_combat.TargetKinds[attacker] != CombatTargetKind.Unit) return;
        var target = _combat.TargetUnits[attacker];
        if ((uint)target >= (uint)_units.Count) return;
        _targetClaimCounts[target] = Math.Max(
            0, _targetClaimCounts[target] - 1);
        _targetCommittedDamage[target] = MathF.Max(
            0f,
            _targetCommittedDamage[target] - _combat.AttackDamage[attacker]);
    }

    private CombatAttribute ResolveWeaponBonusAttributes(
        int attacker,
        CombatTargetLayer layer)
    {
        // Target scoring runs once per candidate. Passing a capturing
        // has-technology lambda into TryResolveWeapon allocated a delegate for
        // every attacker/candidate pair (about 6 MB per tick at 800 units).
        // Resolve the same immutable weapon profile directly and allocate
        // nothing in the acquisition loop.
        var profiles = _combat.WeaponProfiles[attacker];
        for (var index = 0; index < profiles.Length; index++)
        {
            var candidate = profiles[index];
            if ((candidate.TargetLayers & layer) == 0 ||
                !candidate.EnabledByDefault &&
                (candidate.RequiredTechnologyId < 0 ||
                 !HasTechnology(attacker, candidate.RequiredTechnologyId)))
            {
                continue;
            }
            return candidate.BonusVs;
        }
        return CombatAttribute.None;
    }

    private static bool HasSemanticTargetAdvantage(
        CombatAutoTargetScore candidate,
        CombatAutoTargetScore current) =>
        candidate.Priority > current.Priority ||
        candidate.WeaponBonusMatch && !current.WeaponBonusMatch ||
        candidate.ArmedThreat && !current.ArmedThreat;

    private bool IsValidTarget(int unit, int target) =>
        IsDamageableTarget(unit, target) &&
        CanTargetLayer(unit, TargetLayer(target)) &&
        _canPerceiveTarget(_combat.Teams[unit], target);

    private bool IsDamageableTarget(int unit, int target) =>
        (uint)target < (uint)_units.Count &&
        target != unit &&
        _units.Alive[target] &&
        _canTakeDamage(target) &&
        _isHostileTarget(_combat.Teams[unit], _combat.Teams[target]);

    private void UpdateBuildingTarget(
        int unit,
        UnitCommandIntent intent,
        float delta,
        long tick)
    {
        var building = new GameplayBuildingId(_combat.TargetBuildings[unit]);
        if (!_combat.TrySelectWeapon(
                unit, CombatTargetLayer.Building,
                _hasTechnology) ||
            !_movement.IsBuildingTargetValid(unit, building))
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
        var range = _combat.AttackRanges[unit] + _units.Radii[unit] +
                    InteractionGeometry.NumericTolerance(
                        _units.Positions[unit], bounds);
        var minimumRange = _combat.MinimumAttackRanges[unit] +
                           _units.Radii[unit];
        if (minimumRange > 0f && distance < minimumRange)
        {
            if (intent == UnitCommandIntent.Hold)
                Disengage(unit);
            else
                RetreatFromPoint(
                    unit, nearest, minimumRange - distance);
            return;
        }
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
        if (_units.MovementGoalKinds[unit] ==
                UnitMovementGoalKind.AttackRange &&
            _units.MovementLegResults[unit] ==
                UnitMovementLegResult.InProgress &&
            _units.Modes[unit] is
                UnitMoveMode.Moving or UnitMoveMode.WaitingForPath &&
            !_units.BlockedByNavigation[unit])
        {
            return;
        }
        if (!_movement.TryResolveBuildingChaseTarget(
                unit, building, out var chaseTarget))
        {
            Disengage(unit);
            return;
        }
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
        var bounds = _movement.BuildingTargetBounds(building);
        var targetPosition = (bounds.Min + bounds.Max) * 0.5f;
        if (!UnitFacing.IsWithin(
                _units.Facings[unit],
                targetPosition - _units.Positions[unit],
                _combat.AttackHalfAngles[unit]))
        {
            _combat.Phases[unit] = CombatPhase.Chasing;
            _movement.StopForAttack(unit);
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

    private void UpdateCombatObjectTarget(
        int unit,
        UnitCommandIntent intent,
        float delta,
        long tick)
    {
        var target = new CombatObjectId(_combat.TargetBuildings[unit]);
        if (!_movement.IsCombatObjectTargetValid(unit, target))
        {
            Disengage(unit);
            return;
        }
        var snapshot = _movement.CombatObject(target);
        if (!_combat.TrySelectWeapon(
                unit, snapshot.TargetLayer,
                _hasTechnology))
        {
            Disengage(unit);
            return;
        }
        var nearest = snapshot.Bounds.Clamp(_units.Positions[unit]);
        var distance = Vector2.Distance(_units.Positions[unit], nearest);
        var range = _combat.AttackRanges[unit] + _units.Radii[unit] +
                    InteractionGeometry.NumericTolerance(
                        _units.Positions[unit], snapshot.Bounds);
        var minimumRange = _combat.MinimumAttackRanges[unit] +
                           _units.Radii[unit];
        if (minimumRange > 0f && distance < minimumRange)
        {
            if (intent == UnitCommandIntent.Hold) Disengage(unit);
            else RetreatFromPoint(unit, nearest, minimumRange - distance);
            return;
        }
        if (distance <= range)
        {
            UpdateCombatObjectAttack(unit, target, intent, delta, tick);
            return;
        }
        if (intent == UnitCommandIntent.Hold)
        {
            Disengage(unit);
            return;
        }
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.WindupRemaining[unit] = 0f;
        if (!_movement.TryResolveCombatObjectChaseTarget(
                unit, target, out var chaseTarget))
        {
            Disengage(unit);
            return;
        }
        var stayedLocal = Vector2.DistanceSquared(
            chaseTarget, _combat.LastChaseTargets[unit]) <=
            ChaseRetargetDistance * ChaseRetargetDistance;
        if (stayedLocal && _units.Modes[unit] is
                UnitMoveMode.Moving or UnitMoveMode.WaitingForPath)
            return;
        if (_combat.ChaseRepathRemaining[unit] > 0f && stayedLocal) return;
        _combat.LastChaseTargets[unit] = chaseTarget;
        _combat.ChaseRepathRemaining[unit] = ChaseRepathSeconds;
        _movement.Chase(unit, chaseTarget);
    }

    private void UpdateCombatObjectAttack(
        int unit,
        CombatObjectId target,
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
                _movement.IsCombatObjectTargetValid(unit, target))
                FireAtCombatObject(unit, target, tick);
            return;
        }
        if (_combat.CooldownRemaining[unit] > 0f)
        {
            ApplyAttackMovement(
                unit, intent, _combat.CanMoveDuringCooldown[unit]);
            return;
        }
        var position = _movement.CombatObject(target).Position;
        if (!UnitFacing.IsWithin(
                _units.Facings[unit], position - _units.Positions[unit],
                _combat.AttackHalfAngles[unit]))
        {
            _combat.Phases[unit] = CombatPhase.Chasing;
            _movement.StopForAttack(unit);
            return;
        }
        var windup = _combat.AttackWindupDurations[unit];
        ApplyAttackMovement(unit, intent, _combat.CanMoveDuringWindup[unit]);
        PublishAttackStarted(
            unit, CombatTargetKind.Object, target.Value, tick);
        if (windup <= 0f) FireAtCombatObject(unit, target, tick);
        else _combat.WindupRemaining[unit] = windup;
    }

    private void FireAtCombatObject(
        int attacker,
        CombatObjectId target,
        long tick)
    {
        _combat.CooldownRemaining[attacker] =
            _combat.AttackCooldownDurations[attacker];
        var weapon = Weapon(attacker);
        if (_combat.ProjectileSpeed[attacker] > 0f)
        {
            if (Projectiles.Launch(
                    attacker, CombatTargetKind.Object, target.Value,
                    _units.Positions[attacker],
                    _combat.ProjectileSpeed[attacker], weapon,
                    out var projectileId))
                _events.Publish(
                    tick, CombatEventKind.ProjectileLaunched, attacker,
                    CombatTargetKind.Object, target.Value,
                    projectileId: projectileId,
                    worldPosition: _units.Positions[attacker]);
            return;
        }
        ApplyCombatObjectWeapon(attacker, target, weapon, tick, -1);
    }

    private void ApplyCombatObjectWeapon(
        int attacker,
        CombatObjectId target,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId,
        Vector2? impactPosition = null)
    {
        if (!_movement.IsCombatObjectTargetValid(attacker, target)) return;
        var position = impactPosition ?? _movement.CombatObject(target).Position;
        var result = _movement.DamageCombatObject(target, weapon);
        if (!result.Applied) return;
        _events.Publish(
            tick, CombatEventKind.Impact, attacker, CombatTargetKind.Object,
            target.Value, result.AppliedDamage, result.RemainingHealth,
            result.DamagePerAttack, result.AttacksApplied, result.BonusApplied,
            projectileId, position);
        ApplyAreaDamage(attacker, CombatTargetKind.Object, target.Value,
            position, weapon, tick, projectileId);
        ApplyWeaponPropagation(attacker, CombatTargetKind.Object, target.Value,
            position, weapon, tick, projectileId);
        if (!result.Destroyed) return;
        _events.Publish(
            tick, CombatEventKind.TargetDestroyed, attacker,
            CombatTargetKind.Object, target.Value, result.AppliedDamage, 0f,
            worldPosition: position);
        Disengage(attacker);
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
        ApplyAreaDamage(attacker, CombatTargetKind.Building, building.Value,
            worldPosition, weapon, tick, projectileId);
        ApplyWeaponPropagation(attacker, CombatTargetKind.Building,
            building.Value, worldPosition, weapon, tick, projectileId);
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

    private CombatWeaponDamageSnapshot Weapon(int attacker)
    {
        var technologyId = _combat.DamageUpgradeTechnologyIds[attacker];
        var upgradeLevel = technologyId >= 0
            ? _movement.TechnologyLevel(
                _combat.Teams[attacker], technologyId)
            : _movement.WeaponUpgradeLevel(_combat.Teams[attacker]);
        return new CombatWeaponDamageSnapshot(
            _combat.AttackDamage[attacker],
            _combat.AttacksPerVolley[attacker],
            _combat.BonusVs[attacker],
            _combat.BonusDamage[attacker],
            upgradeLevel,
            _combat.BaseUpgradeDamage[attacker],
            _combat.BonusUpgradeDamage[attacker],
            _combat.AttackTypes[attacker],
            _combat.WeaponAreas[attacker],
            EffectivePropagation(attacker));
    }

    private CombatWeaponPropagationSnapshot EffectivePropagation(int attacker)
    {
        var value = _combat.WeaponPropagations[attacker];
        if (value.Kind != CombatWeaponPropagationKind.Line ||
            value.DistanceUpgradeTechnologyId < 0)
            return value;
        var level = _movement.TechnologyLevel(
            _combat.Teams[attacker], value.DistanceUpgradeTechnologyId);
        var distance = value.EffectiveLineDistance(level);
        return distance <= 0f
            ? CombatWeaponPropagationSnapshot.None
            : value with
            {
                LineDistance = distance,
                DistanceUpgradeTechnologyId = -1,
                DistanceUpgradePerLevel = 0f
            };
    }

    private CombatTargetLayer TargetLayer(int target) =>
        _unitTargetLayer(target);

    private bool CanTargetLayer(int unit, CombatTargetLayer layer) =>
        _combat.TryResolveWeapon(
            unit, layer, _hasTechnology, out _);

    private bool TrySelectTargetWeapon(int unit, int target) =>
        _combat.TrySelectWeapon(
            unit, TargetLayer(target), _hasTechnology);

    private bool HasTechnology(int unit, int technologyId) =>
        _movement.TechnologyLevel(_combat.Teams[unit], technologyId) > 0;

    internal CombatDamageResult PreviewDamage(int attacker, int target)
    {
        if ((uint)attacker >= (uint)_units.Count ||
            (uint)target >= (uint)_units.Count ||
            !_units.Alive[attacker] || !_units.Alive[target])
            throw new ArgumentOutOfRangeException();
        return CombatDamageResolver.Resolve(
            Weapon(attacker),
            UnitDefense(target),
            _combat.Health[target]);
    }

    internal float EffectiveArmor(int unit)
    {
        var technologyId = _combat.ArmorUpgradeTechnologyIds[unit];
        var level = technologyId >= 0
            ? _movement.TechnologyLevel(_combat.Teams[unit], technologyId)
            : 0;
        return _combat.Armor[unit] +
               level * _combat.ArmorUpgradePerLevel[unit];
    }

    private CombatDefenseSnapshot UnitDefense(int unit) => new(
        EffectiveArmor(unit), _combat.Attributes[unit],
        _combat.ArmorTypes[unit]);

    private void RetreatFromUnit(int unit, int target, float shortfall) =>
        RetreatFromPoint(unit, _units.Positions[target], shortfall);

    private void RetreatFromPoint(int unit, Vector2 point, float shortfall)
    {
        _combat.Phases[unit] = CombatPhase.Chasing;
        _combat.WindupRemaining[unit] = 0f;
        var direction = _units.Positions[unit] - point;
        if (direction.LengthSquared() <= 0.000001f)
            direction = (unit & 1) == 0 ? Vector2.UnitX : -Vector2.UnitX;
        else
            direction = Vector2.Normalize(direction);
        var target = _units.Positions[unit] +
                     direction * MathF.Max(8f, shortfall + 2f);
        _combat.LastChaseTargets[unit] = target;
        _combat.ChaseRepathRemaining[unit] = ChaseRepathSeconds;
        _movement.Retreat(unit, target);
    }

    private void ApplyAreaDamage(
        int attacker,
        CombatTargetKind primaryKind,
        int primaryId,
        Vector2 center,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId)
    {
        if (!weapon.Area.Enabled) return;
        var scaled = weapon with { Area = CombatWeaponAreaSnapshot.None };
        foreach (var target in _units.AliveUnits)
        {
            if (primaryKind == CombatTargetKind.Unit && target == primaryId ||
                target == attacker || !_canTakeDamage(target) ||
                !_isHostileTarget(
                    _combat.Teams[attacker], _combat.Teams[target]))
                continue;
            var layer = TargetLayer(target);
            if ((weapon.Area.TargetLayers & layer) == 0) continue;
            var fraction = weapon.Area.DamageFraction(
                Vector2.Distance(center, _units.Positions[target]));
            if (fraction <= 0f) continue;
            ApplyAreaUnitDamage(
                attacker, target, center, Scale(scaled, fraction),
                tick, projectileId);
        }

        if ((weapon.Area.TargetLayers & CombatTargetLayer.Building) != 0)
        {
            foreach (var building in _movement.CombatBuildingOverview())
            {
                if (primaryKind == CombatTargetKind.Building &&
                        building.Id.Value == primaryId ||
                    !_movement.IsBuildingTargetValid(attacker, building.Id))
                    continue;
                var fraction = weapon.Area.DamageFraction(
                    Vector2.Distance(center, Center(building.Bounds)));
                if (fraction <= 0f) continue;
                ApplyAreaBuildingDamage(attacker, building.Id, center,
                    Scale(scaled, fraction), tick, projectileId);
            }
        }

        foreach (var target in _movement.CombatObjectOverview())
        {
            if (!target.Alive ||
                primaryKind == CombatTargetKind.Object &&
                    target.Id.Value == primaryId ||
                (weapon.Area.TargetLayers & target.TargetLayer) == 0 ||
                !_movement.IsCombatObjectTargetValid(attacker, target.Id))
                continue;
            var fraction = weapon.Area.DamageFraction(
                Vector2.Distance(center, target.Position));
            if (fraction <= 0f) continue;
            ApplyAreaCombatObjectDamage(
                attacker, target.Id, center, Scale(scaled, fraction),
                tick, projectileId);
        }
    }

    private void ApplyAreaUnitDamage(
        int attacker,
        int target,
        Vector2 center,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId)
    {
        var adjustment = AbilityAttackAdjustmentFor(
            attacker, target, weapon.AttackType, tick, projectileId);
        if (adjustment.Multiplier <= 0f) return;
        weapon = ApplyAbilityAdjustment(weapon, adjustment);
        var result = CombatDamageResolver.Resolve(
            weapon, UnitDefense(target), _combat.Health[target]);
        _combat.Health[target] = result.RemainingHealth;
        _events.Publish(tick, CombatEventKind.Impact, attacker,
            CombatTargetKind.Unit, target, result.TotalDamage,
            result.RemainingHealth, result.DamagePerAttack,
            result.AttacksApplied, result.BonusApplied, projectileId, center);
        if (result.Killed)
            KillUnit(attacker, target, result.TotalDamage, tick, center);
    }

    private void ApplyAreaBuildingDamage(
        int attacker,
        GameplayBuildingId building,
        Vector2 center,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId)
    {
        var result = _movement.DamageBuilding(building, weapon);
        if (!result.Applied) return;
        _events.Publish(tick, CombatEventKind.Impact, attacker,
            CombatTargetKind.Building, building.Value,
            result.AppliedDamage, result.RemainingHealth,
            result.DamagePerAttack, result.AttacksApplied, result.BonusApplied,
            projectileId, center);
        if (result.Destroyed)
            _events.Publish(tick, CombatEventKind.TargetDestroyed, attacker,
                CombatTargetKind.Building, building.Value,
                result.AppliedDamage, 0f, worldPosition: center);
    }

    private void ApplyAreaCombatObjectDamage(
        int attacker,
        CombatObjectId target,
        Vector2 center,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId)
    {
        var result = _movement.DamageCombatObject(target, weapon);
        if (!result.Applied) return;
        _events.Publish(
            tick, CombatEventKind.Impact, attacker, CombatTargetKind.Object,
            target.Value, result.AppliedDamage, result.RemainingHealth,
            result.DamagePerAttack, result.AttacksApplied, result.BonusApplied,
            projectileId, center);
        if (result.Destroyed)
            _events.Publish(
                tick, CombatEventKind.TargetDestroyed, attacker,
                CombatTargetKind.Object, target.Value,
                result.AppliedDamage, 0f, worldPosition: center);
    }

    private void ApplyWeaponPropagation(
        int attacker,
        CombatTargetKind primaryKind,
        int primaryId,
        Vector2 center,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId)
    {
        var propagation = weapon.Propagation;
        if (propagation.Kind == CombatWeaponPropagationKind.None) return;
        var secondary = weapon with
        {
            Area = CombatWeaponAreaSnapshot.None,
            Propagation = CombatWeaponPropagationSnapshot.None
        };
        if (propagation.Kind == CombatWeaponPropagationKind.Line)
            ApplyLinePropagation(attacker, primaryKind, primaryId, center,
                secondary, propagation, tick, projectileId);
        else if (propagation.Kind == CombatWeaponPropagationKind.Bounce)
            ApplyBouncePropagation(attacker, primaryKind, primaryId, center,
                secondary, propagation, tick, projectileId);
    }

    private void ApplyLinePropagation(
        int attacker,
        CombatTargetKind primaryKind,
        int primaryId,
        Vector2 center,
        CombatWeaponDamageSnapshot weapon,
        CombatWeaponPropagationSnapshot propagation,
        long tick,
        int projectileId)
    {
        if (propagation.LineDistance <= 0f) return;
        var direction = center - _units.Positions[attacker];
        if (direction.LengthSquared() <= 0.000001f)
            direction = (attacker & 1) == 0 ? Vector2.UnitX : -Vector2.UnitX;
        else
            direction = Vector2.Normalize(direction);

        var candidates = new List<PropagationTarget>();
        foreach (var target in _units.AliveUnits)
        {
            if (primaryKind == CombatTargetKind.Unit && target == primaryId ||
                target == attacker || !IsDamageableTarget(attacker, target) ||
                (propagation.TargetLayers & TargetLayer(target)) == 0)
                continue;
            AppendLineCandidate(candidates, CombatTargetKind.Unit, target,
                _units.Positions[target], center, direction, propagation);
        }
        if ((propagation.TargetLayers & CombatTargetLayer.Building) != 0)
        {
            foreach (var building in _movement.CombatBuildingOverview())
            {
                if (primaryKind == CombatTargetKind.Building &&
                        building.Id.Value == primaryId ||
                    !_movement.IsBuildingTargetValid(attacker, building.Id))
                    continue;
                AppendLineCandidate(candidates, CombatTargetKind.Building,
                    building.Id.Value, Center(building.Bounds), center,
                    direction, propagation);
            }
        }
        foreach (var target in _movement.CombatObjectOverview())
        {
            if (!target.Alive ||
                primaryKind == CombatTargetKind.Object &&
                    target.Id.Value == primaryId ||
                (propagation.TargetLayers & target.TargetLayer) == 0 ||
                !_movement.IsCombatObjectTargetValid(attacker, target.Id))
                continue;
            AppendLineCandidate(
                candidates, CombatTargetKind.Object, target.Id.Value,
                target.Position, center, direction, propagation);
        }

        candidates.Sort(PropagationTargetComparer.Instance);
        var count = Math.Min(
            candidates.Count, propagation.MaximumTargets - 1);
        for (var index = 0; index < count; index++)
        {
            var candidate = candidates[index];
            var fraction = propagation.DamageFraction(index + 1);
            if (fraction <= 0f) break;
            ApplyPropagationTarget(attacker, candidate, candidate.Position,
                Scale(weapon, fraction),
                tick, projectileId);
        }
    }

    private static void AppendLineCandidate(
        List<PropagationTarget> candidates,
        CombatTargetKind kind,
        int id,
        Vector2 position,
        Vector2 center,
        Vector2 direction,
        CombatWeaponPropagationSnapshot propagation)
    {
        var relative = position - center;
        var along = Vector2.Dot(relative, direction);
        if (along < 0f || along > propagation.LineDistance) return;
        var perpendicular = relative - direction * along;
        if (perpendicular.LengthSquared() >
            propagation.Radius * propagation.Radius) return;
        candidates.Add(new PropagationTarget(kind, id, position, along));
    }

    private void ApplyBouncePropagation(
        int attacker,
        CombatTargetKind primaryKind,
        int primaryId,
        Vector2 center,
        CombatWeaponDamageSnapshot weapon,
        CombatWeaponPropagationSnapshot propagation,
        long tick,
        int projectileId)
    {
        var visitedUnits = new HashSet<int>();
        var visitedBuildings = new HashSet<int>();
        var visitedObjects = new HashSet<int>();
        if (primaryKind == CombatTargetKind.Unit) visitedUnits.Add(primaryId);
        else if (primaryKind == CombatTargetKind.Building)
            visitedBuildings.Add(primaryId);
        else if (primaryKind == CombatTargetKind.Object)
            visitedObjects.Add(primaryId);
        var current = center;
        for (var index = 1; index < propagation.MaximumTargets; index++)
        {
            var candidate = FindBounceTarget(
                attacker, current, propagation, visitedUnits,
                visitedBuildings, visitedObjects);
            if (candidate.Kind == CombatTargetKind.None) break;
            if (candidate.Kind == CombatTargetKind.Unit)
                visitedUnits.Add(candidate.Id);
            else if (candidate.Kind == CombatTargetKind.Building)
                visitedBuildings.Add(candidate.Id);
            else
                visitedObjects.Add(candidate.Id);
            var fraction = propagation.DamageFraction(index);
            if (fraction <= 0f) break;
            ApplyPropagationTarget(attacker, candidate, candidate.Position,
                Scale(weapon, fraction),
                tick, projectileId);
            current = candidate.Position;
        }
    }

    private PropagationTarget FindBounceTarget(
        int attacker,
        Vector2 center,
        CombatWeaponPropagationSnapshot propagation,
        HashSet<int> visitedUnits,
        HashSet<int> visitedBuildings,
        HashSet<int> visitedObjects)
    {
        var best = default(PropagationTarget);
        var radiusSquared = propagation.Radius * propagation.Radius;
        foreach (var target in _units.AliveUnits)
        {
            if (target == attacker || visitedUnits.Contains(target) ||
                !IsDamageableTarget(attacker, target) ||
                (propagation.TargetLayers & TargetLayer(target)) == 0)
                continue;
            var distance = Vector2.DistanceSquared(
                center, _units.Positions[target]);
            if (distance > radiusSquared) continue;
            var candidate = new PropagationTarget(
                CombatTargetKind.Unit, target, _units.Positions[target],
                distance);
            if (Better(candidate, best)) best = candidate;
        }
        if ((propagation.TargetLayers & CombatTargetLayer.Building) != 0)
        {
            foreach (var building in _movement.CombatBuildingOverview())
            {
                if (visitedBuildings.Contains(building.Id.Value) ||
                    !_movement.IsBuildingTargetValid(attacker, building.Id))
                    continue;
                var position = Center(building.Bounds);
                var distance = Vector2.DistanceSquared(center, position);
                if (distance > radiusSquared) continue;
                var candidate = new PropagationTarget(
                    CombatTargetKind.Building, building.Id.Value, position,
                    distance);
                if (Better(candidate, best)) best = candidate;
            }
        }
        foreach (var target in _movement.CombatObjectOverview())
        {
            if (!target.Alive || visitedObjects.Contains(target.Id.Value) ||
                (propagation.TargetLayers & target.TargetLayer) == 0 ||
                !_movement.IsCombatObjectTargetValid(attacker, target.Id))
                continue;
            var distance = Vector2.DistanceSquared(center, target.Position);
            if (distance > radiusSquared) continue;
            var candidate = new PropagationTarget(
                CombatTargetKind.Object, target.Id.Value,
                target.Position, distance);
            if (Better(candidate, best)) best = candidate;
        }
        return best;
    }

    private static bool Better(
        PropagationTarget candidate,
        PropagationTarget current) =>
        current.Kind == CombatTargetKind.None ||
        candidate.Metric < current.Metric ||
        candidate.Metric == current.Metric &&
        ((byte)candidate.Kind < (byte)current.Kind ||
         candidate.Kind == current.Kind && candidate.Id < current.Id);

    private void ApplyPropagationTarget(
        int attacker,
        PropagationTarget target,
        Vector2 center,
        CombatWeaponDamageSnapshot weapon,
        long tick,
        int projectileId)
    {
        if (target.Kind == CombatTargetKind.Unit)
            ApplyAreaUnitDamage(
                attacker, target.Id, center, weapon, tick, projectileId);
        else if (target.Kind == CombatTargetKind.Building)
            ApplyAreaBuildingDamage(attacker,
                new GameplayBuildingId(target.Id), center, weapon, tick,
                projectileId);
        else
            ApplyAreaCombatObjectDamage(
                attacker, new CombatObjectId(target.Id), center, weapon,
                tick, projectileId);
    }

    private void KillUnit(
        int attacker, int target, float damage, long tick, Vector2 position)
    {
        ReleaseTargetClaim(target);
        _units.SetAlive(target, false);
        _combat.CommandIntents[target] = UnitCommandIntent.None;
        _combat.Phases[target] = CombatPhase.None;
        _combat.TargetUnits[target] = -1;
        _combat.TargetBuildings[target] = -1;
        _combat.TargetKinds[target] = CombatTargetKind.None;
        _slots.Release(target);
        _movement.Kill(target);
        _events.Publish(tick, CombatEventKind.TargetDestroyed, attacker,
            CombatTargetKind.Unit, target, damage, 0f,
            worldPosition: position);
    }

    private static CombatWeaponDamageSnapshot Scale(
        CombatWeaponDamageSnapshot weapon,
        float fraction) => weapon with
    {
        BaseDamage = weapon.BaseDamage * fraction,
        BonusDamage = weapon.BonusDamage * fraction,
        BaseUpgradeDamage = weapon.BaseUpgradeDamage * fraction,
        BonusUpgradeDamage = weapon.BonusUpgradeDamage * fraction
    };

    private static CombatWeaponDamageSnapshot ApplyAbilityAdjustment(
        CombatWeaponDamageSnapshot weapon,
        in AbilityAttackAdjustment adjustment)
    {
        weapon = Scale(weapon, adjustment.Multiplier);
        if (adjustment.BonusDamage == 0f) return weapon;
        return weapon with
        {
            BaseDamage = MathF.Max(0f, weapon.BaseDamage +
                adjustment.BonusDamage)
        };
    }

    private readonly record struct AbilityAttackAdjustment(
        float Multiplier,
        float BonusDamage);

    private AbilityAttackAdjustment AbilityAttackAdjustmentFor(
        int attacker,
        int target,
        CombatAttackType attackType,
        long tick,
        int projectileId)
    {
        var source = _abilityModifier(attacker).Normalized;
        var defense = _abilityModifier(target).Normalized;
        var factor = source.DamageDealtMultiplier *
                     defense.DamageTakenMultiplier;
        if (attackType is CombatAttackType.Magic or CombatAttackType.Spells)
            factor *= defense.MagicDamageTakenMultiplier;
        var weaponAttack = attackType != CombatAttackType.Spells;
        var critical = weaponAttack && Roll(
            source.CriticalStrikeChancePercent, tick, attacker, target,
            projectileId, 0xC3);
        var neverMiss = critical && source.CriticalStrikeNeverMiss;
        var missed = weaponAttack && Roll(
            source.AttackMissChancePercent, tick, attacker, target,
            projectileId, 0x51);
        var evaded = weaponAttack && Roll(
            defense.EvasionChancePercent, tick, attacker, target,
            projectileId, 0xE5);
        if ((missed || evaded) && !neverMiss)
            return default;
        var criticalBonus = 0f;
        if (critical)
        {
            factor *= source.CriticalStrikeDamageMultiplier <= 0f
                ? 1f
                : source.CriticalStrikeDamageMultiplier;
            criticalBonus = source.CriticalStrikeBonusDamage *
                            defense.DamageTakenMultiplier *
                            defense.MagicDamageTakenMultiplier;
        }
        if ((attackType is CombatAttackType.Pierce or CombatAttackType.Magic) &&
            Roll(defense.DeflectChancePercent, tick, attacker, target,
                projectileId, 0xA7))
        {
            factor *= attackType == CombatAttackType.Pierce
                ? defense.DeflectedPiercingDamageMultiplier
                : defense.DeflectedMagicDamageMultiplier;
        }
        return new AbilityAttackAdjustment(
            MathF.Max(0f, factor), criticalBonus);
    }

    private static bool Roll(
        float percent,
        long tick,
        int attacker,
        int target,
        int projectileId,
        int salt)
    {
        if (percent <= 0f) return false;
        if (percent >= 100f) return true;
        ulong value = (ulong)tick ^ ((ulong)(uint)attacker << 32) ^
                      (uint)target ^ ((ulong)(uint)projectileId << 17) ^
                      (uint)salt;
        value ^= value >> 33;
        value *= 0xff51afd7ed558ccdUL;
        value ^= value >> 33;
        return value % 10_000UL < (ulong)MathF.Round(percent * 100f);
    }

    private readonly record struct PropagationTarget(
        CombatTargetKind Kind,
        int Id,
        Vector2 Position,
        float Metric);

    private sealed class PropagationTargetComparer :
        IComparer<PropagationTarget>
    {
        public static PropagationTargetComparer Instance { get; } = new();

        public int Compare(PropagationTarget left, PropagationTarget right)
        {
            var metric = left.Metric.CompareTo(right.Metric);
            if (metric != 0) return metric;
            var kind = ((byte)left.Kind).CompareTo((byte)right.Kind);
            return kind != 0 ? kind : left.Id.CompareTo(right.Id);
        }
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
        CombatTargetKind.Object when _movement.IsCombatObjectTargetValid(
            -1, new CombatObjectId(targetId)) =>
            (true, _movement.CombatObject(
                new CombatObjectId(targetId)).Position),
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
        else if (projectile.TargetKind == CombatTargetKind.Object)
            ApplyCombatObjectWeapon(
                projectile.AttackerUnit,
                new CombatObjectId(projectile.TargetId),
                projectile.Weapon, tick, projectile.Id, projectile.Position);
    }

    private static Vector2 Center(SimRect bounds) =>
        (bounds.Min + bounds.Max) * 0.5f;

}
