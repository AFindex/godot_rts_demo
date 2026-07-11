namespace RtsDemo.Simulation;

public enum MatchPhase : byte
{
    Setup,
    Running,
    Completed
}

public enum MatchPlayerStatus : byte
{
    Active,
    Defeated,
    Victorious
}

public readonly record struct PlayerCapabilitySnapshot(
    int PlayerId,
    MatchPlayerStatus Status,
    bool EstablishedPresence,
    int ActiveBuildings,
    int CompletedBuildings,
    int TownHalls,
    int ProductionFacilities,
    int ResearchFacilities,
    int Workers,
    int CombatUnits)
{
    public bool HasWorkerProduction => TownHalls > 0;
    public bool HasArmyProduction => ProductionFacilities > 0;
    public bool HasAnyProduction => HasWorkerProduction || HasArmyProduction;
    public bool IsEliminationRisk => EstablishedPresence && ActiveBuildings <= 1;
}

public sealed record MatchSnapshot(
    MatchPhase Phase,
    long StartedTick,
    long CompletedTick,
    int WinnerPlayerId,
    PlayerCapabilitySnapshot[] Players)
{
    public bool IsRunning => Phase == MatchPhase.Running;
    public bool IsCompleted => Phase == MatchPhase.Completed;
    public bool IsDraw => IsCompleted && WinnerPlayerId < 0;
}

public readonly record struct MatchPlayerRuntimeEntry(
    int PlayerId,
    MatchPlayerStatus Status,
    bool EstablishedPresence,
    long DefeatedTick);

public sealed record MatchRuntimeSnapshot(
    MatchPhase Phase,
    long StartedTick,
    long CompletedTick,
    int WinnerPlayerId,
    MatchPlayerRuntimeEntry[] Players);

internal readonly record struct PlayerBuildingCapabilities(
    int Active,
    int Completed,
    int TownHalls,
    int Production,
    int Research);

public sealed class MatchSystem
{
    private readonly List<PlayerState> _players = [];

    public MatchPhase Phase { get; private set; } = MatchPhase.Setup;
    public long StartedTick { get; private set; } = -1;
    public long CompletedTick { get; private set; } = -1;
    public int WinnerPlayerId { get; private set; } = -1;

    public void Start(
        long tick,
        ReadOnlySpan<int> playerIds,
        PlayerEconomyStore economy)
    {
        if (Phase != MatchPhase.Setup || tick != 0 || playerIds.Length < 2)
            throw new InvalidOperationException(
                "A match must start once at tick zero with at least two players.");
        _players.Clear();
        var ordered = playerIds.ToArray();
        Array.Sort(ordered);
        for (var index = 0; index < ordered.Length; index++)
        {
            if (!economy.IsRegistered(ordered[index]) || ordered[index] <= 0 ||
                index > 0 && ordered[index] == ordered[index - 1])
            {
                throw new ArgumentException("Match players must be unique and registered.");
            }
            _players.Add(new PlayerState(ordered[index]));
        }
        Phase = MatchPhase.Running;
        StartedTick = tick;
    }

    public void Update(
        long tick,
        ConstructionSystem construction)
    {
        if (Phase != MatchPhase.Running)
            return;
        for (var index = 0; index < _players.Count; index++)
        {
            var player = _players[index];
            if (player.Status != MatchPlayerStatus.Active)
                continue;
            var buildings = construction.CountPlayerCapabilities(player.PlayerId);
            player.EstablishedPresence |= buildings.Active > 0;
            if (player.EstablishedPresence && buildings.Active == 0)
            {
                player.Status = MatchPlayerStatus.Defeated;
                player.DefeatedTick = tick;
            }
        }
        if (_players.Any(value => !value.EstablishedPresence))
            return;
        var active = _players.Where(value =>
            value.Status == MatchPlayerStatus.Active).ToArray();
        if (active.Length > 1)
            return;
        Phase = MatchPhase.Completed;
        CompletedTick = tick;
        WinnerPlayerId = active.Length == 1 ? active[0].PlayerId : -1;
        if (active.Length == 1)
            active[0].Status = MatchPlayerStatus.Victorious;
    }

    public bool CanIssueCommands(int playerId) =>
        Phase != MatchPhase.Completed &&
        (Phase == MatchPhase.Setup || _players.Any(value =>
            value.PlayerId == playerId && value.Status == MatchPlayerStatus.Active));

    public bool IsDefeated(int playerId) =>
        _players.Any(value => value.PlayerId == playerId &&
            value.Status == MatchPlayerStatus.Defeated);

    public bool IsParticipant(int playerId) =>
        _players.Any(value => value.PlayerId == playerId);

    public MatchSnapshot CreateSnapshot(
        ConstructionSystem construction,
        EconomySystem economy,
        UnitStore units,
        CombatStore combat)
    {
        var players = new PlayerCapabilitySnapshot[_players.Count];
        for (var index = 0; index < players.Length; index++)
        {
            var state = _players[index];
            var buildings = construction.CountPlayerCapabilities(state.PlayerId);
            var workers = 0;
            var combatUnits = 0;
            for (var unit = 0; unit < units.Count; unit++)
            {
                if (!units.Alive[unit] || combat.Teams[unit] != state.PlayerId)
                    continue;
                if (economy.IsWorker(unit)) workers++;
                else combatUnits++;
            }
            players[index] = new PlayerCapabilitySnapshot(
                state.PlayerId, state.Status, state.EstablishedPresence,
                buildings.Active, buildings.Completed, buildings.TownHalls,
                buildings.Production, buildings.Research, workers, combatUnits);
        }
        return new MatchSnapshot(
            Phase, StartedTick, CompletedTick, WinnerPlayerId, players);
    }

    public MatchRuntimeSnapshot CaptureRuntimeState() => new(
        Phase,
        StartedTick,
        CompletedTick,
        WinnerPlayerId,
        _players.Select(value => new MatchPlayerRuntimeEntry(
            value.PlayerId, value.Status, value.EstablishedPresence,
            value.DefeatedTick)).ToArray());

    public void RestoreRuntimeState(
        MatchRuntimeSnapshot snapshot,
        PlayerEconomyStore economy)
    {
        if (!Enum.IsDefined(snapshot.Phase) || snapshot.StartedTick < -1 ||
            snapshot.CompletedTick < -1 || snapshot.WinnerPlayerId < -1 ||
            snapshot.Phase == MatchPhase.Setup &&
                (snapshot.StartedTick != -1 || snapshot.CompletedTick != -1 ||
                 snapshot.WinnerPlayerId != -1 || snapshot.Players.Length != 0) ||
            snapshot.Phase == MatchPhase.Running &&
                (snapshot.StartedTick != 0 || snapshot.CompletedTick != -1 ||
                 snapshot.WinnerPlayerId != -1 || snapshot.Players.Length < 2) ||
            snapshot.Phase == MatchPhase.Completed &&
                (snapshot.StartedTick != 0 || snapshot.CompletedTick < 0 ||
                 snapshot.Players.Length < 2))
        {
            throw new InvalidOperationException("Match runtime header is invalid.");
        }
        _players.Clear();
        var previousPlayer = 0;
        var victorious = 0;
        var active = 0;
        foreach (var entry in snapshot.Players)
        {
            if (entry.PlayerId <= previousPlayer ||
                !economy.IsRegistered(entry.PlayerId) ||
                !Enum.IsDefined(entry.Status) || entry.DefeatedTick < -1 ||
                entry.Status == MatchPlayerStatus.Active && entry.DefeatedTick != -1 ||
                entry.Status == MatchPlayerStatus.Victorious &&
                    entry.PlayerId != snapshot.WinnerPlayerId ||
                entry.Status == MatchPlayerStatus.Defeated &&
                    (!entry.EstablishedPresence || entry.DefeatedTick < 0))
            {
                throw new InvalidOperationException("Match player state is invalid.");
            }
            victorious += entry.Status == MatchPlayerStatus.Victorious ? 1 : 0;
            active += entry.Status == MatchPlayerStatus.Active ? 1 : 0;
            _players.Add(new PlayerState(entry.PlayerId)
            {
                Status = entry.Status,
                EstablishedPresence = entry.EstablishedPresence,
                DefeatedTick = entry.DefeatedTick
            });
            previousPlayer = entry.PlayerId;
        }
        if (snapshot.Phase == MatchPhase.Running &&
                (victorious != 0 || active == 0) ||
            snapshot.Phase == MatchPhase.Completed && active != 0 ||
            snapshot.WinnerPlayerId >= 0 && victorious != 1 ||
            snapshot.WinnerPlayerId < 0 && victorious != 0)
        {
            throw new InvalidOperationException("Match winner state is invalid.");
        }
        Phase = snapshot.Phase;
        StartedTick = snapshot.StartedTick;
        CompletedTick = snapshot.CompletedTick;
        WinnerPlayerId = snapshot.WinnerPlayerId;
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add((byte)Phase);
        hash.Add(StartedTick);
        hash.Add(CompletedTick);
        hash.Add(WinnerPlayerId);
        hash.Add(_players.Count);
        foreach (var player in _players)
        {
            hash.Add(player.PlayerId);
            hash.Add((byte)player.Status);
            hash.Add(player.EstablishedPresence);
            hash.Add(player.DefeatedTick);
        }
    }

    private sealed class PlayerState(int playerId)
    {
        public int PlayerId { get; } = playerId;
        public MatchPlayerStatus Status { get; set; } = MatchPlayerStatus.Active;
        public bool EstablishedPresence { get; set; }
        public long DefeatedTick { get; set; } = -1;
    }
}
