using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

public enum RuntimeResourceLoadErrorCode : byte
{
    None,
    NavigationLoadFailed,
    GameplayProfilesLoadFailed,
    ClearanceBakeLoadFailed,
    ResourceSetMismatch
}

public readonly record struct RuntimeResourceLoadResult(
    RuntimeResourceLoadErrorCode Code,
    string Message)
{
    public bool Succeeded => Code == RuntimeResourceLoadErrorCode.None;
}

public static class RuntimeResourceSetLoader
{
    public static bool TryLoadFresh(
        string navigationPath,
        string gameplayProfilesPath,
        string clearanceBakePath,
        out RuntimeResourceSetSnapshot? snapshot,
        out RuntimeResourceLoadResult result)
    {
        var navigationResource = ResourceLoader.Load<RtsNavigationMapResource>(
            navigationPath, string.Empty, ResourceLoader.CacheMode.Replace);
        if (navigationResource is null)
        {
            snapshot = null;
            result = Failure(
                RuntimeResourceLoadErrorCode.NavigationLoadFailed,
                "Missing resource");
            return false;
        }
        if (!NavigationMapResourceConverter.TryConvert(
                navigationResource, out var navigation, out var navigationValidation) ||
            navigation is null)
        {
            snapshot = null;
            result = Failure(
                RuntimeResourceLoadErrorCode.NavigationLoadFailed,
                navigationValidation.FirstError.ToString());
            return false;
        }

        var profilesResource = ResourceLoader.Load<RtsGameplayProfilesResource>(
            gameplayProfilesPath, string.Empty, ResourceLoader.CacheMode.Replace);
        if (profilesResource is null)
        {
            snapshot = null;
            result = Failure(
                RuntimeResourceLoadErrorCode.GameplayProfilesLoadFailed,
                "Missing resource");
            return false;
        }
        if (!GameplayProfileResourceConverter.TryConvert(
                profilesResource, out var profiles, out var profilesValidation) ||
            profiles is null)
        {
            snapshot = null;
            result = Failure(
                RuntimeResourceLoadErrorCode.GameplayProfilesLoadFailed,
                profilesValidation.FirstError.ToString());
            return false;
        }

        var bakeResource = ResourceLoader.Load<RtsClearanceBakeResource>(
            clearanceBakePath, string.Empty, ResourceLoader.CacheMode.Replace);
        if (bakeResource is null)
        {
            snapshot = null;
            result = Failure(
                RuntimeResourceLoadErrorCode.ClearanceBakeLoadFailed,
                "Missing resource");
            return false;
        }
        if (!ClearanceBakeResourceConverter.TryConvert(
                bakeResource,
                navigation.StableHash,
                out var bake,
                out var bakeValidation) ||
            bake is null)
        {
            snapshot = null;
            result = Failure(
                RuntimeResourceLoadErrorCode.ClearanceBakeLoadFailed,
                bakeValidation.FirstError.ToString());
            return false;
        }

        if (!RuntimeResourceSetSnapshot.TryCreate(
                navigation, profiles, bake, out snapshot, out var validation) ||
            snapshot is null)
        {
            result = Failure(
                RuntimeResourceLoadErrorCode.ResourceSetMismatch,
                validation.Message);
            return false;
        }

        result = new RuntimeResourceLoadResult(
            RuntimeResourceLoadErrorCode.None, string.Empty);
        return true;
    }

    private static RuntimeResourceLoadResult Failure(
        RuntimeResourceLoadErrorCode code,
        string message) => new(code, message);
}
