using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class SpatialHash
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<int>> _buckets = new();
    private float _maximumRadius;

    public SpatialHash(float cellSize)
    {
        if (cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        _cellSize = cellSize;
    }

    public float MaximumRadius => _maximumRadius;

    public void Rebuild(UnitStore units)
    {
        _maximumRadius = 0f;
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
        }

        for (var i = 0; i < units.Count; i++)
        {
            if (!units.Alive[i])
            {
                continue;
            }
            _maximumRadius = MathF.Max(_maximumRadius, units.Radii[i]);
            var (x, y) = Cell(units.Positions[i]);
            var key = Key(x, y);
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<int>(16);
                _buckets.Add(key, bucket);
            }

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
                if (!_buckets.TryGetValue(Key(x, y), out var bucket))
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

        foreach (var entry in _buckets)
        {
            var bucket = entry.Value;
            if (bucket.Count == 0)
                continue;

            CollectWithinBucket(
                units, bucket, maximumDistanceSquared,
                ref output, ref count);

            var cellX = (int)(entry.Key >> 32);
            var cellY = (int)entry.Key;
            for (var deltaY = 0; deltaY <= cellRange; deltaY++)
            {
                var minimumDeltaX = deltaY == 0 ? 1 : -cellRange;
                for (var deltaX = minimumDeltaX;
                     deltaX <= cellRange;
                     deltaX++)
                {
                    if (!_buckets.TryGetValue(
                            Key(cellX + deltaX, cellY + deltaY),
                            out var neighborBucket) ||
                        neighborBucket.Count == 0)
                        continue;
                    CollectAcrossBuckets(
                        units, bucket, neighborBucket,
                        maximumDistanceSquared,
                        ref output, ref count);
                }
            }
        }

        Array.Sort(output, 0, count);
        return count;
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
