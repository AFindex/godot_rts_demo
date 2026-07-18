using RtsDemo.Simulation;
using War3Rts;
using War3Rts.Data;

namespace RtsDemo.Tests;

public static class War3HumanUiDataSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            var buildings = War3HumanContent.Buildings;
            var units = War3HumanContent.Units;
            var production = War3HumanContent.CreateProductionCatalog();
            var technologies = War3HumanContent.CreateTechnologyCatalog();
            var upgrades = War3HumanContent.CreateBuildingUpgradeCatalog();

            var constructible = buildings
                .Where(value => value.Constructible)
                .ToArray();
            var buildSlots = constructible
                .Select(value => UnitSlot(value.ObjectId))
                .Order()
                .ToArray();
            var buildLayout = constructible.Length == 11 &&
                              buildSlots.SequenceEqual(Enumerable.Range(0, 11)) &&
                              constructible.Any(value =>
                                  value.ObjectId == "hwtw") &&
                              constructible.All(value =>
                                  value.ObjectId is not "hgtw" and not "hctw" and
                                      not "hatw");

            var rawResearch = buildings
                .SelectMany(value => War3HumanContent.DataCatalog.TryGet(
                    value.ObjectId, out var data)
                    ? data.Summary.Researches
                    : [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var adaptedResearch = War3HumanContent.Technologies
                .Select(value => value.ObjectId)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var researchCoverage = rawResearch.SequenceEqual(adaptedResearch) &&
                                   adaptedResearch.Length == 21 &&
                                   War3HumanContent.Technologies.All(value =>
                                       ObjectSlot(
                                           War3HumanContent.UpgradeDataCatalog,
                                           value.ObjectId) is >= 0 and < 12);

            var productionCoverage = production.Recipes.ToArray().All(recipe =>
            {
                var producer = buildings[recipe.ProducerBuildingTypeId];
                var unit = units[recipe.UnitType.Id];
                return War3HumanContent.DataCatalog.TryGet(
                           producer.ObjectId, out var data) &&
                       data.Summary.Trains.Contains(
                           unit.ObjectId, StringComparer.Ordinal) &&
                       UnitSlot(unit.ObjectId) is >= 0 and < 12;
            });

            var buildingCards = buildings.All(building =>
            {
                var occupied = new HashSet<int>();
                foreach (var recipe in production.Recipes.ToArray().Where(value =>
                             upgrades.SatisfiesBuildingType(
                                 building.TypeId,
                                 value.ProducerBuildingTypeId)))
                    if (!occupied.Add(UnitSlot(units[recipe.UnitType.Id].ObjectId)))
                        return false;
                foreach (var technology in technologies.Technologies.ToArray()
                             .Where(value => upgrades.SatisfiesBuildingType(
                                 building.TypeId,
                                 value.ResearcherBuildingTypeId)))
                    if (!occupied.Add(ObjectSlot(
                            War3HumanContent.UpgradeDataCatalog,
                            War3HumanContent.Technologies[technology.Id].ObjectId)))
                        return false;
                foreach (var upgrade in upgrades.ForSource(building.TypeId))
                    if (!occupied.Add(UnitSlot(
                            buildings[upgrade.TargetType.Id].ObjectId)))
                        return false;
                if (building.TypeId < War3HumanContent.CreateBuildingCatalog()
                        .Types.Length)
                {
                    var function = War3HumanContent.CreateBuildingCatalog()
                        .Type(building.TypeId).Function;
                    if (function is BuildingFunctionKind.Production or
                        BuildingFunctionKind.TownHall && !occupied.Add(7))
                        return false;
                }
                return true;
            });

            var towerTargets = upgrades.ForSource(War3HumanContent.ScoutTower)
                .ToArray();
            var towerLayout = towerTargets.Length == 3 &&
                              towerTargets.Select(value => UnitSlot(
                                      buildings[value.TargetType.Id].ObjectId))
                                  .Order().SequenceEqual(new[] { 8, 9, 10 });
            var towerAnimationProperties =
                buildings[War3HumanContent.ScoutTower].AnimationProperties.Length == 0 &&
                buildings[War3HumanContent.GuardTower].AnimationProperties
                    .SequenceEqual(new[] { "upgrade", "first" }) &&
                buildings[War3HumanContent.CannonTower].AnimationProperties
                    .SequenceEqual(new[] { "upgrade", "second" }) &&
                buildings[War3HumanContent.ArcaneTower].AnimationProperties
                    .SequenceEqual(new[] { "upgrade", "third" }) &&
                War3AnimationPropertyResolver.Stand(
                    buildings[War3HumanContent.GuardTower].AnimationProperties)[0] ==
                "Stand Upgrade First" &&
                War3AnimationPropertyResolver.UpgradeBirth(
                    buildings[War3HumanContent.CannonTower].AnimationProperties)[0] ==
                "Birth Upgrade Second" &&
                War3AnimationPropertyResolver.Portrait(
                    buildings[War3HumanContent.ArcaneTower].AnimationProperties)[0] ==
                "Portrait Upgrade Third";

            var playableHeroes = units.Where(value =>
                value.TypeId is War3HumanContent.Archmage or
                    War3HumanContent.MountainKing or
                    War3HumanContent.Paladin or
                    War3HumanContent.BloodMage).ToArray();
            var heroData = playableHeroes.All(value =>
                War3HumanContent.DataCatalog.TryGet(value.ObjectId, out var data) &&
                data.Summary.Abilities.Contains("AInv", StringComparer.Ordinal) &&
                data.Summary.HeroAbilities.Length == 4 &&
                data.Summary.HeroAbilities.Select(rawId => ObjectSlot(
                        War3HumanContent.AbilityDataCatalog, rawId))
                    .Order().SequenceEqual(new[] { 8, 9, 10, 11 }) &&
                data.Summary.HeroAbilities.All(rawId =>
                    War3HumanContent.AbilityDataCatalog.TryGet(
                        rawId, out var ability) &&
                    !string.IsNullOrWhiteSpace(Profile(ability, "ResearchArt"))));
            var inventoryPolicy = heroData && buildings.All(value =>
                !War3HumanContent.DataCatalog.TryGet(value.ObjectId, out var data) ||
                !data.Summary.Abilities.Contains("AInv", StringComparer.Ordinal)) &&
                War3HumanContent.Technologies.Count(value =>
                    value.SemanticKind ==
                        War3TechnologySemanticKind.BackpackInventory &&
                    value.GrantedInventorySlots == 2) == 1 &&
                units.Count(value => War3HumanContent.DataCatalog.TryGet(
                    value.ObjectId, out var data) &&
                    War3HumanContent.Technologies.Any(technology =>
                        technology.SemanticKind ==
                            War3TechnologySemanticKind.BackpackInventory &&
                        data.Summary.Upgrades.Contains(
                            technology.ObjectId, StringComparer.Ordinal))) == 6;

            var abilityPresentation =
                War3HumanContent.TryAbility("AHbz", out var blizzard) &&
                blizzard is not null &&
                blizzard.AnimationNames.FirstOrDefault() == "Stand Channel" &&
                War3HumanContent.TryAbility("AHtb", out var stormBolt) &&
                stormBolt is not null &&
                stormBolt.AnimationNames.FirstOrDefault() == "Spell Throw" &&
                stormBolt.MissileModels.Length > 0 &&
                !stormBolt.EffectModels.Intersect(
                    stormBolt.MissileModels,
                    StringComparer.OrdinalIgnoreCase).Any() &&
                War3HumanContent.TryAbility("Asph", out var spheres) &&
                spheres is not null &&
                spheres.TargetAttachments.SequenceEqual(
                    new[]
                    {
                        "sprite,first", "sprite,second", "sprite,third"
                    }) &&
                War3HumanContent.TryAbility("Ainf", out var innerFire) &&
                innerFire is not null &&
                innerFire.BuffModels.Length > 0;

            var items = War3ItemShopRuntime.ArcaneVaultItems.ToArray();
            var itemRuntime = items.Length == 9 &&
                              items.Select(value => value.RuntimeId)
                                  .SequenceEqual(Enumerable.Range(0, 9)) &&
                              items.Select(value => value.ItemId).SequenceEqual(
                                  new[]
                                  {
                                      "sreg", "mcri", "plcl", "phea", "pman",
                                      "stwp", "tsct", "ofir", "ssan"
                                  }) &&
                              items.Select(value => value.AbilityRawId)
                                  .SequenceEqual(new[]
                                  {
                                      "AIsl", "Amec", "AIpl", "AIh1", "AIm1",
                                      "AItp", "AIbt", "AIfb", "ANsa"
                                  }) &&
                              War3HumanContent.ItemDataCatalog.IsAvailable &&
                              War3HumanContent.ItemDataCatalog.Count == 273 &&
                              items.Count(value => value.RequiresTarget) == 3 &&
                              items.Count(value => value.Perishable) == 7 &&
                              items.Single(value => value.ItemId == "ofir").Passive &&
                              items.Single(value => value.ItemId == "ssan")
                                  .CooldownSeconds == 45f &&
                              items.Single(value => value.ItemId == "tsct")
                                  .Cost == new EconomyCost(40, 20) &&
                              items.Single(value => value.ItemId == "sreg")
                                  .EffectData.GetValueOrDefault("A") == 225f &&
                              items.Single(value => value.ItemId == "sreg").Area ==
                                  600f &&
                              items.Single(value => value.ItemId == "tsct")
                                  .UnitIds.FirstOrDefault() == "hwtw" &&
                              items.Single(value => value.ItemId == "ofir")
                                  .EffectData.GetValueOrDefault("A") == 5f &&
                              items.Single(value => value.ItemId == "ssan").Range ==
                                  700f &&
                              War3HumanContent.DataCatalog.TryGet("necr", out _);

            var passed = units.Count == 17 && buildings.Count == 16 &&
                         buildLayout && researchCoverage && productionCoverage &&
                         buildingCards && towerLayout && towerAnimationProperties &&
                         heroData && inventoryPolicy && abilityPresentation &&
                         itemRuntime;
            return new SelfTestResult(
                passed,
                $"content={units.Count}/{buildings.Count}/" +
                $"{adaptedResearch.Length}, build={buildLayout}, " +
                $"research={researchCoverage}, production={productionCoverage}, " +
                $"cards={buildingCards}, towers={towerLayout}, " +
                $"towerAnimations={towerAnimationProperties}, " +
                $"heroes={heroData}, inventory={inventoryPolicy}, " +
                $"abilityPresentation={abilityPresentation}, " +
                $"items={itemRuntime}[{string.Join(',', items.Select(value =>
                    $"{value.ItemId}:{value.AbilityRawId}:{value.CommandSlot}:" +
                    $"{value.Cost.Minerals}/{value.Cost.VespeneGas}:" +
                    $"{value.MaximumStock}/{value.RestockSeconds}:" +
                    $"{value.RequiredTownTier}:{value.UseKind}:" +
                    $"{value.CooldownSeconds}:{value.Passive}:" +
                    $"{value.RequiresTarget}"))}]");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }

    private static int UnitSlot(string rawId) =>
        War3HumanContent.DataCatalog.TryGetEditorValue(
            rawId, "HumanUnitFunc", "Buttonpos", out var value)
            ? ParseSlot(value)
            : -1;

    private static int ObjectSlot(War3ObjectDataCatalog catalog, string rawId) =>
        catalog.TryGet(rawId, out var data)
            ? ParseSlot(Profile(data, "Buttonpos"))
            : -1;

    private static string Profile(War3ObjectEditorData data, string field) =>
        data.Profile.FirstOrDefault(value => value.Key.Equals(
            field, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private static int ParseSlot(string? value)
    {
        var parts = value?.Split(',', StringSplitOptions.TrimEntries |
                                     StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: >= 2 } &&
               int.TryParse(parts[0], out var column) &&
               int.TryParse(parts[1], out var row) &&
               column is >= 0 and < 4 && row is >= 0 and < 3
            ? row * 4 + column
            : -1;
    }
}
