using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

/// <summary>
/// Presentation-only navigation diagnostics for the Warcraft skirmish. The
/// debugger reads authoritative state but never changes pathfinding rules.
/// </summary>
internal sealed partial class War3NavigationDebugger : Node3D
{
    private const int MaximumEvents = 14;
    private const int MaximumTracePoints = 512;
    private const float OverlayHeight = 0.13f;
    private const float MarkerRadius = 13f;
    private const float TraceMinimumDistanceSquared = 4f;

    private static readonly Color PathColor = new("61ff78");
    private static readonly Color RouteColor = new("d982ff");
    private static readonly Color MoveGoalColor = new("ffd75f");
    private static readonly Color SlotTargetColor = new("42e6ff");
    private static readonly Color VelocityColor = new("52ffb8");
    private static readonly Color PreferredVelocityColor = new("69a7ff");
    private static readonly Color CollisionColor = new("ff5b61");
    private static readonly Color ProblemColor = new("ff9f43");
    private static readonly Color FatalProblemColor = new("ff3355");
    private static readonly Color StaticObstacleColor = new(1f, 0.28f, 0.25f, 0.78f);
    private static readonly Color DynamicObstacleColor = new(1f, 0.68f, 0.18f, 0.86f);
    private static readonly Color ConstructionReservationColor =
        new(0.12f, 0.78f, 1f, 0.88f);
    private static readonly Color ConstructionAccessColor =
        new(0.35f, 1f, 0.72f, 0.92f);
    private static readonly Color GridBlockColor = new(1f, 0.18f, 0.22f, 0.72f);
    private static readonly Color GridComponentColor = new(0.82f, 0.35f, 1f, 0.82f);
    private static readonly Color GridWalkableColor =
        new(0.18f, 0.82f, 0.92f, 0.18f);
    private static readonly Color GridBlockedCellColor =
        new(1f, 0.18f, 0.22f, 0.34f);
    private static readonly Color[] AllUnitPathColors =
    [
        new(0.25f, 0.85f, 1f, 0.58f),
        new(1f, 0.42f, 0.34f, 0.58f),
        new(0.82f, 0.48f, 1f, 0.58f),
        new(1f, 0.82f, 0.26f, 0.58f)
    ];

    private readonly Queue<string> _events = new();
    private readonly Queue<NVector2> _trace = new();
    private RtsSimulation? _simulation;
    private ITerrainMapQuery? _terrain;
    private int[] _selectedUnits = [];
    private int _traceUnit = -1;
    private UnitObservedState? _lastObserved;
    private long _traceUntilTick = -1;
    private ImmediateMesh? _overlayMesh;
    private MeshInstance3D? _overlayVisual;
    private ImmediateMesh? _gridMesh;
    private MeshInstance3D? _gridVisual;
    private StandardMaterial3D? _lineMaterial;
    private NavigationConnectivitySnapshot? _connectivity;
    private DynamicFootprint[] _dynamicFootprints = [];
    private int _dynamicFootprintRevision = -1;
    private int _gridRevision = -1;
    private MovementClass _gridClass = MovementClass.Small;
    private bool _surfaceOpen;
    private bool _active;
    private bool _paused;
    private bool _stepRequested;
    private bool _showPaths = true;
    private bool _showRoutes = true;
    private bool _showAllUnitPaths;
    private bool _showGoals = true;
    private bool _showVelocities = true;
    private bool _showProblems = true;
    private bool _showObstacles;
    private bool _showBuildings;
    private bool _showGrid;
    private bool _showTrace = true;
    private bool _profilingWasEnabled;
    private bool _panelDragging;
    private Vector2 _panelDragPointerStart;
    private Vector2 _panelDragPanelStart;
    private double _panelRefreshAccumulator;
    private Control? _uiRoot;
    private PanelContainer? _panel;
    private Button? _launcherButton;
    private RichTextLabel? _summary;
    private RichTextLabel? _eventHistory;
    private Label? _exportStatus;
    private Button? _pauseButton;
    private CheckButton? _gridToggle;

    public bool Active => _active;

    public void Initialize(
        RtsSimulation simulation,
        ITerrainMapQuery terrain,
        bool initiallyVisible,
        bool initiallyShowGrid = false)
    {
        _simulation = simulation;
        _terrain = terrain;
        CreateWorldVisuals();
        CreatePanel();
        if (initiallyShowGrid)
        {
            _showGrid = true;
            if (_gridToggle is not null) _gridToggle.ButtonPressed = true;
        }
        SetActive(initiallyVisible || initiallyShowGrid);
    }

    public bool HandleHotkey(Key key)
    {
        switch (key)
        {
            case Key.F7:
                SetActive(!_active);
                return true;
            case Key.F9 when _active:
                SetPaused(!_paused);
                return true;
            case Key.F10 when _active:
                RequestSingleStep();
                return true;
            default:
                return false;
        }
    }

    public bool BlocksWorldPointer(Vector2 viewportPosition) =>
        (_active &&
         _panel?.GetGlobalRect().HasPoint(viewportPosition) == true) ||
        (!_active &&
         _launcherButton?.GetGlobalRect().HasPoint(viewportPosition) == true);

    public void SetSelection(IEnumerable<int> units)
    {
        var selected = units.OrderBy(value => value).ToArray();
        var primary = selected.Length == 0 ? -1 : selected[0];
        if (primary != _traceUnit)
        {
            _traceUnit = primary;
            _trace.Clear();
            _lastObserved = null;
            if (primary >= 0 && _simulation is not null)
                AddEvent($"T{_simulation.Metrics.Tick}  跟踪单位切换为 u{primary}");
        }
        _selectedUnits = selected;
        if (_active) RefreshPanel();
    }

    public bool ConsumeAdvanceSimulation()
    {
        if (!_active || !_paused) return true;
        if (!_stepRequested) return false;
        _stepRequested = false;
        return true;
    }

    public void SamplePhysics(double delta)
    {
        if (_simulation is null ||
            (!_active && _simulation.Metrics.Tick > _traceUntilTick))
            return;
        SamplePrimaryUnit();
        if (!_active) return;
        _panelRefreshAccumulator += delta;
        if (_panelRefreshAccumulator < 0.1d) return;
        _panelRefreshAccumulator = 0d;
        RefreshPanel();
    }

    public void RecordSmartCommand(
        NVector2 clickedPoint,
        in SmartCommandTarget target,
        int[] units,
        bool queued,
        bool succeeded,
        string commandCode)
    {
        if (_simulation is null) return;
        _traceUntilTick = Math.Max(
            _traceUntilTick,
            _simulation.Metrics.Tick + 3_600);
        _lastObserved = null;
        var store = _simulation.Units;
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            if ((uint)unit >= (uint)store.Count || !store.Alive[unit])
                continue;
            var position = store.Positions[unit];
            var navigationRadius = store.NavigationRadii[unit];
            var slot = store.SlotTargets[unit];
            var path = store.Paths[unit];
            GD.Print(
                $"WAR3_NAV_COMMAND tick={_simulation.Metrics.Tick} " +
                $"u={unit} queued={queued} accepted={succeeded}/{commandCode} " +
                $"click={Point(clickedPoint)} target={target.Kind}@{Point(target.Position)} " +
                $"targetIds={target.Unit}/{target.Building}/{target.ResourceNode} " +
                $"pos={Point(position)} physical={store.Radii[unit]:0.##} " +
                $"navigation={navigationRadius:0.##} " +
                $"startFree={_simulation.World.IsDiscFree(position, navigationRadius)} " +
                $"goal={Point(store.MoveGoals[unit])} slot={Point(slot)} " +
                $"slotFree={_simulation.World.IsDiscFree(slot, navigationRadius)} " +
                $"mode={store.Modes[unit]} leg={store.MovementLegResults[unit]} " +
                $"pending={store.PathPending[unit]} " +
                $"path={(path?.Cursor ?? -1)}/{(path?.Points.Length ?? 0)} " +
                $"route={store.RouteWaypoints[unit].Length} " +
                $"choke={store.ActiveChokeIds[unit]}/" +
                $"{store.ChokeDirections[unit]}/" +
                $"{store.ChokePhases[unit]}/" +
                $"admitted={store.ChokeAdmitted[unit]} " +
                $"chokeWait={store.ChokeWaitTicks[unit]} " +
                $"recovery={store.RecoveryStages[unit]}");
        }
        SamplePrimaryUnit();
    }

    public override void _Process(double delta)
    {
        if (!_active || _simulation is null) return;
        SyncGrid();
        SyncOverlay();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!_panelDragging || _panel is null) return;
        if (inputEvent is InputEventMouseButton button &&
            button.ButtonIndex == MouseButton.Left && !button.Pressed)
        {
            _panelDragging = false;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (inputEvent is not InputEventMouseMotion) return;
        var pointer = GetViewport().GetMousePosition();
        var desired = _panelDragPanelStart + pointer - _panelDragPointerStart;
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var maximum = new Vector2(
            Mathf.Max(0f, viewportSize.X - _panel.Size.X),
            Mathf.Max(0f, viewportSize.Y - _panel.Size.Y));
        _panel.Position = new Vector2(
            Mathf.Clamp(desired.X, 0f, maximum.X),
            Mathf.Clamp(desired.Y, 0f, maximum.Y));
        GetViewport().SetInputAsHandled();
    }

    private void HandlePanelHeaderInput(InputEvent inputEvent)
    {
        if (_panel is null ||
            inputEvent is not InputEventMouseButton button ||
            button.ButtonIndex != MouseButton.Left || !button.Pressed)
            return;
        _panelDragging = true;
        _panelDragPointerStart = GetViewport().GetMousePosition();
        _panelDragPanelStart = _panel.Position;
        GetViewport().SetInputAsHandled();
    }

    private void SetActive(bool active)
    {
        var wasActive = _active;
        _active = active;
        if (_simulation is not null)
        {
            if (active && !wasActive)
            {
                _profilingWasEnabled = _simulation.DetailedProfilingEnabled;
                _simulation.DetailedProfilingEnabled = true;
            }
            else if (!active && wasActive)
            {
                _simulation.DetailedProfilingEnabled = _profilingWasEnabled;
            }
        }
        if (_uiRoot is not null) _uiRoot.Visible = true;
        if (_panel is not null) _panel.Visible = active;
        if (_launcherButton is not null) _launcherButton.Visible = !active;
        if (_overlayVisual is not null) _overlayVisual.Visible = active;
        if (_gridVisual is not null) _gridVisual.Visible = active && _showGrid;
        if (!active)
        {
            _panelDragging = false;
            SetPaused(false);
            _overlayMesh?.ClearSurfaces();
            _gridVisual?.Hide();
        }
        else
        {
            _gridRevision = -1;
            RefreshPanel();
        }
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        _stepRequested = false;
        if (_pauseButton is not null)
            _pauseButton.Text = paused ? "继续 [F9]" : "暂停 [F9]";
        if (_active && _simulation is not null)
            AddEvent($"T{_simulation.Metrics.Tick}  {(paused ? "模拟暂停" : "模拟继续")}");
    }

    private void RequestSingleStep()
    {
        if (!_paused) SetPaused(true);
        _stepRequested = true;
        if (_simulation is not null)
            AddEvent($"T{_simulation.Metrics.Tick}  请求单步");
    }

    private void SamplePrimaryUnit()
    {
        if (_simulation is null || _traceUnit < 0 ||
            (uint)_traceUnit >= (uint)_simulation.Units.Count ||
            !_simulation.Units.Alive[_traceUnit])
        {
            return;
        }

        var units = _simulation.Units;
        var unit = _traceUnit;
        var position = units.Positions[unit];
        if (_trace.Count == 0 ||
            NVector2.DistanceSquared(_trace.Last(), position) >=
            TraceMinimumDistanceSquared)
        {
            _trace.Enqueue(position);
            while (_trace.Count > MaximumTracePoints) _trace.Dequeue();
        }

        var path = units.Paths[unit];
        var observed = new UnitObservedState(
            units.Modes[unit],
            units.MovementLegResults[unit],
            units.RecoveryStages[unit],
            units.CommandVersions[unit],
            units.PathPending[unit],
            path?.Cursor ?? -1,
            path?.Points.Length ?? 0,
            units.BlockedByNavigation[unit],
            units.ActiveChokeIds[unit],
            units.ChokePhases[unit],
            units.DestinationYieldPhases[unit],
            units.DestinationOverflowed[unit]);
        if (_lastObserved is { } previous && previous != observed)
            AddEvent(DescribeTransition(_simulation.Metrics.Tick, unit, previous, observed));
        _lastObserved = observed;
    }

    private static string DescribeTransition(
        long tick,
        int unit,
        UnitObservedState previous,
        UnitObservedState current)
    {
        var changes = new List<string>(6);
        if (previous.Mode != current.Mode)
            changes.Add($"mode {previous.Mode}→{current.Mode}");
        if (previous.Result != current.Result)
            changes.Add($"leg {previous.Result}→{current.Result}");
        if (previous.Recovery != current.Recovery)
            changes.Add($"recovery {previous.Recovery}→{current.Recovery}");
        if (previous.CommandVersion != current.CommandVersion)
            changes.Add($"cmd {previous.CommandVersion}→{current.CommandVersion}");
        if (previous.PathPending != current.PathPending)
            changes.Add($"pending {previous.PathPending}→{current.PathPending}");
        if (previous.PathCursor != current.PathCursor ||
            previous.PathPoints != current.PathPoints)
        {
            changes.Add(
                $"path {previous.PathCursor}/{previous.PathPoints}→" +
                $"{current.PathCursor}/{current.PathPoints}");
        }
        if (previous.Blocked != current.Blocked)
            changes.Add($"blocked {previous.Blocked}→{current.Blocked}");
        if (previous.ChokeId != current.ChokeId || previous.Choke != current.Choke)
            changes.Add(
                $"choke {previous.ChokeId}/{previous.Choke}→" +
                $"{current.ChokeId}/{current.Choke}");
        if (previous.Yield != current.Yield)
            changes.Add($"yield {previous.Yield}→{current.Yield}");
        if (previous.Overflowed != current.Overflowed)
            changes.Add($"overflow {previous.Overflowed}→{current.Overflowed}");
        return $"T{tick}  u{unit}: {string.Join(" | ", changes)}";
    }

    private void AddEvent(string value)
    {
        _events.Enqueue(value);
        while (_events.Count > MaximumEvents) _events.Dequeue();
        GD.Print($"WAR3_NAV_TRACE {value}");
        RefreshEventHistory();
    }

    private void CreateWorldVisuals()
    {
        _lineMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
        _overlayMesh = new ImmediateMesh();
        _overlayVisual = new MeshInstance3D
        {
            Name = "War3NavigationUnitOverlay",
            Mesh = _overlayMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_overlayVisual);
        _gridMesh = new ImmediateMesh();
        _gridVisual = new MeshInstance3D
        {
            Name = "War3NavigationConnectivityOverlay",
            Mesh = _gridMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        AddChild(_gridVisual);
    }

    private void CreatePanel()
    {
        var layer = new CanvasLayer { Name = "War3NavigationDebugUi", Layer = 90 };
        AddChild(layer);
        _uiRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _uiRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_uiRoot);

        _launcherButton = new Button
        {
            Name = "NavigationGmLauncher",
            Text = "GM 导航 [F7]",
            MouseFilter = Control.MouseFilterEnum.Stop,
            OffsetLeft = -132f,
            OffsetRight = -16f,
            OffsetTop = 64f,
            OffsetBottom = 100f
        };
        _launcherButton.AnchorLeft = 1f;
        _launcherButton.AnchorRight = 1f;
        _launcherButton.AddThemeFontSizeOverride("font_size", 13);
        _launcherButton.Pressed += () => SetActive(true);
        _uiRoot.AddChild(_launcherButton);

        _panel = new PanelContainer
        {
            Name = "NavigationDebugPanel",
            MouseFilter = Control.MouseFilterEnum.Stop,
            OffsetLeft = -652f,
            OffsetRight = -16f,
            OffsetTop = 16f,
            OffsetBottom = -16f,
            Visible = false
        };
        _panel.AnchorLeft = 1f;
        _panel.AnchorRight = 1f;
        _panel.AnchorBottom = 1f;
        var background = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.045f, 0.06f, 0.96f),
            BorderColor = new Color(0.18f, 0.72f, 0.88f, 0.9f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7
        };
        _panel.AddThemeStyleboxOverride("panel", background);
        _panel.AddThemeFontSizeOverride("font_size", 13);
        _uiRoot.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);

        var header = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Drag
        };
        header.GuiInput += HandlePanelHeaderInput;
        column.AddChild(header);
        var title = new Label
        {
            Text = "GM / 导航诊断  [F7]",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color("72e6ff"));
        header.AddChild(title);
        var close = new Button { Text = "关闭" };
        close.Pressed += () => SetActive(false);
        header.AddChild(close);

        var controls = new HBoxContainer();
        column.AddChild(controls);
        _pauseButton = new Button { Text = "暂停 [F9]" };
        _pauseButton.Pressed += () => SetPaused(!_paused);
        controls.AddChild(_pauseButton);
        var step = new Button { Text = "单步 [F10]" };
        step.Pressed += RequestSingleStep;
        controls.AddChild(step);
        var clearTrace = new Button { Text = "清轨迹" };
        clearTrace.Pressed += () =>
        {
            _trace.Clear();
            AddEvent($"T{_simulation?.Metrics.Tick ?? 0}  已清空轨迹");
        };
        controls.AddChild(clearTrace);
        var export = new Button { Text = "导出 JSON" };
        export.Pressed += ExportSnapshot;
        controls.AddChild(export);
        var copyLogPath = new Button { Text = "复制日志路径" };
        copyLogPath.Pressed += CopyRuntimeLogPath;
        controls.AddChild(copyLogPath);

        var toggles = new GridContainer { Columns = 4 };
        column.AddChild(toggles);
        toggles.AddChild(Toggle("选中单位路径", _showPaths, value => _showPaths = value));
        toggles.AddChild(Toggle("选中高层路线", _showRoutes, value => _showRoutes = value));
        toggles.AddChild(Toggle(
            "全部单位路线", _showAllUnitPaths,
            value => _showAllUnitPaths = value));
        toggles.AddChild(Toggle("目标/槽位", _showGoals, value => _showGoals = value));
        toggles.AddChild(Toggle("速度向量", _showVelocities, value => _showVelocities = value));
        toggles.AddChild(Toggle("异常单位", _showProblems, value => _showProblems = value));
        toggles.AddChild(Toggle("静态阻挡", _showObstacles, value => _showObstacles = value));
        toggles.AddChild(Toggle("建筑真实占地", _showBuildings, value => _showBuildings = value));
        toggles.AddChild(Toggle("移动轨迹", _showTrace, value => _showTrace = value));
        _gridToggle = Toggle("运行时导航网格", _showGrid, value =>
        {
            _showGrid = value;
            _gridRevision = -1;
            if (_gridVisual is not null) _gridVisual.Visible = _active && value;
        });
        toggles.AddChild(_gridToggle);

        var gridOptions = new HBoxContainer();
        column.AddChild(gridOptions);
        gridOptions.AddChild(new Label { Text = "净空层" });
        var movementClass = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        movementClass.AddItem("Small", (int)MovementClass.Small);
        movementClass.AddItem("Medium", (int)MovementClass.Medium);
        movementClass.AddItem("Large", (int)MovementClass.Large);
        movementClass.ItemSelected += index =>
        {
            _gridClass = (MovementClass)movementClass.GetItemId((int)index);
            _gridRevision = -1;
        };
        gridOptions.AddChild(movementClass);
        var refreshGrid = new Button { Text = "刷新连通层" };
        refreshGrid.Pressed += () =>
        {
            _showGrid = true;
            _gridToggle!.ButtonPressed = true;
            _gridRevision = -1;
        };
        gridOptions.AddChild(refreshGrid);

        _summary = new RichTextLabel
        {
            BbcodeEnabled = true,
            SelectionEnabled = true,
            ScrollActive = true,
            CustomMinimumSize = new Vector2(0f, 240f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _summary.AddThemeFontSizeOverride("normal_font_size", 12);
        column.AddChild(_summary);

        column.AddChild(new Label { Text = "状态变迁（主选单位）" });
        _eventHistory = new RichTextLabel
        {
            BbcodeEnabled = true,
            SelectionEnabled = true,
            ScrollActive = true,
            CustomMinimumSize = new Vector2(0f, 84f)
        };
        _eventHistory.AddThemeFontSizeOverride("normal_font_size", 11);
        column.AddChild(_eventHistory);
        _exportStatus = new Label
        {
            Text = "GM 按钮/F7 开关 · F9 暂停 · F10 单步",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _exportStatus.AddThemeColorOverride("font_color", new Color("8eb7c2"));
        column.AddChild(_exportStatus);
    }

    private static CheckButton Toggle(
        string text,
        bool initial,
        Action<bool> changed)
    {
        var toggle = new CheckButton
        {
            Text = text,
            ButtonPressed = initial
        };
        toggle.Toggled += value => changed(value);
        return toggle;
    }

    private void RefreshPanel()
    {
        if (!_active || _simulation is null || _summary is null) return;
        var metrics = _simulation.Metrics;
        var units = _simulation.Units;
        var blocked = 0;
        var recovering = 0;
        var stalled = 0;
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (!units.Alive[unit]) continue;
            if (units.BlockedByNavigation[unit]) blocked++;
            if (units.RecoveryStages[unit] != RecoveryStage.Normal) recovering++;
            if (units.DestinationStallTicks[unit] > 60 ||
                units.DynamicBlockageTicks[unit] > 30)
                stalled++;
        }

        var lines = new List<string>(32)
        {
            $"[color=#72e6ff]Tick {metrics.Tick}[/color]   " +
            $"{(_paused ? "[color=#ffcb65]PAUSED[/color]" : "RUNNING")}   " +
            $"nav-revision={metrics.NavigationRevision}",
            $"单位 moving/wait/unreachable = {metrics.MovingUnits}/" +
            $"{metrics.WaitingForPathUnits}/{metrics.UnreachableUnits}   " +
            $"blocked/recovery/stall = {blocked}/{recovering}/{stalled}",
            $"路径队列 {metrics.PendingPathRequests}/{_simulation.PathBudgetPerTick}   " +
            $"本帧 ok/fail/repath = {metrics.PathsCompleted}/" +
            $"{metrics.PathsFailed}/{metrics.RepathRequests}",
            $"path {metrics.PathMilliseconds:0.###}ms  direct/search/simplify = " +
            $"{metrics.PathDirectCheckMilliseconds:0.###}/" +
            $"{metrics.PathSearchMilliseconds:0.###}/" +
            $"{metrics.PathSimplificationMilliseconds:0.###}ms",
            $"A* expanded={metrics.PathExpandedNodes}  points=" +
            $"{metrics.PathRawPoints}→{metrics.PathSimplifiedPoints}  " +
            $"alloc={metrics.PathAllocatedBytes:N0}B",
            $"connectivity full/incremental={metrics.PathFullConnectivityRebuilds}/" +
            $"{metrics.PathIncrementalConnectivityUpdates}  refresh=" +
            $"{metrics.PathConnectivityRefreshMilliseconds:0.###}ms",
            $"碰撞 pairs={metrics.CollisionPairs}  penetration=" +
            $"{metrics.MaximumPenetration:0.###}  yields=" +
            $"{metrics.ActiveDestinationYields}  recovery-events={metrics.RecoveryEvents}",
            string.Empty,
            $"[color=#9fe5ff]选中单位 {_selectedUnits.Length}[/color]"
        };

        foreach (var unit in _selectedUnits.Take(8))
        {
            if ((uint)unit >= (uint)units.Count || !units.Alive[unit]) continue;
            var path = units.Paths[unit];
            lines.Add(
                $"u{unit}  {units.Modes[unit]}/{units.RecoveryStages[unit]}  " +
                $"path={(path is null ? "-" : $"{path.Cursor}/{path.Points.Length}")}  " +
                $"group={units.MovementGroupIds[unit]}/{units.MovementGroupSizes[unit]}  " +
                $"stall={units.DestinationStallTicks[unit]}/" +
                $"{units.DynamicBlockageTicks[unit]}");
        }

        var primary = PrimaryUnit();
        if (primary >= 0)
        {
            var path = units.Paths[primary];
            var position = units.Positions[primary];
            var slot = units.SlotTargets[primary];
            var componentPosition = ComponentAt(position);
            var componentGoal = ComponentAt(slot);
            lines.Add(string.Empty);
            lines.Add($"[color=#ffe08a]主选 u{primary} / team " +
                      $"{_simulation.Combat.Teams[primary]}[/color]");
            lines.Add($"class={units.MovementClasses[primary]}  radius=" +
                      $"{units.Radii[primary]:0.##}/nav{units.NavigationRadii[primary]:0.##}  " +
                      $"cmd={units.CommandVersions[primary]}");
            lines.Add($"mode={units.Modes[primary]}  goal=" +
                      $"{units.MovementGoalKinds[primary]}/" +
                      $"{units.MovementLegResults[primary]}  pending=" +
                      $"{units.PathPending[primary]}");
            lines.Add($"pos={Point(position)}  goal={Point(units.MoveGoals[primary])}  " +
                      $"slot={Point(slot)}  distance={NVector2.Distance(position, slot):0.##}");
            lines.Add($"velocity={Point(units.Velocities[primary])}  preferred=" +
                      $"{Point(units.PreferredVelocities[primary])}  correction=" +
                      $"{Point(units.CollisionCorrections[primary])}");
            lines.Add($"path={(path is null ? "none" :
                $"cursor {path.Cursor}/{path.Points.Length}, v{path.CommandVersion}")}  " +
                      $"route-waypoints={units.RouteWaypoints[primary].Length}  " +
                      $"component={componentPosition}→{componentGoal}");
            lines.Add($"progress timer/best={units.ProgressTimers[primary]:0.00}/" +
                      $"{units.ProgressBestDistances[primary]:0.0}  repath-cd=" +
                      $"{units.RepathCooldowns[primary]:0.00}");
            lines.Add($"recovery={units.RecoveryStages[primary]}  events/retries=" +
                      $"{units.RecoveryEventCounts[primary]}/" +
                      $"{units.RecoveryRetryCounts[primary]}  nav-blocked=" +
                      $"{units.BlockedByNavigation[primary]}");
            lines.Add($"destination stall/near/dynamic={units.DestinationStallTicks[primary]}/" +
                      $"{units.DestinationNearTicks[primary]}/" +
                      $"{units.DynamicBlockageTicks[primary]}  overflow=" +
                      $"{units.DestinationOverflowed[primary]}");
            lines.Add($"yield={units.DestinationYieldPhases[primary]} for=" +
                      $"u{units.DestinationYieldForUnits[primary]}  choke=" +
                      $"{units.ActiveChokeIds[primary]}/{units.ChokePhases[primary]}  " +
                      $"admitted={units.ChokeAdmitted[primary]} rank=" +
                      $"{units.ChokeQueueRanks[primary]} wait={units.ChokeWaitTicks[primary]}");
            if (_simulation.Construction.TryObserveAssignedBuilding(
                    primary, out var construction))
            {
                lines.Add($"construction=b{construction.Id.Value}/" +
                          $"{construction.State} progress={construction.Progress:P0}  " +
                          $"reservation={construction.ReservationId.Value} " +
                          $"footprint={construction.FootprintId.Value}");
                lines.Add($"foundation={Rect(construction.Bounds)}  access=" +
                          $"{Point(construction.AccessPoint)} distance=" +
                          $"{NVector2.Distance(position, construction.AccessPoint):0.##}");
            }
        }
        else
        {
            lines.Add("点选一个单位查看完整路径和恢复状态。");
        }

        if (_connectivity is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"净空层 {_gridClass}: {_connectivity.Columns}×" +
                      $"{_connectivity.Rows}, components={_connectivity.ComponentCount}, " +
                      $"source={_connectivity.Source}, revision={_connectivity.WorldRevision}");
        }
        _summary.Text = string.Join('\n', lines);
        RefreshEventHistory();
    }

    private void RefreshEventHistory()
    {
        if (_eventHistory is null) return;
        _eventHistory.Text = _events.Count == 0
            ? "尚无状态变迁。"
            : string.Join('\n', _events.Select(value => $"• {value}"));
    }

    private void SyncOverlay()
    {
        if (_simulation is null || _overlayMesh is null ||
            _overlayVisual is null || _lineMaterial is null)
            return;
        _overlayMesh.ClearSurfaces();
        _overlayVisual.Visible = true;
        _surfaceOpen = false;

        if (_showTrace && _trace.Count > 1)
        {
            var first = true;
            var previous = default(NVector2);
            foreach (var point in _trace)
            {
                if (!first) AppendLine(previous, point, new Color(0.3f, 1f, 0.9f, 0.58f));
                previous = point;
                first = false;
            }
        }

        if (_showAllUnitPaths) DrawAllUnitPaths();
        foreach (var unit in _selectedUnits)
            DrawSelectedUnit(unit);
        if (_showProblems) DrawProblemUnitsAndChokes();
        if (_showObstacles) DrawObstacles();
        if (_showBuildings) DrawBuildings();
        if (_surfaceOpen) _overlayMesh.SurfaceEnd();
    }

    private void DrawAllUnitPaths()
    {
        if (_simulation is null) return;
        var units = _simulation.Units;
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (!units.Alive[unit] || _selectedUnits.Contains(unit)) continue;
            var color = AllUnitPathColors[
                Math.Abs(_simulation.Combat.Teams[unit]) %
                AllUnitPathColors.Length];
            var position = units.Positions[unit];
            if (units.Paths[unit] is { } path)
            {
                var previous = position;
                var cursor = Math.Clamp(path.Cursor, 0, path.Points.Length);
                for (var index = cursor; index < path.Points.Length; index++)
                {
                    AppendLine(previous, path.Points[index], color);
                    previous = path.Points[index];
                }
            }
            var routeColor = new Color(color.R, color.G, color.B, 0.34f);
            var routePrevious = position;
            foreach (var waypoint in units.RouteWaypoints[unit])
            {
                AppendLine(routePrevious, waypoint, routeColor);
                routePrevious = waypoint;
            }
        }
    }

    private void DrawSelectedUnit(int unit)
    {
        if (_simulation is null || (uint)unit >= (uint)_simulation.Units.Count ||
            !_simulation.Units.Alive[unit])
            return;
        var units = _simulation.Units;
        var position = units.Positions[unit];
        if (_showPaths && units.Paths[unit] is { } path)
        {
            var previous = position;
            var cursor = Math.Clamp(path.Cursor, 0, path.Points.Length);
            for (var index = cursor; index < path.Points.Length; index++)
            {
                AppendLine(previous, path.Points[index], PathColor);
                AppendMarker(path.Points[index], 6f, PathColor);
                previous = path.Points[index];
            }
        }
        if (_showRoutes)
        {
            var previous = position;
            foreach (var waypoint in units.RouteWaypoints[unit])
            {
                AppendLine(previous, waypoint, RouteColor);
                AppendMarker(waypoint, 9f, RouteColor);
                previous = waypoint;
            }
        }
        if (_showGoals)
        {
            AppendCircle(
                position,
                MathF.Max(1f, units.NavigationRadii[unit]),
                new Color(0.38f, 1f, 0.48f, 0.66f));
            AppendLine(position, units.MoveGoals[unit], MoveGoalColor);
            AppendMarker(units.MoveGoals[unit], MarkerRadius, MoveGoalColor);
            AppendLine(position, units.SlotTargets[unit], SlotTargetColor);
            AppendMarker(units.SlotTargets[unit], MarkerRadius * 0.75f, SlotTargetColor);
            if (units.MovementGoalRadii[unit] > 0f)
            {
                AppendCircle(
                    units.SlotTargets[unit],
                    units.MovementGoalRadii[unit],
                    new Color(0.26f, 0.9f, 1f, 0.68f));
            }
            var goalBounds = units.MovementGoalBounds[unit];
            if (goalBounds.Width > 0f && goalBounds.Height > 0f)
                AppendRectangle(goalBounds, new Color(1f, 0.85f, 0.36f, 0.72f));
        }
        if (_showVelocities)
        {
            AppendLine(
                position,
                position + units.Velocities[unit] * 0.30f,
                VelocityColor);
            AppendLine(
                position,
                position + units.PreferredVelocities[unit] * 0.30f,
                PreferredVelocityColor);
            AppendLine(
                position,
                position + units.CollisionCorrections[unit] * 8f,
                CollisionColor);
        }
        AppendMarker(
            position,
            MathF.Max(MarkerRadius * 0.6f, units.Radii[unit]),
            RecoveryColor(units.RecoveryStages[unit]));
    }

    private void DrawProblemUnitsAndChokes()
    {
        if (_simulation is null) return;
        var units = _simulation.Units;
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (!units.Alive[unit] || !IsProblemUnit(unit)) continue;
            var fatal = units.RecoveryStages[unit] == RecoveryStage.Unreachable ||
                        units.MovementLegResults[unit] ==
                        UnitMovementLegResult.Unreachable;
            AppendMarker(
                units.Positions[unit], MarkerRadius * 1.35f,
                fatal ? FatalProblemColor : ProblemColor);
        }
        if (_simulation.ChokeController is null) return;
        foreach (var choke in _simulation.ChokeController.Definitions)
        {
            AppendLine(choke.A, choke.B, new Color(0.72f, 0.42f, 1f, 0.72f));
            AppendMarker(choke.A, 8f, RouteColor);
            AppendMarker(choke.B, 8f, RouteColor);
        }
    }

    private bool IsProblemUnit(int unit)
    {
        var units = _simulation!.Units;
        return units.BlockedByNavigation[unit] ||
               units.PathPending[unit] ||
               units.Modes[unit] == UnitMoveMode.WaitingForPath ||
               units.RecoveryStages[unit] != RecoveryStage.Normal ||
               units.MovementLegResults[unit] == UnitMovementLegResult.Unreachable ||
               units.DestinationOverflowed[unit] ||
               units.DestinationStallTicks[unit] > 60 ||
               units.DynamicBlockageTicks[unit] > 30;
    }

    private void DrawObstacles()
    {
        if (_simulation is null) return;
        var obstacles = _simulation.World.Obstacles;
        for (var index = 0; index < obstacles.Length; index++)
            AppendRectangle(obstacles[index], StaticObstacleColor);
    }

    private void DrawBuildings()
    {
        if (_simulation is null) return;
        if (_dynamicFootprintRevision != _simulation.World.NavigationRevision)
        {
            _dynamicFootprints = _simulation.World.DynamicOccupancy.Snapshot();
            _dynamicFootprintRevision = _simulation.World.NavigationRevision;
        }
        foreach (var footprint in _dynamicFootprints)
            AppendRectangle(footprint.Bounds, DynamicObstacleColor);
        foreach (var building in _simulation.Construction.CreateOverview())
        {
            if (building.IsTerminal) continue;
            AppendRectangle(
                building.Bounds,
                building.FootprintId.Value > 0
                    ? DynamicObstacleColor
                    : ConstructionReservationColor);
            var bottomRight = new NVector2(
                building.Bounds.Max.X, building.Bounds.Min.Y);
            var topLeft = new NVector2(
                building.Bounds.Min.X, building.Bounds.Max.Y);
            AppendLine(building.Bounds.Min, building.Bounds.Max,
                building.FootprintId.Value > 0
                    ? DynamicObstacleColor
                    : ConstructionReservationColor);
            AppendLine(bottomRight, topLeft,
                building.FootprintId.Value > 0
                    ? DynamicObstacleColor
                    : ConstructionReservationColor);
            if (building.State != BuildingLifecycleState.Completed)
            {
                AppendMarker(building.AccessPoint, 7f, ConstructionAccessColor);
                if ((uint)building.BuilderUnit < (uint)_simulation.Units.Count &&
                    _simulation.Units.Alive[building.BuilderUnit])
                {
                    AppendLine(
                        _simulation.Units.Positions[building.BuilderUnit],
                        building.AccessPoint,
                        ConstructionAccessColor);
                }
            }
        }
    }

    private void SyncGrid()
    {
        if (_simulation is null || _gridVisual is null || _gridMesh is null ||
            _lineMaterial is null)
            return;
        _gridVisual.Visible = _active && _showGrid;
        if (!_showGrid) return;
        if (_gridRevision == _simulation.World.NavigationRevision &&
            _connectivity?.NavigationRadius ==
            MovementClearance.ForClass(_gridClass).NavigationRadius)
            return;
        _connectivity = _simulation
            .GetNavigationConnectivitySnapshotForDiagnostics(_gridClass);
        _gridRevision = _simulation.World.NavigationRevision;
        RebuildGridMesh();
        RefreshPanel();
    }

    private void RebuildGridMesh()
    {
        _gridMesh!.ClearSurfaces();
        if (_connectivity is null) return;
        var snapshot = _connectivity;
        var open = false;
        void Add(NVector2 from, NVector2 to, Color color)
        {
            if (!open)
            {
                _gridMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMaterial);
                open = true;
            }
            _gridMesh.SurfaceSetColor(color);
            _gridMesh.SurfaceAddVertex(ToWorldAtGround(from, OverlayHeight * 0.65f));
            _gridMesh.SurfaceSetColor(color);
            _gridMesh.SurfaceAddVertex(ToWorldAtGround(to, OverlayHeight * 0.65f));
        }

        for (var row = 0; row < snapshot.Rows; row++)
        for (var column = 0; column < snapshot.Columns; column++)
        {
            var node = row * snapshot.Columns + column;
            var cell = snapshot.CellBounds(node);
            var cellColor = snapshot.IsWalkable(node)
                ? GridWalkableColor
                : GridBlockedCellColor;
            var bottomRight = new NVector2(cell.Max.X, cell.Min.Y);
            var topLeft = new NVector2(cell.Min.X, cell.Max.Y);
            Add(cell.Min, bottomRight, cellColor);
            Add(cell.Min, topLeft, cellColor);
            if (column == snapshot.Columns - 1)
                Add(bottomRight, cell.Max, cellColor);
            if (row == snapshot.Rows - 1)
                Add(topLeft, cell.Max, cellColor);
            if (column + 1 < snapshot.Columns)
            {
                var right = node + 1;
                if (GridTransition(snapshot, node, right, out var color))
                {
                    var x = snapshot.WorldBounds.Min.X + (column + 1) * snapshot.CellSize;
                    var y0 = snapshot.WorldBounds.Min.Y + row * snapshot.CellSize;
                    var y1 = MathF.Min(snapshot.WorldBounds.Max.Y, y0 + snapshot.CellSize);
                    Add(new NVector2(x, y0), new NVector2(x, y1), color);
                }
            }
            if (row + 1 < snapshot.Rows)
            {
                var below = node + snapshot.Columns;
                if (GridTransition(snapshot, node, below, out var color))
                {
                    var y = snapshot.WorldBounds.Min.Y + (row + 1) * snapshot.CellSize;
                    var x0 = snapshot.WorldBounds.Min.X + column * snapshot.CellSize;
                    var x1 = MathF.Min(snapshot.WorldBounds.Max.X, x0 + snapshot.CellSize);
                    Add(new NVector2(x0, y), new NVector2(x1, y), color);
                }
            }
        }
        var bounds = snapshot.WorldBounds;
        Add(bounds.Min, new NVector2(bounds.Max.X, bounds.Min.Y), SlotTargetColor);
        Add(new NVector2(bounds.Max.X, bounds.Min.Y), bounds.Max, SlotTargetColor);
        Add(bounds.Max, new NVector2(bounds.Min.X, bounds.Max.Y), SlotTargetColor);
        Add(new NVector2(bounds.Min.X, bounds.Max.Y), bounds.Min, SlotTargetColor);
        if (open) _gridMesh.SurfaceEnd();
    }

    private static bool GridTransition(
        NavigationConnectivitySnapshot snapshot,
        int left,
        int right,
        out Color color)
    {
        var leftWalkable = snapshot.IsWalkable(left);
        var rightWalkable = snapshot.IsWalkable(right);
        if (leftWalkable != rightWalkable)
        {
            color = GridBlockColor;
            return true;
        }
        if (leftWalkable && snapshot.ComponentAt(left) != snapshot.ComponentAt(right))
        {
            color = GridComponentColor;
            return true;
        }
        color = default;
        return false;
    }

    private void AppendLine(NVector2 from, NVector2 to, Color color)
    {
        EnsureOverlaySurface();
        _overlayMesh!.SurfaceSetColor(color);
        _overlayMesh.SurfaceAddVertex(ToWorldAtGround(from));
        _overlayMesh.SurfaceSetColor(color);
        _overlayMesh.SurfaceAddVertex(ToWorldAtGround(to));
    }

    private void AppendMarker(NVector2 center, float radius, Color color)
    {
        AppendLine(
            center - new NVector2(radius, 0f),
            center + new NVector2(radius, 0f), color);
        AppendLine(
            center - new NVector2(0f, radius),
            center + new NVector2(0f, radius), color);
        var diagonal = radius * 0.55f;
        AppendLine(
            center - new NVector2(diagonal),
            center + new NVector2(diagonal), color);
        AppendLine(
            center + new NVector2(-diagonal, diagonal),
            center + new NVector2(diagonal, -diagonal), color);
    }

    private void AppendRectangle(SimRect bounds, Color color)
    {
        var bottomRight = new NVector2(bounds.Max.X, bounds.Min.Y);
        var topLeft = new NVector2(bounds.Min.X, bounds.Max.Y);
        AppendLine(bounds.Min, bottomRight, color);
        AppendLine(bottomRight, bounds.Max, color);
        AppendLine(bounds.Max, topLeft, color);
        AppendLine(topLeft, bounds.Min, color);
    }

    private void AppendCircle(
        NVector2 center,
        float radius,
        Color color,
        int segments = 20)
    {
        if (!float.IsFinite(radius) || radius <= 0f) return;
        var previous = center + new NVector2(radius, 0f);
        for (var segment = 1; segment <= segments; segment++)
        {
            var angle = MathF.Tau * segment / segments;
            var current = center + new NVector2(
                MathF.Cos(angle) * radius,
                MathF.Sin(angle) * radius);
            AppendLine(previous, current, color);
            previous = current;
        }
    }

    private void EnsureOverlaySurface()
    {
        if (_surfaceOpen) return;
        _overlayMesh!.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMaterial);
        _surfaceOpen = true;
    }

    private Vector3 ToWorldAtGround(NVector2 position, float offset = OverlayHeight) =>
        SimPlane3DTransform.ToWorld(
            position,
            SimPlane3DTransform.ToWorldLength(_terrain?.HeightAt(position) ?? 0f) +
            offset);

    private int PrimaryUnit()
    {
        if (_simulation is null) return -1;
        foreach (var unit in _selectedUnits)
        {
            if ((uint)unit < (uint)_simulation.Units.Count &&
                _simulation.Units.Alive[unit])
                return unit;
        }
        return -1;
    }

    private int ComponentAt(NVector2 position)
    {
        if (_connectivity is null ||
            !_connectivity.WorldBounds.Contains(position))
            return -1;
        var column = Math.Clamp(
            (int)MathF.Floor(
                (position.X - _connectivity.WorldBounds.Min.X) /
                _connectivity.CellSize),
            0, _connectivity.Columns - 1);
        var row = Math.Clamp(
            (int)MathF.Floor(
                (position.Y - _connectivity.WorldBounds.Min.Y) /
                _connectivity.CellSize),
            0, _connectivity.Rows - 1);
        var node = row * _connectivity.Columns + column;
        return _connectivity.IsWalkable(node)
            ? _connectivity.ComponentAt(node)
            : -1;
    }

    private static Color RecoveryColor(RecoveryStage stage) => stage switch
    {
        RecoveryStage.Normal => PathColor,
        RecoveryStage.Unreachable => FatalProblemColor,
        RecoveryStage.WaitingForClearance => new Color("ff6b4a"),
        _ => ProblemColor
    };

    private static string Point(NVector2 value) =>
        $"{value.X:0.0},{value.Y:0.0}";

    private static string Rect(SimRect value) =>
        $"{Point(value.Min)}→{Point(value.Max)}";

    private void ExportSnapshot()
    {
        if (_simulation is null) return;
        try
        {
            var simulation = _simulation;
            var units = simulation.Units;
            var pending = simulation.CapturePendingPathRequestsForDiagnostics();
            var dynamicFootprints = simulation.World.DynamicOccupancy.Snapshot();
            var constructions = simulation.Construction.CreateOverview();
            var selected = _selectedUnits
                .Where(unit => (uint)unit < (uint)units.Count)
                .Select(unit =>
                {
                    var path = units.Paths[unit];
                    return new
                    {
                        id = unit,
                        alive = units.Alive[unit],
                        team = simulation.Combat.Teams[unit],
                        movementClass = units.MovementClasses[unit].ToString(),
                        physicalRadius = units.Radii[unit],
                        navigationRadius = units.NavigationRadii[unit],
                        position = DebugPoint.From(units.Positions[unit]),
                        velocity = DebugPoint.From(units.Velocities[unit]),
                        preferredVelocity = DebugPoint.From(
                            units.PreferredVelocities[unit]),
                        collisionCorrection = DebugPoint.From(
                            units.CollisionCorrections[unit]),
                        mode = units.Modes[unit].ToString(),
                        goalKind = units.MovementGoalKinds[unit].ToString(),
                        legResult = units.MovementLegResults[unit].ToString(),
                        moveGoal = DebugPoint.From(units.MoveGoals[unit]),
                        slotTarget = DebugPoint.From(units.SlotTargets[unit]),
                        commandVersion = units.CommandVersions[unit],
                        pathPending = units.PathPending[unit],
                        pathCursor = path?.Cursor ?? -1,
                        pathCommandVersion = path?.CommandVersion ?? -1,
                        path = path?.Points.Select(DebugPoint.From).ToArray() ?? [],
                        route = units.RouteWaypoints[unit]
                            .Select(DebugPoint.From).ToArray(),
                        recovery = units.RecoveryStages[unit].ToString(),
                        recoveryEvents = units.RecoveryEventCounts[unit],
                        recoveryRetries = units.RecoveryRetryCounts[unit],
                        blockedByNavigation = units.BlockedByNavigation[unit],
                        progressTimer = units.ProgressTimers[unit],
                        progressBestDistance = units.ProgressBestDistances[unit],
                        repathCooldown = units.RepathCooldowns[unit],
                        movementGroup = units.MovementGroupIds[unit],
                        movementGroupSize = units.MovementGroupSizes[unit],
                        destinationStallTicks = units.DestinationStallTicks[unit],
                        destinationNearTicks = units.DestinationNearTicks[unit],
                        dynamicBlockageTicks = units.DynamicBlockageTicks[unit],
                        overflowed = units.DestinationOverflowed[unit],
                        yield = units.DestinationYieldPhases[unit].ToString(),
                        yieldForUnit = units.DestinationYieldForUnits[unit],
                        chokeId = units.ActiveChokeIds[unit],
                        chokePhase = units.ChokePhases[unit].ToString(),
                        chokeAdmitted = units.ChokeAdmitted[unit],
                        chokeQueueRank = units.ChokeQueueRanks[unit],
                        chokeWaitTicks = units.ChokeWaitTicks[unit]
                    };
                })
                .ToArray();
            var metrics = simulation.Metrics;
            var payload = new
            {
                generatedUtc = DateTime.UtcNow,
                tick = metrics.Tick,
                navigationRevision = simulation.World.NavigationRevision,
                pathBudgetPerTick = simulation.PathBudgetPerTick,
                metrics = new
                {
                    metrics.MovingUnits,
                    metrics.WaitingForPathUnits,
                    metrics.UnreachableUnits,
                    metrics.PendingPathRequests,
                    metrics.PathsCompleted,
                    metrics.PathsFailed,
                    metrics.RepathRequests,
                    metrics.PathMilliseconds,
                    metrics.PathDirectCheckMilliseconds,
                    metrics.PathSearchMilliseconds,
                    metrics.PathSimplificationMilliseconds,
                    metrics.PathExpandedNodes,
                    metrics.PathRawPoints,
                    metrics.PathSimplifiedPoints,
                    metrics.PathFullConnectivityRebuilds,
                    metrics.PathIncrementalConnectivityUpdates,
                    metrics.PathConnectivityRefreshMilliseconds,
                    metrics.CollisionPairs,
                    metrics.MaximumPenetration,
                    metrics.RecoveryEvents
                },
                pendingRequests = pending,
                dynamicFootprints = dynamicFootprints.Select(value => new
                {
                    id = value.Id.Value,
                    revision = value.PlacedRevision,
                    minimum = DebugPoint.From(value.Bounds.Min),
                    maximum = DebugPoint.From(value.Bounds.Max)
                }).ToArray(),
                constructions = constructions.Select(value => new
                {
                    id = value.Id.Value,
                    value.PlayerId,
                    typeId = value.Type.Id,
                    typeName = value.Type.Name,
                    state = value.State.ToString(),
                    builderUnit = value.BuilderUnit,
                    reservationId = value.ReservationId.Value,
                    footprintId = value.FootprintId.Value,
                    minimum = DebugPoint.From(value.Bounds.Min),
                    maximum = DebugPoint.From(value.Bounds.Max),
                    accessPoint = DebugPoint.From(value.AccessPoint),
                    value.Progress,
                    value.Health,
                    value.MaximumHealth
                }).ToArray(),
                connectivity = _connectivity is null ? null : new
                {
                    movementClass = _gridClass.ToString(),
                    _connectivity.Columns,
                    _connectivity.Rows,
                    _connectivity.CellSize,
                    _connectivity.NavigationRadius,
                    _connectivity.WorldRevision,
                    source = _connectivity.Source.ToString(),
                    _connectivity.ComponentCount
                },
                selectedUnits = selected,
                trace = _trace.Select(DebugPoint.From).ToArray(),
                events = _events.ToArray()
            };
            var json = JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                });
            var userPath = $"user://war3_navigation_debug_" +
                           $"{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var path = ProjectSettings.GlobalizePath(userPath);
            File.WriteAllText(path, json);
            _exportStatus!.Text = $"已导出：{path}";
            GD.Print($"WAR3_NAV_DEBUG_EXPORT tick={metrics.Tick} path={path}");
        }
        catch (Exception exception)
        {
            _exportStatus!.Text = $"导出失败：{exception.Message}";
            GD.PushError($"WAR3_NAV_DEBUG_EXPORT_FAIL {exception}");
        }
    }

    private void CopyRuntimeLogPath()
    {
        var path = ResolveRuntimeLogPath();
        DisplayServer.ClipboardSet(path);
        if (_exportStatus is not null)
            _exportStatus.Text = $"已复制运行日志路径：{path}";
        AddEvent(
            $"T{_simulation?.Metrics.Tick ?? 0}  已复制运行日志路径");
    }

    private static string ResolveRuntimeLogPath()
    {
        var arguments = OS.GetCmdlineArgs();
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument == "--log-file" && index + 1 < arguments.Length)
                return ProjectSettings.GlobalizePath(arguments[index + 1]);
            const string prefix = "--log-file=";
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                return ProjectSettings.GlobalizePath(argument[prefix.Length..]);
            }
        }

        const string setting = "debug/file_logging/log_path";
        var configured = ProjectSettings.HasSetting(setting)
            ? ProjectSettings.GetSetting(setting).AsString()
            : "user://logs/godot.log";
        if (string.IsNullOrWhiteSpace(configured))
            configured = "user://logs/godot.log";
        return ProjectSettings.GlobalizePath(configured);
    }

    private readonly record struct UnitObservedState(
        UnitMoveMode Mode,
        UnitMovementLegResult Result,
        RecoveryStage Recovery,
        int CommandVersion,
        bool PathPending,
        int PathCursor,
        int PathPoints,
        bool Blocked,
        int ChokeId,
        ChokePhase Choke,
        DestinationYieldPhase Yield,
        bool Overflowed);

    private readonly record struct DebugPoint(float X, float Y)
    {
        public static DebugPoint From(NVector2 value) => new(value.X, value.Y);
    }
}
