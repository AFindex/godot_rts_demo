using System.Numerics;

namespace RtsDemo.Tests;

public sealed record BenchmarkCaseResult(
    int Units,
    int MeasuredTicks,
    bool Passed,
    double P95BudgetMilliseconds,
    long AllocationBudgetBytes,
    double AverageTickMilliseconds,
    double P95TickMilliseconds,
    double MaximumTickMilliseconds,
    double AverageAllocatedBytes,
    double AveragePathMilliseconds,
    double AverageSteeringMilliseconds,
    double AverageCollisionMilliseconds,
    double AverageOtherMilliseconds);

public sealed record BenchmarkSuiteResult(
    string CreatedAtUtc,
    bool Passed,
    BenchmarkCaseResult[] Cases);

public static class SimulationBenchmark
{
    private const int WarmupTicks = 240;
    private const int MeasuredTicks = 360;

    public static BenchmarkSuiteResult Run()
    {
        BenchmarkCaseResult[] cases =
        [
            RunCase(256),
            RunCase(512),
            RunCase(1000)
        ];
        return new BenchmarkSuiteResult(
            DateTime.UtcNow.ToString("O"),
            cases.All(result => result.Passed),
            cases);
    }

    private static BenchmarkCaseResult RunCase(int unitCount)
    {
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1200f, 700f),
            unitCount + 32);
        var units = new TestUnitId[unitCount];
        const int columns = 40;
        const float spacing = 14.5f;
        for (var index = 0; index < unitCount; index++)
        {
            var row = index / columns;
            var column = index % columns;
            units[index] = rig.Spawn(
                new Vector2(35f + column * spacing, 100f + row * spacing),
                radius: 6f,
                maximumSpeed: 120f,
                acceleration: 680f);
        }

        const int groupSize = 25;
        for (var start = 0; start < units.Length; start += groupSize)
        {
            var count = Math.Min(groupSize, units.Length - start);
            var group = new TestUnitId[count];
            Array.Copy(units, start, group, 0, count);
            var groupIndex = start / groupSize;
            var target = new Vector2(
                930f + (groupIndex & 1) * 120f,
                105f + groupIndex % 10 * 52f);
            rig.Move(group, target);
        }

        for (var tick = 0; tick < WarmupTicks; tick++)
        {
            rig.Step();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var tickTimes = new double[MeasuredTicks];
        var allocatedBytes = 0d;
        var path = 0d;
        var steering = 0d;
        var collision = 0d;
        var other = 0d;
        for (var tick = 0; tick < MeasuredTicks; tick++)
        {
            rig.Step();
            var performance = rig.ObservePerformance();
            tickTimes[tick] = performance.TotalMilliseconds;
            allocatedBytes += performance.AllocatedBytes;
            path += performance.PathMilliseconds;
            steering += performance.SteeringMilliseconds;
            collision += performance.CollisionMilliseconds;
            other += performance.PreferredVelocityMilliseconds +
                     performance.ChokeMilliseconds +
                     performance.SpatialHashMilliseconds +
                     performance.IntegrateMilliseconds +
                     performance.RecoveryMilliseconds;
        }

        var observations = rig.Observe(units);
        var finite = observations.All(unit =>
            float.IsFinite(unit.Position.X) && float.IsFinite(unit.Position.Y) &&
            float.IsFinite(unit.Velocity.X) && float.IsFinite(unit.Velocity.Y));
        var inside = units.All(rig.IsInsideWorld);
        var sorted = tickTimes.ToArray();
        Array.Sort(sorted);
        var p95Index = Math.Clamp(
            (int)MathF.Ceiling(sorted.Length * 0.95f) - 1,
            0,
            sorted.Length - 1);
        var p95 = sorted[p95Index];
        var averageAllocation = allocatedBytes / MeasuredTicks;
        var p95Budget = unitCount switch
        {
            <= 256 => 4.0,
            <= 512 => 12.5,
            _ => 16.67
        };
        const long allocationBudget = 1024;
        return new BenchmarkCaseResult(
            unitCount,
            MeasuredTicks,
            finite && inside && p95 <= p95Budget &&
            averageAllocation <= allocationBudget,
            p95Budget,
            allocationBudget,
            tickTimes.Average(),
            p95,
            sorted[^1],
            averageAllocation,
            path / MeasuredTicks,
            steering / MeasuredTicks,
            collision / MeasuredTicks,
            other / MeasuredTicks);
    }
}
