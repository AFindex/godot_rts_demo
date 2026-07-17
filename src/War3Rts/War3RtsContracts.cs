using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

public enum War3CommandKind : byte
{
    Move,
    AttackMove,
    Stop,
    Hold,
    OpenBuildMenu,
    Build,
    Train,
    Research,
    Rally,
    Ability,
    BuildingAbility,
    OpenLearnMenu,
    LearnAbility,
    Cancel,
    Upgrade
}

public enum War3CommandVisualState : byte
{
    Ready,
    Unavailable,
    Queued,
    Completed,
    Active,
    Passive,
    Learn
}

public enum War3CommandFeedbackKind : byte
{
    Move,
    Attack
}

public readonly record struct War3CommandSnapshot(
    int Slot,
    War3CommandKind Kind,
    int DataId,
    string Label,
    string Tooltip,
    string IconPath,
    string Hotkey,
    bool Enabled = true,
    float CooldownRemaining = 0f,
    float ManaCost = 0f,
    bool Toggled = false,
    War3CommandVisualState State = War3CommandVisualState.Ready,
    string Badge = "");

public enum War3QueueItemKind : byte
{
    Production,
    Research,
    BuildingUpgrade
}

public readonly record struct War3QueueItemSnapshot(
    War3QueueItemKind Kind,
    int OrderId,
    int DataId,
    string Label,
    string IconPath,
    float Progress,
    string StateLabel,
    string Tooltip,
    bool CanCancel = true);

/// <summary>
/// One mini portrait in Warcraft's multi-selection information card. Entries
/// are already ordered by subgroup priority and capped by the runtime at the
/// classic twelve-selection limit.
/// </summary>
public readonly record struct War3SelectionGroupEntry(
    int EntityId,
    bool Building,
    int TypeId,
    string SubgroupKey,
    string Name,
    string IconPath,
    float HealthRatio,
    float ManaRatio,
    bool ActiveSubgroup,
    bool Debuffed,
    int HeroLevel = 0);

public sealed record War3SelectionSnapshot(
    string Title,
    string Subtitle,
    float Health,
    float MaximumHealth,
    string PortraitSource,
    bool PortraitUsesOriginalCamera,
    string IconPath,
    int Count,
    float QueueProgress,
    string QueueLabel,
    float AttackDamage,
    float Armor,
    int Level,
    int WeaponUpgradeLevel,
    string AttackClass,
    string ArmorClass,
    bool PortraitIsBuilding)
{
    public War3QueueItemSnapshot[] QueueItems { get; init; } = [];
    public float Mana { get; init; }
    public float MaximumMana { get; init; }
    public float ManaRegeneration { get; init; }
    public int HeroExperience { get; init; }
    public int ExperienceForNextLevel { get; init; }
    public int UnspentSkillPoints { get; init; }
    public AbilityStatusFlags AbilityStatuses { get; init; }
    public AbilityBuffSnapshot[] Buffs { get; init; } = [];
    public int ActiveWeaponSlot { get; init; } = -1;
    public int WeaponCount { get; init; }
    public string WeaponTargetLabel { get; init; } = string.Empty;
    public string AttackIconPath { get; init; } = string.Empty;
    public string ArmorIconPath { get; init; } = string.Empty;
    public bool IsHero { get; init; }
    public bool SupportsInventory { get; init; }
    public int InventorySlotCount { get; init; }
    public War3SelectionGroupEntry[] GroupEntries { get; init; } = [];
    public bool IsConstructing { get; init; }
    public float ConstructionProgress { get; init; }

    public static War3SelectionSnapshot Empty { get; } = new(
        "未选择单位", "左键选择，拖动框选", 0f, 0f,
        string.Empty, false, string.Empty, 0, 0f, string.Empty,
        0f, 0f, 0, 0, "—", "—", false);
}

public readonly record struct War3MinimapEntity(
    System.Numerics.Vector2 Position,
    int Team,
    bool Building);

public readonly record struct War3MinimapResource(
    System.Numerics.Vector2 Position,
    EconomyResourceKind Kind,
    bool Depleted);

public sealed record War3HudSnapshot(
    int Gold,
    int Lumber,
    int SupplyUsed,
    int SupplyCapacity,
    double ElapsedSeconds,
    War3SelectionSnapshot Selection,
    War3CommandSnapshot[] Commands,
    War3MinimapEntity[] Entities,
    War3MinimapResource[] Resources,
    SimRect WorldBounds,
    string Mode,
    string Status)
{
    public SimRect CameraViewBounds { get; init; }
    public bool MinimapSignalMode { get; init; }

    public static War3HudSnapshot Empty { get; } = new(
        0, 0, 0, 0, 0d, War3SelectionSnapshot.Empty, [], [], [],
        new SimRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One),
        "普通选择", "正在载入人族对战…")
    {
        CameraViewBounds = new SimRect(
            System.Numerics.Vector2.Zero, System.Numerics.Vector2.One)
    };
}

internal static class War3PointerTargeting
{
    private const int TerrainRaySteps = 192;

    /// <summary>
    /// Buildings already expose their complete gameplay footprint. Unlike
    /// units and resource nodes, they must not inherit a world-space snap ring:
    /// otherwise a ground click visibly outside a large building becomes a
    /// building command target.
    /// </summary>
    public static bool HitsBuilding(in SimRect bounds, NVector2 point) =>
        bounds.Contains(point);

    /// <summary>
    /// The bottom Warcraft console blocks world commands, but the outer window
    /// edge must remain available for classic RTS edge scrolling. A navigation
    /// debug overlay is modal and is therefore still allowed to block it.
    /// </summary>
    public static bool BlocksCameraEdgeScroll(
        bool hudBlocksWorldPointer,
        bool navigationDebuggerBlocksWorldPointer) =>
        navigationDebuggerBlocksWorldPointer;

    /// <summary>
    /// Intersects a camera ray with the authoritative terrain height field.
    /// The interval must include fine-height extrema: Warcraft height data can
    /// be below the zero plane or above the next nominal cliff level.
    /// </summary>
    public static bool TryIntersectTerrainRay(
        TerrainMapSnapshot terrain,
        Vector3 origin,
        Vector3 direction,
        out NVector2 point)
    {
        point = default;
        if (direction.Y >= -0.0001f) return false;

        const float heightPadding = 1f;
        var minimumHeight = SimPlane3DTransform.ToWorldLength(
            terrain.MinimumFineHeight - heightPadding);
        var maximumHeight = SimPlane3DTransform.ToWorldLength(
            terrain.MaximumCellLevel * terrain.CliffLevelHeight +
            terrain.MaximumFineHeight + heightPadding);
        var firstPlane = (maximumHeight - origin.Y) / direction.Y;
        var secondPlane = (minimumHeight - origin.Y) / direction.Y;
        var begin = MathF.Max(0f, MathF.Min(firstPlane, secondPlane));
        var end = MathF.Max(firstPlane, secondPlane);
        if (end < begin) return false;

        var previousDistance = begin;
        var previousDelta = HeightDelta(origin + direction * previousDistance);
        for (var step = 1; step <= TerrainRaySteps; step++)
        {
            var distance = begin + (end - begin) * step / TerrainRaySteps;
            var delta = HeightDelta(origin + direction * distance);
            if (previousDelta >= 0f && delta <= 0f)
            {
                var low = previousDistance;
                var high = distance;
                for (var iteration = 0; iteration < 12; iteration++)
                {
                    var middle = (low + high) * 0.5f;
                    if (HeightDelta(origin + direction * middle) > 0f)
                        low = middle;
                    else
                        high = middle;
                }
                point = SimPlane3DTransform.ToSimulation(
                    origin + direction * high);
                return terrain.Bounds.Contains(point);
            }
            previousDistance = distance;
            previousDelta = delta;
        }
        return false;

        float HeightDelta(Vector3 worldPoint)
        {
            var simulationPoint =
                SimPlane3DTransform.ToSimulation(worldPoint);
            if (!terrain.Bounds.Contains(simulationPoint))
                return float.PositiveInfinity;
            return worldPoint.Y - SimPlane3DTransform.ToWorldLength(
                terrain.HeightAt(simulationPoint));
        }
    }
}
