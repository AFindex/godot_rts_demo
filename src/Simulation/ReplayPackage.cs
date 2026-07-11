using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct ReplayResourceManifest(
    int NavigationFormatVersion,
    ulong NavigationHash,
    int GameplayProfileFormatVersion,
    ulong GameplayProfileHash,
    int ClearanceBakeFormatVersion,
    ulong ClearanceBakeHash)
{
    public static ReplayResourceManifest Create(
        NavigationMapSnapshot navigation,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake) => new(
            navigation.FormatVersion,
            navigation.StableHash,
            gameplayProfiles.FormatVersion,
            gameplayProfiles.StableHash,
            clearanceBake?.FormatVersion ?? 0,
            clearanceBake?.StableHash ?? 0UL);
}

public readonly record struct ReplayInitialUnit(
    Vector2 Position,
    float Radius,
    float MaximumSpeed,
    float Acceleration,
    int Team,
    CombatProfileSnapshot CombatProfile);

public readonly record struct ReplayInitialBuilding(
    DynamicFootprintId Id,
    SimRect Bounds,
    int PlacedRevision);

public enum ReplayWorldCommandKind : byte
{
    PlaceBuilding = 1,
    RemoveBuilding = 2
}

public readonly record struct RecordedWorldCommand(
    long Tick,
    ReplayWorldCommandKind Kind,
    DynamicFootprintId FootprintId,
    SimRect Bounds);

public enum ReplayPackageValidationCode : byte
{
    Success,
    PayloadTooShort,
    InvalidMagic,
    UnsupportedVersion,
    InvalidManifest,
    InvalidCapacity,
    InvalidUnitManifest,
    InvalidBuildingManifest,
    InvalidEconomyManifest,
    InvalidConstructionManifest,
    InvalidProductionManifest,
    InvalidWorldCommand,
    InvalidEconomyCommandLog,
    InvalidConstructionCommandLog,
    InvalidProductionCommandLog,
    InvalidCommandLog,
    TrailingBytes,
    ResourceMismatch,
    InitialStateMismatch
}

public readonly record struct ReplayPackageValidationResult(
    ReplayPackageValidationCode Code,
    int ElementIndex = -1)
{
    public bool Succeeded => Code == ReplayPackageValidationCode.Success;
}

/// <summary>
/// A self-describing replay boundary: resource identities, initial simulation
/// manifest, dynamic world mutations and resolved unit commands.
/// </summary>
public sealed class SimulationReplayPackageSnapshot
{
    private const uint Magic = 0x4B505452; // RTPK in little-endian bytes.
    private const int MaximumElements = 1_000_000;
    public const int CurrentFormatVersion = 6;

    public SimulationReplayPackageSnapshot(
        int simulationCapacity,
        ulong initialStateHash,
        ReplayResourceManifest resources,
        ReplayInitialUnit[] units,
        ReplayInitialBuilding[] buildings,
        EconomyRuntimeSnapshot economy,
        ConstructionRuntimeSnapshot construction,
        ProductionRuntimeSnapshot production,
        RecordedWorldCommand[] worldCommands,
        EconomyCommandLogSnapshot economyCommandLog,
        ConstructionCommandLogSnapshot constructionCommandLog,
        ProductionCommandLogSnapshot productionCommandLog,
        SimulationCommandLogSnapshot commandLog)
    {
        SimulationCapacity = simulationCapacity;
        InitialStateHash = initialStateHash;
        Resources = resources;
        Units = units;
        Buildings = buildings;
        Economy = economy;
        Construction = construction;
        Production = production;
        WorldCommands = worldCommands;
        EconomyCommandLog = economyCommandLog;
        ConstructionCommandLog = constructionCommandLog;
        ProductionCommandLog = productionCommandLog;
        CommandLog = commandLog;
        CanonicalBytes = Serialize();
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public int FormatVersion => CurrentFormatVersion;
    public int SimulationCapacity { get; }
    public ulong InitialStateHash { get; }
    public ReplayResourceManifest Resources { get; }
    public ReplayInitialUnit[] Units { get; }
    public ReplayInitialBuilding[] Buildings { get; }
    public EconomyRuntimeSnapshot Economy { get; }
    public ConstructionRuntimeSnapshot Construction { get; }
    public ProductionRuntimeSnapshot Production { get; }
    public RecordedWorldCommand[] WorldCommands { get; }
    public EconomyCommandLogSnapshot EconomyCommandLog { get; }
    public ConstructionCommandLogSnapshot ConstructionCommandLog { get; }
    public ProductionCommandLogSnapshot ProductionCommandLog { get; }
    public SimulationCommandLogSnapshot CommandLog { get; }
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    public ReplayPackageValidationResult ValidateResources(
        NavigationMapSnapshot navigation,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        var actual = ReplayResourceManifest.Create(
            navigation, gameplayProfiles, clearanceBake);
        return actual == Resources
            ? new ReplayPackageValidationResult(ReplayPackageValidationCode.Success)
            : new ReplayPackageValidationResult(
                ReplayPackageValidationCode.ResourceMismatch);
    }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> payload,
        out SimulationReplayPackageSnapshot? snapshot,
        out ReplayPackageValidationResult validation)
    {
        snapshot = null;
        if (payload.Length < 64)
        {
            validation = new ReplayPackageValidationResult(
                ReplayPackageValidationCode.PayloadTooShort);
            return false;
        }

        try
        {
            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidMagic);
                return false;
            }
            if (reader.ReadInt32() != CurrentFormatVersion)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.UnsupportedVersion);
                return false;
            }

            var capacity = reader.ReadInt32();
            var initialStateHash = reader.ReadUInt64();
            var resources = ReadResources(reader);
            if (!ValidResources(resources))
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidManifest);
                return false;
            }
            if (capacity <= 0 || capacity > MaximumElements)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidCapacity);
                return false;
            }

            var unitCount = ReadCount(reader);
            if (unitCount < 0 || unitCount > capacity)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidUnitManifest);
                return false;
            }
            var units = new ReplayInitialUnit[unitCount];
            for (var index = 0; index < units.Length; index++)
            {
                units[index] = ReadUnit(reader);
                if (!ValidUnit(units[index]))
                {
                    validation = new ReplayPackageValidationResult(
                        ReplayPackageValidationCode.InvalidUnitManifest, index);
                    return false;
                }
            }

            var buildingCount = ReadCount(reader);
            if (buildingCount < 0)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidBuildingManifest);
                return false;
            }
            var buildings = new ReplayInitialBuilding[buildingCount];
            for (var index = 0; index < buildings.Length; index++)
            {
                buildings[index] = ReadBuilding(reader);
                if (buildings[index].Id.Value != index + 1 ||
                    buildings[index].PlacedRevision != index + 1 ||
                    !ValidRect(buildings[index].Bounds))
                {
                    validation = new ReplayPackageValidationResult(
                        ReplayPackageValidationCode.InvalidBuildingManifest, index);
                    return false;
                }
            }

            EconomyRuntimeSnapshot economy;
            try
            {
                economy = RuntimeHotSnapshotCodec.ReadEconomy(reader, unitCount);
            }
            catch (InvalidDataException)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidEconomyManifest);
                return false;
            }

            ConstructionRuntimeSnapshot construction;
            try
            {
                construction = RuntimeHotSnapshotCodec.ReadConstruction(
                    reader,
                    unitCount,
                    buildings.Select(value => new DynamicFootprint(
                        value.Id, value.Bounds, value.PlacedRevision)).ToArray(),
                    economy);
            }
            catch (InvalidDataException)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidConstructionManifest);
                return false;
            }
            ProductionRuntimeSnapshot production;
            try
            {
                production = RuntimeHotSnapshotCodec.ReadProduction(
                    reader, unitCount, construction, economy);
            }
            catch (InvalidDataException)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidProductionManifest);
                return false;
            }
            var worldCommandCount = ReadCount(reader);
            if (worldCommandCount < 0)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidWorldCommand);
                return false;
            }
            var worldCommands = new RecordedWorldCommand[worldCommandCount];
            var previousTick = -1L;
            for (var index = 0; index < worldCommands.Length; index++)
            {
                worldCommands[index] = ReadWorldCommand(reader);
                if (worldCommands[index].Tick < previousTick ||
                    worldCommands[index].Tick < 0 ||
                    !Enum.IsDefined(worldCommands[index].Kind) ||
                    worldCommands[index].FootprintId.Value <= 0 ||
                    !ValidRect(worldCommands[index].Bounds))
                {
                    validation = new ReplayPackageValidationResult(
                        ReplayPackageValidationCode.InvalidWorldCommand, index);
                    return false;
                }
                previousTick = worldCommands[index].Tick;
            }

            var economyLogBytes = ReadCount(reader);
            if (economyLogBytes < 0 ||
                economyLogBytes > stream.Length - stream.Position)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidEconomyCommandLog);
                return false;
            }
            var economyPayload = reader.ReadBytes(economyLogBytes);
            if (!EconomyCommandLogSnapshot.TryDeserialize(
                    economyPayload, out var economyLog, out _))
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidEconomyCommandLog);
                return false;
            }

            var constructionLogBytes = ReadCount(reader);
            if (constructionLogBytes < 0 ||
                constructionLogBytes > stream.Length - stream.Position)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidConstructionCommandLog);
                return false;
            }
            var constructionPayload = reader.ReadBytes(constructionLogBytes);
            if (!ConstructionCommandLogSnapshot.TryDeserialize(
                    constructionPayload, out var constructionLog, out _))
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidConstructionCommandLog);
                return false;
            }

            var productionLogBytes = ReadCount(reader);
            if (productionLogBytes < 0 ||
                productionLogBytes > stream.Length - stream.Position)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidProductionCommandLog);
                return false;
            }
            var productionPayload = reader.ReadBytes(productionLogBytes);
            if (!ProductionCommandLogSnapshot.TryDeserialize(
                    productionPayload, out var productionLog, out _))
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidProductionCommandLog);
                return false;
            }
            for (var index = 0; index < productionLog!.Entries.Length; index++)
            {
                var command = productionLog.Entries[index];
                if (command.Kind != ProductionReplayCommandKind.SetRallyPoint)
                    continue;
                if (command.Rally.Kind == RallyTargetKind.ResourceNode &&
                        command.Rally.ResourceNode.Value >=
                            economy.ResourceNodes.Length ||
                    command.Rally.Kind == RallyTargetKind.FriendlyUnit &&
                        command.Rally.Unit >= capacity)
                {
                    validation = new ReplayPackageValidationResult(
                        ReplayPackageValidationCode.InvalidProductionCommandLog,
                        index);
                    return false;
                }
            }

            var commandLogBytes = ReadCount(reader);
            if (commandLogBytes < 0 || commandLogBytes > stream.Length - stream.Position)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidCommandLog);
                return false;
            }
            var commandPayload = reader.ReadBytes(commandLogBytes);
            if (!SimulationCommandLogSnapshot.TryDeserialize(
                    commandPayload, out var commandLog, out _))
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidCommandLog);
                return false;
            }
            if (stream.Position != stream.Length)
            {
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.TrailingBytes);
                return false;
            }

            snapshot = new SimulationReplayPackageSnapshot(
                capacity,
                initialStateHash,
                resources,
                units,
                buildings,
                economy,
                construction,
                production,
                worldCommands,
                economyLog!,
                constructionLog!,
                productionLog,
                commandLog!);
            validation = new ReplayPackageValidationResult(
                ReplayPackageValidationCode.Success);
            return true;
        }
        catch (EndOfStreamException)
        {
            validation = new ReplayPackageValidationResult(
                ReplayPackageValidationCode.PayloadTooShort);
            return false;
        }
        catch (IOException)
        {
            validation = new ReplayPackageValidationResult(
                ReplayPackageValidationCode.PayloadTooShort);
            return false;
        }
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(CurrentFormatVersion);
        writer.Write(SimulationCapacity);
        writer.Write(InitialStateHash);
        WriteResources(writer, Resources);
        writer.Write(Units.Length);
        for (var index = 0; index < Units.Length; index++)
        {
            WriteUnit(writer, Units[index]);
        }
        writer.Write(Buildings.Length);
        for (var index = 0; index < Buildings.Length; index++)
        {
            WriteBuilding(writer, Buildings[index]);
        }
        RuntimeHotSnapshotCodec.WriteEconomy(
            writer, Economy, Units.Length);
        RuntimeHotSnapshotCodec.WriteConstruction(writer, Construction);
        RuntimeHotSnapshotCodec.WriteProduction(writer, Production);
        writer.Write(WorldCommands.Length);
        for (var index = 0; index < WorldCommands.Length; index++)
        {
            WriteWorldCommand(writer, WorldCommands[index]);
        }
        writer.Write(EconomyCommandLog.CanonicalBytes.Length);
        writer.Write(EconomyCommandLog.CanonicalBytes);
        writer.Write(ConstructionCommandLog.CanonicalBytes.Length);
        writer.Write(ConstructionCommandLog.CanonicalBytes);
        writer.Write(ProductionCommandLog.CanonicalBytes.Length);
        writer.Write(ProductionCommandLog.CanonicalBytes);
        writer.Write(CommandLog.CanonicalBytes.Length);
        writer.Write(CommandLog.CanonicalBytes);
        writer.Flush();
        return stream.ToArray();
    }

    private static int ReadCount(BinaryReader reader)
    {
        var value = reader.ReadInt32();
        return value is < 0 or > MaximumElements ? -1 : value;
    }

    private static bool ValidResources(ReplayResourceManifest resources) =>
        resources.NavigationFormatVersion > 0 && resources.NavigationHash != 0UL &&
        resources.GameplayProfileFormatVersion > 0 &&
        resources.GameplayProfileHash != 0UL &&
        ((resources.ClearanceBakeFormatVersion == 0 &&
          resources.ClearanceBakeHash == 0UL) ||
         (resources.ClearanceBakeFormatVersion > 0 &&
          resources.ClearanceBakeHash != 0UL));

    private static bool ValidUnit(ReplayInitialUnit unit)
    {
        if (!Finite(unit.Position) || !Positive(unit.Radius) ||
            !Positive(unit.MaximumSpeed) || !Positive(unit.Acceleration))
        {
            return false;
        }
        try
        {
            unit.CombatProfile.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool ValidRect(SimRect rect) =>
        Finite(rect.Min) && Finite(rect.Max) &&
        rect.Max.X > rect.Min.X && rect.Max.Y > rect.Min.Y;

    private static bool Finite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool Positive(float value) =>
        float.IsFinite(value) && value > 0f;

    private static ReplayResourceManifest ReadResources(BinaryReader reader) => new(
        reader.ReadInt32(), reader.ReadUInt64(),
        reader.ReadInt32(), reader.ReadUInt64(),
        reader.ReadInt32(), reader.ReadUInt64());

    private static void WriteResources(
        BinaryWriter writer, ReplayResourceManifest resources)
    {
        writer.Write(resources.NavigationFormatVersion);
        writer.Write(resources.NavigationHash);
        writer.Write(resources.GameplayProfileFormatVersion);
        writer.Write(resources.GameplayProfileHash);
        writer.Write(resources.ClearanceBakeFormatVersion);
        writer.Write(resources.ClearanceBakeHash);
    }

    private static ReplayInitialUnit ReadUnit(BinaryReader reader) => new(
        ReadVector(reader),
        reader.ReadSingle(),
        reader.ReadSingle(),
        reader.ReadSingle(),
        reader.ReadInt32(),
        new CombatProfileSnapshot(
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), (CombatPositioningKind)reader.ReadByte()));

    private static void WriteUnit(BinaryWriter writer, ReplayInitialUnit unit)
    {
        WriteVector(writer, unit.Position);
        writer.Write(unit.Radius);
        writer.Write(unit.MaximumSpeed);
        writer.Write(unit.Acceleration);
        writer.Write(unit.Team);
        writer.Write(unit.CombatProfile.MaximumHealth);
        writer.Write(unit.CombatProfile.AttackDamage);
        writer.Write(unit.CombatProfile.AttackRange);
        writer.Write(unit.CombatProfile.AcquisitionRange);
        writer.Write(unit.CombatProfile.AttackCooldownSeconds);
        writer.Write(unit.CombatProfile.AttackWindupSeconds);
        writer.Write(unit.CombatProfile.LeashDistance);
        writer.Write((byte)unit.CombatProfile.Positioning);
    }

    private static ReplayInitialBuilding ReadBuilding(BinaryReader reader) => new(
        new DynamicFootprintId(reader.ReadInt32()),
        ReadRect(reader),
        reader.ReadInt32());

    private static void WriteBuilding(
        BinaryWriter writer, ReplayInitialBuilding building)
    {
        writer.Write(building.Id.Value);
        WriteRect(writer, building.Bounds);
        writer.Write(building.PlacedRevision);
    }

    private static RecordedWorldCommand ReadWorldCommand(BinaryReader reader) => new(
        reader.ReadInt64(),
        (ReplayWorldCommandKind)reader.ReadByte(),
        new DynamicFootprintId(reader.ReadInt32()),
        ReadRect(reader));

    private static void WriteWorldCommand(
        BinaryWriter writer, RecordedWorldCommand command)
    {
        writer.Write(command.Tick);
        writer.Write((byte)command.Kind);
        writer.Write(command.FootprintId.Value);
        WriteRect(writer, command.Bounds);
    }

    private static Vector2 ReadVector(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle());

    private static void WriteVector(BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    private static SimRect ReadRect(BinaryReader reader) =>
        new(ReadVector(reader), ReadVector(reader));

    private static void WriteRect(BinaryWriter writer, SimRect value)
    {
        WriteVector(writer, value.Min);
        WriteVector(writer, value.Max);
    }
}

public sealed class SimulationReplayPackageRecorder
{
    private readonly int _capacity;
    private readonly ulong _initialStateHash;
    private readonly ReplayResourceManifest _resources;
    private readonly ReplayInitialUnit[] _units;
    private readonly ReplayInitialBuilding[] _buildings;
    private readonly EconomyRuntimeSnapshot _economy;
    private readonly ConstructionRuntimeSnapshot _construction;
    private readonly ProductionRuntimeSnapshot _production;
    private readonly List<RecordedWorldCommand> _worldCommands = [];

    public SimulationReplayPackageRecorder(
        RtsSimulation simulation,
        ReplayResourceManifest resources)
    {
        if (simulation.Metrics.Tick != 0)
        {
            throw new InvalidOperationException(
                "Replay package recording must start at simulation tick zero.");
        }
        _capacity = simulation.Units.Capacity;
        _initialStateHash = simulation.ComputeStateHash();
        _resources = resources;
        _units = CaptureUnits(simulation);
        _economy = simulation.Economy.CaptureRuntimeState(
            simulation.Units.Count);
        _construction = simulation.Construction.CaptureRuntimeState();
        _production = simulation.Production.CaptureRuntimeState();
        _buildings = simulation.World.DynamicOccupancy.Snapshot()
            .Select(value => new ReplayInitialBuilding(
                value.Id, value.Bounds, value.PlacedRevision))
            .ToArray();
        for (var index = 0; index < _buildings.Length; index++)
        {
            if (_buildings[index].Id.Value != index + 1 ||
                _buildings[index].PlacedRevision != index + 1)
            {
                throw new InvalidOperationException(
                    "Initial replay buildings must be a dense tick-zero setup without prior removals.");
            }
        }
    }

    public void RecordPlace(long tick, DynamicFootprintId id, SimRect bounds) =>
        _worldCommands.Add(new RecordedWorldCommand(
            tick, ReplayWorldCommandKind.PlaceBuilding, id, bounds));

    public void RecordRemove(long tick, DynamicFootprintId id, SimRect bounds) =>
        _worldCommands.Add(new RecordedWorldCommand(
            tick, ReplayWorldCommandKind.RemoveBuilding, id, bounds));

    public SimulationReplayPackageSnapshot Capture(
        SimulationCommandLogSnapshot commandLog,
        EconomyCommandLogSnapshot economyCommandLog,
        ConstructionCommandLogSnapshot constructionCommandLog,
        ProductionCommandLogSnapshot productionCommandLog) => new(
        _capacity,
        _initialStateHash,
        _resources,
        _units.ToArray(),
        _buildings.ToArray(),
        _economy,
        _construction,
        _production,
        _worldCommands.ToArray(),
        economyCommandLog,
        constructionCommandLog,
        productionCommandLog,
        commandLog);

    private static ReplayInitialUnit[] CaptureUnits(RtsSimulation simulation)
    {
        var result = new ReplayInitialUnit[simulation.Units.Count];
        for (var unit = 0; unit < result.Length; unit++)
        {
            if (!simulation.Units.Alive[unit])
            {
                throw new InvalidOperationException(
                    "Replay package initial manifest cannot contain dead units.");
            }
            result[unit] = new ReplayInitialUnit(
                simulation.Units.Positions[unit],
                simulation.Units.Radii[unit],
                simulation.Units.MaxSpeeds[unit],
                simulation.Units.Accelerations[unit],
                simulation.Combat.Teams[unit],
                new CombatProfileSnapshot(
                    simulation.Combat.MaximumHealth[unit],
                    simulation.Combat.AttackDamage[unit],
                    simulation.Combat.AttackRanges[unit],
                    simulation.Combat.AcquisitionRanges[unit],
                    simulation.Combat.AttackCooldownDurations[unit],
                    simulation.Combat.AttackWindupDurations[unit],
                    simulation.Combat.LeashDistances[unit],
                    simulation.Combat.PositioningKinds[unit]));
        }
        return result;
    }
}

public static class SimulationReplayPackageFactory
{
    public static bool TryCreateSimulation(
        SimulationReplayPackageSnapshot package,
        NavigationMapSnapshot navigation,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake,
        out RtsSimulation? simulation,
        out ReplayPackageValidationResult validation)
    {
        validation = package.ValidateResources(
            navigation, gameplayProfiles, clearanceBake);
        if (!validation.Succeeded)
        {
            simulation = null;
            return false;
        }

        var world = navigation.CreateWorld();
        var routePlanner = navigation.CreateRoutePlanner(world);
        var chokeController = navigation.CreateChokeController();
        simulation = new RtsSimulation(
            world,
            new GridPathProvider(world),
            package.SimulationCapacity,
            routePlanner,
            chokeController,
            clearanceBake);
        for (var index = 0; index < package.Buildings.Length; index++)
        {
            var actual = simulation.PlaceBuilding(package.Buildings[index].Bounds);
            if (actual != package.Buildings[index].Id)
            {
                simulation = null;
                validation = new ReplayPackageValidationResult(
                    ReplayPackageValidationCode.InvalidBuildingManifest, index);
                return false;
            }
        }
        for (var index = 0; index < package.Units.Length; index++)
        {
            var unit = package.Units[index];
            var actual = simulation.AddUnit(
                unit.Position,
                unit.Team,
                unit.CombatProfile,
                unit.Radius,
                unit.MaximumSpeed,
                unit.Acceleration);
            if (actual != index)
            {
                throw new InvalidOperationException("Unit IDs are not dense.");
            }
        }
        try
        {
            simulation.Economy.RestoreRuntimeState(
                package.Economy, simulation.Units.Count);
        }
        catch (InvalidOperationException)
        {
            simulation = null;
            validation = new ReplayPackageValidationResult(
                ReplayPackageValidationCode.InvalidEconomyManifest);
            return false;
        }
        try
        {
            simulation.Construction.RestoreRuntimeState(package.Construction);
        }
        catch (InvalidOperationException)
        {
            simulation = null;
            validation = new ReplayPackageValidationResult(
                ReplayPackageValidationCode.InvalidConstructionManifest);
            return false;
        }
        try
        {
            simulation.Production.RestoreRuntimeState(
                package.Production,
                simulation.Construction,
                simulation.Economy,
                simulation.Units);
        }
        catch (InvalidOperationException)
        {
            simulation = null;
            validation = new ReplayPackageValidationResult(
                ReplayPackageValidationCode.InvalidProductionManifest);
            return false;
        }
        if (simulation.ComputeStateHash() != package.InitialStateHash)
        {
            simulation = null;
            validation = new ReplayPackageValidationResult(
                ReplayPackageValidationCode.InitialStateMismatch);
            return false;
        }

        validation = new ReplayPackageValidationResult(
            ReplayPackageValidationCode.Success);
        return true;
    }
}

public sealed class SimulationReplayPackageRunner
{
    private readonly SimulationReplayPackageSnapshot _package;
    private readonly SimulationCommandReplay _commands;
    private readonly EconomyCommandReplay _economyCommands;
    private readonly ConstructionCommandReplay _constructionCommands;
    private readonly ProductionCommandReplay _productionCommands;
    private int _worldCursor;

    public SimulationReplayPackageRunner(SimulationReplayPackageSnapshot package)
    {
        _package = package;
        _commands = new SimulationCommandReplay(package.CommandLog);
        _economyCommands = new EconomyCommandReplay(package.EconomyCommandLog);
        _constructionCommands = new ConstructionCommandReplay(
            package.ConstructionCommandLog);
        _productionCommands = new ProductionCommandReplay(
            package.ProductionCommandLog);
    }

    public bool Completed =>
        _worldCursor >= _package.WorldCommands.Length &&
        _constructionCommands.Completed && _productionCommands.Completed &&
        _economyCommands.Completed &&
        _commands.Completed;

    internal void SeekToTick(long tick)
    {
        _worldCursor = 0;
        while (_worldCursor < _package.WorldCommands.Length &&
               _package.WorldCommands[_worldCursor].Tick < tick)
        {
            _worldCursor++;
        }
        _commands.SeekToTick(tick);
        _economyCommands.SeekToTick(tick);
        _constructionCommands.SeekToTick(tick);
        _productionCommands.SeekToTick(tick);
    }

    public void ApplyForCurrentTick(RtsSimulation simulation)
    {
        if (_worldCursor < _package.WorldCommands.Length &&
            _package.WorldCommands[_worldCursor].Tick < simulation.Metrics.Tick)
        {
            throw new InvalidOperationException("World replay cursor is behind simulation.");
        }
        while (_worldCursor < _package.WorldCommands.Length &&
               _package.WorldCommands[_worldCursor].Tick == simulation.Metrics.Tick)
        {
            ApplyWorldCommand(simulation, _package.WorldCommands[_worldCursor]);
            _worldCursor++;
        }
        _constructionCommands.ApplyForCurrentTick(simulation);
        _productionCommands.ApplyForCurrentTick(simulation);
        _economyCommands.ApplyForCurrentTick(simulation);
        _commands.ApplyForCurrentTick(simulation);
    }

    private static void ApplyWorldCommand(
        RtsSimulation simulation, RecordedWorldCommand command)
    {
        if (command.Kind == ReplayWorldCommandKind.PlaceBuilding)
        {
            var actual = simulation.PlaceBuilding(command.Bounds);
            if (actual != command.FootprintId)
            {
                throw new InvalidOperationException(
                    $"Replay building ID mismatch: {actual.Value} != {command.FootprintId.Value}.");
            }
            return;
        }
        if (!simulation.RemoveBuilding(command.FootprintId))
        {
            throw new InvalidOperationException(
                $"Replay could not remove building {command.FootprintId.Value}.");
        }
    }
}
