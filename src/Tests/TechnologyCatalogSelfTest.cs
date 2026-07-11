using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class TechnologyCatalogSelfTest
{
    public static SelfTestResult Run(TechnologyCatalogSnapshot? loaded = null)
    {
        var catalog = DemoTechnologies.CreateCatalog();
        loaded ??= catalog;
        var roundTrip = TechnologyCatalogSnapshot.TryCreate(
            loaded.FormatVersion,
            loaded.Technologies,
            out var repeated,
            out _);
        var invalid = catalog.Technologies.ToArray();
        invalid[0] = invalid[0] with
        {
            Requirements =
            [
                new(TechnologyRequirementKind.CompletedBuilding, 4, 1),
                new(TechnologyRequirementKind.CompletedBuilding, 4, 2)
            ]
        };
        var invalidAccepted = TechnologyCatalogSnapshot.TryCreate(
            catalog.FormatVersion, invalid, out _, out _);
        var dependenciesValid = TechnologyCatalogDependencyValidator.TryValidate(
            catalog, DemoBuildingTypes.CreateCatalog(), out _);
        var cyclicValues = catalog.Technologies.ToArray();
        cyclicValues[1] = cyclicValues[1] with
        {
            Requirements =
            [new(TechnologyRequirementKind.TechnologyLevel, 2, 1)]
        };
        var cyclicCreated = TechnologyCatalogSnapshot.TryCreate(
            catalog.FormatVersion, cyclicValues, out var cyclic, out _);
        var cyclicRejected = cyclicCreated && cyclic is not null &&
            !TechnologyCatalogDependencyValidator.TryValidate(
                cyclic, DemoBuildingTypes.CreateCatalog(), out _);
        var passed = roundTrip && repeated is not null &&
                     repeated.StableHash == loaded.StableHash &&
                     repeated.CanonicalBytes.Span.SequenceEqual(
                         loaded.CanonicalBytes.Span) &&
                     loaded.StableHash == catalog.StableHash &&
                     catalog.Technologies.Length == 3 &&
                     catalog.Technology(0).MaximumLevel == 3 &&
                     catalog.Technology(1).ExclusiveGroupId ==
                         catalog.Technology(2).ExclusiveGroupId &&
                     !invalidAccepted && dependenciesValid && cyclicRejected;
        return new SelfTestResult(
            passed,
            $"format={loaded.FormatVersion}, hash={loaded.StableHashText}, " +
            $"technologies={catalog.Technologies.Length}, " +
            $"invalid={!invalidAccepted}, cyclic={cyclicRejected}");
    }
}
