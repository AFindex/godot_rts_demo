using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct SelectionCandidate(
    int UnitId,
    int TypeId,
    int Team,
    bool Alive,
    Vector2 Position,
    float Radius);

public static class SelectionFilter
{
    public static int SelectPoint(
        ReadOnlySpan<SelectionCandidate> candidates,
        Vector2 point,
        int playerTeam,
        float extraHitRadius = 7f)
    {
        var best = -1;
        var bestDistance = float.PositiveInfinity;
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (!candidate.Alive || candidate.Team != playerTeam)
            {
                continue;
            }
            var distance = Vector2.DistanceSquared(point, candidate.Position);
            var hitRadius = candidate.Radius + extraHitRadius;
            if (distance <= hitRadius * hitRadius &&
                (distance < bestDistance ||
                 (distance == bestDistance && candidate.UnitId < best)))
            {
                best = candidate.UnitId;
                bestDistance = distance;
            }
        }
        return best;
    }

    public static int[] SelectBox(
        ReadOnlySpan<SelectionCandidate> candidates,
        SimRect bounds,
        int playerTeam)
    {
        var selected = new List<int>();
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (candidate.Alive && candidate.Team == playerTeam &&
                bounds.Contains(candidate.Position))
            {
                selected.Add(candidate.UnitId);
            }
        }
        selected.Sort();
        return selected.ToArray();
    }

    public static int[] SelectVisibleSameType(
        ReadOnlySpan<SelectionCandidate> candidates,
        int anchorUnit,
        SimRect visibleWorld,
        int playerTeam)
    {
        var typeId = -1;
        for (var index = 0; index < candidates.Length; index++)
        {
            if (candidates[index].UnitId == anchorUnit &&
                candidates[index].Alive &&
                candidates[index].Team == playerTeam)
            {
                typeId = candidates[index].TypeId;
                break;
            }
        }
        if (typeId < 0)
        {
            return [];
        }

        var selected = new List<int>();
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (candidate.Alive && candidate.Team == playerTeam &&
                candidate.TypeId == typeId &&
                visibleWorld.Contains(candidate.Position))
            {
                selected.Add(candidate.UnitId);
            }
        }
        selected.Sort();
        return selected.ToArray();
    }
}

public sealed class OperationCameraController
{
    private const float MinimumZoom = 1f;
    private const float MaximumZoom = 2.4f;
    private const float ZoomStep = 1.15f;
    private const float PanSpeed = 620f;
    private const float EdgeSize = 18f;

    public OperationCameraController(SimRect worldBounds, Vector2 viewportSize)
    {
        WorldBounds = worldBounds;
        ViewportSize = viewportSize;
        Position = (worldBounds.Min + worldBounds.Max) * 0.5f;
        Zoom = 1f;
        ClampPosition();
    }

    public SimRect WorldBounds { get; }
    public Vector2 ViewportSize { get; private set; }
    public Vector2 Position { get; private set; }
    public float Zoom { get; private set; }

    public SimRect VisibleWorld
    {
        get
        {
            var half = ViewportSize * (0.5f / Zoom);
            return new SimRect(Position - half, Position + half);
        }
    }

    public void Resize(Vector2 viewportSize)
    {
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
        {
            return;
        }
        ViewportSize = viewportSize;
        ClampPosition();
    }

    public void Pan(Vector2 direction, float deltaSeconds)
    {
        if (direction.LengthSquared() > 1f)
        {
            direction = Vector2.Normalize(direction);
        }
        Position += direction * (PanSpeed / Zoom) * deltaSeconds;
        ClampPosition();
    }

    public void PanFromEdges(Vector2 pointer, float deltaSeconds)
    {
        var direction = Vector2.Zero;
        if (pointer.X <= EdgeSize) direction.X -= 1f;
        if (pointer.X >= ViewportSize.X - EdgeSize) direction.X += 1f;
        if (pointer.Y <= EdgeSize) direction.Y -= 1f;
        if (pointer.Y >= ViewportSize.Y - EdgeSize) direction.Y += 1f;
        Pan(direction, deltaSeconds);
    }

    public void ZoomAt(Vector2 screenPoint, int steps)
    {
        if (steps == 0)
        {
            return;
        }
        var before = ScreenToWorld(screenPoint);
        Zoom = Math.Clamp(
            Zoom * MathF.Pow(ZoomStep, steps), MinimumZoom, MaximumZoom);
        var after = ScreenToWorld(screenPoint);
        Position += before - after;
        ClampPosition();
    }

    public void Focus(ReadOnlySpan<Vector2> positions)
    {
        if (positions.IsEmpty)
        {
            return;
        }
        var sum = Vector2.Zero;
        for (var index = 0; index < positions.Length; index++)
        {
            sum += positions[index];
        }
        Position = sum / positions.Length;
        ClampPosition();
    }

    public Vector2 ScreenToWorld(Vector2 screenPoint) =>
        Position + (screenPoint - ViewportSize * 0.5f) / Zoom;

    private void ClampPosition()
    {
        var half = ViewportSize * (0.5f / Zoom);
        Position = new Vector2(
            ClampAxis(Position.X, WorldBounds.Min.X, WorldBounds.Max.X, half.X),
            ClampAxis(Position.Y, WorldBounds.Min.Y, WorldBounds.Max.Y, half.Y));
    }

    private static float ClampAxis(float value, float minimum, float maximum, float half)
    {
        if (maximum - minimum <= half * 2f)
        {
            return (minimum + maximum) * 0.5f;
        }
        return Math.Clamp(value, minimum + half, maximum - half);
    }
}

public sealed class ControlGroupRecallTracker
{
    private readonly double[] _lastRecallSeconds =
        Enumerable.Repeat(double.NegativeInfinity, ControlGroupManager.GroupCount).ToArray();

    public bool Register(int group, double nowSeconds, double doubleTapSeconds = 0.35)
    {
        if ((uint)group >= ControlGroupManager.GroupCount)
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }
        var doubleTap = nowSeconds - _lastRecallSeconds[group] <= doubleTapSeconds;
        _lastRecallSeconds[group] = doubleTap ? double.NegativeInfinity : nowSeconds;
        return doubleTap;
    }
}
