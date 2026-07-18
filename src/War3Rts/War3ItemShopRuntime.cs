using RtsDemo.Simulation;

namespace War3Rts;

public enum War3ShopPurchaseCode : byte
{
    Success,
    InvalidShop,
    NoShopUser,
    InventoryFull,
    RequirementMissing,
    OutOfStock,
    InsufficientGold,
    InsufficientLumber
}

public enum War3ItemUseKind : byte
{
    Unsupported,
    RegenerationScroll,
    ClarityPotion,
    MechanicalCritter,
    HealingPotion,
    ManaPotion,
    TownPortal,
    IvoryTower,
    OrbOfFire,
    SanctuaryStaff
}

public enum War3ItemUseCode : byte
{
    Success,
    InvalidUnit,
    InvalidSlot,
    PassiveItem,
    Cooldown,
    InvalidTarget,
    OutOfRange,
    NoEffect,
    PlacementBlocked
}

public readonly record struct War3ShopItemDefinition(
    int RuntimeId,
    string ItemId,
    string Name,
    string Description,
    string IconPath,
    string Hotkey,
    int CommandSlot,
    EconomyCost Cost,
    int MaximumStock,
    float RestockSeconds,
    int RequiredTownTier,
    int Charges = 0)
{
    public string AbilityRawId { get; init; } = string.Empty;
    public string AbilityBaseCode { get; init; } = string.Empty;
    public string CooldownGroup { get; init; } = string.Empty;
    public War3ItemUseKind UseKind { get; init; }
    public float CooldownSeconds { get; init; }
    public bool Perishable { get; init; }
    public bool Passive { get; init; }
    public bool RequiresTarget { get; init; }
    public float CastTime { get; init; }
    public float Duration { get; init; }
    public float HeroDuration { get; init; }
    public float Area { get; init; }
    public float Range { get; init; }
    public IReadOnlyDictionary<string, float> EffectData { get; init; } =
        new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    public string[] UnitIds { get; init; } = [];
    public string[] Targets { get; init; } = [];
    public string[] Requirements { get; init; } = [];

    public string RequirementLabel => RequiredTownTier switch
    {
        1 => "主城",
        2 => "城堡",
        _ => "游戏开始"
    };
}

public readonly record struct War3ShopItemOffer(
    War3ShopItemDefinition Item,
    int Stock,
    bool Available,
    War3ShopPurchaseCode Code,
    string Reason);

public readonly record struct War3ShopPurchaseResult(
    War3ShopPurchaseCode Code,
    War3ShopItemDefinition Item,
    int BuyerUnit,
    int RemainingStock)
{
    public bool Succeeded => Code == War3ShopPurchaseCode.Success;
}

/// <summary>
/// Warcraft-specific shop stock and inventory state. The scene composition
/// supplies authority, proximity and town-tier facts; this runtime only owns
/// deterministic item stock, resource transactions and inventory contents.
/// </summary>
public sealed class War3ItemShopRuntime
{
    private readonly Dictionary<StockKey, StockState> _stock = [];
    private readonly Dictionary<int, War3ShopItemDefinition?[]> _inventories = [];
    private readonly Dictionary<CooldownKey, float> _cooldowns = [];

    public const float InteractionRange = 150f;

    private static readonly Lazy<IReadOnlyList<War3ShopItemDefinition>>
        ArcaneVaultItemDefinitions = new(BuildArcaneVaultItems);

    public static IReadOnlyList<War3ShopItemDefinition> ArcaneVaultItems =>
        ArcaneVaultItemDefinitions.Value;

    public void Update(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f) return;
        foreach (var (key, state) in _stock)
        {
            var item = ArcaneVaultItems[key.ItemRuntimeId];
            if (state.Count >= item.MaximumStock)
            {
                state.RestockRemaining = item.RestockSeconds;
                continue;
            }
            state.RestockRemaining -= deltaSeconds;
            while (state.Count < item.MaximumStock &&
                   state.RestockRemaining <= 0f)
            {
                state.Count++;
                state.RestockRemaining += item.RestockSeconds;
            }
        }
        foreach (var key in _cooldowns.Keys.ToArray())
        {
            var remaining = MathF.Max(0f, _cooldowns[key] - deltaSeconds);
            if (remaining <= 0f) _cooldowns.Remove(key);
            else _cooldowns[key] = remaining;
        }
    }

    public War3ShopItemOffer Offer(
        int shopBuilding,
        int itemRuntimeId,
        int buyerUnit,
        int inventorySlots,
        int townTier,
        PlayerEconomyStore economy,
        int playerId)
    {
        if ((uint)itemRuntimeId >= (uint)ArcaneVaultItems.Count)
            return default;
        var item = ArcaneVaultItems[itemRuntimeId];
        var stock = EnsureStock(shopBuilding, item);
        if (buyerUnit < 0)
            return Unavailable(item, stock.Count,
                War3ShopPurchaseCode.NoShopUser,
                $"需要带物品栏的己方单位进入 {InteractionRange:0} 距离");
        if (inventorySlots <= 0 || InventoryCount(buyerUnit) >= inventorySlots)
            return Unavailable(item, stock.Count,
                War3ShopPurchaseCode.InventoryFull, "购买者的物品栏已满");
        if (townTier < item.RequiredTownTier)
            return Unavailable(item, stock.Count,
                War3ShopPurchaseCode.RequirementMissing,
                $"需要：{item.RequirementLabel}");
        if (stock.Count <= 0)
            return Unavailable(item, 0,
                War3ShopPurchaseCode.OutOfStock,
                $"缺货；每 {item.RestockSeconds:0} 秒补充 1 件");
        var spend = economy.ValidateSpend(playerId, item.Cost);
        if (spend.Code == EconomyTransactionCode.InsufficientMinerals)
            return Unavailable(item, stock.Count,
                War3ShopPurchaseCode.InsufficientGold, "黄金不足");
        if (spend.Code == EconomyTransactionCode.InsufficientVespeneGas)
            return Unavailable(item, stock.Count,
                War3ShopPurchaseCode.InsufficientLumber, "木材不足");
        return new War3ShopItemOffer(
            item, stock.Count, true, War3ShopPurchaseCode.Success, string.Empty);
    }

    public War3ShopPurchaseResult Purchase(
        int shopBuilding,
        int itemRuntimeId,
        int buyerUnit,
        int inventorySlots,
        int townTier,
        PlayerEconomyStore economy,
        int playerId)
    {
        if ((uint)itemRuntimeId >= (uint)ArcaneVaultItems.Count)
            return new War3ShopPurchaseResult(
                War3ShopPurchaseCode.InvalidShop, default, buyerUnit, 0);
        var offer = Offer(
            shopBuilding, itemRuntimeId, buyerUnit, inventorySlots,
            townTier, economy, playerId);
        if (!offer.Available)
            return new War3ShopPurchaseResult(
                offer.Code, offer.Item, buyerUnit, offer.Stock);
        var transaction = economy.TrySpend(playerId, offer.Item.Cost);
        if (!transaction.Succeeded)
        {
            var code = transaction.Code ==
                       EconomyTransactionCode.InsufficientVespeneGas
                ? War3ShopPurchaseCode.InsufficientLumber
                : War3ShopPurchaseCode.InsufficientGold;
            return new War3ShopPurchaseResult(
                code, offer.Item, buyerUnit, offer.Stock);
        }
        var inventory = EnsureInventory(buyerUnit, inventorySlots);
        var slot = Array.FindIndex(inventory, value => !value.HasValue);
        if (slot < 0)
            return new War3ShopPurchaseResult(
                War3ShopPurchaseCode.InventoryFull,
                offer.Item, buyerUnit, offer.Stock);
        inventory[slot] = offer.Item;
        var stock = EnsureStock(shopBuilding, offer.Item);
        stock.Count--;
        if (stock.Count == offer.Item.MaximumStock - 1)
            stock.RestockRemaining = offer.Item.RestockSeconds;
        return new War3ShopPurchaseResult(
            War3ShopPurchaseCode.Success, offer.Item, buyerUnit, stock.Count);
    }

    public int InventoryCount(int unit) =>
        _inventories.TryGetValue(unit, out var items)
            ? items.Count(value => value.HasValue)
            : 0;

    public War3InventoryItemSnapshot[] InventorySnapshot(int unit) =>
        !_inventories.TryGetValue(unit, out var items)
            ? []
            : items.Select((value, slot) => (value, slot))
                .Where(value => value.value.HasValue)
                .Select(value =>
                {
                    var item = value.value!.Value;
                    var cooldown = CooldownRemaining(unit, item);
                    var state = item.Passive
                        ? "被动生效"
                        : cooldown > 0f
                            ? $"冷却 {cooldown:0.0} 秒"
                            : item.RequiresTarget
                                ? "点击后选择目标"
                                : "点击使用";
                    return new War3InventoryItemSnapshot(
                        item.ItemId,
                        item.Name,
                        item.IconPath,
                        item.Description,
                        item.Perishable ? Math.Max(1, item.Charges) : 0,
                        value.slot,
                        !item.Passive && cooldown <= 0f,
                        item.Passive,
                        cooldown,
                        state);
                }).ToArray();

    public bool TryGetItem(
        int unit,
        int slot,
        out War3ShopItemDefinition item)
    {
        item = default;
        return _inventories.TryGetValue(unit, out var inventory) &&
               (uint)slot < (uint)inventory.Length &&
               inventory[slot] is { } value &&
               (item = value).ItemId.Length > 0;
    }

    public War3ItemUseCode ValidateUse(
        int unit,
        int slot,
        out War3ShopItemDefinition item)
    {
        if (!TryGetItem(unit, slot, out item))
            return War3ItemUseCode.InvalidSlot;
        if (item.Passive) return War3ItemUseCode.PassiveItem;
        return CooldownRemaining(unit, item) > 0f
            ? War3ItemUseCode.Cooldown
            : War3ItemUseCode.Success;
    }

    public bool CommitUse(int unit, int slot)
    {
        if (!TryGetItem(unit, slot, out var item)) return false;
        if (item.CooldownSeconds > 0f)
            _cooldowns[new CooldownKey(unit, CooldownGroup(item))] =
                item.CooldownSeconds;
        if (item.Perishable)
            _inventories[unit][slot] = null;
        return true;
    }

    public bool HasItem(int unit, War3ItemUseKind kind) =>
        _inventories.TryGetValue(unit, out var inventory) &&
        inventory.Any(value => value?.UseKind == kind);

    public float CooldownRemaining(
        int unit,
        War3ShopItemDefinition item) =>
        _cooldowns.GetValueOrDefault(
            new CooldownKey(unit, CooldownGroup(item)));

    private War3ShopItemDefinition?[] EnsureInventory(int unit, int slots)
    {
        slots = Math.Clamp(slots, 1, 6);
        if (_inventories.TryGetValue(unit, out var inventory))
        {
            if (inventory.Length >= slots) return inventory;
            Array.Resize(ref inventory, slots);
            _inventories[unit] = inventory;
            return inventory;
        }
        inventory = new War3ShopItemDefinition?[slots];
        _inventories.Add(unit, inventory);
        return inventory;
    }

    private static string CooldownGroup(War3ShopItemDefinition item) =>
        item.CooldownGroup.Length > 0
            ? item.CooldownGroup
            : item.AbilityRawId;

    private StockState EnsureStock(
        int shopBuilding,
        War3ShopItemDefinition item)
    {
        var key = new StockKey(shopBuilding, item.RuntimeId);
        if (_stock.TryGetValue(key, out var state)) return state;
        state = new StockState(item.MaximumStock, item.RestockSeconds);
        _stock.Add(key, state);
        return state;
    }

    private static War3ShopItemOffer Unavailable(
        War3ShopItemDefinition item,
        int stock,
        War3ShopPurchaseCode code,
        string reason) => new(item, stock, false, code, reason);

    private static IReadOnlyList<War3ShopItemDefinition> BuildArcaneVaultItems()
    {
        if (!War3HumanContent.DataCatalog.TryGet("hvlt", out var vault))
            throw new InvalidDataException(
                "Arcane Vault unit data is unavailable.");
        return new Data.War3ItemDataAdapter(
                War3HumanContent.ItemDataCatalog,
                War3HumanContent.AbilityDataCatalog)
            .AdaptShop(vault.Summary.MakesItems);
    }

    private readonly record struct StockKey(
        int ShopBuilding,
        int ItemRuntimeId);

    private readonly record struct CooldownKey(int Unit, string Group);

    private sealed class StockState(int count, float restockRemaining)
    {
        public int Count { get; set; } = count;
        public float RestockRemaining { get; set; } = restockRemaining;
    }
}
