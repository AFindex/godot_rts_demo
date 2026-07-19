using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;
using War3Rts.Data;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

public readonly record struct War3PresenterSyncProfile(
    double NodeFreeAdvanceMilliseconds,
    long NodeFreeAdvanceAllocatedBytes,
    double UnitsMilliseconds,
    long UnitsAllocatedBytes,
    double UnitAnimationMilliseconds,
    int UnitActorsVisited,
    int UnitActorsAlive,
    int UnitActorsInFrustum,
    int UnitActorsCreated,
    long UnitResolveAllocatedBytes,
    long UnitFrustumAllocatedBytes,
    long UnitTransformAllocatedBytes,
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
    long TransientsAllocatedBytes,
    double NodeFreeEffectsMilliseconds,
    long NodeFreeEffectsAllocatedBytes,
    double NodeFreeCommitMilliseconds,
    long NodeFreeCommitAllocatedBytes,
    int NodeFreeBatchBufferUploads,
    long NodeFreeBatchUploadedBytes);

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
    private const int DenseCombatUnitThreshold = 240;
    private readonly Dictionary<int, UnitVisual> _units = [];
    private readonly Dictionary<UnitPoolKey, Stack<UnitVisual>> _unitPool = [];
    private readonly Dictionary<int, BuildingVisual> _buildings = [];
    private readonly Dictionary<int, ResourceVisual> _resources = [];
    private readonly List<War3StaticModelBatch> _resourceBatches = [];
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
    private CombatProjectileSnapshot[] _combatProjectileScratch = [];
    private MultiMesh? _denseCombatProjectileMultiMesh;
    private float[] _denseCombatProjectileBuffer = [];
    private MultiMesh? _contactShadowMultiMesh;
    private float[] _contactShadowBuffer = [];
    private int _contactShadowCount;
    private int _unitContactShadowCount;
    private int _buildingContactShadowCount;
    private readonly Plane[] _cameraFrustumPlanes = new Plane[6];
    private readonly float[] _cameraFrustumInsideSigns = new float[6];
    private int _cameraFrustumPlaneCount;
    private GameplayBuildingSnapshot[] _buildingOverviewScratch = [];
    private readonly HashSet<int> _selectedUnits = [];
    private readonly HashSet<int> _selectedBuildings = [];
    private readonly Dictionary<float, TorusMesh> _selectionRingMeshes = [];
    private readonly Dictionary<Color, StandardMaterial3D>
        _selectionRingMaterials = [];
    private readonly Dictionary<string, War3TreeHarvestFeedbackProfile>
        _treeHarvestProfiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _profiledPointerGhostSources =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _profiledBuildingActorSources =
        new(StringComparer.OrdinalIgnoreCase);
    private RtsSimulation? _simulation;
    private ProductionCatalogSnapshot? _production;
    private ITerrainMapQuery? _terrain;
    private Camera3D? _camera;
    private War3NodeFreeRenderWorld? _ridWorld;
    private MeshInstance3D? _pointerPreview;
    private War3RidModelActor? _pointerGhost;
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
    private bool _sawUnitDeathAnimation;
    private bool _sawUnitDecayFlesh;
    private bool _sawUnitDecayBone;
    private bool _sawUnitDeathCompleted;
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
    private long _profileUnitTransformAllocatedBytes;
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
    public int PresentedContactShadowCount => _contactShadowCount;
    public int PresentedUnitContactShadowCount => _unitContactShadowCount;
    public int PresentedBuildingContactShadowCount =>
        _buildingContactShadowCount;
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
    public bool SawUnitDeathAnimation => _sawUnitDeathAnimation;
    public bool SawUnitDecayFlesh => _sawUnitDecayFlesh;
    public bool SawUnitDecayBone => _sawUnitDecayBone;
    public bool SawUnitDeathCompleted => _sawUnitDeathCompleted;
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
    public int NodeFreeActorCount => _ridWorld?.ActorCount ?? 0;
    public int NodeFreeEffectActorCount => _ridWorld?.EffectActorCount ?? 0;
    public int NodeFreeGeometryCount => _ridWorld?.GeometryCount ?? 0;
    public int ImportedModelProbeNodeCount =>
        _ridWorld?.ImportedProbeNodeCount ?? 0;
    public int BattlefieldEntityNodeCount => 0;
    public int ActiveCommandConfirmationCount =>
        _transients.Count(value => value.CommandConfirmation);
    public bool PointerPreviewUsesWar3Model => _pointerGhost is not null;
    public bool AbilityPointerPreviewVisible =>
        _abilityTargetPreview?.Visible == true;
    public bool AbilityRangePreviewVisible =>
        _abilityRangePreview?.Visible == true;
    public bool RallyMarkerUsesWar3Model => _rallyMarker?.Loaded == true &&
        _rallyMarker.Source.Equals(
            "UI\\Feedback\\RallyPoint\\RallyPoint.mdx",
            StringComparison.OrdinalIgnoreCase);
    public bool ProfilingEnabled { get; set; }
    /// <summary>
    /// Test-only presentation override for detailed projectile actors and all
    /// impact visuals. Authoritative combat is unchanged either way.
    /// </summary>
    public bool ForceFullCombatEffects { get; set; }
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
                    visual.Actor.Visible = false;
                break;
            case "buildings-hidden":
                foreach (var visual in _buildings.Values)
                    visual.Actor.Visible = false;
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
        GD.Print(
            $"WAR3_NODE_FREE_LAYOUT actors={NodeFreeActorCount} " +
            $"effect_actors={NodeFreeEffectActorCount} " +
            $"geometry={NodeFreeGeometryCount} " +
            $"entity_nodes={BattlefieldEntityNodeCount} " +
            $"imported_probe_nodes={ImportedModelProbeNodeCount}");
    }

    private static void PrintCategoryRenderLayout(
        string category,
        IEnumerable<War3RidModelActor> actors)
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
        _ridWorld = new War3NodeFreeRenderWorld(this, camera)
        {
            ProfilingEnabled = ProfilingEnabled
        };
        EnsurePointerPreview();
        EnsureAbilityPointerPreview();
        EnsureRallyMarker();
        CreateStaticResourceBatches(simulation);
        Sync(1f);
        if (ProfilingEnabled) PrintRuntimeRenderLayout();
    }

    public bool PrewarmModelAsset(string modelSource, int playerId) =>
        _ridWorld?.PrewarmAsset(modelSource, playerId) ?? false;

    public bool PrewarmBuildingModelAsset(
        string modelSource,
        int playerId) =>
        _ridWorld?.PrewarmAsset(
            modelSource,
            playerId,
            prewarmBuildingLanes: true,
            prewarmEffects: true) ?? false;

    public void FinishBuildingModelPrewarm() =>
        _ridWorld?.FinishRendererPrewarm();

    public void SetSelection(
        IEnumerable<int> units,
        IEnumerable<int> buildings)
    {
        _selectedUnits.Clear();
        _selectedUnits.UnionWith(units);
        _selectedBuildings.Clear();
        _selectedBuildings.UnionWith(buildings);
        foreach (var pair in _units)
        {
            var visible = _selectedUnits.Contains(pair.Key) &&
                          !pair.Value.Dying &&
                          pair.Value.ActorVisible;
            if (pair.Value.SelectionVisible == visible) continue;
            pair.Value.Selection.Visible = visible;
            pair.Value.SelectionVisible = visible;
        }
        foreach (var pair in _buildings)
            pair.Value.Selection.Visible = _selectedBuildings.Contains(pair.Key) &&
                                            !pair.Value.Dying &&
                                            pair.Value.ActorVisible;
    }

    public override void _ExitTree()
    {
        foreach (var batch in _resourceBatches) batch.Dispose();
        _resourceBatches.Clear();
        _ridWorld?.Dispose();
        _ridWorld = null;
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
        if (_ridWorld is not null && !modelSource.Equals(
                _pointerGhostSource, StringComparison.OrdinalIgnoreCase))
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            _pointerGhost?.Dispose();
            _pointerGhost = _ridWorld.CreateActor(
                modelSource,
                War3HumanScenario.PlayerId,
                includeEffects: false);
            _pointerGhost.PlayPreferred(true, "Stand");
            _pointerGhostSource = modelSource;
            if (_profiledPointerGhostSources.Add(modelSource))
                GD.Print(
                    $"WAR3_BUILDING_POINTER_FIRST_USE source={modelSource} " +
                    $"elapsed_ms={ElapsedMilliseconds(started, System.Diagnostics.Stopwatch.GetTimestamp()):0.###} " +
                    "renderer=rid-vat-prewarmed");
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
        _profileUnitTransformAllocatedBytes = 0;
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
        var presenterTransform = GlobalTransform;
        var frameTicksMsec = Time.GetTicksMsec();
        var presenterBasis = presenterTransform.Basis;
        var cameraTransform = _camera.GlobalTransform;
        War3EffectRuntime.PrepareCameraFrame(_camera, cameraTransform.Basis);
        PrepareCameraFrustum(_camera, cameraTransform);
        _ridWorld?.Advance();
        EnsureContactShadowCapacity(
            _simulation.Units.Count +
            _simulation.GameplayBuildingSlotCount);
        _contactShadowCount = 0;
        _unitContactShadowCount = 0;
        _buildingContactShadowCount = 0;
        var advanceEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var advanceAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        SyncUnits(
            _simulation, _production, _camera, presenterTransform,
            interpolation, frameTicksMsec);
        var unitsEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var unitsAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        SyncBuildings(_simulation, _camera, presenterTransform);
        FlushContactShadows();
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
        _ridWorld?.Flush();
        var commitEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var commitAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        if (ProfilingEnabled)
        {
            LastSyncProfile = new War3PresenterSyncProfile(
                ElapsedMilliseconds(stageStart, advanceEnd),
                advanceAllocationEnd - allocationStart,
                ElapsedMilliseconds(advanceEnd, unitsEnd),
                unitsAllocationEnd - advanceAllocationEnd,
                _profileUnitAnimationMilliseconds,
                _profileUnitActorsVisited,
                _profileUnitActorsAlive,
                _profileUnitActorsInFrustum,
                _profileUnitActorsCreated,
                _profileUnitResolveAllocatedBytes,
                _profileUnitFrustumAllocatedBytes,
                _profileUnitTransformAllocatedBytes,
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
                transientsAllocationEnd - projectilesAllocationEnd,
                _ridWorld?.LastEffectsMilliseconds ?? 0d,
                _ridWorld?.LastEffectsAllocatedBytes ?? 0L,
                _ridWorld?.LastCommitMilliseconds ??
                ElapsedMilliseconds(transientsEnd, commitEnd),
                _ridWorld?.LastCommitAllocatedBytes ??
                commitAllocationEnd - transientsAllocationEnd,
                _ridWorld?.LastBatchBufferUploads ?? 0,
                _ridWorld?.LastBatchUploadedBytes ?? 0L);
        }
        _peakEffectCount = Math.Max(_peakEffectCount, ActiveEffectCount);
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d / System.Diagnostics.Stopwatch.Frequency;

    private void SyncUnits(
        RtsSimulation simulation,
        ProductionCatalogSnapshot production,
        Camera3D camera,
        Transform3D presenterTransform,
        float interpolation,
        ulong frameTicksMsec)
    {
        const int detailedProfileSampleStride = 64;
        var presenterBasis = presenterTransform.Basis;
        var creationProgressStart = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        foreach (var pair in _units)
        {
            var unit = pair.Key;
            if ((uint)unit < (uint)simulation.Units.Count &&
                simulation.Units.Alive[unit])
                continue;
            var dead = pair.Value;
            if (ProfilingEnabled) _profileUnitActorsVisited++;
            if (!dead.Dying)
            {
                dead.Dying = true;
                if (dead.SelectionVisible)
                {
                    dead.Selection.Visible = false;
                    dead.SelectionVisible = false;
                }
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
            _sawUnitDeathAnimation |= dead.Actor.DeathPhase ==
                                      War3DeathPresentationPhase.Death;
            _sawUnitDecayFlesh |= dead.Actor.DeathPhase ==
                                  War3DeathPresentationPhase.DecayFlesh;
            _sawUnitDecayBone |= dead.Actor.DeathPhase ==
                                 War3DeathPresentationPhase.DecayBone;
        }

        foreach (var unit in simulation.Units.AliveUnits)
        {
            // Per-unit timestamp/GC probes materially perturb an 800-unit
            // profile. Sample a stable 1/64 subset and scale aggregate-only
            // timings; actor counts remain exact.
            var detailedProfileSample = ProfilingEnabled &&
                                        (unit &
                                         (detailedProfileSampleStride - 1)) == 0;
            if (ProfilingEnabled) _profileUnitActorsVisited++;
            var resolveAllocationStart = detailedProfileSample
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            var hasVisual = _units.TryGetValue(unit, out var visual);
            var definitionChanged = !hasVisual || visual is null ||
                !UnitDefinitionMatches(simulation, unit, visual);
            var resolvedDefinition = definitionChanged
                ? War3HumanContent.ResolveUnit(
                    simulation, production, unit)
                : visual!.Definition;
            if (detailedProfileSample)
                _profileUnitResolveAllocatedBytes +=
                    (GC.GetAllocatedBytesForCurrentThread() -
                     resolveAllocationStart) * detailedProfileSampleStride;
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
                PoolUnitVisual(visual);
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
                visual.Actor.Revive();
                visual.AnimationState = UnitAnimationState.None;
                visual.AnimationVariant = -1;
            }
            var definition = visual.Definition;
            if (ProfilingEnabled) _profileUnitActorsAlive++;
            var position = NVector2.Lerp(
                simulation.Units.PreviousPositions[unit],
                simulation.Units.Positions[unit], interpolation);
            visual.LastPosition = position;
            var world = ToWorldAtGround(position, definition.FlyingHeight);
            var frustumAllocationStart = detailedProfileSample
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            var inFrustum = IsPositionInPreparedFrustum(camera, world);
            if (detailedProfileSample)
                _profileUnitFrustumAllocatedBytes +=
                    (GC.GetAllocatedBytesForCurrentThread() -
                     frustumAllocationStart) * detailedProfileSampleStride;
            if (ProfilingEnabled && inFrustum)
                _profileUnitActorsInFrustum++;
            var facing = UnitFacing.Interpolate(
                simulation.Units.PreviousFacings[unit],
                simulation.Units.Facings[unit], interpolation);
            var facingDirection = UnitFacing.Direction(facing);
            var angle = MathF.Atan2(facingDirection.X, facingDirection.Y);
            var lodAllocationStart = detailedProfileSample
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            var selected = _selectedUnits.Contains(unit);
            var hiddenInsideGoldMine = IsGatheringGold(simulation, unit);
            var visibleToPlayer = simulation.Visibility.IsUnitVisible(
                War3HumanScenario.PlayerId,
                unit,
                simulation.Units,
                simulation.Combat);
            var actorTransform = new Transform3D(
                new Basis(Vector3.Up, angle), world);
            visual.Actor.Processing = inFrustum && visibleToPlayer;
            var actorVisible = inFrustum && visibleToPlayer &&
                               !hiddenInsideGoldMine;
            if (actorVisible)
                WriteUnitContactShadow(
                    position,
                    angle,
                    simulation.Units.NavigationRadii[unit]);
            if (visual.ActorVisible != actorVisible)
            {
                visual.Actor.Visible = actorVisible;
                visual.ActorVisible = actorVisible;
            }
            if (inFrustum && visibleToPlayer)
                visual.Actor.PrepareEffectWorldTransform(
                    presenterTransform * actorTransform);
            if (!visual.TransformInitialized ||
                visual.LastActorPosition != world ||
                visual.LastActorAngle != angle)
            {
                visual.Actor.Transform = actorTransform;
                visual.LastActorPosition = world;
                visual.LastActorAngle = angle;
            }
            visual.TransformInitialized = true;
            if (simulation.Combat.TargetKinds[unit] != CombatTargetKind.None)
            {
                _sawAttackTargetFacing = true;
                _attackTargetFacingMismatch |= MathF.Abs(
                    Mathf.AngleDifference(
                        visual.LastActorAngle, angle)) > 0.001f;
            }
            _sawGoldMinerHidden |= hiddenInsideGoldMine;
            if (selected)
                visual.Selection.Position = new Vector3(
                    world.X, world.Y + SelectionHeight, world.Z);
            var selectionVisible = selected && actorVisible;
            if (visual.SelectionVisible != selectionVisible)
            {
                visual.Selection.Visible = selectionVisible;
                visual.SelectionVisible = selectionVisible;
            }
            if (detailedProfileSample)
                _profileUnitTransformAllocatedBytes +=
                    (GC.GetAllocatedBytesForCurrentThread() -
                     lodAllocationStart) * detailedProfileSampleStride;
            var animationAllocationStart = detailedProfileSample
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            var animationStart = detailedProfileSample
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0L;
            UpdateUnitAnimation(
                simulation, unit, visual, frameTicksMsec);
            if (detailedProfileSample)
            {
                _profileUnitAnimationMilliseconds += ElapsedMilliseconds(
                    animationStart,
                    System.Diagnostics.Stopwatch.GetTimestamp()) *
                    detailedProfileSampleStride;
                _profileUnitAnimationAllocatedBytes +=
                    (GC.GetAllocatedBytesForCurrentThread() -
                     animationAllocationStart) * detailedProfileSampleStride;
            }
            _sawBlendedTransition |=
                visual.Actor.BlendedPoseCommitCount > 0;
        }

        foreach (var id in _units.Where(pair => pair.Value.Dying &&
                                                pair.Value.Actor.DeathPresentationComplete)
                     .Select(pair => pair.Key).ToArray())
        {
            _sawUnitDeathCompleted = true;
            PoolUnitVisual(_units[id]);
            _units.Remove(id);
        }
    }

    private static bool UnitDefinitionMatches(
        RtsSimulation simulation,
        int unit,
        UnitVisual visual)
    {
        if (War3HumanContent.TryResolveReplacementUnit(
                simulation, unit, out var replacementDefinition))
            return visual.Definition.ObjectId.Equals(
                replacementDefinition.ObjectId, StringComparison.Ordinal);
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
        UnitVisual visual,
        ulong frameTicksMsec)
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
            if (EnterUnitAnimationState(
                    visual,
                    UnitAnimationState.Ability,
                    abilityState.ActiveAbilityId))
            {
                var candidates = (uint)abilityState.ActiveAbilityId <
                                 (uint)War3HumanContent.Abilities.Count
                    ? War3HumanContent.Ability(
                        abilityState.ActiveAbilityId).AnimationNames
                    : ["Spell Channel", "Spell", "Spell Slam", "Attack"];
                visual.Actor.PlayRepeatedPreferred(candidates);
            }
            return;
        }
        if (frameTicksMsec < visual.AbilityAnimationUntil)
        {
            EnterUnitAnimationState(
                visual, UnitAnimationState.AbilityLock);
            return;
        }
        if (simulation.Economy.IsWorker(unit))
        {
            if (simulation.Construction.IsAssignedBuilder(unit))
            {
                ResetTreeHarvestFeedback(visual);
                if (moving)
                {
                    if (EnterUnitAnimationState(
                            visual, UnitAnimationState.BuilderWalk))
                        visual.Actor.PlayPreferred(true, "Walk", "Stand");
                }
                else
                {
                    if (EnterUnitAnimationState(
                            visual, UnitAnimationState.BuilderWork))
                        visual.Actor.PlayPreferred(
                            true, "Stand Work", "Attack", "Stand");
                }
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
                    if (EnterUnitAnimationState(
                            visual, UnitAnimationState.GatherLumber))
                        visual.Actor.PlayRepeatedPreferred(
                            "Attack Lumber", "Stand Work Lumber",
                            "Attack", "Stand Work");
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
                    if (EnterUnitAnimationState(
                            visual, UnitAnimationState.GatherGold))
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
                    if (EnterUnitAnimationState(
                            visual,
                            UnitAnimationState.WorkerWalk,
                            CargoAnimationVariant(carriesLumber, carriesGold)))
                        visual.Actor.PlayPreferred(true,
                            carriesLumber
                                ? "Walk Lumber"
                                : carriesGold ? "Walk Gold" : "Walk",
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
                    if (EnterUnitAnimationState(
                            visual,
                            UnitAnimationState.WorkerStand,
                            CargoAnimationVariant(carriesLumber, carriesGold)))
                        visual.Actor.PlayPreferred(true,
                            carriesLumber
                                ? "Stand Lumber"
                                : carriesGold ? "Stand Gold" : "Stand",
                            "Stand");
                }
                return;
            }
        }
        if (simulation.Combat.Phases[unit] == CombatPhase.Attacking)
        {
            if (attackCycleStarted)
            {
                EnterUnitAnimationState(visual, UnitAnimationState.Attack);
                visual.Actor.ReplayPreferred("Attack", "Spell Attack", "Spell");
            }
            else if (!visual.Actor.IsAnimationPlaying ||
                     !visual.Actor.CurrentSequence.StartsWith(
                         "Attack", StringComparison.OrdinalIgnoreCase))
            {
                if (EnterUnitAnimationState(
                        visual, UnitAnimationState.AttackReady))
                    visual.Actor.PlayPreferred(true, "Stand Ready", "Stand");
            }
            else
            {
                EnterUnitAnimationState(visual, UnitAnimationState.Attack);
            }
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
                if (EnterUnitAnimationState(
                        visual,
                        UnitAnimationState.WorkerWalk,
                        CargoAnimationVariant(carriesLumber, carriesGold)))
                    visual.Actor.PlayPreferred(true,
                        carriesLumber
                            ? "Walk Lumber"
                            : carriesGold ? "Walk Gold" : "Walk",
                        "Walk");
                _sawCarriedLumberAnimation |= carriesLumber &&
                    visual.Actor.CurrentSequence.Contains(
                        "Lumber", StringComparison.OrdinalIgnoreCase);
                _sawCarriedGoldAnimation |= carriesGold &&
                    visual.Actor.CurrentSequence.Contains(
                        "Gold", StringComparison.OrdinalIgnoreCase);
                return;
            }
            if (EnterUnitAnimationState(
                    visual,
                    UnitAnimationState.WorkerStand,
                    CargoAnimationVariant(carriesLumber, carriesGold)))
                visual.Actor.PlayPreferred(true,
                    carriesLumber
                        ? "Stand Lumber"
                        : carriesGold ? "Stand Gold" : "Stand",
                    "Stand");
            return;
        }
        if (moving)
        {
            if (EnterUnitAnimationState(visual, UnitAnimationState.Walk))
                visual.Actor.PlayPreferred(true, "Walk", "Stand");
        }
        else if (simulation.Combat.Phases[unit] != CombatPhase.None)
        {
            if (EnterUnitAnimationState(
                    visual, UnitAnimationState.CombatReady))
                visual.Actor.PlayPreferred(true, "Stand Ready", "Stand");
        }
        else
        {
            if (EnterUnitAnimationState(visual, UnitAnimationState.Stand))
                visual.Actor.PlayPreferred(true, "Stand");
        }
    }

    private static bool EnterUnitAnimationState(
        UnitVisual visual,
        UnitAnimationState state,
        int variant = 0)
    {
        if (visual.AnimationState == state &&
            visual.AnimationVariant == variant)
            return false;
        visual.AnimationState = state;
        visual.AnimationVariant = variant;
        return true;
    }

    private static int CargoAnimationVariant(
        bool carriesLumber,
        bool carriesGold) => carriesLumber ? 1 : carriesGold ? 2 : 0;

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
                War3RidModelActor? casterActor = null;
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
        War3RidModelActor? hostActor,
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
                    var actor = _ridWorld!.CreateActor(
                        instance.Model,
                        targetPlayerId,
                        includeEffects: true);
                    var emitterId = _nextTransientAudioEmitterId++;
                    actor.SoundTimelineEvent += value => PublishAnimationAudio(
                        value,
                        simulation.Units.Positions[buff.TargetUnit],
                        emitterId,
                        sourcePlayerId);
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
                part.Actor.Dispose();
            _abilityBuffs.Remove(id);
        }
    }

    private War3RidModelActor? AbilityTargetActor(in AbilityEvent value)
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
        War3RidModelActor? hostActor)
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

    private void SyncBuildings(
        RtsSimulation simulation,
        Camera3D camera,
        Transform3D presenterTransform)
    {
        var presenterBasis = presenterTransform.Basis;
        _seenBuildings.Clear();
        var overviewAllocationStart = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        var requiredBuildingSlots = simulation.GameplayBuildingSlotCount;
        if (_buildingOverviewScratch.Length < requiredBuildingSlots)
        {
            var capacity = Math.Max(
                requiredBuildingSlots,
                Math.Max(16, _buildingOverviewScratch.Length * 2));
            Array.Resize(ref _buildingOverviewScratch, capacity);
        }
        var buildingCount = simulation.CopyGameplayBuildingOverview(
            _buildingOverviewScratch);
        var buildings = _buildingOverviewScratch.AsSpan(0, buildingCount);
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
                    visual.Actor.SoundTimelineEvent -= visual.SoundHandler;
                    visual.Actor.Dispose();
                    visual.Actor = _ridWorld!.CreateActor(
                        definition.ModelSource,
                        building.PlayerId,
                        includeEffects: true);
                    visual.Actor.SoundTimelineEvent += visual.SoundHandler;
                    visual.Actor.Position = visual.WorldPosition;
                    visual.Actor.SetShadowCastingEnabled(true);
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
                visual.WorldPosition = world;
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
                visual.LayoutInitialized = true;
            }
            var foundationStarted = building.FootprintId.Value > 0 ||
                                    building.State is BuildingLifecycleState.Constructing or
                                        BuildingLifecycleState.Completed;
            var ghost = !foundationStarted;
            var inFrustum = BuildingIntersectsFrustum(camera, visual);
            var visibleToPlayer =
                building.PlayerId == War3HumanScenario.PlayerId ||
                simulation.Visibility.IsVisible(
                    War3HumanScenario.PlayerId,
                    visual.LastPosition);
            var actorVisible = inFrustum && visibleToPlayer;
            if (actorVisible && foundationStarted)
                WriteBuildingContactShadow(building.Bounds);
            visual.Actor.Processing = actorVisible;
            if (visual.ActorVisible != actorVisible)
            {
                visual.Actor.Visible = actorVisible;
                visual.ActorVisible = actorVisible;
            }
            visual.Selection.Visible = foundationStarted &&
                                       actorVisible &&
                                       _selectedBuildings.Contains(id);
            if (!actorVisible) continue;
            if (ProfilingEnabled) _profileBuildingActorsInFrustum++;
            var animationAllocationStart = ProfilingEnabled
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            visual.Actor.PrepareEffectWorldTransform(
                presenterTransform * new Transform3D(
                    Basis.Identity, visual.WorldPosition));
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
            pair.Value.Selection.Visible = false;
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
                                                    pair.Value.Actor.DeathPresentationComplete)
                     .Select(pair => pair.Key).ToArray())
        {
            _buildings[id].Actor.Dispose();
            _buildings[id].Selection.Dispose();
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
            var fogVisible = simulation.Visibility.At(
                War3HumanScenario.PlayerId,
                snapshot.Position) != MapVisibility.Hidden;
            if (visual.FogVisible != fogVisible)
            {
                visual.FogVisible = fogVisible;
                if (visual.Actor is not null)
                    visual.Actor.Visible = fogVisible;
                else if (visual.StaticBatch is not null && !visual.Depleted)
                    visual.StaticBatch.SetInstanceVisible(
                        visual.StaticBatchIndex,
                        fogVisible);
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
                        visual.Actor.Visible = fogVisible;
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
        if (!ForceFullCombatEffects &&
            simulation.Units.Count >= DenseCombatUnitThreshold)
        {
            SyncDenseCombatProjectiles(simulation);
            return;
        }
        if (_denseCombatProjectileMultiMesh is not null)
            _denseCombatProjectileMultiMesh.VisibleInstanceCount = 0;

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
                if (!ForceFullCombatEffects &&
                    visualCreations >= MaximumProjectileVisualCreationsPerSync)
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
            visual.LastPosition = projectile.Position;
            var height = MathF.Max(0.7f, definition.FlyingHeight * 0.55f + 0.55f);
            UpdateProjectileTransform(
                visual.Root,
                ToWorldAtGround(projectile.Position, height),
                displacement,
                visual.HasPosition);
            visual.HasPosition = true;
        }
        var impactVisuals = 0;
        foreach (var id in _projectiles.Keys.Where(id => !_seenProjectiles.Contains(id)).ToArray())
        {
            var visual = _projectiles[id];
            visual.Root.Dispose();
            _projectiles.Remove(id);
            if (visual.Definition.ImpactSource.Length > 0 &&
                War3RuntimeAssets.Contains(visual.Definition.ImpactSource) &&
                (ForceFullCombatEffects ||
                 impactVisuals < MaximumProjectileImpactVisualsPerSync &&
                 NonCommandTransientCount() <
                 MaximumNonCommandTransientVisuals))
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

    private static void UpdateProjectileTransform(
        IWar3RidSpatial root,
        Vector3 position,
        NVector2 displacement,
        bool hasPreviousPosition)
    {
        var basis = root.Transform.Basis;
        if (hasPreviousPosition && displacement.LengthSquared() > 0.0001f)
            basis = new Basis(
                Vector3.Up,
                MathF.Atan2(displacement.X, displacement.Y));
        root.Transform = new Transform3D(basis, position);
    }

    private void SyncDenseCombatProjectiles(RtsSimulation simulation)
    {
        // Dense combat can create and retire hundreds of short-lived missile
        // handles per second. Keep the authoritative projectile simulation
        // untouched while using one fixed presentation buffer for this case.
        if (_projectiles.Count > 0)
        {
            foreach (var visual in _projectiles.Values)
                visual.Root.Dispose();
            _projectiles.Clear();
        }

        var activeCount = simulation.CombatProjectiles.ActiveCount;
        EnsureDenseCombatProjectileCapacity(activeCount);
        if (_denseCombatProjectileMultiMesh is null) return;
        if (activeCount == 0)
        {
            _denseCombatProjectileMultiMesh.VisibleInstanceCount = 0;
            return;
        }

        if (_combatProjectileScratch.Length < activeCount)
            Array.Resize(
                ref _combatProjectileScratch,
                Math.Max(activeCount, _combatProjectileScratch.Length * 2));
        var count = simulation.CombatProjectiles.CopyActiveTo(
            _combatProjectileScratch);
        for (var index = 0; index < count; index++)
        {
            var position = ToWorldAtGround(
                _combatProjectileScratch[index].Position, 0.85f);
            WriteDenseCombatProjectileTransform(index, position);
        }
        RenderingServer.MultimeshSetBuffer(
            _denseCombatProjectileMultiMesh.GetRid(),
            _denseCombatProjectileBuffer.AsSpan());
        _denseCombatProjectileMultiMesh.VisibleInstanceCount = count;
    }

    private void EnsureContactShadowCapacity(int required)
    {
        var currentCapacity = _contactShadowBuffer.Length / 12;
        var capacity = Math.Max(32, currentCapacity);
        while (capacity < required) capacity *= 2;
        if (_contactShadowMultiMesh is null)
        {
            var shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode blend_mix, depth_draw_never,
                        cull_disabled, unshaded;

                    void fragment() {
                        vec2 point = UV * 2.0 - vec2(1.0);
                        float distance_squared = dot(point, point);
                        float opacity =
                            (1.0 - smoothstep(0.12, 1.0, distance_squared)) *
                            0.72;
                        if (opacity < 0.006) discard;
                        ALBEDO = vec3(0.012, 0.016, 0.021);
                        ALPHA = opacity;
                    }
                    """
            };
            var material = new ShaderMaterial { Shader = shader };
            var mesh = new PlaneMesh
            {
                Size = new Vector2(2f, 2f),
                Material = material
            };
            _contactShadowMultiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = capacity,
                VisibleInstanceCount = 0
            };
            AddChild(new MultiMeshInstance3D
            {
                Name = "War3ContactShadows",
                Multimesh = _contactShadowMultiMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 10_000f
            });
            _contactShadowBuffer = new float[capacity * 12];
            return;
        }

        if (capacity <= currentCapacity) return;
        _contactShadowMultiMesh.VisibleInstanceCount = 0;
        _contactShadowMultiMesh.InstanceCount = capacity;
        Array.Resize(ref _contactShadowBuffer, capacity * 12);
    }

    private void WriteUnitContactShadow(
        NVector2 position,
        float angle,
        float navigationRadius)
    {
        var radius = MathF.Max(
            0.26f,
            UnitSelectionWorldRadius(navigationRadius) * 1.65f);
        var transform = new Transform3D(
            new Basis(Vector3.Up, angle).Scaled(new Vector3(
                radius * 1.18f,
                1f,
                radius * 0.82f)),
            ToWorldAtGround(position, 0.028f));
        WriteTransformBuffer(
            _contactShadowBuffer,
            _contactShadowCount * 12,
            transform);
        _contactShadowCount++;
        _unitContactShadowCount++;
    }

    private void WriteBuildingContactShadow(SimRect bounds)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var footprint = SimPlane3DTransform.ToWorldSize(
            bounds.Max - bounds.Min);
        var castDistance = Math.Clamp(
            MathF.Max(footprint.X, footprint.Y) * 0.2f,
            0.32f,
            0.95f);
        var world = ToWorldAtGround(center, 0.026f) +
                    new Vector3(
                        castDistance * 0.72f,
                        0f,
                        castDistance);
        var transform = new Transform3D(
            Basis.FromScale(new Vector3(
                MathF.Max(0.48f, footprint.X * 0.68f),
                1f,
                MathF.Max(0.48f, footprint.Y * 0.68f))),
            world);
        WriteTransformBuffer(
            _contactShadowBuffer,
            _contactShadowCount * 12,
            transform);
        _contactShadowCount++;
        _buildingContactShadowCount++;
    }

    private void FlushContactShadows()
    {
        if (_contactShadowMultiMesh is null) return;
        if (_contactShadowCount == 0)
        {
            _contactShadowMultiMesh.VisibleInstanceCount = 0;
            return;
        }
        RenderingServer.MultimeshSetBuffer(
            _contactShadowMultiMesh.GetRid(),
            _contactShadowBuffer.AsSpan());
        _contactShadowMultiMesh.VisibleInstanceCount =
            _contactShadowCount;
    }

    private static void WriteTransformBuffer(
        float[] buffer,
        int offset,
        Transform3D transform)
    {
        var basis = transform.Basis;
        var origin = transform.Origin;
        buffer[offset] = basis.X.X;
        buffer[offset + 1] = basis.Y.X;
        buffer[offset + 2] = basis.Z.X;
        buffer[offset + 3] = origin.X;
        buffer[offset + 4] = basis.X.Y;
        buffer[offset + 5] = basis.Y.Y;
        buffer[offset + 6] = basis.Z.Y;
        buffer[offset + 7] = origin.Y;
        buffer[offset + 8] = basis.X.Z;
        buffer[offset + 9] = basis.Y.Z;
        buffer[offset + 10] = basis.Z.Z;
        buffer[offset + 11] = origin.Z;
    }

    private void EnsureDenseCombatProjectileCapacity(int required)
    {
        var capacity = Math.Max(32, _denseCombatProjectileBuffer.Length / 12);
        while (capacity < required) capacity *= 2;
        if (_denseCombatProjectileMultiMesh is null)
        {
            var material = Emissive(new Color("ffd46a"));
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            var mesh = new SphereMesh
            {
                Radius = 0.075f,
                Height = 0.15f,
                RadialSegments = 8,
                Rings = 4,
                Material = material
            };
            _denseCombatProjectileMultiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = capacity,
                VisibleInstanceCount = 0
            };
            AddChild(new MultiMeshInstance3D
            {
                Name = "DenseCombatProjectiles",
                Multimesh = _denseCombatProjectileMultiMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
            _denseCombatProjectileBuffer = new float[capacity * 12];
            return;
        }

        var currentCapacity = _denseCombatProjectileBuffer.Length / 12;
        if (capacity <= currentCapacity) return;
        _denseCombatProjectileMultiMesh.InstanceCount = capacity;
        Array.Resize(ref _denseCombatProjectileBuffer, capacity * 12);
    }

    private void WriteDenseCombatProjectileTransform(
        int index,
        Vector3 position)
    {
        var offset = index * 12;
        _denseCombatProjectileBuffer[offset] = 1f;
        _denseCombatProjectileBuffer[offset + 1] = 0f;
        _denseCombatProjectileBuffer[offset + 2] = 0f;
        _denseCombatProjectileBuffer[offset + 3] = position.X;
        _denseCombatProjectileBuffer[offset + 4] = 0f;
        _denseCombatProjectileBuffer[offset + 5] = 1f;
        _denseCombatProjectileBuffer[offset + 6] = 0f;
        _denseCombatProjectileBuffer[offset + 7] = position.Y;
        _denseCombatProjectileBuffer[offset + 8] = 0f;
        _denseCombatProjectileBuffer[offset + 9] = 0f;
        _denseCombatProjectileBuffer[offset + 10] = 1f;
        _denseCombatProjectileBuffer[offset + 11] = position.Z;
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
                if (!ForceFullCombatEffects &&
                    creations >= MaximumProjectileVisualCreationsPerSync)
                    continue;
                visual = CreateBuildingProjectile(
                    projectile.Id, definition, attacker.PlayerId, camera);
                _buildingProjectiles.Add(projectile.Id, visual);
                creations++;
            }
            var displacement = projectile.Position - visual.LastPosition;
            visual.LastPosition = projectile.Position;
            UpdateProjectileTransform(
                visual.Root,
                ToWorldAtGround(projectile.Position, 1.05f),
                displacement,
                visual.HasPosition);
            visual.HasPosition = true;
        }

        var impacts = 0;
        foreach (var id in _buildingProjectiles.Keys
                     .Where(id => !_seenBuildingProjectiles.Contains(id))
                     .ToArray())
        {
            var visual = _buildingProjectiles[id];
            visual.Root.Dispose();
            _buildingProjectiles.Remove(id);
            if (visual.Definition.ImpactSource.Length == 0 ||
                !War3RuntimeAssets.Contains(visual.Definition.ImpactSource) ||
                !ForceFullCombatEffects &&
                (impacts >= MaximumProjectileImpactVisualsPerSync ||
                 NonCommandTransientCount() >=
                 MaximumNonCommandTransientVisuals))
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
                if (!ForceFullCombatEffects &&
                    creations >= MaximumProjectileVisualCreationsPerSync)
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
            visual.LastPosition = projectile.Position;
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
            UpdateProjectileTransform(
                visual.Root,
                ToWorldAtGround(
                    projectile.Position,
                    MathF.Max(0.7f, 0.7f + arcHeight)),
                displacement,
                visual.HasPosition);
            visual.HasPosition = true;
        }

        foreach (var id in _abilityProjectiles.Keys
                     .Where(id => !_seenAbilityProjectiles.Contains(id))
                     .ToArray())
        {
            _abilityProjectiles[id].Root.Dispose();
            _abilityProjectiles.Remove(id);
        }
    }

    private UnitVisual CreateUnit(
        int id,
        War3UnitDefinition definition,
        int team,
        Camera3D camera)
    {
        var boundUnitTypeId = _simulation!.Abilities.UnitTypeId(id);
        var poolKey = new UnitPoolKey(
            definition.ObjectId, team, boundUnitTypeId);
        if (_unitPool.TryGetValue(poolKey, out var pooled) &&
            pooled.TryPop(out var reused))
        {
            RebindUnitVisual(reused, id, team);
            return reused;
        }
        var actor = _ridWorld!.CreateActor(
            definition.ModelSource, team, includeEffects: true);
        var selection = SelectionRingRid(0.075f,
            team == War3HumanScenario.PlayerId ? new Color("46d8ff") : new Color("ff5c58"));
        Action<War3ModelSoundTimelineEvent> soundHandler = value =>
            PublishUnitAnimationAudio(id, team, value);
        actor.SoundTimelineEvent += soundHandler;
        actor.SetShadowCastingEnabled(true);
        selection.Scale = Vector3.One * UnitSelectionWorldRadius(
            _simulation!.Units.NavigationRadii[id]);
        selection.Visible = _selectedUnits.Contains(id);
        var visual = new UnitVisual(
            actor, selection, definition,
            boundUnitTypeId, team)
        {
            SoundHandler = soundHandler
        };
        visual.SelectionVisible = selection.Visible;
        return visual;
    }

    private void RebindUnitVisual(UnitVisual visual, int id, int team)
    {
        if (visual.SoundHandler is not null)
            visual.Actor.SoundTimelineEvent -= visual.SoundHandler;
        visual.SoundHandler = value =>
            PublishUnitAnimationAudio(id, team, value);
        visual.Actor.SoundTimelineEvent += visual.SoundHandler;
        visual.Actor.Visible = true;
        visual.Actor.Processing = true;
        visual.Actor.ResetForReuse();
        visual.Selection.Scale = Vector3.One * UnitSelectionWorldRadius(
            _simulation!.Units.NavigationRadii[id]);
        visual.Selection.Visible = _selectedUnits.Contains(id);
        visual.LastWindup = 0f;
        visual.LastCooldown = 0f;
        visual.AnimationState = UnitAnimationState.None;
        visual.AnimationVariant = -1;
        visual.LastPosition = default;
        visual.Dying = false;
        visual.AbilityAnimationUntil = 0;
        visual.TreeHarvestResourceNode = -1;
        visual.LastTreeHarvestStrike = -1;
        visual.ActorVisible = true;
        visual.SelectionVisible = visual.Selection.Visible;
        visual.TransformInitialized = false;
        visual.LastActorPosition = default;
        visual.LastActorAngle = 0f;
    }

    private void PoolUnitVisual(UnitVisual visual)
    {
        visual.Selection.Visible = false;
        visual.SelectionVisible = false;
        visual.Actor.Processing = false;
        visual.Actor.Visible = false;
        visual.ActorVisible = false;
        var team = visual.PoolTeam;
        var key = new UnitPoolKey(
            visual.Definition.ObjectId, team, visual.BoundUnitTypeId);
        if (!_unitPool.TryGetValue(key, out var pooled))
        {
            pooled = [];
            _unitPool.Add(key, pooled);
        }
        pooled.Push(visual);
    }

    private BuildingVisual CreateBuilding(
        GameplayBuildingSnapshot building,
        War3BuildingDefinition definition,
        Camera3D camera)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var actor = _ridWorld!.CreateActor(
            definition.ModelSource,
            building.PlayerId,
            includeEffects: true);
        var selection = SelectionRingRid(0.034f,
            building.PlayerId == War3HumanScenario.PlayerId
                ? new Color("46d8ff")
                : new Color("ff5c58"));
        Action<War3ModelSoundTimelineEvent> soundHandler = value =>
            PublishBuildingAnimationAudio(
            building.Id.Value, building.PlayerId, value);
        actor.SoundTimelineEvent += soundHandler;
        var footprint = SimPlane3DTransform.ToWorldSize(
            building.Bounds.Max - building.Bounds.Min);
        var frustumRadius = MathF.Max(footprint.X, footprint.Y) * 0.7f;
        var center = (building.Bounds.Min + building.Bounds.Max) * 0.5f;
        var world = ToWorldAtGround(center);
        const bool modelLoaded = true;
        actor.SetShadowCastingEnabled(true);
        var profileKey = $"{building.PlayerId}:{definition.ModelSource}";
        if (_profiledBuildingActorSources.Add(profileKey))
            GD.Print(
                $"WAR3_BUILDING_ACTOR_FIRST_USE source={definition.ModelSource} " +
                $"player={building.PlayerId} elapsed_ms=" +
                $"{ElapsedMilliseconds(started, System.Diagnostics.Stopwatch.GetTimestamp()):0.###} " +
                "renderer=rid-vat");
        return new BuildingVisual(
            actor, selection, frustumRadius,
            definition, modelLoaded,
            building.Type.Id)
        {
            SoundHandler = soundHandler
        };
    }

    private void PrepareCameraFrustum(
        Camera3D camera,
        Transform3D cameraTransform)
    {
        _cameraFrustumPlaneCount = 0;
        foreach (var plane in camera.GetFrustum())
        {
            if (_cameraFrustumPlaneCount >= _cameraFrustumPlanes.Length)
                break;
            _cameraFrustumPlanes[_cameraFrustumPlaneCount++] = plane;
        }
        if (_cameraFrustumPlaneCount != _cameraFrustumPlanes.Length)
            return;

        // Godot's frustum planes may point toward either half-space. A point
        // one world unit down the camera's forward axis is inside this RTS
        // camera, so use it to normalize every plane to "positive is out".
        var insideProbe = cameraTransform.Origin -
                          cameraTransform.Basis.Z.Normalized();
        for (var index = 0; index < _cameraFrustumPlaneCount; index++)
        {
            _cameraFrustumInsideSigns[index] =
                _cameraFrustumPlanes[index].DistanceTo(insideProbe) <= 0f
                    ? 1f
                    : -1f;
        }
    }

    private bool IsPositionInPreparedFrustum(
        Camera3D camera,
        Vector3 position)
    {
        if (_cameraFrustumPlaneCount != _cameraFrustumPlanes.Length)
            return camera.IsPositionInFrustum(position);
        for (var index = 0; index < _cameraFrustumPlaneCount; index++)
        {
            if (_cameraFrustumPlanes[index].DistanceTo(position) *
                _cameraFrustumInsideSigns[index] > 0f)
            {
                return false;
            }
        }
        return true;
    }

    private bool BuildingIntersectsFrustum(
        Camera3D camera,
        BuildingVisual visual)
        => BuildingIntersectsFrustum(
            camera, visual.WorldPosition, visual.FrustumRadius);

    private bool BuildingIntersectsFrustum(
        Camera3D camera,
        Vector3 center,
        float radius)
    {
        if (IsPositionInPreparedFrustum(camera, center)) return true;
        return IsPositionInPreparedFrustum(
                   camera, center + Vector3.Right * radius) ||
               IsPositionInPreparedFrustum(
                   camera, center + Vector3.Left * radius) ||
               IsPositionInPreparedFrustum(
                   camera, center + Vector3.Forward * radius) ||
               IsPositionInPreparedFrustum(
                   camera, center + Vector3.Back * radius);
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

    private War3RidModelActor CreateResourceActor(
        int id,
        EconomyResourceKind kind,
        string source,
        Camera3D camera)
    {
        return _ridWorld!.CreateActor(
            source,
            playerId: 0,
            includeEffects: kind == EconomyResourceKind.Minerals);
    }

    private War3RidModelActor PromoteResourceActor(
        int id,
        ResourceVisual visual,
        NVector2 position,
        Camera3D camera)
    {
        if (visual.Actor is not null) return visual.Actor;
        visual.StaticBatch?.SetInstanceVisible(visual.StaticBatchIndex, false);
        var actor = CreateResourceActor(id, visual.Kind, visual.Source, camera);
        actor.Position = ToWorldAtGround(position);
        actor.Visible = visual.FogVisible;
        actor.PlayPreferred(true, "Stand");
        visual.Actor = actor;
        visual.Positioned = true;
        visual.AnimationInitialized = true;
        return actor;
    }

    private static void DemoteResourceActor(ResourceVisual visual)
    {
        if (visual.Actor is null || visual.StaticBatch is null) return;
        visual.Actor.Dispose();
        visual.Actor = null;
        visual.StaticBatch.SetInstanceVisible(
            visual.StaticBatchIndex,
            visual.FogVisible && !visual.Depleted);
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
            var batch = new War3StaticModelBatch(this);
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
        IWar3RidSpatial root;
        if (definition.ProjectileSource.Length > 0 &&
            War3RuntimeAssets.Contains(definition.ProjectileSource))
        {
            var actor = _ridWorld!.CreateActor(
                definition.ProjectileSource,
                playerId: sourcePlayerId,
                includeEffects: true);
            actor.SetShadowCastingEnabled(false);
            actor.SoundTimelineEvent += value => PublishProjectileAnimationAudio(
                id, sourcePlayerId, value);
            actor.PlayPreferred(true, "Stand", "Birth");
            root = actor;
        }
        else
        {
            root = _ridWorld!.CreateGeometry(
                new SphereMesh { Radius = 0.075f, Height = 0.15f },
                Emissive(new Color("ffd46a")),
                castShadows: false);
            root.Transform = new Transform3D(
                Basis.FromScale(Vector3.One * 1.2f), Vector3.Zero);
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
        IWar3RidSpatial root;
        if (definition.ProjectileSource.Length > 0 &&
            War3RuntimeAssets.Contains(definition.ProjectileSource))
        {
            var actor = _ridWorld!.CreateActor(
                definition.ProjectileSource,
                playerId: sourcePlayerId,
                includeEffects: true);
            actor.SetShadowCastingEnabled(false);
            actor.SoundTimelineEvent += value => PublishAnimationAudio(
                value,
                NVector2.Zero,
                ProjectileAudioEmitterBase + 100_000_000 + id,
                sourcePlayerId);
            actor.PlayPreferred(true, "Stand", "Birth");
            root = actor;
        }
        else
        {
            root = _ridWorld!.CreateGeometry(
                new SphereMesh { Radius = 0.075f, Height = 0.15f },
                Emissive(new Color("ffd46a")),
                castShadows: false);
            root.Transform = new Transform3D(
                Basis.FromScale(Vector3.One * 1.2f), Vector3.Zero);
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
        IWar3RidSpatial root;
        var source = definition.MissileModels
            .FirstOrDefault(War3RuntimeAssets.Contains);
        if (source is not null)
        {
            var actor = _ridWorld!.CreateActor(
                source,
                playerId: sourcePlayerId,
                includeEffects: true);
            actor.SetShadowCastingEnabled(false);
            actor.SoundTimelineEvent += value => PublishAnimationAudio(
                value,
                new NVector2(actor.Position.X, actor.Position.Z),
                ProjectileAudioEmitterBase + 200_000_000 + id,
                sourcePlayerId);
            actor.PlayPreferred(true, "Stand", "Birth");
            root = actor;
        }
        else
        {
            root = _ridWorld!.CreateGeometry(
                new SphereMesh { Radius = 0.075f, Height = 0.15f },
                Emissive(new Color("70dfff")),
                castShadows: false);
            root.Transform = new Transform3D(
                Basis.FromScale(Vector3.One * 1.2f), Vector3.Zero);
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
        var actor = _ridWorld!.CreateActor(
            source,
            playerId: sourcePlayerId,
            includeEffects: true);
        actor.SetShadowCastingEnabled(false);
        var emitterId = _nextTransientAudioEmitterId++;
        actor.Position = ToWorldAtGround(position) +
                         (attachmentOffset == default
                             ? new Vector3(0f, 0.18f, 0f)
                             : attachmentOffset);
        actor.SoundTimelineEvent += value => PublishAnimationAudio(
            value, position, emitterId, sourcePlayerId);
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
        var actor = _ridWorld!.CreateActor(
            War3CommandFeedbackCatalog.ConfirmationSource,
            War3HumanScenario.PlayerId,
            includeEffects: false);
        actor.Position = ToWorldAtGround(position, 0.08f);
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
        var ring = SelectionRingRid(
            0.075f,
            War3CommandFeedbackCatalog.ResourceTargetTint);
        var radius = SimPlane3DTransform.ToWorldLength(
            MathF.Max(snapshot.InteractionHalfExtents.X,
                snapshot.InteractionHalfExtents.Y) + 10f);
        ring.Position = ToWorldAtGround(snapshot.Position, SelectionHeight);
        ring.Scale = Vector3.One * MathF.Max(0.55f, radius);
        ring.Visible = true;
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
            _transients[index].Root.Dispose();
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
            _transients[index].Root.Dispose();
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

    private War3RidGeometryInstance SelectionRingRid(float width, Color color)
    {
        if (!_selectionRingMeshes.TryGetValue(width, out var mesh))
        {
            mesh = new TorusMesh
            {
                InnerRadius = MathF.Max(0.1f, 1f - width),
                OuterRadius = 1f,
                Rings = 32,
                RingSegments = 8
            };
            _selectionRingMeshes.Add(width, mesh);
        }
        if (!_selectionRingMaterials.TryGetValue(color, out var material))
        {
            material = Emissive(color);
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            material.NoDepthTest = false;
            _selectionRingMaterials.Add(color, material);
        }
        var ring = _ridWorld!.CreateGeometry(
            mesh,
            material,
            castShadows: false);
        ring.Visible = false;
        return ring;
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

    private enum UnitAnimationState : byte
    {
        None,
        Ability,
        AbilityLock,
        BuilderWalk,
        BuilderWork,
        GatherLumber,
        GatherGold,
        WorkerWalk,
        WorkerStand,
        Attack,
        AttackReady,
        Walk,
        CombatReady,
        Stand
    }

    private sealed class UnitVisual(
        War3RidModelActor actor,
        War3RidGeometryInstance selection,
        War3UnitDefinition definition,
        int boundUnitTypeId,
        int poolTeam)
    {
        public War3RidModelActor Actor { get; } = actor;
        public War3RidGeometryInstance Selection { get; } = selection;
        public War3UnitDefinition Definition { get; } = definition;
        public int BoundUnitTypeId { get; } = boundUnitTypeId;
        public int PoolTeam { get; } = poolTeam;
        public Action<War3ModelSoundTimelineEvent>? SoundHandler { get; set; }
        public float LastWindup { get; set; }
        public float LastCooldown { get; set; }
        public UnitAnimationState AnimationState { get; set; }
        public int AnimationVariant { get; set; } = -1;
        public NVector2 LastPosition { get; set; }
        public bool Dying { get; set; }
        public ulong AbilityAnimationUntil { get; set; }
        public int TreeHarvestResourceNode { get; set; } = -1;
        public int LastTreeHarvestStrike { get; set; } = -1;
        public bool ActorVisible { get; set; } = true;
        public bool SelectionVisible { get; set; }
        public bool TransformInitialized { get; set; }
        public Vector3 LastActorPosition { get; set; }
        public float LastActorAngle { get; set; }
    }

    private readonly record struct UnitPoolKey(
        string ObjectId,
        int Team,
        int BoundUnitTypeId);

    private sealed class BuildingVisual(
        War3RidModelActor actor,
        War3RidGeometryInstance selection,
        float frustumRadius,
        War3BuildingDefinition definition,
        bool modelLoaded,
        int typeId)
    {
        public War3RidModelActor Actor { get; set; } = actor;
        public War3RidGeometryInstance Selection { get; } = selection;
        public Action<War3ModelSoundTimelineEvent> SoundHandler { get; set; } = null!;
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
        public Vector3 WorldPosition { get; set; }
        public bool WasGhost { get; set; }
        public bool Dying { get; set; }
        public bool LayoutInitialized { get; set; }
        public bool GhostInitialized { get; set; }
        public bool IsGhost { get; set; }
        public BuildingCombatPhase LastCombatPhase { get; set; } =
            BuildingCombatPhase.Idle;
        public bool ActorVisible { get; set; } = true;

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
        War3RidModelActor? actor,
        EconomyResourceKind kind,
        string source,
        War3StaticModelBatch? staticBatch = null,
        int staticBatchIndex = -1)
    {
        public War3RidModelActor? Actor { get; set; } = actor;
        public EconomyResourceKind Kind { get; } = kind;
        public string Source { get; } = source;
        public War3StaticModelBatch? StaticBatch { get; } = staticBatch;
        public int StaticBatchIndex { get; } = staticBatchIndex;
        public bool Depleted { get; set; }
        public bool Positioned { get; set; }
        public bool Working { get; set; }
        public bool AnimationInitialized { get; set; }
        public ulong LastHitAt { get; set; }
        public bool FogVisible { get; set; } = true;
    }

    private sealed record ProjectileVisual(
        IWar3RidSpatial Root,
        War3UnitDefinition Definition,
        NVector2 LastPosition,
        int SourcePlayerId)
    {
        public NVector2 LastPosition { get; set; } = LastPosition;
        public bool HasPosition { get; set; }
    }

    private sealed record BuildingProjectileVisual(
        IWar3RidSpatial Root,
        War3BuildingDefinition Definition,
        NVector2 LastPosition,
        int SourcePlayerId)
    {
        public NVector2 LastPosition { get; set; } = LastPosition;
        public bool HasPosition { get; set; }
    }

    private sealed record AbilityProjectileVisual(
        IWar3RidSpatial Root,
        War3AbilityDefinition Definition,
        NVector2 LastPosition,
        int SourcePlayerId)
    {
        public NVector2 LastPosition { get; set; } = LastPosition;
        public bool HasPosition { get; set; }
    }

    private sealed record TransientVisual(
        IWar3RidSpatial Root,
        ulong RemoveAt,
        bool CommandConfirmation = false);
    private readonly record struct AbilityVisualInstance(
        string Model,
        string Attachment);
    private sealed record AbilityBuffPart(
        War3RidModelActor Actor,
        string Attachment);
    private sealed record AbilityBuffVisual(
        AbilityBuffPart[] Parts,
        int TargetUnit);
}
