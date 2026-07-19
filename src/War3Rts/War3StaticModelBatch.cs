using Godot;

namespace War3Rts;

/// <summary>
/// Renders many stationary copies of a Warcraft model with one MultiMesh.
/// The selected Stand geosets are baked into model-local geometry so the
/// source skeleton, animation player and per-instance material overrides are
/// not duplicated for every tree.
/// </summary>
internal sealed class War3StaticModelBatch : IDisposable
{
    private readonly Node3D _host;
    private MultiMesh? _multiMesh;
    private Rid _instance;
    private Transform3D[] _transforms = [];
    private bool[] _visible = [];
    private float[] _buffer = [];
    private int _instanceCount;
    private bool _dynamic;
    private bool _bufferDirty;
    private bool _batchVisible = true;

    public War3StaticModelBatch(Node3D host) => _host = host;

    public int InstanceCount => _instanceCount;
    public int SurfaceCount { get; private set; }
    public bool Visible
    {
        get => _batchVisible;
        set
        {
            if (_batchVisible == value) return;
            _batchVisible = value;
            if (_instance.IsValid)
                RenderingServer.InstanceSetVisible(_instance, value);
        }
    }

    public void Initialize(
        string source,
        int instanceCount,
        int team = 0,
        bool castShadows = true)
    {
        if (instanceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(instanceCount));

        var probe = new War3ModelActor { Name = "StaticModelProbe" };
        _host.AddChild(probe);
        probe.Load(source, camera: null, team, includeEffects: false);
        probe.PlayPreferred(true, "Stand");
        var mesh = BakeVisibleGeometry(probe);
        _host.RemoveChild(probe);
        probe.Free();

        SurfaceCount = mesh.GetSurfaceCount();
        _transforms = new Transform3D[instanceCount];
        _visible = new bool[instanceCount];
        _instanceCount = instanceCount;
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = instanceCount
        };
        _instance = RenderingServer.InstanceCreate2(
            _multiMesh.GetRid(), _host.GetWorld3D().Scenario);
        RenderingServer.InstanceGeometrySetCastShadowsSetting(
            _instance,
            castShadows
                ? RenderingServer.ShadowCastingSetting.On
                : RenderingServer.ShadowCastingSetting.Off);
    }

    public void InitializeDynamic(
        string source,
        int initialCapacity,
        int team)
    {
        Initialize(
            source, Math.Max(1, initialCapacity), team,
            castShadows: false);
        _instanceCount = 0;
        _dynamic = true;
        _buffer = new float[_transforms.Length * 12];
    }

    public int AddInstance()
    {
        if (_multiMesh is null)
            throw new InvalidOperationException("Batch is not initialized.");
        if (_instanceCount >= _transforms.Length)
            Grow(Math.Max(4, _transforms.Length * 2));
        var index = _instanceCount++;
        var hidden = HiddenTransform(Transform3D.Identity);
        _transforms[index] = Transform3D.Identity;
        _visible[index] = false;
        if (_dynamic)
        {
            WriteTransform(index, hidden);
            _bufferDirty = true;
        }
        else
        {
            _multiMesh.SetInstanceTransform(index, hidden);
        }
        return index;
    }

    public void SetInstanceTransform(int index, Transform3D transform)
    {
        if (_multiMesh is null || (uint)index >= (uint)_instanceCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        _transforms[index] = transform;
        _visible[index] = true;
        if (_dynamic)
        {
            WriteTransform(index, transform);
            _bufferDirty = true;
        }
        else
        {
            _multiMesh.SetInstanceTransform(index, transform);
        }
    }

    public void SetInstanceVisible(int index, bool visible)
    {
        if (_multiMesh is null || (uint)index >= (uint)_instanceCount)
            return;
        var transform = visible
            ? _transforms[index]
            : HiddenTransform(_transforms[index]);
        _visible[index] = visible;
        if (_dynamic)
        {
            WriteTransform(index, transform);
            _bufferDirty = true;
        }
        else
        {
            _multiMesh.SetInstanceTransform(index, transform);
        }
    }

    public void SetInstanceTransform(
        int index,
        Transform3D transform,
        bool visible)
    {
        if (_multiMesh is null || (uint)index >= (uint)_instanceCount)
            return;
        _transforms[index] = transform;
        _visible[index] = visible;
        var rendered = visible ? transform : HiddenTransform(transform);
        if (_dynamic)
        {
            WriteTransform(index, rendered);
            _bufferDirty = true;
        }
        else
        {
            _multiMesh.SetInstanceTransform(index, rendered);
        }
    }

    public void FlushDynamicBuffer()
    {
        if (!_dynamic || !_bufferDirty || _multiMesh is null) return;
        RenderingServer.MultimeshSetBuffer(
            _multiMesh.GetRid(), _buffer.AsSpan());
        _bufferDirty = false;
    }

    private void Grow(int capacity)
    {
        if (_multiMesh is null || capacity <= _transforms.Length) return;
        Array.Resize(ref _transforms, capacity);
        Array.Resize(ref _visible, capacity);
        _multiMesh.InstanceCount = capacity;
        if (_dynamic)
        {
            Array.Resize(ref _buffer, capacity * 12);
            for (var index = 0; index < capacity; index++)
            {
                var transform = index < _instanceCount
                    ? _transforms[index]
                    : Transform3D.Identity;
                WriteTransform(
                    index,
                    index < _instanceCount && _visible[index]
                        ? transform
                        : HiddenTransform(transform));
            }
            _bufferDirty = true;
            return;
        }
        for (var index = 0; index < capacity; index++)
        {
            var transform = index < _instanceCount
                ? _transforms[index]
                : Transform3D.Identity;
            _multiMesh.SetInstanceTransform(
                index,
                index < _instanceCount
                    ? transform
                    : HiddenTransform(transform));
        }
    }

    private static Transform3D HiddenTransform(Transform3D transform) =>
        new(
            Basis.FromScale(Vector3.One * 0.00001f),
            transform.Origin);

    private void WriteTransform(int index, Transform3D transform)
    {
        var offset = index * 12;
        var basis = transform.Basis;
        var origin = transform.Origin;
        _buffer[offset] = basis.X.X;
        _buffer[offset + 1] = basis.Y.X;
        _buffer[offset + 2] = basis.Z.X;
        _buffer[offset + 3] = origin.X;
        _buffer[offset + 4] = basis.X.Y;
        _buffer[offset + 5] = basis.Y.Y;
        _buffer[offset + 6] = basis.Z.Y;
        _buffer[offset + 7] = origin.Y;
        _buffer[offset + 8] = basis.X.Z;
        _buffer[offset + 9] = basis.Y.Z;
        _buffer[offset + 10] = basis.Z.Z;
        _buffer[offset + 11] = origin.Z;
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

    public void Dispose()
    {
        if (_instance.IsValid)
        {
            RenderingServer.FreeRid(_instance);
            _instance = default;
        }
        _multiMesh = null;
        _transforms = [];
        _visible = [];
        _batchVisible = false;
        _buffer = [];
        _instanceCount = 0;
    }
}
