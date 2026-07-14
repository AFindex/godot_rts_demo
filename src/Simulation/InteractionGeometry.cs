using System.Numerics;

namespace RtsDemo.Simulation;

/// <summary>
/// Geometry used by gameplay qualification. Navigation targets may guide a unit
/// toward these shapes, but reaching a navigation point never replaces these
/// contact checks.
/// </summary>
public static class InteractionGeometry
{
    public static bool DiscTouchesRectangle(
        Vector2 center,
        float radius,
        SimRect rectangle,
        float extraClearance = 0f)
    {
        var tolerance = NumericTolerance(center, rectangle);
        var allowed = radius + extraClearance + tolerance;
        return rectangle.DistanceSquaredTo(center) <= allowed * allowed;
    }

    public static float NumericTolerance(Vector2 point, SimRect rectangle)
    {
        var largest = MathF.Max(
            MathF.Max(MathF.Abs(point.X), MathF.Abs(point.Y)),
            MathF.Max(
                MathF.Max(MathF.Abs(rectangle.Min.X), MathF.Abs(rectangle.Min.Y)),
                MathF.Max(MathF.Abs(rectangle.Max.X), MathF.Abs(rectangle.Max.Y))));
        return NumericTolerance(largest);
    }

    public static float NumericTolerance(Vector2 left, Vector2 right) =>
        NumericTolerance(MathF.Max(
            MathF.Max(MathF.Abs(left.X), MathF.Abs(left.Y)),
            MathF.Max(MathF.Abs(right.X), MathF.Abs(right.Y))));

    private static float NumericTolerance(float largest)
    {
        largest = MathF.Max(1f, largest);
        var next = MathF.BitIncrement(largest);
        return (next - largest) * 16f;
    }

    public static Vector2 ProjectFromCenter(
        SimRect rectangle,
        Vector2 origin,
        float clearance)
    {
        var center = (rectangle.Min + rectangle.Max) * 0.5f;
        var half = (rectangle.Max - rectangle.Min) * 0.5f;
        var direction = origin - center;
        if (direction.LengthSquared() <= 0f)
            return new Vector2(rectangle.Min.X - clearance, center.Y);

        direction = Vector2.Normalize(direction);
        var x = MathF.Abs(direction.X);
        var y = MathF.Abs(direction.Y);
        if (x > 0f)
        {
            var sideDistance = (half.X + clearance) / x;
            if (y * sideDistance <= half.Y)
                return center + direction * sideDistance;
        }
        if (y > 0f)
        {
            var sideDistance = (half.Y + clearance) / y;
            if (x * sideDistance <= half.X)
                return center + direction * sideDistance;
        }

        var projection = x * half.X + y * half.Y;
        var cornerDistanceSquared = half.LengthSquared() -
                                    clearance * clearance;
        var discriminant = MathF.Max(
            0f,
            projection * projection - cornerDistanceSquared);
        var distance = projection + MathF.Sqrt(discriminant);
        return center + direction * distance;
    }

    public static List<Vector2> SampleRoundedRectangleBoundary(
        SimRect rectangle,
        float clearance,
        float maximumSpacing)
    {
        if (!float.IsFinite(clearance) || clearance < 0f)
            throw new ArgumentOutOfRangeException(nameof(clearance));
        if (!float.IsFinite(maximumSpacing) || maximumSpacing <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maximumSpacing));

        var result = new List<Vector2>();
        AddSide(result,
            new(rectangle.Min.X, rectangle.Min.Y - clearance),
            new(rectangle.Max.X, rectangle.Min.Y - clearance), maximumSpacing);
        AddCornerArc(result,
            new(rectangle.Max.X, rectangle.Min.Y), clearance,
            -MathF.PI * 0.5f, 0f, maximumSpacing);
        AddSide(result,
            new(rectangle.Max.X + clearance, rectangle.Min.Y),
            new(rectangle.Max.X + clearance, rectangle.Max.Y), maximumSpacing);
        AddCornerArc(result,
            rectangle.Max, clearance,
            0f, MathF.PI * 0.5f, maximumSpacing);
        AddSide(result,
            new(rectangle.Max.X, rectangle.Max.Y + clearance),
            new(rectangle.Min.X, rectangle.Max.Y + clearance), maximumSpacing);
        AddCornerArc(result,
            new(rectangle.Min.X, rectangle.Max.Y), clearance,
            MathF.PI * 0.5f, MathF.PI, maximumSpacing);
        AddSide(result,
            new(rectangle.Min.X - clearance, rectangle.Max.Y),
            new(rectangle.Min.X - clearance, rectangle.Min.Y), maximumSpacing);
        AddCornerArc(result,
            rectangle.Min, clearance,
            MathF.PI, MathF.PI * 1.5f, maximumSpacing);
        return result;
    }

    private static void AddCornerArc(
        List<Vector2> result,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        float maximumSpacing)
    {
        var segments = Math.Max(
            1,
            (int)MathF.Ceiling(
                MathF.PI * 0.5f * radius / maximumSpacing));
        for (var index = 0; index <= segments; index++)
        {
            var angle = startAngle +
                        (endAngle - startAngle) * index / segments;
            var candidate = center +
                new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            if (result.Count == 0 || result[^1] != candidate)
                result.Add(candidate);
        }
    }

    private static void AddSide(
        List<Vector2> result,
        Vector2 start,
        Vector2 end,
        float maximumSpacing)
    {
        var segments = Math.Max(
            1,
            (int)MathF.Ceiling(Vector2.Distance(start, end) / maximumSpacing));
        for (var index = 0; index <= segments; index++)
        {
            var candidate = Vector2.Lerp(start, end, index / (float)segments);
            if (result.Count == 0 || result[^1] != candidate)
                result.Add(candidate);
        }
    }
}
