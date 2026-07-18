using System.Diagnostics;
using System.Numerics;
using RtsDemo.Simulation;
using GD = Godot.GD;
using Vector2 = System.Numerics.Vector2;

namespace War3Rts;

internal readonly record struct War3StressUpdateProfile(
    double TotalMilliseconds,
    double BuildingLifecycleMilliseconds,
    double UnitRespawnMilliseconds,
    double CombatOrderMilliseconds,
    double ConstructionIssueMilliseconds,
    long AllocatedBytes);

/// <summary>
/// Opt-in workload director for the Warcraft battlefield. It only exists when
/// --war3-stress-test is present and builds test-local profiles instead of
/// mutating the production catalogs or economy rules.
/// </summary>
internal sealed class War3StressTestMode
{
    private const string EnableArgument = "--war3-stress-test";
    private readonly int _unitsPerTeam;
    private readonly int _builderCount;
    private readonly int _slotCount;
    private readonly int _buildIntervalTicks;
    private readonly int _buildingLifetimeTicks;
    private readonly int _combatRefreshTicks;
    private readonly int _respawnTicks;
    private readonly int _armyInset;
    private readonly int _qualityReportTicks;
    private readonly List<int> _playerCombatUnits = [];
    private readonly List<int> _enemyCombatUnits = [];
    private readonly List<int> _builders = [];
    private readonly List<StressBuilding> _buildings = [];
    private readonly Dictionary<int, UnitQualityProbe> _qualityProbes = [];
    private readonly Dictionary<int, int> _targetClaims = [];
    private readonly List<int> _pathReadyLatencies = [];
    private readonly List<int> _preferredVelocityLatencies = [];
    private readonly List<int> _actualMotionLatencies = [];
    private BuildSlot[] _slots = [];
    private RtsSimulation? _simulation;
    private ProductionCatalogSnapshot? _production;
    private BuildingTypeProfile _stressBuildingProfile;
    private Vector2 _playerCombatSpawn;
    private Vector2 _enemyCombatSpawn;
    private Vector2 _playerCombatTarget;
    private Vector2 _enemyCombatTarget;
    private long _nextBuildTick;
    private long _nextCombatRefreshTick;
    private long _nextRespawnTick;
    private long _nextStatusTick;
    private int _builderCursor;
    private int _slotCursor;
    private int _playerSpawnOrdinal;
    private int _enemySpawnOrdinal;
    private int _buildIssueAttempts;
    private int _buildIssueAccepted;
    private int _buildIssueRejected;
    private int _foundationsStarted;
    private int _buildingsCompleted;
    private int _buildingsDestroyed;
    private int _combatUnitsSpawned;
    private int _combatOrdersIssued;
    private int _combatOrderRefreshes;
    private long _combatOrderCoalesces;
    private int _supersededMotionProbes;
    private long _qualitySamples;
    private long _pathWaitingUnitTicks;
    private long _preferredVelocityStallUnitTicks;
    private long _targetAssignmentSamples;
    private long _uniqueTargetSamples;
    private int _movementStopEvents;
    private int _headingReversalEvents;
    private int _targetSwitchEvents;
    private int _peakTargetClaimants;
    private int _peakPendingPathRequests;
    private int _currentTargetAssignments;
    private int _currentUniqueTargets;
    private int _currentMaximumTargetClaimants;
    private int _currentOverfocusedTargets;
    private float _initialArmySeparation;
    private bool _summaryPrinted;

    private War3StressTestMode(string[] arguments)
    {
        _unitsPerTeam = IntegerArgument(
            arguments, "--war3-stress-units-per-team=", 96, 8, 4_096);
        _builderCount = IntegerArgument(
            arguments, "--war3-stress-builders=", 8, 1, 256);
        _slotCount = IntegerArgument(
            arguments, "--war3-stress-build-slots=", 24, 4, 1_024);
        _buildIntervalTicks = IntegerArgument(
            arguments, "--war3-stress-build-interval=", 30, 1, 600);
        _buildingLifetimeTicks = IntegerArgument(
            arguments, "--war3-stress-building-lifetime=", 180, 15, 3_600);
        _combatRefreshTicks = IntegerArgument(
            arguments, "--war3-stress-combat-refresh=", 180, 15, 1_800);
        _respawnTicks = IntegerArgument(
            arguments, "--war3-stress-respawn=", 60, 15, 1_800);
        _armyInset = IntegerArgument(
            arguments, "--war3-stress-army-inset=", 1_050, 400, 2_400);
        _qualityReportTicks = IntegerArgument(
            arguments, "--war3-stress-quality-report=", 300, 60, 3_600);
    }

    public War3StressUpdateProfile LastUpdateProfile { get; private set; }
    public Vector2 FocusPoint => (_playerCombatSpawn + _enemyCombatSpawn) * 0.5f;

    public static bool IsRequested(string[] arguments) =>
        arguments.Contains(EnableArgument);

    public static War3StressTestMode? TryCreate(string[] arguments) =>
        IsRequested(arguments) ? new War3StressTestMode(arguments) : null;

    public void Initialize(
        RtsSimulation simulation,
        ProductionCatalogSnapshot production,
        BuildingTypeCatalogSnapshot buildings,
        War3HumanRuntime runtime)
    {
        _simulation = simulation;
        _production = production;
        simulation.DetailedProfilingEnabled = true;

        var playerToEnemy = MathF.Sign(runtime.EnemyHome.X - runtime.PlayerHome.X);
        if (playerToEnemy == 0) playerToEnemy = 1;
        _playerCombatSpawn = runtime.PlayerHome +
                             new Vector2(playerToEnemy * _armyInset, 0f);
        _enemyCombatSpawn = runtime.EnemyHome -
                            new Vector2(playerToEnemy * _armyInset, 0f);
        _playerCombatTarget = _enemyCombatSpawn;
        _enemyCombatTarget = _playerCombatSpawn;
        _initialArmySeparation = Vector2.Distance(
            _playerCombatSpawn, _enemyCombatSpawn);

        var farm = buildings.Type(War3HumanContent.Farm);
        _stressBuildingProfile = farm with
        {
            Name = $"{farm.Name} [stress]",
            Cost = default,
            BuildSeconds = 0.35f,
            SupplyProvided = 0,
            ConstructionMethod = ConstructionMethodKind.StartAndRelease,
            RequiresVespeneNode = false
        };
        CreateBuildSlots(runtime.PlayerHome, playerToEnemy, farm.Size);
        SpawnBuilders(runtime.PlayerHome, playerToEnemy);
        SpawnInitialCombatArmies();
        IssueCombatOrders();

        var tick = simulation.Metrics.Tick;
        _nextBuildTick = tick + 1;
        _nextCombatRefreshTick = tick + _combatRefreshTicks;
        _nextRespawnTick = tick + _respawnTicks;
        _nextStatusTick = tick + _qualityReportTicks;
        GD.Print(
            "WAR3_STRESS_READY " +
            $"units_per_team={_unitsPerTeam} " +
            $"spawned={_playerCombatUnits.Count}/{_enemyCombatUnits.Count} " +
            $"builders={_builders.Count} slots={_slots.Length} " +
            $"army_centers={_playerCombatSpawn.X:0},{_playerCombatSpawn.Y:0}/" +
            $"{_enemyCombatSpawn.X:0},{_enemyCombatSpawn.Y:0} " +
            $"army_separation={_initialArmySeparation:0} " +
            $"build_interval_ticks={_buildIntervalTicks} " +
            $"building_lifetime_ticks={_buildingLifetimeTicks} " +
            "construction_cost=free auto_respawn=true auto_destroy=true");
    }

    public void Update()
    {
        if (_simulation is null) return;
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var updateStart = Stopwatch.GetTimestamp();
        ProcessBuildingLifecycle();
        var lifecycleEnd = Stopwatch.GetTimestamp();
        RefreshCombatPopulation();
        var respawnEnd = Stopwatch.GetTimestamp();
        RefreshCombatOrders();
        var combatOrderEnd = Stopwatch.GetTimestamp();
        IssueNextConstruction();
        var constructionEnd = Stopwatch.GetTimestamp();
        LastUpdateProfile = new War3StressUpdateProfile(
            ElapsedMilliseconds(updateStart, constructionEnd),
            ElapsedMilliseconds(updateStart, lifecycleEnd),
            ElapsedMilliseconds(lifecycleEnd, respawnEnd),
            ElapsedMilliseconds(respawnEnd, combatOrderEnd),
            ElapsedMilliseconds(combatOrderEnd, constructionEnd),
            GC.GetAllocatedBytesForCurrentThread() - allocationStart);

        if (_simulation.Metrics.Tick >= _nextStatusTick)
        {
            PrintStatus();
            _nextStatusTick =
                _simulation.Metrics.Tick + _qualityReportTicks;
        }
    }

    /// <summary>
    /// Samples the authoritative result after the simulation tick. Keeping this
    /// out of Update makes command issue, path-ready, preferred-velocity and
    /// actual-motion latency observable as separate stages.
    /// </summary>
    public void ObserveAfterSimulation()
    {
        if (_simulation is null) return;
        ObserveTeamQuality(_playerCombatUnits);
        ObserveTeamQuality(_enemyCombatUnits);
        ObserveTargetDistribution();
        _peakPendingPathRequests = Math.Max(
            _peakPendingPathRequests,
            _simulation.PendingPathRequestCountForDiagnostics);
        _qualitySamples++;
    }

    public void PrintSummary()
    {
        if (_summaryPrinted || _simulation is null) return;
        _summaryPrinted = true;
        GD.Print(
            "WAR3_STRESS_SUMMARY " +
            $"tick={_simulation.Metrics.Tick} " +
            $"unit_slots={_simulation.Units.Count}/{_simulation.Units.Capacity} " +
            $"combat_spawned={_combatUnitsSpawned} " +
            $"combat_alive={AliveCount(_playerCombatUnits)}/" +
            $"{AliveCount(_enemyCombatUnits)} " +
            $"combat_orders={_combatOrdersIssued} " +
            $"order_refreshes={_combatOrderRefreshes} " +
            $"attack_move_coalesced={_combatOrderCoalesces} " +
            $"build_attempts={_buildIssueAttempts} " +
            $"build_accepted={_buildIssueAccepted} " +
            $"build_rejected={_buildIssueRejected} " +
            $"foundations={_foundationsStarted} " +
            $"completed={_buildingsCompleted} destroyed={_buildingsDestroyed} " +
            $"active_buildings={_buildings.Count(value => !value.Released)} " +
            QualitySummary());
    }

    private void ProcessBuildingLifecycle()
    {
        if (_simulation is null) return;
        var tick = _simulation.Metrics.Tick;
        for (var index = 0; index < _buildings.Count; index++)
        {
            var entry = _buildings[index];
            if (entry.Released) continue;
            if (!_simulation.Construction.IsAlive(entry.Id))
            {
                Release(entry);
                continue;
            }

            var snapshot = _simulation.Construction.Observe(entry.Id);
            if (!entry.FoundationObserved && snapshot.FootprintId.Value > 0)
            {
                entry.FoundationObserved = true;
                _foundationsStarted++;
            }
            if (!entry.CompletionObserved &&
                snapshot.State == BuildingLifecycleState.Completed)
            {
                entry.CompletionObserved = true;
                entry.DestroyTick = tick + _buildingLifetimeTicks;
                _buildingsCompleted++;
            }
            if (entry.CompletionObserved && tick >= entry.DestroyTick)
            {
                if (_simulation.DamageBuilding(
                        entry.Id, snapshot.MaximumHealth + snapshot.Health + 1f))
                {
                    _buildingsDestroyed++;
                    Release(entry);
                }
                continue;
            }
            if (snapshot.State == BuildingLifecycleState.BlockedAtStart &&
                tick - entry.IssuedTick >= 600)
            {
                if (_simulation.CancelConstruction(
                        War3HumanScenario.PlayerId, entry.Id))
                {
                    _buildIssueRejected++;
                    Release(entry);
                }
            }
        }
    }

    private void RefreshCombatPopulation()
    {
        if (_simulation is null || _production is null ||
            _simulation.Metrics.Tick < _nextRespawnTick)
            return;
        _nextRespawnTick = _simulation.Metrics.Tick + _respawnTicks;
        RefillTeam(
            War3HumanScenario.PlayerId, _playerCombatUnits,
            ref _playerSpawnOrdinal);
        RefillTeam(
            War3HumanScenario.EnemyId, _enemyCombatUnits,
            ref _enemySpawnOrdinal);
    }

    private void RefreshCombatOrders()
    {
        if (_simulation is null ||
            _simulation.Metrics.Tick < _nextCombatRefreshTick)
            return;
        _nextCombatRefreshTick =
            _simulation.Metrics.Tick + _combatRefreshTicks;
        IssueCombatOrders(refresh: true);
    }

    private void IssueNextConstruction()
    {
        if (_simulation is null || _builders.Count == 0 ||
            _simulation.Metrics.Tick < _nextBuildTick)
            return;
        _nextBuildTick = _simulation.Metrics.Tick + _buildIntervalTicks;
        var builder = FindAvailableBuilder();
        var slot = FindAvailableSlot();
        if (builder < 0 || slot < 0) return;

        _buildIssueAttempts++;
        var result = _simulation.IssueConstruction(
            War3HumanScenario.PlayerId,
            builder,
            _stressBuildingProfile,
            _slots[slot].Center);
        if (!result.Succeeded)
        {
            _buildIssueRejected++;
            return;
        }

        _slots[slot].Occupied = true;
        _buildings.Add(new StressBuilding(
            result.BuildingId, slot, _simulation.Metrics.Tick));
        _buildIssueAccepted++;
    }

    private void SpawnBuilders(Vector2 playerHome, float direction)
    {
        if (_simulation is null || _production is null) return;
        var profile = _production.UnitType(War3HumanContent.Peasant);
        for (var index = 0; index < _builderCount; index++)
        {
            var slot = _slots.Length == 0
                ? playerHome
                : _slots[index % _slots.Length].Center;
            var position = slot + new Vector2(
                -direction * (_stressBuildingProfile.Size.X * 0.5f +
                              profile.Movement.PhysicalRadius + 8f),
                0f);
            var unit = _simulation.AddUnit(
                position, profile.Movement, War3HumanScenario.PlayerId,
                profile.Combat, profile.Perception);
            _simulation.Economy.RegisterWorker(
                unit, War3HumanScenario.PlayerId);
            _builders.Add(unit);
        }
    }

    private void SpawnInitialCombatArmies()
    {
        if (_simulation is null || _production is null) return;
        for (var index = 0; index < _unitsPerTeam; index++)
        {
            if (!TrySpawnCombatUnit(
                    War3HumanScenario.PlayerId, _playerCombatUnits,
                    ref _playerSpawnOrdinal))
                break;
            if (!TrySpawnCombatUnit(
                    War3HumanScenario.EnemyId, _enemyCombatUnits,
                    ref _enemySpawnOrdinal))
                break;
        }
    }

    private void RefillTeam(int team, List<int> units, ref int ordinal)
    {
        while (AliveCount(units) < _unitsPerTeam &&
               TrySpawnCombatUnit(team, units, ref ordinal))
        {
            var unit = units[^1];
            IssueTeamCombatOrder(
                [unit],
                team == War3HumanScenario.PlayerId
                    ? _playerCombatTarget
                    : _enemyCombatTarget,
                refresh: false);
        }
    }

    private bool TrySpawnCombatUnit(
        int team,
        List<int> units,
        ref int ordinal)
    {
        if (_simulation is null || _production is null)
            return false;
        int[] unitTypes =
        [
            War3HumanContent.Footman,
            War3HumanContent.Rifleman,
            War3HumanContent.Priest,
            War3HumanContent.MortarTeam
        ];
        var profile = _production.UnitType(unitTypes[ordinal % unitTypes.Length]);
        var spawn = team == War3HumanScenario.PlayerId
            ? _playerCombatSpawn
            : _enemyCombatSpawn;
        var position = FormationPosition(spawn, ordinal, team);
        var unit = _simulation.AddUnit(
            position, profile.Movement, team, profile.Combat,
            profile.Perception);
        units.Add(unit);
        ordinal++;
        _combatUnitsSpawned++;
        return true;
    }

    private void IssueCombatOrders(bool refresh = false)
    {
        if (_simulation is null) return;
        var player = LivingUnits(_playerCombatUnits);
        var enemy = LivingUnits(_enemyCombatUnits);
        IssueTeamCombatOrder(player, _playerCombatTarget, refresh);
        IssueTeamCombatOrder(enemy, _enemyCombatTarget, refresh);
    }

    private void IssueTeamCombatOrder(
        ReadOnlySpan<int> units,
        Vector2 target,
        bool refresh)
    {
        if (_simulation is null || units.IsEmpty) return;
        var coalescedBefore =
            _simulation.Metrics.RepeatedAttackMoveUnitsCoalesced;
        _simulation.IssueAttackMove(units, target);
        var coalesced =
            _simulation.Metrics.RepeatedAttackMoveUnitsCoalesced -
            coalescedBefore;
        _combatOrdersIssued += units.Length;
        if (refresh) _combatOrderRefreshes += units.Length;
        _combatOrderCoalesces += coalesced;
        if (coalesced == units.Length)
            return;
        for (var index = 0; index < units.Length; index++)
            BeginMotionProbe(units[index]);
    }

    private void CreateBuildSlots(
        Vector2 playerHome,
        float direction,
        Vector2 buildingSize)
    {
        if (_simulation is null) return;
        var spacing = Vector2.Max(buildingSize + new Vector2(96f),
            new Vector2(176f, 160f));
        var preferredCenter = playerHome + new Vector2(direction * 760f, 680f);
        var candidates = new List<BuildSlot>(_slotCount);
        for (var row = -8; row <= 8 && candidates.Count < _slotCount; row++)
        {
            for (var column = -8;
                 column <= 8 && candidates.Count < _slotCount;
                 column++)
            {
                var center = preferredCenter + new Vector2(
                    direction * column * spacing.X,
                    row * spacing.Y);
                var halfSize = buildingSize * 0.5f;
                var assessment = BuildingPlacementValidator.Evaluate(
                    _simulation.World,
                    _simulation.Units,
                    new SimRect(center - halfSize, center + halfSize),
                    new BuildingPlacementRules(
                        _stressBuildingProfile.MinimumPassageClass,
                        _stressBuildingProfile.PlacementProfile.UnitPadding,
                        PreserveConnectivity: false),
                    connectivityGuard: null,
                    includeDynamicFootprint: null,
                    includeUnit: _ => false);
                if (assessment.Succeeded)
                    candidates.Add(new BuildSlot(center));
            }
        }
        _slots = candidates.ToArray();
        if (_slots.Length == 0)
            throw new InvalidOperationException(
                "War3 stress mode could not find a buildable construction slot.");
    }

    private int FindAvailableBuilder()
    {
        if (_simulation is null) return -1;
        for (var attempt = 0; attempt < _builders.Count; attempt++)
        {
            var index = (_builderCursor + attempt) % _builders.Count;
            var builder = _builders[index];
            if (!_simulation.Units.Alive[builder] ||
                _simulation.Construction.IsAssignedBuilder(builder))
                continue;
            _builderCursor = (index + 1) % _builders.Count;
            return builder;
        }
        return -1;
    }

    private int FindAvailableSlot()
    {
        for (var attempt = 0; attempt < _slots.Length; attempt++)
        {
            var index = (_slotCursor + attempt) % _slots.Length;
            if (_slots[index].Occupied) continue;
            _slotCursor = (index + 1) % _slots.Length;
            return index;
        }
        return -1;
    }

    private void Release(StressBuilding entry)
    {
        entry.Released = true;
        if ((uint)entry.Slot < (uint)_slots.Length)
            _slots[entry.Slot].Occupied = false;
    }

    private void PrintStatus()
    {
        if (_simulation is null) return;
        GD.Print(
            "WAR3_STRESS_STATUS " +
            $"tick={_simulation.Metrics.Tick} " +
            $"unit_slots={_simulation.Units.Count}/{_simulation.Units.Capacity} " +
            $"combat_alive={AliveCount(_playerCombatUnits)}/" +
            $"{AliveCount(_enemyCombatUnits)} " +
            $"active_buildings={_buildings.Count(value => !value.Released)} " +
            $"foundations={_foundationsStarted} completed={_buildingsCompleted} " +
            $"destroyed={_buildingsDestroyed} rejected={_buildIssueRejected}");
        PrintQualityStatus();
    }

    private void BeginMotionProbe(int unit)
    {
        if (_simulation is null) return;
        if (!_qualityProbes.TryGetValue(unit, out var probe))
        {
            probe = new UnitQualityProbe();
            _qualityProbes.Add(unit, probe);
        }
        probe.CommandTick = _simulation.Metrics.Tick;
        probe.CommandVersion = _simulation.Units.CommandVersions[unit];
        probe.CommandPosition = _simulation.Units.Positions[unit];
        probe.PathReadyRecorded = false;
        probe.PreferredVelocityRecorded = false;
        probe.ActualMotionRecorded = false;
        probe.AwaitingCommandMotion = true;
    }

    private void ObserveTeamQuality(List<int> units)
    {
        if (_simulation is null) return;
        var tick = _simulation.Metrics.Tick;
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            if ((uint)unit >= (uint)_simulation.Units.Count ||
                !_simulation.Units.Alive[unit])
                continue;
            if (!_qualityProbes.TryGetValue(unit, out var probe))
            {
                probe = new UnitQualityProbe();
                _qualityProbes.Add(unit, probe);
            }

            if (probe.AwaitingCommandMotion)
            {
                if (_simulation.Units.CommandVersions[unit] !=
                    probe.CommandVersion)
                {
                    probe.AwaitingCommandMotion = false;
                    _supersededMotionProbes++;
                }
                else
                {
                    var latency = checked((int)Math.Max(
                        0L, tick - probe.CommandTick));
                    var path = _simulation.Units.Paths[unit];
                    if (!probe.PathReadyRecorded &&
                        path is not null &&
                        path.CommandVersion == probe.CommandVersion &&
                        !_simulation.Units.PathPending[unit])
                    {
                        probe.PathReadyRecorded = true;
                        _pathReadyLatencies.Add(latency);
                    }
                    if (!probe.PreferredVelocityRecorded &&
                        _simulation.Units.PreferredVelocities[unit]
                            .LengthSquared() > 4f)
                    {
                        probe.PreferredVelocityRecorded = true;
                        _preferredVelocityLatencies.Add(latency);
                    }
                    if (!probe.ActualMotionRecorded &&
                        (Vector2.DistanceSquared(
                             probe.CommandPosition,
                             _simulation.Units.Positions[unit]) > 1f ||
                         _simulation.Units.Velocities[unit].LengthSquared() >
                         4f))
                    {
                        probe.ActualMotionRecorded = true;
                        _actualMotionLatencies.Add(latency);
                    }
                    if (probe.PathReadyRecorded &&
                        probe.PreferredVelocityRecorded &&
                        probe.ActualMotionRecorded)
                        probe.AwaitingCommandMotion = false;
                }
            }

            var movementActive =
                _simulation.Units.MovementLegResults[unit] ==
                UnitMovementLegResult.InProgress &&
                _simulation.Units.Modes[unit] is
                    UnitMoveMode.Moving or UnitMoveMode.WaitingForPath;
            if (movementActive &&
                _simulation.Units.Modes[unit] == UnitMoveMode.WaitingForPath)
                _pathWaitingUnitTicks++;

            var marching = movementActive &&
                           _simulation.Combat.CommandIntents[unit] ==
                           UnitCommandIntent.AttackMove &&
                           _simulation.Combat.TargetKinds[unit] ==
                           CombatTargetKind.None &&
                           Vector2.DistanceSquared(
                               _simulation.Units.Positions[unit],
                               _simulation.Units.SlotTargets[unit]) >
                           64f * 64f;
            var velocity = _simulation.Units.Velocities[unit];
            var moving = velocity.LengthSquared() > 4f;
            if (marching &&
                _simulation.Units.Modes[unit] == UnitMoveMode.Moving &&
                _simulation.Units.PreferredVelocities[unit]
                    .LengthSquared() <= 4f)
                _preferredVelocityStallUnitTicks++;
            if (probe.StateInitialized && marching &&
                probe.WasMarchMoving && !moving)
                _movementStopEvents++;
            if (probe.StateInitialized && marching &&
                probe.WasMarchMoving && moving &&
                Vector2.Dot(probe.LastVelocity, velocity) <
                -0.25f * MathF.Sqrt(
                    probe.LastVelocity.LengthSquared() *
                    velocity.LengthSquared()))
                _headingReversalEvents++;

            var target = _simulation.Combat.TargetKinds[unit] ==
                         CombatTargetKind.Unit
                ? _simulation.Combat.TargetUnits[unit]
                : -1;
            if (probe.StateInitialized && probe.LastTarget >= 0 &&
                target >= 0 && target != probe.LastTarget)
                _targetSwitchEvents++;
            probe.LastTarget = target;
            probe.LastVelocity = velocity;
            probe.WasMarchMoving = marching && moving;
            probe.StateInitialized = true;
        }
    }

    private void ObserveTargetDistribution()
    {
        if (_simulation is null) return;
        _targetClaims.Clear();
        CountTargetClaims(_playerCombatUnits);
        CountTargetClaims(_enemyCombatUnits);
        _currentTargetAssignments = 0;
        _currentMaximumTargetClaimants = 0;
        _currentOverfocusedTargets = 0;
        foreach (var claims in _targetClaims.Values)
        {
            _currentTargetAssignments += claims;
            _currentMaximumTargetClaimants = Math.Max(
                _currentMaximumTargetClaimants, claims);
            if (claims > 4) _currentOverfocusedTargets++;
        }
        _currentUniqueTargets = _targetClaims.Count;
        _peakTargetClaimants = Math.Max(
            _peakTargetClaimants, _currentMaximumTargetClaimants);
        _targetAssignmentSamples += _currentTargetAssignments;
        _uniqueTargetSamples += _currentUniqueTargets;
    }

    private void CountTargetClaims(List<int> units)
    {
        if (_simulation is null) return;
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            if ((uint)unit >= (uint)_simulation.Units.Count ||
                !_simulation.Units.Alive[unit] ||
                _simulation.Combat.TargetKinds[unit] != CombatTargetKind.Unit)
                continue;
            var target = _simulation.Combat.TargetUnits[unit];
            if (target < 0) continue;
            _targetClaims[target] =
                _targetClaims.GetValueOrDefault(target) + 1;
        }
    }

    private void PrintQualityStatus()
    {
        if (_simulation is null) return;
        GD.Print(
            "WAR3_STRESS_QUALITY " +
            $"tick={_simulation.Metrics.Tick} " +
            $"path_ready={LatencySummary(_pathReadyLatencies)} " +
            $"preferred={LatencySummary(_preferredVelocityLatencies)} " +
            $"motion={LatencySummary(_actualMotionLatencies)} " +
            $"pending_probes={PendingMotionProbeCount()} " +
            $"superseded={_supersededMotionProbes} " +
            $"path_wait_unit_ticks={_pathWaitingUnitTicks} " +
            $"preferred_stall_unit_ticks={_preferredVelocityStallUnitTicks} " +
            $"stops={_movementStopEvents} reversals={_headingReversalEvents} " +
            $"targets={_currentTargetAssignments}/" +
            $"{_currentUniqueTargets}/max{_currentMaximumTargetClaimants}/" +
            $"over4={_currentOverfocusedTargets} " +
            $"target_switches={_targetSwitchEvents} " +
            $"separation={CurrentArmySeparation():0}/" +
            $"{_initialArmySeparation:0} " +
            $"path_queue={_simulation.PendingPathRequestCountForDiagnostics}/" +
            $"peak{_peakPendingPathRequests}");
    }

    private string QualitySummary()
    {
        var averageAssignments = _qualitySamples == 0
            ? 0d
            : (double)_targetAssignmentSamples / _qualitySamples;
        var averageUniqueTargets = _qualitySamples == 0
            ? 0d
            : (double)_uniqueTargetSamples / _qualitySamples;
        return
            $"quality_path={LatencySummary(_pathReadyLatencies)} " +
            $"quality_preferred={LatencySummary(_preferredVelocityLatencies)} " +
            $"quality_motion={LatencySummary(_actualMotionLatencies)} " +
            $"quality_pending={PendingMotionProbeCount()} " +
            $"quality_wait_ticks={_pathWaitingUnitTicks} " +
            $"quality_stall_ticks={_preferredVelocityStallUnitTicks} " +
            $"quality_stops={_movementStopEvents} " +
            $"quality_reversals={_headingReversalEvents} " +
            $"quality_target_avg={averageAssignments:0.0}/" +
            $"{averageUniqueTargets:0.0} " +
            $"quality_target_peak={_peakTargetClaimants} " +
            $"quality_target_switches={_targetSwitchEvents} " +
            $"quality_path_queue_peak={_peakPendingPathRequests}";
    }

    private int PendingMotionProbeCount() =>
        _qualityProbes.Values.Count(value => value.AwaitingCommandMotion);

    private float CurrentArmySeparation()
    {
        if (!TryTeamCentroid(_playerCombatUnits, out var player) ||
            !TryTeamCentroid(_enemyCombatUnits, out var enemy))
            return 0f;
        return Vector2.Distance(player, enemy);
    }

    private bool TryTeamCentroid(List<int> units, out Vector2 centroid)
    {
        centroid = Vector2.Zero;
        if (_simulation is null) return false;
        var count = 0;
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            if ((uint)unit >= (uint)_simulation.Units.Count ||
                !_simulation.Units.Alive[unit])
                continue;
            centroid += _simulation.Units.Positions[unit];
            count++;
        }
        if (count == 0) return false;
        centroid /= count;
        return true;
    }

    private static string LatencySummary(List<int> values)
    {
        if (values.Count == 0) return "0/p50-1/p95-1/max-1";
        var sorted = values.ToArray();
        Array.Sort(sorted);
        return $"{sorted.Length}/p50{Percentile(sorted, 0.50f)}/" +
               $"p95{Percentile(sorted, 0.95f)}/max{sorted[^1]}";
    }

    private static int Percentile(int[] sorted, float percentile)
    {
        var index = (int)MathF.Ceiling(sorted.Length * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private int AliveCount(List<int> units)
    {
        if (_simulation is null) return 0;
        var count = 0;
        for (var index = 0; index < units.Count; index++)
            count += _simulation.Units.Alive[units[index]] ? 1 : 0;
        return count;
    }

    private int[] LivingUnits(List<int> units)
    {
        if (_simulation is null) return [];
        return units.Where(value => _simulation.Units.Alive[value]).ToArray();
    }

    private static Vector2 FormationPosition(
        Vector2 center,
        int ordinal,
        int team)
    {
        // Build the formation away from the front line. The previous wrapped
        // 12x18 layout started overlapping at unit 216, which made a nominal
        // 800-unit test mostly measure collision recovery at spawn time.
        const int lanes = 20;
        const float spacing = 32f;
        var lane = ordinal % lanes;
        var depth = ordinal / lanes;
        var side = team == War3HumanScenario.PlayerId ? -1f : 1f;
        return center + new Vector2(
            side * depth * spacing,
            (lane - (lanes - 1) * 0.5f) * spacing);
    }

    private static int IntegerArgument(
        string[] arguments,
        string prefix,
        int fallback,
        int minimum,
        int maximum)
    {
        var value = arguments.FirstOrDefault(argument => argument.StartsWith(
            prefix, StringComparison.OrdinalIgnoreCase));
        return value is not null &&
               int.TryParse(value[prefix.Length..], out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d / Stopwatch.Frequency;

    private sealed class BuildSlot(Vector2 center)
    {
        public Vector2 Center { get; } = center;
        public bool Occupied { get; set; }
    }

    private sealed class StressBuilding(
        GameplayBuildingId id,
        int slot,
        long issuedTick)
    {
        public GameplayBuildingId Id { get; } = id;
        public int Slot { get; } = slot;
        public long IssuedTick { get; } = issuedTick;
        public long DestroyTick { get; set; } = long.MaxValue;
        public bool FoundationObserved { get; set; }
        public bool CompletionObserved { get; set; }
        public bool Released { get; set; }
    }

    private sealed class UnitQualityProbe
    {
        public long CommandTick { get; set; }
        public int CommandVersion { get; set; }
        public Vector2 CommandPosition { get; set; }
        public bool AwaitingCommandMotion { get; set; }
        public bool PathReadyRecorded { get; set; }
        public bool PreferredVelocityRecorded { get; set; }
        public bool ActualMotionRecorded { get; set; }
        public bool StateInitialized { get; set; }
        public bool WasMarchMoving { get; set; }
        public Vector2 LastVelocity { get; set; }
        public int LastTarget { get; set; } = -1;
    }
}
