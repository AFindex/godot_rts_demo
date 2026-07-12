namespace RtsDemo.Simulation;

/// <summary>
/// Canonical exact-state hash for same-runtime deterministic replay diagnostics.
/// Floats are hashed by raw IEEE-754 bits; no tolerance or presentation rounding.
/// </summary>
public static class SimulationStateHasher
{
    public const int CurrentFormatVersion = 14;

    public static ulong Compute(RtsSimulation simulation)
    {
        var hash = new StableHash64();
        hash.Add(CurrentFormatVersion);
        hash.Add(simulation.Metrics.Tick);
        hash.Add(simulation.World.NavigationRevision);
        hash.Add(simulation.World.Bounds.Min);
        hash.Add(simulation.World.Bounds.Max);

        var obstacles = simulation.World.Obstacles;
        hash.Add(obstacles.Length);
        for (var index = 0; index < obstacles.Length; index++)
        {
            hash.Add(obstacles[index].Min);
            hash.Add(obstacles[index].Max);
        }

        simulation.World.DynamicOccupancy.AppendStateHash(ref hash);

        AppendUnits(ref hash, simulation.Units);
        AppendCombat(ref hash, simulation.Combat, simulation.Units.Count);
        simulation.Economy.AppendStateHash(ref hash, simulation.Units.Count);
        simulation.Construction.AppendStateHash(ref hash);
        simulation.Production.AppendStateHash(ref hash);
        simulation.Technology.AppendStateHash(ref hash);
        simulation.Visibility.AppendStateHash(ref hash);
        simulation.Match.AppendStateHash(ref hash);
        simulation.CommandQueues.AppendStateHash(ref hash, simulation.Units.Count);
        AppendChokes(ref hash, simulation.ChokeController);
        simulation.AppendPrivateStateHash(ref hash);
        return hash.Value;
    }

    private static void AppendUnits(ref StableHash64 hash, UnitStore units)
    {
        hash.Add(units.Count);
        hash.Add(units.Capacity);
        for (var unit = 0; unit < units.Count; unit++)
        {
            hash.Add(units.Alive[unit]);
            hash.Add(units.Positions[unit]);
            hash.Add(units.PreviousPositions[unit]);
            hash.Add(units.Velocities[unit]);
            hash.Add(units.PreferredVelocities[unit]);
            hash.Add(units.NextVelocities[unit]);
            hash.Add(units.SlotTargets[unit]);
            hash.Add(units.MoveGoals[unit]);
            hash.Add(units.Radii[unit]);
            hash.Add((byte)units.MovementClasses[unit]);
            hash.Add(units.NavigationRadii[unit]);
            hash.Add(units.MaxSpeeds[unit]);
            hash.Add(units.Accelerations[unit]);
            hash.Add((byte)units.Modes[unit]);
            hash.Add(units.CommandVersions[unit]);
            hash.Add(units.PathPending[unit]);
            hash.Add((byte)units.AvoidanceSides[unit]);
            hash.Add((int)units.AvoidanceLockTicks[unit]);
            hash.Add(units.ProgressOrigins[unit]);
            hash.Add(units.ProgressTimers[unit]);
            hash.Add(units.ProgressBestDistances[unit]);
            hash.Add(units.RepathCooldowns[unit]);
            hash.Add(units.CollisionCorrections[unit]);
            hash.Add(units.ActiveChokeIds[unit]);
            hash.Add((byte)units.ChokeDirections[unit]);
            hash.Add(units.ChokeLaneOffsets[unit]);
            hash.Add((byte)units.ChokePhases[unit]);
            hash.Add(units.ChokeAdmitted[unit]);
            hash.Add(units.ChokeQueueRanks[unit]);
            hash.Add(units.ChokeWaitTicks[unit]);
            hash.Add(units.BlockedByNavigation[unit]);
            hash.Add((byte)units.RecoveryStages[unit]);
            hash.Add(units.RecoveryEventCounts[unit]);
            hash.Add(units.RecoveryStableTimers[unit]);
            hash.Add(units.RecoveryRetryCounts[unit]);
            hash.Add(units.MovementGroupIds[unit]);
            hash.Add(units.MovementGroupSizes[unit]);
            hash.Add(units.SlotReflowCooldownTicks[unit]);
            hash.Add(units.DestinationBestDistances[unit]);
            hash.Add(units.DestinationStallTicks[unit]);
            hash.Add(units.DestinationNearTicks[unit]);
            hash.Add(units.DestinationOverflowed[unit]);
            hash.Add((byte)units.DestinationYieldPhases[unit]);
            hash.Add(units.DestinationYieldReturnTargets[unit]);
            hash.Add(units.DestinationYieldPoints[unit]);
            hash.Add(units.DestinationYieldForUnits[unit]);
            hash.Add(units.DestinationYieldForCommandVersions[unit]);
            hash.Add(units.DestinationYieldDeadlines[unit]);
            hash.Add(units.DestinationYieldCooldownTicks[unit]);

            var path = units.Paths[unit];
            hash.Add(path is not null);
            if (path is not null)
            {
                hash.Add(path.CommandVersion);
                hash.Add(path.Cursor);
                hash.Add(path.Points.Length);
                for (var point = 0; point < path.Points.Length; point++)
                {
                    hash.Add(path.Points[point]);
                }
            }

            var route = units.RouteWaypoints[unit];
            hash.Add(route.Length);
            for (var point = 0; point < route.Length; point++)
            {
                hash.Add(route[point]);
            }
        }
    }

    private static void AppendCombat(
        ref StableHash64 hash,
        CombatStore combat,
        int unitCount)
    {
        for (var unit = 0; unit < unitCount; unit++)
        {
            hash.Add(combat.Teams[unit]);
            hash.Add(combat.Health[unit]);
            hash.Add(combat.MaximumHealth[unit]);
            hash.Add(combat.AttackDamage[unit]);
            hash.Add(combat.Armor[unit]);
            hash.Add((ushort)combat.Attributes[unit]);
            hash.Add(combat.AttacksPerVolley[unit]);
            hash.Add((ushort)combat.BonusVs[unit]);
            hash.Add(combat.BonusDamage[unit]);
            hash.Add(combat.BaseUpgradeDamage[unit]);
            hash.Add(combat.BonusUpgradeDamage[unit]);
            hash.Add(combat.AttackRanges[unit]);
            hash.Add(combat.AcquisitionRanges[unit]);
            hash.Add(combat.AttackCooldownDurations[unit]);
            hash.Add(combat.AttackWindupDurations[unit]);
            hash.Add(combat.LeashDistances[unit]);
            hash.Add((byte)combat.PositioningKinds[unit]);
            hash.Add((byte)combat.CommandIntents[unit]);
            hash.Add((byte)combat.Phases[unit]);
            hash.Add(combat.TargetUnits[unit]);
            hash.Add(combat.TargetBuildings[unit]);
            hash.Add((byte)combat.TargetKinds[unit]);
            hash.Add(combat.AttackMoveGoals[unit]);
            hash.Add(combat.EngagementOrigins[unit]);
            hash.Add(combat.LastChaseTargets[unit]);
            hash.Add(combat.AttackSlotTargets[unit]);
            hash.Add(combat.AttackSlotAngles[unit]);
            hash.Add(combat.AttackSlotRadii[unit]);
            hash.Add(combat.HasAttackSlots[unit]);
            hash.Add(combat.CooldownRemaining[unit]);
            hash.Add(combat.WindupRemaining[unit]);
            hash.Add(combat.ChaseRepathRemaining[unit]);
        }
    }

    private static void AppendChokes(
        ref StableHash64 hash,
        ChokeController? controller)
    {
        if (controller is null)
        {
            hash.Add(0);
            return;
        }

        var snapshots = controller.TrafficSnapshots;
        hash.Add(snapshots.Length);
        for (var index = 0; index < snapshots.Length; index++)
        {
            var snapshot = snapshots[index];
            hash.Add(snapshot.ChokeId);
            hash.Add((byte)snapshot.ActiveDirection);
            hash.Add(snapshot.Draining);
            hash.Add(snapshot.PositiveQueue);
            hash.Add(snapshot.NegativeQueue);
            hash.Add(snapshot.InFlight);
            hash.Add(snapshot.PositiveWaitTicks);
            hash.Add(snapshot.NegativeWaitTicks);
            hash.Add(snapshot.BurstTicks);
            hash.Add(snapshot.Capacity);
            hash.Add(snapshot.ConflictTicks);
            hash.Add(snapshot.MaximumPositiveWaitTicks);
            hash.Add(snapshot.MaximumNegativeWaitTicks);
            hash.Add(snapshot.HardBlockers);
            hash.Add(snapshot.BlockedTicks);
        }
        controller.AppendStateHash(ref hash);
    }
}

public readonly record struct SimulationStateHashSample(long Tick, ulong Hash);

public sealed class SimulationReplayTrace
{
    private readonly List<SimulationStateHashSample> _samples = [];

    public IReadOnlyList<SimulationStateHashSample> Samples => _samples;

    public void Add(long tick, ulong hash) =>
        _samples.Add(new SimulationStateHashSample(tick, hash));

    public static long FindFirstDivergence(
        SimulationReplayTrace first,
        SimulationReplayTrace second)
    {
        var count = Math.Min(first._samples.Count, second._samples.Count);
        for (var index = 0; index < count; index++)
        {
            if (first._samples[index].Tick != second._samples[index].Tick ||
                first._samples[index].Hash != second._samples[index].Hash)
            {
                return Math.Min(
                    first._samples[index].Tick,
                    second._samples[index].Tick);
            }
        }
        return first._samples.Count == second._samples.Count
            ? -1
            : count == 0
                ? 0
                : Math.Min(
                    first._samples[Math.Min(count, first._samples.Count - 1)].Tick,
                    second._samples[Math.Min(count, second._samples.Count - 1)].Tick);
    }
}
