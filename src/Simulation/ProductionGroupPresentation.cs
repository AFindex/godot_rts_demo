namespace RtsDemo.Simulation;

public readonly record struct ProductionGroupOrderEntry(
    int OrderId,
    int RecipeId,
    float Progress);

public sealed record ProductionGroupProducerSnapshot(
    int BuildingId,
    ProductionGroupOrderEntry[] Orders);

public sealed record ProductionGroupSnapshot(
    ProductionGroupProducerSnapshot[] Producers)
{
    public int ProducerCount => Producers.Length;
    public int TotalOrders => Producers.Sum(value => value.Orders.Length);
    public int ActiveProducerCount => Producers.Count(value => value.Orders.Length > 0);

    public static ProductionGroupSnapshot Create(
        IEnumerable<ProductionGroupProducerSnapshot> producers)
    {
        var canonical = producers
            .Where(value => value.BuildingId >= 0)
            .GroupBy(value => value.BuildingId)
            .OrderBy(group => group.Key)
            .Select(group => new ProductionGroupProducerSnapshot(
                group.Key,
                group.SelectMany(value => value.Orders)
                    .Where(order => order.OrderId > 0 && order.RecipeId >= 0 &&
                                    float.IsFinite(order.Progress))
                    .DistinctBy(order => order.OrderId)
                    .OrderBy(order => order.OrderId)
                    .ToArray()))
            .ToArray();
        return new ProductionGroupSnapshot(canonical);
    }

    public int[] NewestMatchingOrders(int recipeId) =>
        Producers
            .Select(producer => producer.Orders
                .Where(order => order.RecipeId == recipeId)
                .OrderByDescending(order => order.OrderId)
                .FirstOrDefault())
            .Where(order => order.OrderId > 0 && order.RecipeId == recipeId)
            .Select(order => order.OrderId)
            .Order()
            .ToArray();
}

public readonly record struct ProductionBatchAvailability(
    int BuildingId,
    bool Available,
    string Status);

public sealed record ProductionBatchPlan(
    int[] ProducerIds,
    int ProducerCount,
    int TotalOrders,
    string Status)
{
    public bool CanIssue => ProducerIds.Length > 0;
}

public static class ProductionBatchPlanner
{
    public static ProductionBatchPlan PlanTrain(
        ProductionGroupSnapshot group,
        IEnumerable<ProductionBatchAvailability> availability)
    {
        var byProducer = availability
            .Where(value => value.BuildingId >= 0)
            .GroupBy(value => value.BuildingId)
            .ToDictionary(value => value.Key, value => value.First());
        var ready = group.Producers
            .Where(value => byProducer.TryGetValue(value.BuildingId, out var item) &&
                            item.Available)
            .Select(value => value.BuildingId)
            .ToArray();
        var failure = group.Producers
            .Select(value => byProducer.TryGetValue(value.BuildingId, out var item)
                ? item.Status
                : "Unavailable")
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "Success")
            .GroupBy(value => value)
            .OrderByDescending(value => value.Count())
            .ThenBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => value.Key)
            .FirstOrDefault();
        var status = $"ready {ready.Length}/{group.ProducerCount} · " +
                     $"queued {group.TotalOrders}";
        if (ready.Length == 0 && failure is not null) status += $" · {failure}";
        return new ProductionBatchPlan(
            ready, group.ProducerCount, group.TotalOrders, status);
    }
}
