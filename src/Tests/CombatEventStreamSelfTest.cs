using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class CombatEventStreamSelfTest
{
    public static SelfTestResult Run()
    {
        var stream = new CombatEventStream(3);
        for (var index = 0; index < 5; index++)
            stream.Publish(10 + index, CombatEventKind.Impact, 1,
                CombatTargetKind.Unit, 2, index + 1, 20 - index);
        var overflow = stream.ReadAfter(0);
        var tail = stream.ReadAfter(4);
        stream.Reset();
        var passed = overflow.LostEvents == 2 && overflow.LatestSequence == 5 &&
                     overflow.Events.Select(value => value.Sequence)
                         .SequenceEqual([3UL, 4UL, 5UL]) &&
                     tail.LostEvents == 0 && tail.Events.Length == 1 &&
                     tail.Events[0].Damage == 5f &&
                     stream.LatestSequence == 0 &&
                     stream.ReadAfter(0).Events.Length == 0;
        return new SelfTestResult(passed,
            $"latest={overflow.LatestSequence}, retained={overflow.Events.Length}, " +
            $"lost={overflow.LostEvents}, tail={tail.Events.Length}");
    }
}
