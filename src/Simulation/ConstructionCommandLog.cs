using System.Numerics;
using System.Text;

namespace RtsDemo.Simulation;

public enum ConstructionReplayCommandKind : byte
{
    Build = 1,
    Cancel = 2,
    Resume = 3
}

public readonly record struct RecordedConstructionCommand(
    long Tick,
    ConstructionReplayCommandKind Kind,
    int PlayerId,
    int BuilderUnit,
    GameplayBuildingId BuildingId,
    BuildingTypeProfile Type,
    Vector2 Center,
    EconomyResourceNodeId RefineryNode);

public enum ConstructionCommandLogValidationCode : byte
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

public sealed class ConstructionCommandLogSnapshot
{
    private const uint Magic = 0x43435452; // RTCC
    private const int MaximumEntries = 1_000_000;
    public const int CurrentFormatVersion = 1;

    public ConstructionCommandLogSnapshot(RecordedConstructionCommand[] entries)
    {
        Entries = entries;
        CanonicalBytes = Serialize(entries);
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public RecordedConstructionCommand[] Entries { get; }
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> payload,
        out ConstructionCommandLogSnapshot? snapshot,
        out ConstructionCommandLogValidationCode validation)
    {
        snapshot = null;
        if (payload.Length < 12)
        {
            validation = ConstructionCommandLogValidationCode.PayloadTooShort;
            return false;
        }
        try
        {
            using var stream = new MemoryStream(payload.ToArray(), false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadUInt32() != Magic)
            {
                validation = ConstructionCommandLogValidationCode.InvalidMagic;
                return false;
            }
            if (reader.ReadInt32() != CurrentFormatVersion)
            {
                validation = ConstructionCommandLogValidationCode.UnsupportedVersion;
                return false;
            }
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumEntries)
            {
                validation = ConstructionCommandLogValidationCode.InvalidEntryCount;
                return false;
            }
            var entries = new RecordedConstructionCommand[count];
            var previousTick = -1L;
            for (var index = 0; index < count; index++)
            {
                var tick = reader.ReadInt64();
                var kind = (ConstructionReplayCommandKind)reader.ReadByte();
                var playerId = reader.ReadInt32();
                RecordedConstructionCommand entry;
                if (kind == ConstructionReplayCommandKind.Build)
                {
                    entry = new RecordedConstructionCommand(
                        tick, kind, playerId, reader.ReadInt32(), default,
                        ConstructionSerialization.ReadProfile(reader),
                        ConstructionSerialization.ReadVector(reader),
                        new EconomyResourceNodeId(reader.ReadInt32()));
                }
                else
                {
                    entry = new RecordedConstructionCommand(
                        tick, kind, playerId,
                        kind == ConstructionReplayCommandKind.Resume
                            ? reader.ReadInt32()
                            : -1,
                        new GameplayBuildingId(reader.ReadInt32()),
                        default, default, new EconomyResourceNodeId(-1));
                }
                if (tick < previousTick || tick < 0 || playerId < 0 ||
                    !Enum.IsDefined(kind) ||
                    kind == ConstructionReplayCommandKind.Build &&
                        (entry.BuilderUnit < 0 ||
                         !ConstructionSerialization.ValidProfile(entry.Type) ||
                         !ConstructionSerialization.Finite(entry.Center)) ||
                    kind != ConstructionReplayCommandKind.Build &&
                        entry.BuildingId.Value < 0 ||
                    kind == ConstructionReplayCommandKind.Resume &&
                        entry.BuilderUnit < 0)
                {
                    validation = ConstructionCommandLogValidationCode.InvalidEntry;
                    return false;
                }
                entries[index] = entry;
                previousTick = tick;
            }
            if (stream.Position != stream.Length)
            {
                validation = ConstructionCommandLogValidationCode.TrailingBytes;
                return false;
            }
            snapshot = new ConstructionCommandLogSnapshot(entries);
            validation = ConstructionCommandLogValidationCode.Success;
            return true;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or
                InvalidDataException or ArgumentException)
        {
            validation = ConstructionCommandLogValidationCode.PayloadTooShort;
            return false;
        }
    }

    private static byte[] Serialize(RecordedConstructionCommand[] entries)
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
            if (entry.Kind == ConstructionReplayCommandKind.Build)
            {
                writer.Write(entry.BuilderUnit);
                ConstructionSerialization.WriteProfile(writer, entry.Type);
                ConstructionSerialization.WriteVector(writer, entry.Center);
                writer.Write(entry.RefineryNode.Value);
            }
            else
            {
                if (entry.Kind == ConstructionReplayCommandKind.Resume)
                {
                    writer.Write(entry.BuilderUnit);
                }
                writer.Write(entry.BuildingId.Value);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }
}

public sealed class ConstructionCommandRecorder
{
    private readonly List<RecordedConstructionCommand> _entries = [];

    public void RecordBuild(
        long tick, int playerId, int builderUnit, BuildingTypeProfile type,
        Vector2 center, EconomyResourceNodeId refineryNode) =>
        _entries.Add(new RecordedConstructionCommand(
            tick, ConstructionReplayCommandKind.Build, playerId, builderUnit,
            default, type, center, refineryNode));

    public void RecordCancel(long tick, int playerId, GameplayBuildingId id) =>
        _entries.Add(new RecordedConstructionCommand(
            tick, ConstructionReplayCommandKind.Cancel, playerId, -1,
            id, default, default, new EconomyResourceNodeId(-1)));

    public void RecordResume(
        long tick, int playerId, GameplayBuildingId id, int builderUnit) =>
        _entries.Add(new RecordedConstructionCommand(
            tick, ConstructionReplayCommandKind.Resume, playerId, builderUnit,
            id, default, default, new EconomyResourceNodeId(-1)));

    public ConstructionCommandLogSnapshot Capture() => new(_entries.ToArray());
}

public sealed class ConstructionCommandReplay
{
    private readonly ConstructionCommandLogSnapshot _log;
    private int _cursor;

    public ConstructionCommandReplay(ConstructionCommandLogSnapshot log) =>
        _log = log;

    public bool Completed => _cursor >= _log.Entries.Length;

    internal void SeekToTick(long tick)
    {
        _cursor = 0;
        while (_cursor < _log.Entries.Length && _log.Entries[_cursor].Tick < tick)
        {
            _cursor++;
        }
    }

    public void ApplyForCurrentTick(RtsSimulation simulation)
    {
        if (!Completed && _log.Entries[_cursor].Tick < simulation.Metrics.Tick)
        {
            throw new InvalidOperationException(
                "Construction replay cursor is behind the simulation tick.");
        }
        while (!Completed &&
               _log.Entries[_cursor].Tick == simulation.Metrics.Tick)
        {
            simulation.ApplyRecordedConstructionCommand(_log.Entries[_cursor]);
            _cursor++;
        }
    }
}

internal static class ConstructionSerialization
{
    private const int MaximumNameBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void WriteProfile(BinaryWriter writer, BuildingTypeProfile value)
    {
        writer.Write(value.Id);
        var name = StrictUtf8.GetBytes(value.Name);
        writer.Write(name.Length);
        writer.Write(name);
        writer.Write((byte)value.Function);
        WriteVector(writer, value.Size);
        writer.Write((byte)value.MinimumPassageClass);
        writer.Write(value.Cost.Minerals);
        writer.Write(value.Cost.VespeneGas);
        writer.Write(value.Cost.Supply);
        writer.Write(value.BuildSeconds);
        writer.Write(value.MaximumHealth);
        writer.Write(value.SupplyProvided);
        writer.Write(value.CancelRefundFraction);
        writer.Write((byte)value.ConstructionMethod);
        writer.Write(value.RequiresVespeneNode);
    }

    public static BuildingTypeProfile ReadProfile(BinaryReader reader)
    {
        var id = reader.ReadInt32();
        var nameLength = reader.ReadInt32();
        if (nameLength is < 1 or > MaximumNameBytes)
        {
            throw new InvalidDataException();
        }
        var nameBytes = reader.ReadBytes(nameLength);
        if (nameBytes.Length != nameLength)
        {
            throw new EndOfStreamException();
        }
        return new BuildingTypeProfile(
            id, StrictUtf8.GetString(nameBytes),
            (BuildingFunctionKind)reader.ReadByte(), ReadVector(reader),
            (MovementClass)reader.ReadByte(),
            new EconomyCost(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadInt32(),
            reader.ReadSingle(), (ConstructionMethodKind)reader.ReadByte(),
            reader.ReadBoolean());
    }

    public static void WriteVector(BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    public static Vector2 ReadVector(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle());

    public static bool Finite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    public static bool ValidProfile(BuildingTypeProfile profile) =>
        ConstructionSystem.ValidProfile(profile);
}
