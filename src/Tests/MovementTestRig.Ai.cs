using RtsDemo.AI;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public sealed partial class MovementTestRig
{
    private RtsAiDirector? _aiDirector;
    private ModularSkirmishAiPolicy? _aiPolicy;

    public void AttachDemoAi(
        int playerId,
        int targetWorkers = 10,
        int attackArmySize = 4,
        float buildingSeconds = 1.5f)
    {
        if (_aiDirector is not null || !float.IsFinite(buildingSeconds) ||
            buildingSeconds <= 0f)
            throw new InvalidOperationException("AI is already attached or invalid.");
        var definitions = DemoBuildingTypes.All.Select(value =>
            value with { BuildSeconds = buildingSeconds }).ToArray();
        if (!BuildingTypeCatalogSnapshot.TryCreate(
                BuildingTypeCatalogSnapshot.CurrentFormatVersion,
                definitions,
                out var buildings,
                out var validation) || buildings is null)
        {
            throw new InvalidOperationException(
                $"AI building catalog is invalid: {validation.FirstError}.");
        }
        var technologies = DemoTechnologies.CreateCatalog();
        var adapter = new RtsSimulationAiAdapter(_simulation, technologies);
        _aiPolicy = new ModularSkirmishAiPolicy(new ModularAiConfig(
            buildings,
            DemoProductionCatalog.CreateSnapshot(),
            technologies,
            TargetWorkers: targetWorkers,
            AttackArmySize: attackArmySize));
        _aiDirector = new RtsAiDirector(adapter, adapter);
        _aiDirector.Register(
            playerId,
            _aiPolicy,
            decisionIntervalTicks: 12,
            decisionOffsetTicks: (int)(Tick % 12),
            maximumIntentsPerDecision: 8);
    }

    public TestAiSnapshot ObserveAi()
    {
        if (_aiDirector is null || _aiPolicy is null)
            throw new InvalidOperationException("No AI is attached.");
        var snapshot = _aiDirector.CaptureState();
        return new TestAiSnapshot(
            (TestAiStrategicPhase)_aiPolicy.Phase,
            snapshot.Agents[0].LastDecisionTick,
            snapshot.Agents[0].PolicyState.Length);
    }

    partial void StepAi() => _aiDirector?.Update(Tick);
}
