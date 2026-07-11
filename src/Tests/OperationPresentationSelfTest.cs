using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class OperationPresentationSelfTest
{
    public static SelfTestResult Run()
    {
        var workerKey = new SelectionSubgroupKey(GameplaySelectionKind.Worker, 2);
        var selection = GameplaySelectionSnapshot.Create(
        [
            new(GameplaySelectionKind.Building, 7, 1, "Barracks", new Vector2(4f, 4f)),
            new(GameplaySelectionKind.CombatUnit, 5, 0, "Marine", new Vector2(3f, 3f)),
            new(GameplaySelectionKind.Worker, 3, 2, "SCV", new Vector2(2f, 2f)),
            new(GameplaySelectionKind.Worker, 1, 2, "SCV", new Vector2(1f, 1f)),
            new(GameplaySelectionKind.Worker, 1, 2, "SCV", new Vector2(1f, 1f))
        ], workerKey);
        var card = CommandCardComposer.Compose(selection,
        [
            new(workerKey, CommandCardActionKind.Hold, -1, -1,
                "Hold", true, "", 20),
            new(workerKey, CommandCardActionKind.Stop, -1, -1,
                "Stop", true, "", 10),
            new(new SelectionSubgroupKey(GameplaySelectionKind.Building, 1),
                CommandCardActionKind.Train, 7, 0,
                "Train Marine", false, "SupplyBlocked", 10)
        ]);
        var buildingKey = new SelectionSubgroupKey(
            GameplaySelectionKind.Building, 1);
        var building = GameplaySelectionSnapshot.Create(
            selection.Entities, buildingKey);
        var buildingCard = CommandCardComposer.Compose(building,
        [
            new(buildingKey,
                CommandCardActionKind.Train, 7, 0,
                "Train Marine", false, "SupplyBlocked", 10)
        ]);
        var passed = selection.Entities.Length == 4 &&
                     selection.Subgroups.Length == 3 &&
                     selection.ActiveSubgroup?.Key == workerKey &&
                     selection.ActiveSubgroup.Members.Select(value => value.EntityId)
                         .SequenceEqual([1, 3]) &&
                     card.Actions.Select(value => value.Kind).SequenceEqual(
                         [CommandCardActionKind.Stop, CommandCardActionKind.Hold]) &&
                     selection.Cycle(1).ActiveSubgroup?.Key.Kind ==
                         GameplaySelectionKind.CombatUnit &&
                     selection.Cycle(-1).ActiveSubgroup?.Key.Kind ==
                         GameplaySelectionKind.Building &&
                     building.ActiveSubgroup?.Key.Kind == GameplaySelectionKind.Building &&
                     buildingCard.Actions.Length == 1 &&
                     !buildingCard.Actions[0].Enabled &&
                     buildingCard.Actions[0].Status == "SupplyBlocked";
        return new SelfTestResult(
            passed,
            $"entities={selection.Entities.Length}, groups={selection.Subgroups.Length}, " +
            $"active={selection.ActiveSubgroup?.Name}, actions={card.Actions.Length}, " +
            $"building={(buildingCard.Actions.Length > 0 ? buildingCard.Actions[0].Status : "missing")}");
    }
}
