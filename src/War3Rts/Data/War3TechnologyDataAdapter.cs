using RtsDemo.Simulation;
using System.Collections.Immutable;

namespace War3Rts.Data;

public sealed record War3TechnologyDefinition(
    int TechnologyId,
    string ObjectId,
    string Name,
    string Description,
    string IconPath,
    IReadOnlyList<War3ObjectLevel> Levels,
    IReadOnlyList<War3UpgradeEffect> Effects)
{
    public string NameForLevel(int zeroBasedLevel)
    {
        if (Levels.Count == 0) return Name;
        var level = Levels[Math.Clamp(zeroBasedLevel, 0, Levels.Count - 1)];
        return string.IsNullOrWhiteSpace(level.Name) ? Name : level.Name;
    }

    public string IconPathForLevel(int zeroBasedLevel)
    {
        if (Levels.Count == 0) return IconPath;
        var level = Levels[Math.Clamp(zeroBasedLevel, 0, Levels.Count - 1)];
        var value = level.Icon?.ResolvedPath;
        if (string.IsNullOrWhiteSpace(value)) value = level.Icon?.RequestedPath;
        return string.IsNullOrWhiteSpace(value)
            ? IconPath
            : value.Replace('/', '\\');
    }
}

public sealed record War3ObjectDataImportReport(
    bool AbilityManifestLoaded,
    int IndexedAbilityCount,
    int ReferencedAbilityCount,
    IReadOnlyList<string> MissingAbilityIds,
    bool UpgradeManifestLoaded,
    int IndexedUpgradeCount,
    int ReferencedUpgradeCount,
    IReadOnlyList<string> MissingUpgradeIds,
    int AppliedTechnologyCount,
    IReadOnlyList<string> FallbackTechnologyIds)
{
    public string LogLine =>
        $"abilities={(AbilityManifestLoaded ? "loaded" : "fallback")}/" +
        $"{IndexedAbilityCount} refs={ReferencedAbilityCount} " +
        $"ability_missing={MissingAbilityIds.Count} " +
        $"upgrades={(UpgradeManifestLoaded ? "loaded" : "fallback")}/" +
        $"{IndexedUpgradeCount} refs={ReferencedUpgradeCount} " +
        $"upgrade_missing={MissingUpgradeIds.Count} " +
        $"technology_applied={AppliedTechnologyCount} " +
        $"technology_fallbacks={FallbackTechnologyIds.Count}";
}

/// <summary>
/// Maps only values supported by the generic TechnologySystem. Full per-level
/// Warcraft costs and effects remain available through the catalog and
/// presentation definition instead of being silently flattened or guessed.
/// </summary>
public sealed class War3TechnologyDataAdapter(
    War3ObjectDataCatalog upgradeCatalog)
{
    public War3ObjectDataCatalog Catalog { get; } = upgradeCatalog;
    public IReadOnlyDictionary<string, int> BuildingTypeIds { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> TechnologyIds { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public (TechnologyProfile Profile, War3TechnologyDefinition Definition,
        bool Applied) Apply(
        string objectId,
        TechnologyProfile fallback,
        string fallbackIcon)
    {
        if (!Catalog.TryGet(objectId, out var data) ||
            data.Summary.Levels.Length == 0)
            return (
                fallback,
                new War3TechnologyDefinition(
                    fallback.Id, objectId, fallback.Name, string.Empty,
                    fallbackIcon, [], []),
                false);

        var first = data.Summary.Levels[0];
        var name = Text(first.Name, Text(data.DisplayName, fallback.Name));
        var icon = AssetPath(first.Icon, fallbackIcon);
        var requirements = first.Requirements
            .Distinct(StringComparer.Ordinal)
            .Select(value => Requirement(value, first.RequirementLevel ?? 1))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToImmutableArray();
        var profile = fallback with
        {
            Name = name,
            Cost = new EconomyCost(
                NonNegative(first.Gold) ? first.Gold!.Value : fallback.Cost.Minerals,
                NonNegative(first.Lumber)
                    ? first.Lumber!.Value
                    : fallback.Cost.VespeneGas),
            ResearchSeconds = Positive(first.ResearchSeconds)
                ? first.ResearchSeconds!.Value
                : fallback.ResearchSeconds,
            MaximumLevel = data.Identity.Levels > 0
                ? data.Identity.Levels
                : fallback.MaximumLevel,
            Requirements = requirements.IsDefaultOrEmpty
                ? fallback.Requirements
                : requirements
        };
        return (
            profile,
            new War3TechnologyDefinition(
                fallback.Id,
                objectId,
                name,
                Text(first.ExtendedTooltip, first.Tooltip ?? string.Empty),
                icon,
                data.Summary.Levels,
                data.Summary.Effects),
            true);
    }

    private TechnologyRequirementProfile? Requirement(
        string objectId,
        int level)
    {
        if (BuildingTypeIds.TryGetValue(objectId, out var buildingTypeId))
            return new TechnologyRequirementProfile(
                TechnologyRequirementKind.CompletedBuilding,
                buildingTypeId, 1);
        if (TechnologyIds.TryGetValue(objectId, out var technologyId))
            return new TechnologyRequirementProfile(
                TechnologyRequirementKind.TechnologyLevel,
                technologyId, Math.Max(1, level));
        return null;
    }

    private static string AssetPath(
        War3UnitAssetReference? asset,
        string fallback)
    {
        var value = asset?.ResolvedPath;
        if (string.IsNullOrWhiteSpace(value)) value = asset?.RequestedPath;
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Replace('/', '\\');
    }

    private static string Text(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static bool Positive(float? value) =>
        value is > 0f && float.IsFinite(value.Value);

    private static bool NonNegative(int? value) => value is >= 0;
}
