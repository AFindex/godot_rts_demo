using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct DynamicFootprintId(int Value);

public readonly record struct DynamicFootprint(
    DynamicFootprintId Id,
    SimRect Bounds,
    int PlacedRevision);

public sealed class DynamicOccupancyGrid
{
    private readonly SimRect _bounds;
    private readonly int _columns;
    private readonly int _rows;
    private readonly ushort[] _occupancy;
    private readonly List<int>?[] _footprintsByCell;
    private readonly Dictionary<int, FootprintEntry> _footprints = new();
    private int _nextId = 1;

    public DynamicOccupancyGrid(SimRect bounds, float cellSize = 16f)
    {
        if (cellSize <= 0f || !float.IsFinite(cellSize))
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        _bounds = bounds;
        CellSize = cellSize;
        _columns = Math.Max(1, (int)MathF.Ceiling(bounds.Width / cellSize));
        _rows = Math.Max(1, (int)MathF.Ceiling(bounds.Height / cellSize));
        _occupancy = new ushort[_columns * _rows];
        _footprintsByCell = new List<int>?[_occupancy.Length];
    }

    public float CellSize { get; }
    public int Revision { get; private set; }
    public int Count => _footprints.Count;

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(Revision);
        hash.Add(_nextId);
        var footprints = Snapshot();
        hash.Add(footprints.Length);
        for (var index = 0; index < footprints.Length; index++)
        {
            hash.Add(footprints[index].Id.Value);
            hash.Add(footprints[index].Bounds.Min);
            hash.Add(footprints[index].Bounds.Max);
            hash.Add(footprints[index].PlacedRevision);
        }
    }

    public DynamicFootprintId Place(SimRect footprint)
    {
        ValidateFootprint(footprint);
        var id = new DynamicFootprintId(_nextId++);
        Revision++;
        var cells = CollectCells(footprint);
        var value = new DynamicFootprint(id, footprint, Revision);
        _footprints.Add(id.Value, new FootprintEntry(value, cells));

        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            if (_occupancy[cell] < ushort.MaxValue)
            {
                _occupancy[cell]++;
            }

            var cellFootprints = _footprintsByCell[cell] ??= [];
            cellFootprints.Add(id.Value);
        }

        return id;
    }

    public bool Remove(DynamicFootprintId id, out SimRect removedBounds)
    {
        if (!_footprints.Remove(id.Value, out var entry))
        {
            removedBounds = default;
            return false;
        }

        for (var index = 0; index < entry.Cells.Length; index++)
        {
            var cell = entry.Cells[index];
            if (_occupancy[cell] > 0)
            {
                _occupancy[cell]--;
            }

            _footprintsByCell[cell]?.Remove(id.Value);
        }

        Revision++;
        removedBounds = entry.Value.Bounds;
        return true;
    }

    public DynamicFootprint[] Snapshot() =>
        _footprints.Values
            .Select(entry => entry.Value)
            .OrderBy(footprint => footprint.Id.Value)
            .ToArray();

    public bool IsDiscFree(Vector2 position, float radius)
    {
        var query = new SimRect(
            position - new Vector2(radius),
            position + new Vector2(radius));
        var range = GetCellRange(query);
        for (var row = range.MinimumRow; row <= range.MaximumRow; row++)
        {
            for (var column = range.MinimumColumn; column <= range.MaximumColumn; column++)
            {
                var cell = row * _columns + column;
                if (_occupancy[cell] == 0)
                {
                    continue;
                }

                var candidates = _footprintsByCell[cell];
                if (candidates is null)
                {
                    continue;
                }

                for (var index = 0; index < candidates.Count; index++)
                {
                    if (_footprints[candidates[index]].Value.Bounds
                        .Expanded(radius).Contains(position))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public bool IsSegmentFree(Vector2 from, Vector2 to, float radius)
    {
        var query = new SimRect(Vector2.Min(from, to), Vector2.Max(from, to)).Expanded(radius);
        var range = GetCellRange(query);
        for (var row = range.MinimumRow; row <= range.MaximumRow; row++)
        {
            for (var column = range.MinimumColumn; column <= range.MaximumColumn; column++)
            {
                var cell = row * _columns + column;
                if (_occupancy[cell] == 0)
                {
                    continue;
                }

                var candidates = _footprintsByCell[cell];
                if (candidates is null)
                {
                    continue;
                }

                for (var index = 0; index < candidates.Count; index++)
                {
                    if (_footprints[candidates[index]].Value.Bounds
                        .Expanded(radius).SegmentIntersects(from, to))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public Vector2 ConstrainDisc(Vector2 previous, Vector2 proposed, float radius)
    {
        foreach (var entry in _footprints.Values)
        {
            var expanded = entry.Value.Bounds.Expanded(radius);
            if (!expanded.Contains(proposed))
            {
                continue;
            }

            if (!expanded.Contains(previous))
            {
                var xOnly = new Vector2(proposed.X, previous.Y);
                var yOnly = new Vector2(previous.X, proposed.Y);
                var xFree = !expanded.Contains(xOnly);
                var yFree = !expanded.Contains(yOnly);
                if (xFree && (!yFree || Vector2.DistanceSquared(xOnly, proposed) <=
                    Vector2.DistanceSquared(yOnly, proposed)))
                {
                    proposed = xOnly;
                    continue;
                }

                if (yFree)
                {
                    proposed = yOnly;
                    continue;
                }
            }

            proposed = expanded.PushOutside(proposed);
        }

        return proposed;
    }

    private int[] CollectCells(SimRect footprint)
    {
        var range = GetCellRange(footprint);
        var result = new int[(range.MaximumColumn - range.MinimumColumn + 1) *
                             (range.MaximumRow - range.MinimumRow + 1)];
        var write = 0;
        for (var row = range.MinimumRow; row <= range.MaximumRow; row++)
        {
            for (var column = range.MinimumColumn; column <= range.MaximumColumn; column++)
            {
                result[write++] = row * _columns + column;
            }
        }

        return result;
    }

    private CellRange GetCellRange(SimRect area)
    {
        var minimumColumn = Math.Clamp(
            (int)MathF.Floor((area.Min.X - _bounds.Min.X) / CellSize), 0, _columns - 1);
        var maximumColumn = Math.Clamp(
            (int)MathF.Floor((area.Max.X - _bounds.Min.X) / CellSize), 0, _columns - 1);
        var minimumRow = Math.Clamp(
            (int)MathF.Floor((area.Min.Y - _bounds.Min.Y) / CellSize), 0, _rows - 1);
        var maximumRow = Math.Clamp(
            (int)MathF.Floor((area.Max.Y - _bounds.Min.Y) / CellSize), 0, _rows - 1);
        return new CellRange(minimumColumn, maximumColumn, minimumRow, maximumRow);
    }

    private void ValidateFootprint(SimRect footprint)
    {
        if (footprint.Width <= 0f || footprint.Height <= 0f ||
            !_bounds.Contains(footprint.Min) || !_bounds.Contains(footprint.Max))
        {
            throw new ArgumentException("Dynamic footprint must be non-empty and inside world bounds.",
                nameof(footprint));
        }
    }

    private readonly record struct CellRange(
        int MinimumColumn,
        int MaximumColumn,
        int MinimumRow,
        int MaximumRow);

    private readonly record struct FootprintEntry(DynamicFootprint Value, int[] Cells);
}
