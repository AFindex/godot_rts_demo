using Godot;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime.Resources;

public static class GameplayProfileResourceConverter
{
    public static RtsGameplayProfilesResource FromSnapshot(
        GameplayProfileCatalogSnapshot snapshot)
    {
        var resource = new RtsGameplayProfilesResource
        {
            FormatVersion = snapshot.FormatVersion
        };
        var units = snapshot.UnitProfiles;
        for (var index = 0; index < units.Length; index++)
        {
            var source = units[index];
            resource.UnitProfiles.Add(new UnitMovementProfileResource
            {
                Id = source.Id,
                DisplayName = source.Name,
                PhysicalRadius = source.PhysicalRadius,
                MaximumSpeed = source.MaximumSpeed,
                Acceleration = source.Acceleration,
                TurnRateRadiansPerSecond = source.TurnRateRadiansPerSecond
            });
        }
        var buildings = snapshot.BuildingProfiles;
        for (var index = 0; index < buildings.Length; index++)
        {
            var source = buildings[index];
            resource.BuildingProfiles.Add(new BuildingFootprintProfileResource
            {
                Id = source.Id,
                DisplayName = source.Name,
                FootprintClass = source.FootprintClass,
                Size = new Vector2(source.Size.X, source.Size.Y),
                MinimumPassageClass = source.MinimumPassageClass,
                UnitPadding = source.UnitPadding
            });
        }
        return resource;
    }

    public static bool TryLoadSnapshot(
        string resourcePath,
        out GameplayProfileCatalogSnapshot? snapshot,
        out GameplayProfileValidationResult validation)
    {
        if (!ResourceLoader.Exists(resourcePath))
        {
            snapshot = null;
            validation = SingleIssue(
                GameplayProfileErrorCode.MissingResourceAsset,
                $"Gameplay profile resource does not exist: {resourcePath}");
            return false;
        }

        var resource = GD.Load<RtsGameplayProfilesResource>(resourcePath);
        if (resource is null)
        {
            snapshot = null;
            validation = SingleIssue(
                GameplayProfileErrorCode.MissingResourceAsset,
                $"Gameplay profile resource could not be loaded: {resourcePath}");
            return false;
        }

        return TryConvert(resource, out snapshot, out validation);
    }

    public static bool TryConvert(
        RtsGameplayProfilesResource resource,
        out GameplayProfileCatalogSnapshot? snapshot,
        out GameplayProfileValidationResult validation)
    {
        var units = new UnitMovementProfileSnapshot[resource.UnitProfiles.Count];
        for (var index = 0; index < units.Length; index++)
        {
            var source = resource.UnitProfiles[index];
            if (source is null)
            {
                snapshot = null;
                validation = NullElement("unit", index);
                return false;
            }

            units[index] = new UnitMovementProfileSnapshot(
                source.Id,
                source.DisplayName ?? string.Empty,
                source.PhysicalRadius,
                source.MaximumSpeed,
                source.Acceleration,
                default,
                0f,
                source.TurnRateRadiansPerSecond);
        }

        var buildings = new BuildingFootprintProfileSnapshot[
            resource.BuildingProfiles.Count];
        for (var index = 0; index < buildings.Length; index++)
        {
            var source = resource.BuildingProfiles[index];
            if (source is null)
            {
                snapshot = null;
                validation = NullElement("building", index);
                return false;
            }

            buildings[index] = new BuildingFootprintProfileSnapshot(
                source.Id,
                source.DisplayName ?? string.Empty,
                source.FootprintClass,
                new NVector2(source.Size.X, source.Size.Y),
                source.MinimumPassageClass,
                source.UnitPadding);
        }

        return GameplayProfileCatalogSnapshot.TryCreate(
            resource.FormatVersion,
            units,
            buildings,
            out snapshot,
            out validation);
    }

    private static GameplayProfileValidationResult NullElement(
        string type,
        int index) =>
        SingleIssue(
            GameplayProfileErrorCode.NullResourceElement,
            $"Gameplay {type} profile at index {index} is null.",
            index);

    private static GameplayProfileValidationResult SingleIssue(
        GameplayProfileErrorCode code,
        string message,
        int index = -1) =>
        new([new GameplayProfileValidationIssue(code, index, message)]);
}
