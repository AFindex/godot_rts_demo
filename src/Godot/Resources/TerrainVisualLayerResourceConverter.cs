using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

public static class TerrainVisualLayerResourceConverter
{
    public static bool TryLoad(
        string resourcePath,
        TerrainMapSnapshot source,
        out TerrainVisualLayerMap? visualMap,
        out TerrainVisualLayerValidationResult validation)
    {
        if (!ResourceLoader.Exists(resourcePath))
        {
            return Failure(
                TerrainVisualLayerErrorCode.MissingResourceAsset,
                $"Terrain visual layer resource does not exist: {resourcePath}",
                out visualMap, out validation);
        }
        var resource = GD.Load<RtsTerrainVisualLayerResource>(resourcePath);
        if (resource is null)
        {
            return Failure(
                TerrainVisualLayerErrorCode.MissingResourceAsset,
                $"Terrain visual layer resource could not be loaded: {resourcePath}",
                out visualMap, out validation);
        }
        return TryConvert(resource, source, out visualMap, out validation);
    }

    public static bool TryConvert(
        RtsTerrainVisualLayerResource resource,
        TerrainMapSnapshot source,
        out TerrainVisualLayerMap? visualMap,
        out TerrainVisualLayerValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(source);
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(resource.PayloadBase64);
        }
        catch (FormatException exception)
        {
            return Failure(
                TerrainVisualLayerErrorCode.InvalidPayload,
                $"Terrain visual layer Base64 payload is invalid: {exception.Message}",
                out visualMap, out validation);
        }
        if (!TerrainVisualLayerMap.TryDeserialize(
                payload, source, out visualMap, out validation) ||
            visualMap is null)
        {
            return false;
        }
        if (resource.FormatVersion != visualMap.FormatVersion ||
            resource.PointColumns != visualMap.PointColumns ||
            resource.PointRows != visualMap.PointRows ||
            resource.PointCount != visualMap.PointCount ||
            resource.PayloadBytes != visualMap.CanonicalBytes.Length ||
            !string.Equals(
                resource.SourceTerrainHash,
                visualMap.SourceTerrainHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                TerrainVisualLayerErrorCode.InvalidPayload,
                "Terrain visual layer Inspector metadata does not match its payload.",
                out visualMap, out validation);
        }
        if (!string.Equals(
                resource.VisualHash,
                visualMap.StableHashText,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                TerrainVisualLayerErrorCode.DeclaredHashMismatch,
                "Declared terrain visual layer hash does not match the payload.",
                out visualMap, out validation);
        }
        return true;
    }

    public static RtsTerrainVisualLayerResource FromMap(
        TerrainVisualLayerMap visualMap)
    {
        ArgumentNullException.ThrowIfNull(visualMap);
        return new RtsTerrainVisualLayerResource
        {
            FormatVersion = visualMap.FormatVersion,
            VisualHash = visualMap.StableHashText,
            SourceTerrainHash = visualMap.SourceTerrainHash,
            PointColumns = visualMap.PointColumns,
            PointRows = visualMap.PointRows,
            PointCount = visualMap.PointCount,
            PayloadBytes = visualMap.CanonicalBytes.Length,
            PayloadBase64 = Convert.ToBase64String(
                visualMap.CanonicalBytes.Span)
        };
    }

    private static bool Failure(
        TerrainVisualLayerErrorCode code,
        string message,
        out TerrainVisualLayerMap? visualMap,
        out TerrainVisualLayerValidationResult validation)
    {
        visualMap = null;
        validation = new TerrainVisualLayerValidationResult(
            [new TerrainVisualLayerValidationIssue(code, -1, message)]);
        return false;
    }
}
