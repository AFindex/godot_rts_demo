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
                mortarGround.Area.TargetLayers ==
                    (CombatTargetLayer.GroundUnit |
                     CombatTargetLayer.Building) &&
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
                technologies.Technologies.Length == 17 &&
                technologies.Technology(15).Name.Length > 0 &&
                technologies.Technology(16).Name.Length > 0 &&
                serialization && resourceRoundTrip;

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
            var matrix = Nearly(normalMedium, 15f) &&
                         Nearly(pierceSmall, 20f) &&
                         Nearly(siegeFort, 15f / 1.3f) &&
                         Nearly(spellsHero, 7f) &&
                         Nearly(negativeArmor,
                             10f * (2f - MathF.Pow(0.94f, 5f)));
            var passed = matrix && dataMapped && Nearly(effectiveArmor, 6f) &&
                         area.Passed && minimumRange.Passed &&
                         propagation.Passed && stormHammers.Passed &&
                         facing.Passed;
            passed &= hotRoundTrip;
            return new SelfTestResult(
                passed,
                $"matrix={normalMedium:0.###}/{pierceSmall:0.###}/" +
                $"{siegeFort:0.###}/{spellsHero:0.###}/" +
                $"neg={negativeArmor:0.###}, " +
                $"data={dataMapped}, armor={effectiveArmor:0.###}, " +
                $"area={area.Summary}, minimum={minimumRange.Summary}, " +
                $"propagation={propagation.Summary}, " +
                $"storm={stormHammers.Summary}, facing={facing.Summary}, " +
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
        CombatWeaponPropagationSnapshot propagation = default)
    {
        var weapon = new CombatWeaponProfileSnapshot(
            0, CombatTargetLayer.GroundUnit, true, -1,
            20f, 250f, 10f, 0f, CombatPositioningKind.Ranged,
            1, CombatAttribute.None, 0f, 0f, 0f, 0f, false, false,
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

    private static bool Nearly(float left, float right) =>
        MathF.Abs(left - right) < 0.02f;
}
