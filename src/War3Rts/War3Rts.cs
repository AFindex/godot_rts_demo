using Godot;
using RtsDemo.Demos;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

/// <summary>
/// Formal Warcraft RTS composition root. Input emits simulation commands;
/// gameplay remains deterministic and presentation only consumes snapshots.
/// </summary>
public sealed partial class War3Rts : Node3D
{
    private const float MinimumDragPixels = 7f;
    private RtsSimulation? _simulation;
    private War3HumanRuntime? _runtime;
    private BuildingTypeCatalogSnapshot? _buildings;
    private ProductionCatalogSnapshot? _production;
    private TechnologyCatalogSnapshot? _technologies;
    private War3WorldPresenter? _presenter;
    private Camera3D? _camera;
    private Rts3DCameraController? _cameraController;
    private War3RtsHud? _hud;
    private readonly HashSet<int> _selectedUnits = [];
    private readonly HashSet<int> _selectedBuildings = [];
    private Vector2 _dragStart;
    private bool _dragging;
    private bool _selectionAdditive;
    private bool _movePending;
    private bool _attackMovePending;
    private bool _rallyPending;
    private int _pendingBuilding = -1;
    private bool _buildMenu;
    private string _mode = "普通选择";
    private string _status = "人族基地已就绪";
    private double _elapsed;
    private double _hudAccumulator;
    private bool _smoke;
    private long _smokeEndTick;
    private bool _capture;
    private int _smokeRallyBuilding = -1;
    private NVector2 _smokeRallyPosition;
    private int _smokeConstructionBuilding = -1;
    private int _smokeConstructionBuilder = -1;
    private long _smokeConstructionPauseTick = -1;
    private float _smokeConstructionPauseProgress;
    private bool _smokeConstructionPauseStable = true;
    private bool _smokeConstructionResumed;
    private bool _smokeConstructionAdvancedAfterResume;

    public override void _Ready()
    {
        ValidateHumanAssets();
        _buildings = War3HumanContent.CreateBuildingCatalog();
        _production = War3HumanContent.CreateProductionCatalog();
        _technologies = War3HumanContent.CreateTechnologyCatalog();
        if (!ProductionRequirementCatalogValidator.TryValidate(
                _production, _buildings, out var productionError))
            throw new InvalidOperationException(productionError.Message);
        if (!TechnologyCatalogDependencyValidator.TryValidate(
                _technologies, _buildings, out var technologyError))
            throw new InvalidOperationException(technologyError);

        var navigation = War3HumanScenario.CreateNavigation();
        var world = navigation.CreateWorld();
        var bake = ClearanceBakeSnapshot.Build(navigation);
        _simulation = new RtsSimulation(
            world,
            new GridPathProvider(world, staticBake: bake),
            War3HumanScenario.Capacity,
            navigation.CreateRoutePlanner(world),
            navigation.CreateChokeController(),
            bake);
        _runtime = War3HumanScenario.Prepare(
            _simulation, _buildings, _production, _technologies);

        CreateLighting();
        CreateGround(world);
        CreateCamera(world);
        _presenter = new War3WorldPresenter { Name = "War3World" };
        AddChild(_presenter);
        _presenter.Initialize(_simulation, _production, _camera!);
        CreateHud();
        _selectedUnits.Add(_runtime.PlayerWorkers[0]);
        RefreshSelection();
        _cameraController!.FocusAt(War3HumanScenario.PlayerHome, immediate: true);

        var arguments = OS.GetCmdlineUserArgs();
        _smoke = arguments.Contains("--war3-rts-smoke");
        _capture = arguments.Contains("--war3-rts-capture");
        if (_smoke)
        {
            PrepareSmokeCombat();
            if (_capture && _smokeRallyBuilding >= 0)
            {
                _selectedUnits.Clear();
                _selectedBuildings.Clear();
                _selectedBuildings.Add(_smokeRallyBuilding);
                RefreshSelection();
                _cameraController!.FocusAt(
                    War3HumanScenario.PlayerHome + new NVector2(165f, 105f),
                    immediate: true);
            }
            _smokeEndTick = _simulation.Metrics.Tick + 600;
            _status = "自动回归：验证采集、AI、动画、肖像与特效";
        }
        if (_capture) _ = CaptureAsync();
        UpdateHud();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_simulation is null || _runtime is null) return;
        var frame = (float)Math.Min(delta, 0.05d);
        _runtime.AiDirector.Update(_simulation.Metrics.Tick);
        _simulation.Tick(frame);
        if (_smoke) UpdateSmokeConstructionCycle();
        _elapsed += frame;
        PruneSelection();
        _hudAccumulator += frame;
        if (_hudAccumulator >= 0.1d)
        {
            _hudAccumulator = 0d;
            UpdateHud();
        }
        if (_smoke && _simulation.Metrics.Tick >= _smokeEndTick)
            FinishSmoke();
    }

    public override void _Process(double delta)
    {
        if (_simulation is null) return;
        UpdateBuildPreview();
        _presenter?.Sync((float)Engine.GetPhysicsInterpolationFraction());
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_simulation is null || _camera is null) return;
        switch (inputEvent)
        {
            case InputEventMouseButton mouse:
                HandleMouse(mouse);
                break;
            case InputEventMouseMotion motion when _dragging:
                _hud?.SetDragSelection(_dragStart, motion.Position, true);
                break;
            case InputEventKey key when key.Pressed && !key.Echo:
                HandleKey(key);
                break;
        }
    }

    private void HandleMouse(InputEventMouseButton mouse)
    {
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (HasTargetMode())
            {
                if (!mouse.Pressed && TryGroundPoint(mouse.Position, out var target))
                    CompleteTarget(target, mouse.ShiftPressed);
                return;
            }
            if (mouse.Pressed)
            {
                if (_hud?.BlocksWorldPointer(mouse.Position) == true) return;
                _dragging = true;
                _dragStart = mouse.Position;
                _selectionAdditive = mouse.ShiftPressed;
                _hud?.SetDragSelection(_dragStart, _dragStart, true);
            }
            else if (_dragging)
            {
                _dragging = false;
                _hud?.SetDragSelection(_dragStart, mouse.Position, false);
                if (_dragStart.DistanceTo(mouse.Position) >= MinimumDragPixels)
                    SelectRectangle(_dragStart, mouse.Position, _selectionAdditive);
                else if (TryGroundPoint(mouse.Position, out var point))
                    SelectAt(point, _selectionAdditive);
            }
            return;
        }
        if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
        {
            if (HasTargetMode() || _buildMenu)
            {
                CancelMode();
                return;
            }
            if (_hud?.BlocksWorldPointer(mouse.Position) == true) return;
            if (TryGroundPoint(mouse.Position, out var point))
            {
                if (_selectedUnits.Count > 0)
                    IssueContext(point, mouse.ShiftPressed);
                else if (SelectedProductionBuildings().Length > 0)
                    SetRallyAt(point);
            }
        }
    }

    private void HandleKey(InputEventKey input)
    {
        switch (input.Keycode)
        {
            case Key.Escape:
                CancelMode();
                return;
            case Key.F:
                FocusSelection();
                return;
            case Key.Home:
                _cameraController?.FocusAt(War3HumanScenario.PlayerHome);
                return;
        }
        _hud?.TryInvokeHotkey(input.Keycode);
    }

    private void ExecuteCommand(War3CommandSnapshot command)
    {
        if (!command.Enabled || _simulation is null) return;
        switch (command.Kind)
        {
            case War3CommandKind.Move:
                BeginTarget(TargetMode.Move);
                break;
            case War3CommandKind.AttackMove:
                BeginTarget(TargetMode.AttackMove);
                break;
            case War3CommandKind.Stop:
                Report(_simulation.IssuePlayerStop(
                    War3HumanScenario.PlayerId, SelectedUnits()).Succeeded
                    ? "已停止当前命令" : "停止命令失败");
                break;
            case War3CommandKind.Hold:
                Report(_simulation.IssuePlayerHold(
                    War3HumanScenario.PlayerId, SelectedUnits()).Succeeded
                    ? "保持当前位置" : "保持命令失败");
                break;
            case War3CommandKind.OpenBuildMenu:
                _buildMenu = true;
                _mode = "建造菜单";
                UpdateHud();
                break;
            case War3CommandKind.Build:
                _pendingBuilding = command.DataId;
                _buildMenu = false;
                _mode = $"放置 {War3HumanContent.Buildings[command.DataId].Name}";
                break;
            case War3CommandKind.Train:
                Train(command.DataId);
                break;
            case War3CommandKind.Research:
                Research(command.DataId);
                break;
            case War3CommandKind.Rally:
                BeginTarget(TargetMode.Rally);
                break;
            case War3CommandKind.Cancel:
                CancelMode();
                break;
        }
        UpdateHud();
    }

    private void BeginTarget(TargetMode target)
    {
        _movePending = target == TargetMode.Move && _selectedUnits.Count > 0;
        _attackMovePending = target == TargetMode.AttackMove && _selectedUnits.Count > 0;
        _rallyPending = target == TargetMode.Rally && SelectedProductionBuildings().Length > 0;
        _pendingBuilding = -1;
        _buildMenu = false;
        _mode = target switch
        {
            TargetMode.Move when _movePending => "移动：左键指定位置",
            TargetMode.AttackMove when _attackMovePending => "攻击移动：左键指定位置",
            TargetMode.Rally when _rallyPending => "集结点：左键指定位置",
            _ => "当前选择无法执行该命令"
        };
    }

    private void CompleteTarget(NVector2 point, bool queued)
    {
        if (_simulation is null) return;
        if (_pendingBuilding >= 0)
        {
            IssueConstruction(point, queued);
            return;
        }
        if (_movePending)
        {
            var result = _simulation.IssuePlayerMove(
                War3HumanScenario.PlayerId, SelectedUnits(), point, queued);
            Report(result.Succeeded ? "已下达移动命令" : $"移动失败：{result.Code}");
        }
        else if (_attackMovePending)
        {
            var result = _simulation.IssuePlayerAttackMove(
                War3HumanScenario.PlayerId, SelectedUnits(), point, queued);
            Report(result.Succeeded ? "已下达攻击移动" : $"攻击移动失败：{result.Code}");
        }
        else if (_rallyPending)
        {
            SetRallyAt(point);
        }
        if (!queued) CancelMode();
    }

    private void SetRallyAt(NVector2 point)
    {
        if (_simulation is null) return;
        var producers = SelectedProductionBuildings();
        if (producers.Length == 0)
        {
            Report("当前选择没有可设置集结点的生产建筑");
            return;
        }
        var target = ResolveSmartTarget(point);
        var rally = target.Kind switch
        {
            SmartCommandTargetKind.ResourceNode => RallyTarget.Resource(
                new EconomyResourceNodeId(target.ResourceNode), target.Position),
            SmartCommandTargetKind.FriendlyUnit => RallyTarget.Friendly(
                target.Unit, target.Position),
            _ => RallyTarget.Ground(target.Position)
        };
        var changed = producers.Count(id =>
            _simulation.SetProductionRallyTarget(
                War3HumanScenario.PlayerId, new GameplayBuildingId(id), rally));
        Report(changed > 0
            ? $"已设置 {changed} 个集结点（右键也可直接重设）"
            : "集结点设置失败");
    }

    private void IssueContext(NVector2 point, bool queued)
    {
        if (_simulation is null || _selectedUnits.Count == 0) return;
        var target = ResolveSmartTarget(point);
        var result = _simulation.IssuePlayerSmartCommand(
            War3HumanScenario.PlayerId, SelectedUnits(), target,
            attackMoveModifier: false, queued);
        Report(result.Succeeded
            ? target.Kind switch
            {
                SmartCommandTargetKind.ResourceNode => "农民开始采集",
                SmartCommandTargetKind.EnemyUnit or SmartCommandTargetKind.EnemyBuilding =>
                    "部队开始攻击",
                _ => "部队开始移动"
            }
            : $"命令失败：{result.Code}");
    }

    private SmartCommandTarget ResolveSmartTarget(NVector2 point)
    {
        var simulation = _simulation!;
        var bestDistance = 46f * 46f;
        SmartCommandTarget? best = null;
        for (var unit = 0; unit < simulation.Units.Count; unit++)
        {
            if (!simulation.Units.Alive[unit]) continue;
            var distance = NVector2.DistanceSquared(point, simulation.Units.Positions[unit]);
            var radius = simulation.Units.Radii[unit] + 24f;
            if (distance > radius * radius || distance >= bestDistance) continue;
            var friendly = simulation.Diplomacy.IsFriendly(
                War3HumanScenario.PlayerId, simulation.Combat.Teams[unit]);
            bestDistance = distance;
            best = new SmartCommandTarget(
                friendly ? SmartCommandTargetKind.FriendlyUnit :
                    SmartCommandTargetKind.EnemyUnit,
                simulation.Units.Positions[unit], Unit: unit);
        }
        foreach (var building in simulation.CreateGameplayBuildingOverview())
        {
            if (building.IsTerminal) continue;
            var nearest = building.Bounds.Clamp(point);
            var distance = NVector2.DistanceSquared(point, nearest);
            if (distance >= bestDistance) continue;
            var friendly = simulation.Diplomacy.IsFriendly(
                War3HumanScenario.PlayerId, building.PlayerId);
            bestDistance = distance;
            best = new SmartCommandTarget(
                friendly ? SmartCommandTargetKind.FriendlyBuilding :
                    SmartCommandTargetKind.EnemyBuilding,
                (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                Building: building.Id.Value);
        }
        for (var id = 0; id < simulation.Economy.ResourceNodeCount; id++)
        {
            var node = simulation.Economy.ObserveResourceNode(new EconomyResourceNodeId(id));
            if (node.Remaining <= 0) continue;
            var distance = NVector2.DistanceSquared(point, node.Position);
            var hitRadius = node.Kind == EconomyResourceKind.Minerals ? 78f : 38f;
            if (distance >= bestDistance || distance > hitRadius * hitRadius) continue;
            bestDistance = distance;
            best = new SmartCommandTarget(
                SmartCommandTargetKind.ResourceNode, node.Position, ResourceNode: id);
        }
        return best ?? new SmartCommandTarget(SmartCommandTargetKind.Ground, point);
    }

    private void IssueConstruction(NVector2 point, bool queued)
    {
        if (_simulation is null || _buildings is null || _pendingBuilding < 0) return;
        var builder = _selectedUnits
            .Where(unit => _simulation.Economy.IsWorkerOwnedBy(
                unit, War3HumanScenario.PlayerId))
            .OrderBy(unit => NVector2.DistanceSquared(
                _simulation.Units.Positions[unit], point))
            .FirstOrDefault(-1);
        if (builder < 0)
        {
            Report("建造需要选择一名农民");
            return;
        }
        var profile = _buildings.Type(_pendingBuilding);
        var result = _simulation.IssueConstruction(
            War3HumanScenario.PlayerId, builder, profile, point, default, queued);
        Report(result.Succeeded
            ? $"开始建造 {profile.Name}"
            : $"建造失败：{result.Code}/{result.PlacementCode}");
        if (result.Succeeded && !queued) CancelMode();
    }

    private void Train(int recipeId)
    {
        if (_simulation is null || _production is null) return;
        var recipe = _production.Recipe(recipeId);
        var resultText = "请选择对应的生产建筑";
        foreach (var value in _selectedBuildings.Order())
        {
            var id = new GameplayBuildingId(value);
            if (!_simulation.Construction.IsAlive(id) ||
                _simulation.Construction.Observe(id).Type.Id !=
                recipe.ProducerBuildingTypeId)
                continue;
            var result = _simulation.IssueProduction(
                War3HumanScenario.PlayerId, id, recipe);
            resultText = result.Succeeded
                ? $"已加入队列：{recipe.UnitType.Name}"
                : $"训练失败：{result.Code}";
            if (result.Succeeded) break;
        }
        Report(resultText);
    }

    private void Research(int technologyId)
    {
        if (_simulation is null || _technologies is null) return;
        var technology = _technologies.Technology(technologyId);
        var resultText = "请选择铁匠铺";
        foreach (var value in _selectedBuildings.Order())
        {
            var result = _simulation.IssueResearch(
                War3HumanScenario.PlayerId, new GameplayBuildingId(value), technology);
            resultText = result.Succeeded
                ? $"开始研究 {technology.Name}"
                : $"研究失败：{result.Code}";
            if (result.Succeeded) break;
        }
        Report(resultText);
    }

    private void SelectAt(NVector2 point, bool additive)
    {
        if (_simulation is null) return;
        var best = -1;
        var bestDistance = float.PositiveInfinity;
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit] ||
                _simulation.Combat.Teams[unit] != War3HumanScenario.PlayerId)
                continue;
            var distance = NVector2.DistanceSquared(point, _simulation.Units.Positions[unit]);
            var radius = _simulation.Units.Radii[unit] + 22f;
            if (distance <= radius * radius && distance < bestDistance)
            {
                best = unit;
                bestDistance = distance;
            }
        }
        if (!additive)
        {
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
        }
        if (best >= 0)
        {
            if (additive && _selectedUnits.Contains(best)) _selectedUnits.Remove(best);
            else _selectedUnits.Add(best);
            RefreshSelection();
            return;
        }
        var building = _simulation.CreateGameplayBuildingOverview()
            .FirstOrDefault(value => value.PlayerId == War3HumanScenario.PlayerId &&
                                     !value.IsTerminal &&
                                     value.Bounds.Expanded(8f).Contains(point));
        if (building.Type.Name is not null)
        {
            if (additive && _selectedBuildings.Contains(building.Id.Value))
                _selectedBuildings.Remove(building.Id.Value);
            else
                _selectedBuildings.Add(building.Id.Value);
        }
        RefreshSelection();
    }

    private void SelectRectangle(Vector2 from, Vector2 to, bool additive)
    {
        if (_simulation is null || _camera is null) return;
        if (!additive)
        {
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
        }
        var rect = new Rect2(from, to - from).Abs();
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit] ||
                _simulation.Combat.Teams[unit] != War3HumanScenario.PlayerId)
                continue;
            var world = SimPlane3DTransform.ToWorld(
                _simulation.Units.Positions[unit], 0.8f);
            if (!_camera.IsPositionBehind(world) &&
                rect.HasPoint(_camera.UnprojectPosition(world)))
                _selectedUnits.Add(unit);
        }
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        CancelMode();
        _presenter?.SetSelection(_selectedUnits, _selectedBuildings);
        UpdateHud();
    }

    private void PruneSelection()
    {
        if (_simulation is null) return;
        var changed = false;
        foreach (var unit in _selectedUnits.Where(unit =>
                     (uint)unit >= (uint)_simulation.Units.Count ||
                     !_simulation.Units.Alive[unit]).ToArray())
        {
            _selectedUnits.Remove(unit);
            changed = true;
        }
        foreach (var value in _selectedBuildings.Where(value =>
                     !_simulation.Construction.IsAlive(new GameplayBuildingId(value))).ToArray())
        {
            _selectedBuildings.Remove(value);
            changed = true;
        }
        if (changed) RefreshSelection();
    }

    private void UpdateBuildPreview()
    {
        if (_simulation is null || _buildings is null || _camera is null ||
            _presenter is null || _pendingBuilding < 0)
        {
            _presenter?.HidePointerPreview();
            return;
        }
        if (!TryGroundPoint(GetViewport().GetMousePosition(), out var point)) return;
        var builder = _selectedUnits.FirstOrDefault(unit =>
            _simulation.Economy.IsWorkerOwnedBy(unit, War3HumanScenario.PlayerId), -1);
        var profile = _buildings.Type(_pendingBuilding);
        var valid = builder >= 0 && _simulation.PreviewConstruction(
            War3HumanScenario.PlayerId, builder, profile, point, default, false).Succeeded;
        _presenter.SetPointerPreview(point, profile.Size, valid);
    }

    private void UpdateHud()
    {
        if (_simulation is null || _hud is null || _production is null ||
            _technologies is null)
            return;
        var economy = _simulation.Economy.Players.Snapshot(War3HumanScenario.PlayerId);
        _hud.UpdateSnapshot(new War3HudSnapshot(
            economy.Minerals,
            economy.VespeneGas,
            economy.SupplyUsed,
            economy.SupplyCapacity,
            _elapsed,
            CreateSelectionSnapshot(),
            CreateCommands(),
            CreateMinimapEntities(),
            CreateMinimapResources(),
            _simulation.World.Bounds,
            _mode,
            _status));
    }

    private War3SelectionSnapshot CreateSelectionSnapshot()
    {
        if (_simulation is null || _production is null)
            return War3SelectionSnapshot.Empty;
        if (_selectedUnits.Count > 0)
        {
            var living = _selectedUnits.Where(unit => _simulation.Units.Alive[unit])
                .Order().ToArray();
            if (living.Length == 0) return War3SelectionSnapshot.Empty;
            var first = living[0];
            var definition = War3HumanContent.ResolveUnit(
                _simulation, _production, first);
            var health = living.Sum(unit => _simulation.Combat.Health[unit]);
            var maximum = living.Sum(unit => _simulation.Combat.MaximumHealth[unit]);
            var homogeneous = living.All(unit => War3HumanContent.ResolveUnit(
                _simulation, _production, unit).TypeId == definition.TypeId);
            return new War3SelectionSnapshot(
                homogeneous && living.Length > 1
                    ? $"{definition.Name} × {living.Length}"
                    : definition.Name,
                homogeneous ? definition.Role : $"混合编队 · {living.Length} 个单位",
                health, maximum,
                definition.PortraitSource,
                !definition.PortraitSource.Equals(
                    definition.ModelSource, StringComparison.OrdinalIgnoreCase),
                definition.IconPath,
                living.Length,
                0f,
                string.Empty);
        }
        if (_selectedBuildings.Count > 0)
        {
            var id = new GameplayBuildingId(_selectedBuildings.Order().First());
            if (!_simulation.Construction.IsAlive(id)) return War3SelectionSnapshot.Empty;
            var building = _simulation.Construction.Observe(id);
            var definition = War3HumanContent.Buildings[building.Type.Id];
            var queue = _simulation.Production.Observe(id).Orders.FirstOrDefault();
            var research = _simulation.Technology.Observe(id).Orders.FirstOrDefault();
            var queueLabel = queue.Recipe.Name is not null
                ? $"训练：{queue.Recipe.UnitType.Name}"
                : research.Technology.Name is not null
                    ? $"研究：{research.Technology.Name}"
                    : string.Empty;
            var progress = queue.Recipe.Name is not null ? queue.Progress : research.Progress;
            return new War3SelectionSnapshot(
                definition.Name,
                building.State == BuildingLifecycleState.Completed
                    ? definition.Role
                    : $"建造中 · {building.Progress:P0}",
                building.Health,
                building.MaximumHealth,
                definition.ModelSource,
                false,
                definition.IconPath,
                1,
                progress,
                queueLabel);
        }
        return War3SelectionSnapshot.Empty;
    }

    private static void ValidateHumanAssets()
    {
        var missing = new List<string>();
        void Model(string path)
        {
            if (!War3RuntimeAssets.Contains(path)) missing.Add($"模型:{path}");
        }
        void Texture(string path)
        {
            if (War3RuntimeAssets.LoadTexture(path) is null) missing.Add($"贴图:{path}");
        }

        foreach (var unit in War3HumanContent.Units)
        {
            Model(unit.ModelSource);
            Model(unit.PortraitSource);
            Texture(unit.IconPath);
            if (unit.ProjectileSource.Length > 0) Model(unit.ProjectileSource);
            if (unit.ImpactSource.Length > 0) Model(unit.ImpactSource);
        }
        foreach (var building in War3HumanContent.Buildings)
        {
            Model(building.ModelSource);
            Texture(building.IconPath);
            if (!War3RuntimeAssets.Contains(building.ModelSource)) continue;
            var metadata = War3RuntimeAssets.LoadMetadata(building.ModelSource);
            var idle = metadata.Sequences.FirstOrDefault(sequence =>
                sequence.Name.StartsWith("Stand", StringComparison.OrdinalIgnoreCase));
            var birth = metadata.Sequences.FirstOrDefault(sequence =>
                sequence.Name.StartsWith("Birth", StringComparison.OrdinalIgnoreCase));
            var death = metadata.Sequences.FirstOrDefault(sequence =>
                sequence.Name.StartsWith("Death", StringComparison.OrdinalIgnoreCase));
            if (idle is null || idle.NonLooping)
                missing.Add($"建筑待机动画:{building.ModelSource}");
            if (birth is null || !birth.NonLooping)
                missing.Add($"建筑建造动画:{building.ModelSource}");
            if (death is null || !death.NonLooping)
                missing.Add($"建筑销毁动画:{building.ModelSource}");
        }
        Model(War3HumanContent.GoldMineSource);
        Model(@"UI\Feedback\RallyPoint\RallyPoint.mdx");
        for (var variant = 0; variant < 10; variant++)
            Model(War3HumanContent.TreeSource(variant));

        foreach (var path in new[]
                 {
                     @"UI\Console\Human\HumanUITile01.blp",
                     @"UI\Console\Human\HumanUITile02.blp",
                     @"UI\Console\Human\HumanUITile03.blp",
                     @"UI\Console\Human\HumanUITile04.blp",
                     @"UI\Console\Human\HumanUIPortraitMask.blp",
                     @"UI\Feedback\Resources\ResourceGold.blp",
                     @"UI\Feedback\Resources\ResourceLumber.blp",
                     @"UI\Feedback\Resources\ResourceSupply.blp",
                     Btn("Move"), Btn("Attack"), Btn("Stop"), Btn("HoldPosition"),
                     Btn("BasicStruct"), Btn("RallyPoint"), Btn("SteelMelee"),
                     Btn("Cancel")
                 })
            Texture(path);

        if (missing.Count > 0)
            throw new FileNotFoundException(
                "war3_rts 人族资源不完整：" + string.Join("; ", missing));
    }

    private War3CommandSnapshot[] CreateCommands()
    {
        if (_buildMenu)
        {
            var commands = War3HumanContent.Buildings.Select((definition, index) =>
                new War3CommandSnapshot(
                    index, War3CommandKind.Build, definition.TypeId,
                    definition.Name,
                    $"建造 {definition.Name} · {definition.Role}",
                    definition.IconPath,
                    Hotkey(index))).ToList();
            commands.Add(Command(11, War3CommandKind.Cancel, -1, "返回", "取消建造菜单",
                Btn("Cancel"), "ESC"));
            return commands.ToArray();
        }
        if (_selectedUnits.Count > 0)
        {
            var commands = new List<War3CommandSnapshot>
            {
                Command(0, War3CommandKind.Move, -1, "移动", "移动到目标位置", Btn("Move"), "M"),
                Command(4, War3CommandKind.AttackMove, -1, "攻击移动", "沿途攻击敌人", Btn("Attack"), "A"),
                Command(8, War3CommandKind.Stop, -1, "停止", "停止当前命令", Btn("Stop"), "S"),
                Command(9, War3CommandKind.Hold, -1, "保持位置", "不再追击远处敌人", Btn("HoldPosition"), "H")
            };
            if (_simulation is not null && _selectedUnits.Any(unit =>
                    _simulation.Economy.IsWorkerOwnedBy(
                        unit, War3HumanScenario.PlayerId)))
                commands.Add(Command(11, War3CommandKind.OpenBuildMenu, -1,
                    "建造", "打开人族建筑菜单", Btn("BasicStruct"), "B"));
            return commands.ToArray();
        }
        if (_selectedBuildings.Count > 0 && _simulation is not null &&
            _production is not null && _technologies is not null)
        {
            var id = new GameplayBuildingId(_selectedBuildings.Order().First());
            if (!_simulation.Construction.IsAlive(id)) return [];
            var building = _simulation.Construction.Observe(id);
            var result = new List<War3CommandSnapshot>();
            var slot = 0;
            foreach (var recipe in _production.Recipes.ToArray().Where(value =>
                         value.ProducerBuildingTypeId == building.Type.Id).Take(8))
            {
                var availability = _simulation.Production.ObserveAvailability(
                    War3HumanScenario.PlayerId, id, recipe,
                    _simulation.Construction, _simulation.Economy.Players);
                var definition = War3HumanContent.Units[recipe.UnitType.Id];
                result.Add(new War3CommandSnapshot(
                    slot, War3CommandKind.Train, recipe.Id,
                    definition.Name,
                    $"训练 {definition.Name} · {recipe.Cost.Minerals} 黄金 / " +
                    $"{recipe.Cost.VespeneGas} 木材 / {recipe.Cost.Supply} 人口\n" +
                    availability.Code,
                    definition.IconPath, Hotkey(slot),
                    availability.Code is ProductionCommandCode.Success or
                        ProductionCommandCode.InsufficientMinerals or
                        ProductionCommandCode.InsufficientVespeneGas or
                        ProductionCommandCode.SupplyBlocked));
                slot++;
            }
            if (building.Type.Function == BuildingFunctionKind.Research)
            {
                foreach (var technology in _technologies.Technologies.ToArray()
                             .Where(value => value.ResearcherBuildingTypeId == building.Type.Id)
                             .Take(8 - slot))
                    result.Add(new War3CommandSnapshot(
                        slot++, War3CommandKind.Research, technology.Id,
                        technology.Name,
                        $"研究 {technology.Name} · {technology.Cost.Minerals} 黄金 / " +
                        $"{technology.Cost.VespeneGas} 木材",
                        Btn("SteelMelee"), Hotkey(slot - 1)));
            }
            if (building.Type.Function is BuildingFunctionKind.Production or
                BuildingFunctionKind.TownHall)
                result.Add(Command(8, War3CommandKind.Rally, -1,
                    "设置集结点", "设置新单位的集结位置", Btn("RallyPoint"), "Y"));
            return result.ToArray();
        }
        return [];
    }

    private War3MinimapEntity[] CreateMinimapEntities()
    {
        if (_simulation is null) return [];
        var values = new List<War3MinimapEntity>();
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (_simulation.Units.Alive[unit])
                values.Add(new War3MinimapEntity(
                    _simulation.Units.Positions[unit],
                    _simulation.Combat.Teams[unit], false));
        }
        values.AddRange(_simulation.CreateGameplayBuildingOverview()
            .Where(value => !value.IsTerminal)
            .Select(value => new War3MinimapEntity(
                (value.Bounds.Min + value.Bounds.Max) * 0.5f,
                value.PlayerId, true)));
        return values.ToArray();
    }

    private War3MinimapResource[] CreateMinimapResources()
    {
        if (_simulation is null) return [];
        var values = new War3MinimapResource[_simulation.Economy.ResourceNodeCount];
        for (var id = 0; id < values.Length; id++)
        {
            var node = _simulation.Economy.ObserveResourceNode(
                new EconomyResourceNodeId(id));
            values[id] = new War3MinimapResource(
                node.Position, node.Kind, node.Remaining <= 0);
        }
        return values;
    }

    private void CancelMode()
    {
        _movePending = false;
        _attackMovePending = false;
        _rallyPending = false;
        _pendingBuilding = -1;
        _buildMenu = false;
        _mode = "普通选择";
        _presenter?.HidePointerPreview();
    }

    private void Report(string value)
    {
        _status = value;
        UpdateHud();
    }

    private bool HasTargetMode() =>
        _movePending || _attackMovePending || _rallyPending || _pendingBuilding >= 0;

    private int[] SelectedUnits() => _selectedUnits.Order().ToArray();

    private int[] SelectedProductionBuildings() => _selectedBuildings
        .Where(value =>
        {
            if (_simulation is null) return false;
            var id = new GameplayBuildingId(value);
            if (!_simulation.Construction.IsAlive(id)) return false;
            var building = _simulation.Construction.Observe(id);
            return building.State == BuildingLifecycleState.Completed &&
                   building.Type.Function is BuildingFunctionKind.Production or
                       BuildingFunctionKind.TownHall;
        })
        .Order().ToArray();

    private void FocusSelection()
    {
        if (_simulation is null) return;
        if (_selectedUnits.Count > 0)
        {
            var sum = _selectedUnits.Aggregate(NVector2.Zero,
                (current, unit) => current + _simulation.Units.Positions[unit]);
            _cameraController?.FocusAt(sum / _selectedUnits.Count);
            return;
        }
        if (_selectedBuildings.Count > 0)
        {
            var building = _simulation.Construction.Observe(
                new GameplayBuildingId(_selectedBuildings.First()));
            _cameraController?.FocusAt((building.Bounds.Min + building.Bounds.Max) * 0.5f);
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
        return true;
    }

    private void CreateCamera(StaticWorld world)
    {
        _camera = new Camera3D
        {
            Name = "RtsCamera",
            Current = true,
            Fov = 46f,
            Near = 0.08f,
            Far = 180f
        };
        AddChild(_camera);
        _cameraController = new Rts3DCameraController
        {
            Name = "CameraController",
            InitialDistance = 25f,
            MinimumDistance = 12f,
            MaximumDistance = 48f,
            InitialPitchDegrees = 56f
        };
        AddChild(_cameraController);
        _cameraController.EdgeScrollBlocked = point =>
            _hud?.BlocksWorldPointer(point) == true;
        _cameraController.Initialize(_camera, world.Bounds);
    }

    private void CreateLighting()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("18262d"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("9aafbd"),
                AmbientLightEnergy = 0.74f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-56f, -32f, 0f),
            LightColor = new Color("fff0cf"),
            LightEnergy = 1.4f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 75f
        });
    }

    private void CreateGround(StaticWorld world)
    {
        var size = SimPlane3DTransform.ToWorldSize(world.Bounds.Max - world.Bounds.Min);
        var center = (world.Bounds.Min + world.Bounds.Max) * 0.5f;
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color("294334"),
            Roughness = 0.96f,
            Metallic = 0.01f
        };
        AddChild(new MeshInstance3D
        {
            Name = "PlainBattlefield",
            Mesh = new PlaneMesh { Size = size },
            MaterialOverride = material,
            Position = SimPlane3DTransform.ToWorld(center, -0.025f)
        });
    }

    private void CreateHud()
    {
        var layer = new CanvasLayer { Name = "Interface", Layer = 20 };
        AddChild(layer);
        _hud = new War3RtsHud { Name = "HumanHud" };
        _hud.CommandRequested += ExecuteCommand;
        _hud.ReturnRequested += () =>
            GetTree().ChangeSceneToFile(DemoSceneCatalog.CompatibilityEntry);
        _hud.MinimapFocusRequested += point => _cameraController?.FocusAt(point);
        layer.AddChild(_hud);
    }

    private void FinishSmoke()
    {
        if (_simulation is null || _runtime is null || _presenter is null || _hud is null)
            return;
        _smoke = false;
        var player = _simulation.Economy.Players.Snapshot(War3HumanScenario.PlayerId);
        var enemyArmy = Enumerable.Range(0, _simulation.Units.Count).Count(unit =>
            _simulation.Units.Alive[unit] &&
            _simulation.Combat.Teams[unit] == War3HumanScenario.EnemyId &&
            !_simulation.Economy.IsWorker(unit));
        var rallySet = _smokeRallyBuilding >= 0 &&
                       _simulation.Production.Observe(
                           new GameplayBuildingId(_smokeRallyBuilding)).Rally.Position ==
                       _smokeRallyPosition;
        var resourceCentersClear = Enumerable.Range(0, _simulation.Units.Count)
            .Where(unit => _simulation.Units.Alive[unit])
            .All(unit => _simulation.World.Obstacles.ToArray().All(obstacle =>
                !StrictlyContains(obstacle, _simulation.Units.Positions[unit])));
        var constructionSynchronized =
            _presenter.SawConstructionProgressAnimation &&
            !_presenter.ConstructionAnimationMismatch &&
            _smokeConstructionPauseStable &&
            _smokeConstructionResumed &&
            _smokeConstructionAdvancedAfterResume;
        var treeHealthReduced = Enumerable.Range(
                0, _simulation.Economy.ResourceNodeCount)
            .Select(index => _simulation.Economy.ObserveResourceNode(
                new EconomyResourceNodeId(index)))
            .Any(node => node.Kind == EconomyResourceKind.VespeneGas &&
                         node.Remaining < War3HumanScenario.TreeHealth);
        var harvestingSynchronized =
            _presenter.SawProgressiveLumberCargo && treeHealthReduced &&
            !_presenter.GoldGatherUsedAttackAnimation &&
            _presenter.SawGoldMinerHidden &&
            _presenter.SawRepeatedLumberCycle;
        var buildingAnimationsValid =
            !_presenter.CompletedBuildingUsedLifecycleAnimation;
        var buildingEffectsValid = _presenter.SawConstructionEffect &&
                                   !_presenter.IdleTownHallEffectLeak;
        var attackFacingValid = _presenter.SawAttackTargetFacing &&
                                !_presenter.AttackTargetFacingMismatch;
        var success = _presenter.PresentedUnitCount >= 14 &&
                      _presenter.PresentedBuildingCount >= 18 &&
                      _presenter.PresentedResourceCount >= 30 &&
                      _hud.PortraitReady &&
                      _hud.ConsoleLayoutReady &&
                      player.Minerals > 1_250 &&
                      player.VespeneGas > 700 &&
                      enemyArmy > 0 &&
                      _presenter.PeakEffectCount > 0 &&
                      _presenter.SawGoldGatherAnimation &&
                      _presenter.SawLumberGatherAnimation &&
                      _presenter.SawCarriedGoldAnimation &&
                      _presenter.SawCarriedLumberAnimation &&
                      rallySet && resourceCentersClear && constructionSynchronized &&
                      harvestingSynchronized && buildingAnimationsValid &&
                      buildingEffectsValid && _presenter.SawBlendedTransition &&
                      _presenter.RallyMarkerUsesWar3Model && attackFacingValid;
        GD.Print(
            $"WAR3_RTS_SMOKE success={success} units={_presenter.PresentedUnitCount} " +
            $"buildings={_presenter.PresentedBuildingCount} resources={_presenter.PresentedResourceCount} " +
            $"effects={_presenter.ActiveEffectCount} peak_effects={_presenter.PeakEffectCount} " +
            $"portrait={_hud.PortraitReady} hud_layout={_hud.ConsoleLayoutReady} " +
            $"gold={player.Minerals} lumber={player.VespeneGas} enemy_army={enemyArmy} " +
            $"gold_anim={_presenter.SawGoldGatherAnimation}/{_presenter.SawCarriedGoldAnimation} " +
            $"lumber_anim={_presenter.SawLumberGatherAnimation}/{_presenter.SawCarriedLumberAnimation} " +
            $"lumber_repeat={_presenter.SawRepeatedLumberCycle} " +
            $"rally={rallySet} resource_collision={resourceCentersClear} " +
            $"construction_sync={constructionSynchronized} " +
            $"lumber_progressive={_presenter.SawProgressiveLumberCargo} " +
            $"tree_health={treeHealthReduced} " +
            $"gold_non_attack={!_presenter.GoldGatherUsedAttackAnimation} " +
            $"gold_hidden={_presenter.SawGoldMinerHidden} " +
            $"building_idle={buildingAnimationsValid} " +
            $"building_effects={buildingEffectsValid} " +
            $"animation_blend={_presenter.SawBlendedTransition} " +
            $"rally_model={_presenter.RallyMarkerUsesWar3Model} " +
            $"attack_facing={attackFacingValid}");
        if (!_capture) GetTree().Quit(success ? 0 : 1);
    }

    private void PrepareSmokeCombat()
    {
        if (_simulation is null || _production is null) return;
        var rallyBuilding = _simulation.CreateGameplayBuildingOverview()
            .First(value => value.PlayerId == War3HumanScenario.PlayerId &&
                            value.Type.Id == War3HumanContent.Barracks);
        _smokeRallyBuilding = rallyBuilding.Id.Value;
        _smokeRallyPosition = War3HumanScenario.PlayerHome + new NVector2(330f, 210f);
        if (!_simulation.SetProductionRallyTarget(
                War3HumanScenario.PlayerId, rallyBuilding.Id,
                RallyTarget.Ground(_smokeRallyPosition)))
            throw new InvalidOperationException("Smoke rally target setup failed.");
        if (_buildings is null || _runtime is null)
            throw new InvalidOperationException("Smoke construction catalogs are unavailable.");
        var farm = _buildings.Type(War3HumanContent.Farm) with
        {
            Cost = default,
            BuildSeconds = 20f
        };
        _smokeConstructionBuilder = _runtime.PlayerWorkers[0];
        var construction = _simulation.IssueConstruction(
            War3HumanScenario.PlayerId,
            _smokeConstructionBuilder,
            farm,
            new NVector2(170f, 1_000f));
        if (!construction.Succeeded)
            throw new InvalidOperationException(
                $"Smoke construction setup failed: {construction.Code}/" +
                $"{construction.PlacementCode}.");
        _smokeConstructionBuilding = construction.BuildingId.Value;
        var rifleman = _production.UnitType(War3HumanContent.Rifleman);
        var player = _simulation.AddUnit(
            new NVector2(1_100f, 250f), rifleman.Movement,
            War3HumanScenario.PlayerId, rifleman.Combat);
        var enemy = _simulation.AddUnit(
            new NVector2(1_220f, 250f), rifleman.Movement,
            War3HumanScenario.EnemyId, rifleman.Combat);
        _simulation.IssueAttackTarget([player], enemy);
        _simulation.IssueAttackTarget([enemy], player);
    }

    private void UpdateSmokeConstructionCycle()
    {
        if (_simulation is null || _smokeConstructionBuilding < 0) return;
        var id = new GameplayBuildingId(_smokeConstructionBuilding);
        if (!_simulation.Construction.IsAlive(id)) return;
        var building = _simulation.Construction.Observe(id);
        if (_smokeConstructionPauseTick < 0 &&
            building.State == BuildingLifecycleState.Constructing &&
            building.Progress >= 0.05f)
        {
            var stopped = _simulation.IssuePlayerStop(
                War3HumanScenario.PlayerId, [_smokeConstructionBuilder]);
            if (!stopped.Succeeded)
                throw new InvalidOperationException("Smoke construction pause failed.");
            _smokeConstructionPauseTick = _simulation.Metrics.Tick;
            _smokeConstructionPauseProgress = building.Progress;
            return;
        }
        if (_smokeConstructionPauseTick < 0) return;
        building = _simulation.Construction.Observe(id);
        if (_smokeConstructionResumed)
        {
            _smokeConstructionAdvancedAfterResume |=
                building.Progress > _smokeConstructionPauseProgress + 0.01f;
            return;
        }
        _smokeConstructionPauseStable &=
            MathF.Abs(building.Progress - _smokeConstructionPauseProgress) <= 0.0001f;
        if (_simulation.Metrics.Tick - _smokeConstructionPauseTick < 60) return;
        if (!_simulation.ResumeConstruction(
                War3HumanScenario.PlayerId, id, _smokeConstructionBuilder))
            throw new InvalidOperationException("Smoke construction resume failed.");
        _smokeConstructionResumed = true;
    }

    private static bool StrictlyContains(SimRect bounds, NVector2 point) =>
        point.X > bounds.Min.X + 0.01f && point.X < bounds.Max.X - 0.01f &&
        point.Y > bounds.Min.Y + 0.01f && point.Y < bounds.Max.Y - 0.01f;

    private async Task CaptureAsync()
    {
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
        {
            GD.Print("WAR3_RTS_CAPTURE SKIPPED: headless display has no render texture");
            if (!_smoke) GetTree().Quit(0);
            return;
        }
        for (var frame = 0; frame < 180; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var image = GetViewport().GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath("user://war3_rts_capture.png");
        var result = image.SavePng(path);
        GD.Print($"WAR3_RTS_CAPTURE {result}: {path}");
        if (!_smoke) GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    private static War3CommandSnapshot Command(
        int slot,
        War3CommandKind kind,
        int data,
        string label,
        string tooltip,
        string icon,
        string hotkey,
        bool enabled = true) => new(
            slot, kind, data, label, tooltip, icon, hotkey, enabled);

    private static string Btn(string name) =>
        $@"ReplaceableTextures\CommandButtons\BTN{name}.blp";

    private static string Hotkey(int index) => index switch
    {
        0 => "Q", 1 => "W", 2 => "E", 3 => "R",
        4 => "A", 5 => "S", 6 => "D", 7 => "F",
        8 => "Z", 9 => "X", 10 => "C", _ => string.Empty
    };

    private enum TargetMode : byte
    {
        Move,
        AttackMove,
        Rally
    }
}
