using System.Numerics;

namespace RtsDemo.Simulation;

public enum AbilityEventKind : byte
{
    Started,
    Impact,
    Ended,
    Interrupted
}

public enum AbilityTargetKind : byte
{
    None,
    Self,
    Unit,
    Building,
    World
}

public enum AbilityEndReason : byte
{
    None,
    Completed,
    Canceled,
    CasterDied,
    TargetInvalid
}

public readonly record struct AbilityEvent(
    long Tick,
    ulong Sequence,
    AbilityEventKind Kind,
    string AbilityId,
    int CasterUnit,
    AbilityTargetKind TargetKind,
    int TargetId,
    Vector2 WorldPosition,
    AbilityEndReason EndReason,
    int CasterBuilding = -1);

public readonly record struct AbilityEventBatch(
    AbilityEvent[] Events,
    ulong LatestSequence,
    int LostEvents);

/// <summary>
/// Derived, fixed-capacity ability lifecycle stream. Gameplay systems publish
/// authoritative transitions here; presentation systems consume them without
/// reaching into ability state or owning audio/visual side effects. As with the
/// combat and gameplay event streams, replay regenerates these events and they
/// are deliberately excluded from snapshots and state hashes.
/// </summary>
public sealed class AbilityEventStream
{
    private readonly AbilityEvent[] _events;
    private ulong _nextSequence = 1;
    private int _count;

    public AbilityEventStream(int capacity = 4096)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _events = new AbilityEvent[capacity];
    }

    public int Capacity => _events.Length;
    public ulong LatestSequence => _nextSequence - 1;

    public void Publish(
        long tick,
        AbilityEventKind kind,
        string abilityId,
        int casterUnit,
        AbilityTargetKind targetKind = AbilityTargetKind.None,
        int targetId = -1,
        Vector2 worldPosition = default,
        AbilityEndReason endReason = AbilityEndReason.None,
        int casterBuilding = -1)
    {
        if (string.IsNullOrWhiteSpace(abilityId))
            throw new ArgumentException(
                "Ability event id must be non-empty.", nameof(abilityId));
        if ((casterUnit < 0 && casterBuilding < 0) ||
            (casterUnit >= 0 && casterBuilding >= 0))
            throw new ArgumentOutOfRangeException(nameof(casterUnit));
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(targetKind) ||
            !Enum.IsDefined(endReason))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var sequence = _nextSequence++;
        _events[(int)((sequence - 1) % (ulong)_events.Length)] =
            new AbilityEvent(
                tick, sequence, kind, abilityId, casterUnit, targetKind,
                targetId, worldPosition, endReason, casterBuilding);
        if (_count < _events.Length) _count++;
    }

    public AbilityEventBatch ReadAfter(ulong sequence)
    {
        var latest = LatestSequence;
        if (sequence >= latest) return new AbilityEventBatch([], latest, 0);
        var oldest = _count == 0 ? latest + 1 : latest - (ulong)_count + 1;
        var requested = sequence + 1;
        var lostCount = requested < oldest ? oldest - requested : 0UL;
        var lost = (int)Math.Min(lostCount, int.MaxValue);
        var first = Math.Max(requested, oldest);
        var result = new AbilityEvent[checked((int)(latest - first + 1))];
        for (var index = 0; index < result.Length; index++)
        {
            var eventSequence = first + (ulong)index;
            result[index] =
                _events[(int)((eventSequence - 1) % (ulong)_events.Length)];
        }
        return new AbilityEventBatch(result, latest, lost);
    }

    public void Reset()
    {
        _nextSequence = 1;
        _count = 0;
        Array.Clear(_events);
    }
}
