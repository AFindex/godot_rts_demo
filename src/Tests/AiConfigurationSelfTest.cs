using RtsDemo.AI;

namespace RtsDemo.Tests;

public static class AiConfigurationSelfTest
{
    public static SelfTestResult Run(AiConfigurationCatalogSnapshot? loaded)
    {
        var canonical = DemoAiConfigurations.CreateCatalog();
        loaded ??= canonical;
        var resourceMatches = loaded.FormatVersion == canonical.FormatVersion &&
                              loaded.StableHash == canonical.StableHash &&
                              loaded.Profiles.SequenceEqual(canonical.Profiles);
        var changedValues = canonical.Profiles.ToArray();
        changedValues[1] = changedValues[1] with { AttackArmySize = 5 };
        var changed = AiConfigurationCatalogSnapshot.TryCreate(
            AiConfigurationCatalogSnapshot.CurrentFormatVersion,
            changedValues, out var changedCatalog, out _) &&
            changedCatalog is not null &&
            changedCatalog.StableHash != canonical.StableHash;
        var invalidValues = canonical.Profiles.ToArray();
        invalidValues[1] = invalidValues[1] with { Id = 0 };
        var rejected = !AiConfigurationCatalogSnapshot.TryCreate(
            AiConfigurationCatalogSnapshot.CurrentFormatVersion,
            invalidValues, out _, out _);
        return new SelfTestResult(
            resourceMatches && changed && rejected,
            $"format={canonical.FormatVersion}, hash={canonical.StableHashText}, " +
            $"profiles={canonical.Profiles.Length}, resource={resourceMatches}, " +
            $"changed={changed}, rejected={rejected}");
    }
}
