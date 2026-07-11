using System.Numerics;

namespace RtsDemo.Simulation;

internal static class RuntimeHotSnapshotCodec
{
    private const uint Magic = 0x53535452; // RTSS in little-endian bytes.
    private const int MaximumCapacity = 100_000;
    private const int MaximumPoints = 1_000_000;

    public static byte[] Serialize(
        int formatVersion,
        ulong packageHash,
        SimulationRuntimeStateCapture state)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(formatVersion);
        writer.Write(SimulationStateHasher.CurrentFormatVersion);
        writer.Write(packageHash);
        writer.Write(state.Tick);
        writer.Write(state.StateHash);
        writer.Write(state.Units.Capacity);
        WriteDynamic(writer, state.DynamicOccupancy);
        WriteUnits(writer, state.Units);
        WriteCombat(writer, state.Combat, state.Units.Count);
        WriteQueues(writer, state.CommandQueues, state.Units.Count);
        WriteChoke(writer, state.ChokeTraffic);
        WritePrivate(writer, state.PrivateState);
        writer.Flush();
        return stream.ToArray();
    }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> payload,
        int expectedFormatVersion,
        out ulong packageHash,
        out SimulationRuntimeStateCapture? state,
        out HotSnapshotValidationCode code)
    {
        packageHash = 0UL;
        state = null;
        if (payload.Length < 48)
        {
            code = HotSnapshotValidationCode.PayloadTooShort;
            return false;
        }
        try
        {
            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic)
            {
                code = HotSnapshotValidationCode.InvalidMagic;
                return false;
            }
            if (reader.ReadInt32() != expectedFormatVersion)
            {
                code = HotSnapshotValidationCode.UnsupportedVersion;
                return false;
            }
            if (reader.ReadInt32() != SimulationStateHasher.CurrentFormatVersion)
            {
                code = HotSnapshotValidationCode.UnsupportedStateHashVersion;
                return false;
            }
            packageHash = reader.ReadUInt64();
            var tick = reader.ReadInt64();
            var stateHash = reader.ReadUInt64();
            var capacity = reader.ReadInt32();
            if (tick < 0 || capacity <= 0 || capacity > MaximumCapacity)
            {
                code = HotSnapshotValidationCode.InvalidHeader;
                return false;
            }

            var dynamic = ReadDynamic(reader);
            var units = ReadUnits(reader, capacity);
            var combat = ReadCombat(reader, capacity, units.Count);
            var queues = ReadQueues(reader, capacity, units.Count);
            var choke = ReadChoke(reader);
            var privateState = ReadPrivate(reader);
            if (stream.Position != stream.Length)
            {
                code = HotSnapshotValidationCode.TrailingBytes;
                return false;
            }
            state = new SimulationRuntimeStateCapture
            {
                Tick = tick,
                StateHash = stateHash,
                DynamicOccupancy = dynamic,
                Units = units,
                Combat = combat,
                CommandQueues = queues,
                ChokeTraffic = choke,
                PrivateState = privateState
            };
            code = HotSnapshotValidationCode.Success;
            return true;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or
            InvalidDataException or ArgumentException or
            InvalidOperationException or OverflowException)
        {
            code = HotSnapshotValidationCode.InvalidBody;
            return false;
        }
    }

    private static void WriteDynamic(
        BinaryWriter writer, DynamicOccupancyRuntimeSnapshot state)
    {
        writer.Write(state.Revision);
        writer.Write(state.NextId);
        writer.Write(state.Footprints.Length);
        for (var index = 0; index < state.Footprints.Length; index++)
        {
            var footprint = state.Footprints[index];
            writer.Write(footprint.Id.Value);
            WriteRect(writer, footprint.Bounds);
            writer.Write(footprint.PlacedRevision);
        }
    }

    private static DynamicOccupancyRuntimeSnapshot ReadDynamic(BinaryReader reader)
    {
        var revision = reader.ReadInt32();
        var nextId = reader.ReadInt32();
        var count = ReadCount(reader, MaximumCapacity);
        if (revision < 0 || nextId <= 0)
        {
            throw new InvalidDataException();
        }
        var footprints = new DynamicFootprint[count];
        for (var index = 0; index < count; index++)
        {
            var id = reader.ReadInt32();
            var bounds = ReadRect(reader);
            var placedRevision = reader.ReadInt32();
            if (id <= 0 || placedRevision <= 0)
            {
                throw new InvalidDataException();
            }
            footprints[index] = new DynamicFootprint(
                new DynamicFootprintId(id), bounds, placedRevision);
        }
        return new DynamicOccupancyRuntimeSnapshot(revision, nextId, footprints);
    }

    private static void WriteUnits(BinaryWriter writer, UnitStore units)
    {
        writer.Write(units.Count);
        for (var unit = 0; unit < units.Count; unit++)
        {
            writer.Write(units.Alive[unit]);
            WriteVector(writer, units.Positions[unit]);
            WriteVector(writer, units.PreviousPositions[unit]);
            WriteVector(writer, units.Velocities[unit]);
            WriteVector(writer, units.PreferredVelocities[unit]);
            WriteVector(writer, units.NextVelocities[unit]);
            WriteVector(writer, units.SlotTargets[unit]);
            WriteVector(writer, units.MoveGoals[unit]);
            writer.Write(units.Radii[unit]);
            writer.Write((byte)units.MovementClasses[unit]);
            writer.Write(units.NavigationRadii[unit]);
            writer.Write(units.MaxSpeeds[unit]);
            writer.Write(units.Accelerations[unit]);
            writer.Write((byte)units.Modes[unit]);
            writer.Write(units.CommandVersions[unit]);
            writer.Write(units.PathPending[unit]);
            writer.Write(units.AvoidanceSides[unit]);
            writer.Write(units.AvoidanceLockTicks[unit]);
            WriteVector(writer, units.ProgressOrigins[unit]);
            writer.Write(units.ProgressTimers[unit]);
            writer.Write(units.ProgressBestDistances[unit]);
            writer.Write(units.RepathCooldowns[unit]);
            WriteVector(writer, units.CollisionCorrections[unit]);
            writer.Write(units.ActiveChokeIds[unit]);
            writer.Write(units.ChokeDirections[unit]);
            writer.Write(units.ChokeLaneOffsets[unit]);
            writer.Write((byte)units.ChokePhases[unit]);
            writer.Write(units.ChokeAdmitted[unit]);
            writer.Write(units.ChokeQueueRanks[unit]);
            writer.Write(units.ChokeWaitTicks[unit]);
            writer.Write(units.BlockedByNavigation[unit]);
            writer.Write((byte)units.RecoveryStages[unit]);
            writer.Write(units.RecoveryEventCounts[unit]);
            writer.Write(units.RecoveryStableTimers[unit]);
            writer.Write(units.RecoveryRetryCounts[unit]);
            writer.Write(units.MovementGroupIds[unit]);
            writer.Write(units.MovementGroupSizes[unit]);
            writer.Write(units.SlotReflowCooldownTicks[unit]);
            writer.Write(units.DestinationBestDistances[unit]);
            writer.Write(units.DestinationStallTicks[unit]);
            writer.Write(units.DestinationNearTicks[unit]);
            writer.Write(units.DestinationOverflowed[unit]);
            writer.Write((byte)units.DestinationYieldPhases[unit]);
            WriteVector(writer, units.DestinationYieldReturnTargets[unit]);
            WriteVector(writer, units.DestinationYieldPoints[unit]);
            writer.Write(units.DestinationYieldForUnits[unit]);
            writer.Write(units.DestinationYieldForCommandVersions[unit]);
            writer.Write(units.DestinationYieldDeadlines[unit]);
            writer.Write(units.DestinationYieldCooldownTicks[unit]);
            var path = units.Paths[unit];
            writer.Write(path is not null);
            if (path is not null)
            {
                writer.Write(path.CommandVersion);
                writer.Write(path.Cursor);
                WriteVectors(writer, path.Points);
            }
            WriteVectors(writer, units.RouteWaypoints[unit]);
        }
    }

    private static UnitStore ReadUnits(BinaryReader reader, int capacity)
    {
        var count = ReadCount(reader, capacity);
        var units = new UnitStore(capacity);
        units.SetRuntimeCount(count);
        for (var unit = 0; unit < count; unit++)
        {
            units.Alive[unit] = reader.ReadBoolean();
            units.Positions[unit] = ReadVector(reader);
            units.PreviousPositions[unit] = ReadVector(reader);
            units.Velocities[unit] = ReadVector(reader);
            units.PreferredVelocities[unit] = ReadVector(reader);
            units.NextVelocities[unit] = ReadVector(reader);
            units.SlotTargets[unit] = ReadVector(reader);
            units.MoveGoals[unit] = ReadVector(reader);
            units.Radii[unit] = reader.ReadSingle();
            units.MovementClasses[unit] = (MovementClass)reader.ReadByte();
            units.NavigationRadii[unit] = reader.ReadSingle();
            units.MaxSpeeds[unit] = reader.ReadSingle();
            units.Accelerations[unit] = reader.ReadSingle();
            units.Modes[unit] = (UnitMoveMode)reader.ReadByte();
            units.CommandVersions[unit] = reader.ReadInt32();
            units.PathPending[unit] = reader.ReadBoolean();
            units.AvoidanceSides[unit] = reader.ReadSByte();
            units.AvoidanceLockTicks[unit] = reader.ReadInt16();
            units.ProgressOrigins[unit] = ReadVector(reader);
            units.ProgressTimers[unit] = reader.ReadSingle();
            units.ProgressBestDistances[unit] = reader.ReadSingle();
            units.RepathCooldowns[unit] = reader.ReadSingle();
            units.CollisionCorrections[unit] = ReadVector(reader);
            units.ActiveChokeIds[unit] = reader.ReadInt32();
            units.ChokeDirections[unit] = reader.ReadSByte();
            units.ChokeLaneOffsets[unit] = reader.ReadSingle();
            units.ChokePhases[unit] = (ChokePhase)reader.ReadByte();
            units.ChokeAdmitted[unit] = reader.ReadBoolean();
            units.ChokeQueueRanks[unit] = reader.ReadInt32();
            units.ChokeWaitTicks[unit] = reader.ReadInt32();
            units.BlockedByNavigation[unit] = reader.ReadBoolean();
            units.RecoveryStages[unit] = (RecoveryStage)reader.ReadByte();
            units.RecoveryEventCounts[unit] = reader.ReadInt32();
            units.RecoveryStableTimers[unit] = reader.ReadSingle();
            units.RecoveryRetryCounts[unit] = reader.ReadByte();
            units.MovementGroupIds[unit] = reader.ReadInt32();
            units.MovementGroupSizes[unit] = reader.ReadInt32();
            units.SlotReflowCooldownTicks[unit] = reader.ReadInt64();
            units.DestinationBestDistances[unit] = reader.ReadSingle();
            units.DestinationStallTicks[unit] = reader.ReadInt32();
            units.DestinationNearTicks[unit] = reader.ReadInt32();
            units.DestinationOverflowed[unit] = reader.ReadBoolean();
            units.DestinationYieldPhases[unit] = (DestinationYieldPhase)reader.ReadByte();
            units.DestinationYieldReturnTargets[unit] = ReadVector(reader);
            units.DestinationYieldPoints[unit] = ReadVector(reader);
            units.DestinationYieldForUnits[unit] = reader.ReadInt32();
            units.DestinationYieldForCommandVersions[unit] = reader.ReadInt32();
            units.DestinationYieldDeadlines[unit] = reader.ReadInt64();
            units.DestinationYieldCooldownTicks[unit] = reader.ReadInt64();
            if (reader.ReadBoolean())
            {
                var version = reader.ReadInt32();
                var cursor = reader.ReadInt32();
                var path = new UnitPath(ReadVectors(reader), version) { Cursor = cursor };
                if ((uint)cursor >= (uint)Math.Max(1, path.Points.Length))
                {
                    throw new InvalidDataException();
                }
                units.Paths[unit] = path;
            }
            units.RouteWaypoints[unit] = ReadVectors(reader);
        }
        return units;
    }

    private static void WriteCombat(BinaryWriter writer, CombatStore combat, int count)
    {
        for (var unit = 0; unit < count; unit++)
        {
            writer.Write(combat.Teams[unit]);
            writer.Write(combat.Health[unit]);
            writer.Write(combat.MaximumHealth[unit]);
            writer.Write(combat.AttackDamage[unit]);
            writer.Write(combat.AttackRanges[unit]);
            writer.Write(combat.AcquisitionRanges[unit]);
            writer.Write(combat.AttackCooldownDurations[unit]);
            writer.Write(combat.AttackWindupDurations[unit]);
            writer.Write(combat.LeashDistances[unit]);
            writer.Write((byte)combat.PositioningKinds[unit]);
            writer.Write((byte)combat.CommandIntents[unit]);
            writer.Write((byte)combat.Phases[unit]);
            writer.Write(combat.TargetUnits[unit]);
            WriteVector(writer, combat.AttackMoveGoals[unit]);
            WriteVector(writer, combat.EngagementOrigins[unit]);
            WriteVector(writer, combat.LastChaseTargets[unit]);
            WriteVector(writer, combat.AttackSlotTargets[unit]);
            writer.Write(combat.AttackSlotAngles[unit]);
            writer.Write(combat.AttackSlotRadii[unit]);
            writer.Write(combat.HasAttackSlots[unit]);
            writer.Write(combat.CooldownRemaining[unit]);
            writer.Write(combat.WindupRemaining[unit]);
            writer.Write(combat.ChaseRepathRemaining[unit]);
        }
    }

    private static CombatStore ReadCombat(BinaryReader reader, int capacity, int count)
    {
        var combat = new CombatStore(capacity);
        for (var unit = 0; unit < count; unit++)
        {
            combat.Teams[unit] = reader.ReadInt32();
            combat.Health[unit] = reader.ReadSingle();
            combat.MaximumHealth[unit] = reader.ReadSingle();
            combat.AttackDamage[unit] = reader.ReadSingle();
            combat.AttackRanges[unit] = reader.ReadSingle();
            combat.AcquisitionRanges[unit] = reader.ReadSingle();
            combat.AttackCooldownDurations[unit] = reader.ReadSingle();
            combat.AttackWindupDurations[unit] = reader.ReadSingle();
            combat.LeashDistances[unit] = reader.ReadSingle();
            combat.PositioningKinds[unit] = (CombatPositioningKind)reader.ReadByte();
            combat.CommandIntents[unit] = (UnitCommandIntent)reader.ReadByte();
            combat.Phases[unit] = (CombatPhase)reader.ReadByte();
            combat.TargetUnits[unit] = reader.ReadInt32();
            combat.AttackMoveGoals[unit] = ReadVector(reader);
            combat.EngagementOrigins[unit] = ReadVector(reader);
            combat.LastChaseTargets[unit] = ReadVector(reader);
            combat.AttackSlotTargets[unit] = ReadVector(reader);
            combat.AttackSlotAngles[unit] = reader.ReadSingle();
            combat.AttackSlotRadii[unit] = reader.ReadSingle();
            combat.HasAttackSlots[unit] = reader.ReadBoolean();
            combat.CooldownRemaining[unit] = reader.ReadSingle();
            combat.WindupRemaining[unit] = reader.ReadSingle();
            combat.ChaseRepathRemaining[unit] = reader.ReadSingle();
        }
        return combat;
    }

    private static void WriteQueues(
        BinaryWriter writer, UnitCommandQueueStore queues, int count)
    {
        for (var unit = 0; unit < count; unit++)
        {
            writer.Write(queues.HasActiveOrders[unit]);
            writer.Write((byte)queues.ActiveKinds[unit]);
            WriteVector(writer, queues.ActivePositions[unit]);
            writer.Write(queues.ActiveTargetUnits[unit]);
            writer.Write(queues.ActiveSequenceIds[unit]);
            writer.Write(queues.ActiveOrdersWereQueued[unit]);
            writer.Write(queues.CompletedQueuedOrders[unit]);
            writer.Write(queues.QueueOverflowCounts[unit]);
            writer.Write(queues.PendingCounts[unit]);
            for (var pending = 0; pending < queues.PendingCounts[unit]; pending++)
            {
                var order = queues.PendingAt(unit, pending);
                writer.Write((byte)order.Kind);
                WriteVector(writer, order.TargetPosition);
                writer.Write(order.TargetUnit);
                writer.Write(order.SequenceId);
            }
        }
    }

    private static UnitCommandQueueStore ReadQueues(
        BinaryReader reader, int capacity, int count)
    {
        var queues = new UnitCommandQueueStore(capacity);
        for (var unit = 0; unit < count; unit++)
        {
            queues.HasActiveOrders[unit] = reader.ReadBoolean();
            queues.ActiveKinds[unit] = (UnitOrderKind)reader.ReadByte();
            queues.ActivePositions[unit] = ReadVector(reader);
            queues.ActiveTargetUnits[unit] = reader.ReadInt32();
            queues.ActiveSequenceIds[unit] = reader.ReadInt32();
            queues.ActiveOrdersWereQueued[unit] = reader.ReadBoolean();
            queues.CompletedQueuedOrders[unit] = reader.ReadInt32();
            queues.QueueOverflowCounts[unit] = reader.ReadInt32();
            var pendingCount = reader.ReadByte();
            if (pendingCount > UnitCommandQueueStore.MaximumPendingOrders)
            {
                throw new InvalidDataException();
            }
            for (var pending = 0; pending < pendingCount; pending++)
            {
                var order = new UnitOrder(
                    (UnitOrderKind)reader.ReadByte(),
                    ReadVector(reader),
                    reader.ReadInt32(),
                    reader.ReadInt32());
                if (!queues.TryEnqueue(unit, order))
                {
                    throw new InvalidDataException();
                }
            }
        }
        return queues;
    }

    private static void WriteChoke(
        BinaryWriter writer, ChokeTrafficRuntimeSnapshot? choke)
    {
        writer.Write(choke is not null);
        if (choke is null)
        {
            return;
        }
        writer.Write(choke.States.Length);
        for (var index = 0; index < choke.States.Length; index++)
        {
            var state = choke.States[index];
            writer.Write(state.ActiveDirection);
            writer.Write(state.LastDirection);
            writer.Write(state.Draining);
            writer.Write(state.ClearTicks);
            writer.Write(state.BurstTicks);
            writer.Write(state.PositiveWaitTicks);
            writer.Write(state.NegativeWaitTicks);
            writer.Write(state.Capacity);
            writer.Write(state.ConflictTicks);
            writer.Write(state.MaximumPositiveWaitTicks);
            writer.Write(state.MaximumNegativeWaitTicks);
            writer.Write(state.BlockedTicks);
        }
        writer.Write(choke.Diagnostics.Length);
        for (var index = 0; index < choke.Diagnostics.Length; index++)
        {
            var value = choke.Diagnostics[index];
            writer.Write(value.ChokeId);
            writer.Write(value.ActiveDirection);
            writer.Write(value.Draining);
            writer.Write(value.PositiveQueue);
            writer.Write(value.NegativeQueue);
            writer.Write(value.InFlight);
            writer.Write(value.PositiveWaitTicks);
            writer.Write(value.NegativeWaitTicks);
            writer.Write(value.BurstTicks);
            writer.Write(value.Capacity);
            writer.Write(value.ConflictTicks);
            writer.Write(value.MaximumPositiveWaitTicks);
            writer.Write(value.MaximumNegativeWaitTicks);
            writer.Write(value.HardBlockers);
            writer.Write(value.BlockedTicks);
        }
    }

    private static ChokeTrafficRuntimeSnapshot? ReadChoke(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }
        var count = ReadCount(reader, 10_000);
        var states = new ChokeTrafficStateSnapshot[count];
        for (var index = 0; index < count; index++)
        {
            states[index] = new ChokeTrafficStateSnapshot(
                reader.ReadSByte(), reader.ReadSByte(), reader.ReadBoolean(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }
        var diagnosticCount = ReadCount(reader, 10_000);
        if (diagnosticCount != count)
        {
            throw new InvalidDataException();
        }
        var diagnostics = new ChokeTrafficSnapshot[count];
        for (var index = 0; index < count; index++)
        {
            diagnostics[index] = new ChokeTrafficSnapshot(
                reader.ReadInt32(), reader.ReadSByte(), reader.ReadBoolean(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }
        return new ChokeTrafficRuntimeSnapshot(states, diagnostics);
    }

    private static void WritePrivate(
        BinaryWriter writer, RtsPrivateRuntimeSnapshot state)
    {
        writer.Write(state.PathBudgetPerTick);
        writer.Write(state.PendingNavigationInvalidations);
        writer.Write(state.NextMovementGroupId);
        writer.Write(state.NextOrderSequenceId);
        writer.Write(state.PathRequests.Length);
        for (var index = 0; index < state.PathRequests.Length; index++)
        {
            writer.Write(state.PathRequests[index].UnitIndex);
            writer.Write(state.PathRequests[index].CommandVersion);
        }
    }

    private static RtsPrivateRuntimeSnapshot ReadPrivate(BinaryReader reader)
    {
        var budget = reader.ReadInt32();
        var invalidations = reader.ReadInt32();
        var nextGroup = reader.ReadInt32();
        var nextOrder = reader.ReadInt32();
        var count = ReadCount(reader, MaximumCapacity);
        if (budget < 0 || invalidations < 0 || nextGroup <= 0 || nextOrder <= 0)
        {
            throw new InvalidDataException();
        }
        var requests = new PathRequest[count];
        for (var index = 0; index < count; index++)
        {
            requests[index] = new PathRequest(
                reader.ReadInt32(), reader.ReadInt32());
        }
        return new RtsPrivateRuntimeSnapshot(
            budget, invalidations, nextGroup, nextOrder, requests);
    }

    private static int ReadCount(BinaryReader reader, int maximum)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > maximum)
        {
            throw new InvalidDataException();
        }
        return count;
    }

    private static void WriteVectors(BinaryWriter writer, Vector2[] values)
    {
        writer.Write(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            WriteVector(writer, values[index]);
        }
    }

    private static Vector2[] ReadVectors(BinaryReader reader)
    {
        var count = ReadCount(reader, MaximumPoints);
        var values = new Vector2[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = ReadVector(reader);
        }
        return values;
    }

    private static void WriteRect(BinaryWriter writer, SimRect value)
    {
        WriteVector(writer, value.Min);
        WriteVector(writer, value.Max);
    }

    private static SimRect ReadRect(BinaryReader reader) =>
        new(ReadVector(reader), ReadVector(reader));

    private static void WriteVector(BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    private static Vector2 ReadVector(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle());
}
