using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ChokeCommandReplacementSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            var units = new UnitStore(2);
            var unit = units.Add(
                new Vector2(5f, 10f),
                radius: 6f,
                maxSpeed: 120f,
                acceleration: 600f);
            var controller = new ChokeController(
                new ChokeDefinition(
                    0,
                    new Vector2(10f, 10f),
                    new Vector2(30f, 10f),
                    Width: 28f,
                    ApproachDistance: 40f));

            SetActiveProgress(units, unit, ChokePhase.Approaching, waitTicks: 17);
            controller.AssignForMove(
                units,
                [unit],
                new Vector2(5f, 10f),
                new Vector2(50f, 10f),
                [0]);
            var approachPreserved = HasProgress(
                units, unit, ChokePhase.Approaching, direction: 1, waitTicks: 17);

            units.Positions[unit] = new Vector2(20f, 10f);
            SetActiveProgress(units, unit, ChokePhase.Traversing, waitTicks: 29);
            controller.AssignForMove(
                units,
                [unit],
                new Vector2(20f, 10f),
                new Vector2(50f, 10f),
                [0]);
            var traversalPreserved = HasProgress(
                units, unit, ChokePhase.Traversing, direction: 1, waitTicks: 29);

            controller.AssignForMove(
                units,
                [unit],
                new Vector2(20f, 10f),
                new Vector2(-10f, 10f),
                [0]);
            var reversalReset =
                units.ActiveChokeIds[unit] == 0 &&
                units.ChokeDirections[unit] == -1 &&
                units.ChokePhases[unit] == ChokePhase.None &&
                !units.ChokeAdmitted[unit] &&
                units.ChokeWaitTicks[unit] == 0;

            var farPhaseStable = ProbeStablePhase(
                controller, units, unit, new Vector2(-50f, 10f),
                direction: 1, ChokePhase.None);
            var nearPhaseStable = ProbeStablePhase(
                controller, units, unit, new Vector2(-10f, 10f),
                direction: 1, ChokePhase.Approaching);
            var reverseFarPhaseStable = ProbeStablePhase(
                controller, units, unit, new Vector2(90f, 10f),
                direction: -1, ChokePhase.None);
            var reverseNearPhaseStable = ProbeStablePhase(
                controller, units, unit, new Vector2(50f, 10f),
                direction: -1, ChokePhase.Approaching);

            var passed = approachPreserved && traversalPreserved && reversalReset &&
                         farPhaseStable && nearPhaseStable &&
                         reverseFarPhaseStable && reverseNearPhaseStable;
            return new SelfTestResult(
                passed,
                $"approach={approachPreserved}, " +
                $"inside={traversalPreserved}, reversal={reversalReset}, " +
                $"far-stable={farPhaseStable}, near-stable={nearPhaseStable}, " +
                $"reverse-far-stable={reverseFarPhaseStable}, " +
                $"reverse-near-stable={reverseNearPhaseStable}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }

    private static bool ProbeStablePhase(
        ChokeController controller,
        UnitStore units,
        int unit,
        Vector2 position,
        sbyte direction,
        ChokePhase expected)
    {
        units.Positions[unit] = position;
        units.Modes[unit] = UnitMoveMode.Moving;
        units.ActiveChokeIds[unit] = 0;
        units.ChokeDirections[unit] = direction;
        units.ChokePhases[unit] = ChokePhase.None;
        units.ChokeAdmitted[unit] = false;
        controller.ApplyPreferredVelocities(units);
        var first = units.ChokePhases[unit];
        controller.ApplyPreferredVelocities(units);
        return first == expected && units.ChokePhases[unit] == expected &&
               (expected != ChokePhase.None || !units.ChokeAdmitted[unit]);
    }

    private static void SetActiveProgress(
        UnitStore units,
        int unit,
        ChokePhase phase,
        int waitTicks)
    {
        units.ActiveChokeIds[unit] = 0;
        units.ChokeDirections[unit] = 1;
        units.ChokeLaneOffsets[unit] = 3f;
        units.ChokePhases[unit] = phase;
        units.ChokeAdmitted[unit] = true;
        units.ChokeQueueRanks[unit] = 2;
        units.ChokeWaitTicks[unit] = waitTicks;
    }

    private static bool HasProgress(
        UnitStore units,
        int unit,
        ChokePhase phase,
        sbyte direction,
        int waitTicks) =>
        units.ActiveChokeIds[unit] == 0 &&
        units.ChokeDirections[unit] == direction &&
        units.ChokeLaneOffsets[unit] == 3f &&
        units.ChokePhases[unit] == phase &&
        units.ChokeAdmitted[unit] &&
        units.ChokeQueueRanks[unit] == 2 &&
        units.ChokeWaitTicks[unit] == waitTicks;
}
