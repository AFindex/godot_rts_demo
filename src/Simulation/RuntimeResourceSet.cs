namespace RtsDemo.Simulation;

public enum RuntimeResourceSetErrorCode : byte
{
    None,
    BakeNavigationMismatch
}

public readonly record struct RuntimeResourceSetValidation(
    RuntimeResourceSetErrorCode Code,
    string Message)
{
    public bool IsValid => Code == RuntimeResourceSetErrorCode.None;
}

public sealed class RuntimeResourceSetSnapshot
{
    private RuntimeResourceSetSnapshot(
        NavigationMapSnapshot navigation,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot clearanceBake)
    {
        Navigation = navigation;
        GameplayProfiles = gameplayProfiles;
        ClearanceBake = clearanceBake;
    }

    public NavigationMapSnapshot Navigation { get; }
    public GameplayProfileCatalogSnapshot GameplayProfiles { get; }
    public ClearanceBakeSnapshot ClearanceBake { get; }

    public static bool TryCreate(
        NavigationMapSnapshot navigation,
        GameplayProfileCatalogSnapshot gameplayProfiles,
        ClearanceBakeSnapshot clearanceBake,
        out RuntimeResourceSetSnapshot? snapshot,
        out RuntimeResourceSetValidation validation)
    {
        if (clearanceBake.SourceNavigationHash != navigation.StableHash)
        {
            snapshot = null;
            validation = new RuntimeResourceSetValidation(
                RuntimeResourceSetErrorCode.BakeNavigationMismatch,
                $"Bake source {clearanceBake.SourceNavigationHashText} does not " +
                $"match navigation {navigation.StableHashText}.");
            return false;
        }

        snapshot = new RuntimeResourceSetSnapshot(
            navigation, gameplayProfiles, clearanceBake);
        validation = new RuntimeResourceSetValidation(
            RuntimeResourceSetErrorCode.None, string.Empty);
        return true;
    }
}

public enum ResourceReloadImpact : byte
{
    None,
    RefreshPathingCaches,
    RebuildSimulation
}

public readonly record struct NavigationResourceDiff(
    bool Changed,
    bool WorldBoundsChanged,
    int ChangedObstacles,
    int ChangedPortals,
    int ChangedEdges,
    int ChangedChokes);

public readonly record struct GameplayProfileResourceDiff(
    bool Changed,
    int ChangedUnitProfiles,
    int ChangedBuildingProfiles);

public readonly record struct ClearanceBakeResourceDiff(
    bool Changed,
    bool SourceNavigationChanged);

public sealed class RuntimeResourceReloadPlan
{
    private RuntimeResourceReloadPlan(
        RuntimeResourceSetSnapshot current,
        RuntimeResourceSetSnapshot candidate,
        NavigationResourceDiff navigation,
        GameplayProfileResourceDiff gameplayProfiles,
        ClearanceBakeResourceDiff clearanceBake,
        ResourceReloadImpact impact)
    {
        Current = current;
        Candidate = candidate;
        Navigation = navigation;
        GameplayProfiles = gameplayProfiles;
        ClearanceBake = clearanceBake;
        Impact = impact;
    }

    public RuntimeResourceSetSnapshot Current { get; }
    public RuntimeResourceSetSnapshot Candidate { get; }
    public NavigationResourceDiff Navigation { get; }
    public GameplayProfileResourceDiff GameplayProfiles { get; }
    public ClearanceBakeResourceDiff ClearanceBake { get; }
    public ResourceReloadImpact Impact { get; }
    public bool HasChanges => Impact != ResourceReloadImpact.None;

    public static RuntimeResourceReloadPlan Create(
        RuntimeResourceSetSnapshot current,
        RuntimeResourceSetSnapshot candidate)
    {
        var navigation = CompareNavigation(
            current.Navigation, candidate.Navigation);
        var gameplay = CompareGameplay(
            current.GameplayProfiles, candidate.GameplayProfiles);
        var bake = new ClearanceBakeResourceDiff(
            current.ClearanceBake.StableHash != candidate.ClearanceBake.StableHash,
            current.ClearanceBake.SourceNavigationHash !=
                candidate.ClearanceBake.SourceNavigationHash);
        var impact = navigation.Changed || gameplay.Changed
            ? ResourceReloadImpact.RebuildSimulation
            : bake.Changed
                ? ResourceReloadImpact.RefreshPathingCaches
                : ResourceReloadImpact.None;
        return new RuntimeResourceReloadPlan(
            current, candidate, navigation, gameplay, bake, impact);
    }

    private static NavigationResourceDiff CompareNavigation(
        NavigationMapSnapshot current,
        NavigationMapSnapshot candidate)
    {
        var changed = current.StableHash != candidate.StableHash;
        return new NavigationResourceDiff(
            changed,
            current.WorldBounds != candidate.WorldBounds,
            CountChanged(current.Obstacles, candidate.Obstacles),
            CountChanged(current.PortalNodes, candidate.PortalNodes),
            CountChanged(current.PortalEdges, candidate.PortalEdges),
            CountChanged(current.Chokes, candidate.Chokes));
    }

    private static GameplayProfileResourceDiff CompareGameplay(
        GameplayProfileCatalogSnapshot current,
        GameplayProfileCatalogSnapshot candidate) =>
        new(
            current.StableHash != candidate.StableHash,
            CountChanged(current.UnitProfiles, candidate.UnitProfiles),
            CountChanged(current.BuildingProfiles, candidate.BuildingProfiles));

    private static int CountChanged<T>(
        ReadOnlySpan<T> current,
        ReadOnlySpan<T> candidate)
        where T : IEquatable<T>
    {
        var shared = Math.Min(current.Length, candidate.Length);
        var changed = Math.Abs(current.Length - candidate.Length);
        for (var index = 0; index < shared; index++)
        {
            changed += current[index].Equals(candidate[index]) ? 0 : 1;
        }
        return changed;
    }
}
