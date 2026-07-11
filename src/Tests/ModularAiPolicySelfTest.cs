using System.Numerics;
using RtsDemo.AI;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ModularAiPolicySelfTest
{
    public static SelfTestResult Run()
    {
        var config = ModularAiConfig.Demo() with
        {
            MaximumIntentsPerDecision = 6,
            AttackArmySize = 4
        };
        var policy = new ModularSkirmishAiPolicy(config);
        var openingBuffer = new AiIntentBuffer(12);
        policy.Decide(OpeningObservation(config), openingBuffer);
        var openingIntents = openingBuffer.Intents.ToArray();
        var openingPlan = openingIntents.Any(value =>
                              value.Kind == AiIntentKind.Build &&
                              value.Building.Function == BuildingFunctionKind.Supply) &&
                          openingIntents.Any(value =>
                              value.Kind == AiIntentKind.Train &&
                              value.Recipe.UnitType.IsWorker) &&
                          openingIntents.Any(value =>
                              value.Kind == AiIntentKind.Gather) &&
                          openingIntents.SelectMany(value => value.Units)
                              .GroupBy(value => value)
                              .All(value => value.Count() == 1);
        foreach (var intent in openingIntents)
            policy.ObserveExecution(0, intent,
                new AiIntentExecutionResult(AiIntentExecutionCode.Success));

        var developedBuffer = new AiIntentBuffer(12);
        policy.Decide(DevelopedObservation(config, 720), developedBuffer);
        var developedIntents = developedBuffer.Intents.ToArray();
        var fullPlan = policy.Phase == AiStrategicPhase.Mobilizing &&
                       developedIntents.Any(value =>
                           value.Kind is AiIntentKind.AttackMove or
                               AiIntentKind.AttackUnit or
                               AiIntentKind.AttackBuilding) &&
                       developedIntents.Any(value =>
                           value.Kind == AiIntentKind.Research) &&
                       developedIntents.Any(value =>
                           value.Kind == AiIntentKind.Train &&
                           !value.Recipe.UnitType.IsWorker);

        var state = policy.CaptureState();
        var restored = new ModularSkirmishAiPolicy(config);
        restored.RestoreState(state, policy.StateFormatVersion);
        var originalNext = new AiIntentBuffer(12);
        var restoredNext = new AiIntentBuffer(12);
        var next = DevelopedObservation(config, 960);
        policy.Decide(next, originalNext);
        restored.Decide(next, restoredNext);
        var deterministic = Normalize(originalNext.Intents) ==
                            Normalize(restoredNext.Intents) &&
                            policy.CaptureState().SequenceEqual(restored.CaptureState());

        var truncatedRejected = false;
        try
        {
            restored.RestoreState(state.AsSpan(0, state.Length - 1),
                policy.StateFormatVersion);
        }
        catch (InvalidDataException)
        {
            truncatedRejected = true;
        }

        return new SelfTestResult(
            openingPlan && fullPlan && deterministic && truncatedRejected,
            $"opening={Normalize(openingBuffer.Intents)}, " +
            $"developed={Normalize(developedBuffer.Intents)}, " +
            $"phase={policy.Phase}, deterministic={deterministic}, " +
            $"state={state.Length}B, rejected={truncatedRejected}");
    }

    private static AiObservationSnapshot OpeningObservation(ModularAiConfig config)
    {
        var bounds = new SimRect(Vector2.Zero, new Vector2(1280f, 720f));
        var workers = Enumerable.Range(0, 4).Select(index =>
            new AiUnitSnapshot(
                index, new Vector2(180f + index * 16f, 360f), true,
                WorkerEconomyState.Idle, -1, null,
                UnitMoveMode.Arrived, CombatPhase.None))
            .ToArray();
        var commandCenter = config.Buildings.Type(DemoBuildingTypes.CommandCenter.Id);
        var facility = new AiFacilitySnapshot(
            new GameplayBuildingId(0), commandCenter, commandCenter.Function,
            BuildingLifecycleState.Completed,
            CenteredRect(new Vector2(220f, 360f), commandCenter.Size),
            -1, 0, 0);
        var view = new PlayerViewSnapshot(
            2, bounds, 32f, 40, 23,
            Enumerable.Repeat((byte)MapVisibility.Visible, 920).ToArray(),
            workers.Select(value => new PlayerUnitViewSnapshot(
                value.UnitId, 2, PlayerEntityRelation.Own, value.Position,
                8f, 100f, 100f, value.MoveMode, value.CombatPhase)).ToArray(),
            [new PlayerBuildingViewSnapshot(
                facility.BuildingId, 2, PlayerEntityRelation.Own,
                facility.Type, facility.Bounds, facility.State, 1f, 1500f, 1500f)],
            [
                new PlayerResourceViewSnapshot(
                    new EconomyResourceNodeId(0), EconomyResourceKind.Minerals,
                    new Vector2(390f, 330f), MapVisibility.Visible, 1500, true),
                new PlayerResourceViewSnapshot(
                    new EconomyResourceNodeId(1), EconomyResourceKind.VespeneGas,
                    new Vector2(390f, 430f), MapVisibility.Visible, 1500, false)
            ]);
        return new AiObservationSnapshot(
            0, 2, view, new PlayerEconomySnapshot(2, 500, 0, 14, 15),
            workers,
            [new EconomyBaseSnapshot(
                new EconomyBaseId(0), 2, facility.BuildingId,
                new EconomyDropOffId(0), new Vector2(220f, 360f), true,
                1, 1, 4, 6)],
            [facility],
            config.Technologies.Technologies.ToArray().Select(value =>
                new AiTechnologySnapshot(value, 0, false)).ToArray(),
            new MatchSnapshot(
                MatchPhase.Running, 0, -1, -1,
                [new PlayerCapabilitySnapshot(
                    2, MatchPlayerStatus.Active, true,
                    1, 1, 1, 0, 0, 4, 0)]));
    }

    private static AiObservationSnapshot DevelopedObservation(
        ModularAiConfig config,
        long tick)
    {
        var bounds = new SimRect(Vector2.Zero, new Vector2(1280f, 720f));
        var workers = Enumerable.Range(0, 8).Select(index =>
            new AiUnitSnapshot(
                index, new Vector2(180f + index * 12f, 360f), true,
                WorkerEconomyState.Gathering, 0, EconomyResourceKind.Minerals,
                UnitMoveMode.Arrived,
                CombatPhase.None));
        var army = Enumerable.Range(8, 5).Select(index =>
            new AiUnitSnapshot(
                index, new Vector2(360f + (index - 8) * 18f, 360f), false,
                WorkerEconomyState.None, -1, null, UnitMoveMode.Arrived,
                CombatPhase.Searching));
        var units = workers.Concat(army).ToArray();
        var types = new[]
        {
            config.Buildings.Type(DemoBuildingTypes.CommandCenter.Id),
            config.Buildings.Type(DemoBuildingTypes.SupplyDepot.Id),
            config.Buildings.Type(DemoBuildingTypes.Barracks.Id),
            config.Buildings.Type(DemoBuildingTypes.Refinery.Id),
            config.Buildings.Type(DemoBuildingTypes.Academy.Id)
        };
        var facilities = types.Select((type, index) =>
            new AiFacilitySnapshot(
                new GameplayBuildingId(index), type, type.Function,
                BuildingLifecycleState.Completed,
                CenteredRect(new Vector2(220f + index * 135f, 250f), type.Size),
                -1, 0, 0)).ToArray();
        var unitViews = units.Select(value => new PlayerUnitViewSnapshot(
            value.UnitId, 2, PlayerEntityRelation.Own, value.Position,
            8f, 100f, 100f, value.MoveMode, value.CombatPhase)).Append(
            new PlayerUnitViewSnapshot(
                20, 1, PlayerEntityRelation.Enemy, new Vector2(500f, 360f),
                8f, 100f, 100f, UnitMoveMode.Arrived, CombatPhase.Searching))
            .ToArray();
        var view = new PlayerViewSnapshot(
            2, bounds, 32f, 40, 23,
            Enumerable.Repeat((byte)MapVisibility.Explored, 920).ToArray(),
            unitViews,
            facilities.Select(value => new PlayerBuildingViewSnapshot(
                value.BuildingId, 2, PlayerEntityRelation.Own, value.Type,
                value.Bounds, value.State, 1f, 1000f, 1000f)).ToArray(),
            [new PlayerResourceViewSnapshot(
                new EconomyResourceNodeId(0), EconomyResourceKind.Minerals,
                new Vector2(390f, 330f), MapVisibility.Visible, 1200, true)]);
        return new AiObservationSnapshot(
            tick, 2, view, new PlayerEconomySnapshot(2, 900, 500, 10, 30),
            units,
            [new EconomyBaseSnapshot(
                new EconomyBaseId(0), 2, facilities[0].BuildingId,
                new EconomyDropOffId(0), new Vector2(220f, 360f), true,
                1, 1, 8, 8)],
            facilities,
            config.Technologies.Technologies.ToArray().Select(value =>
                new AiTechnologySnapshot(value, 0, false)).ToArray(),
            new MatchSnapshot(
                MatchPhase.Running, 0, -1, -1,
                [new PlayerCapabilitySnapshot(
                    2, MatchPlayerStatus.Active, true,
                    facilities.Length, facilities.Length, 1, 1, 1, 8, 5)]));
    }

    private static string Normalize(ReadOnlySpan<AiIntent> intents) =>
        string.Join('|', intents.ToArray().Select(value =>
            $"{value.Kind}:{string.Join(',', value.Units)}:" +
            $"B{value.Building.Id}:R{value.Recipe.Id}:T{value.Technology.Id}"));

    private static SimRect CenteredRect(Vector2 center, Vector2 size) =>
        new(center - size * 0.5f, center + size * 0.5f);
}
