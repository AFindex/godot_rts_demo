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
    private War3TerrainShowcasePreset[] _presets = [];
    private int _presetIndex;
    private Rts3DTerrainPresenter? _presenter;
    private Rts3DCameraController? _cameraController;
    private Camera3D? _camera;
    private PanelContainer? _overlayPanel;
    private Label? _summaryLabel;
    private Label? _modeLabel;
    private War3TerrainBlendStyle _blendStyle = War3TerrainBlendStyle.DualGrid;
    private bool _capture;
    private bool _cliffCapture;
    private bool _classicCliffs = true;

    private War3TerrainShowcasePreset CurrentPreset =>
        _presets[_presetIndex];

    public override void _Ready()
    {
        GD.Print("WAR3_TERRAIN_STAGE begin");
        var arguments = OS.GetCmdlineUserArgs();
        _capture = arguments.Contains("--war3-terrain-capture") ||
                   arguments.Contains("--war3-terrain-continuous-capture") ||
                   arguments.Contains("--war3-terrain-cliff-capture") ||
                   arguments.Contains("--war3-terrain-cliff-fallback-capture") ||
                   arguments.Contains("--war3-terrain-stress-capture");
        _cliffCapture = arguments.Contains("--war3-terrain-cliff-capture") ||
                        arguments.Contains("--war3-terrain-cliff-fallback-capture") ||
                        arguments.Contains("--war3-terrain-stress-capture");
        if (arguments.Contains("--war3-terrain-cliff-fallback-capture"))
            _classicCliffs = false;
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

        var baselineTerrain = _terrain;
        var baselineVisual = _visualLayerMap;
        _presets =
        [
            new War3TerrainShowcasePreset(
                "baseline",
                "基准对战地形",
                "原有地形资源，用于确认压力图没有改变基准表现。",
                baselineTerrain,
                baselineVisual),
            .. War3TerrainStressPresetCatalog.Presets.ToArray()
        ];
        var requestedPreset = ReadPresetArgument(arguments);
        if (requestedPreset is null &&
            arguments.Contains("--war3-terrain-stress-capture"))
        {
            requestedPreset = "interlocked-ridges";
        }
        if (requestedPreset is not null)
        {
            var selected = Array.FindIndex(
                _presets,
                preset => string.Equals(
                    preset.Id, requestedPreset,
                    StringComparison.OrdinalIgnoreCase));
            if (selected < 0)
            {
                throw new ArgumentException(
                    $"Unknown War3 terrain preset '{requestedPreset}'.");
            }
            _presetIndex = selected;
        }
        SelectPresetData(_presetIndex);

        GD.Print("WAR3_TERRAIN_STAGE assets-ready");
        CreateEnvironment();
        _presenter = new Rts3DTerrainPresenter { Name = "War3Terrain" };
        AddChild(_presenter);
        GD.Print("WAR3_TERRAIN_STAGE mesh-start");
        ApplyBlendStyle();
        GD.Print("WAR3_TERRAIN_STAGE mesh-ready");
        CreateCamera(_terrain);
        CreateOverlay();
        ConfigureAutomationView(arguments);

        GD.Print(
            $"WAR3_TERRAIN_READY preset={CurrentPreset.Id} " +
            $"terrain={_terrain.StableHashText} " +
            $"surfaces={_terrain.Surfaces.Length} assets={War3TerrainMaterialSet.AssetPaths.Count} " +
            $"theme=LordaeronSummer blend={_blendStyle} " +
            $"cliff={(_classicCliffs ? "ClassicMesh" : "ProceduralFallback")} " +
            "water=Water00");

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
        if (key.Keycode == Key.Pageup)
        {
            ChangePreset(-1);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode == Key.Pagedown)
        {
            ChangePreset(1);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode == Key.F1)
        {
            ToggleOverlay();
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
        _camera = new Camera3D
        {
            Name = "War3TerrainCamera",
            Current = true,
            Fov = 43f,
            Near = 0.1f,
            Far = 500f
        };
        AddChild(_camera);
        _cameraController = new Rts3DCameraController
        {
            Name = "TerrainCameraController",
            InitialPitchDegrees = _cliffCapture ? 43f : 51f,
            InitialYawDegrees = -27f,
            EdgeScrollMargin = 16f
        };
        AddChild(_cameraController);
        ConfigureCameraForTerrain(terrain);
    }

    private void CreateOverlay()
    {
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        _overlayPanel = new PanelContainer
        {
            Position = new Vector2(18f, 18f),
            CustomMinimumSize = new Vector2(850f, 270f)
        };
        _overlayPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
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
        layer.AddChild(_overlayPanel);
        if (_cameraController is not null)
        {
            _cameraController.EdgeScrollBlocked =
                point => _overlayPanel.Visible &&
                         _overlayPanel.GetGlobalRect().HasPoint(point);
        }
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _overlayPanel.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 7);
        margin.AddChild(content);
        _summaryLabel = new Label
        {
            Text = string.Empty
        };
        _summaryLabel.AddThemeFontSizeOverride("font_size", 16);
        _summaryLabel.AddThemeColorOverride("font_color", new Color("f1e6cf"));
        content.AddChild(_summaryLabel);
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
        var cliffToggle = new Button
        {
            Text = "切换悬崖算法",
            CustomMinimumSize = new Vector2(142f, 30f)
        };
        cliffToggle.Pressed += ToggleCliffStyle;
        actions.AddChild(cliffToggle);
        var back = new Button
        {
            Text = "返回  Esc",
            CustomMinimumSize = new Vector2(110f, 30f)
        };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://Main.tscn");
        actions.AddChild(back);

        var presetActions = new HBoxContainer();
        presetActions.AddThemeConstantOverride("separation", 8);
        content.AddChild(presetActions);
        var previous = new Button
        {
            Text = "上一个测试地形  PageUp",
            CustomMinimumSize = new Vector2(205f, 30f)
        };
        previous.Pressed += () => ChangePreset(-1);
        presetActions.AddChild(previous);
        var next = new Button
        {
            Text = "下一个测试地形  PageDown",
            CustomMinimumSize = new Vector2(220f, 30f)
        };
        next.Pressed += () => ChangePreset(1);
        presetActions.AddChild(next);
        var hide = new Button
        {
            Text = "隐藏界面  F1",
            CustomMinimumSize = new Vector2(135f, 30f)
        };
        hide.Pressed += ToggleOverlay;
        presetActions.AddChild(hide);
        UpdateSummaryLabel();
    }

    private static string? ReadPresetArgument(string[] arguments)
    {
        const string prefix = "--war3-terrain-preset=";
        var argument = arguments.FirstOrDefault(value =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return argument is null ? null : argument[prefix.Length..];
    }

    private void ConfigureAutomationView(string[] arguments)
    {
        if (_terrain is null || _cameraController is null)
            return;
        const string focusPrefix = "--war3-terrain-focus-cell=";
        var focusArgument = arguments.FirstOrDefault(value =>
            value.StartsWith(
                focusPrefix, StringComparison.OrdinalIgnoreCase));
        if (focusArgument is not null)
        {
            var coordinates = focusArgument[focusPrefix.Length..].Split(',');
            if (coordinates.Length == 2 &&
                int.TryParse(coordinates[0], out var column) &&
                int.TryParse(coordinates[1], out var row) &&
                (uint)column < (uint)_terrain.Columns &&
                (uint)row < (uint)_terrain.Rows)
            {
                const string distancePrefix =
                    "--war3-terrain-focus-distance=";
                var distanceArgument = arguments.FirstOrDefault(value =>
                    value.StartsWith(
                        distancePrefix, StringComparison.OrdinalIgnoreCase));
                var distance = _cameraController.MinimumDistance;
                if (distanceArgument is not null &&
                    float.TryParse(
                        distanceArgument[distancePrefix.Length..],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsedDistance))
                {
                    distance = parsedDistance;
                }
                const string pitchPrefix =
                    "--war3-terrain-focus-pitch=";
                var pitchArgument = arguments.FirstOrDefault(value =>
                    value.StartsWith(
                        pitchPrefix, StringComparison.OrdinalIgnoreCase));
                var pitch = Mathf.DegToRad(43f);
                if (pitchArgument is not null &&
                    float.TryParse(
                        pitchArgument[pitchPrefix.Length..],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsedPitch))
                {
                    pitch = Mathf.DegToRad(parsedPitch);
                }
                var bounds = _terrain.CellBounds(column, row);
                _cameraController.SetAutomationTarget(
                    (bounds.Min + bounds.Max) * 0.5f,
                    distance,
                    Mathf.DegToRad(-38f),
                    pitch);
            }
        }
        if (arguments.Contains("--war3-terrain-hide-overlay") &&
            _overlayPanel is not null)
        {
            _overlayPanel.Visible = false;
        }
    }

    private void SelectPresetData(int index)
    {
        var preset = _presets[index];
        _terrain = preset.Terrain;
        _visualLayerMap = preset.VisualLayers;
    }

    private void ChangePreset(int offset)
    {
        if (_presets.Length == 0 || _terrain is null) return;
        _presetIndex = (_presetIndex + offset) % _presets.Length;
        if (_presetIndex < 0) _presetIndex += _presets.Length;
        SelectPresetData(_presetIndex);
        ApplyBlendStyle();
        ConfigureCameraForTerrain(_terrain);
        UpdateSummaryLabel();
        UpdateModeLabel();
        GD.Print(
            $"WAR3_TERRAIN_PRESET index={_presetIndex + 1}/{_presets.Length} " +
            $"id={CurrentPreset.Id} terrain={_terrain.StableHashText} " +
            $"visual={_visualLayerMap?.StableHashText}");
    }

    private void ConfigureCameraForTerrain(TerrainMapSnapshot terrain)
    {
        if (_camera is null || _cameraController is null) return;
        var span = MathF.Max(
            SimPlane3DTransform.ToWorldLength(terrain.Bounds.Width),
            SimPlane3DTransform.ToWorldLength(terrain.Bounds.Height));
        _cameraController.MinimumDistance = MathF.Max(8f, span * 0.16f);
        _cameraController.MaximumDistance = MathF.Max(80f, span * 1.55f);
        _cameraController.InitialDistance =
            span * (_cliffCapture ? 0.54f : 0.98f);
        _cameraController.Initialize(_camera, terrain.Bounds);
    }

    private void UpdateSummaryLabel()
    {
        if (_summaryLabel is null || _terrain is null || _presets.Length == 0)
            return;
        _summaryLabel.Text =
            "WARCRAFT III 地形适配专项 · 复杂拓扑压力图\n" +
            $"预设 {_presetIndex + 1}/{_presets.Length}：{CurrentPreset.DisplayName} · " +
            $"{_terrain.Columns}×{_terrain.Rows}\n" +
            CurrentPreset.Purpose + "\n" +
            "WASD/方向键/边缘移动 · 中键拖动 · Alt+中键旋转 · 滚轮缩放\n" +
            "PageUp/PageDown 切换地形 · Home/R 镜头归位 · F1 隐藏/显示界面";
    }

    private void ToggleOverlay()
    {
        if (_overlayPanel is not null)
            _overlayPanel.Visible = !_overlayPanel.Visible;
    }

    private void ToggleBlendStyle()
    {
        _blendStyle = _blendStyle == War3TerrainBlendStyle.DualGrid
            ? War3TerrainBlendStyle.ContinuousWeights
            : War3TerrainBlendStyle.DualGrid;
        ApplyBlendStyle();
        UpdateModeLabel();
    }

    private void ToggleCliffStyle()
    {
        _classicCliffs = !_classicCliffs;
        ApplyBlendStyle();
        UpdateModeLabel();
    }

    private void ApplyBlendStyle()
    {
        if (_terrain is null || _presenter is null) return;
        _presenter.Initialize(
            _terrain,
            new War3TerrainMaterialSet(_blendStyle, _classicCliffs),
            _blendStyle == War3TerrainBlendStyle.DualGrid
                ? _visualLayerMap
                : null);
        GD.Print(
            $"WAR3_TERRAIN_BLEND_STYLE preset={CurrentPreset.Id} " +
            $"style={_blendStyle} " +
            $"cliff={(_classicCliffs ? "ClassicMesh" : "ProceduralFallback")}");
    }

    private void UpdateModeLabel()
    {
        if (_modeLabel is null) return;
        _modeLabel.Text = _blendStyle == War3TerrainBlendStyle.DualGrid
            ? "当前：War3 双网格 · 作者过渡块 + 0–16 带权变化"
            : "当前：SC2 风格连续权重 · 邻域混合 + 低频随机扰动";
        _modeLabel.Text += _classicCliffs
            ? " · 经典 CliffsABCDn 模型（推荐）"
            : " · 程序化崖壁回退（对照）";
        if (_terrain is { HasFineHeight: true } fineTerrain)
        {
            _modeLabel.Text +=
                $" · 权威细高度 {fineTerrain.MinimumFineHeight:0.0}…" +
                $"{fineTerrain.MaximumFineHeight:0.0}";
        }
        if (_classicCliffs &&
            _presenter?.LastClassicCliffDiagnostics is { } diagnostics)
        {
            _modeLabel.Text +=
                $" · {diagnostics.SelectedTiles} 块，" +
                $"{diagnostics.RampFallbackTiles + diagnostics.UnsupportedHeightTiles + diagnostics.MissingAssetTiles} 回退";
        }
        if (_classicCliffs &&
            _presenter?.LastClassicRampDiagnostics is { } rampDiagnostics &&
            rampDiagnostics.AuthoredRampCells > 0)
        {
            _modeLabel.Text +=
                $" · CliffTrans {rampDiagnostics.SelectedTransitions}/" +
                $"{rampDiagnostics.CandidateTransitions}";
        }
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
        var fileName = _cliffCapture
            ? _classicCliffs
                ? "war3_terrain_cliff_capture.png"
                : "war3_terrain_cliff_fallback_capture.png"
            : _blendStyle == War3TerrainBlendStyle.DualGrid
            ? "war3_terrain_dual_grid_capture.png"
            : "war3_terrain_continuous_capture.png";
        if (!string.Equals(
                CurrentPreset.Id, "baseline",
                StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"war3_terrain_{CurrentPreset.Id}_" +
                       $"{(_classicCliffs ? "classic" : "fallback")}_" +
                       $"{(_blendStyle == War3TerrainBlendStyle.DualGrid ? "dual" : "continuous")}.png";
        }
        var path = ProjectSettings.GlobalizePath("user://" + fileName);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"WAR3_TERRAIN_CAPTURE error={error} path={path}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
