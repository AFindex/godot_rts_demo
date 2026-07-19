using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct DynamicFootprintId(int Value);

public readonly record struct DynamicFootprint(
    DynamicFootprintId Id,
    SimRect Bounds,
    int PlacedRevision);

internal sealed record DynamicOccupancyRuntimeSnapshot(
    int Revision,
    int NextId,
    DynamicFootprint[] Footprints);

public sealed class DynamicOccupancyGrid
{
    private const int ChangeHistoryCapacity = 256;
    private readonly SimRect _bounds;
    private readonly int _columns;
    private readonly int _rows;
    private readonly ushort[] _occupancy;
    private readonly List<int>?[] _footprintsByCell;
    private readonly Dictionary<int, FootprintEntry> _footprints = new();
    private int[] _constraintCandidates = new int[16];
    private int[] _constraintCandidateMarks = new int[16];
    private int _constraintQueryStamp;
    private int _nextId = 1;
    private int _lastChangedRevision = -1;
    private SimRect _lastChangedBounds;
    private readonly Queue<RevisionChange> _changeHistory = new();

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
    internal long ConstraintCalls { get; private set; }
    internal long ConstraintCandidateChecks { get; private set; }

    internal void ResetConstraintDiagnostics()
    {
        ConstraintCalls = 0;
        ConstraintCandidateChecks = 0;
    }

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

    internal DynamicOccupancyRuntimeSnapshot CaptureRuntimeState() =>
        new(Revision, _nextId, Snapshot());

    internal void RestoreRuntimeState(DynamicOccupancyRuntimeSnapshot snapshot)
    {
        Array.Clear(_occupancy);
        for (var cell = 0; cell < _footprintsByCell.Length; cell++)
        {
            _footprintsByCell[cell]?.Clear();
        }
        _footprints.Clear();
        for (var index = 0; index < snapshot.Footprints.Length; index++)
        {
            var footprint = snapshot.Footprints[index];
            ValidateFootprint(footprint.Bounds);
            var cells = CollectCells(footprint.Bounds);
            _footprints.Add(footprint.Id.Value, new FootprintEntry(footprint, cells));
            for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                var cell = cells[cellIndex];
                _occupancy[cell]++;
                (_footprintsByCell[cell] ??= []).Add(footprint.Id.Value);
            }
        }
        Revision = snapshot.Revision;
        _nextId = snapshot.NextId;
        _lastChangedRevision = -1;
        _lastChangedBounds = default;
        _changeHistory.Clear();
    }

    public DynamicFootprintId Place(SimRect footprint)
    {
        ValidateFootprint(footprint);
        var id = new DynamicFootprintId(_nextId++);
        Revision++;
        _lastChangedRevision = Revision;
        _lastChangedBounds = footprint;
        RecordChange(footprint);
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

    /// <summary>
    /// Test/bootstrap-only bulk commit. All footprints become visible in one
    /// navigation revision so downstream incremental topology can resample one
    /// combined dirty area instead of replaying a long construction history.
    /// </summary>
    internal DynamicFootprintId[] PlaceBatchForDiagnostics(
        ReadOnlySpan<SimRect> footprints)
    {
        if (footprints.IsEmpty) return [];
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        for (var index = 0; index < footprints.Length; index++)
        {
            ValidateFootprint(footprints[index]);
            minimum = Vector2.Min(minimum, footprints[index].Min);
            maximum = Vector2.Max(maximum, footprints[index].Max);
        }

        Revision++;
        var changedBounds = new SimRect(minimum, maximum);
        _lastChangedRevision = Revision;
        _lastChangedBounds = changedBounds;
        RecordChange(changedBounds);
        var result = new DynamicFootprintId[footprints.Length];
        for (var footprintIndex = 0;
             footprintIndex < footprints.Length;
             footprintIndex++)
        {
            var bounds = footprints[footprintIndex];
            var id = new DynamicFootprintId(_nextId++);
            result[footprintIndex] = id;
            var cells = CollectCells(bounds);
            var value = new DynamicFootprint(id, bounds, Revision);
            _footprints.Add(id.Value, new FootprintEntry(value, cells));
            for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                var cell = cells[cellIndex];
                if (_occupancy[cell] < ushort.MaxValue) _occupancy[cell]++;
                (_footprintsByCell[cell] ??= []).Add(id.Value);
            }
        }
        return result;
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
        _lastChangedRevision = Revision;
        _lastChangedBounds = removedBounds;
        RecordChange(removedBounds);
        return true;
    }

    internal bool TryGetSingleChangeSince(
        int previousRevision,
        out SimRect changedBounds)
    {
        if (previousRevision == Revision - 1 &&
            _lastChangedRevision == Revision)
        {
            changedBounds = _lastChangedBounds;
            return true;
        }
        changedBounds = default;
        return false;
    }

    internal bool TryGetChangesSince(
        int previousRevision,
        out SimRect[] changedBounds)
    {
        if (previousRevision == Revision)
        {
            changedBounds = [];
            return true;
        }
        var required = Revision - previousRevision;
        if (previousRevision < 0 || required <= 0 ||
            required > _changeHistory.Count)
        {
            changedBounds = [];
            return false;
        }
        changedBounds = new SimRect[required];
        var write = 0;
        foreach (var change in _changeHistory)
        {
            if (change.Revision <= previousRevision) continue;
            if (write >= changedBounds.Length ||
                change.Revision != previousRevision + write + 1)
            {
                changedBounds = [];
                return false;
            }
            changedBounds[write++] = change.Bounds;
        }
        if (write == changedBounds.Length) return true;
        changedBounds = [];
        return false;
    }

    private void RecordChange(SimRect bounds)
    {
        _changeHistory.Enqueue(new RevisionChange(Revision, bounds));
        while (_changeHistory.Count > ChangeHistoryCapacity)
            _changeHistory.Dequeue();
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
                        .OverlapsDisc(position, radius))
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
        if (_footprints.Count == 0) return true;
        var query = new SimRect(Vector2.Min(from, to), Vector2.Max(from, to)).Expanded(radius);
        var range = GetCellRange(query);
        var cellsInQuery =
            (range.MaximumRow - range.MinimumRow + 1) *
            (range.MaximumColumn - range.MinimumColumn + 1);
        // Long diagonal commands can cover most of a large map's AABB even
        // when only a few dozen building footprints exist. In that case the
        // exact swept-disc test over each footprint is dramatically cheaper
        // than visiting tens of thousands of empty grid cells. Both branches
        // use the same final geometry predicate, so occupancy semantics do not
        // change.
        if (_footprints.Count <= cellsInQuery)
        {
            foreach (var entry in _footprints.Values)
            {
                if (entry.Value.Bounds.IntersectsSweptDisc(from, to, radius))
                    return false;
            }
            return true;
        }
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
                        .IntersectsSweptDisc(from, to, radius))
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
        ConstraintCalls++;
        if (_footprints.Count == 0) return proposed;

        var queryPadding = MathF.Max(0f, radius);
        var query = new SimRect(
            proposed - new Vector2(queryPadding),
            proposed + new Vector2(queryPadding));
        var candidateCount = CollectConstraintCandidates(query);
        ConstraintCandidateChecks += candidateCount;
        for (var index = 0; index < candidateCount; index++)
        {
            var footprint = _footprints[_constraintCandidates[index]]
                .Value.Bounds;
            if (!footprint.OverlapsDisc(proposed, radius))
            {
                continue;
            }
            proposed = footprint.ConstrainDiscOutside(
                previous, proposed, radius);
        }

        return proposed;
    }

    private int CollectConstraintCandidates(SimRect query)
    {
        if (++_constraintQueryStamp == int.MaxValue)
        {
            Array.Clear(_constraintCandidateMarks);
            _constraintQueryStamp = 1;
        }
        var count = 0;
        var range = GetCellRange(query);
        for (var row = range.MinimumRow; row <= range.MaximumRow; row++)
        {
            for (var column = range.MinimumColumn;
                 column <= range.MaximumColumn;
                 column++)
            {
                var candidates = _footprintsByCell[
                    row * _columns + column];
                if (candidates is null) continue;
                for (var index = 0; index < candidates.Count; index++)
                {
                    var id = candidates[index];
                    EnsureConstraintMarkCapacity(id);
                    if (_constraintCandidateMarks[id] ==
                        _constraintQueryStamp)
                        continue;
                    _constraintCandidateMarks[id] = _constraintQueryStamp;
                    if (count == _constraintCandidates.Length)
                        Array.Resize(
                            ref _constraintCandidates,
                            _constraintCandidates.Length * 2);
                    _constraintCandidates[count++] = id;
                }
            }
        }
        Array.Sort(_constraintCandidates, 0, count);
        return count;
    }

    private void EnsureConstraintMarkCapacity(int id)
    {
        if (id < _constraintCandidateMarks.Length) return;
        var capacity = _constraintCandidateMarks.Length;
        while (capacity <= id) capacity *= 2;
        Array.Resize(ref _constraintCandidateMarks, capacity);
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

    private readonly record struct RevisionChange(int Revision, SimRect Bounds);

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
