using System.Numerics;

namespace RtsDemo.Simulation;

public enum GameplaySelectionKind : byte
{
    Worker,
    CombatUnit,
    Building
}

public readonly record struct GameplaySelectionEntity(
    GameplaySelectionKind Kind,
    int EntityId,
    int TypeId,
    string TypeName,
    Vector2 Position);

public readonly record struct SelectionSubgroupKey(
    GameplaySelectionKind Kind, int TypeId);

public sealed record SelectionSubgroupSnapshot(
    SelectionSubgroupKey Key,
    string Name,
    GameplaySelectionEntity[] Members);

public sealed record GameplaySelectionSnapshot(
    GameplaySelectionEntity[] Entities,
    SelectionSubgroupSnapshot[] Subgroups,
    int ActiveSubgroupIndex)
{
    public static GameplaySelectionSnapshot Empty { get; } = new([], [], -1);
    public SelectionSubgroupSnapshot? ActiveSubgroup =>
        (uint)ActiveSubgroupIndex < (uint)Subgroups.Length
            ? Subgroups[ActiveSubgroupIndex]
            : null;

    public static GameplaySelectionSnapshot Create(
        IEnumerable<GameplaySelectionEntity> entities,
        SelectionSubgroupKey? preferred = null)
    {
        var canonical = entities
            .Where(value => value.EntityId >= 0 && value.TypeId >= 0 &&
                            !string.IsNullOrWhiteSpace(value.TypeName))
            .DistinctBy(value => (value.Kind, value.EntityId))
            .OrderBy(value => value.Kind)
            .ThenBy(value => value.TypeId)
            .ThenBy(value => value.EntityId)
            .ToArray();
        var groups = canonical
            .GroupBy(value => new SelectionSubgroupKey(value.Kind, value.TypeId))
            .Select(group => new SelectionSubgroupSnapshot(
                group.Key, group.First().TypeName, group.ToArray()))
            .ToArray();
        if (groups.Length == 0) return Empty;
        var active = preferred.HasValue
            ? Array.FindIndex(groups, value => value.Key == preferred.Value)
            : 0;
        return new GameplaySelectionSnapshot(
            canonical, groups, active >= 0 ? active : 0);
    }

    public GameplaySelectionSnapshot Cycle(int direction)
    {
        if (Subgroups.Length == 0 || direction == 0) return this;
        var next = (ActiveSubgroupIndex + Math.Sign(direction) + Subgroups.Length) %
                   Subgroups.Length;
        return this with { ActiveSubgroupIndex = next };
    }
}

public enum CommandCardActionKind : byte
{
    Stop,
    Hold,
    Train,
    CancelProduction,
    Research,
    CancelResearch,
    CancelConstruction
}

public readonly record struct CommandCardActionCandidate(
    SelectionSubgroupKey Subgroup,
    CommandCardActionKind Kind,
    int TargetEntityId,
    int DataId,
    string Label,
    bool Enabled,
    string Status,
    int SortOrder);

public sealed record CommandCardActionSnapshot(
    CommandCardActionKind Kind,
    int TargetEntityId,
    int DataId,
    string Label,
    bool Enabled,
    string Status);

public sealed record CommandCardSnapshot(
    string Title,
    int SelectionCount,
    string[] SubgroupLabels,
    int ActiveSubgroupIndex,
    CommandCardActionSnapshot[] Actions)
{
    public static CommandCardSnapshot Empty { get; } =
        new("No selection", 0, [], -1, []);
}

public static class CommandCardComposer
{
    public static CommandCardSnapshot Compose(
        GameplaySelectionSnapshot selection,
        IEnumerable<CommandCardActionCandidate> candidates)
    {
        var active = selection.ActiveSubgroup;
        if (active is null) return CommandCardSnapshot.Empty;
        var actions = candidates
            .Where(value => value.Subgroup == active.Key &&
                            !string.IsNullOrWhiteSpace(value.Label))
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Kind)
            .ThenBy(value => value.DataId)
            .Select(value => new CommandCardActionSnapshot(
                value.Kind, value.TargetEntityId, value.DataId,
                value.Label, value.Enabled, value.Status))
            .ToArray();
        return new CommandCardSnapshot(
            $"{active.Name} ×{active.Members.Length}",
            selection.Entities.Length,
            selection.Subgroups.Select(value =>
                $"{value.Name} {value.Members.Length}").ToArray(),
            selection.ActiveSubgroupIndex,
            actions);
    }
}
