using System.Numerics;

namespace RtsDemo.Simulation;

public enum UnitMoveMode : byte
{
    Idle,
    WaitingForPath,
    Moving,
    Arrived,
    Hold
}

public enum RecoveryStage : byte
{
    Normal,
    AvoidanceFlip,
    LocalRepath,
    RouteReplan,
    DirectFallback,
    WaitingForClearance,
    Unreachable
}

public enum DestinationYieldPhase : byte
{
    None,
    MovingAside,
    Waiting,
    Returning
}

public enum UnitMovementGoalKind : byte
{
    None,
    GroundPoint,
    UnitBody,
    BuildingBoundary,
    ResourceBoundary,
    DropOffBoundary,
    AttackRange,
    FollowRange,
    ProductionExit,
    ConstructionEvacuation
}

public enum UnitMovementLegResult : byte
{
    None,
    InProgress,
    Reached,
    SettledShort,
    Unreachable,
    TargetInvalidated,
    Canceled
}

public static class UnitFacing
{
    public const float LegacyTurnRateRadiansPerSecond = 1000f;
    private const float DirectionEpsilonSquared = 0.000001f;

    public static Vector2 Direction(float radians) =>
        new(MathF.Cos(radians), MathF.Sin(radians));

    public static float FromDirection(Vector2 direction) =>
        MathF.Atan2(direction.Y, direction.X);

    public static float Normalize(float radians)
    {
        radians %= MathF.Tau;
        if (radians > MathF.PI) radians -= MathF.Tau;
        if (radians <= -MathF.PI) radians += MathF.Tau;
        return radians;
    }

    public static float Difference(float from, float to) =>
        Normalize(to - from);

    public static float RotateToward(
        float current,
        Vector2 desiredDirection,
        float maximumRadians)
    {
        if (desiredDirection.LengthSquared() <= DirectionEpsilonSquared ||
            maximumRadians <= 0f)
            return Normalize(current);
        var desired = FromDirection(desiredDirection);
        var difference = Difference(current, desired);
        return Normalize(current + Math.Clamp(
            difference, -maximumRadians, maximumRadians));
    }

    public static bool IsWithin(
        float current,
        Vector2 desiredDirection,
        float halfAngleRadians)
    {
        if (desiredDirection.LengthSquared() <= DirectionEpsilonSquared)
            return true;
        return MathF.Abs(Difference(
                   current, FromDirection(desiredDirection))) <=
               halfAngleRadians + 0.0001f;
    }

    public static float Interpolate(float previous, float current, float weight) =>
        Normalize(previous + Difference(previous, current) *
            Math.Clamp(weight, 0f, 1f));
}

public readonly record struct UnitMovementSnapshot(
    int Unit,
    UnitMovementGoalKind GoalKind,
    Vector2 NavigationTarget,
    SimRect TargetBounds,
    float TargetRadius,
    int TargetId,
    UnitMovementLegResult Result);

public sealed class UnitStore
{
    private int[] _aliveUnits;
    private int[] _alivePositions;
    private int _aliveCount;

    public UnitStore(int capacity)
    {
        Capacity = capacity;
        Positions = new Vector2[capacity];
        Alive = new bool[capacity];
        _aliveUnits = new int[capacity];
        _alivePositions = new int[capacity];
        Array.Fill(_alivePositions, -1);
        PreviousPositions = new Vector2[capacity];
        Facings = new float[capacity];
        PreviousFacings = new float[capacity];
        TurnRatesRadiansPerSecond = new float[capacity];
        Velocities = new Vector2[capacity];
        PreferredVelocities = new Vector2[capacity];
        NextVelocities = new Vector2[capacity];
        SlotTargets = new Vector2[capacity];
        MoveGoals = new Vector2[capacity];
        MovementGoalKinds = new UnitMovementGoalKind[capacity];
        MovementGoalBounds = new SimRect[capacity];
        MovementGoalRadii = new float[capacity];
        MovementGoalTargetIds = new int[capacity];
        MovementLegResults = new UnitMovementLegResult[capacity];
        Radii = new float[capacity];
        MovementClasses = new MovementClass[capacity];
        NavigationRadii = new float[capacity];
        MaxSpeeds = new float[capacity];
        Accelerations = new float[capacity];
        Modes = new UnitMoveMode[capacity];
        Paths = new UnitPath?[capacity];
        RouteWaypoints = new Vector2[capacity][];
        CommandVersions = new int[capacity];
        PathPending = new bool[capacity];
        AvoidanceSides = new sbyte[capacity];
        AvoidanceLockTicks = new short[capacity];
        ProgressOrigins = new Vector2[capacity];
        ProgressTimers = new float[capacity];
        ProgressBestDistances = new float[capacity];
        RepathCooldowns = new float[capacity];
        CollisionCorrections = new Vector2[capacity];
        ActiveChokeIds = new int[capacity];
        ChokeDirections = new sbyte[capacity];
        ChokeLaneOffsets = new float[capacity];
        ChokePhases = new ChokePhase[capacity];
        ChokeAdmitted = new bool[capacity];
        ChokeQueueRanks = new int[capacity];
        ChokeWaitTicks = new int[capacity];
        BlockedByNavigation = new bool[capacity];
        RecoveryStages = new RecoveryStage[capacity];
        RecoveryEventCounts = new int[capacity];
        RecoveryStableTimers = new float[capacity];
        RecoveryRetryCounts = new byte[capacity];
        MovementGroupIds = new int[capacity];
        MovementGroupSizes = new int[capacity];
        SlotReflowCooldownTicks = new long[capacity];
        DestinationBestDistances = new float[capacity];
        DestinationStallTicks = new int[capacity];
        DestinationNearTicks = new int[capacity];
        DestinationOverflowed = new bool[capacity];
        DestinationYieldPhases = new DestinationYieldPhase[capacity];
        DestinationYieldReturnTargets = new Vector2[capacity];
        DestinationYieldPoints = new Vector2[capacity];
        DestinationYieldForUnits = new int[capacity];
        DestinationYieldForCommandVersions = new int[capacity];
        DestinationYieldDeadlines = new long[capacity];
        DestinationYieldCooldownTicks = new long[capacity];
        DynamicBlockageTicks = new int[capacity];
        DynamicBlockageBestDistances = new float[capacity];
        ReservationMigrationTicks = new int[capacity];
        Array.Fill(ActiveChokeIds, -1);
        Array.Fill(DestinationYieldForUnits, -1);
        Array.Fill(MovementGoalTargetIds, -1);
    }

    public int Count { get; private set; }
    public int AliveCount => _aliveCount;
    public ReadOnlySpan<int> AliveUnits =>
        _aliveUnits.AsSpan(0, _aliveCount);
    public int Capacity { get; private set; }
    public Vector2[] Positions { get; private set; }
    public bool[] Alive { get; private set; }
    public Vector2[] PreviousPositions { get; private set; }
    public float[] Facings { get; private set; }
    public float[] PreviousFacings { get; private set; }
    public float[] TurnRatesRadiansPerSecond { get; private set; }
    public Vector2[] Velocities { get; private set; }
    public Vector2[] PreferredVelocities { get; private set; }
    public Vector2[] NextVelocities { get; private set; }
    public Vector2[] SlotTargets { get; private set; }
    public Vector2[] MoveGoals { get; private set; }
    public UnitMovementGoalKind[] MovementGoalKinds { get; private set; }
    public SimRect[] MovementGoalBounds { get; private set; }
    public float[] MovementGoalRadii { get; private set; }
    public int[] MovementGoalTargetIds { get; private set; }
    public UnitMovementLegResult[] MovementLegResults { get; private set; }
    public float[] Radii { get; private set; }
    public MovementClass[] MovementClasses { get; private set; }
    public float[] NavigationRadii { get; private set; }
    public float[] MaxSpeeds { get; private set; }
    public float[] Accelerations { get; private set; }
    public UnitMoveMode[] Modes { get; private set; }
    public UnitPath?[] Paths { get; private set; }
    public Vector2[][] RouteWaypoints { get; private set; }
    public int[] CommandVersions { get; private set; }
    public bool[] PathPending { get; private set; }
    public sbyte[] AvoidanceSides { get; private set; }
    public short[] AvoidanceLockTicks { get; private set; }
    public Vector2[] ProgressOrigins { get; private set; }
    public float[] ProgressTimers { get; private set; }
    public float[] ProgressBestDistances { get; private set; }
    public float[] RepathCooldowns { get; private set; }
    public Vector2[] CollisionCorrections { get; private set; }
    public int[] ActiveChokeIds { get; private set; }
    public sbyte[] ChokeDirections { get; private set; }
    public float[] ChokeLaneOffsets { get; private set; }
    public ChokePhase[] ChokePhases { get; private set; }
    public bool[] ChokeAdmitted { get; private set; }
    public int[] ChokeQueueRanks { get; private set; }
    public int[] ChokeWaitTicks { get; private set; }
    public bool[] BlockedByNavigation { get; private set; }
    public RecoveryStage[] RecoveryStages { get; private set; }
    public int[] RecoveryEventCounts { get; private set; }
    public float[] RecoveryStableTimers { get; private set; }
    public byte[] RecoveryRetryCounts { get; private set; }
    public int[] MovementGroupIds { get; private set; }
    public int[] MovementGroupSizes { get; private set; }
    public long[] SlotReflowCooldownTicks { get; private set; }
    public float[] DestinationBestDistances { get; private set; }
    public int[] DestinationStallTicks { get; private set; }
    public int[] DestinationNearTicks { get; private set; }
    public bool[] DestinationOverflowed { get; private set; }
    public DestinationYieldPhase[] DestinationYieldPhases { get; private set; }
    public Vector2[] DestinationYieldReturnTargets { get; private set; }
    public Vector2[] DestinationYieldPoints { get; private set; }
    public int[] DestinationYieldForUnits { get; private set; }
    public int[] DestinationYieldForCommandVersions { get; private set; }
    public long[] DestinationYieldDeadlines { get; private set; }
    public long[] DestinationYieldCooldownTicks { get; private set; }
    public int[] DynamicBlockageTicks { get; private set; }
    public float[] DynamicBlockageBestDistances { get; private set; }
    public int[] ReservationMigrationTicks { get; private set; }

    internal void EnsureCapacity(int capacity)
    {
        if (capacity <= Capacity)
        {
            return;
        }

        var previous = Capacity;
        Positions = Grow(Positions, capacity);
        Alive = Grow(Alive, capacity);
        _aliveUnits = Grow(_aliveUnits, capacity);
        _alivePositions = Grow(_alivePositions, capacity);
        Array.Fill(_alivePositions, -1, previous, capacity - previous);
        PreviousPositions = Grow(PreviousPositions, capacity);
        Facings = Grow(Facings, capacity);
        PreviousFacings = Grow(PreviousFacings, capacity);
        TurnRatesRadiansPerSecond = Grow(
            TurnRatesRadiansPerSecond, capacity);
        Velocities = Grow(Velocities, capacity);
        PreferredVelocities = Grow(PreferredVelocities, capacity);
        NextVelocities = Grow(NextVelocities, capacity);
        SlotTargets = Grow(SlotTargets, capacity);
        MoveGoals = Grow(MoveGoals, capacity);
        MovementGoalKinds = Grow(MovementGoalKinds, capacity);
        MovementGoalBounds = Grow(MovementGoalBounds, capacity);
        MovementGoalRadii = Grow(MovementGoalRadii, capacity);
        MovementGoalTargetIds = Grow(MovementGoalTargetIds, capacity);
        MovementLegResults = Grow(MovementLegResults, capacity);
        Radii = Grow(Radii, capacity);
        MovementClasses = Grow(MovementClasses, capacity);
        NavigationRadii = Grow(NavigationRadii, capacity);
        MaxSpeeds = Grow(MaxSpeeds, capacity);
        Accelerations = Grow(Accelerations, capacity);
        Modes = Grow(Modes, capacity);
        Paths = Grow(Paths, capacity);
        RouteWaypoints = Grow(RouteWaypoints, capacity);
        CommandVersions = Grow(CommandVersions, capacity);
        PathPending = Grow(PathPending, capacity);
        AvoidanceSides = Grow(AvoidanceSides, capacity);
        AvoidanceLockTicks = Grow(AvoidanceLockTicks, capacity);
        ProgressOrigins = Grow(ProgressOrigins, capacity);
        ProgressTimers = Grow(ProgressTimers, capacity);
        ProgressBestDistances = Grow(ProgressBestDistances, capacity);
        RepathCooldowns = Grow(RepathCooldowns, capacity);
        CollisionCorrections = Grow(CollisionCorrections, capacity);
        ActiveChokeIds = Grow(ActiveChokeIds, capacity);
        ChokeDirections = Grow(ChokeDirections, capacity);
        ChokeLaneOffsets = Grow(ChokeLaneOffsets, capacity);
        ChokePhases = Grow(ChokePhases, capacity);
        ChokeAdmitted = Grow(ChokeAdmitted, capacity);
        ChokeQueueRanks = Grow(ChokeQueueRanks, capacity);
        ChokeWaitTicks = Grow(ChokeWaitTicks, capacity);
        BlockedByNavigation = Grow(BlockedByNavigation, capacity);
        RecoveryStages = Grow(RecoveryStages, capacity);
        RecoveryEventCounts = Grow(RecoveryEventCounts, capacity);
        RecoveryStableTimers = Grow(RecoveryStableTimers, capacity);
        RecoveryRetryCounts = Grow(RecoveryRetryCounts, capacity);
        MovementGroupIds = Grow(MovementGroupIds, capacity);
        MovementGroupSizes = Grow(MovementGroupSizes, capacity);
        SlotReflowCooldownTicks = Grow(SlotReflowCooldownTicks, capacity);
        DestinationBestDistances = Grow(DestinationBestDistances, capacity);
        DestinationStallTicks = Grow(DestinationStallTicks, capacity);
        DestinationNearTicks = Grow(DestinationNearTicks, capacity);
        DestinationOverflowed = Grow(DestinationOverflowed, capacity);
        DestinationYieldPhases = Grow(DestinationYieldPhases, capacity);
        DestinationYieldReturnTargets = Grow(
            DestinationYieldReturnTargets, capacity);
        DestinationYieldPoints = Grow(DestinationYieldPoints, capacity);
        DestinationYieldForUnits = Grow(
            DestinationYieldForUnits, capacity);
        DestinationYieldForCommandVersions = Grow(
            DestinationYieldForCommandVersions, capacity);
        DestinationYieldDeadlines = Grow(DestinationYieldDeadlines, capacity);
        DestinationYieldCooldownTicks = Grow(
            DestinationYieldCooldownTicks, capacity);
        DynamicBlockageTicks = Grow(DynamicBlockageTicks, capacity);
        DynamicBlockageBestDistances = Grow(
            DynamicBlockageBestDistances, capacity);
        ReservationMigrationTicks = Grow(
            ReservationMigrationTicks, capacity);

        Array.Fill(MovementGoalTargetIds, -1, previous, capacity - previous);
        Array.Fill(ActiveChokeIds, -1, previous, capacity - previous);
        Array.Fill(DestinationYieldForUnits, -1, previous, capacity - previous);
        for (var unit = previous; unit < capacity; unit++)
        {
            RouteWaypoints[unit] = [];
        }
        Capacity = capacity;
    }

    public int Add(
        Vector2 position,
        float radius,
        float maxSpeed,
        float acceleration,
        float turnRateRadiansPerSecond =
            UnitFacing.LegacyTurnRateRadiansPerSecond,
        float facingRadians = 0f)
    {
        if (!float.IsFinite(turnRateRadiansPerSecond) ||
            turnRateRadiansPerSecond <= 0f)
            throw new ArgumentOutOfRangeException(nameof(turnRateRadiansPerSecond));
        if (!float.IsFinite(facingRadians))
            throw new ArgumentOutOfRangeException(nameof(facingRadians));
        if (Count >= Capacity)
        {
            throw new InvalidOperationException($"Unit capacity {Capacity} exceeded.");
        }

        var index = Count++;
        SetAlive(index, true);
        Positions[index] = position;
        PreviousPositions[index] = position;
        Facings[index] = UnitFacing.Normalize(facingRadians);
        PreviousFacings[index] = Facings[index];
        TurnRatesRadiansPerSecond[index] = turnRateRadiansPerSecond;
        SlotTargets[index] = position;
        MoveGoals[index] = position;
        MovementGoalKinds[index] = UnitMovementGoalKind.None;
        MovementGoalTargetIds[index] = -1;
        MovementLegResults[index] = UnitMovementLegResult.None;
        DestinationYieldReturnTargets[index] = position;
        DestinationYieldPoints[index] = position;
        var clearance = MovementClearance.FromPhysicalRadius(radius);
        Radii[index] = radius;
        MovementClasses[index] = clearance.Class;
        NavigationRadii[index] = clearance.NavigationRadius;
        MaxSpeeds[index] = maxSpeed;
        Accelerations[index] = acceleration;
        Modes[index] = UnitMoveMode.Idle;
        RouteWaypoints[index] = [];
        ActiveChokeIds[index] = -1;
        ProgressOrigins[index] = position;
        ProgressBestDistances[index] = 0f;
        DynamicBlockageBestDistances[index] = 0f;
        return index;
    }

    internal void ApplyMovementProfile(
        int unit,
        in UnitMovementProfileSnapshot profile)
    {
        if ((uint)unit >= (uint)Count ||
            !float.IsFinite(profile.PhysicalRadius) ||
            profile.PhysicalRadius <= 0f ||
            !float.IsFinite(profile.MaximumSpeed) ||
            profile.MaximumSpeed <= 0f ||
            !float.IsFinite(profile.Acceleration) ||
            profile.Acceleration <= 0f ||
            !float.IsFinite(profile.TurnRateRadiansPerSecond) ||
            profile.TurnRateRadiansPerSecond <= 0f)
            throw new ArgumentOutOfRangeException(nameof(unit));
        var clearance = MovementClearance.FromPhysicalRadius(
            profile.PhysicalRadius);
        Radii[unit] = profile.PhysicalRadius;
        MovementClasses[unit] = clearance.Class;
        NavigationRadii[unit] = clearance.NavigationRadius;
        MaxSpeeds[unit] = profile.MaximumSpeed;
        Accelerations[unit] = profile.Acceleration;
        TurnRatesRadiansPerSecond[unit] = profile.TurnRateRadiansPerSecond;
        Paths[unit] = null;
        RouteWaypoints[unit] = [];
        PathPending[unit] = false;
        Velocities[unit] = Vector2.Zero;
        PreferredVelocities[unit] = Vector2.Zero;
        NextVelocities[unit] = Vector2.Zero;
        CommandVersions[unit]++;
    }

    internal void CopyRuntimeStateFrom(UnitStore source)
    {
        if (source.Capacity != Capacity)
        {
            throw new InvalidOperationException("Unit runtime capacity mismatch.");
        }
        Count = source.Count;
        Copy(source.Positions, Positions);
        Copy(source.Alive, Alive);
        RebuildAliveIndex();
        Copy(source.PreviousPositions, PreviousPositions);
        Copy(source.Facings, Facings);
        Copy(source.PreviousFacings, PreviousFacings);
        Copy(source.TurnRatesRadiansPerSecond, TurnRatesRadiansPerSecond);
        Copy(source.Velocities, Velocities);
        Copy(source.PreferredVelocities, PreferredVelocities);
        Copy(source.NextVelocities, NextVelocities);
        Copy(source.SlotTargets, SlotTargets);
        Copy(source.MoveGoals, MoveGoals);
        Copy(source.MovementGoalKinds, MovementGoalKinds);
        Copy(source.MovementGoalBounds, MovementGoalBounds);
        Copy(source.MovementGoalRadii, MovementGoalRadii);
        Copy(source.MovementGoalTargetIds, MovementGoalTargetIds);
        Copy(source.MovementLegResults, MovementLegResults);
        Copy(source.Radii, Radii);
        Copy(source.MovementClasses, MovementClasses);
        Copy(source.NavigationRadii, NavigationRadii);
        Copy(source.MaxSpeeds, MaxSpeeds);
        Copy(source.Accelerations, Accelerations);
        Copy(source.Modes, Modes);
        Copy(source.CommandVersions, CommandVersions);
        Copy(source.PathPending, PathPending);
        Copy(source.AvoidanceSides, AvoidanceSides);
        Copy(source.AvoidanceLockTicks, AvoidanceLockTicks);
        Copy(source.ProgressOrigins, ProgressOrigins);
        Copy(source.ProgressTimers, ProgressTimers);
        Copy(source.ProgressBestDistances, ProgressBestDistances);
        Copy(source.RepathCooldowns, RepathCooldowns);
        Copy(source.CollisionCorrections, CollisionCorrections);
        Copy(source.ActiveChokeIds, ActiveChokeIds);
        Copy(source.ChokeDirections, ChokeDirections);
        Copy(source.ChokeLaneOffsets, ChokeLaneOffsets);
        Copy(source.ChokePhases, ChokePhases);
        Copy(source.ChokeAdmitted, ChokeAdmitted);
        Copy(source.ChokeQueueRanks, ChokeQueueRanks);
        Copy(source.ChokeWaitTicks, ChokeWaitTicks);
        Copy(source.BlockedByNavigation, BlockedByNavigation);
        Copy(source.RecoveryStages, RecoveryStages);
        Copy(source.RecoveryEventCounts, RecoveryEventCounts);
        Copy(source.RecoveryStableTimers, RecoveryStableTimers);
        Copy(source.RecoveryRetryCounts, RecoveryRetryCounts);
        Copy(source.MovementGroupIds, MovementGroupIds);
        Copy(source.MovementGroupSizes, MovementGroupSizes);
        Copy(source.SlotReflowCooldownTicks, SlotReflowCooldownTicks);
        Copy(source.DestinationBestDistances, DestinationBestDistances);
        Copy(source.DestinationStallTicks, DestinationStallTicks);
        Copy(source.DestinationNearTicks, DestinationNearTicks);
        Copy(source.DestinationOverflowed, DestinationOverflowed);
        Copy(source.DestinationYieldPhases, DestinationYieldPhases);
        Copy(source.DestinationYieldReturnTargets, DestinationYieldReturnTargets);
        Copy(source.DestinationYieldPoints, DestinationYieldPoints);
        Copy(source.DestinationYieldForUnits, DestinationYieldForUnits);
        Copy(source.DestinationYieldForCommandVersions, DestinationYieldForCommandVersions);
        Copy(source.DestinationYieldDeadlines, DestinationYieldDeadlines);
        Copy(source.DestinationYieldCooldownTicks, DestinationYieldCooldownTicks);
        Copy(source.DynamicBlockageTicks, DynamicBlockageTicks);
        Copy(source.DynamicBlockageBestDistances, DynamicBlockageBestDistances);
        Copy(source.ReservationMigrationTicks, ReservationMigrationTicks);

        for (var unit = 0; unit < Capacity; unit++)
        {
            var path = source.Paths[unit];
            if (path is null)
            {
                Paths[unit] = null;
            }
            else
            {
                var copy = new UnitPath(path.Points.ToArray(), path.CommandVersion)
                {
                    Cursor = path.Cursor
                };
                Paths[unit] = copy;
            }
            RouteWaypoints[unit] = source.RouteWaypoints[unit]?.ToArray() ?? [];
        }
    }

    internal void SetRuntimeCount(int count)
    {
        if ((uint)count > (uint)Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        Count = count;
    }

    internal void SetAlive(int unit, bool alive)
    {
        if ((uint)unit >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(unit));
        if (Alive[unit] == alive) return;
        Alive[unit] = alive;
        if (alive)
        {
            var insert = _aliveCount;
            while (insert > 0 && _aliveUnits[insert - 1] > unit)
            {
                _aliveUnits[insert] = _aliveUnits[insert - 1];
                _alivePositions[_aliveUnits[insert]] = insert;
                insert--;
            }
            _aliveUnits[insert] = unit;
            _alivePositions[unit] = insert;
            _aliveCount++;
            return;
        }

        var position = _alivePositions[unit];
        if ((uint)position >= (uint)_aliveCount)
            throw new InvalidOperationException(
                $"Alive unit index is inconsistent for unit {unit}.");
        for (var index = position; index < _aliveCount - 1; index++)
        {
            _aliveUnits[index] = _aliveUnits[index + 1];
            _alivePositions[_aliveUnits[index]] = index;
        }
        _aliveCount--;
        _aliveUnits[_aliveCount] = 0;
        _alivePositions[unit] = -1;
    }

    internal void RebuildAliveIndex()
    {
        _aliveCount = 0;
        Array.Fill(_alivePositions, -1);
        for (var unit = 0; unit < Count; unit++)
        {
            if (!Alive[unit]) continue;
            _alivePositions[unit] = _aliveCount;
            _aliveUnits[_aliveCount++] = unit;
        }
        Array.Clear(_aliveUnits, _aliveCount, Capacity - _aliveCount);
    }

    private static void Copy<T>(T[] source, T[] destination) =>
        Array.Copy(source, destination, source.Length);

    private static T[] Grow<T>(T[] source, int capacity)
    {
        var result = new T[capacity];
        Array.Copy(source, result, source.Length);
        return result;
    }
}
