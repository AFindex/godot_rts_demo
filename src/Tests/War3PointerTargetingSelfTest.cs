using System.Numerics;
using RtsDemo.Simulation;
using War3Rts;

namespace RtsDemo.Tests;

public static class War3PointerTargetingSelfTest
{
    public static SelfTestResult Run()
    {
        var bounds = new SimRect(
            new Vector2(100f, 200f),
            new Vector2(212f, 292f));

        var center = War3PointerTargeting.HitsBuilding(
            bounds, new Vector2(156f, 246f));
        var insideEdge = War3PointerTargeting.HitsBuilding(
            bounds, new Vector2(211.99f, 246f));
        var outsideNear = War3PointerTargeting.HitsBuilding(
            bounds, new Vector2(212.01f, 246f));
        var outsideFormerSnapRing = War3PointerTargeting.HitsBuilding(
            bounds, new Vector2(250f, 246f));

        var passed = center && insideEdge &&
                     !outsideNear && !outsideFormerSnapRing;
        return new SelfTestResult(
            passed,
            $"center={center}, edge={insideEdge}, " +
            $"outside={outsideNear}, formerSnap={outsideFormerSnapRing}");
    }
}
