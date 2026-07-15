using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

public static class ClearanceBakeResourceConverter
{
    public static bool TryLoadSnapshot(
        string resourcePath,
        ulong expectedNavigationHash,
        out ClearanceBakeSnapshot? snapshot,
        out ClearanceBakeValidationResult validation)
        => TryLoadSnapshot(
            resourcePath, expectedNavigationHash, 0UL,
            out snapshot, out validation);

    public static bool TryLoadSnapshot(
        string resourcePath,
        ulong expectedNavigationHash,
        ulong expectedTerrainHash,
        out ClearanceBakeSnapshot? snapshot,
        out ClearanceBakeValidationResult validation)
    {
        if (!ResourceLoader.Exists(resourcePath))
        {
            return Failure(
                ClearanceBakeErrorCode.MissingResourceAsset,
                $"Clearance bake resource does not exist: {resourcePath}",
                out snapshot,
                out validation);
        }

        var resource = GD.Load<RtsClearanceBakeResource>(resourcePath);
        if (resource is null)
        {
            return Failure(
                ClearanceBakeErrorCode.MissingResourceAsset,
                $"Clearance bake resource could not be loaded: {resourcePath}",
                out snapshot,
                out validation);
        }

        return TryConvert(
            resource, expectedNavigationHash, expectedTerrainHash,
            out snapshot, out validation);
    }

    public static bool TryConvert(
        RtsClearanceBakeResource resource,
        ulong expectedNavigationHash,
        out ClearanceBakeSnapshot? snapshot,
        out ClearanceBakeValidationResult validation)
        => TryConvert(
            resource, expectedNavigationHash, 0UL,
            out snapshot, out validation);

    public static bool TryConvert(
        RtsClearanceBakeResource resource,
        ulong expectedNavigationHash,
        ulong expectedTerrainHash,
        out ClearanceBakeSnapshot? snapshot,
        out ClearanceBakeValidationResult validation)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(resource.PayloadBase64);
        }
        catch (FormatException exception)
        {
            return Failure(
                ClearanceBakeErrorCode.InvalidResourcePayload,
                $"Clearance bake Base64 payload is invalid: {exception.Message}",
                out snapshot,
                out validation);
        }

        if (!ClearanceBakeSnapshot.TryDeserialize(
                payload, out snapshot, out validation) || snapshot is null)
        {
            return false;
        }

        if (snapshot.FormatVersion != resource.FormatVersion ||
            MathF.Abs(snapshot.CellSize - resource.CellSize) > 0.0001f ||
            snapshot.ChunkSizeCells != resource.ChunkSizeCells ||
            snapshot.Columns != resource.Columns ||
            snapshot.Rows != resource.Rows ||
            snapshot.CanonicalBytes.Length != resource.PayloadBytes)
        {
            return Failure(
                ClearanceBakeErrorCode.InvalidResourcePayload,
                "Clearance bake Inspector metadata does not match its payload.",
                out snapshot,
                out validation);
        }

        if (snapshot.SourceNavigationHash != expectedNavigationHash)
        {
            return Failure(
                ClearanceBakeErrorCode.SourceNavigationMismatch,
                $"Bake source {snapshot.SourceNavigationHashText} does not match navigation {expectedNavigationHash:X16}.",
                out snapshot,
                out validation);
        }

        if (snapshot.SourceTerrainHash != expectedTerrainHash)
        {
            return Failure(
                ClearanceBakeErrorCode.SourceTerrainMismatch,
                $"Bake terrain source {snapshot.SourceTerrainHashText} does not match terrain {expectedTerrainHash:X16}.",
                out snapshot,
                out validation);
        }

        if (!string.Equals(
                resource.SourceNavigationHash,
                snapshot.SourceNavigationHashText,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                resource.SourceTerrainHash,
                snapshot.SourceTerrainHashText,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                resource.BakeHash,
                snapshot.StableHashText,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                ClearanceBakeErrorCode.DeclaredHashMismatch,
                "Declared clearance bake hashes do not match the payload.",
                out snapshot,
                out validation);
        }

        return true;
    }

    public static RtsClearanceBakeResource FromSnapshot(
        ClearanceBakeSnapshot snapshot) =>
        new()
        {
            FormatVersion = snapshot.FormatVersion,
            SourceNavigationHash = snapshot.SourceNavigationHashText,
            SourceTerrainHash = snapshot.SourceTerrainHashText,
            BakeHash = snapshot.StableHashText,
            CellSize = snapshot.CellSize,
            ChunkSizeCells = snapshot.ChunkSizeCells,
            Columns = snapshot.Columns,
            Rows = snapshot.Rows,
            PayloadBytes = snapshot.CanonicalBytes.Length,
            PayloadBase64 = Convert.ToBase64String(
                snapshot.CanonicalBytes.Span)
        };

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
}
