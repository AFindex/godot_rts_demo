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

public sealed class UnitStore
{
    public UnitStore(int capacity)
    {
        Capacity = capacity;
        Positions = new Vector2[capacity];
        PreviousPositions = new Vector2[capacity];
        Velocities = new Vector2[capacity];
        PreferredVelocities = new Vector2[capacity];
        NextVelocities = new Vector2[capacity];
        SlotTargets = new Vector2[capacity];
        Radii = new float[capacity];
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
        Array.Fill(ActiveChokeIds, -1);
    }

    public int Count { get; private set; }
    public int Capacity { get; }
    public Vector2[] Positions { get; }
    public Vector2[] PreviousPositions { get; }
    public Vector2[] Velocities { get; }
    public Vector2[] PreferredVelocities { get; }
    public Vector2[] NextVelocities { get; }
    public Vector2[] SlotTargets { get; }
    public float[] Radii { get; }
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

    public int Add(Vector2 position, float radius, float maxSpeed, float acceleration)
    {
        if (Count >= Capacity)
        {
            throw new InvalidOperationException($"Unit capacity {Capacity} exceeded.");
        }

        var index = Count++;
        Positions[index] = position;
        PreviousPositions[index] = position;
        SlotTargets[index] = position;
        Radii[index] = radius;
        MaxSpeeds[index] = maxSpeed;
        Accelerations[index] = acceleration;
        Modes[index] = UnitMoveMode.Idle;
        RouteWaypoints[index] = [];
        ActiveChokeIds[index] = -1;
        ProgressOrigins[index] = position;
        ProgressBestDistances[index] = 0f;
        return index;
    }
}
