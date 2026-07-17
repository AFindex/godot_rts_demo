using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace War3Rts.Data;

public sealed record War3AbilityMetadataFieldIndexEntry(
    string Id,
    string DisplayName,
    string RelativePath);

public sealed record War3AbilityMetadataBindingIndexEntry(
    string Id,
    string BaseCode,
    int FieldCount,
    string RelativePath);

/// <summary>
/// Strict, lazy loader for the exported AbilityMetaData field definitions and
/// their resolved per-ability applicability. This catalog describes editor
/// field meaning and types; gameplay behavior remains in content compilers.
/// </summary>
public sealed class War3AbilityMetadataCatalog
{
    public const string SupportedManifestSchema =
        "war3-ability-metadata-manifest/v1";
    public const string SupportedFieldSchema =
        "war3-ability-metadata-field/v1";
    public const string SupportedBindingSchema =
        "war3-ability-metadata-binding/v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, War3AbilityMetadataFieldIndexEntry>
        _fieldIndex;
    private readonly Dictionary<string, War3AbilityMetadataBindingIndexEntry>
        _bindingIndex;
    private readonly Dictionary<string, War3AbilityMetadataField> _fields =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, War3AbilityMetadataBinding> _bindings =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _loadErrors =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private War3AbilityMetadataCatalog(
        string rootPath,
        bool isAvailable,
        string error,
        string schema,
        DateTimeOffset? generatedAt,
        string localization,
        IReadOnlyList<War3AbilityMetadataFieldIndexEntry> fields,
        IReadOnlyList<War3AbilityMetadataBindingIndexEntry> bindings)
    {
        RootPath = rootPath;
        IsAvailable = isAvailable;
        Error = error;
        Schema = schema;
        GeneratedAt = generatedAt;
        Localization = localization;
        Fields = fields;
        Bindings = bindings;
        _fieldIndex = fields.ToDictionary(value => value.Id, StringComparer.Ordinal);
        _bindingIndex = bindings.ToDictionary(value => value.Id, StringComparer.Ordinal);
    }

    public string RootPath { get; }
    public bool IsAvailable { get; }
    public string Error { get; }
    public string Schema { get; }
    public DateTimeOffset? GeneratedAt { get; }
    public string Localization { get; }
    public IReadOnlyList<War3AbilityMetadataFieldIndexEntry> Fields { get; }
    public IReadOnlyList<War3AbilityMetadataBindingIndexEntry> Bindings { get; }
    public int FieldCount => Fields.Count;
    public int BindingCount => Bindings.Count;

    public IReadOnlyDictionary<string, string> LoadErrors
    {
        get
        {
            lock (_gate)
                return new Dictionary<string, string>(
                    _loadErrors, StringComparer.Ordinal);
        }
    }

    public static War3AbilityMetadataCatalog Open(string rootPath)
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
            return new War3AbilityMetadataCatalog(
                NormalizeRoot(rootPath), false, exception.Message,
                string.Empty, null, string.Empty, [], []);
        }
    }

    public static War3AbilityMetadataCatalog Load(string rootPath)
    {
        var root = NormalizeRoot(rootPath);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                "Warcraft ability metadata manifest was not found.",
                manifestPath);
        var manifest = JsonSerializer.Deserialize<ManifestDocument>(
                           File.ReadAllText(manifestPath), JsonOptions) ??
                       throw new InvalidDataException(
                           "Warcraft ability metadata manifest is empty.");
        if (!manifest.Schema.Equals(
                SupportedManifestSchema, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unsupported ability metadata schema '{manifest.Schema}'.");
        if (manifest.Statistics.FieldCount != manifest.Fields.Length ||
            manifest.Statistics.AbilityBindingCount != manifest.Abilities.Length)
            throw new InvalidDataException(
                "Warcraft ability metadata manifest counts are invalid.");

        var fieldIds = new HashSet<string>(StringComparer.Ordinal);
        var fields = manifest.Fields.Select(value =>
        {
            ValidateEntry(root, value.Id, value.Path, fieldIds, "field");
            return new War3AbilityMetadataFieldIndexEntry(
                value.Id, value.DisplayName,
                NormalizeRelativePath(value.Path));
        }).ToArray();
        var abilityIds = new HashSet<string>(StringComparer.Ordinal);
        var bindings = manifest.Abilities.Select(value =>
        {
            ValidateEntry(root, value.Id, value.Path, abilityIds, "binding");
            if (value.FieldCount < 0)
                throw new InvalidDataException(
                    $"Ability metadata binding '{value.Id}' has an invalid field count.");
            return new War3AbilityMetadataBindingIndexEntry(
                value.Id, value.BaseCode, value.FieldCount,
                NormalizeRelativePath(value.Path));
        }).ToArray();
        return new War3AbilityMetadataCatalog(
            root, true, string.Empty, manifest.Schema, manifest.GeneratedAt,
            manifest.Game.Localization, fields, bindings);
    }

    public bool TryGetField(
        string id,
        [NotNullWhen(true)] out War3AbilityMetadataField? field) =>
        TryLoad(
            id, _fieldIndex, _fields, SupportedFieldSchema,
            static value => value.Schema,
            static value => value.Id,
            out field);

    public bool TryGetBinding(
        string abilityId,
        [NotNullWhen(true)] out War3AbilityMetadataBinding? binding) =>
        TryLoad(
            abilityId, _bindingIndex, _bindings, SupportedBindingSchema,
            static value => value.Schema,
            static value => value.Id,
            out binding);

    private bool TryLoad<TIndex, TValue>(
        string id,
        IReadOnlyDictionary<string, TIndex> index,
        IDictionary<string, TValue> cache,
        string schema,
        Func<TValue, string> schemaSelector,
        Func<TValue, string> idSelector,
        [NotNullWhen(true)] out TValue? value)
        where TIndex : notnull
        where TValue : class
    {
        value = null;
        if (!IsAvailable || string.IsNullOrWhiteSpace(id) ||
            !index.TryGetValue(id, out var indexValue))
            return false;
        var relativePath = indexValue switch
        {
            War3AbilityMetadataFieldIndexEntry field => field.RelativePath,
            War3AbilityMetadataBindingIndexEntry binding => binding.RelativePath,
            _ => throw new InvalidOperationException(
                "Unknown ability metadata index entry type.")
        };
        lock (_gate)
        {
            if (cache.TryGetValue(id, out value)) return true;
            if (_loadErrors.ContainsKey(id)) return false;
            try
            {
                value = JsonSerializer.Deserialize<TValue>(
                            File.ReadAllText(ResolveDataPath(
                                RootPath, relativePath)), JsonOptions) ??
                        throw new InvalidDataException(
                            $"Ability metadata '{id}' is empty.");
                if (!schemaSelector(value).Equals(schema, StringComparison.Ordinal) ||
                    !idSelector(value).Equals(id, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Ability metadata '{id}' has an invalid schema or id.");
                cache.Add(id, value);
                return true;
            }
            catch (Exception exception) when (exception is IOException or
                                              UnauthorizedAccessException or
                                              JsonException or
                                              InvalidDataException)
            {
                _loadErrors[id] = exception.Message;
                value = null;
                return false;
            }
        }
    }

    private static void ValidateEntry(
        string root,
        string id,
        string path,
        ISet<string> ids,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path) ||
            !ids.Add(id))
            throw new InvalidDataException(
                $"Ability metadata manifest contains an invalid {kind} entry.");
        _ = ResolveDataPath(root, path);
    }

    private static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "An ability metadata root is required.", nameof(rootPath));
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
                $"Ability metadata path escapes its root: {relativePath}");
        return fullPath;
    }

    private sealed class ManifestDocument
    {
        public string Schema { get; init; } = string.Empty;
        public DateTimeOffset? GeneratedAt { get; init; }
        public ManifestGame Game { get; init; } = new();
        public ManifestStatistics Statistics { get; init; } = new();
        public ManifestField[] Fields { get; init; } = [];
        public ManifestAbility[] Abilities { get; init; } = [];
    }

    private sealed class ManifestGame
    {
        public string Localization { get; init; } = string.Empty;
    }

    private sealed class ManifestStatistics
    {
        public int FieldCount { get; init; }
        public int AbilityBindingCount { get; init; }
    }

    private sealed class ManifestField
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
    }

    private sealed class ManifestAbility
    {
        public string Id { get; init; } = string.Empty;
        public string BaseCode { get; init; } = string.Empty;
        public int FieldCount { get; init; }
        public string Path { get; init; } = string.Empty;
    }
}

public sealed class War3AbilityMetadataField
{
    public string Schema { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string? Field { get; init; }
    public string? Storage { get; init; }
    public int? Index { get; init; }
    public bool? Repeat { get; init; }
    public int? DataIndex { get; init; }
    public string? Category { get; init; }
    public string? DisplayNameKey { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Sort { get; init; }
    public string? ValueType { get; init; }
    public float? Minimum { get; init; }
    public float? Maximum { get; init; }
    public bool? CanBeEmpty { get; init; }
    public bool? ForceNonNegative { get; init; }
    public War3AbilityMetadataApplicability Applicability { get; init; } = new();
    public Dictionary<string, string> EditorData { get; init; } = [];
}

public sealed class War3AbilityMetadataApplicability
{
    public bool? Unit { get; init; }
    public bool? Hero { get; init; }
    public bool? Item { get; init; }
    public bool? Creep { get; init; }
    public string[] Specific { get; init; } = [];
    public string[] Excluded { get; init; } = [];
}

public sealed class War3AbilityMetadataBinding
{
    public string Schema { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string BaseCode { get; init; } = string.Empty;
    public War3AbilityMetadataIdentity Identity { get; init; } = new();
    public string[] FieldIds { get; init; } = [];
    public War3AbilityMetadataBindingField[] Fields { get; init; } = [];
}

public sealed class War3AbilityMetadataIdentity
{
    public string Race { get; init; } = string.Empty;
    public bool Hero { get; init; }
    public bool Item { get; init; }
}

public sealed class War3AbilityMetadataBindingField
{
    public string Id { get; init; } = string.Empty;
    public string? Field { get; init; }
    public int? DataIndex { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? ValueType { get; init; }
    public float? Minimum { get; init; }
    public float? Maximum { get; init; }
}
