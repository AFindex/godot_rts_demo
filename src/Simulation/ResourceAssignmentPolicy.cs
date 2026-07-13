namespace RtsDemo.Simulation;

public readonly record struct ResourceAssignmentCandidate(
    int NodeId,
    int Assigned,
    int IdealAssignments,
    bool Preferred,
    float WorkerToNodeDistanceSquared,
    float NodeToDropOffDistanceSquared);

/// <summary>
/// Stateless deterministic resource assignment. Load is the primary key so a
/// batch fills every patch before adding the next worker layer. Travel cost is
/// only a tie-breaker inside the same saturation layer.
/// </summary>
public static class ResourceAssignmentPolicy
{
    public static int Select(ReadOnlySpan<ResourceAssignmentCandidate> candidates)
    {
        if (candidates.IsEmpty)
            return -1;
        var best = candidates[0];
        for (var index = 1; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (Compare(candidate, best) < 0)
                best = candidate;
        }
        return best.NodeId;
    }

    private static int Compare(
        ResourceAssignmentCandidate left,
        ResourceAssignmentCandidate right)
    {
        var leftLoad = (long)left.Assigned * right.IdealAssignments;
        var rightLoad = (long)right.Assigned * left.IdealAssignments;
        var comparison = leftLoad.CompareTo(rightLoad);
        if (comparison != 0)
            return comparison;
        comparison = right.Preferred.CompareTo(left.Preferred);
        if (comparison != 0)
            return comparison;
        var leftTravel = left.WorkerToNodeDistanceSquared +
                         left.NodeToDropOffDistanceSquared;
        var rightTravel = right.WorkerToNodeDistanceSquared +
                          right.NodeToDropOffDistanceSquared;
        comparison = leftTravel.CompareTo(rightTravel);
        return comparison != 0
            ? comparison
            : left.NodeId.CompareTo(right.NodeId);
    }
}
