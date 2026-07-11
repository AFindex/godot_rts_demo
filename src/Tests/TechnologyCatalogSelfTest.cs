using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class TechnologyCatalogSelfTest
{
    public static SelfTestResult Run()
    {
        var catalog = DemoTechnologies.CreateCatalog();
        var roundTrip = TechnologyCatalogSnapshot.TryCreate(
            catalog.FormatVersion,
            catalog.Technologies,
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
        var passed = roundTrip && repeated is not null &&
                     repeated.StableHash == catalog.StableHash &&
                     repeated.CanonicalBytes.Span.SequenceEqual(
                         catalog.CanonicalBytes.Span) &&
                     catalog.Technologies.Length == 3 &&
                     catalog.Technology(0).MaximumLevel == 3 &&
                     catalog.Technology(1).ExclusiveGroupId ==
                         catalog.Technology(2).ExclusiveGroupId &&
                     !invalidAccepted && dependenciesValid;
        return new SelfTestResult(
            passed,
            $"format={catalog.FormatVersion}, hash={catalog.StableHashText}, " +
            $"technologies={catalog.Technologies.Length}, invalid={!invalidAccepted}");
    }
}
