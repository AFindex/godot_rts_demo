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
    private War3WorldPresenter? _presenter;
    private Rts3DTerrainPresenter? _terrainPresenter;
    private Camera3D? _camera;
    private Rts3DCameraController? _cameraController;
    private War3RtsHud? _hud;
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
    private Vector2 _dragStart;
    private bool _dragging;
    private bool _selectionAdditive;
    private bool _movePending;
    private bool _attackMovePending;
    private bool _rallyPending;
    private int _pendingAbilityId = -1;
    private int _pendingAbilityCaster = -1;
    private int _pendingBuilding = -1;
    private bool _buildMenu;
    private string _mode = "普通选择";
    private string _status = "人族基地已就绪";
    private double _elapsed;
    private double _hudAccumulator;
    private bool _smoke;
    private long _smokeEndTick;
    private bool _capture;
    private bool _terrainCapture;
    private bool _pcgCapture;
    private bool _offlineBakeRequested;
    private int _earlyExitCode;
    private War3RuntimeProfiler? _runtimeProfiler;
    private War3StressTestMode? _stressTest;
    private War3NavigationDebugger? _navigationDebugger;
    private War3CursorController? _cursor;
    private int _smokeRallyBuilding = -1;
    private NVector2 _smokeRallyPosition;
    private int _smokeConstructionBuilding = -1;
    private int _smokeConstructionBuilder = -1;
    private long _smokeConstructionPauseTick = -1;
    private float _smokeConstructionPauseProgress;
    private bool _smokeConstructionPauseStable = true;
    private bool _smokeConstructionResumed;
    private bool _smokeConstructionAdvancedAfterResume;
    private bool _smokeAbilityCastsIssued;
    private bool _smokeAbilityDamageApplied;
    private int _smokeSummonedUnit = -1;

    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        _smoke = arguments.Contains("--war3-rts-smoke");
        _terrainCapture = arguments.Contains("--war3-rts-terrain-capture");
        _pcgCapture = arguments.Contains("--war3-rts-pcg-capture");
        _offlineBakeRequested = arguments.Contains("--war3-bake-map-cache");
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
                   _terrainCapture || _pcgCapture;
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
            navigationAuditRequested || _stressTest is not null ||
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

        await AdvanceMapLoadingAsync(
            6, 0.77d,
            "生成灯光、经典地表、悬崖/坡道批次和 RTS 相机。");
        CreateLighting();
        CreateGround(_terrain);
        CreateCamera(world);

        await AdvanceMapLoadingAsync(
            7, 0.88d,
            "创建单位/建筑表现器、War3 HUD、肖像视口与生产研究队列。");
        _presenter = new War3WorldPresenter
        {
            Name = "War3World",
            ProfilingEnabled = _runtimeProfiler is not null
        };
        AddChild(_presenter);
        _presenter.Initialize(_simulation, _production, _camera!);
        _simulation.DetailedProfilingEnabled =
            _runtimeProfiler is not null || _stressTest is not null;
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
            _selectedUnits.Clear();
            _selectedBuildings.Clear();
            RefreshSelection();
            _cameraController!.FocusAt(_stressTest.FocusPoint, immediate: true);
            _status = "War3 压测：自动战斗、补兵、免费建造与定时自毁";
        }
        if (_capture) _ = CaptureAsync();
        UpdateHud();
        GD.Print($"WAR3_MAP_LOADED id={map.Metadata.Id} hash={map.StableHashText} " +
                 $"terrain={map.Terrain.StableHashText} objects={map.Objects.Length}");
        _runtimeProfiler?.MapReady();
        return false;
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

    public override void _PhysicsProcess(double delta)
    {
        if (_mapLoadInProgress || _simulation is null || _runtime is null) return;
        var profileStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var frame = (float)Math.Min(delta, 0.05d);
        var advanceSimulation =
            _navigationDebugger?.ConsumeAdvanceSimulation() ?? true;
        if (advanceSimulation) _stressTest?.Update();
        var stressEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        if (advanceSimulation)
            _runtime.AiDirector.Update(_simulation.Metrics.Tick);
        var aiEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        if (advanceSimulation)
        {
            _simulation.Tick(frame);
            ConsumeAudioEvents();
        }
        var simulationEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_smoke && advanceSimulation) UpdateSmokeConstructionCycle();
        if (advanceSimulation) _elapsed += frame;
        PruneSelection();
        _navigationDebugger?.SamplePhysics(delta);
        _hudAccumulator += advanceSimulation ? frame : delta;
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
            ElapsedMilliseconds(profileStart, stressEnd),
            ElapsedMilliseconds(stressEnd, aiEnd),
            ElapsedMilliseconds(aiEnd, simulationEnd),
            ElapsedMilliseconds(simulationEnd, profileEnd),
            GC.GetAllocatedBytesForCurrentThread() - allocationStart,
            _simulation.Metrics,
            _runtime.AiDirector.LastUpdateProfile,
            _stressTest?.LastUpdateProfile ?? default);
    }

    public override void _Process(double delta)
    {
        if (_mapLoadInProgress || _simulation is null) return;
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
                _presenter?.LastSyncProfile ?? default) == true)
        {
            _stressTest?.PrintSummary();
            GetTree().Quit(0);
        }
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d / System.Diagnostics.Stopwatch.Frequency;

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
            case War3CommandKind.Rally:
                BeginTarget(TargetMode.Rally);
                break;
            case War3CommandKind.Ability:
                BeginAbility(command.DataId);
                break;
            case War3CommandKind.BuildingAbility:
                UseBuildingAbility(command.DataId);
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

    private void BeginTarget(TargetMode target)
    {
        _movePending = target == TargetMode.Move && _selectedUnits.Count > 0;
        _attackMovePending = target == TargetMode.AttackMove && _selectedUnits.Count > 0;
        _rallyPending = target == TargetMode.Rally && SelectedProductionBuildings().Length > 0;
        _pendingBuilding = -1;
        _buildMenu = false;
        _pendingAbilityId = -1;
        _pendingAbilityCaster = -1;
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
        var caster = _selectedUnits.Order().FirstOrDefault(unit =>
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
        if (_simulation is null || _selectedBuildings.Count == 0) return;
        var building = new GameplayBuildingId(
            _selectedBuildings.Order().First());
        var result = _simulation.IssueBuildingAbility(
            War3HumanScenario.PlayerId, building, abilityId);
        if ((uint)abilityId < (uint)_simulation.Abilities.Catalog.Count)
        {
            var ability = _simulation.Abilities.Catalog.Ability(abilityId);
            Report(result.Succeeded
                ? $"已执行 {ability.Name}"
                : $"{ability.Name} 执行失败：{result.Code}");
        }
    }

    private void LearnAbility(int abilityId)
    {
        if (_simulation is null) return;
        var caster = _selectedUnits.Order().FirstOrDefault(unit =>
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
        Report(result.Succeeded
            ? $"已学习 {ability.Name}"
            : $"{ability.Name} 学习失败：{result.Code}");
    }

    private void CompleteTarget(NVector2 point, bool queued)
    {
        if (_simulation is null) return;
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
            if (result.Succeeded) PlayCommandAudio(attack: false, queued);
        }
        else if (_attackMovePending)
        {
            var result = _simulation.IssuePlayerAttackMove(
                War3HumanScenario.PlayerId, SelectedUnits(), point, queued);
            Report(result.Succeeded ? "已下达攻击移动" : $"攻击移动失败：{result.Code}");
            if (result.Succeeded) PlayCommandAudio(attack: true, queued);
        }
        else if (_rallyPending)
        {
            SetRallyAt(point);
        }
        if (!queued) CancelMode();
    }

    private void CompleteAbilityTarget(NVector2 point)
    {
        if (_simulation is null || _pendingAbilityCaster < 0 ||
            !_simulation.Units.Alive[_pendingAbilityCaster])
        {
            CancelMode();
            return;
        }
        var ability = _simulation.Abilities.Catalog.Ability(_pendingAbilityId);
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
        if (result.Succeeded)
        {
            var attack = target.Kind is SmartCommandTargetKind.EnemyUnit or
                SmartCommandTargetKind.EnemyBuilding;
            PlayCommandAudio(attack, queued);
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
            _ => false
        };
        Report(canceled
            ? $"已取消：{item.Label}"
            : $"无法取消：{item.Label}");
        UpdateHud();
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
            var selected = true;
            if (additive && _selectedUnits.Contains(best))
            {
                _selectedUnits.Remove(best);
                selected = false;
            }
            else
            {
                _selectedUnits.Add(best);
            }
            RefreshSelection();
            if (selected) PlaySelectionAudio();
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
        if (_hud?.BlocksWorldPointer(screen) == true ||
            _navigationDebugger?.BlocksWorldPointer(screen) == true)
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

    private void UpdateAbilityPointer(NVector2 point)
    {
        if (_simulation is null || _presenter is null ||
            _pendingAbilityCaster < 0 ||
            !_simulation.Units.Alive[_pendingAbilityCaster] ||
            (uint)_pendingAbilityId >= (uint)_simulation.Abilities.Catalog.Count)
        {
            _presenter?.HideAbilityPointerPreview();
            _cursor?.SetMode(War3CursorMode.InvalidTarget);
            return;
        }

        var ability = _simulation.Abilities.Catalog.Ability(_pendingAbilityId);
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
            var weaponLevel = _simulation.CombatWeaponUpgradeTechnologyId < 0
                ? 0
                : _simulation.Technology.Level(
                    _simulation.Combat.Teams[first],
                    _simulation.CombatWeaponUpgradeTechnologyId);
            var attack = _simulation.Combat.AttackDamage[first];
            var weapons = _simulation.Combat.WeaponProfiles[first];
            var activeWeaponSlot = _simulation.Combat.ActiveWeaponSlots[first];
            var activeWeapon = weapons.FirstOrDefault(value =>
                value.Slot == activeWeaponSlot);
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
            return new War3SelectionSnapshot(
                homogeneous && living.Length > 1
                    ? $"{definition.Name} × {living.Length}"
                    : definition.Name,
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
                _simulation.Combat.Armor[first],
                abilityState.Hero ? abilityState.HeroLevel : definition.Level,
                weaponLevel,
                attackClass,
                definition.ArmorClass.Length > 0
                    ? definition.ArmorClass
                    : ArmorClass(_simulation.Combat.Attributes[first]),
                false)
            {
                Mana = homogeneous
                    ? living.Sum(unit => _simulation.Abilities.Observe(unit).Mana)
                    : abilityState.Mana,
                MaximumMana = homogeneous
                    ? living.Sum(unit =>
                        _simulation.Abilities.Observe(unit).MaximumMana)
                    : abilityState.MaximumMana,
                ManaRegeneration = abilityState.ManaRegeneration,
                HeroExperience = abilityState.HeroExperience,
                ExperienceForNextLevel =
                    abilityState.ExperienceForNextLevel,
                UnspentSkillPoints = abilityState.UnspentSkillPoints,
                AbilityStatuses = abilityState.Statuses,
                Buffs = _simulation.Abilities.ObserveBuffs(first),
                ActiveWeaponSlot = activeWeaponSlot,
                WeaponCount = weapons.Length,
                WeaponTargetLabel = WeaponTargetLabel(activeWeapon.TargetLayers)
            };
        }
        if (_selectedBuildings.Count > 0)
        {
            var id = new GameplayBuildingId(_selectedBuildings.Order().First());
            if (!_simulation.Construction.IsAlive(id)) return War3SelectionSnapshot.Empty;
            var building = _simulation.ObserveGameplayBuilding(id);
            var definition = War3HumanContent.Buildings[building.Type.Id];
            var queueItems = CreateQueueItems(id);
            var queueLabel = queueItems.Length == 0
                ? string.Empty
                : queueItems[0].StateLabel + "：" + queueItems[0].Label;
            var progress = queueItems.Length == 0 ? 0f : queueItems[0].Progress;
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
                queueLabel,
                0f,
                building.EffectiveArmor,
                1,
                0,
                "无攻击",
                definition.ArmorClass.Length > 0
                    ? definition.ArmorClass
                    : ArmorClass(building.Type.Attributes),
                true)
            {
                QueueItems = queueItems
            };
        }
        return War3SelectionSnapshot.Empty;
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
            if (_simulation is not null)
            {
                var caster = _selectedUnits.Order().FirstOrDefault(unit =>
                    _simulation.Units.Alive[unit] &&
                    _simulation.Combat.Teams[unit] ==
                    War3HumanScenario.PlayerId, -1);
                if (caster >= 0)
                {
                    var state = _simulation.Abilities.Observe(caster);
                    var abilitySlots = new[] { 1, 2, 3, 5, 6, 7, 10 };
                    var commandSlot = 0;
                    foreach (var learned in state.Abilities)
                    {
                        var ability = _simulation.Abilities.Catalog.Ability(
                            learned.AbilityId);
                        if (commandSlot >= abilitySlots.Length)
                            continue;
                        if (learned.Level <= 0)
                        {
                            if (!ability.HeroAbility) continue;
                            var required = ability.RequiredHeroLevel;
                            var enabled = state.UnspentSkillPoints > 0 &&
                                          state.HeroLevel >= required;
                            commands.Add(new War3CommandSnapshot(
                                abilitySlots[commandSlot++],
                                War3CommandKind.LearnAbility,
                                ability.Id,
                                $"学习 {ability.Name}",
                                $"消耗 1 技能点 · 需要英雄等级 {required}\n" +
                                ability.Description,
                                ability.IconPath,
                                ability.Hotkey.Length > 0
                                    ? ability.Hotkey
                                    : Hotkey(commandSlot),
                                enabled));
                            continue;
                        }
                        if (ability.IsPassive) continue;
                        var level = ability.Levels[learned.Level - 1];
                        var ready = learned.CooldownRemaining <= 0.001f &&
                                    state.Mana + 0.001f >= level.ManaCost &&
                                    _simulation.Abilities.CanCast(caster);
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
                        var label = alternateForm &&
                                    !string.IsNullOrWhiteSpace(
                                        presentation?.AlternateName)
                            ? presentation.AlternateName
                            : ability.Name;
                        var description = alternateForm &&
                                          !string.IsNullOrWhiteSpace(
                                              presentation?.AlternateDescription)
                            ? presentation.AlternateDescription
                            : ability.Description;
                        var icon = alternateForm &&
                                   !string.IsNullOrWhiteSpace(
                                       presentation?.AlternateIconPath)
                            ? presentation.AlternateIconPath
                            : ability.IconPath;
                        var hotkey = alternateForm &&
                                     !string.IsNullOrWhiteSpace(
                                         presentation?.AlternateHotkey)
                            ? presentation.AlternateHotkey
                            : ability.Hotkey;
                        commands.Add(new War3CommandSnapshot(
                            abilitySlots[commandSlot++],
                            War3CommandKind.Ability,
                            ability.Id,
                            label,
                            $"{description}\n等级 {learned.Level} · " +
                            $"法力 {level.ManaCost:0} · 冷却 " +
                            $"{level.CooldownSeconds:0.#} 秒{range}{cooldown}",
                            icon,
                            hotkey.Length > 0
                                ? hotkey
                                : Hotkey(commandSlot),
                            ready,
                            learned.CooldownRemaining,
                            level.ManaCost,
                            learned.Toggled));
                    }
                }
            }
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
            foreach (var learned in _simulation.Abilities.ObserveBuilding(
                         id, building.Type.Id))
            {
                var ability = _simulation.Abilities.Catalog.Ability(
                    learned.AbilityId);
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
                    learned.Toggled ? 10 : 9,
                    War3CommandKind.BuildingAbility,
                    ability.Id,
                    label,
                    description,
                    icon,
                    hotkey,
                    Toggled: learned.Toggled));
            }
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
            if (_technologies.Technologies.ToArray().Any(value =>
                    value.ResearcherBuildingTypeId == building.Type.Id))
            {
                foreach (var technology in _technologies.Technologies.ToArray()
                             .Where(value => value.ResearcherBuildingTypeId == building.Type.Id)
                             .Take(8 - slot))
                {
                    var presentation = War3HumanContent.Technologies[technology.Id];
                    var currentLevel = _simulation.Technology.Level(
                        War3HumanScenario.PlayerId, technology.Id);
                    var nextName = presentation.NameForLevel(currentLevel);
                    result.Add(new War3CommandSnapshot(
                        slot++, War3CommandKind.Research, technology.Id,
                        nextName,
                        $"研究 {nextName} " +
                        $"({currentLevel + 1}/{technology.MaximumLevel}) · " +
                        $"{technology.Cost.Minerals} 黄金 / " +
                        $"{technology.Cost.VespeneGas} 木材 / " +
                        $"{technology.ResearchSeconds:0.#} 秒\n" +
                        presentation.Description,
                        presentation.IconPathForLevel(currentLevel),
                        Hotkey(slot - 1)));
                }
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
        _pendingAbilityId = -1;
        _pendingAbilityCaster = -1;
        _pendingBuilding = -1;
        _buildMenu = false;
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
        _pendingAbilityId >= 0 || _pendingBuilding >= 0;

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
        if (direction.Y >= -0.0001f) return false;
        if (_terrain is null)
        {
            var distance = -origin.Y / direction.Y;
            if (distance < 0f) return false;
            point = SimPlane3DTransform.ToSimulation(origin + direction * distance);
            return _simulation?.World.Bounds.Contains(point) == true;
        }

        var maximumHeight = SimPlane3DTransform.ToWorldLength(
            (_terrain.MaximumCellLevel + 1f) * _terrain.CliffLevelHeight);
        var begin = MathF.Max(0f, (maximumHeight - origin.Y) / direction.Y);
        var end = -origin.Y / direction.Y;
        if (end < begin) (begin, end) = (end, begin);
        var previousDistance = begin;
        var previousDelta = HeightDelta(origin + direction * previousDistance);
        const int steps = 192;
        for (var step = 1; step <= steps; step++)
        {
            var distance = begin + (end - begin) * step / steps;
            var delta = HeightDelta(origin + direction * distance);
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
            return worldPoint.Y - GroundWorldHeight(simulationPoint);
        }
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
            _hud?.BlocksWorldPointer(point) == true;
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
        _hud.QueueItemCancelRequested += CancelQueueItem;
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
            objectData.AppliedTechnologyCount == 15 &&
            War3HumanContent.DataCatalog.TryGet("hpea", out var peasantData) &&
            peasantData.Summary.Sight.Day is > 0f &&
            MathF.Abs(peasantProfile.Perception.VisionRange -
                      peasantData.Summary.Sight.Day.Value *
                      War3GameplayImportPolicy.Default.WorldDistanceScale) < 0.01f &&
            flyingProfile.Perception.TerrainVisionMode ==
                TerrainVisionMode.Elevated &&
            _simulation.CombatWeaponUpgradeTechnologyId == 0 &&
            _simulation.CombatBuildingArmorTechnologyId == 2 &&
            War3HumanContent.Technologies.Count == 15;
        var abilityIntegrationReady =
            War3HumanContent.AbilityImportStatus.Catalog.Count == 44 &&
            War3HumanContent.AbilityImportStatus.MissingObjectIds.Length == 0 &&
            _smokeAbilityCastsIssued && _smokeAbilityDamageApplied &&
            _smokeSummonedUnit >= 0 &&
            _simulation.Units.Alive[_smokeSummonedUnit] &&
            _simulation.Abilities.TrySummonedObjectId(
                _smokeSummonedUnit, out var summonedObjectId) &&
            summonedObjectId is "hwat" or "hwt2" or "hwt3" &&
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
                      _hud.MinimapAspectFitReady &&
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
                       _presenter.RallyMarkerUsesWar3Model && attackFacingValid &&
                        constructionPresentationValid && terrainReady &&
                         dataIntegrationReady && mapLoadingValid &&
                         abilityIntegrationReady &&
                         queueUiValid && queueCancelValid &&
                         researchQueueUiValid;
        GD.Print(
            $"WAR3_RTS_SMOKE success={success} units={_presenter.PresentedUnitCount} " +
             $"buildings={_presenter.PresentedBuildingCount} resources={_presenter.PresentedResourceCount} " +
             $"effects={_presenter.ActiveEffectCount} peak_effects={_presenter.PeakEffectCount} " +
             $"projectile_next={_simulation.CombatProjectiles.NextId} " +
            $"portrait={_hud.PortraitReady} hud_layout={_hud.ConsoleLayoutReady} " +
            $"minimap_fit={_hud.MinimapAspectFitReady} " +
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
             $"construction_ghost={_presenter.SawConstructionGhost} " +
             $"foundation_after_arrival={_presenter.FoundationAppearedAfterApproach} " +
             $"placement_model={_presenter.PointerPreviewUsesWar3Model} " +
             $"animation_blend={_presenter.SawBlendedTransition} " +
            $"rally_model={_presenter.RallyMarkerUsesWar3Model} " +
             $"attack_facing={attackFacingValid} terrain={terrainReady} " +
             $"data_integration={dataIntegrationReady} " +
             $"ability_integration={abilityIntegrationReady}/" +
             $"{_simulation.AbilityEvents.LatestSequence} " +
             $"map_loading={mapLoadingValid}/{_loadingStageUpdateCount} " +
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
        _smokeAbilityDamageApplied =
            _simulation.Combat.Health[skillTarget] < targetHealth;
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

    private enum TargetMode : byte
    {
        Move,
        AttackMove,
        Rally
    }
}
