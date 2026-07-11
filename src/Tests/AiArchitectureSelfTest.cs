using System.Numerics;
using RtsDemo.AI;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class AiArchitectureSelfTest
{
    public static SelfTestResult Run()
    {
        var source = new FakeObservationSource();
        var executor = new FakeExecutor();
        var policy = new FakePolicy();
        var director = new RtsAiDirector(source, executor);
        director.Register(
            2, policy,
            decisionIntervalTicks: 4,
            decisionOffsetTicks: 1,
            maximumIntentsPerDecision: 1);
        for (var tick = 0; tick <= 9; tick++)
            director.Update(tick);
        var captured = director.CaptureState();

        var restoredSource = new FakeObservationSource();
        var restoredExecutor = new FakeExecutor();
        var restoredPolicy = new FakePolicy();
        var restored = new RtsAiDirector(restoredSource, restoredExecutor);
        restored.Register(2, restoredPolicy, 4, 1, 1);
        restored.RestoreState(captured);
        var resumedExecutions = restored.Update(13);

        var bounded = executor.Intents.Count == 3 &&
                      executor.Intents.All(value =>
                          value.Kind == AiIntentKind.Move &&
                          value.Units.SequenceEqual([7]));
        var scheduled = source.Captures == 3 &&
                        captured.Agents.Length == 1 &&
                        captured.Agents[0].LastDecisionTick == 9;
        var restoredExactly = resumedExecutions == 1 &&
                              restoredSource.Captures == 1 &&
                              restoredExecutor.Intents.Count == 1 &&
                              restoredPolicy.Decisions == 4;
        return new SelfTestResult(
            bounded && scheduled && restoredExactly,
            $"decisions={policy.Decisions}+{restoredPolicy.Decisions}, " +
            $"captures={source.Captures}+{restoredSource.Captures}, " +
            $"executed={executor.Intents.Count}+{restoredExecutor.Intents.Count}, " +
            $"bounded={bounded}, restored={restoredExactly}");
    }

    private sealed class FakeObservationSource : IRtsAiObservationSource
    {
        public int Captures { get; private set; }

        public AiObservationSnapshot Capture(int playerId)
        {
            Captures++;
            var view = new PlayerViewSnapshot(
                playerId,
                new SimRect(Vector2.Zero, new Vector2(1000f, 600f)),
                32f, 32, 19, new byte[608], [], [], []);
            var capability = new PlayerCapabilitySnapshot(
                playerId, MatchPlayerStatus.Active, true,
                1, 1, 1, 0, 0, 1, 1);
            return new AiObservationSnapshot(
                Captures,
                playerId,
                view,
                new PlayerEconomySnapshot(playerId, 500, 100, 2, 15),
                [],
                [],
                [],
                [],
                new MatchSnapshot(
                    MatchPhase.Running, 0, -1, -1, [capability]));
        }
    }

    private sealed class FakeExecutor : IRtsAiIntentExecutor
    {
        public List<AiIntent> Intents { get; } = [];

        public AiIntentExecutionResult Execute(int playerId, AiIntent intent)
        {
            Intents.Add(intent with { Units = intent.Units.ToArray() });
            return new(AiIntentExecutionCode.Success);
        }
    }

    private sealed class FakePolicy : IRtsAiPolicy
    {
        public string PolicyId => "test.bounded-policy";
        public int StateFormatVersion => 1;
        public int Decisions { get; private set; }

        public void Decide(
            AiObservationSnapshot observation,
            IAiIntentSink intents)
        {
            Decisions++;
            intents.TryAdd(new AiIntent(
                AiIntentKind.Move, [7], new Vector2(400f, 300f)));
            intents.TryAdd(new AiIntent(
                AiIntentKind.AttackMove, [8], new Vector2(700f, 300f)));
        }

        public byte[] CaptureState() => BitConverter.GetBytes(Decisions);

        public void RestoreState(ReadOnlySpan<byte> state, int formatVersion)
        {
            if (formatVersion != StateFormatVersion || state.Length != 4)
                throw new InvalidOperationException("Invalid fake AI state.");
            Decisions = BitConverter.ToInt32(state);
        }
    }
}
