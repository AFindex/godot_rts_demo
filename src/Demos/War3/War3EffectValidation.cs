using Godot;
using RtsDemo.Demos.War3;

namespace War3Rts;

/// <summary>
/// Isolated rendered validation for Warcraft projectile filtering and shadow
/// reception. It deliberately uses the same node-free/VAT actor and the same
/// dual-grid ground material as gameplay, without creating a simulation.
/// </summary>
public sealed partial class War3EffectValidation : Node3D
{
    private const string MortarSource =
        @"Abilities\Weapons\Mortar\MortarMissile.mdx";
    private const string PeasantSource = @"Units\Human\Peasant\Peasant.mdx";
    private readonly List<War3RidModelActor> _mortars = [];
    private readonly List<War3RidModelActor> _shadowActors = [];
    private War3NodeFreeRenderWorld? _renderWorld;
    private Camera3D? _camera;
    private double _elapsed;
    private int _renderedFrames;
    private bool _captureRequested;
    private bool _captureStarted;

    public override void _Ready()
    {
        _captureRequested = OS.GetCmdlineUserArgs().Contains(
            "--war3-effect-validation-capture");
        CreateLighting();
        CreateGroundComparison();
        CreateBackdrop();
        CreateShadowCasters();
        CreateMortars();
        CreateOverlay();
        PrintImportedMaterialDiagnostics();
    }

    public override void _Process(double delta)
    {
        if (_renderWorld is null) return;
        _elapsed += delta;
        UpdateMortars();
        _renderWorld.Advance();
        _renderWorld.Flush();
        _renderedFrames++;
        if (_captureRequested && !_captureStarted && _renderedFrames >= 300)
        {
            _captureStarted = true;
            _ = CaptureAndQuitAsync();
        }
    }

    public override void _ExitTree()
    {
        foreach (var mortar in _mortars) mortar.Dispose();
        _mortars.Clear();
        foreach (var actor in _shadowActors) actor.Dispose();
        _shadowActors.Clear();
        _renderWorld?.Dispose();
        _renderWorld = null;
    }

    private void CreateLighting()
    {
        var gameplayShadowSettings = OS.GetCmdlineUserArgs().Contains(
            "--war3-effect-validation-gameplay-shadow");
        GD.Print(
            $"WAR3_EFFECT_VALIDATION_SHADOW_ATLAS " +
            $"mode={(gameplayShadowSettings ? "gameplay" : "reference")} " +
            $"directional_setting={ProjectSettings.GetSetting(
                "rendering/lights_and_shadows/directional_shadow/size")} " +
            $"positional_before={GetViewport().PositionalShadowAtlasSize}");
        RenderingServer.DirectionalShadowAtlasSetSize(4096, true);
        GetViewport().PositionalShadowAtlasSize = 2048;
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("161b21"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("a8b5c1"),
                AmbientLightEnergy = gameplayShadowSettings ? 0.5f : 0.3f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapExposure = 1.05f
            }
        });
        var shadowLight = new DirectionalLight3D
        {
            Name = "ShadowKeyLight",
            Position = new Vector3(-4f, 6f, -5f),
            LightColor = new Color("fff0cf"),
            LightEnergy = 1.65f,
            ShadowEnabled = true,
            ShadowOpacity = 1f,
            ShadowBias = gameplayShadowSettings ? 0.06f : 0.005f,
            ShadowNormalBias = gameplayShadowSettings ? 1.1f : 0.02f,
            ShadowBlur = 0f,
            DirectionalShadowMode = gameplayShadowSettings
                ? DirectionalLight3D.ShadowMode.Parallel4Splits
                : DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = gameplayShadowSettings ? 160f : 80f,
            DirectionalShadowBlendSplits = gameplayShadowSettings,
            DirectionalShadowFadeStart = gameplayShadowSettings ? 0.88f : 0.8f
        };
        AddChild(shadowLight);
        shadowLight.LookAt(Vector3.Zero, Vector3.Up);
        _camera = new Camera3D
        {
            Name = "ValidationCamera",
            Current = true,
            Position = new Vector3(0f, 6.3f, 11.5f),
            Fov = 46f,
            Near = 0.05f,
            Far = 100f
        };
        AddChild(_camera);
        _camera.LookAt(new Vector3(0f, 1.15f, -0.35f), Vector3.Up);
    }

    private void CreateGroundComparison()
    {
        var standard = new StandardMaterial3D
        {
            AlbedoColor = new Color("d2d8dc"),
            Roughness = 0.95f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        AddChild(new MeshInstance3D
        {
            Name = "StandardShadowReceiver",
            Mesh = GroundQuad(5.8f, 7f, standard, 0f),
            Position = new Vector3(-2.95f, 0f, 0f),
            // Match gameplay terrain chunks: this surface receives shadows
            // but does not need to render itself into the shadow atlas.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });

        var terrain = new War3TerrainMaterialSet(
            War3TerrainBlendStyle.DualGrid,
            classicCliffMeshesEnabled: false);
        AddChild(new MeshInstance3D
        {
            Name = "War3DualGridShadowReceiver",
            Mesh = GroundQuad(
                5.8f,
                7f,
                terrain.DualGridSurfaceMaterial(),
                15f * 4096f),
            Position = new Vector3(2.95f, 0f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    private static ArrayMesh GroundQuad(
        float width,
        float depth,
        Material material,
        float packedDualGridMask)
    {
        var halfWidth = width * 0.5f;
        var halfDepth = depth * 0.5f;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[]
        {
            new(-halfWidth, 0f, -halfDepth),
            new(-halfWidth, 0f, halfDepth),
            new(halfWidth, 0f, halfDepth),
            new(halfWidth, 0f, -halfDepth)
        };
        arrays[(int)Mesh.ArrayType.Normal] = new Vector3[]
        {
            Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up
        };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[]
        {
            new(0f, 0f), new(0f, 7f), new(6f, 7f), new(6f, 0f)
        };
        arrays[(int)Mesh.ArrayType.TexUV2] = new Vector2[]
        {
            new(packedDualGridMask, 0f),
            new(packedDualGridMask, 0f),
            new(packedDualGridMask, 0f),
            new(packedDualGridMask, 0f)
        };
        arrays[(int)Mesh.ArrayType.Index] = new int[]
        {
            0, 2, 1, 0, 3, 2
        };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    private void CreateBackdrop()
    {
        AddBackdropHalf(
            "WhiteTransparencyBackdrop",
            -2.85f,
            Colors.White);
        AddBackdropHalf(
            "DarkTransparencyBackdrop",
            2.85f,
            new Color("080a0d"));
    }

    private void AddBackdropHalf(string name, float x, Color color)
    {
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new QuadMesh
            {
                Size = new Vector2(5.7f, 3.6f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = color,
                    Roughness = 1f,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
                }
            },
            Position = new Vector3(x, 2.05f, -2.65f)
        });
    }

    private void CreateShadowCasters()
    {
        AddShadowCaster("StandardCaster", new Vector3(-2.9f, 2.15f, 0.7f));
        AddShadowCaster("War3TerrainCaster", new Vector3(2.9f, 2.15f, 0.7f));
    }

    private void AddShadowCaster(string name, Vector3 position)
    {
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh
            {
                Size = new Vector3(1.45f, 0.3f, 1.45f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color("e6bf72"),
                    Roughness = 0.75f
                }
            },
            Position = position,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On
        });
    }

    private void CreateMortars()
    {
        _renderWorld = new War3NodeFreeRenderWorld(this, _camera!);
        // Keep the projectile's model layers and PE2 layers separate on the
        // white half. This makes an additive-tail texture with leaked black
        // pixels distinguishable from authored modulate smoke immediately.
        AddMortar(includeEffects: false);
        AddMortar(includeEffects: true);
        AddMortar(includeEffects: true);
        AddMortar(includeEffects: true);
        AddVatShadowProbe(new Vector3(-2.7f, 0.04f, 0.8f), 0f);
        AddVatShadowProbe(new Vector3(2.7f, 0.04f, 0.8f), Mathf.Pi);
        UpdateMortars();
        _renderWorld.Flush();
    }

    private void AddMortar(bool includeEffects)
    {
        var actor = _renderWorld!.CreateActor(
            MortarSource,
            team: 0,
            includeEffects: includeEffects);
        actor.PlayPreferred(true, "Stand", "Birth");
        actor.SetShadowCastingEnabled(false);
        _mortars.Add(actor);
    }

    private void AddVatShadowProbe(Vector3 position, float yaw)
    {
        var actor = _renderWorld!.CreateActor(
            PeasantSource,
            team: War3HumanScenario.PlayerId,
            includeEffects: false);
        actor.PlayPreferred(true, "Stand", "Stand Ready");
        actor.SetShadowCastingEnabled(true);
        actor.Transform = new Transform3D(
            new Basis(Vector3.Up, yaw),
            position);
        _shadowActors.Add(actor);
    }

    private void UpdateMortars()
    {
        if (_mortars.Count < 4) return;
        var time = (float)_elapsed;
        SetMortarTransform(
            _mortars[0],
            new Vector3(-4.05f, 2.55f, -0.75f),
            0f);
        SetMortarTransform(
            _mortars[1],
            new Vector3(-1.75f, 1.35f, -0.75f),
            0f);
        SetMortarTransform(
            _mortars[2],
            new Vector3(2.1f, 1.35f, -0.75f),
            Mathf.Pi);
        var phase = time * 0.72f;
        SetMortarTransform(
            _mortars[3],
            new Vector3(
                Mathf.Sin(phase) * 3.6f,
                2.9f + Mathf.Sin(phase * 1.7f) * 0.18f,
                -0.15f),
            Mathf.Cos(phase) >= 0f ? 0f : Mathf.Pi);
    }

    private static void SetMortarTransform(
        War3RidModelActor actor,
        Vector3 position,
        float yaw)
    {
        actor.Transform = new Transform3D(
            new Basis(Vector3.Up, yaw),
            position);
        actor.PrepareEffectWorldTransform(actor.Transform);
    }

    private void CreateOverlay()
    {
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        var panel = new PanelContainer
        {
            OffsetLeft = 18f,
            OffsetTop = 18f,
            OffsetRight = 655f,
            OffsetBottom = 104f
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("111820e8"),
            BorderColor = new Color("d3a746"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 12f,
            ContentMarginTop = 8f,
            ContentMarginRight = 12f,
            ContentMarginBottom = 8f
        });
        var label = new Label
        {
            Text = "WAR3 EFFECT VALIDATION\n" +
                   "Left: StandardMaterial shadow receiver   |   " +
                   "Right: gameplay War3 dual-grid receiver\n" +
                   "WHITE: upper=model layers only, lower=full PE2   |   " +
                   "BLACK: full PE2   |   moving=full PE2"
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        panel.AddChild(label);
        layer.AddChild(panel);
    }

    private static void PrintImportedMaterialDiagnostics()
    {
        var model = War3RuntimeAssets.InstantiateModel(MortarSource, 0);
        var stack = new Stack<Node>();
        stack.Push(model);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                for (var surface = 0;
                     surface < mesh.Mesh.GetSurfaceCount();
                     surface++)
                {
                    if (mesh.GetActiveMaterial(surface) is not
                        StandardMaterial3D material)
                        continue;
                    GD.Print(
                        $"WAR3_EFFECT_VALIDATION_MATERIAL " +
                        $"mesh={mesh.Name} surface={surface} " +
                        $"name={material.ResourceName} " +
                        $"transparency={material.Transparency} " +
                        $"blend={material.BlendMode} " +
                        $"shading={material.ShadingMode} " +
                        $"texture={(material.AlbedoTexture is null ? "none" : "yes")}");
                }
            }
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
        model.Free();
    }

    private async Task CaptureAndQuitAsync()
    {
        await ToSignal(RenderingServer.Singleton,
            RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath(
            "user://war3_effect_validation.png");
        var result = image.SavePng(path);
        var viewportRid = GetViewport().GetViewportRid();
        var shadowObjects = RenderingServer.ViewportGetRenderInfo(
            viewportRid,
            RenderingServer.ViewportRenderInfoType.Shadow,
            RenderingServer.ViewportRenderInfo.ObjectsInFrame);
        var shadowDrawCalls = RenderingServer.ViewportGetRenderInfo(
            viewportRid,
            RenderingServer.ViewportRenderInfoType.Shadow,
            RenderingServer.ViewportRenderInfo.DrawCallsInFrame);
        var shadowPrimitives = RenderingServer.ViewportGetRenderInfo(
            viewportRid,
            RenderingServer.ViewportRenderInfoType.Shadow,
            RenderingServer.ViewportRenderInfo.PrimitivesInFrame);
        GD.Print(
            $"WAR3_EFFECT_VALIDATION_CAPTURE {result}: {path} " +
            $"particles={_mortars.Sum(value => value.LiveEffectCount)} " +
            $"shadow={shadowObjects}/{shadowDrawCalls}/{shadowPrimitives}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
