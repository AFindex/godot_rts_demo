using System.Text.RegularExpressions;
using Godot;
using RtsDemo.Demos.ThreeD;

namespace RtsDemo.Demos.War3;

/// <summary>
/// Read-only catalog for the exported classic natural-cliff GLBs. Warcraft's
/// Cliffs.slk values are represented by the files themselves, which also lets
/// incomplete export packs degrade to the presenter's procedural fallback.
/// </summary>
public sealed partial class War3ClassicCliffMeshCatalog
{
    private const string RelativeDirectory =
        "models/doodads/terrain/cliffs";
    private readonly Dictionary<string, string[]> _assets;
    private readonly Dictionary<string, Rts3DClassicCliffMesh> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    private War3ClassicCliffMeshCatalog(Dictionary<string, string[]> assets)
    {
        _assets = assets;
        AssetCount = assets.Values.Sum(value => value.Length);
    }

    public int SignatureCount => _assets.Count;
    public int AssetCount { get; }

    public static War3ClassicCliffMeshCatalog LoadDefault()
    {
        var absoluteDirectory = War3AssetPack.AbsolutePath(RelativeDirectory);
        if (!Directory.Exists(absoluteDirectory))
            return new War3ClassicCliffMeshCatalog(
                new Dictionary<string, string[]>(
                    StringComparer.OrdinalIgnoreCase));

        var grouped = new Dictionary<string, SortedDictionary<int, string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var absolutePath in Directory.EnumerateFiles(
                     absoluteDirectory, "*.glb", SearchOption.TopDirectoryOnly))
        {
            var match = NaturalCliffFilePattern().Match(
                Path.GetFileNameWithoutExtension(absolutePath));
            if (!match.Success ||
                !int.TryParse(match.Groups[2].Value, out var variation))
            {
                continue;
            }
            var signature = match.Groups[1].Value.ToUpperInvariant();
            if (!grouped.TryGetValue(signature, out var variants))
            {
                variants = new SortedDictionary<int, string>();
                grouped.Add(signature, variants);
            }
            variants[variation] = Path.Combine(
                    RelativeDirectory,
                    Path.GetFileName(absolutePath))
                .Replace('\\', '/');
        }
        return new War3ClassicCliffMeshCatalog(grouped.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Values.ToArray(),
            StringComparer.OrdinalIgnoreCase));
    }

    public int VariationCount(string signature) =>
        _assets.TryGetValue(signature, out var assets) ? assets.Length : 0;

    public bool TryGet(
        string signature,
        int variation,
        out Rts3DClassicCliffMesh definition)
    {
        definition = default;
        if (!_assets.TryGetValue(signature, out var assets) ||
            assets.Length == 0)
        {
            return false;
        }
        var selected = assets[Math.Clamp(variation, 0, assets.Length - 1)];
        if (_loaded.TryGetValue(selected, out definition))
            return true;

        var document = new GltfDocument();
        var state = new GltfState();
        var absolute = War3AssetPack.AbsolutePath(selected);
        if (document.AppendFromFile(absolute, state) != Error.Ok)
            return false;
        var generated = document.GenerateScene(state);
        if (generated is null)
            return false;
        try
        {
            if (!TryFindStaticMesh(
                    generated, Transform3D.Identity,
                    out var mesh, out var modelTransform) ||
                mesh is null)
            {
                return false;
            }
            definition = new Rts3DClassicCliffMesh(
                selected, mesh, modelTransform);
            _loaded[selected] = definition;
            return true;
        }
        finally
        {
            generated.Free();
        }
    }

    private static bool TryFindStaticMesh(
        Node node,
        Transform3D parentTransform,
        out Mesh? mesh,
        out Transform3D modelTransform)
    {
        var transform = node is Node3D spatial
            ? parentTransform * spatial.Transform
            : parentTransform;
        if (node is MeshInstance3D { Mesh: not null } meshInstance)
        {
            mesh = meshInstance.Mesh;
            modelTransform = transform;
            return true;
        }
        foreach (var child in node.GetChildren())
        {
            if (TryFindStaticMesh(
                    child, transform, out mesh, out modelTransform))
            {
                return true;
            }
        }
        mesh = null;
        modelTransform = Transform3D.Identity;
        return false;
    }

    [GeneratedRegex(
        "^cliffs([abc]{4})([0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NaturalCliffFilePattern();
}
