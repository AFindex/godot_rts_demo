using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ResourceReloadSelfTest
{
    public static SelfTestResult Run(
        NavigationMapSnapshot? navigation = null,
        GameplayProfileCatalogSnapshot? profiles = null,
        ClearanceBakeSnapshot? clearanceBake = null,
        RuntimeResourceSetSnapshot? generatedCandidate = null)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        if (!RuntimeResourceSetSnapshot.TryCreate(
                navigation, profiles, clearanceBake, out var current, out _) ||
            current is null)
        {
            return new SelfTestResult(false, "baseline resource set is invalid");
        }

        var variantNavigation = DemoResourceVariantFactory.CreateNavigationVariant(
            navigation);
        var variantProfiles = DemoResourceVariantFactory.CreateGameplayVariant(
            profiles);
        var variantBake = ClearanceBakeSnapshot.Build(variantNavigation);
        RuntimeResourceSetSnapshot.TryCreate(
            variantNavigation,
            variantProfiles,
            variantBake,
            out var expectedCandidate,
            out _);
        var candidate = generatedCandidate ?? expectedCandidate;
        if (candidate is null)
        {
            return new SelfTestResult(false, "candidate resource set is invalid");
        }

        var plan = RuntimeResourceReloadPlan.Create(current, candidate);
        var samePlan = RuntimeResourceReloadPlan.Create(current, current);
        var bakeOnly = ClearanceBakeSnapshot.Build(
            navigation, cellSize: clearanceBake.CellSize, chunkSizeCells: 8);
        RuntimeResourceSetSnapshot.TryCreate(
            navigation, profiles, bakeOnly, out var bakeCandidate, out _);
        var bakePlan = RuntimeResourceReloadPlan.Create(
            current, bakeCandidate!);
        var rejectsMismatch = !RuntimeResourceSetSnapshot.TryCreate(
            variantNavigation,
            variantProfiles,
            clearanceBake,
            out _,
            out var mismatch) &&
            mismatch.Code == RuntimeResourceSetErrorCode.BakeNavigationMismatch;

        var matchesGenerated = generatedCandidate is null ||
                               candidate.Navigation.StableHash ==
                                   variantNavigation.StableHash &&
                               candidate.GameplayProfiles.StableHash ==
                                   variantProfiles.StableHash &&
                               candidate.ClearanceBake.StableHash ==
                                   variantBake.StableHash;
        var passed = plan.Impact == ResourceReloadImpact.RebuildSimulation &&
                     plan.Navigation.ChangedObstacles == 1 &&
                     plan.GameplayProfiles.ChangedUnitProfiles == 1 &&
                     plan.GameplayProfiles.ChangedBuildingProfiles == 0 &&
                     plan.ClearanceBake.Changed &&
                     samePlan.Impact == ResourceReloadImpact.None &&
                     bakePlan.Impact == ResourceReloadImpact.RefreshPathingCaches &&
                     rejectsMismatch && matchesGenerated;
        return new SelfTestResult(
            passed,
            $"impact={plan.Impact}, obstacles={plan.Navigation.ChangedObstacles}, " +
            $"units={plan.GameplayProfiles.ChangedUnitProfiles}, " +
            $"buildings={plan.GameplayProfiles.ChangedBuildingProfiles}, " +
            $"bake={plan.ClearanceBake.Changed}, bakeOnly={bakePlan.Impact}, " +
            $"mismatch={rejectsMismatch}, generated={matchesGenerated}");
    }

}
