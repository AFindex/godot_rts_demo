using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ProductionCatalogSelfTest
{
    public static SelfTestResult Run()
    {
        var catalog = DemoProductionCatalog.CreateSnapshot();
        var repeated = ProductionCatalogSnapshot.TryCreate(
            catalog.FormatVersion,
            catalog.UnitTypes,
            catalog.Recipes,
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
        var passed = repeated && roundTrip is not null &&
                     roundTripValidation.IsValid &&
                     roundTrip.StableHash == catalog.StableHash &&
                     roundTrip.CanonicalBytes.Span.SequenceEqual(
                         catalog.CanonicalBytes.Span) &&
                     catalog.UnitTypes.Length == 3 &&
                     catalog.Recipes.Length == 3 &&
                     !invalidAccepted &&
                     invalidValidation.Code ==
                         ProductionCatalogErrorCode.InvalidRecipe;
        return new SelfTestResult(
            passed,
            $"format={catalog.FormatVersion}, hash={catalog.StableHashText}, " +
            $"units={catalog.UnitTypes.Length}, recipes={catalog.Recipes.Length}, " +
            $"invalid={invalidValidation.Code}");
    }
}
