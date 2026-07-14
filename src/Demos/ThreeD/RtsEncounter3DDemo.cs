using Godot;
using RtsDemo.Scenarios;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// A playable 3D view over the production planar RTS simulation. Godot nodes
/// never feed physics back into gameplay: input becomes simulation commands,
/// and the presenter only consumes snapshots/SoA state.
/// </summary>
public partial class RtsEncounter3DDemo : Node3D
{
    private const int PlayerId = PlayableSkirmishScenario.PlayerId;
    private const float FixedDelta = 1f / 60f;
    private const float MinimumDragPixels = 7f;

    private readonly HashSet<int> _selectedUnits = [];
    private readonly HashSet<int> _selectedBuildings = [];
    private RtsSimulation? _simulation;
    private PlayableSkirmishRuntime? _runtime;
    private Rts3DWorldPresenter? _presenter;
    private Camera3D? _camera;
    private Label? _hud;
    private Label? _status;
    private Label? _mode;
    private Vector3 _cameraTarget;
    private float _cameraDistance = 30f;
    private float _cameraYaw = -0.55f;
    private float _cameraPitch = 0.92f;
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private bool _dragging;
    private bool _attackMovePending;
    private int _pendingBuildingType = -1;
    private bool _smoke;
    private long _smokeEndTick;
    private int _smokeDurationTicks;

    public override void _Ready()
    {
        var navigation = PlayableSkirmishScenario.CreateNavigationSnapshot();
        var world = navigation.CreateWorld();
        var bake = ClearanceBakeSnapshot.Build(navigation);
        _simulation = new RtsSimulation(
            world,
            new GridPathProvider(world, staticBake: bake),
            PlayableSkirmishScenario.SimulationCapacity,
            navigation.CreateRoutePlanner(world),
            navigation.CreateChokeController(),
            bake);
        _runtime = PlayableSkirmishScenario.Prepare(
            _simulation,
            DemoBuildingTypes.CreateCatalog(),
            DemoProductionCatalog.CreateSnapshot(),
            DemoTechnologies.CreateCatalog());

        CreateLighting();
        CreateGround(world);
        CreateCamera();
        _presenter = new Rts3DWorldPresenter { Name = "WorldPresenter" };
        AddChild(_presenter);
        _presenter.Initialize(_simulation);
        CreateHud();
        FocusAt(PlayableSkirmishScenario.PlayerHome);

        var args = OS.GetCmdlineUserArgs();
        var recording = args.Contains("--demo-3d-recording");
        _smoke = recording || args.Contains("--demo-3d-smoke");
        if (_smoke)
        {
            _smokeDurationTicks = recording ? 900 : 1_800;
            _smokeEndTick = _simulation.Metrics.Tick + _smokeDurationTicks;
            _status!.Text = "自动验证：镜头将巡视基地、中路与敌方发展";
        }
        UpdateHud();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_simulation is null || _runtime is null) return;
        _runtime.AiDirector.Update(_simulation.Metrics.Tick);
        _simulation.Tick((float)Math.Min(delta, 0.05));
        PruneSelection();
        _presenter?.SetSelection(_selectedUnits, _selectedBuildings);

        if (_smoke)
        {
            UpdateSmokeCamera();
            if (_simulation.Metrics.Tick >= _smokeEndTick)
            {
                FinishSmoke();
                return;
            }
        }
        UpdateHud();
    }

    public override void _Process(double delta)
    {
        UpdateCamera((float)delta);
        var interpolation = (float)Engine.GetPhysicsInterpolationFraction();
        _presenter?.Sync(interpolation);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_simulation is null || _camera is null) return;
        switch (@event)
        {
            case InputEventMouseButton mouse:
                HandleMouseButton(mouse);
                break;
            case InputEventMouseMotion motion when _dragging:
                _dragCurrent = motion.Position;
                break;
            case InputEventKey key when key.Pressed && !key.Echo:
                HandleKey(key.Keycode);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouse)
    {
        if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
        {
            _cameraDistance = MathF.Max(13f, _cameraDistance - 3f);
            return;
        }
        if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
        {
            _cameraDistance = MathF.Min(62f, _cameraDistance + 3f);
            return;
        }
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
            {
                _dragging = true;
                _dragStart = mouse.Position;
                _dragCurrent = mouse.Position;
            }
            else if (_dragging)
            {
                _dragging = false;
                CompleteLeftClick(mouse.Position);
            }
            return;
        }
        if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
        {
            CancelTargetMode();
            if (TryGroundPoint(mouse.Position, out var point))
                IssueSmartCommand(point, queued: Input.IsKeyPressed(Key.Shift));
        }
    }

    private void CompleteLeftClick(Vector2 screen)
    {
        if (!TryGroundPoint(screen, out var point)) return;
        var queued = Input.IsKeyPressed(Key.Shift);
        if (_pendingBuildingType >= 0)
        {
            IssueConstruction(point, queued);
            return;
        }
        if (_attackMovePending)
        {
            IssueAttackMove(point, queued);
            return;
        }
        if (_dragStart.DistanceTo(screen) >= MinimumDragPixels)
            SelectRectangle(_dragStart, screen, queued);
        else
            SelectAt(point, queued);
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                CancelTargetMode();
                break;
            case Key.A:
                _attackMovePending = _selectedUnits.Count > 0;
                _pendingBuildingType = -1;
                SetMode(_attackMovePending ? "攻击移动：左键指定目标" : "请先选择单位");
                break;
            case Key.S:
                if (_simulation is not null && _selectedUnits.Count > 0)
                    Report(_simulation.IssuePlayerStop(PlayerId, SelectedUnits()).Code.ToString());
                break;
            case Key.Q:
                BeginBuild(DemoBuildingTypes.SupplyDepot.Id);
                break;
            case Key.W:
                BeginBuild(DemoBuildingTypes.Barracks.Id);
                break;
            case Key.E:
                BeginBuild(DemoBuildingTypes.Academy.Id);
                break;
            case Key.R:
                BeginBuild(DemoBuildingTypes.Refinery.Id);
                break;
            case Key.T:
                BeginBuild(DemoBuildingTypes.CommandCenter.Id);
                break;
            case Key.Z:
                Train(0);
                break;
            case Key.X:
                Train(1);
                break;
            case Key.C:
                Train(2);
                break;
            case Key.V:
                Research(0);
                break;
            case Key.B:
                Research(1);
                break;
            case Key.N:
                Research(2);
                break;
            case Key.F:
                FocusSelection();
                break;
        }
    }

    private void BeginBuild(int typeId)
    {
        if (_simulation is null || !_selectedUnits.Any(unit =>
                _simulation.Economy.IsWorkerOwnedBy(unit, PlayerId)))
        {
            SetMode("建造需要先选择己方农民");
            return;
        }
        _pendingBuildingType = typeId;
        _attackMovePending = false;
        SetMode($"放置 {DemoBuildingTypes.CreateCatalog().Type(typeId).Name}：左键确认，右键取消");
    }

    private void IssueConstruction(NVector2 point, bool queued)
    {
        if (_simulation is null || _pendingBuildingType < 0) return;
        var builder = _selectedUnits
            .Where(unit => _simulation.Economy.IsWorkerOwnedBy(unit, PlayerId))
            .OrderBy(unit => NVector2.DistanceSquared(_simulation.Units.Positions[unit], point))
            .FirstOrDefault(-1);
        if (builder < 0)
        {
            Report("没有可用农民");
            return;
        }
        var profile = DemoBuildingTypes.CreateCatalog().Type(_pendingBuildingType);
        var refinery = default(EconomyResourceNodeId);
        if (profile.RequiresVespeneNode)
        {
            var gas = NearestResource(point, EconomyResourceKind.VespeneGas, 90f);
            if (gas is null)
            {
                Report("精炼厂必须放在气矿上");
                return;
            }
            refinery = gas.Value.Id;
            point = gas.Value.Position;
        }
        var result = _simulation.IssueConstruction(
            PlayerId, builder, profile, point, refinery, queued);
        Report(result.Succeeded
            ? $"已下达建造 {profile.Name}"
            : $"建造失败：{result.Code}/{result.PlacementCode}");
        if (result.Succeeded && !Input.IsKeyPressed(Key.Shift))
            CancelTargetMode();
    }

    private void Train(int recipeId)
    {
        if (_simulation is null) return;
        var recipe = DemoProductionCatalog.CreateSnapshot().Recipe(recipeId);
        var producers = _selectedBuildings
            .Select(value => new GameplayBuildingId(value))
            .Where(id => _simulation.Construction.IsAlive(id) &&
                         _simulation.Construction.Observe(id).PlayerId == PlayerId)
            .ToArray();
        var success = 0;
        ProductionCommandCode last = ProductionCommandCode.InvalidProducer;
        foreach (var producer in producers)
        {
            var result = _simulation.IssueProduction(PlayerId, producer, recipe);
            last = result.Code;
            if (result.Succeeded) success++;
        }
        Report(success > 0 ? $"{recipe.Name} × {success}" : $"训练失败：{last}");
    }

    private void Research(int technologyId)
    {
        if (_simulation is null) return;
        var technology = DemoTechnologies.CreateCatalog().Technology(technologyId);
        var resultText = "请先选择己方学院";
        foreach (var value in _selectedBuildings.Order())
        {
            var id = new GameplayBuildingId(value);
            if (!_simulation.Construction.IsAlive(id)) continue;
            var result = _simulation.IssueResearch(PlayerId, id, technology);
            resultText = result.Succeeded
                ? $"开始研究 {technology.Name}"
                : $"研究失败：{result.Code}";
            if (result.Succeeded) break;
        }
        Report(resultText);
    }

    private void IssueAttackMove(NVector2 point, bool queued)
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var result = _simulation.IssuePlayerAttackMove(
            PlayerId, SelectedUnits(), point, queued);
        Report(result.Succeeded ? "攻击移动" : $"攻击移动失败：{result.Code}");
        if (!queued) CancelTargetMode();
    }

    private void IssueSmartCommand(NVector2 point, bool queued)
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var target = ResolveSmartTarget(point);
        var result = _simulation.IssuePlayerSmartCommand(
            PlayerId, SelectedUnits(), target, attackMoveModifier: false, queued);
        Report(result.Succeeded ? $"智能命令：{target.Kind}" : $"命令失败：{result.Code}");
    }

    private SmartCommandTarget ResolveSmartTarget(NVector2 point)
    {
        if (_simulation is null) return new(SmartCommandTargetKind.Ground, point);
        var bestDistance = 38f * 38f;
        SmartCommandTarget? best = null;
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit]) continue;
            var distance = NVector2.DistanceSquared(point, _simulation.Units.Positions[unit]);
            var radius = _simulation.Units.Radii[unit] + 22f;
            if (distance > radius * radius || distance >= bestDistance) continue;
            var friendly = _simulation.Diplomacy.IsFriendly(
                PlayerId, _simulation.Combat.Teams[unit]);
            bestDistance = distance;
            best = new SmartCommandTarget(
                friendly ? SmartCommandTargetKind.FriendlyUnit : SmartCommandTargetKind.EnemyUnit,
                _simulation.Units.Positions[unit], Unit: unit);
        }
        foreach (var building in _simulation.CreateGameplayBuildingOverview())
        {
            if (building.IsTerminal) continue;
            var nearest = building.Bounds.Clamp(point);
            var distance = NVector2.DistanceSquared(point, nearest);
            if (distance >= bestDistance) continue;
            var friendly = _simulation.Diplomacy.IsFriendly(PlayerId, building.PlayerId);
            bestDistance = distance;
            best = new SmartCommandTarget(
                friendly ? SmartCommandTargetKind.FriendlyBuilding : SmartCommandTargetKind.EnemyBuilding,
                (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                Building: building.Id.Value);
        }
        var resource = NearestResource(point, null, MathF.Sqrt(bestDistance));
        if (resource is not null)
        {
            best = new SmartCommandTarget(
                SmartCommandTargetKind.ResourceNode,
                resource.Value.Position,
                ResourceNode: resource.Value.Id.Value);
        }
        return best ?? new SmartCommandTarget(SmartCommandTargetKind.Ground, point);
    }

    private EconomyResourceNodeSnapshot? NearestResource(
        NVector2 point,
        EconomyResourceKind? kind,
        float maximumDistance)
    {
        if (_simulation is null) return null;
        EconomyResourceNodeSnapshot? best = null;
        var bestDistance = maximumDistance * maximumDistance;
        for (var index = 0; index < _simulation.Economy.ResourceNodeCount; index++)
        {
            var value = _simulation.Economy.ObserveResourceNode(new EconomyResourceNodeId(index));
            if (kind.HasValue && value.Kind != kind.Value) continue;
            var distance = NVector2.DistanceSquared(point, value.Position);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = value;
        }
        return best;
    }

    private void SelectAt(NVector2 point, bool additive)
    {
        if (_simulation is null) return;
        var unit = -1;
        var best = 42f * 42f;
        for (var candidate = 0; candidate < _simulation.Units.Count; candidate++)
        {
            if (!_simulation.Units.Alive[candidate] ||
                _simulation.Combat.Teams[candidate] != PlayerId) continue;
            var distance = NVector2.DistanceSquared(point, _simulation.Units.Positions[candidate]);
            if (distance >= best) continue;
            best = distance;
            unit = candidate;
        }
        if (!additive)
        {
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
        }
        if (unit >= 0)
        {
            _selectedUnits.Add(unit);
            return;
        }
        foreach (var building in _simulation.CreateGameplayBuildingOverview())
        {
            if (building.PlayerId != PlayerId || building.IsTerminal ||
                !building.Bounds.Expanded(8f).Contains(point)) continue;
            _selectedBuildings.Add(building.Id.Value);
            break;
        }
    }

    private void SelectRectangle(Vector2 from, Vector2 to, bool additive)
    {
        if (_simulation is null || _camera is null) return;
        var rect = new Rect2(from, to - from).Abs();
        if (!additive)
        {
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
        }
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit] ||
                _simulation.Combat.Teams[unit] != PlayerId) continue;
            var world = SimPlane3DTransform.ToWorld(_simulation.Units.Positions[unit], 0.5f);
            if (!_camera.IsPositionBehind(world) && rect.HasPoint(_camera.UnprojectPosition(world)))
                _selectedUnits.Add(unit);
        }
    }

    private int[] SelectedUnits() => _selectedUnits.Order().ToArray();

    private void PruneSelection()
    {
        if (_simulation is null) return;
        _selectedUnits.RemoveWhere(unit =>
            (uint)unit >= (uint)_simulation.Units.Count || !_simulation.Units.Alive[unit]);
        _selectedBuildings.RemoveWhere(value =>
        {
            var id = new GameplayBuildingId(value);
            return !_simulation.Construction.IsAlive(id) ||
                   _simulation.Construction.Observe(id).IsTerminal;
        });
    }

    private void FocusSelection()
    {
        if (_simulation is null) return;
        if (_selectedUnits.Count > 0)
        {
            var center = NVector2.Zero;
            foreach (var unit in _selectedUnits) center += _simulation.Units.Positions[unit];
            FocusAt(center / _selectedUnits.Count);
            return;
        }
        if (_selectedBuildings.Count > 0)
        {
            var building = _simulation.Construction.Observe(
                new GameplayBuildingId(_selectedBuildings.Min()));
            FocusAt((building.Bounds.Min + building.Bounds.Max) * 0.5f);
        }
    }

    private bool TryGroundPoint(Vector2 screen, out NVector2 point)
    {
        point = default;
        if (_camera is null) return false;
        var origin = _camera.ProjectRayOrigin(screen);
        var direction = _camera.ProjectRayNormal(screen);
        if (MathF.Abs(direction.Y) < 0.0001f) return false;
        var distance = -origin.Y / direction.Y;
        if (distance < 0f) return false;
        point = SimPlane3DTransform.ToSimulation(origin + direction * distance);
        return _simulation?.World.Bounds.Contains(point) == true;
    }

    private void CreateCamera()
    {
        _camera = new Camera3D
        {
            Name = "RtsCamera",
            Current = true,
            Fov = 48f,
            Near = 0.1f,
            Far = 240f
        };
        AddChild(_camera);
    }

    private void CreateLighting()
    {
        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("172431"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("8ea5b7"),
                AmbientLightEnergy = 0.72f
            }
        };
        AddChild(environment);
        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-58f, -35f, 0f),
            LightColor = new Color("fff1d3"),
            LightEnergy = 1.35f,
            ShadowEnabled = true
        };
        AddChild(sun);
    }

    private void CreateGround(StaticWorld world)
    {
        var size = SimPlane3DTransform.ToWorldSize(
            world.Bounds.Max - world.Bounds.Min);
        var center = (world.Bounds.Min + world.Bounds.Max) * 0.5f;
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color("263d37"),
            Roughness = 0.94f,
            Metallic = 0.02f
        };
        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = size },
            MaterialOverride = material,
            Position = SimPlane3DTransform.ToWorld(center, -0.02f)
        };
        AddChild(ground);
    }

    private void UpdateCamera(float delta)
    {
        if (_camera is null || _simulation is null) return;
        var forward = new Vector3(-MathF.Sin(_cameraYaw), 0f, -MathF.Cos(_cameraYaw));
        var right = new Vector3(forward.Z, 0f, -forward.X);
        var movement = Vector3.Zero;
        if (Input.IsKeyPressed(Key.Up)) movement += forward;
        if (Input.IsKeyPressed(Key.Down)) movement -= forward;
        if (Input.IsKeyPressed(Key.Left)) movement -= right;
        if (Input.IsKeyPressed(Key.Right)) movement += right;
        if (Input.IsKeyPressed(Key.Comma)) _cameraYaw -= delta * 1.25f;
        if (Input.IsKeyPressed(Key.Period)) _cameraYaw += delta * 1.25f;
        if (movement.LengthSquared() > 0f)
            _cameraTarget += movement.Normalized() * delta * (_cameraDistance * 0.72f);
        var bounds = _simulation.World.Bounds;
        _cameraTarget.X = Math.Clamp(
            _cameraTarget.X,
            bounds.Min.X * SimPlane3DTransform.WorldScale,
            bounds.Max.X * SimPlane3DTransform.WorldScale);
        _cameraTarget.Z = Math.Clamp(
            _cameraTarget.Z,
            bounds.Min.Y * SimPlane3DTransform.WorldScale,
            bounds.Max.Y * SimPlane3DTransform.WorldScale);
        var horizontal = MathF.Cos(_cameraPitch) * _cameraDistance;
        var offset = new Vector3(
            MathF.Sin(_cameraYaw) * horizontal,
            MathF.Sin(_cameraPitch) * _cameraDistance,
            MathF.Cos(_cameraYaw) * horizontal);
        _camera.Position = _cameraTarget + offset;
        _camera.LookAt(_cameraTarget, Vector3.Up);
    }

    private void UpdateSmokeCamera()
    {
        if (_simulation is null) return;
        var elapsed = _smokeEndTick - _simulation.Metrics.Tick;
        var progress = 1f - elapsed / (float)_smokeDurationTicks;
        var target = progress switch
        {
            < 0.25f => PlayableSkirmishScenario.PlayerHome,
            < 0.5f => new NVector2(1_100f, 650f),
            < 0.75f => new NVector2(1_600f, 900f),
            _ => PlayableSkirmishScenario.EnemyHome
        };
        var desired = SimPlane3DTransform.ToWorld(target);
        _cameraTarget = _cameraTarget.Lerp(desired, 0.025f);
    }

    private void FocusAt(NVector2 point) =>
        _cameraTarget = SimPlane3DTransform.ToWorld(point);

    private void CancelTargetMode()
    {
        _pendingBuildingType = -1;
        _attackMovePending = false;
        SetMode("普通选择模式");
    }

    private void CreateHud()
    {
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        var root = new VBoxContainer
        {
            Position = new Vector2(16f, 14f),
            CustomMinimumSize = new Vector2(570f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        root.AddThemeConstantOverride("separation", 5);
        layer.AddChild(root);
        _hud = Label(16, new Color("e9f6ff"));
        _mode = Label(15, new Color("79d8ff"));
        _status = Label(14, new Color("ffd37a"));
        root.AddChild(_hud);
        root.AddChild(_mode);
        root.AddChild(_status);
        root.AddChild(new HSeparator());
        var help = Label(13, new Color("b4c8d8"));
        help.Text = "左键/框选 · 右键智能命令 · A攻击移动 · S停止 · F聚焦\n" +
                    "方向键移动 · ,/.旋转 · 滚轮缩放\n" +
                    "建造 Q补给 W兵营 E学院 R气矿 T基地 · 训练 Z陆战队 X重兵 C农民 · 科技 V/B/N";
        root.AddChild(help);
        var back = new Button
        {
            Text = "返回 2D Demo / 测试中心",
            Position = new Vector2(16f, 650f),
            CustomMinimumSize = new Vector2(230f, 42f)
        };
        back.Pressed += () => GetTree().ChangeSceneToFile(DemoSceneCatalog.CompatibilityEntry);
        layer.AddChild(back);
        SetMode("普通选择模式");
    }

    private static Label Label(int size, Color color)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color("000000cc"));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        return label;
    }

    private void UpdateHud()
    {
        if (_simulation is null || _hud is null) return;
        var economy = _simulation.Economy.Players.Snapshot(PlayerId);
        var match = _simulation.Match.CreateSnapshot(
            _simulation.Construction,
            _simulation.Economy,
            _simulation.Units,
            _simulation.Combat);
        var enemy = match.Players.FirstOrDefault(value =>
            value.PlayerId == PlayableSkirmishScenario.EnemyId);
        _hud.Text = $"3D 遭遇战  t={_simulation.Metrics.Tick / 60f:0.0}s   " +
                    $"矿物 {economy.Minerals}  气体 {economy.VespeneGas}  " +
                    $"人口 {economy.SupplyUsed}/{economy.SupplyCapacity}\n" +
                    $"选择 {_selectedUnits.Count} 单位 / {_selectedBuildings.Count} 建筑   " +
                    $"敌方：{enemy.Workers} 农民 / {enemy.CombatUnits} 军队 / {enemy.ActiveBuildings} 建筑";
    }

    private void SetMode(string value)
    {
        if (_mode is not null) _mode.Text = value;
    }

    private void Report(string value)
    {
        if (_status is not null) _status.Text = value;
    }

    private void FinishSmoke()
    {
        if (_simulation is null) return;
        _smoke = false;
        var player = _simulation.Economy.Players.Snapshot(PlayerId);
        var buildings = _simulation.CreateGameplayBuildingOverview();
        var enemyFacilities = buildings.Count(value =>
            !value.IsTerminal && value.PlayerId == PlayableSkirmishScenario.EnemyId);
        var visibleShapes = _presenter?.PresentedEntityCount ?? 0;
        var passed = player.Minerals > 1_800 && enemyFacilities >= 3 && visibleShapes >= 40;
        GD.Print($"RTS_3D_DEMO_{(passed ? "PASS" : "FAIL")} " +
                 $"ticks={_simulation.Metrics.Tick}, bank={player.Minerals}/{player.VespeneGas}, " +
                 $"enemyFacilities={enemyFacilities}, shapes={visibleShapes}");
        GetTree().Quit(passed ? 0 : 1);
    }
}
