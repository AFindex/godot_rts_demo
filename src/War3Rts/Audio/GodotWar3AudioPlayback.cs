using Godot;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts.Audio;

/// <summary>
/// Godot-only implementation of the playback boundary. It owns a bounded pool
/// of players, while cue selection and gameplay mapping stay in pure C#.
/// </summary>
public sealed partial class GodotWar3AudioPlayback : Node3D, IWar3AudioPlayback
{
    public const int DefaultNonPositionalVoiceCount = 10;
    public const int DefaultWorldVoiceCount = 40;

    private readonly List<FlatVoice> _flatVoices = [];
    private readonly List<WorldVoice> _worldVoices = [];
    private readonly Dictionary<string, AudioStream> _streamCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<War3AudioLoopHandle, VoiceReference> _loops = [];
    private readonly War3AudioAdmissionPolicy _admission = new();
    private Func<NVector2, Vector3>? _toWorld;
    private Func<Vector3>? _listenerWorldPosition;
    private ulong _serial;
    private long _played;
    private long _suppressed;
    private long _dropped;
    private long _culled;
    private long _missing;

    public void Initialize(
        Func<NVector2, Vector3> toWorld,
        War3AudioMixSettings settings,
        Func<Vector3>? listenerWorldPosition = null,
        int nonPositionalVoiceCount = DefaultNonPositionalVoiceCount,
        int worldVoiceCount = DefaultWorldVoiceCount)
    {
        ArgumentNullException.ThrowIfNull(toWorld);
        if (_flatVoices.Count > 0 || _worldVoices.Count > 0)
            throw new InvalidOperationException("War3 audio playback was already initialized.");
        if (nonPositionalVoiceCount <= 0 || worldVoiceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(worldVoiceCount));

        _toWorld = toWorld;
        _listenerWorldPosition = listenerWorldPosition;
        War3AudioBusInstaller.EnsureAndApply(settings);
        for (var index = 0; index < nonPositionalVoiceCount; index++)
        {
            var player = new AudioStreamPlayer
            {
                Name = $"FlatVoice{index}",
                MaxPolyphony = 1
            };
            AddChild(player);
            _flatVoices.Add(new FlatVoice(player));
        }
        for (var index = 0; index < worldVoiceCount; index++)
        {
            var player = new AudioStreamPlayer3D
            {
                Name = $"WorldVoice{index}",
                MaxPolyphony = 1,
                AttenuationModel =
                    AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled
            };
            AddChild(player);
            _worldVoices.Add(new WorldVoice(player));
        }
    }

    public bool Play(
        in War3ResolvedAudioCue cue,
        in War3AudioCueRequest request)
    {
        if (_toWorld is null) return false;
        if (cue.SpatialMode == War3AudioSpatialMode.World3D &&
            request.WorldPosition is { } position)
        {
            var worldPosition = _toWorld(position);
            if (!IsAudible(worldPosition, cue.CutoffDistance)) return Cull();
            if (!Admit(request)) return Suppress();
            if (!TryLoad(cue.ResourcePath, out var stream)) return false;
            var voice = AcquireWorld(cue.Priority);
            if (voice is null) return Drop();
            Configure(voice, cue, request, stream, worldPosition, loop: false);
            voice.Player.Play();
        }
        else
        {
            if (!Admit(request)) return Suppress();
            if (!TryLoad(cue.ResourcePath, out var stream)) return false;
            var voice = AcquireFlat(cue.Priority);
            if (voice is null) return Drop();
            Configure(voice, cue, request, stream, loop: false);
            voice.Player.Play();
        }
        _played++;
        return true;
    }

    public bool StartLoop(
        in War3ResolvedAudioCue cue,
        in War3AudioCueRequest request,
        out War3AudioLoopHandle handle)
    {
        handle = new War3AudioLoopHandle(request.EmitterId, request.CueId);
        if (!handle.IsValid || _toWorld is null) return false;
        StopLoop(handle);
        Vector3? worldPosition = null;
        if (cue.SpatialMode == War3AudioSpatialMode.World3D &&
            request.WorldPosition is { } position)
        {
            worldPosition = _toWorld(position);
            if (!IsAudible(worldPosition.Value, cue.CutoffDistance)) return Cull();
        }
        if (!Admit(request, applyCooldown: false)) return Suppress();
        if (!TryLoad(cue.ResourcePath, out var source)) return false;
        var stream = LoopingCopy(source);
        if (worldPosition is { } sourcePosition)
        {
            var voice = AcquireWorld(cue.Priority);
            if (voice is null) return Drop();
            Configure(voice, cue, request, stream, sourcePosition, loop: true);
            _loops.Add(handle, new VoiceReference(voice));
            voice.Player.Play();
        }
        else
        {
            var voice = AcquireFlat(cue.Priority);
            if (voice is null) return Drop();
            Configure(voice, cue, request, stream, loop: true);
            _loops.Add(handle, new VoiceReference(voice));
            voice.Player.Play();
        }
        _played++;
        return true;
    }

    public void StopLoop(War3AudioLoopHandle handle, float fadeSeconds = 0f)
    {
        if (!_loops.Remove(handle, out var voice)) return;
        // A bounded, immediate stop is used in the first runtime slice. The
        // handle keeps fade policy out of simulation and allows a tweened
        // implementation to be introduced without changing callers.
        voice.Stop();
    }

    public void StopEmitter(int emitterId, float fadeSeconds = 0f)
    {
        foreach (var handle in _loops.Keys
                     .Where(value => value.EmitterId == emitterId).ToArray())
            StopLoop(handle, fadeSeconds);
    }

    public void StopAll()
    {
        foreach (var value in _flatVoices) value.Stop();
        foreach (var value in _worldVoices) value.Stop();
        _loops.Clear();
        _admission.Reset();
    }

    public War3AudioRuntimeSnapshot Snapshot() => new(
        _streamCache.Count,
        _flatVoices.Count(value => value.Player.Playing),
        _worldVoices.Count(value => value.Player.Playing),
        _loops.Count,
        _played,
        _suppressed,
        _dropped,
        _culled,
        _missing);

    public override void _ExitTree() => StopAll();

    private bool TryLoad(string resourcePath, out AudioStream stream)
    {
        stream = null!;
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            _missing++;
            return false;
        }
        var normalized = resourcePath.Replace('\\', '/');
        if (!normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            normalized = "res://" + normalized.TrimStart('/');
        if (_streamCache.TryGetValue(normalized, out stream!)) return true;
        if (!ResourceLoader.Exists(normalized))
        {
            _missing++;
            return false;
        }
        stream = ResourceLoader.Load<AudioStream>(normalized)!;
        if (stream is null)
        {
            _missing++;
            return false;
        }
        _streamCache.Add(normalized, stream);
        return true;
    }

    private FlatVoice? AcquireFlat(int priority)
    {
        var available = _flatVoices.FirstOrDefault(value => !value.Player.Playing);
        if (available is not null) return available;
        var candidate = _flatVoices.OrderBy(value => value.Priority)
            .ThenBy(value => value.Serial).First();
        return candidate.Priority > priority ? null : candidate;
    }

    private WorldVoice? AcquireWorld(int priority)
    {
        var available = _worldVoices.FirstOrDefault(value => !value.Player.Playing);
        if (available is not null) return available;
        var candidate = _worldVoices.OrderBy(value => value.Priority)
            .ThenBy(value => value.Serial).First();
        return candidate.Priority > priority ? null : candidate;
    }

    private void Configure(
        FlatVoice voice,
        in War3ResolvedAudioCue cue,
        in War3AudioCueRequest request,
        AudioStream stream,
        bool loop)
    {
        RemoveLoopFor(voice);
        voice.Stop();
        voice.Priority = cue.Priority;
        voice.Serial = ++_serial;
        voice.EmitterId = request.EmitterId;
        voice.Semantic = request.Semantic;
        voice.Loop = loop;
        voice.Player.Stream = stream;
        voice.Player.VolumeDb = LinearToDb(cue.VolumeLinear);
        voice.Player.PitchScale = Math.Clamp(cue.PitchScale, 0.05f, 4f);
        voice.Player.Bus = War3AudioBusInstaller.ResolveBus(cue.Bus);
    }

    private void Configure(
        WorldVoice voice,
        in War3ResolvedAudioCue cue,
        in War3AudioCueRequest request,
        AudioStream stream,
        Vector3 worldPosition,
        bool loop)
    {
        RemoveLoopFor(voice);
        voice.Stop();
        voice.Priority = cue.Priority;
        voice.Serial = ++_serial;
        voice.EmitterId = request.EmitterId;
        voice.Semantic = request.Semantic;
        voice.Loop = loop;
        voice.Player.Stream = stream;
        voice.Player.Position = worldPosition;
        voice.Player.VolumeDb = LinearToDb(cue.VolumeLinear);
        voice.Player.PitchScale = Math.Clamp(cue.PitchScale, 0.05f, 4f);
        voice.Player.UnitSize = Math.Max(0.1f, cue.MinimumDistance);
        voice.Player.MaxDistance = Math.Max(
            voice.Player.UnitSize, cue.CutoffDistance);
        voice.Player.Bus = War3AudioBusInstaller.ResolveBus(cue.Bus);
    }

    private void RemoveLoopFor(FlatVoice voice)
    {
        foreach (var handle in _loops.Where(value => value.Value.Flat == voice)
                     .Select(value => value.Key).ToArray())
            _loops.Remove(handle);
    }

    private void RemoveLoopFor(WorldVoice voice)
    {
        foreach (var handle in _loops.Where(value => value.Value.World == voice)
                     .Select(value => value.Key).ToArray())
            _loops.Remove(handle);
    }

    private bool Drop()
    {
        _dropped++;
        return false;
    }

    private bool Suppress()
    {
        _suppressed++;
        return false;
    }

    private bool Cull()
    {
        _culled++;
        return false;
    }

    private bool IsAudible(Vector3 source, float cutoffDistance)
    {
        if (_listenerWorldPosition is null) return true;
        return War3AudioRangePolicy.IsAudible(
            source.DistanceSquaredTo(_listenerWorldPosition()),
            cutoffDistance);
    }

    private bool Admit(
        in War3AudioCueRequest request,
        bool applyCooldown = true) =>
        _admission.TryAdmit(
            request,
            ActiveSemanticVoiceCount(request.Semantic),
            Time.GetTicksMsec(),
            applyCooldown);

    private int ActiveSemanticVoiceCount(War3AudioSemantic semantic) =>
        _flatVoices.Count(value => value.Player.Playing &&
                                   value.Semantic == semantic) +
        _worldVoices.Count(value => value.Player.Playing &&
                                    value.Semantic == semantic);

    private static float LinearToDb(float value) =>
        value <= 0.0001f ? -80f : Mathf.LinearToDb(Math.Clamp(value, 0f, 4f));

    private static AudioStream LoopingCopy(AudioStream source)
    {
        var copy = (AudioStream)source.Duplicate(true);
        switch (copy)
        {
            case AudioStreamWav wav:
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                break;
            case AudioStreamMP3 mp3:
                mp3.Loop = true;
                break;
        }
        return copy;
    }

    private sealed class FlatVoice(AudioStreamPlayer player)
    {
        public AudioStreamPlayer Player { get; } = player;
        public int Priority { get; set; }
        public ulong Serial { get; set; }
        public int EmitterId { get; set; } = -1;
        public War3AudioSemantic Semantic { get; set; }
        public bool Loop { get; set; }

        public void Stop()
        {
            Player.Stop();
            Loop = false;
            EmitterId = -1;
        }
    }

    private sealed class WorldVoice(AudioStreamPlayer3D player)
    {
        public AudioStreamPlayer3D Player { get; } = player;
        public int Priority { get; set; }
        public ulong Serial { get; set; }
        public int EmitterId { get; set; } = -1;
        public War3AudioSemantic Semantic { get; set; }
        public bool Loop { get; set; }

        public void Stop()
        {
            Player.Stop();
            Loop = false;
            EmitterId = -1;
        }
    }

    private readonly record struct VoiceReference(
        FlatVoice? Flat,
        WorldVoice? World)
    {
        public VoiceReference(FlatVoice voice) : this(voice, null) { }
        public VoiceReference(WorldVoice voice) : this(null, voice) { }

        public void Stop()
        {
            Flat?.Stop();
            World?.Stop();
        }
    }
}

public static class War3AudioBusInstaller
{
    private static readonly (string Name, string Send)[] Buses =
    [
        ("Music", "Master"),
        ("SFX", "Master"),
        ("Combat", "SFX"),
        ("Ability", "SFX"),
        ("World", "SFX"),
        ("Voice", "Master"),
        ("UI", "Master"),
        ("Ambience", "Master"),
        ("Cinematic", "Master")
    ];

    private static readonly HashSet<string> Known = Buses
        .Select(value => value.Name)
        .Append("Master")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static void EnsureAndApply(War3AudioMixSettings settings)
    {
        foreach (var (name, send) in Buses)
        {
            var index = AudioServer.GetBusIndex(name);
            if (index < 0)
            {
                AudioServer.AddBus();
                index = AudioServer.BusCount - 1;
                AudioServer.SetBusName(index, name);
            }
            AudioServer.SetBusSend(index, send);
        }
        var value = settings.Clamp();
        Set("Master", value.Muted ? 0f : value.Master);
        Set("Music", value.Music);
        Set("SFX", value.SoundEffects);
        Set("Voice", value.Voice);
        Set("UI", value.Interface);
        Set("Ambience", value.Ambience);
    }

    public static StringName ResolveBus(string requested) =>
        Known.Contains(requested) ? requested : "SFX";

    private static void Set(string bus, float linear)
    {
        var index = AudioServer.GetBusIndex(bus);
        if (index < 0) return;
        AudioServer.SetBusVolumeDb(
            index,
            linear <= 0.0001f ? -80f : Mathf.LinearToDb(linear));
        AudioServer.SetBusMute(index, linear <= 0.0001f);
    }
}
