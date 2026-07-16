using System.Numerics;
using RtsDemo.Simulation;

namespace War3Rts.Pcg;

public sealed class War3BattlefieldPcgLayout
{
    private readonly Vector2[] _neutralGoldPositions;
    private readonly Vector2[] _forestTreePositions;

    internal War3BattlefieldPcgLayout(
        int seed,
        Vector2[] neutralGoldPositions,
        Vector2[] forestTreePositions,
        int denseTreeCount)
    {
        Seed = seed;
        _neutralGoldPositions = neutralGoldPositions;
        _forestTreePositions = forestTreePositions;
        DenseTreeCount = denseTreeCount;
        StableHash = ComputeHash(neutralGoldPositions, forestTreePositions);
    }

    public int Seed { get; }
    public ReadOnlySpan<Vector2> NeutralGoldPositions => _neutralGoldPositions;
    public ReadOnlySpan<Vector2> ForestTreePositions => _forestTreePositions;
    public int DenseTreeCount { get; }
    public int SparseTreeCount => _forestTreePositions.Length - DenseTreeCount;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    private static ulong ComputeHash(
        ReadOnlySpan<Vector2> golds,
        ReadOnlySpan<Vector2> trees)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var point in golds)
        {
            hash = unchecked(
                (hash ^ (uint)BitConverter.SingleToInt32Bits(point.X)) * prime);
            hash = unchecked(
                (hash ^ (uint)BitConverter.SingleToInt32Bits(point.Y)) * prime);
        }
        foreach (var point in trees)
        {
            hash = unchecked(
                (hash ^ (uint)BitConverter.SingleToInt32Bits(point.X)) * prime);
            hash = unchecked(
                (hash ^ (uint)BitConverter.SingleToInt32Bits(point.Y)) * prime);
        }
        return hash;
    }
}

/// <summary>
/// Authored PCG recipe for the Human skirmish battlefield. The layout is
/// mirrored across the vertical axis for competitive parity, while each forest
/// uses a density field to form packed cores and loose, irregular edges.
/// </summary>
public static class War3BattlefieldPcg
{
    public const int Seed = 0x3A17_2026;
    public const int ForestTreeCount = 252;
    public const float TreeMinimumSpacing = 32f;
    private const float DenseThreshold = 0.58f;

    public static War3BattlefieldPcgLayout Generate(
        SimRect bounds,
        Vector2 playerHome,
        Vector2 enemyHome)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var golds = NeutralGolds(bounds);
        var clusters = ForestClusters(bounds);
        var scatterBounds = new SimRect(
            bounds.Min + new Vector2(96f),
            new Vector2(center.X - 96f, bounds.Max.Y - 96f));
        var random = new DeterministicPcgRandom(
            unchecked((ulong)(uint)Seed), 0x5741_5233UL);
        var samples = PcgPointScatter.Scatter(
            ref random,
            new PcgScatterRequest(
                scatterBounds,
                ForestTreeCount / 2,
                TreeMinimumSpacing,
                82f,
                160_000,
                point => DensityAt(point, clusters),
                point => IsForestAllowed(
                    point, bounds, center, playerHome, enemyHome, golds)));
        if (samples.Length != ForestTreeCount / 2)
        {
            throw new InvalidOperationException(
                $"War3 forest PCG produced {samples.Length * 2}/" +
                $"{ForestTreeCount} trees.");
        }

        var trees = new Vector2[ForestTreeCount];
        var denseTrees = 0;
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            var mirror = new Vector2(
                center.X * 2f - sample.Position.X,
                sample.Position.Y);
            trees[index * 2] = sample.Position;
            trees[index * 2 + 1] = mirror;
            if (sample.Density >= DenseThreshold)
                denseTrees += 2;
        }
        Array.Sort(trees, static (left, right) =>
        {
            var row = left.Y.CompareTo(right.Y);
            return row != 0 ? row : left.X.CompareTo(right.X);
        });
        ValidateLayout(bounds, playerHome, enemyHome, golds, trees);
        return new War3BattlefieldPcgLayout(
            Seed, golds, trees, denseTrees);
    }

    private static Vector2[] NeutralGolds(SimRect bounds)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var xOffset = bounds.Width * 0.125f;
        var yOffset = bounds.Height * 0.3125f;
        return
        [
            center + new Vector2(-xOffset, -yOffset),
            center + new Vector2(xOffset, -yOffset),
            center + new Vector2(-xOffset, yOffset),
            center + new Vector2(xOffset, yOffset)
        ];
    }

    private static ForestCluster[] ForestClusters(SimRect bounds)
    {
        Vector2 At(float x, float y) =>
            bounds.Min + new Vector2(bounds.Width * x, bounds.Height * y);
        return
        [
            new(At(0.055f, 0.13f), 520f, 390f, 1.00f),
            new(At(0.17f, 0.11f), 690f, 430f, 0.96f),
            new(At(0.31f, 0.16f), 660f, 470f, 0.90f),
            new(At(0.43f, 0.28f), 510f, 430f, 0.82f),
            new(At(0.045f, 0.50f), 420f, 720f, 0.95f),
            new(At(0.055f, 0.87f), 540f, 400f, 0.98f),
            new(At(0.18f, 0.90f), 720f, 410f, 1.00f),
            new(At(0.32f, 0.84f), 650f, 470f, 0.91f),
            new(At(0.43f, 0.72f), 520f, 430f, 0.84f)
        ];
    }

    private static float DensityAt(
        Vector2 point,
        ReadOnlySpan<ForestCluster> clusters)
    {
        var density = 0f;
        foreach (var cluster in clusters)
        {
            var offset = point - cluster.Center;
            var distance = MathF.Sqrt(
                offset.X * offset.X / (cluster.RadiusX * cluster.RadiusX) +
                offset.Y * offset.Y / (cluster.RadiusY * cluster.RadiusY));
            var falloff = SmoothStep(0f, 1f, 1f - distance);
            density = MathF.Max(density, cluster.Strength * falloff);
        }
        return density;
    }

    private static bool IsForestAllowed(
        Vector2 point,
        SimRect bounds,
        Vector2 center,
        Vector2 playerHome,
        Vector2 enemyHome,
        ReadOnlySpan<Vector2> golds)
    {
        if (!bounds.Inset(78f).Contains(point))
            return false;

        // High-ground construction plates and their ramp shoulders remain open.
        if (Around(playerHome, 720f, 780f).Contains(point) ||
            Around(enemyHome, 720f, 780f).Contains(point))
        {
            return false;
        }

        // Preserve the primary horizontal battle lane between both ramp mouths.
        if (point.X >= playerHome.X + 540f &&
            point.X <= enemyHome.X - 540f &&
            MathF.Abs(point.Y - center.Y) < 250f)
        {
            return false;
        }

        foreach (var gold in golds)
        {
            if (Vector2.DistanceSquared(point, gold) < 235f * 235f)
                return false;
            var laneEnd = new Vector2(
                gold.X + (gold.X < center.X ? 180f : -180f),
                center.Y + MathF.Sign(gold.Y - center.Y) * 230f);
            if (DistanceSquaredToSegment(point, gold, laneEnd) < 110f * 110f)
                return false;
        }
        return true;
    }

    private static void ValidateLayout(
        SimRect bounds,
        Vector2 playerHome,
        Vector2 enemyHome,
        ReadOnlySpan<Vector2> golds,
        ReadOnlySpan<Vector2> trees)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        for (var index = 0; index < trees.Length; index++)
        {
            var tree = trees[index];
            if (!IsForestAllowed(
                    tree, bounds, center, playerHome, enemyHome, golds))
            {
                throw new InvalidOperationException(
                    $"War3 forest tree {index} is inside an exclusion zone.");
            }
            for (var other = index + 1; other < trees.Length; other++)
            {
                if (Vector2.DistanceSquared(tree, trees[other]) <
                    TreeMinimumSpacing * TreeMinimumSpacing)
                {
                    throw new InvalidOperationException(
                        $"War3 forest trees {index}/{other} overlap.");
                }
            }
        }
    }

    private static SimRect Around(
        Vector2 center,
        float halfWidth,
        float halfHeight) => new(
        center - new Vector2(halfWidth, halfHeight),
        center + new Vector2(halfWidth, halfHeight));

    private static float DistanceSquaredToSegment(
        Vector2 point,
        Vector2 from,
        Vector2 to)
    {
        var segment = to - from;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.0001f)
            return Vector2.DistanceSquared(point, from);
        var amount = Math.Clamp(
            Vector2.Dot(point - from, segment) / lengthSquared, 0f, 1f);
        return Vector2.DistanceSquared(point, from + segment * amount);
    }

    private static float SmoothStep(float minimum, float maximum, float value)
    {
        var amount = Math.Clamp(
            (value - minimum) / MathF.Max(0.0001f, maximum - minimum),
            0f,
            1f);
        return amount * amount * (3f - 2f * amount);
    }

    private readonly record struct ForestCluster(
        Vector2 Center,
        float RadiusX,
        float RadiusY,
        float Strength);
}
