using Godot;

namespace War3Rts;

internal enum War3GroundOverlayKind : byte
{
    Foundation,
    AuthoredShadow
}

/// <summary>
/// Node-free, texture-bucketed ground art. One RenderingServer MultiMesh is
/// shared by every visible building/tree using the same Warcraft texture.
/// </summary>
internal sealed class War3GroundOverlayBatch : IDisposable
{
    public const float WarcraftSourceUnitWorldScale = 0.01f;
    public const float ShadowTexelSourceSize = 32f;

    public static float FoundationWorldDiameter(float sourceHalfExtent) =>
        sourceHalfExtent * 2f * WarcraftSourceUnitWorldScale;

    private readonly MultiMesh _multiMesh;
    private readonly Rid _instance;
    private float[] _buffer;
    private int _count;

    public War3GroundOverlayBatch(
        Node3D host,
        string texturePath,
        War3GroundOverlayKind kind,
        int blendMode,
        int initialCapacity = 16)
    {
        TexturePath = texturePath;
        Kind = kind;
        var texture = War3RuntimeAssets.LoadTexture(texturePath) ??
                      throw new FileNotFoundException(
                          $"Warcraft ground texture is missing: {texturePath}");
        TextureSize = texture.GetSize();
        AnchorPixel = kind == War3GroundOverlayKind.AuthoredShadow
            ? FindShadowAnchor(texture)
            : (TextureSize - Vector2.One) * 0.5f;

        var material = CreateMaterial(texture, kind, blendMode);
        var mesh = new PlaneMesh
        {
            Size = Vector2.One,
            Material = material
        };
        var capacity = Math.Max(1, initialCapacity);
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = capacity,
            VisibleInstanceCount = 0
        };
        _buffer = new float[capacity * 12];
        _instance = RenderingServer.InstanceCreate2(
            _multiMesh.GetRid(), host.GetWorld3D().Scenario);
        RenderingServer.InstanceGeometrySetCastShadowsSetting(
            _instance, RenderingServer.ShadowCastingSetting.Off);
        RenderingServer.InstanceSetIgnoreCulling(_instance, true);
    }

    public string TexturePath { get; }
    public War3GroundOverlayKind Kind { get; }
    public Vector2 TextureSize { get; }
    public Vector2 AnchorPixel { get; }
    public int VisibleCount => _count;

    public void BeginFrame() => _count = 0;

    public void AddCentered(Vector3 center, Vector2 worldSize)
    {
        EnsureCapacity(_count + 1);
        var transform = new Transform3D(
            Basis.FromScale(new Vector3(worldSize.X, 1f, worldSize.Y)),
            center);
        WriteTransform(_buffer, _count * 12, transform);
        _count++;
    }

    public void AddAuthoredShadow(Vector3 modelOrigin)
    {
        var texelWorld = ShadowTexelSourceSize * WarcraftSourceUnitWorldScale;
        var worldSize = TextureSize * texelWorld;
        // The single red texel in an original building/tree shadow is the
        // model origin, not visible art. Offset the plane so that texel lands
        // exactly under the model and discard it in the shader.
        var anchorFromCenter = new Vector2(
            AnchorPixel.X + 0.5f - TextureSize.X * 0.5f,
            AnchorPixel.Y + 0.5f - TextureSize.Y * 0.5f) * texelWorld;
        var center = modelOrigin - new Vector3(
            anchorFromCenter.X, 0f, anchorFromCenter.Y);
        AddCentered(center, worldSize);
    }

    public void Flush()
    {
        if (_count == 0)
        {
            _multiMesh.VisibleInstanceCount = 0;
            return;
        }
        RenderingServer.MultimeshSetBuffer(
            _multiMesh.GetRid(), _buffer.AsSpan());
        _multiMesh.VisibleInstanceCount = _count;
    }

    public void Dispose()
    {
        if (_instance.IsValid) RenderingServer.FreeRid(_instance);
        _multiMesh.Dispose();
    }

    private void EnsureCapacity(int required)
    {
        var capacity = _buffer.Length / 12;
        if (required <= capacity) return;
        while (capacity < required) capacity *= 2;
        _multiMesh.VisibleInstanceCount = 0;
        _multiMesh.InstanceCount = capacity;
        Array.Resize(ref _buffer, capacity * 12);
    }

    private static ShaderMaterial CreateMaterial(
        Texture2D texture,
        War3GroundOverlayKind kind,
        int blendMode)
    {
        var shader = new Shader
        {
            Code = kind == War3GroundOverlayKind.Foundation
                ? blendMode == 1
                    ? FoundationShader("blend_add")
                    : FoundationShader("blend_mix")
                : ShadowShader
        };
        var material = new ShaderMaterial { Shader = shader };
        material.RenderPriority = kind == War3GroundOverlayKind.Foundation
            ? -2
            : -1;
        material.SetShaderParameter("ground_texture", texture);
        return material;
    }

    private static string FoundationShader(string blend) => $$"""
        shader_type spatial;
        render_mode {{blend}}, depth_draw_never, cull_disabled, unshaded;
        uniform sampler2D ground_texture : source_color, filter_linear_mipmap;

        void fragment() {
            vec4 color = texture(ground_texture, UV);
            if (color.a < 0.004) discard;
            ALBEDO = color.rgb;
            ALPHA = color.a;
        }
        """;

    private const string ShadowShader = """
        shader_type spatial;
        render_mode blend_mix, depth_draw_never, cull_disabled, unshaded;
        uniform sampler2D ground_texture : source_color, filter_linear;

        void fragment() {
            vec4 mask = texture(ground_texture, UV);
            bool origin_marker = mask.r > 0.55 &&
                mask.g < 0.25 && mask.b < 0.25;
            if (origin_marker || mask.a < 0.01) discard;
            ALBEDO = vec3(0.006, 0.008, 0.011);
            // Warcraft's authored mask is deliberately tiny, but the game
            // reconstructs it linearly. Preserve its silhouette without
            // exposing the source texel grid as blocky squares.
            ALPHA = mask.a * 0.42;
        }
        """;

    private static Vector2 FindShadowAnchor(Texture2D texture)
    {
        var image = texture.GetImage();
        if (image is null || image.IsEmpty())
            return (texture.GetSize() - Vector2.One) * 0.5f;
        for (var y = 0; y < image.GetHeight(); y++)
        for (var x = 0; x < image.GetWidth(); x++)
        {
            var color = image.GetPixel(x, y);
            if (color.R > 0.55f && color.G < 0.25f &&
                color.B < 0.25f && color.A > 0.5f)
                return new Vector2(x, y);
        }
        return (texture.GetSize() - Vector2.One) * 0.5f;
    }

    private static void WriteTransform(
        float[] buffer,
        int offset,
        Transform3D transform)
    {
        var basis = transform.Basis;
        var origin = transform.Origin;
        buffer[offset] = basis.X.X;
        buffer[offset + 1] = basis.Y.X;
        buffer[offset + 2] = basis.Z.X;
        buffer[offset + 3] = origin.X;
        buffer[offset + 4] = basis.X.Y;
        buffer[offset + 5] = basis.Y.Y;
        buffer[offset + 6] = basis.Z.Y;
        buffer[offset + 7] = origin.Y;
        buffer[offset + 8] = basis.X.Z;
        buffer[offset + 9] = basis.Y.Z;
        buffer[offset + 10] = basis.Z.Z;
        buffer[offset + 11] = origin.Z;
    }
}
