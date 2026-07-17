using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class DestinationSlotClearanceSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            var obstacle = new SimRect(
                new Vector2(100f, 100f),
                new Vector2(200f, 200f));
            var world = new StaticWorld(
                new SimRect(Vector2.Zero, new Vector2(400f, 300f)),
                obstacle);
            var units = new UnitStore(4);
            var unit = units.Add(
                new Vector2(300f, 150f),
                radius: 8.5f,
                maxSpeed: 120f,
                acceleration: 600f);
            var requested = new Vector2(210f, 150f);
            var physicalFree = world.IsDiscFree(
                requested, units.Radii[unit]);
            var navigationFree = world.IsDiscFree(
                requested, units.NavigationRadii[unit]);

            var assignments = new DestinationSlotAllocator(world).Allocate(
                units, [unit], requested);
            var assigned = assignments[unit];
            var assignedNavigationFree = world.IsDiscFree(
                assigned, units.NavigationRadii[unit]);

            var passed = physicalFree && !navigationFree &&
                         assignedNavigationFree &&
                         Vector2.DistanceSquared(assigned, requested) > 0.25f;
            return new SelfTestResult(
                passed,
                $"physical={units.Radii[unit]:0.#}/{physicalFree}, " +
                $"navigation={units.NavigationRadii[unit]:0.#}/{navigationFree}, " +
                $"requested={Point(requested)}, assigned={Point(assigned)}, " +
                $"assignedNavigationFree={assignedNavigationFree}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }

    private static string Point(Vector2 value) =>
        $"{value.X:0.#},{value.Y:0.#}";
}
