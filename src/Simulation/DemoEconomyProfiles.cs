namespace RtsDemo.Simulation;

public readonly record struct EconomyResourceProfile(
    int Amount,
    int HarvestBatch,
    float HarvestSeconds,
    int HarvesterCapacity,
    bool RequiresRefinery);

/// <summary>
/// Shared demo resource rules. Maps and black-box tests read the same profiles
/// instead of copying harvest amounts and timings into scenario code.
/// </summary>
public static class DemoEconomyProfiles
{
    public static EconomyResourceProfile MineralField { get; } = new(
        Amount: 10_000,
        HarvestBatch: 5,
        HarvestSeconds: 0.6f,
        HarvesterCapacity: 2,
        RequiresRefinery: false);

    public static EconomyResourceProfile VespeneGeyser { get; } = new(
        Amount: 10_000,
        HarvestBatch: 4,
        HarvestSeconds: 0.7f,
        HarvesterCapacity: 3,
        RequiresRefinery: true);
}
