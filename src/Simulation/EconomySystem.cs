using System.Numerics;

namespace RtsDemo.Simulation;

public enum EconomyResourceKind : byte
{
    Minerals,
    VespeneGas
}

public readonly record struct EconomyCost(
    int Minerals,
    int VespeneGas,
    int Supply = 0)
{
    public bool IsValid => Minerals >= 0 && VespeneGas >= 0 && Supply >= 0;
}

public enum EconomyTransactionCode : byte
{
    Success,
    InvalidPlayer,
    InvalidCost,
    InsufficientMinerals,
    InsufficientVespeneGas,
    SupplyBlocked
}

public readonly record struct EconomyTransactionResult(
    EconomyTransactionCode Code,
    EconomyCost Cost)
{
    public bool Succeeded => Code == EconomyTransactionCode.Success;
}

public readonly record struct PlayerEconomySnapshot(
    int PlayerId,
    int Minerals,
    int VespeneGas,
    int SupplyUsed,
    int SupplyCapacity)
{
    public int SupplyRemaining => SupplyCapacity - SupplyUsed;
}

public sealed class PlayerEconomyStore
{
    private const int MaximumPlayers = 16;
    private readonly bool[] _registered = new bool[MaximumPlayers];
    private readonly int[] _minerals = new int[MaximumPlayers];
    private readonly int[] _vespene = new int[MaximumPlayers];
    private readonly int[] _supplyUsed = new int[MaximumPlayers];
    private readonly int[] _supplyCapacity = new int[MaximumPlayers];

    public bool HasRegisteredPlayers => _registered.Any(value => value);

    public void RegisterPlayer(
        int playerId,
        int minerals,
        int vespeneGas,
        int supplyCapacity,
        int supplyUsed = 0)
    {
        ValidatePlayerRange(playerId);
        if (minerals < 0 || vespeneGas < 0 || supplyCapacity < 0 ||
            supplyUsed < 0 || supplyUsed > supplyCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(minerals));
        }
        _registered[playerId] = true;
        _minerals[playerId] = minerals;
        _vespene[playerId] = vespeneGas;
        _supplyCapacity[playerId] = supplyCapacity;
        _supplyUsed[playerId] = supplyUsed;
    }

    public bool IsRegistered(int playerId) =>
        (uint)playerId < MaximumPlayers && _registered[playerId];

    public PlayerEconomySnapshot Snapshot(int playerId)
    {
        ValidateRegistered(playerId);
        return new PlayerEconomySnapshot(
            playerId,
            _minerals[playerId],
            _vespene[playerId],
            _supplyUsed[playerId],
            _supplyCapacity[playerId]);
    }

    public EconomyTransactionResult TrySpend(int playerId, EconomyCost cost)
    {
        if (!IsRegistered(playerId))
        {
            return new EconomyTransactionResult(
                EconomyTransactionCode.InvalidPlayer, cost);
        }
        if (!cost.IsValid)
        {
            return new EconomyTransactionResult(
                EconomyTransactionCode.InvalidCost, cost);
        }
        if (_minerals[playerId] < cost.Minerals)
        {
            return new EconomyTransactionResult(
                EconomyTransactionCode.InsufficientMinerals, cost);
        }
        if (_vespene[playerId] < cost.VespeneGas)
        {
            return new EconomyTransactionResult(
                EconomyTransactionCode.InsufficientVespeneGas, cost);
        }
        if (cost.Supply > _supplyCapacity[playerId] - _supplyUsed[playerId])
        {
            return new EconomyTransactionResult(
                EconomyTransactionCode.SupplyBlocked, cost);
        }
        _minerals[playerId] -= cost.Minerals;
        _vespene[playerId] -= cost.VespeneGas;
        _supplyUsed[playerId] += cost.Supply;
        return new EconomyTransactionResult(EconomyTransactionCode.Success, cost);
    }

    public void Refund(int playerId, EconomyCost cost, float fraction = 1f)
    {
        ValidateRegistered(playerId);
        if (!cost.IsValid || !float.IsFinite(fraction) || fraction < 0f ||
            fraction > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(cost));
        }
        _minerals[playerId] = checked(
            _minerals[playerId] + (int)MathF.Floor(cost.Minerals * fraction));
        _vespene[playerId] = checked(
            _vespene[playerId] + (int)MathF.Floor(cost.VespeneGas * fraction));
        _supplyUsed[playerId] = Math.Max(
            0, _supplyUsed[playerId] - cost.Supply);
    }

    public void Credit(int playerId, EconomyResourceKind kind, int amount)
    {
        ValidateRegistered(playerId);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        if (kind == EconomyResourceKind.Minerals)
        {
            _minerals[playerId] = checked(_minerals[playerId] + amount);
        }
        else if (kind == EconomyResourceKind.VespeneGas)
        {
            _vespene[playerId] = checked(_vespene[playerId] + amount);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public void AddSupplyCapacity(int playerId, int amount)
    {
        ValidateRegistered(playerId);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        _supplyCapacity[playerId] = checked(_supplyCapacity[playerId] + amount);
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        for (var player = 0; player < MaximumPlayers; player++)
        {
            hash.Add(_registered[player]);
            if (!_registered[player])
            {
                continue;
            }
            hash.Add(player);
            hash.Add(_minerals[player]);
            hash.Add(_vespene[player]);
            hash.Add(_supplyUsed[player]);
            hash.Add(_supplyCapacity[player]);
        }
    }

    private void ValidateRegistered(int playerId)
    {
        if (!IsRegistered(playerId))
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
    }

    private static void ValidatePlayerRange(int playerId)
    {
        if ((uint)playerId >= MaximumPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
    }
}

public readonly record struct EconomyResourceNodeId(int Value);
public readonly record struct EconomyDropOffId(int Value);

public readonly record struct EconomyResourceNodeSnapshot(
    EconomyResourceNodeId Id,
    EconomyResourceKind Kind,
    Vector2 Position,
    int Remaining,
    int ActiveHarvesters,
    int HarvesterCapacity,
    bool RequiresRefinery,
    bool Operational);

public enum WorkerEconomyState : byte
{
    None,
    Idle,
    GoingToResource,
    WaitingForResource,
    Gathering,
    ReturningCargo
}

public readonly record struct WorkerEconomySnapshot(
    int UnitId,
    int PlayerId,
    WorkerEconomyState State,
    EconomyResourceNodeId TargetNode,
    EconomyResourceKind CargoKind,
    int CargoAmount,
    float WorkRemaining);

public sealed class EconomyOverviewSnapshot
{
    public EconomyOverviewSnapshot(
        PlayerEconomySnapshot player,
        EconomyResourceNodeSnapshot[] resourceNodes,
        int workers,
        int gathering,
        int returning,
        int waiting)
    {
        Player = player;
        ResourceNodes = resourceNodes;
        Workers = workers;
        Gathering = gathering;
        Returning = returning;
        Waiting = waiting;
    }

    public PlayerEconomySnapshot Player { get; }
    public EconomyResourceNodeSnapshot[] ResourceNodes { get; }
    public int Workers { get; }
    public int Gathering { get; }
    public int Returning { get; }
    public int Waiting { get; }
}

public enum GatherCommandCode : byte
{
    Success,
    InvalidUnit,
    UnitNotWorker,
    WrongOwner,
    InvalidNode,
    RefineryRequired,
    ResourceDepleted,
    MissingDropOff
}

public readonly record struct GatherCommandResult(
    GatherCommandCode Code,
    int UnitId,
    EconomyResourceNodeId NodeId)
{
    public bool Succeeded => Code == GatherCommandCode.Success;
}

public sealed class EconomySystem
{
    private const float NodeArrivalPadding = 30f;
    private const float DropOffArrivalRadius = 52f;
    private readonly bool[] _workers;
    private readonly int[] _workerPlayers;
    private readonly WorkerEconomyState[] _workerStates;
    private readonly int[] _workerNodes;
    private readonly EconomyResourceKind[] _cargoKinds;
    private readonly int[] _cargoAmounts;
    private readonly float[] _workRemaining;
    private readonly List<ResourceNode> _nodes = [];
    private readonly List<DropOff> _dropOffs = [];
    private int _registeredWorkerCount;

    public EconomySystem(int unitCapacity)
    {
        Players = new PlayerEconomyStore();
        _workers = new bool[unitCapacity];
        _workerPlayers = new int[unitCapacity];
        _workerStates = new WorkerEconomyState[unitCapacity];
        _workerNodes = new int[unitCapacity];
        _cargoKinds = new EconomyResourceKind[unitCapacity];
        _cargoAmounts = new int[unitCapacity];
        _workRemaining = new float[unitCapacity];
        Array.Fill(_workerNodes, -1);
    }

    public PlayerEconomyStore Players { get; }
    public int ResourceNodeCount => _nodes.Count;
    public int DropOffCount => _dropOffs.Count;
    public bool HasRuntimeState =>
        Players.HasRegisteredPlayers || _nodes.Count > 0 || _dropOffs.Count > 0 ||
        _workers.Any(value => value);

    public EconomyResourceNodeId AddResourceNode(
        EconomyResourceKind kind,
        Vector2 position,
        int amount,
        int harvestBatch,
        float harvestSeconds,
        int harvesterCapacity,
        bool requiresRefinery = false,
        bool operational = true)
    {
        if (!Enum.IsDefined(kind) || !IsFinite(position) || amount <= 0 ||
            harvestBatch <= 0 || !float.IsFinite(harvestSeconds) ||
            harvestSeconds <= 0f || harvesterCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        var id = new EconomyResourceNodeId(_nodes.Count);
        _nodes.Add(new ResourceNode(
            id, kind, position, amount, harvestBatch, harvestSeconds,
            harvesterCapacity, requiresRefinery, operational));
        return id;
    }

    public EconomyDropOffId AddDropOff(
        int playerId,
        Vector2 position,
        bool acceptsMinerals = true,
        bool acceptsVespene = true)
    {
        if (!Players.IsRegistered(playerId) || !IsFinite(position) ||
            !acceptsMinerals && !acceptsVespene)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
        var id = new EconomyDropOffId(_dropOffs.Count);
        _dropOffs.Add(new DropOff(
            id, playerId, position, acceptsMinerals, acceptsVespene));
        return id;
    }

    public void RegisterWorker(int unit, int playerId)
    {
        ValidateUnit(unit);
        if (!Players.IsRegistered(playerId))
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
        if (!_workers[unit])
        {
            _registeredWorkerCount++;
        }
        _workers[unit] = true;
        _workerPlayers[unit] = playerId;
        _workerStates[unit] = WorkerEconomyState.Idle;
        _workerNodes[unit] = -1;
    }

    public void SetRefineryOperational(EconomyResourceNodeId nodeId, bool value)
    {
        var node = Node(nodeId);
        if (!node.RequiresRefinery)
        {
            throw new InvalidOperationException(
                "Only a refinery resource node can change operational state.");
        }
        node.Operational = value;
    }

    public GatherCommandResult ValidateGather(
        int issuingPlayerId,
        int unit,
        EconomyResourceNodeId nodeId)
    {
        if ((uint)unit >= (uint)_workers.Length)
        {
            return Failure(GatherCommandCode.InvalidUnit, unit, nodeId);
        }
        if (!_workers[unit])
        {
            return Failure(GatherCommandCode.UnitNotWorker, unit, nodeId);
        }
        if (_workerPlayers[unit] != issuingPlayerId)
        {
            return Failure(GatherCommandCode.WrongOwner, unit, nodeId);
        }
        if ((uint)nodeId.Value >= (uint)_nodes.Count)
        {
            return Failure(GatherCommandCode.InvalidNode, unit, nodeId);
        }
        var node = _nodes[nodeId.Value];
        if (node.Remaining <= 0)
        {
            return Failure(GatherCommandCode.ResourceDepleted, unit, nodeId);
        }
        if (node.RequiresRefinery && !node.Operational)
        {
            return Failure(GatherCommandCode.RefineryRequired, unit, nodeId);
        }
        if (FindNearestDropOff(issuingPlayerId, node.Kind, node.Position) < 0)
        {
            return Failure(GatherCommandCode.MissingDropOff, unit, nodeId);
        }
        return new GatherCommandResult(GatherCommandCode.Success, unit, nodeId);
    }

    public void BeginGather(int unit, EconomyResourceNodeId nodeId)
    {
        ReleaseClaim(unit);
        _workerNodes[unit] = nodeId.Value;
        _workerStates[unit] = WorkerEconomyState.GoingToResource;
        _cargoAmounts[unit] = 0;
        _workRemaining[unit] = 0f;
    }

    public void Cancel(ReadOnlySpan<int> units)
    {
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            if ((uint)unit >= (uint)_workers.Length || !_workers[unit])
            {
                continue;
            }
            ReleaseClaim(unit);
            _workerStates[unit] = WorkerEconomyState.Idle;
            _workerNodes[unit] = -1;
            _workRemaining[unit] = 0f;
        }
    }

    public void Update(
        float delta,
        UnitStore units,
        Action<int, Vector2> moveWorker,
        Action<int> stopWorker)
    {
        if (_registeredWorkerCount == 0)
        {
            return;
        }
        for (var unit = 0; unit < units.Count; unit++)
        {
            if (!_workers[unit] || _workerStates[unit] is
                    WorkerEconomyState.None or WorkerEconomyState.Idle)
            {
                continue;
            }
            if (!units.Alive[unit])
            {
                ReleaseClaim(unit);
                _workerStates[unit] = WorkerEconomyState.Idle;
                continue;
            }
            switch (_workerStates[unit])
            {
                case WorkerEconomyState.GoingToResource:
                    UpdateGoingToResource(unit, units, moveWorker, stopWorker);
                    break;
                case WorkerEconomyState.WaitingForResource:
                    UpdateWaitingForResource(
                        unit, units, moveWorker, stopWorker);
                    break;
                case WorkerEconomyState.Gathering:
                    UpdateGathering(unit, delta, units, moveWorker);
                    break;
                case WorkerEconomyState.ReturningCargo:
                    UpdateReturning(unit, units, moveWorker);
                    break;
            }
        }
    }

    public EconomyResourceNodeSnapshot ObserveResourceNode(EconomyResourceNodeId id)
    {
        var node = Node(id);
        return new EconomyResourceNodeSnapshot(
            node.Id, node.Kind, node.Position, node.Remaining,
            node.ActiveHarvesters, node.HarvesterCapacity,
            node.RequiresRefinery, node.Operational);
    }

    public WorkerEconomySnapshot Worker(int unit)
    {
        ValidateUnit(unit);
        return new WorkerEconomySnapshot(
            unit,
            _workerPlayers[unit],
            _workerStates[unit],
            new EconomyResourceNodeId(_workerNodes[unit]),
            _cargoKinds[unit],
            _cargoAmounts[unit],
            _workRemaining[unit]);
    }

    public EconomyOverviewSnapshot CreateOverview(int playerId, int unitCount)
    {
        if (unitCount < 0 || unitCount > _workers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCount));
        }
        var nodes = new EconomyResourceNodeSnapshot[_nodes.Count];
        for (var index = 0; index < nodes.Length; index++)
        {
            nodes[index] = ObserveResourceNode(new EconomyResourceNodeId(index));
        }
        var workers = 0;
        var gathering = 0;
        var returning = 0;
        var waiting = 0;
        for (var unit = 0; unit < unitCount; unit++)
        {
            if (!_workers[unit] || _workerPlayers[unit] != playerId)
            {
                continue;
            }
            workers++;
            gathering += _workerStates[unit] == WorkerEconomyState.Gathering ? 1 : 0;
            returning += _workerStates[unit] == WorkerEconomyState.ReturningCargo ? 1 : 0;
            waiting += _workerStates[unit] == WorkerEconomyState.WaitingForResource ? 1 : 0;
        }
        return new EconomyOverviewSnapshot(
            Players.Snapshot(playerId),
            nodes,
            workers,
            gathering,
            returning,
            waiting);
    }

    internal void AppendStateHash(ref StableHash64 hash, int unitCount)
    {
        Players.AppendStateHash(ref hash);
        hash.Add(_nodes.Count);
        for (var index = 0; index < _nodes.Count; index++)
        {
            var node = _nodes[index];
            hash.Add(node.Id.Value);
            hash.Add((byte)node.Kind);
            hash.Add(node.Position);
            hash.Add(node.Remaining);
            hash.Add(node.HarvestBatch);
            hash.Add(node.HarvestSeconds);
            hash.Add(node.ActiveHarvesters);
            hash.Add(node.HarvesterCapacity);
            hash.Add(node.RequiresRefinery);
            hash.Add(node.Operational);
        }
        hash.Add(_dropOffs.Count);
        for (var index = 0; index < _dropOffs.Count; index++)
        {
            var dropOff = _dropOffs[index];
            hash.Add(dropOff.Id.Value);
            hash.Add(dropOff.PlayerId);
            hash.Add(dropOff.Position);
            hash.Add(dropOff.AcceptsMinerals);
            hash.Add(dropOff.AcceptsVespene);
        }
        hash.Add(_registeredWorkerCount);
        if (_registeredWorkerCount == 0)
        {
            return;
        }
        for (var unit = 0; unit < unitCount; unit++)
        {
            hash.Add(_workers[unit]);
            if (!_workers[unit])
            {
                continue;
            }
            hash.Add(_workerPlayers[unit]);
            hash.Add((byte)_workerStates[unit]);
            hash.Add(_workerNodes[unit]);
            hash.Add((byte)_cargoKinds[unit]);
            hash.Add(_cargoAmounts[unit]);
            hash.Add(_workRemaining[unit]);
        }
    }

    private void UpdateGoingToResource(
        int unit,
        UnitStore units,
        Action<int, Vector2> moveWorker,
        Action<int> stopWorker)
    {
        var nodeIndex = _workerNodes[unit];
        if (!IsNodeAvailable(nodeIndex))
        {
            RetargetOrIdle(unit, units.Positions[unit], moveWorker);
            return;
        }
        var node = _nodes[nodeIndex];
        var arrival = NodeArrivalPadding + units.Radii[unit];
        if (Vector2.DistanceSquared(units.Positions[unit], node.Position) <=
            arrival * arrival)
        {
            TryStartGathering(unit, stopWorker);
        }
    }

    private void UpdateWaitingForResource(
        int unit,
        UnitStore units,
        Action<int, Vector2> moveWorker,
        Action<int> stopWorker)
    {
        if (!IsNodeAvailable(_workerNodes[unit]))
        {
            RetargetOrIdle(unit, units.Positions[unit], moveWorker);
            return;
        }
        TryStartGathering(unit, stopWorker);
    }

    private void TryStartGathering(int unit, Action<int> stopWorker)
    {
        var nodeIndex = _workerNodes[unit];
        if (!IsNodeAvailable(nodeIndex))
        {
            _workerStates[unit] = WorkerEconomyState.Idle;
            return;
        }
        var node = _nodes[nodeIndex];
        if (node.ActiveHarvesters >= node.HarvesterCapacity)
        {
            _workerStates[unit] = WorkerEconomyState.WaitingForResource;
            return;
        }
        node.ActiveHarvesters++;
        _workerStates[unit] = WorkerEconomyState.Gathering;
        _workRemaining[unit] = node.HarvestSeconds;
        stopWorker(unit);
    }

    private void UpdateGathering(
        int unit,
        float delta,
        UnitStore units,
        Action<int, Vector2> moveWorker)
    {
        _workRemaining[unit] -= delta;
        if (_workRemaining[unit] > 0f)
        {
            return;
        }
        var node = _nodes[_workerNodes[unit]];
        var amount = Math.Min(node.HarvestBatch, node.Remaining);
        node.Remaining -= amount;
        node.ActiveHarvesters--;
        if (amount <= 0)
        {
            RetargetOrIdle(unit, units.Positions[unit], moveWorker);
            return;
        }
        _cargoKinds[unit] = node.Kind;
        _cargoAmounts[unit] = amount;
        var dropOff = FindNearestDropOff(
            _workerPlayers[unit], node.Kind, units.Positions[unit]);
        if (dropOff < 0)
        {
            _workerStates[unit] = WorkerEconomyState.Idle;
            return;
        }
        _workerStates[unit] = WorkerEconomyState.ReturningCargo;
        moveWorker(unit, _dropOffs[dropOff].Position);
    }

    private void UpdateReturning(
        int unit,
        UnitStore units,
        Action<int, Vector2> moveWorker)
    {
        var dropOffIndex = FindNearestDropOff(
            _workerPlayers[unit], _cargoKinds[unit], units.Positions[unit]);
        if (dropOffIndex < 0)
        {
            _workerStates[unit] = WorkerEconomyState.Idle;
            return;
        }
        var dropOff = _dropOffs[dropOffIndex];
        if (Vector2.DistanceSquared(units.Positions[unit], dropOff.Position) >
            DropOffArrivalRadius * DropOffArrivalRadius)
        {
            return;
        }
        Players.Credit(
            _workerPlayers[unit], _cargoKinds[unit], _cargoAmounts[unit]);
        _cargoAmounts[unit] = 0;
        var nodeIndex = _workerNodes[unit];
        if (!IsNodeAvailable(nodeIndex))
        {
            nodeIndex = FindNearestNode(
                _cargoKinds[unit], units.Positions[unit]);
            _workerNodes[unit] = nodeIndex;
        }
        if (nodeIndex < 0)
        {
            _workerStates[unit] = WorkerEconomyState.Idle;
            return;
        }
        _workerStates[unit] = WorkerEconomyState.GoingToResource;
        moveWorker(unit, _nodes[nodeIndex].Position);
    }

    private void RetargetOrIdle(
        int unit,
        Vector2 position,
        Action<int, Vector2> moveWorker)
    {
        var kind = _workerNodes[unit] >= 0
            ? _nodes[_workerNodes[unit]].Kind
            : _cargoKinds[unit];
        var replacement = FindNearestNode(kind, position);
        _workerNodes[unit] = replacement;
        _workerStates[unit] = replacement >= 0
            ? WorkerEconomyState.GoingToResource
            : WorkerEconomyState.Idle;
        if (replacement >= 0)
        {
            moveWorker(unit, _nodes[replacement].Position);
        }
    }

    private int FindNearestNode(EconomyResourceKind kind, Vector2 position)
    {
        var best = -1;
        var bestDistance = float.PositiveInfinity;
        for (var index = 0; index < _nodes.Count; index++)
        {
            if (_nodes[index].Kind != kind || !IsNodeAvailable(index))
            {
                continue;
            }
            var distance = Vector2.DistanceSquared(position, _nodes[index].Position);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }
        return best;
    }

    private int FindNearestDropOff(
        int playerId,
        EconomyResourceKind kind,
        Vector2 position)
    {
        var best = -1;
        var bestDistance = float.PositiveInfinity;
        for (var index = 0; index < _dropOffs.Count; index++)
        {
            var dropOff = _dropOffs[index];
            if (dropOff.PlayerId != playerId || !dropOff.Accepts(kind))
            {
                continue;
            }
            var distance = Vector2.DistanceSquared(position, dropOff.Position);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }
        return best;
    }

    private bool IsNodeAvailable(int nodeIndex) =>
        (uint)nodeIndex < (uint)_nodes.Count &&
        _nodes[nodeIndex].Remaining > 0 &&
        (!_nodes[nodeIndex].RequiresRefinery || _nodes[nodeIndex].Operational);

    private void ReleaseClaim(int unit)
    {
        if (_workerStates[unit] == WorkerEconomyState.Gathering &&
            (uint)_workerNodes[unit] < (uint)_nodes.Count)
        {
            _nodes[_workerNodes[unit]].ActiveHarvesters = Math.Max(
                0, _nodes[_workerNodes[unit]].ActiveHarvesters - 1);
        }
    }

    private ResourceNode Node(EconomyResourceNodeId id)
    {
        if ((uint)id.Value >= (uint)_nodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }
        return _nodes[id.Value];
    }

    private void ValidateUnit(int unit)
    {
        if ((uint)unit >= (uint)_workers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }
    }

    private static GatherCommandResult Failure(
        GatherCommandCode code,
        int unit,
        EconomyResourceNodeId nodeId) => new(code, unit, nodeId);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private sealed class ResourceNode(
        EconomyResourceNodeId id,
        EconomyResourceKind kind,
        Vector2 position,
        int remaining,
        int harvestBatch,
        float harvestSeconds,
        int harvesterCapacity,
        bool requiresRefinery,
        bool operational)
    {
        public EconomyResourceNodeId Id { get; } = id;
        public EconomyResourceKind Kind { get; } = kind;
        public Vector2 Position { get; } = position;
        public int Remaining { get; set; } = remaining;
        public int HarvestBatch { get; } = harvestBatch;
        public float HarvestSeconds { get; } = harvestSeconds;
        public int HarvesterCapacity { get; } = harvesterCapacity;
        public bool RequiresRefinery { get; } = requiresRefinery;
        public bool Operational { get; set; } = operational;
        public int ActiveHarvesters { get; set; }
    }

    private sealed class DropOff(
        EconomyDropOffId id,
        int playerId,
        Vector2 position,
        bool acceptsMinerals,
        bool acceptsVespene)
    {
        public EconomyDropOffId Id { get; } = id;
        public int PlayerId { get; } = playerId;
        public Vector2 Position { get; } = position;
        public bool AcceptsMinerals { get; } = acceptsMinerals;
        public bool AcceptsVespene { get; } = acceptsVespene;
        public bool Accepts(EconomyResourceKind kind) =>
            kind == EconomyResourceKind.Minerals
                ? AcceptsMinerals
                : AcceptsVespene;
    }
}
