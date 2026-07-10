using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ClearancePreviewSelfTest
{
    public static SelfTestResult Run()
    {
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(Vector2.Zero, new Vector2(1000f, 700f)),
            [],
            [
                new PortalNode(0, new Vector2(100f, 200f), "A"),
                new PortalNode(1, new Vector2(400f, 200f), "B"),
                new PortalNode(2, new Vector2(100f, 500f), "C"),
                new PortalNode(3, new Vector2(400f, 500f), "D")
            ],
            [new PortalEdge(0, 1, 22f), new PortalEdge(2, 3, 96f)],
            [],
            out var navigation,
            out var validation);
        if (!created || navigation is null || !validation.IsValid)
        {
            return new SelfTestResult(false, "clearance preview fixture is invalid");
        }

        var preview = ClearancePreviewSnapshot.Create(
            navigation, DemoGameplayProfiles.CreateSnapshot());
        var narrow = preview.Portals[0];
        var wide = preview.Portals[1];
        var passed = preview.Classes.Length == 3 &&
                     preview.Connectivity.Length == 3 &&
                     preview.Buildings.Length == 4 &&
                     preview.Classes.All(value =>
                         value.ConnectedComponents > 0 &&
                         value.WalkableCells >= value.LargestComponentCells) &&
                     narrow.SmallTraversable && narrow.MediumTraversable &&
                     !narrow.LargeTraversable && narrow.ClassLabel == "SM-" &&
                     wide.SmallTraversable && wide.MediumTraversable &&
                     wide.LargeTraversable && wide.ClassLabel == "SML";
        return new SelfTestResult(
            passed,
            $"classes={preview.Classes.Length}, " +
            $"components={string.Join('/', preview.Classes.Select(value => value.ConnectedComponents))}, " +
            $"buildings={preview.Buildings.Length}, " +
            $"narrow={narrow.ClassLabel}, wide={wide.ClassLabel}");
    }
}
