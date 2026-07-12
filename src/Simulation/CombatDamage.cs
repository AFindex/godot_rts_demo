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
    float AvailableHealth);

public readonly record struct CombatWeaponDamageSnapshot(
    float BaseDamage,
    int AttacksPerVolley,
    CombatAttribute BonusVs,
    float BonusDamage,
    int UpgradeLevel,
    float BaseUpgradeDamage,
    float BonusUpgradeDamage);

public readonly record struct CombatDefenseSnapshot(
    float Armor,
    CombatAttribute Attributes);

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
            availableHealth));

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
        var perAttack = raw <= 0f
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
            !float.IsFinite(value.Armor) || value.Armor < 0f ||
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
    }
}
