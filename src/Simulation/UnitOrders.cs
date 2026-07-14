using System.Numerics;

namespace RtsDemo.Simulation;

public enum UnitOrderKind : byte
{
    None,
    Move,
    AttackMove,
    AttackTarget,
    AttackBuilding,
    Stop,
    Hold,
    GatherResource,
    ResumeConstruction,
    ReturnCargo,
    FollowFriendly,
    ActivateConcealment,
    DeactivateConcealment
}

public readonly record struct UnitOrder(
    UnitOrderKind Kind,
    Vector2 TargetPosition,
    int TargetUnit = -1,
    int TargetBuilding = -1,
    int TargetResourceNode = -1,
    int SequenceId = 0);

public static class UnitOrderContract
{
    public static bool IsStructurallyValid(UnitOrder order) =>
        Enum.IsDefined(order.Kind) && order.Kind != UnitOrderKind.None &&
        float.IsFinite(order.TargetPosition.X) &&
        float.IsFinite(order.TargetPosition.Y) &&
        (order.Kind is UnitOrderKind.AttackTarget or UnitOrderKind.FollowFriendly
            ? order.TargetUnit >= 0
            : order.TargetUnit == -1) &&
        (order.Kind is UnitOrderKind.AttackBuilding or
            UnitOrderKind.ResumeConstruction or UnitOrderKind.Move
            ? order.TargetBuilding >= 0
                || order.Kind == UnitOrderKind.Move &&
                   order.TargetBuilding == -1
            : order.TargetBuilding == -1) &&
        (order.Kind == UnitOrderKind.GatherResource
            ? order.TargetResourceNode >= 0
            : order.TargetResourceNode == -1) &&
        order.SequenceId >= 0;
}

/// <summary>
/// Fixed-capacity per-unit command queues. Control groups never own commands;
/// a group issue appends the same sequence-tagged order to each selected unit.
/// </summary>
public sealed class UnitCommandQueueStore
{
    public const int MaximumPendingOrders = 16;

    private readonly UnitOrderKind[] _pendingKinds;
    private readonly Vector2[] _pendingPositions;
    private readonly int[] _pendingTargetUnits;
    private readonly int[] _pendingTargetBuildings;
    private readonly int[] _pendingTargetResourceNodes;
    private readonly int[] _pendingSequenceIds;
    private readonly byte[] _heads;

    public UnitCommandQueueStore(int capacity)
    {
        PendingCounts = new byte[capacity];
        ActiveKinds = new UnitOrderKind[capacity];
        ActivePositions = new Vector2[capacity];
        ActiveTargetUnits = new int[capacity];
        ActiveTargetBuildings = new int[capacity];
        ActiveTargetResourceNodes = new int[capacity];
        ActiveSequenceIds = new int[capacity];
        HasActiveOrders = new bool[capacity];
        ActiveOrdersWereQueued = new bool[capacity];
        CompletedQueuedOrders = new int[capacity];
        QueueOverflowCounts = new int[capacity];
        ConstructionEvacuationActive = new bool[capacity];
        ConstructionEvacuationBuildings = new int[capacity];
        ConstructionEvacuationTargets = new Vector2[capacity];
        ConstructionEvacuationFootprints = new SimRect[capacity];
        ProductionEvacuationActive = new bool[capacity];
        ProductionEvacuationTargets = new Vector2[capacity];
        ProductionEvacuationFootprints = new SimRect[capacity];
        _heads = new byte[capacity];
        _pendingKinds = new UnitOrderKind[capacity * MaximumPendingOrders];
        _pendingPositions = new Vector2[capacity * MaximumPendingOrders];
        _pendingTargetUnits = new int[capacity * MaximumPendingOrders];
        _pendingTargetBuildings = new int[capacity * MaximumPendingOrders];
        _pendingTargetResourceNodes = new int[capacity * MaximumPendingOrders];
        _pendingSequenceIds = new int[capacity * MaximumPendingOrders];
        Array.Fill(ActiveTargetUnits, -1);
        Array.Fill(_pendingTargetUnits, -1);
        Array.Fill(ActiveTargetBuildings, -1);
        Array.Fill(_pendingTargetBuildings, -1);
        Array.Fill(ActiveTargetResourceNodes, -1);
        Array.Fill(_pendingTargetResourceNodes, -1);
        Array.Fill(ConstructionEvacuationBuildings, -1);
    }

    public byte[] PendingCounts { get; }
    public UnitOrderKind[] ActiveKinds { get; }
    public Vector2[] ActivePositions { get; }
    public int[] ActiveTargetUnits { get; }
    public int[] ActiveTargetBuildings { get; }
    public int[] ActiveTargetResourceNodes { get; }
    public int[] ActiveSequenceIds { get; }
    public bool[] HasActiveOrders { get; }
    public bool[] ActiveOrdersWereQueued { get; }
    public int[] CompletedQueuedOrders { get; }
    public int[] QueueOverflowCounts { get; }
    public bool[] ConstructionEvacuationActive { get; }
    public int[] ConstructionEvacuationBuildings { get; }
    public Vector2[] ConstructionEvacuationTargets { get; }
    public SimRect[] ConstructionEvacuationFootprints { get; }
    public bool[] ProductionEvacuationActive { get; }
    public Vector2[] ProductionEvacuationTargets { get; }
    public SimRect[] ProductionEvacuationFootprints { get; }

    public void Begin(int unit, UnitOrder order, bool wasQueued)
    {
        ActiveKinds[unit] = order.Kind;
        ActivePositions[unit] = order.TargetPosition;
        ActiveTargetUnits[unit] = order.TargetUnit;
        ActiveTargetBuildings[unit] = order.TargetBuilding;
        ActiveTargetResourceNodes[unit] = order.TargetResourceNode;
        ActiveSequenceIds[unit] = order.SequenceId;
        HasActiveOrders[unit] = true;
        ActiveOrdersWereQueued[unit] = wasQueued;
    }

    public void ClearPending(int unit)
    {
        PendingCounts[unit] = 0;
        _heads[unit] = 0;
    }

    public bool TryEnqueue(int unit, UnitOrder order)
    {
        var count = PendingCounts[unit];
        if (count >= MaximumPendingOrders)
        {
            QueueOverflowCounts[unit]++;
            return false;
        }

        var slot = (_heads[unit] + count) % MaximumPendingOrders;
        var index = unit * MaximumPendingOrders + slot;
        _pendingKinds[index] = order.Kind;
        _pendingPositions[index] = order.TargetPosition;
        _pendingTargetUnits[index] = order.TargetUnit;
        _pendingTargetBuildings[index] = order.TargetBuilding;
        _pendingTargetResourceNodes[index] = order.TargetResourceNode;
        _pendingSequenceIds[index] = order.SequenceId;
        PendingCounts[unit]++;
        return true;
    }

    public bool TryDequeue(int unit, out UnitOrder order)
    {
        if (PendingCounts[unit] == 0)
        {
            order = default;
            return false;
        }

        var head = _heads[unit];
        var index = unit * MaximumPendingOrders + head;
        order = new UnitOrder(
            _pendingKinds[index],
            _pendingPositions[index],
            _pendingTargetUnits[index],
            _pendingTargetBuildings[index],
            _pendingTargetResourceNodes[index],
            _pendingSequenceIds[index]);
        _pendingTargetUnits[index] = -1;
        _pendingTargetBuildings[index] = -1;
        _pendingTargetResourceNodes[index] = -1;
        _heads[unit] = (byte)((head + 1) % MaximumPendingOrders);
        PendingCounts[unit]--;
        return true;
    }

    internal void AppendStateHash(ref StableHash64 hash, int unitCount)
    {
        for (var unit = 0; unit < unitCount; unit++)
        {
            hash.Add(HasActiveOrders[unit]);
            hash.Add((byte)ActiveKinds[unit]);
            hash.Add(ActivePositions[unit]);
            hash.Add(ActiveTargetUnits[unit]);
            hash.Add(ActiveTargetBuildings[unit]);
            hash.Add(ActiveTargetResourceNodes[unit]);
            hash.Add(ActiveSequenceIds[unit]);
            hash.Add(ActiveOrdersWereQueued[unit]);
            hash.Add(PendingCounts[unit]);
            hash.Add(CompletedQueuedOrders[unit]);
            hash.Add(QueueOverflowCounts[unit]);
            hash.Add(ConstructionEvacuationActive[unit]);
            hash.Add(ConstructionEvacuationBuildings[unit]);
            hash.Add(ConstructionEvacuationTargets[unit]);
            hash.Add(ConstructionEvacuationFootprints[unit].Min);
            hash.Add(ConstructionEvacuationFootprints[unit].Max);
            hash.Add(ProductionEvacuationActive[unit]);
            hash.Add(ProductionEvacuationTargets[unit]);
            hash.Add(ProductionEvacuationFootprints[unit].Min);
            hash.Add(ProductionEvacuationFootprints[unit].Max);

            for (var pending = 0; pending < PendingCounts[unit]; pending++)
            {
                var slot = (_heads[unit] + pending) % MaximumPendingOrders;
                var index = unit * MaximumPendingOrders + slot;
                hash.Add((byte)_pendingKinds[index]);
                hash.Add(_pendingPositions[index]);
                hash.Add(_pendingTargetUnits[index]);
                hash.Add(_pendingTargetBuildings[index]);
                hash.Add(_pendingTargetResourceNodes[index]);
                hash.Add(_pendingSequenceIds[index]);
            }
        }
    }

    internal void CopyRuntimeStateFrom(UnitCommandQueueStore source)
    {
        if (source.PendingCounts.Length != PendingCounts.Length)
        {
            throw new InvalidOperationException("Command queue runtime capacity mismatch.");
        }
        Copy(source.PendingCounts, PendingCounts);
        Copy(source.ActiveKinds, ActiveKinds);
        Copy(source.ActivePositions, ActivePositions);
        Copy(source.ActiveTargetUnits, ActiveTargetUnits);
        Copy(source.ActiveTargetBuildings, ActiveTargetBuildings);
        Copy(source.ActiveTargetResourceNodes, ActiveTargetResourceNodes);
        Copy(source.ActiveSequenceIds, ActiveSequenceIds);
        Copy(source.HasActiveOrders, HasActiveOrders);
        Copy(source.ActiveOrdersWereQueued, ActiveOrdersWereQueued);
        Copy(source.CompletedQueuedOrders, CompletedQueuedOrders);
        Copy(source.QueueOverflowCounts, QueueOverflowCounts);
        Copy(source.ConstructionEvacuationActive, ConstructionEvacuationActive);
        Copy(source.ConstructionEvacuationBuildings,
            ConstructionEvacuationBuildings);
        Copy(source.ConstructionEvacuationTargets,
            ConstructionEvacuationTargets);
        Copy(source.ConstructionEvacuationFootprints,
            ConstructionEvacuationFootprints);
        Copy(source.ProductionEvacuationActive,
            ProductionEvacuationActive);
        Copy(source.ProductionEvacuationTargets,
            ProductionEvacuationTargets);
        Copy(source.ProductionEvacuationFootprints,
            ProductionEvacuationFootprints);
        Copy(source._heads, _heads);
        Copy(source._pendingKinds, _pendingKinds);
        Copy(source._pendingPositions, _pendingPositions);
        Copy(source._pendingTargetUnits, _pendingTargetUnits);
        Copy(source._pendingTargetBuildings, _pendingTargetBuildings);
        Copy(source._pendingTargetResourceNodes, _pendingTargetResourceNodes);
        Copy(source._pendingSequenceIds, _pendingSequenceIds);
    }

    internal UnitOrder PendingAt(int unit, int logicalIndex)
    {
        if ((uint)logicalIndex >= PendingCounts[unit])
        {
            throw new ArgumentOutOfRangeException(nameof(logicalIndex));
        }
        var slot = (_heads[unit] + logicalIndex) % MaximumPendingOrders;
        var index = unit * MaximumPendingOrders + slot;
        return new UnitOrder(
            _pendingKinds[index],
            _pendingPositions[index],
            _pendingTargetUnits[index],
            _pendingTargetBuildings[index],
            _pendingTargetResourceNodes[index],
            _pendingSequenceIds[index]);
    }

    private static void Copy<T>(T[] source, T[] destination) =>
        Array.Copy(source, destination, source.Length);
}

public enum SmartCommandTargetKind : byte
{
    Ground,
    FriendlyUnit,
    EnemyUnit,
    EnemyBuilding,
    FriendlyBuilding,
    ResourceNode
}

public readonly record struct SmartCommandTarget(
    SmartCommandTargetKind Kind,
    Vector2 Position,
    int Unit = -1,
    int Building = -1,
    int ResourceNode = -1);

public static class SmartCommandResolver
{
    public static UnitOrder Resolve(
        SmartCommandTarget target,
        bool attackMoveModifier) =>
        attackMoveModifier
            ? new UnitOrder(UnitOrderKind.AttackMove, target.Position)
            : target.Kind == SmartCommandTargetKind.EnemyUnit
                ? new UnitOrder(UnitOrderKind.AttackTarget, target.Position, target.Unit)
                : target.Kind == SmartCommandTargetKind.EnemyBuilding
                    ? new UnitOrder(
                        UnitOrderKind.AttackBuilding,
                        target.Position,
                        TargetBuilding: target.Building)
                : new UnitOrder(UnitOrderKind.Move, target.Position);
}

public enum ControlGroupEntityKind : byte
{
    Unit,
    Building
}

public readonly record struct ControlGroupEntity(
    ControlGroupEntityKind Kind,
    int EntityId);

/// <summary>
/// Pure selection index for ten SC-style mixed entity control groups.
/// This is an operation-layer index: entity lifetime and ownership are
/// intentionally supplied by the caller when a group is recalled.
/// </summary>
public sealed class ControlGroupManager
{
    public const int GroupCount = 10;
    private readonly List<ControlGroupEntity>[] _groups;

    public ControlGroupManager(int unitCapacity = 0)
    {
        if (unitCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCapacity));
        }
        _groups = Enumerable.Range(0, GroupCount)
            .Select(_ => new List<ControlGroupEntity>())
            .ToArray();
    }

    public void Assign(int group, ReadOnlySpan<int> units)
    {
        var entities = ToUnitEntities(units);
        Assign(group, entities);
    }

    public void Add(int group, ReadOnlySpan<int> units)
    {
        var entities = ToUnitEntities(units);
        Add(group, entities);
    }

    public void Assign(int group, ReadOnlySpan<ControlGroupEntity> entities)
    {
        ValidateGroup(group);
        _groups[group].Clear();
        Add(group, entities);
    }

    public void Add(int group, ReadOnlySpan<ControlGroupEntity> entities)
    {
        ValidateGroup(group);
        for (var index = 0; index < entities.Length; index++)
        {
            ValidateEntity(entities[index], nameof(entities));
            InsertStable(_groups[group], entities[index]);
        }
    }

    public void StealAssign(int group, ReadOnlySpan<ControlGroupEntity> entities)
    {
        ValidateGroup(group);
        RemoveFromAll(entities);
        Assign(group, entities);
    }

    public void StealAdd(int group, ReadOnlySpan<ControlGroupEntity> entities)
    {
        ValidateGroup(group);
        RemoveFromAll(entities, group);
        Add(group, entities);
    }

    public int Recall(
        int group,
        Span<ControlGroupEntity> output,
        Predicate<ControlGroupEntity>? available = null)
    {
        ValidateGroup(group);
        var count = 0;
        foreach (var entity in _groups[group])
        {
            if (count >= output.Length) break;
            if (available is not null && !available(entity)) continue;
            output[count++] = entity;
        }
        return count;
    }

    public ControlGroupEntity[] Recall(
        int group,
        Predicate<ControlGroupEntity>? available = null)
    {
        ValidateGroup(group);
        return available is null
            ? _groups[group].ToArray()
            : _groups[group].Where(entity => available(entity)).ToArray();
    }

    public int Recall(int group, ReadOnlySpan<bool> alive, Span<int> output)
    {
        ValidateGroup(group);
        var count = 0;
        foreach (var entity in _groups[group])
        {
            if (count >= output.Length) break;
            if (entity.Kind != ControlGroupEntityKind.Unit ||
                (uint)entity.EntityId >= (uint)alive.Length ||
                !alive[entity.EntityId]) continue;
            output[count++] = entity.EntityId;
        }
        return count;
    }

    public int Count(int group, ReadOnlySpan<bool> alive)
    {
        ValidateGroup(group);
        var count = 0;
        foreach (var entity in _groups[group])
        {
            if (entity.Kind == ControlGroupEntityKind.Unit &&
                (uint)entity.EntityId < (uint)alive.Length &&
                alive[entity.EntityId]) count++;
        }
        return count;
    }

    private void RemoveFromAll(
        ReadOnlySpan<ControlGroupEntity> entities,
        int exceptGroup = -1)
    {
        for (var index = 0; index < entities.Length; index++)
        {
            ValidateEntity(entities[index], nameof(entities));
        }
        for (var group = 0; group < GroupCount; group++)
        {
            if (group == exceptGroup) continue;
            for (var index = 0; index < entities.Length; index++)
            {
                RemoveStable(_groups[group], entities[index]);
            }
        }
    }

    private static ControlGroupEntity[] ToUnitEntities(ReadOnlySpan<int> units)
    {
        var result = new ControlGroupEntity[units.Length];
        for (var index = 0; index < units.Length; index++)
        {
            result[index] = new ControlGroupEntity(
                ControlGroupEntityKind.Unit, units[index]);
        }
        return result;
    }

    private static void InsertStable(
        List<ControlGroupEntity> group,
        ControlGroupEntity entity)
    {
        var index = group.BinarySearch(entity, ControlGroupEntityComparer.Instance);
        if (index < 0) group.Insert(~index, entity);
    }

    private static void RemoveStable(
        List<ControlGroupEntity> group,
        ControlGroupEntity entity)
    {
        var index = group.BinarySearch(entity, ControlGroupEntityComparer.Instance);
        if (index >= 0) group.RemoveAt(index);
    }

    private static void ValidateEntity(ControlGroupEntity entity, string parameter)
    {
        if (entity.EntityId < 0 ||
            !Enum.IsDefined(entity.Kind))
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }

    private static void ValidateGroup(int group)
    {
        if ((uint)group >= GroupCount)
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }
    }

    private sealed class ControlGroupEntityComparer : IComparer<ControlGroupEntity>
    {
        public static ControlGroupEntityComparer Instance { get; } = new();

        public int Compare(ControlGroupEntity left, ControlGroupEntity right)
        {
            var kind = left.Kind.CompareTo(right.Kind);
            return kind != 0 ? kind : left.EntityId.CompareTo(right.EntityId);
        }
    }
}
