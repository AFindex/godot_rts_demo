using System.Numerics;

namespace RtsDemo.Simulation;

public enum EconomyResourceKind : byte
{
    Minerals,
    VespeneGas
}

public enum EconomyHarvestMode : byte
{
    Batch,
    Progressive
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
        var validation = ValidateSpend(playerId, cost);
        if (!validation.Succeeded)
        {
            return validation;
        }
        _minerals[playerId] -= cost.Minerals;
        _vespene[playerId] -= cost.VespeneGas;
        _supplyUsed[playerId] += cost.Supply;
        return new EconomyTransactionResult(EconomyTransactionCode.Success, cost);
    }

    public EconomyTransactionResult ValidateSpend(int playerId, EconomyCost cost)
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

    public void RemoveSupplyCapacity(int playerId, int amount)
    {
        ValidateRegistered(playerId);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        _supplyCapacity[playerId] = Math.Max(
            _supplyUsed[playerId], _supplyCapacity[playerId] - amount);
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

    internal PlayerEconomyRuntimeEntry[] CaptureRuntimeState()
    {
        var result = new List<PlayerEconomyRuntimeEntry>();
        for (var player = 0; player < MaximumPlayers; player++)
        {
            if (_registered[player])
            {
                result.Add(new PlayerEconomyRuntimeEntry(
                    player, _minerals[player], _vespene[player],
                    _supplyUsed[player], _supplyCapacity[player]));
            }
        }
        return result.ToArray();
    }

    internal void RestoreRuntimeState(PlayerEconomyRuntimeEntry[] players)
    {
        Array.Clear(_registered);
        Array.Clear(_minerals);
        Array.Clear(_vespene);
        Array.Clear(_supplyUsed);
        Array.Clear(_supplyCapacity);
        var previousId = -1;
        for (var index = 0; index < players.Length; index++)
        {
            var player = players[index];
            if (player.PlayerId <= previousId)
            {
                throw new InvalidOperationException(
                    "Economy player IDs must be unique and ascending.");
            }
            RegisterPlayer(
                player.PlayerId, player.Minerals, player.VespeneGas,
                player.SupplyCapacity, player.SupplyUsed);
            previousId = player.PlayerId;
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
public readonly record struct EconomyBaseId(int Value);

public readonly record struct EconomyResourceNodeSnapshot(
    EconomyResourceNodeId Id,
    EconomyResourceKind Kind,
    Vector2 Position,
    int Remaining,
    int ActiveNormal,
    int AssignedNormal,
    int WaitingNormal,
    int NormalActiveSlots,
    int IdealNormalAssignments,
    int ActiveMules,
    int AssignedMules,
    bool RequiresRefinery,
    bool Operational,
    Vector2 InteractionHalfExtents,
    float InteractionRadius,
    EconomyHarvestMode HarvestMode)
{
    public int ActiveHarvesters => ActiveNormal + ActiveMules;
    public int HarvesterCapacity => IdealNormalAssignments;
}

public enum WorkerEconomyState : byte
{
    None,
    Idle,
    GoingToResource,
    WaitingForResource,
    Gathering,
    ReturningCargo,
    WaitingForDropOff
}

public enum GathererCapability : byte
{
    None,
    NormalWorker,
    Mule
}

public readonly record struct WorkerEconomySnapshot(
    int UnitId,
    int PlayerId,
    WorkerEconomyState State,
    EconomyResourceNodeId TargetNode,
    EconomyResourceKind CargoKind,
    int CargoAmount,
    float WorkRemaining,
    GathererCapability Capability);

public readonly record struct PlayerEconomyRuntimeEntry(
    int PlayerId, int Minerals, int VespeneGas,
    int SupplyUsed, int SupplyCapacity);

public readonly record struct EconomyResourceNodeRuntimeEntry(
    EconomyResourceNodeId Id,
    EconomyResourceKind Kind,
    Vector2 Position,
    int Remaining,
    int HarvestBatch,
    float HarvestSeconds,
    int NormalActiveSlots,
    int IdealNormalAssignments,
    bool RequiresRefinery,
    bool Operational,
    int ActiveNormal,
    int AssignedNormal,
    int WaitingNormal,
    int ActiveMules,
    int AssignedMules,
    Vector2 InteractionHalfExtents,
    EconomyHarvestMode HarvestMode);

public readonly record struct EconomyDropOffRuntimeEntry(
    EconomyDropOffId Id,
    int PlayerId,
    Vector2 Position,
    Vector2 HalfExtents,
    float ArrivalRadius,
    bool AcceptsMinerals,
    bool AcceptsVespene,
    bool Operational);

public readonly record struct EconomyBaseRuntimeEntry(
    EconomyBaseId Id,
    int PlayerId,
    GameplayBuildingId TownHall,
    EconomyDropOffId DropOff,
    Vector2 Position,
    bool Operational);

public readonly record struct WorkerEconomyRuntimeEntry(
    int UnitId,
    bool Registered,
    int PlayerId,
    GathererCapability Capability,
    WorkerEconomyState State,
    int TargetNodeId,
    EconomyResourceKind CargoKind,
    int CargoAmount,
    float WorkRemaining);

public sealed record EconomyRuntimeSnapshot(
    PlayerEconomyRuntimeEntry[] Players,
    EconomyResourceNodeRuntimeEntry[] ResourceNodes,
    EconomyDropOffRuntimeEntry[] DropOffs,
    EconomyBaseRuntimeEntry[] Bases,
    WorkerEconomyRuntimeEntry[] Workers);

public readonly record struct EconomyBaseSnapshot(
    EconomyBaseId Id,
    int PlayerId,
    GameplayBuildingId TownHall,
    EconomyDropOffId DropOff,
    Vector2 Position,
    bool Operational,
    int MineralNodes,
    int VespeneNodes,
    int AssignedMineralWorkers,
    int IdealMineralWorkers,
    int AssignedVespeneWorkers,
    int IdealVespeneWorkers)
{
    public EconomyBaseSnapshot(
        EconomyBaseId id,
        int playerId,
        GameplayBuildingId townHall,
        EconomyDropOffId dropOff,
        Vector2 position,
        bool operational,
        int mineralNodes,
        int vespeneNodes,
        int assignedWorkers,
        int idealWorkers)
        : this(
            id, playerId, townHall, dropOff, position, operational,
            mineralNodes, vespeneNodes,
            assignedWorkers, idealWorkers, 0, 0)
    {
    }

    public int AssignedWorkers =>
        AssignedMineralWorkers + AssignedVespeneWorkers;
    public int IdealWorkers => IdealMineralWorkers + IdealVespeneWorkers;
    public float Saturation => IdealWorkers == 0
        ? 0f
        : AssignedWorkers / (float)IdealWorkers;
}

public readonly record struct EconomyDropOffApproachSnapshot(
    EconomyDropOffId Id,
    Vector2 Center,
    Vector2 HalfExtents,
    Vector2 InteractionHalfExtents,
    float InteractionRadius,
    Vector2 Target,
    float DistanceSquared)
{
    public bool Found => Id.Value >= 0;
}

public enum WorkerTransferCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidSourceBase,
    InvalidTargetBase,
    SameBase,
    InvalidCount,
    NoTargetResources,
    NoEligibleWorkers,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public readonly record struct WorkerTransferCommandResult(
    WorkerTransferCommandCode Code,
    EconomyBaseId SourceBase,
    EconomyBaseId TargetBase,
    int RequestedWorkers,
    int TransferredWorkers)
{
    public bool Succeeded => Code == WorkerTransferCommandCode.Success;
}

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
    MissingDropOff,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant,
    CapabilityUnavailable
}

public readonly record struct GatherCommandResult(
    GatherCommandCode Code,
    int UnitId,
    EconomyResourceNodeId NodeId)
{
    public bool Succeeded => Code == GatherCommandCode.Success;
}

public enum ReturnCargoCommandCode : byte
{
    Success,
    InvalidUnit,
    UnitNotWorker,
    WrongOwner,
    NoCargo,
    MissingDropOff,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public readonly record struct ReturnCargoCommandResult(
    ReturnCargoCommandCode Code,
    int UnitId)
{
    public bool Succeeded => Code == ReturnCargoCommandCode.Success;
}

public sealed class EconomySystem
{
    private const float NodeArrivalPadding = 30f;
    private const float DefaultDropOffArrivalRadius = 52f;
    private const float BaseResourceRadius = 360f;
    private readonly bool[] _workers;
    private readonly int[] _workerPlayers;
    private readonly GathererCapability[] _gathererCapabilities;
    private readonly WorkerEconomyState[] _workerStates;
    private readonly int[] _workerNodes;
    private readonly EconomyResourceKind[] _cargoKinds;
    private readonly int[] _cargoAmounts;
    private readonly float[] _workRemaining;
    private readonly List<ResourceNode> _nodes = [];
    private readonly List<DropOff> _dropOffs = [];
    private readonly List<EconomyBase> _bases = [];
    private int _registeredWorkerCount;

    internal Func<int, EconomyResourceKind, Vector2, float,
        EconomyDropOffApproachSnapshot>? ReachableDropOffResolver { get; set; }

    public EconomySystem(int unitCapacity)
    {
        Players = new PlayerEconomyStore();
        _workers = new bool[unitCapacity];
        _workerPlayers = new int[unitCapacity];
        _gathererCapabilities = new GathererCapability[unitCapacity];
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
    public int BaseCount => _bases.Count(value => value.Operational);
    public bool HasRuntimeState =>
        Players.HasRegisteredPlayers || _nodes.Count > 0 || _dropOffs.Count > 0 ||
        _workers.Any(value => value);

    public bool IsWorker(int unit) =>
        IsGatherer(unit) &&
        _gathererCapabilities[unit] == GathererCapability.NormalWorker;

    public bool IsGatherer(int unit) =>
        (uint)unit < (uint)_workers.Length && _workers[unit];

    public bool IsWorkerOwnedBy(int unit, int playerId) =>
        IsWorker(unit) && _workerPlayers[unit] == playerId;

    public bool IsGathererOwnedBy(int unit, int playerId) =>
        IsGatherer(unit) && _workerPlayers[unit] == playerId;

    public GathererCapability GathererCapabilityOf(int unit) =>
        IsGatherer(unit)
            ? _gathererCapabilities[unit]
            : GathererCapability.None;

    public bool SuppressesUnitCollision(int unit) =>
        IsGatherer(unit) && WorkerCollisionPolicy.SuppressesUnitCollision(
            _workerStates[unit]);

    public bool IsVespeneNode(EconomyResourceNodeId id) =>
        (uint)id.Value < (uint)_nodes.Count &&
        _nodes[id.Value].Kind == EconomyResourceKind.VespeneGas &&
        _nodes[id.Value].RequiresRefinery;

    public Vector2 ResourceNodePosition(EconomyResourceNodeId id) => Node(id).Position;

    public bool IsDiscClearOfResources(
        Vector2 center,
        float radius,
        EconomyResourceNodeId ignoredNode)
    {
        var clearance = NodeArrivalPadding + radius;
        var clearanceSquared = clearance * clearance;
        for (var index = 0; index < _nodes.Count; index++)
        {
            if (index == ignoredNode.Value || _nodes[index].Remaining <= 0)
                continue;
            if (Vector2.DistanceSquared(center, _nodes[index].Position) <
                clearanceSquared)
                return false;
        }
        return true;
    }

    public EconomyResourceNodeId AddResourceNode(
        EconomyResourceKind kind,
        Vector2 position,
        int amount,
        int harvestBatch,
        float harvestSeconds,
        int harvesterCapacity,
        bool requiresRefinery = false,
        bool operational = true,
        int activeHarvesterSlots = 0,
        EconomyHarvestMode harvestMode = EconomyHarvestMode.Batch)
    {
        if (!Enum.IsDefined(kind) || !IsFinite(position) || amount <= 0 ||
            harvestBatch <= 0 || !float.IsFinite(harvestSeconds) ||
            harvestSeconds <= 0f || harvesterCapacity <= 0 ||
            activeHarvesterSlots < 0 || !Enum.IsDefined(harvestMode))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
        var id = new EconomyResourceNodeId(_nodes.Count);
        var resolvedActiveSlots = activeHarvesterSlots > 0
            ? activeHarvesterSlots
            : kind == EconomyResourceKind.Minerals
                ? 1
                : harvesterCapacity;
        _nodes.Add(new ResourceNode(
            id, kind, position, amount, harvestBatch, harvestSeconds,
            resolvedActiveSlots, harvesterCapacity,
            requiresRefinery, operational, harvestMode));
        return id;
    }

    /// <summary>
    /// Gives a resource node a physical interaction footprint. Movement can
    /// then stop at the resource boundary instead of walking through its
    /// visual center.
    /// </summary>
    public void SetResourceInteractionBounds(
        EconomyResourceNodeId nodeId,
        SimRect bounds)
    {
        var node = Node(nodeId);
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var halfExtents = (bounds.Max - bounds.Min) * 0.5f;
        if (!IsFinite(center) || !IsFinite(halfExtents) ||
            halfExtents.X <= 0f || halfExtents.Y <= 0f ||
            Vector2.DistanceSquared(center, node.Position) > 0.0001f)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }
        node.InteractionHalfExtents = halfExtents;
    }

    public EconomyDropOffId AddDropOff(
        int playerId,
        Vector2 position,
        bool acceptsMinerals = true,
        bool acceptsVespene = true,
        float arrivalRadius = DefaultDropOffArrivalRadius,
        Vector2 halfExtents = default)
    {
        if (!Players.IsRegistered(playerId) || !IsFinite(position) ||
            !acceptsMinerals && !acceptsVespene ||
            !float.IsFinite(arrivalRadius) || arrivalRadius <= 0f ||
            !IsFinite(halfExtents) || halfExtents.X < 0f || halfExtents.Y < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
        var id = new EconomyDropOffId(_dropOffs.Count);
        _dropOffs.Add(new DropOff(
            id, playerId, position, halfExtents, arrivalRadius,
            acceptsMinerals, acceptsVespene, true));
        return id;
    }

    public void SetDropOffOperational(EconomyDropOffId id, bool value)
    {
        if ((uint)id.Value >= (uint)_dropOffs.Count)
            throw new ArgumentOutOfRangeException(nameof(id));
        _dropOffs[id.Value].Operational = value;
    }

    public EconomyBaseId RegisterTownHall(
        int playerId,
        GameplayBuildingId townHall,
        SimRect bounds)
    {
        var position = (bounds.Min + bounds.Max) * 0.5f;
        var halfExtents = (bounds.Max - bounds.Min) * 0.5f;
        if (!Players.IsRegistered(playerId) || townHall.Value < 0 ||
            !IsFinite(position) || !IsFinite(halfExtents) ||
            halfExtents.X <= 0f || halfExtents.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
        for (var index = 0; index < _bases.Count; index++)
        {
            if (_bases[index].TownHall != townHall)
            {
                continue;
            }
            if (_bases[index].PlayerId != playerId)
            {
                throw new InvalidOperationException("Town Hall owner changed.");
            }
            _bases[index].Operational = true;
            _dropOffs[_bases[index].DropOff.Value].Operational = true;
            return _bases[index].Id;
        }
        var dropOff = AddDropOff(
            playerId, position, arrivalRadius: 4f,
            halfExtents: halfExtents);
        var id = new EconomyBaseId(_bases.Count);
        _bases.Add(new EconomyBase(
            id, playerId, townHall, dropOff, position, true));
        return id;
    }

    public void SetTownHallOperational(GameplayBuildingId townHall, bool value)
    {
        for (var index = 0; index < _bases.Count; index++)
        {
            var economyBase = _bases[index];
            if (economyBase.TownHall != townHall)
            {
                continue;
            }
            economyBase.Operational = value;
            _dropOffs[economyBase.DropOff.Value].Operational = value;
            return;
        }
    }

    public EconomyBaseSnapshot[] CreateBaseOverview(int playerId, int unitCount)
    {
        if (!Players.IsRegistered(playerId) || unitCount < 0 ||
            unitCount > _workers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
        var result = new List<EconomyBaseSnapshot>();
        for (var baseIndex = 0; baseIndex < _bases.Count; baseIndex++)
        {
            var economyBase = _bases[baseIndex];
            if (economyBase.PlayerId != playerId)
            {
                continue;
            }
            var minerals = 0;
            var vespene = 0;
            var idealMinerals = 0;
            var idealVespene = 0;
            for (var node = 0; node < _nodes.Count; node++)
            {
                if (OwningBase(node) != baseIndex)
                {
                    continue;
                }
                if (_nodes[node].Kind == EconomyResourceKind.Minerals)
                {
                    minerals++;
                    idealMinerals += _nodes[node].IdealNormalAssignments;
                }
                else
                {
                    vespene++;
                    idealVespene += _nodes[node].IdealNormalAssignments;
                }
            }
            var assignedMinerals = 0;
            var assignedVespene = 0;
            for (var unit = 0; unit < unitCount; unit++)
            {
                if (IsWorker(unit) && _workerPlayers[unit] == playerId &&
                    OwningBase(_workerNodes[unit]) == baseIndex)
                {
                    if (_nodes[_workerNodes[unit]].Kind ==
                        EconomyResourceKind.Minerals)
                        assignedMinerals++;
                    else
                        assignedVespene++;
                }
            }
            result.Add(new EconomyBaseSnapshot(
                economyBase.Id, economyBase.PlayerId, economyBase.TownHall,
                economyBase.DropOff, economyBase.Position,
                economyBase.Operational, minerals, vespene,
                assignedMinerals, idealMinerals,
                assignedVespene, idealVespene));
        }
        return result.ToArray();
    }

    public WorkerTransferCommandResult TransferWorkers(
        int playerId,
        EconomyBaseId sourceId,
        EconomyBaseId targetId,
        int count,
        UnitStore units,
        Action<int, Vector2> moveWorker)
    {
        if (!Players.IsRegistered(playerId))
            return TransferFailure(WorkerTransferCommandCode.InvalidPlayer);
        if (!TryOwnedOperationalBase(sourceId, playerId, out _))
            return TransferFailure(WorkerTransferCommandCode.InvalidSourceBase);
        if (!TryOwnedOperationalBase(targetId, playerId, out var target))
            return TransferFailure(WorkerTransferCommandCode.InvalidTargetBase);
        if (sourceId == targetId)
            return TransferFailure(WorkerTransferCommandCode.SameBase);
        if (count <= 0)
            return TransferFailure(WorkerTransferCommandCode.InvalidCount);

        var targetNodes = Enumerable.Range(0, _nodes.Count)
            .Where(node => OwningBase(node) == targetId.Value &&
                           IsNodeAvailable(node))
            .OrderBy(node => _nodes[node].Kind == EconomyResourceKind.Minerals ? 0 : 1)
            .ThenBy(node => node)
            .ToArray();
        if (targetNodes.Length == 0)
            return TransferFailure(WorkerTransferCommandCode.NoTargetResources);

        var candidates = Enumerable.Range(0, units.Count)
            .Where(unit => units.Alive[unit] && IsWorker(unit) &&
                           _workerPlayers[unit] == playerId &&
                           OwningBase(_workerNodes[unit]) == sourceId.Value)
            .OrderBy(unit => Vector2.DistanceSquared(
                units.Positions[unit], target.Position))
            .ThenBy(unit => unit)
            .Take(count)
            .ToArray();
        if (candidates.Length == 0)
            return TransferFailure(WorkerTransferCommandCode.NoEligibleWorkers);

        var assignments = AssignGatherTargets(
            playerId, candidates,
            new EconomyResourceNodeId(targetNodes[0]), units,
            distributeSingle: true);
        for (var index = 0; index < candidates.Length; index++)
        {
            RetargetTransferredWorker(
                candidates[index], assignments[index].Value,
                units.Positions[candidates[index]], units.Radii[candidates[index]],
                moveWorker);
        }
        return new WorkerTransferCommandResult(
            WorkerTransferCommandCode.Success, sourceId, targetId,
            count, candidates.Length);

        WorkerTransferCommandResult TransferFailure(WorkerTransferCommandCode code) =>
            new(code, sourceId, targetId, count, 0);
    }

    public void RegisterWorker(int unit, int playerId)
    {
        RegisterGatherer(unit, playerId, GathererCapability.NormalWorker);
    }

    public void RegisterGatherer(
        int unit,
        int playerId,
        GathererCapability capability)
    {
        ValidateUnit(unit);
        if (!Players.IsRegistered(playerId) ||
            capability == GathererCapability.None ||
            !Enum.IsDefined(capability))
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
        if (!_workers[unit])
        {
            _registeredWorkerCount++;
        }
        else
        {
            TransitionWorker(unit, WorkerEconomyState.Idle, -1);
        }
        _workers[unit] = true;
        _workerPlayers[unit] = playerId;
        _gathererCapabilities[unit] = capability;
        _workerStates[unit] = WorkerEconomyState.Idle;
        _workerNodes[unit] = -1;
    }

    public void SetRefineryOperational(
        EconomyResourceNodeId nodeId,
        bool value,
        SimRect? refineryBounds = null)
    {
        var node = Node(nodeId);
        if (!node.RequiresRefinery)
        {
            throw new InvalidOperationException(
                "Only a refinery resource node can change operational state.");
        }
        node.Operational = value;
        if (refineryBounds.HasValue)
        {
            var bounds = refineryBounds.Value;
            node.InteractionHalfExtents = (bounds.Max - bounds.Min) * 0.5f;
        }
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
        if (_gathererCapabilities[unit] == GathererCapability.Mule &&
            node.Kind != EconomyResourceKind.Minerals)
        {
            return Failure(
                GatherCommandCode.CapabilityUnavailable, unit, nodeId);
        }
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

    public EconomyResourceNodeId[] AssignGatherTargets(
        int playerId,
        ReadOnlySpan<int> workers,
        EconomyResourceNodeId preferredNode,
        UnitStore units,
        bool distributeSingle = false)
    {
        if ((uint)preferredNode.Value >= (uint)_nodes.Count ||
            workers.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(preferredNode));
        var copiedWorkers = workers.ToArray();
        var orderedIndices = Enumerable.Range(0, copiedWorkers.Length)
            .OrderBy(index => copiedWorkers[index])
            .ToArray();
        var result = new EconomyResourceNodeId[copiedWorkers.Length];
        if (copiedWorkers.Length == 1 && !distributeSingle)
        {
            result[0] = preferredNode;
            return result;
        }

        var plannedNormal = _nodes
            .Select(node => node.AssignedNormal)
            .ToArray();
        var plannedMules = _nodes
            .Select(node => node.AssignedMules)
            .ToArray();
        for (var index = 0; index < copiedWorkers.Length; index++)
        {
            var unit = copiedWorkers[index];
            if (!IsGathererOwnedBy(unit, playerId) ||
                (uint)unit >= (uint)units.Count || !units.Alive[unit])
                throw new ArgumentOutOfRangeException(nameof(workers));
            var current = _workerNodes[unit];
            if ((uint)current < (uint)plannedNormal.Length &&
                IsAssignedState(_workerStates[unit]))
            {
                if (_gathererCapabilities[unit] == GathererCapability.Mule)
                    plannedMules[current]--;
                else
                    plannedNormal[current]--;
            }
        }

        foreach (var inputIndex in orderedIndices)
        {
            var unit = copiedWorkers[inputIndex];
            var target = SelectAssignedNode(
                playerId,
                preferredNode.Value,
                units.Positions[unit],
                _gathererCapabilities[unit],
                plannedNormal,
                plannedMules);
            if (target < 0)
                target = preferredNode.Value;
            result[inputIndex] = new EconomyResourceNodeId(target);
            if (_gathererCapabilities[unit] == GathererCapability.Mule)
                plannedMules[target]++;
            else
                plannedNormal[target]++;
        }
        return result;
    }

    public ReturnCargoCommandResult ValidateReturnCargo(
        int issuingPlayerId,
        int unit)
    {
        if ((uint)unit >= (uint)_workers.Length)
            return ReturnCargoFailure(ReturnCargoCommandCode.InvalidUnit, unit);
        if (!_workers[unit])
            return ReturnCargoFailure(ReturnCargoCommandCode.UnitNotWorker, unit);
        if (_workerPlayers[unit] != issuingPlayerId)
            return ReturnCargoFailure(ReturnCargoCommandCode.WrongOwner, unit);
        if (_cargoAmounts[unit] <= 0)
            return ReturnCargoFailure(ReturnCargoCommandCode.NoCargo, unit);
        if (FindNearestDropOff(
                issuingPlayerId, _cargoKinds[unit], Vector2.Zero) < 0)
            return ReturnCargoFailure(ReturnCargoCommandCode.MissingDropOff, unit);
        return new ReturnCargoCommandResult(
            ReturnCargoCommandCode.Success, unit);
    }

    public void BeginGather(
        int unit,
        EconomyResourceNodeId nodeId,
        Vector2 position,
        float unitRadius,
        Action<int, Vector2> moveWorker)
    {
        _workRemaining[unit] = 0f;
        if (_cargoAmounts[unit] > 0)
        {
            TransitionWorker(
                unit, WorkerEconomyState.ReturningCargo, nodeId.Value);
            BeginReturningCargo(unit, position, unitRadius, moveWorker);
            return;
        }
        TransitionWorker(
            unit, WorkerEconomyState.GoingToResource, nodeId.Value);
        moveWorker(unit, _nodes[nodeId.Value].Position);
    }

    public void BeginReturnCargo(
        int unit,
        Vector2 position,
        float unitRadius,
        Action<int, Vector2> moveWorker)
    {
        _workRemaining[unit] = 0f;
        BeginReturningCargo(unit, position, unitRadius, moveWorker);
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
            TransitionWorker(unit, WorkerEconomyState.Idle, -1);
            _workRemaining[unit] = 0f;
        }
    }

    public void Update(
        float delta,
        long tick,
        GameplayEventStream events,
        UnitStore units,
        Action<int, Vector2> moveWorker,
        Action<int> stopWorker,
        Action<int, UnitMovementLegResult> finishDropOffMovement)
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
                TransitionWorker(unit, WorkerEconomyState.Idle, -1);
                continue;
            }
            if (units.MovementGoalKinds[unit] ==
                    UnitMovementGoalKind.ProductionExit &&
                units.MovementLegResults[unit] ==
                    UnitMovementLegResult.InProgress)
                continue;
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
                    UpdateGathering(
                        unit, delta, tick, events, units, moveWorker,
                        finishDropOffMovement);
                    break;
                case WorkerEconomyState.ReturningCargo:
                    UpdateReturning(
                        unit, tick, events, units, moveWorker, stopWorker,
                        finishDropOffMovement);
                    break;
                case WorkerEconomyState.WaitingForDropOff:
                    UpdateWaitingForDropOff(unit, units, moveWorker);
                    break;
            }
        }
    }

    public EconomyResourceNodeSnapshot ObserveResourceNode(EconomyResourceNodeId id)
    {
        var node = Node(id);
        return new EconomyResourceNodeSnapshot(
            node.Id, node.Kind, node.Position, node.Remaining,
            node.ActiveNormal, node.AssignedNormal, node.WaitingNormal,
            node.NormalActiveSlots, node.IdealNormalAssignments,
            node.ActiveMules, node.AssignedMules,
            node.RequiresRefinery, node.Operational,
            node.InteractionHalfExtents, NodeArrivalPadding,
            node.HarvestMode);
    }

    public EconomyDropOffApproachSnapshot[] CreateDropOffApproaches(
        int playerId,
        EconomyResourceKind kind,
        Vector2 origin,
        float unitRadius)
    {
        if (!Players.IsRegistered(playerId) || !Enum.IsDefined(kind) ||
            !IsFinite(origin) || !float.IsFinite(unitRadius) || unitRadius < 0f)
            throw new ArgumentOutOfRangeException(nameof(playerId));
        var result = new List<EconomyDropOffApproachSnapshot>();
        for (var index = 0; index < _dropOffs.Count; index++)
        {
            var dropOff = _dropOffs[index];
            if (!dropOff.Operational || dropOff.PlayerId != playerId ||
                !dropOff.Accepts(kind))
                continue;
            var target = dropOff.ApproachPoint(origin, unitRadius);
            result.Add(new EconomyDropOffApproachSnapshot(
                dropOff.Id,
                dropOff.Position,
                dropOff.HalfExtents,
                dropOff.HalfExtents + new Vector2(unitRadius),
                dropOff.ArrivalRadius,
                target,
                Vector2.DistanceSquared(origin, target)));
        }
        return result.ToArray();
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
            _workRemaining[unit],
            GathererCapabilityOf(unit));
    }

    public EconomyDropOffApproachSnapshot PreviewDropOffApproach(
        int playerId,
        EconomyResourceKind kind,
        Vector2 origin,
        float unitRadius)
    {
        if (!Players.IsRegistered(playerId) || !Enum.IsDefined(kind) ||
            !IsFinite(origin) || !float.IsFinite(unitRadius) || unitRadius < 0f)
            throw new ArgumentOutOfRangeException(nameof(playerId));
        var clearance = unitRadius;
        var index = FindNearestDropOff(playerId, kind, origin, clearance);
        if (index < 0)
            return new EconomyDropOffApproachSnapshot(
                new EconomyDropOffId(-1), default, default, default, 0f, default,
                float.PositiveInfinity);
        var dropOff = _dropOffs[index];
        var target = dropOff.ApproachPoint(origin, clearance);
        return new EconomyDropOffApproachSnapshot(
            dropOff.Id,
            dropOff.Position,
            dropOff.HalfExtents,
            dropOff.HalfExtents + new Vector2(
                unitRadius),
            dropOff.ArrivalRadius,
            target,
            Vector2.DistanceSquared(origin, target));
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
            if (!IsWorker(unit) || _workerPlayers[unit] != playerId)
            {
                continue;
            }
            workers++;
            gathering += _workerStates[unit] == WorkerEconomyState.Gathering ? 1 : 0;
            returning += _workerStates[unit] == WorkerEconomyState.ReturningCargo ? 1 : 0;
            waiting += _workerStates[unit] is
                WorkerEconomyState.WaitingForResource or
                WorkerEconomyState.WaitingForDropOff ? 1 : 0;
        }
        return new EconomyOverviewSnapshot(
            Players.Snapshot(playerId),
            nodes,
            workers,
            gathering,
            returning,
            waiting);
    }

    public bool CanStartReplayRecording(int unitCount)
    {
        if (unitCount < 0 || unitCount > _workers.Length)
        {
            return false;
        }
        for (var unit = 0; unit < unitCount; unit++)
        {
            if (_workers[unit] &&
                (_workerStates[unit] != WorkerEconomyState.Idle ||
                 _cargoAmounts[unit] != 0 || _workRemaining[unit] != 0f))
            {
                return false;
            }
        }
        return _nodes.All(node =>
            node.ActiveNormal == 0 && node.AssignedNormal == 0 &&
            node.WaitingNormal == 0 && node.ActiveMules == 0 &&
            node.AssignedMules == 0);
    }

    public EconomyRuntimeSnapshot CaptureRuntimeState(int unitCount)
    {
        if (unitCount < 0 || unitCount > _workers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCount));
        }
        var nodes = _nodes.Select(node => new EconomyResourceNodeRuntimeEntry(
            node.Id, node.Kind, node.Position, node.Remaining,
            node.HarvestBatch, node.HarvestSeconds,
            node.NormalActiveSlots, node.IdealNormalAssignments,
            node.RequiresRefinery, node.Operational,
            node.ActiveNormal, node.AssignedNormal, node.WaitingNormal,
            node.ActiveMules, node.AssignedMules,
            node.InteractionHalfExtents, node.HarvestMode)).ToArray();
        var dropOffs = _dropOffs.Select(dropOff => new EconomyDropOffRuntimeEntry(
            dropOff.Id, dropOff.PlayerId, dropOff.Position,
            dropOff.HalfExtents,
            dropOff.ArrivalRadius,
            dropOff.AcceptsMinerals, dropOff.AcceptsVespene,
            dropOff.Operational)).ToArray();
        var bases = _bases.Select(value => new EconomyBaseRuntimeEntry(
            value.Id, value.PlayerId, value.TownHall, value.DropOff,
            value.Position, value.Operational)).ToArray();
        var workers = new WorkerEconomyRuntimeEntry[unitCount];
        for (var unit = 0; unit < unitCount; unit++)
        {
            workers[unit] = new WorkerEconomyRuntimeEntry(
                unit, _workers[unit], _workerPlayers[unit],
                _gathererCapabilities[unit],
                _workerStates[unit], _workerNodes[unit], _cargoKinds[unit],
                _cargoAmounts[unit], _workRemaining[unit]);
        }
        return new EconomyRuntimeSnapshot(
            Players.CaptureRuntimeState(), nodes, dropOffs, bases, workers);
    }

    public void RestoreRuntimeState(EconomyRuntimeSnapshot snapshot, int unitCount)
    {
        if (snapshot.Workers.Length != unitCount || unitCount > _workers.Length)
        {
            throw new InvalidOperationException("Economy worker capacity mismatch.");
        }
        Players.RestoreRuntimeState(snapshot.Players);
        _nodes.Clear();
        for (var index = 0; index < snapshot.ResourceNodes.Length; index++)
        {
            var value = snapshot.ResourceNodes[index];
            if (value.Id.Value != index)
            {
                throw new InvalidOperationException("Resource node IDs must be dense.");
            }
            _nodes.Add(new ResourceNode(
                value.Id, value.Kind, value.Position, value.Remaining,
                value.HarvestBatch, value.HarvestSeconds,
                value.NormalActiveSlots, value.IdealNormalAssignments,
                value.RequiresRefinery,
                value.Operational,
                value.HarvestMode)
            {
                ActiveNormal = value.ActiveNormal,
                AssignedNormal = value.AssignedNormal,
                WaitingNormal = value.WaitingNormal,
                ActiveMules = value.ActiveMules,
                AssignedMules = value.AssignedMules,
                InteractionHalfExtents = value.InteractionHalfExtents
            });
        }
        _dropOffs.Clear();
        for (var index = 0; index < snapshot.DropOffs.Length; index++)
        {
            var value = snapshot.DropOffs[index];
            if (value.Id.Value != index)
            {
                throw new InvalidOperationException("DropOff IDs must be dense.");
            }
            _dropOffs.Add(new DropOff(
                value.Id, value.PlayerId, value.Position,
                value.HalfExtents,
                value.ArrivalRadius,
                value.AcceptsMinerals, value.AcceptsVespene,
                value.Operational));
        }
        _bases.Clear();
        for (var index = 0; index < snapshot.Bases.Length; index++)
        {
            var value = snapshot.Bases[index];
            if (value.Id.Value != index || value.TownHall.Value < 0 ||
                (uint)value.DropOff.Value >= (uint)_dropOffs.Count ||
                _dropOffs[value.DropOff.Value].PlayerId != value.PlayerId ||
                _dropOffs[value.DropOff.Value].Operational != value.Operational ||
                !Players.IsRegistered(value.PlayerId) ||
                _bases.Any(existing => existing.TownHall == value.TownHall ||
                    existing.DropOff == value.DropOff) ||
                !IsFinite(value.Position))
            {
                throw new InvalidOperationException("Economy base entry is invalid.");
            }
            _bases.Add(new EconomyBase(
                value.Id, value.PlayerId, value.TownHall, value.DropOff,
                value.Position, value.Operational));
        }
        Array.Clear(_workers);
        Array.Clear(_workerPlayers);
        Array.Clear(_gathererCapabilities);
        Array.Clear(_workerStates);
        Array.Fill(_workerNodes, -1);
        Array.Clear(_cargoKinds);
        Array.Clear(_cargoAmounts);
        Array.Clear(_workRemaining);
        _registeredWorkerCount = 0;
        for (var unit = 0; unit < snapshot.Workers.Length; unit++)
        {
            var value = snapshot.Workers[unit];
            if (value.UnitId != unit)
            {
                throw new InvalidOperationException("Worker IDs must be dense.");
            }
            _workers[unit] = value.Registered;
            _workerPlayers[unit] = value.PlayerId;
            _gathererCapabilities[unit] = value.Capability;
            _workerStates[unit] = value.State;
            _workerNodes[unit] = value.TargetNodeId;
            _cargoKinds[unit] = value.CargoKind;
            _cargoAmounts[unit] = value.CargoAmount;
            _workRemaining[unit] = value.WorkRemaining;
            _registeredWorkerCount += value.Registered ? 1 : 0;
        }
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
            hash.Add(node.NormalActiveSlots);
            hash.Add(node.IdealNormalAssignments);
            hash.Add(node.ActiveNormal);
            hash.Add(node.AssignedNormal);
            hash.Add(node.WaitingNormal);
            hash.Add(node.ActiveMules);
            hash.Add(node.AssignedMules);
            hash.Add(node.RequiresRefinery);
            hash.Add(node.Operational);
            hash.Add(node.InteractionHalfExtents);
            hash.Add((byte)node.HarvestMode);
        }
        hash.Add(_dropOffs.Count);
        for (var index = 0; index < _dropOffs.Count; index++)
        {
            var dropOff = _dropOffs[index];
            hash.Add(dropOff.Id.Value);
            hash.Add(dropOff.PlayerId);
            hash.Add(dropOff.Position);
            hash.Add(dropOff.HalfExtents);
            hash.Add(dropOff.ArrivalRadius);
            hash.Add(dropOff.AcceptsMinerals);
            hash.Add(dropOff.AcceptsVespene);
            hash.Add(dropOff.Operational);
        }
        hash.Add(_bases.Count);
        for (var index = 0; index < _bases.Count; index++)
        {
            var value = _bases[index];
            hash.Add(value.Id.Value);
            hash.Add(value.PlayerId);
            hash.Add(value.TownHall.Value);
            hash.Add(value.DropOff.Value);
            hash.Add(value.Position);
            hash.Add(value.Operational);
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
            hash.Add((byte)_gathererCapabilities[unit]);
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
        if (NodeHasArrived(node, units.Positions[unit], units.Radii[unit]))
        {
            TryStartGathering(
                unit, units, moveWorker, stopWorker,
                allowReassignment: true);
            return;
        }
        if (units.MovementLegResults[unit] is
                UnitMovementLegResult.Unreachable or
                UnitMovementLegResult.SettledShort)
            TransitionWorker(unit, WorkerEconomyState.Idle, -1);
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
        var node = _nodes[_workerNodes[unit]];
        if (!NodeHasArrived(node, units.Positions[unit], units.Radii[unit]))
        {
            TransitionWorker(
                unit, WorkerEconomyState.GoingToResource, node.Id.Value);
            moveWorker(unit, node.Position);
            return;
        }
        TryStartGathering(
            unit, units, moveWorker, stopWorker,
            allowReassignment: false);
    }

    private void TryStartGathering(
        int unit,
        UnitStore units,
        Action<int, Vector2> moveWorker,
        Action<int> stopWorker,
        bool allowReassignment)
    {
        var nodeIndex = _workerNodes[unit];
        if (!IsNodeAvailable(nodeIndex))
        {
            TransitionWorker(unit, WorkerEconomyState.Idle, -1);
            return;
        }
        var node = _nodes[nodeIndex];
        if (!NodeHasArrived(node, units.Positions[unit], units.Radii[unit]))
        {
            TransitionWorker(
                unit, WorkerEconomyState.GoingToResource, nodeIndex);
            moveWorker(unit, node.Position);
            return;
        }
        var capability = _gathererCapabilities[unit];
        var active = capability == GathererCapability.Mule
            ? node.ActiveMules
            : node.ActiveNormal;
        var activeSlots = capability == GathererCapability.Mule
            ? 1
            : node.NormalActiveSlots;
        if (active >= activeSlots)
        {
            if (allowReassignment)
            {
                var assignedNormal = _nodes
                    .Select(value => value.AssignedNormal)
                    .ToArray();
                var assignedMules = _nodes
                    .Select(value => value.AssignedMules)
                    .ToArray();
                if (capability == GathererCapability.Mule)
                    assignedMules[nodeIndex]--;
                else
                    assignedNormal[nodeIndex]--;
                var replacement = SelectAssignedNode(
                    _workerPlayers[unit], nodeIndex,
                    units.Positions[unit], capability,
                    assignedNormal, assignedMules);
                if (replacement >= 0 && replacement != nodeIndex)
                {
                    TransitionWorker(
                        unit, WorkerEconomyState.GoingToResource, replacement);
                    moveWorker(unit, _nodes[replacement].Position);
                    return;
                }
            }
            TransitionWorker(
                unit, WorkerEconomyState.WaitingForResource, nodeIndex);
            return;
        }
        TransitionWorker(unit, WorkerEconomyState.Gathering, nodeIndex);
        _workRemaining[unit] = node.HarvestSeconds;
        stopWorker(unit);
    }

    private void UpdateGathering(
        int unit,
        float delta,
        long tick,
        GameplayEventStream events,
        UnitStore units,
        Action<int, Vector2> moveWorker,
        Action<int, UnitMovementLegResult> finishDropOffMovement)
    {
        var nodeIndex = _workerNodes[unit];
        if (!IsNodeAvailable(nodeIndex))
        {
            RetargetOrIdle(unit, units.Positions[unit], moveWorker);
            return;
        }
        var node = _nodes[nodeIndex];
        if (!NodeHasArrived(node, units.Positions[unit], units.Radii[unit]))
        {
            _workRemaining[unit] = 0f;
            TransitionWorker(
                unit, WorkerEconomyState.GoingToResource, nodeIndex);
            moveWorker(unit, node.Position);
            return;
        }
        _workRemaining[unit] = MathF.Max(0f, _workRemaining[unit] - delta);
        int amount;
        if (node.HarvestMode == EconomyHarvestMode.Progressive)
        {
            var targetCargo = Math.Min(
                node.HarvestBatch,
                (int)MathF.Floor(
                    (1f - _workRemaining[unit] / node.HarvestSeconds) *
                    node.HarvestBatch + 0.0001f));
            var gained = Math.Min(
                Math.Max(0, targetCargo - _cargoAmounts[unit]),
                node.Remaining);
            if (gained > 0)
            {
                node.Remaining -= gained;
                _cargoKinds[unit] = node.Kind;
                _cargoAmounts[unit] += gained;
            }
            if (_workRemaining[unit] > 0f &&
                _cargoAmounts[unit] < node.HarvestBatch && node.Remaining > 0)
                return;
            amount = _cargoAmounts[unit];
        }
        else
        {
            if (_workRemaining[unit] > 0f) return;
            amount = Math.Min(node.HarvestBatch, node.Remaining);
            node.Remaining -= amount;
            if (amount > 0)
            {
                _cargoKinds[unit] = node.Kind;
                _cargoAmounts[unit] = amount;
            }
        }
        _workRemaining[unit] = 0f;
        if (amount <= 0)
        {
            RetargetOrIdle(unit, units.Positions[unit], moveWorker);
            return;
        }
        events.Publish(
            tick,
            GameplayEventKind.HarvestCompleted,
            unit,
            resourceNode: node.Id.Value,
            resourceKind: node.Kind,
            amount: amount,
            worldPosition: units.Positions[unit]);
        var approach = ResolveDropOffApproach(
            _workerPlayers[unit], node.Kind,
            units.Positions[unit], units.Radii[unit]);
        if (!approach.Found)
        {
            TransitionWorker(
                unit, WorkerEconomyState.WaitingForDropOff,
                _workerNodes[unit]);
            finishDropOffMovement(
                unit, UnitMovementLegResult.TargetInvalidated);
            return;
        }
        TransitionWorker(
            unit, WorkerEconomyState.ReturningCargo, _workerNodes[unit]);
        moveWorker(unit, approach.Target);
    }

    private void UpdateReturning(
        int unit,
        long tick,
        GameplayEventStream events,
        UnitStore units,
        Action<int, Vector2> moveWorker,
        Action<int> stopWorker,
        Action<int, UnitMovementLegResult> finishDropOffMovement)
    {
        var approach = ResolveDropOffApproach(
            _workerPlayers[unit], _cargoKinds[unit],
            units.Positions[unit], units.Radii[unit]);
        if (!approach.Found)
        {
            TransitionWorker(
                unit, WorkerEconomyState.WaitingForDropOff,
                _workerNodes[unit]);
            finishDropOffMovement(
                unit, UnitMovementLegResult.TargetInvalidated);
            return;
        }
        if (!DropOffHasArrived(
                approach, units.Positions[unit], units.Radii[unit]))
        {
            if (units.MovementGoalKinds[unit] !=
                    UnitMovementGoalKind.DropOffBoundary ||
                units.MovementGoalTargetIds[unit] != approach.Id.Value ||
                units.MovementLegResults[unit] is
                    UnitMovementLegResult.Unreachable or
                    UnitMovementLegResult.SettledShort)
            {
                moveWorker(unit, approach.Target);
            }
            return;
        }
        Players.Credit(
            _workerPlayers[unit], _cargoKinds[unit], _cargoAmounts[unit]);
        events.Publish(
            tick,
            GameplayEventKind.CargoDelivered,
            unit,
            resourceNode: _workerNodes[unit],
            resourceKind: _cargoKinds[unit],
            amount: _cargoAmounts[unit],
            worldPosition: units.Positions[unit]);
        _cargoAmounts[unit] = 0;
        var nodeIndex = _workerNodes[unit];
        if (nodeIndex < 0)
        {
            TransitionWorker(unit, WorkerEconomyState.Idle, -1);
            return;
        }
        if (!IsNodeAvailable(nodeIndex))
        {
            nodeIndex = SelectReplacementNode(
                unit, nodeIndex, units.Positions[unit]);
        }
        if (nodeIndex < 0)
        {
            TransitionWorker(unit, WorkerEconomyState.Idle, -1);
            return;
        }
        TransitionWorker(
            unit, WorkerEconomyState.GoingToResource, nodeIndex);
        moveWorker(unit, _nodes[nodeIndex].Position);
    }

    private void UpdateWaitingForDropOff(
        int unit,
        UnitStore units,
        Action<int, Vector2> moveWorker)
    {
        var approach = ResolveDropOffApproach(
            _workerPlayers[unit], _cargoKinds[unit],
            units.Positions[unit], units.Radii[unit]);
        if (!approach.Found)
        {
            return;
        }
        TransitionWorker(
            unit, WorkerEconomyState.ReturningCargo, _workerNodes[unit]);
        moveWorker(unit, approach.Target);
    }

    private void BeginReturningCargo(
        int unit,
        Vector2 position,
        float unitRadius,
        Action<int, Vector2> moveWorker)
    {
        var approach = ResolveDropOffApproach(
            _workerPlayers[unit], _cargoKinds[unit], position, unitRadius);
        if (!approach.Found)
        {
            TransitionWorker(
                unit, WorkerEconomyState.WaitingForDropOff,
                _workerNodes[unit]);
            return;
        }
        TransitionWorker(
            unit, WorkerEconomyState.ReturningCargo, _workerNodes[unit]);
        moveWorker(unit, approach.Target);
    }

    private EconomyDropOffApproachSnapshot ResolveDropOffApproach(
        int playerId,
        EconomyResourceKind kind,
        Vector2 origin,
        float unitRadius) =>
        ReachableDropOffResolver is not null
            ? ReachableDropOffResolver(playerId, kind, origin, unitRadius)
            : PreviewDropOffApproach(playerId, kind, origin, unitRadius);

    private static bool DropOffHasArrived(
        EconomyDropOffApproachSnapshot approach,
        Vector2 position,
        float unitRadius)
    {
        if (approach.HalfExtents != Vector2.Zero)
        {
            return InteractionGeometry.DiscTouchesRectangle(
                position,
                unitRadius,
                new SimRect(
                    approach.Center - approach.HalfExtents,
                    approach.Center + approach.HalfExtents));
        }
        var allowed = approach.InteractionRadius + unitRadius +
                      InteractionGeometry.NumericTolerance(
                          position,
                          new SimRect(approach.Center, approach.Center));
        return Vector2.DistanceSquared(position, approach.Center) <=
               allowed * allowed;
    }

    private static bool NodeHasArrived(
        ResourceNode node,
        Vector2 position,
        float unitRadius)
    {
        if (node.InteractionHalfExtents != Vector2.Zero)
        {
            return InteractionGeometry.DiscTouchesRectangle(
                position,
                unitRadius,
                new SimRect(
                    node.Position - node.InteractionHalfExtents,
                    node.Position + node.InteractionHalfExtents));
        }
        var allowed = NodeArrivalPadding + unitRadius +
                      InteractionGeometry.NumericTolerance(
                          position,
                          new SimRect(node.Position, node.Position));
        return Vector2.DistanceSquared(position, node.Position) <=
               allowed * allowed;
    }

    private void RetargetOrIdle(
        int unit,
        Vector2 position,
        Action<int, Vector2> moveWorker)
    {
        var replacement = SelectReplacementNode(
            unit, _workerNodes[unit], position);
        TransitionWorker(
            unit,
            replacement >= 0
                ? WorkerEconomyState.GoingToResource
                : WorkerEconomyState.Idle,
            replacement);
        if (replacement >= 0)
        {
            moveWorker(unit, _nodes[replacement].Position);
        }
    }

    private void RetargetTransferredWorker(
        int unit,
        int node,
        Vector2 position,
        float unitRadius,
        Action<int, Vector2> moveWorker)
    {
        _workRemaining[unit] = 0f;
        if (_cargoAmounts[unit] > 0)
        {
            TransitionWorker(unit, WorkerEconomyState.ReturningCargo, node);
            BeginReturningCargo(unit, position, unitRadius, moveWorker);
            return;
        }
        TransitionWorker(unit, WorkerEconomyState.GoingToResource, node);
        moveWorker(unit, _nodes[node].Position);
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

    private int SelectReplacementNode(
        int unit,
        int preferredNode,
        Vector2 position)
    {
        if ((uint)preferredNode >= (uint)_nodes.Count)
            return FindNearestNode(_cargoKinds[unit], position);
        var assignedNormal = _nodes
            .Select(node => node.AssignedNormal)
            .ToArray();
        var assignedMules = _nodes
            .Select(node => node.AssignedMules)
            .ToArray();
        var currentNode = _workerNodes[unit];
        if (IsAssignedState(_workerStates[unit]) &&
            (uint)currentNode < (uint)assignedNormal.Length)
        {
            if (_gathererCapabilities[unit] == GathererCapability.Mule)
                assignedMules[currentNode]--;
            else
                assignedNormal[currentNode]--;
        }
        return SelectAssignedNode(
            _workerPlayers[unit], preferredNode, position,
            _gathererCapabilities[unit], assignedNormal, assignedMules);
    }

    private int SelectAssignedNode(
        int playerId,
        int preferredNode,
        Vector2 workerPosition,
        GathererCapability capability,
        int[]? assignedNormalOverride = null,
        int[]? assignedMuleOverride = null)
    {
        if ((uint)preferredNode >= (uint)_nodes.Count)
            return -1;
        var preferred = _nodes[preferredNode];
        var preferredBase = OwningBase(preferredNode);
        var candidates = new List<ResourceAssignmentCandidate>();
        var clusterRadiusSquared = BaseResourceRadius * BaseResourceRadius;
        for (var nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
        {
            var node = _nodes[nodeIndex];
            if (node.Kind != preferred.Kind || !IsNodeAvailable(nodeIndex))
                continue;
            var sameCluster = preferredBase >= 0
                ? OwningBase(nodeIndex) == preferredBase
                : Vector2.DistanceSquared(
                    node.Position, preferred.Position) <= clusterRadiusSquared;
            if (!sameCluster)
                continue;
            var dropOffIndex = FindNearestDropOff(
                playerId,
                preferred.Kind,
                node.Position);
            var dropOffDistance = dropOffIndex >= 0
                ? _dropOffs[dropOffIndex].DistanceSquaredTo(node.Position, 0f)
                : float.PositiveInfinity;
            candidates.Add(new ResourceAssignmentCandidate(
                nodeIndex,
                capability == GathererCapability.Mule
                    ? assignedMuleOverride?[nodeIndex] ?? node.AssignedMules
                    : assignedNormalOverride?[nodeIndex] ?? node.AssignedNormal,
                capability == GathererCapability.Mule
                    ? 1
                    : node.IdealNormalAssignments,
                nodeIndex == preferredNode,
                Vector2.DistanceSquared(workerPosition, node.Position),
                dropOffDistance));
        }
        return ResourceAssignmentPolicy.Select(
            candidates.ToArray());
    }

    private int FindNearestDropOff(
        int playerId,
        EconomyResourceKind kind,
        Vector2 position,
        float clearance = 0f)
    {
        var best = -1;
        var bestDistance = float.PositiveInfinity;
        for (var index = 0; index < _dropOffs.Count; index++)
        {
            var dropOff = _dropOffs[index];
            if (!dropOff.Operational || dropOff.PlayerId != playerId ||
                !dropOff.Accepts(kind))
            {
                continue;
            }
            var distance = dropOff.DistanceSquaredTo(position, clearance);
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

    private int OwningBase(int nodeIndex)
    {
        if ((uint)nodeIndex >= (uint)_nodes.Count)
            return -1;
        var best = -1;
        var bestDistance = BaseResourceRadius * BaseResourceRadius;
        for (var index = 0; index < _bases.Count; index++)
        {
            var value = _bases[index];
            if (!value.Operational)
                continue;
            var distance = Vector2.DistanceSquared(
                value.Position, _nodes[nodeIndex].Position);
            if (distance < bestDistance || distance == bestDistance && index < best)
            {
                best = index;
                bestDistance = distance;
            }
        }
        return best;
    }

    private bool TryOwnedOperationalBase(
        EconomyBaseId id,
        int playerId,
        out EconomyBase value)
    {
        if ((uint)id.Value < (uint)_bases.Count &&
            _bases[id.Value].PlayerId == playerId &&
            _bases[id.Value].Operational)
        {
            value = _bases[id.Value];
            return true;
        }
        value = null!;
        return false;
    }

    private void TransitionWorker(
        int unit,
        WorkerEconomyState state,
        int nodeIndex)
    {
        var previousState = _workerStates[unit];
        var previousNode = _workerNodes[unit];
        if ((uint)previousNode < (uint)_nodes.Count &&
            IsAssignedState(previousState))
        {
            var node = _nodes[previousNode];
            if (_gathererCapabilities[unit] == GathererCapability.Mule)
            {
                node.AssignedMules--;
                if (previousState == WorkerEconomyState.Gathering)
                    node.ActiveMules--;
            }
            else
            {
                node.AssignedNormal--;
                if (previousState == WorkerEconomyState.Gathering)
                    node.ActiveNormal--;
                if (previousState == WorkerEconomyState.WaitingForResource)
                    node.WaitingNormal--;
            }
            if (node.AssignedNormal < 0 || node.ActiveNormal < 0 ||
                node.WaitingNormal < 0 || node.AssignedMules < 0 ||
                node.ActiveMules < 0)
                throw new InvalidOperationException(
                    "Worker assignment counters became negative.");
        }

        _workerStates[unit] = state;
        _workerNodes[unit] = nodeIndex;
        if ((uint)nodeIndex < (uint)_nodes.Count && IsAssignedState(state))
        {
            var node = _nodes[nodeIndex];
            if (_gathererCapabilities[unit] == GathererCapability.Mule)
            {
                node.AssignedMules++;
                if (state == WorkerEconomyState.Gathering)
                    node.ActiveMules++;
            }
            else
            {
                node.AssignedNormal++;
                if (state == WorkerEconomyState.Gathering)
                    node.ActiveNormal++;
                if (state == WorkerEconomyState.WaitingForResource)
                    node.WaitingNormal++;
            }
        }
    }

    private static bool IsAssignedState(WorkerEconomyState state) =>
        state is not WorkerEconomyState.None and not WorkerEconomyState.Idle;

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

    private static ReturnCargoCommandResult ReturnCargoFailure(
        ReturnCargoCommandCode code,
        int unit) => new(code, unit);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private sealed class ResourceNode(
        EconomyResourceNodeId id,
        EconomyResourceKind kind,
        Vector2 position,
        int remaining,
        int harvestBatch,
        float harvestSeconds,
        int normalActiveSlots,
        int idealNormalAssignments,
        bool requiresRefinery,
        bool operational,
        EconomyHarvestMode harvestMode)
    {
        public EconomyResourceNodeId Id { get; } = id;
        public EconomyResourceKind Kind { get; } = kind;
        public Vector2 Position { get; } = position;
        public int Remaining { get; set; } = remaining;
        public int HarvestBatch { get; } = harvestBatch;
        public float HarvestSeconds { get; } = harvestSeconds;
        public int NormalActiveSlots { get; } = normalActiveSlots;
        public int IdealNormalAssignments { get; } = idealNormalAssignments;
        public bool RequiresRefinery { get; } = requiresRefinery;
        public bool Operational { get; set; } = operational;
        public EconomyHarvestMode HarvestMode { get; } = harvestMode;
        public Vector2 InteractionHalfExtents { get; set; }
        public int ActiveNormal { get; set; }
        public int AssignedNormal { get; set; }
        public int WaitingNormal { get; set; }
        public int ActiveMules { get; set; }
        public int AssignedMules { get; set; }
    }

    private sealed class DropOff(
        EconomyDropOffId id,
        int playerId,
        Vector2 position,
        Vector2 halfExtents,
        float arrivalRadius,
        bool acceptsMinerals,
        bool acceptsVespene,
        bool operational)
    {
        public EconomyDropOffId Id { get; } = id;
        public int PlayerId { get; } = playerId;
        public Vector2 Position { get; } = position;
        public Vector2 HalfExtents { get; } = halfExtents;
        public float ArrivalRadius { get; } = arrivalRadius;
        public bool AcceptsMinerals { get; } = acceptsMinerals;
        public bool AcceptsVespene { get; } = acceptsVespene;
        public bool Operational { get; set; } = operational;
        public bool Accepts(EconomyResourceKind kind) =>
            kind == EconomyResourceKind.Minerals
                ? AcceptsMinerals
                : AcceptsVespene;

        public Vector2 ApproachPoint(Vector2 origin, float clearance)
        {
            if (HalfExtents == Vector2.Zero) return Position;
            var bounds = new SimRect(
                Position - HalfExtents,
                Position + HalfExtents).Expanded(clearance);
            Vector2[] candidates =
            [
                new(bounds.Min.X,
                    Math.Clamp(origin.Y, bounds.Min.Y, bounds.Max.Y)),
                new(bounds.Max.X,
                    Math.Clamp(origin.Y, bounds.Min.Y, bounds.Max.Y)),
                new(Math.Clamp(origin.X, bounds.Min.X, bounds.Max.X),
                    bounds.Min.Y),
                new(Math.Clamp(origin.X, bounds.Min.X, bounds.Max.X),
                    bounds.Max.Y)
            ];
            var best = candidates[0];
            var bestDistance = Vector2.DistanceSquared(origin, best);
            for (var index = 1; index < candidates.Length; index++)
            {
                var distance = Vector2.DistanceSquared(origin, candidates[index]);
                if (distance < bestDistance)
                {
                    best = candidates[index];
                    bestDistance = distance;
                }
            }
            return best;
        }

        public float DistanceSquaredTo(Vector2 origin, float clearance)
        {
            if (HalfExtents == Vector2.Zero)
            {
                var distance = MathF.Max(
                    0f, Vector2.Distance(origin, Position) - ArrivalRadius);
                return distance * distance;
            }
            var bounds = new SimRect(
                Position - HalfExtents,
                Position + HalfExtents).Expanded(clearance);
            return bounds.DistanceSquaredTo(origin);
        }

    }

    private sealed class EconomyBase(
        EconomyBaseId id,
        int playerId,
        GameplayBuildingId townHall,
        EconomyDropOffId dropOff,
        Vector2 position,
        bool operational)
    {
        public EconomyBaseId Id { get; } = id;
        public int PlayerId { get; } = playerId;
        public GameplayBuildingId TownHall { get; } = townHall;
        public EconomyDropOffId DropOff { get; } = dropOff;
        public Vector2 Position { get; } = position;
        public bool Operational { get; set; } = operational;
    }
}
