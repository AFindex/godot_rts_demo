using System.Numerics;
using RtsDemo.Simulation;

namespace War3Rts.Pcg;

public readonly record struct PcgScatterSample(
    Vector2 Position,
    float Density,
    float Spacing);

public sealed record PcgScatterRequest(
    SimRect Bounds,
    int TargetCount,
    float MinimumSpacing,
    float MaximumSpacing,
    int MaximumAttempts,
    Func<Vector2, float> DensityAt,
    Func<Vector2, bool> IsAllowed);

/// <summary>
/// Density-aware point scatter with variable Poisson-style spacing. Dense
/// regions accept more candidates and use a smaller separation distance;
/// sparse fringes naturally receive fewer, farther-apart samples.
/// </summary>
public static class PcgPointScatter
{
    public static PcgScatterSample[] Scatter(
        ref DeterministicPcgRandom random,
        PcgScatterRequest request)
    {
        Validate(request);
        var accepted = new List<PcgScatterSample>(request.TargetCount);
        var buckets = new Dictionary<long, List<int>>();
        var cellSize = request.MinimumSpacing;
        var neighborRange = Math.Max(
            1, (int)MathF.Ceiling(request.MaximumSpacing / cellSize) + 1);

        for (var attempt = 0;
             attempt < request.MaximumAttempts &&
             accepted.Count < request.TargetCount;
             attempt++)
        {
            var candidate = new Vector2(
                random.Range(request.Bounds.Min.X, request.Bounds.Max.X),
                random.Range(request.Bounds.Min.Y, request.Bounds.Max.Y));
            if (!request.IsAllowed(candidate))
                continue;

            var density = Math.Clamp(request.DensityAt(candidate), 0f, 1f);
            if (!float.IsFinite(density))
            {
                throw new InvalidOperationException(
                    "PCG density callback returned a non-finite value.");
            }
            if (density <= 0f || random.NextSingle() > density)
                continue;

            var spacing = Lerp(
                request.MaximumSpacing, request.MinimumSpacing, density);
            if (!HasClearance(
                    candidate, spacing, accepted, buckets,
                    cellSize, neighborRange))
            {
                continue;
            }

            var sample = new PcgScatterSample(candidate, density, spacing);
            var index = accepted.Count;
            accepted.Add(sample);
            var key = BucketKey(candidate, cellSize);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets.Add(key, bucket);
            }
            bucket.Add(index);
        }

        return accepted.ToArray();
    }

    private static bool HasClearance(
        Vector2 candidate,
        float candidateSpacing,
        List<PcgScatterSample> accepted,
        Dictionary<long, List<int>> buckets,
        float cellSize,
        int neighborRange)
    {
        var bucketX = (int)MathF.Floor(candidate.X / cellSize);
        var bucketY = (int)MathF.Floor(candidate.Y / cellSize);
        for (var y = bucketY - neighborRange;
             y <= bucketY + neighborRange;
             y++)
        for (var x = bucketX - neighborRange;
             x <= bucketX + neighborRange;
             x++)
        {
            if (!buckets.TryGetValue(Key(x, y), out var bucket))
                continue;
            foreach (var index in bucket)
            {
                var other = accepted[index];
                var separation = (candidateSpacing + other.Spacing) * 0.5f;
                if (Vector2.DistanceSquared(candidate, other.Position) <
                    separation * separation)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static long BucketKey(Vector2 point, float cellSize) => Key(
        (int)MathF.Floor(point.X / cellSize),
        (int)MathF.Floor(point.Y / cellSize));

    private static long Key(int x, int y) =>
        ((long)x << 32) | (uint)y;

    private static float Lerp(float from, float to, float amount) =>
        from + (to - from) * amount;

    private static void Validate(PcgScatterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DensityAt);
        ArgumentNullException.ThrowIfNull(request.IsAllowed);
        if (!float.IsFinite(request.Bounds.Min.X) ||
            !float.IsFinite(request.Bounds.Min.Y) ||
            !float.IsFinite(request.Bounds.Max.X) ||
            !float.IsFinite(request.Bounds.Max.Y) ||
            request.Bounds.Width <= 0f || request.Bounds.Height <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Bounds));
        }
        if (request.TargetCount < 0)
            throw new ArgumentOutOfRangeException(nameof(request.TargetCount));
        if (!float.IsFinite(request.MinimumSpacing) ||
            request.MinimumSpacing <= 0f ||
            !float.IsFinite(request.MaximumSpacing) ||
            request.MaximumSpacing < request.MinimumSpacing)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MinimumSpacing));
        }
        if (request.MaximumAttempts < request.TargetCount)
            throw new ArgumentOutOfRangeException(nameof(request.MaximumAttempts));
    }
}
