using Godot;
using RtsDemo.Scenarios;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

public partial class TerrainVisionCombat3DDemo : Node3D
{
    private const int VerificationTicks = 900;
    private TerrainVisionEncounterRuntime? _runtime;
    private Rts3DWorldPresenter? _presenter;
    private Label? _status;
    private bool _recording;
    private bool _initialHighGroundHidden;
    private bool _initialAttackRejected;
    private bool _elevatedVision;
    private bool _projectileLaunched;
    private bool _lostDuringFlight;
    private bool _impactAfterLoss;
    private bool _sharedVisionRestored;
    private bool _smokeInitiallyHidden;
    private bool _smokeElevatedVisible;
    private float _healthAtLoss;
    private bool _sentElevatedScout;
    private bool _sentScoutAway;
    private bool _sentAllyScout;
    private bool _sentScoutToSmoke;

    public override void _Ready()
    {
        _runtime = TerrainVisionEncounterScenario.Prepare();
        _recording = OS.GetCmdlineUserArgs().Contains(
            "--terrain-vision-demo-recording");
        CreateLighting();
        var terrain = new Rts3DTerrainPresenter { Name = "Terrain" };
        AddChild(terrain);
        terrain.Initialize(_runtime.Terrain);
        _presenter = new Rts3DWorldPresenter
        {
            Name = "WorldPresenter",
            ViewerPlayerId = TerrainVisionEncounterScenario.PlayerId
        };
        AddChild(_presenter);
        _presenter.Initialize(_runtime.Simulation, _runtime.Terrain);
        _presenter.SetSelection(
            _runtime.Attackers.Append(_runtime.ElevatedScout), []);
        CreateSmokeVolume();
        CreateCamera();
        CreateOverlay();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_runtime is null) return;
        _runtime.Simulation.Tick((float)Math.Min(delta, 0.05));
        RunTimeline();
        UpdateOverlay();
        if (_recording && _runtime.Simulation.Metrics.Tick >= VerificationTicks)
            FinishRecording();
    }

    public override void _Process(double delta) =>
        _presenter?.Sync((float)Engine.GetPhysicsInterpolationFraction());

    private void RunTimeline()
    {
        if (_runtime is null) return;
        var simulation = _runtime.Simulation;
        var tick = simulation.Metrics.Tick;
        var primaryTarget = _runtime.Defenders[0];
        if (tick == 2)
        {
            _initialHighGroundHidden = !IsUnitVisible(primaryTarget);
            _smokeInitiallyHidden = !IsUnitVisible(_runtime.SmokeTarget);
            _initialAttackRejected = simulation.IssuePlayerSmartCommand(
                TerrainVisionEncounterScenario.PlayerId,
                _runtime.Attackers,
                new SmartCommandTarget(
                    SmartCommandTargetKind.EnemyUnit,
                    simulation.Units.Positions[primaryTarget],
                    primaryTarget),
                attackMoveModifier: false).Code ==
                PlayerOrderCommandCode.TargetNotVisible;
        }
        if (tick >= 70 && !_sentElevatedScout)
        {
            _sentElevatedScout = true;
            simulation.IssueMove(
                [_runtime.ElevatedScout], new NVector2(330f, 205f));
        }
        if (!_elevatedVision && IsUnitVisible(primaryTarget))
        {
            _elevatedVision = true;
            simulation.IssuePlayerSmartCommand(
                TerrainVisionEncounterScenario.PlayerId,
                _runtime.Attackers,
                new SmartCommandTarget(
                    SmartCommandTargetKind.EnemyUnit,
                    simulation.Units.Positions[primaryTarget],
                    primaryTarget),
                attackMoveModifier: false);
        }
        if (!_projectileLaunched &&
            simulation.CombatProjectiles.ActiveCount > 0)
        {
            _projectileLaunched = true;
            _sentScoutAway = true;
            simulation.IssueMove(
                [_runtime.ElevatedScout], new NVector2(900f, 510f));
        }
        if (_projectileLaunched && !_lostDuringFlight &&
            simulation.CombatProjectiles.ActiveCount > 0 &&
            !IsUnitVisible(primaryTarget))
        {
            _lostDuringFlight = true;
            _healthAtLoss = simulation.Combat.Health[primaryTarget];
        }
        if (_lostDuringFlight && !_impactAfterLoss &&
            simulation.Combat.Health[primaryTarget] < _healthAtLoss)
        {
            _impactAfterLoss = true;
        }
        if (tick >= 470 && !_sentAllyScout)
        {
            _sentAllyScout = true;
            simulation.IssueMove(
                [_runtime.AlliedScout], new NVector2(560f, 210f));
        }
        if (_sentAllyScout && !_sharedVisionRestored &&
            simulation.Units.Positions[_runtime.AlliedScout].X < 650f &&
            IsUnitVisible(primaryTarget))
        {
            _sharedVisionRestored = true;
        }
        if (tick >= 690 && !_sentScoutToSmoke)
        {
            _sentScoutToSmoke = true;
            simulation.IssueMove(
                [_runtime.ElevatedScout], new NVector2(70f, 440f));
        }
        if (_sentScoutToSmoke && IsUnitVisible(_runtime.SmokeTarget))
            _smokeElevatedVisible = true;
    }

    private bool IsUnitVisible(int unit) => _runtime is not null &&
        _runtime.Simulation.Visibility.IsUnitVisible(
            TerrainVisionEncounterScenario.PlayerId,
            unit,
            _runtime.Simulation.Units,
            _runtime.Simulation.Combat);

    private void CreateCamera()
    {
        var camera = new Camera3D
        {
            Current = true,
            Fov = 46f,
            Near = 0.1f,
            Far = 180f,
            Position = new Vector3(12f, 22f, 21f)
        };
        AddChild(camera);
        camera.LookAt(new Vector3(12f, 0f, 7f), Vector3.Up);
    }

    private void CreateLighting()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("101924"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("93a9bc"),
                AmbientLightEnergy = 0.78f
            }
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-60f, -32f, 0f),
            LightColor = new Color("fff0d2"),
            LightEnergy = 1.4f,
            ShadowEnabled = true
        });
    }

    private void CreateSmokeVolume()
    {
        var bounds = TerrainVisionEncounterScenario.SmokeBounds;
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var size = SimPlane3DTransform.ToWorldSize(bounds.Max - bounds.Min);
        AddChild(new MeshInstance3D
        {
            Name = "ObstructingTerrainVolume",
            Mesh = new BoxMesh { Size = new Vector3(size.X, 0.5f, size.Y) },
            Position = SimPlane3DTransform.ToWorld(center, 0.27f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.35f, 0.52f, 0.60f, 0.34f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        });
    }

    private void CreateOverlay()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new ColorRect
        {
            Position = new Vector2(22f, 20f),
            Size = new Vector2(670f, 146f),
            Color = new Color(0.025f, 0.05f, 0.08f, 0.90f)
        };
        layer.AddChild(panel);
        _status = new Label
        {
            Position = new Vector2(18f, 12f),
            Size = new Vector2(640f, 122f)
        };
        _status.AddThemeColorOverride("font_color", new Color("d8edf6"));
        _status.AddThemeFontSizeOverride("font_size", 17);
        panel.AddChild(_status);
    }

    private void UpdateOverlay()
    {
        if (_runtime is null || _status is null) return;
        var target = _runtime.Defenders[0];
        _status.Text =
            "TERRAIN VISION / AUTHORITATIVE COMBAT\n" +
            $"High target visible: {IsUnitVisible(target)}   " +
            $"HP: {_runtime.Simulation.Combat.Health[target]:F0}   " +
            $"Projectiles: {_runtime.Simulation.CombatProjectiles.ActiveCount}\n" +
            $"Low reject={_initialAttackRejected}  Elevated spot={_elevatedVision}  " +
            $"Lost in flight={_lostDuringFlight}  Impact={_impactAfterLoss}\n" +
            $"Shared high-ground sight={_sharedVisionRestored}  " +
            $"Smoke hidden/revealed={_smokeInitiallyHidden}/{_smokeElevatedVisible}";
    }

    private void FinishRecording()
    {
        if (_runtime is null) return;
        _recording = false;
        var passed = _initialHighGroundHidden && _initialAttackRejected &&
                     _elevatedVision && _projectileLaunched &&
                     _lostDuringFlight && _impactAfterLoss &&
                     _sharedVisionRestored && _smokeInitiallyHidden &&
                     _smokeElevatedVisible;
        GD.Print($"RTS_TERRAIN_VISION_DEMO_{(passed ? "PASS" : "FAIL")} " +
                 $"ticks={_runtime.Simulation.Metrics.Tick}, " +
                 $"hidden={_initialHighGroundHidden}, reject={_initialAttackRejected}, " +
                 $"elevated={_elevatedVision}, launched={_projectileLaunched}, " +
                 $"lostFlying={_lostDuringFlight}, impact={_impactAfterLoss}, " +
                 $"shared={_sharedVisionRestored}, " +
                 $"smoke={_smokeInitiallyHidden}/{_smokeElevatedVisible}");
        GetTree().Quit(passed ? 0 : 1);
    }
}
