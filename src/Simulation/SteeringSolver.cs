using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class SteeringSolver
{
    private static readonly float[] CandidateAngles =
    [
        0f, -15f, 15f, -30f, 30f, -50f, 50f,
        -75f, 75f, -110f, 110f, 180f
    ];

    private readonly StaticWorld _world;
    private readonly SpatialHash _spatialHash;
    private readonly int[] _neighborBuffer = new int[48];

    public SteeringSolver(StaticWorld world, SpatialHash spatialHash)
    {
        _world = world;
        _spatialHash = spatialHash;
    }

    public void Solve(UnitStore units, float delta)
    {
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (units.Modes[unit] is not UnitMoveMode.Moving)
            {
                units.NextVelocities[unit] = MoveTowards(
                    units.Velocities[unit],
                    Vector2.Zero,
                    units.Accelerations[unit] * delta);
                continue;
            }

            units.NextVelocities[unit] = SolveUnit(units, unit, delta);
        }
    }

    private Vector2 SolveUnit(UnitStore units, int unit, float delta)
    {
        var position = units.Positions[unit];
        var preferred = units.PreferredVelocities[unit];
        var preferredSpeed = preferred.Length();
        if (preferredSpeed < 0.001f)
        {
            return MoveTowards(
                units.Velocities[unit],
                Vector2.Zero,
                units.Accelerations[unit] * delta);
        }

        var radius = units.Radii[unit];
        var neighborRadius = MathF.Max(64f, radius * 7f);
        var slotDistance = Vector2.Distance(position, units.SlotTargets[unit]);
        var horizon = slotDistance < 28f ? 0.16f : slotDistance < 90f ? 0.34f : 0.65f;
        var neighborCount = _spatialHash.Query(
            position,
            neighborRadius,
            unit,
            _neighborBuffer);

        var preferredRisk = CollisionRisk(
            units,
            unit,
            preferred,
            neighborCount,
            horizon);
        if (preferredRisk < 0.02f &&
            _world.IsSegmentFree(position, position + preferred * 0.32f, radius))
        {
            TickAvoidanceMemory(units, unit, 0);
            return MoveTowards(
                units.Velocities[unit],
                preferred,
                units.Accelerations[unit] * delta);
        }

        var preferredDirection = preferred / preferredSpeed;
        var best = Vector2.Zero;
        var bestScore = float.PositiveInfinity;
        var bestSide = (sbyte)0;

        for (var candidateIndex = 0; candidateIndex < CandidateAngles.Length; candidateIndex++)
        {
            var angle = CandidateAngles[candidateIndex] * MathF.PI / 180f;
            var candidateDirection = Rotate(preferredDirection, angle);
            var speedScale = MathF.Abs(CandidateAngles[candidateIndex]) >= 100f ? 0.45f : 1f;
            var candidate = candidateDirection * preferredSpeed * speedScale;

            if (!_world.IsSegmentFree(position, position + candidate * 0.32f, radius))
            {
                continue;
            }

            var collisionRisk = CollisionRisk(
                units,
                unit,
                candidate,
                neighborCount,
                horizon);
            var angularCost = MathF.Abs(CandidateAngles[candidateIndex]) / 180f;
            var speedLoss = 1f - candidate.Length() / preferredSpeed;
            var side = MathF.Abs(angle) < 0.01f ? (sbyte)0 : angle < 0f ? (sbyte)-1 : (sbyte)1;
            var sideSwitchCost = units.AvoidanceLockTicks[unit] > 0 &&
                                 units.AvoidanceSides[unit] != 0 &&
                                 side != 0 &&
                                 side != units.AvoidanceSides[unit]
                ? 2.5f
                : 0f;
            var reverseCost = MathF.Abs(CandidateAngles[candidateIndex]) > 90f ? 1.5f : 0f;
            var score = collisionRisk * 8f + angularCost * 2.2f +
                        speedLoss * 0.8f + sideSwitchCost + reverseCost;

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
                bestSide = side;
            }
        }

        if (!float.IsFinite(bestScore))
        {
            best = Vector2.Zero;
            bestSide = units.AvoidanceSides[unit];
        }

        TickAvoidanceMemory(units, unit, bestSide);
        return MoveTowards(
            units.Velocities[unit],
            best,
            units.Accelerations[unit] * delta);
    }

    private float CollisionRisk(
        UnitStore units,
        int unit,
        Vector2 candidate,
        int neighborCount,
        float horizon)
    {
        var risk = 0f;
        var position = units.Positions[unit];
        var radius = units.Radii[unit];

        for (var i = 0; i < neighborCount; i++)
        {
            var neighbor = _neighborBuffer[i];
            var offset = units.Positions[neighbor] - position;
            var combinedRadius = radius + units.Radii[neighbor] + 1.5f;
            var distanceSquared = offset.LengthSquared();
            if (distanceSquared > 90f * 90f)
            {
                continue;
            }

            if (distanceSquared < combinedRadius * combinedRadius)
            {
                risk += 3f;
                continue;
            }

            var relativeVelocity = units.Velocities[neighbor] - candidate;
            var a = relativeVelocity.LengthSquared();
            if (a < 0.0001f)
            {
                continue;
            }

            var b = 2f * Vector2.Dot(offset, relativeVelocity);
            var c = distanceSquared - combinedRadius * combinedRadius;
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                continue;
            }

            var time = (-b - MathF.Sqrt(discriminant)) / (2f * a);
            if (time >= 0f && time <= horizon)
            {
                risk += 1f + (horizon - time) / horizon;
            }

        }

        return risk;
    }

    private static void TickAvoidanceMemory(UnitStore units, int unit, sbyte selectedSide)
    {
        if (units.AvoidanceLockTicks[unit] > 0)
        {
            units.AvoidanceLockTicks[unit]--;
        }

        if (selectedSide != 0)
        {
            units.AvoidanceSides[unit] = selectedSide;
            units.AvoidanceLockTicks[unit] = 36;
        }
        else if (units.AvoidanceLockTicks[unit] <= 0)
        {
            units.AvoidanceSides[unit] = 0;
        }
    }

    private static Vector2 Rotate(Vector2 value, float radians)
    {
        var sin = MathF.Sin(radians);
        var cos = MathF.Cos(radians);
        return new Vector2(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
    }

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, float maximumDelta)
    {
        var delta = target - current;
        var distance = delta.Length();
        return distance <= maximumDelta || distance < 0.00001f
            ? target
            : current + delta / distance * maximumDelta;
    }
}
