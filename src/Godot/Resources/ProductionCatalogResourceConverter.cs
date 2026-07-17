using Godot;
using RtsDemo.Simulation;
using System.Collections.Immutable;

namespace RtsDemo.GodotRuntime.Resources;

public static class ProductionCatalogResourceConverter
{
    public static RtsProductionCatalogResource FromSnapshot(
        ProductionCatalogSnapshot snapshot)
    {
        var resource = new RtsProductionCatalogResource
        {
            FormatVersion = snapshot.FormatVersion
        };
        foreach (var type in snapshot.UnitTypes)
        {
            var unitResource = new UnitTypeProfileResource
            {
                Id = type.Id,
                DisplayName = type.Name,
                PhysicalRadius = type.Movement.PhysicalRadius,
                MaximumSpeed = type.Movement.MaximumSpeed,
                Acceleration = type.Movement.Acceleration,
                TurnRateRadiansPerSecond =
                    type.Movement.TurnRateRadiansPerSecond,
                MaximumHealth = type.Combat.MaximumHealth,
                AttackDamage = type.Combat.AttackDamage,
                AttackRange = type.Combat.AttackRange,
                AcquisitionRange = type.Combat.AcquisitionRange,
                AttackCooldownSeconds = type.Combat.AttackCooldownSeconds,
                AttackWindupSeconds = type.Combat.AttackWindupSeconds,
                LeashDistance = type.Combat.LeashDistance,
                Positioning = type.Combat.Positioning,
                Armor = type.Combat.Armor,
                Attributes = type.Combat.Attributes,
                AttacksPerVolley = type.Combat.AttacksPerVolley,
                BonusVs = type.Combat.BonusVs,
                BonusDamage = type.Combat.BonusDamage,
                BaseUpgradeDamage = type.Combat.BaseUpgradeDamage,
                BonusUpgradeDamage = type.Combat.BonusUpgradeDamage,
                ProjectileSpeed = type.Combat.ProjectileSpeed,
                CanMoveDuringWindup = type.Combat.CanMoveDuringWindup,
                CanMoveDuringCooldown = type.Combat.CanMoveDuringCooldown,
                AutoTargetPriority = type.Combat.AutoTargetPriority,
                ArmorType = type.Combat.ArmorType,
                ArmorUpgradeTechnologyId =
                    type.Combat.ArmorUpgradeTechnologyId,
                ArmorUpgradePerLevel = type.Combat.ArmorUpgradePerLevel,
                AttackHalfAngleRadians =
                    type.Combat.AttackHalfAngleRadians,
                Concealment = type.Perception.Concealment,
                DetectionRange = type.Perception.DetectionRange,
                VisionRange = type.Perception.VisionRange,
                ObservationHeight = type.Perception.ObservationHeight,
                TerrainVisionMode = type.Perception.TerrainVisionMode,
                IsWorker = type.IsWorker
            };
            foreach (var weapon in type.Combat.Weapons)
            {
                unitResource.Weapons.Add(new CombatWeaponProfileResource
                {
                    Slot = weapon.Slot,
                    TargetLayers = weapon.TargetLayers,
                    EnabledByDefault = weapon.EnabledByDefault,
                    RequiredTechnologyId = weapon.RequiredTechnologyId,
                    AttackDamage = weapon.AttackDamage,
                    AttackRange = weapon.AttackRange,
                    AttackCooldownSeconds = weapon.AttackCooldownSeconds,
                    AttackWindupSeconds = weapon.AttackWindupSeconds,
                    Positioning = weapon.Positioning,
                    AttacksPerVolley = weapon.AttacksPerVolley,
                    BonusVs = weapon.BonusVs,
                    BonusDamage = weapon.BonusDamage,
                    BaseUpgradeDamage = weapon.BaseUpgradeDamage,
                    BonusUpgradeDamage = weapon.BonusUpgradeDamage,
                    ProjectileSpeed = weapon.ProjectileSpeed,
                    CanMoveDuringWindup = weapon.CanMoveDuringWindup,
                    CanMoveDuringCooldown = weapon.CanMoveDuringCooldown,
                    AttackType = weapon.AttackType,
                    DamageUpgradeTechnologyId =
                        weapon.DamageUpgradeTechnologyId,
                    MinimumRange = weapon.MinimumRange,
                    FullDamageRadius = weapon.Area.FullDamageRadius,
                    HalfDamageRadius = weapon.Area.HalfDamageRadius,
                    QuarterDamageRadius = weapon.Area.QuarterDamageRadius,
                    AreaTargetLayers = weapon.Area.TargetLayers,
                    PropagationKind = weapon.Propagation.Kind,
                    PropagationLineDistance =
                        weapon.Propagation.LineDistance,
                    PropagationRadius = weapon.Propagation.Radius,
                    PropagationDamageLossFactor =
                        weapon.Propagation.DamageLossFactor,
                    PropagationMaximumTargets =
                        weapon.Propagation.MaximumTargets,
                    PropagationTargetLayers =
                        weapon.Propagation.TargetLayers,
                    PropagationDistanceUpgradeTechnologyId =
                        weapon.Propagation.DistanceUpgradeTechnologyId,
                    PropagationDistanceUpgradePerLevel =
                        weapon.Propagation.DistanceUpgradePerLevel
                });
            }
            resource.UnitTypes.Add(unitResource);
        }
        foreach (var recipe in snapshot.Recipes)
        {
            var recipeResource = new ProductionRecipeProfileResource
            {
                Id = recipe.Id,
                DisplayName = recipe.Name,
                ProducerBuildingTypeId = recipe.ProducerBuildingTypeId,
                UnitTypeId = recipe.UnitType.Id,
                MineralCost = recipe.Cost.Minerals,
                VespeneCost = recipe.Cost.VespeneGas,
                SupplyCost = recipe.Cost.Supply,
                ProductionSeconds = recipe.ProductionSeconds,
                CancelRefundFraction = recipe.CancelRefundFraction
            };
            foreach (var requirement in recipe.Requirements)
            {
                recipeResource.Requirements.Add(
                    new ProductionRequirementProfileResource
                    {
                        RequiredBuildingTypeId = requirement.TypeId,
                        RequiredCompletedCount = requirement.Count
                    });
            }
            resource.Recipes.Add(recipeResource);
        }
        return resource;
    }

    public static bool TryLoadSnapshot(
        string path,
        out ProductionCatalogSnapshot? snapshot,
        out ProductionCatalogValidationResult validation)
    {
        var resource = ResourceLoader.Load<RtsProductionCatalogResource>(
            path, string.Empty, ResourceLoader.CacheMode.Replace);
        if (resource is null)
        {
            snapshot = null;
            validation = new ProductionCatalogValidationResult(
                ProductionCatalogErrorCode.MissingResourceAsset, -1,
                $"Production catalog resource could not be loaded: {path}");
            return false;
        }
        return TryConvert(resource, out snapshot, out validation);
    }

    public static bool TryConvert(
        RtsProductionCatalogResource resource,
        out ProductionCatalogSnapshot? snapshot,
        out ProductionCatalogValidationResult validation)
    {
        var units = new UnitTypeProfile[resource.UnitTypes.Count];
        for (var index = 0; index < units.Length; index++)
        {
            var source = resource.UnitTypes[index];
            if (source is null)
                return NullElement("unit type", index, out snapshot, out validation);
            var clearance = float.IsFinite(source.PhysicalRadius) &&
                            source.PhysicalRadius > 0f
                ? MovementClearance.FromPhysicalRadius(source.PhysicalRadius)
                : default;
            var movement = new UnitMovementProfileSnapshot(
                source.Id, source.DisplayName ?? string.Empty,
                source.PhysicalRadius, source.MaximumSpeed, source.Acceleration,
                clearance.Class, clearance.NavigationRadius,
                source.TurnRateRadiansPerSecond);
            var weaponProfiles = ImmutableArray
                .CreateBuilder<CombatWeaponProfileSnapshot>(source.Weapons.Count);
            for (var weaponIndex = 0;
                 weaponIndex < source.Weapons.Count;
                 weaponIndex++)
            {
                var weapon = source.Weapons[weaponIndex];
                if (weapon is null)
                    return NullElement(
                        "combat weapon", index, out snapshot, out validation);
                weaponProfiles.Add(new CombatWeaponProfileSnapshot(
                    weapon.Slot,
                    weapon.TargetLayers,
                    weapon.EnabledByDefault,
                    weapon.RequiredTechnologyId,
                    weapon.AttackDamage,
                    weapon.AttackRange,
                    weapon.AttackCooldownSeconds,
                    weapon.AttackWindupSeconds,
                    weapon.Positioning,
                    weapon.AttacksPerVolley,
                    weapon.BonusVs,
                    weapon.BonusDamage,
                    weapon.BaseUpgradeDamage,
                    weapon.BonusUpgradeDamage,
                    weapon.ProjectileSpeed,
                    weapon.CanMoveDuringWindup,
                    weapon.CanMoveDuringCooldown,
                    weapon.AttackType,
                    weapon.DamageUpgradeTechnologyId,
                    weapon.MinimumRange,
                    new CombatWeaponAreaSnapshot(
                        weapon.FullDamageRadius,
                        weapon.HalfDamageRadius,
                        weapon.QuarterDamageRadius,
                        weapon.AreaTargetLayers),
                    new CombatWeaponPropagationSnapshot(
                        weapon.PropagationKind,
                        weapon.PropagationLineDistance,
                        weapon.PropagationRadius,
                        weapon.PropagationDamageLossFactor,
                        weapon.PropagationMaximumTargets,
                        weapon.PropagationTargetLayers,
                        weapon.PropagationDistanceUpgradeTechnologyId,
                        weapon.PropagationDistanceUpgradePerLevel)));
            }
            var combat = new CombatProfileSnapshot(
                source.MaximumHealth, source.AttackDamage, source.AttackRange,
                source.AcquisitionRange, source.AttackCooldownSeconds,
                source.AttackWindupSeconds, source.LeashDistance,
                source.Positioning, source.Armor, source.Attributes,
                source.AttacksPerVolley, source.BonusVs, source.BonusDamage,
                source.BaseUpgradeDamage, source.BonusUpgradeDamage,
                source.ProjectileSpeed,
                source.CanMoveDuringWindup,
                source.CanMoveDuringCooldown,
                source.AutoTargetPriority,
                source.ArmorType,
                source.ArmorUpgradeTechnologyId,
                source.ArmorUpgradePerLevel,
                source.AttackHalfAngleRadians)
            {
                Weapons = weaponProfiles.MoveToImmutable()
            };

            units[index] = new UnitTypeProfile(
                source.Id, source.DisplayName ?? string.Empty,
                movement, combat, source.IsWorker)
            {
                Perception = new UnitPerceptionProfileSnapshot(
                    source.Concealment,
                    source.DetectionRange,
                    source.VisionRange,
                    source.ObservationHeight,
                    source.TerrainVisionMode)
            };
        }

        var recipes = new ProductionRecipeProfile[resource.Recipes.Count];
        for (var index = 0; index < recipes.Length; index++)
        {
            var source = resource.Recipes[index];
            if (source is null)
                return NullElement("recipe", index, out snapshot, out validation);
            if ((uint)source.UnitTypeId >= (uint)units.Length)
            {
                snapshot = null;
                validation = new ProductionCatalogValidationResult(
                    ProductionCatalogErrorCode.RecipeUnitMismatch, index,
                    $"Recipe unit type ID {source.UnitTypeId} is outside the catalog.");
                return false;
            }
            var requirements = new ProductionRequirementProfile[
                source.Requirements.Count];
            for (var requirementIndex = 0;
                 requirementIndex < requirements.Length;
                 requirementIndex++)
            {
                var requirement = source.Requirements[requirementIndex];
                if (requirement is null)
                    return NullElement(
                        "recipe requirement", index, out snapshot, out validation);
                requirements[requirementIndex] = new ProductionRequirementProfile(
                    ProductionRequirementKind.CompletedBuilding,
                    requirement.RequiredBuildingTypeId,
                    requirement.RequiredCompletedCount);
            }
            recipes[index] = new ProductionRecipeProfile(
                source.Id, source.DisplayName ?? string.Empty,
                source.ProducerBuildingTypeId, units[source.UnitTypeId],
                new EconomyCost(
                    source.MineralCost, source.VespeneCost, source.SupplyCost),
                source.ProductionSeconds, source.CancelRefundFraction)
            {
                Requirements = requirements.ToImmutableArray()
            };
        }
        return ProductionCatalogSnapshot.TryCreate(
            resource.FormatVersion, units, recipes, out snapshot, out validation);
    }

    private static bool NullElement(
        string kind,
        int index,
        out ProductionCatalogSnapshot? snapshot,
        out ProductionCatalogValidationResult validation)
    {
        snapshot = null;
        validation = new ProductionCatalogValidationResult(
            ProductionCatalogErrorCode.NullResourceElement, index,
            $"Production {kind} at index {index} is null.");
        return false;
    }
}
