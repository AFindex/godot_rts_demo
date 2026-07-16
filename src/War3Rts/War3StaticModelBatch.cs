using Godot;

namespace War3Rts;

/// <summary>
/// Renders many stationary copies of a Warcraft model with one MultiMesh.
/// The selected Stand geosets are baked into model-local geometry so the
/// source skeleton, animation player and per-instance material overrides are
/// not duplicated for every tree.
/// </summary>
internal sealed partial class War3StaticModelBatch : Node3D
{
    private MultiMesh? _multiMesh;
    private Transform3D[] _transforms = [];

    public int InstanceCount => _transforms.Length;
    public int SurfaceCount { get; private set; }

    public void Initialize(string source, int instanceCount)
    {
        if (instanceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(instanceCount));

        var probe = new War3ModelActor { Name = "StaticModelProbe" };
        AddChild(probe);
        probe.Load(source, camera: null, team: 0, includeEffects: false);
        probe.PlayPreferred(true, "Stand");
        var mesh = BakeVisibleGeometry(probe);
        RemoveChild(probe);
        probe.Free();

        SurfaceCount = mesh.GetSurfaceCount();
        _transforms = new Transform3D[instanceCount];
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = instanceCount
        };
        AddChild(new MultiMeshInstance3D
        {
            Name = "Instances",
            Multimesh = _multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    public void SetInstanceTransform(int index, Transform3D transform)
    {
        if (_multiMesh is null || (uint)index >= (uint)_transforms.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        _transforms[index] = transform;
        _multiMesh.SetInstanceTransform(index, transform);
    }

    public void SetInstanceVisible(int index, bool visible)
    {
        if (_multiMesh is null || (uint)index >= (uint)_transforms.Length)
            return;
        var transform = visible
            ? _transforms[index]
            : new Transform3D(
                Basis.FromScale(Vector3.One * 0.00001f),
                _transforms[index].Origin);
        _multiMesh.SetInstanceTransform(index, transform);
    }

    private static ArrayMesh BakeVisibleGeometry(War3ModelActor actor)
    {
        var output = new ArrayMesh();
        if (actor.ModelRoot is null) return output;
        BakeNode(actor.ModelRoot, actor.GlobalTransform.AffineInverse(), true, output);
        if (output.GetSurfaceCount() == 0)
            throw new InvalidOperationException(
                $"Static Warcraft model has no visible Stand geometry: {actor.Source}");
        return output;
    }

    private static void BakeNode(
        Node node,
        Transform3D actorInverse,
        bool parentVisible,
        ArrayMesh output)
    {
        var visible = parentVisible &&
                      (node is not Node3D spatial || spatial.Visible);
        if (visible && node is MeshInstance3D { Mesh: not null } mesh)
        {
            var transform = actorInverse * mesh.GlobalTransform;
            // Warcraft tree roots use a uniform authoring scale. Transforming
            // and normalizing is equivalent to inverse-transpose here and also
            // tolerates legacy animation tracks that momentarily contain a
            // zero scale on a hidden/death geoset.
            var normalBasis = transform.Basis;
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh is ArrayMesh arrayMesh &&
                    arrayMesh.SurfaceGetPrimitiveType(surface) !=
                    Mesh.PrimitiveType.Triangles)
                    continue;
                var arrays = mesh.Mesh.SurfaceGetArrays(surface);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var uv2s = arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if (indices.Length == 0)
                    indices = Enumerable.Range(0, vertices.Length).ToArray();

                var tool = new SurfaceTool();
                tool.Begin(Mesh.PrimitiveType.Triangles);
                tool.SetMaterial(mesh.GetActiveMaterial(surface));
                foreach (var index in indices)
                {
                    if ((uint)index >= (uint)vertices.Length) continue;
                    if (normals.Length == vertices.Length)
                        tool.SetNormal((normalBasis * normals[index]).Normalized());
                    if (colors.Length == vertices.Length)
                        tool.SetColor(colors[index]);
                    if (uvs.Length == vertices.Length) tool.SetUV(uvs[index]);
                    if (uv2s.Length == vertices.Length) tool.SetUV2(uv2s[index]);
                    tool.AddVertex(transform * vertices[index]);
                }
                tool.Index();
                tool.Commit(output);
            }
        }
        foreach (var child in node.GetChildren())
            BakeNode(child, actorInverse, visible, output);
    }
}
