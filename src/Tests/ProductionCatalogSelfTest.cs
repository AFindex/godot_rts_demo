using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ProductionCatalogSelfTest
{
    public static SelfTestResult Run(ProductionCatalogSnapshot? loaded = null)
    {
        var catalog = DemoProductionCatalog.CreateSnapshot();
        loaded ??= catalog;
        var repeated = ProductionCatalogSnapshot.TryCreate(
            loaded.FormatVersion,
            loaded.UnitTypes,
            loaded.Recipes,
            out var roundTrip,
            out var roundTripValidation);
        var invalidRecipes = catalog.Recipes.ToArray();
        invalidRecipes[0] = invalidRecipes[0] with
        {
            Cost = invalidRecipes[0].Cost with { Supply = 0 }
        };
        var invalidAccepted = ProductionCatalogSnapshot.TryCreate(
            catalog.FormatVersion,
            catalog.UnitTypes,
            invalidRecipes,
            out _,
            out var invalidValidation);
        var duplicateRequirementRecipes = catalog.Recipes.ToArray();
        duplicateRequirementRecipes[0] = duplicateRequirementRecipes[0] with
        {
            Requirements =
            [
                new ProductionRequirementProfile(
                    ProductionRequirementKind.CompletedBuilding, 1, 1),
                new ProductionRequirementProfile(
                    ProductionRequirementKind.CompletedBuilding, 1, 2)
            ]
        };
        var duplicateRequirementAccepted = ProductionCatalogSnapshot.TryCreate(
            catalog.FormatVersion,
            catalog.UnitTypes,
            duplicateRequirementRecipes,
            out _,
            out var duplicateRequirementValidation);
        var crossCatalogRecipes = catalog.Recipes.ToArray();
        crossCatalogRecipes[0] = crossCatalogRecipes[0] with
        {
            Requirements =
            [
                new ProductionRequirementProfile(
                    ProductionRequirementKind.CompletedBuilding, 999, 1)
            ]
        };
        var crossCatalogCreated = ProductionCatalogSnapshot.TryCreate(
            catalog.FormatVersion,
            catalog.UnitTypes,
            crossCatalogRecipes,
            out var crossCatalog,
            out _);
        var crossCatalogRejected = crossCatalogCreated && crossCatalog is not null &&
            !ProductionRequirementCatalogValidator.TryValidate(
                crossCatalog,
                DemoBuildingTypes.CreateCatalog(),
                out var crossValidation) &&
            crossValidation.Code == ProductionCatalogErrorCode.InvalidRecipe;
        var changedRecipes = loaded.Recipes.ToArray();
        changedRecipes[0] = changedRecipes[0] with
        {
            ProductionSeconds = changedRecipes[0].ProductionSeconds + 1f
        };
        var changedCreated = ProductionCatalogSnapshot.TryCreate(
            loaded.FormatVersion, loaded.UnitTypes, changedRecipes,
            out var changed, out _);
        var diff = changedCreated && changed is not null
            ? ProductionCatalogDiff.Compare(loaded, changed)
            : default;
        var passed = repeated && roundTrip is not null &&
                     roundTripValidation.IsValid &&
                     roundTrip.StableHash == loaded.StableHash &&
                     roundTrip.CanonicalBytes.Span.SequenceEqual(
                         loaded.CanonicalBytes.Span) &&
                     loaded.UnitTypes.SequenceEqual(catalog.UnitTypes) &&
                     loaded.Recipes.Length == catalog.Recipes.Length &&
                     loaded.Recipes.ToArray().Zip(catalog.Recipes.ToArray())
                         .All(pair => ProductionCatalogSnapshot.RecipeEquals(
                             pair.First, pair.Second)) &&
                     diff is { Changed: true, ChangedUnitTypes: 0,
                         ChangedRecipes: 1 } &&
                     !invalidAccepted &&
                     invalidValidation.Code ==
                         ProductionCatalogErrorCode.InvalidRecipe &&
                     !duplicateRequirementAccepted &&
                     duplicateRequirementValidation.Code ==
                         ProductionCatalogErrorCode.InvalidRecipe &&
                     crossCatalogRejected;
        return new SelfTestResult(
            passed,
            $"format={loaded.FormatVersion}, hash={loaded.StableHashText}, " +
            $"units={loaded.UnitTypes.Length}, recipes={loaded.Recipes.Length}, " +
            $"changed={diff.ChangedUnitTypes}/{diff.ChangedRecipes}, " +
            $"invalid={invalidValidation.Code}, requirements=" +
            $"{duplicateRequirementValidation.Code}/{crossCatalogRejected}");
    }
}
