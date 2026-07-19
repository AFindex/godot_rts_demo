using System.Collections.Immutable;
using System.Numerics;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;
using War3Rts;

namespace RtsDemo.Tests;

public static class War3CombatRulesSelfTest
{
    private const float Delta = 1f / 30f;

    public static SelfTestResult Run()
    {
        try
        {
            var normalMedium = TypedDamage(
                10f, 0f, CombatAttackType.Normal, CombatArmorType.Medium);
            var pierceSmall = TypedDamage(
                10f, 0f, CombatAttackType.Pierce, CombatArmorType.Small);
            var siegeFort = TypedDamage(
                10f, 5f, CombatAttackType.Siege,
                CombatArmorType.Fortified);
            var spellsHero = TypedDamage(
                10f, 99f, CombatAttackType.Spells, CombatArmorType.Hero);
            var negativeArmor = TypedDamage(
                10f, -5f, CombatAttackType.Normal,
                CombatArmorType.Normal);

            var production = War3HumanContent.CreateProductionCatalog();
            var buildings = War3HumanContent.CreateBuildingCatalog();
            var technologies = War3HumanContent.CreateTechnologyCatalog();
            var footman = production.UnitType(War3HumanContent.Footman);
            var peasant = production.UnitType(War3HumanContent.Peasant);
            var rifleman = production.UnitType(War3HumanContent.Rifleman);
            var mortar = production.UnitType(War3HumanContent.MortarTeam);
            var gryphon = production.UnitType(War3HumanContent.GryphonRider);
            var mortarGround = mortar.Combat.Weapons.Single(value =>
                (value.TargetLayers & CombatTargetLayer.GroundUnit) != 0);
            var gryphonGround = gryphon.Combat.Weapons.Single(value =>
                (value.TargetLayers & CombatTargetLayer.GroundUnit) != 0);
            var serialization = VerifyProductionSerialization(
                production.Recipes.ToArray().First(value =>
                    value.UnitType.Id == War3HumanContent.GryphonRider));
            var resourceRoundTrip = VerifyResourceRoundTrip(production);
            var guardTower = buildings.Type(War3HumanContent.GuardTower);
            var cannonTower = buildings.Type(War3HumanContent.CannonTower);
            var arcaneTower = buildings.Type(War3HumanContent.ArcaneTower);
            var barracks = buildings.Type(War3HumanContent.Barracks);

            var dataMapped =
                Nearly(footman.Movement.TurnRateRadiansPerSecond, 0.6f / 0.03f) &&
                Nearly(footman.Combat.AttackHalfAngleRadians, 0.5f) &&
                footman.Combat.ArmorType == CombatArmorType.Large &&
                footman.Combat.ArmorUpgradeTechnologyId == 1 &&
                footman.Combat.ArmorUpgradePerLevel == 2f &&
                footman.Combat.Weapons[0].AttackType ==
                    CombatAttackType.Normal &&
                footman.Combat.Weapons[0].DamageUpgradeTechnologyId == 0 &&
                footman.Combat.Weapons[0].TargetLayers ==
                    (CombatTargetLayer.GroundUnit |
                     CombatTargetLayer.Building |
                     CombatTargetLayer.Debris |
                     CombatTargetLayer.Item |
                     CombatTargetLayer.Ward) &&
                peasant.Combat.Weapons.Any(value =>
                    value.TargetLayers == CombatTargetLayer.Tree) &&
                rifleman.Combat.ArmorType == CombatArmorType.Medium &&
                rifleman.Combat.ArmorUpgradeTechnologyId == 15 &&
                rifleman.Combat.Weapons[0].AttackType ==
                    CombatAttackType.Pierce &&
                rifleman.Combat.Weapons[0].DamageUpgradeTechnologyId == 16 &&
                mortarGround.AttackType == CombatAttackType.Siege &&
                mortarGround.DamageUpgradeTechnologyId == 16 &&
                Nearly(mortarGround.MinimumRange, 250f * 4f / 15f) &&
                Nearly(mortarGround.Area.FullDamageRadius, 25f * 4f / 15f) &&
                Nearly(mortarGround.Area.HalfDamageRadius, 150f * 4f / 15f) &&
                Nearly(mortarGround.Area.QuarterDamageRadius, 250f * 4f / 15f) &&
                mortarGround.TargetLayers ==
                    (CombatTargetLayer.GroundUnit |
                     CombatTargetLayer.Debris |
                     CombatTargetLayer.Tree |
                     CombatTargetLayer.Wall |
                     CombatTargetLayer.Item |
                     CombatTargetLayer.Ward) &&
                mortarGround.Area.TargetLayers ==
                    (CombatTargetLayer.GroundUnit |
                     CombatTargetLayer.Building |
                     CombatTargetLayer.Debris |
                     CombatTargetLayer.Tree |
                     CombatTargetLayer.Wall) &&
                gryphonGround.Propagation.Kind ==
                    CombatWeaponPropagationKind.Line &&
                Nearly(gryphonGround.Propagation.LineDistance, 0f) &&
                Nearly(gryphonGround.Propagation.Radius, 50f * 4f / 15f) &&
                Nearly(gryphonGround.Propagation.DamageLossFactor, 0.2f) &&
                gryphonGround.Propagation.MaximumTargets == 32 &&
                gryphonGround.Propagation.DistanceUpgradeTechnologyId == 13 &&
                Nearly(gryphonGround.Propagation.DistanceUpgradePerLevel,
                    200f * 4f / 15f) &&
                guardTower.ArmorType == CombatArmorType.Large &&
                barracks.ArmorType == CombatArmorType.Fortified &&
                technologies.Technologies.Length >= 17 &&
                technologies.Technology(15).Name.Length > 0 &&
                technologies.Technology(16).Name.Length > 0 &&
                serialization && resourceRoundTrip;
            var buildingDataMapped =
                guardTower.Combat.Enabled &&
                guardTower.Combat.Weapons.Length == 1 &&
                Nearly(guardTower.Perception.DetectionRange,
                    900f * 4f / 15f) &&
                guardTower.Perception.DetectionTechnologyId >= 0 &&
                War3HumanContent.Technologies[
                        guardTower.Perception.DetectionTechnologyId]
                    .ObjectId.Equals("Rhse", StringComparison.Ordinal) &&
                Nearly(guardTower.Combat.Weapons[0].AttackDamage, 25f) &&
                Nearly(guardTower.Combat.Weapons[0].AttackRange,
                    700f * 4f / 15f) &&
                cannonTower.Combat.Weapons.Length == 2 &&
                cannonTower.Combat.Weapons[0].Area.Enabled &&
                cannonTower.Combat.Weapons[0].AttackType ==
                    CombatAttackType.Siege &&
                arcaneTower.Combat.Weapons.Length == 1 &&
                Nearly(arcaneTower.Combat.Weapons[0].AttackDamage, 9f) &&
                arcaneTower.Combat.OnHitEffects.Length == 1 &&
                arcaneTower.Combat.OnHitEffects[0].Kind ==
                    BuildingWeaponOnHitEffectKind.ManaFeedback &&
                Nearly(
                    arcaneTower.Combat.OnHitEffects[0].UnitMaximumValue,
                    24f) &&
                Nearly(
                    arcaneTower.Combat.OnHitEffects[0].HeroMaximumValue,
                    12f) &&
                War3HumanContent.Buildings[War3HumanContent.GuardTower]
                    .ProjectileSource.Contains(
                        "GuardTowerMissile", StringComparison.OrdinalIgnoreCase);

            var effectiveArmor = VerifyArmorTechnology(
                footman, technologies.Technology(1));
            var area = VerifyAreaDamage();
            var minimumRange = VerifyMinimumRange();
            var propagation = VerifyPropagation();
            var stormHammers = VerifyStormHammers(
                gryphon, technologies.Technology(13));
            var hotRoundTrip = VerifyHotRoundTrip(
                gryphon, technologies.Technology(13));
            var facing = VerifyFacingRules();
            var combatObjects = VerifyCombatObjects();
            var combatObjectReplay = VerifyCombatObjectReplay();
            var buildingCombat = VerifyBuildingCombat(guardTower);
            var buildingFeedback = VerifyBuildingFeedback(
                arcaneTower, production);
            var buildingReveal = VerifyBuildingReveal(arcaneTower);
            var buildingCloud = VerifyBuildingCloud(guardTower);
            var repair = VerifyRepair(guardTower, production);
            var matrix = Nearly(normalMedium, 15f) &&
                         Nearly(pierceSmall, 20f) &&
                         Nearly(siegeFort, 15f / 1.3f) &&
                         Nearly(spellsHero, 7f) &&
                         Nearly(negativeArmor,
                             10f * (2f - MathF.Pow(0.94f, 5f)));
            var passed = matrix && dataMapped && buildingDataMapped &&
                         Nearly(effectiveArmor, 6f) &&
                         area.Passed && minimumRange.Passed &&
                         propagation.Passed && stormHammers.Passed &&
                         facing.Passed && combatObjects.Passed &&
                         combatObjectReplay.Passed && buildingCombat.Passed &&
                         buildingFeedback.Passed && buildingReveal.Passed;
            passed &= buildingCloud.Passed;
            passed &= repair.Passed;
            passed &= hotRoundTrip;
            return new SelfTestResult(
                passed,
                $"matrix={normalMedium:0.###}/{pierceSmall:0.###}/" +
                $"{siegeFort:0.###}/{spellsHero:0.###}/" +
                $"neg={negativeArmor:0.###}, " +
                $"data={dataMapped}/{buildingDataMapped}, " +
                $"armor={effectiveArmor:0.###}, " +
                $"baseFlags=ser:{serialization}/res:{resourceRoundTrip}/" +
                $"gArmor:{guardTower.ArmorType}/bArmor:{barracks.ArmorType}/" +
                $"tech:{technologies.Technologies.Length}, " +
                $"tower={guardTower.Combat.Weapons.Length}/" +
                $"{guardTower.Combat.AcquisitionRange:0.###}/" +
                $"{guardTower.Combat.Weapons.FirstOrDefault().AttackDamage:0.###}/" +
                $"{guardTower.Combat.Weapons.FirstOrDefault().AttackRange:0.###}, " +
                $"cannon={cannonTower.Combat.Weapons.Length}/" +
                $"{cannonTower.Combat.Weapons.FirstOrDefault().Area.Enabled}/" +
                $"{cannonTower.Combat.Weapons.FirstOrDefault().AttackType}, " +
                $"arcane={arcaneTower.Combat.Weapons.Length}/" +
                $"{arcaneTower.Combat.Weapons.FirstOrDefault().AttackDamage:0.###}, " +
                $"missile={War3HumanContent.Buildings[War3HumanContent.GuardTower].ProjectileSource}, " +
                $"area={area.Summary}, minimum={minimumRange.Summary}, " +
                $"propagation={propagation.Summary}, " +
                $"storm={stormHammers.Summary}, facing={facing.Summary}, " +
                $"objects={combatObjects.Summary}, " +
                $"objectReplay={combatObjectReplay.Summary}, " +
                $"building={buildingCombat.Summary}, " +
                $"feedback={buildingFeedback.Summary}, " +
                $"reveal={buildingReveal.Summary}, " +
                $"cloud={buildingCloud.Summary}, " +
                $"repair={repair.Summary}, " +
                $"hot={hotRoundTrip}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }

    private static float TypedDamage(
        float raw,
        float armor,
        CombatAttackType attackType,
        CombatArmorType armorType) => CombatDamageResolver.Resolve(
        new CombatWeaponDamageSnapshot(
            raw, 1, CombatAttribute.None, 0f, 0, 0f, 0f, attackType),
        new CombatDefenseSnapshot(armor, CombatAttribute.None, armorType),
        10_000f).TotalDamage;

    private static bool VerifyProductionSerialization(
        ProductionRecipeProfile recipe)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(
                   stream, System.Text.Encoding.UTF8, leaveOpen: true))
            ProductionSerialization.WriteRecipe(writer, recipe);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var restored = ProductionSerialization.ReadRecipe(reader);
        var source = recipe.UnitType.Combat.Weapons[0];
        var weapon = restored.UnitType.Combat.Weapons[0];
        return restored.UnitType.Combat.ArmorType ==
                   recipe.UnitType.Combat.ArmorType &&
               restored.UnitType.Movement.TurnRateRadiansPerSecond ==
                   recipe.UnitType.Movement.TurnRateRadiansPerSecond &&
               restored.UnitType.Combat.AttackHalfAngleRadians ==
                   recipe.UnitType.Combat.AttackHalfAngleRadians &&
               restored.UnitType.Combat.ArmorUpgradeTechnologyId ==
                   recipe.UnitType.Combat.ArmorUpgradeTechnologyId &&
               restored.UnitType.Combat.ArmorUpgradePerLevel ==
                   recipe.UnitType.Combat.ArmorUpgradePerLevel &&
               weapon.AttackType == source.AttackType &&
               weapon.DamageUpgradeTechnologyId ==
                   source.DamageUpgradeTechnologyId &&
               weapon.MinimumRange == source.MinimumRange &&
               weapon.Area == source.Area &&
               weapon.Propagation == source.Propagation;
    }

    private static bool VerifyResourceRoundTrip(
        ProductionCatalogSnapshot production)
    {
        var resource = ProductionCatalogResourceConverter.FromSnapshot(
            production);
        if (!ProductionCatalogResourceConverter.TryConvert(
                resource, out var restored, out _) || restored is null)
            return false;
        var source = production.UnitType(War3HumanContent.GryphonRider);
        var copy = restored.UnitType(War3HumanContent.GryphonRider);
        return copy.Combat.ArmorType == source.Combat.ArmorType &&
               copy.Movement.TurnRateRadiansPerSecond ==
                   source.Movement.TurnRateRadiansPerSecond &&
               copy.Combat.AttackHalfAngleRadians ==
                   source.Combat.AttackHalfAngleRadians &&
               copy.Combat.ArmorUpgradeTechnologyId ==
                   source.Combat.ArmorUpgradeTechnologyId &&
               copy.Combat.ArmorUpgradePerLevel ==
                   source.Combat.ArmorUpgradePerLevel &&
               copy.Combat.Weapons.SequenceEqual(source.Combat.Weapons);
    }

    private static float VerifyArmorTechnology(
        UnitTypeProfile footman,
        TechnologyProfile armorTechnology)
    {
        var simulation = CreateSimulation();
        simulation.Technology.RestoreRuntimeState(
            new TechnologyRuntimeSnapshot(
                1,
                [new TechnologyLevelRuntimeEntry(1, armorTechnology, 2)],
                []),
            simulation.Construction,
            simulation.Economy.Players);
        var unit = Add(simulation, new Vector2(120f, 120f), 1,
            footman.Combat);
        return simulation.EffectiveUnitArmor(unit);
    }

    private static (bool Passed, string Summary) VerifyAreaDamage()
    {
        var simulation = CreateSimulation();
        var area = new CombatWeaponAreaSnapshot(
            10f, 30f, 60f, CombatTargetLayer.GroundUnit);
        var attackerProfile = ActiveProfile(
            minimumRange: 0f, area: area, speed: 0f);
        var targetProfile = PassiveProfile();
        var attacker = Add(
            simulation, new Vector2(100f, 300f), 1, attackerProfile);
        var primary = Add(
            simulation, new Vector2(250f, 300f), 2, targetProfile);
        var full = Add(
            simulation, new Vector2(257f, 300f), 2, targetProfile);
        var half = Add(
            simulation, new Vector2(270f, 300f), 2, targetProfile);
        var quarter = Add(
            simulation, new Vector2(295f, 300f), 2, targetProfile);
        simulation.IssueAttackTarget([attacker], primary);
        TickUntil(simulation, 30,
            () => simulation.Combat.Health[primary] < 100f);
        var health = new[]
        {
            simulation.Combat.Health[primary],
            simulation.Combat.Health[full],
            simulation.Combat.Health[half],
            simulation.Combat.Health[quarter]
        };
        var passed = Nearly(health[0], 70f) && Nearly(health[1], 70f) &&
                     Nearly(health[2], 85f) && Nearly(health[3], 92.5f);
        return (passed, string.Join("/", health.Select(value =>
            value.ToString("0.###"))));
    }

    private static (bool Passed, string Summary) VerifyMinimumRange()
    {
        var simulation = CreateSimulation();
        var attacker = Add(
            simulation, new Vector2(200f, 140f), 1,
            ActiveProfile(60f, default, 128f));
        var target = Add(
            simulation, new Vector2(215f, 140f), 2, PassiveProfile());
        var initial = Vector2.Distance(
            simulation.Units.Positions[attacker],
            simulation.Units.Positions[target]);
        simulation.IssueAttackTarget([attacker], target);
        for (var tick = 0; tick < 5; tick++) simulation.Tick(Delta);
        var heldFire = Nearly(simulation.Combat.Health[target], 100f);
        var retreated = Vector2.Distance(
            simulation.Units.Positions[attacker],
            simulation.Units.Positions[target]) > initial;
        var eventuallyFired = TickUntil(simulation, 120,
            () => simulation.Combat.Health[target] < 100f);
        return (heldFire && retreated && eventuallyFired,
            $"hold={heldFire}, retreat={retreated}, hit={eventuallyFired}");
    }

    private static (bool Passed, string Summary) VerifyPropagation()
    {
        var lineSimulation = CreateSimulation();
        var line = new CombatWeaponPropagationSnapshot(
            CombatWeaponPropagationKind.Line,
            70f, 8f, 0.2f, 3, CombatTargetLayer.GroundUnit);
        var lineAttacker = Add(lineSimulation, new Vector2(100f, 180f), 1,
            ActiveProfile(0f, default, 0f, line));
        var linePrimary = Add(lineSimulation, new Vector2(220f, 180f), 2,
            PassiveProfile());
        var lineSecond = Add(lineSimulation, new Vector2(250f, 180f), 2,
            PassiveProfile());
        var lineThird = Add(lineSimulation, new Vector2(280f, 180f), 2,
            PassiveProfile());
        var lineOutside = Add(lineSimulation, new Vector2(250f, 195f), 2,
            PassiveProfile());
        lineSimulation.IssueAttackTarget([lineAttacker], linePrimary);
        TickUntil(lineSimulation, 30,
            () => lineSimulation.Combat.Health[linePrimary] < 100f);

        var bounceSimulation = CreateSimulation();
        var bounce = new CombatWeaponPropagationSnapshot(
            CombatWeaponPropagationKind.Bounce,
            0f, 35f, 0.5f, 3, CombatTargetLayer.GroundUnit);
        var bounceAttacker = Add(
            bounceSimulation, new Vector2(100f, 360f), 1,
            ActiveProfile(0f, default, 0f, bounce));
        var bouncePrimary = Add(
            bounceSimulation, new Vector2(220f, 360f), 2,
            PassiveProfile());
        var bounceSecond = Add(
            bounceSimulation, new Vector2(245f, 360f), 2,
            PassiveProfile());
        var bounceThird = Add(
            bounceSimulation, new Vector2(270f, 360f), 2,
            PassiveProfile());
        bounceSimulation.IssueAttackTarget([bounceAttacker], bouncePrimary);
        TickUntil(bounceSimulation, 30,
            () => bounceSimulation.Combat.Health[bouncePrimary] < 100f);

        var lineHealth = new[]
        {
            lineSimulation.Combat.Health[linePrimary],
            lineSimulation.Combat.Health[lineSecond],
            lineSimulation.Combat.Health[lineThird],
            lineSimulation.Combat.Health[lineOutside]
        };
        var bounceHealth = new[]
        {
            bounceSimulation.Combat.Health[bouncePrimary],
            bounceSimulation.Combat.Health[bounceSecond],
            bounceSimulation.Combat.Health[bounceThird]
        };
        var passed = Nearly(lineHealth[0], 70f) &&
                     Nearly(lineHealth[1], 76f) &&
                     Nearly(lineHealth[2], 80.8f) &&
                     Nearly(lineHealth[3], 100f) &&
                     Nearly(bounceHealth[0], 70f) &&
                     Nearly(bounceHealth[1], 85f) &&
                     Nearly(bounceHealth[2], 92.5f);
        return (passed,
            $"line={string.Join('/', lineHealth.Select(value => value.ToString("0.###")))} " +
            $"bounce={string.Join('/', bounceHealth.Select(value => value.ToString("0.###")))}");
    }

    private static (bool Passed, string Summary) VerifyStormHammers(
        UnitTypeProfile gryphon,
        TechnologyProfile stormHammers)
    {
        var without = StormHammerHealth(gryphon, stormHammers, false);
        var with = StormHammerHealth(gryphon, stormHammers, true);
        return (Nearly(without, 100f) && with < 100f,
            $"secondary={without:0.###}/{with:0.###}");
    }

    private static float StormHammerHealth(
        UnitTypeProfile gryphon,
        TechnologyProfile stormHammers,
        bool researched)
    {
        var simulation = CreateSimulation();
        if (researched)
            simulation.Technology.RestoreRuntimeState(
                new TechnologyRuntimeSnapshot(
                    1,
                    [new TechnologyLevelRuntimeEntry(
                        1, stormHammers, 1)],
                    []),
                simulation.Construction,
                simulation.Economy.Players);
        var attacker = Add(simulation, new Vector2(100f, 500f), 1,
            gryphon.Combat);
        var primary = Add(simulation, new Vector2(220f, 500f), 2,
            PassiveProfile());
        var secondary = Add(simulation, new Vector2(250f, 500f), 2,
            PassiveProfile());
        simulation.IssueAttackTarget([attacker], primary);
        TickUntil(simulation, 180,
            () => simulation.Combat.Health[primary] < 100f);
        return simulation.Combat.Health[secondary];
    }

    private static bool VerifyHotRoundTrip(
        UnitTypeProfile gryphon,
        TechnologyProfile stormHammers)
    {
        var simulation = CreateSimulation();
        simulation.Technology.RestoreRuntimeState(
            new TechnologyRuntimeSnapshot(
                1,
                [new TechnologyLevelRuntimeEntry(1, stormHammers, 1)],
                []),
            simulation.Construction,
            simulation.Economy.Players);
        var attacker = Add(simulation, new Vector2(100f, 540f), 1,
            gryphon.Combat);
        var target = Add(simulation, new Vector2(260f, 540f), 2,
            PassiveProfile());
        simulation.IssueAttackTarget([attacker], target);
        if (!TickUntil(simulation, 90,
                () => simulation.CombatProjectiles.ActiveCount > 0))
            return false;
        simulation.Units.Facings[attacker] = 1.25f;
        simulation.Units.PreviousFacings[attacker] = 1f;
        simulation.Units.TurnRatesRadiansPerSecond[attacker] = 12.5f;
        var state = simulation.CaptureRuntimeState();
        var payload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, 0UL, state);
        if (!RuntimeHotSnapshotCodec.TryDeserialize(
                payload, SimulationHotSnapshot.CurrentFormatVersion,
                out var packageHash, out var restored, out var validation) ||
            packageHash != 0UL || restored is null ||
            validation != HotSnapshotValidationCode.Success ||
            restored.CombatProjectiles.Active.Length != 1)
            return false;
        var profile = gryphon.Combat.Weapons.Single(value =>
            (value.TargetLayers & CombatTargetLayer.GroundUnit) != 0);
        var projectile = restored.CombatProjectiles.Active[0];
        return restored.Combat.WeaponPropagations[attacker] ==
                   profile.Propagation &&
               Nearly(restored.Units.Facings[attacker], 1.25f) &&
               Nearly(restored.Units.PreviousFacings[attacker], 1f) &&
               Nearly(restored.Units.TurnRatesRadiansPerSecond[attacker], 12.5f) &&
               Nearly(restored.Combat.AttackHalfAngles[attacker],
                   gryphon.Combat.AttackHalfAngleRadians) &&
               projectile.Weapon.Propagation.Kind ==
                   CombatWeaponPropagationKind.Line &&
               Nearly(projectile.Weapon.Propagation.LineDistance,
                   200f * 4f / 15f) &&
               projectile.Weapon.Propagation.DistanceUpgradeTechnologyId ==
                   -1 &&
               projectile.Weapon.Propagation.DistanceUpgradePerLevel == 0f;
    }

    private static (bool Passed, string Summary) VerifyFacingRules()
    {
        var simulation = CreateSimulation();
        var attackerProfile = ActiveProfile(
                0f, default, speed: 0f)
            with { AttackHalfAngleRadians = 0.05f };
        var attacker = simulation.AddUnit(
            new Vector2(400f, 300f), 1, attackerProfile,
            radius: 2f, maxSpeed: 128f, acceleration: 720f,
            perception: UnitPerceptionProfileSnapshot.Standard,
            turnRateRadiansPerSecond: MathF.PI,
            facingRadians: 0f);
        var target = Add(simulation, new Vector2(300f, 300f), 2,
            PassiveProfile());
        simulation.IssueAttackTarget([attacker], target);

        for (var tick = 0; tick < 20; tick++) simulation.Tick(Delta);
        var blockedWhileTurning = Nearly(
            simulation.Combat.Health[target], 100f);
        var attackedAfterFacing = TickUntil(
            simulation, 30,
            () => simulation.Combat.Health[target] < 100f);
        var error = MathF.Abs(UnitFacing.Difference(
            simulation.Units.Facings[attacker], MathF.PI));
        return (
            blockedWhileTurning && attackedAfterFacing && error <= 0.051f,
            $"blocked={blockedWhileTurning} fired={attackedAfterFacing} " +
            $"error={error:0.###}");
    }

    private static (bool Passed, string Summary) VerifyCombatObjects()
    {
        var direct = CreateSimulation();
        var treeWeapon = ActiveProfile(
            0f, default, 0f,
            targetLayers: CombatTargetLayer.Tree);
        var attacker = Add(
            direct, new Vector2(100f, 100f), 1, treeWeapon);
        var resource = direct.Economy.AddResourceNode(
            EconomyResourceKind.VespeneGas,
            new Vector2(220f, 100f),
            20, 10, 4f, 1,
            activeHarvesterSlots: 1,
            harvestMode: EconomyHarvestMode.Progressive);
        var treeBounds = new SimRect(
            new Vector2(210f, 90f), new Vector2(230f, 110f));
        direct.Economy.SetResourceInteractionBounds(resource, treeBounds);
        var treeFootprint = direct.PlaceBuilding(treeBounds);
        var tree = direct.AddCombatObject(new CombatObjectProfile(
            CombatObjectKind.Tree,
            treeBounds,
            20f,
            LinkedResourceNodeId: resource.Value,
            LinkedDynamicFootprintId: treeFootprint.Value));
        direct.IssueAttackObject([attacker], tree);
        var hit = TickUntil(direct, 30,
            () => !direct.ObserveCombatObject(tree).Alive);
        var linked = direct.Economy.ResourceNodeRemaining(resource) ==
                     direct.ObserveCombatObject(tree).Health;
        var pathingOpened = direct.World.DynamicOccupancy.Snapshot().All(
            value => value.Id != treeFootprint);
        var objectImpact = direct.CombatEvents.ReadAfter(0).Events.Any(value =>
            value.Kind == CombatEventKind.Impact &&
            value.TargetKind == CombatTargetKind.Object &&
            value.TargetId == tree.Value);

        var state = direct.CaptureRuntimeState();
        var payload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, 0UL, state);
        var hot = RuntimeHotSnapshotCodec.TryDeserialize(
                      payload,
                      SimulationHotSnapshot.CurrentFormatVersion,
                      out _, out var restored, out var validation) &&
                  validation == HotSnapshotValidationCode.Success &&
                  restored is not null &&
                  restored.CombatObjects.Objects.Length == 1 &&
                  Nearly(
                      restored.CombatObjects.Objects[0].Health,
                      direct.ObserveCombatObject(tree).Health);

        var wrongLayer = CreateSimulation();
        var wrongAttacker = Add(
            wrongLayer, new Vector2(100f, 180f), 1, treeWeapon);
        var wall = wrongLayer.AddCombatObject(new CombatObjectProfile(
            CombatObjectKind.Wall,
            new SimRect(
                new Vector2(210f, 170f), new Vector2(230f, 190f)),
            80f));
        wrongLayer.IssueAttackObject([wrongAttacker], wall);
        for (var tick = 0; tick < 10; tick++) wrongLayer.Tick(Delta);
        var filtered = Nearly(
            wrongLayer.ObserveCombatObject(wall).Health, 80f);

        var splash = CreateSimulation();
        var area = new CombatWeaponAreaSnapshot(
            35f, 35f, 35f, CombatTargetLayer.Tree);
        var splashAttacker = Add(
            splash, new Vector2(100f, 280f), 1,
            ActiveProfile(0f, area, 0f));
        var primary = Add(
            splash, new Vector2(220f, 280f), 2, PassiveProfile());
        var splashTree = splash.AddCombatObject(new CombatObjectProfile(
            CombatObjectKind.Tree,
            new SimRect(
                new Vector2(235f, 270f), new Vector2(255f, 290f)),
            80f));
        var debris = splash.AddCombatObject(new CombatObjectProfile(
            CombatObjectKind.Debris,
            new SimRect(
                new Vector2(250f, 270f), new Vector2(270f, 290f)),
            80f));
        splash.IssueAttackTarget([splashAttacker], primary);
        TickUntil(splash, 30,
            () => splash.Combat.Health[primary] < 100f);
        var layerSplash = splash.ObserveCombatObject(splashTree).Health < 80f &&
                          Nearly(splash.ObserveCombatObject(debris).Health, 80f);

        var wards = CreateSimulation();
        wards.Abilities.ConfigureCatalog(
            new AbilityCatalogSnapshot(
                [],
                [new UnitAbilityBindingProfile(
                    0,
                    false,
                    UnitManaProfile.None,
                    [],
                    AbilityUnitTraits.Ward)]));
        var wardAttacker = Add(
            wards, new Vector2(100f, 380f), 1,
            ActiveProfile(
                0f, default, 0f,
                targetLayers: CombatTargetLayer.Ward));
        var wardTarget = Add(
            wards, new Vector2(220f, 380f), 2, PassiveProfile());
        var wardBound = wards.Abilities.BindUnitType(wardTarget, 0);
        wards.IssueAttackTarget([wardAttacker], wardTarget);
        var wardHit = TickUntil(wards, 30,
            () => wards.Combat.Health[wardTarget] < 100f);

        var projectileSimulation = CreateSimulation();
        var projectileAttacker = Add(
            projectileSimulation, new Vector2(100f, 460f), 1,
            ActiveProfile(
                0f, default, 90f,
                targetLayers: CombatTargetLayer.Tree));
        var projectileTree = projectileSimulation.AddCombatObject(
            new CombatObjectProfile(
                CombatObjectKind.Tree,
                new SimRect(
                    new Vector2(250f, 450f),
                    new Vector2(270f, 470f)),
                80f));
        projectileSimulation.IssueAttackObject(
            [projectileAttacker], projectileTree);
        var projectileLaunched = TickUntil(
            projectileSimulation, 30,
            () => projectileSimulation.CombatProjectiles.ActiveCount > 0);
        var projectileState = projectileSimulation.CaptureRuntimeState();
        var projectilePayload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion,
            0UL,
            projectileState);
        var projectileHot = RuntimeHotSnapshotCodec.TryDeserialize(
                                projectilePayload,
                                SimulationHotSnapshot.CurrentFormatVersion,
                                out _, out var projectileRestored,
                                out var projectileValidation) &&
                            projectileValidation ==
                            HotSnapshotValidationCode.Success &&
                            projectileRestored is not null &&
                            projectileRestored.CombatProjectiles.Active.Length ==
                            1 &&
                            projectileRestored.CombatProjectiles.Active[0]
                                .TargetKind == CombatTargetKind.Object;
        var projectileHit = TickUntil(
            projectileSimulation, 90,
            () => projectileSimulation.ObserveCombatObject(projectileTree)
                .Health < 80f);
        return (
            hit && linked && pathingOpened && objectImpact && hot && filtered &&
            layerSplash && wardBound && wardHit && projectileLaunched &&
            projectileHot && projectileHit,
            $"hit={hit} linked={linked} path={pathingOpened} " +
            $"event={objectImpact} hot={hot} " +
            $"filter={filtered} splash={layerSplash} ward={wardBound}/{wardHit} " +
            $"projectile={projectileLaunched}/{projectileHot}/{projectileHit}");
    }

    private static (bool Passed, string Summary) VerifyCombatObjectReplay()
    {
        var bounds = new SimRect(
            Vector2.Zero, new Vector2(800f, 600f));
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                bounds,
                [], [], [], [],
                out var navigation,
                out _) || navigation is null)
            return (false, "navigation");
        var world = navigation.CreateWorld();
        var simulation = new RtsSimulation(
            world,
            new GridPathProvider(world),
            capacity: 32,
            navigation.CreateRoutePlanner(world),
            navigation.CreateChokeController());
        simulation.Economy.Players.RegisterPlayer(1, 0, 0, 200);
        simulation.Economy.Players.RegisterPlayer(2, 0, 0, 200);
        var attacker = Add(
            simulation, new Vector2(100f, 100f), 1,
            ActiveProfile(
                0f, default, 0f,
                targetLayers: CombatTargetLayer.Tree));
        var treeBounds = new SimRect(
            new Vector2(210f, 90f), new Vector2(230f, 110f));
        var resource = simulation.Economy.AddResourceNode(
            EconomyResourceKind.VespeneGas,
            new Vector2(220f, 100f),
            20, 10, 4f, 1,
            activeHarvesterSlots: 1,
            harvestMode: EconomyHarvestMode.Progressive);
        simulation.Economy.SetResourceInteractionBounds(resource, treeBounds);
        var footprint = simulation.PlaceBuilding(treeBounds);
        var tree = simulation.AddCombatObject(new CombatObjectProfile(
            CombatObjectKind.Tree,
            treeBounds,
            20f,
            LinkedResourceNodeId: resource.Value,
            LinkedDynamicFootprintId: footprint.Value));
        var gameplay = DemoGameplayProfiles.CreateSnapshot();
        simulation.StartReplayPackageRecording(
            ReplayResourceManifest.Create(navigation, gameplay, null));
        simulation.IssueAttackObject([attacker], tree);
        if (!TickUntil(simulation, 30,
                () => !simulation.ObserveCombatObject(tree).Alive))
            return (false, "no-destruction");
        var targetTick = simulation.Metrics.Tick;
        var expectedHash = simulation.ComputeStateHash();
        var source = simulation.CaptureReplayPackage();
        if (source.WorldCommands.Length != 0 ||
            source.CommandLog.Entries.Length != 1 ||
            source.CommandLog.Entries[0].Kind != UnitOrderKind.AttackObject)
            return (false,
                $"manifest={source.WorldCommands.Length}/" +
                $"{source.CommandLog.Entries.Length}");
        if (!SimulationReplayPackageSnapshot.TryDeserialize(
                source.CanonicalBytes,
                out var package,
                out var validation) || package is null || !validation.Succeeded)
            return (false, $"deserialize={validation.Code}");
        if (!SimulationReplayPackageFactory.TryCreateSimulation(
                package,
                navigation,
                gameplay,
                null,
                out var replay,
                out validation) || replay is null || !validation.Succeeded)
            return (false, $"factory={validation.Code}");
        var runner = new SimulationReplayPackageRunner(package);
        while (replay.Metrics.Tick < targetTick)
        {
            runner.ApplyForCurrentTick(replay);
            replay.Tick(Delta);
        }
        var exact = replay.ComputeStateHash() == expectedHash;
        var dead = !replay.ObserveCombatObject(tree).Alive;
        var opened = replay.World.DynamicOccupancy.Snapshot().All(
            value => value.Id != footprint);
        return (
            runner.Completed && exact && dead && opened,
            $"run={runner.Completed}/{exact}/{dead}/{opened}");
    }

    private static (bool Passed, string Summary) VerifyBuildingCombat(
        BuildingTypeProfile tower)
    {
        var simulation = CreateSimulation();
        var bounds = new SimRect(
            new Vector2(240f, 240f), new Vector2(288f, 288f));
        var footprint = simulation.PlaceBuilding(bounds);
        simulation.Construction.RestoreRuntimeState(
            new ConstructionRuntimeSnapshot(
            [
                new ConstructionRuntimeEntry(
                    new GameplayBuildingId(0), 1, tower, bounds,
                    default, footprint, BuildingLifecycleState.Completed,
                    -1, bounds.Min, 1f, tower.MaximumHealth,
                    new EconomyResourceNodeId(-1))
            ],
            new ConstructionReservationRuntimeSnapshot(1, [])));
        var target = Add(
            simulation, new Vector2(380f, 264f), 2, PassiveProfile());
        var launched = TickUntil(
            simulation, 90,
            () => simulation.BuildingCombat.ActiveProjectileCount > 0);
        if (!launched)
            return (false, "no-projectile");

        var phase = simulation.BuildingCombat.Observe(
            new GameplayBuildingId(0));
        var launchEvents = simulation.BuildingCombatEvents.ReadAfter(0).Events;
        var startedEvent = launchEvents.Any(value =>
            value.Kind == BuildingCombatEventKind.AttackStarted);
        var launchedEvent = launchEvents.Any(value =>
            value.Kind == BuildingCombatEventKind.ProjectileLaunched);
        var state = simulation.CaptureRuntimeState();
        var payload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, 0UL, state);
        var hot = RuntimeHotSnapshotCodec.TryDeserialize(
                      payload,
                      SimulationHotSnapshot.CurrentFormatVersion,
                      out _, out var restored, out var validation) &&
                  restored is not null &&
                  validation == HotSnapshotValidationCode.Success &&
                  restored.BuildingCombat.Projectiles.Length == 1;
        if (restored is not null)
            simulation.RestoreRuntimeState(restored);

        var hit = TickUntil(
            simulation, 90,
            () => simulation.Combat.Health[target] < 100f);
        var events = simulation.BuildingCombatEvents.ReadAfter(0).Events;
        var impactCount = events.Count(value =>
            value.Kind == BuildingCombatEventKind.Impact &&
            value.TargetUnit == target);
        var staged = startedEvent && launchedEvent && impactCount == 1;
        return (
            phase.Phase == BuildingCombatPhase.Cooldown && hot && hit && staged,
            $"phase={phase.Phase} hot={hot} hit={hit} stages={staged}/" +
            $"impact={impactCount} hp={simulation.Combat.Health[target]:0.###}");
    }

    private static (bool Passed, string Summary) VerifyBuildingCloud(
        BuildingTypeProfile tower)
    {
        var simulation = CreateSimulation();
        var cloud = new AbilityProfile(
            0, "TCLF", "Test Building Cloud", string.Empty,
            string.Empty, string.Empty, AbilityActivationKind.TargetPoint,
            AbilityTargetFlags.Point, false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 500f, 80f, 0.5f, 0.5f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.ApplyStatus,
                    AbilityEffectTiming.Impact,
                    AbilityEffectSelector.AreaAtTarget,
                    AbilityRelationFilter.Enemy,
                    Radius: 80f,
                    Duration: 0.5f,
                    Status: AbilityStatusFlags.AttackDisabled,
                    AffectsBuildings: true)])]);
        simulation.Abilities.ConfigureCatalog(new AbilityCatalogSnapshot(
            [cloud],
            [new UnitAbilityBindingProfile(
                0, false, UnitManaProfile.None,
                [new UnitAbilityEntryProfile(0, 1)])]));
        var bounds = new SimRect(
            new Vector2(240f, 240f), new Vector2(288f, 288f));
        var footprint = simulation.PlaceBuilding(bounds);
        var building = new GameplayBuildingId(0);
        simulation.Construction.RestoreRuntimeState(
            new ConstructionRuntimeSnapshot(
            [
                new ConstructionRuntimeEntry(
                    building, 1, tower, bounds, default, footprint,
                    BuildingLifecycleState.Completed, -1, bounds.Min, 1f,
                    tower.MaximumHealth, new EconomyResourceNodeId(-1))
            ],
            new ConstructionReservationRuntimeSnapshot(1, [])));
        var movement = new UnitMovementProfileSnapshot(
            0, "cloud-caster", 2f, 128f, 720f,
            MovementClass.Small, 2f);
        var caster = simulation.AddUnit(
            new Vector2(330f, 264f),
            new UnitTypeProfile(
                0, "cloud-caster", movement, PassiveProfile(), false),
            2);
        _ = Add(simulation, new Vector2(380f, 264f), 2, PassiveProfile());
        simulation.Visibility.Update(
            simulation.Units, simulation.Combat, simulation.Construction);
        var cast = simulation.IssueAbility(
            2, caster, 0,
            AbilityCastTarget.Point((bounds.Min + bounds.Max) * 0.5f));
        var disabled = cast.Succeeded &&
            simulation.Abilities.IsBuildingAttackDisabled(building) &&
            simulation.Abilities.ActiveBuildingStatusCount == 1;
        var payload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, 0UL,
            simulation.CaptureRuntimeState());
        var hot = RuntimeHotSnapshotCodec.TryDeserialize(
                      payload, SimulationHotSnapshot.CurrentFormatVersion,
                      out _, out var restored, out var validation) &&
                  restored is not null &&
                  validation == HotSnapshotValidationCode.Success &&
                  restored.Abilities.BuildingStatuses.Length == 1;
        if (restored is not null)
            simulation.RestoreRuntimeState(restored);
        for (var tick = 0; tick < 8; tick++) simulation.Tick(Delta);
        var suppressed =
            simulation.BuildingCombat.ActiveProjectileCount == 0 &&
            simulation.BuildingCombat.Observe(building).Phase ==
                BuildingCombatPhase.Idle;
        var resumed = TickUntil(
            simulation, 90,
            () => simulation.BuildingCombat.ActiveProjectileCount > 0);
        return (
            disabled && hot && suppressed && resumed,
            $"cast={cast.Code} disabled={disabled} hot={hot} " +
            $"suppressed={suppressed} resumed={resumed}");
    }

    private static (bool Passed, string Summary) VerifyRepair(
        BuildingTypeProfile tower,
        ProductionCatalogSnapshot production)
    {
        var simulation = CreateSimulation();
        simulation.Economy.Players.RegisterPlayer(1, 1_000, 1_000, 200);
        simulation.Abilities.ConfigureCatalog(
            War3HumanContent.CreateAbilityCatalog());
        if (!simulation.Abilities.Catalog.TryFind("Ahrp", out var repair))
            return (false, "missing-Ahrp");
        var effect = repair.Levels.Single().Effects.Single();
        var bounds = new SimRect(
            new Vector2(240f, 240f), new Vector2(288f, 288f));
        var footprint = simulation.PlaceBuilding(bounds);
        var building = new GameplayBuildingId(0);
        var initialHealth = tower.MaximumHealth * 0.5f;
        simulation.Construction.RestoreRuntimeState(
            new ConstructionRuntimeSnapshot(
            [
                new ConstructionRuntimeEntry(
                    building, 1, tower, bounds, default, footprint,
                    BuildingLifecycleState.Completed, -1, bounds.Min, 1f,
                    initialHealth, new EconomyResourceNodeId(-1))
            ],
            new ConstructionReservationRuntimeSnapshot(1, [])));
        var caster = simulation.AddUnit(
            new Vector2(bounds.Max.X + 1f, 264f),
            production.UnitType(War3HumanContent.Peasant), 1);
        simulation.Visibility.Update(
            simulation.Units, simulation.Combat, simulation.Construction);
        var beforeWallet = simulation.Economy.Players.Snapshot(1);
        var cast = simulation.IssueAbility(
            1, caster, repair.Id,
            AbilityCastTarget.Building(
                building, (bounds.Min + bounds.Max) * 0.5f));
        simulation.Tick(Delta);
        var repairing = cast.Succeeded &&
                        simulation.Abilities.ActivePersistentEffectCount == 1 &&
                        simulation.Construction.Observe(building).Health >
                        initialHealth;

        var sourceHash = simulation.ComputeStateHash();
        var payload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, 0UL,
            simulation.CaptureRuntimeState());
        var hot = RuntimeHotSnapshotCodec.TryDeserialize(
                      payload, SimulationHotSnapshot.CurrentFormatVersion,
                      out _, out var restored, out var validation) &&
                  restored is not null &&
                  validation == HotSnapshotValidationCode.Success &&
                  restored.Abilities.PersistentEffects.Length == 1;
        if (restored is not null) simulation.RestoreRuntimeState(restored);
        var exact = sourceHash == simulation.ComputeStateHash();
        for (var tick = 0; tick < 90; tick++) simulation.Tick(Delta);
        var repairedBuilding = simulation.Construction.Observe(building);
        var afterWallet = simulation.Economy.Players.Snapshot(1);
        var expectedMinerals = (int)MathF.Floor(
            tower.Cost.Minerals * effect.Value *
            repairedBuilding.Health / tower.MaximumHealth + 0.0001f) -
            (int)MathF.Floor(
                tower.Cost.Minerals * effect.Value *
                initialHealth / tower.MaximumHealth + 0.0001f);
        var expectedVespene = (int)MathF.Floor(
            tower.Cost.VespeneGas * effect.Value *
            repairedBuilding.Health / tower.MaximumHealth + 0.0001f) -
            (int)MathF.Floor(
                tower.Cost.VespeneGas * effect.Value *
                initialHealth / tower.MaximumHealth + 0.0001f);
        var charged = beforeWallet.Minerals - afterWallet.Minerals ==
                          expectedMinerals &&
                      beforeWallet.VespeneGas - afterWallet.VespeneGas ==
                          expectedVespene;

        simulation.IssueMove([caster], new Vector2(500f, 264f));
        var canceled = simulation.Abilities.ActivePersistentEffectCount == 0;

        const float initialProgress = 0.4f;
        simulation.Construction.RestoreRuntimeState(
            new ConstructionRuntimeSnapshot(
            [
                new ConstructionRuntimeEntry(
                    building, 1, tower, bounds, default, footprint,
                    BuildingLifecycleState.Constructing, caster, bounds.Min,
                    initialProgress,
                    tower.MaximumHealth *
                    (0.1f + 0.9f * initialProgress),
                    new EconomyResourceNodeId(-1))
            ],
            new ConstructionReservationRuntimeSnapshot(1, [])));
        var powerApplied = ((IAbilityRuntimeWorld)simulation)
            .AbilityRepairBuilding(
                caster, building, effect.Value, effect.SecondaryValue,
                effect.HeroValue, effect.HeroSecondaryValue,
                effect.Interval, repair.Levels.Single().Range);
        var expectedProgress = initialProgress +
            effect.Interval / tower.BuildSeconds * effect.HeroSecondaryValue;
        var powerBuild = powerApplied && Nearly(
            simulation.Construction.Observe(building).Progress,
            expectedProgress);

        var mechanicalType = production.UnitType(
            War3HumanContent.FlyingMachine);
        var mechanical = simulation.AddUnit(
            new Vector2(bounds.Max.X + 4f, 264f), mechanicalType, 1);
        simulation.Combat.Health[mechanical] =
            simulation.Combat.MaximumHealth[mechanical] * 0.5f;
        var mechanicalBefore = simulation.Combat.Health[mechanical];
        var mechanicalCast = simulation.IssueAbility(
            1, caster, repair.Id,
            AbilityCastTarget.Unit(
                mechanical, simulation.Units.Positions[mechanical]));
        simulation.Tick(Delta);
        var mechanicalRepair = mechanicalCast.Succeeded &&
            simulation.Combat.Health[mechanical] > mechanicalBefore &&
            simulation.Abilities.TryRepairTargetProfile(
                mechanical, out var mechanicalRepairProfile) &&
            mechanicalRepairProfile.Enabled;
        simulation.IssueMove([caster], simulation.Units.Positions[caster]);

        simulation.Combat.Health[mechanical] =
            simulation.Combat.MaximumHealth[mechanical];
        const float autoRepairHealthFraction = 0.65f;
        simulation.Construction.RestoreRuntimeState(
            new ConstructionRuntimeSnapshot(
            [
                new ConstructionRuntimeEntry(
                    building, 1, tower, bounds, default, footprint,
                    BuildingLifecycleState.Completed, -1, bounds.Min, 1f,
                    tower.MaximumHealth * autoRepairHealthFraction,
                    new EconomyResourceNodeId(-1))
            ],
            new ConstructionReservationRuntimeSnapshot(1, [])));
        simulation.Units.Positions[caster] =
            new Vector2(bounds.Max.X + 1f, 264f);
        simulation.Units.PreviousPositions[caster] =
            simulation.Units.Positions[caster];
        var autoToggle = simulation.IssueSetAbilityAutoCast(
            1, caster, repair.Id, true);
        simulation.Tick(Delta);
        var autoState = simulation.Abilities.Observe(caster);
        var autoRepair = autoToggle.Succeeded &&
            autoState.Abilities.Single(value =>
                value.AbilityId == repair.Id).AutoCastEnabled &&
            simulation.Construction.Observe(building).Health >
            tower.MaximumHealth * autoRepairHealthFraction &&
            simulation.Abilities.ActivePersistentEffectCount == 1;
        return (
            repairing && hot && exact && charged && canceled && powerBuild &&
            mechanicalRepair && autoRepair,
            $"cast={cast.Code} active={repairing} hot={hot}/{exact} " +
            $"hp={repairedBuilding.Health:0.###} cost=" +
            $"{beforeWallet.Minerals - afterWallet.Minerals}/" +
            $"{beforeWallet.VespeneGas - afterWallet.VespeneGas} " +
            $"cancel={canceled} power={powerBuild}/" +
            $"{expectedProgress:0.###} mechanical=" +
            $"{mechanicalCast.Code}/{mechanicalRepair} auto=" +
            $"{autoToggle.Code}/{autoRepair}");
    }

    private static (bool Passed, string Summary) VerifyBuildingFeedback(
        BuildingTypeProfile tower,
        ProductionCatalogSnapshot production)
    {
        var simulation = CreateSimulation();
        simulation.Abilities.ConfigureCatalog(
            War3HumanContent.CreateAbilityCatalog());
        var bounds = new SimRect(
            new Vector2(240f, 240f), new Vector2(288f, 288f));
        var footprint = simulation.PlaceBuilding(bounds);
        simulation.Construction.RestoreRuntimeState(
            new ConstructionRuntimeSnapshot(
            [
                new ConstructionRuntimeEntry(
                    new GameplayBuildingId(0), 1, tower, bounds,
                    default, footprint, BuildingLifecycleState.Completed,
                    -1, bounds.Min, 1f, tower.MaximumHealth,
                    new EconomyResourceNodeId(-1))
            ],
            new ConstructionReservationRuntimeSnapshot(1, [])));
        var targetType = production.UnitType(War3HumanContent.Sorceress);
        var target = simulation.AddUnit(
            new Vector2(390f, 264f), targetType, 2);
        var initialMana = simulation.Abilities.Observe(target).Mana;
        var initialHealth = simulation.Combat.Health[target];
        if (!TickUntil(simulation, 90,
                () => simulation.BuildingCombat.ActiveProjectileCount > 0))
            return (false, "no-projectile");
        var payload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion,
            0UL,
            simulation.CaptureRuntimeState());
        var hot = RuntimeHotSnapshotCodec.TryDeserialize(
                      payload,
                      SimulationHotSnapshot.CurrentFormatVersion,
                      out _, out var restored, out var validation) &&
                  restored is not null &&
                  validation == HotSnapshotValidationCode.Success &&
                  restored.BuildingCombat.Projectiles.Length == 1 &&
                  restored.BuildingCombat.Projectiles[0]
                      .OnHitEffects.Length == 1;
        if (restored is not null)
            simulation.RestoreRuntimeState(restored);
        var hit = TickUntil(
            simulation, 90,
            () => simulation.Combat.Health[target] < initialHealth);
        var remainingMana = simulation.Abilities.Observe(target).Mana;
        var burned = initialMana - remainingMana;
        var damage = initialHealth - simulation.Combat.Health[target];
        return (
            hot && hit && burned is > 23f and <= 24f && damage > 9f,
            $"hot={hot} hit={hit} mana={initialMana:0.###}->" +
            $"{remainingMana:0.###} damage={damage:0.###}");
    }

    private static (bool Passed, string Summary) VerifyBuildingReveal(
        BuildingTypeProfile tower)
    {
        var abilityCatalog = War3HumanContent.CreateAbilityCatalog();
        if (!abilityCatalog.TryFind("AHta", out var reveal) ||
            reveal.Activation != AbilityActivationKind.TargetPoint)
            return (false, "missing-AHta");
        var level = reveal.Levels[0];
        if (!abilityCatalog.TryBuildingBinding(
                War3HumanContent.ArcaneTower, out var binding) ||
            !binding.Abilities.Contains(reveal.Id))
            return (false, "missing-binding");

        var simulation = CreateSimulation();
        simulation.Abilities.ConfigureCatalog(abilityCatalog);
        var bounds = new SimRect(
            new Vector2(240f, 240f), new Vector2(288f, 288f));
        var footprint = simulation.PlaceBuilding(bounds);
        var buildingId = new GameplayBuildingId(0);
        simulation.Construction.RestoreRuntimeState(
            new ConstructionRuntimeSnapshot(
            [
                new ConstructionRuntimeEntry(
                    buildingId, 1, tower, bounds, default, footprint,
                    BuildingLifecycleState.Completed, -1, bounds.Min, 1f,
                    tower.MaximumHealth, new EconomyResourceNodeId(-1))
            ],
            new ConstructionReservationRuntimeSnapshot(1, [])));
        var target = new Vector2(700f, 500f);
        var castTarget = AbilityCastTarget.Point(target);
        var blocked = simulation.IssueBuildingAbility(
            1, buildingId, reveal.Id, castTarget);

        var rhseDefinition = War3HumanContent.Technologies.Single(value =>
            value.ObjectId.Equals("Rhse", StringComparison.Ordinal));
        var rhse = War3HumanContent.CreateTechnologyCatalog()
            .Technology(rhseDefinition.TechnologyId);
        simulation.Technology.RestoreRuntimeState(
            new TechnologyRuntimeSnapshot(
                1,
                [new TechnologyLevelRuntimeEntry(1, rhse, 1)],
                []),
            simulation.Construction,
            simulation.Economy.Players);

        // Normalize derived visibility before taking a replay checkpoint;
        // RestoreRuntimeState intentionally rebuilds the same derived sources.
        simulation.Tick(Delta);
        var beforeCommand = simulation.CaptureRuntimeState();
        simulation.StartCommandRecording();
        var issued = simulation.IssueBuildingAbility(
            1, buildingId, reveal.Id, castTarget);
        var commandLog = simulation.CaptureCommandLog();
        var runtime = simulation.Abilities.ObserveBuildingState(
            buildingId, tower.Id);
        var slot = runtime.Abilities.Single(value =>
            value.AbilityId == reveal.Id);
        var abilityState = simulation.Abilities.CaptureRuntimeState(
            simulation.Units.Count);
        var configured =
            Nearly(level.Area, 900f * 4f / 15f) &&
            Nearly(level.Duration, 15f) &&
            Nearly(level.CooldownSeconds, 180f) &&
            level.Requirements.Any(value =>
                value.Kind == AbilityRequirementKind.TechnologyLevel &&
                value.TargetId == rhse.Id && value.Value == 1);
        var active = issued.Succeeded &&
                     Nearly(slot.CooldownRemaining, 180f) &&
                     Nearly(runtime.Mana, binding.Mana.Initial) &&
                     abilityState.Reveals.Length == 1 &&
                     Nearly(abilityState.Reveals[0].Radius, level.Area) &&
                     Nearly(abilityState.Reveals[0].RemainingSeconds, 15f) &&
                     abilityState.Reveals[0].Position == target;

        var replay = CreateSimulation();
        replay.RestoreRuntimeState(beforeCommand);
        if (commandLog.Entries.Length == 1)
            replay.ApplyRecordedCommand(commandLog.Entries[0]);
        var sourceState = simulation.CaptureRuntimeState();
        var replayState = replay.CaptureRuntimeState();
        var sourceHash = sourceState.StateHash;
        var replayHash = replayState.StateHash;
        var sourceStateBytes = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, 0UL,
            sourceState);
        var replayStateBytes = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, 0UL,
            replayState);
        var sourceAbilityBytes = SerializeAbilityRuntime(sourceState.Abilities);
        var replayAbilityBytes = SerializeAbilityRuntime(replayState.Abilities);
        var replayAbilityExact = sourceAbilityBytes.SequenceEqual(
            replayAbilityBytes);
        var replayStateExact =
            sourceStateBytes.AsSpan(0, 28).SequenceEqual(
                replayStateBytes.AsSpan(0, 28)) &&
            sourceStateBytes.AsSpan(36).SequenceEqual(
                replayStateBytes.AsSpan(36));
        var replayDifference = FirstDifference(
            sourceStateBytes, replayStateBytes, 36);
        var replayExact = commandLog.Entries.Length == 1 &&
                          commandLog.Entries[0].TargetPosition == target &&
                          replayHash == sourceHash && replayStateExact;

        var payload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, 0UL,
            simulation.CaptureRuntimeState());
        var hot = RuntimeHotSnapshotCodec.TryDeserialize(
                      payload, SimulationHotSnapshot.CurrentFormatVersion,
                      out _, out var restored, out var validation) &&
                  validation == HotSnapshotValidationCode.Success &&
                  restored is not null &&
                  restored.Abilities.Reveals.Length == 1 &&
                  restored.Abilities.Buildings.Length == 1 &&
                  Array.IndexOf(
                      restored.Abilities.Buildings[0].AbilityIds,
                      reveal.Id) is var hotSlot && hotSlot >= 0 &&
                  Nearly(restored.Abilities.Buildings[0].Cooldowns[hotSlot],
                      180f);
        if (restored is not null) simulation.RestoreRuntimeState(restored);
        var cooldown = simulation.IssueBuildingAbility(
            1, buildingId, reveal.Id, castTarget);
        simulation.Tick(Delta);
        var visible = simulation.Visibility.At(1, target) ==
                          MapVisibility.Visible &&
                      simulation.Visibility.IsDetected(1, target);
        for (var tick = 0; tick < 451; tick++) simulation.Tick(Delta);
        var expired = simulation.Visibility.At(1, target) !=
                          MapVisibility.Visible &&
                      !simulation.Visibility.IsDetected(1, target) &&
                      simulation.Abilities.CaptureRuntimeState(
                          simulation.Units.Count).Reveals.Length == 0;
        return (
            blocked.Code == AbilityCommandCode.RequirementsNotMet &&
            configured && active && replayExact && hot &&
            cooldown.Code == AbilityCommandCode.Cooldown && visible && expired,
            $"req={blocked.Code} config={configured} active={active} " +
            $"replay={replayExact}/{sourceHash:X16}/{replayHash:X16} " +
            $"bytes={replayStateExact}/{replayDifference}/" +
            $"{sourceStateBytes.Length}/{replayStateBytes.Length} " +
            $"ability={replayAbilityExact}/" +
            $"{sourceAbilityBytes.Length}/{replayAbilityBytes.Length} " +
            $"hot={hot} cooldown={cooldown.Code} " +
            $"visible={visible} expired={expired}");
    }

    private static RtsSimulation CreateSimulation()
    {
        var world = new StaticWorld(new SimRect(
            Vector2.Zero, new Vector2(800f, 600f)));
        var simulation = new RtsSimulation(
            world, new StraightLinePathProvider(), capacity: 32);
        simulation.Economy.Players.RegisterPlayer(1, 0, 0, 200);
        simulation.Economy.Players.RegisterPlayer(2, 0, 0, 200);
        return simulation;
    }

    private static int Add(
        RtsSimulation simulation,
        Vector2 position,
        int team,
        CombatProfileSnapshot profile) => simulation.AddUnit(
        position, team, profile, radius: 2f, maxSpeed: 128f,
        acceleration: 720f,
        perception: UnitPerceptionProfileSnapshot.Standard);

    private static CombatProfileSnapshot ActiveProfile(
        float minimumRange,
        CombatWeaponAreaSnapshot area,
        float speed,
        CombatWeaponPropagationSnapshot propagation = default,
        CombatTargetLayer targetLayers = CombatTargetLayer.GroundUnit)
    {
        var weapon = new CombatWeaponProfileSnapshot(
            0, targetLayers, true, -1,
            20f, 250f, 10f, 0f, CombatPositioningKind.Ranged,
            1, CombatAttribute.None, 0f, 0f, 0f, speed, false, false,
            CombatAttackType.Normal, -1, minimumRange, area, propagation);
        return new CombatProfileSnapshot(
            100f, 20f, 250f, 280f, 10f, 0f, 500f,
            CombatPositioningKind.Ranged,
            ArmorType: CombatArmorType.Normal)
        {
            Weapons = ImmutableArray.Create(weapon)
        };
    }

    private static CombatProfileSnapshot PassiveProfile() => new(
        100f, 0f, 0f, 1f, 10f, 0f, 1f,
        CombatPositioningKind.Ranged,
        ArmorType: CombatArmorType.Medium);

    private static bool TickUntil(
        RtsSimulation simulation,
        int maximumTicks,
        Func<bool> condition)
    {
        for (var tick = 0; tick < maximumTicks; tick++)
        {
            simulation.Tick(Delta);
            if (condition()) return true;
        }
        return false;
    }

    private static int FirstDifference(
        byte[] left,
        byte[] right,
        int start = 0)
    {
        var count = Math.Min(left.Length, right.Length);
        for (var index = start; index < count; index++)
            if (left[index] != right[index]) return index;
        return left.Length == right.Length ? -1 : count;
    }

    private static byte[] SerializeAbilityRuntime(
        AbilityRuntimeSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        AbilitySerialization.WriteRuntime(writer, snapshot);
        writer.Flush();
        return stream.ToArray();
    }

    private static bool Nearly(float left, float right) =>
        MathF.Abs(left - right) < 0.02f;
}
