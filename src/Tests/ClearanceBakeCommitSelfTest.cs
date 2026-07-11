using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ClearanceBakeCommitSelfTest
{
    public static SelfTestResult Run(
        NavigationMapSnapshot? navigation = null,
        GameplayProfileCatalogSnapshot? profiles = null,
        ClearanceBakeSnapshot? clearanceBake = null)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        var candidate = ClearanceBakeSnapshot.Build(
            navigation, clearanceBake.CellSize, chunkSizeCells: 8);

        var unsupportedWorld = navigation.CreateWorld();
        var unsupported = new RtsSimulation(
            unsupportedWorld,
            new StraightLinePathProvider(),
            clearanceBake: clearanceBake);
        var unsupportedResult = unsupported.TryCommitClearanceBake(candidate);

        var recordingWorld = navigation.CreateWorld();
        var recording = new RtsSimulation(
            recordingWorld,
            new GridPathProvider(
                recordingWorld, clearanceBake.CellSize, clearanceBake),
            clearanceBake: clearanceBake);
        recording.StartCommandRecording();
        var recordingResult = recording.TryCommitClearanceBake(candidate);

        var manifestWorld = navigation.CreateWorld();
        var manifestSimulation = new RtsSimulation(
            manifestWorld,
            new GridPathProvider(
                manifestWorld, clearanceBake.CellSize, clearanceBake),
            clearanceBake: clearanceBake);
        var manifestCommit = manifestSimulation.TryCommitClearanceBake(candidate);
        var staleManifestRejected = false;
        try
        {
            manifestSimulation.StartReplayPackageRecording(
                ReplayResourceManifest.Create(
                    navigation, profiles, clearanceBake));
        }
        catch (InvalidOperationException)
        {
            staleManifestRejected = true;
        }

        var passed = unsupportedResult.Code ==
                         ClearanceBakeCommitCode.UnsupportedPathProvider &&
                     unsupportedResult.PreviousBakeHash ==
                         clearanceBake.StableHash &&
                     recordingResult.Code ==
                         ClearanceBakeCommitCode.RecordingActive &&
                     recordingResult.PreviousBakeHash ==
                         clearanceBake.StableHash &&
                     recording.Metrics.ClearanceBakeReloads == 0 &&
                     manifestCommit.Succeeded && staleManifestRejected;
        return new SelfTestResult(
            passed,
            $"unsupported={unsupportedResult.Code}, " +
            $"recording={recordingResult.Code}, " +
            $"staleManifest={staleManifestRejected}, " +
            $"reloads={recording.Metrics.ClearanceBakeReloads}");
    }
}
