using System.Numerics;
using System.Text;
using System.Collections.Immutable;

namespace RtsDemo.Simulation;

public enum ProductionReplayCommandKind : byte
{
    Train = 1,
    Cancel = 2,
    SetRallyPoint = 3,
    Research = 4,
    CancelResearch = 5
}

public readonly record struct RecordedProductionCommand(
    long Tick,
    ProductionReplayCommandKind Kind,
    int PlayerId,
    GameplayBuildingId Producer,
    ProductionOrderId OrderId,
    ProductionRecipeProfile Recipe,
    RallyTarget Rally,
    ResearchOrderId ResearchOrderId,
    TechnologyProfile Technology);

public enum ProductionCommandLogValidationCode : byte
{
    Success,
    PayloadTooShort,
    InvalidMagic,
    UnsupportedVersion,
    InvalidEntryCount,
    InvalidEntry,
    InvalidTickOrder,
    TrailingBytes
}

public sealed class ProductionCommandLogSnapshot
{
    private const uint Magic = 0x43505452; // RTPC
    private const int MaximumEntries = 1_000_000;
    public const int CurrentFormatVersion = 4;

    public ProductionCommandLogSnapshot(RecordedProductionCommand[] entries)
    {
        Entries = entries;
        CanonicalBytes = Serialize(entries);
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public RecordedProductionCommand[] Entries { get; }
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> payload,
        out ProductionCommandLogSnapshot? snapshot,
        out ProductionCommandLogValidationCode validation)
    {
        snapshot = null;
        if (payload.Length < 12)
        {
            validation = ProductionCommandLogValidationCode.PayloadTooShort;
            return false;
        }
        try
        {
            using var stream = new MemoryStream(payload.ToArray(), false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadUInt32() != Magic)
            {
                validation = ProductionCommandLogValidationCode.InvalidMagic;
                return false;
            }
            if (reader.ReadInt32() != CurrentFormatVersion)
            {
                validation = ProductionCommandLogValidationCode.UnsupportedVersion;
                return false;
            }
            var count = reader.ReadInt32();
            if (count is < 0 or > MaximumEntries)
            {
                validation = ProductionCommandLogValidationCode.InvalidEntryCount;
                return false;
            }
            var entries = new RecordedProductionCommand[count];
            var previousTick = -1L;
            for (var index = 0; index < count; index++)
            {
                var tick = reader.ReadInt64();
                var kind = (ProductionReplayCommandKind)reader.ReadByte();
                var player = reader.ReadInt32();
                var producer = new GameplayBuildingId(reader.ReadInt32());
                RecordedProductionCommand entry = kind switch
                {
                    ProductionReplayCommandKind.Train => new(
                        tick, kind, player, producer, default,
                        ProductionSerialization.ReadRecipe(reader), default,
                        default, default),
                    ProductionReplayCommandKind.Cancel => new(
                        tick, kind, player, producer,
                        new ProductionOrderId(reader.ReadInt32()), default, default,
                        default, default),
                    ProductionReplayCommandKind.SetRallyPoint => new(
                        tick, kind, player, producer, default, default,
                        ReadRally(reader), default, default),
                    ProductionReplayCommandKind.Research => new(
                        tick, kind, player, producer, default, default, default,
                        default, TechnologySerialization.ReadProfile(reader)),
                    ProductionReplayCommandKind.CancelResearch => new(
                        tick, kind, player, producer, default, default, default,
                        new ResearchOrderId(reader.ReadInt32()), default),
                    _ => default
                };
                if (tick < previousTick)
                {
                    validation = ProductionCommandLogValidationCode.InvalidTickOrder;
                    return false;
                }
                if (tick < 0 || !Enum.IsDefined(kind) || player < 0 ||
                    producer.Value < 0 ||
                    kind == ProductionReplayCommandKind.Train &&
                        !ProductionSystem.ValidRecipeProfile(entry.Recipe) ||
                    kind == ProductionReplayCommandKind.Cancel &&
                        entry.OrderId.Value <= 0 ||
                    kind == ProductionReplayCommandKind.SetRallyPoint &&
                        !ProductionSystem.ValidRallyTarget(entry.Rally) ||
                    kind == ProductionReplayCommandKind.Research &&
                        !TechnologyCatalogSnapshot.ValidProfile(entry.Technology) ||
                    kind == ProductionReplayCommandKind.CancelResearch &&
                        entry.ResearchOrderId.Value <= 0)
                {
                    validation = ProductionCommandLogValidationCode.InvalidEntry;
                    return false;
                }
                entries[index] = entry;
                previousTick = tick;
            }
            if (stream.Position != stream.Length)
            {
                validation = ProductionCommandLogValidationCode.TrailingBytes;
                return false;
            }
            snapshot = new ProductionCommandLogSnapshot(entries);
            validation = ProductionCommandLogValidationCode.Success;
            return true;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or
                InvalidDataException or ArgumentException)
        {
            validation = ProductionCommandLogValidationCode.PayloadTooShort;
            return false;
        }
    }

    private static byte[] Serialize(RecordedProductionCommand[] entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(Magic);
        writer.Write(CurrentFormatVersion);
        writer.Write(entries.Length);
        foreach (var entry in entries)
        {
            writer.Write(entry.Tick);
            writer.Write((byte)entry.Kind);
            writer.Write(entry.PlayerId);
            writer.Write(entry.Producer.Value);
            if (entry.Kind == ProductionReplayCommandKind.Train)
                ProductionSerialization.WriteRecipe(writer, entry.Recipe);
            else if (entry.Kind == ProductionReplayCommandKind.Cancel)
                writer.Write(entry.OrderId.Value);
            else if (entry.Kind == ProductionReplayCommandKind.SetRallyPoint)
                WriteRally(writer, entry.Rally);
            else if (entry.Kind == ProductionReplayCommandKind.Research)
                TechnologySerialization.WriteProfile(writer, entry.Technology);
            else
                writer.Write(entry.ResearchOrderId.Value);
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static void WriteRally(BinaryWriter writer, RallyTarget value)
    {
        writer.Write((byte)value.Kind);
        ConstructionSerialization.WriteVector(writer, value.Position);
        writer.Write(value.ResourceNode.Value);
        writer.Write(value.Unit);
    }

    internal static RallyTarget ReadRally(BinaryReader reader) => new(
        (RallyTargetKind)reader.ReadByte(),
        ConstructionSerialization.ReadVector(reader),
        new EconomyResourceNodeId(reader.ReadInt32()),
        reader.ReadInt32());
}

public sealed class ProductionCommandRecorder
{
    private readonly List<RecordedProductionCommand> _entries = [];
    public void RecordTrain(
        long tick, int player, GameplayBuildingId producer,
        ProductionRecipeProfile recipe) =>
        _entries.Add(new RecordedProductionCommand(
            tick, ProductionReplayCommandKind.Train, player, producer,
            default, recipe, default, default, default));
    public void RecordCancel(
        long tick, int player, GameplayBuildingId producer,
        ProductionOrderId order) =>
        _entries.Add(new RecordedProductionCommand(
            tick, ProductionReplayCommandKind.Cancel, player, producer,
            order, default, default, default, default));
    public void RecordRally(
        long tick, int player, GameplayBuildingId producer, RallyTarget rally) =>
        _entries.Add(new RecordedProductionCommand(
            tick, ProductionReplayCommandKind.SetRallyPoint, player, producer,
            default, default, rally, default, default));
    public void RecordResearch(
        long tick, int player, GameplayBuildingId researcher,
        TechnologyProfile technology) =>
        _entries.Add(new RecordedProductionCommand(
            tick, ProductionReplayCommandKind.Research, player, researcher,
            default, default, default, default, technology));
    public void RecordCancelResearch(
        long tick, int player, GameplayBuildingId researcher,
        ResearchOrderId order) =>
        _entries.Add(new RecordedProductionCommand(
            tick, ProductionReplayCommandKind.CancelResearch, player, researcher,
            default, default, default, order, default));
    public ProductionCommandLogSnapshot Capture() => new(_entries.ToArray());
}

public sealed class ProductionCommandReplay
{
    private readonly ProductionCommandLogSnapshot _log;
    private int _cursor;
    public ProductionCommandReplay(ProductionCommandLogSnapshot log) => _log = log;
    public bool Completed => _cursor >= _log.Entries.Length;
    internal void SeekToTick(long tick)
    {
        _cursor = 0;
        while (_cursor < _log.Entries.Length && _log.Entries[_cursor].Tick < tick)
            _cursor++;
    }
    public void ApplyForCurrentTick(RtsSimulation simulation)
    {
        if (!Completed && _log.Entries[_cursor].Tick < simulation.Metrics.Tick)
            throw new InvalidOperationException(
                "Production replay cursor is behind the simulation tick.");
        while (!Completed && _log.Entries[_cursor].Tick == simulation.Metrics.Tick)
            simulation.ApplyRecordedProductionCommand(_log.Entries[_cursor++]);
    }
}

internal static class ProductionSerialization
{
    private const int MaximumNameBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void WriteRecipe(BinaryWriter writer, ProductionRecipeProfile value)
    {
        writer.Write(value.Id);
        WriteString(writer, value.Name);
        writer.Write(value.ProducerBuildingTypeId);
        WriteUnitType(writer, value.UnitType);
        writer.Write(value.Cost.Minerals);
        writer.Write(value.Cost.VespeneGas);
        writer.Write(value.Cost.Supply);
        writer.Write(value.ProductionSeconds);
        writer.Write(value.CancelRefundFraction);
        writer.Write(value.Requirements.Length);
        foreach (var requirement in value.Requirements)
        {
            writer.Write((byte)requirement.Kind);
            writer.Write(requirement.TypeId);
            writer.Write(requirement.Count);
        }
    }

    public static ProductionRecipeProfile ReadRecipe(BinaryReader reader)
    {
        var recipe = new ProductionRecipeProfile(
            reader.ReadInt32(), ReadString(reader), reader.ReadInt32(),
            ReadUnitType(reader),
            new EconomyCost(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()),
            reader.ReadSingle(), reader.ReadSingle());
        var count = reader.ReadInt32();
        if (count is < 0 or > 32) throw new InvalidDataException();
        var requirements = new ProductionRequirementProfile[count];
        for (var index = 0; index < count; index++)
            requirements[index] = new ProductionRequirementProfile(
                (ProductionRequirementKind)reader.ReadByte(),
                reader.ReadInt32(), reader.ReadInt32());
        return recipe with { Requirements = requirements.ToImmutableArray() };
    }

    private static void WriteUnitType(BinaryWriter writer, UnitTypeProfile value)
    {
        writer.Write(value.Id);
        WriteString(writer, value.Name);
        writer.Write(value.Movement.Id);
        WriteString(writer, value.Movement.Name);
        writer.Write(value.Movement.PhysicalRadius);
        writer.Write(value.Movement.MaximumSpeed);
        writer.Write(value.Movement.Acceleration);
        writer.Write((byte)value.Movement.MovementClass);
        writer.Write(value.Movement.NavigationRadius);
        writer.Write(value.Combat.MaximumHealth);
        writer.Write(value.Combat.AttackDamage);
        writer.Write(value.Combat.AttackRange);
        writer.Write(value.Combat.AcquisitionRange);
        writer.Write(value.Combat.AttackCooldownSeconds);
        writer.Write(value.Combat.AttackWindupSeconds);
        writer.Write(value.Combat.LeashDistance);
        writer.Write((byte)value.Combat.Positioning);
        writer.Write(value.IsWorker);
    }

    private static UnitTypeProfile ReadUnitType(BinaryReader reader) => new(
        reader.ReadInt32(), ReadString(reader),
        new UnitMovementProfileSnapshot(
            reader.ReadInt32(), ReadString(reader), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            (MovementClass)reader.ReadByte(), reader.ReadSingle()),
        new CombatProfileSnapshot(
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), (CombatPositioningKind)reader.ReadByte()),
        reader.ReadBoolean());

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 1 or > MaximumNameBytes) throw new InvalidDataException();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return StrictUtf8.GetString(bytes);
    }
}
