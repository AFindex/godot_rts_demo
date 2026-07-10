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
    double AverageCombatMilliseconds,
    double AveragePathMilliseconds,
    double AverageSteeringMilliseconds,
    double AverageCollisionMilliseconds,
    double AverageOtherMilliseconds);

public sealed record BenchmarkSuiteResult(
    string CreatedAtUtc,
    bool Passed,
    BenchmarkCaseResult[] Cases,
    BenchmarkCaseResult[] CombatCases);

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
        BenchmarkCaseResult[] combatCases =
        [
            RunCombatCase(128, 4.0),
            RunCombatCase(256, 8.0)
        ];
        return new BenchmarkSuiteResult(
            DateTime.UtcNow.ToString("O"),
            cases.All(result => result.Passed) &&
            combatCases.All(result => result.Passed),
            cases,
            combatCases);
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

        var p95Budget = unitCount switch
        {
            <= 256 => 4.0,
            <= 512 => 12.5,
            _ => 16.67
        };
        return Measure(rig, units, p95Budget);
    }

    private static BenchmarkCaseResult RunCombatCase(
        int unitCount,
        double p95BudgetMilliseconds)
    {
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1600f, 1000f), unitCount + 16);
        var profile = new TestCombatProfile(
            MaximumHealth: 1000f,
            AttackDamage: 0f,
            AttackRange: 70f,
            AcquisitionRange: 600f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 800f,
            Positioning: TestCombatPositioning.Ranged);
        var half = unitCount / 2;
        var left = new TestUnitId[half];
        var right = new TestUnitId[unitCount - half];
        const int columns = 16;
        const float spacing = 17f;
        for (var index = 0; index < left.Length; index++)
        {
            left[index] = rig.SpawnCombat(
                new Vector2(
                    100f + index % columns * spacing,
                    220f + index / columns * spacing),
                team: 1,
                profile,
                radius: 6f,
                maximumSpeed: 120f,
                acceleration: 680f);
        }
        for (var index = 0; index < right.Length; index++)
        {
            right[index] = rig.SpawnCombat(
                new Vector2(
                    1230f + index % columns * spacing,
                    220f + index / columns * spacing),
                team: 2,
                profile,
                radius: 6f,
                maximumSpeed: 120f,
                acceleration: 680f);
        }

        rig.AttackMove(left, new Vector2(1350f, 500f));
        rig.AttackMove(right, new Vector2(250f, 500f));
        return Measure(
            rig,
            left.Concat(right).ToArray(),
            p95BudgetMilliseconds,
            allocationBudget: 8192);
    }

    private static BenchmarkCaseResult Measure(
        MovementTestRig rig,
        TestUnitId[] units,
        double p95Budget,
        long allocationBudget = 1024)
    {
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
        var combat = 0d;
        var steering = 0d;
        var collision = 0d;
        var other = 0d;
        for (var tick = 0; tick < MeasuredTicks; tick++)
        {
            rig.Step();
            var performance = rig.ObservePerformance();
            tickTimes[tick] = performance.TotalMilliseconds;
            allocatedBytes += performance.AllocatedBytes;
            combat += performance.CombatMilliseconds;
            path += performance.PathMilliseconds;
            steering += performance.SteeringMilliseconds;
            collision += performance.CollisionMilliseconds;
            other += performance.PreferredVelocityMilliseconds +
                     performance.ChokeMilliseconds +
                     performance.SpatialHashMilliseconds +
                     performance.IntegrateMilliseconds +
                     performance.RecoveryMilliseconds +
                     performance.CommandMilliseconds;
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
        return new BenchmarkCaseResult(
            units.Length,
            MeasuredTicks,
            finite && inside && p95 <= p95Budget &&
            averageAllocation <= allocationBudget,
            p95Budget,
            allocationBudget,
            tickTimes.Average(),
            p95,
            sorted[^1],
            averageAllocation,
            combat / MeasuredTicks,
            path / MeasuredTicks,
            steering / MeasuredTicks,
            collision / MeasuredTicks,
            other / MeasuredTicks);
    }
}
