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
        AbilitySerialization.WriteRuntime(writer, state.Abilities);
        WriteProjectiles(writer, state.CombatProjectiles);
        WriteEconomy(writer, state.Economy, state.Units.Count);
        WriteDiplomacy(writer, state.Diplomacy);
        WriteVisibility(writer, state.Visibility);
        WriteMatch(writer, state.Match);
        WriteConstruction(writer, state.Construction);
        WriteBuildingUpgrades(writer, state.BuildingUpgrades);
        WriteProduction(writer, state.Production);
        WriteTechnology(writer, state.Technology);
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
            var abilities = AbilitySerialization.ReadRuntime(reader, units.Count);
            var projectiles = ReadProjectiles(reader, units.Count);
            var economy = ReadEconomy(reader, units.Count);
            var diplomacy = ReadDiplomacy(reader);
            var diplomacySystem = new PlayerDiplomacySystem();
            diplomacySystem.RestoreRuntimeState(diplomacy);
            var visibility = ReadVisibility(reader);
            var match = ReadMatch(reader, economy, diplomacySystem);
            var construction = ReadConstruction(
                reader, units.Count, dynamic.Footprints, economy);
            var buildingUpgrades = ReadBuildingUpgrades(
                reader, construction, economy);
            var production = ReadProduction(
                reader, units.Count, construction, economy);
            var technology = ReadTechnology(reader, construction, economy);
            ValidateCombatTargets(combat, units.Count, construction);
            var queues = ReadQueues(
                reader, capacity, units.Count, construction.Buildings.Length);
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
                Abilities = abilities,
                CombatProjectiles = projectiles,
                Economy = economy,
                Diplomacy = diplomacy,
                Visibility = visibility,
                Match = match,
                Construction = construction,
                BuildingUpgrades = buildingUpgrades,
                Production = production,
                Technology = technology,
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
            writer.Write(units.Facings[unit]);
            writer.Write(units.PreviousFacings[unit]);
            writer.Write(units.TurnRatesRadiansPerSecond[unit]);
            WriteVector(writer, units.Velocities[unit]);
            WriteVector(writer, units.PreferredVelocities[unit]);
            WriteVector(writer, units.NextVelocities[unit]);
            WriteVector(writer, units.SlotTargets[unit]);
            WriteVector(writer, units.MoveGoals[unit]);
            writer.Write((byte)units.MovementGoalKinds[unit]);
            WriteRect(writer, units.MovementGoalBounds[unit]);
            writer.Write(units.MovementGoalRadii[unit]);
            writer.Write(units.MovementGoalTargetIds[unit]);
            writer.Write((byte)units.MovementLegResults[unit]);
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
            writer.Write(units.DynamicBlockageTicks[unit]);
            writer.Write(units.DynamicBlockageBestDistances[unit]);
            writer.Write(units.ReservationMigrationTicks[unit]);
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
            units.Facings[unit] = reader.ReadSingle();
            units.PreviousFacings[unit] = reader.ReadSingle();
            units.TurnRatesRadiansPerSecond[unit] = reader.ReadSingle();
            if (!float.IsFinite(units.Facings[unit]) ||
                !float.IsFinite(units.PreviousFacings[unit]) ||
                !float.IsFinite(units.TurnRatesRadiansPerSecond[unit]) ||
                units.TurnRatesRadiansPerSecond[unit] <= 0f)
                throw new InvalidDataException();
            units.Velocities[unit] = ReadVector(reader);
            units.PreferredVelocities[unit] = ReadVector(reader);
            units.NextVelocities[unit] = ReadVector(reader);
            units.SlotTargets[unit] = ReadVector(reader);
            units.MoveGoals[unit] = ReadVector(reader);
            units.MovementGoalKinds[unit] =
                (UnitMovementGoalKind)reader.ReadByte();
            units.MovementGoalBounds[unit] = ReadRect(reader);
            units.MovementGoalRadii[unit] = reader.ReadSingle();
            units.MovementGoalTargetIds[unit] = reader.ReadInt32();
            units.MovementLegResults[unit] =
                (UnitMovementLegResult)reader.ReadByte();
            if (!Enum.IsDefined(units.MovementGoalKinds[unit]) ||
                !Enum.IsDefined(units.MovementLegResults[unit]) ||
                !float.IsFinite(units.MovementGoalRadii[unit]) ||
                units.MovementGoalRadii[unit] < 0f)
            {
                throw new InvalidDataException();
            }
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
            units.DynamicBlockageTicks[unit] = reader.ReadInt32();
            units.DynamicBlockageBestDistances[unit] = reader.ReadSingle();
            units.ReservationMigrationTicks[unit] = reader.ReadInt32();
            if (units.DynamicBlockageTicks[unit] < 0 ||
                !float.IsFinite(units.DynamicBlockageBestDistances[unit]) ||
                units.DynamicBlockageBestDistances[unit] < 0f ||
                units.ReservationMigrationTicks[unit] < 0)
            {
                throw new InvalidDataException();
            }
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
            writer.Write(combat.Armor[unit]);
            writer.Write((byte)combat.ArmorTypes[unit]);
            writer.Write(combat.ArmorUpgradeTechnologyIds[unit]);
            writer.Write(combat.ArmorUpgradePerLevel[unit]);
            writer.Write(combat.AttackHalfAngles[unit]);
            writer.Write((ushort)combat.Attributes[unit]);
            writer.Write(combat.AttacksPerVolley[unit]);
            writer.Write((ushort)combat.BonusVs[unit]);
            writer.Write(combat.BonusDamage[unit]);
            writer.Write(combat.BaseUpgradeDamage[unit]);
            writer.Write(combat.BonusUpgradeDamage[unit]);
            writer.Write(combat.ProjectileSpeed[unit]);
            writer.Write(combat.CanMoveDuringWindup[unit]);
            writer.Write(combat.CanMoveDuringCooldown[unit]);
            writer.Write(combat.AutoTargetPriority[unit]);
            writer.Write((byte)combat.AttackTypes[unit]);
            writer.Write(combat.DamageUpgradeTechnologyIds[unit]);
            writer.Write(combat.MinimumAttackRanges[unit]);
            WriteArea(writer, combat.WeaponAreas[unit]);
            WritePropagation(writer, combat.WeaponPropagations[unit]);
            writer.Write((byte)combat.ConcealmentKinds[unit]);
            writer.Write(combat.DetectionRanges[unit]);
            var capability = combat.ConcealmentCapabilities[unit];
            writer.Write((byte)capability.Kind);
            writer.Write(capability.ActivationSeconds);
            writer.Write(capability.DeactivationSeconds);
            writer.Write(capability.ConcealedVisionRange);
            writer.Write(capability.CanMoveWhileConcealed);
            writer.Write(capability.CanAttackWhileConcealed);
            writer.Write((byte)combat.ConcealmentPhases[unit]);
            writer.Write(combat.ConcealmentTransitionRemaining[unit]);
            writer.Write(combat.BaseVisionRanges[unit]);
            writer.Write(combat.VisionRanges[unit]);
            writer.Write(combat.ObservationHeights[unit]);
            writer.Write((byte)combat.TerrainVisionModes[unit]);
            writer.Write(combat.AttackRanges[unit]);
            writer.Write(combat.AcquisitionRanges[unit]);
            writer.Write(combat.AttackCooldownDurations[unit]);
            writer.Write(combat.AttackWindupDurations[unit]);
            writer.Write(combat.LeashDistances[unit]);
            writer.Write((byte)combat.PositioningKinds[unit]);
            writer.Write((byte)combat.CommandIntents[unit]);
            writer.Write((byte)combat.Phases[unit]);
            writer.Write(combat.TargetUnits[unit]);
            writer.Write(combat.TargetBuildings[unit]);
            writer.Write((byte)combat.TargetKinds[unit]);
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
            writer.Write(combat.TargetLockRemaining[unit]);
            writer.Write(combat.ActiveWeaponSlots[unit]);
            writer.Write(combat.AttackDamageMultipliers[unit]);
            writer.Write(combat.AttackDamageAdds[unit]);
            writer.Write(combat.AttackCooldownMultipliers[unit]);
            var weapons = combat.WeaponProfiles[unit];
            writer.Write(weapons.Length);
            foreach (var weapon in weapons)
                WriteWeaponProfile(writer, weapon);
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
            combat.Armor[unit] = reader.ReadSingle();
            combat.ArmorTypes[unit] = (CombatArmorType)reader.ReadByte();
            combat.ArmorUpgradeTechnologyIds[unit] = reader.ReadInt32();
            combat.ArmorUpgradePerLevel[unit] = reader.ReadSingle();
            combat.AttackHalfAngles[unit] = reader.ReadSingle();
            combat.Attributes[unit] = (CombatAttribute)reader.ReadUInt16();
            combat.AttacksPerVolley[unit] = reader.ReadInt32();
            combat.BonusVs[unit] = (CombatAttribute)reader.ReadUInt16();
            combat.BonusDamage[unit] = reader.ReadSingle();
            combat.BaseUpgradeDamage[unit] = reader.ReadSingle();
            combat.BonusUpgradeDamage[unit] = reader.ReadSingle();
            combat.ProjectileSpeed[unit] = reader.ReadSingle();
            combat.CanMoveDuringWindup[unit] = reader.ReadBoolean();
            combat.CanMoveDuringCooldown[unit] = reader.ReadBoolean();
            combat.AutoTargetPriority[unit] = reader.ReadInt32();
            combat.AttackTypes[unit] = (CombatAttackType)reader.ReadByte();
            combat.DamageUpgradeTechnologyIds[unit] = reader.ReadInt32();
            combat.MinimumAttackRanges[unit] = reader.ReadSingle();
            combat.WeaponAreas[unit] = ReadArea(reader);
            combat.WeaponPropagations[unit] = ReadPropagation(reader);
            combat.ConcealmentKinds[unit] =
                (UnitConcealmentKind)reader.ReadByte();
            combat.DetectionRanges[unit] = reader.ReadSingle();
            var capability = new UnitConcealmentCapabilitySnapshot(
                (UnitConcealmentKind)reader.ReadByte(),
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadBoolean(), reader.ReadBoolean());
            capability.Validate();
            combat.ConcealmentCapabilities[unit] = capability;
            combat.ConcealmentPhases[unit] =
                (UnitConcealmentPhase)reader.ReadByte();
            combat.ConcealmentTransitionRemaining[unit] = reader.ReadSingle();
            combat.BaseVisionRanges[unit] = reader.ReadSingle();
            combat.VisionRanges[unit] = reader.ReadSingle();
            combat.ObservationHeights[unit] = reader.ReadSingle();
            combat.TerrainVisionModes[unit] =
                (TerrainVisionMode)reader.ReadByte();
            combat.AttackRanges[unit] = reader.ReadSingle();
            combat.AcquisitionRanges[unit] = reader.ReadSingle();
            combat.AttackCooldownDurations[unit] = reader.ReadSingle();
            combat.AttackWindupDurations[unit] = reader.ReadSingle();
            combat.LeashDistances[unit] = reader.ReadSingle();
            combat.PositioningKinds[unit] = (CombatPositioningKind)reader.ReadByte();
            combat.CommandIntents[unit] = (UnitCommandIntent)reader.ReadByte();
            combat.Phases[unit] = (CombatPhase)reader.ReadByte();
            combat.TargetUnits[unit] = reader.ReadInt32();
            combat.TargetBuildings[unit] = reader.ReadInt32();
            combat.TargetKinds[unit] = (CombatTargetKind)reader.ReadByte();
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
            combat.TargetLockRemaining[unit] = reader.ReadSingle();
            combat.ActiveWeaponSlots[unit] = reader.ReadInt32();
            combat.AttackDamageMultipliers[unit] = reader.ReadSingle();
            combat.AttackDamageAdds[unit] = reader.ReadSingle();
            combat.AttackCooldownMultipliers[unit] = reader.ReadSingle();
            var weaponCount = reader.ReadInt32();
            if (weaponCount is < 1 or > 8) throw new InvalidDataException();
            var weapons = System.Collections.Immutable.ImmutableArray
                .CreateBuilder<CombatWeaponProfileSnapshot>(weaponCount);
            for (var index = 0; index < weaponCount; index++)
            {
                var weapon = ReadWeaponProfile(reader);
                weapon.Validate();
                weapons.Add(weapon);
            }
            combat.WeaponProfiles[unit] = weapons.MoveToImmutable();
            if (combat.AutoTargetPriority[unit] is < 0 or > 10 ||
                !Enum.IsDefined(combat.ArmorTypes[unit]) ||
                combat.ArmorUpgradeTechnologyIds[unit] < -1 ||
                !float.IsFinite(combat.ArmorUpgradePerLevel[unit]) ||
                combat.ArmorUpgradePerLevel[unit] < 0f ||
                combat.ArmorUpgradeTechnologyIds[unit] < 0 &&
                combat.ArmorUpgradePerLevel[unit] != 0f ||
                !float.IsFinite(combat.AttackHalfAngles[unit]) ||
                combat.AttackHalfAngles[unit] < 0f ||
                combat.AttackHalfAngles[unit] > MathF.PI ||
                !Enum.IsDefined(combat.AttackTypes[unit]) ||
                combat.DamageUpgradeTechnologyIds[unit] < -1 ||
                !float.IsFinite(combat.MinimumAttackRanges[unit]) ||
                combat.MinimumAttackRanges[unit] < 0f ||
                combat.MinimumAttackRanges[unit] >
                combat.AttackRanges[unit] ||
                !ValidArea(combat.WeaponAreas[unit]) ||
                !ValidPropagation(combat.WeaponPropagations[unit]) ||
                !Enum.IsDefined(combat.ConcealmentKinds[unit]) ||
                !Enum.IsDefined(combat.ConcealmentPhases[unit]) ||
                !float.IsFinite(combat.DetectionRanges[unit]) ||
                combat.DetectionRanges[unit] < 0f ||
                !float.IsFinite(combat.ConcealmentTransitionRemaining[unit]) ||
                combat.ConcealmentTransitionRemaining[unit] < 0f ||
                !float.IsFinite(combat.BaseVisionRanges[unit]) ||
                combat.BaseVisionRanges[unit] <= 0f ||
                !float.IsFinite(combat.VisionRanges[unit]) ||
                combat.VisionRanges[unit] <= 0f ||
                !float.IsFinite(combat.ObservationHeights[unit]) ||
                combat.ObservationHeights[unit] < 0f ||
                !Enum.IsDefined(combat.TerrainVisionModes[unit]) ||
                !ValidConcealmentState(combat, unit) ||
                !float.IsFinite(combat.TargetLockRemaining[unit]) ||
                combat.TargetLockRemaining[unit] < 0f ||
                combat.ActiveWeaponSlots[unit] is < 0 or > 7 ||
                !float.IsFinite(combat.AttackDamageMultipliers[unit]) ||
                combat.AttackDamageMultipliers[unit] < 0f ||
                !float.IsFinite(combat.AttackDamageAdds[unit]) ||
                !float.IsFinite(combat.AttackCooldownMultipliers[unit]) ||
                combat.AttackCooldownMultipliers[unit] <= 0f ||
                !combat.WeaponProfiles[unit].Any(value =>
                    value.Slot == combat.ActiveWeaponSlots[unit]))
                throw new InvalidDataException();
        }
        return combat;
    }

    private static void WriteWeaponProfile(
        BinaryWriter writer, in CombatWeaponProfileSnapshot weapon)
    {
        writer.Write(weapon.Slot);
        writer.Write((byte)weapon.TargetLayers);
        writer.Write(weapon.EnabledByDefault);
        writer.Write(weapon.RequiredTechnologyId);
        writer.Write(weapon.AttackDamage);
        writer.Write(weapon.AttackRange);
        writer.Write(weapon.AttackCooldownSeconds);
        writer.Write(weapon.AttackWindupSeconds);
        writer.Write((byte)weapon.Positioning);
        writer.Write(weapon.AttacksPerVolley);
        writer.Write((ushort)weapon.BonusVs);
        writer.Write(weapon.BonusDamage);
        writer.Write(weapon.BaseUpgradeDamage);
        writer.Write(weapon.BonusUpgradeDamage);
        writer.Write(weapon.ProjectileSpeed);
        writer.Write(weapon.CanMoveDuringWindup);
        writer.Write(weapon.CanMoveDuringCooldown);
        writer.Write((byte)weapon.AttackType);
        writer.Write(weapon.DamageUpgradeTechnologyId);
        writer.Write(weapon.MinimumRange);
        WriteArea(writer, weapon.Area);
        WritePropagation(writer, weapon.Propagation);
    }

    private static CombatWeaponProfileSnapshot ReadWeaponProfile(BinaryReader reader) =>
        new(
            reader.ReadInt32(), (CombatTargetLayer)reader.ReadByte(),
            reader.ReadBoolean(), reader.ReadInt32(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            (CombatPositioningKind)reader.ReadByte(), reader.ReadInt32(),
            (CombatAttribute)reader.ReadUInt16(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadBoolean(), reader.ReadBoolean(),
            (CombatAttackType)reader.ReadByte(), reader.ReadInt32(),
            reader.ReadSingle(), ReadArea(reader), ReadPropagation(reader));

    private static void WriteArea(
        BinaryWriter writer, in CombatWeaponAreaSnapshot area)
    {
        writer.Write(area.FullDamageRadius);
        writer.Write(area.HalfDamageRadius);
        writer.Write(area.QuarterDamageRadius);
        writer.Write((byte)area.TargetLayers);
    }

    private static CombatWeaponAreaSnapshot ReadArea(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            (CombatTargetLayer)reader.ReadByte());

    private static void WritePropagation(
        BinaryWriter writer,
        in CombatWeaponPropagationSnapshot value)
    {
        writer.Write((byte)value.Kind);
        writer.Write(value.LineDistance);
        writer.Write(value.Radius);
        writer.Write(value.DamageLossFactor);
        writer.Write(value.MaximumTargets);
        writer.Write((byte)value.TargetLayers);
        writer.Write(value.DistanceUpgradeTechnologyId);
        writer.Write(value.DistanceUpgradePerLevel);
    }

    private static CombatWeaponPropagationSnapshot ReadPropagation(
        BinaryReader reader) => new(
        (CombatWeaponPropagationKind)reader.ReadByte(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadInt32(),
        (CombatTargetLayer)reader.ReadByte(), reader.ReadInt32(),
        reader.ReadSingle());

    private static bool ValidArea(CombatWeaponAreaSnapshot area)
    {
        try
        {
            area.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool ValidPropagation(
        CombatWeaponPropagationSnapshot value)
    {
        try
        {
            value.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool ValidConcealmentState(CombatStore combat, int unit)
    {
        var capability = combat.ConcealmentCapabilities[unit];
        var phase = combat.ConcealmentPhases[unit];
        var kind = combat.ConcealmentKinds[unit];
        if (capability.Kind == UnitConcealmentKind.None)
        {
            return combat.ConcealmentTransitionRemaining[unit] == 0f &&
                   (phase == UnitConcealmentPhase.Visible ||
                    phase == UnitConcealmentPhase.Concealed) &&
                   (phase == UnitConcealmentPhase.Visible
                       ? kind == UnitConcealmentKind.None
                       : kind != UnitConcealmentKind.None);
        }

        var expectedKind = phase is UnitConcealmentPhase.Concealed or
            UnitConcealmentPhase.Deactivating
            ? capability.Kind
            : UnitConcealmentKind.None;
        var expectedVision = phase is UnitConcealmentPhase.Concealed or
            UnitConcealmentPhase.Deactivating
            ? capability.ConcealedVisionRange
            : combat.BaseVisionRanges[unit];
        var transitioning = phase is UnitConcealmentPhase.Activating or
            UnitConcealmentPhase.Deactivating;
        return kind == expectedKind &&
               combat.VisionRanges[unit] == expectedVision &&
               (transitioning
                   ? combat.ConcealmentTransitionRemaining[unit] > 0f
                   : combat.ConcealmentTransitionRemaining[unit] == 0f);
    }

    private static void WriteProjectiles(
        BinaryWriter writer,
        CombatProjectileRuntimeSnapshot snapshot)
    {
        writer.Write(snapshot.NextId);
        writer.Write(snapshot.Active.Length);
        foreach (var value in snapshot.Active)
        {
            writer.Write(value.Id);
            writer.Write(value.AttackerUnit);
            writer.Write((byte)value.TargetKind);
            writer.Write(value.TargetId);
            WriteVector(writer, value.Position);
            writer.Write(value.Speed);
            WriteWeapon(writer, value.Weapon);
        }
    }

    private static CombatProjectileRuntimeSnapshot ReadProjectiles(
        BinaryReader reader,
        int unitCount)
    {
        var nextId = reader.ReadInt32();
        var count = ReadCount(reader, CombatProjectileSystem.MaximumProjectiles);
        var active = new CombatProjectileSnapshot[count];
        var previous = 0;
        for (var index = 0; index < count; index++)
        {
            var value = new CombatProjectileSnapshot(
                reader.ReadInt32(), reader.ReadInt32(),
                (CombatTargetKind)reader.ReadByte(), reader.ReadInt32(),
                ReadVector(reader), reader.ReadSingle(), ReadWeapon(reader));
            if (value.Id <= previous || value.Id >= nextId ||
                (uint)value.AttackerUnit >= (uint)unitCount ||
                value.TargetKind is not (CombatTargetKind.Unit or
                    CombatTargetKind.Building) ||
                value.TargetId < 0 || value.Speed <= 0f ||
                !float.IsFinite(value.Speed) ||
                !CombatProjectileSystem.ValidWeapon(value.Weapon))
                throw new InvalidDataException();
            active[index] = value;
            previous = value.Id;
        }
        return new CombatProjectileRuntimeSnapshot(nextId, active);
    }

    private static void WriteWeapon(
        BinaryWriter writer,
        CombatWeaponDamageSnapshot value)
    {
        writer.Write(value.BaseDamage);
        writer.Write(value.AttacksPerVolley);
        writer.Write((ushort)value.BonusVs);
        writer.Write(value.BonusDamage);
        writer.Write(value.UpgradeLevel);
        writer.Write(value.BaseUpgradeDamage);
        writer.Write(value.BonusUpgradeDamage);
        writer.Write((byte)value.AttackType);
        WriteArea(writer, value.Area);
        WritePropagation(writer, value.Propagation);
    }

    private static CombatWeaponDamageSnapshot ReadWeapon(BinaryReader reader) => new(
        reader.ReadSingle(), reader.ReadInt32(),
        (CombatAttribute)reader.ReadUInt16(), reader.ReadSingle(),
        reader.ReadInt32(), reader.ReadSingle(), reader.ReadSingle(),
        (CombatAttackType)reader.ReadByte(), ReadArea(reader),
        ReadPropagation(reader));

    internal static void WriteEconomy(
        BinaryWriter writer,
        EconomyRuntimeSnapshot economy,
        int unitCount)
    {
        writer.Write(economy.Players.Length);
        foreach (var player in economy.Players)
        {
            writer.Write(player.PlayerId);
            writer.Write(player.Minerals);
            writer.Write(player.VespeneGas);
            writer.Write(player.SupplyUsed);
            writer.Write(player.SupplyCapacity);
        }
        writer.Write(economy.ResourceNodes.Length);
        foreach (var node in economy.ResourceNodes)
        {
            writer.Write(node.Id.Value);
            writer.Write((byte)node.Kind);
            WriteVector(writer, node.Position);
            writer.Write(node.Remaining);
            writer.Write(node.HarvestBatch);
            writer.Write(node.HarvestSeconds);
            writer.Write(node.NormalActiveSlots);
            writer.Write(node.IdealNormalAssignments);
            writer.Write(node.RequiresRefinery);
            writer.Write(node.Operational);
            writer.Write(node.ActiveNormal);
            writer.Write(node.AssignedNormal);
            writer.Write(node.WaitingNormal);
            writer.Write(node.ActiveMules);
            writer.Write(node.AssignedMules);
            WriteVector(writer, node.InteractionHalfExtents);
            writer.Write((byte)node.HarvestMode);
        }
        writer.Write(economy.DropOffs.Length);
        foreach (var dropOff in economy.DropOffs)
        {
            writer.Write(dropOff.Id.Value);
            writer.Write(dropOff.PlayerId);
            WriteVector(writer, dropOff.Position);
            WriteVector(writer, dropOff.HalfExtents);
            writer.Write(dropOff.ArrivalRadius);
            writer.Write(dropOff.AcceptsMinerals);
            writer.Write(dropOff.AcceptsVespene);
            writer.Write(dropOff.Operational);
        }
        writer.Write(economy.Bases.Length);
        foreach (var value in economy.Bases)
        {
            writer.Write(value.Id.Value);
            writer.Write(value.PlayerId);
            writer.Write(value.TownHall.Value);
            writer.Write(value.DropOff.Value);
            WriteVector(writer, value.Position);
            writer.Write(value.Operational);
        }
        if (economy.Workers.Length != unitCount)
        {
            throw new InvalidOperationException("Economy worker count mismatch.");
        }
        writer.Write(economy.Workers.Length);
        foreach (var worker in economy.Workers)
        {
            writer.Write(worker.UnitId);
            writer.Write(worker.Registered);
            writer.Write(worker.PlayerId);
            writer.Write((byte)worker.Capability);
            writer.Write((byte)worker.State);
            writer.Write(worker.TargetNodeId);
            writer.Write((byte)worker.CargoKind);
            writer.Write(worker.CargoAmount);
            writer.Write(worker.WorkRemaining);
        }
    }

    internal static EconomyRuntimeSnapshot ReadEconomy(
        BinaryReader reader,
        int unitCount)
    {
        var playerCount = ReadCount(reader, 16);
        var players = new PlayerEconomyRuntimeEntry[playerCount];
        var previousPlayer = -1;
        for (var index = 0; index < playerCount; index++)
        {
            var value = new PlayerEconomyRuntimeEntry(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32());
            if (value.PlayerId <= previousPlayer || value.Minerals < 0 ||
                value.VespeneGas < 0 || value.SupplyUsed < 0 ||
                value.SupplyCapacity < value.SupplyUsed)
            {
                throw new InvalidDataException();
            }
            players[index] = value;
            previousPlayer = value.PlayerId;
        }
        var nodeCount = ReadCount(reader, MaximumCapacity);
        var nodes = new EconomyResourceNodeRuntimeEntry[nodeCount];
        for (var index = 0; index < nodeCount; index++)
        {
            var value = new EconomyResourceNodeRuntimeEntry(
                new EconomyResourceNodeId(reader.ReadInt32()),
                (EconomyResourceKind)reader.ReadByte(),
                ReadVector(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadSingle(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                ReadVector(reader),
                (EconomyHarvestMode)reader.ReadByte());
            if (value.Id.Value != index || !Enum.IsDefined(value.Kind) ||
                !Finite(value.Position) || value.Remaining < 0 ||
                value.HarvestBatch <= 0 || !Positive(value.HarvestSeconds) ||
                value.NormalActiveSlots <= 0 ||
                value.IdealNormalAssignments <= 0 ||
                value.ActiveNormal < 0 ||
                value.ActiveNormal > value.NormalActiveSlots ||
                value.AssignedNormal < value.ActiveNormal ||
                value.WaitingNormal < 0 ||
                value.WaitingNormal > value.AssignedNormal ||
                value.ActiveMules < 0 || value.ActiveMules > 1 ||
                value.AssignedMules < value.ActiveMules ||
                !Finite(value.InteractionHalfExtents) ||
                value.InteractionHalfExtents.X < 0f ||
                value.InteractionHalfExtents.Y < 0f ||
                !Enum.IsDefined(value.HarvestMode))
            {
                throw new InvalidDataException();
            }
            nodes[index] = value;
        }
        var dropOffCount = ReadCount(reader, MaximumCapacity);
        var dropOffs = new EconomyDropOffRuntimeEntry[dropOffCount];
        for (var index = 0; index < dropOffCount; index++)
        {
            var value = new EconomyDropOffRuntimeEntry(
                new EconomyDropOffId(reader.ReadInt32()),
                reader.ReadInt32(), ReadVector(reader),
                ReadVector(reader),
                reader.ReadSingle(),
                reader.ReadBoolean(), reader.ReadBoolean(),
                reader.ReadBoolean());
            if (value.Id.Value != index || !Finite(value.Position) ||
                !Finite(value.HalfExtents) ||
                value.HalfExtents.X < 0f || value.HalfExtents.Y < 0f ||
                !Positive(value.ArrivalRadius) ||
                !players.Any(player => player.PlayerId == value.PlayerId) ||
                !value.AcceptsMinerals && !value.AcceptsVespene)
            {
                throw new InvalidDataException();
            }
            dropOffs[index] = value;
        }
        var baseCount = ReadCount(reader, MaximumCapacity);
        var bases = new EconomyBaseRuntimeEntry[baseCount];
        var baseTownHalls = new HashSet<int>();
        var baseDropOffs = new HashSet<int>();
        for (var index = 0; index < baseCount; index++)
        {
            var value = new EconomyBaseRuntimeEntry(
                new EconomyBaseId(reader.ReadInt32()),
                reader.ReadInt32(),
                new GameplayBuildingId(reader.ReadInt32()),
                new EconomyDropOffId(reader.ReadInt32()),
                ReadVector(reader),
                reader.ReadBoolean());
            if (value.Id.Value != index || value.TownHall.Value < 0 ||
                (uint)value.DropOff.Value >= (uint)dropOffs.Length ||
                dropOffs[value.DropOff.Value].PlayerId != value.PlayerId ||
                dropOffs[value.DropOff.Value].Operational != value.Operational ||
                !players.Any(player => player.PlayerId == value.PlayerId) ||
                !Finite(value.Position) ||
                !baseTownHalls.Add(value.TownHall.Value) ||
                !baseDropOffs.Add(value.DropOff.Value))
            {
                throw new InvalidDataException();
            }
            bases[index] = value;
        }
        var workerCount = ReadCount(reader, MaximumCapacity);
        if (workerCount != unitCount)
        {
            throw new InvalidDataException();
        }
        var workers = new WorkerEconomyRuntimeEntry[workerCount];
        var activeNormalCounts = new int[nodeCount];
        var assignedNormalCounts = new int[nodeCount];
        var waitingNormalCounts = new int[nodeCount];
        var activeMuleCounts = new int[nodeCount];
        var assignedMuleCounts = new int[nodeCount];
        for (var unit = 0; unit < workerCount; unit++)
        {
            var value = new WorkerEconomyRuntimeEntry(
                reader.ReadInt32(), reader.ReadBoolean(), reader.ReadInt32(),
                (GathererCapability)reader.ReadByte(),
                (WorkerEconomyState)reader.ReadByte(), reader.ReadInt32(),
                (EconomyResourceKind)reader.ReadByte(), reader.ReadInt32(),
                reader.ReadSingle());
            if (value.UnitId != unit || !Enum.IsDefined(value.State) ||
                !Enum.IsDefined(value.Capability) ||
                !value.Registered && value.Capability != GathererCapability.None ||
                value.Registered &&
                    value.Capability == GathererCapability.None &&
                    (value.State != WorkerEconomyState.Idle ||
                     value.TargetNodeId != -1) ||
                !Enum.IsDefined(value.CargoKind) || value.CargoAmount < 0 ||
                !float.IsFinite(value.WorkRemaining) || value.WorkRemaining < 0f ||
                value.Registered &&
                    !players.Any(player => player.PlayerId == value.PlayerId) ||
                value.TargetNodeId < -1 || value.TargetNodeId >= nodeCount)
            {
                throw new InvalidDataException();
            }
            if (value.State == WorkerEconomyState.Gathering)
            {
                if (value.TargetNodeId < 0)
                {
                    throw new InvalidDataException();
                }
                if (value.Capability == GathererCapability.Mule)
                    activeMuleCounts[value.TargetNodeId]++;
                else
                    activeNormalCounts[value.TargetNodeId]++;
            }
            if (value.Registered && value.TargetNodeId >= 0 &&
                value.State is not WorkerEconomyState.None and
                    not WorkerEconomyState.Idle)
            {
                if (value.Capability == GathererCapability.Mule)
                {
                    assignedMuleCounts[value.TargetNodeId]++;
                }
                else
                {
                    assignedNormalCounts[value.TargetNodeId]++;
                    if (value.State == WorkerEconomyState.WaitingForResource)
                        waitingNormalCounts[value.TargetNodeId]++;
                }
            }
            workers[unit] = value;
        }
        for (var node = 0; node < nodeCount; node++)
        {
            if (activeNormalCounts[node] != nodes[node].ActiveNormal ||
                assignedNormalCounts[node] != nodes[node].AssignedNormal ||
                waitingNormalCounts[node] != nodes[node].WaitingNormal ||
                activeMuleCounts[node] != nodes[node].ActiveMules ||
                assignedMuleCounts[node] != nodes[node].AssignedMules)
            {
                throw new InvalidDataException();
            }
        }
        return new EconomyRuntimeSnapshot(players, nodes, dropOffs, bases, workers);
    }

    internal static void WriteConstruction(
        BinaryWriter writer,
        ConstructionRuntimeSnapshot construction)
    {
        writer.Write(construction.Reservations.NextId);
        writer.Write(construction.Reservations.Entries.Length);
        foreach (var reservation in construction.Reservations.Entries)
        {
            writer.Write(reservation.Id.Value);
            writer.Write(reservation.BuildingId.Value);
            writer.Write(reservation.PlayerId);
            WriteRect(writer, reservation.Bounds);
            writer.Write(reservation.AcceptedTick);
        }
        writer.Write(construction.Buildings.Length);
        foreach (var value in construction.Buildings)
        {
            writer.Write(value.Id.Value);
            writer.Write(value.PlayerId);
            ConstructionSerialization.WriteProfile(writer, value.Type);
            WriteRect(writer, value.Bounds);
            writer.Write(value.ReservationId.Value);
            writer.Write(value.FootprintId.Value);
            writer.Write((byte)value.State);
            writer.Write(value.BuilderUnit);
            WriteVector(writer, value.AccessPoint);
            writer.Write(value.Progress);
            writer.Write(value.Health);
            writer.Write(value.RefineryNode.Value);
        }
    }

    internal static void WriteDiplomacy(
        BinaryWriter writer,
        PlayerDiplomacyRuntimeSnapshot diplomacy)
    {
        writer.Write(diplomacy.Players.Length);
        foreach (var entry in diplomacy.Players)
        {
            writer.Write(entry.PlayerId);
            writer.Write(entry.AllianceId);
            writer.Write(entry.SharedVision);
        }
    }

    internal static PlayerDiplomacyRuntimeSnapshot ReadDiplomacy(
        BinaryReader reader)
    {
        var count = ReadCount(reader, PlayerVisibilitySystem.MaximumPlayers - 1);
        var players = new PlayerDiplomacyRuntimeEntry[count];
        for (var index = 0; index < count; index++)
        {
            players[index] = new PlayerDiplomacyRuntimeEntry(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean());
        }
        var result = new PlayerDiplomacyRuntimeSnapshot(players);
        var validator = new PlayerDiplomacySystem();
        validator.RestoreRuntimeState(result);
        return result;
    }

    private static void WriteVisibility(
        BinaryWriter writer,
        PlayerVisibilityRuntimeSnapshot visibility)
    {
        writer.Write(visibility.CellSize);
        writer.Write(visibility.Columns);
        writer.Write(visibility.Rows);
        writer.Write(visibility.Players.Length);
        foreach (var player in visibility.Players)
        {
            writer.Write(player.PlayerId);
            writer.Write(player.ExploredCells.Length);
            writer.Write(player.ExploredCells);
        }
    }

    private static PlayerVisibilityRuntimeSnapshot ReadVisibility(
        BinaryReader reader)
    {
        var cellSize = reader.ReadSingle();
        var columns = reader.ReadInt32();
        var rows = reader.ReadInt32();
        if (!Positive(cellSize) || columns <= 0 || rows <= 0 ||
            columns > MaximumCapacity || rows > MaximumCapacity ||
            (long)columns * rows > MaximumPoints)
        {
            throw new InvalidDataException();
        }
        var packedLength = ((columns * rows) + 7) / 8;
        var count = ReadCount(reader, PlayerVisibilitySystem.MaximumPlayers);
        var players = new PlayerVisibilityRuntimeEntry[count];
        var previousPlayer = 0;
        for (var index = 0; index < count; index++)
        {
            var playerId = reader.ReadInt32();
            var length = reader.ReadInt32();
            if (playerId <= previousPlayer ||
                playerId >= PlayerVisibilitySystem.MaximumPlayers ||
                length != packedLength)
                throw new InvalidDataException();
            var cells = reader.ReadBytes(length);
            if (cells.Length != length)
                throw new EndOfStreamException();
            var unusedBits = length * 8 - columns * rows;
            if (unusedBits > 0 &&
                (cells[^1] & (0xFF << (8 - unusedBits))) != 0)
            {
                throw new InvalidDataException();
            }
            players[index] = new PlayerVisibilityRuntimeEntry(playerId, cells);
            previousPlayer = playerId;
        }
        return new PlayerVisibilityRuntimeSnapshot(
            cellSize, columns, rows, players);
    }

    internal static void WriteMatch(BinaryWriter writer, MatchRuntimeSnapshot match)
    {
        writer.Write((byte)match.Phase);
        writer.Write(match.StartedTick);
        writer.Write(match.CompletedTick);
        writer.Write(match.WinnerPlayerId);
        writer.Write(match.WinnerAllianceId);
        writer.Write(match.Players.Length);
        foreach (var player in match.Players)
        {
            writer.Write(player.PlayerId);
            writer.Write((byte)player.Status);
            writer.Write(player.EstablishedPresence);
            writer.Write(player.DefeatedTick);
        }
    }

    internal static MatchRuntimeSnapshot ReadMatch(
        BinaryReader reader,
        EconomyRuntimeSnapshot economy,
        PlayerDiplomacySystem diplomacy)
    {
        var phase = (MatchPhase)reader.ReadByte();
        var startedTick = reader.ReadInt64();
        var completedTick = reader.ReadInt64();
        var winner = reader.ReadInt32();
        var winnerAlliance = reader.ReadInt32();
        var count = ReadCount(reader, PlayerVisibilitySystem.MaximumPlayers);
        var players = new MatchPlayerRuntimeEntry[count];
        var previousPlayer = 0;
        var active = 0;
        var victorious = 0;
        for (var index = 0; index < count; index++)
        {
            var value = new MatchPlayerRuntimeEntry(
                reader.ReadInt32(),
                (MatchPlayerStatus)reader.ReadByte(),
                reader.ReadBoolean(),
                reader.ReadInt64());
            if (value.PlayerId <= previousPlayer ||
                value.PlayerId >= PlayerVisibilitySystem.MaximumPlayers ||
                !economy.Players.Any(player =>
                    player.PlayerId == value.PlayerId) ||
                !Enum.IsDefined(value.Status) || value.DefeatedTick < -1 ||
                value.Status == MatchPlayerStatus.Active &&
                    value.DefeatedTick != -1 ||
                value.Status == MatchPlayerStatus.Victorious &&
                    diplomacy.AllianceIdFor(value.PlayerId) != winnerAlliance ||
                value.Status == MatchPlayerStatus.Defeated &&
                    (!value.EstablishedPresence || value.DefeatedTick < 0))
            {
                throw new InvalidDataException();
            }
            players[index] = value;
            active += value.Status == MatchPlayerStatus.Active ? 1 : 0;
            victorious += value.Status == MatchPlayerStatus.Victorious ? 1 : 0;
            previousPlayer = value.PlayerId;
        }
        if (!Enum.IsDefined(phase) || startedTick < -1 || completedTick < -1 ||
            winner < -1 || winnerAlliance < -1 ||
            phase == MatchPhase.Setup &&
                (startedTick != -1 || completedTick != -1 || winner != -1 ||
                 winnerAlliance != -1 ||
                 players.Length != 0) ||
            phase == MatchPhase.Running &&
                (startedTick != 0 || completedTick != -1 || winner != -1 ||
                 winnerAlliance != -1 ||
                 players.Length < 2 || active == 0 || victorious != 0) ||
            phase == MatchPhase.Completed &&
                (startedTick != 0 || completedTick < 0 || players.Length < 2 ||
                 active != 0) ||
            winnerAlliance >= 0 && victorious == 0 ||
            winnerAlliance < 0 && victorious != 0 ||
            winner >= 0 && victorious != 1 ||
            winner >= 0 && !players.Any(value =>
                value.PlayerId == winner &&
                value.Status == MatchPlayerStatus.Victorious) ||
            winner < 0 && victorious == 1)
            throw new InvalidDataException();
        return new MatchRuntimeSnapshot(
            phase, startedTick, completedTick, winner, players,
            winnerAlliance);
    }

    internal static ConstructionRuntimeSnapshot ReadConstruction(
        BinaryReader reader,
        int unitCount,
        DynamicFootprint[] footprints,
        EconomyRuntimeSnapshot economy)
    {
        var nextReservationId = reader.ReadInt32();
        var reservationCount = ReadCount(reader, MaximumCapacity);
        if (nextReservationId <= 0)
            throw new InvalidDataException();
        var reservations = new ConstructionReservationEntry[reservationCount];
        var reservationIds = new HashSet<int>();
        var reservationBuildings = new HashSet<int>();
        var maximumReservationId = 0;
        for (var index = 0; index < reservationCount; index++)
        {
            var value = new ConstructionReservationEntry(
                new ConstructionReservationId(reader.ReadInt32()),
                new GameplayBuildingId(reader.ReadInt32()),
                reader.ReadInt32(),
                ReadRect(reader),
                reader.ReadInt64());
            if (!value.Id.IsValid || !reservationIds.Add(value.Id.Value) ||
                value.BuildingId.Value < 0 ||
                !reservationBuildings.Add(value.BuildingId.Value) ||
                !economy.Players.Any(player =>
                    player.PlayerId == value.PlayerId) ||
                !Finite(value.Bounds.Min) || !Finite(value.Bounds.Max) ||
                value.Bounds.Width <= 0f || value.Bounds.Height <= 0f ||
                value.AcceptedTick < 0)
                throw new InvalidDataException();
            maximumReservationId = Math.Max(
                maximumReservationId, value.Id.Value);
            reservations[index] = value;
        }
        if (nextReservationId <= maximumReservationId)
            throw new InvalidDataException();
        var reservationsById = reservations.ToDictionary(
            value => value.Id.Value);
        var count = ReadCount(reader, MaximumCapacity);
        var buildings = new ConstructionRuntimeEntry[count];
        var boundNodes = new HashSet<int>();
        var matchedReservations = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var value = new ConstructionRuntimeEntry(
                new GameplayBuildingId(reader.ReadInt32()),
                reader.ReadInt32(),
                ConstructionSerialization.ReadProfile(reader),
                ReadRect(reader),
                new ConstructionReservationId(reader.ReadInt32()),
                new DynamicFootprintId(reader.ReadInt32()),
                (BuildingLifecycleState)reader.ReadByte(),
                reader.ReadInt32(),
                ReadVector(reader),
                reader.ReadSingle(),
                reader.ReadSingle(),
                new EconomyResourceNodeId(reader.ReadInt32()));
            var terminal = value.State is
                BuildingLifecycleState.Canceled or BuildingLifecycleState.Destroyed;
            var activeProgress = value.State is
                BuildingLifecycleState.ReservedApproach or
                BuildingLifecycleState.BlockedAtStart or
                BuildingLifecycleState.Approaching or
                BuildingLifecycleState.Constructing or
                BuildingLifecycleState.WaitingForBuilder;
            var continuousBuilderRequired =
                value.Type.ConstructionMethod ==
                    ConstructionMethodKind.ContinuousWorker &&
                value.State is BuildingLifecycleState.ReservedApproach or
                    BuildingLifecycleState.BlockedAtStart or
                    BuildingLifecycleState.Approaching or
                    BuildingLifecycleState.Constructing;
            var footprint = footprints.FirstOrDefault(item =>
                item.Id == value.FootprintId);
            var hasFootprint = footprint.Id.Value > 0;
            var reservation = default(ConstructionReservationEntry);
            var hasReservation = value.ReservationId.IsValid &&
                reservationsById.TryGetValue(
                    value.ReservationId.Value, out reservation);
            var reservedState = value.State is
                BuildingLifecycleState.ReservedApproach or
                BuildingLifecycleState.BlockedAtStart;
            var hardState = value.State is
                BuildingLifecycleState.Approaching or
                BuildingLifecycleState.Constructing or
                BuildingLifecycleState.Completed;
            if (value.Id.Value != index || value.PlayerId < 0 ||
                !economy.Players.Any(player => player.PlayerId == value.PlayerId) ||
                !ConstructionSerialization.ValidProfile(value.Type) ||
                !Enum.IsDefined(value.State) || !Finite(value.Bounds.Min) ||
                !Finite(value.Bounds.Max) || value.Bounds.Width <= 0f ||
                value.Bounds.Height <= 0f ||
                value.FootprintId.Value < 0 ||
                value.ReservationId.Value < 0 ||
                value.BuilderUnit < -1 || value.BuilderUnit >= unitCount ||
                !Finite(value.AccessPoint) ||
                !float.IsFinite(value.Progress) || value.Progress is < 0f or > 1f ||
                !float.IsFinite(value.Health) || value.Health < 0f ||
                value.Health > value.Type.MaximumHealth ||
                value.State == BuildingLifecycleState.Completed &&
                    value.Progress != 1f ||
                activeProgress && value.Progress >= 1f ||
                continuousBuilderRequired && value.BuilderUnit < 0 ||
                terminal && (hasFootprint || hasReservation) ||
                !terminal && hasFootprint == hasReservation ||
                reservedState && !hasReservation ||
                hardState && !hasFootprint ||
                hasFootprint && footprint.Bounds != value.Bounds ||
                value.ReservationId.IsValid && !hasReservation ||
                hasReservation &&
                    (reservation.BuildingId != value.Id ||
                     reservation.PlayerId != value.PlayerId ||
                     reservation.Bounds != value.Bounds ||
                     !matchedReservations.Add(value.ReservationId.Value)) ||
                value.RefineryNode.Value < -1 ||
                value.RefineryNode.Value >= economy.ResourceNodes.Length ||
                value.Type.RequiresVespeneNode != (value.RefineryNode.Value >= 0) ||
                value.RefineryNode.Value >= 0 &&
                    (economy.ResourceNodes[value.RefineryNode.Value].Kind !=
                         EconomyResourceKind.VespeneGas ||
                     !economy.ResourceNodes[value.RefineryNode.Value]
                         .RequiresRefinery) ||
                value.RefineryNode.Value >= 0 &&
                    !boundNodes.Add(value.RefineryNode.Value))
            {
                throw new InvalidDataException();
            }
            buildings[index] = value;
        }
        if (matchedReservations.Count != reservations.Length)
            throw new InvalidDataException();
        foreach (var economyBase in economy.Bases)
        {
            if ((uint)economyBase.TownHall.Value >= (uint)buildings.Length)
                throw new InvalidDataException();
            var building = buildings[economyBase.TownHall.Value];
            var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
            if (building.PlayerId != economyBase.PlayerId ||
                building.Type.Function != BuildingFunctionKind.TownHall ||
                center != economyBase.Position ||
                economy.DropOffs[economyBase.DropOff.Value].Operational !=
                    economyBase.Operational ||
                economyBase.Operational !=
                    (building.State == BuildingLifecycleState.Completed))
            {
                throw new InvalidDataException();
            }
        }
        for (var index = 0; index < buildings.Length; index++)
        {
            if (buildings[index].Type.Function == BuildingFunctionKind.TownHall &&
                buildings[index].State == BuildingLifecycleState.Completed &&
                economy.Bases.Count(value => value.TownHall.Value == index) != 1)
            {
                throw new InvalidDataException();
            }
        }
        return new ConstructionRuntimeSnapshot(
            buildings,
            new ConstructionReservationRuntimeSnapshot(
                nextReservationId, reservations));
    }

    internal static void WriteBuildingUpgrades(
        BinaryWriter writer,
        BuildingUpgradeRuntimeSnapshot upgrades)
    {
        writer.Write(upgrades.NextOrderId);
        writer.Write(upgrades.CatalogProfiles.Length);
        foreach (var profile in upgrades.CatalogProfiles)
            BuildingUpgradeSerialization.WriteProfile(writer, profile);
        writer.Write(upgrades.Orders.Length);
        foreach (var order in upgrades.Orders)
        {
            writer.Write(order.Id.Value);
            writer.Write(order.Building.Value);
            writer.Write(order.PlayerId);
            BuildingUpgradeSerialization.WriteProfile(writer, order.Profile);
            writer.Write(order.Progress);
        }
    }

    internal static BuildingUpgradeRuntimeSnapshot ReadBuildingUpgrades(
        BinaryReader reader,
        ConstructionRuntimeSnapshot construction,
        EconomyRuntimeSnapshot economy)
    {
        var nextOrderId = reader.ReadInt32();
        var profileCount = ReadCount(reader, MaximumCapacity);
        var profiles = new BuildingUpgradeProfile[profileCount];
        for (var index = 0; index < profiles.Length; index++)
            profiles[index] = BuildingUpgradeSerialization.ReadProfile(reader);
        if (nextOrderId <= 0 ||
            !BuildingUpgradeCatalogSnapshot.TryCreate(
                BuildingUpgradeCatalogSnapshot.CurrentFormatVersion,
                profiles,
                out var catalog,
                out _) || catalog is null)
            throw new InvalidDataException();

        var count = ReadCount(reader, MaximumCapacity);
        var orders = new BuildingUpgradeOrderRuntimeEntry[count];
        var ids = new HashSet<int>();
        var buildings = new HashSet<int>();
        var maximumId = 0;
        for (var index = 0; index < orders.Length; index++)
        {
            var value = new BuildingUpgradeOrderRuntimeEntry(
                new BuildingUpgradeOrderId(reader.ReadInt32()),
                new GameplayBuildingId(reader.ReadInt32()),
                reader.ReadInt32(),
                BuildingUpgradeSerialization.ReadProfile(reader),
                reader.ReadSingle());
            if (value.Id.Value <= 0 || !ids.Add(value.Id.Value) ||
                !buildings.Add(value.Building.Value) ||
                (uint)value.Building.Value >=
                    (uint)construction.Buildings.Length ||
                !economy.Players.Any(player =>
                    player.PlayerId == value.PlayerId) ||
                construction.Buildings[value.Building.Value].PlayerId !=
                    value.PlayerId ||
                construction.Buildings[value.Building.Value].State !=
                    BuildingLifecycleState.Completed ||
                construction.Buildings[value.Building.Value].Type.Id !=
                    value.Profile.SourceBuildingTypeId ||
                !catalog.TryForSource(
                    value.Profile.SourceBuildingTypeId, out var canonical) ||
                !BuildingUpgradeCatalogSnapshot.ProfileEquals(
                    value.Profile, canonical) ||
                !float.IsFinite(value.Progress) ||
                value.Progress is < 0f or >= 1f)
                throw new InvalidDataException();
            maximumId = Math.Max(maximumId, value.Id.Value);
            orders[index] = value;
        }
        if (nextOrderId <= maximumId) throw new InvalidDataException();
        return new BuildingUpgradeRuntimeSnapshot(
            nextOrderId, profiles, orders);
    }

    internal static void WriteProduction(
        BinaryWriter writer,
        ProductionRuntimeSnapshot production)
    {
        writer.Write(production.NextOrderId);
        writer.Write(production.Queues.Length);
        foreach (var queue in production.Queues)
        {
            writer.Write(queue.Producer.Value);
            ProductionCommandLogSnapshot.WriteRally(writer, queue.Rally);
            writer.Write(queue.Orders.Length);
            foreach (var order in queue.Orders)
            {
                writer.Write(order.Id.Value);
                writer.Write(order.PlayerId);
                ProductionSerialization.WriteRecipe(writer, order.Recipe);
                writer.Write((byte)order.State);
                writer.Write(order.Progress);
            }
        }
        writer.Write(production.ProducedUnits.Length);
        foreach (var unit in production.ProducedUnits)
        {
            writer.Write(unit.Unit);
            writer.Write(unit.PlayerId);
            writer.Write(unit.Supply);
        }
    }

    internal static ProductionRuntimeSnapshot ReadProduction(
        BinaryReader reader,
        int unitCount,
        ConstructionRuntimeSnapshot construction,
        EconomyRuntimeSnapshot economy)
    {
        var nextOrder = reader.ReadInt32();
        var queueCount = ReadCount(reader, MaximumCapacity);
        if (nextOrder <= 0) throw new InvalidDataException();
        var queues = new ProducerQueueRuntimeEntry[queueCount];
        var producers = new HashSet<int>();
        var orderIds = new HashSet<int>();
        var reservedSupply = new Dictionary<int, int>();
        var maximumOrder = 0;
        for (var index = 0; index < queueCount; index++)
        {
            var producer = new GameplayBuildingId(reader.ReadInt32());
            var rally = ProductionCommandLogSnapshot.ReadRally(reader);
            if (!ProductionSystem.ValidRallyTarget(rally) ||
                rally.Kind == RallyTargetKind.ResourceNode &&
                    (rally.ResourceNode.Value >= economy.ResourceNodes.Length ||
                     economy.ResourceNodes[rally.ResourceNode.Value].Position !=
                         rally.Position) ||
                rally.Kind == RallyTargetKind.FriendlyUnit &&
                    rally.Unit >= unitCount)
                throw new InvalidDataException("Invalid production rally target.");
            var orderCount = ReadCount(reader, ProductionSystem.MaximumQueueLength);
            var building = producer.Value >= 0 &&
                           producer.Value < construction.Buildings.Length
                ? construction.Buildings[producer.Value]
                : default;
            if (!producers.Add(producer.Value) || building.Id != producer ||
                building.State is BuildingLifecycleState.Canceled or
                    BuildingLifecycleState.Destroyed)
                throw new InvalidDataException();
            if (orderCount > 0 && building.State != BuildingLifecycleState.Completed)
                throw new InvalidDataException();
            var orders = new ProductionOrderRuntimeEntry[orderCount];
            for (var orderIndex = 0; orderIndex < orderCount; orderIndex++)
            {
                var id = new ProductionOrderId(reader.ReadInt32());
                var player = reader.ReadInt32();
                var recipe = ProductionSerialization.ReadRecipe(reader);
                var state = (ProductionOrderState)reader.ReadByte();
                var progress = reader.ReadSingle();
                if (id.Value <= 0 || !orderIds.Add(id.Value) ||
                    !economy.Players.Any(value => value.PlayerId == player) ||
                    building.PlayerId != player ||
                    !ProductionSystem.ValidRecipeProfile(recipe) ||
                    building.Type.Id != recipe.ProducerBuildingTypeId ||
                    !Enum.IsDefined(state) || !float.IsFinite(progress) ||
                    progress is < 0f or > 1f ||
                    orderIndex > 0 && state != ProductionOrderState.Queued ||
                    state == ProductionOrderState.WaitingForExit && progress != 1f)
                    throw new InvalidDataException();
                orders[orderIndex] = new ProductionOrderRuntimeEntry(
                    id, player, recipe, state, progress);
                maximumOrder = Math.Max(maximumOrder, id.Value);
                reservedSupply[player] = reservedSupply.GetValueOrDefault(player) +
                                         recipe.Cost.Supply;
            }
            queues[index] = new ProducerQueueRuntimeEntry(producer, rally, orders);
        }
        if (nextOrder <= maximumOrder) throw new InvalidDataException();
        var producedCount = ReadCount(reader, MaximumCapacity);
        var produced = new ProducedUnitPopulationRuntimeEntry[producedCount];
        var producedIds = new HashSet<int>();
        for (var index = 0; index < producedCount; index++)
        {
            var value = new ProducedUnitPopulationRuntimeEntry(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            if ((uint)value.Unit >= (uint)unitCount ||
                !producedIds.Add(value.Unit) || value.Supply <= 0 ||
                !economy.Players.Any(player => player.PlayerId == value.PlayerId))
                throw new InvalidDataException();
            produced[index] = value;
            reservedSupply[value.PlayerId] =
                reservedSupply.GetValueOrDefault(value.PlayerId) + value.Supply;
        }
        foreach (var value in reservedSupply)
        {
            var player = economy.Players.First(item => item.PlayerId == value.Key);
            if (value.Value > player.SupplyUsed) throw new InvalidDataException();
        }
        return new ProductionRuntimeSnapshot(nextOrder, queues, produced);
    }

    internal static void WriteTechnology(
        BinaryWriter writer,
        TechnologyRuntimeSnapshot technology)
    {
        writer.Write(technology.NextOrderId);
        writer.Write(technology.Levels.Length);
        foreach (var value in technology.Levels)
        {
            writer.Write(value.PlayerId);
            TechnologySerialization.WriteProfile(writer, value.Technology);
            writer.Write(value.Level);
        }
        writer.Write(technology.Queues.Length);
        foreach (var queue in technology.Queues)
        {
            writer.Write(queue.Researcher.Value);
            writer.Write(queue.Orders.Length);
            foreach (var order in queue.Orders)
            {
                writer.Write(order.Id.Value);
                writer.Write(order.PlayerId);
                TechnologySerialization.WriteProfile(writer, order.Technology);
                writer.Write(order.Progress);
            }
        }
    }

    internal static TechnologyRuntimeSnapshot ReadTechnology(
        BinaryReader reader,
        ConstructionRuntimeSnapshot construction,
        EconomyRuntimeSnapshot economy)
    {
        var nextOrderId = reader.ReadInt32();
        if (nextOrderId <= 0) throw new InvalidDataException();
        var levelCount = ReadCount(reader, MaximumCapacity);
        var levels = new TechnologyLevelRuntimeEntry[levelCount];
        var levelKeys = new HashSet<(int, int)>();
        for (var index = 0; index < levelCount; index++)
        {
            var player = reader.ReadInt32();
            var technology = TechnologySerialization.ReadProfile(reader);
            var level = reader.ReadInt32();
            if (!economy.Players.Any(value => value.PlayerId == player) ||
                !TechnologyCatalogSnapshot.ValidProfile(technology) ||
                level <= 0 || level > technology.MaximumLevel ||
                !levelKeys.Add((player, technology.Id)))
                throw new InvalidDataException();
            levels[index] = new TechnologyLevelRuntimeEntry(
                player, technology, level);
        }
        var queueCount = ReadCount(reader, MaximumCapacity);
        var queues = new ResearchQueueRuntimeEntry[queueCount];
        var researchers = new HashSet<int>();
        var orderIds = new HashSet<int>();
        var maximumOrderId = 0;
        for (var index = 0; index < queueCount; index++)
        {
            var researcher = new GameplayBuildingId(reader.ReadInt32());
            var orderCount = ReadCount(reader, TechnologySystem.MaximumQueueLength);
            var building = researcher.Value >= 0 &&
                           researcher.Value < construction.Buildings.Length
                ? construction.Buildings[researcher.Value]
                : default;
            if (!researchers.Add(researcher.Value) || building.Id != researcher ||
                building.State != BuildingLifecycleState.Completed)
                throw new InvalidDataException();
            var orders = new ResearchOrderRuntimeEntry[orderCount];
            for (var orderIndex = 0; orderIndex < orderCount; orderIndex++)
            {
                var id = new ResearchOrderId(reader.ReadInt32());
                var player = reader.ReadInt32();
                var technology = TechnologySerialization.ReadProfile(reader);
                var progress = reader.ReadSingle();
                if (id.Value <= 0 || !orderIds.Add(id.Value) ||
                    !economy.Players.Any(value => value.PlayerId == player) ||
                    building.PlayerId != player ||
                    building.Type.Id != technology.ResearcherBuildingTypeId ||
                    !TechnologyCatalogSnapshot.ValidProfile(technology) ||
                    !float.IsFinite(progress) || progress is < 0f or >= 1f)
                    throw new InvalidDataException();
                orders[orderIndex] = new ResearchOrderRuntimeEntry(
                    id, player, technology, progress);
                maximumOrderId = Math.Max(maximumOrderId, id.Value);
            }
            queues[index] = new ResearchQueueRuntimeEntry(researcher, orders);
        }
        if (nextOrderId <= maximumOrderId) throw new InvalidDataException();
        return new TechnologyRuntimeSnapshot(nextOrderId, levels, queues);
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
            writer.Write(queues.ActiveTargetBuildings[unit]);
            writer.Write(queues.ActiveTargetResourceNodes[unit]);
            writer.Write(queues.ActiveSequenceIds[unit]);
            writer.Write(queues.ActiveOrdersWereQueued[unit]);
            writer.Write(queues.CompletedQueuedOrders[unit]);
            writer.Write(queues.QueueOverflowCounts[unit]);
            writer.Write(queues.ConstructionEvacuationActive[unit]);
            writer.Write(queues.ConstructionEvacuationBuildings[unit]);
            WriteVector(writer, queues.ConstructionEvacuationTargets[unit]);
            WriteRect(writer, queues.ConstructionEvacuationFootprints[unit]);
            writer.Write(queues.ProductionEvacuationActive[unit]);
            WriteVector(writer, queues.ProductionEvacuationTargets[unit]);
            WriteRect(writer, queues.ProductionEvacuationFootprints[unit]);
            writer.Write(queues.PendingCounts[unit]);
            for (var pending = 0; pending < queues.PendingCounts[unit]; pending++)
            {
                var order = queues.PendingAt(unit, pending);
                writer.Write((byte)order.Kind);
                WriteVector(writer, order.TargetPosition);
                writer.Write(order.TargetUnit);
                writer.Write(order.TargetBuilding);
                writer.Write(order.TargetResourceNode);
                writer.Write(order.SequenceId);
            }
        }
    }

    private static void ValidateCombatTargets(
        CombatStore combat,
        int unitCount,
        ConstructionRuntimeSnapshot construction)
    {
        for (var unit = 0; unit < unitCount; unit++)
        {
            var kind = combat.TargetKinds[unit];
            var unitTarget = combat.TargetUnits[unit];
            var buildingTarget = combat.TargetBuildings[unit];
            if (!Enum.IsDefined(kind) ||
                kind == CombatTargetKind.None &&
                    (unitTarget != -1 || buildingTarget != -1) ||
                kind == CombatTargetKind.Unit &&
                    ((uint)unitTarget >= (uint)unitCount || buildingTarget != -1) ||
                kind == CombatTargetKind.Building &&
                    (unitTarget != -1 ||
                     (uint)buildingTarget >= (uint)construction.Buildings.Length))
            {
                throw new InvalidDataException();
            }
        }
    }

    private static UnitCommandQueueStore ReadQueues(
        BinaryReader reader, int capacity, int count, int buildingCount)
    {
        var queues = new UnitCommandQueueStore(capacity);
        for (var unit = 0; unit < count; unit++)
        {
            queues.HasActiveOrders[unit] = reader.ReadBoolean();
            queues.ActiveKinds[unit] = (UnitOrderKind)reader.ReadByte();
            queues.ActivePositions[unit] = ReadVector(reader);
            queues.ActiveTargetUnits[unit] = reader.ReadInt32();
            queues.ActiveTargetBuildings[unit] = reader.ReadInt32();
            queues.ActiveTargetResourceNodes[unit] = reader.ReadInt32();
            queues.ActiveSequenceIds[unit] = reader.ReadInt32();
            queues.ActiveOrdersWereQueued[unit] = reader.ReadBoolean();
            queues.CompletedQueuedOrders[unit] = reader.ReadInt32();
            queues.QueueOverflowCounts[unit] = reader.ReadInt32();
            queues.ConstructionEvacuationActive[unit] = reader.ReadBoolean();
            queues.ConstructionEvacuationBuildings[unit] = reader.ReadInt32();
            queues.ConstructionEvacuationTargets[unit] = ReadVector(reader);
            queues.ConstructionEvacuationFootprints[unit] = ReadRect(reader);
            queues.ProductionEvacuationActive[unit] = reader.ReadBoolean();
            queues.ProductionEvacuationTargets[unit] = ReadVector(reader);
            queues.ProductionEvacuationFootprints[unit] = ReadRect(reader);
            var evacuationActive =
                queues.ConstructionEvacuationActive[unit];
            var evacuationFootprint =
                queues.ConstructionEvacuationFootprints[unit];
            if (evacuationActive
                    ? (uint)queues.ConstructionEvacuationBuildings[unit] >=
                          (uint)buildingCount ||
                      !Finite(queues.ConstructionEvacuationTargets[unit]) ||
                      !Finite(evacuationFootprint.Min) ||
                      !Finite(evacuationFootprint.Max) ||
                      evacuationFootprint.Width <= 0f ||
                      evacuationFootprint.Height <= 0f
                    : queues.ConstructionEvacuationBuildings[unit] != -1 ||
                      queues.ConstructionEvacuationTargets[unit] != Vector2.Zero ||
                      evacuationFootprint != default)
            {
                throw new InvalidDataException();
            }
            var productionEvacuationFootprint =
                queues.ProductionEvacuationFootprints[unit];
            if (queues.ProductionEvacuationActive[unit]
                    ? !Finite(queues.ProductionEvacuationTargets[unit]) ||
                      !Finite(productionEvacuationFootprint.Min) ||
                      !Finite(productionEvacuationFootprint.Max) ||
                      productionEvacuationFootprint.Width <= 0f ||
                      productionEvacuationFootprint.Height <= 0f
                    : queues.ProductionEvacuationTargets[unit] != Vector2.Zero ||
                      productionEvacuationFootprint != default)
            {
                throw new InvalidDataException();
            }
            if (queues.HasActiveOrders[unit] &&
                !UnitOrderContract.IsStructurallyValid(new UnitOrder(
                    queues.ActiveKinds[unit],
                    queues.ActivePositions[unit],
                    queues.ActiveTargetUnits[unit],
                    queues.ActiveTargetBuildings[unit],
                    queues.ActiveTargetResourceNodes[unit],
                    queues.ActiveSequenceIds[unit])))
            {
                throw new InvalidDataException();
            }
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
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32());
                if (order.Kind == UnitOrderKind.FollowFriendly ||
                    !UnitOrderContract.IsStructurallyValid(order) ||
                    !queues.TryEnqueue(unit, order))
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

    private static bool Finite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool Positive(float value) =>
        float.IsFinite(value) && value > 0f;
}
