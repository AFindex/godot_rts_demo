namespace RtsDemo.Simulation;

public enum CombatContactRole : byte
{
    Standard,
    MobileWeapon,
    FixedCooldown,
    MeleeContact,
    FixedWindup
}

public readonly record struct CombatContactSnapshot(
    CombatContactRole Role,
    float InverseMobility,
    int ResistanceRank);

public readonly record struct CombatContactResolution(
    CombatContactSnapshot Left,
    CombatContactSnapshot Right,
    float LeftCorrectionShare,
    float RightCorrectionShare);

/// <summary>
/// Derives collision response from public movement and combat state. The result is
/// transient: profiles, saves and the generic collision solver do not own combat
/// contact rules.
/// </summary>
public static class CombatContactPolicy
{
    private const float MobileWeaponInverseMobility = 0.72f;
    private const float FixedCooldownInverseMobility = 0.30f;
    private const float MeleeContactInverseMobility = 0.14f;
    private const float FixedWindupInverseMobility = 0.08f;

    public static CombatContactSnapshot Evaluate(
        UnitMoveMode moveMode,
        CombatPhase phase,
        UnitCommandIntent commandIntent,
        CombatPositioningKind positioning,
        float windupRemaining,
        float cooldownRemaining,
        bool canMoveDuringWindup,
        bool canMoveDuringCooldown)
    {
        if (phase != CombatPhase.Attacking)
        {
            return new CombatContactSnapshot(
                CombatContactRole.Standard,
                StandardInverseMobility(moveMode),
                0);
        }

        if (positioning == CombatPositioningKind.Melee)
        {
            return new CombatContactSnapshot(
                CombatContactRole.MeleeContact,
                MeleeContactInverseMobility,
                3);
        }

        if (windupRemaining > 0f)
        {
            return commandIntent == UnitCommandIntent.AttackMove &&
                   canMoveDuringWindup
                ? MobileWeapon()
                : new CombatContactSnapshot(
                    CombatContactRole.FixedWindup,
                    FixedWindupInverseMobility,
                    4);
        }

        if (cooldownRemaining > 0f)
        {
            return commandIntent == UnitCommandIntent.AttackMove &&
                   canMoveDuringCooldown
                ? MobileWeapon()
                : new CombatContactSnapshot(
                    CombatContactRole.FixedCooldown,
                    FixedCooldownInverseMobility,
                    2);
        }

        return new CombatContactSnapshot(
            CombatContactRole.Standard,
            StandardInverseMobility(moveMode),
            0);
    }

    public static CombatContactResolution Resolve(
        CombatContactSnapshot left,
        CombatContactSnapshot right)
    {
        var total = left.InverseMobility + right.InverseMobility;
        return total > 0f
            ? new CombatContactResolution(
                left,
                right,
                left.InverseMobility / total,
                right.InverseMobility / total)
            : new CombatContactResolution(left, right, 0f, 0f);
    }

    private static CombatContactSnapshot MobileWeapon() => new(
        CombatContactRole.MobileWeapon,
        MobileWeaponInverseMobility,
        1);

    private static float StandardInverseMobility(UnitMoveMode mode) =>
        mode switch
        {
            UnitMoveMode.Hold => 0f,
            UnitMoveMode.Arrived => 0.28f,
            UnitMoveMode.Idle => 0.65f,
            _ => 1f
        };
}
