using System.Numerics;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class Rts3DInterfaceSelfTest
{
    public static SelfTestResult Run()
    {
        var unitCard = new CommandCardSnapshot(
            "Marine ×4", 4, ["Marine 4"], 0,
        [
            new(CommandCardActionKind.Stop, -1, -1,
                "Stop", true, "Ready"),
            new(CommandCardActionKind.Move, -1, -1,
                "Move", true, "Ready"),
            new(CommandCardActionKind.Hold, -1, -1,
                "Hold", true, "Ready"),
            new(CommandCardActionKind.AttackMove, -1, -1,
                "Attack Move", true, "Ready")
        ]);
        var buildingCard = new CommandCardSnapshot(
            "Command Center", 1, ["Command Center 1"], 0,
        [
            new(CommandCardActionKind.Train, 2, 2,
                "SCV", true, "50M"),
            new(CommandCardActionKind.Rally, 2, -1,
                "Set Rally", true, "Ready")
        ]);
        var units = Rts3DCommandLayout.Compose(unitCard);
        var buildings = Rts3DCommandLayout.Compose(buildingCard);
        var bounds = new SimRect(
            new Vector2(100f, 200f), new Vector2(260f, 320f));
        var right = Rts3DRallyGeometry.EdgeToward(
            bounds, new Vector2(500f, 260f));
        var diagonal = Rts3DRallyGeometry.EdgeToward(
            bounds, new Vector2(500f, 500f));
        var rally = buildings.Single(value =>
            value.Action.Kind == CommandCardActionKind.Rally);
        var passed = units.Select(value => value.Slot).Distinct().Count() ==
                         units.Length &&
                     units.Single(value => value.Action.Kind ==
                         CommandCardActionKind.Move).Slot == 0 &&
                     units.Single(value => value.Action.Kind ==
                         CommandCardActionKind.AttackMove).Slot == 5 &&
                     units.Single(value => value.Action.Kind ==
                         CommandCardActionKind.Stop).Slot == 10 &&
                     rally.Slot == 10 && rally.Hotkey == "Y" &&
                     right == new Vector2(260f, 260f) &&
                     diagonal.X == 260f &&
                     diagonal.Y is >= 300f and <= 320f;
        return new SelfTestResult(
            passed,
            $"unitSlots={string.Join(',', units.Select(value => value.Slot))}, " +
            $"rally={rally.Slot}/{rally.Hotkey}, edge={right}/{diagonal}");
    }
}
