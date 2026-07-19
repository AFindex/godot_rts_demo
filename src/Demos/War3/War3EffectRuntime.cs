using Godot;

namespace RtsDemo.Demos.War3;

/// <summary>
/// Reconstructs Warcraft III ParticleEmitter2 and RibbonEmitter data that glTF
/// cannot represent. The simulation is intentionally CPU based so seeking and
/// frame-by-frame inspection stay deterministic in the asset lab.
/// </summary>
internal sealed class War3EffectRuntimeCore : IDisposable
{
    private const double FixedStepMilliseconds = 1000d / 30d;
    private const float WarcraftUnitScale = 0.01f;
    private const int ParticleEmitterModelSpaceFlag = 524288;
    private const int ParticleEmitterXyQuadFlag = 1048576;
    private static readonly Dictionary<string, Texture2D?> TextureCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static Camera3D? _preparedCamera;
    private static Basis _preparedCameraBasis;
    private static int _nextRandomSeed;
    private readonly List<ParticleEmitterRuntime> _particles = [];
    private readonly List<RibbonEmitterRuntime> _ribbons = [];
    private War3ModelMetadata? _metadata;
    private Camera3D? _camera;
    private War3Sequence? _sequence;
    private double _simulatedMilliseconds;
    private double _lastSyncMilliseconds;
    private double _accumulatorMilliseconds;
    private int _teamColor;
    private uint _randomState = 0x9E3779B9u ^
        (uint)Interlocked.Increment(ref _nextRandomSeed) * 0x85EBCA6Bu;
    private Basis _preparedWorldBasis;
    private Transform3D _preparedWorldTransform = Transform3D.Identity;
    private Transform3D _preparedWorldInverse;
    private bool _hasPreparedWorldBasis;
    private Basis _lastRebuildCameraBasis;
    private Basis _lastRebuildWorldBasis;
    private bool _hasRebuildBasis;
    private bool _geometryDirty = true;
    private bool _externalWorldTransformPrepared;
    private bool _nodeFree;
    private bool _nodeFreeVisible = true;
    private Node3D? _legacyHost;
    private bool _disposed;

    // ParticleEmitter2 and material layers use two different Warcraft filter
    // mode enums. Keep their resolved blend semantics explicit instead of
    // passing the raw integer through one shared, ambiguous switch.
    private enum EffectBlendMode
    {
        AlphaBlend,
        Additive,
        Modulate,
        Modulate2X,
        AlphaKey
    }

    public int LiveParticleCount
    {
        get
        {
            var count = 0;
            for (var index = 0; index < _particles.Count; index++)
                count += _particles[index].Particles.Count;
            return count;
        }
    }
    public int LiveRibbonPointCount
    {
        get
        {
            var count = 0;
            for (var index = 0; index < _ribbons.Count; index++)
                count += _ribbons[index].Points.Count;
            return count;
        }
    }
    public int ActiveParticleEmitterCount => _particles.Count;
    public int ActiveRibbonEmitterCount => _ribbons.Count;
    public int ResolvedEmitterCount =>
        _particles.Count(runtime => runtime.Binding is not null) +
        _ribbons.Count(runtime => runtime.Binding is not null);

    public static void PrepareCameraFrame(Camera3D camera, Basis basis)
    {
        _preparedCamera = camera;
        _preparedCameraBasis = basis;
    }

    public void PrepareWorldBasis(Basis basis)
    {
        _preparedWorldBasis = basis;
        _hasPreparedWorldBasis = true;
    }

    public void PrepareWorldTransform(Transform3D transform)
    {
        if (_hasPreparedWorldBasis &&
            transform.Origin != _preparedWorldTransform.Origin)
        {
            for (var index = 0; index < _particles.Count; index++)
            {
                var emitter = _particles[index];
                if (emitter.Particles.Count == 0 ||
                    (emitter.Definition.Flags &
                     ParticleEmitterModelSpaceFlag) != 0)
                    continue;
                // World-space particles are uploaded relative to their
                // current actor instance. A moving actor therefore needs new
                // local positions even between fixed particle steps.
                _geometryDirty = true;
                break;
            }
        }
        _preparedWorldTransform = transform;
        _preparedWorldBasis = transform.Basis;
        _preparedWorldInverse = transform.AffineInverse();
        _hasPreparedWorldBasis = true;
        _externalWorldTransformPrepared = true;
        if (_nodeFree)
        {
            foreach (var emitter in _particles)
                if (emitter.HasSurface && _nodeFreeVisible)
                    RenderingServer.InstanceSetTransform(
                        emitter.Instance, transform);
            foreach (var emitter in _ribbons)
                if (emitter.HasSurface && _nodeFreeVisible)
                    RenderingServer.InstanceSetTransform(
                        emitter.Instance, transform);
        }
    }

    public void SetNodeFreeVisible(bool visible)
    {
        _nodeFreeVisible = visible;
        if (!_nodeFree) return;
        foreach (var emitter in _particles)
        {
            if (visible && emitter.HasSurface)
                RenderingServer.InstanceSetTransform(
                    emitter.Instance, _preparedWorldTransform);
            RenderingServer.InstanceSetVisible(
                emitter.Instance, visible && emitter.HasSurface);
        }
        foreach (var emitter in _ribbons)
        {
            if (visible && emitter.HasSurface)
                RenderingServer.InstanceSetTransform(
                    emitter.Instance, _preparedWorldTransform);
            RenderingServer.InstanceSetVisible(
                emitter.Instance, visible && emitter.HasSurface);
        }
    }

    public void Initialize(
        Node modelRoot,
        Camera3D camera,
        War3ModelMetadata metadata,
        Node3D legacyHost)
    {
        ClearRuntime();
        _legacyHost = legacyHost;
        _metadata = metadata;
        _camera = camera;

        foreach (var definition in metadata.Particles)
        {
            var runtime = new ParticleEmitterRuntime(
                definition,
                FindEmitterBinding(modelRoot, definition.ObjectId, definition.Name),
                CreateMeshVisual(definition.Name),
                default,
                CreateParticleMaterial(metadata, definition));
            runtime.NodeVisual!.Mesh = runtime.Mesh;
            runtime.NodeVisual.MaterialOverride = runtime.Material;
            legacyHost.AddChild(runtime.NodeVisual);
            _particles.Add(runtime);
        }

        foreach (var definition in metadata.Ribbons)
        {
            var runtime = new RibbonEmitterRuntime(
                definition,
                FindEmitterBinding(modelRoot, definition.ObjectId, definition.Name),
                CreateMeshVisual(definition.Name),
                default,
                CreateRibbonMaterial(metadata, definition));
            runtime.NodeVisual!.Mesh = runtime.Mesh;
            runtime.NodeVisual.MaterialOverride = runtime.Material;
            legacyHost.AddChild(runtime.NodeVisual);
            _ribbons.Add(runtime);
        }

        if (metadata.Sequences.Count > 0) SetSequence(0);
    }

    /// <summary>
    /// Initializes the deterministic effect simulation without adding emitter
    /// or mesh Nodes to the SceneTree. A centralized owner supplies sampled
    /// emitter transforms for the current model pose.
    /// </summary>
    public void InitializeNodeFree(
        Camera3D camera,
        War3ModelMetadata metadata,
        Rid scenario,
        Func<int, string, double, Transform3D> resolveEmitter)
    {
        ClearRuntime();
        _legacyHost = null;
        _nodeFree = true;
        _metadata = metadata;
        _camera = camera;
        foreach (var definition in metadata.Particles)
        {
            var material = CreateParticleMaterial(metadata, definition);
            var batch = new NodeFreeParticleBatch(
                scenario,
                material,
                ParticleBlendMode(definition.FilterMode));
            var instance = batch.Instance;
            // Empty emitters stay out of the RenderingServer visible set.
            // They become visible lazily when their first surface is built.
            RenderingServer.InstanceSetVisible(instance, false);
            _particles.Add(new ParticleEmitterRuntime(
                definition,
                new EmitterBinding(() => resolveEmitter(
                    definition.ObjectId,
                    definition.Name,
                    _simulatedMilliseconds)),
                null,
                instance,
                material,
                batch.Mesh,
                batch));
        }
        foreach (var definition in metadata.Ribbons)
        {
            var mesh = new ArrayMesh();
            var material = CreateRibbonMaterial(metadata, definition);
            var instance = RenderingServer.InstanceCreate2(
                mesh.GetRid(), scenario);
            RenderingServer.InstanceSetSurfaceOverrideMaterial(
                instance, 0, material.GetRid());
            RenderingServer.InstanceGeometrySetCastShadowsSetting(
                instance, RenderingServer.ShadowCastingSetting.Off);
            RenderingServer.InstanceSetVisible(instance, false);
            _ribbons.Add(new RibbonEmitterRuntime(
                definition,
                new EmitterBinding(() => resolveEmitter(
                    definition.ObjectId,
                    definition.Name,
                    _simulatedMilliseconds)),
                null,
                instance,
                material,
                mesh));
        }
        if (metadata.Sequences.Count > 0) SetSequence(0);
    }

    public void SetSequence(int sequenceIndex)
    {
        if (_metadata is null || _metadata.Sequences.Count == 0) return;
        _sequence = _metadata.Sequences[Math.Clamp(sequenceIndex, 0, _metadata.Sequences.Count - 1)];
        Reset();
    }

    public void SetTeamColor(int teamColor)
    {
        _teamColor = Math.Clamp(teamColor, 0, 11);
        foreach (var emitter in _particles)
        {
            var texture = ResolveParticleTexture(
                _metadata!, emitter.Definition);
            emitter.Material.AlbedoTexture = texture;
            emitter.Batch?.SetTexture(texture);
        }
    }

    public void Reset()
    {
        _simulatedMilliseconds = 0d;
        _lastSyncMilliseconds = 0d;
        _accumulatorMilliseconds = 0d;
        foreach (var emitter in _particles)
        {
            emitter.Particles.Clear();
            emitter.SpawnRemainder = 0d;
            emitter.LastSquirtFrame = null;
            if (emitter.Batch is not null)
                emitter.Batch.Reset();
            else
                emitter.Mesh.ClearSurfaces();
            emitter.HasSurface = false;
            if (_nodeFree)
                RenderingServer.InstanceSetVisible(emitter.Instance, false);
        }
        foreach (var emitter in _ribbons)
        {
            emitter.Points.Clear();
            emitter.SpawnRemainder = 0d;
            emitter.Mesh.ClearSurfaces();
            emitter.HasSurface = false;
            if (_nodeFree)
                RenderingServer.InstanceSetVisible(emitter.Instance, false);
        }
        _geometryDirty = true;
    }

    public void Sync(
        double localMilliseconds,
        Action<double>? sampleAnimation = null)
    {
        if (_sequence is null || _metadata is null || _camera is null) return;
        PrepareWorldTransformForSync();
        localMilliseconds = Math.Clamp(localMilliseconds, 0d, _sequence.DurationMilliseconds);
        if (localMilliseconds + 0.5d < _lastSyncMilliseconds) Reset();
        var delta = localMilliseconds - _lastSyncMilliseconds;
        if (delta <= 0d)
        {
            _lastSyncMilliseconds = localMilliseconds;
            sampleAnimation?.Invoke(localMilliseconds);
            RebuildMeshesIfNeeded();
            return;
        }

        _accumulatorMilliseconds += delta;
        var simulated = false;
        while (_accumulatorMilliseconds >= FixedStepMilliseconds)
        {
            _simulatedMilliseconds += FixedStepMilliseconds;
            sampleAnimation?.Invoke(_simulatedMilliseconds);
            SimulateStep(FixedStepMilliseconds / 1000d);
            _accumulatorMilliseconds -= FixedStepMilliseconds;
            simulated = true;
        }
        _lastSyncMilliseconds = localMilliseconds;
        sampleAnimation?.Invoke(localMilliseconds);
        if (simulated) _geometryDirty = true;
        RebuildMeshesIfNeeded();
    }

    public void Seek(double localMilliseconds, Action<double>? sampleAnimation = null)
    {
        if (_sequence is null) return;
        PrepareWorldTransformForSync();
        Reset();
        var target = Math.Clamp(localMilliseconds, 0d, _sequence.DurationMilliseconds);
        while (_simulatedMilliseconds + FixedStepMilliseconds <= target)
        {
            _simulatedMilliseconds += FixedStepMilliseconds;
            sampleAnimation?.Invoke(_simulatedMilliseconds);
            SimulateStep(FixedStepMilliseconds / 1000d);
        }
        _lastSyncMilliseconds = target;
        _accumulatorMilliseconds = target - _simulatedMilliseconds;
        sampleAnimation?.Invoke(target);
        _geometryDirty = true;
        RebuildMeshesIfNeeded();
    }

    private void SimulateStep(double deltaSeconds)
    {
        if (_metadata is null || _sequence is null) return;
        var frame = _sequence.StartFrame + _simulatedMilliseconds;
        Skeleton3D? cachedSkeleton = null;
        var cachedSkeletonTransform = Transform3D.Identity;

        foreach (var runtime in _particles)
        {
            var definition = runtime.Definition;
            var visibility = definition.Visibility.Sample(frame, _sequence, _metadata.GlobalSequences);
            var rate = Math.Max(0d,
                definition.EmissionRate.Sample(frame, _sequence, _metadata.GlobalSequences));
            if (visibility > 0.01d && runtime.Binding is not null)
            {
                int spawnCount;
                if (definition.Squirt && definition.EmissionRate.Keys.Count > 0)
                {
                    var key = LatestSequenceKey(
                        definition.EmissionRate, frame, _sequence, _metadata.GlobalSequences);
                    spawnCount = key is not null && runtime.LastSquirtFrame != key.Frame
                        ? Math.Max(0, (int)Math.Floor(key.Value))
                        : 0;
                    if (key is not null) runtime.LastSquirtFrame = key.Frame;
                }
                else
                {
                    var desired = rate * deltaSeconds + runtime.SpawnRemainder;
                    spawnCount = (int)Math.Floor(desired);
                    runtime.SpawnRemainder = desired - spawnCount;
                }
                spawnCount = Math.Min(spawnCount, 96);
                var emitterTransform = runtime.Binding.ResolveGlobalTransform(
                    ref cachedSkeleton, ref cachedSkeletonTransform);
                for (var index = 0; index < spawnCount; index++)
                    SpawnParticle(runtime, frame, emitterTransform);
            }

            var survivingParticles = 0;
            var particleCount = runtime.Particles.Count;
            for (var index = 0; index < particleCount; index++)
            {
                var particle = runtime.Particles[index];
                particle.Age += deltaSeconds;
                if (particle.Age >= particle.Life) continue;
                particle.Velocity += particle.Gravity * (float)deltaSeconds;
                particle.Position += particle.Velocity * (float)deltaSeconds;
                runtime.Particles[survivingParticles++] = particle;
            }
            if (survivingParticles < particleCount)
                runtime.Particles.RemoveRange(
                    survivingParticles, particleCount - survivingParticles);
        }

        foreach (var runtime in _ribbons)
        {
            var definition = runtime.Definition;
            var visibility = definition.Visibility.Sample(frame, _sequence, _metadata.GlobalSequences);
            if (visibility > 0.01d && runtime.Binding is not null)
            {
                var desired = Math.Max(1d, definition.EmissionRate) * deltaSeconds +
                              runtime.SpawnRemainder;
                var count = (int)Math.Floor(desired);
                runtime.SpawnRemainder = desired - count;
                var emitterTransform = runtime.Binding.ResolveGlobalTransform(
                    ref cachedSkeleton, ref cachedSkeletonTransform);
                for (var index = 0; index < Math.Min(count, 8); index++)
                    SpawnRibbonPoint(runtime, frame, emitterTransform);
                if (runtime.Points.Count > 512)
                    runtime.Points.RemoveRange(0, runtime.Points.Count - 512);
            }
            var survivingPoints = 0;
            var pointCount = runtime.Points.Count;
            for (var index = 0; index < pointCount; index++)
            {
                var point = runtime.Points[index];
                point.Age += deltaSeconds;
                var gravityOffset = Vector3.Down *
                                    (float)(definition.Gravity * WarcraftUnitScale *
                                            point.Age * deltaSeconds);
                point.Center += gravityOffset;
                point.Above += gravityOffset;
                point.Below += gravityOffset;
                if (point.Age >= Math.Max(0.05d, definition.LifeSpan)) continue;
                runtime.Points[survivingPoints++] = point;
            }
            if (survivingPoints < pointCount)
                runtime.Points.RemoveRange(
                    survivingPoints, pointCount - survivingPoints);
        }
    }

    private static War3ScalarTrack.Key? LatestSequenceKey(
        War3ScalarTrack track,
        double frame,
        War3Sequence sequence,
        IReadOnlyList<double> globalSequences)
    {
        var sampleFrame = frame;
        var global = false;
        if (track.GlobalSequenceId is int globalId &&
            globalId >= 0 && globalId < globalSequences.Count)
        {
            var duration = Math.Max(1d, globalSequences[globalId]);
            sampleFrame = ((frame % duration) + duration) % duration;
            global = true;
        }
        War3ScalarTrack.Key? latest = null;
        for (var index = 0; index < track.Keys.Count; index++)
        {
            var key = track.Keys[index];
            if (!global && (key.Frame < sequence.StartFrame ||
                            key.Frame > sequence.EndFrame))
                continue;
            if (key.Frame <= sampleFrame &&
                (latest is null || key.Frame >= latest.Frame))
                latest = key;
        }
        return latest;
    }

    private void SpawnParticle(
        ParticleEmitterRuntime runtime,
        double frame,
        Transform3D emitterTransform)
    {
        var definition = runtime.Definition;
        var speed = definition.Speed.Sample(frame, _sequence!, _metadata!.GlobalSequences);
        var variation = definition.Variation.Sample(frame, _sequence!, _metadata.GlobalSequences);
        speed *= 1d + RandomRange(-variation, variation);
        var latitude = Mathf.DegToRad((float)definition.Latitude.Sample(
            frame, _sequence!, _metadata.GlobalSequences));
        var yaw = (float)RandomRange(-Math.PI, Math.PI);
        var polar = (float)RandomRange(0d, latitude);
        var direction = new Vector3(
            MathF.Sin(polar) * MathF.Cos(yaw),
            MathF.Sin(polar) * MathF.Sin(yaw),
            MathF.Cos(polar));
        if ((definition.Flags & 131072) != 0) direction.X = 0f;
        direction = (emitterTransform.Basis.Orthonormalized() * direction)
            .Normalized();
        // Width/Length remain in MDX node-local units. ToGlobal applies the
        // WarcraftRoot scale; multiplying by 0.01 here would scale twice.
        var width = (float)definition.Width.Sample(frame, _sequence!, _metadata.GlobalSequences);
        var length = (float)definition.Length.Sample(frame, _sequence!, _metadata.GlobalSequences);
        var localOffset = new Vector3(
            (float)RandomRange(-width, width),
            (float)RandomRange(-length, length),
            0f);
        var worldOrigin = emitterTransform * localOffset;
        var worldScale = WorldScale(emitterTransform.Basis);
        var worldVelocity = direction * (float)speed * worldScale;
        var worldGravity = Vector3.Down *
                           (float)definition.Gravity.Sample(
                               frame, _sequence!, _metadata.GlobalSequences) *
                           worldScale;
        var modelSpace =
            (definition.Flags & ParticleEmitterModelSpaceFlag) != 0;
        runtime.Particles.Add(new Particle
        {
            // Warcraft PE2 particles are world-space unless the emitter has
            // the explicit ModelSpace flag. Keeping ordinary smoke in the
            // actor's local space drags every old particle along with a moving
            // missile and collapses the trail into one opaque clump.
            Position = modelSpace ? ToEffectLocal(worldOrigin) : worldOrigin,
            Velocity = modelSpace
                ? ToEffectLocalVector(worldVelocity)
                : worldVelocity,
            Gravity = modelSpace
                ? ToEffectLocalVector(worldGravity)
                : worldGravity,
            Life = Math.Max(0.03d, definition.LifeSpan),
            Rotation = yaw
        });
    }

    private void SpawnRibbonPoint(
        RibbonEmitterRuntime runtime,
        double frame,
        Transform3D transform)
    {
        var definition = runtime.Definition;
        var above = (float)definition.HeightAbove.Sample(
            frame, _sequence!, _metadata!.GlobalSequences);
        var below = (float)definition.HeightBelow.Sample(
            frame, _sequence!, _metadata.GlobalSequences);
        var center = ToEffectLocal(transform.Origin);
        runtime.Points.Add(new RibbonPoint
        {
            Center = center,
            Above = ToEffectLocal(transform * new Vector3(0f, above, 0f)),
            Below = ToEffectLocal(transform * new Vector3(0f, -below, 0f)),
            Alpha = (float)Math.Clamp(definition.Alpha.Sample(
                frame, _sequence!, _metadata.GlobalSequences), 0d, 1d),
            Age = 0d
        });
    }

    private Vector3 ToEffectLocal(Vector3 globalPosition) =>
        _hasPreparedWorldBasis
            ? _preparedWorldInverse * globalPosition
            : _legacyHost?.ToLocal(globalPosition) ?? globalPosition;

    private Vector3 ToEffectLocalVector(Vector3 globalVector) =>
        _hasPreparedWorldBasis
            ? _preparedWorldBasis.Inverse() * globalVector
            : _legacyHost?.GlobalTransform.Basis.Inverse() * globalVector ??
              globalVector;

    private void PrepareWorldTransformForSync()
    {
        if (!_externalWorldTransformPrepared)
        {
            var transform = _legacyHost?.GlobalTransform ?? Transform3D.Identity;
            _preparedWorldTransform = transform;
            _preparedWorldBasis = transform.Basis;
            _preparedWorldInverse = transform.AffineInverse();
            _hasPreparedWorldBasis = true;
        }
        _externalWorldTransformPrepared = false;
    }

    private void RebuildMeshesIfNeeded()
    {
        if (_camera is null) return;
        var cameraBasis = ReferenceEquals(_camera, _preparedCamera)
            ? _preparedCameraBasis
            : _camera.GlobalBasis;
        var worldBasis = _hasPreparedWorldBasis
            ? _preparedWorldBasis
            : _legacyHost?.GlobalBasis ?? Basis.Identity;
        if (!_geometryDirty && _hasRebuildBasis &&
            cameraBasis == _lastRebuildCameraBasis &&
            worldBasis == _lastRebuildWorldBasis)
            return;
        _lastRebuildCameraBasis = cameraBasis;
        _lastRebuildWorldBasis = worldBasis;
        _hasRebuildBasis = true;
        _geometryDirty = false;
        RebuildMeshes(cameraBasis, worldBasis);
    }

    private void RebuildMeshes(Basis cameraBasis, Basis worldBasis)
    {
        var inverseBasis = worldBasis.Inverse();
        var right = inverseBasis * cameraBasis.X.Normalized();
        var up = inverseBasis * cameraBasis.Y.Normalized();
        Skeleton3D? cachedSkeleton = null;
        var cachedSkeletonTransform = Transform3D.Identity;
        foreach (var runtime in _particles)
        {
            var emitterWorldScale = runtime.Binding is null
                ? WarcraftUnitScale
                : WorldScale(runtime.Binding.ResolveGlobalTransform(
                    ref cachedSkeleton, ref cachedSkeletonTransform).Basis);
            RebuildParticleMesh(runtime, right, up, emitterWorldScale);
        }
        foreach (var runtime in _ribbons) RebuildRibbonMesh(runtime);
    }

    private void RebuildParticleMesh(
        ParticleEmitterRuntime runtime,
        Vector3 cameraRight,
        Vector3 cameraUp,
        float emitterWorldScale)
    {
        if (runtime.Particles.Count == 0)
        {
            if (runtime.HasSurface)
            {
                if (runtime.Batch is not null)
                    runtime.Batch.Reset();
                else
                    runtime.Mesh.ClearSurfaces();
                runtime.HasSurface = false;
                SetNodeFreeSurfaceVisible(runtime.Instance, false);
            }
            return;
        }
        var rendersHead = (runtime.Definition.FrameFlags & 1) != 0 ||
                          runtime.Definition.FrameFlags == 0;
        var rendersTail = false;
        if ((runtime.Definition.FrameFlags & 2) != 0)
        {
            for (var index = 0; index < runtime.Particles.Count; index++)
            {
                if (runtime.Particles[index].Velocity.LengthSquared() <=
                    0.00001f)
                    continue;
                rendersTail = true;
                break;
            }
        }
        if (!rendersHead && !rendersTail)
        {
            if (runtime.HasSurface)
            {
                if (runtime.Batch is not null)
                    runtime.Batch.Reset();
                else
                    runtime.Mesh.ClearSurfaces();
            }
            runtime.HasSurface = false;
            SetNodeFreeSurfaceVisible(runtime.Instance, false);
            return;
        }
        if (runtime.Batch is not null)
        {
            RebuildNodeFreeParticleBatch(
                runtime, cameraRight, cameraUp,
                emitterWorldScale, rendersHead, rendersTail);
            return;
        }
        var vertexCount = rendersHead ? runtime.Particles.Count * 6 : 0;
        if (rendersTail)
        {
            for (var index = 0; index < runtime.Particles.Count; index++)
                if (runtime.Particles[index].Velocity.LengthSquared() >
                    0.00001f)
                    vertexCount += 6;
        }
        runtime.Mesh.ClearSurfaces();
        runtime.Geometry.Prepare(vertexCount);
        foreach (var particle in runtime.Particles)
        {
            var modelSpace =
                (runtime.Definition.Flags & ParticleEmitterModelSpaceFlag) != 0;
            var position = modelSpace
                ? particle.Position
                : ToEffectLocal(particle.Position);
            var velocity = modelSpace
                ? particle.Velocity
                : ToEffectLocalVector(particle.Velocity);
            var progress = (float)Math.Clamp(particle.Age / particle.Life, 0d, 1d);
            var color = ParticleColor(runtime.Definition, progress);
            var size = ParticleScale(runtime.Definition, progress) *
                       emitterWorldScale;
            var (uv0, uv1) = ParticleUv(runtime.Definition, progress);
            if ((runtime.Definition.FrameFlags & 1) != 0 || runtime.Definition.FrameFlags == 0)
            {
                var rotatesInPlane =
                    (runtime.Definition.Flags & ParticleEmitterXyQuadFlag) != 0;
                var cos = rotatesInPlane ? MathF.Cos(particle.Rotation) : 1f;
                var sin = rotatesInPlane ? MathF.Sin(particle.Rotation) : 0f;
                var baseRight = rotatesInPlane
                    ? Vector3.Right
                    : cameraRight;
                var baseUp = rotatesInPlane
                    ? Vector3.Up
                    : cameraUp;
                var rotatedRight = baseRight * cos + baseUp * sin;
                var rotatedUp = -baseRight * sin + baseUp * cos;
                AppendQuad(runtime.Geometry, position, rotatedRight * size,
                    rotatedUp * size, color, uv0, uv1);
            }
            if ((runtime.Definition.FrameFlags & 2) != 0 && velocity.LengthSquared() > 0.00001f)
            {
                var tail = velocity * (float)runtime.Definition.TailLength;
                var side = tail.Cross(cameraUp).Normalized() * Math.Max(0.004f, size * 0.45f);
                AppendTrailQuad(runtime.Geometry, position, position - tail,
                    side, color, uv0, uv1);
            }
        }
        runtime.Geometry.Commit(runtime.Mesh);
        if (!runtime.HasSurface)
            ActivateNodeFreeSurface(runtime.Instance);
        runtime.HasSurface = true;
    }

    private void RebuildNodeFreeParticleBatch(
        ParticleEmitterRuntime runtime,
        Vector3 cameraRight,
        Vector3 cameraUp,
        float emitterWorldScale,
        bool rendersHead,
        bool rendersTail)
    {
        var batch = runtime.Batch!;
        var instanceCount = rendersHead ? runtime.Particles.Count : 0;
        if (rendersTail)
            for (var index = 0; index < runtime.Particles.Count; index++)
                if (runtime.Particles[index].Velocity.LengthSquared() >
                    0.00001f)
                    instanceCount++;
        batch.Prepare(instanceCount);
        foreach (var particle in runtime.Particles)
        {
            var modelSpace =
                (runtime.Definition.Flags & ParticleEmitterModelSpaceFlag) != 0;
            var position = modelSpace
                ? particle.Position
                : ToEffectLocal(particle.Position);
            var velocity = modelSpace
                ? particle.Velocity
                : ToEffectLocalVector(particle.Velocity);
            var progress = (float)Math.Clamp(
                particle.Age / particle.Life, 0d, 1d);
            var color = ParticleColor(runtime.Definition, progress);
            var size = ParticleScale(runtime.Definition, progress) *
                       emitterWorldScale;
            var (uv0, uv1) = ParticleUv(runtime.Definition, progress);
            if (rendersHead)
            {
                var rotatesInPlane =
                    (runtime.Definition.Flags & ParticleEmitterXyQuadFlag) != 0;
                var cos = rotatesInPlane ? MathF.Cos(particle.Rotation) : 1f;
                var sin = rotatesInPlane ? MathF.Sin(particle.Rotation) : 0f;
                var baseRight = rotatesInPlane
                    ? Vector3.Right
                    : cameraRight;
                var baseUp = rotatesInPlane
                    ? Vector3.Up
                    : cameraUp;
                var right = (baseRight * cos + baseUp * sin) * size;
                var up = (-baseRight * sin + baseUp * cos) * size;
                batch.Add(QuadTransform(
                    position, right, up), color, uv0, uv1);
            }
            if (rendersTail &&
                velocity.LengthSquared() > 0.00001f)
            {
                var tail = velocity *
                           (float)runtime.Definition.TailLength;
                var side = tail.Cross(cameraUp).Normalized() *
                           Math.Max(0.004f, size * 0.45f);
                batch.Add(
                    QuadTransform(
                        position - tail * 0.5f,
                        side,
                        tail * 0.5f),
                    color,
                    uv0,
                    uv1);
            }
        }
        batch.Commit();
        if (!runtime.HasSurface)
            ActivateNodeFreeSurface(runtime.Instance);
        runtime.HasSurface = true;
    }

    private static Transform3D QuadTransform(
        Vector3 center,
        Vector3 right,
        Vector3 up)
    {
        var forward = right.Cross(up);
        if (forward.LengthSquared() <= 0.0000001f)
            forward = Vector3.Back * Math.Max(
                0.0001f, MathF.Max(right.Length(), up.Length()));
        return new Transform3D(
            new Basis(right, up, forward.Normalized() *
                Math.Max(0.0001f, MathF.Max(right.Length(), up.Length()))),
            center);
    }

    private void RebuildRibbonMesh(RibbonEmitterRuntime runtime)
    {
        if (runtime.Points.Count < 2)
        {
            if (runtime.HasSurface)
            {
                runtime.Mesh.ClearSurfaces();
                runtime.HasSurface = false;
                SetNodeFreeSurfaceVisible(runtime.Instance, false);
            }
            return;
        }
        runtime.Mesh.ClearSurfaces();
        runtime.Geometry.Prepare((runtime.Points.Count - 1) * 6);
        for (var index = 1; index < runtime.Points.Count; index++)
        {
            var previous = runtime.Points[index - 1];
            var current = runtime.Points[index];
            var previousProgress = (float)Math.Clamp(
                previous.Age / Math.Max(0.05d, runtime.Definition.LifeSpan), 0d, 1d);
            var currentProgress = (float)Math.Clamp(
                current.Age / Math.Max(0.05d, runtime.Definition.LifeSpan), 0d, 1d);
            var previousColor = runtime.Definition.Color with
            {
                A = previous.Alpha * (1f - previousProgress)
            };
            var currentColor = runtime.Definition.Color with
            {
                A = current.Alpha * (1f - currentProgress)
            };
            AppendRibbonSegment(runtime.Geometry, previous, current,
                previousColor, currentColor, index - 1, runtime.Points.Count - 1);
        }
        runtime.Geometry.Commit(runtime.Mesh);
        if (!runtime.HasSurface)
            ActivateNodeFreeSurface(runtime.Instance);
        runtime.HasSurface = true;
    }

    private void ActivateNodeFreeSurface(Rid instance)
    {
        if (!_nodeFree) return;
        RenderingServer.InstanceSetTransform(instance, _preparedWorldTransform);
        RenderingServer.InstanceSetVisible(instance, _nodeFreeVisible);
    }

    private void SetNodeFreeSurfaceVisible(Rid instance, bool visible)
    {
        if (_nodeFree)
            RenderingServer.InstanceSetVisible(
                instance, visible && _nodeFreeVisible);
    }

    private static void AppendQuad(
        EffectGeometryBuffer geometry,
        Vector3 center,
        Vector3 right,
        Vector3 up,
        Color color,
        Vector2 uv0,
        Vector2 uv1)
    {
        var a = center - right - up;
        var b = center + right - up;
        var c = center + right + up;
        var d = center - right + up;
        AddTriangle(geometry, a, b, c, color,
            new Vector2(uv0.X, uv1.Y), uv1, new Vector2(uv1.X, uv0.Y));
        AddTriangle(geometry, a, c, d, color,
            new Vector2(uv0.X, uv1.Y), new Vector2(uv1.X, uv0.Y), uv0);
    }

    private static void AppendTrailQuad(
        EffectGeometryBuffer geometry,
        Vector3 head,
        Vector3 tail,
        Vector3 side,
        Color color,
        Vector2 uv0,
        Vector2 uv1)
    {
        AddTriangle(geometry, head - side, head + side, tail + side, color,
            new Vector2(uv0.X, uv0.Y), new Vector2(uv0.X, uv1.Y), uv1);
        AddTriangle(geometry, head - side, tail + side, tail - side, color,
            new Vector2(uv0.X, uv0.Y), uv1, new Vector2(uv1.X, uv0.Y));
    }

    private static void AppendRibbonSegment(
        EffectGeometryBuffer geometry,
        RibbonPoint previous,
        RibbonPoint current,
        Color previousColor,
        Color currentColor,
        int index,
        int total)
    {
        var u0 = total <= 0 ? 0f : index / (float)total;
        var u1 = total <= 0 ? 1f : (index + 1f) / total;
        geometry.Add(previous.Above, previousColor, new Vector2(u0, 0f));
        geometry.Add(previous.Below, previousColor, new Vector2(u0, 1f));
        geometry.Add(current.Below, currentColor, new Vector2(u1, 1f));
        geometry.Add(previous.Above, previousColor, new Vector2(u0, 0f));
        geometry.Add(current.Below, currentColor, new Vector2(u1, 1f));
        geometry.Add(current.Above, currentColor, new Vector2(u1, 0f));
    }

    private static void AddTriangle(
        EffectGeometryBuffer geometry,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color color,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC)
    {
        geometry.Add(a, color, uvA);
        geometry.Add(b, color, uvB);
        geometry.Add(c, color, uvC);
    }

    private static Color ParticleColor(War3ParticleEmitterDefinition definition, float progress)
    {
        var colors = definition.SegmentColors.Length >= 3
            ? definition.SegmentColors
            : [Colors.White, Colors.White, Colors.White];
        var alpha = definition.Alpha.Length >= 3 ? definition.Alpha : [255, 255, 0];
        if (progress <= definition.Time)
        {
            var t = definition.Time <= 0d ? 1f : progress / (float)definition.Time;
            return colors[0].Lerp(colors[1], t) with
            {
                A = Mathf.Lerp(alpha[0], alpha[1], t) / 255f
            };
        }
        var denominator = Math.Max(0.001d, 1d - definition.Time);
        var secondT = (float)Math.Clamp((progress - definition.Time) / denominator, 0d, 1d);
        return colors[1].Lerp(colors[2], secondT) with
        {
            A = Mathf.Lerp(alpha[1], alpha[2], secondT) / 255f
        };
    }

    private static float ParticleScale(War3ParticleEmitterDefinition definition, float progress)
    {
        var scaling = definition.Scaling.Length >= 3 ? definition.Scaling : [1f, 1f, 1f];
        if (progress <= definition.Time)
        {
            var t = definition.Time <= 0d ? 1f : progress / (float)definition.Time;
            return Mathf.Lerp(scaling[0], scaling[1], t);
        }
        var t2 = (float)Math.Clamp((progress - definition.Time) /
                                   Math.Max(0.001d, 1d - definition.Time), 0d, 1d);
        return Mathf.Lerp(scaling[1], scaling[2], t2);
    }

    private static (Vector2 Start, Vector2 End) ParticleUv(
        War3ParticleEmitterDefinition definition,
        float progress)
    {
        var firstHalf = progress <= definition.Time;
        var animation = firstHalf
            ? definition.LifeSpanUv
            : definition.DecayUv;
        var start = animation.Length > 0 ? animation[0] : 0;
        var end = animation.Length > 1 ? animation[1] : 0;
        var phaseProgress = firstHalf
            ? definition.Time <= 0d
                ? 1f
                : progress / (float)definition.Time
            : (float)Math.Clamp(
                (progress - definition.Time) /
                Math.Max(0.001d, 1d - definition.Time), 0d, 1d);
        var frame = (int)MathF.Round(Mathf.Lerp(start, end, phaseProgress));
        frame = Math.Clamp(frame, Math.Min(start, end), Math.Max(start, end));
        var column = frame % definition.Columns;
        var row = frame / definition.Columns;
        return (
            new Vector2(column / (float)definition.Columns, row / (float)definition.Rows),
            new Vector2((column + 1f) / definition.Columns, (row + 1f) / definition.Rows));
    }

    private StandardMaterial3D CreateParticleMaterial(
        War3ModelMetadata metadata,
        War3ParticleEmitterDefinition definition)
    {
        var material = CreateEffectMaterial(
            ParticleBlendMode(definition.FilterMode));
        material.AlbedoTexture = ResolveParticleTexture(metadata, definition);
        return material;
    }

    private StandardMaterial3D CreateRibbonMaterial(
        War3ModelMetadata metadata,
        War3RibbonEmitterDefinition definition)
    {
        var filterMode = 1;
        Texture2D? texture = null;
        if (definition.MaterialId >= 0 && definition.MaterialId < metadata.Materials.Count)
        {
            var layer = metadata.Materials[definition.MaterialId].Layers.FirstOrDefault();
            if (layer is not null)
            {
                filterMode = layer.FilterMode;
                var textureId = (int)Math.Round(layer.TextureId.Constant);
                texture = ResolveTexture(metadata, textureId, 0);
            }
        }
        var material = CreateEffectMaterial(LayerBlendMode(filterMode));
        material.AlbedoTexture = texture;
        return material;
    }

    private static EffectBlendMode ParticleBlendMode(int filterMode) =>
        filterMode switch
        {
            1 => EffectBlendMode.Additive,
            2 => EffectBlendMode.Modulate,
            3 => EffectBlendMode.Modulate2X,
            4 => EffectBlendMode.AlphaKey,
            _ => EffectBlendMode.AlphaBlend
        };

    private static EffectBlendMode LayerBlendMode(int filterMode) =>
        filterMode switch
        {
            3 or 4 => EffectBlendMode.Additive,
            5 => EffectBlendMode.Modulate,
            6 => EffectBlendMode.Modulate2X,
            _ => EffectBlendMode.AlphaBlend
        };

    private static StandardMaterial3D CreateEffectMaterial(
        EffectBlendMode blendMode) => new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = blendMode == EffectBlendMode.AlphaKey
                ? BaseMaterial3D.TransparencyEnum.AlphaScissor
                : BaseMaterial3D.TransparencyEnum.Alpha,
            AlphaScissorThreshold = blendMode == EffectBlendMode.AlphaKey
                ? 0.83f
                : 0.5f,
            BlendMode = blendMode switch
            {
                EffectBlendMode.Additive => BaseMaterial3D.BlendModeEnum.Add,
                EffectBlendMode.Modulate or EffectBlendMode.Modulate2X =>
                    BaseMaterial3D.BlendModeEnum.Mul,
                _ => BaseMaterial3D.BlendModeEnum.Mix
            },
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = Colors.White
        };

    private Texture2D? ResolveParticleTexture(
        War3ModelMetadata metadata,
        War3ParticleEmitterDefinition definition) =>
        ResolveTexture(metadata, definition.TextureId, definition.ReplaceableId);

    private Texture2D? ResolveTexture(
        War3ModelMetadata metadata,
        int textureId,
        int fallbackReplaceableId)
    {
        if (textureId < 0 || textureId >= metadata.Textures.Count) return null;
        var definition = metadata.Textures[textureId];
        var replaceableId = definition.ReplaceableId != 0
            ? definition.ReplaceableId
            : fallbackReplaceableId;
        var image = definition.Image;
        if (replaceableId == 1) image = $"ReplaceableTextures\\TeamColor\\TeamColor{_teamColor:00}.blp";
        if (replaceableId == 2) image = $"ReplaceableTextures\\TeamGlow\\TeamGlow{_teamColor:00}.blp";
        if (image.Length == 0) return null;
        var relative = image.Replace('\\', '/');
        relative = Path.ChangeExtension(relative, ".png").Replace('\\', '/');
        var path = War3AssetPack.AbsolutePath($"textures/{relative}");
        if (TextureCache.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path))
        {
            TextureCache[path] = null;
            return null;
        }
        var loaded = Image.LoadFromFile(path);
        var texture = loaded is null || loaded.IsEmpty()
            ? null
            : ImageTexture.CreateFromImage(loaded);
        TextureCache[path] = texture;
        return texture;
    }

    private static MeshInstance3D CreateMeshVisual(string name) => new()
    {
        Name = $"{name}_Runtime",
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        ExtraCullMargin = 1000f
    };

    private static EmitterBinding? FindEmitterBinding(Node root, int objectId, string sourceName)
    {
        var suffix = $"_{objectId}";
        var spatialNodes = new List<Node3D>();
        var skeletons = new List<Skeleton3D>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Node3D spatial) spatialNodes.Add(spatial);
            if (node is Skeleton3D skeleton) skeletons.Add(skeleton);
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
        var byObjectId = spatialNodes.FirstOrDefault(node => node.Name.ToString()
            .EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (byObjectId is not null) return new EmitterBinding(byObjectId);
        foreach (var skeleton in skeletons)
        {
            for (var bone = 0; bone < skeleton.GetBoneCount(); bone++)
            {
                if (skeleton.GetBoneName(bone).ToString()
                    .EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return new EmitterBinding(skeleton, bone);
            }
        }
        var byName = spatialNodes.FirstOrDefault(node => node.Name.ToString()
            .Contains(sourceName, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return new EmitterBinding(byName);
        foreach (var skeleton in skeletons)
        {
            for (var bone = 0; bone < skeleton.GetBoneCount(); bone++)
            {
                if (skeleton.GetBoneName(bone).ToString()
                    .Contains(sourceName, StringComparison.OrdinalIgnoreCase))
                    return new EmitterBinding(skeleton, bone);
            }
        }
        return null;
    }

    private static float WorldScale(Basis basis)
    {
        return (basis.X.Length() + basis.Y.Length() + basis.Z.Length()) / 3f;
    }

    private void ClearRuntime()
    {
        foreach (var emitter in _particles)
            if (emitter.Batch is not null)
                emitter.Batch.Dispose();
            else if (emitter.Instance.IsValid)
                RenderingServer.FreeRid(emitter.Instance);
        foreach (var emitter in _ribbons)
            if (emitter.Instance.IsValid)
                RenderingServer.FreeRid(emitter.Instance);
        if (_legacyHost is not null)
            foreach (var child in _legacyHost.GetChildren()) child.QueueFree();
        _particles.Clear();
        _ribbons.Clear();
        _metadata = null;
        _sequence = null;
        _nodeFree = false;
        _legacyHost = null;
        _hasRebuildBasis = false;
        _geometryDirty = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearRuntime();
    }

    private double RandomRange(double minimum, double maximum)
    {
        // Effect randomness is presentation-only. Keep it local so hundreds
        // of emitters do not cross the managed/native boundary or perturb
        // Godot's global random stream for every spawned particle.
        var value = _randomState;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        _randomState = value;
        var unit = (value >> 8) * (1d / 16_777_216d);
        return minimum + unit * (maximum - minimum);
    }

    /// <summary>
    /// Reuses managed vertex arrays while reducing a dynamic emitter rebuild
    /// from three Godot interop calls per vertex to one surface submission.
    /// </summary>
    private sealed class EffectGeometryBuffer
    {
        private const int MaximumPooledSetsPerVertexCount = 4;
        private static readonly Lock PoolLock = new();
        private static readonly Dictionary<int, Stack<EffectGeometryArrays>>
            ArrayPool = [];
        private readonly Godot.Collections.Array _surfaceArrays = [];
        private EffectGeometryArrays? _arrays;
        private int _writeIndex;

        public EffectGeometryBuffer() =>
            _surfaceArrays.Resize((int)Mesh.ArrayType.Max);

        public void Prepare(int vertexCount)
        {
            if (_arrays is not null)
                throw new InvalidOperationException(
                    "Effect geometry buffer was prepared twice.");
            _arrays = Rent(vertexCount);
            _writeIndex = 0;
        }

        public void Add(Vector3 vertex, Color color, Vector2 uv)
        {
            var arrays = _arrays ?? throw new InvalidOperationException(
                "Effect geometry buffer was not prepared.");
            arrays.Vertices[_writeIndex] = vertex;
            arrays.Colors[_writeIndex] = color;
            arrays.Uvs[_writeIndex] = uv;
            _writeIndex++;
        }

        public void Commit(ArrayMesh mesh)
        {
            var arrays = _arrays ?? throw new InvalidOperationException(
                "Effect geometry buffer was not prepared.");
            if (_writeIndex != arrays.Vertices.Length)
                throw new InvalidOperationException(
                    "Effect geometry vertex count changed during rebuild.");
            _surfaceArrays[(int)Mesh.ArrayType.Vertex] = arrays.Vertices;
            _surfaceArrays[(int)Mesh.ArrayType.Color] = arrays.Colors;
            _surfaceArrays[(int)Mesh.ArrayType.TexUV] = arrays.Uvs;
            try
            {
                mesh.AddSurfaceFromArrays(
                    Mesh.PrimitiveType.Triangles, _surfaceArrays);
            }
            finally
            {
                _arrays = null;
                Return(arrays);
            }
        }

        private static EffectGeometryArrays Rent(int vertexCount)
        {
            lock (PoolLock)
            {
                if (ArrayPool.TryGetValue(vertexCount, out var available) &&
                    available.TryPop(out var arrays))
                    return arrays;
            }
            return new EffectGeometryArrays(vertexCount);
        }

        private static void Return(EffectGeometryArrays arrays)
        {
            lock (PoolLock)
            {
                if (!ArrayPool.TryGetValue(
                        arrays.Vertices.Length, out var available))
                {
                    available = [];
                    ArrayPool.Add(arrays.Vertices.Length, available);
                }
                if (available.Count < MaximumPooledSetsPerVertexCount)
                    available.Push(arrays);
            }
        }

        private sealed class EffectGeometryArrays(int vertexCount)
        {
            public Vector3[] Vertices { get; } = new Vector3[vertexCount];
            public Color[] Colors { get; } = new Color[vertexCount];
            public Vector2[] Uvs { get; } = new Vector2[vertexCount];
        }
    }

    /// <summary>
    /// Node-free particle geometry is a stable quad MultiMesh. Particle state
    /// is uploaded as one contiguous transform/color/UV buffer, avoiding an
    /// ArrayMesh surface teardown plus three packed-array marshals per emitter.
    /// </summary>
    private sealed class NodeFreeParticleBatch : IDisposable
    {
        private const int BufferStride = 20;
        private const float HiddenScale = 0.000001f;
        private static readonly Dictionary<EffectBlendMode, Shader> Shaders = [];
        private readonly MultiMesh _multiMesh;
        private readonly ShaderMaterial _material;
        private float[] _buffer = [];
        private int _capacity;
        private int _writeIndex;
        private int _visibleCount = -1;
        private bool _disposed;

        public NodeFreeParticleBatch(
            Rid scenario,
            StandardMaterial3D source,
            EffectBlendMode blendMode)
        {
            _material = new ShaderMaterial
            {
                Shader = ResolveShader(blendMode)
            };
            SetTexture(source.AlbedoTexture);
            Mesh = CreateQuadMesh(_material);
            _multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = Mesh,
                InstanceCount = 0
            };
            Instance = RenderingServer.InstanceCreate2(
                _multiMesh.GetRid(), scenario);
            RenderingServer.InstanceGeometrySetCastShadowsSetting(
                Instance, RenderingServer.ShadowCastingSetting.Off);
            RenderingServer.InstanceSetIgnoreCulling(Instance, true);
        }

        public ArrayMesh Mesh { get; }
        public Rid Instance { get; private set; }

        public void SetTexture(Texture2D? texture)
        {
            _material.SetShaderParameter(
                "has_albedo_texture", texture is not null);
            if (texture is not null)
                _material.SetShaderParameter("albedo_texture", texture);
        }

        public void Prepare(int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureCapacity(count);
            _writeIndex = 0;
        }

        public void Add(
            Transform3D transform,
            Color color,
            Vector2 uv0,
            Vector2 uv1)
        {
            var offset = _writeIndex * BufferStride;
            WriteTransform(_buffer, offset, transform);
            _buffer[offset + 12] = color.R;
            _buffer[offset + 13] = color.G;
            _buffer[offset + 14] = color.B;
            _buffer[offset + 15] = color.A;
            _buffer[offset + 16] = uv0.X;
            _buffer[offset + 17] = uv0.Y;
            _buffer[offset + 18] = uv1.X;
            _buffer[offset + 19] = uv1.Y;
            _writeIndex++;
        }

        public void Commit()
        {
            if (_writeIndex != _visibleCount)
            {
                _multiMesh.VisibleInstanceCount = _writeIndex;
                _visibleCount = _writeIndex;
            }
            if (_writeIndex <= 0) return;
            RenderingServer.MultimeshSetBuffer(
                _multiMesh.GetRid(), _buffer.AsSpan());
        }

        public void Reset()
        {
            _writeIndex = 0;
            if (_visibleCount == 0) return;
            _multiMesh.VisibleInstanceCount = 0;
            _visibleCount = 0;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _capacity) return;
            var capacity = Math.Max(8, _capacity);
            while (capacity < required) capacity *= 2;
            Array.Resize(ref _buffer, capacity * BufferStride);
            for (var index = _capacity; index < capacity; index++)
            {
                var offset = index * BufferStride;
                WriteTransform(_buffer, offset, new Transform3D(
                    Basis.FromScale(Vector3.One * HiddenScale),
                    Vector3.Zero));
            }
            _capacity = capacity;
            _multiMesh.InstanceCount = capacity;
            _multiMesh.VisibleInstanceCount = Math.Max(0, _writeIndex);
            _visibleCount = _writeIndex;
        }

        private static void WriteTransform(
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

        private static ArrayMesh CreateQuadMesh(ShaderMaterial material)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = new Vector3[]
            {
                new(-1f, -1f, 0f),
                new(1f, -1f, 0f),
                new(1f, 1f, 0f),
                new(-1f, 1f, 0f)
            };
            arrays[(int)Godot.Mesh.ArrayType.Normal] = new Vector3[]
            {
                Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back
            };
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = new Vector2[]
            {
                new(0f, 1f),
                new(1f, 1f),
                new(1f, 0f),
                new(0f, 0f)
            };
            arrays[(int)Godot.Mesh.ArrayType.Index] = new int[]
            {
                0, 1, 2, 0, 2, 3
            };
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(
                Godot.Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, material);
            return mesh;
        }

        private static Shader ResolveShader(EffectBlendMode blendMode)
        {
            if (Shaders.TryGetValue(blendMode, out var cached)) return cached;
            var renderBlend = blendMode switch
            {
                EffectBlendMode.Additive => "blend_add",
                EffectBlendMode.Modulate => "blend_mix",
                EffectBlendMode.Modulate2X => "blend_mul",
                _ => "blend_mix"
            };
            var fragmentOutput = blendMode switch
            {
                EffectBlendMode.Modulate =>
                    "float modulation = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));\n                        float coverage = clamp(color.a * (1.0 - modulation), 0.0, 1.0);\n                        if (coverage < 0.002) discard;\n                        ALBEDO = vec3(0.0);\n                        ALPHA = coverage;",
                EffectBlendMode.Modulate2X =>
                    "ALBEDO = mix(vec3(1.0), color.rgb * 2.0, color.a);",
                EffectBlendMode.AlphaKey =>
                    "if (color.a < 0.83) discard;\n                        ALBEDO = color.rgb;",
                _ => "ALBEDO = color.rgb;\n                        ALPHA = color.a;"
            };
            // Warcraft's Modulate textures were authored for a fixed-function
            // gamma-space path. Keep their encoded attenuation values; the
            // Modulate branch above converts grayscale darkening plus the PNG
            // mask into ordinary alpha coverage. Godot's blend_mul does not
            // use alpha as coverage, so feeding the mask to it directly makes
            // overlapping particles expose their whole quad silhouettes.
            var textureHints = blendMode is
                EffectBlendMode.Modulate or EffectBlendMode.Modulate2X
                    ? "filter_linear_mipmap, repeat_enable"
                    : "source_color, filter_linear_mipmap, repeat_enable";
            var shader = new Shader
            {
                Code = $$"""
                    shader_type spatial;
                    render_mode {{renderBlend}}, depth_draw_never,
                        cull_disabled, unshaded;

                    uniform sampler2D albedo_texture : {{textureHints}};
                    uniform bool has_albedo_texture = false;
                    varying vec2 effect_uv;

                    void vertex() {
                        effect_uv = mix(
                            INSTANCE_CUSTOM.xy,
                            INSTANCE_CUSTOM.zw,
                            UV);
                    }

                    void fragment() {
                        vec4 texel = has_albedo_texture
                            ? texture(albedo_texture, effect_uv)
                            : vec4(1.0);
                        vec4 color = texel * COLOR;
                        {{fragmentOutput}}
                    }
                    """
            };
            Shaders.Add(blendMode, shader);
            return shader;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Instance.IsValid) RenderingServer.FreeRid(Instance);
            Instance = default;
            _buffer = [];
        }
    }

    private sealed class ParticleEmitterRuntime(
        War3ParticleEmitterDefinition definition,
        EmitterBinding? binding,
        MeshInstance3D? nodeVisual,
        Rid instance,
        StandardMaterial3D material,
        ArrayMesh? mesh = null,
        NodeFreeParticleBatch? batch = null)
    {
        public War3ParticleEmitterDefinition Definition { get; } = definition;
        public EmitterBinding? Binding { get; } = binding;
        public MeshInstance3D? NodeVisual { get; } = nodeVisual;
        public Rid Instance { get; } = instance;
        public ArrayMesh Mesh { get; } = mesh ?? new ArrayMesh();
        public EffectGeometryBuffer Geometry { get; } = new();
        public StandardMaterial3D Material { get; } = material;
        public NodeFreeParticleBatch? Batch { get; } = batch;
        public List<Particle> Particles { get; } = [];
        public double SpawnRemainder { get; set; }
        public double? LastSquirtFrame { get; set; }
        public bool HasSurface { get; set; }
    }

    private sealed class RibbonEmitterRuntime(
        War3RibbonEmitterDefinition definition,
        EmitterBinding? binding,
        MeshInstance3D? nodeVisual,
        Rid instance,
        StandardMaterial3D material,
        ArrayMesh? mesh = null)
    {
        public War3RibbonEmitterDefinition Definition { get; } = definition;
        public EmitterBinding? Binding { get; } = binding;
        public MeshInstance3D? NodeVisual { get; } = nodeVisual;
        public Rid Instance { get; } = instance;
        public ArrayMesh Mesh { get; } = mesh ?? new ArrayMesh();
        public EffectGeometryBuffer Geometry { get; } = new();
        public StandardMaterial3D Material { get; } = material;
        public List<RibbonPoint> Points { get; } = [];
        public double SpawnRemainder { get; set; }
        public bool HasSurface { get; set; }
    }

    private sealed class EmitterBinding
    {
        private readonly Node3D? _node;
        private readonly Skeleton3D? _skeleton;
        private readonly Func<Transform3D>? _external;
        private readonly int _boneIndex = -1;

        public EmitterBinding(Node3D node) => _node = node;

        public EmitterBinding(Func<Transform3D> external) =>
            _external = external;

        public EmitterBinding(Skeleton3D skeleton, int boneIndex)
        {
            _skeleton = skeleton;
            _boneIndex = boneIndex;
        }

        public Transform3D ResolveGlobalTransform(
            ref Skeleton3D? cachedSkeleton,
            ref Transform3D cachedSkeletonTransform)
        {
            if (_external is not null) return _external();
            if (_node is not null) return _node.GlobalTransform;
            if (!ReferenceEquals(cachedSkeleton, _skeleton))
            {
                cachedSkeleton = _skeleton;
                cachedSkeletonTransform = _skeleton!.GlobalTransform;
            }
            return cachedSkeletonTransform *
                   _skeleton!.GetBoneGlobalPose(_boneIndex);
        }
    }

    private struct Particle
    {
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Gravity { get; set; }
        public double Life { get; set; }
        public double Age { get; set; }
        public float Rotation { get; set; }
    }

    private struct RibbonPoint
    {
        public Vector3 Center { get; set; }
        public Vector3 Above { get; set; }
        public Vector3 Below { get; set; }
        public float Alpha { get; set; }
        public double Age { get; set; }
    }
}

/// <summary>
/// SceneTree adapter used only by the asset lab, portraits, and other singular
/// preview surfaces. Battlefield actors own <see cref="War3EffectRuntimeCore"/>
/// directly and therefore do not allocate a Godot Object or Node per entity.
/// </summary>
public sealed partial class War3EffectRuntime : Node3D
{
    private readonly War3EffectRuntimeCore _core = new();

    public int LiveParticleCount => _core.LiveParticleCount;
    public int LiveRibbonPointCount => _core.LiveRibbonPointCount;
    public int ActiveParticleEmitterCount => _core.ActiveParticleEmitterCount;
    public int ActiveRibbonEmitterCount => _core.ActiveRibbonEmitterCount;
    public int ResolvedEmitterCount => _core.ResolvedEmitterCount;

    public static void PrepareCameraFrame(Camera3D camera, Basis basis) =>
        War3EffectRuntimeCore.PrepareCameraFrame(camera, basis);

    public void Initialize(
        Node modelRoot,
        Camera3D camera,
        War3ModelMetadata metadata) =>
        _core.Initialize(modelRoot, camera, metadata, this);

    public void PrepareWorldBasis(Basis basis) =>
        _core.PrepareWorldBasis(basis);

    public void PrepareWorldTransform(Transform3D transform) =>
        _core.PrepareWorldTransform(transform);

    public void SetTeamColor(int teamColor) =>
        _core.SetTeamColor(teamColor);

    public void SetSequence(int sequenceIndex) =>
        _core.SetSequence(sequenceIndex);

    public void Reset() => _core.Reset();

    public void Sync(
        double localMilliseconds,
        Action<double>? sampleAnimation = null) =>
        _core.Sync(localMilliseconds, sampleAnimation);

    public void Seek(
        double localMilliseconds,
        Action<double>? sampleAnimation = null) =>
        _core.Seek(localMilliseconds, sampleAnimation);

    public override void _ExitTree() => _core.Dispose();
}
