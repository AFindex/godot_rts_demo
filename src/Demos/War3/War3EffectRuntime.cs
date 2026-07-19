using Godot;

namespace RtsDemo.Demos.War3;

/// <summary>
/// Reconstructs Warcraft III ParticleEmitter2 and RibbonEmitter data that glTF
/// cannot represent. The simulation is intentionally CPU based so seeking and
/// frame-by-frame inspection stay deterministic in the asset lab.
/// </summary>
public sealed partial class War3EffectRuntime : Node3D
{
    private const double FixedStepMilliseconds = 1000d / 30d;
    private const float WarcraftUnitScale = 0.01f;
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
    private double _accumulatorMilliseconds;
    private int _teamColor;
    private uint _randomState = 0x9E3779B9u ^
        (uint)Interlocked.Increment(ref _nextRandomSeed) * 0x85EBCA6Bu;
    private Basis _preparedWorldBasis;
    private Transform3D _preparedWorldInverse;
    private bool _hasPreparedWorldBasis;
    private bool _externalWorldTransformPrepared;

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
        _preparedWorldBasis = transform.Basis;
        _preparedWorldInverse = transform.AffineInverse();
        _hasPreparedWorldBasis = true;
        _externalWorldTransformPrepared = true;
    }

    public void Initialize(Node modelRoot, Camera3D camera, War3ModelMetadata metadata)
    {
        ClearRuntime();
        _metadata = metadata;
        _camera = camera;

        foreach (var definition in metadata.Particles)
        {
            var runtime = new ParticleEmitterRuntime(
                definition,
                FindEmitterBinding(modelRoot, definition.ObjectId, definition.Name),
                CreateMeshVisual(definition.Name),
                CreateParticleMaterial(metadata, definition));
            runtime.Visual.Mesh = runtime.Mesh;
            runtime.Visual.MaterialOverride = runtime.Material;
            AddChild(runtime.Visual);
            _particles.Add(runtime);
        }

        foreach (var definition in metadata.Ribbons)
        {
            var runtime = new RibbonEmitterRuntime(
                definition,
                FindEmitterBinding(modelRoot, definition.ObjectId, definition.Name),
                CreateMeshVisual(definition.Name),
                CreateRibbonMaterial(metadata, definition));
            runtime.Visual.Mesh = runtime.Mesh;
            runtime.Visual.MaterialOverride = runtime.Material;
            AddChild(runtime.Visual);
            _ribbons.Add(runtime);
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
            emitter.Material.AlbedoTexture = ResolveParticleTexture(_metadata!, emitter.Definition);
        }
    }

    public void Reset()
    {
        _simulatedMilliseconds = 0d;
        _accumulatorMilliseconds = 0d;
        foreach (var emitter in _particles)
        {
            emitter.Particles.Clear();
            emitter.SpawnRemainder = 0d;
            emitter.LastSquirtFrame = null;
            emitter.Mesh.ClearSurfaces();
            emitter.HasSurface = false;
        }
        foreach (var emitter in _ribbons)
        {
            emitter.Points.Clear();
            emitter.SpawnRemainder = 0d;
            emitter.Mesh.ClearSurfaces();
            emitter.HasSurface = false;
        }
    }

    public void Sync(
        double localMilliseconds,
        Action<double>? sampleAnimation = null)
    {
        if (_sequence is null || _metadata is null || _camera is null) return;
        PrepareWorldTransformForSync();
        localMilliseconds = Math.Clamp(localMilliseconds, 0d, _sequence.DurationMilliseconds);
        if (localMilliseconds + 0.5d < _simulatedMilliseconds) Reset();
        var delta = localMilliseconds - _simulatedMilliseconds;
        if (delta <= 0d)
        {
            RebuildMeshes();
            return;
        }

        _accumulatorMilliseconds += delta;
        while (_accumulatorMilliseconds >= FixedStepMilliseconds)
        {
            _simulatedMilliseconds += FixedStepMilliseconds;
            sampleAnimation?.Invoke(_simulatedMilliseconds);
            SimulateStep(FixedStepMilliseconds / 1000d);
            _accumulatorMilliseconds -= FixedStepMilliseconds;
        }
        _simulatedMilliseconds = localMilliseconds;
        sampleAnimation?.Invoke(localMilliseconds);
        RebuildMeshes();
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
        _simulatedMilliseconds = target;
        sampleAnimation?.Invoke(target);
        RebuildMeshes();
    }

    private void SimulateStep(double deltaSeconds)
    {
        if (_metadata is null || _sequence is null) return;
        var frame = _sequence.StartFrame + _simulatedMilliseconds;

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
                var emitterTransform = runtime.Binding.GlobalTransform;
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
                var emitterTransform = runtime.Binding.GlobalTransform;
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
        var origin = ToEffectLocal(emitterTransform * localOffset);
        var worldScale = WorldScale(emitterTransform.Basis);
        runtime.Particles.Add(new Particle
        {
            Position = origin,
            Velocity = direction * (float)speed * worldScale,
            Gravity = Vector3.Down *
                      (float)definition.Gravity.Sample(frame, _sequence!, _metadata.GlobalSequences) *
                      worldScale,
            Life = Math.Max(0.03d, definition.LifeSpan),
            Rotation = (float)RandomRange(-Math.PI, Math.PI)
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
            : ToLocal(globalPosition);

    private void PrepareWorldTransformForSync()
    {
        if (!_externalWorldTransformPrepared)
        {
            var transform = GlobalTransform;
            _preparedWorldBasis = transform.Basis;
            _preparedWorldInverse = transform.AffineInverse();
            _hasPreparedWorldBasis = true;
        }
        _externalWorldTransformPrepared = false;
    }

    private void RebuildMeshes()
    {
        if (_camera is null) return;
        var cameraBasis = ReferenceEquals(_camera, _preparedCamera)
            ? _preparedCameraBasis
            : _camera.GlobalBasis;
        var worldBasis = _hasPreparedWorldBasis
            ? _preparedWorldBasis
            : GlobalBasis;
        var inverseBasis = worldBasis.Inverse();
        var right = inverseBasis * cameraBasis.X.Normalized();
        var up = inverseBasis * cameraBasis.Y.Normalized();
        foreach (var runtime in _particles) RebuildParticleMesh(runtime, right, up);
        foreach (var runtime in _ribbons) RebuildRibbonMesh(runtime);
    }

    private static void RebuildParticleMesh(
        ParticleEmitterRuntime runtime,
        Vector3 cameraRight,
        Vector3 cameraUp)
    {
        if (runtime.Particles.Count == 0)
        {
            if (runtime.HasSurface)
            {
                runtime.Mesh.ClearSurfaces();
                runtime.HasSurface = false;
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
            if (runtime.HasSurface) runtime.Mesh.ClearSurfaces();
            runtime.HasSurface = false;
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
        var emitterWorldScale = WorldScale(runtime.Binding);
        foreach (var particle in runtime.Particles)
        {
            var progress = (float)Math.Clamp(particle.Age / particle.Life, 0d, 1d);
            var color = ParticleColor(runtime.Definition, progress);
            var size = ParticleScale(runtime.Definition, progress) *
                       emitterWorldScale;
            var (uv0, uv1) = ParticleUv(runtime.Definition, progress);
            if ((runtime.Definition.FrameFlags & 1) != 0 || runtime.Definition.FrameFlags == 0)
            {
                var cos = MathF.Cos(particle.Rotation);
                var sin = MathF.Sin(particle.Rotation);
                var baseRight = (runtime.Definition.Flags & 1048576) != 0
                    ? Vector3.Right
                    : cameraRight;
                var baseUp = (runtime.Definition.Flags & 1048576) != 0
                    ? Vector3.Up
                    : cameraUp;
                var rotatedRight = baseRight * cos + baseUp * sin;
                var rotatedUp = -baseRight * sin + baseUp * cos;
                AppendQuad(runtime.Geometry, particle.Position, rotatedRight * size,
                    rotatedUp * size, color, uv0, uv1);
            }
            if ((runtime.Definition.FrameFlags & 2) != 0 && particle.Velocity.LengthSquared() > 0.00001f)
            {
                var tail = particle.Velocity * (float)runtime.Definition.TailLength;
                var side = tail.Cross(cameraUp).Normalized() * Math.Max(0.004f, size * 0.45f);
                AppendTrailQuad(runtime.Geometry, particle.Position, particle.Position - tail,
                    side, color, uv0, uv1);
            }
        }
        runtime.Geometry.Commit(runtime.Mesh);
        runtime.HasSurface = true;
    }

    private static void RebuildRibbonMesh(RibbonEmitterRuntime runtime)
    {
        if (runtime.Points.Count < 2)
        {
            if (runtime.HasSurface)
            {
                runtime.Mesh.ClearSurfaces();
                runtime.HasSurface = false;
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
        runtime.HasSurface = true;
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
        var animation = progress <= definition.Time
            ? definition.LifeSpanUv
            : definition.DecayUv;
        var start = animation.Length > 0 ? animation[0] : 0;
        var end = animation.Length > 1 ? animation[1] : 0;
        var repeat = Math.Max(1, animation.Length > 2 ? animation[2] : 0);
        var frame = start + (int)Math.Floor((end - start + 1) * repeat * progress);
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
        var material = CreateEffectMaterial(definition.FilterMode);
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
        var material = CreateEffectMaterial(filterMode);
        material.AlbedoTexture = texture;
        return material;
    }

    private static StandardMaterial3D CreateEffectMaterial(int filterMode) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = filterMode switch
        {
            1 or 3 or 4 or 5 => BaseMaterial3D.BlendModeEnum.Add,
            2 or 6 => BaseMaterial3D.BlendModeEnum.Mul,
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

    private static float WorldScale(EmitterBinding? binding)
    {
        if (binding is null) return WarcraftUnitScale;
        return WorldScale(binding.GlobalBasis);
    }

    private static float WorldScale(Basis basis)
    {
        return (basis.X.Length() + basis.Y.Length() + basis.Z.Length()) / 3f;
    }

    private void ClearRuntime()
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _particles.Clear();
        _ribbons.Clear();
        _metadata = null;
        _sequence = null;
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
        private readonly Godot.Collections.Array _surfaceArrays = [];
        private Vector3[] _vertices = [];
        private Color[] _colors = [];
        private Vector2[] _uvs = [];
        private int _writeIndex;

        public EffectGeometryBuffer() =>
            _surfaceArrays.Resize((int)Mesh.ArrayType.Max);

        public void Prepare(int vertexCount)
        {
            if (_vertices.Length != vertexCount)
            {
                _vertices = new Vector3[vertexCount];
                _colors = new Color[vertexCount];
                _uvs = new Vector2[vertexCount];
            }
            _writeIndex = 0;
        }

        public void Add(Vector3 vertex, Color color, Vector2 uv)
        {
            _vertices[_writeIndex] = vertex;
            _colors[_writeIndex] = color;
            _uvs[_writeIndex] = uv;
            _writeIndex++;
        }

        public void Commit(ArrayMesh mesh)
        {
            if (_writeIndex != _vertices.Length)
                throw new InvalidOperationException(
                    "Effect geometry vertex count changed during rebuild.");
            _surfaceArrays[(int)Mesh.ArrayType.Vertex] = _vertices;
            _surfaceArrays[(int)Mesh.ArrayType.Color] = _colors;
            _surfaceArrays[(int)Mesh.ArrayType.TexUV] = _uvs;
            mesh.AddSurfaceFromArrays(
                Mesh.PrimitiveType.Triangles, _surfaceArrays);
        }
    }

    private sealed class ParticleEmitterRuntime(
        War3ParticleEmitterDefinition definition,
        EmitterBinding? binding,
        MeshInstance3D visual,
        StandardMaterial3D material)
    {
        public War3ParticleEmitterDefinition Definition { get; } = definition;
        public EmitterBinding? Binding { get; } = binding;
        public MeshInstance3D Visual { get; } = visual;
        public ArrayMesh Mesh { get; } = new();
        public EffectGeometryBuffer Geometry { get; } = new();
        public StandardMaterial3D Material { get; } = material;
        public List<Particle> Particles { get; } = [];
        public double SpawnRemainder { get; set; }
        public double? LastSquirtFrame { get; set; }
        public bool HasSurface { get; set; }
    }

    private sealed class RibbonEmitterRuntime(
        War3RibbonEmitterDefinition definition,
        EmitterBinding? binding,
        MeshInstance3D visual,
        StandardMaterial3D material)
    {
        public War3RibbonEmitterDefinition Definition { get; } = definition;
        public EmitterBinding? Binding { get; } = binding;
        public MeshInstance3D Visual { get; } = visual;
        public ArrayMesh Mesh { get; } = new();
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
        private readonly int _boneIndex = -1;

        public EmitterBinding(Node3D node) => _node = node;

        public EmitterBinding(Skeleton3D skeleton, int boneIndex)
        {
            _skeleton = skeleton;
            _boneIndex = boneIndex;
        }

        public Transform3D GlobalTransform => _node is not null
            ? _node.GlobalTransform
            : _skeleton!.GlobalTransform * _skeleton.GetBoneGlobalPose(_boneIndex);

        public Basis GlobalBasis => GlobalTransform.Basis;
    }

    private sealed class Particle
    {
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Gravity { get; set; }
        public double Life { get; set; }
        public double Age { get; set; }
        public float Rotation { get; set; }
    }

    private sealed class RibbonPoint
    {
        public Vector3 Center { get; set; }
        public Vector3 Above { get; set; }
        public Vector3 Below { get; set; }
        public float Alpha { get; set; } = 1f;
        public double Age { get; set; }
    }
}
