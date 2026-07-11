using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class BuildingConnectivityDiffSelfTest
{
    public static readonly SimRect BlockingFootprint = new(
        new Vector2(344f, 210f), new Vector2(456f, 290f));

    public static readonly SimRect SafeFootprint = new(
        new Vector2(620f, 70f), new Vector2(680f, 130f));

    public static SelfTestResult Run()
    {
        var navigation = CreateNavigationFixture();
        var bake = ClearanceBakeSnapshot.Build(navigation);
        var blocking = BuildingConnectivityDiffSnapshot.Create(
            navigation, BlockingFootprint, bake);
        var safe = BuildingConnectivityDiffSnapshot.Create(
            navigation, SafeFootprint, bake);
        var blockingClasses = blocking.Classes.Count(value => !value.Preserved);
        var safeClasses = safe.Classes.Count(value => value.Preserved);
        var disconnected = blocking.Classes.Sum(value => value.DisconnectedCells);
        var passed = blockingClasses == 3 && safeClasses == 3 &&
                     disconnected > 0 && blocking.DirtyChunks.Length > 0 &&
                     safe.DirtyChunks.Length > 0 &&
                     blocking.Classes.All(value =>
                         value.BaselineSource ==
                             NavigationConnectivitySource.StaticBake &&
                         value.SplitComponents == 1 &&
                         value.CandidateComponents == 2);
        return new SelfTestResult(
            passed,
            $"blocked={blockingClasses}/3, safe={safeClasses}/3, " +
            $"disconnected={disconnected}, dirty=" +
            $"{blocking.DirtyChunks.Length}/{safe.DirtyChunks.Length}");
    }

    public static NavigationMapSnapshot CreateNavigationFixture()
    {
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(Vector2.Zero, new Vector2(800f, 500f)),
            [
                new SimRect(new Vector2(344f, 0f), new Vector2(456f, 210f)),
                new SimRect(new Vector2(344f, 290f), new Vector2(456f, 500f))
            ],
            [],
            [],
            [],
            out var navigation,
            out var validation);
        if (!created || navigation is null)
        {
            throw new InvalidOperationException(
                $"Connectivity diff fixture is invalid: {validation.FirstError}.");
        }
        return navigation;
    }
}
