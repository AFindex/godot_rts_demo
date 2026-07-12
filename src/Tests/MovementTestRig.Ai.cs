using RtsDemo.AI;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public sealed class TestAiRuntimeCapture
{
    internal TestAiRuntimeCapture(RtsAiRuntimeSnapshot backend) => Backend = backend;
    internal RtsAiRuntimeSnapshot Backend { get; }
    public long Tick => Backend.Tick;
    public int AgentCount => Backend.Director.Agents.Length;
}

public readonly record struct TestAiPersistenceResult(
    ulong ContinuousHash,
    ulong LiveRestoredHash,
    ulong ReplayHash,
    bool LiveExact,
    bool ReplayExact);

public sealed partial class MovementTestRig
{
    private RtsAiDirector? _aiDirector;
    private readonly Dictionary<int, ModularSkirmishAiPolicy> _aiPolicies = [];
    private readonly List<AiTestRegistration> _aiRegistrations = [];

    public void AttachDemoAi(
        int playerId,
        int targetWorkers = 10,
        int attackArmySize = 4,
        float buildingSeconds = 1.5f) =>
        AttachDemoAi(
            playerId,
            DemoAiConfigurations.Standard with
            {
                TargetWorkers = targetWorkers,
                AttackArmySize = attackArmySize
            },
            buildingSeconds);

    public void AttachDemoAi(
        int playerId,
        AiDifficultyProfile profile,
        float buildingSeconds = 1.5f,
        int decisionOffsetTicks = -1)
    {
        if (_aiPolicies.ContainsKey(playerId) ||
            !float.IsFinite(buildingSeconds) || buildingSeconds <= 0f)
            throw new InvalidOperationException("AI registration is invalid.");
        var definitions = DemoBuildingTypes.All.Select(value =>
            value with { BuildSeconds = buildingSeconds }).ToArray();
        if (!BuildingTypeCatalogSnapshot.TryCreate(
                BuildingTypeCatalogSnapshot.CurrentFormatVersion,
                definitions,
                out var buildings,
                out var validation) || buildings is null)
            throw new InvalidOperationException(
                $"AI building catalog is invalid: {validation.FirstError}.");
        var technologies = DemoTechnologies.CreateCatalog();
        var config = ModularAiConfig.FromProfile(
            buildings,
            DemoProductionCatalog.CreateSnapshot(),
            technologies,
            profile);
        var offset = decisionOffsetTicks >= 0
            ? decisionOffsetTicks
            : (int)(Tick % profile.DecisionIntervalTicks);
        if (offset >= profile.DecisionIntervalTicks)
            throw new ArgumentOutOfRangeException(nameof(decisionOffsetTicks));
        if (_aiDirector is null)
        {
            var adapter = new RtsSimulationAiAdapter(_simulation, technologies);
            _aiDirector = new RtsAiDirector(adapter, adapter);
        }
        var policy = new ModularSkirmishAiPolicy(config);
        _aiDirector.Register(
            playerId,
            policy,
            profile.DecisionIntervalTicks,
            offset,
            profile.MaximumIntentsPerDecision);
        _aiPolicies.Add(playerId, policy);
        _aiRegistrations.Add(new AiTestRegistration(
            playerId, config, profile.DecisionIntervalTicks, offset,
            profile.MaximumIntentsPerDecision));
        _aiRegistrations.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));
    }

    public TestAiSnapshot ObserveAi(int playerId = 1)
    {
        if (_aiDirector is null || !_aiPolicies.TryGetValue(playerId, out var policy))
            throw new InvalidOperationException("Requested AI is not attached.");
        var agent = _aiDirector.CaptureState().Agents.Single(value =>
            value.PlayerId == playerId);
        return new TestAiSnapshot(
            (TestAiStrategicPhase)policy.Phase,
            agent.LastDecisionTick,
            agent.PolicyState.Length,
            policy.LastAttackTick);
    }

    public TestAiRuntimeCapture CaptureAiRuntimeState()
    {
        if (_aiDirector is null)
            throw new InvalidOperationException("No AI is attached.");
        return new TestAiRuntimeCapture(
            RtsAiRuntimeState.Capture(_simulation, _aiDirector));
    }

    public TestAiPersistenceResult ValidateAiPersistence(
        TestReplayPackage package,
        TestAiRuntimeCapture capture,
        long targetTick)
    {
        if (_navigationMap is null || _gameplayProfiles is null ||
            targetTick != Tick || capture.Tick > targetTick)
            throw new InvalidOperationException("AI persistence arguments are invalid.");
        var bound = SimulationHotSnapshotFactory.Bind(
            capture.Backend.Simulation, package.Backend);
        if (!SimulationHotSnapshotFactory.TryRestore(
                bound, package.Backend, _navigationMap, _gameplayProfiles,
                _clearanceBake, out var restoredSimulation, out _) ||
            restoredSimulation is null)
            throw new InvalidOperationException("AI hot simulation restore failed.");
        var restoredDirector = CreateDirector(restoredSimulation, out _);
        RtsAiRuntimeState.Restore(
            restoredSimulation, restoredDirector, capture.Backend);
        while (restoredSimulation.Metrics.Tick < targetTick)
        {
            restoredDirector.Update(restoredSimulation.Metrics.Tick);
            restoredSimulation.Tick(1f / 60f);
        }
        var liveHash = restoredSimulation.ComputeStateHash();
        var replayHash = ReplayPackage(package, targetTick).FinalHash;
        var continuousHash = StateHash;
        return new TestAiPersistenceResult(
            continuousHash, liveHash, replayHash,
            liveHash == continuousHash,
            replayHash == continuousHash);
    }

    private RtsAiDirector CreateDirector(
        RtsSimulation simulation,
        out Dictionary<int, ModularSkirmishAiPolicy> policies)
    {
        var adapter = new RtsSimulationAiAdapter(
            simulation, DemoTechnologies.CreateCatalog());
        var director = new RtsAiDirector(adapter, adapter);
        policies = [];
        foreach (var registration in _aiRegistrations)
        {
            var policy = new ModularSkirmishAiPolicy(registration.Config);
            director.Register(
                registration.PlayerId, policy, registration.Interval,
                registration.Offset, registration.MaximumIntents);
            policies.Add(registration.PlayerId, policy);
        }
        return director;
    }

    partial void StepAi() => _aiDirector?.Update(Tick);

    private readonly record struct AiTestRegistration(
        int PlayerId,
        ModularAiConfig Config,
        int Interval,
        int Offset,
        int MaximumIntents);
}
