using Godot;
using RtsDemo.AI;
using RtsDemo.Simulation;

namespace War3Rts;

/// <summary>
/// Opt-in fixed-scene profiler used by the War3 skirmish runtime. It is kept
/// out of the normal execution path unless --war3-runtime-profile is present.
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
    private readonly Series _simProduction = new();
    private readonly Series _simTechnology = new();
    private readonly Series _simEconomySystem = new();
    private readonly Series _simLifecycleFinalize = new();
    private readonly Series _simCombat = new();
    private readonly Series _simPath = new();
    private readonly Series _simPreferredVelocity = new();
    private readonly Series _simChoke = new();
    private readonly Series _simSpatialHash = new();
    private readonly Series _simSteering = new();
    private readonly Series _simIntegrate = new();
    private readonly Series _simCollision = new();
    private readonly Series _simRecovery = new();
    private readonly Series _simQueue = new();
    private readonly Series _simVisibility = new();
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
    private double _lastPhysicsMilliseconds;
    private double _lastSimulationMilliseconds;
    private long _lastPhysicsAllocatedBytes;
    private SimulationMetrics _lastSimulationMetrics = new();
    private RtsAiUpdateProfile _lastAiProfile;
    private int _lastGen0Collections;
    private int _lastGen1Collections;
    private int _lastGen2Collections;

    private War3RuntimeProfiler(
        string variant,
        double warmupSeconds,
        double sampleSeconds,
        double spikeMilliseconds)
    {
        Variant = variant;
        _warmupUsec = (ulong)(warmupSeconds * 1_000_000d);
        _sampleUsec = (ulong)(sampleSeconds * 1_000_000d);
        _spikeMilliseconds = spikeMilliseconds;
        GD.Print(
            $"WAR3_RUNTIME_PROFILE configured variant={Variant} " +
            $"warmup_s={warmupSeconds:0.###} sample_s={sampleSeconds:0.###} " +
            $"spike_ms={spikeMilliseconds:0.###}");
    }

    public string Variant { get; }

    public static War3RuntimeProfiler? TryCreate(string[] arguments)
    {
        if (!arguments.Contains("--war3-runtime-profile")) return null;
        var variant = ArgumentValue(arguments, "--war3-profile-variant=") ??
                      "baseline";
        var warmup = PositiveDouble(
            ArgumentValue(arguments, "--war3-profile-warmup="),
            DefaultWarmupSeconds);
        var sample = PositiveDouble(
            ArgumentValue(arguments, "--war3-profile-seconds="),
            DefaultSampleSeconds);
        var spike = PositiveDouble(
            ArgumentValue(arguments, "--war3-profile-spike-ms="),
            DefaultSpikeMilliseconds);
        return new War3RuntimeProfiler(variant, warmup, sample, spike);
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
        double aiMilliseconds,
        double simulationMilliseconds,
        double selectionHudMilliseconds,
        long allocatedBytes,
        SimulationMetrics metrics,
        RtsAiUpdateProfile aiProfile)
    {
        _lastPhysicsMilliseconds = totalMilliseconds;
        _lastSimulationMilliseconds = simulationMilliseconds;
        _lastPhysicsAllocatedBytes = allocatedBytes;
        _lastSimulationMetrics = metrics;
        _lastAiProfile = aiProfile;
        if (!IsSampling(Time.GetTicksUsec())) return;
        _physics.Add(totalMilliseconds);
        _ai.Add(aiMilliseconds);
        _simulation.Add(simulationMilliseconds);
        _selectionHud.Add(selectionHudMilliseconds);
        _physicsAllocated.Add(allocatedBytes);
        _simTotal.Add(metrics.TotalMilliseconds);
        _simEconomy.Add(metrics.EconomyMilliseconds);
        _simConstruction.Add(metrics.ConstructionMilliseconds);
        _simProduction.Add(metrics.ProductionMilliseconds);
        _simTechnology.Add(metrics.TechnologyMilliseconds);
        _simEconomySystem.Add(metrics.EconomySystemMilliseconds);
        _simLifecycleFinalize.Add(metrics.LifecycleFinalizeMilliseconds);
        _simCombat.Add(metrics.CombatMilliseconds);
        _simPath.Add(metrics.PathMilliseconds);
        _simPreferredVelocity.Add(metrics.PreferredVelocityMilliseconds);
        _simChoke.Add(metrics.ChokeMilliseconds);
        _simSpatialHash.Add(metrics.SpatialHashMilliseconds);
        _simSteering.Add(metrics.SteeringMilliseconds);
        _simIntegrate.Add(metrics.IntegrateMilliseconds);
        _simCollision.Add(metrics.CollisionMilliseconds);
        _simRecovery.Add(metrics.RecoveryMilliseconds);
        _simQueue.Add(metrics.QueueMilliseconds);
        _simVisibility.Add(metrics.VisibilityMilliseconds);
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
            $"production_ms={_lastSimulationMetrics.ProductionMilliseconds:0.###} " +
            $"combat_ms={_lastSimulationMetrics.CombatMilliseconds:0.###} " +
            $"path_ms={_lastSimulationMetrics.PathMilliseconds:0.###} " +
            $"visibility_ms={_lastSimulationMetrics.VisibilityMilliseconds:0.###} " +
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
            $"frames={_frame.Count} physics_ticks={_physics.Count}");
        Print("frame_ms", _frame);
        Print("physics_ms", _physics);
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
        Print("sim_production_ms", _simProduction);
        Print("sim_technology_ms", _simTechnology);
        Print("sim_economy_system_ms", _simEconomySystem);
        Print("sim_lifecycle_finalize_ms", _simLifecycleFinalize);
        Print("sim_combat_ms", _simCombat);
        Print("sim_path_ms", _simPath);
        Print("sim_preferred_velocity_ms", _simPreferredVelocity);
        Print("sim_choke_ms", _simChoke);
        Print("sim_spatial_hash_ms", _simSpatialHash);
        Print("sim_steering_ms", _simSteering);
        Print("sim_integrate_ms", _simIntegrate);
        Print("sim_collision_ms", _simCollision);
        Print("sim_recovery_ms", _simRecovery);
        Print("sim_queue_ms", _simQueue);
        Print("sim_visibility_ms", _simVisibility);
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
