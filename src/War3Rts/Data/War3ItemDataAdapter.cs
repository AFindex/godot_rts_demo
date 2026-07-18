using RtsDemo.Simulation;

namespace War3Rts.Data;

/// <summary>
/// Builds shop/inventory definitions from exported ItemData plus the referenced
/// AbilityData record. Only native behavior-family dispatch remains code; item
/// names, art, layout, costs, stock, requirements, cooldowns and effect values
/// all remain authoritative configuration.
/// </summary>
public sealed class War3ItemDataAdapter(
    War3ObjectDataCatalog itemCatalog,
    War3ObjectDataCatalog abilityCatalog)
{
    public IReadOnlyList<War3ShopItemDefinition> AdaptShop(
        IReadOnlyList<string> itemIds)
    {
        var definitions = new List<War3ShopItemDefinition>(itemIds.Count);
        for (var runtimeId = 0; runtimeId < itemIds.Count; runtimeId++)
        {
            var itemId = itemIds[runtimeId];
            if (!itemCatalog.TryGet(itemId, out var item))
                throw new InvalidDataException(
                    $"Shop item '{itemId}' is absent from item_editor_data.");
            var abilityId = item.Summary.Abilities.FirstOrDefault() ??
                            item.Summary.CooldownAbilityId;
            if (string.IsNullOrWhiteSpace(abilityId) ||
                !abilityCatalog.TryGet(abilityId, out var ability) ||
                ability.Summary.Levels.Length == 0)
                throw new InvalidDataException(
                    $"Shop item '{itemId}' has no exported ability definition.");
            definitions.Add(Adapt(runtimeId, item, ability));
        }
        return definitions;
    }

    private static War3ShopItemDefinition Adapt(
        int runtimeId,
        War3ObjectEditorData item,
        War3ObjectEditorData ability)
    {
        var level = ability.Summary.Levels[0];
        var useKind = ResolveUseKind(ability.Identity.BaseCode, level);
        var slot = item.Summary.ButtonPosition.Length >= 2
            ? item.Summary.ButtonPosition[1] * 4 +
              item.Summary.ButtonPosition[0]
            : runtimeId;
        var icon = AssetPath(item, "art");
        var charges = item.Summary.Charges > 0
            ? item.Summary.Charges
            : item.Summary.Perishable ? 1 : 0;
        return new War3ShopItemDefinition(
            runtimeId,
            item.Id,
            item.DisplayName,
            TrimQuotes(item.Summary.Description),
            icon,
            item.Summary.Hotkey,
            slot,
            new EconomyCost(item.Summary.GoldCost, item.Summary.LumberCost),
            Math.Max(1, item.Summary.StockMaximum),
            Math.Max(0.01f, item.Summary.StockReplenishSeconds),
            RequiredTownTier(item.Summary.Requirements),
            charges)
        {
            AbilityRawId = ability.Id,
            AbilityBaseCode = ability.Identity.BaseCode,
            CooldownGroup = item.Summary.CooldownAbilityId,
            UseKind = useKind,
            CooldownSeconds = level.Cooldown ?? 0f,
            Perishable = item.Summary.Perishable,
            Passive = !item.Summary.Usable,
            RequiresTarget = useKind is War3ItemUseKind.TownPortal or
                War3ItemUseKind.IvoryTower or War3ItemUseKind.SanctuaryStaff,
            CastTime = level.CastTime ?? 0f,
            Duration = level.Duration ?? 0f,
            HeroDuration = level.HeroDuration ?? level.Duration ?? 0f,
            Area = level.Area ?? 0f,
            Range = level.Range ?? 0f,
            EffectData = level.Data.ToDictionary(
                value => value.Key,
                value => float.TryParse(
                    value.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number) ? number : 0f,
                StringComparer.OrdinalIgnoreCase),
            UnitIds = level.UnitIds,
            Targets = level.Targets,
            Requirements = item.Summary.Requirements
        };
    }

    private static War3ItemUseKind ResolveUseKind(
        string baseCode,
        War3ObjectLevel level) => baseCode switch
    {
        "AIrg" => Data(level, "A") > 0f
            ? War3ItemUseKind.RegenerationScroll
            : War3ItemUseKind.ClarityPotion,
        "Amec" => War3ItemUseKind.MechanicalCritter,
        "AIhe" => War3ItemUseKind.HealingPotion,
        "AIma" => War3ItemUseKind.ManaPotion,
        "AItp" => War3ItemUseKind.TownPortal,
        "AIbl" => War3ItemUseKind.IvoryTower,
        "AIfb" => War3ItemUseKind.OrbOfFire,
        "ANsa" => War3ItemUseKind.SanctuaryStaff,
        _ => War3ItemUseKind.Unsupported
    };

    private static float Data(War3ObjectLevel level, string key) =>
        level.Data.TryGetValue(key, out var value) &&
        float.TryParse(value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var number)
            ? number
            : 0f;

    private static int RequiredTownTier(IReadOnlyList<string> requirements)
    {
        var tier = 0;
        foreach (var requirement in requirements)
        {
            if (requirement.Equals("hcas", StringComparison.OrdinalIgnoreCase) ||
                requirement.Equals("TWN3", StringComparison.OrdinalIgnoreCase))
                tier = Math.Max(tier, 2);
            else if (requirement.Equals("hkee", StringComparison.OrdinalIgnoreCase) ||
                     requirement.Equals("TWN2", StringComparison.OrdinalIgnoreCase))
                tier = Math.Max(tier, 1);
        }
        return tier;
    }

    private static string AssetPath(War3ObjectEditorData data, string kind)
    {
        if (!data.Assets.TryGetValue(kind, out var assets) ||
            assets.Length == 0)
            return string.Empty;
        var value = assets[0].ResolvedPath;
        if (string.IsNullOrWhiteSpace(value)) value = assets[0].RequestedPath;
        return value.Replace('/', '\\');
    }

    private static string TrimQuotes(string value)
    {
        var result = value.Trim();
        return result.Length >= 2 && result[0] == '"' && result[^1] == '"'
            ? result[1..^1]
            : result;
    }
}
