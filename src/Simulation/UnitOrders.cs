using System.Numerics;

namespace RtsDemo.Simulation;

public enum UnitOrderKind : byte
{
    None,
    Move,
    AttackMove,
    AttackTarget,
    Stop,
    Hold
}

public readonly record struct UnitOrder(
    UnitOrderKind Kind,
    Vector2 TargetPosition,
    int TargetUnit = -1,
    int SequenceId = 0);

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
    private readonly int[] _pendingSequenceIds;
    private readonly byte[] _heads;

    public UnitCommandQueueStore(int capacity)
    {
        PendingCounts = new byte[capacity];
        ActiveKinds = new UnitOrderKind[capacity];
        ActivePositions = new Vector2[capacity];
        ActiveTargetUnits = new int[capacity];
        ActiveSequenceIds = new int[capacity];
        HasActiveOrders = new bool[capacity];
        ActiveOrdersWereQueued = new bool[capacity];
        CompletedQueuedOrders = new int[capacity];
        QueueOverflowCounts = new int[capacity];
        _heads = new byte[capacity];
        _pendingKinds = new UnitOrderKind[capacity * MaximumPendingOrders];
        _pendingPositions = new Vector2[capacity * MaximumPendingOrders];
        _pendingTargetUnits = new int[capacity * MaximumPendingOrders];
        _pendingSequenceIds = new int[capacity * MaximumPendingOrders];
        Array.Fill(ActiveTargetUnits, -1);
        Array.Fill(_pendingTargetUnits, -1);
    }

    public byte[] PendingCounts { get; }
    public UnitOrderKind[] ActiveKinds { get; }
    public Vector2[] ActivePositions { get; }
    public int[] ActiveTargetUnits { get; }
    public int[] ActiveSequenceIds { get; }
    public bool[] HasActiveOrders { get; }
    public bool[] ActiveOrdersWereQueued { get; }
    public int[] CompletedQueuedOrders { get; }
    public int[] QueueOverflowCounts { get; }

    public void Begin(int unit, UnitOrder order, bool wasQueued)
    {
        ActiveKinds[unit] = order.Kind;
        ActivePositions[unit] = order.TargetPosition;
        ActiveTargetUnits[unit] = order.TargetUnit;
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
            _pendingSequenceIds[index]);
        _pendingTargetUnits[index] = -1;
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
            hash.Add(ActiveSequenceIds[unit]);
            hash.Add(ActiveOrdersWereQueued[unit]);
            hash.Add(PendingCounts[unit]);
            hash.Add(CompletedQueuedOrders[unit]);
            hash.Add(QueueOverflowCounts[unit]);

            for (var pending = 0; pending < PendingCounts[unit]; pending++)
            {
                var slot = (_heads[unit] + pending) % MaximumPendingOrders;
                var index = unit * MaximumPendingOrders + slot;
                hash.Add((byte)_pendingKinds[index]);
                hash.Add(_pendingPositions[index]);
                hash.Add(_pendingTargetUnits[index]);
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
        Copy(source.ActiveSequenceIds, ActiveSequenceIds);
        Copy(source.HasActiveOrders, HasActiveOrders);
        Copy(source.ActiveOrdersWereQueued, ActiveOrdersWereQueued);
        Copy(source.CompletedQueuedOrders, CompletedQueuedOrders);
        Copy(source.QueueOverflowCounts, QueueOverflowCounts);
        Copy(source._heads, _heads);
        Copy(source._pendingKinds, _pendingKinds);
        Copy(source._pendingPositions, _pendingPositions);
        Copy(source._pendingTargetUnits, _pendingTargetUnits);
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
            _pendingSequenceIds[index]);
    }

    private static void Copy<T>(T[] source, T[] destination) =>
        Array.Copy(source, destination, source.Length);
}

public enum SmartCommandTargetKind : byte
{
    Ground,
    FriendlyUnit,
    EnemyUnit
}

public readonly record struct SmartCommandTarget(
    SmartCommandTargetKind Kind,
    Vector2 Position,
    int Unit = -1);

public static class SmartCommandResolver
{
    public static UnitOrder Resolve(
        SmartCommandTarget target,
        bool attackMoveModifier) =>
        attackMoveModifier
            ? new UnitOrder(UnitOrderKind.AttackMove, target.Position)
            : target.Kind == SmartCommandTargetKind.EnemyUnit
                ? new UnitOrder(UnitOrderKind.AttackTarget, target.Position, target.Unit)
                : new UnitOrder(UnitOrderKind.Move, target.Position);
}

/// <summary>
/// Pure selection index for ten SC-style control groups.
/// </summary>
public sealed class ControlGroupManager
{
    public const int GroupCount = 10;
    private readonly int _unitCapacity;
    private readonly bool[] _memberships;

    public ControlGroupManager(int unitCapacity)
    {
        _unitCapacity = unitCapacity;
        _memberships = new bool[GroupCount * unitCapacity];
    }

    public void Assign(int group, ReadOnlySpan<int> units)
    {
        ValidateGroup(group);
        Array.Clear(_memberships, group * _unitCapacity, _unitCapacity);
        Add(group, units);
    }

    public void Add(int group, ReadOnlySpan<int> units)
    {
        ValidateGroup(group);
        var offset = group * _unitCapacity;
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            if ((uint)unit >= (uint)_unitCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(units));
            }
            _memberships[offset + unit] = true;
        }
    }

    public int Recall(int group, ReadOnlySpan<bool> alive, Span<int> output)
    {
        ValidateGroup(group);
        if (alive.Length < _unitCapacity)
        {
            throw new ArgumentException("Alive mask is smaller than unit capacity.", nameof(alive));
        }

        var count = 0;
        var offset = group * _unitCapacity;
        for (var unit = 0; unit < _unitCapacity && count < output.Length; unit++)
        {
            if (_memberships[offset + unit] && alive[unit])
            {
                output[count++] = unit;
            }
        }
        return count;
    }

    public int Count(int group, ReadOnlySpan<bool> alive)
    {
        ValidateGroup(group);
        var count = 0;
        var offset = group * _unitCapacity;
        for (var unit = 0; unit < _unitCapacity; unit++)
        {
            if (_memberships[offset + unit] && alive[unit])
            {
                count++;
            }
        }
        return count;
    }

    private static void ValidateGroup(int group)
    {
        if ((uint)group >= GroupCount)
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }
    }
}
