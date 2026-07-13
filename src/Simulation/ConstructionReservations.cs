namespace RtsDemo.Simulation;

public readonly record struct ConstructionReservationId(int Value)
{
    public bool IsValid => Value > 0;
}

public readonly record struct ConstructionReservationEntry(
    ConstructionReservationId Id,
    GameplayBuildingId BuildingId,
    int PlayerId,
    SimRect Bounds,
    long AcceptedTick);

public sealed record ConstructionReservationRuntimeSnapshot(
    int NextId,
    ConstructionReservationEntry[] Entries);

public sealed class ConstructionReservationStore
{
    private readonly List<ConstructionReservationEntry> _entries = [];
    private int _nextId = 1;

    public int Count => _entries.Count;

    public ConstructionReservationId Add(
        GameplayBuildingId buildingId,
        int playerId,
        SimRect bounds,
        long acceptedTick)
    {
        if (buildingId.Value < 0 || playerId < 0 || acceptedTick < 0 ||
            !ValidBounds(bounds))
            throw new ArgumentOutOfRangeException(nameof(buildingId));
        var id = new ConstructionReservationId(_nextId++);
        _entries.Add(new ConstructionReservationEntry(
            id, buildingId, playerId, bounds, acceptedTick));
        return id;
    }

    public bool Remove(ConstructionReservationId id)
    {
        var index = _entries.FindIndex(value => value.Id == id);
        if (index < 0)
            return false;
        _entries.RemoveAt(index);
        return true;
    }

    public bool TryFindOverlap(
        SimRect bounds,
        out ConstructionReservationEntry conflict,
        ConstructionReservationId ignore = default)
    {
        for (var index = 0; index < _entries.Count; index++)
        {
            var value = _entries[index];
            if (value.Id == ignore)
                continue;
            if (!OverlapsArea(bounds, value.Bounds))
                continue;
            conflict = value;
            return true;
        }
        conflict = default;
        return false;
    }

    public bool Contains(ConstructionReservationId id) =>
        id.IsValid && _entries.Any(value => value.Id == id);

    public ConstructionReservationRuntimeSnapshot CaptureRuntimeState() =>
        new(_nextId, _entries.OrderBy(value => value.Id.Value).ToArray());

    public void RestoreRuntimeState(
        ConstructionReservationRuntimeSnapshot snapshot)
    {
        if (snapshot.NextId <= 0 || snapshot.Entries is null)
            throw new InvalidOperationException("Reservation snapshot is invalid.");
        _entries.Clear();
        var ids = new HashSet<int>();
        var buildings = new HashSet<int>();
        var maximumId = 0;
        for (var index = 0; index < snapshot.Entries.Length; index++)
        {
            var value = snapshot.Entries[index];
            if (!value.Id.IsValid || !ids.Add(value.Id.Value) ||
                value.BuildingId.Value < 0 ||
                !buildings.Add(value.BuildingId.Value) || value.PlayerId < 0 ||
                value.AcceptedTick < 0 || !ValidBounds(value.Bounds))
            {
                throw new InvalidOperationException(
                    "Reservation runtime entry is invalid.");
            }
            maximumId = Math.Max(maximumId, value.Id.Value);
            _entries.Add(value);
        }
        if (snapshot.NextId <= maximumId)
            throw new InvalidOperationException("Reservation next ID is invalid.");
        _entries.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
        _nextId = snapshot.NextId;
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(_nextId);
        hash.Add(_entries.Count);
        foreach (var value in _entries.OrderBy(entry => entry.Id.Value))
        {
            hash.Add(value.Id.Value);
            hash.Add(value.BuildingId.Value);
            hash.Add(value.PlayerId);
            hash.Add(value.Bounds.Min);
            hash.Add(value.Bounds.Max);
            hash.Add(value.AcceptedTick);
        }
    }

    private static bool ValidBounds(SimRect bounds) =>
        float.IsFinite(bounds.Min.X) && float.IsFinite(bounds.Min.Y) &&
        float.IsFinite(bounds.Max.X) && float.IsFinite(bounds.Max.Y) &&
        bounds.Width > 0f && bounds.Height > 0f;

    private static bool OverlapsArea(SimRect left, SimRect right) =>
        left.Min.X < right.Max.X && left.Max.X > right.Min.X &&
        left.Min.Y < right.Max.Y && left.Max.Y > right.Min.Y;
}
