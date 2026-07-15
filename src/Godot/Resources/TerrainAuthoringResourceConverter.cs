using Godot;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime.Resources;

public static class TerrainAuthoringResourceConverter
{
    public static bool TryConvert(
        RtsTerrainAuthoringResource resource,
        out TerrainAuthoringDocument? document,
        out TerrainAuthoringValidationResult validation)
    {
        var issues = new List<TerrainAuthoringIssue>();
        if (resource.FormatVersion != RtsTerrainAuthoringResource.CurrentFormatVersion)
        {
            issues.Add(ResourceError(
                $"Expected authoring format " +
                $"{RtsTerrainAuthoringResource.CurrentFormatVersion}, got " +
                $"{resource.FormatVersion}."));
        }
        if (resource.CellSize <= 0f || !float.IsFinite(resource.CellSize) ||
            resource.WorldBounds.Size.X <= 0f ||
            resource.WorldBounds.Size.Y <= 0f)
        {
            issues.Add(ResourceError("Authoring bounds and cell size must be positive."));
        }
        var expected = resource.CellCount;
        if (resource.DataColumns != resource.Columns ||
            resource.DataRows != resource.Rows)
        {
            issues.Add(ResourceError(
                $"Cell arrays are {resource.DataColumns}x{resource.DataRows}, " +
                $"but bounds/cell size require {resource.Columns}x{resource.Rows}. " +
                "Use Initialize / Resize Cell Arrays."));
        }
        ValidateLength(resource.CliffLevels.Length, expected, "CliffLevels", issues);
        ValidateLength(resource.SurfaceIds.Length, expected, "SurfaceIds", issues);
        ValidateLength(resource.PathingMasks.Length, expected, "PathingMasks", issues);
        ValidateLength(resource.CellFlags.Length, expected, "CellFlags", issues);
        ValidateLength(resource.RampDirections.Length, expected, "RampDirections", issues);
        var surfaces = new TerrainSurfaceDefinition[resource.Surfaces.Count];
        for (var index = 0; index < surfaces.Length; index++)
        {
            var source = resource.Surfaces[index];
            if (source is null || source.Id is < 0 or > ushort.MaxValue)
            {
                issues.Add(ResourceError($"Surface {index} is null or has invalid ID."));
                continue;
            }
            surfaces[index] = new TerrainSurfaceDefinition(
                (ushort)source.Id, source.MaterialKey, source.DisplayName);
        }
        if (issues.Count > 0)
        {
            document = null;
            validation = new TerrainAuthoringValidationResult([.. issues]);
            return false;
        }
        var cells = new TerrainCell[expected];
        for (var index = 0; index < cells.Length; index++)
        {
            if (resource.SurfaceIds[index] is < 0 or > ushort.MaxValue)
            {
                issues.Add(CellError(index, resource.Columns,
                    $"Surface ID {resource.SurfaceIds[index]} is out of range."));
                continue;
            }
            cells[index] = new TerrainCell(
                resource.CliffLevels[index],
                (ushort)resource.SurfaceIds[index],
                (TerrainPathing)resource.PathingMasks[index],
                (TerrainCellFlags)resource.CellFlags[index],
                (TerrainRampDirection)resource.RampDirections[index]);
        }
        var anchors = new TerrainAuthoringAnchor[resource.Anchors.Count];
        for (var index = 0; index < anchors.Length; index++)
        {
            var source = resource.Anchors[index];
            if (source is null)
            {
                issues.Add(ResourceError($"Anchor {index} is null."));
                continue;
            }
            anchors[index] = new TerrainAuthoringAnchor(
                source.Id,
                source.Kind,
                new NVector2(source.Position.X, source.Position.Y),
                source.RequiredRadius);
        }
        if (issues.Count > 0)
        {
            document = null;
            validation = new TerrainAuthoringValidationResult([.. issues]);
            return false;
        }
        try
        {
            document = new TerrainAuthoringDocument(
                new SimRect(
                    new NVector2(
                        resource.WorldBounds.Position.X,
                        resource.WorldBounds.Position.Y),
                    new NVector2(
                        resource.WorldBounds.End.X,
                        resource.WorldBounds.End.Y)),
                resource.CellSize,
                resource.CliffLevelHeight,
                surfaces,
                cells,
                anchors);
            validation = new TerrainAuthoringValidationResult([]);
            return true;
        }
        catch (ArgumentException exception)
        {
            document = null;
            validation = new TerrainAuthoringValidationResult([
                ResourceError(exception.Message)
            ]);
            return false;
        }
    }

    public static bool TryExport(
        RtsTerrainAuthoringResource resource,
        out TerrainMapSnapshot? snapshot,
        out TerrainAuthoringValidationResult validation)
    {
        if (!TryConvert(resource, out var document, out validation) ||
            document is null)
        {
            snapshot = null;
            return false;
        }
        return document.TryExport(
            new TerrainAuthoringValidationSettings(
                resource.MinimumGroundIslandCells,
                resource.MinimumGroundRadius),
            out snapshot,
            out validation);
    }

    public static RtsTerrainAuthoringResource FromDocument(
        TerrainAuthoringDocument document,
        string runtimeOutputPath = "res://data/authored_terrain.tres")
    {
        var resource = new RtsTerrainAuthoringResource
        {
            WorldBounds = new Rect2(
                document.Bounds.Min.X,
                document.Bounds.Min.Y,
                document.Bounds.Width,
                document.Bounds.Height),
            CellSize = document.CellSize,
            CliffLevelHeight = document.CliffLevelHeight,
            DataColumns = document.Columns,
            DataRows = document.Rows,
            RuntimeOutputPath = runtimeOutputPath
        };
        foreach (var surface in document.Surfaces)
        {
            resource.Surfaces.Add(new RtsTerrainSurfaceAuthoringResource
            {
                Id = surface.Id,
                MaterialKey = surface.MaterialKey,
                DisplayName = surface.DisplayName
            });
        }
        foreach (var anchor in document.Anchors)
        {
            resource.Anchors.Add(new RtsTerrainAnchorAuthoringResource
            {
                Id = anchor.Id,
                Kind = anchor.Kind,
                Position = new Vector2(anchor.Position.X, anchor.Position.Y),
                RequiredRadius = anchor.RequiredRadius
            });
        }
        WriteCells(resource, document.Cells);
        return resource;
    }

    public static void WriteCells(
        RtsTerrainAuthoringResource resource,
        ReadOnlySpan<TerrainCell> cells)
    {
        var count = cells.Length;
        var levels = new byte[count];
        var surfaces = new int[count];
        var pathing = new byte[count];
        var flags = new byte[count];
        var ramps = new byte[count];
        for (var index = 0; index < count; index++)
        {
            var cell = cells[index];
            levels[index] = cell.CliffLevel;
            surfaces[index] = cell.SurfaceId;
            pathing[index] = (byte)cell.Pathing;
            flags[index] = (byte)cell.Flags;
            ramps[index] = (byte)cell.RampDirection;
        }
        resource.CliffLevels = levels;
        resource.SurfaceIds = surfaces;
        resource.PathingMasks = pathing;
        resource.CellFlags = flags;
        resource.RampDirections = ramps;
        resource.EmitChanged();
    }

    public static TerrainAuthoringResizeResult ResizeCellArrays(
        RtsTerrainAuthoringResource resource,
        TerrainCell fill)
    {
        var oldColumns = Math.Max(0, resource.DataColumns);
        var oldRows = Math.Max(0, resource.DataRows);
        var oldCount = oldColumns * oldRows;
        var oldValid = oldCount == resource.CliffLevels.Length &&
                       oldCount == resource.SurfaceIds.Length &&
                       oldCount == resource.PathingMasks.Length &&
                       oldCount == resource.CellFlags.Length &&
                       oldCount == resource.RampDirections.Length;
        var newColumns = resource.Columns;
        var newRows = resource.Rows;
        var cells = Enumerable.Repeat(fill, newColumns * newRows).ToArray();
        var copied = 0;
        if (oldValid)
        {
            var copyColumns = Math.Min(oldColumns, newColumns);
            var copyRows = Math.Min(oldRows, newRows);
            for (var row = 0; row < copyRows; row++)
            {
                for (var column = 0; column < copyColumns; column++)
                {
                    var oldIndex = row * oldColumns + column;
                    cells[row * newColumns + column] = new TerrainCell(
                        resource.CliffLevels[oldIndex],
                        (ushort)Math.Clamp(resource.SurfaceIds[oldIndex],
                            0, ushort.MaxValue),
                        (TerrainPathing)resource.PathingMasks[oldIndex],
                        (TerrainCellFlags)resource.CellFlags[oldIndex],
                        (TerrainRampDirection)resource.RampDirections[oldIndex]);
                    copied++;
                }
            }
        }
        resource.DataColumns = newColumns;
        resource.DataRows = newRows;
        WriteCells(resource, cells);
        return new TerrainAuthoringResizeResult(
            oldColumns,
            oldRows,
            newColumns,
            newRows,
            copied);
    }

    private static void ValidateLength(
        int actual,
        int expected,
        string field,
        List<TerrainAuthoringIssue> issues)
    {
        if (actual != expected)
            issues.Add(ResourceError(
                $"{field} contains {actual} values; expected {expected}."));
    }

    private static TerrainAuthoringIssue CellError(
        int index,
        int columns,
        string message)
    {
        var column = columns > 0 ? index % columns : -1;
        var row = columns > 0 ? index / columns : -1;
        return new TerrainAuthoringIssue(
            TerrainAuthoringIssueSeverity.Error,
            TerrainAuthoringErrorCode.InvalidDocument,
            column,
            row,
            $"Cell [{column},{row}]: {message}");
    }

    private static TerrainAuthoringIssue ResourceError(string message) => new(
        TerrainAuthoringIssueSeverity.Error,
        TerrainAuthoringErrorCode.InvalidDocument,
        -1,
        -1,
        message);
}

public readonly record struct TerrainAuthoringResizeResult(
    int OldColumns,
    int OldRows,
    int NewColumns,
    int NewRows,
    int CopiedCells);
