using System.Text.Json;

namespace War3Rts.Audio;

public sealed record War3AudioMixSettings
{
    public float Master { get; init; } = 1f;
    public float Music { get; init; } = 0.72f;
    public float SoundEffects { get; init; } = 0.9f;
    public float Voice { get; init; } = 1f;
    public float Interface { get; init; } = 0.9f;
    public float Ambience { get; init; } = 0.7f;
    public bool Muted { get; init; }

    public War3AudioMixSettings Clamp() => this with
    {
        Master = ClampVolume(Master),
        Music = ClampVolume(Music),
        SoundEffects = ClampVolume(SoundEffects),
        Voice = ClampVolume(Voice),
        Interface = ClampVolume(Interface),
        Ambience = ClampVolume(Ambience)
    };

    private static float ClampVolume(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 1f;
}

public static class War3AudioMixSettingsCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static War3AudioMixSettings LoadOrDefault(string path)
    {
        try
        {
            if (!File.Exists(path)) return new War3AudioMixSettings();
            return (JsonSerializer.Deserialize<War3AudioMixSettings>(
                        File.ReadAllText(path), JsonOptions) ??
                    new War3AudioMixSettings()).Clamp();
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          JsonException)
        {
            return new War3AudioMixSettings();
        }
    }

    public static void Save(string path, War3AudioMixSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(settings.Clamp(), JsonOptions) +
            Environment.NewLine);
    }
}
