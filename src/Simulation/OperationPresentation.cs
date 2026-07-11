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
    Move,
    AttackMove,
    Rally,
    Build,
    Stop,
    Hold,
    Train,
    CancelProduction,
    Research,
    CancelResearch,
    CancelConstruction
}

public enum TargetCommandKind : byte
{
    Move,
    AttackMove,
    Rally,
    Build
}

public enum TargetCommandPointerButton : byte
{
    Primary,
    Secondary
}

public enum TargetCommandResolutionKind : byte
{
    Issue,
    Cancel
}

public sealed record TargetCommandRequest(
    TargetCommandKind Kind,
    int[] UnitIds,
    int[] BuildingIds,
    string Label,
    int DataId)
{
    public static TargetCommandRequest Create(
        TargetCommandKind kind,
        IEnumerable<int> unitIds,
        IEnumerable<int> buildingIds,
        string label,
        int dataId = -1)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(label))
            throw new ArgumentOutOfRangeException(nameof(kind));
        var unitValues = unitIds.ToArray();
        var buildingValues = buildingIds.ToArray();
        if (unitValues.Any(value => value < 0) ||
            buildingValues.Any(value => value < 0))
            throw new ArgumentOutOfRangeException(nameof(unitIds));
        var units = unitValues.Distinct().Order().ToArray();
        var buildings = buildingValues.Distinct().Order().ToArray();
        if (kind is TargetCommandKind.Move or TargetCommandKind.AttackMove)
        {
            if (units.Length == 0 || buildings.Length != 0 || dataId != -1)
                throw new ArgumentException(
                    "Unit target commands require units and no buildings.");
        }
        else if (kind == TargetCommandKind.Rally)
        {
            if (buildings.Length == 0 || units.Length != 0 || dataId != -1)
                throw new ArgumentException(
                    "Rally target commands require buildings and no units.");
        }
        else if (units.Length == 0 || buildings.Length != 0 || dataId < 0)
        {
            throw new ArgumentException(
                "Build target commands require workers and a building type.");
        }
        return new TargetCommandRequest(
            kind, units, buildings, label.Trim(), dataId);
    }
}

public sealed record BuildTargetPreviewSnapshot(
    SimRect Bounds,
    int BuilderUnit,
    int ResourceNode,
    bool CanPlace,
    string Status)
{
    public static BuildTargetPreviewSnapshot Empty { get; } = new(
        default, -1, -1, false, "No preview");
}

public static class BuildTargetSnapper
{
    public static Vector2 Snap(Vector2 position, float gridSize = 8f)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            !float.IsFinite(gridSize) || gridSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(position));
        return new Vector2(
            MathF.Floor(position.X / gridSize + 0.5f) * gridSize,
            MathF.Floor(position.Y / gridSize + 0.5f) * gridSize);
    }
}

public readonly record struct TargetCommandResolution(
    TargetCommandResolutionKind Kind,
    TargetCommandKind Command,
    Vector2 Position,
    bool Queued,
    bool KeepTargeting);

public static class TargetCommandResolver
{
    public static TargetCommandResolution Resolve(
        TargetCommandRequest request,
        TargetCommandPointerButton button,
        Vector2 position,
        bool shiftPressed)
    {
        if (button == TargetCommandPointerButton.Secondary)
            return new TargetCommandResolution(
                TargetCommandResolutionKind.Cancel,
                request.Kind, default, false, false);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        var queueable = request.Kind is
            TargetCommandKind.Move or TargetCommandKind.AttackMove;
        return new TargetCommandResolution(
            TargetCommandResolutionKind.Issue,
            request.Kind,
            position,
            shiftPressed && queueable,
            shiftPressed && queueable);
    }
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
