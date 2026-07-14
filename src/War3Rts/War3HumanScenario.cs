using System.Numerics;
using RtsDemo.AI;
using RtsDemo.Simulation;

namespace War3Rts;

public sealed record War3HumanRuntime(
    RtsAiDirector AiDirector,
    Vector2 PlayerHome,
    Vector2 EnemyHome,
    int[] PlayerWorkers,
    int[] EnemyWorkers,
    EconomyResourceNodeId[] ResourceNodes);

/// <summary>Human-versus-Human fast-tech skirmish composition.</summary>
public static class War3HumanScenario
{
    public const float TreeHarvestSeconds = 4f;
    public const int LumberPerTrip = 10;
    public const int TreeHealth = 200;
    private static readonly Vector2 GoldHalfExtents = new(52f, 42f);
    private static readonly Vector2 TreeHalfExtents = new(13f, 13f);
    public const int PlayerId = 1;
    public const int EnemyId = 2;
    public const int InitialWorkers = 7;
    public const int Capacity = 512;

    public static readonly Vector2 PlayerHome = new(430f, 640f);
    public static readonly Vector2 EnemyHome = new(1970f, 640f);

    public static NavigationMapSnapshot CreateNavigation()
    {
        var obstacles = new List<SimRect>();
        AddBaseResourceObstacles(obstacles, PlayerHome, 1f);
        AddBaseResourceObstacles(obstacles, EnemyHome, -1f);
        AddCenterResourceObstacles(obstacles);
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(new Vector2(-100f, -50f), new Vector2(2500f, 1330f)),
            obstacles.ToArray(), [], [], [], out var snapshot, out var validation);
        if (!created || snapshot is null)
            throw new InvalidOperationException(
                $"war3_rts navigation is invalid: {validation.FirstError}.");
        return snapshot;
    }

    public static War3HumanRuntime Prepare(
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies)
    {
        simulation.Economy.Players.RegisterPlayer(
            PlayerId, minerals: 1_250, vespeneGas: 700,
            supplyCapacity: 24, supplyUsed: InitialWorkers);
        simulation.Economy.Players.RegisterPlayer(
            EnemyId, minerals: 1_250, vespeneGas: 700,
            supplyCapacity: 24, supplyUsed: InitialWorkers);

        var resourceNodes = new List<EconomyResourceNodeId>();
        var playerResources = AddResources(simulation, PlayerHome, 1f, resourceNodes);
        var enemyResources = AddResources(simulation, EnemyHome, -1f, resourceNodes);
        AddCenterResources(simulation, resourceNodes);

        var worker = production.UnitType(War3HumanContent.Peasant);
        var playerWorkers = SpawnWorkers(simulation, worker, PlayerId, PlayerHome, 1f);
        var enemyWorkers = SpawnWorkers(simulation, worker, EnemyId, EnemyHome, -1f);

        simulation.StartMatch([PlayerId, EnemyId]);
        CompleteStartingBase(simulation, buildings, PlayerId, PlayerHome, 1f,
            playerWorkers[0]);
        CompleteStartingBase(simulation, buildings, EnemyId, EnemyHome, -1f,
            enemyWorkers[0]);

        AssignWorkers(simulation, PlayerId, playerWorkers, playerResources);
        AssignWorkers(simulation, EnemyId, enemyWorkers, enemyResources);

        var adapter = new RtsSimulationAiAdapter(simulation, technologies);
        var director = new RtsAiDirector(adapter, adapter);
        var profile = new AiDifficultyProfile(
            0, "Human Alliance AI", TargetWorkers: 14, AttackArmySize: 8,
            MaximumIntentsPerDecision: 12, SupplyBuffer: 4,
            DecisionIntervalTicks: 12, ScoutIntervalTicks: 300,
            AttackIntervalTicks: 180, DefenseRadius: 390f);
        director.Register(
            EnemyId,
            new ModularSkirmishAiPolicy(ModularAiConfig.FromProfile(
                buildings, production, technologies, profile)),
            profile.DecisionIntervalTicks,
            decisionOffsetTicks: 4,
            profile.MaximumIntentsPerDecision);

        return new War3HumanRuntime(
            director, PlayerHome, EnemyHome, playerWorkers, enemyWorkers,
            resourceNodes.ToArray());
    }

    private static ResourceCluster AddResources(
        RtsSimulation simulation,
        Vector2 home,
        float direction,
        List<EconomyResourceNodeId> all)
    {
        var goldPosition = BaseGoldPosition(home, direction);
        var gold = simulation.Economy.AddResourceNode(
            EconomyResourceKind.Minerals,
            goldPosition,
            amount: 32_000, harvestBatch: 10, harvestSeconds: 1.05f,
            harvesterCapacity: 5, activeHarvesterSlots: 5);
        simulation.Economy.SetResourceInteractionBounds(
            gold, ResourceBounds(goldPosition, GoldHalfExtents));
        all.Add(gold);
        var trees = new EconomyResourceNodeId[14];
        for (var index = 0; index < trees.Length; index++)
        {
            var position = BaseTreePosition(home, direction, index);
            trees[index] = simulation.Economy.AddResourceNode(
                EconomyResourceKind.VespeneGas, position,
                amount: TreeHealth, harvestBatch: LumberPerTrip,
                harvestSeconds: TreeHarvestSeconds,
                harvesterCapacity: 1, requiresRefinery: false,
                operational: true, activeHarvesterSlots: 1,
                harvestMode: EconomyHarvestMode.Progressive);
            simulation.Economy.SetResourceInteractionBounds(
                trees[index], ResourceBounds(position, TreeHalfExtents));
            all.Add(trees[index]);
        }
        return new ResourceCluster(gold, trees);
    }

    private static void AddCenterResources(
        RtsSimulation simulation,
        List<EconomyResourceNodeId> all)
    {
        foreach (var x in new[] { 980f, 1420f })
        {
            var goldPosition = new Vector2(x, 640f);
            var gold = simulation.Economy.AddResourceNode(
                EconomyResourceKind.Minerals, goldPosition,
                25_000, 10, 1.05f, 5, activeHarvesterSlots: 5);
            simulation.Economy.SetResourceInteractionBounds(
                gold, ResourceBounds(goldPosition, GoldHalfExtents));
            all.Add(gold);
            for (var index = 0; index < 8; index++)
            {
                var position = CenterTreePosition(x, index);
                var tree = simulation.Economy.AddResourceNode(
                    EconomyResourceKind.VespeneGas,
                    position, TreeHealth, LumberPerTrip, TreeHarvestSeconds, 1,
                    activeHarvesterSlots: 1,
                    harvestMode: EconomyHarvestMode.Progressive);
                simulation.Economy.SetResourceInteractionBounds(
                    tree, ResourceBounds(position, TreeHalfExtents));
                all.Add(tree);
            }
        }
    }

    private static void AddBaseResourceObstacles(
        List<SimRect> obstacles,
        Vector2 home,
        float direction)
    {
        obstacles.Add(ResourceBounds(
            BaseGoldPosition(home, direction), GoldHalfExtents));
        for (var index = 0; index < 14; index++)
            obstacles.Add(ResourceBounds(
                BaseTreePosition(home, direction, index), TreeHalfExtents));
    }

    private static void AddCenterResourceObstacles(List<SimRect> obstacles)
    {
        foreach (var x in new[] { 980f, 1420f })
        {
            obstacles.Add(ResourceBounds(new Vector2(x, 640f), GoldHalfExtents));
            for (var index = 0; index < 8; index++)
                obstacles.Add(ResourceBounds(
                    CenterTreePosition(x, index), TreeHalfExtents));
        }
    }

    private static Vector2 BaseGoldPosition(Vector2 home, float direction) =>
        home + new Vector2(direction * 235f, 0f);

    private static Vector2 BaseTreePosition(
        Vector2 home,
        float direction,
        int index)
    {
        var row = index / 7;
        var column = index % 7;
        return home + new Vector2(
            direction * (column - 3f) * 44f,
            row == 0 ? -300f : 300f);
    }

    private static Vector2 CenterTreePosition(float x, int index)
    {
        var angle = MathF.Tau * index / 8f;
        return new Vector2(x, 640f) +
               new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 150f;
    }

    private static SimRect ResourceBounds(Vector2 center, Vector2 halfExtents) =>
        new(center - halfExtents, center + halfExtents);

    private static int[] SpawnWorkers(
        RtsSimulation simulation,
        UnitTypeProfile worker,
        int player,
        Vector2 home,
        float direction)
    {
        var output = new int[InitialWorkers];
        for (var index = 0; index < output.Length; index++)
        {
            var position = home + new Vector2(
                direction * (105f + (index / 4) * 26f),
                (index % 4 - 1.5f) * 30f);
            output[index] = simulation.AddUnit(
                position, worker.Movement, player, worker.Combat);
            simulation.Economy.RegisterWorker(output[index], player);
        }
        return output;
    }

    private static void CompleteStartingBase(
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        int player,
        Vector2 home,
        float direction,
        int builder)
    {
        (int Type, Vector2 Offset)[] layout =
        [
            (War3HumanContent.TownHall, Vector2.Zero),
            (War3HumanContent.Farm, new Vector2(-direction * 115f, -205f)),
            (War3HumanContent.Farm, new Vector2(-direction * 115f, 205f)),
            (War3HumanContent.Barracks, new Vector2(-direction * 245f, -150f)),
            (War3HumanContent.Blacksmith, new Vector2(-direction * 250f, 0f)),
            (War3HumanContent.AltarOfKings, new Vector2(-direction * 245f, 150f)),
            (War3HumanContent.ArcaneSanctum, new Vector2(-direction * 390f, -185f)),
            (War3HumanContent.Workshop, new Vector2(-direction * 405f, -25f)),
            (War3HumanContent.GryphonAviary, new Vector2(-direction * 390f, 155f))
        ];
        foreach (var item in layout)
            CompleteBuilding(simulation, buildings.Type(item.Type), player,
                home + item.Offset, builder);
    }

    private static void CompleteBuilding(
        RtsSimulation simulation,
        BuildingTypeProfile building,
        int player,
        Vector2 center,
        int builder)
    {
        var setup = building with
        {
            Cost = default,
            BuildSeconds = 0.03f,
            ConstructionMethod = ConstructionMethodKind.StartAndRelease
        };
        var result = simulation.IssueConstruction(player, builder, setup, center);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Starting {building.Name} failed: {result.Code}/{result.PlacementCode}");
        for (var tick = 0; tick < 720 &&
             simulation.Construction.Observe(result.BuildingId).State !=
             BuildingLifecycleState.Completed; tick++)
            simulation.Tick(1f / 60f);
        if (simulation.Construction.Observe(result.BuildingId).State !=
            BuildingLifecycleState.Completed)
        {
            var snapshot = simulation.Construction.Observe(result.BuildingId);
            throw new InvalidOperationException(
                $"Starting {building.Name} did not complete: " +
                $"state={snapshot.State} progress={snapshot.Progress:0.00} " +
                $"builder={snapshot.BuilderUnit}.");
        }
    }

    private static void AssignWorkers(
        RtsSimulation simulation,
        int player,
        int[] workers,
        ResourceCluster resources)
    {
        for (var index = 0; index < workers.Length; index++)
        {
            var node = index < 5
                ? resources.Gold
                : resources.Trees[index - 5];
            var result = simulation.IssueGather(player, workers[index], node);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Initial Human gathering failed: {result.Code}.");
        }
    }

    private readonly record struct ResourceCluster(
        EconomyResourceNodeId Gold,
        EconomyResourceNodeId[] Trees);
}
