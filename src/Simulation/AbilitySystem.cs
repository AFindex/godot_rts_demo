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
    int AbilityId = -1,
    int CasterBuilding = -1)
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
    AbilityUnitTraits Traits,
    int HeroLevel,
    int HeroMaximumLevel,
    int HeroExperience,
    int ExperienceForNextLevel,
    int UnspentSkillPoints,
    float Mana,
    float MaximumMana,
    float ManaRegeneration,
    AbilityCastPhase CastPhase,
    int ActiveAbilityId,
    float CastRemaining,
    AbilityStatusFlags Statuses,
    UnitAbilitySlotSnapshot[] Abilities);

/// <summary>
/// Allocation-free rendering view. Full slot snapshots remain available to
/// HUD and inspection callers through Observe.
/// </summary>
public readonly record struct UnitAbilityPresentationSnapshot(
    int UnitTypeId,
    AbilityCastPhase CastPhase,
    int ActiveAbilityId);

public readonly record struct UnitAbilitySlotSnapshot(
    int AbilityId,
    int Level,
    float CooldownRemaining,
    bool Toggled,
    bool AutoCastEnabled);

public readonly record struct BuildingAbilitySlotSnapshot(
    int AbilityId,
    float CooldownRemaining,
    bool Toggled);

public readonly record struct BuildingAbilitySnapshot(
    int Building,
    int BuildingTypeId,
    float Mana,
    float MaximumMana,
    float ManaRegeneration,
    BuildingAbilitySlotSnapshot[] Abilities);

public readonly record struct AbilityBuffSnapshot(
    int InstanceId,
    int AbilityId,
    int SourceUnit,
    int TargetUnit,
    string BuffId,
    float RemainingSeconds,
    bool Beneficial,
    AbilityBuffPolarity Polarity,
    AbilityBuffDispelKind DispelKind,
    AbilityBuffStackingKind Stacking,
    AbilityStatusFlags Status,
    AbilityStatModifier Modifier,
    AbilityCombatModifier CombatModifier);

public readonly record struct AbilitySummonedUnitSnapshot(
    int Unit,
    int SourceUnit,
    string ObjectId,
    float RemainingSeconds);

internal interface IAbilityRuntimeWorld
{
    int AbilityUnitCount { get; }
    int AbilityBuildingCount => 0;
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
    bool AbilityBuildingCompleted(GameplayBuildingId building);
    int AbilityBuildingOwner(GameplayBuildingId building);
    SimRect AbilityBuildingBounds(GameplayBuildingId building);
    float AbilityBuildingHealth(GameplayBuildingId building);
    float AbilityBuildingMaximumHealth(GameplayBuildingId building);
    bool AbilityCanSeePosition(int playerId, Vector2 position);
    bool AbilityDamageUnit(
        int sourceUnit,
        int targetUnit,
        float damage,
        AbilityDamageKind damageKind);
    bool AbilityHealUnit(int targetUnit, float amount);
    bool AbilityDamageBuilding(
        int sourceUnit,
        GameplayBuildingId building,
        float damage,
        AbilityDamageKind damageKind);
    bool AbilityRepairBuilding(
        int sourceUnit,
        GameplayBuildingId building,
        float repairCostRatio,
        float repairTimeRatio,
        float powerBuildCostRatio,
        float powerBuildRate,
        float workSeconds,
        float maximumRange);
    bool AbilityRepairUnit(
        int sourceUnit,
        int targetUnit,
        float repairCostRatio,
        float repairTimeRatio,
        float workSeconds,
        float maximumRange);
    bool AbilityReviveUnit(int unit, float healthFraction);
    void AbilitySetUnitOwner(int unit, int playerId);
    void AbilityTeleportUnit(int unit, Vector2 position);
    void AbilityTeleportUnits(int[] units, Vector2 position)
    {
        foreach (var unit in units) AbilityTeleportUnit(unit, position);
    }
    int AbilitySpawnSummon(
        int sourceUnit,
        int playerId,
        Vector2 position,
        in AbilitySummonProfile summon);
    bool AbilityTryFindNearestOwnedBuilding(
        int unit,
        BuildingFunctionKind function,
        out GameplayBuildingId building);
    void AbilityMoveUnitToBuilding(int unit, GameplayBuildingId building);
    bool AbilityUnitTouchesBuilding(int unit, GameplayBuildingId building);
    bool AbilityUnitMovingToBuilding(int unit, GameplayBuildingId building);
    bool AbilityApplyUnitProfile(int unit, in UnitTypeProfile profile);
    void AbilityPrepareCaster(int unit);
    void AbilityKillSummon(int unit);
}

internal readonly record struct AbilityUnitRuntimeEntry(
    int Unit,
    int UnitTypeId,
    bool Hero,
    AbilityUnitTraits Traits,
    int HeroLevel,
    int HeroMaximumLevel,
    int HeroExperience,
    int ExperienceBounty,
    int UnitLevel,
    int UnspentSkillPoints,
    float Mana,
    float MaximumMana,
    float BaseMaximumMana,
    float ManaBonusDecayPerSecond,
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
    int PulsesRemaining,
    AbilityCastTarget Target);

internal readonly record struct AbilityBuffRuntimeEntry(
    int InstanceId,
    int AbilityId,
    int SourceUnit,
    int TargetUnit,
    string BuffId,
    float RemainingSeconds,
    bool Beneficial,
    AbilityBuffPolarity Polarity,
    AbilityBuffDispelKind DispelKind,
    AbilityBuffStackingKind Stacking,
    AbilityStatusFlags Status,
    AbilityStatModifier Modifier,
    AbilityCombatModifier CombatModifier);

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

internal readonly record struct AbilityPersistentEffectRuntimeEntry(
    int InstanceId,
    int AbilityId,
    int SourceUnit,
    int Level,
    int EffectIndex,
    AbilityCastTarget Target,
    float RemainingSeconds,
    float StartDelayRemaining,
    float PulseRemaining,
    int PulsesRemaining);

internal enum AbilityUnitFormPhase : byte
{
    ApproachingAlternate,
    Alternate,
    ApproachingNormal
}

internal readonly record struct AbilityUnitFormRuntimeEntry(
    int Unit,
    AbilityUnitFormProfile Profile,
    AbilityUnitFormPhase Phase,
    int TargetBuilding,
    float RemainingSeconds);

internal readonly record struct AbilityBuildingToggleRuntimeEntry(
    int Building,
    int AbilityId);

internal readonly record struct AbilityBuildingStatusRuntimeEntry(
    int Building,
    int AbilityId,
    int SourceUnit,
    float RemainingSeconds,
    AbilityStatusFlags Status);

internal readonly record struct AbilityBuildingRuntimeEntry(
    int Building,
    int BuildingTypeId,
    float Mana,
    float MaximumMana,
    float ManaRegeneration,
    int[] AbilityIds,
    float[] Cooldowns);

public readonly record struct AbilityProjectileSnapshot(
    int Id,
    int AbilityId,
    int CasterUnit,
    int Level,
    AbilityCastTarget Target,
    Vector2 Origin,
    Vector2 Position,
    Vector2 Destination,
    float Speed,
    bool Homing);

internal sealed record AbilityRuntimeSnapshot(
    AbilityCatalogSnapshot Catalog,
    int NextBuffInstanceId,
    int NextPersistentEffectInstanceId,
    int NextProjectileInstanceId,
    AbilityUnitRuntimeEntry[] Units,
    AbilityBuffRuntimeEntry[] Buffs,
    AbilitySummonRuntimeEntry[] Summons,
    AbilityRevealRuntimeEntry[] Reveals,
    AbilityPersistentEffectRuntimeEntry[] PersistentEffects,
    AbilityUnitFormRuntimeEntry[] UnitForms,
    AbilityProjectileSnapshot[] Projectiles,
    AbilityBuildingRuntimeEntry[] Buildings,
    AbilityBuildingToggleRuntimeEntry[] BuildingToggles,
    AbilityBuildingStatusRuntimeEntry[] BuildingStatuses);

/// <summary>
/// Deterministic, content-neutral ability authority. It owns mana, cooldowns,
/// cast/channel state, timed modifiers, passives and summon lifetimes. World
/// mutation is deliberately routed through IAbilityRuntimeWorld.
/// </summary>
public sealed class AbilitySystem
{
    private int _capacity;
    private int[] _unitTypeIds;
    private bool[] _heroes;
    private AbilityUnitTraits[] _traits;
    private int[] _heroLevels;
    private int[] _heroMaximumLevels;
    private int[] _heroExperience;
    private int[] _experienceBounty;
    private int[] _unitLevels;
    private int[] _unspentSkillPoints;
    private float[] _mana;
    private float[] _maximumMana;
    private float[] _baseMaximumMana;
    private float[] _manaBonusDecayPerSecond;
    private float[] _baseManaRegeneration;
    private float[] _effectiveManaRegeneration;
    private float[] _baseMaximumSpeed;
    private float[] _baseAttackDamage;
    private float[] _baseArmor;
    private float[] _baseAttackCooldown;
    private float[] _baseMaximumHealth;
    private float[] _baseDetectionRange;
    private UnitConcealmentKind[] _baseConcealment;
    private UnitConcealmentPhase[] _baseConcealmentPhase;
    private int[][] _abilityIds;
    private int[][] _levels;
    private float[][] _cooldowns;
    private bool[][] _toggles;
    private bool[][] _autoCast;
    private AbilityStatusFlags[] _statuses;
    private string?[] _summonedObjectIds;
    private bool[] _abilityAppliedInvisibility;
    private AbilityCastPhase[] _castPhases;
    private int[] _activeSlots;
    private float[] _castRemaining;
    private float[] _channelRemaining;
    private float[] _pulseRemaining;
    private int[] _pulsesRemaining;
    private AbilityCastTarget[] _targets;
    private AbilityStatModifier[] _modifierScratch;
    private AbilityCombatModifier[] _combatModifierScratch;
    private readonly List<AbilityBuffRuntimeEntry> _buffs = [];
    private readonly List<AbilitySummonRuntimeEntry> _summons = [];
    private readonly List<AbilityPersistentEffectRuntimeEntry>
        _persistentEffects = [];
    private readonly List<AbilityUnitFormRuntimeEntry> _unitForms = [];
    private readonly List<AbilityBuildingToggleRuntimeEntry>
        _buildingToggles = [];
    private readonly List<AbilityBuildingStatusRuntimeEntry>
        _buildingStatuses = [];
    private readonly List<AbilityBuildingRuntimeEntry> _buildings = [];
    private readonly List<AbilityProjectileSnapshot> _projectiles = [];
    private readonly Dictionary<(int Target, string BuffId), AbilityEffectProfile>
        _resolvedAuraEffects = [];
    private readonly List<(int Target, string BuffId)> _resolvedAuraOrder = [];
    private CombatEvent[] _combatEventScratch = [];
    private AbilityCatalogSnapshot _catalog = AbilityCatalogSnapshot.Empty;
    private int _nextBuffInstanceId = 1;
    private int _nextPersistentEffectInstanceId = 1;
    private int _nextProjectileId = 1;
    private ulong _combatEventCursor;

    public AbilitySystem(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _unitTypeIds = new int[capacity];
        _heroes = new bool[capacity];
        _traits = new AbilityUnitTraits[capacity];
        _heroLevels = new int[capacity];
        _heroMaximumLevels = new int[capacity];
        _heroExperience = new int[capacity];
        _experienceBounty = new int[capacity];
        _unitLevels = new int[capacity];
        _unspentSkillPoints = new int[capacity];
        _mana = new float[capacity];
        _maximumMana = new float[capacity];
        _baseMaximumMana = new float[capacity];
        _manaBonusDecayPerSecond = new float[capacity];
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
        _summonedObjectIds = new string?[capacity];
        _abilityAppliedInvisibility = new bool[capacity];
        _castPhases = new AbilityCastPhase[capacity];
        _activeSlots = new int[capacity];
        _castRemaining = new float[capacity];
        _channelRemaining = new float[capacity];
        _pulseRemaining = new float[capacity];
        _pulsesRemaining = new int[capacity];
        _targets = new AbilityCastTarget[capacity];
        _modifierScratch = new AbilityStatModifier[capacity];
        _combatModifierScratch = new AbilityCombatModifier[capacity];
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
    public int ActivePersistentEffectCount => _persistentEffects.Count;
    public int ActiveUnitFormCount => _unitForms.Count;
    public int ActiveBuildingToggleCount => _buildingToggles.Count;
    public int ActiveBuildingRuntimeCount => _buildings.Count;
    public int ActiveBuildingStatusCount => _buildingStatuses.Count;

    public bool IsBuildingAttackDisabled(GameplayBuildingId building) =>
        _buildingStatuses.Any(value =>
            value.Building == building.Value &&
            (value.Status & AbilityStatusFlags.AttackDisabled) != 0);

    public bool IsBuildingInvulnerable(
        GameplayBuildingId building,
        int buildingTypeId)
    {
        if (_buildingStatuses.Any(value =>
                value.Building == building.Value &&
                (value.Status & AbilityStatusFlags.Invulnerable) != 0))
            return true;
        if (!_catalog.TryBuildingBinding(buildingTypeId, out var binding))
            return false;
        foreach (var abilityId in binding.Abilities)
        {
            var ability = _catalog.Ability(abilityId);
            if (!ability.IsPassive || ability.Levels.IsDefaultOrEmpty)
                continue;
            if (ability.Levels[0].Effects.Any(effect =>
                    effect.Timing == AbilityEffectTiming.Aura &&
                    (effect.Status & AbilityStatusFlags.Invulnerable) != 0))
                return true;
        }
        return false;
    }
    public int ActiveProjectileCount => _projectiles.Count;

    internal void EnsureCapacity(int capacity)
    {
        if (capacity <= _capacity)
        {
            return;
        }

        var previous = _capacity;
        Array.Resize(ref _unitTypeIds, capacity);
        Array.Resize(ref _heroes, capacity);
        Array.Resize(ref _traits, capacity);
        Array.Resize(ref _heroLevels, capacity);
        Array.Resize(ref _heroMaximumLevels, capacity);
        Array.Resize(ref _heroExperience, capacity);
        Array.Resize(ref _experienceBounty, capacity);
        Array.Resize(ref _unitLevels, capacity);
        Array.Resize(ref _unspentSkillPoints, capacity);
        Array.Resize(ref _mana, capacity);
        Array.Resize(ref _maximumMana, capacity);
        Array.Resize(ref _baseMaximumMana, capacity);
        Array.Resize(ref _manaBonusDecayPerSecond, capacity);
        Array.Resize(ref _baseManaRegeneration, capacity);
        Array.Resize(ref _effectiveManaRegeneration, capacity);
        Array.Resize(ref _baseMaximumSpeed, capacity);
        Array.Resize(ref _baseAttackDamage, capacity);
        Array.Resize(ref _baseArmor, capacity);
        Array.Resize(ref _baseAttackCooldown, capacity);
        Array.Resize(ref _baseMaximumHealth, capacity);
        Array.Resize(ref _baseDetectionRange, capacity);
        Array.Resize(ref _baseConcealment, capacity);
        Array.Resize(ref _baseConcealmentPhase, capacity);
        Array.Resize(ref _abilityIds, capacity);
        Array.Resize(ref _levels, capacity);
        Array.Resize(ref _cooldowns, capacity);
        Array.Resize(ref _toggles, capacity);
        Array.Resize(ref _autoCast, capacity);
        Array.Resize(ref _statuses, capacity);
        Array.Resize(ref _summonedObjectIds, capacity);
        Array.Resize(ref _abilityAppliedInvisibility, capacity);
        Array.Resize(ref _castPhases, capacity);
        Array.Resize(ref _activeSlots, capacity);
        Array.Resize(ref _castRemaining, capacity);
        Array.Resize(ref _channelRemaining, capacity);
        Array.Resize(ref _pulseRemaining, capacity);
        Array.Resize(ref _pulsesRemaining, capacity);
        Array.Resize(ref _targets, capacity);
        Array.Resize(ref _modifierScratch, capacity);
        Array.Resize(ref _combatModifierScratch, capacity);
        Array.Fill(_unitTypeIds, -1, previous, capacity - previous);
        Array.Fill(_activeSlots, -1, previous, capacity - previous);
        for (var unit = previous; unit < capacity; unit++)
        {
            _abilityIds[unit] = [];
            _levels[unit] = [];
            _cooldowns[unit] = [];
            _toggles[unit] = [];
            _autoCast[unit] = [];
        }
        _capacity = capacity;
    }

    internal AbilityUnitTraits UnitTraits(int unit) =>
        (uint)unit < (uint)_traits.Length
            ? _traits[unit]
            : AbilityUnitTraits.None;

    public void ConfigureCatalog(AbilityCatalogSnapshot catalog, int unitCount = 0)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (unitCount != 0 || _buffs.Count != 0 || _summons.Count != 0 ||
            _persistentEffects.Count != 0 || _unitForms.Count != 0 ||
            _buildingToggles.Count != 0 || _buildingStatuses.Count != 0 ||
            _buildings.Count != 0 ||
            _projectiles.Count != 0)
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
        _traits[unit] = AbilityUnitTraits.None;
        _heroLevels[unit] = 0;
        _heroMaximumLevels[unit] = 0;
        _heroExperience[unit] = 0;
        _experienceBounty[unit] = 0;
        _unitLevels[unit] = 1;
        _unspentSkillPoints[unit] = 0;
        _mana[unit] = 0f;
        _maximumMana[unit] = 0f;
        _baseMaximumMana[unit] = 0f;
        _manaBonusDecayPerSecond[unit] = 0f;
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
        _summonedObjectIds[unit] = null;
        _abilityAppliedInvisibility[unit] = false;
        ClearCast(unit);
    }

    internal bool BindUnitType(int unit, int unitTypeId)
    {
        ValidateUnitSlot(unit);
        if (!_catalog.TryBinding(unitTypeId, out var binding)) return false;
        _unitTypeIds[unit] = unitTypeId;
        _heroes[unit] = binding.Hero;
        _traits[unit] = binding.Traits;
        _heroLevels[unit] = binding.Hero ? 1 : 0;
        _heroMaximumLevels[unit] = binding.Hero
            ? binding.HeroMaximumLevel
            : 0;
        _heroExperience[unit] = 0;
        _experienceBounty[unit] = binding.ExperienceBounty;
        _unitLevels[unit] = binding.UnitLevel;
        _unspentSkillPoints[unit] = binding.Hero ? 1 : 0;
        _mana[unit] = binding.Mana.Initial;
        _maximumMana[unit] = binding.Mana.Maximum;
        _baseMaximumMana[unit] = binding.Mana.Maximum;
        _manaBonusDecayPerSecond[unit] = 0f;
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

    private bool RebindUnitForm(
        int unit,
        in UnitTypeProfile profile,
        UnitStore units,
        CombatStore combat)
    {
        if (!BindUnitType(unit, profile.Id)) return false;
        _baseMaximumSpeed[unit] = units.MaxSpeeds[unit];
        _baseAttackDamage[unit] = combat.AttackDamage[unit];
        _baseArmor[unit] = combat.Armor[unit];
        _baseAttackCooldown[unit] = combat.AttackCooldownDurations[unit];
        _baseMaximumHealth[unit] = combat.MaximumHealth[unit];
        _baseDetectionRange[unit] = combat.DetectionRanges[unit];
        _baseConcealment[unit] = combat.ConcealmentKinds[unit];
        _baseConcealmentPhase[unit] = combat.ConcealmentPhases[unit];
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
            unit, _unitTypeIds[unit], _heroes[unit], _traits[unit],
            _heroLevels[unit], _heroMaximumLevels[unit],
            _heroExperience[unit],
            ExperienceRequiredForLevel(_heroLevels[unit] + 1),
            _unspentSkillPoints[unit], _mana[unit],
            _maximumMana[unit], _effectiveManaRegeneration[unit],
            _castPhases[unit],
            _activeSlots[unit] >= 0
                ? _abilityIds[unit][_activeSlots[unit]]
                : -1,
            _castRemaining[unit] + _channelRemaining[unit],
            _statuses[unit], slots);
    }

    public UnitAbilityPresentationSnapshot ObservePresentation(int unit)
    {
        ValidateUnitSlot(unit);
        return new UnitAbilityPresentationSnapshot(
            _unitTypeIds[unit],
            _castPhases[unit],
            _activeSlots[unit] >= 0
                ? _abilityIds[unit][_activeSlots[unit]]
                : -1);
    }

    public int UnitTypeId(int unit)
    {
        ValidateUnitSlot(unit);
        return _unitTypeIds[unit];
    }

    public BuildingAbilitySlotSnapshot[] ObserveBuilding(
        GameplayBuildingId building,
        int buildingTypeId)
    {
        if (!_catalog.TryBuildingBinding(buildingTypeId, out var binding))
            return [];
        var runtimeIndex = FindBuildingRuntime(building);
        var runtime = runtimeIndex >= 0 ? _buildings[runtimeIndex] : default;
        return binding.Abilities.Select(abilityId =>
                new BuildingAbilitySlotSnapshot(
                    abilityId,
                    runtimeIndex >= 0
                        ? BuildingCooldown(runtime, abilityId)
                        : 0f,
                    _buildingToggles.Any(value =>
                        value.Building == building.Value &&
                        value.AbilityId == abilityId)))
            .ToArray();
    }

    public BuildingAbilitySnapshot ObserveBuildingState(
        GameplayBuildingId building,
        int buildingTypeId)
    {
        if (!_catalog.TryBuildingBinding(buildingTypeId, out var binding))
            return new BuildingAbilitySnapshot(
                building.Value, buildingTypeId, 0f, 0f, 0f, []);
        var runtimeIndex = FindBuildingRuntime(building);
        var mana = runtimeIndex >= 0
            ? _buildings[runtimeIndex].Mana
            : binding.Mana.Initial;
        return new BuildingAbilitySnapshot(
            building.Value, buildingTypeId, mana, binding.Mana.Maximum,
            binding.Mana.RegenerationPerSecond,
            ObserveBuilding(building, buildingTypeId));
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

    public AbilityProjectileSnapshot[] ObserveProjectiles() =>
        _projectiles.OrderBy(value => value.Id).ToArray();

    private static AbilityBuffSnapshot ToSnapshot(
        AbilityBuffRuntimeEntry value) => new(
        value.InstanceId, value.AbilityId, value.SourceUnit,
        value.TargetUnit, value.BuffId, value.RemainingSeconds,
        value.Beneficial, value.Polarity, value.DispelKind, value.Stacking,
        value.Status, value.Modifier, value.CombatModifier);

    public AbilityCombatModifier CombatModifier(int unit) =>
        (uint)unit < (uint)_capacity
            ? _combatModifierScratch[unit].Normalized
            : AbilityCombatModifier.Identity;

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
            _heroLevels[unit] >= _heroMaximumLevels[unit])
            return false;
        var next = Math.Min(
            _heroMaximumLevels[unit], _heroLevels[unit] + levels);
        var gained = next - _heroLevels[unit];
        _heroLevels[unit] = next;
        _heroExperience[unit] = Math.Max(
            _heroExperience[unit], ExperienceRequiredForLevel(next));
        _unspentSkillPoints[unit] += gained;
        return true;
    }

    public bool TryGrantHeroExperience(int unit, int amount)
    {
        ValidateUnitSlot(unit);
        if (!_heroes[unit] || amount <= 0 ||
            _heroLevels[unit] >= _heroMaximumLevels[unit])
            return false;
        var maximumExperience = ExperienceRequiredForLevel(
            _heroMaximumLevels[unit]);
        _heroExperience[unit] = (int)Math.Min(
            maximumExperience,
            (long)_heroExperience[unit] + amount);
        while (_heroLevels[unit] < _heroMaximumLevels[unit] &&
               _heroExperience[unit] >= ExperienceRequiredForLevel(
                   _heroLevels[unit] + 1))
        {
            _heroLevels[unit]++;
            _unspentSkillPoints[unit]++;
        }
        return true;
    }

    public static int ExperienceRequiredForLevel(int level)
    {
        if (level <= 1) return 0;
        return checked(50 * (level - 1) * (level + 2));
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
        if ((uint)unit < (uint)_summonedObjectIds.Length &&
            _summonedObjectIds[unit] is { Length: > 0 } value)
        {
            objectId = value;
            return true;
        }
        objectId = string.Empty;
        return false;
    }

    public bool TryReplacementObjectId(
        int unit,
        bool air,
        out string objectId)
    {
        foreach (var buff in _buffs)
        {
            if (buff.TargetUnit != unit ||
                (buff.Status & AbilityStatusFlags.Polymorphed) == 0)
                continue;
            var ability = _catalog.Ability(buff.AbilityId);
            foreach (var level in ability.Levels)
            foreach (var effect in level.Effects)
            {
                if (!effect.Replacement.Enabled) continue;
                objectId = effect.Replacement.Select(air);
                return objectId.Length > 0;
            }
        }
        objectId = string.Empty;
        return false;
    }

    public bool TryRepairTargetProfile(
        int unit,
        out AbilityRepairTargetProfile profile)
    {
        if ((uint)unit < (uint)_capacity &&
            _catalog.TryBinding(_unitTypeIds[unit], out var binding) &&
            binding.Repair.Enabled)
        {
            profile = binding.Repair;
            return true;
        }
        profile = default;
        return false;
    }

    /// <summary>
    /// Restores mana through the authoritative ability store. Item runtimes
    /// use this instead of maintaining a second mana value beside abilities.
    /// </summary>
    public float RestoreMana(int unit, float amount)
    {
        if ((uint)unit >= (uint)_capacity || !float.IsFinite(amount) ||
            amount <= 0f || _maximumMana[unit] <= 0f)
            return 0f;
        var before = _mana[unit];
        _mana[unit] = MathF.Min(_maximumMana[unit], before + amount);
        return _mana[unit] - before;
    }

    public float RemoveMana(int unit, float amount)
    {
        if ((uint)unit >= (uint)_capacity || !float.IsFinite(amount) ||
            amount <= 0f || _maximumMana[unit] <= 0f)
            return 0f;
        var before = _mana[unit];
        _mana[unit] = MathF.Max(0f, before - amount);
        return before - _mana[unit];
    }

    public bool IsHero(int unit) =>
        (uint)unit < (uint)_capacity && _heroes[unit];

    public bool IsSummoned(int unit) =>
        (uint)unit < (uint)_capacity &&
        _summonedObjectIds[unit] is not null;

    public AbilityUnitTraits Traits(int unit) =>
        (uint)unit < (uint)_capacity
            ? _traits[unit]
            : AbilityUnitTraits.None;

    /// <summary>
    /// Registers a unit created by an external data-driven effect so the
    /// presentation layer can resolve its original object id and model.
    /// </summary>
    public void RegisterExternalSummon(
        int unit,
        int sourceUnit,
        string objectId,
        float lifetimeSeconds = float.MaxValue)
    {
        if ((uint)unit >= (uint)_capacity || unit < 0 || sourceUnit < 0 ||
            string.IsNullOrWhiteSpace(objectId) ||
            !float.IsFinite(lifetimeSeconds) || lifetimeSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(unit));
        _summons.RemoveAll(value => value.Unit == unit);
        _summonedObjectIds[unit] = null;
        AddSummon(new AbilitySummonRuntimeEntry(
            unit, sourceUnit, objectId, lifetimeSeconds));
    }

    private void AddSummon(in AbilitySummonRuntimeEntry summon)
    {
        _summons.Add(summon);
        _summonedObjectIds[summon.Unit] ??= summon.ObjectId;
    }

    public bool HasStatus(int unit, AbilityStatusFlags status) =>
        (uint)unit < (uint)_capacity && (_statuses[unit] & status) != 0;

    public bool CanMove(int unit) =>
        !HasStatus(unit, AbilityStatusFlags.Stunned |
                         AbilityStatusFlags.MovementDisabled);

    public bool CanAttack(int unit) =>
        !HasStatus(unit, AbilityStatusFlags.Stunned |
                         AbilityStatusFlags.AttackDisabled |
                         AbilityStatusFlags.Polymorphed |
                         AbilityStatusFlags.Banished);

    public bool CanCast(int unit) =>
        !HasStatus(unit, AbilityStatusFlags.Stunned |
                         AbilityStatusFlags.Polymorphed);

    internal AbilityCommandResult Preview(
        int playerId,
        int caster,
        int abilityId,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world)
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
        if (slot < 0 || _levels[caster][slot] <= 0)
            return new(AbilityCommandCode.AbilityNotLearned, caster, abilityId);
        var ability = _catalog.Ability(abilityId);
        if (ability.IsPassive)
            return new(AbilityCommandCode.PassiveAbility, caster, abilityId);
        var level = ability.Levels[_levels[caster][slot] - 1];
        if (!RequirementsMet(playerId, level, world))
            return new(AbilityCommandCode.RequirementsNotMet, caster, abilityId);
        var targetCode = ValidateTarget(
            playerId, caster, ability, level, target, world);
        if (targetCode != AbilityCommandCode.Success)
            return new(targetCode, caster, abilityId);
        if (!RuntimeEffectTargetValid(level, target))
            return new(AbilityCommandCode.InvalidTarget, caster, abilityId);
        if (_cooldowns[caster][slot] > 0f)
            return new(AbilityCommandCode.Cooldown, caster, abilityId);
        if (_mana[caster] + 0.0001f <
            EffectiveManaCost(level, target, world))
            return new(AbilityCommandCode.InsufficientMana, caster, abilityId);
        return new(AbilityCommandCode.Success, caster, abilityId);
    }

    internal AbilityCommandResult Issue(
        int playerId,
        int caster,
        int abilityId,
        in AbilityCastTarget target,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        var preview = Preview(playerId, caster, abilityId, target, world);
        if (!preview.Succeeded) return preview;
        var slot = Array.IndexOf(_abilityIds[caster], abilityId);
        var ability = _catalog.Ability(abilityId);
        var level = ability.Levels[_levels[caster][slot] - 1];

        CancelCast(caster, tick, AbilityEndReason.Canceled, world, events);
        _mana[caster] = MathF.Max(
            0f, _mana[caster] - EffectiveManaCost(level, target, world));
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

    private bool RuntimeEffectTargetValid(
        in AbilityLevelProfile level,
        in AbilityCastTarget target)
    {
        foreach (var effect in level.Effects)
        {
            if (effect.MaximumTargetUnitLevel > 0 &&
                target.Kind == AbilityTargetKind.Unit &&
                _unitLevels[target.Id] > effect.MaximumTargetUnitLevel)
                return false;
            if (effect.Kind != AbilityEffectKind.TransferControl ||
                effect.SecondaryValue <= 0f)
                continue;
            if (target.Kind != AbilityTargetKind.Unit ||
                !IsSummoned(target.Id))
                return false;
        }
        return true;
    }

    private static float EffectiveManaCost(
        in AbilityLevelProfile level,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world)
    {
        var cost = level.ManaCost;
        if (target.Kind != AbilityTargetKind.Unit) return cost;
        foreach (var effect in level.Effects)
        {
            if (effect.Kind != AbilityEffectKind.TransferControl ||
                effect.SecondaryValue <= 0f)
                continue;
            cost += MathF.Max(0f, world.AbilityUnitHealth(target.Id)) *
                    effect.SecondaryValue *
                    MathF.Max(0f, effect.HeroValue);
        }
        return cost;
    }

    internal AbilityCommandResult PreviewBuilding(
        int playerId,
        GameplayBuildingId caster,
        int buildingTypeId,
        int abilityId,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world)
    {
        if (playerId <= 0) return new(AbilityCommandCode.InvalidPlayer);
        if (!world.AbilityBuildingAlive(caster) ||
            !world.AbilityBuildingCompleted(caster))
            return new(
                AbilityCommandCode.InvalidCaster,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        if (world.AbilityBuildingOwner(caster) != playerId)
            return new(
                AbilityCommandCode.WrongOwner,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        if ((uint)abilityId >= (uint)_catalog.Count)
            return new(
                AbilityCommandCode.UnknownAbility,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        if (!_catalog.TryBuildingBinding(buildingTypeId, out var binding) ||
            !binding.Abilities.Contains(abilityId))
            return new(
                AbilityCommandCode.AbilityNotLearned,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);

        var ability = _catalog.Ability(abilityId);
        if (ability.IsPassive)
            return new(
                AbilityCommandCode.PassiveAbility,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        var level = ability.Levels[0];
        if (!RequirementsMet(playerId, level, world))
            return new(
                AbilityCommandCode.RequirementsNotMet,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        var targetCode = ValidateBuildingTarget(
            caster, ability, level, target, world);
        if (targetCode != AbilityCommandCode.Success)
            return new(
                targetCode,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        if (!CanApplyBuildingAbility(ability, level))
            return new(
                AbilityCommandCode.InvalidTarget,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        var runtimeIndex = FindBuildingRuntime(caster);
        if (runtimeIndex >= 0)
        {
            var runtime = _buildings[runtimeIndex];
            if (BuildingCooldown(runtime, abilityId) > 0f)
                return new(
                    AbilityCommandCode.Cooldown,
                    AbilityId: abilityId,
                    CasterBuilding: caster.Value);
            if (runtime.Mana + 0.0001f < level.ManaCost)
                return new(
                    AbilityCommandCode.InsufficientMana,
                    AbilityId: abilityId,
                    CasterBuilding: caster.Value);
        }
        else if (binding.Mana.Initial + 0.0001f < level.ManaCost)
        {
            return new(
                AbilityCommandCode.InsufficientMana,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        }
        return new(
            AbilityCommandCode.Success,
            AbilityId: abilityId,
            CasterBuilding: caster.Value);
    }

    internal AbilityCommandResult IssueBuilding(
        int playerId,
        GameplayBuildingId caster,
        int buildingTypeId,
        int abilityId,
        in AbilityCastTarget target,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        var preview = PreviewBuilding(
            playerId, caster, buildingTypeId, abilityId, target, world);
        if (!preview.Succeeded) return preview;
        var binding = _catalog.TryBuildingBinding(buildingTypeId, out var value)
            ? value
            : throw new InvalidOperationException(
                "Validated building ability binding disappeared.");
        var ability = _catalog.Ability(abilityId);
        var level = ability.Levels[0];
        var runtimeIndex = EnsureBuildingRuntime(caster, binding);
        var runtime = _buildings[runtimeIndex];
        runtime = runtime with
        {
            Mana = MathF.Max(0f, runtime.Mana - level.ManaCost)
        };
        SetBuildingCooldown(ref runtime, abilityId, level.CooldownSeconds);
        _buildings[runtimeIndex] = runtime;

        var bounds = world.AbilityBuildingBounds(caster);
        var center = (bounds.Min + bounds.Max) * 0.5f;
        events.Publish(
            tick, AbilityEventKind.Started, ability.RawId, -1,
            target.Kind, target.Id, target.Position,
            casterBuilding: caster.Value);
        if (ability.Activation == AbilityActivationKind.Toggle)
            return IssueBuildingToggle(
                playerId, caster, ability, level, center, tick, world, events);

        events.Publish(
            tick, AbilityEventKind.Released, ability.RawId, -1,
            target.Kind, target.Id, target.Position,
            casterBuilding: caster.Value);
        foreach (var effect in level.Effects)
        {
            if (effect.Timing != AbilityEffectTiming.Impact) continue;
            ApplyBuildingEffect(playerId, level, effect, target);
        }
        events.Publish(
            tick, AbilityEventKind.Impact, ability.RawId, -1,
            target.Kind, target.Id, target.Position,
            casterBuilding: caster.Value);
        events.Publish(
            tick, AbilityEventKind.Ended, ability.RawId, -1,
            target.Kind, target.Id, target.Position,
            AbilityEndReason.Completed, caster.Value);
        return new(
            AbilityCommandCode.Success,
            AbilityId: abilityId,
            CasterBuilding: caster.Value);
    }

    private AbilityCommandResult IssueBuildingToggle(
        int playerId,
        GameplayBuildingId caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        Vector2 center,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        var formEffect = level.Effects.FirstOrDefault(value =>
            value.Kind == AbilityEffectKind.TransformUnit &&
            value.Timing == AbilityEffectTiming.Impact &&
            value.Selector == AbilityEffectSelector.AreaAtCaster);
        if (formEffect.Kind != AbilityEffectKind.TransformUnit)
            return new(
                AbilityCommandCode.InvalidTarget,
                AbilityId: ability.Id,
                CasterBuilding: caster.Value);

        var abilityId = ability.Id;
        var toggleIndex = _buildingToggles.FindIndex(value =>
            value.Building == caster.Value && value.AbilityId == abilityId);
        var enabling = toggleIndex < 0;
        if (enabling)
            _buildingToggles.Add(new AbilityBuildingToggleRuntimeEntry(
                caster.Value, abilityId));
        else
            _buildingToggles.RemoveAt(toggleIndex);

        var radius = formEffect.Radius > 0f
            ? formEffect.Radius
            : level.Area;
        var radiusSquared = radius * radius;
        var maximumTargets = formEffect.MaximumTargets > 0
            ? formEffect.MaximumTargets
            : int.MaxValue;
        var affected = 0;
        for (var unit = 0; unit < world.AbilityUnitCount &&
             affected < maximumTargets; unit++)
        {
            if (!world.AbilityUnitAlive(unit) ||
                world.AbilityUnitOwner(unit) != playerId)
                continue;
            var isApproachingThisBuilding = _unitForms.Any(value =>
                value.Unit == unit &&
                value.TargetBuilding == caster.Value &&
                value.Phase == AbilityUnitFormPhase.ApproachingAlternate);
            if (Vector2.DistanceSquared(
                    world.AbilityUnitPosition(unit), center) > radiusSquared &&
                !(!enabling && isApproachingThisBuilding))
                continue;
            var type = _unitTypeIds[unit];
            if (enabling && type != formEffect.UnitForm.Normal.Id ||
                !enabling && type != formEffect.UnitForm.Alternate.Id &&
                !isApproachingThisBuilding)
                continue;
            BeginUnitFormTransition(
                unit, formEffect.UnitForm, world, caster, enabling);
            affected++;
        }

        events.Publish(
            tick, AbilityEventKind.Released, ability.RawId, -1,
            AbilityTargetKind.Building, caster.Value, center,
            casterBuilding: caster.Value);
        events.Publish(
            tick, AbilityEventKind.Impact, ability.RawId, -1,
            AbilityTargetKind.Building, caster.Value, center,
            casterBuilding: caster.Value);
        events.Publish(
            tick, AbilityEventKind.Ended, ability.RawId, -1,
            AbilityTargetKind.Building, caster.Value, center,
            AbilityEndReason.Completed, caster.Value);
        return new(
            AbilityCommandCode.Success,
            AbilityId: ability.Id,
            CasterBuilding: caster.Value);
    }

    private static bool CanApplyBuildingAbility(
        in AbilityProfile ability,
        in AbilityLevelProfile level)
    {
        if (ability.Activation == AbilityActivationKind.Toggle)
            return level.Effects.Any(value =>
                value.Kind == AbilityEffectKind.TransformUnit &&
                value.Timing == AbilityEffectTiming.Impact &&
                value.Selector == AbilityEffectSelector.AreaAtCaster);
        return level.Effects.Any(value =>
            value.Kind == AbilityEffectKind.Reveal &&
            value.Timing == AbilityEffectTiming.Impact);
    }

    private static AbilityCommandCode ValidateBuildingTarget(
        GameplayBuildingId caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world)
    {
        var bounds = world.AbilityBuildingBounds(caster);
        var center = (bounds.Min + bounds.Max) * 0.5f;
        Vector2 targetPosition;
        switch (ability.Activation)
        {
            case AbilityActivationKind.Instant:
            case AbilityActivationKind.Toggle:
                if (target.Kind != AbilityTargetKind.Building ||
                    target.Id != caster.Value)
                    return AbilityCommandCode.InvalidTarget;
                targetPosition = center;
                break;
            case AbilityActivationKind.TargetPoint:
            case AbilityActivationKind.ChannelPoint:
                if (target.Kind != AbilityTargetKind.World ||
                    !float.IsFinite(target.Position.X) ||
                    !float.IsFinite(target.Position.Y))
                    return AbilityCommandCode.InvalidTarget;
                targetPosition = target.Position;
                break;
            default:
                return AbilityCommandCode.InvalidTarget;
        }
        if (level.Range <= 0f ||
            ability.Activation is AbilityActivationKind.Instant or
                AbilityActivationKind.Toggle)
            return AbilityCommandCode.Success;
        var extent = bounds.Max - bounds.Min;
        var allowance = level.Range + MathF.Max(extent.X, extent.Y) * 0.5f;
        return Vector2.DistanceSquared(center, targetPosition) <=
               allowance * allowance
            ? AbilityCommandCode.Success
            : AbilityCommandCode.OutOfRange;
    }

    private void ApplyBuildingEffect(
        int playerId,
        in AbilityLevelProfile level,
        in AbilityEffectProfile effect,
        in AbilityCastTarget target)
    {
        if (effect.Kind != AbilityEffectKind.Reveal) return;
        _revealSources.Add(new AbilityRevealRuntimeEntry(
            playerId, target.Position, EffectRadius(effect, level),
            MathF.Max(
                0.05f,
                effect.Duration > 0f ? effect.Duration : level.Duration)));
    }

    private int EnsureBuildingRuntime(
        GameplayBuildingId building,
        in BuildingAbilityBindingProfile binding)
    {
        var index = FindBuildingRuntime(building);
        if (index >= 0)
        {
            if (_buildings[index].BuildingTypeId != binding.BuildingTypeId)
                throw new InvalidOperationException(
                    "Building ability runtime type mismatch.");
            return index;
        }
        _buildings.Add(new AbilityBuildingRuntimeEntry(
            building.Value, binding.BuildingTypeId,
            binding.Mana.Initial, binding.Mana.Maximum,
            binding.Mana.RegenerationPerSecond,
            binding.Abilities.ToArray(), new float[binding.Abilities.Length]));
        return _buildings.Count - 1;
    }

    private int FindBuildingRuntime(GameplayBuildingId building) =>
        _buildings.FindIndex(value => value.Building == building.Value);

    private static float BuildingCooldown(
        in AbilityBuildingRuntimeEntry runtime,
        int abilityId)
    {
        var slot = Array.IndexOf(runtime.AbilityIds, abilityId);
        return slot >= 0 ? runtime.Cooldowns[slot] : 0f;
    }

    private static void SetBuildingCooldown(
        ref AbilityBuildingRuntimeEntry runtime,
        int abilityId,
        float cooldown)
    {
        var slot = Array.IndexOf(runtime.AbilityIds, abilityId);
        if (slot < 0)
            throw new InvalidOperationException(
                "Building ability cooldown slot is missing.");
        runtime.Cooldowns[slot] = MathF.Max(0f, cooldown);
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
            if (_maximumMana[unit] > _baseMaximumMana[unit] &&
                _manaBonusDecayPerSecond[unit] > 0f)
            {
                var decay = _manaBonusDecayPerSecond[unit] * delta;
                _mana[unit] = MathF.Max(
                    _baseMaximumMana[unit], _mana[unit] - decay);
                _maximumMana[unit] = MathF.Max(
                    _baseMaximumMana[unit], _mana[unit]);
                if (_maximumMana[unit] <= _baseMaximumMana[unit])
                    _manaBonusDecayPerSecond[unit] = 0f;
            }
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
                _summonedObjectIds[summon.Unit] = null;
                _summons.RemoveAt(index);
                continue;
            }
            if (float.IsPositiveInfinity(summon.RemainingSeconds))
                continue;
            summon = summon with { RemainingSeconds = summon.RemainingSeconds - delta };
            if (summon.RemainingSeconds <= 0f)
            {
                world.AbilityKillSummon(summon.Unit);
                _summonedObjectIds[summon.Unit] = null;
                _summons.RemoveAt(index);
            }
            else
            {
                _summons[index] = summon;
            }
        }

        UpdatePersistentEffects(delta, tick, world, events);
        UpdateProjectiles(delta, tick, world, events);
        UpdateUnitForms(delta, world, units, combat);
        for (var index = _buildings.Count - 1; index >= 0; index--)
        {
            var runtime = _buildings[index];
            if (!world.AbilityBuildingAlive(
                    new GameplayBuildingId(runtime.Building)))
            {
                _buildings.RemoveAt(index);
                continue;
            }
            for (var slot = 0; slot < runtime.Cooldowns.Length; slot++)
                runtime.Cooldowns[slot] = MathF.Max(
                    0f, runtime.Cooldowns[slot] - delta);
            runtime = runtime with
            {
                Mana = MathF.Min(
                    runtime.MaximumMana,
                    runtime.Mana + runtime.ManaRegeneration * delta)
            };
            _buildings[index] = runtime;
        }
        for (var index = _buildingToggles.Count - 1; index >= 0; index--)
        {
            if (!world.AbilityBuildingAlive(
                    new GameplayBuildingId(_buildingToggles[index].Building)))
                _buildingToggles.RemoveAt(index);
        }
        for (var index = _buildingStatuses.Count - 1; index >= 0; index--)
        {
            var status = _buildingStatuses[index];
            if (!world.AbilityBuildingAlive(
                    new GameplayBuildingId(status.Building)))
            {
                _buildingStatuses.RemoveAt(index);
                continue;
            }
            status = status with
            {
                RemainingSeconds = status.RemainingSeconds - delta
            };
            if (status.RemainingSeconds <= 0f)
                _buildingStatuses.RemoveAt(index);
            else
                _buildingStatuses[index] = status;
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
            var repairs = ContainsEffectKind(
                level.Effects, AbilityEffectKind.Repair);
            if (repairs && HasActiveRepairEffect(caster))
                continue;
            for (var target = 0; target < world.AbilityUnitCount; target++)
            {
                var castTarget = AbilityCastTarget.Unit(
                    target, world.AbilityUnitPosition(target));
                var autoCastRange = level.AutoCastRange > 0f
                    ? level.AutoCastRange
                    : level.Range;
                if (autoCastRange > 0f)
                {
                    var allowance = autoCastRange +
                                    world.AbilityUnitRadius(caster);
                    if (Vector2.DistanceSquared(
                            world.AbilityUnitPosition(caster),
                            castTarget.Position) > allowance * allowance)
                        continue;
                }
                if (ValidateTarget(
                        world.AbilityUnitOwner(caster), caster, ability, level,
                        castTarget, world) != AbilityCommandCode.Success ||
                    !ShouldAutoCast(caster, target, ability, level, world))
                    continue;
                return Issue(
                    world.AbilityUnitOwner(caster), caster, ability.Id,
                    castTarget, tick, world, events).Succeeded;
            }
            if ((ability.Targets & AbilityTargetFlags.Building) == 0 ||
                !repairs)
                continue;
            for (var target = 0; target < world.AbilityBuildingCount; target++)
            {
                var building = new GameplayBuildingId(target);
                if (!world.AbilityBuildingAlive(building) ||
                    world.AbilityBuildingHealth(building) + 0.5f >=
                    world.AbilityBuildingMaximumHealth(building))
                    continue;
                var bounds = world.AbilityBuildingBounds(building);
                var castTarget = AbilityCastTarget.Building(
                    building, (bounds.Min + bounds.Max) * 0.5f);
                if (ValidateTarget(
                        world.AbilityUnitOwner(caster), caster, ability, level,
                        castTarget, world) != AbilityCommandCode.Success)
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
            if (effect.Kind == AbilityEffectKind.Repair &&
                (world.AbilityUnitAttributes(target) &
                 CombatAttribute.Mechanical) != 0 &&
                world.AbilityUnitHealth(target) + 0.5f <
                world.AbilityUnitMaximumHealth(target))
                return true;
            if (effect.Kind == AbilityEffectKind.ApplyStatus &&
                (effect.Modifier.HealthRegenerationAdd > 0f ||
                 effect.Modifier.ManaRegenerationAdd > 0f))
            {
                if (effect.Modifier.HealthRegenerationAdd > 0f &&
                    world.AbilityUnitHealth(target) + 0.5f <
                    world.AbilityUnitMaximumHealth(target) ||
                    effect.Modifier.ManaRegenerationAdd > 0f &&
                    _mana[target] + 0.5f < _maximumMana[target])
                    return true;
                continue;
            }
            if (effect.Kind == AbilityEffectKind.TransferMana)
            {
                if (relation is AbilityRelationFilter.Enemy or
                        AbilityRelationFilter.Neutral)
                {
                    if (_mana[target] > 0f &&
                        _mana[caster] < _maximumMana[caster])
                        return true;
                }
                else if (_mana[caster] > 0f &&
                         _mana[target] < _maximumMana[target])
                {
                    return true;
                }
            }
            if ((effect.Kind is AbilityEffectKind.ApplyStatus or
                    AbilityEffectKind.ToggleStatus))
            {
                var buffId = string.IsNullOrWhiteSpace(effect.BuffId)
                    ? ability.RawId
                    : effect.BuffId;
                if (!HasBuff(target, buffId))
                    return true;
            }
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
        var batch = combatEvents.CopyAfter(
            _combatEventCursor, ref _combatEventScratch);
        _combatEventCursor = batch.LatestSequence;
        for (var eventIndex = 0;
             eventIndex < batch.Count;
             eventIndex++)
        {
            var combatEvent = _combatEventScratch[eventIndex];
            if (combatEvent.Kind == CombatEventKind.TargetDestroyed &&
                combatEvent.TargetKind == CombatTargetKind.Unit)
            {
                AwardKillExperience(
                    combatEvent.AttackerUnit, combatEvent.TargetId, world);
                continue;
            }
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
                if (ability.Targets != AbilityTargetFlags.None &&
                    ValidateTarget(
                        world.AbilityUnitOwner(caster), caster,
                        ability, level, target, world,
                        impactValidation: true) != AbilityCommandCode.Success)
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
        AbilityEventStream events)
    {
        CancelCast(caster, tick, reason, world, events);
        _persistentEffects.RemoveAll(value =>
            value.SourceUnit == caster &&
            IsRepairEffect(value));
    }

    private bool IsRepairEffect(
        in AbilityPersistentEffectRuntimeEntry entry)
    {
        if ((uint)entry.AbilityId >= (uint)_catalog.Count)
            return false;
        var ability = _catalog.Ability(entry.AbilityId);
        if ((uint)(entry.Level - 1) >= (uint)ability.Levels.Length)
            return false;
        var effects = ability.Levels[entry.Level - 1].Effects;
        return (uint)entry.EffectIndex < (uint)effects.Length &&
               effects[entry.EffectIndex].Kind == AbilityEffectKind.Repair;
    }

    internal void ApplyVisibilitySources(
        PlayerVisibilitySystem visibility,
        IAbilityRuntimeWorld world)
    {
        foreach (var source in _revealSources)
            visibility.RevealAbilityArea(
                source.PlayerId, source.Position, source.Radius, detection: true);
        foreach (var buff in _buffs)
        {
            if ((buff.Status & AbilityStatusFlags.Revealed) == 0 ||
                !world.AbilityUnitAlive(buff.TargetUnit))
                continue;
            visibility.RevealAbilityArea(
                world.AbilityUnitOwner(buff.SourceUnit),
                world.AbilityUnitPosition(buff.TargetUnit),
                MathF.Max(1f, world.AbilityUnitRadius(buff.TargetUnit)),
                detection: true);
        }
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
        if (ability.Projectile.Enabled && level.ChannelSeconds <= 0f)
        {
            var destination = ProjectileDestination(target, world);
            var projectileId = _nextProjectileId++;
            _projectiles.Add(new AbilityProjectileSnapshot(
                projectileId, ability.Id, caster, level.Level, target,
                world.AbilityUnitPosition(caster),
                world.AbilityUnitPosition(caster), destination,
                ability.Projectile.Speed, ability.Projectile.Homing));
            events.Publish(
                tick, AbilityEventKind.Released, ability.RawId, caster,
                target.Kind, target.Id, destination,
                instanceId: projectileId);
            ClearCast(caster);
            return;
        }
        events.Publish(
            tick, AbilityEventKind.Released, ability.RawId, caster,
            target.Kind, target.Id, target.Position);
        events.Publish(
            tick, AbilityEventKind.Impact, ability.RawId, caster,
            target.Kind, target.Id, target.Position);
        ApplyEffects(
            caster, ability, level, AbilityEffectTiming.Impact, target, world);
        SchedulePersistentEffects(caster, ability, level, target);
        _castRemaining[caster] = 0f;
        if (level.ChannelSeconds > 0f && level.Effects.Any(value =>
                value.Timing == AbilityEffectTiming.ChannelPulse))
        {
            _castPhases[caster] = AbilityCastPhase.Channeling;
            _channelRemaining[caster] = level.ChannelSeconds;
            _pulsesRemaining[caster] = level.Effects
                .Where(value =>
                    value.Timing == AbilityEffectTiming.ChannelPulse)
                .Select(value => value.PulseCount)
                .DefaultIfEmpty(0)
                .Max();
            _pulseRemaining[caster] = _pulsesRemaining[caster] > 0
                ? level.Effects
                    .Where(value =>
                        value.Timing == AbilityEffectTiming.ChannelPulse &&
                        value.Interval > 0f)
                    .Select(value => value.Interval)
                    .DefaultIfEmpty(1f)
                    .Min()
                : 0f;
            return;
        }
        CompleteCast(caster, tick, events);
    }

    private void UpdateProjectiles(
        float delta,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            var projectile = _projectiles[index];
            var destination = projectile.Homing
                ? ProjectileDestination(projectile.Target, world)
                : projectile.Destination;
            var offset = destination - projectile.Position;
            var distance = offset.Length();
            var step = projectile.Speed * delta;
            if (distance > step && distance > 0.0001f)
            {
                _projectiles[index] = projectile with
                {
                    Position = projectile.Position + offset / distance * step,
                    Destination = destination
                };
                continue;
            }
            _projectiles.RemoveAt(index);
            CompleteProjectile(
                projectile with
                {
                    Position = destination,
                    Destination = destination
                }, tick, world, events);
        }
    }

    private void CompleteProjectile(
        in AbilityProjectileSnapshot projectile,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        if ((uint)projectile.AbilityId >= (uint)_catalog.Count ||
            (uint)projectile.CasterUnit >= (uint)world.AbilityUnitCount)
            return;
        var ability = _catalog.Ability(projectile.AbilityId);
        if (projectile.Level <= 0 ||
            projectile.Level > ability.Levels.Length)
            return;
        var level = ability.Levels[projectile.Level - 1];
        if (ValidateTarget(
                world.AbilityUnitOwner(projectile.CasterUnit),
                projectile.CasterUnit, ability, level,
                projectile.Target, world, impactValidation: true) !=
            AbilityCommandCode.Success)
        {
            events.Publish(
                tick, AbilityEventKind.Interrupted, ability.RawId,
                projectile.CasterUnit, projectile.Target.Kind,
                projectile.Target.Id, projectile.Destination,
                AbilityEndReason.TargetInvalid,
                instanceId: projectile.Id);
            return;
        }
        events.Publish(
            tick, AbilityEventKind.Impact, ability.RawId,
            projectile.CasterUnit, projectile.Target.Kind,
            projectile.Target.Id, projectile.Destination,
            instanceId: projectile.Id);
        ApplyEffects(
            projectile.CasterUnit, ability, level,
            AbilityEffectTiming.Impact, projectile.Target, world);
        SchedulePersistentEffects(
            projectile.CasterUnit, ability, level, projectile.Target);
        events.Publish(
            tick, AbilityEventKind.Ended, ability.RawId,
            projectile.CasterUnit, projectile.Target.Kind,
            projectile.Target.Id, projectile.Destination,
            AbilityEndReason.Completed,
            instanceId: projectile.Id);
    }

    private static Vector2 ProjectileDestination(
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world) => target.Kind switch
    {
        AbilityTargetKind.Unit or AbilityTargetKind.Self
            when (uint)target.Id < (uint)world.AbilityUnitCount =>
            world.AbilityUnitPosition(target.Id),
        AbilityTargetKind.Building
            when world.AbilityBuildingAlive(
                new GameplayBuildingId(target.Id)) =>
            (world.AbilityBuildingBounds(new GameplayBuildingId(target.Id)).Min +
             world.AbilityBuildingBounds(new GameplayBuildingId(target.Id)).Max) *
            0.5f,
        _ => target.Position
    };

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
        var counted = _pulsesRemaining[caster] > 0;
        while (_pulseRemaining[caster] <= 0f &&
               (counted
                   ? _pulsesRemaining[caster] > 0
                   : _channelRemaining[caster] > -interval))
        {
            events.Publish(
                tick, AbilityEventKind.Impact, ability.RawId, caster,
                target.Kind, target.Id, target.Position);
            ApplyEffects(
                caster, ability, level, AbilityEffectTiming.ChannelPulse,
                target, world);
            if (counted) _pulsesRemaining[caster]--;
            _pulseRemaining[caster] += MathF.Max(0.05f, interval);
        }
        if (counted
                ? _pulsesRemaining[caster] <= 0
                : _channelRemaining[caster] <= 0f)
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
        _pulsesRemaining[unit] = 0;
        _targets[unit] = default;
    }

    private void SchedulePersistentEffects(
        int caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityCastTarget target)
    {
        for (var effectIndex = 0;
             effectIndex < level.Effects.Length;
             effectIndex++)
        {
            var effect = level.Effects[effectIndex];
            if (effect.Timing != AbilityEffectTiming.PersistentPulse)
                continue;
            if (effect.Kind == AbilityEffectKind.Repair)
                _persistentEffects.RemoveAll(value =>
                    value.SourceUnit == caster && IsRepairEffect(value));
            var continuous = effect.Kind == AbilityEffectKind.Repair &&
                             effect.Duration == 0f &&
                             effect.PulseCount == 0;
            _persistentEffects.Add(new AbilityPersistentEffectRuntimeEntry(
                _nextPersistentEffectInstanceId++, ability.Id, caster,
                level.Level, effectIndex, target,
                continuous
                    ? float.MaxValue
                    : effect.StartDelay + effect.Duration,
                effect.StartDelay,
                effect.StartDelay > 0f ? effect.Interval : 0f,
                continuous ? int.MaxValue : effect.PulseCount));
        }
    }

    private void UpdatePersistentEffects(
        float delta,
        long tick,
        IAbilityRuntimeWorld world,
        AbilityEventStream events)
    {
        for (var index = _persistentEffects.Count - 1; index >= 0; index--)
        {
            var entry = _persistentEffects[index];
            if ((uint)entry.AbilityId >= (uint)_catalog.Count ||
                (uint)entry.SourceUnit >= (uint)world.AbilityUnitCount)
            {
                _persistentEffects.RemoveAt(index);
                continue;
            }
            var ability = _catalog.Ability(entry.AbilityId);
            if ((uint)(entry.Level - 1) >= (uint)ability.Levels.Length)
            {
                _persistentEffects.RemoveAt(index);
                continue;
            }
            var level = ability.Levels[entry.Level - 1];
            if ((uint)entry.EffectIndex >= (uint)level.Effects.Length)
            {
                _persistentEffects.RemoveAt(index);
                continue;
            }
            var effect = level.Effects[entry.EffectIndex];
            var remaining = entry.RemainingSeconds - delta;
            var delay = entry.StartDelayRemaining;
            var pulse = entry.PulseRemaining;
            if (delay > 0f)
            {
                delay -= delta;
                if (delay < 0f)
                {
                    pulse += delay;
                    delay = 0f;
                }
            }
            else
            {
                pulse -= delta;
            }
            var pulses = entry.PulsesRemaining;
            while (delay <= 0f && pulse <= 0f && pulses > 0)
            {
                events.Publish(
                    tick, AbilityEventKind.Impact, ability.RawId,
                    entry.SourceUnit, entry.Target.Kind, entry.Target.Id,
                    entry.Target.Position);
                var keepRunning = effect.Kind != AbilityEffectKind.Repair ||
                    entry.Target.Kind switch
                    {
                        AbilityTargetKind.Building =>
                            world.AbilityRepairBuilding(
                                entry.SourceUnit,
                                new GameplayBuildingId(entry.Target.Id),
                                effect.Value,
                                effect.SecondaryValue,
                                effect.HeroValue,
                                effect.HeroSecondaryValue,
                                effect.Interval,
                                level.Range),
                        AbilityTargetKind.Unit => world.AbilityRepairUnit(
                            entry.SourceUnit,
                            entry.Target.Id,
                            effect.Value,
                            effect.SecondaryValue,
                            effect.Interval,
                            level.Range),
                        _ => false
                    };
                if (effect.Kind != AbilityEffectKind.Repair)
                    ApplyEffect(
                        entry.SourceUnit, ability, level, effect,
                        entry.Target, world);
                if (!keepRunning)
                {
                    pulses = 0;
                    break;
                }
                pulses--;
                pulse += MathF.Max(0.05f, effect.Interval);
            }
            if (pulses <= 0 || remaining <= 0f)
            {
                _persistentEffects.RemoveAt(index);
                continue;
            }
            _persistentEffects[index] = entry with
            {
                RemainingSeconds = remaining,
                StartDelayRemaining = delay,
                PulseRemaining = pulse,
                PulsesRemaining = pulses
            };
        }
    }

    private void BeginUnitFormTransition(
        int unit,
        in AbilityUnitFormProfile profile,
        IAbilityRuntimeWorld world,
        GameplayBuildingId? forcedBuilding = null,
        bool? alternateRequested = null)
    {
        var requestAlternate = alternateRequested ??
                               _unitTypeIds[unit] == profile.Normal.Id;
        if (requestAlternate && _unitTypeIds[unit] == profile.Normal.Id)
        {
            _unitForms.RemoveAll(value => value.Unit == unit);
            var entry = new AbilityUnitFormRuntimeEntry(
                unit, profile, AbilityUnitFormPhase.ApproachingAlternate,
                -1, profile.AlternateDurationSeconds);
            _unitForms.Add(AssignFormBuilding(
                entry, world, forcedBuilding));
            return;
        }
        if (requestAlternate) return;
        if (_unitTypeIds[unit] == profile.Normal.Id)
        {
            if (_unitForms.RemoveAll(value => value.Unit == unit) > 0)
                world.AbilityPrepareCaster(unit);
            return;
        }
        if (_unitTypeIds[unit] != profile.Alternate.Id) return;
        var index = _unitForms.FindIndex(value => value.Unit == unit);
        var returning = new AbilityUnitFormRuntimeEntry(
            unit, profile, AbilityUnitFormPhase.ApproachingNormal,
            -1, 0f);
        returning = AssignFormBuilding(returning, world, forcedBuilding);
        if (index >= 0) _unitForms[index] = returning;
        else _unitForms.Add(returning);
    }

    private void UpdateUnitForms(
        float delta,
        IAbilityRuntimeWorld world,
        UnitStore units,
        CombatStore combat)
    {
        for (var index = _unitForms.Count - 1; index >= 0; index--)
        {
            var entry = _unitForms[index];
            if (!world.AbilityUnitAlive(entry.Unit))
            {
                _unitForms.RemoveAt(index);
                continue;
            }
            if (entry.Phase == AbilityUnitFormPhase.Alternate)
            {
                var remaining = entry.RemainingSeconds - delta;
                if (remaining > 0f)
                {
                    _unitForms[index] = entry with
                    {
                        RemainingSeconds = remaining
                    };
                    continue;
                }
                entry = new AbilityUnitFormRuntimeEntry(
                    entry.Unit, entry.Profile,
                    AbilityUnitFormPhase.ApproachingNormal, -1, 0f);
                entry = TryAssignFormBuilding(entry, world);
                _unitForms[index] = entry;
                continue;
            }

            var building = new GameplayBuildingId(entry.TargetBuilding);
            if (entry.TargetBuilding < 0 ||
                !world.AbilityBuildingAlive(building) ||
                world.AbilityBuildingOwner(building) !=
                world.AbilityUnitOwner(entry.Unit))
            {
                entry = TryAssignFormBuilding(
                    entry with { TargetBuilding = -1 }, world);
                _unitForms[index] = entry;
                continue;
            }
            if (!world.AbilityUnitTouchesBuilding(entry.Unit, building))
            {
                if (!world.AbilityUnitMovingToBuilding(entry.Unit, building))
                    world.AbilityMoveUnitToBuilding(entry.Unit, building);
                continue;
            }

            var profile = entry.Phase ==
                          AbilityUnitFormPhase.ApproachingAlternate
                ? entry.Profile.Alternate
                : entry.Profile.Normal;
            if (!world.AbilityApplyUnitProfile(entry.Unit, profile) ||
                !RebindUnitForm(entry.Unit, profile, units, combat))
                continue;
            if (entry.Phase == AbilityUnitFormPhase.ApproachingAlternate)
            {
                _unitForms[index] = entry with
                {
                    Phase = AbilityUnitFormPhase.Alternate,
                    TargetBuilding = -1,
                    RemainingSeconds = entry.Profile.AlternateDurationSeconds
                };
            }
            else
            {
                _unitForms.RemoveAt(index);
            }
        }
    }

    private static AbilityUnitFormRuntimeEntry TryAssignFormBuilding(
        in AbilityUnitFormRuntimeEntry entry,
        IAbilityRuntimeWorld world)
    {
        if (!world.AbilityTryFindNearestOwnedBuilding(
                entry.Unit, entry.Profile.RequiredBuildingFunction,
                out var building))
            return entry;
        world.AbilityMoveUnitToBuilding(entry.Unit, building);
        return entry with { TargetBuilding = building.Value };
    }

    private static AbilityUnitFormRuntimeEntry AssignFormBuilding(
        in AbilityUnitFormRuntimeEntry entry,
        IAbilityRuntimeWorld world,
        GameplayBuildingId? forcedBuilding)
    {
        if (forcedBuilding is not { } building)
            return TryAssignFormBuilding(entry, world);
        if (!world.AbilityBuildingAlive(building) ||
            !world.AbilityBuildingCompleted(building) ||
            world.AbilityBuildingOwner(building) !=
            world.AbilityUnitOwner(entry.Unit))
            return entry;
        world.AbilityMoveUnitToBuilding(entry.Unit, building);
        return entry with { TargetBuilding = building.Value };
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
            if (!impactValidation &&
                !world.AbilityCanSeeUnit(playerId, target.Id) && alive)
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
                HasStatus(target.Id, AbilityStatusFlags.Resistant) &&
                !_heroes[target.Id] &&
                (ability.Targets & AbilityTargetFlags.NonHero) != 0 &&
                (ability.Targets & AbilityTargetFlags.Hero) == 0 ||
                (ability.Targets & AbilityTargetFlags.Ground) != 0 &&
                (ability.Targets & AbilityTargetFlags.Air) == 0 && isAir ||
                (ability.Targets & AbilityTargetFlags.Air) != 0 &&
                (ability.Targets & AbilityTargetFlags.Ground) == 0 && !isAir ||
                (ability.Targets & AbilityTargetFlags.Vulnerable) != 0 &&
                (ability.Targets & AbilityTargetFlags.Invulnerable) == 0 &&
                invulnerable ||
                (ability.Targets & AbilityTargetFlags.Invulnerable) != 0 &&
                (ability.Targets & AbilityTargetFlags.Vulnerable) == 0 &&
                !invulnerable ||
                !UnitTraitsMatch(
                    target.Id, ability.Targets,
                    world.AbilityUnitOwner(target.Id)))
                return AbilityCommandCode.InvalidTarget;
            if (HasStatus(target.Id, AbilityStatusFlags.MagicImmune) &&
                relation == AbilityRelationFilter.Enemy &&
                AbilityBlockedByMagicImmunity(level, relation))
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

        if (!impactValidation && level.Range > 0f &&
            target.Kind != AbilityTargetKind.Self)
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

    private static bool AbilityBlockedByMagicImmunity(
        in AbilityLevelProfile level,
        AbilityRelationFilter relation)
    {
        var found = false;
        foreach (var effect in level.Effects)
        {
            if ((effect.Relations & relation) == 0 ||
                effect.Timing is AbilityEffectTiming.Aura or
                    AbilityEffectTiming.AttackHit)
                continue;
            found = true;
            if (!EffectBlockedByMagicImmunity(effect)) return false;
        }
        return found;
    }

    private static bool EffectBlockedByMagicImmunity(
        in AbilityEffectProfile effect) => effect.Kind switch
    {
        AbilityEffectKind.Damage =>
            effect.DamageKind == AbilityDamageKind.Magic,
        AbilityEffectKind.Mana => true,
        AbilityEffectKind.TransferMana => true,
        AbilityEffectKind.ApplyStatus or AbilityEffectKind.ToggleStatus =>
            effect.DamageKind == AbilityDamageKind.Magic ||
            (effect.BuffDispelKind & AbilityBuffDispelKind.Magic) != 0,
        AbilityEffectKind.TransferControl or AbilityEffectKind.TransferBuff =>
            true,
        _ => false
    };

    private static bool EffectBlockedByInvulnerability(
        in AbilityEffectProfile effect) => effect.Kind is
        AbilityEffectKind.Damage or
        AbilityEffectKind.Mana or
        AbilityEffectKind.TransferMana or
        AbilityEffectKind.TransferControl or
        AbilityEffectKind.ApplyStatus or
        AbilityEffectKind.ToggleStatus;

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
                    AddSummon(new AbilitySummonRuntimeEntry(
                        summoned, caster, effect.Summon.ObjectId,
                        effect.Summon.LifetimeSeconds > 0f
                            ? effect.Summon.LifetimeSeconds
                            : float.PositiveInfinity));
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

        if (effect.Kind == AbilityEffectKind.Teleport &&
            effect.ClusteredPlacement)
        {
            var selected = SelectUnits(
                    caster, ability, level, effect, target, world)
                .ToArray();
            world.AbilityTeleportUnits(
                selected, TargetPosition(caster, target, world));
            return;
        }

        if (effect.Kind == AbilityEffectKind.Damage &&
            effect.Selector is AbilityEffectSelector.AreaAtTarget or
                AbilityEffectSelector.AreaAtCaster)
        {
            ApplyAreaDamage(
                caster, ability, level, effect, target, world);
            return;
        }

        if (effect.AffectsBuildings)
        {
            ApplyEffectToBuildings(
                caster, ability, level, effect, target, world);
            return;
        }

        if (effect.Selector == AbilityEffectSelector.Primary &&
            target.Kind == AbilityTargetKind.Building)
        {
            if (effect.Kind == AbilityEffectKind.Damage)
                world.AbilityDamageBuilding(
                    caster, new GameplayBuildingId(target.Id),
                    MathF.Max(0f, effect.Value), effect.DamageKind);
            return;
        }

        foreach (var targetUnit in SelectUnits(
                     caster, ability, level, effect, target, world))
        {
            ApplyEffectToUnit(
                caster, ability, level, effect, target, targetUnit, world, toggle);
        }
    }

    private void ApplyEffectToBuildings(
        int caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityEffectProfile effect,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world)
    {
        if (effect.Kind != AbilityEffectKind.ApplyStatus)
            throw new InvalidOperationException(
                "Only status effects can currently target buildings.");
        var duration = EffectDuration(effect, level, false, false);
        var abilityId = ability.Id;
        foreach (var building in SelectBuildings(
                     caster, level, effect, target, world))
        {
            var index = _buildingStatuses.FindIndex(value =>
                value.Building == building.Value &&
                value.AbilityId == abilityId);
            var status = new AbilityBuildingStatusRuntimeEntry(
                building.Value, abilityId, caster, duration, effect.Status);
            if (index >= 0)
                _buildingStatuses[index] = status;
            else
                _buildingStatuses.Add(status);
        }
    }

    private void ApplyAreaDamage(
        int caster,
        in AbilityProfile ability,
        in AbilityLevelProfile level,
        in AbilityEffectProfile effect,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world)
    {
        var units = SelectUnits(
                caster, ability, level, effect, target, world)
            .ToArray();
        var buildings = SelectBuildings(
                caster, level, effect, target, world)
            .ToArray();
        foreach (var group in new[]
                 {
                     AbilityRelationFilter.Friendly,
                     AbilityRelationFilter.Enemy,
                     AbilityRelationFilter.Neutral
                 })
        {
            var selectedUnits = units.Where(unit =>
            {
                var relation = RelationFilter(
                    world.AbilityUnitOwner(caster), caster, unit,
                    world.AbilityUnitOwner(unit), world);
                return group == AbilityRelationFilter.Friendly
                    ? relation is AbilityRelationFilter.Self or
                        AbilityRelationFilter.Friendly
                    : relation == group;
            }).ToArray();
            var selectedBuildings = buildings.Where(building =>
            {
                var relation = RelationFilter(
                    world.AbilityUnitOwner(caster), caster, -1,
                    world.AbilityBuildingOwner(building), world);
                return relation == group;
            }).ToArray();
            var count = selectedUnits.Length + selectedBuildings.Length;
            if (count == 0) continue;
            var value = effect.MaximumTotalValue > 0f
                ? MathF.Min(
                    effect.Value, effect.MaximumTotalValue / count)
                : effect.Value;
            var adjusted = effect with { Value = value };
            foreach (var unit in selectedUnits)
                ApplyEffectToUnit(
                    caster, ability, level, adjusted, target, unit,
                    world, false);
            if (effect.BuildingValueMultiplier <= 0f) continue;
            var buildingDamage = MathF.Max(
                0f, value * effect.BuildingValueMultiplier);
            foreach (var building in selectedBuildings)
                world.AbilityDamageBuilding(
                    caster, building, buildingDamage, effect.DamageKind);
        }
    }

    private IEnumerable<GameplayBuildingId> SelectBuildings(
        int caster,
        in AbilityLevelProfile level,
        in AbilityEffectProfile effect,
        in AbilityCastTarget target,
        IAbilityRuntimeWorld world)
    {
        if (!effect.AffectsBuildings && effect.BuildingValueMultiplier <= 0f)
            return [];
        var center = effect.Selector == AbilityEffectSelector.AreaAtCaster
            ? world.AbilityUnitPosition(caster)
            : TargetPosition(caster, target, world);
        var radius = EffectRadius(effect, level);
        var radiusSquared = radius * radius;
        var output = new List<GameplayBuildingId>();
        for (var index = 0; index < world.AbilityBuildingCount; index++)
        {
            var building = new GameplayBuildingId(index);
            if (!world.AbilityBuildingAlive(building)) continue;
            var relation = RelationFilter(
                world.AbilityUnitOwner(caster), caster, -1,
                world.AbilityBuildingOwner(building), world);
            if ((effect.Relations & relation) == 0) continue;
            var closest = world.AbilityBuildingBounds(building).Clamp(center);
            if (Vector2.DistanceSquared(center, closest) > radiusSquared)
                continue;
            output.Add(building);
        }
        return output;
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
        if (!EffectTraitsMatch(target, effect)) return;
        if (relation == AbilityRelationFilter.Enemy &&
            HasStatus(target, AbilityStatusFlags.Invulnerable) &&
            EffectBlockedByInvulnerability(effect))
            return;
        if (relation == AbilityRelationFilter.Enemy &&
            HasStatus(target, AbilityStatusFlags.MagicImmune) &&
            EffectBlockedByMagicImmunity(effect))
            return;
        switch (effect.Kind)
        {
            case AbilityEffectKind.Heal:
                world.AbilityHealUnit(target, MathF.Max(0f, effect.Value));
                break;
            case AbilityEffectKind.Damage:
                if (!HasStatus(target, AbilityStatusFlags.Invulnerable))
                    DamageUnitAndAwardExperience(
                        world,
                        caster, target, MathF.Max(0f, effect.Value),
                        effect.DamageKind);
                break;
            case AbilityEffectKind.Mana:
            {
                var useHeroValues = _heroes[target] &&
                                    effect.HeroValue != 0f;
                var amount = useHeroValues
                    ? effect.HeroValue
                    : effect.Value;
                var damagePerMana = useHeroValues
                    ? effect.HeroSecondaryValue
                    : effect.SecondaryValue;
                if (amount < 0f)
                {
                    var removed = MathF.Min(_mana[target], -amount);
                    _mana[target] -= removed;
                    if (damagePerMana > 0f)
                        DamageUnitAndAwardExperience(
                            world,
                            caster, target, removed * damagePerMana,
                            effect.DamageKind == AbilityDamageKind.None
                                ? AbilityDamageKind.Magic
                                : effect.DamageKind);
                    if (effect.SummonedValue > 0f && IsSummoned(target) &&
                        world.AbilityUnitAlive(target))
                        DamageUnitAndAwardExperience(
                            world,
                            caster, target, effect.SummonedValue,
                            effect.DamageKind == AbilityDamageKind.None
                                ? AbilityDamageKind.Magic
                                : effect.DamageKind);
                }
                else
                {
                    _mana[target] = MathF.Min(
                        _maximumMana[target], _mana[target] + amount);
                }
                break;
            }
            case AbilityEffectKind.TransferMana:
            {
                var amount = MathF.Max(0f, effect.Value);
                if (relation is AbilityRelationFilter.Enemy or
                    AbilityRelationFilter.Neutral)
                    TransferMana(
                        target, caster, amount,
                        effect.SecondaryValue, effect.HeroValue);
                else
                    TransferMana(
                        caster, target, amount,
                        effect.SecondaryValue, effect.HeroValue);
                break;
            }
            case AbilityEffectKind.ApplyStatus:
                if (relation == AbilityRelationFilter.Enemy &&
                    HasStatus(target, AbilityStatusFlags.Invulnerable))
                    break;
                if (effect.Value > 0f &&
                    !HasStatus(target, AbilityStatusFlags.Invulnerable))
                    DamageUnitAndAwardExperience(
                        world,
                        caster, target, effect.Value,
                        effect.DamageKind == AbilityDamageKind.None
                            ? AbilityDamageKind.Magic
                            : effect.DamageKind);
                var appliedEffect = effect;
                if ((effect.Status & AbilityStatusFlags.Polymorphed) != 0 &&
                    effect.SecondaryValue > 0f)
                {
                    var modifier = effect.Modifier.Normalized with
                    {
                        MovementSpeedMultiplier = Math.Clamp(
                            effect.SecondaryValue /
                            MathF.Max(0.001f, _baseMaximumSpeed[target]),
                            0f, 10f)
                    };
                    appliedEffect = effect with { Modifier = modifier };
                }
                AddOrRefreshBuff(
                    caster, ability, target,
                    toggle
                        ? float.PositiveInfinity
                        : EffectDuration(
                            effect, level, _heroes[target],
                            HasStatus(target, AbilityStatusFlags.Resistant)),
                    relation is AbilityRelationFilter.Self or
                        AbilityRelationFilter.Friendly,
                    appliedEffect);
                break;
            case AbilityEffectKind.ToggleStatus:
                AddOrRefreshBuff(
                    caster, ability, target, float.PositiveInfinity,
                    true, effect);
                break;
            case AbilityEffectKind.Dispel:
            {
                var dispelKind = effect.BuffDispelKind;
                _buffs.RemoveAll(value =>
                    value.TargetUnit == target &&
                    value.DispelKind != AbilityBuffDispelKind.None &&
                    (value.DispelKind & dispelKind) != 0 &&
                    (relation == AbilityRelationFilter.Enemy
                        ? value.Beneficial
                        : !value.Beneficial));
                if (effect.SecondaryValue > 0f &&
                    relation is (AbilityRelationFilter.Enemy or
                        AbilityRelationFilter.Neutral) &&
                    IsSummoned(target))
                {
                    DamageUnitAndAwardExperience(
                        world, caster, target, effect.SecondaryValue,
                        effect.DamageKind == AbilityDamageKind.None
                            ? AbilityDamageKind.Magic
                            : effect.DamageKind);
                }
                break;
            }
            case AbilityEffectKind.TransferBuff:
                TransferOneBuff(
                    caster, target, relation, effect.BuffDispelKind,
                    EffectRadius(effect, level), world);
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
            case AbilityEffectKind.TransformUnit:
                BeginUnitFormTransition(target, effect.UnitForm, world);
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
            return UnitTraitsMatch(
                target.Id, ability.Targets,
                world.AbilityUnitOwner(target.Id)) &&
                EffectTraitsMatch(target.Id, effect)
                ? [target.Id]
                : [];

        var center = effect.Selector == AbilityEffectSelector.AreaAtCaster
            ? world.AbilityUnitPosition(caster)
            : TargetPosition(caster, target, world);
        var radius = EffectRadius(effect, level);
        var radiusSquared = radius * radius;
        var innerRadiusSquared = effect.InnerRadius * effect.InnerRadius;
        var output = new List<int>();
        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            var needsDead = (ability.Targets & AbilityTargetFlags.Dead) != 0 &&
                            (ability.Targets & AbilityTargetFlags.Alive) == 0;
            var distanceSquared = Vector2.DistanceSquared(
                center, world.AbilityUnitPosition(unit));
            if (!world.AbilityUnitExists(unit) ||
                needsDead == world.AbilityUnitAlive(unit) ||
                distanceSquared > radiusSquared ||
                effect.InnerRadius > 0f &&
                distanceSquared <= innerRadiusSquared)
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
            if (!UnitTraitsMatch(
                    unit, ability.Targets, world.AbilityUnitOwner(unit)))
                continue;
            if (!EffectTraitsMatch(unit, effect)) continue;
            if (relation == AbilityRelationFilter.Enemy &&
                HasStatus(unit, AbilityStatusFlags.MagicImmune) &&
                EffectBlockedByMagicImmunity(effect))
                continue;
            output.Add(unit);
            if (effect.Kind != AbilityEffectKind.Teleport &&
                effect.MaximumTargets > 0 &&
                output.Count >= effect.MaximumTargets)
                break;
        }
        if (effect.Kind == AbilityEffectKind.Teleport)
        {
            output.Sort((left, right) =>
            {
                if (left == caster) return right == caster ? 0 : -1;
                if (right == caster) return 1;
                var leftDistance = Vector2.DistanceSquared(
                    center, world.AbilityUnitPosition(left));
                var rightDistance = Vector2.DistanceSquared(
                    center, world.AbilityUnitPosition(right));
                var comparison = leftDistance.CompareTo(rightDistance);
                return comparison != 0 ? comparison : left.CompareTo(right);
            });
            if (effect.MaximumTargets > 0 &&
                output.Count > effect.MaximumTargets)
                output.RemoveRange(
                    effect.MaximumTargets,
                    output.Count - effect.MaximumTargets);
        }
        return output;
    }

    private bool UnitTraitsMatch(
        int unit,
        AbilityTargetFlags targets,
        int owner)
    {
        var traits = _traits[unit];
        var ancient = (traits & AbilityUnitTraits.Ancient) != 0;
        if ((targets & AbilityTargetFlags.Ancient) != 0 &&
            (targets & AbilityTargetFlags.NonAncient) == 0 && !ancient)
            return false;
        if ((targets & AbilityTargetFlags.NonAncient) != 0 &&
            (targets & AbilityTargetFlags.Ancient) == 0 && ancient)
            return false;
        if ((targets & AbilityTargetFlags.NonSapper) != 0 &&
            (traits & AbilityUnitTraits.Sapper) != 0)
            return false;
        if ((traits & AbilityUnitTraits.Ward) != 0 &&
            (targets & AbilityTargetFlags.Ward) == 0)
            return false;
        if ((targets & AbilityTargetFlags.PlayerControlled) != 0 && owner <= 0)
            return false;
        return true;
    }

    private bool EffectTraitsMatch(
        int unit,
        in AbilityEffectProfile effect)
    {
        var traits = _traits[unit];
        return (traits & effect.RequiredUnitTraits) ==
                   effect.RequiredUnitTraits &&
               (traits & effect.ExcludedUnitTraits) == 0;
    }

    private void TransferMana(
        int source,
        int target,
        float amount,
        float bonusFactor,
        float bonusDecayPerSecond)
    {
        if (amount <= 0f || _baseMaximumMana[target] <= 0f) return;
        bonusFactor = MathF.Max(0f, bonusFactor);
        var capacity = _baseMaximumMana[target] * (1f + bonusFactor);
        var transferred = MathF.Min(
            MathF.Min(_mana[source], amount),
            capacity - _mana[target]);
        if (transferred <= 0f) return;
        _mana[source] -= transferred;
        _mana[target] += transferred;
        if (_mana[target] > _baseMaximumMana[target])
        {
            _maximumMana[target] = _mana[target];
            _manaBonusDecayPerSecond[target] = MathF.Max(
                _manaBonusDecayPerSecond[target], bonusDecayPerSecond);
        }
    }

    private bool DamageUnitAndAwardExperience(
        IAbilityRuntimeWorld world,
        int source,
        int target,
        float damage,
        AbilityDamageKind damageKind)
    {
        var wasAlive = world.AbilityUnitAlive(target);
        var applied = world.AbilityDamageUnit(
            source, target, damage, damageKind);
        if (applied && wasAlive && !world.AbilityUnitAlive(target))
            AwardKillExperience(source, target, world);
        return applied;
    }

    private void AwardKillExperience(
        int killer,
        int target,
        IAbilityRuntimeWorld world)
    {
        if ((uint)killer >= (uint)_capacity ||
            (uint)target >= (uint)_capacity ||
            _experienceBounty[target] <= 0 ||
            !world.AbilityUnitExists(killer))
            return;
        const float radius = 240f;
        var radiusSquared = radius * radius;
        var killerOwner = world.AbilityUnitOwner(killer);
        if (killerOwner <= 0) return;
        var position = world.AbilityUnitPosition(target);
        var recipients = new List<int>();
        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            if (!_heroes[unit] || !world.AbilityUnitAlive(unit) ||
                _heroLevels[unit] >= _heroMaximumLevels[unit] ||
                Vector2.DistanceSquared(
                    position, world.AbilityUnitPosition(unit)) > radiusSquared)
                continue;
            if (world.AbilityRelation(
                    killerOwner, world.AbilityUnitOwner(unit)) is not
                (PlayerEntityRelation.Own or PlayerEntityRelation.Ally))
                continue;
            recipients.Add(unit);
        }
        if (recipients.Count == 0) return;
        recipients.Sort();
        var share = _experienceBounty[target] / recipients.Count;
        var remainder = _experienceBounty[target] % recipients.Count;
        for (var index = 0; index < recipients.Count; index++)
            TryGrantHeroExperience(
                recipients[index], share + (index < remainder ? 1 : 0));
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
        bool targetHero,
        bool targetResistant) =>
        MathF.Max(0.05f,
            effect.Duration > 0f
                ? effect.Duration
                : (targetHero || targetResistant) && level.HeroDuration > 0f
                    ? level.HeroDuration
                    : level.Duration > 0f
                        ? level.Duration
                        : 0.05f);

    private void AddOrRefreshBuff(
        int source,
        in AbilityProfile ability,
        int target,
        float duration,
        bool beneficial,
        in AbilityEffectProfile effect)
    {
        var modifier = effect.Modifier.Normalized;
        var buffId = string.IsNullOrWhiteSpace(effect.BuffId)
            ? ability.RawId
            : effect.BuffId;
        var polarity = effect.BuffPolarity == AbilityBuffPolarity.Neutral
            ? beneficial
                ? AbilityBuffPolarity.Beneficial
                : AbilityBuffPolarity.Harmful
            : effect.BuffPolarity;
        var index = effect.BuffStacking == AbilityBuffStackingKind.Stack
            ? -1
            : _buffs.FindIndex(value =>
                value.TargetUnit == target &&
                value.BuffId.Equals(buffId, StringComparison.Ordinal));
        if (index >= 0)
        {
            if (effect.BuffStacking == AbilityBuffStackingKind.Replace)
            {
                _buffs.RemoveAt(index);
            }
            else
            {
                _buffs[index] = _buffs[index] with
                {
                    AbilityId = ability.Id,
                    SourceUnit = source,
                    RemainingSeconds = duration,
                    Beneficial = beneficial,
                    Polarity = polarity,
                    DispelKind = effect.BuffDispelKind,
                    Stacking = effect.BuffStacking,
                    Status = effect.Status,
                    Modifier = modifier,
                    CombatModifier = effect.CombatModifier.Normalized
                };
                return;
            }
        }
        _buffs.Add(new AbilityBuffRuntimeEntry(
            _nextBuffInstanceId++, ability.Id, source, target, buffId, duration,
            beneficial, polarity, effect.BuffDispelKind,
            effect.BuffStacking, effect.Status, modifier,
            effect.CombatModifier.Normalized));
    }

    private void TransferOneBuff(
        int caster,
        int target,
        AbilityRelationFilter relation,
        AbilityBuffDispelKind dispelKind,
        float range,
        IAbilityRuntimeWorld world)
    {
        var stealingBeneficial = relation is AbilityRelationFilter.Enemy or
            AbilityRelationFilter.Neutral;
        var index = _buffs.FindIndex(value =>
            value.TargetUnit == target &&
            value.DispelKind != AbilityBuffDispelKind.None &&
            (value.DispelKind & dispelKind) != 0 &&
            (stealingBeneficial
                ? value.Beneficial
                : !value.Beneficial));
        if (index < 0) return;
        var receiver = stealingBeneficial
            ? caster
            : FindNearestHostileUnit(caster, range, world);
        if (receiver < 0) return;
        var buff = _buffs[index];
        _buffs.RemoveAt(index);
        _buffs.Add(buff with
        {
            InstanceId = _nextBuffInstanceId++,
            SourceUnit = caster,
            TargetUnit = receiver,
            Beneficial = stealingBeneficial,
            Polarity = stealingBeneficial
                ? AbilityBuffPolarity.Beneficial
                : AbilityBuffPolarity.Harmful
        });
    }

    private static int FindNearestHostileUnit(
        int caster,
        float range,
        IAbilityRuntimeWorld world)
    {
        var owner = world.AbilityUnitOwner(caster);
        var center = world.AbilityUnitPosition(caster);
        var rangeSquared = range * range;
        var best = -1;
        var bestDistance = float.PositiveInfinity;
        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            if (!world.AbilityUnitAlive(unit) || unit == caster ||
                world.AbilityRelation(owner, world.AbilityUnitOwner(unit)) !=
                PlayerEntityRelation.Enemy)
                continue;
            var distance = Vector2.DistanceSquared(
                center, world.AbilityUnitPosition(unit));
            if (distance > rangeSquared || distance > bestDistance ||
                distance == bestDistance && unit >= best)
                continue;
            best = unit;
            bestDistance = distance;
        }
        return best;
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
        var combatModifiers = _combatModifierScratch;
        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            modifiers[unit] = AbilityStatModifier.Identity;
            combatModifiers[unit] = AbilityCombatModifier.Identity;
            _statuses[unit] = AbilityStatusFlags.None;
        }
        foreach (var buff in _buffs)
        {
            if (!world.AbilityUnitAlive(buff.TargetUnit)) continue;
            _statuses[buff.TargetUnit] |= buff.Status;
            modifiers[buff.TargetUnit] = Combine(
                modifiers[buff.TargetUnit], buff.Modifier);
            combatModifiers[buff.TargetUnit] = CombineCombat(
                combatModifiers[buff.TargetUnit], buff.CombatModifier);
        }
        ApplyPassiveAuras(world, modifiers, combatModifiers);

        for (var unit = 0; unit < world.AbilityUnitCount; unit++)
        {
            var modifier = modifiers[unit].Normalized;
            units.MaxSpeeds[unit] = MathF.Max(
                0f, _baseMaximumSpeed[unit] * modifier.MovementSpeedMultiplier);
            combat.SetAttackModifiers(
                unit,
                modifier.AttackDamageMultiplier,
                modifier.AttackDamageAdd,
                modifier.AttackCooldownMultiplier);
            combat.Armor[unit] = _baseArmor[unit] + modifier.ArmorAdd;
            combat.MaximumHealth[unit] = MathF.Max(
                1f, _baseMaximumHealth[unit] + modifier.MaximumHealthAdd);
            combat.Health[unit] = MathF.Min(
                combat.Health[unit], combat.MaximumHealth[unit]);
            combat.DetectionRanges[unit] = MathF.Max(
                0f, _baseDetectionRange[unit] + modifier.DetectionRangeAdd);
            _effectiveManaRegeneration[unit] = MathF.Max(
                0f, _baseManaRegeneration[unit] +
                    modifier.ManaRegenerationAdd);
            if (delta > 0f && modifier.HealthRegenerationAdd > 0f &&
                world.AbilityUnitAlive(unit))
                world.AbilityHealUnit(
                    unit, modifier.HealthRegenerationAdd * delta);
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
        AbilityStatModifier[] modifiers,
        AbilityCombatModifier[] combatModifiers)
    {
        _resolvedAuraEffects.Clear();
        _resolvedAuraOrder.Clear();
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
                        var buffId = string.IsNullOrWhiteSpace(effect.BuffId)
                            ? ability.RawId
                            : effect.BuffId;
                        if (effect.BuffStacking ==
                            AbilityBuffStackingKind.Stack)
                            buffId += $"\0{caster}";
                        var key = (target, buffId);
                        if (!_resolvedAuraEffects.TryGetValue(
                                key, out var current) ||
                            AuraStrength(effect) >
                            AuraStrength(current))
                            _resolvedAuraEffects[key] = effect;
                    }
                }
            }
        }
        foreach (var key in _resolvedAuraEffects.Keys)
            _resolvedAuraOrder.Add(key);
        _resolvedAuraOrder.Sort(static (left, right) =>
        {
            var target = left.Target.CompareTo(right.Target);
            return target != 0
                ? target
                : StringComparer.Ordinal.Compare(left.BuffId, right.BuffId);
        });
        foreach (var key in _resolvedAuraOrder)
        {
            var effect = _resolvedAuraEffects[key];
            var target = key.Target;
            _statuses[target] |= effect.Status;
            modifiers[target] = Combine(
                modifiers[target], effect.Modifier.Normalized);
            combatModifiers[target] = CombineCombat(
                combatModifiers[target], effect.CombatModifier.Normalized);
        }
    }

    private static bool ContainsEffectKind(
        ImmutableArray<AbilityEffectProfile> effects,
        AbilityEffectKind kind)
    {
        foreach (var effect in effects)
            if (effect.Kind == kind)
                return true;
        return false;
    }

    private bool HasActiveRepairEffect(int caster)
    {
        for (var index = 0; index < _persistentEffects.Count; index++)
        {
            var effect = _persistentEffects[index];
            if (effect.SourceUnit == caster && IsRepairEffect(effect))
                return true;
        }
        return false;
    }

    private bool HasBuff(int target, string buffId)
    {
        for (var index = 0; index < _buffs.Count; index++)
        {
            var buff = _buffs[index];
            if (buff.TargetUnit == target && buff.BuffId.Equals(
                    buffId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static float AuraStrength(in AbilityEffectProfile effect)
    {
        var value = effect.Modifier.Normalized;
        var combat = effect.CombatModifier.Normalized;
        return MathF.Abs(value.MovementSpeedMultiplier - 1f) +
               MathF.Abs(value.AttackCooldownMultiplier - 1f) +
               MathF.Abs(value.AttackDamageMultiplier - 1f) +
               MathF.Abs(value.AttackDamageAdd) +
               MathF.Abs(value.ArmorAdd) +
               MathF.Abs(value.MaximumHealthAdd) +
               MathF.Abs(value.ManaRegenerationAdd) +
               MathF.Abs(value.DetectionRangeAdd) +
               MathF.Abs(value.HealthRegenerationAdd) +
               MathF.Abs(combat.DamageTakenMultiplier - 1f) +
               MathF.Abs(combat.DamageDealtMultiplier - 1f) +
               MathF.Abs(combat.MagicDamageTakenMultiplier - 1f) +
               combat.DeflectChancePercent +
               combat.AttackMissChancePercent +
               combat.EvasionChancePercent +
               combat.CriticalStrikeChancePercent +
               MathF.Abs(combat.CriticalStrikeDamageMultiplier - 1f) +
               MathF.Abs(combat.CriticalStrikeBonusDamage) +
               (combat.CriticalStrikeNeverMiss ? 1f : 0f) +
               (ushort)effect.Status;
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
            left.DetectionRangeAdd + right.DetectionRangeAdd,
            left.HealthRegenerationAdd + right.HealthRegenerationAdd);
    }

    private static AbilityCombatModifier CombineCombat(
        AbilityCombatModifier left,
        AbilityCombatModifier right)
    {
        left = left.Normalized;
        right = right.Normalized;
        return new AbilityCombatModifier(
            left.DamageTakenMultiplier * right.DamageTakenMultiplier,
            left.DamageDealtMultiplier * right.DamageDealtMultiplier,
            left.MagicDamageTakenMultiplier *
            right.MagicDamageTakenMultiplier,
            MathF.Max(left.DeflectChancePercent,
                right.DeflectChancePercent),
            left.DeflectedPiercingDamageMultiplier *
            right.DeflectedPiercingDamageMultiplier,
            left.DeflectedMagicDamageMultiplier *
            right.DeflectedMagicDamageMultiplier,
            MathF.Max(left.AttackMissChancePercent,
                right.AttackMissChancePercent),
            MathF.Max(left.EvasionChancePercent,
                right.EvasionChancePercent),
            MathF.Max(left.CriticalStrikeChancePercent,
                right.CriticalStrikeChancePercent),
            MathF.Max(left.CriticalStrikeDamageMultiplier,
                right.CriticalStrikeDamageMultiplier),
            left.CriticalStrikeBonusDamage + right.CriticalStrikeBonusDamage,
            left.CriticalStrikeNeverMiss || right.CriticalStrikeNeverMiss);
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
                unit, _unitTypeIds[unit], _heroes[unit], _traits[unit],
                _heroLevels[unit], _heroMaximumLevels[unit],
                _heroExperience[unit], _experienceBounty[unit],
                _unitLevels[unit], _unspentSkillPoints[unit], _mana[unit],
                _maximumMana[unit], _baseMaximumMana[unit],
                _manaBonusDecayPerSecond[unit],
                _baseManaRegeneration[unit],
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
                _pulsesRemaining[unit],
                _targets[unit]);
        return new AbilityRuntimeSnapshot(
            _catalog, _nextBuffInstanceId,
            _nextPersistentEffectInstanceId, _nextProjectileId, units,
            _buffs.OrderBy(value => value.InstanceId).ToArray(),
            _summons.OrderBy(value => value.Unit).ToArray(),
            _revealSources.OrderBy(value => value.PlayerId)
                .ThenBy(value => value.Position.X)
                .ThenBy(value => value.Position.Y).ToArray(),
            _persistentEffects.OrderBy(value => value.InstanceId).ToArray(),
            _unitForms.OrderBy(value => value.Unit).ToArray(),
            _projectiles.OrderBy(value => value.Id).ToArray(),
            _buildings.OrderBy(value => value.Building)
                .Select(value => value with
                {
                    AbilityIds = value.AbilityIds.ToArray(),
                    Cooldowns = value.Cooldowns.ToArray()
                }).ToArray(),
            _buildingToggles.OrderBy(value => value.Building)
                .ThenBy(value => value.AbilityId).ToArray(),
            _buildingStatuses.OrderBy(value => value.Building)
                .ThenBy(value => value.AbilityId).ToArray());
    }

    internal void RestoreRuntimeState(
        AbilityRuntimeSnapshot snapshot,
        int unitCount)
    {
        if (snapshot.Units.Length != unitCount || unitCount > _capacity)
            throw new InvalidOperationException("Ability runtime unit mismatch.");
        _catalog = snapshot.Catalog;
        _nextBuffInstanceId = snapshot.NextBuffInstanceId;
        _nextPersistentEffectInstanceId =
            snapshot.NextPersistentEffectInstanceId;
        _nextProjectileId = snapshot.NextProjectileInstanceId;
        _buffs.Clear();
        _buffs.AddRange(snapshot.Buffs);
        _summons.Clear();
        Array.Clear(_summonedObjectIds);
        for (var index = 0; index < snapshot.Summons.Length; index++)
        {
            if ((uint)snapshot.Summons[index].Unit >= (uint)unitCount ||
                string.IsNullOrWhiteSpace(snapshot.Summons[index].ObjectId))
                throw new InvalidOperationException(
                    "Invalid summoned-unit runtime entry.");
            AddSummon(snapshot.Summons[index]);
        }
        _revealSources.Clear();
        _revealSources.AddRange(snapshot.Reveals);
        _persistentEffects.Clear();
        _persistentEffects.AddRange(snapshot.PersistentEffects);
        _unitForms.Clear();
        _unitForms.AddRange(snapshot.UnitForms);
        _projectiles.Clear();
        _projectiles.AddRange(snapshot.Projectiles);
        _buildings.Clear();
        _buildings.AddRange(snapshot.Buildings.Select(value => value with
        {
            AbilityIds = value.AbilityIds.ToArray(),
            Cooldowns = value.Cooldowns.ToArray()
        }));
        _buildingToggles.Clear();
        _buildingToggles.AddRange(snapshot.BuildingToggles);
        _buildingStatuses.Clear();
        _buildingStatuses.AddRange(snapshot.BuildingStatuses);
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
            _traits[unit] = value.Traits;
            _heroLevels[unit] = value.HeroLevel;
            _heroMaximumLevels[unit] = value.HeroMaximumLevel;
            _heroExperience[unit] = value.HeroExperience;
            _experienceBounty[unit] = value.ExperienceBounty;
            _unitLevels[unit] = value.UnitLevel;
            _unspentSkillPoints[unit] = value.UnspentSkillPoints;
            _mana[unit] = value.Mana;
            _maximumMana[unit] = value.MaximumMana;
            _baseMaximumMana[unit] = value.BaseMaximumMana;
            _manaBonusDecayPerSecond[unit] = value.ManaBonusDecayPerSecond;
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
            _pulsesRemaining[unit] = value.PulsesRemaining;
            _targets[unit] = value.Target;
        }
        _combatEventCursor = 0;
    }

    internal void AppendStateHash(ref StableHash64 hash, int unitCount)
    {
        hash.Add((long)_catalog.StableHash);
        hash.Add(_nextBuffInstanceId);
        hash.Add(_nextPersistentEffectInstanceId);
        hash.Add(_nextProjectileId);
        hash.Add(unitCount);
        for (var unit = 0; unit < unitCount; unit++)
        {
            hash.Add(_unitTypeIds[unit]);
            hash.Add(_heroes[unit]);
            hash.Add((byte)_traits[unit]);
            hash.Add(_heroLevels[unit]);
            hash.Add(_heroMaximumLevels[unit]);
            hash.Add(_heroExperience[unit]);
            hash.Add(_experienceBounty[unit]);
            hash.Add(_unitLevels[unit]);
            hash.Add(_unspentSkillPoints[unit]);
            hash.Add(_mana[unit]);
            hash.Add(_maximumMana[unit]);
            hash.Add(_baseMaximumMana[unit]);
            hash.Add(_manaBonusDecayPerSecond[unit]);
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
            hash.Add(_pulsesRemaining[unit]);
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
            hash.Add(StableStringHash(buff.BuffId));
            hash.Add(buff.RemainingSeconds);
            hash.Add(buff.Beneficial);
            hash.Add((byte)buff.Polarity);
            hash.Add((byte)buff.DispelKind);
            hash.Add((byte)buff.Stacking);
            hash.Add((int)buff.Status);
            AppendModifier(ref hash, buff.Modifier);
            AppendCombatModifier(ref hash, buff.CombatModifier);
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
        hash.Add(_persistentEffects.Count);
        foreach (var effect in _persistentEffects
                     .OrderBy(value => value.InstanceId))
        {
            hash.Add(effect.InstanceId);
            hash.Add(effect.AbilityId);
            hash.Add(effect.SourceUnit);
            hash.Add(effect.Level);
            hash.Add(effect.EffectIndex);
            hash.Add((byte)effect.Target.Kind);
            hash.Add(effect.Target.Id);
            hash.Add(effect.Target.Position);
            hash.Add(effect.RemainingSeconds);
            hash.Add(effect.StartDelayRemaining);
            hash.Add(effect.PulseRemaining);
            hash.Add(effect.PulsesRemaining);
        }
        hash.Add(_unitForms.Count);
        foreach (var form in _unitForms.OrderBy(value => value.Unit))
        {
            hash.Add(form.Unit);
            AppendUnitForm(ref hash, form.Profile);
            hash.Add((byte)form.Phase);
            hash.Add(form.TargetBuilding);
            hash.Add(form.RemainingSeconds);
        }
        hash.Add(_projectiles.Count);
        foreach (var projectile in _projectiles.OrderBy(value => value.Id))
        {
            hash.Add(projectile.Id);
            hash.Add(projectile.AbilityId);
            hash.Add(projectile.CasterUnit);
            hash.Add(projectile.Level);
            hash.Add((byte)projectile.Target.Kind);
            hash.Add(projectile.Target.Id);
            hash.Add(projectile.Target.Position);
            hash.Add(projectile.Origin);
            hash.Add(projectile.Position);
            hash.Add(projectile.Destination);
            hash.Add(projectile.Speed);
            hash.Add(projectile.Homing);
        }
        hash.Add(_buildings.Count);
        foreach (var building in _buildings.OrderBy(value => value.Building))
        {
            hash.Add(building.Building);
            hash.Add(building.BuildingTypeId);
            hash.Add(building.Mana);
            hash.Add(building.MaximumMana);
            hash.Add(building.ManaRegeneration);
            hash.Add(building.AbilityIds.Length);
            for (var slot = 0; slot < building.AbilityIds.Length; slot++)
            {
                hash.Add(building.AbilityIds[slot]);
                hash.Add(building.Cooldowns[slot]);
            }
        }
        hash.Add(_buildingToggles.Count);
        foreach (var toggle in _buildingToggles
                     .OrderBy(value => value.Building)
                     .ThenBy(value => value.AbilityId))
        {
            hash.Add(toggle.Building);
            hash.Add(toggle.AbilityId);
        }
        hash.Add(_buildingStatuses.Count);
        foreach (var status in _buildingStatuses
                     .OrderBy(value => value.Building)
                     .ThenBy(value => value.AbilityId))
        {
            hash.Add(status.Building);
            hash.Add(status.AbilityId);
            hash.Add(status.SourceUnit);
            hash.Add(status.RemainingSeconds);
            hash.Add((ushort)status.Status);
        }
    }

    private static void AppendUnitForm(
        ref StableHash64 hash,
        in AbilityUnitFormProfile form)
    {
        AppendUnitType(ref hash, form.Normal);
        AppendUnitType(ref hash, form.Alternate);
        hash.Add(form.AlternateDurationSeconds);
        hash.Add((byte)form.RequiredBuildingFunction);
    }

    private static void AppendUnitType(
        ref StableHash64 hash,
        in UnitTypeProfile profile)
    {
        hash.Add(profile.Id);
        hash.Add(StableStringHash(profile.Name));
        hash.Add(profile.Movement.Id);
        hash.Add(StableStringHash(profile.Movement.Name));
        hash.Add(profile.Movement.PhysicalRadius);
        hash.Add(profile.Movement.MaximumSpeed);
        hash.Add(profile.Movement.Acceleration);
        hash.Add((byte)profile.Movement.MovementClass);
        hash.Add(profile.Movement.NavigationRadius);
        hash.Add(profile.Movement.TurnRateRadiansPerSecond);
        hash.Add(profile.Combat.MaximumHealth);
        hash.Add(profile.Combat.AttackDamage);
        hash.Add(profile.Combat.AttackRange);
        hash.Add(profile.Combat.AcquisitionRange);
        hash.Add(profile.Combat.AttackCooldownSeconds);
        hash.Add(profile.Combat.AttackWindupSeconds);
        hash.Add(profile.Combat.LeashDistance);
        hash.Add((byte)profile.Combat.Positioning);
        hash.Add(profile.Combat.Armor);
        hash.Add((ushort)profile.Combat.Attributes);
        hash.Add(profile.Combat.AttacksPerVolley);
        hash.Add((ushort)profile.Combat.BonusVs);
        hash.Add(profile.Combat.BonusDamage);
        hash.Add(profile.Combat.BaseUpgradeDamage);
        hash.Add(profile.Combat.BonusUpgradeDamage);
        hash.Add(profile.Combat.ProjectileSpeed);
        hash.Add(profile.Combat.CanMoveDuringWindup);
        hash.Add(profile.Combat.CanMoveDuringCooldown);
        hash.Add(profile.Combat.AutoTargetPriority);
        hash.Add((byte)profile.Combat.ArmorType);
        hash.Add(profile.Combat.ArmorUpgradeTechnologyId);
        hash.Add(profile.Combat.ArmorUpgradePerLevel);
        hash.Add(profile.Combat.AttackHalfAngleRadians);
        hash.Add(profile.Combat.Weapons.Length);
        foreach (var weapon in profile.Combat.Weapons)
        {
            hash.Add(weapon.Slot);
            hash.Add((byte)weapon.TargetLayers);
            hash.Add(weapon.EnabledByDefault);
            hash.Add(weapon.RequiredTechnologyId);
            hash.Add(weapon.AttackDamage);
            hash.Add(weapon.AttackRange);
            hash.Add(weapon.AttackCooldownSeconds);
            hash.Add(weapon.AttackWindupSeconds);
            hash.Add((byte)weapon.Positioning);
            hash.Add(weapon.AttacksPerVolley);
            hash.Add((ushort)weapon.BonusVs);
            hash.Add(weapon.BonusDamage);
            hash.Add(weapon.BaseUpgradeDamage);
            hash.Add(weapon.BonusUpgradeDamage);
            hash.Add(weapon.ProjectileSpeed);
            hash.Add(weapon.CanMoveDuringWindup);
            hash.Add(weapon.CanMoveDuringCooldown);
            hash.Add((byte)weapon.AttackType);
            hash.Add(weapon.DamageUpgradeTechnologyId);
            hash.Add(weapon.MinimumRange);
            hash.Add(weapon.Area.FullDamageRadius);
            hash.Add(weapon.Area.HalfDamageRadius);
            hash.Add(weapon.Area.QuarterDamageRadius);
            hash.Add((byte)weapon.Area.TargetLayers);
            hash.Add((byte)weapon.Propagation.Kind);
            hash.Add(weapon.Propagation.LineDistance);
            hash.Add(weapon.Propagation.Radius);
            hash.Add(weapon.Propagation.DamageLossFactor);
            hash.Add(weapon.Propagation.MaximumTargets);
            hash.Add((byte)weapon.Propagation.TargetLayers);
            hash.Add(weapon.Propagation.DistanceUpgradeTechnologyId);
            hash.Add(weapon.Propagation.DistanceUpgradePerLevel);
        }
        hash.Add(profile.IsWorker);
        hash.Add((byte)profile.Perception.Concealment);
        hash.Add(profile.Perception.DetectionRange);
        hash.Add(profile.Perception.VisionRange);
        hash.Add(profile.Perception.ObservationHeight);
        hash.Add((byte)profile.Perception.TerrainVisionMode);
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
        hash.Add(modifier.HealthRegenerationAdd);
    }

    private static void AppendCombatModifier(
        ref StableHash64 hash,
        AbilityCombatModifier modifier)
    {
        modifier = modifier.Normalized;
        hash.Add(modifier.DamageTakenMultiplier);
        hash.Add(modifier.DamageDealtMultiplier);
        hash.Add(modifier.MagicDamageTakenMultiplier);
        hash.Add(modifier.DeflectChancePercent);
        hash.Add(modifier.DeflectedPiercingDamageMultiplier);
        hash.Add(modifier.DeflectedMagicDamageMultiplier);
        hash.Add(modifier.AttackMissChancePercent);
        hash.Add(modifier.EvasionChancePercent);
        hash.Add(modifier.CriticalStrikeChancePercent);
        hash.Add(modifier.CriticalStrikeDamageMultiplier);
        hash.Add(modifier.CriticalStrikeBonusDamage);
        hash.Add(modifier.CriticalStrikeNeverMiss);
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
        writer.Write(snapshot.NextPersistentEffectInstanceId);
        writer.Write(snapshot.NextProjectileInstanceId);
        writer.Write(snapshot.Units.Length);
        foreach (var unit in snapshot.Units) WriteRuntimeUnit(writer, unit);
        writer.Write(snapshot.Buffs.Length);
        foreach (var buff in snapshot.Buffs)
        {
            writer.Write(buff.InstanceId);
            writer.Write(buff.AbilityId);
            writer.Write(buff.SourceUnit);
            writer.Write(buff.TargetUnit);
            WriteRuntimeString(writer, buff.BuffId);
            writer.Write(buff.RemainingSeconds);
            writer.Write(buff.Beneficial);
            writer.Write((byte)buff.Polarity);
            writer.Write((byte)buff.DispelKind);
            writer.Write((byte)buff.Stacking);
            writer.Write((ushort)buff.Status);
            WriteRuntimeModifier(writer, buff.Modifier);
            WriteRuntimeCombatModifier(writer, buff.CombatModifier);
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
        writer.Write(snapshot.PersistentEffects.Length);
        foreach (var effect in snapshot.PersistentEffects)
        {
            writer.Write(effect.InstanceId);
            writer.Write(effect.AbilityId);
            writer.Write(effect.SourceUnit);
            writer.Write(effect.Level);
            writer.Write(effect.EffectIndex);
            writer.Write((byte)effect.Target.Kind);
            writer.Write(effect.Target.Id);
            writer.Write(effect.Target.Position.X);
            writer.Write(effect.Target.Position.Y);
            writer.Write(effect.RemainingSeconds);
            writer.Write(effect.StartDelayRemaining);
            writer.Write(effect.PulseRemaining);
            writer.Write(effect.PulsesRemaining);
        }
        writer.Write(snapshot.UnitForms.Length);
        foreach (var form in snapshot.UnitForms)
        {
            writer.Write(form.Unit);
            WriteUnitForm(writer, form.Profile);
            writer.Write((byte)form.Phase);
            writer.Write(form.TargetBuilding);
            writer.Write(form.RemainingSeconds);
        }
        writer.Write(snapshot.Projectiles.Length);
        foreach (var projectile in snapshot.Projectiles)
        {
            writer.Write(projectile.Id);
            writer.Write(projectile.AbilityId);
            writer.Write(projectile.CasterUnit);
            writer.Write(projectile.Level);
            writer.Write((byte)projectile.Target.Kind);
            writer.Write(projectile.Target.Id);
            writer.Write(projectile.Target.Position.X);
            writer.Write(projectile.Target.Position.Y);
            writer.Write(projectile.Origin.X);
            writer.Write(projectile.Origin.Y);
            writer.Write(projectile.Position.X);
            writer.Write(projectile.Position.Y);
            writer.Write(projectile.Destination.X);
            writer.Write(projectile.Destination.Y);
            writer.Write(projectile.Speed);
            writer.Write(projectile.Homing);
        }
        writer.Write(snapshot.Buildings.Length);
        foreach (var building in snapshot.Buildings)
        {
            writer.Write(building.Building);
            writer.Write(building.BuildingTypeId);
            writer.Write(building.Mana);
            writer.Write(building.MaximumMana);
            writer.Write(building.ManaRegeneration);
            writer.Write(building.AbilityIds.Length);
            for (var slot = 0; slot < building.AbilityIds.Length; slot++)
            {
                writer.Write(building.AbilityIds[slot]);
                writer.Write(building.Cooldowns[slot]);
            }
        }
        writer.Write(snapshot.BuildingToggles.Length);
        foreach (var toggle in snapshot.BuildingToggles)
        {
            writer.Write(toggle.Building);
            writer.Write(toggle.AbilityId);
        }
        writer.Write(snapshot.BuildingStatuses.Length);
        foreach (var status in snapshot.BuildingStatuses)
        {
            writer.Write(status.Building);
            writer.Write(status.AbilityId);
            writer.Write(status.SourceUnit);
            writer.Write(status.RemainingSeconds);
            writer.Write((ushort)status.Status);
        }
    }

    public static AbilityRuntimeSnapshot ReadRuntime(
        BinaryReader reader,
        int unitCount)
    {
        var catalog = ReadCatalog(reader);
        var nextBuff = reader.ReadInt32();
        var nextPersistentEffect = reader.ReadInt32();
        var nextProjectile = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (nextBuff <= 0 || nextPersistentEffect <= 0 ||
            nextProjectile <= 0 || count != unitCount)
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
                reader.ReadInt32(), ReadRuntimeString(reader),
                reader.ReadSingle(), reader.ReadBoolean(),
                (AbilityBuffPolarity)reader.ReadByte(),
                (AbilityBuffDispelKind)reader.ReadByte(),
                (AbilityBuffStackingKind)reader.ReadByte(),
                (AbilityStatusFlags)reader.ReadUInt16(),
                ReadRuntimeModifier(reader),
                ReadRuntimeCombatModifier(reader));
            if (buff.InstanceId <= previousBuff ||
                (uint)buff.AbilityId >= (uint)catalog.Count ||
                (uint)buff.SourceUnit >= (uint)unitCount ||
                (uint)buff.TargetUnit >= (uint)unitCount ||
                string.IsNullOrWhiteSpace(buff.BuffId) ||
                buff.BuffId.Length > 64 ||
                !Enum.IsDefined(buff.Polarity) ||
                (buff.DispelKind & ~AbilityBuffDispelKind.Both) != 0 ||
                !Enum.IsDefined(buff.Stacking) ||
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
                (!float.IsFinite(summon.RemainingSeconds) &&
                 !float.IsPositiveInfinity(summon.RemainingSeconds)) ||
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
        var persistentCount = reader.ReadInt32();
        if (persistentCount is < 0 or > 100_000)
            throw new InvalidDataException();
        var persistent = new AbilityPersistentEffectRuntimeEntry[persistentCount];
        var previousPersistentId = 0;
        for (var index = 0; index < persistentCount; index++)
        {
            var effect = new AbilityPersistentEffectRuntimeEntry(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(),
                new AbilityCastTarget(
                    (AbilityTargetKind)reader.ReadByte(), reader.ReadInt32(),
                    new Vector2(reader.ReadSingle(), reader.ReadSingle())),
                reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadInt32());
            if (effect.InstanceId <= previousPersistentId ||
                effect.InstanceId >= nextPersistentEffect ||
                (uint)effect.AbilityId >= (uint)catalog.Count ||
                (uint)effect.SourceUnit >= (uint)unitCount ||
                effect.Level <= 0 ||
                effect.Level > catalog.Ability(effect.AbilityId).Levels.Length ||
                (uint)effect.EffectIndex >= (uint)catalog
                    .Ability(effect.AbilityId).Levels[effect.Level - 1]
                    .Effects.Length ||
                catalog.Ability(effect.AbilityId).Levels[effect.Level - 1]
                    .Effects[effect.EffectIndex].Timing !=
                    AbilityEffectTiming.PersistentPulse ||
                !Enum.IsDefined(effect.Target.Kind) ||
                !float.IsFinite(effect.RemainingSeconds) ||
                effect.RemainingSeconds <= 0f ||
                !float.IsFinite(effect.StartDelayRemaining) ||
                effect.StartDelayRemaining < 0f ||
                !float.IsFinite(effect.PulseRemaining) ||
                effect.PulsesRemaining <= 0)
                throw new InvalidDataException(
                    "Invalid persistent ability effect.");
            persistent[index] = effect;
            previousPersistentId = effect.InstanceId;
        }
        var formCount = reader.ReadInt32();
        if (formCount is < 0 || formCount > unitCount)
            throw new InvalidDataException();
        var forms = new AbilityUnitFormRuntimeEntry[formCount];
        var previousFormUnit = -1;
        for (var index = 0; index < formCount; index++)
        {
            var form = new AbilityUnitFormRuntimeEntry(
                reader.ReadInt32(), ReadUnitForm(reader),
                (AbilityUnitFormPhase)reader.ReadByte(),
                reader.ReadInt32(), reader.ReadSingle());
            if (form.Unit <= previousFormUnit ||
                (uint)form.Unit >= (uint)unitCount ||
                !form.Profile.IsValid || !Enum.IsDefined(form.Phase) ||
                form.TargetBuilding < -1 ||
                !float.IsFinite(form.RemainingSeconds) ||
                form.RemainingSeconds < 0f ||
                form.Phase == AbilityUnitFormPhase.Alternate &&
                    form.RemainingSeconds <= 0f)
                throw new InvalidDataException("Invalid ability unit form.");
            forms[index] = form;
            previousFormUnit = form.Unit;
        }
        var projectileCount = reader.ReadInt32();
        if (projectileCount is < 0 or > 100_000)
            throw new InvalidDataException();
        var projectiles = new AbilityProjectileSnapshot[projectileCount];
        var previousProjectileId = 0;
        for (var index = 0; index < projectileCount; index++)
        {
            var projectile = new AbilityProjectileSnapshot(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(),
                new AbilityCastTarget(
                    (AbilityTargetKind)reader.ReadByte(), reader.ReadInt32(),
                    new Vector2(reader.ReadSingle(), reader.ReadSingle())),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                reader.ReadSingle(), reader.ReadBoolean());
            var projectileProfile =
                (uint)projectile.AbilityId < (uint)catalog.Count
                    ? catalog.Ability(projectile.AbilityId).Projectile
                    : default;
            if (projectile.Id <= previousProjectileId ||
                projectile.Id >= nextProjectile ||
                (uint)projectile.AbilityId >= (uint)catalog.Count ||
                (uint)projectile.CasterUnit >= (uint)unitCount ||
                projectile.Level <= 0 ||
                projectile.Level > catalog.Ability(projectile.AbilityId).Levels.Length ||
                !projectileProfile.Enabled ||
                projectile.Speed != projectileProfile.Speed ||
                projectile.Homing != projectileProfile.Homing ||
                !Enum.IsDefined(projectile.Target.Kind) ||
                projectile.Target.Kind == AbilityTargetKind.None ||
                !float.IsFinite(projectile.Target.Position.X) ||
                !float.IsFinite(projectile.Target.Position.Y) ||
                (projectile.Target.Kind is AbilityTargetKind.Unit or
                    AbilityTargetKind.Self) &&
                    (uint)projectile.Target.Id >= (uint)unitCount ||
                projectile.Target.Kind == AbilityTargetKind.Building &&
                    projectile.Target.Id < 0 ||
                !float.IsFinite(projectile.Origin.X) ||
                !float.IsFinite(projectile.Origin.Y) ||
                !float.IsFinite(projectile.Position.X) ||
                !float.IsFinite(projectile.Position.Y) ||
                !float.IsFinite(projectile.Destination.X) ||
                !float.IsFinite(projectile.Destination.Y) ||
                !float.IsFinite(projectile.Speed) || projectile.Speed <= 0f)
                throw new InvalidDataException(
                    "Invalid ability projectile runtime.");
            projectiles[index] = projectile;
            previousProjectileId = projectile.Id;
        }
        var buildingCount = reader.ReadInt32();
        if (buildingCount is < 0 or > 100_000)
            throw new InvalidDataException();
        var buildings = new AbilityBuildingRuntimeEntry[buildingCount];
        var previousRuntimeBuilding = -1;
        for (var index = 0; index < buildingCount; index++)
        {
            var building = reader.ReadInt32();
            var buildingTypeId = reader.ReadInt32();
            var mana = reader.ReadSingle();
            var maximumMana = reader.ReadSingle();
            var manaRegeneration = reader.ReadSingle();
            var abilityCount = reader.ReadInt32();
            if (abilityCount is < 0 or > 32)
                throw new InvalidDataException(
                    "Invalid building ability runtime slot count.");
            var abilityIds = new int[abilityCount];
            var cooldowns = new float[abilityCount];
            for (var slot = 0; slot < abilityCount; slot++)
            {
                abilityIds[slot] = reader.ReadInt32();
                cooldowns[slot] = reader.ReadSingle();
            }
            var runtime = new AbilityBuildingRuntimeEntry(
                building, buildingTypeId, mana, maximumMana,
                manaRegeneration, abilityIds, cooldowns);
            if (runtime.Building <= previousRuntimeBuilding ||
                runtime.BuildingTypeId < 0 ||
                !float.IsFinite(runtime.Mana) || runtime.Mana < 0f ||
                !float.IsFinite(runtime.MaximumMana) ||
                runtime.MaximumMana < 0f ||
                runtime.Mana > runtime.MaximumMana ||
                !float.IsFinite(runtime.ManaRegeneration) ||
                runtime.ManaRegeneration < 0f ||
                !catalog.TryBuildingBinding(
                    runtime.BuildingTypeId, out var binding) ||
                runtime.AbilityIds.Length != binding.Abilities.Length ||
                !runtime.AbilityIds.SequenceEqual(binding.Abilities) ||
                runtime.Cooldowns.Any(value =>
                    !float.IsFinite(value) || value < 0f))
                throw new InvalidDataException(
                    "Invalid building ability runtime.");
            buildings[index] = runtime;
            previousRuntimeBuilding = runtime.Building;
        }
        var buildingToggleCount = reader.ReadInt32();
        if (buildingToggleCount is < 0 or > 100_000)
            throw new InvalidDataException();
        var buildingToggles =
            new AbilityBuildingToggleRuntimeEntry[buildingToggleCount];
        var previousBuilding = -1;
        var previousAbility = -1;
        for (var index = 0; index < buildingToggleCount; index++)
        {
            var toggle = new AbilityBuildingToggleRuntimeEntry(
                reader.ReadInt32(), reader.ReadInt32());
            if (toggle.Building < 0 ||
                (uint)toggle.AbilityId >= (uint)catalog.Count ||
                toggle.Building < previousBuilding ||
                toggle.Building == previousBuilding &&
                toggle.AbilityId <= previousAbility)
                throw new InvalidDataException(
                    "Invalid building ability toggle.");
            buildingToggles[index] = toggle;
            previousBuilding = toggle.Building;
            previousAbility = toggle.AbilityId;
        }
        var buildingStatusCount = reader.ReadInt32();
        if (buildingStatusCount is < 0 or > 100_000)
            throw new InvalidDataException();
        var buildingStatuses =
            new AbilityBuildingStatusRuntimeEntry[buildingStatusCount];
        previousBuilding = -1;
        previousAbility = -1;
        for (var index = 0; index < buildingStatusCount; index++)
        {
            var status = new AbilityBuildingStatusRuntimeEntry(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadSingle(),
                (AbilityStatusFlags)reader.ReadUInt16());
            if (status.Building < 0 ||
                (uint)status.AbilityId >= (uint)catalog.Count ||
                status.SourceUnit < 0 || status.SourceUnit >= unitCount ||
                !float.IsFinite(status.RemainingSeconds) ||
                status.RemainingSeconds <= 0f ||
                status.Status == AbilityStatusFlags.None ||
                status.Building < previousBuilding ||
                status.Building == previousBuilding &&
                status.AbilityId <= previousAbility)
                throw new InvalidDataException(
                    "Invalid building ability status.");
            buildingStatuses[index] = status;
            previousBuilding = status.Building;
            previousAbility = status.AbilityId;
        }
        return new AbilityRuntimeSnapshot(
            catalog, nextBuff, nextPersistentEffect, nextProjectile,
            units, buffs, summons, reveals, persistent, forms, projectiles,
            buildings, buildingToggles, buildingStatuses);
    }

    private static void WriteRuntimeUnit(
        BinaryWriter writer,
        AbilityUnitRuntimeEntry value)
    {
        writer.Write(value.Unit);
        writer.Write(value.UnitTypeId);
        writer.Write(value.Hero);
        writer.Write((byte)value.Traits);
        writer.Write(value.HeroLevel);
        writer.Write(value.HeroMaximumLevel);
        writer.Write(value.HeroExperience);
        writer.Write(value.ExperienceBounty);
        writer.Write(value.UnitLevel);
        writer.Write(value.UnspentSkillPoints);
        writer.Write(value.Mana);
        writer.Write(value.MaximumMana);
        writer.Write(value.BaseMaximumMana);
        writer.Write(value.ManaBonusDecayPerSecond);
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
        writer.Write(value.PulsesRemaining);
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
        var traits = (AbilityUnitTraits)reader.ReadByte();
        var heroLevel = reader.ReadInt32();
        var heroMaximumLevel = reader.ReadInt32();
        var heroExperience = reader.ReadInt32();
        var experienceBounty = reader.ReadInt32();
        var unitLevel = reader.ReadInt32();
        var unspentSkillPoints = reader.ReadInt32();
        var mana = reader.ReadSingle();
        var maximumMana = reader.ReadSingle();
        var baseMaximumMana = reader.ReadSingle();
        var manaBonusDecay = reader.ReadSingle();
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
            (traits & ~AbilityUnitTraits.All) != 0 ||
            hero && (heroLevel <= 0 || heroMaximumLevel < heroLevel ||
                     heroMaximumLevel > 100 || heroExperience < 0 ||
                     heroExperience <
                     AbilitySystem.ExperienceRequiredForLevel(heroLevel) ||
                     heroExperience >
                     AbilitySystem.ExperienceRequiredForLevel(
                         heroMaximumLevel) ||
                     unspentSkillPoints < 0) ||
            !hero && (heroLevel != 0 || heroMaximumLevel != 0 ||
                      heroExperience != 0 || unspentSkillPoints != 0) ||
            experienceBounty is < 0 or > 1_000_000 ||
            unitLevel is < 0 or > 100 ||
            !float.IsFinite(mana) || !float.IsFinite(maximumMana) ||
            !float.IsFinite(baseMaximumMana) ||
            !float.IsFinite(manaBonusDecay) ||
            mana < 0f || mana > maximumMana || maximumMana < 0f ||
            baseMaximumMana < 0f || baseMaximumMana > maximumMana ||
            manaBonusDecay < 0f)
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
        var pulsesRemaining = reader.ReadInt32();
        var target = new AbilityCastTarget(
            (AbilityTargetKind)reader.ReadByte(), reader.ReadInt32(),
            new Vector2(reader.ReadSingle(), reader.ReadSingle()));
        if (!Enum.IsDefined(castPhase) ||
            castPhase == AbilityCastPhase.None && activeSlot != -1 ||
            castPhase != AbilityCastPhase.None &&
                (uint)activeSlot >= (uint)slotCount ||
            pulsesRemaining < 0 ||
            !Enum.IsDefined(target.Kind))
            throw new InvalidDataException("Invalid active ability cast.");
        return new AbilityUnitRuntimeEntry(
            unit, unitType, hero, traits, heroLevel, heroMaximumLevel,
            heroExperience, experienceBounty, unitLevel, unspentSkillPoints,
            mana, maximumMana, baseMaximumMana, manaBonusDecay, regeneration,
            effectiveRegeneration, speed,
            damage, armor, attackCooldown, maximumHealth, detection,
            concealment, concealmentPhase, ids, levels, cooldowns, toggles,
            autoCast, castPhase, activeSlot, castRemaining,
            channelRemaining, pulseRemaining, pulsesRemaining, target);
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
        writer.Write(value.HealthRegenerationAdd);
    }

    private static AbilityStatModifier ReadRuntimeModifier(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void WriteRuntimeCombatModifier(
        BinaryWriter writer,
        AbilityCombatModifier value)
    {
        value = value.Normalized;
        writer.Write(value.DamageTakenMultiplier);
        writer.Write(value.DamageDealtMultiplier);
        writer.Write(value.MagicDamageTakenMultiplier);
        writer.Write(value.DeflectChancePercent);
        writer.Write(value.DeflectedPiercingDamageMultiplier);
        writer.Write(value.DeflectedMagicDamageMultiplier);
        writer.Write(value.AttackMissChancePercent);
        writer.Write(value.EvasionChancePercent);
        writer.Write(value.CriticalStrikeChancePercent);
        writer.Write(value.CriticalStrikeDamageMultiplier);
        writer.Write(value.CriticalStrikeBonusDamage);
        writer.Write(value.CriticalStrikeNeverMiss);
    }

    private static AbilityCombatModifier ReadRuntimeCombatModifier(
        BinaryReader reader) => new(
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadBoolean());

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
