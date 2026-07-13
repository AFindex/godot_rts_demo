namespace RtsDemo.Simulation;

public readonly record struct PlayerDiplomacyRuntimeEntry(
    int PlayerId,
    int AllianceId,
    bool SharedVision);

public sealed record PlayerDiplomacyRuntimeSnapshot(
    PlayerDiplomacyRuntimeEntry[] Players);

/// <summary>
/// Setup-time player diplomacy. Unconfigured positive players are independent
/// alliances whose alliance id equals their player id. Player zero is neutral.
/// </summary>
public sealed class PlayerDiplomacySystem
{
    private const int DefaultAllianceBase = 1_000_000;
    private readonly Dictionary<int, Entry> _players = [];

    public void ConfigureAlliance(
        int allianceId,
        bool sharedVision,
        ReadOnlySpan<int> playerIds)
    {
        if (allianceId <= 0 || allianceId >= DefaultAllianceBase ||
            playerIds.Length < 2)
            throw new ArgumentOutOfRangeException(nameof(allianceId));
        if (_players.Values.Any(value =>
                value.AllianceId == allianceId &&
                value.SharedVision != sharedVision))
        {
            throw new InvalidOperationException(
                $"Alliance {allianceId} already has a different shared-vision setting.");
        }
        var ordered = playerIds.ToArray();
        Array.Sort(ordered);
        for (var index = 0; index < ordered.Length; index++)
        {
            var playerId = ordered[index];
            if (playerId <= 0 || playerId >= PlayerVisibilitySystem.MaximumPlayers ||
                index > 0 && playerId == ordered[index - 1])
            {
                throw new ArgumentException(
                    "Alliance players must be unique positive player ids.",
                    nameof(playerIds));
            }
            if (_players.TryGetValue(playerId, out var existing) &&
                (existing.AllianceId != allianceId ||
                 existing.SharedVision != sharedVision))
            {
                throw new InvalidOperationException(
                    $"Player {playerId} already belongs to alliance {existing.AllianceId}.");
            }
        }
        for (var index = 0; index < ordered.Length; index++)
            _players[ordered[index]] = new Entry(allianceId, sharedVision);
    }

    public PlayerEntityRelation Relation(int viewerPlayerId, int ownerPlayerId)
    {
        if (viewerPlayerId > 0 && viewerPlayerId == ownerPlayerId)
            return PlayerEntityRelation.Own;
        if (viewerPlayerId <= 0 || ownerPlayerId <= 0)
            return PlayerEntityRelation.Neutral;
        return AllianceIdFor(viewerPlayerId) == AllianceIdFor(ownerPlayerId)
            ? PlayerEntityRelation.Ally
            : PlayerEntityRelation.Enemy;
    }

    public bool IsEnemy(int viewerPlayerId, int ownerPlayerId) =>
        Relation(viewerPlayerId, ownerPlayerId) == PlayerEntityRelation.Enemy;

    public bool IsFriendly(int viewerPlayerId, int ownerPlayerId) =>
        Relation(viewerPlayerId, ownerPlayerId) is
            PlayerEntityRelation.Own or PlayerEntityRelation.Ally;

    public int AllianceIdFor(int playerId) =>
        playerId <= 0
            ? 0
            : _players.TryGetValue(playerId, out var entry)
                ? entry.AllianceId
                : DefaultAllianceBase + playerId;

    public bool SharesVision(int viewerPlayerId, int sourcePlayerId)
    {
        if (viewerPlayerId <= 0 || sourcePlayerId <= 0)
            return false;
        if (viewerPlayerId == sourcePlayerId)
            return true;
        return Relation(viewerPlayerId, sourcePlayerId) ==
                   PlayerEntityRelation.Ally &&
               _players.TryGetValue(sourcePlayerId, out var source) &&
               source.SharedVision;
    }

    public PlayerDiplomacyRuntimeSnapshot CaptureRuntimeState() => new(
        _players.OrderBy(value => value.Key)
            .Select(value => new PlayerDiplomacyRuntimeEntry(
                value.Key, value.Value.AllianceId, value.Value.SharedVision))
            .ToArray());

    public void RestoreRuntimeState(PlayerDiplomacyRuntimeSnapshot snapshot)
    {
        _players.Clear();
        var previousPlayer = 0;
        foreach (var entry in snapshot.Players)
        {
            if (entry.PlayerId <= previousPlayer ||
                entry.PlayerId >= PlayerVisibilitySystem.MaximumPlayers ||
                entry.AllianceId <= 0 || entry.AllianceId >= DefaultAllianceBase)
            {
                throw new InvalidOperationException(
                    "Diplomacy players must be ordered and valid.");
            }
            _players.Add(
                entry.PlayerId,
                new Entry(entry.AllianceId, entry.SharedVision));
            previousPlayer = entry.PlayerId;
        }
        foreach (var group in snapshot.Players.GroupBy(value => value.AllianceId))
        {
            if (group.Count() < 2 ||
                group.Select(value => value.SharedVision).Distinct().Count() != 1)
            {
                throw new InvalidOperationException(
                    "Shared vision is an alliance-wide setting.");
            }
        }
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        var snapshot = CaptureRuntimeState();
        hash.Add(snapshot.Players.Length);
        foreach (var entry in snapshot.Players)
        {
            hash.Add(entry.PlayerId);
            hash.Add(entry.AllianceId);
            hash.Add(entry.SharedVision);
        }
    }

    private readonly record struct Entry(int AllianceId, bool SharedVision);
}
