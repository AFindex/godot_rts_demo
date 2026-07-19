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
        ShaderRoot + "War3CliffReveal.gdshader",
        ShaderRoot + "War3CliffTransition.gdshader",
        ShaderRoot + "War3Water.gdshader",
        ShaderRoot + "War3Fog.gdshaderinc",
        .. SurfaceTextures.Values.Distinct(StringComparer.OrdinalIgnoreCase),
        TextureRoot + "replaceabletextures/cliff/cliff0.png",
        TextureRoot + "replaceabletextures/cliff/cliff1.png",
        TextureRoot + "replaceabletextures/water/water00.png",
        "models/doodads/terrain/cliffs/cliffsaaab0.glb",
        "models/doodads/terrain/clifftrans/clifftransaahl0.glb"
    ];

    private readonly Dictionary<string, Material> _surfaceMaterials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Shader _groundShader;
    private readonly Shader _groundBlendShader;
    private readonly Shader _groundDualGridShader;
    private readonly Shader _cliffShader;
    private readonly Shader _cliffRevealShader;
    private readonly Shader _cliffTransitionShader;
    private readonly Shader _waterShader;
    private readonly Texture2D _waterTexture;
    private readonly Dictionary<string, Material> _cliffMaterials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Material> _classicCliffMaterials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Material> _classicCliffRevealMaterials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Material> _classicRampMaterials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ShaderMaterial> _shaderMaterials = [];
    private readonly War3ClassicCliffMeshCatalog _classicCliffs;
    private readonly War3ClassicRampMeshCatalog _classicRamps;
    private Material? _blendedSurfaceMaterial;
    private Material? _dualGridSurfaceMaterial;
    private readonly bool _classicCliffMeshesEnabled;
    private Texture2D? _fineHeightTexture;
    private Vector2 _fineHeightOrigin;
    private Vector2 _fineHeightMapSize = Vector2.One;
    private float _fineHeightCellWorldSize = 1f;
    private bool _useFineHeight;
    private bool _fogEnabled;
    private Texture2D? _fogTexture;
    private Vector2 _fogWorldOrigin;
    private Vector2 _fogWorldSize = Vector2.One;

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
        _cliffRevealShader = LoadRequired<Shader>(
            ShaderRoot + "War3CliffReveal.gdshader");
        _cliffTransitionShader = LoadRequired<Shader>(
            ShaderRoot + "War3CliffTransition.gdshader");
        _waterShader = LoadRequired<Shader>(
            ShaderRoot + "War3Water.gdshader");
        _waterTexture = LoadTexture(
            TextureRoot + "replaceabletextures/water/water00.png");
        _classicCliffs = War3ClassicCliffMeshCatalog.LoadDefault();
        _classicRamps = War3ClassicRampMeshCatalog.LoadDefault();
    }

    public War3TerrainBlendStyle BlendStyle { get; }
    public bool DualGridEnabled =>
        BlendStyle == War3TerrainBlendStyle.DualGrid;
    public bool ClassicCliffMeshesEnabled =>
        _classicCliffMeshesEnabled && _classicCliffs.SignatureCount > 0;
    public int ClassicCliffSignatureCount => _classicCliffs.SignatureCount;
    public int ClassicCliffAssetCount => _classicCliffs.AssetCount;
    public int ClassicRampAssetCount => _classicRamps.AssetCount;
    public byte DefaultClassicCliffStyle => 0;

    public void ConfigureFogOfWar(
        Texture2D texture,
        Vector2 worldOrigin,
        Vector2 worldSize)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (worldSize.X <= 0f || worldSize.Y <= 0f)
            throw new ArgumentOutOfRangeException(nameof(worldSize));
        _fogEnabled = true;
        _fogTexture = texture;
        _fogWorldOrigin = worldOrigin;
        _fogWorldSize = worldSize;
        foreach (var material in _shaderMaterials)
            ConfigureFogMaterial(material);
    }

    public void ConfigureClassicHeightField(TerrainMapSnapshot terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        _useFineHeight = terrain.HasFineHeight;
        var origin = SimPlane3DTransform.ToWorld(terrain.Bounds.Min);
        _fineHeightOrigin = new Vector2(origin.X, origin.Z);
        _fineHeightMapSize = new Vector2(
            terrain.HeightPointColumns, terrain.HeightPointRows);
        _fineHeightCellWorldSize =
            SimPlane3DTransform.ToWorldLength(terrain.CellSize);
        if (!_useFineHeight)
        {
            _fineHeightTexture = null;
            return;
        }

        var image = Image.CreateEmpty(
            terrain.HeightPointColumns,
            terrain.HeightPointRows,
            false,
            Image.Format.Rf);
        for (var row = 0; row < terrain.HeightPointRows; row++)
        for (var column = 0; column < terrain.HeightPointColumns; column++)
        {
            image.SetPixel(
                column,
                row,
                new Color(
                    SimPlane3DTransform.ToWorldLength(
                        terrain.FineHeightPoint(column, row)),
                    0f,
                    0f));
        }
        _fineHeightTexture = ImageTexture.CreateFromImage(image);
    }

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

    public bool HasClassicRampMesh(string signature) =>
        _classicRamps.Contains(signature);

    public bool TryGetClassicRampMesh(
        string signature,
        out Rts3DClassicCliffMesh definition) =>
        _classicRamps.TryGet(signature, out definition);

    public Material CliffMaterial(TerrainSurfaceDefinition upperSurface)
        => CliffMaterial(upperSurface, classicModelUv: false);

    public Material ClassicCliffMaterial(byte cliffStyle) =>
        CliffMaterial(CliffName(cliffStyle), classicModelUv: true);

    public Material ClassicCliffRevealMaterial(byte cliffStyle)
    {
        var cliffName = CliffName(cliffStyle);
        if (_classicCliffRevealMaterials.TryGetValue(
                cliffName, out var material))
        {
            return material;
        }
        material = Track(new ShaderMaterial
        {
            Shader = _cliffRevealShader
        }).WithParameter(
            "cliff_atlas",
            LoadTexture(
                TextureRoot + $"replaceabletextures/cliff/{cliffName}.png"))
         .WithParameter("terrain_reveal_inner_height", 17f);
        ConfigureFineHeightMaterial((ShaderMaterial)material);
        _classicCliffRevealMaterials.Add(cliffName, material);
        return material;
    }

    public Material ClassicRampMaterial(byte cliffStyle)
    {
        var cliffName = CliffName(cliffStyle);
        if (_classicRampMaterials.TryGetValue(cliffName, out var material))
            return material;
        material = Track(new ShaderMaterial
        {
            Shader = _cliffTransitionShader
        }).WithParameter(
            "cliff_atlas",
            LoadTexture(
                TextureRoot + $"replaceabletextures/cliff/{cliffName}.png"));
        ConfigureFineHeightMaterial((ShaderMaterial)material);
        _classicRampMaterials.Add(cliffName, material);
        return material;
    }

    public bool TryGetClassicCliffGroundLayer(
        byte cliffStyle,
        out int layer)
    {
        // Lordaeron Summer Cliff0 declares Ldrt (dirt) as its ground tile;
        // Cliff1 declares Lgrs (grass). This is the same priority texture W3
        // applies to tilepoints neighbouring a cliff model.
        var groundKey = CliffName(cliffStyle) == "cliff1"
            ? "badlands"
            : "sand";
        return BlendChannels.TryGetValue(groundKey, out layer);
    }

    private Material CliffMaterial(
        TerrainSurfaceDefinition upperSurface,
        bool classicModelUv) =>
        CliffMaterial(ResolveCliffName(upperSurface), classicModelUv);

    private Material CliffMaterial(
        string cliffName,
        bool classicModelUv)
    {
        var cache = classicModelUv
            ? _classicCliffMaterials
            : _cliffMaterials;
        if (cache.TryGetValue(cliffName, out var material))
            return material;
        material = Track(new ShaderMaterial
        {
            Shader = _cliffShader
        }).WithParameter(
            "cliff_atlas",
            LoadTexture(
                TextureRoot + $"replaceabletextures/cliff/{cliffName}.png"))
         .WithParameter("use_model_atlas_uv", classicModelUv);
        ConfigureFineHeightMaterial((ShaderMaterial)material);
        cache.Add(cliffName, material);
        return material;
    }

    private void ConfigureFineHeightMaterial(ShaderMaterial material)
    {
        material.SetShaderParameter("use_fine_height", _useFineHeight);
        material.SetShaderParameter("fine_height_origin", _fineHeightOrigin);
        material.SetShaderParameter("fine_height_map_size", _fineHeightMapSize);
        material.SetShaderParameter(
            "fine_height_cell_size", _fineHeightCellWorldSize);
        if (_fineHeightTexture is not null)
        {
            material.SetShaderParameter(
                "fine_height_map", _fineHeightTexture);
        }
    }

    private ShaderMaterial Track(ShaderMaterial material)
    {
        _shaderMaterials.Add(material);
        ConfigureFogMaterial(material);
        return material;
    }

    private void ConfigureFogMaterial(ShaderMaterial material)
    {
        material.SetShaderParameter("war3_fog_enabled", _fogEnabled);
        material.SetShaderParameter("war3_fog_world_origin", _fogWorldOrigin);
        material.SetShaderParameter("war3_fog_world_size", _fogWorldSize);
        if (_fogTexture is not null)
            material.SetShaderParameter("war3_fog_texture", _fogTexture);
    }

    private static string CliffName(byte cliffStyle) => cliffStyle switch
    {
        0 => "cliff0",
        1 => "cliff1",
        _ => throw new ArgumentOutOfRangeException(
            nameof(cliffStyle), cliffStyle,
            "Lordaeron Summer exports exactly two cliff styles (0 and 1).")
    };

    private static string ResolveCliffName(
        TerrainSurfaceDefinition upperSurface) =>
        upperSurface.MaterialKey is "badlands" or "mud" or "vision-smoke"
            ? "cliff1"
            : "cliff0";

    private Material CreateBlendedSurfaceMaterial()
        => CreateLayeredSurfaceMaterial(_groundBlendShader);

    private Material CreateLayeredSurfaceMaterial(Shader shader)
    {
        var material = Track(new ShaderMaterial { Shader = shader });
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
        return Track(new ShaderMaterial
        {
            Shader = _groundShader
        }).WithParameter("terrain_atlas", LoadTexture(path));
    }

    private Material CreateWaterMaterial(bool deep)
    {
        var material = Track(new ShaderMaterial { Shader = _waterShader });
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
