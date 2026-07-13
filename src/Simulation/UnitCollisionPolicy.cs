namespace RtsDemo.Simulation;

/// <summary>
/// Central pairwise contact matrix. Economy/construction suppression ignores
/// all unit contacts for that mover; Burrow only suppresses mixed-depth pairs.
/// Terrain and building collision are intentionally outside this policy.
/// </summary>
public static class UnitCollisionPolicy
{
    public static bool SuppressesPair(
        bool leftSuppressed,
        UnitConcealmentKind leftConcealment,
        bool rightSuppressed,
        UnitConcealmentKind rightConcealment)
    {
        if (leftSuppressed || rightSuppressed)
            return true;
        var leftBurrowed = leftConcealment == UnitConcealmentKind.Burrowed;
        var rightBurrowed = rightConcealment == UnitConcealmentKind.Burrowed;
        return leftBurrowed != rightBurrowed;
    }
}
