using System.Numerics;
using System.Collections.Immutable;

namespace RtsDemo.Simulation;

public enum UnitCommandIntent : byte
{
    None,
    Move,
    AttackMove,
    AttackTarget,
    Stop,
    Hold
}

public enum CombatPhase : byte
{
    None,
    Searching,
    Chasing,
    Attacking
}

public enum CombatTargetKind : byte
{
    None,
    Unit,
    Building
}

[Flags]
public enum CombatTargetLayer : byte
{
    None = 0,
    GroundUnit = 1 << 0,
    AirUnit = 1 << 1,
    Building = 1 << 2,
    All = GroundUnit | AirUnit | Building
}

public enum CombatPositioningKind : byte
{
    Melee,
    Ranged
}

public enum UnitConcealmentKind : byte
{
    None,
    Cloaked,
    Burrowed
}

public enum UnitConcealmentPhase : byte
{
    Visible,
    Activating,
    Concealed,
    Deactivating
}

public readonly record struct UnitConcealmentCapabilitySnapshot(
    UnitConcealmentKind Kind,
    float ActivationSeconds,
    float DeactivationSeconds,
    float ConcealedVisionRange,
    bool CanMoveWhileConcealed,
    bool CanAttackWhileConcealed)
{
    public static UnitConcealmentCapabilitySnapshot None => default;

    public static UnitConcealmentCapabilitySnapshot StandardBurrow => new(
        UnitConcealmentKind.Burrowed,
        ActivationSeconds: 1f,
        DeactivationSeconds: 0.75f,
        ConcealedVisionRange: 96f,
        CanMoveWhileConcealed: false,
        CanAttackWhileConcealed: false);

    public static UnitConcealmentCapabilitySnapshot StandardCloak => new(
        UnitConcealmentKind.Cloaked,
        ActivationSeconds: 0.25f,
        DeactivationSeconds: 0.25f,
        ConcealedVisionRange: PlayerVisibilitySystem.UnitVisionRadius,
        CanMoveWhileConcealed: true,
        CanAttackWhileConcealed: true);

    public void Validate()
    {
        if (!Enum.IsDefined(Kind) ||
            Kind == UnitConcealmentKind.None &&
            (ActivationSeconds != 0f || DeactivationSeconds != 0f ||
             ConcealedVisionRange != 0f || CanMoveWhileConcealed ||
             CanAttackWhileConcealed) ||
            Kind != UnitConcealmentKind.None &&
            (!float.IsFinite(ActivationSeconds) || ActivationSeconds <= 0f ||
             !float.IsFinite(DeactivationSeconds) || DeactivationSeconds <= 0f ||
             !float.IsFinite(ConcealedVisionRange) ||
             ConcealedVisionRange <= 0f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(UnitConcealmentCapabilitySnapshot),
                "Unit concealment capability values are invalid.");
        }
    }
}

public readonly record struct UnitPerceptionProfileSnapshot(
    UnitConcealmentKind Concealment,
    float DetectionRange,
    float VisionRange = PlayerVisibilitySystem.UnitVisionRadius,
    float ObservationHeight = PlayerVisibilitySystem.DefaultGroundObservationHeight,
    TerrainVisionMode TerrainVisionMode = TerrainVisionMode.Ground)
{
    public static UnitPerceptionProfileSnapshot Standard => new(
        UnitConcealmentKind.None, 0f,
        PlayerVisibilitySystem.UnitVisionRadius,
        PlayerVisibilitySystem.DefaultGroundObservationHeight,
        TerrainVisionMode.Ground);

    public static UnitPerceptionProfileSnapshot ElevatedObserver(
        float visionRange = PlayerVisibilitySystem.UnitVisionRadius,
        float detectionRange = 0f) => new(
        UnitConcealmentKind.None,
        detectionRange,
        visionRange,
        PlayerVisibilitySystem.DefaultGroundObservationHeight,
        TerrainVisionMode.Elevated);

    public void Validate()
    {
        if (!Enum.IsDefined(Concealment) ||
            !float.IsFinite(DetectionRange) || DetectionRange < 0f ||
            !float.IsFinite(VisionRange) || VisionRange <= 0f ||
            !float.IsFinite(ObservationHeight) || ObservationHeight < 0f ||
            !Enum.IsDefined(TerrainVisionMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(UnitPerceptionProfileSnapshot),
                "Unit perception values are invalid.");
        }
    }
}

public readonly record struct CombatProfileSnapshot(
    float MaximumHealth,
    float AttackDamage,
    float AttackRange,
    float AcquisitionRange,
    float AttackCooldownSeconds,
    float AttackWindupSeconds,
    float LeashDistance,
    CombatPositioningKind Positioning = CombatPositioningKind.Ranged,
    float Armor = 0f,
    CombatAttribute Attributes = CombatAttribute.Biological,
    int AttacksPerVolley = 1,
    CombatAttribute BonusVs = CombatAttribute.None,
    float BonusDamage = 0f,
    float BaseUpgradeDamage = 0f,
    float BonusUpgradeDamage = 0f,
    float ProjectileSpeed = 0f,
    bool CanMoveDuringWindup = false,
    bool CanMoveDuringCooldown = false,
    int AutoTargetPriority = 0,
    CombatArmorType ArmorType = CombatArmorType.Legacy,
    int ArmorUpgradeTechnologyId = -1,
    float ArmorUpgradePerLevel = 0f,
    float AttackHalfAngleRadians = MathF.PI)
{
    public ImmutableArray<CombatWeaponProfileSnapshot> Weapons { get; init; } = [];

    public static CombatProfileSnapshot Standard => new(
        MaximumHealth: 45f,
        AttackDamage: 8f,
        AttackRange: 34f,
        AcquisitionRange: 155f,
        AttackCooldownSeconds: 0.72f,
        AttackWindupSeconds: 0.18f,
        LeashDistance: 260f);

    public void Validate()
    {
        if (!float.IsFinite(MaximumHealth) || MaximumHealth <= 0f ||
            !float.IsFinite(AttackDamage) || AttackDamage < 0f ||
            !float.IsFinite(AttackRange) || AttackRange < 0f ||
            !float.IsFinite(AcquisitionRange) || AcquisitionRange < AttackRange ||
            !float.IsFinite(AttackCooldownSeconds) || AttackCooldownSeconds <= 0f ||
            !float.IsFinite(AttackWindupSeconds) || AttackWindupSeconds < 0f ||
            AttackWindupSeconds > AttackCooldownSeconds ||
            !float.IsFinite(LeashDistance) || LeashDistance < AcquisitionRange ||
            !Enum.IsDefined(Positioning) || !float.IsFinite(Armor) ||
            (Attributes & ~CombatAttribute.All) != 0 ||
            (BonusVs & ~CombatAttribute.All) != 0 ||
            AttacksPerVolley is < 1 or > 32 ||
            !float.IsFinite(BonusDamage) || BonusDamage < 0f ||
            !float.IsFinite(BaseUpgradeDamage) || BaseUpgradeDamage < 0f ||
            !float.IsFinite(BonusUpgradeDamage) || BonusUpgradeDamage < 0f ||
            !float.IsFinite(ProjectileSpeed) || ProjectileSpeed < 0f ||
            AutoTargetPriority is < 0 or > 10 ||
            !Enum.IsDefined(ArmorType) || ArmorUpgradeTechnologyId < -1 ||
            !float.IsFinite(ArmorUpgradePerLevel) ||
            ArmorUpgradePerLevel < 0f ||
            ArmorUpgradeTechnologyId < 0 && ArmorUpgradePerLevel != 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CombatProfileSnapshot),
                $"Combat profile values are invalid or internally inconsistent: " +
                $"hp={MaximumHealth}, damage={AttackDamage}, range={AttackRange}, " +
                $"acquisition={AcquisitionRange}, cooldown={AttackCooldownSeconds}, " +
                $"windup={AttackWindupSeconds}, leash={LeashDistance}, " +
                $"positioning={Positioning}, armor={Armor}, " +
                $"attacks={AttacksPerVolley}, priority={AutoTargetPriority}.");
        }
        if (!float.IsFinite(AttackHalfAngleRadians) ||
            AttackHalfAngleRadians < 0f ||
            AttackHalfAngleRadians > MathF.PI)
            throw new ArgumentOutOfRangeException(
                nameof(AttackHalfAngleRadians),
                "Attack half angle must be between zero and pi radians.");
        if (Weapons.IsDefault || Weapons.Length > 8)
            throw new ArgumentOutOfRangeException(
                nameof(Weapons), "Combat weapon groups must contain at most eight weapons.");
        var slots = new HashSet<int>();
        foreach (var weapon in Weapons)
        {
            weapon.Validate();
            if (!slots.Add(weapon.Slot))
                throw new ArgumentException(
                    $"Combat weapon slot {weapon.Slot} is duplicated.", nameof(Weapons));
        }
    }
}

public readonly record struct CombatWeaponProfileSnapshot(
    int Slot,
    CombatTargetLayer TargetLayers,
    bool EnabledByDefault,
    int RequiredTechnologyId,
    float AttackDamage,
    float AttackRange,
    float AttackCooldownSeconds,
    float AttackWindupSeconds,
    CombatPositioningKind Positioning,
    int AttacksPerVolley = 1,
    CombatAttribute BonusVs = CombatAttribute.None,
    float BonusDamage = 0f,
    float BaseUpgradeDamage = 0f,
    float BonusUpgradeDamage = 0f,
    float ProjectileSpeed = 0f,
    bool CanMoveDuringWindup = false,
    bool CanMoveDuringCooldown = false,
    CombatAttackType AttackType = CombatAttackType.Legacy,
    int DamageUpgradeTechnologyId = -1,
    float MinimumRange = 0f,
    CombatWeaponAreaSnapshot Area = default,
    CombatWeaponPropagationSnapshot Propagation = default)
{
    public static CombatWeaponProfileSnapshot FromLegacy(
        in CombatProfileSnapshot profile) => new(
        0, CombatTargetLayer.All, true, -1,
        profile.AttackDamage, profile.AttackRange,
        profile.AttackCooldownSeconds, profile.AttackWindupSeconds,
        profile.Positioning, profile.AttacksPerVolley, profile.BonusVs,
        profile.BonusDamage, profile.BaseUpgradeDamage,
        profile.BonusUpgradeDamage, profile.ProjectileSpeed,
        profile.CanMoveDuringWindup, profile.CanMoveDuringCooldown,
        CombatAttackType.Legacy, -1, 0f, default, default);

    public void Validate()
    {
        if (Slot is < 0 or > 7 ||
            TargetLayers == CombatTargetLayer.None ||
            (TargetLayers & ~CombatTargetLayer.All) != 0 ||
            !EnabledByDefault && RequiredTechnologyId < 0 ||
            RequiredTechnologyId < -1 ||
            !float.IsFinite(AttackDamage) || AttackDamage < 0f ||
            !float.IsFinite(AttackRange) || AttackRange < 0f ||
            !float.IsFinite(AttackCooldownSeconds) ||
            AttackCooldownSeconds <= 0f ||
            !float.IsFinite(AttackWindupSeconds) || AttackWindupSeconds < 0f ||
            AttackWindupSeconds > AttackCooldownSeconds ||
            !Enum.IsDefined(Positioning) || AttacksPerVolley is < 1 or > 32 ||
            (BonusVs & ~CombatAttribute.All) != 0 ||
            !float.IsFinite(BonusDamage) || BonusDamage < 0f ||
            !float.IsFinite(BaseUpgradeDamage) || BaseUpgradeDamage < 0f ||
            !float.IsFinite(BonusUpgradeDamage) || BonusUpgradeDamage < 0f ||
            !float.IsFinite(ProjectileSpeed) || ProjectileSpeed < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CombatWeaponProfileSnapshot),
                $"Combat weapon slot {Slot} is invalid.");
        }
        if (!Enum.IsDefined(AttackType) || DamageUpgradeTechnologyId < -1 ||
            !float.IsFinite(MinimumRange) || MinimumRange < 0f ||
            MinimumRange > AttackRange)
            throw new ArgumentOutOfRangeException(
                nameof(CombatWeaponProfileSnapshot),
                $"Combat weapon slot {Slot} has invalid typed fields.");
        Area.Validate();
        Propagation.Validate();
    }
}

/// <summary>
/// Combat state is indexed by stable UnitStore IDs but kept separate from movement data.
/// This keeps command intent and the resumable AttackMove route independent from chase paths.
/// </summary>
public sealed class CombatStore
{
    public CombatStore(int capacity)
    {
        Teams = new int[capacity];
        Health = new float[capacity];
        MaximumHealth = new float[capacity];
        AttackDamage = new float[capacity];
        Armor = new float[capacity];
        ArmorTypes = new CombatArmorType[capacity];
        ArmorUpgradeTechnologyIds = new int[capacity];
        ArmorUpgradePerLevel = new float[capacity];
        Attributes = new CombatAttribute[capacity];
        AttacksPerVolley = new int[capacity];
        BonusVs = new CombatAttribute[capacity];
        BonusDamage = new float[capacity];
        BaseUpgradeDamage = new float[capacity];
        BonusUpgradeDamage = new float[capacity];
        ProjectileSpeed = new float[capacity];
        CanMoveDuringWindup = new bool[capacity];
        CanMoveDuringCooldown = new bool[capacity];
        AutoTargetPriority = new int[capacity];
        AttackTypes = new CombatAttackType[capacity];
        DamageUpgradeTechnologyIds = new int[capacity];
        MinimumAttackRanges = new float[capacity];
        WeaponAreas = new CombatWeaponAreaSnapshot[capacity];
        WeaponPropagations = new CombatWeaponPropagationSnapshot[capacity];
        ConcealmentKinds = new UnitConcealmentKind[capacity];
        ConcealmentCapabilities =
            new UnitConcealmentCapabilitySnapshot[capacity];
        ConcealmentPhases = new UnitConcealmentPhase[capacity];
        ConcealmentTransitionRemaining = new float[capacity];
        DetectionRanges = new float[capacity];
        BaseVisionRanges = new float[capacity];
        VisionRanges = new float[capacity];
        ObservationHeights = new float[capacity];
        TerrainVisionModes = new TerrainVisionMode[capacity];
        AttackRanges = new float[capacity];
        AcquisitionRanges = new float[capacity];
        AttackCooldownDurations = new float[capacity];
        AttackWindupDurations = new float[capacity];
        LeashDistances = new float[capacity];
        PositioningKinds = new CombatPositioningKind[capacity];
        CommandIntents = new UnitCommandIntent[capacity];
        Phases = new CombatPhase[capacity];
        TargetUnits = new int[capacity];
        TargetBuildings = new int[capacity];
        TargetKinds = new CombatTargetKind[capacity];
        AttackMoveGoals = new Vector2[capacity];
        EngagementOrigins = new Vector2[capacity];
        LastChaseTargets = new Vector2[capacity];
        AttackSlotTargets = new Vector2[capacity];
        AttackSlotAngles = new float[capacity];
        AttackSlotRadii = new float[capacity];
        HasAttackSlots = new bool[capacity];
        CooldownRemaining = new float[capacity];
        WindupRemaining = new float[capacity];
        ChaseRepathRemaining = new float[capacity];
        TargetLockRemaining = new float[capacity];
        WeaponProfiles = new ImmutableArray<CombatWeaponProfileSnapshot>[capacity];
        ActiveWeaponSlots = new int[capacity];
        AttackDamageMultipliers = new float[capacity];
        AttackDamageAdds = new float[capacity];
        AttackCooldownMultipliers = new float[capacity];
        AttackHalfAngles = new float[capacity];
        Array.Fill(TargetUnits, -1);
        Array.Fill(TargetBuildings, -1);
        Array.Fill(ActiveWeaponSlots, -1);
        Array.Fill(ArmorUpgradeTechnologyIds, -1);
        Array.Fill(DamageUpgradeTechnologyIds, -1);
        Array.Fill(AttackDamageMultipliers, 1f);
        Array.Fill(AttackCooldownMultipliers, 1f);
    }

    public int[] Teams { get; }
    public float[] Health { get; }
    public float[] MaximumHealth { get; }
    public float[] AttackDamage { get; }
    public float[] Armor { get; }
    public CombatArmorType[] ArmorTypes { get; }
    public int[] ArmorUpgradeTechnologyIds { get; }
    public float[] ArmorUpgradePerLevel { get; }
    public CombatAttribute[] Attributes { get; }
    public int[] AttacksPerVolley { get; }
    public CombatAttribute[] BonusVs { get; }
    public float[] BonusDamage { get; }
    public float[] BaseUpgradeDamage { get; }
    public float[] BonusUpgradeDamage { get; }
    public float[] ProjectileSpeed { get; }
    public bool[] CanMoveDuringWindup { get; }
    public bool[] CanMoveDuringCooldown { get; }
    public int[] AutoTargetPriority { get; }
    public CombatAttackType[] AttackTypes { get; }
    public int[] DamageUpgradeTechnologyIds { get; }
    public float[] MinimumAttackRanges { get; }
    public CombatWeaponAreaSnapshot[] WeaponAreas { get; }
    public CombatWeaponPropagationSnapshot[] WeaponPropagations { get; }
    public UnitConcealmentKind[] ConcealmentKinds { get; }
    public UnitConcealmentCapabilitySnapshot[] ConcealmentCapabilities { get; }
    public UnitConcealmentPhase[] ConcealmentPhases { get; }
    public float[] ConcealmentTransitionRemaining { get; }
    public float[] DetectionRanges { get; }
    public float[] BaseVisionRanges { get; }
    public float[] VisionRanges { get; }
    public float[] ObservationHeights { get; }
    public TerrainVisionMode[] TerrainVisionModes { get; }
    public float[] AttackRanges { get; }
    public float[] AcquisitionRanges { get; }
    public float[] AttackCooldownDurations { get; }
    public float[] AttackWindupDurations { get; }
    public float[] LeashDistances { get; }
    public CombatPositioningKind[] PositioningKinds { get; }
    public UnitCommandIntent[] CommandIntents { get; }
    public CombatPhase[] Phases { get; }
    public int[] TargetUnits { get; }
    public int[] TargetBuildings { get; }
    public CombatTargetKind[] TargetKinds { get; }
    public Vector2[] AttackMoveGoals { get; }
    public Vector2[] EngagementOrigins { get; }
    public Vector2[] LastChaseTargets { get; }
    public Vector2[] AttackSlotTargets { get; }
    public float[] AttackSlotAngles { get; }
    public float[] AttackSlotRadii { get; }
    public bool[] HasAttackSlots { get; }
    public float[] CooldownRemaining { get; }
    public float[] WindupRemaining { get; }
    public float[] ChaseRepathRemaining { get; }
    public float[] TargetLockRemaining { get; }
    public ImmutableArray<CombatWeaponProfileSnapshot>[] WeaponProfiles { get; }
    public int[] ActiveWeaponSlots { get; }
    public float[] AttackDamageMultipliers { get; }
    public float[] AttackDamageAdds { get; }
    public float[] AttackCooldownMultipliers { get; }
    public float[] AttackHalfAngles { get; }

    public void Register(
        int unit,
        int team,
        Vector2 position,
        CombatProfileSnapshot profile,
        UnitPerceptionProfileSnapshot perception = default,
        UnitConcealmentCapabilitySnapshot concealmentCapability = default)
    {
        profile.Validate();
        if (perception == default)
            perception = UnitPerceptionProfileSnapshot.Standard;
        perception.Validate();
        concealmentCapability.Validate();
        if (perception.Concealment != UnitConcealmentKind.None &&
            concealmentCapability.Kind != UnitConcealmentKind.None &&
            perception.Concealment != concealmentCapability.Kind)
        {
            throw new ArgumentException(
                "Initial concealment and toggle capability must use the same kind.",
                nameof(concealmentCapability));
        }
        Teams[unit] = team;
        Health[unit] = profile.MaximumHealth;
        MaximumHealth[unit] = profile.MaximumHealth;
        AttackDamage[unit] = profile.AttackDamage;
        Armor[unit] = profile.Armor;
        ArmorTypes[unit] = profile.ArmorType;
        ArmorUpgradeTechnologyIds[unit] =
            profile.ArmorUpgradeTechnologyId;
        ArmorUpgradePerLevel[unit] = profile.ArmorUpgradePerLevel;
        AttackHalfAngles[unit] = profile.AttackHalfAngleRadians;
        Attributes[unit] = profile.Attributes;
        AttacksPerVolley[unit] = profile.AttacksPerVolley;
        BonusVs[unit] = profile.BonusVs;
        BonusDamage[unit] = profile.BonusDamage;
        BaseUpgradeDamage[unit] = profile.BaseUpgradeDamage;
        BonusUpgradeDamage[unit] = profile.BonusUpgradeDamage;
        ProjectileSpeed[unit] = profile.ProjectileSpeed;
        CanMoveDuringWindup[unit] = profile.CanMoveDuringWindup;
        CanMoveDuringCooldown[unit] = profile.CanMoveDuringCooldown;
        AutoTargetPriority[unit] = profile.AutoTargetPriority;
        ConcealmentKinds[unit] = perception.Concealment;
        ConcealmentCapabilities[unit] = concealmentCapability;
        ConcealmentPhases[unit] = perception.Concealment ==
                                  UnitConcealmentKind.None
            ? UnitConcealmentPhase.Visible
            : UnitConcealmentPhase.Concealed;
        ConcealmentTransitionRemaining[unit] = 0f;
        DetectionRanges[unit] = perception.DetectionRange;
        BaseVisionRanges[unit] = perception.VisionRange;
        ObservationHeights[unit] = perception.ObservationHeight;
        TerrainVisionModes[unit] = perception.TerrainVisionMode;
        VisionRanges[unit] = perception.Concealment !=
                                 UnitConcealmentKind.None &&
                             concealmentCapability.Kind !=
                                 UnitConcealmentKind.None
            ? concealmentCapability.ConcealedVisionRange
            : perception.VisionRange;
        AttackRanges[unit] = profile.AttackRange;
        AcquisitionRanges[unit] = profile.AcquisitionRange;
        AttackCooldownDurations[unit] = profile.AttackCooldownSeconds;
        AttackWindupDurations[unit] = profile.AttackWindupSeconds;
        LeashDistances[unit] = profile.LeashDistance;
        PositioningKinds[unit] = profile.Positioning;
        CommandIntents[unit] = UnitCommandIntent.None;
        Phases[unit] = CombatPhase.None;
        TargetUnits[unit] = -1;
        TargetBuildings[unit] = -1;
        TargetKinds[unit] = CombatTargetKind.None;
        AttackMoveGoals[unit] = position;
        EngagementOrigins[unit] = position;
        LastChaseTargets[unit] = position;
        AttackSlotTargets[unit] = position;
        ConfigureWeapons(unit, profile);
    }

    internal void ApplyProfile(
        int unit,
        in CombatProfileSnapshot profile,
        in UnitPerceptionProfileSnapshot perception)
    {
        profile.Validate();
        perception.Validate();
        if ((uint)unit >= (uint)Teams.Length)
            throw new ArgumentOutOfRangeException(nameof(unit));
        var healthFraction = MaximumHealth[unit] > 0f
            ? Math.Clamp(Health[unit] / MaximumHealth[unit], 0f, 1f)
            : 1f;
        MaximumHealth[unit] = profile.MaximumHealth;
        Health[unit] = MathF.Max(
            1f, profile.MaximumHealth * healthFraction);
        AttackDamage[unit] = profile.AttackDamage;
        Armor[unit] = profile.Armor;
        ArmorTypes[unit] = profile.ArmorType;
        ArmorUpgradeTechnologyIds[unit] =
            profile.ArmorUpgradeTechnologyId;
        ArmorUpgradePerLevel[unit] = profile.ArmorUpgradePerLevel;
        AttackHalfAngles[unit] = profile.AttackHalfAngleRadians;
        Attributes[unit] = profile.Attributes;
        AttacksPerVolley[unit] = profile.AttacksPerVolley;
        BonusVs[unit] = profile.BonusVs;
        BonusDamage[unit] = profile.BonusDamage;
        BaseUpgradeDamage[unit] = profile.BaseUpgradeDamage;
        BonusUpgradeDamage[unit] = profile.BonusUpgradeDamage;
        ProjectileSpeed[unit] = profile.ProjectileSpeed;
        CanMoveDuringWindup[unit] = profile.CanMoveDuringWindup;
        CanMoveDuringCooldown[unit] = profile.CanMoveDuringCooldown;
        AutoTargetPriority[unit] = profile.AutoTargetPriority;
        AttackRanges[unit] = profile.AttackRange;
        AcquisitionRanges[unit] = profile.AcquisitionRange;
        AttackCooldownDurations[unit] = profile.AttackCooldownSeconds;
        AttackWindupDurations[unit] = profile.AttackWindupSeconds;
        LeashDistances[unit] = profile.LeashDistance;
        PositioningKinds[unit] = profile.Positioning;
        ConfigureWeapons(unit, profile);
        ConcealmentKinds[unit] = perception.Concealment;
        ConcealmentCapabilities[unit] =
            UnitConcealmentCapabilitySnapshot.None;
        ConcealmentPhases[unit] = perception.Concealment ==
                                  UnitConcealmentKind.None
            ? UnitConcealmentPhase.Visible
            : UnitConcealmentPhase.Concealed;
        ConcealmentTransitionRemaining[unit] = 0f;
        DetectionRanges[unit] = perception.DetectionRange;
        BaseVisionRanges[unit] = perception.VisionRange;
        VisionRanges[unit] = perception.VisionRange;
        ObservationHeights[unit] = perception.ObservationHeight;
        TerrainVisionModes[unit] = perception.TerrainVisionMode;
        SetCommand(unit, UnitCommandIntent.Stop, AttackMoveGoals[unit]);
        CooldownRemaining[unit] = 0f;
        WindupRemaining[unit] = 0f;
    }

    internal void CopyRuntimeStateFrom(CombatStore source)
    {
        if (source.Teams.Length != Teams.Length)
        {
            throw new InvalidOperationException("Combat runtime capacity mismatch.");
        }
        Copy(source.Teams, Teams);
        Copy(source.Health, Health);
        Copy(source.MaximumHealth, MaximumHealth);
        Copy(source.AttackDamage, AttackDamage);
        Copy(source.Armor, Armor);
        Copy(source.ArmorTypes, ArmorTypes);
        Copy(source.ArmorUpgradeTechnologyIds, ArmorUpgradeTechnologyIds);
        Copy(source.ArmorUpgradePerLevel, ArmorUpgradePerLevel);
        Copy(source.AttackHalfAngles, AttackHalfAngles);
        Copy(source.Attributes, Attributes);
        Copy(source.AttacksPerVolley, AttacksPerVolley);
        Copy(source.BonusVs, BonusVs);
        Copy(source.BonusDamage, BonusDamage);
        Copy(source.BaseUpgradeDamage, BaseUpgradeDamage);
        Copy(source.BonusUpgradeDamage, BonusUpgradeDamage);
        Copy(source.ProjectileSpeed, ProjectileSpeed);
        Copy(source.CanMoveDuringWindup, CanMoveDuringWindup);
        Copy(source.CanMoveDuringCooldown, CanMoveDuringCooldown);
        Copy(source.AutoTargetPriority, AutoTargetPriority);
        Copy(source.AttackTypes, AttackTypes);
        Copy(source.DamageUpgradeTechnologyIds, DamageUpgradeTechnologyIds);
        Copy(source.MinimumAttackRanges, MinimumAttackRanges);
        Copy(source.WeaponAreas, WeaponAreas);
        Copy(source.WeaponPropagations, WeaponPropagations);
        Copy(source.ConcealmentKinds, ConcealmentKinds);
        Copy(source.ConcealmentCapabilities, ConcealmentCapabilities);
        Copy(source.ConcealmentPhases, ConcealmentPhases);
        Copy(source.ConcealmentTransitionRemaining,
            ConcealmentTransitionRemaining);
        Copy(source.DetectionRanges, DetectionRanges);
        Copy(source.BaseVisionRanges, BaseVisionRanges);
        Copy(source.VisionRanges, VisionRanges);
        Copy(source.ObservationHeights, ObservationHeights);
        Copy(source.TerrainVisionModes, TerrainVisionModes);
        Copy(source.AttackRanges, AttackRanges);
        Copy(source.AcquisitionRanges, AcquisitionRanges);
        Copy(source.AttackCooldownDurations, AttackCooldownDurations);
        Copy(source.AttackWindupDurations, AttackWindupDurations);
        Copy(source.LeashDistances, LeashDistances);
        Copy(source.PositioningKinds, PositioningKinds);
        Copy(source.CommandIntents, CommandIntents);
        Copy(source.Phases, Phases);
        Copy(source.TargetUnits, TargetUnits);
        Copy(source.TargetBuildings, TargetBuildings);
        Copy(source.TargetKinds, TargetKinds);
        Copy(source.AttackMoveGoals, AttackMoveGoals);
        Copy(source.EngagementOrigins, EngagementOrigins);
        Copy(source.LastChaseTargets, LastChaseTargets);
        Copy(source.AttackSlotTargets, AttackSlotTargets);
        Copy(source.AttackSlotAngles, AttackSlotAngles);
        Copy(source.AttackSlotRadii, AttackSlotRadii);
        Copy(source.HasAttackSlots, HasAttackSlots);
        Copy(source.CooldownRemaining, CooldownRemaining);
        Copy(source.WindupRemaining, WindupRemaining);
        Copy(source.ChaseRepathRemaining, ChaseRepathRemaining);
        Copy(source.TargetLockRemaining, TargetLockRemaining);
        Copy(source.WeaponProfiles, WeaponProfiles);
        Copy(source.ActiveWeaponSlots, ActiveWeaponSlots);
        Copy(source.AttackDamageMultipliers, AttackDamageMultipliers);
        Copy(source.AttackDamageAdds, AttackDamageAdds);
        Copy(source.AttackCooldownMultipliers, AttackCooldownMultipliers);
    }

    public bool CanTarget(
        int unit,
        CombatTargetLayer layer,
        Func<int, bool> hasTechnology) =>
        TryResolveWeapon(unit, layer, hasTechnology, out _);

    public bool TrySelectWeapon(
        int unit,
        CombatTargetLayer layer,
        Func<int, bool> hasTechnology)
    {
        if (!TryResolveWeapon(unit, layer, hasTechnology, out var weapon))
            return false;
        ActivateWeapon(unit, weapon);
        return true;
    }

    public bool TryResolveWeapon(
        int unit,
        CombatTargetLayer layer,
        Func<int, bool> hasTechnology,
        out CombatWeaponProfileSnapshot weapon)
    {
        if ((uint)unit >= (uint)WeaponProfiles.Length ||
            layer == CombatTargetLayer.None)
        {
            weapon = default;
            return false;
        }
        var profiles = WeaponProfiles[unit];
        for (var index = 0; index < profiles.Length; index++)
        {
            var candidate = profiles[index];
            if ((candidate.TargetLayers & layer) == 0 ||
                !candidate.EnabledByDefault &&
                (candidate.RequiredTechnologyId < 0 ||
                 !hasTechnology(candidate.RequiredTechnologyId)))
                continue;
            weapon = candidate;
            return true;
        }
        weapon = default;
        return false;
    }

    public void SetAttackModifiers(
        int unit,
        float damageMultiplier,
        float damageAdd,
        float cooldownMultiplier)
    {
        AttackDamageMultipliers[unit] = MathF.Max(0f, damageMultiplier);
        AttackDamageAdds[unit] = damageAdd;
        AttackCooldownMultipliers[unit] = MathF.Max(0.001f, cooldownMultiplier);
        var profiles = WeaponProfiles[unit];
        var slot = ActiveWeaponSlots[unit];
        for (var index = 0; index < profiles.Length; index++)
        {
            if (profiles[index].Slot != slot) continue;
            ActivateWeapon(unit, profiles[index]);
            return;
        }
    }

    private void ConfigureWeapons(
        int unit,
        in CombatProfileSnapshot profile)
    {
        var weapons = profile.Weapons.IsDefaultOrEmpty
            ? ImmutableArray.Create(CombatWeaponProfileSnapshot.FromLegacy(profile))
            : profile.Weapons;
        WeaponProfiles[unit] = weapons;
        AttackDamageMultipliers[unit] = 1f;
        AttackDamageAdds[unit] = 0f;
        AttackCooldownMultipliers[unit] = 1f;
        ActivateWeapon(unit, weapons[0]);
    }

    private void ActivateWeapon(
        int unit,
        in CombatWeaponProfileSnapshot weapon)
    {
        ActiveWeaponSlots[unit] = weapon.Slot;
        AttackDamage[unit] = MathF.Max(
            0f, weapon.AttackDamage * AttackDamageMultipliers[unit] +
                AttackDamageAdds[unit]);
        AttackRanges[unit] = weapon.AttackRange;
        AttackCooldownDurations[unit] = MathF.Max(
            0.05f,
            weapon.AttackCooldownSeconds * AttackCooldownMultipliers[unit]);
        AttackWindupDurations[unit] = MathF.Min(
            weapon.AttackWindupSeconds, AttackCooldownDurations[unit]);
        PositioningKinds[unit] = weapon.Positioning;
        AttacksPerVolley[unit] = weapon.AttacksPerVolley;
        BonusVs[unit] = weapon.BonusVs;
        BonusDamage[unit] = weapon.BonusDamage;
        BaseUpgradeDamage[unit] = weapon.BaseUpgradeDamage;
        BonusUpgradeDamage[unit] = weapon.BonusUpgradeDamage;
        ProjectileSpeed[unit] = weapon.ProjectileSpeed;
        CanMoveDuringWindup[unit] = weapon.CanMoveDuringWindup;
        CanMoveDuringCooldown[unit] = weapon.CanMoveDuringCooldown;
        AttackTypes[unit] = weapon.AttackType;
        DamageUpgradeTechnologyIds[unit] =
            weapon.DamageUpgradeTechnologyId;
        MinimumAttackRanges[unit] = weapon.MinimumRange;
        WeaponAreas[unit] = weapon.Area;
        WeaponPropagations[unit] = weapon.Propagation;
    }

    private static void Copy<T>(T[] source, T[] destination) =>
        Array.Copy(source, destination, source.Length);

    public void SetCommand(
        int unit,
        UnitCommandIntent intent,
        Vector2 goal,
        int targetUnit = -1,
        int targetBuilding = -1)
    {
        CommandIntents[unit] = intent;
        Phases[unit] = intent is UnitCommandIntent.AttackMove or
            UnitCommandIntent.AttackTarget or
            UnitCommandIntent.Stop or UnitCommandIntent.Hold
            ? CombatPhase.Searching
            : CombatPhase.None;
        TargetUnits[unit] = intent == UnitCommandIntent.AttackTarget
            && targetUnit >= 0
            ? targetUnit
            : -1;
        TargetBuildings[unit] = intent == UnitCommandIntent.AttackTarget
            && targetBuilding >= 0
            ? targetBuilding
            : -1;
        TargetKinds[unit] = TargetUnits[unit] >= 0
            ? CombatTargetKind.Unit
            : TargetBuildings[unit] >= 0
                ? CombatTargetKind.Building
                : CombatTargetKind.None;
        HasAttackSlots[unit] = false;
        WindupRemaining[unit] = 0f;
        ChaseRepathRemaining[unit] = 0f;
        TargetLockRemaining[unit] = 0f;
        if (intent == UnitCommandIntent.AttackMove)
        {
            AttackMoveGoals[unit] = goal;
        }
    }
}
