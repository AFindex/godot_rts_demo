using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace War3Rts.Audio;

public sealed record War3AudioCatalogPolicy
{
    public static War3AudioCatalogPolicy Default { get; } = new();

    /// <summary>Warcraft/editor ground units to Godot world units.</summary>
    public float WorldDistanceScale { get; init; } = 0.025f;
    public float MinimumWorldUnitSize { get; init; } = 0.5f;
    public float DefaultWorldUnitSize { get; init; } = 4f;
    public float DefaultMaximumWorldDistance { get; init; } = 80f;
    public float MaximumWorldDistanceLimit { get; init; } = 2_500f;
}

/// <summary>
/// Lazy read-only repository for the generated SoundInfo catalog. It owns path
/// normalization and deterministic variation selection, but has no Godot
/// dependency and performs no playback.
/// </summary>
public sealed class War3AudioCatalog : IWar3AudioCatalog
{
    public const string SupportedManifestSchema =
        "war3-audio-catalog-manifest/v1";
    public const string SupportedCueSchema = "war3-audio-cue/v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, CueIndexEntry> _cueIndex;
    private readonly Dictionary<string, War3AudioCueDefinition> _cueCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, War3UnitAudioBinding> _unitBindings;
    private readonly Dictionary<string, War3AbilityAudioBinding> _abilityBindings;
    private readonly Dictionary<string, string> _animationEvents;
    private readonly object _gate = new();

    private War3AudioCatalog(
        string rootPath,
        string runtimeResourceRoot,
        War3AudioCatalogPolicy policy,
        bool available,
        string error,
        IEnumerable<CueIndexEntry> cues,
        IEnumerable<War3UnitAudioBinding> unitBindings,
        IEnumerable<War3AbilityAudioBinding> abilityBindings,
        IReadOnlyDictionary<string, string> animationEvents)
    {
        RootPath = rootPath;
        RuntimeResourceRoot = runtimeResourceRoot.TrimEnd('/', '\\');
        Policy = policy;
        IsAvailable = available;
        Error = error;
        _cueIndex = cues.ToDictionary(value => value.Id,
            StringComparer.OrdinalIgnoreCase);
        _unitBindings = unitBindings.ToDictionary(value => value.ObjectId,
            StringComparer.Ordinal);
        _abilityBindings = abilityBindings.ToDictionary(value => value.AbilityId,
            StringComparer.Ordinal);
        _animationEvents = new Dictionary<string, string>(
            animationEvents, StringComparer.OrdinalIgnoreCase);
    }

    public string RootPath { get; }
    public string RuntimeResourceRoot { get; }
    public War3AudioCatalogPolicy Policy { get; }
    public bool IsAvailable { get; }
    public string Error { get; }
    public int CueCount => _cueIndex.Count;
    public int UnitBindingCount => _unitBindings.Count;
    public int AbilityBindingCount => _abilityBindings.Count;
    public int AnimationEventBindingCount => _animationEvents.Count;

    public static War3AudioCatalog Open(
        string rootPath,
        string runtimeResourceRoot = "res://assets/generated/warcraft3_audio",
        War3AudioCatalogPolicy? policy = null)
    {
        try
        {
            return Load(rootPath, runtimeResourceRoot, policy);
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          JsonException or
                                          InvalidDataException or
                                          ArgumentException)
        {
            return new War3AudioCatalog(
                NormalizeRoot(rootPath), runtimeResourceRoot,
                policy ?? War3AudioCatalogPolicy.Default,
                false, exception.Message, [], [], [],
                new Dictionary<string, string>());
        }
    }

    public static War3AudioCatalog Load(
        string rootPath,
        string runtimeResourceRoot = "res://assets/generated/warcraft3_audio",
        War3AudioCatalogPolicy? policy = null)
    {
        var root = NormalizeRoot(rootPath);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                "Warcraft III audio catalog manifest was not found.",
                manifestPath);
        var manifest = JsonSerializer.Deserialize<ManifestDocument>(
                           File.ReadAllText(manifestPath), JsonOptions) ??
                       throw new InvalidDataException(
                           "Warcraft III audio manifest is empty.");
        if (!manifest.Schema.Equals(
                SupportedManifestSchema, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unsupported Warcraft III audio manifest schema '{manifest.Schema}'.");
        if (manifest.Cues.Length == 0)
            throw new InvalidDataException(
                "Warcraft III audio manifest contains no cues.");

        var cueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cues = new List<CueIndexEntry>(manifest.Cues.Length);
        foreach (var value in manifest.Cues)
        {
            if (string.IsNullOrWhiteSpace(value.Id) ||
                string.IsNullOrWhiteSpace(value.Path) ||
                !cueIds.Add(value.Id))
                throw new InvalidDataException(
                    "Warcraft III audio manifest contains an invalid cue entry.");
            var cuePath = ResolveDataPath(root, value.Path);
            if (!File.Exists(cuePath))
                throw new FileNotFoundException(
                    $"Audio cue '{value.Id}' was not found.", cuePath);
            cues.Add(new CueIndexEntry(
                value.Id, value.Category, NormalizeRelativePath(value.Path)));
        }

        var bindings = LoadUnitBindings(root);
        var abilityBindings = LoadAbilityBindings(root);
        var animationEvents = LoadAnimationEventBindings(root, cueIds);
        return new War3AudioCatalog(
            root, runtimeResourceRoot,
            policy ?? War3AudioCatalogPolicy.Default,
            true, string.Empty, cues, bindings, abilityBindings,
            animationEvents);
    }

    public bool ContainsCue(string cueId) =>
        IsAvailable && !string.IsNullOrWhiteSpace(cueId) &&
        _cueIndex.ContainsKey(cueId);

    public bool TryGetUnitBinding(
        string objectId,
        [NotNullWhen(true)] out War3UnitAudioBinding? binding) =>
        _unitBindings.TryGetValue(objectId, out binding);

    public bool TryGetAbilityBinding(
        string abilityId,
        [NotNullWhen(true)] out War3AbilityAudioBinding? binding) =>
        _abilityBindings.TryGetValue(abilityId, out binding);

    public bool TryGetAnimationEventCue(
        string eventCode,
        [NotNullWhen(true)] out string? cueId) =>
        _animationEvents.TryGetValue(eventCode, out cueId);

    public bool TryResolve(
        in War3AudioCueRequest request,
        out War3ResolvedAudioCue cue)
    {
        cue = default;
        if (!TryGetCue(request.CueId, out var definition) ||
            definition.ResourcePaths.Length == 0)
            return false;
        var index = VariationIndex(
            request.CueId,
            request.EventSequence,
            request.EmitterId,
            definition.ResourcePaths.Length);
        var pitch = definition.Pitch;
        if (definition.PitchVariance > 0f &&
            definition.Flags.Contains(
                "RANDOMPITCH", StringComparer.OrdinalIgnoreCase))
        {
            var normalized = VariationUnit(
                request.CueId, request.EventSequence, request.EmitterId);
            pitch *= Math.Max(
                0.05f,
                1f + (normalized * 2f - 1f) * definition.PitchVariance);
        }
        cue = new War3ResolvedAudioCue(
            definition.Id,
            definition.ResourcePaths[index],
            request.WorldPosition is null
                ? War3AudioSpatialMode.NonPositional
                : definition.SpatialMode,
            request.LoopOverride ?? definition.Loop,
            definition.VolumeLinear,
            pitch,
            definition.MinimumDistance,
            definition.MaximumDistance,
            definition.CutoffDistance,
            definition.Priority,
            definition.Bus);
        return true;
    }

    private bool TryGetCue(
        string cueId,
        [NotNullWhen(true)] out War3AudioCueDefinition? cue)
    {
        cue = null;
        if (!IsAvailable || string.IsNullOrWhiteSpace(cueId) ||
            !_cueIndex.TryGetValue(cueId, out var entry))
            return false;
        lock (_gate)
        {
            if (_cueCache.TryGetValue(cueId, out cue)) return true;
            try
            {
                var document = JsonSerializer.Deserialize<CueDocument>(
                                   File.ReadAllText(ResolveDataPath(
                                       RootPath, entry.RelativePath)),
                                   JsonOptions) ??
                               throw new InvalidDataException(
                                   $"Audio cue '{cueId}' is empty.");
                if (!document.Schema.Equals(
                        SupportedCueSchema, StringComparison.Ordinal) ||
                    !document.Id.Equals(cueId,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Audio cue '{cueId}' has an invalid schema or id.");
                var normalized = document.Normalized;
                var resources = normalized.Sources
                    .Where(value => value.Exists &&
                                    !string.IsNullOrWhiteSpace(value.ResolvedPath))
                    .Select(value => RuntimeResourcePath(value.ResolvedPath!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var spatial = ParseSpatial(normalized.SpatialMode);
                var unitSize = Distance(
                    normalized.MinDistance,
                    Policy.DefaultWorldUnitSize,
                    Policy.MinimumWorldUnitSize,
                    Policy.MaximumWorldDistanceLimit);
                var maximum = Distance(
                    normalized.MaxDistance,
                    Policy.DefaultMaximumWorldDistance,
                    unitSize,
                    Policy.MaximumWorldDistanceLimit);
                var cutoff = Distance(
                    normalized.DistanceCutoff,
                    maximum,
                    maximum,
                    Policy.MaximumWorldDistanceLimit);
                cue = new War3AudioCueDefinition(
                    document.Id,
                    document.Category.Length > 0
                        ? document.Category
                        : entry.Category,
                    resources,
                    spatial,
                    normalized.Loop,
                    Math.Clamp(normalized.VolumeLinear, 0f, 4f),
                    normalized.Pitch > 0f ? normalized.Pitch : 1f,
                    Math.Max(0f, normalized.PitchVariance),
                    unitSize,
                    maximum,
                    cutoff,
                    normalized.PriorityRaw,
                    Bus(normalized.SuggestedBus, document.Category),
                    normalized.Flags);
                _cueCache.Add(cueId, cue);
                return true;
            }
            catch (Exception exception) when (exception is IOException or
                                              UnauthorizedAccessException or
                                              JsonException or
                                              InvalidDataException)
            {
                return false;
            }
        }
    }

    private string RuntimeResourcePath(string sourcePath) =>
        $"{RuntimeResourceRoot}/{sourcePath.Replace('\\', '/').TrimStart('/')}";

    private float Distance(
        float raw,
        float fallback,
        float minimum,
        float maximum)
    {
        if (!float.IsFinite(raw) || raw <= 0f) return fallback;
        return Math.Clamp(raw * Policy.WorldDistanceScale, minimum, maximum);
    }

    private static IReadOnlyList<War3UnitAudioBinding> LoadUnitBindings(
        string root)
    {
        var directory = Path.Combine(root, "audio_refs", "units");
        if (!Directory.Exists(directory)) return [];
        var result = new List<War3UnitAudioBinding>();
        foreach (var path in Directory.EnumerateFiles(
                     directory, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var value = document.RootElement;
            var objectId = String(value, "objectId", "id");
            if (objectId.Length == 0) continue;
            var references = value.TryGetProperty("references", out var nested) &&
                             nested.ValueKind == JsonValueKind.Object
                ? nested
                : value;
            var attacks = new List<War3WeaponAudioBinding>();
            if (references.TryGetProperty("attacks", out var attackValues) &&
                attackValues.ValueKind == JsonValueKind.Array)
            {
                foreach (var attack in attackValues.EnumerateArray())
                {
                    var family = ReferenceLabel(attack, "soundFamily");
                    if (family.Length == 0) continue;
                    var sourceIndex = Integer(
                        attack, "index", attacks.Count + 1);
                    attacks.Add(new War3WeaponAudioBinding(
                        Math.Max(0, sourceIndex - 1), family));
                }
            }
            result.Add(new War3UnitAudioBinding(
                objectId,
                ReferenceLabel(references, "voiceSet"),
                String(references, "impactMaterial", "armorMaterial"),
                attacks.ToArray(),
                ReferenceLabel(references, "movement"),
                ReferenceLabel(references, "building"),
                Number(references, "loopFadeInSeconds", "loopingSoundFadeIn"),
                Number(references, "loopFadeOutSeconds", "loopingSoundFadeOut")));
        }
        return result;
    }

    private static IReadOnlyList<War3AbilityAudioBinding> LoadAbilityBindings(
        string root)
    {
        var directory = Path.Combine(root, "audio_refs", "abilities");
        if (!Directory.Exists(directory)) return [];
        var result = new List<War3AbilityAudioBinding>();
        foreach (var path in Directory.EnumerateFiles(
                     directory, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var value = document.RootElement;
            var abilityId = String(value, "abilityId", "id");
            if (abilityId.Length == 0) continue;
            var references = value.TryGetProperty("references", out var nested) &&
                             nested.ValueKind == JsonValueKind.Object
                ? nested
                : value;
            result.Add(new War3AbilityAudioBinding(
                abilityId,
                ReferenceLabel(references, "effect"),
                ReferenceLabel(references, "loopedEffect")));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string>
        LoadAnimationEventBindings(string root, ISet<string> cueIds)
    {
        var path = Path.Combine(root, "animation_event_map.json");
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("events", out var events) ||
            events.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var value in events.EnumerateArray())
        {
            var eventCode = String(value, "eventCode");
            if (eventCode.Length == 0 ||
                !value.TryGetProperty("sound", out var sound) ||
                sound.ValueKind != JsonValueKind.Object ||
                !sound.TryGetProperty("cues", out var cues) ||
                cues.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var cue in cues.EnumerateArray())
            {
                var cueId = String(cue, "id");
                if (cueId.Length == 0 || !cueIds.Contains(cueId)) continue;
                result.TryAdd(eventCode, cueId);
                break;
            }
        }
        return result;
    }

    private static string ReferenceLabel(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return string.Empty;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        return value.ValueKind == JsonValueKind.Object
            ? String(value, "label", "id", "value")
            : string.Empty;
    }

    private static string String(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (value.TryGetProperty(name, out var property) &&
                property.ValueKind == JsonValueKind.String)
                return property.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static int Integer(JsonElement value, string name, int fallback)
    {
        if (!value.TryGetProperty(name, out var property)) return fallback;
        if (property.TryGetInt32(out var number)) return number;
        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), out number)
            ? number
            : fallback;
    }

    private static float Number(
        JsonElement value,
        string first,
        string second)
    {
        foreach (var name in new[] { first, second })
        {
            if (!value.TryGetProperty(name, out var property)) continue;
            if (property.TryGetSingle(out var number)) return number;
            if (property.ValueKind == JsonValueKind.String &&
                float.TryParse(property.GetString(), out number))
                return number;
        }
        return 0f;
    }

    private static War3AudioSpatialMode ParseSpatial(string value) =>
        value.Contains('3', StringComparison.OrdinalIgnoreCase) ||
        value.Equals("world", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("spatial", StringComparison.OrdinalIgnoreCase)
            ? War3AudioSpatialMode.World3D
            : War3AudioSpatialMode.NonPositional;

    private static string Bus(string requested, string category)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested;
        return category.ToLowerInvariant() switch
        {
            "voice" => "Voice",
            "ui" => "UI",
            "dialogue" => "Cinematic",
            "ambience" or "midi" => "Ambience",
            "ability" => "Ability",
            "combat" or "animation" => "Combat",
            _ => "SFX"
        };
    }

    private static int VariationIndex(
        string cueId,
        ulong sequence,
        int emitterId,
        int count) =>
        count <= 1 ? 0 : (int)(Hash(cueId, sequence, emitterId) % (uint)count);

    private static float VariationUnit(
        string cueId,
        ulong sequence,
        int emitterId) =>
        (Hash(cueId, sequence ^ 0x9e3779b97f4a7c15UL, emitterId) &
         0x00ff_ffff) / 16_777_215f;

    private static uint Hash(string value, ulong sequence, int emitterId)
    {
        const uint offset = 2_166_136_261;
        const uint prime = 16_777_619;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= char.ToUpperInvariant(character);
            hash *= prime;
        }
        for (var shift = 0; shift < 64; shift += 8)
        {
            hash ^= (byte)(sequence >> shift);
            hash *= prime;
        }
        hash ^= unchecked((uint)emitterId);
        hash *= prime;
        return hash;
    }

    private static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "A Warcraft III audio root is required.", nameof(rootPath));
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
                $"Audio data path escapes its root: {relativePath}");
        return fullPath;
    }

    private sealed record CueIndexEntry(
        string Id,
        string Category,
        string RelativePath);

    private sealed class ManifestDocument
    {
        public string Schema { get; init; } = string.Empty;
        public ManifestCue[] Cues { get; init; } = [];
    }

    private sealed class ManifestCue
    {
        public string Id { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
    }

    private sealed class CueDocument
    {
        public string Schema { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public CueNormalized Normalized { get; init; } = new();
    }

    private sealed class CueNormalized
    {
        public CueSource[] Sources { get; init; } = [];
        public string SpatialMode { get; init; } = string.Empty;
        public bool Loop { get; init; }
        public float VolumeLinear { get; init; } = 1f;
        public float Pitch { get; init; } = 1f;
        public float PitchVariance { get; init; }
        public int PriorityRaw { get; init; }
        public float MinDistance { get; init; }
        public float MaxDistance { get; init; }
        public float DistanceCutoff { get; init; }
        public string[] Flags { get; init; } = [];
        public string SuggestedBus { get; init; } = string.Empty;
    }

    private sealed class CueSource
    {
        public string? ResolvedPath { get; init; }
        public bool Exists { get; init; }
    }
}
