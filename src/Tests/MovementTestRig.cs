using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public readonly record struct TestUnitId(int Value);
public readonly record struct TestBuildingId(int Value);

public readonly record struct TestChokeTrafficSnapshot(
    int ActiveDirection,
    bool Draining,
    int PositiveQueue,
    int NegativeQueue,
    int InFlight,
    int Capacity,
    int ConflictTicks,
    int MaximumPositiveWaitTicks,
    int MaximumNegativeWaitTicks,
    int HardBlockers,
    int BlockedTicks);

public enum TestRecoveryStage : byte
{
    Normal,
    AvoidanceFlip,
    LocalRepath,
    RouteReplan,
    DirectFallback,
    WaitingForClearance,
    Unreachable
}

public readonly record struct TestRecoverySnapshot(
    int TotalEvents,
    int UnreachableUnits,
    TestRecoveryStage MaximumStage,
    int PendingPathRequests);

public readonly record struct TestPerformanceSnapshot(
    double TotalMilliseconds,
    double PathMilliseconds,
    double PreferredVelocityMilliseconds,
    double ChokeMilliseconds,
    double SpatialHashMilliseconds,
    double SteeringMilliseconds,
    double IntegrateMilliseconds,
    double CollisionMilliseconds,
    double RecoveryMilliseconds,
    long AllocatedBytes);

public readonly record struct TestMovementDiagnostics(
    long GroupRoutePlans,
    long SharedRouteAssignments,
    long DestinationSlotSwaps,
    long DestinationLocalRematches,
    long DestinationLocalRematchedUnits,
    long DestinationOverflowAssignments,
    int MaximumDestinationStallTicks,
    int MaximumDestinationNearTicks,
    long DestinationYieldEvents,
    int ActiveDestinationYields);

public enum TestUnitState : byte
{
    Idle,
    Moving,
    Arrived,
    Holding
}

public readonly record struct TestUnitSnapshot(
    TestUnitId Id,
    Vector2 Position,
    Vector2 Velocity,
    Vector2 AssignedTarget,
    float Radius,
    TestUnitState State);

/// <summary>
/// Stable business-level test API. Scenario definitions only use this facade;
/// concrete simulation data structures are confined to this adapter.
/// </summary>
public sealed class MovementTestRig
{
    private readonly StaticWorld _world;
    private readonly RtsSimulation _simulation;
    private readonly PortalGraphRoutePlanner? _routePlanner;
    private readonly ChokeController? _chokeController;
    private readonly NavigationMapSnapshot? _navigationMap;

    private MovementTestRig(
        StaticWorld world,
        RtsSimulation simulation,
        PortalGraphRoutePlanner? routePlanner,
        ChokeController? chokeController,
        NavigationMapSnapshot? navigationMap)
    {
        _world = world;
        _simulation = simulation;
        _routePlanner = routePlanner;
        _chokeController = chokeController;
        _navigationMap = navigationMap;
    }

    public long Tick => _simulation.Metrics.Tick;
    public int UnitCount => _simulation.Units.Count;
    public Vector2 WorldMinimum => _world.Bounds.Min;
    public Vector2 WorldMaximum => _world.Bounds.Max;
    public int NavigationRevision => _world.NavigationRevision;
    public int BuildingCount => _world.DynamicOccupancy.Count;
    public int NavigationFormatVersion => _navigationMap?.FormatVersion ?? 0;
    public ulong NavigationDataHash => _navigationMap?.StableHash ?? 0UL;
    public int LastNavigationInvalidations =>
        _simulation.Metrics.NavigationInvalidations;

    internal StaticWorld RenderWorld => _world;
    internal RtsSimulation RenderSimulation => _simulation;
    internal PortalGraphRoutePlanner? RenderRoutePlanner => _routePlanner;
    internal ChokeController? RenderChokeController => _chokeController;

    public static MovementTestRig CreateOpenField(Vector2 size, int capacity)
    {
        var world = new StaticWorld(new SimRect(Vector2.Zero, size));
        return new MovementTestRig(
            world,
            new RtsSimulation(world, new GridPathProvider(world, 8f), capacity),
            null,
            null,
            null);
    }

    public static MovementTestRig CreateChokeMap(
        int capacity,
        NavigationMapSnapshot? navigationMap = null)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        var world = navigationMap.CreateWorld();
        var routePlanner = navigationMap.CreateRoutePlanner(world);
        var chokeController = navigationMap.CreateChokeController();
        var simulation = new RtsSimulation(
            world,
            new GridPathProvider(world, 8f),
            capacity,
            routePlanner,
            chokeController);
        return new MovementTestRig(
            world,
            simulation,
            routePlanner,
            chokeController,
            navigationMap);
    }

    public TestUnitId Spawn(
        Vector2 position,
        float radius = 7.5f,
        float maximumSpeed = 128f,
        float acceleration = 720f) =>
        new(_simulation.AddUnit(position, radius, maximumSpeed, acceleration));

    public TestUnitId[] SpawnGrid(
        Vector2 origin,
        int rows,
        int columns,
        float spacing,
        float radius = 7.5f,
        float maximumSpeed = 128f,
        float acceleration = 720f)
    {
        var result = new TestUnitId[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                result[row * columns + column] = Spawn(
                    origin + new Vector2(column * spacing, row * spacing),
                    radius,
                    maximumSpeed,
                    acceleration);
            }
        }

        return result;
    }

    public void Move(IReadOnlyList<TestUnitId> units, Vector2 target) =>
        _simulation.IssueMove(ToBackendIndices(units), target);

    public void Stop(IReadOnlyList<TestUnitId> units) =>
        _simulation.Stop(ToBackendIndices(units));

    public void Hold(IReadOnlyList<TestUnitId> units) =>
        _simulation.Hold(ToBackendIndices(units));

    public TestBuildingId PlaceBuilding(Vector2 center, Vector2 size)
    {
        if (size.X <= 0f || size.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var halfSize = size * 0.5f;
        var id = _simulation.PlaceBuilding(new SimRect(center - halfSize, center + halfSize));
        return new TestBuildingId(id.Value);
    }

    public bool RemoveBuilding(TestBuildingId id) =>
        _simulation.RemoveBuilding(new DynamicFootprintId(id.Value));

    public TestChokeTrafficSnapshot ObserveChokeTraffic(int chokeIndex = 0)
    {
        if (_chokeController is null ||
            (uint)chokeIndex >= (uint)_chokeController.TrafficSnapshots.Length)
        {
            throw new InvalidOperationException("This test world has no requested choke traffic data.");
        }

        var snapshot = _chokeController.TrafficSnapshots[chokeIndex];
        return new TestChokeTrafficSnapshot(
            snapshot.ActiveDirection,
            snapshot.Draining,
            snapshot.PositiveQueue,
            snapshot.NegativeQueue,
            snapshot.InFlight,
            snapshot.Capacity,
            snapshot.ConflictTicks,
            snapshot.MaximumPositiveWaitTicks,
            snapshot.MaximumNegativeWaitTicks,
            snapshot.HardBlockers,
            snapshot.BlockedTicks);
    }

    public TestRecoverySnapshot ObserveRecovery(IReadOnlyList<TestUnitId> units)
    {
        var totalEvents = 0;
        var unreachable = 0;
        var maximumStage = RecoveryStage.Normal;
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index].Value;
            totalEvents += _simulation.Units.RecoveryEventCounts[unit];
            var stage = _simulation.Units.RecoveryStages[unit];
            if (stage == RecoveryStage.Unreachable)
            {
                unreachable++;
            }

            if (stage > maximumStage)
            {
                maximumStage = stage;
            }
        }

        return new TestRecoverySnapshot(
            totalEvents,
            unreachable,
            (TestRecoveryStage)maximumStage,
            _simulation.Metrics.PendingPathRequests);
    }

    public TestPerformanceSnapshot ObservePerformance()
    {
        var metrics = _simulation.Metrics;
        return new TestPerformanceSnapshot(
            metrics.TotalMilliseconds,
            metrics.PathMilliseconds,
            metrics.PreferredVelocityMilliseconds,
            metrics.ChokeMilliseconds,
            metrics.SpatialHashMilliseconds,
            metrics.SteeringMilliseconds,
            metrics.IntegrateMilliseconds,
            metrics.CollisionMilliseconds,
            metrics.RecoveryMilliseconds,
            metrics.AllocatedBytes);
    }

    public TestMovementDiagnostics ObserveMovementDiagnostics()
    {
        var metrics = _simulation.Metrics;
        return new TestMovementDiagnostics(
            metrics.GroupRoutePlans,
            metrics.SharedRouteAssignments,
            metrics.DestinationSlotSwaps,
            metrics.DestinationLocalRematches,
            metrics.DestinationLocalRematchedUnits,
            metrics.DestinationOverflowAssignments,
            metrics.MaximumDestinationStallTicks,
            metrics.MaximumDestinationNearTicks,
            metrics.DestinationYieldEvents,
            metrics.ActiveDestinationYields);
    }

    public void Step() => _simulation.Tick(1f / 60f);

    public TestUnitSnapshot Observe(TestUnitId unit)
    {
        var index = unit.Value;
        if ((uint)index >= (uint)_simulation.Units.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }

        return new TestUnitSnapshot(
            unit,
            _simulation.Units.Positions[index],
            _simulation.Units.Velocities[index],
            _simulation.Units.SlotTargets[index],
            _simulation.Units.Radii[index],
            ToTestState(_simulation.Units.Modes[index]));
    }

    public TestUnitSnapshot[] Observe(IReadOnlyList<TestUnitId> units)
    {
        var result = new TestUnitSnapshot[units.Count];
        for (var index = 0; index < units.Count; index++)
        {
            result[index] = Observe(units[index]);
        }

        return result;
    }

    public bool IsInsideWorld(TestUnitId unit)
    {
        var snapshot = Observe(unit);
        return _world.IsDiscFree(snapshot.Position, snapshot.Radius);
    }

    internal int[] ToBackendIndices(IReadOnlyList<TestUnitId> units)
    {
        var result = new int[units.Count];
        for (var index = 0; index < units.Count; index++)
        {
            result[index] = units[index].Value;
        }

        return result;
    }

    private static TestUnitState ToTestState(UnitMoveMode mode) => mode switch
    {
        UnitMoveMode.Arrived => TestUnitState.Arrived,
        UnitMoveMode.Hold => TestUnitState.Holding,
        UnitMoveMode.Idle => TestUnitState.Idle,
        _ => TestUnitState.Moving
    };
}

public readonly record struct ScenarioResult(bool Passed, string Summary);

public sealed class VisualTestSession
{
    private readonly SortedDictionary<long, List<ScheduledAction>> _actions = new();
    private readonly Func<MovementTestRig, ScenarioResult> _evaluate;

    public VisualTestSession(
        string id,
        string displayName,
        int durationTicks,
        MovementTestRig rig,
        TestUnitId[] visibleUnits,
        Func<MovementTestRig, ScenarioResult> evaluate)
    {
        Id = id;
        DisplayName = displayName;
        DurationTicks = durationTicks;
        Rig = rig;
        VisibleUnits = visibleUnits;
        _evaluate = evaluate;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public int DurationTicks { get; }
    public MovementTestRig Rig { get; }
    public TestUnitId[] VisibleUnits { get; }
    public string Phase { get; private set; } = "Running";

    internal StaticWorld World => Rig.RenderWorld;
    internal RtsSimulation Simulation => Rig.RenderSimulation;
    internal PortalGraphRoutePlanner? RoutePlanner => Rig.RenderRoutePlanner;
    internal ChokeController? ChokeController => Rig.RenderChokeController;
    internal int[] RenderUnitIndices => Rig.ToBackendIndices(VisibleUnits);

    public VisualTestSession At(
        long tick,
        string phase,
        Action<MovementTestRig> action)
    {
        if (!_actions.TryGetValue(tick, out var actions))
        {
            actions = [];
            _actions.Add(tick, actions);
        }

        actions.Add(new ScheduledAction(phase, action));
        return this;
    }

    public void Step()
    {
        if (_actions.TryGetValue(Rig.Tick, out var actions))
        {
            for (var index = 0; index < actions.Count; index++)
            {
                Phase = actions[index].Phase;
                actions[index].Action(Rig);
            }
        }

        Rig.Step();
    }

    public ScenarioResult Evaluate() => _evaluate(Rig);

    private readonly record struct ScheduledAction(
        string Phase,
        Action<MovementTestRig> Action);
}
