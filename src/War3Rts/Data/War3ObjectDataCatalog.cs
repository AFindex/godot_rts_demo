using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace War3Rts.Data;

public enum War3ObjectDataKind : byte
{
    Ability,
    Upgrade,
    BuffEffect
}

public sealed record War3ObjectDataIndexEntry(
    string Id,
    string DisplayName,
    string Race,
    string RelativePath);

/// <summary>
/// Lazy, read-only repository shared by Warcraft ability and upgrade exports.
/// The simulation only consumes explicitly adapted values and never reads JSON.
/// </summary>
public sealed class War3ObjectDataCatalog
{
    private const string AbilityManifestSchema =
        "war3-ability-editor-manifest/v1";
    private const string AbilityObjectSchema = "war3-ability-editor-data/v1";
    private const string UpgradeManifestSchema =
        "war3-upgrade-editor-manifest/v1";
    private const string UpgradeObjectSchema = "war3-upgrade-editor-data/v1";
    private const string BuffEffectManifestSchema =
        "war3-buff-effect-editor-manifest/v1";
    private const string BuffEffectObjectSchema =
        "war3-buff-effect-editor-data/v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, War3ObjectDataIndexEntry> _index;
    private readonly Dictionary<string, War3ObjectEditorData> _cache =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _loadErrors =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private War3ObjectDataCatalog(
        War3ObjectDataKind kind,
        string rootPath,
        bool isAvailable,
        string error,
        string schema,
        DateTimeOffset? generatedAt,
        string localization,
        IReadOnlyList<War3ObjectDataIndexEntry> entries)
    {
        Kind = kind;
        RootPath = rootPath;
        IsAvailable = isAvailable;
        Error = error;
        Schema = schema;
        GeneratedAt = generatedAt;
        Localization = localization;
        Entries = entries;
        _index = entries.ToDictionary(value => value.Id, StringComparer.Ordinal);
    }

    public War3ObjectDataKind Kind { get; }
    public string RootPath { get; }
    public bool IsAvailable { get; }
    public string Error { get; }
    public string Schema { get; }
    public DateTimeOffset? GeneratedAt { get; }
    public string Localization { get; }
    public IReadOnlyList<War3ObjectDataIndexEntry> Entries { get; }
    public int Count => Entries.Count;
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

    public static War3ObjectDataCatalog OpenAbility(string rootPath) =>
        Open(War3ObjectDataKind.Ability, rootPath);

    public static War3ObjectDataCatalog OpenUpgrade(string rootPath) =>
        Open(War3ObjectDataKind.Upgrade, rootPath);

    public static War3ObjectDataCatalog OpenBuffEffect(string rootPath) =>
        Open(War3ObjectDataKind.BuffEffect, rootPath);

    public static War3ObjectDataCatalog LoadAbility(string rootPath) =>
        Load(War3ObjectDataKind.Ability, rootPath);

    public static War3ObjectDataCatalog LoadUpgrade(string rootPath) =>
        Load(War3ObjectDataKind.Upgrade, rootPath);

    public static War3ObjectDataCatalog LoadBuffEffect(string rootPath) =>
        Load(War3ObjectDataKind.BuffEffect, rootPath);

    public bool Contains(string objectId) =>
        IsAvailable && _index.ContainsKey(objectId);

    public bool TryGet(
        string objectId,
        [NotNullWhen(true)] out War3ObjectEditorData? data)
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
                data = JsonSerializer.Deserialize<War3ObjectEditorData>(
                           File.ReadAllText(ResolveDataPath(
                               RootPath, entry.RelativePath)), JsonOptions) ??
                       throw new InvalidDataException(
                           $"Warcraft object data '{objectId}' is empty.");
                if (!data.Schema.Equals(ObjectSchema(Kind),
                        StringComparison.Ordinal) ||
                    !data.Id.Equals(objectId, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Warcraft object file '{objectId}' has an invalid schema or id.");
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

    private static War3ObjectDataCatalog Open(
        War3ObjectDataKind kind,
        string rootPath)
    {
        try
        {
            return Load(kind, rootPath);
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          JsonException or
                                          InvalidDataException or
                                          ArgumentException)
        {
            return new War3ObjectDataCatalog(
                kind, NormalizeRoot(rootPath), false, exception.Message,
                string.Empty, null, string.Empty, []);
        }
    }

    private static War3ObjectDataCatalog Load(
        War3ObjectDataKind kind,
        string rootPath)
    {
        var root = NormalizeRoot(rootPath);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                $"Warcraft {kind} manifest was not found.", manifestPath);
        var manifest = JsonSerializer.Deserialize<ManifestDocument>(
                           File.ReadAllText(manifestPath), JsonOptions) ??
                       throw new InvalidDataException(
                           $"Warcraft {kind} manifest is empty.");
        if (!manifest.Schema.Equals(ManifestSchema(kind),
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unsupported Warcraft {kind} manifest schema '{manifest.Schema}'.");
        if (manifest.Objects.Length == 0 ||
            manifest.Statistics.ObjectCount != manifest.Objects.Length)
            throw new InvalidDataException(
                $"Warcraft {kind} manifest count is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<War3ObjectDataIndexEntry>(
            manifest.Objects.Length);
        foreach (var value in manifest.Objects)
        {
            if (string.IsNullOrWhiteSpace(value.Id) ||
                string.IsNullOrWhiteSpace(value.Path) || !ids.Add(value.Id))
                throw new InvalidDataException(
                    $"Warcraft {kind} manifest contains an invalid entry.");
            _ = ResolveDataPath(root, value.Path);
            entries.Add(new War3ObjectDataIndexEntry(
                value.Id, value.DisplayName, value.Race,
                NormalizeRelativePath(value.Path)));
        }
        return new War3ObjectDataCatalog(
            kind, root, true, string.Empty, manifest.Schema,
            manifest.GeneratedAt, manifest.Game.Localization,
            entries.ToArray());
    }

    private static string ManifestSchema(War3ObjectDataKind kind) => kind switch
    {
        War3ObjectDataKind.Ability => AbilityManifestSchema,
        War3ObjectDataKind.Upgrade => UpgradeManifestSchema,
        War3ObjectDataKind.BuffEffect => BuffEffectManifestSchema,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string ObjectSchema(War3ObjectDataKind kind) => kind switch
    {
        War3ObjectDataKind.Ability => AbilityObjectSchema,
        War3ObjectDataKind.Upgrade => UpgradeObjectSchema,
        War3ObjectDataKind.BuffEffect => BuffEffectObjectSchema,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "A Warcraft object data root is required.", nameof(rootPath));
        return Path.GetFullPath(rootPath).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRelativePath(string value) =>
        value.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static string ResolveDataPath(string root, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            root, NormalizeRelativePath(relativePath)));
        if (!fullPath.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Object data path escapes its root: {relativePath}");
        return fullPath;
    }

    private sealed class ManifestDocument
    {
        public string Schema { get; init; } = string.Empty;
        public DateTimeOffset? GeneratedAt { get; init; }
        public ManifestGame Game { get; init; } = new();
        public ManifestStatistics Statistics { get; init; } = new();
        public ManifestEntry[] Objects { get; init; } = [];
    }

    private sealed class ManifestGame
    {
        public string Localization { get; init; } = string.Empty;
    }

    private sealed class ManifestStatistics
    {
        public int ObjectCount { get; init; }
    }

    private sealed class ManifestEntry
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Race { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
    }
}

public sealed class War3ObjectEditorData
{
    public string Schema { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public War3ObjectIdentity Identity { get; init; } = new();
    public Dictionary<string, War3UnitAssetReference[]> Assets { get; init; } = [];
    public War3ObjectSummary Summary { get; init; } = new();
    public War3BuffEffectPresentation Presentation { get; init; } = new();
    public Dictionary<string, string> EditorData { get; init; } = [];
    public Dictionary<string, string> Profile { get; init; } = [];
}

public sealed class War3ObjectIdentity
{
    public string Race { get; init; } = string.Empty;
    public string BaseCode { get; init; } = string.Empty;
    public string Class { get; init; } = string.Empty;
    public bool Hero { get; init; }
    public bool Item { get; init; }
    public bool Effect { get; init; }
    public bool ProfileOnly { get; init; }
    public bool? EditorVisible { get; init; }
    public bool Used { get; init; }
    public bool Global { get; init; }
    public int Levels { get; init; }
    public int RequiredHeroLevel { get; init; }
    public int HeroLevelSkip { get; init; }
}

public sealed class War3ObjectSummary
{
    public War3ObjectLevel[] Levels { get; init; } = [];
    public War3UpgradeEffect[] Effects { get; init; } = [];
}

public sealed class War3ObjectLevel
{
    public int Level { get; init; }
    public string? Name { get; init; }
    public string? Tooltip { get; init; }
    public string? ExtendedTooltip { get; init; }
    public string? Hotkey { get; init; }
    public int? Gold { get; init; }
    public int? Lumber { get; init; }
    public float? ResearchSeconds { get; init; }
    public War3UnitAssetReference? Icon { get; init; }
    public string[] Requirements { get; init; } = [];
    public string[] Targets { get; init; } = [];
    public float? CastTime { get; init; }
    public float? Duration { get; init; }
    public float? HeroDuration { get; init; }
    public float? Cooldown { get; init; }
    public float? ManaCost { get; init; }
    public float? Area { get; init; }
    public float? Range { get; init; }
    public Dictionary<string, string> Data { get; init; } = [];
    public string[] UnitIds { get; init; } = [];
    public string? SummonedUnitId { get; init; }
    public string[] BuffIds { get; init; } = [];
    public string[] EffectIds { get; init; } = [];
    public int? RequirementLevel { get; init; }
}

public sealed class War3BuffEffectPresentation
{
    public string[] CasterAttachments { get; init; } = [];
    public int? CasterAttachmentCount { get; init; }
    public string[] TargetAttachments { get; init; } = [];
    public int? TargetAttachmentCount { get; init; }
    public string[] SpecialAttachments { get; init; } = [];
    public int? SpecialAttachmentCount { get; init; }
    public string? EffectSound { get; init; }
    public string? EffectSoundLooped { get; init; }
}

public sealed class War3UpgradeEffect
{
    public int Slot { get; init; }
    public string Type { get; init; } = string.Empty;
    public float? Base { get; init; }
    public float? Modifier { get; init; }
    public string? Code { get; init; }
}
