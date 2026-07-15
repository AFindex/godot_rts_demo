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
    private TerrainMapSnapshot? _terrain;
    private PlayableSkirmishRuntime? _runtime;
    private Rts3DWorldPresenter? _presenter;
    private Camera3D? _camera;
    private Rts3DCameraController? _cameraController;
    private Rts3DHud? _hud;
    private Rts3DInterfaceAdapter? _interfaceAdapter;
    private BuildingTypeCatalogSnapshot? _buildingCatalog;
    private ProductionCatalogSnapshot? _productionCatalog;
    private TechnologyCatalogSnapshot? _technologyCatalog;
    private GameplaySelectionSnapshot _selectionSnapshot =
        GameplaySelectionSnapshot.Empty;
    private SelectionSubgroupKey? _activeSelectionSubgroup;
    private ControlGroupManager? _controlGroups;
    private readonly ControlGroupRecallTracker _controlGroupRecallTracker = new();
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private bool _dragging;
    private bool _selectionAdditive;
    private bool _selectionSameType;
    private bool _movePending;
    private bool _attackMovePending;
    private bool _rallyPending;
    private int _pendingBuildingType = -1;
    private bool _debugMovement;
    private string _modeText = "普通选择";
    private string _statusText = "就绪";
    private long _lastMinimapTick = -1;
    private int _lastIdleWorker = -1;
    private int _baseCycleIndex = -1;
    private bool _smoke;
    private long _smokeEndTick;
    private int _smokeDurationTicks;

    public override void _Ready()
    {
        var navigation = PlayableSkirmishScenario.CreateNavigationSnapshot();
        _terrain = PlayableSkirmishTerrainDefinition.Create(navigation);
        var world = navigation.CreateWorld(_terrain);
        _simulation = new RtsSimulation(
            world,
            new GridPathProvider(world),
            PlayableSkirmishScenario.SimulationCapacity,
            navigation.CreateRoutePlanner(world),
            navigation.CreateChokeController());
        _buildingCatalog = DemoBuildingTypes.CreateCatalog();
        _productionCatalog = DemoProductionCatalog.CreateSnapshot();
        _technologyCatalog = DemoTechnologies.CreateCatalog();
        _runtime = PlayableSkirmishScenario.Prepare(
            _simulation,
            _buildingCatalog,
            _productionCatalog,
            _technologyCatalog);
        _interfaceAdapter = new Rts3DInterfaceAdapter(
            PlayerId, _buildingCatalog, _productionCatalog, _technologyCatalog);
        _controlGroups = new ControlGroupManager(_simulation.Units.Capacity);

        CreateLighting();
        var terrainPresenter = new Rts3DTerrainPresenter { Name = "Terrain" };
        AddChild(terrainPresenter);
        terrainPresenter.Initialize(_terrain);
        CreateCamera(world);
        _presenter = new Rts3DWorldPresenter { Name = "WorldPresenter" };
        AddChild(_presenter);
        _presenter.Initialize(_simulation, _terrain);
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
            PrepareRecordingPresentation();
            SetMovementDebug(true);
        }
        RefreshSelectionPresentation();
        UpdateHud();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_simulation is null || _runtime is null) return;
        _runtime.AiDirector.Update(_simulation.Metrics.Tick);
        _simulation.Tick((float)Math.Min(delta, 0.05));
        PruneSelection();
        RefreshSelectionPresentation();

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
                HandleKey(key);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouse)
    {
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (HasTargetMode())
            {
                if (!mouse.Pressed) CompleteLeftClick(mouse.Position);
                return;
            }
            if (mouse.Pressed)
            {
                _dragging = true;
                _dragStart = mouse.Position;
                _dragCurrent = mouse.Position;
                _selectionAdditive = mouse.ShiftPressed;
                _selectionSameType = mouse.CtrlPressed || mouse.DoubleClick;
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
            if (HasTargetMode())
            {
                CancelTargetMode();
                return;
            }
            if (TryGroundPoint(mouse.Position, out var point))
                IssueContextCommand(point, queued: mouse.ShiftPressed);
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
        if (_rallyPending)
        {
            SetSelectedRally(point);
            return;
        }
        if (_movePending)
        {
            IssueMove(point, queued);
            return;
        }
        if (_attackMovePending)
        {
            IssueAttackMove(point, queued);
            return;
        }
        if (_dragStart.DistanceTo(screen) >= MinimumDragPixels)
            SelectRectangle(_dragStart, screen, _selectionAdditive);
        else
            SelectAt(point, _selectionAdditive, _selectionSameType);
    }

    private void HandleKey(InputEventKey keyEvent)
    {
        if (TryReadControlGroup(keyEvent, out var group))
        {
            HandleControlGroup(
                group,
                assign: keyEvent.CtrlPressed,
                add: keyEvent.ShiftPressed,
                steal: keyEvent.AltPressed);
            return;
        }
        switch (keyEvent.Keycode)
        {
            case Key.Escape:
                CancelTargetMode();
                return;
            case Key.Tab:
                CycleSelectionSubgroup(keyEvent.ShiftPressed ? -1 : 1);
                return;
            case Key.F1:
                SelectIdleWorker();
                return;
            case Key.Backspace:
                CycleTownHall();
                return;
            case Key.F:
                FocusSelection();
                return;
            case Key.D:
                SetMovementDebug(!_debugMovement);
                return;
            case Key.Home:
                FocusAt(PlayableSkirmishScenario.PlayerHome);
                return;
        }
        _hud?.TryInvokeShortcut(keyEvent.Keycode);
    }

    private void BeginMove()
    {
        _movePending = _selectedUnits.Count > 0;
        _attackMovePending = false;
        _rallyPending = false;
        _pendingBuildingType = -1;
        _hud?.SetTargetMode(_movePending
            ? new Rts3DTargetModeSnapshot(
                TargetCommandKind.Move, "MOVE", "Left click target · RMB/Esc cancel")
            : null);
        SetMode(_movePending ? "Move target" : "Select units first");
    }

    private void BeginAttackMove()
    {
        _attackMovePending = _selectedUnits.Count > 0;
        _movePending = false;
        _rallyPending = false;
        _pendingBuildingType = -1;
        _hud?.SetTargetMode(_attackMovePending
            ? new Rts3DTargetModeSnapshot(
                TargetCommandKind.AttackMove, "ATTACK MOVE",
                "Left click target · Shift queues · RMB/Esc cancel")
            : null);
        SetMode(_attackMovePending ? "攻击移动：左键指定目标" : "请先选择单位");
    }

    private void BeginRally()
    {
        var available = RallyBuildings();
        _rallyPending = available.Length > 0;
        _movePending = false;
        _attackMovePending = false;
        _pendingBuildingType = -1;
        _hud?.SetTargetMode(_rallyPending
            ? new Rts3DTargetModeSnapshot(
                TargetCommandKind.Rally, "SET RALLY",
                "Ground, resource or friendly unit · RMB/Esc cancel")
            : null);
        SetMode(_rallyPending ? "Set rally target" : "Select a production building");
    }

    private void StopSelection()
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var result = _simulation.IssuePlayerStop(PlayerId, SelectedUnits());
        Report(result.Succeeded ? "停止" : $"停止失败：{result.Code}");
    }

    private void HoldSelection()
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var result = _simulation.IssuePlayerHold(PlayerId, SelectedUnits());
        Report(result.Succeeded ? "Hold position" : $"Hold failed: {result.Code}");
    }

    private void ReturnCargo()
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var workers = _selectedUnits.Where(unit =>
                _simulation.Economy.IsWorkerOwnedBy(unit, PlayerId))
            .Order().ToArray();
        var result = _simulation.IssuePlayerReturnCargo(PlayerId, workers);
        Report(result.Succeeded ? "Return cargo" : $"Return failed: {result.Code}");
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
        _movePending = false;
        _attackMovePending = false;
        _rallyPending = false;
        var profile = _buildingCatalog!.Type(typeId);
        _hud?.SetTargetMode(new Rts3DTargetModeSnapshot(
            TargetCommandKind.Build,
            $"BUILD {profile.Name.ToUpperInvariant()}",
            "Left click place · Shift repeats · RMB/Esc cancel"));
        SetMode($"放置 {profile.Name}");
    }

    private void IssueConstruction(NVector2 point, bool queued)
    {
        if (_simulation is null || _pendingBuildingType < 0) return;
        var profile = _buildingCatalog!.Type(_pendingBuildingType);
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
        var recipe = _productionCatalog!.Recipe(recipeId);
        var producers = ActiveBuildingIds()
            .Select(value => new GameplayBuildingId(value))
            .Where(id => _simulation.Construction.IsAlive(id) &&
                         _simulation.Construction.Observe(id).PlayerId == PlayerId &&
                         _simulation.Construction.Observe(id).Type.Id ==
                         recipe.ProducerBuildingTypeId)
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
        var technology = _technologyCatalog!.Technology(technologyId);
        var resultText = "请先选择己方学院";
        foreach (var value in ActiveBuildingIds())
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

    private void IssueMove(NVector2 point, bool queued)
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var result = _simulation.IssuePlayerMove(
            PlayerId, SelectedUnits(), point, queued);
        Report(result.Succeeded ? "Move" : $"Move failed: {result.Code}");
        if (result.Succeeded) ShowCommandMarker(point, Rts3DCommandMarkerKind.Move);
        if (!queued) CancelTargetMode();
    }

    private void IssueContextCommand(NVector2 point, bool queued)
    {
        if (_simulation is null) return;
        var target = ResolveSmartTarget(point);
        var unitSucceeded = false;
        if (_selectedUnits.Count > 0)
        {
            unitSucceeded = _simulation.IssuePlayerSmartCommand(
                PlayerId, SelectedUnits(), target,
                attackMoveModifier: false, queued).Succeeded;
        }
        var rallySucceeded = SetSelectedRally(target, report: false);
        if (!unitSucceeded && !rallySucceeded)
        {
            Report("No unit or production building can use this command");
            return;
        }
        var kind = rallySucceeded && !unitSucceeded
            ? Rts3DCommandMarkerKind.Rally
            : target.Kind is SmartCommandTargetKind.EnemyUnit or
                SmartCommandTargetKind.EnemyBuilding
                ? Rts3DCommandMarkerKind.Attack
                : target.Kind is SmartCommandTargetKind.ResourceNode or
                    SmartCommandTargetKind.FriendlyBuilding or
                    SmartCommandTargetKind.FriendlyUnit
                    ? Rts3DCommandMarkerKind.Interact
                    : Rts3DCommandMarkerKind.Move;
        ShowCommandMarker(target.Position, kind);
        Report(unitSucceeded && rallySucceeded
            ? $"Units: {target.Kind} · production rally updated"
            : rallySucceeded
                ? $"Rally: {RallyTargetFrom(target).Kind}"
                : $"Command: {target.Kind}");
    }

    private void SetSelectedRally(NVector2 point)
    {
        var target = ResolveSmartTarget(point);
        var succeeded = SetSelectedRally(target, report: true);
        if (succeeded)
        {
            ShowCommandMarker(target.Position, Rts3DCommandMarkerKind.Rally);
            CancelTargetMode();
        }
    }

    private bool SetSelectedRally(SmartCommandTarget target, bool report)
    {
        if (_simulation is null) return false;
        var buildings = RallyBuildings();
        if (buildings.Length == 0) return false;
        var rally = RallyTargetFrom(target);
        var success = 0;
        foreach (var value in buildings)
        {
            if (_simulation.SetProductionRallyTarget(
                    PlayerId, new GameplayBuildingId(value), rally))
                success++;
        }
        if (report)
            Report(success == buildings.Length
                ? $"Rally set for {success} building(s): {rally.Kind}"
                : $"Rally updated {success}/{buildings.Length}");
        RefreshSelectionPresentation();
        return success > 0;
    }

    private static RallyTarget RallyTargetFrom(SmartCommandTarget target) =>
        target.Kind switch
        {
            SmartCommandTargetKind.ResourceNode => RallyTarget.Resource(
                new EconomyResourceNodeId(target.ResourceNode), target.Position),
            SmartCommandTargetKind.FriendlyUnit => RallyTarget.Friendly(
                target.Unit, target.Position),
            _ => RallyTarget.Ground(target.Position)
        };

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

    private void SelectAt(NVector2 point, bool additive, bool sameType)
    {
        if (_simulation is null || _interfaceAdapter is null) return;
        var candidates = UnitSelectionCandidates();
        var unit = SelectionFilter.SelectPoint(candidates, point, PlayerId, 22f);
        if (unit >= 0)
        {
            if (!additive)
            {
                _selectedUnits.Clear();
                _selectedBuildings.Clear();
            }
            if (sameType)
            {
                _selectedUnits.UnionWith(SelectionFilter.SelectVisibleSameType(
                    candidates, unit, VisibleWorldRect(), PlayerId));
            }
            else if (additive && _selectedUnits.Contains(unit))
            {
                _selectedUnits.Remove(unit);
            }
            else
            {
                _selectedUnits.Add(unit);
            }
            RefreshSelectionPresentation();
            return;
        }
        GameplayBuildingSnapshot? hit = null;
        foreach (var building in _simulation.CreateGameplayBuildingOverview())
        {
            if (building.PlayerId != PlayerId || building.IsTerminal ||
                !building.Bounds.Expanded(8f).Contains(point)) continue;
            hit = building;
            break;
        }
        if (!additive)
        {
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
        }
        if (hit.HasValue)
        {
            if (sameType)
            {
                var visible = VisibleWorldRect();
                _selectedBuildings.UnionWith(
                    _simulation.CreateGameplayBuildingOverview()
                        .Where(value => value.PlayerId == PlayerId &&
                                        !value.IsTerminal &&
                                        value.Type.Id == hit.Value.Type.Id &&
                                        visible.Contains((value.Bounds.Min +
                                                          value.Bounds.Max) * 0.5f))
                        .Select(value => value.Id.Value));
            }
            else if (additive && _selectedBuildings.Contains(hit.Value.Id.Value))
            {
                _selectedBuildings.Remove(hit.Value.Id.Value);
            }
            else
            {
                _selectedBuildings.Add(hit.Value.Id.Value);
            }
        }
        RefreshSelectionPresentation();
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
        RefreshSelectionPresentation();
    }

    private int[] SelectedUnits() => _selectedUnits.Order().ToArray();

    private int[] ActiveBuildingIds() =>
        _selectionSnapshot.ActiveSubgroup?.Members
            .Where(value => value.Kind == GameplaySelectionKind.Building)
            .Select(value => value.EntityId)
            .Order().ToArray() ?? [];

    private int[] RallyBuildings()
    {
        if (_simulation is null || _interfaceAdapter is null) return [];
        return _selectedBuildings
            .Where(value => _interfaceAdapter.SupportsRally(_simulation, value))
            .Order().ToArray();
    }

    private SelectionCandidate[] UnitSelectionCandidates()
    {
        if (_simulation is null || _interfaceAdapter is null) return [];
        var result = new SelectionCandidate[_simulation.Units.Count];
        for (var unit = 0; unit < result.Length; unit++)
        {
            result[unit] = new SelectionCandidate(
                unit,
                _interfaceAdapter.UnitTypeId(_simulation, unit),
                _simulation.Combat.Teams[unit],
                _simulation.Units.Alive[unit],
                _simulation.Units.Positions[unit],
                _simulation.Units.Radii[unit]);
        }
        return result;
    }

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

    private void RefreshSelectionPresentation()
    {
        if (_simulation is null || _interfaceAdapter is null) return;
        _selectionSnapshot = _interfaceAdapter.CreateSelection(
            _simulation,
            _selectedUnits,
            _selectedBuildings,
            _activeSelectionSubgroup);
        _activeSelectionSubgroup = _selectionSnapshot.ActiveSubgroup?.Key;
        _presenter?.SetSelection(_selectedUnits, _selectedBuildings);
        _presenter?.SetRallyMarkers(
            _interfaceAdapter.CreateRallyMarkers(
                _simulation, _selectedBuildings));
    }

    private void CycleSelectionSubgroup(int direction)
    {
        if (_selectionSnapshot.Subgroups.Length == 0) return;
        _selectionSnapshot = _selectionSnapshot.Cycle(direction);
        _activeSelectionSubgroup = _selectionSnapshot.ActiveSubgroup?.Key;
        UpdateHud();
    }

    private void SelectSubgroup(int index)
    {
        if ((uint)index >= (uint)_selectionSnapshot.Subgroups.Length) return;
        _selectionSnapshot = _selectionSnapshot with { ActiveSubgroupIndex = index };
        _activeSelectionSubgroup = _selectionSnapshot.ActiveSubgroup?.Key;
        UpdateHud();
    }

    private void ExecuteHudAction(CommandCardActionSnapshot action)
    {
        if (!action.Enabled) return;
        switch (action.Kind)
        {
            case CommandCardActionKind.Move:
                BeginMove();
                break;
            case CommandCardActionKind.AttackMove:
                BeginAttackMove();
                break;
            case CommandCardActionKind.Stop:
                StopSelection();
                break;
            case CommandCardActionKind.Hold:
                HoldSelection();
                break;
            case CommandCardActionKind.ReturnCargo:
                ReturnCargo();
                break;
            case CommandCardActionKind.Rally:
                BeginRally();
                break;
            case CommandCardActionKind.Build:
                BeginBuild(action.DataId);
                break;
            case CommandCardActionKind.Train:
                Train(action.DataId);
                break;
            case CommandCardActionKind.Research:
                Research(action.DataId);
                break;
        }
    }

    private void HandleControlGroup(int group, bool assign, bool add, bool steal)
    {
        if (_simulation is null || _controlGroups is null) return;
        var selected = SelectedControlGroupEntities();
        if (steal)
        {
            if (add) _controlGroups.StealAdd(group, selected);
            else _controlGroups.StealAssign(group, selected);
            Report($"Control group {group} steal-{(add ? "add" : "assign")}");
        }
        else if (assign)
        {
            _controlGroups.Assign(group, selected);
            Report($"Control group {group} assigned");
        }
        else if (add)
        {
            _controlGroups.Add(group, selected);
            Report($"Added selection to control group {group}");
        }
        else
        {
            var recalled = _controlGroups.Recall(group, IsAvailableControlGroupEntity);
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
            foreach (var entity in recalled)
            {
                if (entity.Kind == ControlGroupEntityKind.Unit)
                    _selectedUnits.Add(entity.EntityId);
                else
                    _selectedBuildings.Add(entity.EntityId);
            }
            RefreshSelectionPresentation();
            if (_controlGroupRecallTracker.Register(
                    group, Time.GetTicksMsec() / 1000.0) && recalled.Length > 0)
                FocusSelection();
        }
        UpdateHud();
    }

    private ControlGroupEntity[] SelectedControlGroupEntities()
    {
        var result = new List<ControlGroupEntity>(
            _selectedUnits.Count + _selectedBuildings.Count);
        result.AddRange(_selectedUnits.Order().Select(value =>
            new ControlGroupEntity(ControlGroupEntityKind.Unit, value)));
        result.AddRange(_selectedBuildings.Order().Select(value =>
            new ControlGroupEntity(ControlGroupEntityKind.Building, value)));
        return result.ToArray();
    }

    private bool IsAvailableControlGroupEntity(ControlGroupEntity entity)
    {
        if (_simulation is null || _interfaceAdapter is null) return false;
        return entity.Kind == ControlGroupEntityKind.Unit
            ? _interfaceAdapter.IsOwnedUnitAvailable(_simulation, entity.EntityId)
            : _interfaceAdapter.IsOwnedBuildingAvailable(
                _simulation, entity.EntityId);
    }

    private Rts3DControlGroupSnapshot[] ControlGroupSnapshots()
    {
        if (_controlGroups is null) return [];
        var result = new List<Rts3DControlGroupSnapshot>();
        for (var group = 0; group < ControlGroupManager.GroupCount; group++)
        {
            var entities = _controlGroups.Recall(group, IsAvailableControlGroupEntity);
            if (entities.Length == 0) continue;
            result.Add(new Rts3DControlGroupSnapshot(
                group,
                entities.Count(value => value.Kind == ControlGroupEntityKind.Unit),
                entities.Count(value => value.Kind == ControlGroupEntityKind.Building)));
        }
        return result.ToArray();
    }

    private void SelectIdleWorker()
    {
        if (_simulation is null) return;
        var idle = Enumerable.Range(0, _simulation.Units.Count)
            .Where(unit => _simulation.Units.Alive[unit] &&
                           _simulation.Economy.IsWorkerOwnedBy(unit, PlayerId) &&
                           _simulation.Economy.Worker(unit).State ==
                           WorkerEconomyState.Idle)
            .Order().ToArray();
        if (idle.Length == 0)
        {
            Report("No idle workers");
            return;
        }
        var current = Array.IndexOf(idle, _lastIdleWorker);
        _lastIdleWorker = idle[(current + 1) % idle.Length];
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        _selectedUnits.Add(_lastIdleWorker);
        RefreshSelectionPresentation();
        FocusSelection();
        Report($"Idle worker #{_lastIdleWorker}");
    }

    private void CycleTownHall()
    {
        if (_simulation is null) return;
        var bases = _simulation.CreateGameplayBuildingOverview()
            .Where(value => value.PlayerId == PlayerId && !value.IsTerminal &&
                            value.Type.Function == BuildingFunctionKind.TownHall)
            .OrderBy(value => value.Id.Value).ToArray();
        if (bases.Length == 0) return;
        _baseCycleIndex = (_baseCycleIndex + 1) % bases.Length;
        var building = bases[_baseCycleIndex];
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        _selectedBuildings.Add(building.Id.Value);
        RefreshSelectionPresentation();
        FocusAt((building.Bounds.Min + building.Bounds.Max) * 0.5f);
        Report($"Base {_baseCycleIndex + 1}/{bases.Length}");
    }

    private static bool TryReadControlGroup(InputEventKey keyEvent, out int group)
    {
        var code = keyEvent.Unicode != 0
            ? keyEvent.Unicode
            : (long)keyEvent.Keycode;
        if (code >= '0' && code <= '9')
        {
            group = (int)(code - '0');
            return true;
        }
        group = -1;
        return false;
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
        if (direction.Y >= -0.0001f) return false;
        if (_terrain is null)
        {
            var distance = -origin.Y / direction.Y;
            if (distance < 0f) return false;
            point = SimPlane3DTransform.ToSimulation(origin + direction * distance);
            return true;
        }

        var maximumHeight = SimPlane3DTransform.ToWorldLength(
            (_terrain.MaximumCellLevel + 1f) * _terrain.CliffLevelHeight);
        var begin = MathF.Max(0f, (maximumHeight - origin.Y) / direction.Y);
        var end = -origin.Y / direction.Y;
        if (end < begin) (begin, end) = (end, begin);
        var previousDistance = begin;
        var previousDelta = HeightDelta(
            origin + direction * previousDistance);
        const int steps = 192;
        for (var step = 1; step <= steps; step++)
        {
            var progress = step / (float)steps;
            var distance = begin + (end - begin) * progress;
            var worldPoint = origin + direction * distance;
            var delta = HeightDelta(worldPoint);
            if (previousDelta >= 0f && delta <= 0f)
            {
                var low = previousDistance;
                var high = distance;
                for (var iteration = 0; iteration < 12; iteration++)
                {
                    var middle = (low + high) * 0.5f;
                    if (HeightDelta(origin + direction * middle) > 0f)
                        low = middle;
                    else
                        high = middle;
                }
                point = SimPlane3DTransform.ToSimulation(
                    origin + direction * high);
                return _terrain.Bounds.Contains(point);
            }
            previousDistance = distance;
            previousDelta = delta;
        }
        return false;

        float HeightDelta(Vector3 worldPoint)
        {
            var simulationPoint = SimPlane3DTransform.ToSimulation(worldPoint);
            if (!_terrain.Bounds.Contains(simulationPoint))
                return float.PositiveInfinity;
            return worldPoint.Y - SimPlane3DTransform.ToWorldLength(
                _terrain.HeightAt(simulationPoint));
        }
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

    private void PrepareRecordingPresentation()
    {
        if (_simulation is null || _runtime is null || _controlGroups is null)
            return;
        var workers = _runtime.PlayerWorkers.Take(6).ToArray();
        _controlGroups.Assign(1, workers);
        var townHall = _simulation.CreateGameplayBuildingOverview()
            .Where(value => value.PlayerId == PlayerId && !value.IsTerminal &&
                            value.Type.Function == BuildingFunctionKind.TownHall)
            .OrderBy(value => value.Id.Value)
            .FirstOrDefault();
        if (townHall.Type.Name is null) return;
        _controlGroups.Assign(4,
        [
            new ControlGroupEntity(
                ControlGroupEntityKind.Building, townHall.Id.Value)
        ]);
        _selectedBuildings.Add(townHall.Id.Value);
        var center = (townHall.Bounds.Min + townHall.Bounds.Max) * 0.5f;
        var mineral = NearestResource(center, EconomyResourceKind.Minerals, 420f);
        if (mineral.HasValue)
        {
            _simulation.SetProductionRallyTarget(
                PlayerId,
                townHall.Id,
                RallyTarget.Resource(mineral.Value.Id, mineral.Value.Position));
            Report("SC-style HUD · Town Hall selected · mineral rally active");
        }
    }

    private void CancelTargetMode()
    {
        _pendingBuildingType = -1;
        _movePending = false;
        _attackMovePending = false;
        _rallyPending = false;
        _presenter?.HidePointerPreview();
        _hud?.SetTargetMode(null);
        SetMode("普通选择模式");
    }

    private bool HasTargetMode() =>
        _pendingBuildingType >= 0 || _movePending ||
        _attackMovePending || _rallyPending;

    private void CreateHud()
    {
        _hud = new Rts3DHud { Name = "EncounterHud" };
        AddChild(_hud);
        if (_cameraController is not null)
            _cameraController.EdgeScrollBlocked = _hud.BlocksWorldPointer;
        _hud.ActionRequested += ExecuteHudAction;
        _hud.SubgroupRequested += SelectSubgroup;
        _hud.ControlGroupRecallRequested += group =>
            HandleControlGroup(group, assign: false, add: false, steal: false);
        _hud.IdleWorkerRequested += SelectIdleWorker;
        _hud.FocusRequested += FocusSelection;
        _hud.ToggleDebugRequested += () => SetMovementDebug(!_debugMovement);
        _hud.Return2DRequested += () =>
            GetTree().ChangeSceneToFile(DemoSceneCatalog.CompatibilityEntry);
        _hud.MinimapFocusRequested += point => FocusAt(point);
        _hud.MinimapSmartCommandRequested += point =>
            IssueContextCommand(point, queued: Input.IsKeyPressed(Key.Shift));
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
            var profile = _buildingCatalog!.Type(_pendingBuildingType);
            var valid = TryResolveConstructionTarget(
                point, profile, Input.IsKeyPressed(Key.Shift),
                out _, out var center, out _, out _);
            _presenter.SetPointerPreview(center, valid, profile.Size);
            return;
        }
        _presenter.SetPointerPreview(
            point,
            valid: true,
            new NVector2(HasTargetMode() ? 18f : 10f));
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
        if (_simulation is null || _hud is null ||
            _interfaceAdapter is null) return;
        _hud.UpdateSnapshot(_interfaceAdapter.CreateHudSnapshot(
            _simulation,
            _selectionSnapshot,
            ControlGroupSnapshots(),
            _simulation.Metrics.Tick / 60.0,
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
                                (_smokeDurationTicks != 900 ||
                                 (_debugMovement &&
                                  _presenter?.RallyMarkerCount > 0));
        var passed = player.Minerals > 1_800 && enemyFacilities >= 3 &&
                     visibleShapes >= 40 && presentationReady;
        GD.Print($"RTS_3D_DEMO_{(passed ? "PASS" : "FAIL")} " +
                 $"ticks={_simulation.Metrics.Tick}, bank={player.Minerals}/{player.VespeneGas}, " +
                 $"enemyFacilities={enemyFacilities}, shapes={visibleShapes}, " +
                 $"presentation={presentationReady}, debug={_debugMovement}, " +
                 $"rallyMarkers={_presenter?.RallyMarkerCount ?? 0}");
        GetTree().Quit(passed ? 0 : 1);
    }
}
