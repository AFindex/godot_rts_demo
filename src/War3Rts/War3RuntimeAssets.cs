using System.Text.Json;
using Godot;
using RtsDemo.Demos.War3;

namespace War3Rts;

/// <summary>
/// Direct-load cache for the external Warcraft pack. The pack remains under
/// .gdignore, so Godot never creates a second multi-gigabyte import cache.
/// </summary>
public static class War3RuntimeAssets
{
    private static readonly Lazy<IReadOnlyDictionary<string, War3AssetEntry>> Entries =
        new(BuildEntryIndex);
    private static readonly Dictionary<string, PackedScene> SceneTemplates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IReadOnlyDictionary<string, MaterialDescriptor>>
        MaterialDescriptors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, War3ModelMetadata> Metadata =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture2D?> Textures =
        new(StringComparer.OrdinalIgnoreCase);

    public static War3AssetEntry? Find(string source) =>
        Entries.Value.GetValueOrDefault(NormalizeSource(source));

    public static bool Contains(string source)
    {
        var entry = Find(source);
        return entry is not null &&
               File.Exists(War3AssetPack.AbsolutePath(entry.ModelRelativePath)) &&
               File.Exists(War3AssetPack.AbsolutePath(entry.MetadataRelativePath));
    }

    public static Node InstantiateModel(string source, int teamColor = 0)
    {
        var entry = Find(source) ?? throw new FileNotFoundException(
            $"Warcraft asset is not present in the export catalog: {source}");
        var key = NormalizeSource(entry.Source);
        var template = GetOrBuildTemplate(entry, key);
        var instance = template.Instantiate();
        ApplyWar3MeshMaterials(instance, MaterialDescriptors[key], teamColor);
        return instance;
    }

    public static void PreloadModelTemplate(string source)
    {
        var entry = Find(source) ?? throw new FileNotFoundException(
            $"Warcraft asset is not present in the export catalog: {source}");
        var key = NormalizeSource(entry.Source);
        _ = GetOrBuildTemplate(entry, key);
        _ = LoadMetadata(source);
    }

    private static PackedScene GetOrBuildTemplate(
        War3AssetEntry entry,
        string key)
    {
        if (SceneTemplates.TryGetValue(key, out var template)) return template;
        var descriptors = ReadGlbMaterialDescriptors(
            War3AssetPack.AbsolutePath(entry.ModelRelativePath));
        MaterialDescriptors.Add(key, descriptors);
        template = BuildTemplate(entry, descriptors);
        SceneTemplates.Add(key, template);
        return template;
    }

    public static War3ModelMetadata LoadMetadata(string source)
    {
        var entry = Find(source) ?? throw new FileNotFoundException(
            $"Warcraft metadata is not present in the export catalog: {source}");
        var key = NormalizeSource(entry.Source);
        if (!Metadata.TryGetValue(key, out var metadata))
        {
            metadata = War3ModelMetadata.Load(entry.MetadataRelativePath);
            Metadata.Add(key, metadata);
        }
        return metadata;
    }

    public static Texture2D? LoadTexture(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath)) return null;
        var normalized = virtualPath.Replace('\\', '/').TrimStart('/');
        var extension = Path.GetExtension(normalized);
        if (extension.Length == 0 || extension.Equals(".blp", StringComparison.OrdinalIgnoreCase))
            normalized = Path.ChangeExtension(normalized, ".png");
        normalized = normalized.ToLowerInvariant();
        if (Textures.TryGetValue(normalized, out var cached)) return cached;
        var absolute = War3AssetPack.AbsolutePath($"textures/{normalized}");
        if (!File.Exists(absolute))
        {
            Textures[normalized] = null;
            return null;
        }
        try
        {
            var image = Image.LoadFromFile(absolute);
            var texture = image is null || image.IsEmpty()
                ? null
                : ImageTexture.CreateFromImage(image);
            Textures[normalized] = texture;
            return texture;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Warcraft texture failed: {virtualPath} ({exception.Message})");
            Textures[normalized] = null;
            return null;
        }
    }

    private static IReadOnlyDictionary<string, War3AssetEntry> BuildEntryIndex() =>
        War3AssetPack.LoadCatalog()
            .GroupBy(value => NormalizeSource(value.Source), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);

    private static PackedScene BuildTemplate(
        War3AssetEntry entry,
        IReadOnlyDictionary<string, MaterialDescriptor> descriptors)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        var modelPath = War3AssetPack.AbsolutePath(entry.ModelRelativePath);
        var error = document.AppendFromFile(modelPath, state);
        if (error != Error.Ok)
            throw new InvalidOperationException($"glTF load failed ({entry.Source}): {error}");
        var generated = document.GenerateScene(state) ??
                        throw new InvalidOperationException(
                            $"glTF did not generate a scene: {entry.Source}");
        generated.Name = Path.GetFileNameWithoutExtension(entry.Source.Replace('\\', '/'));
        ApplyWar3MeshMaterials(generated, descriptors, 0);
        var template = new PackedScene();
        var packed = template.Pack(generated);
        generated.Free();
        if (packed != Error.Ok)
            throw new InvalidOperationException(
                $"glTF scene cache failed ({entry.Source}): {packed}");
        return template;
    }

    private static IReadOnlyDictionary<string, MaterialDescriptor>
        ReadGlbMaterialDescriptors(string glbPath)
    {
        var output = new Dictionary<string, MaterialDescriptor>(
            StringComparer.OrdinalIgnoreCase);
        var bytes = File.ReadAllBytes(glbPath);
        if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546c67)
            return output;
        var jsonLength = BitConverter.ToInt32(bytes, 12);
        if (jsonLength <= 0 || 20 + jsonLength > bytes.Length) return output;
        using var document = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
        if (!document.RootElement.TryGetProperty("materials", out var materials))
            return output;
        foreach (var material in materials.EnumerateArray())
        {
            if (!material.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                !material.TryGetProperty("extras", out var extras))
                continue;
            var name = nameElement.GetString();
            var filterMode = extras.TryGetProperty("war3FilterMode", out var filterElement) &&
                             filterElement.TryGetInt32(out var filter)
                ? filter
                : 0;
            var replaceableId = extras.TryGetProperty("replaceableId", out var replaceableElement) &&
                                replaceableElement.TryGetInt32(out var replaceable)
                ? replaceable
                : 0;
            var shading = extras.TryGetProperty("war3Shading", out var shadingElement) &&
                          shadingElement.TryGetInt32(out var shadingFlags)
                ? shadingFlags
                : 0;
            if (!string.IsNullOrWhiteSpace(name))
                output[name] = new MaterialDescriptor(
                    filterMode, replaceableId, shading);
        }
        return output;
    }

    private static void ApplyWar3MeshMaterials(
        Node root,
        IReadOnlyDictionary<string, MaterialDescriptor> descriptors,
        int teamColor)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D meshInstance && meshInstance.Mesh is not null)
            {
                for (var surface = 0; surface < meshInstance.Mesh.GetSurfaceCount(); surface++)
                {
                    var source = meshInstance.GetActiveMaterial(surface) as StandardMaterial3D;
                    if (source is null ||
                        !descriptors.TryGetValue(source.ResourceName, out var descriptor))
                        continue;
                    var material = (StandardMaterial3D)source.Duplicate();
                    material.ResourceName = source.ResourceName;
                    var explicitlyUnshaded = (descriptor.Shading & 1) != 0;
                    material.ShadingMode = !explicitlyUnshaded &&
                                           descriptor.FilterMode is 0 or 1 or 2
                        ? BaseMaterial3D.ShadingModeEnum.PerPixel
                        : BaseMaterial3D.ShadingModeEnum.Unshaded;
                    material.Metallic = 0f;
                    material.Roughness = 0.9f;
                    if (descriptor.FilterMode >= 2)
                    {
                        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                        material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
                    }
                    material.BlendMode = descriptor.FilterMode switch
                    {
                        3 or 4 => BaseMaterial3D.BlendModeEnum.Add,
                        5 or 6 => BaseMaterial3D.BlendModeEnum.Mul,
                        _ => BaseMaterial3D.BlendModeEnum.Mix
                    };
                    ApplyReplaceableTexture(material, descriptor.ReplaceableId, teamColor);
                    meshInstance.SetSurfaceOverrideMaterial(surface, material);
                }
            }
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
    }

    private static void ApplyReplaceableTexture(
        StandardMaterial3D material,
        int replaceableId,
        int teamColor)
    {
        var texturePath = replaceableId switch
        {
            1 => $"ReplaceableTextures/TeamColor/TeamColor{Math.Clamp(teamColor, 0, 14):00}.png",
            2 => $"ReplaceableTextures/TeamGlow/TeamGlow{Math.Clamp(teamColor, 0, 14):00}.png",
            31 => "ReplaceableTextures/LordaeronTree/LordaeronSummerTree.png",
            _ => string.Empty
        };
        if (texturePath.Length == 0) return;
        var texture = LoadTexture(texturePath);
        if (texture is null)
        {
            GD.PushWarning($"Warcraft replaceable texture is missing: {texturePath}");
            return;
        }
        material.AlbedoTexture = texture;
        material.AlbedoColor = Colors.White;
        if (replaceableId == 31)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }
    }

    private sealed record MaterialDescriptor(
        int FilterMode,
        int ReplaceableId,
        int Shading);

    private static string NormalizeSource(string source) =>
        source.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
}
