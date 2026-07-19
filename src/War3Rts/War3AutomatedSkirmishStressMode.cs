using System.Diagnostics;
using RtsDemo.AI;
using RtsDemo.Simulation;
using GD = Godot.GD;
using Vector2 = System.Numerics.Vector2;

namespace War3Rts;

[Flags]
internal enum War3AutomatedSkirmishActivity
{
    None = 0,
    BankTopUp = 1 << 0,
    Gather = 1 << 1,
    TransferWorkers = 1 << 2,
    Construction = 1 << 3,
    Production = 1 << 4,
    Research = 1 << 5,
    Scouting = 1 << 6,
    Combat = 1 << 7,
    StatusSample = 1 << 8
}

internal readonly record struct War3AutomatedSkirmishUpdateProfile(
    double TotalMilliseconds,
    double BankTopUpMilliseconds,
    double SupportConstructionMilliseconds,
    double AiMilliseconds,
    double StatusSampleMilliseconds,
    long AllocatedBytes,
    int ExecutedIntents,
    War3AutomatedSkirmishActivity Activities);

/// <summary>
/// Test-only, opt-in full-skirmish workload. Both sides use the production AI
/// and the normal command/simulation pipelines. Acceleration and bank top-ups
/// live here so normal matches and production catalogs remain unchanged.
/// </summary>
internal sealed class War3AutomatedSkirmishStressMode
{
    public const string EnableArgument = "--war3-auto-skirmish-stress";
    private const int ActivityCount = 9;
    private readonly int _ticksPerPhysicsFrame;
    private readonly int _bankTarget;
    private readonly int _bankRefreshTicks;
    private readonly int _statusIntervalTicks;
    private readonly int _targetWorkers;
    private readonly int _attackArmySize;
    private readonly int _decisionIntervalTicks;
    private readonly int _attackIntervalTicks;
    private readonly bool _externalDotnetProfiling;
    private readonly long[] _activityCounts = new long[ActivityCount];
    private readonly ProbedAiRuntime _aiRuntime;
    private readonly RtsAiDirector _director;
    private readonly BuildingTypeProfile _supplyBuilding;
    private RtsSimulation? _simulation;
    private Vector2 _playerHome;
    private Vector2 _enemyHome;
    private float _playerDirection;
    private long _nextBankRefreshTick;
    private long _nextSupportConstructionTick;
    private long _nextStatusTick;
    private long _updates;
    private long _executedIntents;
    private long _bankCredits;
    private int _supportConstructionAttempts;
    private int _supportConstructionAccepted;
    private int _supportConstructionRejected;
    private int _playerBuildSiteCursor;
    private int _enemyBuildSiteCursor;
    private bool _summaryPrinted;

    private War3AutomatedSkirmishStressMode(
        string[] arguments,
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies)
    {
        _ticksPerPhysicsFrame = IntegerArgument(
            arguments, "--war3-auto-skirmish-ticks-per-frame=", 4, 1, 32);
        _bankTarget = IntegerArgument(
            arguments, "--war3-auto-skirmish-bank=", 250_000, 10_000,
            10_000_000);
        _bankRefreshTicks = IntegerArgument(
            arguments, "--war3-auto-skirmish-bank-refresh=", 30, 1, 600);
        _statusIntervalTicks = IntegerArgument(
            arguments, "--war3-auto-skirmish-status-interval=", 300, 30,
            3_600);
        _targetWorkers = IntegerArgument(
            arguments, "--war3-auto-skirmish-workers=", 14, 4, 128);
        _attackArmySize = IntegerArgument(
            arguments, "--war3-auto-skirmish-army=", 8, 2, 256);
        _decisionIntervalTicks = IntegerArgument(
            arguments, "--war3-auto-skirmish-decision-interval=", 6, 1, 60);
        _attackIntervalTicks = IntegerArgument(
            arguments, "--war3-auto-skirmish-attack-interval=", 90, 6, 1_800);
        _externalDotnetProfiling = arguments.Contains(
            War3RuntimeProfiler.ExternalDotnetArgument);

        var adapter = new RtsSimulationAiAdapter(simulation, technologies);
        _aiRuntime = new ProbedAiRuntime(adapter);
        _director = new RtsAiDirector(_aiRuntime, _aiRuntime);
        var config = new ModularAiConfig(
            buildings,
            production,
            technologies,
            TargetWorkers: _targetWorkers,
            AttackArmySize: _attackArmySize,
            MaximumIntentsPerDecision: 16,
            SupplyBuffer: 5,
            ScoutIntervalTicks: 120,
            AttackIntervalTicks: _attackIntervalTicks,
            DefenseRadius: 420f);
        _director.Register(
            War3HumanScenario.PlayerId,
            new ModularSkirmishAiPolicy(config),
            _decisionIntervalTicks,
            decisionOffsetTicks: 0,
            maximumIntentsPerDecision: config.MaximumIntentsPerDecision);
        _director.Register(
            War3HumanScenario.EnemyId,
            new ModularSkirmishAiPolicy(config),
            _decisionIntervalTicks,
            decisionOffsetTicks: Math.Min(2, _decisionIntervalTicks - 1),
            maximumIntentsPerDecision: config.MaximumIntentsPerDecision);
        _supplyBuilding = buildings.Type(War3HumanContent.Farm);
    }

    public int TicksPerPhysicsFrame => _ticksPerPhysicsFrame;
    public Vector2 FocusPoint { get; private set; }
    public RtsAiUpdateProfile LastAiProfile => _director.LastUpdateProfile;
    public War3AutomatedSkirmishUpdateProfile LastUpdateProfile
    {
        get;
        private set;
    }

    public static bool IsRequested(string[] arguments) =>
        arguments.Contains(EnableArgument);

    public static War3AutomatedSkirmishStressMode? TryCreate(
        string[] arguments,
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies,
        War3HumanRuntime runtime)
    {
        if (!IsRequested(arguments)) return null;
        var mode = new War3AutomatedSkirmishStressMode(
            arguments, simulation, buildings, production, technologies);
        mode.Initialize(simulation, runtime);
        return mode;
    }

    public void UpdateBeforeSimulation()
    {
        if (_simulation is null) return;
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var updateStart = Stopwatch.GetTimestamp();
        _aiRuntime.BeginTick();
        var activities = War3AutomatedSkirmishActivity.None;

        var bankStart = Stopwatch.GetTimestamp();
        if (_simulation.Metrics.Tick >= _nextBankRefreshTick)
        {
            if (TopUpBanks())
                activities |= War3AutomatedSkirmishActivity.BankTopUp;
            _nextBankRefreshTick =
                _simulation.Metrics.Tick + _bankRefreshTicks;
        }
        var bankEnd = Stopwatch.GetTimestamp();

        if (_simulation.Metrics.Tick >= _nextSupportConstructionTick)
        {
            if (IssueSupplySupportConstruction())
                activities |= War3AutomatedSkirmishActivity.Construction;
            _nextSupportConstructionTick = _simulation.Metrics.Tick + 30;
        }
        var supportConstructionEnd = Stopwatch.GetTimestamp();

        var executed = _director.Update(_simulation.Metrics.Tick);
        var aiEnd = Stopwatch.GetTimestamp();
        activities |= _aiRuntime.Activities;
        _updates++;
        _executedIntents += executed;
        CountActivities(activities);

        if (_simulation.Metrics.Tick >= _nextStatusTick)
        {
            PrintStatus();
            activities |= War3AutomatedSkirmishActivity.StatusSample;
            _activityCounts[8]++;
            _nextStatusTick =
                _simulation.Metrics.Tick + _statusIntervalTicks;
        }
        var statusEnd = Stopwatch.GetTimestamp();
        LastUpdateProfile = new War3AutomatedSkirmishUpdateProfile(
            ElapsedMilliseconds(updateStart, statusEnd),
            ElapsedMilliseconds(bankStart, bankEnd),
            ElapsedMilliseconds(bankEnd, supportConstructionEnd),
            ElapsedMilliseconds(supportConstructionEnd, aiEnd),
            ElapsedMilliseconds(aiEnd, statusEnd),
            GC.GetAllocatedBytesForCurrentThread() - allocationStart,
            executed,
            activities);
    }

    public void PrintSummary()
    {
        if (_summaryPrinted || _simulation is null) return;
        _summaryPrinted = true;
        var match = _simulation.Match.CreateSnapshot(
            _simulation.Construction,
            _simulation.Economy,
            _simulation.Units,
            _simulation.Combat);
        var player = Participant(match, War3HumanScenario.PlayerId);
        var enemy = Participant(match, War3HumanScenario.EnemyId);
        GD.Print(
            "WAR3_AUTO_SKIRMISH_SUMMARY " +
            $"tick={_simulation.Metrics.Tick} updates={_updates} " +
            $"ticks_per_physics={_ticksPerPhysicsFrame} " +
            $"match={match.Phase} winner={match.WinnerPlayerId} " +
            $"intents={_executedIntents} bank_credits={_bankCredits} " +
            $"support_builds={_supportConstructionAccepted}/" +
            $"{_supportConstructionAttempts}/" +
            $"{_supportConstructionRejected} " +
            $"units={player.Workers + player.CombatUnits}/" +
            $"{enemy.Workers + enemy.CombatUnits} " +
            $"workers={player.Workers}/{enemy.Workers} " +
            $"armies={player.CombatUnits}/{enemy.CombatUnits} " +
            $"buildings={player.ActiveBuildings}/{enemy.ActiveBuildings} " +
            $"activities={ActivitySummary()}");
    }

    private void Initialize(RtsSimulation simulation, War3HumanRuntime runtime)
    {
        _simulation = simulation;
        _playerHome = runtime.PlayerHome;
        _enemyHome = runtime.EnemyHome;
        _playerDirection = MathF.Sign(_enemyHome.X - _playerHome.X);
        if (_playerDirection == 0f) _playerDirection = 1f;
        FocusPoint = (runtime.PlayerHome + runtime.EnemyHome) * 0.5f;
        _nextBankRefreshTick = simulation.Metrics.Tick;
        _nextSupportConstructionTick = simulation.Metrics.Tick + 1;
        _nextStatusTick = simulation.Metrics.Tick + _statusIntervalTicks;
        if (!_externalDotnetProfiling)
            simulation.DetailedProfilingEnabled = true;
        TopUpBanks();
        GD.Print(
            "WAR3_AUTO_SKIRMISH_READY " +
            $"ticks_per_physics={_ticksPerPhysicsFrame} " +
            $"bank={_bankTarget} bank_refresh={_bankRefreshTicks} " +
            $"workers={_targetWorkers} army={_attackArmySize} " +
            $"decision_interval={_decisionIntervalTicks} " +
            $"attack_interval={_attackIntervalTicks} " +
            "players=both real_ai=true real_commands=true " +
            "supply_construction=real-command catalog_mutation=false");
    }

    private bool IssueSupplySupportConstruction()
    {
        if (_simulation is null) return false;
        var playerIssued = TryIssueSupplyBuilding(
            War3HumanScenario.PlayerId,
            _playerHome,
            _playerDirection,
            ref _playerBuildSiteCursor);
        var enemyIssued = TryIssueSupplyBuilding(
            War3HumanScenario.EnemyId,
            _enemyHome,
            -_playerDirection,
            ref _enemyBuildSiteCursor);
        return playerIssued || enemyIssued;
    }

    private bool TryIssueSupplyBuilding(
        int player,
        Vector2 home,
        float direction,
        ref int siteCursor)
    {
        if (_simulation is null) return false;
        var bank = _simulation.Economy.Players.Snapshot(player);
        if (bank.SupplyRemaining > 2 || HasPendingSupplyBuilding(player))
            return false;
        var builder = FindBuilder(player);
        if (builder < 0) return false;

        ReadOnlySpan<Vector2> sites =
        [
            new Vector2(-520f, -330f),
            new Vector2(-520f, 330f),
            new Vector2(-650f, -220f),
            new Vector2(-650f, 220f),
            new Vector2(-520f, -500f),
            new Vector2(-520f, 500f),
            new Vector2(-780f, -330f),
            new Vector2(-780f, 330f),
            new Vector2(-650f, -500f),
            new Vector2(-650f, 500f)
        ];
        var local = sites[siteCursor % sites.Length];
        siteCursor++;
        var center = home + new Vector2(direction * local.X, local.Y);
        _supportConstructionAttempts++;
        var result = _simulation.IssueConstruction(
            player, builder, _supplyBuilding, center);
        if (!result.Succeeded)
        {
            _supportConstructionRejected++;
            return true;
        }
        _supportConstructionAccepted++;
        return true;
    }

    private bool HasPendingSupplyBuilding(int player)
    {
        if (_simulation is null) return false;
        for (var index = 0; index < _simulation.Construction.SlotCount; index++)
        {
            var id = new GameplayBuildingId(index);
            if (!_simulation.Construction.IsAlive(id)) continue;
            var building = _simulation.Construction.Observe(id);
            if (building.PlayerId == player &&
                building.Type.Id == _supplyBuilding.Id &&
                building.State != BuildingLifecycleState.Completed)
                return true;
        }
        return false;
    }

    private int FindBuilder(int player)
    {
        if (_simulation is null) return -1;
        for (var unit = 0; unit < _simulation.Units.Count; unit++)
        {
            if (_simulation.Units.Alive[unit] &&
                _simulation.Economy.IsWorkerOwnedBy(unit, player) &&
                !_simulation.Construction.IsAssignedBuilder(unit))
                return unit;
        }
        return -1;
    }

    private bool TopUpBanks()
    {
        if (_simulation is null) return false;
        var playerCredited = TopUpPlayer(War3HumanScenario.PlayerId);
        var enemyCredited = TopUpPlayer(War3HumanScenario.EnemyId);
        return playerCredited || enemyCredited;
    }

    private bool TopUpPlayer(int player)
    {
        if (_simulation is null) return false;
        var bank = _simulation.Economy.Players.Snapshot(player);
        var minerals = Math.Max(0, _bankTarget - bank.Minerals);
        var gas = Math.Max(0, _bankTarget - bank.VespeneGas);
        if (minerals == 0 && gas == 0) return false;
        _simulation.Economy.Players.Refund(
            player, new EconomyCost(minerals, gas));
        _bankCredits++;
        return true;
    }

    private void PrintStatus()
    {
        if (_simulation is null) return;
        var match = _simulation.Match.CreateSnapshot(
            _simulation.Construction,
            _simulation.Economy,
            _simulation.Units,
            _simulation.Combat);
        var player = Participant(match, War3HumanScenario.PlayerId);
        var enemy = Participant(match, War3HumanScenario.EnemyId);
        var playerBank = _simulation.Economy.Players.Snapshot(
            War3HumanScenario.PlayerId);
        var enemyBank = _simulation.Economy.Players.Snapshot(
            War3HumanScenario.EnemyId);
        GD.Print(
            "WAR3_AUTO_SKIRMISH_STATUS " +
            $"tick={_simulation.Metrics.Tick} match={match.Phase} " +
            $"workers={player.Workers}/{enemy.Workers} " +
            $"armies={player.CombatUnits}/{enemy.CombatUnits} " +
            $"buildings={player.ActiveBuildings}/{enemy.ActiveBuildings} " +
            $"production={player.ProductionFacilities}/" +
            $"{enemy.ProductionFacilities} " +
            $"research={player.ResearchFacilities}/{enemy.ResearchFacilities} " +
            $"bank={playerBank.Minerals}:{playerBank.VespeneGas}/" +
            $"{enemyBank.Minerals}:{enemyBank.VespeneGas} " +
            $"supply={playerBank.SupplyUsed}:{playerBank.SupplyCapacity}/" +
            $"{enemyBank.SupplyUsed}:{enemyBank.SupplyCapacity}");
    }

    private static PlayerCapabilitySnapshot Participant(
        MatchSnapshot match,
        int playerId) => match.Players.First(value => value.PlayerId == playerId);

    private void CountActivities(War3AutomatedSkirmishActivity activities)
    {
        for (var index = 0; index < ActivityCount - 1; index++)
        {
            if (((int)activities & (1 << index)) != 0)
                _activityCounts[index]++;
        }
    }

    private string ActivitySummary()
    {
        string[] names =
        [
            "bank", "gather", "transfer", "construction", "production",
            "research", "scouting", "combat", "status"
        ];
        return string.Join(',', names.Select(
            (name, index) => $"{name}:{_activityCounts[index]}"));
    }

    private static int IntegerArgument(
        string[] arguments,
        string prefix,
        int fallback,
        int minimum,
        int maximum)
    {
        var value = arguments.FirstOrDefault(argument => argument.StartsWith(
            prefix, StringComparison.OrdinalIgnoreCase));
        return value is not null &&
               int.TryParse(value[prefix.Length..], out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d / Stopwatch.Frequency;

    private sealed class ProbedAiRuntime :
        IRtsAiObservationSource,
        IRtsAiIntentExecutor
    {
        private readonly RtsSimulationAiAdapter _inner;

        public ProbedAiRuntime(RtsSimulationAiAdapter inner) => _inner = inner;

        public War3AutomatedSkirmishActivity Activities { get; private set; }

        public void BeginTick() => Activities = War3AutomatedSkirmishActivity.None;

        public AiObservationSnapshot Capture(int playerId) =>
            _inner.Capture(playerId);

        public AiIntentExecutionResult Execute(int playerId, AiIntent intent)
        {
            Activities |= intent.Kind switch
            {
                AiIntentKind.Gather => War3AutomatedSkirmishActivity.Gather,
                AiIntentKind.TransferWorkers =>
                    War3AutomatedSkirmishActivity.TransferWorkers,
                AiIntentKind.Build or AiIntentKind.ResumeBuild =>
                    War3AutomatedSkirmishActivity.Construction,
                AiIntentKind.Train => War3AutomatedSkirmishActivity.Production,
                AiIntentKind.Research => War3AutomatedSkirmishActivity.Research,
                AiIntentKind.Move => War3AutomatedSkirmishActivity.Scouting,
                AiIntentKind.AttackMove or AiIntentKind.AttackUnit or
                    AiIntentKind.AttackBuilding =>
                    War3AutomatedSkirmishActivity.Combat,
                _ => War3AutomatedSkirmishActivity.None
            };
            return _inner.Execute(playerId, intent);
        }
    }
}
