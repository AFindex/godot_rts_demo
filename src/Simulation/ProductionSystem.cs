using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct ProductionOrderId(int Value);

public enum ProductionCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidProducer,
    WrongOwner,
    ProducerNotCompleted,
    WrongProducerType,
    QueueFull,
    InvalidRecipe,
    InsufficientMinerals,
    InsufficientVespeneGas,
    SupplyBlocked,
    InvalidOrder
}

public readonly record struct ProductionCommandResult(
    ProductionCommandCode Code,
    ProductionOrderId OrderId)
{
    public bool Succeeded => Code == ProductionCommandCode.Success;
}

public enum ProductionOrderState : byte
{
    Queued,
    Producing,
    WaitingForExit
}

public readonly record struct ProductionOrderSnapshot(
    ProductionOrderId Id,
    GameplayBuildingId Producer,
    int PlayerId,
    ProductionRecipeProfile Recipe,
    ProductionOrderState State,
    float Progress);

public readonly record struct ProductionQueueSnapshot(
    GameplayBuildingId Producer,
    Vector2? RallyPoint,
    ProductionOrderSnapshot[] Orders);

public sealed class ProductionSystem
{
    public const int MaximumQueueLength = 5;
    private readonly Dictionary<int, ProducerQueue> _queues = new();
    private readonly List<ProducedUnitPopulation> _producedUnits = [];
    private int _nextOrderId = 1;

    public int ActiveOrderCount => _queues.Values.Sum(value => value.Orders.Count);
    public bool HasRuntimeState => _queues.Count > 0 || _producedUnits.Count > 0;

    public ProductionCommandResult Enqueue(
        int playerId,
        GameplayBuildingId producer,
        ProductionRecipeProfile recipe,
        ConstructionSystem construction,
        PlayerEconomyStore economy)
    {
        var validation = ValidateEnqueue(
            playerId, producer, recipe, construction, economy);
        if (!validation.Succeeded) return validation;

        var spend = economy.TrySpend(playerId, recipe.Cost);
        if (!spend.Succeeded)
            throw new InvalidOperationException(
                "Production cost changed between validation and commit.");
        if (!_queues.TryGetValue(producer.Value, out var queue))
        {
            queue = new ProducerQueue(producer);
            _queues.Add(producer.Value, queue);
        }
        var id = new ProductionOrderId(_nextOrderId++);
        queue.Orders.Add(new ProductionOrder(id, playerId, recipe));
        return new ProductionCommandResult(ProductionCommandCode.Success, id);
    }

    public ProductionCommandResult ValidateEnqueue(
        int playerId,
        GameplayBuildingId producer,
        ProductionRecipeProfile recipe,
        ConstructionSystem construction,
        PlayerEconomyStore economy)
    {
        if (!economy.IsRegistered(playerId)) return Failure(ProductionCommandCode.InvalidPlayer);
        if (!ValidRecipe(recipe)) return Failure(ProductionCommandCode.InvalidRecipe);
        if (!construction.IsAlive(producer)) return Failure(ProductionCommandCode.InvalidProducer);
        var building = construction.Observe(producer);
        if (building.PlayerId != playerId) return Failure(ProductionCommandCode.WrongOwner);
        if (building.State != BuildingLifecycleState.Completed)
            return Failure(ProductionCommandCode.ProducerNotCompleted);
        if (building.Type.Id != recipe.ProducerBuildingTypeId)
            return Failure(ProductionCommandCode.WrongProducerType);
        if (_queues.TryGetValue(producer.Value, out var queue) &&
            queue.Orders.Count >= MaximumQueueLength)
            return Failure(ProductionCommandCode.QueueFull);
        var spend = economy.ValidateSpend(playerId, recipe.Cost);
        return spend.Code switch
        {
            EconomyTransactionCode.Success =>
                new ProductionCommandResult(ProductionCommandCode.Success, default),
            EconomyTransactionCode.InsufficientMinerals =>
                Failure(ProductionCommandCode.InsufficientMinerals),
            EconomyTransactionCode.InsufficientVespeneGas =>
                Failure(ProductionCommandCode.InsufficientVespeneGas),
            EconomyTransactionCode.SupplyBlocked =>
                Failure(ProductionCommandCode.SupplyBlocked),
            _ => Failure(ProductionCommandCode.InvalidPlayer)
        };
    }

    public bool Cancel(
        int playerId,
        ProductionOrderId orderId,
        PlayerEconomyStore economy)
    {
        foreach (var queue in _queues.Values)
        {
            var index = queue.Orders.FindIndex(value => value.Id == orderId);
            if (index < 0 || queue.Orders[index].PlayerId != playerId) continue;
            var order = queue.Orders[index];
            economy.Refund(playerId, order.Recipe.Cost,
                order.Recipe.CancelRefundFraction);
            queue.Orders.RemoveAt(index);
            return true;
        }
        return false;
    }

    public bool SetRallyPoint(
        int playerId,
        GameplayBuildingId producer,
        Vector2 rallyPoint,
        ConstructionSystem construction)
    {
        if (!float.IsFinite(rallyPoint.X) || !float.IsFinite(rallyPoint.Y) ||
            !construction.IsAlive(producer) ||
            construction.Observe(producer).PlayerId != playerId)
            return false;
        if (!_queues.TryGetValue(producer.Value, out var queue))
        {
            queue = new ProducerQueue(producer);
            _queues.Add(producer.Value, queue);
        }
        queue.RallyPoint = rallyPoint;
        return true;
    }

    public void Update(
        float delta,
        ConstructionSystem construction,
        PlayerEconomyStore economy,
        UnitStore units,
        StaticWorld world,
        Func<UnitTypeProfile, int, Vector2, int> spawn,
        Action<int, Vector2> moveToRally)
    {
        ReleaseDeadUnitSupply(units, economy);
        List<int>? retiredProducers = null;
        foreach (var pair in _queues)
        {
            var queue = pair.Value;
            if (!construction.IsAlive(queue.Producer))
            {
                foreach (var canceledOrder in queue.Orders)
                    economy.Refund(
                        canceledOrder.PlayerId, canceledOrder.Recipe.Cost);
                queue.Orders.Clear();
                retiredProducers ??= [];
                retiredProducers.Add(pair.Key);
                continue;
            }
            if (queue.Orders.Count == 0) continue;
            var order = queue.Orders[0];
            if (order.State != ProductionOrderState.WaitingForExit)
            {
                order.State = ProductionOrderState.Producing;
                order.Progress = Math.Clamp(
                    order.Progress + delta / order.Recipe.ProductionSeconds,
                    0f, 1f);
                if (order.Progress >= 1f)
                    order.State = ProductionOrderState.WaitingForExit;
            }
            if (order.State != ProductionOrderState.WaitingForExit) continue;
            var building = construction.Observe(queue.Producer);
            if (!TryFindExit(
                    building.Bounds,
                    order.Recipe.UnitType.Movement.PhysicalRadius,
                    queue.RallyPoint,
                    units,
                    world,
                    out var exit))
                continue;
            var unit = spawn(order.Recipe.UnitType, order.PlayerId, exit);
            _producedUnits.Add(new ProducedUnitPopulation(
                unit, order.PlayerId, order.Recipe.Cost.Supply));
            if (queue.RallyPoint.HasValue)
                moveToRally(unit, queue.RallyPoint.Value);
            queue.Orders.RemoveAt(0);
        }
        if (retiredProducers is not null)
        {
            foreach (var producer in retiredProducers)
                _queues.Remove(producer);
        }
    }

    public ProductionQueueSnapshot Observe(GameplayBuildingId producer)
    {
        if (!_queues.TryGetValue(producer.Value, out var queue))
            return new ProductionQueueSnapshot(producer, null, []);
        return new ProductionQueueSnapshot(
            producer,
            queue.RallyPoint,
            queue.Orders.Select(value => value.Snapshot(producer)).ToArray());
    }

    public ProductionQueueSnapshot[] CreateOverview() =>
        _queues.Values
            .Where(value => value.Orders.Count > 0 || value.RallyPoint.HasValue)
            .OrderBy(value => value.Producer.Value)
            .Select(value => Observe(value.Producer))
            .ToArray();

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(_nextOrderId);
        hash.Add(_producedUnits.Count);
        foreach (var unit in _producedUnits)
        {
            hash.Add(unit.Unit);
            hash.Add(unit.PlayerId);
            hash.Add(unit.Supply);
        }
        if (_queues.Count == 0)
        {
            hash.Add(0);
            return;
        }
        var queues = _queues.Values.OrderBy(value => value.Producer.Value).ToArray();
        hash.Add(queues.Length);
        foreach (var queue in queues)
        {
            hash.Add(queue.Producer.Value);
            hash.Add(queue.RallyPoint.HasValue);
            if (queue.RallyPoint.HasValue) hash.Add(queue.RallyPoint.Value);
            hash.Add(queue.Orders.Count);
            foreach (var order in queue.Orders)
            {
                hash.Add(order.Id.Value);
                hash.Add(order.PlayerId);
                hash.Add(order.Recipe.Id);
                hash.Add((byte)order.State);
                hash.Add(order.Progress);
            }
        }
    }

    private static bool TryFindExit(
        SimRect bounds,
        float radius,
        Vector2? rally,
        UnitStore units,
        StaticWorld world,
        out Vector2 exit)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var offset = radius + 6f;
        Span<Vector2> candidates = stackalloc Vector2[12]
        {
            new(bounds.Max.X + offset, center.Y),
            new(bounds.Min.X - offset, center.Y),
            new(center.X, bounds.Max.Y + offset),
            new(center.X, bounds.Min.Y - offset),
            new(bounds.Max.X + offset, bounds.Max.Y + offset),
            new(bounds.Max.X + offset, bounds.Min.Y - offset),
            new(bounds.Min.X - offset, bounds.Max.Y + offset),
            new(bounds.Min.X - offset, bounds.Min.Y - offset),
            new(bounds.Max.X + offset, bounds.Min.Y + bounds.Height * 0.25f),
            new(bounds.Max.X + offset, bounds.Min.Y + bounds.Height * 0.75f),
            new(bounds.Min.X - offset, bounds.Min.Y + bounds.Height * 0.25f),
            new(bounds.Min.X - offset, bounds.Min.Y + bounds.Height * 0.75f)
        };
        var bestDistance = float.PositiveInfinity;
        exit = default;
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (!world.IsDiscFree(candidate, radius) ||
                UnitOverlaps(candidate, radius, units)) continue;
            var score = rally.HasValue
                ? Vector2.DistanceSquared(candidate, rally.Value)
                : index;
            if (score >= bestDistance) continue;
            bestDistance = score;
            exit = candidate;
        }
        return float.IsFinite(bestDistance);
    }

    private static bool UnitOverlaps(Vector2 position, float radius, UnitStore units)
    {
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (!units.Alive[unit]) continue;
            var minimum = radius + units.Radii[unit] + 1f;
            if (Vector2.DistanceSquared(position, units.Positions[unit]) <
                minimum * minimum) return true;
        }
        return false;
    }

    private void ReleaseDeadUnitSupply(
        UnitStore units,
        PlayerEconomyStore economy)
    {
        for (var index = _producedUnits.Count - 1; index >= 0; index--)
        {
            var produced = _producedUnits[index];
            if (units.Alive[produced.Unit]) continue;
            economy.Refund(
                produced.PlayerId,
                new EconomyCost(0, 0, produced.Supply));
            _producedUnits.RemoveAt(index);
        }
    }

    private static bool ValidRecipe(ProductionRecipeProfile recipe) =>
        recipe.Id >= 0 && !string.IsNullOrWhiteSpace(recipe.Name) &&
        ProductionCatalogSnapshot.ValidUnitProfile(recipe.UnitType) &&
        recipe.ProducerBuildingTypeId >= 0 && recipe.Cost.IsValid &&
        recipe.Cost.Supply > 0 && float.IsFinite(recipe.ProductionSeconds) &&
        recipe.ProductionSeconds > 0f &&
        float.IsFinite(recipe.CancelRefundFraction) &&
        recipe.CancelRefundFraction is >= 0f and <= 1f;

    private static ProductionCommandResult Failure(ProductionCommandCode code) =>
        new(code, default);

    private sealed class ProducerQueue(GameplayBuildingId producer)
    {
        public GameplayBuildingId Producer { get; } = producer;
        public Vector2? RallyPoint { get; set; }
        public List<ProductionOrder> Orders { get; } = [];
    }

    private sealed class ProductionOrder(
        ProductionOrderId id,
        int playerId,
        ProductionRecipeProfile recipe)
    {
        public ProductionOrderId Id { get; } = id;
        public int PlayerId { get; } = playerId;
        public ProductionRecipeProfile Recipe { get; } = recipe;
        public ProductionOrderState State { get; set; } = ProductionOrderState.Queued;
        public float Progress { get; set; }
        public ProductionOrderSnapshot Snapshot(GameplayBuildingId producer) =>
            new(Id, producer, PlayerId, Recipe, State, Progress);
    }

    private readonly record struct ProducedUnitPopulation(
        int unit,
        int playerId,
        int supply)
    {
        public int Unit { get; } = unit;
        public int PlayerId { get; } = playerId;
        public int Supply { get; } = supply;
    }
}
