using RtsDemo.Simulation;
using Vector2 = System.Numerics.Vector2;

namespace War3Rts.Maps;

public enum War3MapTool : byte
{
    Surface,
    RaiseHeight,
    LowerHeight,
    SmoothHeight,
    PerturbHeight,
    CliffLevel,
    PlaceRamp,
    DeleteRamp,
    GroundPathing,
    Buildable,
    PlaceSpawn,
    PlaceGoldMine,
    PlaceTree,
    EraseObject
}

public enum War3BrushShape : byte
{
    Circle,
    Square,
    Diamond
}

public readonly record struct War3MapBrush(
    War3MapTool Tool,
    int RadiusCells,
    float Strength,
    War3BrushShape Shape,
    ushort SurfaceId = 0,
    byte CliffLevel = 0,
    TerrainRampDirection RampDirection = TerrainRampDirection.PositiveX,
    bool Enabled = true,
    int OwnerSlot = 0);

/// <summary>
/// Mutable, engine-independent map editing session. The scene node is only a
/// view/controller; every committed state is serializable as a War3MapAsset.
/// </summary>
public sealed class War3MapEditDocument
{
    private readonly TerrainSurfaceDefinition[] _surfaces;
    private TerrainCell[] _cells;
    private float[] _fineHeights;
    private readonly List<War3MapObject> _objects;

    private War3MapEditDocument(
        War3MapMetadata metadata,
        SimRect bounds,
        float cellSize,
        float cliffHeight,
        TerrainSurfaceDefinition[] surfaces,
        TerrainCell[] cells,
        float[] fineHeights,
        List<War3MapObject> objects)
    {
        Metadata = metadata;
        Bounds = bounds;
        CellSize = cellSize;
        CliffHeight = cliffHeight;
        Columns = Math.Max(1, (int)MathF.Ceiling(bounds.Width / cellSize));
        Rows = Math.Max(1, (int)MathF.Ceiling(bounds.Height / cellSize));
        _surfaces = surfaces;
        _cells = cells;
        _fineHeights = fineHeights;
        _objects = objects;
    }

    public War3MapMetadata Metadata { get; }
    public SimRect Bounds { get; }
    public float CellSize { get; }
    public float CliffHeight { get; }
    public int Columns { get; }
    public int Rows { get; }
    public IReadOnlyList<War3MapObject> Objects => _objects;

    public static bool TryCreate(
        War3MapAsset asset,
        out War3MapEditDocument? document,
        out War3MapValidationResult validation)
    {
        if (!War3MapCodec.TryExpand(asset, out var runtime, out validation) ||
            runtime is null)
        {
            document = null;
            return false;
        }
        var terrain = runtime.Terrain;
        document = new War3MapEditDocument(
            new War3MapMetadata
            {
                Id = runtime.Metadata.Id,
                DisplayName = runtime.Metadata.DisplayName,
                Description = runtime.Metadata.Description,
                Author = runtime.Metadata.Author,
                RecommendedPlayers = runtime.Metadata.RecommendedPlayers,
                PreviewPath = runtime.Metadata.PreviewPath
            },
            terrain.Bounds,
            terrain.CellSize,
            terrain.CliffLevelHeight,
            terrain.Surfaces.ToArray(),
            terrain.Cells.ToArray(),
            terrain.FineHeightPoints.ToArray(),
            runtime.Objects.Select(CloneObject).ToList());
        return true;
    }

    public bool TryBuildTerrain(
        out TerrainMapSnapshot? terrain,
        out TerrainMapValidationResult validation) =>
        TerrainMapSnapshot.TryCreate(
            Bounds, CellSize, CliffHeight,
            _surfaces, _cells, _fineHeights,
            out terrain, out validation);

    public War3MapAsset CaptureAsset()
    {
        if (!TryBuildTerrain(out var terrain, out var validation) || terrain is null)
            throw new InvalidOperationException(
                $"Cannot capture invalid terrain: {validation.FirstError}.");
        return War3MapCodec.FromRuntime(Metadata, terrain, _objects);
    }

    public string CaptureJson() => War3MapCodec.Serialize(CaptureAsset());

    public int Apply(Vector2 position, War3MapBrush brush, int strokeSeed)
    {
        if (!Bounds.Contains(position)) return 0;
        if (brush.Tool is War3MapTool.PlaceSpawn or
            War3MapTool.PlaceGoldMine or
            War3MapTool.PlaceTree or
            War3MapTool.EraseObject)
            return ApplyObject(position, brush);
        var centerColumn = Math.Clamp(
            (int)MathF.Floor((position.X - Bounds.Min.X) / CellSize),
            0, Columns - 1);
        var centerRow = Math.Clamp(
            (int)MathF.Floor((position.Y - Bounds.Min.Y) / CellSize),
            0, Rows - 1);
        return brush.Tool is War3MapTool.RaiseHeight or
            War3MapTool.LowerHeight or
            War3MapTool.SmoothHeight or
            War3MapTool.PerturbHeight
            ? ApplyHeight(centerColumn, centerRow, brush, strokeSeed)
            : ApplyCells(centerColumn, centerRow, brush);
    }

    private int ApplyCells(int centerColumn, int centerRow, War3MapBrush brush)
    {
        var changed = 0;
        var radius = Math.Clamp(brush.RadiusCells, 0, 32);
        for (var row = Math.Max(0, centerRow - radius);
             row <= Math.Min(Rows - 1, centerRow + radius); row++)
        for (var column = Math.Max(0, centerColumn - radius);
             column <= Math.Min(Columns - 1, centerColumn + radius); column++)
        {
            if (!InsideShape(column - centerColumn, row - centerRow,
                    radius, brush.Shape))
                continue;
            var index = row * Columns + column;
            var before = _cells[index];
            var after = brush.Tool switch
            {
                War3MapTool.Surface => before with
                {
                    SurfaceId = (ushort)Math.Clamp(
                        brush.SurfaceId, 0, _surfaces.Length - 1)
                },
                War3MapTool.CliffLevel => before with
                {
                    CliffLevel = (byte)Math.Clamp(
                        (int)brush.CliffLevel, 0, TerrainMapSnapshot.MaximumCliffLevel)
                },
                War3MapTool.PlaceRamp => before with
                {
                    CliffLevel = (byte)Math.Clamp(
                        (int)brush.CliffLevel, 0, TerrainMapSnapshot.MaximumCliffLevel - 1),
                    Flags = (before.Flags | TerrainCellFlags.Ramp) &
                            ~TerrainCellFlags.Buildable,
                    RampDirection = brush.RampDirection
                },
                War3MapTool.DeleteRamp => before with
                {
                    Flags = before.Flags & ~TerrainCellFlags.Ramp,
                    RampDirection = TerrainRampDirection.None
                },
                War3MapTool.GroundPathing => before with
                {
                    Pathing = brush.Enabled
                        ? before.Pathing | TerrainPathing.Ground
                        : before.Pathing & ~TerrainPathing.Ground
                },
                War3MapTool.Buildable => before with
                {
                    Flags = brush.Enabled
                        ? before.Flags | TerrainCellFlags.Buildable
                        : before.Flags & ~TerrainCellFlags.Buildable
                },
                _ => before
            };
            if (after == before) continue;
            _cells[index] = after;
            changed++;
        }
        return changed;
    }

    private int ApplyHeight(
        int centerColumn,
        int centerRow,
        War3MapBrush brush,
        int strokeSeed)
    {
        var radius = Math.Clamp(brush.RadiusCells, 0, 32) + 1;
        var strength = Math.Clamp(brush.Strength, 0.05f, 32f);
        var source = brush.Tool == War3MapTool.SmoothHeight
            ? _fineHeights.ToArray()
            : _fineHeights;
        var changed = 0;
        for (var row = Math.Max(0, centerRow - radius);
             row <= Math.Min(Rows, centerRow + radius + 1); row++)
        for (var column = Math.Max(0, centerColumn - radius);
             column <= Math.Min(Columns, centerColumn + radius + 1); column++)
        {
            var dx = column - centerColumn - 0.5f;
            var dy = row - centerRow - 0.5f;
            if (!InsideShape(dx, dy, radius, brush.Shape)) continue;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            var falloff = brush.Shape == War3BrushShape.Square
                ? 1f
                : Math.Clamp(1f - distance / MathF.Max(1f, radius), 0.1f, 1f);
            var index = row * (Columns + 1) + column;
            var before = _fineHeights[index];
            var after = brush.Tool switch
            {
                War3MapTool.RaiseHeight => before + strength * falloff,
                War3MapTool.LowerHeight => before - strength * falloff,
                War3MapTool.SmoothHeight => Lerp(
                    before, NeighbourAverage(source, column, row),
                    Math.Clamp(strength / 16f, 0.05f, 1f) * falloff),
                War3MapTool.PerturbHeight => before +
                    SignedNoise(column, row, strokeSeed) * strength * falloff,
                _ => before
            };
            if (MathF.Abs(after - before) <= 0.0001f) continue;
            _fineHeights[index] = after;
            changed++;
        }
        return changed;
    }

    private int ApplyObject(Vector2 position, War3MapBrush brush)
    {
        if (brush.Tool == War3MapTool.EraseObject)
        {
            var radius = MathF.Max(CellSize, brush.RadiusCells * CellSize);
            var index = _objects.FindIndex(value =>
                Vector2.DistanceSquared(value.Position, position) <= radius * radius);
            if (index < 0) return 0;
            _objects.RemoveAt(index);
            return 1;
        }
        if (brush.Tool == War3MapTool.PlaceSpawn)
        {
            var existing = _objects.FirstOrDefault(value =>
                value.Kind == War3MapObjectKind.SpawnPoint &&
                value.OwnerSlot == brush.OwnerSlot);
            if (existing is not null)
            {
                existing.X = position.X;
                existing.Y = position.Y;
                return 1;
            }
        }
        var id = NextObjectId(brush.Tool switch
        {
            War3MapTool.PlaceSpawn => $"spawn-{Math.Max(1, brush.OwnerSlot)}",
            War3MapTool.PlaceGoldMine => "gold",
            _ => "tree"
        });
        _objects.Add(new War3MapObject
        {
            Id = id,
            Kind = brush.Tool switch
            {
                War3MapTool.PlaceSpawn => War3MapObjectKind.SpawnPoint,
                War3MapTool.PlaceGoldMine => War3MapObjectKind.GoldMine,
                _ => War3MapObjectKind.Tree
            },
            X = position.X,
            Y = position.Y,
            RadiusX = brush.Tool == War3MapTool.PlaceGoldMine ? 52f :
                brush.Tool == War3MapTool.PlaceSpawn ? 32f : 13f,
            RadiusY = brush.Tool == War3MapTool.PlaceGoldMine ? 42f :
                brush.Tool == War3MapTool.PlaceSpawn ? 32f : 13f,
            OwnerSlot = brush.OwnerSlot,
            Amount = brush.Tool == War3MapTool.PlaceGoldMine
                ? 25_000
                : brush.Tool == War3MapTool.PlaceTree
                    ? War3HumanScenario.TreeHealth
                    : 0,
            Prototype = brush.Tool switch
            {
                War3MapTool.PlaceSpawn => "human_start",
                War3MapTool.PlaceGoldMine => "war3_gold_mine",
                _ => "lordaeron_tree"
            }
        });
        return 1;
    }

    private float NeighbourAverage(float[] source, int column, int row)
    {
        var total = 0f;
        var count = 0;
        for (var y = Math.Max(0, row - 1); y <= Math.Min(Rows, row + 1); y++)
        for (var x = Math.Max(0, column - 1); x <= Math.Min(Columns, column + 1); x++)
        {
            total += source[y * (Columns + 1) + x];
            count++;
        }
        return total / Math.Max(1, count);
    }

    private string NextObjectId(string prefix)
    {
        if (_objects.All(value => !value.Id.Equals(prefix,
                StringComparison.OrdinalIgnoreCase)))
            return prefix;
        for (var index = 1; ; index++)
        {
            var candidate = $"{prefix}-{index}";
            if (_objects.All(value => !value.Id.Equals(candidate,
                    StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private static bool InsideShape(
        float dx,
        float dy,
        int radius,
        War3BrushShape shape) => shape switch
    {
        War3BrushShape.Circle => dx * dx + dy * dy <= radius * radius,
        War3BrushShape.Diamond => MathF.Abs(dx) + MathF.Abs(dy) <= radius,
        _ => MathF.Abs(dx) <= radius && MathF.Abs(dy) <= radius
    };

    private static float SignedNoise(int x, int y, int seed)
    {
        var value = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ seed));
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        return (value / (float)uint.MaxValue) * 2f - 1f;
    }

    private static float Lerp(float from, float to, float amount) =>
        from + (to - from) * amount;

    private static War3MapObject CloneObject(War3MapObject value) => new()
    {
        Id = value.Id,
        Kind = value.Kind,
        X = value.X,
        Y = value.Y,
        RadiusX = value.RadiusX,
        RadiusY = value.RadiusY,
        OwnerSlot = value.OwnerSlot,
        Amount = value.Amount,
        Prototype = value.Prototype
    };
}
