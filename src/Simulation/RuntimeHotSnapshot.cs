namespace RtsDemo.Simulation;

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
    public const int CurrentFormatVersion = 1;

    internal SimulationHotSnapshot(
        ulong packageHash,
        SimulationRuntimeStateCapture state)
    {
        PackageHash = packageHash;
        State = state;
        var hash = new StableHash64();
        hash.Add(CurrentFormatVersion);
        hash.Add((long)packageHash);
        hash.Add(state.Tick);
        hash.Add((long)state.StateHash);
        StableHash = hash.Value;
    }

    internal SimulationRuntimeStateCapture State { get; }
    public int FormatVersion => CurrentFormatVersion;
    public ulong PackageHash { get; }
    public long Tick => State.Tick;
    public ulong StateHash => State.StateHash;
    public ulong StableHash { get; }
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
