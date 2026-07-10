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
}
