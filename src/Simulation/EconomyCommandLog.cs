namespace RtsDemo.Simulation;

public enum EconomyCommandKind : byte
{
    Gather = 1,
    SetRefineryOperational = 2
}

public readonly record struct RecordedEconomyCommand(
    long Tick,
    EconomyCommandKind Kind,
    int PlayerId,
    int UnitId,
    int ResourceNodeId,
    bool Value);

public enum EconomyCommandLogValidationCode : byte
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

public sealed class EconomyCommandLogSnapshot
{
    private const uint Magic = 0x43455452; // RTEC
    private const int MaximumEntries = 1_000_000;
    public const int CurrentFormatVersion = 1;

    public EconomyCommandLogSnapshot(RecordedEconomyCommand[] entries)
    {
        Entries = entries;
        CanonicalBytes = Serialize(entries);
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public RecordedEconomyCommand[] Entries { get; }
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> payload,
        out EconomyCommandLogSnapshot? snapshot,
        out EconomyCommandLogValidationCode validation)
    {
        snapshot = null;
        if (payload.Length < 12)
        {
            validation = EconomyCommandLogValidationCode.PayloadTooShort;
            return false;
        }
        try
        {
            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic)
            {
                validation = EconomyCommandLogValidationCode.InvalidMagic;
                return false;
            }
            if (reader.ReadInt32() != CurrentFormatVersion)
            {
                validation = EconomyCommandLogValidationCode.UnsupportedVersion;
                return false;
            }
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumEntries)
            {
                validation = EconomyCommandLogValidationCode.InvalidEntryCount;
                return false;
            }
            var entries = new RecordedEconomyCommand[count];
            var previousTick = -1L;
            for (var index = 0; index < count; index++)
            {
                var entry = new RecordedEconomyCommand(
                    reader.ReadInt64(),
                    (EconomyCommandKind)reader.ReadByte(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadBoolean());
                if (entry.Tick < previousTick)
                {
                    validation = EconomyCommandLogValidationCode.InvalidTickOrder;
                    return false;
                }
                if (entry.Tick < 0 || !Enum.IsDefined(entry.Kind) ||
                    entry.PlayerId < 0 || entry.ResourceNodeId < 0 ||
                    entry.Kind == EconomyCommandKind.Gather && entry.UnitId < 0)
                {
                    validation = EconomyCommandLogValidationCode.InvalidEntry;
                    return false;
                }
                entries[index] = entry;
                previousTick = entry.Tick;
            }
            if (stream.Position != stream.Length)
            {
                validation = EconomyCommandLogValidationCode.TrailingBytes;
                return false;
            }
            snapshot = new EconomyCommandLogSnapshot(entries);
            validation = EconomyCommandLogValidationCode.Success;
            return true;
        }
        catch (EndOfStreamException)
        {
            validation = EconomyCommandLogValidationCode.PayloadTooShort;
            return false;
        }
    }

    private static byte[] Serialize(RecordedEconomyCommand[] entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(CurrentFormatVersion);
        writer.Write(entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            writer.Write(entry.Tick);
            writer.Write((byte)entry.Kind);
            writer.Write(entry.PlayerId);
            writer.Write(entry.UnitId);
            writer.Write(entry.ResourceNodeId);
            writer.Write(entry.Value);
        }
        writer.Flush();
        return stream.ToArray();
    }
}

public sealed class EconomyCommandRecorder
{
    private readonly List<RecordedEconomyCommand> _entries = [];

    public void RecordGather(long tick, int playerId, int unitId, int nodeId) =>
        _entries.Add(new RecordedEconomyCommand(
            tick, EconomyCommandKind.Gather,
            playerId, unitId, nodeId, false));

    public void RecordRefinery(
        long tick,
        int playerId,
        int nodeId,
        bool operational) =>
        _entries.Add(new RecordedEconomyCommand(
            tick, EconomyCommandKind.SetRefineryOperational,
            playerId, -1, nodeId, operational));

    public EconomyCommandLogSnapshot Capture() => new(_entries.ToArray());
}

public sealed class EconomyCommandReplay
{
    private readonly EconomyCommandLogSnapshot _log;
    private int _cursor;

    public EconomyCommandReplay(EconomyCommandLogSnapshot log) => _log = log;
    public bool Completed => _cursor >= _log.Entries.Length;

    internal void SeekToTick(long tick)
    {
        _cursor = 0;
        while (_cursor < _log.Entries.Length &&
               _log.Entries[_cursor].Tick < tick)
        {
            _cursor++;
        }
    }

    public void ApplyForCurrentTick(RtsSimulation simulation)
    {
        if (!Completed && _log.Entries[_cursor].Tick < simulation.Metrics.Tick)
        {
            throw new InvalidOperationException(
                "Economy replay cursor is behind the simulation tick.");
        }
        while (!Completed &&
               _log.Entries[_cursor].Tick == simulation.Metrics.Tick)
        {
            simulation.ApplyRecordedEconomyCommand(_log.Entries[_cursor]);
            _cursor++;
        }
    }
}
