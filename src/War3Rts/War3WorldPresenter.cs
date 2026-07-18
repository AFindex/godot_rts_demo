using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;
using War3Rts.Data;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

public readonly record struct War3PresenterSyncProfile(
    double UnitsMilliseconds,
    long UnitsAllocatedBytes,
    double UnitAnimationMilliseconds,
    int UnitActorsVisited,
    int UnitActorsAlive,
    int UnitActorsInFrustum,
    int UnitActorsCreated,
    long UnitResolveAllocatedBytes,
    long UnitFrustumAllocatedBytes,
    long UnitLodAllocatedBytes,
    long UnitAnimationAllocatedBytes,
    double BuildingsMilliseconds,
    long BuildingsAllocatedBytes,
    int BuildingActorsVisited,
    int BuildingActorsInFrustum,
    long BuildingOverviewAllocatedBytes,
    long BuildingAnimationAllocatedBytes,
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

public readonly record struct War3TreeHarvestFeedbackEvent(
    string WorkerObjectId,
    int WorkerUnit,
    int ResourceNode,
    int WeaponSlot,
    string SoundFamily,
    NVector2 WorldPosition,
    int SourcePlayerId,
    ulong Sequence);

/// <summary>Read-only Warcraft presentation of the authoritative RTS state.</summary>
public sealed partial class War3WorldPresenter : Node3D
{
    // Warcraft's selection circle uses a 32-coordinate base radius and MDX
    // models are imported at 0.01 world units per source coordinate.
    private const float WarcraftSelectionCircleWorldRadius = 32f * 0.01f;
    private const float SelectionHeight = 0.035f;
    private const int BuildingAudioEmitterBase = 1_000_000_000;
    private const int ProjectileAudioEmitterBase = 1_250_000_000;
    private const int MaximumProjectileVisualCreationsPerSync = 4;
    private const int MaximumProjectileImpactVisualsPerSync = 4;
    private const int MaximumNonCommandTransientVisuals = 256;
    private const int DenseUnitLodThreshold = 240;
    private const int MaximumFullDetailUnits = 192;
    private readonly Dictionary<int, UnitVisual> _units = [];
    private readonly Dictionary<int, BuildingVisual> _buildings = [];
    private readonly Dictionary<int, ResourceVisual> _resources = [];
    private readonly List<War3StaticModelBatch> _resourceBatches = [];
    private readonly Dictionary<(string Source, int Team), War3StaticModelBatch>
        _unitLodBatches = [];
    private readonly Dictionary<int, ProjectileVisual> _projectiles = [];
    private readonly Dictionary<int, BuildingProjectileVisual>
        _buildingProjectiles = [];
    private readonly Dictionary<int, AbilityProjectileVisual>
        _abilityProjectiles = [];
    private readonly List<TransientVisual> _transients = [];
    private readonly Dictionary<int, AbilityBuffVisual> _abilityBuffs = [];
    private readonly HashSet<int> _seenAbilityBuffs = [];
    private readonly HashSet<int> _seenBuildings = [];
    private readonly HashSet<int> _seenProjectiles = [];
    private readonly HashSet<int> _seenBuildingProjectiles = [];
    private readonly HashSet<int> _seenAbilityProjectiles = [];
    private readonly HashSet<int> _selectedUnits = [];
    private readonly HashSet<int> _selectedBuildings = [];
    private CylinderMesh? _unitShadowProxyMesh;
    private readonly Dictionary<string, War3TreeHarvestFeedbackProfile>
        _treeHarvestProfiles = new(StringComparer.Ordinal);
    private RtsSimulation? _simulation;
    private ProductionCatalogSnapshot? _production;
    private ITerrainMapQuery? _terrain;
    private Camera3D? _camera;
    private MeshInstance3D? _pointerPreview;
    private War3ModelActor? _pointerGhost;
    private string _pointerGhostSource = string.Empty;
    private MeshInstance3D? _abilityTargetPreview;
    private MeshInstance3D? _abilityRangePreview;
    private War3ModelActor? _rallyMarker;
    private bool _rallyMarkerActive;
    private NVector2 _rallyMarkerPosition;
    private StandardMaterial3D? _validPreview;
    private StandardMaterial3D? _invalidPreview;
    private StandardMaterial3D? _abilityValidPreview;
    private StandardMaterial3D? _abilityInvalidPreview;
    private StandardMaterial3D? _abilityRangeMaterial;
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
    private bool _sawMoveCommandConfirmation;
    private bool _sawAttackCommandConfirmation;
    private bool _sawTreeTargetConfirmation;
    private bool _sawTreeHitAnimation;
    private int _treeHarvestFeedbackCount;
    private int _nextTransientAudioEmitterId = 1_500_000_000;
    private ulong _animationAudioSequence;
    private ulong _treeHarvestFeedbackSequence;
    private ulong _abilityEventCursor;
    private double _profileUnitAnimationMilliseconds;
    private int _profileUnitActorsVisited;
    private int _profileUnitActorsAlive;
    private int _profileUnitActorsInFrustum;
    private int _profileUnitActorsCreated;
    private long _profileUnitResolveAllocatedBytes;
    private long _profileUnitFrustumAllocatedBytes;
    private long _profileUnitLodAllocatedBytes;
    private long _profileUnitAnimationAllocatedBytes;
    private int _profileBuildingActorsVisited;
    private int _profileBuildingActorsInFrustum;
    private long _profileBuildingOverviewAllocatedBytes;
    private long _profileBuildingAnimationAllocatedBytes;

    public event Action<War3AnimationAudioEvent>? AnimationAudioEvent;
    public event Action<War3TreeHarvestFeedbackEvent>? TreeHarvestFeedback;

    public int PresentedUnitCount => _units.Values.Count(value => !value.Dying);
    public bool UnitSelectionAgentFitReady => _simulation is not null &&
        _units.Where(pair => !pair.Value.Dying).All(pair =>
            pair.Key >= 0 && pair.Key < _simulation.Units.Count &&
            MathF.Abs(pair.Value.Selection.Scale.X -
                UnitSelectionWorldRadius(
                    _simulation.Units.NavigationRadii[pair.Key])) < 0.001f);
    public int PresentedBuildingCount => _buildings.Values.Count(value => !value.Dying);
    public int PresentedResourceCount => _resources.Count;
    public int ActiveEffectCount =>
        _projectiles.Count + _buildingProjectiles.Count +
        _abilityProjectiles.Count + _transients.Count + _abilityBuffs.Count;
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
    public bool SawMoveCommandConfirmation => _sawMoveCommandConfirmation;
    public bool SawAttackCommandConfirmation => _sawAttackCommandConfirmation;
    public bool SawTreeTargetConfirmation => _sawTreeTargetConfirmation;
    public bool SawTreeHitAnimation => _sawTreeHitAnimation;
    public int TreeHarvestFeedbackCount => _treeHarvestFeedbackCount;
    public int ActiveCommandConfirmationCount =>
        _transients.Count(value => value.CommandConfirmation);
    public bool PointerPreviewUsesWar3Model => _pointerGhost?.Loaded == true;
    public bool AbilityPointerPreviewVisible =>
        _abilityTargetPreview?.Visible == true;
    public bool AbilityRangePreviewVisible =>
        _abilityRangePreview?.Visible == true;
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
        EnsureAbilityPointerPreview();
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

    public void SetAbilityPointerPreview(
        NVector2 target,
        NVector2 caster,
        float castRange,
        float targetRadius,
        bool valid)
    {
        EnsureAbilityPointerPreview();
        var worldRadius = SimPlane3DTransform.ToWorldLength(
            MathF.Max(12f, targetRadius));
        _abilityTargetPreview!.Position = ToWorldAtGround(target, 0.055f);
        _abilityTargetPreview.Scale = new Vector3(
            worldRadius, 1f, worldRadius);
        _abilityTargetPreview.MaterialOverride = valid
            ? _abilityValidPreview
            : _abilityInvalidPreview;
        _abilityTargetPreview.Visible = true;

        if (castRange > 0f)
        {
            var worldRange = SimPlane3DTransform.ToWorldLength(castRange);
            _abilityRangePreview!.Position = ToWorldAtGround(caster, 0.045f);
            _abilityRangePreview.Scale = new Vector3(
                worldRange, 1f, worldRange);
            _abilityRangePreview.Visible = true;
        }
        else if (_abilityRangePreview is not null)
        {
            _abilityRangePreview.Visible = false;
        }
    }

    public void HideAbilityPointerPreview()
    {
        if (_abilityTargetPreview is not null)
            _abilityTargetPreview.Visible = false;
        if (_abilityRangePreview is not null)
            _abilityRangePreview.Visible = false;
    }

    public void Sync(float interpolation)
    {
        if (_simulation is null || _production is null || _camera is null) return;
        interpolation = Math.Clamp(interpolation, 0f, 1f);
        _profileUnitAnimationMilliseconds = 0d;
        _profileUnitActorsVisited = 0;
        _profileUnitActorsAlive = 0;
        _profileUnitActorsInFrustum = 0;
        _profileUnitActorsCreated = 0;
        _profileUnitResolveAllocatedBytes = 0;
        _profileUnitFrustumAllocatedBytes = 0;
        _profileUnitLodAllocatedBytes = 0;
        _profileUnitAnimationAllocatedBytes = 0;
        _profileBuildingActorsVisited = 0;
        _profileBuildingActorsInFrustum = 0;
        _profileBuildingOverviewAllocatedBytes = 0;
        _profileBuildingAnimationAllocatedBytes = 0;
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
        SyncBuildingProjectiles(_simulation, _camera);
        SyncAbilityProjectiles(_simulation, _camera);
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
                _profileUnitAnimationMilliseconds,
                _profileUnitActorsVisited,
                _profileUnitActorsAlive,
                _profileUnitActorsInFrustum,
                _profileUnitActorsCreated,
                _profileUnitResolveAllocatedBytes,
                _profileUnitFrustumAllocatedBytes,
                _profileUnitLodAllocatedBytes,
                _profileUnitAnimationAllocatedBytes,
                ElapsedMilliseconds(unitsEnd, buildingsEnd),
                buildingsAllocationEnd - unitsAllocationEnd,
                _profileBuildingActorsVisited,
                _profileBuildingActorsInFrustum,
                _profileBuildingOverviewAllocatedBytes,
                _profileBuildingAnimationAllocatedBytes,
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
        var creationProgressStart = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var denseUnitLod = simulation.Units.Count >= DenseUnitLodThreshold;
        var fullDetailUnits = 0;
        for (var unit = 0; unit < simulation.Units.Count; unit++)
        {
            if (ProfilingEnabled) _profileUnitActorsVisited++;
            if (!simulation.Units.Alive[unit])
            {
                if (_units.TryGetValue(unit, out var dead) && !dead.Dying)
                {
                    dead.LodBatch?.SetInstanceVisible(dead.LodIndex, false);
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
            var resolveAllocationStart = ProfilingEnabled
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            var hasVisual = _units.TryGetValue(unit, out var visual);
            var definitionChanged = !hasVisual || visual is null ||
                !UnitDefinitionMatches(simulation, unit, visual);
            var resolvedDefinition = definitionChanged
                ? War3HumanContent.ResolveUnit(
                    simulation, production, unit)
                : visual!.Definition;
            if (ProfilingEnabled)
                _profileUnitResolveAllocatedBytes +=
                    GC.GetAllocatedBytesForCurrentThread() -
                    resolveAllocationStart;
            if (!hasVisual || visual is null)
            {
                visual = CreateUnit(
                    unit, resolvedDefinition,
                    simulation.Combat.Teams[unit], camera);
                _units.Add(unit, visual);
                if (ProfilingEnabled)
                {
                    _profileUnitActorsCreated++;
                    PrintUnitCreationProgress(
                        simulation.Units.Count, creationProgressStart);
                }
            }
            else if (definitionChanged)
            {
                visual.LodBatch?.SetInstanceVisible(visual.LodIndex, false);
                visual.Root.QueueFree();
                visual = CreateUnit(
                    unit, resolvedDefinition,
                    simulation.Combat.Teams[unit], camera);
                _units[unit] = visual;
                if (ProfilingEnabled)
                {
                    _profileUnitActorsCreated++;
                    PrintUnitCreationProgress(
                        simulation.Units.Count, creationProgressStart);
                }
            }
            else if (visual.Dying)
            {
                visual.Dying = false;
                visual.RemoveAt = 0;
                visual.Actor.Revive();
            }
            var definition = visual.Definition;
            if (ProfilingEnabled) _profileUnitActorsAlive++;
            var position = NVector2.Lerp(
                simulation.Units.PreviousPositions[unit],
                simulation.Units.Positions[unit], interpolation);
            visual.LastPosition = position;
            var world = ToWorldAtGround(position, definition.FlyingHeight);
            var frustumAllocationStart = ProfilingEnabled
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            var inFrustum = camera.IsPositionInFrustum(world);
            if (ProfilingEnabled)
                _profileUnitFrustumAllocatedBytes +=
                    GC.GetAllocatedBytesForCurrentThread() -
                    frustumAllocationStart;
            if (ProfilingEnabled && inFrustum)
                _profileUnitActorsInFrustum++;
            var facing = UnitFacing.Interpolate(
                simulation.Units.PreviousFacings[unit],
                simulation.Units.Facings[unit], interpolation);
            var facingDirection = UnitFacing.Direction(facing);
            var angle = MathF.Atan2(facingDirection.X, facingDirection.Y);
            var lodAllocationStart = ProfilingEnabled
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            if (denseUnitLod && visual.LodBatch is null)
                AssignUnitLod(visual, simulation.Combat.Teams[unit]);
            var selected = _selectedUnits.Contains(unit);
            var fullDetail = !denseUnitLod ||
                             inFrustum &&
                             (selected ||
                              fullDetailUnits < MaximumFullDetailUnits);
            if (fullDetail) fullDetailUnits++;
            var hiddenInsideGoldMine = IsGatheringGold(simulation, unit);
            var lodTransform = new Transform3D(
                new Basis(Vector3.Up, angle), world);
            visual.LodBatch?.SetInstanceTransform(
                visual.LodIndex,
                lodTransform,
                denseUnitLod && !fullDetail && !hiddenInsideGoldMine);
            if (visual.FullDetail != fullDetail)
            {
                visual.FullDetail = fullDetail;
                visual.Actor.ProcessMode = fullDetail
                    ? ProcessModeEnum.Inherit
                    : ProcessModeEnum.Disabled;
            }
            visual.Actor.Visible = fullDetail && !hiddenInsideGoldMine;
            if (fullDetail)
            {
                visual.Actor.Position = world;
                visual.Actor.Rotation = new Vector3(0f, angle, 0f);
            }
            if (simulation.Combat.TargetKinds[unit] != CombatTargetKind.None)
            {
                _sawAttackTargetFacing = true;
                if (fullDetail)
                    _attackTargetFacingMismatch |= MathF.Abs(
                        Mathf.AngleDifference(
                            visual.Actor.Rotation.Y, angle)) > 0.001f;
            }
            _sawGoldMinerHidden |= hiddenInsideGoldMine;
            if (selected)
                visual.Selection.Position = new Vector3(
                    world.X, world.Y + SelectionHeight, world.Z);
            visual.Selection.Visible = selected && !hiddenInsideGoldMine;
            if (ProfilingEnabled)
                _profileUnitLodAllocatedBytes +=
                    GC.GetAllocatedBytesForCurrentThread() -
                    lodAllocationStart;
            if (fullDetail)
            {
                var animationAllocationStart = ProfilingEnabled
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                var animationStart = ProfilingEnabled
                    ? System.Diagnostics.Stopwatch.GetTimestamp()
                    : 0L;
                UpdateUnitAnimation(simulation, unit, visual);
                if (ProfilingEnabled)
                {
                    _profileUnitAnimationMilliseconds += ElapsedMilliseconds(
                        animationStart,
                        System.Diagnostics.Stopwatch.GetTimestamp());
                    _profileUnitAnimationAllocatedBytes +=
                        GC.GetAllocatedBytesForCurrentThread() -
                        animationAllocationStart;
                }
                _sawBlendedTransition |=
                    visual.Actor.LastTransitionBlendSeconds > 0d;
            }
        }

        foreach (var id in _units.Where(pair => pair.Value.Dying &&
                                                Time.GetTicksMsec() >= pair.Value.RemoveAt)
                     .Select(pair => pair.Key).ToArray())
        {
            _units[id].LodBatch?.SetInstanceVisible(
                _units[id].LodIndex, false);
            _units[id].Root.QueueFree();
            _units.Remove(id);
        }
        foreach (var batch in _unitLodBatches.Values)
            batch.FlushDynamicBuffer();
    }

    private void AssignUnitLod(UnitVisual visual, int team)
    {
        var key = (visual.Definition.ModelSource, team);
        if (!_unitLodBatches.TryGetValue(key, out var batch))
        {
            batch = new War3StaticModelBatch
            {
                Name = $"UnitLod{_unitLodBatches.Count}"
            };
            AddChild(batch);
            batch.InitializeDynamic(key.ModelSource, 64, team);
            _unitLodBatches.Add(key, batch);
        }
        visual.LodBatch = batch;
        visual.LodIndex = batch.AddInstance();
    }

    private static bool UnitDefinitionMatches(
        RtsSimulation simulation,
        int unit,
        UnitVisual visual)
    {
        if (War3HumanContent.TryResolveSummonedUnit(
                simulation, unit, out var summonedDefinition))
            return visual.Definition.ObjectId.Equals(
                summonedDefinition.ObjectId, StringComparison.Ordinal);
        return visual.Definition.TypeId >= 0 &&
               visual.BoundUnitTypeId ==
               simulation.Abilities.UnitTypeId(unit);
    }

    private void PrintUnitCreationProgress(int totalUnits, long start)
    {
        if (_profileUnitActorsCreated % 100 != 0 &&
            _profileUnitActorsCreated != totalUnits)
            return;
        GD.Print(
            $"WAR3_PRESENTER_UNIT_CREATE_PROGRESS " +
            $"created={_profileUnitActorsCreated} total={totalUnits} " +
            $"elapsed_ms={ElapsedMilliseconds(start, System.Diagnostics.Stopwatch.GetTimestamp()):0.###}");
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
        var abilityState = simulation.Abilities.ObservePresentation(unit);
        if (abilityState.CastPhase != AbilityCastPhase.None)
        {
            var candidates = (uint)abilityState.ActiveAbilityId <
                             (uint)War3HumanContent.Abilities.Count
                ? War3HumanContent.Ability(
                    abilityState.ActiveAbilityId).AnimationNames
                : ["Spell Channel", "Spell", "Spell Slam", "Attack"];
            visual.Actor.PlayRepeatedPreferred(candidates);
            return;
        }
        if (Time.GetTicksMsec() < visual.AbilityAnimationUntil)
            return;
        if (simulation.Economy.IsWorker(unit))
        {
            if (simulation.Construction.IsAssignedBuilder(unit))
            {
                ResetTreeHarvestFeedback(visual);
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
                    UpdateTreeHarvestFeedback(
                        simulation, unit, visual, worker, resource);
                }
                else
                {
                    ResetTreeHarvestFeedback(visual);
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
            ResetTreeHarvestFeedback(visual);
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
                War3ModelActor? casterActor = null;
                if (_units.TryGetValue(value.CasterUnit, out var caster))
                {
                    casterActor = caster.Actor;
                    caster.AbilityAnimationUntil = Time.GetTicksMsec() + 700;
                    caster.Actor.ReplayPreferred(definition.AnimationNames);
                }
                else if (_buildings.TryGetValue(
                             value.CasterBuilding, out var buildingCaster))
                {
                    casterActor = buildingCaster.Actor;
                    buildingCaster.Actor.ReplayPreferred(
                        definition.AnimationNames);
                }
                SpawnAbilityModels(
                    definition.CasterModels,
                    definition.CasterAttachments,
                    definition.CasterAttachmentCount,
                    AbilityCasterPosition(simulation, value),
                    casterActor, camera, sourcePlayerId);
                continue;
            }
            if (value.Kind != AbilityEventKind.Impact) continue;
            var position = AbilityTargetPosition(simulation, value);
            var targetActor = AbilityTargetActor(value);
            SpawnAbilityModels(
                definition.TargetModels,
                definition.TargetAttachments,
                definition.TargetAttachmentCount,
                position, targetActor, camera, sourcePlayerId);
            SpawnAbilityModels(
                definition.EffectModels, [], 0,
                position, null, camera, sourcePlayerId);
        }
    }

    private void SpawnAbilityModels(
        IEnumerable<string> models,
        IReadOnlyList<string> attachments,
        int declaredAttachmentCount,
        NVector2 position,
        War3ModelActor? hostActor,
        Camera3D camera,
        int sourcePlayerId)
    {
        foreach (var instance in AbilityVisualInstances(
                     models, attachments, declaredAttachmentCount))
            SpawnTransient(
                instance.Model, position, camera, 1_800, sourcePlayerId,
                AbilityAttachmentOffset(instance.Attachment, hostActor));
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
                var sourcePlayerId = (uint)buff.SourceUnit <
                                     (uint)simulation.Units.Count
                    ? simulation.Combat.Teams[buff.SourceUnit]
                    : -1;
                var targetPlayerId = simulation.Combat.Teams[buff.TargetUnit];
                var parts = new List<AbilityBuffPart>();
                foreach (var instance in AbilityVisualInstances(
                             definition.BuffModels,
                             definition.BuffAttachments,
                             definition.BuffAttachmentCount))
                {
                    var actor = new War3ModelActor
                    {
                        Name = $"AbilityBuff{buff.InstanceId}_{parts.Count}"
                    };
                    AddChild(actor);
                    var emitterId = _nextTransientAudioEmitterId++;
                    actor.SoundTimelineEvent += value => PublishAnimationAudio(
                        value,
                        simulation.Units.Positions[buff.TargetUnit],
                        emitterId,
                        sourcePlayerId);
                    actor.Load(instance.Model, camera, targetPlayerId);
                    actor.PlayPreferred(true, "Stand", "Birth");
                    parts.Add(new AbilityBuffPart(actor, instance.Attachment));
                }
                if (parts.Count == 0) continue;
                visual = new AbilityBuffVisual(parts.ToArray(), buff.TargetUnit);
                _abilityBuffs.Add(buff.InstanceId, visual);
            }
            var hostActor = _units.TryGetValue(
                visual.TargetUnit, out var targetVisual)
                ? targetVisual.Actor
                : null;
            foreach (var part in visual.Parts)
                part.Actor.Position = ToWorldAtGround(
                    simulation.Units.Positions[visual.TargetUnit]) +
                    AbilityAttachmentOffset(part.Attachment, hostActor);
        }

        foreach (var id in _abilityBuffs.Keys
                     .Where(value => !_seenAbilityBuffs.Contains(value))
                     .ToArray())
        {
            foreach (var part in _abilityBuffs[id].Parts)
                part.Actor.QueueFree();
            _abilityBuffs.Remove(id);
        }
    }

    private War3ModelActor? AbilityTargetActor(in AbilityEvent value)
    {
        if (value.TargetKind is AbilityTargetKind.Unit or AbilityTargetKind.Self &&
            _units.TryGetValue(value.TargetId, out var unit))
            return unit.Actor;
        if (value.TargetKind == AbilityTargetKind.Building &&
            _buildings.TryGetValue(value.TargetId, out var building))
            return building.Actor;
        return null;
    }

    private static IEnumerable<AbilityVisualInstance> AbilityVisualInstances(
        IEnumerable<string> models,
        IReadOnlyList<string> attachments,
        int declaredAttachmentCount)
    {
        var availableModels = models
            .Where(War3RuntimeAssets.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (availableModels.Length == 0) yield break;
        var instanceCount = Math.Min(6, Math.Max(
            availableModels.Length,
            Math.Max(declaredAttachmentCount, attachments.Count)));
        for (var index = 0; index < instanceCount; index++)
            yield return new AbilityVisualInstance(
                availableModels[index % availableModels.Length],
                attachments.Count == 0
                    ? string.Empty
                    : attachments[index % attachments.Count]);
    }

    private static Vector3 AbilityAttachmentOffset(
        string attachment,
        War3ModelActor? hostActor)
    {
        var height = hostActor?.ApproximateWorldHeight() ?? 1.5f;
        var tokens = attachment.Split(',',
            StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return new Vector3(0f, 0.18f, 0f);
        if (tokens.Any(value => value.Equals(
                "overhead", StringComparison.OrdinalIgnoreCase)))
            return new Vector3(0f, height + MathF.Max(0.12f, height * 0.08f), 0f);
        if (tokens.Any(value => value.Contains(
                "head", StringComparison.OrdinalIgnoreCase)))
            return new Vector3(0f, height * 0.84f, 0f);
        if (tokens.Any(value => value.Contains(
                "chest", StringComparison.OrdinalIgnoreCase)))
            return new Vector3(0f, height * 0.58f, 0f);
        if (tokens.Any(value => value.Contains(
                "hand", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("weapon", StringComparison.OrdinalIgnoreCase)))
        {
            var side = tokens.Any(value => value.Contains(
                "left", StringComparison.OrdinalIgnoreCase)) ? -1f : 1f;
            return new Vector3(side * height * 0.18f, height * 0.52f, 0f);
        }
        if (tokens.Any(value => value.Contains(
                "foot", StringComparison.OrdinalIgnoreCase)))
            return new Vector3(0f, MathF.Max(0.08f, height * 0.06f), 0f);
        if (tokens.Any(value => value.Equals(
                "sprite", StringComparison.OrdinalIgnoreCase)))
        {
            var ordinal = Array.FindIndex(SpriteAttachmentOrdinals,
                value => tokens.Any(token => token.Equals(
                    value, StringComparison.OrdinalIgnoreCase)));
            ordinal = ordinal < 0 ? 0 : ordinal;
            var angle = ordinal * MathF.Tau / SpriteAttachmentOrdinals.Length;
            var radius = MathF.Max(0.16f, height * 0.24f);
            return new Vector3(
                MathF.Cos(angle) * radius,
                height * 0.5f,
                MathF.Sin(angle) * radius);
        }
        return new Vector3(0f, 0.18f, 0f);
    }

    private static readonly string[] SpriteAttachmentOrdinals =
        ["first", "second", "third", "fourth", "fifth", "sixth"];

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
        var overviewAllocationStart = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        var buildings = simulation.CreateGameplayBuildingOverview();
        if (ProfilingEnabled)
            _profileBuildingOverviewAllocatedBytes +=
                GC.GetAllocatedBytesForCurrentThread() -
                overviewAllocationStart;
        foreach (var building in buildings)
        {
            if (building.IsTerminal) continue;
            if (ProfilingEnabled) _profileBuildingActorsVisited++;
            var id = building.Id.Value;
            _seenBuildings.Add(id);
            var definition = War3HumanContent.Buildings[building.Type.Id];
            if (!_buildings.TryGetValue(id, out var visual))
            {
                visual = CreateBuilding(building, definition, camera);
                _buildings.Add(id, visual);
            }
            else if (visual.TypeId != building.Type.Id)
            {
                if (visual.ModelLoaded &&
                    !visual.Definition.ModelSource.Equals(
                        definition.ModelSource,
                        StringComparison.OrdinalIgnoreCase))
                {
                    visual.Actor.Load(
                        definition.ModelSource, camera, building.PlayerId);
                    visual.Actor.SetShadowCastingEnabled(false);
                }
                visual.SetDefinition(definition);
                visual.TypeId = building.Type.Id;
            }
            if (!visual.LayoutInitialized)
            {
                var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
                visual.LastPosition = center;
                var world = ToWorldAtGround(center);
                visual.Actor.Position = world;
                var footprintRadius = MathF.Max(
                    SimPlane3DTransform.ToWorldLength(building.Type.Size.X),
                    SimPlane3DTransform.ToWorldLength(building.Type.Size.Y)) * 0.62f;
                visual.Selection.Position = new Vector3(
                    world.X, world.Y + SelectionHeight, world.Z);
                var selectionRadius = visual.Definition.SelectionCircleScale > 0f
                    ? visual.Definition.SelectionCircleScale *
                      WarcraftSelectionCircleWorldRadius
                    : MathF.Max(0.85f, footprintRadius);
                visual.Selection.Scale = Vector3.One * selectionRadius;
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
            var inFrustum = BuildingIntersectsFrustum(camera, visual);
            visual.Actor.ProcessMode = inFrustum
                ? ProcessModeEnum.Inherit
                : ProcessModeEnum.Disabled;
            visual.Selection.Visible = foundationStarted &&
                                       inFrustum &&
                                       _selectedBuildings.Contains(id);
            if (!inFrustum) continue;
            if (ProfilingEnabled) _profileBuildingActorsInFrustum++;
            var animationAllocationStart = ProfilingEnabled
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            if (!visual.ModelLoaded)
            {
                visual.Actor.Load(
                    visual.Definition.ModelSource,
                    camera,
                    building.PlayerId);
                visual.Actor.SetShadowCastingEnabled(false);
                visual.ModelLoaded = true;
                visual.GhostInitialized = false;
            }
            if (!visual.GhostInitialized || visual.IsGhost != ghost)
            {
                visual.Actor.SetGhostAppearance(ghost);
                visual.IsGhost = ghost;
                visual.GhostInitialized = true;
            }
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
            else if (simulation.BuildingUpgrades.TryObserve(
                         building.Id, out var upgrade))
            {
                var targetDefinition = War3HumanContent.Buildings[
                    upgrade.Profile.TargetType.Id];
                visual.Actor.SetSequenceProgress(
                    upgrade.Progress,
                    War3AnimationPropertyResolver.UpgradeBirth(
                        targetDefinition.AnimationProperties));
            }
            else if (simulation.Production.HasOrders(building.Id))
            {
                visual.Actor.PlayPreferred(
                    true,
                    visual.WorkingStandAnimations);
            }
            else if (simulation.BuildingCombat.Observe(building.Id).Phase is
                     BuildingCombatPhase.Windup or BuildingCombatPhase.Cooldown)
            {
                var combatState = simulation.BuildingCombat.Observe(building.Id);
                if (combatState.Phase == BuildingCombatPhase.Windup &&
                    visual.LastCombatPhase != BuildingCombatPhase.Windup)
                {
                    visual.Actor.ReplayPreferred(
                        visual.AttackAnimations);
                }
                else if (!visual.Actor.IsAnimationPlaying ||
                         !visual.Actor.CurrentSequence.StartsWith(
                             "Attack", StringComparison.OrdinalIgnoreCase))
                {
                    visual.Actor.PlayPreferred(
                        true,
                        visual.StandAnimations);
                }
                visual.LastCombatPhase = combatState.Phase;
            }
            else
            {
                visual.Actor.PlayPreferred(
                    true,
                    visual.StandAnimations);
                visual.LastCombatPhase = BuildingCombatPhase.Idle;
            }
            if (building.State == BuildingLifecycleState.Completed)
            {
                var sequence = visual.Actor.CurrentSequence;
                _completedBuildingUsedLifecycleAnimation |=
                    sequence.StartsWith("Birth", StringComparison.OrdinalIgnoreCase) ||
                    sequence.StartsWith("Death", StringComparison.OrdinalIgnoreCase) ||
                    sequence.StartsWith("Decay", StringComparison.OrdinalIgnoreCase);
                if (building.Type.Id == War3HumanContent.TownHall &&
                    !simulation.Production.HasOrders(building.Id) &&
                    sequence.Equals("Stand", StringComparison.OrdinalIgnoreCase))
                    _idleTownHallEffectLeak |= visual.Actor.LiveEffectCount > 0;
            }
            if (ProfilingEnabled)
                _profileBuildingAnimationAllocatedBytes +=
                    GC.GetAllocatedBytesForCurrentThread() -
                    animationAllocationStart;
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
        var now = Time.GetTicksMsec();
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
                    }
                    visual.Actor?.PlayDeath();
                }
                continue;
            }
            var isTree = snapshot.Kind == EconomyResourceKind.VespeneGas;
            if (isTree && snapshot.ActiveHarvesters > 0)
                PromoteResourceActor(id, visual, snapshot.Position, camera);
            var working = snapshot.ActiveHarvesters > 0 &&
                          snapshot.Kind == EconomyResourceKind.Minerals;
            if (!visual.AnimationInitialized || visual.Working != working)
            {
                visual.Actor?.PlayPreferred(
                    true, working ? "Stand Work" : "Stand");
                visual.Working = working;
                visual.AnimationInitialized = true;
            }
            if (isTree && snapshot.ActiveHarvesters == 0 &&
                visual.Actor is not null && visual.StaticBatch is not null &&
                now >= visual.LastHitAt + 500)
                DemoteResourceActor(visual);
        }
    }

    private void UpdateTreeHarvestFeedback(
        RtsSimulation simulation,
        int unit,
        UnitVisual unitVisual,
        in WorkerEconomySnapshot worker,
        in EconomyResourceNodeSnapshot resource)
    {
        var objectId = unitVisual.Definition.ObjectId;
        if (!_treeHarvestProfiles.TryGetValue(objectId, out var profile))
        {
            profile = War3TreeHarvestFeedbackCatalog.Resolve(
                War3HumanContent.DataCatalog, objectId);
            _treeHarvestProfiles.Add(objectId, profile);
        }

        var node = worker.TargetNode.Value;
        if (unitVisual.TreeHarvestResourceNode != node)
        {
            unitVisual.TreeHarvestResourceNode = node;
            unitVisual.LastTreeHarvestStrike = -1;
        }
        var strike = War3TreeHarvestFeedbackCatalog.StrikeIndex(
            profile, resource.HarvestSeconds, worker.WorkRemaining);
        if (strike <= unitVisual.LastTreeHarvestStrike) return;
        // A render frame can cover more than one simulation step. Emit every
        // authored strike crossed so audio/animation do not depend on FPS.
        for (var index = unitVisual.LastTreeHarvestStrike + 1;
             index <= strike;
             index++)
        {
            TriggerTreeHarvestFeedback(
                unit,
                simulation.Combat.Teams[unit],
                node,
                objectId,
                profile,
                resource.Position);
        }
        unitVisual.LastTreeHarvestStrike = strike;
    }

    private static void ResetTreeHarvestFeedback(UnitVisual visual)
    {
        visual.TreeHarvestResourceNode = -1;
        visual.LastTreeHarvestStrike = -1;
    }

    private void TriggerTreeHarvestFeedback(
        int workerUnit,
        int sourcePlayerId,
        int resourceNode,
        string workerObjectId,
        in War3TreeHarvestFeedbackProfile profile,
        NVector2 position)
    {
        if (_camera is not null &&
            _resources.TryGetValue(resourceNode, out var visual))
        {
            var actor = PromoteResourceActor(
                resourceNode, visual, position, _camera);
            visual.LastHitAt = Time.GetTicksMsec();
            if (actor.ReplayPreferred("Stand Hit", "Hit", "Stand"))
                _sawTreeHitAnimation |= actor.CurrentSequence.Contains(
                    "Hit", StringComparison.OrdinalIgnoreCase);
        }

        _treeHarvestFeedbackCount++;
        TreeHarvestFeedback?.Invoke(new War3TreeHarvestFeedbackEvent(
            workerObjectId,
            workerUnit,
            resourceNode,
            profile.WeaponSlot,
            profile.SoundFamily,
            position,
            sourcePlayerId,
            ++_treeHarvestFeedbackSequence));
    }

    private void SyncProjectiles(
        RtsSimulation simulation,
        ProductionCatalogSnapshot production,
        Camera3D camera)
    {
        _seenProjectiles.Clear();
        var visualCreations = 0;
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
                // Model loading is intentionally bounded per rendered frame.
                // A large battle can launch hundreds of authoritative missiles
                // between two presenter syncs; creating all their Godot node
                // trees at once caused 700+ ms cosmetic spikes. Missiles that
                // exceed this visual budget remain fully simulated and can be
                // picked up by a later frame if they are still active.
                if (visualCreations >= MaximumProjectileVisualCreationsPerSync)
                    continue;
                visual = CreateProjectile(
                    projectile.Id,
                    definition,
                    simulation.Combat.Teams[projectile.AttackerUnit],
                    camera);
                _projectiles.Add(projectile.Id, visual);
                visualCreations++;
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
        var impactVisuals = 0;
        foreach (var id in _projectiles.Keys.Where(id => !_seenProjectiles.Contains(id)).ToArray())
        {
            var visual = _projectiles[id];
            visual.Root.QueueFree();
            _projectiles.Remove(id);
            if (visual.Definition.ImpactSource.Length > 0 &&
                War3RuntimeAssets.Contains(visual.Definition.ImpactSource) &&
                impactVisuals < MaximumProjectileImpactVisualsPerSync &&
                NonCommandTransientCount() < MaximumNonCommandTransientVisuals)
            {
                SpawnTransient(
                    visual.Definition.ImpactSource,
                    visual.LastPosition,
                    camera,
                    1_300,
                    visual.SourcePlayerId);
                impactVisuals++;
            }
        }
    }

    private int NonCommandTransientCount()
    {
        var count = 0;
        for (var index = 0; index < _transients.Count; index++)
            if (!_transients[index].CommandConfirmation) count++;
        return count;
    }

    private void SyncBuildingProjectiles(
        RtsSimulation simulation,
        Camera3D camera)
    {
        _seenBuildingProjectiles.Clear();
        var creations = 0;
        foreach (var projectile in simulation.BuildingCombat.ObserveProjectiles())
        {
            _seenBuildingProjectiles.Add(projectile.Id);
            if (!simulation.Construction.IsAlive(projectile.AttackerBuilding))
                continue;
            var attacker = simulation.Construction.Observe(
                projectile.AttackerBuilding);
            if ((uint)attacker.Type.Id >=
                (uint)War3HumanContent.Buildings.Count)
                continue;
            var definition = War3HumanContent.Buildings[attacker.Type.Id];
            if (!_buildingProjectiles.TryGetValue(
                    projectile.Id, out var visual))
            {
                if (creations >= MaximumProjectileVisualCreationsPerSync)
                    continue;
                visual = CreateBuildingProjectile(
                    projectile.Id, definition, attacker.PlayerId, camera);
                _buildingProjectiles.Add(projectile.Id, visual);
                creations++;
            }
            var displacement = projectile.Position - visual.LastPosition;
            if (visual.HasPosition && displacement.LengthSquared() > 0.0001f)
                visual.Root.Rotation = new Vector3(
                    0f, MathF.Atan2(displacement.X, displacement.Y), 0f);
            visual.LastPosition = projectile.Position;
            visual.HasPosition = true;
            visual.Root.Position = ToWorldAtGround(projectile.Position, 1.05f);
        }

        var impacts = 0;
        foreach (var id in _buildingProjectiles.Keys
                     .Where(id => !_seenBuildingProjectiles.Contains(id))
                     .ToArray())
        {
            var visual = _buildingProjectiles[id];
            visual.Root.QueueFree();
            _buildingProjectiles.Remove(id);
            if (visual.Definition.ImpactSource.Length == 0 ||
                !War3RuntimeAssets.Contains(visual.Definition.ImpactSource) ||
                impacts >= MaximumProjectileImpactVisualsPerSync ||
                NonCommandTransientCount() >= MaximumNonCommandTransientVisuals)
                continue;
            SpawnTransient(
                visual.Definition.ImpactSource,
                visual.LastPosition,
                camera,
                1_300,
                visual.SourcePlayerId);
            impacts++;
        }
    }

    private void SyncAbilityProjectiles(
        RtsSimulation simulation,
        Camera3D camera)
    {
        _seenAbilityProjectiles.Clear();
        var creations = 0;
        foreach (var projectile in simulation.Abilities.ObserveProjectiles())
        {
            _seenAbilityProjectiles.Add(projectile.Id);
            if ((uint)projectile.AbilityId >=
                (uint)simulation.Abilities.Catalog.Count)
                continue;
            var definition = War3HumanContent.Ability(projectile.AbilityId);
            if (!_abilityProjectiles.TryGetValue(
                    projectile.Id, out var visual))
            {
                if (creations >= MaximumProjectileVisualCreationsPerSync)
                    continue;
                var sourcePlayerId = (uint)projectile.CasterUnit <
                                     (uint)simulation.Units.Count
                    ? simulation.Combat.Teams[projectile.CasterUnit]
                    : -1;
                visual = CreateAbilityProjectile(
                    projectile.Id, definition, sourcePlayerId, camera);
                _abilityProjectiles.Add(projectile.Id, visual);
                creations++;
            }
            var displacement = projectile.Position - visual.LastPosition;
            if (visual.HasPosition && displacement.LengthSquared() > 0.0001f)
                visual.Root.Rotation = new Vector3(
                    0f, MathF.Atan2(displacement.X, displacement.Y), 0f);
            visual.LastPosition = projectile.Position;
            visual.HasPosition = true;
            var profile = simulation.Abilities.Catalog
                .Ability(projectile.AbilityId).Projectile;
            var total = NVector2.Distance(
                projectile.Origin, projectile.Destination);
            var traveled = NVector2.Distance(
                projectile.Origin, projectile.Position);
            var progress = total <= 0.0001f
                ? 1f
                : Math.Clamp(traveled / total, 0f, 1f);
            var arcHeight = MathF.Sin(progress * MathF.PI) *
                            total * profile.Arc;
            visual.Root.Position = ToWorldAtGround(
                projectile.Position, MathF.Max(0.7f, 0.7f + arcHeight));
        }

        foreach (var id in _abilityProjectiles.Keys
                     .Where(id => !_seenAbilityProjectiles.Contains(id))
                     .ToArray())
        {
            _abilityProjectiles[id].Root.QueueFree();
            _abilityProjectiles.Remove(id);
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
        var selection = SelectionRing($"UnitSelection{id}", 0.075f,
            team == War3HumanScenario.PlayerId ? new Color("46d8ff") : new Color("ff5c58"));
        AddChild(root);
        root.AddChild(actor);
        root.AddChild(selection);
        actor.SoundTimelineEvent += value =>
            PublishUnitAnimationAudio(id, team, value);
        actor.Load(definition.ModelSource, camera, team);
        // Animated Warcraft units commonly expand to 6-8 source surfaces.
        // Keep their color pass intact, but replace all source shadow surfaces
        // with one shared low-poly proxy per unit.
        actor.SetShadowCastingEnabled(false);
        actor.AddChild(new MeshInstance3D
        {
            Name = "ShadowProxy",
            Mesh = _unitShadowProxyMesh ??= new CylinderMesh
            {
                Height = 1.1f,
                TopRadius = 0.24f,
                BottomRadius = 0.28f,
                RadialSegments = 8,
                Rings = 1
            },
            Position = new Vector3(0f, 0.55f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
        });
        selection.Scale = Vector3.One * UnitSelectionWorldRadius(
            _simulation!.Units.NavigationRadii[id]);
        selection.Visible = _selectedUnits.Contains(id);
        return new UnitVisual(
            root, actor, selection, definition,
            _simulation!.Abilities.UnitTypeId(id));
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
        var footprint = SimPlane3DTransform.ToWorldSize(
            building.Bounds.Max - building.Bounds.Min);
        var frustumRadius = MathF.Max(footprint.X, footprint.Y) * 0.7f;
        var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
        var world = ToWorldAtGround(center);
        var modelLoaded = BuildingIntersectsFrustum(
            camera, world, frustumRadius);
        if (modelLoaded)
        {
            actor.Load(definition.ModelSource, camera, building.PlayerId);
            // Imported buildings can contain dozens of geoset surfaces.
            // Replace their source shadow surfaces with one cheap proxy.
            actor.SetShadowCastingEnabled(false);
        }
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
            root, actor, selection, shadowProxy, proxyHeight, frustumRadius,
            definition, modelLoaded,
            building.Type.Id);
    }

    private static bool BuildingIntersectsFrustum(
        Camera3D camera,
        BuildingVisual visual)
        => BuildingIntersectsFrustum(
            camera, visual.Actor.Position, visual.FrustumRadius);

    private static bool BuildingIntersectsFrustum(
        Camera3D camera,
        Vector3 center,
        float radius)
    {
        if (camera.IsPositionInFrustum(center)) return true;
        return camera.IsPositionInFrustum(center + Vector3.Right * radius) ||
               camera.IsPositionInFrustum(center + Vector3.Left * radius) ||
               camera.IsPositionInFrustum(center + Vector3.Forward * radius) ||
               camera.IsPositionInFrustum(center + Vector3.Back * radius);
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

    private War3ModelActor PromoteResourceActor(
        int id,
        ResourceVisual visual,
        NVector2 position,
        Camera3D camera)
    {
        if (visual.Actor is not null) return visual.Actor;
        visual.StaticBatch?.SetInstanceVisible(visual.StaticBatchIndex, false);
        var actor = CreateResourceActor(id, visual.Kind, visual.Source, camera);
        actor.Position = ToWorldAtGround(position);
        actor.PlayPreferred(true, "Stand");
        visual.Actor = actor;
        visual.Positioned = true;
        visual.AnimationInitialized = true;
        return actor;
    }

    private static void DemoteResourceActor(ResourceVisual visual)
    {
        if (visual.Actor is null || visual.StaticBatch is null) return;
        visual.Actor.QueueFree();
        visual.Actor = null;
        visual.StaticBatch.SetInstanceVisible(visual.StaticBatchIndex, true);
        visual.AnimationInitialized = true;
        visual.Working = false;
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

    private BuildingProjectileVisual CreateBuildingProjectile(
        int id,
        War3BuildingDefinition definition,
        int sourcePlayerId,
        Camera3D camera)
    {
        var root = new Node3D { Name = $"BuildingProjectile{id}" };
        AddChild(root);
        if (definition.ProjectileSource.Length > 0 &&
            War3RuntimeAssets.Contains(definition.ProjectileSource))
        {
            var actor = new War3ModelActor { Name = "ProjectileActor" };
            root.AddChild(actor);
            actor.SoundTimelineEvent += value => PublishAnimationAudio(
                value,
                NVector2.Zero,
                ProjectileAudioEmitterBase + 100_000_000 + id,
                sourcePlayerId);
            actor.Load(definition.ProjectileSource, camera, 0);
            actor.PlayPreferred(true, "Stand", "Birth");
        }
        else
        {
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.075f, Height = 0.15f },
                MaterialOverride = Emissive(new Color("ffd46a")),
                Scale = Vector3.One * 1.2f
            });
        }
        return new BuildingProjectileVisual(
            root, definition, NVector2.Zero, sourcePlayerId);
    }

    private AbilityProjectileVisual CreateAbilityProjectile(
        int id,
        War3AbilityDefinition definition,
        int sourcePlayerId,
        Camera3D camera)
    {
        var root = new Node3D { Name = $"AbilityProjectile{id}" };
        AddChild(root);
        var source = definition.MissileModels
            .FirstOrDefault(War3RuntimeAssets.Contains);
        if (source is not null)
        {
            var actor = new War3ModelActor { Name = "ProjectileActor" };
            root.AddChild(actor);
            actor.SoundTimelineEvent += value => PublishAnimationAudio(
                value,
                new NVector2(root.Position.X, root.Position.Z),
                ProjectileAudioEmitterBase + 200_000_000 + id,
                sourcePlayerId);
            actor.Load(source, camera, 0);
            actor.PlayPreferred(true, "Stand", "Birth");
        }
        else
        {
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.075f, Height = 0.15f },
                MaterialOverride = Emissive(new Color("70dfff")),
                Scale = Vector3.One * 1.2f
            });
        }
        return new AbilityProjectileVisual(
            root, definition, NVector2.Zero, sourcePlayerId);
    }

    private void SpawnTransient(
        string source,
        NVector2 position,
        Camera3D camera,
        ulong lifetime,
        int sourcePlayerId = -1,
        Vector3 attachmentOffset = default)
    {
        var actor = new War3ModelActor { Name = "Impact" };
        var emitterId = _nextTransientAudioEmitterId++;
        AddChild(actor);
        actor.Position = ToWorldAtGround(position) +
                         (attachmentOffset == default
                             ? new Vector3(0f, 0.18f, 0f)
                             : attachmentOffset);
        actor.SoundTimelineEvent += value => PublishAnimationAudio(
            value, position, emitterId, sourcePlayerId);
        actor.Load(source, camera, 0);
        actor.PlayPreferred(false, "Birth", "Stand", "Death");
        _transients.Add(new TransientVisual(actor, Time.GetTicksMsec() + lifetime));
    }

    public bool ShowCommandConfirmation(
        NVector2 position,
        War3CommandFeedbackKind kind)
    {
        if (_camera is null || !War3RuntimeAssets.Contains(
                War3CommandFeedbackCatalog.ConfirmationSource))
            return false;

        RetireOldestCommandConfirmationAtCapacity();
        var actor = new War3ModelActor
        {
            Name = kind == War3CommandFeedbackKind.Attack
                ? "AttackCommandConfirmation"
                : "MoveCommandConfirmation"
        };
        AddChild(actor);
        actor.Position = ToWorldAtGround(position, 0.08f);
        actor.Load(
            War3CommandFeedbackCatalog.ConfirmationSource,
            _camera,
            War3HumanScenario.PlayerId,
            includeEffects: false);
        actor.SetSurfaceTint(War3CommandFeedbackCatalog.Tint(kind));
        actor.SetShadowCastingEnabled(false);
        actor.ReplayPreferred("Stand");
        _transients.Add(new TransientVisual(
            actor,
            Time.GetTicksMsec() +
            War3CommandFeedbackCatalog.VisibleLifetimeMilliseconds,
            CommandConfirmation: true));
        if (kind == War3CommandFeedbackKind.Attack)
            _sawAttackCommandConfirmation = true;
        else
            _sawMoveCommandConfirmation = true;
        return true;
    }

    public bool FlashResourceTarget(EconomyResourceNodeId resourceNode)
    {
        if (_simulation is null ||
            (uint)resourceNode.Value >= (uint)_simulation.Economy.ResourceNodeCount)
            return false;
        var snapshot = _simulation.Economy.ObserveResourceNode(resourceNode);
        if (snapshot.Remaining <= 0 ||
            snapshot.Kind != EconomyResourceKind.VespeneGas)
            return false;

        RetireOldestCommandConfirmationAtCapacity();
        var ring = SelectionRing(
            $"TreeTargetConfirmation{resourceNode.Value}",
            0.075f,
            War3CommandFeedbackCatalog.ResourceTargetTint);
        var radius = SimPlane3DTransform.ToWorldLength(
            MathF.Max(snapshot.InteractionHalfExtents.X,
                snapshot.InteractionHalfExtents.Y) + 10f);
        ring.Position = ToWorldAtGround(snapshot.Position, SelectionHeight);
        ring.Scale = Vector3.One * MathF.Max(0.55f, radius);
        ring.Visible = true;
        AddChild(ring);
        _transients.Add(new TransientVisual(
            ring,
            Time.GetTicksMsec() +
            War3CommandFeedbackCatalog.ResourceTargetLifetimeMilliseconds,
            CommandConfirmation: true));
        _sawTreeTargetConfirmation = true;
        return true;
    }

    private void RetireOldestCommandConfirmationAtCapacity()
    {
        var count = 0;
        foreach (var value in _transients)
            if (value.CommandConfirmation) count++;
        if (count < War3CommandFeedbackCatalog.MaximumSimultaneousConfirmations)
            return;
        for (var index = 0; index < _transients.Count; index++)
        {
            if (!_transients[index].CommandConfirmation) continue;
            _transients[index].Root.QueueFree();
            _transients.RemoveAt(index);
            return;
        }
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

    private void EnsureAbilityPointerPreview()
    {
        if (_abilityTargetPreview is not null) return;
        _abilityValidPreview = PreviewRingMaterial(new Color("45e7d6d8"));
        _abilityInvalidPreview = PreviewRingMaterial(new Color("ff4f63dc"));
        _abilityRangeMaterial = PreviewRingMaterial(new Color("72a9ff78"));
        _abilityTargetPreview = AbilityRing(
            "AbilityTargetPreview", 0.12f, _abilityValidPreview);
        _abilityRangePreview = AbilityRing(
            "AbilityRangePreview", 0.035f, _abilityRangeMaterial);
        AddChild(_abilityTargetPreview);
        AddChild(_abilityRangePreview);
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

    private static float UnitSelectionWorldRadius(float navigationRadius) =>
        SimPlane3DTransform.ToWorldLength(navigationRadius);

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

    private static StandardMaterial3D PreviewRingMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = false,
        EmissionEnabled = true,
        Emission = new Color(color.R, color.G, color.B, 1f),
        EmissionEnergyMultiplier = 1.15f
    };

    private static MeshInstance3D AbilityRing(
        string name,
        float width,
        Material material) => new()
    {
        Name = name,
        Mesh = new TorusMesh
        {
            InnerRadius = MathF.Max(0.05f, 1f - width),
            OuterRadius = 1f,
            Rings = 64,
            RingSegments = 8
        },
        MaterialOverride = material,
        Visible = false,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
    };

    private sealed class UnitVisual(
        Node3D root,
        War3ModelActor actor,
        MeshInstance3D selection,
        War3UnitDefinition definition,
        int boundUnitTypeId)
    {
        public Node3D Root { get; } = root;
        public War3ModelActor Actor { get; } = actor;
        public MeshInstance3D Selection { get; } = selection;
        public War3UnitDefinition Definition { get; } = definition;
        public int BoundUnitTypeId { get; } = boundUnitTypeId;
        public float LastWindup { get; set; }
        public float LastCooldown { get; set; }
        public NVector2 LastPosition { get; set; }
        public bool Dying { get; set; }
        public ulong RemoveAt { get; set; }
        public ulong AbilityAnimationUntil { get; set; }
        public int TreeHarvestResourceNode { get; set; } = -1;
        public int LastTreeHarvestStrike { get; set; } = -1;
        public War3StaticModelBatch? LodBatch { get; set; }
        public int LodIndex { get; set; } = -1;
        public bool FullDetail { get; set; } = true;
    }

    private sealed class BuildingVisual(
        Node3D root,
        War3ModelActor actor,
        MeshInstance3D selection,
        MeshInstance3D shadowProxy,
        float shadowProxyHeight,
        float frustumRadius,
        War3BuildingDefinition definition,
        bool modelLoaded,
        int typeId)
    {
        public Node3D Root { get; } = root;
        public War3ModelActor Actor { get; } = actor;
        public MeshInstance3D Selection { get; } = selection;
        public MeshInstance3D ShadowProxy { get; } = shadowProxy;
        public float ShadowProxyHeight { get; } = shadowProxyHeight;
        public float FrustumRadius { get; } = frustumRadius;
        public bool ModelLoaded { get; set; } = modelLoaded;
        public War3BuildingDefinition Definition { get; private set; } =
            definition;
        public string[] StandAnimations { get; private set; } =
            War3AnimationPropertyResolver.Stand(
                definition.AnimationProperties);
        public string[] WorkingStandAnimations { get; private set; } =
            War3AnimationPropertyResolver.Stand(
                definition.AnimationProperties, working: true);
        public string[] AttackAnimations { get; private set; } =
            War3AnimationPropertyResolver.Attack(
                definition.AnimationProperties);
        public int TypeId { get; set; } = typeId;
        public NVector2 LastPosition { get; set; }
        public bool WasGhost { get; set; }
        public bool Dying { get; set; }
        public ulong RemoveAt { get; set; }
        public bool LayoutInitialized { get; set; }
        public bool GhostInitialized { get; set; }
        public bool IsGhost { get; set; }
        public BuildingCombatPhase LastCombatPhase { get; set; } =
            BuildingCombatPhase.Idle;

        public void SetDefinition(War3BuildingDefinition value)
        {
            Definition = value;
            StandAnimations = War3AnimationPropertyResolver.Stand(
                value.AnimationProperties);
            WorkingStandAnimations = War3AnimationPropertyResolver.Stand(
                value.AnimationProperties, working: true);
            AttackAnimations = War3AnimationPropertyResolver.Attack(
                value.AnimationProperties);
        }
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
        public ulong LastHitAt { get; set; }
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

    private sealed record BuildingProjectileVisual(
        Node3D Root,
        War3BuildingDefinition Definition,
        NVector2 LastPosition,
        int SourcePlayerId)
    {
        public NVector2 LastPosition { get; set; } = LastPosition;
        public bool HasPosition { get; set; }
    }

    private sealed record AbilityProjectileVisual(
        Node3D Root,
        War3AbilityDefinition Definition,
        NVector2 LastPosition,
        int SourcePlayerId)
    {
        public NVector2 LastPosition { get; set; } = LastPosition;
        public bool HasPosition { get; set; }
    }

    private sealed record TransientVisual(
        Node3D Root,
        ulong RemoveAt,
        bool CommandConfirmation = false);
    private readonly record struct AbilityVisualInstance(
        string Model,
        string Attachment);
    private sealed record AbilityBuffPart(
        War3ModelActor Actor,
        string Attachment);
    private sealed record AbilityBuffVisual(
        AbilityBuffPart[] Parts,
        int TargetUnit);
}
