using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ControlGroupSelfTest
{
    public static SelfTestResult Run()
    {
        var manager = new ControlGroupManager();
        var worker = new ControlGroupEntity(ControlGroupEntityKind.Unit, 3);
        var marine = new ControlGroupEntity(ControlGroupEntityKind.Unit, 8);
        var barracks = new ControlGroupEntity(ControlGroupEntityKind.Building, 2);
        var factory = new ControlGroupEntity(ControlGroupEntityKind.Building, 5);

        manager.Assign(1, [factory, worker, barracks, marine, worker]);
        manager.StealAssign(2, [worker]);
        manager.StealAdd(2, [factory]);

        var first = manager.Recall(1);
        var second = manager.Recall(2);
        var filtered = manager.Recall(2, entity => entity != worker);
        var passed = first.SequenceEqual([marine, barracks]) &&
                     second.SequenceEqual([worker, factory]) &&
                     filtered.SequenceEqual([factory]);
        return new SelfTestResult(
            passed,
            $"group1={first.Length}, group2={second.Length}, " +
            $"filtered={filtered.Length}, stable={passed}");
    }
}
