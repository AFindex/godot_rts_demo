using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace War3Rts.Data;

/// <summary>
/// Read-only boundary for the exported Warcraft III object-editor data. The
/// manifest is indexed eagerly while individual unit JSON files are loaded on
/// demand, so gameplay modules do not need to know the export directory layout.
/// </summary>
public interface IWar3UnitDataCatalog
{
    string RootPath { get; }
    string Schema { get; }
    bool IsAvailable { get; }
    string Error { get; }
    int Count { get; }
    int LoadedCount { get; }
    DateTimeOffset? GeneratedAt { get; }
    string Localization { get; }
    IReadOnlyList<War3UnitDataIndexEntry> Entries { get; }
    IReadOnlyDictionary<string, string> LoadErrors { get; }

    bool Contains(string objectId);
    bool TryGet(
        string objectId,
        [NotNullWhen(true)] out War3UnitData? data);
    bool TryGetEditorValue(
        string objectId,
        string table,
        string field,
        [NotNullWhen(true)] out string? value);
}

public sealed record War3UnitDataIndexEntry(
    string Id,
    string DisplayName,
    string Race,
    string Category,
    bool IsHero,
    bool IsBuilding,
    string RelativePath,
    string ModelPath,
    string IconPath,
    int FieldCount);

public sealed class War3UnitDataCatalog : IWar3UnitDataCatalog
{
    public const string SupportedManifestSchema = "war3-unit-editor-manifest/v1";
    public const string SupportedUnitSchema = "war3-unit-editor-data/v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, War3UnitDataIndexEntry> _index;
    private readonly Dictionary<string, War3UnitData> _cache =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _loadErrors =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private War3UnitDataCatalog(
        string rootPath,
        string schema,
        bool isAvailable,
        string error,
        DateTimeOffset? generatedAt,
        string localization,
        IReadOnlyList<War3UnitDataIndexEntry> entries)
    {
        RootPath = rootPath;
        Schema = schema;
        IsAvailable = isAvailable;
        Error = error;
        GeneratedAt = generatedAt;
        Localization = localization;
        Entries = entries;
        _index = entries.ToDictionary(value => value.Id, StringComparer.Ordinal);
    }

    public string RootPath { get; }
    public string Schema { get; }
    public bool IsAvailable { get; }
    public string Error { get; }
    public int Count => Entries.Count;
    public DateTimeOffset? GeneratedAt { get; }
    public string Localization { get; }
    public IReadOnlyList<War3UnitDataIndexEntry> Entries { get; }

    public int LoadedCount
    {
        get
        {
            lock (_gate) return _cache.Count;
        }
    }

    public IReadOnlyDictionary<string, string> LoadErrors
    {
        get
        {
            lock (_gate)
                return new Dictionary<string, string>(
                    _loadErrors, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Opens a catalog without making data availability fatal. This is the
    /// composition-root entry point used by the playable scene; individual
    /// object mappings can then fall back to curated project defaults.
    /// </summary>
    public static War3UnitDataCatalog Open(string rootPath)
    {
        try
        {
            return Load(rootPath);
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          JsonException or
                                          InvalidDataException or
                                          ArgumentException)
        {
            return new War3UnitDataCatalog(
                NormalizeRoot(rootPath), string.Empty, false,
                exception.Message, null, string.Empty, []);
        }
    }

    /// <summary>Strict loader intended for tools and self-tests.</summary>
    public static War3UnitDataCatalog Load(string rootPath)
    {
        var root = NormalizeRoot(rootPath);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                "Warcraft III unit data manifest was not found.", manifestPath);

        var manifest = JsonSerializer.Deserialize<ManifestDocument>(
                           File.ReadAllText(manifestPath), JsonOptions) ??
                       throw new InvalidDataException(
                           "Warcraft III unit data manifest is empty.");
        if (!manifest.Schema.Equals(
                SupportedManifestSchema, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unsupported Warcraft III unit manifest schema '{manifest.Schema}'.");
        if (manifest.Units.Length == 0)
            throw new InvalidDataException(
                "Warcraft III unit data manifest contains no records.");
        if (manifest.Statistics.UnitCount > 0 &&
            manifest.Statistics.UnitCount != manifest.Units.Length)
            throw new InvalidDataException(
                "Warcraft III unit data manifest count does not match its index.");

        var entries = new List<War3UnitDataIndexEntry>(manifest.Units.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in manifest.Units)
        {
            if (string.IsNullOrWhiteSpace(value.Id) ||
                string.IsNullOrWhiteSpace(value.Path))
                throw new InvalidDataException(
                    "Warcraft III unit data manifest contains an incomplete entry.");
            if (!ids.Add(value.Id))
                throw new InvalidDataException(
                    $"Duplicate Warcraft III object id '{value.Id}'.");

            _ = ResolveDataPath(root, value.Path);
            entries.Add(new War3UnitDataIndexEntry(
                value.Id,
                value.DisplayName,
                value.Race,
                value.Category,
                value.IsHero,
                value.IsBuilding,
                NormalizeRelativePath(value.Path),
                value.ModelPath,
                value.IconPath,
                value.FieldCount));
        }

        return new War3UnitDataCatalog(
            root, manifest.Schema, true, string.Empty,
            manifest.GeneratedAt,
            manifest.Game.Localization,
            entries.ToArray());
    }

    public bool Contains(string objectId) =>
        IsAvailable && _index.ContainsKey(objectId);

    public bool TryGet(
        string objectId,
        [NotNullWhen(true)] out War3UnitData? data)
    {
        data = null;
        if (!IsAvailable || string.IsNullOrWhiteSpace(objectId) ||
            !_index.TryGetValue(objectId, out var entry))
            return false;

        lock (_gate)
        {
            if (_cache.TryGetValue(objectId, out data)) return true;
            if (_loadErrors.ContainsKey(objectId)) return false;
            try
            {
                var path = ResolveDataPath(RootPath, entry.RelativePath);
                data = JsonSerializer.Deserialize<War3UnitData>(
                           File.ReadAllText(path), JsonOptions) ??
                       throw new InvalidDataException(
                           $"Warcraft III unit data '{objectId}' is empty.");
                if (!data.Schema.Equals(
                        SupportedUnitSchema, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Unsupported unit schema '{data.Schema}' for '{objectId}'.");
                if (!data.Id.Equals(objectId, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Unit file id '{data.Id}' does not match manifest id '{objectId}'.");
                _cache.Add(objectId, data);
                return true;
            }
            catch (Exception exception) when (exception is IOException or
                                              UnauthorizedAccessException or
                                              JsonException or
                                              InvalidDataException)
            {
                _loadErrors[objectId] = exception.Message;
                data = null;
                return false;
            }
        }
    }

    public bool TryGetEditorValue(
        string objectId,
        string table,
        string field,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!TryGet(objectId, out var data)) return false;
        var tableValues = data.Editor.FirstOrDefault(pair =>
            pair.Key.Equals(table, StringComparison.OrdinalIgnoreCase)).Value;
        if (tableValues is null) return false;
        var match = tableValues.FirstOrDefault(pair =>
            pair.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
        if (match.Key is null) return false;
        value = match.Value;
        return true;
    }

    private static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "A Warcraft III unit data root is required.", nameof(rootPath));
        return Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static string ResolveDataPath(string rootPath, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalized));
        var prefix = rootPath + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Unit data path escapes the catalog root: {relativePath}");
        return fullPath;
    }

    private sealed class ManifestDocument
    {
        public string Schema { get; init; } = string.Empty;
        public DateTimeOffset? GeneratedAt { get; init; }
        public ManifestGame Game { get; init; } = new();
        public ManifestStatistics Statistics { get; init; } = new();
        public ManifestEntry[] Units { get; init; } = [];
    }

    private sealed class ManifestGame
    {
        public string Localization { get; init; } = string.Empty;
    }

    private sealed class ManifestStatistics
    {
        public int UnitCount { get; init; }
    }

    private sealed class ManifestEntry
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Race { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public bool IsHero { get; init; }
        public bool IsBuilding { get; init; }
        public string Path { get; init; } = string.Empty;
        public string ModelPath { get; init; } = string.Empty;
        public string IconPath { get; init; } = string.Empty;
        public int FieldCount { get; init; }
    }
}

public sealed class War3UnitData
{
    public string Schema { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public War3UnitIdentity Identity { get; init; } = new();
    public War3UnitAssetSet Assets { get; init; } = new();
    public War3UnitSummary Summary { get; init; } = new();
    public Dictionary<string, Dictionary<string, string>> Editor { get; init; } = [];
}

public sealed class War3UnitIdentity
{
    public string ClassName { get; init; } = string.Empty;
    public string[] ProperNames { get; init; } = [];
    public string EditorName { get; init; } = string.Empty;
    public string EditorClass { get; init; } = string.Empty;
    public string Race { get; init; } = string.Empty;
    public string RawRace { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool IsHero { get; init; }
    public bool IsBuilding { get; init; }
    public bool EditorVisible { get; init; }
    public bool Valid { get; init; }
}

public sealed class War3UnitAssetSet
{
    public War3UnitAssetReference? Model { get; init; }
    public War3UnitAssetReference? Portrait { get; init; }
    public War3UnitAssetReference? Icon { get; init; }
    public War3UnitAssetReference? Missile { get; init; }
    public War3UnitAssetReference? SpecialEffect { get; init; }
}

public sealed class War3UnitAssetReference
{
    public string RequestedPath { get; init; } = string.Empty;
    public string? ResolvedPath { get; init; }
    public string? SourceLayer { get; init; }
    public string? GodotPath { get; init; }
}

public sealed class War3UnitSummary
{
    public int? Level { get; init; }
    public War3HitPointSummary HitPoints { get; init; } = new();
    public War3ManaSummary Mana { get; init; } = new();
    public War3ArmorSummary Armor { get; init; } = new();
    public War3CostSummary Cost { get; init; } = new();
    public War3MovementSummary Movement { get; init; } = new();
    public War3SightSummary Sight { get; init; } = new();
    public War3HeroAttributeSummary HeroAttributes { get; init; } = new();
    public War3CombatSummary Combat { get; init; } = new();
    public string[] Abilities { get; init; } = [];
    public string? DefaultActiveAbility { get; init; }
    public string[] Upgrades { get; init; } = [];
}

public sealed class War3HitPointSummary
{
    public float? Base { get; init; }
    public float? Effective { get; init; }
    public float? Regeneration { get; init; }
    public string? RegenerationType { get; init; }
}

public sealed class War3ManaSummary
{
    public float? Initial { get; init; }
    public float? Maximum { get; init; }
    public float? Effective { get; init; }
    public float? Regeneration { get; init; }
}

public sealed class War3ArmorSummary
{
    public float? Base { get; init; }
    public float? Effective { get; init; }
    public string? Type { get; init; }
    public float? UpgradeAmount { get; init; }
}

public sealed class War3CostSummary
{
    public int? Gold { get; init; }
    public int? Lumber { get; init; }
    public int? FoodUsed { get; init; }
    public int? FoodProduced { get; init; }
    public float? BuildTime { get; init; }
    public float? RepairTime { get; init; }
    public int? RepairGold { get; init; }
    public int? RepairLumber { get; init; }
}

public sealed class War3MovementSummary
{
    public string? Type { get; init; }
    public float? Speed { get; init; }
    public float? MinimumSpeed { get; init; }
    public float? MaximumSpeed { get; init; }
    public float? TurnRate { get; init; }
    public float? CollisionSize { get; init; }
    public float? FlyingHeight { get; init; }
}

public sealed class War3SightSummary
{
    public float? Day { get; init; }
    public float? Night { get; init; }
}

public sealed class War3HeroAttributeSummary
{
    public string? Primary { get; init; }
    public float? Strength { get; init; }
    public float? StrengthPerLevel { get; init; }
    public float? Agility { get; init; }
    public float? AgilityPerLevel { get; init; }
    public float? Intelligence { get; init; }
    public float? IntelligencePerLevel { get; init; }
}

public sealed class War3CombatSummary
{
    public float? AcquisitionRange { get; init; }
    public War3AttackSummary[] Attacks { get; init; } = [];
}

public sealed class War3AttackSummary
{
    public bool Enabled { get; init; }
    public string? AttackType { get; init; }
    public string? WeaponType { get; init; }
    public string? SoundType { get; init; }
    public float? Cooldown { get; init; }
    public float? Range { get; init; }
    public float? MinimumRange { get; init; }
    public string[] Targets { get; init; } = [];
    public War3AttackDamageSummary Damage { get; init; } = new();
    public War3AttackTimingSummary Timing { get; init; } = new();
    public War3AttackAreaSummary Area { get; init; } = new();
}

public sealed class War3AttackDamageSummary
{
    public int? Dice { get; init; }
    public int? SidesPerDie { get; init; }
    public float? BaseBonus { get; init; }
    public float? Minimum { get; init; }
    public float? Average { get; init; }
    public float? Maximum { get; init; }
    public float? UpgradeAmount { get; init; }
}

public sealed class War3AttackTimingSummary
{
    public float? DamagePoint { get; init; }
    public float? Backswing { get; init; }
}

public sealed class War3AttackAreaSummary
{
    public float? FullDamageRadius { get; init; }
    public float? HalfDamageRadius { get; init; }
    public float? QuarterDamageRadius { get; init; }
    public string[] Targets { get; init; } = [];
}
