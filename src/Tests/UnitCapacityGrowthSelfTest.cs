using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class UnitCapacityGrowthSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            const int initialCapacity = 8;
            const int unitCount = 801;
            var world = new StaticWorld(new SimRect(
                Vector2.Zero, new Vector2(4_096f, 4_096f)));
            var simulation = new RtsSimulation(
                world, new StraightLinePathProvider(), initialCapacity);
            simulation.Economy.Players.RegisterPlayer(
                1, minerals: 10_000, vespeneGas: 10_000,
                supplyCapacity: 200, supplyUsed: 0);

            var units = new int[unitCount];
            for (var index = 0; index < unitCount; index++)
            {
                var position = new Vector2(
                    100f + index % 32 * 32f,
                    100f + index / 32 * 32f);
                units[index] = index % 100 == 0
                    ? simulation.AddWorker(position, 1)
                    : simulation.AddUnit(
                        position,
                        team: 1,
                        CombatProfileSnapshot.Standard);
            }

            var command = simulation.IssuePlayerMove(
                1, [units[^1]], new Vector2(1_200f, 1_100f));
            simulation.Tick(1f / 30f);
            var state = simulation.CaptureRuntimeState();
            var payload = RuntimeHotSnapshotCodec.Serialize(
                SimulationHotSnapshot.CurrentFormatVersion, 123UL, state);
            var decoded = RuntimeHotSnapshotCodec.TryDeserialize(
                payload,
                SimulationHotSnapshot.CurrentFormatVersion,
                out var identity,
                out var restoredState,
                out var validation) &&
                restoredState is not null;
            var restored = new RtsSimulation(
                new StaticWorld(new SimRect(
                    Vector2.Zero, new Vector2(4_096f, 4_096f))),
                new StraightLinePathProvider(),
                initialCapacity);
            if (decoded)
            {
                restored.RestoreRuntimeState(restoredState!);
            }

            var capacityConsistent = simulation.Units.Capacity >= unitCount &&
                simulation.Combat.Teams.Length == simulation.Units.Capacity &&
                simulation.CommandQueues.PendingCounts.Length ==
                    simulation.Units.Capacity;
            var denseIndex = new UnitStore(8);
            for (var index = 0; index < 5; index++)
            {
                denseIndex.Add(
                    new Vector2(index, index),
                    radius: 4f,
                    maxSpeed: 10f,
                    acceleration: 20f);
            }
            denseIndex.SetAlive(1, false);
            denseIndex.SetAlive(3, false);
            denseIndex.SetAlive(1, true);
            var denseIndexConsistent = denseIndex.AliveCount == 4 &&
                denseIndex.AliveUnits.SequenceEqual([0, 1, 2, 4]);
            var restoredAliveIndexConsistent =
                restored.Units.AliveCount == unitCount &&
                restored.Units.AliveUnits.SequenceEqual(
                    Enumerable.Range(0, unitCount).ToArray());
            var passed = command.Succeeded &&
                units.SequenceEqual(Enumerable.Range(0, unitCount)) &&
                capacityConsistent && decoded && identity == 123UL &&
                validation == HotSnapshotValidationCode.Success &&
                restored.Units.Count == unitCount &&
                restored.Units.Capacity == simulation.Units.Capacity &&
                denseIndexConsistent && restoredAliveIndexConsistent &&
                restored.ComputeStateHash() == simulation.ComputeStateHash();
            return new SelfTestResult(
                passed,
                $"units={simulation.Units.Count},capacity=" +
                $"{initialCapacity}->{simulation.Units.Capacity}," +
                $"command={command.Code},consistent={capacityConsistent}," +
                $"aliveIndex={denseIndexConsistent}/" +
                $"{restoredAliveIndexConsistent}," +
                $"hot={decoded}/{validation},hash=" +
                $"{(decoded && restored.ComputeStateHash() == simulation.ComputeStateHash())}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }
}
