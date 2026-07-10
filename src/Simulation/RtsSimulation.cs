using System.Diagnostics;
using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class RtsSimulation
{
    private const float WaypointRadius = 13f;
    private const float ArrivalSlowRadius = 58f;
    private const float ArrivalStopRadius = 3.5f;
    private const int CollisionIterations = 3;
    private const int DestinationReflowIntervalTicks = 12;
    private const int DestinationReflowCooldownTicks = 90;
    private const int MaximumDestinationSwapsPerPass = 12;
    private const int DestinationLocalRematchIntervalTicks = 60;
    private const int DestinationLocalRematchCooldownTicks = 180;
    private const int DestinationOverflowIntervalTicks = 30;
    private const int MaximumDestinationOverflowsPerPass = 2;
    private const int DestinationYieldIntervalTicks = 30;
    private const int MaximumDestinationYieldStartsPerPass = 2;
    private const int MaximumActiveDestinationYields = 4;
    private const int DestinationYieldDeadlineTicks = 360;
    private const int DestinationYieldReturnDeadlineTicks = 180;
    private const int DestinationYieldCooldownTicks = 600;

    private readonly IPathProvider _pathProvider;
    private readonly Queue<PathRequest> _pathRequests = new();
    private readonly SpatialHash _spatialHash;
    private readonly DestinationSlotAllocator _slotAllocator;
    private readonly DestinationSlotReflow _slotReflow;
    private readonly DestinationLocalRematcher _localRematcher;
    private readonly DestinationOverflowResolver _overflowResolver;
    private readonly DestinationYieldResolver _yieldResolver;
    private readonly SteeringSolver _steeringSolver;
    private readonly IGroupRoutePlanner? _groupRoutePlanner;
    private readonly ChokeController? _chokeController;
    private readonly int[] _collisionNeighbors = new int[64];
    private int _pendingNavigationInvalidations;
    private int _nextMovementGroupId = 1;

    public RtsSimulation(
        StaticWorld world,
        IPathProvider pathProvider,
        int capacity = 512,
        IGroupRoutePlanner? groupRoutePlanner = null,
        ChokeController? chokeController = null)
    {
        World = world;
        _pathProvider = pathProvider;
        Units = new UnitStore(capacity);
        _spatialHash = new SpatialHash(40f);
        _slotAllocator = new DestinationSlotAllocator(world);
        _slotReflow = new DestinationSlotReflow(world);
        _localRematcher = new DestinationLocalRematcher(world);
        _overflowResolver = new DestinationOverflowResolver(world);
        _yieldResolver = new DestinationYieldResolver(world);
        _steeringSolver = new SteeringSolver(world, _spatialHash);
        _groupRoutePlanner = groupRoutePlanner;
        _chokeController = chokeController;
    }

    public StaticWorld World { get; }
    public UnitStore Units { get; }
    public SimulationMetrics Metrics { get; } = new();
    public GroupRoutePlan LastIssuedGroupRoute { get; private set; } = GroupRoutePlan.Empty;
    public IGroupRoutePlanner? GroupRoutePlanner => _groupRoutePlanner;
    public ChokeController? ChokeController => _chokeController;
    public int PathBudgetPerTick { get; set; } = 24;

    public DynamicFootprintId PlaceBuilding(SimRect footprint)
    {
        var id = World.DynamicOccupancy.Place(footprint);
        InvalidatePathsIntersecting(footprint);
        return id;
    }

    public BuildingPlacementResult TryPlaceBuilding(
        SimRect footprint,
        BuildingPlacementRules rules)
    {
        var validation = BuildingPlacementValidator.Validate(
            World, Units, footprint, rules);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var id = PlaceBuilding(footprint);
        return new BuildingPlacementResult(
            BuildingPlacementCode.Success, id, -1);
    }

    public BuildingPlacementResult TryPlaceBuilding(
        Vector2 center,
        BuildingFootprintProfileSnapshot profile)
    {
        var halfSize = profile.Size * 0.5f;
        return TryPlaceBuilding(
            new SimRect(center - halfSize, center + halfSize),
            new BuildingPlacementRules(
                profile.MinimumPassageClass,
                profile.UnitPadding));
    }

    public bool RemoveBuilding(DynamicFootprintId id)
    {
        if (!World.DynamicOccupancy.Remove(id, out _))
        {
            return false;
        }

        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (Units.BlockedByNavigation[unit])
            {
                QueueNavigationReplan(unit);
            }
        }

        return true;
    }

    public int AddUnit(
        Vector2 position,
        float radius = 7.5f,
        float maxSpeed = 128f,
        float acceleration = 720f) =>
        Units.Add(position, radius, maxSpeed, acceleration);

    public int AddUnit(
        Vector2 position,
        UnitMovementProfileSnapshot profile) =>
        AddUnit(
            position,
            profile.PhysicalRadius,
            profile.MaximumSpeed,
            profile.Acceleration);

    public void IssueMove(ReadOnlySpan<int> unitIndices, Vector2 target)
    {
        if (unitIndices.IsEmpty)
        {
            return;
        }

        var assignments = _slotAllocator.Allocate(Units, unitIndices, target);
        var centroid = Vector2.Zero;
        var maximumRadius = 0f;
        var maximumNavigationRadius = 0f;
        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            centroid += Units.Positions[unit];
            maximumRadius = MathF.Max(maximumRadius, Units.Radii[unit]);
            maximumNavigationRadius = MathF.Max(
                maximumNavigationRadius, Units.NavigationRadii[unit]);
        }

        centroid /= unitIndices.Length;
        var groupGoal = World.Bounds.Inset(maximumRadius + 2f).Clamp(target);
        LastIssuedGroupRoute = PlanGroupRoute(
            centroid, groupGoal, maximumNavigationRadius);
        var movementGroupId = NextMovementGroupId();
        if (unitIndices.Length > 1 && LastIssuedGroupRoute.Waypoints.Length > 0)
        {
            Metrics.SharedRouteAssignments += unitIndices.Length;
        }
        _chokeController?.AssignForMove(
            Units,
            unitIndices,
            centroid,
            groupGoal,
            LastIssuedGroupRoute.ChokeIds);

        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            ValidateUnitIndex(unit);
            Units.CommandVersions[unit]++;
            Units.SlotTargets[unit] = assignments[unit];
            Units.MoveGoals[unit] = groupGoal;
            Units.MovementGroupIds[unit] = movementGroupId;
            Units.MovementGroupSizes[unit] = unitIndices.Length;
            Units.SlotReflowCooldownTicks[unit] = 0;
            Units.DestinationBestDistances[unit] = Vector2.Distance(
                Units.Positions[unit], assignments[unit]);
            Units.DestinationStallTicks[unit] = 0;
            Units.DestinationNearTicks[unit] = 0;
            Units.DestinationOverflowed[unit] = false;
            Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
            Units.DestinationYieldReturnTargets[unit] = assignments[unit];
            Units.DestinationYieldPoints[unit] = assignments[unit];
            Units.DestinationYieldForUnits[unit] = -1;
            Units.DestinationYieldForCommandVersions[unit] = 0;
            Units.DestinationYieldDeadlines[unit] = 0;
            Units.DestinationYieldCooldownTicks[unit] = 0;
            Units.Paths[unit] = null;
            Units.RouteWaypoints[unit] = LastIssuedGroupRoute.Waypoints;
            Units.PathPending[unit] = true;
            Units.Modes[unit] = UnitMoveMode.WaitingForPath;
            Units.ProgressOrigins[unit] = Units.Positions[unit];
            Units.ProgressTimers[unit] = 0f;
            Units.ProgressBestDistances[unit] = Vector2.Distance(
                Units.Positions[unit], Units.SlotTargets[unit]);
            Units.RepathCooldowns[unit] = 0f;
            Units.AvoidanceSides[unit] = 0;
            Units.AvoidanceLockTicks[unit] = 0;
            Units.BlockedByNavigation[unit] = false;
            Units.RecoveryStages[unit] = RecoveryStage.Normal;
            Units.RecoveryEventCounts[unit] = 0;
            Units.RecoveryStableTimers[unit] = 0f;
            Units.RecoveryRetryCounts[unit] = 0;
            _pathRequests.Enqueue(new PathRequest(unit, Units.CommandVersions[unit]));
        }
    }

    public void Stop(ReadOnlySpan<int> unitIndices)
    {
        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            ValidateUnitIndex(unit);
            Units.CommandVersions[unit]++;
            Units.Paths[unit] = null;
            Units.RouteWaypoints[unit] = [];
            Units.PathPending[unit] = false;
            Units.Modes[unit] = UnitMoveMode.Idle;
            Units.SlotTargets[unit] = Units.Positions[unit];
            Units.MoveGoals[unit] = Units.Positions[unit];
            Units.MovementGroupIds[unit] = 0;
            Units.MovementGroupSizes[unit] = 0;
            Units.SlotReflowCooldownTicks[unit] = 0;
            Units.DestinationBestDistances[unit] = 0f;
            Units.DestinationStallTicks[unit] = 0;
            Units.DestinationNearTicks[unit] = 0;
            Units.DestinationOverflowed[unit] = false;
            Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
            Units.DestinationYieldReturnTargets[unit] = Units.Positions[unit];
            Units.DestinationYieldPoints[unit] = Units.Positions[unit];
            Units.DestinationYieldForUnits[unit] = -1;
            Units.DestinationYieldForCommandVersions[unit] = 0;
            Units.DestinationYieldDeadlines[unit] = 0;
            Units.DestinationYieldCooldownTicks[unit] = 0;
            Units.ActiveChokeIds[unit] = -1;
            Units.ChokeDirections[unit] = 0;
            Units.ChokePhases[unit] = ChokePhase.None;
            Units.ChokeAdmitted[unit] = false;
            Units.ChokeQueueRanks[unit] = -1;
            Units.ChokeWaitTicks[unit] = 0;
            Units.BlockedByNavigation[unit] = false;
            Units.RecoveryStages[unit] = RecoveryStage.Normal;
            Units.RecoveryEventCounts[unit] = 0;
            Units.RecoveryStableTimers[unit] = 0f;
            Units.RecoveryRetryCounts[unit] = 0;
        }
    }

    public void Hold(ReadOnlySpan<int> unitIndices)
    {
        Stop(unitIndices);
        for (var i = 0; i < unitIndices.Length; i++)
        {
            Units.Modes[unitIndices[i]] = UnitMoveMode.Hold;
        }
    }

    public void Tick(float delta)
    {
        if (delta <= 0f || !float.IsFinite(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        var tickStart = Stopwatch.GetTimestamp();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        Metrics.Tick++;
        Metrics.PathsCompleted = 0;
        Metrics.PathsFailed = 0;
        Metrics.CollisionPairs = 0;
        Metrics.MaximumPenetration = 0f;
        Metrics.RepathRequests = 0;
        Metrics.NavigationRevision = World.NavigationRevision;
        Metrics.NavigationInvalidations = _pendingNavigationInvalidations;
        Metrics.RecoveryEvents = 0;
        _pendingNavigationInvalidations = 0;

        var phaseStart = Stopwatch.GetTimestamp();
        ProcessPathRequests();
        Metrics.PathMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        UpdatePreferredVelocities();
        Metrics.PreferredVelocityMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        _chokeController?.ApplyPreferredVelocities(Units);
        Metrics.ChokeMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        _spatialHash.Rebuild(Units);
        Metrics.SpatialHashMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        _steeringSolver.Solve(Units, delta);
        _chokeController?.ConstrainSolvedVelocities(Units);
        Metrics.SteeringMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        Integrate(delta);
        Metrics.IntegrateMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        SolveCollisions();
        _chokeController?.ConstrainPositions(Units);
        Metrics.CollisionMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        UpdateProgress(delta);
        _overflowResolver.UpdateStallTracking(Units);
        UpdateDestinationLocalRematch();
        UpdateDestinationReflow();
        UpdateDestinationYielding();
        UpdateDestinationOverflow();
        UpdateMetrics();
        Metrics.RecoveryMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.TotalMilliseconds = Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
        Metrics.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
    }

    private void ProcessPathRequests()
    {
        if (!_pathProvider.IsReady)
        {
            return;
        }

        var requestsToProcess = Math.Min(PathBudgetPerTick, _pathRequests.Count);
        for (var requestIndex = 0; requestIndex < requestsToProcess; requestIndex++)
        {
            var request = _pathRequests.Dequeue();
            var unit = request.UnitIndex;
            if (request.CommandVersion != Units.CommandVersions[unit] ||
                !Units.PathPending[unit])
            {
                continue;
            }

            var start = Units.Positions[unit];
            var goal = Units.SlotTargets[unit];
            var points = BuildUnitPath(unit, start, goal);

            if (request.CommandVersion != Units.CommandVersions[unit])
            {
                continue;
            }

            Units.PathPending[unit] = false;
            if (points.Length == 0 ||
                Vector2.DistanceSquared(points[^1], goal) > 24f * 24f)
            {
                Units.Paths[unit] = null;
                Metrics.PathsFailed++;
                HandlePathFailure(unit);
                continue;
            }

            if (points.Length == 1)
            {
                points = [start, points[0]];
            }

            Units.Paths[unit] = new UnitPath(points, request.CommandVersion);
            Units.Modes[unit] = UnitMoveMode.Moving;
            Units.BlockedByNavigation[unit] = false;
            Units.ProgressBestDistances[unit] = RemainingPathDistance(
                unit, Units.Paths[unit]);
            Metrics.PathsCompleted++;
        }
    }

    private Vector2[] BuildUnitPath(int unit, Vector2 start, Vector2 finalGoal)
    {
        var result = new List<Vector2>(12) { start };
        var current = start;
        var route = Units.RouteWaypoints[unit];
        var navigationRadius = Units.NavigationRadii[unit];

        for (var waypointIndex = 0; waypointIndex <= route.Length; waypointIndex++)
        {
            var segmentGoal = waypointIndex < route.Length
                ? ResolveUnitRouteWaypoint(unit, route[waypointIndex])
                : finalGoal;
            Vector2[] segment;
            if (World.IsSegmentFree(current, segmentGoal, navigationRadius))
            {
                segment = [current, segmentGoal];
            }
            else
            {
                segment = _pathProvider.FindPath(
                    current, segmentGoal, navigationRadius);
            }

            if (segment.Length == 0 ||
                Vector2.DistanceSquared(segment[^1], segmentGoal) > 24f * 24f ||
                !PathIsNavigable(segment, navigationRadius))
            {
                return [];
            }

            for (var pointIndex = 1; pointIndex < segment.Length; pointIndex++)
            {
                if (Vector2.DistanceSquared(result[^1], segment[pointIndex]) > 0.5f * 0.5f)
                {
                    result.Add(segment[pointIndex]);
                }
            }

            current = segment[^1];
        }

        return result.ToArray();
    }

    private bool PathIsNavigable(ReadOnlySpan<Vector2> points, float radius)
    {
        for (var index = 1; index < points.Length; index++)
        {
            if (!World.IsSegmentFree(points[index - 1], points[index], radius))
            {
                return false;
            }
        }

        return points.Length > 0;
    }

    private void InvalidatePathsIntersecting(SimRect footprint)
    {
        var affectedUnits = new List<int>();
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (Units.Modes[unit] is UnitMoveMode.Idle or UnitMoveMode.Hold or UnitMoveMode.Arrived ||
                !RemainingPathIntersects(unit, footprint))
            {
                continue;
            }

            affectedUnits.Add(unit);
        }

        if (affectedUnits.Count == 0)
        {
            return;
        }

        _pendingNavigationInvalidations += affectedUnits.Count;
        ReplanInvalidatedGroups(affectedUnits);
    }

    private void ReplanInvalidatedGroups(List<int> affectedUnits)
    {
        var processed = new bool[affectedUnits.Count];
        for (var affectedIndex = 0; affectedIndex < affectedUnits.Count; affectedIndex++)
        {
            if (processed[affectedIndex])
            {
                continue;
            }

            var firstUnit = affectedUnits[affectedIndex];
            var movementGroupId = Units.MovementGroupIds[firstUnit];
            var group = new List<int>();
            for (var candidateIndex = affectedIndex;
                 candidateIndex < affectedUnits.Count;
                 candidateIndex++)
            {
                var candidate = affectedUnits[candidateIndex];
                var sameGroup = movementGroupId > 0
                    ? Units.MovementGroupIds[candidate] == movementGroupId
                    : candidate == firstUnit;
                if (!processed[candidateIndex] && sameGroup)
                {
                    processed[candidateIndex] = true;
                    group.Add(candidate);
                }
            }

            ReplanInvalidatedGroup(group.ToArray());
        }
    }

    private void ReplanInvalidatedGroup(int[] group)
    {
        var centroid = Vector2.Zero;
        var maximumNavigationRadius = 0f;
        for (var index = 0; index < group.Length; index++)
        {
            var unit = group[index];
            centroid += Units.Positions[unit];
            maximumNavigationRadius = MathF.Max(
                maximumNavigationRadius, Units.NavigationRadii[unit]);
        }

        centroid /= group.Length;
        var groupGoal = Units.MoveGoals[group[0]];
        var route = PlanGroupRoute(
            centroid, groupGoal, maximumNavigationRadius);
        if (group.Length > 1 && route.Waypoints.Length > 0)
        {
            Metrics.SharedRouteAssignments += group.Length;
        }

        _chokeController?.AssignForMove(
            Units,
            group,
            centroid,
            groupGoal,
            route.ChokeIds);
        for (var index = 0; index < group.Length; index++)
        {
            var unit = group[index];
            Units.RouteWaypoints[unit] = route.Waypoints;
            QueueNavigationReplan(
                unit,
                resetRecovery: true,
                countInvalidation: false,
                sharedRoute: route,
                assignChoke: false);
        }
    }

    private bool RemainingPathIntersects(int unit, SimRect footprint)
    {
        var expanded = footprint.Expanded(Units.NavigationRadii[unit]);
        var path = Units.Paths[unit];
        var previous = Units.Positions[unit];
        if (path is not null)
        {
            for (var index = path.Cursor; index < path.Points.Length; index++)
            {
                if (expanded.SegmentIntersects(previous, path.Points[index]))
                {
                    return true;
                }

                previous = path.Points[index];
            }

            return false;
        }

        var route = Units.RouteWaypoints[unit];
        for (var index = 0; index < route.Length; index++)
        {
            if (expanded.SegmentIntersects(previous, route[index]))
            {
                return true;
            }

            previous = route[index];
        }

        return expanded.SegmentIntersects(previous, Units.SlotTargets[unit]);
    }

    private void QueueNavigationReplan(
        int unit,
        bool resetRecovery = true,
        bool countInvalidation = true,
        GroupRoutePlan? sharedRoute = null,
        bool assignChoke = true)
    {
        Units.CommandVersions[unit]++;
        var route = sharedRoute ?? PlanGroupRoute(
            Units.Positions[unit],
            Units.SlotTargets[unit],
            Units.NavigationRadii[unit]);
        Units.RouteWaypoints[unit] = route.Waypoints;
        if (assignChoke && _chokeController is not null)
        {
            int[] singleUnit = [unit];
            _chokeController.AssignForMove(
                Units,
                singleUnit,
                Units.Positions[unit],
                Units.SlotTargets[unit],
                route.ChokeIds);
        }

        Units.Paths[unit] = null;
        Units.PathPending[unit] = true;
        Units.BlockedByNavigation[unit] = false;
        Units.Modes[unit] = UnitMoveMode.WaitingForPath;
        Units.ProgressOrigins[unit] = Units.Positions[unit];
        Units.ProgressTimers[unit] = 0f;
        Units.ProgressBestDistances[unit] = Vector2.Distance(
            Units.Positions[unit], Units.SlotTargets[unit]);
        Units.RepathCooldowns[unit] = 0f;
        if (resetRecovery)
        {
            Units.RecoveryStages[unit] = RecoveryStage.Normal;
            Units.RecoveryStableTimers[unit] = 0f;
            Units.RecoveryRetryCounts[unit] = 0;
        }
        _pathRequests.Enqueue(new PathRequest(unit, Units.CommandVersions[unit]));
        if (countInvalidation)
        {
            _pendingNavigationInvalidations++;
        }
    }

    private GroupRoutePlan PlanGroupRoute(
        Vector2 start,
        Vector2 goal,
        float agentRadius)
    {
        if (_groupRoutePlanner is null)
        {
            return GroupRoutePlan.Empty;
        }

        Metrics.GroupRoutePlans++;
        return _groupRoutePlanner.Plan(start, goal, agentRadius);
    }

    private int NextMovementGroupId()
    {
        if (_nextMovementGroupId == int.MaxValue)
        {
            _nextMovementGroupId = 1;
        }

        return _nextMovementGroupId++;
    }

    private void QueueLocalRepath(int unit, bool clearHighLevelRoute)
    {
        Units.CommandVersions[unit]++;
        if (clearHighLevelRoute)
        {
            Units.RouteWaypoints[unit] = [];
            Units.ActiveChokeIds[unit] = -1;
            Units.ChokeDirections[unit] = 0;
            Units.ChokePhases[unit] = ChokePhase.None;
            Units.ChokeAdmitted[unit] = false;
        }

        Units.Paths[unit] = null;
        Units.PathPending[unit] = true;
        Units.BlockedByNavigation[unit] = false;
        Units.Modes[unit] = UnitMoveMode.WaitingForPath;
        Units.ProgressOrigins[unit] = Units.Positions[unit];
        Units.ProgressTimers[unit] = 0f;
        Units.ProgressBestDistances[unit] = Vector2.Distance(
            Units.Positions[unit], Units.SlotTargets[unit]);
        Units.RepathCooldowns[unit] = 0.45f;
        _pathRequests.Enqueue(new PathRequest(unit, Units.CommandVersions[unit]));
        Metrics.RepathRequests++;
    }

    private void HandlePathFailure(int unit)
    {
        switch (Units.RecoveryStages[unit])
        {
            case RecoveryStage.LocalRepath:
                SetRecoveryStage(unit, RecoveryStage.RouteReplan);
                QueueNavigationReplan(unit, resetRecovery: false, countInvalidation: false);
                Metrics.RepathRequests++;
                return;
            case RecoveryStage.RouteReplan:
                SetRecoveryStage(unit, RecoveryStage.DirectFallback);
                QueueLocalRepath(unit, clearHighLevelRoute: true);
                return;
            default:
                MarkUnreachable(unit);
                return;
        }
    }

    private void MarkUnreachable(int unit)
    {
        Units.Paths[unit] = null;
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Idle;
        Units.Velocities[unit] = Vector2.Zero;
        Units.BlockedByNavigation[unit] = true;
        SetRecoveryStage(unit, RecoveryStage.Unreachable);
    }

    private void SetRecoveryStage(int unit, RecoveryStage stage)
    {
        if (Units.RecoveryStages[unit] == stage)
        {
            return;
        }

        Units.RecoveryStages[unit] = stage;
        Units.RecoveryStableTimers[unit] = 0f;
        Units.RecoveryEventCounts[unit]++;
        Metrics.RecoveryEvents++;
    }

    private Vector2 ResolveUnitRouteWaypoint(int unit, Vector2 groupWaypoint)
    {
        if (_chokeController is null)
        {
            return groupWaypoint;
        }

        var chokeId = Units.ActiveChokeIds[unit];
        var definitions = _chokeController.Definitions;
        if ((uint)chokeId >= (uint)definitions.Length)
        {
            return groupWaypoint;
        }

        var choke = definitions[chokeId];
        var endpointToleranceSquared = 2f * 2f;
        if (Vector2.DistanceSquared(groupWaypoint, choke.A) <= endpointToleranceSquared ||
            Vector2.DistanceSquared(groupWaypoint, choke.B) <= endpointToleranceSquared)
        {
            return groupWaypoint + choke.Normal * Units.ChokeLaneOffsets[unit];
        }

        return groupWaypoint;
    }

    private void UpdatePreferredVelocities()
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            Units.PreferredVelocities[unit] = Vector2.Zero;
            if (Units.Modes[unit] == UnitMoveMode.Arrived &&
                Vector2.DistanceSquared(Units.Positions[unit], Units.SlotTargets[unit]) >
                8f * 8f)
            {
                Units.Paths[unit] = new UnitPath(
                    [Units.Positions[unit], Units.SlotTargets[unit]],
                    Units.CommandVersions[unit]);
                Units.Modes[unit] = UnitMoveMode.Moving;
            }

            if (Units.Modes[unit] != UnitMoveMode.Moving)
            {
                continue;
            }

            var path = Units.Paths[unit];
            if (path is null || path.CommandVersion != Units.CommandVersions[unit])
            {
                Units.Modes[unit] = UnitMoveMode.WaitingForPath;
                continue;
            }

            AdvancePathCursor(unit, path);
            var finalPoint = path.Cursor >= path.Points.Length - 1;
            var waypoint = path.Points[path.Cursor];
            var toWaypoint = waypoint - Units.Positions[unit];
            var distance = toWaypoint.Length();

            if (finalPoint && distance <= ArrivalStopRadius)
            {
                Units.Modes[unit] = UnitMoveMode.Arrived;
                Units.Paths[unit] = null;
                continue;
            }

            if (distance < 0.0001f)
            {
                continue;
            }

            var speed = Units.MaxSpeeds[unit];
            if (finalPoint && distance < ArrivalSlowRadius)
            {
                var factor = Math.Clamp(distance / ArrivalSlowRadius, 0.18f, 1f);
                speed *= factor * factor * (3f - 2f * factor);
            }

            Units.PreferredVelocities[unit] = toWaypoint / distance * speed;
        }
    }

    private void AdvancePathCursor(int unit, UnitPath path)
    {
        while (path.Cursor < path.Points.Length - 1)
        {
            var position = Units.Positions[unit];
            var point = path.Points[path.Cursor];
            var reachedRadius = Vector2.DistanceSquared(position, point) <=
                                WaypointRadius * WaypointRadius;
            var nextDirection = path.Points[path.Cursor + 1] - point;
            var crossedWaypointPlane = nextDirection.LengthSquared() > 0.0001f &&
                                       Vector2.Dot(position - point, nextDirection) >= 0f;
            if (!reachedRadius && !crossedWaypointPlane)
            {
                break;
            }

            path.Cursor++;
        }
    }

    private void Integrate(float delta)
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            Units.PreviousPositions[unit] = Units.Positions[unit];
            Units.Velocities[unit] = Units.NextVelocities[unit];
            var proposed = Units.Positions[unit] + Units.Velocities[unit] * delta;
            Units.Positions[unit] = World.ConstrainDisc(
                Units.Positions[unit],
                proposed,
                Units.Radii[unit]);
        }
    }

    private void SolveCollisions()
    {
        for (var iteration = 0; iteration < CollisionIterations; iteration++)
        {
            Array.Clear(Units.CollisionCorrections, 0, Units.Count);
            _spatialHash.Rebuild(Units);

            for (var unit = 0; unit < Units.Count; unit++)
            {
                var neighborCount = _spatialHash.Query(
                    Units.Positions[unit],
                    Units.Radii[unit] * 4f + 8f,
                    unit,
                    _collisionNeighbors);

                for (var neighborIndex = 0; neighborIndex < neighborCount; neighborIndex++)
                {
                    var neighbor = _collisionNeighbors[neighborIndex];
                    if (neighbor <= unit)
                    {
                        continue;
                    }

                    var offset = Units.Positions[neighbor] - Units.Positions[unit];
                    var minimumDistance = Units.Radii[unit] + Units.Radii[neighbor];
                    var distanceSquared = offset.LengthSquared();
                    if (distanceSquared >= minimumDistance * minimumDistance)
                    {
                        continue;
                    }

                    var distance = MathF.Sqrt(MathF.Max(distanceSquared, 0.000001f));
                    var normal = distanceSquared > 0.000001f
                        ? offset / distance
                        : DeterministicNormal(unit, neighbor);
                    var penetration = minimumDistance - distance;
                    Metrics.CollisionPairs++;
                    Metrics.MaximumPenetration = MathF.Max(
                        Metrics.MaximumPenetration,
                        penetration);

                    var inverseA = InversePushMass(Units.Modes[unit]);
                    var inverseB = InversePushMass(Units.Modes[neighbor]);
                    var totalInverse = inverseA + inverseB;
                    if (totalInverse <= 0f)
                    {
                        continue;
                    }

                    var correction = normal * (penetration + 0.01f) * 0.72f;
                    Units.CollisionCorrections[unit] -= correction * (inverseA / totalInverse);
                    Units.CollisionCorrections[neighbor] += correction * (inverseB / totalInverse);
                }
            }

            for (var unit = 0; unit < Units.Count; unit++)
            {
                var previous = Units.Positions[unit];
                Units.Positions[unit] = World.ConstrainDisc(
                    previous,
                    previous + Units.CollisionCorrections[unit],
                    Units.Radii[unit]);
            }
        }
    }

    private void UpdateProgress(float delta)
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            Units.RepathCooldowns[unit] = MathF.Max(0f, Units.RepathCooldowns[unit] - delta);
            if (Units.Modes[unit] != UnitMoveMode.Moving)
            {
                Units.ProgressOrigins[unit] = Units.Positions[unit];
                Units.ProgressTimers[unit] = 0f;
                continue;
            }

            if (Units.ChokePhases[unit] != ChokePhase.None)
            {
                Units.ProgressOrigins[unit] = Units.Positions[unit];
                Units.ProgressTimers[unit] = 0f;
                continue;
            }

            if (Vector2.DistanceSquared(
                    Units.Positions[unit], Units.SlotTargets[unit]) <= 70f * 70f)
            {
                Units.ProgressOrigins[unit] = Units.Positions[unit];
                Units.ProgressTimers[unit] = 0f;
                continue;
            }

            Units.RecoveryStableTimers[unit] += delta;
            if (Units.RecoveryStages[unit] != RecoveryStage.Normal &&
                Units.RecoveryStableTimers[unit] >= 2.5f)
            {
                Units.RecoveryStages[unit] = RecoveryStage.Normal;
                Units.RecoveryStableTimers[unit] = 0f;
                Units.RecoveryRetryCounts[unit] = 0;
            }

            Units.ProgressTimers[unit] += delta;
            var remainingDistance = RemainingPathDistance(unit, Units.Paths[unit]);
            if (remainingDistance <= Units.ProgressBestDistances[unit] - 5f)
            {
                Units.ProgressOrigins[unit] = Units.Positions[unit];
                Units.ProgressBestDistances[unit] = remainingDistance;
                Units.ProgressTimers[unit] = 0f;
                continue;
            }

            if (Units.ProgressTimers[unit] < 1.5f ||
                Units.RepathCooldowns[unit] > 0f ||
                Units.PathPending[unit])
            {
                continue;
            }

            Units.ProgressTimers[unit] = 0f;
            Units.ProgressOrigins[unit] = Units.Positions[unit];
            Units.RecoveryStableTimers[unit] = 0f;
            switch (Units.RecoveryStages[unit])
            {
                case RecoveryStage.Normal:
                    Units.AvoidanceSides[unit] = Units.AvoidanceSides[unit] == 0
                        ? (sbyte)((unit & 1) == 0 ? 1 : -1)
                        : (sbyte)-Units.AvoidanceSides[unit];
                    Units.AvoidanceLockTicks[unit] = 90;
                    Units.RepathCooldowns[unit] = 0.4f;
                    SetRecoveryStage(unit, RecoveryStage.AvoidanceFlip);
                    break;
                case RecoveryStage.AvoidanceFlip:
                    SetRecoveryStage(unit, RecoveryStage.LocalRepath);
                    QueueLocalRepath(unit, clearHighLevelRoute: false);
                    break;
                case RecoveryStage.LocalRepath:
                    SetRecoveryStage(unit, RecoveryStage.RouteReplan);
                    QueueNavigationReplan(
                        unit,
                        resetRecovery: false,
                        countInvalidation: false);
                    Metrics.RepathRequests++;
                    break;
                case RecoveryStage.RouteReplan:
                    SetRecoveryStage(unit, RecoveryStage.DirectFallback);
                    QueueLocalRepath(unit, clearHighLevelRoute: true);
                    break;
                case RecoveryStage.DirectFallback:
                    SetRecoveryStage(unit, RecoveryStage.WaitingForClearance);
                    Units.RepathCooldowns[unit] = 2f;
                    break;
                case RecoveryStage.WaitingForClearance:
                    if (Units.RecoveryRetryCounts[unit] < 2)
                    {
                        Units.RecoveryRetryCounts[unit]++;
                        SetRecoveryStage(unit, RecoveryStage.DirectFallback);
                        QueueLocalRepath(unit, clearHighLevelRoute: true);
                    }
                    else
                    {
                        MarkUnreachable(unit);
                    }
                    break;
            }
        }
    }

    private void UpdateDestinationReflow()
    {
        if (Metrics.Tick % DestinationReflowIntervalTicks != 0)
        {
            return;
        }

        for (var swap = 0; swap < MaximumDestinationSwapsPerPass; swap++)
        {
            if (!_slotReflow.TryFindSwap(
                    Units,
                    Metrics.Tick,
                    out var firstUnit,
                    out var secondUnit))
            {
                break;
            }

            (Units.SlotTargets[firstUnit], Units.SlotTargets[secondUnit]) =
                (Units.SlotTargets[secondUnit], Units.SlotTargets[firstUnit]);
            var nextEligibleTick = Metrics.Tick + DestinationReflowCooldownTicks;
            Units.SlotReflowCooldownTicks[firstUnit] = nextEligibleTick;
            Units.SlotReflowCooldownTicks[secondUnit] = nextEligibleTick;
            QueueDestinationRetarget(firstUnit, preserveTerminalHistory: true);
            QueueDestinationRetarget(secondUnit, preserveTerminalHistory: true);
            Metrics.DestinationSlotSwaps++;
        }
    }

    private void UpdateDestinationLocalRematch()
    {
        if (Metrics.Tick % DestinationLocalRematchIntervalTicks != 0)
        {
            return;
        }

        Span<int> rematchedUnits =
            stackalloc int[DestinationLocalRematcher.MaximumUnits];
        Span<Vector2> rematchedTargets =
            stackalloc Vector2[DestinationLocalRematcher.MaximumUnits];
        if (!_localRematcher.TryBuildPlan(
                Units,
                Metrics.Tick,
                rematchedUnits,
                rematchedTargets,
                out var rematchedCount))
        {
            return;
        }

        var nextEligibleTick =
            Metrics.Tick + DestinationLocalRematchCooldownTicks;
        for (var index = 0; index < rematchedCount; index++)
        {
            var unit = rematchedUnits[index];
            Units.SlotTargets[unit] = rematchedTargets[index];
            Units.SlotReflowCooldownTicks[unit] = nextEligibleTick;
        }

        for (var index = 0; index < rematchedCount; index++)
        {
            QueueDestinationRetarget(
                rematchedUnits[index], preserveTerminalHistory: true);
        }

        Metrics.DestinationLocalRematches++;
        Metrics.DestinationLocalRematchedUnits += rematchedCount;
    }

    private void UpdateDestinationOverflow()
    {
        if (Metrics.Tick % DestinationOverflowIntervalTicks != 0)
        {
            return;
        }

        for (var assignment = 0;
             assignment < MaximumDestinationOverflowsPerPass;
             assignment++)
        {
            if (!_overflowResolver.TryFindOverflowAssignment(
                    Units,
                    out var unit,
                    out var overflowTarget))
            {
                break;
            }

            Units.SlotTargets[unit] = overflowTarget;
            Units.DestinationOverflowed[unit] = true;
            QueueDestinationRetarget(unit);
            Metrics.DestinationOverflowAssignments++;
        }
    }

    private void UpdateDestinationYielding()
    {
        var activeYields = 0;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            var phase = Units.DestinationYieldPhases[unit];
            if (phase == DestinationYieldPhase.None)
            {
                continue;
            }

            activeYields++;
            switch (phase)
            {
                case DestinationYieldPhase.MovingAside:
                    if (Units.Modes[unit] == UnitMoveMode.Arrived ||
                        Vector2.DistanceSquared(
                            Units.Positions[unit], Units.SlotTargets[unit]) <= 5f * 5f)
                    {
                        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.Waiting;
                    }
                    else if (Metrics.Tick >= Units.DestinationYieldDeadlines[unit])
                    {
                        BeginDestinationYieldReturn(unit);
                    }
                    break;
                case DestinationYieldPhase.Waiting:
                    if (DestinationYieldTargetCleared(unit) ||
                        Metrics.Tick >= Units.DestinationYieldDeadlines[unit])
                    {
                        BeginDestinationYieldReturn(unit);
                    }
                    break;
                case DestinationYieldPhase.Returning:
                    if (Units.Modes[unit] == UnitMoveMode.Arrived ||
                        Vector2.DistanceSquared(
                            Units.Positions[unit], Units.SlotTargets[unit]) <= 5f * 5f)
                    {
                        ClearDestinationYield(unit);
                        activeYields--;
                    }
                    else if (Metrics.Tick >= Units.DestinationYieldDeadlines[unit])
                    {
                        ConvertDestinationYieldToOverflow(unit);
                        activeYields--;
                    }
                    break;
            }
        }

        if (Metrics.Tick % DestinationYieldIntervalTicks != 0)
        {
            return;
        }

        for (var start = 0;
             start < MaximumDestinationYieldStartsPerPass &&
             activeYields < MaximumActiveDestinationYields;
             start++)
        {
            if (!_yieldResolver.TryFindYield(
                    Units,
                    Metrics.Tick,
                    out var blockedUnit,
                    out var blockerUnit,
                    out var yieldPoint))
            {
                break;
            }

            Units.DestinationYieldReturnTargets[blockerUnit] =
                Units.SlotTargets[blockerUnit];
            Units.DestinationYieldForUnits[blockerUnit] = blockedUnit;
            Units.DestinationYieldForCommandVersions[blockerUnit] =
                Units.CommandVersions[blockedUnit];
            Units.DestinationYieldDeadlines[blockerUnit] =
                Metrics.Tick + DestinationYieldDeadlineTicks;
            Units.DestinationYieldPoints[blockerUnit] = yieldPoint;
            Units.DestinationYieldPhases[blockerUnit] =
                DestinationYieldPhase.MovingAside;
            Units.SlotTargets[blockerUnit] = yieldPoint;
            QueueDestinationRetarget(blockerUnit);
            Metrics.DestinationYieldEvents++;
            activeYields++;
        }
    }

    private bool DestinationYieldTargetCleared(int yieldingUnit)
    {
        var targetUnit = Units.DestinationYieldForUnits[yieldingUnit];
        if ((uint)targetUnit >= (uint)Units.Count ||
            Units.CommandVersions[targetUnit] !=
            Units.DestinationYieldForCommandVersions[yieldingUnit] ||
            Units.DestinationOverflowed[targetUnit])
        {
            return true;
        }

        return Units.Modes[targetUnit] == UnitMoveMode.Arrived ||
               Vector2.DistanceSquared(
                   Units.Positions[targetUnit], Units.SlotTargets[targetUnit]) <=
               18f * 18f;
    }

    private void BeginDestinationYieldReturn(int unit)
    {
        Units.SlotTargets[unit] = Units.DestinationYieldReturnTargets[unit];
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.Returning;
        Units.DestinationYieldDeadlines[unit] =
            Metrics.Tick + DestinationYieldReturnDeadlineTicks;
        QueueDestinationRetarget(unit);
    }

    private void ConvertDestinationYieldToOverflow(int unit)
    {
        var overflowTarget = Units.DestinationYieldPoints[unit];
        ClearDestinationYield(unit);
        Units.SlotTargets[unit] = overflowTarget;
        Units.DestinationOverflowed[unit] = true;
        QueueDestinationRetarget(unit);
        Metrics.DestinationOverflowAssignments++;
    }

    private void ClearDestinationYield(int unit)
    {
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
        Units.DestinationYieldReturnTargets[unit] = Units.SlotTargets[unit];
        Units.DestinationYieldPoints[unit] = Units.SlotTargets[unit];
        Units.DestinationYieldForUnits[unit] = -1;
        Units.DestinationYieldForCommandVersions[unit] = 0;
        Units.DestinationYieldDeadlines[unit] = 0;
        Units.DestinationYieldCooldownTicks[unit] =
            Metrics.Tick + DestinationYieldCooldownTicks;
    }

    private void QueueDestinationRetarget(
        int unit,
        bool preserveTerminalHistory = false)
    {
        Units.CommandVersions[unit]++;
        Units.Paths[unit] = null;
        Units.RouteWaypoints[unit] = [];
        Units.PathPending[unit] = true;
        Units.Modes[unit] = UnitMoveMode.WaitingForPath;
        Units.ActiveChokeIds[unit] = -1;
        Units.ChokeDirections[unit] = 0;
        Units.ChokePhases[unit] = ChokePhase.None;
        Units.ChokeAdmitted[unit] = false;
        Units.ProgressOrigins[unit] = Units.Positions[unit];
        Units.ProgressTimers[unit] = 0f;
        Units.ProgressBestDistances[unit] = Vector2.Distance(
            Units.Positions[unit], Units.SlotTargets[unit]);
        Units.DestinationBestDistances[unit] = Units.ProgressBestDistances[unit];
        Units.DestinationStallTicks[unit] = 0;
        if (!preserveTerminalHistory)
        {
            Units.DestinationNearTicks[unit] = 0;
        }
        Units.RepathCooldowns[unit] = 0f;
        _pathRequests.Enqueue(new PathRequest(unit, Units.CommandVersions[unit]));
    }

    private float RemainingPathDistance(int unit, UnitPath? path)
    {
        if (path is null || path.Points.Length == 0)
        {
            return Vector2.Distance(Units.Positions[unit], Units.SlotTargets[unit]);
        }

        var cursor = Math.Clamp(path.Cursor, 0, path.Points.Length - 1);
        var distance = Vector2.Distance(Units.Positions[unit], path.Points[cursor]);
        for (var index = cursor + 1; index < path.Points.Length; index++)
        {
            distance += Vector2.Distance(path.Points[index - 1], path.Points[index]);
        }

        return distance;
    }

    private void UpdateMetrics()
    {
        Metrics.MovingUnits = 0;
        Metrics.ArrivedUnits = 0;
        Metrics.WaitingForPathUnits = 0;
        Metrics.UnreachableUnits = 0;
        Metrics.MaximumDestinationStallTicks = 0;
        Metrics.MaximumDestinationNearTicks = 0;
        Metrics.ActiveDestinationYields = 0;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            Metrics.MaximumDestinationStallTicks = Math.Max(
                Metrics.MaximumDestinationStallTicks,
                Units.DestinationStallTicks[unit]);
            Metrics.MaximumDestinationNearTicks = Math.Max(
                Metrics.MaximumDestinationNearTicks,
                Units.DestinationNearTicks[unit]);
            if (Units.DestinationYieldPhases[unit] != DestinationYieldPhase.None)
            {
                Metrics.ActiveDestinationYields++;
            }
            switch (Units.Modes[unit])
            {
                case UnitMoveMode.Moving:
                    Metrics.MovingUnits++;
                    break;
                case UnitMoveMode.Arrived:
                    Metrics.ArrivedUnits++;
                    break;
                case UnitMoveMode.WaitingForPath:
                    Metrics.WaitingForPathUnits++;
                    break;
            }

            if (Units.RecoveryStages[unit] == RecoveryStage.Unreachable)
            {
                Metrics.UnreachableUnits++;
            }
        }

        Metrics.PendingPathRequests = _pathRequests.Count;
    }

    private void ValidateUnitIndex(int unit)
    {
        if ((uint)unit >= (uint)Units.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }
    }

    private static float InversePushMass(UnitMoveMode mode) => mode switch
    {
        UnitMoveMode.Hold => 0f,
        UnitMoveMode.Arrived => 0.28f,
        UnitMoveMode.Idle => 0.65f,
        _ => 1f
    };

    private static Vector2 DeterministicNormal(int left, int right)
    {
        var hash = unchecked((uint)(left * 73856093) ^ (uint)(right * 19349663));
        var angle = hash / (float)uint.MaxValue * MathF.Tau;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static double ElapsedMilliseconds(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
}
