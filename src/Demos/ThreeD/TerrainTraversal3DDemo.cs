using Godot;
using RtsDemo.Scenarios;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Automated and directly launchable terrain contract demo. All movement is
/// produced by RtsSimulation; this node observes results and draws context.
/// </summary>
public partial class TerrainTraversal3DDemo : Node3D
{
    private const int VerificationTicks = 900;
    private TerrainTraversalRuntime? _runtime;
    private Rts3DWorldPresenter? _worldPresenter;
    private Camera3D? _camera;
    private Label? _status;
    private bool _recording;
    private bool _observedRampDetour;
    private bool _crossedCliffOutsideRamp;
    private float _minimumPairDistance = float.PositiveInfinity;

    public override void _Ready()
    {
        _runtime = TerrainTraversalScenario.Prepare();
        _recording = OS.GetCmdlineUserArgs().Contains("--terrain-demo-recording");
        CreateLighting();

        var terrainPresenter = new Rts3DTerrainPresenter { Name = "Terrain" };
        AddChild(terrainPresenter);
        terrainPresenter.Initialize(_runtime.Terrain);

        _worldPresenter = new Rts3DWorldPresenter { Name = "WorldPresenter" };
        AddChild(_worldPresenter);
        _worldPresenter.Initialize(_runtime.Simulation, _runtime.Terrain);
        _worldPresenter.SetSelection(_runtime.Units, []);
        _worldPresenter.DebugMovementEnabled = true;

        CreateWaterPlacementMarker();
        CreateCamera();
        CreateOverlay();
        UpdateOverlay();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_runtime is null) return;
        _runtime.Simulation.Tick((float)Math.Min(delta, 0.05));
        ObserveTerrainContract();
        UpdateOverlay();
        if (_recording && _runtime.Simulation.Metrics.Tick >= VerificationTicks)
            FinishRecording();
    }

    public override void _Process(double delta)
    {
        _worldPresenter?.Sync((float)Engine.GetPhysicsInterpolationFraction());
    }

    private void ObserveTerrainContract()
    {
        if (_runtime is null) return;
        var units = _runtime.Simulation.Units;
        for (var firstIndex = 0; firstIndex < _runtime.Units.Length; firstIndex++)
        {
            var first = _runtime.Units[firstIndex];
            var position = units.Positions[first];
            if (position.Y > TerrainTraversalScenario.RampFirstRow *
                             TerrainTraversalScenario.CellSize - 20f)
            {
                _observedRampDetour = true;
            }
            var cliffStart = TerrainTraversalScenario.RampColumn *
                             TerrainTraversalScenario.CellSize;
            var cliffEnd = cliffStart + TerrainTraversalScenario.CellSize;
            if (position.X >= cliffStart - 4f && position.X <= cliffEnd + 4f &&
                !TerrainTraversalScenario.IsInsideRampCorridor(position, 12f))
            {
                _crossedCliffOutsideRamp = true;
            }
            for (var secondIndex = firstIndex + 1;
                 secondIndex < _runtime.Units.Length;
                 secondIndex++)
            {
                var second = _runtime.Units[secondIndex];
                _minimumPairDistance = MathF.Min(
                    _minimumPairDistance,
                    NVector2.Distance(position, units.Positions[second]));
            }
        }
    }

    private void CreateCamera()
    {
        _camera = new Camera3D
        {
            Name = "TerrainCamera",
            Current = true,
            Fov = 46f,
            Near = 0.1f,
            Far = 180f,
            Position = new Vector3(16.2f, 23f, 25.5f)
        };
        AddChild(_camera);
        _camera.LookAt(new Vector3(16f, 0f, 9f), Vector3.Up);
    }

    private void CreateLighting()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("111a25"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("91a8bd"),
                AmbientLightEnergy = 0.76f
            }
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-62f, -28f, 0f),
            LightColor = new Color("fff0cf"),
            LightEnergy = 1.45f,
            ShadowEnabled = true
        });
    }

    private void CreateWaterPlacementMarker()
    {
        if (_runtime is null) return;
        var footprint = TerrainTraversalScenario.ShallowWaterBuildingFootprint;
        var center = (footprint.Min + footprint.Max) * 0.5f;
        var size = footprint.Max - footprint.Min;
        var worldSize = SimPlane3DTransform.ToWorldSize(size);
        var marker = new MeshInstance3D
        {
            Name = "RejectedBuildingPlacement",
            Mesh = new BoxMesh { Size = new Vector3(worldSize.X, 0.18f, worldSize.Y) },
            Position = SimPlane3DTransform.ToWorld(center, 0.14f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.15f, 0.12f, 0.58f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        };
        AddChild(marker);
    }

    private void CreateOverlay()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new ColorRect
        {
            Position = new Vector2(22f, 20f),
            Size = new Vector2(580f, 122f),
            Color = new Color(0.025f, 0.05f, 0.08f, 0.88f)
        };
        layer.AddChild(panel);
        _status = new Label
        {
            Position = new Vector2(18f, 12f),
            Size = new Vector2(548f, 100f)
        };
        _status.AddThemeColorOverride("font_color", new Color("d8edf6"));
        _status.AddThemeFontSizeOverride("font_size", 18);
        panel.AddChild(_status);
    }

    private void UpdateOverlay()
    {
        if (_runtime is null || _status is null) return;
        var reached = CountReachedHighGround();
        _status.Text =
            "TERRAIN CONTRACT / PRODUCTION PATHING\n" +
            $"Cliff detour: {(_observedRampDetour ? "OBSERVED" : "WAITING")}   " +
            $"Reached plateau: {reached}/{_runtime.Units.Length}\n" +
            $"Shallow water placement: {_runtime.ShallowWaterPlacement.Code}   " +
            $"Nav hash: {_runtime.Navigation.StableHashText}   Terrain: {_runtime.Terrain.StableHashText}";
    }

    private int CountReachedHighGround()
    {
        if (_runtime is null) return 0;
        return _runtime.Units.Count(unit =>
            _runtime.Simulation.Units.Positions[unit].X >= 850f);
    }

    private void FinishRecording()
    {
        if (_runtime is null) return;
        _recording = false;
        var reached = CountReachedHighGround();
        var placementRejected = _runtime.ShallowWaterPlacement.Code ==
                                StaticPlacementCode.TerrainUnbuildable;
        var bakeCompatible = _runtime.Clearance.IsCompatible(
            _runtime.Simulation.World,
            cellSize: 16f,
            navigationRadius: MovementClearance.ForClass(
                MovementClass.Small).NavigationRadius);
        var noOverlap = _minimumPairDistance >= 15.99f;
        var passed = reached == _runtime.Units.Length &&
                     _observedRampDetour &&
                     !_crossedCliffOutsideRamp &&
                     placementRejected &&
                     bakeCompatible &&
                     noOverlap;
        GD.Print($"RTS_TERRAIN_DEMO_{(passed ? "PASS" : "FAIL")} " +
                 $"ticks={_runtime.Simulation.Metrics.Tick}, " +
                 $"reached={reached}/{_runtime.Units.Length}, " +
                 $"detour={_observedRampDetour}, illegalCliff={_crossedCliffOutsideRamp}, " +
                 $"placement={_runtime.ShallowWaterPlacement.Code}, " +
                 $"bakeCompatible={bakeCompatible}, minPair={_minimumPairDistance:F2}");
        GetTree().Quit(passed ? 0 : 1);
    }
}
