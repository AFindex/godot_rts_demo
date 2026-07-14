namespace RtsDemo.Simulation;

// Mining movement is an economy intent, but collision filtering is consumed by
// movement. Keeping the policy here avoids either system owning the other.
public static class WorkerCollisionPolicy
{
    public static bool SuppressesUnitCollision(WorkerEconomyState state) =>
        state is WorkerEconomyState.GoingToResource or
            WorkerEconomyState.WaitingForResource or
            WorkerEconomyState.Gathering or
            WorkerEconomyState.ReturningCargo;
}
