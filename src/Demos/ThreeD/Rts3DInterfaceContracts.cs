using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Immutable view model consumed by the Godot 3D HUD. It deliberately contains
/// no simulation, catalog, node or resource references.
/// </summary>
public sealed record Rts3DHudSnapshot(
    double ElapsedSeconds,
    int Minerals,
    int Gas,
    int SupplyUsed,
    int SupplyCapacity,
    int IdleWorkers,
    Rts3DSelectionPanelSnapshot Selection,
    Rts3DCommandSlotSnapshot[] CommandSlots,
    Rts3DControlGroupSnapshot[] ControlGroups,
    string Mode,
    string Status)
{
    public static Rts3DHudSnapshot Empty { get; } = new(
        0d, 0, 0, 0, 0, 0,
        Rts3DSelectionPanelSnapshot.Empty, [], [],
        "Normal selection", "Ready");
}

public sealed record Rts3DSelectionPanelSnapshot(
    string Title,
    string Subtitle,
    float Health,
    float MaximumHealth,
    SelectionSubgroupSnapshot[] Subgroups,
    int ActiveSubgroupIndex,
    string Rally,
    Rts3DQueueItemSnapshot[] Queue)
{
    public static Rts3DSelectionPanelSnapshot Empty { get; } = new(
        "No selection", "Select a unit or building", 0f, 0f, [], -1,
        string.Empty, []);
}

public readonly record struct Rts3DQueueItemSnapshot(
    string Label,
    float Progress,
    int Count,
    string State);

public readonly record struct Rts3DControlGroupSnapshot(
    int Group,
    int Units,
    int Buildings)
{
    public int Count => Units + Buildings;
}

public readonly record struct Rts3DCommandSlotSnapshot(
    int Slot,
    string Hotkey,
    string Glyph,
    CommandCardActionSnapshot Action);

public readonly record struct Rts3DRallyMarkerSnapshot(
    int BuildingId,
    NVector2 Source,
    NVector2 Target,
    RallyTargetKind Kind);

public readonly record struct Rts3DTargetModeSnapshot(
    TargetCommandKind Kind,
    string Label,
    string Hint);

/// <summary>Stable 3x5 SC-style command-card layout.</summary>
public static class Rts3DCommandLayout
{
    public const int SlotCount = 15;

    public static Rts3DCommandSlotSnapshot[] Compose(CommandCardSnapshot card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var result = new List<Rts3DCommandSlotSnapshot>(
            Math.Min(card.Actions.Length, SlotCount));
        var used = new HashSet<int>();
        foreach (var action in card.Actions)
        {
            var preferred = PreferredSlot(action);
            var slot = FindFreeSlot(preferred, used);
            if (slot < 0) break;
            used.Add(slot);
            result.Add(new Rts3DCommandSlotSnapshot(
                slot, Hotkey(action), Glyph(action), action));
        }
        return result.OrderBy(value => value.Slot).ToArray();
    }

    private static int PreferredSlot(CommandCardActionSnapshot action) =>
        action.Kind switch
        {
            CommandCardActionKind.Move => 0,
            CommandCardActionKind.AttackMove => 5,
            CommandCardActionKind.Stop => 10,
            CommandCardActionKind.Hold => 11,
            CommandCardActionKind.ReturnCargo => 4,
            CommandCardActionKind.Rally => 10,
            CommandCardActionKind.Build => 1 + action.DataId,
            CommandCardActionKind.Train => action.DataId,
            CommandCardActionKind.Research => action.DataId,
            _ => 0
        };

    private static string Hotkey(CommandCardActionSnapshot action) =>
        action.Kind switch
        {
            CommandCardActionKind.Move => "M",
            CommandCardActionKind.AttackMove => "A",
            CommandCardActionKind.Stop => "S",
            CommandCardActionKind.Hold => "H",
            CommandCardActionKind.ReturnCargo => "C",
            CommandCardActionKind.Rally => "Y",
            CommandCardActionKind.Build => action.DataId switch
            {
                0 => "Q", 1 => "W", 2 => "E", 3 => "R", 4 => "T", _ => string.Empty
            },
            CommandCardActionKind.Train or CommandCardActionKind.Research =>
                action.DataId switch
                {
                    0 => "Q", 1 => "W", 2 => "E", 3 => "R", 4 => "T",
                    _ => string.Empty
                },
            _ => string.Empty
        };

    private static string Glyph(CommandCardActionSnapshot action) =>
        action.Kind switch
        {
            CommandCardActionKind.Move => "MOVE",
            CommandCardActionKind.AttackMove => "ATK",
            CommandCardActionKind.Stop => "STOP",
            CommandCardActionKind.Hold => "HOLD",
            CommandCardActionKind.ReturnCargo => "CARGO",
            CommandCardActionKind.Rally => "RALLY",
            CommandCardActionKind.Build => "BUILD",
            CommandCardActionKind.Train => "TRAIN",
            CommandCardActionKind.Research => "TECH",
            _ => "CMD"
        };

    private static int FindFreeSlot(int preferred, HashSet<int> used)
    {
        if ((uint)preferred < SlotCount && !used.Contains(preferred))
            return preferred;
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (!used.Contains(slot)) return slot;
        }
        return -1;
    }
}

public static class Rts3DRallyGeometry
{
    /// <summary>Returns the exact rectangular edge point facing a target.</summary>
    public static NVector2 EdgeToward(SimRect bounds, NVector2 target)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var half = (bounds.Max - bounds.Min) * 0.5f;
        var direction = target - center;
        if (direction.LengthSquared() < 0.0001f) return center;
        var xScale = MathF.Abs(direction.X) > 0.0001f
            ? half.X / MathF.Abs(direction.X)
            : float.PositiveInfinity;
        var yScale = MathF.Abs(direction.Y) > 0.0001f
            ? half.Y / MathF.Abs(direction.Y)
            : float.PositiveInfinity;
        return center + direction * MathF.Min(xScale, yScale);
    }
}
