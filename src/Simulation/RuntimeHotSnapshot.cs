namespace RtsDemo.Simulation;

public enum HotSnapshotValidationCode : byte
{
    Success,
    PayloadTooShort,
    InvalidMagic,
    UnsupportedVersion,
    UnsupportedStateHashVersion,
    InvalidHeader,
    InvalidBody,
    TrailingBytes
}

internal sealed record RtsPrivateRuntimeSnapshot(
    int PathBudgetPerTick,
    int PendingNavigationInvalidations,
    int NextMovementGroupId,
    int NextOrderSequenceId,
    PathRequest[] PathRequests);

internal sealed class SimulationRuntimeStateCapture
{
    public required long Tick { get; init; }
    public required ulong StateHash { get; init; }
    public required DynamicOccupancyRuntimeSnapshot DynamicOccupancy { get; init; }
    public required UnitStore Units { get; init; }
    public required CombatStore Combat { get; init; }
    public required CombatProjectileRuntimeSnapshot CombatProjectiles { get; init; }
    public required EconomyRuntimeSnapshot Economy { get; init; }
    public required PlayerVisibilityRuntimeSnapshot Visibility { get; init; }
    public required MatchRuntimeSnapshot Match { get; init; }
    public required ConstructionRuntimeSnapshot Construction { get; init; }
    public required ProductionRuntimeSnapshot Production { get; init; }
    public required TechnologyRuntimeSnapshot Technology { get; init; }
    public required UnitCommandQueueStore CommandQueues { get; init; }
    public ChokeTrafficRuntimeSnapshot? ChokeTraffic { get; init; }
    public required RtsPrivateRuntimeSnapshot PrivateState { get; init; }
}

/// <summary>
/// Process-local deep runtime snapshot. Unlike ReplayCheckpoint v1, restoring
/// this object does not replay earlier ticks. Durable binary encoding is a
/// separate format layer so internal SoA layout remains hidden from callers.
/// </summary>
public sealed class SimulationHotSnapshot
{
    public const int CurrentFormatVersion = 25;

    internal SimulationHotSnapshot(
        ulong packageHash,
        SimulationRuntimeStateCapture state)
    {
        PackageHash = packageHash;
        State = state;
        CanonicalBytes = RuntimeHotSnapshotCodec.Serialize(
            CurrentFormatVersion, packageHash, state);
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    internal SimulationRuntimeStateCapture State { get; }
    public int FormatVersion => CurrentFormatVersion;
    public ulong PackageHash { get; }
    public long Tick => State.Tick;
    public ulong StateHash => State.StateHash;
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> payload,
        out SimulationHotSnapshot? snapshot,
        out HotSnapshotValidationCode validation)
    {
        snapshot = null;
        if (!RuntimeHotSnapshotCodec.TryDeserialize(
                payload,
                CurrentFormatVersion,
                out var packageHash,
                out var state,
                out validation) ||
            state is null)
        {
            return false;
        }
        snapshot = new SimulationHotSnapshot(packageHash, state);
        return true;
    }
}

public static class SimulationHotSnapshotFactory
{
    internal static SimulationHotSnapshot Bind(
        SimulationRuntimeStateCapture state,
        SimulationReplayPackageSnapshot package) =>
        new(package.StableHash, state);

    public static bool TryRestore(
        SimulationHotSnapshot snapshot,
        SimulationReplayPackageSnapshot package,
        NavigationMapSnapshot navigation,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake,
        out RtsSimulation? simulation,
        out SimulationReplayPackageRunner? runner)
    {
        simulation = null;
        runner = null;
        if (snapshot.FormatVersion != SimulationHotSnapshot.CurrentFormatVersion ||
            snapshot.PackageHash != package.StableHash ||
            !SimulationReplayPackageFactory.TryCreateSimulation(
                package,
                navigation,
                gameplayProfiles,
                clearanceBake,
                out simulation,
                out _) ||
            simulation is null)
        {
            return false;
        }

        simulation.RestoreRuntimeState(snapshot.State);
        if (simulation.ComputeStateHash() != snapshot.StateHash)
        {
            simulation = null;
            return false;
        }
        runner = new SimulationReplayPackageRunner(package);
        runner.SeekToTick(snapshot.Tick);
        return true;
    }
}
