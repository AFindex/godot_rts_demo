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
                     loaded.Recipes.SequenceEqual(catalog.Recipes) &&
                     diff is { Changed: true, ChangedUnitTypes: 0,
                         ChangedRecipes: 1 } &&
                     !invalidAccepted &&
                     invalidValidation.Code ==
                         ProductionCatalogErrorCode.InvalidRecipe;
        return new SelfTestResult(
            passed,
            $"format={loaded.FormatVersion}, hash={loaded.StableHashText}, " +
            $"units={loaded.UnitTypes.Length}, recipes={loaded.Recipes.Length}, " +
            $"changed={diff.ChangedUnitTypes}/{diff.ChangedRecipes}, " +
            $"invalid={invalidValidation.Code}");
    }
}
