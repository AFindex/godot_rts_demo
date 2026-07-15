using System.Numerics;
using System.Text;

namespace RtsDemo.Simulation;

[Flags]
public enum TerrainPathing : byte
{
    None = 0,
    Ground = 1 << 0,
    ShallowWater = 1 << 1,
    DeepWater = 1 << 2,
    AirBlocked = 1 << 3
}

[Flags]
public enum TerrainCellFlags : byte
{
    None = 0,
    Buildable = 1 << 0,
    Ramp = 1 << 1,
    BlocksVision = 1 << 2,
    BlocksCreep = 1 << 3
}

public enum TerrainMovementMode : byte
{
    Ground,
    Float,
    Amphibious,
    Flying
}

public enum TerrainRampDirection : byte
{
    None,
    PositiveX,
    NegativeX,
    PositiveY,
    NegativeY
}

public enum TerrainVisionMode : byte
{
    Ground,
    Elevated
}

public readonly record struct TerrainSurfaceDefinition(
    ushort Id,
    string MaterialKey,
    string DisplayName);

/// <summary>
/// One authored terrain cell. CliffLevel is the lower level for ramp cells;
/// the ramp rises exactly one level in RampDirection.
/// </summary>
public readonly record struct TerrainCell(
    byte CliffLevel,
    ushort SurfaceId,
    TerrainPathing Pathing,
    TerrainCellFlags Flags,
    TerrainRampDirection RampDirection = TerrainRampDirection.None)
{
    public bool IsRamp => (Flags & TerrainCellFlags.Ramp) != 0;
    public bool IsBuildable => (Flags & TerrainCellFlags.Buildable) != 0;
}

public enum TerrainMapErrorCode
{
    None,
    InvalidHeader,
    UnsupportedFormatVersion,
    InvalidPayload,
    TrailingPayload,
    MissingResourceAsset,
    DeclaredHashMismatch,
    InvalidBounds,
    InvalidCellSize,
    InvalidCliffHeight,
    InvalidDimensions,
    InvalidCellCount,
    MissingSurface,
    NonDenseSurfaceId,
    InvalidSurface,
    InvalidCellSurface,
    InvalidPathing,
    InvalidRamp,
    InvalidCliffLevel
}

public readonly record struct TerrainMapValidationIssue(
    TerrainMapErrorCode Code,
    int ElementIndex,
    string Message);

public sealed class TerrainMapValidationResult(
    TerrainMapValidationIssue[] issues)
{
    public TerrainMapValidationIssue[] Issues { get; } = issues;
    public bool IsValid => Issues.Length == 0;
    public TerrainMapErrorCode FirstError =>
        IsValid ? TerrainMapErrorCode.None : Issues[0].Code;
}

/// <summary>
/// Engine-independent terrain contract consumed by simulation systems. Godot
/// presentation and editor tooling may produce or display this data, but never
/// become the authority for movement or placement.
/// </summary>
public interface ITerrainMapQuery
{
    SimRect Bounds { get; }
    ulong StableHash { get; }
    float CellSize { get; }
    float CliffLevelHeight { get; }
    bool HasVisionBlockers { get; }
    float HeightAt(Vector2 position);
    bool IsVisibleFrom(
        Vector2 observer,
        Vector2 target,
        float observationHeight,
        TerrainVisionMode mode = TerrainVisionMode.Ground);
    bool IsDiscTraversable(
        Vector2 position,
        float radius,
        TerrainMovementMode mode = TerrainMovementMode.Ground);
    bool IsSegmentTraversable(
        Vector2 from,
        Vector2 to,
        float radius,
        TerrainMovementMode mode = TerrainMovementMode.Ground);
    bool IsAreaBuildable(SimRect area);
}

/// <summary>
/// Validated immutable terrain raster. Texture identity, gameplay pathing,
/// cliff height and buildability are intentionally independent fields.
/// </summary>
public sealed class TerrainMapSnapshot : ITerrainMapQuery
{
    public const int CurrentFormatVersion = 1;
    public const byte MaximumCliffLevel = 15;
    private const int DiscProbeCount = 16;
    private const int Magic = 0x4E525452;

    private readonly TerrainSurfaceDefinition[] _surfaces;
    private readonly TerrainCell[] _cells;
    private readonly byte[] _canonicalBytes;
    private readonly SimRect _visionBlockerBounds;

    private TerrainMapSnapshot(
        SimRect bounds,
        float cellSize,
        float cliffLevelHeight,
        int columns,
        int rows,
        TerrainSurfaceDefinition[] surfaces,
        TerrainCell[] cells)
    {
        Bounds = bounds;
        CellSize = cellSize;
        CliffLevelHeight = cliffLevelHeight;
        Columns = columns;
        Rows = rows;
        _surfaces = surfaces;
        _cells = cells;
        MaximumCellLevel = cells.Max(value => value.CliffLevel);
        HasVisionBlockers = cells.Any(value =>
            (value.Flags & TerrainCellFlags.BlocksVision) != 0);
        _visionBlockerBounds = HasVisionBlockers
            ? ComputeVisionBlockerBounds()
            : default;
        _canonicalBytes = BuildCanonicalBytes();
        StableHash = ComputeStableHash(_canonicalBytes);
    }

    public SimRect Bounds { get; }
    public int FormatVersion => CurrentFormatVersion;
    public float CellSize { get; }
    public float CliffLevelHeight { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int CellCount => _cells.Length;
    public byte MaximumCellLevel { get; }
    public bool HasVisionBlockers { get; }
    public ReadOnlySpan<TerrainSurfaceDefinition> Surfaces => _surfaces;
    public ReadOnlySpan<TerrainCell> Cells => _cells;
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    public static bool TryCreate(
        SimRect bounds,
        float cellSize,
        float cliffLevelHeight,
        ReadOnlySpan<TerrainSurfaceDefinition> surfaces,
        ReadOnlySpan<TerrainCell> cells,
        out TerrainMapSnapshot? snapshot,
        out TerrainMapValidationResult validation)
    {
        var columns = ValidSize(bounds.Width, cellSize)
            ? (int)MathF.Ceiling(bounds.Width / cellSize)
            : 0;
        var rows = ValidSize(bounds.Height, cellSize)
            ? (int)MathF.Ceiling(bounds.Height / cellSize)
            : 0;
        var surfaceCopy = surfaces.ToArray();
        var cellCopy = cells.ToArray();
        validation = Validate(
            bounds, cellSize, cliffLevelHeight, columns, rows,
            surfaceCopy, cellCopy);
        if (!validation.IsValid)
        {
            snapshot = null;
            return false;
        }

        snapshot = new TerrainMapSnapshot(
            bounds, cellSize, cliffLevelHeight, columns, rows,
            surfaceCopy, cellCopy);
        return true;
    }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> data,
        out TerrainMapSnapshot? snapshot,
        out TerrainMapValidationResult validation)
    {
        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadInt32() != Magic)
            {
                return DeserializeFailure(
                    TerrainMapErrorCode.InvalidHeader,
                    "Terrain magic header is invalid.",
                    out snapshot,
                    out validation);
            }
            var version = reader.ReadInt32();
            if (version != CurrentFormatVersion)
            {
                return DeserializeFailure(
                    TerrainMapErrorCode.UnsupportedFormatVersion,
                    $"Expected terrain format {CurrentFormatVersion}, got {version}.",
                    out snapshot,
                    out validation);
            }
            var bounds = new SimRect(
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()));
            var cellSize = reader.ReadSingle();
            var cliffLevelHeight = reader.ReadSingle();
            var declaredColumns = reader.ReadInt32();
            var declaredRows = reader.ReadInt32();
            var surfaceCount = ReadCount(reader, 65_535);
            var surfaces = new TerrainSurfaceDefinition[surfaceCount];
            for (var index = 0; index < surfaces.Length; index++)
            {
                surfaces[index] = new TerrainSurfaceDefinition(
                    reader.ReadUInt16(),
                    reader.ReadString(),
                    reader.ReadString());
            }
            var cellCount = ReadCount(reader, 16_777_216);
            var cells = new TerrainCell[cellCount];
            for (var index = 0; index < cells.Length; index++)
            {
                cells[index] = new TerrainCell(
                    reader.ReadByte(),
                    reader.ReadUInt16(),
                    (TerrainPathing)reader.ReadByte(),
                    (TerrainCellFlags)reader.ReadByte(),
                    (TerrainRampDirection)reader.ReadByte());
            }
            if (stream.Position != stream.Length)
            {
                return DeserializeFailure(
                    TerrainMapErrorCode.TrailingPayload,
                    "Terrain payload contains trailing bytes.",
                    out snapshot,
                    out validation);
            }
            if (!TryCreate(
                    bounds, cellSize, cliffLevelHeight,
                    surfaces, cells, out snapshot, out validation) ||
                snapshot is null)
            {
                return false;
            }
            if (snapshot.Columns != declaredColumns ||
                snapshot.Rows != declaredRows)
            {
                return DeserializeFailure(
                    TerrainMapErrorCode.InvalidPayload,
                    "Terrain payload dimensions do not match its bounds and cell size.",
                    out snapshot,
                    out validation);
            }
            return true;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or
                OverflowException or ArgumentException)
        {
            return DeserializeFailure(
                TerrainMapErrorCode.InvalidPayload,
                $"Terrain payload is invalid: {exception.Message}",
                out snapshot,
                out validation);
        }
    }

    public TerrainCell Cell(int column, int row)
    {
        if ((uint)column >= (uint)Columns || (uint)row >= (uint)Rows)
            throw new ArgumentOutOfRangeException(nameof(column));
        return _cells[row * Columns + column];
    }

    public TerrainSurfaceDefinition Surface(ushort id)
    {
        if (id >= _surfaces.Length)
            throw new ArgumentOutOfRangeException(nameof(id));
        return _surfaces[id];
    }

    public SimRect CellBounds(int column, int row)
    {
        if ((uint)column >= (uint)Columns || (uint)row >= (uint)Rows)
            throw new ArgumentOutOfRangeException(nameof(column));
        var minimum = Bounds.Min + new Vector2(column * CellSize, row * CellSize);
        return new SimRect(
            minimum,
            Vector2.Min(Bounds.Max, minimum + new Vector2(CellSize)));
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

    public float HeightAt(Vector2 position)
    {
        if (!TryCellAt(position, out var column, out var row))
            return 0f;
        var bounds = CellBounds(column, row);
        var localX = bounds.Width <= 0f
            ? 0f
            : Math.Clamp((position.X - bounds.Min.X) / bounds.Width, 0f, 1f);
        var localY = bounds.Height <= 0f
            ? 0f
            : Math.Clamp((position.Y - bounds.Min.Y) / bounds.Height, 0f, 1f);
        return CellHeight(Cell(column, row), localX, localY);
    }

    public bool IsVisibleFrom(
        Vector2 observer,
        Vector2 target,
        float observationHeight,
        TerrainVisionMode mode = TerrainVisionMode.Ground)
    {
        if (!float.IsFinite(observationHeight) || observationHeight < 0f ||
            !Bounds.Contains(observer) || !Bounds.Contains(target) ||
            !Enum.IsDefined(mode))
        {
            return false;
        }
        if (mode == TerrainVisionMode.Elevated)
            return true;

        var observerEyeHeight = HeightAt(observer) + observationHeight;
        var numericTolerance = MathF.Max(0.01f, CliffLevelHeight * 0.001f);
        if (HeightAt(target) > observerEyeHeight + numericTolerance)
            return false;
        if (!HasVisionBlockers)
            return true;

        var segmentMinimum = Vector2.Min(observer, target) -
                             new Vector2(0.01f);
        var segmentMaximum = Vector2.Max(observer, target) +
                             new Vector2(0.01f);
        if (!_visionBlockerBounds.Intersects(
                new SimRect(segmentMinimum, segmentMaximum)))
        {
            return true;
        }

        var observerBlocked = IsVisionBlocker(observer);
        var targetBlocked = IsVisionBlocker(target);
        if (observerBlocked != targetBlocked)
            return false;

        var distance = Vector2.Distance(observer, target);
        if (distance <= 0.0001f)
            return true;
        var steps = Math.Max(
            1, (int)MathF.Ceiling(distance / MathF.Max(2f, CellSize * 0.25f)));
        for (var step = 1; step < steps; step++)
        {
            var position = Vector2.Lerp(observer, target, step / (float)steps);
            if (IsVisionBlocker(position) != observerBlocked)
                return false;
        }
        return true;
    }

    public float CellCornerHeight(
        int column,
        int row,
        bool maximumX,
        bool maximumY) =>
        CellHeight(Cell(column, row), maximumX ? 1f : 0f, maximumY ? 1f : 0f);

    public bool IsDiscTraversable(
        Vector2 position,
        float radius,
        TerrainMovementMode mode = TerrainMovementMode.Ground)
    {
        if (!float.IsFinite(radius) || radius < 0f ||
            !Bounds.Inset(radius).Contains(position) ||
            !TryCellAt(position, out var centerColumn, out var centerRow) ||
            !Allows(Cell(centerColumn, centerRow), mode))
        {
            return false;
        }

        if (radius <= 0.0001f)
            return true;
        for (var probe = 0; probe < DiscProbeCount; probe++)
        {
            var angle = MathF.Tau * probe / DiscProbeCount;
            var edge = position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            if (!IsCenterLineTraversable(position, edge, mode))
                return false;
        }
        return true;
    }

    public bool IsSegmentTraversable(
        Vector2 from,
        Vector2 to,
        float radius,
        TerrainMovementMode mode = TerrainMovementMode.Ground)
    {
        if (!IsDiscTraversable(from, radius, mode) ||
            !IsDiscTraversable(to, radius, mode))
        {
            return false;
        }

        var distance = Vector2.Distance(from, to);
        if (distance <= 0.0001f)
            return true;
        var stepLength = MathF.Max(2f, CellSize * 0.2f);
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / stepLength));
        var previous = from;
        for (var step = 1; step <= steps; step++)
        {
            var current = Vector2.Lerp(from, to, step / (float)steps);
            if (!IsDiscTraversable(current, radius, mode) ||
                !CanStep(previous, current, mode))
            {
                return false;
            }
            previous = current;
        }
        return true;
    }

    public bool IsAreaBuildable(SimRect area)
    {
        if (!Finite(area.Min) || !Finite(area.Max) ||
            area.Width <= 0f || area.Height <= 0f ||
            !Bounds.Contains(area.Min) || !Bounds.Contains(area.Max))
        {
            return false;
        }

        var insetMaximum = area.Max - new Vector2(0.0001f);
        if (!TryCellAt(area.Min, out var minimumColumn, out var minimumRow) ||
            !TryCellAt(insetMaximum, out var maximumColumn, out var maximumRow))
        {
            return false;
        }

        byte? level = null;
        for (var row = minimumRow; row <= maximumRow; row++)
        {
            for (var column = minimumColumn; column <= maximumColumn; column++)
            {
                var cell = Cell(column, row);
                if (!cell.IsBuildable || cell.IsRamp ||
                    (cell.Pathing & TerrainPathing.Ground) == 0)
                {
                    return false;
                }
                level ??= cell.CliffLevel;
                if (level.Value != cell.CliffLevel)
                    return false;
            }
        }
        return true;
    }

    private bool IsCenterLineTraversable(
        Vector2 from,
        Vector2 to,
        TerrainMovementMode mode)
    {
        var distance = Vector2.Distance(from, to);
        var steps = Math.Max(
            1, (int)MathF.Ceiling(distance / MathF.Max(2f, CellSize * 0.2f)));
        var previous = from;
        for (var step = 1; step <= steps; step++)
        {
            var current = Vector2.Lerp(from, to, step / (float)steps);
            if (!CanStep(previous, current, mode))
                return false;
            previous = current;
        }
        return true;
    }

    private bool IsVisionBlocker(Vector2 position) =>
        TryCellAt(position, out var column, out var row) &&
        (Cell(column, row).Flags & TerrainCellFlags.BlocksVision) != 0;

    private SimRect ComputeVisionBlockerBounds()
    {
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                if ((Cell(column, row).Flags & TerrainCellFlags.BlocksVision) == 0)
                    continue;
                var bounds = CellBounds(column, row);
                minimum = Vector2.Min(minimum, bounds.Min);
                maximum = Vector2.Max(maximum, bounds.Max);
            }
        }
        return new SimRect(minimum, maximum);
    }

    private bool CanStep(
        Vector2 from,
        Vector2 to,
        TerrainMovementMode mode)
    {
        if (!TryCellAt(from, out var fromColumn, out var fromRow) ||
            !TryCellAt(to, out var toColumn, out var toRow))
        {
            return false;
        }
        var fromCell = Cell(fromColumn, fromRow);
        var toCell = Cell(toColumn, toRow);
        if (!Allows(fromCell, mode) || !Allows(toCell, mode))
            return false;
        if (mode == TerrainMovementMode.Flying ||
            fromColumn == toColumn && fromRow == toRow)
        {
            return true;
        }
        if (fromCell.CliffLevel == toCell.CliffLevel &&
            !fromCell.IsRamp && !toCell.IsRamp)
        {
            return true;
        }
        if (!fromCell.IsRamp && !toCell.IsRamp)
            return false;
        return MathF.Abs(HeightAt(from) - HeightAt(to)) <=
               CliffLevelHeight * 0.35f + 0.001f;
    }

    private static bool Allows(TerrainCell cell, TerrainMovementMode mode) =>
        mode switch
        {
            TerrainMovementMode.Ground =>
                (cell.Pathing & (TerrainPathing.Ground |
                                 TerrainPathing.ShallowWater)) != 0,
            TerrainMovementMode.Float =>
                (cell.Pathing & (TerrainPathing.ShallowWater |
                                 TerrainPathing.DeepWater)) != 0,
            TerrainMovementMode.Amphibious =>
                (cell.Pathing & (TerrainPathing.Ground |
                                 TerrainPathing.ShallowWater |
                                 TerrainPathing.DeepWater)) != 0,
            TerrainMovementMode.Flying =>
                (cell.Pathing & TerrainPathing.AirBlocked) == 0,
            _ => false
        };

    private float CellHeight(TerrainCell cell, float localX, float localY)
    {
        var rise = cell.IsRamp
            ? cell.RampDirection switch
            {
                TerrainRampDirection.PositiveX => localX,
                TerrainRampDirection.NegativeX => 1f - localX,
                TerrainRampDirection.PositiveY => localY,
                TerrainRampDirection.NegativeY => 1f - localY,
                _ => 0f
            }
            : 0f;
        return (cell.CliffLevel + rise) * CliffLevelHeight;
    }

    private byte[] BuildCanonicalBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(CurrentFormatVersion);
        writer.Write(Bounds.Min.X);
        writer.Write(Bounds.Min.Y);
        writer.Write(Bounds.Max.X);
        writer.Write(Bounds.Max.Y);
        writer.Write(CellSize);
        writer.Write(CliffLevelHeight);
        writer.Write(Columns);
        writer.Write(Rows);
        writer.Write(_surfaces.Length);
        foreach (var surface in _surfaces)
        {
            writer.Write(surface.Id);
            writer.Write(surface.MaterialKey);
            writer.Write(surface.DisplayName);
        }
        writer.Write(_cells.Length);
        foreach (var cell in _cells)
        {
            writer.Write(cell.CliffLevel);
            writer.Write(cell.SurfaceId);
            writer.Write((byte)cell.Pathing);
            writer.Write((byte)cell.Flags);
            writer.Write((byte)cell.RampDirection);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static TerrainMapValidationResult Validate(
        SimRect bounds,
        float cellSize,
        float cliffLevelHeight,
        int columns,
        int rows,
        TerrainSurfaceDefinition[] surfaces,
        TerrainCell[] cells)
    {
        var issues = new List<TerrainMapValidationIssue>();
        if (!Finite(bounds.Min) || !Finite(bounds.Max) ||
            bounds.Width <= 0f || bounds.Height <= 0f)
        {
            issues.Add(new TerrainMapValidationIssue(
                TerrainMapErrorCode.InvalidBounds, -1,
                "Terrain bounds must be finite and non-empty."));
        }
        if (!float.IsFinite(cellSize) || cellSize <= 0f)
        {
            issues.Add(new TerrainMapValidationIssue(
                TerrainMapErrorCode.InvalidCellSize, -1,
                "Terrain cell size must be finite and positive."));
        }
        if (!float.IsFinite(cliffLevelHeight) || cliffLevelHeight <= 0f)
        {
            issues.Add(new TerrainMapValidationIssue(
                TerrainMapErrorCode.InvalidCliffHeight, -1,
                "Cliff level height must be finite and positive."));
        }
        if (columns <= 0 || rows <= 0)
        {
            issues.Add(new TerrainMapValidationIssue(
                TerrainMapErrorCode.InvalidDimensions, -1,
                "Terrain grid dimensions must be positive."));
        }
        if (cells.Length != columns * rows)
        {
            issues.Add(new TerrainMapValidationIssue(
                TerrainMapErrorCode.InvalidCellCount, -1,
                $"Expected {columns * rows} cells, got {cells.Length}."));
        }
        if (surfaces.Length == 0)
        {
            issues.Add(new TerrainMapValidationIssue(
                TerrainMapErrorCode.MissingSurface, -1,
                "Terrain requires at least one surface."));
        }
        for (var index = 0; index < surfaces.Length; index++)
        {
            var surface = surfaces[index];
            if (surface.Id != index)
            {
                issues.Add(new TerrainMapValidationIssue(
                    TerrainMapErrorCode.NonDenseSurfaceId, index,
                    "Surface IDs must match their dense array index."));
            }
            if (string.IsNullOrWhiteSpace(surface.MaterialKey) ||
                string.IsNullOrWhiteSpace(surface.DisplayName))
            {
                issues.Add(new TerrainMapValidationIssue(
                    TerrainMapErrorCode.InvalidSurface, index,
                    "Surface material key and display name are required."));
            }
        }
        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            if (cell.CliffLevel > MaximumCliffLevel)
            {
                issues.Add(new TerrainMapValidationIssue(
                    TerrainMapErrorCode.InvalidCliffLevel, index,
                    $"Cliff level cannot exceed {MaximumCliffLevel}."));
            }
            if (cell.SurfaceId >= surfaces.Length)
            {
                issues.Add(new TerrainMapValidationIssue(
                    TerrainMapErrorCode.InvalidCellSurface, index,
                    "Cell references an unknown surface."));
            }
            if ((cell.Pathing & ~(TerrainPathing.Ground |
                                  TerrainPathing.ShallowWater |
                                  TerrainPathing.DeepWater |
                                  TerrainPathing.AirBlocked)) != 0)
            {
                issues.Add(new TerrainMapValidationIssue(
                    TerrainMapErrorCode.InvalidPathing, index,
                    "Cell contains unknown pathing flags."));
            }
            var rampFlag = cell.IsRamp;
            var rampDirection = cell.RampDirection != TerrainRampDirection.None;
            if (rampFlag != rampDirection ||
                rampFlag && cell.CliffLevel >= MaximumCliffLevel)
            {
                issues.Add(new TerrainMapValidationIssue(
                    TerrainMapErrorCode.InvalidRamp, index,
                    "Ramp cells require a direction and room for one higher level."));
            }
        }
        return new TerrainMapValidationResult(issues.ToArray());
    }

    private static bool ValidSize(float length, float cellSize) =>
        float.IsFinite(length) && length > 0f &&
        float.IsFinite(cellSize) && cellSize > 0f;

    private static int ReadCount(BinaryReader reader, int maximum)
    {
        var value = reader.ReadInt32();
        if (value < 0 || value > maximum)
            throw new InvalidDataException($"Invalid terrain element count {value}.");
        return value;
    }

    private static bool DeserializeFailure(
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

    private static bool Finite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static ulong ComputeStableHash(ReadOnlySpan<byte> bytes)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= prime;
        }
        return hash;
    }
}

/// <summary>
/// Mutable authoring helper. Runtime systems only receive the immutable result.
/// </summary>
public sealed class TerrainMapBuilder
{
    private readonly SimRect _bounds;
    private readonly float _cellSize;
    private readonly float _cliffLevelHeight;
    private readonly TerrainSurfaceDefinition[] _surfaces;
    private readonly TerrainCell[] _cells;

    public TerrainMapBuilder(
        SimRect bounds,
        float cellSize,
        float cliffLevelHeight,
        ReadOnlySpan<TerrainSurfaceDefinition> surfaces,
        TerrainCell fill)
    {
        if (!float.IsFinite(cellSize) || cellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        _bounds = bounds;
        _cellSize = cellSize;
        _cliffLevelHeight = cliffLevelHeight;
        _surfaces = surfaces.ToArray();
        Columns = Math.Max(1, (int)MathF.Ceiling(bounds.Width / cellSize));
        Rows = Math.Max(1, (int)MathF.Ceiling(bounds.Height / cellSize));
        _cells = new TerrainCell[Columns * Rows];
        Array.Fill(_cells, fill);
    }

    public int Columns { get; }
    public int Rows { get; }

    public void SetCell(int column, int row, TerrainCell cell)
    {
        if ((uint)column >= (uint)Columns || (uint)row >= (uint)Rows)
            throw new ArgumentOutOfRangeException(nameof(column));
        _cells[row * Columns + column] = cell;
    }

    public void Paint(SimRect area, TerrainCell cell)
    {
        var minimumColumn = Column(area.Min.X);
        var minimumRow = Row(area.Min.Y);
        var maximumColumn = Column(area.Max.X - 0.0001f);
        var maximumRow = Row(area.Max.Y - 0.0001f);
        for (var row = minimumRow; row <= maximumRow; row++)
        {
            for (var column = minimumColumn; column <= maximumColumn; column++)
                SetCell(column, row, cell);
        }
    }

    public void PaintContained(SimRect area, TerrainCell cell)
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var bounds = CellBounds(column, row);
                if (area.Contains(bounds.Min) && area.Contains(bounds.Max))
                    SetCell(column, row, cell);
            }
        }
    }

    public TerrainMapSnapshot Build()
    {
        if (!TerrainMapSnapshot.TryCreate(
                _bounds, _cellSize, _cliffLevelHeight,
                _surfaces, _cells, out var snapshot, out var validation) ||
            snapshot is null)
        {
            throw new InvalidOperationException(
                $"Terrain build failed: {validation.FirstError}.");
        }
        return snapshot;
    }

    private int Column(float value) => Math.Clamp(
        (int)MathF.Floor((value - _bounds.Min.X) / _cellSize),
        0, Columns - 1);

    private int Row(float value) => Math.Clamp(
        (int)MathF.Floor((value - _bounds.Min.Y) / _cellSize),
        0, Rows - 1);

    private SimRect CellBounds(int column, int row)
    {
        var minimum = _bounds.Min + new Vector2(
            column * _cellSize, row * _cellSize);
        return new SimRect(
            minimum,
            Vector2.Min(_bounds.Max, minimum + new Vector2(_cellSize)));
    }
}
