using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct SimRect(Vector2 Min, Vector2 Max)
{
    public float Width => Max.X - Min.X;
    public float Height => Max.Y - Min.Y;

    public SimRect Expanded(float amount) =>
        new(Min - new Vector2(amount), Max + new Vector2(amount));

    public SimRect Inset(float amount) =>
        new(Min + new Vector2(amount), Max - new Vector2(amount));

    public bool Contains(Vector2 point) =>
        point.X >= Min.X && point.X <= Max.X &&
        point.Y >= Min.Y && point.Y <= Max.Y;

    public bool Intersects(SimRect other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y;

    public Vector2 Clamp(Vector2 point) => Vector2.Clamp(point, Min, Max);

    public float DistanceSquaredTo(Vector2 point)
    {
        var closest = Clamp(point);
        return Vector2.DistanceSquared(point, closest);
    }

    public bool OverlapsDisc(Vector2 center, float radius)
    {
        if (Contains(center))
            return true;
        return radius > 0f && DistanceSquaredTo(center) < radius * radius;
    }

    public bool IntersectsSweptDisc(Vector2 from, Vector2 to, float radius)
    {
        if (SegmentIntersects(from, to))
            return true;
        if (radius <= 0f)
            return false;

        // Exact broad phase for the rounded rectangle (this rectangle swept
        // by the disc radius). Equality is a tangent and the narrow phase
        // below uses a strict distance comparison, so it is also a miss.
        // Long movement probes commonly inspect sparse building footprints;
        // reject the distant ones before the twelve projection calculations.
        if (MathF.Max(from.X, to.X) <= Min.X - radius ||
            MathF.Min(from.X, to.X) >= Max.X + radius ||
            MathF.Max(from.Y, to.Y) <= Min.Y - radius ||
            MathF.Min(from.Y, to.Y) >= Max.Y + radius)
        {
            return false;
        }

        var topLeft = Min;
        var topRight = new Vector2(Max.X, Min.Y);
        var bottomRight = Max;
        var bottomLeft = new Vector2(Min.X, Max.Y);
        // Each edge-to-segment query includes both edge corners, so the four
        // corners were evaluated twice. Keep the exact same unique point to
        // segment candidates while avoiding those duplicate projections.
        var distanceSquared = float.PositiveInfinity;
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(from, topLeft, topRight));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(to, topLeft, topRight));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(from, topRight, bottomRight));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(to, topRight, bottomRight));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(from, bottomRight, bottomLeft));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(to, bottomRight, bottomLeft));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(from, bottomLeft, topLeft));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(to, bottomLeft, topLeft));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(topLeft, from, to));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(topRight, from, to));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(bottomRight, from, to));
        distanceSquared = MathF.Min(distanceSquared,
            PointSegmentDistanceSquared(bottomLeft, from, to));
        return distanceSquared < radius * radius;
    }

    public Vector2 ConstrainDiscOutside(
        Vector2 previous,
        Vector2 proposed,
        float radius)
    {
        if (!Contains(proposed))
        {
            var closest = Clamp(proposed);
            var offset = proposed - closest;
            var distanceSquared = offset.LengthSquared();
            if (radius <= 0f || distanceSquared >= radius * radius)
                return proposed;
            if (distanceSquared > 0f)
                return closest + offset / MathF.Sqrt(distanceSquared) * radius;
        }

        Span<Vector2> candidates = stackalloc Vector2[4];
        candidates[0] = new Vector2(Min.X - radius, proposed.Y);
        candidates[1] = new Vector2(Max.X + radius, proposed.Y);
        candidates[2] = new Vector2(proposed.X, Min.Y - radius);
        candidates[3] = new Vector2(proposed.X, Max.Y + radius);
        var best = candidates[0];
        var bestCorrection = Vector2.DistanceSquared(proposed, best);
        var bestContinuity = Vector2.DistanceSquared(previous, best);
        for (var index = 1; index < candidates.Length; index++)
        {
            var correction = Vector2.DistanceSquared(
                proposed, candidates[index]);
            var continuity = Vector2.DistanceSquared(
                previous, candidates[index]);
            if (correction > bestCorrection ||
                correction == bestCorrection && continuity >= bestContinuity)
            {
                continue;
            }
            best = candidates[index];
            bestCorrection = correction;
            bestContinuity = continuity;
        }
        return best;
    }

    public bool SegmentIntersects(Vector2 from, Vector2 to)
    {
        var direction = to - from;
        var tMin = 0f;
        var tMax = 1f;

        if (!ClipAxis(from.X, direction.X, Min.X, Max.X, ref tMin, ref tMax))
        {
            return false;
        }

        return ClipAxis(from.Y, direction.Y, Min.Y, Max.Y, ref tMin, ref tMax);
    }

    public Vector2 PushOutside(Vector2 point)
    {
        if (!Contains(point))
        {
            return point;
        }

        var toLeft = MathF.Abs(point.X - Min.X);
        var toRight = MathF.Abs(Max.X - point.X);
        var toTop = MathF.Abs(point.Y - Min.Y);
        var toBottom = MathF.Abs(Max.Y - point.Y);
        var minimum = MathF.Min(MathF.Min(toLeft, toRight), MathF.Min(toTop, toBottom));

        if (minimum == toLeft)
        {
            point.X = Min.X;
        }
        else if (minimum == toRight)
        {
            point.X = Max.X;
        }
        else if (minimum == toTop)
        {
            point.Y = Min.Y;
        }
        else
        {
            point.Y = Max.Y;
        }

        return point;
    }

    private static bool ClipAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float tMin,
        ref float tMax)
    {
        if (MathF.Abs(direction) < 0.00001f)
        {
            return origin >= minimum && origin <= maximum;
        }

        var inverse = 1f / direction;
        var a = (minimum - origin) * inverse;
        var b = (maximum - origin) * inverse;
        if (a > b)
        {
            (a, b) = (b, a);
        }

        tMin = MathF.Max(tMin, a);
        tMax = MathF.Min(tMax, b);
        return tMin <= tMax;
    }

    private static float PointSegmentDistanceSquared(
        Vector2 point,
        Vector2 start,
        Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0f)
            return Vector2.DistanceSquared(point, start);
        var amount = Math.Clamp(
            Vector2.Dot(point - start, segment) / lengthSquared,
            0f,
            1f);
        return Vector2.DistanceSquared(point, start + segment * amount);
    }
}
