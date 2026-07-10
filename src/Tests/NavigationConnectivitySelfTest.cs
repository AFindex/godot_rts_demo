using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class NavigationConnectivitySelfTest
{
    public static SelfTestResult Run()
    {
        var world = new StaticWorld(
            new SimRect(Vector2.Zero, new Vector2(800f, 500f)),
            new SimRect(new Vector2(344f, 0f), new Vector2(456f, 210f)),
            new SimRect(new Vector2(344f, 290f), new Vector2(456f, 500f)));
        var analyzer = new NavigationConnectivityAnalyzer(world);
        var radius = MovementClearance.ForClass(
            MovementClass.Large).NavigationRadius;
        var baseline = analyzer.Analyze(radius);
        var blocking = analyzer.Analyze(
            radius,
            new SimRect(new Vector2(344f, 210f), new Vector2(456f, 290f)));
        var safe = analyzer.Analyze(
            radius,
            new SimRect(new Vector2(620f, 70f), new Vector2(680f, 130f)));
        var blockingReport = NavigationConnectivityComparer.Compare(
            baseline, blocking);
        var safeReport = NavigationConnectivityComparer.Compare(
            baseline, safe);
        var passed = baseline.ComponentCount == 1 &&
                     blocking.ComponentCount == 2 &&
                     !blockingReport.Preserved &&
                     blockingReport.SplitComponentCount == 1 &&
                     blockingReport.DisconnectedCellCount > 0 &&
                     safeReport.Preserved;
        return new SelfTestResult(
            passed,
            $"baseline={baseline.ComponentCount}, " +
            $"blocked={blocking.ComponentCount}, " +
            $"split={blockingReport.SplitComponentCount}, " +
            $"disconnected={blockingReport.DisconnectedCellCount}, " +
            $"safe={safeReport.Preserved}");
    }
}
