using System.Collections.Immutable;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;
using War3Rts.Data;

namespace War3Rts;

public sealed record War3UnitDefinition(
    int TypeId,
    string ObjectId,
    string Name,
    string Role,
    string ModelSource,
    string PortraitSource,
    string IconPath,
    float FlyingHeight = 0f,
    string ProjectileSource = "",
    string ImpactSource = "",
    string SpecialEffectSource = "",
    int Level = 1,
    string AttackClass = "",
    string ArmorClass = "",
    string AbilitySummary = "");

public sealed record War3BuildingDefinition(
    int TypeId,
    string ObjectId,
    string Name,
    string Role,
    string ModelSource,
    string IconPath,
    string SpecialEffectSource = "",
    string ArmorClass = "",
    bool Constructible = true);

/// <summary>
/// Human-faction composition boundary. Stable dense IDs remain in the core
/// simulation while Warcraft object IDs are resolved through a lazy data
/// repository and adapted into the existing movement, combat, economy,
/// construction, production and presentation profiles.
/// </summary>
public static class War3HumanContent
{
    public const int Farm = 0;
    public const int Barracks = 1;
    public const int TownHall = 2;
    public const int Blacksmith = 3;
    public const int AltarOfKings = 4;
    public const int LumberMill = 5;
    public const int ArcaneSanctum = 6;
    public const int Workshop = 7;
    public const int GryphonAviary = 8;
    public const int ArcaneVault = 9;
    public const int GuardTower = 10;
    public const int Keep = 11;
    public const int Castle = 12;

    public const int Footman = 0;
    public const int Rifleman = 1;
    public const int Peasant = 2;
    public const int Knight = 3;
    public const int Priest = 4;
    public const int Sorceress = 5;
    public const int SpellBreaker = 6;
    public const int MortarTeam = 7;
    public const int FlyingMachine = 8;
    public const int SiegeEngine = 9;
    public const int GryphonRider = 10;
    public const int DragonhawkRider = 11;
    public const int Archmage = 12;
    public const int MountainKing = 13;
    public const int Paladin = 14;
    public const int BloodMage = 15;
    public const int Militia = 16;

    private static readonly Lazy<War3HumanContentBundle> Runtime =
        new(BuildRuntime, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<War3BuildingDefinition> Buildings =>
        Runtime.Value.Buildings;

    public static IReadOnlyList<War3UnitDefinition> Units =>
        Runtime.Value.Units;

    /// <summary>
    /// Runtime query surface for all 837 indexed records, including objects not
    /// yet bound to this faction's playable dense IDs.
    /// </summary>
    public static IWar3UnitDataCatalog DataCatalog => Runtime.Value.DataCatalog;

    public static War3GameplayImportReport DataStatus => Runtime.Value.DataStatus;

    public static War3ObjectDataCatalog AbilityDataCatalog =>
        Runtime.Value.AbilityDataCatalog;

    public static War3ObjectDataCatalog UpgradeDataCatalog =>
        Runtime.Value.UpgradeDataCatalog;

    public static War3ObjectDataCatalog BuffEffectDataCatalog =>
        Runtime.Value.BuffEffectDataCatalog;

    public static War3AbilityMetadataCatalog AbilityMetadataCatalog =>
        Runtime.Value.AbilityMetadataCatalog;

    public static War3ObjectDataImportReport ObjectDataStatus =>
        Runtime.Value.ObjectDataStatus;

    public static IReadOnlyList<War3AbilityDefinition> Abilities =>
        Runtime.Value.Abilities;

    public static War3AbilityImportResult AbilityImportStatus =>
        Runtime.Value.AbilityImportStatus;

    public static IReadOnlyList<War3TechnologyDefinition> Technologies =>
        Runtime.Value.Technologies;

    public static BuildingTypeCatalogSnapshot CreateBuildingCatalog() =>
        Runtime.Value.BuildingCatalog;

    public static ProductionCatalogSnapshot CreateProductionCatalog() =>
        Runtime.Value.ProductionCatalog;

    public static BuildingUpgradeCatalogSnapshot CreateBuildingUpgradeCatalog() =>
        Runtime.Value.BuildingUpgradeCatalog;

    public static TechnologyCatalogSnapshot CreateTechnologyCatalog() =>
        Runtime.Value.TechnologyCatalog;

    public static AbilityCatalogSnapshot CreateAbilityCatalog() =>
        Runtime.Value.AbilityCatalog;

    public static War3AbilityDefinition Ability(int id) =>
        Runtime.Value.Abilities[id];

    public static bool TryAbility(
        string rawId,
        out War3AbilityDefinition? definition)
    {
        definition = Runtime.Value.Abilities.FirstOrDefault(value =>
            value.ObjectId.Equals(rawId, StringComparison.Ordinal));
        return definition is not null;
    }

    public static War3UnitDefinition ResolveUnit(
        RtsSimulation simulation,
        ProductionCatalogSnapshot catalog,
        int unit)
    {
        if (simulation.Abilities.TrySummonedObjectId(unit, out var objectId) &&
            Runtime.Value.SummonedUnits.TryGetValue(objectId, out var summoned))
            return summoned;
        var unitTypeId = simulation.Abilities.Observe(unit).UnitTypeId;
        if ((uint)unitTypeId < (uint)Units.Count)
            return Units[unitTypeId];
        var isWorker = simulation.Economy.IsWorker(unit);
        var radius = simulation.Units.Radii[unit];
        var health = simulation.Combat.MaximumHealth[unit];
        var profile = catalog.UnitTypes.ToArray()
            .Where(value => value.IsWorker == isWorker)
            .OrderBy(value => MathF.Abs(value.Movement.PhysicalRadius - radius) * 20f +
                              MathF.Abs(value.Combat.MaximumHealth - health))
            .ThenBy(value => value.Id)
            .First();
        return Units[profile.Id];
    }

    public static string GoldMineSource =>
        @"buildings\Other\GoldMine\GoldMine.mdx";

    public static string TreeSource(int variant) =>
        $@"Doodads\Terrain\LordaeronTree\LordaeronTree{Math.Abs(variant) % 10}.mdx";

    private static War3HumanContentBundle BuildRuntime()
    {
        var fallbackUnits = CreateFallbackUnitDefinitions();
        var fallbackBuildings = CreateFallbackBuildingDefinitions();
        var dataCatalog = War3UnitDataCatalog.Open(
            War3AssetPack.AbsolutePath("data/unit_editor_data"));
        var abilityCatalog = War3ObjectDataCatalog.OpenAbility(
            War3AssetPack.AbsolutePath("data/ability_editor_data"));
        var upgradeCatalog = War3ObjectDataCatalog.OpenUpgrade(
            War3AssetPack.AbsolutePath("data/upgrade_editor_data"));
        var buffEffectCatalog = War3ObjectDataCatalog.OpenBuffEffect(
            War3AssetPack.AbsolutePath("data/buff_effect_editor_data"));
        var abilityMetadataCatalog = War3AbilityMetadataCatalog.Open(
            War3AssetPack.AbsolutePath("data/ability_metadata"));
        var adapter = new War3GameplayDataAdapter(
            dataCatalog, War3GameplayImportPolicy.Default)
        {
            WeaponUnlockTechnologies = new Dictionary<string, int>(
                StringComparer.Ordinal)
            {
                [War3GameplayDataAdapter.WeaponUnlockKey("hgyr", 1)] = 4,
                [War3GameplayDataAdapter.WeaponUnlockKey("hmtt", 1)] = 6
            }
        };

        var units = fallbackUnits
            .Select(adapter.ApplyPresentation)
            .ToArray();
        for (var index = 0; index < units.Length; index++)
        {
            if (!dataCatalog.TryGet(units[index].ObjectId, out var unitData))
                continue;
            var abilityNames = unitData.Summary.Abilities
                .Concat(unitData.Summary.HeroAbilities)
                .Where(abilityCatalog.Contains)
                .Select(id => abilityCatalog.TryGet(id, out var ability)
                    ? ability.DisplayName
                    : id)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray();
            units[index] = units[index] with
            {
                AbilitySummary = abilityNames.Length == 0
                    ? string.Empty
                    : $"技能：{string.Join("、", abilityNames)}"
            };
        }
        var buildings = fallbackBuildings
            .Select(adapter.ApplyPresentation)
            .ToArray();

        var unitProfiles = CreateFallbackUnitProfiles();
        for (var index = 0; index < unitProfiles.Length; index++)
            unitProfiles[index] = adapter.ApplyUnitProfile(
                units[index], unitProfiles[index]);

        var buildingProfiles = CreateFallbackBuildingProfiles();
        for (var index = 0; index < buildingProfiles.Length; index++)
            buildingProfiles[index] = adapter.ApplyBuildingProfile(
                buildings[index], buildingProfiles[index]);

        var recipes = CreateFallbackRecipes(unitProfiles);
        for (var index = 0; index < recipes.Length; index++)
        {
            var unit = unitProfiles[recipes[index].UnitType.Id];
            recipes[index] = adapter.ApplyRecipe(
                units[unit.Id], recipes[index], unit);
        }

        var buildingCatalog = CreateBuildingCatalog(buildingProfiles);
        var buildingUpgradeCatalog = CreateBuildingUpgradeCatalog(
            adapter.CreateBuildingUpgrades(buildings, buildingProfiles),
            buildingCatalog);
        var productionCatalog = CreateProductionCatalog(unitProfiles, recipes);
        var fallbackTechnologyValues = CreateFallbackTechnologies().ToList();
        var technologyBindings = new List<(
            string ObjectId, string Icon, int Researcher)>
        {
            ("Rhme", Btn("SteelMelee"), Blacksmith),
            ("Rhar", Btn("HumanArmorUpOne"), Blacksmith),
            ("Rhac", Btn("ImbuedMasonry"), Blacksmith),
            ("Rhde", Btn("SelectHeroOn"), Barracks),
            ("Rhgb", Btn("SelectHeroOn"), Workshop),
            ("Rhfl", Btn("SelectHeroOn"), Workshop),
            ("Rhrt", Btn("SelectHeroOn"), Workshop),
            ("Rhfc", Btn("SelectHeroOn"), Workshop),
            ("Rhfs", Btn("SelectHeroOn"), Workshop),
            ("Rhpt", Btn("SelectHeroOn"), ArcaneSanctum),
            ("Rhst", Btn("SelectHeroOn"), ArcaneSanctum),
            ("Rhss", Btn("SelectHeroOn"), ArcaneSanctum),
            ("Rhpm", Btn("SelectHeroOn"), TownHall),
            ("Rhhb", Btn("SelectHeroOn"), GryphonAviary),
            ("Rhcd", Btn("SelectHeroOn"), GryphonAviary)
        };
        for (var index = fallbackTechnologyValues.Count;
             index < technologyBindings.Count;
             index++)
        {
            var binding = technologyBindings[index];
            fallbackTechnologyValues.Add(new TechnologyProfile(
                index,
                binding.ObjectId,
                binding.Researcher,
                new EconomyCost(0, 0),
                1f,
                1,
                0.75f,
                -1)
            {
                Requirements = [new TechnologyRequirementProfile(
                    TechnologyRequirementKind.CompletedBuilding,
                    binding.Researcher,
                    1)]
            });
        }
        var fallbackTechnologies = fallbackTechnologyValues.ToArray();
        var technologyIdMap = technologyBindings
            .Select((value, index) => (value.ObjectId, Index: index))
            .ToDictionary(
                value => value.ObjectId,
                value => value.Index,
                StringComparer.Ordinal);
        var buildingTypeIdMap = buildings.ToDictionary(
            value => value.ObjectId,
            value => value.TypeId,
            StringComparer.Ordinal);
        var technologyAdapter = new War3TechnologyDataAdapter(upgradeCatalog)
        {
            BuildingTypeIds = buildingTypeIdMap,
            TechnologyIds = technologyIdMap
        };
        var technologyDefinitions = new War3TechnologyDefinition[
            fallbackTechnologies.Length];
        var appliedTechnologies = 0;
        var fallbackTechnologyIds = new List<string>();
        for (var index = 0; index < fallbackTechnologies.Length; index++)
        {
            var binding = technologyBindings[index];
            var adapted = technologyAdapter.Apply(
                binding.ObjectId, fallbackTechnologies[index], binding.Icon);
            fallbackTechnologies[index] = adapted.Profile;
            technologyDefinitions[index] = adapted.Definition;
            if (adapted.Applied) appliedTechnologies++;
            else fallbackTechnologyIds.Add(binding.ObjectId);
        }
        var technologyCatalog = CreateTechnologyCatalog(fallbackTechnologies);
        var technologyIds = technologyDefinitions.ToDictionary(
            value => value.ObjectId,
            value => value.TechnologyId,
            StringComparer.Ordinal);
        var buildingIds = buildings.ToDictionary(
            value => value.ObjectId,
            value => value.TypeId,
            StringComparer.Ordinal);
        var unitTypes = units.ToDictionary(
            value => value.ObjectId,
            value => unitProfiles[value.TypeId],
            StringComparer.Ordinal);
        var abilityImport = new War3AbilityDataAdapter(
            abilityCatalog, buffEffectCatalog, dataCatalog,
            War3GameplayImportPolicy.Default,
            technologyIds,
            buildingIds,
            unitTypes)
            .Build(units, buildings);
        var report = adapter.CreateReport(
            units.Select(value => value.ObjectId),
            buildings.Select(value => value.ObjectId));
        var referencedAbilities = units
            .Select(value => dataCatalog.TryGet(value.ObjectId, out var data)
                ? data.Summary.Abilities.Concat(data.Summary.HeroAbilities)
                : [])
            .SelectMany(value => value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var referencedUpgrades = units
            .Select(value => dataCatalog.TryGet(value.ObjectId, out var data)
                ? data.Summary.Upgrades
                : [])
            .SelectMany(value => value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var objectReport = new War3ObjectDataImportReport(
            abilityCatalog.IsAvailable,
            abilityCatalog.Count,
            referencedAbilities.Length,
            referencedAbilities.Where(id => !abilityCatalog.Contains(id)).ToArray(),
            upgradeCatalog.IsAvailable,
            upgradeCatalog.Count,
            referencedUpgrades.Length,
            referencedUpgrades.Where(id => !upgradeCatalog.Contains(id)).ToArray(),
            appliedTechnologies,
            fallbackTechnologyIds);
        var summonedUnits = CreateSummonedUnitDefinitions(dataCatalog);
        return new War3HumanContentBundle(
            units,
            buildings,
            dataCatalog,
            report,
            abilityCatalog,
            upgradeCatalog,
            buffEffectCatalog,
            abilityMetadataCatalog,
            objectReport,
            technologyDefinitions,
            abilityImport.Definitions,
            abilityImport,
            buildingCatalog,
            buildingUpgradeCatalog,
            productionCatalog,
            technologyCatalog,
            abilityImport.Catalog,
            summonedUnits);
    }

    private static IReadOnlyDictionary<string, War3UnitDefinition>
        CreateSummonedUnitDefinitions(IWar3UnitDataCatalog catalog)
    {
        var result = new Dictionary<string, War3UnitDefinition>(
            StringComparer.Ordinal);
        foreach (var objectId in new[] { "hwat", "hwt2", "hwt3", "hphx" })
        {
            if (!catalog.TryGet(objectId, out var data)) continue;
            var model = ModelAsset(data.Assets.Model, string.Empty);
            if (model.Length == 0) continue;
            var portrait = ModelAsset(data.Assets.Portrait, model);
            var icon = PlainAsset(data.Assets.Icon, Btn("SelectHeroOn"));
            var missile = ModelAsset(data.Assets.Missile, string.Empty);
            var special = ModelAsset(data.Assets.SpecialEffect, string.Empty);
            var attack = data.Summary.Combat.Attacks.FirstOrDefault(value =>
                value.Enabled);
            result.Add(objectId, new War3UnitDefinition(
                -1,
                objectId,
                string.IsNullOrWhiteSpace(data.DisplayName)
                    ? objectId
                    : data.DisplayName,
                "召唤单位",
                model,
                portrait,
                icon,
                data.Summary.Movement.FlyingHeight ?? 0f,
                missile,
                string.Empty,
                special,
                Math.Max(1, data.Summary.Level ?? 1),
                attack?.AttackType ?? string.Empty,
                data.Summary.Armor.Type ?? string.Empty));
        }
        return result;
    }

    private static string PlainAsset(
        War3UnitAssetReference? asset,
        string fallback)
    {
        var value = asset?.ResolvedPath;
        if (string.IsNullOrWhiteSpace(value)) value = asset?.RequestedPath;
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return value.Split(',', StringSplitOptions.TrimEntries |
                                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Replace('/', '\\') ?? fallback;
    }

    private static string ModelAsset(
        War3UnitAssetReference? asset,
        string fallback)
    {
        var path = PlainAsset(asset, fallback);
        if (path.Length == 0) return path;
        var extension = Path.GetExtension(path);
        if (extension.Length == 0) return path + ".mdx";
        return extension.Equals(".mdl", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(path, ".mdx")
            : path;
    }

    private static BuildingTypeCatalogSnapshot CreateBuildingCatalog(
        BuildingTypeProfile[] definitions)
    {
        if (!BuildingTypeCatalogSnapshot.TryCreate(
                BuildingTypeCatalogSnapshot.CurrentFormatVersion,
                definitions, out var catalog, out var validation) || catalog is null)
            throw new InvalidOperationException(
                $"Warcraft Human building catalog is invalid: {validation.FirstError}.");
        return catalog;
    }

    private static ProductionCatalogSnapshot CreateProductionCatalog(
        UnitTypeProfile[] units,
        ProductionRecipeProfile[] recipes)
    {
        if (!ProductionCatalogSnapshot.TryCreate(
                ProductionCatalogSnapshot.CurrentFormatVersion,
                units, recipes, out var catalog, out var validation) || catalog is null)
            throw new InvalidOperationException(
                $"Warcraft Human production catalog is invalid: " +
                $"{validation.Code} {validation.Message}");
        return catalog;
    }

    private static BuildingUpgradeCatalogSnapshot CreateBuildingUpgradeCatalog(
        BuildingUpgradeProfile[] values,
        BuildingTypeCatalogSnapshot buildings)
    {
        if (!BuildingUpgradeCatalogSnapshot.TryCreate(
                BuildingUpgradeCatalogSnapshot.CurrentFormatVersion,
                values,
                out var catalog,
                out var error) || catalog is null ||
            !BuildingUpgradeCatalogSnapshot.TryValidateDependencies(
                catalog, buildings, out error))
            throw new InvalidOperationException(
                $"Warcraft Human building upgrades are invalid: {error}");
        return catalog;
    }

    private static TechnologyCatalogSnapshot CreateTechnologyCatalog(
        TechnologyProfile[] values)
    {
        if (!TechnologyCatalogSnapshot.TryCreate(
                TechnologyCatalogSnapshot.CurrentFormatVersion,
                values, out var catalog, out var error) || catalog is null)
            throw new InvalidOperationException(error);
        return catalog;
    }

    private static War3BuildingDefinition[] CreateFallbackBuildingDefinitions() =>
    [
        new(Farm, "hhou", "农场", "提供人口",
            @"buildings\Human\Farm\Farm.mdx", Btn("Farm")),
        new(Barracks, "hbar", "兵营", "训练步兵、火枪手与骑士",
            @"buildings\Human\HumanBarracks\HumanBarracks.mdx", Btn("HumanBarracks")),
        new(TownHall, "htow", "城镇大厅", "训练农民并接收黄金与木材",
            @"Buildings\Human\TownHall\TownHall.mdx", Btn("TownHall")),
        new(Blacksmith, "hbla", "铁匠铺", "研究人族武器与护甲",
            @"buildings\Human\Blacksmith\Blacksmith.mdx", Btn("Blacksmith")),
        new(AltarOfKings, "halt", "国王祭坛", "召唤人族英雄",
            @"buildings\Human\AltarOfKings\AltarOfKings.mdx", Btn("AltarOfKings")),
        new(LumberMill, "hlum", "伐木场", "木材科技与远程升级",
            @"buildings\Human\HumanLumberMill\HumanLumberMill.mdx", Btn("HumanLumberMill")),
        new(ArcaneSanctum, "hars", "神秘圣地", "训练法师部队",
            @"buildings\Human\ArcaneSanctum\ArcaneSanctum.mdx", Btn("ArcaneSanctum")),
        new(Workshop, "harm", "车间", "生产机械单位",
            @"buildings\Human\Workshop\Workshop.mdx", Btn("Workshop")),
        new(GryphonAviary, "hgra", "狮鹫笼", "训练空中部队",
            @"Buildings\Human\GryphonAviary\GryphonAviary.mdx", Btn("GryphonAviary")),
        new(ArcaneVault, "hvlt", "神秘藏宝室", "人族物品商店",
            @"Buildings\Human\ArcaneVault\ArcaneVault.mdx", Btn("ArcaneVault")),
        new(GuardTower, "hgtw", "防御塔", "基地防御建筑",
            @"Buildings\Human\HumanTower\HumanTower.mdx", Btn("GuardTower")),
        new(Keep, "hkee", "主城", "二级城镇大厅，可使用战斗号召",
            @"Buildings\Human\TownHall\TownHall.mdx", Btn("Keep"),
            Constructible: false),
        new(Castle, "hcas", "城堡", "三级城镇大厅，可使用战斗号召",
            @"Buildings\Human\TownHall\TownHall.mdx", Btn("Castle"),
            Constructible: false)
    ];

    private static War3UnitDefinition[] CreateFallbackUnitDefinitions() =>
    [
        Unit(Footman, "hfoo", "步兵", "重甲近战",
            "Footman", "Footman", "Footman"),
        Unit(Rifleman, "hrif", "矮人火枪手", "远程火力",
            "Rifleman", "Rifleman", "Rifleman",
            impact: @"Abilities\Weapons\Rifle\RifleImpact.mdx"),
        Unit(Peasant, "hpea", "农民", "采集与建造",
            "Peasant", "Peasant", "Peasant"),
        Unit(Knight, "hkni", "骑士", "高速重甲近战",
            "Knight", "Knight", "Knight"),
        Unit(Priest, "hmpr", "牧师", "治疗型施法者",
            "Priest", "Priest", "Priest",
            projectile: @"Abilities\Weapons\PriestMissile\PriestMissile.mdx"),
        Unit(Sorceress, "hsor", "女巫", "控制型施法者",
            "Sorceress", "Sorceress", "Sorceress",
            projectile: @"Abilities\Weapons\SorceressMissile\SorceressMissile.mdx"),
        Unit(SpellBreaker, "hspt", "魔法破坏者", "反魔法战士",
            "BloodElfSpellThief", "BloodElfSpellThief", "SpellBreaker"),
        Unit(MortarTeam, "hmtm", "迫击炮小队", "攻城远程",
            "MortarTeam", "MortarTeam", "MortarTeam",
            projectile: @"Abilities\Weapons\Mortar\MortarMissile.mdx",
            impact: @"Abilities\Weapons\Mortar\ScatterShotTarget.mdx"),
        Unit(FlyingMachine, "hgyr", "飞行机器", "轻型空中侦察",
            "GyroCopter", "GyroCopter", "FlyingMachine", 1.8f,
            @"Abilities\Weapons\SteamMissile\SteamMissile.mdx"),
        Unit(SiegeEngine, "hmtt", "蒸汽机车", "重型攻城机械",
            "WarWagon", "", "SeigeEngine",
            projectile: @"Abilities\Weapons\SteamMissile\SteamMissile.mdx",
            impact: @"Abilities\Weapons\SteamTank\SteamTankImpact.mdx"),
        Unit(GryphonRider, "hgry", "狮鹫骑士", "重型空中单位",
            "GryphonRider", "GryphonRider", "GryphonRider", 2.15f,
            @"Abilities\Weapons\GryphonRiderMissile\GryphonRiderMissile.mdx",
            @"Abilities\Weapons\GryphonRiderMissile\GryphonRiderMissileTarget.mdx"),
        Unit(DragonhawkRider, "hdhw", "龙鹰骑士", "空中控制单位",
            "BloodElfDragonHawk", "BloodElfDragonHawk", "DragonHawk", 2.05f,
            @"Abilities\Weapons\DragonHawkMissile\DragonHawkMissile.mdx"),
        Unit(Archmage, "Hamg", "大魔法师", "远程智力英雄",
            "HeroArchMage", "HeroArchMage", "HeroArchMage",
            projectile: @"Abilities\Weapons\FireBallMissile\FireBallMissile.mdx"),
        Unit(MountainKing, "Hmkg", "山丘之王", "近战力量英雄",
            "HeroMountainKing", "HeroMountainKing", "HeroMountainKing"),
        Unit(Paladin, "Hpal", "圣骑士", "近战支援英雄",
            "HeroPaladin", "HeroPaladin", "HeroPaladin"),
        Unit(BloodMage, "Hblm", "血魔法师", "远程智力英雄",
            "HeroBloodElf", "HeroBloodElf", "HeroBloodElfPrince",
            projectile: @"Abilities\Weapons\BloodElfMissile\BloodElfMissile.mdx"),
        Unit(Militia, "hmil", "民兵", "战斗号召临时形态",
            "Militia", "Militia", "Militia")
    ];

    private static BuildingTypeProfile[] CreateFallbackBuildingProfiles() =>
    [
        Building(Farm, "农场", BuildingFunctionKind.Supply, 64, 56,
            80, 20, 5.5f, 500, 6),
        Building(Barracks, "兵营", BuildingFunctionKind.Production, 112, 92,
            160, 60, 8f, 1500),
        Building(TownHall, "城镇大厅", BuildingFunctionKind.TownHall, 150, 132,
            385, 205, 12f, 2500, 12),
        Building(Blacksmith, "铁匠铺", BuildingFunctionKind.Research, 92, 82,
            140, 60, 7f, 1200),
        Building(AltarOfKings, "国王祭坛", BuildingFunctionKind.Production, 104, 96,
            180, 50, 8f, 1250),
        Building(LumberMill, "伐木场", BuildingFunctionKind.Research, 94, 82,
            120, 70, 7f, 900),
        Building(ArcaneSanctum, "神秘圣地", BuildingFunctionKind.Production, 104, 92,
            150, 140, 8f, 1050),
        Building(Workshop, "车间", BuildingFunctionKind.Production, 112, 94,
            140, 140, 8f, 1200),
        Building(GryphonAviary, "狮鹫笼", BuildingFunctionKind.Production, 112, 100,
            140, 150, 9f, 1200),
        Building(ArcaneVault, "神秘藏宝室", BuildingFunctionKind.Research, 80, 72,
            130, 30, 6f, 900),
        Building(GuardTower, "防御塔", BuildingFunctionKind.Research, 58, 58,
            125, 80, 6f, 950),
        Building(Keep, "主城", BuildingFunctionKind.TownHall, 150, 132,
            0, 0, 12f, 3000, 12),
        Building(Castle, "城堡", BuildingFunctionKind.TownHall, 150, 132,
            0, 0, 12f, 3500, 12)
    ];

    private static UnitTypeProfile[] CreateFallbackUnitProfiles() =>
    [
        Profile(Footman, "步兵", 8.5f, 120, 420, 70, 12, 24, 1.15f, false, false, 2),
        Profile(Rifleman, "矮人火枪手", 8.2f, 112, 535, 55, 18, 150, 1.35f, true),
        Profile(Peasant, "农民", 7.7f, 116, 300, 45, 7, 28, 1.2f, false, false, 0, true),
        Profile(Knight, "骑士", 12.2f, 145, 835, 85, 28, 30, 1.25f, false, true, 5),
        Profile(Priest, "牧师", 7.8f, 108, 465, 38, 10, 145, 1.5f, true),
        Profile(Sorceress, "女巫", 7.9f, 108, 480, 38, 11, 150, 1.55f, true),
        Profile(SpellBreaker, "魔法破坏者", 9.2f, 120, 650, 55, 16, 34, 1.25f, false, true, 3),
        Profile(MortarTeam, "迫击炮小队", 10.2f, 92, 535, 42, 34, 230, 2.2f, true, true, 1),
        Profile(FlyingMachine, "飞行机器", 9.4f, 155, 260, 65, 12, 135, 0.85f, true, true),
        Profile(SiegeEngine, "蒸汽机车", 15.5f, 82, 980, 80, 40, 205, 2.1f, true, true, 6),
        Profile(GryphonRider, "狮鹫骑士", 13.2f, 125, 975, 80, 42, 170, 1.65f, true, true, 4),
        Profile(DragonhawkRider, "龙鹰骑士", 11.8f, 135, 775, 75, 25, 155, 1.45f, true, true, 3),
        Profile(Archmage, "大魔法师", 10.5f, 125, 900, 75, 38, 165, 1.55f, true, false, 3),
        Profile(MountainKing, "山丘之王", 11.5f, 118, 1125, 80, 44, 34, 1.35f, false, true, 5),
        Profile(Paladin, "圣骑士", 11f, 122, 1025, 80, 39, 34, 1.4f, false, true, 5),
        Profile(BloodMage, "血魔法师", 10.2f, 128, 875, 72, 37, 165, 1.5f, true, false, 2),
        Profile(Militia, "民兵", 7.7f, 120, 300, 45, 12, 28, 1.2f, false, false, 4)
    ];

    private static ProductionRecipeProfile[] CreateFallbackRecipes(
        UnitTypeProfile[] units) =>
    [
        Recipe(0, Footman, Barracks, 135, 0, 2, 3.2f, units),
        Recipe(1, Rifleman, Barracks, 205, 30, 3, 4.2f, units),
        Recipe(2, Peasant, TownHall, 75, 0, 1, 3f, units),
        Recipe(3, Knight, Barracks, 245, 60, 4, 5.6f, units, Blacksmith),
        Recipe(4, Priest, ArcaneSanctum, 135, 20, 2, 4.2f, units),
        Recipe(5, Sorceress, ArcaneSanctum, 155, 20, 2, 4.5f, units),
        Recipe(6, SpellBreaker, ArcaneSanctum, 215, 30, 3, 5f, units),
        Recipe(7, MortarTeam, Workshop, 180, 70, 3, 5.2f, units),
        Recipe(8, FlyingMachine, Workshop, 100, 30, 1, 3.6f, units),
        Recipe(9, SiegeEngine, Workshop, 195, 60, 3, 5.8f, units),
        Recipe(10, GryphonRider, GryphonAviary, 280, 70, 4, 6.2f, units),
        Recipe(11, DragonhawkRider, GryphonAviary, 200, 30, 3, 5.4f, units),
        Recipe(12, Archmage, AltarOfKings, 425, 100, 5, 7f, units),
        Recipe(13, MountainKing, AltarOfKings, 425, 100, 5, 7f, units),
        Recipe(14, Paladin, AltarOfKings, 425, 100, 5, 7f, units),
        Recipe(15, BloodMage, AltarOfKings, 425, 100, 5, 7f, units)
    ];

    private static TechnologyProfile[] CreateFallbackTechnologies() =>
    [
        Technology(0, "钢铁武器", 100, 75, 3),
        Technology(1, "铁甲升级", 125, 75, 3),
        Technology(2, "加强型石工技术", 150, 100, 3)
    ];

    private static UnitTypeProfile Profile(
        int id,
        string name,
        float radius,
        float speed,
        float health,
        float acquisition,
        float damage,
        float range,
        float cooldown,
        bool ranged,
        bool armored = false,
        float armor = 0f,
        bool worker = false)
    {
        var attributes = CombatAttribute.Biological |
                         (armored ? CombatAttribute.Armored : CombatAttribute.Light);
        if (id is FlyingMachine or SiegeEngine)
            attributes = CombatAttribute.Mechanical | CombatAttribute.Armored;
        var clearance = MovementClearance.FromPhysicalRadius(radius);
        return new UnitTypeProfile(
            id, name,
            new UnitMovementProfileSnapshot(
                id, name, radius, speed, speed * 5.5f,
                clearance.Class,
                clearance.NavigationRadius),
            new CombatProfileSnapshot(
                health, damage, range, MathF.Max(acquisition, range + 20f),
                cooldown, MathF.Min(0.35f, cooldown * 0.3f),
                MathF.Max(300f, acquisition * 3.5f),
                ranged ? CombatPositioningKind.Ranged : CombatPositioningKind.Melee,
                Armor: armor,
                Attributes: attributes,
                BaseUpgradeDamage: 1f,
                ProjectileSpeed: ranged ? 430f : 0f),
            worker);
    }

    private static BuildingTypeProfile Building(
        int id,
        string name,
        BuildingFunctionKind function,
        float width,
        float depth,
        int gold,
        int lumber,
        float seconds,
        float health,
        int supply = 0) => new(
            id, name, function, new System.Numerics.Vector2(width, depth),
            width >= 90 ? MovementClass.Large : MovementClass.Medium,
            new EconomyCost(gold, lumber), seconds, health, supply, 0.75f,
            ConstructionMethodKind.ContinuousWorker,
            Armor: function == BuildingFunctionKind.TownHall ? 5f : 2f,
            ArmorUpgradePerLevel: 1f);

    private static ProductionRecipeProfile Recipe(
        int id,
        int unitId,
        int producer,
        int gold,
        int lumber,
        int supply,
        float seconds,
        UnitTypeProfile[] units,
        int? requirement = null)
    {
        var profile = units[unitId];
        return new ProductionRecipeProfile(
            id, $"训练{profile.Name}", producer, profile,
            new EconomyCost(gold, lumber, supply), seconds, 0.75f)
        {
            Requirements = requirement.HasValue
                ? [new ProductionRequirementProfile(
                    ProductionRequirementKind.CompletedBuilding,
                    requirement.Value, 1)]
                : []
        };
    }

    private static TechnologyProfile Technology(
        int id,
        string name,
        int gold,
        int lumber,
        int maximumLevel) => new(
            id, name, Blacksmith, new EconomyCost(gold, lumber),
            5f, maximumLevel, 0.75f, -1)
        {
            Requirements = [new TechnologyRequirementProfile(
                TechnologyRequirementKind.CompletedBuilding, Blacksmith, 1)]
        };

    private static War3UnitDefinition Unit(
        int id,
        string objectId,
        string name,
        string role,
        string folder,
        string portraitStem,
        string icon,
        float flyingHeight = 0f,
        string projectile = "",
        string impact = "") => new(
            id, objectId, name, role,
            $@"Units\Human\{folder}\{folder}.mdx",
            portraitStem.Length == 0
                ? $@"Units\Human\{folder}\{folder}.mdx"
                : $@"Units\Human\{folder}\{portraitStem}_Portrait.mdx",
            Btn(icon), flyingHeight, projectile, impact);

    private static string Btn(string name) =>
        $@"ReplaceableTextures\CommandButtons\BTN{name}.blp";

    private sealed record War3HumanContentBundle(
        IReadOnlyList<War3UnitDefinition> Units,
        IReadOnlyList<War3BuildingDefinition> Buildings,
        IWar3UnitDataCatalog DataCatalog,
        War3GameplayImportReport DataStatus,
        War3ObjectDataCatalog AbilityDataCatalog,
        War3ObjectDataCatalog UpgradeDataCatalog,
        War3ObjectDataCatalog BuffEffectDataCatalog,
        War3AbilityMetadataCatalog AbilityMetadataCatalog,
        War3ObjectDataImportReport ObjectDataStatus,
        IReadOnlyList<War3TechnologyDefinition> Technologies,
        IReadOnlyList<War3AbilityDefinition> Abilities,
        War3AbilityImportResult AbilityImportStatus,
        BuildingTypeCatalogSnapshot BuildingCatalog,
        BuildingUpgradeCatalogSnapshot BuildingUpgradeCatalog,
        ProductionCatalogSnapshot ProductionCatalog,
        TechnologyCatalogSnapshot TechnologyCatalog,
        AbilityCatalogSnapshot AbilityCatalog,
        IReadOnlyDictionary<string, War3UnitDefinition> SummonedUnits);
}
