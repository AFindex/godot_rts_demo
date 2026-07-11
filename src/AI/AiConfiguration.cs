using System.Text;
using RtsDemo.Simulation;

namespace RtsDemo.AI;

public readonly record struct AiDifficultyProfile(
    int Id,
    string Name,
    int TargetWorkers,
    int AttackArmySize,
    int MaximumIntentsPerDecision,
    int SupplyBuffer,
    int DecisionIntervalTicks,
    int ScoutIntervalTicks,
    int AttackIntervalTicks,
    float DefenseRadius);

public sealed class AiConfigurationCatalogSnapshot
{
    public const int CurrentFormatVersion = 1;
    private readonly AiDifficultyProfile[] _profiles;
    private readonly byte[] _canonicalBytes;

    private AiConfigurationCatalogSnapshot(
        int formatVersion,
        AiDifficultyProfile[] profiles)
    {
        FormatVersion = formatVersion;
        _profiles = profiles;
        _canonicalBytes = Serialize();
        StableHash = StableHash64.Compute(_canonicalBytes);
    }

    public int FormatVersion { get; }
    public ReadOnlySpan<AiDifficultyProfile> Profiles => _profiles;
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");
    public AiDifficultyProfile Profile(int id) => _profiles[id];

    public static bool TryCreate(
        int formatVersion,
        ReadOnlySpan<AiDifficultyProfile> profiles,
        out AiConfigurationCatalogSnapshot? snapshot,
        out string error)
    {
        var copy = profiles.ToArray();
        if (formatVersion != CurrentFormatVersion || copy.Length == 0)
        {
            snapshot = null;
            error = "AI configuration format or profile count is invalid.";
            return false;
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < copy.Length; index++)
        {
            var value = copy[index];
            if (value.Id != index || string.IsNullOrWhiteSpace(value.Name) ||
                !names.Add(value.Name) || value.TargetWorkers <= 0 ||
                value.AttackArmySize <= 0 ||
                value.MaximumIntentsPerDecision is <= 0 or > 64 ||
                value.SupplyBuffer < 0 || value.DecisionIntervalTicks <= 0 ||
                value.ScoutIntervalTicks <= 0 ||
                value.AttackIntervalTicks <= 0 ||
                !float.IsFinite(value.DefenseRadius) || value.DefenseRadius <= 0f)
            {
                snapshot = null;
                error = $"AI difficulty profile {index} is invalid or duplicated.";
                return false;
            }
        }
        snapshot = new AiConfigurationCatalogSnapshot(formatVersion, copy);
        error = string.Empty;
        return true;
    }

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(FormatVersion);
        writer.Write(_profiles.Length);
        foreach (var value in _profiles)
        {
            writer.Write(value.Id);
            writer.Write(value.Name);
            writer.Write(value.TargetWorkers);
            writer.Write(value.AttackArmySize);
            writer.Write(value.MaximumIntentsPerDecision);
            writer.Write(value.SupplyBuffer);
            writer.Write(value.DecisionIntervalTicks);
            writer.Write(value.ScoutIntervalTicks);
            writer.Write(value.AttackIntervalTicks);
            writer.Write(value.DefenseRadius);
        }
        writer.Flush();
        return stream.ToArray();
    }
}

public static class DemoAiConfigurations
{
    private static readonly AiConfigurationCatalogSnapshot Catalog = Build();

    public static AiConfigurationCatalogSnapshot CreateCatalog() => Catalog;
    public static AiDifficultyProfile Standard => Catalog.Profile(0);
    public static AiDifficultyProfile Aggressive => Catalog.Profile(1);

    private static AiConfigurationCatalogSnapshot Build()
    {
        AiDifficultyProfile[] profiles =
        [
            new(0, "Standard", 10, 6, 6, 3, 12, 360, 240, 340f),
            new(1, "Aggressive", 8, 4, 8, 2, 10, 240, 120, 380f)
        ];
        if (!AiConfigurationCatalogSnapshot.TryCreate(
                AiConfigurationCatalogSnapshot.CurrentFormatVersion,
                profiles, out var catalog, out var error) || catalog is null)
            throw new InvalidOperationException(error);
        return catalog;
    }
}
