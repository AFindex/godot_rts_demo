using System.Numerics;

namespace RtsDemo.Simulation;

public enum ClearanceBakeErrorCode
{
    None = 0,
    UnsupportedFormatVersion = 3001,
    InvalidHeader = 3002,
    InvalidSourceNavigationHash = 3003,
    InvalidGridLayout = 3101,
    InvalidLayerCount = 3201,
    InvalidLayerClass = 3202,
    InvalidLayerRadius = 3203,
    InvalidLayerPayload = 3204,
    NonDenseComponentId = 3205,
    TrailingPayload = 3301,
    MissingResourceAsset = 3401,
    InvalidResourcePayload = 3402,
    SourceNavigationMismatch = 3403,
    DeclaredHashMismatch = 3404,
    SourceTerrainMismatch = 3405
}

public readonly record struct ClearanceBakeValidationIssue(
    ClearanceBakeErrorCode Code,
    int LayerIndex,
    string Message);

public sealed class ClearanceBakeValidationResult
{
    public ClearanceBakeValidationResult(ClearanceBakeValidationIssue[] issues)
    {
        Issues = issues;
    }

    public bool IsValid => Issues.Length == 0;
    public ClearanceBakeValidationIssue[] Issues { get; }
    public ClearanceBakeErrorCode FirstError =>
        IsValid ? ClearanceBakeErrorCode.None : Issues[0].Code;
}

internal static class ClearanceBakeCodec
{
    private const int Magic = 0x42434C52;

    public static byte[] Serialize(
        int formatVersion,
        ulong sourceNavigationHash,
        ulong sourceTerrainHash,
        SimRect bounds,
        float cellSize,
        int columns,
        int rows,
        int chunkSizeCells,
        ReadOnlySpan<ClearanceBakeLayerSnapshot> layers)
    {
        using var stream = new MemoryStream(64 * 1024);
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(formatVersion);
        writer.Write(sourceNavigationHash);
        writer.Write(sourceTerrainHash);
        WriteRect(writer, bounds);
        WriteFloat(writer, cellSize);
        writer.Write(columns);
        writer.Write(rows);
        writer.Write(chunkSizeCells);
        writer.Write(layers.Length);
        for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            var layer = layers[layerIndex];
            writer.Write((int)layer.MovementClass);
            WriteFloat(writer, layer.NavigationRadius);
            writer.Write(layer.WalkableBits.Length);
            writer.Write(layer.WalkableBits);
            writer.Write(layer.ComponentIds.Length);
            var componentIds = layer.ComponentIds;
            for (var node = 0; node < componentIds.Length; node++)
            {
                writer.Write(componentIds[node]);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> data,
        out ClearanceBakeSnapshot? snapshot,
        out ClearanceBakeValidationResult validation)
    {
        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Magic)
            {
                return Failure(
                    ClearanceBakeErrorCode.InvalidHeader,
                    "Clearance bake magic header is invalid.",
                    out snapshot,
                    out validation);
            }

            var formatVersion = reader.ReadInt32();
            if (formatVersion != ClearanceBakeSnapshot.CurrentFormatVersion)
            {
                return Failure(
                    ClearanceBakeErrorCode.UnsupportedFormatVersion,
                    $"Expected clearance bake format {ClearanceBakeSnapshot.CurrentFormatVersion}, got {formatVersion}.",
                    out snapshot,
                    out validation);
            }

            var sourceHash = reader.ReadUInt64();
            if (sourceHash == 0UL)
            {
                return Failure(
                    ClearanceBakeErrorCode.InvalidSourceNavigationHash,
                    "Source navigation hash must be non-zero.",
                    out snapshot,
                    out validation);
            }
            var sourceTerrainHash = reader.ReadUInt64();

            var bounds = ReadRect(reader);
            var cellSize = ReadFloat(reader);
            var columns = reader.ReadInt32();
            var rows = reader.ReadInt32();
            var chunkSizeCells = reader.ReadInt32();
            if (!IsValidLayout(bounds, cellSize, columns, rows, chunkSizeCells))
            {
                return Failure(
                    ClearanceBakeErrorCode.InvalidGridLayout,
                    "Clearance bake grid layout is invalid.",
                    out snapshot,
                    out validation);
            }

            var layerCount = reader.ReadInt32();
            if (layerCount != 3)
            {
                return Failure(
                    ClearanceBakeErrorCode.InvalidLayerCount,
                    $"Expected 3 clearance layers, got {layerCount}.",
                    out snapshot,
                    out validation);
            }

            var nodeCount = checked(columns * rows);
            var layers = new ClearanceBakeLayerSnapshot[layerCount];
            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                if (!TryReadLayer(
                        reader,
                        bounds,
                        cellSize,
                        columns,
                        nodeCount,
                        layerIndex,
                        out layers[layerIndex],
                        out var issue))
                {
                    snapshot = null;
                    validation = new ClearanceBakeValidationResult([issue]);
                    return false;
                }
            }

            if (stream.Position != stream.Length)
            {
                return Failure(
                    ClearanceBakeErrorCode.TrailingPayload,
                    "Clearance bake has trailing bytes.",
                    out snapshot,
                    out validation);
            }

            var canonical = data.ToArray();
            snapshot = new ClearanceBakeSnapshot(
                formatVersion,
                sourceHash,
                sourceTerrainHash,
                bounds,
                cellSize,
                columns,
                rows,
                chunkSizeCells,
                layers,
                canonical);
            validation = new ClearanceBakeValidationResult([]);
            return true;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or OverflowException)
        {
            return Failure(
                ClearanceBakeErrorCode.InvalidLayerPayload,
                $"Clearance bake payload is truncated or invalid: {exception.Message}",
                out snapshot,
                out validation);
        }
    }

    public static ulong ComputeStableHash(ReadOnlySpan<byte> data)
    {
        var hash = 14695981039346656037UL;
        for (var index = 0; index < data.Length; index++)
        {
            hash ^= data[index];
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static bool TryReadLayer(
        BinaryReader reader,
        SimRect bounds,
        float cellSize,
        int columns,
        int nodeCount,
        int layerIndex,
        out ClearanceBakeLayerSnapshot layer,
        out ClearanceBakeValidationIssue issue)
    {
        var movementClass = (MovementClass)reader.ReadInt32();
        if (!Enum.IsDefined(movementClass) || (int)movementClass != layerIndex)
        {
            return LayerFailure(
                ClearanceBakeErrorCode.InvalidLayerClass,
                layerIndex,
                "Clearance layer IDs must be dense Small/Medium/Large.",
                out layer,
                out issue);
        }

        var navigationRadius = ReadFloat(reader);
        var expectedRadius = MovementClearance.ForClass(
            movementClass).NavigationRadius;
        if (!float.IsFinite(navigationRadius) ||
            MathF.Abs(navigationRadius - expectedRadius) > 0.0001f)
        {
            return LayerFailure(
                ClearanceBakeErrorCode.InvalidLayerRadius,
                layerIndex,
                $"Layer {movementClass} radius must be {expectedRadius}.",
                out layer,
                out issue);
        }

        var bitLength = reader.ReadInt32();
        if (bitLength != (nodeCount + 7) / 8)
        {
            return LayerFailure(
                ClearanceBakeErrorCode.InvalidLayerPayload,
                layerIndex,
                "Walkable bit payload length does not match grid.",
                out layer,
                out issue);
        }

        var bits = reader.ReadBytes(bitLength);
        if (bits.Length != bitLength || reader.ReadInt32() != nodeCount)
        {
            return LayerFailure(
                ClearanceBakeErrorCode.InvalidLayerPayload,
                layerIndex,
                "Component payload length does not match grid.",
                out layer,
                out issue);
        }

        var componentIds = new int[nodeCount];
        for (var node = 0; node < nodeCount; node++)
        {
            componentIds[node] = reader.ReadInt32();
        }

        if (!TryBuildComponents(
                bounds,
                cellSize,
                columns,
                bits,
                componentIds,
                out var components))
        {
            return LayerFailure(
                ClearanceBakeErrorCode.NonDenseComponentId,
                layerIndex,
                "Walkable cells must reference dense component IDs.",
                out layer,
                out issue);
        }

        layer = new ClearanceBakeLayerSnapshot(
            movementClass,
            navigationRadius,
            bits,
            componentIds,
            components);
        issue = default;
        return true;
    }

    private static bool TryBuildComponents(
        SimRect bounds,
        float cellSize,
        int columns,
        byte[] walkableBits,
        int[] componentIds,
        out NavigationComponentSummary[] components)
    {
        var maximumId = -1;
        for (var node = 0; node < componentIds.Length; node++)
        {
            var walkable = (walkableBits[node >> 3] &
                            (1 << (node & 7))) != 0;
            if (!walkable && componentIds[node] != -1 ||
                walkable && componentIds[node] < 0)
            {
                components = [];
                return false;
            }

            maximumId = Math.Max(maximumId, componentIds[node]);
        }

        var counts = new int[maximumId + 1];
        var minimum = new Vector2[maximumId + 1];
        var maximum = new Vector2[maximumId + 1];
        Array.Fill(minimum, new Vector2(float.PositiveInfinity));
        Array.Fill(maximum, new Vector2(float.NegativeInfinity));
        for (var node = 0; node < componentIds.Length; node++)
        {
            var component = componentIds[node];
            if (component < 0)
            {
                continue;
            }

            counts[component]++;
            var column = node % columns;
            var row = node / columns;
            var cellMinimum = bounds.Min +
                              new Vector2(column * cellSize, row * cellSize);
            var cellMaximum = Vector2.Min(
                bounds.Max, cellMinimum + new Vector2(cellSize));
            minimum[component] = Vector2.Min(minimum[component], cellMinimum);
            maximum[component] = Vector2.Max(maximum[component], cellMaximum);
        }

        components = new NavigationComponentSummary[counts.Length];
        for (var component = 0; component < counts.Length; component++)
        {
            if (counts[component] == 0)
            {
                components = [];
                return false;
            }

            components[component] = new NavigationComponentSummary(
                component,
                counts[component],
                new SimRect(minimum[component], maximum[component]));
        }

        return true;
    }

    private static bool IsValidLayout(
        SimRect bounds,
        float cellSize,
        int columns,
        int rows,
        int chunkSizeCells) =>
        IsFinite(bounds.Min) && IsFinite(bounds.Max) &&
        bounds.Width > 0f && bounds.Height > 0f &&
        float.IsFinite(cellSize) && cellSize > 0f &&
        columns == Math.Max(
            1, (int)MathF.Ceiling(bounds.Width / cellSize)) &&
        rows == Math.Max(
            1, (int)MathF.Ceiling(bounds.Height / cellSize)) &&
        chunkSizeCells > 0;

    private static bool Failure(
        ClearanceBakeErrorCode code,
        string message,
        out ClearanceBakeSnapshot? snapshot,
        out ClearanceBakeValidationResult validation)
    {
        snapshot = null;
        validation = new ClearanceBakeValidationResult(
            [new ClearanceBakeValidationIssue(code, -1, message)]);
        return false;
    }

    private static bool LayerFailure(
        ClearanceBakeErrorCode code,
        int layerIndex,
        string message,
        out ClearanceBakeLayerSnapshot layer,
        out ClearanceBakeValidationIssue issue)
    {
        layer = null!;
        issue = new ClearanceBakeValidationIssue(code, layerIndex, message);
        return false;
    }

    private static void WriteRect(BinaryWriter writer, SimRect rect)
    {
        WriteFloat(writer, rect.Min.X);
        WriteFloat(writer, rect.Min.Y);
        WriteFloat(writer, rect.Max.X);
        WriteFloat(writer, rect.Max.Y);
    }

    private static SimRect ReadRect(BinaryReader reader) =>
        new(
            new Vector2(ReadFloat(reader), ReadFloat(reader)),
            new Vector2(ReadFloat(reader), ReadFloat(reader)));

    private static void WriteFloat(BinaryWriter writer, float value) =>
        writer.Write(BitConverter.SingleToInt32Bits(value));

    private static float ReadFloat(BinaryReader reader) =>
        BitConverter.Int32BitsToSingle(reader.ReadInt32());

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
