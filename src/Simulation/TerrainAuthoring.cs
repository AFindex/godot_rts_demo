using System.Numerics;

namespace RtsDemo.Simulation;

public enum TerrainAuthoringAnchorKind : byte
{
    Spawn,
    Resource,
    Objective
}

public readonly record struct TerrainAuthoringAnchor(
    int Id,
    TerrainAuthoringAnchorKind Kind,
    Vector2 Position,
    float RequiredRadius);

public enum TerrainBrushKind : byte
{
    CliffLevel,
    Surface,
    Pathing,
    Buildable,
    BlocksVision,
    BlocksCreep,
    Ramp,
    ClearRamp
}

public readonly record struct TerrainBrush(
    TerrainBrushKind Kind,
    int RadiusCells = 0,
    byte CliffLevel = 0,
    ushort SurfaceId = 0,
    TerrainPathing Pathing = TerrainPathing.Ground,
    bool Enabled = true,
    TerrainRampDirection RampDirection = TerrainRampDirection.None);

public enum TerrainAuthoringIssueSeverity : byte
{
    Warning,
    Error
}

public enum TerrainAuthoringErrorCode
{
    None,
    InvalidDocument,
    RuntimeValidation,
    InvalidRampEdge,
    InvalidRampLowerNeighbor,
    InvalidRampUpperNeighbor,
    IsolatedGroundIsland,
    InvalidAnchor,
    AnchorNotTraversable,
    AnchorUnreachable,
    NarrowPassage
}

public readonly record struct TerrainAuthoringIssue(
    TerrainAuthoringIssueSeverity Severity,
    TerrainAuthoringErrorCode Code,
    int Column,
    int Row,
    string Message);

public sealed class TerrainAuthoringValidationResult(
    TerrainAuthoringIssue[] issues)
{
    public TerrainAuthoringIssue[] Issues { get; } = issues;
    public bool IsValid => Issues.All(value =>
        value.Severity != TerrainAuthoringIssueSeverity.Error);
    public TerrainAuthoringIssue? FirstError => Issues.FirstOrDefault(value =>
        value.Severity == TerrainAuthoringIssueSeverity.Error);
}

public readonly record struct TerrainAuthoringValidationSettings(
    int MinimumGroundIslandCells,
    float MinimumGroundRadius)
{
    public static TerrainAuthoringValidationSettings Standard => new(
        MinimumGroundIslandCells: 4,
        MinimumGroundRadius: 8f);
}

/// <summary>
/// Mutable, engine-independent terrain authoring data. Editing tools mutate this
/// document; runtime systems only receive a validated TerrainMapSnapshot export.
/// </summary>
public sealed class TerrainAuthoringDocument
{
    private readonly TerrainSurfaceDefinition[] _surfaces;
    private readonly TerrainCell[] _cells;
    private readonly TerrainAuthoringAnchor[] _anchors;

    public TerrainAuthoringDocument(
        SimRect bounds,
        float cellSize,
        float cliffLevelHeight,
        ReadOnlySpan<TerrainSurfaceDefinition> surfaces,
        ReadOnlySpan<TerrainCell> cells,
        ReadOnlySpan<TerrainAuthoringAnchor> anchors)
    {
        if (bounds.Width <= 0f || bounds.Height <= 0f ||
            !float.IsFinite(cellSize) || cellSize <= 0f ||
            !float.IsFinite(cliffLevelHeight) || cliffLevelHeight <= 0f)
        {
            throw new ArgumentException("Terrain authoring geometry is invalid.");
        }
        Bounds = bounds;
        CellSize = cellSize;
        CliffLevelHeight = cliffLevelHeight;
        Columns = Math.Max(1, (int)MathF.Ceiling(bounds.Width / cellSize));
        Rows = Math.Max(1, (int)MathF.Ceiling(bounds.Height / cellSize));
        if (cells.Length != Columns * Rows)
        {
            throw new ArgumentException(
                $"Expected {Columns * Rows} cells, got {cells.Length}.",
                nameof(cells));
        }
        _surfaces = surfaces.ToArray();
        _cells = cells.ToArray();
        _anchors = anchors.ToArray();
    }

    public SimRect Bounds { get; }
    public float CellSize { get; }
    public float CliffLevelHeight { get; }
    public int Columns { get; }
    public int Rows { get; }
    public ReadOnlySpan<TerrainSurfaceDefinition> Surfaces => _surfaces;
    public ReadOnlySpan<TerrainCell> Cells => _cells;
    public ReadOnlySpan<TerrainAuthoringAnchor> Anchors => _anchors;

    public TerrainCell Cell(int column, int row)
    {
        ValidateCell(column, row);
        return _cells[Index(column, row)];
    }

    public void SetCell(int column, int row, TerrainCell value)
    {
        ValidateCell(column, row);
        _cells[Index(column, row)] = value;
    }

    public int ApplyBrush(int centerColumn, int centerRow, TerrainBrush brush)
    {
        ValidateCell(centerColumn, centerRow);
        if (brush.RadiusCells is < 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(brush));
        var changed = 0;
        var radiusSquared = brush.RadiusCells * brush.RadiusCells;
        for (var row = Math.Max(0, centerRow - brush.RadiusCells);
             row <= Math.Min(Rows - 1, centerRow + brush.RadiusCells);
             row++)
        {
            for (var column = Math.Max(0, centerColumn - brush.RadiusCells);
                 column <= Math.Min(Columns - 1, centerColumn + brush.RadiusCells);
                 column++)
            {
                var offsetColumn = column - centerColumn;
                var offsetRow = row - centerRow;
                if (offsetColumn * offsetColumn + offsetRow * offsetRow >
                    radiusSquared)
                {
                    continue;
                }
                var index = Index(column, row);
                var before = _cells[index];
                var after = Apply(before, brush);
                if (after == before) continue;
                _cells[index] = after;
                changed++;
            }
        }
        return changed;
    }

    public TerrainCell[] CaptureCells() => _cells.ToArray();

    public void RestoreCells(ReadOnlySpan<TerrainCell> cells)
    {
        if (cells.Length != _cells.Length)
            throw new ArgumentException("Terrain cell restore length mismatch.");
        cells.CopyTo(_cells);
    }

    public bool TryExport(
        TerrainAuthoringValidationSettings settings,
        out TerrainMapSnapshot? snapshot,
        out TerrainAuthoringValidationResult validation)
    {
        var issues = new List<TerrainAuthoringIssue>();
        if (!TerrainMapSnapshot.TryCreate(
                Bounds, CellSize, CliffLevelHeight,
                _surfaces, _cells, out snapshot, out var runtimeValidation) ||
            snapshot is null)
        {
            foreach (var issue in runtimeValidation.Issues)
            {
                var column = issue.ElementIndex >= 0
                    ? issue.ElementIndex % Columns
                    : -1;
                var row = issue.ElementIndex >= 0
                    ? issue.ElementIndex / Columns
                    : -1;
                issues.Add(new TerrainAuthoringIssue(
                    TerrainAuthoringIssueSeverity.Error,
                    TerrainAuthoringErrorCode.RuntimeValidation,
                    column,
                    row,
                    CoordinateMessage(column, row, issue.Message)));
            }
            validation = new TerrainAuthoringValidationResult([.. issues]);
            return false;
        }

        ValidateRamps(issues);
        ValidateIslands(settings, issues);
        ValidateAnchors(snapshot, settings, issues);
        validation = new TerrainAuthoringValidationResult([.. issues]);
        if (!validation.IsValid) snapshot = null;
        return validation.IsValid;
    }

    public bool TryCellAt(Vector2 position, out int column, out int row)
    {
        if (!Bounds.Contains(position))
        {
            column = -1;
            row = -1;
            return false;
        }
        column = Math.Clamp(
            (int)MathF.Floor((position.X - Bounds.Min.X) / CellSize),
            0, Columns - 1);
        row = Math.Clamp(
            (int)MathF.Floor((position.Y - Bounds.Min.Y) / CellSize),
            0, Rows - 1);
        return true;
    }

    private static TerrainCell Apply(TerrainCell cell, TerrainBrush brush) =>
        brush.Kind switch
        {
            TerrainBrushKind.CliffLevel => cell with
            {
                CliffLevel = brush.CliffLevel
            },
            TerrainBrushKind.Surface => cell with
            {
                SurfaceId = brush.SurfaceId
            },
            TerrainBrushKind.Pathing => cell with
            {
                Pathing = brush.Pathing
            },
            TerrainBrushKind.Buildable => cell with
            {
                Flags = SetFlag(
                    cell.Flags, TerrainCellFlags.Buildable, brush.Enabled)
            },
            TerrainBrushKind.BlocksVision => cell with
            {
                Flags = SetFlag(
                    cell.Flags, TerrainCellFlags.BlocksVision, brush.Enabled)
            },
            TerrainBrushKind.BlocksCreep => cell with
            {
                Flags = SetFlag(
                    cell.Flags, TerrainCellFlags.BlocksCreep, brush.Enabled)
            },
            TerrainBrushKind.Ramp => cell with
            {
                CliffLevel = brush.CliffLevel,
                Flags = (cell.Flags | TerrainCellFlags.Ramp) &
                        ~TerrainCellFlags.Buildable,
                RampDirection = brush.RampDirection
            },
            TerrainBrushKind.ClearRamp => cell with
            {
                Flags = cell.Flags & ~TerrainCellFlags.Ramp,
                RampDirection = TerrainRampDirection.None
            },
            _ => throw new ArgumentOutOfRangeException(nameof(brush))
        };

    private void ValidateRamps(List<TerrainAuthoringIssue> issues)
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var cell = Cell(column, row);
                if (!cell.IsRamp) continue;
                var direction = Direction(cell.RampDirection);
                var lowerColumn = column - direction.X;
                var lowerRow = row - direction.Y;
                var upperColumn = column + direction.X;
                var upperRow = row + direction.Y;
                if (!Inside(lowerColumn, lowerRow) ||
                    !Inside(upperColumn, upperRow))
                {
                    AddError(
                        issues, TerrainAuthoringErrorCode.InvalidRampEdge,
                        column, row,
                        "Ramp requires one lower-side and one upper-side neighbor.");
                    continue;
                }
                var lower = Cell(lowerColumn, lowerRow);
                var upper = Cell(upperColumn, upperRow);
                if (lower.IsRamp || lower.CliffLevel != cell.CliffLevel)
                {
                    AddError(
                        issues,
                        TerrainAuthoringErrorCode.InvalidRampLowerNeighbor,
                        column,
                        row,
                        $"Ramp lower neighbor [{lowerColumn},{lowerRow}] must be " +
                        $"flat level {cell.CliffLevel}.");
                }
                if (upper.IsRamp || upper.CliffLevel != cell.CliffLevel + 1)
                {
                    AddError(
                        issues,
                        TerrainAuthoringErrorCode.InvalidRampUpperNeighbor,
                        column,
                        row,
                        $"Ramp upper neighbor [{upperColumn},{upperRow}] must be " +
                        $"flat level {cell.CliffLevel + 1}.");
                }
            }
        }
    }

    private void ValidateIslands(
        TerrainAuthoringValidationSettings settings,
        List<TerrainAuthoringIssue> issues)
    {
        if (settings.MinimumGroundIslandCells <= 0) return;
        var components = BuildComponents(clearance: false, snapshot: null, 0f);
        foreach (var component in components.Groups)
        {
            if (component.Count >= settings.MinimumGroundIslandCells) continue;
            var first = component[0];
            AddError(
                issues,
                TerrainAuthoringErrorCode.IsolatedGroundIsland,
                first % Columns,
                first / Columns,
                $"Ground island contains {component.Count} cells; minimum is " +
                $"{settings.MinimumGroundIslandCells}.");
        }
    }

    private void ValidateAnchors(
        TerrainMapSnapshot snapshot,
        TerrainAuthoringValidationSettings settings,
        List<TerrainAuthoringIssue> issues)
    {
        if (_anchors.Length == 0) return;
        var ids = new HashSet<int>();
        foreach (var anchor in _anchors)
        {
            if (anchor.Id < 0 || !ids.Add(anchor.Id) ||
                !float.IsFinite(anchor.Position.X) ||
                !float.IsFinite(anchor.Position.Y) ||
                !float.IsFinite(anchor.RequiredRadius) ||
                anchor.RequiredRadius < 0f ||
                !Enum.IsDefined(anchor.Kind) ||
                !TryCellAt(anchor.Position, out var column, out var row))
            {
                issues.Add(new TerrainAuthoringIssue(
                    TerrainAuthoringIssueSeverity.Error,
                    TerrainAuthoringErrorCode.InvalidAnchor,
                    -1,
                    -1,
                    $"Anchor {anchor.Id} has invalid identity, kind, position or radius."));
                continue;
            }
            var radius = MathF.Max(settings.MinimumGroundRadius,
                anchor.RequiredRadius);
            if (!snapshot.IsDiscTraversable(
                    anchor.Position, radius, TerrainMovementMode.Ground))
            {
                AddError(
                    issues,
                    TerrainAuthoringErrorCode.AnchorNotTraversable,
                    column,
                    row,
                    $"{anchor.Kind} anchor {anchor.Id} cannot fit radius {radius:0.##}.");
            }
        }

        var validAnchors = _anchors.Where(value =>
                TryCellAt(value.Position, out _, out _))
            .ToArray();
        var spawns = validAnchors.Where(value =>
            value.Kind == TerrainAuthoringAnchorKind.Spawn).ToArray();
        if (spawns.Length == 0) return;
        var primarySpawn = spawns.OrderBy(value => value.Id).First();
        var minimumRadius = MathF.Max(0f, settings.MinimumGroundRadius);
        var point = BuildComponents(clearance: false, snapshot: null, 0f);
        var clearance = BuildComponents(
            clearance: true, snapshot, minimumRadius);
        foreach (var anchor in validAnchors)
        {
            if (!TryCellAt(anchor.Position, out var column, out var row)) continue;
            var index = Index(column, row);
            var nearestSpawn = anchor.Kind == TerrainAuthoringAnchorKind.Spawn &&
                               anchor.Id != primarySpawn.Id
                ? primarySpawn
                : spawns
                    .Where(value => value.Id != anchor.Id || spawns.Length == 1)
                    .OrderBy(value => Vector2.DistanceSquared(
                        value.Position, anchor.Position))
                    .DefaultIfEmpty(primarySpawn)
                    .First();
            TryCellAt(nearestSpawn.Position, out var spawnColumn, out var spawnRow);
            var spawnIndex = Index(spawnColumn, spawnRow);
            var pointConnected = point.Labels[index] >= 0 &&
                                 point.Labels[index] == point.Labels[spawnIndex];
            var clearanceConnected = clearance.Labels[index] >= 0 &&
                                     clearance.Labels[index] ==
                                     clearance.Labels[spawnIndex];
            if (clearanceConnected) continue;
            AddError(
                issues,
                pointConnected
                    ? TerrainAuthoringErrorCode.NarrowPassage
                    : TerrainAuthoringErrorCode.AnchorUnreachable,
                column,
                row,
                pointConnected
                    ? $"{anchor.Kind} anchor {anchor.Id} is point-connected to spawn " +
                      $"{nearestSpawn.Id}, but radius {minimumRadius:0.##} cannot pass."
                    : $"{anchor.Kind} anchor {anchor.Id} is disconnected from spawn " +
                      $"{nearestSpawn.Id}.");
        }
    }

    private ComponentMap BuildComponents(
        bool clearance,
        TerrainMapSnapshot? snapshot,
        float radius)
    {
        var labels = Enumerable.Repeat(-1, _cells.Length).ToArray();
        var groups = new List<List<int>>();
        var queue = new Queue<int>();
        for (var index = 0; index < _cells.Length; index++)
        {
            if (labels[index] >= 0 || !Walkable(index, clearance, snapshot, radius))
                continue;
            var component = groups.Count;
            var group = new List<int>();
            groups.Add(group);
            labels[index] = component;
            queue.Enqueue(index);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                group.Add(current);
                var column = current % Columns;
                var row = current / Columns;
                Visit(column - 1, row);
                Visit(column + 1, row);
                Visit(column, row - 1);
                Visit(column, row + 1);

                void Visit(int nextColumn, int nextRow)
                {
                    if (!Inside(nextColumn, nextRow)) return;
                    var next = Index(nextColumn, nextRow);
                    if (labels[next] >= 0 ||
                        !Walkable(next, clearance, snapshot, radius) ||
                        !Connected(current, next))
                    {
                        return;
                    }
                    labels[next] = component;
                    queue.Enqueue(next);
                }
            }
        }
        return new ComponentMap(labels, groups);
    }

    private bool Walkable(
        int index,
        bool clearance,
        TerrainMapSnapshot? snapshot,
        float radius)
    {
        var cell = _cells[index];
        if ((cell.Pathing & (TerrainPathing.Ground |
                             TerrainPathing.ShallowWater)) == 0)
            return false;
        if (!clearance) return true;
        var column = index % Columns;
        var row = index / Columns;
        var center = Bounds.Min + new Vector2(
            (column + 0.5f) * CellSize,
            (row + 0.5f) * CellSize);
        center = Vector2.Min(center, Bounds.Max - new Vector2(0.001f));
        return snapshot!.IsDiscTraversable(
            center, radius, TerrainMovementMode.Ground);
    }

    private bool Connected(int firstIndex, int secondIndex)
    {
        var firstColumn = firstIndex % Columns;
        var firstRow = firstIndex / Columns;
        var secondColumn = secondIndex % Columns;
        var secondRow = secondIndex / Columns;
        var first = _cells[firstIndex];
        var second = _cells[secondIndex];
        if (!first.IsRamp && !second.IsRamp)
            return first.CliffLevel == second.CliffLevel;
        if (first.IsRamp && second.IsRamp)
        {
            return first.RampDirection == second.RampDirection &&
                   first.CliffLevel == second.CliffLevel &&
                    CellOffset.Dot(
                       new CellOffset(secondColumn - firstColumn,
                           secondRow - firstRow),
                       Direction(first.RampDirection)) == 0;
        }
        var ramp = first.IsRamp ? first : second;
        var rampColumn = first.IsRamp ? firstColumn : secondColumn;
        var rampRow = first.IsRamp ? firstRow : secondRow;
        var flat = first.IsRamp ? second : first;
        var flatColumn = first.IsRamp ? secondColumn : firstColumn;
        var flatRow = first.IsRamp ? secondRow : firstRow;
        var offset = new CellOffset(flatColumn - rampColumn, flatRow - rampRow);
        var direction = Direction(ramp.RampDirection);
        return offset == -direction && flat.CliffLevel == ramp.CliffLevel ||
               offset == direction && flat.CliffLevel == ramp.CliffLevel + 1;
    }

    private static TerrainCellFlags SetFlag(
        TerrainCellFlags flags,
        TerrainCellFlags flag,
        bool enabled) => enabled ? flags | flag : flags & ~flag;

    private static CellOffset Direction(TerrainRampDirection direction) =>
        direction switch
        {
            TerrainRampDirection.PositiveX => new CellOffset(1, 0),
            TerrainRampDirection.NegativeX => new CellOffset(-1, 0),
            TerrainRampDirection.PositiveY => new CellOffset(0, 1),
            TerrainRampDirection.NegativeY => new CellOffset(0, -1),
            _ => default
        };

    private static void AddError(
        List<TerrainAuthoringIssue> issues,
        TerrainAuthoringErrorCode code,
        int column,
        int row,
        string message) => issues.Add(new TerrainAuthoringIssue(
        TerrainAuthoringIssueSeverity.Error,
        code,
        column,
        row,
        CoordinateMessage(column, row, message)));

    private static string CoordinateMessage(int column, int row, string message) =>
        column >= 0 && row >= 0
            ? $"Cell [{column},{row}]: {message}"
            : message;

    private bool Inside(int column, int row) =>
        (uint)column < (uint)Columns && (uint)row < (uint)Rows;

    private int Index(int column, int row) => row * Columns + column;

    private void ValidateCell(int column, int row)
    {
        if (!Inside(column, row))
            throw new ArgumentOutOfRangeException(nameof(column));
    }

    private sealed record ComponentMap(int[] Labels, List<List<int>> Groups);

    private readonly record struct CellOffset(int X, int Y)
    {
        public static CellOffset operator -(CellOffset value) =>
            new(-value.X, -value.Y);

        public static int Dot(CellOffset left, CellOffset right) =>
            left.X * right.X + left.Y * right.Y;
    }
}
