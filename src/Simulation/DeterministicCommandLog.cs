using System.Buffers.Binary;
using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct RecordedSimulationCommand(
    long Tick,
    UnitOrderKind Kind,
    bool Queued,
    Vector2 TargetPosition,
    int TargetUnit,
    int TargetBuilding,
    int TargetResourceNode,
    int[] Units);

public enum CommandLogValidationCode : byte
{
    Success,
    PayloadTooShort,
    InvalidMagic,
    UnsupportedVersion,
    InvalidEntryCount,
    TruncatedEntry,
    InvalidTickOrder,
    InvalidOrderKind,
    InvalidTarget,
    InvalidUnitList,
    TrailingBytes
}

public readonly record struct CommandLogValidationResult(
    CommandLogValidationCode Code,
    int EntryIndex = -1)
{
    public bool Succeeded => Code == CommandLogValidationCode.Success;
}

public sealed class SimulationCommandLogSnapshot
{
    private const uint Magic = 0x434D5452; // RTMC in little-endian bytes.
    public const int CurrentFormatVersion = 4;
    private const int HeaderBytes = 12;
    private const int EntryFixedBytes = 34;
    private const int MaximumEntries = 1_000_000;

    public SimulationCommandLogSnapshot(RecordedSimulationCommand[] entries)
    {
        Entries = entries;
        CanonicalBytes = Serialize(entries);
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public int FormatVersion => CurrentFormatVersion;
    public RecordedSimulationCommand[] Entries { get; }
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    public static bool TryDeserialize(
        ReadOnlySpan<byte> payload,
        out SimulationCommandLogSnapshot? snapshot,
        out CommandLogValidationResult validation)
    {
        snapshot = null;
        if (payload.Length < HeaderBytes)
        {
            validation = new CommandLogValidationResult(
                CommandLogValidationCode.PayloadTooShort);
            return false;
        }

        var offset = 0;
        var magic = ReadUInt32(payload, ref offset);
        if (magic != Magic)
        {
            validation = new CommandLogValidationResult(
                CommandLogValidationCode.InvalidMagic);
            return false;
        }
        var version = ReadInt32(payload, ref offset);
        if (version != CurrentFormatVersion)
        {
            validation = new CommandLogValidationResult(
                CommandLogValidationCode.UnsupportedVersion);
            return false;
        }
        var entryCount = ReadInt32(payload, ref offset);
        if (entryCount < 0 || entryCount > MaximumEntries)
        {
            validation = new CommandLogValidationResult(
                CommandLogValidationCode.InvalidEntryCount);
            return false;
        }

        var entries = new RecordedSimulationCommand[entryCount];
        var previousTick = -1L;
        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            if (payload.Length - offset < EntryFixedBytes)
            {
                validation = new CommandLogValidationResult(
                    CommandLogValidationCode.TruncatedEntry, entryIndex);
                return false;
            }

            var tick = ReadInt64(payload, ref offset);
            var kind = (UnitOrderKind)payload[offset++];
            var queued = payload[offset++] != 0;
            var targetX = BitConverter.Int32BitsToSingle(ReadInt32(payload, ref offset));
            var targetY = BitConverter.Int32BitsToSingle(ReadInt32(payload, ref offset));
            var targetUnit = ReadInt32(payload, ref offset);
            var targetBuilding = ReadInt32(payload, ref offset);
            var targetResourceNode = ReadInt32(payload, ref offset);
            var unitCount = ReadInt32(payload, ref offset);
            if (tick < previousTick)
            {
                validation = new CommandLogValidationResult(
                    CommandLogValidationCode.InvalidTickOrder, entryIndex);
                return false;
            }
            var order = new UnitOrder(
                kind,
                new Vector2(targetX, targetY),
                targetUnit,
                targetBuilding,
                targetResourceNode);
            if (!UnitOrderContract.IsStructurallyValid(order))
            {
                validation = new CommandLogValidationResult(
                    Enum.IsDefined(kind) && kind != UnitOrderKind.None
                        ? CommandLogValidationCode.InvalidTarget
                        : CommandLogValidationCode.InvalidOrderKind,
                    entryIndex);
                return false;
            }
            if (unitCount <= 0 || unitCount > ushort.MaxValue ||
                payload.Length - offset < unitCount * sizeof(int))
            {
                validation = new CommandLogValidationResult(
                    CommandLogValidationCode.InvalidUnitList, entryIndex);
                return false;
            }

            var units = new int[unitCount];
            var previousUnit = -1;
            for (var unitIndex = 0; unitIndex < unitCount; unitIndex++)
            {
                var unit = ReadInt32(payload, ref offset);
                if (unit < 0 || unit <= previousUnit)
                {
                    validation = new CommandLogValidationResult(
                        CommandLogValidationCode.InvalidUnitList, entryIndex);
                    return false;
                }
                units[unitIndex] = unit;
                previousUnit = unit;
            }

            entries[entryIndex] = new RecordedSimulationCommand(
                tick,
                kind,
                queued,
                new Vector2(targetX, targetY),
                targetUnit,
                targetBuilding,
                targetResourceNode,
                units);
            previousTick = tick;
        }

        if (offset != payload.Length)
        {
            validation = new CommandLogValidationResult(
                CommandLogValidationCode.TrailingBytes);
            return false;
        }

        snapshot = new SimulationCommandLogSnapshot(entries);
        validation = new CommandLogValidationResult(CommandLogValidationCode.Success);
        return true;
    }

    private static byte[] Serialize(RecordedSimulationCommand[] entries)
    {
        var byteCount = HeaderBytes;
        for (var index = 0; index < entries.Length; index++)
        {
            byteCount = checked(
                byteCount + EntryFixedBytes + entries[index].Units.Length * sizeof(int));
        }

        var payload = new byte[byteCount];
        var offset = 0;
        WriteUInt32(payload, ref offset, Magic);
        WriteInt32(payload, ref offset, CurrentFormatVersion);
        WriteInt32(payload, ref offset, entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            WriteInt64(payload, ref offset, entry.Tick);
            payload[offset++] = (byte)entry.Kind;
            payload[offset++] = entry.Queued ? (byte)1 : (byte)0;
            WriteInt32(
                payload, ref offset, BitConverter.SingleToInt32Bits(entry.TargetPosition.X));
            WriteInt32(
                payload, ref offset, BitConverter.SingleToInt32Bits(entry.TargetPosition.Y));
            WriteInt32(payload, ref offset, entry.TargetUnit);
            WriteInt32(payload, ref offset, entry.TargetBuilding);
            WriteInt32(payload, ref offset, entry.TargetResourceNode);
            WriteInt32(payload, ref offset, entry.Units.Length);
            for (var unitIndex = 0; unitIndex < entry.Units.Length; unitIndex++)
            {
                WriteInt32(payload, ref offset, entry.Units[unitIndex]);
            }
        }
        return payload;
    }

    private static int ReadInt32(ReadOnlySpan<byte> source, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);
        return value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);
        offset += sizeof(uint);
        return value;
    }

    private static long ReadInt64(ReadOnlySpan<byte> source, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(source[offset..]);
        offset += sizeof(long);
        return value;
    }

    private static void WriteInt32(Span<byte> destination, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], value);
        offset += sizeof(int);
    }

    private static void WriteUInt32(Span<byte> destination, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], value);
        offset += sizeof(uint);
    }

    private static void WriteInt64(Span<byte> destination, ref int offset, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination[offset..], value);
        offset += sizeof(long);
    }
}

public sealed class SimulationCommandRecorder
{
    private readonly List<RecordedSimulationCommand> _entries = [];

    public void Record(
        long tick,
        ReadOnlySpan<int> units,
        UnitOrder order,
        bool queued)
    {
        var canonicalUnits = units.ToArray();
        Array.Sort(canonicalUnits);
        for (var index = 1; index < canonicalUnits.Length; index++)
        {
            if (canonicalUnits[index] == canonicalUnits[index - 1])
            {
                throw new InvalidOperationException(
                    $"Recorded command contains duplicate unit {canonicalUnits[index]}.");
            }
        }
        _entries.Add(new RecordedSimulationCommand(
            tick,
            order.Kind,
            queued,
            order.TargetPosition,
            order.TargetUnit,
            order.TargetBuilding,
            order.TargetResourceNode,
            canonicalUnits));
    }

    public SimulationCommandLogSnapshot Capture() =>
        new(_entries.ToArray());
}

public sealed class SimulationCommandReplay
{
    private readonly SimulationCommandLogSnapshot _log;
    private int _cursor;

    public SimulationCommandReplay(SimulationCommandLogSnapshot log)
    {
        _log = log;
    }

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
                $"Replay cursor is behind simulation tick {simulation.Metrics.Tick}.");
        }

        while (!Completed &&
               _log.Entries[_cursor].Tick == simulation.Metrics.Tick)
        {
            simulation.ApplyRecordedCommand(_log.Entries[_cursor]);
            _cursor++;
        }
    }
}

public struct StableHash64
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private ulong _value;

    public StableHash64()
    {
        _value = OffsetBasis;
    }

    public readonly ulong Value => _value;

    public void Add(byte value)
    {
        _value ^= value;
        _value *= Prime;
    }

    public void Add(bool value) => Add(value ? (byte)1 : (byte)0);

    public void Add(int value)
    {
        Add((byte)value);
        Add((byte)(value >> 8));
        Add((byte)(value >> 16));
        Add((byte)(value >> 24));
    }

    public void Add(long value)
    {
        Add((int)value);
        Add((int)(value >> 32));
    }

    public void Add(float value) => Add(BitConverter.SingleToInt32Bits(value));

    public void Add(Vector2 value)
    {
        Add(value.X);
        Add(value.Y);
    }

    public static ulong Compute(ReadOnlySpan<byte> payload)
    {
        var hash = new StableHash64();
        for (var index = 0; index < payload.Length; index++)
        {
            hash.Add(payload[index]);
        }
        return hash.Value;
    }
}
