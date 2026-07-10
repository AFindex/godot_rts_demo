using Godot;
using RtsDemo.Simulation;
using RtsDemo.Tests;
using RtsDemo.GodotRuntime.Resources;
using System.Text.Json;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime;

public partial class RtsDemo : Node2D
{
    private const string DemoNavigationResourcePath =
        "res://data/demo_navigation_map.tres";

    [Export]
    public RtsNavigationMapResource? NavigationMapAsset { get; set; }
    private readonly HashSet<int> _selectedUnits = new();
    private StaticWorld? _world;
    private GodotPathProvider? _pathProvider;
    private IPathProvider? _simulationPathProvider;
    private RtsSimulation? _simulation;
    private PortalGraphRoutePlanner? _routePlanner;
    private ChokeController? _chokeController;
    private VisualTestSession? _visualTest;
    private Label? _hud;
    private bool _navigationReady;
    private bool _dragging;
    private bool _showDebug = true;
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private Vector2 _commandMarker;
    private float _commandMarkerTime;
    private int _visualTestFinishFrames = -1;
    private int _visualTestExitCode;
    private readonly Stack<DynamicFootprintId> _demoBuildings = new();
    private NavigationMapSnapshot? _navigationSnapshot;

    public override async void _Ready()
    {
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

        if (userArguments.Contains("--self-test"))
        {
            if (!TryLoadNavigationSnapshot())
            {
                GetTree().Quit(1);
                return;
            }

            var result = SimulationSelfTest.Run(_navigationSnapshot);
            GD.Print($"RTS_SELF_TEST {(result.Passed ? "PASS" : "FAIL")}: {result.Summary}");
            GetTree().Quit(result.Passed ? 0 : 1);
            return;
        }

        var visualTestId = ReadArgument(userArguments, "--visual-test");
        if (visualTestId is not null)
        {
            if (!TryLoadNavigationSnapshot())
            {
                GetTree().Quit(1);
                return;
            }

            StartVisualTest(visualTestId);
            return;
        }

        if (!TryLoadNavigationSnapshot() || _navigationSnapshot is null)
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
            new GridPathProvider(_world, radius: 8f),
            _world,
            radius: 8f);
        _simulation = new RtsSimulation(
            _world,
            _simulationPathProvider,
            capacity: 256,
            groupRoutePlanner: _routePlanner,
            chokeController: _chokeController);
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

        if (userArguments.Contains("--capture"))
        {
            await CaptureDemoFrame();
        }
    }

    public override void _ExitTree()
    {
        _pathProvider?.Dispose();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_navigationReady || _simulation is null)
        {
            return;
        }

        if (_visualTest is null)
        {
            _simulation.Tick((float)delta);
        }
        else
        {
            _visualTest.Step();
        }
        _commandMarkerTime = MathF.Max(0f, _commandMarkerTime - (float)delta);
        UpdateHud();
        QueueRedraw();

        if (_visualTest is not null &&
            _simulation.Metrics.Tick >= _visualTest.DurationTicks)
        {
            _navigationReady = false;
            _visualTestFinishFrames = 2;
            var result = _visualTest.Evaluate();
            _visualTestExitCode = result.Passed ? 0 : 1;
            GD.Print(
                $"RTS_VISUAL_TEST_{(result.Passed ? "PASS" : "FAIL")} {_visualTest.Id}: " +
                $"ticks={_simulation.Metrics.Tick}, {result.Summary}");
        }
    }

    public override void _Process(double delta)
    {
        if (_visualTestFinishFrames < 0)
        {
            return;
        }

        _visualTestFinishFrames--;
        if (_visualTestFinishFrames == 0)
        {
            GetTree().Quit(_visualTestExitCode);
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_simulation is null)
        {
            return;
        }

        if (inputEvent is InputEventMouseMotion motion && _dragging)
        {
            _dragCurrent = motion.Position;
            QueueRedraw();
            return;
        }

        if (inputEvent is InputEventMouseButton mouse)
        {
            HandleMouse(mouse);
            return;
        }

        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            HandleKey(key.Keycode);
        }
    }

    public override void _Draw()
    {
        if (_world is null || _simulation is null)
        {
            return;
        }

        DrawRect(new Rect2(Vector2.Zero, new Vector2(1280f, 720f)), new Color("101722"));
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

        foreach (var footprint in _world.DynamicOccupancy.Snapshot())
        {
            var rect = ToRect2(footprint.Bounds);
            DrawRect(rect, new Color(0.82f, 0.25f, 0.16f, 0.72f), filled: true);
            DrawRect(rect, new Color("ff9b5e"), filled: false, width: 2f);
        }

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
            var markerColor = new Color(0.25f, 1f, 0.45f, alpha);
            DrawArc(_commandMarker, 12f, 0f, MathF.Tau, 24, markerColor, 2f);
            DrawLine(_commandMarker - new Vector2(6f, 0f),
                _commandMarker + new Vector2(6f, 0f), markerColor, 2f);
            DrawLine(_commandMarker - new Vector2(0f, 6f),
                _commandMarker + new Vector2(0f, 6f), markerColor, 2f);
        }
    }

    private void HandleMouse(InputEventMouseButton mouse)
    {
        if (_simulation is null)
        {
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
                SelectInRect(
                    MakeRect(_dragStart, mouse.Position),
                    additive: Input.IsKeyPressed(Key.Shift));
                QueueRedraw();
            }
        }
        else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed &&
                 _selectedUnits.Count > 0 && _navigationReady)
        {
            var selected = _selectedUnits.OrderBy(index => index).ToArray();
            _simulation.IssueMove(selected, GodotPathProvider.ToNumerics(mouse.Position));
            _commandMarker = mouse.Position;
            _commandMarkerTime = 0.65f;
            QueueRedraw();
        }
    }

    private void HandleKey(Key key)
    {
        if (_simulation is null)
        {
            return;
        }

        switch (key)
        {
            case Key.Space:
                _selectedUnits.Clear();
                for (var unit = 0; unit < _simulation.Units.Count; unit++)
                {
                    _selectedUnits.Add(unit);
                }
                break;
            case Key.S:
                _simulation.Stop(_selectedUnits.OrderBy(index => index).ToArray());
                break;
            case Key.H:
                _simulation.Hold(_selectedUnits.OrderBy(index => index).ToArray());
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

    private void SelectInRect(Rect2 rect, bool additive)
    {
        if (_simulation is null)
        {
            return;
        }

        if (!additive)
        {
            _selectedUnits.Clear();
        }

        if (rect.Size.LengthSquared() < 5f * 5f)
        {
            var click = rect.Position + rect.Size * 0.5f;
            var best = -1;
            var bestDistance = 15f * 15f;
            for (var unit = 0; unit < _simulation.Units.Count; unit++)
            {
                var position = GodotPathProvider.ToGodot(_simulation.Units.Positions[unit]);
                var distance = position.DistanceSquaredTo(click);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = unit;
                }
            }

            if (best >= 0)
            {
                _selectedUnits.Add(best);
            }

            return;
        }

        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            var position = GodotPathProvider.ToGodot(_simulation.Units.Positions[unit]);
            if (rect.HasPoint(position))
            {
                _selectedUnits.Add(unit);
            }
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

            DrawCircle(position, radius, color);
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
        _hud = new Label
        {
            Position = new Vector2(12f, 8f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color("d8e7f3")
        };
        _hud.AddThemeFontSizeOverride("font_size", 15);
        AddChild(_hud);
    }

    private void UpdateHud()
    {
        if (_hud is null || _simulation is null)
        {
            return;
        }

        var metrics = _simulation.Metrics;
        var chokeUnits = 0;
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
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
            $"{title}  |  units {_simulation.Units.Count}  " +
            $"selected {_selectedUnits.Count}  moving {metrics.MovingUnits}  " +
            $"arrived {metrics.ArrivedUnits}  choke {chokeUnits}  " +
            $"route {_simulation.LastIssuedGroupRoute.Waypoints.Length}  " +
            $"route plans {metrics.GroupRoutePlans} shared {metrics.SharedRouteAssignments}  " +
            $"slot swaps {metrics.DestinationSlotSwaps}  " +
            $"overflow {metrics.DestinationOverflowAssignments}  " +
            $"stall {metrics.MaximumDestinationStallTicks}  " +
            $"near {metrics.MaximumDestinationNearTicks}  " +
            $"yield {metrics.DestinationYieldEvents}/{metrics.ActiveDestinationYields}  " +
            $"path queue {metrics.PendingPathRequests}  nav r{metrics.NavigationRevision}  " +
            $"buildings {_world?.DynamicOccupancy.Count ?? 0}  " +
            $"invalidated {metrics.NavigationInvalidations}  " +
            $"recovery {metrics.RecoveryEvents}  unreachable {metrics.UnreachableUnits}\n" +
            $"map {ActiveNavigationLabel()}  " +
            $"tick {metrics.TotalMilliseconds:0.00}ms  path {metrics.PathMilliseconds:0.00}  " +
            $"steer {metrics.SteeringMilliseconds:0.00}  " +
            $"collision {metrics.CollisionMilliseconds:0.00}  " +
            $"alloc {metrics.AllocatedBytes / 1024.0:0.0}KB\n" +
            $"{trafficText}\n" +
            "LMB drag/select  RMB move  Space select all  S stop  H hold  " +
            "B place building  X remove building  D debug  R reset";
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

    private void StartVisualTest(string caseId)
    {
        try
        {
            _visualTest = VisualTestCatalog.Create(caseId, _navigationSnapshot);
        }
        catch (ArgumentException exception)
        {
            GD.PushError(exception.Message);
            GetTree().Quit(2);
            return;
        }

        _world = _visualTest.World;
        _simulation = _visualTest.Simulation;
        _routePlanner = _visualTest.RoutePlanner;
        _chokeController = _visualTest.ChokeController;
        _selectedUnits.Clear();
        foreach (var unit in _visualTest.RenderUnitIndices)
        {
            _selectedUnits.Add(unit);
        }

        _navigationReady = true;
        CreateHud();
        UpdateHud();
        QueueRedraw();
        GD.Print(
            $"RTS_VISUAL_TEST_START {_visualTest.Id}: " +
            $"ticks={_visualTest.DurationTicks}, units={_visualTest.VisibleUnits.Length}");
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

    private void GenerateDemoNavigationResource()
    {
        var snapshot = DemoMapDefinition.CreateSnapshot();
        var resource = NavigationMapResourceConverter.FromSnapshot(snapshot);
        var directory = ProjectSettings.GlobalizePath("res://data");
        var directoryError = DirAccess.MakeDirRecursiveAbsolute(directory);
        if (directoryError is not (Error.Ok or Error.AlreadyExists))
        {
            GD.PushError($"Unable to create navigation data directory: {directoryError}");
            GetTree().Quit(1);
            return;
        }

        var saveError = ResourceSaver.Save(resource, DemoNavigationResourcePath);
        GD.Print(
            $"RTS_NAVIGATION_RESOURCE_GENERATE error={saveError} " +
            $"path={DemoNavigationResourcePath} hash={snapshot.StableHashText}");
        GetTree().Quit(saveError == Error.Ok ? 0 : 1);
    }

    private void SpawnDemoUnits()
    {
        if (_simulation is null)
        {
            return;
        }

        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 12; column++)
            {
                var unit = _simulation.AddUnit(
                    new NVector2(105f + column * 20f, 160f + row * 20f));
                _selectedUnits.Add(unit);
            }
        }

        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                _simulation.AddUnit(
                    new NVector2(1030f + column * 20f, 510f + row * 20f));
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
            chokeController: _chokeController);
        _selectedUnits.Clear();
        SpawnDemoUnits();
        _commandMarkerTime = 0f;
    }

    private void PlaceDemoBuilding()
    {
        if (_world is null || _simulation is null || _visualTest is not null)
        {
            return;
        }

        var size = new NVector2(100f, 150f);
        var halfSize = size * 0.5f;
        var allowedCenters = _world.Bounds.Inset(MathF.Max(halfSize.X, halfSize.Y));
        var center = allowedCenters.Clamp(
            GodotPathProvider.ToNumerics(GetGlobalMousePosition()));
        var id = _simulation.PlaceBuilding(
            new SimRect(center - halfSize, center + halfSize));
        _demoBuildings.Push(id);
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
