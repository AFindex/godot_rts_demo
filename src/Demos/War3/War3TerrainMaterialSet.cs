using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;

namespace RtsDemo.Demos.War3;

/// <summary>
/// Warcraft III classic presentation theme for the engine-independent RTS
/// terrain. Terrain.slk identities are mapped to the project's semantic
/// surface keys; pathing, height, buildability and vision remain untouched.
/// </summary>
public sealed class War3TerrainMaterialSet :
    IRts3DTerrainBlendMaterialProvider,
    IRts3DTerrainDualGridMaterialProvider,
    IRts3DTerrainClassicCliffProvider
{
    private const string TextureRoot = "textures/";
    private const string LordaeronRoot =
        TextureRoot + "terrainart/lordaeronsummer/";
    private const string ShaderRoot = "res://war3_rts/terrain/shaders/";

    private static readonly IReadOnlyDictionary<string, string> SurfaceTextures =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["badlands"] = LordaeronRoot + "lords_grass.png",
            ["sand"] = LordaeronRoot + "lords_dirt.png",
            ["mud"] = LordaeronRoot + "lords_dirtgrass.png",
            ["rock"] = LordaeronRoot + "lords_rock.png",
            ["metal"] = LordaeronRoot + "lords_dirtrough.png",
            ["vision-smoke"] = LordaeronRoot + "lords_grassdark.png"
        };

    private static readonly IReadOnlyDictionary<string, int> BlendChannels =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // Lordaeron Summer keeps dirt at the bottom and grass above it,
            // matching the classic tileset order used for alpha-over layers.
            ["sand"] = 0,
            ["metal"] = 1,
            ["rock"] = 2,
            ["badlands"] = 3
        };

    private static readonly string[] RequiredPaths =
    [
        ShaderRoot + "War3Ground.gdshader",
        ShaderRoot + "War3GroundBlend.gdshader",
        ShaderRoot + "War3GroundDualGrid.gdshader",
        ShaderRoot + "War3Cliff.gdshader",
        ShaderRoot + "War3Water.gdshader",
        .. SurfaceTextures.Values.Distinct(StringComparer.OrdinalIgnoreCase),
        TextureRoot + "replaceabletextures/cliff/cliff0.png",
        TextureRoot + "replaceabletextures/cliff/cliff1.png",
        TextureRoot + "replaceabletextures/water/water00.png",
        "models/doodads/terrain/cliffs/cliffsaaab0.glb"
    ];

    private readonly Dictionary<string, Material> _surfaceMaterials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Shader _groundShader;
    private readonly Shader _groundBlendShader;
    private readonly Shader _groundDualGridShader;
    private readonly Shader _cliffShader;
    private readonly Shader _waterShader;
    private readonly Texture2D _waterTexture;
    private readonly Dictionary<string, Material> _cliffMaterials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Material> _classicCliffMaterials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly War3ClassicCliffMeshCatalog _classicCliffs;
    private Material? _blendedSurfaceMaterial;
    private Material? _dualGridSurfaceMaterial;
    private readonly bool _classicCliffMeshesEnabled;

    public War3TerrainMaterialSet(
        War3TerrainBlendStyle blendStyle = War3TerrainBlendStyle.DualGrid,
        bool classicCliffMeshesEnabled = true)
    {
        BlendStyle = blendStyle;
        _classicCliffMeshesEnabled = classicCliffMeshesEnabled;
        var missing = MissingAssetPaths();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "War3 terrain assets are incomplete: " +
                string.Join(", ", missing));
        }
        _groundShader = LoadRequired<Shader>(
            ShaderRoot + "War3Ground.gdshader");
        _groundBlendShader = LoadRequired<Shader>(
            ShaderRoot + "War3GroundBlend.gdshader");
        _groundDualGridShader = LoadRequired<Shader>(
            ShaderRoot + "War3GroundDualGrid.gdshader");
        _cliffShader = LoadRequired<Shader>(
            ShaderRoot + "War3Cliff.gdshader");
        _waterShader = LoadRequired<Shader>(
            ShaderRoot + "War3Water.gdshader");
        _waterTexture = LoadTexture(
            TextureRoot + "replaceabletextures/water/water00.png");
        _classicCliffs = War3ClassicCliffMeshCatalog.LoadDefault();
    }

    public War3TerrainBlendStyle BlendStyle { get; }
    public bool DualGridEnabled =>
        BlendStyle == War3TerrainBlendStyle.DualGrid;
    public bool ClassicCliffMeshesEnabled =>
        _classicCliffMeshesEnabled && _classicCliffs.SignatureCount > 0;
    public int ClassicCliffSignatureCount => _classicCliffs.SignatureCount;
    public int ClassicCliffAssetCount => _classicCliffs.AssetCount;

    public static IReadOnlyList<string> AssetPaths => RequiredPaths;

    public static string[] MissingAssetPaths() => RequiredPaths
        .Where(path => path.StartsWith("res://", StringComparison.Ordinal)
            ? !ResourceLoader.Exists(path)
            : !File.Exists(War3AssetPack.AbsolutePath(path)))
        .ToArray();

    public Material SurfaceMaterial(TerrainSurfaceDefinition surface)
    {
        if (_surfaceMaterials.TryGetValue(surface.MaterialKey, out var material))
            return material;
        material = surface.MaterialKey switch
        {
            "shallow-water" => CreateWaterMaterial(deep: false),
            "deep-water" => CreateWaterMaterial(deep: true),
            _ => CreateGroundMaterial(surface.MaterialKey)
        };
        _surfaceMaterials.Add(surface.MaterialKey, material);
        return material;
    }

    public Material BlendedSurfaceMaterial() =>
        _blendedSurfaceMaterial ??= CreateBlendedSurfaceMaterial();

    public bool TryGetBlendChannel(
        TerrainSurfaceDefinition surface,
        out int channel) =>
        BlendChannels.TryGetValue(surface.MaterialKey, out channel);

    public Material DualGridSurfaceMaterial() =>
        _dualGridSurfaceMaterial ??= CreateLayeredSurfaceMaterial(
            _groundDualGridShader);

    public bool TryGetDualGridLayer(
        TerrainSurfaceDefinition surface,
        out int layer) =>
        BlendChannels.TryGetValue(surface.MaterialKey, out layer);

    public int ClassicCliffVariationCount(string signature) =>
        _classicCliffs.VariationCount(signature);

    public bool TryGetClassicCliffMesh(
        string signature,
        int variation,
        out Rts3DClassicCliffMesh definition) =>
        _classicCliffs.TryGet(signature, variation, out definition);

    public Material CliffMaterial(TerrainSurfaceDefinition upperSurface)
        => CliffMaterial(upperSurface, classicModelUv: false);

    public Material ClassicCliffMaterial(
        TerrainSurfaceDefinition upperSurface)
        => CliffMaterial(upperSurface, classicModelUv: true);

    public bool TryGetClassicCliffGroundLayer(
        TerrainSurfaceDefinition upperSurface,
        out int layer)
    {
        // Lordaeron Summer Cliff0 declares Ldrt (dirt) as its ground tile;
        // Cliff1 declares Lgrs (grass). This is the same priority texture W3
        // applies to tilepoints neighbouring a cliff model.
        var groundKey = ResolveCliffName(upperSurface) == "cliff1"
            ? "badlands"
            : "sand";
        return BlendChannels.TryGetValue(groundKey, out layer);
    }

    private Material CliffMaterial(
        TerrainSurfaceDefinition upperSurface,
        bool classicModelUv)
    {
        var cliffName = ResolveCliffName(upperSurface);
        var cache = classicModelUv
            ? _classicCliffMaterials
            : _cliffMaterials;
        if (cache.TryGetValue(cliffName, out var material))
            return material;
        material = new ShaderMaterial
        {
            Shader = _cliffShader
        }.WithParameter(
            "cliff_atlas",
            LoadTexture(
                TextureRoot + $"replaceabletextures/cliff/{cliffName}.png"))
         .WithParameter("use_model_atlas_uv", classicModelUv);
        cache.Add(cliffName, material);
        return material;
    }

    private static string ResolveCliffName(
        TerrainSurfaceDefinition upperSurface) =>
        upperSurface.MaterialKey is "badlands" or "mud" or "vision-smoke"
            ? "cliff1"
            : "cliff0";

    private Material CreateBlendedSurfaceMaterial()
        => CreateLayeredSurfaceMaterial(_groundBlendShader);

    private Material CreateLayeredSurfaceMaterial(Shader shader)
    {
        var material = new ShaderMaterial { Shader = shader };
        foreach (var (key, channel) in BlendChannels)
        {
            var uniform = channel switch
            {
                0 => "terrain_r",
                1 => "terrain_g",
                2 => "terrain_b",
                _ => "terrain_a"
            };
            material.SetShaderParameter(
                uniform,
                LoadTexture(SurfaceTextures[key]));
        }
        return material;
    }

    private Material CreateGroundMaterial(string key)
    {
        var path = SurfaceTextures.TryGetValue(key, out var mapped)
            ? mapped
            : LordaeronRoot + "lords_grass.png";
        return new ShaderMaterial
        {
            Shader = _groundShader
        }.WithParameter("terrain_atlas", LoadTexture(path));
    }

    private Material CreateWaterMaterial(bool deep)
    {
        var material = new ShaderMaterial { Shader = _waterShader };
        material.SetShaderParameter("water_texture", _waterTexture);
        material.SetShaderParameter("depth_tint", deep ? 0.72f : 0.08f);
        return material;
    }

    private static T LoadRequired<T>(string path) where T : Resource
    {
        var resource = GD.Load<T>(path);
        return resource ?? throw new InvalidOperationException(
            $"Required War3 terrain resource could not be loaded: {path}");
    }

    private static Texture2D LoadTexture(string relativePath)
    {
        var absolute = War3AssetPack.AbsolutePath(relativePath);
        var image = Image.LoadFromFile(absolute);
        if (image is null || image.IsEmpty())
            throw new InvalidOperationException(
                $"Required War3 terrain texture could not be loaded: {absolute}");
        return ImageTexture.CreateFromImage(image);
    }
}

public enum War3TerrainBlendStyle
{
    DualGrid,
    ContinuousWeights
}

internal static class War3ShaderMaterialExtensions
{
    public static ShaderMaterial WithParameter(
        this ShaderMaterial material,
        StringName name,
        Variant value)
    {
        material.SetShaderParameter(name, value);
        return material;
    }
}
