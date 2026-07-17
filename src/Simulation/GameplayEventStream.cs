using System.Numerics;

namespace RtsDemo.Simulation;

public enum GameplayEventKind : byte
{
    ConstructionStarted,
    ConstructionResumed,
    ConstructionProgressed,
    ConstructionCompleted,
    HarvestCompleted,
    CargoDelivered,
    MovementLegFinished,
    UnitProduced,
    ConstructionDisplacementStarted,
    ConstructionDisplacementFinished,
    ProductionDisplacementStarted,
    ProductionDisplacementFinished,
    BuildingAttackChaseRetargeted,
    ProductionRallyChanged,
    ResearchCompleted
}

public readonly record struct GameplayEvent(
    long Tick,
    ulong Sequence,
    GameplayEventKind Kind,
    int Unit,
    int Building,
    int ResourceNode,
    EconomyResourceKind ResourceKind,
    int Amount,
    float Value,
    UnitMovementGoalKind MovementGoalKind,
    UnitMovementLegResult MovementResult,
    Vector2 WorldPosition,
    int Player,
    int Technology);

public readonly record struct GameplayEventBatch(
    GameplayEvent[] Events,
    ulong LatestSequence,
    int LostEvents);

/// <summary>
/// Derived, fixed-capacity business event stream. It is rebuilt by replay and
/// deliberately excluded from authoritative snapshots and state hashes.
/// </summary>
public sealed class GameplayEventStream
{
    private readonly GameplayEvent[] _events;
    private ulong _nextSequence = 1;
    private int _count;

    public GameplayEventStream(int capacity = 4096)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _events = new GameplayEvent[capacity];
    }

    public ulong LatestSequence => _nextSequence - 1;

    public void Publish(
        long tick,
        GameplayEventKind kind,
        int unit = -1,
        int building = -1,
        int resourceNode = -1,
        EconomyResourceKind resourceKind = EconomyResourceKind.Minerals,
        int amount = 0,
        float value = 0f,
        UnitMovementGoalKind movementGoalKind = UnitMovementGoalKind.None,
        UnitMovementLegResult movementResult = UnitMovementLegResult.None,
        Vector2 worldPosition = default,
        int player = -1,
        int technology = -1)
    {
        var sequence = _nextSequence++;
        _events[(int)((sequence - 1) % (ulong)_events.Length)] =
            new GameplayEvent(
                tick, sequence, kind, unit, building, resourceNode,
                resourceKind, amount, value, movementGoalKind, movementResult,
                worldPosition, player, technology);
        if (_count < _events.Length) _count++;
    }

    public GameplayEventBatch ReadAfter(ulong sequence)
    {
        var latest = LatestSequence;
        if (sequence >= latest) return new GameplayEventBatch([], latest, 0);
        var oldest = _count == 0 ? latest + 1 : latest - (ulong)_count + 1;
        var requested = sequence + 1;
        var lostCount = requested < oldest ? oldest - requested : 0UL;
        var lost = (int)Math.Min(lostCount, int.MaxValue);
        var first = Math.Max(requested, oldest);
        var result = new GameplayEvent[checked((int)(latest - first + 1))];
        for (var index = 0; index < result.Length; index++)
        {
            var eventSequence = first + (ulong)index;
            result[index] =
                _events[(int)((eventSequence - 1) % (ulong)_events.Length)];
        }
        return new GameplayEventBatch(result, latest, lost);
    }

    public void Reset()
    {
        _nextSequence = 1;
        _count = 0;
        Array.Clear(_events);
    }
}
