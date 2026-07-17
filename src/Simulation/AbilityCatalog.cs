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
    ToggleStatus
}

public enum AbilityEffectTiming : byte
{
    Impact,
    ChannelPulse,
    Aura,
    AttackHit
}

public enum AbilityEffectSelector : byte
{
    Caster,
    Primary,
    AreaAtTarget,
    AreaAtCaster
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
    AbilitySummonProfile Summon = default)
{
    public bool IsValid =>
        Enum.IsDefined(Kind) && Enum.IsDefined(Timing) &&
        Enum.IsDefined(Selector) && Relations != AbilityRelationFilter.None &&
        (Relations & ~AbilityRelationFilter.Any) == 0 &&
        float.IsFinite(Value) && float.IsFinite(SecondaryValue) &&
        float.IsFinite(Radius) && Radius >= 0f &&
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
        (Kind != AbilityEffectKind.Summon || Summon.IsValid);
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

public readonly record struct UnitAbilityBindingProfile(
    int UnitTypeId,
    bool Hero,
    UnitManaProfile Mana,
    ImmutableArray<UnitAbilityEntryProfile> Abilities)
{
    public bool IsValid(AbilityProfile[] profiles)
    {
        if (UnitTypeId < 0 || !Mana.IsValid || Abilities.IsDefault ||
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
/// Immutable, deterministic ability definitions and unit-type loadouts. The
/// simulation consumes dense integer IDs; content adapters retain Warcraft
/// rawcodes and presentation strings at this boundary.
/// </summary>
public sealed class AbilityCatalogSnapshot
{
    public const int CurrentFormatVersion = 2;
    private readonly Dictionary<string, int> _rawIds;
    private readonly Dictionary<int, UnitAbilityBindingProfile> _bindings;

    public AbilityCatalogSnapshot(
        AbilityProfile[] abilities,
        UnitAbilityBindingProfile[] bindings)
    {
        Abilities = abilities.ToArray();
        Bindings = bindings.OrderBy(value => value.UnitTypeId).ToArray();
        Validate();
        _rawIds = Abilities.ToDictionary(
            value => value.RawId, value => value.Id, StringComparer.Ordinal);
        _bindings = Bindings.ToDictionary(value => value.UnitTypeId);
        CanonicalBytes = AbilitySerialization.SerializeCatalog(this);
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public AbilityProfile[] Abilities { get; }
    public UnitAbilityBindingProfile[] Bindings { get; }
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

    public static AbilityCatalogSnapshot Empty { get; } = new([], []);

    private void Validate()
    {
        if (Abilities.Length > 4096 || Bindings.Length > 4096)
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
        return new AbilityCatalogSnapshot(abilities, bindings);
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
        return new AbilityEffectProfile(
            kind, timing, selector, relations, value, secondary, radius,
            duration, interval, maximumTargets, status, modifier, summon);
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
    }

    private static CombatProfileSnapshot ReadCombat(BinaryReader reader) => new(
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), (CombatPositioningKind)reader.ReadByte(),
        reader.ReadSingle(), (CombatAttribute)reader.ReadUInt16(),
        reader.ReadInt32(), (CombatAttribute)reader.ReadUInt16(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadBoolean(), reader.ReadBoolean(),
        reader.ReadInt32());

    private static void WriteBinding(
        BinaryWriter writer,
        UnitAbilityBindingProfile value)
    {
        writer.Write(value.UnitTypeId);
        writer.Write(value.Hero);
        writer.Write(value.Mana.Initial);
        writer.Write(value.Mana.Maximum);
        writer.Write(value.Mana.RegenerationPerSecond);
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
        var count = ReadCount(reader, 32);
        var abilities = new UnitAbilityEntryProfile[count];
        for (var index = 0; index < count; index++)
            abilities[index] = new UnitAbilityEntryProfile(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean());
        return new UnitAbilityBindingProfile(
            unitType, hero, mana, abilities.ToImmutableArray());
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
