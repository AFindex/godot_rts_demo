using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;

namespace RtsDemo.Demos.War3;

/// <summary>
/// Visual acceptance scene for the Warcraft III material adapter running on
/// the existing SC2-style authoritative terrain and topology data.
/// </summary>
public partial class War3TerrainShowcase3D : Node3D
{
    private const string TerrainPath = "res://data/demo_playable_terrain.tres";
    private const string VisualLayerPath =
        "res://data/demo_war3_terrain_visual_layers.tres";
    private TerrainMapSnapshot? _terrain;
    private TerrainVisualLayerMap? _visualLayerMap;
    private Rts3DTerrainPresenter? _presenter;
    private Rts3DCameraController? _cameraController;
    private Label? _modeLabel;
    private War3TerrainBlendStyle _blendStyle = War3TerrainBlendStyle.DualGrid;
    private bool _capture;

    public override void _Ready()
    {
        GD.Print("WAR3_TERRAIN_STAGE begin");
        var arguments = OS.GetCmdlineUserArgs();
        _capture = arguments.Contains("--war3-terrain-capture") ||
                   arguments.Contains("--war3-terrain-continuous-capture");
        if (arguments.Contains("--war3-terrain-continuous-capture"))
            _blendStyle = War3TerrainBlendStyle.ContinuousWeights;
        if (!TerrainMapResourceConverter.TryLoadSnapshot(
                TerrainPath, out _terrain, out var validation) ||
            _terrain is null)
        {
            throw new InvalidOperationException(
                $"War3 terrain showcase failed to load {TerrainPath}: " +
                validation.FirstError);
        }

        var missing = War3TerrainMaterialSet.MissingAssetPaths();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                "War3 terrain showcase is missing: " + string.Join(", ", missing));

        var generateVisualResource = arguments.Contains(
            "--generate-war3-terrain-visual-resource");
        if (generateVisualResource)
        {
            _visualLayerMap = TerrainVisualLayerMap.FromTerrain(
                _terrain,
                new War3TerrainMaterialSet(War3TerrainBlendStyle.DualGrid));
        }
        else if (TerrainVisualLayerResourceConverter.TryLoad(
                     VisualLayerPath, _terrain,
                     out _visualLayerMap, out var visualValidation) &&
                 _visualLayerMap is not null)
        {
            GD.Print(
                $"WAR3_TERRAIN_VISUAL_RESOURCE loaded={VisualLayerPath} " +
                $"visual={_visualLayerMap.StableHashText}");
        }
        else if (visualValidation.FirstError ==
                 TerrainVisualLayerErrorCode.MissingResourceAsset)
        {
            _visualLayerMap = TerrainVisualLayerMap.FromTerrain(
                _terrain,
                new War3TerrainMaterialSet(War3TerrainBlendStyle.DualGrid));
            GD.Print(
                "WAR3_TERRAIN_VISUAL_RESOURCE fallback=surface-migration " +
                $"visual={_visualLayerMap.StableHashText}");
        }
        else
        {
            throw new InvalidOperationException(
                $"War3 terrain visual layer failed validation: " +
                visualValidation.FirstError);
        }

        if (generateVisualResource)
        {
            var error = ResourceSaver.Save(
                TerrainVisualLayerResourceConverter.FromMap(_visualLayerMap),
                VisualLayerPath);
            GD.Print(
                $"WAR3_TERRAIN_VISUAL_RESOURCE_GENERATED error={error} " +
                $"path={VisualLayerPath} visual={_visualLayerMap.StableHashText}");
            GetTree().Quit(error == Error.Ok ? 0 : 1);
            return;
        }

        GD.Print("WAR3_TERRAIN_STAGE assets-ready");
        CreateEnvironment();
        _presenter = new Rts3DTerrainPresenter { Name = "War3Terrain" };
        AddChild(_presenter);
        GD.Print("WAR3_TERRAIN_STAGE mesh-start");
        ApplyBlendStyle();
        GD.Print("WAR3_TERRAIN_STAGE mesh-ready");
        CreateCamera(_terrain);
        CreateOverlay(_terrain);

        GD.Print(
            $"WAR3_TERRAIN_READY terrain={_terrain.StableHashText} " +
            $"surfaces={_terrain.Surfaces.Length} assets={War3TerrainMaterialSet.AssetPaths.Count} " +
            $"theme=LordaeronSummer blend={_blendStyle} " +
            "cliff=UpperSurfaceAdaptive water=Water00");

        if (_capture)
            _ = CaptureAsync();
        else if (OS.GetCmdlineUserArgs().Contains("--war3-terrain-smoke"))
        {
            GD.Print("WAR3_TERRAIN_SMOKE success=True");
            GetTree().Quit(0);
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode is Key.Home or Key.R)
        {
            _cameraController?.ResetView();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode != Key.Escape) return;
        GetTree().ChangeSceneToFile("res://Main.tscn");
        GetViewport().SetInputAsHandled();
    }

    private void CreateEnvironment()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("7894aa"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b9c6cd"),
                AmbientLightEnergy = 0.72f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        });
        AddChild(new DirectionalLight3D
        {
            Name = "War3Sun",
            RotationDegrees = new Vector3(-58f, -32f, 0f),
            LightColor = new Color("ffe2ad"),
            LightEnergy = 1.45f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 160f
        });
    }

    private void CreateCamera(TerrainMapSnapshot terrain)
    {
        var span = MathF.Max(
            SimPlane3DTransform.ToWorldLength(terrain.Bounds.Width),
            SimPlane3DTransform.ToWorldLength(terrain.Bounds.Height));
        var camera = new Camera3D
        {
            Name = "War3TerrainCamera",
            Current = true,
            Fov = 43f,
            Near = 0.1f,
            Far = 500f
        };
        AddChild(camera);
        _cameraController = new Rts3DCameraController
        {
            Name = "TerrainCameraController",
            MinimumDistance = MathF.Max(8f, span * 0.16f),
            MaximumDistance = MathF.Max(80f, span * 1.55f),
            InitialDistance = span * 0.92f,
            InitialPitchDegrees = 51f,
            InitialYawDegrees = -27f,
            EdgeScrollMargin = 16f
        };
        AddChild(_cameraController);
        _cameraController.Initialize(camera, terrain.Bounds);
    }

    private void CreateOverlay(TerrainMapSnapshot terrain)
    {
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        var panel = new PanelContainer
        {
            Position = new Vector2(18f, 18f),
            CustomMinimumSize = new Vector2(760f, 218f)
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("071019dd"),
            BorderColor = new Color("b28a43"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8
        });
        layer.AddChild(panel);
        if (_cameraController is not null)
        {
            _cameraController.EdgeScrollBlocked =
                point => panel.GetGlobalRect().HasPoint(point);
        }
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 7);
        margin.AddChild(content);
        var label = new Label
        {
            Text =
                "WARCRAFT III 地形适配专项 · 双网格阶段\n" +
                "Lordaeron Summer 角点控制地表 · 带权随机变化 · Water00 水面\n" +
                $"权威地形 {terrain.Columns}×{terrain.Rows} · {terrain.Surfaces.Length} 种语义表面\n" +
                "WASD/方向键/边缘移动 · 中键拖动 · Alt+中键旋转 · 滚轮缩放\n" +
                "玩法层仍使用现有高度 / 坡道 / 通行 / 建造 / 视野合同"
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", new Color("f1e6cf"));
        content.AddChild(label);
        _modeLabel = new Label();
        _modeLabel.AddThemeFontSizeOverride("font_size", 14);
        _modeLabel.AddThemeColorOverride("font_color", new Color("c9d9bf"));
        content.AddChild(_modeLabel);
        UpdateModeLabel();
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        content.AddChild(actions);
        var reset = new Button
        {
            Text = "镜头归位  Home / R",
            CustomMinimumSize = new Vector2(178f, 30f)
        };
        reset.Pressed += () => _cameraController?.ResetView();
        actions.AddChild(reset);
        var toggle = new Button
        {
            Text = "切换纹理算法",
            CustomMinimumSize = new Vector2(142f, 30f)
        };
        toggle.Pressed += ToggleBlendStyle;
        actions.AddChild(toggle);
        var back = new Button
        {
            Text = "返回  Esc",
            CustomMinimumSize = new Vector2(110f, 30f)
        };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://Main.tscn");
        actions.AddChild(back);
    }

    private void ToggleBlendStyle()
    {
        _blendStyle = _blendStyle == War3TerrainBlendStyle.DualGrid
            ? War3TerrainBlendStyle.ContinuousWeights
            : War3TerrainBlendStyle.DualGrid;
        ApplyBlendStyle();
        UpdateModeLabel();
    }

    private void ApplyBlendStyle()
    {
        if (_terrain is null || _presenter is null) return;
        _presenter.Initialize(
            _terrain,
            new War3TerrainMaterialSet(_blendStyle),
            _blendStyle == War3TerrainBlendStyle.DualGrid
                ? _visualLayerMap
                : null);
        GD.Print($"WAR3_TERRAIN_BLEND_STYLE style={_blendStyle}");
    }

    private void UpdateModeLabel()
    {
        if (_modeLabel is null) return;
        _modeLabel.Text = _blendStyle == War3TerrainBlendStyle.DualGrid
            ? "当前：War3 双网格 · 作者过渡块 + 0–16 带权变化（推荐）"
            : "当前：SC2 风格连续权重 · 邻域混合 + 低频随机扰动（对照）";
    }

    private async Task CaptureAsync()
    {
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
        {
            GD.Print("WAR3_TERRAIN_CAPTURE SKIPPED headless=True");
            GetTree().Quit(0);
            return;
        }
        for (var frame = 0; frame < 120; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var fileName = _blendStyle == War3TerrainBlendStyle.DualGrid
            ? "war3_terrain_dual_grid_capture.png"
            : "war3_terrain_continuous_capture.png";
        var path = ProjectSettings.GlobalizePath("user://" + fileName);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"WAR3_TERRAIN_CAPTURE error={error} path={path}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
