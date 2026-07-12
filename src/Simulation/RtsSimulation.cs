using System.Diagnostics;
using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class RtsSimulation : ICombatMovementDriver
{
    private const float WaypointRadius = 13f;
    private const float ArrivalSlowRadius = 58f;
    private const float ArrivalStopRadius = 3.5f;
    private const int CollisionIterations = 3;
    private const int DestinationReflowIntervalTicks = 12;
    private const int DestinationReflowCooldownTicks = 90;
    private const int MaximumDestinationSwapsPerPass = 12;
    private const int DestinationLocalRematchIntervalTicks = 60;
    private const int DestinationLocalRematchCooldownTicks = 180;
    private const int DestinationOverflowIntervalTicks = 30;
    private const int MaximumDestinationOverflowsPerPass = 2;
    private const int DestinationYieldIntervalTicks = 30;
    private const int MaximumDestinationYieldStartsPerPass = 2;
    private const int MaximumActiveDestinationYields = 4;
    private const int DestinationYieldDeadlineTicks = 360;
    private const int DestinationYieldReturnDeadlineTicks = 180;
    private const int DestinationYieldCooldownTicks = 600;

    private readonly IPathProvider _pathProvider;
    private readonly Queue<PathRequest> _pathRequests = new();
    private readonly SpatialHash _spatialHash;
    private readonly DestinationSlotAllocator _slotAllocator;
    private readonly DestinationSlotReflow _slotReflow;
    private readonly DestinationLocalRematcher _localRematcher;
    private readonly DestinationOverflowResolver _overflowResolver;
    private readonly DestinationYieldResolver _yieldResolver;
    private readonly BuildingConnectivityGuard _buildingConnectivityGuard;
    private readonly SteeringSolver _steeringSolver;
    private readonly IGroupRoutePlanner? _groupRoutePlanner;
    private readonly ChokeController? _chokeController;
    private readonly CombatSystem _combatSystem;
    private readonly Action<int, Vector2> _economyMoveWorker;
    private readonly Action<int> _economyStopWorker;
    private readonly Action<int, Vector2> _constructionMoveWorker;
    private readonly Action<int> _constructionStopWorker;
    private readonly Func<UnitTypeProfile, int, Vector2, int> _productionSpawnUnit;
    private readonly Action<int, int, RallyTarget> _productionApplyRally;
    private readonly int[] _collisionNeighbors = new int[64];
    private readonly int[] _orderReadyUnits;
    private readonly UnitOrder[] _orderReadyOrders;
    private readonly bool[] _orderReadyProcessed;
    private readonly int[] _orderDispatchUnits;
    private int _pendingNavigationInvalidations;
    private int _nextMovementGroupId = 1;
    private int _nextOrderSequenceId = 1;
    private SimulationCommandRecorder? _commandRecorder;
    private EconomyCommandRecorder? _economyCommandRecorder;
    private ConstructionCommandRecorder? _constructionCommandRecorder;
    private ProductionCommandRecorder? _productionCommandRecorder;
    private SimulationReplayPackageRecorder? _replayPackageRecorder;
    private bool _issuingSystemOrder;
    private bool _issuingConstructionWorldMutation;

    public RtsSimulation(
        StaticWorld world,
        IPathProvider pathProvider,
        int capacity = 512,
        IGroupRoutePlanner? groupRoutePlanner = null,
        ChokeController? chokeController = null,
        ClearanceBakeSnapshot? clearanceBake = null)
    {
        World = world;
        _pathProvider = pathProvider;
        Units = new UnitStore(capacity);
        Combat = new CombatStore(capacity);
        CombatEvents = new CombatEventStream();
        CommandQueues = new UnitCommandQueueStore(capacity);
        Economy = new EconomySystem(capacity);
        Construction = new ConstructionSystem();
        Production = new ProductionSystem();
        Technology = new TechnologySystem();
        Visibility = new PlayerVisibilitySystem(world.Bounds);
        Match = new MatchSystem();
        _orderReadyUnits = new int[capacity];
        _orderReadyOrders = new UnitOrder[capacity];
        _orderReadyProcessed = new bool[capacity];
        _orderDispatchUnits = new int[capacity];
        _spatialHash = new SpatialHash(40f);
        _slotAllocator = new DestinationSlotAllocator(world);
        _slotReflow = new DestinationSlotReflow(world);
        _localRematcher = new DestinationLocalRematcher(world);
        _overflowResolver = new DestinationOverflowResolver(world);
        _yieldResolver = new DestinationYieldResolver(world);
        _buildingConnectivityGuard = new BuildingConnectivityGuard(
            world, staticBake: clearanceBake);
        _steeringSolver = new SteeringSolver(world, _spatialHash);
        _groupRoutePlanner = groupRoutePlanner;
        _chokeController = chokeController;
        _combatSystem = new CombatSystem(Units, Combat, this, world, CombatEvents);
        _economyMoveWorker = MoveEconomyWorker;
        _economyStopWorker = StopEconomyWorker;
        _constructionMoveWorker = MoveConstructionWorker;
        _constructionStopWorker = StopConstructionWorker;
        _productionSpawnUnit = SpawnProducedUnit;
        _productionApplyRally = ApplyProducedUnitRally;
    }

    public StaticWorld World { get; }
    public UnitStore Units { get; }
    public CombatStore Combat { get; }
    public CombatEventStream CombatEvents { get; }
    public UnitCommandQueueStore CommandQueues { get; }
    public EconomySystem Economy { get; }
    public ConstructionSystem Construction { get; }
    public ProductionSystem Production { get; }
    public TechnologySystem Technology { get; }
    public PlayerVisibilitySystem Visibility { get; }
    public MatchSystem Match { get; }
    public SimulationMetrics Metrics { get; } = new();
    public GroupRoutePlan LastIssuedGroupRoute { get; private set; } = GroupRoutePlan.Empty;
    public IGroupRoutePlanner? GroupRoutePlanner => _groupRoutePlanner;
    public ChokeController? ChokeController => _chokeController;
    public int PathBudgetPerTick { get; set; } = 24;
    public int CombatWeaponUpgradeTechnologyId { get; set; } =
        DemoTechnologies.InfantryWeapons.Id;
    public int CombatBuildingArmorTechnologyId { get; set; } =
        DemoTechnologies.FortificationDoctrine.Id;
    public ulong ActiveClearanceBakeHash =>
        _buildingConnectivityGuard.ClearanceBakeHash;

    public ulong ComputeStateHash() => SimulationStateHasher.Compute(this);

    public CombatDamageResult PreviewCombatDamage(int attacker, int target) =>
        _combatSystem.PreviewDamage(attacker, target);

    public CombatDamageResult PreviewCombatDamage(
        int attacker,
        GameplayBuildingId building)
    {
        if ((uint)attacker >= (uint)Units.Count || !Units.Alive[attacker] ||
            !Construction.IsAlive(building))
            throw new ArgumentOutOfRangeException();
        var target = Construction.Observe(building);
        return CombatDamageResolver.Resolve(
            CombatWeapon(attacker), BuildingDefense(target), target.Health);
    }

    public ClearanceBakeCommitResult TryCommitClearanceBake(
        ClearanceBakeSnapshot candidate)
    {
        var previousHash = _buildingConnectivityGuard.ClearanceBakeHash;
        if (_commandRecorder is not null || _replayPackageRecorder is not null)
        {
            return new ClearanceBakeCommitResult(
                ClearanceBakeCommitCode.RecordingActive,
                previousHash,
                candidate.StableHash,
                0);
        }
        if (_pathProvider is not IClearanceBakeReloadTarget pathTarget)
        {
            return new ClearanceBakeCommitResult(
                ClearanceBakeCommitCode.UnsupportedPathProvider,
                previousHash,
                candidate.StableHash,
                0);
        }

        var pathValidation = pathTarget.ValidateClearanceBake(candidate);
        if (!pathValidation.Succeeded)
        {
            return new ClearanceBakeCommitResult(
                pathValidation.Code,
                previousHash,
                candidate.StableHash,
                0);
        }
        var guardValidation =
            _buildingConnectivityGuard.ValidateClearanceBake(candidate);
        if (!guardValidation.Succeeded)
        {
            return new ClearanceBakeCommitResult(
                guardValidation.Code,
                previousHash,
                candidate.StableHash,
                0);
        }

        pathTarget.CommitClearanceBake(candidate);
        _buildingConnectivityGuard.CommitClearanceBake(candidate);
        var affectedUnits = new List<int>();
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (Units.Alive[unit] &&
                Units.Modes[unit] is UnitMoveMode.Moving or
                    UnitMoveMode.WaitingForPath)
            {
                affectedUnits.Add(unit);
            }
        }
        if (affectedUnits.Count > 0)
        {
            _pendingNavigationInvalidations += affectedUnits.Count;
            ReplanInvalidatedGroups(affectedUnits);
        }
        Metrics.ClearanceBakeReloads++;
        return new ClearanceBakeCommitResult(
            ClearanceBakeCommitCode.Success,
            previousHash,
            candidate.StableHash,
            affectedUnits.Count);
    }

    internal SimulationRuntimeStateCapture CaptureRuntimeState()
    {
        var units = new UnitStore(Units.Capacity);
        units.CopyRuntimeStateFrom(Units);
        var combat = new CombatStore(Units.Capacity);
        combat.CopyRuntimeStateFrom(Combat);
        var queues = new UnitCommandQueueStore(Units.Capacity);
        queues.CopyRuntimeStateFrom(CommandQueues);
        return new SimulationRuntimeStateCapture
        {
            Tick = Metrics.Tick,
            StateHash = ComputeStateHash(),
            DynamicOccupancy = World.DynamicOccupancy.CaptureRuntimeState(),
            Units = units,
            Combat = combat,
            Economy = Economy.CaptureRuntimeState(Units.Count),
            Visibility = Visibility.CaptureRuntimeState(),
            Match = Match.CaptureRuntimeState(),
            Construction = Construction.CaptureRuntimeState(),
            Production = Production.CaptureRuntimeState(),
            Technology = Technology.CaptureRuntimeState(),
            CommandQueues = queues,
            ChokeTraffic = _chokeController?.CaptureRuntimeState(),
            PrivateState = new RtsPrivateRuntimeSnapshot(
                PathBudgetPerTick,
                _pendingNavigationInvalidations,
                _nextMovementGroupId,
                _nextOrderSequenceId,
                _pathRequests.ToArray())
        };
    }

    internal void RestoreRuntimeState(SimulationRuntimeStateCapture snapshot)
    {
        if (snapshot.Units.Capacity != Units.Capacity)
        {
            throw new InvalidOperationException("Simulation runtime capacity mismatch.");
        }
        World.DynamicOccupancy.RestoreRuntimeState(snapshot.DynamicOccupancy);
        Units.CopyRuntimeStateFrom(snapshot.Units);
        Combat.CopyRuntimeStateFrom(snapshot.Combat);
        CombatEvents.Reset();
        Economy.RestoreRuntimeState(snapshot.Economy, Units.Count);
        Construction.RestoreRuntimeState(snapshot.Construction);
        Production.RestoreRuntimeState(
            snapshot.Production, Construction, Economy, Units);
        Technology.RestoreRuntimeState(
            snapshot.Technology, Construction, Economy.Players);
        Visibility.RestoreRuntimeState(snapshot.Visibility);
        Visibility.Update(Units, Combat, Construction);
        Match.RestoreRuntimeState(snapshot.Match, Economy.Players);
        CommandQueues.CopyRuntimeStateFrom(snapshot.CommandQueues);
        if (_chokeController is not null && snapshot.ChokeTraffic is not null)
        {
            _chokeController.RestoreRuntimeState(snapshot.ChokeTraffic);
        }
        else if (_chokeController is not null || snapshot.ChokeTraffic is not null)
        {
            throw new InvalidOperationException("Simulation choke runtime mismatch.");
        }

        Metrics.Tick = snapshot.Tick;
        PathBudgetPerTick = snapshot.PrivateState.PathBudgetPerTick;
        _pendingNavigationInvalidations =
            snapshot.PrivateState.PendingNavigationInvalidations;
        _nextMovementGroupId = snapshot.PrivateState.NextMovementGroupId;
        _nextOrderSequenceId = snapshot.PrivateState.NextOrderSequenceId;
        _pathRequests.Clear();
        for (var index = 0; index < snapshot.PrivateState.PathRequests.Length; index++)
        {
            _pathRequests.Enqueue(snapshot.PrivateState.PathRequests[index]);
        }
        _commandRecorder = null;
        _economyCommandRecorder = null;
        _constructionCommandRecorder = null;
        _productionCommandRecorder = null;
        _replayPackageRecorder = null;
    }

    public void StartCommandRecording()
    {
        _commandRecorder = new SimulationCommandRecorder();
        _economyCommandRecorder = new EconomyCommandRecorder();
        _constructionCommandRecorder = new ConstructionCommandRecorder();
        _productionCommandRecorder = new ProductionCommandRecorder();
    }

    public void StartMatch(ReadOnlySpan<int> playerIds)
    {
        if (_commandRecorder is not null || _replayPackageRecorder is not null)
            throw new InvalidOperationException(
                "Match setup must be completed before command recording starts.");
        Match.Start(Metrics.Tick, playerIds, Economy.Players);
    }

    public SimulationCommandLogSnapshot CaptureCommandLog()
    {
        if (_commandRecorder is null)
        {
            throw new InvalidOperationException("Command recording has not been started.");
        }
        return _commandRecorder.Capture();
    }

    public EconomyCommandLogSnapshot CaptureEconomyCommandLog()
    {
        if (_economyCommandRecorder is null)
        {
            throw new InvalidOperationException("Command recording has not been started.");
        }
        return _economyCommandRecorder.Capture();
    }

    public ConstructionCommandLogSnapshot CaptureConstructionCommandLog()
    {
        if (_constructionCommandRecorder is null)
        {
            throw new InvalidOperationException("Command recording has not been started.");
        }
        return _constructionCommandRecorder.Capture();
    }

    public ProductionCommandLogSnapshot CaptureProductionCommandLog()
    {
        if (_productionCommandRecorder is null)
            throw new InvalidOperationException("Command recording has not been started.");
        return _productionCommandRecorder.Capture();
    }

    public void StartReplayPackageRecording(ReplayResourceManifest resources)
    {
        if (!Economy.CanStartReplayRecording(Units.Count))
        {
            throw new InvalidOperationException(
                "Replay package recording requires idle economy workers at tick zero.");
        }
        if (Visibility.HasExploredState)
        {
            throw new InvalidOperationException(
                "Replay package recording requires unexplored visibility at tick zero.");
        }
        if (resources.ClearanceBakeHash != ActiveClearanceBakeHash)
        {
            throw new InvalidOperationException(
                "Replay manifest must reference the active Clearance Bake.");
        }
        _commandRecorder = new SimulationCommandRecorder();
        _economyCommandRecorder = new EconomyCommandRecorder();
        _constructionCommandRecorder = new ConstructionCommandRecorder();
        _productionCommandRecorder = new ProductionCommandRecorder();
        _replayPackageRecorder = new SimulationReplayPackageRecorder(this, resources);
    }

    public SimulationReplayPackageSnapshot CaptureReplayPackage()
    {
        if (_commandRecorder is null || _economyCommandRecorder is null ||
            _constructionCommandRecorder is null ||
            _productionCommandRecorder is null ||
            _replayPackageRecorder is null)
        {
            throw new InvalidOperationException(
                "Replay package recording has not been started.");
        }
        return _replayPackageRecorder.Capture(
            _commandRecorder.Capture(),
            _economyCommandRecorder.Capture(),
            _constructionCommandRecorder.Capture(),
            _productionCommandRecorder.Capture());
    }

    public void ApplyRecordedCommand(RecordedSimulationCommand command)
    {
        switch (command.Kind)
        {
            case UnitOrderKind.Move:
                IssueMove(command.Units, command.TargetPosition, command.Queued);
                break;
            case UnitOrderKind.AttackMove:
                IssueAttackMove(command.Units, command.TargetPosition, command.Queued);
                break;
            case UnitOrderKind.AttackTarget:
                IssueAttackTarget(command.Units, command.TargetUnit, command.Queued);
                break;
            case UnitOrderKind.AttackBuilding:
                IssueAttackBuilding(
                    command.Units,
                    new GameplayBuildingId(command.TargetBuilding),
                    command.Queued);
                break;
            case UnitOrderKind.GatherResource:
                IssueDeferredWorkerOrder(
                    command.Units,
                    new UnitOrder(
                        UnitOrderKind.GatherResource,
                        command.TargetPosition,
                        TargetResourceNode: command.TargetResourceNode),
                    command.Queued);
                break;
            case UnitOrderKind.ResumeConstruction:
                IssueDeferredWorkerOrder(
                    command.Units,
                    new UnitOrder(
                        UnitOrderKind.ResumeConstruction,
                        command.TargetPosition,
                        TargetBuilding: command.TargetBuilding),
                    command.Queued);
                break;
            case UnitOrderKind.Stop:
                Stop(command.Units);
                break;
            case UnitOrderKind.Hold:
                Hold(command.Units);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported recorded command {command.Kind}.");
        }
    }

    public void ApplyRecordedEconomyCommand(RecordedEconomyCommand command)
    {
        switch (command.Kind)
        {
            case EconomyCommandKind.Gather:
                var result = IssueGather(
                    command.PlayerId,
                    command.UnitId,
                    new EconomyResourceNodeId(command.ResourceNodeId));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Replay Gather failed with {result.Code}.");
                }
                break;
            case EconomyCommandKind.SetRefineryOperational:
                SetRefineryOperational(
                    command.PlayerId,
                    new EconomyResourceNodeId(command.ResourceNodeId),
                    command.Value);
                break;
            case EconomyCommandKind.TransferWorkers:
                var transfer = IssueWorkerTransfer(
                    command.PlayerId,
                    new EconomyBaseId(command.SourceBaseId),
                    new EconomyBaseId(command.TargetBaseId),
                    command.Count);
                if (!transfer.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Replay worker transfer failed with {transfer.Code}.");
                }
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported economy command {command.Kind}.");
        }
    }

    public void ApplyRecordedConstructionCommand(
        RecordedConstructionCommand command)
    {
        switch (command.Kind)
        {
            case ConstructionReplayCommandKind.Build:
                var result = IssueConstruction(
                    command.PlayerId, command.BuilderUnit, command.Type,
                    command.Center, command.RefineryNode);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Replay Build failed with {result.Code}.");
                }
                break;
            case ConstructionReplayCommandKind.Cancel:
                if (!CancelConstruction(command.PlayerId, command.BuildingId))
                {
                    throw new InvalidOperationException("Replay Cancel failed.");
                }
                break;
            case ConstructionReplayCommandKind.Resume:
                if (!ResumeConstruction(
                        command.PlayerId, command.BuildingId, command.BuilderUnit))
                {
                    throw new InvalidOperationException("Replay Resume failed.");
                }
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported construction command {command.Kind}.");
        }
    }

    public void ApplyRecordedProductionCommand(RecordedProductionCommand command)
    {
        switch (command.Kind)
        {
            case ProductionReplayCommandKind.Train:
                var result = IssueProduction(
                    command.PlayerId, command.Producer, command.Recipe);
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Replay Train failed with {result.Code}.");
                break;
            case ProductionReplayCommandKind.Cancel:
                if (!Production.Observe(command.Producer).Orders.Any(
                        value => value.Id == command.OrderId))
                    throw new InvalidOperationException(
                        "Replay production Cancel producer/order mismatch.");
                if (!CancelProduction(command.PlayerId, command.OrderId))
                    throw new InvalidOperationException("Replay production Cancel failed.");
                break;
            case ProductionReplayCommandKind.SetRallyPoint:
                if (!SetProductionRallyTarget(
                        command.PlayerId, command.Producer, command.Rally))
                    throw new InvalidOperationException("Replay Rally failed.");
                break;
            case ProductionReplayCommandKind.Research:
                var research = IssueResearch(
                    command.PlayerId, command.Producer, command.Technology);
                if (!research.Succeeded)
                    throw new InvalidOperationException(
                        $"Replay Research failed with {research.Code}.");
                break;
            case ProductionReplayCommandKind.CancelResearch:
                if (!Technology.Observe(command.Producer).Orders.Any(
                        value => value.Id == command.ResearchOrderId) ||
                    !CancelResearch(command.PlayerId, command.ResearchOrderId))
                    throw new InvalidOperationException("Replay research Cancel failed.");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported production command {command.Kind}.");
        }
    }

    public DynamicFootprintId PlaceBuilding(SimRect footprint)
    {
        var id = World.DynamicOccupancy.Place(footprint);
        InvalidatePathsIntersecting(footprint);
        if (!_issuingConstructionWorldMutation)
        {
            _replayPackageRecorder?.RecordPlace(Metrics.Tick, id, footprint);
        }
        return id;
    }

    public BuildingPlacementResult TryPlaceBuilding(
        SimRect footprint,
        BuildingPlacementRules rules)
    {
        var validation = BuildingPlacementValidator.Validate(
            World, Units, footprint, rules, _buildingConnectivityGuard);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var id = PlaceBuilding(footprint);
        return new BuildingPlacementResult(
            BuildingPlacementCode.Success, id, -1);
    }

    public BuildingPlacementResult TryPlaceBuilding(
        Vector2 center,
        BuildingFootprintProfileSnapshot profile)
    {
        var halfSize = profile.Size * 0.5f;
        return TryPlaceBuilding(
            new SimRect(center - halfSize, center + halfSize),
            new BuildingPlacementRules(
                profile.MinimumPassageClass,
                profile.UnitPadding));
    }

    public bool RemoveBuilding(DynamicFootprintId id)
    {
        if (Construction.OwnsActiveFootprint(id))
        {
            return false;
        }
        return RemoveDynamicFootprint(id);
    }

    private bool RemoveGameplayBuildingFootprint(DynamicFootprintId id)
    {
        _issuingConstructionWorldMutation = true;
        try
        {
            return RemoveDynamicFootprint(id);
        }
        finally
        {
            _issuingConstructionWorldMutation = false;
        }
    }

    private bool RemoveDynamicFootprint(DynamicFootprintId id)
    {
        if (!World.DynamicOccupancy.Remove(id, out var removedBounds))
        {
            return false;
        }

        if (!_issuingConstructionWorldMutation)
        {
            _replayPackageRecorder?.RecordRemove(Metrics.Tick, id, removedBounds);
        }

        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (Units.BlockedByNavigation[unit])
            {
                QueueNavigationReplan(unit);
            }
        }

        return true;
    }

    public int AddUnit(
        Vector2 position,
        float radius = 7.5f,
        float maxSpeed = 128f,
        float acceleration = 720f) =>
        AddUnit(position, 0, CombatProfileSnapshot.Standard, radius, maxSpeed, acceleration);

    public int AddUnit(
        Vector2 position,
        int team,
        CombatProfileSnapshot combatProfile,
        float radius = 7.5f,
        float maxSpeed = 128f,
        float acceleration = 720f)
    {
        var unit = Units.Add(position, radius, maxSpeed, acceleration);
        Combat.Register(unit, team, position, combatProfile);
        return unit;
    }

    public int AddUnit(
        Vector2 position,
        UnitMovementProfileSnapshot profile) =>
        AddUnit(
            position,
            profile.PhysicalRadius,
            profile.MaximumSpeed,
            profile.Acceleration);

    public int AddUnit(
        Vector2 position,
        UnitMovementProfileSnapshot movementProfile,
        int team,
        CombatProfileSnapshot combatProfile) =>
        AddUnit(
            position,
            team,
            combatProfile,
            movementProfile.PhysicalRadius,
            movementProfile.MaximumSpeed,
            movementProfile.Acceleration);

    public int AddWorker(
        Vector2 position,
        int playerId,
        float radius = 7.5f,
        float maxSpeed = 128f,
        float acceleration = 720f)
    {
        var unit = AddUnit(
            position,
            playerId,
            CombatProfileSnapshot.Standard,
            radius,
            maxSpeed,
            acceleration);
        Economy.RegisterWorker(unit, playerId);
        return unit;
    }

    public GatherCommandResult IssueGather(
        int issuingPlayerId,
        int unit,
        EconomyResourceNodeId nodeId)
    {
        var matchBlock = MatchCommandBlockFor(issuingPlayerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new GatherCommandResult(
                matchBlock switch
                {
                    MatchCommandBlock.Completed => GatherCommandCode.MatchCompleted,
                    MatchCommandBlock.NotParticipant => GatherCommandCode.NotParticipant,
                    _ => GatherCommandCode.PlayerDefeated
                }, unit, nodeId);
        }
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit])
        {
            return new GatherCommandResult(
                GatherCommandCode.InvalidUnit, unit, nodeId);
        }
        var validation = Economy.ValidateGather(
            issuingPlayerId, unit, nodeId);
        if (!validation.Succeeded)
        {
            return validation;
        }
        _economyCommandRecorder?.RecordGather(
            Metrics.Tick, issuingPlayerId, unit, nodeId.Value);
        CommandQueues.ClearPending(unit);
        CommandQueues.HasActiveOrders[unit] = false;
        Span<int> gatheringWorker = stackalloc int[1];
        gatheringWorker[0] = unit;
        Construction.InterruptBuilders(gatheringWorker);
        Economy.BeginGather(unit, nodeId);
        MoveEconomyWorker(unit, Economy.ObserveResourceNode(nodeId).Position);
        CommandQueues.Begin(
            unit,
            new UnitOrder(
                UnitOrderKind.GatherResource,
                Economy.ResourceNodePosition(nodeId),
                TargetResourceNode: nodeId.Value,
                SequenceId: NextOrderSequenceId()),
            wasQueued: false);
        return validation;
    }

    public void SetRefineryOperational(
        int issuingPlayerId,
        EconomyResourceNodeId nodeId,
        bool operational)
    {
        if (!Economy.Players.IsRegistered(issuingPlayerId))
        {
            throw new ArgumentOutOfRangeException(nameof(issuingPlayerId));
        }
        Economy.SetRefineryOperational(nodeId, operational);
        _economyCommandRecorder?.RecordRefinery(
            Metrics.Tick, issuingPlayerId, nodeId.Value, operational);
    }

    public WorkerTransferCommandResult IssueWorkerTransfer(
        int issuingPlayerId,
        EconomyBaseId sourceBase,
        EconomyBaseId targetBase,
        int count)
    {
        var matchBlock = MatchCommandBlockFor(issuingPlayerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new WorkerTransferCommandResult(
                matchBlock switch
                {
                    MatchCommandBlock.Completed =>
                        WorkerTransferCommandCode.MatchCompleted,
                    MatchCommandBlock.NotParticipant =>
                        WorkerTransferCommandCode.NotParticipant,
                    _ => WorkerTransferCommandCode.PlayerDefeated
                }, sourceBase, targetBase, count, 0);
        }
        var result = Economy.TransferWorkers(
            issuingPlayerId, sourceBase, targetBase, count,
            Units, _economyMoveWorker);
        if (result.Succeeded)
        {
            _economyCommandRecorder?.RecordWorkerTransfer(
                Metrics.Tick, issuingPlayerId, sourceBase, targetBase, count);
        }
        return result;
    }

    public ConstructionCommandResult IssueConstruction(
        int issuingPlayerId,
        int builderUnit,
        BuildingTypeProfile profile,
        Vector2 center,
        EconomyResourceNodeId refineryNode = default)
    {
        var validation = ValidateConstructionRequest(
            issuingPlayerId, builderUnit, profile, center, refineryNode,
            out center, out refineryNode);
        if (!validation.Succeeded) return validation;
        BuildingPlacementResult placement;
        _issuingConstructionWorldMutation = true;
        try
        {
            placement = TryPlaceBuilding(center, profile.PlacementProfile);
        }
        finally
        {
            _issuingConstructionWorldMutation = false;
        }
        if (!placement.Succeeded)
        {
            return new ConstructionCommandResult(
                ConstructionCommandCode.PlacementRejected,
                default,
                placement.Code);
        }
        var charged = Economy.Players.TrySpend(issuingPlayerId, profile.Cost);
        if (!charged.Succeeded)
        {
            RemoveBuilding(placement.FootprintId);
            throw new InvalidOperationException(
                "Construction cost changed between validation and commit.");
        }
        Economy.Cancel([builderUnit]);
        CommandQueues.ClearPending(builderUnit);
        CommandQueues.HasActiveOrders[builderUnit] = false;
        var halfSize = profile.Size * 0.5f;
        var id = Construction.Add(
            issuingPlayerId,
            builderUnit,
            profile,
            new SimRect(center - halfSize, center + halfSize),
            placement.FootprintId,
            refineryNode,
            Units.Positions[builderUnit],
            Units.Radii[builderUnit]);
        MoveConstructionWorker(builderUnit, Construction.BuilderAccessPoint(id));
        _constructionCommandRecorder?.RecordBuild(
            Metrics.Tick, issuingPlayerId, builderUnit, profile, center, refineryNode);
        return new ConstructionCommandResult(
            ConstructionCommandCode.Success, id);
    }

    public ConstructionCommandResult PreviewConstruction(
        int issuingPlayerId,
        int builderUnit,
        BuildingTypeProfile profile,
        Vector2 center,
        EconomyResourceNodeId refineryNode = default) =>
        ValidateConstructionRequest(
            issuingPlayerId, builderUnit, profile, center, refineryNode,
            out _, out _);

    private ConstructionCommandResult ValidateConstructionRequest(
        int issuingPlayerId,
        int builderUnit,
        BuildingTypeProfile profile,
        Vector2 requestedCenter,
        EconomyResourceNodeId requestedRefineryNode,
        out Vector2 resolvedCenter,
        out EconomyResourceNodeId resolvedRefineryNode)
    {
        resolvedCenter = requestedCenter;
        resolvedRefineryNode = requestedRefineryNode;
        var matchBlock = MatchCommandBlockFor(issuingPlayerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new ConstructionCommandResult(
                matchBlock switch
                {
                    MatchCommandBlock.Completed => ConstructionCommandCode.MatchCompleted,
                    MatchCommandBlock.NotParticipant => ConstructionCommandCode.NotParticipant,
                    _ => ConstructionCommandCode.PlayerDefeated
                }, default);
        }
        if (!profile.RequiresVespeneNode && requestedRefineryNode == default)
            resolvedRefineryNode = new EconomyResourceNodeId(-1);
        var validation = Construction.ValidateRequest(
            Economy, Units, issuingPlayerId, builderUnit, profile,
            resolvedRefineryNode);
        if (!validation.Succeeded) return validation;

        var spend = Economy.Players.ValidateSpend(issuingPlayerId, profile.Cost);
        if (!spend.Succeeded)
        {
            return new ConstructionCommandResult(
                spend.Code switch
                {
                    EconomyTransactionCode.InsufficientMinerals =>
                        ConstructionCommandCode.InsufficientMinerals,
                    EconomyTransactionCode.InsufficientVespeneGas =>
                        ConstructionCommandCode.InsufficientVespeneGas,
                    EconomyTransactionCode.SupplyBlocked =>
                        ConstructionCommandCode.SupplyBlocked,
                    _ => ConstructionCommandCode.InvalidPlayer
                }, default);
        }
        if (profile.RequiresVespeneNode)
            resolvedCenter = Economy.ResourceNodePosition(resolvedRefineryNode);

        var placementProfile = profile.PlacementProfile;
        var halfSize = placementProfile.Size * 0.5f;
        var footprint = new SimRect(
            resolvedCenter - halfSize, resolvedCenter + halfSize);
        var placement = BuildingPlacementValidator.Validate(
            World, Units, footprint,
            new BuildingPlacementRules(
                placementProfile.MinimumPassageClass,
                placementProfile.UnitPadding),
            _buildingConnectivityGuard);
        return placement.Succeeded
            ? new ConstructionCommandResult(
                ConstructionCommandCode.Success, default)
            : new ConstructionCommandResult(
                ConstructionCommandCode.PlacementRejected,
                default, placement.Code);
    }

    public bool CancelConstruction(int playerId, GameplayBuildingId buildingId)
    {
        if (MatchCommandBlockFor(playerId) != MatchCommandBlock.None)
            return false;
        var succeeded = Construction.Cancel(
            buildingId, playerId, Economy, RemoveGameplayBuildingFootprint);
        if (succeeded)
        {
            _constructionCommandRecorder?.RecordCancel(
                Metrics.Tick, playerId, buildingId);
        }
        return succeeded;
    }

    public bool ResumeConstruction(
        int playerId,
        GameplayBuildingId buildingId,
        int builderUnit) =>
        TryResumeConstruction(
            playerId, buildingId, builderUnit,
            replaceUnitOrders: true, recordCommand: true);

    private bool TryResumeConstruction(
        int playerId,
        GameplayBuildingId buildingId,
        int builderUnit,
        bool replaceUnitOrders,
        bool recordCommand)
    {
        if (MatchCommandBlockFor(playerId) != MatchCommandBlock.None)
            return false;
        if (!Construction.CanResume(
                buildingId, playerId, builderUnit, Economy, Units))
            return false;
        Span<int> builder = stackalloc int[1];
        builder[0] = builderUnit;
        Economy.Cancel(builder);
        if (replaceUnitOrders)
        {
            CommandQueues.ClearPending(builderUnit);
            CommandQueues.HasActiveOrders[builderUnit] = false;
        }
        var succeeded = Construction.Resume(
            buildingId, playerId, builderUnit, Economy, Units,
            _constructionMoveWorker);
        if (succeeded && recordCommand)
        {
            _constructionCommandRecorder?.RecordResume(
                Metrics.Tick, playerId, buildingId, builderUnit);
        }
        if (succeeded && replaceUnitOrders)
        {
            var building = Construction.Observe(buildingId);
            CommandQueues.Begin(
                builderUnit,
                new UnitOrder(
                    UnitOrderKind.ResumeConstruction,
                    (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                    TargetBuilding: buildingId.Value,
                    SequenceId: NextOrderSequenceId()),
                wasQueued: false);
        }
        return succeeded;
    }

    public bool DamageBuilding(GameplayBuildingId buildingId, float damage) =>
        Construction.ApplyDamage(
            buildingId, damage, Economy, RemoveGameplayBuildingFootprint);

    public bool DamageUnit(int unit, float damage)
    {
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit] ||
            !float.IsFinite(damage) || damage <= 0f)
            return false;
        Combat.Health[unit] = MathF.Max(0f, Combat.Health[unit] - damage);
        if (Combat.Health[unit] > 0f) return true;
        Units.Alive[unit] = false;
        Combat.CommandIntents[unit] = UnitCommandIntent.None;
        Combat.Phases[unit] = CombatPhase.None;
        Combat.TargetUnits[unit] = -1;
        Combat.TargetBuildings[unit] = -1;
        Combat.TargetKinds[unit] = CombatTargetKind.None;
        ((ICombatMovementDriver)this).Kill(unit);
        return true;
    }

    public ProductionCommandResult IssueProduction(
        int playerId,
        GameplayBuildingId producer,
        ProductionRecipeProfile recipe)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new ProductionCommandResult(
                matchBlock switch
                {
                    MatchCommandBlock.Completed => ProductionCommandCode.MatchCompleted,
                    MatchCommandBlock.NotParticipant => ProductionCommandCode.NotParticipant,
                    _ => ProductionCommandCode.PlayerDefeated
                }, default);
        }
        var result = Production.Enqueue(
            playerId, producer, recipe, Construction, Economy.Players);
        if (result.Succeeded)
            _productionCommandRecorder?.RecordTrain(
                Metrics.Tick, playerId, producer, recipe);
        return result;
    }

    public bool CancelProduction(
        int playerId,
        ProductionOrderId orderId)
    {
        if (MatchCommandBlockFor(playerId) != MatchCommandBlock.None)
            return false;
        var result = Production.Cancel(
            playerId, orderId, Economy.Players, out var producer);
        if (result)
            _productionCommandRecorder?.RecordCancel(
                Metrics.Tick, playerId, producer, orderId);
        return result;
    }

    public bool SetProductionRallyPoint(
        int playerId,
        GameplayBuildingId producer,
        Vector2 point) =>
        SetProductionRallyTarget(
            playerId, producer, RallyTarget.Ground(point));

    public bool SetProductionRallyTarget(
        int playerId,
        GameplayBuildingId producer,
        RallyTarget rally)
    {
        if (MatchCommandBlockFor(playerId) != MatchCommandBlock.None)
            return false;
        if (!ValidateRallyReference(playerId, rally)) return false;
        var result = Production.SetRallyTarget(
            playerId, producer, rally, Construction);
        if (result)
            _productionCommandRecorder?.RecordRally(
                Metrics.Tick, playerId, producer, rally);
        return result;
    }

    public ResearchCommandResult IssueResearch(
        int playerId,
        GameplayBuildingId researcher,
        TechnologyProfile technology)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new ResearchCommandResult(
                matchBlock switch
                {
                    MatchCommandBlock.Completed => ResearchCommandCode.MatchCompleted,
                    MatchCommandBlock.NotParticipant => ResearchCommandCode.NotParticipant,
                    _ => ResearchCommandCode.PlayerDefeated
                }, default);
        }
        var result = Technology.Enqueue(
            playerId, researcher, technology, Construction, Economy.Players);
        if (result.Succeeded)
            _productionCommandRecorder?.RecordResearch(
                Metrics.Tick, playerId, researcher, technology);
        return result;
    }

    public bool CancelResearch(int playerId, ResearchOrderId orderId)
    {
        if (MatchCommandBlockFor(playerId) != MatchCommandBlock.None)
            return false;
        var result = Technology.Cancel(
            playerId, orderId, Economy.Players, out var researcher);
        if (result)
            _productionCommandRecorder?.RecordCancelResearch(
                Metrics.Tick, playerId, researcher, orderId);
        return result;
    }

    public PlayerViewSnapshot CreatePlayerView(int playerId)
    {
        if (!Economy.Players.IsRegistered(playerId))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        var units = new List<PlayerUnitViewSnapshot>();
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit])
                continue;
            var owner = Combat.Teams[unit];
            var relation = Relation(playerId, owner);
            if (relation != PlayerEntityRelation.Own &&
                !Visibility.IsVisible(playerId, Units.Positions[unit]))
            {
                continue;
            }
            units.Add(new PlayerUnitViewSnapshot(
                unit, owner, relation, Units.Positions[unit], Units.Radii[unit],
                Combat.Health[unit], Combat.MaximumHealth[unit],
                Units.Modes[unit], Combat.Phases[unit]));
        }
        var buildings = new List<PlayerBuildingViewSnapshot>();
        foreach (var building in Construction.CreateOverview())
        {
            if (building.IsTerminal)
                continue;
            var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
            var relation = Relation(playerId, building.PlayerId);
            if (relation != PlayerEntityRelation.Own &&
                !Visibility.IsVisible(playerId, center))
            {
                continue;
            }
            buildings.Add(new PlayerBuildingViewSnapshot(
                building.Id, building.PlayerId, relation, building.Type,
                building.Bounds, building.State, building.Progress,
                building.Health, building.MaximumHealth));
        }
        var resources = new List<PlayerResourceViewSnapshot>();
        for (var index = 0; index < Economy.ResourceNodeCount; index++)
        {
            var node = Economy.ObserveResourceNode(
                new EconomyResourceNodeId(index));
            var visibility = Visibility.At(playerId, node.Position);
            if (visibility == MapVisibility.Hidden)
                continue;
            resources.Add(new PlayerResourceViewSnapshot(
                node.Id, node.Kind, node.Position, visibility,
                visibility == MapVisibility.Visible ? node.Remaining : -1,
                visibility == MapVisibility.Visible && node.Operational));
        }
        return new PlayerViewSnapshot(
            playerId, World.Bounds, Visibility.CellSize,
            Visibility.Columns, Visibility.Rows,
            Visibility.CreateCells(playerId),
            units.ToArray(), buildings.ToArray(), resources.ToArray());
    }

    public PlayerOrderCommandResult IssuePlayerMove(
        int playerId,
        ReadOnlySpan<int> units,
        Vector2 target,
        bool queued = false)
    {
        var validation = ValidatePlayerSelection(playerId, units);
        if (!validation.Succeeded) return validation;
        IssueMove(units, target, queued);
        return validation;
    }

    public PlayerOrderCommandResult IssuePlayerAttackMove(
        int playerId,
        ReadOnlySpan<int> units,
        Vector2 target,
        bool queued = false)
    {
        var validation = ValidatePlayerSelection(playerId, units);
        if (!validation.Succeeded) return validation;
        IssueAttackMove(units, target, queued);
        return validation;
    }

    public PlayerOrderCommandResult IssuePlayerStop(
        int playerId,
        ReadOnlySpan<int> units)
    {
        var validation = ValidatePlayerSelection(playerId, units);
        if (!validation.Succeeded) return validation;
        Stop(units);
        return validation;
    }

    public PlayerOrderCommandResult IssuePlayerHold(
        int playerId,
        ReadOnlySpan<int> units)
    {
        var validation = ValidatePlayerSelection(playerId, units);
        if (!validation.Succeeded) return validation;
        Hold(units);
        return validation;
    }

    public PlayerOrderCommandResult IssuePlayerSmartCommand(
        int playerId,
        ReadOnlySpan<int> units,
        SmartCommandTarget target,
        bool attackMoveModifier,
        bool queued = false)
    {
        var validation = ValidatePlayerSelection(playerId, units);
        if (!validation.Succeeded) return validation;
        switch (target.Kind)
        {
            case SmartCommandTargetKind.FriendlyUnit:
                if ((uint)target.Unit >= (uint)Units.Count ||
                    !Units.Alive[target.Unit])
                    return new(PlayerOrderCommandCode.InvalidTarget);
                if (Combat.Teams[target.Unit] != playerId)
                    return new(PlayerOrderCommandCode.FriendlyTarget);
                break;
            case SmartCommandTargetKind.EnemyUnit:
                if ((uint)target.Unit >= (uint)Units.Count ||
                    !Units.Alive[target.Unit])
                    return new(PlayerOrderCommandCode.InvalidTarget);
                if (Combat.Teams[target.Unit] == playerId)
                    return new(PlayerOrderCommandCode.FriendlyTarget);
                if (!Visibility.IsVisible(playerId, Units.Positions[target.Unit]))
                    return new(PlayerOrderCommandCode.TargetNotVisible);
                break;
            case SmartCommandTargetKind.EnemyBuilding:
                var id = new GameplayBuildingId(target.Building);
                if (!Construction.IsAlive(id))
                    return new(PlayerOrderCommandCode.InvalidTarget);
                var building = Construction.Observe(id);
                if (building.PlayerId == playerId)
                    return new(PlayerOrderCommandCode.FriendlyTarget);
                var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
                if (!Visibility.IsVisible(playerId, center))
                    return new(PlayerOrderCommandCode.TargetNotVisible);
                break;
            case SmartCommandTargetKind.FriendlyBuilding:
                var friendlyId = new GameplayBuildingId(target.Building);
                if (!Construction.IsAlive(friendlyId))
                    return new(PlayerOrderCommandCode.InvalidTarget);
                if (Construction.Observe(friendlyId).PlayerId != playerId)
                    return new(PlayerOrderCommandCode.InvalidTarget);
                break;
            case SmartCommandTargetKind.ResourceNode:
                if ((uint)target.ResourceNode >= (uint)Economy.ResourceNodeCount)
                    return new(PlayerOrderCommandCode.InvalidTarget);
                var resourceId = new EconomyResourceNodeId(target.ResourceNode);
                if (Economy.ResourceNodePosition(resourceId) != target.Position)
                    return new(PlayerOrderCommandCode.InvalidTarget);
                if (Visibility.At(playerId, target.Position) == MapVisibility.Hidden)
                    return new(PlayerOrderCommandCode.TargetNotVisible);
                break;
        }

        if (!attackMoveModifier && target.Kind == SmartCommandTargetKind.ResourceNode)
            return IssueResourceSmartCommand(playerId, units, target, queued);
        if (!attackMoveModifier && target.Kind == SmartCommandTargetKind.FriendlyBuilding)
            return IssueFriendlyBuildingSmartCommand(playerId, units, target, queued);
        IssueSmartCommand(units, target, attackMoveModifier, queued);
        return validation;
    }

    private PlayerOrderCommandResult IssueResourceSmartCommand(
        int playerId,
        ReadOnlySpan<int> units,
        SmartCommandTarget target,
        bool queued)
    {
        var workers = new List<int>(units.Length);
        var movers = new List<int>(units.Length);
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            (Economy.IsWorkerOwnedBy(unit, playerId) ? workers : movers).Add(unit);
        }
        if (workers.Count == 0)
        {
            IssueMove(units, target.Position, queued);
            return new(PlayerOrderCommandCode.Success);
        }
        var resource = new EconomyResourceNodeId(target.ResourceNode);
        for (var index = 0; index < workers.Count; index++)
        {
            if (!Economy.ValidateGather(playerId, workers[index], resource).Succeeded)
                return new(PlayerOrderCommandCode.ContextActionUnavailable);
        }
        if (queued)
        {
            IssueDeferredWorkerOrder(
                workers.ToArray(),
                new UnitOrder(
                    UnitOrderKind.GatherResource,
                    target.Position,
                    TargetResourceNode: target.ResourceNode),
                queued: true);
            if (movers.Count > 0)
                IssueMove(movers.ToArray(), target.Position, queued: true);
            return new(PlayerOrderCommandCode.Success);
        }
        for (var index = 0; index < workers.Count; index++)
            IssueGather(playerId, workers[index], resource);
        if (movers.Count > 0)
            IssueMove(movers.ToArray(), target.Position);
        return new(PlayerOrderCommandCode.Success);
    }

    private PlayerOrderCommandResult IssueFriendlyBuildingSmartCommand(
        int playerId,
        ReadOnlySpan<int> units,
        SmartCommandTarget target,
        bool queued)
    {
        var buildingId = new GameplayBuildingId(target.Building);
        var building = Construction.Observe(buildingId);
        var builder = -1;
        if (building.State == BuildingLifecycleState.WaitingForBuilder)
        {
            for (var index = 0; index < units.Length; index++)
            {
                var unit = units[index];
                if (Economy.IsWorkerOwnedBy(unit, playerId) &&
                    (builder < 0 || unit < builder))
                    builder = unit;
            }
        }
        if (builder < 0)
        {
            IssueMove(units, target.Position, queued);
            return new(PlayerOrderCommandCode.Success);
        }
        if (!Construction.CanResume(
                buildingId, playerId, builder, Economy, Units))
            return new(PlayerOrderCommandCode.ContextActionUnavailable);
        var movers = new List<int>(units.Length - 1);
        for (var index = 0; index < units.Length; index++)
        {
            if (units[index] != builder) movers.Add(units[index]);
        }
        if (queued)
        {
            IssueDeferredWorkerOrder(
                [builder],
                new UnitOrder(
                    UnitOrderKind.ResumeConstruction,
                    target.Position,
                    TargetBuilding: target.Building),
                queued: true);
            if (movers.Count > 0)
                IssueMove(movers.ToArray(), target.Position, queued: true);
            return new(PlayerOrderCommandCode.Success);
        }
        if (!ResumeConstruction(playerId, buildingId, builder))
            return new(PlayerOrderCommandCode.ContextActionUnavailable);
        if (movers.Count > 0)
            IssueMove(movers.ToArray(), target.Position);
        return new(PlayerOrderCommandCode.Success);
    }

    private void IssueDeferredWorkerOrder(
        ReadOnlySpan<int> unitIndices,
        UnitOrder order,
        bool queued)
    {
        if (!UnitOrderContract.IsStructurallyValid(order) || unitIndices.IsEmpty)
            throw new ArgumentException("Deferred worker order is invalid.", nameof(order));
        for (var index = 0; index < unitIndices.Length; index++)
        {
            ValidateUnitIndex(unitIndices[index]);
            ValidateLivingUnit(unitIndices[index]);
        }
        _commandRecorder?.Record(Metrics.Tick, unitIndices, order, queued);
        order = order with { SequenceId = NextOrderSequenceId() };
        if (!queued)
        {
            ExecuteOrderGroup(unitIndices, order, wasQueued: false);
            return;
        }
        var immediateCount = 0;
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            if (CommandQueues.PendingCounts[unit] == 0 &&
                IsActiveOrderComplete(unit))
                _orderDispatchUnits[immediateCount++] = unit;
            else
                CommandQueues.TryEnqueue(unit, order);
        }
        if (immediateCount > 0)
        {
            ExecuteOrderGroup(
                _orderDispatchUnits.AsSpan(0, immediateCount),
                order,
                wasQueued: true);
        }
    }

    public void IssueMove(
        ReadOnlySpan<int> unitIndices,
        Vector2 target,
        bool queued = false) =>
        IssueOrder(
            unitIndices,
            new UnitOrder(UnitOrderKind.Move, target),
            queued);

    private PlayerOrderCommandResult ValidatePlayerSelection(
        int playerId,
        ReadOnlySpan<int> units)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new PlayerOrderCommandResult(matchBlock switch
            {
                MatchCommandBlock.Completed => PlayerOrderCommandCode.MatchCompleted,
                MatchCommandBlock.NotParticipant => PlayerOrderCommandCode.NotParticipant,
                _ => PlayerOrderCommandCode.PlayerDefeated
            });
        }
        if (!Economy.Players.IsRegistered(playerId))
            return new(PlayerOrderCommandCode.InvalidPlayer);
        if (units.IsEmpty)
            return new(PlayerOrderCommandCode.EmptySelection);
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit])
                return new(PlayerOrderCommandCode.InvalidUnit);
            if (Combat.Teams[unit] != playerId)
                return new(PlayerOrderCommandCode.WrongOwner);
        }
        return new(PlayerOrderCommandCode.Success);
    }

    private MatchCommandBlock MatchCommandBlockFor(int playerId)
    {
        if (Match.Phase == MatchPhase.Setup)
            return MatchCommandBlock.None;
        if (Match.Phase == MatchPhase.Completed)
            return MatchCommandBlock.Completed;
        if (!Match.IsParticipant(playerId))
            return MatchCommandBlock.NotParticipant;
        return Match.IsDefeated(playerId)
            ? MatchCommandBlock.Defeated
            : MatchCommandBlock.None;
    }

    private enum MatchCommandBlock : byte
    {
        None,
        Defeated,
        Completed,
        NotParticipant
    }

    private static PlayerEntityRelation Relation(int playerId, int ownerId) =>
        ownerId == playerId
            ? PlayerEntityRelation.Own
            : ownerId <= 0
                ? PlayerEntityRelation.Neutral
                : PlayerEntityRelation.Enemy;

    public void IssueAttackMove(
        ReadOnlySpan<int> unitIndices,
        Vector2 target,
        bool queued = false) =>
        IssueOrder(
            unitIndices,
            new UnitOrder(UnitOrderKind.AttackMove, target),
            queued);

    public void IssueAttackTarget(
        ReadOnlySpan<int> unitIndices,
        int targetUnit,
        bool queued = false)
    {
        ValidateUnitIndex(targetUnit);
        ValidateLivingUnit(targetUnit);
        IssueOrder(
            unitIndices,
            new UnitOrder(
                UnitOrderKind.AttackTarget,
                Units.Positions[targetUnit],
                targetUnit),
            queued);
    }

    public void IssueAttackBuilding(
        ReadOnlySpan<int> unitIndices,
        GameplayBuildingId targetBuilding,
        bool queued = false)
    {
        if (!Construction.IsAlive(targetBuilding))
        {
            throw new ArgumentOutOfRangeException(nameof(targetBuilding));
        }
        for (var index = 0; index < unitIndices.Length; index++)
        {
            ValidateUnitIndex(unitIndices[index]);
            ValidateLivingUnit(unitIndices[index]);
            if (!Construction.IsEnemyTarget(
                    targetBuilding, Combat.Teams[unitIndices[index]]))
            {
                throw new InvalidOperationException(
                    $"Building {targetBuilding.Value} is friendly to attacker " +
                    $"{unitIndices[index]}.");
            }
        }
        var target = Construction.Observe(targetBuilding);
        IssueOrder(
            unitIndices,
            new UnitOrder(
                UnitOrderKind.AttackBuilding,
                (target.Bounds.Min + target.Bounds.Max) * 0.5f,
                TargetBuilding: targetBuilding.Value),
            queued);
    }

    public void IssueSmartCommand(
        ReadOnlySpan<int> unitIndices,
        SmartCommandTarget target,
        bool attackMoveModifier,
        bool queued = false) =>
        IssueOrder(
            unitIndices,
            SmartCommandResolver.Resolve(target, attackMoveModifier),
            queued);

    private void ExecuteMoveOrder(
        ReadOnlySpan<int> unitIndices,
        Vector2 target,
        UnitCommandIntent intent)
    {
        if (unitIndices.IsEmpty)
        {
            return;
        }

        for (var index = 0; index < unitIndices.Length; index++)
        {
            ValidateUnitIndex(unitIndices[index]);
            ValidateLivingUnit(unitIndices[index]);
        }

        var assignments = _slotAllocator.Allocate(Units, unitIndices, target);
        var centroid = Vector2.Zero;
        var maximumRadius = 0f;
        var maximumNavigationRadius = 0f;
        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            centroid += Units.Positions[unit];
            maximumRadius = MathF.Max(maximumRadius, Units.Radii[unit]);
            maximumNavigationRadius = MathF.Max(
                maximumNavigationRadius, Units.NavigationRadii[unit]);
        }

        centroid /= unitIndices.Length;
        var groupGoal = World.Bounds.Inset(maximumRadius + 2f).Clamp(target);
        LastIssuedGroupRoute = PlanGroupRoute(
            centroid, groupGoal, maximumNavigationRadius);
        var movementGroupId = NextMovementGroupId();
        if (unitIndices.Length > 1 && LastIssuedGroupRoute.Waypoints.Length > 0)
        {
            Metrics.SharedRouteAssignments += unitIndices.Length;
        }
        _chokeController?.AssignForMove(
            Units,
            unitIndices,
            centroid,
            groupGoal,
            LastIssuedGroupRoute.ChokeIds);

        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            ValidateUnitIndex(unit);
            ValidateLivingUnit(unit);
            Combat.SetCommand(unit, intent, groupGoal);
            Units.CommandVersions[unit]++;
            Units.SlotTargets[unit] = assignments[unit];
            Units.MoveGoals[unit] = groupGoal;
            Units.MovementGroupIds[unit] = movementGroupId;
            Units.MovementGroupSizes[unit] = unitIndices.Length;
            Units.SlotReflowCooldownTicks[unit] = 0;
            Units.DestinationBestDistances[unit] = Vector2.Distance(
                Units.Positions[unit], assignments[unit]);
            Units.DestinationStallTicks[unit] = 0;
            Units.DestinationNearTicks[unit] = 0;
            Units.DestinationOverflowed[unit] = false;
            Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
            Units.DestinationYieldReturnTargets[unit] = assignments[unit];
            Units.DestinationYieldPoints[unit] = assignments[unit];
            Units.DestinationYieldForUnits[unit] = -1;
            Units.DestinationYieldForCommandVersions[unit] = 0;
            Units.DestinationYieldDeadlines[unit] = 0;
            Units.DestinationYieldCooldownTicks[unit] = 0;
            Units.Paths[unit] = null;
            Units.RouteWaypoints[unit] = LastIssuedGroupRoute.Waypoints;
            Units.PathPending[unit] = true;
            Units.Modes[unit] = UnitMoveMode.WaitingForPath;
            Units.ProgressOrigins[unit] = Units.Positions[unit];
            Units.ProgressTimers[unit] = 0f;
            Units.ProgressBestDistances[unit] = Vector2.Distance(
                Units.Positions[unit], Units.SlotTargets[unit]);
            Units.RepathCooldowns[unit] = 0f;
            Units.AvoidanceSides[unit] = 0;
            Units.AvoidanceLockTicks[unit] = 0;
            Units.BlockedByNavigation[unit] = false;
            Units.RecoveryStages[unit] = RecoveryStage.Normal;
            Units.RecoveryEventCounts[unit] = 0;
            Units.RecoveryStableTimers[unit] = 0f;
            Units.RecoveryRetryCounts[unit] = 0;
            _pathRequests.Enqueue(new PathRequest(unit, Units.CommandVersions[unit]));
        }
    }

    public void Stop(ReadOnlySpan<int> unitIndices) =>
        IssueOrder(
            unitIndices,
            new UnitOrder(UnitOrderKind.Stop, Vector2.Zero),
            queued: false);

    public void Hold(ReadOnlySpan<int> unitIndices) =>
        IssueOrder(
            unitIndices,
            new UnitOrder(UnitOrderKind.Hold, Vector2.Zero),
            queued: false);

    private void ExecuteStop(ReadOnlySpan<int> unitIndices)
    {
        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            ValidateUnitIndex(unit);
            if (!Units.Alive[unit])
            {
                continue;
            }
            Combat.SetCommand(unit, UnitCommandIntent.Stop, Units.Positions[unit]);
            Units.CommandVersions[unit]++;
            Units.Paths[unit] = null;
            Units.RouteWaypoints[unit] = [];
            Units.PathPending[unit] = false;
            Units.Modes[unit] = UnitMoveMode.Idle;
            Units.SlotTargets[unit] = Units.Positions[unit];
            Units.MoveGoals[unit] = Units.Positions[unit];
            Units.MovementGroupIds[unit] = 0;
            Units.MovementGroupSizes[unit] = 0;
            Units.SlotReflowCooldownTicks[unit] = 0;
            Units.DestinationBestDistances[unit] = 0f;
            Units.DestinationStallTicks[unit] = 0;
            Units.DestinationNearTicks[unit] = 0;
            Units.DestinationOverflowed[unit] = false;
            Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
            Units.DestinationYieldReturnTargets[unit] = Units.Positions[unit];
            Units.DestinationYieldPoints[unit] = Units.Positions[unit];
            Units.DestinationYieldForUnits[unit] = -1;
            Units.DestinationYieldForCommandVersions[unit] = 0;
            Units.DestinationYieldDeadlines[unit] = 0;
            Units.DestinationYieldCooldownTicks[unit] = 0;
            Units.ActiveChokeIds[unit] = -1;
            Units.ChokeDirections[unit] = 0;
            Units.ChokePhases[unit] = ChokePhase.None;
            Units.ChokeAdmitted[unit] = false;
            Units.ChokeQueueRanks[unit] = -1;
            Units.ChokeWaitTicks[unit] = 0;
            Units.BlockedByNavigation[unit] = false;
            Units.RecoveryStages[unit] = RecoveryStage.Normal;
            Units.RecoveryEventCounts[unit] = 0;
            Units.RecoveryStableTimers[unit] = 0f;
            Units.RecoveryRetryCounts[unit] = 0;
        }
    }

    private void ExecuteHold(ReadOnlySpan<int> unitIndices)
    {
        ExecuteStop(unitIndices);
        for (var i = 0; i < unitIndices.Length; i++)
        {
            var unit = unitIndices[i];
            if (!Units.Alive[unit])
            {
                continue;
            }
            Units.Modes[unit] = UnitMoveMode.Hold;
            Combat.SetCommand(unit, UnitCommandIntent.Hold, Units.Positions[unit]);
        }
    }

    private void IssueOrder(
        ReadOnlySpan<int> unitIndices,
        UnitOrder order,
        bool queued)
    {
        if (unitIndices.IsEmpty)
        {
            return;
        }
        if (order.Kind == UnitOrderKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(order));
        }

        for (var index = 0; index < unitIndices.Length; index++)
        {
            ValidateUnitIndex(unitIndices[index]);
            ValidateLivingUnit(unitIndices[index]);
        }

        if (!_issuingSystemOrder)
        {
            Economy.Cancel(unitIndices);
            Construction.InterruptBuilders(unitIndices);
        }

        if (!_issuingSystemOrder)
        {
            _commandRecorder?.Record(Metrics.Tick, unitIndices, order, queued);
        }
        order = order with { SequenceId = NextOrderSequenceId() };
        if (!queued || order.Kind is UnitOrderKind.Stop or UnitOrderKind.Hold)
        {
            for (var index = 0; index < unitIndices.Length; index++)
            {
                CommandQueues.ClearPending(unitIndices[index]);
            }
            ExecuteOrderGroup(unitIndices, order, wasQueued: false);
            return;
        }

        var immediateCount = 0;
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            if (CommandQueues.PendingCounts[unit] == 0 &&
                IsActiveOrderComplete(unit))
            {
                _orderDispatchUnits[immediateCount++] = unit;
            }
            else
            {
                CommandQueues.TryEnqueue(unit, order);
            }
        }

        if (immediateCount > 0)
        {
            ExecuteOrderGroup(
                _orderDispatchUnits.AsSpan(0, immediateCount),
                order,
                wasQueued: true);
        }
    }

    private void ExecuteOrderGroup(
        ReadOnlySpan<int> unitIndices,
        UnitOrder order,
        bool wasQueued)
    {
        switch (order.Kind)
        {
            case UnitOrderKind.Move:
                ExecuteMoveOrder(unitIndices, order.TargetPosition, UnitCommandIntent.Move);
                break;
            case UnitOrderKind.AttackMove:
                ExecuteMoveOrder(
                    unitIndices,
                    order.TargetPosition,
                    UnitCommandIntent.AttackMove);
                break;
            case UnitOrderKind.AttackTarget:
                ExecuteAttackTarget(unitIndices, order.TargetUnit);
                break;
            case UnitOrderKind.AttackBuilding:
                ExecuteAttackBuilding(unitIndices, order.TargetBuilding);
                break;
            case UnitOrderKind.GatherResource:
                ExecuteGatherResourceTask(
                    unitIndices, order.TargetResourceNode);
                break;
            case UnitOrderKind.ResumeConstruction:
                ExecuteResumeConstructionTask(
                    unitIndices, order.TargetBuilding);
                break;
            case UnitOrderKind.Stop:
                ExecuteStop(unitIndices);
                break;
            case UnitOrderKind.Hold:
                ExecuteHold(unitIndices);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(order));
        }

        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            if (Units.Alive[unit] && !_issuingSystemOrder)
            {
                CommandQueues.Begin(unit, order, wasQueued);
            }
        }
    }

    private void ExecuteGatherResourceTask(
        ReadOnlySpan<int> unitIndices,
        int targetResourceNode)
    {
        var resource = new EconomyResourceNodeId(targetResourceNode);
        Span<int> worker = stackalloc int[1];
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            var playerId = Combat.Teams[unit];
            if (!Economy.ValidateGather(playerId, unit, resource).Succeeded)
                continue;
            worker[0] = unit;
            Construction.InterruptBuilders(worker);
            Economy.BeginGather(unit, resource);
            MoveEconomyWorker(unit, Economy.ResourceNodePosition(resource));
        }
    }

    private void ExecuteResumeConstructionTask(
        ReadOnlySpan<int> unitIndices,
        int targetBuilding)
    {
        var building = new GameplayBuildingId(targetBuilding);
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            TryResumeConstruction(
                Combat.Teams[unit], building, unit,
                replaceUnitOrders: false, recordCommand: false);
        }
    }

    private void ExecuteAttackTarget(ReadOnlySpan<int> unitIndices, int targetUnit)
    {
        ExecuteStop(unitIndices);
        if ((uint)targetUnit >= (uint)Units.Count || !Units.Alive[targetUnit])
        {
            return;
        }

        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            if (Combat.Teams[unit] == Combat.Teams[targetUnit])
            {
                throw new InvalidOperationException(
                    $"Unit {targetUnit} is friendly to attacker {unit}.");
            }
            Combat.SetCommand(
                unit,
                UnitCommandIntent.AttackTarget,
                Units.Positions[targetUnit],
                targetUnit);
        }
    }

    private void ExecuteAttackBuilding(
        ReadOnlySpan<int> unitIndices,
        int targetBuilding)
    {
        ExecuteStop(unitIndices);
        var buildingId = new GameplayBuildingId(targetBuilding);
        if (!Construction.IsAlive(buildingId))
        {
            return;
        }
        var target = Construction.Observe(buildingId);
        var center = (target.Bounds.Min + target.Bounds.Max) * 0.5f;
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            if (!Construction.IsEnemyTarget(
                    buildingId, Combat.Teams[unit]))
            {
                throw new InvalidOperationException(
                    $"Building {targetBuilding} is friendly to attacker {unit}.");
            }
            Combat.SetCommand(
                unit,
                UnitCommandIntent.AttackTarget,
                center,
                targetBuilding: targetBuilding);
        }
    }

    public void Tick(float delta)
    {
        if (delta <= 0f || !float.IsFinite(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        var tickStart = Stopwatch.GetTimestamp();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        Metrics.Tick++;
        Metrics.PathsCompleted = 0;
        Metrics.PathsFailed = 0;
        Metrics.CollisionPairs = 0;
        Metrics.MaximumPenetration = 0f;
        Metrics.RepathRequests = 0;
        Metrics.NavigationRevision = World.NavigationRevision;
        Metrics.NavigationInvalidations = _pendingNavigationInvalidations;
        Metrics.RecoveryEvents = 0;
        _pendingNavigationInvalidations = 0;

        var phaseStart = Stopwatch.GetTimestamp();
        Construction.Update(
            delta,
            Units,
            Economy,
            _constructionMoveWorker,
            _constructionStopWorker);
        Production.Update(
            delta,
            Construction,
            Economy.Players,
            Units,
            World,
            _productionSpawnUnit,
            _productionApplyRally);
        Technology.Update(delta, Construction, Economy.Players);
        Economy.Update(
            delta, Units, _economyMoveWorker, _economyStopWorker);
        Metrics.EconomyMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        _combatSystem.Update(delta, Metrics.Tick);
        Metrics.CombatMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        ProcessPathRequests();
        Metrics.PathMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        UpdatePreferredVelocities();
        Metrics.PreferredVelocityMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        _chokeController?.ApplyPreferredVelocities(Units);
        Metrics.ChokeMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        _spatialHash.Rebuild(Units);
        Metrics.SpatialHashMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        _steeringSolver.Solve(Units, delta);
        _chokeController?.ConstrainSolvedVelocities(Units);
        Metrics.SteeringMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        Integrate(delta);
        Metrics.IntegrateMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        SolveCollisions();
        _chokeController?.ConstrainPositions(Units);
        Metrics.CollisionMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        UpdateProgress(delta);
        _overflowResolver.UpdateStallTracking(Units);
        UpdateDestinationLocalRematch();
        UpdateDestinationReflow();
        UpdateDestinationYielding();
        UpdateDestinationOverflow();
        UpdateMetrics();
        Metrics.RecoveryMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        AdvanceQueuedOrders();
        Visibility.Update(Units, Combat, Construction);
        Match.Update(Metrics.Tick, Construction);
        Metrics.CommandMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.TotalMilliseconds = Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
        Metrics.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
    }

    private int SpawnProducedUnit(
        UnitTypeProfile type,
        int playerId,
        Vector2 position)
    {
        var unit = AddUnit(position, type.Movement, playerId, type.Combat);
        if (type.IsWorker) Economy.RegisterWorker(unit, playerId);
        return unit;
    }

    private bool ValidateRallyReference(int playerId, RallyTarget rally)
    {
        if (!ProductionSystem.ValidRallyTarget(rally)) return false;
        return rally.Kind switch
        {
            RallyTargetKind.ResourceNode =>
                rally.ResourceNode.Value < Economy.ResourceNodeCount &&
                Economy.ResourceNodePosition(rally.ResourceNode) == rally.Position,
            RallyTargetKind.FriendlyUnit =>
                (uint)rally.Unit < (uint)Units.Count && Units.Alive[rally.Unit] &&
                Combat.Teams[rally.Unit] == playerId,
            _ => true
        };
    }

    private void ApplyProducedUnitRally(
        int unit,
        int playerId,
        RallyTarget rally)
    {
        _issuingSystemOrder = true;
        try
        {
            Span<int> produced = stackalloc int[1];
            produced[0] = unit;
            switch (rally.Kind)
            {
                case RallyTargetKind.ResourceNode:
                    if (Economy.IsWorkerOwnedBy(unit, playerId) &&
                        Economy.ValidateGather(
                            playerId, unit, rally.ResourceNode).Succeeded)
                    {
                        Economy.BeginGather(unit, rally.ResourceNode);
                    }
                    else
                    {
                        IssueMove(produced, rally.Position);
                    }
                    break;
                case RallyTargetKind.FriendlyUnit:
                    var target = (uint)rally.Unit < (uint)Units.Count &&
                                 Units.Alive[rally.Unit] &&
                                 Combat.Teams[rally.Unit] == playerId
                        ? Units.Positions[rally.Unit]
                        : rally.Position;
                    IssueMove(produced, target);
                    break;
                case RallyTargetKind.Ground:
                    IssueMove(produced, rally.Position);
                    break;
            }
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private void ProcessPathRequests()
    {
        if (!_pathProvider.IsReady)
        {
            return;
        }

        var requestsToProcess = Math.Min(PathBudgetPerTick, _pathRequests.Count);
        for (var requestIndex = 0; requestIndex < requestsToProcess; requestIndex++)
        {
            var request = _pathRequests.Dequeue();
            var unit = request.UnitIndex;
            if (!Units.Alive[unit] ||
                request.CommandVersion != Units.CommandVersions[unit] ||
                !Units.PathPending[unit])
            {
                continue;
            }

            var start = Units.Positions[unit];
            var goal = Units.SlotTargets[unit];
            var points = BuildUnitPath(unit, start, goal);

            if (request.CommandVersion != Units.CommandVersions[unit])
            {
                continue;
            }

            Units.PathPending[unit] = false;
            if (points.Length == 0 ||
                Vector2.DistanceSquared(points[^1], goal) > 24f * 24f)
            {
                Units.Paths[unit] = null;
                Metrics.PathsFailed++;
                HandlePathFailure(unit);
                continue;
            }

            if (points.Length == 1)
            {
                points = [start, points[0]];
            }

            Units.Paths[unit] = new UnitPath(points, request.CommandVersion);
            Units.Modes[unit] = UnitMoveMode.Moving;
            Units.BlockedByNavigation[unit] = false;
            Units.ProgressBestDistances[unit] = RemainingPathDistance(
                unit, Units.Paths[unit]);
            Metrics.PathsCompleted++;
        }
    }

    private Vector2[] BuildUnitPath(int unit, Vector2 start, Vector2 finalGoal)
    {
        var result = new List<Vector2>(12) { start };
        var current = start;
        var route = Units.RouteWaypoints[unit];
        var navigationRadius = Units.NavigationRadii[unit];

        for (var waypointIndex = 0; waypointIndex <= route.Length; waypointIndex++)
        {
            var segmentGoal = waypointIndex < route.Length
                ? ResolveUnitRouteWaypoint(unit, route[waypointIndex])
                : finalGoal;
            Vector2[] segment;
            if (World.IsSegmentFree(current, segmentGoal, navigationRadius))
            {
                segment = [current, segmentGoal];
            }
            else
            {
                segment = _pathProvider.FindPath(
                    current, segmentGoal, navigationRadius);
            }

            if (segment.Length == 0 ||
                Vector2.DistanceSquared(segment[^1], segmentGoal) > 24f * 24f ||
                !PathIsNavigable(segment, navigationRadius))
            {
                return [];
            }

            for (var pointIndex = 1; pointIndex < segment.Length; pointIndex++)
            {
                if (Vector2.DistanceSquared(result[^1], segment[pointIndex]) > 0.5f * 0.5f)
                {
                    result.Add(segment[pointIndex]);
                }
            }

            current = segment[^1];
        }

        return result.ToArray();
    }

    private bool PathIsNavigable(ReadOnlySpan<Vector2> points, float radius)
    {
        for (var index = 1; index < points.Length; index++)
        {
            if (!World.IsSegmentFree(points[index - 1], points[index], radius))
            {
                return false;
            }
        }

        return points.Length > 0;
    }

    private void InvalidatePathsIntersecting(SimRect footprint)
    {
        var affectedUnits = new List<int>();
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (Units.Modes[unit] is UnitMoveMode.Idle or UnitMoveMode.Hold or UnitMoveMode.Arrived ||
                !RemainingPathIntersects(unit, footprint))
            {
                continue;
            }

            affectedUnits.Add(unit);
        }

        if (affectedUnits.Count == 0)
        {
            return;
        }

        _pendingNavigationInvalidations += affectedUnits.Count;
        ReplanInvalidatedGroups(affectedUnits);
    }

    private void ReplanInvalidatedGroups(List<int> affectedUnits)
    {
        var processed = new bool[affectedUnits.Count];
        for (var affectedIndex = 0; affectedIndex < affectedUnits.Count; affectedIndex++)
        {
            if (processed[affectedIndex])
            {
                continue;
            }

            var firstUnit = affectedUnits[affectedIndex];
            var movementGroupId = Units.MovementGroupIds[firstUnit];
            var group = new List<int>();
            for (var candidateIndex = affectedIndex;
                 candidateIndex < affectedUnits.Count;
                 candidateIndex++)
            {
                var candidate = affectedUnits[candidateIndex];
                var sameGroup = movementGroupId > 0
                    ? Units.MovementGroupIds[candidate] == movementGroupId
                    : candidate == firstUnit;
                if (!processed[candidateIndex] && sameGroup)
                {
                    processed[candidateIndex] = true;
                    group.Add(candidate);
                }
            }

            ReplanInvalidatedGroup(group.ToArray());
        }
    }

    private void ReplanInvalidatedGroup(int[] group)
    {
        var centroid = Vector2.Zero;
        var maximumNavigationRadius = 0f;
        for (var index = 0; index < group.Length; index++)
        {
            var unit = group[index];
            centroid += Units.Positions[unit];
            maximumNavigationRadius = MathF.Max(
                maximumNavigationRadius, Units.NavigationRadii[unit]);
        }

        centroid /= group.Length;
        var groupGoal = Units.MoveGoals[group[0]];
        var route = PlanGroupRoute(
            centroid, groupGoal, maximumNavigationRadius);
        if (group.Length > 1 && route.Waypoints.Length > 0)
        {
            Metrics.SharedRouteAssignments += group.Length;
        }

        _chokeController?.AssignForMove(
            Units,
            group,
            centroid,
            groupGoal,
            route.ChokeIds);
        for (var index = 0; index < group.Length; index++)
        {
            var unit = group[index];
            Units.RouteWaypoints[unit] = route.Waypoints;
            QueueNavigationReplan(
                unit,
                resetRecovery: true,
                countInvalidation: false,
                sharedRoute: route,
                assignChoke: false);
        }
    }

    private bool RemainingPathIntersects(int unit, SimRect footprint)
    {
        var expanded = footprint.Expanded(Units.NavigationRadii[unit]);
        var path = Units.Paths[unit];
        var previous = Units.Positions[unit];
        if (path is not null)
        {
            for (var index = path.Cursor; index < path.Points.Length; index++)
            {
                if (expanded.SegmentIntersects(previous, path.Points[index]))
                {
                    return true;
                }

                previous = path.Points[index];
            }

            return false;
        }

        var route = Units.RouteWaypoints[unit];
        for (var index = 0; index < route.Length; index++)
        {
            if (expanded.SegmentIntersects(previous, route[index]))
            {
                return true;
            }

            previous = route[index];
        }

        return expanded.SegmentIntersects(previous, Units.SlotTargets[unit]);
    }

    private void QueueNavigationReplan(
        int unit,
        bool resetRecovery = true,
        bool countInvalidation = true,
        GroupRoutePlan? sharedRoute = null,
        bool assignChoke = true)
    {
        Units.CommandVersions[unit]++;
        var route = sharedRoute ?? PlanGroupRoute(
            Units.Positions[unit],
            Units.SlotTargets[unit],
            Units.NavigationRadii[unit]);
        Units.RouteWaypoints[unit] = route.Waypoints;
        if (assignChoke && _chokeController is not null)
        {
            int[] singleUnit = [unit];
            _chokeController.AssignForMove(
                Units,
                singleUnit,
                Units.Positions[unit],
                Units.SlotTargets[unit],
                route.ChokeIds);
        }

        Units.Paths[unit] = null;
        Units.PathPending[unit] = true;
        Units.BlockedByNavigation[unit] = false;
        Units.Modes[unit] = UnitMoveMode.WaitingForPath;
        Units.ProgressOrigins[unit] = Units.Positions[unit];
        Units.ProgressTimers[unit] = 0f;
        Units.ProgressBestDistances[unit] = Vector2.Distance(
            Units.Positions[unit], Units.SlotTargets[unit]);
        Units.RepathCooldowns[unit] = 0f;
        if (resetRecovery)
        {
            Units.RecoveryStages[unit] = RecoveryStage.Normal;
            Units.RecoveryStableTimers[unit] = 0f;
            Units.RecoveryRetryCounts[unit] = 0;
        }
        _pathRequests.Enqueue(new PathRequest(unit, Units.CommandVersions[unit]));
        if (countInvalidation)
        {
            _pendingNavigationInvalidations++;
        }
    }

    private GroupRoutePlan PlanGroupRoute(
        Vector2 start,
        Vector2 goal,
        float agentRadius)
    {
        if (_groupRoutePlanner is null)
        {
            return GroupRoutePlan.Empty;
        }

        Metrics.GroupRoutePlans++;
        return _groupRoutePlanner.Plan(start, goal, agentRadius);
    }

    private int NextMovementGroupId()
    {
        if (_nextMovementGroupId == int.MaxValue)
        {
            _nextMovementGroupId = 1;
        }

        return _nextMovementGroupId++;
    }

    private void QueueLocalRepath(int unit, bool clearHighLevelRoute)
    {
        Units.CommandVersions[unit]++;
        if (clearHighLevelRoute)
        {
            Units.RouteWaypoints[unit] = [];
            Units.ActiveChokeIds[unit] = -1;
            Units.ChokeDirections[unit] = 0;
            Units.ChokePhases[unit] = ChokePhase.None;
            Units.ChokeAdmitted[unit] = false;
        }

        Units.Paths[unit] = null;
        Units.PathPending[unit] = true;
        Units.BlockedByNavigation[unit] = false;
        Units.Modes[unit] = UnitMoveMode.WaitingForPath;
        Units.ProgressOrigins[unit] = Units.Positions[unit];
        Units.ProgressTimers[unit] = 0f;
        Units.ProgressBestDistances[unit] = Vector2.Distance(
            Units.Positions[unit], Units.SlotTargets[unit]);
        Units.RepathCooldowns[unit] = 0.45f;
        _pathRequests.Enqueue(new PathRequest(unit, Units.CommandVersions[unit]));
        Metrics.RepathRequests++;
    }

    private void HandlePathFailure(int unit)
    {
        switch (Units.RecoveryStages[unit])
        {
            case RecoveryStage.LocalRepath:
                SetRecoveryStage(unit, RecoveryStage.RouteReplan);
                QueueNavigationReplan(unit, resetRecovery: false, countInvalidation: false);
                Metrics.RepathRequests++;
                return;
            case RecoveryStage.RouteReplan:
                SetRecoveryStage(unit, RecoveryStage.DirectFallback);
                QueueLocalRepath(unit, clearHighLevelRoute: true);
                return;
            default:
                MarkUnreachable(unit);
                return;
        }
    }

    private void MarkUnreachable(int unit)
    {
        Units.Paths[unit] = null;
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Idle;
        Units.Velocities[unit] = Vector2.Zero;
        Units.BlockedByNavigation[unit] = true;
        SetRecoveryStage(unit, RecoveryStage.Unreachable);
    }

    private void SetRecoveryStage(int unit, RecoveryStage stage)
    {
        if (Units.RecoveryStages[unit] == stage)
        {
            return;
        }

        Units.RecoveryStages[unit] = stage;
        Units.RecoveryStableTimers[unit] = 0f;
        Units.RecoveryEventCounts[unit]++;
        Metrics.RecoveryEvents++;
    }

    private Vector2 ResolveUnitRouteWaypoint(int unit, Vector2 groupWaypoint)
    {
        if (_chokeController is null)
        {
            return groupWaypoint;
        }

        var chokeId = Units.ActiveChokeIds[unit];
        var definitions = _chokeController.Definitions;
        if ((uint)chokeId >= (uint)definitions.Length)
        {
            return groupWaypoint;
        }

        var choke = definitions[chokeId];
        var endpointToleranceSquared = 2f * 2f;
        if (Vector2.DistanceSquared(groupWaypoint, choke.A) <= endpointToleranceSquared ||
            Vector2.DistanceSquared(groupWaypoint, choke.B) <= endpointToleranceSquared)
        {
            return groupWaypoint + choke.Normal * Units.ChokeLaneOffsets[unit];
        }

        return groupWaypoint;
    }

    private void UpdatePreferredVelocities()
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            Units.PreferredVelocities[unit] = Vector2.Zero;
            if (!Units.Alive[unit])
            {
                continue;
            }
            if (Units.Modes[unit] == UnitMoveMode.Arrived &&
                Vector2.DistanceSquared(Units.Positions[unit], Units.SlotTargets[unit]) >
                8f * 8f)
            {
                Units.Paths[unit] = new UnitPath(
                    [Units.Positions[unit], Units.SlotTargets[unit]],
                    Units.CommandVersions[unit]);
                Units.Modes[unit] = UnitMoveMode.Moving;
            }

            if (Units.Modes[unit] != UnitMoveMode.Moving)
            {
                continue;
            }

            var path = Units.Paths[unit];
            if (path is null || path.CommandVersion != Units.CommandVersions[unit])
            {
                Units.Modes[unit] = UnitMoveMode.WaitingForPath;
                continue;
            }

            AdvancePathCursor(unit, path);
            var finalPoint = path.Cursor >= path.Points.Length - 1;
            var waypoint = path.Points[path.Cursor];
            var toWaypoint = waypoint - Units.Positions[unit];
            var distance = toWaypoint.Length();

            if (finalPoint && distance <= ArrivalStopRadius)
            {
                Units.Modes[unit] = UnitMoveMode.Arrived;
                Units.Paths[unit] = null;
                continue;
            }

            if (distance < 0.0001f)
            {
                continue;
            }

            var speed = Units.MaxSpeeds[unit];
            if (finalPoint && distance < ArrivalSlowRadius)
            {
                var factor = Math.Clamp(distance / ArrivalSlowRadius, 0.18f, 1f);
                speed *= factor * factor * (3f - 2f * factor);
            }

            Units.PreferredVelocities[unit] = toWaypoint / distance * speed;
        }
    }

    private void AdvancePathCursor(int unit, UnitPath path)
    {
        while (path.Cursor < path.Points.Length - 1)
        {
            var position = Units.Positions[unit];
            var point = path.Points[path.Cursor];
            var reachedRadius = Vector2.DistanceSquared(position, point) <=
                                WaypointRadius * WaypointRadius;
            var nextDirection = path.Points[path.Cursor + 1] - point;
            var crossedWaypointPlane = nextDirection.LengthSquared() > 0.0001f &&
                                       Vector2.Dot(position - point, nextDirection) >= 0f;
            if (!reachedRadius && !crossedWaypointPlane)
            {
                break;
            }

            path.Cursor++;
        }
    }

    private void Integrate(float delta)
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            Units.PreviousPositions[unit] = Units.Positions[unit];
            if (!Units.Alive[unit])
            {
                Units.Velocities[unit] = Vector2.Zero;
                Units.NextVelocities[unit] = Vector2.Zero;
                continue;
            }
            Units.Velocities[unit] = Units.NextVelocities[unit];
            var proposed = Units.Positions[unit] + Units.Velocities[unit] * delta;
            Units.Positions[unit] = World.ConstrainDisc(
                Units.Positions[unit],
                proposed,
                Units.Radii[unit]);
        }
    }

    private void SolveCollisions()
    {
        for (var iteration = 0; iteration < CollisionIterations; iteration++)
        {
            Array.Clear(Units.CollisionCorrections, 0, Units.Count);
            _spatialHash.Rebuild(Units);

            for (var unit = 0; unit < Units.Count; unit++)
            {
                if (!Units.Alive[unit])
                {
                    continue;
                }
                var neighborCount = _spatialHash.Query(
                    Units.Positions[unit],
                    Units.Radii[unit] * 4f + 8f,
                    unit,
                    _collisionNeighbors);

                for (var neighborIndex = 0; neighborIndex < neighborCount; neighborIndex++)
                {
                    var neighbor = _collisionNeighbors[neighborIndex];
                    if (neighbor <= unit)
                    {
                        continue;
                    }

                    var offset = Units.Positions[neighbor] - Units.Positions[unit];
                    var minimumDistance = Units.Radii[unit] + Units.Radii[neighbor];
                    var distanceSquared = offset.LengthSquared();
                    if (distanceSquared >= minimumDistance * minimumDistance)
                    {
                        continue;
                    }

                    var distance = MathF.Sqrt(MathF.Max(distanceSquared, 0.000001f));
                    var normal = distanceSquared > 0.000001f
                        ? offset / distance
                        : DeterministicNormal(unit, neighbor);
                    var penetration = minimumDistance - distance;
                    Metrics.CollisionPairs++;
                    Metrics.MaximumPenetration = MathF.Max(
                        Metrics.MaximumPenetration,
                        penetration);

                    var inverseA = InversePushMass(Units.Modes[unit]);
                    var inverseB = InversePushMass(Units.Modes[neighbor]);
                    var totalInverse = inverseA + inverseB;
                    if (totalInverse <= 0f)
                    {
                        continue;
                    }

                    var correction = normal * (penetration + 0.01f) * 0.72f;
                    Units.CollisionCorrections[unit] -= correction * (inverseA / totalInverse);
                    Units.CollisionCorrections[neighbor] += correction * (inverseB / totalInverse);
                }
            }

            for (var unit = 0; unit < Units.Count; unit++)
            {
                if (!Units.Alive[unit])
                {
                    continue;
                }
                var previous = Units.Positions[unit];
                Units.Positions[unit] = World.ConstrainDisc(
                    previous,
                    previous + Units.CollisionCorrections[unit],
                    Units.Radii[unit]);
            }
        }
    }

    private void UpdateProgress(float delta)
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit])
            {
                continue;
            }
            Units.RepathCooldowns[unit] = MathF.Max(0f, Units.RepathCooldowns[unit] - delta);
            if (Units.Modes[unit] != UnitMoveMode.Moving)
            {
                Units.ProgressOrigins[unit] = Units.Positions[unit];
                Units.ProgressTimers[unit] = 0f;
                continue;
            }

            if (Units.ChokePhases[unit] != ChokePhase.None)
            {
                Units.ProgressOrigins[unit] = Units.Positions[unit];
                Units.ProgressTimers[unit] = 0f;
                continue;
            }

            if (Vector2.DistanceSquared(
                    Units.Positions[unit], Units.SlotTargets[unit]) <= 70f * 70f)
            {
                Units.ProgressOrigins[unit] = Units.Positions[unit];
                Units.ProgressTimers[unit] = 0f;
                continue;
            }

            Units.RecoveryStableTimers[unit] += delta;
            if (Units.RecoveryStages[unit] != RecoveryStage.Normal &&
                Units.RecoveryStableTimers[unit] >= 2.5f)
            {
                Units.RecoveryStages[unit] = RecoveryStage.Normal;
                Units.RecoveryStableTimers[unit] = 0f;
                Units.RecoveryRetryCounts[unit] = 0;
            }

            Units.ProgressTimers[unit] += delta;
            var remainingDistance = RemainingPathDistance(unit, Units.Paths[unit]);
            if (remainingDistance <= Units.ProgressBestDistances[unit] - 5f)
            {
                Units.ProgressOrigins[unit] = Units.Positions[unit];
                Units.ProgressBestDistances[unit] = remainingDistance;
                Units.ProgressTimers[unit] = 0f;
                continue;
            }

            if (Units.ProgressTimers[unit] < 1.5f ||
                Units.RepathCooldowns[unit] > 0f ||
                Units.PathPending[unit])
            {
                continue;
            }

            Units.ProgressTimers[unit] = 0f;
            Units.ProgressOrigins[unit] = Units.Positions[unit];
            Units.RecoveryStableTimers[unit] = 0f;
            switch (Units.RecoveryStages[unit])
            {
                case RecoveryStage.Normal:
                    Units.AvoidanceSides[unit] = Units.AvoidanceSides[unit] == 0
                        ? (sbyte)((unit & 1) == 0 ? 1 : -1)
                        : (sbyte)-Units.AvoidanceSides[unit];
                    Units.AvoidanceLockTicks[unit] = 90;
                    Units.RepathCooldowns[unit] = 0.4f;
                    SetRecoveryStage(unit, RecoveryStage.AvoidanceFlip);
                    break;
                case RecoveryStage.AvoidanceFlip:
                    SetRecoveryStage(unit, RecoveryStage.LocalRepath);
                    QueueLocalRepath(unit, clearHighLevelRoute: false);
                    break;
                case RecoveryStage.LocalRepath:
                    SetRecoveryStage(unit, RecoveryStage.RouteReplan);
                    QueueNavigationReplan(
                        unit,
                        resetRecovery: false,
                        countInvalidation: false);
                    Metrics.RepathRequests++;
                    break;
                case RecoveryStage.RouteReplan:
                    SetRecoveryStage(unit, RecoveryStage.DirectFallback);
                    QueueLocalRepath(unit, clearHighLevelRoute: true);
                    break;
                case RecoveryStage.DirectFallback:
                    SetRecoveryStage(unit, RecoveryStage.WaitingForClearance);
                    Units.RepathCooldowns[unit] = 2f;
                    break;
                case RecoveryStage.WaitingForClearance:
                    if (Units.RecoveryRetryCounts[unit] < 2)
                    {
                        Units.RecoveryRetryCounts[unit]++;
                        SetRecoveryStage(unit, RecoveryStage.DirectFallback);
                        QueueLocalRepath(unit, clearHighLevelRoute: true);
                    }
                    else
                    {
                        MarkUnreachable(unit);
                    }
                    break;
            }
        }
    }

    private void UpdateDestinationReflow()
    {
        if (Metrics.Tick % DestinationReflowIntervalTicks != 0)
        {
            return;
        }

        for (var swap = 0; swap < MaximumDestinationSwapsPerPass; swap++)
        {
            if (!_slotReflow.TryFindSwap(
                    Units,
                    Metrics.Tick,
                    out var firstUnit,
                    out var secondUnit))
            {
                break;
            }

            (Units.SlotTargets[firstUnit], Units.SlotTargets[secondUnit]) =
                (Units.SlotTargets[secondUnit], Units.SlotTargets[firstUnit]);
            var nextEligibleTick = Metrics.Tick + DestinationReflowCooldownTicks;
            Units.SlotReflowCooldownTicks[firstUnit] = nextEligibleTick;
            Units.SlotReflowCooldownTicks[secondUnit] = nextEligibleTick;
            QueueDestinationRetarget(firstUnit, preserveTerminalHistory: true);
            QueueDestinationRetarget(secondUnit, preserveTerminalHistory: true);
            Metrics.DestinationSlotSwaps++;
        }
    }

    private void UpdateDestinationLocalRematch()
    {
        if (Metrics.Tick % DestinationLocalRematchIntervalTicks != 0)
        {
            return;
        }

        Span<int> rematchedUnits =
            stackalloc int[DestinationLocalRematcher.MaximumUnits];
        Span<Vector2> rematchedTargets =
            stackalloc Vector2[DestinationLocalRematcher.MaximumUnits];
        if (!_localRematcher.TryBuildPlan(
                Units,
                Metrics.Tick,
                rematchedUnits,
                rematchedTargets,
                out var rematchedCount))
        {
            return;
        }

        var nextEligibleTick =
            Metrics.Tick + DestinationLocalRematchCooldownTicks;
        for (var index = 0; index < rematchedCount; index++)
        {
            var unit = rematchedUnits[index];
            Units.SlotTargets[unit] = rematchedTargets[index];
            Units.SlotReflowCooldownTicks[unit] = nextEligibleTick;
        }

        for (var index = 0; index < rematchedCount; index++)
        {
            QueueDestinationRetarget(
                rematchedUnits[index], preserveTerminalHistory: true);
        }

        Metrics.DestinationLocalRematches++;
        Metrics.DestinationLocalRematchedUnits += rematchedCount;
    }

    private void UpdateDestinationOverflow()
    {
        if (Metrics.Tick % DestinationOverflowIntervalTicks != 0)
        {
            return;
        }

        for (var assignment = 0;
             assignment < MaximumDestinationOverflowsPerPass;
             assignment++)
        {
            if (!_overflowResolver.TryFindOverflowAssignment(
                    Units,
                    out var unit,
                    out var overflowTarget))
            {
                break;
            }

            Units.SlotTargets[unit] = overflowTarget;
            Units.DestinationOverflowed[unit] = true;
            QueueDestinationRetarget(unit);
            Metrics.DestinationOverflowAssignments++;
        }
    }

    private void UpdateDestinationYielding()
    {
        var activeYields = 0;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            var phase = Units.DestinationYieldPhases[unit];
            if (phase == DestinationYieldPhase.None)
            {
                continue;
            }

            activeYields++;
            switch (phase)
            {
                case DestinationYieldPhase.MovingAside:
                    if (Units.Modes[unit] == UnitMoveMode.Arrived ||
                        Vector2.DistanceSquared(
                            Units.Positions[unit], Units.SlotTargets[unit]) <= 5f * 5f)
                    {
                        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.Waiting;
                    }
                    else if (Metrics.Tick >= Units.DestinationYieldDeadlines[unit])
                    {
                        BeginDestinationYieldReturn(unit);
                    }
                    break;
                case DestinationYieldPhase.Waiting:
                    if (DestinationYieldTargetCleared(unit) ||
                        Metrics.Tick >= Units.DestinationYieldDeadlines[unit])
                    {
                        BeginDestinationYieldReturn(unit);
                    }
                    break;
                case DestinationYieldPhase.Returning:
                    if (Units.Modes[unit] == UnitMoveMode.Arrived ||
                        Vector2.DistanceSquared(
                            Units.Positions[unit], Units.SlotTargets[unit]) <= 5f * 5f)
                    {
                        ClearDestinationYield(unit);
                        activeYields--;
                    }
                    else if (Metrics.Tick >= Units.DestinationYieldDeadlines[unit])
                    {
                        ConvertDestinationYieldToOverflow(unit);
                        activeYields--;
                    }
                    break;
            }
        }

        if (Metrics.Tick % DestinationYieldIntervalTicks != 0)
        {
            return;
        }

        for (var start = 0;
             start < MaximumDestinationYieldStartsPerPass &&
             activeYields < MaximumActiveDestinationYields;
             start++)
        {
            if (!_yieldResolver.TryFindYield(
                    Units,
                    Metrics.Tick,
                    out var blockedUnit,
                    out var blockerUnit,
                    out var yieldPoint))
            {
                break;
            }

            Units.DestinationYieldReturnTargets[blockerUnit] =
                Units.SlotTargets[blockerUnit];
            Units.DestinationYieldForUnits[blockerUnit] = blockedUnit;
            Units.DestinationYieldForCommandVersions[blockerUnit] =
                Units.CommandVersions[blockedUnit];
            Units.DestinationYieldDeadlines[blockerUnit] =
                Metrics.Tick + DestinationYieldDeadlineTicks;
            Units.DestinationYieldPoints[blockerUnit] = yieldPoint;
            Units.DestinationYieldPhases[blockerUnit] =
                DestinationYieldPhase.MovingAside;
            Units.SlotTargets[blockerUnit] = yieldPoint;
            QueueDestinationRetarget(blockerUnit);
            Metrics.DestinationYieldEvents++;
            activeYields++;
        }
    }

    private bool DestinationYieldTargetCleared(int yieldingUnit)
    {
        var targetUnit = Units.DestinationYieldForUnits[yieldingUnit];
        if ((uint)targetUnit >= (uint)Units.Count ||
            Units.CommandVersions[targetUnit] !=
            Units.DestinationYieldForCommandVersions[yieldingUnit] ||
            Units.DestinationOverflowed[targetUnit])
        {
            return true;
        }

        return Units.Modes[targetUnit] == UnitMoveMode.Arrived ||
               Vector2.DistanceSquared(
                   Units.Positions[targetUnit], Units.SlotTargets[targetUnit]) <=
               18f * 18f;
    }

    private void BeginDestinationYieldReturn(int unit)
    {
        Units.SlotTargets[unit] = Units.DestinationYieldReturnTargets[unit];
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.Returning;
        Units.DestinationYieldDeadlines[unit] =
            Metrics.Tick + DestinationYieldReturnDeadlineTicks;
        QueueDestinationRetarget(unit);
    }

    private void ConvertDestinationYieldToOverflow(int unit)
    {
        var overflowTarget = Units.DestinationYieldPoints[unit];
        ClearDestinationYield(unit);
        Units.SlotTargets[unit] = overflowTarget;
        Units.DestinationOverflowed[unit] = true;
        QueueDestinationRetarget(unit);
        Metrics.DestinationOverflowAssignments++;
    }

    private void ClearDestinationYield(int unit)
    {
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
        Units.DestinationYieldReturnTargets[unit] = Units.SlotTargets[unit];
        Units.DestinationYieldPoints[unit] = Units.SlotTargets[unit];
        Units.DestinationYieldForUnits[unit] = -1;
        Units.DestinationYieldForCommandVersions[unit] = 0;
        Units.DestinationYieldDeadlines[unit] = 0;
        Units.DestinationYieldCooldownTicks[unit] =
            Metrics.Tick + DestinationYieldCooldownTicks;
    }

    private void QueueDestinationRetarget(
        int unit,
        bool preserveTerminalHistory = false)
    {
        Units.CommandVersions[unit]++;
        Units.Paths[unit] = null;
        Units.RouteWaypoints[unit] = [];
        Units.PathPending[unit] = true;
        Units.Modes[unit] = UnitMoveMode.WaitingForPath;
        Units.ActiveChokeIds[unit] = -1;
        Units.ChokeDirections[unit] = 0;
        Units.ChokePhases[unit] = ChokePhase.None;
        Units.ChokeAdmitted[unit] = false;
        Units.ProgressOrigins[unit] = Units.Positions[unit];
        Units.ProgressTimers[unit] = 0f;
        Units.ProgressBestDistances[unit] = Vector2.Distance(
            Units.Positions[unit], Units.SlotTargets[unit]);
        Units.DestinationBestDistances[unit] = Units.ProgressBestDistances[unit];
        Units.DestinationStallTicks[unit] = 0;
        if (!preserveTerminalHistory)
        {
            Units.DestinationNearTicks[unit] = 0;
        }
        Units.RepathCooldowns[unit] = 0f;
        _pathRequests.Enqueue(new PathRequest(unit, Units.CommandVersions[unit]));
    }

    private float RemainingPathDistance(int unit, UnitPath? path)
    {
        if (path is null || path.Points.Length == 0)
        {
            return Vector2.Distance(Units.Positions[unit], Units.SlotTargets[unit]);
        }

        var cursor = Math.Clamp(path.Cursor, 0, path.Points.Length - 1);
        var distance = Vector2.Distance(Units.Positions[unit], path.Points[cursor]);
        for (var index = cursor + 1; index < path.Points.Length; index++)
        {
            distance += Vector2.Distance(path.Points[index - 1], path.Points[index]);
        }

        return distance;
    }

    private void UpdateMetrics()
    {
        Metrics.MovingUnits = 0;
        Metrics.ArrivedUnits = 0;
        Metrics.WaitingForPathUnits = 0;
        Metrics.UnreachableUnits = 0;
        Metrics.MaximumDestinationStallTicks = 0;
        Metrics.MaximumDestinationNearTicks = 0;
        Metrics.ActiveDestinationYields = 0;
        Metrics.PendingUnitOrders = 0;
        Metrics.CompletedQueuedOrders = 0;
        Metrics.QueueOverflowEvents = 0;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            Metrics.PendingUnitOrders += CommandQueues.PendingCounts[unit];
            Metrics.CompletedQueuedOrders += CommandQueues.CompletedQueuedOrders[unit];
            Metrics.QueueOverflowEvents += CommandQueues.QueueOverflowCounts[unit];
            if (!Units.Alive[unit])
            {
                continue;
            }
            Metrics.MaximumDestinationStallTicks = Math.Max(
                Metrics.MaximumDestinationStallTicks,
                Units.DestinationStallTicks[unit]);
            Metrics.MaximumDestinationNearTicks = Math.Max(
                Metrics.MaximumDestinationNearTicks,
                Units.DestinationNearTicks[unit]);
            if (Units.DestinationYieldPhases[unit] != DestinationYieldPhase.None)
            {
                Metrics.ActiveDestinationYields++;
            }
            switch (Units.Modes[unit])
            {
                case UnitMoveMode.Moving:
                    Metrics.MovingUnits++;
                    break;
                case UnitMoveMode.Arrived:
                    Metrics.ArrivedUnits++;
                    break;
                case UnitMoveMode.WaitingForPath:
                    Metrics.WaitingForPathUnits++;
                    break;
            }

            if (Units.RecoveryStages[unit] == RecoveryStage.Unreachable)
            {
                Metrics.UnreachableUnits++;
            }
        }

        Metrics.PendingPathRequests = _pathRequests.Count;
    }

    private void ValidateUnitIndex(int unit)
    {
        if ((uint)unit >= (uint)Units.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }
    }

    private void ValidateLivingUnit(int unit)
    {
        if (!Units.Alive[unit])
        {
            throw new InvalidOperationException($"Unit {unit} is dead.");
        }
    }

    private void AdvanceQueuedOrders()
    {
        var readyCount = 0;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] || !IsActiveOrderComplete(unit))
            {
                continue;
            }

            if (CommandQueues.ActiveOrdersWereQueued[unit])
            {
                CommandQueues.CompletedQueuedOrders[unit]++;
                CommandQueues.ActiveOrdersWereQueued[unit] = false;
            }

            if (CommandQueues.TryDequeue(unit, out var order))
            {
                _orderReadyUnits[readyCount] = unit;
                _orderReadyOrders[readyCount] = order;
                _orderReadyProcessed[readyCount] = false;
                readyCount++;
            }
        }

        for (var first = 0; first < readyCount; first++)
        {
            if (_orderReadyProcessed[first])
            {
                continue;
            }

            var order = _orderReadyOrders[first];
            var dispatchCount = 0;
            for (var candidate = first; candidate < readyCount; candidate++)
            {
                if (_orderReadyProcessed[candidate] ||
                    _orderReadyOrders[candidate] != order)
                {
                    continue;
                }

                _orderReadyProcessed[candidate] = true;
                _orderDispatchUnits[dispatchCount++] = _orderReadyUnits[candidate];
            }

            ExecuteOrderGroup(
                _orderDispatchUnits.AsSpan(0, dispatchCount),
                order,
                wasQueued: true);
        }
    }

    private bool IsActiveOrderComplete(int unit)
    {
        if (!CommandQueues.HasActiveOrders[unit])
        {
            return true;
        }

        return CommandQueues.ActiveKinds[unit] switch
        {
            UnitOrderKind.Move => Units.Modes[unit] == UnitMoveMode.Arrived,
            UnitOrderKind.AttackMove =>
                Units.Modes[unit] == UnitMoveMode.Arrived &&
                Combat.TargetUnits[unit] < 0,
            UnitOrderKind.AttackTarget =>
                (uint)CommandQueues.ActiveTargetUnits[unit] >= (uint)Units.Count ||
                !Units.Alive[CommandQueues.ActiveTargetUnits[unit]],
            UnitOrderKind.AttackBuilding =>
                !Construction.IsAlive(new GameplayBuildingId(
                    CommandQueues.ActiveTargetBuildings[unit])),
            UnitOrderKind.GatherResource =>
                IsGatherResourceOrderComplete(unit),
            UnitOrderKind.ResumeConstruction =>
                IsResumeConstructionOrderComplete(unit),
            UnitOrderKind.Stop or UnitOrderKind.Hold => true,
            _ => true
        };
    }

    private bool IsGatherResourceOrderComplete(int unit)
    {
        if (!Economy.IsWorker(unit)) return true;
        var worker = Economy.Worker(unit);
        return worker.State is WorkerEconomyState.None or WorkerEconomyState.Idle ||
               worker.TargetNode.Value !=
               CommandQueues.ActiveTargetResourceNodes[unit];
    }

    private bool IsResumeConstructionOrderComplete(int unit)
    {
        var buildingId = new GameplayBuildingId(
            CommandQueues.ActiveTargetBuildings[unit]);
        if (!Construction.IsAlive(buildingId)) return true;
        var building = Construction.Observe(buildingId);
        return building.BuilderUnit != unit ||
               building.State is BuildingLifecycleState.WaitingForBuilder or
                   BuildingLifecycleState.Completed or
                   BuildingLifecycleState.Canceled or
                   BuildingLifecycleState.Destroyed;
    }

    private int NextOrderSequenceId()
    {
        if (_nextOrderSequenceId == int.MaxValue)
        {
            _nextOrderSequenceId = 1;
        }
        return _nextOrderSequenceId++;
    }

    void ICombatMovementDriver.Chase(int unit, Vector2 target)
    {
        SetCombatDestination(unit, target);
    }

    void ICombatMovementDriver.StopForAttack(int unit)
    {
        if (Units.Modes[unit] == UnitMoveMode.Hold && !Units.PathPending[unit])
        {
            return;
        }

        Units.CommandVersions[unit]++;
        Units.Paths[unit] = null;
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Hold;
        Units.PreferredVelocities[unit] = Vector2.Zero;
        Units.ActiveChokeIds[unit] = -1;
        Units.ChokePhases[unit] = ChokePhase.None;
        Units.ChokeAdmitted[unit] = false;
    }

    void ICombatMovementDriver.ResumeAttackMove(int unit, Vector2 target)
    {
        if (!Units.Alive[unit])
        {
            return;
        }

        if (Vector2.DistanceSquared(Units.Positions[unit], target) <=
            ArrivalStopRadius * ArrivalStopRadius)
        {
            Units.Modes[unit] = UnitMoveMode.Arrived;
            Units.SlotTargets[unit] = target;
            Units.MoveGoals[unit] = target;
            return;
        }

        SetCombatDestination(unit, target);
    }

    void ICombatMovementDriver.FinishEngagement(int unit, UnitCommandIntent intent)
    {
        Units.CommandVersions[unit]++;
        Units.Paths[unit] = null;
        Units.RouteWaypoints[unit] = [];
        Units.PathPending[unit] = false;
        Units.Modes[unit] = intent == UnitCommandIntent.Hold
            ? UnitMoveMode.Hold
            : UnitMoveMode.Idle;
        Units.SlotTargets[unit] = Units.Positions[unit];
        Units.MoveGoals[unit] = Units.Positions[unit];
        Units.PreferredVelocities[unit] = Vector2.Zero;
        Units.ActiveChokeIds[unit] = -1;
        Units.ChokePhases[unit] = ChokePhase.None;
        Units.ChokeAdmitted[unit] = false;
        Units.MovementGroupIds[unit] = 0;
        Units.MovementGroupSizes[unit] = 0;
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
    }

    void ICombatMovementDriver.Kill(int unit)
    {
        Units.CommandVersions[unit]++;
        Units.Paths[unit] = null;
        Units.RouteWaypoints[unit] = [];
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Idle;
        Units.PreferredVelocities[unit] = Vector2.Zero;
        Units.Velocities[unit] = Vector2.Zero;
        Units.NextVelocities[unit] = Vector2.Zero;
        Units.ActiveChokeIds[unit] = -1;
        Units.ChokePhases[unit] = ChokePhase.None;
        Units.ChokeAdmitted[unit] = false;
        Units.MovementGroupIds[unit] = 0;
        Units.MovementGroupSizes[unit] = 0;
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
        CommandQueues.ClearPending(unit);
        CommandQueues.HasActiveOrders[unit] = false;
    }

    bool ICombatMovementDriver.IsBuildingTargetValid(
        int attacker,
        GameplayBuildingId building) =>
        (uint)attacker < (uint)Units.Count && Units.Alive[attacker] &&
        Construction.IsEnemyTarget(building, Combat.Teams[attacker]);

    SimRect ICombatMovementDriver.BuildingTargetBounds(
        GameplayBuildingId building) => Construction.Observe(building).Bounds;

    CombatBuildingDamageResult ICombatMovementDriver.DamageBuilding(
        GameplayBuildingId building,
        CombatWeaponDamageSnapshot weapon)
    {
        if (!Construction.IsAlive(building)) return default;
        var before = Construction.Observe(building).Health;
        var target = Construction.Observe(building);
        var damage = CombatDamageResolver.Resolve(
            weapon, BuildingDefense(target), before);
        if (!DamageBuilding(building, damage.TotalDamage)) return default;
        var after = Construction.Observe(building);
        var remaining = MathF.Max(0f, after.Health);
        return new CombatBuildingDamageResult(
            true,
            after.State == BuildingLifecycleState.Destroyed,
            MathF.Max(0f, before - remaining),
            remaining,
            damage.DamagePerAttack,
            damage.AttacksApplied,
            damage.BonusApplied);
    }

    int ICombatMovementDriver.WeaponUpgradeLevel(int team) =>
        CombatWeaponUpgradeTechnologyId < 0
            ? 0
            : Technology.Level(team, CombatWeaponUpgradeTechnologyId);

    private CombatWeaponDamageSnapshot CombatWeapon(int attacker) => new(
        Combat.AttackDamage[attacker], Combat.AttacksPerVolley[attacker],
        Combat.BonusVs[attacker], Combat.BonusDamage[attacker],
        ((ICombatMovementDriver)this).WeaponUpgradeLevel(Combat.Teams[attacker]),
        Combat.BaseUpgradeDamage[attacker], Combat.BonusUpgradeDamage[attacker]);

    private CombatDefenseSnapshot BuildingDefense(GameplayBuildingSnapshot building)
    {
        var level = CombatBuildingArmorTechnologyId < 0
            ? 0
            : Technology.Level(
                building.PlayerId, CombatBuildingArmorTechnologyId);
        return new CombatDefenseSnapshot(
            building.Type.Armor + level * building.Type.ArmorUpgradePerLevel,
            building.Type.Attributes);
    }

    private void SetCombatDestination(int unit, Vector2 target)
    {
        var clamped = World.Bounds.Inset(Units.Radii[unit] + 2f).Clamp(target);
        Units.SlotTargets[unit] = clamped;
        Units.MoveGoals[unit] = clamped;
        Units.MovementGroupIds[unit] = 0;
        Units.MovementGroupSizes[unit] = 1;
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
        Units.DestinationOverflowed[unit] = false;
        QueueNavigationReplan(unit, countInvalidation: false);
    }

    private static float InversePushMass(UnitMoveMode mode) => mode switch
    {
        UnitMoveMode.Hold => 0f,
        UnitMoveMode.Arrived => 0.28f,
        UnitMoveMode.Idle => 0.65f,
        _ => 1f
    };

    private static Vector2 DeterministicNormal(int left, int right)
    {
        var hash = unchecked((uint)(left * 73856093) ^ (uint)(right * 19349663));
        var angle = hash / (float)uint.MaxValue * MathF.Tau;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    internal void AppendPrivateStateHash(ref StableHash64 hash)
    {
        hash.Add(PathBudgetPerTick);
        hash.Add(_pendingNavigationInvalidations);
        hash.Add(_nextMovementGroupId);
        hash.Add(_nextOrderSequenceId);
        hash.Add(_pathRequests.Count);
        foreach (var request in _pathRequests)
        {
            hash.Add(request.UnitIndex);
            hash.Add(request.CommandVersion);
        }
    }

    private static double ElapsedMilliseconds(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private void MoveEconomyWorker(int unit, Vector2 target)
    {
        Span<int> worker = stackalloc int[1];
        worker[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            IssueMove(worker, target);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private void StopEconomyWorker(int unit)
    {
        Span<int> worker = stackalloc int[1];
        worker[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            Stop(worker);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private void MoveConstructionWorker(int unit, Vector2 target)
    {
        Span<int> worker = stackalloc int[1];
        worker[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            IssueMove(worker, target);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private void StopConstructionWorker(int unit)
    {
        Span<int> worker = stackalloc int[1];
        worker[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            Stop(worker);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }


}
