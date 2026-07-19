using Godot;
using RtsDemo.Demos.War3;

namespace War3Rts;

public readonly record struct War3ModelActorProcessProfile(
    double TotalMilliseconds,
    double GeosetMilliseconds,
    double EffectsMilliseconds,
    long AllocatedBytes,
    int ActorCallbacks,
    int VisibleActors,
    int PlayingAnimationActors,
    int EffectActors);

public readonly record struct War3ModelSoundTimelineEvent(
    string EventCode,
    string SequenceName,
    ulong Sequence);

/// <summary>
/// One independently animated Warcraft model instance. It owns animation,
/// geoset visibility and legacy particle/ribbon reconstruction, but no gameplay.
/// </summary>
public sealed partial class War3ModelActor : Node3D
{
    // Warcraft models are authored facing +X. After the MDX -> glTF axis
    // conversion, rotate the imported root once so the public actor forward
    // matches the RTS world's +Z ground direction.
    private const float ImportedFacingCorrection = -Mathf.Pi / 2f;
    private const int RuntimeProfileSampleShift = 4;
    private const int RuntimeProfileSampleScale = 1 << RuntimeProfileSampleShift;
    private const int RuntimeProfileSampleMask = RuntimeProfileSampleScale - 1;
    private readonly Dictionary<int, List<Node3D>> _geosets = [];
    private readonly Dictionary<int, bool> _geosetVisibility = [];
    private Node? _model;
    private AnimationPlayer? _animation;
    private War3EffectRuntime? _effects;
    private War3ModelMetadata? _metadata;
    private StringName?[] _sequenceAnimationNames = [];
    private Animation?[] _sequenceAnimations = [];
    private int _sequenceIndex;
    private string _requestedSequence = string.Empty;
    private IReadOnlyList<string>? _requestedCandidatesReference;
    private bool _requestedLoop = true;
    private bool _requestedRepeat;
    private bool _progressDriven;
    private double _drivenMilliseconds;
    private double _sequenceMilliseconds;
    private double _sequencePlaybackDurationMilliseconds = 1d;
    private bool _animationPlaying;
    private bool _deathLocked;
    private double _lastSoundTimelineMilliseconds = -0.001d;
    private ulong _soundTimelineSequence;
    private bool _visibleInTree = true;
    private StandardMaterial3D? _ghostMaterial;
    private static bool _runtimeProfilingEnabled;
    private static long _profileTotalTicks;
    private static long _profileGeosetTicks;
    private static long _profileEffectsTicks;
    private static long _profileAllocatedBytes;
    private static int _profileActorCallbacks;
    private static int _profileVisibleActors;
    private static int _profilePlayingAnimationActors;
    private static int _profileEffectActors;
    private static int _profileSampleCursor;

    public event Action<War3ModelSoundTimelineEvent>? SoundTimelineEvent;

    public string Source { get; private set; } = string.Empty;
    public War3ModelMetadata? Metadata => _metadata;
    public Node? ModelRoot => _model;
    public bool Loaded => _model is not null;
    public bool IsAnimationPlaying => _animationPlaying;
    public int LiveEffectCount => (_effects?.LiveParticleCount ?? 0) +
                                  (_effects?.LiveRibbonPointCount ?? 0);
    public bool IsProgressDriven => _progressDriven;
    public float DrivenProgress { get; private set; }
    public double LastTransitionBlendSeconds { get; private set; }
    public int RepeatedSequenceRestartCount { get; private set; }
    public int RenderMeshCount { get; private set; }
    public int RenderSurfaceCount { get; private set; }
    public int ShadowSurfaceCount { get; private set; }
    public Color SurfaceTint { get; private set; } = Colors.White;
    public string CurrentSequence => _sequenceIndex >= 0 && _metadata is not null &&
                                     _sequenceIndex < _metadata.Sequences.Count
        ? _metadata.Sequences[_sequenceIndex].Name
        : string.Empty;
    public double CurrentSequenceDurationSeconds =>
        _sequenceIndex >= 0 && _metadata is not null &&
        _sequenceIndex < _metadata.Sequences.Count
            ? _metadata.Sequences[_sequenceIndex].DurationMilliseconds / 1000d
            : 0d;

    public float ApproximateWorldHeight()
    {
        if (_metadata is null || _model is null) return 1.5f;
        var root = FindByName<Node3D>(_model, "WarcraftRoot") ??
                   _model as Node3D;
        if (root is null)
            return MathF.Max(0.25f,
                (_metadata.Bounds.Maximum.Z -
                 _metadata.Bounds.Minimum.Z) * 0.01f);
        var center = _metadata.Bounds.Center;
        var bottom = root.ToGlobal(new Vector3(
            center.X, center.Y, _metadata.Bounds.Minimum.Z));
        var top = root.ToGlobal(new Vector3(
            center.X, center.Y, _metadata.Bounds.Maximum.Z));
        var vertical = MathF.Abs(top.Y - bottom.Y);
        return MathF.Max(0.25f,
            vertical > 0.05f ? vertical : top.DistanceTo(bottom));
    }

    public override void _Ready()
    {
        ProcessPriority = 100;
        _visibleInTree = IsVisibleInTree();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsInsideTree())
            _visibleInTree = IsVisibleInTree();
    }

    public void Load(
        string source,
        Camera3D? camera,
        int team,
        bool includeEffects = true)
    {
        ClearModel();
        Source = source;
        _metadata = War3RuntimeAssets.LoadMetadata(source);
        _model = War3RuntimeAssets.InstantiateModel(
            source, TeamColorIndex(team));
        _model.Name = "Model";
        if (_model is Node3D spatialModel)
            spatialModel.Rotation = new Vector3(0f, ImportedFacingCorrection, 0f);
        AddChild(_model);
        RefreshRenderStats();
        IndexGeosets(_model, _geosets);
        _animation = FindFirst<AnimationPlayer>(_model);
        if (_animation is not null)
            _animation.AnimationFinished += OnAnimationFinished;
        CacheSequenceAnimationNames();
        if (includeEffects && camera is not null &&
            (_metadata.Particles.Count > 0 || _metadata.Ribbons.Count > 0))
        {
            _effects = new War3EffectRuntime { Name = "Effects" };
            AddChild(_effects);
            _effects.Initialize(_model, camera, _metadata);
            _effects.SetTeamColor(TeamColorIndex(team));
        }
        _sequenceIndex = -1;
        _requestedSequence = string.Empty;
        _requestedCandidatesReference = null;
        _requestedRepeat = false;
        _progressDriven = false;
        _drivenMilliseconds = 0d;
        DrivenProgress = 0f;
        LastTransitionBlendSeconds = 0d;
        RepeatedSequenceRestartCount = 0;
        _soundTimelineSequence = 0;
        _deathLocked = false;
        SurfaceTint = Colors.White;
        PlayPreferred(true, "Stand", "Birth");
    }

    public void SetShadowCastingEnabled(bool enabled)
    {
        if (_model is null) return;
        var setting = enabled
            ? GeometryInstance3D.ShadowCastingSetting.On
            : GeometryInstance3D.ShadowCastingSetting.Off;
        SetShadowCasting(_model, setting);
        RefreshRenderStats();
    }

    public void PrepareEffectWorldBasis(Basis basis) =>
        _effects?.PrepareWorldBasis(basis);

    public void PrepareEffectWorldTransform(Transform3D transform) =>
        _effects?.PrepareWorldTransform(transform);

    /// <summary>
    /// Multiplies every imported textured surface by one runtime color. The
    /// material is duplicated per actor so feedback tinting never leaks into
    /// the cached model template or another instance.
    /// </summary>
    public void SetSurfaceTint(Color tint)
    {
        if (_model is null) return;
        ApplySurfaceTint(_model, tint);
        SurfaceTint = tint;
    }

    private static void ApplySurfaceTint(Node node, Color tint)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh)
        {
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source)
                    continue;
                var material = (StandardMaterial3D)source.Duplicate();
                material.ResourceName = source.ResourceName;
                material.AlbedoColor = new Color(
                    source.AlbedoColor.R * tint.R,
                    source.AlbedoColor.G * tint.G,
                    source.AlbedoColor.B * tint.B,
                    source.AlbedoColor.A * tint.A);
                mesh.SetSurfaceOverrideMaterial(surface, material);
            }
        }
        foreach (var child in node.GetChildren()) ApplySurfaceTint(child, tint);
    }

    public bool PlayPreferred(bool loop, params string[] candidates)
        => RequestSequence(loop, repeatNonLooping: false, candidates);

    public bool PlayPreferred(bool loop, string first) =>
        ContinueOrRequest(loop, false, first);

    public bool PlayPreferred(bool loop, string first, string second) =>
        ContinueOrRequest(loop, false, first, second);

    public bool PlayPreferred(
        bool loop,
        string first,
        string second,
        string third) =>
        ContinueOrRequest(loop, false, first, second, third);

    public bool PlayPreferred(
        bool loop,
        string first,
        string second,
        string third,
        string fourth) =>
        ContinueOrRequest(loop, false, first, second, third, fourth);

    /// <summary>
    /// Repeats a gameplay action even when each source sequence is marked
    /// NonLooping. Each cycle still reaches its authored final frame before the
    /// next cycle starts, unlike forcing the imported clip itself to loop.
    /// </summary>
    public bool PlayRepeatedPreferred(params string[] candidates)
        => RequestSequence(loop: true, repeatNonLooping: true, candidates);

    public bool PlayRepeatedPreferred(string first) =>
        ContinueOrRequest(true, true, first);

    public bool PlayRepeatedPreferred(string first, string second) =>
        ContinueOrRequest(true, true, first, second);

    public bool PlayRepeatedPreferred(
        string first,
        string second,
        string third) =>
        ContinueOrRequest(true, true, first, second, third);

    public bool PlayRepeatedPreferred(
        string first,
        string second,
        string third,
        string fourth) =>
        ContinueOrRequest(true, true, first, second, third, fourth);

    /// <summary>Restarts a non-looping sequence for a new gameplay cycle.</summary>
    public bool ReplayPreferred(params string[] candidates)
    {
        if (_deathLocked || _metadata is null || _metadata.Sequences.Count == 0)
            return false;
        var index = FindSequence(candidates);
        if (index < 0) return false;
        _requestedSequence = string.Join('|', candidates);
        _requestedCandidatesReference = candidates;
        _requestedLoop = false;
        _requestedRepeat = false;
        StartSequence(index, false);
        return true;
    }

    /// <summary>
    /// Pins a sequence to authoritative gameplay progress. The animation is
    /// explicitly paused after seeking, so unchanged progress cannot advance
    /// on render time while construction is waiting for a builder.
    /// </summary>
    public bool SetSequenceProgress(float normalizedProgress, params string[] candidates)
    {
        if (_deathLocked || _metadata is null || _metadata.Sequences.Count == 0)
            return false;
        var index = FindSequence(candidates);
        if (index < 0) return false;
        var progress = Math.Clamp(normalizedProgress, 0f, 1f);
        var key = ReferenceEquals(_requestedCandidatesReference, candidates)
            ? _requestedSequence
            : string.Join('|', candidates);
        if (!_progressDriven || _sequenceIndex != index || _requestedSequence != key)
            StartSequence(index, false, allowBlend: false);

        var sequence = _metadata.Sequences[index];
        _requestedSequence = key;
        _requestedCandidatesReference = candidates;
        _requestedLoop = false;
        _requestedRepeat = false;
        _progressDriven = true;
        _animationPlaying = false;
        DrivenProgress = progress;
        _drivenMilliseconds = sequence.DurationMilliseconds * progress;
        _sequenceMilliseconds = _drivenMilliseconds;
        _lastSoundTimelineMilliseconds = _drivenMilliseconds;
        if (_animation is not null)
        {
            var animation = AnimationForSequence(index);
            if (animation is not null)
            {
                _animation.Seek(animation.Length * progress, true);
                _animation.Pause();
            }
        }
        ApplyGeosetVisibility(
            _geosets, _geosetVisibility, _metadata, index,
            _drivenMilliseconds);
        _effects?.Sync(_drivenMilliseconds, SeekAnimationToMilliseconds);
        return true;
    }

    private bool RequestSequence(
        bool loop,
        bool repeatNonLooping,
        IReadOnlyList<string> candidates)
    {
        if (_deathLocked || _metadata is null || _metadata.Sequences.Count == 0)
            return false;
        var index = FindSequence(candidates);
        if (index < 0) return false;
        var nonLooping = _metadata.Sequences[index].NonLooping;
        var effectiveLoop = loop && !nonLooping;
        var repeatAfterFinish = loop && repeatNonLooping && nonLooping;
        var key = ReferenceEquals(_requestedCandidatesReference, candidates)
            ? _requestedSequence
            : string.Join('|', candidates);
        var sameRequest = _requestedSequence == key &&
                          _requestedLoop == effectiveLoop &&
                          _requestedRepeat == repeatAfterFinish && !_progressDriven;
        _requestedSequence = key;
        _requestedCandidatesReference = candidates;
        _requestedLoop = effectiveLoop;
        _requestedRepeat = repeatAfterFinish;
        _progressDriven = false;
        if (sameRequest && _sequenceIndex == index)
        {
            if (_animationPlaying) return true;
            if (!effectiveLoop && !repeatAfterFinish) return true;
            if (repeatAfterFinish) RepeatedSequenceRestartCount++;
        }
        StartSequence(index, effectiveLoop);
        return true;
    }

    private bool ContinueOrRequest(
        bool loop,
        bool repeatNonLooping,
        string first,
        string? second = null,
        string? third = null,
        string? fourth = null)
    {
        var count = fourth is not null
            ? 4
            : third is not null
                ? 3
                : second is not null
                    ? 2
                    : 1;
        if (TryContinueRequest(
                loop,
                repeatNonLooping,
                first,
                second,
                third,
                fourth,
                count,
                out var continued))
        {
            return continued;
        }
        return count switch
        {
            1 => RequestSequence(loop, repeatNonLooping, [first]),
            2 => RequestSequence(loop, repeatNonLooping, [first, second!]),
            3 => RequestSequence(
                loop, repeatNonLooping, [first, second!, third!]),
            _ => RequestSequence(
                loop, repeatNonLooping, [first, second!, third!, fourth!])
        };
    }

    private bool TryContinueRequest(
        bool loop,
        bool repeatNonLooping,
        string first,
        string? second,
        string? third,
        string? fourth,
        int count,
        out bool result)
    {
        result = false;
        if (_deathLocked || _metadata is null || _sequenceIndex < 0 ||
            _sequenceIndex >= _metadata.Sequences.Count || _progressDriven ||
            !RequestedCandidatesEqual(
                first, second, third, fourth, count))
        {
            return false;
        }
        var nonLooping = _metadata.Sequences[_sequenceIndex].NonLooping;
        var effectiveLoop = loop && !nonLooping;
        var repeatAfterFinish = loop && repeatNonLooping && nonLooping;
        if (_requestedLoop != effectiveLoop ||
            _requestedRepeat != repeatAfterFinish)
        {
            return false;
        }
        if (_animationPlaying)
        {
            result = true;
            return true;
        }
        if (!effectiveLoop && !repeatAfterFinish)
        {
            result = true;
            return true;
        }
        if (repeatAfterFinish) RepeatedSequenceRestartCount++;
        StartSequence(_sequenceIndex, effectiveLoop);
        result = true;
        return true;
    }

    private bool RequestedCandidatesEqual(
        string first,
        string? second,
        string? third,
        string? fourth,
        int count)
    {
        var key = _requestedSequence.AsSpan();
        var offset = 0;
        for (var index = 0; index < count; index++)
        {
            var candidate = index switch
            {
                0 => first,
                1 => second!,
                2 => third!,
                _ => fourth!
            };
            if (index > 0)
            {
                if ((uint)offset >= (uint)key.Length || key[offset] != '|')
                    return false;
                offset++;
            }
            if (offset + candidate.Length > key.Length ||
                !key.Slice(offset, candidate.Length).SequenceEqual(
                    candidate.AsSpan()))
            {
                return false;
            }
            offset += candidate.Length;
        }
        return offset == key.Length;
    }

    public bool PlayDeath()
    {
        _deathLocked = false;
        var index = FindSequence(["Death", "Dissipate", "Decay"]);
        if (index < 0) return false;
        StartSequence(index, false);
        _requestedSequence = "Death";
        _requestedCandidatesReference = null;
        _requestedLoop = false;
        _requestedRepeat = false;
        _deathLocked = true;
        return true;
    }

    public void Revive()
    {
        _deathLocked = false;
        ReplayPreferred("Birth", "Stand");
    }

    public void ResetForReuse()
    {
        _deathLocked = false;
        ReplayPreferred("Stand", "Birth");
    }

    public override void _Process(double delta)
    {
        AdvanceSequenceClock(delta);
        if (!_runtimeProfilingEnabled)
        {
            ProcessModelFrame();
            return;
        }
        _profileActorCallbacks++;
        if (_visibleInTree) _profileVisibleActors++;
        if (_animationPlaying) _profilePlayingAnimationActors++;
        if (_effects is not null) _profileEffectActors++;
        if ((_profileSampleCursor++ & RuntimeProfileSampleMask) != 0)
        {
            ProcessModelFrame();
            return;
        }
        var allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        var totalStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var geosetStart = totalStart;
        if (_metadata is null || _metadata.Sequences.Count == 0) return;
        var milliseconds = _progressDriven
            ? _drivenMilliseconds
            : _sequenceMilliseconds;
        ApplyGeosetVisibility(
            _geosets, _geosetVisibility, _metadata, _sequenceIndex,
            milliseconds);
        var geosetEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_progressDriven) DispatchSoundTimeline(milliseconds);
        var effectsStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _effects?.Sync(milliseconds);
        var effectsEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_deathLocked && _animation is not null && !_animationPlaying &&
            _requestedSequence.Length > 0 && (_requestedLoop || _requestedRepeat))
        {
            if (_requestedRepeat) RepeatedSequenceRestartCount++;
            StartSequence(_sequenceIndex, _requestedLoop);
        }
        var totalEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        _profileTotalTicks +=
            (totalEnd - totalStart) * RuntimeProfileSampleScale;
        _profileGeosetTicks +=
            (geosetEnd - geosetStart) * RuntimeProfileSampleScale;
        _profileEffectsTicks +=
            (effectsEnd - effectsStart) * RuntimeProfileSampleScale;
        _profileAllocatedBytes +=
            (GC.GetAllocatedBytesForCurrentThread() - allocatedStart) *
            RuntimeProfileSampleScale;
    }

    private void ProcessModelFrame()
    {
        if (_metadata is null || _metadata.Sequences.Count == 0) return;
        var milliseconds = _progressDriven
            ? _drivenMilliseconds
            : _sequenceMilliseconds;
        ApplyGeosetVisibility(
            _geosets, _geosetVisibility, _metadata, _sequenceIndex,
            milliseconds);
        if (!_progressDriven) DispatchSoundTimeline(milliseconds);
        _effects?.Sync(milliseconds);
        if (!_deathLocked && _animation is not null && !_animationPlaying &&
            _requestedSequence.Length > 0 && (_requestedLoop || _requestedRepeat))
        {
            if (_requestedRepeat) RepeatedSequenceRestartCount++;
            StartSequence(_sequenceIndex, _requestedLoop);
        }
    }

    internal static void BeginRuntimeProfiling()
    {
        ResetRuntimeProfile();
        _profileSampleCursor = 0;
        _runtimeProfilingEnabled = true;
    }

    internal static void EndRuntimeProfiling()
    {
        _runtimeProfilingEnabled = false;
        ResetRuntimeProfile();
        _profileSampleCursor = 0;
    }

    internal static War3ModelActorProcessProfile CaptureRuntimeProfile()
    {
        if (!_runtimeProfilingEnabled) return default;
        var profile = new War3ModelActorProcessProfile(
            TicksToMilliseconds(_profileTotalTicks),
            TicksToMilliseconds(_profileGeosetTicks),
            TicksToMilliseconds(_profileEffectsTicks),
            _profileAllocatedBytes,
            _profileActorCallbacks,
            _profileVisibleActors,
            _profilePlayingAnimationActors,
            _profileEffectActors);
        ResetRuntimeProfile();
        return profile;
    }

    private static void ResetRuntimeProfile()
    {
        _profileTotalTicks = 0;
        _profileGeosetTicks = 0;
        _profileEffectsTicks = 0;
        _profileAllocatedBytes = 0;
        _profileActorCallbacks = 0;
        _profileVisibleActors = 0;
        _profilePlayingAnimationActors = 0;
        _profileEffectActors = 0;
    }

    private static double TicksToMilliseconds(long ticks) =>
        ticks * 1_000d / System.Diagnostics.Stopwatch.Frequency;

    public void FrameCamera(Camera3D camera, bool portrait, bool building = false)
    {
        if (_metadata is null || _model is null) return;
        var bounds = _metadata.Bounds;
        var root = FindByName<Node3D>(_model, "WarcraftRoot") ?? _model as Node3D;
        var target = root is null ? bounds.Center * 0.01f : root.ToGlobal(bounds.Center);
        var visibleSize = 0f;
        if (building && TryGetVisibleMeshBounds(_model, out var visibleMin, out var visibleMax))
        {
            target = (visibleMin + visibleMax) * 0.5f;
            var dimensions = visibleMax - visibleMin;
            // The portrait opening is slightly narrower than tall. Account for
            // that here so wide structures do not clip against the stone frame.
            visibleSize = Math.Max(dimensions.Y,
                Math.Max(dimensions.X / 0.93f, dimensions.Z / 0.93f));
        }
        var definition = portrait && !building
            ? War3AssetPack.LoadPortraitCamera(Source)
            : null;
        if (definition is not null && root is not null)
        {
            camera.Fov = Mathf.RadToDeg(definition.FieldOfViewRadians);
            camera.Near = Math.Max(0.001f, definition.NearClip * 0.01f);
            camera.Far = Math.Max(camera.Near + 1f, definition.FarClip * 0.01f);
            camera.Position = root.ToGlobal(definition.Position);
            target = root.ToGlobal(definition.TargetPosition);
            camera.LookAt(target, Vector3.Up);
            return;
        }
        var size = building && visibleSize > 0.01f
            ? Math.Max(0.25f, visibleSize)
            : Math.Max(0.25f, bounds.Size * 0.01f);
        var distance = building
            ? Math.Clamp(size * 1.45f, 0.7f, 36f)
            : portrait
            ? Math.Clamp(size * 0.82f, 0.8f, 18f)
            : Math.Clamp(size * 1.55f, 1.2f, 36f);
        camera.Fov = building ? 45f : portrait ? 36f : 44f;
        camera.Near = 0.005f;
        camera.Far = 100f;
        var localOffset = building
            ? new Vector3(distance * 0.72f, distance * 0.48f, distance * 0.92f)
            : portrait
                ? new Vector3(distance * 0.78f, distance * 0.12f, distance)
                : new Vector3(distance * 0.72f, distance * 0.48f, distance * 0.92f);
        // The actor applies a -90 degree correction to imported Warcraft
        // models. Keep fallback cameras in the corrected model basis too;
        // otherwise buildings are viewed from a different side while MDX
        // portrait cameras (which already inherit the root basis) look right.
        var offset = root is null
            ? localOffset
            : root.GlobalBasis.Orthonormalized() * localOffset;
        camera.Position = target + offset;
        camera.LookAt(target, Vector3.Up);
    }

    private static int TeamColorIndex(int team) => team switch
    {
        War3HumanScenario.PlayerId => 0,
        War3HumanScenario.EnemyId => 1,
        _ => 12
    };

    private static bool TryGetVisibleMeshBounds(
        Node node,
        out Vector3 minimum,
        out Vector3 maximum)
    {
        minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity,
            float.PositiveInfinity);
        maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity,
            float.NegativeInfinity);
        AccumulateVisibleMeshBounds(node, ref minimum, ref maximum);
        return float.IsFinite(minimum.X) && float.IsFinite(maximum.X);
    }

    private static void AccumulateVisibleMeshBounds(
        Node node,
        ref Vector3 minimum,
        ref Vector3 maximum)
    {
        if (node is MeshInstance3D mesh && mesh.Visible && mesh.Mesh is not null)
        {
            var aabb = mesh.GetAabb();
            for (var x = 0; x <= 1; x++)
            for (var y = 0; y <= 1; y++)
            for (var z = 0; z <= 1; z++)
            {
                var local = aabb.Position + new Vector3(
                    aabb.Size.X * x, aabb.Size.Y * y, aabb.Size.Z * z);
                var point = mesh.ToGlobal(local);
                minimum = minimum.Min(point);
                maximum = maximum.Max(point);
            }
        }
        foreach (var child in node.GetChildren())
            AccumulateVisibleMeshBounds(child, ref minimum, ref maximum);
    }

    /// <summary>
    /// Switches the imported meshes to a translucent placement silhouette.
    /// Surface materials are left untouched and become visible again when the
    /// preview turns into a real construction foundation.
    /// </summary>
    public void SetGhostAppearance(bool enabled, bool valid = true)
    {
        if (_model is null) return;
        if (enabled)
        {
            var color = valid ? new Color("55e6b06e") : new Color("ef666676");
            _ghostMaterial ??= new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                EmissionEnabled = true
            };
            _ghostMaterial.AlbedoColor = color;
            _ghostMaterial.Emission = new Color(color.R, color.G, color.B, 1f);
            _ghostMaterial.EmissionEnergyMultiplier = 0.85f;
        }
        ApplyGhostMaterial(_model, enabled ? _ghostMaterial : null);
        if (_effects is not null) _effects.Visible = !enabled;
    }

    private static void ApplyGhostMaterial(Node node, Material? material)
    {
        if (node is MeshInstance3D mesh) mesh.MaterialOverride = material;
        foreach (var child in node.GetChildren())
            ApplyGhostMaterial(child, material);
    }

    private void RefreshRenderStats()
    {
        RenderMeshCount = 0;
        RenderSurfaceCount = 0;
        ShadowSurfaceCount = 0;
        if (_model is not null) AccumulateRenderStats(_model);
    }

    private void AccumulateRenderStats(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh)
        {
            var surfaces = mesh.Mesh.GetSurfaceCount();
            RenderMeshCount++;
            RenderSurfaceCount += surfaces;
            if (mesh.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off)
                ShadowSurfaceCount += surfaces;
        }
        foreach (var child in node.GetChildren()) AccumulateRenderStats(child);
    }

    private static void SetShadowCasting(
        Node node,
        GeometryInstance3D.ShadowCastingSetting setting)
    {
        if (node is GeometryInstance3D geometry) geometry.CastShadow = setting;
        foreach (var child in node.GetChildren())
            SetShadowCasting(child, setting);
    }

    private void StartSequence(
        int index,
        bool loop,
        bool allowBlend = true)
    {
        if (_metadata is null || (uint)index >= (uint)_metadata.Sequences.Count)
            return;
        var previousIndex = _sequenceIndex;
        _sequenceIndex = index;
        _progressDriven = false;
        _drivenMilliseconds = 0d;
        _sequenceMilliseconds = 0d;
        _sequencePlaybackDurationMilliseconds =
            _metadata.Sequences[index].DurationMilliseconds;
        _animationPlaying = false;
        _lastSoundTimelineMilliseconds = -0.001d;
        DrivenProgress = 0f;
        var sequence = _metadata.Sequences[index];
        if (_animation is not null)
        {
            var name = AnimationNameForSequence(index);
            var animation = AnimationForSequence(index);
            if (name is not null && animation is not null)
            {
                _sequencePlaybackDurationMilliseconds =
                    Math.Max(1d, animation.Length * 1000d);
                animation.LoopMode = loop
                    ? Animation.LoopModeEnum.Linear
                    : Animation.LoopModeEnum.None;
                var transition = allowBlend && previousIndex >= 0 &&
                                 previousIndex != index
                    ? Math.Clamp(_metadata.BlendTimeMilliseconds / 1000d, 0d, 0.5d)
                    : 0d;
                LastTransitionBlendSeconds = transition;
                _animation.Play(name, customBlend: transition);
                _animation.Seek(0d, true);
                _animationPlaying = true;
            }
        }
        _effects?.SetSequence(index);
        ApplyGeosetVisibility(
            _geosets, _geosetVisibility, _metadata, index, 0d);
    }

    private void SeekAnimationToMilliseconds(double localMilliseconds)
    {
        if (_animation is null || _metadata is null || _sequenceIndex < 0) return;
        var animation = AnimationForSequence(_sequenceIndex);
        if (animation is null) return;
        _animation.Seek(Math.Clamp(
            localMilliseconds / 1000d, 0d, animation.Length), true);
    }

    private int FindSequence(IReadOnlyList<string> candidates)
    {
        if (_metadata is null) return -1;
        foreach (var candidate in candidates)
        {
            for (var index = 0; index < _metadata.Sequences.Count; index++)
            {
                if (_metadata.Sequences[index].Name.Equals(
                        candidate, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            for (var index = 0; index < _metadata.Sequences.Count; index++)
            {
                if (_metadata.Sequences[index].Name.StartsWith(
                        candidate, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
        }
        return -1;
    }

    private void ClearModel()
    {
        if (_animation is not null)
            _animation.AnimationFinished -= OnAnimationFinished;
        _effects?.QueueFree();
        _model?.QueueFree();
        _effects = null;
        _model = null;
        _metadata = null;
        _animation = null;
        _animationPlaying = false;
        _sequenceMilliseconds = 0d;
        _sequencePlaybackDurationMilliseconds = 1d;
        _sequenceAnimationNames = [];
        _sequenceAnimations = [];
        _requestedCandidatesReference = null;
        _geosets.Clear();
        _geosetVisibility.Clear();
        _lastSoundTimelineMilliseconds = -0.001d;
        _soundTimelineSequence = 0;
        RenderMeshCount = 0;
        RenderSurfaceCount = 0;
        ShadowSurfaceCount = 0;
    }

    private void AdvanceSequenceClock(double delta)
    {
        if (_progressDriven || !_animationPlaying || delta <= 0d) return;
        var duration = Math.Max(1d, _sequencePlaybackDurationMilliseconds);
        _sequenceMilliseconds += delta * 1000d;
        if (_requestedLoop)
        {
            if (_sequenceMilliseconds >= duration)
                _sequenceMilliseconds %= duration;
            return;
        }
        if (_sequenceMilliseconds > duration)
            _sequenceMilliseconds = duration;
    }

    private void OnAnimationFinished(StringName animationName)
    {
        if (_progressDriven) return;
        var current = AnimationNameForSequence(_sequenceIndex);
        if (current is null || current != animationName) return;
        _animationPlaying = false;
        _sequenceMilliseconds = _sequencePlaybackDurationMilliseconds;
    }

    private void CacheSequenceAnimationNames()
    {
        if (_animation is null || _metadata is null)
        {
            _sequenceAnimationNames = [];
            _sequenceAnimations = [];
            return;
        }
        var animations = _animation.GetAnimationList();
        _sequenceAnimationNames = new StringName?[_metadata.Sequences.Count];
        _sequenceAnimations = new Animation?[_metadata.Sequences.Count];
        for (var index = 0; index < _metadata.Sequences.Count; index++)
        {
            var sequenceName = _metadata.Sequences[index].Name;
            StringName? match = null;
            foreach (var name in animations)
            {
                if (!name.ToString().Equals(
                        sequenceName, StringComparison.OrdinalIgnoreCase))
                    continue;
                match = name;
                break;
            }
            if (match is null)
            {
                foreach (var name in animations)
                {
                    if (!name.ToString().Contains(
                            sequenceName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    match = name;
                    break;
                }
            }
            _sequenceAnimationNames[index] = match;
            if (match is not null)
                _sequenceAnimations[index] = _animation.GetAnimation(match);
        }
    }

    private StringName? AnimationNameForSequence(int index) =>
        (uint)index < (uint)_sequenceAnimationNames.Length
            ? _sequenceAnimationNames[index]
            : null;

    private Animation? AnimationForSequence(int index) =>
        (uint)index < (uint)_sequenceAnimations.Length
            ? _sequenceAnimations[index]
            : null;

    private static void IndexGeosets(
        Node root,
        Dictionary<int, List<Node3D>> output)
    {
        output.Clear();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Node3D spatial && TryReadGeosetId(node.Name, out var id))
            {
                if (!output.TryGetValue(id, out var nodes))
                {
                    nodes = [];
                    output.Add(id, nodes);
                }
                nodes.Add(spatial);
            }
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
    }

    private static void ApplyGeosetVisibility(
        IReadOnlyDictionary<int, List<Node3D>> geosets,
        IDictionary<int, bool> visibilityCache,
        War3ModelMetadata metadata,
        int sequenceIndex,
        double localMilliseconds)
    {
        if (metadata.Sequences.Count == 0 || metadata.GeosetAnimations.Count == 0 ||
            sequenceIndex < 0)
            return;
        var sequence = metadata.Sequences[Math.Clamp(sequenceIndex, 0,
            metadata.Sequences.Count - 1)];
        var frame = sequence.StartFrame + Math.Clamp(
            localMilliseconds, 0d, sequence.DurationMilliseconds);
        foreach (var animation in metadata.GeosetAnimations)
        {
            if (!geosets.TryGetValue(animation.GeosetId, out var nodes)) continue;
            var visible = animation.Alpha.Sample(
                frame, sequence, metadata.GlobalSequences) > 0.001d;
            if (visibilityCache.TryGetValue(
                    animation.GeosetId, out var previous) &&
                previous == visible)
                continue;
            visibilityCache[animation.GeosetId] = visible;
            var scale = visible ? Vector3.One : Vector3.Zero;
            foreach (var node in nodes) node.Scale = scale;
        }
    }

    private void DispatchSoundTimeline(double currentMilliseconds)
    {
        if (_metadata is null || _sequenceIndex < 0 ||
            _sequenceIndex >= _metadata.Sequences.Count ||
            _metadata.EventObjects.Count == 0)
            return;
        var sequence = _metadata.Sequences[_sequenceIndex];
        var duration = Math.Max(0d, sequence.DurationMilliseconds);
        var current = Math.Clamp(currentMilliseconds, 0d, duration);
        var previous = _lastSoundTimelineMilliseconds;
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
        double previousMilliseconds,
        double currentMilliseconds)
    {
        if (!_visibleInTree) return;
        foreach (var value in _metadata!.EventObjects)
        {
            if (!value.TryGetSoundEventCode(out var eventCode)) continue;
            foreach (var frame in value.EventTrack)
            {
                var localMilliseconds = frame - sequence.StartFrame;
                if (localMilliseconds < 0d ||
                    localMilliseconds > sequence.DurationMilliseconds ||
                    localMilliseconds <= previousMilliseconds ||
                    localMilliseconds > currentMilliseconds + 0.001d)
                    continue;
                SoundTimelineEvent?.Invoke(new War3ModelSoundTimelineEvent(
                    eventCode, sequence.Name, ++_soundTimelineSequence));
            }
        }
    }

    private static bool TryReadGeosetId(StringName nodeName, out int id)
    {
        const string marker = "_Geoset_";
        var value = nodeName.ToString();
        id = -1;
        var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return false;
        var start = markerIndex + marker.Length;
        var end = value.IndexOf('_', start);
        if (end < 0) end = value.Length;
        return int.TryParse(value[start..end], out id);
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

    private static T? FindByName<T>(Node root, string name) where T : Node
    {
        if (root is T match && root.Name.ToString().Equals(
                name, StringComparison.OrdinalIgnoreCase))
            return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindByName<T>(child, name);
            if (found is not null) return found;
        }
        return null;
    }
}
