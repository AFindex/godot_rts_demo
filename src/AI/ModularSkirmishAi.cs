using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.AI;

public enum AiStrategicPhase : byte
{
    Establishing,
    Developing,
    Mobilizing,
    Attacking,
    Recovering
}

public enum AiIntentPriority : byte
{
    Scouting = 20,
    Economy = 40,
    Technology = 50,
    Production = 60,
    Infrastructure = 70,
    Expansion = 75,
    Combat = 80,
    Defense = 90,
    EmergencySupply = 100
}

public sealed record ModularAiConfig(
    BuildingTypeCatalogSnapshot Buildings,
    ProductionCatalogSnapshot Production,
    TechnologyCatalogSnapshot Technologies,
    int TargetWorkers = 14,
    int AttackArmySize = 6,
    int MaximumIntentsPerDecision = 6,
    int SupplyBuffer = 3,
    long ScoutIntervalTicks = 360,
    long AttackIntervalTicks = 240)
{
    public static ModularAiConfig Demo() => new(
        DemoBuildingTypes.CreateCatalog(),
        DemoProductionCatalog.CreateSnapshot(),
        DemoTechnologies.CreateCatalog());
}

public readonly record struct AiIntentProposal(
    AiIntent Intent,
    AiIntentPriority Priority,
    EconomyCost Cost,
    string ExclusivityKey,
    int Sequence);

public interface IAiPlanner
{
    void Plan(AiPlanningContext context);
}

public sealed class AiPlanningContext
{
    private int _sequence;

    public AiPlanningContext(
        AiObservationSnapshot observation,
        ModularAiConfig config,
        StrategicBlackboard blackboard)
    {
        Observation = observation;
        Config = config;
        Blackboard = blackboard;
    }

    public AiObservationSnapshot Observation { get; }
    public ModularAiConfig Config { get; }
    public StrategicBlackboard Blackboard { get; }
    public List<AiIntentProposal> Proposals { get; } = [];

    public void Propose(
        AiIntent intent,
        AiIntentPriority priority,
        EconomyCost cost,
        string exclusivityKey) =>
        Proposals.Add(new AiIntentProposal(
            intent, priority, cost, exclusivityKey, _sequence++));
}

public sealed class StrategicBlackboard
{
    private const int BuildingTypeCount = 5;
    private readonly long[] _nextBuildTicks = new long[BuildingTypeCount];

    public AiStrategicPhase Phase { get; set; } = AiStrategicPhase.Establishing;
    public long Decisions { get; set; }
    public long LastScoutTick { get; set; } = -1;
    public long LastAttackTick { get; set; } = -1;
    public long LastExpansionTick { get; set; } = -1;
    public int PlacementAttempt { get; set; }
    public int ConsecutiveRejections { get; set; }
    public bool HasLastEnemyPosition { get; set; }
    public Vector2 LastEnemyPosition { get; set; }
    public long LastEnemySeenTick { get; set; } = -1;

    public bool CanBuild(int typeId, long tick) =>
        (uint)typeId < BuildingTypeCount && tick >= _nextBuildTicks[typeId];

    public void DelayBuild(int typeId, long untilTick)
    {
        if ((uint)typeId >= BuildingTypeCount)
            throw new ArgumentOutOfRangeException(nameof(typeId));
        _nextBuildTicks[typeId] = Math.Max(_nextBuildTicks[typeId], untilTick);
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)Phase);
        writer.Write(Decisions);
        writer.Write(LastScoutTick);
        writer.Write(LastAttackTick);
        writer.Write(LastExpansionTick);
        writer.Write(PlacementAttempt);
        writer.Write(ConsecutiveRejections);
        writer.Write(HasLastEnemyPosition);
        writer.Write(LastEnemyPosition.X);
        writer.Write(LastEnemyPosition.Y);
        writer.Write(LastEnemySeenTick);
        writer.Write(_nextBuildTicks.Length);
        foreach (var tick in _nextBuildTicks) writer.Write(tick);
    }

    public void Read(BinaryReader reader)
    {
        var phase = (AiStrategicPhase)reader.ReadByte();
        var decisions = reader.ReadInt64();
        var lastScout = reader.ReadInt64();
        var lastAttack = reader.ReadInt64();
        var lastExpansion = reader.ReadInt64();
        var placementAttempt = reader.ReadInt32();
        var rejections = reader.ReadInt32();
        var hasEnemy = reader.ReadBoolean();
        var enemy = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        var lastEnemyTick = reader.ReadInt64();
        var count = reader.ReadInt32();
        if (!Enum.IsDefined(phase) || decisions < 0 || lastScout < -1 ||
            lastAttack < -1 || lastExpansion < -1 || placementAttempt < 0 ||
            rejections < 0 || !float.IsFinite(enemy.X) ||
            !float.IsFinite(enemy.Y) || lastEnemyTick < -1 ||
            count != _nextBuildTicks.Length)
        {
            throw new InvalidDataException("AI blackboard state is invalid.");
        }
        for (var index = 0; index < count; index++)
        {
            var tick = reader.ReadInt64();
            if (tick < 0) throw new InvalidDataException("AI build delay is invalid.");
            _nextBuildTicks[index] = tick;
        }
        Phase = phase;
        Decisions = decisions;
        LastScoutTick = lastScout;
        LastAttackTick = lastAttack;
        LastExpansionTick = lastExpansion;
        PlacementAttempt = placementAttempt;
        ConsecutiveRejections = rejections;
        HasLastEnemyPosition = hasEnemy;
        LastEnemyPosition = enemy;
        LastEnemySeenTick = lastEnemyTick;
    }
}

public sealed class AiIntentArbiter
{
    public int Emit(AiPlanningContext context, IAiIntentSink sink)
    {
        var economy = context.Observation.Economy;
        var minerals = economy.Minerals;
        var vespene = economy.VespeneGas;
        var supply = economy.SupplyRemaining;
        var emitted = 0;
        var exclusive = new HashSet<string>(StringComparer.Ordinal);
        var claimedUnits = new HashSet<int>();
        foreach (var proposal in context.Proposals
                     .OrderByDescending(value => value.Priority)
                     .ThenBy(value => value.Sequence))
        {
            if (emitted >= context.Config.MaximumIntentsPerDecision ||
                exclusive.Contains(proposal.ExclusivityKey) ||
                proposal.Intent.Units.Any(value => claimedUnits.Contains(value)) ||
                proposal.Cost.Minerals > minerals ||
                proposal.Cost.VespeneGas > vespene ||
                proposal.Cost.Supply > supply)
            {
                continue;
            }
            if (!sink.TryAdd(proposal.Intent)) break;
            exclusive.Add(proposal.ExclusivityKey);
            minerals -= proposal.Cost.Minerals;
            vespene -= proposal.Cost.VespeneGas;
            supply -= proposal.Cost.Supply;
            foreach (var unit in proposal.Intent.Units) claimedUnits.Add(unit);
            emitted++;
        }
        return emitted;
    }
}

public sealed class ModularSkirmishAiPolicy : IRtsAiPolicy, IRtsAiExecutionObserver
{
    private const int FormatVersion = 1;
    private readonly ModularAiConfig _config;
    private readonly StrategicBlackboard _blackboard = new();
    private readonly IAiPlanner[] _planners;
    private readonly AiIntentArbiter _arbiter = new();

    public ModularSkirmishAiPolicy(ModularAiConfig config)
    {
        if (config.TargetWorkers <= 0 || config.AttackArmySize <= 0 ||
            config.MaximumIntentsPerDecision is <= 0 or > 64 ||
            config.SupplyBuffer < 0 || config.ScoutIntervalTicks <= 0 ||
            config.AttackIntervalTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config));
        }
        _config = config;
        _planners =
        [
            new EconomyPlanner(),
            new BuildPlanner(),
            new ProductionPlanner(),
            new TechnologyPlanner(),
            new ScoutingPlanner(),
            new DefensePlanner(),
            new CombatPlanner()
        ];
    }

    public string PolicyId => "rts-demo.modular-skirmish";
    public int StateFormatVersion => FormatVersion;
    public AiStrategicPhase Phase => _blackboard.Phase;

    public void Decide(AiObservationSnapshot observation, IAiIntentSink intents)
    {
        _blackboard.Decisions++;
        UpdateKnowledge(observation);
        UpdatePhase(observation);
        var context = new AiPlanningContext(observation, _config, _blackboard);
        foreach (var planner in _planners) planner.Plan(context);
        _arbiter.Emit(context, intents);
    }

    public void ObserveExecution(
        long tick,
        AiIntent intent,
        AiIntentExecutionResult result)
    {
        if (result.Succeeded)
        {
            _blackboard.ConsecutiveRejections = 0;
            switch (intent.Kind)
            {
                case AiIntentKind.Build:
                    _blackboard.PlacementAttempt = 0;
                    _blackboard.DelayBuild(intent.Building.Id, tick + 120);
                    if (intent.Building.Function == BuildingFunctionKind.TownHall)
                        _blackboard.LastExpansionTick = tick;
                    break;
                case AiIntentKind.Move:
                    _blackboard.LastScoutTick = tick;
                    break;
                case AiIntentKind.AttackMove:
                case AiIntentKind.AttackUnit:
                case AiIntentKind.AttackBuilding:
                    _blackboard.LastAttackTick = tick;
                    break;
            }
            return;
        }
        _blackboard.ConsecutiveRejections++;
        if (intent.Kind == AiIntentKind.Build)
        {
            _blackboard.PlacementAttempt++;
            _blackboard.DelayBuild(intent.Building.Id, tick + 48);
        }
    }

    public byte[] CaptureState()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        _blackboard.Write(writer);
        writer.Flush();
        return stream.ToArray();
    }

    public void RestoreState(ReadOnlySpan<byte> state, int formatVersion)
    {
        if (formatVersion != FormatVersion)
            throw new InvalidOperationException("Unsupported modular AI state format.");
        using var stream = new MemoryStream(state.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        try
        {
            _blackboard.Read(reader);
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Trailing modular AI state bytes.");
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Truncated modular AI state.", exception);
        }
    }

    private void UpdateKnowledge(AiObservationSnapshot observation)
    {
        var enemies = observation.View.Units
            .Where(value => value.Relation == PlayerEntityRelation.Enemy)
            .Select(value => value.Position)
            .Concat(observation.View.Buildings
                .Where(value => value.Relation == PlayerEntityRelation.Enemy)
                .Select(value => (value.Bounds.Min + value.Bounds.Max) * 0.5f))
            .ToArray();
        if (enemies.Length == 0) return;
        _blackboard.HasLastEnemyPosition = true;
        _blackboard.LastEnemyPosition = enemies[0];
        _blackboard.LastEnemySeenTick = observation.Tick;
    }

    private void UpdatePhase(AiObservationSnapshot observation)
    {
        var self = observation.Match.Players.FirstOrDefault(value =>
            value.PlayerId == observation.PlayerId);
        if (self.PlayerId == observation.PlayerId && self.IsEliminationRisk)
            _blackboard.Phase = AiStrategicPhase.Recovering;
        else if (self.CombatUnits >= _config.AttackArmySize)
            _blackboard.Phase = _blackboard.LastAttackTick >= 0
                ? AiStrategicPhase.Attacking
                : AiStrategicPhase.Mobilizing;
        else if (self.ProductionFacilities > 0)
            _blackboard.Phase = AiStrategicPhase.Developing;
        else
            _blackboard.Phase = AiStrategicPhase.Establishing;
    }
}

public sealed class EconomyPlanner : IAiPlanner
{
    public void Plan(AiPlanningContext context)
    {
        var observation = context.Observation;
        var activeBuilders = observation.Facilities
            .Where(value => value.BuilderUnit >= 0 &&
                value.State is BuildingLifecycleState.Approaching or
                    BuildingLifecycleState.Constructing)
            .Select(value => value.BuilderUnit)
            .ToHashSet();
        var resources = observation.View.Resources
            .Where(value => value.KnownOperational && value.KnownRemaining != 0)
            .OrderBy(value => value.NodeId.Value)
            .ToArray();
        if (resources.Length > 0)
        {
            var preferGas = observation.Economy.VespeneGas * 3 <
                            observation.Economy.Minerals;
            var idle = observation.OwnUnits
                .Where(value => value.IsWorker &&
                    value.WorkerState == WorkerEconomyState.Idle &&
                    !activeBuilders.Contains(value.UnitId))
                .OrderBy(value => value.UnitId)
                .Take(2)
                .ToArray();
            for (var index = 0; index < idle.Length; index++)
            {
                var preferred = resources.Where(value =>
                    value.Kind == (preferGas
                        ? EconomyResourceKind.VespeneGas
                        : EconomyResourceKind.Minerals)).ToArray();
                var choices = preferred.Length > 0 ? preferred : resources;
                var resource = choices[(idle[index].UnitId +
                    (int)context.Blackboard.Decisions) % choices.Length];
                context.Propose(
                    new AiIntent(
                        AiIntentKind.Gather, [idle[index].UnitId],
                        resource.Position, resource.NodeId.Value),
                    AiIntentPriority.Economy,
                    default,
                    $"worker:{idle[index].UnitId}");
            }

            var gasNodes = resources.Where(value =>
                value.Kind == EconomyResourceKind.VespeneGas).ToArray();
            var gasWorkers = observation.OwnUnits.Count(value =>
                value.IsWorker &&
                value.TargetResourceKind == EconomyResourceKind.VespeneGas);
            if (preferGas && gasNodes.Length > 0 && gasWorkers < 2)
            {
                var idleIds = idle.Select(value => value.UnitId).ToHashSet();
                var transfers = observation.OwnUnits
                    .Where(value => value.IsWorker &&
                        value.TargetResourceKind != EconomyResourceKind.VespeneGas &&
                        !activeBuilders.Contains(value.UnitId) &&
                        !idleIds.Contains(value.UnitId))
                    .OrderBy(value => value.UnitId)
                    .Take(2 - gasWorkers)
                    .ToArray();
                for (var index = 0; index < transfers.Length; index++)
                {
                    var gas = gasNodes[index % gasNodes.Length];
                    context.Propose(
                        new AiIntent(
                            AiIntentKind.Gather, [transfers[index].UnitId],
                            gas.Position, gas.NodeId.Value),
                        AiIntentPriority.Economy,
                        default,
                        $"worker:{transfers[index].UnitId}");
                }
            }
        }

        var operational = observation.Bases.Where(value => value.Operational).ToArray();
        if (operational.Length < 2) return;
        var source = operational.OrderByDescending(value => value.Saturation).First();
        var target = operational.OrderBy(value => value.Saturation).First();
        if (source.Id == target.Id || source.Saturation < 1.1f ||
            target.Saturation > 0.8f)
            return;
        var count = Math.Max(1, Math.Min(3,
            source.AssignedWorkers - source.IdealWorkers));
        context.Propose(
            new AiIntent(
                AiIntentKind.TransferWorkers, [], target.Position,
                SourceBase: source.Id, TargetBase: target.Id, Count: count),
            AiIntentPriority.Economy,
            default,
            "worker-transfer");
    }
}

public sealed class BuildPlanner : IAiPlanner
{
    private static readonly Vector2[] PlacementOffsets =
    [
        new(210f, -130f), new(230f, 120f), new(-220f, -130f),
        new(-230f, 130f), new(0f, -210f), new(0f, 220f),
        new(310f, 0f), new(-310f, 0f)
    ];

    public void Plan(AiPlanningContext context)
    {
        var observation = context.Observation;
        var activeBuilders = observation.Facilities
            .Where(value => value.BuilderUnit >= 0 &&
                value.State is BuildingLifecycleState.Approaching or
                    BuildingLifecycleState.Constructing)
            .Select(value => value.BuilderUnit)
            .ToHashSet();
        var workerCandidates = observation.OwnUnits
            .Where(value => value.IsWorker &&
                !activeBuilders.Contains(value.UnitId))
            .OrderBy(value => value.WorkerState == WorkerEconomyState.Idle ? 0 : 1)
            .ThenBy(value => value.UnitId)
            .ToArray();
        if (workerCandidates.Length == 0) return;
        var worker = workerCandidates[0];

        var waiting = observation.Facilities
            .Where(value => value.State == BuildingLifecycleState.WaitingForBuilder)
            .OrderBy(value => value.BuildingId.Value)
            .FirstOrDefault();
        if (waiting.State == BuildingLifecycleState.WaitingForBuilder)
        {
            context.Propose(
                new AiIntent(
                    AiIntentKind.ResumeBuild,
                    [worker.UnitId],
                    (waiting.Bounds.Min + waiting.Bounds.Max) * 0.5f,
                    Facility: waiting.BuildingId),
                AiIntentPriority.Infrastructure,
                default,
                $"resume:{waiting.BuildingId.Value}");
        }

        var supply = context.Config.Buildings.Type(DemoBuildingTypes.SupplyDepot.Id);
        var barracks = context.Config.Buildings.Type(DemoBuildingTypes.Barracks.Id);
        var commandCenter = context.Config.Buildings.Type(DemoBuildingTypes.CommandCenter.Id);
        var refinery = context.Config.Buildings.Type(DemoBuildingTypes.Refinery.Id);
        var academy = context.Config.Buildings.Type(DemoBuildingTypes.Academy.Id);
        var facilities = observation.Facilities;
        var basePosition = observation.Bases.FirstOrDefault(value => value.Operational).Position;
        if (basePosition == Vector2.Zero)
            basePosition = worker.Position;

        var supplyNeeded = observation.Economy.SupplyRemaining <=
                           context.Config.SupplyBuffer ||
                           observation.Economy.SupplyCapacity > 0 &&
                           observation.Economy.SupplyUsed * 100 /
                               observation.Economy.SupplyCapacity >= 80;
        if (supplyNeeded && !HasActive(facilities, supply.Id))
            ProposeBuilding(context, worker.UnitId, supply, basePosition,
                AiIntentPriority.EmergencySupply);

        if (!HasActive(facilities, barracks.Id) &&
            observation.OwnUnits.Count(value => value.IsWorker) >= 4)
            ProposeBuilding(context, worker.UnitId, barracks, basePosition,
                AiIntentPriority.Infrastructure);

        if (HasCompleted(facilities, barracks.Id) &&
            !HasActive(facilities, refinery.Id))
        {
            var gasCandidates = observation.View.Resources
                .Where(value => value.Kind == EconomyResourceKind.VespeneGas &&
                    value.KnownRemaining != 0)
                .OrderBy(value => Vector2.DistanceSquared(value.Position, basePosition))
                .ToArray();
            if (gasCandidates.Length > 0)
            {
                var gas = gasCandidates[0];
                ProposeBuilding(context, worker.UnitId, refinery, gas.Position,
                    AiIntentPriority.Infrastructure, gas.NodeId.Value);
            }
        }

        if (HasCompleted(facilities, barracks.Id) &&
            !HasActive(facilities, academy.Id))
            ProposeBuilding(context, worker.UnitId, academy, basePosition,
                AiIntentPriority.Technology);

        var workers = observation.OwnUnits.Count(value => value.IsWorker);
        var operationalBases = observation.Bases.Count(value => value.Operational);
        if (operationalBases == 1 && workers >= 10 &&
            observation.Tick - context.Blackboard.LastExpansionTick >= 600)
        {
            var expansionResources = observation.View.Resources
                .Where(value => value.Kind == EconomyResourceKind.Minerals &&
                    value.KnownRemaining != 0 &&
                    Vector2.DistanceSquared(value.Position, basePosition) > 360f * 360f)
                .OrderBy(value => Vector2.DistanceSquared(value.Position, basePosition))
                .ToArray();
            if (expansionResources.Length > 0)
            {
                var expansionResource = expansionResources[0];
                var direction = Vector2.Normalize(expansionResource.Position - basePosition);
                var center = expansionResource.Position + direction * 170f;
                center = ClampToWorld(center, commandCenter.Size,
                    observation.View.WorldBounds);
                ProposeBuilding(context, worker.UnitId, commandCenter, center,
                    AiIntentPriority.Expansion);
            }
        }
    }

    private static bool HasActive(AiFacilitySnapshot[] facilities, int typeId) =>
        facilities.Any(value => value.Type.Id == typeId &&
            value.State is not BuildingLifecycleState.Canceled and
                not BuildingLifecycleState.Destroyed);

    private static bool HasCompleted(AiFacilitySnapshot[] facilities, int typeId) =>
        facilities.Any(value => value.Type.Id == typeId &&
            value.State == BuildingLifecycleState.Completed);

    private static void ProposeBuilding(
        AiPlanningContext context,
        int worker,
        BuildingTypeProfile building,
        Vector2 basePosition,
        AiIntentPriority priority,
        int resourceNode = -1)
    {
        if (!context.Blackboard.CanBuild(building.Id, context.Observation.Tick))
            return;
        var center = resourceNode >= 0
            ? basePosition
            : ClampToWorld(
                basePosition + PlacementOffsets[
                    (context.Blackboard.PlacementAttempt + building.Id * 2) %
                    PlacementOffsets.Length],
                building.Size,
                context.Observation.View.WorldBounds);
        context.Propose(
            new AiIntent(
                AiIntentKind.Build, [worker], center, resourceNode,
                Building: building),
            priority,
            building.Cost,
            $"build:{building.Id}");
    }

    private static Vector2 ClampToWorld(
        Vector2 center,
        Vector2 size,
        SimRect bounds)
    {
        var half = size * 0.5f + new Vector2(4f);
        return Vector2.Clamp(center, bounds.Min + half, bounds.Max - half);
    }
}

public sealed class ProductionPlanner : IAiPlanner
{
    public void Plan(AiPlanningContext context)
    {
        var observation = context.Observation;
        var workers = observation.OwnUnits.Count(value => value.IsWorker);
        var workerRecipe = context.Config.Production.Recipes.ToArray()
            .First(value => value.UnitType.IsWorker);
        var combatRecipes = context.Config.Production.Recipes.ToArray()
            .Where(value => !value.UnitType.IsWorker)
            .OrderBy(value => value.Id)
            .ToArray();
        foreach (var facility in observation.Facilities
                     .Where(value => value.State == BuildingLifecycleState.Completed)
                     .OrderBy(value => value.BuildingId.Value))
        {
            if (facility.Type.Id == workerRecipe.ProducerBuildingTypeId &&
                workers < context.Config.TargetWorkers &&
                facility.ProductionOrders == 0)
            {
                context.Propose(
                    new AiIntent(
                        AiIntentKind.Train, [], Vector2.Zero,
                        Facility: facility.BuildingId, Recipe: workerRecipe),
                    AiIntentPriority.Production,
                    workerRecipe.Cost,
                    $"train:{facility.BuildingId.Value}");
            }
            if (facility.Function != BuildingFunctionKind.Production ||
                facility.ProductionOrders >= 2 || combatRecipes.Length == 0)
                continue;
            var recipe = observation.Economy.VespeneGas >= 25 &&
                         context.Blackboard.Decisions % 3 == 0 &&
                         combatRecipes.Length > 1
                ? combatRecipes[1]
                : combatRecipes[0];
            context.Propose(
                new AiIntent(
                    AiIntentKind.Train, [], Vector2.Zero,
                    Facility: facility.BuildingId, Recipe: recipe),
                AiIntentPriority.Production,
                recipe.Cost,
                $"train:{facility.BuildingId.Value}");
        }
    }
}

public sealed class TechnologyPlanner : IAiPlanner
{
    public void Plan(AiPlanningContext context)
    {
        var researchers = context.Observation.Facilities
            .Where(value => value.Function == BuildingFunctionKind.Research &&
                value.State == BuildingLifecycleState.Completed &&
                value.ResearchOrders == 0)
            .OrderBy(value => value.BuildingId.Value)
            .ToArray();
        if (researchers.Length == 0) return;
        var researcher = researchers[0];
        var technologies = context.Observation.Technologies
            .Where(value => !value.Queued &&
                value.CurrentLevel < value.Technology.MaximumLevel)
            .OrderBy(value => value.Technology.Id == 0 ? 0 : 1)
            .ThenBy(value => value.Technology.Id)
            .ToArray();
        if (technologies.Length == 0) return;
        var technology = technologies[0];
        context.Propose(
            new AiIntent(
                AiIntentKind.Research, [], Vector2.Zero,
                Facility: researcher.BuildingId,
                Technology: technology.Technology),
            AiIntentPriority.Technology,
            technology.Technology.Cost,
            "research");
    }
}

public sealed class ScoutingPlanner : IAiPlanner
{
    public void Plan(AiPlanningContext context)
    {
        var observation = context.Observation;
        if (observation.Tick - context.Blackboard.LastScoutTick <
                context.Config.ScoutIntervalTicks)
            return;
        var scouts = observation.OwnUnits
            .Where(value => !value.IsWorker &&
                value.CombatPhase is CombatPhase.None or CombatPhase.Searching)
            .OrderBy(value => value.UnitId)
            .ToArray();
        if (scouts.Length == 0) return;
        var scout = scouts[0];
        var target = FindHiddenTarget(observation.View, scout.Position);
        if (!target.HasValue) return;
        context.Propose(
            new AiIntent(AiIntentKind.Move, [scout.UnitId], target.Value),
            AiIntentPriority.Scouting,
            default,
            "scout");
    }

    internal static Vector2? FindHiddenTarget(
        PlayerViewSnapshot view,
        Vector2 origin)
    {
        var bestDistance = float.NegativeInfinity;
        Vector2? best = null;
        for (var row = 0; row < view.VisibilityRows; row += 2)
        {
            for (var column = 0; column < view.VisibilityColumns; column += 2)
            {
                var index = row * view.VisibilityColumns + column;
                if (view.VisibilityCells[index] != (byte)MapVisibility.Hidden)
                    continue;
                var target = view.WorldBounds.Min + new Vector2(
                    (column + 0.5f) * view.VisibilityCellSize,
                    (row + 0.5f) * view.VisibilityCellSize);
                target = Vector2.Min(target, view.WorldBounds.Max - new Vector2(8f));
                var distance = Vector2.DistanceSquared(origin, target);
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                best = target;
            }
        }
        return best;
    }
}

public sealed class CombatPlanner : IAiPlanner
{
    public void Plan(AiPlanningContext context)
    {
        var observation = context.Observation;
        var army = observation.OwnUnits
            .Where(value => !value.IsWorker)
            .OrderBy(value => value.UnitId)
            .Select(value => value.UnitId)
            .ToArray();
        if (army.Length == 0) return;
        var visibleEnemies = AiTargeting.VisibleEnemies(observation.View);
        if (army.Length < context.Config.AttackArmySize ||
            observation.Tick - context.Blackboard.LastAttackTick <
                context.Config.AttackIntervalTicks)
            return;
        if (visibleEnemies.Length > 0)
        {
            context.Propose(
                AiTargeting.TargetIntent(army, visibleEnemies[0]),
                AiIntentPriority.Combat,
                default,
                "combat");
            return;
        }
        var targetPosition = context.Blackboard.HasLastEnemyPosition
            ? context.Blackboard.LastEnemyPosition
            : Vector2.Zero;
        if (targetPosition == Vector2.Zero)
            targetPosition = ScoutingPlanner.FindHiddenTarget(
                observation.View,
                observation.OwnUnits.First(value => !value.IsWorker).Position)
                ?? observation.View.WorldBounds.Max - new Vector2(32f);
        context.Propose(
            new AiIntent(AiIntentKind.AttackMove, army, targetPosition),
            AiIntentPriority.Combat,
            default,
            "combat");
    }
}

public sealed class DefensePlanner : IAiPlanner
{
    public void Plan(AiPlanningContext context)
    {
        var observation = context.Observation;
        var army = observation.OwnUnits
            .Where(value => !value.IsWorker)
            .OrderBy(value => value.UnitId)
            .Select(value => value.UnitId)
            .ToArray();
        if (army.Length == 0) return;
        var operationalBases = observation.Bases
            .Where(value => value.Operational).ToArray();
        if (operationalBases.Length == 0) return;
        var threats = AiTargeting.VisibleEnemies(observation.View)
            .Where(target => operationalBases.Any(economyBase =>
                Vector2.DistanceSquared(target.Position, economyBase.Position) <=
                340f * 340f))
            .OrderBy(target => operationalBases.Min(economyBase =>
                Vector2.DistanceSquared(target.Position, economyBase.Position)))
            .ToArray();
        if (threats.Length == 0) return;
        context.Propose(
            AiTargeting.TargetIntent(army, threats[0]),
            AiIntentPriority.Defense,
            default,
            "combat");
    }
}

internal readonly record struct AiEnemyTarget(
    bool IsBuilding,
    int Id,
    Vector2 Position);

internal static class AiTargeting
{
    public static AiEnemyTarget[] VisibleEnemies(PlayerViewSnapshot view) =>
        view.Units
            .Where(value => value.Relation == PlayerEntityRelation.Enemy)
            .Select(value => new AiEnemyTarget(
                false, value.UnitId, value.Position))
            .Concat(view.Buildings
                .Where(value => value.Relation == PlayerEntityRelation.Enemy)
                .Select(value => new AiEnemyTarget(
                    true,
                    value.BuildingId.Value,
                    (value.Bounds.Min + value.Bounds.Max) * 0.5f)))
            .ToArray();

    public static AiIntent TargetIntent(int[] army, AiEnemyTarget target) =>
        target.IsBuilding
            ? new AiIntent(
                AiIntentKind.AttackBuilding,
                army,
                target.Position,
                TargetBuildingId: target.Id)
            : new AiIntent(
                AiIntentKind.AttackUnit,
                army,
                target.Position,
                TargetUnitId: target.Id);
}
