using War3Rts.Data;
using RtsDemo.Demos.War3;

namespace War3Rts;

/// <summary>
/// Original Warcraft ground-art bindings for a building. UnitUI owns the
/// per-object selection while UberSplatData owns the texture and source-unit
/// scale. Pathing textures are deliberately not used as visual-size guesses.
/// </summary>
public readonly record struct War3BuildingGroundVisualDefinition(
    string UberSplatId,
    string FoundationTexturePath,
    float FoundationSourceHalfExtent,
    int FoundationBlendMode,
    string BuildingShadowId,
    string BuildingShadowTexturePath)
{
    public bool HasFoundation =>
        FoundationTexturePath.Length > 0 && FoundationSourceHalfExtent > 0f;

    public bool HasBuildingShadow => BuildingShadowTexturePath.Length > 0;
}

public readonly record struct War3UberSplatDefinition(
    string Id,
    string TexturePath,
    float SourceHalfExtent,
    int BlendMode);

/// <summary>
/// Data bridge for the building ground art exported from classic Warcraft III.
/// The IDs below are the complete set referenced by exported building records;
/// values come from patch/Splats/UberSplatData.slk.
/// </summary>
public static class War3BuildingGroundVisualCatalog
{
    private const string SplatRoot = @"ReplaceableTextures\Splats";
    private const string ShadowRoot = @"ReplaceableTextures\Shadows";
    public const string CityTreeShadowTexturePath =
        @"ReplaceableTextures\Shadows\ShadowCityTree.blp";

    private static readonly Dictionary<string, War3UberSplatDefinition>
        UberSplats = new Dictionary<string, War3UberSplatDefinition>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["DPSE"] = Splat("DPSE", "DarkPortalUberSplatSE", 400f),
            ["DPSW"] = Splat("DPSW", "DarkPortalUberSplatSW", 400f),
            ["EMDA"] = Splat("EMDA", "AncientUberSplat", 200f),
            ["EMDB"] = Splat("EMDB", "NightElfUberSplat", 180f),
            ["ESMA"] = Splat("ESMA", "AncientUberSplat", 120f),
            ["ESMB"] = Splat("ESMB", "NightElfUberSplat", 110f),
            ["HCAS"] = Splat("HCAS", "HumanCastleUberSplat", 230f),
            ["HLAR"] = Splat("HLAR", "HumanUberSplat", 230f),
            ["HMED"] = Splat("HMED", "HumanUberSplat", 190f),
            ["HSMA"] = Splat("HSMA", "HumanUberSplat", 110f),
            ["HTOW"] = Splat("HTOW", "HumanTownHallUberSplat", 230f),
            ["NDGS"] = Splat("NDGS", "DemonGateUberSplat", 375f, 1),
            ["NGOL"] = Splat("NGOL", "GoldmineUberSplat", 180f),
            ["NLAR"] = Splat("NLAR", "NagaTownHallUberSplat", 230f),
            ["OLAR"] = Splat("OLAR", "OrcUberSplat", 240f),
            ["OMED"] = Splat("OMED", "OrcUberSplat", 200f),
            ["OSMA"] = Splat("OSMA", "OrcUberSplat", 110f),
            ["ULAR"] = Splat("ULAR", "UndeadUberSplat", 240f),
            ["UMED"] = Splat("UMED", "UndeadUberSplat", 200f),
            ["USMA"] = Splat("USMA", "UndeadUberSplat", 170f)
        };

    public static IReadOnlyCollection<War3UberSplatDefinition>
        Definitions => UberSplats.Values;

    /// <summary>
    /// The current encounter maps use Lordaeron Summer terrain. Warcraft
    /// exports terrain-tinted variants as l_*.png; special splats without a
    /// terrain variant deliberately fall back to their base texture.
    /// </summary>
    public static string ResolveLordaeronFoundationTexturePath(
        string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath)) return string.Empty;
        var normalized = texturePath.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        var directory = separator >= 0
            ? normalized[..(separator + 1)]
            : string.Empty;
        var file = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        var themed = $"{directory}l_{file}";
        return TextureExists(themed) ? themed : texturePath;
    }

    public static bool TextureExists(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath)) return false;
        var normalized = virtualPath.Replace('\\', '/').TrimStart('/');
        var extension = Path.GetExtension(normalized);
        if (extension.Length == 0 || extension.Equals(
                ".blp", StringComparison.OrdinalIgnoreCase))
            normalized = Path.ChangeExtension(normalized, ".png");
        return File.Exists(War3AssetPack.AbsolutePath(
            $"textures/{normalized.ToLowerInvariant()}"));
    }

    public static War3BuildingGroundVisualDefinition Resolve(
        IWar3UnitDataCatalog catalog,
        string objectId)
    {
        if (!catalog.TryGet(objectId, out var data)) return default;
        return Resolve(data);
    }

    public static War3BuildingGroundVisualDefinition Resolve(
        War3UnitData data)
    {
        var splatId = EditorValue(data, "UnitUI", "uberSplat");
        var shadowId = EditorValue(data, "UnitUI", "buildingShadow");
        War3UberSplatDefinition splat = default;
        var hasSplat = Meaningful(splatId) &&
                       UberSplats.TryGetValue(splatId, out splat);
        var hasShadow = Meaningful(shadowId);
        return new War3BuildingGroundVisualDefinition(
            hasSplat ? splat.Id : string.Empty,
            hasSplat ? splat.TexturePath : string.Empty,
            hasSplat ? splat.SourceHalfExtent : 0f,
            hasSplat ? splat.BlendMode : 0,
            hasShadow ? shadowId : string.Empty,
            hasShadow
                ? $@"{ShadowRoot}\{shadowId}.blp"
                : string.Empty);
    }

    public static bool TryResolveUberSplat(
        string id,
        out War3UberSplatDefinition definition) =>
        UberSplats.TryGetValue(id, out definition);

    private static War3UberSplatDefinition Splat(
        string id,
        string file,
        float sourceHalfExtent,
        int blendMode = 0) =>
        new(id, $@"{SplatRoot}\{file}.blp", sourceHalfExtent, blendMode);

    private static string EditorValue(
        War3UnitData data,
        string table,
        string field)
    {
        var values = data.Editor.FirstOrDefault(pair =>
            pair.Key.Equals(table, StringComparison.OrdinalIgnoreCase)).Value;
        if (values is null) return string.Empty;
        var match = values.FirstOrDefault(pair =>
            pair.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
        return match.Key is null ? string.Empty : match.Value?.Trim() ?? string.Empty;
    }

    private static bool Meaningful(string value) =>
        value.Length > 0 && value is not "_" and not "-";
}
