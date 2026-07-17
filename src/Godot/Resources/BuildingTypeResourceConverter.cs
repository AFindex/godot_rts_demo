using Godot;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime.Resources;

public static class BuildingTypeResourceConverter
{
    public static RtsBuildingTypeCatalogResource FromSnapshot(
        BuildingTypeCatalogSnapshot snapshot)
    {
        var resource = new RtsBuildingTypeCatalogResource
        {
            FormatVersion = snapshot.FormatVersion
        };
        foreach (var source in snapshot.Types)
        {
            resource.Types.Add(new BuildingTypeProfileResource
            {
                Id = source.Id,
                DisplayName = source.Name,
                Function = source.Function,
                Size = new Vector2(source.Size.X, source.Size.Y),
                MinimumPassageClass = source.MinimumPassageClass,
                MineralCost = source.Cost.Minerals,
                VespeneCost = source.Cost.VespeneGas,
                SupplyCost = source.Cost.Supply,
                BuildSeconds = source.BuildSeconds,
                MaximumHealth = source.MaximumHealth,
                SupplyProvided = source.SupplyProvided,
                CancelRefundFraction = source.CancelRefundFraction,
                ConstructionMethod = source.ConstructionMethod,
                RequiresVespeneNode = source.RequiresVespeneNode,
                Armor = source.Armor,
                Attributes = source.Attributes,
                ArmorUpgradePerLevel = source.ArmorUpgradePerLevel,
                ArmorType = source.ArmorType
            });
        }
        return resource;
    }

    public static bool TryLoadSnapshot(
        string path,
        out BuildingTypeCatalogSnapshot? snapshot,
        out BuildingTypeCatalogValidationResult validation)
    {
        var resource = ResourceLoader.Load<RtsBuildingTypeCatalogResource>(
            path, string.Empty, ResourceLoader.CacheMode.Replace);
        if (resource is null)
        {
            snapshot = null;
            validation = SingleIssue(
                BuildingTypeCatalogErrorCode.MissingResourceAsset,
                $"Building type resource could not be loaded: {path}");
            return false;
        }
        return TryConvert(resource, out snapshot, out validation);
    }

    public static bool TryConvert(
        RtsBuildingTypeCatalogResource resource,
        out BuildingTypeCatalogSnapshot? snapshot,
        out BuildingTypeCatalogValidationResult validation)
    {
        var types = new BuildingTypeProfile[resource.Types.Count];
        for (var index = 0; index < types.Length; index++)
        {
            var source = resource.Types[index];
            if (source is null)
            {
                snapshot = null;
                validation = SingleIssue(
                    BuildingTypeCatalogErrorCode.NullResourceElement,
                    $"Building type at index {index} is null.", index);
                return false;
            }
            types[index] = new BuildingTypeProfile(
                source.Id,
                source.DisplayName ?? string.Empty,
                source.Function,
                new NVector2(source.Size.X, source.Size.Y),
                source.MinimumPassageClass,
                new EconomyCost(
                    source.MineralCost,
                    source.VespeneCost,
                    source.SupplyCost),
                source.BuildSeconds,
                source.MaximumHealth,
                source.SupplyProvided,
                source.CancelRefundFraction,
                source.ConstructionMethod,
                source.RequiresVespeneNode,
                source.Armor,
                source.Attributes,
                source.ArmorUpgradePerLevel,
                source.ArmorType);
        }
        return BuildingTypeCatalogSnapshot.TryCreate(
            resource.FormatVersion, types, out snapshot, out validation);
    }

    private static BuildingTypeCatalogValidationResult SingleIssue(
        BuildingTypeCatalogErrorCode code,
        string message,
        int index = -1) =>
        new([new BuildingTypeCatalogValidationIssue(code, index, message)]);
}
