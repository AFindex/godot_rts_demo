using System.Collections.Immutable;
using System.Numerics;

namespace RtsDemo.Simulation;

public enum AbilityCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidCaster,
    WrongOwner,
    UnknownAbility,
    AbilityNotLearned,
    PassiveAbility,
    InvalidTarget,
    FriendlyTargetRequired,
    EnemyTargetRequired,
    TargetNotVisible,
    OutOfRange,
    InsufficientMana,
    Cooldown,
    MagicImmune,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant,
    CasterDisabled,
    RequirementsNotMet,
    HeroOnly,
    NoSkillPoints,
    HeroLevelTooLow,
    MaximumLevel,
    AutoCastUnavailable
}

public readonly record struct AbilityCommandResult(
    AbilityCommandCode Code,
    int CasterUnit = -1,
    int AbilityId = -1)
{
    public bool Succeeded => Code == AbilityCommandCode.Success;
}

public readonly record struct AbilityCastTarget(
    AbilityTargetKind Kind,
    int Id,
    Vector2 Position)
{
    public static AbilityCastTarget Self(int unit, Vector2 position) =>
        new(AbilityTargetKind.Self, unit, position);

    public static AbilityCastTarget Unit(int unit, Vector2 position) =>
        new(AbilityTargetKind.Unit, unit, position);

    public static AbilityCastTarget Building(
        GameplayBuildingId building,
        Vector2 position) =>
        new(AbilityTargetKind.Building, building.Value, position);

    public static AbilityCastTarget Point(Vector2 position) =>
        new(AbilityTargetKind.World, -1, position);
}

public enum AbilityCastPhase : byte
{
    None,
    Casting,
    Channeling
}

public readonly record struct UnitAbilitySnapshot(
    int Unit,
    int UnitTypeId,
    bool Hero,
    int HeroLevel,
    int UnspentSkillPoints,
    float Mana,
    float MaximumMana,
    float ManaRegeneration,
    AbilityCastPhase CastPhase,
    int ActiveAbilityId,
    float CastRemaining,
    AbilityStatusFlags Statuses,
    UnitAbilitySlotSnapshot[] Abilities);

public readonly record struct UnitAbilitySlotSnapshot(
    int AbilityId,
    int Level,
    float CooldownRemaining,
    bool Toggled,
    bool AutoCastEnabled);

public readonly record struct AbilityBuffSnapshot(
    int InstanceId,
    int AbilityId,
    int SourceUnit,
    int TargetUnit,
    float RemainingSeconds,
    bool Beneficial,
    AbilityStatusFlags Status,
    AbilityStatModifier Modifier);

public readonly record struct AbilitySummonedUnitSnapshot(
    int Unit,
    int SourceUnit,
    string ObjectId,
    float RemainingSeconds);

internal interface IAbilityRuntimeWorld
{
    int AbilityUnitCount { get; }
    bool AbilityUnitExists(int unit);
    bool AbilityUnitAlive(int unit);
    int AbilityUnitOwner(int unit);
    Vector2 AbilityUnitPosition(int unit);
    float AbilityUnitRadius(int unit);
    float AbilityUnitHealth(int unit);
    float AbilityUnitMaximumHealth(int unit);
    CombatAttribute AbilityUnitAttributes(int unit);
    bool AbilityUnitIsAir(int unit);
    bool AbilityCanSeeUnit(int playerId, int unit);
    PlayerEntityRelation AbilityRelation(int playerId, int otherPlayerId);
    int AbilityTechnologyLevel(int playerId, int technologyId);
    int AbilityCompletedBuildingCount(int playerId, int buildingTypeId);
    bool AbilityBuildingAlive(GameplayBuildingId building);
    int AbilityBuildingOwner(GameplayBuildingId building);
    SimRect AbilityBuildingBounds(GameplayBuildingId building);
    bool AbilityCanSeePosition(int playerId, Vector2 position);
    bool AbilityDamageUnit(int sourceUnit, int targetUnit, float damage);
    bool AbilityHealUnit(int targetUnit, float amount);
    bool AbilityDamageBuilding(
        int sourceUnit,
        GameplayBuildingId building,
        float damage);
    bool AbilityReviveUnit(int unit, float healthFraction);
    void AbilitySetUnitOwner(int unit, int playerId);
    void AbilityTeleportUnit(int unit, Vector2 position);
    int AbilitySpawnSummon(
        int sourceUnit,
        int playerId,
        Vector2 position,
        in AbilitySummonProfile summon);
    void AbilityPrepareCaster(int unit);
    void AbilityKillSummon(int unit);
}

internal readonly record struct AbilityUnitRuntimeEntry(
    int Unit,
    int UnitTypeId,
    bool Hero,
    int HeroLevel,
    int UnspentSkillPoints,
    float Mana,
    float MaximumMana,
    float BaseManaRegeneration,
    float EffectiveManaRegeneration,
    float BaseMaximumSpeed,
    float BaseAttackDamage,
    float BaseArmor,
    float BaseAttackCooldown,
    float BaseMaximumHealth,
    float BaseDetectionRange,
    UnitConcealmentKind BaseConcealment,
    UnitConcealmentPhase BaseConcealmentPhase,
    int[] AbilityIds,
    int[] Levels,
    float[] Cooldowns,
    bool[] Toggles,
    bool[] AutoCast,
    AbilityCastPhase CastPhase,
    int ActiveSlot,
    float CastRemaining,
    float ChannelRemaining,
    float PulseRemaining,
    AbilityCastTarget Target);

internal readonly record struct AbilityBuffRuntimeEntry(
    int InstanceId,
    int AbilityId,
    int SourceUnit,
    int TargetUnit,
    float RemainingSeconds,
    bool Beneficial,
    AbilityStatusFlags Status,
    AbilityStatModifier Modifier);

internal readonly record struct AbilitySummonRuntimeEntry(
    int Unit,
    int SourceUnit,
    string ObjectId,
    float RemainingSeconds);

internal readonly record struct AbilityRevealRuntimeEntry(
    int PlayerId,
    Vector2 Position,
    float Radius,
    float RemainingSeconds);

internal sealed record AbilityRuntimeSnapshot(
    AbilityCatalogSnapshot Catalog,
    int NextBuffInstanceId,
    AbilityUnitRuntimeEntry[] Units,
    AbilityBuffRuntimeEntry[] Buffs,
    AbilitySummonRuntimeEntry[] Summons,
    AbilityRevealRuntimeEntry[] Reveals);

/// <summary>
/// Deterministic, content-neutral ability authority. It owns mana, cooldowns,
/// cast/channel state, timed modifiers, passives and summon lifetimes. World
/// mutation is deliberately routed through IAbilityRuntimeWorld.
/// </summary>
public sealed class AbilitySystem
{
    private readonly int _capacity;
    private readonly int[] _unitTypeIds;
    private readonly bool[] _heroes;
    private readonly int[] _heroLevels;
    private readonly int[] _unspentSkillPoints;
    private readonly float[] _mana;
    private readonly float[] _maximumMana;
    private readonly float[] _baseManaRegeneration;
    private readonly float[] _effectiveManaRegeneration;
    private readonly float[] _baseMaximumSpeed;
    private readonly float[] _baseAttackDamage;
    private readonly float[] _baseArmor;
    private readonly float[] _baseAttackCooldown;
    private readonly float[] _baseMaximumHealth;
    private readonly float[] _baseDetectionRange;
    private readonly UnitConcealmentKind[] _baseConcealment;
    private readonly UnitConcealmentPhase[] _baseConcealmentPhase;
    private readonly int[][] _abilityIds;
    private readonly int[][] _levels;
    private readonly float[][] _cooldowns;
    private readonly bool[][] _toggles;
    private readonly bool[][] _autoCast;
    private readonly AbilityStatusFlags[] _statuses;
    private readonly bool[] _abilityAppliedInvisibility;
    private readonly AbilityCastPhase[] _castPhases;
    private readonly int[] _activeSlots;
    private readonly float[] _castRemaining;
    private readonly float[] _channelRemaining;
    private readonly float[] _pulseRemaining;
    private readonly AbilityCastTarget[] _targets;
    private readonly AbilityStatModifier[] _modifierScratch;
    private readonly List<AbilityBuffRuntimeEntry> _buffs = [];
    private readonly List<AbilitySummonRuntimeEntry> _summons = [];
    private AbilityCatalogSnapshot _catalog = AbilityCatalogSnapshot.Empty;
    private int _nextBuffInstanceId = 1;
    private ulong _combatEventCursor;

    public AbilitySystem(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _unitTypeIds = new int[capacity];
        _heroes = new bool[capacity];
        _heroLevels = new int[capacity];
        _unspentSkillPoints = new int[capacity];
        _mana = new float[capacity];
        _maximumMana = new float[capacity];
        _baseManaRegeneration = new float[capacity];
        _effectiveManaRegeneration = new float[capacity];
        _baseMaximumSpeed = new float[capacity];
        _baseAttackDamage = new float[capacity];
        _baseArmor = new float[capacity];
        _baseAttackCooldown = new float[capacity];
        _baseMaximumHealth = new float[capacity];
        _baseDetectionRange = new float[capacity];
        _baseConcealment = new UnitConcealmentKind[capacity];
        _baseConcealmentPhase = new UnitConcealmentPhase[capacity];
        _abilityIds = new int[capacity][];
        _levels = new int[capacity][];
        _cooldowns = new float[capacity][];
        _toggles = new bool[capacity][];
        _autoCast = new bool[capacity][];
        _statuses = new AbilityStatusFlags[capacity];
        _abilityAppliedInvisibility = new bool[capacity];
        _castPhases = new AbilityCastPhase[capacity];
        _activeSlots = new int[capacity];
        _castRemaining = new float[capacity];
        _channelRemaining = new float[capacity];
        _pulseRemaining = new float[capacity];
        _targets = new AbilityCastTarget[capacity];
        _modifierScratch = new AbilityStatModifier[capacity];
        Array.Fill(_unitTypeIds, -1);
        Array.Fill(_activeSlots, -1);
        for (var unit = 0; unit < capacity; unit++)
        {
            _abilityIds[unit] = [];
            _levels[unit] = [];
            _cooldowns[unit] = [];
            _toggles[unit] = [];
            _autoCast[unit] = [];
        }
    }

    public AbilityCatalogSnapshot Catalog => _catalog;
    public int ActiveBuffCount => _buffs.Count;
    public int ActiveSummonCount => _summons.Count;

    public void ConfigureCatalog(AbilityCatalogSnapshot catalog, int unitCount = 0)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (unitCount != 0 || _buffs.Count != 0 || _summons.Count != 0)
            throw new InvalidOperationException(
                "Ability catalog must be configured before units are spawned.");
        _catalog = catalog;
    }

    internal void RegisterUnboundUnit(
        int unit,
        UnitStore units,
        CombatStore combat)
    {
        ValidateUnitSlot(unit);
        _unitTypeIds[unit] = -1;
        _heroes[unit] = false;
        _heroLevels[unit] = 0;
        _unspentSkillPoints[unit] = 0;
        _mana[unit] = 0f;
        _maximumMana[unit] = 0f;
        _baseManaRegeneration[unit] = 0f;
        _effectiveManaRegeneration[unit] = 0f;
        _baseMaximumSpeed[unit] = units.MaxSpeeds[unit];
        _baseAttackDamage[unit] = combat.AttackDamage[unit];
        _baseArmor[unit] = combat.Armor[unit];
        _baseAttackCooldown[unit] = combat.AttackCooldownDurations[unit];
        _baseMaximumHealth[unit] = combat.MaximumHealth[unit];
        _baseDetectionRange[unit] = combat.DetectionRanges[unit];
        _baseConcealment[unit] = combat.ConcealmentKinds[unit];
        _baseConcealmentPhase[unit] = combat.ConcealmentPhases[unit];
        _abilityIds[unit] = [];
        _levels[unit] = [];
        _cooldowns[unit] = [];
        _toggles[unit] = [];
        _autoCast[unit] = [];
        _statuses[unit] = AbilityStatusFlags.None;
        _abilityAppliedInvisibility[unit] = false;
        ClearCast(unit);
    }

    internal bool BindUnitType(int unit, int unitTypeId)
    {
        ValidateUnitSlot(unit);
        if (!_catalog.TryBinding(unitTypeId, out var binding)) return false;
        _unitTypeIds[unit] = unitTypeId;
        _heroes[unit] = binding.Hero;
        _heroLevels[unit] = binding.Hero ? 1 : 0;
        _unspentSkillPoints[unit] = binding.Hero ? 1 : 0;
        _mana[unit] = binding.Mana.Initial;
        _maximumMana[unit] = binding.Mana.Maximum;
        _baseManaRegeneration[unit] = binding.Mana.RegenerationPerSecond;
        _effectiveManaRegeneration[unit] = binding.Mana.RegenerationPerSecond;
        _abilityIds[unit] = binding.Abilities.Select(value => value.AbilityId).ToArray();
        _levels[unit] = binding.Abilities.Select(value => value.Level).ToArray();
        _cooldowns[unit] = new float[binding.Abilities.Length];
        _toggles[unit] = new bool[binding.Abilities.Length];
        _autoCast[unit] = binding.Abilities.Select(value =>
            value.Level > 0 && (value.AutoCastEnabled ||
            _catalog.Ability(value.AbilityId).AutoCastDefault)).ToArray();
        return true;
    }

    public UnitAbilitySnapshot Observe(int unit)
    {
        ValidateUnitSlot(unit);
        var slots = new UnitAbilitySlotSnapshot[_abilityIds[unit].Length];
        for (var slot = 0; slot < slots.Length; slot++)
            slots[slot] = new UnitAbilitySlotSnapshot(
                _abilityIds[unit][slot], _levels[unit][slot],
                _cooldowns[unit][slot], _toggles[unit][slot],
                _autoCast[unit][slot]);
        return new UnitAbilitySnapshot(
            unit, _unitTypeIds[unit], _heroes[unit],
            _heroLevels[unit], _unspentSkillPoints[unit], _mana[unit],
            _maximumMana[unit], _effectiveManaRegeneration[unit],
            _castPhases[unit],
            _activeSlots[unit] >= 0
                ? _abilityIds[unit][_activeSlots[unit]]
                : -1,
            _castRemaining[unit] + _channelRemaining[unit],
            _statuses[unit], slots);
    }

    public AbilityBuffSnapshot[] ObserveBuffs(int targetUnit) =>
        _buffs.Where(value => value.TargetUnit == targetUnit)
            .OrderBy(value => value.InstanceId)
            .Select(ToSnapshot)
            .ToArray();

    public AbilityBuffSnapshot[] ObserveAllBuffs() =>
        _buffs.OrderBy(value => value.InstanceId)
            .Select(ToSnapshot)
            .ToArray();

    private static AbilityBuffSnapshot ToSnapshot(
        AbilityBuffRuntimeEntry value) => new(
        value.InstanceId, value.AbilityId, value.SourceUnit,
        value.TargetUnit, value.RemainingSeconds, value.Beneficial,
        value.Status, value.Modifier);

    public bool TrySetLevel(int unit, int abilityId, int level)
    {
        ValidateUnitSlot(unit);
        var slot = Array.IndexOf(_abilityIds[unit], abilityId);
        if (slot < 0 || level < 0 ||
            level > _catalog.Ability(abilityId).Levels.Length ||
            level == 0 && !_catalog.Ability(abilityId).HeroAbility)
            return false;
        _levels[unit][slot] = level;
        return true;
    }

    public bool TrySetAutoCast(int unit, int abilityId, bool enabled)
    {
        ValidateUnitSlot(unit);
        var slot = Array.IndexOf(_abilityIds[unit], abilityId);
        if (slot < 0 || _levels[unit][slot] <= 0 ||
            _catalog.Ability(abilityId).IsPassive)
            return false;
        _autoCast[unit][slot] = enabled;
        return true;
    }

    public bool TryAdvanceHeroLevel(int unit, int levels = 1)
    {
        ValidateUnitSlot(unit);
        if (!_heroes[unit] || levels <= 0 ||
            _heroLevels[unit] > int.MaxValue - levels ||
            _unspentSkillPoints[unit] > int.MaxValue - levels)
            return false;
        _heroLevels[unit] += levels;
        _unspentSkillPoints[unit] += levels;
        return true;
    }

    internal AbilityCommandResult Learn(
        int playerId,
        int caster,
        int abilityId,
        IAbilityRuntimeWorld world)
    {
        if (playerId <= 0) return new(AbilityCommandCode.InvalidPlayer);
        if (!world.AbilityUnitExists(caster) || !world.AbilityUnitAlive(caster))
            return new(AbilityCommandCode.InvalidCaster, caster, abilityId);
        if (world.AbilityUnitOwner(caster) != playerId)
            return new(AbilityCommandCode.WrongOwner, caster, abilityId);
        if ((uint)abilityId >= (uint)_catalog.Count)
            return new(AbilityCommandCode.UnknownAbility, caster, abilityId);
        var slot = Array.IndexOf(_abilityIds[caster], abilityId);
        if (slot < 0) return new(AbilityCommandCode.AbilityNotLearned,
            caster, abilityId);
        var ability = _catalog.Ability(abilityId);
        if (!_heroes[caster] || !ability.HeroAbility)
            return new(AbilityCommandCode.HeroOnly, caster, abilityId);
        if (_unspentSkillPoints[caster] <= 0)
            return new(AbilityCommandCode.NoSkillPoints, caster, abilityId);
        var nextLevel = _levels[caster][slot] + 1;
        if (nextLevel > ability.Levels.Length)
            return new(AbilityCommandCode.MaximumLevel, caster, abilityId);
        var requiredHeroLevel = ability.RequiredHeroLevel +
                                (nextLevel - 1) * ability.HeroLevelSkip;
        if (_heroLevels[caster] < requiredHeroLevel)
            return new(AbilityCommandCode.HeroLevelTooLow, caster, abilityId);
        _levels[caster][slot] = nextLevel;
        _unspentSkillPoints[caster]--;
        return new(AbilityCommandCode.Success, caster, abilityId);
    }

    internal AbilityCommandResult SetAutoCast(
        int playerId,
        int caster,
        int abilityId,
        bool enabled,
        IAbilityRuntimeWorld world)
    {
        if (playerId <= 0) return new(AbilityCommandCode.InvalidPlayer);
        if (!world.AbilityUnitExists(caster) || !world.AbilityUnitAlive(caster))
            return new(AbilityCommandCode.InvalidCaster, caster, abilityId);
        if (world.AbilityUnitOwner(caster) != playerId)
            return new(AbilityCommandCode.WrongOwner, caster, abilityId);
        if ((uint)abilityId >= (uint)_catalog.Count)
            return new(AbilityCommandCode.UnknownAbility, caster, abilityId);
        var slot = Array.IndexOf(_abilityIds[caster], abilityId);
        if (slot < 0 || _levels[caster][slot] <= 0)
            return new(AbilityCommandCode.AbilityNotLearned, caster, abilityId);
        if (_catalog.Ability(abilityId).IsPassive)
            return new(AbilityCommandCode.AutoCastUnavailable, caster, abilityId);
        _autoCast[caster][slot] = enabled;
        return new(AbilityCommandCode.Success, caster, abilityId);
    }

    public AbilitySummonedUnitSnapshot[] ObserveSummons() =>
        _summons.OrderBy(value => value.Unit)
            .Select(value => new AbilitySummonedUnitSnapshot(
                value.Unit, value.SourceUnit, value.ObjectId,
                value.RemainingSeconds))
            .ToArray();

    public bool TrySummonedObjectId(int unit, out string objectId)
    {
        var summon = _summons.FirstOrDefault(value => value.Unit == unit);
        objectId = summon.ObjectId ?? string.Empty;
        return objectId.Length > 0;
    }

    public bool HasStatus(int unit, AbilityStatusFlags status) =>
        (uint)unit < (uint)_capacity && (_statuses[unit] & status) != 0;

    public bool CanMove(int unit) =>
        !HasStatus(unit, AbilityStatusFlags.Stunned |
                         AbilityStatusFlags.MovementDisabled |
                         AbilityStatusFlags.Polymorphed);

    public bool CanAttack(int unit) =>
        !HasStatus(unit, AbilityStatusFlags.Stunned |
                         AbilityStatusFlags.AttackDisabled |
                         AbilityStatusFlags.Polymorphed |
                         AbilityStatusFlags.Banished);

    public bool CanCast(int unit) =>
        !HasStatus(unit, AbilityStatusFlags.Stunned |
                         AbilityStatusFlags.Polymorphed |
                         AbilityStatusFlags.Banished);

    internal AbilityCommandResult Issue(
        int playerId,
        int caster,
        int abilityId,
        in AbilityCastTarget target,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        if (playerId <= 0) return new(AbilityCommandCode.InvalidPlayer);
        if (!world.AbilityUnitExists(caster) || !world.AbilityUnitAlive(caster))
            return new(AbilityCommandCode.InvalidCaster);
        if (world.AbilityUnitOwner(caster) != playerId)
            return new(AbilityCommandCode.WrongOwner);
        if (!CanCast(caster))
            return new(AbilityCommandCode.CasterDisabled, caster, abilityId);
        if ((uint)abilityId >= (uint)_catalog.Count)
            return new(AbilityCommandCode.UnknownAbility);
        var slot = Array.IndexOf(_abilityIds[caster], abilityId);
        if (slot < 0) return new(AbilityCommandCode.AbilityNotLearned);
        if (_levels[caster][slot] <= 0)
            return new(AbilityCommandCode.AbilityNotLearned, caster, abilityId);
        var ability = _catalog.Ability(abilityId);
        if (ability.IsPassive) return new(AbilityCommandCode.PassiveAbility);
        var level = ability.Levels[_levels[caster][slot] - 1];
        if (!RequirementsMet(playerId, level, world))
            return new(AbilityCommandCode.RequirementsNotMet, caster, abilityId);
        var validation = ValidateTarget(
            playerId, caster, ability, level, target, world);
        if (validation != AbilityCommandCode.Success)
            return new(validation, caster, abilityId);
        if (_cooldowns[caster][slot] > 0f)
            return new(AbilityCommandCode.Cooldown, caster, abilityId);
        if (_mana[caster] + 0.0001f < level.ManaCost)
            return new(AbilityCommandCode.InsufficientMana, caster, abilityId);

        CancelCast(caster, tick, AbilityEndReason.Canceled, world, events);
        _mana[caster] = MathF.Max(0f, _mana[caster] - level.ManaCost);
        _cooldowns[caster][slot] = level.CooldownSeconds;
        world.AbilityPrepareCaster(caster);
        events.Publish(
            tick, AbilityEventKind.Started, ability.RawId, caster,
            target.Kind, target.Id, target.Position);

        if (ability.Activation == AbilityActivationKind.Toggle)
        {
            Toggle(caster, slot, ability, level, target, tick, world, events);
            return new(AbilityCommandCode.Success, caster, abilityId);
        }

        _activeSlots[caster] = slot;
        _targets[caster] = target;
        if (level.CastSeconds > 0f)
        {
            _castPhases[caster] = AbilityCastPhase.Casting;
            _castRemaining[caster] = level.CastSeconds;
        }
        else
        {
            BeginImpact(caster, tick, world, events);
        }
        return new(AbilityCommandCode.Success, caster, abilityId);
    }

    internal void Update(
        float delta,
        long tick,
        IAbilityRuntimeWorld world,
        UnitStore units,
        CombatStore combat,
        AbilityEventStream events)
    {
        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            for (var slot = 0; slot < _cooldowns[unit].Length; slot++)
                _cooldowns[unit][slot] = MathF.Max(
                    0f, _cooldowns[unit][slot] - delta);
            if (world.AbilityUnitAlive(unit) && _maximumMana[unit] > 0f)
                _mana[unit] = MathF.Min(
                    _maximumMana[unit],
                    _mana[unit] + _effectiveManaRegeneration[unit] * delta);

            if (_castPhases[unit] != AbilityCastPhase.None &&
                !world.AbilityUnitAlive(unit))
            {
                CancelCast(
                    unit, tick, AbilityEndReason.CasterDied, world, events);
                continue;
            }
            if (_castPhases[unit] != AbilityCastPhase.None && !CanCast(unit))
            {
                CancelCast(
                    unit, tick, AbilityEndReason.Canceled, world, events);
                continue;
            }
            if (_castPhases[unit] == AbilityCastPhase.Casting)
            {
                _castRemaining[unit] -= delta;
                if (_castRemaining[unit] <= 0f)
                    BeginImpact(unit, tick, world, events);
                continue;
            }
            if (_castPhases[unit] == AbilityCastPhase.Channeling)
            {
                UpdateChannel(unit, delta, tick, world, events);
                continue;
            }
            if (world.AbilityUnitAlive(unit))
                TryAutoCast(unit, tick, world, events);
        }

        for (var index = _buffs.Count - 1; index >= 0; index--)
        {
            var buff = _buffs[index];
            if (!world.AbilityUnitAlive(buff.TargetUnit))
            {
                _buffs.RemoveAt(index);
                continue;
            }
            if (!float.IsPositiveInfinity(buff.RemainingSeconds))
            {
                buff = buff with { RemainingSeconds = buff.RemainingSeconds - delta };
                if (buff.RemainingSeconds <= 0f)
                {
                    _buffs.RemoveAt(index);
                    continue;
                }
                _buffs[index] = buff;
            }
        }

        for (var index = _summons.Count - 1; index >= 0; index--)
        {
            var summon = _summons[index];
            if (!world.AbilityUnitAlive(summon.Unit))
            {
                _summons.RemoveAt(index);
                continue;
            }
            summon = summon with { RemainingSeconds = summon.RemainingSeconds - delta };
            if (summon.RemainingSeconds <= 0f)
            {
                world.AbilityKillSummon(summon.Unit);
                _summons.RemoveAt(index);
            }
            else
            {
                _summons[index] = summon;
            }
        }

        RebuildDerivedStats(delta, world, units, combat);
    }

    private bool TryAutoCast(
        int caster,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        for (var slot = 0; slot < _abilityIds[caster].Length; slot++)
        {
            if (!_autoCast[caster][slot] || _cooldowns[caster][slot] > 0f)
                continue;
            if (_levels[caster][slot] <= 0) continue;
            var ability = _catalog.Ability(_abilityIds[caster][slot]);
            if (ability.Activation is not (AbilityActivationKind.TargetUnit or
                    AbilityActivationKind.ChannelUnit))
                continue;
            var level = ability.Levels[_levels[caster][slot] - 1];
            if (_mana[caster] + 0.0001f < level.ManaCost) continue;
            for (var target = 0; target < world.AbilityUnitCount; target++)
            {
                var castTarget = AbilityCastTarget.Unit(
                    target, world.AbilityUnitPosition(target));
                if (ValidateTarget(
                        world.AbilityUnitOwner(caster), caster, ability, level,
                        castTarget, world) != AbilityCommandCode.Success ||
                    !ShouldAutoCast(caster, target, ability, level, world))
                    continue;
                return Issue(
                    world.AbilityUnitOwner(caster), caster, ability.Id,
                    castTarget, tick, world, events).Succeeded;
            }
        }
        return false;
    }

    private bool ShouldAutoCast(
        int caster,
        int target,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        IAbilityRuntimeWorld world)
    {
        var abilityId = ability.Id;
        var relation = RelationFilter(
            world.AbilityUnitOwner(caster), caster, target,
            world.AbilityUnitOwner(target), world);
        foreach (var effect in level.Effects)
        {
            if ((effect.Relations & relation) == 0) continue;
            if (effect.Kind == AbilityEffectKind.Heal &&
                world.AbilityUnitHealth(target) + 0.5f <
                world.AbilityUnitMaximumHealth(target))
                return true;
            if ((effect.Kind is AbilityEffectKind.ApplyStatus or
                    AbilityEffectKind.ToggleStatus) &&
                !_buffs.Any(value => value.AbilityId == abilityId &&
                                     value.TargetUnit == target))
                return true;
            if (relation == AbilityRelationFilter.Enemy &&
                effect.Kind is (AbilityEffectKind.Damage or
                    AbilityEffectKind.Mana or
                    AbilityEffectKind.TransferControl or
                    AbilityEffectKind.TransferBuff))
                return true;
        }
        return false;
    }

    internal void ProcessCombatEvents(
        long tick,
        CombatEventStream combatEvents,
        IAbilityRuntimeWorld world,
        AbilityEventStream abilityEvents)
    {
        var batch = combatEvents.ReadAfter(_combatEventCursor);
        _combatEventCursor = batch.LatestSequence;
        foreach (var combatEvent in batch.Events)
        {
            if (combatEvent.Kind != CombatEventKind.Impact ||
                combatEvent.TargetKind != CombatTargetKind.Unit ||
                !world.AbilityUnitAlive(combatEvent.AttackerUnit))
                continue;
            var caster = combatEvent.AttackerUnit;
            var target = AbilityCastTarget.Unit(
                combatEvent.TargetId, combatEvent.WorldPosition);
            for (var slot = 0; slot < _abilityIds[caster].Length; slot++)
            {
                if (_levels[caster][slot] <= 0) continue;
                var ability = _catalog.Ability(_abilityIds[caster][slot]);
                var level = ability.Levels[_levels[caster][slot] - 1];
                if (!RequirementsMet(
                        world.AbilityUnitOwner(caster), level, world))
                    continue;
                var triggered = false;
                for (var effectIndex = 0;
                     effectIndex < level.Effects.Length;
                     effectIndex++)
                {
                    var effect = level.Effects[effectIndex];
                    if (effect.Timing != AbilityEffectTiming.AttackHit)
                        continue;
                    var chance = effect.Interval <= 0f
                        ? 100f
                        : effect.Interval;
                    if (!DeterministicRoll(
                            combatEvent.Sequence, caster,
                            ability.Id, effectIndex, chance))
                        continue;
                    if (!triggered)
                    {
                        abilityEvents.Publish(
                            tick, AbilityEventKind.Started, ability.RawId,
                            caster, target.Kind, target.Id, target.Position);
                        abilityEvents.Publish(
                            tick, AbilityEventKind.Impact, ability.RawId,
                            caster, target.Kind, target.Id, target.Position);
                        triggered = true;
                    }
                    ApplyEffect(
                        caster, ability, level, effect, target, world);
                }
                if (triggered)
                    abilityEvents.Publish(
                        tick, AbilityEventKind.Ended, ability.RawId, caster,
                        target.Kind, target.Id, target.Position,
                        AbilityEndReason.Completed);
            }
        }
    }

    internal void CancelCaster(
        int caster,
        long tick,
        AbilityEndReason reason,
        IAbilityRuntimeWorld world,
        AbilityEventStream events) =>
        CancelCast(caster, tick, reason, world, events);

    public void ApplyVisibilitySources(PlayerVisibilitySystem visibility)
    {
        foreach (var source in _revealSources)
            visibility.RevealAbilityArea(
                source.PlayerId, source.Position, source.Radius, detection: true);
    }

    private readonly List<AbilityRevealRuntimeEntry> _revealSources = [];

    private void BeginImpact(
        int caster,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        var slot = _activeSlots[caster];
        if (slot < 0) return;
        var ability = _catalog.Ability(_abilityIds[caster][slot]);
        var level = ability.Levels[_levels[caster][slot] - 1];
        var target = _targets[caster];
        var validation = ValidateTarget(
            world.AbilityUnitOwner(caster), caster, ability, level,
            target, world, impactValidation: true);
        if (validation != AbilityCommandCode.Success)
        {
            CancelCast(
                caster, tick, AbilityEndReason.TargetInvalid, world, events);
            return;
        }
        events.Publish(
            tick, AbilityEventKind.Impact, ability.RawId, caster,
            target.Kind, target.Id, target.Position);
        ApplyEffects(
            caster, ability, level, AbilityEffectTiming.Impact, target, world);
        _castRemaining[caster] = 0f;
        if (level.ChannelSeconds > 0f && level.Effects.Any(value =>
                value.Timing == AbilityEffectTiming.ChannelPulse))
        {
            _castPhases[caster] = AbilityCastPhase.Channeling;
            _channelRemaining[caster] = level.ChannelSeconds;
            _pulseRemaining[caster] = 0f;
            return;
        }
        CompleteCast(caster, tick, events);
    }

    private void UpdateChannel(
        int caster,
        float delta,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        var slot = _activeSlots[caster];
        if (slot < 0) return;
        var ability = _catalog.Ability(_abilityIds[caster][slot]);
        var level = ability.Levels[_levels[caster][slot] - 1];
        var target = _targets[caster];
        if (ValidateTarget(
                world.AbilityUnitOwner(caster), caster, ability, level,
                target, world, impactValidation: true) !=
            AbilityCommandCode.Success)
        {
            CancelCast(
                caster, tick, AbilityEndReason.TargetInvalid, world, events);
            return;
        }
        _channelRemaining[caster] -= delta;
        _pulseRemaining[caster] -= delta;
        var interval = level.Effects
            .Where(value => value.Timing == AbilityEffectTiming.ChannelPulse)
            .Select(value => value.Interval)
            .Where(value => value > 0f)
            .DefaultIfEmpty(1f)
            .Min();
        while (_pulseRemaining[caster] <= 0f &&
               _channelRemaining[caster] > -interval)
        {
            events.Publish(
                tick, AbilityEventKind.Impact, ability.RawId, caster,
                target.Kind, target.Id, target.Position);
            ApplyEffects(
                caster, ability, level, AbilityEffectTiming.ChannelPulse,
                target, world);
            _pulseRemaining[caster] += MathF.Max(0.05f, interval);
        }
        if (_channelRemaining[caster] <= 0f)
            CompleteCast(caster, tick, events);
    }

    private void Toggle(
        int caster,
        int slot,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityCastTarget target,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        _toggles[caster][slot] = !_toggles[caster][slot];
        if (_toggles[caster][slot])
        {
            ApplyEffects(
                caster, ability, level, AbilityEffectTiming.Impact,
                target, world, toggle: true);
        }
        else
        {
            var abilityId = ability.Id;
            _buffs.RemoveAll(value =>
                value.SourceUnit == caster && value.TargetUnit == caster &&
                value.AbilityId == abilityId &&
                float.IsPositiveInfinity(value.RemainingSeconds));
        }
        events.Publish(
            tick, AbilityEventKind.Impact, ability.RawId, caster,
            target.Kind, target.Id, target.Position);
        events.Publish(
            tick, AbilityEventKind.Ended, ability.RawId, caster,
            target.Kind, target.Id, target.Position,
            AbilityEndReason.Completed);
    }

    private void CompleteCast(
        int caster,
        long tick,
        AbilityEventStream events)
    {
        var slot = _activeSlots[caster];
        if (slot < 0) return;
        var ability = _catalog.Ability(_abilityIds[caster][slot]);
        var target = _targets[caster];
        events.Publish(
            tick, AbilityEventKind.Ended, ability.RawId, caster,
            target.Kind, target.Id, target.Position,
            AbilityEndReason.Completed);
        ClearCast(caster);
    }

    private void CancelCast(
        int caster,
        long tick,
        AbilityEndReason reason,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        if ((uint)caster >= (uint)_capacity ||
            _castPhases[caster] == AbilityCastPhase.None ||
            _activeSlots[caster] < 0)
            return;
        var ability = _catalog.Ability(
            _abilityIds[caster][_activeSlots[caster]]);
        var target = _targets[caster];
        events.Publish(
            tick, AbilityEventKind.Interrupted, ability.RawId, caster,
            target.Kind, target.Id, target.Position, reason);
        ClearCast(caster);
    }

    private void ClearCast(int unit)
    {
        _castPhases[unit] = AbilityCastPhase.None;
        _activeSlots[unit] = -1;
        _castRemaining[unit] = 0f;
        _channelRemaining[unit] = 0f;
        _pulseRemaining[unit] = 0f;
        _targets[unit] = default;
    }

    private AbilityCommandCode ValidateTarget(
        int playerId,
        int caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world,
        bool impactValidation = false)
    {
        switch (ability.Activation)
        {
            case AbilityActivationKind.Instant:
            case AbilityActivationKind.Toggle:
                if (target.Kind != AbilityTargetKind.Self || target.Id != caster)
                    return AbilityCommandCode.InvalidTarget;
                break;
            case AbilityActivationKind.TargetPoint:
            case AbilityActivationKind.ChannelPoint:
                if (target.Kind != AbilityTargetKind.World ||
                    !world.AbilityCanSeePosition(playerId, target.Position))
                    return AbilityCommandCode.TargetNotVisible;
                break;
            case AbilityActivationKind.TargetUnit:
            case AbilityActivationKind.ChannelUnit:
                if (target.Kind is not (AbilityTargetKind.Unit or
                    AbilityTargetKind.Building))
                    return AbilityCommandCode.InvalidTarget;
                break;
        }

        Vector2 targetPosition;
        if (target.Kind == AbilityTargetKind.Unit)
        {
            if ((ability.Targets & AbilityTargetFlags.Unit) == 0)
                return AbilityCommandCode.InvalidTarget;
            if ((ability.Targets & AbilityTargetFlags.NotSelf) != 0 &&
                target.Id == caster)
                return AbilityCommandCode.InvalidTarget;
            if (!world.AbilityUnitExists(target.Id))
                return AbilityCommandCode.InvalidTarget;
            var alive = world.AbilityUnitAlive(target.Id);
            if (alive && (ability.Targets & AbilityTargetFlags.Dead) != 0 &&
                (ability.Targets & AbilityTargetFlags.Alive) == 0 ||
                !alive && (ability.Targets & AbilityTargetFlags.Dead) == 0)
                return AbilityCommandCode.InvalidTarget;
            if (!world.AbilityCanSeeUnit(playerId, target.Id) && alive)
                return AbilityCommandCode.TargetNotVisible;
            var relation = RelationFilter(
                playerId, caster, target.Id,
                world.AbilityUnitOwner(target.Id), world);
            var relationCode = ValidateRelation(ability.Targets, relation);
            if (relationCode != AbilityCommandCode.Success) return relationCode;
            var attributes = world.AbilityUnitAttributes(target.Id);
            var isAir = world.AbilityUnitIsAir(target.Id);
            var invulnerable = HasStatus(
                target.Id, AbilityStatusFlags.Invulnerable);
            if ((ability.Targets & AbilityTargetFlags.Organic) != 0 &&
                (attributes & CombatAttribute.Biological) == 0 ||
                (ability.Targets & AbilityTargetFlags.Mechanical) != 0 &&
                (attributes & CombatAttribute.Mechanical) == 0 ||
                (ability.Targets & AbilityTargetFlags.Hero) != 0 &&
                !_heroes[target.Id] ||
                (ability.Targets & AbilityTargetFlags.NonHero) != 0 &&
                _heroes[target.Id] ||
                (ability.Targets & AbilityTargetFlags.Ground) != 0 &&
                (ability.Targets & AbilityTargetFlags.Air) == 0 && isAir ||
                (ability.Targets & AbilityTargetFlags.Air) != 0 &&
                (ability.Targets & AbilityTargetFlags.Ground) == 0 && !isAir ||
                (ability.Targets & AbilityTargetFlags.Vulnerable) != 0 &&
                (ability.Targets & AbilityTargetFlags.Invulnerable) == 0 &&
                invulnerable ||
                (ability.Targets & AbilityTargetFlags.Invulnerable) != 0 &&
                (ability.Targets & AbilityTargetFlags.Vulnerable) == 0 &&
                !invulnerable)
                return AbilityCommandCode.InvalidTarget;
            if (HasStatus(target.Id, AbilityStatusFlags.MagicImmune) &&
                relation == AbilityRelationFilter.Enemy)
                return AbilityCommandCode.MagicImmune;
            targetPosition = world.AbilityUnitPosition(target.Id);
        }
        else if (target.Kind == AbilityTargetKind.Building)
        {
            if ((ability.Targets & AbilityTargetFlags.Building) == 0)
                return AbilityCommandCode.InvalidTarget;
            var building = new GameplayBuildingId(target.Id);
            if (!world.AbilityBuildingAlive(building))
                return AbilityCommandCode.InvalidTarget;
            var relation = RelationFilter(
                playerId, caster, -1,
                world.AbilityBuildingOwner(building), world);
            var relationCode = ValidateRelation(ability.Targets, relation);
            if (relationCode != AbilityCommandCode.Success) return relationCode;
            targetPosition = world.AbilityBuildingBounds(building)
                .Clamp(world.AbilityUnitPosition(caster));
        }
        else
        {
            targetPosition = target.Kind == AbilityTargetKind.Self
                ? world.AbilityUnitPosition(caster)
                : target.Position;
        }

        if (level.Range > 0f && target.Kind != AbilityTargetKind.Self)
        {
            var allowance = level.Range + world.AbilityUnitRadius(caster);
            if (Vector2.DistanceSquared(
                    world.AbilityUnitPosition(caster), targetPosition) >
                allowance * allowance)
                return AbilityCommandCode.OutOfRange;
        }
        return AbilityCommandCode.Success;
    }

    private static AbilityCommandCode ValidateRelation(
        AbilityTargetFlags targets,
        AbilityRelationFilter relation)
    {
        var allowed = relation switch
        {
            AbilityRelationFilter.Self =>
                (targets & AbilityTargetFlags.Self) != 0 ||
                (targets & AbilityTargetFlags.Friendly) != 0,
            AbilityRelationFilter.Friendly =>
                (targets & AbilityTargetFlags.Friendly) != 0,
            AbilityRelationFilter.Enemy =>
                (targets & AbilityTargetFlags.Enemy) != 0,
            AbilityRelationFilter.Neutral =>
                (targets & AbilityTargetFlags.Neutral) != 0,
            _ => false
        };
        if (allowed) return AbilityCommandCode.Success;
        if ((targets & (AbilityTargetFlags.Self |
                        AbilityTargetFlags.Friendly)) != 0 &&
            (targets & AbilityTargetFlags.Enemy) == 0)
            return AbilityCommandCode.FriendlyTargetRequired;
        if ((targets & AbilityTargetFlags.Enemy) != 0 &&
            (targets & (AbilityTargetFlags.Self |
                        AbilityTargetFlags.Friendly)) == 0)
            return AbilityCommandCode.EnemyTargetRequired;
        return AbilityCommandCode.InvalidTarget;
    }

    private static AbilityRelationFilter RelationFilter(
        int playerId,
        int caster,
        int targetUnit,
        int targetPlayer,
        IAbilityRuntimeWorld world)
    {
        if (targetUnit == caster) return AbilityRelationFilter.Self;
        return world.AbilityRelation(playerId, targetPlayer) switch
        {
            PlayerEntityRelation.Own or PlayerEntityRelation.Ally =>
                AbilityRelationFilter.Friendly,
            PlayerEntityRelation.Enemy => AbilityRelationFilter.Enemy,
            _ => AbilityRelationFilter.Neutral
        };
    }

    private void ApplyEffects(
        int caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        AbilityEffectTiming timing,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world,
        bool toggle = false)
    {
        foreach (var effect in level.Effects)
        {
            if (effect.Timing != timing) continue;
            ApplyEffect(caster, ability, level, effect, target, world, toggle);
        }
    }

    private void ApplyEffect(
        int caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityEffectProfile effect,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world,
        bool toggle = false)
    {
        if (effect.Kind == AbilityEffectKind.Summon)
        {
            var count = Math.Max(1, effect.MaximumTargets);
            var center = TargetPosition(caster, target, world);
            for (var index = 0; index < count; index++)
            {
                var angle = MathF.Tau * index / count;
                var offset = count == 1
                    ? Vector2.Zero
                    : new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 24f;
                var summoned = world.AbilitySpawnSummon(
                    caster, world.AbilityUnitOwner(caster), center + offset,
                    effect.Summon);
                if (summoned >= 0)
                {
                    _summons.Add(new AbilitySummonRuntimeEntry(
                        summoned, caster, effect.Summon.ObjectId,
                        effect.Summon.LifetimeSeconds));
                }
            }
            return;
        }
        if (effect.Kind == AbilityEffectKind.Reveal)
        {
            var radius = EffectRadius(effect, level);
            _revealSources.Add(new AbilityRevealRuntimeEntry(
                world.AbilityUnitOwner(caster),
                TargetPosition(caster, target, world), radius,
                MathF.Max(
                    0.05f,
                    effect.Duration > 0f
                        ? effect.Duration
                        : level.Duration)));
            return;
        }

        if (effect.Selector == AbilityEffectSelector.Primary &&
            target.Kind == AbilityTargetKind.Building)
        {
            if (effect.Kind == AbilityEffectKind.Damage)
                world.AbilityDamageBuilding(
                    caster, new GameplayBuildingId(target.Id),
                    MathF.Max(0f, effect.Value));
            return;
        }

        foreach (var targetUnit in SelectUnits(
                     caster, ability, level, effect, target, world))
        {
            ApplyEffectToUnit(
                caster, ability, level, effect, target, targetUnit, world, toggle);
        }
    }

    private void ApplyEffectToUnit(
        int caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityEffectProfile effect,
        in AbilityCastTarget castTarget,
        int target,
        IAbilityRuntimeWorld world,
        bool toggle)
    {
        var relation = RelationFilter(
            world.AbilityUnitOwner(caster), caster, target,
            world.AbilityUnitOwner(target), world);
        if ((effect.Relations & relation) == 0) return;
        if (relation == AbilityRelationFilter.Enemy &&
            HasStatus(target, AbilityStatusFlags.Invulnerable))
            return;
        if (relation == AbilityRelationFilter.Enemy &&
            HasStatus(target, AbilityStatusFlags.MagicImmune) &&
            (effect.Timing != AbilityEffectTiming.AttackHit ||
             effect.Kind == AbilityEffectKind.Mana))
            return;
        switch (effect.Kind)
        {
            case AbilityEffectKind.Heal:
                world.AbilityHealUnit(target, MathF.Max(0f, effect.Value));
                break;
            case AbilityEffectKind.Damage:
                if (!HasStatus(target, AbilityStatusFlags.Invulnerable |
                                       AbilityStatusFlags.MagicImmune))
                    world.AbilityDamageUnit(caster, target, MathF.Max(0f, effect.Value));
                break;
            case AbilityEffectKind.Mana:
            {
                var amount = effect.Value;
                if (amount < 0f)
                {
                    var removed = MathF.Min(_mana[target], -amount);
                    _mana[target] -= removed;
                    if (effect.SecondaryValue > 0f)
                        world.AbilityDamageUnit(
                            caster, target, removed * effect.SecondaryValue);
                }
                else
                {
                    _mana[target] = MathF.Min(
                        _maximumMana[target], _mana[target] + amount);
                }
                break;
            }
            case AbilityEffectKind.ApplyStatus:
                if (relation == AbilityRelationFilter.Enemy &&
                    HasStatus(target, AbilityStatusFlags.Invulnerable |
                                      AbilityStatusFlags.MagicImmune))
                    break;
                if (effect.Value > 0f &&
                    !HasStatus(target, AbilityStatusFlags.Invulnerable |
                                       AbilityStatusFlags.MagicImmune))
                    world.AbilityDamageUnit(caster, target, effect.Value);
                AddOrRefreshBuff(
                    caster, ability.Id, target,
                    toggle
                        ? float.PositiveInfinity
                        : EffectDuration(effect, level, _heroes[target]),
                    relation is AbilityRelationFilter.Self or
                        AbilityRelationFilter.Friendly,
                    effect.Status, effect.Modifier);
                break;
            case AbilityEffectKind.ToggleStatus:
                AddOrRefreshBuff(
                    caster, ability.Id, target, float.PositiveInfinity,
                    true, effect.Status, effect.Modifier);
                break;
            case AbilityEffectKind.Dispel:
                _buffs.RemoveAll(value =>
                    value.TargetUnit == target &&
                    (relation == AbilityRelationFilter.Enemy
                        ? value.Beneficial
                        : !value.Beneficial));
                break;
            case AbilityEffectKind.TransferBuff:
                TransferOneBuff(caster, target, relation);
                break;
            case AbilityEffectKind.TransferControl:
                world.AbilitySetUnitOwner(
                    target, world.AbilityUnitOwner(caster));
                break;
            case AbilityEffectKind.Teleport:
                world.AbilityTeleportUnit(
                    target, TargetPosition(caster, castTarget, world));
                break;
            case AbilityEffectKind.Revive:
                if (!world.AbilityUnitAlive(target))
                    world.AbilityReviveUnit(
                        target, effect.Value > 0f ? effect.Value : 1f);
                break;
        }
    }

    private IEnumerable<int> SelectUnits(
        int caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityEffectProfile effect,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world)
    {
        if (effect.Selector == AbilityEffectSelector.Caster)
            return [caster];
        if (effect.Selector == AbilityEffectSelector.Primary &&
            target.Kind is AbilityTargetKind.Unit or AbilityTargetKind.Self)
            return [target.Id];

        var center = effect.Selector == AbilityEffectSelector.AreaAtCaster
            ? world.AbilityUnitPosition(caster)
            : TargetPosition(caster, target, world);
        var radius = EffectRadius(effect, level);
        var radiusSquared = radius * radius;
        var output = new List<int>();
        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            var needsDead = (ability.Targets & AbilityTargetFlags.Dead) != 0 &&
                            (ability.Targets & AbilityTargetFlags.Alive) == 0;
            if (!world.AbilityUnitExists(unit) ||
                needsDead == world.AbilityUnitAlive(unit) ||
                Vector2.DistanceSquared(
                    center, world.AbilityUnitPosition(unit)) > radiusSquared)
                continue;
            if (effect.Timing == AbilityEffectTiming.AttackHit &&
                effect.Selector == AbilityEffectSelector.AreaAtTarget &&
                target.Kind == AbilityTargetKind.Unit && unit == target.Id)
                continue;
            var relation = RelationFilter(
                world.AbilityUnitOwner(caster), caster, unit,
                world.AbilityUnitOwner(unit), world);
            if ((effect.Relations & relation) == 0) continue;
            var isAir = world.AbilityUnitIsAir(unit);
            if ((ability.Targets & AbilityTargetFlags.Ground) != 0 &&
                (ability.Targets & AbilityTargetFlags.Air) == 0 && isAir ||
                (ability.Targets & AbilityTargetFlags.Air) != 0 &&
                (ability.Targets & AbilityTargetFlags.Ground) == 0 && !isAir)
                continue;
            if (relation == AbilityRelationFilter.Enemy &&
                HasStatus(unit, AbilityStatusFlags.MagicImmune) &&
                effect.Kind is AbilityEffectKind.Damage or
                    AbilityEffectKind.Mana or
                    AbilityEffectKind.ApplyStatus or
                    AbilityEffectKind.TransferControl or
                    AbilityEffectKind.TransferBuff)
                continue;
            output.Add(unit);
            if (effect.MaximumTargets > 0 &&
                output.Count >= effect.MaximumTargets)
                break;
        }
        return output;
    }

    private static Vector2 TargetPosition(
        int caster,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world) => target.Kind switch
    {
        AbilityTargetKind.Unit or AbilityTargetKind.Self =>
            world.AbilityUnitPosition(target.Id),
        AbilityTargetKind.Building =>
            world.AbilityBuildingBounds(new GameplayBuildingId(target.Id))
                .Clamp(world.AbilityUnitPosition(caster)),
        _ => target.Position
    };

    private static float EffectRadius(
        in AbilityEffectProfile effect,
        in AbilityLevelProfile level) =>
        MathF.Max(1f, effect.Radius > 0f ? effect.Radius : level.Area);

    private static float EffectDuration(
        in AbilityEffectProfile effect,
        in AbilityLevelProfile level,
        bool targetHero) =>
        MathF.Max(0.05f,
            effect.Duration > 0f
                ? effect.Duration
                : targetHero && level.HeroDuration > 0f
                    ? level.HeroDuration
                    : level.Duration > 0f
                        ? level.Duration
                        : 0.05f);

    private void AddOrRefreshBuff(
        int source,
        int abilityId,
        int target,
        float duration,
        bool beneficial,
        AbilityStatusFlags status,
        AbilityStatModifier modifier)
    {
        modifier = modifier.Normalized;
        var index = _buffs.FindIndex(value =>
            value.SourceUnit == source && value.AbilityId == abilityId &&
            value.TargetUnit == target);
        if (index >= 0)
        {
            _buffs[index] = _buffs[index] with
            {
                RemainingSeconds = duration,
                Beneficial = beneficial,
                Status = status,
                Modifier = modifier
            };
            return;
        }
        _buffs.Add(new AbilityBuffRuntimeEntry(
            _nextBuffInstanceId++, abilityId, source, target, duration,
            beneficial, status, modifier));
    }

    private void TransferOneBuff(
        int caster,
        int target,
        AbilityRelationFilter relation)
    {
        var index = _buffs.FindIndex(value =>
            value.TargetUnit == target &&
            (relation == AbilityRelationFilter.Enemy
                ? value.Beneficial
                : !value.Beneficial));
        if (index < 0) return;
        var buff = _buffs[index];
        _buffs.RemoveAt(index);
        _buffs.Add(buff with
        {
            InstanceId = _nextBuffInstanceId++,
            SourceUnit = caster,
            TargetUnit = caster,
            Beneficial = true
        });
    }

    private void RebuildDerivedStats(
        float delta,
        IAbilityRuntimeWorld world,
        UnitStore units,
        CombatStore combat)
    {
        _revealSources.RemoveAll(value => value.RemainingSeconds <= 0f);
        for (var index = 0; index < _revealSources.Count; index++)
        {
            var source = _revealSources[index];
            _revealSources[index] = source with
            {
                RemainingSeconds = source.RemainingSeconds - delta
            };
        }

        var modifiers = _modifierScratch;
        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            modifiers[unit] = AbilityStatModifier.Identity;
            _statuses[unit] = AbilityStatusFlags.None;
        }
        foreach (var buff in _buffs)
        {
            if (!world.AbilityUnitAlive(buff.TargetUnit)) continue;
            _statuses[buff.TargetUnit] |= buff.Status;
            modifiers[buff.TargetUnit] = Combine(
                modifiers[buff.TargetUnit], buff.Modifier);
        }
        ApplyPassiveAuras(world, modifiers);

        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            var modifier = modifiers[unit].Normalized;
            units.MaxSpeeds[unit] = MathF.Max(
                0f, _baseMaximumSpeed[unit] * modifier.MovementSpeedMultiplier);
            combat.AttackDamage[unit] = MathF.Max(
                0f, _baseAttackDamage[unit] * modifier.AttackDamageMultiplier +
                    modifier.AttackDamageAdd);
            combat.Armor[unit] = MathF.Max(
                0f, _baseArmor[unit] + modifier.ArmorAdd);
            combat.AttackCooldownDurations[unit] = MathF.Max(
                0.05f,
                _baseAttackCooldown[unit] * modifier.AttackCooldownMultiplier);
            combat.MaximumHealth[unit] = MathF.Max(
                1f, _baseMaximumHealth[unit] + modifier.MaximumHealthAdd);
            combat.Health[unit] = MathF.Min(
                combat.Health[unit], combat.MaximumHealth[unit]);
            combat.DetectionRanges[unit] = MathF.Max(
                0f, _baseDetectionRange[unit] + modifier.DetectionRangeAdd);
            _effectiveManaRegeneration[unit] = MathF.Max(
                0f, _baseManaRegeneration[unit] +
                    modifier.ManaRegenerationAdd);
            if ((_statuses[unit] & AbilityStatusFlags.Invisible) != 0)
            {
                combat.ConcealmentKinds[unit] = UnitConcealmentKind.Cloaked;
                combat.ConcealmentPhases[unit] = UnitConcealmentPhase.Concealed;
                _abilityAppliedInvisibility[unit] = true;
            }
            else if (_abilityAppliedInvisibility[unit])
            {
                combat.ConcealmentKinds[unit] = _baseConcealment[unit];
                combat.ConcealmentPhases[unit] = _baseConcealmentPhase[unit];
                _abilityAppliedInvisibility[unit] = false;
            }
        }
    }

    internal void RefreshDerivedState(
        IAbilityRuntimeWorld world,
        UnitStore units,
        CombatStore combat) =>
        RebuildDerivedStats(0f, world, units, combat);

    private void ApplyPassiveAuras(
        IAbilityRuntimeWorld world,
        AbilityStatModifier[] modifiers)
    {
        for (var caster = 0; caster < world.AbilityUnitCount; caster++)
        {
            if (!world.AbilityUnitAlive(caster)) continue;
            for (var slot = 0; slot < _abilityIds[caster].Length; slot++)
            {
                if (_levels[caster][slot] <= 0) continue;
                var ability = _catalog.Ability(_abilityIds[caster][slot]);
                if (!ability.IsPassive) continue;
                var level = ability.Levels[_levels[caster][slot] - 1];
                if (!RequirementsMet(
                        world.AbilityUnitOwner(caster), level, world))
                    continue;
                foreach (var effect in level.Effects)
                {
                    if (effect.Timing != AbilityEffectTiming.Aura) continue;
                    var targets = SelectUnits(
                        caster, ability, level, effect,
                        AbilityCastTarget.Self(
                            caster, world.AbilityUnitPosition(caster)), world);
                    foreach (var target in targets)
                    {
                        _statuses[target] |= effect.Status;
                        modifiers[target] = Combine(
                            modifiers[target], effect.Modifier.Normalized);
                    }
                }
            }
        }
    }

    private static bool RequirementsMet(
        int playerId,
        in AbilityLevelProfile level,
        IAbilityRuntimeWorld world)
    {
        if (level.Requirements.IsDefaultOrEmpty) return true;
        foreach (var requirement in level.Requirements)
        {
            var current = requirement.Kind switch
            {
                AbilityRequirementKind.CompletedBuilding =>
                    world.AbilityCompletedBuildingCount(
                        playerId, requirement.TargetId),
                AbilityRequirementKind.TechnologyLevel =>
                    world.AbilityTechnologyLevel(playerId, requirement.TargetId),
                _ => 0
            };
            if (current < requirement.Value) return false;
        }
        return true;
    }

    private static AbilityStatModifier Combine(
        AbilityStatModifier left,
        AbilityStatModifier right)
    {
        left = left.Normalized;
        right = right.Normalized;
        return new AbilityStatModifier(
            left.MovementSpeedMultiplier * right.MovementSpeedMultiplier,
            left.AttackCooldownMultiplier * right.AttackCooldownMultiplier,
            left.AttackDamageMultiplier * right.AttackDamageMultiplier,
            left.AttackDamageAdd + right.AttackDamageAdd,
            left.ArmorAdd + right.ArmorAdd,
            left.MaximumHealthAdd + right.MaximumHealthAdd,
            left.ManaRegenerationAdd + right.ManaRegenerationAdd,
            left.DetectionRangeAdd + right.DetectionRangeAdd);
    }

    private static bool DeterministicRoll(
        ulong sequence,
        int caster,
        int ability,
        int effect,
        float percent)
    {
        if (percent >= 100f) return true;
        if (percent <= 0f) return false;
        var value = sequence ^ ((ulong)(uint)caster << 32) ^
                    (uint)(ability * 0x45d9f3b) ^
                    (uint)(effect * 0x119de1f3);
        value ^= value >> 33;
        value *= 0xff51afd7ed558ccdUL;
        value ^= value >> 33;
        return value % 10_000UL < (ulong)MathF.Round(percent * 100f);
    }

    internal AbilityRuntimeSnapshot CaptureRuntimeState(int unitCount)
    {
        var units = new AbilityUnitRuntimeEntry[unitCount];
        for (var unit = 0; unit < unitCount; unit++)
            units[unit] = new AbilityUnitRuntimeEntry(
                unit, _unitTypeIds[unit], _heroes[unit],
                _heroLevels[unit], _unspentSkillPoints[unit], _mana[unit],
                _maximumMana[unit], _baseManaRegeneration[unit],
                _effectiveManaRegeneration[unit],
                _baseMaximumSpeed[unit], _baseAttackDamage[unit],
                _baseArmor[unit], _baseAttackCooldown[unit],
                _baseMaximumHealth[unit], _baseDetectionRange[unit],
                _baseConcealment[unit], _baseConcealmentPhase[unit],
                _abilityIds[unit].ToArray(), _levels[unit].ToArray(),
                _cooldowns[unit].ToArray(), _toggles[unit].ToArray(),
                _autoCast[unit].ToArray(), _castPhases[unit],
                _activeSlots[unit], _castRemaining[unit],
                _channelRemaining[unit], _pulseRemaining[unit],
                _targets[unit]);
        return new AbilityRuntimeSnapshot(
            _catalog, _nextBuffInstanceId, units,
            _buffs.OrderBy(value => value.InstanceId).ToArray(),
            _summons.OrderBy(value => value.Unit).ToArray(),
            _revealSources.OrderBy(value => value.PlayerId)
                .ThenBy(value => value.Position.X)
                .ThenBy(value => value.Position.Y).ToArray());
    }

    internal void RestoreRuntimeState(
        AbilityRuntimeSnapshot snapshot,
        int unitCount)
    {
        if (snapshot.Units.Length != unitCount || unitCount > _capacity)
            throw new InvalidOperationException("Ability runtime unit mismatch.");
        _catalog = snapshot.Catalog;
        _nextBuffInstanceId = snapshot.NextBuffInstanceId;
        _buffs.Clear();
        _buffs.AddRange(snapshot.Buffs);
        _summons.Clear();
        _summons.AddRange(snapshot.Summons);
        _revealSources.Clear();
        _revealSources.AddRange(snapshot.Reveals);
        Array.Clear(_abilityAppliedInvisibility);
        for (var unit = 0; unit < unitCount; unit++)
        {
            var value = snapshot.Units[unit];
            if (value.Unit != unit ||
                value.AbilityIds.Length != value.Levels.Length ||
                value.AbilityIds.Length != value.Cooldowns.Length ||
                value.AbilityIds.Length != value.Toggles.Length ||
                value.AbilityIds.Length != value.AutoCast.Length)
                throw new InvalidOperationException("Invalid ability runtime entry.");
            _unitTypeIds[unit] = value.UnitTypeId;
            _heroes[unit] = value.Hero;
            _heroLevels[unit] = value.HeroLevel;
            _unspentSkillPoints[unit] = value.UnspentSkillPoints;
            _mana[unit] = value.Mana;
            _maximumMana[unit] = value.MaximumMana;
            _baseManaRegeneration[unit] = value.BaseManaRegeneration;
            _effectiveManaRegeneration[unit] = value.EffectiveManaRegeneration;
            _baseMaximumSpeed[unit] = value.BaseMaximumSpeed;
            _baseAttackDamage[unit] = value.BaseAttackDamage;
            _baseArmor[unit] = value.BaseArmor;
            _baseAttackCooldown[unit] = value.BaseAttackCooldown;
            _baseMaximumHealth[unit] = value.BaseMaximumHealth;
            _baseDetectionRange[unit] = value.BaseDetectionRange;
            _baseConcealment[unit] = value.BaseConcealment;
            _baseConcealmentPhase[unit] = value.BaseConcealmentPhase;
            _abilityIds[unit] = value.AbilityIds.ToArray();
            _levels[unit] = value.Levels.ToArray();
            _cooldowns[unit] = value.Cooldowns.ToArray();
            _toggles[unit] = value.Toggles.ToArray();
            _autoCast[unit] = value.AutoCast.ToArray();
            _castPhases[unit] = value.CastPhase;
            _activeSlots[unit] = value.ActiveSlot;
            _castRemaining[unit] = value.CastRemaining;
            _channelRemaining[unit] = value.ChannelRemaining;
            _pulseRemaining[unit] = value.PulseRemaining;
            _targets[unit] = value.Target;
        }
        _combatEventCursor = 0;
    }

    internal void AppendStateHash(ref StableHash64 hash, int unitCount)
    {
        hash.Add((long)_catalog.StableHash);
        hash.Add(_nextBuffInstanceId);
        hash.Add(unitCount);
        for (var unit = 0; unit < unitCount; unit++)
        {
            hash.Add(_unitTypeIds[unit]);
            hash.Add(_heroes[unit]);
            hash.Add(_heroLevels[unit]);
            hash.Add(_unspentSkillPoints[unit]);
            hash.Add(_mana[unit]);
            hash.Add(_maximumMana[unit]);
            hash.Add(_baseManaRegeneration[unit]);
            hash.Add(_effectiveManaRegeneration[unit]);
            hash.Add(_baseMaximumSpeed[unit]);
            hash.Add(_baseAttackDamage[unit]);
            hash.Add(_baseArmor[unit]);
            hash.Add(_baseAttackCooldown[unit]);
            hash.Add(_baseMaximumHealth[unit]);
            hash.Add(_baseDetectionRange[unit]);
            hash.Add((byte)_baseConcealment[unit]);
            hash.Add((byte)_baseConcealmentPhase[unit]);
            hash.Add(_abilityIds[unit].Length);
            for (var slot = 0; slot < _abilityIds[unit].Length; slot++)
            {
                hash.Add(_abilityIds[unit][slot]);
                hash.Add(_levels[unit][slot]);
                hash.Add(_cooldowns[unit][slot]);
                hash.Add(_toggles[unit][slot]);
                hash.Add(_autoCast[unit][slot]);
            }
            hash.Add((byte)_castPhases[unit]);
            hash.Add(_activeSlots[unit]);
            hash.Add(_castRemaining[unit]);
            hash.Add(_channelRemaining[unit]);
            hash.Add(_pulseRemaining[unit]);
            hash.Add((byte)_targets[unit].Kind);
            hash.Add(_targets[unit].Id);
            hash.Add(_targets[unit].Position);
        }
        hash.Add(_buffs.Count);
        foreach (var buff in _buffs.OrderBy(value => value.InstanceId))
        {
            hash.Add(buff.InstanceId);
            hash.Add(buff.AbilityId);
            hash.Add(buff.SourceUnit);
            hash.Add(buff.TargetUnit);
            hash.Add(buff.RemainingSeconds);
            hash.Add(buff.Beneficial);
            hash.Add((int)buff.Status);
            AppendModifier(ref hash, buff.Modifier);
        }
        hash.Add(_summons.Count);
        foreach (var summon in _summons.OrderBy(value => value.Unit))
        {
            hash.Add(summon.Unit);
            hash.Add(summon.SourceUnit);
            hash.Add(StableStringHash(summon.ObjectId));
            hash.Add(summon.RemainingSeconds);
        }
        hash.Add(_revealSources.Count);
        foreach (var reveal in _revealSources
                     .OrderBy(value => value.PlayerId)
                     .ThenBy(value => value.Position.X)
                     .ThenBy(value => value.Position.Y))
        {
            hash.Add(reveal.PlayerId);
            hash.Add(reveal.Position);
            hash.Add(reveal.Radius);
            hash.Add(reveal.RemainingSeconds);
        }
    }

    private static void AppendModifier(
        ref StableHash64 hash,
        AbilityStatModifier modifier)
    {
        modifier = modifier.Normalized;
        hash.Add(modifier.MovementSpeedMultiplier);
        hash.Add(modifier.AttackCooldownMultiplier);
        hash.Add(modifier.AttackDamageMultiplier);
        hash.Add(modifier.AttackDamageAdd);
        hash.Add(modifier.ArmorAdd);
        hash.Add(modifier.MaximumHealthAdd);
        hash.Add(modifier.ManaRegenerationAdd);
        hash.Add(modifier.DetectionRangeAdd);
    }

    private static long StableStringHash(string value)
    {
        var hash = new StableHash64();
        foreach (var character in value)
        {
            hash.Add((byte)character);
            hash.Add((byte)(character >> 8));
        }
        return unchecked((long)hash.Value);
    }

    private void ValidateUnitSlot(int unit)
    {
        if ((uint)unit >= (uint)_capacity)
            throw new ArgumentOutOfRangeException(nameof(unit));
    }

}

internal static partial class AbilitySerialization
{
    public static void WriteRuntime(
        BinaryWriter writer,
        AbilityRuntimeSnapshot snapshot)
    {
        WriteCatalog(writer, snapshot.Catalog);
        writer.Write(snapshot.NextBuffInstanceId);
        writer.Write(snapshot.Units.Length);
        foreach (var unit in snapshot.Units) WriteRuntimeUnit(writer, unit);
        writer.Write(snapshot.Buffs.Length);
        foreach (var buff in snapshot.Buffs)
        {
            writer.Write(buff.InstanceId);
            writer.Write(buff.AbilityId);
            writer.Write(buff.SourceUnit);
            writer.Write(buff.TargetUnit);
            writer.Write(buff.RemainingSeconds);
            writer.Write(buff.Beneficial);
            writer.Write((ushort)buff.Status);
            WriteRuntimeModifier(writer, buff.Modifier);
        }
        writer.Write(snapshot.Summons.Length);
        foreach (var summon in snapshot.Summons)
        {
            writer.Write(summon.Unit);
            writer.Write(summon.SourceUnit);
            WriteRuntimeString(writer, summon.ObjectId);
            writer.Write(summon.RemainingSeconds);
        }
        writer.Write(snapshot.Reveals.Length);
        foreach (var reveal in snapshot.Reveals)
        {
            writer.Write(reveal.PlayerId);
            writer.Write(reveal.Position.X);
            writer.Write(reveal.Position.Y);
            writer.Write(reveal.Radius);
            writer.Write(reveal.RemainingSeconds);
        }
    }

    public static AbilityRuntimeSnapshot ReadRuntime(
        BinaryReader reader,
        int unitCount)
    {
        var catalog = ReadCatalog(reader);
        var nextBuff = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (nextBuff <= 0 || count != unitCount)
            throw new InvalidDataException("Invalid ability runtime header.");
        var units = new AbilityUnitRuntimeEntry[count];
        for (var index = 0; index < count; index++)
            units[index] = ReadRuntimeUnit(reader, catalog, index);
        var buffCount = reader.ReadInt32();
        if (buffCount is < 0 or > 100_000) throw new InvalidDataException();
        var buffs = new AbilityBuffRuntimeEntry[buffCount];
        var previousBuff = 0;
        for (var index = 0; index < buffCount; index++)
        {
            var buff = new AbilityBuffRuntimeEntry(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadSingle(), reader.ReadBoolean(),
                (AbilityStatusFlags)reader.ReadUInt16(),
                ReadRuntimeModifier(reader));
            if (buff.InstanceId <= previousBuff ||
                (uint)buff.AbilityId >= (uint)catalog.Count ||
                (uint)buff.SourceUnit >= (uint)unitCount ||
                (uint)buff.TargetUnit >= (uint)unitCount ||
                (!float.IsFinite(buff.RemainingSeconds) &&
                 !float.IsPositiveInfinity(buff.RemainingSeconds)) ||
                buff.RemainingSeconds <= 0f)
                throw new InvalidDataException("Invalid ability buff.");
            buffs[index] = buff;
            previousBuff = buff.InstanceId;
        }
        var summonCount = reader.ReadInt32();
        if (summonCount < 0 || summonCount > unitCount)
            throw new InvalidDataException();
        var summons = new AbilitySummonRuntimeEntry[summonCount];
        var summonUnits = new HashSet<int>();
        for (var index = 0; index < summonCount; index++)
        {
            var summon = new AbilitySummonRuntimeEntry(
                reader.ReadInt32(), reader.ReadInt32(),
                ReadRuntimeString(reader), reader.ReadSingle());
            if ((uint)summon.Unit >= (uint)unitCount ||
                (uint)summon.SourceUnit >= (uint)unitCount ||
                !summonUnits.Add(summon.Unit) ||
                string.IsNullOrWhiteSpace(summon.ObjectId) ||
                !float.IsFinite(summon.RemainingSeconds) ||
                summon.RemainingSeconds <= 0f)
                throw new InvalidDataException("Invalid ability summon.");
            summons[index] = summon;
        }
        var revealCount = reader.ReadInt32();
        if (revealCount is < 0 or > 100_000) throw new InvalidDataException();
        var reveals = new AbilityRevealRuntimeEntry[revealCount];
        for (var index = 0; index < revealCount; index++)
        {
            var reveal = new AbilityRevealRuntimeEntry(
                reader.ReadInt32(),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                reader.ReadSingle(), reader.ReadSingle());
            if (reveal.PlayerId <= 0 ||
                !float.IsFinite(reveal.Position.X) ||
                !float.IsFinite(reveal.Position.Y) ||
                !float.IsFinite(reveal.Radius) || reveal.Radius <= 0f ||
                !float.IsFinite(reveal.RemainingSeconds) ||
                reveal.RemainingSeconds <= 0f)
                throw new InvalidDataException("Invalid ability reveal source.");
            reveals[index] = reveal;
        }
        return new AbilityRuntimeSnapshot(
            catalog, nextBuff, units, buffs, summons, reveals);
    }

    private static void WriteRuntimeUnit(
        BinaryWriter writer,
        AbilityUnitRuntimeEntry value)
    {
        writer.Write(value.Unit);
        writer.Write(value.UnitTypeId);
        writer.Write(value.Hero);
        writer.Write(value.HeroLevel);
        writer.Write(value.UnspentSkillPoints);
        writer.Write(value.Mana);
        writer.Write(value.MaximumMana);
        writer.Write(value.BaseManaRegeneration);
        writer.Write(value.EffectiveManaRegeneration);
        writer.Write(value.BaseMaximumSpeed);
        writer.Write(value.BaseAttackDamage);
        writer.Write(value.BaseArmor);
        writer.Write(value.BaseAttackCooldown);
        writer.Write(value.BaseMaximumHealth);
        writer.Write(value.BaseDetectionRange);
        writer.Write((byte)value.BaseConcealment);
        writer.Write((byte)value.BaseConcealmentPhase);
        writer.Write(value.AbilityIds.Length);
        for (var slot = 0; slot < value.AbilityIds.Length; slot++)
        {
            writer.Write(value.AbilityIds[slot]);
            writer.Write(value.Levels[slot]);
            writer.Write(value.Cooldowns[slot]);
            writer.Write(value.Toggles[slot]);
            writer.Write(value.AutoCast[slot]);
        }
        writer.Write((byte)value.CastPhase);
        writer.Write(value.ActiveSlot);
        writer.Write(value.CastRemaining);
        writer.Write(value.ChannelRemaining);
        writer.Write(value.PulseRemaining);
        writer.Write((byte)value.Target.Kind);
        writer.Write(value.Target.Id);
        writer.Write(value.Target.Position.X);
        writer.Write(value.Target.Position.Y);
    }

    private static AbilityUnitRuntimeEntry ReadRuntimeUnit(
        BinaryReader reader,
        AbilityCatalogSnapshot catalog,
        int expectedUnit)
    {
        var unit = reader.ReadInt32();
        var unitType = reader.ReadInt32();
        var hero = reader.ReadBoolean();
        var heroLevel = reader.ReadInt32();
        var unspentSkillPoints = reader.ReadInt32();
        var mana = reader.ReadSingle();
        var maximumMana = reader.ReadSingle();
        var regeneration = reader.ReadSingle();
        var effectiveRegeneration = reader.ReadSingle();
        var speed = reader.ReadSingle();
        var damage = reader.ReadSingle();
        var armor = reader.ReadSingle();
        var attackCooldown = reader.ReadSingle();
        var maximumHealth = reader.ReadSingle();
        var detection = reader.ReadSingle();
        var concealment = (UnitConcealmentKind)reader.ReadByte();
        var concealmentPhase = (UnitConcealmentPhase)reader.ReadByte();
        var slotCount = reader.ReadInt32();
        if (unit != expectedUnit || slotCount is < 0 or > 32 ||
            hero && (heroLevel <= 0 || unspentSkillPoints < 0) ||
            !hero && (heroLevel != 0 || unspentSkillPoints != 0) ||
            !float.IsFinite(mana) || !float.IsFinite(maximumMana) ||
            mana < 0f || mana > maximumMana || maximumMana < 0f)
            throw new InvalidDataException("Invalid ability unit.");
        var ids = new int[slotCount];
        var levels = new int[slotCount];
        var cooldowns = new float[slotCount];
        var toggles = new bool[slotCount];
        var autoCast = new bool[slotCount];
        var seen = new HashSet<int>();
        for (var slot = 0; slot < slotCount; slot++)
        {
            ids[slot] = reader.ReadInt32();
            levels[slot] = reader.ReadInt32();
            cooldowns[slot] = reader.ReadSingle();
            toggles[slot] = reader.ReadBoolean();
            autoCast[slot] = reader.ReadBoolean();
            if ((uint)ids[slot] >= (uint)catalog.Count ||
                !seen.Add(ids[slot]) || levels[slot] < 0 ||
                levels[slot] > catalog.Ability(ids[slot]).Levels.Length ||
                levels[slot] == 0 &&
                (!hero || !catalog.Ability(ids[slot]).HeroAbility) ||
                !float.IsFinite(cooldowns[slot]) || cooldowns[slot] < 0f)
                throw new InvalidDataException("Invalid ability slot.");
        }
        var castPhase = (AbilityCastPhase)reader.ReadByte();
        var activeSlot = reader.ReadInt32();
        var castRemaining = reader.ReadSingle();
        var channelRemaining = reader.ReadSingle();
        var pulseRemaining = reader.ReadSingle();
        var target = new AbilityCastTarget(
            (AbilityTargetKind)reader.ReadByte(), reader.ReadInt32(),
            new Vector2(reader.ReadSingle(), reader.ReadSingle()));
        if (!Enum.IsDefined(castPhase) ||
            castPhase == AbilityCastPhase.None && activeSlot != -1 ||
            castPhase != AbilityCastPhase.None &&
                (uint)activeSlot >= (uint)slotCount ||
            !Enum.IsDefined(target.Kind))
            throw new InvalidDataException("Invalid active ability cast.");
        return new AbilityUnitRuntimeEntry(
            unit, unitType, hero, heroLevel, unspentSkillPoints,
            mana, maximumMana, regeneration,
            effectiveRegeneration, speed,
            damage, armor, attackCooldown, maximumHealth, detection,
            concealment, concealmentPhase, ids, levels, cooldowns, toggles,
            autoCast, castPhase, activeSlot, castRemaining,
            channelRemaining, pulseRemaining, target);
    }

    private static void WriteRuntimeModifier(
        BinaryWriter writer,
        AbilityStatModifier value)
    {
        value = value.Normalized;
        writer.Write(value.MovementSpeedMultiplier);
        writer.Write(value.AttackCooldownMultiplier);
        writer.Write(value.AttackDamageMultiplier);
        writer.Write(value.AttackDamageAdd);
        writer.Write(value.ArmorAdd);
        writer.Write(value.MaximumHealthAdd);
        writer.Write(value.ManaRegenerationAdd);
        writer.Write(value.DetectionRangeAdd);
    }

    private static AbilityStatModifier ReadRuntimeModifier(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());

    private static void WriteRuntimeString(BinaryWriter writer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadRuntimeString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 1 or > 1024) throw new InvalidDataException();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
