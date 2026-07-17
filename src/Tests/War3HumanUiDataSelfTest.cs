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
                units.Count(value => War3HumanContent.DataCatalog.TryGet(
                    value.ObjectId, out var data) &&
                    data.Summary.Upgrades.Contains(
                        "Rhpm", StringComparer.Ordinal)) == 6;

            var passed = units.Count == 17 && buildings.Count == 16 &&
                         buildLayout && researchCoverage && productionCoverage &&
                         buildingCards && towerLayout && heroData && inventoryPolicy;
            return new SelfTestResult(
                passed,
                $"content={units.Count}/{buildings.Count}/" +
                $"{adaptedResearch.Length}, build={buildLayout}, " +
                $"research={researchCoverage}, production={productionCoverage}, " +
                $"cards={buildingCards}, towers={towerLayout}, " +
                $"heroes={heroData}, inventory={inventoryPolicy}");
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
