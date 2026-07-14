using System.Collections.Immutable;
using RtsDemo.Simulation;

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
    string ImpactSource = "");

public sealed record War3BuildingDefinition(
    int TypeId,
    string ObjectId,
    string Name,
    string Role,
    string ModelSource,
    string IconPath);

/// <summary>
/// The Human faction's gameplay and presentation catalog. Simulation IDs stay
/// dense and stable; Warcraft object IDs and asset paths never leak into the
/// deterministic simulation layer.
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

    public static IReadOnlyList<War3BuildingDefinition> Buildings { get; } =
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
            @"Buildings\Human\HumanTower\HumanTower.mdx", Btn("GuardTower"))
    ];

    public static IReadOnlyList<War3UnitDefinition> Units { get; } =
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
            projectile: @"Abilities\Weapons\BloodElfMissile\BloodElfMissile.mdx")
    ];

    public static BuildingTypeCatalogSnapshot CreateBuildingCatalog()
    {
        BuildingTypeProfile[] definitions =
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
                125, 80, 6f, 950)
        ];
        if (!BuildingTypeCatalogSnapshot.TryCreate(
                BuildingTypeCatalogSnapshot.CurrentFormatVersion,
                definitions, out var catalog, out var validation) || catalog is null)
            throw new InvalidOperationException(
                $"Warcraft Human building catalog is invalid: {validation.FirstError}.");
        return catalog;
    }

    public static ProductionCatalogSnapshot CreateProductionCatalog()
    {
        var units = CreateUnitProfiles();
        ProductionRecipeProfile[] recipes =
        [
            Recipe(0, Footman, Barracks, 135, 0, 2, 3.2f),
            Recipe(1, Rifleman, Barracks, 205, 30, 3, 4.2f),
            Recipe(2, Peasant, TownHall, 75, 0, 1, 3f),
            Recipe(3, Knight, Barracks, 245, 60, 4, 5.6f, Blacksmith),
            Recipe(4, Priest, ArcaneSanctum, 135, 20, 2, 4.2f),
            Recipe(5, Sorceress, ArcaneSanctum, 155, 20, 2, 4.5f),
            Recipe(6, SpellBreaker, ArcaneSanctum, 215, 30, 3, 5f),
            Recipe(7, MortarTeam, Workshop, 180, 70, 3, 5.2f),
            Recipe(8, FlyingMachine, Workshop, 100, 30, 1, 3.6f),
            Recipe(9, SiegeEngine, Workshop, 195, 60, 3, 5.8f),
            Recipe(10, GryphonRider, GryphonAviary, 280, 70, 4, 6.2f),
            Recipe(11, DragonhawkRider, GryphonAviary, 200, 30, 3, 5.4f),
            Recipe(12, Archmage, AltarOfKings, 425, 100, 5, 7f),
            Recipe(13, MountainKing, AltarOfKings, 425, 100, 5, 7f),
            Recipe(14, Paladin, AltarOfKings, 425, 100, 5, 7f),
            Recipe(15, BloodMage, AltarOfKings, 425, 100, 5, 7f)
        ];
        if (!ProductionCatalogSnapshot.TryCreate(
                ProductionCatalogSnapshot.CurrentFormatVersion,
                units, recipes, out var catalog, out var validation) || catalog is null)
            throw new InvalidOperationException(
                $"Warcraft Human production catalog is invalid: {validation.Code} {validation.Message}");
        return catalog;
    }

    public static TechnologyCatalogSnapshot CreateTechnologyCatalog()
    {
        TechnologyProfile[] values =
        [
            Technology(0, "钢铁武器", 100, 75, 3),
            Technology(1, "铁甲升级", 125, 75, 3),
            Technology(2, "火药升级", 150, 100, 3)
        ];
        if (!TechnologyCatalogSnapshot.TryCreate(
                TechnologyCatalogSnapshot.CurrentFormatVersion,
                values, out var catalog, out var error) || catalog is null)
            throw new InvalidOperationException(error);
        return catalog;
    }

    public static War3UnitDefinition ResolveUnit(
        RtsSimulation simulation,
        ProductionCatalogSnapshot catalog,
        int unit)
    {
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

    public static string GoldMineSource => @"buildings\Other\GoldMine\GoldMine.mdx";

    public static string TreeSource(int variant) =>
        $@"Doodads\Terrain\LordaeronTree\LordaeronTree{Math.Abs(variant) % 10}.mdx";

    private static UnitTypeProfile[] CreateUnitProfiles() =>
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
        Profile(BloodMage, "血魔法师", 10.2f, 128, 875, 72, 37, 165, 1.5f, true, false, 2)
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
        int? requirement = null)
    {
        var profile = CreateUnitProfiles()[unitId];
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
}
