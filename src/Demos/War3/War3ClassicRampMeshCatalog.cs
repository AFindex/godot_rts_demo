using System.Text.RegularExpressions;
using Godot;
using RtsDemo.Demos.ThreeD;

namespace RtsDemo.Demos.War3;

/// <summary>
/// Read-only catalog for Warcraft III's natural CliffTrans pieces. These are
/// the authored 1x2/2x1 side transitions around a walkable ramp, not a
/// replacement for the ground slope itself.
/// </summary>
public sealed partial class War3ClassicRampMeshCatalog
{
    private const string RelativeDirectory =
        "models/doodads/terrain/clifftrans";
    private readonly Dictionary<string, string> _assets;
    private readonly Dictionary<string, Rts3DClassicCliffMesh> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    private War3ClassicRampMeshCatalog(Dictionary<string, string> assets)
    {
        _assets = assets;
    }

    public int SignatureCount => _assets.Count;
    public int AssetCount => _assets.Count;

    public static War3ClassicRampMeshCatalog LoadDefault()
    {
        var absoluteDirectory = War3AssetPack.AbsolutePath(RelativeDirectory);
        var assets = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(absoluteDirectory))
            return new War3ClassicRampMeshCatalog(assets);

        foreach (var absolutePath in Directory.EnumerateFiles(
                     absoluteDirectory, "*.glb", SearchOption.TopDirectoryOnly))
        {
            var match = RampFilePattern().Match(
                Path.GetFileNameWithoutExtension(absolutePath));
            if (!match.Success)
                continue;
            assets[match.Groups[1].Value.ToUpperInvariant()] = Path.Combine(
                    RelativeDirectory,
                    Path.GetFileName(absolutePath))
                .Replace('\\', '/');
        }
        return new War3ClassicRampMeshCatalog(assets);
    }

    public bool Contains(string signature) => _assets.ContainsKey(signature);

    public bool TryGet(
        string signature,
        out Rts3DClassicCliffMesh definition)
    {
        definition = default;
        if (!_assets.TryGetValue(signature, out var selected))
            return false;
        if (_loaded.TryGetValue(selected, out definition))
            return true;

        var document = new GltfDocument();
        var state = new GltfState();
        if (document.AppendFromFile(
                War3AssetPack.AbsolutePath(selected), state) != Error.Ok)
        {
            return false;
        }
        var generated = document.GenerateScene(state);
        if (generated is null)
            return false;
        try
        {
            if (!TryFindStaticMesh(
                    generated,
                    Transform3D.Identity,
                    out var mesh,
                    out var modelTransform) ||
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
        "^clifftrans([a-z]{4})0$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RampFilePattern();
}
