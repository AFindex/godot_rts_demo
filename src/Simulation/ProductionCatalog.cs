using System.Text;
using System.Collections.Immutable;

namespace RtsDemo.Simulation;

public readonly record struct UnitTypeProfile(
    int Id,
    string Name,
    UnitMovementProfileSnapshot Movement,
    CombatProfileSnapshot Combat,
    bool IsWorker)
{
    public UnitPerceptionProfileSnapshot Perception { get; init; } =
        UnitPerceptionProfileSnapshot.Standard;
}

public readonly record struct ProductionRecipeProfile(
    int Id,
    string Name,
    int ProducerBuildingTypeId,
    UnitTypeProfile UnitType,
    EconomyCost Cost,
    float ProductionSeconds,
    float CancelRefundFraction)
{
    public ImmutableArray<ProductionRequirementProfile> Requirements { get; init; } = [];
}

public enum ProductionRequirementKind : byte
{
    CompletedBuilding
}

public readonly record struct ProductionRequirementProfile(
    ProductionRequirementKind Kind,
    int TypeId,
    int Count);

public enum ProductionCatalogErrorCode
{
    None,
    UnsupportedFormatVersion,
    EmptyUnitTypes,
    EmptyRecipes,
    NonDenseUnitTypeId,
    NonDenseRecipeId,
    InvalidUnitType,
    InvalidRecipe,
    DuplicateName,
    RecipeUnitMismatch,
    MissingResourceAsset,
    NullResourceElement
}

public readonly record struct ProductionCatalogValidationResult(
    ProductionCatalogErrorCode Code,
    int Index,
    string Message)
{
    public bool IsValid => Code == ProductionCatalogErrorCode.None;
}

public sealed class ProductionCatalogSnapshot
{
    public const int CurrentFormatVersion = 12;
    private readonly UnitTypeProfile[] _unitTypes;
    private readonly ProductionRecipeProfile[] _recipes;
    private readonly byte[] _canonicalBytes;

    private ProductionCatalogSnapshot(
        int formatVersion,
        UnitTypeProfile[] unitTypes,
        ProductionRecipeProfile[] recipes)
    {
        FormatVersion = formatVersion;
        _unitTypes = unitTypes;
        _recipes = recipes;
        _canonicalBytes = BuildCanonicalBytes();
        StableHash = StableHash64.Compute(_canonicalBytes);
    }

    public int FormatVersion { get; }
    public ReadOnlySpan<UnitTypeProfile> UnitTypes => _unitTypes;
    public ReadOnlySpan<ProductionRecipeProfile> Recipes => _recipes;
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");
    public UnitTypeProfile UnitType(int id) => _unitTypes[id];
    public ProductionRecipeProfile Recipe(int id) => _recipes[id];

    public static bool TryCreate(
        int formatVersion,
        ReadOnlySpan<UnitTypeProfile> unitTypes,
        ReadOnlySpan<ProductionRecipeProfile> recipes,
        out ProductionCatalogSnapshot? snapshot,
        out ProductionCatalogValidationResult validation)
    {
        var units = unitTypes.ToArray();
        var recipeCopy = recipes.ToArray();
        validation = Validate(formatVersion, units, recipeCopy);
        if (!validation.IsValid)
        {
            snapshot = null;
            return false;
        }
        snapshot = new ProductionCatalogSnapshot(
            formatVersion, units, recipeCopy);
        return true;
    }

    private static ProductionCatalogValidationResult Validate(
        int formatVersion,
        UnitTypeProfile[] units,
        ProductionRecipeProfile[] recipes)
    {
        if (formatVersion != CurrentFormatVersion)
            return Failure(ProductionCatalogErrorCode.UnsupportedFormatVersion, -1,
                $"Expected production catalog {CurrentFormatVersion}, got {formatVersion}.");
        if (units.Length == 0)
            return Failure(ProductionCatalogErrorCode.EmptyUnitTypes, -1,
                "At least one unit type is required.");
        if (recipes.Length == 0)
            return Failure(ProductionCatalogErrorCode.EmptyRecipes, -1,
                "At least one production recipe is required.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            if (unit.Id != index)
                return Failure(ProductionCatalogErrorCode.NonDenseUnitTypeId, index,
                    $"Unit type ID must equal dense index {index}.");
            if (!ValidUnitProfile(unit))
                return Failure(ProductionCatalogErrorCode.InvalidUnitType, index,
                    "Unit movement and combat fields must be finite and positive.");
            if (!names.Add(unit.Name))
                return Failure(ProductionCatalogErrorCode.DuplicateName, index,
                    $"Duplicate production name '{unit.Name}'.");
        }
        for (var index = 0; index < recipes.Length; index++)
        {
            var recipe = recipes[index];
            if (recipe.Id != index)
                return Failure(ProductionCatalogErrorCode.NonDenseRecipeId, index,
                    $"Recipe ID must equal dense index {index}.");
            if (string.IsNullOrWhiteSpace(recipe.Name) ||
                recipe.ProducerBuildingTypeId < 0 || !recipe.Cost.IsValid ||
                recipe.Cost.Supply <= 0 || !Positive(recipe.ProductionSeconds) ||
                !float.IsFinite(recipe.CancelRefundFraction) ||
                recipe.CancelRefundFraction is < 0f or > 1f)
                return Failure(ProductionCatalogErrorCode.InvalidRecipe, index,
                    "Recipe producer, cost, supply, duration and refund must be valid.");
            if (!ValidRequirements(recipe.Requirements))
                return Failure(ProductionCatalogErrorCode.InvalidRecipe, index,
                    "Recipe requirements must be unique completed-building counts.");
            if ((uint)recipe.UnitType.Id >= (uint)units.Length ||
                !UnitTypeEquals(recipe.UnitType, units[recipe.UnitType.Id]))
                return Failure(ProductionCatalogErrorCode.RecipeUnitMismatch, index,
                    "Recipe unit must exactly match a catalog unit type.");
            if (!names.Add(recipe.Name))
                return Failure(ProductionCatalogErrorCode.DuplicateName, index,
                    $"Duplicate production name '{recipe.Name}'.");
        }
        return new ProductionCatalogValidationResult(
            ProductionCatalogErrorCode.None, -1, string.Empty);
    }

    private byte[] BuildCanonicalBytes()
    {
        using var stream = new MemoryStream(1024);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        writer.Write(_unitTypes.Length);
        foreach (var unit in _unitTypes) WriteUnit(writer, unit);
        writer.Write(_recipes.Length);
        foreach (var recipe in _recipes)
        {
            writer.Write(recipe.Id);
            WriteString(writer, recipe.Name);
            writer.Write(recipe.ProducerBuildingTypeId);
            writer.Write(recipe.UnitType.Id);
            writer.Write(recipe.Cost.Minerals);
            writer.Write(recipe.Cost.VespeneGas);
            writer.Write(recipe.Cost.Supply);
            writer.Write(BitConverter.SingleToInt32Bits(recipe.ProductionSeconds));
            writer.Write(BitConverter.SingleToInt32Bits(recipe.CancelRefundFraction));
            writer.Write(recipe.Requirements.Length);
            foreach (var requirement in recipe.Requirements)
            {
                writer.Write((byte)requirement.Kind);
                writer.Write(requirement.TypeId);
                writer.Write(requirement.Count);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteUnit(BinaryWriter writer, UnitTypeProfile unit)
    {
        writer.Write(unit.Id);
        WriteString(writer, unit.Name);
        writer.Write(BitConverter.SingleToInt32Bits(unit.Movement.PhysicalRadius));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Movement.MaximumSpeed));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Movement.Acceleration));
        writer.Write(BitConverter.SingleToInt32Bits(
            unit.Movement.TurnRateRadiansPerSecond));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.MaximumHealth));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.AttackDamage));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.AttackRange));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.AcquisitionRange));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.AttackCooldownSeconds));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.AttackWindupSeconds));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.LeashDistance));
        writer.Write((byte)unit.Combat.Positioning);
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.Armor));
        writer.Write((ushort)unit.Combat.Attributes);
        writer.Write(unit.Combat.AttacksPerVolley);
        writer.Write((ushort)unit.Combat.BonusVs);
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.BonusDamage));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.BaseUpgradeDamage));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.BonusUpgradeDamage));
        writer.Write(BitConverter.SingleToInt32Bits(unit.Combat.ProjectileSpeed));
        writer.Write(unit.Combat.CanMoveDuringWindup);
        writer.Write(unit.Combat.CanMoveDuringCooldown);
        writer.Write(unit.Combat.AutoTargetPriority);
        writer.Write((byte)unit.Combat.ArmorType);
        writer.Write(unit.Combat.ArmorUpgradeTechnologyId);
        writer.Write(BitConverter.SingleToInt32Bits(
            unit.Combat.ArmorUpgradePerLevel));
        writer.Write(BitConverter.SingleToInt32Bits(
            unit.Combat.AttackHalfAngleRadians));
        writer.Write(unit.Combat.Weapons.Length);
        foreach (var weapon in unit.Combat.Weapons)
        {
            writer.Write(weapon.Slot);
            writer.Write((byte)weapon.TargetLayers);
            writer.Write(weapon.EnabledByDefault);
            writer.Write(weapon.RequiredTechnologyId);
            writer.Write(BitConverter.SingleToInt32Bits(weapon.AttackDamage));
            writer.Write(BitConverter.SingleToInt32Bits(weapon.AttackRange));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.AttackCooldownSeconds));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.AttackWindupSeconds));
            writer.Write((byte)weapon.Positioning);
            writer.Write(weapon.AttacksPerVolley);
            writer.Write((ushort)weapon.BonusVs);
            writer.Write(BitConverter.SingleToInt32Bits(weapon.BonusDamage));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.BaseUpgradeDamage));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.BonusUpgradeDamage));
            writer.Write(BitConverter.SingleToInt32Bits(weapon.ProjectileSpeed));
            writer.Write(weapon.CanMoveDuringWindup);
            writer.Write(weapon.CanMoveDuringCooldown);
            writer.Write((byte)weapon.AttackType);
            writer.Write(weapon.DamageUpgradeTechnologyId);
            writer.Write(BitConverter.SingleToInt32Bits(weapon.MinimumRange));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.Area.FullDamageRadius));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.Area.HalfDamageRadius));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.Area.QuarterDamageRadius));
            writer.Write((byte)weapon.Area.TargetLayers);
            writer.Write((byte)weapon.Propagation.Kind);
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.Propagation.LineDistance));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.Propagation.Radius));
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.Propagation.DamageLossFactor));
            writer.Write(weapon.Propagation.MaximumTargets);
            writer.Write((byte)weapon.Propagation.TargetLayers);
            writer.Write(weapon.Propagation.DistanceUpgradeTechnologyId);
            writer.Write(BitConverter.SingleToInt32Bits(
                weapon.Propagation.DistanceUpgradePerLevel));
        }
        writer.Write((byte)unit.Perception.Concealment);
        writer.Write(BitConverter.SingleToInt32Bits(
            unit.Perception.DetectionRange));
        writer.Write(BitConverter.SingleToInt32Bits(
            unit.Perception.VisionRange));
        writer.Write(BitConverter.SingleToInt32Bits(
            unit.Perception.ObservationHeight));
        writer.Write((byte)unit.Perception.TerrainVisionMode);
        writer.Write(unit.IsWorker);
    }

    internal static bool ValidUnitProfile(UnitTypeProfile unit) =>
        !string.IsNullOrWhiteSpace(unit.Name) &&
        unit.Movement.Id == unit.Id &&
        unit.Movement.Name == unit.Name &&
        Positive(unit.Movement.PhysicalRadius) &&
        unit.Movement.MovementClass ==
            MovementClearance.FromPhysicalRadius(
                unit.Movement.PhysicalRadius).Class &&
        unit.Movement.NavigationRadius ==
            MovementClearance.FromPhysicalRadius(
                unit.Movement.PhysicalRadius).NavigationRadius &&
        Positive(unit.Movement.MaximumSpeed) &&
        Positive(unit.Movement.Acceleration) &&
        Positive(unit.Movement.TurnRateRadiansPerSecond) &&
        Positive(unit.Combat.MaximumHealth) &&
        unit.Combat.AttackDamage >= 0f && float.IsFinite(unit.Combat.AttackDamage) &&
        unit.Combat.AttackRange >= 0f && float.IsFinite(unit.Combat.AttackRange) &&
        Positive(unit.Combat.AcquisitionRange) &&
        unit.Combat.AcquisitionRange >= unit.Combat.AttackRange &&
        Positive(unit.Combat.AttackCooldownSeconds) &&
        unit.Combat.AttackWindupSeconds >= 0f &&
        float.IsFinite(unit.Combat.AttackWindupSeconds) &&
        unit.Combat.AttackWindupSeconds <= unit.Combat.AttackCooldownSeconds &&
        Positive(unit.Combat.LeashDistance) &&
        unit.Combat.LeashDistance >= unit.Combat.AcquisitionRange &&
        Enum.IsDefined(unit.Combat.Positioning) &&
        float.IsFinite(unit.Combat.Armor) &&
        (unit.Combat.Attributes & ~CombatAttribute.All) == 0 &&
        unit.Combat.AttacksPerVolley is >= 1 and <= 32 &&
        (unit.Combat.BonusVs & ~CombatAttribute.All) == 0 &&
        unit.Combat.BonusDamage >= 0f && float.IsFinite(unit.Combat.BonusDamage) &&
        unit.Combat.BaseUpgradeDamage >= 0f &&
        float.IsFinite(unit.Combat.BaseUpgradeDamage) &&
        unit.Combat.BonusUpgradeDamage >= 0f &&
        float.IsFinite(unit.Combat.BonusUpgradeDamage) &&
        unit.Combat.ProjectileSpeed >= 0f &&
        float.IsFinite(unit.Combat.ProjectileSpeed) &&
        Enum.IsDefined(unit.Combat.ArmorType) &&
        unit.Combat.ArmorUpgradeTechnologyId >= -1 &&
        unit.Combat.ArmorUpgradePerLevel >= 0f &&
        float.IsFinite(unit.Combat.ArmorUpgradePerLevel) &&
        (unit.Combat.ArmorUpgradeTechnologyId >= 0 ||
         unit.Combat.ArmorUpgradePerLevel == 0f) &&
        float.IsFinite(unit.Combat.AttackHalfAngleRadians) &&
        unit.Combat.AttackHalfAngleRadians is >= 0f and <= MathF.PI &&
        ValidWeapons(unit.Combat.Weapons) &&
        ValidPerception(unit.Perception);

    private static bool ValidWeapons(
        ImmutableArray<CombatWeaponProfileSnapshot> weapons)
    {
        if (weapons.IsDefault || weapons.Length > 8) return false;
        var slots = new HashSet<int>();
        try
        {
            foreach (var weapon in weapons)
            {
                weapon.Validate();
                if (!slots.Add(weapon.Slot)) return false;
            }
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ValidPerception(UnitPerceptionProfileSnapshot value) =>
        Enum.IsDefined(value.Concealment) &&
        float.IsFinite(value.DetectionRange) && value.DetectionRange >= 0f &&
        float.IsFinite(value.VisionRange) && value.VisionRange > 0f &&
        float.IsFinite(value.ObservationHeight) && value.ObservationHeight >= 0f &&
        Enum.IsDefined(value.TerrainVisionMode);

    internal static bool ValidRequirements(
        ImmutableArray<ProductionRequirementProfile> requirements)
    {
        if (requirements.IsDefault || requirements.Length > 32) return false;
        var keys = new HashSet<(ProductionRequirementKind, int)>();
        foreach (var requirement in requirements)
        {
            if (!Enum.IsDefined(requirement.Kind) || requirement.TypeId < 0 ||
                requirement.Count <= 0 ||
                !keys.Add((requirement.Kind, requirement.TypeId)))
                return false;
        }
        return true;
    }

    public static bool RecipeEquals(
        ProductionRecipeProfile left,
        ProductionRecipeProfile right) =>
        left with { Requirements = default, UnitType = default } ==
            right with { Requirements = default, UnitType = default } &&
        UnitTypeEquals(left.UnitType, right.UnitType) &&
        left.Requirements.AsSpan().SequenceEqual(right.Requirements.AsSpan());

    public static bool UnitTypeEquals(
        UnitTypeProfile left,
        UnitTypeProfile right) =>
        left with
        {
            Combat = left.Combat with { Weapons = default }
        } == right with
        {
            Combat = right.Combat with { Weapons = default }
        } &&
        left.Combat.Weapons.AsSpan().SequenceEqual(
            right.Combat.Weapons.AsSpan());

    private static bool Positive(float value) => float.IsFinite(value) && value > 0f;
    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
    private static ProductionCatalogValidationResult Failure(
        ProductionCatalogErrorCode code, int index, string message) =>
        new(code, index, message);
}

public readonly record struct ProductionCatalogDiff(
    bool Changed,
    int ChangedUnitTypes,
    int ChangedRecipes)
{
    public static ProductionCatalogDiff Compare(
        ProductionCatalogSnapshot current,
        ProductionCatalogSnapshot candidate) => new(
        current.StableHash != candidate.StableHash,
        CountChangedUnitTypes(current.UnitTypes, candidate.UnitTypes),
        CountChangedRecipes(current.Recipes, candidate.Recipes));

    private static int CountChanged<T>(ReadOnlySpan<T> current, ReadOnlySpan<T> candidate)
        where T : IEquatable<T>
    {
        var shared = Math.Min(current.Length, candidate.Length);
        var changed = Math.Abs(current.Length - candidate.Length);
        for (var index = 0; index < shared; index++)
            changed += current[index].Equals(candidate[index]) ? 0 : 1;
        return changed;
    }

    private static int CountChangedUnitTypes(
        ReadOnlySpan<UnitTypeProfile> current,
        ReadOnlySpan<UnitTypeProfile> candidate)
    {
        var shared = Math.Min(current.Length, candidate.Length);
        var changed = Math.Abs(current.Length - candidate.Length);
        for (var index = 0; index < shared; index++)
            changed += ProductionCatalogSnapshot.UnitTypeEquals(
                current[index], candidate[index]) ? 0 : 1;
        return changed;
    }

    private static int CountChangedRecipes(
        ReadOnlySpan<ProductionRecipeProfile> current,
        ReadOnlySpan<ProductionRecipeProfile> candidate)
    {
        var shared = Math.Min(current.Length, candidate.Length);
        var changed = Math.Abs(current.Length - candidate.Length);
        for (var index = 0; index < shared; index++)
        {
            var left = current[index];
            var right = candidate[index];
            if (!ProductionCatalogSnapshot.RecipeEquals(left, right))
                changed++;
        }
        return changed;
    }
}

public static class ProductionRequirementCatalogValidator
{
    public static bool TryValidate(
        ProductionCatalogSnapshot production,
        BuildingTypeCatalogSnapshot buildings,
        out ProductionCatalogValidationResult validation)
    {
        for (var recipeIndex = 0;
             recipeIndex < production.Recipes.Length;
             recipeIndex++)
        {
            var recipe = production.Recipes[recipeIndex];
            if ((uint)recipe.ProducerBuildingTypeId >=
                    (uint)buildings.Types.Length ||
                buildings.Type(recipe.ProducerBuildingTypeId).Function is not
                    (BuildingFunctionKind.Production or
                        BuildingFunctionKind.TownHall))
            {
                validation = new ProductionCatalogValidationResult(
                    ProductionCatalogErrorCode.InvalidRecipe,
                    recipeIndex,
                    $"Producer building type {recipe.ProducerBuildingTypeId} " +
                    "cannot train units.");
                return false;
            }
            foreach (var requirement in recipe.Requirements)
            {
                if ((uint)requirement.TypeId >= (uint)buildings.Types.Length)
                {
                    validation = new ProductionCatalogValidationResult(
                        ProductionCatalogErrorCode.InvalidRecipe,
                        recipeIndex,
                        $"Requirement building type {requirement.TypeId} " +
                        "is outside the building catalog.");
                    return false;
                }
            }
        }
        validation = new ProductionCatalogValidationResult(
            ProductionCatalogErrorCode.None, -1, string.Empty);
        return true;
    }
}

public static class DemoProductionCatalog
{
    private static readonly ProductionCatalogSnapshot Snapshot = Build();
    public static ProductionCatalogSnapshot CreateSnapshot() => Snapshot;

    private static ProductionCatalogSnapshot Build()
    {
        var marine = new UnitTypeProfile(
            0, "Marine",
            new UnitMovementProfileSnapshot(0, "Marine", 7.5f, 128f, 720f,
                MovementClass.Medium, 8f),
            new CombatProfileSnapshot(100f, 12f, 90f, 220f, 0.75f, 0.1f, 500f,
                CombatPositioningKind.Ranged,
                Attributes: CombatAttribute.Light | CombatAttribute.Biological,
                BaseUpgradeDamage: 1f,
                ProjectileSpeed: 520f), false);
        var marauder = new UnitTypeProfile(
            1, "Marauder",
            new UnitMovementProfileSnapshot(1, "Marauder", 10f, 105f, 600f,
                MovementClass.Large, 12f),
            new CombatProfileSnapshot(180f, 22f, 80f, 210f, 1.1f, 0.15f, 500f,
                CombatPositioningKind.Ranged,
                Armor: 1f,
                Attributes: CombatAttribute.Armored | CombatAttribute.Biological,
                BonusVs: CombatAttribute.Armored,
                BonusDamage: 10f,
                BaseUpgradeDamage: 1f,
                BonusUpgradeDamage: 1f,
                ProjectileSpeed: 320f), false);

        var worker = new UnitTypeProfile(
            2, "SCV",
            new UnitMovementProfileSnapshot(2, "SCV", 7.5f, 128f, 720f,
                MovementClass.Medium, 8f),
            CombatProfileSnapshot.Standard with
            {
                Attributes = CombatAttribute.Light | CombatAttribute.Biological |
                             CombatAttribute.Mechanical,
                BaseUpgradeDamage = 1f
            }, true);
        if (!ProductionCatalogSnapshot.TryCreate(
                ProductionCatalogSnapshot.CurrentFormatVersion,
                [marine, marauder, worker],
                [
                    new(0, "Train Marine", 1, marine,
                        new EconomyCost(50, 0, 1), 3f, 1f),
                    new(1, "Train Marauder", 1, marauder,
                        new EconomyCost(100, 25, 2), 5f, 1f),
                    new(2, "Train SCV", 2, worker,
                        new EconomyCost(50, 0, 1), 3.5f, 1f)
                ],
                out var snapshot,
                out var validation) || snapshot is null)
            throw new InvalidOperationException(
                $"Built-in production catalog is invalid: {validation.Code}.");
        return snapshot;
    }
}
