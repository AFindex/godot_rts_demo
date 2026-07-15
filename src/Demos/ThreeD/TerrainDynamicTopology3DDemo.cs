using Godot;
using RtsDemo.Scenarios;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Observable production-stack demonstration of building-driven route
/// changes. The node only presents and schedules public scenario actions.
/// </summary>
public partial class TerrainDynamicTopology3DDemo : Node3D
{
    private const int RemoveBlockerTick = 420;
    private const int VerificationTick = 1_200;
    private TerrainDynamicTopologyRuntime? _runtime;
    private Rts3DWorldPresenter? _worldPresenter;
    private MeshInstance3D? _blockerMarker;
    private Label? _status;
    private DynamicFootprintId _blockerId;
    private int[] _blockedWave = [];
    private int[] _openWave = [];
    private int[] _blockedRoute = [];
    private int[] _openRoute = [];
    private bool _removed;
    private bool _sawFarRamp;
    private bool _sawNearRamp;
    private bool _illegalCliffCrossing;
    private float _minimumPairDistance = float.PositiveInfinity;
    private bool _recording;

    public override void _Ready()
    {
        _runtime = TerrainDynamicTopologyScenario.Prepare();
        _recording = OS.GetCmdlineUserArgs().Contains(
            "--terrain-dynamic-demo-recording");
        CreateLighting();
        var terrain = new Rts3DTerrainPresenter { Name = "Terrain" };
        AddChild(terrain);
        terrain.Initialize(_runtime.Terrain);
        _worldPresenter = new Rts3DWorldPresenter { Name = "World" };
        AddChild(_worldPresenter);
        _worldPresenter.Initialize(_runtime.Simulation, _runtime.Terrain);
        _worldPresenter.DebugMovementEnabled = true;

        var placement = _runtime.Simulation.TryPlaceBuilding(
            TerrainDynamicTopologyScenario.Footprint(
                BuildingFootprintClass.Large),
            new BuildingPlacementRules(
                MovementClass.Medium,
                PreserveConnectivity: true));
        if (!placement.Succeeded)
            throw new InvalidOperationException(
                $"Dynamic terrain blocker placement failed: {placement.Code}.");
        _blockerId = placement.FootprintId;
        CreateBlockerMarker();
        _blockedWave = TerrainDynamicTopologyScenario.SpawnWave(
            _runtime.Simulation, 3);
        _blockedRoute = _runtime.Simulation.LastIssuedGroupRoute.ChokeIds;
        _worldPresenter.SetSelection(_blockedWave, []);
        CreateCamera();
        CreateOverlay();
        UpdateOverlay();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_runtime is null) return;
        _runtime.Simulation.Tick((float)Math.Min(delta, 0.05));
        if (!_removed &&
            _runtime.Simulation.Metrics.Tick >= RemoveBlockerTick)
        {
            _removed = _runtime.Simulation.RemoveBuilding(_blockerId);
            if (!_removed)
                throw new InvalidOperationException(
                    "Dynamic terrain blocker removal failed.");
            _blockerMarker?.QueueFree();
            _blockerMarker = null;
            _openWave = TerrainDynamicTopologyScenario.SpawnWave(
                _runtime.Simulation, 3, verticalOffset: 24f);
            _openRoute = _runtime.Simulation.LastIssuedGroupRoute.ChokeIds;
            _worldPresenter?.SetSelection(
                _blockedWave.Concat(_openWave).ToArray(), []);
        }
        Observe();
        UpdateOverlay();
        if (_recording &&
            _runtime.Simulation.Metrics.Tick >= VerificationTick)
        {
            FinishRecording();
        }
    }

    public override void _Process(double delta) =>
        _worldPresenter?.Sync((float)Engine.GetPhysicsInterpolationFraction());

    private void Observe()
    {
        if (_runtime is null) return;
        var store = _runtime.Simulation.Units;
        _sawFarRamp |= _blockedWave.Any(unit =>
            store.ActiveChokeIds[unit] == 1);
        _sawNearRamp |= _openWave.Any(unit =>
            store.ActiveChokeIds[unit] == 0);
        var all = _blockedWave.Concat(_openWave).ToArray();
        for (var firstIndex = 0; firstIndex < all.Length; firstIndex++)
        {
            var position = store.Positions[all[firstIndex]];
            var cliffStart = TerrainDynamicTopologyScenario.TransitionColumn *
                             TerrainDynamicTopologyScenario.CellSize;
            var cliffEnd = cliffStart +
                           TerrainDynamicTopologyScenario.CellSize;
            if (position.X >= cliffStart - 4f &&
                position.X <= cliffEnd + 4f &&
                !InsideEitherRamp(position, 20f))
            {
                _illegalCliffCrossing = true;
            }
            for (var secondIndex = firstIndex + 1;
                 secondIndex < all.Length;
                 secondIndex++)
            {
                _minimumPairDistance = MathF.Min(
                    _minimumPairDistance,
                    NVector2.Distance(
                        position, store.Positions[all[secondIndex]]));
            }
        }
    }

    private static bool InsideEitherRamp(NVector2 position, float margin)
    {
        var cell = TerrainDynamicTopologyScenario.CellSize;
        var near = position.Y >=
                   TerrainDynamicTopologyScenario.NearRampFirstRow * cell - margin &&
                   position.Y <=
                   (TerrainDynamicTopologyScenario.NearRampLastRow + 1) * cell + margin;
        var far = position.Y >=
                  TerrainDynamicTopologyScenario.FarRampFirstRow * cell - margin &&
                  position.Y <=
                  (TerrainDynamicTopologyScenario.FarRampLastRow + 1) * cell + margin;
        return near || far;
    }

    private void CreateBlockerMarker()
    {
        var bounds = TerrainDynamicTopologyScenario.Footprint(
            BuildingFootprintClass.Large);
        var size = SimPlane3DTransform.ToWorldSize(bounds.Max - bounds.Min);
        _blockerMarker = new MeshInstance3D
        {
            Name = "LargeBuildingBlocker",
            Mesh = new BoxMesh
            {
                Size = new Vector3(size.X, 1.7f, size.Y)
            },
            Position = SimPlane3DTransform.ToWorld(
                (bounds.Min + bounds.Max) * 0.5f, 0.9f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("d46a3a"),
                Metallic = 0.15f,
                Roughness = 0.7f
            }
        };
        AddChild(_blockerMarker);
    }

    private void CreateCamera()
    {
        var camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Fov = 47f,
            Near = 0.1f,
            Far = 120f,
            Position = new Vector3(7.1f, 18.5f, 18.2f)
        };
        AddChild(camera);
        camera.LookAt(new Vector3(7.1f, 0f, 6.3f), Vector3.Up);
    }

    private void CreateLighting()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("111923"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("8fa7bc"),
                AmbientLightEnergy = 0.8f
            }
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-62f, -28f, 0f),
            LightColor = new Color("fff0cf"),
            LightEnergy = 1.4f,
            ShadowEnabled = true
        });
    }

    private void CreateOverlay()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new ColorRect
        {
            Position = new Vector2(22f, 20f),
            Size = new Vector2(710f, 142f),
            Color = new Color(0.025f, 0.05f, 0.08f, 0.9f)
        };
        layer.AddChild(panel);
        _status = new Label
        {
            Position = new Vector2(18f, 12f),
            Size = new Vector2(680f, 118f)
        };
        _status.AddThemeColorOverride("font_color", new Color("d8edf6"));
        _status.AddThemeFontSizeOverride("font_size", 18);
        panel.AddChild(_status);
    }

    private void UpdateOverlay()
    {
        if (_runtime is null || _status is null) return;
        _status.Text =
            "DYNAMIC BUILDING / PARALLEL RAMP ROUTING\n" +
            $"Phase: {(_removed ? "BUILDING REMOVED" : "LARGE BUILDING BLOCKS NEAR RAMP")}   " +
            $"Tick: {_runtime.Simulation.Metrics.Tick}/{VerificationTick}\n" +
            $"Blocked wave: {CountArrived(_blockedWave)}/{_blockedWave.Length} " +
            $"via [{string.Join(',', _blockedRoute)}]   " +
            $"Open wave: {CountArrived(_openWave)}/{_openWave.Length} " +
            $"via [{string.Join(',', _openRoute)}]\n" +
            $"Incremental updates: {_runtime.RoutePlanner.IncrementalUpdates}   " +
            $"Dirty chunks: {_runtime.RoutePlanner.LastDirtyChunkIds.Length}   " +
            $"Resampled: {_runtime.RoutePlanner.LastResampledCells} cells";
    }

    private int CountArrived(int[] units)
    {
        if (_runtime is null) return 0;
        return units.Count(unit =>
            _runtime.Simulation.Units.Modes[unit] is not
                (UnitMoveMode.Moving or UnitMoveMode.WaitingForPath));
    }

    private void FinishRecording()
    {
        if (_runtime is null) return;
        _recording = false;
        var blockedArrived = CountArrived(_blockedWave);
        var openArrived = CountArrived(_openWave);
        var routeChanged = _blockedRoute.SequenceEqual([1]) &&
                           _openRoute.SequenceEqual([0]);
        var incremental = _runtime.RoutePlanner.IncrementalUpdates == 2 &&
                          _runtime.RoutePlanner.FullRebuilds == 0 &&
                          _runtime.RoutePlanner.LastDirtyChunkIds.Length > 0 &&
                          _runtime.RoutePlanner.LastResampledCells <
                          _runtime.Clearance.NodeCount * 3;
        var passed = _removed && routeChanged && incremental &&
                     _sawFarRamp && _sawNearRamp &&
                     !_illegalCliffCrossing &&
                     blockedArrived == _blockedWave.Length &&
                     openArrived == _openWave.Length &&
                     _minimumPairDistance >= 15.9f;
        GD.Print($"RTS_TERRAIN_DYNAMIC_DEMO_{(passed ? "PASS" : "FAIL")} " +
                 $"ticks={_runtime.Simulation.Metrics.Tick}, " +
                 $"routes={string.Join(',', _blockedRoute)}>" +
                 $"{string.Join(',', _openRoute)}, " +
                 $"arrived={blockedArrived}/{_blockedWave.Length}+" +
                 $"{openArrived}/{_openWave.Length}, " +
                 $"seen={_sawFarRamp}/{_sawNearRamp}, " +
                 $"illegalCliff={_illegalCliffCrossing}, " +
                 $"updates={_runtime.RoutePlanner.IncrementalUpdates}, " +
                 $"full={_runtime.RoutePlanner.FullRebuilds}, " +
                 $"chunks={_runtime.RoutePlanner.LastDirtyChunkIds.Length}, " +
                 $"resampled={_runtime.RoutePlanner.LastResampledCells}, " +
                 $"minPair={_minimumPairDistance:F2}, " +
                 $"blockedState={UnitState(_blockedWave)}, " +
                 $"openState={UnitState(_openWave)}");
        GetTree().Quit(passed ? 0 : 1);
    }

    private string UnitState(int[] units)
    {
        if (_runtime is null) return "none";
        var store = _runtime.Simulation.Units;
        return string.Join(';', units.Select(unit =>
            $"u{unit}@{store.Positions[unit].X:F0}," +
            $"{store.Positions[unit].Y:F0}/{store.Modes[unit]}/" +
            $"c{store.ActiveChokeIds[unit]}/" +
            $"{store.ChokePhases[unit]}"));
    }
}
