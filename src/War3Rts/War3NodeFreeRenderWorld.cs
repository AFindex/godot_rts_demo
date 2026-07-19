using Godot;
using RtsDemo.Demos.War3;
using System.Security.Cryptography;
using System.Text;

namespace War3Rts;

/// <summary>
/// Owns every low-level Warcraft presentation resource used by the battlefield.
/// Gameplay entities never become Nodes: their visual state lives in small
/// managed handles and RenderingServer RIDs. Imported scene trees exist only
/// while a shared model asset is sampled.
/// </summary>
internal sealed class War3NodeFreeRenderWorld : IDisposable
{
    private readonly Node3D _host;
    private readonly Camera3D _camera;
    private readonly Rid _scenario;
    private readonly Dictionary<ModelAssetKey, War3NodeFreeModelAsset> _assets = [];
    private readonly HashSet<War3RidModelActor> _actors = [];
    private readonly HashSet<War3RidModelActor> _effectActors = [];
    private readonly HashSet<War3RidGeometryInstance> _geometry = [];
    private ulong _lastAdvanceMicroseconds;
    private bool _disposed;

    public War3NodeFreeRenderWorld(Node3D host, Camera3D camera)
    {
        _host = host;
        _camera = camera;
        _scenario = host.GetWorld3D().Scenario;
        _lastAdvanceMicroseconds = Time.GetTicksUsec();
    }

    public int ActorCount => _actors.Count;
    public int EffectActorCount => _effectActors.Count;
    public int GeometryCount => _geometry.Count;
    public int AssetCount => _assets.Count;
    public int ImportedProbeNodeCount => 0;
    public bool ProfilingEnabled { get; set; }
    public double LastEffectsMilliseconds { get; private set; }
    public long LastEffectsAllocatedBytes { get; private set; }
    public double LastCommitMilliseconds { get; private set; }
    public long LastCommitAllocatedBytes { get; private set; }
    public int LastBatchBufferUploads { get; private set; }
    public long LastBatchUploadedBytes { get; private set; }

    public War3RidModelActor CreateActor(
        string source,
        int playerId,
        bool includeEffects = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var asset = Asset(source, playerId);
        var actor = new War3RidModelActor(
            this, asset, _camera, _scenario, includeEffects);
        _actors.Add(actor);
        if (actor.HasEffects) _effectActors.Add(actor);
        return actor;
    }

    public War3RidGeometryInstance CreateGeometry(
        Mesh mesh,
        Material? material = null,
        bool castShadows = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var instance = new War3RidGeometryInstance(
            this,
            _scenario,
            mesh,
            material,
            castShadows
                ? RenderingServer.ShadowCastingSetting.On
                : RenderingServer.ShadowCastingSetting.Off);
        _geometry.Add(instance);
        return instance;
    }

    public War3RidGeometryInstance CreateGeometry(
        Mesh mesh,
        Material? material,
        RenderingServer.ShadowCastingSetting shadowCasting)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var instance = new War3RidGeometryInstance(
            this, _scenario, mesh, material, shadowCasting);
        _geometry.Add(instance);
        return instance;
    }

    public void Advance()
    {
        if (_disposed) return;
        var now = Time.GetTicksUsec();
        var elapsed = _lastAdvanceMicroseconds == 0 || now <= _lastAdvanceMicroseconds
            ? 0d
            : Math.Min(0.25d, (now - _lastAdvanceMicroseconds) / 1_000_000d);
        _lastAdvanceMicroseconds = now;
        foreach (var actor in _actors) actor.AdvanceTimeline(elapsed);
    }

    public void Flush()
    {
        if (_disposed) return;
        var effectsStart = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var effectsAllocationStart = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        foreach (var actor in _effectActors) actor.AdvanceEffects();
        var effectsEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        var effectsAllocationEnd = ProfilingEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        foreach (var actor in _actors) actor.FlushPresentation();
        LastBatchBufferUploads = 0;
        LastBatchUploadedBytes = 0;
        foreach (var asset in _assets.Values)
        {
            asset.FlushPresentation();
            LastBatchBufferUploads += asset.LastBatchBufferUploads;
            LastBatchUploadedBytes += asset.LastBatchUploadedBytes;
        }
        if (!ProfilingEnabled) return;
        var commitEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        var commitAllocationEnd = GC.GetAllocatedBytesForCurrentThread();
        LastEffectsMilliseconds = ElapsedMilliseconds(
            effectsStart, effectsEnd);
        LastEffectsAllocatedBytes =
            effectsAllocationEnd - effectsAllocationStart;
        LastCommitMilliseconds = ElapsedMilliseconds(
            effectsEnd, commitEnd);
        LastCommitAllocatedBytes =
            commitAllocationEnd - effectsAllocationEnd;
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d /
        System.Diagnostics.Stopwatch.Frequency;

    internal void Retire(War3RidModelActor actor)
    {
        _actors.Remove(actor);
        _effectActors.Remove(actor);
    }
    internal void Retire(War3RidGeometryInstance geometry) =>
        _geometry.Remove(geometry);

    private War3NodeFreeModelAsset Asset(string source, int playerId)
    {
        var key = new ModelAssetKey(
            source.Replace('/', '\\').TrimStart('\\').ToLowerInvariant(),
            TeamColorIndex(playerId));
        if (_assets.TryGetValue(key, out var asset)) return asset;
        var sharedPoseSource = _assets
            .Where(pair => pair.Key.Source.Equals(
                key.Source, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .FirstOrDefault();
        asset = War3NodeFreeModelAsset.Build(
            _host, key.Source, key.TeamColor, sharedPoseSource);
        _assets.Add(key, asset);
        return asset;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var actor in _actors.ToArray()) actor.Dispose();
        _actors.Clear();
        _effectActors.Clear();
        foreach (var geometry in _geometry.ToArray()) geometry.Dispose();
        _geometry.Clear();
        foreach (var asset in _assets.Values) asset.Dispose();
        _assets.Clear();
    }

    private static int TeamColorIndex(int playerId) => playerId switch
    {
        War3HumanScenario.PlayerId => 0,
        War3HumanScenario.EnemyId => 1,
        _ => 12
    };

    private readonly record struct ModelAssetKey(string Source, int TeamColor);
}

/// <summary>
/// Node-free instance of a Warcraft model. Animation sequencing intentionally
/// mirrors War3ModelActor, while pose application is delegated to the shared
/// sampled asset and RenderingServer.
/// </summary>
internal interface IWar3RidSpatial : IDisposable
{
    Transform3D Transform { get; set; }
    Vector3 Position { get; set; }
    bool Visible { get; set; }
}

internal enum War3DeathPresentationPhase : byte
{
    None,
    Death,
    DecayFlesh,
    DecayBone,
    Complete
}

internal sealed class War3RidModelActor : IWar3RidSpatial
{
    private const double DecayFleshPresentationMilliseconds = 2_000d;
    private const double DecayBonePresentationMilliseconds = 5_000d;
    private readonly War3NodeFreeRenderWorld _owner;
    private readonly War3NodeFreeModelAsset _asset;
    private readonly War3VatModelBatch _batch;
    private readonly int _batchSlot;
    private readonly bool _includeEffects;
    private readonly War3EffectRuntimeCore? _effects;
    private int _sequenceIndex = -1;
    private bool _requestedLoop = true;
    private bool _requestedRepeat;
    private bool _progressDriven;
    private bool _animationPlaying;
    private bool _deathLocked;
    private bool _visible = true;
    private bool _processing = true;
    private bool _shadowCasting = true;
    private bool _ghostAppearance;
    private bool _ghostValid = true;
    private bool _effectsHiddenByGhost;
    private double _sequenceMilliseconds;
    private double _drivenMilliseconds;
    private double _lastSoundTimelineMilliseconds = -0.001d;
    private int _appliedPose = -1;
    private War3NodeFreePose? _committedPose;
    private War3NodeFreePose? _blendSourcePose;
    private double _blendElapsedMilliseconds;
    private double _blendDurationMilliseconds;
    private double _deathPhaseDurationMilliseconds;
    private War3DeathPresentationPhase _deathPhase;
    private Transform3D _transform = Transform3D.Identity;
    private Transform3D _effectWorldTransform = Transform3D.Identity;
    private ulong _soundTimelineSequence;
    private bool _disposed;
    private bool _transformDirty = true;
    private bool _poseDirty = true;
    private bool _visibilityDirty = true;
    private bool _materialDirty = true;
    private Color _surfaceTint = Colors.White;
    private bool _hasExplicitEffectWorldTransform;

    internal War3RidModelActor(
        War3NodeFreeRenderWorld owner,
        War3NodeFreeModelAsset asset,
        Camera3D camera,
        Rid scenario,
        bool includeEffects)
    {
        _owner = owner;
        _asset = asset;
        _includeEffects = includeEffects;
        _batch = asset.Batch(scenario);
        _batchSlot = _batch.AcquireSlot();
        if (includeEffects &&
            (asset.Metadata.Particles.Count > 0 ||
             asset.Metadata.Ribbons.Count > 0))
        {
            _effects = new War3EffectRuntimeCore();
            _effects.InitializeNodeFree(
                camera,
                asset.Metadata,
                scenario,
                ResolveEmitterTransform);
            _effects.SetTeamColor(asset.TeamColor);
            _effects.PrepareWorldTransform(_effectWorldTransform);
        }
        PlayPreferred(true, "Stand", "Birth");
        ApplyPose(force: true);
    }

    public event Action<War3ModelSoundTimelineEvent>? SoundTimelineEvent;

    public string Source => _asset.Source;
    public War3ModelMetadata Metadata => _asset.Metadata;
    public bool IsAnimationPlaying => _animationPlaying;
    public bool IsProgressDriven => _progressDriven;
    public float DrivenProgress { get; private set; }
    public double LastTransitionBlendSeconds { get; private set; }
    public int RepeatedSequenceRestartCount { get; private set; }
    public int BlendedPoseCommitCount { get; private set; }
    public int RenderMeshCount => _asset.Parts.Count;
    public int RenderSurfaceCount => _asset.SurfaceCount;
    public int ShadowSurfaceCount => _shadowCasting ? _asset.SurfaceCount : 0;
    public int LiveEffectCount => (_effects?.LiveParticleCount ?? 0) +
                                  (_effects?.LiveRibbonPointCount ?? 0);
    public bool HasEffects => _effects is not null;
    public War3DeathPresentationPhase DeathPhase => _deathPhase;
    public bool DeathPresentationComplete =>
        _deathLocked && _deathPhase == War3DeathPresentationPhase.Complete;
    public string CurrentSequence => _sequenceIndex >= 0 &&
                                     _sequenceIndex < _asset.Metadata.Sequences.Count
        ? _asset.Metadata.Sequences[_sequenceIndex].Name
        : string.Empty;
    public double CurrentSequenceDurationSeconds =>
        _sequenceIndex >= 0 && _sequenceIndex < _asset.Sequences.Count
            ? _asset.Sequences[_sequenceIndex].DurationMilliseconds / 1000d
            : 0d;

    public Transform3D Transform
    {
        get => _transform;
        set
        {
            if (_transform == value) return;
            _transform = value;
            _transformDirty = true;
            if (!_hasExplicitEffectWorldTransform)
            {
                _effectWorldTransform = value;
                _effects?.PrepareWorldTransform(value);
            }
        }
    }

    public Vector3 Position
    {
        get => _transform.Origin;
        set => Transform = new Transform3D(_transform.Basis, value);
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            _effects?.SetNodeFreeVisible(value && !_effectsHiddenByGhost);
            if (value) _transformDirty = true;
            ApplyVisibility();
        }
    }

    public bool Processing
    {
        get => _processing;
        set
        {
            if (_processing == value) return;
            _processing = value;
            if (value) ApplyPose(force: true);
        }
    }

    public float ApproximateWorldHeight() => _asset.ApproximateHeight;

    public void PrepareEffectWorldTransform(Transform3D transform)
    {
        _hasExplicitEffectWorldTransform = true;
        _effectWorldTransform = transform;
        _effects?.PrepareWorldTransform(transform);
    }

    public void SetShadowCastingEnabled(bool enabled)
    {
        if (_shadowCasting == enabled) return;
        _shadowCasting = enabled;
        _materialDirty = true;
    }

    public bool PlayPreferred(bool loop, params string[] candidates) =>
        RequestSequence(loop, repeatNonLooping: false, candidates);

    public bool PlayPreferred(bool loop, string first) =>
        RequestSequence(loop, false, _asset.FindSequence(first));

    public bool PlayPreferred(bool loop, string first, string second) =>
        RequestSequence(
            loop, false, _asset.FindSequence(first, second));

    public bool PlayPreferred(
        bool loop, string first, string second, string third) =>
        RequestSequence(
            loop, false, _asset.FindSequence(first, second, third));

    public bool PlayPreferred(
        bool loop, string first, string second, string third, string fourth) =>
        RequestSequence(
            loop, false,
            _asset.FindSequence(first, second, third, fourth));

    public bool PlayRepeatedPreferred(params string[] candidates) =>
        RequestSequence(loop: true, repeatNonLooping: true, candidates);

    public bool PlayRepeatedPreferred(string first) =>
        RequestSequence(true, true, _asset.FindSequence(first));

    public bool PlayRepeatedPreferred(string first, string second) =>
        RequestSequence(
            true, true, _asset.FindSequence(first, second));

    public bool PlayRepeatedPreferred(
        string first, string second, string third) =>
        RequestSequence(
            true, true, _asset.FindSequence(first, second, third));

    public bool PlayRepeatedPreferred(
        string first, string second, string third, string fourth) =>
        RequestSequence(
            true, true,
            _asset.FindSequence(first, second, third, fourth));

    public bool ReplayPreferred(string first) =>
        ReplaySequence(_asset.FindSequence(first));

    public bool ReplayPreferred(string first, string second) =>
        ReplaySequence(_asset.FindSequence(first, second));

    public bool ReplayPreferred(
        string first, string second, string third) =>
        ReplaySequence(_asset.FindSequence(first, second, third));

    public bool ReplayPreferred(params string[] candidates)
    {
        return ReplaySequence(_asset.FindSequence(candidates));
    }

    private bool ReplaySequence(int index)
    {
        if (_deathLocked || index < 0) return false;
        _requestedLoop = false;
        _requestedRepeat = false;
        StartSequence(index, loop: false);
        return true;
    }

    public bool SetSequenceProgress(
        float normalizedProgress,
        string candidate) =>
        SetSequenceProgress(
            normalizedProgress, _asset.FindSequence(candidate));

    public bool SetSequenceProgress(
        float normalizedProgress,
        params string[] candidates)
        => SetSequenceProgress(
            normalizedProgress, _asset.FindSequence(candidates));

    private bool SetSequenceProgress(
        float normalizedProgress,
        int index)
    {
        if (_deathLocked) return false;
        if (index < 0) return false;
        var progress = Math.Clamp(normalizedProgress, 0f, 1f);
        var sequence = _asset.Sequences[index];
        var drivenMilliseconds = sequence.DurationMilliseconds * progress;
        if (_progressDriven && _sequenceIndex == index &&
            Math.Abs(_drivenMilliseconds - drivenMilliseconds) < 0.001d)
        {
            DrivenProgress = progress;
            return true;
        }
        if (!_progressDriven || _sequenceIndex != index)
            StartSequence(index, loop: false, allowBlend: false);
        _requestedLoop = false;
        _requestedRepeat = false;
        _progressDriven = true;
        _animationPlaying = false;
        DrivenProgress = progress;
        _drivenMilliseconds = drivenMilliseconds;
        _sequenceMilliseconds = _drivenMilliseconds;
        _lastSoundTimelineMilliseconds = _drivenMilliseconds;
        ApplyPose(force: true);
        return true;
    }

    public bool PlayDeath()
    {
        _deathLocked = false;
        _deathLocked = true;
        _deathPhase = War3DeathPresentationPhase.None;
        var index = _asset.FindSequence("Death", "Dissipate");
        if (index >= 0)
        {
            BeginDeathPhase(
                War3DeathPresentationPhase.Death,
                index,
                _asset.Sequences[index].DurationMilliseconds);
            return true;
        }
        if (TryBeginDecayFlesh() || TryBeginDecayBone()) return true;
        _deathPhase = War3DeathPresentationPhase.Complete;
        _animationPlaying = false;
        return true;
    }

    public void Revive()
    {
        _deathLocked = false;
        _deathPhase = War3DeathPresentationPhase.None;
        _deathPhaseDurationMilliseconds = 0d;
        ReplayPreferred("Birth", "Stand");
    }

    public void ResetForReuse()
    {
        _deathLocked = false;
        _deathPhase = War3DeathPresentationPhase.None;
        _deathPhaseDurationMilliseconds = 0d;
        CancelBlend();
        ReplayPreferred("Stand", "Birth");
    }

    public void SetGhostAppearance(bool enabled, bool valid = true)
    {
        _effectsHiddenByGhost = enabled;
        _effects?.SetNodeFreeVisible(_visible && !enabled);
        if (_ghostAppearance == enabled && _ghostValid == valid) return;
        _ghostAppearance = enabled;
        _ghostValid = valid;
        _materialDirty = true;
        _visibilityDirty = true;
    }

    public void SetSurfaceTint(Color tint)
    {
        if (_surfaceTint == tint) return;
        _surfaceTint = tint;
        _materialDirty = true;
    }

    internal void AdvanceTimeline(double deltaSeconds)
    {
        if (_disposed) return;
        if (deltaSeconds <= 0d) return;
        AdvanceBlend(deltaSeconds * 1000d);
        if (_progressDriven || !_animationPlaying) return;
        var sequence = _asset.Sequences[_sequenceIndex];
        var duration = PlaybackDurationMilliseconds(sequence);
        var previous = _sequenceMilliseconds;
        _sequenceMilliseconds += deltaSeconds * 1000d;
        if (_requestedLoop)
        {
            if (_sequenceMilliseconds >= duration)
                _sequenceMilliseconds %= duration;
        }
        else if (_sequenceMilliseconds >= duration)
        {
            _sequenceMilliseconds = duration;
            _animationPlaying = false;
        }
        DispatchSoundTimeline(
            ToAssetSequenceMilliseconds(previous, sequence, duration),
            ToAssetSequenceMilliseconds(
                _sequenceMilliseconds, sequence, duration),
            sequence.DurationMilliseconds);
        if (_processing) ApplyPose(force: false);
        if (_deathLocked && !_animationPlaying)
        {
            AdvanceDeathPresentation();
        }
        else if (!_deathLocked && !_animationPlaying &&
                 (_requestedLoop || _requestedRepeat))
        {
            if (_requestedRepeat) RepeatedSequenceRestartCount++;
            StartSequence(_sequenceIndex, _requestedLoop);
        }
    }

    internal void AdvanceEffects()
    {
        if (_disposed || !_processing) return;
        SyncEffects(CurrentAssetSequenceMilliseconds());
    }

    private bool RequestSequence(
        bool loop,
        bool repeatNonLooping,
        IReadOnlyList<string> candidates)
        => RequestSequence(
            loop, repeatNonLooping, _asset.FindSequence(candidates));

    private bool RequestSequence(
        bool loop,
        bool repeatNonLooping,
        int index)
    {
        if (_deathLocked) return false;
        if (index < 0) return false;
        var nonLooping = _asset.Metadata.Sequences[index].NonLooping;
        var effectiveLoop = loop && !nonLooping;
        var repeatAfterFinish = loop && repeatNonLooping && nonLooping;
        var sameRequest = _requestedLoop == effectiveLoop &&
                          _requestedRepeat == repeatAfterFinish &&
                          !_progressDriven && _sequenceIndex == index;
        _requestedLoop = effectiveLoop;
        _requestedRepeat = repeatAfterFinish;
        _progressDriven = false;
        if (sameRequest)
        {
            if (_animationPlaying ||
                (!effectiveLoop && !repeatAfterFinish))
                return true;
            if (repeatAfterFinish) RepeatedSequenceRestartCount++;
        }
        StartSequence(index, effectiveLoop);
        return true;
    }

    private void StartSequence(
        int index,
        bool loop,
        bool allowBlend = true)
    {
        var previous = _sequenceIndex;
        var previousPose = CurrentPose();
        _sequenceIndex = index;
        _progressDriven = false;
        _drivenMilliseconds = 0d;
        _sequenceMilliseconds = 0d;
        _lastSoundTimelineMilliseconds = -0.001d;
        DrivenProgress = 0f;
        _animationPlaying = _asset.Sequences[index].PoseCount > 0;
        LastTransitionBlendSeconds = allowBlend && previous >= 0 &&
                                     previous != index
            ? Math.Clamp(
                _asset.Metadata.BlendTimeMilliseconds / 1000d, 0d, 0.5d)
            : 0d;
        if (LastTransitionBlendSeconds > 0d && previousPose is not null)
        {
            _blendSourcePose = previousPose;
            _blendElapsedMilliseconds = 0d;
            _blendDurationMilliseconds =
                LastTransitionBlendSeconds * 1000d;
        }
        else
        {
            CancelBlend();
        }
        _requestedLoop = loop;
        _effects?.SetSequence(index);
        _appliedPose = -1;
        ApplyPose(force: true);
    }

    private void ApplyPose(bool force)
    {
        if (_disposed || !_visible || _sequenceIndex < 0 ||
            _sequenceIndex >= _asset.Sequences.Count)
            return;
        var sequence = _asset.Sequences[_sequenceIndex];
        if (sequence.PoseCount == 0) return;
        var milliseconds = CurrentAssetSequenceMilliseconds();
        var poseIndex = sequence.PoseIndex(milliseconds);
        if (!force && poseIndex == _appliedPose && !BlendActive) return;
        _appliedPose = poseIndex;
        _poseDirty = true;
    }

    internal void FlushPresentation()
    {
        if (_disposed) return;
        if (!_visible)
        {
            if (_visibilityDirty) _batch.HideSlot(_batchSlot);
            _visibilityDirty = false;
            _transformDirty = false;
            _poseDirty = false;
            _materialDirty = false;
            return;
        }
        if (_sequenceIndex < 0 ||
            _sequenceIndex >= _asset.Sequences.Count)
            return;
        var sequence = _asset.Sequences[_sequenceIndex];
        if ((uint)_appliedPose >= (uint)sequence.PoseCount)
            return;
        var pose = sequence.Poses[_appliedPose];
        var poseChanged = !ReferenceEquals(_committedPose, pose);
        if (!_transformDirty && !_visibilityDirty && !_materialDirty &&
            !_poseDirty && !poseChanged && !BlendActive)
            return;
        if (BlendActive) BlendedPoseCommitCount++;
        var targetPoseIndex = _asset.VatAnimation.PoseIndex(pose);
        var sourcePose = BlendActive && _blendSourcePose is not null
            ? _blendSourcePose
            : pose;
        var sourcePoseIndex = _asset.VatAnimation.PoseIndex(sourcePose);
        var blendWeight = BlendActive ? BlendWeight : 1f;
        var appearance = _ghostAppearance
            ? War3VatAppearance.Ghost
            : War3VatAppearance.Normal;
        var tint = _ghostAppearance
            ? (_ghostValid
                ? new Color("55e6b06e")
                : new Color("ef666676"))
            : _surfaceTint;
        _batch.BeginSlot(
            _batchSlot,
            new War3VatBatchLaneKey(appearance, _shadowCasting));
        _batch.WriteActor(
            _batchSlot,
            _transform,
            true,
            targetPoseIndex,
            sourcePoseIndex,
            blendWeight,
            tint);
        _committedPose = pose;
        _transformDirty = false;
        _visibilityDirty = false;
        _poseDirty = false;
        _materialDirty = false;
    }

    private Transform3D ResolveEmitterTransform(
        int objectId,
        string name,
        double milliseconds)
    {
        var target = _asset.ResolveEmitterLocalTransform(
            _sequenceIndex,
            milliseconds,
            objectId,
            name);
        if (BlendActive && _blendSourcePose is not null)
        {
            var source = _asset.ResolveEmitterLocalTransform(
                _blendSourcePose, objectId, name);
            target = source.InterpolateWith(target, BlendWeight);
        }
        return _effectWorldTransform * target;
    }

    private void SyncEffects(double milliseconds)
    {
        if (_effects is null) return;
        _effects.PrepareWorldTransform(_effectWorldTransform);
        _effects.Sync(milliseconds);
    }

    private void ApplyVisibility()
    {
        _visibilityDirty = true;
        if (_visible) ApplyPose(force: true);
    }

    private bool BlendActive =>
        _blendSourcePose is not null &&
        _blendDurationMilliseconds > 0d &&
        _blendElapsedMilliseconds < _blendDurationMilliseconds;

    private float BlendWeight => (float)Math.Clamp(
        _blendElapsedMilliseconds / Math.Max(1d, _blendDurationMilliseconds),
        0d,
        1d);

    private void AdvanceBlend(double deltaMilliseconds)
    {
        if (!BlendActive) return;
        _blendElapsedMilliseconds = Math.Min(
            _blendDurationMilliseconds,
            _blendElapsedMilliseconds + deltaMilliseconds);
        _poseDirty = true;
        if (_blendElapsedMilliseconds >= _blendDurationMilliseconds)
        {
            _blendSourcePose = null;
            _blendDurationMilliseconds = 0d;
        }
    }

    private void CancelBlend()
    {
        _blendSourcePose = null;
        _blendElapsedMilliseconds = 0d;
        _blendDurationMilliseconds = 0d;
    }

    private War3NodeFreePose? CurrentPose()
    {
        if ((uint)_sequenceIndex >= (uint)_asset.Sequences.Count)
            return null;
        var sequence = _asset.Sequences[_sequenceIndex];
        if (sequence.PoseCount == 0) return null;
        return sequence.Poses[sequence.PoseIndex(
            CurrentAssetSequenceMilliseconds())];
    }

    private double CurrentAssetSequenceMilliseconds()
    {
        if ((uint)_sequenceIndex >= (uint)_asset.Sequences.Count) return 0d;
        var sequence = _asset.Sequences[_sequenceIndex];
        var playback = _progressDriven
            ? _drivenMilliseconds
            : _sequenceMilliseconds;
        return ToAssetSequenceMilliseconds(
            playback, sequence, PlaybackDurationMilliseconds(sequence));
    }

    private double PlaybackDurationMilliseconds(
        War3NodeFreeSequence sequence) =>
        _deathLocked &&
        _deathPhase is not War3DeathPresentationPhase.None and
            not War3DeathPresentationPhase.Complete &&
        _deathPhaseDurationMilliseconds > 0d
            ? _deathPhaseDurationMilliseconds
            : Math.Max(1d, sequence.DurationMilliseconds);

    private static double ToAssetSequenceMilliseconds(
        double playbackMilliseconds,
        War3NodeFreeSequence sequence,
        double playbackDuration) =>
        sequence.DurationMilliseconds * Math.Clamp(
            playbackMilliseconds / Math.Max(1d, playbackDuration), 0d, 1d);

    private void BeginDeathPhase(
        War3DeathPresentationPhase phase,
        int sequenceIndex,
        double presentationMilliseconds)
    {
        _deathPhase = phase;
        _deathPhaseDurationMilliseconds = Math.Max(
            1d, presentationMilliseconds);
        StartSequence(sequenceIndex, loop: false, allowBlend: false);
        _requestedLoop = false;
        _requestedRepeat = false;
    }

    private void AdvanceDeathPresentation()
    {
        switch (_deathPhase)
        {
            case War3DeathPresentationPhase.Death:
                if (TryBeginDecayFlesh() || TryBeginDecayBone()) return;
                break;
            case War3DeathPresentationPhase.DecayFlesh:
                if (TryBeginDecayBone()) return;
                break;
            case War3DeathPresentationPhase.DecayBone:
                break;
            default:
                return;
        }
        _deathPhase = War3DeathPresentationPhase.Complete;
        _deathPhaseDurationMilliseconds = 0d;
        _animationPlaying = false;
    }

    private bool TryBeginDecayFlesh()
    {
        var index = _asset.FindSequence("Decay Flesh");
        if (index < 0)
        {
            index = _asset.FindSequence("Decay");
            if (index >= 0 &&
                _asset.Metadata.Sequences[index].Name.StartsWith(
                    "Decay Bone", StringComparison.OrdinalIgnoreCase))
                index = -1;
        }
        if (index < 0 || index == _sequenceIndex) return false;
        BeginDeathPhase(
            War3DeathPresentationPhase.DecayFlesh,
            index,
            DecayFleshPresentationMilliseconds);
        return true;
    }

    private bool TryBeginDecayBone()
    {
        var index = _asset.FindSequence("Decay Bone");
        if (index < 0 || index == _sequenceIndex) return false;
        BeginDeathPhase(
            War3DeathPresentationPhase.DecayBone,
            index,
            DecayBonePresentationMilliseconds);
        return true;
    }

    private void DispatchSoundTimeline(
        double previous,
        double current,
        double duration)
    {
        if (!_visible || _sequenceIndex < 0 ||
            _asset.Metadata.EventObjects.Count == 0)
            return;
        var sequence = _asset.Metadata.Sequences[_sequenceIndex];
        if (current + 0.5d < previous && _requestedLoop)
        {
            DispatchSoundTimelineRange(sequence, previous, duration);
            DispatchSoundTimelineRange(sequence, -0.001d, current);
        }
        else if (current > previous)
        {
            DispatchSoundTimelineRange(sequence, previous, current);
        }
        _lastSoundTimelineMilliseconds = current;
    }

    private void DispatchSoundTimelineRange(
        War3Sequence sequence,
        double previous,
        double current)
    {
        foreach (var value in _asset.Metadata.EventObjects)
        {
            if (!value.TryGetSoundEventCode(out var eventCode)) continue;
            foreach (var frame in value.EventTrack)
            {
                var localMilliseconds = frame - sequence.StartFrame;
                if (localMilliseconds < 0d ||
                    localMilliseconds > sequence.DurationMilliseconds ||
                    localMilliseconds <= previous ||
                    localMilliseconds > current + 0.001d)
                    continue;
                SoundTimelineEvent?.Invoke(new War3ModelSoundTimelineEvent(
                    eventCode, sequence.Name, ++_soundTimelineSequence));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Retire(this);
        if (_effects is not null)
        {
            _effects.Dispose();
        }
        _batch.ReleaseSlot(_batchSlot);
    }
}

/// <summary>A direct RenderingServer mesh instance with no SceneTree node.</summary>
internal sealed class War3RidGeometryInstance : IWar3RidSpatial
{
    private readonly War3NodeFreeRenderWorld _owner;
    private Rid _instance;
    private bool _visible = true;
    private Transform3D _transform = Transform3D.Identity;

    public War3RidGeometryInstance(
        War3NodeFreeRenderWorld owner,
        Rid scenario,
        Mesh mesh,
        Material? material,
        RenderingServer.ShadowCastingSetting shadowCasting)
    {
        _owner = owner;
        Mesh = mesh;
        Material = material;
        _instance = RenderingServer.InstanceCreate2(mesh.GetRid(), scenario);
        if (material is not null)
            RenderingServer.InstanceSetSurfaceOverrideMaterial(
                _instance, 0, material.GetRid());
        RenderingServer.InstanceGeometrySetCastShadowsSetting(
            _instance, shadowCasting);
    }

    public Mesh Mesh { get; }
    public Material? Material { get; set; }
    public Transform3D Transform
    {
        get => _transform;
        set
        {
            _transform = value;
            RenderingServer.InstanceSetTransform(_instance, value);
        }
    }
    public Vector3 Position
    {
        get => _transform.Origin;
        set => Transform = new Transform3D(_transform.Basis, value);
    }
    public Vector3 Scale
    {
        get => _transform.Basis.Scale;
        set
        {
            var rotation = _transform.Basis.GetRotationQuaternion();
            Transform = new Transform3D(new Basis(rotation).Scaled(value),
                _transform.Origin);
        }
    }
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            RenderingServer.InstanceSetVisible(_instance, value);
        }
    }

    public void SetMaterial(Material? material)
    {
        Material = material;
        RenderingServer.InstanceSetSurfaceOverrideMaterial(
            _instance, 0, material?.GetRid() ?? default);
    }

    public void Dispose()
    {
        if (!_instance.IsValid) return;
        _owner.Retire(this);
        RenderingServer.FreeRid(_instance);
        _instance = default;
    }
}

internal sealed class War3NodeFreeModelAsset : IDisposable
{
    private const int CacheMagic = 0x464E3357; // W3NF
    private const int CacheVersion = 4;
    private const int SamplesPerSecond = 60;
    private const int BlendSteps = 8;
    private readonly List<Rid> _ownedSkeletons = [];
    private readonly Dictionary<War3NodeFreeBlendKey, Rid>
        _blendedSkeletons = [];
    private readonly Dictionary<(int Part, int Surface, Color Tint), Material?>
        _tintedMaterials = [];
    private readonly Dictionary<int, int> _emitterByObjectId = [];
    private readonly Dictionary<string, int> _emitterByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly War3NodeFreeModelAsset? _sharedBlendSource;
    private StandardMaterial3D? _validGhostMaterial;
    private StandardMaterial3D? _invalidGhostMaterial;
    private War3VatAnimationData? _vatAnimation;
    private War3VatModelBatch? _vatBatch;
    private bool _disposed;

    private War3NodeFreeModelAsset(
        string source,
        War3ModelMetadata metadata,
        List<War3NodeFreeMeshPart> parts,
        List<War3NodeFreeSequence> sequences,
        War3NodeFreeEmitterKey[] emitterKeys,
        int teamColor,
        float approximateHeight,
        War3NodeFreeModelAsset? sharedBlendSource)
    {
        Source = source;
        Metadata = metadata;
        Parts = parts;
        Sequences = sequences;
        EmitterKeys = emitterKeys;
        TeamColor = teamColor;
        _sharedBlendSource = sharedBlendSource;
        for (var index = 0; index < emitterKeys.Length; index++)
        {
            _emitterByObjectId.TryAdd(emitterKeys[index].ObjectId, index);
            _emitterByName.TryAdd(emitterKeys[index].Name, index);
        }
        ApproximateHeight = approximateHeight;
        SurfaceCount = parts.Sum(value => value.Mesh.GetSurfaceCount());
        SkeletonSlotCount = parts.Count == 0
            ? 0
            : parts.Max(value => value.SkeletonSlot) + 1;
    }

    public string Source { get; }
    public War3ModelMetadata Metadata { get; }
    public IReadOnlyList<War3NodeFreeMeshPart> Parts { get; }
    public IReadOnlyList<War3NodeFreeSequence> Sequences { get; }
    public float ApproximateHeight { get; }
    public int SurfaceCount { get; }
    public int SkeletonSlotCount { get; }
    public int TeamColor { get; }
    public IReadOnlyList<War3NodeFreeEmitterKey> EmitterKeys { get; }
    public War3VatAnimationData VatAnimation =>
        _vatAnimation ?? throw new InvalidOperationException(
            $"VAT animation data is not initialized: {Source}");

    public War3VatModelBatch Batch(Rid scenario)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _vatBatch ??= new War3VatModelBatch(
            this, VatAnimation, scenario);
    }

    public void FlushPresentation() => _vatBatch?.Flush();
    public int LastBatchBufferUploads => _vatBatch?.BufferUploads ?? 0;
    public long LastBatchUploadedBytes => _vatBatch?.UploadedBytes ?? 0L;

    public static War3NodeFreeModelAsset Build(
        Node3D host,
        string source,
        int teamColor,
        War3NodeFreeModelAsset? sharedPoseSource = null)
    {
        var metadata = War3RuntimeAssets.LoadMetadata(source);
        var probeRoot = new Node3D
        {
            Name = $"NodeFreeBake_{Path.GetFileNameWithoutExtension(source)}",
            ProcessMode = Node.ProcessModeEnum.Disabled,
            Visible = false
        };
        var model = War3RuntimeAssets.InstantiateModel(source, teamColor);
        model.Name = "Model";
        if (model is Node3D model3D)
            model3D.Rotation = new Vector3(0f, -Mathf.Pi / 2f, 0f);
        host.AddChild(probeRoot);
        probeRoot.AddChild(model);

        try
        {
            var animation = sharedPoseSource is null
                ? FindFirst<AnimationPlayer>(model)
                : null;
            var skeletons = sharedPoseSource is null
                ? FindAll<Skeleton3D>(model)
                : [];
            var meshNodes = FindAll<MeshInstance3D>(model)
                .Where(value => value.Mesh is not null)
                .ToArray();
            var skinBindings = sharedPoseSource is null
                ? meshNodes
                    .Select(mesh => ProbeSkinBinding.Create(mesh, skeletons))
                    .ToArray()
                : [];
            var emitterKeys = sharedPoseSource is not null
                ? sharedPoseSource.EmitterKeys.ToArray()
                : metadata.Particles
                    .Select(value => new War3NodeFreeEmitterKey(
                        value.ObjectId, value.Name))
                    .Concat(metadata.Ribbons.Select(value =>
                        new War3NodeFreeEmitterKey(value.ObjectId, value.Name)))
                    .ToArray();
            var emitterBindings = sharedPoseSource is null
                ? emitterKeys
                    .Select(value => FindEmitterBinding(
                        model, value.ObjectId, value.Name))
                    .ToArray()
                : [];
            if (meshNodes.Length == 0)
                throw new InvalidOperationException(
                    $"Node-free Warcraft asset contains no meshes: {source}");
            // Keep the imported model's fixed -90 degree Warcraft-to-Godot
            // correction in every part and attachment transform.  The actor
            // root is the coordinate space driven by the simulation.
            var actorInverse = probeRoot.GlobalTransform.AffineInverse();
            var parts = new List<War3NodeFreeMeshPart>(meshNodes.Length);
            var skeletonSlots = new Dictionary<ulong, int>();
            for (var partIndex = 0;
                 partIndex < meshNodes.Length;
                 partIndex++)
            {
                var mesh = meshNodes[partIndex];
                var materials = new Material?[mesh.Mesh!.GetSurfaceCount()];
                for (var surface = 0; surface < materials.Length; surface++)
                    materials[surface] = mesh.GetActiveMaterial(surface);
                var skeletonSlot = -1;
                if (sharedPoseSource is not null &&
                    partIndex < sharedPoseSource.Parts.Count)
                {
                    skeletonSlot =
                        sharedPoseSource.Parts[partIndex].SkeletonSlot;
                }
                else if (partIndex < skinBindings.Length &&
                         skinBindings[partIndex] is { } binding)
                {
                    if (!skeletonSlots.TryGetValue(
                            binding.Key, out skeletonSlot))
                    {
                        skeletonSlot = skeletonSlots.Count;
                        skeletonSlots.Add(binding.Key, skeletonSlot);
                    }
                }
                parts.Add(new War3NodeFreeMeshPart(
                    mesh.Mesh,
                    materials,
                    ReadGeosetId(mesh, model),
                    actorInverse * mesh.GlobalTransform,
                    skeletonSlot));
            }
            if (OS.GetCmdlineUserArgs().Contains(
                    "--war3-node-free-animation-probe"))
                PrintAnimationProbe(
                    source,
                    metadata,
                    animation,
                    skeletons,
                    meshNodes,
                    skinBindings);

            var output = new War3NodeFreeModelAsset(
                source,
                metadata,
                parts,
                [],
                emitterKeys,
                teamColor,
                MathF.Max(0.25f,
                    (metadata.Bounds.Maximum.Z - metadata.Bounds.Minimum.Z) * 0.01f),
                sharedPoseSource);
            var sequences = (List<War3NodeFreeSequence>)output.Sequences;
            var cacheState = "shared";
            if (sharedPoseSource is not null)
            {
                sequences.AddRange(sharedPoseSource.Sequences);
            }
            else
            {
                var cacheHit = output.TryLoadPoseCache(
                    parts.Count, sequences);
                cacheState = cacheHit ? "hit" : "built";
                if (!cacheHit)
                {
                    for (var sequenceIndex = 0;
                         sequenceIndex < metadata.Sequences.Count;
                         sequenceIndex++)
                    {
                        sequences.Add(output.SampleSequence(
                            animation,
                            skeletons,
                            meshNodes,
                            skinBindings,
                            emitterBindings,
                            model,
                            actorInverse,
                            sequenceIndex));
                    }
                    output.TrySavePoseCache();
                }
            }
            output._vatAnimation = sharedPoseSource?._vatAnimation ??
                                   War3VatAnimationData.Build(output);
            // The immutable bone/part textures are authoritative from here on.
            // Pose Skeleton RIDs existed only to bake those textures; keeping
            // thousands of sampled skeletons alive would defeat the node-free
            // runtime even though no actor attaches them anymore.
            if (sharedPoseSource is null)
                output.ReleaseBakedSkeletonResources();
            GD.Print(
                $"WAR3_NODE_FREE_ASSET source={source} team={teamColor} " +
                $"cache={cacheState} parts={parts.Count} " +
                $"sequences={sequences.Count} " +
                $"frames={sequences.Sum(value => value.PoseCount)} " +
                $"unique_poses={sequences.Sum(value => value.UniquePoseCount)}");
            return output;
        }
        catch
        {
            probeRoot.QueueFree();
            throw;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(probeRoot))
            {
                host.RemoveChild(probeRoot);
                probeRoot.Free();
            }
        }
    }

    private void ReleaseBakedSkeletonResources()
    {
        var released = 0;
        foreach (var skeleton in _ownedSkeletons)
        {
            if (!skeleton.IsValid) continue;
            RenderingServer.FreeRid(skeleton);
            released++;
        }
        _ownedSkeletons.Clear();
        foreach (var skeleton in _blendedSkeletons.Values)
            if (skeleton.IsValid) RenderingServer.FreeRid(skeleton);
        _blendedSkeletons.Clear();

        var visited = new HashSet<War3NodeFreePose>(
            ReferenceEqualityComparer.Instance);
        foreach (var sequence in Sequences)
        foreach (var pose in sequence.Poses)
        {
            if (!visited.Add(pose)) continue;
            for (var part = 0; part < pose.Parts.Length; part++)
            {
                var value = pose.Parts[part];
                if (!value.Skeleton.IsValid) continue;
                pose.Parts[part] = new War3NodeFreePartPose(
                    value.LocalTransform, default, value.Visible);
            }
        }
        GD.Print(
            $"WAR3_VAT_CPU_SKELETONS_RELEASED source={Source} " +
            $"count={released}");
    }

    public int FindSequence(IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var index = FindSequence(candidate);
            if (index >= 0) return index;
        }
        return -1;
    }

    public int FindSequence(string first) => FindSequenceCandidate(first);

    public int FindSequence(string first, string second)
    {
        var index = FindSequenceCandidate(first);
        return index >= 0 ? index : FindSequenceCandidate(second);
    }

    public int FindSequence(
        string first,
        string second,
        string third)
    {
        var index = FindSequenceCandidate(first);
        if (index >= 0) return index;
        index = FindSequenceCandidate(second);
        return index >= 0 ? index : FindSequenceCandidate(third);
    }

    public int FindSequence(
        string first,
        string second,
        string third,
        string fourth)
    {
        var index = FindSequenceCandidate(first);
        if (index >= 0) return index;
        index = FindSequenceCandidate(second);
        if (index >= 0) return index;
        index = FindSequenceCandidate(third);
        return index >= 0 ? index : FindSequenceCandidate(fourth);
    }

    private int FindSequenceCandidate(string candidate)
    {
        for (var index = 0; index < Metadata.Sequences.Count; index++)
            if (Metadata.Sequences[index].Name.Equals(
                    candidate, StringComparison.OrdinalIgnoreCase))
                return index;
        for (var index = 0; index < Metadata.Sequences.Count; index++)
            if (Metadata.Sequences[index].Name.StartsWith(
                    candidate, StringComparison.OrdinalIgnoreCase))
                return index;
        return -1;
    }

    public Material? TintedMaterial(int part, int surface, Color tint)
    {
        var key = (part, surface, tint);
        if (_tintedMaterials.TryGetValue(key, out var cached)) return cached;
        if ((uint)part >= (uint)Parts.Count ||
            (uint)surface >= (uint)Parts[part].Materials.Length ||
            Parts[part].Materials[surface] is not StandardMaterial3D source)
        {
            _tintedMaterials[key] = null;
            return null;
        }
        var material = (StandardMaterial3D)source.Duplicate();
        material.AlbedoColor = new Color(
            source.AlbedoColor.R * tint.R,
            source.AlbedoColor.G * tint.G,
            source.AlbedoColor.B * tint.B,
            source.AlbedoColor.A * tint.A);
        _tintedMaterials[key] = material;
        return material;
    }

    public Transform3D ResolveEmitterTransform(
        int sequenceIndex,
        double localMilliseconds,
        int objectId,
        string name,
        Transform3D actorWorld)
    {
        if ((uint)sequenceIndex >= (uint)Sequences.Count)
            return actorWorld;
        if (!_emitterByObjectId.TryGetValue(objectId, out var emitterIndex) &&
            !_emitterByName.TryGetValue(name, out emitterIndex))
            return actorWorld;
        var sequence = Sequences[sequenceIndex];
        if (sequence.PoseCount == 0) return actorWorld;
        var pose = sequence.Poses[sequence.PoseIndex(localMilliseconds)];
        return (uint)emitterIndex < (uint)pose.Emitters.Length
            ? actorWorld * pose.Emitters[emitterIndex]
            : actorWorld;
    }

    public Transform3D ResolveEmitterLocalTransform(
        int sequenceIndex,
        double localMilliseconds,
        int objectId,
        string name)
    {
        if ((uint)sequenceIndex >= (uint)Sequences.Count)
            return Transform3D.Identity;
        var sequence = Sequences[sequenceIndex];
        if (sequence.PoseCount == 0) return Transform3D.Identity;
        var pose = sequence.Poses[sequence.PoseIndex(localMilliseconds)];
        return ResolveEmitterLocalTransform(pose, objectId, name);
    }

    public Transform3D ResolveEmitterLocalTransform(
        War3NodeFreePose pose,
        int objectId,
        string name)
    {
        if (!_emitterByObjectId.TryGetValue(objectId, out var emitterIndex) &&
            !_emitterByName.TryGetValue(name, out emitterIndex))
            return Transform3D.Identity;
        return (uint)emitterIndex < (uint)pose.Emitters.Length
            ? pose.Emitters[emitterIndex]
            : Transform3D.Identity;
    }

    public Rid ResolveBlendedSkeleton(
        War3NodeFreePose sourcePose,
        War3NodeFreePose targetPose,
        int partIndex,
        float weight)
    {
        if (_sharedBlendSource is not null)
            return _sharedBlendSource.ResolveBlendedSkeleton(
                sourcePose, targetPose, partIndex, weight);
        var target = targetPose.Parts[partIndex].Skeleton;
        var source = sourcePose.Parts[partIndex].Skeleton;
        var slot = Parts[partIndex].SkeletonSlot;
        if (slot < 0 || !source.IsValid || !target.IsValid)
            return target;
        var step = Math.Clamp(
            (int)MathF.Round(weight * BlendSteps), 0, BlendSteps);
        if (step <= 0) return source;
        if (step >= BlendSteps) return target;
        var key = new War3NodeFreeBlendKey(
            sourcePose, targetPose, slot, (byte)step);
        if (_blendedSkeletons.TryGetValue(key, out var cached))
            return cached;
        var sourceBones = RenderingServer.SkeletonGetBoneCount(source);
        var targetBones = RenderingServer.SkeletonGetBoneCount(target);
        if (sourceBones <= 0 || sourceBones != targetBones)
            return target;
        var skeleton = RenderingServer.SkeletonCreate();
        RenderingServer.SkeletonAllocateData(skeleton, targetBones);
        var quantizedWeight = step / (float)BlendSteps;
        for (var bone = 0; bone < targetBones; bone++)
        {
            var blended = RenderingServer.SkeletonBoneGetTransform(
                    source, bone)
                .InterpolateWith(
                    RenderingServer.SkeletonBoneGetTransform(target, bone),
                    quantizedWeight);
            RenderingServer.SkeletonBoneSetTransform(
                skeleton, bone, blended);
        }
        _blendedSkeletons.Add(key, skeleton);
        _ownedSkeletons.Add(skeleton);
        return skeleton;
    }

    public StandardMaterial3D GhostMaterial(bool valid)
    {
        ref var material = ref valid
            ? ref _validGhostMaterial
            : ref _invalidGhostMaterial;
        if (material is not null) return material;
        var color = valid ? new Color("55e6b06e") : new Color("ef666676");
        material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            AlbedoColor = color,
            Emission = new Color(color.R, color.G, color.B, 1f),
            EmissionEnergyMultiplier = 0.85f
        };
        return material;
    }

    private War3NodeFreeSequence SampleSequence(
        AnimationPlayer? animationPlayer,
        IReadOnlyList<Skeleton3D> skeletons,
        IReadOnlyList<MeshInstance3D> meshes,
        IReadOnlyList<ProbeSkinBinding?> skinBindings,
        IReadOnlyList<ProbeEmitterBinding?> emitterBindings,
        Node model,
        Transform3D actorInverse,
        int sequenceIndex)
    {
        var metadataSequence = Metadata.Sequences[sequenceIndex];
        var animationName = MatchAnimation(
            animationPlayer, metadataSequence.Name);
        var animation = animationName is null
            ? null
            : animationPlayer!.GetAnimation(animationName);
        var durationMilliseconds = animation is null
            ? Math.Max(1d, metadataSequence.DurationMilliseconds)
            : Math.Max(1d, animation.Length * 1000d);
        var poseCount = animation is null
            ? 1
            : Math.Max(2, (int)Math.Ceiling(
                animation.Length * SamplesPerSecond) + 1);
        var poses = new War3NodeFreePose[poseCount];
        var uniquePoses = new Dictionary<int, List<War3NodeFreePose>>();
        var sequenceProbe = OS.GetCmdlineUserArgs().Contains(
                                "--war3-node-free-animation-probe") &&
                            (metadataSequence.Name.StartsWith(
                                 "Walk", StringComparison.OrdinalIgnoreCase) ||
                             metadataSequence.Name.StartsWith(
                                 "Attack", StringComparison.OrdinalIgnoreCase));
        Transform3D[]? probeStart = null;
        var probeDifference = 0d;
        var probePosition = 0d;
        if (animationName is not null)
        {
            // play() only schedules the new playback state.  The bake runs in
            // one host frame, so commit it explicitly before seeking samples;
            // otherwise every sample observes the sequence's entry pose.
            animationPlayer!.Stop();
            animationPlayer.Play(animationName, customBlend: 0d);
            animationPlayer.Advance(0d);
        }
        for (var poseIndex = 0; poseIndex < poseCount; poseIndex++)
        {
            var normalized = poseCount <= 1
                ? 0d
                : poseIndex / (double)(poseCount - 1);
            var localMilliseconds = durationMilliseconds * normalized;
            if (animation is not null)
            {
                animationPlayer!.Seek(
                    animation.Length * normalized, update: true);
                animationPlayer.Advance(0d);
            }
            foreach (var skeleton in skeletons)
            {
#pragma warning disable CS0618 // Asset sampling requires the evaluated skin now.
                skeleton.ForceUpdateAllBoneTransforms();
#pragma warning restore CS0618
            }
            if (sequenceProbe && poseIndex == 0)
                probeStart = CaptureNodeBonePoses(skeletons);
            else if (sequenceProbe && poseIndex == poseCount / 2 &&
                     probeStart is not null)
            {
                probeDifference = PoseDifference(
                    probeStart, CaptureNodeBonePoses(skeletons));
                probePosition = animationPlayer?.CurrentAnimationPosition ?? 0d;
            }
            var sharedSkeletons = new Dictionary<ulong, Rid>();
            var partPoses = new War3NodeFreePartPose[meshes.Count];
            for (var partIndex = 0; partIndex < meshes.Count; partIndex++)
            {
                var mesh = meshes[partIndex];
                var skeleton = CaptureSkeleton(
                    skinBindings[partIndex], sharedSkeletons);
                var visible = HierarchyVisible(mesh, model) &&
                              GeosetVisible(
                                  Parts[partIndex].GeosetId,
                                  metadataSequence,
                                  localMilliseconds);
                partPoses[partIndex] = new War3NodeFreePartPose(
                    actorInverse * mesh.GlobalTransform,
                    skeleton,
                    visible);
            }
            var emitterPoses = new Transform3D[emitterBindings.Count];
            for (var emitterIndex = 0;
                 emitterIndex < emitterBindings.Count;
                 emitterIndex++)
                emitterPoses[emitterIndex] =
                    emitterBindings[emitterIndex]?.Resolve(actorInverse) ??
                    Transform3D.Identity;
            var sampled = new War3NodeFreePose(partPoses, emitterPoses);
            var poseHash = PoseHash(sampled);
            if (uniquePoses.TryGetValue(poseHash, out var candidates))
            {
                var existing = candidates.FirstOrDefault(candidate =>
                    PoseEquals(candidate, sampled));
                if (existing is not null)
                {
                    ReleasePoseSkeletons(sampled);
                    poses[poseIndex] = existing;
                    continue;
                }
            }
            else
            {
                candidates = [];
                uniquePoses.Add(poseHash, candidates);
            }
            candidates.Add(sampled);
            poses[poseIndex] = sampled;
        }
        if (sequenceProbe)
            GD.Print(
                $"WAR3_NODE_FREE_SEQUENCE_PROBE source={Source} " +
                $"sequence={metadataSequence.Name} frames={poseCount} " +
                $"unique={uniquePoses.Values.Sum(value => value.Count)} " +
                $"node={probeDifference:0.######} " +
                $"position={probePosition:0.######} " +
                $"current={animationPlayer?.CurrentAnimation}");
        return new War3NodeFreeSequence(
            durationMilliseconds, SamplesPerSecond, poses);
    }

    private bool TryLoadPoseCache(
        int expectedPartCount,
        ICollection<War3NodeFreeSequence> output)
    {
        var path = PoseCachePath();
        if (!File.Exists(path)) return false;
        var createdSkeletons = new List<Rid>();
        var loadedSequences = new List<War3NodeFreeSequence>();
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (reader.ReadInt32() != CacheMagic ||
                reader.ReadInt32() != CacheVersion ||
                reader.ReadInt32() != SamplesPerSecond ||
                !reader.ReadString().Equals(Source, StringComparison.OrdinalIgnoreCase))
                return false;
            var fingerprint = ModelFingerprint();
            if (reader.ReadInt64() != fingerprint.Length ||
                reader.ReadInt64() != fingerprint.LastWriteTicks ||
                reader.ReadInt32() != expectedPartCount ||
                reader.ReadInt32() != Metadata.Sequences.Count ||
                reader.ReadInt32() != EmitterKeys.Count)
                return false;

            for (var sequenceIndex = 0;
                 sequenceIndex < Metadata.Sequences.Count;
                 sequenceIndex++)
            {
                var metadataSequence = Metadata.Sequences[sequenceIndex];
                RequireCache(reader.ReadString().Equals(
                    metadataSequence.Name,
                    StringComparison.Ordinal), "sequence name");
                var duration = reader.ReadDouble();
                var frameCount = reader.ReadInt32();
                RequireCache(
                    frameCount > 0 && frameCount <= 100_000,
                    "frame count");
                var uniquePoseCount = reader.ReadInt32();
                RequireCache(
                    uniquePoseCount > 0 && uniquePoseCount <= frameCount,
                    "unique pose count");
                var uniquePoses = new War3NodeFreePose[uniquePoseCount];
                for (var poseIndex = 0;
                     poseIndex < uniquePoseCount;
                     poseIndex++)
                {
                    var skeletonCount = reader.ReadInt32();
                    RequireCache(
                        skeletonCount >= 0 &&
                        skeletonCount <= expectedPartCount,
                        "skeleton count");
                    var skeletons = new Rid[skeletonCount];
                    for (var skeletonIndex = 0;
                         skeletonIndex < skeletonCount;
                         skeletonIndex++)
                    {
                        var boneCount = reader.ReadInt32();
                        RequireCache(
                            boneCount > 0 && boneCount <= 1024,
                            "bone count");
                        var skeleton = RenderingServer.SkeletonCreate();
                        RenderingServer.SkeletonAllocateData(skeleton, boneCount);
                        for (var bone = 0; bone < boneCount; bone++)
                            RenderingServer.SkeletonBoneSetTransform(
                                skeleton, bone, ReadTransform(reader));
                        skeletons[skeletonIndex] = skeleton;
                        createdSkeletons.Add(skeleton);
                    }

                    var partCount = reader.ReadInt32();
                    RequireCache(
                        partCount == expectedPartCount,
                        "part count");
                    var parts = new War3NodeFreePartPose[partCount];
                    for (var partIndex = 0; partIndex < partCount; partIndex++)
                    {
                        var local = ReadTransform(reader);
                        var visible = reader.ReadBoolean();
                        var skeletonIndex = reader.ReadInt32();
                        RequireCache(
                            skeletonIndex >= -1 &&
                            skeletonIndex < skeletonCount,
                            "part skeleton index");
                        parts[partIndex] = new War3NodeFreePartPose(
                            local,
                            skeletonIndex >= 0
                                ? skeletons[skeletonIndex]
                                : default,
                            visible);
                    }
                    var emitterCount = reader.ReadInt32();
                    RequireCache(
                        emitterCount == EmitterKeys.Count,
                        "emitter count");
                    var emitters = new Transform3D[emitterCount];
                    for (var emitterIndex = 0;
                         emitterIndex < emitterCount;
                         emitterIndex++)
                        emitters[emitterIndex] = ReadTransform(reader);
                    uniquePoses[poseIndex] = new War3NodeFreePose(
                        parts, emitters);
                }
                var poses = new War3NodeFreePose[frameCount];
                for (var frameIndex = 0;
                     frameIndex < frameCount;
                     frameIndex++)
                {
                    var poseIndex = reader.ReadInt32();
                    RequireCache(
                        (uint)poseIndex < (uint)uniquePoseCount,
                        "frame pose index");
                    poses[frameIndex] = uniquePoses[poseIndex];
                }
                loadedSequences.Add(new War3NodeFreeSequence(
                    duration, SamplesPerSecond, poses));
            }
            RequireCache(stream.Position == stream.Length, "trailing data");
            foreach (var sequence in loadedSequences) output.Add(sequence);
            _ownedSkeletons.AddRange(createdSkeletons);
            return true;
        }
        catch (Exception exception)
        {
            GD.PushWarning(
                $"Node-free pose cache ignored ({Source}): {exception.Message}");
            foreach (var skeleton in createdSkeletons)
                if (skeleton.IsValid) RenderingServer.FreeRid(skeleton);
            return false;
        }
    }

    private static void RequireCache(bool condition, string field)
    {
        if (!condition)
            throw new InvalidDataException(
                $"Invalid node-free pose cache {field}.");
    }

    private void TrySavePoseCache()
    {
        // RenderingServer is a dummy backend in headless mode. Never persist
        // dummy skin matrices over a cache produced by a rendered bake.
        if (DisplayServer.GetName().Equals(
                "headless", StringComparison.OrdinalIgnoreCase))
            return;
        var path = PoseCachePath();
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(temporary))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(CacheMagic);
                writer.Write(CacheVersion);
                writer.Write(SamplesPerSecond);
                writer.Write(Source);
                var fingerprint = ModelFingerprint();
                writer.Write(fingerprint.Length);
                writer.Write(fingerprint.LastWriteTicks);
                writer.Write(Parts.Count);
                writer.Write(Metadata.Sequences.Count);
                writer.Write(EmitterKeys.Count);
                for (var sequenceIndex = 0;
                     sequenceIndex < Sequences.Count;
                     sequenceIndex++)
                {
                    var sequence = Sequences[sequenceIndex];
                    writer.Write(Metadata.Sequences[sequenceIndex].Name);
                    writer.Write(sequence.DurationMilliseconds);
                    writer.Write(sequence.PoseCount);
                    var uniquePoses = new List<War3NodeFreePose>();
                    var poseIndexes = new Dictionary<War3NodeFreePose, int>(
                        ReferenceEqualityComparer.Instance);
                    foreach (var pose in sequence.Poses)
                    {
                        if (poseIndexes.ContainsKey(pose)) continue;
                        poseIndexes.Add(pose, uniquePoses.Count);
                        uniquePoses.Add(pose);
                    }
                    writer.Write(uniquePoses.Count);
                    foreach (var pose in uniquePoses)
                    {
                        var skeletons = new List<Rid>();
                        var skeletonIndexes = new Dictionary<Rid, int>();
                        foreach (var part in pose.Parts)
                        {
                            if (!part.Skeleton.IsValid ||
                                skeletonIndexes.ContainsKey(part.Skeleton))
                                continue;
                            skeletonIndexes.Add(part.Skeleton, skeletons.Count);
                            skeletons.Add(part.Skeleton);
                        }
                        writer.Write(skeletons.Count);
                        foreach (var skeleton in skeletons)
                        {
                            var boneCount = RenderingServer.SkeletonGetBoneCount(skeleton);
                            writer.Write(boneCount);
                            for (var bone = 0; bone < boneCount; bone++)
                                WriteTransform(
                                    writer,
                                    RenderingServer.SkeletonBoneGetTransform(
                                        skeleton, bone));
                        }
                        writer.Write(pose.Parts.Length);
                        foreach (var part in pose.Parts)
                        {
                            WriteTransform(writer, part.LocalTransform);
                            writer.Write(part.Visible);
                            writer.Write(part.Skeleton.IsValid
                                ? skeletonIndexes[part.Skeleton]
                                : -1);
                        }
                        writer.Write(pose.Emitters.Length);
                        foreach (var emitter in pose.Emitters)
                            WriteTransform(writer, emitter);
                    }
                    foreach (var pose in sequence.Poses)
                        writer.Write(poseIndexes[pose]);
                }
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception)
        {
            GD.PushWarning(
                $"Node-free pose cache save failed ({Source}): {exception.Message}");
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private string PoseCachePath()
    {
        var key = $"{Source}|{SamplesPerSecond}|{CacheVersion}";
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
        return Path.Combine(
            ProjectSettings.GlobalizePath("user://war3_node_free_model_cache"),
            $"{hash}.w3pose");
    }

    private (long Length, long LastWriteTicks) ModelFingerprint()
    {
        var entry = War3RuntimeAssets.Find(Source);
        if (entry is null) return default;
        var path = War3AssetPack.AbsolutePath(entry.ModelRelativePath);
        var info = new FileInfo(path);
        return info.Exists
            ? (info.Length, info.LastWriteTimeUtc.Ticks)
            : default;
    }

    private static int PoseHash(War3NodeFreePose pose)
    {
        var hash = new HashCode();
        foreach (var part in pose.Parts)
        {
            AddTransformHash(ref hash, part.LocalTransform);
            hash.Add(part.Visible);
            if (!part.Skeleton.IsValid)
            {
                hash.Add(-1);
                continue;
            }
            var boneCount = RenderingServer.SkeletonGetBoneCount(
                part.Skeleton);
            hash.Add(boneCount);
            for (var bone = 0; bone < boneCount; bone++)
                AddTransformHash(
                    ref hash,
                    RenderingServer.SkeletonBoneGetTransform(
                        part.Skeleton, bone));
        }
        foreach (var emitter in pose.Emitters)
            AddTransformHash(ref hash, emitter);
        return hash.ToHashCode();
    }

    private static void AddTransformHash(
        ref HashCode hash,
        Transform3D value)
    {
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.X.X));
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.X.Y));
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.X.Z));
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.Y.X));
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.Y.Y));
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.Y.Z));
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.Z.X));
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.Z.Y));
        hash.Add(BitConverter.SingleToInt32Bits(value.Basis.Z.Z));
        hash.Add(BitConverter.SingleToInt32Bits(value.Origin.X));
        hash.Add(BitConverter.SingleToInt32Bits(value.Origin.Y));
        hash.Add(BitConverter.SingleToInt32Bits(value.Origin.Z));
    }

    private static bool PoseEquals(
        War3NodeFreePose left,
        War3NodeFreePose right)
    {
        if (left.Parts.Length != right.Parts.Length ||
            left.Emitters.Length != right.Emitters.Length)
            return false;
        for (var index = 0; index < left.Parts.Length; index++)
        {
            var leftPart = left.Parts[index];
            var rightPart = right.Parts[index];
            if (leftPart.Visible != rightPart.Visible ||
                leftPart.LocalTransform != rightPart.LocalTransform ||
                !SkeletonEquals(
                    leftPart.Skeleton, rightPart.Skeleton))
                return false;
        }
        for (var index = 0; index < left.Emitters.Length; index++)
            if (left.Emitters[index] != right.Emitters[index])
                return false;
        return true;
    }

    private static bool SkeletonEquals(Rid left, Rid right)
    {
        if (left.IsValid != right.IsValid) return false;
        if (!left.IsValid) return true;
        var boneCount = RenderingServer.SkeletonGetBoneCount(left);
        if (boneCount != RenderingServer.SkeletonGetBoneCount(right))
            return false;
        for (var bone = 0; bone < boneCount; bone++)
            if (RenderingServer.SkeletonBoneGetTransform(left, bone) !=
                RenderingServer.SkeletonBoneGetTransform(right, bone))
                return false;
        return true;
    }

    private void ReleasePoseSkeletons(War3NodeFreePose pose)
    {
        var seen = new HashSet<Rid>();
        foreach (var part in pose.Parts)
        {
            if (!part.Skeleton.IsValid || !seen.Add(part.Skeleton)) continue;
            RenderingServer.FreeRid(part.Skeleton);
            _ownedSkeletons.Remove(part.Skeleton);
        }
    }

    private static void WriteTransform(BinaryWriter writer, Transform3D value)
    {
        writer.Write(value.Basis.X.X);
        writer.Write(value.Basis.X.Y);
        writer.Write(value.Basis.X.Z);
        writer.Write(value.Basis.Y.X);
        writer.Write(value.Basis.Y.Y);
        writer.Write(value.Basis.Y.Z);
        writer.Write(value.Basis.Z.X);
        writer.Write(value.Basis.Z.Y);
        writer.Write(value.Basis.Z.Z);
        writer.Write(value.Origin.X);
        writer.Write(value.Origin.Y);
        writer.Write(value.Origin.Z);
    }

    private static Transform3D ReadTransform(BinaryReader reader) => new(
        new Basis(
            new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())),
        new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

    private Rid CaptureSkeleton(
        ProbeSkinBinding? binding,
        IDictionary<ulong, Rid> shared)
    {
        if (binding is null) return default;
        var key = binding.Key;
        if (shared.TryGetValue(key, out var existing)) return existing;
        var count = binding.BindCount;
        if (count <= 0) return default;
        var clone = RenderingServer.SkeletonCreate();
        RenderingServer.SkeletonAllocateData(clone, count);
        for (var bind = 0; bind < count; bind++)
        {
            var bone = binding.BoneIndexes[bind];
            if (bone < 0 || bone >= binding.Skeleton.GetBoneCount()) continue;
            RenderingServer.SkeletonBoneSetTransform(
                clone,
                bind,
                binding.Skeleton.GetBoneGlobalPose(bone) *
                binding.Skin.GetBindPose(bind));
        }
        shared[key] = clone;
        _ownedSkeletons.Add(clone);
        return clone;
    }

    private bool GeosetVisible(
        int geosetId,
        War3Sequence sequence,
        double localMilliseconds)
    {
        if (geosetId < 0) return true;
        var frame = sequence.StartFrame + Math.Clamp(
            localMilliseconds, 0d, sequence.DurationMilliseconds);
        foreach (var animation in Metadata.GeosetAnimations)
        {
            if (animation.GeosetId != geosetId) continue;
            return animation.Alpha.Sample(
                frame, sequence, Metadata.GlobalSequences) > 0.001d;
        }
        return true;
    }

    private static StringName? MatchAnimation(
        AnimationPlayer? player,
        string sequenceName)
    {
        if (player is null) return null;
        var names = player.GetAnimationList();
        foreach (var name in names)
            if (name.ToString().Equals(
                    sequenceName, StringComparison.OrdinalIgnoreCase))
                return name;
        foreach (var name in names)
            if (name.ToString().Contains(
                    sequenceName, StringComparison.OrdinalIgnoreCase))
                return name;
        return null;
    }

    private static void PrintAnimationProbe(
        string source,
        War3ModelMetadata metadata,
        AnimationPlayer? player,
        IReadOnlyList<Skeleton3D> skeletons,
        IReadOnlyList<MeshInstance3D> meshes,
        IReadOnlyList<ProbeSkinBinding?> skinBindings)
    {
        if (player is null || skeletons.Count == 0) return;
        var sequence = metadata.Sequences.FirstOrDefault(value =>
            value.Name.StartsWith("Walk", StringComparison.OrdinalIgnoreCase));
        sequence ??= metadata.Sequences.FirstOrDefault(value =>
            value.Name.StartsWith("Attack", StringComparison.OrdinalIgnoreCase));
        if (sequence is null) return;
        var animationName = MatchAnimation(player, sequence.Name);
        if (animationName is null) return;
        var animation = player.GetAnimation(animationName);
        if (animation is null || animation.Length <= 0d) return;

        player.Play(animationName);
        player.Seek(0d, update: true);
        ForceSkeletonUpdate(skeletons);
        var nodeStart = CaptureNodeBonePoses(skeletons);
        var serverStart = CaptureServerBonePoses(meshes);
        var evaluatedStart = CaptureEvaluatedSkinPoses(skinBindings);
        player.Seek(animation.Length * 0.5d, update: true);
        ForceSkeletonUpdate(skeletons);
        var nodeMiddle = CaptureNodeBonePoses(skeletons);
        var serverMiddle = CaptureServerBonePoses(meshes);
        var evaluatedMiddle = CaptureEvaluatedSkinPoses(skinBindings);
        GD.Print(
            $"WAR3_NODE_FREE_ANIMATION_PROBE source={source} " +
            $"sequence={sequence.Name} animation={animationName} " +
            $"node={PoseDifference(nodeStart, nodeMiddle):0.######} " +
            $"server={PoseDifference(serverStart, serverMiddle):0.######} " +
            $"evaluated={PoseDifference(evaluatedStart, evaluatedMiddle):0.######} " +
            $"node_bones={nodeStart.Length} server_bones={serverStart.Length} " +
            $"skin_binds={evaluatedStart.Length}");
    }

    private static void ForceSkeletonUpdate(
        IReadOnlyList<Skeleton3D> skeletons)
    {
        foreach (var skeleton in skeletons)
        {
#pragma warning disable CS0618
            skeleton.ForceUpdateAllBoneTransforms();
#pragma warning restore CS0618
        }
    }

    private static Transform3D[] CaptureNodeBonePoses(
        IReadOnlyList<Skeleton3D> skeletons)
    {
        var output = new List<Transform3D>();
        foreach (var skeleton in skeletons)
        for (var bone = 0; bone < skeleton.GetBoneCount(); bone++)
            output.Add(skeleton.GetBoneGlobalPose(bone));
        return output.ToArray();
    }

    private static Transform3D[] CaptureServerBonePoses(
        IReadOnlyList<MeshInstance3D> meshes)
    {
        foreach (var mesh in meshes)
        {
            var skeleton = mesh.GetSkinReference()?.GetSkeleton() ?? default;
            if (!skeleton.IsValid) continue;
            var count = RenderingServer.SkeletonGetBoneCount(skeleton);
            var output = new Transform3D[count];
            for (var bone = 0; bone < count; bone++)
                output[bone] = RenderingServer.SkeletonBoneGetTransform(
                    skeleton, bone);
            return output;
        }
        return [];
    }

    private static Transform3D[] CaptureEvaluatedSkinPoses(
        IReadOnlyList<ProbeSkinBinding?> bindings)
    {
        var output = new List<Transform3D>();
        var seen = new HashSet<ulong>();
        foreach (var binding in bindings)
        {
            if (binding is null || !seen.Add(binding.Key)) continue;
            for (var bind = 0; bind < binding.BindCount; bind++)
            {
                var bone = binding.BoneIndexes[bind];
                output.Add(bone >= 0 && bone < binding.Skeleton.GetBoneCount()
                    ? binding.Skeleton.GetBoneGlobalPose(bone) *
                      binding.Skin.GetBindPose(bind)
                    : Transform3D.Identity);
            }
        }
        return output.ToArray();
    }

    private static double PoseDifference(
        IReadOnlyList<Transform3D> left,
        IReadOnlyList<Transform3D> right)
    {
        var count = Math.Min(left.Count, right.Count);
        var difference = 0d;
        for (var index = 0; index < count; index++)
            difference += TransformDifference(left[index], right[index]);
        return difference;
    }

    private static double TransformDifference(
        Transform3D left,
        Transform3D right) =>
        (left.Origin - right.Origin).Length() +
        (left.Basis.X - right.Basis.X).Length() +
        (left.Basis.Y - right.Basis.Y).Length() +
        (left.Basis.Z - right.Basis.Z).Length();

    private static int ReadGeosetId(Node node, Node stop)
    {
        for (Node? current = node;
             current is not null && current != stop;
             current = current.GetParent())
        {
            const string marker = "_Geoset_";
            var value = current.Name.ToString();
            var markerIndex = value.IndexOf(
                marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) continue;
            var start = markerIndex + marker.Length;
            var end = value.IndexOf('_', start);
            if (end < 0) end = value.Length;
            if (int.TryParse(value[start..end], out var id)) return id;
        }
        return -1;
    }

    private static bool HierarchyVisible(Node node, Node stop)
    {
        for (Node? current = node;
             current is not null && current != stop;
             current = current.GetParent())
        {
            if (current is Node3D spatial && !spatial.Visible) return false;
        }
        return true;
    }

    private static ProbeEmitterBinding? FindEmitterBinding(
        Node root,
        int objectId,
        string sourceName)
    {
        var suffix = $"_{objectId}";
        var spatialNodes = FindAll<Node3D>(root);
        var skeletons = FindAll<Skeleton3D>(root);
        var byObjectId = spatialNodes.FirstOrDefault(node =>
            node.Name.ToString().EndsWith(
                suffix, StringComparison.OrdinalIgnoreCase));
        if (byObjectId is not null)
            return new ProbeEmitterBinding(byObjectId);
        foreach (var skeleton in skeletons)
        for (var bone = 0; bone < skeleton.GetBoneCount(); bone++)
            if (skeleton.GetBoneName(bone).ToString().EndsWith(
                    suffix, StringComparison.OrdinalIgnoreCase))
                return new ProbeEmitterBinding(skeleton, bone);
        var byName = spatialNodes.FirstOrDefault(node =>
            node.Name.ToString().Contains(
                sourceName, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return new ProbeEmitterBinding(byName);
        foreach (var skeleton in skeletons)
        for (var bone = 0; bone < skeleton.GetBoneCount(); bone++)
            if (skeleton.GetBoneName(bone).ToString().Contains(
                    sourceName, StringComparison.OrdinalIgnoreCase))
                return new ProbeEmitterBinding(skeleton, bone);
        return null;
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindFirst<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static List<T> FindAll<T>(Node root) where T : Node
    {
        var output = new List<T>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is T match) output.Add(match);
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
        return output;
    }

    private sealed class ProbeEmitterBinding
    {
        private readonly Node3D? _node;
        private readonly Skeleton3D? _skeleton;
        private readonly int _boneIndex = -1;

        public ProbeEmitterBinding(Node3D node) => _node = node;

        public ProbeEmitterBinding(Skeleton3D skeleton, int boneIndex)
        {
            _skeleton = skeleton;
            _boneIndex = boneIndex;
        }

        public Transform3D Resolve(Transform3D actorInverse) =>
            _node is not null
                ? actorInverse * _node.GlobalTransform
                : actorInverse * _skeleton!.GlobalTransform *
                  _skeleton.GetBoneGlobalPose(_boneIndex);
    }

    private sealed class ProbeSkinBinding
    {
        private ProbeSkinBinding(
            ulong key,
            Skeleton3D skeleton,
            Skin skin,
            int[] boneIndexes)
        {
            Key = key;
            Skeleton = skeleton;
            Skin = skin;
            BoneIndexes = boneIndexes;
        }

        public ulong Key { get; }
        public Skeleton3D Skeleton { get; }
        public Skin Skin { get; }
        public int[] BoneIndexes { get; }
        public int BindCount => BoneIndexes.Length;

        public static ProbeSkinBinding? Create(
            MeshInstance3D mesh,
            IReadOnlyList<Skeleton3D> skeletons)
        {
            var reference = mesh.GetSkinReference();
            if (reference is null) return null;
            var skin = reference.GetSkin();
            if (skin is null || skin.GetBindCount() <= 0) return null;

            Skeleton3D? skeleton = null;
            if (!string.IsNullOrEmpty(mesh.Skeleton.ToString()))
                skeleton = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
            if (skeleton is null && skeletons.Count == 1)
                skeleton = skeletons[0];
            if (skeleton is null) return null;

            var boneIndexes = new int[skin.GetBindCount()];
            for (var bind = 0; bind < boneIndexes.Length; bind++)
            {
                var bone = skin.GetBindBone(bind);
                if (bone < 0)
                {
                    var name = skin.GetBindName(bind).ToString();
                    if (!string.IsNullOrEmpty(name))
                        bone = skeleton.FindBone(name);
                }
                boneIndexes[bind] = bone;
            }
            return new ProbeSkinBinding(
                reference.GetInstanceId(), skeleton, skin, boneIndexes);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vatBatch?.Dispose();
        _vatBatch = null;
        _vatAnimation = null;
        foreach (var skeleton in _ownedSkeletons)
            if (skeleton.IsValid) RenderingServer.FreeRid(skeleton);
        _ownedSkeletons.Clear();
        _blendedSkeletons.Clear();
        _tintedMaterials.Clear();
        _validGhostMaterial = null;
        _invalidGhostMaterial = null;
    }
}

internal sealed record War3NodeFreeMeshPart(
    Mesh Mesh,
    Material?[] Materials,
    int GeosetId,
    Transform3D RestTransform,
    int SkeletonSlot);

internal sealed record War3NodeFreeSequence(
    double DurationMilliseconds,
    int SamplesPerSecond,
    War3NodeFreePose[] Poses)
{
    public int PoseCount => Poses.Length;
    public int UniquePoseCount
    {
        get
        {
            var unique = new HashSet<War3NodeFreePose>(
                ReferenceEqualityComparer.Instance);
            foreach (var pose in Poses) unique.Add(pose);
            return unique.Count;
        }
    }

    public int PoseIndex(double milliseconds)
    {
        if (Poses.Length <= 1 || DurationMilliseconds <= 0d) return 0;
        var normalized = Math.Clamp(
            milliseconds / DurationMilliseconds, 0d, 1d);
        return Math.Clamp(
            (int)Math.Round(normalized * (Poses.Length - 1)),
            0,
            Poses.Length - 1);
    }
}

internal sealed record War3NodeFreePose(
    War3NodeFreePartPose[] Parts,
    Transform3D[] Emitters);

internal readonly record struct War3NodeFreeEmitterKey(
    int ObjectId,
    string Name);

internal readonly record struct War3NodeFreeBlendKey(
    War3NodeFreePose Source,
    War3NodeFreePose Target,
    int SkeletonSlot,
    byte Step);

internal readonly record struct War3NodeFreePartPose(
    Transform3D LocalTransform,
    Rid Skeleton,
    bool Visible);
