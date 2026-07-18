using System.Diagnostics;
using System.Numerics;

namespace RtsDemo.Simulation;

public sealed class RtsSimulation : ICombatMovementDriver, IAbilityRuntimeWorld
{
    // Count-only throttling allowed a batch of individually reasonable A*
    // requests to accumulate into a long frame. These deterministic work
    // units bound aggregate search complexity without consulting wall time,
    // so replay and lockstep behavior remain stable across machines.
    private const int PathWorkBudgetPerTick = 9_000;
    private const int PathRequestBaseWork = 128;
    private const int PathRawPointWork = 8;
    private const int PathSimplifiedPointWork = 16;
    private const string ActiveConcealmentAbilityId = "active-concealment";
    private const string ActiveRevealAbilityId = "active-reveal";
    private const float WaypointRadius = 13f;
    private const float MinimumArrivalStopRadius = 3.5f;
    private const float MaximumArrivalStopRadius = 8f;
    private const int CollisionIterations = 3;
    private const int ResidualCollisionPassLimit = 18;
    private const int ResidualCollisionBroadphaseBatchSize = 6;
    private const float ResidualCollisionTolerance = 0.005f;
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
    private const int MaximumReservationRelaxationPasses = 64;
    private const int MaximumReservationMigrationComponentUnits = 128;
    private const float ReservationMigrationActivationDistance = 8f;
    private const float MaximumMigratedReservationDistance = 12f;
    private const int TerminalSettleMinimumGroupSize = 4;
    private const float TerminalSettleQuorum = 0.9f;
    private const float TerminalSettleMaximumDistance = 180f;

    private readonly IPathProvider _pathProvider;
    private readonly Queue<PathRequest> _pathRequests = new();
    private bool[] _pathRequestQueued;
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
    private readonly Action<int, UnitMovementLegResult>
        _economyFinishDropOffMovement;
    private readonly Action<int, Vector2> _constructionMoveWorker;
    private readonly Action<int> _constructionStopWorker;
    private readonly Func<ConstructionReservationId, SimRect, BuildingPlacementRules, int,
        HardFootprintCommitResult> _constructionCommitFootprint;
    private readonly Func<UnitTypeProfile, int, Vector2, int> _productionSpawnUnit;
    private readonly Action<int, int, RallyTarget> _productionApplyRally;
    private readonly Action<int, bool> _concealmentTransitionCompleted;
    private readonly Func<int, int, SimRect, float, bool>
        _productionEvacuateExitBlocker;
    private readonly int[] _collisionNeighbors = new int[64];
    private long[] _collisionPairScratch = new long[4096];
    private bool[] _collisionPairApplicableScratch = new bool[4096];
    private bool[] _collisionResidualMovedUnits = [];
    private CombatContactSnapshot[] _combatContacts;
    private bool[] _unitCollisionSuppressed;
    private int[] _orderReadyUnits;
    private UnitOrder[] _orderReadyOrders;
    private bool[] _orderReadyProcessed;
    private int[] _orderDispatchUnits;
    private int[] _displacedStationaryUnits;
    private Vector2[] _reservationTargetScratch;
    private int[] _reservationSelectionScratch;
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
    private bool _detailedProfilingEnabled;
    private long _combatBuildingOverviewTick = long.MinValue;
    private GameplayBuildingSnapshot[] _combatBuildingOverview = [];
    private long _combatObjectOverviewTick = long.MinValue;
    private CombatObjectSnapshot[] _combatObjectOverview = [];

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
        _pathRequestQueued = new bool[capacity];
        Combat = new CombatStore(capacity);
        CombatObjects = new CombatObjectStore();
        Concealment = new UnitConcealmentController(Units, Combat);
        Abilities = new AbilitySystem(capacity);
        CombatEvents = new CombatEventStream();
        GameplayEvents = new GameplayEventStream();
        AbilityEvents = new AbilityEventStream();
        CommandQueues = new UnitCommandQueueStore(capacity);
        Economy = new EconomySystem(capacity);
        Economy.ReachableDropOffResolver = ResolveReachableDropOff;
        Construction = new ConstructionSystem();
        BuildingUpgrades = new BuildingUpgradeSystem();
        Production = new ProductionSystem();
        Technology = new TechnologySystem();
        Diplomacy = new PlayerDiplomacySystem();
        Visibility = new PlayerVisibilitySystem(
            world.Bounds, Diplomacy, terrain: world.Terrain);
        Visibility.TechnologyLevelResolver = Technology.Level;
        Match = new MatchSystem();
        BuildingCombat = new BuildingCombatSystem(
            Units, Combat, Construction, Technology, Diplomacy, Visibility,
            CombatTargetLayerForUnit, DamageUnit, Abilities.RemoveMana,
            Abilities.IsHero, Abilities.IsSummoned);
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
            Diplomacy.IsEnemy, CanUnitAttack, CanUnitTakeDamage,
            Abilities.CombatModifier,
            CombatTargetLayerForUnit, _spatialHash);
        _economyMoveWorker = MoveEconomyWorker;
        _economyStopWorker = StopEconomyWorker;
        _economyFinishDropOffMovement = FinishEconomyDropOffMovement;
        _constructionMoveWorker = MoveConstructionWorker;
        _constructionStopWorker = StopConstructionWorker;
        _constructionCommitFootprint = CommitConstructionFootprint;
        _productionSpawnUnit = SpawnProducedUnit;
        _productionApplyRally = ApplyProducedUnitRally;
        _concealmentTransitionCompleted = OnConcealmentTransitionCompleted;
        _productionEvacuateExitBlocker = EvacuateProductionExitBlocker;
    }

    public StaticWorld World { get; }
    public UnitStore Units { get; }
    public CombatStore Combat { get; }
    public CombatObjectStore CombatObjects { get; }
    public UnitConcealmentController Concealment { get; }
    public AbilitySystem Abilities { get; }
    public CombatProjectileSystem CombatProjectiles => _combatSystem.Projectiles;
    public CombatEventStream CombatEvents { get; }
    public GameplayEventStream GameplayEvents { get; }
    public AbilityEventStream AbilityEvents { get; }
    public UnitCommandQueueStore CommandQueues { get; }
    public EconomySystem Economy { get; }
    public ConstructionSystem Construction { get; }
    public BuildingUpgradeSystem BuildingUpgrades { get; }
    public QueuedConstructionPolicy ConstructionQueuePolicy { get; } =
        QueuedConstructionPolicy.ProjectDefault;
    public ProductionSystem Production { get; }
    public TechnologySystem Technology { get; }
    public PlayerDiplomacySystem Diplomacy { get; }
    public PlayerVisibilitySystem Visibility { get; }
    public MatchSystem Match { get; }
    public BuildingCombatSystem BuildingCombat { get; }
    public BuildingCombatEventStream BuildingCombatEvents =>
        BuildingCombat.Events;
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
    public bool DetailedProfilingEnabled
    {
        get => _detailedProfilingEnabled;
        set
        {
            _detailedProfilingEnabled = value;
            _buildingConnectivityGuard.ProfilingEnabled = value;
            _combatSystem.ProfilingEnabled = value;
            Visibility.ProfilingEnabled = value;
        }
    }

    public void WarmPathingCaches()
    {
        if (_pathProvider is GridPathProvider gridPathProvider)
            gridPathProvider.WarmConnectivitySnapshots();
    }

    /// <summary>
    /// Captures the live grid-path connectivity layer for presentation-only
    /// diagnostics. It can refresh incremental navigation data and should only
    /// be called by an explicitly enabled navigation debugger.
    /// </summary>
    public NavigationConnectivitySnapshot?
        GetNavigationConnectivitySnapshotForDiagnostics(
            MovementClass movementClass) =>
        _pathProvider is GridPathProvider gridPathProvider
            ? gridPathProvider.GetConnectivitySnapshotForDiagnostics(
                movementClass)
            : null;

    public PathRequest[] CapturePendingPathRequestsForDiagnostics() =>
        _pathRequests.ToArray();

    public int PendingPathRequestCountForDiagnostics => _pathRequests.Count;

    /// <summary>
    /// Runs the authoritative low-level path provider without issuing a unit
    /// order. Intended for explicit offline diagnostics and automated map
    /// audits only.
    /// </summary>
    public Vector2[] FindNavigationPathForDiagnostics(
        Vector2 start,
        Vector2 goal,
        float navigationRadius)
    {
        if (!float.IsFinite(start.X) || !float.IsFinite(start.Y) ||
            !float.IsFinite(goal.X) || !float.IsFinite(goal.Y) ||
            !float.IsFinite(navigationRadius) || navigationRadius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(navigationRadius));
        }
        return _pathProvider.FindPath(start, goal, navigationRadius);
    }

    internal void RefreshNavigationAfterDiagnosticsBootstrap(
        SimRect changedBounds)
    {
        if (_groupRoutePlanner is IGroupRouteNavigationChangeSink routeSink)
            routeSink.OnNavigationChanged(changedBounds);
    }

    public ulong ComputeStateHash() => SimulationStateHasher.Compute(this);

    public UnitMovementSnapshot ObserveUnitMovement(int unit)
    {
        ValidateUnitIndex(unit);
        return new UnitMovementSnapshot(
            unit,
            Units.MovementGoalKinds[unit],
            Units.SlotTargets[unit],
            Units.MovementGoalBounds[unit],
            Units.MovementGoalRadii[unit],
            Units.MovementGoalTargetIds[unit],
            Units.MovementLegResults[unit]);
    }

    public CombatDamageResult PreviewCombatDamage(int attacker, int target) =>
        _combatSystem.PreviewDamage(attacker, target);

    public float EffectiveUnitArmor(int unit)
    {
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit])
            throw new ArgumentOutOfRangeException(nameof(unit));
        return _combatSystem.EffectiveArmor(unit);
    }

    public float RestoreUnitHealth(int unit, float amount)
    {
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit] ||
            !float.IsFinite(amount) || amount <= 0f)
            return 0f;
        var before = Combat.Health[unit];
        Combat.Health[unit] = MathF.Min(
            Combat.MaximumHealth[unit], before + amount);
        return Combat.Health[unit] - before;
    }

    public void TeleportUnit(int unit, Vector2 position) =>
        ((IAbilityRuntimeWorld)this).AbilityTeleportUnit(unit, position);

    public void TeleportUnits(int[] units, Vector2 position) =>
        ((IAbilityRuntimeWorld)this).AbilityTeleportUnits(units, position);

    public CombatAutoTargetScore PreviewAutoTargetScore(
        int attacker,
        int target) => _combatSystem.PreviewAutoTargetScore(attacker, target);

    public CombatObjectId AddCombatObject(in CombatObjectProfile profile) =>
        CombatObjects.Add(profile);

    public CombatObjectSnapshot ObserveCombatObject(CombatObjectId id) =>
        CombatObjects.Observe(id);

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
            CombatObjects = CombatObjects.CaptureRuntimeState(),
            Abilities = Abilities.CaptureRuntimeState(Units.Count),
            CombatProjectiles = CombatProjectiles.CaptureRuntimeState(),
            BuildingCombat = BuildingCombat.CaptureRuntimeState(),
            Economy = Economy.CaptureRuntimeState(Units.Count),
            Diplomacy = Diplomacy.CaptureRuntimeState(),
            Visibility = Visibility.CaptureRuntimeState(),
            Match = Match.CaptureRuntimeState(),
            Construction = Construction.CaptureRuntimeState(),
            BuildingUpgrades = BuildingUpgrades.CaptureRuntimeState(),
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
        if (snapshot.Units.Capacity > Units.Capacity)
        {
            ResizeUnitCapacity(snapshot.Units.Capacity);
        }
        if (snapshot.Units.Capacity != Units.Capacity)
        {
            throw new InvalidOperationException("Simulation runtime capacity mismatch.");
        }
        World.DynamicOccupancy.RestoreRuntimeState(snapshot.DynamicOccupancy);
        if (_groupRoutePlanner is IGroupRouteNavigationChangeSink routeSink)
        {
            routeSink.OnNavigationStateRestored();
        }
        Units.CopyRuntimeStateFrom(snapshot.Units);
        Combat.CopyRuntimeStateFrom(snapshot.Combat);
        CombatObjects.RestoreRuntimeState(snapshot.CombatObjects);
        Abilities.RestoreRuntimeState(snapshot.Abilities, Units.Count);
        CombatProjectiles.RestoreRuntimeState(snapshot.CombatProjectiles);
        BuildingCombat.RestoreRuntimeState(snapshot.BuildingCombat);
        CombatEvents.Reset();
        BuildingCombatEvents.Reset();
        GameplayEvents.Reset();
        AbilityEvents.Reset();
        Economy.RestoreRuntimeState(snapshot.Economy, Units.Count);
        SyncLinkedCombatObjects();
        Diplomacy.RestoreRuntimeState(snapshot.Diplomacy);
        Construction.RestoreRuntimeState(snapshot.Construction);
        Production.RestoreRuntimeState(
            snapshot.Production, Construction, Economy, Units);
        Technology.RestoreRuntimeState(
            snapshot.Technology, Construction, Economy.Players);
        BuildingUpgrades.RestoreRuntimeState(
            snapshot.BuildingUpgrades, Construction, Economy.Players);
        BuildingUpgrades.ValidateQueueExclusivity(Production, Technology);
        Abilities.RefreshDerivedState(this, Units, Combat);
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
        _combatBuildingOverviewTick = long.MinValue;
        _combatObjectOverviewTick = long.MinValue;
        PathBudgetPerTick = snapshot.PrivateState.PathBudgetPerTick;
        _pendingNavigationInvalidations =
            snapshot.PrivateState.PendingNavigationInvalidations;
        _nextMovementGroupId = snapshot.PrivateState.NextMovementGroupId;
        _nextOrderSequenceId = snapshot.PrivateState.NextOrderSequenceId;
        _pathRequests.Clear();
        Array.Clear(_pathRequestQueued);
        for (var index = 0; index < snapshot.PrivateState.PathRequests.Length; index++)
        {
            var request = snapshot.PrivateState.PathRequests[index];
            if ((uint)request.UnitIndex >= (uint)Units.Count ||
                _pathRequestQueued[request.UnitIndex])
            {
                continue;
            }
            _pathRequestQueued[request.UnitIndex] = true;
            _pathRequests.Enqueue(request);
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
                if (command.TargetBuilding >= 0)
                {
                    IssueOrder(
                        command.Units,
                        new UnitOrder(
                            UnitOrderKind.Move,
                            command.TargetPosition,
                            TargetBuilding: command.TargetBuilding),
                        command.Queued);
                }
                else
                {
                    IssueMove(
                        command.Units,
                        command.TargetPosition,
                        command.Queued);
                }
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
            case UnitOrderKind.AttackObject:
                IssueAttackObject(
                    command.Units,
                    new CombatObjectId(command.TargetResourceNode),
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
            case UnitOrderKind.CastAbility:
            {
                if (command.Units.Length != 1 ||
                    (uint)command.TargetResourceNode >=
                    (uint)Abilities.Catalog.Count)
                    throw new InvalidOperationException(
                        "Recorded ability command is invalid.");
                var caster = command.Units[0];
                var ability = Abilities.Catalog.Ability(
                    command.TargetResourceNode);
                var target = command.TargetUnit >= 0
                    ? AbilityCastTarget.Unit(
                        command.TargetUnit, command.TargetPosition)
                    : command.TargetBuilding >= 0
                        ? AbilityCastTarget.Building(
                            new GameplayBuildingId(command.TargetBuilding),
                            command.TargetPosition)
                        : ability.Activation is AbilityActivationKind.Instant or
                            AbilityActivationKind.Toggle
                            ? AbilityCastTarget.Self(
                                caster, Units.Positions[caster])
                            : AbilityCastTarget.Point(command.TargetPosition);
                var cast = IssueAbility(
                    Combat.Teams[caster], caster,
                    command.TargetResourceNode, target);
                if (!cast.Succeeded)
                    throw new InvalidOperationException(
                        $"Replay ability cast failed with {cast.Code}.");
                break;
            }
            case UnitOrderKind.LearnAbility:
            {
                if (command.Units.Length != 1 ||
                    (uint)command.TargetResourceNode >=
                    (uint)Abilities.Catalog.Count)
                    throw new InvalidOperationException(
                        "Recorded ability learn command is invalid.");
                var caster = command.Units[0];
                var learned = IssueLearnAbility(
                    Combat.Teams[caster], caster,
                    command.TargetResourceNode);
                if (!learned.Succeeded)
                    throw new InvalidOperationException(
                        $"Replay ability learn failed with {learned.Code}.");
                break;
            }
            case UnitOrderKind.SetAbilityAutoCast:
            {
                if (command.Units.Length != 1 ||
                    (uint)command.TargetResourceNode >=
                    (uint)Abilities.Catalog.Count ||
                    command.TargetUnit is not (0 or 1))
                    throw new InvalidOperationException(
                        "Recorded ability auto-cast command is invalid.");
                var caster = command.Units[0];
                var changed = IssueSetAbilityAutoCast(
                    Combat.Teams[caster], caster,
                    command.TargetResourceNode,
                    command.TargetUnit == 1);
                if (!changed.Succeeded)
                    throw new InvalidOperationException(
                        $"Replay auto-cast change failed with {changed.Code}.");
                break;
            }
            case UnitOrderKind.CastBuildingAbility:
            {
                if (command.Units.Length != 0 ||
                    (uint)command.TargetResourceNode >=
                    (uint)Abilities.Catalog.Count)
                    throw new InvalidOperationException(
                        "Recorded building ability command is invalid.");
                var building = new GameplayBuildingId(command.TargetBuilding);
                if (!Construction.IsAlive(building))
                    throw new InvalidOperationException(
                        "Recorded building ability caster is invalid.");
                var ability = Abilities.Catalog.Ability(
                    command.TargetResourceNode);
                var target = ability.Activation is
                        AbilityActivationKind.TargetPoint or
                        AbilityActivationKind.ChannelPoint
                    ? AbilityCastTarget.Point(command.TargetPosition)
                    : AbilityCastTarget.Building(
                        building, command.TargetPosition);
                var cast = IssueBuildingAbility(
                    Construction.Observe(building).PlayerId,
                    building,
                    command.TargetResourceNode,
                    target);
                if (!cast.Succeeded)
                    throw new InvalidOperationException(
                        $"Replay building ability cast failed with {cast.Code}.");
                break;
            }
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
            case ProductionReplayCommandKind.UpgradeBuilding:
                var upgrade = IssueBuildingUpgrade(
                    command.PlayerId,
                    command.Producer,
                    command.BuildingUpgrade);
                if (!upgrade.Succeeded)
                    throw new InvalidOperationException(
                        $"Replay building upgrade failed with {upgrade.Code}.");
                break;
            case ProductionReplayCommandKind.CancelBuildingUpgrade:
                if (!BuildingUpgrades.TryObserve(
                        command.Producer, out var activeUpgrade) ||
                    activeUpgrade.Id != command.BuildingUpgradeOrderId ||
                    !CancelBuildingUpgrade(
                        command.PlayerId,
                        command.BuildingUpgradeOrderId))
                    throw new InvalidOperationException(
                        "Replay building upgrade Cancel failed.");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported production command {command.Kind}.");
        }
    }

    public DynamicFootprintId PlaceBuilding(SimRect footprint)
    {
        if (_detailedProfilingEnabled && _issuingConstructionWorldMutation)
        {
            var phaseStart = Stopwatch.GetTimestamp();
            var profiledId = World.DynamicOccupancy.Place(footprint);
            var occupancyEnd = Stopwatch.GetTimestamp();
            NotifyGroupRouteNavigationChanged(footprint);
            var routeEnd = Stopwatch.GetTimestamp();
            InvalidatePathsIntersecting(footprint);
            var invalidationEnd = Stopwatch.GetTimestamp();
            Metrics.ConstructionOccupancyPlaceMilliseconds +=
                ElapsedMilliseconds(phaseStart, occupancyEnd);
            Metrics.ConstructionRouteTopologyMilliseconds +=
                ElapsedMilliseconds(occupancyEnd, routeEnd);
            Metrics.ConstructionPathInvalidationMilliseconds +=
                ElapsedMilliseconds(routeEnd, invalidationEnd);
            return profiledId;
        }

        var id = World.DynamicOccupancy.Place(footprint);
        NotifyGroupRouteNavigationChanged(footprint);
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
        ConstructionReservationId ignoreReservation,
        int ignoreUnit = -1)
    {
        var assessment = BuildingPlacementValidator.Evaluate(
            World,
            Units,
            footprint,
            rules,
            _buildingConnectivityGuard,
            includeDynamicFootprint: null,
            includeUnit: ignoreUnit >= 0
                ? unit => unit != ignoreUnit
                : null);
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

    /// <summary>
    /// Creates an already completed building after normal authoritative
    /// placement validation. Intended for map and item effects that do not
    /// assign a construction worker.
    /// </summary>
    public bool TryCreateCompletedBuilding(
        int playerId,
        BuildingTypeProfile profile,
        Vector2 center,
        out GameplayBuildingId buildingId)
    {
        buildingId = new GameplayBuildingId(-1);
        if (!Economy.Players.IsRegistered(playerId) ||
            !float.IsFinite(center.X) || !float.IsFinite(center.Y) ||
            profile.Id < 0 || profile.Size.X <= 0f || profile.Size.Y <= 0f ||
            profile.RequiresVespeneNode)
            return false;
        var halfSize = profile.Size * 0.5f;
        var bounds = new SimRect(center - halfSize, center + halfSize);
        var commit = TryCommitHardFootprint(
            bounds,
            new BuildingPlacementRules(
                profile.MinimumPassageClass,
                profile.PlacementProfile.UnitPadding));
        if (!commit.Succeeded) return false;

        buildingId = Construction.AddCompleted(
            playerId, profile, bounds, commit.FootprintId);
        if (profile.SupplyProvided > 0)
            Economy.Players.AddSupplyCapacity(playerId, profile.SupplyProvided);
        if (profile.Function == BuildingFunctionKind.TownHall)
            Economy.RegisterTownHall(playerId, buildingId, bounds);
        GameplayEvents.Publish(
            Metrics.Tick,
            GameplayEventKind.ConstructionStarted,
            building: buildingId.Value,
            worldPosition: center,
            player: playerId);
        GameplayEvents.Publish(
            Metrics.Tick,
            GameplayEventKind.ConstructionCompleted,
            building: buildingId.Value,
            value: 1f,
            worldPosition: center,
            player: playerId);
        return true;
    }

    public bool TryCreateReleasedBuilding(
        int playerId,
        BuildingTypeProfile profile,
        Vector2 center,
        out GameplayBuildingId buildingId)
    {
        buildingId = new GameplayBuildingId(-1);
        if (!Economy.Players.IsRegistered(playerId) ||
            !float.IsFinite(center.X) || !float.IsFinite(center.Y) ||
            profile.Id < 0 || profile.Size.X <= 0f || profile.Size.Y <= 0f ||
            profile.RequiresVespeneNode ||
            profile.ConstructionMethod != ConstructionMethodKind.StartAndRelease)
            return false;
        var halfSize = profile.Size * 0.5f;
        var bounds = new SimRect(center - halfSize, center + halfSize);
        var commit = TryCommitHardFootprint(
            bounds,
            new BuildingPlacementRules(
                profile.MinimumPassageClass,
                profile.PlacementProfile.UnitPadding));
        if (!commit.Succeeded) return false;
        buildingId = Construction.AddReleased(
            playerId, profile, bounds, commit.FootprintId);
        GameplayEvents.Publish(
            Metrics.Tick,
            GameplayEventKind.ConstructionStarted,
            building: buildingId.Value,
            worldPosition: center,
            player: playerId);
        return true;
    }

    private HardFootprintCommitResult TryCommitHardFootprint(
        SimRect footprint,
        BuildingPlacementRules rules,
        ConstructionReservationId ignoreReservation,
        int ignoreUnit = -1)
    {
        var evaluationSequence = _buildingConnectivityGuard.EvaluationSequence;
        var validationStart = _detailedProfilingEnabled
            ? Stopwatch.GetTimestamp()
            : 0L;
        var assessment = AssessBuildingPlacement(
            footprint, rules, ignoreReservation, ignoreUnit);
        if (_detailedProfilingEnabled)
        {
            Metrics.ConstructionCommitAttempts++;
            Metrics.ConstructionPlacementValidationMilliseconds +=
                ElapsedMilliseconds(validationStart);
            if (_buildingConnectivityGuard.EvaluationSequence !=
                evaluationSequence)
            {
                var connectivity =
                    _buildingConnectivityGuard.LastEvaluationProfile;
                Metrics.ConstructionConnectivityEvaluations++;
                Metrics.ConstructionConnectivityBaselineRebuilds +=
                    connectivity.BaselineRebuilt ? 1 : 0;
                Metrics.ConstructionConnectivityBaselineMilliseconds +=
                    connectivity.BaselineMilliseconds;
                Metrics.ConstructionConnectivityCandidateMilliseconds +=
                    connectivity.CandidateMilliseconds;
                Metrics.ConstructionConnectivityCompareMilliseconds +=
                    connectivity.CompareMilliseconds;
                Metrics.ConstructionConnectivityAllocatedBytes +=
                    connectivity.AllocatedBytes;
            }
        }
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
        if (_detailedProfilingEnabled)
            Metrics.ConstructionCommitSuccesses++;
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

        NotifyGroupRouteNavigationChanged(removedBounds);

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

    private void NotifyGroupRouteNavigationChanged(SimRect changedBounds)
    {
        if (_groupRoutePlanner is IGroupRouteNavigationChangeSink sink)
        {
            sink.OnNavigationChanged(changedBounds);
        }
    }

    private void EnsureUnitCapacityForSpawn()
    {
        if (Units.Count < Units.Capacity)
        {
            return;
        }

        var next = checked(Math.Max(Units.Capacity + 1, Units.Capacity * 2));
        ResizeUnitCapacity(next);
    }

    private void ResizeUnitCapacity(int capacity)
    {
        if (capacity <= Units.Capacity)
        {
            return;
        }

        Units.EnsureCapacity(capacity);
        Combat.EnsureCapacity(capacity);
        Abilities.EnsureCapacity(capacity);
        CommandQueues.EnsureCapacity(capacity);
        Economy.EnsureUnitCapacity(capacity);
        Array.Resize(ref _pathRequestQueued, capacity);
        Array.Resize(ref _orderReadyUnits, capacity);
        Array.Resize(ref _orderReadyOrders, capacity);
        Array.Resize(ref _orderReadyProcessed, capacity);
        Array.Resize(ref _orderDispatchUnits, capacity);
        Array.Resize(ref _displacedStationaryUnits, capacity);
        Array.Resize(ref _reservationTargetScratch, capacity);
        Array.Resize(ref _reservationSelectionScratch, capacity);
        Array.Resize(ref _combatContacts, capacity);
        Array.Resize(ref _unitCollisionSuppressed, capacity);
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
        UnitConcealmentCapabilitySnapshot concealmentCapability = default,
        float turnRateRadiansPerSecond =
            UnitFacing.LegacyTurnRateRadiansPerSecond,
        float facingRadians = 0f)
    {
        EnsureUnitCapacityForSpawn();
        var unit = Units.Add(
            position, radius, maxSpeed, acceleration,
            turnRateRadiansPerSecond, facingRadians);
        Combat.Register(
            unit, team, position, combatProfile, perception,
            concealmentCapability);
        Abilities.RegisterUnboundUnit(unit, Units, Combat);
        return unit;
    }

    public int AddUnit(
        Vector2 position,
        UnitTypeProfile type,
        int team)
    {
        var unit = AddUnit(
            position, type.Movement, team, type.Combat, type.Perception);
        Abilities.BindUnitType(unit, type.Id);
        return unit;
    }

    public int AddUnit(
        Vector2 position,
        UnitMovementProfileSnapshot profile) =>
        AddUnit(
            position,
            team: 0,
            combatProfile: CombatProfileSnapshot.Standard,
            radius: profile.PhysicalRadius,
            maxSpeed: profile.MaximumSpeed,
            acceleration: profile.Acceleration,
            turnRateRadiansPerSecond: profile.TurnRateRadiansPerSecond);

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
            concealmentCapability,
            movementProfile.TurnRateRadiansPerSecond);

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

        var approach = ResolveConstructionAccessPoint(
            bounds,
            builderUnit,
            default,
            refineryNode);
        if (!approach.Found)
        {
            Economy.Players.Refund(issuingPlayerId, profile.Cost, 1f);
            return new ConstructionCommandResult(
                ConstructionCommandCode.ApproachUnavailable, default);
        }
        Economy.Cancel([builderUnit]);
        ClearConstructionEvacuation(builderUnit);
        CommandQueues.ClearPending(builderUnit);
        CommandQueues.HasActiveOrders[builderUnit] = false;
        var accessPoint = approach.Target;
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
        var buildingSnapshot = Construction.Observe(buildingId);
        var approach = ResolveConstructionAccessPoint(
            buildingSnapshot.Bounds,
            builderUnit,
            buildingSnapshot.ReservationId,
            buildingSnapshot.RefineryNode);
        if (!approach.Found)
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
        var accessPoint = approach.Target;
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
            Abilities.HasStatus(unit, AbilityStatusFlags.Invulnerable) ||
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

    public AbilityCommandResult IssueAbility(
        int playerId,
        int caster,
        int abilityId,
        in AbilityCastTarget target)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new AbilityCommandResult(matchBlock switch
            {
                MatchCommandBlock.Completed => AbilityCommandCode.MatchCompleted,
                MatchCommandBlock.NotParticipant => AbilityCommandCode.NotParticipant,
                _ => AbilityCommandCode.PlayerDefeated
            }, caster, abilityId);
        }
        var result = Abilities.Issue(
            playerId, caster, abilityId, target, Metrics.Tick,
            this, AbilityEvents);
        if (result.Succeeded)
        {
            Span<int> unit = stackalloc int[1];
            unit[0] = caster;
            _commandRecorder?.Record(
                Metrics.Tick,
                unit,
                new UnitOrder(
                    UnitOrderKind.CastAbility,
                    target.Position,
                    target.Kind == AbilityTargetKind.Unit ? target.Id : -1,
                    target.Kind == AbilityTargetKind.Building ? target.Id : -1,
                    abilityId),
                queued: false);
        }
        return result;
    }

    public AbilityCommandResult PreviewAbility(
        int playerId,
        int caster,
        int abilityId,
        in AbilityCastTarget target)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new AbilityCommandResult(matchBlock switch
            {
                MatchCommandBlock.Completed => AbilityCommandCode.MatchCompleted,
                MatchCommandBlock.NotParticipant => AbilityCommandCode.NotParticipant,
                _ => AbilityCommandCode.PlayerDefeated
            }, caster, abilityId);
        }
        return Abilities.Preview(playerId, caster, abilityId, target, this);
    }

    public AbilityCommandResult IssueBuildingAbility(
        int playerId,
        GameplayBuildingId caster,
        int abilityId)
    {
        if (!Construction.IsAlive(caster))
            return new AbilityCommandResult(
                AbilityCommandCode.InvalidCaster,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        var building = Construction.Observe(caster);
        var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
        return IssueBuildingAbility(
            playerId, caster, abilityId,
            AbilityCastTarget.Building(caster, center));
    }

    public AbilityCommandResult PreviewBuildingAbility(
        int playerId,
        GameplayBuildingId caster,
        int abilityId,
        in AbilityCastTarget target)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
            return new AbilityCommandResult(matchBlock switch
            {
                MatchCommandBlock.Completed => AbilityCommandCode.MatchCompleted,
                MatchCommandBlock.NotParticipant => AbilityCommandCode.NotParticipant,
                _ => AbilityCommandCode.PlayerDefeated
            }, AbilityId: abilityId, CasterBuilding: caster.Value);
        if (!Construction.IsAlive(caster))
            return new AbilityCommandResult(
                AbilityCommandCode.InvalidCaster,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        if (BuildingUpgrades.IsUpgrading(caster))
            return new AbilityCommandResult(
                AbilityCommandCode.CasterDisabled,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        var building = Construction.Observe(caster);
        return Abilities.PreviewBuilding(
            playerId, caster, building.Type.Id, abilityId, target, this);
    }

    public AbilityCommandResult IssueBuildingAbility(
        int playerId,
        GameplayBuildingId caster,
        int abilityId,
        in AbilityCastTarget target)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new AbilityCommandResult(matchBlock switch
            {
                MatchCommandBlock.Completed => AbilityCommandCode.MatchCompleted,
                MatchCommandBlock.NotParticipant => AbilityCommandCode.NotParticipant,
                _ => AbilityCommandCode.PlayerDefeated
            }, AbilityId: abilityId, CasterBuilding: caster.Value);
        }
        if (!Construction.IsAlive(caster))
            return new AbilityCommandResult(
                AbilityCommandCode.InvalidCaster,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        if (BuildingUpgrades.IsUpgrading(caster))
            return new AbilityCommandResult(
                AbilityCommandCode.CasterDisabled,
                AbilityId: abilityId,
                CasterBuilding: caster.Value);
        var building = Construction.Observe(caster);
        var result = Abilities.IssueBuilding(
            playerId, caster, building.Type.Id, abilityId, target, Metrics.Tick,
            this, AbilityEvents);
        if (result.Succeeded)
        {
            _commandRecorder?.Record(
                Metrics.Tick,
                ReadOnlySpan<int>.Empty,
                new UnitOrder(
                    UnitOrderKind.CastBuildingAbility,
                    target.Position,
                    TargetBuilding: caster.Value,
                    TargetResourceNode: abilityId),
                queued: false);
        }
        return result;
    }

    public AbilityCommandResult IssueLearnAbility(
        int playerId,
        int caster,
        int abilityId)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new AbilityCommandResult(matchBlock switch
            {
                MatchCommandBlock.Completed => AbilityCommandCode.MatchCompleted,
                MatchCommandBlock.NotParticipant => AbilityCommandCode.NotParticipant,
                _ => AbilityCommandCode.PlayerDefeated
            }, caster, abilityId);
        }
        var result = Abilities.Learn(playerId, caster, abilityId, this);
        if (result.Succeeded)
        {
            Span<int> unit = stackalloc int[1];
            unit[0] = caster;
            _commandRecorder?.Record(
                Metrics.Tick,
                unit,
                new UnitOrder(
                    UnitOrderKind.LearnAbility,
                    Vector2.Zero,
                    TargetResourceNode: abilityId),
                queued: false);
        }
        return result;
    }

    public AbilityCommandResult IssueSetAbilityAutoCast(
        int playerId,
        int caster,
        int abilityId,
        bool enabled)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new AbilityCommandResult(matchBlock switch
            {
                MatchCommandBlock.Completed => AbilityCommandCode.MatchCompleted,
                MatchCommandBlock.NotParticipant => AbilityCommandCode.NotParticipant,
                _ => AbilityCommandCode.PlayerDefeated
            }, caster, abilityId);
        }
        var result = Abilities.SetAutoCast(
            playerId, caster, abilityId, enabled, this);
        if (result.Succeeded)
        {
            Span<int> unit = stackalloc int[1];
            unit[0] = caster;
            _commandRecorder?.Record(
                Metrics.Tick,
                unit,
                new UnitOrder(
                    UnitOrderKind.SetAbilityAutoCast,
                    Vector2.Zero,
                    TargetUnit: enabled ? 1 : 0,
                    TargetResourceNode: abilityId),
                queued: false);
        }
        return result;
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
            playerId, producer, recipe, Construction, Economy.Players,
            BuildingUpgrades.IsUpgrading,
            BuildingUpgrades.SatisfiesBuildingType);
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
        {
            _productionCommandRecorder?.RecordRally(
                Metrics.Tick, playerId, producer, rally);
            GameplayEvents.Publish(
                Metrics.Tick,
                GameplayEventKind.ProductionRallyChanged,
                building: producer.Value,
                worldPosition: rally.Position,
                player: playerId);
        }
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
            playerId, researcher, technology, Construction, Economy.Players,
            BuildingUpgrades.IsUpgrading,
            BuildingUpgrades.SatisfiesBuildingType);
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

    public BuildingUpgradeCommandResult IssueBuildingUpgrade(
        int playerId,
        GameplayBuildingId building,
        BuildingUpgradeProfile profile)
    {
        var matchBlock = MatchCommandBlockFor(playerId);
        if (matchBlock != MatchCommandBlock.None)
        {
            return new BuildingUpgradeCommandResult(
                matchBlock switch
                {
                    MatchCommandBlock.Completed =>
                        BuildingUpgradeCommandCode.MatchCompleted,
                    MatchCommandBlock.NotParticipant =>
                        BuildingUpgradeCommandCode.NotParticipant,
                    _ => BuildingUpgradeCommandCode.PlayerDefeated
                }, default);
        }
        var result = BuildingUpgrades.Enqueue(
            playerId,
            building,
            profile,
            Construction,
            Economy.Players,
            IsBuildingProductionOrResearchBusy,
            technologyId => Technology.Level(playerId, technologyId));
        if (!result.Succeeded) return result;
        _productionCommandRecorder?.RecordBuildingUpgrade(
            Metrics.Tick, playerId, building, profile);
        var source = Construction.Observe(building);
        GameplayEvents.Publish(
            Metrics.Tick,
            GameplayEventKind.BuildingUpgradeStarted,
            building: building.Value,
            value: profile.TargetType.Id,
            worldPosition: (source.Bounds.Min + source.Bounds.Max) * 0.5f,
            player: playerId);
        return result;
    }

    public bool CancelBuildingUpgrade(
        int playerId,
        BuildingUpgradeOrderId orderId)
    {
        if (MatchCommandBlockFor(playerId) != MatchCommandBlock.None)
            return false;
        if (!BuildingUpgrades.Cancel(
                playerId, orderId, Economy.Players, out var building))
            return false;
        _productionCommandRecorder?.RecordCancelBuildingUpgrade(
            Metrics.Tick, playerId, building, orderId);
        if (Construction.IsAlive(building))
        {
            var source = Construction.Observe(building);
            GameplayEvents.Publish(
                Metrics.Tick,
                GameplayEventKind.BuildingUpgradeCanceled,
                building: building.Value,
                worldPosition:
                    (source.Bounds.Min + source.Bounds.Max) * 0.5f,
                player: playerId);
        }
        return true;
    }

    private bool IsBuildingProductionOrResearchBusy(GameplayBuildingId building) =>
        Production.Observe(building).Orders.Length > 0 ||
        Technology.Observe(building).Orders.Length > 0;

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

    public PlayerOrderCommandResult IssuePlayerAttackObject(
        int playerId,
        ReadOnlySpan<int> units,
        CombatObjectId target,
        bool queued = false)
    {
        var validation = ValidatePlayerSelection(playerId, units);
        if (!validation.Succeeded) return validation;
        if (!UnitsAllowAttack(units))
            return new(PlayerOrderCommandCode.ContextActionUnavailable);
        if (!CombatObjects.IsAlive(target))
            return new(PlayerOrderCommandCode.InvalidTarget);
        var value = CombatObjects.Observe(target);
        if (value.Profile.OwnerTeam >= 0 &&
            !Diplomacy.IsEnemy(playerId, value.Profile.OwnerTeam))
            return new(PlayerOrderCommandCode.FriendlyTarget);
        if (!Visibility.IsVisible(playerId, value.Position))
            return new(PlayerOrderCommandCode.TargetNotVisible);
        IssueAttackObject(units, target, queued);
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
            var approach = ConstructionAccessPointResolver.ResolveInteraction(
                World,
                _pathProvider,
                building.Bounds,
                Units.Positions[unit],
                Units.Radii[unit],
                Units.NavigationRadii[unit],
                interactionPadding: 0f);
            if (!approach.Found)
            {
                SetImmediateMovementFailure(
                    unit,
                    UnitMovementGoalKind.BuildingBoundary,
                    building.Bounds,
                    building.Id.Value,
                    UnitMovementLegResult.Unreachable);
                continue;
            }
            IssueOrder(
                single,
                new UnitOrder(
                    UnitOrderKind.Move,
                    approach.Target,
                    TargetBuilding: building.Id.Value),
                queued);
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
            if (!Concealment.CanMove(units[index]) ||
                !Abilities.CanMove(units[index]))
                return false;
        }
        return true;
    }

    private bool UnitsAllowAttack(ReadOnlySpan<int> units)
    {
        for (var index = 0; index < units.Length; index++)
        {
            if (!Concealment.CanAttack(units[index]) ||
                !Abilities.CanAttack(units[index]))
                return false;
        }
        return true;
    }

    private bool CanUnitAttack(int unit)
    {
        if (!Concealment.CanAttack(unit) || !Abilities.CanAttack(unit) ||
            Construction.SuppressesBuilderCombat(unit))
            return false;
        if (!Economy.IsGatherer(unit))
            return true;
        return Economy.Worker(unit).State is
            WorkerEconomyState.None or WorkerEconomyState.Idle;
    }

    private bool CanUnitTakeDamage(int unit) =>
        !Abilities.HasStatus(
            unit, AbilityStatusFlags.Invulnerable |
                  AbilityStatusFlags.Banished);

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

    public void IssueAttackObject(
        ReadOnlySpan<int> unitIndices,
        CombatObjectId targetObject,
        bool queued = false)
    {
        if (!CombatObjects.IsAlive(targetObject))
            throw new ArgumentOutOfRangeException(nameof(targetObject));
        var target = CombatObjects.Observe(targetObject);
        for (var index = 0; index < unitIndices.Length; index++)
        {
            ValidateUnitIndex(unitIndices[index]);
            ValidateLivingUnit(unitIndices[index]);
            if (target.Profile.OwnerTeam >= 0 &&
                !Diplomacy.IsEnemy(
                    Combat.Teams[unitIndices[index]],
                    target.Profile.OwnerTeam))
            {
                throw new InvalidOperationException(
                    $"Combat object {targetObject.Value} is friendly to " +
                    $"attacker {unitIndices[index]}.");
            }
        }
        IssueOrder(
            unitIndices,
            new UnitOrder(
                UnitOrderKind.AttackObject,
                target.Position,
                TargetResourceNode: targetObject.Value),
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
        bool exactDestination = false,
        UnitMovementGoalKind goalKind = UnitMovementGoalKind.GroundPoint,
        SimRect goalBounds = default,
        float goalRadius = 0f,
        int goalTargetId = -1)
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
            SetMovementGoal(
                unit, goalKind, goalBounds, goalRadius, goalTargetId,
                UnitMovementLegResult.InProgress);
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
            if (goalKind == UnitMovementGoalKind.GroundPoint &&
                unitIndices.Length > PathBudgetPerTick &&
                TryStartDirectGroupPath(unit, assignedTarget))
                continue;
            EnqueuePathRequest(unit);
        }
    }

    private bool TryStartDirectGroupPath(int unit, Vector2 target)
    {
        var start = Units.Positions[unit];
        var navigationRadius = Units.NavigationRadii[unit];
        if (!World.IsDiscFree(start, navigationRadius) ||
            !World.IsDiscFree(target, navigationRadius) ||
            !World.IsSegmentFree(start, target, navigationRadius))
            return false;

        Units.Paths[unit] = new UnitPath(
            [start, target], Units.CommandVersions[unit]);
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Moving;
        return true;
    }

    private void ExecuteBuildingBoundaryMove(
        ReadOnlySpan<int> unitIndices,
        int targetBuilding)
    {
        var buildingId = new GameplayBuildingId(targetBuilding);
        if (!Construction.IsAlive(buildingId))
        {
            for (var index = 0; index < unitIndices.Length; index++)
            {
                SetImmediateMovementFailure(
                    unitIndices[index],
                    UnitMovementGoalKind.BuildingBoundary,
                    default,
                    targetBuilding,
                    UnitMovementLegResult.TargetInvalidated);
            }
            return;
        }

        var building = Construction.Observe(buildingId);
        Span<int> single = stackalloc int[1];
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            var approach = ConstructionAccessPointResolver.ResolveInteraction(
                World,
                _pathProvider,
                building.Bounds,
                Units.Positions[unit],
                Units.Radii[unit],
                Units.NavigationRadii[unit],
                interactionPadding: 0f);
            if (!approach.Found)
            {
                SetImmediateMovementFailure(
                    unit,
                    UnitMovementGoalKind.BuildingBoundary,
                    building.Bounds,
                    targetBuilding,
                    UnitMovementLegResult.Unreachable);
                continue;
            }

            single[0] = unit;
            ExecuteMoveOrder(
                single,
                approach.Target,
                UnitCommandIntent.Move,
                exactDestination: true,
                goalKind: UnitMovementGoalKind.BuildingBoundary,
                goalBounds: building.Bounds,
                goalTargetId: targetBuilding);
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
            if (_issuingSystemOrder)
                Units.MovementLegResults[unit] = UnitMovementLegResult.Canceled;
            else
                FinishMovementLeg(unit, UnitMovementLegResult.Canceled);
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
            AbilityEvents.Publish(
                Metrics.Tick,
                AbilityEventKind.Started,
                ConcealmentAbilityId(activate),
                unit,
                AbilityTargetKind.Self,
                unit,
                Units.Positions[unit]);
        }
    }

    private void OnConcealmentTransitionCompleted(int unit, bool activated)
    {
        var abilityId = ConcealmentAbilityId(activated);
        var position = Units.Positions[unit];
        AbilityEvents.Publish(
            Metrics.Tick,
            AbilityEventKind.Impact,
            abilityId,
            unit,
            AbilityTargetKind.Self,
            unit,
            position);
        AbilityEvents.Publish(
            Metrics.Tick,
            AbilityEventKind.Ended,
            abilityId,
            unit,
            AbilityTargetKind.Self,
            unit,
            position,
            AbilityEndReason.Completed);
    }

    private static string ConcealmentAbilityId(bool activate) =>
        activate ? ActiveConcealmentAbilityId : ActiveRevealAbilityId;

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

    private void FreezeAbilityRestrictedUnits()
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] || Abilities.CanMove(unit)) continue;
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

        // A repeated non-queued AttackMove to the exact same command point is
        // an input refresh, not a replacement movement leg.  Replacing it used
        // to cancel combat/abilities, discard formation slots and paths, bump
        // command versions and put the whole selection back into the path
        // budget.  Large selections could therefore be kept permanently in
        // WaitingForPath by an otherwise harmless command repeat.
        //
        // Only coalesce when every selected unit still owns a valid execution
        // of that command. A failed or prematurely settled member makes the
        // group command a real retry; queued orders and different destinations
        // retain normal replacement semantics.
        if (!_issuingSystemOrder &&
            !queued &&
            order.Kind == UnitOrderKind.AttackMove &&
            CanCoalesceRepeatedAttackMove(unitIndices, order.TargetPosition))
        {
            _commandRecorder?.Record(
                Metrics.Tick, unitIndices, order, queued: false);
            for (var index = 0; index < unitIndices.Length; index++)
                CommandQueues.ClearPending(unitIndices[index]);
            Metrics.RepeatedAttackMoveUnitsCoalesced += unitIndices.Length;
            return;
        }

        for (var index = 0; index < unitIndices.Length; index++)
        {
            if (!_issuingSystemOrder)
                Abilities.CancelCaster(
                    unitIndices[index], Metrics.Tick,
                    AbilityEndReason.Canceled, this, AbilityEvents);
        }

        if (!_issuingSystemOrder)
        {
            for (var index = 0; index < unitIndices.Length; index++)
                ClearProductionEvacuation(unitIndices[index]);
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

    private bool CanCoalesceRepeatedAttackMove(
        ReadOnlySpan<int> unitIndices,
        Vector2 target)
    {
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            if (!CommandQueues.HasActiveOrders[unit] ||
                CommandQueues.ActiveKinds[unit] != UnitOrderKind.AttackMove ||
                CommandQueues.ActivePositions[unit] != target ||
                Combat.CommandIntents[unit] != UnitCommandIntent.AttackMove ||
                Units.MovementLegResults[unit] is
                    UnitMovementLegResult.Unreachable or
                    UnitMovementLegResult.TargetInvalidated or
                    UnitMovementLegResult.Canceled ||
                MovementLegFinished(unit) &&
                Combat.TargetKinds[unit] == CombatTargetKind.None &&
                Vector2.DistanceSquared(
                    Units.Positions[unit], Units.SlotTargets[unit]) >
                ArrivalStopDistance(unit) * ArrivalStopDistance(unit))
            {
                return false;
            }
        }

        return true;
    }

    private void ExecuteOrderGroup(
        ReadOnlySpan<int> unitIndices,
        UnitOrder order,
        bool wasQueued)
    {
        switch (order.Kind)
        {
            case UnitOrderKind.Move:
                if (order.TargetBuilding >= 0)
                {
                    ExecuteBuildingBoundaryMove(
                        unitIndices, order.TargetBuilding);
                }
                else
                {
                    ExecuteMoveOrder(
                        unitIndices,
                        order.TargetPosition,
                        UnitCommandIntent.Move);
                }
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
            case UnitOrderKind.AttackObject:
                ExecuteAttackObject(unitIndices, order.TargetResourceNode);
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
            case UnitOrderKind.FollowFriendly:
                Span<int> follower = stackalloc int[1];
                for (var index = 0; index < unitIndices.Length; index++)
                {
                    var unit = unitIndices[index];
                    if ((uint)order.TargetUnit < (uint)Units.Count &&
                        Units.Alive[order.TargetUnit])
                    {
                        SetFollowDestination(unit, order.TargetUnit);
                    }
                    else
                    {
                        follower[0] = unit;
                        ExecuteStop(follower);
                    }
                }
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

    private void ExecuteAttackObject(
        ReadOnlySpan<int> unitIndices,
        int targetObject)
    {
        ExecuteStop(unitIndices);
        var objectId = new CombatObjectId(targetObject);
        if (!CombatObjects.IsAlive(objectId)) return;
        var target = CombatObjects.Observe(objectId);
        for (var index = 0; index < unitIndices.Length; index++)
        {
            var unit = unitIndices[index];
            if (target.Profile.OwnerTeam >= 0 &&
                !Diplomacy.IsEnemy(
                    Combat.Teams[unit], target.Profile.OwnerTeam))
            {
                throw new InvalidOperationException(
                    $"Combat object {targetObject} is friendly to attacker " +
                    $"{unit}.");
            }
            Combat.SetCommand(
                unit,
                UnitCommandIntent.AttackTarget,
                target.Position,
                targetObject: targetObject);
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
        Metrics.PathRequestsProcessed = 0;
        Metrics.PathWorkUnits = 0;
        Metrics.PathBudgetDeferrals = 0;
        Metrics.PathSlowestRequestMilliseconds = 0d;
        Metrics.PathSlowestRequestUnit = -1;
        Metrics.PathSlowestRequestCommandVersion = 0;
        Metrics.PathSlowestRequestDistance = 0f;
        Metrics.PathSlowestRequestNavigationRadius = 0f;
        Metrics.PathSlowestRequestRouteWaypoints = 0;
        Metrics.PathSlowestRequestExpandedNodes = 0;
        Metrics.PathNormalizationMilliseconds = 0d;
        Metrics.PathStartEscapeMilliseconds = 0d;
        Metrics.PathEdgeCacheInvalidatedStates = 0;
        Metrics.PathEdgeCacheFullClears = 0;
        Metrics.PathCompletedCacheHits = 0;
        Metrics.CollisionPairs = 0;
        Metrics.CollisionBroadphasePairs = 0;
        Metrics.CollisionMainIterations = 0;
        Metrics.CollisionResidualPasses = 0;
        Metrics.CollisionConstraintCalls = 0;
        Metrics.CollisionResidualPairChecks = 0;
        Metrics.CollisionResidualPairMoves = 0;
        Metrics.CollisionVelocityProjections = 0;
        Metrics.WorldVelocityProjections = 0;
        Metrics.WorldConstraintCalls = 0;
        Metrics.DynamicFootprintCandidateChecks = 0;
        World.DynamicOccupancy.ResetConstraintDiagnostics();
        Metrics.MaximumPenetration = 0f;
        Metrics.RepathRequests = 0;
        Metrics.NavigationRevision = World.NavigationRevision;
        Metrics.NavigationInvalidations = _pendingNavigationInvalidations;
        Metrics.RecoveryEvents = 0;
        Metrics.ConstructionCommitAttempts = 0;
        Metrics.ConstructionCommitSuccesses = 0;
        Metrics.ConstructionPlacementValidationMilliseconds = 0d;
        Metrics.ConstructionConnectivityBaselineMilliseconds = 0d;
        Metrics.ConstructionConnectivityCandidateMilliseconds = 0d;
        Metrics.ConstructionConnectivityCompareMilliseconds = 0d;
        Metrics.ConstructionConnectivityAllocatedBytes = 0;
        Metrics.ConstructionConnectivityEvaluations = 0;
        Metrics.ConstructionConnectivityBaselineRebuilds = 0;
        Metrics.ConstructionOccupancyPlaceMilliseconds = 0d;
        Metrics.ConstructionRouteTopologyMilliseconds = 0d;
        Metrics.ConstructionPathInvalidationMilliseconds = 0d;
        _pendingNavigationInvalidations = 0;

        var phaseStart = Stopwatch.GetTimestamp();
        var lifecycleStart = phaseStart;
        var phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        UpdateConstructionEvacuations();
        Construction.Update(
            delta,
            Metrics.Tick,
            GameplayEvents,
            Units,
            Economy,
            _constructionMoveWorker,
            _constructionStopWorker,
            _constructionCommitFootprint,
            EvacuateConstructionStartOccupant);
        Metrics.ConstructionMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.ConstructionAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;

        BuildingUpgrades.Update(
            delta,
            Metrics.Tick,
            GameplayEvents,
            Construction,
            Economy);

        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        UpdateProductionEvacuations();
        Production.Update(
            delta,
            Metrics.Tick,
            GameplayEvents,
            Construction,
            Economy.Players,
            Units,
            Combat,
            World,
            _productionSpawnUnit,
            _productionApplyRally,
            ProductionExitPathCost,
            _productionEvacuateExitBlocker);
        Metrics.ProductionMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.ProductionAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;

        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        Technology.Update(
            delta,
            Metrics.Tick,
            GameplayEvents,
            Construction,
            Economy.Players);
        Metrics.TechnologyMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.TechnologyAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;

        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        Economy.Update(
            delta,
            Metrics.Tick,
            GameplayEvents,
            Units,
            _economyMoveWorker,
            _economyStopWorker,
            _economyFinishDropOffMovement);
        SyncLinkedCombatObjects();
        Metrics.EconomySystemMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.EconomySystemAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;

        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        UpdateProducedUnitRallyFollowers();
        Concealment.Update(delta, _concealmentTransitionCompleted);
        Abilities.Update(
            delta, Metrics.Tick, this, Units, Combat, AbilityEvents);
        FreezeConcealmentRestrictedUnits();
        FreezeAbilityRestrictedUnits();
        for (var unit = 0; unit < Units.Count; unit++)
            _unitCollisionSuppressed[unit] =
                Economy.SuppressesUnitCollision(unit) ||
                Construction.SuppressesBuilderUnitCollision(unit);
        Metrics.LifecycleFinalizeMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.LifecycleFinalizeAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;
        Metrics.EconomyMilliseconds = ElapsedMilliseconds(lifecycleStart);

        // Combat acquisition uses the same deterministic unit broadphase as
        // steering. Refresh it after lifecycle/spawn work so target searches
        // inspect nearby cells instead of scanning every unit in the match.
        _spatialHash.Rebuild(Units);

        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var detectionStart = phaseStart;
        // A freshly prepared scenario can receive orders before its first tick.
        // Seed the complete visibility state before combat authority is checked;
        // later ticks reuse the full state produced at the previous tick end and
        // only refresh detection here.
        if (!Visibility.HasExploredState)
            Visibility.Update(Units, Combat, Construction);
        else
            Visibility.UpdateDetection(Units, Combat, Construction);
        Abilities.ApplyVisibilitySources(Visibility);
        Metrics.VisibilityDetectionMilliseconds =
            ElapsedMilliseconds(detectionStart);
        UpdateUnitFacings(delta);
        _combatSystem.Update(delta, Metrics.Tick);
        BuildingCombat.Update(delta, Metrics.Tick);
        Abilities.ProcessCombatEvents(
            Metrics.Tick, CombatEvents, this, AbilityEvents);
        Metrics.CombatMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.CombatAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;
        if (_detailedProfilingEnabled)
        {
            var combatProfile = _combatSystem.LastUpdateProfile;
            Metrics.CombatProjectileMilliseconds =
                combatProfile.ProjectileMilliseconds;
            Metrics.CombatUnitLoopMilliseconds =
                combatProfile.UnitLoopMilliseconds;
            Metrics.CombatTargetSearchMilliseconds =
                combatProfile.TargetSearchMilliseconds;
            Metrics.CombatTargetSearches = combatProfile.TargetSearches;
        }

        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        if (_pathProvider is GridPathProvider pathDiagnostics)
            pathDiagnostics.ResetPathDiagnostics();
        ProcessPathRequests();
        Metrics.PathMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.PathAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;
        if (_pathProvider is GridPathProvider gridPathProvider)
        {
            Metrics.PathFullConnectivityRebuilds =
                gridPathProvider.FullConnectivityRebuilds;
            Metrics.PathIncrementalConnectivityUpdates =
                gridPathProvider.IncrementalConnectivityUpdates;
            Metrics.PathConnectivityRefreshMilliseconds =
                gridPathProvider.LastConnectivityRefreshMilliseconds;
            Metrics.PathDirectCheckMilliseconds =
                gridPathProvider.LastDirectCheckMilliseconds;
            Metrics.PathSearchMilliseconds =
                gridPathProvider.LastSearchMilliseconds;
            Metrics.PathSimplificationMilliseconds =
                gridPathProvider.LastSimplificationMilliseconds;
            Metrics.PathExpandedNodes = gridPathProvider.LastExpandedNodes;
            Metrics.PathRawPoints = gridPathProvider.LastRawPathPoints;
            Metrics.PathSimplifiedPoints =
                gridPathProvider.LastSimplifiedPathPoints;
            Metrics.PathEdgeCacheInvalidatedStates =
                gridPathProvider.LastEdgeCacheInvalidatedStates;
            Metrics.PathEdgeCacheFullClears =
                gridPathProvider.LastEdgeCacheFullClears;
            Metrics.PathCompletedCacheHits =
                gridPathProvider.LastCompletedPathCacheHits;
        }

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
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        UpdateCombatContacts();
        _steeringSolver.Solve(
            Units, delta, _unitCollisionSuppressed, Combat.ConcealmentKinds,
            _combatContacts, Combat.TargetKinds);
        Metrics.SteeringNeighborPairs = _steeringSolver.LastNeighborPairs;
        Metrics.SteeringCandidateEvaluations =
            _steeringSolver.LastCandidateEvaluations;
        Metrics.SteeringMovingUnits = _steeringSolver.LastMovingUnits;
        Metrics.SteeringPreferredFastPaths =
            _steeringSolver.LastPreferredFastPaths;
        Metrics.SteeringAvoidingUnits = _steeringSolver.LastAvoidingUnits;
        Metrics.SteeringWorldSegmentProbes =
            _steeringSolver.LastWorldSegmentProbes;
        Metrics.SteeringCollisionRiskNeighborChecks =
            _steeringSolver.LastCollisionRiskNeighborChecks;
        Metrics.SteeringPredictedCollisionHits =
            _steeringSolver.LastPredictedCollisionHits;
        Metrics.SteeringOverlappingNeighborHits =
            _steeringSolver.LastOverlappingNeighborHits;
        _chokeController?.ConstrainSolvedVelocities(Units);
        Metrics.SteeringMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.SteeringAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;

        phaseStart = Stopwatch.GetTimestamp();
        Integrate(delta);
        Metrics.IntegrateMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        SolveCollisions();
        _chokeController?.ConstrainPositions(Units);
        AdoptDisplacedStationaryReservations();
        Metrics.CollisionMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.WorldConstraintCalls =
            World.DynamicOccupancy.ConstraintCalls;
        Metrics.DynamicFootprintCandidateChecks =
            World.DynamicOccupancy.ConstraintCandidateChecks;

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
        RecoverFailedExplicitBuildingAttackChases();
        UpdateMetrics();
        Metrics.RecoveryMilliseconds = ElapsedMilliseconds(phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        AdvanceQueuedOrders();
        Metrics.QueueMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.QueueAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;
        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        Visibility.Update(Units, Combat, Construction);
        Abilities.ApplyVisibilitySources(Visibility);
        Metrics.VisibilityMilliseconds = ElapsedMilliseconds(phaseStart);
        if (_detailedProfilingEnabled)
        {
            var visibilityProfile = Visibility.LastUpdateProfile;
            Metrics.VisibilityClearMilliseconds =
                visibilityProfile.ClearMilliseconds;
            Metrics.VisibilityUnitVisionMilliseconds =
                visibilityProfile.UnitVisionMilliseconds;
            Metrics.VisibilityBuildingVisionMilliseconds =
                visibilityProfile.BuildingVisionMilliseconds;
            Metrics.VisibilityUnitCacheHits =
                visibilityProfile.UnitCacheHits;
            Metrics.VisibilityUnitCacheRebuilds =
                visibilityProfile.UnitCacheRebuilds;
            Metrics.VisibilityCandidateCells =
                visibilityProfile.CandidateCells;
        }
        Metrics.VisibilityAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;
        phaseStart = Stopwatch.GetTimestamp();
        phaseAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        Match.Update(Metrics.Tick, Construction, Diplomacy);
        Metrics.MatchMilliseconds = ElapsedMilliseconds(phaseStart);
        Metrics.MatchAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - phaseAllocationStart;
        Metrics.CommandMilliseconds =
            Metrics.QueueMilliseconds + Metrics.VisibilityMilliseconds +
            Metrics.MatchMilliseconds;
        Metrics.TotalMilliseconds = Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
        Metrics.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
    }

    private int SpawnProducedUnit(
        UnitTypeProfile type,
        int playerId,
        Vector2 position)
    {
        var unit = AddUnit(position, type, playerId);
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
                    SetFollowDestination(unit, rally.Unit);
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
        Span<int> follower = stackalloc int[1];
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
                var allowed = Units.Radii[unit] + Units.Radii[target] +
                              InteractionGeometry.NumericTolerance(
                                  Units.Positions[unit],
                                  new SimRect(position, position));
                if (Vector2.DistanceSquared(
                        Units.Positions[unit], position) <= allowed * allowed)
                {
                    if (Units.Modes[unit] is not
                            (UnitMoveMode.Idle or UnitMoveMode.Hold) ||
                        Units.MovementLegResults[unit] !=
                            UnitMovementLegResult.Reached)
                    {
                        follower[0] = unit;
                        ExecuteStop(follower);
                        SetMovementGoal(
                            unit,
                            UnitMovementGoalKind.FollowRange,
                            default,
                            allowed,
                            target,
                            UnitMovementLegResult.Reached);
                    }
                    continue;
                }
                if (Units.MovementGoalKinds[unit] !=
                        UnitMovementGoalKind.FollowRange ||
                    Units.MovementGoalTargetIds[unit] != target ||
                    Units.MovementLegResults[unit] !=
                        UnitMovementLegResult.InProgress ||
                    Vector2.DistanceSquared(
                        Units.MoveGoals[unit], position) >
                        allowed * allowed)
                    SetFollowDestination(unit, target);
                continue;
            }

            var lastPosition = CommandQueues.ActivePositions[unit];
            CommandQueues.ActiveKinds[unit] = UnitOrderKind.Move;
            CommandQueues.ActiveTargetUnits[unit] = -1;
            FinishMovementLeg(
                unit, UnitMovementLegResult.TargetInvalidated);
            if (Vector2.DistanceSquared(
                    Units.MoveGoals[unit], lastPosition) > 0.01f)
            {
                follower[0] = unit;
                ExecuteMoveOrder(
                    follower,
                    lastPosition,
                    UnitCommandIntent.Move,
                    exactDestination: true);
            }
        }
    }

    private void SetFollowDestination(int unit, int targetUnit)
    {
        var approach = ConstructionAccessPointResolver.ResolveCircle(
            World,
            _pathProvider,
            Units.Positions[targetUnit],
            Units.Radii[targetUnit],
            Units.Positions[unit],
            Units.Radii[unit],
            Units.NavigationRadii[unit]);
        if (!approach.Found)
        {
            SetImmediateMovementFailure(
                unit,
                UnitMovementGoalKind.FollowRange,
                default,
                targetUnit,
                UnitMovementLegResult.Unreachable);
            return;
        }
        Combat.SetCommand(
            unit, UnitCommandIntent.Move, Units.Positions[targetUnit]);
        SetCombatDestination(unit, approach.Target);
        SetMovementGoal(
            unit,
            UnitMovementGoalKind.FollowRange,
            default,
            Units.Radii[unit] + Units.Radii[targetUnit],
            targetUnit,
            UnitMovementLegResult.InProgress);
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
        var exitTolerance = InteractionGeometry.NumericTolerance(
            exit, new SimRect(exit, exit));
        if (CommandQueues.ProductionEvacuationActive[blockerUnit] &&
            Vector2.DistanceSquared(
                CommandQueues.ProductionEvacuationTargets[blockerUnit],
                exit) <= exitTolerance * exitTolerance)
        {
            return true;
        }
        if (!CommandQueues.ProductionEvacuationActive[blockerUnit])
        {
            GameplayEvents.Publish(
                Metrics.Tick,
                GameplayEventKind.ProductionDisplacementStarted,
                blockerUnit,
                worldPosition: Units.Positions[blockerUnit]);
        }
        CommandQueues.ProductionEvacuationActive[blockerUnit] = true;
        CommandQueues.ProductionEvacuationTargets[blockerUnit] = exit;
        CommandQueues.ProductionEvacuationFootprints[blockerUnit] =
            producerBounds.Expanded(producedUnitRadius);
        SetCombatDestination(blockerUnit, exit);
        SetMovementGoal(
            blockerUnit,
            UnitMovementGoalKind.ProductionExit,
            producerBounds,
            producedUnitRadius,
            -1,
            UnitMovementLegResult.InProgress);
        return true;
    }

    private float ProductionExitPathCost(
        Vector2 start,
        Vector2 goal,
        float physicalRadius)
    {
        if (!_pathProvider.IsReady)
            return World.IsSegmentFree(start, goal, physicalRadius)
                ? Vector2.Distance(start, goal)
                : float.PositiveInfinity;
        var navigationRadius =
            MovementClearance.FromPhysicalRadius(physicalRadius)
                .NavigationRadius;
        var path = _pathProvider.FindPath(start, goal, navigationRadius);
        if (path.Length == 0)
            return float.PositiveInfinity;
        var result = 0f;
        for (var index = 1; index < path.Length; index++)
            result += Vector2.Distance(path[index - 1], path[index]);
        return result;
    }

    private void ProcessPathRequests()
    {
        if (!_pathProvider.IsReady)
        {
            return;
        }

        var requestsToProcess = Math.Min(PathBudgetPerTick, _pathRequests.Count);
        var workUnits = 0;
        for (var requestIndex = 0; requestIndex < requestsToProcess; requestIndex++)
        {
            if (requestIndex > 0 && workUnits >= PathWorkBudgetPerTick)
            {
                Metrics.PathBudgetDeferrals = _pathRequests.Count;
                break;
            }

            var request = _pathRequests.Dequeue();
            var unit = request.UnitIndex;
            _pathRequestQueued[unit] = false;
            if (!Units.Alive[unit] || !Units.PathPending[unit])
            {
                continue;
            }

            // A queue entry is a per-unit scheduling token, not an immutable
            // order snapshot. Command replacement updates the unit state while
            // retaining its place in line; processing always uses the latest
            // version and destination.
            var commandVersion = Units.CommandVersions[unit];
            var start = Units.Positions[unit];
            var goal = Units.SlotTargets[unit];
            var requestStart = Stopwatch.GetTimestamp();
            var pathDiagnostics = _pathProvider as GridPathProvider;
            var expandedBefore = pathDiagnostics?.LastExpandedNodes ?? 0;
            var rawPointsBefore = pathDiagnostics?.LastRawPathPoints ?? 0;
            var simplifiedPointsBefore =
                pathDiagnostics?.LastSimplifiedPathPoints ?? 0;
            var routeWaypointCount = Units.RouteWaypoints[unit].Length;
            var points = BuildUnitPath(unit, start, goal);
            if (points.Length == 0 &&
                Units.RouteWaypoints[unit].Length > 0)
            {
                // Region/choke routes are acceleration hints. A stale or
                // clearance-incompatible waypoint must not overrule a valid
                // low-level path from the unit's actual position to its goal.
                Units.RouteWaypoints[unit] = [];
                points = BuildUnitPath(unit, start, goal);
            }
            var requestMilliseconds = ElapsedMilliseconds(requestStart);
            var expandedNodes = Math.Max(
                0,
                (pathDiagnostics?.LastExpandedNodes ?? expandedBefore) -
                expandedBefore);
            var rawPoints = Math.Max(
                0,
                (pathDiagnostics?.LastRawPathPoints ?? rawPointsBefore) -
                rawPointsBefore);
            var simplifiedPoints = Math.Max(
                0,
                (pathDiagnostics?.LastSimplifiedPathPoints ??
                 simplifiedPointsBefore) - simplifiedPointsBefore);
            workUnits += PathRequestBaseWork + expandedNodes +
                         rawPoints * PathRawPointWork +
                         simplifiedPoints * PathSimplifiedPointWork;
            Metrics.PathWorkUnits = workUnits;
            Metrics.PathRequestsProcessed++;
            if (requestMilliseconds > Metrics.PathSlowestRequestMilliseconds)
            {
                Metrics.PathSlowestRequestMilliseconds = requestMilliseconds;
                Metrics.PathSlowestRequestUnit = unit;
                Metrics.PathSlowestRequestCommandVersion = commandVersion;
                Metrics.PathSlowestRequestDistance = Vector2.Distance(start, goal);
                Metrics.PathSlowestRequestNavigationRadius =
                    Units.NavigationRadii[unit];
                Metrics.PathSlowestRequestRouteWaypoints = routeWaypointCount;
                Metrics.PathSlowestRequestExpandedNodes = expandedNodes;
            }

            if (commandVersion != Units.CommandVersions[unit])
            {
                EnqueuePathRequest(unit);
                continue;
            }

            Units.PathPending[unit] = false;
            if (points.Length == 0 ||
                !MovementGoalAcceptsNavigationEndpoint(
                    unit, points[^1], goal))
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

            Units.Paths[unit] = new UnitPath(points, commandVersion);
            Units.Modes[unit] = UnitMoveMode.Moving;
            Units.BlockedByNavigation[unit] = false;
            Units.ProgressBestDistances[unit] = RemainingPathDistance(
                unit, Units.Paths[unit]);
            Metrics.PathsCompleted++;
        }
    }

    private void EnqueuePathRequest(int unit)
    {
        if (_pathRequestQueued[unit])
        {
            return;
        }

        _pathRequestQueued[unit] = true;
        _pathRequests.Enqueue(new PathRequest(
            unit, Units.CommandVersions[unit]));
    }

    private Vector2[] BuildUnitPath(int unit, Vector2 start, Vector2 finalGoal)
    {
        var result = new List<Vector2>(12) { start };
        var current = start;
        var route = Units.RouteWaypoints[unit];
        var navigationRadius = Units.NavigationRadii[unit];

        if (!World.IsDiscFree(start, navigationRadius))
        {
            var escapeStart = Stopwatch.GetTimestamp();
            var escaped = TryResolveNavigationStartEscape(
                unit, start, finalGoal, out var escape);
            Metrics.PathStartEscapeMilliseconds +=
                ElapsedMilliseconds(escapeStart);
            if (!escaped)
            {
                return [];
            }
            if (Vector2.DistanceSquared(start, escape) > 0.0001f)
            {
                result.Add(escape);
                current = escape;
            }
        }

        for (var waypointIndex = 0; waypointIndex <= route.Length; waypointIndex++)
        {
            var finalSegment = waypointIndex == route.Length;
            var segmentGoal = waypointIndex < route.Length
                ? ResolveUnitRouteWaypoint(unit, route[waypointIndex])
                : finalGoal;
            Vector2[] segment;
            var segmentAlreadyValidated = false;
            var surfaceEntryDistance = MathF.Max(
                1f,
                navigationRadius - Units.Radii[unit] + 0.5f);
            var directSurfaceEntry = finalSegment &&
                IsSurfaceInteractionGoal(unit) &&
                MovementGoalAcceptsNavigationEndpoint(
                    unit, segmentGoal, finalGoal) &&
                Vector2.DistanceSquared(current, segmentGoal) <=
                surfaceEntryDistance * surfaceEntryDistance &&
                World.IsSegmentFree(
                    current, segmentGoal, Units.Radii[unit]);
            if (directSurfaceEntry)
            {
                segment = [current, segmentGoal];
                segmentAlreadyValidated = true;
            }
            else if (_pathProvider is GridPathProvider)
            {
                // GridPathProvider owns the same direct-line fast path. Let it
                // perform that check once; doing it here first doubled the
                // broad-phase scan for every blocked cross-map segment.
                segment = _pathProvider.FindPath(
                    current, segmentGoal, navigationRadius);
                segmentAlreadyValidated = true;
            }
            else if (World.IsSegmentFree(
                         current, segmentGoal, navigationRadius))
            {
                segment = [current, segmentGoal];
                segmentAlreadyValidated = true;
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

            if (segment.Length > 0 && finalSegment &&
                IsSurfaceInteractionGoal(unit) &&
                !MovementGoalAcceptsPathEndpoint(
                    unit, segment[^1], segmentGoal) &&
                World.IsSegmentFree(
                    segment[^1], segmentGoal, Units.Radii[unit]))
            {
                segment = [.. segment, segmentGoal];
            }

            var endpointAccepted = segment.Length > 0 &&
                (finalSegment
                    ? MovementGoalAcceptsNavigationEndpoint(
                        unit, segment[^1], segmentGoal)
                    : Vector2.DistanceSquared(segment[^1], segmentGoal) <=
                      24f * 24f);
            if (!endpointAccepted)
            {
                return [];
            }
            if (!segmentAlreadyValidated)
            {
                var normalizationStart = Stopwatch.GetTimestamp();
                var normalized = NavigationPathTransition.TryNormalize(
                    World,
                    segment,
                    navigationRadius,
                    result.Count == 1 &&
                    !World.IsDiscFree(current, navigationRadius)
                        ? Units.Radii[unit]
                        : null,
                    finalSegment && IsSurfaceInteractionGoal(unit)
                        ? Units.Radii[unit]
                        : null,
                    out segment);
                Metrics.PathNormalizationMilliseconds +=
                    ElapsedMilliseconds(normalizationStart);
                if (!normalized)
                    return [];
            }

            for (var pointIndex = 1; pointIndex < segment.Length; pointIndex++)
            {
                var minimumSeparationSquared = finalSegment &&
                    IsSurfaceInteractionGoal(unit)
                        ? 0f
                        : 0.5f * 0.5f;
                if (Vector2.DistanceSquared(
                        result[^1], segment[pointIndex]) >
                    minimumSeparationSquared)
                {
                    result.Add(segment[pointIndex]);
                }
            }

            current = segment[^1];
        }

        if (IsSurfaceInteractionGoal(unit) &&
            !MovementGoalAcceptsNavigationEndpoint(
                unit, result[^1], finalGoal) &&
            MovementGoalAcceptsNavigationEndpoint(
                unit, current, finalGoal) &&
            Vector2.DistanceSquared(result[^1], current) > 0f)
        {
            result.Add(current);
        }

        return result.ToArray();
    }

    private bool TryResolveNavigationStartEscape(
        int unit,
        Vector2 start,
        Vector2 goal,
        out Vector2 escape)
    {
        const float physicalProjectionMargin = 0.05f;
        const float navigationProjectionMargin = 0.05f;
        escape = default;
        var physicalRadius = Units.Radii[unit];
        var navigationRadius = Units.NavigationRadii[unit];
        var physicalOrigin = start;
        if (!World.IsDiscFree(physicalOrigin, physicalRadius))
        {
            physicalOrigin = World.ConstrainDisc(
                physicalOrigin, physicalOrigin,
                physicalRadius + physicalProjectionMargin);
            var maximumRepair = MathF.Max(
                0.25f, navigationRadius - physicalRadius + 0.25f);
            if (!World.IsDiscFree(physicalOrigin, physicalRadius) ||
                Vector2.DistanceSquared(start, physicalOrigin) >
                maximumRepair * maximumRepair)
            {
                // Do not turn a genuinely embedded unit into an implicit
                // teleport. Construction/production evacuation owns those
                // larger corrections; this path only repairs surface drift.
                return false;
            }
        }

        if (World.IsDiscFree(physicalOrigin, navigationRadius))
        {
            escape = physicalOrigin;
            return true;
        }

        var projected = World.ConstrainDisc(
            physicalOrigin, physicalOrigin,
            navigationRadius + navigationProjectionMargin);
        if (NavigationEscapeCandidateIsValid(
                physicalOrigin, projected,
                physicalRadius, navigationRadius))
        {
            escape = projected;
            return true;
        }

        if (_pathProvider is GridPathProvider gridPathProvider &&
            gridPathProvider.TryFindStartEscape(
                physicalOrigin,
                goal,
                physicalRadius,
                navigationRadius,
                out escape))
            return true;

        // Terrain corners and overlapping footprints may not have a useful
        // single projection. Search the smallest local ring and prefer the
        // point that continues toward the requested destination.
        var direction = goal - physicalOrigin;
        var baseAngle = direction.LengthSquared() > 0.0001f
            ? MathF.Atan2(direction.Y, direction.X)
            : 0f;
        const int directionCount = 32;
        const float ringStep = 0.5f;
        var maximumDistance = MathF.Max(8f, navigationRadius * 2f);
        for (var distance = ringStep;
             distance <= maximumDistance + 0.0001f;
             distance += ringStep)
        {
            var best = default(Vector2);
            var bestGoalDistance = float.PositiveInfinity;
            var bestIndex = int.MaxValue;
            for (var index = 0; index < directionCount; index++)
            {
                var angle = baseAngle + MathF.Tau * index / directionCount;
                var candidate = physicalOrigin + new Vector2(
                    MathF.Cos(angle), MathF.Sin(angle)) * distance;
                if (!NavigationEscapeCandidateIsValid(
                        physicalOrigin, candidate,
                        physicalRadius, navigationRadius))
                {
                    continue;
                }
                var goalDistance = Vector2.DistanceSquared(candidate, goal);
                if (goalDistance > bestGoalDistance ||
                    goalDistance == bestGoalDistance && index >= bestIndex)
                {
                    continue;
                }
                best = candidate;
                bestGoalDistance = goalDistance;
                bestIndex = index;
            }
            if (float.IsFinite(bestGoalDistance))
            {
                escape = best;
                return true;
            }
        }
        return false;
    }

    private bool NavigationEscapeCandidateIsValid(
        Vector2 physicalOrigin,
        Vector2 candidate,
        float physicalRadius,
        float navigationRadius) =>
        World.IsDiscFree(candidate, navigationRadius) &&
        (Vector2.DistanceSquared(physicalOrigin, candidate) <= 0.0001f ||
         World.IsSegmentFree(physicalOrigin, candidate, physicalRadius));

    private bool MovementGoalAcceptsPathEndpoint(
        int unit,
        Vector2 endpoint,
        Vector2 requestedPoint)
    {
        var kind = Units.MovementGoalKinds[unit];
        var bounds = Units.MovementGoalBounds[unit];
        switch (kind)
        {
            case UnitMovementGoalKind.BuildingBoundary:
                return InteractionGeometry.DiscTouchesRectangle(
                    endpoint, Units.Radii[unit], bounds);
            case UnitMovementGoalKind.ResourceBoundary:
            case UnitMovementGoalKind.DropOffBoundary:
                if (bounds.Width > 0f || bounds.Height > 0f)
                {
                    return InteractionGeometry.DiscTouchesRectangle(
                        endpoint, Units.Radii[unit], bounds);
                }
                var center = bounds.Min;
                var allowed = Units.Radii[unit] +
                              Units.MovementGoalRadii[unit] +
                              InteractionGeometry.NumericTolerance(
                                  endpoint, bounds);
                return Vector2.DistanceSquared(endpoint, center) <=
                       allowed * allowed;
            case UnitMovementGoalKind.AttackRange:
                if (bounds.Width > 0f || bounds.Height > 0f)
                {
                    return InteractionGeometry.DiscTouchesRectangle(
                        endpoint,
                        Units.Radii[unit],
                        bounds,
                        Units.MovementGoalRadii[unit]);
                }
                goto case UnitMovementGoalKind.UnitBody;
            case UnitMovementGoalKind.UnitBody:
            case UnitMovementGoalKind.FollowRange:
                var target = Units.MovementGoalTargetIds[unit];
                if ((uint)target >= (uint)Units.Count || !Units.Alive[target])
                    return false;
                var targetRange = Units.Radii[unit] + Units.Radii[target] +
                    (kind == UnitMovementGoalKind.FollowRange
                        ? 0f
                        : Units.MovementGoalRadii[unit]);
                targetRange += InteractionGeometry.NumericTolerance(
                    endpoint,
                    new SimRect(
                        Units.Positions[target], Units.Positions[target]));
                return Vector2.DistanceSquared(
                           endpoint, Units.Positions[target]) <=
                       targetRange * targetRange;
            default:
                return Vector2.DistanceSquared(endpoint, requestedPoint) <=
                       24f * 24f;
        }
    }

    private bool MovementGoalAcceptsNavigationEndpoint(
        int unit,
        Vector2 endpoint,
        Vector2 requestedPoint)
    {
        if (Units.MovementGoalKinds[unit] is not
            (UnitMovementGoalKind.ResourceBoundary or
             UnitMovementGoalKind.DropOffBoundary))
        {
            return MovementGoalAcceptsPathEndpoint(
                unit, endpoint, requestedPoint);
        }

        var bounds = Units.MovementGoalBounds[unit];
        if (bounds.Width > 0f || bounds.Height > 0f)
        {
            return InteractionGeometry.DiscTouchesRectangle(
                endpoint, Units.NavigationRadii[unit], bounds);
        }
        var center = bounds.Min;
        var allowed = Units.NavigationRadii[unit] +
                      Units.MovementGoalRadii[unit] +
                      InteractionGeometry.NumericTolerance(endpoint, bounds);
        return Vector2.DistanceSquared(endpoint, center) <= allowed * allowed;
    }

    private bool IsSurfaceInteractionGoal(int unit) =>
        Units.MovementGoalKinds[unit] is
            UnitMovementGoalKind.BuildingBoundary or
            UnitMovementGoalKind.ResourceBoundary or
            UnitMovementGoalKind.DropOffBoundary or
            UnitMovementGoalKind.AttackRange or
            UnitMovementGoalKind.UnitBody or
            UnitMovementGoalKind.FollowRange;

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
        EnqueuePathRequest(unit);
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
        EnqueuePathRequest(unit);
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
        if (TryRetryEconomyDropOffBoundary(unit))
            return;
        Units.Paths[unit] = null;
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Idle;
        Units.Velocities[unit] = Vector2.Zero;
        Units.BlockedByNavigation[unit] = true;
        FinishMovementLeg(unit, UnitMovementLegResult.Unreachable);
        SetRecoveryStage(unit, RecoveryStage.Unreachable);
    }

    private bool TryRetryEconomyDropOffBoundary(int unit)
    {
        if (Units.MovementGoalKinds[unit] !=
                UnitMovementGoalKind.DropOffBoundary ||
            !Economy.IsGatherer(unit) ||
            Economy.Worker(unit).State != WorkerEconomyState.ReturningCargo)
            return false;

        // A building perimeter has several valid interaction points.
        // Exhausting recovery for one point queues the next boundary point;
        // it does not synchronously test the whole perimeter or mark the
        // worker globally unreachable.
        Units.MovementLegResults[unit] = UnitMovementLegResult.Unreachable;
        MoveEconomyWorker(unit, Units.MoveGoals[unit]);
        return Units.PathPending[unit];
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

            if (IsSurfaceInteractionGoal(unit) &&
                MovementGoalAcceptsPathEndpoint(
                    unit,
                    Units.Positions[unit],
                    Units.SlotTargets[unit]))
            {
                CompleteMovementLeg(unit);
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
                CompleteMovementLeg(unit);
                continue;
            }

            if (distance < 0.0001f)
            {
                continue;
            }

            Units.PreferredVelocities[unit] =
                toWaypoint / distance * Units.MaxSpeeds[unit];
        }
    }

    private void CompleteMovementLeg(int unit)
    {
        Units.Modes[unit] = UnitMoveMode.Arrived;
        FinishMovementLeg(unit, UnitMovementLegResult.Reached);
        Units.Paths[unit] = null;
        Units.PathPending[unit] = false;
        Units.PreferredVelocities[unit] = Vector2.Zero;
        Units.Velocities[unit] = Vector2.Zero;
        Units.NextVelocities[unit] = Vector2.Zero;
        if (Combat.CommandIntents[unit] == UnitCommandIntent.Move)
        {
            Combat.SetCommand(
                unit, UnitCommandIntent.None, Units.Positions[unit]);
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
            if ((!reachedRadius && !crossedWaypointPlane) ||
                !CanAdvanceToNextPathPoint(unit, path, position))
            {
                break;
            }

            path.Cursor++;
        }
    }

    private bool CanAdvanceToNextPathPoint(
        int unit,
        UnitPath path,
        Vector2 position)
    {
        var nextIndex = path.Cursor + 1;
        var radius = nextIndex == path.Points.Length - 1 &&
                     IsSurfaceInteractionGoal(unit)
            ? Units.Radii[unit]
            : Units.NavigationRadii[unit];
        return World.IsSegmentFree(position, path.Points[nextIndex], radius);
    }

    private void SyncLinkedCombatObjects()
    {
        for (var index = 0; index < CombatObjects.Count; index++)
        {
            var id = new CombatObjectId(index);
            var value = CombatObjects.Observe(id);
            var resource = value.Profile.LinkedResourceNodeId;
            if (resource < 0) continue;
            if ((uint)resource >= (uint)Economy.ResourceNodeCount)
                throw new InvalidOperationException(
                    $"Combat object {index} links missing resource {resource}.");
            var remaining = Economy.ResourceNodeRemaining(
                new EconomyResourceNodeId(resource));
            if (value.Health == remaining) continue;
            CombatObjects.SetHealth(id, remaining);
            if (value.Alive && remaining <= 0)
                RemoveCombatObjectFootprint(value.Profile);
        }
    }

    private void RemoveCombatObjectFootprint(
        in CombatObjectProfile profile)
    {
        if (profile.LinkedDynamicFootprintId <= 0) return;
        _issuingConstructionWorldMutation = true;
        try
        {
            RemoveDynamicFootprint(
                new DynamicFootprintId(profile.LinkedDynamicFootprintId));
        }
        finally
        {
            _issuingConstructionWorldMutation = false;
        }
    }

    private CombatTargetLayer CombatTargetLayerForUnit(int unit)
    {
        if ((Abilities.UnitTraits(unit) & AbilityUnitTraits.Ward) != 0)
            return CombatTargetLayer.Ward;
        return Combat.TerrainVisionModes[unit] == TerrainVisionMode.Elevated
            ? CombatTargetLayer.AirUnit
            : CombatTargetLayer.GroundUnit;
    }

    private void UpdateUnitFacings(float delta)
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            Units.PreviousFacings[unit] = Units.Facings[unit];
            if (!Units.Alive[unit]) continue;

            var desired = Vector2.Zero;
            switch (Combat.TargetKinds[unit])
            {
                case CombatTargetKind.Unit:
                {
                    var target = Combat.TargetUnits[unit];
                    if ((uint)target < (uint)Units.Count && Units.Alive[target])
                        desired = Units.Positions[target] - Units.Positions[unit];
                    break;
                }
                case CombatTargetKind.Building:
                {
                    var target = new GameplayBuildingId(
                        Combat.TargetBuildings[unit]);
                    if (Construction.IsAlive(target))
                    {
                        var bounds = Construction.Observe(target).Bounds;
                        desired = (bounds.Min + bounds.Max) * 0.5f -
                                  Units.Positions[unit];
                    }
                    break;
                }
                case CombatTargetKind.Object:
                {
                    var target = new CombatObjectId(
                        Combat.TargetBuildings[unit]);
                    if (CombatObjects.IsAlive(target))
                    {
                        desired = CombatObjects.Observe(target).Position -
                                  Units.Positions[unit];
                    }
                    break;
                }
            }

            if (desired.LengthSquared() <= 0.000001f)
            {
                desired = Units.Velocities[unit].LengthSquared() > 0.000001f
                    ? Units.Velocities[unit]
                    : Units.PreferredVelocities[unit];
            }

            Units.Facings[unit] = UnitFacing.RotateToward(
                Units.Facings[unit], desired,
                Units.TurnRatesRadiansPerSecond[unit] * delta);
        }
    }

    private void Integrate(float delta)
    {
        for (var unit = 0; unit < Units.Count; unit++)
            Units.PreviousPositions[unit] = Units.Positions[unit];

        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit])
            {
                Units.Velocities[unit] = Vector2.Zero;
                Units.NextVelocities[unit] = Vector2.Zero;
                continue;
            }
            Units.Velocities[unit] = Units.NextVelocities[unit];
            var displacement = Units.Velocities[unit] * delta;
            var proposed = Units.Positions[unit] + displacement;
            var path = Units.Paths[unit];
            if (Units.Modes[unit] == UnitMoveMode.Moving &&
                path is not null &&
                path.CommandVersion == Units.CommandVersions[unit])
            {
                var waypoint = path.Points[path.Cursor];
                var toWaypoint = waypoint - Units.Positions[unit];
                if (toWaypoint.LengthSquared() > 0f &&
                    Vector2.Dot(displacement, toWaypoint) >=
                    toWaypoint.LengthSquared())
                {
                    proposed = waypoint;
                }
            }
            if (Units.Modes[unit] == UnitMoveMode.Moving &&
                IsSurfaceInteractionGoal(unit) &&
                MovementGoalAcceptsPathEndpoint(
                    unit, proposed, Units.SlotTargets[unit]))
            {
                proposed = SurfaceArrivalPosition(
                    unit, Units.Positions[unit], proposed);
                Units.Positions[unit] = World.ConstrainDisc(
                    Units.Positions[unit], proposed, Units.Radii[unit]);
                CompleteMovementLeg(unit);
                continue;
            }
            var previous = Units.Positions[unit];
            var constrained = World.ConstrainDisc(
                previous, proposed, Units.Radii[unit]);
            Units.Positions[unit] = constrained;
            if (Units.Count > 256 &&
                Vector2.DistanceSquared(constrained, proposed) > 0.000001f)
            {
                // Preserve tangential motion but discard the component that a
                // wall/building actually rejected. Otherwise the next tick
                // starts from a velocity pointing into the obstacle and the
                // unit repeatedly sticks, turns and gets projected back out.
                Units.Velocities[unit] = (constrained - previous) / delta;
                Units.NextVelocities[unit] = Units.Velocities[unit];
                Metrics.WorldVelocityProjections++;
            }
        }
    }

    private Vector2 SurfaceArrivalPosition(
        int unit,
        Vector2 start,
        Vector2 end)
    {
        if (Units.MovementGoalKinds[unit] is
            UnitMovementGoalKind.UnitBody or UnitMovementGoalKind.FollowRange)
        {
            var target = Units.MovementGoalTargetIds[unit];
            if ((uint)target < (uint)Units.Count && Units.Alive[target])
            {
                var fromTarget = start - Units.Positions[target];
                if (fromTarget.LengthSquared() > 0f)
                {
                    var extraRange = Units.MovementGoalKinds[unit] ==
                                     UnitMovementGoalKind.UnitBody
                        ? Units.MovementGoalRadii[unit]
                        : 0f;
                    var distance = Units.Radii[unit] + Units.Radii[target] +
                                   extraRange +
                                   InteractionGeometry.NumericTolerance(
                                       start, Units.Positions[target]) * 0.5f;
                    return Units.Positions[target] +
                           Vector2.Normalize(fromTarget) * distance;
                }
            }
        }

        var outside = start;
        var inside = end;
        const int searchSteps = 20;
        for (var step = 0; step < searchSteps; step++)
        {
            var middle = (outside + inside) * 0.5f;
            if (MovementGoalAcceptsPathEndpoint(
                    unit, middle, Units.SlotTargets[unit]))
            {
                inside = middle;
            }
            else
            {
                outside = middle;
            }
        }
        return inside;
    }

    private void SolveCollisions()
    {
        if (Units.Count <= 256)
        {
            SolveCollisionsEstablishedOrder();
            return;
        }

        for (var iteration = 0; iteration < CollisionIterations; iteration++)
        {
            Metrics.CollisionMainIterations++;
            Array.Clear(Units.CollisionCorrections, 0, Units.Count);
            _spatialHash.Rebuild(Units);
            var pairCount = CollectCollisionPairs(0f);
            Metrics.CollisionBroadphasePairs += pairCount;

            for (var pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                var encodedPair = _collisionPairScratch[pairIndex];
                var unit = (int)(encodedPair >> 32);
                var neighbor = (int)encodedPair;
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

                ProjectClosingCollisionVelocity(
                    unit, neighbor, normal, resolution);

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

            var moved = false;
            for (var unit = 0; unit < Units.Count; unit++)
            {
                if (!Units.Alive[unit])
                {
                    continue;
                }
                var correction = Units.CollisionCorrections[unit];
                if (correction.LengthSquared() <= 0.0000001f)
                    continue;
                var previous = Units.Positions[unit];
                Metrics.CollisionConstraintCalls++;
                var constrained = World.ConstrainDisc(
                    previous,
                    previous + correction,
                    Units.Radii[unit]);
                Units.Positions[unit] = constrained;
                moved |= Vector2.DistanceSquared(previous, constrained) >
                         0.0000001f;
            }
            if (!moved)
                break;
        }
        ResolveResidualCollisionPenetrations();
    }

    private void SolveCollisionsEstablishedOrder()
    {
        for (var iteration = 0; iteration < CollisionIterations; iteration++)
        {
            Metrics.CollisionMainIterations++;
            Array.Clear(Units.CollisionCorrections, 0, Units.Count);
            _spatialHash.Rebuild(Units);

            for (var unit = 0; unit < Units.Count; unit++)
            {
                if (!Units.Alive[unit])
                    continue;
                var neighborCount = _spatialHash.Query(
                    Units.Positions[unit],
                    Units.Radii[unit] * 4f + 8f,
                    unit,
                    _collisionNeighbors);

                for (var neighborIndex = 0;
                     neighborIndex < neighborCount;
                     neighborIndex++)
                {
                    var neighbor = _collisionNeighbors[neighborIndex];
                    if (neighbor <= unit)
                        continue;
                    Metrics.CollisionBroadphasePairs++;
                    if (UnitCollisionPolicy.SuppressesPair(
                            _unitCollisionSuppressed[unit],
                            Combat.ConcealmentKinds[unit],
                            _unitCollisionSuppressed[neighbor],
                            Combat.ConcealmentKinds[neighbor]))
                        continue;

                    var offset = Units.Positions[neighbor] -
                                 Units.Positions[unit];
                    var minimumDistance = Units.Radii[unit] +
                                          Units.Radii[neighbor];
                    var distanceSquared = offset.LengthSquared();
                    if (distanceSquared >= minimumDistance * minimumDistance)
                        continue;

                    var distance = MathF.Sqrt(MathF.Max(
                        distanceSquared, 0.000001f));
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
                        continue;

                    var correctionMagnitude =
                        (penetration + 0.01f) * 0.72f;
                    var correction = normal * correctionMagnitude;
                    Units.CollisionCorrections[unit] -=
                        correction * resolution.LeftCorrectionShare;
                    Units.CollisionCorrections[neighbor] +=
                        correction * resolution.RightCorrectionShare;
                    if (resolution.LeftPushing || resolution.RightPushing)
                    {
                        Metrics.PriorityPushPairs++;
                        Metrics.PriorityPushDisplacement +=
                            correctionMagnitude *
                            (resolution.LeftPushing
                                ? resolution.RightCorrectionShare
                                : resolution.LeftCorrectionShare);
                    }
                }
            }

            for (var unit = 0; unit < Units.Count; unit++)
            {
                if (!Units.Alive[unit])
                    continue;
                var previous = Units.Positions[unit];
                Metrics.CollisionConstraintCalls++;
                Units.Positions[unit] = World.ConstrainDisc(
                    previous,
                    previous + Units.CollisionCorrections[unit],
                    Units.Radii[unit]);
            }
        }
        ResolveResidualCollisionPenetrations();
    }

    /// <summary>
    /// The parallel steering correction above is stable for crowds, but
    /// corrections from several neighbors can cancel near a cliff or wall.
    /// This bounded sequential projection removes the remaining penetration
    /// without changing push priority. Open-field movement exits after one
    /// empty pass; extra work is paid only by an actually crowded region.
    /// </summary>
    private void ResolveResidualCollisionPenetrations()
    {
        if (Units.Count <= 256)
        {
            ResolveResidualCollisionPenetrationsEstablishedOrder();
            return;
        }

        // A per-contact active queue looks attractive, but moving either side
        // and immediately querying its neighbors amplifies a dense crowd into
        // tens of thousands of repeated hash queries. It also queries buckets
        // built from positions before the correction. Instead, collect a
        // deterministic contact superset with one-radius guard space and run a
        // small Gauss-Seidel batch over it. Rebuilding between batches admits
        // contacts created by correction without paying a broadphase query for
        // every moved pair.
        var remainingPasses = ResidualCollisionPassLimit;
        while (remainingPasses > 0)
        {
            _spatialHash.Rebuild(Units);
            var pairCount = CollectCollisionPairs(
                _spatialHash.MaximumRadius + ResidualCollisionTolerance);
            if (pairCount == 0) return;
            if (_collisionPairApplicableScratch.Length < pairCount)
                Array.Resize(
                    ref _collisionPairApplicableScratch,
                    _collisionPairScratch.Length);
            if (_collisionResidualMovedUnits.Length < Units.Count)
                Array.Resize(
                    ref _collisionResidualMovedUnits,
                    Units.Capacity);

            var batchPasses = Math.Min(
                remainingPasses,
                ResidualCollisionBroadphaseBatchSize);
            for (var pass = 0; pass < batchPasses; pass++)
            {
                Metrics.CollisionResidualPasses++;
                Metrics.CollisionBroadphasePairs += pairCount;
                Array.Clear(
                    _collisionResidualMovedUnits, 0, Units.Count);
                var moved = false;
                for (var pairOffset = 0;
                     pairOffset < pairCount;
                     pairOffset++)
                {
                    // Alternating traversal prevents a long packed chain from
                    // propagating separation in only one id direction. The
                    // contact set and all corrections remain deterministic.
                    var pairIndex = (Metrics.CollisionResidualPasses & 1) != 0
                        ? pairOffset
                        : pairCount - pairOffset - 1;
                    var encodedPair = _collisionPairScratch[pairIndex];
                    var unit = (int)(encodedPair >> 32);
                    var neighbor = (int)encodedPair;
                    Metrics.CollisionResidualPairChecks++;
                    var result = ResolveResidualCollisionPair(unit, neighbor);
                    _collisionPairApplicableScratch[pairIndex] =
                        result.Applicable;
                    moved |= result.Moved;
                    if (result.Moved)
                    {
                        Metrics.CollisionResidualPairMoves++;
                        // The result intentionally does not expose which side
                        // moved. Marking both is conservative and still lets
                        // the next pass discard every provably unaffected
                        // contact candidate.
                        _collisionResidualMovedUnits[unit] = true;
                        _collisionResidualMovedUnits[neighbor] = true;
                    }
                }
                if (!moved) return;

                var nextPairCount = 0;
                for (var pairIndex = 0;
                     pairIndex < pairCount;
                     pairIndex++)
                {
                    var encodedPair = _collisionPairScratch[pairIndex];
                    var unit = (int)(encodedPair >> 32);
                    var neighbor = (int)encodedPair;
                    if (!_collisionPairApplicableScratch[pairIndex] &&
                        !_collisionResidualMovedUnits[unit] &&
                        !_collisionResidualMovedUnits[neighbor])
                        continue;
                    _collisionPairScratch[nextPairCount++] = encodedPair;
                }
                pairCount = nextPairCount;
                if (pairCount == 0) return;
            }
            remainingPasses -= batchPasses;
        }
    }

    private void ResolveResidualCollisionPenetrationsEstablishedOrder()
    {
        for (var pass = 0; pass < ResidualCollisionPassLimit; pass++)
        {
            Metrics.CollisionResidualPasses++;
            var corrected = false;
            _spatialHash.Rebuild(Units);
            for (var unit = 0; unit < Units.Count; unit++)
            {
                if (!Units.Alive[unit])
                    continue;
                var neighborCount = _spatialHash.Query(
                    Units.Positions[unit],
                    Units.Radii[unit] * 4f + 8f,
                    unit,
                    _collisionNeighbors);
                for (var neighborIndex = 0;
                     neighborIndex < neighborCount;
                     neighborIndex++)
                {
                    var neighbor = _collisionNeighbors[neighborIndex];
                    if (neighbor <= unit)
                        continue;
                    Metrics.CollisionBroadphasePairs++;
                    var result = ResolveResidualCollisionPair(unit, neighbor);
                    // Preserve the established bounded retry behavior for
                    // ordinary-size movement scenes. Large crowds instead
                    // stop when a pass made no physical progress.
                    corrected |= result.Applicable;
                }
            }
            if (!corrected)
                return;
        }
    }

    private (bool Applicable, bool Moved) ResolveResidualCollisionPair(
        int unit,
        int neighbor)
    {
        if (UnitCollisionPolicy.SuppressesPair(
                _unitCollisionSuppressed[unit],
                Combat.ConcealmentKinds[unit],
                _unitCollisionSuppressed[neighbor],
                Combat.ConcealmentKinds[neighbor]))
            return (false, false);

        var offset = Units.Positions[neighbor] - Units.Positions[unit];
        var minimumDistance = Units.Radii[unit] + Units.Radii[neighbor];
        var distanceSquared = offset.LengthSquared();
        var allowedDistance = minimumDistance - ResidualCollisionTolerance;
        if (distanceSquared >= allowedDistance * allowedDistance)
            return (false, false);

        var distance = MathF.Sqrt(MathF.Max(distanceSquared, 0.000001f));
        var normal = distanceSquared > 0.000001f
            ? offset / distance
            : DeterministicNormal(unit, neighbor);
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
            return (false, false);

        var required = minimumDistance - distance +
                       ResidualCollisionTolerance;
        var pairMoved = ApplyResidualCollisionCorrection(
            unit,
            -normal * required * resolution.LeftCorrectionShare);
        pairMoved |= ApplyResidualCollisionCorrection(
            neighbor,
            normal * required * resolution.RightCorrectionShare);

        // A wall may clip one unit's assigned share. Transfer only the
        // still-missing separation to a side that policy says may move.
        offset = Units.Positions[neighbor] - Units.Positions[unit];
        distance = offset.Length();
        var remaining = minimumDistance - distance +
                        ResidualCollisionTolerance;
        if (remaining <= 0f)
        {
            ProjectClosingCollisionVelocity(
                unit, neighbor, normal, resolution);
            return (true, pairMoved);
        }
        normal = distance > 0.0001f
            ? offset / distance
            : DeterministicNormal(unit, neighbor);
        if (resolution.RightCorrectionShare >=
            resolution.LeftCorrectionShare)
        {
            if (resolution.RightCorrectionShare > 0f)
                pairMoved |= ApplyResidualCollisionCorrection(
                    neighbor, normal * remaining);
            remaining = RemainingCollisionSeparation(unit, neighbor);
            if (remaining > 0f && resolution.LeftCorrectionShare > 0f)
                pairMoved |= ApplyResidualCollisionCorrection(
                    unit, -normal * remaining);
        }
        else
        {
            if (resolution.LeftCorrectionShare > 0f)
                pairMoved |= ApplyResidualCollisionCorrection(
                    unit, -normal * remaining);
            remaining = RemainingCollisionSeparation(unit, neighbor);
            if (remaining > 0f && resolution.RightCorrectionShare > 0f)
                pairMoved |= ApplyResidualCollisionCorrection(
                    neighbor, normal * remaining);
        }
        offset = Units.Positions[neighbor] - Units.Positions[unit];
        if (offset.LengthSquared() > 0.000001f)
            normal = Vector2.Normalize(offset);
        ProjectClosingCollisionVelocity(unit, neighbor, normal, resolution);
        return (true, pairMoved);
    }

    private void ProjectClosingCollisionVelocity(
        int unit,
        int neighbor,
        Vector2 normal,
        UnitPushResolution resolution)
    {
        // Keep established small/medium deterministic movement snapshots
        // unchanged while the large-crowd response is validated separately.
        if (Units.Count <= 256) return;
        var closingSpeed = Vector2.Dot(
            Units.Velocities[neighbor] - Units.Velocities[unit], normal);
        if (closingSpeed >= -0.001f) return;
        var shareTotal = resolution.LeftCorrectionShare +
                         resolution.RightCorrectionShare;
        if (shareTotal <= 0f) return;
        var cancellation = -closingSpeed;
        Units.Velocities[unit] -= normal * cancellation *
            (resolution.LeftCorrectionShare / shareTotal);
        Units.Velocities[neighbor] += normal * cancellation *
            (resolution.RightCorrectionShare / shareTotal);
        Units.NextVelocities[unit] = Units.Velocities[unit];
        Units.NextVelocities[neighbor] = Units.Velocities[neighbor];
        Metrics.CollisionVelocityProjections++;
    }

    private bool ApplyResidualCollisionCorrection(int unit, Vector2 correction)
    {
        if (correction.LengthSquared() <= 0.0000001f)
            return false;
        var position = Units.Positions[unit];
        Metrics.CollisionConstraintCalls++;
        var constrained = World.ConstrainDisc(
            position,
            position + correction,
            Units.Radii[unit]);
        Units.Positions[unit] = constrained;
        return Vector2.DistanceSquared(position, constrained) > 0.0000001f;
    }

    private int CollectCollisionPairs(float tolerance)
    {
        // Keep the established small/medium crowd ordering used by movement
        // regression scenes. Large crowds switch to the bucket-pair traversal,
        // which removes the duplicated per-unit neighborhood scans that
        // dominate the 800-unit workload.
        if (Units.Count > 256)
            return _spatialHash.CollectPotentialCollisionPairs(
                Units, tolerance, ref _collisionPairScratch);

        var count = 0;
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit])
                continue;
            var neighborCount = _spatialHash.Query(
                Units.Positions[unit],
                Units.Radii[unit] * 4f + 8f + tolerance,
                unit,
                _collisionNeighbors);
            for (var neighborIndex = 0;
                 neighborIndex < neighborCount;
                 neighborIndex++)
            {
                var neighbor = _collisionNeighbors[neighborIndex];
                if (neighbor <= unit)
                    continue;
                if (count == _collisionPairScratch.Length)
                    Array.Resize(
                        ref _collisionPairScratch,
                        _collisionPairScratch.Length * 2);
                _collisionPairScratch[count++] =
                    ((long)unit << 32) | (uint)neighbor;
            }
        }
        return count;
    }

    private float RemainingCollisionSeparation(int left, int right) =>
        Units.Radii[left] + Units.Radii[right] -
        Vector2.Distance(Units.Positions[left], Units.Positions[right]) +
        ResidualCollisionTolerance;

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
                var displacedFromReservation = Vector2.DistanceSquared(
                    Units.Positions[unit], Units.SlotTargets[unit]) >
                    ReservationMigrationActivationDistance *
                    ReservationMigrationActivationDistance;
                if (displacedFromReservation ||
                    Units.ReservationMigrationTicks[unit] > 0)
                {
                    Units.ReservationMigrationTicks[unit] =
                        ReservationMigrationSettleTicks;
                }
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

        if (displacedCount > MaximumReservationMigrationComponentUnits ||
            !ExpandReservationMigrationComponent(ref displacedCount))
        {
            for (var index = 0; index < displacedCount; index++)
                Units.ReservationMigrationTicks[
                    _displacedStationaryUnits[index]] = 0;
            return;
        }

        var relaxationPasses = Math.Clamp(
            displacedCount * 2,
            ReservationMigrationRelaxationPasses,
            MaximumReservationRelaxationPasses);
        Metrics.ReservationMigrationAttempts++;
        Metrics.LastReservationMigrationUnits = displacedCount;
        var resolved = ResolveTerminalReservations(
            _displacedStationaryUnits.AsSpan(0, displacedCount),
            _reservationTargetScratch.AsSpan(0, displacedCount),
            relaxationPasses,
            MaximumMigratedReservationDistance);
        Metrics.LastReservationMigrationClearance =
            MinimumTerminalReservationClearance(
                _displacedStationaryUnits.AsSpan(0, displacedCount),
                _reservationTargetScratch.AsSpan(0, displacedCount));
        if (!resolved)
        {
            Metrics.ReservationMigrationFailures++;
            for (var index = 0; index < displacedCount; index++)
            {
                var unit = _displacedStationaryUnits[index];
                if (IsStationaryReservationCandidate(unit))
                {
                    Units.ReservationMigrationTicks[unit] = 0;
                }
            }
            return;
        }
        for (var index = 0; index < displacedCount; index++)
        {
            var unit = _displacedStationaryUnits[index];
            var reservation = _reservationTargetScratch[index];
            Units.SlotTargets[unit] = reservation;
            Units.MoveGoals[unit] = reservation;
            Units.DestinationYieldReturnTargets[unit] = reservation;
            Units.DestinationYieldPoints[unit] = reservation;
        }
    }

    private bool ExpandReservationMigrationComponent(ref int selectedCount)
    {
        _reservationSelectionScratch.AsSpan(0, Units.Count).Fill(-1);
        for (var index = 0; index < selectedCount; index++)
            _reservationSelectionScratch[_displacedStationaryUnits[index]] = index;

        const float reservationPadding = 2f;
        for (var selectedIndex = 0;
             selectedIndex < selectedCount;
             selectedIndex++)
        {
            var selected = _displacedStationaryUnits[selectedIndex];
            for (var candidate = 0; candidate < Units.Count; candidate++)
            {
                if (_reservationSelectionScratch[candidate] >= 0 ||
                    !IsStationaryReservationCandidate(candidate))
                {
                    continue;
                }

                var required = Units.Radii[selected] + Units.Radii[candidate] +
                               reservationPadding;
                var mutualReach = required +
                                  MaximumMigratedReservationDistance * 2f;
                var selectedReach = required +
                                    MaximumMigratedReservationDistance;
                var canAffect = Vector2.DistanceSquared(
                        Units.Positions[selected], Units.Positions[candidate]) <=
                    mutualReach * mutualReach ||
                    Vector2.DistanceSquared(
                        Units.Positions[selected], Units.SlotTargets[candidate]) <=
                    selectedReach * selectedReach ||
                    Vector2.DistanceSquared(
                        Units.SlotTargets[selected], Units.Positions[candidate]) <=
                    selectedReach * selectedReach;
                if (!canAffect)
                    continue;

                if (selectedCount >=
                    MaximumReservationMigrationComponentUnits)
                {
                    return false;
                }

                _reservationSelectionScratch[candidate] = selectedCount;
                _displacedStationaryUnits[selectedCount++] = candidate;
            }
        }
        return true;
    }

    private bool IsStationaryReservationCandidate(int unit) =>
        Units.Alive[unit] &&
        (Units.Modes[unit] is UnitMoveMode.Idle or UnitMoveMode.Arrived) &&
        Combat.TargetKinds[unit] == CombatTargetKind.None &&
        Units.DestinationYieldPhases[unit] == DestinationYieldPhase.None;

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
        if (IsSurfaceInteractionGoal(unit))
        {
            if (MovementGoalAcceptsPathEndpoint(
                    unit,
                    Units.Positions[unit],
                    Units.SlotTargets[unit]))
            {
                CompleteMovementLeg(unit);
            }
            else
            {
                MarkUnreachable(unit);
            }
            return;
        }

        var position = Units.Positions[unit];
        Units.Paths[unit] = null;
        Units.RouteWaypoints[unit] = [];
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Arrived;
        FinishMovementLeg(unit, UnitMovementLegResult.SettledShort);
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
        Units.ReservationMigrationTicks[unit] =
            ReservationMigrationSettleTicks;
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
                Units.SlotReflowCooldownTicks[leader] > Metrics.Tick ||
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
            if (!ResolveTerminalReservations(
                    groupUnits,
                    terminalAssignments,
                    ReservationMigrationRelaxationPasses))
            {
                // Preserve the group's unique slots and allow destination
                // reflow/yield to improve the layout before another bounded
                // settle attempt. Reusing the leader's existing absolute
                // reflow cooldown keeps this retry state replay-safe.
                Units.SlotReflowCooldownTicks[leader] = Math.Max(
                    Units.SlotReflowCooldownTicks[leader],
                    Metrics.Tick + DestinationReflowCooldownTicks);
                continue;
            }

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
                FinishMovementLeg(unit, UnitMovementLegResult.Reached);
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

    private bool ResolveTerminalReservations(
        ReadOnlySpan<int> groupUnits,
        Span<Vector2> targets,
        int relaxationPasses,
        float maximumDistanceFromPosition = float.PositiveInfinity)
    {
        if (targets.Length < groupUnits.Length)
            throw new ArgumentException(
                "Reservation target buffer is too small.", nameof(targets));
        if (groupUnits.IsEmpty)
            return true;
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
            var foundConflict = false;
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
                    var numericSafety = InteractionGeometry.NumericTolerance(
                        targets[leftIndex], rightTarget);
                    var separationTarget = required + numericSafety * 2f;
                    if (distance >= separationTarget)
                        continue;
                    foundConflict = true;

                    var normal = distance > 0.0001f
                        ? offset / distance
                        : DeterministicNormal(left, right);
                    var correction = separationTarget - distance;
                    if (rightIndex >= 0)
                    {
                        MoveTerminalReservation(
                            left,
                            ref targets[leftIndex],
                            normal * correction * 0.5f,
                            maximumDistanceFromPosition);
                        MoveTerminalReservation(
                            right,
                            ref targets[rightIndex],
                            -normal * correction * 0.5f,
                            maximumDistanceFromPosition);
                    }
                    else
                    {
                        MoveTerminalReservation(
                            left,
                            ref targets[leftIndex],
                            normal * correction,
                            maximumDistanceFromPosition);
                    }
                }
            }
            if (!foundConflict)
                return true;
        }

        return TerminalReservationsAreSeparated(
            groupUnits, targets, reservationPadding);
    }

    private void MoveTerminalReservation(
        int unit,
        ref Vector2 target,
        Vector2 displacement,
        float maximumDistanceFromPosition)
    {
        var candidate = World.Bounds
            .Inset(Units.Radii[unit] + 2f)
            .Clamp(target + displacement);
        if (float.IsFinite(maximumDistanceFromPosition))
        {
            var offset = candidate - Units.Positions[unit];
            if (offset.LengthSquared() > maximumDistanceFromPosition *
                maximumDistanceFromPosition)
            {
                candidate = Units.Positions[unit] + Vector2.Normalize(offset) *
                    maximumDistanceFromPosition;
            }
        }
        if (World.IsDiscFree(candidate, Units.Radii[unit]))
            target = candidate;
    }

    private bool TerminalReservationsAreSeparated(
        ReadOnlySpan<int> groupUnits,
        ReadOnlySpan<Vector2> targets,
        float padding)
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
                var required = Units.Radii[left] + Units.Radii[right] + padding +
                    InteractionGeometry.NumericTolerance(
                        targets[leftIndex], rightTarget);
                if (Vector2.DistanceSquared(targets[leftIndex], rightTarget) <
                    required * required)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private float MinimumTerminalReservationClearance(
        ReadOnlySpan<int> groupUnits,
        ReadOnlySpan<Vector2> targets)
    {
        var minimum = float.PositiveInfinity;
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
                var clearance = Vector2.Distance(
                    targets[leftIndex], rightTarget) -
                    Units.Radii[left] - Units.Radii[right];
                minimum = MathF.Min(minimum, clearance);
            }
        }
        return minimum;
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
        EnqueuePathRequest(unit);
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
                CommandQueues.ProductionEvacuationActive[unit] ||
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
            UnitOrderKind.Move => MovementLegFinished(unit),
            UnitOrderKind.AttackMove =>
                MovementLegFinished(unit) &&
                Combat.TargetUnits[unit] < 0,
            UnitOrderKind.AttackTarget =>
                (uint)CommandQueues.ActiveTargetUnits[unit] >= (uint)Units.Count ||
                !Units.Alive[CommandQueues.ActiveTargetUnits[unit]] ||
                Units.MovementLegResults[unit] ==
                    UnitMovementLegResult.Unreachable,
            UnitOrderKind.AttackBuilding =>
                !Construction.IsAlive(new GameplayBuildingId(
                    CommandQueues.ActiveTargetBuildings[unit])) ||
                Units.MovementLegResults[unit] ==
                    UnitMovementLegResult.Unreachable,
            UnitOrderKind.AttackObject =>
                !CombatObjects.IsAlive(new CombatObjectId(
                    CommandQueues.ActiveTargetResourceNodes[unit])) ||
                Units.MovementLegResults[unit] ==
                    UnitMovementLegResult.Unreachable,
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

    private void RecoverFailedExplicitBuildingAttackChases()
    {
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!Units.Alive[unit] ||
                !CommandQueues.HasActiveOrders[unit] ||
                CommandQueues.ActiveKinds[unit] != UnitOrderKind.AttackBuilding ||
                Units.MovementGoalKinds[unit] != UnitMovementGoalKind.AttackRange)
            {
                continue;
            }

            var unreachable = Units.MovementLegResults[unit] ==
                              UnitMovementLegResult.Unreachable;
            var stalled = Units.MovementLegResults[unit] ==
                              UnitMovementLegResult.InProgress &&
                          Units.DynamicBlockageTicks[unit] >=
                              DynamicBlockageTimeoutTicks / 2;
            if (!unreachable && !stalled)
                continue;

            var building = new GameplayBuildingId(
                CommandQueues.ActiveTargetBuildings[unit]);
            if (!Construction.IsAlive(building) ||
                !IsHostileBuilding(building, Combat.Teams[unit]))
            {
                continue;
            }

            var bounds = Construction.Observe(building).Bounds;
            var nearest = bounds.Clamp(Units.Positions[unit]);
            var distance = Vector2.Distance(Units.Positions[unit], nearest);
            var range = Combat.AttackRanges[unit] + Units.Radii[unit] +
                        InteractionGeometry.NumericTolerance(
                            Units.Positions[unit], bounds);
            if (distance <= range)
            {
                ((ICombatMovementDriver)this).StopForAttack(unit);
                continue;
            }

            if (!TryResolveUnoccupiedBuildingAttackTarget(
                    unit, building, out var target))
            {
                continue;
            }

            Combat.TargetKinds[unit] = CombatTargetKind.Building;
            Combat.TargetUnits[unit] = -1;
            Combat.TargetBuildings[unit] = building.Value;
            Combat.Phases[unit] = CombatPhase.Chasing;
            Combat.LastChaseTargets[unit] = target;
            var blockageTicks = unreachable
                ? DynamicBlockageTimeoutTicks
                : Units.DynamicBlockageTicks[unit];
            SetCombatDestination(unit, target);
            Units.DynamicBlockageTicks[unit] = 0;
            Units.DynamicBlockageBestDistances[unit] = Vector2.Distance(
                Units.Positions[unit], target);
            GameplayEvents.Publish(
                Metrics.Tick,
                GameplayEventKind.BuildingAttackChaseRetargeted,
                unit,
                building.Value,
                value: blockageTicks,
                movementGoalKind: UnitMovementGoalKind.AttackRange,
                movementResult: UnitMovementLegResult.InProgress,
                worldPosition: target);
        }
    }

    private bool TryResolveUnoccupiedBuildingAttackTarget(
        int attacker,
        GameplayBuildingId building,
        out Vector2 target)
    {
        target = default;
        if (!Construction.IsAlive(building))
            return false;
        var bounds = Construction.Observe(building).Bounds;
        var approach = ConstructionAccessPointResolver.Resolve(
            World,
            _pathProvider,
            bounds,
            Units.Positions[attacker],
            Units.Radii[attacker],
            Units.NavigationRadii[attacker],
            Combat.AttackRanges[attacker],
            (center, radius) =>
                IsDiscClearOfFriendlyUnits(attacker, center, radius));
        target = approach.Target;
        return approach.Found;
    }

    private bool IsDiscClearOfFriendlyUnits(
        int attacker,
        Vector2 center,
        float radius)
    {
        var team = Combat.Teams[attacker];
        for (var other = 0; other < Units.Count; other++)
        {
            if (other == attacker || !Units.Alive[other] ||
                Combat.Teams[other] != team)
            {
                continue;
            }

            var otherBounds = new SimRect(
                Units.Positions[other], Units.Positions[other]);
            var separation = radius + Units.Radii[other] +
                             InteractionGeometry.NumericTolerance(
                                 center, otherBounds);
            if (Vector2.DistanceSquared(center, Units.Positions[other]) <
                separation * separation)
            {
                return false;
            }
        }
        return true;
    }

    private bool MovementLegFinished(int unit) =>
        Units.MovementLegResults[unit] is
            UnitMovementLegResult.Reached or
            UnitMovementLegResult.SettledShort or
            UnitMovementLegResult.Unreachable or
            UnitMovementLegResult.TargetInvalidated or
            UnitMovementLegResult.Canceled;

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

    void ICombatMovementDriver.Retreat(int unit, Vector2 target)
    {
        SetCombatRetreatDestination(unit, target);
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
        FinishMovementLeg(unit, UnitMovementLegResult.Reached);
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
        Abilities.CancelCaster(
            unit, Metrics.Tick, AbilityEndReason.CasterDied,
            this, AbilityEvents);
        var concealmentPhase = Combat.ConcealmentPhases[unit];
        if (concealmentPhase is UnitConcealmentPhase.Activating or
            UnitConcealmentPhase.Deactivating)
        {
            AbilityEvents.Publish(
                Metrics.Tick,
                AbilityEventKind.Interrupted,
                ConcealmentAbilityId(
                    concealmentPhase == UnitConcealmentPhase.Activating),
                unit,
                AbilityTargetKind.Self,
                unit,
                Units.Positions[unit],
                AbilityEndReason.CasterDied);
        }
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

    int IAbilityRuntimeWorld.AbilityUnitCount => Units.Count;

    int IAbilityRuntimeWorld.AbilityBuildingCount => Construction.SlotCount;

    bool IAbilityRuntimeWorld.AbilityUnitExists(int unit) =>
        (uint)unit < (uint)Units.Count;

    bool IAbilityRuntimeWorld.AbilityUnitAlive(int unit) =>
        (uint)unit < (uint)Units.Count && Units.Alive[unit];

    int IAbilityRuntimeWorld.AbilityUnitOwner(int unit) => Combat.Teams[unit];

    Vector2 IAbilityRuntimeWorld.AbilityUnitPosition(int unit) =>
        Units.Positions[unit];

    float IAbilityRuntimeWorld.AbilityUnitRadius(int unit) => Units.Radii[unit];

    float IAbilityRuntimeWorld.AbilityUnitHealth(int unit) =>
        Combat.Health[unit];

    float IAbilityRuntimeWorld.AbilityUnitMaximumHealth(int unit) =>
        Combat.MaximumHealth[unit];

    CombatAttribute IAbilityRuntimeWorld.AbilityUnitAttributes(int unit) =>
        Combat.Attributes[unit];

    bool IAbilityRuntimeWorld.AbilityUnitIsAir(int unit) =>
        Combat.TerrainVisionModes[unit] == TerrainVisionMode.Elevated;

    bool IAbilityRuntimeWorld.AbilityCanSeeUnit(int playerId, int unit) =>
        Visibility.IsUnitVisible(playerId, unit, Units, Combat);

    PlayerEntityRelation IAbilityRuntimeWorld.AbilityRelation(
        int playerId,
        int otherPlayerId) => Diplomacy.Relation(playerId, otherPlayerId);

    int IAbilityRuntimeWorld.AbilityTechnologyLevel(
        int playerId,
        int technologyId) => Technology.Level(playerId, technologyId);

    int IAbilityRuntimeWorld.AbilityCompletedBuildingCount(
        int playerId,
        int buildingTypeId) => Construction.CountCompleted(
        playerId, buildingTypeId);

    bool IAbilityRuntimeWorld.AbilityBuildingAlive(
        GameplayBuildingId building) => Construction.IsAlive(building);

    bool IAbilityRuntimeWorld.AbilityBuildingCompleted(
        GameplayBuildingId building) =>
        Construction.IsAlive(building) &&
        Construction.Observe(building).State ==
        BuildingLifecycleState.Completed;

    int IAbilityRuntimeWorld.AbilityBuildingOwner(
        GameplayBuildingId building) => Construction.Observe(building).PlayerId;

    SimRect IAbilityRuntimeWorld.AbilityBuildingBounds(
        GameplayBuildingId building) => Construction.Observe(building).Bounds;

    bool IAbilityRuntimeWorld.AbilityCanSeePosition(
        int playerId,
        Vector2 position) => Visibility.IsVisible(playerId, position);

    bool IAbilityRuntimeWorld.AbilityDamageUnit(
        int sourceUnit,
        int targetUnit,
        float damage,
        AbilityDamageKind damageKind)
    {
        if (damageKind == AbilityDamageKind.None ||
            damageKind == AbilityDamageKind.Magic &&
            Abilities.HasStatus(targetUnit, AbilityStatusFlags.MagicImmune))
            return false;
        if (damageKind == AbilityDamageKind.Physical &&
            (uint)targetUnit < (uint)Units.Count && Units.Alive[targetUnit])
        {
            damage = CombatDamageResolver.Resolve(
                new CombatWeaponDamageSnapshot(
                    damage, 1, CombatAttribute.None, 0f, 0, 0f, 0f),
                new CombatDefenseSnapshot(
                    EffectiveUnitArmor(targetUnit),
                    Combat.Attributes[targetUnit]),
                Combat.Health[targetUnit]).TotalDamage;
        }
        return DamageUnit(targetUnit, damage);
    }

    bool IAbilityRuntimeWorld.AbilityHealUnit(int targetUnit, float amount)
    {
        if ((uint)targetUnit >= (uint)Units.Count || !Units.Alive[targetUnit] ||
            !float.IsFinite(amount) || amount <= 0f)
            return false;
        Combat.Health[targetUnit] = MathF.Min(
            Combat.MaximumHealth[targetUnit], Combat.Health[targetUnit] + amount);
        return true;
    }

    bool IAbilityRuntimeWorld.AbilityDamageBuilding(
        int sourceUnit,
        GameplayBuildingId building,
        float damage,
        AbilityDamageKind damageKind)
    {
        if (damageKind == AbilityDamageKind.None ||
            !Construction.IsAlive(building))
            return false;
        if (damageKind == AbilityDamageKind.Physical)
        {
            var snapshot = Construction.Observe(building);
            damage = CombatDamageResolver.Resolve(
                new CombatWeaponDamageSnapshot(
                    damage, 1, CombatAttribute.None, 0f, 0, 0f, 0f),
                new CombatDefenseSnapshot(
                    EffectiveBuildingArmor(snapshot), snapshot.Type.Attributes),
                snapshot.Health).TotalDamage;
        }
        return DamageBuilding(building, damage);
    }

    bool IAbilityRuntimeWorld.AbilityReviveUnit(int unit, float healthFraction)
    {
        if ((uint)unit >= (uint)Units.Count || Units.Alive[unit] ||
            !float.IsFinite(healthFraction) || healthFraction <= 0f)
            return false;
        Units.Alive[unit] = true;
        Combat.Health[unit] = MathF.Max(
            1f, Combat.MaximumHealth[unit] * MathF.Min(1f, healthFraction));
        Units.PreviousPositions[unit] = Units.Positions[unit];
        Units.Modes[unit] = UnitMoveMode.Idle;
        Combat.SetCommand(unit, UnitCommandIntent.Stop, Units.Positions[unit]);
        return true;
    }

    void IAbilityRuntimeWorld.AbilitySetUnitOwner(int unit, int playerId)
    {
        if ((uint)unit < (uint)Units.Count && Units.Alive[unit])
            Combat.Teams[unit] = playerId;
    }

    void IAbilityRuntimeWorld.AbilityTeleportUnit(int unit, Vector2 position)
    {
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit]) return;
        var target = World.Bounds.Inset(Units.Radii[unit] + 2f).Clamp(position);
        Units.Positions[unit] = target;
        Units.PreviousPositions[unit] = target;
        Units.SlotTargets[unit] = target;
        Units.MoveGoals[unit] = target;
        Units.Paths[unit] = null;
        Units.RouteWaypoints[unit] = [];
        Units.PathPending[unit] = false;
        Units.Velocities[unit] = Vector2.Zero;
        Units.PreferredVelocities[unit] = Vector2.Zero;
        Units.NextVelocities[unit] = Vector2.Zero;
        Units.CommandVersions[unit]++;
    }

    void IAbilityRuntimeWorld.AbilityTeleportUnits(
        int[] units,
        Vector2 position)
    {
        if (units.Length == 0) return;
        var valid = units.Where(unit =>
                (uint)unit < (uint)Units.Count && Units.Alive[unit])
            .Distinct()
            .Order()
            .ToArray();
        if (valid.Length == 0) return;
        Dictionary<int, Vector2> slots;
        try
        {
            slots = _slotAllocator.Allocate(Units, valid, position);
        }
        catch (InvalidOperationException)
        {
            slots = valid.ToDictionary(unit => unit, _ => position);
        }
        foreach (var unit in valid)
            ((IAbilityRuntimeWorld)this).AbilityTeleportUnit(
                unit, slots[unit]);
    }

    int IAbilityRuntimeWorld.AbilitySpawnSummon(
        int sourceUnit,
        int playerId,
        Vector2 position,
        in AbilitySummonProfile summon) => AddUnit(
        World.Bounds.Inset(summon.Movement.PhysicalRadius + 2f).Clamp(position),
        summon.Movement,
        playerId,
        summon.Combat,
        summon.Perception);

    bool IAbilityRuntimeWorld.AbilityTryFindNearestOwnedBuilding(
        int unit,
        BuildingFunctionKind function,
        out GameplayBuildingId building)
    {
        building = new GameplayBuildingId(-1);
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit])
            return false;
        var owner = Combat.Teams[unit];
        var bestDistance = float.PositiveInfinity;
        for (var index = 0; index < Construction.SlotCount; index++)
        {
            var candidateId = new GameplayBuildingId(index);
            if (!Construction.IsAlive(candidateId)) continue;
            var candidate = Construction.Observe(candidateId);
            if (candidate.PlayerId != owner ||
                candidate.State != BuildingLifecycleState.Completed ||
                candidate.Type.Function != function)
                continue;
            var closest = candidate.Bounds.Clamp(Units.Positions[unit]);
            var distance = Vector2.DistanceSquared(
                Units.Positions[unit], closest);
            if (distance < bestDistance ||
                distance == bestDistance && candidateId.Value < building.Value)
            {
                building = candidateId;
                bestDistance = distance;
            }
        }
        return building.Value >= 0;
    }

    void IAbilityRuntimeWorld.AbilityMoveUnitToBuilding(
        int unit,
        GameplayBuildingId building)
    {
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit] ||
            !Construction.IsAlive(building))
            return;
        Span<int> single = stackalloc int[1];
        single[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            Economy.Cancel(single);
            Construction.InterruptBuilders(single);
            CommandQueues.ClearPending(unit);
            CommandQueues.HasActiveOrders[unit] = false;
            ExecuteBuildingBoundaryMove(single, building.Value);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    bool IAbilityRuntimeWorld.AbilityUnitTouchesBuilding(
        int unit,
        GameplayBuildingId building) =>
        (uint)unit < (uint)Units.Count && Units.Alive[unit] &&
        Construction.IsAlive(building) &&
        InteractionGeometry.DiscTouchesRectangle(
            Units.Positions[unit], Units.Radii[unit],
            Construction.Observe(building).Bounds);

    bool IAbilityRuntimeWorld.AbilityUnitMovingToBuilding(
        int unit,
        GameplayBuildingId building) =>
        (uint)unit < (uint)Units.Count && Units.Alive[unit] &&
        Units.MovementGoalKinds[unit] == UnitMovementGoalKind.BuildingBoundary &&
        Units.MovementGoalTargetIds[unit] == building.Value &&
        Units.MovementLegResults[unit] == UnitMovementLegResult.InProgress;

    bool IAbilityRuntimeWorld.AbilityApplyUnitProfile(
        int unit,
        in UnitTypeProfile profile)
    {
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit]) return false;
        if (!Economy.SetNormalWorkerEnabled(unit, profile.IsWorker)) return false;
        Units.ApplyMovementProfile(unit, profile.Movement);
        Combat.ApplyProfile(unit, profile.Combat, profile.Perception);
        Span<int> single = stackalloc int[1];
        single[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            ExecuteStop(single);
            CommandQueues.ClearPending(unit);
            CommandQueues.HasActiveOrders[unit] = false;
        }
        finally
        {
            _issuingSystemOrder = false;
        }
        return true;
    }

    void IAbilityRuntimeWorld.AbilityPrepareCaster(int unit)
    {
        Span<int> caster = stackalloc int[1];
        caster[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            ExecuteStop(caster);
            Economy.Cancel(caster);
            Construction.InterruptBuilders(caster);
            CommandQueues.ClearPending(unit);
            CommandQueues.HasActiveOrders[unit] = false;
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    void IAbilityRuntimeWorld.AbilityKillSummon(int unit)
    {
        if ((uint)unit >= (uint)Units.Count || !Units.Alive[unit]) return;
        Combat.Health[unit] = 0f;
        Units.Alive[unit] = false;
        Combat.CommandIntents[unit] = UnitCommandIntent.None;
        Combat.Phases[unit] = CombatPhase.None;
        Combat.TargetUnits[unit] = -1;
        Combat.TargetBuildings[unit] = -1;
        Combat.TargetKinds[unit] = CombatTargetKind.None;
        ((ICombatMovementDriver)this).Kill(unit);
    }

    bool ICombatMovementDriver.IsBuildingTargetValid(
        int attacker,
        GameplayBuildingId building)
    {
        if ((uint)attacker >= (uint)Units.Count || !Units.Alive[attacker] ||
            !IsHostileBuilding(building, Combat.Teams[attacker]))
        {
            return false;
        }
        var bounds = Construction.Observe(building).Bounds;
        var nearestFootprintPoint = bounds.Clamp(Units.Positions[attacker]);
        return Visibility.IsVisible(
            Combat.Teams[attacker], nearestFootprintPoint);
    }

    bool ICombatMovementDriver.IsBuildingAlive(GameplayBuildingId building) =>
        Construction.IsAlive(building);

    SimRect ICombatMovementDriver.BuildingTargetBounds(
        GameplayBuildingId building) => Construction.Observe(building).Bounds;

    GameplayBuildingId ICombatMovementDriver.FindAutoBuildingTarget(
        int attacker,
        float acquisitionRange)
    {
        var best = new GameplayBuildingId(-1);
        var bestDistance = float.PositiveInfinity;
        var playerId = Combat.Teams[attacker];
        var position = Units.Positions[attacker];
        var buildings = CombatBuildingOverviewForCurrentTick();
        for (var index = 0; index < buildings.Length; index++)
        {
            var building = buildings[index];
            if (building.IsTerminal ||
                !IsHostileBuilding(building.Id, playerId))
                continue;
            var nearest = building.Bounds.Clamp(position);
            if (!Visibility.IsVisible(playerId, nearest))
                continue;
            var distance = building.Bounds.DistanceSquaredTo(position);
            var allowed = acquisitionRange + Units.Radii[attacker];
            if (distance > allowed * allowed ||
                distance > bestDistance ||
                distance == bestDistance && building.Id.Value >= best.Value)
                continue;
            best = building.Id;
            bestDistance = distance;
        }
        return best;
    }

    bool ICombatMovementDriver.TryResolveBuildingChaseTarget(
        int attacker,
        GameplayBuildingId building,
        out Vector2 target)
    {
        target = default;
        if (!Construction.IsAlive(building))
            return false;
        var snapshot = Construction.Observe(building);
        var approach = ConstructionAccessPointResolver.Resolve(
            World,
            _pathProvider,
            snapshot.Bounds,
            Units.Positions[attacker],
            Units.Radii[attacker],
            Units.NavigationRadii[attacker],
            Combat.AttackRanges[attacker]);
        target = approach.Target;
        return approach.Found;
    }

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

    GameplayBuildingSnapshot[]
        ICombatMovementDriver.CombatBuildingOverview() =>
        CombatBuildingOverviewForCurrentTick();

    bool ICombatMovementDriver.IsCombatObjectTargetValid(
        int attacker,
        CombatObjectId target)
    {
        if (!CombatObjects.IsAlive(target)) return false;
        if (attacker < 0) return true;
        if ((uint)attacker >= (uint)Units.Count || !Units.Alive[attacker])
            return false;
        var value = CombatObjects.Observe(target);
        if (value.Profile.OwnerTeam >= 0 &&
            !Diplomacy.IsEnemy(
                Combat.Teams[attacker], value.Profile.OwnerTeam))
            return false;
        return Visibility.IsVisible(
            Combat.Teams[attacker], value.Position);
    }

    CombatObjectSnapshot ICombatMovementDriver.CombatObject(
        CombatObjectId target) => CombatObjects.Observe(target);

    bool ICombatMovementDriver.TryResolveCombatObjectChaseTarget(
        int attacker,
        CombatObjectId target,
        out Vector2 position)
    {
        position = default;
        if (!CombatObjects.IsAlive(target)) return false;
        var value = CombatObjects.Observe(target);
        var approach = ConstructionAccessPointResolver.Resolve(
            World,
            _pathProvider,
            value.Bounds,
            Units.Positions[attacker],
            Units.Radii[attacker],
            Units.NavigationRadii[attacker],
            Combat.AttackRanges[attacker]);
        position = approach.Target;
        return approach.Found;
    }

    CombatObjectDamageResult ICombatMovementDriver.DamageCombatObject(
        CombatObjectId target,
        CombatWeaponDamageSnapshot weapon)
    {
        if (!CombatObjects.IsAlive(target)) return default;
        var value = CombatObjects.Observe(target);
        if (value.Profile.LinkedResourceNodeId < 0)
        {
            var result = CombatObjects.ApplyDamage(target, weapon);
            if (result.Destroyed)
                RemoveCombatObjectFootprint(value.Profile);
            return result;
        }

        var resolved = CombatDamageResolver.Resolve(
            weapon,
            new CombatDefenseSnapshot(
                value.Profile.Armor,
                value.Profile.Attributes,
                value.Profile.ArmorType),
            value.Health);
        var requested = Math.Max(
            1,
            (int)MathF.Round(
                resolved.TotalDamage,
                MidpointRounding.AwayFromZero));
        var applied = Economy.ApplyResourceNodeDamage(
            new EconomyResourceNodeId(value.Profile.LinkedResourceNodeId),
            requested);
        var remaining = Economy.ResourceNodeRemaining(
            new EconomyResourceNodeId(value.Profile.LinkedResourceNodeId));
        CombatObjects.SetHealth(target, remaining);
        if (remaining <= 0)
            RemoveCombatObjectFootprint(value.Profile);
        return new CombatObjectDamageResult(
            applied > 0,
            applied,
            remaining,
            resolved.DamagePerAttack,
            resolved.AttacksApplied,
            resolved.BonusApplied,
            remaining <= 0);
    }

    CombatObjectSnapshot[] ICombatMovementDriver.CombatObjectOverview() =>
        CombatObjectOverviewForCurrentTick();

    private GameplayBuildingSnapshot[] CombatBuildingOverviewForCurrentTick()
    {
        if (_combatBuildingOverviewTick == Metrics.Tick)
            return _combatBuildingOverview;
        _combatBuildingOverview = Construction.CreateOverview();
        _combatBuildingOverviewTick = Metrics.Tick;
        return _combatBuildingOverview;
    }

    private CombatObjectSnapshot[] CombatObjectOverviewForCurrentTick()
    {
        if (_combatObjectOverviewTick == Metrics.Tick)
            return _combatObjectOverview;
        _combatObjectOverview = CombatObjects.CreateOverview();
        _combatObjectOverviewTick = Metrics.Tick;
        return _combatObjectOverview;
    }

    int ICombatMovementDriver.WeaponUpgradeLevel(int team) =>
        CombatWeaponUpgradeTechnologyId < 0
            ? 0
            : Technology.Level(team, CombatWeaponUpgradeTechnologyId);

    int ICombatMovementDriver.TechnologyLevel(int team, int technologyId) =>
        technologyId < 0 ? 0 : Technology.Level(team, technologyId);

    private CombatWeaponDamageSnapshot CombatWeapon(int attacker)
    {
        var technologyId = Combat.DamageUpgradeTechnologyIds[attacker];
        var level = technologyId >= 0
            ? Technology.Level(Combat.Teams[attacker], technologyId)
            : ((ICombatMovementDriver)this).WeaponUpgradeLevel(
                Combat.Teams[attacker]);
        return new CombatWeaponDamageSnapshot(
            Combat.AttackDamage[attacker], Combat.AttacksPerVolley[attacker],
            Combat.BonusVs[attacker], Combat.BonusDamage[attacker], level,
            Combat.BaseUpgradeDamage[attacker],
            Combat.BonusUpgradeDamage[attacker],
            Combat.AttackTypes[attacker], Combat.WeaponAreas[attacker]);
    }

    private CombatDefenseSnapshot BuildingDefense(GameplayBuildingSnapshot building)
        => new(EffectiveBuildingArmor(building), building.Type.Attributes,
            building.Type.ArmorType);

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
        var targetKind = Combat.TargetKinds[unit];
        var targetBounds = targetKind switch
        {
            CombatTargetKind.Building when Construction.IsAlive(
                new GameplayBuildingId(Combat.TargetBuildings[unit])) =>
                Construction.Observe(new GameplayBuildingId(
                    Combat.TargetBuildings[unit])).Bounds,
            CombatTargetKind.Object when CombatObjects.IsAlive(
                new CombatObjectId(Combat.TargetBuildings[unit])) =>
                CombatObjects.Observe(new CombatObjectId(
                    Combat.TargetBuildings[unit])).Bounds,
            _ => default
        };
        var movementGoalKind = targetKind switch
        {
            CombatTargetKind.Building or CombatTargetKind.Object =>
                UnitMovementGoalKind.AttackRange,
            CombatTargetKind.Unit => UnitMovementGoalKind.UnitBody,
            _ => UnitMovementGoalKind.GroundPoint
        };
        SetMovementGoal(
            unit,
            movementGoalKind,
            targetBounds,
            targetKind == CombatTargetKind.None
                ? 0f
                : Combat.AttackRanges[unit],
            targetKind is CombatTargetKind.Building or CombatTargetKind.Object
                ? Combat.TargetBuildings[unit]
                : targetKind == CombatTargetKind.Unit
                    ? Combat.TargetUnits[unit]
                    : -1,
            UnitMovementLegResult.InProgress);
        if (Combat.CommandIntents[unit] != UnitCommandIntent.AttackMove)
        {
            Units.MovementGroupIds[unit] = 0;
            Units.MovementGroupSizes[unit] = 1;
        }
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
        Units.DestinationOverflowed[unit] = false;
        QueueNavigationReplan(unit, countInvalidation: false);
    }

    private void SetCombatRetreatDestination(int unit, Vector2 target)
    {
        var clamped = World.Bounds.Inset(Units.Radii[unit] + 2f).Clamp(target);
        Units.SlotTargets[unit] = clamped;
        Units.MoveGoals[unit] = clamped;
        SetMovementGoal(
            unit,
            UnitMovementGoalKind.GroundPoint,
            default,
            0f,
            -1,
            UnitMovementLegResult.InProgress);
        Units.MovementGroupIds[unit] = 0;
        Units.MovementGroupSizes[unit] = 1;
        Units.DestinationYieldPhases[unit] = DestinationYieldPhase.None;
        Units.DestinationOverflowed[unit] = false;
        QueueNavigationReplan(unit, countInvalidation: false);
    }

    private float ArrivalStopDistance(int unit)
    {
        if (Units.MovementGoalKinds[unit] is
                UnitMovementGoalKind.BuildingBoundary or
                UnitMovementGoalKind.ResourceBoundary or
                UnitMovementGoalKind.DropOffBoundary or
                UnitMovementGoalKind.AttackRange or
                UnitMovementGoalKind.UnitBody or
                UnitMovementGoalKind.FollowRange or
                UnitMovementGoalKind.ConstructionEvacuation)
        {
            return InteractionGeometry.NumericTolerance(
                       Units.Positions[unit], Units.MovementGoalBounds[unit]) *
                   0.25f;
        }
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

    private static double ElapsedMilliseconds(
        long startTimestamp,
        long endTimestamp) =>
        (endTimestamp - startTimestamp) * 1_000d / Stopwatch.Frequency;

    private void MoveEconomyWorker(int unit, Vector2 target)
    {
        Span<int> worker = stackalloc int[1];
        worker[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            var state = Economy.Worker(unit);
            var goalKind = UnitMovementGoalKind.GroundPoint;
            var goalBounds = default(SimRect);
            var goalRadius = 0f;
            var goalTargetId = -1;
            if (state.State == WorkerEconomyState.GoingToResource &&
                state.TargetNode.Value >= 0)
            {
                var node = Economy.ObserveResourceNode(state.TargetNode);
                var navigationApproach = node.InteractionHalfExtents != Vector2.Zero
                    ? ConstructionAccessPointResolver.ResolveAvailableEndpoint(
                        World,
                        new SimRect(
                            node.Position - node.InteractionHalfExtents,
                            node.Position + node.InteractionHalfExtents),
                        Units.Positions[unit],
                        Units.NavigationRadii[unit],
                        interactionPadding: 0f)
                    : ConstructionAccessPointResolver.ResolveAvailableCircleEndpoint(
                        World,
                        node.Position,
                        node.InteractionRadius,
                        Units.Positions[unit],
                        Units.NavigationRadii[unit]);
                var approach = navigationApproach;
                var surfaceEntryDistance = MathF.Max(
                    1f,
                    Units.NavigationRadii[unit] - Units.Radii[unit] + 0.5f);
                if (navigationApproach.Found &&
                    Vector2.DistanceSquared(
                        Units.Positions[unit], navigationApproach.Target) <=
                    surfaceEntryDistance * surfaceEntryDistance)
                {
                    // The ordinary route ends at navigation clearance. The
                    // final sub-leg deliberately uses the physical body radius
                    // so gathering still begins only at real geometry contact.
                    approach = node.InteractionHalfExtents != Vector2.Zero
                        ? ConstructionAccessPointResolver.ResolveAvailableEndpoint(
                            World,
                            new SimRect(
                                node.Position - node.InteractionHalfExtents,
                                node.Position + node.InteractionHalfExtents),
                            Units.Positions[unit],
                            Units.Radii[unit],
                            interactionPadding: 0f)
                        : ConstructionAccessPointResolver.ResolveAvailableCircleEndpoint(
                            World,
                            node.Position,
                            node.InteractionRadius,
                            Units.Positions[unit],
                            Units.Radii[unit]);
                }
                goalKind = UnitMovementGoalKind.ResourceBoundary;
                goalBounds = node.InteractionHalfExtents != Vector2.Zero
                    ? new SimRect(
                        node.Position - node.InteractionHalfExtents,
                        node.Position + node.InteractionHalfExtents)
                    : new SimRect(node.Position, node.Position);
                goalRadius = node.InteractionRadius;
                goalTargetId = state.TargetNode.Value;
                if (!approach.Found)
                {
                    SetImmediateMovementFailure(
                        unit, goalKind, goalBounds, goalTargetId,
                        UnitMovementLegResult.Unreachable);
                    return;
                }
                target = approach.Target;
            }
            else if (state.State == WorkerEconomyState.ReturningCargo)
            {
                var retryDropOff =
                    Units.MovementGoalKinds[unit] ==
                        UnitMovementGoalKind.DropOffBoundary &&
                    Units.MovementLegResults[unit] is
                        UnitMovementLegResult.Unreachable or
                        UnitMovementLegResult.SettledShort
                        ? Units.MovementGoalTargetIds[unit]
                        : -1;
                var excludedTarget = retryDropOff >= 0
                    ? Units.MoveGoals[unit]
                    : (Vector2?)null;
                var navigationApproach = ResolveReachableDropOff(
                    state.PlayerId,
                    state.CargoKind,
                    Units.Positions[unit],
                    Units.NavigationRadii[unit],
                    retryDropOff,
                    excludedTarget);
                var approach = navigationApproach;
                var surfaceEntryDistance = MathF.Max(
                    1f,
                    Units.NavigationRadii[unit] - Units.Radii[unit] + 0.5f);
                if (navigationApproach.Found &&
                    Vector2.DistanceSquared(
                        Units.Positions[unit], navigationApproach.Target) <=
                    surfaceEntryDistance * surfaceEntryDistance)
                {
                    // Keep ordinary pathfinding outside the building's
                    // navigation clearance. Only the final short sub-leg uses
                    // the physical body radius so delivery still requires real
                    // contact with the drop-off geometry.
                    approach = ResolveReachableDropOff(
                        state.PlayerId,
                        state.CargoKind,
                        Units.Positions[unit],
                        Units.Radii[unit],
                        navigationApproach.Id.Value,
                        excludedTarget: null);
                }
                goalKind = UnitMovementGoalKind.DropOffBoundary;
                goalBounds = approach.Found
                    ? approach.HalfExtents != Vector2.Zero
                        ? new SimRect(
                            approach.Center - approach.HalfExtents,
                            approach.Center + approach.HalfExtents)
                        : new SimRect(approach.Center, approach.Center)
                    : default;
                goalRadius = approach.InteractionRadius;
                goalTargetId = approach.Id.Value;
                if (!approach.Found)
                {
                    SetImmediateMovementFailure(
                        unit, goalKind, goalBounds, goalTargetId,
                        UnitMovementLegResult.Unreachable);
                    return;
                }
                target = approach.Target;
            }
            ExecuteMoveOrder(
                worker, target, UnitCommandIntent.Move,
                exactDestination: true,
                goalKind,
                goalBounds,
                goalRadius,
                goalTargetId);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private EconomyDropOffApproachSnapshot ResolveReachableDropOff(
        int playerId,
        EconomyResourceKind kind,
        Vector2 origin,
        float unitRadius) =>
        ResolveReachableDropOff(
            playerId, kind, origin, unitRadius,
            preferredDropOff: -1, excludedTarget: null);

    private EconomyDropOffApproachSnapshot ResolveReachableDropOff(
        int playerId,
        EconomyResourceKind kind,
        Vector2 origin,
        float unitRadius,
        int preferredDropOff,
        Vector2? excludedTarget)
    {
        var candidates = Economy.CreateDropOffApproaches(
            playerId, kind, origin, unitRadius);
        var missing = new EconomyDropOffApproachSnapshot(
            new EconomyDropOffId(-1),
            default,
            default,
            default,
            0f,
            default,
            float.PositiveInfinity);
        Span<bool> tried = candidates.Length <= 64
            ? stackalloc bool[candidates.Length]
            : new bool[candidates.Length];
        for (var attempt = 0; attempt < candidates.Length; attempt++)
        {
            var selected = -1;
            for (var index = 0; index < candidates.Length; index++)
            {
                if (tried[index]) continue;
                if (selected < 0 ||
                    DropOffCandidatePrecedes(
                        candidates[index], candidates[selected],
                        preferredDropOff))
                {
                    selected = index;
                }
            }
            if (selected < 0) break;
            tried[selected] = true;
            var candidate = candidates[selected];
            var endpoint = candidate.HalfExtents != Vector2.Zero
                ? ConstructionAccessPointResolver.ResolveAvailableEndpoint(
                    World,
                    new SimRect(
                        candidate.Center - candidate.HalfExtents,
                        candidate.Center + candidate.HalfExtents),
                    origin,
                    unitRadius,
                    excludedTarget: excludedTarget)
                : ConstructionAccessPointResolver.ResolveAvailableCircleEndpoint(
                    World,
                    candidate.Center,
                    candidate.InteractionRadius,
                    origin,
                    unitRadius,
                    excludedTarget);
            if (endpoint.Found)
                return candidate with { Target = endpoint.Target };
        }
        // Returning an obstructed projection here used to turn one invalid
        // boundary point into an endless path-failure/reissue loop. No endpoint
        // is preferable: the worker waits for a genuinely reachable drop-off.
        return missing;
    }

    private static bool DropOffCandidatePrecedes(
        EconomyDropOffApproachSnapshot candidate,
        EconomyDropOffApproachSnapshot current,
        int preferredDropOff)
    {
        var candidatePriority = candidate.Id.Value == preferredDropOff ? 0 : 1;
        var currentPriority = current.Id.Value == preferredDropOff ? 0 : 1;
        return candidatePriority < currentPriority ||
               candidatePriority == currentPriority &&
               (candidate.DistanceSquared < current.DistanceSquared ||
                candidate.DistanceSquared == current.DistanceSquared &&
                candidate.Id.Value < current.Id.Value);
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
            FinishMovementLeg(unit, UnitMovementLegResult.Reached);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private void FinishEconomyDropOffMovement(
        int unit,
        UnitMovementLegResult result)
    {
        var hadDropOffGoal = Units.MovementGoalKinds[unit] ==
                             UnitMovementGoalKind.DropOffBoundary;
        SetImmediateMovementFailure(
            unit,
            UnitMovementGoalKind.DropOffBoundary,
            hadDropOffGoal
                ? Units.MovementGoalBounds[unit]
                : default,
            hadDropOffGoal
                ? Units.MovementGoalTargetIds[unit]
                : -1,
            result);
    }

    private void MoveConstructionWorker(int unit, Vector2 target)
    {
        Span<int> worker = stackalloc int[1];
        worker[0] = unit;
        _issuingSystemOrder = true;
        try
        {
            if (Construction.TryObserveAssignedBuilding(unit, out var assigned))
            {
                var approach = ResolveConstructionAccessPoint(
                    assigned.Bounds,
                    unit,
                    assigned.ReservationId,
                    assigned.RefineryNode);
                if (!approach.Found)
                {
                    SetImmediateMovementFailure(
                        unit,
                        UnitMovementGoalKind.BuildingBoundary,
                        assigned.Bounds,
                        assigned.Id.Value,
                        UnitMovementLegResult.Unreachable);
                    Construction.MarkBuilderApproachUnavailable(unit);
                    return;
                }
                target = approach.Target;
                Construction.TryUpdateBuilderAccessPoint(unit, target);
            }
            ExecuteMoveOrder(
                worker, target, UnitCommandIntent.Move,
                exactDestination: true);
            if (Construction.TryObserveAssignedBuilding(unit, out var building))
            {
                SetMovementGoal(
                    unit,
                    UnitMovementGoalKind.BuildingBoundary,
                    building.Bounds,
                    building.Type.PlacementProfile.UnitPadding,
                    building.Id.Value,
                    UnitMovementLegResult.InProgress);
            }
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
            FinishMovementLeg(unit, UnitMovementLegResult.Reached);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private HardFootprintCommitResult CommitConstructionFootprint(
        ConstructionReservationId reservationId,
        SimRect footprint,
        BuildingPlacementRules rules,
        int builderUnit)
    {
        _issuingConstructionWorldMutation = true;
        try
        {
            return TryCommitHardFootprint(
                footprint, rules, reservationId, builderUnit);
        }
        finally
        {
            _issuingConstructionWorldMutation = false;
        }
    }

    private InteractionApproachResolution ResolveConstructionAccessPoint(
        SimRect bounds,
        int builderUnit,
        ConstructionReservationId ignoreReservation,
        EconomyResourceNodeId ignoredResourceNode) =>
        ConstructionAccessPointResolver.Resolve(
            World,
            _pathProvider,
            bounds,
            Units.Positions[builderUnit],
            Units.Radii[builderUnit],
            Units.NavigationRadii[builderUnit],
            interactionPadding: 0f,
            (center, radius) => Construction.Reservations.IsDiscFree(
                                    center, radius, ignoreReservation) &&
                                Economy.IsDiscClearOfResources(
                                    center, radius, ignoredResourceNode));

    private void SetImmediateMovementFailure(
        int unit,
        UnitMovementGoalKind kind,
        SimRect bounds,
        int targetId,
        UnitMovementLegResult result)
    {
        Units.Paths[unit] = null;
        Units.RouteWaypoints[unit] = [];
        Units.PathPending[unit] = false;
        Units.Modes[unit] = UnitMoveMode.Idle;
        Units.Velocities[unit] = Vector2.Zero;
        Units.PreferredVelocities[unit] = Vector2.Zero;
        Units.NextVelocities[unit] = Vector2.Zero;
        SetMovementGoal(unit, kind, bounds, 0f, targetId, result);
        FinishMovementLeg(unit, result);
    }

    private void FinishMovementLeg(
        int unit,
        UnitMovementLegResult result)
    {
        Units.MovementLegResults[unit] = result;
        GameplayEvents.Publish(
            Metrics.Tick,
            GameplayEventKind.MovementLegFinished,
            unit,
            movementGoalKind: Units.MovementGoalKinds[unit],
            movementResult: result,
            worldPosition: Units.Positions[unit]);
    }

    private void SetMovementGoal(
        int unit,
        UnitMovementGoalKind kind,
        SimRect bounds,
        float radius,
        int targetId,
        UnitMovementLegResult result)
    {
        Units.MovementGoalKinds[unit] = kind;
        Units.MovementGoalBounds[unit] = bounds;
        Units.MovementGoalRadii[unit] = radius;
        Units.MovementGoalTargetIds[unit] = targetId;
        Units.MovementLegResults[unit] = result;
    }

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
            GameplayEvents.Publish(
                Metrics.Tick,
                GameplayEventKind.ConstructionDisplacementStarted,
                assignment.Unit,
                buildingId.Value,
                worldPosition: Units.Positions[assignment.Unit]);
            MoveConstructionEvacuationUnit(
                assignment.Unit, assignment.Target, footprint);
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
                if (Units.MovementGoalKinds[unit] !=
                        UnitMovementGoalKind.ConstructionEvacuation ||
                    Units.MovementLegResults[unit] !=
                        UnitMovementLegResult.InProgress ||
                    Vector2.DistanceSquared(Units.MoveGoals[unit], target) > 1f)
                {
                    MoveConstructionEvacuationUnit(
                        unit,
                        target,
                        CommandQueues.ConstructionEvacuationFootprints[unit]);
                }
                continue;
            }

            ClearConstructionEvacuation(unit);
            RestoreDisplacedUnitOrder(unit);
        }
    }

    private void UpdateProductionEvacuations()
    {
        Span<int> single = stackalloc int[1];
        for (var unit = 0; unit < Units.Count; unit++)
        {
            if (!CommandQueues.ProductionEvacuationActive[unit])
                continue;
            if (!Units.Alive[unit])
            {
                ClearProductionEvacuation(unit);
                continue;
            }
            var blockedArea =
                CommandQueues.ProductionEvacuationFootprints[unit]
                    .Expanded(Units.Radii[unit]);
            if (blockedArea.Contains(Units.Positions[unit]) &&
                Units.MovementLegResults[unit] ==
                    UnitMovementLegResult.InProgress)
            {
                continue;
            }

            ClearProductionEvacuation(unit);
            RestoreDisplacedUnitOrder(unit);
        }
    }

    private void MoveConstructionEvacuationUnit(
        int unit,
        Vector2 target,
        SimRect footprint)
    {
        Span<int> single = stackalloc int[1];
        single[0] = unit;
        ExecuteMoveOrder(
            single,
            target,
            UnitCommandIntent.Move,
            exactDestination: true,
            goalKind: UnitMovementGoalKind.ConstructionEvacuation,
            goalBounds: footprint);
    }

    private void RestoreDisplacedUnitOrder(int unit)
    {
        Span<int> single = stackalloc int[1];
        single[0] = unit;
        var economy = Economy.IsGatherer(unit)
            ? Economy.Worker(unit)
            : default;
        if (Economy.IsGatherer(unit) && economy.State is not
                (WorkerEconomyState.None or WorkerEconomyState.Idle))
        {
            MoveEconomyWorker(unit, Units.Positions[unit]);
            return;
        }
        if (Construction.IsAssignedBuilder(unit))
        {
            MoveConstructionWorker(unit, Units.Positions[unit]);
            return;
        }
        if (!CommandQueues.HasActiveOrders[unit])
        {
            ExecuteStop(single);
            return;
        }

        var order = new UnitOrder(
            CommandQueues.ActiveKinds[unit],
            CommandQueues.ActivePositions[unit],
            CommandQueues.ActiveTargetUnits[unit],
            CommandQueues.ActiveTargetBuildings[unit],
            CommandQueues.ActiveTargetResourceNodes[unit],
            CommandQueues.ActiveSequenceIds[unit]);
        _issuingSystemOrder = true;
        try
        {
            ExecuteOrderGroup(
                single,
                order,
                CommandQueues.ActiveOrdersWereQueued[unit]);
        }
        finally
        {
            _issuingSystemOrder = false;
        }
    }

    private void ClearConstructionEvacuation(int unit)
    {
        if (CommandQueues.ConstructionEvacuationActive[unit])
        {
            GameplayEvents.Publish(
                Metrics.Tick,
                GameplayEventKind.ConstructionDisplacementFinished,
                unit,
                CommandQueues.ConstructionEvacuationBuildings[unit],
                worldPosition: Units.Positions[unit]);
        }
        CommandQueues.ConstructionEvacuationActive[unit] = false;
        CommandQueues.ConstructionEvacuationBuildings[unit] = -1;
        CommandQueues.ConstructionEvacuationTargets[unit] = Vector2.Zero;
        CommandQueues.ConstructionEvacuationFootprints[unit] = default;
    }

    private void ClearProductionEvacuation(int unit)
    {
        if (CommandQueues.ProductionEvacuationActive[unit])
        {
            GameplayEvents.Publish(
                Metrics.Tick,
                GameplayEventKind.ProductionDisplacementFinished,
                unit,
                worldPosition: Units.Positions[unit]);
        }
        CommandQueues.ProductionEvacuationActive[unit] = false;
        CommandQueues.ProductionEvacuationTargets[unit] = Vector2.Zero;
        CommandQueues.ProductionEvacuationFootprints[unit] = default;
    }

    private bool IsHostileBuilding(
        GameplayBuildingId building,
        int viewerPlayerId) =>
        Construction.IsAlive(building) &&
        Diplomacy.IsEnemy(
            viewerPlayerId, Construction.Observe(building).PlayerId);


}
