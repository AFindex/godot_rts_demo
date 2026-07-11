using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

public readonly record struct HotReloadTestResourceGeneration(
    Error NavigationError,
    Error ProfilesError,
    Error BakeError,
    Error BakeOnlyError,
    string NavigationHash,
    string ProfilesHash,
    string BakeHash,
    string BakeOnlyHash)
{
    public bool Succeeded => NavigationError == Error.Ok &&
                             ProfilesError == Error.Ok &&
                             BakeError == Error.Ok &&
                             BakeOnlyError == Error.Ok;
}

public static class HotReloadTestResourceGenerator
{
    public const string DirectoryPath = "res://test_resources/hot_reload";
    public const string NavigationPath = DirectoryPath + "/navigation_variant.tres";
    public const string ProfilesPath = DirectoryPath + "/gameplay_variant.tres";
    public const string BakePath = DirectoryPath + "/clearance_variant.tres";
    public const string BakeOnlyPath =
        DirectoryPath + "/clearance_bake_only_variant.tres";

    public static HotReloadTestResourceGeneration Generate(
        NavigationMapSnapshot sourceNavigation,
        GameplayProfileCatalogSnapshot sourceProfiles)
    {
        var navigation = DemoResourceVariantFactory.CreateNavigationVariant(
            sourceNavigation);
        var profiles = DemoResourceVariantFactory.CreateGameplayVariant(
            sourceProfiles);
        var bake = ClearanceBakeSnapshot.Build(navigation);
        var bakeOnly = ClearanceBakeSnapshot.Build(
            sourceNavigation, chunkSizeCells: 8);
        var directoryError = DirAccess.MakeDirRecursiveAbsolute(
            ProjectSettings.GlobalizePath(DirectoryPath));
        if (directoryError is not (Error.Ok or Error.AlreadyExists))
        {
            return new HotReloadTestResourceGeneration(
                directoryError,
                directoryError,
                directoryError,
                directoryError,
                navigation.StableHashText,
                profiles.StableHashText,
                bake.StableHashText,
                bakeOnly.StableHashText);
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
            ResourceSaver.Save(
                ClearanceBakeResourceConverter.FromSnapshot(bakeOnly),
                BakeOnlyPath),
            navigation.StableHashText,
            profiles.StableHashText,
            bake.StableHashText,
            bakeOnly.StableHashText);
    }
}
