using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

/// <summary>
/// Test-only one-mortar battlefield. Combat, projectile creation and
/// presentation all use production systems; only setup and controls live here.
/// </summary>
internal sealed partial class War3MortarProjectileRegressionMode : Node
{
    public const string CaptureArgument =
        "--war3-mortar-projectile-regression-capture";
    public const string SequenceCaptureArgument =
        "--war3-mortar-sequence-capture";
    private const int SequenceCaptureFrames = 48;

    private RtsSimulation? _simulation;
    private int _mortarUnit = -1;
    private int _targetUnit = -1;
    private long _nextOrderTick;
    private bool _paused;
    private bool _stepRequested;
    private bool _captureRequested;
    private bool _sequenceCaptureRequested;
    private bool _captureStarted;
    private int _renderedFrames;
    private NVector2 _mortarPosition;
    private NVector2 _targetPosition;
    private Label? _status;
    private Button? _pauseButton;

    public int MortarUnit => _mortarUnit;
    public NVector2 FocusPoint { get; private set; }

    public static bool IsRequested(string[] arguments) =>
        arguments.Contains(
            War3LaunchRequest.MortarProjectileRegressionArgument);

    public void Initialize(
        Node3D host,
        RtsSimulation simulation,
        ProductionCatalogSnapshot production,
        War3HumanRuntime runtime,
        string[] arguments)
    {
        _simulation = simulation;
        _captureRequested = arguments.Contains(CaptureArgument);
        _sequenceCaptureRequested = arguments.Contains(
            SequenceCaptureArgument);
        FocusPoint = (runtime.PlayerHome + runtime.EnemyHome) * 0.5f;
        var direction = NVector2.Normalize(
            runtime.EnemyHome - runtime.PlayerHome);
        if (!float.IsFinite(direction.X) || direction.LengthSquared() < 0.5f)
            direction = NVector2.UnitX;
        FindClearCombatPair(
            simulation,
            FocusPoint,
            direction,
            out _mortarPosition,
            out _targetPosition);
        FocusPoint = (_mortarPosition + _targetPosition) * 0.5f;
        _mortarUnit = simulation.AddUnit(
            _mortarPosition,
            production.UnitType(War3HumanContent.MortarTeam),
            War3HumanScenario.PlayerId);
        _targetUnit = simulation.AddUnit(
            _targetPosition,
            production.UnitType(War3HumanContent.Footman),
            War3HumanScenario.EnemyId);
        CreateOverlay(host);
        // Visibility is updated by the first production simulation tick. Issue
        // the order immediately after that instead of bypassing fog rules.
        _nextOrderTick = simulation.Metrics.Tick + 2;
        SetProcess(true);
        SetProcessUnhandledInput(true);
        GD.Print(
            "WAR3_MORTAR_REGRESSION_READY " +
            $"mortar={_mortarUnit} target_unit={_targetUnit} " +
            $"positions={_mortarPosition.X:0},{_mortarPosition.Y:0}/" +
            $"{_targetPosition.X:0},{_targetPosition.Y:0} " +
            $"combat=range:{simulation.Combat.AttackRanges[_mortarUnit]:0.##}/" +
            $"min:{simulation.Combat.MinimumAttackRanges[_mortarUnit]:0.##}/" +
            $"vision:{simulation.Combat.VisionRanges[_mortarUnit]:0.##}/" +
            $"projectile_speed:{simulation.Combat.ProjectileSpeed[_mortarUnit]:0.##} " +
            "command=IssueAttackTarget " +
            "pipeline=normal-unit-attack/combat-projectile/world-presenter " +
            "local_units=1v1 ai=off terrain=real-war3-map scale=normal");
    }

    public bool ConsumeAdvanceSimulation()
    {
        if (!_paused) return true;
        if (!_stepRequested) return false;
        _stepRequested = false;
        return true;
    }

    public void UpdateAfterSimulationTick()
    {
        if (_simulation is null ||
            !_simulation.Units.Alive[_mortarUnit] ||
            !_simulation.Units.Alive[_targetUnit])
            return;
        if (_simulation.Metrics.Tick < _nextOrderTick) return;
        IssueAttack();
    }

    public override void _Process(double delta)
    {
        if (_simulation is null) return;
        _renderedFrames++;
        var active = _simulation.CombatProjectiles.ObserveActive()
            .Where(value => value.AttackerUnit == _mortarUnit)
            .ToArray();
        if (_status is not null)
        {
            var projectile = active.FirstOrDefault();
            _status.Text =
                $"Tick {_simulation.Metrics.Tick}  ·  " +
                $"炮弹 {active.Length}  ·  " +
                (active.Length == 0
                    ? "等待下一发"
                    : $"id={projectile.Id}  " +
                      $"位置={projectile.Position.X:0.0}," +
                      $"{projectile.Position.Y:0.0}") +
                $"  ·  {(_paused ? "已暂停" : "运行中")}";
        }
        if (_captureStarted || _renderedFrames < 30 || active.Length == 0)
            return;
        if (_sequenceCaptureRequested)
        {
            _captureStarted = true;
            _ = CaptureSequenceAndQuitAsync();
            return;
        }
        if (!_captureRequested) return;
        var value = active[0];
        var progress = (value.Position.X - _mortarPosition.X) /
                       (_targetPosition.X - _mortarPosition.X);
        // The reported black quad appears at the muzzle/early-flight phase.
        // Capture the first visible projectile frames instead of the previous
        // mid-flight frame, which could completely miss a short-lived PE2.
        if (progress is < 0f or > 0.18f) return;
        _captureStarted = true;
        _paused = true;
        UpdatePauseButton();
        _ = CaptureAndQuitAsync(value);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true } key ||
            key.Echo)
            return;
        if (key.Keycode == Key.Space)
            TogglePaused();
        else if (key.Keycode is Key.Period or Key.F10)
            StepOnce();
        else if (key.Keycode == Key.R)
            IssueAttack();
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    private void IssueAttack()
    {
        if (_simulation is null || _mortarUnit < 0 ||
            !_simulation.Units.Alive[_mortarUnit] ||
            _targetUnit < 0 || !_simulation.Units.Alive[_targetUnit])
            return;
        _simulation.IssueAttackTarget([_mortarUnit], _targetUnit);
        _nextOrderTick = _simulation.Metrics.Tick + 600;
    }

    private void TogglePaused()
    {
        _paused = !_paused;
        _stepRequested = false;
        UpdatePauseButton();
    }

    private void StepOnce()
    {
        _paused = true;
        _stepRequested = true;
        UpdatePauseButton();
    }

    private void UpdatePauseButton()
    {
        if (_pauseButton is not null)
            _pauseButton.Text = _paused ? "继续 [Space]" : "暂停 [Space]";
    }

    private static void FindClearCombatPair(
        RtsSimulation simulation,
        NVector2 center,
        NVector2 direction,
        out NVector2 mortar,
        out NVector2 target)
    {
        const float halfSeparation = 170f;
        const float clearance = 12f;
        for (var radius = 0f; radius <= 768f; radius += 64f)
        {
            var samples = radius <= 0f ? 1 : 16;
            for (var sample = 0; sample < samples; sample++)
            {
                var angle = samples == 1
                    ? 0f
                    : MathF.Tau * sample / samples;
                var candidateCenter = center + new NVector2(
                    MathF.Cos(angle), MathF.Sin(angle)) * radius;
                var left = candidateCenter - direction * halfSeparation;
                var right = candidateCenter + direction * halfSeparation;
                if (!simulation.World.IsDiscFree(left, clearance) ||
                    !simulation.World.IsDiscFree(right, clearance) ||
                    !simulation.World.IsSegmentFree(left, right, clearance))
                    continue;
                mortar = left;
                target = right;
                return;
            }
        }
        throw new InvalidOperationException(
            "Mortar regression could not find a clear real-terrain 1v1 lane.");
    }

    private void CreateOverlay(Node3D host)
    {
        var layer = new CanvasLayer { Layer = 80 };
        host.AddChild(layer);
        var panel = new PanelContainer
        {
            OffsetLeft = 16f,
            OffsetTop = 16f,
            OffsetRight = 660f,
            OffsetBottom = 142f
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("101720ee"),
            BorderColor = new Color("e2b64d"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 12f,
            ContentMarginTop = 9f,
            ContentMarginRight = 12f,
            ContentMarginBottom = 9f
        });
        var content = new VBoxContainer();
        content.AddChild(new Label
        {
            Text = "迫击炮黑块 · 单位真实链路最小回归",
            ThemeTypeVariation = "HeaderSmall"
        });
        content.AddChild(new Label
        {
            Text = "1 门迫击炮右键攻击 1 个敌方步兵；真实地形、正常尺寸、完整攻击链路"
        });
        _status = new Label { Text = "初始化…" };
        content.AddChild(_status);
        var controls = new HBoxContainer();
        _pauseButton = new Button { Text = "暂停 [Space]" };
        _pauseButton.Pressed += TogglePaused;
        controls.AddChild(_pauseButton);
        var step = new Button { Text = "单步 [. / F10]" };
        step.Pressed += StepOnce;
        controls.AddChild(step);
        var fire = new Button { Text = "重新下达攻击 [R]" };
        fire.Pressed += IssueAttack;
        controls.AddChild(fire);
        content.AddChild(controls);
        panel.AddChild(content);
        layer.AddChild(panel);
    }

    private async Task CaptureAndQuitAsync(
        CombatProjectileSnapshot projectile)
    {
        await ToSignal(RenderingServer.Singleton,
            RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath(
            "user://war3_mortar_regression_real_attack.png");
        var result = image.SavePng(path);
        GD.Print(
            $"WAR3_MORTAR_REGRESSION_CAPTURE {result}: {path} " +
            $"tick={_simulation?.Metrics.Tick ?? -1} " +
            $"projectile={projectile.Id} " +
            $"position={projectile.Position.X:0.###}," +
            $"{projectile.Position.Y:0.###}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    private async Task CaptureSequenceAndQuitAsync()
    {
        var directory = ProjectSettings.GlobalizePath(
            "user://war3_mortar_sequence_real_attack");
        var directoryResult = DirAccess.MakeDirRecursiveAbsolute(directory);
        if (directoryResult != Error.Ok)
        {
            GD.PushError(
                $"Mortar sequence capture directory failed: " +
                $"{directoryResult} ({directory})");
            GetTree().Quit(1);
            return;
        }

        for (var frame = 0; frame < SequenceCaptureFrames; frame++)
        {
            await ToSignal(RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            var path = Path.Combine(
                directory,
                $"frame_{frame:00}.png");
            var result = image.SavePng(path);
            if (result != Error.Ok)
            {
                GD.PushError(
                    $"Mortar sequence frame failed: {result} ({path})");
                GetTree().Quit(1);
                return;
            }
        }

        GD.Print(
            $"WAR3_MORTAR_SEQUENCE_CAPTURE frames={SequenceCaptureFrames} " +
            $"directory={directory}");
        GetTree().Quit(0);
    }
}
