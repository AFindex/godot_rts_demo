using System.Collections.Immutable;
using System.Numerics;

namespace RtsDemo.Simulation;

public enum BuildingCombatPhase : byte
{
    Idle,
    Windup,
    Cooldown
}

public enum BuildingCombatEventKind : byte
{
    AttackStarted,
    ProjectileLaunched,
    ProjectileExpired,
    Impact,
    TargetDestroyed
}

public readonly record struct BuildingCombatStateSnapshot(
    GameplayBuildingId BuildingId,
    BuildingCombatPhase Phase,
    int TargetUnit,
    int WeaponSlot,
    float CooldownRemaining,
    float WindupRemaining);

public readonly record struct BuildingCombatProjectileSnapshot(
    int Id,
    GameplayBuildingId AttackerBuilding,
    int TargetUnit,
    int WeaponSlot,
    Vector2 Position,
    float Speed,
    CombatWeaponDamageSnapshot Weapon,
    ImmutableArray<BuildingWeaponOnHitEffectSnapshot> OnHitEffects);

public sealed record BuildingCombatRuntimeSnapshot(
    int NextProjectileId,
    BuildingCombatStateSnapshot[] Buildings,
    BuildingCombatProjectileSnapshot[] Projectiles);

public readonly record struct BuildingCombatEvent(
    long Tick,
    ulong Sequence,
    BuildingCombatEventKind Kind,
    GameplayBuildingId AttackerBuilding,
    int TargetUnit,
    int WeaponSlot,
    int ProjectileId,
    Vector2 WorldPosition,
    float Damage = 0f,
    float RemainingHealth = 0f);

public readonly record struct BuildingCombatEventBatch(
    BuildingCombatEvent[] Events,
    ulong LatestSequence,
    int LostEvents);

/// <summary>
/// Derived presentation events for authoritative building attacks.  The
/// stream is deliberately separate from unit combat so an attacker ID never
/// needs a negative-number or offset encoding.
/// </summary>
public sealed class BuildingCombatEventStream
{
    private readonly BuildingCombatEvent[] _events;
    private ulong _nextSequence = 1;
    private int _count;

    public BuildingCombatEventStream(int capacity = 2048)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _events = new BuildingCombatEvent[capacity];
    }

    public ulong LatestSequence => _nextSequence - 1;

    public void Publish(
        long tick,
        BuildingCombatEventKind kind,
        GameplayBuildingId building,
        int targetUnit,
        int weaponSlot,
        int projectileId,
        Vector2 position,
        float damage = 0f,
        float remainingHealth = 0f)
    {
        var sequence = _nextSequence++;
        _events[(int)((sequence - 1) % (ulong)_events.Length)] =
            new BuildingCombatEvent(
                tick, sequence, kind, building, targetUnit, weaponSlot,
                projectileId, position, damage, remainingHealth);
        if (_count < _events.Length) _count++;
    }

    public BuildingCombatEventBatch ReadAfter(ulong sequence)
    {
        var latest = LatestSequence;
        if (sequence >= latest)
            return new BuildingCombatEventBatch([], latest, 0);
        var oldest = _count == 0 ? latest + 1 : latest - (ulong)_count + 1;
        var requested = sequence + 1;
        var lost = requested < oldest
            ? (int)Math.Min(oldest - requested, int.MaxValue)
            : 0;
        var first = Math.Max(requested, oldest);
        var values = new BuildingCombatEvent[checked((int)(latest - first + 1))];
        for (var index = 0; index < values.Length; index++)
        {
            var current = first + (ulong)index;
            values[index] = _events[
                (int)((current - 1) % (ulong)_events.Length)];
        }
        return new BuildingCombatEventBatch(values, latest, lost);
    }

    public void Reset()
    {
        _nextSequence = 1;
        _count = 0;
        Array.Clear(_events);
    }
}

/// <summary>
/// Deterministic building-weapon runtime.  It interprets generic profiles and
/// has no Warcraft rawcode, unit name, tower type or asset-path knowledge.
/// </summary>
public sealed class BuildingCombatSystem
{
    public const int MaximumProjectiles = 2048;
    private readonly UnitStore _units;
    private readonly CombatStore _combat;
    private readonly ConstructionSystem _construction;
    private readonly TechnologySystem _technology;
    private readonly PlayerDiplomacySystem _diplomacy;
    private readonly PlayerVisibilitySystem _visibility;
    private readonly Func<int, CombatTargetLayer> _targetLayer;
    private readonly Func<int, float, bool> _damageUnit;
    private readonly Func<int, float, float> _removeMana;
    private readonly Func<int, bool> _isHero;
    private readonly Func<int, bool> _isSummoned;
    private BuildingCombatStateSnapshot[] _states = [];
    private readonly List<BuildingCombatProjectileSnapshot> _projectiles = [];
    private int _nextProjectileId = 1;

    public BuildingCombatSystem(
        UnitStore units,
        CombatStore combat,
        ConstructionSystem construction,
        TechnologySystem technology,
        PlayerDiplomacySystem diplomacy,
        PlayerVisibilitySystem visibility,
        Func<int, CombatTargetLayer> targetLayer,
        Func<int, float, bool> damageUnit,
        Func<int, float, float> removeMana,
        Func<int, bool> isHero,
        Func<int, bool> isSummoned)
    {
        _units = units;
        _combat = combat;
        _construction = construction;
        _technology = technology;
        _diplomacy = diplomacy;
        _visibility = visibility;
        _targetLayer = targetLayer;
        _damageUnit = damageUnit;
        _removeMana = removeMana;
        _isHero = isHero;
        _isSummoned = isSummoned;
    }

    public BuildingCombatEventStream Events { get; } = new();
    public int ActiveProjectileCount => _projectiles.Count;
    public int NextProjectileId => _nextProjectileId;

    public BuildingCombatStateSnapshot Observe(GameplayBuildingId building) =>
        (uint)building.Value < (uint)_states.Length
            ? _states[building.Value]
            : new BuildingCombatStateSnapshot(
                building, BuildingCombatPhase.Idle, -1, -1, 0f, 0f);

    public BuildingCombatProjectileSnapshot[] ObserveProjectiles() =>
        _projectiles.ToArray();

    public void Update(float delta, long tick)
    {
        if (!float.IsFinite(delta) || delta < 0f)
            throw new ArgumentOutOfRangeException(nameof(delta));
        EnsureStateCapacity();
        UpdateProjectiles(delta, tick);
        for (var id = 0; id < _construction.SlotCount; id++)
            UpdateBuilding(new GameplayBuildingId(id), delta, tick);
    }

    private void EnsureStateCapacity()
    {
        var oldLength = _states.Length;
        if (oldLength >= _construction.SlotCount) return;
        Array.Resize(ref _states, _construction.SlotCount);
        for (var id = oldLength; id < _states.Length; id++)
            _states[id] = new BuildingCombatStateSnapshot(
                new GameplayBuildingId(id), BuildingCombatPhase.Idle,
                -1, -1, 0f, 0f);
    }

    private void UpdateBuilding(GameplayBuildingId id, float delta, long tick)
    {
        var state = _states[id.Value];
        var building = _construction.Observe(id);
        var profile = building.Type.Combat;
        if (building.State != BuildingLifecycleState.Completed ||
            !profile.Enabled)
        {
            _states[id.Value] = Idle(id);
            return;
        }

        var cooldown = MathF.Max(0f, state.CooldownRemaining - delta);
        state = state with { CooldownRemaining = cooldown };
        if (state.Phase == BuildingCombatPhase.Windup)
        {
            if (!TryWeaponForTarget(
                    building, state.TargetUnit, state.WeaponSlot,
                    requireAttackRange: true, out var weapon))
            {
                _states[id.Value] = state with
                {
                    Phase = cooldown > 0f
                        ? BuildingCombatPhase.Cooldown
                        : BuildingCombatPhase.Idle,
                    TargetUnit = -1,
                    WeaponSlot = -1,
                    WindupRemaining = 0f
                };
                return;
            }
            var windup = MathF.Max(0f, state.WindupRemaining - delta);
            state = state with { WindupRemaining = windup };
            if (windup <= 0f)
                state = Fire(building, state.TargetUnit, weapon, tick);
            _states[id.Value] = state;
            return;
        }

        if (cooldown > 0f)
        {
            _states[id.Value] = state with
            {
                Phase = BuildingCombatPhase.Cooldown,
                WindupRemaining = 0f
            };
            return;
        }

        if (!TryAcquire(building, out var target, out var selected))
        {
            _states[id.Value] = Idle(id);
            return;
        }

        Events.Publish(tick, BuildingCombatEventKind.AttackStarted,
            id, target, selected.Slot, -1, Center(building.Bounds));
        state = new BuildingCombatStateSnapshot(
            id, BuildingCombatPhase.Windup, target, selected.Slot,
            0f, selected.AttackWindupSeconds);
        if (selected.AttackWindupSeconds <= 0f)
            state = Fire(building, target, selected, tick);
        _states[id.Value] = state;
    }

    private BuildingCombatStateSnapshot Fire(
        in GameplayBuildingSnapshot building,
        int target,
        in CombatWeaponProfileSnapshot profile,
        long tick)
    {
        var source = Center(building.Bounds);
        var weapon = EffectiveWeapon(building.PlayerId, profile);
        var state = new BuildingCombatStateSnapshot(
            building.Id, BuildingCombatPhase.Cooldown, target, profile.Slot,
            profile.AttackCooldownSeconds, 0f);
        if (profile.ProjectileSpeed > 0f)
        {
            if (_projectiles.Count >= MaximumProjectiles ||
                _nextProjectileId == int.MaxValue)
            {
                Events.Publish(tick,
                    BuildingCombatEventKind.ProjectileExpired,
                    building.Id, target, profile.Slot, -1, source);
                return state;
            }
            var projectile = new BuildingCombatProjectileSnapshot(
                _nextProjectileId++, building.Id, target, profile.Slot,
                source, profile.ProjectileSpeed, weapon,
                EffectsForWeapon(building.Type.Combat, profile.Slot));
            _projectiles.Add(projectile);
            Events.Publish(tick, BuildingCombatEventKind.ProjectileLaunched,
                building.Id, target, profile.Slot, projectile.Id, source);
            return state;
        }
        ApplyImpact(
            building.Id, target, profile.Slot, -1, source, weapon,
            EffectsForWeapon(building.Type.Combat, profile.Slot), tick);
        return state;
    }

    private void UpdateProjectiles(float delta, long tick)
    {
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            var value = _projectiles[index];
            if (!ValidTargetForOwner(value.AttackerBuilding, value.TargetUnit))
            {
                Events.Publish(tick,
                    BuildingCombatEventKind.ProjectileExpired,
                    value.AttackerBuilding, value.TargetUnit, value.WeaponSlot,
                    value.Id, value.Position);
                _projectiles.RemoveAt(index);
                continue;
            }
            var targetPosition = _units.Positions[value.TargetUnit];
            var offset = targetPosition - value.Position;
            var distance = offset.Length();
            var step = value.Speed * delta;
            if (distance <= step || distance <= 0.001f)
            {
                ApplyImpact(
                    value.AttackerBuilding, value.TargetUnit, value.WeaponSlot,
                    value.Id, targetPosition, value.Weapon,
                    value.OnHitEffects, tick);
                _projectiles.RemoveAt(index);
                continue;
            }
            _projectiles[index] = value with
            {
                Position = value.Position + offset / distance * step
            };
        }
        _projectiles.Sort(static (left, right) => left.Id.CompareTo(right.Id));
    }

    private void ApplyImpact(
        GameplayBuildingId attacker,
        int target,
        int weaponSlot,
        int projectileId,
        Vector2 position,
        in CombatWeaponDamageSnapshot weapon,
        ImmutableArray<BuildingWeaponOnHitEffectSnapshot> onHitEffects,
        long tick)
    {
        if (!ValidTargetForOwner(attacker, target)) return;
        var resolved = CombatDamageResolver.Resolve(
            weapon, Defense(target), _combat.Health[target]);
        var effectDamage = ResolveOnHitDamage(
            target, onHitEffects, Defense(target), _combat.Health[target]);
        var totalDamage = MathF.Min(
            _combat.Health[target], resolved.TotalDamage + effectDamage);
        _damageUnit(target, totalDamage);
        Events.Publish(tick, BuildingCombatEventKind.Impact,
            attacker, target, weaponSlot, projectileId, position,
            totalDamage, _combat.Health[target]);
        if (!_units.Alive[target])
            Events.Publish(tick, BuildingCombatEventKind.TargetDestroyed,
                attacker, target, weaponSlot, projectileId, position,
                totalDamage, 0f);
        ApplyArea(attacker, target, weaponSlot, projectileId, position,
            weapon, tick);
    }

    private float ResolveOnHitDamage(
        int target,
        ImmutableArray<BuildingWeaponOnHitEffectSnapshot> effects,
        in CombatDefenseSnapshot defense,
        float health)
    {
        if (effects.IsDefaultOrEmpty) return 0f;
        var total = 0f;
        foreach (var effect in effects)
        {
            if (effect.Kind != BuildingWeaponOnHitEffectKind.ManaFeedback)
                continue;
            float raw;
            if (_isSummoned(target))
            {
                raw = effect.SummonedDamage;
            }
            else
            {
                var hero = _isHero(target);
                var maximum = hero
                    ? effect.HeroMaximumValue
                    : effect.UnitMaximumValue;
                var ratio = hero
                    ? effect.HeroDamagePerValue
                    : effect.UnitDamagePerValue;
                raw = _removeMana(target, maximum) * ratio;
            }
            if (raw <= 0f) continue;
            var available = MathF.Max(0f, health - total);
            if (available <= 0f) break;
            total += CombatDamageResolver.Resolve(
                new CombatWeaponDamageSnapshot(
                    raw, 1, CombatAttribute.None, 0f, 0, 0f, 0f,
                    CombatAttackType.Spells),
                defense with { ArmorType = defense.ArmorType == CombatArmorType.Legacy
                    ? CombatArmorType.Normal
                    : defense.ArmorType },
                available).TotalDamage;
        }
        return total;
    }

    private static ImmutableArray<BuildingWeaponOnHitEffectSnapshot>
        EffectsForWeapon(
            in BuildingCombatProfileSnapshot profile,
            int weaponSlot) =>
        profile.OnHitEffects.IsDefaultOrEmpty
            ? []
            : profile.OnHitEffects
                .Where(value => value.WeaponSlot == weaponSlot)
                .ToImmutableArray();

    private void ApplyArea(
        GameplayBuildingId attacker,
        int primary,
        int weaponSlot,
        int projectileId,
        Vector2 center,
        in CombatWeaponDamageSnapshot weapon,
        long tick)
    {
        if (!weapon.Area.Enabled) return;
        var scaled = weapon with { Area = default, Propagation = default };
        for (var target = 0; target < _units.Count; target++)
        {
            if (target == primary || !ValidTargetForOwner(attacker, target) ||
                (weapon.Area.TargetLayers & _targetLayer(target)) == 0)
                continue;
            var fraction = weapon.Area.DamageFraction(
                Vector2.Distance(center, _units.Positions[target]));
            if (fraction <= 0f) continue;
            var areaWeapon = scaled with
            {
                BaseDamage = scaled.BaseDamage * fraction,
                BonusDamage = scaled.BonusDamage * fraction,
                BaseUpgradeDamage = scaled.BaseUpgradeDamage * fraction,
                BonusUpgradeDamage = scaled.BonusUpgradeDamage * fraction
            };
            var resolved = CombatDamageResolver.Resolve(
                areaWeapon, Defense(target), _combat.Health[target]);
            _damageUnit(target, resolved.TotalDamage);
            Events.Publish(tick, BuildingCombatEventKind.Impact,
                attacker, target, weaponSlot, projectileId, center,
                resolved.TotalDamage, _combat.Health[target]);
            if (!_units.Alive[target])
                Events.Publish(tick, BuildingCombatEventKind.TargetDestroyed,
                    attacker, target, weaponSlot, projectileId, center,
                    resolved.TotalDamage, 0f);
        }
    }

    private bool TryAcquire(
        in GameplayBuildingSnapshot building,
        out int target,
        out CombatWeaponProfileSnapshot weapon)
    {
        target = -1;
        weapon = default;
        var center = Center(building.Bounds);
        var bestDistance = float.PositiveInfinity;
        for (var unit = 0; unit < _units.Count; unit++)
        {
            if (!TrySelectWeapon(building, unit, true, out var candidate))
                continue;
            var distance = Vector2.DistanceSquared(center, _units.Positions[unit]);
            if (distance > bestDistance ||
                distance == bestDistance && unit >= target)
                continue;
            target = unit;
            weapon = candidate;
            bestDistance = distance;
        }
        return target >= 0;
    }

    private bool TryWeaponForTarget(
        in GameplayBuildingSnapshot building,
        int target,
        int slot,
        bool requireAttackRange,
        out CombatWeaponProfileSnapshot weapon)
    {
        weapon = default;
        if (!ValidTarget(building, target)) return false;
        foreach (var candidate in building.Type.Combat.Weapons)
        {
            if (candidate.Slot != slot ||
                !WeaponEnabled(building.PlayerId, candidate) ||
                (candidate.TargetLayers & _targetLayer(target)) == 0 ||
                requireAttackRange && !InsideWeaponRange(
                    building, target, candidate))
                continue;
            weapon = candidate;
            return true;
        }
        return false;
    }

    private bool TrySelectWeapon(
        in GameplayBuildingSnapshot building,
        int target,
        bool requireAttackRange,
        out CombatWeaponProfileSnapshot weapon)
    {
        weapon = default;
        if (!ValidTarget(building, target)) return false;
        foreach (var candidate in building.Type.Combat.Weapons
                     .OrderBy(value => value.Slot))
        {
            if (!WeaponEnabled(building.PlayerId, candidate) ||
                (candidate.TargetLayers & _targetLayer(target)) == 0 ||
                requireAttackRange && !InsideWeaponRange(
                    building, target, candidate))
                continue;
            weapon = candidate;
            return true;
        }
        return false;
    }

    private bool ValidTarget(
        in GameplayBuildingSnapshot building,
        int target)
    {
        if ((uint)target >= (uint)_units.Count || !_units.Alive[target] ||
            !_diplomacy.IsEnemy(building.PlayerId, _combat.Teams[target]) ||
            !_visibility.IsUnitVisible(
                building.PlayerId, target, _units, _combat))
            return false;
        var allowed = building.Type.Combat.AcquisitionRange +
                      _units.Radii[target];
        return Vector2.DistanceSquared(
                   Center(building.Bounds), _units.Positions[target]) <=
               allowed * allowed;
    }

    private bool ValidTargetForOwner(GameplayBuildingId attacker, int target)
    {
        if (!_construction.IsAlive(attacker)) return false;
        return ValidTarget(_construction.Observe(attacker), target);
    }

    private bool InsideWeaponRange(
        in GameplayBuildingSnapshot building,
        int target,
        in CombatWeaponProfileSnapshot weapon)
    {
        var distance = Vector2.Distance(
            Center(building.Bounds), _units.Positions[target]);
        var radius = _units.Radii[target];
        return distance <= weapon.AttackRange + radius &&
               (weapon.MinimumRange <= 0f ||
                distance + radius >= weapon.MinimumRange);
    }

    private bool WeaponEnabled(
        int playerId, in CombatWeaponProfileSnapshot weapon) =>
        weapon.EnabledByDefault ||
        weapon.RequiredTechnologyId >= 0 &&
        _technology.Level(playerId, weapon.RequiredTechnologyId) > 0;

    private CombatWeaponDamageSnapshot EffectiveWeapon(
        int playerId, in CombatWeaponProfileSnapshot value)
    {
        var level = value.DamageUpgradeTechnologyId >= 0
            ? _technology.Level(playerId, value.DamageUpgradeTechnologyId)
            : 0;
        var propagation = value.Propagation;
        if (propagation.Kind == CombatWeaponPropagationKind.Line &&
            propagation.DistanceUpgradeTechnologyId >= 0)
        {
            propagation = propagation with
            {
                LineDistance = propagation.EffectiveLineDistance(
                    _technology.Level(
                        playerId,
                        propagation.DistanceUpgradeTechnologyId)),
                DistanceUpgradeTechnologyId = -1,
                DistanceUpgradePerLevel = 0f
            };
        }
        return new CombatWeaponDamageSnapshot(
            value.AttackDamage, value.AttacksPerVolley, value.BonusVs,
            value.BonusDamage, level, value.BaseUpgradeDamage,
            value.BonusUpgradeDamage, value.AttackType, value.Area,
            propagation);
    }

    private CombatDefenseSnapshot Defense(int unit)
    {
        var armorTechnology = _combat.ArmorUpgradeTechnologyIds[unit];
        var armorLevel = armorTechnology >= 0
            ? _technology.Level(_combat.Teams[unit], armorTechnology)
            : 0;
        return new CombatDefenseSnapshot(
            _combat.Armor[unit] +
            armorLevel * _combat.ArmorUpgradePerLevel[unit],
            _combat.Attributes[unit], _combat.ArmorTypes[unit]);
    }

    private static Vector2 Center(in SimRect bounds) =>
        (bounds.Min + bounds.Max) * 0.5f;

    private static BuildingCombatStateSnapshot Idle(GameplayBuildingId id) =>
        new(id, BuildingCombatPhase.Idle, -1, -1, 0f, 0f);

    public BuildingCombatRuntimeSnapshot CaptureRuntimeState()
    {
        EnsureStateCapacity();
        return new BuildingCombatRuntimeSnapshot(
            _nextProjectileId, _states.ToArray(), _projectiles.ToArray());
    }

    public void RestoreRuntimeState(BuildingCombatRuntimeSnapshot snapshot)
    {
        if (snapshot.NextProjectileId <= 0 ||
            snapshot.Projectiles.Length > MaximumProjectiles)
            throw new InvalidDataException();
        _states = snapshot.Buildings.ToArray();
        _projectiles.Clear();
        _projectiles.AddRange(snapshot.Projectiles);
        _nextProjectileId = snapshot.NextProjectileId;
        ValidateRuntime();
        Events.Reset();
    }

    private void ValidateRuntime()
    {
        for (var id = 0; id < _states.Length; id++)
        {
            var value = _states[id];
            if (value.BuildingId.Value != id || !Enum.IsDefined(value.Phase) ||
                value.TargetUnit < -1 || value.WeaponSlot is < -1 or > 7 ||
                !float.IsFinite(value.CooldownRemaining) ||
                value.CooldownRemaining < 0f ||
                !float.IsFinite(value.WindupRemaining) ||
                value.WindupRemaining < 0f)
                throw new InvalidDataException();
        }
        var previous = 0;
        foreach (var value in _projectiles)
        {
            if (value.Id <= previous || value.Id >= _nextProjectileId ||
                value.AttackerBuilding.Value < 0 || value.TargetUnit < 0 ||
                value.WeaponSlot is < 0 or > 7 || value.Speed <= 0f ||
                !float.IsFinite(value.Speed) ||
                !float.IsFinite(value.Position.X) ||
                !float.IsFinite(value.Position.Y) ||
                !CombatProjectileSystem.ValidWeapon(value.Weapon))
                throw new InvalidDataException();
            if (value.OnHitEffects.IsDefault ||
                value.OnHitEffects.Length > 16)
                throw new InvalidDataException();
            foreach (var effect in value.OnHitEffects)
            {
                try { effect.Validate(); }
                catch (ArgumentOutOfRangeException)
                {
                    throw new InvalidDataException();
                }
            }
            previous = value.Id;
        }
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(_nextProjectileId);
        hash.Add(_states.Length);
        foreach (var value in _states)
        {
            hash.Add(value.BuildingId.Value);
            hash.Add((byte)value.Phase);
            hash.Add(value.TargetUnit);
            hash.Add(value.WeaponSlot);
            hash.Add(value.CooldownRemaining);
            hash.Add(value.WindupRemaining);
        }
        hash.Add(_projectiles.Count);
        foreach (var value in _projectiles)
        {
            hash.Add(value.Id);
            hash.Add(value.AttackerBuilding.Value);
            hash.Add(value.TargetUnit);
            hash.Add(value.WeaponSlot);
            hash.Add(value.Position);
            hash.Add(value.Speed);
            CombatProjectileSystem.AddWeapon(ref hash, value.Weapon);
            hash.Add(value.OnHitEffects.IsDefault
                ? 0
                : value.OnHitEffects.Length);
            if (!value.OnHitEffects.IsDefault)
                foreach (var effect in value.OnHitEffects)
                {
                    hash.Add(effect.WeaponSlot);
                    hash.Add((byte)effect.Kind);
                    hash.Add(effect.UnitMaximumValue);
                    hash.Add(effect.UnitDamagePerValue);
                    hash.Add(effect.HeroMaximumValue);
                    hash.Add(effect.HeroDamagePerValue);
                    hash.Add(effect.SummonedDamage);
                }
        }
    }
}
