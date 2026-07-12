using System.Numerics;
using RtsDemo.AI;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public sealed record AiEncounterResourceDefinition(
    TestEconomyResourceKind Kind,
    Vector2 Position,
    int Amount,
    int HarvestBatch,
    float HarvestSeconds,
    int HarvesterCapacity,
    bool RequiresRefinery = false,
    bool Operational = true);

public sealed record AiEncounterSideDefinition(
    int PlayerId,
    int Minerals,
    int VespeneGas,
    int SupplyCapacity,
    Vector2 TownHallPosition,
    Vector2[] WorkerPositions,
    int[] InitialMineralIndices,
    AiDifficultyProfile AiProfile,
    int DecisionOffsetTicks);

public sealed record AiEncounterLevelDefinition(
    string Id,
    string DisplayName,
    NavigationMapSnapshot Navigation,
    AiEncounterResourceDefinition[] Resources,
    AiEncounterSideDefinition[] Sides,
    BuildingTypeProfile StartingTownHall,
    int DurationTicks,
    int AiAttachTick,
    float AiBuildingSeconds)
{
    public static AiEncounterLevelDefinition CreateContinuousBattle(
        AiConfigurationCatalogSnapshot configurations)
    {
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                new SimRect(new Vector2(20f, 40f), new Vector2(1480f, 810f)),
                [
                    new SimRect(new Vector2(690f, 40f), new Vector2(810f, 315f)),
                    new SimRect(new Vector2(690f, 535f), new Vector2(810f, 810f))
                ],
                [], [], [],
                out var navigation,
                out var validation) || navigation is null)
            throw new InvalidOperationException(
                $"AI encounter navigation is invalid: {validation.FirstError}");

        AiEncounterResourceDefinition[] resources =
        [
            new(TestEconomyResourceKind.Minerals,
                new Vector2(330f, 300f), 12000, 6, 0.55f, 3),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(350f, 425f), 12000, 6, 0.55f, 3),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(330f, 550f), 12000, 6, 0.55f, 3),
            new(TestEconomyResourceKind.VespeneGas,
                new Vector2(250f, 660f), 10000, 5, 0.60f, 3,
                RequiresRefinery: true, Operational: false),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(420f, 600f), 10000, 6, 0.58f, 3),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(450f, 700f), 10000, 6, 0.58f, 3),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(1170f, 300f), 12000, 6, 0.55f, 3),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(1150f, 425f), 12000, 6, 0.55f, 3),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(1170f, 550f), 12000, 6, 0.55f, 3),
            new(TestEconomyResourceKind.VespeneGas,
                new Vector2(1250f, 190f), 10000, 5, 0.60f, 3,
                RequiresRefinery: true, Operational: false),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(1080f, 600f), 10000, 6, 0.58f, 3),
            new(TestEconomyResourceKind.Minerals,
                new Vector2(1050f, 700f), 10000, 6, 0.58f, 3)
        ];

        var standard = configurations.Profile(0) with
        {
            TargetWorkers = 10,
            AttackArmySize = 2,
            AttackIntervalTicks = 180,
            ScoutIntervalTicks = 180
        };
        var aggressive = configurations.Profile(1) with
        {
            TargetWorkers = 10,
            AttackArmySize = 2,
            AttackIntervalTicks = 120,
            ScoutIntervalTicks = 150
        };
        AiEncounterSideDefinition[] sides =
        [
            new(
                1, 4000, 1200, 15,
                new Vector2(170f, 425f),
                Enumerable.Range(0, 6).Select(index =>
                    index == 5
                        ? new Vector2(400f, 700f)
                        : new Vector2(
                            80f + index % 3 * 26f,
                            index < 3 ? 320f : 530f)).ToArray(),
                [0, 1, 2],
                standard,
                0),
            new(
                2, 4000, 1200, 15,
                new Vector2(1330f, 425f),
                Enumerable.Range(0, 6).Select(index =>
                    index == 5
                        ? new Vector2(1100f, 700f)
                        : new Vector2(
                            1420f - index % 3 * 26f,
                            index < 3 ? 320f : 530f)).ToArray(),
                [6, 7, 8],
                aggressive,
                5)
        ];
        return new AiEncounterLevelDefinition(
            "ai-continuous-encounter",
            "Two-AI continuous development and encounter battle",
            navigation,
            resources,
            sides,
            DemoBuildingTypes.CommandCenter with
            {
                BuildSeconds = 0.5f,
                MaximumHealth = 12000f,
                SupplyProvided = 0
            },
            DurationTicks: 3600,
            AiAttachTick: 60,
            AiBuildingSeconds: 1.2f);
    }
}

public readonly record struct AiEncounterDeployment(
    int[][] Workers,
    int[] ResourceNodes,
    int[] StartingTownHalls);

public readonly record struct AiEncounterSideSnapshot(
    int PlayerId,
    bool Established,
    int Minerals,
    int VespeneGas,
    int Workers,
    int CombatUnits,
    int TownHalls,
    int SupplyDepots,
    int Barracks,
    int Refineries,
    int Academies,
    int WeaponLevel,
    int AssaultLevel,
    int FortificationLevel,
    TestAiStrategicPhase AiPhase,
    long LastAttackTick);

public readonly record struct AiEncounterCombatSignal(
    ulong Sequence,
    CombatEventKind Kind,
    int AttackerPlayerId);

public interface IAiEncounterLevelRuntime
{
    void RegisterPlayer(int playerId, int minerals, int gas, int supply, int used);
    int SpawnWorker(Vector2 position, int playerId);
    int AddResource(AiEncounterResourceDefinition resource);
    void StartMatch(int[] playerIds);
    int BuildStartingTownHall(
        int playerId,
        int worker,
        BuildingTypeProfile profile,
        Vector2 position);
    bool Gather(int playerId, int worker, int resourceNode);
    void AttachAi(
        int playerId,
        AiDifficultyProfile profile,
        float buildingSeconds,
        int decisionOffsetTicks);
    AiEncounterSideSnapshot ObserveSide(int playerId);
    AiEncounterCombatSignal[] ObserveCombatSignals(ulong afterSequence);
}

public static class AiEncounterLevelOrchestrator
{
    public static AiEncounterDeployment Prepare(
        AiEncounterLevelDefinition level,
        IAiEncounterLevelRuntime runtime)
    {
        var resources = level.Resources.Select(runtime.AddResource).ToArray();
        var workers = new int[level.Sides.Length][];
        for (var sideIndex = 0; sideIndex < level.Sides.Length; sideIndex++)
        {
            var side = level.Sides[sideIndex];
            runtime.RegisterPlayer(
                side.PlayerId,
                side.Minerals,
                side.VespeneGas,
                side.SupplyCapacity,
                side.WorkerPositions.Length);
            workers[sideIndex] = side.WorkerPositions.Select(position =>
                runtime.SpawnWorker(position, side.PlayerId)).ToArray();
        }
        runtime.StartMatch(level.Sides.Select(value => value.PlayerId).ToArray());
        return new AiEncounterDeployment(workers, resources, []);
    }

    public static AiEncounterDeployment Begin(
        AiEncounterLevelDefinition level,
        IAiEncounterLevelRuntime runtime,
        AiEncounterDeployment prepared)
    {
        var townHalls = new int[level.Sides.Length];
        for (var sideIndex = 0; sideIndex < level.Sides.Length; sideIndex++)
        {
            var side = level.Sides[sideIndex];
            var workers = prepared.Workers[sideIndex];
            townHalls[sideIndex] = runtime.BuildStartingTownHall(
                side.PlayerId,
                workers[0],
                level.StartingTownHall,
                side.TownHallPosition);
        }
        return prepared with { StartingTownHalls = townHalls };
    }

    public static void StartEconomy(
        AiEncounterLevelDefinition level,
        IAiEncounterLevelRuntime runtime,
        AiEncounterDeployment deployment)
    {
        for (var sideIndex = 0; sideIndex < level.Sides.Length; sideIndex++)
        {
            var side = level.Sides[sideIndex];
            var workers = deployment.Workers[sideIndex];
            for (var workerIndex = 1; workerIndex < workers.Length; workerIndex++)
            {
                var resourceIndex = side.InitialMineralIndices[
                    (workerIndex - 1) % side.InitialMineralIndices.Length];
                if (!runtime.Gather(
                        side.PlayerId,
                        workers[workerIndex],
                        deployment.ResourceNodes[resourceIndex]))
                    throw new InvalidOperationException(
                        $"Initial gather failed for player {side.PlayerId}.");
            }
        }
    }

    public static void AttachAi(
        AiEncounterLevelDefinition level,
        IAiEncounterLevelRuntime runtime)
    {
        foreach (var side in level.Sides.OrderBy(value => value.PlayerId))
        {
            runtime.AttachAi(
                side.PlayerId,
                side.AiProfile,
                level.AiBuildingSeconds,
                side.DecisionOffsetTicks);
        }
    }
}

public sealed class AiEncounterTelemetry
{
    private readonly Dictionary<int, SideProgress> _progress = [];
    private ulong _latestCombatSequence;

    public void Observe(
        AiEncounterLevelDefinition level,
        IAiEncounterLevelRuntime runtime,
        long tick)
    {
        foreach (var side in level.Sides)
        {
            var snapshot = runtime.ObserveSide(side.PlayerId);
            if (!_progress.TryGetValue(side.PlayerId, out var progress))
            {
                progress = new SideProgress();
                _progress.Add(side.PlayerId, progress);
            }
            progress.Observe(snapshot, tick);
        }
        var signals = runtime.ObserveCombatSignals(_latestCombatSequence);
        foreach (var signal in signals)
        {
            _latestCombatSequence = Math.Max(
                _latestCombatSequence, signal.Sequence);
            if (signal.Kind == CombatEventKind.Impact &&
                _progress.TryGetValue(signal.AttackerPlayerId, out var progress))
                progress.Impacts++;
        }
    }

    public AiEncounterTelemetrySnapshot Snapshot(int playerId)
    {
        if (!_progress.TryGetValue(playerId, out var progress))
            throw new InvalidOperationException("Encounter side was not observed.");
        return progress.Snapshot();
    }

    private sealed class SideProgress
    {
        private long _lastAttackTick = -1;
        private int _minimumArmyAfterFirstAttack = int.MaxValue;

        public long EstablishedTick { get; private set; } = -1;
        public long InfrastructureTick { get; private set; } = -1;
        public long TechnologyTick { get; private set; } = -1;
        public long ExpansionTick { get; private set; } = -1;
        public long FirstAttackTick { get; private set; } = -1;
        public int AttackOrders { get; private set; }
        public int Impacts { get; set; }
        public int MaximumArmy { get; private set; }
        public int MaximumTechnologyLevels { get; private set; }
        public AiEncounterSideSnapshot Latest { get; private set; }

        public void Observe(AiEncounterSideSnapshot value, long tick)
        {
            Latest = value;
            if (value.Established && EstablishedTick < 0)
                EstablishedTick = tick;
            if (value.Barracks > 0 && value.Refineries > 0 &&
                value.Academies > 0 && InfrastructureTick < 0)
                InfrastructureTick = tick;
            var technologyLevels = value.WeaponLevel + value.AssaultLevel +
                                   value.FortificationLevel;
            MaximumTechnologyLevels = Math.Max(
                MaximumTechnologyLevels, technologyLevels);
            if (technologyLevels > 0 && TechnologyTick < 0)
                TechnologyTick = tick;
            if (value.TownHalls >= 2 && ExpansionTick < 0)
                ExpansionTick = tick;
            MaximumArmy = Math.Max(MaximumArmy, value.CombatUnits);
            if (value.LastAttackTick > _lastAttackTick)
            {
                if (value.LastAttackTick >= 0)
                {
                    AttackOrders++;
                    if (FirstAttackTick < 0) FirstAttackTick = tick;
                }
                _lastAttackTick = value.LastAttackTick;
            }
            if (FirstAttackTick >= 0)
            {
                _minimumArmyAfterFirstAttack = Math.Min(
                    _minimumArmyAfterFirstAttack, value.CombatUnits);
            }
        }

        public AiEncounterTelemetrySnapshot Snapshot() => new(
            EstablishedTick,
            InfrastructureTick,
            TechnologyTick,
            ExpansionTick,
            FirstAttackTick,
            AttackOrders,
            Impacts,
            MaximumArmy,
            _minimumArmyAfterFirstAttack == int.MaxValue
                ? 0
                : _minimumArmyAfterFirstAttack,
            MaximumTechnologyLevels,
            Latest);
    }
}

public readonly record struct AiEncounterTelemetrySnapshot(
    long EstablishedTick,
    long InfrastructureTick,
    long TechnologyTick,
    long ExpansionTick,
    long FirstAttackTick,
    int AttackOrders,
    int Impacts,
    int MaximumArmy,
    int MinimumArmyAfterFirstAttack,
    int MaximumTechnologyLevels,
    AiEncounterSideSnapshot Latest);

public sealed class MovementTestRigAiEncounterRuntime(
    MovementTestRig rig) : IAiEncounterLevelRuntime
{
    public void RegisterPlayer(
        int playerId,
        int minerals,
        int gas,
        int supply,
        int used) => rig.RegisterPlayer(playerId, minerals, gas, supply, used);

    public int SpawnWorker(Vector2 position, int playerId) =>
        rig.SpawnWorker(position, playerId).Value;

    public int AddResource(AiEncounterResourceDefinition resource) =>
        rig.AddResourceNode(
            resource.Kind,
            resource.Position,
            resource.Amount,
            resource.HarvestBatch,
            resource.HarvestSeconds,
            resource.HarvesterCapacity,
            resource.RequiresRefinery,
            resource.Operational).Value;

    public void StartMatch(int[] playerIds) => rig.StartMatch(playerIds);

    public int BuildStartingTownHall(
        int playerId,
        int worker,
        BuildingTypeProfile profile,
        Vector2 position)
    {
        var result = rig.Build(
            playerId, new TestUnitId(worker), profile, position);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Starting Town Hall failed: {result.Code}.");
        return result.BuildingId.Value;
    }

    public bool Gather(int playerId, int worker, int resourceNode)
    {
        var result = rig.Gather(
            playerId,
            new TestUnitId(worker),
            new TestResourceNodeId(resourceNode));
        if (result != TestGatherCommandCode.Success)
            throw new InvalidOperationException(
                $"Initial gather command was rejected: {result}.");
        return true;
    }

    public void AttachAi(
        int playerId,
        AiDifficultyProfile profile,
        float buildingSeconds,
        int decisionOffsetTicks) =>
        rig.AttachDemoAi(
            playerId, profile, buildingSeconds, decisionOffsetTicks);

    public AiEncounterSideSnapshot ObserveSide(int playerId)
    {
        var match = rig.ObserveMatch().Players.Single(value =>
            value.PlayerId == playerId);
        var economy = rig.ObservePlayerEconomy(playerId);
        var ai = rig.ObserveAi(playerId);
        return new AiEncounterSideSnapshot(
            playerId,
            match.EstablishedPresence,
            economy.Minerals,
            economy.VespeneGas,
            match.Workers,
            match.CombatUnits,
            rig.CountPlayerBuildings(playerId, DemoBuildingTypes.CommandCenter.Id),
            rig.CountPlayerBuildings(playerId, DemoBuildingTypes.SupplyDepot.Id),
            rig.CountPlayerBuildings(playerId, DemoBuildingTypes.Barracks.Id),
            rig.CountPlayerBuildings(playerId, DemoBuildingTypes.Refinery.Id),
            rig.CountPlayerBuildings(playerId, DemoBuildingTypes.Academy.Id),
            rig.TechnologyLevel(playerId, DemoTechnologies.InfantryWeapons.Id),
            rig.TechnologyLevel(playerId, DemoTechnologies.AssaultDoctrine.Id),
            rig.TechnologyLevel(playerId, DemoTechnologies.FortificationDoctrine.Id),
            ai.Phase,
            ai.LastAttackTick);
    }

    public AiEncounterCombatSignal[] ObserveCombatSignals(ulong afterSequence)
    {
        var batch = rig.ObserveCombatEvents(afterSequence);
        return batch.Events.Select(value => new AiEncounterCombatSignal(
            value.Sequence,
            value.Kind,
            rig.ObserveCombat(value.Attacker).Team)).ToArray();
    }
}
