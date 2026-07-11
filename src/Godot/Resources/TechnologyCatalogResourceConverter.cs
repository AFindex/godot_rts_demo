using Godot;
using RtsDemo.Simulation;
using System.Collections.Immutable;

namespace RtsDemo.GodotRuntime.Resources;

public enum TechnologyResourceErrorCode
{
    None,
    MissingResourceAsset,
    NullTechnology,
    NullRequirement,
    InvalidCatalog,
    InvalidDependency
}

public readonly record struct TechnologyResourceValidationResult(
    TechnologyResourceErrorCode Code,
    int Index,
    string Message)
{
    public bool IsValid => Code == TechnologyResourceErrorCode.None;
}

public static class TechnologyCatalogResourceConverter
{
    public static RtsTechnologyCatalogResource FromSnapshot(
        TechnologyCatalogSnapshot snapshot)
    {
        var resource = new RtsTechnologyCatalogResource
        {
            FormatVersion = snapshot.FormatVersion
        };
        foreach (var technology in snapshot.Technologies)
        {
            var value = new TechnologyProfileResource
            {
                Id = technology.Id,
                DisplayName = technology.Name,
                ResearcherBuildingTypeId = technology.ResearcherBuildingTypeId,
                MineralCost = technology.Cost.Minerals,
                VespeneCost = technology.Cost.VespeneGas,
                ResearchSeconds = technology.ResearchSeconds,
                MaximumLevel = technology.MaximumLevel,
                CancelRefundFraction = technology.CancelRefundFraction,
                ExclusiveGroupId = technology.ExclusiveGroupId
            };
            foreach (var requirement in technology.Requirements)
                value.Requirements.Add(new TechnologyRequirementProfileResource
                {
                    Kind = requirement.Kind,
                    TargetId = requirement.TargetId,
                    RequiredValue = requirement.Value
                });
            resource.Technologies.Add(value);
        }
        return resource;
    }

    public static bool TryLoadSnapshot(
        string path,
        BuildingTypeCatalogSnapshot buildings,
        out TechnologyCatalogSnapshot? snapshot,
        out TechnologyResourceValidationResult validation)
    {
        var resource = ResourceLoader.Load<RtsTechnologyCatalogResource>(
            path, string.Empty, ResourceLoader.CacheMode.Replace);
        if (resource is null)
        {
            snapshot = null;
            validation = new TechnologyResourceValidationResult(
                TechnologyResourceErrorCode.MissingResourceAsset, -1,
                $"Technology catalog could not be loaded: {path}");
            return false;
        }
        return TryConvert(resource, buildings, out snapshot, out validation);
    }

    public static bool TryConvert(
        RtsTechnologyCatalogResource resource,
        BuildingTypeCatalogSnapshot buildings,
        out TechnologyCatalogSnapshot? snapshot,
        out TechnologyResourceValidationResult validation)
    {
        var technologies = new TechnologyProfile[resource.Technologies.Count];
        for (var index = 0; index < technologies.Length; index++)
        {
            var source = resource.Technologies[index];
            if (source is null)
                return Failure(
                    TechnologyResourceErrorCode.NullTechnology, index,
                    "Technology resource is null.", out snapshot, out validation);
            var requirements = new TechnologyRequirementProfile[
                source.Requirements.Count];
            for (var requirementIndex = 0;
                 requirementIndex < requirements.Length;
                 requirementIndex++)
            {
                var requirement = source.Requirements[requirementIndex];
                if (requirement is null)
                    return Failure(
                        TechnologyResourceErrorCode.NullRequirement, index,
                        $"Requirement {requirementIndex} is null.",
                        out snapshot, out validation);
                requirements[requirementIndex] = new TechnologyRequirementProfile(
                    requirement.Kind,
                    requirement.TargetId,
                    requirement.RequiredValue);
            }
            technologies[index] = new TechnologyProfile(
                source.Id,
                source.DisplayName ?? string.Empty,
                source.ResearcherBuildingTypeId,
                new EconomyCost(source.MineralCost, source.VespeneCost),
                source.ResearchSeconds,
                source.MaximumLevel,
                source.CancelRefundFraction,
                source.ExclusiveGroupId)
            {
                Requirements = requirements.ToImmutableArray()
            };
        }
        if (!TechnologyCatalogSnapshot.TryCreate(
                resource.FormatVersion, technologies,
                out snapshot, out var error) || snapshot is null)
        {
            validation = new TechnologyResourceValidationResult(
                TechnologyResourceErrorCode.InvalidCatalog, -1, error);
            return false;
        }
        if (!TechnologyCatalogDependencyValidator.TryValidate(
                snapshot, buildings, out error))
        {
            snapshot = null;
            validation = new TechnologyResourceValidationResult(
                TechnologyResourceErrorCode.InvalidDependency, -1, error);
            return false;
        }
        validation = new TechnologyResourceValidationResult(
            TechnologyResourceErrorCode.None, -1, string.Empty);
        return true;
    }

    private static bool Failure(
        TechnologyResourceErrorCode code,
        int index,
        string message,
        out TechnologyCatalogSnapshot? snapshot,
        out TechnologyResourceValidationResult validation)
    {
        snapshot = null;
        validation = new TechnologyResourceValidationResult(code, index, message);
        return false;
    }
}
