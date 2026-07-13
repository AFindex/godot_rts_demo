using System.Numerics;
using RtsDemo.AI;
using RtsDemo.Simulation;

namespace RtsDemo.Scenarios;

public sealed record PlayableSkirmishRuntime(
    RtsAiDirector AiDirector,
    Vector2 PlayerHome,
    int[] PlayerWorkers,
    int[] EnemyWorkers,
    EconomyResourceNodeId[] ResourceNodes);

/// <summary>
/// Pure C# composition root for the player-facing skirmish. It owns scenario
/// data and setup only; all gameplay is executed by the production simulation.
/// </summary>
public static class PlayableSkirmishScenario
{
    public const int PlayerId = 1;
    public const int EnemyId = 2;
    public const int InitialWorkersPerSide = 12;
    public const int SimulationCapacity = 768;

    public static readonly Vector2 PlayerHome = new(420f, 900f);
    public static readonly Vector2 EnemyHome = new(2780f, 900f);

    private static readonly NavigationMapSnapshot Navigation = BuildNavigation();

    public static NavigationMapSnapshot CreateNavigationSnapshot() => Navigation;

    public static PlayableSkirmishRuntime Prepare(
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(technologies);

        simulation.Economy.Players.RegisterPlayer(
            PlayerId, minerals: 1_800, vespeneGas: 600,
            supplyCapacity: 30, supplyUsed: InitialWorkersPerSide);
        simulation.Economy.Players.RegisterPlayer(
            EnemyId, minerals: 1_800, vespeneGas: 600,
            supplyCapacity: 30, supplyUsed: InitialWorkersPerSide);

        var resourceNodes = new List<EconomyResourceNodeId>(64);
        var playerMainMinerals = AddResourceCluster(
            simulation, PlayerHome, facing: Vector2.UnitX,
            mineralCount: 10, mineralAmount: 8_000, resourceNodes);
        var enemyMainMinerals = AddResourceCluster(
            simulation, EnemyHome, facing: -Vector2.UnitX,
            mineralCount: 10, mineralAmount: 8_000, resourceNodes);

        AddResourceCluster(simulation, new Vector2(900f, 430f),
            new Vector2(-0.65f, 0.75f), 9, 7_000, resourceNodes);
        AddResourceCluster(simulation, new Vector2(2300f, 1370f),
            new Vector2(0.65f, -0.75f), 9, 7_000, resourceNodes);
        AddResourceCluster(simulation, new Vector2(900f, 1370f),
            new Vector2(-0.65f, -0.75f), 8, 6_500, resourceNodes);
        AddResourceCluster(simulation, new Vector2(2300f, 430f),
            new Vector2(0.65f, 0.75f), 8, 6_500, resourceNodes);
        AddCentralRichResources(simulation, resourceNodes);

        var workerType = production.UnitTypes.ToArray()
            .Single(value => value.IsWorker);
        var playerWorkers = SpawnWorkers(
            simulation, workerType, PlayerId, PlayerHome);
        var enemyWorkers = SpawnWorkers(
            simulation, workerType, EnemyId, EnemyHome);

        simulation.StartMatch([PlayerId, EnemyId]);
        CompleteStartingTownHall(
            simulation, buildings.Type(DemoBuildingTypes.CommandCenter.Id),
            PlayerId, PlayerHome, playerWorkers[0]);
        CompleteStartingTownHall(
            simulation, buildings.Type(DemoBuildingTypes.CommandCenter.Id),
            EnemyId, EnemyHome, enemyWorkers[0]);

        AssignInitialMinerals(
            simulation, PlayerId, playerWorkers, playerMainMinerals);
        AssignInitialMinerals(
            simulation, EnemyId, enemyWorkers, enemyMainMinerals);

        var adapter = new RtsSimulationAiAdapter(simulation, technologies);
        var director = new RtsAiDirector(adapter, adapter);
        var enemyProfile = new AiDifficultyProfile(
            0, "Playable Enemy", TargetWorkers: 24, AttackArmySize: 10,
            MaximumIntentsPerDecision: 12, SupplyBuffer: 4,
            DecisionIntervalTicks: 10, ScoutIntervalTicks: 300,
            AttackIntervalTicks: 180, DefenseRadius: 460f);
        director.Register(
            EnemyId,
            new ModularSkirmishAiPolicy(ModularAiConfig.FromProfile(
                buildings, production, technologies, enemyProfile)),
            enemyProfile.DecisionIntervalTicks,
            decisionOffsetTicks: 3,
            enemyProfile.MaximumIntentsPerDecision);

        return new PlayableSkirmishRuntime(
            director, PlayerHome, playerWorkers, enemyWorkers,
            resourceNodes.ToArray());
    }

    private static EconomyResourceNodeId[] AddResourceCluster(
        RtsSimulation simulation,
        Vector2 baseCenter,
        Vector2 facing,
        int mineralCount,
        int mineralAmount,
        List<EconomyResourceNodeId> allNodes)
    {
        facing = Vector2.Normalize(facing);
        var tangent = new Vector2(-facing.Y, facing.X);
        var minerals = new EconomyResourceNodeId[mineralCount];
        for (var index = 0; index < mineralCount; index++)
        {
            var row = index / 5;
            var column = index % 5;
            var position = baseCenter + facing * (195f + row * 42f) +
                           tangent * ((column - 2f) * 52f);
            minerals[index] = simulation.Economy.AddResourceNode(
                EconomyResourceKind.Minerals, position, mineralAmount,
                harvestBatch: 6, harvestSeconds: 0.55f,
                harvesterCapacity: 2);
            allNodes.Add(minerals[index]);
        }

        for (var side = -1; side <= 1; side += 2)
        {
            var gasPosition = baseCenter + facing * 120f +
                              tangent * (side * 205f);
            allNodes.Add(simulation.Economy.AddResourceNode(
                EconomyResourceKind.VespeneGas, gasPosition, 6_000,
                harvestBatch: 5, harvestSeconds: 0.7f,
                harvesterCapacity: 3, requiresRefinery: true,
                operational: false));
        }
        return minerals;
    }

    private static void AddCentralRichResources(
        RtsSimulation simulation,
        List<EconomyResourceNodeId> allNodes)
    {
        Vector2[] centers = [new(1320f, 900f), new(1880f, 900f)];
        foreach (var center in centers)
        {
            for (var index = 0; index < 6; index++)
            {
                var position = center + new Vector2(
                    (index % 2) * 54f - 27f,
                    (index / 2 - 1) * 58f);
                allNodes.Add(simulation.Economy.AddResourceNode(
                    EconomyResourceKind.Minerals, position, 10_000,
                    harvestBatch: 8, harvestSeconds: 0.5f,
                    harvesterCapacity: 2));
            }
        }
    }

    private static int[] SpawnWorkers(
        RtsSimulation simulation,
        UnitTypeProfile workerType,
        int playerId,
        Vector2 home)
    {
        var workers = new int[InitialWorkersPerSide];
        for (var index = 0; index < workers.Length; index++)
        {
            var row = index / 6;
            var column = index % 6;
            var direction = playerId == PlayerId ? -1f : 1f;
            var position = home + new Vector2(
                direction * (105f + row * 24f),
                (column - 2.5f) * 26f);
            workers[index] = simulation.AddUnit(
                position, workerType.Movement, playerId, workerType.Combat);
            simulation.Economy.RegisterWorker(workers[index], playerId);
        }
        return workers;
    }

    private static void CompleteStartingTownHall(
        RtsSimulation simulation,
        BuildingTypeProfile commandCenter,
        int playerId,
        Vector2 center,
        int builder)
    {
        var setupProfile = commandCenter with
        {
            Cost = default,
            BuildSeconds = 0.05f,
            ConstructionMethod = ConstructionMethodKind.StartAndRelease
        };
        var result = simulation.IssueConstruction(
            playerId, builder, setupProfile, center);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Starting town hall placement failed for P{playerId}: " +
                $"{result.Code}/{result.PlacementCode}.");
        }
        for (var tick = 0; tick < 8; tick++) simulation.Tick(1f / 60f);
        if (simulation.Construction.Observe(result.BuildingId).State !=
            BuildingLifecycleState.Completed)
        {
            throw new InvalidOperationException(
                $"Starting town hall did not complete for P{playerId}.");
        }
    }

    private static void AssignInitialMinerals(
        RtsSimulation simulation,
        int playerId,
        int[] workers,
        EconomyResourceNodeId[] minerals)
    {
        for (var index = 0; index < workers.Length; index++)
        {
            var result = simulation.IssueGather(
                playerId, workers[index], minerals[index % minerals.Length]);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Initial gather failed for P{playerId}: {result.Code}.");
        }
    }

    private static NavigationMapSnapshot BuildNavigation()
    {
        PortalNode[] portals =
        [
            new(0, new Vector2(1320f, 590f), "West upper lane"),
            new(1, new Vector2(1880f, 590f), "East upper lane"),
            new(2, new Vector2(1320f, 1210f), "West lower lane"),
            new(3, new Vector2(1880f, 1210f), "East lower lane")
        ];
        PortalEdge[] edges =
        [
            new(0, 1, 180f, ChokeId: 0),
            new(2, 3, 180f, ChokeId: 1),
            new(0, 2, 420f),
            new(1, 3, 420f)
        ];
        ChokeDefinition[] chokes =
        [
            new(0, portals[0].Position, portals[1].Position,
                Width: 180f, ApproachDistance: 210f),
            new(1, portals[2].Position, portals[3].Position,
                Width: 180f, ApproachDistance: 210f)
        ];
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(new Vector2(40f), new Vector2(3160f, 1760f)),
            [
                new SimRect(new Vector2(1480f, 80f), new Vector2(1720f, 500f)),
                new SimRect(new Vector2(1480f, 680f), new Vector2(1720f, 1120f)),
                new SimRect(new Vector2(1480f, 1300f), new Vector2(1720f, 1720f)),
                new SimRect(new Vector2(1060f, 720f), new Vector2(1190f, 1080f)),
                new SimRect(new Vector2(2010f, 720f), new Vector2(2140f, 1080f))
            ],
            portals, edges, chokes,
            out var snapshot, out var validation);
        if (!created || snapshot is null)
        {
            throw new InvalidOperationException(
                $"Playable skirmish navigation is invalid: " +
                $"{validation.FirstError}.");
        }
        return snapshot;
    }
}
