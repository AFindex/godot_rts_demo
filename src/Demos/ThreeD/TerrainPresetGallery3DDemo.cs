using Godot;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Scenarios;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Presentation-only browser and asset generator for the reusable terrain
/// preset catalog. Navigation assertions remain in the pure C# self-test.
/// </summary>
public partial class TerrainPresetGallery3DDemo : Node3D
{
    private const int PresetDisplayTicks = 120;
    private PresetView[] _views = [];
    private Node3D? _content;
    private Camera3D? _camera;
    private Label? _status;
    private int _index;
    private int _ticks;
    private bool _recording;

    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        if (arguments.Contains("--generate-terrain-preset-assets"))
        {
            GenerateAssets();
            return;
        }

        _recording = arguments.Contains("--terrain-presets-recording");
        _views = LoadViews();
        CreateLighting();
        CreateCamera();
        CreateOverlay();
        ShowPreset(0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_recording || _views.Length == 0) return;
        _ticks++;
        if (_ticks % PresetDisplayTicks == 0)
        {
            var next = _ticks / PresetDisplayTicks;
            if (next >= _views.Length)
            {
                FinishRecording();
                return;
            }
            ShowPreset(next);
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key ||
            _views.Length == 0)
        {
            return;
        }
        if (key.Keycode is Key.Right or Key.Space)
            ShowPreset((_index + 1) % _views.Length);
        else if (key.Keycode == Key.Left)
            ShowPreset((_index - 1 + _views.Length) % _views.Length);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    private static void GenerateAssets()
    {
        const string directory = "res://test_resources/terrain_presets";
        var absolute = ProjectSettings.GlobalizePath(directory);
        var directoryError = DirAccess.MakeDirRecursiveAbsolute(absolute);
        if (directoryError != Error.Ok && directoryError != Error.AlreadyExists)
        {
            GD.PrintErr($"RTS_TERRAIN_PRESET_GENERATE_FAIL directory={directoryError}");
            Engine.GetMainLoop().CallDeferred(SceneTree.MethodName.Quit, 1);
            return;
        }

        var saved = 0;
        foreach (var preset in TerrainTestPresetCatalog.Presets)
        {
            var resource = TerrainMapResourceConverter.FromSnapshot(
                preset.Terrain);
            var error = ResourceSaver.Save(resource, preset.ResourcePath);
            if (error != Error.Ok)
            {
                GD.PrintErr(
                    $"RTS_TERRAIN_PRESET_GENERATE_FAIL id={preset.Id} error={error}");
                Engine.GetMainLoop().CallDeferred(SceneTree.MethodName.Quit, 1);
                return;
            }
            saved++;
        }
        GD.Print(
            $"RTS_TERRAIN_PRESET_GENERATE_PASS saved={saved} directory={directory}");
        Engine.GetMainLoop().CallDeferred(SceneTree.MethodName.Quit, 0);
    }

    private static PresetView[] LoadViews()
    {
        var result = new List<PresetView>();
        foreach (var preset in TerrainTestPresetCatalog.Presets)
        {
            if (!TerrainMapResourceConverter.TryLoadSnapshot(
                    preset.ResourcePath,
                    out var terrain,
                    out var validation) || terrain is null)
            {
                throw new InvalidOperationException(
                    $"Terrain preset asset {preset.Id} failed: " +
                    $"{validation.FirstError}.");
            }
            if (terrain.StableHash != preset.Terrain.StableHash)
                throw new InvalidOperationException(
                    $"Terrain preset asset {preset.Id} is stale.");
            var navigation = EmptyNavigation(terrain.Bounds);
            var clearance = ClearanceBakeSnapshot.Build(
                navigation, terrain, cellSize: 8f);
            var topology = TerrainNavigationTopologyBuilder.Build(
                terrain, clearance);
            result.Add(new PresetView(preset, terrain, topology));
        }
        return result.ToArray();
    }

    private void ShowPreset(int index)
    {
        _index = index;
        _content?.QueueFree();
        var view = _views[index];
        _content = new Node3D { Name = $"Preset_{view.Preset.Id}" };
        AddChild(_content);
        var terrain = new Rts3DTerrainPresenter { Name = "Terrain" };
        _content.AddChild(terrain);
        terrain.Initialize(view.Terrain);
        AddPointMarker(
            _content, view.Terrain, view.Preset.Start,
            "START", new Color("42a5f5"));
        AddPointMarker(
            _content, view.Terrain, view.Preset.Goal,
            "GOAL", new Color("65d26e"));
        AddAreaMarker(
            _content,
            view.Terrain,
            view.Preset.BuildingProbe,
            view.Preset.BuildingProbeBuildable
                ? new Color(0.2f, 0.85f, 0.35f, 0.38f)
                : new Color(0.95f, 0.18f, 0.16f, 0.42f),
            "BUILD PROBE");
        if (view.Preset.DynamicBlocker is { } blocker)
        {
            AddAreaMarker(
                _content,
                view.Terrain,
                blocker,
                new Color(1f, 0.55f, 0.12f, 0.48f),
                "DYNAMIC BLOCKER");
        }
        FrameCamera(view.Terrain);
        UpdateOverlay(view);
    }

    private static void AddPointMarker(
        Node parent,
        TerrainMapSnapshot terrain,
        NVector2 position,
        string text,
        Color color)
    {
        var height = SimPlane3DTransform.ToWorldLength(
            terrain.HeightAt(position));
        var marker = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.18f,
                BottomRadius = 0.18f,
                Height = 0.35f
            },
            Position = SimPlane3DTransform.ToWorld(position, height + 0.2f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                EmissionEnabled = true,
                Emission = color * 0.55f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        };
        parent.AddChild(marker);
        var label = new Label3D
        {
            Text = text,
            Position = marker.Position + new Vector3(0f, 0.55f, 0f),
            FontSize = 28,
            Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true
        };
        parent.AddChild(label);
    }

    private static void AddAreaMarker(
        Node parent,
        TerrainMapSnapshot terrain,
        SimRect bounds,
        Color color,
        string text)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var height = SimPlane3DTransform.ToWorldLength(terrain.HeightAt(center));
        var size = SimPlane3DTransform.ToWorldSize(bounds.Max - bounds.Min);
        var marker = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(size.X, 0.08f, size.Y)
            },
            Position = SimPlane3DTransform.ToWorld(center, height + 0.08f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        };
        parent.AddChild(marker);
        var label = new Label3D
        {
            Text = text,
            Position = marker.Position + new Vector3(0f, 0.25f, 0f),
            FontSize = 22,
            Modulate = color with { A = 1f },
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true
        };
        parent.AddChild(label);
    }

    private void FrameCamera(TerrainMapSnapshot terrain)
    {
        if (_camera is null) return;
        var worldWidth = SimPlane3DTransform.ToWorldLength(
            terrain.Bounds.Width);
        var worldDepth = SimPlane3DTransform.ToWorldLength(
            terrain.Bounds.Height);
        var center = new Vector3(worldWidth * 0.5f, 0f, worldDepth * 0.5f);
        var span = MathF.Max(worldWidth, worldDepth);
        _camera.Position = center + new Vector3(
            -span * 0.08f,
            span * 0.92f,
            span * 0.62f);
        _camera.LookAt(center, Vector3.Up);
    }

    private void CreateCamera()
    {
        _camera = new Camera3D
        {
            Name = "GalleryCamera",
            Current = true,
            Fov = 46f,
            Near = 0.1f,
            Far = 300f
        };
        AddChild(_camera);
    }

    private void CreateLighting()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("101923"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("8ea8bd"),
                AmbientLightEnergy = 0.78f
            }
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-64f, -25f, 0f),
            LightColor = new Color("fff0d2"),
            LightEnergy = 1.45f,
            ShadowEnabled = true
        });
    }

    private void CreateOverlay()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new ColorRect
        {
            Position = new Vector2(20f, 18f),
            Size = new Vector2(790f, 166f),
            Color = new Color(0.02f, 0.045f, 0.075f, 0.92f)
        };
        layer.AddChild(panel);
        _status = new Label
        {
            Position = new Vector2(18f, 12f),
            Size = new Vector2(755f, 142f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _status.AddThemeColorOverride("font_color", new Color("d9edf6"));
        _status.AddThemeFontSizeOverride("font_size", 18);
        panel.AddChild(_status);
    }

    private void UpdateOverlay(PresetView view)
    {
        if (_status is null) return;
        var medium = view.Topology.Layer(MovementClass.Medium);
        _status.Text =
            $"TERRAIN TEST PRESETS  {_index + 1}/{_views.Length}  " +
            $"[{view.Preset.Id}]\n" +
            $"{view.Preset.DisplayName} — {view.Preset.Purpose}\n" +
            $"Grid: {view.Terrain.Columns}x{view.Terrain.Rows}   " +
            $"Levels: {view.Terrain.MaximumCellLevel + 1}   " +
            $"Ramps: {view.Topology.Ramps.Length}   " +
            $"Medium regions: {medium.RegionCount}   " +
            $"Connected: {view.Preset.StartGoalConnected}\n" +
            $"Tags: {view.Preset.Tags}   Hash: {view.Terrain.StableHashText}   " +
            "←/→ browse";
    }

    private void FinishRecording()
    {
        _recording = false;
        var valid = _views.Length == 12 &&
                    _views.All(view =>
                        view.Topology.Ramps.Length ==
                        view.Preset.ExpectedRampCount) &&
                    _views.Select(view => view.Terrain.StableHash)
                        .Distinct().Count() == _views.Length;
        GD.Print($"RTS_TERRAIN_PRESETS_DEMO_{(valid ? "PASS" : "FAIL")} " +
                 $"presets={_views.Length}, " +
                 $"ramps={_views.Sum(view => view.Topology.Ramps.Length)}, " +
                 $"assets={_views.Count(view => ResourceLoader.Exists(view.Preset.ResourcePath))}, " +
                 $"hashes={_views.Select(view => view.Terrain.StableHash).Distinct().Count()}");
        GetTree().Quit(valid ? 0 : 1);
    }

    private static NavigationMapSnapshot EmptyNavigation(SimRect bounds)
    {
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                bounds,
                [], [], [], [],
                out var navigation,
                out var validation) || navigation is null)
        {
            throw new InvalidOperationException(
                $"Gallery navigation failed: {validation.FirstError}.");
        }
        return navigation;
    }

    private sealed record PresetView(
        TerrainTestPreset Preset,
        TerrainMapSnapshot Terrain,
        TerrainNavigationTopologySnapshot Topology);
}
