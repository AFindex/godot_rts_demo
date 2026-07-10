using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class SpatialHash
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<int>> _buckets = new();

    public SpatialHash(float cellSize)
    {
        if (cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        _cellSize = cellSize;
    }

    public void Rebuild(UnitStore units)
    {
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

    private (int X, int Y) Cell(Vector2 position) =>
        ((int)MathF.Floor(position.X / _cellSize),
         (int)MathF.Floor(position.Y / _cellSize));

    private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
}
