using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class SpatialHash
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<int>> _buckets = new();
    private readonly List<int> _activeDenseBuckets = [];
    private readonly List<long> _activeOverflowBuckets = [];
    private readonly List<int>?[] _denseBuckets;
    private readonly int _minimumCellX;
    private readonly int _minimumCellY;
    private readonly int _denseColumns;
    private readonly int _denseRows;
    private float _maximumRadius;

    public SpatialHash(float cellSize, SimRect? bounds = null)
    {
        if (cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        _cellSize = cellSize;
        if (bounds is not { } denseBounds)
        {
            _denseBuckets = [];
            return;
        }
        _minimumCellX = (int)MathF.Floor(denseBounds.Min.X / cellSize);
        _minimumCellY = (int)MathF.Floor(denseBounds.Min.Y / cellSize);
        var maximumCellX = (int)MathF.Floor(denseBounds.Max.X / cellSize);
        var maximumCellY = (int)MathF.Floor(denseBounds.Max.Y / cellSize);
        _denseColumns = maximumCellX - _minimumCellX + 1;
        _denseRows = maximumCellY - _minimumCellY + 1;
        _denseBuckets = new List<int>?[_denseColumns * _denseRows];
    }

    public float MaximumRadius => _maximumRadius;

    public void Rebuild(UnitStore units)
    {
        _maximumRadius = 0f;
        for (var index = 0; index < _activeDenseBuckets.Count; index++)
            _denseBuckets[_activeDenseBuckets[index]]!.Clear();
        _activeDenseBuckets.Clear();
        for (var index = 0; index < _activeOverflowBuckets.Count; index++)
        {
            if (_buckets.TryGetValue(
                    _activeOverflowBuckets[index], out var overflow))
                overflow.Clear();
        }
        _activeOverflowBuckets.Clear();

        foreach (var i in units.AliveUnits)
        {
            _maximumRadius = MathF.Max(_maximumRadius, units.Radii[i]);
            var (x, y) = Cell(units.Positions[i]);
            var bucket = GetOrCreateBucket(x, y);
            bucket.Add(i);
        }
    }

    public int Query(Vector2 center, float radius, int exclude, Span<int> output)
    {
        var minimum = Cell(center - new Vector2(radius));
        var maximum = Cell(center + new Vector2(radius));
        var count = 0;

        for (var y = minimum.Y; y <= maximum.Y; y++)
        {
            for (var x = minimum.X; x <= maximum.X; x++)
            {
                var bucket = GetBucket(x, y);
                if (bucket is null)
                {
                    continue;
                }

                for (var i = 0; i < bucket.Count && count < output.Length; i++)
                {
                    var unit = bucket[i];
                    if (unit != exclude)
                    {
                        output[count++] = unit;
                    }
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Collects every unordered unit pair that can possibly overlap. Bucket
    /// pairs are visited only once and the final unit-pair order is sorted, so
    /// collision resolution is independent of dictionary insertion history.
    /// The scratch array grows only when a denser crowd is first encountered.
    /// </summary>
    public int CollectPotentialCollisionPairs(
        UnitStore units,
        float tolerance,
        ref long[] output)
    {
        if (_maximumRadius <= 0f)
            return 0;

        var maximumDistance = _maximumRadius * 2f +
                              MathF.Max(0f, tolerance);
        var maximumDistanceSquared = maximumDistance * maximumDistance;
        var cellRange = Math.Max(
            1,
            (int)MathF.Ceiling(maximumDistance / _cellSize));
        var count = 0;

        for (var index = 0; index < _activeDenseBuckets.Count; index++)
        {
            var denseIndex = _activeDenseBuckets[index];
            var cellX = denseIndex % _denseColumns + _minimumCellX;
            var cellY = denseIndex / _denseColumns + _minimumCellY;
            CollectBucketPairs(
                units, _denseBuckets[denseIndex]!, cellX, cellY,
                cellRange, maximumDistanceSquared, ref output, ref count);
        }
        for (var index = 0; index < _activeOverflowBuckets.Count; index++)
        {
            var key = _activeOverflowBuckets[index];
            CollectBucketPairs(
                units, _buckets[key], (int)(key >> 32), (int)key,
                cellRange, maximumDistanceSquared, ref output, ref count);
        }

        Array.Sort(output, 0, count);
        return count;
    }

    private void CollectBucketPairs(
        UnitStore units,
        List<int> bucket,
        int cellX,
        int cellY,
        int cellRange,
        float maximumDistanceSquared,
        ref long[] output,
        ref int count)
    {
        CollectWithinBucket(
            units, bucket, maximumDistanceSquared, ref output, ref count);
        for (var deltaY = 0; deltaY <= cellRange; deltaY++)
        {
            var minimumDeltaX = deltaY == 0 ? 1 : -cellRange;
            for (var deltaX = minimumDeltaX;
                 deltaX <= cellRange;
                 deltaX++)
            {
                var neighborBucket = GetBucket(
                    cellX + deltaX, cellY + deltaY);
                if (neighborBucket is null) continue;
                CollectAcrossBuckets(
                    units, bucket, neighborBucket,
                    maximumDistanceSquared, ref output, ref count);
            }
        }
    }

    private List<int> GetOrCreateBucket(int x, int y)
    {
        if (TryDenseIndex(x, y, out var index))
        {
            var bucket = _denseBuckets[index] ??= new List<int>(16);
            if (bucket.Count == 0) _activeDenseBuckets.Add(index);
            return bucket;
        }
        var key = Key(x, y);
        if (!_buckets.TryGetValue(key, out var overflow))
        {
            overflow = new List<int>(16);
            _buckets.Add(key, overflow);
        }
        if (overflow.Count == 0) _activeOverflowBuckets.Add(key);
        return overflow;
    }

    private List<int>? GetBucket(int x, int y)
    {
        if (TryDenseIndex(x, y, out var index))
        {
            var bucket = _denseBuckets[index];
            return bucket is { Count: > 0 } ? bucket : null;
        }
        return _buckets.TryGetValue(Key(x, y), out var overflow) &&
               overflow.Count > 0
            ? overflow
            : null;
    }

    private bool TryDenseIndex(int x, int y, out int index)
    {
        var column = x - _minimumCellX;
        var row = y - _minimumCellY;
        if ((uint)column >= (uint)_denseColumns ||
            (uint)row >= (uint)_denseRows)
        {
            index = -1;
            return false;
        }
        index = row * _denseColumns + column;
        return true;
    }

    private static void CollectWithinBucket(
        UnitStore units,
        List<int> bucket,
        float maximumDistanceSquared,
        ref long[] output,
        ref int count)
    {
        for (var leftIndex = 0; leftIndex < bucket.Count; leftIndex++)
        {
            var left = bucket[leftIndex];
            for (var rightIndex = leftIndex + 1;
                 rightIndex < bucket.Count;
                 rightIndex++)
                AddPairIfNear(
                    units, left, bucket[rightIndex],
                    maximumDistanceSquared, ref output, ref count);
        }
    }

    private static void CollectAcrossBuckets(
        UnitStore units,
        List<int> leftBucket,
        List<int> rightBucket,
        float maximumDistanceSquared,
        ref long[] output,
        ref int count)
    {
        for (var leftIndex = 0;
             leftIndex < leftBucket.Count;
             leftIndex++)
        {
            var left = leftBucket[leftIndex];
            for (var rightIndex = 0;
                 rightIndex < rightBucket.Count;
                 rightIndex++)
                AddPairIfNear(
                    units, left, rightBucket[rightIndex],
                    maximumDistanceSquared, ref output, ref count);
        }
    }

    private static void AddPairIfNear(
        UnitStore units,
        int first,
        int second,
        float maximumDistanceSquared,
        ref long[] output,
        ref int count)
    {
        if (Vector2.DistanceSquared(
                units.Positions[first],
                units.Positions[second]) > maximumDistanceSquared)
            return;

        if (count == output.Length)
            Array.Resize(ref output, Math.Max(256, output.Length * 2));
        var left = Math.Min(first, second);
        var right = Math.Max(first, second);
        output[count++] = ((long)left << 32) | (uint)right;
    }

    private (int X, int Y) Cell(Vector2 position) =>
        ((int)MathF.Floor(position.X / _cellSize),
         (int)MathF.Floor(position.Y / _cellSize));

    private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
}
