namespace RtsDemo.Simulation;

public sealed class SimulationMetrics
{
    public long Tick { get; internal set; }
    public int MovingUnits { get; internal set; }
    public int ArrivedUnits { get; internal set; }
    public int WaitingForPathUnits { get; internal set; }
    public int PendingPathRequests { get; internal set; }
    public int PathsCompleted { get; internal set; }
    public int PathsFailed { get; internal set; }
    public int CollisionPairs { get; internal set; }
    public float MaximumPenetration { get; internal set; }
    public int RepathRequests { get; internal set; }
    public int NavigationRevision { get; internal set; }
    public int NavigationInvalidations { get; internal set; }
    public int RecoveryEvents { get; internal set; }
    public int UnreachableUnits { get; internal set; }
    public long GroupRoutePlans { get; internal set; }
    public long SharedRouteAssignments { get; internal set; }
    public long DestinationSlotSwaps { get; internal set; }
    public long DestinationOverflowAssignments { get; internal set; }
    public int MaximumDestinationStallTicks { get; internal set; }
    public int MaximumDestinationNearTicks { get; internal set; }
    public long DestinationYieldEvents { get; internal set; }
    public int ActiveDestinationYields { get; internal set; }
    public double TotalMilliseconds { get; internal set; }
    public double PathMilliseconds { get; internal set; }
    public double PreferredVelocityMilliseconds { get; internal set; }
    public double ChokeMilliseconds { get; internal set; }
    public double SpatialHashMilliseconds { get; internal set; }
    public double SteeringMilliseconds { get; internal set; }
    public double IntegrateMilliseconds { get; internal set; }
    public double CollisionMilliseconds { get; internal set; }
    public double RecoveryMilliseconds { get; internal set; }
    public long AllocatedBytes { get; internal set; }
}
