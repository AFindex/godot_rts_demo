using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public readonly record struct TestUnitId(int Value);
public readonly record struct TestBuildingId(int Value);
public readonly record struct TestResourceNodeId(int Value);
public readonly record struct TestDropOffId(int Value);
public readonly record struct TestEconomyBaseId(int Value);
public readonly record struct TestGameplayBuildingId(int Value);
public readonly record struct TestProductionOrderId(int Value);
public sealed record TestControlGroupSelection(
    TestUnitId[] Units,
    TestGameplayBuildingId[] Buildings);

public enum TestTargetCommandKind : byte
{
    Move,
    AttackMove,
    Rally,
    Build
}

public sealed record TestTargetCommandRequest(
    int PlayerId,
    TestTargetCommandKind Kind,
    TestUnitId[] Units,
    TestGameplayBuildingId[] Buildings,
    string Label,
    int DataId);

public readonly record struct TestTargetCommandResult(
    bool Issued,
    bool Canceled,
    bool Queued,
    bool KeepTargeting,
    string Status = "",
    TestGameplayBuildingId Building = default);

public readonly record struct TestBuildTargetPreview(
    Vector2 Center,
    Vector2 Size,
    TestUnitId Builder,
    bool CanPlace,
    TestConstructionCommandCode Code,
    TestBuildingPlacementCode PlacementCode);

public enum TestAiStrategicPhase : byte
{
    Establishing,
    Developing,
    Mobilizing,
    Attacking,
    Recovering
}

public readonly record struct TestAiSnapshot(
    TestAiStrategicPhase Phase,
    long LastDecisionTick,
    int StateBytes,
    long LastAttackTick);

public enum TestProductionCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidProducer,
    WrongOwner,
    ProducerNotCompleted,
    WrongProducerType,
    QueueFull,
    InvalidRecipe,
    InsufficientMinerals,
    InsufficientVespeneGas,
    SupplyBlocked,
    MissingPrerequisite,
    InvalidOrder,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public readonly record struct TestProductionRequirementStatus(
    int BuildingTypeId,
    int RequiredCount,
    int CurrentCount)
{
    public bool Satisfied => CurrentCount >= RequiredCount;
}

public readonly record struct TestProductionAvailabilitySnapshot(
    TestProductionCommandCode Code,
    TestProductionRequirementStatus[] Requirements)
{
    public bool Available => Code == TestProductionCommandCode.Success;
}

public enum TestResearchCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidResearcher,
    WrongOwner,
    ResearcherNotCompleted,
    WrongResearcherType,
    QueueFull,
    InvalidTechnology,
    MissingPrerequisite,
    MaximumLevel,
    AlreadyQueued,
    MutuallyExclusive,
    InsufficientMinerals,
    InsufficientVespeneGas,
    InvalidOrder,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public readonly record struct TestResearchOrderId(int Value);
public readonly record struct TestResearchResult(
    TestResearchCommandCode Code,
    TestResearchOrderId OrderId)
{
    public bool Succeeded => Code == TestResearchCommandCode.Success;
}
public readonly record struct TestResearchAvailabilitySnapshot(
    TestResearchCommandCode Code,
    int CurrentLevel,
    int[] CurrentValues,
    int[] RequiredValues)
{
    public bool Available => Code == TestResearchCommandCode.Success;
}

public enum TestProductionOrderState : byte
{
    Queued,
    Producing,
    WaitingForExit
}

public enum TestRallyTargetKind : byte
{
    None,
    Ground,
    ResourceNode,
    FriendlyUnit
}

public readonly record struct TestRallyTarget(
    TestRallyTargetKind Kind,
    Vector2 Position,
    TestResourceNodeId ResourceNode,
    TestUnitId Unit);

public readonly record struct TestProductionResult(
    TestProductionCommandCode Code,
    TestProductionOrderId OrderId)
{
    public bool Succeeded => Code == TestProductionCommandCode.Success;
}

public readonly record struct TestProductionQueueSnapshot(
    int OrderCount,
    TestProductionOrderState? ActiveState,
    float ActiveProgress,
    Vector2? RallyPoint);

public readonly record struct TestProductionBatchResult(
    int Producers,
    int Planned,
    int Succeeded,
    TestProductionCommandCode[] Results);

public readonly record struct TestProductionGroupSnapshot(
    int Producers,
    int TotalOrders,
    int ActiveProducers,
    int[] QueueLengths);

public enum TestBuildingLifecycleState : byte
{
    ReservedApproach,
    BlockedAtStart,
    Approaching,
    Constructing,
    WaitingForBuilder,
    Completed,
    Canceled,
    Destroyed
}

public enum TestConstructionCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidBuilder,
    WrongOwner,
    BuilderBusy,
    InvalidProfile,
    InsufficientMinerals,
    InsufficientVespeneGas,
    SupplyBlocked,
    PlacementRejected,
    RefineryNodeRequired,
    InvalidRefineryNode,
    RefineryAlreadyBound,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant,
    QueueFull
}

public readonly record struct TestConstructionResult(
    TestConstructionCommandCode Code,
    TestGameplayBuildingId BuildingId,
    TestBuildingPlacementCode PlacementCode)
{
    public bool Succeeded => Code == TestConstructionCommandCode.Success;
}

public readonly record struct TestGameplayBuildingSnapshot(
    TestGameplayBuildingId Id,
    int TypeId,
    string Name,
    Vector2 Size,
    int ReservationId,
    TestBuildingId FootprintId,
    TestBuildingLifecycleState State,
    Vector2 Center,
    Vector2 AccessPoint,
    float Progress,
    float Health,
    float MaximumHealth,
    float Armor,
    CombatAttribute Attributes,
    float ArmorUpgradePerLevel,
    TestResourceNodeId RefineryNode,
    TestUnitId BuilderUnit);

public enum TestEconomyResourceKind : byte
{
    Minerals,
    VespeneGas
}

public enum TestEconomyTransactionCode : byte
{
    Success,
    InvalidPlayer,
    InvalidCost,
    InsufficientMinerals,
    InsufficientVespeneGas,
    SupplyBlocked
}

public enum TestGatherCommandCode : byte
{
    Success,
    InvalidUnit,
    UnitNotWorker,
    WrongOwner,
    InvalidNode,
    RefineryRequired,
    ResourceDepleted,
    MissingDropOff,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant,
    CapabilityUnavailable
}

public enum TestReturnCargoCommandCode : byte
{
    Success,
    InvalidUnit,
    UnitNotWorker,
    WrongOwner,
    NoCargo,
    MissingDropOff,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public enum TestWorkerEconomyState : byte
{
    None,
    Idle,
    GoingToResource,
    WaitingForResource,
    Gathering,
    ReturningCargo,
    WaitingForDropOff
}

public enum TestGathererCapability : byte
{
    None,
    NormalWorker,
    Mule
}

public readonly record struct TestPlayerEconomySnapshot(
    int Minerals,
    int VespeneGas,
    int SupplyUsed,
    int SupplyCapacity);

public readonly record struct TestResourceNodeSnapshot(
    TestResourceNodeId Id,
    TestEconomyResourceKind Kind,
    Vector2 Position,
    int Remaining,
    int ActiveNormal,
    int AssignedNormal,
    int WaitingNormal,
    int NormalActiveSlots,
    int IdealNormalAssignments,
    int ActiveMules,
    int AssignedMules,
    bool Operational)
{
    public int ActiveHarvesters => ActiveNormal + ActiveMules;
    public int HarvesterCapacity => IdealNormalAssignments;
}

public readonly record struct TestWorkerEconomySnapshot(
    TestWorkerEconomyState State,
    TestResourceNodeId TargetNode,
    TestEconomyResourceKind CargoKind,
    int CargoAmount,
    TestGathererCapability Capability);

public readonly record struct TestWorkerCycleSnapshot(
    TestUnitId Unit,
    TestWorkerEconomyState State,
    TestResourceNodeId TargetNode,
    TestEconomyResourceKind CargoKind,
    int CargoAmount,
    Vector2 MovementGoal);

public readonly record struct TestDropOffApproachSnapshot(
    bool Found,
    Vector2 Center,
    Vector2 HalfExtents,
    Vector2 InteractionHalfExtents,
    Vector2 Target,
    float DistanceSquared);

public enum TestMapVisibility : byte
{
    Hidden,
    Explored,
    Visible
}

public enum TestPlayerOrderCommandCode : byte
{
    Success,
    InvalidPlayer,
    EmptySelection,
    InvalidUnit,
    WrongOwner,
    InvalidTarget,
    FriendlyTarget,
    TargetNotVisible,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant,
    ContextActionUnavailable
}

public readonly record struct TestPlayerResourceView(
    TestResourceNodeId NodeId,
    TestMapVisibility Visibility,
    int KnownRemaining);

public readonly record struct TestPlayerViewSnapshot(
    int PlayerId,
    TestUnitId[] Units,
    TestGameplayBuildingId[] Buildings,
    TestPlayerResourceView[] Resources,
    int HiddenCells,
    int ExploredCells,
    int VisibleCells);

public enum TestMatchPhase : byte
{
    Setup,
    Running,
    Completed
}

public enum TestMatchPlayerStatus : byte
{
    Active,
    Defeated,
    Victorious
}

public readonly record struct TestPlayerCapabilitySnapshot(
    int PlayerId,
    TestMatchPlayerStatus Status,
    bool EstablishedPresence,
    int ActiveBuildings,
    int TownHalls,
    int ProductionFacilities,
    int Workers,
    int CombatUnits,
    bool HasAnyProduction,
    bool IsEliminationRisk);

public readonly record struct TestMatchSnapshot(
    TestMatchPhase Phase,
    long CompletedTick,
    int WinnerPlayerId,
    TestPlayerCapabilitySnapshot[] Players);

public readonly record struct TestEconomyBaseSnapshot(
    TestEconomyBaseId Id,
    TestGameplayBuildingId TownHall,
    Vector2 Position,
    bool Operational,
    int MineralNodes,
    int VespeneNodes,
    int AssignedMineralWorkers,
    int IdealMineralWorkers,
    int AssignedVespeneWorkers,
    int IdealVespeneWorkers)
{
    public int AssignedWorkers =>
        AssignedMineralWorkers + AssignedVespeneWorkers;
    public int IdealWorkers => IdealMineralWorkers + IdealVespeneWorkers;
    public float Saturation => IdealWorkers > 0
        ? AssignedWorkers / (float)IdealWorkers
        : 0f;
}

public enum TestWorkerTransferCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidSourceBase,
    InvalidTargetBase,
    SameBase,
    InvalidCount,
    NoTargetResources,
    NoEligibleWorkers,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public readonly record struct TestWorkerTransferResult(
    TestWorkerTransferCommandCode Code,
    int RequestedWorkers,
    int TransferredWorkers)
{
    public bool Succeeded => Code == TestWorkerTransferCommandCode.Success;
}

public enum TestOrderKind : byte
{
    None,
    Move,
    AttackMove,
    AttackTarget,
    AttackBuilding,
    Stop,
    Hold,
    GatherResource,
    ResumeConstruction,
    ReturnCargo,
    FollowFriendly
}

public readonly record struct TestOrderSnapshot(
    TestOrderKind ActiveOrder,
    int PendingOrders,
    int CompletedQueuedOrders,
    int QueueOverflows,
    int ActiveTargetBuilding,
    int ActiveTargetUnit,
    bool ConstructionEvacuationActive,
    int ConstructionEvacuationBuilding,
    Vector2 ConstructionEvacuationTarget);

public sealed class TestCommandLog
{
    internal TestCommandLog(SimulationCommandLogSnapshot snapshot)
    {
        Backend = snapshot;
    }

    internal SimulationCommandLogSnapshot Backend { get; }
    public int FormatVersion => Backend.FormatVersion;
    public int EntryCount => Backend.Entries.Length;
    public int CanonicalByteCount => Backend.CanonicalBytes.Length;
    public ulong StableHash => Backend.StableHash;

    public bool TryCanonicalRoundTrip(out TestCommandLog? roundTripped)
    {
        var succeeded = SimulationCommandLogSnapshot.TryDeserialize(
            Backend.CanonicalBytes,
            out var snapshot,
            out _);
        roundTripped = succeeded && snapshot is not null
            ? new TestCommandLog(snapshot)
            : null;
        return succeeded;
    }

    public bool RejectsUnsupportedVersion()
    {
        var payload = Backend.CanonicalBytes.ToArray();
        payload[4]++;
        return !SimulationCommandLogSnapshot.TryDeserialize(
            payload, out _, out var validation) &&
            validation.Code == CommandLogValidationCode.UnsupportedVersion;
    }

    public bool RejectsTruncatedPayload()
    {
        var payload = Backend.CanonicalBytes.AsSpan(
            0, Backend.CanonicalBytes.Length - 1);
        return !SimulationCommandLogSnapshot.TryDeserialize(
            payload, out _, out var validation) &&
            validation.Code is CommandLogValidationCode.TruncatedEntry or
                CommandLogValidationCode.InvalidUnitList;
    }

    public TestCommandLog WithTargetOffset(int entryIndex, Vector2 offset)
    {
        if ((uint)entryIndex >= (uint)Backend.Entries.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIndex));
        }
        var entries = new RecordedSimulationCommand[Backend.Entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var source = Backend.Entries[index];
            entries[index] = source with
            {
                TargetPosition = index == entryIndex
                    ? source.TargetPosition + offset
                    : source.TargetPosition,
                Units = source.Units.ToArray()
            };
        }
        return new TestCommandLog(new SimulationCommandLogSnapshot(entries));
    }
}

public sealed class TestEconomyCommandLog
{
    internal TestEconomyCommandLog(EconomyCommandLogSnapshot snapshot)
    {
        Backend = snapshot;
    }

    internal EconomyCommandLogSnapshot Backend { get; }
    public int FormatVersion => EconomyCommandLogSnapshot.CurrentFormatVersion;
    public int EntryCount => Backend.Entries.Length;
    public int CanonicalByteCount => Backend.CanonicalBytes.Length;
    public ulong StableHash => Backend.StableHash;

    public bool TryCanonicalRoundTrip(out TestEconomyCommandLog? roundTripped)
    {
        var succeeded = EconomyCommandLogSnapshot.TryDeserialize(
            Backend.CanonicalBytes, out var snapshot, out _);
        roundTripped = succeeded && snapshot is not null
            ? new TestEconomyCommandLog(snapshot)
            : null;
        return succeeded;
    }

    public bool RejectsUnsupportedVersion()
    {
        var payload = Backend.CanonicalBytes.ToArray();
        payload[4]++;
        return !EconomyCommandLogSnapshot.TryDeserialize(
            payload, out _, out var validation) &&
            validation == EconomyCommandLogValidationCode.UnsupportedVersion;
    }

    public bool RejectsTruncatedPayload() =>
        !EconomyCommandLogSnapshot.TryDeserialize(
            Backend.CanonicalBytes.AsSpan(0, Backend.CanonicalBytes.Length - 1),
            out _,
            out var validation) &&
        validation == EconomyCommandLogValidationCode.PayloadTooShort;
}

public sealed class TestConstructionCommandLog
{
    internal TestConstructionCommandLog(ConstructionCommandLogSnapshot snapshot) =>
        Backend = snapshot;

    internal ConstructionCommandLogSnapshot Backend { get; }
    public int EntryCount => Backend.Entries.Length;
    public int CanonicalByteCount => Backend.CanonicalBytes.Length;
    public ulong StableHash => Backend.StableHash;

    public bool TryCanonicalRoundTrip(out TestConstructionCommandLog? roundTripped)
    {
        var succeeded = ConstructionCommandLogSnapshot.TryDeserialize(
            Backend.CanonicalBytes, out var snapshot, out _);
        roundTripped = succeeded && snapshot is not null
            ? new TestConstructionCommandLog(snapshot)
            : null;
        return succeeded;
    }

    public bool RejectsUnsupportedVersion()
    {
        var payload = Backend.CanonicalBytes.ToArray();
        payload[4]++;
        return !ConstructionCommandLogSnapshot.TryDeserialize(
            payload, out _, out var validation) &&
            validation == ConstructionCommandLogValidationCode.UnsupportedVersion;
    }

    public bool RejectsTruncatedPayload() =>
        !ConstructionCommandLogSnapshot.TryDeserialize(
            Backend.CanonicalBytes.AsSpan(0, Backend.CanonicalBytes.Length - 1),
            out _, out var validation) &&
        validation == ConstructionCommandLogValidationCode.PayloadTooShort;
}

public sealed class TestProductionCommandLog
{
    internal TestProductionCommandLog(ProductionCommandLogSnapshot snapshot) =>
        Backend = snapshot;
    internal ProductionCommandLogSnapshot Backend { get; }
    public int EntryCount => Backend.Entries.Length;
    public int CanonicalByteCount => Backend.CanonicalBytes.Length;
    public ulong StableHash => Backend.StableHash;
    public bool TryCanonicalRoundTrip(out TestProductionCommandLog? roundTripped)
    {
        var succeeded = ProductionCommandLogSnapshot.TryDeserialize(
            Backend.CanonicalBytes, out var snapshot, out _);
        roundTripped = succeeded && snapshot is not null
            ? new TestProductionCommandLog(snapshot)
            : null;
        return succeeded;
    }
    public bool RejectsUnsupportedVersion()
    {
        var payload = Backend.CanonicalBytes.ToArray();
        payload[4]++;
        return !ProductionCommandLogSnapshot.TryDeserialize(
            payload, out _, out var validation) &&
            validation == ProductionCommandLogValidationCode.UnsupportedVersion;
    }
    public bool RejectsTruncatedPayload() =>
        !ProductionCommandLogSnapshot.TryDeserialize(
            Backend.CanonicalBytes.AsSpan(0, Backend.CanonicalBytes.Length - 1),
            out _, out var validation) &&
        validation == ProductionCommandLogValidationCode.PayloadTooShort;
}

public sealed class TestReplayPackage
{
    internal TestReplayPackage(SimulationReplayPackageSnapshot snapshot)
    {
        Backend = snapshot;
    }

    internal SimulationReplayPackageSnapshot Backend { get; }
    public int FormatVersion => Backend.FormatVersion;
    public int InitialUnitCount => Backend.Units.Length;
    public int InitialBuildingCount => Backend.Buildings.Length;
    public int WorldCommandCount => Backend.WorldCommands.Length;
    public int EconomyCommandCount => Backend.EconomyCommandLog.Entries.Length;
    public int ConstructionCommandCount =>
        Backend.ConstructionCommandLog.Entries.Length;
    public int ProductionCommandCount =>
        Backend.ProductionCommandLog.Entries.Length;
    public int UnitCommandCount => Backend.CommandLog.Entries.Length;
    public int CanonicalByteCount => Backend.CanonicalBytes.Length;
    public ulong StableHash => Backend.StableHash;
    public ulong InitialStateHash => Backend.InitialStateHash;

    public bool TryCanonicalRoundTrip(out TestReplayPackage? roundTripped)
    {
        var succeeded = SimulationReplayPackageSnapshot.TryDeserialize(
            Backend.CanonicalBytes, out var snapshot, out _);
        roundTripped = succeeded && snapshot is not null
            ? new TestReplayPackage(snapshot)
            : null;
        return succeeded;
    }

    public bool RejectsUnsupportedVersion()
    {
        var payload = Backend.CanonicalBytes.ToArray();
        payload[4]++;
        return !SimulationReplayPackageSnapshot.TryDeserialize(
            payload, out _, out var validation) &&
            validation.Code == ReplayPackageValidationCode.UnsupportedVersion;
    }

    public bool RejectsTruncatedPayload()
    {
        return !SimulationReplayPackageSnapshot.TryDeserialize(
            Backend.CanonicalBytes.AsSpan(0, Backend.CanonicalBytes.Length - 1),
            out _,
            out var validation) &&
            validation.Code is ReplayPackageValidationCode.InvalidEconomyCommandLog or
                ReplayPackageValidationCode.InvalidCommandLog or
                ReplayPackageValidationCode.PayloadTooShort;
    }
}

public sealed class TestReplayCheckpoint
{
    internal TestReplayCheckpoint(SimulationReplayCheckpointSnapshot snapshot)
    {
        Backend = snapshot;
    }

    internal SimulationReplayCheckpointSnapshot Backend { get; }
    public int FormatVersion => Backend.FormatVersion;
    public long Tick => Backend.Tick;
    public int CanonicalByteCount => Backend.CanonicalBytes.Length;
    public ulong StableHash => Backend.StableHash;

    public bool TryCanonicalRoundTrip(out TestReplayCheckpoint? roundTripped)
    {
        var succeeded = SimulationReplayCheckpointSnapshot.TryDeserialize(
            Backend.CanonicalBytes, out var snapshot, out _);
        roundTripped = succeeded && snapshot is not null
            ? new TestReplayCheckpoint(snapshot)
            : null;
        return succeeded;
    }

    public bool RejectsUnsupportedVersion()
    {
        var payload = Backend.CanonicalBytes.ToArray();
        payload[4]++;
        return !SimulationReplayCheckpointSnapshot.TryDeserialize(
            payload, out _, out var validation) &&
            validation.Code == ReplayCheckpointValidationCode.UnsupportedVersion;
    }

    public bool RejectsTruncatedPayload() =>
        !SimulationReplayCheckpointSnapshot.TryDeserialize(
            Backend.CanonicalBytes.AsSpan(0, Backend.CanonicalBytes.Length - 1),
            out _,
            out var validation) &&
        validation.Code == ReplayCheckpointValidationCode.InvalidLength;

    public TestReplayCheckpoint WithStateHashOffset(ulong offset) =>
        new(new SimulationReplayCheckpointSnapshot(
            Backend.Tick,
            Backend.PackageHash,
            Backend.StateHash ^ offset));
}

public sealed class TestRuntimeStateCapture
{
    internal TestRuntimeStateCapture(SimulationRuntimeStateCapture backend)
    {
        Backend = backend;
    }

    internal SimulationRuntimeStateCapture Backend { get; }
    public long Tick => Backend.Tick;
    public ulong StateHash => Backend.StateHash;
}

public sealed class TestHotSnapshot
{
    internal TestHotSnapshot(SimulationHotSnapshot backend)
    {
        Backend = backend;
    }

    internal SimulationHotSnapshot Backend { get; }
    public int FormatVersion => Backend.FormatVersion;
    public long Tick => Backend.Tick;
    public ulong StateHash => Backend.StateHash;
    public ulong StableHash => Backend.StableHash;
    public int CanonicalByteCount => Backend.CanonicalBytes.Length;

    public bool TryCanonicalRoundTrip(out TestHotSnapshot? roundTripped)
    {
        var succeeded = SimulationHotSnapshot.TryDeserialize(
            Backend.CanonicalBytes, out var snapshot, out _);
        roundTripped = succeeded && snapshot is not null
            ? new TestHotSnapshot(snapshot)
            : null;
        return succeeded;
    }

    public bool RejectsUnsupportedVersion()
    {
        var payload = Backend.CanonicalBytes.ToArray();
        payload[4]++;
        return !SimulationHotSnapshot.TryDeserialize(
            payload, out _, out var validation) &&
            validation == HotSnapshotValidationCode.UnsupportedVersion;
    }

    public bool RejectsTruncatedPayload() =>
        !SimulationHotSnapshot.TryDeserialize(
            Backend.CanonicalBytes.AsSpan(0, Backend.CanonicalBytes.Length - 1),
            out _,
            out var validation) &&
        validation == HotSnapshotValidationCode.InvalidBody;

    internal TestHotSnapshot WithBodyByteFlip()
    {
        var payload = Backend.CanonicalBytes.ToArray();
        const int firstUnitPositionByte = 105;
        if (payload.Length <= firstUnitPositionByte)
        {
            throw new InvalidOperationException("Hot snapshot payload is too small.");
        }
        payload[firstUnitPositionByte] ^= 1;
        if (!SimulationHotSnapshot.TryDeserialize(
                payload, out var snapshot, out _) || snapshot is null)
        {
            throw new InvalidOperationException(
                "Chosen hot snapshot corruption must remain structurally valid.");
        }
        return new TestHotSnapshot(snapshot);
    }
}

public sealed class TestReplayTrace
{
    internal TestReplayTrace(SimulationReplayTrace trace, ulong finalHash)
    {
        Backend = trace;
        FinalHash = finalHash;
    }

    internal SimulationReplayTrace Backend { get; }
    public int SampleCount => Backend.Samples.Count;
    public ulong FinalHash { get; }

    public long FindFirstDivergence(TestReplayTrace other) =>
        SimulationReplayTrace.FindFirstDivergence(Backend, other.Backend);

    public bool MatchesFrom(TestReplayTrace other, long firstTick)
    {
        var first = Backend.Samples.Where(sample => sample.Tick >= firstTick).ToArray();
        var second = other.Backend.Samples.Where(sample => sample.Tick >= firstTick).ToArray();
        return first.SequenceEqual(second);
    }
}

public readonly record struct TestCombatProfile(
    float MaximumHealth = 45f,
    float AttackDamage = 8f,
    float AttackRange = 34f,
    float AcquisitionRange = 155f,
    float AttackCooldownSeconds = 0.72f,
    float AttackWindupSeconds = 0.18f,
    float LeashDistance = 260f,
    TestCombatPositioning Positioning = TestCombatPositioning.Ranged,
    float Armor = 0f,
    CombatAttribute Attributes = CombatAttribute.Biological,
    int AttacksPerVolley = 1,
    CombatAttribute BonusVs = CombatAttribute.None,
    float BonusDamage = 0f,
    float BaseUpgradeDamage = 0f,
    float BonusUpgradeDamage = 0f,
    float ProjectileSpeed = 0f,
    bool CanMoveDuringWindup = false,
    bool CanMoveDuringCooldown = false,
    int AutoTargetPriority = 0)
{
    public static TestCombatProfile Standard => new(
        45f, 8f, 34f, 155f, 0.72f, 0.18f, 260f);
}

public enum TestCombatPositioning : byte
{
    Melee,
    Ranged
}

public enum TestCombatState : byte
{
    None,
    Searching,
    Chasing,
    Attacking,
    Dead
}

public readonly record struct TestCombatSnapshot(
    TestUnitId Id,
    int Team,
    bool Alive,
    float Health,
    float MaximumHealth,
    TestUnitId? Target,
    TestCombatState State,
    bool HasAttackPosition,
    Vector2 AttackPosition,
    float WindupRemaining,
    float CooldownRemaining);

public readonly record struct TestCombatEvent(
    long Tick,
    ulong Sequence,
    CombatEventKind Kind,
    TestUnitId Attacker,
    CombatTargetKind TargetKind,
    int TargetId,
    float Damage,
    float RemainingHealth,
    float DamagePerAttack,
    int AttacksApplied,
    bool BonusApplied,
    int ProjectileId,
    Vector2 WorldPosition);

public readonly record struct TestCombatProjectileSnapshot(
    int Id,
    TestUnitId Attacker,
    CombatTargetKind TargetKind,
    int TargetId,
    Vector2 Position,
    float Speed);

public enum TestCombatProjectileVisualKind : byte
{
    Bolt,
    Orb,
    Volley
}

public enum TestCombatPresentationCueKind : byte
{
    Impact,
    Expired
}

public readonly record struct TestCombatPresentationProjectile(
    int Id,
    TestCombatProjectileVisualKind VisualKind,
    Vector2 Position,
    Vector2 Heading,
    Vector2[] Trail);

public readonly record struct TestCombatPresentationCue(
    ulong Sequence,
    TestCombatPresentationCueKind Kind,
    int ProjectileId,
    Vector2 Position,
    float NormalizedAge,
    float Damage,
    bool BonusApplied);

public readonly record struct TestCombatPresentationFrame(
    TestCombatPresentationProjectile[] Projectiles,
    TestCombatPresentationCue[] Cues,
    ulong LatestEventSequence,
    int LostEvents);

public readonly record struct TestCombatEventBatch(
    TestCombatEvent[] Events,
    ulong LatestSequence,
    int LostEvents);

public readonly record struct TestCombatDamagePreview(
    float DamagePerAttack,
    float TotalDamage,
    int AttacksApplied,
    bool BonusApplied);

public readonly record struct TestCombatAutoTargetScore(
    TestUnitId Target,
    float DistanceSquared,
    int Priority,
    bool WeaponBonusMatch,
    bool ArmedThreat,
    float TotalScore);

public enum TestCombatContactRole : byte
{
    Standard,
    MobileWeapon,
    FixedCooldown,
    MeleeContact,
    FixedWindup
}

public readonly record struct TestCombatContactSnapshot(
    TestCombatContactRole Role,
    float InverseMobility,
    int ResistanceRank);

public readonly record struct TestCombatContactResolution(
    TestCombatContactSnapshot Left,
    TestCombatContactSnapshot Right,
    float LeftCorrectionShare,
    float RightCorrectionShare);

public enum TestBuildingFootprintClass : byte
{
    Small,
    Medium,
    Large,
    Huge
}

public enum TestMovementClass : byte
{
    Small,
    Medium,
    Large
}

public enum TestBuildingPlacementCode : byte
{
    Success,
    InvalidFootprint,
    OutsideWorld,
    StaticObstacleOverlap,
    DynamicFootprintOverlap,
    UnitOverlap,
    InsufficientClearance,
    DisconnectsNavigation
}

public readonly record struct TestBuildingPlacementResult(
    TestBuildingPlacementCode Code,
    TestBuildingId BuildingId,
    Vector2 Size)
{
    public bool Succeeded => Code == TestBuildingPlacementCode.Success;
}

public enum TestStaticPlacementCode : byte
{
    Success,
    InvalidFootprint,
    OutsideWorld,
    StaticObstacleOverlap,
    DynamicFootprintOverlap,
    InsufficientClearance,
    DisconnectsNavigation
}

public enum TestDynamicStartValidationCode : byte
{
    NotEvaluated,
    Success,
    InvalidFootprint,
    UnitOverlap
}

public readonly record struct TestBuildingPlacementAssessment(
    TestStaticPlacementCode StaticCode,
    TestDynamicStartValidationCode DynamicCode,
    TestBuildingPlacementCode CombinedCode,
    int ConflictId)
{
    public bool Succeeded => CombinedCode == TestBuildingPlacementCode.Success;
}

public enum TestHardFootprintCommitCode : byte
{
    Success,
    StaticPlacementRejected,
    DynamicOccupantRejected
}

public readonly record struct TestHardFootprintCommitResult(
    TestHardFootprintCommitCode Code,
    TestBuildingId BuildingId,
    Vector2 Size,
    TestBuildingPlacementAssessment Assessment)
{
    public bool Succeeded => Code == TestHardFootprintCommitCode.Success;
}

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
    double EconomyMilliseconds,
    double CombatMilliseconds,
    double PathMilliseconds,
    double PreferredVelocityMilliseconds,
    double ChokeMilliseconds,
    double SpatialHashMilliseconds,
    double SteeringMilliseconds,
    double IntegrateMilliseconds,
    double CollisionMilliseconds,
    double RecoveryMilliseconds,
    double CommandMilliseconds,
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

public readonly record struct TestOperationInteractionSnapshot(
    int PointSelection,
    int SameTypeCount,
    int BoxSelectionCount,
    bool ZoomAnchorStable,
    bool EdgePanMoved,
    bool GroupDoubleTap,
    Vector2 FocusPosition)
{
    public bool Passed =>
        PointSelection == 0 && SameTypeCount == 2 && BoxSelectionCount == 3 &&
        ZoomAnchorStable && EdgePanMoved && GroupDoubleTap &&
        Vector2.Distance(FocusPosition, new Vector2(950f, 550f)) < 0.01f;
}

public readonly record struct TestMinimapInteractionSnapshot(
    bool RoundTrip,
    bool ViewportMapped,
    bool FocusResolved,
    bool CommandResolved,
    bool OutsideRejected,
    Vector2 CommandWorld)
{
    public bool Passed =>
        RoundTrip && ViewportMapped && FocusResolved && CommandResolved &&
        OutsideRejected && Vector2.Distance(CommandWorld, new Vector2(900f, 500f)) < 0.01f;
}

public readonly record struct TestClearanceBakeCommitSnapshot(
    ClearanceBakeCommitCode Code,
    ulong PreviousHash,
    ulong CandidateHash,
    int ReplannedUnits,
    int ReloadCount)
{
    public bool Succeeded => Code == ClearanceBakeCommitCode.Success;
}

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
public sealed partial class MovementTestRig
{
    private readonly StaticWorld _world;
    private readonly RtsSimulation _simulation;
    private readonly PortalGraphRoutePlanner? _routePlanner;
    private readonly ChokeController? _chokeController;
    private readonly NavigationMapSnapshot? _navigationMap;
    private readonly GameplayProfileCatalogSnapshot? _gameplayProfiles;
    private readonly ClearanceBakeSnapshot? _clearanceBake;
    private readonly ControlGroupManager _controlGroups;
    private readonly CombatPresentationComposer _combatPresentation = new();

    private MovementTestRig(
        StaticWorld world,
        RtsSimulation simulation,
        PortalGraphRoutePlanner? routePlanner,
        ChokeController? chokeController,
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles = null,
        ClearanceBakeSnapshot? clearanceBake = null)
    {
        _world = world;
        _simulation = simulation;
        _routePlanner = routePlanner;
        _chokeController = chokeController;
        _navigationMap = navigationMap;
        _gameplayProfiles = gameplayProfiles;
        _clearanceBake = clearanceBake;
        _controlGroups = new ControlGroupManager(simulation.Units.Capacity);
    }

    public long Tick => _simulation.Metrics.Tick;
    public int MaximumQueuedOrders => UnitCommandQueueStore.MaximumPendingOrders;
    public int UnitCount => _simulation.Units.Count;
    public Vector2 WorldMinimum => _world.Bounds.Min;
    public Vector2 WorldMaximum => _world.Bounds.Max;
    public int NavigationRevision => _world.NavigationRevision;
    public int BuildingCount => _world.DynamicOccupancy.Count;
    public int GameplayBuildingCount => _simulation.Construction.Count;
    public int NavigationFormatVersion => _navigationMap?.FormatVersion ?? 0;
    public ulong NavigationDataHash => _navigationMap?.StableHash ?? 0UL;
    public int LastNavigationInvalidations =>
        _simulation.Metrics.NavigationInvalidations;
    public ulong StateHash => _simulation.ComputeStateHash();

    internal StaticWorld RenderWorld => _world;
    internal RtsSimulation RenderSimulation => _simulation;
    internal PortalGraphRoutePlanner? RenderRoutePlanner => _routePlanner;
    internal ChokeController? RenderChokeController => _chokeController;

    public static MovementTestRig CreateOpenField(Vector2 size, int capacity)
    {
        var world = new StaticWorld(new SimRect(Vector2.Zero, size));
        return new MovementTestRig(
            world,
            new RtsSimulation(world, new GridPathProvider(world), capacity),
            null,
            null,
            null);
    }

    public static MovementTestRig CreateEconomyMap(
        Vector2 size,
        int capacity) => CreateOpenField(size, capacity);

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
            new GridPathProvider(world),
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

    public static MovementTestRig CreateReplayPackageMap(
        int capacity,
        NavigationMapSnapshot navigationMap,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        var world = navigationMap.CreateWorld();
        var routePlanner = navigationMap.CreateRoutePlanner(world);
        var chokeController = navigationMap.CreateChokeController();
        var simulation = new RtsSimulation(
            world,
            new GridPathProvider(world),
            capacity,
            routePlanner,
            chokeController,
            clearanceBake);
        return new MovementTestRig(
            world,
            simulation,
            routePlanner,
            chokeController,
            navigationMap,
            gameplayProfiles,
            clearanceBake);
    }

    public static MovementTestRig CreateBakeReloadMap(
        int capacity,
        NavigationMapSnapshot navigationMap,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot clearanceBake)
    {
        var world = navigationMap.CreateWorld();
        var routePlanner = navigationMap.CreateRoutePlanner(world);
        var chokeController = navigationMap.CreateChokeController();
        var provider = new GridPathProvider(
            world, clearanceBake.CellSize, clearanceBake);
        var simulation = new RtsSimulation(
            world,
            provider,
            capacity,
            routePlanner,
            chokeController,
            clearanceBake);
        return new MovementTestRig(
            world,
            simulation,
            routePlanner,
            chokeController,
            navigationMap,
            gameplayProfiles,
            clearanceBake);
    }

    public static MovementTestRig CreateClearanceChoiceMap(int capacity)
    {
        PortalNode[] portals =
        [
            new(0, new Vector2(450f, 220f), "Narrow west"),
            new(1, new Vector2(750f, 220f), "Narrow east"),
            new(2, new Vector2(450f, 550f), "Wide west"),
            new(3, new Vector2(750f, 550f), "Wide east")
        ];
        PortalEdge[] edges =
        [
            new(0, 1, 22f),
            new(2, 3, 96f)
        ];
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(Vector2.Zero, new Vector2(1200f, 700f)),
            [new SimRect(new Vector2(500f, 250f), new Vector2(700f, 450f))],
            portals,
            edges,
            [],
            out var snapshot,
            out var validation);
        if (!created || snapshot is null)
        {
            throw new InvalidOperationException(
                $"Clearance test map is invalid: {validation.FirstError}.");
        }

        var world = snapshot.CreateWorld();
        var routePlanner = snapshot.CreateRoutePlanner(world);
        var simulation = new RtsSimulation(
            world,
            new GridPathProvider(world),
            capacity,
            routePlanner);
        return new MovementTestRig(
            world,
            simulation,
            routePlanner,
            null,
            snapshot);
    }

    public static MovementTestRig CreateConnectivityGuardMap(int capacity)
    {
        var world = new StaticWorld(
            new SimRect(Vector2.Zero, new Vector2(800f, 500f)),
            new SimRect(new Vector2(344f, 0f), new Vector2(456f, 210f)),
            new SimRect(new Vector2(344f, 290f), new Vector2(456f, 500f)));
        return new MovementTestRig(
            world,
            new RtsSimulation(world, new GridPathProvider(world), capacity),
            null,
            null,
            null);
    }

    public TestUnitId Spawn(
        Vector2 position,
        float radius = 7.5f,
        float maximumSpeed = 128f,
        float acceleration = 720f) =>
        new(_simulation.AddUnit(position, radius, maximumSpeed, acceleration));

    public TestUnitId Spawn(
        Vector2 position,
        UnitMovementProfileSnapshot profile) =>
        new(_simulation.AddUnit(position, profile));

    public void RegisterPlayer(
        int playerId,
        int minerals,
        int vespeneGas,
        int supplyCapacity,
        int supplyUsed = 0) =>
        _simulation.Economy.Players.RegisterPlayer(
            playerId, minerals, vespeneGas, supplyCapacity, supplyUsed);

    public TestUnitId SpawnWorker(
        Vector2 position,
        int playerId,
        float radius = 7.5f,
        float maximumSpeed = 150f,
        float acceleration = 720f,
        TestCombatProfile? combatProfile = null) =>
        new(_simulation.AddWorker(
            position, playerId, radius, maximumSpeed, acceleration,
            combatProfile.HasValue
                ? ToBackendCombat(combatProfile.Value)
                : null));

    public TestUnitId SpawnMule(
        Vector2 position,
        int playerId,
        float radius = 10f,
        float maximumSpeed = 165f,
        float acceleration = 800f) =>
        new(_simulation.AddGatherer(
            position, playerId, GathererCapability.Mule,
            radius, maximumSpeed, acceleration));

    public TestResourceNodeId AddResourceNode(
        TestEconomyResourceKind kind,
        Vector2 position,
        int amount,
        int harvestBatch,
        float harvestSeconds,
        int harvesterCapacity,
        bool requiresRefinery = false,
        bool operational = true) =>
        new(_simulation.Economy.AddResourceNode(
            (EconomyResourceKind)kind,
            position,
            amount,
            harvestBatch,
            harvestSeconds,
            harvesterCapacity,
            requiresRefinery,
            operational).Value);

    public TestDropOffId AddResourceDropOff(
        int playerId,
        Vector2 position,
        bool acceptsMinerals = true,
        bool acceptsVespene = true) =>
        new(_simulation.Economy.AddDropOff(
            playerId, position, acceptsMinerals, acceptsVespene).Value);

    public void SetResourceDropOffOperational(
        TestDropOffId dropOff,
        bool operational) =>
        _simulation.Economy.SetDropOffOperational(
            new EconomyDropOffId(dropOff.Value), operational);

    public void SetRefineryOperational(
        TestResourceNodeId nodeId,
        bool value,
        int playerId = 1) =>
        _simulation.SetRefineryOperational(
            playerId, new EconomyResourceNodeId(nodeId.Value), value);

    public TestGatherCommandCode Gather(
        int issuingPlayerId,
        TestUnitId worker,
        TestResourceNodeId nodeId) =>
        (TestGatherCommandCode)_simulation.IssueGather(
            issuingPlayerId,
            worker.Value,
            new EconomyResourceNodeId(nodeId.Value)).Code;

    public TestReturnCargoCommandCode ReturnCargo(
        int issuingPlayerId,
        TestUnitId worker,
        bool queued = false) =>
        (TestReturnCargoCommandCode)_simulation.IssueReturnCargo(
            issuingPlayerId, worker.Value, queued).Code;

    public TestEconomyTransactionCode Spend(
        int playerId,
        int minerals,
        int vespeneGas,
        int supply = 0) =>
        (TestEconomyTransactionCode)_simulation.Economy.Players.TrySpend(
            playerId,
            new EconomyCost(minerals, vespeneGas, supply)).Code;

    public void Refund(
        int playerId,
        int minerals,
        int vespeneGas,
        int supply = 0,
        float fraction = 1f) =>
        _simulation.Economy.Players.Refund(
            playerId,
            new EconomyCost(minerals, vespeneGas, supply),
            fraction);

    public TestPlayerEconomySnapshot ObservePlayerEconomy(int playerId)
    {
        var snapshot = _simulation.Economy.Players.Snapshot(playerId);
        return new TestPlayerEconomySnapshot(
            snapshot.Minerals,
            snapshot.VespeneGas,
            snapshot.SupplyUsed,
            snapshot.SupplyCapacity);
    }

    public TestResourceNodeSnapshot ObserveResourceNode(
        TestResourceNodeId nodeId)
    {
        var snapshot = _simulation.Economy.ObserveResourceNode(
            new EconomyResourceNodeId(nodeId.Value));
        return new TestResourceNodeSnapshot(
            nodeId,
            (TestEconomyResourceKind)snapshot.Kind,
            snapshot.Position,
            snapshot.Remaining,
            snapshot.ActiveNormal,
            snapshot.AssignedNormal,
            snapshot.WaitingNormal,
            snapshot.NormalActiveSlots,
            snapshot.IdealNormalAssignments,
            snapshot.ActiveMules,
            snapshot.AssignedMules,
            snapshot.Operational);
    }

    public TestWorkerEconomySnapshot ObserveWorkerEconomy(TestUnitId worker)
    {
        var snapshot = _simulation.Economy.Worker(worker.Value);
        return new TestWorkerEconomySnapshot(
            (TestWorkerEconomyState)snapshot.State,
            new TestResourceNodeId(snapshot.TargetNode.Value),
            (TestEconomyResourceKind)snapshot.CargoKind,
            snapshot.CargoAmount,
            (TestGathererCapability)snapshot.Capability);
    }

    public TestWorkerCycleSnapshot[] ObservePlayerWorkers(int playerId)
    {
        var workers = new List<TestWorkerCycleSnapshot>();
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (!_simulation.Units.Alive[unit] ||
                !_simulation.Economy.IsWorkerOwnedBy(unit, playerId))
                continue;
            var snapshot = _simulation.Economy.Worker(unit);
            workers.Add(new TestWorkerCycleSnapshot(
                new TestUnitId(unit),
                (TestWorkerEconomyState)snapshot.State,
                new TestResourceNodeId(snapshot.TargetNode.Value),
                (TestEconomyResourceKind)snapshot.CargoKind,
                snapshot.CargoAmount,
                _simulation.Units.MoveGoals[unit]));
        }
        return workers.ToArray();
    }

    public TestDropOffApproachSnapshot PreviewDropOffApproach(
        int playerId,
        TestEconomyResourceKind kind,
        TestUnitId worker)
    {
        var unit = Observe(worker);
        var snapshot = _simulation.Economy.PreviewDropOffApproach(
            playerId,
            (EconomyResourceKind)kind,
            unit.Position,
            unit.Radius);
        return new TestDropOffApproachSnapshot(
            snapshot.Found,
            snapshot.Center,
            snapshot.HalfExtents,
            snapshot.InteractionHalfExtents,
            snapshot.Target,
            snapshot.DistanceSquared);
    }

    public TestPlayerViewSnapshot ObservePlayerView(int playerId)
    {
        var view = _simulation.CreatePlayerView(playerId);
        return new TestPlayerViewSnapshot(
            playerId,
            view.Units.Select(value => new TestUnitId(value.UnitId)).ToArray(),
            view.Buildings.Select(value =>
                new TestGameplayBuildingId(value.BuildingId.Value)).ToArray(),
            view.Resources.Select(value => new TestPlayerResourceView(
                new TestResourceNodeId(value.NodeId.Value),
                (TestMapVisibility)value.Visibility,
                value.KnownRemaining)).ToArray(),
            view.VisibilityCells.Count(value =>
                value == (byte)MapVisibility.Hidden),
            view.VisibilityCells.Count(value =>
                value == (byte)MapVisibility.Explored),
            view.VisibilityCells.Count(value =>
                value == (byte)MapVisibility.Visible));
    }

    public void StartMatch(params int[] playerIds) =>
        _simulation.StartMatch(playerIds);

    public TestMatchSnapshot ObserveMatch()
    {
        var snapshot = _simulation.Match.CreateSnapshot(
            _simulation.Construction,
            _simulation.Economy,
            _simulation.Units,
            _simulation.Combat);
        return new TestMatchSnapshot(
            (TestMatchPhase)snapshot.Phase,
            snapshot.CompletedTick,
            snapshot.WinnerPlayerId,
            snapshot.Players.Select(value =>
                new TestPlayerCapabilitySnapshot(
                    value.PlayerId,
                    (TestMatchPlayerStatus)value.Status,
                    value.EstablishedPresence,
                    value.ActiveBuildings,
                    value.TownHalls,
                    value.ProductionFacilities,
                    value.Workers,
                    value.CombatUnits,
                    value.HasAnyProduction,
                    value.IsEliminationRisk)).ToArray());
    }

    public TestPlayerOrderCommandCode PlayerMove(
        int playerId,
        IReadOnlyList<TestUnitId> units,
        Vector2 target,
        bool queued = false) =>
        (TestPlayerOrderCommandCode)_simulation.IssuePlayerMove(
            playerId,
            units.Select(value => value.Value).ToArray(),
            target,
            queued).Code;

    public TestPlayerOrderCommandCode PlayerAttackUnit(
        int playerId,
        IReadOnlyList<TestUnitId> units,
        TestUnitId target,
        bool queued = false)
    {
        var targetPosition = (uint)target.Value < (uint)_simulation.Units.Count
            ? _simulation.Units.Positions[target.Value]
            : Vector2.Zero;
        return (TestPlayerOrderCommandCode)_simulation.IssuePlayerSmartCommand(
            playerId,
            units.Select(value => value.Value).ToArray(),
            new SmartCommandTarget(
                SmartCommandTargetKind.EnemyUnit,
                targetPosition,
                target.Value),
            attackMoveModifier: false,
            queued).Code;
    }

    public TestPlayerOrderCommandCode PlayerSmartResource(
        int playerId,
        IReadOnlyList<TestUnitId> units,
        TestResourceNodeId resource,
        bool queued = false)
    {
        var snapshot = ObserveResourceNode(resource);
        return (TestPlayerOrderCommandCode)_simulation.IssuePlayerSmartCommand(
            playerId,
            units.Select(value => value.Value).ToArray(),
            new SmartCommandTarget(
                SmartCommandTargetKind.ResourceNode,
                snapshot.Position,
                ResourceNode: resource.Value),
            attackMoveModifier: false,
            queued).Code;
    }

    public TestPlayerOrderCommandCode PlayerSmartFriendlyBuilding(
        int playerId,
        IReadOnlyList<TestUnitId> units,
        TestGameplayBuildingId building,
        bool queued = false)
    {
        var snapshot = _simulation.ObserveGameplayBuilding(
            new GameplayBuildingId(building.Value));
        return (TestPlayerOrderCommandCode)_simulation.IssuePlayerSmartCommand(
            playerId,
            units.Select(value => value.Value).ToArray(),
            new SmartCommandTarget(
                SmartCommandTargetKind.FriendlyBuilding,
                (snapshot.Bounds.Min + snapshot.Bounds.Max) * 0.5f,
                Building: building.Value),
            attackMoveModifier: false,
            queued).Code;
    }

    public TestEconomyBaseSnapshot[] ObserveEconomyBases(int playerId) =>
        _simulation.Economy.CreateBaseOverview(playerId, _simulation.Units.Count)
            .Select(value => new TestEconomyBaseSnapshot(
                new TestEconomyBaseId(value.Id.Value),
                new TestGameplayBuildingId(value.TownHall.Value),
                value.Position,
                value.Operational,
                value.MineralNodes,
                value.VespeneNodes,
                value.AssignedMineralWorkers,
                value.IdealMineralWorkers,
                value.AssignedVespeneWorkers,
                value.IdealVespeneWorkers))
            .ToArray();

    public TestWorkerTransferResult TransferWorkers(
        int playerId,
        TestEconomyBaseId source,
        TestEconomyBaseId target,
        int count)
    {
        var result = _simulation.IssueWorkerTransfer(
            playerId,
            new EconomyBaseId(source.Value),
            new EconomyBaseId(target.Value),
            count);
        return new TestWorkerTransferResult(
            (TestWorkerTransferCommandCode)result.Code,
            result.RequestedWorkers,
            result.TransferredWorkers);
    }

    public TestConstructionResult Build(
        int playerId,
        TestUnitId worker,
        BuildingTypeProfile profile,
        Vector2 center,
        TestResourceNodeId? refineryNode = null,
        bool queued = false)
    {
        var result = _simulation.IssueConstruction(
            playerId,
            worker.Value,
            profile,
            center,
            refineryNode.HasValue
                ? new EconomyResourceNodeId(refineryNode.Value.Value)
                : new EconomyResourceNodeId(-1),
            queued);
        return new TestConstructionResult(
            (TestConstructionCommandCode)result.Code,
            new TestGameplayBuildingId(result.BuildingId.Value),
            (TestBuildingPlacementCode)result.PlacementCode);
    }

    public bool CancelConstruction(
        int playerId,
        TestGameplayBuildingId building) =>
        _simulation.CancelConstruction(
            playerId, new GameplayBuildingId(building.Value));

    public bool ResumeConstruction(
        int playerId,
        TestGameplayBuildingId building,
        TestUnitId worker) =>
        _simulation.ResumeConstruction(
            playerId,
            new GameplayBuildingId(building.Value),
            worker.Value);

    public bool DamageBuilding(TestGameplayBuildingId building, float damage) =>
        _simulation.DamageBuilding(
            new GameplayBuildingId(building.Value), damage);

    public bool DamageUnit(TestUnitId unit, float damage) =>
        _simulation.DamageUnit(unit.Value, damage);

    public TestGameplayBuildingSnapshot ObserveGameplayBuilding(
        TestGameplayBuildingId building)
    {
        var value = _simulation.ObserveGameplayBuilding(
            new GameplayBuildingId(building.Value));
        return ToTestGameplayBuildingSnapshot(value);
    }

    public TestGameplayBuildingSnapshot[] ObserveGameplayBuildings() =>
        _simulation.CreateGameplayBuildingOverview()
            .Select(ToTestGameplayBuildingSnapshot)
            .ToArray();

    private static TestGameplayBuildingSnapshot ToTestGameplayBuildingSnapshot(
        GameplayBuildingSnapshot value)
    {
        return new TestGameplayBuildingSnapshot(
            new TestGameplayBuildingId(value.Id.Value),
            value.Type.Id,
            value.Type.Name,
            value.Type.Size,
            value.ReservationId.Value,
            new TestBuildingId(value.FootprintId.Value),
            (TestBuildingLifecycleState)value.State,
            (value.Bounds.Min + value.Bounds.Max) * 0.5f,
            value.AccessPoint,
            value.Progress,
            value.Health,
            value.MaximumHealth,
            value.EffectiveArmor,
            value.Type.Attributes,
            value.Type.ArmorUpgradePerLevel,
            new TestResourceNodeId(value.RefineryNode.Value),
            new TestUnitId(value.BuilderUnit));
    }

    public int CountPlayerBuildings(
        int playerId,
        int typeId,
        bool completedOnly = true) =>
        _simulation.CreateGameplayBuildingOverview().Count(value =>
            value.PlayerId == playerId && value.Type.Id == typeId &&
            !value.IsTerminal &&
            (!completedOnly || value.State == BuildingLifecycleState.Completed));

    public TestProductionResult Train(
        int playerId,
        TestGameplayBuildingId producer,
        ProductionRecipeProfile recipe)
    {
        var result = _simulation.IssueProduction(
            playerId, new GameplayBuildingId(producer.Value), recipe);
        return new TestProductionResult(
            (TestProductionCommandCode)result.Code,
            new TestProductionOrderId(result.OrderId.Value));
    }

    public TestProductionBatchResult TrainBatch(
        int playerId,
        IReadOnlyList<TestGameplayBuildingId> producers,
        ProductionRecipeProfile recipe)
    {
        var group = BuildProductionGroup(producers);
        var availability = group.Producers.Select(producer =>
        {
            var value = _simulation.Production.ObserveAvailability(
                playerId, new GameplayBuildingId(producer.BuildingId), recipe,
                _simulation.Construction, _simulation.Economy.Players);
            return new ProductionBatchAvailability(
                producer.BuildingId, value.Available, value.Code.ToString());
        });
        var plan = ProductionBatchPlanner.PlanTrain(group, availability);
        var results = plan.ProducerIds.Select(value => _simulation.IssueProduction(
                playerId, new GameplayBuildingId(value), recipe))
            .ToArray();
        return new TestProductionBatchResult(
            group.ProducerCount,
            plan.ProducerIds.Length,
            results.Count(value => value.Succeeded),
            results.Select(value => (TestProductionCommandCode)value.Code).ToArray());
    }

    public int CancelNewestProductionBatch(
        int playerId,
        IReadOnlyList<TestGameplayBuildingId> producers,
        int recipeId)
    {
        var group = BuildProductionGroup(producers);
        return group.NewestMatchingOrders(recipeId).Count(order =>
            _simulation.CancelProduction(playerId, new ProductionOrderId(order)));
    }

    public TestProductionGroupSnapshot ObserveProductionGroup(
        IReadOnlyList<TestGameplayBuildingId> producers)
    {
        var group = BuildProductionGroup(producers);
        return new TestProductionGroupSnapshot(
            group.ProducerCount,
            group.TotalOrders,
            group.ActiveProducerCount,
            group.Producers.Select(value => value.Orders.Length).ToArray());
    }

    private ProductionGroupSnapshot BuildProductionGroup(
        IReadOnlyList<TestGameplayBuildingId> producers) =>
        ProductionGroupSnapshot.Create(producers.Select(value =>
        {
            var queue = _simulation.Production.Observe(
                new GameplayBuildingId(value.Value));
            return new ProductionGroupProducerSnapshot(
                value.Value,
                queue.Orders.Select(order => new ProductionGroupOrderEntry(
                    order.Id.Value, order.Recipe.Id, order.Progress)).ToArray());
        }));

    public TestProductionAvailabilitySnapshot ObserveProductionAvailability(
        int playerId,
        TestGameplayBuildingId producer,
        ProductionRecipeProfile recipe)
    {
        var snapshot = _simulation.Production.ObserveAvailability(
            playerId,
            new GameplayBuildingId(producer.Value),
            recipe,
            _simulation.Construction,
            _simulation.Economy.Players);
        return new TestProductionAvailabilitySnapshot(
            (TestProductionCommandCode)snapshot.Code,
            snapshot.Requirements.Select(value =>
                new TestProductionRequirementStatus(
                    value.Requirement.TypeId,
                    value.Requirement.Count,
                    value.CurrentCount)).ToArray());
    }

    public TestResearchResult Research(
        int playerId,
        TestGameplayBuildingId researcher,
        TechnologyProfile technology)
    {
        var result = _simulation.IssueResearch(
            playerId, new GameplayBuildingId(researcher.Value), technology);
        return new TestResearchResult(
            (TestResearchCommandCode)result.Code,
            new TestResearchOrderId(result.OrderId.Value));
    }

    public bool CancelResearch(
        int playerId,
        TestResearchOrderId order) =>
        _simulation.CancelResearch(playerId, new ResearchOrderId(order.Value));

    public int TechnologyLevel(int playerId, int technologyId) =>
        _simulation.Technology.Level(playerId, technologyId);

    public int ResearchQueueCount(TestGameplayBuildingId researcher) =>
        _simulation.Technology.Observe(
            new GameplayBuildingId(researcher.Value)).Orders.Length;

    public TestResearchAvailabilitySnapshot ObserveResearchAvailability(
        int playerId,
        TestGameplayBuildingId researcher,
        TechnologyProfile technology)
    {
        var value = _simulation.Technology.ObserveAvailability(
            playerId,
            new GameplayBuildingId(researcher.Value),
            technology,
            _simulation.Construction,
            _simulation.Economy.Players);
        return new TestResearchAvailabilitySnapshot(
            (TestResearchCommandCode)value.Code,
            value.CurrentLevel,
            value.Requirements.Select(item => item.CurrentValue).ToArray(),
            value.Requirements.Select(item => item.Requirement.Value).ToArray());
    }

    public bool CancelProduction(
        int playerId,
        TestProductionOrderId order) =>
        _simulation.CancelProduction(
            playerId, new ProductionOrderId(order.Value));

    public bool SetRallyPoint(
        int playerId,
        TestGameplayBuildingId producer,
        Vector2 point) =>
        _simulation.SetProductionRallyPoint(
            playerId, new GameplayBuildingId(producer.Value), point);

    public bool SetRallyResource(
        int playerId,
        TestGameplayBuildingId producer,
        TestResourceNodeId node)
    {
        var backend = new EconomyResourceNodeId(node.Value);
        return _simulation.SetProductionRallyTarget(
            playerId,
            new GameplayBuildingId(producer.Value),
            RallyTarget.Resource(
                backend, _simulation.Economy.ResourceNodePosition(backend)));
    }

    public bool SetRallyFriendlyUnit(
        int playerId,
        TestGameplayBuildingId producer,
        TestUnitId unit)
    {
        var position = _simulation.Units.Positions[unit.Value];
        return _simulation.SetProductionRallyTarget(
            playerId,
            new GameplayBuildingId(producer.Value),
            RallyTarget.Friendly(unit.Value, position));
    }

    public TestRallyTarget ObserveProductionRally(
        TestGameplayBuildingId producer)
    {
        var rally = _simulation.Production.Observe(
            new GameplayBuildingId(producer.Value)).Rally;
        return new TestRallyTarget(
            (TestRallyTargetKind)rally.Kind,
            rally.Position,
            new TestResourceNodeId(rally.ResourceNode.Value),
            new TestUnitId(rally.Unit));
    }

    public TestProductionQueueSnapshot ObserveProduction(
        TestGameplayBuildingId producer)
    {
        var queue = _simulation.Production.Observe(
            new GameplayBuildingId(producer.Value));
        var active = queue.Orders.FirstOrDefault();
        return new TestProductionQueueSnapshot(
            queue.Orders.Length,
            queue.Orders.Length == 0
                ? null
                : (TestProductionOrderState)active.State,
            queue.Orders.Length == 0 ? 0f : active.Progress,
            queue.Rally.IsSet ? queue.Rally.Position : null);
    }


    public TestUnitId SpawnCombat(
        Vector2 position,
        int team,
        TestCombatProfile? profile = null,
        float radius = 7.5f,
        float maximumSpeed = 128f,
        float acceleration = 720f)
    {
        var resolvedProfile = profile ?? TestCombatProfile.Standard;
        var backendProfile = ToBackendCombat(resolvedProfile);
        return new TestUnitId(_simulation.AddUnit(
            position, team, backendProfile, radius, maximumSpeed, acceleration));
    }

    private static CombatProfileSnapshot ToBackendCombat(
        TestCombatProfile profile) => new(
        profile.MaximumHealth,
        profile.AttackDamage,
        profile.AttackRange,
        profile.AcquisitionRange,
        profile.AttackCooldownSeconds,
        profile.AttackWindupSeconds,
        profile.LeashDistance,
        (CombatPositioningKind)profile.Positioning,
        profile.Armor,
        profile.Attributes,
        profile.AttacksPerVolley,
        profile.BonusVs,
        profile.BonusDamage,
        profile.BaseUpgradeDamage,
        profile.BonusUpgradeDamage,
        profile.ProjectileSpeed,
        profile.CanMoveDuringWindup,
        profile.CanMoveDuringCooldown,
        profile.AutoTargetPriority);

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

    public void Move(
        IReadOnlyList<TestUnitId> units,
        Vector2 target,
        bool queued = false) =>
        _simulation.IssueMove(ToBackendIndices(units), target, queued);

    public void AttackMove(
        IReadOnlyList<TestUnitId> units,
        Vector2 target,
        bool queued = false) =>
        _simulation.IssueAttackMove(ToBackendIndices(units), target, queued);

    public void AttackTarget(
        IReadOnlyList<TestUnitId> units,
        TestUnitId target,
        bool queued = false) =>
        _simulation.IssueAttackTarget(
            ToBackendIndices(units), target.Value, queued);

    public void AttackBuilding(
        IReadOnlyList<TestUnitId> units,
        TestGameplayBuildingId target,
        bool queued = false) =>
        _simulation.IssueAttackBuilding(
            ToBackendIndices(units),
            new GameplayBuildingId(target.Value),
            queued);

    public void SmartCommandBuilding(
        IReadOnlyList<TestUnitId> units,
        TestGameplayBuildingId target,
        bool queued = false)
    {
        var building = _simulation.ObserveGameplayBuilding(
            new GameplayBuildingId(target.Value));
        _simulation.IssueSmartCommand(
            ToBackendIndices(units),
            new SmartCommandTarget(
                SmartCommandTargetKind.EnemyBuilding,
                (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                Building: target.Value),
            attackMoveModifier: false,
            queued);
    }

    public void SmartCommandGround(
        IReadOnlyList<TestUnitId> units,
        Vector2 target,
        bool attackMoveModifier = false,
        bool queued = false) =>
        _simulation.IssueSmartCommand(
            ToBackendIndices(units),
            new SmartCommandTarget(SmartCommandTargetKind.Ground, target),
            attackMoveModifier,
            queued);

    public void SmartCommandUnit(
        IReadOnlyList<TestUnitId> units,
        TestUnitId target,
        bool queued = false)
    {
        if (units.Count == 0)
        {
            return;
        }
        var selected = ToBackendIndices(units);
        var targetIndex = target.Value;
        var kind = _simulation.Combat.Teams[selected[0]] ==
                   _simulation.Combat.Teams[targetIndex]
            ? SmartCommandTargetKind.FriendlyUnit
            : SmartCommandTargetKind.EnemyUnit;
        _simulation.IssueSmartCommand(
            selected,
            new SmartCommandTarget(
                kind,
                _simulation.Units.Positions[targetIndex],
                targetIndex),
            attackMoveModifier: false,
            queued);
    }

    public void Stop(IReadOnlyList<TestUnitId> units) =>
        _simulation.Stop(ToBackendIndices(units));

    public void Hold(IReadOnlyList<TestUnitId> units) =>
        _simulation.Hold(ToBackendIndices(units));

    public void AssignControlGroup(int group, IReadOnlyList<TestUnitId> units) =>
        _controlGroups.Assign(group, ToBackendIndices(units));

    public void AddToControlGroup(int group, IReadOnlyList<TestUnitId> units) =>
        _controlGroups.Add(group, ToBackendIndices(units));

    public TestUnitId[] RecallControlGroup(int group)
    {
        var backend = new int[_simulation.Units.Count];
        var count = _controlGroups.Recall(
            group, _simulation.Units.Alive, backend);
        var result = new TestUnitId[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = new TestUnitId(backend[index]);
        }
        return result;
    }

    public void AssignMixedControlGroup(
        int group,
        IReadOnlyList<TestUnitId> units,
        IReadOnlyList<TestGameplayBuildingId> buildings) =>
        _controlGroups.Assign(group, ToControlGroupEntities(units, buildings));

    public void AddMixedControlGroup(
        int group,
        IReadOnlyList<TestUnitId> units,
        IReadOnlyList<TestGameplayBuildingId> buildings) =>
        _controlGroups.Add(group, ToControlGroupEntities(units, buildings));

    public void StealAssignControlGroup(
        int group,
        IReadOnlyList<TestUnitId> units,
        IReadOnlyList<TestGameplayBuildingId> buildings) =>
        _controlGroups.StealAssign(group, ToControlGroupEntities(units, buildings));

    public void StealAddControlGroup(
        int group,
        IReadOnlyList<TestUnitId> units,
        IReadOnlyList<TestGameplayBuildingId> buildings) =>
        _controlGroups.StealAdd(group, ToControlGroupEntities(units, buildings));

    public TestControlGroupSelection RecallMixedControlGroup(int group)
    {
        var recalled = _controlGroups.Recall(group, entity => entity.Kind switch
        {
            ControlGroupEntityKind.Unit =>
                (uint)entity.EntityId < (uint)_simulation.Units.Count &&
                _simulation.Units.Alive[entity.EntityId],
            ControlGroupEntityKind.Building =>
                _simulation.Construction.IsAlive(
                    new GameplayBuildingId(entity.EntityId)),
            _ => false
        });
        return new TestControlGroupSelection(
            recalled.Where(value => value.Kind == ControlGroupEntityKind.Unit)
                .Select(value => new TestUnitId(value.EntityId)).ToArray(),
            recalled.Where(value => value.Kind == ControlGroupEntityKind.Building)
                .Select(value => new TestGameplayBuildingId(value.EntityId)).ToArray());
    }

    private static ControlGroupEntity[] ToControlGroupEntities(
        IReadOnlyList<TestUnitId> units,
        IReadOnlyList<TestGameplayBuildingId> buildings) =>
        units.Select(value => new ControlGroupEntity(
                ControlGroupEntityKind.Unit, value.Value))
            .Concat(buildings.Select(value => new ControlGroupEntity(
                ControlGroupEntityKind.Building, value.Value)))
            .ToArray();

    public TestTargetCommandRequest BeginTargetCommand(
        int playerId,
        TestTargetCommandKind kind,
        IReadOnlyList<TestUnitId> units,
        IReadOnlyList<TestGameplayBuildingId> buildings,
        string label,
        int dataId = -1)
    {
        var request = TargetCommandRequest.Create(
            (TargetCommandKind)kind,
            units.Select(value => value.Value),
            buildings.Select(value => value.Value),
            label,
            dataId);
        return new TestTargetCommandRequest(
            playerId, kind,
            request.UnitIds.Select(value => new TestUnitId(value)).ToArray(),
            request.BuildingIds.Select(value =>
                new TestGameplayBuildingId(value)).ToArray(),
            request.Label,
            request.DataId);
    }

    public TestTargetCommandResult ResolveTargetCommand(
        TestTargetCommandRequest request,
        Vector2 position,
        bool shiftPressed,
        bool cancel = false)
    {
        var internalRequest = TargetCommandRequest.Create(
            (TargetCommandKind)request.Kind,
            request.Units.Select(value => value.Value),
            request.Buildings.Select(value => value.Value),
            request.Label,
            request.DataId);
        var resolution = TargetCommandResolver.Resolve(
            internalRequest,
            cancel ? TargetCommandPointerButton.Secondary :
                TargetCommandPointerButton.Primary,
            position,
            shiftPressed);
        if (resolution.Kind == TargetCommandResolutionKind.Cancel)
            return new TestTargetCommandResult(
                false, true, false, false);

        if (request.Kind == TestTargetCommandKind.Build)
        {
            var preview = PreviewBuildTarget(
                request, resolution.Position, resolution.Queued);
            if (!preview.CanPlace)
                return new TestTargetCommandResult(
                    false, false, false, true,
                    preview.Code == TestConstructionCommandCode.PlacementRejected
                        ? preview.PlacementCode.ToString()
                        : preview.Code.ToString());
            var result = _simulation.IssueConstruction(
                request.PlayerId,
                preview.Builder.Value,
                DemoBuildingTypes.CreateCatalog().Type(request.DataId),
                preview.Center,
                new EconomyResourceNodeId(-1),
                resolution.Queued);
            return new TestTargetCommandResult(
                result.Succeeded, false, resolution.Queued,
                result.Succeeded ? resolution.KeepTargeting : true,
                result.Succeeded ? "Success" : result.Code.ToString(),
                new TestGameplayBuildingId(result.BuildingId.Value));
        }

        var issued = request.Kind switch
        {
            TestTargetCommandKind.Move => _simulation.IssuePlayerMove(
                request.PlayerId,
                request.Units.Select(value => value.Value).ToArray(),
                resolution.Position,
                resolution.Queued).Succeeded,
            TestTargetCommandKind.AttackMove =>
                _simulation.IssuePlayerAttackMove(
                    request.PlayerId,
                    request.Units.Select(value => value.Value).ToArray(),
                    resolution.Position,
                    resolution.Queued).Succeeded,
            TestTargetCommandKind.Rally => SetTargetCommandRally(
                request.PlayerId, request.Buildings, resolution.Position),
            _ => false
        };
        return new TestTargetCommandResult(
            issued, false, resolution.Queued,
            issued ? resolution.KeepTargeting : true);
    }

    public TestBuildTargetPreview PreviewBuildTarget(
        TestTargetCommandRequest request,
        Vector2 pointer,
        bool queued = false)
    {
        if (request.Kind != TestTargetCommandKind.Build)
            throw new ArgumentException("Request is not a Build target.", nameof(request));
        var profile = DemoBuildingTypes.CreateCatalog().Type(request.DataId);
        var center = BuildTargetSnapper.Snap(pointer);
        var candidates = request.Units
            .OrderBy(value => Vector2.DistanceSquared(
                _simulation.Units.Positions[value.Value], center))
            .ThenBy(value => value.Value)
            .ToArray();
        ConstructionCommandResult? first = null;
        var builder = candidates.Length > 0 ? candidates[0] : new TestUnitId(-1);
        foreach (var candidate in candidates)
        {
            var result = _simulation.PreviewConstruction(
                request.PlayerId, candidate.Value, profile, center,
                new EconomyResourceNodeId(-1), queued);
            first ??= result;
            if (!result.Succeeded) continue;
            first = result;
            builder = candidate;
            break;
        }
        var preview = first ?? new ConstructionCommandResult(
            ConstructionCommandCode.InvalidBuilder, default);
        return new TestBuildTargetPreview(
            center, profile.Size, builder, preview.Succeeded,
            (TestConstructionCommandCode)preview.Code,
            (TestBuildingPlacementCode)preview.PlacementCode);
    }

    private bool SetTargetCommandRally(
        int playerId,
        IReadOnlyList<TestGameplayBuildingId> buildings,
        Vector2 position)
    {
        foreach (var value in buildings)
        {
            var id = new GameplayBuildingId(value.Value);
            if (!_simulation.Construction.IsAlive(id) ||
                _simulation.ObserveGameplayBuilding(id).PlayerId != playerId)
                return false;
        }
        foreach (var value in buildings)
        {
            if (!_simulation.SetProductionRallyPoint(
                    playerId, new GameplayBuildingId(value.Value), position))
                return false;
        }
        return true;
    }

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

    public TestBuildingPlacementResult TryPlaceBuilding(
        Vector2 center,
        TestBuildingFootprintClass footprintClass,
        TestMovementClass minimumPassageClass)
    {
        var backendFootprintClass = (BuildingFootprintClass)footprintClass;
        var profile = BuildingFootprintProfile.For(backendFootprintClass);
        var halfSize = profile.Size * 0.5f;
        var result = _simulation.TryPlaceBuilding(
            new SimRect(center - halfSize, center + halfSize),
            new BuildingPlacementRules((MovementClass)minimumPassageClass));
        return new TestBuildingPlacementResult(
            (TestBuildingPlacementCode)result.Code,
            new TestBuildingId(result.FootprintId.Value),
            profile.Size);
    }

    public TestBuildingPlacementAssessment AssessBuildingPlacement(
        Vector2 center,
        Vector2 size,
        TestMovementClass minimumPassageClass)
    {
        var halfSize = size * 0.5f;
        var value = _simulation.AssessBuildingPlacement(
            new SimRect(center - halfSize, center + halfSize),
            new BuildingPlacementRules(
                (MovementClass)minimumPassageClass));
        return ToTestBuildingPlacementAssessment(value);
    }

    public TestBuildingPlacementAssessment AssessBuildingPlacement(
        Vector2 center,
        TestBuildingFootprintClass footprintClass,
        TestMovementClass minimumPassageClass) =>
        AssessBuildingPlacement(
            center,
            BuildingFootprintProfile.For(
                (BuildingFootprintClass)footprintClass).Size,
            minimumPassageClass);

    public TestHardFootprintCommitResult TryCommitHardFootprint(
        Vector2 center,
        Vector2 size,
        TestMovementClass minimumPassageClass)
    {
        var halfSize = size * 0.5f;
        var value = _simulation.TryCommitHardFootprint(
            new SimRect(center - halfSize, center + halfSize),
            new BuildingPlacementRules(
                (MovementClass)minimumPassageClass));
        return new TestHardFootprintCommitResult(
            (TestHardFootprintCommitCode)value.Code,
            new TestBuildingId(value.FootprintId.Value),
            size,
            ToTestBuildingPlacementAssessment(value.Assessment));
    }

    public TestHardFootprintCommitResult TryCommitHardFootprint(
        Vector2 center,
        TestBuildingFootprintClass footprintClass,
        TestMovementClass minimumPassageClass)
    {
        var size = BuildingFootprintProfile.For(
            (BuildingFootprintClass)footprintClass).Size;
        return TryCommitHardFootprint(
            center, size, minimumPassageClass);
    }

    public TestBuildingPlacementResult TryPlaceBuilding(
        Vector2 center,
        BuildingFootprintProfileSnapshot profile)
    {
        var result = _simulation.TryPlaceBuilding(center, profile);
        return new TestBuildingPlacementResult(
            (TestBuildingPlacementCode)result.Code,
            new TestBuildingId(result.FootprintId.Value),
            profile.Size);
    }

    public bool RemoveBuilding(TestBuildingId id) =>
        _simulation.RemoveBuilding(new DynamicFootprintId(id.Value));

    private static TestBuildingPlacementAssessment
        ToTestBuildingPlacementAssessment(BuildingPlacementAssessment value) =>
        new(
            (TestStaticPlacementCode)value.Static.Code,
            (TestDynamicStartValidationCode)value.Dynamic.Code,
            (TestBuildingPlacementCode)value.Code,
            value.ConflictId);

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
            metrics.EconomyMilliseconds,
            metrics.CombatMilliseconds,
            metrics.PathMilliseconds,
            metrics.PreferredVelocityMilliseconds,
            metrics.ChokeMilliseconds,
            metrics.SpatialHashMilliseconds,
            metrics.SteeringMilliseconds,
            metrics.IntegrateMilliseconds,
            metrics.CollisionMilliseconds,
            metrics.RecoveryMilliseconds,
            metrics.CommandMilliseconds,
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

    public TestOperationInteractionSnapshot VerifyOperationInteractions()
    {
        SelectionCandidate[] candidates =
        [
            new(0, 10, 1, true, new Vector2(120f, 120f), 8f),
            new(1, 10, 1, true, new Vector2(260f, 180f), 8f),
            new(2, 20, 1, true, new Vector2(360f, 260f), 10f),
            new(3, 10, 1, true, new Vector2(900f, 500f), 8f),
            new(4, 10, 2, true, new Vector2(180f, 140f), 8f),
            new(5, 10, 1, false, new Vector2(220f, 160f), 8f)
        ];
        var visible = new SimRect(Vector2.Zero, new Vector2(600f, 400f));
        var point = SelectionFilter.SelectPoint(
            candidates, new Vector2(122f, 121f), playerTeam: 1);
        var sameType = SelectionFilter.SelectVisibleSameType(
            candidates, point, visible, playerTeam: 1);
        var box = SelectionFilter.SelectBox(
            candidates, visible, playerTeam: 1);

        var camera = new OperationCameraController(
            new SimRect(Vector2.Zero, new Vector2(1200f, 700f)),
            new Vector2(400f, 300f));
        var pointer = new Vector2(100f, 100f);
        var beforeZoom = camera.ScreenToWorld(pointer);
        camera.ZoomAt(pointer, 2);
        var zoomStable = Vector2.Distance(
            beforeZoom, camera.ScreenToWorld(pointer)) < 0.01f;
        var beforePan = camera.Position;
        camera.PanFromEdges(new Vector2(399f, 150f), 0.25f);
        var edgeMoved = camera.Position.X > beforePan.X;
        camera.Focus([new Vector2(900f, 500f), new Vector2(1000f, 600f)]);

        var tracker = new ControlGroupRecallTracker();
        var first = tracker.Register(1, 10.0);
        var second = tracker.Register(1, 10.2);
        var consumed = tracker.Register(1, 10.3);
        return new TestOperationInteractionSnapshot(
            point,
            sameType.Length,
            box.Length,
            zoomStable,
            edgeMoved,
            !first && second && !consumed,
            camera.Position);
    }

    public TestMinimapInteractionSnapshot VerifyMinimapInteractions()
    {
        var world = new SimRect(
            new Vector2(24f, 70f), new Vector2(1256f, 696f));
        var panel = new MinimapPanelRect(Vector2.Zero, new Vector2(230f, 140f));
        var transform = new MinimapTransform(world, panel);
        var sample = new Vector2(900f, 500f);
        var panelPoint = transform.WorldToPanel(sample);
        var roundTrip = Vector2.Distance(
            sample, transform.PanelToWorld(panelPoint)) < 0.01f;
        var viewport = transform.WorldRectToPanel(new SimRect(
            new Vector2(400f, 250f), new Vector2(800f, 550f)));
        var viewportMapped = viewport.Size.X > 0f && viewport.Size.Y > 0f &&
                             panel.Contains(viewport.Position) &&
                             panel.Contains(viewport.Position + viewport.Size);
        var focus = MinimapInteractionResolver.Resolve(
            transform, panelPoint, primaryButton: true, secondaryButton: false);
        var command = MinimapInteractionResolver.Resolve(
            transform, panelPoint, primaryButton: false, secondaryButton: true);
        var outside = MinimapInteractionResolver.Resolve(
            transform, new Vector2(-2f, 40f), primaryButton: true, secondaryButton: false);
        return new TestMinimapInteractionSnapshot(
            roundTrip,
            viewportMapped,
            focus.Kind == MinimapInteractionKind.FocusCamera,
            command.Kind == MinimapInteractionKind.SmartCommand,
            outside.Kind == MinimapInteractionKind.None,
            command.WorldPosition);
    }

    public TestClearanceBakeCommitSnapshot CommitClearanceBakeVariant(
        int chunkSizeCells = 8)
    {
        if (_navigationMap is null || _clearanceBake is null)
        {
            return new TestClearanceBakeCommitSnapshot(
                ClearanceBakeCommitCode.MissingBaseline, 0UL, 0UL, 0, 0);
        }
        var candidate = ClearanceBakeSnapshot.Build(
            _navigationMap,
            _clearanceBake.CellSize,
            chunkSizeCells);
        var result = _simulation.TryCommitClearanceBake(candidate);
        return new TestClearanceBakeCommitSnapshot(
            result.Code,
            result.PreviousBakeHash,
            result.CandidateBakeHash,
            result.ReplannedUnits,
            _simulation.Metrics.ClearanceBakeReloads);
    }

    public TestClearanceBakeCommitSnapshot CommitMismatchedClearanceBake()
    {
        if (_navigationMap is null)
        {
            return new TestClearanceBakeCommitSnapshot(
                ClearanceBakeCommitCode.MissingBaseline, 0UL, 0UL, 0, 0);
        }
        var candidate = ClearanceBakeSnapshot.Build(
            DemoResourceVariantFactory.CreateNavigationVariant(_navigationMap));
        var result = _simulation.TryCommitClearanceBake(candidate);
        return new TestClearanceBakeCommitSnapshot(
            result.Code,
            result.PreviousBakeHash,
            result.CandidateBakeHash,
            result.ReplannedUnits,
            _simulation.Metrics.ClearanceBakeReloads);
    }

    public void Step()
    {
        StepAi();
        _simulation.Tick(1f / 60f);
    }

    partial void StepAi();

    public void StartCommandRecording() =>
        _simulation.StartCommandRecording();

    public void StartReplayPackageRecording()
    {
        if (_navigationMap is null || _gameplayProfiles is null)
        {
            throw new InvalidOperationException(
                "Replay package recording requires versioned navigation and gameplay assets.");
        }
        _simulation.StartReplayPackageRecording(ReplayResourceManifest.Create(
            _navigationMap, _gameplayProfiles, _clearanceBake));
    }

    public TestCommandLog CaptureCommandLog() =>
        new(_simulation.CaptureCommandLog());

    public TestEconomyCommandLog CaptureEconomyCommandLog() =>
        new(_simulation.CaptureEconomyCommandLog());

    public TestConstructionCommandLog CaptureConstructionCommandLog() =>
        new(_simulation.CaptureConstructionCommandLog());

    public TestProductionCommandLog CaptureProductionCommandLog() =>
        new(_simulation.CaptureProductionCommandLog());

    public TestReplayPackage CaptureReplayPackage() =>
        new(_simulation.CaptureReplayPackage());

    public TestReplayTrace ReplayPackage(
        TestReplayPackage package,
        long targetTick,
        int hashIntervalTicks = 30)
    {
        if (_navigationMap is null || _gameplayProfiles is null ||
            targetTick < 0 || hashIntervalTicks <= 0)
        {
            throw new InvalidOperationException(
                "Replay package assets or arguments are invalid.");
        }
        if (!SimulationReplayPackageFactory.TryCreateSimulation(
                package.Backend,
                _navigationMap,
                _gameplayProfiles,
                _clearanceBake,
                out var simulation,
                out var validation) || simulation is null)
        {
            throw new InvalidOperationException(
                $"Replay package creation failed: {validation.Code}.");
        }

        var replay = new SimulationReplayPackageRunner(package.Backend);
        var trace = new SimulationReplayTrace();
        trace.Add(0, simulation.ComputeStateHash());
        while (simulation.Metrics.Tick < targetTick)
        {
            replay.ApplyForCurrentTick(simulation);
            simulation.Tick(1f / 60f);
            if (simulation.Metrics.Tick % hashIntervalTicks == 0 ||
                simulation.Metrics.Tick == targetTick)
            {
                trace.Add(simulation.Metrics.Tick, simulation.ComputeStateHash());
            }
        }
        if (!replay.Completed)
        {
            throw new InvalidOperationException(
                "Replay package stopped before all commands were applied.");
        }
        var finalHash = simulation.ComputeStateHash();
        return new TestReplayTrace(trace, finalHash);
    }

    public bool RejectsReplayPackageResourceMismatch(TestReplayPackage package)
    {
        if (_navigationMap is null || _gameplayProfiles is null)
        {
            return false;
        }
        var source = package.Backend;
        var changedResources = source.Resources with
        {
            NavigationHash = source.Resources.NavigationHash ^ 1UL
        };
        var changed = new SimulationReplayPackageSnapshot(
            source.SimulationCapacity,
            source.InitialStateHash,
            changedResources,
            source.Units.ToArray(),
            source.Buildings.ToArray(),
            source.Economy,
            source.Construction,
            source.Production,
            source.Technology,
            source.Match,
            source.WorldCommands.ToArray(),
            source.EconomyCommandLog,
            source.ConstructionCommandLog,
            source.ProductionCommandLog,
            source.CommandLog);
        return !SimulationReplayPackageFactory.TryCreateSimulation(
                   changed,
                   _navigationMap,
                   _gameplayProfiles,
                   _clearanceBake,
                   out _,
                   out var validation) &&
               validation.Code == ReplayPackageValidationCode.ResourceMismatch;
    }

    public TestReplayCheckpoint CreateReplayCheckpoint(
        TestReplayPackage package,
        long tick,
        ulong stateHash) =>
        new(new SimulationReplayCheckpointSnapshot(
            tick, package.StableHash, stateHash));

    public TestReplayTrace ResumeReplayPackage(
        TestReplayPackage package,
        TestReplayCheckpoint checkpoint,
        long targetTick,
        int hashIntervalTicks = 30)
    {
        if (_navigationMap is null || _gameplayProfiles is null ||
            targetTick < checkpoint.Tick || hashIntervalTicks <= 0)
        {
            throw new InvalidOperationException(
                "Replay checkpoint assets or arguments are invalid.");
        }
        if (!SimulationReplayCheckpointFactory.TryRestore(
                checkpoint.Backend,
                package.Backend,
                _navigationMap,
                _gameplayProfiles,
                _clearanceBake,
                out var simulation,
                out var runner,
                out var validation) || simulation is null || runner is null)
        {
            throw new InvalidOperationException(
                $"Replay checkpoint restore failed: {validation.Code}.");
        }

        var trace = new SimulationReplayTrace();
        trace.Add(simulation.Metrics.Tick, simulation.ComputeStateHash());
        while (simulation.Metrics.Tick < targetTick)
        {
            runner.ApplyForCurrentTick(simulation);
            simulation.Tick(1f / 60f);
            if (simulation.Metrics.Tick % hashIntervalTicks == 0 ||
                simulation.Metrics.Tick == targetTick)
            {
                trace.Add(simulation.Metrics.Tick, simulation.ComputeStateHash());
            }
        }
        if (!runner.Completed)
        {
            throw new InvalidOperationException(
                "Replay checkpoint stopped before all commands were applied.");
        }
        return new TestReplayTrace(trace, simulation.ComputeStateHash());
    }

    public bool RejectsReplayCheckpointStateMismatch(
        TestReplayPackage package,
        TestReplayCheckpoint checkpoint)
    {
        if (_navigationMap is null || _gameplayProfiles is null)
        {
            return false;
        }
        var changed = checkpoint.WithStateHashOffset(1UL);
        return !SimulationReplayCheckpointFactory.TryRestore(
                   changed.Backend,
                   package.Backend,
                   _navigationMap,
                   _gameplayProfiles,
                   _clearanceBake,
                   out _,
                   out _,
                   out var validation) &&
               validation.Code == ReplayCheckpointValidationCode.StateMismatch;
    }

    public TestRuntimeStateCapture CaptureRuntimeState() =>
        new(_simulation.CaptureRuntimeState());

    public TestHotSnapshot BindHotSnapshot(
        TestReplayPackage package,
        TestRuntimeStateCapture capture) =>
        new(SimulationHotSnapshotFactory.Bind(capture.Backend, package.Backend));

    public TestReplayTrace ResumeHotSnapshot(
        TestReplayPackage package,
        TestHotSnapshot snapshot,
        long targetTick,
        int hashIntervalTicks = 30)
    {
        if (_navigationMap is null || _gameplayProfiles is null ||
            targetTick < snapshot.Tick || hashIntervalTicks <= 0 ||
            !SimulationHotSnapshotFactory.TryRestore(
                snapshot.Backend,
                package.Backend,
                _navigationMap,
                _gameplayProfiles,
                _clearanceBake,
                out var simulation,
                out var runner) ||
            simulation is null || runner is null)
        {
            throw new InvalidOperationException("Hot snapshot restore failed.");
        }

        var trace = new SimulationReplayTrace();
        trace.Add(simulation.Metrics.Tick, simulation.ComputeStateHash());
        while (simulation.Metrics.Tick < targetTick)
        {
            runner.ApplyForCurrentTick(simulation);
            simulation.Tick(1f / 60f);
            if (simulation.Metrics.Tick % hashIntervalTicks == 0 ||
                simulation.Metrics.Tick == targetTick)
            {
                trace.Add(simulation.Metrics.Tick, simulation.ComputeStateHash());
            }
        }
        if (!runner.Completed)
        {
            throw new InvalidOperationException(
                "Hot snapshot stopped before all commands were applied.");
        }
        return new TestReplayTrace(trace, simulation.ComputeStateHash());
    }

    public bool RejectsHotSnapshotPackageMismatch(
        TestReplayPackage package,
        TestHotSnapshot snapshot)
    {
        if (_navigationMap is null || _gameplayProfiles is null)
        {
            return false;
        }
        var source = package.Backend;
        var changed = new SimulationReplayPackageSnapshot(
            source.SimulationCapacity,
            source.InitialStateHash,
            source.Resources,
            source.Units.ToArray(),
            source.Buildings.ToArray(),
            source.Economy,
            source.Construction,
            source.Production,
            source.Technology,
            source.Match,
            source.WorldCommands.ToArray(),
            source.EconomyCommandLog,
            source.ConstructionCommandLog,
            source.ProductionCommandLog,
            new SimulationCommandLogSnapshot(
                source.CommandLog.Entries.Select(entry => entry with
                {
                    Units = entry.Units.ToArray()
                }).Append(new RecordedSimulationCommand(
                    long.MaxValue,
                    UnitOrderKind.Hold,
                    false,
                    Vector2.Zero,
                    -1,
                    -1,
                    -1,
                    [0])).ToArray()));
        return !SimulationHotSnapshotFactory.TryRestore(
            snapshot.Backend,
            changed,
            _navigationMap,
            _gameplayProfiles,
            _clearanceBake,
            out _,
            out _);
    }

    public bool RejectsHotSnapshotStateMismatch(
        TestReplayPackage package,
        TestHotSnapshot snapshot)
    {
        if (_navigationMap is null || _gameplayProfiles is null)
        {
            return false;
        }
        var changed = snapshot.WithBodyByteFlip();
        return !SimulationHotSnapshotFactory.TryRestore(
            changed.Backend,
            package.Backend,
            _navigationMap,
            _gameplayProfiles,
            _clearanceBake,
            out _,
            out _);
    }

    public TestReplayTrace Replay(
        TestCommandLog log,
        long targetTick,
        int hashIntervalTicks = 30)
    {
        if (targetTick < Tick || hashIntervalTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTick));
        }

        var replay = new SimulationCommandReplay(log.Backend);
        var trace = new SimulationReplayTrace();
        trace.Add(Tick, StateHash);
        while (Tick < targetTick)
        {
            replay.ApplyForCurrentTick(_simulation);
            Step();
            if (Tick % hashIntervalTicks == 0 || Tick == targetTick)
            {
                trace.Add(Tick, StateHash);
            }
        }
        if (!replay.Completed)
        {
            throw new InvalidOperationException(
                $"Replay stopped at tick {Tick} before all commands were applied.");
        }
        return new TestReplayTrace(trace, StateHash);
    }

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

    public TestCombatSnapshot ObserveCombat(TestUnitId unit)
    {
        var index = unit.Value;
        if ((uint)index >= (uint)_simulation.Units.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }

        var target = _simulation.Combat.TargetUnits[index];
        return new TestCombatSnapshot(
            unit,
            _simulation.Combat.Teams[index],
            _simulation.Units.Alive[index],
            _simulation.Combat.Health[index],
            _simulation.Combat.MaximumHealth[index],
            target >= 0 ? new TestUnitId(target) : null,
            !_simulation.Units.Alive[index]
                ? TestCombatState.Dead
                : (TestCombatState)_simulation.Combat.Phases[index],
            _simulation.Combat.HasAttackSlots[index],
            _simulation.Combat.AttackSlotTargets[index],
            _simulation.Combat.WindupRemaining[index],
            _simulation.Combat.CooldownRemaining[index]);
    }

    public TestCombatEventBatch ObserveCombatEvents(ulong afterSequence = 0)
    {
        var batch = _simulation.CombatEvents.ReadAfter(afterSequence);
        return new TestCombatEventBatch(
            batch.Events.Select(value => new TestCombatEvent(
                value.Tick, value.Sequence, value.Kind,
                new TestUnitId(value.AttackerUnit), value.TargetKind,
                value.TargetId, value.Damage, value.RemainingHealth,
                value.DamagePerAttack, value.AttacksApplied,
                value.BonusApplied, value.ProjectileId,
                value.WorldPosition)).ToArray(),
            batch.LatestSequence,
            batch.LostEvents);
    }

    public TestCombatProjectileSnapshot[] ObserveCombatProjectiles() =>
        _simulation.CombatProjectiles.ObserveActive()
            .Select(value => new TestCombatProjectileSnapshot(
                value.Id, new TestUnitId(value.AttackerUnit),
                value.TargetKind, value.TargetId, value.Position, value.Speed))
            .ToArray();

    public TestCombatPresentationFrame ObserveCombatPresentation(
        float elapsedSeconds = 1f / 60f)
    {
        var events = _simulation.CombatEvents.ReadAfter(
            _combatPresentation.LatestEventSequence);
        var frame = _combatPresentation.Update(
            _simulation.CombatProjectiles.ObserveActive(),
            events,
            elapsedSeconds);
        return new TestCombatPresentationFrame(
            frame.Projectiles.Select(value =>
                new TestCombatPresentationProjectile(
                    value.Id,
                    (TestCombatProjectileVisualKind)value.VisualKind,
                    value.Position,
                    value.Heading,
                    value.Trail.ToArray())).ToArray(),
            frame.Cues.Select(value => new TestCombatPresentationCue(
                value.Sequence,
                (TestCombatPresentationCueKind)value.Kind,
                value.ProjectileId,
                value.Position,
                value.NormalizedAge,
                value.Damage,
                value.BonusApplied)).ToArray(),
            frame.LatestEventSequence,
            frame.LostEvents);
    }

    public TestCombatDamagePreview PreviewCombatDamage(
        TestUnitId attacker,
        TestUnitId target)
    {
        var value = _simulation.PreviewCombatDamage(attacker.Value, target.Value);
        return new TestCombatDamagePreview(
            value.DamagePerAttack, value.TotalDamage,
            value.AttacksApplied, value.BonusApplied);
    }

    public TestCombatAutoTargetScore PreviewAutoTargetScore(
        TestUnitId attacker,
        TestUnitId target)
    {
        var value = _simulation.PreviewAutoTargetScore(
            attacker.Value, target.Value);
        return new TestCombatAutoTargetScore(
            new TestUnitId(value.TargetUnit),
            value.DistanceSquared,
            value.Priority,
            value.WeaponBonusMatch,
            value.ArmedThreat,
            value.TotalScore);
    }

    public TestCombatContactSnapshot PreviewCombatContact(TestUnitId unit) =>
        ToTestCombatContact(_simulation.PreviewCombatContact(unit.Value));

    public TestCombatContactResolution PreviewCombatContact(
        TestUnitId left,
        TestUnitId right)
    {
        var value = _simulation.PreviewCombatContact(left.Value, right.Value);
        return new TestCombatContactResolution(
            ToTestCombatContact(value.Left),
            ToTestCombatContact(value.Right),
            value.LeftCorrectionShare,
            value.RightCorrectionShare);
    }

    public TestCombatDamagePreview PreviewCombatDamage(
        TestUnitId attacker,
        TestGameplayBuildingId target)
    {
        var value = _simulation.PreviewCombatDamage(
            attacker.Value, new GameplayBuildingId(target.Value));
        return new TestCombatDamagePreview(
            value.DamagePerAttack, value.TotalDamage,
            value.AttacksApplied, value.BonusApplied);
    }

    private static TestCombatContactSnapshot ToTestCombatContact(
        CombatContactSnapshot value) => new(
        (TestCombatContactRole)value.Role,
        value.InverseMobility,
        value.ResistanceRank);

    public TestOrderSnapshot ObserveOrders(TestUnitId unit)
    {
        var index = unit.Value;
        if ((uint)index >= (uint)_simulation.Units.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }
        return new TestOrderSnapshot(
            (TestOrderKind)_simulation.CommandQueues.ActiveKinds[index],
            _simulation.CommandQueues.PendingCounts[index],
            _simulation.CommandQueues.CompletedQueuedOrders[index],
            _simulation.CommandQueues.QueueOverflowCounts[index],
            _simulation.CommandQueues.ActiveTargetBuildings[index],
            _simulation.CommandQueues.ActiveTargetUnits[index],
            _simulation.CommandQueues.ConstructionEvacuationActive[index],
            _simulation.CommandQueues.ConstructionEvacuationBuildings[index],
            _simulation.CommandQueues.ConstructionEvacuationTargets[index]);
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

    public bool IsUnitAlive(TestUnitId unit) =>
        (uint)unit.Value < (uint)_simulation.Units.Count &&
        _simulation.Units.Alive[unit.Value];

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

public enum TestDiagnosticKind : byte
{
    Info,
    Accepted,
    Rejected
}

public readonly record struct TestDiagnosticArea(
    SimRect Bounds,
    string Label,
    TestDiagnosticKind Kind);

public readonly record struct TestCameraKeyframe(
    long Tick,
    Vector2 Center,
    float Zoom);

public sealed class VisualTestSession
{
    private readonly SortedDictionary<long, List<ScheduledAction>> _actions = new();
    private readonly List<TestDiagnosticArea> _diagnosticAreas = [];
    private readonly List<TestGameplayBuildingId> _selectedBuildings = [];
    private readonly List<TestCameraKeyframe> _cameraKeyframes = [];
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
    internal int[] RenderUnitIndices => DynamicUnitRendering
        ? Enumerable.Range(0, Rig.UnitCount).ToArray()
        : Rig.ToBackendIndices(VisibleUnits);
    internal IReadOnlyList<TestDiagnosticArea> DiagnosticAreas => _diagnosticAreas;
    internal IReadOnlyList<TestGameplayBuildingId> SelectedBuildings =>
        _selectedBuildings;
    internal TargetCommandRequest? TargetCommandPreview { get; private set; }
    internal Vector2? TargetCommandPreviewPointer { get; private set; }
    private bool DynamicUnitRendering { get; set; }
    internal bool OmniscientRendering { get; private set; }
    internal bool HasCameraTrack => _cameraKeyframes.Count > 0;

    public VisualTestSession RenderSpawnedUnits()
    {
        DynamicUnitRendering = true;
        return this;
    }

    public VisualTestSession RenderOmniscient()
    {
        OmniscientRendering = true;
        return this;
    }

    public VisualTestSession CameraKeyframe(
        long tick,
        Vector2 center,
        float zoom)
    {
        if (tick < 0 || tick > DurationTicks ||
            !float.IsFinite(center.X) || !float.IsFinite(center.Y) ||
            !float.IsFinite(zoom) || zoom <= 0f)
            throw new ArgumentOutOfRangeException(nameof(tick));
        if (_cameraKeyframes.Any(value => value.Tick == tick))
            throw new InvalidOperationException(
                $"Camera keyframe {tick} is already defined.");
        _cameraKeyframes.Add(new TestCameraKeyframe(tick, center, zoom));
        _cameraKeyframes.Sort((left, right) => left.Tick.CompareTo(right.Tick));
        return this;
    }

    internal TestCameraKeyframe CameraAt(long tick)
    {
        if (_cameraKeyframes.Count == 0)
            throw new InvalidOperationException("No camera track is defined.");
        if (tick <= _cameraKeyframes[0].Tick) return _cameraKeyframes[0];
        for (var index = 1; index < _cameraKeyframes.Count; index++)
        {
            var right = _cameraKeyframes[index];
            if (tick > right.Tick) continue;
            var left = _cameraKeyframes[index - 1];
            var amount = (tick - left.Tick) /
                         (float)(right.Tick - left.Tick);
            amount = amount * amount * (3f - 2f * amount);
            return new TestCameraKeyframe(
                tick,
                Vector2.Lerp(left.Center, right.Center, amount),
                left.Zoom + (right.Zoom - left.Zoom) * amount);
        }
        return _cameraKeyframes[^1];
    }

    public VisualTestSession Highlight(
        SimRect bounds,
        string label,
        TestDiagnosticKind kind = TestDiagnosticKind.Info)
    {
        _diagnosticAreas.Add(new TestDiagnosticArea(bounds, label, kind));
        return this;
    }

    public VisualTestSession SelectBuildings(
        params TestGameplayBuildingId[] buildings)
    {
        _selectedBuildings.Clear();
        _selectedBuildings.AddRange(buildings.Distinct().OrderBy(value => value.Value));
        return this;
    }

    public VisualTestSession ShowTargetCommandPreview(
        TestTargetCommandRequest request,
        Vector2? pointer = null)
    {
        TargetCommandPreview = TargetCommandRequest.Create(
            (TargetCommandKind)request.Kind,
            request.Units.Select(value => value.Value),
            request.Buildings.Select(value => value.Value),
            request.Label,
            request.DataId);
        TargetCommandPreviewPointer = pointer;
        return this;
    }

    public VisualTestSession MoveTargetCommandPreviewPointerAt(
        long tick,
        string phase,
        Vector2 pointer) =>
        At(tick, phase, _ => TargetCommandPreviewPointer = pointer);

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
