using Godot;
using RtsDemo.AI;
using RtsDemo.Presentation;
using RtsDemo.Simulation;
using RtsDemo.Tests;
using RtsDemo.GodotRuntime.Resources;
using System.Text.Json;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime;

public partial class RtsDemo : Node2D
{
    private const int PlayerTeam = 1;
    private const string DemoNavigationResourcePath =
        "res://data/demo_navigation_map.tres";
    private const string DemoGameplayProfilesResourcePath =
        "res://data/demo_gameplay_profiles.tres";
    private const string DemoBuildingTypesResourcePath =
        "res://data/demo_building_types.tres";
    private const string DemoProductionCatalogResourcePath =
        "res://data/demo_production_catalog.tres";
    private const string DemoTechnologyCatalogResourcePath =
        "res://data/demo_technology_catalog.tres";
    private const string DemoAiConfigurationResourcePath =
        "res://data/demo_ai_configurations.tres";
    private const string DemoClearanceBakeResourcePath =
        "res://data/demo_clearance_bake.tres";

    [Export]
    public RtsNavigationMapResource? NavigationMapAsset { get; set; }

    [Export]
    public RtsGameplayProfilesResource? GameplayProfilesAsset { get; set; }

    [Export]
    public RtsBuildingTypeCatalogResource? BuildingTypesAsset { get; set; }

    [Export]
    public RtsProductionCatalogResource? ProductionCatalogAsset { get; set; }

    [Export]
    public RtsTechnologyCatalogResource? TechnologyCatalogAsset { get; set; }

    [Export]
    public RtsAiConfigurationCatalogResource? AiConfigurationAsset { get; set; }

    [Export]
    public RtsClearanceBakeResource? ClearanceBakeAsset { get; set; }

    [Export]
    public bool EnableResourceFileWatcher { get; set; } = true;
    private readonly HashSet<int> _selectedUnits = new();
    private readonly HashSet<int> _selectedBuildings = [];
    private SelectionSubgroupKey? _activeSelectionSubgroup;
    private GameplaySelectionSnapshot _selectionSnapshot =
        GameplaySelectionSnapshot.Empty;
    private RtsCommandCardControl? _commandCard;
    private StaticWorld? _world;
    private GodotPathProvider? _pathProvider;
    private IPathProvider? _simulationPathProvider;
    private RtsSimulation? _simulation;
    private PortalGraphRoutePlanner? _routePlanner;
    private ChokeController? _chokeController;
    private VisualTestSession? _visualTest;
    private AiConfigurationCatalogSnapshot? _aiConfigurations;
    private Label? _hud;
    private Control? _hudRoot;
    private CanvasLayer? _hudLayer;
    private RtsLaunchScreen? _launchScreen;
    private bool _frontEndBlocking;
    private bool _interactiveVisualTest;
    private string _interactiveTestStatus = "";
    private TestShowcaseEntry[] _testShowcaseEntries = [];
    private StaticWorld? _defaultWorld;
    private GodotPathProvider? _defaultPathProvider;
    private IPathProvider? _defaultSimulationPathProvider;
    private RtsSimulation? _defaultSimulation;
    private PortalGraphRoutePlanner? _defaultRoutePlanner;
    private ChokeController? _defaultChokeController;
    private bool _navigationReady;
    private bool _dragging;
    private bool _showDebug = true;
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private Vector2 _commandMarker;
    private float _commandMarkerTime;
    private bool _commandMarkerAttackMove;
    private bool _commandMarkerQueued;
    private TargetCommandRequest? _targetCommand;
    private RtsTargetCommandOverlay? _targetCommandOverlay;
    private BuildTargetPreviewSnapshot? _buildTargetPreview;
    private TargetCommandRequest? _buildPreviewRequest;
    private Vector2 _buildPreviewPointer;
    private long _buildPreviewTick = long.MinValue;
    private int _visualTestFinishFrames = -1;
    private int _visualTestExitCode;
    private readonly Stack<DynamicFootprintId> _demoBuildings = new();
    private int _nextDemoBuildingClass;
    private NavigationMapSnapshot? _navigationSnapshot;
    private GameplayProfileCatalogSnapshot? _gameplayProfiles;
    private BuildingTypeCatalogSnapshot? _buildingTypes;
    private ProductionCatalogSnapshot? _productionCatalog;
    private TechnologyCatalogSnapshot? _technologyCatalog;
    private ClearanceBakeSnapshot? _clearanceBake;
    private ControlGroupManager? _controlGroups;
    private int[] _selectionTypeIds = [];
    private readonly ControlGroupRecallTracker _controlGroupRecallTracker = new();
    private Camera2D? _camera;
    private OperationCameraController? _cameraController;
    private Vector2 _pointerScreen;
    private bool _doubleClickSelection;
    private RtsMinimapControl? _minimap;
    private RuntimeResourceSetSnapshot? _hotReloadCandidate;
    private RuntimeResourceReloadPlan? _hotReloadPlan;
    private RtsResourceReloadControl? _resourceReloadControl;
    private BuildingConnectivityDiffSnapshot? _buildingConnectivityDiff;
    private RtsBuildingConnectivityDiffControl? _buildingConnectivityDiffControl;
    private RuntimeResourceFileWatcher? _resourceFileWatcher;
    private ResourceReloadWorkflowSnapshot _resourceWatchStatus =
        ResourceReloadWorkflowSnapshot.Idle;
    private RtsResourceWatchControl? _resourceWatchControl;
    private EconomyOverviewSnapshot? _economyOverview;
    private PlayerViewSnapshot? _playerView;
    private readonly HashSet<int> _visibleUnitIds = [];
    private readonly HashSet<int> _visibleBuildingIds = [];
    private readonly Dictionary<int, PlayerResourceViewSnapshot> _visibleResources = [];
    private RtsEconomyControl? _economyControl;
    private readonly CombatPresentationComposer _combatPresentation = new();
    private RtsCombatProjectileLayer? _combatProjectileLayer;

    public override async void _Ready()
    {
        _combatProjectileLayer =
            GetNodeOrNull<RtsCombatProjectileLayer>("CombatProjectileLayer");
        var userArguments = OS.GetCmdlineUserArgs();
        if (userArguments.Contains("--benchmark"))
        {
            var benchmark = SimulationBenchmark.Run();
            foreach (var result in benchmark.Cases)
            {
                GD.Print(
                    $"RTS_BENCHMARK units={result.Units} " +
                    $"avg={result.AverageTickMilliseconds:0.000}ms " +
                    $"p95={result.P95TickMilliseconds:0.000}ms " +
                    $"hash={result.AverageStateHashMilliseconds:0.000}ms " +
                    $"max={result.MaximumTickMilliseconds:0.000}ms " +
                    $"alloc={result.AverageAllocatedBytes / 1024.0:0.0}KB/tick");
            }
            foreach (var result in benchmark.CombatCases)
            {
                GD.Print(
                    $"RTS_COMBAT_BENCHMARK units={result.Units} " +
                    $"avg={result.AverageTickMilliseconds:0.000}ms " +
                    $"p95={result.P95TickMilliseconds:0.000}ms " +
                    $"hash={result.AverageStateHashMilliseconds:0.000}ms " +
                    $"max={result.MaximumTickMilliseconds:0.000}ms " +
                    $"alloc={result.AverageAllocatedBytes / 1024.0:0.0}KB/tick");
            }

            GD.Print($"RTS_BENCHMARK_JSON {JsonSerializer.Serialize(benchmark)}");
            GetTree().Quit(benchmark.Passed ? 0 : 1);
            return;
        }

        if (userArguments.Contains("--list-visual-tests"))
        {
            GD.Print($"RTS_VISUAL_TEST_CASES {string.Join(',', VisualTestCatalog.CaseIds)}");
            GetTree().Quit(0);
            return;
        }

        if (userArguments.Contains("--generate-demo-navigation-resource"))
        {
            GenerateDemoNavigationResource();
            return;
        }

        if (userArguments.Contains("--validate-navigation-resource"))
        {
            var valid = TryLoadNavigationSnapshot();
            GetTree().Quit(valid ? 0 : 1);
            return;
        }

        if (userArguments.Contains("--validate-gameplay-profiles"))
        {
            var valid = TryLoadGameplayProfiles();
            GetTree().Quit(valid ? 0 : 1);
            return;
        }

        if (userArguments.Contains("--generate-demo-building-types"))
        {
            GenerateDemoBuildingTypes();
            return;
        }

        if (userArguments.Contains("--validate-building-types"))
        {
            // Exercise the file-based CacheMode.Replace path, not the scene's
            // already-instantiated Resource reference.
            BuildingTypesAsset = null;
            _buildingTypes = null;
            var valid = TryLoadBuildingTypes();
            GetTree().Quit(valid ? 0 : 1);
            return;
        }

        if (userArguments.Contains("--generate-demo-production-catalog"))
        {
            GenerateDemoProductionCatalog();
            return;
        }

        if (userArguments.Contains("--validate-production-catalog"))
        {
            ProductionCatalogAsset = null;
            _productionCatalog = null;
            var valid = TryLoadProductionCatalog();
            GetTree().Quit(valid ? 0 : 1);
            return;
        }

        if (userArguments.Contains("--generate-demo-technology-catalog"))
        {
            GenerateDemoTechnologyCatalog();
            return;
        }

        if (userArguments.Contains("--validate-technology-catalog"))
        {
            TechnologyCatalogAsset = null;
            _technologyCatalog = null;
            var valid = TryLoadTechnologyCatalog();
            GetTree().Quit(valid ? 0 : 1);
            return;
        }

        if (userArguments.Contains("--generate-demo-ai-configurations"))
        {
            GenerateDemoAiConfigurations();
            return;
        }

        if (userArguments.Contains("--validate-ai-configurations"))
        {
            AiConfigurationAsset = null;
            _aiConfigurations = null;
            var valid = TryLoadAiConfigurations();
            GetTree().Quit(valid ? 0 : 1);
            return;
        }

        if (userArguments.Contains("--generate-demo-clearance-bake"))
        {
            if (!TryLoadNavigationSnapshot())
            {
                GetTree().Quit(1);
                return;
            }

            GenerateDemoClearanceBake();
            return;
        }

        if (userArguments.Contains("--validate-clearance-bake"))
        {
            var valid = TryLoadNavigationSnapshot() && TryLoadClearanceBake();
            GetTree().Quit(valid ? 0 : 1);
            return;
        }

        if (userArguments.Contains("--generate-hot-reload-test-resources"))
        {
            if (!TryLoadRuntimeData())
            {
                GetTree().Quit(1);
                return;
            }
            GenerateHotReloadTestResources();
            return;
        }

        if (userArguments.Contains("--self-test"))
        {
            if (!TryLoadRuntimeData())
            {
                GetTree().Quit(1);
                return;
            }

            var result = SimulationSelfTest.Run(
                _navigationSnapshot, _gameplayProfiles, _clearanceBake,
                _buildingTypes, _productionCatalog, _technologyCatalog,
                _aiConfigurations);
            GD.Print($"RTS_SELF_TEST {(result.Passed ? "PASS" : "FAIL")}: {result.Summary}");
            GetTree().Quit(result.Passed ? 0 : 1);
            return;
        }

        var visualTestId = ReadArgument(userArguments, "--visual-test");
        if (visualTestId is not null)
        {
            if (!TryLoadRuntimeData())
            {
                GetTree().Quit(1);
                return;
            }

            if (visualTestId == "resource-hot-reload" &&
                !TryPrepareHotReloadCandidate())
            {
                GetTree().Quit(1);
                return;
            }
            StartVisualTest(visualTestId);
            return;
        }

        if (!TryLoadRuntimeData() || _navigationSnapshot is null ||
            _gameplayProfiles is null || _clearanceBake is null)
        {
            GetTree().Quit(1);
            return;
        }

        _world = _navigationSnapshot.CreateWorld();
        _routePlanner = _navigationSnapshot.CreateRoutePlanner(_world);
        _chokeController = _navigationSnapshot.CreateChokeController();
        _pathProvider = new GodotPathProvider(this, _world, navigationRadius: 8f);
        _simulationPathProvider = new ValidatingFallbackPathProvider(
            _pathProvider,
            new GridPathProvider(_world, staticBake: _clearanceBake),
            _world);
        _simulation = new RtsSimulation(
            _world,
            _simulationPathProvider,
            capacity: 256,
            groupRoutePlanner: _routePlanner,
            chokeController: _chokeController,
            clearanceBake: _clearanceBake);
        _combatPresentation.Reset();
        InitializeResourceFileWatcher(enableFileSystemWatchers:
            EnableResourceFileWatcher);
        InitializeOperationState();
        InitializeCamera();
        CreateHud();
        SpawnDemoUnits();

        for (var attempt = 0; attempt < 120 && !_pathProvider.IsReady; attempt++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            _pathProvider.TryMarkSynchronized();
        }

        _navigationReady = _pathProvider.IsReady;
        if (!_navigationReady)
        {
            GD.PushError("RTS navigation map did not expose a surface within 120 physics frames.");
        }
        UpdateHud();
        QueueRedraw();
        RememberDefaultRuntime();
        CreateLaunchScreen();

        if (userArguments.Contains("--capture"))
        {
            await CaptureDemoFrame();
        }
    }

    public override void _ExitTree()
    {
        _resourceFileWatcher?.Stop();
        _pathProvider?.Dispose();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_frontEndBlocking || !_navigationReady || _simulation is null)
        {
            return;
        }

        if (_visualTest is null || _visualTest.Id == "minimap-interaction")
        {
            _simulation.Tick((float)delta);
        }
        else
        {
            _visualTest.Step();
        }
        UpdateCombatPresentation((float)delta);
        _commandMarkerTime = MathF.Max(0f, _commandMarkerTime - (float)delta);
        UpdateHud();
        SyncWorldPresentationLayers();
        QueueRedraw();

        if (_visualTest is not null &&
            _simulation.Metrics.Tick >= _visualTest.DurationTicks)
        {
            _navigationReady = false;
            _visualTestFinishFrames = 2;
            var result = _visualTest.Evaluate();
            var passed = result.Passed;
            var summary = result.Summary;
            if (_visualTest.Id == "resource-file-watch-workflow")
            {
                var watcherPassed =
                    _resourceWatchStatus.State ==
                        ResourceReloadWorkflowState.Applied &&
                    _simulation.Metrics.ClearanceBakeReloads == 1;
                passed &= watcherPassed;
                summary += $", watcher={_resourceWatchStatus.State}, " +
                           $"reloads={_simulation.Metrics.ClearanceBakeReloads}, " +
                           $"replanned={_resourceWatchStatus.ReplannedUnits}";
            }
            _visualTestExitCode = passed ? 0 : 1;
            _interactiveTestStatus = passed
                ? $"测试通过：{_visualTest.Id}。{summary}"
                : $"测试失败：{_visualTest.Id}。{summary}";
            GD.Print(
                $"RTS_VISUAL_TEST_{(passed ? "PASS" : "FAIL")} {_visualTest.Id}: " +
                $"ticks={_simulation.Metrics.Tick}, {summary}");
        }
    }

    public override void _Process(double delta)
    {
        UpdateHudLayout();
        if (_visualTest?.TargetCommandPreviewPointer is { } previewPointer)
            _pointerScreen = GodotPathProvider.ToGodot(previewPointer);
        UpdateTargetCommandOverlay();
        if (!_frontEndBlocking && _visualTest is null &&
            _cameraController is not null && _camera is not null)
        {
            UpdateCamera((float)delta);
        }
        if (_visualTestFinishFrames < 0)
        {
            return;
        }

        _visualTestFinishFrames--;
        if (_visualTestFinishFrames == 0)
        {
            if (_interactiveVisualTest)
                ReturnToTestBrowser(_interactiveTestStatus);
            else
                GetTree().Quit(_visualTestExitCode);
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_frontEndBlocking || _simulation is null)
        {
            return;
        }

        if (inputEvent is InputEventMouseMotion motion)
        {
            _pointerScreen = motion.Position;
            if (_dragging)
            {
                _dragCurrent = ScreenToWorld(motion.Position);
                QueueRedraw();
                return;
            }
        }

        if (inputEvent is InputEventMouseButton mouse)
        {
            HandleMouse(mouse);
            return;
        }

        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            HandleKey(key);
        }
    }

    public override void _Draw()
    {
        if (_world is null || _simulation is null)
        {
            return;
        }

        DrawRect(new Rect2(Vector2.Zero, new Vector2(1280f, 720f)), new Color("101722"));
        var worldCanvasTransform = CurrentWorldCanvasTransform();
        if (worldCanvasTransform != RtsWorldCanvasTransform.Identity)
        {
            DrawSetTransform(
                worldCanvasTransform.Offset,
                0f,
                new Vector2(
                    worldCanvasTransform.Scale,
                    worldCanvasTransform.Scale));
        }
        DrawGrid();

        var bounds = ToRect2(_world.Bounds);
        DrawRect(bounds, new Color("172536"), filled: true);
        DrawRect(bounds, new Color("4d6a82"), filled: false, width: 2f);
        foreach (var obstacle in _world.Obstacles)
        {
            var rect = ToRect2(obstacle);
            DrawRect(rect, new Color("273849"), filled: true);
            DrawRect(rect, new Color("71869a"), filled: false, width: 2f);
        }

        if (_playerView is null)
        {
            foreach (var footprint in _world.DynamicOccupancy.Snapshot())
            {
                var rect = ToRect2(footprint.Bounds);
                DrawRect(rect, new Color(0.82f, 0.25f, 0.16f, 0.72f), filled: true);
                DrawRect(rect, new Color("ff9b5e"), filled: false, width: 2f);
            }
        }

        DrawPlayerFog();
        DrawEconomyResources();
        DrawGameplayBuildings();

        DrawVisualTestDiagnostics();

        if (_showDebug)
        {
            DrawPortalGraph();
            DrawSelectedPaths();
            DrawSelectedSlots();
        }

        DrawUnits();

        if (_dragging)
        {
            var selectionRect = MakeRect(_dragStart, _dragCurrent);
            DrawRect(selectionRect, new Color(0.2f, 0.8f, 1f, 0.12f), filled: true);
            DrawRect(selectionRect, new Color("55d8ff"), filled: false, width: 1.5f);
        }

        if (_commandMarkerTime > 0f)
        {
            var alpha = Math.Clamp(_commandMarkerTime * 2f, 0f, 1f);
            var markerColor = _commandMarkerAttackMove
                ? new Color(1f, 0.3f, 0.24f, alpha)
                : new Color(0.25f, 1f, 0.45f, alpha);
            DrawArc(_commandMarker, 12f, 0f, MathF.Tau, 24, markerColor, 2f);
            if (_commandMarkerQueued)
            {
                DrawArc(
                    _commandMarker,
                    17f,
                    0f,
                    MathF.Tau,
                    24,
                    markerColor with { A = alpha * 0.7f },
                    1.5f);
            }
            DrawLine(_commandMarker - new Vector2(6f, 0f),
                _commandMarker + new Vector2(6f, 0f), markerColor, 2f);
            DrawLine(_commandMarker - new Vector2(0f, 6f),
                _commandMarker + new Vector2(0f, 6f), markerColor, 2f);
        }
    }

    private void DrawVisualTestDiagnostics()
    {
        if (_visualTest is null || _visualTest.Id == "minimap-interaction")
        {
            return;
        }

        foreach (var area in _visualTest.DiagnosticAreas)
        {
            var color = area.Kind switch
            {
                TestDiagnosticKind.Accepted => new Color("58d68d"),
                TestDiagnosticKind.Rejected => new Color("ff5f6d"),
                _ => new Color("5dade2")
            };
            var rect = ToRect2(area.Bounds);
            DrawRect(rect, color with { A = 0.18f }, filled: true);
            DrawRect(rect, color, filled: false, width: 3f);
            DrawString(
                ThemeDB.FallbackFont,
                rect.Position + new Vector2(0f, -8f),
                area.Label,
                HorizontalAlignment.Left,
                -1f,
                13,
                color);
        }
    }

    private void DrawPlayerFog()
    {
        if (_playerView is null)
            return;
        var cells = _playerView.VisibilityCells;
        for (var row = 0; row < _playerView.VisibilityRows; row++)
        {
            for (var column = 0; column < _playerView.VisibilityColumns; column++)
            {
                var visibility = (MapVisibility)cells[
                    row * _playerView.VisibilityColumns + column];
                if (visibility == MapVisibility.Visible)
                    continue;
                var minimum = _playerView.WorldBounds.Min + new NVector2(
                    column * _playerView.VisibilityCellSize,
                    row * _playerView.VisibilityCellSize);
                var maximum = NVector2.Min(
                    minimum + new NVector2(_playerView.VisibilityCellSize),
                    _playerView.WorldBounds.Max);
                DrawRect(
                    ToRect2(new SimRect(minimum, maximum)),
                    visibility == MapVisibility.Hidden
                        ? new Color(0.015f, 0.025f, 0.04f, 0.82f)
                        : new Color(0.03f, 0.05f, 0.075f, 0.46f),
                    filled: true);
            }
        }
    }

    private void DrawEconomyResources()
    {
        if (_economyOverview is null)
        {
            return;
        }
        foreach (var node in _economyOverview.ResourceNodes)
        {
            var visibleNode = default(PlayerResourceViewSnapshot);
            if (_playerView is not null &&
                !_visibleResources.TryGetValue(node.Id.Value, out visibleNode))
            {
                continue;
            }
            var position = GodotPathProvider.ToGodot(node.Position);
            var color = node.Kind == EconomyResourceKind.Minerals
                ? new Color("65c9ff")
                : new Color("61db83");
            var operational = _playerView is null
                ? node.Operational
                : visibleNode.KnownOperational;
            if (!operational)
            {
                color = new Color("7a8791");
            }
            DrawCircle(position, 19f, color with { A = 0.72f });
            DrawArc(position, 22f, 0f, MathF.Tau, 24, color, 2f);
            DrawString(
                ThemeDB.FallbackFont,
                position + new Vector2(-18f, 38f),
                _playerView is not null && visibleNode.KnownRemaining < 0
                    ? "?"
                    : node.Remaining.ToString(),
                HorizontalAlignment.Left,
                -1f,
                13,
                color);
        }
    }

    private void DrawGameplayBuildings()
    {
        if (_simulation is null)
        {
            return;
        }
        foreach (var building in _simulation.Construction.CreateOverview())
        {
            if (building.IsTerminal)
            {
                continue;
            }
            if (_playerView is not null &&
                !_visibleBuildingIds.Contains(building.Id.Value))
            {
                continue;
            }
            var rect = ToRect2(building.Bounds);
            var color = building.Type.Function switch
            {
                BuildingFunctionKind.Supply => new Color("e3c65f"),
                BuildingFunctionKind.Production => new Color("e58b52"),
                BuildingFunctionKind.TownHall => new Color("5b9fe8"),
                BuildingFunctionKind.Refinery => new Color("63d68b"),
                BuildingFunctionKind.Research => new Color("a783e8"),
                _ => new Color("d6d6d6")
            };
            var completed = building.State == BuildingLifecycleState.Completed;
            DrawRect(rect, color with { A = completed ? 0.88f : 0.46f }, true);
            DrawRect(rect, completed ? color.Lightened(0.25f) : color, false, 3f);
            var teamColor = building.PlayerId == 1
                ? new Color("4da3ff")
                : building.PlayerId == 2
                    ? new Color("f05b64")
                    : new Color("d6d6d6");
            DrawRect(rect.Grow(3f), teamColor, false, 2f);
            DrawBuildingIdentity(building.Type.Function, rect, teamColor);
            if (_selectedBuildings.Contains(building.Id.Value))
            {
                DrawRect(rect.Grow(5f), new Color("f8f4a6"), false, 3f);
            }

            var progressWidth = rect.Size.X * Math.Clamp(building.Progress, 0f, 1f);
            var progressRect = new Rect2(
                rect.Position + new Vector2(0f, rect.Size.Y - 7f),
                new Vector2(progressWidth, 7f));
            DrawRect(progressRect, new Color("8dff9b"), true);
            DrawString(
                ThemeDB.FallbackFont,
                rect.Position + new Vector2(5f, 17f),
                $"{building.Type.Name}  {building.Progress:P0}",
                HorizontalAlignment.Left,
                rect.Size.X - 10f,
                12,
                new Color("f5f7fa"));
            var production = building.PlayerId == PlayerTeam
                ? _simulation.Production.Observe(building.Id)
                : new ProductionQueueSnapshot(building.Id, RallyTarget.None, []);
            var research = building.PlayerId == PlayerTeam
                ? _simulation.Technology.Observe(building.Id)
                : new ResearchQueueSnapshot(building.Id, []);
            if (production.Orders.Length > 0)
            {
                var active = production.Orders[0];
                var queueProgress = new Rect2(
                    rect.Position + new Vector2(0f, rect.Size.Y - 13f),
                    new Vector2(
                        rect.Size.X * Math.Clamp(active.Progress, 0f, 1f),
                        5f));
                DrawRect(queueProgress, new Color("74d7ff"), true);
                DrawString(
                    ThemeDB.FallbackFont,
                    rect.Position + new Vector2(5f, 34f),
                    $"{active.Recipe.UnitType.Name}  {active.State}  " +
                    $"Q:{production.Orders.Length}",
                    HorizontalAlignment.Left,
                    rect.Size.X - 10f,
                    11,
                    new Color("b9efff"));
            }
            if (research.Orders.Length > 0)
            {
                var activeResearch = research.Orders[0];
                DrawRect(
                    new Rect2(
                        rect.Position + new Vector2(0f, rect.Size.Y - 19f),
                        new Vector2(
                            rect.Size.X * Math.Clamp(activeResearch.Progress, 0f, 1f),
                            5f)),
                    new Color("bd8cff"), true);
                DrawString(
                    ThemeDB.FallbackFont,
                    rect.Position + new Vector2(5f, 34f),
                    $"{activeResearch.Technology.Name} L" +
                    $"{_simulation.Technology.Level(
                        activeResearch.PlayerId,
                        activeResearch.Technology.Id) + 1}  " +
                    $"Q:{research.Orders.Length}",
                    HorizontalAlignment.Left,
                    rect.Size.X - 10f,
                    11,
                    new Color("eadbff"));
            }
            if (_selectedBuildings.Contains(building.Id.Value))
            {
                DrawString(
                    ThemeDB.FallbackFont,
                    rect.Position + new Vector2(
                        5f, production.Orders.Length > 0 ||
                            research.Orders.Length > 0 ? 51f : 35f),
                    $"HP {building.Health:0}/{building.MaximumHealth:0}  {building.State}",
                    HorizontalAlignment.Left,
                    rect.Size.X - 10f,
                    11,
                    new Color("fff4b0"));
                if (production.Rally.IsSet)
                {
                    var center = GodotPathProvider.ToGodot(
                        (building.Bounds.Min + building.Bounds.Max) * 0.5f);
                    var target = production.Rally.Kind ==
                                     RallyTargetKind.FriendlyUnit &&
                                 (uint)production.Rally.Unit <
                                     (uint)_simulation.Units.Count &&
                                 _simulation.Units.Alive[production.Rally.Unit]
                        ? _simulation.Units.Positions[production.Rally.Unit]
                        : production.Rally.Position;
                    var targetGodot = GodotPathProvider.ToGodot(target);
                    DrawDashedLine(
                        center, targetGodot, new Color("8dff9b"), 2f, 10f);
                    DrawArc(
                        targetGodot, 10f, 0f, MathF.Tau, 20,
                        new Color("8dff9b"), 2f);
                }
                DrawProductionAvailability(building, rect);
            }
        }
    }

    private void DrawProductionAvailability(
        GameplayBuildingSnapshot building,
        Rect2 rect)
    {
        if (_simulation is null || _productionCatalog is null ||
            building.State != BuildingLifecycleState.Completed)
            return;
        var row = 0;
        foreach (var recipe in _productionCatalog.Recipes)
        {
            if (recipe.ProducerBuildingTypeId != building.Type.Id) continue;
            var availability = _simulation.Production.ObserveAvailability(
                building.PlayerId,
                building.Id,
                recipe,
                _simulation.Construction,
                _simulation.Economy.Players);
            var requirementText = availability.Requirements.Length == 0
                ? string.Empty
                : " [" + string.Join(", ", availability.Requirements.Select(value =>
                    $"B{value.Requirement.TypeId} " +
                    $"{value.CurrentCount}/{value.Requirement.Count}")) + "]";
            var color = availability.Code is ProductionCommandCode.Success or
                ProductionCommandCode.InsufficientMinerals or
                ProductionCommandCode.InsufficientVespeneGas or
                ProductionCommandCode.SupplyBlocked
                ? new Color("b9efff")
                : new Color("ffad8f");
            DrawString(
                ThemeDB.FallbackFont,
                rect.Position + new Vector2(5f, 68f + row * 15f),
                $"{recipe.UnitType.Name}: {availability.Code}{requirementText}",
                HorizontalAlignment.Left,
                Math.Max(rect.Size.X - 10f, 220f),
                10,
                color);
            row++;
        }
    }

    private void HandleMouse(InputEventMouseButton mouse)
    {
        if (_simulation is null)
        {
            return;
        }

        if (_targetCommand is not null && mouse.Pressed &&
            mouse.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            ResolveTargetCommand(mouse);
            return;
        }

        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
            {
                _dragging = true;
                _doubleClickSelection = mouse.DoubleClick;
                _dragStart = ScreenToWorld(mouse.Position);
                _dragCurrent = _dragStart;
            }
            else if (_dragging)
            {
                _dragging = false;
                SelectInRect(
                    MakeRect(_dragStart, ScreenToWorld(mouse.Position)),
                    additive: Input.IsKeyPressed(Key.Shift),
                    sameType: _doubleClickSelection);
                QueueRedraw();
            }
        }
        else if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _cameraController?.ZoomAt(
                GodotPathProvider.ToNumerics(mouse.Position),
                mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
            ApplyCameraState();
        }
        else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed &&
                 _selectedBuildings.Count == 1 && _selectedUnits.Count == 0)
        {
            var worldPosition = GodotPathProvider.ToNumerics(
                ScreenToWorld(mouse.Position));
            var selectedBuilding = new GameplayBuildingId(
                _selectedBuildings.First());
            var rally = ResolveProductionRallyTarget(worldPosition);
            if (_simulation.SetProductionRallyTarget(
                    PlayerTeam, selectedBuilding, rally))
            {
                _commandMarker = GodotPathProvider.ToGodot(rally.Position);
                _commandMarkerAttackMove = false;
                _commandMarkerQueued = false;
                _commandMarkerTime = 0.65f;
                QueueRedraw();
            }
        }
        else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed &&
                 _selectedUnits.Count > 0 && _navigationReady)
        {
            var selected = SelectedLivingUnits();
            if (selected.Length == 0)
            {
                return;
            }
            _commandMarkerAttackMove = Input.IsKeyPressed(Key.A);
            _commandMarkerQueued = Input.IsKeyPressed(Key.Shift);
            var worldPosition = ScreenToWorld(mouse.Position);
            var target = ResolveSmartCommandTarget(
                selected[0], GodotPathProvider.ToNumerics(worldPosition));
            var issued = _simulation.IssuePlayerSmartCommand(
                PlayerTeam,
                selected,
                target,
                _commandMarkerAttackMove,
                queued: _commandMarkerQueued);
            if (!issued.Succeeded)
                return;
            _commandMarkerAttackMove |= target.Kind is
                SmartCommandTargetKind.EnemyUnit or
                SmartCommandTargetKind.EnemyBuilding;
            _commandMarker = worldPosition;
            _commandMarkerTime = 0.65f;
            QueueRedraw();
        }
    }

    private void HandleKey(InputEventKey keyEvent)
    {
        if (_simulation is null)
        {
            return;
        }

        if (keyEvent.Keycode == Key.Escape && _targetCommand is not null)
        {
            _targetCommand = null;
            QueueRedraw();
            return;
        }

        if (TryReadControlGroup(keyEvent, out var group))
        {
            HandleControlGroup(
                group,
                keyEvent.CtrlPressed,
                keyEvent.ShiftPressed,
                keyEvent.AltPressed);
            QueueRedraw();
            return;
        }

        switch (keyEvent.Keycode)
        {
            case Key.Space:
                _targetCommand = null;
                _selectedUnits.Clear();
                _selectedBuildings.Clear();
                for (var unit = 0; unit < _simulation.Units.Count; unit++)
                {
                    if (_simulation.Units.Alive[unit] &&
                        _simulation.Combat.Teams[unit] == PlayerTeam)
                    {
                        _selectedUnits.Add(unit);
                    }
                }
                break;
            case Key.Tab:
                CycleSelectionSubgroup(keyEvent.ShiftPressed ? -1 : 1);
                break;
            case Key.S:
                _simulation.IssuePlayerStop(PlayerTeam, SelectedLivingUnits());
                break;
            case Key.H:
                _simulation.IssuePlayerHold(PlayerTeam, SelectedLivingUnits());
                break;
            case Key.D:
                _showDebug = !_showDebug;
                break;
            case Key.R:
                ResetDemoUnits();
                break;
            case Key.B:
                PlaceDemoBuilding();
                break;
            case Key.X:
                RemoveLastDemoBuilding();
                break;
        }

        QueueRedraw();
    }

    private void InitializeOperationState()
    {
        if (_simulation is null)
        {
            return;
        }
        _controlGroups = new ControlGroupManager(_simulation.Units.Capacity);
        _selectionTypeIds = Enumerable.Repeat(-1, _simulation.Units.Capacity).ToArray();
    }

    private void InitializeCamera()
    {
        if (_world is null)
        {
            return;
        }
        var viewport = GetViewportRect().Size;
        _cameraController = new OperationCameraController(
            _world.Bounds,
            GodotPathProvider.ToNumerics(viewport));
        _camera ??= new Camera2D { Enabled = true };
        if (_camera.GetParent() is null)
        {
            AddChild(_camera);
        }
        _pointerScreen = viewport * 0.5f;
        ApplyCameraState();
    }

    private void UpdateCamera(float delta)
    {
        if (_cameraController is null)
        {
            return;
        }
        var viewport = GetViewportRect().Size;
        _cameraController.Resize(GodotPathProvider.ToNumerics(viewport));
        var keyboard = NVector2.Zero;
        if (Input.IsKeyPressed(Key.Left)) keyboard.X -= 1f;
        if (Input.IsKeyPressed(Key.Right)) keyboard.X += 1f;
        if (Input.IsKeyPressed(Key.Up)) keyboard.Y -= 1f;
        if (Input.IsKeyPressed(Key.Down)) keyboard.Y += 1f;
        _cameraController.Pan(keyboard, delta);
        _cameraController.PanFromEdges(
            GodotPathProvider.ToNumerics(_pointerScreen), delta);
        ApplyCameraState();
    }

    private void ApplyCameraState()
    {
        if (_camera is null || _cameraController is null)
        {
            return;
        }
        _camera.Position = GodotPathProvider.ToGodot(_cameraController.Position);
        _camera.Zoom = new Vector2(_cameraController.Zoom, _cameraController.Zoom);
    }

    private Vector2 ScreenToWorld(Vector2 screenPosition) =>
        _cameraController is null
            ? screenPosition
            : GodotPathProvider.ToGodot(_cameraController.ScreenToWorld(
                GodotPathProvider.ToNumerics(screenPosition)));

    private void FocusCameraOnSelection()
    {
        if (_simulation is null || _cameraController is null)
        {
            return;
        }
        var positions = new List<NVector2>();
        foreach (var unit in SelectedLivingUnits())
        {
            positions.Add(_simulation.Units.Positions[unit]);
        }
        foreach (var buildingValue in _selectedBuildings.OrderBy(value => value))
        {
            var buildingId = new GameplayBuildingId(buildingValue);
            if (!_simulation.Construction.IsAlive(buildingId)) continue;
            var building = _simulation.Construction.Observe(buildingId);
            if (building.PlayerId != PlayerTeam) continue;
            positions.Add((building.Bounds.Min + building.Bounds.Max) * 0.5f);
        }
        _cameraController.Focus(positions.ToArray());
        ApplyCameraState();
    }

    private SelectionCandidate[] BuildSelectionCandidates()
    {
        if (_simulation is null)
        {
            return [];
        }
        var result = new SelectionCandidate[_simulation.Units.Count];
        for (var unit = 0; unit < result.Length; unit++)
        {
            result[unit] = new SelectionCandidate(
                unit,
                _selectionTypeIds[unit],
                _simulation.Combat.Teams[unit],
                _simulation.Units.Alive[unit],
                _simulation.Units.Positions[unit],
                _simulation.Units.Radii[unit]);
        }
        return result;
    }

    private void HandleControlGroup(
        int group,
        bool assign,
        bool add,
        bool steal)
    {
        if (_simulation is null || _controlGroups is null)
        {
            return;
        }

        var selected = SelectedControlGroupEntities();
        if (steal)
        {
            if (add) _controlGroups.StealAdd(group, selected);
            else _controlGroups.StealAssign(group, selected);
            return;
        }
        if (assign)
        {
            _controlGroups.Assign(group, selected);
            return;
        }
        if (add)
        {
            _controlGroups.Add(group, selected);
            return;
        }

        _targetCommand = null;
        var recalled = _controlGroups.Recall(group, IsAvailableControlGroupEntity);
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        foreach (var entity in recalled)
        {
            if (entity.Kind == ControlGroupEntityKind.Unit)
            {
                _selectedUnits.Add(entity.EntityId);
            }
            else
            {
                _selectedBuildings.Add(entity.EntityId);
            }
        }
        if (_controlGroupRecallTracker.Register(
                group, Time.GetTicksMsec() / 1000.0) && recalled.Length > 0)
        {
            FocusCameraOnSelection();
        }
    }

    private ControlGroupEntity[] SelectedControlGroupEntities()
    {
        var units = SelectedLivingUnits();
        var result = new List<ControlGroupEntity>(
            units.Length + _selectedBuildings.Count);
        result.AddRange(units.Select(unit => new ControlGroupEntity(
            ControlGroupEntityKind.Unit, unit)));
        if (_simulation is null) return result.ToArray();
        foreach (var value in _selectedBuildings.OrderBy(value => value))
        {
            var id = new GameplayBuildingId(value);
            if (!_simulation.Construction.IsAlive(id) ||
                _simulation.Construction.Observe(id).PlayerId != PlayerTeam) continue;
            result.Add(new ControlGroupEntity(ControlGroupEntityKind.Building, value));
        }
        return result.ToArray();
    }

    private bool IsAvailableControlGroupEntity(ControlGroupEntity entity)
    {
        if (_simulation is null) return false;
        if (entity.Kind == ControlGroupEntityKind.Unit)
        {
            return (uint)entity.EntityId < (uint)_simulation.Units.Count &&
                   _simulation.Units.Alive[entity.EntityId] &&
                   _simulation.Combat.Teams[entity.EntityId] == PlayerTeam;
        }
        var id = new GameplayBuildingId(entity.EntityId);
        return _simulation.Construction.IsAlive(id) &&
               _simulation.Construction.Observe(id).PlayerId == PlayerTeam;
    }

    private static bool TryReadControlGroup(
        InputEventKey keyEvent,
        out int group)
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

    private int[] SelectedLivingUnits()
    {
        if (_simulation is null)
        {
            return [];
        }
        return _selectedUnits
            .Where(unit =>
                _simulation.Units.Alive[unit] &&
                _simulation.Combat.Teams[unit] == PlayerTeam)
            .OrderBy(unit => unit)
            .ToArray();
    }

    private SmartCommandTarget ResolveSmartCommandTarget(
        int selectedUnit,
        NVector2 position)
    {
        if (_simulation is null)
        {
            return new SmartCommandTarget(SmartCommandTargetKind.Ground, position);
        }

        var best = -1;
        var bestDistanceSquared = 18f * 18f;
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit])
            {
                continue;
            }
            if (_playerView is not null && !_visibleUnitIds.Contains(unit))
                continue;
            var distanceSquared = NVector2.DistanceSquared(
                position, _simulation.Units.Positions[unit]);
            var hitRadius = _simulation.Units.Radii[unit] + 5f;
            if (distanceSquared <= hitRadius * hitRadius &&
                distanceSquared < bestDistanceSquared)
            {
                best = unit;
                bestDistanceSquared = distanceSquared;
            }
        }

        if (best < 0)
        {
            foreach (var building in _simulation.Construction.CreateOverview())
            {
                if (!building.IsTerminal &&
                    (_playerView is null ||
                     _visibleBuildingIds.Contains(building.Id.Value)) &&
                    building.Bounds.Contains(position))
                {
                    var ownBuilding = building.PlayerId ==
                                      _simulation.Combat.Teams[selectedUnit];
                    return new SmartCommandTarget(
                        ownBuilding
                            ? SmartCommandTargetKind.FriendlyBuilding
                            : SmartCommandTargetKind.EnemyBuilding,
                        (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                        Building: building.Id.Value);
                }
            }
            if (_playerView is not null)
            {
                foreach (var resource in _playerView.Resources)
                {
                    if (NVector2.DistanceSquared(position, resource.Position) <=
                        26f * 26f)
                    {
                        return new SmartCommandTarget(
                            SmartCommandTargetKind.ResourceNode,
                            resource.Position,
                            ResourceNode: resource.NodeId.Value);
                    }
                }
            }
            return new SmartCommandTarget(SmartCommandTargetKind.Ground, position);
        }
        var kind = _simulation.Combat.Teams[selectedUnit] ==
                   _simulation.Combat.Teams[best]
            ? SmartCommandTargetKind.FriendlyUnit
            : SmartCommandTargetKind.EnemyUnit;
        return new SmartCommandTarget(kind, _simulation.Units.Positions[best], best);
    }

    private RallyTarget ResolveProductionRallyTarget(NVector2 position)
    {
        if (_simulation is null) return RallyTarget.Ground(position);

        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit] ||
                _simulation.Combat.Teams[unit] != PlayerTeam)
                continue;
            var hitRadius = _simulation.Units.Radii[unit] + 5f;
            if (NVector2.DistanceSquared(
                    position, _simulation.Units.Positions[unit]) <=
                hitRadius * hitRadius)
                return RallyTarget.Friendly(
                    unit, _simulation.Units.Positions[unit]);
        }

        if (_economyOverview is not null)
        {
            foreach (var node in _economyOverview.ResourceNodes)
            {
                if (NVector2.DistanceSquared(position, node.Position) <= 26f * 26f)
                    return RallyTarget.Resource(node.Id, node.Position);
            }
        }
        return RallyTarget.Ground(position);
    }

    private void SelectInRect(Rect2 rect, bool additive, bool sameType)
    {
        if (_simulation is null)
        {
            return;
        }

        if (!additive)
        {
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
        }

        if (rect.Size.LengthSquared() < 5f * 5f)
        {
            var click = rect.Position + rect.Size * 0.5f;
            var candidates = BuildSelectionCandidates();
            var best = SelectionFilter.SelectPoint(
                candidates,
                GodotPathProvider.ToNumerics(click),
                PlayerTeam);

            if (best >= 0)
            {
                var selected = sameType && _cameraController is not null
                    ? SelectionFilter.SelectVisibleSameType(
                        candidates,
                        best,
                        _cameraController.VisibleWorld,
                        PlayerTeam)
                    : [best];
                foreach (var unit in selected)
                {
                    _selectedUnits.Add(unit);
                }
                if (!additive) _selectedBuildings.Clear();
            }
            else
            {
                var clickWorld = GodotPathProvider.ToNumerics(click);
                var building = _simulation.Construction.CreateOverview()
                    .Where(value =>
                        !value.IsTerminal && value.PlayerId == PlayerTeam &&
                        value.Bounds.Contains(clickWorld))
                    .OrderByDescending(value => value.Id.Value)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(building.Type.Name))
                {
                    _selectedBuildings.Add(building.Id.Value);
                }
            }

            return;
        }

        var selectedInBox = SelectionFilter.SelectBox(
            BuildSelectionCandidates(),
            new SimRect(
                GodotPathProvider.ToNumerics(rect.Position),
                GodotPathProvider.ToNumerics(rect.End)),
            PlayerTeam);
        for (var index = 0; index < selectedInBox.Length; index++)
        {
            _selectedUnits.Add(selectedInBox[index]);
        }
    }

    private void DrawUnits()
    {
        if (_simulation is null)
        {
            return;
        }

        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit])
            {
                continue;
            }
            if (_playerView is not null && !_visibleUnitIds.Contains(unit))
                continue;
            var position = GodotPathProvider.ToGodot(_simulation.Units.Positions[unit]);
            var radius = _simulation.Units.Radii[unit];
            var mode = _simulation.Units.Modes[unit];
            var color = mode switch
            {
                UnitMoveMode.Arrived => new Color("5fd18b"),
                UnitMoveMode.Hold => new Color("f5b84b"),
                UnitMoveMode.WaitingForPath => new Color("a78bfa"),
                _ => new Color("5da9e9")
            };
            if (_simulation.Units.RecoveryStages[unit] == RecoveryStage.Unreachable)
            {
                color = new Color("d95763");
            }
            if (_simulation.Combat.Teams[unit] != 0)
            {
                color = _simulation.Combat.Teams[unit] == 1
                    ? new Color("4da3ff")
                    : new Color("f05b64");
                if (_simulation.Combat.Phases[unit] == CombatPhase.Attacking)
                {
                    color = color.Lightened(0.25f);
                }
            }
            color = _simulation.Units.ChokePhases[unit] == ChokePhase.Approaching &&
                    !_simulation.Units.ChokeAdmitted[unit]
                ? new Color("ff5f87")
                : _simulation.Units.ChokePhases[unit] switch
            {
                ChokePhase.Approaching => new Color("d88cff"),
                ChokePhase.Traversing => new Color("ff9e57"),
                ChokePhase.Exiting => new Color("ffd166"),
                _ => color
            };

            var isWorker = _simulation.Economy.IsWorker(unit);
            var isHeavy = !isWorker &&
                (_simulation.Combat.Attributes[unit] & CombatAttribute.Armored) != 0;
            if (isWorker)
            {
                Vector2[] points = [
                    position + new Vector2(0f, -radius - 2f),
                    position + new Vector2(radius + 2f, 0f),
                    position + new Vector2(0f, radius + 2f),
                    position + new Vector2(-radius - 2f, 0f)
                ];
                DrawColoredPolygon(points, color);
                DrawPolyline(
                    new Vector2[] {
                        points[0], points[1], points[2], points[3], points[0]
                    }, color.Lightened(0.35f), 1.5f);
                DrawWorkerEconomyState(unit, position, radius, color);
            }
            else if (isHeavy)
            {
                DrawCircle(position, radius + 2f, color);
                DrawCircle(position, radius * 0.53f, color.Darkened(0.32f));
                DrawArc(position, radius + 4f, 0f, MathF.Tau, 8,
                    color.Lightened(0.32f), 2f);
            }
            else
            {
                DrawCircle(position, radius, color);
                DrawArc(position, radius + 2f, -0.65f, 0.65f, 5,
                    color.Lightened(0.38f), 2f);
            }
            if (_simulation.Combat.Teams[unit] != 0)
            {
                var healthRatio = Math.Clamp(
                    _simulation.Combat.Health[unit] /
                    _simulation.Combat.MaximumHealth[unit], 0f, 1f);
                var barStart = position + new Vector2(-radius, -radius - 5f);
                DrawLine(
                    barStart,
                    barStart + new Vector2(radius * 2f, 0f),
                    new Color("252a34"), 2f);
                DrawLine(
                    barStart,
                    barStart + new Vector2(radius * 2f * healthRatio, 0f),
                    new Color("72e06a"), 2f);
            }
            var velocity = _simulation.Units.Velocities[unit];
            if (velocity.LengthSquared() > 2f)
            {
                var heading = GodotPathProvider.ToGodot(NVector2.Normalize(velocity));
                DrawLine(position, position + heading * (radius + 5f),
                    new Color("d9f1ff"), 1.5f);
            }

            if (_selectedUnits.Contains(unit))
            {
                DrawArc(position, radius + 3f, 0f, MathF.Tau, 18,
                    new Color("65f5ff"), 2f);
            }
        }
    }

    private void DrawWorkerEconomyState(
        int unit,
        Vector2 position,
        float radius,
        Color teamColor)
    {
        if (_simulation is null || !_simulation.Economy.IsWorker(unit)) return;
        var worker = _simulation.Economy.Worker(unit);
        var label = worker.State switch
        {
            WorkerEconomyState.GoingToResource => "SCV > RES",
            WorkerEconomyState.WaitingForResource => "SCV WAIT",
            WorkerEconomyState.Gathering => "SCV MINING",
            WorkerEconomyState.ReturningCargo => worker.CargoKind ==
                EconomyResourceKind.Minerals
                    ? $"SCV < M+{worker.CargoAmount}"
                    : $"SCV < G+{worker.CargoAmount}",
            _ => "SCV IDLE"
        };
        var routeColor = worker.CargoKind == EconomyResourceKind.VespeneGas
            ? new Color("61db83")
            : new Color("65c9ff");
        if (worker.State == WorkerEconomyState.GoingToResource &&
            _economyOverview is not null)
        {
            foreach (var node in _economyOverview.ResourceNodes)
            {
                if (node.Id != worker.TargetNode) continue;
                DrawLine(position, GodotPathProvider.ToGodot(node.Position),
                    routeColor with { A = 0.32f }, 1.2f);
                break;
            }
        }
        else if (worker.State == WorkerEconomyState.ReturningCargo)
        {
            DrawLine(position,
                GodotPathProvider.ToGodot(_simulation.Units.MoveGoals[unit]),
                routeColor with { A = 0.38f }, 1.4f);
            DrawCircle(position + new Vector2(radius + 3f, -radius - 1f),
                3.2f, routeColor);
        }
        DrawString(ThemeDB.FallbackFont,
            position + new Vector2(-24f, radius + 15f), label,
            HorizontalAlignment.Center, 48f, 9,
            worker.State == WorkerEconomyState.Idle
                ? teamColor.Darkened(0.15f)
                : routeColor.Lightened(0.18f));
    }

    private void DrawBuildingIdentity(
        BuildingFunctionKind function,
        Rect2 rect,
        Color teamColor)
    {
        var center = rect.GetCenter();
        var extent = MathF.Min(rect.Size.X, rect.Size.Y) * 0.22f;
        var ink = teamColor.Lightened(0.45f);
        switch (function)
        {
            case BuildingFunctionKind.TownHall:
                DrawRect(new Rect2(center - new Vector2(extent, extent),
                    new Vector2(extent * 2f, extent * 2f)), ink, false, 3f);
                DrawLine(center - new Vector2(extent, 0f),
                    center + new Vector2(extent, 0f), ink, 2f);
                DrawLine(center - new Vector2(0f, extent),
                    center + new Vector2(0f, extent), ink, 2f);
                break;
            case BuildingFunctionKind.Supply:
                for (var offset = -1; offset <= 1; offset++)
                    DrawLine(center + new Vector2(-extent, offset * 6f),
                        center + new Vector2(extent, offset * 6f), ink, 3f);
                break;
            case BuildingFunctionKind.Production:
                DrawLine(center + new Vector2(-extent, extent),
                    center + new Vector2(0f, -extent), ink, 3f);
                DrawLine(center + new Vector2(0f, -extent),
                    center + new Vector2(extent, extent), ink, 3f);
                break;
            case BuildingFunctionKind.Refinery:
                DrawCircle(center, extent, ink with { A = 0.26f });
                DrawArc(center, extent, 0f, MathF.Tau, 16, ink, 3f);
                break;
            case BuildingFunctionKind.Research:
                DrawPolyline(new Vector2[] {
                    center + new Vector2(0f, -extent),
                    center + new Vector2(extent, 0f),
                    center + new Vector2(0f, extent),
                    center + new Vector2(-extent, 0f),
                    center + new Vector2(0f, -extent)
                }, ink, 3f);
                break;
        }
    }

    private void DrawSelectedPaths()
    {
        if (_simulation is null)
        {
            return;
        }

        foreach (var unit in _selectedUnits.Take(18))
        {
            var path = _simulation.Units.Paths[unit];
            if (path is null || path.Points.Length < 2)
            {
                continue;
            }

            var points = new Vector2[path.Points.Length - path.Cursor + 1];
            points[0] = GodotPathProvider.ToGodot(_simulation.Units.Positions[unit]);
            for (var index = path.Cursor; index < path.Points.Length; index++)
            {
                points[index - path.Cursor + 1] = GodotPathProvider.ToGodot(path.Points[index]);
            }

            DrawPolyline(points, new Color(0.25f, 0.75f, 1f, 0.3f), 1f);
        }
    }

    private void DrawSelectedSlots()
    {
        if (_simulation is null)
        {
            return;
        }

        foreach (var unit in _selectedUnits)
        {
            var slot = GodotPathProvider.ToGodot(_simulation.Units.SlotTargets[unit]);
            DrawArc(slot, 3f, 0f, MathF.Tau, 10,
                new Color(0.3f, 1f, 0.5f, 0.55f), 1f);
        }
    }

    private void DrawPortalGraph()
    {
        if (_routePlanner is null || _simulation is null)
        {
            return;
        }

        var nodes = _routePlanner.Nodes;
        var edges = _routePlanner.Edges;
        for (var edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
        {
            var edge = edges[edgeIndex];
            var color = edge.ChokeId >= 0
                ? new Color(1f, 0.55f, 0.2f, 0.7f)
                : new Color(0.3f, 0.8f, 1f, 0.35f);
            DrawLine(
                GodotPathProvider.ToGodot(nodes[edge.FromNode].Position),
                GodotPathProvider.ToGodot(nodes[edge.ToNode].Position),
                color,
                edge.ChokeId >= 0 ? 3f : 1.5f);
        }

        for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            DrawCircle(
                GodotPathProvider.ToGodot(nodes[nodeIndex].Position),
                5f,
                new Color("4fd1ff"));
        }

        if (_chokeController is not null)
        {
            var definitions = _chokeController.Definitions;
            for (var chokeIndex = 0; chokeIndex < definitions.Length; chokeIndex++)
            {
                var choke = definitions[chokeIndex];
                var halfWidth = choke.Normal * choke.Width * 0.5f;
                DrawLine(
                    GodotPathProvider.ToGodot(choke.A - halfWidth),
                    GodotPathProvider.ToGodot(choke.B - halfWidth),
                    new Color(1f, 0.55f, 0.2f, 0.45f),
                    1f);
                DrawLine(
                    GodotPathProvider.ToGodot(choke.A + halfWidth),
                    GodotPathProvider.ToGodot(choke.B + halfWidth),
                    new Color(1f, 0.55f, 0.2f, 0.45f),
                    1f);

                var traffic = _chokeController.TrafficSnapshots[chokeIndex];
                var positiveOpen = traffic.ActiveDirection > 0 && !traffic.Draining;
                var negativeOpen = traffic.ActiveDirection < 0 && !traffic.Draining;
                DrawLine(
                    GodotPathProvider.ToGodot(choke.A - halfWidth),
                    GodotPathProvider.ToGodot(choke.A + halfWidth),
                    positiveOpen ? new Color("55e68a") : new Color("ff5f67"),
                    3f);
                DrawLine(
                    GodotPathProvider.ToGodot(choke.B - halfWidth),
                    GodotPathProvider.ToGodot(choke.B + halfWidth),
                    negativeOpen ? new Color("55e68a") : new Color("ff5f67"),
                    3f);
                if (traffic.ActiveDirection != 0)
                {
                    var center = (choke.A + choke.B) * 0.5f;
                    var arrow = choke.Axis * traffic.ActiveDirection * 34f;
                    DrawLine(
                        GodotPathProvider.ToGodot(center - arrow),
                        GodotPathProvider.ToGodot(center + arrow),
                        traffic.Draining ? new Color("ffd166") : new Color("55e68a"),
                        4f);
                }
            }
        }

        var route = _simulation.LastIssuedGroupRoute.Waypoints;
        for (var waypoint = 0; waypoint < route.Length; waypoint++)
        {
            var position = GodotPathProvider.ToGodot(route[waypoint]);
            DrawArc(position, 9f, 0f, MathF.Tau, 16, new Color("f7ef72"), 2.5f);
            if (waypoint > 0)
            {
                DrawLine(
                    GodotPathProvider.ToGodot(route[waypoint - 1]),
                    position,
                    new Color(0.97f, 0.94f, 0.45f, 0.8f),
                    2f);
            }
        }
    }

    private void DrawGrid()
    {
        var gridColor = new Color(0.18f, 0.25f, 0.32f, 0.35f);
        for (var x = 0; x <= 1280; x += 40)
        {
            DrawLine(new Vector2(x, 0f), new Vector2(x, 720f), gridColor);
        }

        for (var y = 0; y <= 720; y += 40)
        {
            DrawLine(new Vector2(0f, y), new Vector2(1280f, y), gridColor);
        }
    }

    private void CreateHud()
    {
        DestroyHud();
        _targetCommandOverlay ??= new RtsTargetCommandOverlay();
        if (_targetCommandOverlay.GetParent() is null)
            AddChild(_targetCommandOverlay);
        _hud = new Label
        {
            Position = new Vector2(12f, 8f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color("d8e7f3")
        };
        _hud.AddThemeFontSizeOverride("font_size", 15);
        _hudLayer = new CanvasLayer { Layer = 10 };
        AddChild(_hudLayer);
        _hudRoot = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _hudLayer.AddChild(_hudRoot);
        _hudRoot.AddChild(_hud);
        if (_visualTest is null || _visualTest.Id == "minimap-interaction")
        {
            _minimap = new RtsMinimapControl
            {
                Size = new Vector2(230f, 140f)
            };
            _minimap.FocusRequested += FocusCameraAt;
            _minimap.SmartCommandRequested += IssueMinimapCommand;
            _hudRoot.AddChild(_minimap);
        }
        if (_visualTest is null || _visualTest.Id is
                "operation-mixed-command-card" or
                "operation-target-command-mode" or
                "operation-build-placement-mode" or
                "operation-production-group-batch" or
                "minimap-interaction")
        {
            _commandCard = new RtsCommandCardControl
            {
                Size = new Vector2(520f, 190f)
            };
            _commandCard.ActionRequested += ExecuteCommandCardAction;
            _commandCard.SubgroupCycleRequested += CycleSelectionSubgroup;
            _hudRoot.AddChild(_commandCard);
        }
        if (_visualTest?.Id == "resource-hot-reload" && _hotReloadPlan is not null)
        {
            _resourceReloadControl = new RtsResourceReloadControl
            {
                Size = new Vector2(720f, 190f)
            };
            _resourceReloadControl.SetPlan(_hotReloadPlan);
            _hudRoot.AddChild(_resourceReloadControl);
        }
        if (_visualTest?.Id == "building-connectivity-diff-preview" &&
            _buildingConnectivityDiff is not null)
        {
            _buildingConnectivityDiffControl =
                new RtsBuildingConnectivityDiffControl
                {
                    Size = new Vector2(720f, 218f)
                };
            _buildingConnectivityDiffControl.SetSnapshot(
                _buildingConnectivityDiff);
            _hudRoot.AddChild(_buildingConnectivityDiffControl);
        }
        if (_visualTest?.Id == "resource-file-watch-workflow")
        {
            _resourceWatchControl = new RtsResourceWatchControl
            {
                Size = new Vector2(720f, 190f)
            };
            _resourceWatchControl.SetSnapshot(_resourceWatchStatus);
            _hudRoot.AddChild(_resourceWatchControl);
        }
        if (_visualTest?.Id == "economy-dual-resource")
        {
            _economyControl = new RtsEconomyControl
            {
                Size = new Vector2(720f, 180f)
            };
            if (_economyOverview is not null)
            {
                _economyControl.SetSnapshot(_economyOverview);
            }
            _hudRoot.AddChild(_economyControl);
        }
        UpdateHudLayout();
    }

    private void DestroyHud()
    {
        if (_hudLayer is not null && GodotObject.IsInstanceValid(_hudLayer))
            _hudLayer.QueueFree();
        _hudLayer = null;
        _hudRoot = null;
        _hud = null;
        _minimap = null;
        _commandCard = null;
        _resourceReloadControl = null;
        _buildingConnectivityDiffControl = null;
        _resourceWatchControl = null;
        _economyControl = null;
    }

    private void UpdateTargetCommandOverlay()
    {
        var worldPosition = ScreenToWorld(_pointerScreen);
        UpdateBuildTargetPreview(GodotPathProvider.ToNumerics(worldPosition));
        _targetCommandOverlay?.SetSnapshot(
            _targetCommand, worldPosition, _buildTargetPreview);
    }

    private void UpdateHudLayout()
    {
        if (_hudRoot is null)
        {
            return;
        }
        var viewportSize = GetViewportRect().Size;
        if (_hudRoot.Size != viewportSize)
        {
            _hudRoot.Size = viewportSize;
        }
        if (_minimap is not null)
        {
            _minimap.Position = viewportSize - _minimap.Size - new Vector2(12f, 12f);
        }
        if (_resourceReloadControl is not null)
        {
            _resourceReloadControl.Position = new Vector2(
                24f,
                viewportSize.Y - _resourceReloadControl.Size.Y - 20f);
        }
        if (_buildingConnectivityDiffControl is not null)
        {
            _buildingConnectivityDiffControl.Position = new Vector2(
                24f,
                viewportSize.Y - _buildingConnectivityDiffControl.Size.Y - 20f);
        }
        if (_resourceWatchControl is not null)
        {
            _resourceWatchControl.Position = new Vector2(
                24f,
                viewportSize.Y - _resourceWatchControl.Size.Y - 20f);
        }
        if (_economyControl is not null)
        {
            _economyControl.Position = new Vector2(
                24f,
                viewportSize.Y - _economyControl.Size.Y - 20f);
        }
        if (_commandCard is not null)
        {
            _commandCard.Position = new Vector2(
                (viewportSize.X - _commandCard.Size.X) * 0.5f,
                viewportSize.Y - _commandCard.Size.Y - 12f);
        }
    }

    private void UpdateHud()
    {
        if (_hud is null || _simulation is null)
        {
            return;
        }

        var metrics = _simulation.Metrics;
        if (_simulation.Economy.Players.IsRegistered(PlayerTeam))
        {
            _economyOverview = _simulation.Economy.CreateOverview(
                PlayerTeam, _simulation.Units.Count);
            _economyControl?.SetSnapshot(_economyOverview);
            if (_visualTest?.OmniscientRendering == true)
            {
                _playerView = null;
                _visibleUnitIds.Clear();
                _visibleBuildingIds.Clear();
                _visibleResources.Clear();
            }
            else
            {
                _playerView = _simulation.CreatePlayerView(PlayerTeam);
                _visibleUnitIds.Clear();
                _visibleBuildingIds.Clear();
                _visibleResources.Clear();
                foreach (var unit in _playerView.Units)
                    _visibleUnitIds.Add(unit.UnitId);
                foreach (var building in _playerView.Buildings)
                    _visibleBuildingIds.Add(building.BuildingId.Value);
                foreach (var resource in _playerView.Resources)
                    _visibleResources.Add(resource.NodeId.Value, resource);
            }
        }
        else
        {
            _playerView = null;
            _visibleUnitIds.Clear();
            _visibleBuildingIds.Clear();
            _visibleResources.Clear();
        }
        UpdateCommandCard();
        var chokeUnits = 0;
        var livingUnits = 0;
        var engagedUnits = 0;
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (_playerView is not null && !_visibleUnitIds.Contains(unit))
                continue;
            if (_simulation.Units.Alive[unit])
            {
                livingUnits++;
            }
            if (_simulation.Combat.Phases[unit] is CombatPhase.Chasing or CombatPhase.Attacking)
            {
                engagedUnits++;
            }
            if (_simulation.Units.ChokePhases[unit] != ChokePhase.None)
            {
                chokeUnits++;
            }
        }

        var title = _visualTest is null
            ? "Godot 4.7 .NET RTS movement demo"
            : $"REC  {_visualTest.DisplayName}  " +
              $"{metrics.Tick / 60f:0.0}/{_visualTest.DurationTicks / 60f:0.0}s  " +
              $"[{_visualTest.Phase}]";
        var unitText = _playerView is null
            ? $"{livingUnits}/{_simulation.Units.Count}"
            : $"{livingUnits} visible";
        var movingUnits = _playerView?.Units.Count(value =>
            value.MoveMode is UnitMoveMode.Moving or UnitMoveMode.WaitingForPath)
            ?? metrics.MovingUnits;
        var arrivedUnits = _playerView?.Units.Count(value =>
            value.MoveMode == UnitMoveMode.Arrived) ?? metrics.ArrivedUnits;
        var buildingCount = _playerView?.Buildings.Length ??
                            _world?.DynamicOccupancy.Count ?? 0;
        var productionOrders = _playerView is null
            ? _simulation.Production.ActiveOrderCount
            : _playerView.Buildings
                .Where(value => value.Relation == PlayerEntityRelation.Own)
                .Sum(value => _simulation.Production
                    .Observe(value.BuildingId).Orders.Length);
        var researchOrders = _playerView is null
            ? _simulation.Technology.ActiveOrderCount
            : _playerView.Buildings
                .Where(value => value.Relation == PlayerEntityRelation.Own)
                .Sum(value => _simulation.Technology
                    .Observe(value.BuildingId).Orders.Length);
        var match = _simulation.Match.CreateSnapshot(
            _simulation.Construction,
            _simulation.Economy,
            _simulation.Units,
            _simulation.Combat);
        var matchText = match.Phase switch
        {
            MatchPhase.Setup => "match setup",
            MatchPhase.Running => "match running  " + string.Join("  ",
                match.Players.Select(value =>
                    $"P{value.PlayerId}:{value.Status} " +
                    $"B{value.ActiveBuildings}/TH{value.TownHalls}/" +
                    $"Prod{value.ProductionFacilities}")),
            MatchPhase.Completed when match.WinnerPlayerId >= 0 =>
                $"MATCH COMPLETE  WINNER P{match.WinnerPlayerId}",
            MatchPhase.Completed => "MATCH COMPLETE  DRAW",
            _ => throw new ArgumentOutOfRangeException()
        };
        var trafficText = "traffic none";
        if (_chokeController is not null && _chokeController.TrafficSnapshots.Length > 0)
        {
            var traffic = _chokeController.TrafficSnapshots[0];
            var direction = traffic.ActiveDirection > 0
                ? ">"
                : traffic.ActiveDirection < 0
                    ? "<"
                    : "-";
            trafficText = $"traffic {direction}{(traffic.Draining ? " drain" : "")}  " +
                          $"queue +{traffic.PositiveQueue}/-{traffic.NegativeQueue}  " +
                          $"inflight {traffic.InFlight}/{traffic.Capacity}  " +
                          $"maxwait +{traffic.MaximumPositiveWaitTicks}/" +
                          $"-{traffic.MaximumNegativeWaitTicks}  conflicts {traffic.ConflictTicks}  " +
                          $"blockers {traffic.HardBlockers} ({traffic.BlockedTicks} ticks)";
        }

        _hud.Text =
            $"{title}  |  units {unitText}  " +
            $"selected {_selectedUnits.Count + _selectedBuildings.Count}  " +
            $"groups {_selectionSnapshot.Subgroups.Length}  moving {movingUnits}  " +
            $"arrived {arrivedUnits}  queued {metrics.PendingUnitOrders}  " +
            $"engaged {engagedUnits}  choke {chokeUnits}  " +
            $"route {_simulation.LastIssuedGroupRoute.Waypoints.Length}  " +
            $"route plans {metrics.GroupRoutePlans} shared {metrics.SharedRouteAssignments}  " +
            $"slot swaps {metrics.DestinationSlotSwaps}  " +
            $"local rematch {metrics.DestinationLocalRematches}/{metrics.DestinationLocalRematchedUnits}  " +
            $"overflow {metrics.DestinationOverflowAssignments}  " +
            $"stall {metrics.MaximumDestinationStallTicks}  " +
            $"near {metrics.MaximumDestinationNearTicks}  " +
            $"yield {metrics.DestinationYieldEvents}/{metrics.ActiveDestinationYields}  " +
            $"path queue {metrics.PendingPathRequests}  nav r{metrics.NavigationRevision}  " +
            $"bake reload {metrics.ClearanceBakeReloads}  " +
            $"buildings {buildingCount}  " +
            $"production {productionOrders}  " +
            $"research {researchOrders}  " +
            $"invalidated {metrics.NavigationInvalidations}  " +
            $"recovery {metrics.RecoveryEvents}  unreachable {metrics.UnreachableUnits}\n" +
            $"map {ActiveNavigationLabel()}  " +
            $"tick {metrics.TotalMilliseconds:0.00}ms  econ {metrics.EconomyMilliseconds:0.00}  " +
            $"path {metrics.PathMilliseconds:0.00}  " +
            $"steer {metrics.SteeringMilliseconds:0.00}  " +
            $"collision {metrics.CollisionMilliseconds:0.00}  " +
            $"alloc {metrics.AllocatedBytes / 1024.0:0.0}KB\n" +
            $"{matchText}\n" +
            $"{trafficText}\n" +
            "LMB select  RMB smart  Shift+RMB queue  A+RMB attack-move  " +
            "Ctrl+# assign  Shift+# add  Alt+# steal  # recall  Space all  S stop  H hold  " +
            "B place building  X remove building  D debug  R reset";
        UpdateMinimap();
    }

    private void UpdateCommandCard()
    {
        if (_simulation is null || _commandCard is null) return;
        _selectedBuildings.RemoveWhere(value =>
        {
            var id = new GameplayBuildingId(value);
            return !_simulation.Construction.IsAlive(id) ||
                   _simulation.Construction.Observe(id).PlayerId != PlayerTeam;
        });
        var entities = new List<GameplaySelectionEntity>();
        foreach (var unit in _selectedUnits.OrderBy(value => value))
        {
            if ((uint)unit >= (uint)_simulation.Units.Count ||
                !_simulation.Units.Alive[unit] ||
                _simulation.Combat.Teams[unit] != PlayerTeam)
                continue;
            var worker = _simulation.Economy.IsWorkerOwnedBy(unit, PlayerTeam);
            var declaredType = (uint)unit < (uint)_selectionTypeIds.Length
                ? _selectionTypeIds[unit]
                : -1;
            var typeId = declaredType >= 0 ? declaredType : worker ? 10_000 : 20_000;
            var typeName = declaredType >= 0 && _gameplayProfiles is not null &&
                           declaredType < _gameplayProfiles.UnitProfiles.Length
                ? _gameplayProfiles.Unit(declaredType).Name
                : worker ? "Worker" : "Combat Unit";
            entities.Add(new GameplaySelectionEntity(
                worker ? GameplaySelectionKind.Worker : GameplaySelectionKind.CombatUnit,
                unit, typeId, typeName, _simulation.Units.Positions[unit]));
        }
        foreach (var buildingValue in _selectedBuildings.OrderBy(value => value))
        {
            var buildingId = new GameplayBuildingId(buildingValue);
            if (!_simulation.Construction.IsAlive(buildingId)) continue;
            var building = _simulation.Construction.Observe(buildingId);
            if (building.PlayerId != PlayerTeam) continue;
            entities.Add(new GameplaySelectionEntity(
                GameplaySelectionKind.Building,
                buildingValue,
                building.Type.Id,
                building.Type.Name,
                (building.Bounds.Min + building.Bounds.Max) * 0.5f));
        }
        _selectionSnapshot = GameplaySelectionSnapshot.Create(
            entities, _activeSelectionSubgroup);
        _activeSelectionSubgroup = _selectionSnapshot.ActiveSubgroup?.Key;
        _commandCard.SetSnapshot(CommandCardComposer.Compose(
            _selectionSnapshot, BuildCommandCardCandidates()));
    }

    private List<CommandCardActionCandidate> BuildCommandCardCandidates()
    {
        var result = new List<CommandCardActionCandidate>();
        if (_simulation is null || _selectionSnapshot.ActiveSubgroup is not { } active)
            return result;
        var key = active.Key;
        if (key.Kind is GameplaySelectionKind.Worker or GameplaySelectionKind.CombatUnit)
        {
            result.Add(new(key, CommandCardActionKind.Move, -1, -1,
                "Move", true, "Choose target", 1));
            result.Add(new(key, CommandCardActionKind.AttackMove, -1, -1,
                "Attack Move", true, "Choose target", 2));
            result.Add(new(key, CommandCardActionKind.Stop, -1, -1,
                "Stop", true, "S", 10));
            result.Add(new(key, CommandCardActionKind.Hold, -1, -1,
                "Hold Position", true, "H", 20));
            if (key.Kind == GameplaySelectionKind.Worker &&
                _buildingTypes is not null)
            {
                foreach (var type in _buildingTypes.Types)
                {
                    result.Add(new(key, CommandCardActionKind.Build, -1, type.Id,
                        $"Build {type.Name}", true,
                        $"{type.Cost.Minerals}M {type.Cost.VespeneGas}G",
                        30 + type.Id));
                }
            }
            return result;
        }

        var buildings = active.Members
            .Select(value => new GameplayBuildingId(value.EntityId))
            .Where(value => _simulation.Construction.IsAlive(value))
            .Select(value => _simulation.Construction.Observe(value))
            .Where(value => value.PlayerId == PlayerTeam)
            .OrderBy(value => value.Id.Value)
            .ToArray();
        if (buildings.Length == 0) return result;
        var unfinished = buildings
            .Where(value => value.State != BuildingLifecycleState.Completed)
            .ToArray();
        if (unfinished.Length > 0)
        {
            result.Add(new(key, CommandCardActionKind.CancelConstruction,
                unfinished.Length == 1 ? unfinished[0].Id.Value : -1,
                -1, $"Cancel Construction ×{unfinished.Length}", true,
                $"avg {unfinished.Average(value => value.Progress):P0}", 4));
        }
        var representative = buildings.FirstOrDefault(value =>
            value.State == BuildingLifecycleState.Completed);
        if (representative.State != BuildingLifecycleState.Completed)
            return result;
        if (_productionCatalog is not null)
            AddProductionGroupCandidates(result, key, buildings, representative.Type.Id);
        if (_technologyCatalog is not null)
        {
            var researcher = representative.Id;
            foreach (var technology in _technologyCatalog.Technologies)
            {
                if (technology.ResearcherBuildingTypeId != representative.Type.Id) continue;
                var availability = _simulation.Technology.ObserveAvailability(
                    PlayerTeam, researcher, technology,
                    _simulation.Construction, _simulation.Economy.Players);
                result.Add(new(key, CommandCardActionKind.Research,
                    researcher.Value, technology.Id,
                    $"Research {technology.Name}", availability.Available,
                    $"L{availability.CurrentLevel} {availability.Code}",
                    30 + technology.Id));
            }
            var queue = _simulation.Technology.Observe(researcher);
            if (queue.Orders.Length > 0)
            {
                result.Add(new(key, CommandCardActionKind.CancelResearch,
                    researcher.Value, queue.Orders[0].Id.Value,
                    "Cancel Research", true,
                    $"{queue.Orders[0].Progress:P0}", 90));
            }
        }
        return result;
    }

    private void AddProductionGroupCandidates(
        List<CommandCardActionCandidate> result,
        SelectionSubgroupKey key,
        IReadOnlyList<GameplayBuildingSnapshot> buildings,
        int buildingTypeId)
    {
        if (_simulation is null || _productionCatalog is null) return;
        var group = BuildProductionGroupSnapshot(
            buildings.Select(value => value.Id.Value));
        var recipes = new List<ProductionRecipeProfile>();
        foreach (var recipe in _productionCatalog.Recipes)
        {
            if (recipe.ProducerBuildingTypeId == buildingTypeId)
                recipes.Add(recipe);
        }
        if (recipes.Count > 0)
        {
            result.Add(new(key, CommandCardActionKind.Rally,
                -1, -1, $"Set Rally ×{group.ProducerCount}", true,
                "Ground / Resource / Unit", 5));
        }
        foreach (var recipe in recipes)
        {
            var availability = group.Producers.Select(producer =>
            {
                var value = _simulation.Production.ObserveAvailability(
                    PlayerTeam, new GameplayBuildingId(producer.BuildingId), recipe,
                    _simulation.Construction, _simulation.Economy.Players);
                return new ProductionBatchAvailability(
                    producer.BuildingId, value.Available, value.Code.ToString());
            });
            var plan = ProductionBatchPlanner.PlanTrain(group, availability);
            result.Add(new(key, CommandCardActionKind.Train,
                -1, recipe.Id,
                $"Train {recipe.UnitType.Name} ×{group.ProducerCount}",
                plan.CanIssue, plan.Status, 20 + recipe.Id));

            var cancelOrders = group.NewestMatchingOrders(recipe.Id);
            if (cancelOrders.Length > 0)
            {
                result.Add(new(key, CommandCardActionKind.CancelProductionBatch,
                    -1, recipe.Id,
                    $"Cancel {recipe.UnitType.Name} ×{cancelOrders.Length}",
                    true, $"newest per producer · queued {group.TotalOrders}",
                    80 + recipe.Id));
            }
        }
    }

    private ProductionGroupSnapshot BuildProductionGroupSnapshot(
        IEnumerable<int> buildingIds)
    {
        if (_simulation is null) return ProductionGroupSnapshot.Create([]);
        return ProductionGroupSnapshot.Create(buildingIds.Select(value =>
        {
            var queue = _simulation.Production.Observe(
                new GameplayBuildingId(value));
            return new ProductionGroupProducerSnapshot(
                value,
                queue.Orders.Select(order => new ProductionGroupOrderEntry(
                    order.Id.Value, order.Recipe.Id, order.Progress)).ToArray());
        }));
    }

    private void CycleSelectionSubgroup(int direction)
    {
        _selectionSnapshot = _selectionSnapshot.Cycle(direction);
        _activeSelectionSubgroup = _selectionSnapshot.ActiveSubgroup?.Key;
        UpdateCommandCard();
    }

    private void ExecuteCommandCardAction(CommandCardActionSnapshot action)
    {
        if (_simulation is null || !action.Enabled) return;
        var activeUnits = _selectionSnapshot.ActiveSubgroup?.Members
            .Where(value => value.Kind is GameplaySelectionKind.Worker or
                GameplaySelectionKind.CombatUnit)
            .Select(value => value.EntityId)
            .ToArray() ?? [];
        switch (action.Kind)
        {
            case CommandCardActionKind.Move:
                BeginTargetCommand(TargetCommandKind.Move, "Move");
                break;
            case CommandCardActionKind.AttackMove:
                BeginTargetCommand(TargetCommandKind.AttackMove, "Attack Move");
                break;
            case CommandCardActionKind.Rally:
                BeginTargetCommand(TargetCommandKind.Rally, "Set Rally");
                break;
            case CommandCardActionKind.Build when _buildingTypes is not null:
                BeginTargetCommand(
                    TargetCommandKind.Build,
                    $"Build {_buildingTypes.Type(action.DataId).Name}",
                    action.DataId);
                break;
            case CommandCardActionKind.Stop:
                _simulation.IssuePlayerStop(PlayerTeam, activeUnits);
                break;
            case CommandCardActionKind.Hold:
                _simulation.IssuePlayerHold(PlayerTeam, activeUnits);
                break;
            case CommandCardActionKind.Train when _productionCatalog is not null:
                ExecuteProductionBatch(_productionCatalog.Recipe(action.DataId));
                break;
            case CommandCardActionKind.CancelProduction:
                _simulation.CancelProduction(
                    PlayerTeam, new ProductionOrderId(action.DataId));
                break;
            case CommandCardActionKind.CancelProductionBatch:
                CancelNewestProductionBatch(action.DataId);
                break;
            case CommandCardActionKind.Research when _technologyCatalog is not null:
                _simulation.IssueResearch(
                    PlayerTeam, new GameplayBuildingId(action.TargetEntityId),
                    _technologyCatalog.Technology(action.DataId));
                break;
            case CommandCardActionKind.CancelResearch:
                _simulation.CancelResearch(
                    PlayerTeam, new ResearchOrderId(action.DataId));
                break;
            case CommandCardActionKind.CancelConstruction:
                if (action.TargetEntityId >= 0)
                {
                    _simulation.CancelConstruction(
                        PlayerTeam, new GameplayBuildingId(action.TargetEntityId));
                    _selectedBuildings.Remove(action.TargetEntityId);
                }
                else
                {
                    foreach (var member in _selectionSnapshot.ActiveSubgroup?.Members ?? [])
                    {
                        var id = new GameplayBuildingId(member.EntityId);
                        if (!_simulation.Construction.IsAlive(id) ||
                            _simulation.Construction.Observe(id).State ==
                                BuildingLifecycleState.Completed) continue;
                        if (_simulation.CancelConstruction(PlayerTeam, id))
                            _selectedBuildings.Remove(id.Value);
                    }
                }
                break;
        }
        UpdateCommandCard();
        QueueRedraw();
    }

    private void ExecuteProductionBatch(ProductionRecipeProfile recipe)
    {
        if (_simulation is null) return;
        var buildingIds = ActiveBuildingIds();
        var group = BuildProductionGroupSnapshot(buildingIds);
        var availability = group.Producers.Select(producer =>
        {
            var value = _simulation.Production.ObserveAvailability(
                PlayerTeam, new GameplayBuildingId(producer.BuildingId), recipe,
                _simulation.Construction, _simulation.Economy.Players);
            return new ProductionBatchAvailability(
                producer.BuildingId, value.Available, value.Code.ToString());
        });
        var plan = ProductionBatchPlanner.PlanTrain(group, availability);
        foreach (var building in plan.ProducerIds)
        {
            _simulation.IssueProduction(
                PlayerTeam, new GameplayBuildingId(building), recipe);
        }
    }

    private void CancelNewestProductionBatch(int recipeId)
    {
        if (_simulation is null) return;
        var group = BuildProductionGroupSnapshot(ActiveBuildingIds());
        foreach (var order in group.NewestMatchingOrders(recipeId))
            _simulation.CancelProduction(PlayerTeam, new ProductionOrderId(order));
    }

    private int[] ActiveBuildingIds() =>
        _selectionSnapshot.ActiveSubgroup?.Members
            .Where(value => value.Kind == GameplaySelectionKind.Building)
            .Select(value => value.EntityId)
            .Order()
            .ToArray() ?? [];

    private void BeginTargetCommand(
        TargetCommandKind kind,
        string label,
        int dataId = -1)
    {
        var active = _selectionSnapshot.ActiveSubgroup?.Members ?? [];
        _targetCommand = TargetCommandRequest.Create(
            kind,
            active.Where(value => value.Kind is GameplaySelectionKind.Worker or
                    GameplaySelectionKind.CombatUnit)
                .Select(value => value.EntityId),
            active.Where(value => value.Kind == GameplaySelectionKind.Building)
                .Select(value => value.EntityId),
            label,
            dataId);
        _buildPreviewRequest = null;
        _buildTargetPreview = null;
    }

    private void ResolveTargetCommand(InputEventMouseButton mouse)
    {
        if (_simulation is null || _targetCommand is null) return;
        var request = _targetCommand;
        var resolution = TargetCommandResolver.Resolve(
            request,
            mouse.ButtonIndex == MouseButton.Left
                ? TargetCommandPointerButton.Primary
                : TargetCommandPointerButton.Secondary,
            GodotPathProvider.ToNumerics(ScreenToWorld(mouse.Position)),
            mouse.ShiftPressed);
        if (resolution.Kind == TargetCommandResolutionKind.Cancel)
        {
            _targetCommand = null;
            QueueRedraw();
            return;
        }

        var succeeded = resolution.Command switch
        {
            TargetCommandKind.Move => _simulation.IssuePlayerMove(
                PlayerTeam, request.UnitIds, resolution.Position,
                resolution.Queued).Succeeded,
            TargetCommandKind.AttackMove => _simulation.IssuePlayerAttackMove(
                PlayerTeam, request.UnitIds, resolution.Position,
                resolution.Queued).Succeeded,
            TargetCommandKind.Rally => SetTargetCommandRally(
                request.BuildingIds, resolution.Position),
            TargetCommandKind.Build => IssueTargetConstruction(
                request, resolution.Position),
            _ => false
        };
        if (!succeeded) return;

        _commandMarker = GodotPathProvider.ToGodot(resolution.Position);
        _commandMarkerAttackMove = resolution.Command == TargetCommandKind.AttackMove;
        _commandMarkerQueued = resolution.Queued;
        _commandMarkerTime = 0.65f;
        if (!resolution.KeepTargeting) _targetCommand = null;
        UpdateCommandCard();
        QueueRedraw();
    }

    private void UpdateBuildTargetPreview(NVector2 pointer)
    {
        if (_targetCommand?.Kind != TargetCommandKind.Build)
        {
            _buildTargetPreview = null;
            _buildPreviewRequest = null;
            return;
        }
        var snapped = BuildTargetSnapper.Snap(pointer);
        var tick = _simulation?.Metrics.Tick ?? 0;
        if (ReferenceEquals(_buildPreviewRequest, _targetCommand) &&
            _buildPreviewPointer == GodotPathProvider.ToGodot(snapped) &&
            tick - _buildPreviewTick < 6)
            return;
        _buildPreviewRequest = _targetCommand;
        _buildPreviewPointer = GodotPathProvider.ToGodot(snapped);
        _buildPreviewTick = tick;
        _buildTargetPreview = ComputeBuildTargetPreview(_targetCommand, snapped);
    }

    private BuildTargetPreviewSnapshot ComputeBuildTargetPreview(
        TargetCommandRequest request,
        NVector2 pointer)
    {
        if (_simulation is null || _buildingTypes is null ||
            (uint)request.DataId >= (uint)_buildingTypes.Types.Length)
            return BuildTargetPreviewSnapshot.Empty;
        var profile = _buildingTypes.Type(request.DataId);
        var center = BuildTargetSnapper.Snap(pointer);
        var resourceNode = new EconomyResourceNodeId(-1);
        if (profile.RequiresVespeneNode && _economyOverview is not null)
        {
            var node = _economyOverview.ResourceNodes
                .Where(value => value.Kind == EconomyResourceKind.VespeneGas &&
                                value.Operational &&
                                NVector2.DistanceSquared(value.Position, pointer) <=
                                32f * 32f)
                .OrderBy(value => NVector2.DistanceSquared(value.Position, pointer))
                .ThenBy(value => value.Id.Value)
                .FirstOrDefault();
            if (node.Kind == EconomyResourceKind.VespeneGas)
            {
                resourceNode = node.Id;
                center = node.Position;
            }
        }

        var candidates = request.UnitIds
            .Where(value => (uint)value < (uint)_simulation.Units.Count)
            .OrderBy(value => NVector2.DistanceSquared(
                _simulation.Units.Positions[value], center))
            .ThenBy(value => value)
            .ToArray();
        ConstructionCommandResult? first = null;
        var builder = candidates.Length > 0 ? candidates[0] : -1;
        foreach (var unit in candidates)
        {
            var preview = _simulation.PreviewConstruction(
                PlayerTeam, unit, profile, center, resourceNode);
            first ??= preview;
            if (!preview.Succeeded) continue;
            builder = unit;
            first = preview;
            break;
        }
        var result = first ?? new ConstructionCommandResult(
            ConstructionCommandCode.InvalidBuilder, default);
        var halfSize = profile.Size * 0.5f;
        var status = result.Code == ConstructionCommandCode.PlacementRejected
            ? result.PlacementCode.ToString()
            : result.Code.ToString();
        return new BuildTargetPreviewSnapshot(
            new SimRect(center - halfSize, center + halfSize),
            builder,
            resourceNode.Value,
            result.Succeeded,
            status);
    }

    private bool IssueTargetConstruction(
        TargetCommandRequest request,
        NVector2 pointer)
    {
        if (_simulation is null || _buildingTypes is null) return false;
        var preview = ComputeBuildTargetPreview(request, pointer);
        _buildTargetPreview = preview;
        if (!preview.CanPlace || preview.BuilderUnit < 0) return false;
        var center = (preview.Bounds.Min + preview.Bounds.Max) * 0.5f;
        var result = _simulation.IssueConstruction(
            PlayerTeam,
            preview.BuilderUnit,
            _buildingTypes.Type(request.DataId),
            center,
            new EconomyResourceNodeId(preview.ResourceNode));
        return result.Succeeded;
    }

    private bool SetTargetCommandRally(
        IReadOnlyList<int> buildings,
        NVector2 position)
    {
        if (_simulation is null || buildings.Count == 0) return false;
        var rally = ResolveProductionRallyTarget(position);
        foreach (var value in buildings)
        {
            var id = new GameplayBuildingId(value);
            if (!_simulation.Construction.IsAlive(id) ||
                _simulation.Construction.Observe(id).PlayerId != PlayerTeam)
                return false;
        }
        var succeeded = 0;
        foreach (var value in buildings)
        {
            if (_simulation.SetProductionRallyTarget(
                    PlayerTeam, new GameplayBuildingId(value), rally))
                succeeded++;
        }
        return succeeded == buildings.Count;
    }

    private void UpdateMinimap()
    {
        if (_minimap is null || _world is null || _simulation is null)
        {
            return;
        }
        var markers = new List<MinimapMarker>(
            _simulation.Units.Count + _world.DynamicOccupancy.Count);
        if (_playerView is not null)
        {
            foreach (var unit in _playerView.Units)
            {
                markers.Add(new MinimapMarker(
                    unit.UnitId,
                    MinimapMarkerKind.Unit,
                    unit.OwnerPlayerId,
                    unit.Position,
                    new NVector2(unit.Radius * 2f),
                    _selectedUnits.Contains(unit.UnitId)));
            }
            foreach (var building in _playerView.Buildings)
            {
                markers.Add(new MinimapMarker(
                    building.BuildingId.Value,
                    MinimapMarkerKind.Building,
                    building.OwnerPlayerId,
                    (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                    building.Bounds.Max - building.Bounds.Min));
            }
            _minimap.SetSnapshot(new MinimapSnapshot(
                _world.Bounds,
                _cameraController?.VisibleWorld ?? _world.Bounds,
                _world.Obstacles.ToArray(),
                markers.ToArray()));
            return;
        }
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit])
            {
                continue;
            }
            markers.Add(new MinimapMarker(
                unit,
                MinimapMarkerKind.Unit,
                _simulation.Combat.Teams[unit],
                _simulation.Units.Positions[unit],
                new NVector2(_simulation.Units.Radii[unit] * 2f),
                _selectedUnits.Contains(unit)));
        }
        foreach (var footprint in _world.DynamicOccupancy.Snapshot())
        {
            markers.Add(new MinimapMarker(
                footprint.Id.Value,
                MinimapMarkerKind.Building,
                Team: 0,
                (footprint.Bounds.Min + footprint.Bounds.Max) * 0.5f,
                footprint.Bounds.Max - footprint.Bounds.Min));
        }
        _minimap.SetSnapshot(new MinimapSnapshot(
            _world.Bounds,
            _cameraController?.VisibleWorld ?? _world.Bounds,
            _world.Obstacles.ToArray(),
            markers.ToArray()));
    }

    private void FocusCameraAt(NVector2 worldPosition)
    {
        if (_cameraController is null)
        {
            return;
        }
        _cameraController.Focus([worldPosition]);
        ApplyCameraState();
    }

    private void IssueMinimapCommand(NVector2 worldPosition)
    {
        if (_simulation is null || !_navigationReady)
        {
            return;
        }
        var selected = SelectedLivingUnits();
        if (selected.Length == 0)
        {
            return;
        }
        _commandMarkerAttackMove = Input.IsKeyPressed(Key.A);
        _commandMarkerQueued = Input.IsKeyPressed(Key.Shift);
        var target = ResolveSmartCommandTarget(selected[0], worldPosition);
        var issued = _simulation.IssuePlayerSmartCommand(
            PlayerTeam,
            selected,
            target,
            _commandMarkerAttackMove,
            queued: _commandMarkerQueued);
        if (!issued.Succeeded)
            return;
        _commandMarkerAttackMove |= target.Kind is
            SmartCommandTargetKind.EnemyUnit or
            SmartCommandTargetKind.EnemyBuilding;
        _commandMarker = GodotPathProvider.ToGodot(worldPosition);
        _commandMarkerTime = 0.65f;
        QueueRedraw();
    }

    private string ActiveNavigationLabel()
    {
        if (_visualTest is not null)
        {
            return _visualTest.Rig.NavigationDataHash == 0UL
                ? "procedural"
                : $"f{_visualTest.Rig.NavigationFormatVersion} " +
                  _visualTest.Rig.NavigationDataHash.ToString("X16");
        }

        return _navigationSnapshot is null
            ? "none"
            : $"f{_navigationSnapshot.FormatVersion} {_navigationSnapshot.StableHashText}";
    }

    private void RememberDefaultRuntime()
    {
        _defaultWorld = _world;
        _defaultPathProvider = _pathProvider;
        _defaultSimulationPathProvider = _simulationPathProvider;
        _defaultSimulation = _simulation;
        _defaultRoutePlanner = _routePlanner;
        _defaultChokeController = _chokeController;
    }

    private void CreateLaunchScreen()
    {
        _testShowcaseEntries = TestShowcaseCatalog.Build(
            VisualTestCatalog.CaseIds);
        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        _launchScreen = new RtsLaunchScreen();
        _launchScreen.DemoRequested += EnterDefaultDemo;
        _launchScreen.TestRequested += StartInteractiveVisualTest;
        _launchScreen.TestBrowserRequested += () =>
            ReturnToTestBrowser("已停止当前测试，可选择其他场景。 ");
        layer.AddChild(_launchScreen);
        _launchScreen.Initialize(_testShowcaseEntries);
        SetFrontEndBlocking(true);
    }

    private void EnterDefaultDemo()
    {
        if (_defaultWorld is null || _defaultSimulation is null)
            return;
        _world = _defaultWorld;
        _pathProvider = _defaultPathProvider;
        _simulationPathProvider = _defaultSimulationPathProvider;
        _simulation = _defaultSimulation;
        _routePlanner = _defaultRoutePlanner;
        _chokeController = _defaultChokeController;
        _visualTest = null;
        _interactiveVisualTest = false;
        _visualTestFinishFrames = -1;
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        _activeSelectionSubgroup = null;
        _combatPresentation.Reset();
        _combatProjectileLayer?.SetFrame(CombatPresentationFrame.Empty);
        InitializeOperationState();
        InitializeCamera();
        InitializeResourceFileWatcher(EnableResourceFileWatcher);
        CreateHud();
        _navigationReady = _defaultPathProvider?.IsReady ?? true;
        _launchScreen?.HideScreen();
        SetFrontEndBlocking(false);
        UpdateHud();
        SyncWorldPresentationLayers();
        QueueRedraw();
    }

    private void StartInteractiveVisualTest(string caseId)
    {
        var entry = _testShowcaseEntries.FirstOrDefault(value =>
            string.Equals(value.Id, caseId, StringComparison.Ordinal));
        if (entry is null)
            return;
        if (caseId == "resource-hot-reload" &&
            !TryPrepareHotReloadCandidate())
        {
            ReturnToTestBrowser("测试资源准备失败，请查看控制台错误。 ");
            return;
        }
        _interactiveVisualTest = true;
        _interactiveTestStatus = "";
        _visualTestFinishFrames = -1;
        _launchScreen?.ShowRunning(entry);
        SetFrontEndBlocking(false);
        StartVisualTest(caseId);
    }

    private void ReturnToTestBrowser(string status)
    {
        _navigationReady = false;
        _visualTestFinishFrames = -1;
        _interactiveVisualTest = false;
        _resourceFileWatcher?.Stop();
        _resourceFileWatcher = null;
        DestroyHud();
        _targetCommand = null;
        _buildTargetPreview = null;
        _combatPresentation.Reset();
        _combatProjectileLayer?.SetFrame(CombatPresentationFrame.Empty);
        _launchScreen?.ShowBrowser(status);
        SetFrontEndBlocking(true);
        QueueRedraw();
    }

    private void SetFrontEndBlocking(bool blocking)
    {
        _frontEndBlocking = blocking;
        if (_hudRoot is not null)
            _hudRoot.Visible = !blocking;
        if (_targetCommandOverlay is not null)
            _targetCommandOverlay.Visible = !blocking;
    }

    private void ShowTestBrowserPreview()
    {
        var entries = TestShowcaseCatalog.Build(VisualTestCatalog.CaseIds);
        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        var preview = new RtsLaunchScreen();
        layer.AddChild(preview);
        preview.Initialize(entries);
        preview.ShowBrowser("自动录像：测试目录、分类、搜索与中文说明。 ");
    }

    private void StartVisualTest(string caseId)
    {
        try
        {
        _visualTest = VisualTestCatalog.Create(
            caseId,
            _navigationSnapshot,
            _gameplayProfiles,
            _clearanceBake,
            _hotReloadCandidate,
            _buildingTypes,
            _productionCatalog,
            _technologyCatalog,
            _aiConfigurations);
        }
        catch (ArgumentException exception)
        {
            GD.PushError(exception.Message);
            if (_interactiveVisualTest)
                ReturnToTestBrowser($"无法启动测试：{exception.Message}");
            else
                GetTree().Quit(2);
            return;
        }

        _world = _visualTest.World;
        _simulation = _visualTest.Simulation;
        _combatPresentation.Reset();
        _combatProjectileLayer?.SetFrame(CombatPresentationFrame.Empty);
        _routePlanner = _visualTest.RoutePlanner;
        _chokeController = _visualTest.ChokeController;
        if (caseId == "resource-file-watch-workflow")
        {
            InitializeResourceFileWatcher(
                enableFileSystemWatchers: false,
                clearanceBakePath: HotReloadTestResourceGenerator.BakeOnlyPath);
            _resourceFileWatcher?.NotifyChanged(
                RuntimeResourceChangeKind.ClearanceBake);
            _resourceFileWatcher?.NotifyChanged(
                RuntimeResourceChangeKind.ClearanceBake);
        }
        if (caseId == "building-connectivity-diff-preview")
        {
            var navigation =
                BuildingConnectivityDiffSelfTest.CreateNavigationFixture();
            _buildingConnectivityDiff =
                BuildingConnectivityDiffSnapshot.Create(
                    navigation,
                    BuildingConnectivityDiffSelfTest.BlockingFootprint,
                    ClearanceBakeSnapshot.Build(navigation));
        }
        GetNodeOrNull<ClearancePreview2D>("ClearancePreview")?.SetRuntimeSnapshots(
            _navigationSnapshot,
            _gameplayProfiles,
            caseId is "clearance-editor-preview" or
                "clearance-bake-resource-runtime" or
                "clearance-incremental-chunks",
            _clearanceBake,
            caseId == "clearance-incremental-chunks"
                ? ClearanceIncrementalSelfTest.ChangedArea
                : null);
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        foreach (var unit in _visualTest.RenderUnitIndices)
        {
            _selectedUnits.Add(unit);
        }
        foreach (var building in _visualTest.SelectedBuildings)
            _selectedBuildings.Add(building.Value);
        _pointerScreen = _visualTest.TargetCommandPreviewPointer.HasValue
            ? GodotPathProvider.ToGodot(
                _visualTest.TargetCommandPreviewPointer.Value)
            : GetViewportRect().Size * 0.5f;
        _targetCommand = _visualTest.TargetCommandPreview;
        if ((caseId is "operation-mixed-command-card" or
                "operation-production-group-batch") &&
            _selectedBuildings.Count > 0)
        {
            var selected = _simulation.Construction.Observe(
                new GameplayBuildingId(_selectedBuildings.Min()));
            _activeSelectionSubgroup = new SelectionSubgroupKey(
                GameplaySelectionKind.Building, selected.Type.Id);
        }

        _navigationReady = true;
        if (caseId == "minimap-interaction")
        {
            InitializeCamera();
            _cameraController?.ZoomAt(
                GodotPathProvider.ToNumerics(GetViewportRect().Size * 0.5f), 2);
            ApplyCameraState();
        }
        CreateHud();
        if (caseId == "frontend-test-browser" && !_interactiveVisualTest)
            ShowTestBrowserPreview();
        UpdateHud();
        SyncWorldPresentationLayers();
        QueueRedraw();
        GD.Print(
            $"RTS_VISUAL_TEST_START {_visualTest.Id}: " +
            $"ticks={_visualTest.DurationTicks}, units={_visualTest.VisibleUnits.Length}");
    }

    private void UpdateCombatPresentation(float deltaSeconds)
    {
        if (_simulation is null || _combatProjectileLayer is null)
            return;
        var events = _simulation.CombatEvents.ReadAfter(
            _combatPresentation.LatestEventSequence);
        var frame = _combatPresentation.Update(
            _simulation.CombatProjectiles.ObserveActive(),
            events,
            deltaSeconds);
        _combatProjectileLayer.SetFrame(frame);
    }

    private RtsWorldCanvasTransform CurrentWorldCanvasTransform()
    {
        if (_visualTest?.HasCameraTrack != true || _simulation is null)
            return RtsWorldCanvasTransform.Identity;
        var viewport = GetViewportRect().Size;
        var camera = _visualTest.CameraAt(_simulation.Metrics.Tick);
        var viewportCenter = new Vector2(
            viewport.X * 0.5f,
            82f + (viewport.Y - 82f) * 0.5f);
        return new RtsWorldCanvasTransform(
            viewportCenter -
            GodotPathProvider.ToGodot(camera.Center) * camera.Zoom,
            camera.Zoom);
    }

    private void SyncWorldPresentationLayers()
    {
        var transform = CurrentWorldCanvasTransform();
        _combatProjectileLayer?.SetWorldCanvasTransform(transform);
        _targetCommandOverlay?.SetWorldCanvasTransform(transform);
        GetNodeOrNull<ClearancePreview2D>("ClearancePreview")?
            .SetWorldCanvasTransform(transform);
    }

    private bool TryPrepareHotReloadCandidate()
    {
        if (_navigationSnapshot is null || _gameplayProfiles is null ||
            _clearanceBake is null)
        {
            GD.PushError("RTS_RESOURCE_RELOAD FAIL baseline resources missing");
            return false;
        }
        if (!RuntimeResourceSetSnapshot.TryCreate(
                _navigationSnapshot,
                _gameplayProfiles,
                _clearanceBake,
                out var current,
                out var currentValidation) ||
            current is null)
        {
            GD.PushError(
                $"RTS_RESOURCE_RELOAD FAIL baseline={currentValidation.Message}");
            return false;
        }
        if (!RuntimeResourceSetLoader.TryLoadFresh(
                HotReloadTestResourceGenerator.NavigationPath,
                HotReloadTestResourceGenerator.ProfilesPath,
                HotReloadTestResourceGenerator.BakePath,
                out _hotReloadCandidate,
                out var loadResult) ||
            _hotReloadCandidate is null)
        {
            GD.PushError(
                $"RTS_RESOURCE_RELOAD FAIL code={loadResult.Code} " +
                $"message={loadResult.Message}");
            return false;
        }

        _hotReloadPlan = RuntimeResourceReloadPlan.Create(
            current, _hotReloadCandidate);
        var mismatchRejected = !RuntimeResourceSetLoader.TryLoadFresh(
            HotReloadTestResourceGenerator.NavigationPath,
            HotReloadTestResourceGenerator.ProfilesPath,
            DemoClearanceBakeResourcePath,
            out _,
            out var mismatchResult) &&
            mismatchResult.Code ==
                RuntimeResourceLoadErrorCode.ClearanceBakeLoadFailed;
        if (!mismatchRejected)
        {
            GD.PushError(
                $"RTS_RESOURCE_RELOAD FAIL mismatched set accepted " +
                $"code={mismatchResult.Code}");
            return false;
        }
        GD.Print(
            $"RTS_RESOURCE_RELOAD PASS impact={_hotReloadPlan.Impact} " +
            $"navigation={_hotReloadPlan.Navigation.ChangedObstacles}/" +
            $"{_hotReloadPlan.Navigation.ChangedPortals}/" +
            $"{_hotReloadPlan.Navigation.ChangedEdges} " +
            $"profiles={_hotReloadPlan.GameplayProfiles.ChangedUnitProfiles}/" +
            $"{_hotReloadPlan.GameplayProfiles.ChangedBuildingProfiles} " +
            $"bake={_hotReloadPlan.ClearanceBake.Changed} " +
            $"mismatchRejected={mismatchRejected}");
        return true;
    }

    private static string? ReadArgument(string[] arguments, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index].StartsWith(prefix, StringComparison.Ordinal))
            {
                return arguments[index][prefix.Length..];
            }

            if (arguments[index] == name && index + 1 < arguments.Length)
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private bool TryLoadNavigationSnapshot()
    {
        if (_navigationSnapshot is not null)
        {
            return true;
        }

        var resourcePath = NavigationMapAsset?.ResourcePath ?? DemoNavigationResourcePath;
        var converted = NavigationMapAsset is not null
            ? NavigationMapResourceConverter.TryConvert(
                NavigationMapAsset,
                out var snapshot,
                out var validation)
            : NavigationMapResourceConverter.TryLoadSnapshot(
                DemoNavigationResourcePath,
                out snapshot,
                out validation);
        if (converted && snapshot is not null)
        {
            _navigationSnapshot = snapshot;
            GD.Print(
                $"RTS_NAVIGATION_RESOURCE PASS path={resourcePath} " +
                $"format={snapshot.FormatVersion} hash={snapshot.StableHashText}");
            return true;
        }

        foreach (var issue in validation.Issues)
        {
            GD.PushError(
                $"RTS_NAVIGATION_RESOURCE FAIL code={issue.Code} " +
                $"index={issue.ElementIndex} message={issue.Message}");
        }

        return false;
    }

    private bool TryLoadRuntimeData() =>
        TryLoadNavigationSnapshot() &&
        TryLoadGameplayProfiles() &&
        TryLoadBuildingTypes() &&
        TryLoadProductionCatalog() &&
        TryLoadTechnologyCatalog() &&
        TryLoadAiConfigurations() &&
        TryLoadClearanceBake();

    private bool TryLoadAiConfigurations()
    {
        if (_aiConfigurations is not null) return true;
        var resourcePath = AiConfigurationAsset?.ResourcePath ??
                           DemoAiConfigurationResourcePath;
        var converted = AiConfigurationAsset is not null
            ? AiConfigurationResourceConverter.TryConvert(
                AiConfigurationAsset, out var snapshot, out var validation)
            : AiConfigurationResourceConverter.TryLoadSnapshot(
                DemoAiConfigurationResourcePath, out snapshot, out validation);
        if (converted && snapshot is not null)
        {
            _aiConfigurations = snapshot;
            GD.Print(
                $"RTS_AI_CONFIG PASS path={resourcePath} " +
                $"format={snapshot.FormatVersion} hash={snapshot.StableHashText} " +
                $"profiles={snapshot.Profiles.Length}");
            return true;
        }
        GD.PushError(
            $"RTS_AI_CONFIG FAIL code={validation.Code} " +
            $"index={validation.Index} message={validation.Message}");
        return false;
    }

    private bool TryLoadProductionCatalog()
    {
        if (_productionCatalog is not null) return true;
        if (_buildingTypes is null && !TryLoadBuildingTypes()) return false;
        var resourcePath = ProductionCatalogAsset?.ResourcePath ??
                           DemoProductionCatalogResourcePath;
        var converted = ProductionCatalogAsset is not null
            ? ProductionCatalogResourceConverter.TryConvert(
                ProductionCatalogAsset, out var snapshot, out var validation)
            : ProductionCatalogResourceConverter.TryLoadSnapshot(
                DemoProductionCatalogResourcePath, out snapshot, out validation);
        if (converted && snapshot is not null &&
            ProductionRequirementCatalogValidator.TryValidate(
                snapshot, _buildingTypes!, out validation))
        {
            _productionCatalog = snapshot;
            GD.Print(
                $"RTS_PRODUCTION_CATALOG PASS path={resourcePath} " +
                $"format={snapshot.FormatVersion} hash={snapshot.StableHashText} " +
                $"units={snapshot.UnitTypes.Length} recipes={snapshot.Recipes.Length}");
            return true;
        }
        GD.PushError(
            $"RTS_PRODUCTION_CATALOG FAIL code={validation.Code} " +
            $"index={validation.Index} message={validation.Message}");
        return false;
    }

    private bool TryLoadBuildingTypes()
    {
        if (_buildingTypes is not null)
        {
            return true;
        }

        var resourcePath = BuildingTypesAsset?.ResourcePath ??
                           DemoBuildingTypesResourcePath;
        var converted = BuildingTypesAsset is not null
            ? BuildingTypeResourceConverter.TryConvert(
                BuildingTypesAsset, out var snapshot, out var validation)
            : BuildingTypeResourceConverter.TryLoadSnapshot(
                DemoBuildingTypesResourcePath, out snapshot, out validation);
        if (converted && snapshot is not null)
        {
            _buildingTypes = snapshot;
            GD.Print(
                $"RTS_BUILDING_TYPES PASS path={resourcePath} " +
                $"format={snapshot.FormatVersion} hash={snapshot.StableHashText} " +
                $"types={snapshot.Types.Length}");
            return true;
        }

        foreach (var issue in validation.Issues)
        {
            GD.PushError(
                $"RTS_BUILDING_TYPES FAIL code={issue.Code} " +
                $"index={issue.ElementIndex} message={issue.Message}");
        }
        return false;
    }

    private bool TryLoadTechnologyCatalog()
    {
        if (_technologyCatalog is not null) return true;
        if (_buildingTypes is null && !TryLoadBuildingTypes()) return false;
        var resourcePath = TechnologyCatalogAsset?.ResourcePath ??
                           DemoTechnologyCatalogResourcePath;
        var converted = TechnologyCatalogAsset is not null
            ? TechnologyCatalogResourceConverter.TryConvert(
                TechnologyCatalogAsset, _buildingTypes!,
                out var snapshot, out var validation)
            : TechnologyCatalogResourceConverter.TryLoadSnapshot(
                DemoTechnologyCatalogResourcePath, _buildingTypes!,
                out snapshot, out validation);
        if (converted && snapshot is not null)
        {
            _technologyCatalog = snapshot;
            GD.Print(
                $"RTS_TECHNOLOGY_CATALOG PASS path={resourcePath} " +
                $"format={snapshot.FormatVersion} hash={snapshot.StableHashText} " +
                $"technologies={snapshot.Technologies.Length}");
            return true;
        }
        GD.PushError(
            $"RTS_TECHNOLOGY_CATALOG FAIL code={validation.Code} " +
            $"index={validation.Index} message={validation.Message}");
        return false;
    }

    private bool TryLoadGameplayProfiles()
    {
        if (_gameplayProfiles is not null)
        {
            return true;
        }

        var resourcePath = GameplayProfilesAsset?.ResourcePath ??
                           DemoGameplayProfilesResourcePath;
        var converted = GameplayProfilesAsset is not null
            ? GameplayProfileResourceConverter.TryConvert(
                GameplayProfilesAsset,
                out var snapshot,
                out var validation)
            : GameplayProfileResourceConverter.TryLoadSnapshot(
                DemoGameplayProfilesResourcePath,
                out snapshot,
                out validation);
        if (converted && snapshot is not null)
        {
            _gameplayProfiles = snapshot;
            GD.Print(
                $"RTS_GAMEPLAY_PROFILES PASS path={resourcePath} " +
                $"format={snapshot.FormatVersion} hash={snapshot.StableHashText} " +
                $"units={snapshot.UnitProfiles.Length} " +
                $"buildings={snapshot.BuildingProfiles.Length}");
            return true;
        }

        foreach (var issue in validation.Issues)
        {
            GD.PushError(
                $"RTS_GAMEPLAY_PROFILES FAIL code={issue.Code} " +
                $"index={issue.ElementIndex} message={issue.Message}");
        }

        return false;
    }

    private bool TryLoadClearanceBake()
    {
        if (_clearanceBake is not null)
        {
            return true;
        }

        if (_navigationSnapshot is null)
        {
            GD.PushError("Navigation snapshot must load before clearance bake.");
            return false;
        }

        var resourcePath = ClearanceBakeAsset?.ResourcePath ??
                           DemoClearanceBakeResourcePath;
        var converted = ClearanceBakeAsset is not null
            ? ClearanceBakeResourceConverter.TryConvert(
                ClearanceBakeAsset,
                _navigationSnapshot.StableHash,
                out var snapshot,
                out var validation)
            : ClearanceBakeResourceConverter.TryLoadSnapshot(
                DemoClearanceBakeResourcePath,
                _navigationSnapshot.StableHash,
                out snapshot,
                out validation);
        if (converted && snapshot is not null)
        {
            _clearanceBake = snapshot;
            GD.Print(
                $"RTS_CLEARANCE_BAKE PASS path={resourcePath} " +
                $"format={snapshot.FormatVersion} hash={snapshot.StableHashText} " +
                $"source={snapshot.SourceNavigationHashText} " +
                $"grid={snapshot.Columns}x{snapshot.Rows} " +
                $"chunks={snapshot.ChunkColumns}x{snapshot.ChunkRows}");
            return true;
        }

        foreach (var issue in validation.Issues)
        {
            GD.PushError(
                $"RTS_CLEARANCE_BAKE FAIL code={issue.Code} " +
                $"layer={issue.LayerIndex} message={issue.Message}");
        }

        return false;
    }

    private void GenerateDemoNavigationResource()
    {
        var snapshot = DemoMapDefinition.CreateSnapshot();
        var resource = NavigationMapResourceConverter.FromSnapshot(snapshot);
        if (!EnsureDataDirectory())
        {
            GetTree().Quit(1);
            return;
        }

        var saveError = ResourceSaver.Save(resource, DemoNavigationResourcePath);
        GD.Print(
            $"RTS_NAVIGATION_RESOURCE_GENERATE error={saveError} " +
            $"path={DemoNavigationResourcePath} hash={snapshot.StableHashText}");
        GetTree().Quit(saveError == Error.Ok ? 0 : 1);
    }

    private void GenerateDemoBuildingTypes()
    {
        var snapshot = DemoBuildingTypes.CreateCatalog();
        var resource = BuildingTypeResourceConverter.FromSnapshot(snapshot);
        if (!EnsureDataDirectory())
        {
            GetTree().Quit(1);
            return;
        }
        var saveError = ResourceSaver.Save(
            resource, DemoBuildingTypesResourcePath);
        GD.Print(
            $"RTS_BUILDING_TYPES_GENERATE error={saveError} " +
            $"path={DemoBuildingTypesResourcePath} hash={snapshot.StableHashText}");
        GetTree().Quit(saveError == Error.Ok ? 0 : 1);
    }

    private void GenerateDemoProductionCatalog()
    {
        var snapshot = DemoProductionCatalog.CreateSnapshot();
        var resource = ProductionCatalogResourceConverter.FromSnapshot(snapshot);
        if (!EnsureDataDirectory())
        {
            GetTree().Quit(1);
            return;
        }
        var error = ResourceSaver.Save(
            resource, DemoProductionCatalogResourcePath);
        GD.Print(
            $"RTS_PRODUCTION_CATALOG_GENERATE error={error} " +
            $"path={DemoProductionCatalogResourcePath} " +
            $"hash={snapshot.StableHashText}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private void GenerateDemoTechnologyCatalog()
    {
        var snapshot = DemoTechnologies.CreateCatalog();
        var resource = TechnologyCatalogResourceConverter.FromSnapshot(snapshot);
        if (!EnsureDataDirectory())
        {
            GetTree().Quit(1);
            return;
        }
        var error = ResourceSaver.Save(
            resource, DemoTechnologyCatalogResourcePath);
        GD.Print(
            $"RTS_TECHNOLOGY_CATALOG_GENERATE error={error} " +
            $"path={DemoTechnologyCatalogResourcePath} " +
            $"hash={snapshot.StableHashText}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private void GenerateDemoAiConfigurations()
    {
        var snapshot = DemoAiConfigurations.CreateCatalog();
        var resource = AiConfigurationResourceConverter.FromSnapshot(snapshot);
        if (!EnsureDataDirectory())
        {
            GetTree().Quit(1);
            return;
        }
        var error = ResourceSaver.Save(
            resource, DemoAiConfigurationResourcePath);
        GD.Print(
            $"RTS_AI_CONFIG_GENERATE error={error} " +
            $"path={DemoAiConfigurationResourcePath} " +
            $"hash={snapshot.StableHashText}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private void GenerateDemoClearanceBake()
    {
        if (_navigationSnapshot is null)
        {
            GD.PushError("Navigation snapshot must load before clearance bake generation.");
            GetTree().Quit(1);
            return;
        }

        var snapshot = ClearanceBakeSnapshot.Build(_navigationSnapshot);
        var resource = ClearanceBakeResourceConverter.FromSnapshot(snapshot);
        if (!EnsureDataDirectory())
        {
            GetTree().Quit(1);
            return;
        }

        var saveError = ResourceSaver.Save(
            resource, DemoClearanceBakeResourcePath);
        GD.Print(
            $"RTS_CLEARANCE_BAKE_GENERATE error={saveError} " +
            $"path={DemoClearanceBakeResourcePath} " +
            $"hash={snapshot.StableHashText} " +
            $"bytes={snapshot.CanonicalBytes.Length}");
        GetTree().Quit(saveError == Error.Ok ? 0 : 1);
    }

    private void GenerateHotReloadTestResources()
    {
        if (_navigationSnapshot is null || _gameplayProfiles is null)
        {
            GD.PushError("Runtime resources must load before generating fixtures.");
            GetTree().Quit(1);
            return;
        }

        var result = HotReloadTestResourceGenerator.Generate(
            _navigationSnapshot, _gameplayProfiles);
        GD.Print(
            $"RTS_HOT_RELOAD_FIXTURES {(result.Succeeded ? "PASS" : "FAIL")} " +
            $"navigation={result.NavigationHash} " +
            $"profiles={result.ProfilesHash} bake={result.BakeHash} " +
            $"bakeOnly={result.BakeOnlyHash} " +
            $"errors={result.NavigationError}/{result.ProfilesError}/" +
            $"{result.BakeError}/{result.BakeOnlyError}");
        GetTree().Quit(result.Succeeded ? 0 : 1);
    }

    private void InitializeResourceFileWatcher(
        bool enableFileSystemWatchers,
        string clearanceBakePath = DemoClearanceBakeResourcePath)
    {
        if (_navigationSnapshot is null || _gameplayProfiles is null ||
            _clearanceBake is null || _simulation is null ||
            !RuntimeResourceSetSnapshot.TryCreate(
                _navigationSnapshot,
                _gameplayProfiles,
                _clearanceBake,
                out var current,
                out _) ||
            current is null)
        {
            GD.PushError("RTS_RESOURCE_WATCH baseline resource set is invalid.");
            return;
        }

        _resourceFileWatcher?.QueueFree();
        _resourceFileWatcher = new RuntimeResourceFileWatcher();
        _resourceFileWatcher.StatusChanged += status =>
        {
            _resourceWatchStatus = status;
            _resourceWatchControl?.SetSnapshot(status);
        };
        _resourceFileWatcher.ResourceSetApplied += applied =>
        {
            _clearanceBake = applied.ClearanceBake;
        };
        AddChild(_resourceFileWatcher);
        _resourceFileWatcher.Start(
            DemoNavigationResourcePath,
            DemoGameplayProfilesResourcePath,
            clearanceBakePath,
            current,
            _simulation,
            enableFileSystemWatchers);
        _resourceWatchStatus = _resourceFileWatcher.Status;
    }

    private static bool EnsureDataDirectory()
    {
        var directory = ProjectSettings.GlobalizePath("res://data");
        var error = DirAccess.MakeDirRecursiveAbsolute(directory);
        if (error is Error.Ok or Error.AlreadyExists)
        {
            return true;
        }

        GD.PushError($"Unable to create navigation data directory: {error}");
        return false;
    }

    private void SpawnDemoUnits()
    {
        if (_simulation is null || _gameplayProfiles is null)
        {
            return;
        }

        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 12; column++)
            {
                var profile = _gameplayProfiles.Unit((row + column) %
                    _gameplayProfiles.UnitProfiles.Length);
                var unit = _simulation.AddUnit(
                    new NVector2(105f + column * 20f, 160f + row * 20f),
                    profile,
                    team: 1,
                    CombatProfileSnapshot.Standard);
                _selectionTypeIds[unit] = profile.Id;
                _selectedUnits.Add(unit);
            }
        }

        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                var profile = _gameplayProfiles.Unit((row + column) %
                    _gameplayProfiles.UnitProfiles.Length);
                var unit = _simulation.AddUnit(
                    new NVector2(1030f + column * 20f, 510f + row * 20f),
                    profile,
                    team: 2,
                    CombatProfileSnapshot.Standard);
                _selectionTypeIds[unit] = profile.Id;
            }
        }
    }

    private void ResetDemoUnits()
    {
        if (_world is null || _simulationPathProvider is null)
        {
            return;
        }

        _simulation = new RtsSimulation(
            _world,
            _simulationPathProvider,
            capacity: 256,
            groupRoutePlanner: _routePlanner,
            chokeController: _chokeController,
            clearanceBake: _clearanceBake);
        InitializeOperationState();
        _selectedUnits.Clear();
        SpawnDemoUnits();
        _commandMarkerTime = 0f;
    }

    private void PlaceDemoBuilding()
    {
        if (_world is null || _simulation is null || _gameplayProfiles is null ||
            _visualTest is not null)
        {
            return;
        }

        var profile = _gameplayProfiles.Building(
            _nextDemoBuildingClass % _gameplayProfiles.BuildingProfiles.Length);
        var size = profile.Size;
        var halfSize = size * 0.5f;
        var allowedCenters = _world.Bounds.Inset(MathF.Max(halfSize.X, halfSize.Y));
        var center = allowedCenters.Clamp(
            GodotPathProvider.ToNumerics(GetGlobalMousePosition()));
        var result = _simulation.TryPlaceBuilding(center, profile);
        if (!result.Succeeded)
        {
            GD.Print($"RTS_BUILDING_REJECT profile={profile.Name} " +
                     $"code={result.Code} conflict={result.ConflictId}");
            return;
        }

        _demoBuildings.Push(result.FootprintId);
        _nextDemoBuildingClass++;
        GD.Print($"RTS_BUILDING_PLACED profile={profile.Name} " +
                 $"size={size} id={result.FootprintId.Value}");
        QueueRedraw();
    }

    private void RemoveLastDemoBuilding()
    {
        if (_simulation is null || _demoBuildings.Count == 0 || _visualTest is not null)
        {
            return;
        }

        _simulation.RemoveBuilding(_demoBuildings.Pop());
        QueueRedraw();
    }

    private async Task CaptureDemoFrame()
    {
        if (_simulation is null)
        {
            return;
        }

        var selected = _selectedUnits.OrderBy(index => index).ToArray();
        _simulation.IssueMove(selected, new NVector2(1110f, 350f));
        _commandMarker = new Vector2(1110f, 350f);
        _commandMarkerTime = 10f;

        for (var frame = 0; frame < 180; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        _navigationReady = false;
        QueueRedraw();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var image = GetViewport().GetTexture().GetImage();
        var outputPath = ProjectSettings.GlobalizePath("res://rts_demo_capture.png");
        var error = image.SavePng(outputPath);
        GD.Print($"RTS_CAPTURE {(error == Error.Ok ? "SAVED" : error.ToString())}: {outputPath}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private static Rect2 MakeRect(Vector2 first, Vector2 second)
    {
        var minimum = new Vector2(MathF.Min(first.X, second.X), MathF.Min(first.Y, second.Y));
        var maximum = new Vector2(MathF.Max(first.X, second.X), MathF.Max(first.Y, second.Y));
        return new Rect2(minimum, maximum - minimum);
    }

    private static Rect2 ToRect2(SimRect rect) =>
        new(GodotPathProvider.ToGodot(rect.Min),
            GodotPathProvider.ToGodot(rect.Max - rect.Min));
}
