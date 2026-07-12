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
            resource.UnitTypes.Add(new UnitTypeProfileResource
            {
                Id = type.Id,
                DisplayName = type.Name,
                PhysicalRadius = type.Movement.PhysicalRadius,
                MaximumSpeed = type.Movement.MaximumSpeed,
                Acceleration = type.Movement.Acceleration,
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
                IsWorker = type.IsWorker
            });
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
                clearance.Class, clearance.NavigationRadius);
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
                source.AutoTargetPriority);

            units[index] = new UnitTypeProfile(
                source.Id, source.DisplayName ?? string.Empty,
                movement, combat, source.IsWorker);
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
