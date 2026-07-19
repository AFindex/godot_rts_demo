using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace RtsDemo.Demos.War3;

public enum War3AssetCategory : byte
{
    Units,
    Heroes,
    Buildings,
    Portraits,
    Effects,
    Environment,
    Terrain,
    Interface,
    Miscellaneous
}

public sealed record War3AssetEntry(
    string Source,
    string ModelRelativePath,
    string MetadataRelativePath,
    string DisplayName,
    string Id,
    string Race,
    string IconPath,
    War3AssetCategory Category,
    int SequenceCount,
    int GeosetCount,
    int ParticleCount,
    int RibbonCount,
    int TextureCount)
{
    public bool HasEffects => ParticleCount > 0 || RibbonCount > 0;
}

public sealed record War3CameraDefinition(
    Vector3 Position,
    Vector3 TargetPosition,
    float FieldOfViewRadians,
    float NearClip,
    float FarClip);

public static partial class War3AssetPack
{
    public const string ResourceRoot = "res://assets/warcraft3/classic";
    private static Dictionary<string, War3CameraDefinition>? _portraitCameras;

    public static string AbsoluteRoot =>
        ProjectSettings.GlobalizePath(ResourceRoot).TrimEnd('/', '\\');

    public static string AbsolutePath(string relativePath) => Path.Combine(
        AbsoluteRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar));

    public static IReadOnlyList<War3AssetEntry> LoadCatalog()
    {
        var display = LoadDisplayCatalog();
        var manifestPath = AbsolutePath("catalog/manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var output = new List<War3AssetEntry>();
        foreach (var model in document.RootElement.GetProperty("models").EnumerateArray())
        {
            var source = ReadString(model, "source");
            display.TryGetValue(source, out var localized);
            output.Add(new War3AssetEntry(
                source,
                ReadString(model, "output"),
                ReadString(model, "metadata"),
                localized?.Name is { Length: > 0 } name ? name : Humanize(source),
                localized?.Id ?? string.Empty,
                localized?.Race ?? InferRace(source),
                localized?.IconPath ?? string.Empty,
                localized is null ? InferCategory(source) : ParseCategory(localized.Category, source),
                ReadInt(model, "sequences"),
                ReadInt(model, "geosets"),
                ReadInt(model, "particles"),
                ReadInt(model, "ribbons"),
                ReadInt(model, "textures")));
        }
        return output
            .OrderBy(entry => entry.Category)
            .ThenBy(entry => entry.DisplayName, StringComparer.CurrentCulture)
            .ThenBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static War3CameraDefinition? LoadPortraitCamera(string source)
    {
        _portraitCameras ??= LoadPortraitCameras();
        return _portraitCameras.GetValueOrDefault(source);
    }

    public static string CategoryLabel(War3AssetCategory category) => category switch
    {
        War3AssetCategory.Units => "单位",
        War3AssetCategory.Heroes => "英雄",
        War3AssetCategory.Buildings => "建筑",
        War3AssetCategory.Portraits => "肖像",
        War3AssetCategory.Effects => "特效",
        War3AssetCategory.Environment => "环境",
        War3AssetCategory.Terrain => "地形",
        War3AssetCategory.Interface => "界面",
        _ => "其他"
    };

    private static Dictionary<string, DisplayEntry> LoadDisplayCatalog()
    {
        var path = AbsolutePath("catalog/display_catalog.json");
        var output = new Dictionary<string, DisplayEntry>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return output;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var source = ReadString(item, "source");
            if (source.Length == 0) continue;
            output[source] = new DisplayEntry(
                ReadString(item, "name"),
                ReadString(item, "id"),
                ReadString(item, "category"),
                ReadString(item, "race"),
                ReadString(item, "iconPath"));
        }
        return output;
    }

    private static Dictionary<string, War3CameraDefinition> LoadPortraitCameras()
    {
        var output = new Dictionary<string, War3CameraDefinition>(StringComparer.OrdinalIgnoreCase);
        var path = AbsolutePath("catalog/portrait_cameras.json");
        if (!File.Exists(path)) return output;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var source = ReadString(item, "source");
            if (source.Length == 0 || !item.TryGetProperty("cameras", out var cameras) ||
                cameras.GetArrayLength() == 0)
                continue;
            var camera = cameras[0];
            output[source] = new War3CameraDefinition(
                ReadVector(camera, "Position"),
                ReadVector(camera, "TargetPosition"),
                camera.TryGetProperty("FieldOfView", out var fieldOfView)
                    ? fieldOfView.GetSingle()
                    : Mathf.Pi / 4f,
                camera.TryGetProperty("NearClip", out var nearClip) ? nearClip.GetSingle() : 0.1f,
                camera.TryGetProperty("FarClip", out var farClip) ? farClip.GetSingle() : 5000f);
        }
        return output;
    }

    private static Vector3 ReadVector(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.GetArrayLength() < 3)
            return Vector3.Zero;
        return new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    }

    private static War3AssetCategory ParseCategory(string value, string source) => value switch
    {
        "units" or "unitModels" => War3AssetCategory.Units,
        "heroes" => War3AssetCategory.Heroes,
        "buildings" => War3AssetCategory.Buildings,
        "portraits" => War3AssetCategory.Portraits,
        "effects" => War3AssetCategory.Effects,
        "environment" => War3AssetCategory.Environment,
        "terrain" => War3AssetCategory.Terrain,
        "uiModels" => War3AssetCategory.Interface,
        _ => InferCategory(source)
    };

    private static War3AssetCategory InferCategory(string source)
    {
        var normalized = source.Replace('/', '\\');
        if (normalized.StartsWith("Abilities\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Objects\\Spawnmodels\\", StringComparison.OrdinalIgnoreCase))
            return War3AssetCategory.Effects;
        if (normalized.StartsWith("Buildings\\", StringComparison.OrdinalIgnoreCase))
            return War3AssetCategory.Buildings;
        if (normalized.StartsWith("Units\\", StringComparison.OrdinalIgnoreCase))
            return normalized.Contains("Portrait", StringComparison.OrdinalIgnoreCase)
                ? War3AssetCategory.Portraits
                : War3AssetCategory.Units;
        if (normalized.StartsWith("Doodads\\Terrain\\", StringComparison.OrdinalIgnoreCase))
            return War3AssetCategory.Terrain;
        if (normalized.StartsWith("Doodads\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Destructables\\", StringComparison.OrdinalIgnoreCase))
            return War3AssetCategory.Environment;
        if (normalized.StartsWith("UI\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Glue\\", StringComparison.OrdinalIgnoreCase))
            return War3AssetCategory.Interface;
        return War3AssetCategory.Miscellaneous;
    }

    private static string InferRace(string source)
    {
        var lower = source.Replace('/', '\\').ToLowerInvariant();
        foreach (var race in new[] { "human", "orc", "undead", "nightelf", "naga", "demon" })
        {
            if (lower.Contains($"\\{race}\\", StringComparison.Ordinal)) return race;
        }
        return "neutral";
    }

    private static string Humanize(string source)
    {
        var stem = Path.GetFileNameWithoutExtension(source.Replace('\\', '/'));
        stem = stem.Replace('_', ' ').Replace('-', ' ');
        return WordBoundary().Replace(stem, "$1 $2").Trim();
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;

    private sealed record DisplayEntry(
        string Name,
        string Id,
        string Category,
        string Race,
        string IconPath);

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}

public sealed record War3Sequence(
    string Name,
    double StartFrame,
    double EndFrame,
    bool NonLooping)
{
    public double DurationMilliseconds => Math.Max(1d, EndFrame - StartFrame);
}

public sealed record War3TextureDefinition(int ReplaceableId, string Image, int Flags);

public sealed record War3MaterialLayer(
    int FilterMode,
    War3ScalarTrack TextureId,
    War3ScalarTrack Alpha,
    int Shading);

public sealed record War3MaterialDefinition(IReadOnlyList<War3MaterialLayer> Layers);

public sealed record War3GeosetAnimation(
    int GeosetId,
    War3ScalarTrack Alpha);

public sealed record War3ParticleEmitterDefinition(
    string Name,
    int ObjectId,
    int Flags,
    War3ScalarTrack Speed,
    War3ScalarTrack Variation,
    War3ScalarTrack Latitude,
    War3ScalarTrack Gravity,
    double LifeSpan,
    War3ScalarTrack EmissionRate,
    War3ScalarTrack Width,
    War3ScalarTrack Length,
    int FilterMode,
    int Rows,
    int Columns,
    int FrameFlags,
    double TailLength,
    double Time,
    Color[] SegmentColors,
    byte[] Alpha,
    float[] Scaling,
    int[] LifeSpanUv,
    int[] DecayUv,
    int[] TailUv,
    int[] TailDecayUv,
    int TextureId,
    bool Squirt,
    int PriorityPlane,
    int ReplaceableId,
    War3ScalarTrack Visibility);

public sealed record War3RibbonEmitterDefinition(
    string Name,
    int ObjectId,
    War3ScalarTrack HeightAbove,
    War3ScalarTrack HeightBelow,
    War3ScalarTrack Alpha,
    Color Color,
    double LifeSpan,
    War3ScalarTrack TextureSlot,
    double EmissionRate,
    int Rows,
    int Columns,
    int MaterialId,
    double Gravity,
    War3ScalarTrack Visibility);

public sealed record War3ModelBounds(Vector3 Minimum, Vector3 Maximum, float Radius)
{
    public Vector3 Center => (Minimum + Maximum) * 0.5f;
    public float Size => Math.Max(
        Radius,
        Math.Max(Maximum.X - Minimum.X,
            Math.Max(Maximum.Y - Minimum.Y, Maximum.Z - Minimum.Z)));
}

public sealed record War3ModelEventObject(
    string Name,
    int ObjectId,
    IReadOnlyList<double> EventTrack)
{
    public bool TryGetSoundEventCode(out string eventCode)
    {
        eventCode = string.Empty;
        if (Name.Length < 8 || !Name.StartsWith(
                "SNDX", StringComparison.OrdinalIgnoreCase))
            return false;
        eventCode = Name.Substring(4, 4).ToUpperInvariant();
        return true;
    }
}

public sealed class War3ModelMetadata
{
    public required War3ModelBounds Bounds { get; init; }
    public required double BlendTimeMilliseconds { get; init; }
    public required IReadOnlyList<War3Sequence> Sequences { get; init; }
    public required IReadOnlyList<War3TextureDefinition> Textures { get; init; }
    public required IReadOnlyList<War3MaterialDefinition> Materials { get; init; }
    public required IReadOnlyList<War3GeosetAnimation> GeosetAnimations { get; init; }
    public required IReadOnlyList<War3ParticleEmitterDefinition> Particles { get; init; }
    public required IReadOnlyList<War3RibbonEmitterDefinition> Ribbons { get; init; }
    public required IReadOnlyList<War3ModelEventObject> EventObjects { get; init; }
    public required IReadOnlyList<double> GlobalSequences { get; init; }
    public required int LegacyParticleEmitterCount { get; init; }
    public required int EventObjectCount { get; init; }

    public static War3ModelMetadata Load(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            War3AssetPack.AbsolutePath(relativePath)));
        var root = document.RootElement;
        var globals = root.TryGetProperty("globalSequences", out var globalElement)
            ? globalElement.EnumerateArray().Select(value => value.GetDouble()).ToArray()
            : [];
        var sequences = root.GetProperty("sequences").EnumerateArray()
            .Select(ParseSequence).ToArray();
        var textures = root.GetProperty("textures").EnumerateArray()
            .Select(ParseTexture).ToArray();
        var materials = root.GetProperty("materials").EnumerateArray()
            .Select(ParseMaterial).ToArray();
        var geosetAnimations = root.TryGetProperty("geosetAnims", out var geosetElement)
            ? geosetElement.EnumerateArray().Select(ParseGeosetAnimation).ToArray()
            : [];
        var particles = root.GetProperty("particleEmitters2").EnumerateArray()
            .Select(ParseParticle).ToArray();
        var ribbons = root.GetProperty("ribbonEmitters").EnumerateArray()
            .Select(ParseRibbon).ToArray();
        var events = root.TryGetProperty("eventObjects", out var eventElement)
            ? eventElement.EnumerateArray().Select(ParseEventObject).ToArray()
            : [];
        var info = root.GetProperty("info");
        return new War3ModelMetadata
        {
            Bounds = new War3ModelBounds(
                ReadVector3(info, "MinimumExtent"),
                ReadVector3(info, "MaximumExtent"),
                ReadFloat(info, "BoundsRadius")),
            BlendTimeMilliseconds = Math.Max(0d, ReadDouble(info, "BlendTime")),
            Sequences = sequences,
            Textures = textures,
            Materials = materials,
            GeosetAnimations = geosetAnimations,
            Particles = particles,
            Ribbons = ribbons,
            EventObjects = events,
            GlobalSequences = globals,
            LegacyParticleEmitterCount = root.TryGetProperty("particleEmitters", out var legacyParticles)
                ? legacyParticles.GetArrayLength()
                : 0,
            EventObjectCount = events.Length
        };
    }

    private static War3Sequence ParseSequence(JsonElement element)
    {
        var interval = element.GetProperty("Interval").EnumerateArray()
            .Select(value => value.GetDouble()).ToArray();
        return new War3Sequence(
            ReadString(element, "Name", "Static"),
            interval.ElementAtOrDefault(0),
            interval.ElementAtOrDefault(1),
            ReadBool(element, "NonLooping"));
    }

    private static War3TextureDefinition ParseTexture(JsonElement element) => new(
        ReadInt(element, "ReplaceableId"),
        ReadString(element, "Image", string.Empty),
        ReadInt(element, "Flags"));

    private static War3MaterialDefinition ParseMaterial(JsonElement element)
    {
        var layers = element.TryGetProperty("Layers", out var value)
            ? value.EnumerateArray().Select(layer => new War3MaterialLayer(
                ReadInt(layer, "FilterMode"),
                War3ScalarTrack.Parse(layer, "TextureID", 0d),
                War3ScalarTrack.Parse(layer, "Alpha", 1d),
                ReadInt(layer, "Shading"))).ToArray()
            : [];
        return new War3MaterialDefinition(layers);
    }

    private static War3GeosetAnimation ParseGeosetAnimation(JsonElement element) => new(
        ReadInt(element, "GeosetId", -1),
        War3ScalarTrack.Parse(element, "Alpha", 1d));

    private static War3ParticleEmitterDefinition ParseParticle(JsonElement element) => new(
        ReadString(element, "Name", "Particle"),
        ReadInt(element, "ObjectId"),
        ReadInt(element, "Flags"),
        War3ScalarTrack.Parse(element, "Speed"),
        War3ScalarTrack.Parse(element, "Variation"),
        War3ScalarTrack.Parse(element, "Latitude"),
        War3ScalarTrack.Parse(element, "Gravity"),
        ReadDouble(element, "LifeSpan", 1d),
        War3ScalarTrack.Parse(element, "EmissionRate"),
        War3ScalarTrack.Parse(element, "Width"),
        War3ScalarTrack.Parse(element, "Length"),
        ReadInt(element, "FilterMode"),
        Math.Max(1, ReadInt(element, "Rows", 1)),
        Math.Max(1, ReadInt(element, "Columns", 1)),
        ReadInt(element, "FrameFlags", 1),
        ReadDouble(element, "TailLength"),
        ReadDouble(element, "Time", 0.5d),
        ReadColors(element, "SegmentColor", [Colors.White, Colors.White, Colors.White]),
        ReadBytes(element, "Alpha", [255, 255, 0]),
        ReadFloats(element, "ParticleScaling", [1f, 1f, 1f]),
        ReadInts(element, "LifeSpanUVAnim", [0, 0, 1]),
        ReadInts(element, "DecayUVAnim", [0, 0, 1]),
        ReadInts(element, "TailUVAnim", [0, 0, 1]),
        ReadInts(element, "TailDecayUVAnim", [0, 0, 1]),
        ReadInt(element, "TextureID"),
        ReadBool(element, "Squirt"),
        ReadInt(element, "PriorityPlane"),
        ReadInt(element, "ReplaceableId"),
        War3ScalarTrack.Parse(element, "Visibility", 1d));

    private static War3RibbonEmitterDefinition ParseRibbon(JsonElement element) => new(
        ReadString(element, "Name", "Ribbon"),
        ReadInt(element, "ObjectId"),
        War3ScalarTrack.Parse(element, "HeightAbove"),
        War3ScalarTrack.Parse(element, "HeightBelow"),
        War3ScalarTrack.Parse(element, "Alpha", 1d),
        ReadColor(element, "Color", Colors.White),
        ReadDouble(element, "LifeSpan", 1d),
        War3ScalarTrack.Parse(element, "TextureSlot"),
        ReadDouble(element, "EmissionRate", 30d),
        Math.Max(1, ReadInt(element, "Rows", 1)),
        Math.Max(1, ReadInt(element, "Columns", 1)),
        ReadInt(element, "MaterialID"),
        ReadDouble(element, "Gravity"),
        War3ScalarTrack.Parse(element, "Visibility"));

    private static War3ModelEventObject ParseEventObject(JsonElement element) => new(
        ReadString(element, "Name", string.Empty),
        ReadInt(element, "ObjectId", -1),
        ReadDoubles(element, "EventTrack"));

    private static Color[] ReadColors(JsonElement element, string property, Color[] fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(ReadColor).ToArray()
            : fallback;

    private static Color ReadColor(JsonElement value) => new(
        value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), 1f);

    private static Color ReadColor(JsonElement element, string property, Color fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? ReadColor(value)
            : fallback;

    private static Vector3 ReadVector3(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.GetArrayLength() < 3)
            return Vector3.Zero;
        return new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    }

    private static float[] ReadFloats(JsonElement element, string property, float[] fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetSingle()).ToArray()
            : fallback;

    private static double[] ReadDoubles(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetDouble()).ToArray()
            : [];

    private static int[] ReadInts(JsonElement element, string property, int[] fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetInt32()).ToArray()
            : fallback;

    private static byte[] ReadBytes(JsonElement element, string property, byte[] fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => (byte)Math.Clamp(item.GetInt32(), 0, 255)).ToArray()
            : fallback;

    private static string ReadString(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int ReadInt(JsonElement element, string property, int fallback = 0) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;

    private static float ReadFloat(JsonElement element, string property, float fallback = 0f) =>
        element.TryGetProperty(property, out var value) && value.TryGetSingle(out var result)
            ? result
            : fallback;

    private static double ReadDouble(JsonElement element, string property, double fallback = 0d) =>
        element.TryGetProperty(property, out var value) && value.TryGetDouble(out var result)
            ? result
            : fallback;

    private static bool ReadBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}

public sealed class War3ScalarTrack
{
    public sealed record Key(double Frame, double Value, double? InTangent, double? OutTangent);

    public double Constant { get; }
    public int LineType { get; }
    public int? GlobalSequenceId { get; }
    public IReadOnlyList<Key> Keys { get; }
    public double Maximum => Keys.Count == 0 ? Constant : Math.Max(Constant, Keys.Max(key => key.Value));

    private War3ScalarTrack(
        double constant,
        int lineType = 0,
        int? globalSequenceId = null,
        IReadOnlyList<Key>? keys = null)
    {
        Constant = constant;
        LineType = lineType;
        GlobalSequenceId = globalSequenceId;
        Keys = keys ?? [];
    }

    public static War3ScalarTrack Parse(
        JsonElement owner,
        string property,
        double fallback = 0d)
    {
        if (!owner.TryGetProperty(property, out var value)) return new War3ScalarTrack(fallback);
        if (value.ValueKind == JsonValueKind.Number) return new War3ScalarTrack(value.GetDouble());
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("Keys", out var keyArray))
            return new War3ScalarTrack(fallback);
        var keys = keyArray.EnumerateArray().Select(key => new Key(
            key.GetProperty("Frame").GetDouble(),
            First(key, "Vector", fallback),
            OptionalFirst(key, "InTan"),
            OptionalFirst(key, "OutTan"))).ToArray();
        int? global = null;
        if (value.TryGetProperty("GlobalSeqId", out var globalValue) &&
            globalValue.ValueKind == JsonValueKind.Number)
            global = globalValue.GetInt32();
        return new War3ScalarTrack(
            fallback,
            value.TryGetProperty("LineType", out var line) ? line.GetInt32() : 0,
            global,
            keys);
    }

    public double Sample(
        double frame,
        War3Sequence sequence,
        IReadOnlyList<double> globalSequences)
    {
        if (Keys.Count == 0) return Constant;
        var localFrame = frame;
        var globalId = GlobalSequenceId ?? -1;
        var global = globalId >= 0 && globalId < globalSequences.Count;
        if (global)
        {
            var duration = Math.Max(1d, globalSequences[globalId]);
            localFrame = ((frame % duration) + duration) % duration;
        }

        var firstIndex = -1;
        var lastIndex = -1;
        for (var index = 0; index < Keys.Count; index++)
        {
            var key = Keys[index];
            if (!global && (key.Frame < sequence.StartFrame ||
                            key.Frame > sequence.EndFrame))
                continue;
            if (firstIndex < 0) firstIndex = index;
            lastIndex = index;
        }
        // Warcraft scopes non-global tracks to a sequence. Missing keys do not
        // inherit a value from Birth, Upgrade, Death, or Decay. Keep the exact
        // source-order semantics while avoiding the per-sample LINQ array.
        if (firstIndex < 0) return Constant;
        var first = Keys[firstIndex];
        var last = Keys[lastIndex];
        if (localFrame <= first.Frame) return first.Value;
        if (localFrame >= last.Frame) return last.Value;

        var leftIndex = firstIndex;
        var rightIndex = -1;
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var key = Keys[index];
            if (!global && (key.Frame < sequence.StartFrame ||
                            key.Frame > sequence.EndFrame))
                continue;
            if (key.Frame >= localFrame)
            {
                rightIndex = index;
                break;
            }
            leftIndex = index;
        }
        if (rightIndex < 0) return last.Value;
        var left = Keys[leftIndex];
        var right = Keys[rightIndex];
        if (LineType == 0) return left.Value;
        var t = Math.Clamp((localFrame - left.Frame) /
                           Math.Max(1d, right.Frame - left.Frame), 0d, 1d);
        return LineType switch
        {
            2 when left.OutTangent.HasValue && right.InTangent.HasValue =>
                Hermite(left.Value, left.OutTangent.Value,
                    right.InTangent.Value, right.Value, t),
            3 when left.OutTangent.HasValue && right.InTangent.HasValue =>
                Bezier(left.Value, left.OutTangent.Value,
                    right.InTangent.Value, right.Value, t),
            _ => left.Value + (right.Value - left.Value) * t
        };
    }

    public IReadOnlyList<Key> KeysFor(War3Sequence sequence) => Keys
        .Where(key => key.Frame >= sequence.StartFrame && key.Frame <= sequence.EndFrame)
        .ToArray();

    private static double First(JsonElement owner, string property, double fallback) =>
        owner.TryGetProperty(property, out var value) && value.GetArrayLength() > 0
            ? value[0].GetDouble()
            : fallback;

    private static double? OptionalFirst(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) && value.GetArrayLength() > 0
            ? value[0].GetDouble()
            : null;

    private static double Hermite(double p0, double m0, double m1, double p1, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return (2d * t3 - 3d * t2 + 1d) * p0 + (t3 - 2d * t2 + t) * m0 +
               (-2d * t3 + 3d * t2) * p1 + (t3 - t2) * m1;
    }

    private static double Bezier(double p0, double c0, double c1, double p1, double t)
    {
        var inverse = 1d - t;
        return inverse * inverse * inverse * p0 + 3d * inverse * inverse * t * c0 +
               3d * inverse * t * t * c1 + t * t * t * p1;
    }
}
