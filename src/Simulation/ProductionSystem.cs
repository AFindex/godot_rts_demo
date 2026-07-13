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
    MissingPrerequisite,
    InvalidOrder,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public readonly record struct ProductionCommandResult(
    ProductionCommandCode Code,
    ProductionOrderId OrderId)
{
    public bool Succeeded => Code == ProductionCommandCode.Success;
}

public readonly record struct ProductionRequirementStatus(
    ProductionRequirementProfile Requirement,
    int CurrentCount)
{
    public bool Satisfied => CurrentCount >= Requirement.Count;
}

public readonly record struct ProductionAvailabilitySnapshot(
    ProductionCommandCode Code,
    ProductionRequirementStatus[] Requirements)
{
    public bool Available => Code == ProductionCommandCode.Success;
}

public enum ProductionOrderState : byte
{
    Queued,
    Producing,
    WaitingForExit
}

public enum RallyTargetKind : byte
{
    None,
    Ground,
    ResourceNode,
    FriendlyUnit
}

/// <summary>
/// Stable production-rally intent. Position is the deterministic fallback used
/// when an entity target disappears before the produced unit can resolve it.
/// </summary>
public readonly record struct RallyTarget(
    RallyTargetKind Kind,
    Vector2 Position,
    EconomyResourceNodeId ResourceNode,
    int Unit)
{
    public static RallyTarget None => new(
        RallyTargetKind.None, default, new EconomyResourceNodeId(-1), -1);
    public static RallyTarget Ground(Vector2 position) => new(
        RallyTargetKind.Ground, position, new EconomyResourceNodeId(-1), -1);
    public static RallyTarget Resource(EconomyResourceNodeId node, Vector2 position) =>
        new(RallyTargetKind.ResourceNode, position, node, -1);
    public static RallyTarget Friendly(int unit, Vector2 position) => new(
        RallyTargetKind.FriendlyUnit, position, new EconomyResourceNodeId(-1), unit);

    public bool IsSet => Kind != RallyTargetKind.None;
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
    RallyTarget Rally,
    ProductionOrderSnapshot[] Orders);

public readonly record struct ProductionOrderRuntimeEntry(
    ProductionOrderId Id,
    int PlayerId,
    ProductionRecipeProfile Recipe,
    ProductionOrderState State,
    float Progress);

public readonly record struct ProducerQueueRuntimeEntry(
    GameplayBuildingId Producer,
    RallyTarget Rally,
    ProductionOrderRuntimeEntry[] Orders);

public readonly record struct ProducedUnitPopulationRuntimeEntry(
    int Unit,
    int PlayerId,
    int Supply);

public sealed record ProductionRuntimeSnapshot(
    int NextOrderId,
    ProducerQueueRuntimeEntry[] Queues,
    ProducedUnitPopulationRuntimeEntry[] ProducedUnits);

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
        if (!ValidRecipeProfile(recipe)) return Failure(ProductionCommandCode.InvalidRecipe);
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
        var requirements = EvaluateRequirements(
            playerId, recipe.Requirements, construction);
        if (requirements.Any(value => !value.Satisfied))
            return Failure(ProductionCommandCode.MissingPrerequisite);
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

    public ProductionAvailabilitySnapshot ObserveAvailability(
        int playerId,
        GameplayBuildingId producer,
        ProductionRecipeProfile recipe,
        ConstructionSystem construction,
        PlayerEconomyStore economy)
    {
        var result = ValidateEnqueue(
            playerId, producer, recipe, construction, economy);
        return new ProductionAvailabilitySnapshot(
            result.Code,
            ProductionCatalogSnapshot.ValidRequirements(recipe.Requirements)
                ? EvaluateRequirements(playerId, recipe.Requirements, construction)
                : []);
    }

    public bool Cancel(
        int playerId,
        ProductionOrderId orderId,
        PlayerEconomyStore economy,
        out GameplayBuildingId producer)
    {
        producer = default;
        foreach (var queue in _queues.Values)
        {
            var index = queue.Orders.FindIndex(value => value.Id == orderId);
            if (index < 0 || queue.Orders[index].PlayerId != playerId) continue;
            var order = queue.Orders[index];
            economy.Refund(playerId, order.Recipe.Cost,
                order.Recipe.CancelRefundFraction);
            queue.Orders.RemoveAt(index);
            producer = queue.Producer;
            return true;
        }
        return false;
    }

    public bool SetRallyTarget(
        int playerId,
        GameplayBuildingId producer,
        RallyTarget rally,
        ConstructionSystem construction)
    {
        if (!ValidRallyTarget(rally) ||
            !construction.IsAlive(producer) ||
            construction.Observe(producer).PlayerId != playerId)
            return false;
        if (!_queues.TryGetValue(producer.Value, out var queue))
        {
            queue = new ProducerQueue(producer);
            _queues.Add(producer.Value, queue);
        }
        queue.Rally = rally;
        return true;
    }

    public void Update(
        float delta,
        ConstructionSystem construction,
        PlayerEconomyStore economy,
        UnitStore units,
        CombatStore combat,
        StaticWorld world,
        Func<UnitTypeProfile, int, Vector2, int> spawn,
        Action<int, int, RallyTarget> applyRally,
        Func<int, int, SimRect, float, bool> evacuateFriendlyExitBlocker)
    {
        ReleaseDeadUnitSupply(units, economy);
        List<int>? retiredProducers = null;
        Span<int> friendlyBlockers = stackalloc int[
            ProductionExitResolver.MaximumReportedFriendlyBlockers];
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
            var exit = ProductionExitResolver.Resolve(
                building.Bounds,
                order.Recipe.UnitType.Movement.PhysicalRadius,
                queue.Rally.IsSet ? queue.Rally.Position : null,
                order.PlayerId,
                units,
                combat,
                world,
                friendlyBlockers);
            if (exit.Status == ProductionExitStatus.SoftBlockedByFriendly)
            {
                for (var blocker = 0;
                     blocker < exit.FriendlyBlockerCount;
                     blocker++)
                {
                    evacuateFriendlyExitBlocker(
                        order.PlayerId,
                        friendlyBlockers[blocker],
                        building.Bounds,
                        order.Recipe.UnitType.Movement.PhysicalRadius);
                }
                continue;
            }
            if (exit.Status != ProductionExitStatus.Available)
                continue;
            var unit = spawn(
                order.Recipe.UnitType, order.PlayerId, exit.Position);
            _producedUnits.Add(new ProducedUnitPopulation(
                unit, order.PlayerId, order.Recipe.Cost.Supply));
            if (queue.Rally.IsSet)
                applyRally(unit, order.PlayerId, queue.Rally);
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
            return new ProductionQueueSnapshot(producer, RallyTarget.None, []);
        return new ProductionQueueSnapshot(
            producer,
            queue.Rally,
            queue.Orders.Select(value => value.Snapshot(producer)).ToArray());
    }

    public ProductionQueueSnapshot[] CreateOverview() =>
        _queues.Values
            .Where(value => value.Orders.Count > 0 || value.Rally.IsSet)
            .OrderBy(value => value.Producer.Value)
            .Select(value => Observe(value.Producer))
            .ToArray();

    public ProductionRuntimeSnapshot CaptureRuntimeState() => new(
        _nextOrderId,
        _queues.Values.OrderBy(value => value.Producer.Value)
            .Select(value => new ProducerQueueRuntimeEntry(
                value.Producer,
                value.Rally,
                value.Orders.Select(order => new ProductionOrderRuntimeEntry(
                    order.Id, order.PlayerId, order.Recipe,
                    order.State, order.Progress)).ToArray()))
            .ToArray(),
        _producedUnits.Select(value =>
            new ProducedUnitPopulationRuntimeEntry(
                value.Unit, value.PlayerId, value.Supply)).ToArray());

    public void RestoreRuntimeState(
        ProductionRuntimeSnapshot snapshot,
        ConstructionSystem construction,
        EconomySystem economy,
        UnitStore units)
    {
        if (snapshot.NextOrderId <= 0)
            throw new InvalidOperationException("Invalid next production order ID.");
        _queues.Clear();
        _producedUnits.Clear();
        var orderIds = new HashSet<int>();
        var reservedSupply = new Dictionary<int, int>();
        var maximumOrderId = 0;
        foreach (var value in snapshot.Queues)
        {
            if (value.Producer.Value < 0 || !construction.IsAlive(value.Producer) ||
                value.Orders.Length > 0 &&
                    construction.Observe(value.Producer).State !=
                        BuildingLifecycleState.Completed ||
                !ValidRallyTarget(value.Rally) ||
                value.Rally.Kind == RallyTargetKind.ResourceNode &&
                    (value.Rally.ResourceNode.Value >= economy.ResourceNodeCount ||
                     economy.ResourceNodePosition(value.Rally.ResourceNode) !=
                         value.Rally.Position) ||
                value.Rally.Kind == RallyTargetKind.FriendlyUnit &&
                    value.Rally.Unit >= units.Count ||
                value.Orders.Length > MaximumQueueLength ||
                !_queues.TryAdd(value.Producer.Value,
                    new ProducerQueue(value.Producer)))
                throw new InvalidOperationException("Invalid production queue runtime entry.");
            var queue = _queues[value.Producer.Value];
            queue.Rally = value.Rally;
            for (var index = 0; index < value.Orders.Length; index++)
            {
                var order = value.Orders[index];
                if (order.Id.Value <= 0 || !orderIds.Add(order.Id.Value) ||
                    !economy.Players.IsRegistered(order.PlayerId) ||
                    construction.Observe(value.Producer).PlayerId != order.PlayerId ||
                    !ValidRecipeProfile(order.Recipe) ||
                    construction.Observe(value.Producer).Type.Id !=
                        order.Recipe.ProducerBuildingTypeId ||
                    !Enum.IsDefined(order.State) ||
                    !float.IsFinite(order.Progress) ||
                    order.Progress is < 0f or > 1f ||
                    index > 0 && order.State != ProductionOrderState.Queued ||
                    order.State == ProductionOrderState.WaitingForExit &&
                        order.Progress != 1f)
                    throw new InvalidOperationException("Invalid production order runtime entry.");
                queue.Orders.Add(new ProductionOrder(
                    order.Id, order.PlayerId, order.Recipe)
                {
                    State = order.State,
                    Progress = order.Progress
                });
                maximumOrderId = Math.Max(maximumOrderId, order.Id.Value);
                reservedSupply[order.PlayerId] =
                    reservedSupply.GetValueOrDefault(order.PlayerId) +
                    order.Recipe.Cost.Supply;
            }
        }
        var producedIds = new HashSet<int>();
        foreach (var value in snapshot.ProducedUnits)
        {
            if ((uint)value.Unit >= (uint)units.Count ||
                !economy.Players.IsRegistered(value.PlayerId) || value.Supply <= 0 ||
                !producedIds.Add(value.Unit))
                throw new InvalidOperationException(
                    "Invalid produced-unit population runtime entry.");
            _producedUnits.Add(new ProducedUnitPopulation(
                value.Unit, value.PlayerId, value.Supply));
            reservedSupply[value.PlayerId] =
                reservedSupply.GetValueOrDefault(value.PlayerId) + value.Supply;
        }
        foreach (var value in reservedSupply)
        {
            if (value.Value > economy.Players.Snapshot(value.Key).SupplyUsed)
                throw new InvalidOperationException(
                    "Production reserved supply exceeds player supply used.");
        }
        if (snapshot.NextOrderId <= maximumOrderId)
            throw new InvalidOperationException("Next production order ID is stale.");
        _nextOrderId = snapshot.NextOrderId;
    }

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
            hash.Add((byte)queue.Rally.Kind);
            if (queue.Rally.IsSet)
            {
                hash.Add(queue.Rally.Position);
                hash.Add(queue.Rally.ResourceNode.Value);
                hash.Add(queue.Rally.Unit);
            }
            hash.Add(queue.Orders.Count);
            foreach (var order in queue.Orders)
            {
                hash.Add(order.Id.Value);
                hash.Add(order.PlayerId);
                hash.Add(order.Recipe.Id);
                hash.Add(order.Recipe.Requirements.Length);
                foreach (var requirement in order.Recipe.Requirements)
                {
                    hash.Add((byte)requirement.Kind);
                    hash.Add(requirement.TypeId);
                    hash.Add(requirement.Count);
                }
                hash.Add((byte)order.State);
                hash.Add(order.Progress);
            }
        }
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

    internal static bool ValidRecipeProfile(ProductionRecipeProfile recipe) =>
        recipe.Id >= 0 && !string.IsNullOrWhiteSpace(recipe.Name) &&
        ProductionCatalogSnapshot.ValidUnitProfile(recipe.UnitType) &&
        recipe.ProducerBuildingTypeId >= 0 && recipe.Cost.IsValid &&
        recipe.Cost.Supply > 0 && float.IsFinite(recipe.ProductionSeconds) &&
        recipe.ProductionSeconds > 0f &&
        float.IsFinite(recipe.CancelRefundFraction) &&
        recipe.CancelRefundFraction is >= 0f and <= 1f &&
        ProductionCatalogSnapshot.ValidRequirements(recipe.Requirements);

    private static ProductionRequirementStatus[] EvaluateRequirements(
        int playerId,
        System.Collections.Immutable.ImmutableArray<ProductionRequirementProfile>
            requirements,
        ConstructionSystem construction)
    {
        var result = new ProductionRequirementStatus[requirements.Length];
        for (var index = 0; index < requirements.Length; index++)
        {
            var requirement = requirements[index];
            var current = requirement.Kind ==
                          ProductionRequirementKind.CompletedBuilding
                ? construction.CountCompleted(playerId, requirement.TypeId)
                : 0;
            result[index] = new ProductionRequirementStatus(requirement, current);
        }
        return result;
    }

    internal static bool ValidRallyTarget(RallyTarget target) =>
        Enum.IsDefined(target.Kind) &&
        (target.Kind == RallyTargetKind.None
            ? target.ResourceNode.Value == -1 && target.Unit == -1
            : float.IsFinite(target.Position.X) && float.IsFinite(target.Position.Y) &&
              target.Kind switch
              {
                  RallyTargetKind.Ground =>
                      target.ResourceNode.Value == -1 && target.Unit == -1,
                  RallyTargetKind.ResourceNode =>
                      target.ResourceNode.Value >= 0 && target.Unit == -1,
                  RallyTargetKind.FriendlyUnit =>
                      target.ResourceNode.Value == -1 && target.Unit >= 0,
                  _ => false
              });

    private static ProductionCommandResult Failure(ProductionCommandCode code) =>
        new(code, default);

    private sealed class ProducerQueue(GameplayBuildingId producer)
    {
        public GameplayBuildingId Producer { get; } = producer;
        public RallyTarget Rally { get; set; } = RallyTarget.None;
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
