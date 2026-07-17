using System.Text;
using System.Text.Json;

namespace War3Rts.Data;

public sealed record War3AbilityCoverageReference(
    string UnitId,
    string DisplayName,
    string Race,
    string Category,
    string Kind);

public sealed record War3AbilityCoverageEntry(
    string Id,
    string DisplayName,
    string BaseCode,
    string Race,
    bool Hero,
    bool Item,
    bool? EditorVisible,
    int LevelCount,
    bool UnitReferenced,
    bool CurrentRuntimeCompiled,
    bool FamilyRegistered,
    bool HasPrototypeCompiler,
    string Compiler,
    string Activation,
    bool AutoCastDefault,
    string RuntimeStatus,
    string Reason,
    string[] TargetTokens,
    string[] UnsupportedTargetTokens,
    string[] RequirementIds,
    War3AbilityCoverageReference[] ReferencedBy);

public sealed record War3AbilityTargetTokenCoverage(
    string[] ExportedTokens,
    string[] UnrecognizedTokens,
    string[] RuntimeUnsupportedTokens,
    int AbilitiesWithUnsupportedTokens);

public sealed record War3AbilityTechnologyCoverage(
    int AbilitiesWithRequirements,
    string[] AllRequirementIds,
    int CurrentRuntimeAbilitiesWithRequirements,
    string[] CurrentRuntimeRequirementIds,
    string[] CurrentRuntimeResolvedRequirementIds,
    string[] CurrentRuntimeUnresolvedRequirementIds);

public sealed record War3AbilityCoverageSlice(
    int AbilityCount,
    int BaseFamilyCount,
    int ClassifiedAbilityCount,
    int ClassifiedBaseFamilyCount,
    int PrototypeAbilityCount,
    int PrototypeBaseFamilyCount,
    IReadOnlyDictionary<string, int> StatusCounts);

public sealed record War3AbilityRuntimeCoverageReport(
    string Schema,
    DateTimeOffset? AbilitySourceGeneratedAt,
    DateTimeOffset? UnitSourceGeneratedAt,
    int RegistryBaseFamilyCount,
    War3AbilityCoverageSlice All,
    War3AbilityCoverageSlice UnitReferenced,
    War3AbilityCoverageSlice Items,
    War3AbilityCoverageSlice CurrentRuntime,
    War3AbilityTargetTokenCoverage Targeting,
    War3AbilityTechnologyCoverage TechnologyRequirements,
    string[] UnclassifiedReferencedBaseCodes,
    string[] BlockedReferencedBaseCodes,
    string[] OrphanRegisteredBaseCodes,
    War3AbilityCoverageEntry[] Abilities)
{
    public const string SupportedSchema =
        "war3-ability-runtime-coverage/v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Summary =>
        $"all={All.AbilityCount}/{All.BaseFamilyCount} " +
        $"classified={All.ClassifiedAbilityCount}/" +
        $"{All.ClassifiedBaseFamilyCount} " +
        $"unit={UnitReferenced.AbilityCount}/" +
        $"{UnitReferenced.BaseFamilyCount} " +
        $"unit_unclassified={UnclassifiedReferencedBaseCodes.Length} " +
        $"target_unsupported=" +
        $"{Targeting.RuntimeUnsupportedTokens.Length}/" +
        $"{Targeting.AbilitiesWithUnsupportedTokens} " +
        $"tech_unresolved=" +
        $"{TechnologyRequirements.CurrentRuntimeUnresolvedRequirementIds.Length} " +
        $"runtime={CurrentRuntime.AbilityCount}/" +
        $"{CurrentRuntime.BaseFamilyCount} " +
        $"registry={RegistryBaseFamilyCount}";

    public void Write(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath) ??
                        throw new InvalidOperationException(
                            "Ability coverage output path has no directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(this, JsonOptions) +
            Environment.NewLine,
            new UTF8Encoding(false));
    }
}

/// <summary>
/// Joins the complete exported object data with the runtime behavior registry.
/// This report deliberately distinguishes a classified family from a gameplay
/// prototype and from a finished implementation.
/// </summary>
public static class War3AbilityRuntimeCoverageAudit
{
    private static readonly HashSet<string> RecognizedTargetTokens = new(
        [
            "self", "friend", "ally", "enemy", "neutral", "structure",
            "dead", "alive", "ground", "air", "organic", "mechanical",
            "hero", "nonhero", "invu", "vuln", "notself", "ward", "tree",
            "debris", "player", "ancient", "nonancient", "nonsapper",
            "bridge", "item", "wall"
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> UnsupportedTargetTokens = new(
        [
            "ward", "tree", "debris", "player", "ancient", "nonancient",
            "nonsapper", "bridge", "item", "wall"
        ],
        StringComparer.Ordinal);

    public static War3AbilityRuntimeCoverageReport Analyze(
        War3ObjectDataCatalog abilities,
        IWar3UnitDataCatalog units,
        IEnumerable<string>? currentRuntimeRawcodes = null,
        IEnumerable<string>? currentRuntimeTechnologyRawcodes = null)
    {
        if (!abilities.IsAvailable ||
            abilities.Kind != War3ObjectDataKind.Ability)
            throw new InvalidDataException(
                "A strict Warcraft ability catalog is required.");
        if (!units.IsAvailable)
            throw new InvalidDataException(
                "A strict Warcraft unit catalog is required.");

        var current = (currentRuntimeRawcodes ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var technologies = (currentRuntimeTechnologyRawcodes ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var references = BuildReferences(units);
        var entries = new List<War3AbilityCoverageEntry>(abilities.Count);
        foreach (var index in abilities.Entries.OrderBy(value => value.Id,
                     StringComparer.Ordinal))
        {
            if (!abilities.TryGet(index.Id, out var ability))
                throw new InvalidDataException(
                    $"Unable to load ability '{index.Id}' while auditing.");
            var baseCode = string.IsNullOrWhiteSpace(ability.Identity.BaseCode)
                ? ability.Id
                : ability.Identity.BaseCode;
            var registered = War3AbilityBehaviorRegistry.TryGet(
                baseCode, out var behavior);
            behavior ??= War3AbilityBehaviorRegistry.Resolve(baseCode);
            var referencedBy = references.TryGetValue(ability.Id, out var refs)
                ? refs.ToArray()
                : [];
            var targetTokens = ability.Summary.Levels
                .SelectMany(value => value.Targets)
                .Select(value => value.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var requirementIds = ability.Summary.Levels
                .SelectMany(value => value.Requirements)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            entries.Add(new War3AbilityCoverageEntry(
                ability.Id,
                ability.DisplayName,
                baseCode,
                ability.Identity.Race,
                ability.Identity.Hero,
                ability.Identity.Item,
                ability.Identity.EditorVisible,
                ability.Summary.Levels.Length,
                referencedBy.Length > 0,
                current.Contains(ability.Id),
                registered,
                behavior.HasPrototypeCompiler,
                ToSnakeCase(behavior.Compiler.ToString()),
                ToSnakeCase(behavior.Activation.ToString()),
                behavior.AutoCastDefault,
                War3AbilityBehaviorRegistry.StatusText(behavior.Status),
                behavior.Reason,
                targetTokens,
                targetTokens.Where(UnsupportedTargetTokens.Contains).ToArray(),
                requirementIds,
                referencedBy));
        }

        var array = entries.ToArray();
        var registeredBaseCodes = War3AbilityBehaviorRegistry.All
            .Select(value => value.BaseCode)
            .ToHashSet(StringComparer.Ordinal);
        var exportedBaseCodes = array.Select(value => value.BaseCode)
            .ToHashSet(StringComparer.Ordinal);
        var unitReferenced = array.Where(value => value.UnitReferenced).ToArray();
        return new War3AbilityRuntimeCoverageReport(
            War3AbilityRuntimeCoverageReport.SupportedSchema,
            abilities.GeneratedAt,
            units.GeneratedAt,
            registeredBaseCodes.Count,
            Slice(array),
            Slice(unitReferenced),
            Slice(array.Where(value => value.Item)),
            Slice(array.Where(value => value.CurrentRuntimeCompiled)),
            TargetCoverage(array),
            TechnologyCoverage(array, technologies),
            unitReferenced
                .Where(value => !value.FamilyRegistered)
                .Select(value => value.BaseCode)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            unitReferenced
                .Where(value => value.RuntimeStatus == "blocked")
                .Select(value => value.BaseCode)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            registeredBaseCodes.Except(exportedBaseCodes, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            array);
    }

    private static War3AbilityTechnologyCoverage TechnologyCoverage(
        IEnumerable<War3AbilityCoverageEntry> entries,
        HashSet<string> runtimeTechnologyRawcodes)
    {
        var values = entries.ToArray();
        var withRequirements = values
            .Where(value => value.RequirementIds.Length > 0)
            .ToArray();
        var current = withRequirements
            .Where(value => value.CurrentRuntimeCompiled)
            .ToArray();
        var allIds = withRequirements.SelectMany(value => value.RequirementIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentIds = current.SelectMany(value => value.RequirementIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new War3AbilityTechnologyCoverage(
            withRequirements.Length,
            allIds,
            current.Length,
            currentIds,
            currentIds.Where(runtimeTechnologyRawcodes.Contains).ToArray(),
            currentIds.Where(value =>
                    !runtimeTechnologyRawcodes.Contains(value))
                .ToArray());
    }

    private static War3AbilityTargetTokenCoverage TargetCoverage(
        IEnumerable<War3AbilityCoverageEntry> entries)
    {
        var values = entries.ToArray();
        var exported = values.SelectMany(value => value.TargetTokens)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new War3AbilityTargetTokenCoverage(
            exported,
            exported.Where(value => !RecognizedTargetTokens.Contains(value))
                .ToArray(),
            exported.Where(UnsupportedTargetTokens.Contains).ToArray(),
            values.Count(value => value.UnsupportedTargetTokens.Length > 0));
    }

    private static Dictionary<string, List<War3AbilityCoverageReference>>
        BuildReferences(IWar3UnitDataCatalog units)
    {
        var result = new Dictionary<
            string, List<War3AbilityCoverageReference>>(StringComparer.Ordinal);
        foreach (var index in units.Entries.OrderBy(value => value.Id,
                     StringComparer.Ordinal))
        {
            if (!units.TryGet(index.Id, out var unit))
                throw new InvalidDataException(
                    $"Unable to load unit '{index.Id}' while auditing.");
            Add(unit.Summary.Abilities, "normal");
            Add(unit.Summary.HeroAbilities, "hero");

            void Add(IEnumerable<string> rawcodes, string kind)
            {
                foreach (var rawcode in rawcodes
                             .Distinct(StringComparer.Ordinal))
                {
                    if (!result.TryGetValue(rawcode, out var values))
                    {
                        values = [];
                        result.Add(rawcode, values);
                    }
                    values.Add(new War3AbilityCoverageReference(
                        index.Id,
                        index.DisplayName,
                        index.Race,
                        index.Category,
                        kind));
                }
            }
        }
        return result;
    }

    private static War3AbilityCoverageSlice Slice(
        IEnumerable<War3AbilityCoverageEntry> source)
    {
        var values = source.ToArray();
        var classified = values.Where(value => value.FamilyRegistered).ToArray();
        var prototypes = values.Where(value => value.HasPrototypeCompiler).ToArray();
        var statuses = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var status in new[]
                 {
                     "implemented", "implemented_gameplay", "delegated",
                     "presentation_only", "not_applicable", "blocked",
                     "unclassified"
                 })
            statuses.Add(status, values.Count(value =>
                value.RuntimeStatus.Equals(status, StringComparison.Ordinal)));
        return new War3AbilityCoverageSlice(
            values.Length,
            values.Select(value => value.BaseCode)
                .Distinct(StringComparer.Ordinal).Count(),
            classified.Length,
            classified.Select(value => value.BaseCode)
                .Distinct(StringComparer.Ordinal).Count(),
            prototypes.Length,
            prototypes.Select(value => value.BaseCode)
                .Distinct(StringComparer.Ordinal).Count(),
            statuses);
    }

    private static string ToSnakeCase(string value) => string.Concat(
        value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
}
