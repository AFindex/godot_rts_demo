using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

public readonly record struct War3PresenterSyncProfile(
    double UnitsMilliseconds,
    long UnitsAllocatedBytes,
    double BuildingsMilliseconds,
    long BuildingsAllocatedBytes,
    double ResourcesMilliseconds,
    long ResourcesAllocatedBytes,
    double ProjectilesMilliseconds,
    long ProjectilesAllocatedBytes,
    double TransientsMilliseconds,
    long TransientsAllocatedBytes);

public readonly record struct War3AnimationAudioEvent(
    string EventCode,
    string SequenceName,
    NVector2 WorldPosition,
    int EmitterId,
    int SourcePlayerId,
    ulong Sequence);

/// <summary>Read-only Warcraft presentation of the authoritative RTS state.</summary>
public sealed partial class War3WorldPresenter : Node3D
{
    private const float SelectionHeight = 0.035f;
    private const int BuildingAudioEmitterBase = 1_000_000_000;
    private const int ProjectileAudioEmitterBase = 1_250_000_000;
    private readonly Dictionary<int, UnitVisual> _units = [];
    private readonly Dictionary<int, BuildingVisual> _buildings = [];
    private readonly Dictionary<int, ResourceVisual> _resources = [];
    private readonly List<War3StaticModelBatch> _resourceBatches = [];
    private readonly Dictionary<int, ProjectileVisual> _projectiles = [];
    private readonly List<TransientVisual> _transients = [];
    private readonly Dictionary<int, AbilityBuffVisual> _abilityBuffs = [];
    private readonly HashSet<int> _seenAbilityBuffs = [];
    private readonly HashSet<int> _seenBuildings = [];
    private readonly HashSet<int> _seenProjectiles = [];
    private readonly HashSet<int> _selectedUnits = [];
    private readonly HashSet<int> _selectedBuildings = [];
    private RtsSimulation? _simulation;
    private ProductionCatalogSnapshot? _production;
    private ITerrainMapQuery? _terrain;
    private Camera3D? _camera;
    private MeshInstance3D? _pointerPreview;
    private War3ModelActor? _pointerGhost;
    private string _pointerGhostSource = string.Empty;
    private War3ModelActor? _rallyMarker;
    private bool _rallyMarkerActive;
    private NVector2 _rallyMarkerPosition;
    private StandardMaterial3D? _validPreview;
    private StandardMaterial3D? _invalidPreview;
    private int _peakEffectCount;
    private bool _sawGoldGatherAnimation;
    private bool _sawLumberGatherAnimation;
    private bool _sawCarriedGoldAnimation;
    private bool _sawCarriedLumberAnimation;
    private bool _sawRepeatedLumberCycle;
    private bool _sawConstructionProgressAnimation;
    private bool _constructionAnimationMismatch;
    private bool _sawProgressiveLumberCargo;
    private bool _goldGatherUsedAttackAnimation;
    private bool _sawGoldMinerHidden;
    private bool _completedBuildingUsedLifecycleAnimation;
    private bool _sawBlendedTransition;
    private bool _sawConstructionEffect;
    private bool _idleTownHallEffectLeak;
    private bool _sawAttackTargetFacing;
    private bool _attackTargetFacingMismatch;
    private bool _sawConstructionGhost;
    private bool _foundationAppearedAfterApproach;
    private int _nextTransientAudioEmitterId = 1_500_000_000;
    private ulong _animationAudioSequence;
    private ulong _abilityEventCursor;

    public event Action<War3AnimationAudioEvent>? AnimationAudioEvent;

    public int PresentedUnitCount => _units.Values.Count(value => !value.Dying);
    public int PresentedBuildingCount => _buildings.Values.Count(value => !value.Dying);
    public int PresentedResourceCount => _resources.Count;
    public int ActiveEffectCount =>
        _projectiles.Count + _transients.Count + _abilityBuffs.Count;
    public int PeakEffectCount => _peakEffectCount;
    public bool SawGoldGatherAnimation => _sawGoldGatherAnimation;
    public bool SawLumberGatherAnimation => _sawLumberGatherAnimation;
    public bool SawCarriedGoldAnimation => _sawCarriedGoldAnimation;
    public bool SawCarriedLumberAnimation => _sawCarriedLumberAnimation;
    public bool SawRepeatedLumberCycle => _sawRepeatedLumberCycle;
    public bool SawConstructionProgressAnimation => _sawConstructionProgressAnimation;
    public bool ConstructionAnimationMismatch => _constructionAnimationMismatch;
    public bool SawProgressiveLumberCargo => _sawProgressiveLumberCargo;
    public bool GoldGatherUsedAttackAnimation => _goldGatherUsedAttackAnimation;
    public bool SawGoldMinerHidden => _sawGoldMinerHidden;
    public bool CompletedBuildingUsedLifecycleAnimation =>
        _completedBuildingUsedLifecycleAnimation;
    public bool SawBlendedTransition => _sawBlendedTransition;
    public bool SawConstructionEffect => _sawConstructionEffect;
    public bool IdleTownHallEffectLeak => _idleTownHallEffectLeak;
    public bool SawAttackTargetFacing => _sawAttackTargetFacing;
    public bool AttackTargetFacingMismatch => _attackTargetFacingMismatch;
    public bool SawConstructionGhost => _sawConstructionGhost;
    public bool FoundationAppearedAfterApproach => _foundationAppearedAfterApproach;
    public bool PointerPreviewUsesWar3Model => _pointerGhost?.Loaded == true;
    public bool RallyMarkerUsesWar3Model => _rallyMarker?.Loaded == true &&
        _rallyMarker.Source.Equals(
            "UI\\Feedback\\RallyPoint\\RallyPoint.mdx",
            StringComparison.OrdinalIgnoreCase);
    public bool ProfilingEnabled { get; set; }
    public War3PresenterSyncProfile LastSyncProfile { get; private set; }

    public void ApplyRuntimeProfileVariant(string variant)
    {
        switch (variant.ToLowerInvariant())
        {
            case "resources-hidden":
                foreach (var visual in _resources.Values)
                    if (visual.Actor is not null) visual.Actor.Visible = false;
                foreach (var batch in _resourceBatches) batch.Visible = false;
                break;
            case "units-hidden":
                foreach (var visual in _units.Values)
                    visual.Root.Visible = false;
                break;
            case "buildings-hidden":
                foreach (var visual in _buildings.Values)
                    visual.Root.Visible = false;
                break;
            case "models-no-shadow":
                foreach (var visual in _units.Values)
                    visual.Actor.SetShadowCastingEnabled(false);
                foreach (var visual in _buildings.Values)
                    visual.Actor.SetShadowCastingEnabled(false);
                foreach (var visual in _resources.Values)
                    visual.Actor?.SetShadowCastingEnabled(false);
                break;
            case "resources-no-shadow":
                foreach (var visual in _resources.Values)
                    visual.Actor?.SetShadowCastingEnabled(false);
                break;
            case "units-no-shadow":
                foreach (var visual in _units.Values)
                    visual.Actor.SetShadowCastingEnabled(false);
                break;
            case "buildings-no-shadow":
                foreach (var visual in _buildings.Values)
                    visual.Actor.SetShadowCastingEnabled(false);
                break;
        }
    }

    public void PrintRuntimeRenderLayout()
    {
        PrintCategoryRenderLayout("units", _units.Values.Select(value => value.Actor));
        PrintCategoryRenderLayout(
            "buildings", _buildings.Values.Select(value => value.Actor));
        PrintCategoryRenderLayout(
            "resources", _resources.Values
                .Where(value => value.Actor is not null)
                .Select(value => value.Actor!));
        GD.Print(
            $"WAR3_MODEL_RENDER_LAYOUT category=resource_batches " +
            $"batches={_resourceBatches.Count} " +
            $"instances={_resourceBatches.Sum(value => value.InstanceCount)} " +
            $"surfaces={_resourceBatches.Sum(value => value.SurfaceCount)} " +
            "shadow_surfaces=0");
    }

    private static void PrintCategoryRenderLayout(
        string category,
        IEnumerable<War3ModelActor> actors)
    {
        var actorCount = 0;
        var meshCount = 0;
        var surfaceCount = 0;
        var shadowSurfaceCount = 0;
        foreach (var actor in actors)
        {
            actorCount++;
            meshCount += actor.RenderMeshCount;
            surfaceCount += actor.RenderSurfaceCount;
            shadowSurfaceCount += actor.ShadowSurfaceCount;
        }
        GD.Print(
            $"WAR3_MODEL_RENDER_LAYOUT category={category} actors={actorCount} " +
            $"meshes={meshCount} surfaces={surfaceCount} " +
            $"shadow_surfaces={shadowSurfaceCount}");
    }

    public void Initialize(
        RtsSimulation simulation,
        ProductionCatalogSnapshot production,
        Camera3D camera)
    {
        _simulation = simulation;
        _production = production;
        _terrain = simulation.World.Terrain;
        _camera = camera;
        EnsurePointerPreview();
        EnsureRallyMarker();
        CreateStaticResourceBatches(simulation);
        Sync(1f);
        if (ProfilingEnabled) PrintRuntimeRenderLayout();
    }

    public void SetSelection(
        IEnumerable<int> units,
        IEnumerable<int> buildings)
    {
        _selectedUnits.Clear();
        _selectedUnits.UnionWith(units);
        _selectedBuildings.Clear();
        _selectedBuildings.UnionWith(buildings);
        foreach (var pair in _units)
            pair.Value.Selection.Visible = _selectedUnits.Contains(pair.Key) &&
                                           !pair.Value.Dying &&
                                           pair.Value.Actor.Visible;
        foreach (var pair in _buildings)
            pair.Value.Selection.Visible = _selectedBuildings.Contains(pair.Key) &&
                                           !pair.Value.Dying;
    }

    public void SetPointerPreview(
        NVector2 position,
        NVector2 footprint,
        string modelSource,
        bool valid)
    {
        EnsurePointerPreview();
        var size = SimPlane3DTransform.ToWorldSize(footprint);
        _pointerPreview!.Position = ToWorldAtGround(position, 0.06f);
        _pointerPreview.Scale = new Vector3(size.X, 0.06f, size.Y);
        _pointerPreview.MaterialOverride = valid ? _validPreview : _invalidPreview;
        _pointerPreview.Visible = true;
        if (_camera is not null && !modelSource.Equals(
                _pointerGhostSource, StringComparison.OrdinalIgnoreCase))
        {
            _pointerGhost?.QueueFree();
            _pointerGhost = new War3ModelActor { Name = "BuildModelGhost" };
            AddChild(_pointerGhost);
            _pointerGhost.Load(
                modelSource, _camera, War3HumanScenario.PlayerId,
                includeEffects: false);
            _pointerGhost.PlayPreferred(true, "Stand");
            _pointerGhostSource = modelSource;
        }
        if (_pointerGhost is not null)
        {
            _pointerGhost.Position = ToWorldAtGround(position, 0.01f);
            _pointerGhost.SetGhostAppearance(true, valid);
            _pointerGhost.Visible = true;
        }
    }

    public void HidePointerPreview()
    {
        if (_pointerPreview is not null) _pointerPreview.Visible = false;
        if (_pointerGhost is not null) _pointerGhost.Visible = false;
    }

    public void Sync(float interpolation)
    {
        if (_simulation is null || _production is null || _camera is null) return;
        interpolation = Math.Clamp(interpolation, 0f, 1f);
        var stageStart = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var allocationStart = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        SyncUnits(_simulation, _production, _camera, interpolation);
        var unitsEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var unitsAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        SyncBuildings(_simulation, _camera);
        SyncRallyMarker(_simulation);
        var buildingsEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var buildingsAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        SyncResources(_simulation, _camera);
        var resourcesEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var resourcesAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        SyncProjectiles(_simulation, _production, _camera);
        SyncAbilityEvents(_simulation, _camera);
        SyncAbilityBuffs(_simulation, _camera);
        var projectilesEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var projectilesAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        SyncTransientLifetime();
        var transientsEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var transientsAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        if (ProfilingEnabled)
        {
            LastSyncProfile = new War3PresenterSyncProfile(
                ElapsedMilliseconds(stageStart, unitsEnd),
                unitsAllocationEnd - allocationStart,
                ElapsedMilliseconds(unitsEnd, buildingsEnd),
                buildingsAllocationEnd - unitsAllocationEnd,
                ElapsedMilliseconds(buildingsEnd, resourcesEnd),
                resourcesAllocationEnd - buildingsAllocationEnd,
                ElapsedMilliseconds(resourcesEnd, projectilesEnd),
                projectilesAllocationEnd - resourcesAllocationEnd,
                ElapsedMilliseconds(projectilesEnd, transientsEnd),
                transientsAllocationEnd - projectilesAllocationEnd);
        }
        _peakEffectCount = Math.Max(_peakEffectCount, ActiveEffectCount);
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d / System.Diagnostics.Stopwatch.Frequency;

    private void SyncUnits(
        RtsSimulation simulation,
        ProductionCatalogSnapshot production,
        Camera3D camera,
        float interpolation)
    {
        for (var unit = 0; unit < simulation.Units.Count; unit++)
        {
            if (!simulation.Units.Alive[unit])
            {
                if (_units.TryGetValue(unit, out var dead) && !dead.Dying)
                {
                    dead.Dying = true;
                    dead.RemoveAt = Time.GetTicksMsec() + 3_400;
                    dead.Selection.Visible = false;
                    dead.Actor.PlayDeath();
                    if (dead.Definition.SpecialEffectSource.Length > 0 &&
                        War3RuntimeAssets.Contains(
                            dead.Definition.SpecialEffectSource))
                        SpawnTransient(
                            dead.Definition.SpecialEffectSource,
                            dead.LastPosition,
                            camera,
                            1_600,
                            simulation.Combat.Teams[unit]);
                }
                continue;
            }
            var resolvedDefinition = War3HumanContent.ResolveUnit(
                simulation, production, unit);
            if (!_units.TryGetValue(unit, out var visual))
            {
                visual = CreateUnit(
                    unit, resolvedDefinition,
                    simulation.Combat.Teams[unit], camera);
                _units.Add(unit, visual);
            }
            else if (!visual.Definition.ObjectId.Equals(
                         resolvedDefinition.ObjectId,
                         StringComparison.Ordinal))
            {
                visual.Root.QueueFree();
                visual = CreateUnit(
                    unit, resolvedDefinition,
                    simulation.Combat.Teams[unit], camera);
                _units[unit] = visual;
            }
            else if (visual.Dying)
            {
                visual.Dying = false;
                visual.RemoveAt = 0;
                visual.Actor.Revive();
            }
            var definition = visual.Definition;
            var position = NVector2.Lerp(
                simulation.Units.PreviousPositions[unit],
                simulation.Units.Positions[unit], interpolation);
            visual.LastPosition = position;
            var world = ToWorldAtGround(position, definition.FlyingHeight);
            visual.Actor.Position = world;
            var velocity = simulation.Units.Velocities[unit];
            if (TryResolveActionDirection(
                    simulation, unit, position, interpolation, out var attackDirection))
            {
                var angle = MathF.Atan2(attackDirection.X, attackDirection.Y);
                visual.Actor.Rotation = new Vector3(0f, angle, 0f);
                _sawAttackTargetFacing = true;
                _attackTargetFacingMismatch |= MathF.Abs(Mathf.AngleDifference(
                    visual.Actor.Rotation.Y, angle)) > 0.001f;
            }
            else if (velocity.LengthSquared() > 1f)
                visual.Actor.Rotation = new Vector3(
                    0f, MathF.Atan2(velocity.X, velocity.Y), 0f);
            var hiddenInsideGoldMine = IsGatheringGold(simulation, unit);
            visual.Actor.Visible = !hiddenInsideGoldMine;
            _sawGoldMinerHidden |= hiddenInsideGoldMine;
            visual.Selection.Position = new Vector3(
                world.X, world.Y + SelectionHeight, world.Z);
            visual.Selection.Visible = _selectedUnits.Contains(unit) &&
                                       !hiddenInsideGoldMine;
            UpdateUnitAnimation(simulation, unit, visual);
            _sawBlendedTransition |= visual.Actor.LastTransitionBlendSeconds > 0d;
        }

        foreach (var id in _units.Where(pair => pair.Value.Dying &&
                                                Time.GetTicksMsec() >= pair.Value.RemoveAt)
                     .Select(pair => pair.Key).ToArray())
        {
            _units[id].Root.QueueFree();
            _units.Remove(id);
        }
    }

    private static bool TryResolveActionDirection(
        RtsSimulation simulation,
        int unit,
        NVector2 attackerPosition,
        float interpolation,
        out NVector2 direction)
    {
        direction = NVector2.Zero;
        NVector2 targetPosition;
        if (simulation.Combat.Phases[unit] == CombatPhase.Attacking)
        {
            switch (simulation.Combat.TargetKinds[unit])
            {
                case CombatTargetKind.Unit:
                {
                    var target = simulation.Combat.TargetUnits[unit];
                    if ((uint)target >= (uint)simulation.Units.Count ||
                        !simulation.Units.Alive[target])
                        return false;
                    targetPosition = NVector2.Lerp(
                        simulation.Units.PreviousPositions[target],
                        simulation.Units.Positions[target], interpolation);
                    break;
                }
                case CombatTargetKind.Building:
                {
                    var target = new GameplayBuildingId(
                        simulation.Combat.TargetBuildings[unit]);
                    if (!simulation.Construction.IsAlive(target)) return false;
                    var building = simulation.Construction.Observe(target);
                    targetPosition = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
                    break;
                }
                default:
                    return false;
            }
        }
        else if (simulation.Economy.IsWorker(unit))
        {
            var worker = simulation.Economy.Worker(unit);
            if (worker.State != WorkerEconomyState.Gathering) return false;
            var resource = simulation.Economy.ObserveResourceNode(worker.TargetNode);
            if (resource.Kind != EconomyResourceKind.VespeneGas)
                return false;
            targetPosition = resource.Position;
        }
        else
        {
            return false;
        }
        direction = targetPosition - attackerPosition;
        return direction.LengthSquared() > 0.0001f;
    }

    private void UpdateUnitAnimation(
        RtsSimulation simulation,
        int unit,
        UnitVisual visual)
    {
        var moving = simulation.Units.Velocities[unit].LengthSquared() > 4f;
        var windup = simulation.Combat.WindupRemaining[unit];
        var cooldown = simulation.Combat.CooldownRemaining[unit];
        var attackCycleStarted =
            windup > 0f && visual.LastWindup <= 0f ||
            simulation.Combat.AttackWindupDurations[unit] <= 0f &&
            cooldown > visual.LastCooldown + 0.001f;
        visual.LastWindup = windup;
        visual.LastCooldown = cooldown;
        var abilityState = simulation.Abilities.Observe(unit);
        if (abilityState.CastPhase != AbilityCastPhase.None)
        {
            visual.Actor.PlayRepeatedPreferred(
                "Spell Channel", "Spell", "Spell Slam", "Attack");
            return;
        }
        if (Time.GetTicksMsec() < visual.AbilityAnimationUntil)
            return;
        if (simulation.Economy.IsWorker(unit))
        {
            if (simulation.Construction.IsAssignedBuilder(unit))
            {
                if (moving)
                    visual.Actor.PlayPreferred(true, "Walk", "Stand");
                else
                    visual.Actor.PlayPreferred(true, "Stand Work", "Attack", "Stand");
                return;
            }
            var worker = simulation.Economy.Worker(unit);
            var carriesLumber = worker.CargoAmount > 0 &&
                                worker.CargoKind == EconomyResourceKind.VespeneGas;
            var carriesGold = worker.CargoAmount > 0 &&
                              worker.CargoKind == EconomyResourceKind.Minerals;
            if (worker.State == WorkerEconomyState.Gathering)
            {
                var resource = simulation.Economy.ObserveResourceNode(worker.TargetNode);
                if (resource.Kind == EconomyResourceKind.VespeneGas)
                {
                    visual.Actor.PlayRepeatedPreferred(
                        "Attack Lumber", "Stand Work Lumber", "Attack", "Stand Work");
                    _sawLumberGatherAnimation |= visual.Actor.CurrentSequence.Contains(
                        "Lumber", StringComparison.OrdinalIgnoreCase);
                    _sawRepeatedLumberCycle |=
                        visual.Actor.RepeatedSequenceRestartCount > 0;
                    _sawProgressiveLumberCargo |= worker.CargoAmount > 0 &&
                        worker.CargoAmount < War3HumanScenario.LumberPerTrip;
                }
                else
                {
                    visual.Actor.PlayPreferred(true,
                        "Stand Gold", "Stand");
                    _sawGoldGatherAnimation |= visual.Actor.CurrentSequence.Contains(
                        "Gold", StringComparison.OrdinalIgnoreCase);
                    _goldGatherUsedAttackAnimation |=
                        visual.Actor.CurrentSequence.StartsWith(
                            "Attack", StringComparison.OrdinalIgnoreCase);
                }
                return;
            }
            if (worker.State is not WorkerEconomyState.None and
                not WorkerEconomyState.Idle)
            {
                if (moving)
                {
                    visual.Actor.PlayPreferred(true,
                        carriesLumber ? "Walk Lumber" : carriesGold ? "Walk Gold" : "Walk",
                        "Walk");
                    _sawCarriedLumberAnimation |= carriesLumber &&
                        visual.Actor.CurrentSequence.Contains(
                            "Lumber", StringComparison.OrdinalIgnoreCase);
                    _sawCarriedGoldAnimation |= carriesGold &&
                        visual.Actor.CurrentSequence.Contains(
                            "Gold", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    visual.Actor.PlayPreferred(true,
                        carriesLumber ? "Stand Lumber" : carriesGold ? "Stand Gold" : "Stand",
                        "Stand");
                }
                return;
            }
        }
        if (simulation.Combat.Phases[unit] == CombatPhase.Attacking)
        {
            if (attackCycleStarted)
                visual.Actor.ReplayPreferred("Attack", "Spell Attack", "Spell");
            else if (!visual.Actor.IsAnimationPlaying ||
                     !visual.Actor.CurrentSequence.StartsWith(
                         "Attack", StringComparison.OrdinalIgnoreCase))
                visual.Actor.PlayPreferred(true, "Stand Ready", "Stand");
            return;
        }
        if (simulation.Economy.IsWorker(unit))
        {
            var worker = simulation.Economy.Worker(unit);
            var carriesLumber = worker.CargoAmount > 0 &&
                                worker.CargoKind == EconomyResourceKind.VespeneGas;
            var carriesGold = worker.CargoAmount > 0 &&
                              worker.CargoKind == EconomyResourceKind.Minerals;
            if (moving)
            {
                visual.Actor.PlayPreferred(true,
                    carriesLumber ? "Walk Lumber" : carriesGold ? "Walk Gold" : "Walk",
                    "Walk");
                _sawCarriedLumberAnimation |= carriesLumber &&
                    visual.Actor.CurrentSequence.Contains(
                        "Lumber", StringComparison.OrdinalIgnoreCase);
                _sawCarriedGoldAnimation |= carriesGold &&
                    visual.Actor.CurrentSequence.Contains(
                        "Gold", StringComparison.OrdinalIgnoreCase);
                return;
            }
            visual.Actor.PlayPreferred(true,
                carriesLumber ? "Stand Lumber" : carriesGold ? "Stand Gold" : "Stand",
                "Stand");
            return;
        }
        if (moving)
            visual.Actor.PlayPreferred(true, "Walk", "Stand");
        else if (simulation.Combat.Phases[unit] != CombatPhase.None)
            visual.Actor.PlayPreferred(true, "Stand Ready", "Stand");
        else
            visual.Actor.PlayPreferred(true, "Stand");
    }

    private void SyncAbilityEvents(
        RtsSimulation simulation,
        Camera3D camera)
    {
        var batch = simulation.AbilityEvents.ReadAfter(_abilityEventCursor);
        _abilityEventCursor = batch.LatestSequence;
        foreach (var value in batch.Events)
        {
            if (!War3HumanContent.TryAbility(
                    value.AbilityId, out var definition) || definition is null)
                continue;
            var sourcePlayerId = (uint)value.CasterUnit <
                                 (uint)simulation.Units.Count
                ? simulation.Combat.Teams[value.CasterUnit]
                : value.CasterBuilding >= 0 &&
                  simulation.Construction.IsAlive(
                      new GameplayBuildingId(value.CasterBuilding))
                    ? simulation.Construction.Observe(
                        new GameplayBuildingId(value.CasterBuilding)).PlayerId
                    : -1;
            if (value.Kind == AbilityEventKind.Started)
            {
                if (_units.TryGetValue(value.CasterUnit, out var caster))
                {
                    caster.AbilityAnimationUntil = Time.GetTicksMsec() + 700;
                    caster.Actor.ReplayPreferred(
                        "Spell", "Spell Slam", "Spell Channel", "Attack");
                }
                SpawnAbilityModels(
                    definition.CasterModels,
                    AbilityCasterPosition(simulation, value),
                    camera, sourcePlayerId);
                continue;
            }
            if (value.Kind != AbilityEventKind.Impact) continue;
            var position = AbilityTargetPosition(simulation, value);
            SpawnAbilityModels(
                definition.TargetModels, position, camera, sourcePlayerId);
            SpawnAbilityModels(
                definition.EffectModels, position, camera, sourcePlayerId);
            SpawnAbilityModels(
                definition.MissileModels, position, camera, sourcePlayerId);
        }
    }

    private void SpawnAbilityModels(
        IEnumerable<string> models,
        NVector2 position,
        Camera3D camera,
        int sourcePlayerId)
    {
        foreach (var model in models
                     .Where(War3RuntimeAssets.Contains)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(6))
            SpawnTransient(model, position, camera, 1_800, sourcePlayerId);
    }

    private void SyncAbilityBuffs(
        RtsSimulation simulation,
        Camera3D camera)
    {
        _seenAbilityBuffs.Clear();
        foreach (var buff in simulation.Abilities.ObserveAllBuffs())
        {
            if ((uint)buff.TargetUnit >= (uint)simulation.Units.Count ||
                !simulation.Units.Alive[buff.TargetUnit])
                continue;
            _seenAbilityBuffs.Add(buff.InstanceId);
            if (!_abilityBuffs.TryGetValue(buff.InstanceId, out var visual))
            {
                var definition = War3HumanContent.Ability(buff.AbilityId);
                var source = definition.TargetModels
                    .Concat(definition.EffectModels)
                    .FirstOrDefault(War3RuntimeAssets.Contains);
                if (source is null) continue;
                var actor = new War3ModelActor
                {
                    Name = $"AbilityBuff{buff.InstanceId}"
                };
                AddChild(actor);
                var sourcePlayerId = simulation.Combat.Teams[buff.SourceUnit];
                var emitterId = _nextTransientAudioEmitterId++;
                actor.SoundTimelineEvent += value => PublishAnimationAudio(
                    value,
                    simulation.Units.Positions[buff.TargetUnit],
                    emitterId,
                    sourcePlayerId);
                actor.Load(
                    source, camera,
                    simulation.Combat.Teams[buff.TargetUnit]);
                actor.PlayPreferred(true, "Stand", "Birth");
                visual = new AbilityBuffVisual(actor, buff.TargetUnit);
                _abilityBuffs.Add(buff.InstanceId, visual);
            }
            visual.Actor.Position = ToWorldAtGround(
                simulation.Units.Positions[visual.TargetUnit], 0.18f);
        }

        foreach (var id in _abilityBuffs.Keys
                     .Where(value => !_seenAbilityBuffs.Contains(value))
                     .ToArray())
        {
            _abilityBuffs[id].Actor.QueueFree();
            _abilityBuffs.Remove(id);
        }
    }

    private static NVector2 AbilityCasterPosition(
        RtsSimulation simulation,
        in AbilityEvent value) =>
        (uint)value.CasterUnit < (uint)simulation.Units.Count
            ? simulation.Units.Positions[value.CasterUnit]
            : value.CasterBuilding >= 0 &&
              simulation.Construction.IsAlive(
                  new GameplayBuildingId(value.CasterBuilding))
                ? Center(simulation.Construction.Observe(
                    new GameplayBuildingId(value.CasterBuilding)).Bounds)
                : value.WorldPosition;

    private static NVector2 Center(in SimRect bounds) =>
        (bounds.Min + bounds.Max) * 0.5f;

    private static NVector2 AbilityTargetPosition(
        RtsSimulation simulation,
        in AbilityEvent value)
    {
        if (value.TargetKind is AbilityTargetKind.Unit or AbilityTargetKind.Self &&
            (uint)value.TargetId < (uint)simulation.Units.Count)
            return simulation.Units.Positions[value.TargetId];
        if (value.TargetKind == AbilityTargetKind.Building)
        {
            var building = new GameplayBuildingId(value.TargetId);
            if (simulation.Construction.IsAlive(building))
            {
                var bounds = simulation.Construction.Observe(building).Bounds;
                return (bounds.Min + bounds.Max) * 0.5f;
            }
        }
        return value.WorldPosition;
    }

    private static bool IsGatheringGold(RtsSimulation simulation, int unit)
    {
        if (!simulation.Economy.IsWorker(unit)) return false;
        var worker = simulation.Economy.Worker(unit);
        if (worker.State != WorkerEconomyState.Gathering) return false;
        return simulation.Economy.ObserveResourceNode(worker.TargetNode).Kind ==
               EconomyResourceKind.Minerals;
    }

    private void SyncBuildings(RtsSimulation simulation, Camera3D camera)
    {
        _seenBuildings.Clear();
        foreach (var building in simulation.CreateGameplayBuildingOverview())
        {
            if (building.IsTerminal) continue;
            var id = building.Id.Value;
            _seenBuildings.Add(id);
            var definition = War3HumanContent.Buildings[building.Type.Id];
            if (!_buildings.TryGetValue(id, out var visual))
            {
                visual = CreateBuilding(building, definition, camera);
                _buildings.Add(id, visual);
            }
            if (!visual.LayoutInitialized)
            {
                var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
                visual.LastPosition = center;
                var world = ToWorldAtGround(center);
                visual.Actor.Position = world;
                var diameter = MathF.Max(
                    SimPlane3DTransform.ToWorldLength(building.Type.Size.X),
                    SimPlane3DTransform.ToWorldLength(building.Type.Size.Y)) * 0.62f;
                visual.Selection.Position = new Vector3(
                    world.X, world.Y + SelectionHeight, world.Z);
                visual.Selection.Scale = Vector3.One * MathF.Max(0.85f, diameter);
                visual.ShadowProxy.Position = new Vector3(
                    world.X,
                    world.Y + visual.ShadowProxyHeight * 0.5f,
                    world.Z);
                visual.LayoutInitialized = true;
            }
            var foundationStarted = building.FootprintId.Value > 0 ||
                                    building.State is BuildingLifecycleState.Constructing or
                                        BuildingLifecycleState.Completed;
            var ghost = !foundationStarted;
            visual.ShadowProxy.Visible = foundationStarted && !visual.Dying;
            if (!visual.GhostInitialized || visual.IsGhost != ghost)
            {
                visual.Actor.SetGhostAppearance(ghost);
                visual.IsGhost = ghost;
                visual.GhostInitialized = true;
            }
            visual.Selection.Visible = foundationStarted && _selectedBuildings.Contains(id);
            if (ghost)
            {
                visual.Actor.SetSequenceProgress(0f, "Stand");
                visual.WasGhost = true;
                _sawConstructionGhost = true;
            }
            else if (building.State != BuildingLifecycleState.Completed)
            {
                _foundationAppearedAfterApproach |= visual.WasGhost;
                var synchronized = visual.Actor.SetSequenceProgress(
                    building.Progress, "Birth", "Stand");
                _sawConstructionProgressAnimation |= building.Progress is > 0.001f and < 0.999f;
                _constructionAnimationMismatch |= !synchronized ||
                    !visual.Actor.IsProgressDriven || visual.Actor.IsAnimationPlaying ||
                    MathF.Abs(visual.Actor.DrivenProgress - building.Progress) > 0.001f;
                _sawConstructionEffect |= visual.Actor.LiveEffectCount > 0;
            }
            else if (simulation.Production.Observe(building.Id).Orders.Length > 0)
                visual.Actor.PlayPreferred(true, "Stand Work", "Stand");
            else
                visual.Actor.PlayPreferred(true, "Stand");
            if (building.State == BuildingLifecycleState.Completed)
            {
                var sequence = visual.Actor.CurrentSequence;
                _completedBuildingUsedLifecycleAnimation |=
                    sequence.StartsWith("Birth", StringComparison.OrdinalIgnoreCase) ||
                    sequence.StartsWith("Death", StringComparison.OrdinalIgnoreCase) ||
                    sequence.StartsWith("Decay", StringComparison.OrdinalIgnoreCase);
                var orders = simulation.Production.Observe(building.Id).Orders;
                if (building.Type.Id == War3HumanContent.TownHall &&
                    orders.Length == 0 &&
                    sequence.Equals("Stand", StringComparison.OrdinalIgnoreCase))
                    _idleTownHallEffectLeak |= visual.Actor.LiveEffectCount > 0;
            }
        }

        foreach (var pair in _buildings.Where(pair => !_seenBuildings.Contains(pair.Key) &&
                                                       !pair.Value.Dying).ToArray())
        {
            pair.Value.Dying = true;
            pair.Value.RemoveAt = Time.GetTicksMsec() + 4_000;
            pair.Value.Selection.Visible = false;
            pair.Value.ShadowProxy.Visible = false;
            pair.Value.Actor.PlayDeath();
            if (pair.Value.Definition.SpecialEffectSource.Length > 0 &&
                War3RuntimeAssets.Contains(
                    pair.Value.Definition.SpecialEffectSource))
                SpawnTransient(
                    pair.Value.Definition.SpecialEffectSource,
                    pair.Value.LastPosition,
                    camera,
                    1_600);
        }
        foreach (var id in _buildings.Where(pair => pair.Value.Dying &&
                                                    Time.GetTicksMsec() >= pair.Value.RemoveAt)
                     .Select(pair => pair.Key).ToArray())
        {
            _buildings[id].Root.QueueFree();
            _buildings.Remove(id);
        }
    }

    private void SyncResources(RtsSimulation simulation, Camera3D camera)
    {
        for (var id = 0; id < simulation.Economy.ResourceNodeCount; id++)
        {
            var snapshot = simulation.Economy.ObserveResourceNode(
                new EconomyResourceNodeId(id));
            if (!_resources.TryGetValue(id, out var visual))
            {
                var source = snapshot.Kind == EconomyResourceKind.Minerals
                    ? War3HumanContent.GoldMineSource
                    : War3HumanContent.TreeSource(id);
                visual = CreateResource(id, snapshot.Kind, source, camera);
                _resources.Add(id, visual);
            }
            if (!visual.Positioned)
            {
                if (visual.Actor is not null)
                    visual.Actor.Position = ToWorldAtGround(snapshot.Position);
                visual.Positioned = true;
            }
            if (snapshot.Remaining <= 0)
            {
                if (!visual.Depleted)
                {
                    visual.Depleted = true;
                    if (visual.StaticBatch is not null)
                    {
                        visual.StaticBatch.SetInstanceVisible(
                            visual.StaticBatchIndex, false);
                        visual.Actor = CreateResourceActor(
                            id, visual.Kind, visual.Source, camera);
                        visual.Actor.Position = ToWorldAtGround(snapshot.Position);
                        visual.Actor.SetShadowCastingEnabled(false);
                    }
                    visual.Actor?.PlayDeath();
                }
                continue;
            }
            var working = snapshot.ActiveHarvesters > 0 &&
                          snapshot.Kind == EconomyResourceKind.Minerals;
            if (!visual.AnimationInitialized || visual.Working != working)
            {
                visual.Actor?.PlayPreferred(
                    true, working ? "Stand Work" : "Stand");
                visual.Working = working;
                visual.AnimationInitialized = true;
            }
        }
    }

    private void SyncProjectiles(
        RtsSimulation simulation,
        ProductionCatalogSnapshot production,
        Camera3D camera)
    {
        _seenProjectiles.Clear();
        foreach (var projectile in simulation.CombatProjectiles.ObserveActive())
        {
            _seenProjectiles.Add(projectile.Id);
            if ((uint)projectile.AttackerUnit >= (uint)simulation.Units.Count) continue;
            var definition = _units.TryGetValue(
                projectile.AttackerUnit, out var attackerVisual)
                ? attackerVisual.Definition
                : War3HumanContent.ResolveUnit(
                    simulation, production, projectile.AttackerUnit);
            if (!_projectiles.TryGetValue(projectile.Id, out var visual))
            {
                visual = CreateProjectile(
                    projectile.Id,
                    definition,
                    simulation.Combat.Teams[projectile.AttackerUnit],
                    camera);
                _projectiles.Add(projectile.Id, visual);
            }
            var displacement = projectile.Position - visual.LastPosition;
            if (visual.HasPosition && displacement.LengthSquared() > 0.0001f)
                visual.Root.Rotation = new Vector3(
                    0f, MathF.Atan2(displacement.X, displacement.Y), 0f);
            visual.LastPosition = projectile.Position;
            visual.HasPosition = true;
            var height = MathF.Max(0.7f, definition.FlyingHeight * 0.55f + 0.55f);
            visual.Root.Position = ToWorldAtGround(projectile.Position, height);
        }
        foreach (var id in _projectiles.Keys.Where(id => !_seenProjectiles.Contains(id)).ToArray())
        {
            var visual = _projectiles[id];
            visual.Root.QueueFree();
            _projectiles.Remove(id);
            if (visual.Definition.ImpactSource.Length > 0 &&
                War3RuntimeAssets.Contains(visual.Definition.ImpactSource))
                SpawnTransient(
                    visual.Definition.ImpactSource,
                    visual.LastPosition,
                    camera,
                    1_300,
                    visual.SourcePlayerId);
        }
    }

    private UnitVisual CreateUnit(
        int id,
        War3UnitDefinition definition,
        int team,
        Camera3D camera)
    {
        var root = new Node3D { Name = $"Unit{id}_{definition.ObjectId}" };
        var actor = new War3ModelActor { Name = "Actor" };
        var selection = SelectionRing($"UnitSelection{id}", 0.034f,
            team == War3HumanScenario.PlayerId ? new Color("46d8ff") : new Color("ff5c58"));
        AddChild(root);
        root.AddChild(actor);
        root.AddChild(selection);
        actor.SoundTimelineEvent += value =>
            PublishUnitAnimationAudio(id, team, value);
        actor.Load(definition.ModelSource, camera, team);
        var diameter = SimPlane3DTransform.ToWorldLength(
            MathF.Max(18f, _simulation!.Units.Radii[id] * 2.8f));
        selection.Scale = Vector3.One * diameter;
        selection.Visible = _selectedUnits.Contains(id);
        return new UnitVisual(root, actor, selection, definition);
    }

    private BuildingVisual CreateBuilding(
        GameplayBuildingSnapshot building,
        War3BuildingDefinition definition,
        Camera3D camera)
    {
        var root = new Node3D { Name = $"Building{building.Id.Value}_{definition.ObjectId}" };
        var actor = new War3ModelActor { Name = "Actor" };
        var selection = SelectionRing($"BuildingSelection{building.Id.Value}", 0.034f,
            building.PlayerId == War3HumanScenario.PlayerId
                ? new Color("46d8ff")
                : new Color("ff5c58"));
        AddChild(root);
        root.AddChild(actor);
        root.AddChild(selection);
        actor.SoundTimelineEvent += value => PublishBuildingAnimationAudio(
            building.Id.Value, building.PlayerId, value);
        actor.Load(definition.ModelSource, camera, building.PlayerId);
        // Imported buildings can contain dozens of geoset surfaces. Submitting
        // every surface into multiple directional-light cascades is much more
        // expensive than their main color pass, so retain a single cheap
        // footprint proxy for grounding and disable source-mesh shadow passes.
        actor.SetShadowCastingEnabled(false);
        var footprint = SimPlane3DTransform.ToWorldSize(
            building.Bounds.Max - building.Bounds.Min);
        var proxyHeight = MathF.Max(0.8f, MathF.Max(footprint.X, footprint.Y) * 0.72f);
        var shadowProxy = new MeshInstance3D
        {
            Name = "ShadowProxy",
            Mesh = new BoxMesh
            {
                Size = new Vector3(
                    MathF.Max(0.55f, footprint.X * 0.72f),
                    proxyHeight,
                    MathF.Max(0.55f, footprint.Y * 0.72f))
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
            Visible = false
        };
        root.AddChild(shadowProxy);
        return new BuildingVisual(
            root, actor, selection, shadowProxy, proxyHeight, definition);
    }

    private ResourceVisual CreateResource(
        int id,
        EconomyResourceKind kind,
        string source,
        Camera3D camera)
    {
        var actor = CreateResourceActor(id, kind, source, camera);
        actor.PlayPreferred(true, "Stand");
        return new ResourceVisual(actor, kind, source)
        {
            AnimationInitialized = true
        };
    }

    private War3ModelActor CreateResourceActor(
        int id,
        EconomyResourceKind kind,
        string source,
        Camera3D camera)
    {
        var actor = new War3ModelActor { Name = $"Resource{id}" };
        AddChild(actor);
        actor.Load(
            source, camera, 0,
            includeEffects: kind == EconomyResourceKind.Minerals);
        return actor;
    }

    private void CreateStaticResourceBatches(RtsSimulation simulation)
    {
        var trees = new List<(int Id, string Source, NVector2 Position)>();
        for (var id = 0; id < simulation.Economy.ResourceNodeCount; id++)
        {
            var snapshot = simulation.Economy.ObserveResourceNode(
                new EconomyResourceNodeId(id));
            if (snapshot.Kind == EconomyResourceKind.Minerals) continue;
            trees.Add((id, War3HumanContent.TreeSource(id), snapshot.Position));
        }
        foreach (var group in trees.GroupBy(value => value.Source)
                     .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase))
        {
            var values = group.OrderBy(value => value.Id).ToArray();
            var batch = new War3StaticModelBatch
            {
                Name = $"StaticTrees{_resourceBatches.Count}"
            };
            AddChild(batch);
            batch.Initialize(group.Key, values.Length);
            _resourceBatches.Add(batch);
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                batch.SetInstanceTransform(
                    index,
                    new Transform3D(
                        Basis.Identity,
                        ToWorldAtGround(value.Position)));
                _resources.Add(
                    value.Id,
                    new ResourceVisual(
                        actor: null,
                        EconomyResourceKind.VespeneGas,
                        value.Source,
                        batch,
                        index)
                    {
                        Positioned = true,
                        AnimationInitialized = true
                    });
            }
        }
    }

    private ProjectileVisual CreateProjectile(
        int id,
        War3UnitDefinition definition,
        int sourcePlayerId,
        Camera3D camera)
    {
        var root = new Node3D { Name = $"Projectile{id}" };
        AddChild(root);
        if (definition.ProjectileSource.Length > 0 &&
            War3RuntimeAssets.Contains(definition.ProjectileSource))
        {
            var actor = new War3ModelActor { Name = "ProjectileActor" };
            root.AddChild(actor);
            actor.SoundTimelineEvent += value => PublishProjectileAnimationAudio(
                id, sourcePlayerId, value);
            actor.Load(definition.ProjectileSource, camera, 0);
            actor.PlayPreferred(true, "Stand", "Birth");
        }
        else
        {
            var mesh = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.075f, Height = 0.15f },
                MaterialOverride = Emissive(new Color("ffd46a")),
                Scale = Vector3.One * 1.2f
            };
            root.AddChild(mesh);
        }
        return new ProjectileVisual(
            root, definition, NVector2.Zero, sourcePlayerId);
    }

    private void SpawnTransient(
        string source,
        NVector2 position,
        Camera3D camera,
        ulong lifetime,
        int sourcePlayerId = -1)
    {
        var actor = new War3ModelActor { Name = "Impact" };
        var emitterId = _nextTransientAudioEmitterId++;
        AddChild(actor);
        actor.Position = ToWorldAtGround(position, 0.18f);
        actor.SoundTimelineEvent += value => PublishAnimationAudio(
            value, position, emitterId, sourcePlayerId);
        actor.Load(source, camera, 0);
        actor.PlayPreferred(false, "Birth", "Stand", "Death");
        _transients.Add(new TransientVisual(actor, Time.GetTicksMsec() + lifetime));
    }

    private void PublishUnitAnimationAudio(
        int unit,
        int sourcePlayerId,
        War3ModelSoundTimelineEvent value)
    {
        if (!_units.TryGetValue(unit, out var visual)) return;
        PublishAnimationAudio(
            value, visual.LastPosition, unit, sourcePlayerId);
    }

    private void PublishBuildingAnimationAudio(
        int building,
        int sourcePlayerId,
        War3ModelSoundTimelineEvent value)
    {
        if (!_buildings.TryGetValue(building, out var visual)) return;
        PublishAnimationAudio(
            value,
            visual.LastPosition,
            BuildingAudioEmitterBase + building,
            sourcePlayerId);
    }

    private void PublishProjectileAnimationAudio(
        int projectile,
        int sourcePlayerId,
        War3ModelSoundTimelineEvent value)
    {
        if (!_projectiles.TryGetValue(projectile, out var visual)) return;
        PublishAnimationAudio(
            value,
            visual.LastPosition,
            ProjectileAudioEmitterBase + projectile,
            sourcePlayerId);
    }

    private void PublishAnimationAudio(
        War3ModelSoundTimelineEvent value,
        NVector2 position,
        int emitterId,
        int sourcePlayerId) =>
        AnimationAudioEvent?.Invoke(new War3AnimationAudioEvent(
            value.EventCode,
            value.SequenceName,
            position,
            emitterId,
            sourcePlayerId,
            ++_animationAudioSequence));

    private void SyncTransientLifetime()
    {
        for (var index = _transients.Count - 1; index >= 0; index--)
        {
            if (Time.GetTicksMsec() < _transients[index].RemoveAt) continue;
            _transients[index].Root.QueueFree();
            _transients.RemoveAt(index);
        }
    }

    private void EnsurePointerPreview()
    {
        if (_pointerPreview is not null) return;
        _validPreview = PreviewMaterial(new Color("42d99a66"));
        _invalidPreview = PreviewMaterial(new Color("ef5f5f72"));
        _pointerPreview = new MeshInstance3D
        {
            Name = "BuildPreview",
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = _validPreview,
            Visible = false
        };
        AddChild(_pointerPreview);
    }

    private void EnsureRallyMarker()
    {
        if (_rallyMarker is not null) return;
        if (_camera is null) return;
        _rallyMarker = new War3ModelActor { Name = "RallyPointMarker" };
        _rallyMarker.Visible = false;
        AddChild(_rallyMarker);
        _rallyMarker.Load(
            "UI\\Feedback\\RallyPoint\\RallyPoint.mdx",
            _camera,
            War3HumanScenario.PlayerId,
            includeEffects: false);
    }

    private void SyncRallyMarker(RtsSimulation simulation)
    {
        EnsureRallyMarker();
        if (_rallyMarker is null) return;
        var found = false;
        foreach (var value in _selectedBuildings.Order())
        {
            var id = new GameplayBuildingId(value);
            if (!simulation.Construction.IsAlive(id)) continue;
            var rally = simulation.Production.Observe(id).Rally;
            if (!rally.IsSet) continue;
            found = true;
            var moved = !_rallyMarkerActive ||
                        NVector2.DistanceSquared(_rallyMarkerPosition, rally.Position) > 0.01f;
            _rallyMarkerPosition = rally.Position;
            _rallyMarker.Position = ToWorldAtGround(rally.Position, 0.01f);
            _rallyMarker.Visible = true;
            if (moved)
                _rallyMarker.ReplayPreferred("Birth", "Stand");
            else if (!_rallyMarker.IsAnimationPlaying)
                _rallyMarker.PlayPreferred(true, "Stand");
            _rallyMarkerActive = true;
            break;
        }
        if (found) return;
        _rallyMarker.Visible = false;
        _rallyMarkerActive = false;
    }

    private float GroundWorldHeight(NVector2 position) =>
        _terrain is null
            ? 0f
            : SimPlane3DTransform.ToWorldLength(_terrain.HeightAt(position));

    private Vector3 ToWorldAtGround(
        NVector2 position,
        float localHeight = 0f) =>
        SimPlane3DTransform.ToWorld(
            position,
            GroundWorldHeight(position) + localHeight);

    private static MeshInstance3D SelectionRing(string name, float width, Color color)
    {
        var material = Emissive(color);
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        material.NoDepthTest = false;
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new TorusMesh
            {
                InnerRadius = MathF.Max(0.1f, 1f - width),
                OuterRadius = 1f,
                Rings = 32,
                RingSegments = 8
            },
            MaterialOverride = material,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
    }

    private static StandardMaterial3D Emissive(Color color) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = 1.4f,
        Roughness = 0.5f
    };

    private static StandardMaterial3D PreviewMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = false
    };

    private sealed class UnitVisual(
        Node3D root,
        War3ModelActor actor,
        MeshInstance3D selection,
        War3UnitDefinition definition)
    {
        public Node3D Root { get; } = root;
        public War3ModelActor Actor { get; } = actor;
        public MeshInstance3D Selection { get; } = selection;
        public War3UnitDefinition Definition { get; } = definition;
        public float LastWindup { get; set; }
        public float LastCooldown { get; set; }
        public NVector2 LastPosition { get; set; }
        public bool Dying { get; set; }
        public ulong RemoveAt { get; set; }
        public ulong AbilityAnimationUntil { get; set; }
    }

    private sealed class BuildingVisual(
        Node3D root,
        War3ModelActor actor,
        MeshInstance3D selection,
        MeshInstance3D shadowProxy,
        float shadowProxyHeight,
        War3BuildingDefinition definition)
    {
        public Node3D Root { get; } = root;
        public War3ModelActor Actor { get; } = actor;
        public MeshInstance3D Selection { get; } = selection;
        public MeshInstance3D ShadowProxy { get; } = shadowProxy;
        public float ShadowProxyHeight { get; } = shadowProxyHeight;
        public War3BuildingDefinition Definition { get; } = definition;
        public NVector2 LastPosition { get; set; }
        public bool WasGhost { get; set; }
        public bool Dying { get; set; }
        public ulong RemoveAt { get; set; }
        public bool LayoutInitialized { get; set; }
        public bool GhostInitialized { get; set; }
        public bool IsGhost { get; set; }
    }

    private sealed class ResourceVisual(
        War3ModelActor? actor,
        EconomyResourceKind kind,
        string source,
        War3StaticModelBatch? staticBatch = null,
        int staticBatchIndex = -1)
    {
        public War3ModelActor? Actor { get; set; } = actor;
        public EconomyResourceKind Kind { get; } = kind;
        public string Source { get; } = source;
        public War3StaticModelBatch? StaticBatch { get; } = staticBatch;
        public int StaticBatchIndex { get; } = staticBatchIndex;
        public bool Depleted { get; set; }
        public bool Positioned { get; set; }
        public bool Working { get; set; }
        public bool AnimationInitialized { get; set; }
    }

    private sealed record ProjectileVisual(
        Node3D Root,
        War3UnitDefinition Definition,
        NVector2 LastPosition,
        int SourcePlayerId)
    {
        public NVector2 LastPosition { get; set; } = LastPosition;
        public bool HasPosition { get; set; }
    }

    private sealed record TransientVisual(Node3D Root, ulong RemoveAt);
    private sealed record AbilityBuffVisual(
        War3ModelActor Actor,
        int TargetUnit);
}
