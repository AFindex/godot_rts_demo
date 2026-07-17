using System.Collections.Immutable;
using System.Numerics;
using System.Text;

namespace RtsDemo.Simulation;

public enum AbilityActivationKind : byte
{
    Passive,
    Instant,
    TargetUnit,
    TargetPoint,
    Toggle,
    ChannelUnit,
    ChannelPoint
}

[Flags]
public enum AbilityTargetFlags : uint
{
    None = 0,
    Self = 1 << 0,
    Unit = 1 << 1,
    Building = 1 << 2,
    Point = 1 << 3,
    Friendly = 1 << 4,
    Enemy = 1 << 5,
    Neutral = 1 << 6,
    Alive = 1 << 7,
    Dead = 1 << 8,
    Ground = 1 << 9,
    Air = 1 << 10,
    Organic = 1 << 11,
    Mechanical = 1 << 12,
    Hero = 1 << 13,
    NonHero = 1 << 14,
    Vulnerable = 1 << 15,
    Invulnerable = 1 << 16,
    Ward = 1 << 17,
    Tree = 1 << 18,
    Debris = 1 << 19,
    PlayerControlled = 1 << 20,
    NotSelf = 1 << 21,
    Ancient = 1 << 22,
    NonAncient = 1 << 23,
    NonSapper = 1 << 24,
    Bridge = 1 << 25,
    Item = 1 << 26,
    Wall = 1 << 27
}

public enum AbilityEffectKind : byte
{
    Heal,
    Damage,
    Mana,
    ApplyStatus,
    Dispel,
    Reveal,
    Summon,
    Teleport,
    Revive,
    TransferControl,
    TransferBuff,
    ToggleStatus,
    TransferMana,
    TransformUnit
}

public enum AbilityEffectTiming : byte
{
    Impact,
    ChannelPulse,
    Aura,
    AttackHit,
    PersistentPulse
}

public enum AbilityEffectSelector : byte
{
    Caster,
    Primary,
    AreaAtTarget,
    AreaAtCaster
}

/// <summary>
/// Warcraft distinguishes spell damage from physical attack-derived damage
/// and universal damage. None is valid for non-damaging effects only.
/// </summary>
public enum AbilityDamageKind : byte
{
    None,
    Physical,
    Magic,
    Universal
}

public enum AbilityBuffPolarity : byte
{
    Neutral,
    Beneficial,
    Harmful
}

[Flags]
public enum AbilityBuffDispelKind : byte
{
    None = 0,
    Magic = 1 << 0,
    Physical = 1 << 1,
    Both = Magic | Physical
}

public enum AbilityBuffStackingKind : byte
{
    Refresh,
    Replace,
    Stack
}

[Flags]
public enum AbilityRelationFilter : byte
{
    None = 0,
    Self = 1 << 0,
    Friendly = 1 << 1,
    Enemy = 1 << 2,
    Neutral = 1 << 3,
    Any = Self | Friendly | Enemy | Neutral
}

[Flags]
public enum AbilityStatusFlags : ushort
{
    None = 0,
    Stunned = 1 << 0,
    Invulnerable = 1 << 1,
    MagicImmune = 1 << 2,
    Invisible = 1 << 3,
    Polymorphed = 1 << 4,
    Banished = 1 << 5,
    AttackDisabled = 1 << 6,
    MovementDisabled = 1 << 7
}

public readonly record struct AbilityStatModifier(
    float MovementSpeedMultiplier = 1f,
    float AttackCooldownMultiplier = 1f,
    float AttackDamageMultiplier = 1f,
    float AttackDamageAdd = 0f,
    float ArmorAdd = 0f,
    float MaximumHealthAdd = 0f,
    float ManaRegenerationAdd = 0f,
    float DetectionRangeAdd = 0f)
{
    public static AbilityStatModifier Identity => new(1f, 1f, 1f);

    public AbilityStatModifier Normalized => this == default ? Identity : this;

    public bool IsIdentity => Normalized == Identity;

    public bool IsValid =>
        this == default ||
        float.IsFinite(MovementSpeedMultiplier) &&
        MovementSpeedMultiplier is >= 0f and <= 10f &&
        float.IsFinite(AttackCooldownMultiplier) &&
        AttackCooldownMultiplier is >= 0.05f and <= 10f &&
        float.IsFinite(AttackDamageMultiplier) &&
        AttackDamageMultiplier is >= 0f and <= 10f &&
        float.IsFinite(AttackDamageAdd) &&
        float.IsFinite(ArmorAdd) &&
        float.IsFinite(MaximumHealthAdd) &&
        float.IsFinite(ManaRegenerationAdd) &&
        float.IsFinite(DetectionRangeAdd);
}

public readonly record struct AbilitySummonProfile(
    string ObjectId,
    UnitMovementProfileSnapshot Movement,
    CombatProfileSnapshot Combat,
    UnitPerceptionProfileSnapshot Perception,
    float LifetimeSeconds)
{
    public bool IsValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ObjectId) ||
                !float.IsFinite(LifetimeSeconds) || LifetimeSeconds <= 0f)
                return false;
            try
            {
                Combat.Validate();
                Perception.Validate();
                return Movement.PhysicalRadius > 0f &&
                       Movement.MaximumSpeed > 0f &&
                       Movement.Acceleration > 0f;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }
}

/// <summary>
/// Complete, content-neutral description of a reversible unit form. Both
/// profiles live in the ability catalog so replay and hot-snapshot restoration
/// never need to reopen Warcraft JSON or a presentation-side catalog.
/// </summary>
public readonly record struct AbilityUnitFormProfile(
    UnitTypeProfile Normal,
    UnitTypeProfile Alternate,
    float AlternateDurationSeconds,
    BuildingFunctionKind RequiredBuildingFunction)
{
    public bool IsValid
    {
        get
        {
            if (Normal.Id < 0 || Alternate.Id < 0 || Normal.Id == Alternate.Id ||
                !float.IsFinite(AlternateDurationSeconds) ||
                AlternateDurationSeconds <= 0f ||
                !Enum.IsDefined(RequiredBuildingFunction))
                return false;
            try
            {
                Normal.Combat.Validate();
                Alternate.Combat.Validate();
                Normal.Perception.Validate();
                Alternate.Perception.Validate();
                return !string.IsNullOrWhiteSpace(Normal.Name) &&
                       !string.IsNullOrWhiteSpace(Alternate.Name) &&
                       ValidMovement(Normal.Movement) &&
                       ValidMovement(Alternate.Movement);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }

    private static bool ValidMovement(UnitMovementProfileSnapshot value) =>
        value.Id >= 0 && !string.IsNullOrWhiteSpace(value.Name) &&
        float.IsFinite(value.PhysicalRadius) && value.PhysicalRadius > 0f &&
        float.IsFinite(value.MaximumSpeed) && value.MaximumSpeed > 0f &&
        float.IsFinite(value.Acceleration) && value.Acceleration > 0f &&
        Enum.IsDefined(value.MovementClass) &&
        float.IsFinite(value.NavigationRadius) && value.NavigationRadius > 0f;
}

public readonly record struct AbilityEffectProfile(
    AbilityEffectKind Kind,
    AbilityEffectTiming Timing,
    AbilityEffectSelector Selector,
    AbilityRelationFilter Relations,
    float Value = 0f,
    float SecondaryValue = 0f,
    float Radius = 0f,
    float Duration = 0f,
    float Interval = 0f,
    int MaximumTargets = 0,
    AbilityStatusFlags Status = AbilityStatusFlags.None,
    AbilityStatModifier Modifier = default,
    AbilitySummonProfile Summon = default,
    AbilityDamageKind DamageKind = AbilityDamageKind.None,
    string BuffId = "",
    AbilityBuffPolarity BuffPolarity = AbilityBuffPolarity.Neutral,
    AbilityBuffDispelKind BuffDispelKind = AbilityBuffDispelKind.None,
    AbilityBuffStackingKind BuffStacking = AbilityBuffStackingKind.Refresh,
    float HeroValue = 0f,
    float HeroSecondaryValue = 0f,
    AbilityUnitTraits RequiredUnitTraits = AbilityUnitTraits.None,
    AbilityUnitTraits ExcludedUnitTraits = AbilityUnitTraits.None,
    float InnerRadius = 0f,
    int PulseCount = 0,
    float StartDelay = 0f,
    float MaximumTotalValue = 0f,
    float BuildingValueMultiplier = 0f,
    int VisualCount = 0,
    bool ClusteredPlacement = false,
    AbilityUnitFormProfile UnitForm = default)
{
    public bool IsValid =>
        Enum.IsDefined(Kind) && Enum.IsDefined(Timing) &&
        Enum.IsDefined(Selector) && Relations != AbilityRelationFilter.None &&
        (Relations & ~AbilityRelationFilter.Any) == 0 &&
        float.IsFinite(Value) && float.IsFinite(SecondaryValue) &&
        float.IsFinite(Radius) && Radius >= 0f &&
        float.IsFinite(InnerRadius) && InnerRadius >= 0f &&
        InnerRadius <= Radius &&
        float.IsFinite(Duration) && Duration >= 0f &&
        float.IsFinite(Interval) && Interval >= 0f &&
        MaximumTargets is >= 0 and <= 256 &&
        (Status & ~(AbilityStatusFlags.Stunned |
                    AbilityStatusFlags.Invulnerable |
                    AbilityStatusFlags.MagicImmune |
                    AbilityStatusFlags.Invisible |
                    AbilityStatusFlags.Polymorphed |
                    AbilityStatusFlags.Banished |
                    AbilityStatusFlags.AttackDisabled |
                    AbilityStatusFlags.MovementDisabled)) == 0 &&
        Modifier.IsValid &&
        (Kind != AbilityEffectKind.Summon || Summon.IsValid) &&
        Enum.IsDefined(DamageKind) &&
        (Kind != AbilityEffectKind.Damage ||
         DamageKind != AbilityDamageKind.None) &&
        BuffId is not null && BuffId.Length <= 64 &&
        Enum.IsDefined(BuffPolarity) &&
        (BuffDispelKind & ~AbilityBuffDispelKind.Both) == 0 &&
        Enum.IsDefined(BuffStacking) &&
        float.IsFinite(HeroValue) && float.IsFinite(HeroSecondaryValue) &&
        (RequiredUnitTraits & ~AbilityUnitTraits.All) == 0 &&
        (ExcludedUnitTraits & ~AbilityUnitTraits.All) == 0 &&
        (RequiredUnitTraits & ExcludedUnitTraits) == 0 &&
        PulseCount is >= 0 and <= 100_000 &&
        float.IsFinite(StartDelay) && StartDelay >= 0f &&
        float.IsFinite(MaximumTotalValue) && MaximumTotalValue >= 0f &&
        float.IsFinite(BuildingValueMultiplier) &&
        BuildingValueMultiplier is >= 0f and <= 10f &&
        VisualCount is >= 0 and <= 100_000 &&
        (Timing != AbilityEffectTiming.PersistentPulse ||
         Kind == AbilityEffectKind.Damage && Duration > 0f &&
         Interval > 0f && PulseCount > 0) &&
        (MaximumTotalValue <= 0f || Kind == AbilityEffectKind.Damage) &&
        (BuildingValueMultiplier <= 0f || Kind == AbilityEffectKind.Damage) &&
        (!ClusteredPlacement || Kind == AbilityEffectKind.Teleport) &&
        (Kind != AbilityEffectKind.TransformUnit || UnitForm.IsValid) &&
        (Kind == AbilityEffectKind.TransformUnit || UnitForm == default);
}

public enum AbilityRequirementKind : byte
{
    CompletedBuilding,
    TechnologyLevel
}

public readonly record struct AbilityRequirementProfile(
    AbilityRequirementKind Kind,
    int TargetId,
    int Value)
{
    public bool IsValid =>
        Enum.IsDefined(Kind) && TargetId >= 0 && Value > 0;
}

public readonly record struct AbilityLevelProfile(
    int Level,
    float ManaCost,
    float CooldownSeconds,
    float CastSeconds,
    float ChannelSeconds,
    float Range,
    float Area,
    float Duration,
    float HeroDuration,
    ImmutableArray<AbilityEffectProfile> Effects,
    ImmutableArray<AbilityRequirementProfile> Requirements = default)
{
    public bool IsValid =>
        Level > 0 && float.IsFinite(ManaCost) && ManaCost >= 0f &&
        float.IsFinite(CooldownSeconds) && CooldownSeconds >= 0f &&
        float.IsFinite(CastSeconds) && CastSeconds >= 0f &&
        float.IsFinite(ChannelSeconds) && ChannelSeconds >= 0f &&
        float.IsFinite(Range) && Range >= 0f &&
        float.IsFinite(Area) && Area >= 0f &&
        float.IsFinite(Duration) && Duration >= 0f &&
        float.IsFinite(HeroDuration) && HeroDuration >= 0f &&
        !Effects.IsDefault && Effects.Length <= 32 &&
        Effects.All(value => value.IsValid) &&
        (Requirements.IsDefault || Requirements.Length <= 64 &&
            Requirements.All(value => value.IsValid));
}

public readonly record struct AbilityProfile(
    int Id,
    string RawId,
    string Name,
    string Description,
    string IconPath,
    string Hotkey,
    AbilityActivationKind Activation,
    AbilityTargetFlags Targets,
    bool HeroAbility,
    bool AutoCastDefault,
    ImmutableArray<AbilityLevelProfile> Levels,
    int RequiredHeroLevel = 0,
    int HeroLevelSkip = 0)
{
    public bool IsPassive => Activation == AbilityActivationKind.Passive;

    public bool IsValid =>
        Id >= 0 && !string.IsNullOrWhiteSpace(RawId) && RawId.Length <= 64 &&
        !string.IsNullOrWhiteSpace(Name) &&
        Enum.IsDefined(Activation) &&
        (Targets & ~(AbilityTargetFlags.Self | AbilityTargetFlags.Unit |
                     AbilityTargetFlags.Building | AbilityTargetFlags.Point |
                     AbilityTargetFlags.Friendly | AbilityTargetFlags.Enemy |
                     AbilityTargetFlags.Neutral | AbilityTargetFlags.Alive |
                     AbilityTargetFlags.Dead | AbilityTargetFlags.Ground |
                     AbilityTargetFlags.Air | AbilityTargetFlags.Organic |
                     AbilityTargetFlags.Mechanical | AbilityTargetFlags.Hero |
                     AbilityTargetFlags.NonHero | AbilityTargetFlags.Vulnerable |
                     AbilityTargetFlags.Invulnerable | AbilityTargetFlags.Ward |
                     AbilityTargetFlags.Tree | AbilityTargetFlags.Debris |
                     AbilityTargetFlags.PlayerControlled |
                     AbilityTargetFlags.NotSelf | AbilityTargetFlags.Ancient |
                     AbilityTargetFlags.NonAncient |
                     AbilityTargetFlags.NonSapper | AbilityTargetFlags.Bridge |
                     AbilityTargetFlags.Item | AbilityTargetFlags.Wall)) == 0 &&
        !Levels.IsDefaultOrEmpty && Levels.Length <= 32 &&
        RequiredHeroLevel >= 0 && HeroLevelSkip >= 0 &&
        Levels.Select((value, index) =>
                value.Level == index + 1 && value.IsValid)
            .All(value => value);
}

public readonly record struct UnitManaProfile(
    float Initial,
    float Maximum,
    float RegenerationPerSecond)
{
    public static UnitManaProfile None => default;

    public bool IsValid =>
        float.IsFinite(Initial) && float.IsFinite(Maximum) &&
        float.IsFinite(RegenerationPerSecond) &&
        Maximum >= 0f && Initial >= 0f && Initial <= Maximum &&
        RegenerationPerSecond >= 0f;
}

public readonly record struct UnitAbilityEntryProfile(
    int AbilityId,
    int Level,
    bool AutoCastEnabled = false);

[Flags]
public enum AbilityUnitTraits : byte
{
    None = 0,
    Ancient = 1 << 0,
    Sapper = 1 << 1,
    Ward = 1 << 2,
    Undead = 1 << 3,
    All = Ancient | Sapper | Ward | Undead
}

public readonly record struct UnitAbilityBindingProfile(
    int UnitTypeId,
    bool Hero,
    UnitManaProfile Mana,
    ImmutableArray<UnitAbilityEntryProfile> Abilities,
    AbilityUnitTraits Traits = AbilityUnitTraits.None,
    int UnitLevel = 1,
    int ExperienceBounty = 0,
    int HeroMaximumLevel = 10)
{
    public bool IsValid(AbilityProfile[] profiles)
    {
        if (UnitTypeId < 0 || !Mana.IsValid || Abilities.IsDefault ||
            (Traits & ~AbilityUnitTraits.All) != 0 ||
            UnitLevel is < 0 or > 100 ||
            ExperienceBounty is < 0 or > 1_000_000 ||
            HeroMaximumLevel is < 1 or > 100 ||
            Abilities.Length > 32)
            return false;
        var seen = new HashSet<int>();
        foreach (var entry in Abilities)
        {
            if ((uint)entry.AbilityId >= (uint)profiles.Length ||
                !seen.Add(entry.AbilityId) || entry.Level < 0 ||
                entry.Level > profiles[entry.AbilityId].Levels.Length ||
                entry.Level == 0 &&
                (!Hero || !profiles[entry.AbilityId].HeroAbility))
                return false;
        }
        return true;
    }
}

/// <summary>
/// Building loadouts deliberately contain only ability IDs. Buildings do not
/// share unit mana, hero-level or auto-cast state; the authoritative runtime
/// stores their toggle state against the concrete GameplayBuildingId.
/// </summary>
public readonly record struct BuildingAbilityBindingProfile(
    int BuildingTypeId,
    ImmutableArray<int> Abilities)
{
    public bool IsValid(AbilityProfile[] profiles)
    {
        if (BuildingTypeId < 0 || Abilities.IsDefault ||
            Abilities.Length > 32)
            return false;
        var seen = new HashSet<int>();
        return Abilities.All(id =>
            (uint)id < (uint)profiles.Length && seen.Add(id));
    }
}

/// <summary>
/// Immutable, deterministic ability definitions and unit-type loadouts. The
/// simulation consumes dense integer IDs; content adapters retain Warcraft
/// rawcodes and presentation strings at this boundary.
/// </summary>
public sealed class AbilityCatalogSnapshot
{
    public const int CurrentFormatVersion = 13;
    private readonly Dictionary<string, int> _rawIds;
    private readonly Dictionary<int, UnitAbilityBindingProfile> _bindings;
    private readonly Dictionary<int, BuildingAbilityBindingProfile>
        _buildingBindings;

    public AbilityCatalogSnapshot(
        AbilityProfile[] abilities,
        UnitAbilityBindingProfile[] bindings,
        BuildingAbilityBindingProfile[]? buildingBindings = null)
    {
        Abilities = abilities.ToArray();
        Bindings = bindings.OrderBy(value => value.UnitTypeId).ToArray();
        BuildingBindings = (buildingBindings ?? [])
            .OrderBy(value => value.BuildingTypeId).ToArray();
        Validate();
        _rawIds = Abilities.ToDictionary(
            value => value.RawId, value => value.Id, StringComparer.Ordinal);
        _bindings = Bindings.ToDictionary(value => value.UnitTypeId);
        _buildingBindings = BuildingBindings.ToDictionary(
            value => value.BuildingTypeId);
        CanonicalBytes = AbilitySerialization.SerializeCatalog(this);
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public AbilityProfile[] Abilities { get; }
    public UnitAbilityBindingProfile[] Bindings { get; }
    public BuildingAbilityBindingProfile[] BuildingBindings { get; }
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");
    public int Count => Abilities.Length;

    public AbilityProfile Ability(int id) => Abilities[id];

    public bool TryFind(string rawId, out AbilityProfile profile)
    {
        if (_rawIds.TryGetValue(rawId, out var id))
        {
            profile = Abilities[id];
            return true;
        }
        profile = default;
        return false;
    }

    public bool TryBinding(
        int unitTypeId,
        out UnitAbilityBindingProfile binding) =>
        _bindings.TryGetValue(unitTypeId, out binding);

    public bool TryBuildingBinding(
        int buildingTypeId,
        out BuildingAbilityBindingProfile binding) =>
        _buildingBindings.TryGetValue(buildingTypeId, out binding);

    public static AbilityCatalogSnapshot Empty { get; } = new([], []);

    private void Validate()
    {
        if (Abilities.Length > 4096 || Bindings.Length > 4096 ||
            BuildingBindings.Length > 4096)
            throw new ArgumentOutOfRangeException(nameof(Abilities));
        var rawIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Abilities.Length; index++)
        {
            if (Abilities[index].Id != index || !Abilities[index].IsValid ||
                !rawIds.Add(Abilities[index].RawId))
                throw new ArgumentException(
                    $"Ability profile {index} is invalid or non-dense.");
        }
        var unitTypes = new HashSet<int>();
        foreach (var binding in Bindings)
        {
            if (!unitTypes.Add(binding.UnitTypeId) ||
                !binding.IsValid(Abilities))
                throw new ArgumentException(
                    $"Ability binding {binding.UnitTypeId} is invalid.");
        }
        var buildingTypes = new HashSet<int>();
        foreach (var binding in BuildingBindings)
        {
            if (!buildingTypes.Add(binding.BuildingTypeId) ||
                !binding.IsValid(Abilities))
                throw new ArgumentException(
                    $"Building ability binding {binding.BuildingTypeId} is invalid.");
        }
    }
}

internal static partial class AbilitySerialization
{
    private const int MaximumStringBytes = 1_048_576;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] SerializeCatalog(AbilityCatalogSnapshot catalog)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteCatalog(writer, catalog);
        writer.Flush();
        return stream.ToArray();
    }

    public static void WriteCatalog(
        BinaryWriter writer,
        AbilityCatalogSnapshot catalog)
    {
        writer.Write(AbilityCatalogSnapshot.CurrentFormatVersion);
        writer.Write(catalog.Abilities.Length);
        foreach (var ability in catalog.Abilities) WriteAbility(writer, ability);
        writer.Write(catalog.Bindings.Length);
        foreach (var binding in catalog.Bindings) WriteBinding(writer, binding);
        writer.Write(catalog.BuildingBindings.Length);
        foreach (var binding in catalog.BuildingBindings)
            WriteBuildingBinding(writer, binding);
    }

    public static AbilityCatalogSnapshot ReadCatalog(BinaryReader reader)
    {
        if (reader.ReadInt32() != AbilityCatalogSnapshot.CurrentFormatVersion)
            throw new InvalidDataException("Unsupported ability catalog format.");
        var abilityCount = ReadCount(reader, 4096);
        var abilities = new AbilityProfile[abilityCount];
        for (var index = 0; index < abilityCount; index++)
            abilities[index] = ReadAbility(reader);
        var bindingCount = ReadCount(reader, 4096);
        var bindings = new UnitAbilityBindingProfile[bindingCount];
        for (var index = 0; index < bindingCount; index++)
            bindings[index] = ReadBinding(reader);
        var buildingBindingCount = ReadCount(reader, 4096);
        var buildingBindings =
            new BuildingAbilityBindingProfile[buildingBindingCount];
        for (var index = 0; index < buildingBindingCount; index++)
            buildingBindings[index] = ReadBuildingBinding(reader);
        return new AbilityCatalogSnapshot(abilities, bindings, buildingBindings);
    }

    private static void WriteAbility(BinaryWriter writer, AbilityProfile value)
    {
        writer.Write(value.Id);
        WriteString(writer, value.RawId);
        WriteString(writer, value.Name);
        WriteString(writer, value.Description);
        WriteString(writer, value.IconPath);
        WriteString(writer, value.Hotkey);
        writer.Write((byte)value.Activation);
        writer.Write((uint)value.Targets);
        writer.Write(value.HeroAbility);
        writer.Write(value.AutoCastDefault);
        writer.Write(value.RequiredHeroLevel);
        writer.Write(value.HeroLevelSkip);
        writer.Write(value.Levels.Length);
        foreach (var level in value.Levels) WriteLevel(writer, level);
    }

    private static AbilityProfile ReadAbility(BinaryReader reader)
    {
        var id = reader.ReadInt32();
        var rawId = ReadString(reader);
        var name = ReadString(reader);
        var description = ReadString(reader);
        var icon = ReadString(reader);
        var hotkey = ReadString(reader);
        var activation = (AbilityActivationKind)reader.ReadByte();
        var targets = (AbilityTargetFlags)reader.ReadUInt32();
        var hero = reader.ReadBoolean();
        var autoCast = reader.ReadBoolean();
        var requiredHeroLevel = reader.ReadInt32();
        var heroLevelSkip = reader.ReadInt32();
        var levelCount = ReadCount(reader, 32);
        var levels = new AbilityLevelProfile[levelCount];
        for (var index = 0; index < levelCount; index++)
            levels[index] = ReadLevel(reader);
        return new AbilityProfile(
            id, rawId, name, description, icon, hotkey, activation, targets,
            hero, autoCast, levels.ToImmutableArray(), requiredHeroLevel,
            heroLevelSkip);
    }

    private static void WriteLevel(BinaryWriter writer, AbilityLevelProfile value)
    {
        writer.Write(value.Level);
        writer.Write(value.ManaCost);
        writer.Write(value.CooldownSeconds);
        writer.Write(value.CastSeconds);
        writer.Write(value.ChannelSeconds);
        writer.Write(value.Range);
        writer.Write(value.Area);
        writer.Write(value.Duration);
        writer.Write(value.HeroDuration);
        var requirements = value.Requirements.IsDefault
            ? ImmutableArray<AbilityRequirementProfile>.Empty
            : value.Requirements;
        writer.Write(requirements.Length);
        foreach (var requirement in requirements)
        {
            writer.Write((byte)requirement.Kind);
            writer.Write(requirement.TargetId);
            writer.Write(requirement.Value);
        }
        writer.Write(value.Effects.Length);
        foreach (var effect in value.Effects) WriteEffect(writer, effect);
    }

    private static AbilityLevelProfile ReadLevel(BinaryReader reader)
    {
        var level = reader.ReadInt32();
        var mana = reader.ReadSingle();
        var cooldown = reader.ReadSingle();
        var cast = reader.ReadSingle();
        var channel = reader.ReadSingle();
        var range = reader.ReadSingle();
        var area = reader.ReadSingle();
        var duration = reader.ReadSingle();
        var heroDuration = reader.ReadSingle();
        var requirementCount = ReadCount(reader, 64);
        var requirements = new AbilityRequirementProfile[requirementCount];
        for (var index = 0; index < requirementCount; index++)
            requirements[index] = new AbilityRequirementProfile(
                (AbilityRequirementKind)reader.ReadByte(),
                reader.ReadInt32(),
                reader.ReadInt32());
        var count = ReadCount(reader, 32);
        var effects = new AbilityEffectProfile[count];
        for (var index = 0; index < count; index++)
            effects[index] = ReadEffect(reader);
        return new AbilityLevelProfile(
            level, mana, cooldown, cast, channel, range, area, duration,
            heroDuration, effects.ToImmutableArray(),
            requirements.ToImmutableArray());
    }

    private static void WriteEffect(BinaryWriter writer, AbilityEffectProfile value)
    {
        writer.Write((byte)value.Kind);
        writer.Write((byte)value.Timing);
        writer.Write((byte)value.Selector);
        writer.Write((byte)value.Relations);
        writer.Write(value.Value);
        writer.Write(value.SecondaryValue);
        writer.Write(value.Radius);
        writer.Write(value.Duration);
        writer.Write(value.Interval);
        writer.Write(value.MaximumTargets);
        writer.Write((ushort)value.Status);
        WriteModifier(writer, value.Modifier);
        var hasSummon = value.Kind == AbilityEffectKind.Summon;
        writer.Write(hasSummon);
        if (hasSummon) WriteSummon(writer, value.Summon);
        writer.Write((byte)value.DamageKind);
        WriteString(writer, value.BuffId);
        writer.Write((byte)value.BuffPolarity);
        writer.Write((byte)value.BuffDispelKind);
        writer.Write((byte)value.BuffStacking);
        writer.Write(value.HeroValue);
        writer.Write(value.HeroSecondaryValue);
        writer.Write((byte)value.RequiredUnitTraits);
        writer.Write((byte)value.ExcludedUnitTraits);
        writer.Write(value.InnerRadius);
        writer.Write(value.PulseCount);
        writer.Write(value.StartDelay);
        writer.Write(value.MaximumTotalValue);
        writer.Write(value.BuildingValueMultiplier);
        writer.Write(value.VisualCount);
        writer.Write(value.ClusteredPlacement);
        var hasUnitForm = value.Kind == AbilityEffectKind.TransformUnit;
        writer.Write(hasUnitForm);
        if (hasUnitForm) WriteUnitForm(writer, value.UnitForm);
    }

    private static AbilityEffectProfile ReadEffect(BinaryReader reader)
    {
        var kind = (AbilityEffectKind)reader.ReadByte();
        var timing = (AbilityEffectTiming)reader.ReadByte();
        var selector = (AbilityEffectSelector)reader.ReadByte();
        var relations = (AbilityRelationFilter)reader.ReadByte();
        var value = reader.ReadSingle();
        var secondary = reader.ReadSingle();
        var radius = reader.ReadSingle();
        var duration = reader.ReadSingle();
        var interval = reader.ReadSingle();
        var maximumTargets = reader.ReadInt32();
        var status = (AbilityStatusFlags)reader.ReadUInt16();
        var modifier = ReadModifier(reader);
        var summon = reader.ReadBoolean() ? ReadSummon(reader) : default;
        var damageKind = (AbilityDamageKind)reader.ReadByte();
        var buffId = ReadString(reader);
        var buffPolarity = (AbilityBuffPolarity)reader.ReadByte();
        var buffDispelKind = (AbilityBuffDispelKind)reader.ReadByte();
        var buffStacking = (AbilityBuffStackingKind)reader.ReadByte();
        var heroValue = reader.ReadSingle();
        var heroSecondaryValue = reader.ReadSingle();
        var requiredUnitTraits = (AbilityUnitTraits)reader.ReadByte();
        var excludedUnitTraits = (AbilityUnitTraits)reader.ReadByte();
        var innerRadius = reader.ReadSingle();
        var pulseCount = reader.ReadInt32();
        var startDelay = reader.ReadSingle();
        var maximumTotalValue = reader.ReadSingle();
        var buildingValueMultiplier = reader.ReadSingle();
        var visualCount = reader.ReadInt32();
        var clusteredPlacement = reader.ReadBoolean();
        var unitForm = reader.ReadBoolean() ? ReadUnitForm(reader) : default;
        return new AbilityEffectProfile(
            kind, timing, selector, relations, value, secondary, radius,
            duration, interval, maximumTargets, status, modifier, summon,
            damageKind, buffId, buffPolarity, buffDispelKind, buffStacking,
            heroValue, heroSecondaryValue, requiredUnitTraits,
            excludedUnitTraits, innerRadius, pulseCount, startDelay,
            maximumTotalValue, buildingValueMultiplier, visualCount,
            clusteredPlacement, unitForm);
    }

    private static void WriteUnitForm(
        BinaryWriter writer,
        AbilityUnitFormProfile value)
    {
        WriteUnitType(writer, value.Normal);
        WriteUnitType(writer, value.Alternate);
        writer.Write(value.AlternateDurationSeconds);
        writer.Write((byte)value.RequiredBuildingFunction);
    }

    private static AbilityUnitFormProfile ReadUnitForm(BinaryReader reader) => new(
        ReadUnitType(reader),
        ReadUnitType(reader),
        reader.ReadSingle(),
        (BuildingFunctionKind)reader.ReadByte());

    private static void WriteUnitType(BinaryWriter writer, UnitTypeProfile value)
    {
        writer.Write(value.Id);
        WriteString(writer, value.Name);
        writer.Write(value.Movement.Id);
        WriteString(writer, value.Movement.Name);
        writer.Write(value.Movement.PhysicalRadius);
        writer.Write(value.Movement.MaximumSpeed);
        writer.Write(value.Movement.Acceleration);
        writer.Write((byte)value.Movement.MovementClass);
        writer.Write(value.Movement.NavigationRadius);
        WriteCombat(writer, value.Combat);
        writer.Write(value.IsWorker);
        writer.Write((byte)value.Perception.Concealment);
        writer.Write(value.Perception.DetectionRange);
        writer.Write(value.Perception.VisionRange);
        writer.Write(value.Perception.ObservationHeight);
        writer.Write((byte)value.Perception.TerrainVisionMode);
    }

    private static UnitTypeProfile ReadUnitType(BinaryReader reader)
    {
        var id = reader.ReadInt32();
        var name = ReadString(reader);
        var movement = new UnitMovementProfileSnapshot(
            reader.ReadInt32(), ReadString(reader), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            (MovementClass)reader.ReadByte(), reader.ReadSingle());
        var combat = ReadCombat(reader);
        var worker = reader.ReadBoolean();
        var perception = new UnitPerceptionProfileSnapshot(
            (UnitConcealmentKind)reader.ReadByte(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            (TerrainVisionMode)reader.ReadByte());
        return new UnitTypeProfile(id, name, movement, combat, worker)
        {
            Perception = perception
        };
    }

    private static void WriteModifier(BinaryWriter writer, AbilityStatModifier value)
    {
        writer.Write(value.MovementSpeedMultiplier);
        writer.Write(value.AttackCooldownMultiplier);
        writer.Write(value.AttackDamageMultiplier);
        writer.Write(value.AttackDamageAdd);
        writer.Write(value.ArmorAdd);
        writer.Write(value.MaximumHealthAdd);
        writer.Write(value.ManaRegenerationAdd);
        writer.Write(value.DetectionRangeAdd);
    }

    private static AbilityStatModifier ReadModifier(BinaryReader reader) => new(
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle());

    private static void WriteSummon(BinaryWriter writer, AbilitySummonProfile value)
    {
        WriteString(writer, value.ObjectId);
        writer.Write(value.Movement.Id);
        WriteString(writer, value.Movement.Name);
        writer.Write(value.Movement.PhysicalRadius);
        writer.Write(value.Movement.MaximumSpeed);
        writer.Write(value.Movement.Acceleration);
        writer.Write((byte)value.Movement.MovementClass);
        writer.Write(value.Movement.NavigationRadius);
        WriteCombat(writer, value.Combat);
        writer.Write((byte)value.Perception.Concealment);
        writer.Write(value.Perception.DetectionRange);
        writer.Write(value.Perception.VisionRange);
        writer.Write(value.Perception.ObservationHeight);
        writer.Write((byte)value.Perception.TerrainVisionMode);
        writer.Write(value.LifetimeSeconds);
    }

    private static AbilitySummonProfile ReadSummon(BinaryReader reader)
    {
        var objectId = ReadString(reader);
        var movement = new UnitMovementProfileSnapshot(
            reader.ReadInt32(), ReadString(reader), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            (MovementClass)reader.ReadByte(), reader.ReadSingle());
        var combat = ReadCombat(reader);
        var perception = new UnitPerceptionProfileSnapshot(
            (UnitConcealmentKind)reader.ReadByte(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            (TerrainVisionMode)reader.ReadByte());
        return new AbilitySummonProfile(
            objectId, movement, combat, perception, reader.ReadSingle());
    }

    private static void WriteCombat(BinaryWriter writer, CombatProfileSnapshot value)
    {
        writer.Write(value.MaximumHealth);
        writer.Write(value.AttackDamage);
        writer.Write(value.AttackRange);
        writer.Write(value.AcquisitionRange);
        writer.Write(value.AttackCooldownSeconds);
        writer.Write(value.AttackWindupSeconds);
        writer.Write(value.LeashDistance);
        writer.Write((byte)value.Positioning);
        writer.Write(value.Armor);
        writer.Write((ushort)value.Attributes);
        writer.Write(value.AttacksPerVolley);
        writer.Write((ushort)value.BonusVs);
        writer.Write(value.BonusDamage);
        writer.Write(value.BaseUpgradeDamage);
        writer.Write(value.BonusUpgradeDamage);
        writer.Write(value.ProjectileSpeed);
        writer.Write(value.CanMoveDuringWindup);
        writer.Write(value.CanMoveDuringCooldown);
        writer.Write(value.AutoTargetPriority);
        writer.Write(value.Weapons.Length);
        foreach (var weapon in value.Weapons)
            WriteWeapon(writer, weapon);
    }

    private static CombatProfileSnapshot ReadCombat(BinaryReader reader)
    {
        var value = new CombatProfileSnapshot(
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), (CombatPositioningKind)reader.ReadByte(),
            reader.ReadSingle(), (CombatAttribute)reader.ReadUInt16(),
            reader.ReadInt32(), (CombatAttribute)reader.ReadUInt16(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadBoolean(), reader.ReadBoolean(),
            reader.ReadInt32());
        var count = reader.ReadInt32();
        if (count is < 0 or > 8) throw new InvalidDataException();
        var weapons = ImmutableArray.CreateBuilder<CombatWeaponProfileSnapshot>(count);
        for (var index = 0; index < count; index++)
            weapons.Add(ReadWeapon(reader));
        return value with { Weapons = weapons.MoveToImmutable() };
    }

    private static void WriteWeapon(
        BinaryWriter writer, in CombatWeaponProfileSnapshot value)
    {
        writer.Write(value.Slot);
        writer.Write((byte)value.TargetLayers);
        writer.Write(value.EnabledByDefault);
        writer.Write(value.RequiredTechnologyId);
        writer.Write(value.AttackDamage);
        writer.Write(value.AttackRange);
        writer.Write(value.AttackCooldownSeconds);
        writer.Write(value.AttackWindupSeconds);
        writer.Write((byte)value.Positioning);
        writer.Write(value.AttacksPerVolley);
        writer.Write((ushort)value.BonusVs);
        writer.Write(value.BonusDamage);
        writer.Write(value.BaseUpgradeDamage);
        writer.Write(value.BonusUpgradeDamage);
        writer.Write(value.ProjectileSpeed);
        writer.Write(value.CanMoveDuringWindup);
        writer.Write(value.CanMoveDuringCooldown);
    }

    private static CombatWeaponProfileSnapshot ReadWeapon(BinaryReader reader) =>
        new(
            reader.ReadInt32(), (CombatTargetLayer)reader.ReadByte(),
            reader.ReadBoolean(), reader.ReadInt32(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            (CombatPositioningKind)reader.ReadByte(), reader.ReadInt32(),
            (CombatAttribute)reader.ReadUInt16(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadBoolean(), reader.ReadBoolean());

    private static void WriteBinding(
        BinaryWriter writer,
        UnitAbilityBindingProfile value)
    {
        writer.Write(value.UnitTypeId);
        writer.Write(value.Hero);
        writer.Write(value.Mana.Initial);
        writer.Write(value.Mana.Maximum);
        writer.Write(value.Mana.RegenerationPerSecond);
        writer.Write((byte)value.Traits);
        writer.Write(value.UnitLevel);
        writer.Write(value.ExperienceBounty);
        writer.Write(value.HeroMaximumLevel);
        writer.Write(value.Abilities.Length);
        foreach (var ability in value.Abilities)
        {
            writer.Write(ability.AbilityId);
            writer.Write(ability.Level);
            writer.Write(ability.AutoCastEnabled);
        }
    }

    private static UnitAbilityBindingProfile ReadBinding(BinaryReader reader)
    {
        var unitType = reader.ReadInt32();
        var hero = reader.ReadBoolean();
        var mana = new UnitManaProfile(
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        var traits = (AbilityUnitTraits)reader.ReadByte();
        var unitLevel = reader.ReadInt32();
        var experienceBounty = reader.ReadInt32();
        var heroMaximumLevel = reader.ReadInt32();
        var count = ReadCount(reader, 32);
        var abilities = new UnitAbilityEntryProfile[count];
        for (var index = 0; index < count; index++)
            abilities[index] = new UnitAbilityEntryProfile(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean());
        return new UnitAbilityBindingProfile(
            unitType, hero, mana, abilities.ToImmutableArray(), traits,
            unitLevel, experienceBounty, heroMaximumLevel);
    }

    private static void WriteBuildingBinding(
        BinaryWriter writer,
        BuildingAbilityBindingProfile value)
    {
        writer.Write(value.BuildingTypeId);
        writer.Write(value.Abilities.Length);
        foreach (var ability in value.Abilities) writer.Write(ability);
    }

    private static BuildingAbilityBindingProfile ReadBuildingBinding(
        BinaryReader reader)
    {
        var buildingType = reader.ReadInt32();
        var count = ReadCount(reader, 32);
        var abilities = new int[count];
        for (var index = 0; index < count; index++)
            abilities[index] = reader.ReadInt32();
        return new BuildingAbilityBindingProfile(
            buildingType, abilities.ToImmutableArray());
    }

    private static int ReadCount(BinaryReader reader, int maximum)
    {
        var value = reader.ReadInt32();
        if (value is < 0 || value > maximum) throw new InvalidDataException();
        return value;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = StrictUtf8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 0 or > MaximumStringBytes) throw new InvalidDataException();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return StrictUtf8.GetString(bytes);
    }
}
