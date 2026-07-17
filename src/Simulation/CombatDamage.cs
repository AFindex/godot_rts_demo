namespace RtsDemo.Simulation;

[Flags]
public enum CombatAttribute : ushort
{
    None = 0,
    Light = 1 << 0,
    Armored = 1 << 1,
    Biological = 1 << 2,
    Mechanical = 1 << 3,
    Structure = 1 << 4,
    Massive = 1 << 5,
    All = Light | Armored | Biological | Mechanical | Structure | Massive
}

/// <summary>
/// Content-facing attack classes. Legacy preserves the original demo's flat
/// armor subtraction; the remaining values use the classic Warcraft III
/// damage table compiled from Units/MiscGame.txt.
/// </summary>
public enum CombatAttackType : byte
{
    Legacy,
    Normal,
    Pierce,
    Siege,
    Magic,
    Chaos,
    Spells,
    Hero
}

public enum CombatArmorType : byte
{
    Legacy,
    Small,
    Medium,
    Large,
    Fortified,
    Normal,
    Hero,
    Divine,
    None
}

public enum CombatWeaponPropagationKind : byte
{
    None,
    Line,
    Bounce
}

/// <summary>
/// Secondary-target behavior for Warcraft III line and bounce missiles.
/// MaximumTargets includes the primary target. LineDistance may be unlocked
/// or extended by one technology without coupling combat to object rawcodes.
/// </summary>
public readonly record struct CombatWeaponPropagationSnapshot(
    CombatWeaponPropagationKind Kind,
    float LineDistance,
    float Radius,
    float DamageLossFactor,
    int MaximumTargets,
    CombatTargetLayer TargetLayers,
    int DistanceUpgradeTechnologyId = -1,
    float DistanceUpgradePerLevel = 0f)
{
    public static CombatWeaponPropagationSnapshot None => default;

    public float EffectiveLineDistance(int technologyLevel) =>
        MathF.Max(0f, LineDistance +
            Math.Max(0, technologyLevel) * DistanceUpgradePerLevel);

    public float DamageFraction(int secondaryIndex) =>
        secondaryIndex <= 0
            ? 1f
            : MathF.Pow(1f - DamageLossFactor, secondaryIndex);

    public void Validate()
    {
        if (!Enum.IsDefined(Kind) ||
            !float.IsFinite(LineDistance) || LineDistance < 0f ||
            !float.IsFinite(Radius) || Radius < 0f ||
            !float.IsFinite(DamageLossFactor) ||
            DamageLossFactor is < 0f or > 1f ||
            MaximumTargets is < 0 or > 32 ||
            (TargetLayers & ~CombatTargetLayer.All) != 0 ||
            DistanceUpgradeTechnologyId < -1 ||
            !float.IsFinite(DistanceUpgradePerLevel) ||
            DistanceUpgradePerLevel < 0f ||
            DistanceUpgradeTechnologyId < 0 && DistanceUpgradePerLevel != 0f)
            throw new ArgumentOutOfRangeException(
                nameof(CombatWeaponPropagationSnapshot));

        var empty = Kind == CombatWeaponPropagationKind.None;
        if (empty)
        {
            // default(T) leaves integer fields at zero; accept both zero and
            // the explicit -1 sentinel when the propagation is otherwise empty.
            if (LineDistance != 0f || Radius != 0f ||
                DamageLossFactor != 0f || MaximumTargets != 0 ||
                TargetLayers != CombatTargetLayer.None ||
                DistanceUpgradeTechnologyId is not (-1 or 0) ||
                DistanceUpgradePerLevel != 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(CombatWeaponPropagationSnapshot));
            return;
        }

        if (Radius <= 0f || TargetLayers == CombatTargetLayer.None ||
            MaximumTargets < 2 ||
            Kind == CombatWeaponPropagationKind.Line &&
                LineDistance <= 0f && DistanceUpgradePerLevel <= 0f ||
            Kind == CombatWeaponPropagationKind.Bounce &&
                (LineDistance != 0f || DistanceUpgradeTechnologyId != -1 ||
                 DistanceUpgradePerLevel != 0f))
            throw new ArgumentOutOfRangeException(
                nameof(CombatWeaponPropagationSnapshot));
    }
}

public readonly record struct CombatWeaponAreaSnapshot(
    float FullDamageRadius,
    float HalfDamageRadius,
    float QuarterDamageRadius,
    CombatTargetLayer TargetLayers)
{
    public static CombatWeaponAreaSnapshot None => default;
    public bool Enabled => QuarterDamageRadius > 0f &&
                           TargetLayers != CombatTargetLayer.None;

    public void Validate()
    {
        if (!float.IsFinite(FullDamageRadius) || FullDamageRadius < 0f ||
            !float.IsFinite(HalfDamageRadius) ||
            HalfDamageRadius < FullDamageRadius ||
            !float.IsFinite(QuarterDamageRadius) ||
            QuarterDamageRadius < HalfDamageRadius ||
            (TargetLayers & ~CombatTargetLayer.All) != 0 ||
            (QuarterDamageRadius == 0f) !=
                (TargetLayers == CombatTargetLayer.None))
            throw new ArgumentOutOfRangeException(
                nameof(CombatWeaponAreaSnapshot));
    }

    public float DamageFraction(float distance)
    {
        if (!Enabled || !float.IsFinite(distance) || distance < 0f ||
            distance > QuarterDamageRadius)
            return 0f;
        if (distance <= FullDamageRadius) return 1f;
        if (distance <= HalfDamageRadius) return 0.5f;
        return 0.25f;
    }
}

public readonly record struct CombatDamageRequest(
    float BaseDamage,
    int AttacksPerVolley,
    float Armor,
    CombatAttribute TargetAttributes,
    CombatAttribute BonusVs,
    float BonusDamage,
    int WeaponUpgradeLevel,
    float BaseUpgradeDamage,
    float BonusUpgradeDamage,
    float AvailableHealth,
    CombatAttackType AttackType = CombatAttackType.Legacy,
    CombatArmorType ArmorType = CombatArmorType.Legacy);

public readonly record struct CombatWeaponDamageSnapshot(
    float BaseDamage,
    int AttacksPerVolley,
    CombatAttribute BonusVs,
    float BonusDamage,
    int UpgradeLevel,
    float BaseUpgradeDamage,
    float BonusUpgradeDamage,
    CombatAttackType AttackType = CombatAttackType.Legacy,
    CombatWeaponAreaSnapshot Area = default,
    CombatWeaponPropagationSnapshot Propagation = default);

public readonly record struct CombatDefenseSnapshot(
    float Armor,
    CombatAttribute Attributes,
    CombatArmorType ArmorType = CombatArmorType.Legacy);

public readonly record struct CombatDamageResult(
    float DamagePerAttack,
    float TotalDamage,
    int AttacksApplied,
    float RemainingHealth,
    bool BonusApplied,
    bool Killed);

public static class CombatDamageResolver
{
    public const float MinimumDamagePerAttack = 0.5f;

    public static CombatDamageResult Resolve(
        CombatWeaponDamageSnapshot weapon,
        CombatDefenseSnapshot defense,
        float availableHealth) => Resolve(new CombatDamageRequest(
            weapon.BaseDamage,
            weapon.AttacksPerVolley,
            defense.Armor,
            defense.Attributes,
            weapon.BonusVs,
            weapon.BonusDamage,
            weapon.UpgradeLevel,
            weapon.BaseUpgradeDamage,
            weapon.BonusUpgradeDamage,
            availableHealth,
            weapon.AttackType,
            defense.ArmorType));

    public static CombatDamageResult Resolve(CombatDamageRequest request)
    {
        Validate(request);
        var bonus = request.BonusVs != CombatAttribute.None &&
                    (request.TargetAttributes & request.BonusVs) != 0;
        var raw = request.BaseDamage +
                  request.WeaponUpgradeLevel * request.BaseUpgradeDamage;
        if (bonus)
            raw += request.BonusDamage +
                   request.WeaponUpgradeLevel * request.BonusUpgradeDamage;
        var typed = request.AttackType != CombatAttackType.Legacy &&
                    request.ArmorType != CombatArmorType.Legacy;
        var perAttack = typed
            ? ResolveTypedDamage(
                raw, request.Armor, request.AttackType, request.ArmorType)
            : raw <= 0f
                ? 0f
                : MathF.Max(MinimumDamagePerAttack, raw - request.Armor);
        var remaining = request.AvailableHealth;
        var total = 0f;
        var applied = 0;
        for (var index = 0; index < request.AttacksPerVolley && remaining > 0f;
             index++)
        {
            var damage = MathF.Min(remaining, perAttack);
            remaining -= damage;
            total += damage;
            applied++;
        }
        return new CombatDamageResult(
            perAttack, total, applied, remaining, bonus, remaining <= 0f);
    }

    private static void Validate(CombatDamageRequest value)
    {
        if (!float.IsFinite(value.BaseDamage) || value.BaseDamage < 0f ||
            value.AttacksPerVolley is < 1 or > 32 ||
            !float.IsFinite(value.Armor) ||
            (value.TargetAttributes & ~CombatAttribute.All) != 0 ||
            (value.BonusVs & ~CombatAttribute.All) != 0 ||
            !float.IsFinite(value.BonusDamage) || value.BonusDamage < 0f ||
            value.WeaponUpgradeLevel is < 0 or > 255 ||
            !float.IsFinite(value.BaseUpgradeDamage) ||
            value.BaseUpgradeDamage < 0f ||
            !float.IsFinite(value.BonusUpgradeDamage) ||
            value.BonusUpgradeDamage < 0f ||
            !float.IsFinite(value.AvailableHealth) || value.AvailableHealth < 0f)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (!Enum.IsDefined(value.AttackType) ||
            !Enum.IsDefined(value.ArmorType) ||
            (value.AttackType == CombatAttackType.Legacy) !=
                (value.ArmorType == CombatArmorType.Legacy))
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static float ResolveTypedDamage(
        float raw,
        float armor,
        CombatAttackType attackType,
        CombatArmorType armorType)
    {
        if (raw <= 0f) return 0f;
        var typed = raw * TypeMultiplier(attackType, armorType);
        if (attackType == CombatAttackType.Spells || typed <= 0f)
            return typed;
        return typed * (armor >= 0f
            ? 1f / (1f + armor * 0.06f)
            : 2f - MathF.Pow(0.94f, -armor));
    }

    public static float TypeMultiplier(
        CombatAttackType attackType,
        CombatArmorType armorType)
    {
        if (attackType == CombatAttackType.Legacy ||
            armorType == CombatArmorType.Legacy ||
            !Enum.IsDefined(attackType) || !Enum.IsDefined(armorType))
            throw new ArgumentOutOfRangeException();
        ReadOnlySpan<float> row = attackType switch
        {
            CombatAttackType.Normal =>
                [1f, 1.5f, 1f, 0.7f, 1f, 1f, 0.05f, 1f],
            CombatAttackType.Pierce =>
                [2f, 0.75f, 1f, 0.35f, 1f, 0.5f, 0.05f, 1.5f],
            CombatAttackType.Siege =>
                [1f, 0.5f, 1f, 1.5f, 1f, 0.5f, 0.05f, 1.5f],
            CombatAttackType.Magic =>
                [1.25f, 0.75f, 2f, 0.35f, 1f, 0.5f, 0.05f, 1f],
            CombatAttackType.Chaos =>
                [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f],
            CombatAttackType.Spells =>
                [1f, 1f, 1f, 1f, 1f, 0.7f, 0.05f, 1f],
            CombatAttackType.Hero =>
                [1f, 1f, 1f, 0.5f, 1f, 1f, 0.05f, 1f],
            _ => throw new ArgumentOutOfRangeException(nameof(attackType))
        };
        return row[(int)armorType - 1];
    }
}
