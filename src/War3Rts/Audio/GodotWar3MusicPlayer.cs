using Godot;

namespace War3Rts.Audio;

/// <summary>
/// System-level, non-positional music player. Playlist state is deliberately
/// separate from SoundInfo one-shots and world-emitter voice pools.
/// </summary>
public sealed partial class GodotWar3MusicPlayer : Node
{
    private readonly Dictionary<string, AudioStream> _streams =
        new(StringComparer.OrdinalIgnoreCase);
    private AudioStreamPlayer? _player;
    private War3MusicPlaylist? _playlist;
    private int _trackIndex = -1;

    public string CurrentTrackId { get; private set; } = string.Empty;
    public long StartedTracks { get; private set; }
    public long MissingTracks { get; private set; }

    public void Initialize(War3MusicPlaylist playlist, bool autoplay = true)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        if (_player is not null)
            throw new InvalidOperationException("Warcraft music was already initialized.");
        _playlist = playlist;
        _player = new AudioStreamPlayer
        {
            Name = "MusicPlayer",
            Bus = "Music",
            MaxPolyphony = 1
        };
        _player.Finished += OnFinished;
        AddChild(_player);
        if (autoplay && playlist.Count > 0) PlayNext();
    }

    public bool PlayNext()
    {
        if (_player is null || _playlist is null || _playlist.Count == 0)
            return false;
        for (var attempt = 0; attempt < _playlist.Count; attempt++)
        {
            _trackIndex = (_trackIndex + 1) % _playlist.Count;
            var track = _playlist.Tracks[_trackIndex];
            if (!TryLoad(track.ResourcePath, out var stream))
            {
                MissingTracks++;
                continue;
            }
            _player.Stream = stream;
            _player.Play();
            CurrentTrackId = track.Id;
            StartedTracks++;
            return true;
        }
        return false;
    }

    public void Stop()
    {
        _player?.Stop();
        CurrentTrackId = string.Empty;
    }

    public override void _ExitTree() => Stop();

    private void OnFinished() => PlayNext();

    private bool TryLoad(string path, out AudioStream stream)
    {
        if (_streams.TryGetValue(path, out stream!)) return true;
        if (!ResourceLoader.Exists(path))
        {
            stream = null!;
            return false;
        }
        stream = ResourceLoader.Load<AudioStream>(path)!;
        if (stream is null) return false;
        if (stream is AudioStreamMP3 mp3 && mp3.Loop)
        {
            stream = (AudioStreamMP3)mp3.Duplicate(true);
            ((AudioStreamMP3)stream).Loop = false;
        }
        _streams.Add(path, stream);
        return true;
    }
}
