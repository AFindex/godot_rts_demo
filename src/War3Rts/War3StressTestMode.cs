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
    private readonly List<int> _playerCombatUnits = [];
    private readonly List<int> _enemyCombatUnits = [];
    private readonly List<int> _builders = [];
    private readonly List<StressBuilding> _buildings = [];
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
    private bool _summaryPrinted;

    private War3StressTestMode(string[] arguments)
    {
        _unitsPerTeam = IntegerArgument(
            arguments, "--war3-stress-units-per-team=", 96, 8, 220);
        _builderCount = IntegerArgument(
            arguments, "--war3-stress-builders=", 8, 1, 24);
        _slotCount = IntegerArgument(
            arguments, "--war3-stress-build-slots=", 24, 4, 64);
        _buildIntervalTicks = IntegerArgument(
            arguments, "--war3-stress-build-interval=", 30, 1, 600);
        _buildingLifetimeTicks = IntegerArgument(
            arguments, "--war3-stress-building-lifetime=", 180, 15, 3_600);
        _combatRefreshTicks = IntegerArgument(
            arguments, "--war3-stress-combat-refresh=", 180, 15, 1_800);
        _respawnTicks = IntegerArgument(
            arguments, "--war3-stress-respawn=", 60, 15, 1_800);
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
        var arena = (runtime.PlayerHome + runtime.EnemyHome) * 0.5f;
        _playerCombatSpawn = arena + new Vector2(-playerToEnemy * 200f, -80f);
        _enemyCombatSpawn = arena + new Vector2(playerToEnemy * 200f, 80f);
        _playerCombatTarget = _enemyCombatSpawn;
        _enemyCombatTarget = _playerCombatSpawn;

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
        _nextStatusTick = tick + 300;
        GD.Print(
            "WAR3_STRESS_READY " +
            $"units_per_team={_unitsPerTeam} " +
            $"spawned={_playerCombatUnits.Count}/{_enemyCombatUnits.Count} " +
            $"builders={_builders.Count} slots={_slots.Length} " +
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
            _nextStatusTick = _simulation.Metrics.Tick + 300;
        }
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
            $"build_attempts={_buildIssueAttempts} " +
            $"build_accepted={_buildIssueAccepted} " +
            $"build_rejected={_buildIssueRejected} " +
            $"foundations={_foundationsStarted} " +
            $"completed={_buildingsCompleted} destroyed={_buildingsDestroyed} " +
            $"active_buildings={_buildings.Count(value => !value.Released)}");
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
        IssueCombatOrders();
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
        for (var index = 0; index < _builderCount &&
             _simulation.Units.Count < _simulation.Units.Capacity; index++)
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
        }
    }

    private bool TrySpawnCombatUnit(
        int team,
        List<int> units,
        ref int ordinal)
    {
        if (_simulation is null || _production is null ||
            _simulation.Units.Count >= _simulation.Units.Capacity)
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

    private void IssueCombatOrders()
    {
        if (_simulation is null) return;
        var player = LivingUnits(_playerCombatUnits);
        var enemy = LivingUnits(_enemyCombatUnits);
        if (player.Length > 0)
        {
            _simulation.IssueAttackMove(player, _playerCombatTarget);
            _combatOrdersIssued += player.Length;
        }
        if (enemy.Length > 0)
        {
            _simulation.IssueAttackMove(enemy, _enemyCombatTarget);
            _combatOrdersIssued += enemy.Length;
        }
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
        const int columns = 12;
        var column = ordinal % columns;
        var row = (ordinal / columns) % 18;
        var wave = ordinal / (columns * 18);
        var side = team == War3HumanScenario.PlayerId ? -1f : 1f;
        return center + new Vector2(
            side * (column * 36f + wave * 18f),
            (row - 8.5f) * 36f + (wave % 3 - 1) * 10f);
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
}
