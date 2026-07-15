using Godot;
using RtsDemo.Demos.War3;

namespace War3Rts;

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
    private readonly Dictionary<int, List<Node3D>> _geosets = [];
    private Node? _model;
    private AnimationPlayer? _animation;
    private War3EffectRuntime? _effects;
    private War3ModelMetadata? _metadata;
    private int _sequenceIndex;
    private string _requestedSequence = string.Empty;
    private bool _requestedLoop = true;
    private bool _requestedRepeat;
    private bool _progressDriven;
    private double _drivenMilliseconds;
    private bool _deathLocked;
    private StandardMaterial3D? _ghostMaterial;

    public string Source { get; private set; } = string.Empty;
    public War3ModelMetadata? Metadata => _metadata;
    public Node? ModelRoot => _model;
    public bool Loaded => _model is not null;
    public bool IsAnimationPlaying => _animation?.IsPlaying() == true;
    public int LiveEffectCount => (_effects?.LiveParticleCount ?? 0) +
                                  (_effects?.LiveRibbonPointCount ?? 0);
    public bool IsProgressDriven => _progressDriven;
    public float DrivenProgress { get; private set; }
    public double LastTransitionBlendSeconds { get; private set; }
    public int RepeatedSequenceRestartCount { get; private set; }
    public string CurrentSequence => _sequenceIndex >= 0 && _metadata is not null &&
                                     _sequenceIndex < _metadata.Sequences.Count
        ? _metadata.Sequences[_sequenceIndex].Name
        : string.Empty;

    public override void _Ready()
    {
        ProcessPriority = 100;
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
            source, team == War3HumanScenario.PlayerId ? 0 : 1);
        _model.Name = "Model";
        if (_model is Node3D spatialModel)
            spatialModel.Rotation = new Vector3(0f, ImportedFacingCorrection, 0f);
        AddChild(_model);
        IndexGeosets(_model, _geosets);
        _animation = FindFirst<AnimationPlayer>(_model);
        if (includeEffects && camera is not null &&
            (_metadata.Particles.Count > 0 || _metadata.Ribbons.Count > 0))
        {
            _effects = new War3EffectRuntime { Name = "Effects" };
            AddChild(_effects);
            _effects.Initialize(_model, camera, _metadata);
            _effects.SetTeamColor(team == War3HumanScenario.PlayerId ? 0 : 1);
        }
        _sequenceIndex = -1;
        _requestedSequence = string.Empty;
        _requestedRepeat = false;
        _progressDriven = false;
        _drivenMilliseconds = 0d;
        DrivenProgress = 0f;
        LastTransitionBlendSeconds = 0d;
        RepeatedSequenceRestartCount = 0;
        _deathLocked = false;
        PlayPreferred(true, "Stand", "Birth");
    }

    public bool PlayPreferred(bool loop, params string[] candidates)
        => RequestSequence(loop, repeatNonLooping: false, candidates);

    /// <summary>
    /// Repeats a gameplay action even when each source sequence is marked
    /// NonLooping. Each cycle still reaches its authored final frame before the
    /// next cycle starts, unlike forcing the imported clip itself to loop.
    /// </summary>
    public bool PlayRepeatedPreferred(params string[] candidates)
        => RequestSequence(loop: true, repeatNonLooping: true, candidates);

    /// <summary>Restarts a non-looping sequence for a new gameplay cycle.</summary>
    public bool ReplayPreferred(params string[] candidates)
    {
        if (_deathLocked || _metadata is null || _metadata.Sequences.Count == 0)
            return false;
        var index = FindSequence(candidates);
        if (index < 0) return false;
        _requestedSequence = string.Join('|', candidates);
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
        var key = string.Join('|', candidates);
        if (!_progressDriven || _sequenceIndex != index || _requestedSequence != key)
            StartSequence(index, false, allowBlend: false);

        var sequence = _metadata.Sequences[index];
        _requestedSequence = key;
        _requestedLoop = false;
        _requestedRepeat = false;
        _progressDriven = true;
        DrivenProgress = progress;
        _drivenMilliseconds = sequence.DurationMilliseconds * progress;
        if (_animation is not null)
        {
            var name = FindAnimationName(_animation, sequence.Name);
            if (name is not null)
            {
                var animation = _animation.GetAnimation(name);
                _animation.Seek(animation.Length * progress, true);
                _animation.Pause();
            }
        }
        ApplyGeosetVisibility(
            _geosets, _metadata, index, _drivenMilliseconds);
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
        var key = string.Join('|', candidates);
        var sameRequest = _requestedSequence == key &&
                          _requestedLoop == effectiveLoop &&
                          _requestedRepeat == repeatAfterFinish && !_progressDriven;
        _requestedSequence = key;
        _requestedLoop = effectiveLoop;
        _requestedRepeat = repeatAfterFinish;
        _progressDriven = false;
        if (sameRequest && _sequenceIndex == index)
        {
            if (_animation?.IsPlaying() == true) return true;
            if (!effectiveLoop && !repeatAfterFinish) return true;
            if (repeatAfterFinish) RepeatedSequenceRestartCount++;
        }
        StartSequence(index, effectiveLoop);
        return true;
    }

    public bool PlayDeath()
    {
        _deathLocked = false;
        var index = FindSequence(["Death", "Dissipate", "Decay"]);
        if (index < 0) return false;
        StartSequence(index, false);
        _requestedSequence = "Death";
        _requestedLoop = false;
        _requestedRepeat = false;
        _deathLocked = true;
        return true;
    }

    public override void _Process(double delta)
    {
        if (_metadata is null || _metadata.Sequences.Count == 0) return;
        var milliseconds = _progressDriven
            ? _drivenMilliseconds
            : (_animation?.CurrentAnimationPosition ?? 0d) * 1000d;
        ApplyGeosetVisibility(_geosets, _metadata, _sequenceIndex, milliseconds);
        _effects?.Sync(milliseconds);
        if (!_deathLocked && _animation is not null && !_animation.IsPlaying() &&
            _requestedSequence.Length > 0 && (_requestedLoop || _requestedRepeat))
        {
            if (_requestedRepeat) RepeatedSequenceRestartCount++;
            StartSequence(_sequenceIndex, _requestedLoop);
        }
    }

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
        camera.Position = target + (building
            ? new Vector3(distance * 0.72f, distance * 0.48f, distance * 0.92f)
            : portrait
            ? new Vector3(distance * 0.78f, distance * 0.12f, distance)
            : new Vector3(distance * 0.72f, distance * 0.48f, distance * 0.92f));
        camera.LookAt(target, Vector3.Up);
    }

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
        DrivenProgress = 0f;
        var sequence = _metadata.Sequences[index];
        if (_animation is not null)
        {
            var name = FindAnimationName(_animation, sequence.Name);
            if (name is not null)
            {
                var animation = _animation.GetAnimation(name);
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
            }
        }
        _effects?.SetSequence(index);
        ApplyGeosetVisibility(_geosets, _metadata, index, 0d);
    }

    private void SeekAnimationToMilliseconds(double localMilliseconds)
    {
        if (_animation is null || _metadata is null || _sequenceIndex < 0) return;
        var sequence = _metadata.Sequences[_sequenceIndex];
        var name = FindAnimationName(_animation, sequence.Name);
        if (name is null) return;
        var animation = _animation.GetAnimation(name);
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
        _effects?.QueueFree();
        _model?.QueueFree();
        _effects = null;
        _model = null;
        _metadata = null;
        _animation = null;
        _geosets.Clear();
    }

    private static StringName? FindAnimationName(
        AnimationPlayer animationPlayer,
        string sequenceName)
    {
        var animations = animationPlayer.GetAnimationList();
        foreach (var name in animations)
        {
            if (name.ToString().Equals(sequenceName, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        foreach (var name in animations)
        {
            if (name.ToString().Contains(sequenceName, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }

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
            var scale = animation.Alpha.Sample(
                frame, sequence, metadata.GlobalSequences) > 0.001d
                ? Vector3.One
                : Vector3.Zero;
            foreach (var node in nodes) node.Scale = scale;
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
