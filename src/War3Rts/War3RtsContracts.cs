using RtsDemo.Simulation;

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
    LearnAbility,
    Cancel
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
    bool Toggled = false);

public enum War3QueueItemKind : byte
{
    Production,
    Research
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
    public AbilityStatusFlags AbilityStatuses { get; init; }
    public AbilityBuffSnapshot[] Buffs { get; init; } = [];

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
    public static War3HudSnapshot Empty { get; } = new(
        0, 0, 0, 0, 0d, War3SelectionSnapshot.Empty, [], [], [],
        new SimRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One),
        "普通选择", "正在载入人族对战…");
}
