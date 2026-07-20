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
    Upgrade,
    PurchaseItem
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
    string Badge = "",
    bool AutoCastAvailable = false);

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

public readonly record struct War3InventoryItemSnapshot(
    string ItemId,
    string Name,
    string IconPath,
    string Tooltip,
    int Charges = 0,
    int Slot = -1,
    bool Usable = false,
    bool Passive = false,
    float CooldownRemaining = 0f,
    string StateLabel = "");

/// <summary>
/// One mini portrait in Warcraft's multi-selection information card. Entries
/// are ordered by subgroup priority. The HUD pages the complete selection;
/// gameplay selection itself is limited only by the live entity set.
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
    public War3InventoryItemSnapshot[] InventoryItems { get; init; } = [];
    public int PortraitTeam { get; init; }
    public bool PortraitAnimated { get; init; }
    public string[] PortraitAnimationProperties { get; init; } = [];
    public bool Controllable { get; init; }
    public bool IsShop { get; init; }
    public int ShopUserUnit { get; init; } = -1;
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

internal enum War3PointerHitTier : byte
{
    Model,
    Body,
    Assistance
}

internal static class War3PointerTargeting
{
    private const int TerrainRaySteps = 192;

    /// <summary>
    /// Returns the real squared pixel distance to a projected body segment.
    /// Unlike a normalized model-sized score, this remains comparable between
    /// a small unit, a tall unit and a building when their visuals overlap.
    /// </summary>
    public static float DistanceSquaredToSegment(
        Vector2 pointer,
        Vector2 start,
        Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.0001f)
            return pointer.DistanceSquaredTo(start);
        var distanceAlong = Math.Clamp(
            (pointer - start).Dot(segment) / lengthSquared,
            0f,
            1f);
        return pointer.DistanceSquaredTo(start + segment * distanceAlong);
    }

    /// <summary>
    /// Screen-space capsule pick score. Positive infinity means the pointer is
    /// outside the deliberately bounded click tolerance.
    /// </summary>
    public static float CapsuleHitScore(
        Vector2 pointer,
        Vector2 start,
        Vector2 end,
        float radius)
    {
        if (radius <= 0f || !float.IsFinite(radius))
            return float.PositiveInfinity;
        var score = DistanceSquaredToSegment(pointer, start, end);
        return score <= radius * radius ? score : float.PositiveInfinity;
    }

    /// <summary>
    /// Resolves overlapping screen hits. An actual model-volume ray hit wins
    /// over the projected body and click-assistance fallbacks. Only two actual
    /// volume hits use front-most intersection depth; approximate fallbacks
    /// stay ordered by real cursor distance.
    /// </summary>
    public static bool PreferLayeredScreenHit(
        War3PointerHitTier candidateTier,
        float candidateScore,
        float candidateDepth,
        int candidateId,
        War3PointerHitTier bestTier,
        float bestScore,
        float bestDepth,
        int bestId)
    {
        if (!float.IsFinite(candidateScore)) return false;
        if (bestId < 0) return true;
        if (candidateTier != bestTier) return candidateTier < bestTier;

        const float scoreTieTolerance = 0.01f;
        const float depthTieTolerance = 0.001f;
        if (candidateTier == War3PointerHitTier.Model)
        {
            if (candidateDepth < bestDepth - depthTieTolerance) return true;
            if (MathF.Abs(candidateDepth - bestDepth) > depthTieTolerance)
                return false;
            if (candidateScore < bestScore - scoreTieTolerance) return true;
            if (MathF.Abs(candidateScore - bestScore) > scoreTieTolerance)
                return false;
        }
        else
        {
            if (candidateScore < bestScore - scoreTieTolerance) return true;
            if (MathF.Abs(candidateScore - bestScore) > scoreTieTolerance)
                return false;
            if (candidateDepth < bestDepth - depthTieTolerance) return true;
            if (MathF.Abs(candidateDepth - bestDepth) > depthTieTolerance)
                return false;
        }
        return candidateId < bestId;
    }

    public static bool TryIntersectRayAabb(
        Vector3 origin,
        Vector3 direction,
        in Aabb bounds,
        out float distance)
    {
        distance = float.PositiveInfinity;
        if (direction.LengthSquared() <= 0.000001f ||
            bounds.Size.X <= 0f || bounds.Size.Y <= 0f ||
            bounds.Size.Z <= 0f)
            return false;
        var minimum = bounds.Position;
        var maximum = bounds.Position + bounds.Size;
        var near = 0f;
        var far = float.PositiveInfinity;
        if (!IntersectRaySlab(
                origin.X, direction.X, minimum.X, maximum.X,
                ref near, ref far) ||
            !IntersectRaySlab(
                origin.Y, direction.Y, minimum.Y, maximum.Y,
                ref near, ref far) ||
            !IntersectRaySlab(
                origin.Z, direction.Z, minimum.Z, maximum.Z,
                ref near, ref far))
            return false;
        distance = near;
        return float.IsFinite(distance) && distance >= 0f;
    }

    private static bool IntersectRaySlab(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float near,
        ref float far)
    {
        if (MathF.Abs(direction) <= 0.000001f)
            return origin >= minimum && origin <= maximum;
        var first = (minimum - origin) / direction;
        var second = (maximum - origin) / direction;
        if (first > second) (first, second) = (second, first);
        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far && far >= 0f;
    }

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
