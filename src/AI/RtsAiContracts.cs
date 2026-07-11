using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.AI;

public enum AiIntentKind : byte
{
    Gather,
    TransferWorkers,
    Build,
    ResumeBuild,
    Train,
    Research,
    Move,
    AttackMove,
    AttackUnit,
    AttackBuilding
}

public readonly record struct AiUnitSnapshot(
    int UnitId,
    Vector2 Position,
    bool IsWorker,
    WorkerEconomyState WorkerState,
    int TargetResourceNodeId,
    EconomyResourceKind? TargetResourceKind,
    UnitMoveMode MoveMode,
    CombatPhase CombatPhase);

public readonly record struct AiFacilitySnapshot(
    GameplayBuildingId BuildingId,
    BuildingTypeProfile Type,
    BuildingFunctionKind Function,
    BuildingLifecycleState State,
    SimRect Bounds,
    int BuilderUnit,
    int ProductionOrders,
    int ResearchOrders);

public readonly record struct AiTechnologySnapshot(
    TechnologyProfile Technology,
    int CurrentLevel,
    bool Queued);

public sealed record AiObservationSnapshot(
    long Tick,
    int PlayerId,
    PlayerViewSnapshot View,
    PlayerEconomySnapshot Economy,
    AiUnitSnapshot[] OwnUnits,
    EconomyBaseSnapshot[] Bases,
    AiFacilitySnapshot[] Facilities,
    AiTechnologySnapshot[] Technologies,
    MatchSnapshot Match);

public readonly record struct AiIntent(
    AiIntentKind Kind,
    int[] Units,
    Vector2 Position,
    int ResourceNodeId = -1,
    EconomyBaseId SourceBase = default,
    EconomyBaseId TargetBase = default,
    int Count = 0,
    GameplayBuildingId Facility = default,
    BuildingTypeProfile Building = default,
    ProductionRecipeProfile Recipe = default,
    TechnologyProfile Technology = default,
    int TargetUnitId = -1,
    int TargetBuildingId = -1);

public enum AiIntentExecutionCode : byte
{
    Success,
    Rejected,
    InvalidIntent
}

public readonly record struct AiIntentExecutionResult(
    AiIntentExecutionCode Code)
{
    public bool Succeeded => Code == AiIntentExecutionCode.Success;
}

public interface IRtsAiObservationSource
{
    AiObservationSnapshot Capture(int playerId);
}

public interface IRtsAiIntentExecutor
{
    AiIntentExecutionResult Execute(int playerId, AiIntent intent);
}

public interface IAiIntentSink
{
    int Count { get; }
    int Capacity { get; }
    bool TryAdd(AiIntent intent);
}

public interface IRtsAiPolicy
{
    string PolicyId { get; }
    int StateFormatVersion { get; }
    void Decide(AiObservationSnapshot observation, IAiIntentSink intents);
    byte[] CaptureState();
    void RestoreState(ReadOnlySpan<byte> state, int formatVersion);
}

public interface IRtsAiExecutionObserver
{
    void ObserveExecution(
        long tick,
        AiIntent intent,
        AiIntentExecutionResult result);
}

public sealed class AiIntentBuffer : IAiIntentSink
{
    private readonly AiIntent[] _intents;

    public AiIntentBuffer(int capacity)
    {
        if (capacity <= 0 || capacity > 256)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _intents = new AiIntent[capacity];
    }

    public int Count { get; private set; }
    public int Capacity => _intents.Length;
    public ReadOnlySpan<AiIntent> Intents => _intents.AsSpan(0, Count);

    public bool TryAdd(AiIntent intent)
    {
        if (Count >= Capacity || !Enum.IsDefined(intent.Kind) ||
            intent.Units is null)
        {
            return false;
        }
        _intents[Count++] = intent with { Units = intent.Units.ToArray() };
        return true;
    }

    public void Clear()
    {
        Array.Clear(_intents, 0, Count);
        Count = 0;
    }
}

public readonly record struct AiAgentRuntimeEntry(
    int PlayerId,
    string PolicyId,
    int PolicyStateFormatVersion,
    int DecisionIntervalTicks,
    int DecisionOffsetTicks,
    long LastDecisionTick,
    byte[] PolicyState);

public sealed record RtsAiDirectorSnapshot(AiAgentRuntimeEntry[] Agents);

public sealed class RtsAiDirector
{
    private readonly IRtsAiObservationSource _observations;
    private readonly IRtsAiIntentExecutor _executor;
    private readonly List<Agent> _agents = [];

    public RtsAiDirector(
        IRtsAiObservationSource observations,
        IRtsAiIntentExecutor executor)
    {
        _observations = observations;
        _executor = executor;
    }

    public void Register(
        int playerId,
        IRtsAiPolicy policy,
        int decisionIntervalTicks = 12,
        int decisionOffsetTicks = 0,
        int maximumIntentsPerDecision = 8)
    {
        if (playerId <= 0 || string.IsNullOrWhiteSpace(policy.PolicyId) ||
            policy.StateFormatVersion <= 0 || decisionIntervalTicks <= 0 ||
            decisionOffsetTicks < 0 ||
            decisionOffsetTicks >= decisionIntervalTicks ||
            _agents.Any(value => value.PlayerId == playerId))
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
        _agents.Add(new Agent(
            playerId, policy, decisionIntervalTicks, decisionOffsetTicks,
            new AiIntentBuffer(maximumIntentsPerDecision)));
        _agents.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));
    }

    public int Update(long tick)
    {
        var executed = 0;
        foreach (var agent in _agents)
        {
            if (tick < agent.Offset ||
                (tick - agent.Offset) % agent.Interval != 0 ||
                tick <= agent.LastDecisionTick)
            {
                continue;
            }
            var observation = _observations.Capture(agent.PlayerId);
            var participant = observation.Match.Players.FirstOrDefault(
                value => value.PlayerId == agent.PlayerId);
            if (observation.Match.IsCompleted ||
                observation.Match.IsRunning &&
                    (participant.PlayerId != agent.PlayerId ||
                     participant.Status != MatchPlayerStatus.Active))
            {
                agent.LastDecisionTick = tick;
                continue;
            }
            agent.Buffer.Clear();
            agent.Policy.Decide(observation, agent.Buffer);
            foreach (var intent in agent.Buffer.Intents)
            {
                var result = _executor.Execute(agent.PlayerId, intent);
                if (agent.Policy is IRtsAiExecutionObserver observer)
                    observer.ObserveExecution(tick, intent, result);
                executed++;
            }
            agent.LastDecisionTick = tick;
        }
        return executed;
    }

    public RtsAiDirectorSnapshot CaptureState() => new(
        _agents.Select(value => new AiAgentRuntimeEntry(
            value.PlayerId,
            value.Policy.PolicyId,
            value.Policy.StateFormatVersion,
            value.Interval,
            value.Offset,
            value.LastDecisionTick,
            value.Policy.CaptureState().ToArray())).ToArray());

    public void RestoreState(RtsAiDirectorSnapshot snapshot)
    {
        if (snapshot.Agents.Length != _agents.Count)
            throw new InvalidOperationException("AI agent count mismatch.");
        for (var index = 0; index < _agents.Count; index++)
        {
            var agent = _agents[index];
            var state = snapshot.Agents[index];
            if (state.PlayerId != agent.PlayerId ||
                state.PolicyId != agent.Policy.PolicyId ||
                state.PolicyStateFormatVersion !=
                    agent.Policy.StateFormatVersion ||
                state.DecisionIntervalTicks != agent.Interval ||
                state.DecisionOffsetTicks != agent.Offset ||
                state.LastDecisionTick < -1)
            {
                throw new InvalidOperationException("AI agent state mismatch.");
            }
            agent.Policy.RestoreState(
                state.PolicyState, state.PolicyStateFormatVersion);
            agent.LastDecisionTick = state.LastDecisionTick;
        }
    }

    private sealed class Agent(
        int playerId,
        IRtsAiPolicy policy,
        int interval,
        int offset,
        AiIntentBuffer buffer)
    {
        public int PlayerId { get; } = playerId;
        public IRtsAiPolicy Policy { get; } = policy;
        public int Interval { get; } = interval;
        public int Offset { get; } = offset;
        public AiIntentBuffer Buffer { get; } = buffer;
        public long LastDecisionTick { get; set; } = -1;
    }
}

public sealed class RtsSimulationAiAdapter :
    IRtsAiObservationSource,
    IRtsAiIntentExecutor
{
    private readonly RtsSimulation _simulation;
    private readonly TechnologyCatalogSnapshot? _technologyCatalog;

    public RtsSimulationAiAdapter(
        RtsSimulation simulation,
        TechnologyCatalogSnapshot? technologyCatalog = null)
    {
        _simulation = simulation;
        _technologyCatalog = technologyCatalog;
    }

    public AiObservationSnapshot Capture(int playerId)
    {
        var view = _simulation.CreatePlayerView(playerId);
        var facilities = view.Buildings
            .Where(value => value.Relation == PlayerEntityRelation.Own)
            .Select(value => new AiFacilitySnapshot(
                value.BuildingId,
                value.Type,
                value.Type.Function,
                value.State,
                value.Bounds,
                _simulation.Construction.Observe(value.BuildingId).BuilderUnit,
                _simulation.Production.Observe(value.BuildingId).Orders.Length,
                _simulation.Technology.Observe(value.BuildingId).Orders.Length))
            .ToArray();
        var ownUnits = view.Units
            .Where(value => value.Relation == PlayerEntityRelation.Own)
            .Select(value =>
            {
                var worker = _simulation.Economy.IsWorker(value.UnitId);
                var workerSnapshot = worker
                    ? _simulation.Economy.Worker(value.UnitId)
                    : default;
                return new AiUnitSnapshot(
                    value.UnitId,
                    value.Position,
                    worker,
                    worker
                        ? workerSnapshot.State
                        : WorkerEconomyState.None,
                    worker
                        ? workerSnapshot.TargetNode.Value
                        : -1,
                    worker && workerSnapshot.TargetNode.Value >= 0
                        ? _simulation.Economy.ObserveResourceNode(
                            workerSnapshot.TargetNode).Kind
                        : null,
                    value.MoveMode,
                    value.CombatPhase);
            })
            .ToArray();
        var technologies = _technologyCatalog is null
            ? []
            : _technologyCatalog.Technologies.ToArray().Select(value =>
                new AiTechnologySnapshot(
                    value,
                    _simulation.Technology.Level(playerId, value.Id),
                    facilities.Any(facility =>
                        _simulation.Technology.Observe(facility.BuildingId)
                            .Orders.Any(order =>
                                order.PlayerId == playerId &&
                                order.Technology.Id == value.Id))))
                .ToArray();
        return new AiObservationSnapshot(
            _simulation.Metrics.Tick,
            playerId,
            view,
            _simulation.Economy.Players.Snapshot(playerId),
            ownUnits,
            _simulation.Economy.CreateBaseOverview(
                playerId, _simulation.Units.Count),
            facilities,
            technologies,
            _simulation.Match.CreateSnapshot(
                _simulation.Construction, _simulation.Economy,
                _simulation.Units, _simulation.Combat));
    }

    public AiIntentExecutionResult Execute(int playerId, AiIntent intent)
    {
        if (intent.Units is null)
            return new(AiIntentExecutionCode.InvalidIntent);
        var succeeded = intent.Kind switch
        {
            AiIntentKind.Gather => intent.Units.Length == 1 &&
                _simulation.IssueGather(
                    playerId, intent.Units[0],
                    new EconomyResourceNodeId(intent.ResourceNodeId)).Succeeded,
            AiIntentKind.TransferWorkers => _simulation.IssueWorkerTransfer(
                playerId, intent.SourceBase, intent.TargetBase,
                intent.Count).Succeeded,
            AiIntentKind.Build => intent.Units.Length == 1 &&
                _simulation.IssueConstruction(
                    playerId, intent.Units[0], intent.Building,
                    intent.Position,
                    new EconomyResourceNodeId(intent.ResourceNodeId)).Succeeded,
            AiIntentKind.ResumeBuild => intent.Units.Length == 1 &&
                _simulation.ResumeConstruction(
                    playerId, intent.Facility, intent.Units[0]),
            AiIntentKind.Train => _simulation.IssueProduction(
                playerId, intent.Facility, intent.Recipe).Succeeded,
            AiIntentKind.Research => _simulation.IssueResearch(
                playerId, intent.Facility, intent.Technology).Succeeded,
            AiIntentKind.Move => _simulation.IssuePlayerMove(
                playerId, intent.Units, intent.Position).Succeeded,
            AiIntentKind.AttackMove => _simulation.IssuePlayerAttackMove(
                playerId, intent.Units, intent.Position).Succeeded,
            AiIntentKind.AttackUnit => intent.TargetUnitId >= 0 &&
                _simulation.IssuePlayerSmartCommand(
                    playerId,
                    intent.Units,
                    new SmartCommandTarget(
                        SmartCommandTargetKind.EnemyUnit,
                        intent.Position,
                        Unit: intent.TargetUnitId),
                    attackMoveModifier: false).Succeeded,
            AiIntentKind.AttackBuilding => intent.TargetBuildingId >= 0 &&
                _simulation.IssuePlayerSmartCommand(
                    playerId,
                    intent.Units,
                    new SmartCommandTarget(
                        SmartCommandTargetKind.EnemyBuilding,
                        intent.Position,
                        Building: intent.TargetBuildingId),
                    attackMoveModifier: false).Succeeded,
            _ => false
        };
        return new(succeeded
            ? AiIntentExecutionCode.Success
            : AiIntentExecutionCode.Rejected);
    }
}
