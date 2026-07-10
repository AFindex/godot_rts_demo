using System.Numerics;

namespace RtsDemo.Simulation;

public enum UnitCommandIntent : byte
{
    None,
    Move,
    AttackMove,
    AttackTarget,
    Stop,
    Hold
}

public enum CombatPhase : byte
{
    None,
    Searching,
    Chasing,
    Attacking
}

public enum CombatPositioningKind : byte
{
    Melee,
    Ranged
}

public readonly record struct CombatProfileSnapshot(
    float MaximumHealth,
    float AttackDamage,
    float AttackRange,
    float AcquisitionRange,
    float AttackCooldownSeconds,
    float AttackWindupSeconds,
    float LeashDistance,
    CombatPositioningKind Positioning = CombatPositioningKind.Ranged)
{
    public static CombatProfileSnapshot Standard => new(
        MaximumHealth: 45f,
        AttackDamage: 8f,
        AttackRange: 34f,
        AcquisitionRange: 155f,
        AttackCooldownSeconds: 0.72f,
        AttackWindupSeconds: 0.18f,
        LeashDistance: 260f);

    public void Validate()
    {
        if (!float.IsFinite(MaximumHealth) || MaximumHealth <= 0f ||
            !float.IsFinite(AttackDamage) || AttackDamage < 0f ||
            !float.IsFinite(AttackRange) || AttackRange < 0f ||
            !float.IsFinite(AcquisitionRange) || AcquisitionRange < AttackRange ||
            !float.IsFinite(AttackCooldownSeconds) || AttackCooldownSeconds <= 0f ||
            !float.IsFinite(AttackWindupSeconds) || AttackWindupSeconds < 0f ||
            AttackWindupSeconds > AttackCooldownSeconds ||
            !float.IsFinite(LeashDistance) || LeashDistance < AcquisitionRange ||
            !Enum.IsDefined(Positioning))
        {
            throw new ArgumentOutOfRangeException(
                nameof(CombatProfileSnapshot),
                "Combat profile values are invalid or internally inconsistent.");
        }
    }
}

/// <summary>
/// Combat state is indexed by stable UnitStore IDs but kept separate from movement data.
/// This keeps command intent and the resumable AttackMove route independent from chase paths.
/// </summary>
public sealed class CombatStore
{
    public CombatStore(int capacity)
    {
        Teams = new int[capacity];
        Health = new float[capacity];
        MaximumHealth = new float[capacity];
        AttackDamage = new float[capacity];
        AttackRanges = new float[capacity];
        AcquisitionRanges = new float[capacity];
        AttackCooldownDurations = new float[capacity];
        AttackWindupDurations = new float[capacity];
        LeashDistances = new float[capacity];
        PositioningKinds = new CombatPositioningKind[capacity];
        CommandIntents = new UnitCommandIntent[capacity];
        Phases = new CombatPhase[capacity];
        TargetUnits = new int[capacity];
        AttackMoveGoals = new Vector2[capacity];
        EngagementOrigins = new Vector2[capacity];
        LastChaseTargets = new Vector2[capacity];
        AttackSlotTargets = new Vector2[capacity];
        AttackSlotAngles = new float[capacity];
        AttackSlotRadii = new float[capacity];
        HasAttackSlots = new bool[capacity];
        CooldownRemaining = new float[capacity];
        WindupRemaining = new float[capacity];
        ChaseRepathRemaining = new float[capacity];
        Array.Fill(TargetUnits, -1);
    }

    public int[] Teams { get; }
    public float[] Health { get; }
    public float[] MaximumHealth { get; }
    public float[] AttackDamage { get; }
    public float[] AttackRanges { get; }
    public float[] AcquisitionRanges { get; }
    public float[] AttackCooldownDurations { get; }
    public float[] AttackWindupDurations { get; }
    public float[] LeashDistances { get; }
    public CombatPositioningKind[] PositioningKinds { get; }
    public UnitCommandIntent[] CommandIntents { get; }
    public CombatPhase[] Phases { get; }
    public int[] TargetUnits { get; }
    public Vector2[] AttackMoveGoals { get; }
    public Vector2[] EngagementOrigins { get; }
    public Vector2[] LastChaseTargets { get; }
    public Vector2[] AttackSlotTargets { get; }
    public float[] AttackSlotAngles { get; }
    public float[] AttackSlotRadii { get; }
    public bool[] HasAttackSlots { get; }
    public float[] CooldownRemaining { get; }
    public float[] WindupRemaining { get; }
    public float[] ChaseRepathRemaining { get; }

    public void Register(int unit, int team, Vector2 position, CombatProfileSnapshot profile)
    {
        profile.Validate();
        Teams[unit] = team;
        Health[unit] = profile.MaximumHealth;
        MaximumHealth[unit] = profile.MaximumHealth;
        AttackDamage[unit] = profile.AttackDamage;
        AttackRanges[unit] = profile.AttackRange;
        AcquisitionRanges[unit] = profile.AcquisitionRange;
        AttackCooldownDurations[unit] = profile.AttackCooldownSeconds;
        AttackWindupDurations[unit] = profile.AttackWindupSeconds;
        LeashDistances[unit] = profile.LeashDistance;
        PositioningKinds[unit] = profile.Positioning;
        CommandIntents[unit] = UnitCommandIntent.None;
        Phases[unit] = CombatPhase.None;
        TargetUnits[unit] = -1;
        AttackMoveGoals[unit] = position;
        EngagementOrigins[unit] = position;
        LastChaseTargets[unit] = position;
        AttackSlotTargets[unit] = position;
    }

    public void SetCommand(
        int unit,
        UnitCommandIntent intent,
        Vector2 goal,
        int targetUnit = -1)
    {
        CommandIntents[unit] = intent;
        Phases[unit] = intent is UnitCommandIntent.AttackMove or
            UnitCommandIntent.AttackTarget or
            UnitCommandIntent.Stop or UnitCommandIntent.Hold
            ? CombatPhase.Searching
            : CombatPhase.None;
        TargetUnits[unit] = intent == UnitCommandIntent.AttackTarget
            ? targetUnit
            : -1;
        HasAttackSlots[unit] = false;
        WindupRemaining[unit] = 0f;
        ChaseRepathRemaining[unit] = 0f;
        if (intent == UnitCommandIntent.AttackMove)
        {
            AttackMoveGoals[unit] = goal;
        }
    }
}
