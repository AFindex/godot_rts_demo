using System.Numerics;
using RtsDemo.AI;
using RtsDemo.Simulation;
using War3Rts.Maps;
using War3Rts.Pcg;

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
    public const float TerrainCellSize = 32f;
    public const float TerrainCliffHeight = 48f;

    public static readonly SimRect WorldBounds = new(
        Vector2.Zero, new Vector2(6_400f, 3_840f));
    public static readonly Vector2 PlayerHome = new(1_280f, 1_920f);
    public static readonly Vector2 EnemyHome = new(5_120f, 1_920f);
    private static readonly War3BattlefieldPcgLayout BattlefieldPcg =
        War3BattlefieldPcg.Generate(WorldBounds, PlayerHome, EnemyHome);

    public static int PcgTreeCount =>
        BattlefieldPcg.ForestTreePositions.Length;
    public static int ExpectedResourceNodeCount =>
        2 * 15 + BattlefieldPcg.NeutralGoldPositions.Length + PcgTreeCount;
    public static string PcgHashText => BattlefieldPcg.StableHashText;
    public static int DensePcgTreeCount => BattlefieldPcg.DenseTreeCount;
    public static int SparsePcgTreeCount => BattlefieldPcg.SparseTreeCount;

    public static TerrainMapSnapshot CreateTerrain() =>
        LoadDefaultMap().Terrain;

    public static NavigationMapSnapshot CreateNavigation()
        => LoadDefaultMap().CreateNavigation();

    public static NavigationMapSnapshot CreateNavigation(War3MapRuntime map)
        => map.CreateNavigation();

    public static War3MapRuntime LoadDefaultMap()
    {
        var entry = War3MapCatalog.Enumerate()
            .FirstOrDefault(value => value.Manifest.Id == War3MapCodec.DefaultMapId);
        if (entry is not null &&
            War3MapCatalog.TryLoadRuntime(entry, out var runtime, out _) &&
            runtime is not null)
            return runtime;
        var asset = War3MapCodec.CreateBuiltInDefaultAsset();
        if (!War3MapCodec.TryExpand(asset, out runtime, out var validation) ||
            runtime is null)
            throw new InvalidOperationException(
                $"Built-in War3 map is invalid: {validation.Summary}");
        return runtime;
    }

    public static War3HumanRuntime Prepare(
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies) => Prepare(
        simulation, buildings, production, technologies, LoadDefaultMap());

    public static War3HumanRuntime Prepare(
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies,
        War3MapRuntime map)
    {
        simulation.Economy.Players.RegisterPlayer(
            PlayerId, minerals: 1_250, vespeneGas: 700,
            supplyCapacity: 24, supplyUsed: InitialWorkers);
        simulation.Economy.Players.RegisterPlayer(
            EnemyId, minerals: 1_250, vespeneGas: 700,
            supplyCapacity: 24, supplyUsed: InitialWorkers);

        var resourceNodes = new List<EconomyResourceNodeId>();
        var clusters = AddMapResources(simulation, map, resourceNodes);
        var playerResources = clusters[PlayerId];
        var enemyResources = clusters[EnemyId];

        var playerHome = map.PlayerSpawn;
        var enemyHome = map.EnemySpawn;
        var playerDirection = MathF.Sign(enemyHome.X - playerHome.X);
        if (playerDirection == 0) playerDirection = 1;
        var enemyDirection = -playerDirection;

        var worker = production.UnitType(War3HumanContent.Peasant);
        var playerWorkers = SpawnWorkers(
            simulation, worker, PlayerId, playerHome, playerDirection);
        var enemyWorkers = SpawnWorkers(
            simulation, worker, EnemyId, enemyHome, enemyDirection);

        simulation.CombatWeaponUpgradeTechnologyId = 0;
        simulation.CombatBuildingArmorTechnologyId = 2;
        simulation.StartMatch([PlayerId, EnemyId]);
        CompleteStartingBase(simulation, buildings, PlayerId,
            playerHome, playerDirection,
            playerWorkers[0]);
        CompleteStartingBase(simulation, buildings, EnemyId,
            enemyHome, enemyDirection,
            enemyWorkers[0]);

        AssignWorkers(simulation, PlayerId, playerWorkers, playerResources);
        AssignWorkers(simulation, EnemyId, enemyWorkers, enemyResources);

        return new War3HumanRuntime(
            CreateAiDirector(
                simulation, buildings, production, technologies),
            playerHome, enemyHome, playerWorkers, enemyWorkers,
            resourceNodes.ToArray());
    }

    internal static War3HumanRuntime RestoreRuntime(
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies,
        War3MapRuntime map,
        int[] playerWorkers,
        int[] enemyWorkers,
        int[] resourceNodeIds)
    {
        var restoredPlayerWorkers = playerWorkers.ToArray();
        var restoredEnemyWorkers = enemyWorkers.ToArray();
        if (restoredPlayerWorkers.Length != InitialWorkers ||
            restoredEnemyWorkers.Length != InitialWorkers)
        {
            throw new InvalidDataException(
                "War3 bootstrap worker counts do not match the scenario contract.");
        }

        var seenWorkers = new HashSet<int>();
        ValidateWorkers(
            simulation, restoredPlayerWorkers, PlayerId, seenWorkers);
        ValidateWorkers(
            simulation, restoredEnemyWorkers, EnemyId, seenWorkers);
        if (resourceNodeIds.Length != simulation.Economy.ResourceNodeCount)
        {
            throw new InvalidDataException(
                "War3 bootstrap resource-node count does not match the simulation.");
        }
        var resourceNodes = new EconomyResourceNodeId[resourceNodeIds.Length];
        for (var index = 0; index < resourceNodes.Length; index++)
        {
            if (resourceNodeIds[index] != index)
            {
                throw new InvalidDataException(
                    "War3 bootstrap resource-node IDs must be dense and ordered.");
            }
            resourceNodes[index] = new EconomyResourceNodeId(index);
        }

        return new War3HumanRuntime(
            CreateAiDirector(
                simulation, buildings, production, technologies),
            map.PlayerSpawn,
            map.EnemySpawn,
            restoredPlayerWorkers,
            restoredEnemyWorkers,
            resourceNodes);
    }

    private static void ValidateWorkers(
        RtsSimulation simulation,
        ReadOnlySpan<int> workers,
        int playerId,
        HashSet<int> seen)
    {
        for (var index = 0; index < workers.Length; index++)
        {
            var worker = workers[index];
            if (!seen.Add(worker) ||
                (uint)worker >= (uint)simulation.Units.Count ||
                !simulation.Units.Alive[worker] ||
                !simulation.Economy.IsWorkerOwnedBy(worker, playerId))
            {
                throw new InvalidDataException(
                    $"War3 bootstrap worker {worker} is invalid for player {playerId}.");
            }
        }
    }

    private static RtsAiDirector CreateAiDirector(
        RtsSimulation simulation,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies)
    {
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
        return director;
    }

    private static Dictionary<int, ResourceCluster> AddMapResources(
        RtsSimulation simulation,
        War3MapRuntime map,
        List<EconomyResourceNodeId> all)
    {
        var golds = new Dictionary<int, EconomyResourceNodeId>();
        var trees = new Dictionary<int, List<EconomyResourceNodeId>>
        {
            [PlayerId] = [],
            [EnemyId] = []
        };
        foreach (var resource in map.Resources)
        {
            EconomyResourceNodeId id;
            if (resource.Kind == War3MapObjectKind.GoldMine)
            {
                id = simulation.Economy.AddResourceNode(
                    EconomyResourceKind.Minerals,
                    resource.Position,
                    resource.Amount > 0 ? resource.Amount : 25_000,
                    10, 1.05f, 5, activeHarvesterSlots: 5);
                if (resource.OwnerSlot is PlayerId or EnemyId)
                    golds[resource.OwnerSlot] = id;
            }
            else
            {
                id = simulation.Economy.AddResourceNode(
                    EconomyResourceKind.VespeneGas,
                    resource.Position,
                    resource.Amount > 0 ? resource.Amount : TreeHealth,
                    LumberPerTrip,
                    TreeHarvestSeconds,
                    1,
                    requiresRefinery: false,
                    operational: true,
                    activeHarvesterSlots: 1,
                    harvestMode: EconomyHarvestMode.Progressive);
                if (resource.OwnerSlot is PlayerId or EnemyId)
                    trees[resource.OwnerSlot].Add(id);
            }
            simulation.Economy.SetResourceInteractionBounds(id, resource.Bounds);
            all.Add(id);
        }
        foreach (var player in new[] { PlayerId, EnemyId })
        {
            if (!golds.ContainsKey(player) || trees[player].Count < 2)
                throw new InvalidDataException(
                    $"Map '{map.Metadata.Id}' requires one owned gold mine and " +
                    $"at least two owned trees for slot {player}.");
        }
        return new Dictionary<int, ResourceCluster>
        {
            [PlayerId] = new(golds[PlayerId], [.. trees[PlayerId]]),
            [EnemyId] = new(golds[EnemyId], [.. trees[EnemyId]])
        };
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

    private static void AddNeutralGolds(
        RtsSimulation simulation,
        List<EconomyResourceNodeId> all)
    {
        foreach (var goldPosition in BattlefieldPcg.NeutralGoldPositions)
        {
            var gold = simulation.Economy.AddResourceNode(
                EconomyResourceKind.Minerals, goldPosition,
                25_000, 10, 1.05f, 5, activeHarvesterSlots: 5);
            simulation.Economy.SetResourceInteractionBounds(
                gold, ResourceBounds(goldPosition, GoldHalfExtents));
            all.Add(gold);
        }
    }

    private static void AddPcgForest(
        RtsSimulation simulation,
        List<EconomyResourceNodeId> all)
    {
        foreach (var position in BattlefieldPcg.ForestTreePositions)
        {
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

    private static void AddNeutralGoldObstacles(List<SimRect> obstacles)
    {
        foreach (var goldPosition in BattlefieldPcg.NeutralGoldPositions)
            obstacles.Add(ResourceBounds(goldPosition, GoldHalfExtents));
    }

    private static void AddPcgForestObstacles(List<SimRect> obstacles)
    {
        foreach (var position in BattlefieldPcg.ForestTreePositions)
            obstacles.Add(ResourceBounds(position, TreeHalfExtents));
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
        var axialJitter =
            (PcgHashNoise.Value01(index * 1.37f, row * 3.11f, 0xBA53_7EEDu) -
             0.5f) * 12f;
        var woodlineDistance = 274f +
            PcgHashNoise.Value01(index * 0.91f + 7f, row * 2.43f,
                0x71EE_2026u) * 70f;
        return home + new Vector2(
            direction * ((column - 3f) * 46f + axialJitter),
            (row == 0 ? -1f : 1f) * woodlineDistance);
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
                position, worker.Movement, player, worker.Combat,
                worker.Perception);
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
