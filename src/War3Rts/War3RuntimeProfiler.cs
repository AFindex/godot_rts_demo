using Godot;
using RtsDemo.AI;
using RtsDemo.Simulation;

namespace War3Rts;

/// <summary>
/// Opt-in fixed-scene profiler used by the War3 skirmish runtime. It is kept
/// out of the normal execution path unless profiling or the automated
/// skirmish stress mode is explicitly requested.
/// </summary>
internal sealed class War3RuntimeProfiler
{
    private const double DefaultWarmupSeconds = 5d;
    private const double DefaultSampleSeconds = 15d;
    private const double DefaultSpikeMilliseconds = 25d;
    private readonly ulong _warmupUsec;
    private readonly ulong _sampleUsec;
    private readonly Series _frame = new();
    private readonly Series _physics = new();
    private readonly Series _stressDriver = new();
    private readonly Series _stressBuildingLifecycle = new();
    private readonly Series _stressUnitRespawn = new();
    private readonly Series _stressCombatOrders = new();
    private readonly Series _stressConstructionIssue = new();
    private readonly Series _stressAllocated = new();
    private readonly Series _automatedSkirmishTick = new();
    private readonly Series _automatedSkirmishBankTopUp = new();
    private readonly Series _automatedSkirmishSupportConstruction = new();
    private readonly Series _automatedSkirmishAi = new();
    private readonly Series _automatedSkirmishStatus = new();
    private readonly Series _automatedSkirmishSimulation = new();
    private readonly Series _automatedSkirmishAllocated = new();
    private readonly Series _automatedSkirmishSpikeBurstInterval = new();
    private readonly Series _ai = new();
    private readonly Series _simulation = new();
    private readonly Series _selectionHud = new();
    private readonly Series _presenter = new();
    private readonly Series _buildPreview = new();
    private readonly Series _physicsAllocated = new();
    private readonly Series _presenterAllocated = new();
    private readonly Series _presenterUnits = new();
    private readonly Series _presenterBuildings = new();
    private readonly Series _presenterResources = new();
    private readonly Series _presenterProjectiles = new();
    private readonly Series _presenterTransients = new();
    private readonly Series _presenterUnitsAllocated = new();
    private readonly Series _presenterBuildingsAllocated = new();
    private readonly Series _presenterResourcesAllocated = new();
    private readonly Series _presenterProjectilesAllocated = new();
    private readonly Series _presenterTransientsAllocated = new();
    private readonly Series _objects = new();
    private readonly Series _drawCalls = new();
    private readonly Series _primitives = new();
    private readonly Series _simTotal = new();
    private readonly Series _simEconomy = new();
    private readonly Series _simConstruction = new();
    private readonly Series _simConstructionPlacementValidation = new();
    private readonly Series _simConstructionConnectivityBaseline = new();
    private readonly Series _simConstructionConnectivityCandidate = new();
    private readonly Series _simConstructionConnectivityCompare = new();
    private readonly Series _simConstructionOccupancyPlace = new();
    private readonly Series _simConstructionRouteTopology = new();
    private readonly Series _simConstructionPathInvalidation = new();
    private readonly Series _simConstructionConnectivityAllocated = new();
    private readonly Series _simProduction = new();
    private readonly Series _simTechnology = new();
    private readonly Series _simEconomySystem = new();
    private readonly Series _simLifecycleFinalize = new();
    private readonly Series _simCombat = new();
    private readonly Series _simCombatProjectiles = new();
    private readonly Series _simCombatUnitLoop = new();
    private readonly Series _simCombatTargetSearch = new();
    private readonly Series _simCombatAllocated = new();
    private readonly Series _simPath = new();
    private readonly Series _simPathAllocated = new();
    private readonly Series _simPreferredVelocity = new();
    private readonly Series _simChoke = new();
    private readonly Series _simSpatialHash = new();
    private readonly Series _simSteering = new();
    private readonly Series _simSteeringAllocated = new();
    private readonly Series _simIntegrate = new();
    private readonly Series _simCollision = new();
    private readonly Series _simRecovery = new();
    private readonly Series _simQueue = new();
    private readonly Series _simVisibility = new();
    private readonly Series _simVisibilityDetection = new();
    private readonly Series _simVisibilityClear = new();
    private readonly Series _simVisibilityUnits = new();
    private readonly Series _simVisibilityBuildings = new();
    private readonly Series _simVisibilityCandidateCells = new();
    private readonly Series _simMatch = new();
    private readonly Series _simCommand = new();
    private readonly Series _simQueueAllocated = new();
    private readonly Series _simVisibilityAllocated = new();
    private readonly Series _simMatchAllocated = new();
    private readonly Series _simConstructionAllocated = new();
    private readonly Series _simProductionAllocated = new();
    private readonly Series _simTechnologyAllocated = new();
    private readonly Series _simEconomySystemAllocated = new();
    private readonly Series _simLifecycleFinalizeAllocated = new();
    private readonly Series _simAllocated = new();
    private ulong _readyUsec;
    private ulong _lastFrameUsec;
    private bool _samplingAnnounced;
    private bool _completed;
    private readonly double _spikeMilliseconds;
    private readonly double _automatedSkirmishSpikeMilliseconds;
    private double _lastPhysicsMilliseconds;
    private double _lastSimulationMilliseconds;
    private long _lastPhysicsAllocatedBytes;
    private SimulationMetrics _lastSimulationMetrics = new();
    private RtsAiUpdateProfile _lastAiProfile;
    private War3StressUpdateProfile _lastStressProfile;
    private int _lastGen0Collections;
    private int _lastGen1Collections;
    private int _lastGen2Collections;
    private long _lastAutomatedSkirmishSpikeTick = -1;
    private long _lastAutomatedSkirmishSpikeBurstTick = -1;
    private int _automatedSkirmishSpikeCount;
    private int _automatedSkirmishSpikeBurstCount;
    private int _automatedSkirmishConsecutiveSpikes;
    private int _automatedSkirmishMaximumConsecutiveSpikes;

    private War3RuntimeProfiler(
        string variant,
        double warmupSeconds,
        double sampleSeconds,
        double spikeMilliseconds,
        double automatedSkirmishSpikeMilliseconds)
    {
        Variant = variant;
        _warmupUsec = (ulong)(warmupSeconds * 1_000_000d);
        _sampleUsec = (ulong)(sampleSeconds * 1_000_000d);
        _spikeMilliseconds = spikeMilliseconds;
        _automatedSkirmishSpikeMilliseconds =
            automatedSkirmishSpikeMilliseconds;
        GD.Print(
            $"WAR3_RUNTIME_PROFILE configured variant={Variant} " +
            $"warmup_s={warmupSeconds:0.###} sample_s={sampleSeconds:0.###} " +
            $"spike_ms={spikeMilliseconds:0.###} " +
            $"auto_tick_spike_ms={automatedSkirmishSpikeMilliseconds:0.###}");
    }

    public string Variant { get; }

    public static War3RuntimeProfiler? TryCreate(string[] arguments)
    {
        var automatedSkirmish =
            War3AutomatedSkirmishStressMode.IsRequested(arguments);
        if (!arguments.Contains("--war3-runtime-profile") &&
            !automatedSkirmish)
            return null;
        var variant = ArgumentValue(arguments, "--war3-profile-variant=") ??
                      (automatedSkirmish ? "auto-skirmish" : "baseline");
        var warmup = PositiveDouble(
            ArgumentValue(arguments, "--war3-profile-warmup="),
            automatedSkirmish ? 2d : DefaultWarmupSeconds);
        var sample = PositiveDouble(
            ArgumentValue(arguments, "--war3-profile-seconds="),
            automatedSkirmish ? 20d : DefaultSampleSeconds);
        var spike = PositiveDouble(
            ArgumentValue(arguments, "--war3-profile-spike-ms="),
            DefaultSpikeMilliseconds);
        var automatedSkirmishSpike = PositiveDouble(
            ArgumentValue(arguments, "--war3-auto-skirmish-spike-ms="),
            8d);
        return new War3RuntimeProfiler(
            variant, warmup, sample, spike, automatedSkirmishSpike);
    }

    public void MapReady()
    {
        _readyUsec = Time.GetTicksUsec();
        _lastFrameUsec = 0;
        _lastGen0Collections = GC.CollectionCount(0);
        _lastGen1Collections = GC.CollectionCount(1);
        _lastGen2Collections = GC.CollectionCount(2);
        GD.Print($"WAR3_RUNTIME_PROFILE map_ready variant={Variant}");
    }

    public void RecordPhysics(
        double totalMilliseconds,
        double stressDriverMilliseconds,
        double aiMilliseconds,
        double simulationMilliseconds,
        double selectionHudMilliseconds,
        long allocatedBytes,
        SimulationMetrics metrics,
        RtsAiUpdateProfile aiProfile,
        War3StressUpdateProfile stressProfile)
    {
        _lastPhysicsMilliseconds = totalMilliseconds;
        _lastSimulationMilliseconds = simulationMilliseconds;
        _lastPhysicsAllocatedBytes = allocatedBytes;
        _lastSimulationMetrics = metrics;
        _lastAiProfile = aiProfile;
        _lastStressProfile = stressProfile;
        if (!IsSampling(Time.GetTicksUsec())) return;
        if (totalMilliseconds >= _spikeMilliseconds)
        {
            ReportPhysicsSpike(
                totalMilliseconds,
                stressDriverMilliseconds,
                aiMilliseconds,
                simulationMilliseconds,
                selectionHudMilliseconds,
                allocatedBytes,
                metrics,
                aiProfile,
                stressProfile);
        }
        _physics.Add(totalMilliseconds);
        _stressDriver.Add(stressDriverMilliseconds);
        _stressBuildingLifecycle.Add(
            stressProfile.BuildingLifecycleMilliseconds);
        _stressUnitRespawn.Add(stressProfile.UnitRespawnMilliseconds);
        _stressCombatOrders.Add(stressProfile.CombatOrderMilliseconds);
        _stressConstructionIssue.Add(
            stressProfile.ConstructionIssueMilliseconds);
        _stressAllocated.Add(stressProfile.AllocatedBytes);
        _ai.Add(aiMilliseconds);
        _simulation.Add(simulationMilliseconds);
        _selectionHud.Add(selectionHudMilliseconds);
        _physicsAllocated.Add(allocatedBytes);
        _simTotal.Add(metrics.TotalMilliseconds);
        _simEconomy.Add(metrics.EconomyMilliseconds);
        _simConstruction.Add(metrics.ConstructionMilliseconds);
        _simConstructionPlacementValidation.Add(
            metrics.ConstructionPlacementValidationMilliseconds);
        _simConstructionConnectivityBaseline.Add(
            metrics.ConstructionConnectivityBaselineMilliseconds);
        _simConstructionConnectivityCandidate.Add(
            metrics.ConstructionConnectivityCandidateMilliseconds);
        _simConstructionConnectivityCompare.Add(
            metrics.ConstructionConnectivityCompareMilliseconds);
        _simConstructionOccupancyPlace.Add(
            metrics.ConstructionOccupancyPlaceMilliseconds);
        _simConstructionRouteTopology.Add(
            metrics.ConstructionRouteTopologyMilliseconds);
        _simConstructionPathInvalidation.Add(
            metrics.ConstructionPathInvalidationMilliseconds);
        _simConstructionConnectivityAllocated.Add(
            metrics.ConstructionConnectivityAllocatedBytes);
        _simProduction.Add(metrics.ProductionMilliseconds);
        _simTechnology.Add(metrics.TechnologyMilliseconds);
        _simEconomySystem.Add(metrics.EconomySystemMilliseconds);
        _simLifecycleFinalize.Add(metrics.LifecycleFinalizeMilliseconds);
        _simCombat.Add(metrics.CombatMilliseconds);
        _simCombatProjectiles.Add(metrics.CombatProjectileMilliseconds);
        _simCombatUnitLoop.Add(metrics.CombatUnitLoopMilliseconds);
        _simCombatTargetSearch.Add(metrics.CombatTargetSearchMilliseconds);
        _simCombatAllocated.Add(metrics.CombatAllocatedBytes);
        _simPath.Add(metrics.PathMilliseconds);
        _simPathAllocated.Add(metrics.PathAllocatedBytes);
        _simPreferredVelocity.Add(metrics.PreferredVelocityMilliseconds);
        _simChoke.Add(metrics.ChokeMilliseconds);
        _simSpatialHash.Add(metrics.SpatialHashMilliseconds);
        _simSteering.Add(metrics.SteeringMilliseconds);
        _simSteeringAllocated.Add(metrics.SteeringAllocatedBytes);
        _simIntegrate.Add(metrics.IntegrateMilliseconds);
        _simCollision.Add(metrics.CollisionMilliseconds);
        _simRecovery.Add(metrics.RecoveryMilliseconds);
        _simQueue.Add(metrics.QueueMilliseconds);
        _simVisibility.Add(metrics.VisibilityMilliseconds);
        _simVisibilityDetection.Add(metrics.VisibilityDetectionMilliseconds);
        _simVisibilityClear.Add(metrics.VisibilityClearMilliseconds);
        _simVisibilityUnits.Add(metrics.VisibilityUnitVisionMilliseconds);
        _simVisibilityBuildings.Add(
            metrics.VisibilityBuildingVisionMilliseconds);
        _simVisibilityCandidateCells.Add(metrics.VisibilityCandidateCells);
        _simMatch.Add(metrics.MatchMilliseconds);
        _simCommand.Add(metrics.CommandMilliseconds);
        _simQueueAllocated.Add(metrics.QueueAllocatedBytes);
        _simVisibilityAllocated.Add(metrics.VisibilityAllocatedBytes);
        _simMatchAllocated.Add(metrics.MatchAllocatedBytes);
        _simConstructionAllocated.Add(metrics.ConstructionAllocatedBytes);
        _simProductionAllocated.Add(metrics.ProductionAllocatedBytes);
        _simTechnologyAllocated.Add(metrics.TechnologyAllocatedBytes);
        _simEconomySystemAllocated.Add(metrics.EconomySystemAllocatedBytes);
        _simLifecycleFinalizeAllocated.Add(
            metrics.LifecycleFinalizeAllocatedBytes);
        _simAllocated.Add(metrics.AllocatedBytes);
    }

    public void RecordAutomatedSkirmishStep(
        double totalMilliseconds,
        double simulationMilliseconds,
        long allocatedBytes,
        SimulationMetrics metrics,
        RtsAiUpdateProfile aiProfile,
        War3AutomatedSkirmishUpdateProfile automationProfile)
    {
        if (!IsSampling(Time.GetTicksUsec())) return;
        _automatedSkirmishTick.Add(totalMilliseconds);
        _automatedSkirmishBankTopUp.Add(
            automationProfile.BankTopUpMilliseconds);
        _automatedSkirmishSupportConstruction.Add(
            automationProfile.SupportConstructionMilliseconds);
        _automatedSkirmishAi.Add(automationProfile.AiMilliseconds);
        _automatedSkirmishStatus.Add(
            automationProfile.StatusSampleMilliseconds);
        _automatedSkirmishSimulation.Add(simulationMilliseconds);
        _automatedSkirmishAllocated.Add(allocatedBytes);

        if (totalMilliseconds < _automatedSkirmishSpikeMilliseconds)
        {
            _automatedSkirmishConsecutiveSpikes = 0;
            return;
        }

        _automatedSkirmishSpikeCount++;
        var newBurst = _lastAutomatedSkirmishSpikeTick != metrics.Tick - 1;
        if (newBurst)
        {
            _automatedSkirmishSpikeBurstCount++;
            _automatedSkirmishConsecutiveSpikes = 1;
            if (_lastAutomatedSkirmishSpikeBurstTick >= 0)
            {
                _automatedSkirmishSpikeBurstInterval.Add(
                    metrics.Tick - _lastAutomatedSkirmishSpikeBurstTick);
            }
            _lastAutomatedSkirmishSpikeBurstTick = metrics.Tick;
        }
        else
        {
            _automatedSkirmishConsecutiveSpikes++;
        }
        _automatedSkirmishMaximumConsecutiveSpikes = Math.Max(
            _automatedSkirmishMaximumConsecutiveSpikes,
            _automatedSkirmishConsecutiveSpikes);
        _lastAutomatedSkirmishSpikeTick = metrics.Tick;

        GD.Print(
            $"WAR3_AUTO_SKIRMISH_TICK_SPIKE tick={metrics.Tick} " +
            $"tick_ms={totalMilliseconds:0.###} " +
            $"driver_ms={automationProfile.TotalMilliseconds:0.###} " +
            $"bank_ms={automationProfile.BankTopUpMilliseconds:0.###} " +
            $"support_build_ms=" +
            $"{automationProfile.SupportConstructionMilliseconds:0.###} " +
            $"ai_ms={automationProfile.AiMilliseconds:0.###} " +
            $"status_ms={automationProfile.StatusSampleMilliseconds:0.###} " +
            $"simulation_ms={simulationMilliseconds:0.###} " +
            $"allocated={allocatedBytes} " +
            $"activities={ActivityText(automationProfile.Activities)} " +
            $"intents={automationProfile.ExecutedIntents} " +
            $"ai_detail={aiProfile.CaptureMilliseconds:0.###}/" +
            $"{aiProfile.DecisionMilliseconds:0.###}/" +
            $"{aiProfile.ExecutionMilliseconds:0.###}/" +
            $"{aiProfile.AllocatedBytes}/" +
            $"{aiProfile.SlowestIntent}/" +
            $"{aiProfile.SlowestIntentMilliseconds:0.###} " +
            $"economy_ms={metrics.EconomyMilliseconds:0.###} " +
            $"construction_ms={metrics.ConstructionMilliseconds:0.###} " +
            $"production_ms={metrics.ProductionMilliseconds:0.###} " +
            $"technology_ms={metrics.TechnologyMilliseconds:0.###} " +
            $"combat_ms={metrics.CombatMilliseconds:0.###} " +
            $"path_ms={metrics.PathMilliseconds:0.###} " +
            $"steering_ms={metrics.SteeringMilliseconds:0.###} " +
            $"collision_ms={metrics.CollisionMilliseconds:0.###} " +
            $"visibility_ms={metrics.VisibilityMilliseconds:0.###} " +
            $"visibility_cells={metrics.VisibilityCandidateCells} " +
            $"paths={metrics.PathsCompleted}/{metrics.PathsFailed}/" +
            $"{metrics.PendingPathRequests}");
    }

    private static string ActivityText(
        War3AutomatedSkirmishActivity activities) =>
        activities == War3AutomatedSkirmishActivity.None
            ? "none"
            : activities.ToString().Replace(", ", "+");

    private static void ReportPhysicsSpike(
        double totalMilliseconds,
        double stressDriverMilliseconds,
        double aiMilliseconds,
        double simulationMilliseconds,
        double selectionHudMilliseconds,
        long allocatedBytes,
        SimulationMetrics metrics,
        RtsAiUpdateProfile aiProfile,
        War3StressUpdateProfile stressProfile)
    {
        GD.Print(
            $"WAR3_RUNTIME_PHYSICS_SPIKE tick={metrics.Tick} " +
            $"physics_ms={totalMilliseconds:0.###} " +
            $"stress_ms={stressDriverMilliseconds:0.###} " +
            $"ai_ms={aiMilliseconds:0.###} " +
            $"simulation_ms={simulationMilliseconds:0.###} " +
            $"selection_hud_ms={selectionHudMilliseconds:0.###} " +
            $"physics_alloc={allocatedBytes} " +
            $"construction_ms={metrics.ConstructionMilliseconds:0.###} " +
            $"construction_commits={metrics.ConstructionCommitAttempts}/" +
            $"{metrics.ConstructionCommitSuccesses} " +
            $"construction_detail=" +
            $"{metrics.ConstructionPlacementValidationMilliseconds:0.###}/" +
            $"{metrics.ConstructionConnectivityBaselineMilliseconds:0.###}/" +
            $"{metrics.ConstructionConnectivityCandidateMilliseconds:0.###}/" +
            $"{metrics.ConstructionConnectivityCompareMilliseconds:0.###}/" +
            $"{metrics.ConstructionOccupancyPlaceMilliseconds:0.###}/" +
            $"{metrics.ConstructionRouteTopologyMilliseconds:0.###}/" +
            $"{metrics.ConstructionPathInvalidationMilliseconds:0.###}/" +
            $"{metrics.ConstructionConnectivityAllocatedBytes} " +
            $"visibility_ms={metrics.VisibilityMilliseconds:0.###} " +
            $"visibility_detail={metrics.VisibilityDetectionMilliseconds:0.###}/" +
            $"{metrics.VisibilityClearMilliseconds:0.###}/" +
            $"{metrics.VisibilityUnitVisionMilliseconds:0.###}/" +
            $"{metrics.VisibilityBuildingVisionMilliseconds:0.###}/" +
            $"{metrics.VisibilityUnitCacheHits}/" +
            $"{metrics.VisibilityUnitCacheRebuilds}/" +
            $"{metrics.VisibilityCandidateCells} " +
            $"combat_ms={metrics.CombatMilliseconds:0.###} " +
            $"combat_detail={metrics.CombatProjectileMilliseconds:0.###}/" +
            $"{metrics.CombatUnitLoopMilliseconds:0.###}/" +
            $"{metrics.CombatTargetSearchMilliseconds:0.###}/" +
            $"{metrics.CombatTargetSearches} " +
            $"path_ms={metrics.PathMilliseconds:0.###} " +
            $"path_alloc={metrics.PathAllocatedBytes} " +
            $"steering_ms={metrics.SteeringMilliseconds:0.###} " +
            $"steering_alloc={metrics.SteeringAllocatedBytes} " +
            $"collision_ms={metrics.CollisionMilliseconds:0.###} " +
            $"ai_detail={aiProfile.CaptureMilliseconds:0.###}/" +
            $"{aiProfile.DecisionMilliseconds:0.###}/" +
            $"{aiProfile.ExecutionMilliseconds:0.###}/" +
            $"{aiProfile.AllocatedBytes} " +
            $"stress_detail={stressProfile.BuildingLifecycleMilliseconds:0.###}/" +
            $"{stressProfile.UnitRespawnMilliseconds:0.###}/" +
            $"{stressProfile.CombatOrderMilliseconds:0.###}/" +
            $"{stressProfile.ConstructionIssueMilliseconds:0.###}/" +
            $"{stressProfile.AllocatedBytes}");
    }

    public bool RecordFrameAndShouldQuit(
        double presenterMilliseconds,
        double buildPreviewMilliseconds,
        long presenterAllocatedBytes,
        War3PresenterSyncProfile presenterProfile)
    {
        if (_readyUsec == 0 || _completed) return false;
        var now = Time.GetTicksUsec();
        var sampling = IsSampling(now);
        if (sampling)
        {
            if (_lastFrameUsec != 0)
            {
                var frameMilliseconds = (now - _lastFrameUsec) / 1_000d;
                _frame.Add(frameMilliseconds);
                ReportSpike(
                    frameMilliseconds,
                    presenterMilliseconds,
                    presenterAllocatedBytes);
            }
            _presenter.Add(presenterMilliseconds);
            _buildPreview.Add(buildPreviewMilliseconds);
            _presenterAllocated.Add(presenterAllocatedBytes);
            _presenterUnits.Add(presenterProfile.UnitsMilliseconds);
            _presenterBuildings.Add(presenterProfile.BuildingsMilliseconds);
            _presenterResources.Add(presenterProfile.ResourcesMilliseconds);
            _presenterProjectiles.Add(presenterProfile.ProjectilesMilliseconds);
            _presenterTransients.Add(presenterProfile.TransientsMilliseconds);
            _presenterUnitsAllocated.Add(presenterProfile.UnitsAllocatedBytes);
            _presenterBuildingsAllocated.Add(
                presenterProfile.BuildingsAllocatedBytes);
            _presenterResourcesAllocated.Add(
                presenterProfile.ResourcesAllocatedBytes);
            _presenterProjectilesAllocated.Add(
                presenterProfile.ProjectilesAllocatedBytes);
            _presenterTransientsAllocated.Add(
                presenterProfile.TransientsAllocatedBytes);
            _objects.Add(RenderingServer.GetRenderingInfo(
                RenderingServer.RenderingInfo.TotalObjectsInFrame));
            _drawCalls.Add(RenderingServer.GetRenderingInfo(
                RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
            _primitives.Add(RenderingServer.GetRenderingInfo(
                RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
        }
        _lastFrameUsec = sampling ? now : 0;
        if (now - _readyUsec < _warmupUsec + _sampleUsec) return false;
        PrintSummary();
        _completed = true;
        return true;
    }

    private void ReportSpike(
        double frameMilliseconds,
        double presenterMilliseconds,
        long presenterAllocatedBytes)
    {
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var delta0 = gen0 - _lastGen0Collections;
        var delta1 = gen1 - _lastGen1Collections;
        var delta2 = gen2 - _lastGen2Collections;
        _lastGen0Collections = gen0;
        _lastGen1Collections = gen1;
        _lastGen2Collections = gen2;
        if (frameMilliseconds < _spikeMilliseconds) return;
        GD.Print(
            $"WAR3_RUNTIME_SPIKE tick={_lastSimulationMetrics.Tick} " +
            $"frame_ms={frameMilliseconds:0.###} " +
            $"gc={delta0}/{delta1}/{delta2} " +
            $"heap_bytes={GC.GetTotalMemory(false)} " +
            $"physics_ms={_lastPhysicsMilliseconds:0.###} " +
            $"simulation_ms={_lastSimulationMilliseconds:0.###} " +
            $"presenter_ms={presenterMilliseconds:0.###} " +
            $"physics_alloc={_lastPhysicsAllocatedBytes} " +
            $"presenter_alloc={presenterAllocatedBytes} " +
            $"economy_ms={_lastSimulationMetrics.EconomyMilliseconds:0.###} " +
            $"construction_ms={_lastSimulationMetrics.ConstructionMilliseconds:0.###} " +
            $"construction_alloc={_lastSimulationMetrics.ConstructionAllocatedBytes} " +
            $"construction_commits={_lastSimulationMetrics.ConstructionCommitAttempts}/" +
            $"{_lastSimulationMetrics.ConstructionCommitSuccesses} " +
            $"construction_detail=" +
            $"{_lastSimulationMetrics.ConstructionPlacementValidationMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.ConstructionConnectivityBaselineMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.ConstructionConnectivityCandidateMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.ConstructionConnectivityCompareMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.ConstructionOccupancyPlaceMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.ConstructionRouteTopologyMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.ConstructionPathInvalidationMilliseconds:0.###} " +
            $"construction_connectivity={_lastSimulationMetrics.ConstructionConnectivityEvaluations}/" +
            $"{_lastSimulationMetrics.ConstructionConnectivityBaselineRebuilds}/" +
            $"{_lastSimulationMetrics.ConstructionConnectivityAllocatedBytes} " +
            $"production_ms={_lastSimulationMetrics.ProductionMilliseconds:0.###} " +
            $"combat_ms={_lastSimulationMetrics.CombatMilliseconds:0.###} " +
            $"combat_detail={_lastSimulationMetrics.CombatProjectileMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.CombatUnitLoopMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.CombatTargetSearchMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.CombatTargetSearches} " +
            $"stress_detail={_lastStressProfile.TotalMilliseconds:0.###}/" +
            $"{_lastStressProfile.BuildingLifecycleMilliseconds:0.###}/" +
            $"{_lastStressProfile.UnitRespawnMilliseconds:0.###}/" +
            $"{_lastStressProfile.CombatOrderMilliseconds:0.###}/" +
            $"{_lastStressProfile.ConstructionIssueMilliseconds:0.###}/" +
            $"{_lastStressProfile.AllocatedBytes} " +
            $"path_ms={_lastSimulationMetrics.PathMilliseconds:0.###} " +
            $"visibility_ms={_lastSimulationMetrics.VisibilityMilliseconds:0.###} " +
            $"visibility_detail={_lastSimulationMetrics.VisibilityDetectionMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.VisibilityClearMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.VisibilityUnitVisionMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.VisibilityBuildingVisionMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.VisibilityUnitCacheHits}/" +
            $"{_lastSimulationMetrics.VisibilityUnitCacheRebuilds}/" +
            $"{_lastSimulationMetrics.VisibilityCandidateCells} " +
            $"steering_ms={_lastSimulationMetrics.SteeringMilliseconds:0.###} " +
            $"recovery_ms={_lastSimulationMetrics.RecoveryMilliseconds:0.###} " +
            $"ai_detail={_lastAiProfile.CaptureMilliseconds:0.###}/" +
            $"{_lastAiProfile.DecisionMilliseconds:0.###}/" +
            $"{_lastAiProfile.ExecutionMilliseconds:0.###}/" +
            $"{_lastAiProfile.AllocatedBytes}/" +
            $"{_lastAiProfile.ExecutedIntents}/" +
            $"{_lastAiProfile.SlowestIntent}/" +
            $"{_lastAiProfile.SlowestIntentMilliseconds:0.###} " +
            $"paths={_lastSimulationMetrics.PathsCompleted}/" +
            $"{_lastSimulationMetrics.PathsFailed} " +
            $"pending_paths={_lastSimulationMetrics.PendingPathRequests} " +
            $"path_detail={_lastSimulationMetrics.PathDirectCheckMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.PathSearchMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.PathSimplificationMilliseconds:0.###}/" +
            $"{_lastSimulationMetrics.PathExpandedNodes}/" +
            $"{_lastSimulationMetrics.PathRawPoints}/" +
            $"{_lastSimulationMetrics.PathSimplifiedPoints} " +
            $"connectivity={_lastSimulationMetrics.PathFullConnectivityRebuilds}/" +
            $"{_lastSimulationMetrics.PathIncrementalConnectivityUpdates}/" +
            $"{_lastSimulationMetrics.PathConnectivityRefreshMilliseconds:0.###}");
    }

    private bool IsSampling(ulong now)
    {
        if (_readyUsec == 0 || now - _readyUsec < _warmupUsec) return false;
        if (!_samplingAnnounced)
        {
            _samplingAnnounced = true;
            GD.Print($"WAR3_RUNTIME_PROFILE sampling variant={Variant}");
        }
        return now - _readyUsec < _warmupUsec + _sampleUsec;
    }

    private void PrintSummary()
    {
        GD.Print(
            $"WAR3_RUNTIME_PROFILE_SUMMARY variant={Variant} " +
            $"frames={_frame.Count} physics_ticks={_physics.Count} " +
            $"auto_tick_spikes={_automatedSkirmishSpikeCount} " +
            $"auto_spike_bursts={_automatedSkirmishSpikeBurstCount} " +
            $"auto_max_consecutive_spikes=" +
            $"{_automatedSkirmishMaximumConsecutiveSpikes}");
        Print("frame_ms", _frame);
        Print("physics_ms", _physics);
        Print("stress_driver_ms", _stressDriver);
        Print("stress_building_lifecycle_ms", _stressBuildingLifecycle);
        Print("stress_unit_respawn_ms", _stressUnitRespawn);
        Print("stress_combat_orders_ms", _stressCombatOrders);
        Print("stress_construction_issue_ms", _stressConstructionIssue);
        Print("stress_alloc_bytes", _stressAllocated);
        Print("auto_skirmish_tick_ms", _automatedSkirmishTick);
        Print("auto_skirmish_bank_top_up_ms", _automatedSkirmishBankTopUp);
        Print("auto_skirmish_support_construction_ms",
            _automatedSkirmishSupportConstruction);
        Print("auto_skirmish_ai_ms", _automatedSkirmishAi);
        Print("auto_skirmish_status_ms", _automatedSkirmishStatus);
        Print("auto_skirmish_simulation_ms", _automatedSkirmishSimulation);
        Print("auto_skirmish_alloc_bytes", _automatedSkirmishAllocated);
        Print("auto_skirmish_spike_burst_interval_ticks",
            _automatedSkirmishSpikeBurstInterval);
        Print("ai_ms", _ai);
        Print("simulation_call_ms", _simulation);
        Print("selection_hud_ms", _selectionHud);
        Print("presenter_ms", _presenter);
        Print("build_preview_ms", _buildPreview);
        Print("physics_alloc_bytes", _physicsAllocated);
        Print("presenter_alloc_bytes", _presenterAllocated);
        Print("presenter_units_ms", _presenterUnits);
        Print("presenter_buildings_ms", _presenterBuildings);
        Print("presenter_resources_ms", _presenterResources);
        Print("presenter_projectiles_ms", _presenterProjectiles);
        Print("presenter_transients_ms", _presenterTransients);
        Print("presenter_units_alloc_bytes", _presenterUnitsAllocated);
        Print("presenter_buildings_alloc_bytes", _presenterBuildingsAllocated);
        Print("presenter_resources_alloc_bytes", _presenterResourcesAllocated);
        Print("presenter_projectiles_alloc_bytes",
            _presenterProjectilesAllocated);
        Print("presenter_transients_alloc_bytes", _presenterTransientsAllocated);
        Print("render_objects", _objects);
        Print("render_draw_calls", _drawCalls);
        Print("render_primitives", _primitives);
        Print("sim_total_ms", _simTotal);
        Print("sim_economy_ms", _simEconomy);
        Print("sim_construction_ms", _simConstruction);
        Print("sim_construction_placement_validation_ms",
            _simConstructionPlacementValidation);
        Print("sim_construction_connectivity_baseline_ms",
            _simConstructionConnectivityBaseline);
        Print("sim_construction_connectivity_candidate_ms",
            _simConstructionConnectivityCandidate);
        Print("sim_construction_connectivity_compare_ms",
            _simConstructionConnectivityCompare);
        Print("sim_construction_occupancy_place_ms",
            _simConstructionOccupancyPlace);
        Print("sim_construction_route_topology_ms",
            _simConstructionRouteTopology);
        Print("sim_construction_path_invalidation_ms",
            _simConstructionPathInvalidation);
        Print("sim_construction_connectivity_alloc_bytes",
            _simConstructionConnectivityAllocated);
        Print("sim_production_ms", _simProduction);
        Print("sim_technology_ms", _simTechnology);
        Print("sim_economy_system_ms", _simEconomySystem);
        Print("sim_lifecycle_finalize_ms", _simLifecycleFinalize);
        Print("sim_combat_ms", _simCombat);
        Print("sim_combat_projectiles_ms", _simCombatProjectiles);
        Print("sim_combat_unit_loop_ms", _simCombatUnitLoop);
        Print("sim_combat_target_search_ms", _simCombatTargetSearch);
        Print("sim_combat_alloc_bytes", _simCombatAllocated);
        Print("sim_path_ms", _simPath);
        Print("sim_path_alloc_bytes", _simPathAllocated);
        Print("sim_preferred_velocity_ms", _simPreferredVelocity);
        Print("sim_choke_ms", _simChoke);
        Print("sim_spatial_hash_ms", _simSpatialHash);
        Print("sim_steering_ms", _simSteering);
        Print("sim_steering_alloc_bytes", _simSteeringAllocated);
        Print("sim_integrate_ms", _simIntegrate);
        Print("sim_collision_ms", _simCollision);
        Print("sim_recovery_ms", _simRecovery);
        Print("sim_queue_ms", _simQueue);
        Print("sim_visibility_ms", _simVisibility);
        Print("sim_visibility_detection_ms", _simVisibilityDetection);
        Print("sim_visibility_clear_ms", _simVisibilityClear);
        Print("sim_visibility_units_ms", _simVisibilityUnits);
        Print("sim_visibility_buildings_ms", _simVisibilityBuildings);
        Print("sim_visibility_candidate_cells", _simVisibilityCandidateCells);
        Print("sim_match_ms", _simMatch);
        Print("sim_command_ms", _simCommand);
        Print("sim_queue_alloc_bytes", _simQueueAllocated);
        Print("sim_visibility_alloc_bytes", _simVisibilityAllocated);
        Print("sim_match_alloc_bytes", _simMatchAllocated);
        Print("sim_construction_alloc_bytes", _simConstructionAllocated);
        Print("sim_production_alloc_bytes", _simProductionAllocated);
        Print("sim_technology_alloc_bytes", _simTechnologyAllocated);
        Print("sim_economy_system_alloc_bytes", _simEconomySystemAllocated);
        Print("sim_lifecycle_finalize_alloc_bytes",
            _simLifecycleFinalizeAllocated);
        Print("sim_alloc_bytes", _simAllocated);
    }

    private void Print(string name, Series series)
    {
        var stats = series.Stats();
        GD.Print(
            $"WAR3_RUNTIME_PROFILE_METRIC variant={Variant} name={name} " +
            $"count={stats.Count} avg={stats.Average:0.###} " +
            $"p50={stats.P50:0.###} p95={stats.P95:0.###} " +
            $"max={stats.Maximum:0.###}");
    }

    private static string? ArgumentValue(string[] arguments, string prefix) =>
        arguments.FirstOrDefault(value => value.StartsWith(
            prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static double PositiveDouble(string? value, double fallback) =>
        double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) && parsed > 0d
            ? parsed
            : fallback;

    private sealed class Series
    {
        private readonly List<double> _values = [];

        public int Count => _values.Count;

        public void Add(double value)
        {
            if (double.IsFinite(value)) _values.Add(value);
        }

        public SeriesStats Stats()
        {
            if (_values.Count == 0) return default;
            var sorted = _values.Order().ToArray();
            return new SeriesStats(
                sorted.Length,
                sorted.Average(),
                Percentile(sorted, 0.50d),
                Percentile(sorted, 0.95d),
                sorted[^1]);
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
        }
    }

    private readonly record struct SeriesStats(
        int Count,
        double Average,
        double P50,
        double P95,
        double Maximum);
}
