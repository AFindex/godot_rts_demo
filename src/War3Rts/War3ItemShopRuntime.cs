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
    private readonly Dictionary<int, List<War3ShopItemDefinition>> _inventories = [];

    public const float InteractionRange = 150f;

    // Arcane Vault order, positions, costs and stock values come from hvlt's
    // Makeitems plus ItemFunc.txt/ItemData.slk in the exported Classic data.
    public static IReadOnlyList<War3ShopItemDefinition> ArcaneVaultItems { get; } =
    [
        Item(0, "sreg", "恢复卷轴",
            "45 秒内为周围非机械友军恢复生命值。",
            "ScrollOfRegenerationGreen", "R", 0, 100, 0, 2, 90f, 0, 1),
        Item(1, "plcl", "小净化药水",
            "在一段时间内恢复英雄的魔法值。",
            "LesserClarityPotion", "C", 1, 70, 0, 2, 30f, 0, 1),
        Item(2, "mcri", "机械类的小玩艺",
            "召唤一个由玩家控制的机械侦察单位。",
            "MechanicalCritter", "E", 2, 50, 0, 2, 60f, 0, 1),
        Item(3, "phea", "生命药水",
            "立即恢复 250 点生命值。",
            "PotionGreenSmall", "P", 4, 150, 0, 3, 120f, 1, 1),
        Item(4, "pman", "魔法药水",
            "立即恢复 150 点魔法值。",
            "PotionBlueSmall", "M", 5, 200, 0, 2, 120f, 1, 1),
        Item(5, "stwp", "回城卷轴",
            "将英雄和附近单位传送到己方或友军城镇。",
            "ScrollUber", "T", 6, 350, 0, 2, 120f, 1, 1),
        Item(6, "tsct", "象牙塔",
            "在指定区域快速创建一座哨塔。",
            "HumanWatchTower", "V", 7, 40, 20, 3, 30f, 1, 1),
        Item(7, "ofir", "火焰之球",
            "为英雄攻击附加火焰伤害并允许对空远程攻击。",
            "OrbOfFire", "F", 8, 275, 0, 1, 120f, 2),
        Item(8, "ssan", "避难权杖",
            "将目标友军传送到最高等级主基地并持续治疗。",
            "StaffOfSanctuary", "N", 9, 250, 0, 1, 120f, 2)
    ];

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
        var inventory = _inventories.GetValueOrDefault(buyerUnit);
        if (inventory is null)
        {
            inventory = [];
            _inventories.Add(buyerUnit, inventory);
        }
        inventory.Add(offer.Item);
        var stock = EnsureStock(shopBuilding, offer.Item);
        stock.Count--;
        if (stock.Count == offer.Item.MaximumStock - 1)
            stock.RestockRemaining = offer.Item.RestockSeconds;
        return new War3ShopPurchaseResult(
            War3ShopPurchaseCode.Success, offer.Item, buyerUnit, stock.Count);
    }

    public int InventoryCount(int unit) =>
        _inventories.TryGetValue(unit, out var items) ? items.Count : 0;

    public War3InventoryItemSnapshot[] InventorySnapshot(int unit) =>
        !_inventories.TryGetValue(unit, out var items)
            ? []
            : items.Select(item => new War3InventoryItemSnapshot(
                item.ItemId,
                item.Name,
                item.IconPath,
                item.Description,
                item.Charges)).ToArray();

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

    private static War3ShopItemDefinition Item(
        int runtimeId,
        string itemId,
        string name,
        string description,
        string icon,
        string hotkey,
        int slot,
        int gold,
        int lumber,
        int maximumStock,
        float restockSeconds,
        int requiredTownTier,
        int charges = 0) => new(
        runtimeId, itemId, name, description,
        $@"ReplaceableTextures\CommandButtons\BTN{icon}.blp",
        hotkey, slot, new EconomyCost(gold, lumber), maximumStock,
        restockSeconds, requiredTownTier, charges);

    private readonly record struct StockKey(
        int ShopBuilding,
        int ItemRuntimeId);

    private sealed class StockState(int count, float restockRemaining)
    {
        public int Count { get; set; } = count;
        public float RestockRemaining { get; set; } = restockRemaining;
    }
}
