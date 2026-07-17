using System.Globalization;
using System.Collections.Immutable;
using RtsDemo.Simulation;

namespace War3Rts.Data;

/// <summary>
/// Explicit conversion policy from Warcraft editor units to this project's
/// simulation plane. Costs and seconds retain their Warcraft values; spatial
/// values are converted because the deterministic world uses a smaller scale.
/// </summary>
public sealed record War3GameplayImportPolicy
{
    public static War3GameplayImportPolicy Default { get; } = new();

    public float WorldDistanceScale { get; init; } = 4f / 15f;
    public float MovementSpeedScale { get; init; } = 4f / 9f;
    public float AccelerationMultiplier { get; init; } = 5.5f;
    public float VisualHeightScale { get; init; } = 0.0075f;
    public float MinimumUnitRadius { get; init; } = 5.5f;
    public float MinimumMovementSpeed { get; init; } = 32f;
    public float LeashRangeMultiplier { get; init; } = 1.75f;
    public float DefaultProjectileSpeed { get; init; } = 430f;
    public float BuildTimeScale { get; init; } = 1f;
    public float ProductionTimeScale { get; init; } = 1f;
}

public sealed record War3GameplayImportReport(
    bool ManifestLoaded,
    int IndexedRecordCount,
    int AppliedUnitCount,
    int AppliedBuildingCount,
    IReadOnlyList<string> FallbackObjectIds,
    IReadOnlyDictionary<string, string> LoadErrors,
    string SourceError)
{
    public bool UsedFallbacks => FallbackObjectIds.Count > 0;

    public string LogLine =>
        $"manifest={(ManifestLoaded ? "loaded" : "fallback")} " +
        $"indexed={IndexedRecordCount} units={AppliedUnitCount} " +
        $"buildings={AppliedBuildingCount} fallbacks={FallbackObjectIds.Count}" +
        (SourceError.Length == 0 ? string.Empty : $" error={SourceError}");
}

/// <summary>
/// Pure gameplay/presentation adapter. It consumes the read-only data boundary
/// and emits existing simulation profiles; simulation systems remain unaware of
/// JSON, MPQ fields, object ids and Godot paths.
/// </summary>
public sealed class War3GameplayDataAdapter(
    IWar3UnitDataCatalog catalog,
    War3GameplayImportPolicy policy)
{
    public IWar3UnitDataCatalog Catalog { get; } = catalog;
    public War3GameplayImportPolicy Policy { get; } = policy;
    public IReadOnlyDictionary<string, int> WeaponUnlockTechnologies { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public War3UnitDefinition ApplyPresentation(War3UnitDefinition fallback)
    {
        if (!Catalog.TryGet(fallback.ObjectId, out var data)) return fallback;
        var flyingHeight = Positive(data.Summary.Movement.FlyingHeight)
            ? data.Summary.Movement.FlyingHeight!.Value * Policy.VisualHeightScale
            : fallback.FlyingHeight;
        var attack = FirstEnabledAttack(data);
        var projectileSource = fallback.ProjectileSource;
        if (IsProjectileWeapon(attack?.WeaponType))
            projectileSource = ModelPath(data.Assets.Missile, projectileSource);

        return fallback with
        {
            Name = Text(data.DisplayName, fallback.Name),
            ModelSource = ModelPath(data.Assets.Model, fallback.ModelSource),
            PortraitSource = ModelPath(data.Assets.Portrait, fallback.PortraitSource),
            IconPath = AssetPath(data.Assets.Icon, fallback.IconPath),
            FlyingHeight = flyingHeight,
            ProjectileSource = projectileSource,
            SpecialEffectSource = ModelPath(
                data.Assets.SpecialEffect, fallback.SpecialEffectSource),
            Level = data.Summary.Level is > 0
                ? data.Summary.Level.Value
                : fallback.Level,
            AttackClass = LocalizeAttackType(
                attack?.AttackType, fallback.AttackClass),
            ArmorClass = LocalizeArmorType(
                data.Summary.Armor.Type, fallback.ArmorClass)
        };
    }

    public War3BuildingDefinition ApplyPresentation(
        War3BuildingDefinition fallback)
    {
        if (!Catalog.TryGet(fallback.ObjectId, out var data)) return fallback;
        return fallback with
        {
            Name = Text(data.DisplayName, fallback.Name),
            ModelSource = ModelPath(data.Assets.Model, fallback.ModelSource),
            IconPath = AssetPath(data.Assets.Icon, fallback.IconPath),
            SpecialEffectSource = ModelPath(
                data.Assets.SpecialEffect, fallback.SpecialEffectSource),
            ArmorClass = LocalizeArmorType(
                data.Summary.Armor.Type, fallback.ArmorClass)
        };
    }

    public UnitTypeProfile ApplyUnitProfile(
        War3UnitDefinition binding,
        UnitTypeProfile fallback)
    {
        if (!Catalog.TryGet(binding.ObjectId, out var data)) return fallback;
        var movement = ApplyMovement(data, fallback.Movement);
        var combat = ApplyCombat(binding, data, fallback.Combat);
        var perception = ApplyPerception(data, fallback.Perception);
        return fallback with
        {
            Name = Text(data.DisplayName, fallback.Name),
            Movement = movement,
            Combat = combat,
            Perception = perception
        };
    }

    public BuildingTypeProfile ApplyBuildingProfile(
        War3BuildingDefinition binding,
        BuildingTypeProfile fallback)
    {
        if (!Catalog.TryGet(binding.ObjectId, out var data)) return fallback;
        var summary = data.Summary;
        var cost = fallback.Cost;
        if (NonNegative(summary.Cost.Gold) && NonNegative(summary.Cost.Lumber))
            cost = new EconomyCost(
                summary.Cost.Gold!.Value,
                summary.Cost.Lumber!.Value,
                fallback.Cost.Supply);
        return fallback with
        {
            Name = Text(data.DisplayName, fallback.Name),
            Cost = cost,
            BuildSeconds = Positive(summary.Cost.BuildTime)
                ? summary.Cost.BuildTime!.Value * Policy.BuildTimeScale
                : fallback.BuildSeconds,
            MaximumHealth = Positive(summary.HitPoints.Effective)
                ? summary.HitPoints.Effective!.Value
                : fallback.MaximumHealth,
            SupplyProvided = NonNegative(summary.Cost.FoodProduced)
                ? summary.Cost.FoodProduced!.Value
                : fallback.SupplyProvided,
            Armor = NonNegative(summary.Armor.Effective)
                ? summary.Armor.Effective!.Value
                : fallback.Armor,
            ArmorUpgradePerLevel = NonNegative(summary.Armor.UpgradeAmount)
                ? summary.Armor.UpgradeAmount!.Value
                : fallback.ArmorUpgradePerLevel
        };
    }

    public ProductionRecipeProfile ApplyRecipe(
        War3UnitDefinition binding,
        ProductionRecipeProfile fallback,
        UnitTypeProfile adaptedUnit)
    {
        if (!Catalog.TryGet(binding.ObjectId, out var data))
            return fallback with { UnitType = adaptedUnit };
        var summary = data.Summary;
        var cost = fallback.Cost;
        if (NonNegative(summary.Cost.Gold) &&
            NonNegative(summary.Cost.Lumber) &&
            Positive(summary.Cost.FoodUsed))
            cost = new EconomyCost(
                summary.Cost.Gold!.Value,
                summary.Cost.Lumber!.Value,
                summary.Cost.FoodUsed!.Value);
        return fallback with
        {
            Name = $"训练{Text(data.DisplayName, adaptedUnit.Name)}",
            UnitType = adaptedUnit,
            Cost = cost,
            ProductionSeconds = Positive(summary.Cost.BuildTime)
                ? summary.Cost.BuildTime!.Value * Policy.ProductionTimeScale
                : fallback.ProductionSeconds
        };
    }

    public BuildingUpgradeProfile[] CreateBuildingUpgrades(
        IReadOnlyList<War3BuildingDefinition> buildings,
        IReadOnlyList<BuildingTypeProfile> profiles)
    {
        var buildingIds = buildings.ToDictionary(
            value => value.ObjectId,
            value => value.TypeId,
            StringComparer.Ordinal);
        var result = new List<BuildingUpgradeProfile>();
        foreach (var source in buildings.OrderBy(value => value.TypeId))
        {
            if (!Catalog.TryGet(source.ObjectId, out var sourceData)) continue;
            var targetObjectId = EditorValue(sourceData, "Upgrade");
            if (string.IsNullOrWhiteSpace(targetObjectId) ||
                !buildingIds.TryGetValue(targetObjectId, out var targetId))
                continue;
            var sourceProfile = profiles[source.TypeId];
            var targetProfile = profiles[targetId];
            var requirements = ImmutableArray<TechnologyRequirementProfile>.Empty;
            if (Catalog.TryGet(targetObjectId, out var targetData))
            {
                requirements = EditorList(targetData, "Requires")
                    .Where(buildingIds.ContainsKey)
                    .Select(value => new TechnologyRequirementProfile(
                        TechnologyRequirementKind.CompletedBuilding,
                        buildingIds[value],
                        1))
                    .Distinct()
                    .ToImmutableArray();
            }
            result.Add(new BuildingUpgradeProfile(
                result.Count,
                $"升级为{targetProfile.Name}",
                source.TypeId,
                targetProfile,
                new EconomyCost(
                    Math.Max(0,
                        targetProfile.Cost.Minerals -
                        sourceProfile.Cost.Minerals),
                    Math.Max(0,
                        targetProfile.Cost.VespeneGas -
                        sourceProfile.Cost.VespeneGas)),
                targetProfile.BuildSeconds,
                targetProfile.CancelRefundFraction)
            {
                Requirements = requirements
            });
        }
        return result.ToArray();
    }

    public War3GameplayImportReport CreateReport(
        IEnumerable<string> unitObjectIds,
        IEnumerable<string> buildingObjectIds)
    {
        var units = unitObjectIds.Distinct(StringComparer.Ordinal).ToArray();
        var buildings = buildingObjectIds.Distinct(StringComparer.Ordinal).ToArray();
        var appliedUnits = units.Count(id => Catalog.TryGet(id, out _));
        var appliedBuildings = buildings.Count(id => Catalog.TryGet(id, out _));
        var fallbacks = units.Concat(buildings)
            .Where(id => !Catalog.TryGet(id, out _))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new War3GameplayImportReport(
            Catalog.IsAvailable,
            Catalog.Count,
            appliedUnits,
            appliedBuildings,
            fallbacks,
            Catalog.LoadErrors,
            Catalog.Error);
    }

    private UnitMovementProfileSnapshot ApplyMovement(
        War3UnitData data,
        UnitMovementProfileSnapshot fallback)
    {
        var radius = Positive(data.Summary.Movement.CollisionSize)
            ? MathF.Max(
                Policy.MinimumUnitRadius,
                data.Summary.Movement.CollisionSize!.Value * Policy.WorldDistanceScale)
            : fallback.PhysicalRadius;
        var speed = Positive(data.Summary.Movement.Speed)
            ? MathF.Max(
                Policy.MinimumMovementSpeed,
                data.Summary.Movement.Speed!.Value * Policy.MovementSpeedScale)
            : fallback.MaximumSpeed;
        var clearance = MovementClearance.FromPhysicalRadius(radius);
        var name = Text(data.DisplayName, fallback.Name);
        return new UnitMovementProfileSnapshot(
            fallback.Id,
            name,
            radius,
            speed,
            speed * Policy.AccelerationMultiplier,
            clearance.Class,
            clearance.NavigationRadius);
    }

    private CombatProfileSnapshot ApplyCombat(
        War3UnitDefinition binding,
        War3UnitData data,
        CombatProfileSnapshot fallback)
    {
        var maximumHealth = Positive(data.Summary.HitPoints.Effective)
            ? data.Summary.HitPoints.Effective!.Value
            : fallback.MaximumHealth;
        var armor = NonNegative(data.Summary.Armor.Effective)
            ? data.Summary.Armor.Effective!.Value
            : fallback.Armor;
        var attack = FirstEnabledAttack(data);
        if (attack is null)
            return fallback with { MaximumHealth = maximumHealth, Armor = armor };

        var weaponValues = data.Summary.Combat.Attacks
            .Select((value, slot) => new
            {
                Attack = value,
                Slot = slot,
                RequiredTechnology = WeaponUnlockTechnologies.TryGetValue(
                    WeaponUnlockKey(binding.ObjectId, slot), out var technology)
                    ? technology
                    : -1
            })
            .Where(value => value.Attack.Enabled || value.RequiredTechnology >= 0)
            .Select(value => ApplyWeapon(
                data, value.Attack, value.Slot,
                value.RequiredTechnology, fallback))
            .OrderBy(value => value.Slot)
            .ToImmutableArray();
        var primary = weaponValues.First(value => value.EnabledByDefault);
        var range = primary.AttackRange;
        var acquisition = NonNegative(data.Summary.Combat.AcquisitionRange)
            ? data.Summary.Combat.AcquisitionRange!.Value * Policy.WorldDistanceScale
            : fallback.AcquisitionRange;
        acquisition = MathF.Max(
            acquisition,
            weaponValues.Select(value => value.AttackRange).DefaultIfEmpty(range).Max());
        return fallback with
        {
            MaximumHealth = maximumHealth,
            AttackDamage = primary.AttackDamage,
            AttackRange = range,
            AcquisitionRange = acquisition,
            AttackCooldownSeconds = primary.AttackCooldownSeconds,
            AttackWindupSeconds = primary.AttackWindupSeconds,
            LeashDistance = MathF.Max(
                acquisition * Policy.LeashRangeMultiplier,
                acquisition + MathF.Max(20f, range * 0.25f)),
            Positioning = primary.Positioning,
            Armor = armor,
            AttacksPerVolley = primary.AttacksPerVolley,
            BonusVs = primary.BonusVs,
            BonusDamage = primary.BonusDamage,
            BaseUpgradeDamage = primary.BaseUpgradeDamage,
            BonusUpgradeDamage = primary.BonusUpgradeDamage,
            ProjectileSpeed = primary.ProjectileSpeed,
            CanMoveDuringWindup = primary.CanMoveDuringWindup,
            CanMoveDuringCooldown = primary.CanMoveDuringCooldown,
            Weapons = weaponValues
        };
    }

    private CombatWeaponProfileSnapshot ApplyWeapon(
        War3UnitData data,
        War3AttackSummary attack,
        int slot,
        int requiredTechnology,
        in CombatProfileSnapshot fallback)
    {
        var range = NonNegative(attack.Range)
            ? attack.Range!.Value * Policy.WorldDistanceScale
            : fallback.AttackRange;
        var cooldown = Positive(attack.Cooldown)
            ? attack.Cooldown!.Value
            : fallback.AttackCooldownSeconds;
        var windup = NonNegative(attack.Timing.DamagePoint)
            ? Math.Clamp(attack.Timing.DamagePoint!.Value, 0f, cooldown)
            : MathF.Min(fallback.AttackWindupSeconds, cooldown);
        var damage = NonNegative(attack.Damage.Average)
            ? attack.Damage.Average!.Value + PrimaryHeroDamage(data)
            : fallback.AttackDamage;
        var projectile = IsProjectileWeapon(attack.WeaponType);
        var projectileSpeed = projectile
            ? ReadMissileSpeed(data) * Policy.WorldDistanceScale
            : 0f;
        if (projectile && projectileSpeed <= 0f)
            projectileSpeed = Policy.DefaultProjectileSpeed;
        return new CombatWeaponProfileSnapshot(
            slot,
            TargetLayers(attack.Targets),
            attack.Enabled,
            requiredTechnology,
            damage,
            range,
            cooldown,
            windup,
            projectile || range > 45f
                ? CombatPositioningKind.Ranged
                : CombatPositioningKind.Melee,
            fallback.AttacksPerVolley,
            fallback.BonusVs,
            fallback.BonusDamage,
            NonNegative(attack.Damage.UpgradeAmount)
                ? attack.Damage.UpgradeAmount!.Value
                : fallback.BaseUpgradeDamage,
            fallback.BonusUpgradeDamage,
            projectileSpeed,
            fallback.CanMoveDuringWindup,
            fallback.CanMoveDuringCooldown);
    }

    public static string WeaponUnlockKey(string objectId, int slot) =>
        $"{objectId}:{slot}";

    private static CombatTargetLayer TargetLayers(IEnumerable<string> values)
    {
        var result = CombatTargetLayer.None;
        foreach (var value in values)
        {
            result |= value.ToLowerInvariant() switch
            {
                "air" => CombatTargetLayer.AirUnit,
                "ground" => CombatTargetLayer.GroundUnit,
                "structure" => CombatTargetLayer.Building,
                _ => CombatTargetLayer.None
            };
        }
        return result == CombatTargetLayer.None ? CombatTargetLayer.All : result;
    }

    private UnitPerceptionProfileSnapshot ApplyPerception(
        War3UnitData data,
        UnitPerceptionProfileSnapshot fallback)
    {
        var vision = Positive(data.Summary.Sight.Day)
            ? data.Summary.Sight.Day!.Value * Policy.WorldDistanceScale
            : fallback.VisionRange;
        var flying = data.Summary.Movement.Type?.Equals(
                         "fly", StringComparison.OrdinalIgnoreCase) == true ||
                     Positive(data.Summary.Movement.FlyingHeight);
        return fallback with
        {
            VisionRange = vision,
            TerrainVisionMode = flying
                ? TerrainVisionMode.Elevated
                : TerrainVisionMode.Ground
        };
    }

    private static War3AttackSummary? FirstEnabledAttack(War3UnitData data) =>
        data.Summary.Combat.Attacks.FirstOrDefault(value => value.Enabled);

    private static float PrimaryHeroDamage(War3UnitData data)
    {
        if (!data.Identity.IsHero) return 0f;
        var attributes = data.Summary.HeroAttributes;
        return attributes.Primary?.ToUpperInvariant() switch
        {
            "STR" => attributes.Strength ?? 0f,
            "AGI" => attributes.Agility ?? 0f,
            "INT" => attributes.Intelligence ?? 0f,
            _ => 0f
        };
    }

    private static float ReadMissileSpeed(War3UnitData data)
    {
        foreach (var table in data.Editor.Values)
        {
            var value = table.FirstOrDefault(pair => pair.Key.Equals(
                "Missilespeed", StringComparison.OrdinalIgnoreCase)).Value;
            if (float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var speed) && speed > 0f)
                return speed;
        }
        return 0f;
    }

    private static string EditorValue(War3UnitData data, string field)
    {
        foreach (var table in data.Editor.Values)
        foreach (var value in table)
        {
            if (value.Key.Equals(field, StringComparison.OrdinalIgnoreCase))
                return value.Value?.Trim() ?? string.Empty;
        }
        return string.Empty;
    }

    private static IEnumerable<string> EditorList(
        War3UnitData data,
        string field) =>
        EditorValue(data, field)
            .Split(',', StringSplitOptions.TrimEntries |
                        StringSplitOptions.RemoveEmptyEntries)
            .Where(value => value is not "_" and not "-");

    private static bool IsProjectileWeapon(string? value) =>
        value?.ToLowerInvariant() is "missile" or "artillery" or
            "mline" or "mbounce" or "msplash";

    private static string LocalizeAttackType(string? value, string fallback) =>
        value?.ToLowerInvariant() switch
        {
            "normal" => "普通攻击",
            "pierce" => "穿刺攻击",
            "siege" => "攻城攻击",
            "magic" => "魔法攻击",
            "chaos" => "混乱攻击",
            "hero" => "英雄攻击",
            "spells" => "法术攻击",
            _ => fallback
        };

    private static string LocalizeArmorType(string? value, string fallback) =>
        value?.ToLowerInvariant() switch
        {
            "unarmored" => "无甲",
            "small" => "轻甲",
            "medium" => "中甲",
            "large" => "重甲",
            "fort" => "城甲",
            "hero" => "英雄甲",
            "divine" => "神圣甲",
            "none" => "无护甲",
            _ => fallback
        };

    private static string Text(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string AssetPath(
        War3UnitAssetReference? asset,
        string fallback)
    {
        var path = asset?.ResolvedPath;
        if (string.IsNullOrWhiteSpace(path)) path = asset?.RequestedPath;
        return string.IsNullOrWhiteSpace(path)
            ? fallback
            : FirstPath(path).Replace('/', '\\');
    }

    private static string ModelPath(
        War3UnitAssetReference? asset,
        string fallback)
    {
        var path = AssetPath(asset, fallback);
        if (string.IsNullOrWhiteSpace(path)) return fallback;
        var extension = Path.GetExtension(path);
        if (extension.Length == 0) return path + ".mdx";
        return extension.Equals(".mdl", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(path, ".mdx")
            : path;
    }

    private static string FirstPath(string path) =>
        path.Split(',', StringSplitOptions.TrimEntries |
                        StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

    private static bool Positive(float? value) =>
        value is > 0f && float.IsFinite(value.Value);

    private static bool Positive(int? value) => value is > 0;

    private static bool NonNegative(float? value) =>
        value is >= 0f && float.IsFinite(value.Value);

    private static bool NonNegative(int? value) => value is >= 0;
}
