using Godot;
using RtsDemo.Demos;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;
using War3Rts.Data;
using War3Rts.Maps;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

/// <summary>
/// Formal Warcraft RTS composition root. Input emits simulation commands;
/// gameplay remains deterministic and presentation only consumes snapshots.
/// </summary>
public sealed partial class War3Rts : Node3D
{
    private const float MinimumDragPixels = 7f;
    private const float CursorEdgePixels = 10f;
    private RtsSimulation? _simulation;
    private War3HumanRuntime? _runtime;
    private TerrainMapSnapshot? _terrain;
    private BuildingTypeCatalogSnapshot? _buildings;
    private ProductionCatalogSnapshot? _production;
    private TechnologyCatalogSnapshot? _technologies;
    private BuildingUpgradeCatalogSnapshot? _buildingUpgrades;
    private War3WorldPresenter? _presenter;
    private Rts3DTerrainPresenter? _terrainPresenter;
    private Camera3D? _camera;
    private Rts3DCameraController? _cameraController;
    private War3RtsHud? _hud;
    private War3ItemShopRuntime? _itemShops;
    private War3ItemEffectRuntime? _itemEffects;
    private readonly Dictionary<int, War3InventoryAbilityProfile>
        _knownInventoryProfiles = [];
    private readonly Dictionary<string, War3UnitData?>
        _inventoryUnitDataByObjectId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, War3InventoryAbilityProfile[]>
        _inventoryCandidatesByObjectId = new(StringComparer.Ordinal);
    private readonly HashSet<int> _inventoryAliveUnits = [];
    private War3MapRuntime? _activeMap;
    private CanvasLayer? _mapSelectionLayer;
    private War3MapLoadingOverlay? _mapLoadingOverlay;
    private bool _mapLoadInProgress;
    private bool _loadingUiReady;
    private bool _loadingProgressMonotonic = true;
    private bool _loadingReachedCompletion;
    private double _lastLoadingProgress;
    private int _loadingStageUpdateCount;
    private readonly HashSet<int> _selectedUnits = [];
    private readonly HashSet<int> _selectedBuildings = [];
    private string _activeSelectionSubgroup = string.Empty;
    private Vector2 _dragStart;
    private bool _dragging;
    private bool _selectionAdditive;
    private bool _movePending;
    private bool _attackMovePending;
    private bool _rallyPending;
    private int _pendingAbilityId = -1;
    private int _pendingAbilityCaster = -1;
    private int _pendingAbilityBuilding = -1;
    private int _pendingItemUnit = -1;
    private int _pendingItemSlot = -1;
    private int _pendingBuilding = -1;
    private bool _buildMenu;
    private bool _learnMenu;
    private string _mode = "普通选择";
    private string _status = "人族基地已就绪";
    private double _elapsed;
    private double _hudAccumulator;
    private bool _smoke;
    private long _smokeEndTick;
    private bool _capture;
    private bool _terrainCapture;
    private bool _pcgCapture;
    private bool _heroCapture;
    private bool _offlineBakeRequested;
    private bool _rightClickInputSmoke;
    private int _earlyExitCode;
    private War3RuntimeProfiler? _runtimeProfiler;
    private War3StressTestMode? _stressTest;
    private War3AutomatedSkirmishStressMode? _automatedSkirmish;
    private bool _automatedSkirmishRequested;
    private War3NavigationDebugger? _navigationDebugger;
    private War3CursorController? _cursor;
    private int _smokeRallyBuilding = -1;
    private NVector2 _smokeRallyPosition;
    private int _smokeConstructionBuilding = -1;
    private int _smokeConstructionBuilder = -1;
    private int _smokeShopBuilding = -1;
    private long _smokeConstructionPauseTick = -1;
    private float _smokeConstructionPauseProgress;
    private bool _smokeConstructionPauseStable = true;
    private bool _smokeConstructionResumed;
    private bool _smokeConstructionAdvancedAfterResume;
    private bool _smokeSelectionGroupUiValid;
    private bool _smokeConstructionProgressUiValid;
    private bool _smokeBuildingPortraitValid;
    private bool _smokeUnitPortraitValid;
    private bool _smokeEnemySelectionValid;
    private bool _smokeShopValid;
    private bool _smokeItemUseValid;
    private bool _smokeAbilityCastsIssued;
    private bool _smokeAbilityDamageApplied;
    private int _smokeAbilityTarget = -1;
    private float _smokeAbilityTargetInitialHealth;
    private string _smokeExpectedSummonObjectId = string.Empty;
    private int _smokeSummonedUnit = -1;
    private int _previousMaximumPhysicsStepsPerFrame = -1;

    public override void _Ready()
    {
        // This scene historically only used _UnhandledInput. Enable the new
        // pre-GUI command input stage explicitly so imported/hot-reloaded C#
        // metadata cannot leave it disabled on an existing scene instance.
        SetProcessInput(true);
        var arguments = War3LaunchRequest.ConsumeArguments(
            OS.GetCmdlineUserArgs());
        if (arguments.Contains(War3LaunchRequest.InteractiveStressArgument))
        {
            _previousMaximumPhysicsStepsPerFrame =
                Engine.MaxPhysicsStepsPerFrame;
            Engine.MaxPhysicsStepsPerFrame = Math.Min(
                _previousMaximumPhysicsStepsPerFrame, 2);
            GD.Print(
                "WAR3_INTERACTIVE_STRESS_LAUNCH " +
                "units=800 map=320x160 long_march=true builders=48 slots=96 " +
                "animations=all effects=all rendered=true " +
                $"max_physics_steps={Engine.MaxPhysicsStepsPerFrame}");
        }
        _smoke = arguments.Contains("--war3-rts-smoke");
        _terrainCapture = arguments.Contains("--war3-rts-terrain-capture");
        _pcgCapture = arguments.Contains("--war3-rts-pcg-capture");
        _heroCapture = arguments.Contains("--war3-rts-hero-capture");
        _offlineBakeRequested = arguments.Contains("--war3-bake-map-cache");
        _rightClickInputSmoke = arguments.Contains(
            "--war3-right-click-input-smoke");
        _automatedSkirmishRequested =
            War3AutomatedSkirmishStressMode.IsRequested(arguments);
        var navigationAuditRequested =
            arguments.Contains(War3NavigationMapAudit.EnableArgument);
        if (navigationAuditRequested)
        {
            War3NavigationMapAudit.BeginProgress(
                War3MapCodec.DefaultMapId, arguments);
            War3NavigationMapAudit.TraceBootstrap("status=ready_entered");
        }
        _runtimeProfiler = War3RuntimeProfiler.TryCreate(arguments);
        _stressTest = War3StressTestMode.TryCreate(arguments);
        if (arguments.Contains("--war3-audio-smoke"))
        {
            RunAudioSmoke();
            return;
        }
        if (!navigationAuditRequested && !_offlineBakeRequested)
        {
            _cursor = new War3CursorController { Name = "War3Cursor" };
            AddChild(_cursor);
            _cursor.Initialize(War3CursorCatalog.ParseRace(arguments));
        }
        _capture = arguments.Contains("--war3-rts-capture") ||
                   _terrainCapture || _pcgCapture || _heroCapture;
        if (navigationAuditRequested)
            War3NavigationMapAudit.TraceBootstrap("status=catalog_begin");
        var catalog = War3MapCatalog.Enumerate();
        if (navigationAuditRequested)
            War3NavigationMapAudit.TraceBootstrap(
                $"status=catalog_complete count={catalog.Count}");
        var requestedId = arguments
            .FirstOrDefault(value => value.StartsWith(
                "--war3-map=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1];
        if (_smoke || _capture || _offlineBakeRequested ||
            _rightClickInputSmoke ||
            navigationAuditRequested || _stressTest is not null ||
            _automatedSkirmishRequested ||
            !string.IsNullOrWhiteSpace(requestedId))
        {
            var id = string.IsNullOrWhiteSpace(requestedId)
                ? War3MapCodec.DefaultMapId
                : requestedId;
            var entry = catalog.FirstOrDefault(value => value.Manifest.Id.Equals(
                id, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                GD.PushError(
                    $"WAR3_MAP_LOAD_FAIL id={id} error=Map id is not present in the catalog.");
                GetTree().Quit(1);
                return;
            }
            _ = LoadMapAsync(entry, arguments, selectionStatus: null);
            return;
        }
        ShowMapSelection(catalog, arguments);
        if (arguments.Contains("--war3-map-selection-capture"))
            _ = CaptureMapSelectionAsync();
    }

    private async Task<bool> StartMapAsync(
        War3MapRuntime map,
        War3MapCatalogEntry entry,
        War3OfflineMapCache? offlineCache,
        string[] arguments)
    {
        _activeMap = map;
        var navigationAuditRequested =
            arguments.Contains(War3NavigationMapAudit.EnableArgument);
        await AdvanceMapLoadingAsync(
            1, 0.14d,
            navigationAuditRequested
                ? "导航专项模式：跳过表现资源，仅加载权威地图与模拟数据。"
                : "检查 Human UI、模型、肖像、命令按钮和队列进度条资源。");
        if (!navigationAuditRequested) ValidateHumanAssets();

        await AdvanceMapLoadingAsync(
            2, 0.24d,
            "读取单位、技能、升级 manifest，并建立玩法目录与稠密类型映射。");
        _buildings = War3HumanContent.CreateBuildingCatalog();
        _production = War3HumanContent.CreateProductionCatalog();
        _technologies = War3HumanContent.CreateTechnologyCatalog();
        _buildingUpgrades = War3HumanContent.CreateBuildingUpgradeCatalog();
        var dataStatus = War3HumanContent.DataStatus;
        var objectDataStatus = War3HumanContent.ObjectDataStatus;
        var buffEffectCatalog = War3HumanContent.BuffEffectDataCatalog;
        var abilityMetadataCatalog = War3HumanContent.AbilityMetadataCatalog;
        _status = dataStatus.ManifestLoaded
            ? $"War3 数据驱动已启用：{dataStatus.AppliedUnitCount} 单位 / " +
              $"{dataStatus.AppliedBuildingCount} 建筑"
            : "War3 数据不可用，已启用内置玩法回退";
        if (dataStatus.ManifestLoaded)
            GD.Print($"WAR3_GAMEPLAY_DATA {dataStatus.LogLine}");
        else
            GD.PushWarning($"WAR3_GAMEPLAY_DATA {dataStatus.LogLine}");
        if (objectDataStatus.AbilityManifestLoaded &&
            objectDataStatus.UpgradeManifestLoaded)
            GD.Print($"WAR3_OBJECT_DATA {objectDataStatus.LogLine}");
        else
            GD.PushWarning($"WAR3_OBJECT_DATA {objectDataStatus.LogLine}");
        var relatedDataLine =
            $"buff_effect={(buffEffectCatalog.IsAvailable ? "loaded" : "missing")}/" +
            $"{buffEffectCatalog.Count} metadata=" +
            $"{(abilityMetadataCatalog.IsAvailable ? "loaded" : "missing")}/" +
            $"{abilityMetadataCatalog.FieldCount}/" +
            $"{abilityMetadataCatalog.BindingCount}";
        if (buffEffectCatalog.IsAvailable && abilityMetadataCatalog.IsAvailable)
            GD.Print($"WAR3_ABILITY_RELATED_DATA {relatedDataLine}");
        else
            GD.PushWarning($"WAR3_ABILITY_RELATED_DATA {relatedDataLine}");
        var abilityDataStatus = War3HumanContent.AbilityImportStatus;
        if (abilityDataStatus.MissingObjectIds.Length == 0)
            GD.Print($"WAR3_ABILITY_DATA {abilityDataStatus.LogLine}");
        else
            GD.PushWarning($"WAR3_ABILITY_DATA {abilityDataStatus.LogLine}");
        if (!ProductionRequirementCatalogValidator.TryValidate(
                _production, _buildings, out var productionError))
            throw new InvalidOperationException(productionError.Message);
        if (!TechnologyCatalogDependencyValidator.TryValidate(
                _technologies, _buildings, out var technologyError))
            throw new InvalidOperationException(technologyError);
        if (!BuildingUpgradeCatalogSnapshot.TryValidateDependencies(
                _buildingUpgrades, _buildings, out var buildingUpgradeError))
            throw new InvalidOperationException(buildingUpgradeError);

        await AdvanceMapLoadingAsync(
            3, 0.36d,
            $"展开 {map.Terrain.Columns}×{map.Terrain.Rows} 地形，" +
            $"装载 {map.Objects.Length} 个地图对象并构建导航拓扑。");
        _terrain = map.Terrain;
        var navigation = map.CreateNavigation();

        await AdvanceMapLoadingAsync(
            4, 0.50d,
            $"按 {War3OfflineMapCache.BattlefieldPathCellSize:0} 世界单位网格烘焙 Small / Medium / Large 净空层。");
        ClearanceBakeSnapshot bake;
        var clearanceReason = "cache_unavailable";
        if (offlineCache is not null &&
            offlineCache.TryLoadClearance(
                navigation, _terrain, out var cachedBake, out clearanceReason) &&
            cachedBake is not null)
        {
            bake = cachedBake;
            GD.Print(
                $"WAR3_OFFLINE_CACHE clearance=hit hash={bake.StableHashText}");
        }
        else
        {
            if (offlineCache is not null)
            {
                GD.PushWarning(
                    $"WAR3_OFFLINE_CACHE clearance=fallback reason={clearanceReason}");
            }
            bake = ClearanceBakeSnapshot.Build(
                navigation,
                _terrain,
                War3OfflineMapCache.BattlefieldPathCellSize);
        }

        await AdvanceMapLoadingAsync(
            5, 0.64d,
            "创建确定性模拟、寻路器、路线规划、阻塞控制和双方初始经济状态。");
        if (navigationAuditRequested)
            War3NavigationMapAudit.BeginProgress(map, arguments);
        StaticWorld world;
        if (offlineCache is not null && !navigationAuditRequested)
        {
            var candidate = CreateSimulation(
                navigation, _terrain, bake, out var candidateWorld);
            if (offlineCache.TryRestoreBootstrap(
                    candidate,
                    map,
                    navigation,
                    bake,
                    _buildings,
                    _production,
                    _technologies,
                    out var restoredRuntime,
                    out var bootstrapReason) &&
                restoredRuntime is not null)
            {
                _simulation = candidate;
                _runtime = restoredRuntime;
                world = candidateWorld;
                GD.Print(
                    $"WAR3_OFFLINE_CACHE bootstrap=hit tick={_simulation.Metrics.Tick} " +
                    $"state={_simulation.ComputeStateHash():X16}");
            }
            else
            {
                GD.PushWarning(
                    $"WAR3_OFFLINE_CACHE bootstrap=fallback reason={bootstrapReason}");
                _simulation = CreateSimulation(
                    navigation, _terrain, bake, out world);
                _runtime = War3HumanScenario.Prepare(
                    _simulation, _buildings, _production, _technologies, map);
            }
        }
        else
        {
            _simulation = CreateSimulation(
                navigation, _terrain, bake, out world);
            if (navigationAuditRequested)
            {
                War3NavigationMapAudit.TraceBootstrap(
                    "status=simulation_created");
                _runtime = War3HumanScenario.PrepareNavigationAudit(
                    _simulation, _buildings, _production, _technologies, map);
            }
            else
            {
                _runtime = War3HumanScenario.Prepare(
                    _simulation, _buildings, _production, _technologies, map);
            }
        }

        if (_offlineBakeRequested)
        {
            if (!War3OfflineMapCache.TryWrite(
                    entry,
                    map,
                    navigation,
                    bake,
                    _simulation,
                    _runtime,
                    _buildings,
                    _production,
                    _technologies,
                    out var path,
                    out var byteCount,
                    out var cacheError))
            {
                throw new InvalidOperationException(
                    $"Offline map cache generation failed: {cacheError}");
            }
            GD.Print(
                $"WAR3_OFFLINE_CACHE bake=success path={path} bytes={byteCount} " +
                $"map={map.StableHashText} clearance={bake.StableHashText} " +
                $"state={_simulation.ComputeStateHash():X16}");
            return true;
        }

        if (navigationAuditRequested)
        {
            var audit = War3NavigationMapAudit.Run(
                _simulation, _runtime, map, arguments);
            _earlyExitCode = audit.Passed ? 0 : 2;
            return true;
        }

        _simulation.WarmPathingCaches();
        _itemShops = new War3ItemShopRuntime();
        _knownInventoryProfiles.Clear();
        _inventoryUnitDataByObjectId.Clear();
        _inventoryCandidatesByObjectId.Clear();
        _inventoryAliveUnits.Clear();
        _itemEffects = new War3ItemEffectRuntime();

        await AdvanceMapLoadingAsync(
            6, 0.77d,
            "生成灯光、经典地表、悬崖/坡道批次和 RTS 相机。");
        CreateLighting();
        CreateGround(_terrain);
        CreateCamera(world);

        await AdvanceMapLoadingAsync(
            7, 0.88d,
            "创建单位/建筑表现器、War3 HUD、肖像视口与生产研究队列。");
        var profileProductionPresentation = arguments.Contains(
            "--war3-profile-production-presentation");
        _presenter = new War3WorldPresenter
        {
            Name = "War3World",
            ProfilingEnabled = _runtimeProfiler is not null,
            ForceFullUnitPresentation = _stressTest is not null &&
                                        !profileProductionPresentation,
            ForceFullCombatEffects = _stressTest is not null &&
                                     !profileProductionPresentation
        };
        if (profileProductionPresentation)
            GD.Print(
                "WAR3_PROFILE_PRESENTATION production_lod=true " +
                "stress_default_unchanged=true");
        AddChild(_presenter);
        _presenter.Initialize(_simulation, _production, _camera!);
        var externalDotnetProfiling = arguments.Contains(
            War3RuntimeProfiler.ExternalDotnetArgument);
        _simulation.RuntimeMetricsProfilingEnabled =
            !externalDotnetProfiling &&
            (_runtimeProfiler is not null || _stressTest is not null ||
             _automatedSkirmishRequested);
        _simulation.DetailedProfilingEnabled =
            !externalDotnetProfiling &&
            (_runtimeProfiler is not null || _stressTest is not null ||
             _automatedSkirmishRequested);
        if (externalDotnetProfiling)
            GD.Print(
                "WAR3_EXTERNAL_DOTNET_PROFILE in_game_probes=false " +
                "stress_workload=true");
        CreateHud();
        _navigationDebugger = new War3NavigationDebugger
        {
            Name = "War3NavigationDebugger"
        };
        AddChild(_navigationDebugger);
        _navigationDebugger.Initialize(
            _simulation,
            _terrain,
            arguments.Contains("--war3-navigation-debug"),
            arguments.Contains("--war3-navigation-debug-grid"));
        InitializeAudio();
        ApplyRuntimeProfileVariant();

        await AdvanceMapLoadingAsync(
            8, 0.96d,
            "同步初始选择、相机位置、可见性和自动化验收状态。");
        _selectedUnits.Add(_runtime.PlayerWorkers[0]);
        RefreshSelection();
        _cameraController!.FocusAt(map.PlayerSpawn, immediate: true);
        if (_heroCapture)
        {
            var hero = _simulation.AddUnit(
                map.PlayerSpawn + new NVector2(190f, 95f),
                _production.UnitType(War3HumanContent.Archmage),
                War3HumanScenario.PlayerId);
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
            _selectedUnits.Add(hero);
            RefreshSelection();
        }
        if (_pcgCapture)
        {
            _hud!.Visible = false;
            _cameraController!.SetAutomationTarget(
                (map.Terrain.Bounds.Min + map.Terrain.Bounds.Max) * 0.5f,
                distance: 82f,
                yaw: 0f,
                pitch: Mathf.DegToRad(70f));
        }
        else if (_terrainCapture)
        {
            _hud!.Visible = false;
            _cameraController!.SetAutomationTarget(
                map.PlayerSpawn + new NVector2(640f, 0f),
                distance: 38f,
                yaw: Mathf.DegToRad(90f),
                pitch: Mathf.DegToRad(54f));
        }
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
                    map.PlayerSpawn + new NVector2(165f, 105f),
                    immediate: true);
            }
            // Warcraft's exported peasant speed is lower than the former demo
            // tuning. Keep the deposit assertion and allow a complete lumber
            // gather-and-return cycle at the data-driven movement speed.
            _smokeEndTick = _simulation.Metrics.Tick + 900;
            _status = "自动回归：验证采集、AI、动画、肖像与特效";
        }
        if (_stressTest is not null)
        {
            _stressTest.Initialize(
                _simulation, _production, _buildings, _runtime);
            if (externalDotnetProfiling)
                _simulation.DetailedProfilingEnabled = false;
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
            RefreshSelection();
            _cameraController!.MaximumDistance = 90f;
            _cameraController.InitialDistance = 62f;
            _cameraController.InitialPitchDegrees = 68f;
            _cameraController.InitialYawDegrees = 0f;
            _cameraController.ResetView(immediate: true);
            _cameraController!.FocusAt(_stressTest.FocusPoint, immediate: true);
            _status = "War3 表现压测：长距离对冲、全单位动画、完整战斗特效与运动质量诊断";
            GD.Print(
                "WAR3_STRESS_PRESENTATION full_unit_actors=true " +
                "full_animation=true full_model_effects=true " +
                "full_projectiles=true impact_budget=unbounded");
        }
        if (_automatedSkirmishRequested)
        {
            _automatedSkirmish = War3AutomatedSkirmishStressMode.TryCreate(
                arguments,
                _simulation,
                _buildings,
                _production,
                _technologies,
                _runtime);
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
            RefreshSelection();
            _cameraController!.FocusAt(
                _automatedSkirmish!.FocusPoint, immediate: true);
            _status = "War3 自动运营压测：双边采集、建造、科技、生产与交战";
        }
        if (_capture) _ = CaptureAsync();
        UpdateHud();
        if (_runtimeProfiler is not null &&
            (_stressTest is not null || _automatedSkirmish is not null))
        {
            var initialPresenterStart =
                System.Diagnostics.Stopwatch.GetTimestamp();
            _presenter?.Sync(1f);
            var initialPresenterMilliseconds = ElapsedMilliseconds(
                initialPresenterStart,
                System.Diagnostics.Stopwatch.GetTimestamp());
            var initialProfile = _presenter?.LastSyncProfile ?? default;
            GD.Print(
                $"WAR3_INITIAL_PRESENTATION_PROFILE " +
                $"total_ms={initialPresenterMilliseconds:0.###} " +
                $"units_ms={initialProfile.UnitsMilliseconds:0.###} " +
                $"unit_animation_ms={initialProfile.UnitAnimationMilliseconds:0.###} " +
                $"units={initialProfile.UnitActorsVisited}/" +
                $"{initialProfile.UnitActorsAlive}/" +
                $"{initialProfile.UnitActorsInFrustum}/" +
                $"{initialProfile.UnitActorsCreated} " +
                $"buildings_ms={initialProfile.BuildingsMilliseconds:0.###} " +
                $"resources_ms={initialProfile.ResourcesMilliseconds:0.###}");
            _presenter?.PrintRuntimeRenderLayout();
        }
        GD.Print($"WAR3_MAP_LOADED id={map.Metadata.Id} hash={map.StableHashText} " +
                 $"terrain={map.Terrain.StableHashText} objects={map.Objects.Length}");
        _runtimeProfiler?.MapReady(GetViewport());
        if (_rightClickInputSmoke) _ = RunRightClickInputSmokeAsync();
        return false;
    }

    private async Task RunRightClickInputSmokeAsync()
    {
        while (_mapLoadInProgress)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_simulation is null || _runtime is null || _camera is null ||
            _runtime.PlayerWorkers.Length == 0)
        {
            GD.Print("WAR3_RIGHT_CLICK_INPUT_SMOKE FAIL reason=runtime-not-ready");
            GetTree().Quit(1);
            return;
        }

        var unit = _runtime.PlayerWorkers[0];
        var versionBefore = _simulation.Units.CommandVersions[unit];
        var screen = GetViewport().GetVisibleRect().Size *
                     new Vector2(0.62f, 0.32f);
        GetViewport().NotifyMouseEntered();
        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            Pressed = true,
            Position = screen,
            GlobalPosition = screen
        }, inLocalCoords: true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            Pressed = false,
            Position = screen,
            GlobalPosition = screen
        }, inLocalCoords: true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var versionAfter = _simulation.Units.CommandVersions[unit];
        var confirmationVisible =
            _presenter?.SawMoveCommandConfirmation == true &&
            _presenter.ActiveCommandConfirmationCount > 0;
        var passed = versionAfter > versionBefore && confirmationVisible;
        GD.Print(
            $"WAR3_RIGHT_CLICK_INPUT_SMOKE {(passed ? "PASS" : "FAIL")} " +
            $"u={unit} command={versionBefore}->{versionAfter} " +
            $"confirmation={confirmationVisible}/" +
            $"{_presenter?.ActiveCommandConfirmationCount ?? 0} " +
            $"mode={_simulation.Units.Modes[unit]} " +
            $"screen={screen.X:0.#},{screen.Y:0.#}");
        GetTree().Quit(passed ? 0 : 1);
    }

    private static RtsSimulation CreateSimulation(
        NavigationMapSnapshot navigation,
        TerrainMapSnapshot terrain,
        ClearanceBakeSnapshot bake,
        out StaticWorld world)
    {
        world = navigation.CreateWorld(terrain);
        var terrainTopology = TerrainNavigationTopologyBuilder.Build(
            terrain, bake);
        return new RtsSimulation(
            world,
            new GridPathProvider(
                world,
                War3OfflineMapCache.BattlefieldPathCellSize,
                staticBake: bake),
            War3HumanScenario.Capacity,
            terrainTopology.CreateRoutePlanner(),
            terrainTopology.CreateChokeController(),
            bake);
    }

    private async Task LoadMapAsync(
        War3MapCatalogEntry entry,
        string[] arguments,
        Label? selectionStatus)
    {
        if (_mapLoadInProgress)
        {
            if (selectionStatus is not null)
                selectionStatus.Text = "另一张地图正在加载，请稍候。";
            return;
        }

        _mapLoadInProgress = true;
        _earlyExitCode = 0;
        if (arguments.Contains(War3NavigationMapAudit.EnableArgument))
            War3NavigationMapAudit.BeginProgress(entry.Manifest.Id, arguments);
        ShowMapLoading(entry.Manifest.DisplayName);
        try
        {
            await AdvanceMapLoadingAsync(
                0, 0.03d,
                $"正在读取 {entry.Manifest.DisplayName} 的 manifest、地形块、对象和出生点…");
            War3OfflineMapCache? offlineCache = null;
            War3MapRuntime? runtime;
            var cacheReason = _offlineBakeRequested
                ? "generation_requested"
                : "cache_unavailable";
            if (!_offlineBakeRequested &&
                War3OfflineMapCache.TryLoadMap(
                    entry,
                    out offlineCache,
                    out runtime,
                    out cacheReason) &&
                runtime is not null)
            {
                GD.Print(
                    $"WAR3_OFFLINE_CACHE map=hit path={offlineCache!.Path} " +
                    $"hash={runtime.StableHashText}");
            }
            else
            {
                if (!_offlineBakeRequested)
                {
                    GD.Print(
                        $"WAR3_OFFLINE_CACHE map=miss reason={cacheReason}");
                }
                if (!War3MapCatalog.TryLoadRuntime(
                        entry, out runtime, out var error) || runtime is null)
                    throw new InvalidOperationException(error);
            }

            if (War3AutomatedSkirmishStressMap.IsRequested(arguments))
            {
                runtime = War3AutomatedSkirmishStressMap.Create(arguments);
                offlineCache = null;
                GD.Print(
                    $"WAR3_AUTO_SKIRMISH_MAP id={runtime.Metadata.Id} " +
                    $"terrain={runtime.Terrain.Columns}x{runtime.Terrain.Rows} " +
                    $"bounds={runtime.Terrain.Bounds.Max.X:0}x" +
                    $"{runtime.Terrain.Bounds.Max.Y:0} objects={runtime.Objects.Length} " +
                    "source=ephemeral cache=disabled");
            }

            await AdvanceMapLoadingAsync(
                0, 0.10d,
                $"地图包读取完成：{runtime.Terrain.Columns}×{runtime.Terrain.Rows} 地形，" +
                $"{runtime.Objects.Length} 个对象。稳定哈希 {runtime.StableHashText}。");
            _mapSelectionLayer?.QueueFree();
            _mapSelectionLayer = null;

            var earlyExitRequested = await StartMapAsync(
                runtime, entry, offlineCache, arguments);
            if (earlyExitRequested)
            {
                HideMapLoading();
                GetTree().Quit(_earlyExitCode);
                return;
            }
            await AdvanceMapLoadingAsync(
                9, 1d,
                $"{entry.Manifest.DisplayName} 已完成：地形、导航、模拟、HUD 与模型状态均已同步。");
            _loadingReachedCompletion = true;

            // Keep 100% visible for one rendered frame before handing input to
            // the battlefield. The loading layer also prevents clicks from
            // leaking into the world while the last UI nodes are being added.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            HideMapLoading();
        }
        catch (Exception exception)
        {
            GD.PushError(
                $"WAR3_MAP_LOAD_FAIL id={entry.Manifest.Id} error={exception.Message}");
            _mapLoadingOverlay?.ShowFailure(exception.Message);
            if (selectionStatus is not null)
            {
                selectionStatus.Text = $"加载失败：{exception.Message}";
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                HideMapLoading();
            }
            else
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GetTree().Quit(1);
            }
        }
        finally
        {
            _mapLoadInProgress = false;
        }
    }

    private void ShowMapLoading(string mapName)
    {
        HideMapLoading();
        _loadingUiReady = false;
        _loadingProgressMonotonic = true;
        _loadingReachedCompletion = false;
        _lastLoadingProgress = 0d;
        _loadingStageUpdateCount = 0;
        _mapLoadingOverlay = new War3MapLoadingOverlay
        {
            Name = "MapLoading"
        };
        AddChild(_mapLoadingOverlay);
        _mapLoadingOverlay.Initialize(mapName);
        _loadingUiReady = _mapLoadingOverlay.InterfaceReady;
    }

    private void HideMapLoading()
    {
        if (_mapLoadingOverlay is null) return;
        _mapLoadingOverlay.QueueFree();
        _mapLoadingOverlay = null;
    }

    private async Task AdvanceMapLoadingAsync(
        int stage,
        double progress,
        string detail)
    {
        var normalized = Math.Clamp(progress, 0d, 1d);
        if (normalized + 0.000_001d < _lastLoadingProgress)
            _loadingProgressMonotonic = false;
        _lastLoadingProgress = Math.Max(_lastLoadingProgress, normalized);
        _loadingStageUpdateCount++;
        _mapLoadingOverlay?.SetProgress(stage, _lastLoadingProgress, detail);
        GD.Print(
            $"WAR3_MAP_LOADING step={stage} progress={_lastLoadingProgress:0.00} " +
            $"detail={detail}");

        // Each milestone represents completed work. Yielding here lets Godot
        // render it and keeps the window responsive between expensive phases.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void ShowMapSelection(
        IReadOnlyList<War3MapCatalogEntry> catalog,
        string[] arguments)
    {
        _mapSelectionLayer = new CanvasLayer { Name = "MapSelection", Layer = 100 };
        AddChild(_mapSelectionLayer);
        var backdrop = new ColorRect
        {
            Color = new Color("111a22"),
            LayoutMode = 1,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _mapSelectionLayer.AddChild(backdrop);
        var panel = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(620f, 0f),
            Position = new Vector2(330f, 100f)
        };
        backdrop.AddChild(panel);
        var title = new Label
        {
            Text = "选择 Warcraft III 战场",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        panel.AddChild(title);
        panel.AddChild(new Label
        {
            Text = "地图来自 res://war3_rts/maps 的版本化 catalog；选择后才加载地形、导航、资源、PCG 与出生点。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var status = new Label
        {
            Text = catalog.Count == 0 ? "未找到有效地图。" : $"发现 {catalog.Count} 张地图",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.AddChild(status);
        foreach (var entry in catalog)
        {
            var button = new Button
            {
                Text = $"{entry.Manifest.DisplayName}  ·  {entry.Manifest.RecommendedPlayers} 人\n" +
                       $"{entry.Manifest.Description}",
                CustomMinimumSize = new Vector2(0f, 72f)
            };
            button.Pressed += () => _ = LoadMapAsync(entry, arguments, status);
            panel.AddChild(button);
        }
        var back = new Button { Text = "返回启动页" };
        back.Pressed += () =>
            GetTree().ChangeSceneToFile(DemoSceneCatalog.CompatibilityEntry);
        panel.AddChild(back);
    }

    private async Task CaptureMapSelectionAsync()
    {
        if (DisplayServer.GetName().Equals(
                "headless", StringComparison.OrdinalIgnoreCase))
        {
            GD.Print("WAR3_MAP_SELECTION_CAPTURE skipped=headless");
            GetTree().Quit(0);
            return;
        }
        for (var frame = 0; frame < 45; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var image = GetViewport().GetTexture().GetImage();
        const string path = "user://war3_map_selection_capture.png";
        var result = image.SavePng(path);
        GD.Print($"WAR3_MAP_SELECTION_CAPTURE success={result == Error.Ok} " +
                 $"path={ProjectSettings.GlobalizePath(path)}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    public override void _ExitTree()
    {
        if (_previousMaximumPhysicsStepsPerFrame > 0)
        {
            Engine.MaxPhysicsStepsPerFrame =
                _previousMaximumPhysicsStepsPerFrame;
            _previousMaximumPhysicsStepsPerFrame = -1;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mapLoadInProgress || _simulation is null || _runtime is null) return;
        var profileStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var frame = (float)Math.Min(delta, 0.05d);
        var advanceSimulation =
            _navigationDebugger?.ConsumeAdvanceSimulation() ?? true;
        var simulationSteps = advanceSimulation
            ? _automatedSkirmish?.TicksPerPhysicsFrame ?? 1
            : 0;
        var stressMilliseconds = 0d;
        var aiMilliseconds = 0d;
        var simulationMilliseconds = 0d;
        var aiProfile = default(RtsDemo.AI.RtsAiUpdateProfile);
        var stressProfile = default(War3StressUpdateProfile);
        if (advanceSimulation)
        {
            for (var step = 0; step < simulationSteps; step++)
            {
                var stepAllocationStart = GC.GetAllocatedBytesForCurrentThread();
                var stepStart = System.Diagnostics.Stopwatch.GetTimestamp();
                var automationProfile =
                    default(War3AutomatedSkirmishUpdateProfile);
                _stressTest?.Update();
                var stressEnd =
                    System.Diagnostics.Stopwatch.GetTimestamp();
                stressMilliseconds += ElapsedMilliseconds(stepStart, stressEnd);
                stressProfile = _stressTest?.LastUpdateProfile ?? default;
                if (_automatedSkirmish is not null)
                {
                    _automatedSkirmish.UpdateBeforeSimulation();
                    automationProfile = _automatedSkirmish.LastUpdateProfile;
                    aiProfile = _automatedSkirmish.LastAiProfile;
                    stressMilliseconds += Math.Max(
                        0d,
                        automationProfile.TotalMilliseconds -
                        automationProfile.AiMilliseconds);
                    aiMilliseconds += automationProfile.AiMilliseconds;
                }
                else if (_stressTest is null)
                {
                    _runtime.AiDirector.Update(_simulation.Metrics.Tick);
                    var aiEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                    aiMilliseconds += ElapsedMilliseconds(stressEnd, aiEnd);
                    aiProfile = _runtime.AiDirector.LastUpdateProfile;
                    stressProfile = _stressTest?.LastUpdateProfile ?? default;
                }

                var simulationStart =
                    System.Diagnostics.Stopwatch.GetTimestamp();
                _simulation.Tick(frame);
                UpdateGroundItemLifecycle();
                _stressTest?.ObserveAfterSimulation();
                _itemShops?.Update(frame);
                if (_itemEffects is not null)
                    _itemEffects.Update(frame, _simulation);
                ConsumeAudioEvents();
                var stepEnd =
                    System.Diagnostics.Stopwatch.GetTimestamp();
                var stepSimulationMilliseconds = ElapsedMilliseconds(
                    simulationStart, stepEnd);
                simulationMilliseconds += stepSimulationMilliseconds;
                if (_automatedSkirmish is not null)
                {
                    _runtimeProfiler?.RecordAutomatedSkirmishStep(
                        ElapsedMilliseconds(stepStart, stepEnd),
                        stepSimulationMilliseconds,
                        GC.GetAllocatedBytesForCurrentThread() -
                        stepAllocationStart,
                        _simulation.Metrics,
                        aiProfile,
                        automationProfile);
                }
                _elapsed += frame;
            }
        }
        var simulationEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_smoke && advanceSimulation) UpdateSmokeConstructionCycle();
        PruneSelection();
        _navigationDebugger?.SamplePhysics(delta);
        _hudAccumulator += advanceSimulation ? frame * simulationSteps : delta;
        if (_hudAccumulator >= 0.1d)
        {
            _hudAccumulator = 0d;
            UpdateHud();
        }
        if (_smoke && _simulation.Metrics.Tick >= _smokeEndTick)
            FinishSmoke();
        var profileEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        _runtimeProfiler?.RecordPhysics(
            ElapsedMilliseconds(profileStart, profileEnd),
            stressMilliseconds,
            aiMilliseconds,
            simulationMilliseconds,
            ElapsedMilliseconds(simulationEnd, profileEnd),
            GC.GetAllocatedBytesForCurrentThread() - allocationStart,
            _simulation.Metrics,
            aiProfile,
            stressProfile);
    }

    public override void _Process(double delta)
    {
        if (_mapLoadInProgress || _simulation is null) return;
        var modelActorProfile = War3ModelActor.CaptureRuntimeProfile();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var previewStart = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdatePointerFeedback();
        var presenterStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _presenter?.Sync((float)Engine.GetPhysicsInterpolationFraction());
        var presenterEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_runtimeProfiler?.RecordFrameAndShouldQuit(
                ElapsedMilliseconds(presenterStart, presenterEnd),
                ElapsedMilliseconds(previewStart, presenterStart),
                GC.GetAllocatedBytesForCurrentThread() - allocationStart,
                _presenter?.LastSyncProfile ?? default,
                modelActorProfile) == true)
        {
            _stressTest?.PrintSummary();
            _automatedSkirmish?.PrintSummary();
            GetTree().Quit(0);
        }
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// World right-click commands are captured before GUI dispatch. The HUD is
    /// a full-screen Control and some of its dynamic children consume mouse
    /// events, so relying on _UnhandledInput makes otherwise identical world
    /// clicks disappear before the command code can observe them.
    /// </summary>
    public override void _Input(InputEvent inputEvent)
    {
        if (_mapLoadInProgress || _simulation is null || _camera is null ||
            inputEvent is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Right,
                Pressed: true
            } mouse)
            return;
        HandleRightMouse(mouse);
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_mapLoadInProgress || _simulation is null || _camera is null) return;
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
        if (_navigationDebugger?.BlocksWorldPointer(mouse.Position) == true)
            return;
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
    }

    private void HandleRightMouse(InputEventMouseButton mouse)
    {
        if (_navigationDebugger?.BlocksWorldPointer(mouse.Position) == true)
        {
            TraceRightPointer(mouse, "debug-ui");
            return;
        }
        if (HasTargetMode() || _buildMenu || _learnMenu)
        {
            TraceRightPointer(mouse, "cancel-mode");
            CancelMode();
            return;
        }
        if (_hud?.BlocksWorldPointer(mouse.Position) == true)
        {
            TraceRightPointer(mouse, "hud-ui");
            return;
        }
        if (!TryGroundPoint(mouse.Position, out var point))
        {
            TraceRightPointer(mouse, "ground-miss");
            return;
        }
        if (_selectedUnits.Count > 0)
        {
            TraceRightPointer(mouse, "unit-command", point);
            IssueContext(point, mouse.ShiftPressed);
            return;
        }
        if (SelectedProductionBuildings().Length > 0)
        {
            TraceRightPointer(mouse, "rally-command", point);
            SetRallyAt(point);
            return;
        }
        TraceRightPointer(mouse, "no-command-selection", point);
    }

    private void TraceRightPointer(
        InputEventMouseButton mouse,
        string route,
        NVector2? world = null)
    {
        GD.Print(
            $"WAR3_POINTER_RIGHT tick={_simulation?.Metrics.Tick ?? -1} " +
            $"screen={mouse.Position.X:0.#},{mouse.Position.Y:0.#} " +
            $"world={(world.HasValue ? $"{world.Value.X:0.#},{world.Value.Y:0.#}" : "-")} " +
            $"route={route} queued={mouse.ShiftPressed} " +
            $"selected={_selectedUnits.Count}/{_selectedBuildings.Count} " +
            $"targetMode={HasTargetMode()} buildMenu={_buildMenu} mode={_mode}");
    }

    private void HandleKey(InputEventKey input)
    {
        if (_navigationDebugger?.HandleHotkey(input.Keycode) == true) return;
        switch (input.Keycode)
        {
            case Key.Escape:
                CancelMode();
                return;
            case Key.F:
                FocusSelection();
                return;
            case Key.Home:
                _cameraController?.FocusAt(
                    _runtime?.PlayerHome ?? War3HumanScenario.PlayerHome);
                return;
            case Key.Tab:
                CycleSelectionSubgroup(input.ShiftPressed ? -1 : 1);
                return;
        }
        _hud?.TryInvokeHotkey(input.Keycode);
    }

    private void ExecuteCommand(War3CommandSnapshot command)
    {
        if (!command.Enabled || _simulation is null) return;
        PlayInterfaceAudio();
        switch (command.Kind)
        {
            case War3CommandKind.Move:
                BeginTarget(TargetMode.Move);
                break;
            case War3CommandKind.AttackMove:
                BeginTarget(TargetMode.AttackMove);
                break;
            case War3CommandKind.Stop:
                var stop = _simulation.IssuePlayerStop(
                    War3HumanScenario.PlayerId, SelectedUnits());
                Report(stop.Succeeded ? "已停止当前命令" : "停止命令失败");
                if (stop.Succeeded) PlayCommandAudio(attack: false);
                break;
            case War3CommandKind.Hold:
                var hold = _simulation.IssuePlayerHold(
                    War3HumanScenario.PlayerId, SelectedUnits());
                Report(hold.Succeeded ? "保持当前位置" : "保持命令失败");
                if (hold.Succeeded) PlayCommandAudio(attack: false);
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
            case War3CommandKind.Upgrade:
                UpgradeBuilding(command.DataId);
                break;
            case War3CommandKind.Rally:
                BeginTarget(TargetMode.Rally);
                break;
            case War3CommandKind.Ability:
                BeginAbility(command.DataId);
                break;
            case War3CommandKind.BuildingAbility:
                UseBuildingAbility(command.DataId);
                break;
            case War3CommandKind.PurchaseItem:
                PurchaseShopItem(command.DataId);
                break;
            case War3CommandKind.OpenLearnMenu:
                _learnMenu = true;
                _buildMenu = false;
                _mode = "学习英雄技能";
                break;
            case War3CommandKind.LearnAbility:
                LearnAbility(command.DataId);
                break;
            case War3CommandKind.Cancel:
                CancelMode();
                break;
        }
        UpdateHud();
    }

    private void ToggleCommandAutoCast(War3CommandSnapshot command)
    {
        if (_simulation is null || !command.AutoCastAvailable ||
            command.Kind != War3CommandKind.Ability)
            return;
        var enabled = !command.Toggled;
        var changed = 0;
        foreach (var unit in ActiveSubgroupUnits())
        {
            if (!_simulation.Units.Alive[unit] ||
                _simulation.Combat.Teams[unit] != War3HumanScenario.PlayerId ||
                !_simulation.Abilities.Observe(unit).Abilities.Any(value =>
                    value.AbilityId == command.DataId))
                continue;
            if (_simulation.IssueSetAbilityAutoCast(
                    War3HumanScenario.PlayerId, unit,
                    command.DataId, enabled).Succeeded)
                changed++;
        }
        PlayInterfaceAudio();
        Report(changed > 0
            ? $"{command.Label} 自动施放已{(enabled ? "开启" : "关闭")}"
            : $"{command.Label} 自动施放切换失败");
        UpdateHud();
    }

    private void BeginTarget(TargetMode target)
    {
        _movePending = target == TargetMode.Move && _selectedUnits.Count > 0;
        _attackMovePending = target == TargetMode.AttackMove && _selectedUnits.Count > 0;
        _rallyPending = target == TargetMode.Rally && SelectedProductionBuildings().Length > 0;
        _pendingBuilding = -1;
        _buildMenu = false;
        _learnMenu = false;
        _pendingAbilityId = -1;
        _pendingAbilityCaster = -1;
        _pendingAbilityBuilding = -1;
        _mode = target switch
        {
            TargetMode.Move when _movePending => "移动：左键指定位置",
            TargetMode.AttackMove when _attackMovePending => "攻击移动：左键指定位置",
            TargetMode.Rally when _rallyPending => "集结点：左键指定位置",
            _ => "当前选择无法执行该命令"
        };
    }

    private void BeginAbility(int abilityId)
    {
        if (_simulation is null ||
            (uint)abilityId >= (uint)_simulation.Abilities.Catalog.Count)
            return;
        var caster = ActiveSubgroupUnits().FirstOrDefault(unit =>
            _simulation.Units.Alive[unit] &&
            _simulation.Combat.Teams[unit] == War3HumanScenario.PlayerId &&
            _simulation.Abilities.Observe(unit).Abilities.Any(value =>
                value.AbilityId == abilityId), -1);
        if (caster < 0)
        {
            Report("当前选择没有能够施放该技能的单位");
            return;
        }
        var ability = _simulation.Abilities.Catalog.Ability(abilityId);
        if (ability.Activation is AbilityActivationKind.Instant or
            AbilityActivationKind.Toggle)
        {
            var result = _simulation.IssueAbility(
                War3HumanScenario.PlayerId, caster, abilityId,
                AbilityCastTarget.Self(caster, _simulation.Units.Positions[caster]));
            Report(result.Succeeded
                ? $"已施放 {ability.Name}"
                : $"{ability.Name} 施放失败：{result.Code}");
            return;
        }
        _movePending = false;
        _attackMovePending = false;
        _rallyPending = false;
        _pendingAbilityId = -1;
        _pendingAbilityCaster = -1;
        _pendingAbilityBuilding = -1;
        _pendingBuilding = -1;
        _buildMenu = false;
        _pendingAbilityId = abilityId;
        _pendingAbilityCaster = caster;
        _mode = ability.Activation is AbilityActivationKind.TargetUnit or
            AbilityActivationKind.ChannelUnit
            ? $"{ability.Name}：选择目标单位"
            : $"{ability.Name}：选择目标位置";
    }

    private void UseBuildingAbility(int abilityId)
    {
        if (_simulation is null || _selectedBuildings.Count == 0 ||
            (uint)abilityId >= (uint)_simulation.Abilities.Catalog.Count)
            return;
        var building = new GameplayBuildingId(ActiveSelectionBuilding());
        var ability = _simulation.Abilities.Catalog.Ability(abilityId);
        if (ability.Activation is not (
                AbilityActivationKind.Instant or
                AbilityActivationKind.Toggle))
        {
            CancelMode();
            _pendingAbilityId = abilityId;
            _pendingAbilityBuilding = building.Value;
            _mode = ability.Activation is AbilityActivationKind.TargetUnit or
                AbilityActivationKind.ChannelUnit
                ? $"{ability.Name}：选择目标单位"
                : $"{ability.Name}：选择目标位置";
            return;
        }
        var result = _simulation.IssueBuildingAbility(
            War3HumanScenario.PlayerId, building, abilityId);
        Report(result.Succeeded
            ? $"已执行 {ability.Name}"
            : $"{ability.Name} 执行失败：{result.Code}");
    }

    private void LearnAbility(int abilityId)
    {
        if (_simulation is null) return;
        var caster = ActiveSubgroupUnits().FirstOrDefault(unit =>
        {
            if (!_simulation.Units.Alive[unit] ||
                _simulation.Combat.Teams[unit] != War3HumanScenario.PlayerId)
                return false;
            var state = _simulation.Abilities.Observe(unit);
            return state.Hero && state.Abilities.Any(value =>
                value.AbilityId == abilityId);
        }, -1);
        if (caster < 0)
        {
            Report("当前选择没有能够学习该技能的英雄");
            return;
        }
        var ability = _simulation.Abilities.Catalog.Ability(abilityId);
        var result = _simulation.IssueLearnAbility(
            War3HumanScenario.PlayerId, caster, abilityId);
        if (result.Succeeded)
        {
            _learnMenu = false;
            _mode = "普通选择";
        }
        Report(result.Succeeded
            ? $"已学习 {ability.Name}"
            : $"{ability.Name} 学习失败：{result.Code}");
    }

    private void CompleteTarget(NVector2 point, bool queued)
    {
        if (_simulation is null) return;
        if (_pendingItemSlot >= 0)
        {
            CompleteItemTarget(point);
            return;
        }
        if (_pendingBuilding >= 0)
        {
            IssueConstruction(point, queued);
            return;
        }
        if (_pendingAbilityId >= 0)
        {
            CompleteAbilityTarget(point);
            return;
        }
        if (_movePending)
        {
            var result = _simulation.IssuePlayerMove(
                War3HumanScenario.PlayerId, SelectedUnits(), point, queued);
            Report(result.Succeeded ? "已下达移动命令" : $"移动失败：{result.Code}");
            if (result.Succeeded)
            {
                PlayCommandAudio(attack: false, queued);
                _presenter?.ShowCommandConfirmation(
                    point, War3CommandFeedbackKind.Move);
            }
        }
        else if (_attackMovePending)
        {
            var selected = SelectedUnits();
            var objectTarget = ResolveCombatObjectTarget(point);
            var result = objectTarget.Value >= 0
                ? _simulation.IssuePlayerAttackObject(
                    War3HumanScenario.PlayerId,
                    selected,
                    objectTarget,
                    queued)
                : _simulation.IssuePlayerAttackMove(
                    War3HumanScenario.PlayerId, selected, point, queued);
            Report(result.Succeeded
                ? objectTarget.Value >= 0
                    ? "已下达攻击可破坏物命令"
                    : "已下达攻击移动"
                : $"攻击命令失败：{result.Code}");
            if (result.Succeeded)
            {
                PlayCommandAudio(attack: true, queued);
                _presenter?.ShowCommandConfirmation(
                    point, War3CommandFeedbackKind.Attack);
            }
        }
        else if (_rallyPending)
        {
            SetRallyAt(point);
        }
        if (!queued) CancelMode();
    }

    private CombatObjectId ResolveCombatObjectTarget(NVector2 point)
    {
        if (_simulation is null) return new CombatObjectId(-1);
        var best = new CombatObjectId(-1);
        var bestDistance = float.PositiveInfinity;
        foreach (var value in _simulation.CombatObjects.CreateOverview())
        {
            if (!value.Alive || !value.Bounds.Expanded(8f).Contains(point))
                continue;
            var distance = NVector2.DistanceSquared(point, value.Position);
            if (distance >= bestDistance) continue;
            best = value.Id;
            bestDistance = distance;
        }
        return best;
    }

    private void CompleteAbilityTarget(NVector2 point)
    {
        if (_simulation is null)
        {
            CancelMode();
            return;
        }
        var ability = _simulation.Abilities.Catalog.Ability(_pendingAbilityId);
        if (_pendingAbilityBuilding >= 0)
        {
            var building = new GameplayBuildingId(_pendingAbilityBuilding);
            if (!_simulation.Construction.IsAlive(building))
            {
                CancelMode();
                return;
            }
            var buildingTarget = AbilityCastTarget.Point(point);
            var buildingResult = _simulation.IssueBuildingAbility(
                War3HumanScenario.PlayerId, building,
                _pendingAbilityId, buildingTarget);
            Report(buildingResult.Succeeded
                ? $"已施放 {ability.Name}"
                : $"{ability.Name} 施放失败：{buildingResult.Code}");
            if (buildingResult.Succeeded) CancelMode();
            return;
        }
        if (_pendingAbilityCaster < 0 ||
            !_simulation.Units.Alive[_pendingAbilityCaster])
        {
            CancelMode();
            return;
        }
        AbilityCastTarget target;
        if (ability.Activation is AbilityActivationKind.TargetUnit or
            AbilityActivationKind.ChannelUnit)
        {
            var smart = ResolveSmartTarget(point);
            target = smart.Kind switch
            {
                SmartCommandTargetKind.FriendlyUnit or
                    SmartCommandTargetKind.EnemyUnit =>
                    AbilityCastTarget.Unit(smart.Unit, smart.Position),
                SmartCommandTargetKind.FriendlyBuilding or
                    SmartCommandTargetKind.EnemyBuilding =>
                    AbilityCastTarget.Building(
                        new GameplayBuildingId(smart.Building), smart.Position),
                _ => default
            };
            if (target.Kind == AbilityTargetKind.None)
            {
                Report($"{ability.Name} 需要选择单位或建筑目标");
                return;
            }
        }
        else
        {
            target = AbilityCastTarget.Point(point);
        }
        var result = _simulation.IssueAbility(
            War3HumanScenario.PlayerId, _pendingAbilityCaster,
            _pendingAbilityId, target);
        Report(result.Succeeded
            ? $"已施放 {ability.Name}"
            : $"{ability.Name} 施放失败：{result.Code}");
        if (result.Succeeded) CancelMode();
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
        var selected = SelectedUnits();
        var result = _simulation.IssuePlayerSmartCommand(
            War3HumanScenario.PlayerId, selected, target,
            attackMoveModifier: false, queued);
        _navigationDebugger?.RecordSmartCommand(
            point,
            target,
            selected,
            queued,
            result.Succeeded,
            result.Code.ToString());
        Report(result.Succeeded
            ? target.Kind switch
            {
                SmartCommandTargetKind.ResourceNode => "农民开始采集",
                SmartCommandTargetKind.EnemyUnit or SmartCommandTargetKind.EnemyBuilding =>
                    "部队开始攻击",
                _ => "部队开始移动"
            }
            : $"命令失败：{result.Code}");
        if (result.Succeeded)
        {
            var attack = target.Kind is SmartCommandTargetKind.EnemyUnit or
                SmartCommandTargetKind.EnemyBuilding;
            PlayCommandAudio(attack, queued);
            if (target.Kind != SmartCommandTargetKind.ResourceNode ||
                _presenter?.FlashResourceTarget(
                    new EconomyResourceNodeId(target.ResourceNode)) != true)
            {
                _presenter?.ShowCommandConfirmation(
                    target.Position,
                    War3CommandFeedbackCatalog.ForSmartTarget(target.Kind));
            }
        }
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
            if (!War3PointerTargeting.HitsBuilding(building.Bounds, point))
                continue;
            const float distance = 0f;
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
        if (result.Succeeded)
            _worldAudio?.PlayBuildingPlaced(War3HumanScenario.PlayerId);
        else
            _worldAudio?.PlayInterfaceError();
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
                !_simulation.BuildingUpgrades.SatisfiesBuildingType(
                    _simulation.Construction.Observe(id).Type.Id,
                    recipe.ProducerBuildingTypeId))
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

    private void UpgradeBuilding(int profileId)
    {
        if (_simulation is null || _buildingUpgrades is null ||
            (uint)profileId >= (uint)_buildingUpgrades.Profiles.Length)
            return;
        var profile = _buildingUpgrades.Profiles[profileId];
        var resultText = $"请选择{War3HumanContent.Buildings[profile.SourceBuildingTypeId].Name}";
        foreach (var value in _selectedBuildings.Order())
        {
            var building = new GameplayBuildingId(value);
            if (!_simulation.Construction.IsAlive(building) ||
                _simulation.Construction.Observe(building).Type.Id !=
                    profile.SourceBuildingTypeId)
                continue;
            var result = _simulation.IssueBuildingUpgrade(
                War3HumanScenario.PlayerId, building, profile);
            resultText = result.Succeeded
                ? $"开始升级：{profile.TargetType.Name}"
                : $"升级失败：{result.Code}";
            if (result.Succeeded) break;
        }
        Report(resultText);
    }

    private void CancelQueueItem(War3QueueItemSnapshot item)
    {
        if (_simulation is null || !item.CanCancel) return;
        var canceled = item.Kind switch
        {
            War3QueueItemKind.Production => _simulation.CancelProduction(
                War3HumanScenario.PlayerId,
                new ProductionOrderId(item.OrderId)),
            War3QueueItemKind.Research => _simulation.CancelResearch(
                War3HumanScenario.PlayerId,
                new ResearchOrderId(item.OrderId)),
            War3QueueItemKind.BuildingUpgrade =>
                _simulation.CancelBuildingUpgrade(
                    War3HumanScenario.PlayerId,
                    new BuildingUpgradeOrderId(item.OrderId)),
            _ => false
        };
        Report(canceled
            ? $"已取消：{item.Label}"
            : $"无法取消：{item.Label}");
        UpdateHud();
    }

    private War3CommandSnapshot[] CreateArcaneVaultCommands(
        GameplayBuildingId shop,
        int buyer)
    {
        if (_simulation is null || _itemShops is null)
            return [];
        var inventorySlots = buyer >= 0 ? InventorySlotsForUnit(buyer) : 0;
        var canGetItems = buyer >= 0 &&
                          TryInventoryProfileForUnit(
                              buyer, out var inventoryProfile) &&
                          inventoryProfile.CanGetItems;
        var townTier = HighestHumanTownTier();
        return War3ItemShopRuntime.ArcaneVaultItems
            .Select(item =>
            {
                var offer = _itemShops.Offer(
                    shop.Value, item.RuntimeId, buyer, inventorySlots,
                    canGetItems,
                    townTier, _simulation.Economy.Players,
                    War3HumanScenario.PlayerId);
                var lumber = item.Cost.VespeneGas > 0
                    ? $" / {item.Cost.VespeneGas} 木材"
                    : string.Empty;
                var buyerLabel = buyer >= 0
                    ? War3HumanContent.ResolveUnit(
                        _simulation, _production!, buyer).Name
                    : "无可用购买者";
                var state = offer.Available
                    ? War3CommandVisualState.Ready
                    : War3CommandVisualState.Unavailable;
                var reason = offer.Available
                    ? "可购买"
                    : offer.Reason;
                return new War3CommandSnapshot(
                    item.CommandSlot,
                    War3CommandKind.PurchaseItem,
                    item.RuntimeId,
                    item.Name,
                    $"{item.Description}\n" +
                    $"价格：{item.Cost.Minerals} 黄金{lumber}\n" +
                    $"库存：{offer.Stock}/{item.MaximumStock} · " +
                    $"补货 {item.RestockSeconds:0} 秒\n" +
                    $"解锁：{item.RequirementLabel} · " +
                    $"购买者：{buyerLabel}\n状态：{reason}",
                    item.IconPath,
                    item.Hotkey,
                    offer.Available,
                    State: state,
                    Badge: offer.Stock.ToString());
            })
            .ToArray();
    }

    private void PurchaseShopItem(int itemRuntimeId)
    {
        if (_simulation is null || _itemShops is null ||
            _selectedBuildings.Count == 0)
            return;
        var id = new GameplayBuildingId(ActiveSelectionBuilding());
        if (!_simulation.Construction.IsAlive(id)) return;
        var building = _simulation.Construction.Observe(id);
        if (building.PlayerId != War3HumanScenario.PlayerId ||
            building.State != BuildingLifecycleState.Completed ||
            building.Type.Id != War3HumanContent.ArcaneVault)
        {
            Report("当前选择不是可用的神秘藏宝室");
            return;
        }
        var buyer = LocalShopUser(building);
        var slots = buyer >= 0 ? InventorySlotsForUnit(buyer) : 0;
        War3InventoryAbilityProfile? inventoryProfile = null;
        if (buyer >= 0 && TryInventoryProfileForUnit(
                buyer, out var resolvedInventoryProfile))
            inventoryProfile = resolvedInventoryProfile;
        var canGetItems = inventoryProfile?.CanGetItems == true;
        var purchase = _itemShops.Purchase(
            id.Value, itemRuntimeId, buyer, slots, canGetItems,
            HighestHumanTownTier(),
            _simulation.Economy.Players, War3HumanScenario.PlayerId);
        if (purchase.Succeeded &&
            inventoryProfile?.CanUseItems == true &&
            purchase.Item.UseKind == War3ItemUseKind.OrbOfFire)
            _itemEffects?.ApplyOrbOfFire(_simulation, buyer, purchase.Item);
        Report(purchase.Succeeded
            ? $"{purchase.Item.Name} 已放入 " +
              $"{War3HumanContent.ResolveUnit(_simulation, _production!, buyer).Name} 的物品栏"
            : $"购买失败：{ShopPurchaseReason(purchase.Code)}");
    }

    private void UseInventoryItem(int slot)
    {
        if (_simulation is null || _production is null ||
            _itemShops is null || _itemEffects is null)
            return;
        var unit = ActiveSelectionUnit();
        if (unit < 0 || _simulation.Combat.Teams[unit] !=
            War3HumanScenario.PlayerId)
        {
            Report("只有己方当前单位可以使用物品");
            return;
        }
        var canUseItems = TryInventoryProfileForUnit(
            unit, out var inventoryProfile) && inventoryProfile.CanUseItems;
        var validation = _itemShops.ValidateUse(
            unit, slot, canUseItems, out var item);
        if (validation != War3ItemUseCode.Success)
        {
            Report($"{item.Name} 无法使用：{ItemUseReason(validation)}");
            return;
        }
        if (item.RequiresTarget)
        {
            CancelMode();
            _pendingItemUnit = unit;
            _pendingItemSlot = slot;
            _mode = $"使用 {item.Name}：选择目标";
            _cursor?.SetMode(War3CursorMode.TargetSelect);
            UpdateHud();
            return;
        }

        var result = item.UseKind switch
        {
            War3ItemUseKind.RegenerationScroll =>
                _itemEffects.UseRegenerationScroll(_simulation, unit, item),
            War3ItemUseKind.ClarityPotion =>
                _itemEffects.UseClarityPotion(_simulation, unit, item),
            War3ItemUseKind.MechanicalCritter =>
                _itemEffects.UseMechanicalCritter(
                    _simulation,
                    unit,
                    item,
                    _production.UnitTypes[War3HumanContent.Peasant],
                    out _),
            War3ItemUseKind.HealingPotion =>
                _itemEffects.UseHealingPotion(_simulation, unit, item),
            War3ItemUseKind.ManaPotion =>
                _itemEffects.UseManaPotion(_simulation, unit, item),
            _ => War3ItemUseCode.PassiveItem
        };
        FinishItemUse(unit, slot, item, result);
    }

    private void CompleteItemTarget(NVector2 point)
    {
        if (_simulation is null || _buildings is null ||
            _itemShops is null || _itemEffects is null ||
            _pendingItemUnit < 0 || _pendingItemSlot < 0)
        {
            CancelMode();
            return;
        }
        var unit = _pendingItemUnit;
        var slot = _pendingItemSlot;
        var canUseItems = TryInventoryProfileForUnit(
            unit, out var inventoryProfile) && inventoryProfile.CanUseItems;
        var validation = _itemShops.ValidateUse(
            unit, slot, canUseItems, out var item);
        if (validation != War3ItemUseCode.Success)
        {
            FinishItemUse(unit, slot, item, validation);
            return;
        }
        var smart = ResolveSmartTarget(point);
        var result = item.UseKind switch
        {
            War3ItemUseKind.TownPortal =>
                UseTownPortalTarget(unit, smart, item),
            War3ItemUseKind.IvoryTower =>
                UseIvoryTowerTarget(point, item),
            War3ItemUseKind.SanctuaryStaff =>
                UseSanctuaryTarget(unit, smart, item),
            _ => War3ItemUseCode.InvalidTarget
        };
        FinishItemUse(unit, slot, item, result);
    }

    private War3ItemUseCode UseTownPortalTarget(
        int caster,
        in SmartCommandTarget target,
        War3ShopItemDefinition item)
    {
        if (_simulation is null || _itemEffects is null ||
            target.Kind != SmartCommandTargetKind.FriendlyBuilding)
            return War3ItemUseCode.InvalidTarget;
        var id = new GameplayBuildingId(target.Building);
        if (!_simulation.Construction.IsAlive(id))
            return War3ItemUseCode.InvalidTarget;
        var building = _simulation.Construction.Observe(id);
        if (building.State != BuildingLifecycleState.Completed ||
            building.Type.Function != BuildingFunctionKind.TownHall ||
            !_simulation.Diplomacy.IsFriendly(
                _simulation.Combat.Teams[caster], building.PlayerId))
            return War3ItemUseCode.InvalidTarget;
        return _itemEffects.BeginTownPortal(
            _simulation, caster, BuildingArrival(building, caster), item);
    }

    private War3ItemUseCode UseIvoryTowerTarget(
        NVector2 point,
        War3ShopItemDefinition item)
    {
        if (_simulation is null || _buildings is null)
            return War3ItemUseCode.InvalidTarget;
        var objectId = item.UnitIds.FirstOrDefault();
        var building = War3HumanContent.Buildings.FirstOrDefault(value =>
            value.ObjectId.Equals(objectId, StringComparison.Ordinal));
        if (building is null) return War3ItemUseCode.InvalidTarget;
        var profile = _buildings.Type(building.TypeId) with
        {
            Cost = default,
            BuildSeconds = MathF.Max(0.01f, item.Duration),
            ConstructionMethod = ConstructionMethodKind.StartAndRelease
        };
        return _simulation.TryCreateReleasedBuilding(
            War3HumanScenario.PlayerId, profile, point, out _)
            ? War3ItemUseCode.Success
            : War3ItemUseCode.PlacementBlocked;
    }

    private War3ItemUseCode UseSanctuaryTarget(
        int caster,
        in SmartCommandTarget target,
        War3ShopItemDefinition item)
    {
        if (_simulation is null || _itemEffects is null ||
            target.Kind != SmartCommandTargetKind.FriendlyUnit)
            return War3ItemUseCode.InvalidTarget;
        if (!TryHighestTownHall(
                _simulation.Combat.Teams[caster], out var townHall))
            return War3ItemUseCode.InvalidTarget;
        return _itemEffects.UseSanctuaryStaff(
            _simulation,
            caster,
            target.Unit,
            BuildingArrival(townHall, target.Unit),
            item);
    }

    private bool TryHighestTownHall(
        int playerId,
        out GameplayBuildingSnapshot townHall)
    {
        townHall = default;
        if (_simulation is null) return false;
        var values = _simulation.CreateGameplayBuildingOverview()
            .Where(value => value.PlayerId == playerId &&
                            value.State == BuildingLifecycleState.Completed &&
                            value.Type.Function == BuildingFunctionKind.TownHall)
            .OrderByDescending(value => value.Type.Id switch
            {
                War3HumanContent.Castle => 3,
                War3HumanContent.Keep => 2,
                _ => 1
            })
            .ThenBy(value => value.Id.Value)
            .ToArray();
        if (values.Length == 0) return false;
        townHall = values[0];
        return true;
    }

    private NVector2 BuildingArrival(
        in GameplayBuildingSnapshot building,
        int unit)
    {
        var radius = _simulation is not null &&
                     (uint)unit < (uint)_simulation.Units.Count
            ? _simulation.Units.Radii[unit]
            : 8f;
        return new NVector2(
            (building.Bounds.Min.X + building.Bounds.Max.X) * 0.5f,
            building.Bounds.Max.Y + radius + 8f);
    }

    private void FinishItemUse(
        int unit,
        int slot,
        in War3ShopItemDefinition item,
        War3ItemUseCode result)
    {
        if (result == War3ItemUseCode.Success)
        {
            _itemShops?.CommitUse(unit, slot);
            CancelMode();
            Report($"已使用：{item.Name}");
            return;
        }
        Report($"{item.Name} 使用失败：{ItemUseReason(result)}");
    }

    private static string ItemUseReason(War3ItemUseCode code) => code switch
    {
        War3ItemUseCode.InvalidUnit => "单位无效",
        War3ItemUseCode.InvalidSlot => "物品栏位置为空",
        War3ItemUseCode.UnitCannotUseItems =>
            "该单位的物品栏配置禁止使用物品",
        War3ItemUseCode.PassiveItem => "这是自动生效的被动物品",
        War3ItemUseCode.Cooldown => "物品仍在冷却",
        War3ItemUseCode.InvalidTarget => "请选择有效的友军单位或主基地",
        War3ItemUseCode.OutOfRange => "目标超出施法范围",
        War3ItemUseCode.NoEffect => "当前没有可恢复的生命或魔法",
        War3ItemUseCode.PlacementBlocked => "目标位置不能放置哨塔",
        _ => "未知错误"
    };

    private int LocalShopUser(GameplayBuildingSnapshot building)
    {
        if (_simulation is null || _production is null) return -1;
        return Enumerable.Range(0, _simulation.Units.Count)
            .Where(unit => _simulation.Units.Alive[unit] &&
                           _simulation.Combat.Teams[unit] ==
                           War3HumanScenario.PlayerId &&
                           TryInventoryProfileForUnit(
                               unit, out var inventory) &&
                           inventory.CanGetItems)
            .Select(unit => new
            {
                Unit = unit,
                DistanceSquared = DistanceSquaredToBounds(
                    _simulation.Units.Positions[unit], building.Bounds)
            })
            .Where(value => value.DistanceSquared <=
                            War3ItemShopRuntime.InteractionRange *
                            War3ItemShopRuntime.InteractionRange)
            .OrderBy(value => value.DistanceSquared)
            .ThenBy(value => value.Unit)
            .Select(value => value.Unit)
            .FirstOrDefault(-1);
    }

    private int InventorySlotsForUnit(int unit)
    {
        return TryInventoryProfileForUnit(unit, out var profile)
            ? profile.Capacity
            : 0;
    }

    private void UpdateGroundItemLifecycle()
    {
        if (_simulation is null || _itemShops is null) return;
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (_simulation.Units.Alive[unit])
            {
                _inventoryAliveUnits.Add(unit);
                if (TryInventoryProfileForUnit(unit, out var profile))
                    _knownInventoryProfiles[unit] = profile;
                continue;
            }
            if (!_inventoryAliveUnits.Remove(unit)) continue;
            if (_knownInventoryProfiles.TryGetValue(unit, out var previous))
                _itemShops.DropInventoryOnDeath(
                    unit, _simulation.Units.Positions[unit],
                    previous.DropItemsOnDeath);
        }
    }

    private bool TryInventoryProfileForUnit(
        int unit,
        out War3InventoryAbilityProfile profile)
    {
        profile = null!;
        if (_simulation is null || _production is null ||
            (uint)unit >= (uint)_simulation.Units.Count ||
            !_simulation.Units.Alive[unit])
            return false;
        var definition = War3HumanContent.ResolveUnit(
            _simulation, _production, unit);
        if (!_inventoryCandidatesByObjectId.TryGetValue(
                definition.ObjectId, out var candidates))
        {
            _inventoryUnitDataByObjectId.TryGetValue(
                definition.ObjectId, out var data);
            if (!_inventoryUnitDataByObjectId.ContainsKey(definition.ObjectId))
            {
                War3HumanContent.DataCatalog.TryGet(
                    definition.ObjectId, out data);
                _inventoryUnitDataByObjectId.Add(definition.ObjectId, data);
            }
            if (data is null)
            {
                candidates = [];
            }
            else
            {
                var resolved = new List<War3InventoryAbilityProfile>();
                foreach (var rawId in data.Summary.Abilities)
                {
                    if (War3HumanContent.TryInventoryAbility(
                            rawId, out var candidate))
                        resolved.Add(candidate);
                }
                candidates = resolved.ToArray();
            }
            _inventoryCandidatesByObjectId.Add(
                definition.ObjectId, candidates);
        }
        if (candidates.Length == 0) return false;
        var playerId = _simulation.Combat.Teams[unit];
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (!InventoryRequirementsMet(candidate, playerId))
                continue;
            if (profile is null || candidate.Capacity > profile.Capacity)
                profile = candidate;
        }
        return profile is not null;
    }

    private bool InventoryRequirementsMet(
        War3InventoryAbilityProfile profile,
        int playerId)
    {
        if (_simulation is null) return false;
        foreach (var requirement in profile.Requirements)
        {
            var current = requirement.Kind switch
            {
                AbilityRequirementKind.CompletedBuilding =>
                    _simulation.Construction.CountCompleted(
                        playerId, requirement.TargetId),
                AbilityRequirementKind.TechnologyLevel =>
                    _simulation.Technology.Level(
                        playerId, requirement.TargetId),
                _ => 0
            };
            if (current < requirement.Value) return false;
        }
        return true;
    }

    private int HighestHumanTownTier()
    {
        if (_simulation is null) return 0;
        var tier = 0;
        foreach (var building in _simulation.CreateGameplayBuildingOverview())
        {
            if (building.PlayerId != War3HumanScenario.PlayerId ||
                building.State != BuildingLifecycleState.Completed)
                continue;
            if (building.Type.Id == War3HumanContent.Castle)
                return 2;
            if (building.Type.Id == War3HumanContent.Keep)
                tier = Math.Max(tier, 1);
        }
        return tier;
    }

    private static float DistanceSquaredToBounds(
        NVector2 point,
        SimRect bounds)
    {
        var x = Math.Clamp(point.X, bounds.Min.X, bounds.Max.X);
        var y = Math.Clamp(point.Y, bounds.Min.Y, bounds.Max.Y);
        return NVector2.DistanceSquared(point, new NVector2(x, y));
    }

    private static string ShopPurchaseReason(War3ShopPurchaseCode code) =>
        code switch
        {
            War3ShopPurchaseCode.NoShopUser =>
                "需要带物品栏的己方单位靠近商店",
            War3ShopPurchaseCode.CannotAcquireItems =>
                "该单位的物品栏配置禁止取得物品",
            War3ShopPurchaseCode.InventoryFull => "购买者物品栏已满",
            War3ShopPurchaseCode.RequirementMissing => "主基地等级不足",
            War3ShopPurchaseCode.OutOfStock => "商品正在补货",
            War3ShopPurchaseCode.InsufficientGold => "黄金不足",
            War3ShopPurchaseCode.InsufficientLumber => "木材不足",
            _ => "商店或商品无效"
        };

    private void SelectAt(NVector2 point, bool additive)
    {
        if (_simulation is null) return;
        var best = -1;
        var bestDistance = float.PositiveInfinity;
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit] ||
                (_simulation.Combat.Teams[unit] != War3HumanScenario.PlayerId &&
                 !_simulation.Visibility.IsUnitVisible(
                     War3HumanScenario.PlayerId, unit,
                     _simulation.Units, _simulation.Combat)))
                continue;
            var distance = NVector2.DistanceSquared(point, _simulation.Units.Positions[unit]);
            var radius = _simulation.Units.Radii[unit] + 22f;
            if (distance <= radius * radius && distance < bestDistance)
            {
                best = unit;
                bestDistance = distance;
            }
        }
        var ownedUnit = best >= 0 &&
                        _simulation.Combat.Teams[best] ==
                        War3HumanScenario.PlayerId;
        if (best >= 0)
        {
            if (!additive || !ownedUnit || SelectionContainsNonLocal() ||
                _selectedBuildings.Count > 0)
            {
                _selectedUnits.Clear();
                _selectedBuildings.Clear();
            }
            var selected = true;
            if (additive && _selectedUnits.Contains(best))
            {
                _selectedUnits.Remove(best);
                selected = false;
            }
            else
            {
                _selectedUnits.Add(best);
                _activeSelectionSubgroup = UnitSubgroupKey(best);
            }
            RefreshSelection();
            if (selected && ownedUnit) PlaySelectionAudio();
            return;
        }
        var building = _simulation.CreateGameplayBuildingOverview()
            .Where(value => !value.IsTerminal &&
                            value.Bounds.Expanded(8f).Contains(point))
            .Where(value => value.PlayerId == War3HumanScenario.PlayerId ||
                            _simulation.Visibility.IsVisible(
                                War3HumanScenario.PlayerId,
                                BuildingCenter(value.Bounds)))
            .OrderBy(value => value.Bounds.Width * value.Bounds.Height)
            .FirstOrDefault();
        if (building.Type.Name is not null)
        {
            var ownedBuilding = building.PlayerId == War3HumanScenario.PlayerId;
            if (!additive || !ownedBuilding || SelectionContainsNonLocal() ||
                _selectedUnits.Count > 0)
            {
                _selectedUnits.Clear();
                _selectedBuildings.Clear();
            }
            if (additive && _selectedBuildings.Contains(building.Id.Value))
                _selectedBuildings.Remove(building.Id.Value);
            else
            {
                _selectedBuildings.Add(building.Id.Value);
                _activeSelectionSubgroup = BuildingSubgroupKey(building.Type.Id);
            }
        }
        else if (!additive)
        {
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
        }
        RefreshSelection();
    }

    private bool SelectionContainsNonLocal()
    {
        if (_simulation is null) return false;
        if (_selectedUnits.Any(unit =>
                (uint)unit < (uint)_simulation.Units.Count &&
                _simulation.Combat.Teams[unit] != War3HumanScenario.PlayerId))
            return true;
        return _selectedBuildings.Any(value =>
        {
            var id = new GameplayBuildingId(value);
            return _simulation.Construction.IsAlive(id) &&
                   _simulation.Construction.Observe(id).PlayerId !=
                   War3HumanScenario.PlayerId;
        });
    }

    private void SelectRectangle(Vector2 from, Vector2 to, bool additive)
    {
        if (_simulation is null || _camera is null) return;
        var selectedNewUnit = false;
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
            var position = _simulation.Units.Positions[unit];
            var world = SimPlane3DTransform.ToWorld(
                position,
                GroundWorldHeight(position) + 0.8f);
            if (!_camera.IsPositionBehind(world) &&
                rect.HasPoint(_camera.UnprojectPosition(world)))
            {
                if (!_selectedUnits.Contains(unit)) selectedNewUnit = true;
                _selectedUnits.Add(unit);
            }
        }
        RefreshSelection();
        if (selectedNewUnit) PlaySelectionAudio();
    }

    private void RefreshSelection()
    {
        NormalizeActiveSelectionSubgroup();
        CancelMode();
        _presenter?.SetSelection(_selectedUnits, _selectedBuildings);
        _navigationDebugger?.SetSelection(_selectedUnits);
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

    private string UnitSubgroupKey(int unit)
    {
        if (_simulation is null || _production is null || unit < 0 ||
            unit >= _simulation.Units.Count)
            return string.Empty;
        var definition = War3HumanContent.ResolveUnit(
            _simulation, _production, unit);
        return $"unit:{definition.ObjectId}";
    }

    private static string BuildingSubgroupKey(int typeId) =>
        $"building:{typeId}";

    private SelectionSubgroup[] OrderedUnitSubgroups()
    {
        if (_simulation is null || _production is null) return [];
        var candidates = _selectedUnits
            .Where(unit => (uint)unit < (uint)_simulation.Units.Count &&
                           _simulation.Units.Alive[unit])
            .Select(unit =>
            {
                var definition = War3HumanContent.ResolveUnit(
                    _simulation, _production, unit);
                var state = _simulation.Abilities.Observe(unit);
                var caster = state.Abilities.Any(slot =>
                    slot.Level > 0 &&
                    (uint)slot.AbilityId <
                    (uint)_simulation.Abilities.Catalog.Count &&
                    !_simulation.Abilities.Catalog.Ability(slot.AbilityId)
                        .IsPassive);
                return new SelectionUnitCandidate(
                    unit,
                    $"unit:{definition.ObjectId}",
                    state.Hero ? 0 : caster ? 1 : 2,
                    state.Hero ? state.HeroLevel : 0);
            })
            .ToArray();
        return candidates
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => new SelectionSubgroup(
                group.Key,
                group.Min(value => value.Priority),
                group.Max(value => value.HeroLevel),
                group.Min(value => value.Unit),
                group.OrderByDescending(value => value.HeroLevel)
                    .ThenBy(value => value.Unit)
                    .Select(value => value.Unit)
                    .ToArray()))
            .OrderBy(value => value.Priority)
            .ThenByDescending(value => value.HeroLevel)
            .ThenBy(value => value.FirstEntity)
            .ToArray();
    }

    private SelectionSubgroup[] OrderedBuildingSubgroups()
    {
        if (_simulation is null) return [];
        return _selectedBuildings
            .Select(value => new GameplayBuildingId(value))
            .Where(_simulation.Construction.IsAlive)
            .Select(id => _simulation.ObserveGameplayBuilding(id))
            .GroupBy(value => value.Type.Id)
            .Select(group => new SelectionSubgroup(
                BuildingSubgroupKey(group.Key),
                3,
                0,
                group.Min(value => value.Id.Value),
                group.OrderBy(value => value.Id.Value)
                    .Select(value => value.Id.Value)
                    .ToArray()))
            .OrderBy(value => value.FirstEntity)
            .ToArray();
    }

    private SelectionSubgroup[] ActiveSelectionSubgroups() =>
        _selectedUnits.Count > 0
            ? OrderedUnitSubgroups()
            : OrderedBuildingSubgroups();

    private void NormalizeActiveSelectionSubgroup()
    {
        var groups = ActiveSelectionSubgroups();
        if (groups.Length == 0)
        {
            _activeSelectionSubgroup = string.Empty;
            return;
        }
        if (!groups.Any(value => value.Key.Equals(
                _activeSelectionSubgroup, StringComparison.Ordinal)))
            _activeSelectionSubgroup = groups[0].Key;
    }

    private int[] ActiveSubgroupUnits()
    {
        var group = OrderedUnitSubgroups().FirstOrDefault(value =>
            value.Key.Equals(_activeSelectionSubgroup, StringComparison.Ordinal));
        return group?.Entities ?? [];
    }

    private int ActiveSelectionUnit() =>
        ActiveSubgroupUnits().FirstOrDefault(-1);

    private int ActiveSelectionBuilding()
    {
        var group = OrderedBuildingSubgroups().FirstOrDefault(value =>
            value.Key.Equals(_activeSelectionSubgroup, StringComparison.Ordinal));
        return group?.Entities.FirstOrDefault() ??
               _selectedBuildings.Order().FirstOrDefault(-1);
    }

    private void CycleSelectionSubgroup(int direction)
    {
        var groups = ActiveSelectionSubgroups();
        if (groups.Length < 2) return;
        var current = Array.FindIndex(groups, value => value.Key.Equals(
            _activeSelectionSubgroup, StringComparison.Ordinal));
        if (current < 0) current = 0;
        current = (current + direction + groups.Length) % groups.Length;
        _activeSelectionSubgroup = groups[current].Key;
        CancelMode();
        UpdateHud();
    }

    private void SelectGroupEntry(War3SelectionGroupEntry entry)
    {
        if (!entry.SubgroupKey.Equals(
                _activeSelectionSubgroup, StringComparison.Ordinal))
        {
            _activeSelectionSubgroup = entry.SubgroupKey;
            CancelMode();
            UpdateHud();
            return;
        }
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        if (entry.Building)
            _selectedBuildings.Add(entry.EntityId);
        else
            _selectedUnits.Add(entry.EntityId);
        _activeSelectionSubgroup = entry.SubgroupKey;
        RefreshSelection();
    }

    private void UpdatePointerFeedback()
    {
        if (_simulation is null || _buildings is null || _camera is null ||
            _presenter is null)
        {
            _presenter?.HidePointerPreview();
            _presenter?.HideAbilityPointerPreview();
            _cursor?.SetMode(War3CursorMode.Normal);
            return;
        }

        var screen = GetViewport().GetMousePosition();
        if (_navigationDebugger?.BlocksWorldPointer(screen) == true)
        {
            _presenter.HidePointerPreview();
            _presenter.HideAbilityPointerPreview();
            _cursor?.SetMode(War3CursorMode.Normal);
            return;
        }

        if (!HasTargetMode() && !_dragging &&
            TryResolveEdgeCursor(screen, out var edgeMode))
        {
            _presenter.HidePointerPreview();
            _presenter.HideAbilityPointerPreview();
            _cursor?.SetMode(edgeMode);
            return;
        }

        if (_hud?.BlocksWorldPointer(screen) == true)
        {
            _presenter.HidePointerPreview();
            _presenter.HideAbilityPointerPreview();
            _cursor?.SetMode(War3CursorMode.Normal);
            return;
        }

        if (!TryGroundPoint(screen, out var point))
        {
            _presenter.HidePointerPreview();
            _presenter.HideAbilityPointerPreview();
            _cursor?.SetMode(HasTargetMode()
                ? War3CursorMode.InvalidTarget
                : War3CursorMode.Normal);
            return;
        }

        if (_pendingBuilding >= 0)
        {
            UpdateBuildingPointer(point);
            return;
        }

        if (_pendingItemSlot >= 0)
        {
            UpdateItemPointer(point);
            return;
        }

        _presenter.HidePointerPreview();
        if (_pendingAbilityId >= 0)
        {
            UpdateAbilityPointer(point);
            return;
        }

        _presenter.HideAbilityPointerPreview();
        if (_movePending)
        {
            _cursor?.SetMode(War3CursorMode.Target);
            return;
        }
        if (_attackMovePending || _rallyPending)
        {
            _cursor?.SetMode(War3CursorMode.TargetSelect);
            return;
        }
        if (_dragging)
        {
            _cursor?.SetMode(War3CursorMode.Select);
            return;
        }
        _cursor?.SetMode(ResolveSmartTarget(point).Kind ==
                         SmartCommandTargetKind.Ground
            ? War3CursorMode.Normal
            : War3CursorMode.Select);
    }

    private void UpdateBuildingPointer(NVector2 point)
    {
        if (_simulation is null || _buildings is null || _presenter is null)
            return;
        _presenter.HideAbilityPointerPreview();
        var builder = _selectedUnits.FirstOrDefault(unit =>
            _simulation.Economy.IsWorkerOwnedBy(unit, War3HumanScenario.PlayerId), -1);
        var profile = _buildings.Type(_pendingBuilding);
        var definition = War3HumanContent.Buildings[_pendingBuilding];
        var valid = builder >= 0 && _simulation.PreviewConstruction(
            War3HumanScenario.PlayerId, builder, profile, point, default, false).Succeeded;
        _presenter.SetPointerPreview(point, profile.Size, definition.ModelSource, valid);
        _cursor?.SetMode(valid
            ? War3CursorMode.HoldItem
            : War3CursorMode.InvalidTarget);
    }

    private void UpdateItemPointer(NVector2 point)
    {
        if (_simulation is null || _buildings is null || _presenter is null ||
            _itemShops is null ||
            !_itemShops.TryGetItem(
                _pendingItemUnit, _pendingItemSlot, out var item))
        {
            _presenter?.HidePointerPreview();
            _presenter?.HideAbilityPointerPreview();
            _cursor?.SetMode(War3CursorMode.InvalidTarget);
            return;
        }
        _presenter.HideAbilityPointerPreview();
        if (item.UseKind == War3ItemUseKind.IvoryTower)
        {
            var profile = _buildings.Type(War3HumanContent.ScoutTower);
            var half = profile.Size * 0.5f;
            var valid = _simulation.AssessBuildingPlacement(
                new SimRect(point - half, point + half),
                new BuildingPlacementRules(
                    profile.MinimumPassageClass,
                    profile.PlacementProfile.UnitPadding)).Succeeded;
            _presenter.SetPointerPreview(
                point,
                profile.Size,
                War3HumanContent.Buildings[War3HumanContent.ScoutTower]
                    .ModelSource,
                valid);
            _cursor?.SetMode(valid
                ? War3CursorMode.HoldItem
                : War3CursorMode.InvalidTarget);
            return;
        }

        _presenter.HidePointerPreview();
        var smart = ResolveSmartTarget(point);
        var validTarget = item.UseKind switch
        {
            War3ItemUseKind.TownPortal =>
                IsValidTownPortalTarget(_pendingItemUnit, smart),
            War3ItemUseKind.SanctuaryStaff =>
                IsValidSanctuaryTarget(_pendingItemUnit, smart, item),
            _ => false
        };
        _cursor?.SetMode(validTarget
            ? War3CursorMode.TargetSelect
            : War3CursorMode.InvalidTarget);
    }

    private bool IsValidTownPortalTarget(
        int caster,
        in SmartCommandTarget target)
    {
        if (_simulation is null ||
            target.Kind != SmartCommandTargetKind.FriendlyBuilding)
            return false;
        var id = new GameplayBuildingId(target.Building);
        if (!_simulation.Construction.IsAlive(id)) return false;
        var building = _simulation.Construction.Observe(id);
        return building.State == BuildingLifecycleState.Completed &&
               building.Type.Function == BuildingFunctionKind.TownHall &&
               _simulation.Diplomacy.IsFriendly(
                   _simulation.Combat.Teams[caster], building.PlayerId);
    }

    private bool IsValidSanctuaryTarget(
        int caster,
        in SmartCommandTarget target,
        War3ShopItemDefinition item)
    {
        if (_simulation is null ||
            target.Kind != SmartCommandTargetKind.FriendlyUnit ||
            (uint)target.Unit >= (uint)_simulation.Units.Count ||
            !_simulation.Units.Alive[target.Unit] ||
            !TryHighestTownHall(
                _simulation.Combat.Teams[caster], out _))
            return false;
        var range = item.Range * War3ItemEffectRuntime.WorldDistanceScale;
        return NVector2.DistanceSquared(
                   _simulation.Units.Positions[caster],
                   _simulation.Units.Positions[target.Unit]) <=
               range * range;
    }

    private void UpdateAbilityPointer(NVector2 point)
    {
        if (_simulation is null || _presenter is null ||
            (uint)_pendingAbilityId >= (uint)_simulation.Abilities.Catalog.Count)
        {
            _presenter?.HideAbilityPointerPreview();
            _cursor?.SetMode(War3CursorMode.InvalidTarget);
            return;
        }

        var ability = _simulation.Abilities.Catalog.Ability(_pendingAbilityId);
        if (_pendingAbilityBuilding >= 0)
        {
            var buildingId = new GameplayBuildingId(_pendingAbilityBuilding);
            if (!_simulation.Construction.IsAlive(buildingId))
            {
                _presenter.HideAbilityPointerPreview();
                _cursor?.SetMode(War3CursorMode.InvalidTarget);
                return;
            }
            var building = _simulation.Construction.Observe(buildingId);
            var buildingLevel = ability.Levels[0];
            var buildingTarget = AbilityCastTarget.Point(point);
            var buildingPreview = _simulation.PreviewBuildingAbility(
                War3HumanScenario.PlayerId, buildingId,
                _pendingAbilityId, buildingTarget);
            var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
            var extent = building.Bounds.Max - building.Bounds.Min;
            var buildingCastRange = buildingLevel.Range <= 0f
                ? 0f
                : buildingLevel.Range +
                  MathF.Max(extent.X, extent.Y) * 0.5f;
            _presenter.SetAbilityPointerPreview(
                point, center, buildingCastRange,
                AbilityPointerRadius(buildingLevel.Area, default),
                buildingPreview.Succeeded);
            _cursor?.SetMode(buildingPreview.Succeeded
                ? War3CursorMode.TargetSelect
                : War3CursorMode.InvalidTarget);
            return;
        }
        if (_pendingAbilityCaster < 0 ||
            !_simulation.Units.Alive[_pendingAbilityCaster])
        {
            _presenter.HideAbilityPointerPreview();
            _cursor?.SetMode(War3CursorMode.InvalidTarget);
            return;
        }
        var state = _simulation.Abilities.Observe(_pendingAbilityCaster);
        var slot = state.Abilities.FirstOrDefault(value =>
            value.AbilityId == _pendingAbilityId);
        if (slot.Level <= 0 || slot.Level > ability.Levels.Length)
        {
            _presenter.HideAbilityPointerPreview();
            _cursor?.SetMode(War3CursorMode.InvalidTarget);
            return;
        }

        var level = ability.Levels[slot.Level - 1];
        var smart = ResolveSmartTarget(point);
        var targetPosition = point;
        AbilityCastTarget target;
        var unitTarget = ability.Activation is AbilityActivationKind.TargetUnit or
            AbilityActivationKind.ChannelUnit;
        if (unitTarget)
        {
            target = smart.Kind switch
            {
                SmartCommandTargetKind.FriendlyUnit or
                    SmartCommandTargetKind.EnemyUnit =>
                    AbilityCastTarget.Unit(smart.Unit, smart.Position),
                SmartCommandTargetKind.FriendlyBuilding or
                    SmartCommandTargetKind.EnemyBuilding =>
                    AbilityCastTarget.Building(
                        new GameplayBuildingId(smart.Building), smart.Position),
                _ => default
            };
            if (target.Kind != AbilityTargetKind.None)
                targetPosition = target.Position;
        }
        else
        {
            target = AbilityCastTarget.Point(point);
        }

        var preview = target.Kind == AbilityTargetKind.None
            ? new AbilityCommandResult(
                AbilityCommandCode.InvalidTarget,
                _pendingAbilityCaster,
                _pendingAbilityId)
            : _simulation.PreviewAbility(
                War3HumanScenario.PlayerId,
                _pendingAbilityCaster,
                _pendingAbilityId,
                target);
        var targetRadius = AbilityPointerRadius(
            level.Area, unitTarget ? smart : default);
        var castRange = level.Range <= 0f
            ? 0f
            : level.Range + _simulation.Units.Radii[_pendingAbilityCaster];
        _presenter.SetAbilityPointerPreview(
            targetPosition,
            _simulation.Units.Positions[_pendingAbilityCaster],
            castRange,
            targetRadius,
            preview.Succeeded);
        _cursor?.SetMode(preview.Succeeded
            ? War3CursorMode.TargetSelect
            : War3CursorMode.InvalidTarget);
    }

    private float AbilityPointerRadius(
        float area,
        in SmartCommandTarget target)
    {
        if (area > 0f) return area;
        if (_simulation is null) return 16f;
        if ((target.Kind is SmartCommandTargetKind.FriendlyUnit or
             SmartCommandTargetKind.EnemyUnit) &&
            (uint)target.Unit < (uint)_simulation.Units.Count)
            return _simulation.Units.Radii[target.Unit] + 4f;
        if (target.Kind is SmartCommandTargetKind.FriendlyBuilding or
            SmartCommandTargetKind.EnemyBuilding)
        {
            var id = new GameplayBuildingId(target.Building);
            if (_simulation.Construction.IsAlive(id))
            {
                var bounds = _simulation.Construction.Observe(id).Bounds;
                return MathF.Min(bounds.Width, bounds.Height) * 0.5f;
            }
        }
        return 16f;
    }

    private bool TryResolveEdgeCursor(
        Vector2 screen,
        out War3CursorMode mode)
    {
        mode = War3CursorMode.Normal;
        var size = GetViewport().GetVisibleRect().Size;
        if (size.X <= 0f || size.Y <= 0f ||
            screen.X < 0f || screen.Y < 0f ||
            screen.X > size.X || screen.Y > size.Y)
            return false;
        var left = screen.X <= CursorEdgePixels;
        var right = screen.X >= size.X - CursorEdgePixels;
        var up = screen.Y <= CursorEdgePixels;
        var down = screen.Y >= size.Y - CursorEdgePixels;
        mode = (left, right, up, down) switch
        {
            (true, _, true, _) => War3CursorMode.ScrollUpLeft,
            (_, true, true, _) => War3CursorMode.ScrollUpRight,
            (true, _, _, true) => War3CursorMode.ScrollDownLeft,
            (_, true, _, true) => War3CursorMode.ScrollDownRight,
            (true, _, _, _) => War3CursorMode.ScrollLeft,
            (_, true, _, _) => War3CursorMode.ScrollRight,
            (_, _, true, _) => War3CursorMode.ScrollUp,
            (_, _, _, true) => War3CursorMode.ScrollDown,
            _ => War3CursorMode.Normal
        };
        return left || right || up || down;
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
            _status)
        {
            CameraViewBounds = CreateCameraViewBounds(),
            MinimapSignalMode = false
        });
    }

    private War3SelectionSnapshot CreateSelectionSnapshot()
    {
        if (_simulation is null || _production is null)
            return War3SelectionSnapshot.Empty;
        if (_selectedUnits.Count > 0)
        {
            var living = OrderedUnitSubgroups()
                .SelectMany(value => value.Entities)
                .ToArray();
            if (living.Length == 0) return War3SelectionSnapshot.Empty;
            var first = ActiveSelectionUnit();
            if (first < 0) first = living[0];
            var definition = War3HumanContent.ResolveUnit(
                _simulation, _production, first);
            var health = _simulation.Combat.Health[first];
            var maximum = _simulation.Combat.MaximumHealth[first];
            var homogeneous = living.All(unit => War3HumanContent.ResolveUnit(
                _simulation, _production, unit).TypeId == definition.TypeId);
            var activeGroupCount = ActiveSubgroupUnits().Length;
            var attack = _simulation.Combat.AttackDamage[first];
            var weapons = _simulation.Combat.WeaponProfiles[first];
            var activeWeaponSlot = _simulation.Combat.ActiveWeaponSlots[first];
            var activeWeapon = weapons.FirstOrDefault(value =>
                value.Slot == activeWeaponSlot);
            var weaponTechnologyId =
                _simulation.Combat.DamageUpgradeTechnologyIds[first];
            if (weaponTechnologyId < 0)
                weaponTechnologyId =
                    _simulation.CombatWeaponUpgradeTechnologyId;
            var weaponLevel = weaponTechnologyId < 0
                ? 0
                : _simulation.Technology.Level(
                    _simulation.Combat.Teams[first], weaponTechnologyId);
            var abilityState = _simulation.Abilities.Observe(first);
            var abilityStatus = AbilityStatusLabel(first, abilityState);
            var heroProgress = abilityState.Hero
                ? $"经验 {abilityState.HeroExperience}/" +
                  $"{abilityState.ExperienceForNextLevel} · " +
                  $"技能点 {abilityState.UnspentSkillPoints}"
                : string.Empty;
            var attackClass = definition.AttackClass.Length > 0
                ? definition.AttackClass
                : attack <= 0f
                    ? "无攻击"
                    : _simulation.Combat.AttackRanges[first] > 45f
                        ? "远程"
                        : "近战";
            var inventorySlots = InventorySlotsForUnit(first);
            var team = _simulation.Combat.Teams[first];
            var controllable = team == War3HumanScenario.PlayerId;
            return new War3SelectionSnapshot(
                living.Length > 1
                    ? $"{definition.Name} × {Math.Max(1, activeGroupCount)}"
                    : SelectionPrefix(team) + definition.Name,
                homogeneous
                    ? string.Join(" · ", new[]
                    {
                        definition.Role,
                        definition.AbilitySummary,
                        abilityStatus,
                        heroProgress
                    }.Where(value => value.Length > 0))
                    : $"混合编队 · {living.Length} 个单位",
                health, maximum,
                definition.PortraitSource,
                !definition.PortraitSource.Equals(
                    definition.ModelSource, StringComparison.OrdinalIgnoreCase),
                definition.IconPath,
                living.Length,
                0f,
                string.Empty,
                attack,
                _simulation.EffectiveUnitArmor(first),
                abilityState.Hero ? abilityState.HeroLevel : definition.Level,
                weaponLevel,
                attackClass,
                definition.ArmorClass.Length > 0
                    ? definition.ArmorClass
                    : ArmorClass(_simulation.Combat.Attributes[first]),
                false)
            {
                Mana = abilityState.Mana,
                MaximumMana = abilityState.MaximumMana,
                ManaRegeneration = abilityState.ManaRegeneration,
                HeroExperience = abilityState.HeroExperience,
                ExperienceForNextLevel =
                    abilityState.ExperienceForNextLevel,
                UnspentSkillPoints = abilityState.UnspentSkillPoints,
                AbilityStatuses = abilityState.Statuses,
                Buffs = _simulation.Abilities.ObserveBuffs(first),
                ActiveWeaponSlot = activeWeaponSlot,
                WeaponCount = weapons.Length,
                WeaponTargetLabel = WeaponTargetLabel(activeWeapon.TargetLayers),
                AttackIconPath = attack > 0f
                    ? Btn(_simulation.Combat.AttackRanges[first] > 45f
                        ? "SteelRanged"
                        : "SteelMelee")
                    : string.Empty,
                ArmorIconPath = Btn("HumanArmorUpOne"),
                IsHero = abilityState.Hero,
                SupportsInventory = inventorySlots > 0,
                InventorySlotCount = inventorySlots,
                InventoryItems = _itemShops?.InventorySnapshot(
                    first,
                    TryInventoryProfileForUnit(
                        first, out var inventoryProfile) &&
                    inventoryProfile.CanUseItems) ?? [],
                GroupEntries = CreateSelectionGroupEntries(),
                PortraitTeam = team,
                PortraitAnimated = controllable,
                Controllable = controllable
            };
        }
        if (_selectedBuildings.Count > 0)
        {
            var activeGroup = OrderedBuildingSubgroups().FirstOrDefault(value =>
                value.Key.Equals(_activeSelectionSubgroup, StringComparison.Ordinal));
            var activeId = activeGroup?.Entities.FirstOrDefault() ??
                           _selectedBuildings.Order().First();
            var id = new GameplayBuildingId(activeId);
            if (!_simulation.Construction.IsAlive(id)) return War3SelectionSnapshot.Empty;
            var building = _simulation.ObserveGameplayBuilding(id);
            var definition = War3HumanContent.Buildings[building.Type.Id];
            var queueItems = CreateQueueItems(id);
            var queueLabel = queueItems.Length == 0
                ? string.Empty
                : queueItems[0].StateLabel + "：" + queueItems[0].Label;
            var progress = queueItems.Length == 0 ? 0f : queueItems[0].Progress;
            var rawAttack = RawBuildingAttack(definition.ObjectId);
            var controllable = building.PlayerId == War3HumanScenario.PlayerId;
            var shopUser = controllable &&
                           definition.TypeId == War3HumanContent.ArcaneVault
                ? LocalShopUser(building)
                : -1;
            return new War3SelectionSnapshot(
                SelectionPrefix(building.PlayerId) + definition.Name,
                _simulation.BuildingUpgrades.TryObserve(
                    id, out var activeUpgrade)
                    ? $"升级为{activeUpgrade.Profile.TargetType.Name} · " +
                      $"{activeUpgrade.Progress:P0}"
                    : building.State == BuildingLifecycleState.Completed
                    ? definition.Role
                    : $"建造中 · {building.Progress:P0}",
                building.Health,
                building.MaximumHealth,
                definition.ModelSource,
                false,
                definition.IconPath,
                1,
                progress,
                queueLabel,
                rawAttack.Damage,
                building.EffectiveArmor,
                1,
                0,
                rawAttack.Class,
                definition.ArmorClass.Length > 0
                    ? definition.ArmorClass
                    : ArmorClass(building.Type.Attributes),
                true)
            {
                QueueItems = queueItems,
                AttackIconPath = rawAttack.Damage > 0f
                    ? Btn(rawAttack.Ranged ? "SteelRanged" : "SteelMelee")
                    : string.Empty,
                ArmorIconPath = Btn("ImbuedMasonry"),
                IsHero = false,
                SupportsInventory = false,
                InventorySlotCount = 0,
                GroupEntries = CreateSelectionGroupEntries(),
                IsConstructing = building.State !=
                                 BuildingLifecycleState.Completed,
                ConstructionProgress = building.Progress,
                PortraitTeam = building.PlayerId,
                PortraitAnimated = controllable,
                PortraitAnimationProperties = definition.AnimationProperties,
                Controllable = controllable,
                IsShop = definition.TypeId == War3HumanContent.ArcaneVault,
                ShopUserUnit = shopUser
            };
        }
        return War3SelectionSnapshot.Empty;
    }

    private War3SelectionGroupEntry[] CreateSelectionGroupEntries()
    {
        if (_simulation is null || _production is null) return [];
        var result = new List<War3SelectionGroupEntry>(
            _selectedUnits.Count + _selectedBuildings.Count);
        if (_selectedUnits.Count > 0)
        {
            foreach (var group in OrderedUnitSubgroups())
            foreach (var unit in group.Entities)
            {
                var definition = War3HumanContent.ResolveUnit(
                    _simulation, _production, unit);
                var state = _simulation.Abilities.Observe(unit);
                var maximumHealth = _simulation.Combat.MaximumHealth[unit];
                result.Add(new War3SelectionGroupEntry(
                    unit,
                    false,
                    definition.TypeId,
                    group.Key,
                    definition.Name,
                    definition.IconPath,
                    maximumHealth > 0f
                        ? _simulation.Combat.Health[unit] / maximumHealth
                        : 0f,
                    state.MaximumMana > 0f
                        ? state.Mana / state.MaximumMana
                        : 0f,
                    group.Key.Equals(
                        _activeSelectionSubgroup, StringComparison.Ordinal),
                    _simulation.Abilities.ObserveBuffs(unit)
                        .Any(value => !value.Beneficial),
                    state.Hero ? state.HeroLevel : 0));
            }
            return result.ToArray();
        }

        foreach (var group in OrderedBuildingSubgroups())
        foreach (var value in group.Entities)
        {
            var building = _simulation.ObserveGameplayBuilding(
                new GameplayBuildingId(value));
            var definition = War3HumanContent.Buildings[building.Type.Id];
            result.Add(new War3SelectionGroupEntry(
                value,
                true,
                building.Type.Id,
                group.Key,
                definition.Name,
                definition.IconPath,
                building.MaximumHealth > 0f
                    ? building.Health / building.MaximumHealth
                    : 0f,
                0f,
                group.Key.Equals(
                    _activeSelectionSubgroup, StringComparison.Ordinal),
                false));
        }
        return result.ToArray();
    }

    private static string SelectionPrefix(int playerId) => playerId switch
    {
        War3HumanScenario.PlayerId => string.Empty,
        War3HumanScenario.EnemyId => "敌方 · ",
        _ => "中立 · "
    };

    private static (float Damage, string Class, bool Ranged) RawBuildingAttack(
        string objectId)
    {
        if (!War3HumanContent.DataCatalog.TryGet(objectId, out var data))
            return (0f, "无攻击", false);
        var attack = data.Summary.Combat.Attacks.FirstOrDefault(value =>
            value.Enabled);
        if (attack is null) return (0f, "无攻击", false);
        var damage = attack.Damage.Average ?? 0f;
        var attackClass = attack.AttackType?.ToLowerInvariant() switch
        {
            "pierce" => "穿刺",
            "siege" => "攻城",
            "magic" => "魔法",
            "chaos" => "混乱",
            "hero" => "英雄",
            _ => "普通"
        };
        return (damage, attackClass, (attack.Range ?? 0f) > 90f);
    }

    private SimRect CreateCameraViewBounds()
    {
        if (_simulation is null || _camera is null)
            return War3HudSnapshot.Empty.CameraViewBounds;
        var world = _simulation.World.Bounds;
        var viewport = GetViewport().GetVisibleRect().Size;
        var consoleScale = Math.Min(1f, viewport.X / 1000f);
        var worldBottom = MathF.Max(1f,
            viewport.Y - War3RtsHud.ConsoleHeight * consoleScale);
        Span<Vector2> screens =
        [
            new Vector2(0f, 0f),
            new Vector2(viewport.X, 0f),
            new Vector2(0f, worldBottom),
            new Vector2(viewport.X, worldBottom)
        ];
        Span<NVector2> points = stackalloc NVector2[4];
        var count = 0;
        foreach (var screen in screens)
        {
            if (!TryCameraGroundPoint(screen, world, out var point)) continue;
            points[count++] = point;
        }
        if (count == 0)
        {
            var center = _cameraController?.Target ??
                         (world.Min + world.Max) * 0.5f;
            var half = new NVector2(
                MathF.Min(world.Width, 320f),
                MathF.Min(world.Height, 240f));
            return new SimRect(
                world.Clamp(center - half),
                world.Clamp(center + half));
        }
        var minimum = points[0];
        var maximum = points[0];
        for (var index = 1; index < count; index++)
        {
            minimum = NVector2.Min(minimum, points[index]);
            maximum = NVector2.Max(maximum, points[index]);
        }
        return new SimRect(world.Clamp(minimum), world.Clamp(maximum));
    }

    private bool TryCameraGroundPoint(
        Vector2 screen,
        in SimRect world,
        out NVector2 point)
    {
        if (TryGroundPoint(screen, out point)) return true;
        point = default;
        if (_camera is null) return false;
        var origin = _camera.ProjectRayOrigin(screen);
        var direction = _camera.ProjectRayNormal(screen);
        if (direction.Y >= -0.0001f) return false;
        var target = _cameraController?.Target ??
                     (world.Min + world.Max) * 0.5f;
        var distance = (GroundWorldHeight(target) - origin.Y) / direction.Y;
        if (distance < 0f) return false;
        point = world.Clamp(SimPlane3DTransform.ToSimulation(
            origin + direction * distance));
        return true;
    }

    private static string WeaponTargetLabel(CombatTargetLayer layers)
    {
        if (layers == CombatTargetLayer.None) return string.Empty;
        if (layers == CombatTargetLayer.All) return "全目标";
        var labels = new List<string>(3);
        if ((layers & CombatTargetLayer.GroundUnit) != 0) labels.Add("对地");
        if ((layers & CombatTargetLayer.AirUnit) != 0) labels.Add("对空");
        if ((layers & CombatTargetLayer.Building) != 0) labels.Add("对建筑");
        return string.Join('、', labels);
    }

    private War3QueueItemSnapshot[] CreateQueueItems(GameplayBuildingId building)
    {
        if (_simulation is null) return [];
        if (_simulation.BuildingUpgrades.TryObserve(
                building, out var activeUpgrade))
        {
            var definition = War3HumanContent.Buildings[
                activeUpgrade.Profile.TargetType.Id];
            return
            [
                new War3QueueItemSnapshot(
                    War3QueueItemKind.BuildingUpgrade,
                    activeUpgrade.Id.Value,
                    activeUpgrade.Profile.Id,
                    definition.Name,
                    definition.IconPath,
                    activeUpgrade.Progress,
                    "升级中",
                    $"升级中：{definition.Name}\n点击取消并返还 " +
                    $"{Refund(activeUpgrade.Profile.Cost.Minerals, activeUpgrade.Profile.CancelRefundFraction)} 黄金 / " +
                    $"{Refund(activeUpgrade.Profile.Cost.VespeneGas, activeUpgrade.Profile.CancelRefundFraction)} 木材")
            ];
        }
        var production = _simulation.Production.Observe(building).Orders;
        if (production.Length > 0)
        {
            return production.Select(order =>
            {
                var definition = War3HumanContent.Units[order.Recipe.UnitType.Id];
                var state = order.State switch
                {
                    ProductionOrderState.WaitingForExit => "等待出口",
                    ProductionOrderState.Producing => "训练中",
                    _ => "等待训练"
                };
                return new War3QueueItemSnapshot(
                    War3QueueItemKind.Production,
                    order.Id.Value,
                    order.Recipe.Id,
                    definition.Name,
                    definition.IconPath,
                    order.Progress,
                    state,
                    $"{state}：{definition.Name}\n点击取消并返还 " +
                    $"{Refund(order.Recipe.Cost.Minerals,
                        order.Recipe.CancelRefundFraction)} 黄金 / " +
                    $"{Refund(order.Recipe.Cost.VespeneGas,
                        order.Recipe.CancelRefundFraction)} 木材");
            }).ToArray();
        }

        var research = _simulation.Technology.Observe(building).Orders;
        return research.Select(order =>
        {
            var definition = War3HumanContent.Technologies[order.Technology.Id];
            var currentLevel = _simulation.Technology.Level(
                order.PlayerId, order.Technology.Id);
            var label = definition.NameForLevel(currentLevel);
            return new War3QueueItemSnapshot(
                War3QueueItemKind.Research,
                order.Id.Value,
                order.Technology.Id,
                label,
                definition.IconPathForLevel(currentLevel),
                order.Progress,
                "研究中",
                $"研究中：{label}\n点击取消并返还 " +
                $"{Refund(order.Technology.Cost.Minerals,
                    order.Technology.CancelRefundFraction)} 黄金 / " +
                $"{Refund(order.Technology.Cost.VespeneGas,
                    order.Technology.CancelRefundFraction)} 木材");
        }).ToArray();
    }

    private static int Refund(int value, float fraction) =>
        (int)MathF.Round(value * fraction, MidpointRounding.AwayFromZero);

    private static string ArmorClass(CombatAttribute attributes)
    {
        if ((attributes & CombatAttribute.Structure) != 0) return "建筑";
        if ((attributes & CombatAttribute.Armored) != 0) return "重甲";
        if ((attributes & CombatAttribute.Light) != 0) return "轻甲";
        if ((attributes & CombatAttribute.Mechanical) != 0) return "机械";
        return "普通";
    }

    private string AbilityStatusLabel(
        int unit,
        UnitAbilitySnapshot snapshot)
    {
        if (_simulation is null) return string.Empty;
        var values = new List<string>();
        if (snapshot.MaximumMana > 0f)
            values.Add(
                $"法力 {snapshot.Mana:0}/{snapshot.MaximumMana:0} " +
                $"(+{snapshot.ManaRegeneration:0.##}/秒)");
        if (snapshot.CastPhase != AbilityCastPhase.None &&
            snapshot.ActiveAbilityId >= 0)
            values.Add($"施法：{_simulation.Abilities.Catalog
                .Ability(snapshot.ActiveAbilityId).Name}");
        if ((snapshot.Statuses & AbilityStatusFlags.Stunned) != 0) values.Add("眩晕");
        if ((snapshot.Statuses & AbilityStatusFlags.Invulnerable) != 0) values.Add("无敌");
        if ((snapshot.Statuses & AbilityStatusFlags.MagicImmune) != 0) values.Add("魔免");
        if ((snapshot.Statuses & AbilityStatusFlags.Invisible) != 0) values.Add("隐形");
        if ((snapshot.Statuses & AbilityStatusFlags.Polymorphed) != 0) values.Add("变形");
        if ((snapshot.Statuses & AbilityStatusFlags.Banished) != 0) values.Add("放逐");
        var buffs = _simulation.Abilities.ObserveBuffs(unit)
            .Select(value =>
            {
                var name = _simulation.Abilities.Catalog
                    .Ability(value.AbilityId).Name;
                return float.IsPositiveInfinity(value.RemainingSeconds)
                    ? name
                    : $"{name} {value.RemainingSeconds:0.#}秒";
            })
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
        if (buffs.Length > 0) values.Add($"效果 {string.Join("/", buffs)}");
        return string.Join(" · ", values);
    }

    private static void ValidateHumanAssets()
    {
        var missing = new List<string>();
        var optionalMissing = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var warmedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warmupStart = System.Diagnostics.Stopwatch.GetTimestamp();
        void Model(string path)
        {
            if (!War3RuntimeAssets.Contains(path))
            {
                missing.Add($"模型:{path}");
                return;
            }
            if (warmedModels.Add(path))
                War3RuntimeAssets.PreloadModelTemplate(path);
        }
        void Texture(string path)
        {
            if (War3RuntimeAssets.LoadTexture(path) is null) missing.Add($"贴图:{path}");
        }
        void OptionalModel(string path)
        {
            if (path.Length == 0) return;
            if (!War3RuntimeAssets.Contains(path))
            {
                optionalMissing.Add(path);
                return;
            }
            if (warmedModels.Add(path))
                War3RuntimeAssets.PreloadModelTemplate(path);
        }
        static War3Sequence? ResolveSequence(
            IReadOnlyList<War3Sequence> sequences,
            IReadOnlyList<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                var exact = sequences.FirstOrDefault(value => value.Name.Equals(
                    candidate, StringComparison.OrdinalIgnoreCase));
                if (exact is not null) return exact;
                var prefix = sequences.FirstOrDefault(value => value.Name.StartsWith(
                    candidate, StringComparison.OrdinalIgnoreCase));
                if (prefix is not null) return prefix;
            }
            return null;
        }

        foreach (var unit in War3HumanContent.Units)
        {
            Model(unit.ModelSource);
            Model(unit.PortraitSource);
            Texture(unit.IconPath);
            if (unit.ProjectileSource.Length > 0) Model(unit.ProjectileSource);
            if (unit.ImpactSource.Length > 0) Model(unit.ImpactSource);
            if (unit.SpecialEffectSource.Length > 0) Model(unit.SpecialEffectSource);
        }
        foreach (var building in War3HumanContent.Buildings)
        {
            Model(building.ModelSource);
            Texture(building.IconPath);
            if (building.SpecialEffectSource.Length > 0)
                Model(building.SpecialEffectSource);
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
            if (building.AnimationProperties.Length == 0) continue;
            foreach (var (phase, candidates, nonLooping) in new[]
                     {
                         ("升级", War3AnimationPropertyResolver.UpgradeBirth(
                             building.AnimationProperties), true),
                         ("待机", War3AnimationPropertyResolver.Stand(
                             building.AnimationProperties), false),
                         ("肖像", War3AnimationPropertyResolver.Portrait(
                             building.AnimationProperties), false)
                     })
            {
                // Stand/Portrait must resolve the exact configured variant;
                // accepting their generic fallback would silently display the
                // scout tower again. Tower upgrade birth is one combined MDX
                // sequence, so it may legitimately use the ordered fallbacks.
                var validationCandidates = phase == "升级"
                    ? candidates
                    : [candidates[0]];
                var resolved = ResolveSequence(
                    metadata.Sequences, validationCandidates);
                if (resolved is null || resolved.NonLooping != nonLooping)
                    missing.Add(
                        $"建筑{phase}动画:{building.ObjectId}:" +
                        string.Join('|', candidates));
            }
        }
        foreach (var ability in War3HumanContent.Abilities)
        {
            if (ability.IconPath.Length > 0) Texture(ability.IconPath);
            if (ability.AlternateIconPath.Length > 0)
                Texture(ability.AlternateIconPath);
            foreach (var model in ability.CasterModels
                         .Concat(ability.TargetModels)
                         .Concat(ability.EffectModels)
                         .Concat(ability.MissileModels))
                OptionalModel(model);
        }
        foreach (var item in War3ItemShopRuntime.ArcaneVaultItems)
            Texture(item.IconPath);
        if (!War3HumanContent.DataCatalog.TryGet("hvlt", out var vaultData) ||
            !vaultData.Summary.MakesItems.Order(StringComparer.Ordinal)
                .SequenceEqual(
                    War3ItemShopRuntime.ArcaneVaultItems
                        .Select(value => value.ItemId)
                        .Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
            missing.Add("商店数据:hvlt.Makeitems");
        Model(War3HumanContent.GoldMineSource);
        Model(@"UI\Feedback\RallyPoint\RallyPoint.mdx");
        Model(War3CommandFeedbackCatalog.ConfirmationSource);
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
                     @"UI\Widgets\Console\Human\human-unitqueue-border.blp",
                     @"UI\Feedback\BuildProgressBar\human-buildprogressbar-fill.blp",
                     @"UI\Feedback\BuildProgressBar\human-buildprogressbar-border.blp",
                     Btn("Move"), Btn("Attack"), Btn("Stop"), Btn("HoldPosition"),
                     Btn("BasicStruct"), Btn("RallyPoint"), Btn("SteelMelee"),
                     Btn("Cancel")
                 })
            Texture(path);

        if (missing.Count > 0)
            throw new FileNotFoundException(
                "war3_rts 人族资源不完整：" + string.Join("; ", missing));
        var warmupMilliseconds =
            (System.Diagnostics.Stopwatch.GetTimestamp() - warmupStart) *
            1_000d / System.Diagnostics.Stopwatch.Frequency;
        GD.Print(
            $"WAR3_MODEL_PREWARM templates={warmedModels.Count} " +
            $"elapsed_ms={warmupMilliseconds:0.###}");
        if (optionalMissing.Count > 0)
            GD.PushWarning(
                $"WAR3_ABILITY_ART missing_optional={optionalMissing.Count} " +
                string.Join(';', optionalMissing.Take(12)));
    }

    private War3CommandSnapshot[] CreateCommands()
    {
        if (_buildMenu)
        {
            var commands = War3HumanContent.Buildings
                .Where(definition => definition.Constructible)
                .Select((definition, index) =>
                new War3CommandSnapshot(
                    UnitObjectSlot(definition.ObjectId, index),
                    War3CommandKind.Build, definition.TypeId,
                    definition.Name,
                    $"建造 {definition.Name} · {definition.Role}",
                    definition.IconPath,
                    UnitObjectHotkey(definition.ObjectId, Hotkey(index))))
                .ToList();
            commands.Add(Command(11, War3CommandKind.Cancel, -1, "返回", "取消建造菜单",
                Btn("Cancel"), "ESC"));
            return commands.ToArray();
        }
        if (_selectedUnits.Count > 0)
        {
            if (_simulation is null)
                return [];
            var active = ActiveSelectionUnit();
            if (active < 0 ||
                _simulation.Combat.Teams[active] != War3HumanScenario.PlayerId)
                return [];
            var commands = new List<War3CommandSnapshot>
            {
                Command(0, War3CommandKind.Move, -1, "移动", "移动到目标位置", Btn("Move"), "M"),
                Command(4, War3CommandKind.AttackMove, -1, "攻击移动", "沿途攻击敌人", Btn("Attack"), "A"),
                Command(1, War3CommandKind.Stop, -1, "停止", "停止当前命令", Btn("Stop"), "S"),
                Command(2, War3CommandKind.Hold, -1, "保持位置", "不再追击远处敌人", Btn("HoldPosition"), "H")
            };
            if (_simulation is not null)
            {
                var caster = ActiveSubgroupUnits().FirstOrDefault(unit =>
                    _simulation.Units.Alive[unit] &&
                    _simulation.Combat.Teams[unit] ==
                    War3HumanScenario.PlayerId, -1);
                if (caster >= 0)
                {
                    var state = _simulation.Abilities.Observe(caster);
                    if (_learnMenu && state.Hero)
                        return CreateLearnCommands(state).ToArray();
                    var fallbackSlot = 8;
                    foreach (var learned in state.Abilities)
                    {
                        var ability = _simulation.Abilities.Catalog.Ability(
                            learned.AbilityId);
                        if (learned.Level <= 0) continue;
                        var level = ability.Levels[learned.Level - 1];
                        var ready = learned.CooldownRemaining <= 0.001f &&
                                    state.Mana + 0.001f >= level.ManaCost &&
                                    _simulation.Abilities.CanCast(caster) &&
                                    !ability.IsPassive;
                        var range = level.Range > 0f
                            ? $" · 距离 {level.Range:0}"
                            : string.Empty;
                        var cooldown = learned.CooldownRemaining > 0f
                            ? $"\n剩余冷却 {learned.CooldownRemaining:0.0} 秒"
                            : string.Empty;
                        var formEffect = level.Effects.FirstOrDefault(value =>
                            value.Kind == AbilityEffectKind.TransformUnit);
                        var alternateForm = formEffect.Kind ==
                                                AbilityEffectKind.TransformUnit &&
                                            state.UnitTypeId ==
                                                formEffect.UnitForm.Alternate.Id;
                        War3HumanContent.TryAbility(
                            ability.RawId, out var presentation);
                        var autoCastAvailable =
                            presentation?.SupportsAutoCast == true;
                        var commandToggled = learned.Toggled ||
                            autoCastAvailable && learned.AutoCastEnabled;
                        var alternatePresentation = learned.Toggled ||
                            autoCastAvailable && !learned.AutoCastEnabled;
                        var label = (alternateForm || alternatePresentation) &&
                                    !string.IsNullOrWhiteSpace(
                                        presentation?.AlternateName)
                            ? presentation.AlternateName
                            : ability.Name;
                        var description = (alternateForm ||
                                           alternatePresentation) &&
                                          !string.IsNullOrWhiteSpace(
                                              presentation?.AlternateDescription)
                            ? presentation.AlternateDescription
                            : ability.Description;
                        var icon = (alternateForm || alternatePresentation) &&
                                   !string.IsNullOrWhiteSpace(
                                       presentation?.AlternateIconPath)
                            ? presentation.AlternateIconPath
                            : ability.IconPath;
                        var hotkey = (alternateForm || alternatePresentation) &&
                                     !string.IsNullOrWhiteSpace(
                                         presentation?.AlternateHotkey)
                            ? presentation.AlternateHotkey
                            : ability.Hotkey;
                        var slot = AbilityObjectSlot(
                            ability.RawId, learned.Toggled, fallbackSlot++);
                        commands.Add(new War3CommandSnapshot(
                            slot,
                            War3CommandKind.Ability,
                            ability.Id,
                            label,
                            $"{description}\n等级 {learned.Level} · " +
                            $"法力 {level.ManaCost:0} · 冷却 " +
                            $"{level.CooldownSeconds:0.#} 秒{range}{cooldown}" +
                            (autoCastAvailable
                                ? "\n右键切换自动施放"
                                : string.Empty),
                            icon,
                            ability.IsPassive
                                ? string.Empty
                                : hotkey.Length > 0
                                    ? hotkey
                                    : Hotkey(slot),
                            ready,
                            learned.CooldownRemaining,
                            level.ManaCost,
                            commandToggled,
                            ability.IsPassive
                                ? War3CommandVisualState.Passive
                                : commandToggled
                                    ? War3CommandVisualState.Active
                                    : ready
                                        ? War3CommandVisualState.Ready
                                        : War3CommandVisualState.Unavailable,
                            $"{learned.Level}",
                            autoCastAvailable));
                    }
                    if (state.Hero && state.UnspentSkillPoints > 0)
                        commands.Add(new War3CommandSnapshot(
                            7, War3CommandKind.OpenLearnMenu, -1,
                            "学习技能", "打开英雄技能学习面板",
                            Btn("SelectHeroOn"), "O", true,
                            State: War3CommandVisualState.Learn,
                            Badge: state.UnspentSkillPoints.ToString()));
                }
            }
            if (_simulation is not null && ActiveSubgroupUnits().Any(unit =>
                    _simulation.Economy.IsWorkerOwnedBy(
                        unit, War3HumanScenario.PlayerId)))
                commands.Add(Command(7, War3CommandKind.OpenBuildMenu, -1,
                    "建造", "打开人族建筑菜单", Btn("BasicStruct"), "B"));
            return commands.ToArray();
        }
        if (_selectedBuildings.Count > 0 && _simulation is not null &&
            _production is not null && _technologies is not null &&
            _buildingUpgrades is not null)
        {
            var id = new GameplayBuildingId(ActiveSelectionBuilding());
            if (!_simulation.Construction.IsAlive(id)) return [];
            var building = _simulation.Construction.Observe(id);
            if (building.PlayerId != War3HumanScenario.PlayerId)
                return [];
            var result = new List<War3CommandSnapshot>();
            if (_simulation.BuildingUpgrades.IsUpgrading(id)) return [];
            if (building.State == BuildingLifecycleState.Completed &&
                building.Type.Id == War3HumanContent.ArcaneVault)
                return CreateArcaneVaultCommands(id, LocalShopUser(building));
            foreach (var learned in _simulation.Abilities.ObserveBuilding(
                         id, building.Type.Id))
            {
                var ability = _simulation.Abilities.Catalog.Ability(
                    learned.AbilityId);
                var level = ability.Levels[0];
                var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
                var target = ability.Activation is
                        AbilityActivationKind.TargetPoint or
                        AbilityActivationKind.ChannelPoint
                    ? AbilityCastTarget.Point(center)
                    : AbilityCastTarget.Building(id, center);
                var preview = ability.IsPassive
                    ? new AbilityCommandResult(
                        AbilityCommandCode.PassiveAbility,
                        AbilityId: ability.Id,
                        CasterBuilding: id.Value)
                    : _simulation.PreviewBuildingAbility(
                        War3HumanScenario.PlayerId, id, ability.Id, target);
                var ready = !ability.IsPassive && preview.Succeeded;
                War3HumanContent.TryAbility(
                    ability.RawId, out var presentation);
                var label = learned.Toggled &&
                            !string.IsNullOrWhiteSpace(
                                presentation?.AlternateName)
                    ? presentation.AlternateName
                    : ability.Name;
                var description = learned.Toggled &&
                                  !string.IsNullOrWhiteSpace(
                                      presentation?.AlternateDescription)
                    ? presentation.AlternateDescription
                    : ability.Description;
                var icon = learned.Toggled &&
                           !string.IsNullOrWhiteSpace(
                               presentation?.AlternateIconPath)
                    ? presentation.AlternateIconPath
                    : ability.IconPath;
                var hotkey = learned.Toggled &&
                             !string.IsNullOrWhiteSpace(
                                 presentation?.AlternateHotkey)
                    ? presentation.AlternateHotkey
                    : ability.Hotkey;
                result.Add(new War3CommandSnapshot(
                    AbilityObjectSlot(ability.RawId, learned.Toggled, 8),
                    War3CommandKind.BuildingAbility,
                    ability.Id,
                    label,
                    $"{description}\n法力 {level.ManaCost:0} · 冷却 " +
                    $"{level.CooldownSeconds:0.#} 秒" +
                    (learned.CooldownRemaining > 0f
                        ? $"（剩余 {learned.CooldownRemaining:0.0}）"
                        : string.Empty),
                    icon,
                    hotkey,
                    ready,
                    learned.CooldownRemaining,
                    level.ManaCost,
                    Toggled: learned.Toggled,
                    State: ability.IsPassive
                        ? War3CommandVisualState.Passive
                        : learned.Toggled
                            ? War3CommandVisualState.Active
                            : ready
                                ? War3CommandVisualState.Ready
                                : War3CommandVisualState.Unavailable));
            }
            foreach (var recipe in _production.Recipes.ToArray().Where(value =>
                         _simulation.BuildingUpgrades.SatisfiesBuildingType(
                             building.Type.Id,
                             value.ProducerBuildingTypeId)).Take(8))
            {
                var availability = _simulation.Production.ObserveAvailability(
                    War3HumanScenario.PlayerId, id, recipe,
                    _simulation.Construction, _simulation.Economy.Players,
                    _simulation.BuildingUpgrades.IsUpgrading,
                    _simulation.BuildingUpgrades.SatisfiesBuildingType);
                var definition = War3HumanContent.Units[recipe.UnitType.Id];
                var commandSlot = UnitObjectSlot(definition.ObjectId, 0);
                var available = availability.Code == ProductionCommandCode.Success;
                result.Add(new War3CommandSnapshot(
                    commandSlot, War3CommandKind.Train, recipe.Id,
                    definition.Name,
                    $"训练 {definition.Name} · {recipe.Cost.Minerals} 黄金 / " +
                    $"{recipe.Cost.VespeneGas} 木材 / {recipe.Cost.Supply} 人口\n" +
                    availability.Code,
                    definition.IconPath,
                    UnitObjectHotkey(definition.ObjectId, Hotkey(commandSlot)),
                    available,
                    State: available
                        ? War3CommandVisualState.Ready
                        : War3CommandVisualState.Unavailable));
            }
            if (_technologies.Technologies.ToArray().Any(value =>
                    _simulation.BuildingUpgrades.SatisfiesBuildingType(
                        building.Type.Id,
                        value.ResearcherBuildingTypeId)))
            {
                foreach (var technology in _technologies.Technologies.ToArray()
                             .Where(value =>
                                 _simulation.BuildingUpgrades.SatisfiesBuildingType(
                                     building.Type.Id,
                                     value.ResearcherBuildingTypeId))
                             )
                {
                    var presentation = War3HumanContent.Technologies[technology.Id];
                    var currentLevel = _simulation.Technology.Level(
                        War3HumanScenario.PlayerId, technology.Id);
                    var completed = currentLevel >= technology.MaximumLevel;
                    var queued = _simulation.Technology.IsQueued(
                        War3HumanScenario.PlayerId, technology.Id);
                    var availability = _simulation.Technology.ObserveAvailability(
                        War3HumanScenario.PlayerId, id, technology,
                        _simulation.Construction, _simulation.Economy.Players,
                        _simulation.BuildingUpgrades.IsUpgrading,
                        _simulation.BuildingUpgrades.SatisfiesBuildingType);
                    var iconLevel = completed
                        ? Math.Max(0, technology.MaximumLevel - 1)
                        : currentLevel;
                    var nextName = presentation.NameForLevel(iconLevel);
                    var commandSlot = ObjectProfileSlot(
                        War3HumanContent.UpgradeDataCatalog,
                        presentation.ObjectId, 8);
                    var ready = !completed && !queued &&
                                availability.Code == ResearchCommandCode.Success;
                    result.Add(new War3CommandSnapshot(
                        commandSlot, War3CommandKind.Research, technology.Id,
                        completed ? $"{nextName}（已完成）" : nextName,
                        (completed ? "科技已完成" : queued ? "科技已在研究队列中" :
                        $"研究 {nextName} " +
                        $"({currentLevel + 1}/{technology.MaximumLevel}) · " +
                        $"{technology.Cost.Minerals} 黄金 / " +
                        $"{technology.Cost.VespeneGas} 木材 / " +
                        $"{technology.ResearchSeconds:0.#} 秒\n" +
                        $"状态：{availability.Code}\n") +
                        presentation.Description,
                        presentation.IconPathForLevel(iconLevel),
                        ObjectProfileHotkey(
                            War3HumanContent.UpgradeDataCatalog,
                            presentation.ObjectId, Hotkey(commandSlot)),
                        ready,
                        State: completed
                            ? War3CommandVisualState.Completed
                            : queued
                                ? War3CommandVisualState.Queued
                                : ready
                                    ? War3CommandVisualState.Ready
                                    : War3CommandVisualState.Unavailable,
                        Badge: completed
                            ? "完成"
                            : queued
                                ? "队列"
                                : $"{currentLevel + 1}/{technology.MaximumLevel}"));
                }
            }
            foreach (var buildingUpgrade in _buildingUpgrades.ForSource(
                         building.Type.Id))
            {
                var availability =
                    _simulation.BuildingUpgrades.ObserveAvailability(
                        War3HumanScenario.PlayerId,
                        id,
                        buildingUpgrade,
                        _simulation.Construction,
                        _simulation.Economy.Players,
                        target =>
                            _simulation.Production.Observe(target).Orders.Length > 0 ||
                            _simulation.Technology.Observe(target).Orders.Length > 0,
                        technologyId => _simulation.Technology.Level(
                            War3HumanScenario.PlayerId, technologyId));
                var target = War3HumanContent.Buildings[
                    buildingUpgrade.TargetType.Id];
                var commandSlot = UnitObjectSlot(target.ObjectId, 8);
                var ready = availability.Code == BuildingUpgradeCommandCode.Success;
                result.Add(new War3CommandSnapshot(
                    commandSlot,
                    War3CommandKind.Upgrade,
                    buildingUpgrade.Id,
                    target.Name,
                    $"升级为 {target.Name} · " +
                    $"{buildingUpgrade.Cost.Minerals} 黄金 / " +
                    $"{buildingUpgrade.Cost.VespeneGas} 木材 / " +
                    $"{buildingUpgrade.UpgradeSeconds:0.#} 秒\n" +
                    availability.Code,
                    target.IconPath,
                    UnitObjectHotkey(target.ObjectId, "U"),
                    ready,
                    State: ready
                        ? War3CommandVisualState.Ready
                        : War3CommandVisualState.Unavailable));
            }
            if (building.Type.Function is BuildingFunctionKind.Production or
                BuildingFunctionKind.TownHall)
                result.Add(Command(7, War3CommandKind.Rally, -1,
                    "设置集结点", "设置新单位的集结位置", Btn("RallyPoint"), "Y"));
            return result.ToArray();
        }
        return [];
    }

    private List<War3CommandSnapshot> CreateLearnCommands(
        UnitAbilitySnapshot state)
    {
        if (_simulation is null) return [];
        var result = new List<War3CommandSnapshot>();
        foreach (var learned in state.Abilities)
        {
            var ability = _simulation.Abilities.Catalog.Ability(
                learned.AbilityId);
            if (!ability.HeroAbility) continue;
            var maximum = learned.Level >= ability.Levels.Length;
            var nextLevel = Math.Min(learned.Level + 1, ability.Levels.Length);
            var required = ability.RequiredHeroLevel +
                           Math.Max(0, nextLevel - 1) * ability.HeroLevelSkip;
            var ready = !maximum && state.UnspentSkillPoints > 0 &&
                        state.HeroLevel >= required;
            var slot = AbilityObjectSlot(ability.RawId, false, 8);
            result.Add(new War3CommandSnapshot(
                slot,
                War3CommandKind.LearnAbility,
                ability.Id,
                maximum ? $"{ability.Name}（已学满）" : $"学习 {ability.Name}",
                maximum
                    ? $"已达到最高等级 {ability.Levels.Length}\n{ability.Description}"
                    : $"消耗 1 技能点 · 需要英雄等级 {required}\n" +
                      $"当前 {learned.Level}/{ability.Levels.Length} 级\n" +
                      ability.Description,
                AbilityLearnIcon(ability),
                ability.Hotkey,
                ready,
                State: maximum
                    ? War3CommandVisualState.Completed
                    : ready
                        ? War3CommandVisualState.Learn
                        : War3CommandVisualState.Unavailable,
                Badge: maximum ? "满" : $"{nextLevel}"));
        }
        result.Add(Command(7, War3CommandKind.Cancel, -1,
            "返回", "返回普通命令面板", Btn("Cancel"), "ESC"));
        return result;
    }

    private static int UnitObjectSlot(string objectId, int fallback) =>
        TryCommandSlot(
            War3HumanContent.DataCatalog.TryGetEditorValue(
                objectId, "HumanUnitFunc", "Buttonpos", out var value)
                ? value
                : string.Empty,
            fallback);

    private static string UnitObjectHotkey(
        string objectId,
        string fallback) =>
        War3HumanContent.DataCatalog.TryGetEditorValue(
            objectId, "HumanUnitStrings", "Hotkey", out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()[..1].ToUpperInvariant()
            : fallback;

    private static int AbilityObjectSlot(
        string rawId,
        bool alternate,
        int fallback)
    {
        if (!War3HumanContent.AbilityDataCatalog.TryGet(rawId, out var data))
            return fallback;
        var field = alternate ? "UnButtonpos" : "Buttonpos";
        var value = ObjectProfileValue(data, field);
        if (string.IsNullOrWhiteSpace(value) && alternate)
            value = ObjectProfileValue(data, "Buttonpos");
        return TryCommandSlot(value, fallback);
    }

    private static int ObjectProfileSlot(
        War3ObjectDataCatalog catalog,
        string rawId,
        int fallback) =>
        catalog.TryGet(rawId, out var data)
            ? TryCommandSlot(ObjectProfileValue(data, "Buttonpos"), fallback)
            : fallback;

    private static string ObjectProfileHotkey(
        War3ObjectDataCatalog catalog,
        string rawId,
        string fallback)
    {
        if (!catalog.TryGet(rawId, out var data)) return fallback;
        var value = ObjectProfileValue(data, "Hotkey")
            .Split(',', StringSplitOptions.TrimEntries |
                        StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value[..1].ToUpperInvariant();
    }

    private static string AbilityLearnIcon(AbilityProfile ability)
    {
        if (!War3HumanContent.AbilityDataCatalog.TryGet(
                ability.RawId, out var data))
            return ability.IconPath;
        var icon = ObjectProfileValue(data, "ResearchArt")
            .Split(',', StringSplitOptions.TrimEntries |
                        StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(icon) ? ability.IconPath : icon;
    }

    private static string ObjectProfileValue(
        War3ObjectEditorData data,
        string field) =>
        data.Profile.FirstOrDefault(value => value.Key.Equals(
            field, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private static int TryCommandSlot(string? value, int fallback)
    {
        var parts = value?.Split(',', StringSplitOptions.TrimEntries |
                                     StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: >= 2 } &&
               int.TryParse(parts[0], out var column) &&
               int.TryParse(parts[1], out var row) &&
               column is >= 0 and < 4 && row is >= 0 and < 3
            ? row * 4 + column
            : Math.Clamp(fallback, 0, 11);
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
        _pendingAbilityId = -1;
        _pendingAbilityCaster = -1;
        _pendingAbilityBuilding = -1;
        _pendingItemUnit = -1;
        _pendingItemSlot = -1;
        _pendingBuilding = -1;
        _buildMenu = false;
        _learnMenu = false;
        _mode = "普通选择";
        _presenter?.HidePointerPreview();
        _presenter?.HideAbilityPointerPreview();
        _cursor?.SetMode(War3CursorMode.Normal);
    }

    private void Report(string value)
    {
        _status = value;
        UpdateHud();
    }

    private bool HasTargetMode() =>
        _movePending || _attackMovePending || _rallyPending ||
        _pendingAbilityId >= 0 || _pendingBuilding >= 0 ||
        _pendingItemSlot >= 0;

    private int[] SelectedUnits() => _simulation is null
        ? []
        : _selectedUnits
            .Where(unit => (uint)unit < (uint)_simulation.Units.Count &&
                           _simulation.Units.Alive[unit] &&
                           _simulation.Combat.Teams[unit] ==
                           War3HumanScenario.PlayerId)
            .Order().ToArray();

    private int[] SelectedProductionBuildings() => _selectedBuildings
        .Where(value =>
        {
            if (_simulation is null) return false;
            var id = new GameplayBuildingId(value);
            if (!_simulation.Construction.IsAlive(id)) return false;
            var building = _simulation.Construction.Observe(id);
            return building.PlayerId == War3HumanScenario.PlayerId &&
                   building.State == BuildingLifecycleState.Completed &&
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
        if (direction.Y >= -0.0001f) return false;
        if (_terrain is null)
        {
            var distance = -origin.Y / direction.Y;
            if (distance < 0f) return false;
            point = SimPlane3DTransform.ToSimulation(origin + direction * distance);
            return _simulation?.World.Bounds.Contains(point) == true;
        }

        return War3PointerTargeting.TryIntersectTerrainRay(
            _terrain, origin, direction, out point);
    }

    private void CreateCamera(StaticWorld world)
    {
        _camera = new Camera3D
        {
            Name = "RtsCamera",
            Current = true,
            Fov = 46f,
            Near = 0.08f,
            Far = 300f
        };
        AddChild(_camera);
        _cameraController = new Rts3DCameraController
        {
            Name = "CameraController",
            InitialDistance = 27f,
            MinimumDistance = 12f,
            MaximumDistance = 88f,
            InitialPitchDegrees = 56f,
            InitialYawDegrees = 0f
        };
        AddChild(_cameraController);
        _cameraController.EdgeScrollBlocked = point =>
            War3PointerTargeting.BlocksCameraEdgeScroll(
                _hud?.BlocksWorldPointer(point) == true,
                _navigationDebugger?.BlocksWorldPointer(point) == true);
        _cameraController.TargetWorldHeight = GroundWorldHeight;
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
                AmbientLightEnergy = 0.5f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapExposure = 1.05f,
                SsaoEnabled = true,
                SsaoRadius = 1.6f,
                SsaoIntensity = 1.8f,
                SsaoPower = 1.35f,
                SsaoLightAffect = 0.14f,
                GlowEnabled = true,
                GlowIntensity = 0.55f,
                GlowBloom = 0.06f
            }
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-56f, -32f, 0f),
            LightColor = new Color("fff0cf"),
            LightEnergy = 1.7f,
            ShadowEnabled = true,
            ShadowBias = 0.06f,
            ShadowNormalBias = 1.1f,
            DirectionalShadowMode =
                DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 160f,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowFadeStart = 0.88f
        });
    }

    private static NVector2 BuildingCenter(SimRect bounds) =>
        (bounds.Min + bounds.Max) * 0.5f;

    private void CreateGround(TerrainMapSnapshot terrain)
    {
        _terrainPresenter = new Rts3DTerrainPresenter
        {
            Name = "War3BattlefieldTerrain"
        };
        AddChild(_terrainPresenter);
        _terrainPresenter.Initialize(
            terrain,
            new War3TerrainMaterialSet(
                War3TerrainBlendStyle.DualGrid,
                classicCliffMeshesEnabled: true),
            cliffStyleMap: TerrainClassicCliffStyleMap.Uniform(terrain, 1));
    }

    private void ApplyRuntimeProfileVariant()
    {
        if (_runtimeProfiler is null) return;
        switch (_runtimeProfiler.Variant.ToLowerInvariant())
        {
            case "baseline":
            case "auto-skirmish":
            case "interactive-rendered-800":
            case "interactive-cap2":
                break;
            case "terrain-hidden":
                if (_terrainPresenter is not null) _terrainPresenter.Visible = false;
                break;
            case "terrain-no-shadow":
                _terrainPresenter?.SetShadowCastingEnabled(false);
                break;
            case "world-hidden":
                if (_presenter is not null) _presenter.Visible = false;
                break;
            case "resources-hidden":
            case "units-hidden":
            case "buildings-hidden":
            case "models-no-shadow":
            case "resources-no-shadow":
            case "units-no-shadow":
            case "buildings-no-shadow":
                _presenter?.ApplyRuntimeProfileVariant(_runtimeProfiler.Variant);
                break;
            case "path-budget-1":
                if (_simulation is not null) _simulation.PathBudgetPerTick = 1;
                break;
            case "path-budget-2":
                if (_simulation is not null) _simulation.PathBudgetPerTick = 2;
                break;
            default:
                GD.PushWarning(
                    $"WAR3_RUNTIME_PROFILE unknown_variant={_runtimeProfiler.Variant}");
                break;
        }
        GD.Print($"WAR3_RUNTIME_PROFILE variant_applied={_runtimeProfiler.Variant}");
    }

    private float GroundWorldHeight(NVector2 position) =>
        _terrain is null
            ? 0f
            : SimPlane3DTransform.ToWorldLength(_terrain.HeightAt(position));

    private void CreateHud()
    {
        var layer = new CanvasLayer { Name = "Interface", Layer = 20 };
        AddChild(layer);
        _hud = new War3RtsHud { Name = "HumanHud" };
        _hud.CommandRequested += ExecuteCommand;
        _hud.CommandAutoCastRequested += ToggleCommandAutoCast;
        _hud.QueueItemCancelRequested += CancelQueueItem;
        _hud.InventoryItemRequested += UseInventoryItem;
        _hud.SelectionGroupEntryRequested += SelectGroupEntry;
        _hud.ReturnRequested += () =>
            GetTree().ChangeSceneToFile(DemoSceneCatalog.CompatibilityEntry);
        _hud.MinimapFocusRequested += point => _cameraController?.FocusAt(point);
        layer.AddChild(_hud);
    }

    private void FinishSmoke()
    {
        if (_simulation is null || _runtime is null || _production is null ||
            _presenter is null || _hud is null)
            return;
        if ((uint)_smokeAbilityTarget < (uint)_simulation.Units.Count)
            _smokeAbilityDamageApplied |=
                _simulation.Combat.Health[_smokeAbilityTarget] <
                _smokeAbilityTargetInitialHealth;
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
        var smokeQueueOrders = _smokeRallyBuilding < 0
            ? []
            : _simulation.Production.Observe(
                new GameplayBuildingId(_smokeRallyBuilding)).Orders;
        var queueUiValid = smokeQueueOrders.Length == 5 &&
                           smokeQueueOrders[0].Progress is > 0f and < 1f &&
                           _hud.QueueLayoutReady &&
                           _hud.QueuePanelVisible &&
                           !_hud.SelectionDetailsVisible &&
                           _hud.QueuePresentationExclusive &&
                           _hud.QueueIconsAboveBackdrop &&
                           _hud.VisibleQueueItemCount == 5 &&
                           _hud.ActiveQueueItemKind ==
                               War3QueueItemKind.Production &&
                           _hud.ActiveQueueIconReady;
        var queueCancelValid = queueUiValid &&
                               _hud.TryInvokeQueueSlot(4) &&
                               _simulation.Production.Observe(
                                   new GameplayBuildingId(_smokeRallyBuilding))
                                   .Orders.Length == 4 &&
                               _hud.VisibleQueueItemCount == 4;
        var blacksmith = _simulation.CreateGameplayBuildingOverview()
            .First(value => value.PlayerId == War3HumanScenario.PlayerId &&
                            value.Type.Id == War3HumanContent.Blacksmith);
        var smokeTechnology = _technologies!.Technology(0) with
        {
            Cost = default,
            ResearchSeconds = 120f
        };
        var researchQueued = _simulation.IssueResearch(
            War3HumanScenario.PlayerId, blacksmith.Id, smokeTechnology);
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        _selectedBuildings.Add(blacksmith.Id.Value);
        RefreshSelection();
        var researchQueueUiValid = researchQueued.Succeeded &&
                                    _hud.QueuePanelVisible &&
                                    !_hud.SelectionDetailsVisible &&
                                    _hud.QueuePresentationExclusive &&
                                    _hud.QueueIconsAboveBackdrop &&
                                    _hud.VisibleQueueItemCount == 1 &&
                                   _hud.ActiveQueueItemKind ==
                                       War3QueueItemKind.Research &&
                                   _hud.ActiveQueueIconReady &&
                                   _hud.TryInvokeQueueSlot(0) &&
                                    _simulation.Technology.Observe(blacksmith.Id)
                                        .Orders.Length == 0 &&
                                    !_hud.QueuePanelVisible &&
                                    _hud.SelectionDetailsVisible &&
                                    _hud.QueuePresentationExclusive;
        if (_smokeShopBuilding >= 0 && _itemShops is not null)
        {
            var shopId = new GameplayBuildingId(_smokeShopBuilding);
            if (_simulation.Construction.IsAlive(shopId))
            {
                var shopBuilding = _simulation.Construction.Observe(shopId);
                _selectedUnits.Clear();
                _selectedBuildings.Clear();
                _selectedBuildings.Add(shopId.Value);
                RefreshSelection();
                var shopSelection = CreateSelectionSnapshot();
                var shopCommands = CreateCommands();
                var buy = shopCommands.FirstOrDefault(value =>
                    value.Kind == War3CommandKind.PurchaseItem &&
                    value.DataId == 0);
                var buyer = shopSelection.ShopUserUnit;
                var beforeGold = _simulation.Economy.Players.Snapshot(
                    War3HumanScenario.PlayerId).Minerals;
                if (buy.Enabled) ExecuteCommand(buy);
                var afterShopCommands = CreateCommands();
                var stockBadge = afterShopCommands.FirstOrDefault(value =>
                    value.Kind == War3CommandKind.PurchaseItem &&
                    value.DataId == 0).Badge;
                var afterGold = _simulation.Economy.Players.Snapshot(
                    War3HumanScenario.PlayerId).Minerals;
                if (buyer >= 0)
                {
                    _selectedBuildings.Clear();
                    _selectedUnits.Add(buyer);
                    RefreshSelection();
                }
                _smokeShopValid =
                    shopBuilding.State == BuildingLifecycleState.Completed &&
                    shopSelection.IsShop && buyer >= 0 &&
                    shopCommands.Length == 9 &&
                    shopCommands.Count(value =>
                        value.Kind == War3CommandKind.PurchaseItem) == 9 &&
                    shopCommands.Any(value => value.Slot == 0) &&
                    shopCommands.Any(value => value.Slot == 9) &&
                    buy.Enabled && beforeGold - afterGold == 100 &&
                    stockBadge == "1" &&
                    _itemShops.InventoryCount(buyer) == 1 &&
                    _hud.VisibleInventoryItemCount == 1;
                if (_smokeShopValid && _itemEffects is not null)
                {
                    _simulation.DamageUnit(buyer, 100f);
                    var damagedHealth = _simulation.Combat.Health[buyer];
                    UseInventoryItem(0);
                    _itemEffects.Update(1f, _simulation);
                    RefreshSelection();
                    _smokeItemUseValid =
                        _itemShops.InventoryCount(buyer) == 0 &&
                        _simulation.Combat.Health[buyer] > damagedHealth &&
                        _itemEffects.ActiveRecoveryCount > 0 &&
                        _hud.VisibleInventoryItemCount == 0;
                }
            }
        }
        player = _simulation.Economy.Players.Snapshot(
            War3HumanScenario.PlayerId);
        var liveCombatObjects = _simulation.CombatObjects.CreateOverview()
            .Where(value => value.Alive)
            .ToArray();
        var resourceCentersClear = Enumerable.Range(0, _simulation.Units.Count)
            .Where(unit => _simulation.Units.Alive[unit])
            .All(unit => _simulation.World.Obstacles.ToArray().All(obstacle =>
                    !StrictlyContains(
                        obstacle, _simulation.Units.Positions[unit])) &&
                liveCombatObjects.All(value => !StrictlyContains(
                    value.Bounds, _simulation.Units.Positions[unit])));
        var combatObjectsReady = _simulation.CombatObjects.Count ==
            Enumerable.Range(0, _simulation.Economy.ResourceNodeCount)
                .Count(index => _simulation.Economy.ObserveResourceNode(
                    new EconomyResourceNodeId(index)).Kind ==
                    EconomyResourceKind.VespeneGas) &&
            _simulation.CombatObjects.Count > 0;
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
            _presenter.SawRepeatedLumberCycle &&
            _presenter.SawTreeHitAnimation &&
            _presenter.TreeHarvestFeedbackCount > 0 &&
            _audioTreeHarvestPlayed > 0;
        var buildingAnimationsValid =
            !_presenter.CompletedBuildingUsedLifecycleAnimation;
        var buildingEffectsValid = _presenter.SawConstructionEffect &&
                                   !_presenter.IdleTownHallEffectLeak;
        var attackFacingValid = _presenter.SawAttackTargetFacing &&
                                 !_presenter.AttackTargetFacingMismatch;
        var constructionPresentationValid =
            _presenter.SawConstructionGhost &&
            _presenter.FoundationAppearedAfterApproach &&
            _presenter.PointerPreviewUsesWar3Model;
        var terrainReady = _terrain is not null &&
                           _terrain.FormatVersion ==
                           TerrainMapSnapshot.CurrentFormatVersion &&
                           _terrain.HeightAt(_runtime.PlayerHome) >
                           _terrain.HeightAt(
                               (_runtime.PlayerHome +
                                _runtime.EnemyHome) * 0.5f) + 32f &&
                           _terrain.HeightAt(_runtime.EnemyHome) >
                           _terrain.HeightAt(
                               (_runtime.PlayerHome +
                                _runtime.EnemyHome) * 0.5f) + 32f &&
                           _terrain.Cells.ToArray().Count(
                               static cell => cell.IsRamp) == 20 &&
                           _simulation.World.IsSegmentFree(
                               new NVector2(1_856f, 1_920f),
                               new NVector2(2_016f, 1_920f),
                               6f) &&
                           _simulation.World.IsSegmentFree(
                               new NVector2(4_384f, 1_920f),
                               new NVector2(4_544f, 1_920f),
                               6f) &&
                           War3HumanScenario.PcgTreeCount == 252 &&
                           _simulation.Economy.ResourceNodeCount ==
                           War3HumanScenario.ExpectedResourceNodeCount;
        var objectData = War3HumanContent.ObjectDataStatus;
        var peasantProfile = _production.UnitType(War3HumanContent.Peasant);
        var flyingProfile = _production.UnitType(War3HumanContent.FlyingMachine);
        var dataIntegrationReady =
            objectData.AbilityManifestLoaded &&
            objectData.UpgradeManifestLoaded &&
            objectData.MissingAbilityIds.Count == 0 &&
            objectData.MissingUpgradeIds.Count == 0 &&
            objectData.AppliedTechnologyCount == 21 &&
            War3HumanContent.DataCatalog.TryGet("hpea", out var peasantData) &&
            peasantData.Summary.Sight.Day is > 0f &&
            MathF.Abs(peasantProfile.Perception.VisionRange -
                      peasantData.Summary.Sight.Day.Value *
                      War3GameplayImportPolicy.Default.WorldDistanceScale) < 0.01f &&
            flyingProfile.Perception.TerrainVisionMode ==
                TerrainVisionMode.Elevated &&
            _simulation.CombatWeaponUpgradeTechnologyId == 0 &&
            _simulation.CombatBuildingArmorTechnologyId == 2 &&
            War3HumanContent.Technologies.Count == 21;
        var smokeSummonAlive = _smokeSummonedUnit >= 0 &&
                               _simulation.Units.Alive[_smokeSummonedUnit];
        var summonedObjectId = string.Empty;
        var smokeSummonKnown = smokeSummonAlive &&
            _simulation.Abilities.TrySummonedObjectId(
                _smokeSummonedUnit, out summonedObjectId);
        var smokeSummonMatches = smokeSummonKnown &&
                                 !string.IsNullOrEmpty(
                                     _smokeExpectedSummonObjectId) &&
                                 summonedObjectId.Equals(
                                     _smokeExpectedSummonObjectId,
                                     StringComparison.Ordinal);
        var abilityIntegrationReady =
            War3HumanContent.AbilityImportStatus.Catalog.Count ==
                War3HumanContent.AbilityImportStatus.RequestedCount &&
            War3HumanContent.AbilityImportStatus.MissingObjectIds.Length == 0 &&
            _smokeAbilityCastsIssued && _smokeAbilityDamageApplied &&
            smokeSummonMatches &&
            _simulation.AbilityEvents.LatestSequence >= 6;
        var mapLoadingValid = _loadingUiReady &&
                              _loadingProgressMonotonic &&
                              _loadingReachedCompletion &&
                              _lastLoadingProgress >= 1d &&
                              _loadingStageUpdateCount >= 11;
        var success = _presenter.PresentedUnitCount >= 14 &&
                      _presenter.PresentedBuildingCount >= 18 &&
                      _presenter.PresentedResourceCount >= 30 &&
                      _hud.PortraitReady &&
                      _hud.ConsoleLayoutReady &&
                      _hud.InventoryLayoutReady &&
                      _hud.MinimapAspectFitReady &&
                      _presenter.UnitSelectionAgentFitReady &&
                      player.Minerals + (_smokeShopValid ? 100 : 0) > 1_250 &&
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
                      _presenter.RallyMarkerUsesWar3Model && attackFacingValid &&
                       _presenter.SawMoveCommandConfirmation &&
                       _presenter.SawAttackCommandConfirmation &&
                       _presenter.SawTreeTargetConfirmation &&
                      constructionPresentationValid && terrainReady &&
                      dataIntegrationReady && mapLoadingValid &&
                      abilityIntegrationReady &&
                      _smokeSelectionGroupUiValid &&
                      _smokeConstructionProgressUiValid &&
                      _smokeBuildingPortraitValid &&
                      _smokeUnitPortraitValid &&
                      _smokeEnemySelectionValid &&
                      _smokeShopValid &&
                      _smokeItemUseValid &&
                      queueUiValid && queueCancelValid &&
                      researchQueueUiValid;
        success &= combatObjectsReady;
        GD.Print(
            $"WAR3_RTS_SMOKE success={success} units={_presenter.PresentedUnitCount} " +
             $"buildings={_presenter.PresentedBuildingCount} resources={_presenter.PresentedResourceCount} " +
             $"effects={_presenter.ActiveEffectCount} peak_effects={_presenter.PeakEffectCount} " +
             $"projectile_next={_simulation.CombatProjectiles.NextId} " +
            $"portrait={_hud.PortraitReady} hud_layout={_hud.ConsoleLayoutReady} " +
            $"inventory_layout={_hud.InventoryLayoutReady} " +
            $"minimap_fit={_hud.MinimapAspectFitReady} " +
            $"selection_agent_fit={_presenter.UnitSelectionAgentFitReady} " +
            $"gold={player.Minerals} lumber={player.VespeneGas} enemy_army={enemyArmy} " +
            $"gold_anim={_presenter.SawGoldGatherAnimation}/{_presenter.SawCarriedGoldAnimation} " +
            $"lumber_anim={_presenter.SawLumberGatherAnimation}/{_presenter.SawCarriedLumberAnimation} " +
            $"lumber_repeat={_presenter.SawRepeatedLumberCycle} " +
            $"rally={rallySet} resource_collision={resourceCentersClear} " +
            $"combat_objects={_simulation.CombatObjects.Count}/" +
            $"{combatObjectsReady} " +
            $"construction_sync={constructionSynchronized} " +
            $"lumber_progressive={_presenter.SawProgressiveLumberCargo} " +
            $"tree_health={treeHealthReduced} " +
            $"gold_non_attack={!_presenter.GoldGatherUsedAttackAnimation} " +
            $"gold_hidden={_presenter.SawGoldMinerHidden} " +
            $"building_idle={buildingAnimationsValid} " +
             $"building_effects={buildingEffectsValid} " +
             $"construction_ghost={_presenter.SawConstructionGhost} " +
             $"foundation_after_arrival={_presenter.FoundationAppearedAfterApproach} " +
             $"placement_model={_presenter.PointerPreviewUsesWar3Model} " +
             $"animation_blend={_presenter.SawBlendedTransition} " +
            $"rally_model={_presenter.RallyMarkerUsesWar3Model} " +
             $"command_confirmation={_presenter.SawMoveCommandConfirmation}/" +
             $"{_presenter.SawAttackCommandConfirmation}/" +
             $"tree:{_presenter.SawTreeTargetConfirmation} " +
             $"tree_feedback={_presenter.SawTreeHitAnimation}/" +
             $"{_presenter.TreeHarvestFeedbackCount}/" +
             $"audio:{_audioTreeHarvestPlayed}/{_audioTreeHarvestEvents} " +
             $"attack_facing={attackFacingValid} terrain={terrainReady} " +
             $"data_integration={dataIntegrationReady} " +
             $"ability_integration={abilityIntegrationReady}/" +
             $"{_simulation.AbilityEvents.LatestSequence} " +
             $"ability_parts={_smokeAbilityCastsIssued}/" +
             $"{_smokeAbilityDamageApplied}/" +
             $"{smokeSummonAlive}/{smokeSummonKnown}/{smokeSummonMatches} " +
             $"map_loading={mapLoadingValid}/{_loadingStageUpdateCount} " +
             $"selection_group_ui={_smokeSelectionGroupUiValid} " +
             $"construction_progress_ui={_smokeConstructionProgressUiValid} " +
             $"portrait_building={_smokeBuildingPortraitValid} " +
             $"portrait_unit={_smokeUnitPortraitValid} " +
             $"enemy_selection={_smokeEnemySelectionValid} " +
             $"shop={_smokeShopValid} item_use={_smokeItemUseValid} " +
             $"queue_ui={queueUiValid}/5 queue_cancel={queueCancelValid}/" +
             $"4 research_queue={researchQueueUiValid} " +
             $"terrain_hash={_terrain?.StableHashText ?? "none"} " +
             $"pcg_trees={War3HumanScenario.PcgTreeCount} " +
             $"pcg_dense={War3HumanScenario.DensePcgTreeCount} " +
             $"pcg_sparse={War3HumanScenario.SparsePcgTreeCount} " +
             $"pcg_hash={War3HumanScenario.PcgHashText}");
        if (!_capture) GetTree().Quit(success ? 0 : 1);
    }

    private void PrepareSmokeCombat()
    {
        if (_simulation is null || _production is null) return;
        var rallyBuilding = _simulation.CreateGameplayBuildingOverview()
            .First(value => value.PlayerId == War3HumanScenario.PlayerId &&
                            value.Type.Id == War3HumanContent.Barracks);
        _smokeRallyBuilding = rallyBuilding.Id.Value;
        var home = _runtime?.PlayerHome ?? War3HumanScenario.PlayerHome;
        if (_presenter is null ||
            !_presenter.ShowCommandConfirmation(
                home + new NVector2(90f, 30f),
                War3CommandFeedbackKind.Move) ||
            !_presenter.ShowCommandConfirmation(
                home + new NVector2(175f, 30f),
                War3CommandFeedbackKind.Attack) ||
            !_presenter.FlashResourceTarget(
                _runtime!.ResourceNodes.First(id =>
                    _simulation.Economy.ObserveResourceNode(id).Kind ==
                    EconomyResourceKind.VespeneGas)))
            throw new InvalidOperationException(
                "Smoke command confirmation setup failed.");
        _smokeRallyPosition = home + new NVector2(330f, 210f);
        if (!_simulation.SetProductionRallyTarget(
                War3HumanScenario.PlayerId, rallyBuilding.Id,
                RallyTarget.Ground(_smokeRallyPosition)))
            throw new InvalidOperationException("Smoke rally target setup failed.");
        var smokeRecipe = _production.Recipe(0) with
        {
            Cost = new EconomyCost(0, 0, 1),
            ProductionSeconds = 120f
        };
        for (var index = 0; index < ProductionSystem.MaximumQueueLength; index++)
        {
            var queued = _simulation.IssueProduction(
                War3HumanScenario.PlayerId, rallyBuilding.Id, smokeRecipe);
            if (!queued.Succeeded)
                throw new InvalidOperationException(
                    $"Smoke production queue setup failed: {queued.Code}.");
        }
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        _selectedBuildings.Add(rallyBuilding.Id.Value);
        RefreshSelection();
        _smokeBuildingPortraitValid = _hud is not null &&
                                      _hud.PortraitReady &&
                                      _hud.PortraitAnimationPlaying &&
                                      _hud.PortraitSequence.StartsWith(
                                          "Portrait",
                                          StringComparison.OrdinalIgnoreCase);
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
            home + new NVector2(-340f, 330f));
        if (!construction.Succeeded)
            throw new InvalidOperationException(
                $"Smoke construction setup failed: {construction.Code}/" +
                $"{construction.PlacementCode}.");
        _smokeConstructionBuilding = construction.BuildingId.Value;
        var vault = _buildings.Type(War3HumanContent.ArcaneVault) with
        {
            Cost = default,
            BuildSeconds = 0.03f,
            ConstructionMethod = ConstructionMethodKind.StartAndRelease
        };
        var shopBuilder = _runtime.PlayerWorkers[1];
        var shopOffsets = new[]
        {
            new NVector2(380f, -300f),
            new NVector2(380f, 300f),
            new NVector2(-520f, 320f),
            new NVector2(-520f, -320f),
            new NVector2(0f, -430f),
            new NVector2(0f, 430f)
        };
        foreach (var offset in shopOffsets)
        {
            var center = home + offset;
            if (!_simulation.PreviewConstruction(
                    War3HumanScenario.PlayerId, shopBuilder, vault, center)
                .Succeeded)
                continue;
            var shopConstruction = _simulation.IssueConstruction(
                War3HumanScenario.PlayerId, shopBuilder, vault, center);
            if (!shopConstruction.Succeeded) continue;
            _smokeShopBuilding = shopConstruction.BuildingId.Value;
            _simulation.AddUnit(
                center + new NVector2(100f, 0f),
                _production.UnitType(War3HumanContent.Archmage),
                War3HumanScenario.PlayerId);
            break;
        }
        if (_smokeShopBuilding < 0)
            throw new InvalidOperationException(
                "Smoke Arcane Vault construction setup failed.");
        _presenter?.SetPointerPreview(
            home + new NVector2(-340f, 330f), farm.Size,
            War3HumanContent.Buildings[War3HumanContent.Farm].ModelSource,
            valid: true);
        _presenter?.HidePointerPreview();
        // Rifleman is an instant-hit weapon in the Warcraft editor data.
        // Exercise the projectile/effect pipeline with an actual missile unit.
        var projectileUnit = _production.UnitType(War3HumanContent.Priest);
        var player = _simulation.AddUnit(
            home + new NVector2(300f, 120f), projectileUnit,
            War3HumanScenario.PlayerId);
        var enemy = _simulation.AddUnit(
            home + new NVector2(420f, 120f), projectileUnit,
            War3HumanScenario.EnemyId);
        var hero = _simulation.AddUnit(
            home + new NVector2(260f, -40f),
            _production.UnitType(War3HumanContent.Archmage),
            War3HumanScenario.PlayerId);
        var mountainKing = _simulation.AddUnit(
            home + new NVector2(260f, -115f),
            _production.UnitType(War3HumanContent.MountainKing),
            War3HumanScenario.PlayerId);
        var skillTarget = _simulation.AddUnit(
            home + new NVector2(355f, -40f),
            _production.UnitType(War3HumanContent.Footman),
            War3HumanScenario.EnemyId);
        var pagingUnits = Enumerable.Range(0, 10)
            .Select(index => _simulation.AddUnit(
                home + new NVector2(
                    320f + index % 5 * 26f,
                    180f + index / 5 * 26f),
                _production.UnitType(War3HumanContent.Footman),
                War3HumanScenario.PlayerId))
            .ToArray();
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        _selectedUnits.Add(pagingUnits[0]);
        RefreshSelection();
        _smokeUnitPortraitValid = _hud is not null &&
                                  _hud.PortraitReady &&
                                  _hud.PortraitAnimationPlaying &&
                                  _hud.PortraitManaBackgroundVisible &&
                                  _hud.PortraitSequence.StartsWith(
                                      "Portrait",
                                      StringComparison.OrdinalIgnoreCase);
        _selectedUnits.Clear();
        _selectedUnits.Add(enemy);
        RefreshSelection();
        var enemySelection = CreateSelectionSnapshot();
        _smokeEnemySelectionValid = _hud is not null &&
                                    !enemySelection.Controllable &&
                                    !enemySelection.PortraitAnimated &&
                                    enemySelection.PortraitTeam ==
                                        War3HumanScenario.EnemyId &&
                                    CreateCommands().Length == 0 &&
                                    _hud.PortraitReady &&
                                    !_hud.PortraitAnimationPlaying;
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        foreach (var worker in _runtime.PlayerWorkers)
            _selectedUnits.Add(worker);
        _selectedUnits.Add(player);
        _selectedUnits.Add(hero);
        _selectedUnits.Add(mountainKing);
        foreach (var unit in pagingUnits) _selectedUnits.Add(unit);
        RefreshSelection();
        const int expectedSelectionEntries =
            War3HumanScenario.InitialWorkers + 13;
        var firstSelectionPageValid = _hud is not null &&
                                      _hud.SelectionGroupVisible &&
                                      _hud.SelectionGroupTotalEntryCount ==
                                          expectedSelectionEntries &&
                                      _hud.SelectionGroupPageCount == 2 &&
                                      _hud.SelectionGroupPage == 0 &&
                                      _hud.VisibleSelectionGroupEntryCount == 18 &&
                                      _hud.VisibleSelectionPageTabCount == 2 &&
                                      _hud.SelectionGroupIconsAreSquare;
        var secondPageInvoked = _hud?.TryInvokeSelectionPageTab(1) == true;
        _smokeSelectionGroupUiValid = firstSelectionPageValid &&
                                      secondPageInvoked &&
                                      _hud is not null &&
                                      _hud.SelectionGroupPage == 1 &&
                                      _hud.VisibleSelectionGroupEntryCount == 2 &&
                                      _hud.QueuePresentationExclusive;
        _selectedUnits.Clear();
        _selectedBuildings.Clear();
        _selectedBuildings.Add(_smokeConstructionBuilding);
        RefreshSelection();
        _smokeConstructionProgressUiValid = _hud is not null &&
                                            _hud.ConstructionProgressVisible &&
                                            _hud.SelectionDetailsVisible &&
                                            _hud.QueuePresentationExclusive;
        _selectedBuildings.Clear();
        _selectedBuildings.Add(rallyBuilding.Id.Value);
        RefreshSelection();
        // The scenario has already seeded fog-of-war before these smoke-only
        // units are appended. Refresh visibility once so their first attack
        // order is not discarded before the normal end-of-tick refresh.
        _simulation.Visibility.Update(
            _simulation.Units, _simulation.Combat, _simulation.Construction);
        if (!War3HumanContent.TryAbility("AHwe", out var waterElemental) ||
            waterElemental is null ||
            !War3HumanContent.TryAbility("AHtb", out var stormBolt) ||
            stormBolt is null)
            throw new InvalidOperationException(
                "Smoke ability definitions are unavailable.");
        var learnSummon = _simulation.IssueLearnAbility(
            War3HumanScenario.PlayerId, hero, waterElemental.AbilityId);
        var learnBolt = _simulation.IssueLearnAbility(
            War3HumanScenario.PlayerId, mountainKing, stormBolt.AbilityId);
        // This integration probe validates the cast/effect pipeline rather
        // than the scenario's starting-resource balance. Refill through the
        // authoritative store using each JSON-derived mana snapshot.
        var heroMana = _simulation.Abilities.Observe(hero);
        _simulation.Abilities.RestoreMana(
            hero, heroMana.MaximumMana - heroMana.Mana);
        var mountainKingMana = _simulation.Abilities.Observe(mountainKing);
        _simulation.Abilities.RestoreMana(
            mountainKing,
            mountainKingMana.MaximumMana - mountainKingMana.Mana);
        var summonLevel = _simulation.Abilities.Catalog
            .Ability(waterElemental.AbilityId).Levels[0];
        _smokeExpectedSummonObjectId = summonLevel.Effects
            .First(value => value.Kind == AbilityEffectKind.Summon)
            .Summon.ObjectId;
        var summon = _simulation.IssueAbility(
            War3HumanScenario.PlayerId, hero, waterElemental.AbilityId,
            AbilityCastTarget.Self(hero, _simulation.Units.Positions[hero]));
        var targetHealth = _simulation.Combat.Health[skillTarget];
        var bolt = _simulation.IssueAbility(
            War3HumanScenario.PlayerId, mountainKing, stormBolt.AbilityId,
            AbilityCastTarget.Unit(
                skillTarget, _simulation.Units.Positions[skillTarget]));
        _smokeAbilityCastsIssued = learnSummon.Succeeded &&
                                   learnBolt.Succeeded &&
                                   summon.Succeeded && bolt.Succeeded;
        GD.Print(
            $"WAR3_SMOKE_ABILITY learn_summon={learnSummon.Code} " +
            $"learn_bolt={learnBolt.Code} summon={summon.Code} " +
            $"bolt={bolt.Code}");
        // Projectile damage belongs to the later authoritative Impact phase.
        // FinishSmoke compares this checkpoint after the flight completes.
        _smokeAbilityTarget = skillTarget;
        _smokeAbilityTargetInitialHealth = targetHealth;
        _smokeSummonedUnit = _simulation.Abilities.ObserveSummons()
            .Select(value => value.Unit).DefaultIfEmpty(-1).First();
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
        var path = ProjectSettings.GlobalizePath(
            _pcgCapture
                ? "user://war3_rts_pcg_capture.png"
                : _terrainCapture
                    ? "user://war3_rts_terrain_capture.png"
                    : _heroCapture
                        ? "user://war3_rts_hero_capture.png"
                        : "user://war3_rts_capture.png");
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

    private readonly record struct SelectionUnitCandidate(
        int Unit,
        string Key,
        int Priority,
        int HeroLevel);

    private sealed record SelectionSubgroup(
        string Key,
        int Priority,
        int HeroLevel,
        int FirstEntity,
        int[] Entities);

    private enum TargetMode : byte
    {
        Move,
        AttackMove,
        Rally
    }
}
