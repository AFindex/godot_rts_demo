using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

public static class TerrainMapResourceConverter
{
    public static bool TryLoadSnapshot(
        string resourcePath,
        out TerrainMapSnapshot? snapshot,
        out TerrainMapValidationResult validation)
    {
        if (!ResourceLoader.Exists(resourcePath))
        {
            return Failure(
                TerrainMapErrorCode.MissingResourceAsset,
                $"Terrain resource does not exist: {resourcePath}",
                out snapshot,
                out validation);
        }
        var resource = GD.Load<RtsTerrainMapResource>(resourcePath);
        if (resource is null)
        {
            return Failure(
                TerrainMapErrorCode.MissingResourceAsset,
                $"Terrain resource could not be loaded: {resourcePath}",
                out snapshot,
                out validation);
        }
        return TryConvert(resource, out snapshot, out validation);
    }

    public static bool TryConvert(
        RtsTerrainMapResource resource,
        out TerrainMapSnapshot? snapshot,
        out TerrainMapValidationResult validation)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(resource.PayloadBase64);
        }
        catch (FormatException exception)
        {
            return Failure(
                TerrainMapErrorCode.InvalidPayload,
                $"Terrain Base64 payload is invalid: {exception.Message}",
                out snapshot,
                out validation);
        }
        if (!TerrainMapSnapshot.TryDeserialize(
                payload, out snapshot, out validation) || snapshot is null)
        {
            return false;
        }
        var expectedBounds = new SimRect(
            new System.Numerics.Vector2(
                resource.WorldBounds.Position.X,
                resource.WorldBounds.Position.Y),
            new System.Numerics.Vector2(
                resource.WorldBounds.End.X,
                resource.WorldBounds.End.Y));
        if (resource.FormatVersion != snapshot.FormatVersion ||
            MathF.Abs(resource.CellSize - snapshot.CellSize) > 0.0001f ||
            MathF.Abs(resource.CliffLevelHeight - snapshot.CliffLevelHeight) > 0.0001f ||
            resource.Columns != snapshot.Columns ||
            resource.Rows != snapshot.Rows ||
            resource.SurfaceCount != snapshot.Surfaces.Length ||
            resource.PayloadBytes != snapshot.CanonicalBytes.Length ||
            expectedBounds != snapshot.Bounds)
        {
            return Failure(
                TerrainMapErrorCode.InvalidPayload,
                "Terrain Inspector metadata does not match its payload.",
                out snapshot,
                out validation);
        }
        if (!string.Equals(
                resource.TerrainHash,
                snapshot.StableHashText,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                TerrainMapErrorCode.DeclaredHashMismatch,
                "Declared terrain hash does not match the payload.",
                out snapshot,
                out validation);
        }
        return true;
    }

    public static RtsTerrainMapResource FromSnapshot(
        TerrainMapSnapshot snapshot) =>
        new()
        {
            FormatVersion = snapshot.FormatVersion,
            TerrainHash = snapshot.StableHashText,
            WorldBounds = ToGodotRect(snapshot.Bounds),
            CellSize = snapshot.CellSize,
            CliffLevelHeight = snapshot.CliffLevelHeight,
            Columns = snapshot.Columns,
            Rows = snapshot.Rows,
            SurfaceCount = snapshot.Surfaces.Length,
            PayloadBytes = snapshot.CanonicalBytes.Length,
            PayloadBase64 = Convert.ToBase64String(snapshot.CanonicalBytes.Span)
        };

    private static Rect2 ToGodotRect(SimRect rect) =>
        new(
            new Vector2(rect.Min.X, rect.Min.Y),
            new Vector2(rect.Width, rect.Height));

    private static bool Failure(
        TerrainMapErrorCode code,
        string message,
        out TerrainMapSnapshot? snapshot,
        out TerrainMapValidationResult validation)
    {
        snapshot = null;
        validation = new TerrainMapValidationResult(
            [new TerrainMapValidationIssue(code, -1, message)]);
        return false;
    }
}
