using Godot;
using RtsDemo.AI;

namespace RtsDemo.GodotRuntime.Resources;

public enum AiConfigurationResourceErrorCode
{
    None,
    MissingResourceAsset,
    NullProfile,
    InvalidCatalog
}

public readonly record struct AiConfigurationResourceValidationResult(
    AiConfigurationResourceErrorCode Code,
    int Index,
    string Message)
{
    public bool IsValid => Code == AiConfigurationResourceErrorCode.None;
}

public static class AiConfigurationResourceConverter
{
    public static RtsAiConfigurationCatalogResource FromSnapshot(
        AiConfigurationCatalogSnapshot snapshot)
    {
        var resource = new RtsAiConfigurationCatalogResource
        {
            FormatVersion = snapshot.FormatVersion
        };
        foreach (var profile in snapshot.Profiles)
            resource.Profiles.Add(new AiDifficultyProfileResource
            {
                Id = profile.Id,
                DisplayName = profile.Name,
                TargetWorkers = profile.TargetWorkers,
                AttackArmySize = profile.AttackArmySize,
                MaximumIntentsPerDecision = profile.MaximumIntentsPerDecision,
                SupplyBuffer = profile.SupplyBuffer,
                DecisionIntervalTicks = profile.DecisionIntervalTicks,
                ScoutIntervalTicks = profile.ScoutIntervalTicks,
                AttackIntervalTicks = profile.AttackIntervalTicks,
                DefenseRadius = profile.DefenseRadius
            });
        return resource;
    }

    public static bool TryLoadSnapshot(
        string path,
        out AiConfigurationCatalogSnapshot? snapshot,
        out AiConfigurationResourceValidationResult validation)
    {
        var resource = ResourceLoader.Load<RtsAiConfigurationCatalogResource>(
            path, string.Empty, ResourceLoader.CacheMode.Replace);
        if (resource is null)
        {
            snapshot = null;
            validation = new(
                AiConfigurationResourceErrorCode.MissingResourceAsset,
                -1,
                $"AI configuration catalog could not be loaded: {path}");
            return false;
        }
        return TryConvert(resource, out snapshot, out validation);
    }

    public static bool TryConvert(
        RtsAiConfigurationCatalogResource resource,
        out AiConfigurationCatalogSnapshot? snapshot,
        out AiConfigurationResourceValidationResult validation)
    {
        var profiles = new AiDifficultyProfile[resource.Profiles.Count];
        for (var index = 0; index < profiles.Length; index++)
        {
            var source = resource.Profiles[index];
            if (source is null)
            {
                snapshot = null;
                validation = new(
                    AiConfigurationResourceErrorCode.NullProfile,
                    index,
                    "AI difficulty profile resource is null.");
                return false;
            }
            profiles[index] = new AiDifficultyProfile(
                source.Id,
                source.DisplayName ?? string.Empty,
                source.TargetWorkers,
                source.AttackArmySize,
                source.MaximumIntentsPerDecision,
                source.SupplyBuffer,
                source.DecisionIntervalTicks,
                source.ScoutIntervalTicks,
                source.AttackIntervalTicks,
                source.DefenseRadius);
        }
        if (!AiConfigurationCatalogSnapshot.TryCreate(
                resource.FormatVersion,
                profiles,
                out snapshot,
                out var error) || snapshot is null)
        {
            validation = new(
                AiConfigurationResourceErrorCode.InvalidCatalog,
                -1,
                error);
            return false;
        }
        validation = new(AiConfigurationResourceErrorCode.None, -1, string.Empty);
        return true;
    }
}
