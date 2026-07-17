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
    public UnitStore(int capacity)
    {
        Capacity = capacity;
        Positions = new Vector2[capacity];
        Alive = new bool[capacity];
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
    public int Capacity { get; }
    public Vector2[] Positions { get; }
    public bool[] Alive { get; }
    public Vector2[] PreviousPositions { get; }
    public float[] Facings { get; }
    public float[] PreviousFacings { get; }
    public float[] TurnRatesRadiansPerSecond { get; }
    public Vector2[] Velocities { get; }
    public Vector2[] PreferredVelocities { get; }
    public Vector2[] NextVelocities { get; }
    public Vector2[] SlotTargets { get; }
    public Vector2[] MoveGoals { get; }
    public UnitMovementGoalKind[] MovementGoalKinds { get; }
    public SimRect[] MovementGoalBounds { get; }
    public float[] MovementGoalRadii { get; }
    public int[] MovementGoalTargetIds { get; }
    public UnitMovementLegResult[] MovementLegResults { get; }
    public float[] Radii { get; }
    public MovementClass[] MovementClasses { get; }
    public float[] NavigationRadii { get; }
    public float[] MaxSpeeds { get; }
    public float[] Accelerations { get; }
    public UnitMoveMode[] Modes { get; }
    public UnitPath?[] Paths { get; }
    public Vector2[][] RouteWaypoints { get; }
    public int[] CommandVersions { get; }
    public bool[] PathPending { get; }
    public sbyte[] AvoidanceSides { get; }
    public short[] AvoidanceLockTicks { get; }
    public Vector2[] ProgressOrigins { get; }
    public float[] ProgressTimers { get; }
    public float[] ProgressBestDistances { get; }
    public float[] RepathCooldowns { get; }
    public Vector2[] CollisionCorrections { get; }
    public int[] ActiveChokeIds { get; }
    public sbyte[] ChokeDirections { get; }
    public float[] ChokeLaneOffsets { get; }
    public ChokePhase[] ChokePhases { get; }
    public bool[] ChokeAdmitted { get; }
    public int[] ChokeQueueRanks { get; }
    public int[] ChokeWaitTicks { get; }
    public bool[] BlockedByNavigation { get; }
    public RecoveryStage[] RecoveryStages { get; }
    public int[] RecoveryEventCounts { get; }
    public float[] RecoveryStableTimers { get; }
    public byte[] RecoveryRetryCounts { get; }
    public int[] MovementGroupIds { get; }
    public int[] MovementGroupSizes { get; }
    public long[] SlotReflowCooldownTicks { get; }
    public float[] DestinationBestDistances { get; }
    public int[] DestinationStallTicks { get; }
    public int[] DestinationNearTicks { get; }
    public bool[] DestinationOverflowed { get; }
    public DestinationYieldPhase[] DestinationYieldPhases { get; }
    public Vector2[] DestinationYieldReturnTargets { get; }
    public Vector2[] DestinationYieldPoints { get; }
    public int[] DestinationYieldForUnits { get; }
    public int[] DestinationYieldForCommandVersions { get; }
    public long[] DestinationYieldDeadlines { get; }
    public long[] DestinationYieldCooldownTicks { get; }
    public int[] DynamicBlockageTicks { get; }
    public float[] DynamicBlockageBestDistances { get; }
    public int[] ReservationMigrationTicks { get; }

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
        Alive[index] = true;
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

    private static void Copy<T>(T[] source, T[] destination) =>
        Array.Copy(source, destination, source.Length);
}
