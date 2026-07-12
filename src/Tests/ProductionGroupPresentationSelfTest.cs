using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ProductionGroupPresentationSelfTest
{
    public static SelfTestResult Run()
    {
        var group = ProductionGroupSnapshot.Create(
        [
            new(2,
            [
                new(6, 1, 0.2f),
                new(4, 0, 0.8f)
            ]),
            new(1,
            [
                new(3, 0, 0.1f),
                new(1, 0, 0.7f),
                new(1, 0, 0.7f)
            ]),
            new(1, [new(9, 1, 0f)])
        ]);
        var plan = ProductionBatchPlanner.PlanTrain(group,
        [
            new(2, false, "QueueFull"),
            new(1, true, "Success")
        ]);
        var cancel = group.NewestMatchingOrders(0);
        var passed = group.Producers.Select(value => value.BuildingId)
                         .SequenceEqual([1, 2]) &&
                     group.TotalOrders == 5 &&
                     group.ActiveProducerCount == 2 &&
                     plan.ProducerIds.SequenceEqual([1]) &&
                     plan.Status == "ready 1/2 · queued 5" &&
                     cancel.SequenceEqual([3, 4]);
        return new SelfTestResult(
            passed,
            $"producers={group.ProducerCount}, queued={group.TotalOrders}, " +
            $"ready={plan.ProducerIds.Length}, cancel={string.Join(',', cancel)}");
    }
}
