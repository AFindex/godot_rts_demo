using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

public readonly record struct HotReloadTestResourceGeneration(
    Error NavigationError,
    Error ProfilesError,
    Error BakeError,
    string NavigationHash,
    string ProfilesHash,
    string BakeHash)
{
    public bool Succeeded => NavigationError == Error.Ok &&
                             ProfilesError == Error.Ok &&
                             BakeError == Error.Ok;
}

public static class HotReloadTestResourceGenerator
{
    public const string DirectoryPath = "res://test_resources/hot_reload";
    public const string NavigationPath = DirectoryPath + "/navigation_variant.tres";
    public const string ProfilesPath = DirectoryPath + "/gameplay_variant.tres";
    public const string BakePath = DirectoryPath + "/clearance_variant.tres";

    public static HotReloadTestResourceGeneration Generate(
        NavigationMapSnapshot sourceNavigation,
        GameplayProfileCatalogSnapshot sourceProfiles)
    {
        var navigation = DemoResourceVariantFactory.CreateNavigationVariant(
            sourceNavigation);
        var profiles = DemoResourceVariantFactory.CreateGameplayVariant(
            sourceProfiles);
        var bake = ClearanceBakeSnapshot.Build(navigation);
        var directoryError = DirAccess.MakeDirRecursiveAbsolute(
            ProjectSettings.GlobalizePath(DirectoryPath));
        if (directoryError is not (Error.Ok or Error.AlreadyExists))
        {
            return new HotReloadTestResourceGeneration(
                directoryError,
                directoryError,
                directoryError,
                navigation.StableHashText,
                profiles.StableHashText,
                bake.StableHashText);
        }

        return new HotReloadTestResourceGeneration(
            ResourceSaver.Save(
                NavigationMapResourceConverter.FromSnapshot(navigation),
                NavigationPath),
            ResourceSaver.Save(
                GameplayProfileResourceConverter.FromSnapshot(profiles),
                ProfilesPath),
            ResourceSaver.Save(
                ClearanceBakeResourceConverter.FromSnapshot(bake),
                BakePath),
            navigation.StableHashText,
            profiles.StableHashText,
            bake.StableHashText);
    }
}
