using System.Buffers.Binary;

namespace RtsDemo.Simulation;

public enum ReplayCheckpointValidationCode : byte
{
    Success,
    InvalidLength,
    InvalidMagic,
    UnsupportedVersion,
    InvalidTick,
    UnsupportedStateHashVersion,
    PackageMismatch,
    StateMismatch,
    ReplayIncomplete
}

public readonly record struct ReplayCheckpointValidationResult(
    ReplayCheckpointValidationCode Code)
{
    public bool Succeeded => Code == ReplayCheckpointValidationCode.Success;
}

/// <summary>
/// Versioned checkpoint contract bound to one canonical replay package.
/// Version 1 restores by deterministic seek from tick zero; it intentionally
/// does not expose or persist simulation implementation arrays.
/// </summary>
public sealed class SimulationReplayCheckpointSnapshot
{
    private const uint Magic = 0x50435452; // RTCP in little-endian bytes.
    private const int PayloadBytes = 36;
    public const int CurrentFormatVersion = 1;

    public SimulationReplayCheckpointSnapshot(
        long tick,
        ulong packageHash,
        ulong stateHash)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }
        Tick = tick;
        PackageHash = packageHash;
        StateHash = stateHash;
        CanonicalBytes = Serialize();
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public int FormatVersion => CurrentFormatVersion;
    public int StateHashFormatVersion => SimulationStateHasher.CurrentFormatVersion;
    public long Tick { get; }
    public ulong PackageHash { get; }
    public ulong StateHash { get; }
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> payload,
        out SimulationReplayCheckpointSnapshot? snapshot,
        out ReplayCheckpointValidationResult validation)
    {
        snapshot = null;
        if (payload.Length != PayloadBytes)
        {
            validation = new ReplayCheckpointValidationResult(
                ReplayCheckpointValidationCode.InvalidLength);
            return false;
        }
        var offset = 0;
        if (ReadUInt32(payload, ref offset) != Magic)
        {
            validation = new ReplayCheckpointValidationResult(
                ReplayCheckpointValidationCode.InvalidMagic);
            return false;
        }
        if (ReadInt32(payload, ref offset) != CurrentFormatVersion)
        {
            validation = new ReplayCheckpointValidationResult(
                ReplayCheckpointValidationCode.UnsupportedVersion);
            return false;
        }
        var stateHashVersion = ReadInt32(payload, ref offset);
        if (stateHashVersion != SimulationStateHasher.CurrentFormatVersion)
        {
            validation = new ReplayCheckpointValidationResult(
                ReplayCheckpointValidationCode.UnsupportedStateHashVersion);
            return false;
        }
        var tick = ReadInt64(payload, ref offset);
        if (tick < 0)
        {
            validation = new ReplayCheckpointValidationResult(
                ReplayCheckpointValidationCode.InvalidTick);
            return false;
        }
        var packageHash = ReadUInt64(payload, ref offset);
        var stateHash = ReadUInt64(payload, ref offset);
        snapshot = new SimulationReplayCheckpointSnapshot(
            tick, packageHash, stateHash);
        validation = new ReplayCheckpointValidationResult(
            ReplayCheckpointValidationCode.Success);
        return true;
    }

    private byte[] Serialize()
    {
        var payload = new byte[PayloadBytes];
        var offset = 0;
        WriteUInt32(payload, ref offset, Magic);
        WriteInt32(payload, ref offset, CurrentFormatVersion);
        WriteInt32(payload, ref offset, StateHashFormatVersion);
        WriteInt64(payload, ref offset, Tick);
        WriteUInt64(payload, ref offset, PackageHash);
        WriteUInt64(payload, ref offset, StateHash);
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

    private static ulong ReadUInt64(ReadOnlySpan<byte> source, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(source[offset..]);
        offset += sizeof(ulong);
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

    private static void WriteUInt64(Span<byte> destination, ref int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination[offset..], value);
        offset += sizeof(ulong);
    }
}

public static class SimulationReplayCheckpointFactory
{
    public static bool TryRestore(
        SimulationReplayCheckpointSnapshot checkpoint,
        SimulationReplayPackageSnapshot package,
        NavigationMapSnapshot navigation,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake,
        out RtsSimulation? simulation,
        out SimulationReplayPackageRunner? runner,
        out ReplayCheckpointValidationResult validation)
    {
        simulation = null;
        runner = null;
        if (checkpoint.PackageHash != package.StableHash)
        {
            validation = new ReplayCheckpointValidationResult(
                ReplayCheckpointValidationCode.PackageMismatch);
            return false;
        }
        if (!SimulationReplayPackageFactory.TryCreateSimulation(
                package,
                navigation,
                gameplayProfiles,
                clearanceBake,
                out simulation,
                out _))
        {
            validation = new ReplayCheckpointValidationResult(
                ReplayCheckpointValidationCode.PackageMismatch);
            return false;
        }

        runner = new SimulationReplayPackageRunner(package);
        while (simulation!.Metrics.Tick < checkpoint.Tick)
        {
            runner.ApplyForCurrentTick(simulation);
            simulation.Tick(1f / 60f);
        }
        if (simulation.ComputeStateHash() != checkpoint.StateHash)
        {
            simulation = null;
            runner = null;
            validation = new ReplayCheckpointValidationResult(
                ReplayCheckpointValidationCode.StateMismatch);
            return false;
        }
        validation = new ReplayCheckpointValidationResult(
            ReplayCheckpointValidationCode.Success);
        return true;
    }
}
