using System.Text.Json;

namespace War3Rts.Audio;

public sealed record War3MusicTrack(string Id, string ResourcePath, string Race);

/// <summary>Pure runtime-pack view of non-positional background music.</summary>
public sealed class War3MusicPlaylist
{
    private War3MusicPlaylist(IReadOnlyList<War3MusicTrack> tracks)
    {
        Tracks = tracks;
    }

    public IReadOnlyList<War3MusicTrack> Tracks { get; }
    public int Count => Tracks.Count;

    public static War3MusicPlaylist Load(
        string runtimeManifestPath,
        string runtimeResourceRoot = "res://assets/generated/warcraft3_audio")
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(runtimeManifestPath));
        var root = document.RootElement;
        if (!root.GetProperty("schema").GetString()!.Equals(
                "war3-audio-runtime-pack/v1", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported Warcraft audio runtime pack.");
        var tracks = new List<War3MusicTrack>();
        if (!root.TryGetProperty("music", out var music) ||
            music.ValueKind != JsonValueKind.Array)
            return new War3MusicPlaylist(tracks);
        foreach (var value in music.EnumerateArray())
        {
            var id = value.GetProperty("id").GetString() ?? string.Empty;
            var relative = value.GetProperty("resource").GetString() ?? string.Empty;
            var race = value.TryGetProperty("race", out var raceValue)
                ? raceValue.GetString() ?? string.Empty
                : string.Empty;
            if (id.Length == 0 || relative.Length == 0) continue;
            tracks.Add(new War3MusicTrack(
                id,
                $"{runtimeResourceRoot.TrimEnd('/', '\\')}/" +
                relative.Replace('\\', '/').TrimStart('/'),
                race));
        }
        return new War3MusicPlaylist(tracks);
    }
}
