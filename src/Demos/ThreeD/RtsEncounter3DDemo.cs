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
    private const float MinimumDragPixels = 7f;

    private readonly HashSet<int> _selectedUnits = [];
    private readonly HashSet<int> _selectedBuildings = [];
    private RtsSimulation? _simulation;
    private PlayableSkirmishRuntime? _runtime;
    private Rts3DWorldPresenter? _presenter;
    private Camera3D? _camera;
    private Rts3DCameraController? _cameraController;
    private Rts3DHud? _hud;
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private bool _dragging;
    private bool _attackMovePending;
    private int _pendingBuildingType = -1;
    private bool _debugMovement;
    private string _modeText = "普通选择";
    private string _statusText = "就绪";
    private long _lastMinimapTick = -1;
    private int _lastCommandUiSignature = int.MinValue;
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
        CreateCamera(world);
        _presenter = new Rts3DWorldPresenter { Name = "WorldPresenter" };
        AddChild(_presenter);
        _presenter.Initialize(_simulation);
        CreateHud();
        FocusAt(PlayableSkirmishScenario.PlayerHome, immediate: true);

        var args = OS.GetCmdlineUserArgs();
        var recording = args.Contains("--demo-3d-recording");
        _smoke = recording || args.Contains("--demo-3d-smoke");
        if (_smoke)
        {
            _smokeDurationTicks = recording ? 900 : 1_800;
            _smokeEndTick = _simulation.Metrics.Tick + _smokeDurationTicks;
            Report("自动验证：镜头将巡视基地、中路与敌方发展");
        }
        if (recording && _runtime is not null)
        {
            _selectedUnits.UnionWith(_runtime.PlayerWorkers.Take(6));
            SetMovementDebug(true);
        }
        UpdateCommandOptions(force: true);
        UpdateHud();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_simulation is null || _runtime is null) return;
        _runtime.AiDirector.Update(_simulation.Metrics.Tick);
        _simulation.Tick((float)Math.Min(delta, 0.05));
        PruneSelection();
        _presenter?.SetSelection(_selectedUnits, _selectedBuildings);
        UpdateCommandOptions();

        if (_smoke)
        {
            UpdateSmokeCamera();
            if (_simulation.Metrics.Tick >= _smokeEndTick)
            {
                FinishSmoke();
                return;
            }
        }
        UpdateMinimap();
        UpdateHud();
    }

    public override void _Process(double delta)
    {
        UpdatePointerPresentation();
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
                _hud?.SetDragSelection(_dragStart, _dragCurrent, visible: true);
                break;
            case InputEventKey key when key.Pressed && !key.Echo:
                HandleKey(key.Keycode);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouse)
    {
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
            {
                _dragging = true;
                _dragStart = mouse.Position;
                _dragCurrent = mouse.Position;
                _hud?.SetDragSelection(_dragStart, _dragCurrent, visible: true);
            }
            else if (_dragging)
            {
                _dragging = false;
                _hud?.SetDragSelection(_dragStart, mouse.Position, visible: false);
                CompleteLeftClick(mouse.Position);
            }
            return;
        }
        if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
        {
            if (_pendingBuildingType >= 0 || _attackMovePending)
            {
                CancelTargetMode();
                return;
            }
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
                BeginAttackMove();
                break;
            case Key.S:
                StopSelection();
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
            case Key.D:
                SetMovementDebug(!_debugMovement);
                break;
            case Key.Home:
                FocusAt(PlayableSkirmishScenario.PlayerHome);
                break;
        }
    }

    private void BeginAttackMove()
    {
        _attackMovePending = _selectedUnits.Count > 0;
        _pendingBuildingType = -1;
        _hud?.SetBuildMode(null);
        SetMode(_attackMovePending ? "攻击移动：左键指定目标" : "请先选择单位");
    }

    private void StopSelection()
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var result = _simulation.IssuePlayerStop(PlayerId, SelectedUnits());
        Report(result.Succeeded ? "停止" : $"停止失败：{result.Code}");
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
        var profile = DemoBuildingTypes.CreateCatalog().Type(typeId);
        _hud?.SetBuildMode(profile.Name);
        SetMode($"放置 {profile.Name}");
    }

    private void IssueConstruction(NVector2 point, bool queued)
    {
        if (_simulation is null || _pendingBuildingType < 0) return;
        var profile = DemoBuildingTypes.CreateCatalog().Type(_pendingBuildingType);
        if (!TryResolveConstructionTarget(
                point, profile, queued,
                out var builder, out var center, out var refinery,
                out var preview))
        {
            Report(preview);
            return;
        }
        var result = _simulation.IssueConstruction(
            PlayerId, builder, profile, center, refinery, queued);
        Report(result.Succeeded
            ? $"已下达建造 {profile.Name}"
            : $"建造失败：{result.Code}/{result.PlacementCode}");
        if (result.Succeeded)
            ShowCommandMarker(center, Rts3DCommandMarkerKind.Build);
        if (result.Succeeded && !Input.IsKeyPressed(Key.Shift))
            CancelTargetMode();
    }

    private bool TryResolveConstructionTarget(
        NVector2 point,
        BuildingTypeProfile profile,
        bool queued,
        out int builder,
        out NVector2 center,
        out EconomyResourceNodeId refinery,
        out string resultText)
    {
        builder = -1;
        center = point;
        refinery = default;
        resultText = "没有可用农民";
        if (_simulation is null) return false;
        builder = _selectedUnits
            .Where(unit => _simulation.Economy.IsWorkerOwnedBy(unit, PlayerId))
            .OrderBy(unit => NVector2.DistanceSquared(_simulation.Units.Positions[unit], point))
            .FirstOrDefault(-1);
        if (builder < 0) return false;
        if (profile.RequiresVespeneNode)
        {
            var gas = NearestResource(point, EconomyResourceKind.VespeneGas, 90f);
            if (gas is null)
            {
                resultText = "精炼厂必须放在气矿上";
                return false;
            }
            refinery = gas.Value.Id;
            center = gas.Value.Position;
        }
        var preview = _simulation.PreviewConstruction(
            PlayerId, builder, profile, center, refinery, queued);
        resultText = preview.Succeeded
            ? "可放置"
            : $"不可放置：{preview.Code}/{preview.PlacementCode}";
        return preview.Succeeded;
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
        if (result.Succeeded)
            ShowCommandMarker(point, Rts3DCommandMarkerKind.Attack);
        if (!queued) CancelTargetMode();
    }

    private void IssueSmartCommand(NVector2 point, bool queued)
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var target = ResolveSmartTarget(point);
        var result = _simulation.IssuePlayerSmartCommand(
            PlayerId, SelectedUnits(), target, attackMoveModifier: false, queued);
        Report(result.Succeeded ? $"智能命令：{target.Kind}" : $"命令失败：{result.Code}");
        if (result.Succeeded)
        {
            var kind = target.Kind is SmartCommandTargetKind.EnemyUnit or
                SmartCommandTargetKind.EnemyBuilding
                ? Rts3DCommandMarkerKind.Attack
                : target.Kind is SmartCommandTargetKind.ResourceNode or
                    SmartCommandTargetKind.FriendlyBuilding
                    ? Rts3DCommandMarkerKind.Interact
                    : Rts3DCommandMarkerKind.Move;
            ShowCommandMarker(target.Position, kind);
        }
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
            UpdateCommandOptions(force: true);
            return;
        }
        foreach (var building in _simulation.CreateGameplayBuildingOverview())
        {
            if (building.PlayerId != PlayerId || building.IsTerminal ||
                !building.Bounds.Expanded(8f).Contains(point)) continue;
            _selectedBuildings.Add(building.Id.Value);
            break;
        }
        UpdateCommandOptions(force: true);
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
        UpdateCommandOptions(force: true);
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
        if (!TryGroundPointRaw(screen, out point)) return false;
        return _simulation?.World.Bounds.Contains(point) == true;
    }

    private bool TryGroundPointRaw(Vector2 screen, out NVector2 point)
    {
        point = default;
        if (_camera is null) return false;
        var origin = _camera.ProjectRayOrigin(screen);
        var direction = _camera.ProjectRayNormal(screen);
        if (MathF.Abs(direction.Y) < 0.0001f) return false;
        var distance = -origin.Y / direction.Y;
        if (distance < 0f) return false;
        point = SimPlane3DTransform.ToSimulation(origin + direction * distance);
        return true;
    }

    private void CreateCamera(StaticWorld world)
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
        _cameraController = new Rts3DCameraController { Name = "CameraController" };
        AddChild(_cameraController);
        _cameraController.Initialize(_camera, world.Bounds);
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
        var distance = progress is < 0.2f or > 0.8f ? 25f : 34f;
        var yaw = -0.72f + progress * 0.38f;
        _cameraController?.SetAutomationTarget(target, distance, yaw);
    }

    private void FocusAt(NVector2 point, bool immediate = false) =>
        _cameraController?.FocusAt(point, immediate);

    private void CancelTargetMode()
    {
        _pendingBuildingType = -1;
        _attackMovePending = false;
        _presenter?.HidePointerPreview();
        _hud?.SetBuildMode(null);
        SetMode("普通选择模式");
    }

    private void CreateHud()
    {
        _hud = new Rts3DHud { Name = "EncounterHud" };
        AddChild(_hud);
        _hud.AttackMoveRequested += BeginAttackMove;
        _hud.StopRequested += StopSelection;
        _hud.FocusRequested += FocusSelection;
        _hud.BuildRequested += BeginBuild;
        _hud.TrainRequested += Train;
        _hud.ResearchRequested += Research;
        _hud.ToggleDebugRequested += () => SetMovementDebug(!_debugMovement);
        _hud.Return2DRequested += () =>
            GetTree().ChangeSceneToFile(DemoSceneCatalog.CompatibilityEntry);
        _hud.MinimapFocusRequested += point => FocusAt(point);
        _hud.MinimapSmartCommandRequested += point =>
            IssueSmartCommand(point, queued: Input.IsKeyPressed(Key.Shift));
        SetMode("普通选择");
    }

    private void UpdatePointerPresentation()
    {
        if (_simulation is null || _presenter is null) return;
        var screen = GetViewport().GetMousePosition();
        if (!TryGroundPoint(screen, out var point))
        {
            _presenter.HidePointerPreview();
            return;
        }
        if (_pendingBuildingType >= 0)
        {
            var profile = DemoBuildingTypes.CreateCatalog().Type(_pendingBuildingType);
            var valid = TryResolveConstructionTarget(
                point, profile, Input.IsKeyPressed(Key.Shift),
                out _, out var center, out _, out _);
            _presenter.SetPointerPreview(center, valid, profile.Size);
            return;
        }
        _presenter.SetPointerPreview(
            point,
            valid: true,
            new NVector2(_attackMovePending ? 18f : 10f));
    }

    private void ShowCommandMarker(NVector2 point, Rts3DCommandMarkerKind kind)
    {
        if (_camera is null || _hud is null) return;
        var world = SimPlane3DTransform.ToWorld(point, 0.1f);
        if (_camera.IsPositionBehind(world)) return;
        _hud.ShowCommandMarker(_camera.UnprojectPosition(world), kind);
    }

    private void SetMovementDebug(bool enabled)
    {
        _debugMovement = enabled;
        if (_presenter is not null) _presenter.DebugMovementEnabled = enabled;
        _hud?.SetDebugEnabled(enabled);
        Report(enabled
            ? "寻路调试开启：黄色=MoveGoal，青色=SlotTarget"
            : "寻路调试关闭");
    }

    private void UpdateCommandOptions(bool force = false)
    {
        if (_simulation is null || _hud is null) return;
        var workerSelected = _selectedUnits.Any(unit =>
            _simulation.Economy.IsWorkerOwnedBy(unit, PlayerId));
        var selectedProfiles = _selectedBuildings
            .Select(value => new GameplayBuildingId(value))
            .Where(_simulation.Construction.IsAlive)
            .Select(_simulation.Construction.Observe)
            .Where(value => value.PlayerId == PlayerId &&
                            value.State == BuildingLifecycleState.Completed)
            .Select(value => value.Type.Id)
            .ToHashSet();
        var signature = HashCode.Combine(
            workerSelected,
            _selectedUnits.Count,
            _selectedBuildings.Count,
            selectedProfiles.Aggregate(17, (hash, value) => hash * 31 + value));
        if (!force && signature == _lastCommandUiSignature) return;
        _lastCommandUiSignature = signature;

        var buildingCatalog = DemoBuildingTypes.CreateCatalog();
        string[] buildKeys = ["Q", "W", "T", "R", "E"];
        _hud.SetCommandOptions(
            Rts3DHudCommandGroup.Build,
            buildingCatalog.Types.ToArray().Select(profile =>
                new Rts3DHudCommandOption(
                    profile.Id,
                    profile.Name,
                    buildKeys[profile.Id],
                    $"{profile.Size.X:0}×{profile.Size.Y:0} footprint · " +
                    $"{profile.Cost.Minerals}/{profile.Cost.VespeneGas}",
                    workerSelected)).ToArray());

        var production = DemoProductionCatalog.CreateSnapshot();
        string[] trainKeys = ["Z", "X", "C"];
        _hud.SetCommandOptions(
            Rts3DHudCommandGroup.Train,
            production.Recipes.ToArray().Select(recipe =>
                new Rts3DHudCommandOption(
                    recipe.Id,
                    recipe.UnitType.Name,
                    trainKeys[recipe.Id],
                    $"{recipe.Cost.Minerals}/{recipe.Cost.VespeneGas} · " +
                    $"人口 {recipe.Cost.Supply}",
                    selectedProfiles.Contains(recipe.ProducerBuildingTypeId)))
                .ToArray());

        var technologies = DemoTechnologies.CreateCatalog();
        string[] researchKeys = ["V", "B", "N"];
        _hud.SetCommandOptions(
            Rts3DHudCommandGroup.Research,
            technologies.Technologies.ToArray().Select(technology =>
                new Rts3DHudCommandOption(
                    technology.Id,
                    technology.Name,
                    researchKeys[technology.Id],
                    $"{technology.Cost.Minerals}/{technology.Cost.VespeneGas}",
                    selectedProfiles.Contains(technology.ResearcherBuildingTypeId)))
                .ToArray());
    }

    private void UpdateMinimap()
    {
        if (_simulation is null || _hud is null ||
            _lastMinimapTick == _simulation.Metrics.Tick ||
            _simulation.Metrics.Tick % 6 != 0)
        {
            return;
        }
        _lastMinimapTick = _simulation.Metrics.Tick;
        var markers = new List<MinimapMarker>(
            _simulation.Units.Count + _simulation.Construction.Count);
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit]) continue;
            markers.Add(new MinimapMarker(
                unit,
                MinimapMarkerKind.Unit,
                _simulation.Combat.Teams[unit],
                _simulation.Units.Positions[unit],
                new NVector2(_simulation.Units.Radii[unit] * 2f),
                _selectedUnits.Contains(unit)));
        }
        foreach (var building in _simulation.CreateGameplayBuildingOverview())
        {
            if (building.IsTerminal) continue;
            markers.Add(new MinimapMarker(
                building.Id.Value,
                MinimapMarkerKind.Building,
                building.PlayerId,
                (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                building.Bounds.Max - building.Bounds.Min,
                _selectedBuildings.Contains(building.Id.Value)));
        }
        _hud.SetMinimapSnapshot(new MinimapSnapshot(
            _simulation.World.Bounds,
            VisibleWorldRect(),
            _simulation.World.Obstacles.ToArray(),
            markers.ToArray()));
    }

    private SimRect VisibleWorldRect()
    {
        if (_simulation is null || _camera is null) return default;
        var size = GetViewport().GetVisibleRect().Size;
        Vector2[] corners =
        [
            Vector2.Zero,
            new Vector2(size.X, 0f),
            size,
            new Vector2(0f, size.Y)
        ];
        var points = new List<NVector2>(4);
        foreach (var corner in corners)
        {
            if (TryGroundPointRaw(corner, out var point))
                points.Add(_simulation.World.Bounds.Clamp(point));
        }
        if (points.Count == 0)
        {
            var target = _cameraController?.Target ??
                         (_simulation.World.Bounds.Min + _simulation.World.Bounds.Max) * 0.5f;
            return new SimRect(target - new NVector2(200f), target + new NVector2(200f));
        }
        var minimum = points.Aggregate(NVector2.Min);
        var maximum = points.Aggregate(NVector2.Max);
        return new SimRect(minimum, maximum);
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
        _hud.UpdateSnapshot(new Rts3DHudSnapshot(
            _simulation.Metrics.Tick / 60.0,
            economy.Minerals,
            economy.VespeneGas,
            economy.SupplyUsed,
            economy.SupplyCapacity,
            _selectedUnits.Count,
            _selectedBuildings.Count,
            enemy.Workers,
            enemy.CombatUnits,
            enemy.ActiveBuildings,
            _modeText,
            _statusText));
    }

    private void SetMode(string value)
    {
        _modeText = value;
        _hud?.SetMode(value);
    }

    private void Report(string value)
    {
        _statusText = value;
        _hud?.SetStatus(value);
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
        var presentationReady = _cameraController?.IsInitialized == true &&
                                _hud is not null &&
                                (_smokeDurationTicks != 900 || _debugMovement);
        var passed = player.Minerals > 1_800 && enemyFacilities >= 3 &&
                     visibleShapes >= 40 && presentationReady;
        GD.Print($"RTS_3D_DEMO_{(passed ? "PASS" : "FAIL")} " +
                 $"ticks={_simulation.Metrics.Tick}, bank={player.Minerals}/{player.VespeneGas}, " +
                 $"enemyFacilities={enemyFacilities}, shapes={visibleShapes}, " +
                 $"presentation={presentationReady}, debug={_debugMovement}");
        GetTree().Quit(passed ? 0 : 1);
    }
}
