using System.Diagnostics;
using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class RtsSimulation : ICombatMovementDriver
{
    private const float WaypointRadius = 13f;
    private const float MinimumArrivalStopRadius = 3.5f;
    private const float MaximumArrivalStopRadius = 8f;
    private const float BaseArrivalResponseRadius = 58f;
    private const float MinimumArrivalSpeedFactor = 0.45f;
    private const float MaximumMinimumArrivalSpeed = 64f;
    private const float FriendlyBuildingInteractionPadding = 8f;
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
    private const int DynamicBlockageTimeoutTicks = 180;
    private const float DynamicBlockageProgressDistance = 12f;
    private const float DynamicBlockageLookAheadDistance = 96f;
    private const int DynamicBlockageProbeIntervalTicks = 6;
    private const float DynamicBlockageProbeForwardSpeed = 4f;
    private const int ReservationMigrationSettleTicks = 6;
    private const int ReservationMigrationRelaxationPasses = 8;
    private const float MaximumMigratedReservationDistance = 12f;
    private const int TerminalSettleMinimumGroupSize = 4;
    private const float TerminalSettleQuorum = 0.9f;
    private const float TerminalSettleMaximumDistance = 180f;

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
    private readonly Func<ConstructionReservationId, SimRect, BuildingPlacementRules,
        HardFootprintCommitResult> _constructionCommitFootprint;
    private readonly Func<UnitTypeProfile, int, Vector2, int> _productionSpawnUnit;
    private readonly Action<int, int, RallyTarget> _productionApplyRally;
    private readonly Func<int, int, SimRect, float, bool>
        _productionEvacuateExitBlocker;
    private readonly int[] _collisionNeighbors = new int[64];
    private readonly CombatContactSnapshot[] _combatContacts;
    private readonly bool[] _unitCollisionSuppressed;
    private readonly int[] _orderReadyUnits;
    private readonly UnitOrder[] _orderReadyOrders;
    private readonly bool[] _orderReadyProcessed;
    private readonly int[] _orderDispatchUnits;
    private readonly int[] _displacedStationaryUnits;
    private readonly Vector2[] _reservationTargetScratch;
    private readonly int[] _reservationSelectionScratch;
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
        Concealment = new UnitConcealmentController(Units, Combat);
        CombatEvents = new CombatEventStream();
        CommandQueues = new UnitCommandQueueStore(capacity);
        Economy = new EconomySystem(capacity);
        Construction = new ConstructionSystem();
        Production = new ProductionSystem();
        Technology = new TechnologySystem();
        Diplomacy = new PlayerDiplomacySystem();
        Visibility = new PlayerVisibilitySystem(world.Bounds, Diplomacy);
        Match = new MatchSystem();
        _orderReadyUnits = new int[capacity];
        _orderReadyOrders = new UnitOrder[capacity];
        _orderReadyProcessed = new bool[capacity];
        _orderDispatchUnits = new int[capacity];
        _displacedStationaryUnits = new int[capacity];
        _reservationTargetScratch = new Vector2[capacity];
        _reservationSelectionScratch = new int[capacity];
        _combatContacts = new CombatContactSnapshot[capacity];
        _unitCollisionSuppressed = new bool[capacity];
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
        _combatSystem = new CombatSystem(
            Units, Combat, this, world, CombatEvents, CanCombatPerceiveUnit,
            Diplomacy.IsEnemy, CanUnitAttack);
        _economyMoveWorker = MoveEconomyWorker;
        _economyStopWorker = StopEconomyWorker;
        _constructionMoveWorker = MoveConstructionWorker;
        _constructionStopWorker = StopConstructionWorker;
        _constructionCommitFootprint = CommitConstructionFootprint;
        _productionSpawnUnit = SpawnProducedUnit;
        _productionApplyRally = ApplyProducedUnitRally;
        _productionEvacuateExitBlocker = EvacuateProductionExitBlocker;
    }

    public StaticWorld World { get; }
    public UnitStore Units { get; }
    public CombatStore Combat { get; }
    public UnitConcealmentController Concealment { get; }
    public CombatProjectileSystem CombatProjectiles => _combatSystem.Projectiles;
    public CombatEventStream CombatEvents { get; }
    public UnitCommandQueueStore CommandQueues { get; }
    public EconomySystem Economy { get; }
    public ConstructionSystem Construction { get; }
    public QueuedConstructionPolicy ConstructionQueuePolicy { get; } =
        QueuedConstructionPolicy.ProjectDefault;
    public ProductionSystem Production { get; }
    public TechnologySystem Technology { get; }
    public PlayerDiplomacySystem Diplomacy { get; }
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

    public CombatAutoTargetScore PreviewAutoTargetScore(
        int attacker,
        int target) => _combatSystem.PreviewAutoTargetScore(attacker, target);

    public CombatContactSnapshot PreviewCombatContact(int unit)
    {
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit])
            throw new ArgumentOutOfRangeException(nameof(unit));
        return EvaluateCombatContact(unit);
    }

    public CombatContactResolution PreviewCombatContact(
        int left,
        int right) => CombatContactPolicy.Resolve(
        PreviewCombatContact(left), PreviewCombatContact(right));

    public CombatDamageResult PreviewCombatDamage(
        int attacker,
        GameplayBuildingId building)
    {
        if ((uint)attacker >= (uint)Units.Count || !Units.Alive[attacker] ||
            !Construction.IsAlive(building))
            throw new ArgumentOutOfRangeException();
        var target = ObserveGameplayBuilding(building);
        return CombatDamageResolver.Resolve(
            CombatWeapon(attacker), BuildingDefense(target), target.Health);
    }

    public GameplayBuildingSnapshot ObserveGameplayBuilding(
        GameplayBuildingId building)
    {
        var snapshot = Construction.Observe(building);
        return snapshot with { EffectiveArmor = EffectiveBuildingArmor(snapshot) };
    }

    public GameplayBuildingSnapshot[] CreateGameplayBuildingOverview()
    {
        var snapshots = Construction.CreateOverview();
        for (var index = 0; index < snapshots.Length; index++)
        {
            snapshots[index] = snapshots[index] with
            {
                EffectiveArmor = EffectiveBuildingArmor(snapshots[index])
            };
        }
        return snapshots;
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
            CombatProjectiles = CombatProjectiles.CaptureRuntimeState(),
            Economy = Economy.CaptureRuntimeState(Units.Count),
            Diplomacy = Diplomacy.CaptureRuntimeState(),
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
        CombatProjectiles.RestoreRuntimeState(snapshot.CombatProjectiles);
        CombatEvents.Reset();
        Economy.RestoreRuntimeState(snapshot.Economy, Units.Count);
        Diplomacy.RestoreRuntimeState(snapshot.Diplomacy);
        Construction.RestoreRuntimeState(snapshot.Construction);
        Production.RestoreRuntimeState(
            snapshot.Production, Construction, Economy, Units);
        Technology.RestoreRuntimeState(
            snapshot.Technology, Construction, Economy.Players);
        Visibility.RestoreRuntimeState(snapshot.Visibility);
        Visibility.Update(Units, Combat, Construction);
        Match.RestoreRuntimeState(snapshot.Match, Economy.Players, Diplomacy);
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

    public void ConfigureAlliance(
        int allianceId,
        bool sharedVision,
        params int[] playerIds)
    {
        if (Metrics.Tick != 0 || Match.Phase != MatchPhase.Setup ||
            _commandRecorder is not null || _replayPackageRecorder is not null)
        {
            throw new InvalidOperationException(
                "Alliances must be configured during tick-zero match setup.");
        }
        Diplomacy.ConfigureAlliance(allianceId, sharedVision, playerIds);
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
            case UnitOrderKind.ReturnCargo:
                IssueDeferredWorkerOrder(
                    command.Units,
                    new UnitOrder(UnitOrderKind.ReturnCargo, Vector2.Zero),
                    command.Queued);
                break;
            case UnitOrderKind.Stop:
                Stop(command.Units);
                break;
            case UnitOrderKind.Hold:
                Hold(command.Units);
                break;
            case UnitOrderKind.ActivateConcealment:
                IssueConcealment(command.Units, activate: true, command.Queued);
                break;
            case UnitOrderKind.DeactivateConcealment:
                IssueConcealment(command.Units, activate: false, command.Queued);
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
            case EconomyCommandKind.ReturnCargo:
                var returned = IssueReturnCargo(
                    command.PlayerId, command.UnitId);
                if (!returned.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Replay Return Cargo failed with {returned.Code}.");
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
                    command.Center, command.RefineryNode, command.Queued);
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
        var commit = TryCommitHardFootprint(footprint, rules);
        return commit.Assessment.ToPlacementResult(commit.FootprintId);
    }

    public BuildingPlacementAssessment AssessBuildingPlacement(
        SimRect footprint,
        BuildingPlacementRules rules) =>
        AssessBuildingPlacement(footprint, rules, default);

    private BuildingPlacementAssessment AssessBuildingPlacement(
        SimRect footprint,
        BuildingPlacementRules rules,
        ConstructionReservationId ignoreReservation)
    {
        var assessment = BuildingPlacementValidator.Evaluate(
            World, Units, footprint, rules, _buildingConnectivityGuard);
        if (!assessment.Succeeded ||
            !Construction.Reservations.TryFindOverlap(
                footprint, out var conflict, ignoreReservation))
            return assessment;
        return new BuildingPlacementAssessment(
            new StaticPlacementResult(
                StaticPlacementCode.DynamicFootprintOverlap,
                conflict.Id.Value),
            new DynamicStartValidationResult(
                DynamicStartValidationCode.NotEvaluated, -1));
    }

    private BuildingPlacementAssessment AssessPlayerKnownBuildingPlacement(
        int playerId,
        SimRect footprint,
        BuildingPlacementRules rules)
    {
        var assessment = BuildingPlacementValidator.Evaluate(
            World,
            Units,
            footprint,
            rules with { PreserveConnectivity = false },
            _buildingConnectivityGuard,
            value => IsDynamicFootprintKnownToPlayer(playerId, value),
            unit => IsUnitPlacementBlockerKnownToPlayer(playerId, unit));
        if (!assessment.Succeeded ||
            !Construction.Reservations.TryFindOverlap(
                footprint,
                out var conflict,
                default,
                value => IsReservationKnownToPlayer(playerId, value)))
            return assessment;
        return new BuildingPlacementAssessment(
            new StaticPlacementResult(
                StaticPlacementCode.DynamicFootprintOverlap,
                conflict.Id.Value),
            new DynamicStartValidationResult(
                DynamicStartValidationCode.NotEvaluated, -1));
    }

    private bool IsDynamicFootprintKnownToPlayer(
        int playerId,
        DynamicFootprint footprint)
    {
        if (!Construction.TryObserveFootprint(footprint.Id, out var building))
            return true;
        if (building.PlayerId == playerId)
            return true;
        var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
        return Visibility.IsVisible(playerId, center);
    }

    private bool IsUnitPlacementBlockerKnownToPlayer(int playerId, int unit)
    {
        if (Combat.Teams[unit] == playerId)
            return false;
        return CanPlayerPerceiveUnit(playerId, unit);
    }

    private bool CanPlayerPerceiveUnit(int playerId, int unit) =>
        Visibility.IsUnitVisible(playerId, unit, Units, Combat);

    private bool CanCombatPerceiveUnit(int playerId, int unit) =>
        Combat.ConcealmentKinds[unit] == UnitConcealmentKind.None ||
        CanPlayerPerceiveUnit(playerId, unit);

    private bool IsReservationKnownToPlayer(
        int playerId,
        ConstructionReservationEntry reservation)
    {
        if (reservation.PlayerId == playerId)
            return true;
        var center = (reservation.Bounds.Min + reservation.Bounds.Max) * 0.5f;
        return Visibility.IsVisible(playerId, center);
    }

    public HardFootprintCommitResult TryCommitHardFootprint(
        SimRect footprint,
        BuildingPlacementRules rules) =>
        TryCommitHardFootprint(footprint, rules, default);

    private HardFootprintCommitResult TryCommitHardFootprint(
        SimRect footprint,
        BuildingPlacementRules rules,
        ConstructionReservationId ignoreReservation)
    {
        var assessment = AssessBuildingPlacement(
            footprint, rules, ignoreReservation);
        if (!assessment.Succeeded)
        {
            return new HardFootprintCommitResult(
                assessment.Code == BuildingPlacementCode.UnitOverlap
                    ? HardFootprintCommitCode.DynamicOccupantRejected
                    : HardFootprintCommitCode.StaticPlacementRejected,
                default,
                assessment);
        }

        var id = PlaceBuilding(footprint);
        return new HardFootprintCommitResult(
            HardFootprintCommitCode.Success, id, assessment);
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
        float acceleration = 720f,
        UnitPerceptionProfileSnapshot perception = default,
        UnitConcealmentCapabilitySnapshot concealmentCapability = default)
    {
        var unit = Units.Add(position, radius, maxSpeed, acceleration);
        Combat.Register(
            unit, team, position, combatProfile, perception,
            concealmentCapability);
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
        CombatProfileSnapshot combatProfile,
        UnitPerceptionProfileSnapshot perception = default,
        UnitConcealmentCapabilitySnapshot concealmentCapability = default) =>
        AddUnit(
            position,
            team,
            combatProfile,
            movementProfile.PhysicalRadius,
            movementProfile.MaximumSpeed,
            movementProfile.Acceleration,
            perception,
            concealmentCapability);

    public int AddWorker(
        Vector2 position,
        int playerId,
        float radius = 7.5f,
        float maxSpeed = 128f,
        float acceleration = 720f,
        CombatProfileSnapshot? combatProfile = null)
    {
        return AddGatherer(
            position, playerId, GathererCapability.NormalWorker,
            radius, maxSpeed, acceleration, combatProfile);
    }

    public int AddGatherer(
        Vector2 position,
        int playerId,
        GathererCapability capability,
        float radius = 7.5f,
        float maxSpeed = 128f,
        float acceleration = 720f,
        CombatProfileSnapshot? combatProfile = null)
    {
        if (!Economy.Players.IsRegistered(playerId) ||
            capability == GathererCapability.None ||
            !Enum.IsDefined(capability))
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
        var unit = AddUnit(
            position,
            playerId,
            combatProfile ?? CombatProfileSnapshot.Standard,
            radius,
            maxSpeed,
            acceleration);
        Economy.RegisterGatherer(unit, playerId, capability);
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
        ClearConstructionEvacuation(unit);
        CommandQueues.ClearPending(unit);
        CommandQueues.HasActiveOrders[unit] = false;
        Span<int> gatheringWorker = stackalloc int[1];
        gatheringWorker[0] = unit;
        Construction.InterruptBuilders(gatheringWorker);
        Economy.BeginGather(
            unit, nodeId, Units.Positions[unit], Units.Radii[unit],
            _economyMoveWorker);
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

    public ReturnCargoCommandResult IssueReturnCargo(
        int issuingPlayerId,
        int unit,
        bool queued = false)
    {
        var matchBlock = MatchCommandBlockFor(issuingPlayerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new ReturnCargoCommandResult(
                matchBlock switch
                {
                    MatchCommandBlock.Completed =>
                        ReturnCargoCommandCode.MatchCompleted,
                    MatchCommandBlock.NotParticipant =>
                        ReturnCargoCommandCode.NotParticipant,
                    _ => ReturnCargoCommandCode.PlayerDefeated
                }, unit);
        }
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit])
        {
            return new ReturnCargoCommandResult(
                ReturnCargoCommandCode.InvalidUnit, unit);
        }
        var validation = Economy.ValidateReturnCargo(issuingPlayerId, unit);
        if (!validation.Succeeded)
            return validation;

        var order = new UnitOrder(UnitOrderKind.ReturnCargo, Vector2.Zero);
        if (queued)
        {
            Span<int> queuedWorker = stackalloc int[1];
            queuedWorker[0] = unit;
            IssueDeferredWorkerOrder(queuedWorker, order, queued: true);
            return validation;
        }

        _economyCommandRecorder?.RecordReturnCargo(
            Metrics.Tick, issuingPlayerId, unit);
        ClearConstructionEvacuation(unit);
        CommandQueues.ClearPending(unit);
        CommandQueues.HasActiveOrders[unit] = false;
        Span<int> worker = stackalloc int[1];
        worker[0] = unit;
        Construction.InterruptBuilders(worker);
        Economy.BeginReturnCargo(
            unit, Units.Positions[unit], Units.Radii[unit],
            _economyMoveWorker);
        CommandQueues.Begin(
            unit,
            order with { SequenceId = NextOrderSequenceId() },
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
        EconomyResourceNodeId refineryNode = default,
        bool queued = false)
    {
        var validation = ValidateConstructionRequest(
            issuingPlayerId, builderUnit, profile, center, refineryNode,
            out center, out refineryNode, queued);
        if (!validation.Succeeded) return validation;
        if (queued &&
            CommandQueues.PendingCounts[builderUnit] >=
                UnitCommandQueueStore.MaximumPendingOrders)
        {
            return new ConstructionCommandResult(
                ConstructionCommandCode.QueueFull, default);
        }
        if (queued && ConstructionQueuePolicy.PaymentTiming !=
            QueuedConstructionPaymentTiming.ReserveOnIssue)
        {
            throw new InvalidOperationException(
                "Unsupported queued construction payment policy.");
        }
        var charged = Economy.Players.TrySpend(issuingPlayerId, profile.Cost);
        if (!charged.Succeeded)
        {
            throw new InvalidOperationException(
                "Construction cost changed between validation and commit.");
        }
        var halfSize = profile.Size * 0.5f;
        var bounds = new SimRect(center - halfSize, center + halfSize);
        if (queued)
        {
            var queuedId = Construction.AddQueued(
                issuingPlayerId, profile, bounds, refineryNode, Metrics.Tick);
            if (CommandQueues.PendingCounts[builderUnit] == 0 &&
                CommandQueues.ActiveKinds[builderUnit] ==
                    UnitOrderKind.GatherResource)
            {
                Economy.Cancel([builderUnit]);
                CommandQueues.HasActiveOrders[builderUnit] = false;
            }
            Span<int> queuedBuilder = stackalloc int[1];
            queuedBuilder[0] = builderUnit;
            var accepted = IssueDeferredWorkerOrder(
                queuedBuilder,
                new UnitOrder(
                    UnitOrderKind.ResumeConstruction,
                    center,
                    TargetBuilding: queuedId.Value),
                queued: true,
                recordCommand: false);
            if (!accepted)
            {
                if (!Construction.RejectQueuedReservation(
                        queuedId, issuingPlayerId, Economy))
                {
                    throw new InvalidOperationException(
                        "Queued construction could not roll back its reservation.");
                }
                return new ConstructionCommandResult(
                    ConstructionCommandCode.QueueFull, default);
            }
            _constructionCommandRecorder?.RecordBuild(
                Metrics.Tick, issuingPlayerId, builderUnit, profile, center,
                refineryNode, queued: true);
            return new ConstructionCommandResult(
                ConstructionCommandCode.Success, queuedId);
        }

        Economy.Cancel([builderUnit]);
        ClearConstructionEvacuation(builderUnit);
        CommandQueues.ClearPending(builderUnit);
        CommandQueues.HasActiveOrders[builderUnit] = false;
        var accessPoint = ResolveConstructionAccessPoint(
            bounds,
            builderUnit,
            profile.PlacementProfile.UnitPadding,
            default,
            refineryNode);
        var id = Construction.Add(
            issuingPlayerId,
            builderUnit,
            profile,
            bounds,
            refineryNode,
            accessPoint,
            Metrics.Tick);
        MoveConstructionWorker(builderUnit, Construction.BuilderAccessPoint(id));
        _constructionCommandRecorder?.RecordBuild(
            Metrics.Tick, issuingPlayerId, builderUnit, profile, center,
            refineryNode, queued: false);
        return new ConstructionCommandResult(
            ConstructionCommandCode.Success, id);
    }

    public ConstructionCommandResult PreviewConstruction(
        int issuingPlayerId,
        int builderUnit,
        BuildingTypeProfile profile,
        Vector2 center,
        EconomyResourceNodeId refineryNode = default,
        bool queued = false) =>
        ValidateConstructionRequest(
            issuingPlayerId, builderUnit, profile, center, refineryNode,
            out _, out _, queued);

    private ConstructionCommandResult ValidateConstructionRequest(
        int issuingPlayerId,
        int builderUnit,
        BuildingTypeProfile profile,
        Vector2 requestedCenter,
        EconomyResourceNodeId requestedRefineryNode,
        out Vector2 resolvedCenter,
        out EconomyResourceNodeId resolvedRefineryNode,
        bool allowBusyBuilder = false)
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
            resolvedRefineryNode, allowBusyBuilder);
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
        var placement = AssessPlayerKnownBuildingPlacement(
            issuingPlayerId,
            footprint,
            new BuildingPlacementRules(
                placementProfile.MinimumPassageClass,
                placementProfile.UnitPadding));
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
            ClearConstructionEvacuation(builderUnit);
            CommandQueues.ClearPending(builderUnit);
            CommandQueues.HasActiveOrders[builderUnit] = false;
        }
        var buildingSnapshot = Construction.Observe(buildingId);
        var accessPoint = ResolveConstructionAccessPoint(
            buildingSnapshot.Bounds,
            builderUnit,
            buildingSnapshot.Type.PlacementProfile.UnitPadding,
            buildingSnapshot.ReservationId,
            buildingSnapshot.RefineryNode);
        var succeeded = Construction.Resume(
            buildingId, playerId, builderUnit, Economy, Units,
            accessPoint, _constructionMoveWorker);
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
            var relation = Diplomacy.Relation(playerId, owner);
            if (relation != PlayerEntityRelation.Own &&
                !CanPlayerPerceiveUnit(playerId, unit))
            {
                continue;
            }
            var concealment = Concealment.Observe(unit);
            units.Add(new PlayerUnitViewSnapshot(
                unit, owner, relation, Units.Positions[unit], Units.Radii[unit],
                Combat.Health[unit], Combat.MaximumHealth[unit],
                Units.Modes[unit], Combat.Phases[unit],
                Visibility.ConcealmentStateFor(
                    playerId, unit, Units, Combat),
                concealment.Phase,
                concealment.TransitionProgress,
                concealment.CapabilityKind != UnitConcealmentKind.None &&
                relation == PlayerEntityRelation.Own));
        }
        var buildings = new List<PlayerBuildingViewSnapshot>();
        foreach (var building in Construction.CreateOverview())
        {
            if (building.IsTerminal)
                continue;
            var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
            var relation = Diplomacy.Relation(playerId, building.PlayerId);
            if (relation != PlayerEntityRelation.Own &&
                !Visibility.IsVisible(playerId, center))
            {
                continue;
            }
            buildings.Add(new PlayerBuildingViewSnapshot(
                building.Id, building.PlayerId, relation, building.Type,
                building.Bounds, building.State, building.Progress,
                building.Health, building.MaximumHealth,
                PublicConstructionStatusFor(playerId, building)));
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
        if (!UnitsAllowMovement(units))
            return new(PlayerOrderCommandCode.ContextActionUnavailable);
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
        if (!UnitsAllowMovement(units) || !UnitsAllowAttack(units))
            return new(PlayerOrderCommandCode.ContextActionUnavailable);
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

    public PlayerOrderCommandResult IssuePlayerConcealment(
        int playerId,
        ReadOnlySpan<int> units,
        bool activate,
        bool queued = false)
    {
        var validation = ValidatePlayerSelection(playerId, units);
        if (!validation.Succeeded) return validation;
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            if (!Concealment.CanActivate(unit))
                return new(PlayerOrderCommandCode.ContextActionUnavailable);
            if (queued)
            {
                if (ProjectedConcealmentActive(unit) == activate)
                    return new(PlayerOrderCommandCode.ContextActionUnavailable);
                continue;
            }
            var phase = Combat.ConcealmentPhases[unit];
            if (phase is UnitConcealmentPhase.Activating or
                UnitConcealmentPhase.Deactivating ||
                activate && phase != UnitConcealmentPhase.Visible ||
                !activate && phase != UnitConcealmentPhase.Concealed)
            {
                return new(PlayerOrderCommandCode.ContextActionUnavailable);
            }
        }
        IssueConcealment(units, activate, queued);
        return validation;
    }

    private bool ProjectedConcealmentActive(int unit)
    {
        var active = Combat.ConcealmentPhases[unit] switch
        {
            UnitConcealmentPhase.Concealed => true,
            UnitConcealmentPhase.Activating => true,
            UnitConcealmentPhase.Visible => false,
            UnitConcealmentPhase.Deactivating => false,
            _ => throw new InvalidOperationException(
                "Unknown concealment phase.")
        };
        for (var pending = 0;
             pending < CommandQueues.PendingCounts[unit];
             pending++)
        {
            active = CommandQueues.PendingAt(unit, pending).Kind switch
            {
                UnitOrderKind.ActivateConcealment => true,
                UnitOrderKind.DeactivateConcealment => false,
                _ => active
            };
        }
        return active;
    }

    public PlayerOrderCommandResult IssuePlayerReturnCargo(
        int playerId,
        ReadOnlySpan<int> units,
        bool queued = false)
    {
        var selection = ValidatePlayerSelection(playerId, units);
        if (!selection.Succeeded) return selection;
        var issued = false;
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            if (!Economy.IsGathererOwnedBy(unit, playerId) ||
                Economy.Worker(unit).CargoAmount <= 0)
                continue;
            var result = IssueReturnCargo(playerId, unit, queued);
            issued |= result.Succeeded;
        }
        return issued
            ? selection
            : new PlayerOrderCommandResult(
                PlayerOrderCommandCode.ContextActionUnavailable);
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
        var requiresAttack = attackMoveModifier || target.Kind is
            SmartCommandTargetKind.EnemyUnit or
            SmartCommandTargetKind.EnemyBuilding;
        var requiresMovement = attackMoveModifier || target.Kind is
            SmartCommandTargetKind.Ground or
            SmartCommandTargetKind.FriendlyUnit or
            SmartCommandTargetKind.FriendlyBuilding or
            SmartCommandTargetKind.ResourceNode;
        if (requiresAttack && !UnitsAllowAttack(units) ||
            requiresMovement && !UnitsAllowMovement(units))
        {
            return new(PlayerOrderCommandCode.ContextActionUnavailable);
        }
        switch (target.Kind)
        {
            case SmartCommandTargetKind.FriendlyUnit:
                if ((uint)target.Unit >= (uint)Units.Count ||
                    !Units.Alive[target.Unit])
                    return new(PlayerOrderCommandCode.InvalidTarget);
                if (!Diplomacy.IsFriendly(playerId, Combat.Teams[target.Unit]))
                    return new(PlayerOrderCommandCode.FriendlyTarget);
                break;
            case SmartCommandTargetKind.EnemyUnit:
                if ((uint)target.Unit >= (uint)Units.Count ||
                    !Units.Alive[target.Unit])
                    return new(PlayerOrderCommandCode.InvalidTarget);
                if (!Diplomacy.IsEnemy(playerId, Combat.Teams[target.Unit]))
                    return new(PlayerOrderCommandCode.FriendlyTarget);
                if (!CanPlayerPerceiveUnit(playerId, target.Unit))
                    return new(PlayerOrderCommandCode.TargetNotVisible);
                break;
            case SmartCommandTargetKind.EnemyBuilding:
                var id = new GameplayBuildingId(target.Building);
                if (!Construction.IsAlive(id))
                    return new(PlayerOrderCommandCode.InvalidTarget);
                var building = Construction.Observe(id);
                if (Diplomacy.IsFriendly(playerId, building.PlayerId))
                    return new(PlayerOrderCommandCode.FriendlyTarget);
                if (!IsHostileBuilding(id, playerId))
                    return new(PlayerOrderCommandCode.InvalidTarget);
                var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
                if (!Visibility.IsVisible(playerId, center))
                    return new(PlayerOrderCommandCode.TargetNotVisible);
                break;
            case SmartCommandTargetKind.FriendlyBuilding:
                var friendlyId = new GameplayBuildingId(target.Building);
                if (!Construction.IsAlive(friendlyId))
                    return new(PlayerOrderCommandCode.InvalidTarget);
                if (!Diplomacy.IsFriendly(
                        playerId, Construction.Observe(friendlyId).PlayerId))
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
            (Economy.IsGathererOwnedBy(unit, playerId) ? workers : movers).Add(unit);
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
        var workerArray = workers.ToArray();
        var assignments = Economy.AssignGatherTargets(
            playerId, workerArray, resource, Units);
        for (var index = 0; index < workerArray.Length; index++)
            IssueGather(playerId, workerArray[index], assignments[index]);
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
        if (building.PlayerId == playerId &&
            building.State == BuildingLifecycleState.WaitingForBuilder)
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
            IssueFriendlyBuildingMove(building, units, queued);
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
            {
                IssueFriendlyBuildingMove(
                    building, movers.ToArray(), queued: true);
            }
            return new(PlayerOrderCommandCode.Success);
        }
        if (!ResumeConstruction(playerId, buildingId, builder))
            return new(PlayerOrderCommandCode.ContextActionUnavailable);
        if (movers.Count > 0)
        {
            IssueFriendlyBuildingMove(building, movers.ToArray(), queued: false);
        }
        return new(PlayerOrderCommandCode.Success);
    }

    private void IssueFriendlyBuildingMove(
        GameplayBuildingSnapshot building,
        ReadOnlySpan<int> units,
        bool queued)
    {
        Span<int> single = stackalloc int[1];
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            single[0] = unit;
            var target = ConstructionAccessPointResolver.ResolveInteraction(
                World,
                _pathProvider,
                building.Bounds,
                Units.Positions[unit],
                Units.Radii[unit],
                Units.NavigationRadii[unit],
                building.Type.PlacementProfile.UnitPadding +
                FriendlyBuildingInteractionPadding);
            IssueMove(single, target, queued);
        }
    }

    private bool IssueDeferredWorkerOrder(
        ReadOnlySpan<int> unitIndices,
        UnitOrder order,
        bool queued,
        bool recordCommand = true)
    {
        if (!UnitOrderContract.IsStructurallyValid(order) || unitIndices.IsEmpty)
            throw new ArgumentException("Deferred worker order is invalid.", nameof(order));
        for (var index = 0; index < unitIndices.Length; index++)
        {
            ValidateUnitIndex(unitIndices[index]);
            ValidateLivingUnit(unitIndices[index]);
            if (!_issuingSystemOrder &&
                (!queued || order.Kind is UnitOrderKind.Stop or UnitOrderKind.Hold))
            {
                ClearConstructionEvacuation(unitIndices[index]);
            }
        }
        if (recordCommand)
            _commandRecorder?.Record(Metrics.Tick, unitIndices, order, queued);
        order = order with { SequenceId = NextOrderSequenceId() };
        if (!queued)
        {
            ExecuteOrderGroup(unitIndices, order, wasQueued: false);
            return true;
        }
        var immediateCount = 0;
        var accepted = 0;
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            if (CommandQueues.PendingCounts[unit] == 0 &&
                IsActiveOrderComplete(unit))
            {
                _orderDispatchUnits[immediateCount++] = unit;
                accepted++;
            }
            else if (CommandQueues.TryEnqueue(unit, order))
            {
                accepted++;
            }
        }
        if (immediateCount > 0)
        {
            ExecuteOrderGroup(
                _orderDispatchUnits.AsSpan(0, immediateCount),
                order,
                wasQueued: true);
        }
        return accepted == unitIndices.Length;
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

    private bool UnitsAllowMovement(ReadOnlySpan<int> units)
    {
        for (var index = 0; index < units.Length; index++)
        {
            if (!Concealment.CanMove(units[index]))
                return false;
        }
        return true;
    }

    private bool UnitsAllowAttack(ReadOnlySpan<int> units)
    {
        for (var index = 0; index < units.Length; index++)
        {
            if (!Concealment.CanAttack(units[index]))
                return false;
        }
        return true;
    }

    private bool CanUnitAttack(int unit)
    {
        if (!Concealment.CanAttack(unit) ||
            Construction.SuppressesBuilderCombat(unit))
            return false;
        if (!Economy.IsGatherer(unit))
            return true;
        return Economy.Worker(unit).State is
            WorkerEconomyState.None or WorkerEconomyState.Idle;
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

    private PublicConstructionStatus PublicConstructionStatusFor(
        int playerId,
        GameplayBuildingSnapshot building)
    {
        if (building.State != BuildingLifecycleState.BlockedAtStart)
            return PublicConstructionStatus.None;
        var knownOccupant = false;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] || unit == building.BuilderUnit ||
                !building.Bounds.Expanded(
                        Units.Radii[unit] +
                        building.Type.PlacementProfile.UnitPadding)
                    .Contains(Units.Positions[unit]))
                continue;
            if (Combat.Teams[unit] == playerId)
            {
                if (CommandQueues.ConstructionEvacuationActive[unit] &&
                    CommandQueues.ConstructionEvacuationBuildings[unit] ==
                        building.Id.Value)
                    return PublicConstructionStatus.ClearingFriendlyUnits;
                knownOccupant = true;
                continue;
            }
            if (CanPlayerPerceiveUnit(playerId, unit))
                knownOccupant = true;
        }
        return knownOccupant
            ? PublicConstructionStatus.KnownOccupant
            : PublicConstructionStatus.WaitingForClearance;
    }

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
            if (!IsHostileBuilding(
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
        UnitCommandIntent intent,
        bool exactDestination = false)
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
        var assignments = exactDestination
            ? null
            : _slotAllocator.Allocate(Units, unitIndices, groupGoal);
        LastIssuedGroupRoute = PlanGroupRoute(
            centroid, groupGoal, maximumNavigationRadius);
        var movementGroupId = exactDestination ? 0 : NextMovementGroupId();
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
            var assignedTarget = exactDestination
                ? groupGoal
                : assignments![unit];
            ValidateUnitIndex(unit);
            ValidateLivingUnit(unit);
            Combat.SetCommand(
                unit,
                intent,
                intent == UnitCommandIntent.AttackMove
                    ? assignedTarget
                    : groupGoal);
            Units.CommandVersions[unit]++;
            Units.SlotTargets[unit] = assignedTarget;
            Units.MoveGoals[unit] = groupGoal;
            Units.MovementGroupIds[unit] = movementGroupId;
            Units.MovementGroupSizes[unit] = exactDestination
                ? 1
                : unitIndices.Length;
            Units.SlotReflowCooldownTicks[unit] = 0;
            Units.DestinationBestDistances[unit] = Vector2.Distance(
                Units.Positions[unit], assignedTarget);
            Units.DestinationStallTicks[unit] = 0;
            Units.DestinationNearTicks[unit] = 0;
            Units.DestinationOverflowed[unit] = false;
            Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
            Units.DestinationYieldReturnTargets[unit] = assignedTarget;
            Units.DestinationYieldPoints[unit] = assignedTarget;
            Units.DestinationYieldForUnits[unit] = -1;
            Units.DestinationYieldForCommandVersions[unit] = 0;
            Units.DestinationYieldDeadlines[unit] = 0;
            Units.DestinationYieldCooldownTicks[unit] = 0;
            Units.DynamicBlockageTicks[unit] = 0;
            Units.DynamicBlockageBestDistances[unit] = Vector2.Distance(
                Units.Positions[unit], assignedTarget);
            Units.ReservationMigrationTicks[unit] = 0;
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

    public void IssueConcealment(
        ReadOnlySpan<int> unitIndices,
        bool activate,
        bool queued = false) =>
        IssueOrder(
            unitIndices,
            new UnitOrder(
                activate
                    ? UnitOrderKind.ActivateConcealment
                    : UnitOrderKind.DeactivateConcealment,
                Vector2.Zero),
            queued);

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
            Units.DynamicBlockageTicks[unit] = 0;
            Units.DynamicBlockageBestDistances[unit] = 0f;
            Units.ReservationMigrationTicks[unit] = 0;
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

    private void ExecuteConcealmentOrder(
        ReadOnlySpan<int> unitIndices,
        bool activate)
    {
        Span<int> single = stackalloc int[1];
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            single[0] = unit;
            ExecuteStop(single);
            var result = Concealment.Begin(unit, activate);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Concealment transition failed for unit {unit}: " +
                    $"{result.Code}.");
            }
        }
    }

    private void FreezeConcealmentRestrictedUnits()
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] || Concealment.CanMove(unit))
                continue;
            Units.Paths[unit] = null;
            Units.RouteWaypoints[unit] = [];
            Units.PathPending[unit] = false;
            Units.Modes[unit] = UnitMoveMode.Hold;
            Units.Velocities[unit] = Vector2.Zero;
            Units.PreferredVelocities[unit] = Vector2.Zero;
            Units.NextVelocities[unit] = Vector2.Zero;
            Units.SlotTargets[unit] = Units.Positions[unit];
            Units.MoveGoals[unit] = Units.Positions[unit];
            Units.ActiveChokeIds[unit] = -1;
            Units.ChokePhases[unit] = ChokePhase.None;
            Units.ChokeAdmitted[unit] = false;
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
            for (var index = 0;
                 !_issuingSystemOrder && index < unitIndices.Length;
                 index++)
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
            case UnitOrderKind.ReturnCargo:
                ExecuteReturnCargoTask(unitIndices);
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
            case UnitOrderKind.ActivateConcealment:
                ExecuteConcealmentOrder(unitIndices, activate: true);
                break;
            case UnitOrderKind.DeactivateConcealment:
                ExecuteConcealmentOrder(unitIndices, activate: false);
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
        var workers = new List<int>(unitIndices.Length);
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            var playerId = Combat.Teams[unit];
            if (Economy.ValidateGather(playerId, unit, resource).Succeeded)
                workers.Add(unit);
        }
        if (workers.Count == 0)
            return;
        var owner = Combat.Teams[workers[0]];
        var workerArray = workers.ToArray();
        var assignments = Economy.AssignGatherTargets(
            owner, workerArray, resource, Units);
        Span<int> worker = stackalloc int[1];
        for (var index = 0; index < workerArray.Length; index++)
        {
            var unit = workerArray[index];
            worker[0] = unit;
            Construction.InterruptBuilders(worker);
            Economy.BeginGather(
                unit, assignments[index],
                Units.Positions[unit], Units.Radii[unit],
                _economyMoveWorker);
        }
    }

    private void ExecuteReturnCargoTask(ReadOnlySpan<int> unitIndices)
    {
        Span<int> worker = stackalloc int[1];
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            var playerId = Combat.Teams[unit];
            if (!Economy.ValidateReturnCargo(playerId, unit).Succeeded)
                continue;
            worker[0] = unit;
            Construction.InterruptBuilders(worker);
            Economy.BeginReturnCargo(
                unit, Units.Positions[unit], Units.Radii[unit],
                _economyMoveWorker);
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
            if (!Construction.IsAlive(building))
                continue;
            var snapshot = Construction.Observe(building);
            if (snapshot.State == BuildingLifecycleState.WaitingForBuilder &&
                snapshot.BuilderUnit == -1 && snapshot.ReservationId.IsValid)
            {
                var placement = snapshot.Type.PlacementProfile;
                var assessment = AssessBuildingPlacement(
                    snapshot.Bounds,
                    new BuildingPlacementRules(
                        placement.MinimumPassageClass,
                        placement.UnitPadding),
                    snapshot.ReservationId);
                if (!assessment.Static.Succeeded &&
                    ConstructionQueuePolicy.StaticFailureAction ==
                        QueuedConstructionStaticFailureAction.RefundAndContinue)
                {
                    if (!Construction.RejectQueuedReservation(
                            building, snapshot.PlayerId, Economy))
                    {
                        throw new InvalidOperationException(
                            "Invalid queued construction could not be rejected.");
                    }
                    continue;
                }
            }
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
            if (!Diplomacy.IsEnemy(
                    Combat.Teams[unit], Combat.Teams[targetUnit]))
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
            if (!IsHostileBuilding(buildingId, Combat.Teams[unit]))
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
        UpdateConstructionEvacuations();
        Construction.Update(
            delta,
            Units,
            Economy,
            _constructionMoveWorker,
            _constructionStopWorker,
            _constructionCommitFootprint,
            EvacuateConstructionStartOccupant);
        Production.Update(
            delta,
            Construction,
            Economy.Players,
            Units,
            Combat,
            World,
            _productionSpawnUnit,
            _productionApplyRally,
            _productionEvacuateExitBlocker);
        Technology.Update(delta, Construction, Economy.Players);
        Economy.Update(
            delta, Units, _economyMoveWorker, _economyStopWorker);
        UpdateProducedUnitRallyFollowers();
        Concealment.Update(delta);
        FreezeConcealmentRestrictedUnits();
        for (var unit = 0; unit < Units.Count; unit++)
            _unitCollisionSuppressed[unit] =
                Economy.SuppressesUnitCollision(unit) ||
                Construction.SuppressesBuilderUnitCollision(unit);
        Metrics.EconomyMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        Visibility.UpdateDetection(Units, Combat);
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
        UpdateCombatContacts();
        _steeringSolver.Solve(
            Units, delta, _unitCollisionSuppressed, Combat.ConcealmentKinds,
            _combatContacts, Combat.TargetKinds);
        _chokeController?.ConstrainSolvedVelocities(Units);
        Metrics.SteeringMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        Integrate(delta);
        Metrics.IntegrateMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        SolveCollisions();
        _chokeController?.ConstrainPositions(Units);
        AdoptDisplacedStationaryReservations();
        Metrics.CollisionMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        UpdateProgress(delta);
        UpdateDynamicBlockageProgress();
        _overflowResolver.UpdateStallTracking(Units, Combat.TargetKinds);
        ResolveDynamicBlockageTimeouts();
        UpdateDestinationLocalRematch();
        UpdateDestinationReflow();
        UpdateDestinationYielding();
        UpdateDestinationOverflow();
        FinalizeSettledMovementGroups();
        UpdateMetrics();
        Metrics.RecoveryMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        AdvanceQueuedOrders();
        Visibility.Update(Units, Combat, Construction);
        Match.Update(Metrics.Tick, Construction, Diplomacy);
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
                        var assignment = Economy.AssignGatherTargets(
                            playerId, produced, rally.ResourceNode, Units,
                            distributeSingle: true)[0];
                        Economy.BeginGather(
                            unit, assignment,
                            Units.Positions[unit], Units.Radii[unit],
                            _economyMoveWorker);
                    }
                    else
                    {
                        IssueMove(produced, rally.Position);
                    }
                    break;
                case RallyTargetKind.FriendlyUnit:
                    if ((uint)rally.Unit >= (uint)Units.Count ||
                        !Units.Alive[rally.Unit] ||
                        Combat.Teams[rally.Unit] != playerId)
                    {
                        break;
                    }
                    var target = Units.Positions[rally.Unit];
                    ExecuteMoveOrder(
                        produced, target, UnitCommandIntent.Move);
                    CommandQueues.Begin(
                        unit,
                        new UnitOrder(
                            UnitOrderKind.FollowFriendly,
                            target,
                            rally.Unit),
                        wasQueued: false);
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

    private void UpdateProducedUnitRallyFollowers()
    {
        const float followRepathDistance = 8f;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] || !CommandQueues.HasActiveOrders[unit] ||
                CommandQueues.ActiveKinds[unit] != UnitOrderKind.FollowFriendly)
            {
                continue;
            }
            var target = CommandQueues.ActiveTargetUnits[unit];
            if ((uint)target < (uint)Units.Count && Units.Alive[target] &&
                Diplomacy.IsFriendly(
                    Combat.Teams[unit], Combat.Teams[target]))
            {
                var position = Units.Positions[target];
                CommandQueues.ActivePositions[unit] = position;
                if (Vector2.DistanceSquared(
                        Units.MoveGoals[unit], position) >
                    followRepathDistance * followRepathDistance)
                {
                    SetCombatDestination(unit, position);
                }
                continue;
            }

            var lastPosition = CommandQueues.ActivePositions[unit];
            CommandQueues.ActiveKinds[unit] = UnitOrderKind.Move;
            CommandQueues.ActiveTargetUnits[unit] = -1;
            if (Vector2.DistanceSquared(
                    Units.MoveGoals[unit], lastPosition) > 0.01f)
            {
                SetCombatDestination(unit, lastPosition);
            }
        }
    }

    private bool EvacuateProductionExitBlocker(
        int playerId,
        int blockerUnit,
        SimRect producerBounds,
        float producedUnitRadius)
    {
        if ((uint)blockerUnit >= (uint)Units.Count ||
            !Units.Alive[blockerUnit] ||
            Combat.Teams[blockerUnit] != playerId ||
            Construction.IsAssignedBuilder(blockerUnit))
        {
            return false;
        }
        if (!ConstructionStartEvacuation.TryFindExit(
                World,
                producerBounds,
                Units.Positions[blockerUnit],
                Units.Radii[blockerUnit],
                producedUnitRadius + 24f,
                out var exit))
        {
            return false;
        }
        if (Vector2.DistanceSquared(Units.MoveGoals[blockerUnit], exit) <= 1f &&
            Units.Modes[blockerUnit] is not UnitMoveMode.Idle)
        {
            return true;
        }

        Span<int> blocker = stackalloc int[1];
        blocker[0] = blockerUnit;
        if (Economy.IsWorker(blockerUnit))
            Economy.Cancel(blocker);
        _issuingSystemOrder = true;
        try
        {
            IssueMove(blocker, exit);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
        return true;
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

            if (segment.Length > 0 &&
                Vector2.DistanceSquared(segment[^1], segmentGoal) > 0.25f &&
                World.IsSegmentFree(
                    segment[^1], segmentGoal, navigationRadius))
            {
                segment = [.. segment, segmentGoal];
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
            var arrivalStopRadius = ArrivalStopDistance(unit);

            if (finalPoint && distance <= arrivalStopRadius)
            {
                Units.Modes[unit] = UnitMoveMode.Arrived;
                Units.Paths[unit] = null;
                Units.Velocities[unit] = Vector2.Zero;
                Units.NextVelocities[unit] = Vector2.Zero;
                if (Combat.CommandIntents[unit] == UnitCommandIntent.Move)
                    Combat.SetCommand(
                        unit, UnitCommandIntent.None, Units.Positions[unit]);
                continue;
            }

            if (distance < 0.0001f)
            {
                continue;
            }

            var speed = Units.MaxSpeeds[unit];
            if (finalPoint)
            {
                var physicalBrakingRadius = speed * speed /
                    (2f * Units.Accelerations[unit]) + arrivalStopRadius;
                var responseRadius = MathF.Max(
                    BaseArrivalResponseRadius, physicalBrakingRadius);
                var normalized = Math.Clamp(
                    (distance - arrivalStopRadius) /
                    (responseRadius - arrivalStopRadius), 0f, 1f);
                var smooth = normalized * normalized *
                             (3f - 2f * normalized);
                var minimumSpeed = MathF.Min(
                    MaximumMinimumArrivalSpeed,
                    speed * MinimumArrivalSpeedFactor);
                speed = minimumSpeed + (speed - minimumSpeed) * smooth;
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
                    if (UnitCollisionPolicy.SuppressesPair(
                            _unitCollisionSuppressed[unit],
                            Combat.ConcealmentKinds[unit],
                            _unitCollisionSuppressed[neighbor],
                            Combat.ConcealmentKinds[neighbor]))
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

                    var resolution = UnitPushPriorityPolicy.Resolve(
                        Units,
                        unit,
                        neighbor,
                        _combatContacts[unit],
                        _combatContacts[neighbor],
                        normal,
                        Combat.TargetKinds[unit] != CombatTargetKind.None,
                        Combat.TargetKinds[neighbor] != CombatTargetKind.None);
                    if (resolution.LeftCorrectionShare <= 0f &&
                        resolution.RightCorrectionShare <= 0f)
                    {
                        continue;
                    }

                    var correctionMagnitude = (penetration + 0.01f) * 0.72f;
                    var correction = normal * correctionMagnitude;
                    Units.CollisionCorrections[unit] -=
                        correction * resolution.LeftCorrectionShare;
                    Units.CollisionCorrections[neighbor] +=
                        correction * resolution.RightCorrectionShare;
                    if (resolution.LeftPushing || resolution.RightPushing)
                    {
                        Metrics.PriorityPushPairs++;
                        Metrics.PriorityPushDisplacement += correctionMagnitude *
                            (resolution.LeftPushing
                                ? resolution.RightCorrectionShare
                                : resolution.LeftCorrectionShare);
                    }
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

    private void UpdateCombatContacts()
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            _combatContacts[unit] = Units.Alive[unit]
                ? EvaluateCombatContact(unit)
                : default;
        }
    }

    private void AdoptDisplacedStationaryReservations()
    {
        var displacedCount = 0;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] ||
                Units.Modes[unit] is not
                    (UnitMoveMode.Idle or UnitMoveMode.Arrived) ||
                Combat.TargetKinds[unit] != CombatTargetKind.None ||
                Units.DestinationYieldPhases[unit] != DestinationYieldPhase.None)
            {
                Units.ReservationMigrationTicks[unit] = 0;
                continue;
            }

            if (Vector2.DistanceSquared(
                    Units.Positions[unit], Units.PreviousPositions[unit]) > 0.01f)
            {
                Units.ReservationMigrationTicks[unit] =
                    ReservationMigrationSettleTicks;
                continue;
            }

            var settleTicks = Units.ReservationMigrationTicks[unit];
            if (settleTicks <= 0)
                continue;
            settleTicks--;
            Units.ReservationMigrationTicks[unit] = settleTicks;
            if (settleTicks == 0)
                _displacedStationaryUnits[displacedCount++] = unit;
        }

        if (displacedCount == 0)
            return;

        ResolveTerminalReservations(
            _displacedStationaryUnits.AsSpan(0, displacedCount),
            _reservationTargetScratch.AsSpan(0, displacedCount),
            ReservationMigrationRelaxationPasses);
        for (var index = 0; index < displacedCount; index++)
        {
            var unit = _displacedStationaryUnits[index];
            var reservation = _reservationTargetScratch[index];
            var offset = reservation - Units.Positions[unit];
            if (offset.LengthSquared() > MaximumMigratedReservationDistance *
                MaximumMigratedReservationDistance)
            {
                reservation = Units.Positions[unit] + Vector2.Normalize(offset) *
                    MaximumMigratedReservationDistance;
            }
            Units.SlotTargets[unit] = reservation;
            Units.MoveGoals[unit] = reservation;
            Units.DestinationYieldReturnTargets[unit] = reservation;
            Units.DestinationYieldPoints[unit] = reservation;
        }
    }

    private void UpdateDynamicBlockageProgress()
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] ||
                Units.Modes[unit] != UnitMoveMode.Moving ||
                Units.PathPending[unit] ||
                Units.ChokePhases[unit] != ChokePhase.None ||
                _unitCollisionSuppressed[unit])
            {
                Units.DynamicBlockageTicks[unit] = 0;
                Units.DynamicBlockageBestDistances[unit] =
                    Vector2.Distance(
                        Units.Positions[unit], Units.SlotTargets[unit]);
                continue;
            }

            var preferred = Units.PreferredVelocities[unit];
            var preferredLengthSquared = preferred.LengthSquared();
            var forwardVelocity = Vector2.Dot(
                Units.Velocities[unit], preferred);
            if (Units.DynamicBlockageTicks[unit] == 0 &&
                forwardVelocity > 0f &&
                forwardVelocity * forwardVelocity >=
                preferredLengthSquared *
                DynamicBlockageProbeForwardSpeed *
                DynamicBlockageProbeForwardSpeed)
            {
                continue;
            }

            var probesThisTick =
                (Metrics.Tick + unit) % DynamicBlockageProbeIntervalTicks == 0;
            if (probesThisTick && !IsBlockedByUnit(unit))
            {
                Units.DynamicBlockageTicks[unit] = 0;
                Units.DynamicBlockageBestDistances[unit] =
                    RemainingPathDistance(unit, Units.Paths[unit]);
                continue;
            }
            if (!probesThisTick && Units.DynamicBlockageTicks[unit] == 0)
                continue;

            var remaining = RemainingPathDistance(unit, Units.Paths[unit]);

            if (Units.DynamicBlockageTicks[unit] == 0)
            {
                Units.DynamicBlockageTicks[unit] = 1;
                Units.DynamicBlockageBestDistances[unit] = remaining;
                continue;
            }

            Units.DynamicBlockageTicks[unit]++;
            if (Units.DynamicBlockageTicks[unit] < DynamicBlockageTimeoutTicks)
                continue;

            var windowStartDistance =
                Units.DynamicBlockageBestDistances[unit];
            var meaningfulProgress = MathF.Max(
                DynamicBlockageProgressDistance,
                Units.Radii[unit] * 1.5f);
            if (windowStartDistance - remaining >= meaningfulProgress)
            {
                Units.DynamicBlockageTicks[unit] = 1;
                Units.DynamicBlockageBestDistances[unit] = remaining;
            }
        }
    }

    private bool IsBlockedByUnit(int unit)
    {
        var preferred = Units.PreferredVelocities[unit];
        if (preferred.LengthSquared() <= 1f)
            return false;
        var direction = Vector2.Normalize(preferred);
        var searchRadius = MathF.Max(
            DynamicBlockageLookAheadDistance,
            Units.Radii[unit] * 8f + 32f);
        var neighborCount = _spatialHash.Query(
            Units.Positions[unit], searchRadius, unit, _collisionNeighbors);
        for (var index = 0; index < neighborCount; index++)
        {
            var neighbor = _collisionNeighbors[index];
            if (!Units.Alive[neighbor] ||
                UnitCollisionPolicy.SuppressesPair(
                    _unitCollisionSuppressed[unit],
                    Combat.ConcealmentKinds[unit],
                    _unitCollisionSuppressed[neighbor],
                    Combat.ConcealmentKinds[neighbor]))
                continue;

            var offset = Units.Positions[neighbor] - Units.Positions[unit];
            var forward = Vector2.Dot(offset, direction);
            var clearance = Units.Radii[unit] + Units.Radii[neighbor] + 4f;
            if (forward < -1f || forward > DynamicBlockageLookAheadDistance)
                continue;
            var lateral = offset - direction * forward;
            var corridorWidth = clearance * 1.25f;
            if (lateral.LengthSquared() <= corridorWidth * corridorWidth)
                return true;
        }
        return false;
    }

    private void ResolveDynamicBlockageTimeouts()
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] ||
                Units.DynamicBlockageTicks[unit] <
                    DynamicBlockageTimeoutTicks ||
                Units.Modes[unit] != UnitMoveMode.Moving ||
                Units.DestinationYieldPhases[unit] != DestinationYieldPhase.None)
                continue;

            SettleBlockedNavigationLeg(unit);
        }
    }

    private void SettleBlockedNavigationLeg(int unit)
    {
        var position = Units.Positions[unit];
        Units.Paths[unit] = null;
        Units.RouteWaypoints[unit] = [];
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Arrived;
        Units.SlotTargets[unit] = position;
        Units.MoveGoals[unit] = position;
        Units.PreferredVelocities[unit] = Vector2.Zero;
        Units.Velocities[unit] = Vector2.Zero;
        Units.NextVelocities[unit] = Vector2.Zero;
        Units.ActiveChokeIds[unit] = -1;
        Units.ChokeDirections[unit] = 0;
        Units.ChokePhases[unit] = ChokePhase.None;
        Units.ChokeAdmitted[unit] = false;
        Units.MovementGroupIds[unit] = 0;
        Units.MovementGroupSizes[unit] = 1;
        Units.DestinationBestDistances[unit] = 0f;
        Units.DestinationStallTicks[unit] = 0;
        Units.DestinationNearTicks[unit] = 0;
        Units.DestinationOverflowed[unit] = false;
        Units.DestinationYieldReturnTargets[unit] = position;
        Units.DestinationYieldPoints[unit] = position;
        Units.DynamicBlockageTicks[unit] = 0;
        Units.DynamicBlockageBestDistances[unit] = 0f;
        Units.ReservationMigrationTicks[unit] = 0;
        if (Combat.CommandIntents[unit] == UnitCommandIntent.Move)
            Combat.SetCommand(unit, UnitCommandIntent.None, position);
        else if (Combat.CommandIntents[unit] == UnitCommandIntent.AttackMove &&
                 Combat.TargetKinds[unit] == CombatTargetKind.None)
            Combat.AttackMoveGoals[unit] = position;
        Metrics.DynamicBlockageSettles++;
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
                    Combat.TargetKinds,
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
                    Combat.TargetKinds,
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

    private void FinalizeSettledMovementGroups()
    {
        for (var leader = 0; leader < Units.Count; leader++)
        {
            var groupId = Units.MovementGroupIds[leader];
            if (!Units.Alive[leader] || groupId <= 0 ||
                Units.MovementGroupSizes[leader] < TerminalSettleMinimumGroupSize ||
                HasEarlierGroupMember(leader, groupId))
                continue;

            var members = 0;
            var arrived = 0;
            var remainingEligible = true;
            var compactGroup = Units.MovementGroupSizes[leader] <= 8;
            for (var unit = leader; unit < Units.Count; unit++)
            {
                if (!Units.Alive[unit] ||
                    Units.MovementGroupIds[unit] != groupId)
                    continue;
                members++;
                if (Combat.TargetKinds[unit] != CombatTargetKind.None)
                {
                    remainingEligible = false;
                    continue;
                }
                if (Units.Modes[unit] == UnitMoveMode.Arrived)
                {
                    arrived++;
                    continue;
                }
                var distance = Vector2.Distance(
                    Units.Positions[unit], Units.SlotTargets[unit]);
                var maximumDistance = compactGroup
                    ? 60f
                    : TerminalSettleMaximumDistance;
                remainingEligible &=
                    Units.Modes[unit] is
                        UnitMoveMode.Moving or UnitMoveMode.WaitingForPath &&
                    distance <= maximumDistance;
            }

            var required = compactGroup
                ? members - 1
                : (int)MathF.Ceiling(members * TerminalSettleQuorum);
            if (members < TerminalSettleMinimumGroupSize ||
                arrived < required || !remainingEligible)
                continue;

            var groupUnits = new int[members];
            var memberIndex = 0;
            for (var unit = leader; unit < Units.Count; unit++)
            {
                if (!Units.Alive[unit] ||
                    Units.MovementGroupIds[unit] != groupId)
                    continue;
                groupUnits[memberIndex++] = unit;
            }
            var terminalAssignments = new Vector2[groupUnits.Length];
            ResolveTerminalReservations(groupUnits, terminalAssignments);

            for (var index = 0; index < groupUnits.Length; index++)
            {
                var unit = groupUnits[index];
                Units.SlotTargets[unit] = terminalAssignments[index];
                if (Combat.CommandIntents[unit] == UnitCommandIntent.AttackMove)
                    Combat.AttackMoveGoals[unit] = terminalAssignments[index];
                Units.Paths[unit] = null;
                Units.RouteWaypoints[unit] = [];
                Units.PathPending[unit] = false;
                Units.Modes[unit] = UnitMoveMode.Arrived;
                Units.PreferredVelocities[unit] = Vector2.Zero;
                Units.Velocities[unit] = Vector2.Zero;
                Units.NextVelocities[unit] = Vector2.Zero;
                Units.DestinationYieldPhases[unit] =
                    DestinationYieldPhase.None;
                Units.MovementGroupIds[unit] = 0;
                Units.MovementGroupSizes[unit] = 1;
                if (Combat.CommandIntents[unit] == UnitCommandIntent.Move)
                    Combat.SetCommand(
                        unit, UnitCommandIntent.None, Units.Positions[unit]);
            }
        }
    }

    private void ResolveTerminalReservations(
        ReadOnlySpan<int> groupUnits,
        Span<Vector2> targets,
        int relaxationPasses = 16)
    {
        if (targets.Length < groupUnits.Length)
            throw new ArgumentException(
                "Reservation target buffer is too small.", nameof(targets));
        _reservationSelectionScratch.AsSpan(0, Units.Count).Fill(-1);
        for (var index = 0; index < groupUnits.Length; index++)
        {
            var unit = groupUnits[index];
            targets[index] = Units.Positions[unit];
            _reservationSelectionScratch[unit] = index;
        }

        const float reservationPadding = 2f;
        for (var pass = 0; pass < relaxationPasses; pass++)
        {
            for (var leftIndex = 0; leftIndex < groupUnits.Length; leftIndex++)
            {
                var left = groupUnits[leftIndex];
                for (var right = 0; right < Units.Count; right++)
                {
                    if (right == left || !Units.Alive[right])
                        continue;
                    var rightIndex = _reservationSelectionScratch[right];
                    if (rightIndex >= 0 && rightIndex <= leftIndex)
                        continue;

                    var rightTarget = rightIndex >= 0
                        ? targets[rightIndex]
                        : Units.SlotTargets[right];
                    var offset = targets[leftIndex] - rightTarget;
                    var distance = offset.Length();
                    var required = Units.Radii[left] + Units.Radii[right] +
                                   reservationPadding;
                    if (distance >= required)
                        continue;

                    var normal = distance > 0.0001f
                        ? offset / distance
                        : DeterministicNormal(left, right);
                    var correction = required - distance;
                    if (rightIndex >= 0)
                    {
                        MoveTerminalReservation(
                            left, ref targets[leftIndex], normal * correction * 0.5f);
                        MoveTerminalReservation(
                            right, ref targets[rightIndex], -normal * correction * 0.5f);
                    }
                    else
                    {
                        MoveTerminalReservation(
                            left, ref targets[leftIndex], normal * correction);
                    }
                }
            }
        }

    }

    private void MoveTerminalReservation(
        int unit,
        ref Vector2 target,
        Vector2 displacement)
    {
        var candidate = World.Bounds
            .Inset(Units.Radii[unit] + 2f)
            .Clamp(target + displacement);
        if (World.IsDiscFree(candidate, Units.Radii[unit]))
            target = candidate;
    }

    private bool HasEarlierGroupMember(int unit, int groupId)
    {
        for (var candidate = 0; candidate < unit; candidate++)
        {
            if (Units.Alive[candidate] &&
                Units.MovementGroupIds[candidate] == groupId)
                return true;
        }
        return false;
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
                    Combat.TargetKinds,
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
        Metrics.MaximumDynamicBlockageTicks = 0;
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
            Metrics.MaximumDynamicBlockageTicks = Math.Max(
                Metrics.MaximumDynamicBlockageTicks,
                Units.DynamicBlockageTicks[unit]);
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
            if (!Units.Alive[unit] ||
                CommandQueues.ConstructionEvacuationActive[unit] ||
                !IsActiveOrderComplete(unit))
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

    private void RejectPendingConstructionsForUnit(int unit)
    {
        if (ConstructionQueuePolicy.BuilderDeathAction !=
            QueuedConstructionBuilderDeathAction.RefundPendingReservations)
        {
            return;
        }
        for (var pending = 0;
             pending < CommandQueues.PendingCounts[unit];
             pending++)
        {
            var order = CommandQueues.PendingAt(unit, pending);
            if (order.Kind != UnitOrderKind.ResumeConstruction ||
                order.TargetBuilding < 0)
            {
                continue;
            }
            var buildingId = new GameplayBuildingId(order.TargetBuilding);
            if (!Construction.IsAlive(buildingId))
                continue;
            var building = Construction.Observe(buildingId);
            if (building.State == BuildingLifecycleState.WaitingForBuilder &&
                building.BuilderUnit == -1 && building.ReservationId.IsValid)
            {
                Construction.RejectQueuedReservation(
                    buildingId, building.PlayerId, Economy);
            }
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
            UnitOrderKind.ReturnCargo =>
                !Economy.IsGatherer(unit) ||
                Economy.Worker(unit).CargoAmount <= 0,
            UnitOrderKind.ResumeConstruction =>
                IsResumeConstructionOrderComplete(unit),
            UnitOrderKind.ActivateConcealment =>
                Concealment.RequestedStateReached(unit, active: true),
            UnitOrderKind.DeactivateConcealment =>
                Concealment.RequestedStateReached(unit, active: false),
            UnitOrderKind.FollowFriendly => false,
            UnitOrderKind.Stop or UnitOrderKind.Hold => true,
            _ => true
        };
    }

    private bool IsGatherResourceOrderComplete(int unit)
    {
        if (!Economy.IsGatherer(unit)) return true;
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
        Units.Velocities[unit] = Vector2.Zero;
        Units.NextVelocities[unit] = Vector2.Zero;
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

        var arrivalStopRadius = ArrivalStopDistance(unit);
        if (Vector2.DistanceSquared(Units.Positions[unit], target) <=
            arrivalStopRadius * arrivalStopRadius)
        {
            Units.Modes[unit] = UnitMoveMode.Arrived;
            Units.SlotTargets[unit] = target;
            Units.MoveGoals[unit] = target;
            return;
        }

        if ((Units.Modes[unit] is UnitMoveMode.Moving or
                UnitMoveMode.WaitingForPath) &&
            Vector2.DistanceSquared(Units.MoveGoals[unit], target) <= 0.01f)
        {
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
        RejectPendingConstructionsForUnit(unit);
        CommandQueues.ClearPending(unit);
        CommandQueues.HasActiveOrders[unit] = false;
    }

    bool ICombatMovementDriver.IsBuildingTargetValid(
        int attacker,
        GameplayBuildingId building) =>
        (uint)attacker < (uint)Units.Count && Units.Alive[attacker] &&
        IsHostileBuilding(building, Combat.Teams[attacker]);

    bool ICombatMovementDriver.IsBuildingAlive(GameplayBuildingId building) =>
        Construction.IsAlive(building);

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
        => new(EffectiveBuildingArmor(building), building.Type.Attributes);

    private float EffectiveBuildingArmor(GameplayBuildingSnapshot building)
    {
        if (building.State != BuildingLifecycleState.Completed)
            return 0f;
        var level = CombatBuildingArmorTechnologyId < 0
            ? 0
            : Technology.Level(
                building.PlayerId, CombatBuildingArmorTechnologyId);
        return building.Type.Armor +
               level * building.Type.ArmorUpgradePerLevel;
    }

    private void SetCombatDestination(int unit, Vector2 target)
    {
        var clamped = World.Bounds.Inset(Units.Radii[unit] + 2f).Clamp(target);
        Units.SlotTargets[unit] = clamped;
        Units.MoveGoals[unit] = clamped;
        if (Combat.CommandIntents[unit] != UnitCommandIntent.AttackMove)
        {
            Units.MovementGroupIds[unit] = 0;
            Units.MovementGroupSizes[unit] = 1;
        }
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
        Units.DestinationOverflowed[unit] = false;
        QueueNavigationReplan(unit, countInvalidation: false);
    }

    private float ArrivalStopDistance(int unit)
    {
        if (Construction.IsAssignedBuilder(unit) ||
            CommandQueues.ConstructionEvacuationActive[unit] ||
            Combat.TargetKinds[unit] != CombatTargetKind.None ||
            Economy.IsGatherer(unit) && Economy.Worker(unit).State is not
                (WorkerEconomyState.None or WorkerEconomyState.Idle))
            return MinimumArrivalStopRadius;
        return Math.Clamp(
            Units.Radii[unit],
            MinimumArrivalStopRadius,
            MaximumArrivalStopRadius);
    }

    private CombatContactSnapshot EvaluateCombatContact(int unit) =>
        CombatContactPolicy.Evaluate(
            Units.Modes[unit],
            Combat.Phases[unit],
            Combat.CommandIntents[unit],
            Combat.PositioningKinds[unit],
            Combat.WindupRemaining[unit],
            Combat.CooldownRemaining[unit],
            Combat.CanMoveDuringWindup[unit],
            Combat.CanMoveDuringCooldown[unit]);

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
            ExecuteMoveOrder(
                worker, target, UnitCommandIntent.Move,
                exactDestination: true);
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
            Units.Velocities[unit] = Vector2.Zero;
            Units.NextVelocities[unit] = Vector2.Zero;
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
            ExecuteMoveOrder(
                worker, target, UnitCommandIntent.Move,
                exactDestination: true);
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
            Units.Velocities[unit] = Vector2.Zero;
            Units.NextVelocities[unit] = Vector2.Zero;
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private HardFootprintCommitResult CommitConstructionFootprint(
        ConstructionReservationId reservationId,
        SimRect footprint,
        BuildingPlacementRules rules)
    {
        _issuingConstructionWorldMutation = true;
        try
        {
            return TryCommitHardFootprint(footprint, rules, reservationId);
        }
        finally
        {
            _issuingConstructionWorldMutation = false;
        }
    }

    private Vector2 ResolveConstructionAccessPoint(
        SimRect bounds,
        int builderUnit,
        float placementPadding,
        ConstructionReservationId ignoreReservation,
        EconomyResourceNodeId ignoredResourceNode) =>
        ConstructionAccessPointResolver.Resolve(
            World,
            _pathProvider,
            bounds,
            Units.Positions[builderUnit],
            Units.Radii[builderUnit],
            Units.NavigationRadii[builderUnit],
            placementPadding,
            (center, radius) => Construction.Reservations.IsDiscFree(
                                    center, radius, ignoreReservation) &&
                                Economy.IsDiscClearOfResources(
                                    center, radius, ignoredResourceNode));

    private void EvacuateConstructionStartOccupant(
        int playerId,
        int builderUnit,
        GameplayBuildingId buildingId,
        SimRect footprint,
        BuildingPlacementRules rules,
        int occupantUnit)
    {
        if ((uint)occupantUnit >= (uint)Units.Count ||
            !Units.Alive[occupantUnit])
        {
            return;
        }

        Span<ConstructionBlocker> blockers = stackalloc ConstructionBlocker[
            ConstructionEvictionPlanner.MaximumBlockers];
        var blockerCount = 0;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] || unit == builderUnit ||
                !footprint.Expanded(Units.Radii[unit] + rules.UnitPadding)
                    .Contains(Units.Positions[unit]))
            {
                continue;
            }
            if (blockerCount == blockers.Length)
                return;
            blockers[blockerCount++] = new ConstructionBlocker(
                unit,
                ClassifyConstructionBlocker(
                    unit, playerId, buildingId));
        }

        var allAlreadyEvacuating = blockerCount > 0;
        for (var index = 0; index < blockerCount; index++)
        {
            var unit = blockers[index].Unit;
            allAlreadyEvacuating &=
                CommandQueues.ConstructionEvacuationActive[unit] &&
                CommandQueues.ConstructionEvacuationBuildings[unit] ==
                    buildingId.Value;
        }
        if (allAlreadyEvacuating)
            return;

        var plan = ConstructionEvictionPlanner.Plan(
                World,
                Units,
                footprint,
                rules.UnitPadding,
                blockers[..blockerCount],
                ConstructionBlockerPolicy.ProjectDefault);
        if (!plan.CanIssue)
        {
            return;
        }

        for (var index = 0; index < plan.Assignments.Length; index++)
        {
            var assignment = plan.Assignments[index];
            if (CommandQueues.ConstructionEvacuationActive[assignment.Unit] &&
                CommandQueues.ConstructionEvacuationBuildings[assignment.Unit] ==
                    buildingId.Value &&
                Vector2.DistanceSquared(
                    CommandQueues.ConstructionEvacuationTargets[assignment.Unit],
                    assignment.Target) <= 1f)
            {
                continue;
            }
            CommandQueues.ConstructionEvacuationActive[assignment.Unit] = true;
            CommandQueues.ConstructionEvacuationBuildings[assignment.Unit] =
                buildingId.Value;
            CommandQueues.ConstructionEvacuationTargets[assignment.Unit] =
                assignment.Target;
            CommandQueues.ConstructionEvacuationFootprints[assignment.Unit] =
                footprint;
            SetCombatDestination(assignment.Unit, assignment.Target);
        }
    }

    private ConstructionBlockerKind ClassifyConstructionBlocker(
        int unit,
        int playerId,
        GameplayBuildingId buildingId)
    {
        if (Combat.Teams[unit] != playerId)
            return Diplomacy.IsFriendly(playerId, Combat.Teams[unit])
                ? ConstructionBlockerKind.AuthorityAlly
                : ConstructionBlockerKind.AuthorityEnemy;
        if (Construction.IsAssignedBuilder(unit))
            return ConstructionBlockerKind.FriendlyAssignedBuilder;
        if (CommandQueues.ConstructionEvacuationActive[unit] &&
            CommandQueues.ConstructionEvacuationBuildings[unit] !=
                buildingId.Value)
        {
            return ConstructionBlockerKind.FriendlyOtherOrder;
        }
        if (Economy.IsWorker(unit))
        {
            var state = Economy.Worker(unit).State;
            if (state is not WorkerEconomyState.None and
                not WorkerEconomyState.Idle)
            {
                return ConstructionBlockerKind.FriendlyEconomyTask;
            }
        }
        if (Units.Modes[unit] == UnitMoveMode.Hold)
            return ConstructionBlockerKind.FriendlyHold;
        if (!CommandQueues.HasActiveOrders[unit] ||
            CommandQueues.ActiveKinds[unit] is
                UnitOrderKind.Move or UnitOrderKind.Stop)
        {
            return ConstructionBlockerKind.MovableFriendly;
        }
        return ConstructionBlockerKind.FriendlyOtherOrder;
    }

    private void UpdateConstructionEvacuations()
    {
        Span<int> single = stackalloc int[1];
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!CommandQueues.ConstructionEvacuationActive[unit])
                continue;
            if (!Units.Alive[unit])
            {
                ClearConstructionEvacuation(unit);
                continue;
            }
            var buildingId = new GameplayBuildingId(
                CommandQueues.ConstructionEvacuationBuildings[unit]);
            var pending = Construction.IsAlive(buildingId) &&
                          Construction.Observe(buildingId).State is
                              BuildingLifecycleState.ReservedApproach or
                              BuildingLifecycleState.BlockedAtStart;
            if (pending)
            {
                var target =
                    CommandQueues.ConstructionEvacuationTargets[unit];
                if (Vector2.DistanceSquared(Units.MoveGoals[unit], target) > 1f)
                    SetCombatDestination(unit, target);
                continue;
            }

            ClearConstructionEvacuation(unit);
            single[0] = unit;
            if (CommandQueues.HasActiveOrders[unit] &&
                CommandQueues.ActiveKinds[unit] == UnitOrderKind.Move)
            {
                SetCombatDestination(
                    unit, CommandQueues.ActivePositions[unit]);
            }
            else
            {
                ExecuteStop(single);
            }
        }
    }

    private void ClearConstructionEvacuation(int unit)
    {
        CommandQueues.ConstructionEvacuationActive[unit] = false;
        CommandQueues.ConstructionEvacuationBuildings[unit] = -1;
        CommandQueues.ConstructionEvacuationTargets[unit] = Vector2.Zero;
        CommandQueues.ConstructionEvacuationFootprints[unit] = default;
    }

    private bool IsHostileBuilding(
        GameplayBuildingId building,
        int viewerPlayerId) =>
        Construction.IsAlive(building) &&
        Diplomacy.IsEnemy(
            viewerPlayerId, Construction.Observe(building).PlayerId);


}
