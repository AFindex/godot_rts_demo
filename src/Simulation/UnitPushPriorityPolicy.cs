using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct UnitPushResolution(
    float LeftCorrectionShare,
    float RightCorrectionShare,
    bool LeftPushing,
    bool RightPushing);

/// <summary>
/// Resolves directional right-of-way separately from combat contact resistance.
/// Movement class supplies the stable base priority. A same-priority moving unit
/// may displace an ordinary stationary unit; Hold remains anchored unless the
/// mover has strictly higher priority.
/// </summary>
public static class UnitPushPriorityPolicy
{
    private const float DirectionThreshold = 0.08f;

    public static int Priority(UnitStore units, int unit) =>
        (int)units.MovementClasses[unit];

    public static bool CanDisplace(
        UnitStore units,
        int movingUnit,
        int blockingUnit)
    {
        var movingPriority = Priority(units, movingUnit);
        var blockingPriority = Priority(units, blockingUnit);
        return movingPriority > blockingPriority ||
               movingPriority == blockingPriority &&
               units.Modes[blockingUnit] != UnitMoveMode.Hold;
    }

    public static float AvoidanceResponsibility(
        UnitStore units,
        int unit,
        int neighbor,
        Vector2 candidateVelocity,
        CombatContactSnapshot neighborContact,
        bool unitEngaged,
        bool neighborEngaged)
    {
        var offset = units.Positions[neighbor] - units.Positions[unit];
        if (!IsAvoidanceDirectedToward(candidateVelocity, offset))
            return 1f;

        return ForwardAvoidanceResponsibility(
            units, unit, neighbor, offset, neighborContact,
            unitEngaged, neighborEngaged);
    }

    public static bool IsAvoidanceDirectedToward(
        Vector2 candidateVelocity,
        Vector2 offset)
    {
        var candidateLengthSquared = candidateVelocity.LengthSquared();
        var offsetLengthSquared = offset.LengthSquared();
        return IsAvoidanceDirectedToward(
            candidateVelocity,
            offset,
            candidateLengthSquared,
            offsetLengthSquared);
    }

    public static bool IsAvoidanceDirectedToward(
        Vector2 candidateVelocity,
        Vector2 offset,
        float candidateLengthSquared,
        float offsetLengthSquared)
    {
        if (candidateLengthSquared <= 0.0001f ||
            offsetLengthSquared <= 0.0001f)
            return false;
        var forward = Vector2.Dot(candidateVelocity, offset);
        return forward > 0f &&
               forward * forward > candidateLengthSquared *
               offsetLengthSquared * DirectionThreshold * DirectionThreshold;
    }

    /// <summary>
    /// Returns the stable pair responsibility once the caller has established
    /// that its candidate velocity points toward the neighbor. Steering probes
    /// many candidate velocities against the same neighbor set, so separating
    /// the directional test lets it cache the rest of the pair policy once.
    /// </summary>
    public static float ForwardAvoidanceResponsibility(
        UnitStore units,
        int unit,
        int neighbor,
        Vector2 offset,
        CombatContactSnapshot neighborContact,
        bool unitEngaged,
        bool neighborEngaged)
    {

        if (!CanDisplace(units, unit, neighbor))
            return 1.45f;

        var priorityDelta = Priority(units, unit) - Priority(units, neighbor);
        if (priorityDelta == 0 &&
            (unitEngaged || neighborEngaged ||
             neighborContact.ResistanceRank > 0))
            return 1f;
        var neighborMoving = units.Modes[neighbor] == UnitMoveMode.Moving &&
                             units.PreferredVelocities[neighbor].LengthSquared() >
                             1f;
        if (neighborMoving)
        {
            var towardUnit = Vector2.Dot(
                units.PreferredVelocities[neighbor], -offset) > 0f;
            if (towardUnit && priorityDelta == 0)
                return 1f;
        }

        return priorityDelta > 0 ? 0.04f : 0.10f;
    }

    public static UnitPushResolution Resolve(
        UnitStore units,
        int left,
        int right,
        CombatContactSnapshot leftContact,
        CombatContactSnapshot rightContact,
        Vector2 leftToRightNormal,
        bool leftEngaged,
        bool rightEngaged)
    {
        var leftAdvancing = IsAdvancing(
            units, left, leftToRightNormal);
        var rightAdvancing = IsAdvancing(
            units, right, -leftToRightNormal);

        if (leftAdvancing && !rightAdvancing)
        {
            if (!CanDisplace(units, left, right))
                return new UnitPushResolution(1f, 0f, false, false);
            if (Priority(units, left) == Priority(units, right) &&
                (leftEngaged || rightEngaged ||
                 rightContact.ResistanceRank > 0))
                return Reciprocal(leftContact, rightContact);
            return AuthorizedPush(
                units, left, right, leftContact, rightContact,
                leftIsPusher: true);
        }

        if (rightAdvancing && !leftAdvancing)
        {
            if (!CanDisplace(units, right, left))
                return new UnitPushResolution(0f, 1f, false, false);
            if (Priority(units, right) == Priority(units, left) &&
                (leftEngaged || rightEngaged ||
                 leftContact.ResistanceRank > 0))
                return Reciprocal(leftContact, rightContact);
            return AuthorizedPush(
                units, right, left, rightContact, leftContact,
                leftIsPusher: false);
        }

        if (leftAdvancing && rightAdvancing)
        {
            var leftPriority = Priority(units, left);
            var rightPriority = Priority(units, right);
            if (leftPriority > rightPriority)
                return AuthorizedPush(
                    units, left, right, leftContact, rightContact,
                    leftIsPusher: true);
            if (rightPriority > leftPriority)
                return AuthorizedPush(
                    units, right, left, rightContact, leftContact,
                    leftIsPusher: false);
        }

        return Reciprocal(leftContact, rightContact);
    }

    private static UnitPushResolution AuthorizedPush(
        UnitStore units,
        int pusher,
        int blocker,
        CombatContactSnapshot pusherContact,
        CombatContactSnapshot blockerContact,
        bool leftIsPusher)
    {
        var strictlyHigher = Priority(units, pusher) > Priority(units, blocker);
        var pusherWeight = MathF.Max(
            0.02f, pusherContact.InverseMobility *
                   (strictlyHigher ? 0.08f : 0.18f));
        var blockerWeight = MathF.Max(
            blockerContact.InverseMobility,
            strictlyHigher ? 0.35f : 0.25f);
        var total = pusherWeight + blockerWeight;
        var pusherShare = pusherWeight / total;
        var blockerShare = blockerWeight / total;
        return leftIsPusher
            ? new UnitPushResolution(
                pusherShare, blockerShare, true, false)
            : new UnitPushResolution(
                blockerShare, pusherShare, false, true);
    }

    private static UnitPushResolution Reciprocal(
        CombatContactSnapshot left,
        CombatContactSnapshot right)
    {
        var total = left.InverseMobility + right.InverseMobility;
        return total > 0f
            ? new UnitPushResolution(
                left.InverseMobility / total,
                right.InverseMobility / total,
                false,
                false)
            : new UnitPushResolution(0f, 0f, false, false);
    }

    private static bool IsAdvancing(
        UnitStore units,
        int unit,
        Vector2 toward)
    {
        if (units.Modes[unit] != UnitMoveMode.Moving)
            return false;
        var preferred = units.PreferredVelocities[unit];
        var threshold = MathF.Max(1f, units.MaxSpeeds[unit] * DirectionThreshold);
        return Vector2.Dot(preferred, toward) >= threshold;
    }
}
