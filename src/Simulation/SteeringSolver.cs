using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class SteeringSolver
{
    private static readonly float[] CandidateAngles =
    [
        0f, -15f, 15f, -30f, 30f, -50f, 50f,
        -75f, 75f, -110f, 110f, 180f
    ];
    private static readonly CandidateSample[] CandidateSamples =
        BuildCandidateSamples();

    private readonly StaticWorld _world;
    private readonly SpatialHash _spatialHash;
    private readonly int[] _neighborIds = new int[48];
    private readonly Vector2[] _neighborOffsets = new Vector2[48];
    private readonly Vector2[] _neighborVelocities = new Vector2[48];
    private readonly float[] _neighborDistancesSquared = new float[48];
    private readonly float[] _neighborCombinedRadiiSquared = new float[48];
    private readonly float[] _neighborForwardResponsibilities = new float[48];
    private Vector2[] _preferredProbeStarts = [];
    private Vector2[] _preferredProbeEnds = [];
    private float[] _preferredProbeRadii = [];
    private int[] _preferredProbeWorldRevisions = [];
    private int[] _preferredProbeCommandVersions = [];
    private int[] _preferredProbePathCursors = [];
    private bool[] _preferredProbeValid = [];
    private Vector2[] _freeNeighborhoodCenters = [];
    private float[] _freeNeighborhoodRadii = [];
    private int[] _freeNeighborhoodWorldRevisions = [];
    private bool[] _freeNeighborhoodValid = [];

    public int LastNeighborPairs { get; private set; }
    public int LastCandidateEvaluations { get; private set; }
    public int LastMovingUnits { get; private set; }
    public int LastPreferredFastPaths { get; private set; }
    public int LastAvoidingUnits { get; private set; }
    public int LastWorldSegmentProbes { get; private set; }
    public int LastWorldNeighborhoodProbes { get; private set; }
    public int LastCollisionRiskNeighborChecks { get; private set; }
    public int LastPredictedCollisionHits { get; private set; }
    public int LastOverlappingNeighborHits { get; private set; }

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
        LastMovingUnits = 0;
        LastPreferredFastPaths = 0;
        LastAvoidingUnits = 0;
        LastWorldSegmentProbes = 0;
        LastWorldNeighborhoodProbes = 0;
        LastCollisionRiskNeighborChecks = 0;
        LastPredictedCollisionHits = 0;
        LastOverlappingNeighborHits = 0;
        EnsurePreferredProbeCapacity(units.Capacity);
        foreach (var unit in units.AliveUnits)
        {
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

            LastMovingUnits++;
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
        var freeWorldNeighborhood = ProbeFreeWorldNeighborhood(
            unit,
            position,
            radius + preferredSpeed * preferredProbeSeconds);

        var preferredRisk = CollisionRisk(
            preferred,
            neighborCount,
            horizon);
        LastCandidateEvaluations++;
        var preferredSegmentFree = false;
        if (preferredRisk < 0.02f)
        {
            preferredSegmentFree = freeWorldNeighborhood ||
                ProbePreferredSegment(
                    units,
                    unit,
                    position,
                    position + preferred * preferredProbeSeconds,
                    radius);
        }
        if (preferredRisk < 0.02f && preferredSegmentFree)
        {
            LastPreferredFastPaths++;
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
        LastAvoidingUnits++;

        for (var candidateIndex = 0;
             candidateIndex < CandidateSamples.Length;
             candidateIndex++)
        {
            var sample = CandidateSamples[candidateIndex];
            var rotation = sample.Rotation;
            var candidateDirection = new Vector2(
                preferredDirection.X * rotation.X -
                preferredDirection.Y * rotation.Y,
                preferredDirection.X * rotation.Y +
                preferredDirection.Y * rotation.X);
            var candidateSpeed = preferredSpeed * sample.SpeedScale;
            var candidate = candidateDirection * candidateSpeed;
            var candidateProbeSeconds = MathF.Min(
                0.32f,
                slotDistance / candidateSpeed);
            var sideSwitchCost = units.AvoidanceLockTicks[unit] > 0 &&
                                 units.AvoidanceSides[unit] != 0 &&
                                 sample.Side != 0 &&
                                 sample.Side != units.AvoidanceSides[unit]
                ? 2.5f
                : 0f;
            var staticScore = sample.StaticScore + sideSwitchCost;
            // Collision risk is non-negative. Once static costs alone cannot
            // beat the current result, avoid both the world query and the
            // neighbor time-of-impact sweep without changing the winner.
            if (staticScore >= bestScore)
                continue;
            var segmentFree = candidateIndex == 0 &&
                              preferredRisk < 0.02f
                ? preferredSegmentFree
                : freeWorldNeighborhood ||
                  ProbeSegment(
                      position,
                      position + candidate * candidateProbeSeconds,
                      radius);
            if (!segmentFree) continue;
            var collisionRisk = candidateIndex == 0
                ? preferredRisk
                : CollisionRisk(
                    candidate,
                    neighborCount,
                    horizon,
                    staticScore,
                    bestScore);
            if (candidateIndex != 0) LastCandidateEvaluations++;
            var score = collisionRisk * 8f + staticScore;

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
                bestSide = sample.Side;
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
        float horizon,
        float staticScore = 0f,
        float bestScore = float.PositiveInfinity)
    {
        var risk = 0f;
        var candidateX = candidate.X;
        var candidateY = candidate.Y;
        var candidateLengthSquared =
            candidateX * candidateX + candidateY * candidateY;
        var horizonSquared = horizon * horizon;
        var neighborChecks = 0;
        var predictedHits = 0;
        var overlappingHits = 0;

        for (var i = 0; i < neighborCount; i++)
        {
            neighborChecks++;
            var offset = _neighborOffsets[i];
            var distanceSquared = _neighborDistancesSquared[i];
            if (distanceSquared < _neighborCombinedRadiiSquared[i])
            {
                overlappingHits++;
                var responsibility = AvoidanceResponsibility(
                    candidate,
                    candidateLengthSquared,
                    offset,
                    distanceSquared,
                    i);
                risk += 3f * responsibility;
                if (risk * 8f + staticScore >= bestScore)
                {
                    CommitRiskMetrics(
                        neighborChecks, predictedHits, overlappingHits);
                    return risk;
                }
                continue;
            }

            var neighborVelocity = _neighborVelocities[i];
            var relativeX = neighborVelocity.X - candidateX;
            var relativeY = neighborVelocity.Y - candidateY;
            var a = relativeX * relativeX + relativeY * relativeY;
            if (a < 0.0001f)
            {
                continue;
            }

            var offsetVelocity =
                offset.X * relativeX + offset.Y * relativeY;
            if (offsetVelocity >= 0f)
                continue;
            // Reject the overwhelmingly common near miss without evaluating
            // the quadratic root. If closest approach lies inside the time
            // horizon, compare squared distance using a multiplied form that
            // avoids division; otherwise compare distance at the horizon.
            var closestInsideHorizon = -offsetVelocity <= a * horizon;
            var misses = closestInsideHorizon
                ? distanceSquared * a -
                  offsetVelocity * offsetVelocity >
                  _neighborCombinedRadiiSquared[i] * a
                : distanceSquared +
                  2f * offsetVelocity * horizon +
                  a * horizonSquared >
                  _neighborCombinedRadiiSquared[i];
            if (misses)
                continue;

            var b = 2f * offsetVelocity;
            var c = distanceSquared - _neighborCombinedRadiiSquared[i];
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                continue;
            }

            var time = (-b - MathF.Sqrt(discriminant)) / (2f * a);
            if (time >= 0f && time <= horizon)
            {
                predictedHits++;
                var responsibility = AvoidanceResponsibility(
                    candidate,
                    candidateLengthSquared,
                    offset,
                    distanceSquared,
                    i);
                risk += (1f + (horizon - time) / horizon) * responsibility;
                if (risk * 8f + staticScore >= bestScore)
                {
                    CommitRiskMetrics(
                        neighborChecks, predictedHits, overlappingHits);
                    return risk;
                }
            }

        }

        CommitRiskMetrics(neighborChecks, predictedHits, overlappingHits);
        return risk;
    }

    private void CommitRiskMetrics(
        int neighborChecks,
        int predictedHits,
        int overlappingHits)
    {
        LastCollisionRiskNeighborChecks += neighborChecks;
        LastPredictedCollisionHits += predictedHits;
        LastOverlappingNeighborHits += overlappingHits;
    }

    private float AvoidanceResponsibility(
        Vector2 candidate,
        float candidateLengthSquared,
        Vector2 offset,
        float offsetLengthSquared,
        int neighborIndex)
    {
        return UnitPushPriorityPolicy.IsAvoidanceDirectedToward(
            candidate,
            offset,
            candidateLengthSquared,
            offsetLengthSquared)
            ? _neighborForwardResponsibilities[neighborIndex]
            : 1f;
    }

    private bool ProbeSegment(Vector2 start, Vector2 end, float radius)
    {
        LastWorldSegmentProbes++;
        return _world.IsSegmentFree(start, end, radius);
    }

    private bool ProbePreferredSegment(
        UnitStore units,
        int unit,
        Vector2 start,
        Vector2 end,
        float radius)
    {
        var pathCursor = units.Paths[unit]?.Cursor ?? -1;
        var revision = _world.NavigationRevision;
        if (_preferredProbeValid[unit] &&
            _preferredProbeWorldRevisions[unit] == revision &&
            _preferredProbeCommandVersions[unit] ==
                units.CommandVersions[unit] &&
            _preferredProbePathCursors[unit] == pathCursor &&
            CachedProbeContains(unit, start, end, radius))
        {
            return true;
        }

        var segment = end - start;
        var length = segment.Length();
        if (length > 0.0001f)
        {
            // Probe a conservative superset once. A successful result can be
            // reused while this unit advances inside the same path segment;
            // failure falls back to the exact request, so steering behavior
            // near corners and newly placed buildings is unchanged.
            var extension = MathF.Max(8f, length);
            var extendedEnd = end + segment / length * extension;
            var cachedRadius = radius + 0.5f;
            LastWorldSegmentProbes++;
            if (_world.IsSegmentFree(start, extendedEnd, cachedRadius))
            {
                _preferredProbeStarts[unit] = start;
                _preferredProbeEnds[unit] = extendedEnd;
                _preferredProbeRadii[unit] = cachedRadius;
                _preferredProbeWorldRevisions[unit] = revision;
                _preferredProbeCommandVersions[unit] =
                    units.CommandVersions[unit];
                _preferredProbePathCursors[unit] = pathCursor;
                _preferredProbeValid[unit] = true;
                return true;
            }
        }

        _preferredProbeValid[unit] = false;
        return ProbeSegment(start, end, radius);
    }

    private bool ProbeFreeWorldNeighborhood(
        int unit,
        Vector2 center,
        float requiredRadius)
    {
        var revision = _world.NavigationRevision;
        if (_freeNeighborhoodValid[unit] &&
            _freeNeighborhoodWorldRevisions[unit] == revision)
        {
            var travel = Vector2.Distance(
                center, _freeNeighborhoodCenters[unit]);
            if (travel + requiredRadius <= _freeNeighborhoodRadii[unit])
                return true;
        }

        // A free disc is a conservative superset of every short steering
        // capsule starting at its center. The extra travel allowance lets the
        // proof survive several ticks. If the larger disc is not free we do
        // not cache a negative result; exact candidate segment tests below
        // retain the established wall/corner behavior.
        const float travelAllowance = 12f;
        var probeRadius = requiredRadius + travelAllowance;
        LastWorldNeighborhoodProbes++;
        if (!_world.IsDiscFree(center, probeRadius))
        {
            _freeNeighborhoodValid[unit] = false;
            return false;
        }

        _freeNeighborhoodCenters[unit] = center;
        _freeNeighborhoodRadii[unit] = probeRadius;
        _freeNeighborhoodWorldRevisions[unit] = revision;
        _freeNeighborhoodValid[unit] = true;
        return true;
    }

    private bool CachedProbeContains(
        int unit,
        Vector2 start,
        Vector2 end,
        float radius)
    {
        var cachedStart = _preferredProbeStarts[unit];
        var cachedSegment = _preferredProbeEnds[unit] - cachedStart;
        var lengthSquared = cachedSegment.LengthSquared();
        var margin = _preferredProbeRadii[unit] - radius;
        if (lengthSquared <= 0.0001f || margin < 0f)
            return false;

        static bool ContainsPoint(
            Vector2 point,
            Vector2 segmentStart,
            Vector2 segment,
            float segmentLengthSquared,
            float allowedOffset)
        {
            var projection = Vector2.Dot(
                point - segmentStart, segment) / segmentLengthSquared;
            if (projection < 0f || projection > 1f)
                return false;
            var nearest = segmentStart + segment * projection;
            return Vector2.DistanceSquared(point, nearest) <=
                   allowedOffset * allowedOffset;
        }

        return ContainsPoint(
                   start, cachedStart, cachedSegment, lengthSquared, margin) &&
               ContainsPoint(
                   end, cachedStart, cachedSegment, lengthSquared, margin);
    }

    private void EnsurePreferredProbeCapacity(int capacity)
    {
        if (_preferredProbeValid.Length >= capacity) return;
        Array.Resize(ref _preferredProbeStarts, capacity);
        Array.Resize(ref _preferredProbeEnds, capacity);
        Array.Resize(ref _preferredProbeRadii, capacity);
        Array.Resize(ref _preferredProbeWorldRevisions, capacity);
        Array.Resize(ref _preferredProbeCommandVersions, capacity);
        Array.Resize(ref _preferredProbePathCursors, capacity);
        Array.Resize(ref _preferredProbeValid, capacity);
        Array.Resize(ref _freeNeighborhoodCenters, capacity);
        Array.Resize(ref _freeNeighborhoodRadii, capacity);
        Array.Resize(ref _freeNeighborhoodWorldRevisions, capacity);
        Array.Resize(ref _freeNeighborhoodValid, capacity);
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

    private static CandidateSample[] BuildCandidateSamples()
    {
        var result = new CandidateSample[CandidateAngles.Length];
        for (var index = 0; index < result.Length; index++)
        {
            var angle = CandidateAngles[index];
            var absoluteAngle = MathF.Abs(angle);
            var radians = angle * MathF.PI / 180f;
            var speedScale = absoluteAngle >= 100f ? 0.45f : 1f;
            var angularCost = absoluteAngle / 180f;
            var speedLoss = 1f - speedScale;
            var reverseCost = absoluteAngle > 90f ? 1.5f : 0f;
            var side = absoluteAngle < 0.01f
                ? (sbyte)0
                : angle < 0f ? (sbyte)-1 : (sbyte)1;
            result[index] = new CandidateSample(
                new Vector2(MathF.Cos(radians), MathF.Sin(radians)),
                speedScale,
                angularCost * 2.2f + speedLoss * 0.8f + reverseCost,
                side);
        }
        return result;
    }

    private readonly record struct CandidateSample(
        Vector2 Rotation,
        float SpeedScale,
        float StaticScore,
        sbyte Side);

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, float maximumDelta)
    {
        var delta = target - current;
        var distance = delta.Length();
        return distance <= maximumDelta || distance < 0.00001f
            ? target
            : current + delta / distance * maximumDelta;
    }
}
