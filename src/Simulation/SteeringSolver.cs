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
    private readonly int[] _neighborIds = new int[48];
    private readonly Vector2[] _neighborOffsets = new Vector2[48];
    private readonly Vector2[] _neighborVelocities = new Vector2[48];
    private readonly float[] _neighborDistancesSquared = new float[48];
    private readonly float[] _neighborCombinedRadiiSquared = new float[48];
    private readonly float[] _neighborForwardResponsibilities = new float[48];

    public int LastNeighborPairs { get; private set; }
    public int LastCandidateEvaluations { get; private set; }

    public SteeringSolver(StaticWorld world, SpatialHash spatialHash)
    {
        _world = world;
        _spatialHash = spatialHash;
    }

    public void Solve(
        UnitStore units,
        float delta,
        ReadOnlySpan<bool> unitCollisionSuppressed,
        ReadOnlySpan<UnitConcealmentKind> concealmentKinds,
        ReadOnlySpan<CombatContactSnapshot> combatContacts,
        ReadOnlySpan<CombatTargetKind> combatTargets)
    {
        LastNeighborPairs = 0;
        LastCandidateEvaluations = 0;
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (!units.Alive[unit])
            {
                units.NextVelocities[unit] = Vector2.Zero;
                continue;
            }
            if (units.Modes[unit] is not UnitMoveMode.Moving)
            {
                units.NextVelocities[unit] =
                    units.Velocities[unit].LengthSquared() <= 0.0000001f
                        ? Vector2.Zero
                        : MoveTowards(
                            units.Velocities[unit],
                            Vector2.Zero,
                            units.Accelerations[unit] * delta);
                continue;
            }

            units.NextVelocities[unit] = SolveUnit(
                units, unit, delta, unitCollisionSuppressed, concealmentKinds,
                combatContacts, combatTargets);
        }
    }

    private Vector2 SolveUnit(
        UnitStore units,
        int unit,
        float delta,
        ReadOnlySpan<bool> unitCollisionSuppressed,
        ReadOnlySpan<UnitConcealmentKind> concealmentKinds,
        ReadOnlySpan<CombatContactSnapshot> combatContacts,
        ReadOnlySpan<CombatTargetKind> combatTargets)
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
        var neighborCount = PrepareNeighbors(
            units, unit, position, neighborRadius,
            unitCollisionSuppressed, concealmentKinds,
            combatContacts, combatTargets);
        LastNeighborPairs += neighborCount;
        var preferredProbeSeconds = MathF.Min(
            0.32f,
            slotDistance / preferredSpeed);

        var preferredRisk = CollisionRisk(
            preferred,
            neighborCount,
            horizon);
        LastCandidateEvaluations++;
        if (preferredRisk < 0.02f &&
            _world.IsSegmentFree(
                position,
                position + preferred * preferredProbeSeconds,
                radius))
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

        for (var candidateIndex = 0;
             candidateIndex < CandidateAngles.Length;
             candidateIndex++)
        {
            var angle = CandidateAngles[candidateIndex] * MathF.PI / 180f;
            var candidateDirection = Rotate(preferredDirection, angle);
            var speedScale = MathF.Abs(CandidateAngles[candidateIndex]) >= 100f
                ? 0.45f
                : 1f;
            var candidate = candidateDirection * preferredSpeed * speedScale;
            var candidateProbeSeconds = MathF.Min(
                0.32f,
                slotDistance / candidate.Length());
            if (!_world.IsSegmentFree(
                    position,
                    position + candidate * candidateProbeSeconds,
                    radius))
                continue;
            var collisionRisk = CollisionRisk(
                candidate,
                neighborCount,
                horizon);
            LastCandidateEvaluations++;

            var angularCost = MathF.Abs(CandidateAngles[candidateIndex]) / 180f;
            var speedLoss = 1f - candidate.Length() / preferredSpeed;
            var side = MathF.Abs(angle) < 0.01f
                ? (sbyte)0
                : angle < 0f ? (sbyte)-1 : (sbyte)1;
            var sideSwitchCost = units.AvoidanceLockTicks[unit] > 0 &&
                                 units.AvoidanceSides[unit] != 0 &&
                                 side != 0 &&
                                 side != units.AvoidanceSides[unit]
                ? 2.5f
                : 0f;
            var reverseCost = MathF.Abs(CandidateAngles[candidateIndex]) > 90f
                ? 1.5f
                : 0f;
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

    private int PrepareNeighbors(
        UnitStore units,
        int unit,
        Vector2 position,
        float neighborRadius,
        ReadOnlySpan<bool> unitCollisionSuppressed,
        ReadOnlySpan<UnitConcealmentKind> concealmentKinds,
        ReadOnlySpan<CombatContactSnapshot> combatContacts,
        ReadOnlySpan<CombatTargetKind> combatTargets)
    {
        var queried = _spatialHash.Query(
            position,
            neighborRadius,
            unit,
            _neighborIds);
        var prepared = 0;
        var unitEngaged = combatTargets[unit] != CombatTargetKind.None;
        for (var index = 0; index < queried; index++)
        {
            var neighbor = _neighborIds[index];
            if (UnitCollisionPolicy.SuppressesPair(
                    unitCollisionSuppressed[unit], concealmentKinds[unit],
                    unitCollisionSuppressed[neighbor],
                    concealmentKinds[neighbor]))
                continue;

            var offset = units.Positions[neighbor] - position;
            var distanceSquared = offset.LengthSquared();
            if (distanceSquared > 90f * 90f)
                continue;

            var combinedRadius = units.Radii[unit] +
                                 units.Radii[neighbor] + 1.5f;
            _neighborIds[prepared] = neighbor;
            _neighborOffsets[prepared] = offset;
            _neighborVelocities[prepared] = units.Velocities[neighbor];
            _neighborDistancesSquared[prepared] = distanceSquared;
            _neighborCombinedRadiiSquared[prepared] =
                combinedRadius * combinedRadius;
            _neighborForwardResponsibilities[prepared] =
                UnitPushPriorityPolicy.ForwardAvoidanceResponsibility(
                    units, unit, neighbor, offset,
                    combatContacts[neighbor], unitEngaged,
                    combatTargets[neighbor] != CombatTargetKind.None);
            prepared++;
        }
        return prepared;
    }

    private float CollisionRisk(
        Vector2 candidate,
        int neighborCount,
        float horizon)
    {
        var risk = 0f;

        for (var i = 0; i < neighborCount; i++)
        {
            var offset = _neighborOffsets[i];
            var distanceSquared = _neighborDistancesSquared[i];
            var responsibility =
                UnitPushPriorityPolicy.IsAvoidanceDirectedToward(
                    candidate, offset)
                    ? _neighborForwardResponsibilities[i]
                    : 1f;

            if (distanceSquared < _neighborCombinedRadiiSquared[i])
            {
                risk += 3f * responsibility;
                continue;
            }

            var relativeVelocity = _neighborVelocities[i] - candidate;
            var a = relativeVelocity.LengthSquared();
            if (a < 0.0001f)
            {
                continue;
            }

            var b = 2f * Vector2.Dot(offset, relativeVelocity);
            var c = distanceSquared - _neighborCombinedRadiiSquared[i];
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                continue;
            }

            var time = (-b - MathF.Sqrt(discriminant)) / (2f * a);
            if (time >= 0f && time <= horizon)
            {
                risk += (1f + (horizon - time) / horizon) * responsibility;
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
