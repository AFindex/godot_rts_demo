using System.Collections.Immutable;
using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class AbilitySystemSelfTest
{
    public static SelfTestResult Run()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(240f), 24);
        var simulation = rig.RenderSimulation;
        var catalog = CreateCatalog();
        simulation.Abilities.ConfigureCatalog(catalog);
        var unitFormResult = TestUnitForm(catalog);
        var buildingFormResult = TestBuildingUnitForm(catalog);
        var configuredScalarResult = TestConfiguredScalarEffects();

        var movement = new UnitMovementProfileSnapshot(
            0, "ability-test", 7.5f, 90f, 400f,
            MovementClass.Small, 8f);
        var combat = CombatProfileSnapshot.Standard with
        {
            MaximumHealth = 100f,
            AttackDamage = 0f
        };
        var casterType = new UnitTypeProfile(
            0, "caster", movement, combat, false);
        var targetType = new UnitTypeProfile(
            1, "target", movement with { Id = 1 },
            combat with { Armor = 5f }, false);
        var caster = simulation.AddUnit(new Vector2(60f, 60f), casterType, 1);
        var ally = simulation.AddUnit(new Vector2(72f, 60f), targetType, 1);
        var enemy = simulation.AddUnit(new Vector2(84f, 60f), casterType, 2);
        var secondEnemy = simulation.AddUnit(
            new Vector2(90f, 66f), casterType, 2);
        var magicTarget = simulation.AddUnit(
            new Vector2(78f, 66f), targetType, 1);
        var buffTarget = simulation.AddUnit(
            new Vector2(72f, 72f), targetType, 1);
        var wardType = new UnitTypeProfile(
            2, "ward", movement with { Id = 2 }, combat, false);
        var ancientType = new UnitTypeProfile(
            3, "ancient", movement with { Id = 3 }, combat, false);
        var sapperType = new UnitTypeProfile(
            4, "sapper", movement with { Id = 4 }, combat, false);
        var wardTarget = simulation.AddUnit(
            new Vector2(96f, 60f), wardType, 2);
        var ancientTarget = simulation.AddUnit(
            new Vector2(102f, 60f), ancientType, 2);
        var sapperTarget = simulation.AddUnit(
            new Vector2(108f, 60f), sapperType, 2);
        var neutralTarget = simulation.AddUnit(
            new Vector2(114f, 60f), targetType, 0);
        var manaTargetType = new UnitTypeProfile(
            6, "mana-target", movement with { Id = 6 }, combat, false);
        var manaTarget = simulation.AddUnit(
            new Vector2(108f, 72f), manaTargetType, 2);
        var undeadType = new UnitTypeProfile(
            7, "undead-target", movement with { Id = 7 }, combat, false);
        var undeadTarget = simulation.AddUnit(
            new Vector2(120f, 72f), undeadType, 2);
        var friendlyUndead = simulation.AddUnit(
            new Vector2(60f, 84f), undeadType, 1);
        var bandCenter = new Vector2(180f, 180f);
        var fullBandTarget = simulation.AddUnit(
            bandCenter + new Vector2(5f, 0f), targetType, 2);
        var middleBandTarget = simulation.AddUnit(
            bandCenter + new Vector2(0f, 15f), targetType, 2);
        var outerBandTarget = simulation.AddUnit(
            bandCenter + new Vector2(-25f, 0f), targetType, 2);
        var persistentCenter = new Vector2(220f, 180f);
        var persistentTarget = simulation.AddUnit(
            persistentCenter, targetType, 0);
        var persistentSecondTarget = simulation.AddUnit(
            persistentCenter - new Vector2(18f, 0f), targetType, 0);
        var teleportAnchor = simulation.AddUnit(
            new Vector2(210f, 40f), targetType, 1);
        var airTarget = simulation.AddUnit(
            new Vector2(92f, 52f),
            targetType with
            {
                Perception = UnitPerceptionProfileSnapshot.ElevatedObserver()
            },
            2);

        var beforeLearn = simulation.IssueAbility(
            1, caster, 4,
            AbilityCastTarget.Self(caster, simulation.Units.Positions[caster]));
        var learn = simulation.IssueLearnAbility(1, caster, 4);
        var afterLearn = simulation.IssueAbility(
            1, caster, 4,
            AbilityCastTarget.Self(caster, simulation.Units.Positions[caster]));
        var heroState = simulation.Abilities.Observe(caster);
        var heroLearningWorked =
            beforeLearn.Code == AbilityCommandCode.AbilityNotLearned &&
            learn.Succeeded && afterLearn.Succeeded &&
            heroState.HeroLevel == 1 && heroState.UnspentSkillPoints == 0 &&
            heroState.Abilities.Single(value => value.AbilityId == 4).Level == 1;

        var gated = simulation.IssueAbility(
            1, caster, 3,
            AbilityCastTarget.Self(caster, simulation.Units.Positions[caster]));
        var requirementGateWorked =
            gated.Code == AbilityCommandCode.RequirementsNotMet;

        simulation.DamageUnit(ally, 40f);
        rig.Step();
        rig.Step();
        var autoHealWorked = simulation.Combat.Health[ally] == 85f &&
                             simulation.Abilities.Observe(caster).Mana == 90f;

        var previewMana = simulation.Abilities.Observe(caster).Mana;
        var previewHealth = simulation.Combat.Health[enemy];
        var firePreview = simulation.PreviewAbility(
            1, caster, 0,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var invalidPreview = simulation.PreviewAbility(
            1, caster, 0,
            AbilityCastTarget.Unit(ally, simulation.Units.Positions[ally]));
        var previewWorked = firePreview.Succeeded &&
            invalidPreview.Code == AbilityCommandCode.EnemyTargetRequired &&
            simulation.Abilities.Observe(caster).Mana == previewMana &&
            simulation.Combat.Health[enemy] == previewHealth;
        var fire = simulation.IssueAbility(
            1, caster, 0,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var projectileReleased = fire.Succeeded &&
            simulation.Abilities.ActiveProjectileCount == 1 &&
            simulation.Combat.Health[enemy] == previewHealth &&
            simulation.AbilityEvents.ReadAfter(0).Events.Any(value =>
                value.AbilityId == "T001" &&
                value.Kind == AbilityEventKind.Released &&
                value.InstanceId > 0);
        var projectileHot = new SimulationHotSnapshot(
            0xB017UL, simulation.CaptureRuntimeState());
        var projectileRestored = SimulationHotSnapshot.TryDeserialize(
            projectileHot.CanonicalBytes, out var parsedProjectile,
            out var projectileValidation) && parsedProjectile is not null &&
            projectileValidation == HotSnapshotValidationCode.Success &&
            parsedProjectile.State.Abilities.Projectiles.Length == 1;
        if (parsedProjectile is not null)
            simulation.RestoreRuntimeState(parsedProjectile.State);
        for (var tick = 0; tick < 60 &&
             simulation.Combat.Health[enemy] == previewHealth; tick++)
            rig.Step();
        var projectileImpacted = simulation.AbilityEvents.ReadAfter(0)
            .Events.Any(value =>
                value.AbilityId == "T001" &&
                value.Kind == AbilityEventKind.Impact &&
                value.InstanceId > 0);
        var directDamageWorked = fire.Succeeded &&
                                 projectileReleased && projectileRestored &&
                                 projectileImpacted &&
                                 simulation.Abilities.ActiveProjectileCount == 0 &&
                                 simulation.Combat.Health[enemy] == 70f;
        simulation.Abilities.RestoreMana(caster, 100f);
        var enemyManaBefore = simulation.Abilities.Observe(enemy).Mana;
        var enemyManaTransfer = simulation.IssueAbility(
            1, caster, 19,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var enemyManaAfter = simulation.Abilities.Observe(enemy).Mana;
        var casterManaAfterEnemyTransfer =
            simulation.Abilities.Observe(caster).Mana;
        var enemyManaTransferWorked = enemyManaTransfer.Succeeded &&
            enemyManaBefore == 100f && enemyManaAfter == 70f &&
            casterManaAfterEnemyTransfer == 130f &&
            simulation.Abilities.Observe(caster).MaximumMana == 130f;
        var feedbackUnit = simulation.IssueAbility(
            1, caster, 20,
            AbilityCastTarget.Unit(
                manaTarget, simulation.Units.Positions[manaTarget]));
        var feedbackHero = simulation.IssueAbility(
            1, caster, 20,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var feedbackWorked = feedbackUnit.Succeeded && feedbackHero.Succeeded &&
            simulation.Abilities.Observe(manaTarget).Mana == 80f &&
            simulation.Combat.Health[manaTarget] == 80f &&
            simulation.Abilities.Observe(enemy).Mana == 66f &&
            simulation.Combat.Health[enemy] == 66f;
        simulation.DamageUnit(ally, 20f);
        simulation.DamageUnit(friendlyUndead, 20f);
        var holyEnemyUndead = simulation.IssueAbility(
            1, caster, 21,
            AbilityCastTarget.Unit(
                undeadTarget, simulation.Units.Positions[undeadTarget]));
        var livingEnemyHealth = simulation.Combat.Health[manaTarget];
        var holyEnemyLiving = simulation.IssueAbility(
            1, caster, 21,
            AbilityCastTarget.Unit(
                manaTarget, simulation.Units.Positions[manaTarget]));
        var holyFriendlyLiving = simulation.IssueAbility(
            1, caster, 21,
            AbilityCastTarget.Unit(ally, simulation.Units.Positions[ally]));
        var holyFriendlyUndead = simulation.IssueAbility(
            1, caster, 21,
            AbilityCastTarget.Unit(
                friendlyUndead, simulation.Units.Positions[friendlyUndead]));
        var holyLightWorked = holyEnemyUndead.Succeeded &&
            holyEnemyLiving.Succeeded && holyFriendlyLiving.Succeeded &&
            holyFriendlyUndead.Succeeded &&
            simulation.Combat.Health[undeadTarget] == 70f &&
            simulation.Combat.Health[manaTarget] == livingEnemyHealth &&
            simulation.Combat.Health[ally] == 95f &&
            simulation.Combat.Health[friendlyUndead] == 80f;
        var bandCast = simulation.IssueAbility(
            1, caster, 22, AbilityCastTarget.Point(bandCenter));
        var fullBandHealth = simulation.Combat.Health[fullBandTarget];
        var middleBandHealth = simulation.Combat.Health[middleBandTarget];
        var outerBandHealth = simulation.Combat.Health[outerBandTarget];
        var damageBandsWorked = bandCast.Succeeded &&
            fullBandHealth == 70f && middleBandHealth == 80f &&
            outerBandHealth == 90f;
        var groundBeforeAirPassive = simulation.Combat.Health[manaTarget];
        simulation.CombatEvents.Publish(
            simulation.Metrics.Tick, CombatEventKind.Impact,
            caster, CombatTargetKind.Unit, manaTarget,
            worldPosition: simulation.Units.Positions[manaTarget]);
        rig.Step();
        var groundAfterAirPassive = simulation.Combat.Health[manaTarget];
        var airBeforePassive = simulation.Combat.Health[airTarget];
        simulation.CombatEvents.Publish(
            simulation.Metrics.Tick, CombatEventKind.Impact,
            caster, CombatTargetKind.Unit, airTarget,
            worldPosition: simulation.Units.Positions[airTarget]);
        rig.Step();
        var attackHitTargetLayerWorked =
            groundAfterAirPassive == groundBeforeAirPassive &&
            simulation.Combat.Health[airTarget] == airBeforePassive - 5f;
        var persistentCast = simulation.IssueAbility(
            1, caster, 23, AbilityCastTarget.Point(persistentCenter));
        var persistentScheduled = persistentCast.Succeeded &&
            simulation.Abilities.ActivePersistentEffectCount == 2;

        var immunity = simulation.IssueAbility(
            1, caster, 5,
            AbilityCastTarget.Unit(
                magicTarget, simulation.Units.Positions[magicTarget]));
        rig.Step();
        var blockedMagic = simulation.IssueAbility(
            2, enemy, 0,
            AbilityCastTarget.Unit(
                magicTarget, simulation.Units.Positions[magicTarget]));
        var physical = simulation.IssueAbility(
            2, enemy, 6,
            AbilityCastTarget.Unit(
                magicTarget, simulation.Units.Positions[magicTarget]));
        var classifiedHealth = simulation.Combat.Health[magicTarget];
        var damageClassificationWorked = immunity.Succeeded &&
            blockedMagic.Code == AbilityCommandCode.MagicImmune &&
            physical.Succeeded &&
            classifiedHealth == 77f;

        var firstDebuff = simulation.IssueAbility(
            2, enemy, 7,
            AbilityCastTarget.Unit(
                buffTarget, simulation.Units.Positions[buffTarget]));
        var secondDebuff = simulation.IssueAbility(
            2, secondEnemy, 7,
            AbilityCastTarget.Unit(
                buffTarget, simulation.Units.Positions[buffTarget]));
        var buffState = simulation.Abilities.ObserveBuffs(buffTarget);
        var buffIdentityWorked = firstDebuff.Succeeded &&
            secondDebuff.Succeeded &&
            buffState.Count(value => value.BuffId == "BTST") == 1 &&
            buffState.Single(value => value.BuffId == "BTST").SourceUnit ==
            secondEnemy;
        var harmfulSteal = simulation.IssueAbility(
            1, caster, 29,
            AbilityCastTarget.Unit(
                buffTarget, simulation.Units.Positions[buffTarget]));
        var harmfulReceiver = new[] { enemy, secondEnemy }
            .SingleOrDefault(unit => simulation.Abilities.ObserveBuffs(unit)
                .Any(value => value.BuffId == "BTST"), -1);
        var enemyShield = simulation.IssueAbility(
            2, enemy, 1,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var beneficialSteal = simulation.IssueAbility(
            1, caster, 29,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var harmfulGone = simulation.Abilities.ObserveBuffs(buffTarget)
            .All(value => value.BuffId != "BTST");
        var beneficialGone = simulation.Abilities.ObserveBuffs(enemy)
            .All(value => value.BuffId != "T002");
        var beneficialReceived = simulation.Abilities.ObserveBuffs(caster)
            .Any(value => value.BuffId == "T002" && value.Beneficial);
        var spellStealWorked = harmfulSteal.Succeeded &&
            harmfulReceiver >= 0 && harmfulGone &&
            enemyShield.Succeeded && beneficialSteal.Succeeded &&
            beneficialGone && beneficialReceived;
        var dispel = simulation.IssueAbility(
            1, caster, 8,
            AbilityCastTarget.Unit(
                buffTarget, simulation.Units.Positions[buffTarget]));
        var dispelWorked = dispel.Succeeded &&
            simulation.Abilities.ObserveBuffs(buffTarget)
                .All(value => value.BuffId != "BTST");
        var defaultWard = simulation.IssueAbility(
            1, caster, 9,
            AbilityCastTarget.Unit(
                wardTarget, simulation.Units.Positions[wardTarget]));
        var allowedWard = simulation.IssueAbility(
            1, caster, 10,
            AbilityCastTarget.Unit(
                wardTarget, simulation.Units.Positions[wardTarget]));
        var rejectedAncient = simulation.IssueAbility(
            1, caster, 11,
            AbilityCastTarget.Unit(
                ancientTarget, simulation.Units.Positions[ancientTarget]));
        var allowedAncient = simulation.IssueAbility(
            1, caster, 12,
            AbilityCastTarget.Unit(
                ancientTarget, simulation.Units.Positions[ancientTarget]));
        var rejectedSapper = simulation.IssueAbility(
            1, caster, 13,
            AbilityCastTarget.Unit(
                sapperTarget, simulation.Units.Positions[sapperTarget]));
        var rejectedNeutralPlayer = simulation.IssueAbility(
            1, caster, 14,
            AbilityCastTarget.Unit(
                neutralTarget, simulation.Units.Positions[neutralTarget]));
        var allowedPlayer = simulation.IssueAbility(
            1, caster, 14,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var unitTraitsWorked =
            defaultWard.Code == AbilityCommandCode.InvalidTarget &&
            allowedWard.Succeeded &&
            rejectedAncient.Code == AbilityCommandCode.InvalidTarget &&
            allowedAncient.Succeeded &&
            rejectedSapper.Code == AbilityCommandCode.InvalidTarget &&
            rejectedNeutralPlayer.Code == AbilityCommandCode.InvalidTarget &&
            allowedPlayer.Succeeded;

        var auraBeforeSecondCaster = simulation.Combat.Armor[ally];
        var alliedHero = simulation.AddUnit(
            new Vector2(66f, 78f), casterType, 1);
        rig.Step();
        var auraStackingWorked = auraBeforeSecondCaster == 7f &&
                                 simulation.Combat.Armor[ally] == 7f;
        var alliedSpend = simulation.IssueAbility(
            1, alliedHero, 0,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var allyManaBeforeTransfer =
            simulation.Abilities.Observe(alliedHero).Mana;
        var casterManaBeforeFriendlyTransfer =
            simulation.Abilities.Observe(caster).Mana;
        var friendlyManaTransfer = simulation.IssueAbility(
            1, caster, 19,
            AbilityCastTarget.Unit(
                alliedHero, simulation.Units.Positions[alliedHero]));
        var allyManaAfterFriendlyTransfer =
            simulation.Abilities.Observe(alliedHero).Mana;
        var casterManaAfterFriendlyTransfer =
            simulation.Abilities.Observe(caster).Mana;
        var manaTransferWorked = enemyManaTransferWorked &&
            alliedSpend.Succeeded && friendlyManaTransfer.Succeeded &&
            MathF.Abs(allyManaAfterFriendlyTransfer -
                MathF.Min(200f, allyManaBeforeTransfer + 30f)) < 0.001f &&
            MathF.Abs(casterManaAfterFriendlyTransfer -
                (casterManaBeforeFriendlyTransfer -
                 MathF.Min(30f, 200f - allyManaBeforeTransfer))) < 0.001f;
        var victimType = new UnitTypeProfile(
            5, "experience-victim", movement with { Id = 5 },
            combat with { MaximumHealth = 1f }, false);
        var experienceVictim = simulation.AddUnit(
            new Vector2(120f, 60f), victimType, 2);
        var experienceKill = simulation.IssueAbility(
            1, caster, 15,
            AbilityCastTarget.Unit(
                experienceVictim,
                simulation.Units.Positions[experienceVictim]));
        var casterExperience = simulation.Abilities.Observe(caster);
        var allyExperience = simulation.Abilities.Observe(alliedHero);
        var heroExperienceWorked = experienceKill.Succeeded &&
            !simulation.Units.Alive[experienceVictim] &&
            casterExperience.HeroLevel == 2 &&
            casterExperience.HeroExperience == 200 &&
            casterExperience.ExperienceForNextLevel == 500 &&
            casterExperience.UnspentSkillPoints == 1 &&
            allyExperience.HeroLevel == 2 &&
            allyExperience.HeroExperience == 200 &&
            allyExperience.UnspentSkillPoints == 2 &&
            AbilitySystem.ExperienceRequiredForLevel(10) == 5_400;
        var enemySummonCast = simulation.IssueAbility(
            2, enemy, 17,
            AbilityCastTarget.Self(enemy, simulation.Units.Positions[enemy]));
        var enemySummon = simulation.Abilities.ObserveSummons()
            .Single(value => value.SourceUnit == enemy).Unit;
        var friendlySummonCast = simulation.IssueAbility(
            1, caster, 17,
            AbilityCastTarget.Self(caster, simulation.Units.Positions[caster]));
        var friendlySummon = simulation.Abilities.ObserveSummons()
            .Single(value => value.SourceUnit == caster).Unit;
        var permanentSummonWorked = float.IsPositiveInfinity(
            simulation.Abilities.ObserveSummons()
                .Single(value => value.Unit == friendlySummon)
                .RemainingSeconds);
        var summonDispel = simulation.IssueAbility(
            1, caster, 18,
            AbilityCastTarget.Point(simulation.Units.Positions[enemySummon]));
        var summonDispelWorked = enemySummonCast.Succeeded &&
            friendlySummonCast.Succeeded && summonDispel.Succeeded &&
            !simulation.Units.Alive[enemySummon] &&
            simulation.Units.Alive[friendlySummon];

        var shield = simulation.IssueAbility(
            1, caster, 1,
            AbilityCastTarget.Unit(ally, simulation.Units.Positions[ally]));
        rig.Step();
        var shieldApplied = shield.Succeeded &&
                            simulation.Abilities.HasStatus(
                                ally, AbilityStatusFlags.Invulnerable);
        var healthBeforeBlockedDamage = simulation.Combat.Health[ally];
        var blocked = !simulation.DamageUnit(ally, 15f) &&
                      simulation.Combat.Health[ally] == healthBeforeBlockedDamage;
        var capture = simulation.CaptureRuntimeState();
        var restoredRig = MovementTestRig.CreateOpenField(
            new Vector2(240f), 24);
        restoredRig.RenderSimulation.RestoreRuntimeState(capture);
        var snapshotWorked = simulation.ComputeStateHash() ==
                             restoredRig.RenderSimulation.ComputeStateHash() &&
                             restoredRig.RenderSimulation.Abilities.HasStatus(
                                 ally, AbilityStatusFlags.Invulnerable) &&
                             restoredRig.RenderSimulation.Abilities.Observe(
                                 caster).HeroExperience == 200 &&
                             simulation.Abilities.Catalog.StableHash ==
                             restoredRig.RenderSimulation.Abilities.Catalog.StableHash;
        for (var index = 0; index < 40; index++)
        {
            rig.Step();
            restoredRig.Step();
        }
        var persistentHealth = simulation.Combat.Health[persistentTarget];
        var persistentPosition = simulation.Units.Positions[persistentTarget];
        var persistentWorked = persistentScheduled &&
            MathF.Abs(persistentHealth - 84f) <
                0.001f &&
            MathF.Abs(
                simulation.Combat.Health[persistentSecondTarget] - 84f) <
                0.001f &&
            simulation.Abilities.ActivePersistentEffectCount == 0 &&
            restoredRig.RenderSimulation.Abilities
                .ActivePersistentEffectCount == 0 &&
            simulation.ComputeStateHash() ==
                restoredRig.RenderSimulation.ComputeStateHash();
        var expired = !simulation.Abilities.HasStatus(
                          ally, AbilityStatusFlags.Invulnerable) &&
                      simulation.DamageUnit(ally, 5f);

        var waveCast = simulation.IssueAbility(
            1, caster, 24, AbilityCastTarget.Point(persistentCenter));
        for (var index = 0; index < 24; index++) rig.Step();
        var countedWavesWorked = waveCast.Succeeded &&
            MathF.Abs(simulation.Combat.Health[persistentTarget] - 72f) <
                0.001f &&
            MathF.Abs(
                simulation.Combat.Health[persistentSecondTarget] - 72f) <
                0.001f &&
            simulation.Abilities.Observe(caster).CastPhase ==
                AbilityCastPhase.None;

        var sourceCandidates = Enumerable.Range(0, simulation.Units.Count)
            .Where(unit => simulation.Units.Alive[unit] &&
                simulation.Combat.Teams[unit] == 1 &&
                unit != teleportAnchor &&
                Vector2.DistanceSquared(
                    simulation.Units.Positions[caster],
                    simulation.Units.Positions[unit]) <= 30f * 30f)
            .ToArray();
        var sourcePositions = sourceCandidates.ToDictionary(
            unit => unit, unit => simulation.Units.Positions[unit]);
        var anchorBefore = simulation.Units.Positions[teleportAnchor];
        var teleportCast = simulation.IssueAbility(
            1, caster, 25,
            AbilityCastTarget.Unit(teleportAnchor, anchorBefore));
        var moved = sourceCandidates.Where(unit =>
                simulation.Units.Positions[unit] != sourcePositions[unit])
            .ToArray();
        var clusteredTeleportWorked = teleportCast.Succeeded &&
            moved.Length == Math.Min(3, sourceCandidates.Length) &&
            moved.Contains(caster) &&
            moved.All(unit => Vector2.DistanceSquared(
                simulation.Units.Positions[unit], anchorBefore) < 70f * 70f) &&
            simulation.Units.Positions[teleportAnchor] == anchorBefore &&
            moved.SelectMany((left, index) =>
                    moved.Skip(index + 1).Select(right =>
                        Vector2.Distance(
                            simulation.Units.Positions[left],
                            simulation.Units.Positions[right])))
                .All(distance => distance >= 17f);

        var events = simulation.AbilityEvents.ReadAfter(0).Events;
        var countedWaveImpacts = events.Count(value =>
            value.AbilityId == "T025" &&
            value.Kind == AbilityEventKind.Impact);
        var persistentImpacts = events.Count(value =>
            value.AbilityId == "T024" &&
            value.Kind == AbilityEventKind.Impact);
        var lifecycleWorked = events.Count(value =>
                                  value.Kind == AbilityEventKind.Started) >= 3 &&
                              events.Any(value =>
                                  value.Kind == AbilityEventKind.Impact &&
                                  value.AbilityId == "T001");

        var passed = catalog.Count == 30 && autoHealWorked && previewWorked &&
                     directDamageWorked && shieldApplied && blocked && expired &&
                     lifecycleWorked && snapshotWorked && requirementGateWorked &&
                     heroLearningWorked && damageClassificationWorked &&
                     buffIdentityWorked && spellStealWorked && dispelWorked &&
                     unitTraitsWorked &&
                     heroExperienceWorked && auraStackingWorked;
        passed &= summonDispelWorked && permanentSummonWorked;
        passed &= manaTransferWorked;
        passed &= feedbackWorked;
        passed &= holyLightWorked;
        passed &= damageBandsWorked;
        passed &= attackHitTargetLayerWorked;
        passed &= persistentWorked;
        passed &= countedWavesWorked;
        passed &= clusteredTeleportWorked;
        passed &= unitFormResult.Passed;
        passed &= buildingFormResult.Passed;
        passed &= configuredScalarResult.Passed;
        return new SelfTestResult(
            passed,
            $"catalog={catalog.Count}, auto_heal={autoHealWorked}, " +
            $"preview={previewWorked}/{firePreview.Code}/{invalidPreview.Code}, " +
            $"damage={directDamageWorked}, shield={shieldApplied && blocked}, " +
            $"expiry={expired}, requirement={requirementGateWorked}, " +
            $"hero_learn={heroLearningWorked}, " +
            $"damage_kind={damageClassificationWorked}, " +
            $"damage_kind_detail={immunity.Code}/{blockedMagic.Code}/" +
            $"{physical.Code}/{classifiedHealth}, " +
            $"buff_identity={buffIdentityWorked}, dispel={dispelWorked}, " +
            $"spell_steal={spellStealWorked}/{harmfulReceiver}/" +
            $"{harmfulSteal.Code}/{enemyShield.Code}/" +
            $"{beneficialSteal.Code}/g={harmfulGone}/" +
            $"{beneficialGone}/{beneficialReceived}/" +
            $"enemy={string.Join('|', simulation.Abilities.ObserveBuffs(enemy).Select(value => value.BuffId))}/" +
            $"caster={string.Join('|', simulation.Abilities.ObserveBuffs(caster).Select(value => value.BuffId))}, " +
            $"unit_traits={unitTraitsWorked}, " +
            $"hero_xp={heroExperienceWorked}, " +
            $"aura_stack={auraStackingWorked}, " +
            $"summon_dispel={summonDispelWorked}, " +
            $"permanent_summon={permanentSummonWorked}, " +
            $"mana_transfer={manaTransferWorked}/" +
            $"{enemyManaBefore}->{enemyManaAfter}/" +
            $"caster={casterManaAfterEnemyTransfer}/" +
            $"{simulation.Abilities.Observe(caster).Mana:0.###}/" +
            $"friend={allyManaBeforeTransfer}->" +
            $"{simulation.Abilities.Observe(alliedHero).Mana:0.###}/" +
            $"source={casterManaBeforeFriendlyTransfer:0.###}, " +
            $"feedback={feedbackWorked}, " +
            $"holy_light={holyLightWorked}, " +
            $"damage_bands={damageBandsWorked}, " +
            $"attack_hit_layer={attackHitTargetLayerWorked}/" +
            $"{groundBeforeAirPassive}->{groundAfterAirPassive}/" +
            $"{airBeforePassive}->{simulation.Combat.Health[airTarget]}, " +
            $"band_hp={fullBandHealth}/{middleBandHealth}/{outerBandHealth}, " +
            $"persistent={persistentWorked}/" +
            $"{persistentHealth}->{simulation.Combat.Health[persistentTarget]}/" +
            $"{persistentImpacts}@{persistentPosition.X:F1}," +
            $"{persistentPosition.Y:F1}, " +
            $"counted_waves={countedWavesWorked}/{waveCast.Code}/" +
            $"{countedWaveImpacts}, " +
            $"teleport={clusteredTeleportWorked}/{moved.Length}, " +
            $"unit_form={unitFormResult.Passed}/{unitFormResult.Summary}, " +
            $"building_form={buildingFormResult.Passed}/" +
            $"{buildingFormResult.Summary}, " +
            $"configured_scalars={configuredScalarResult.Passed}/" +
            $"{configuredScalarResult.Summary}, " +
            $"events={events.Length}, snapshot={snapshotWorked}");
    }

    private static SelfTestResult TestConfiguredScalarEffects()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(220f), 22);
        var simulation = rig.RenderSimulation;
        var regeneration = new AbilityProfile(
            0, "TREG", "Configured Regeneration", string.Empty,
            string.Empty, string.Empty,
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Friendly |
            AbilityTargetFlags.NotSelf | AbilityTargetFlags.Alive,
            false, true,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 120f, 0f, 5f, 5f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.ApplyStatus,
                    AbilityEffectTiming.Impact,
                    AbilityEffectSelector.Primary,
                    AbilityRelationFilter.Friendly,
                    Duration: 5f,
                    Modifier: new AbilityStatModifier(
                        HealthRegenerationAdd: 12f),
                    BuffId: "BREG",
                    BuffPolarity: AbilityBuffPolarity.Beneficial,
                    BuffDispelKind: AbilityBuffDispelKind.Magic)],
                AutoCastRange: 30f)]);
        var feedback = new AbilityProfile(
            1, "TFBK", "Configured Feedback", string.Empty,
            string.Empty, string.Empty,
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Alive,
            false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 120f, 0f, 0f, 0f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.Mana,
                    AbilityEffectTiming.Impact,
                    AbilityEffectSelector.Primary,
                    AbilityRelationFilter.Enemy,
                    Value: -20f,
                    SecondaryValue: 1f,
                    DamageKind: AbilityDamageKind.Magic,
                    SummonedValue: 12f)])]);
        var catalog = new AbilityCatalogSnapshot(
            [regeneration, feedback],
            [new UnitAbilityBindingProfile(
                0, false, UnitManaProfile.None,
                [
                    new UnitAbilityEntryProfile(0, 1, true),
                    new UnitAbilityEntryProfile(1, 1)
                ]),
             new UnitAbilityBindingProfile(
                 1, false, UnitManaProfile.None, [])]);
        simulation.Abilities.ConfigureCatalog(catalog);
        var movement = new UnitMovementProfileSnapshot(
            0, "configured-scalar", 7.5f, 90f, 400f,
            MovementClass.Small, 8f);
        var combat = CombatProfileSnapshot.Standard with
        {
            MaximumHealth = 100f,
            AttackDamage = 0f
        };
        var casterType = new UnitTypeProfile(
            0, "configured-caster", movement, combat, false);
        var targetType = new UnitTypeProfile(
            1, "configured-target", movement with { Id = 1 }, combat, false);
        var caster = simulation.AddUnit(new Vector2(60f, 60f), casterType, 1);
        var nearby = simulation.AddUnit(new Vector2(90f, 60f), targetType, 1);
        var outsideAutoCast = simulation.AddUnit(
            new Vector2(110f, 60f), targetType, 1);
        var summoned = simulation.AddUnit(
            new Vector2(75f, 80f), targetType, 2);
        simulation.Abilities.RegisterExternalSummon(
            summoned, caster, "configured-summon");
        simulation.DamageUnit(nearby, 50f);
        simulation.DamageUnit(outsideAutoCast, 50f);
        rig.Step();
        var feedbackCast = simulation.IssueAbility(
            1, caster, 1,
            AbilityCastTarget.Unit(
                summoned, simulation.Units.Positions[summoned]));
        var selectedByConfiguredRange =
            simulation.Abilities.ObserveBuffs(nearby).SingleOrDefault()
                .Modifier.HealthRegenerationAdd == 12f &&
            simulation.Abilities.ObserveBuffs(outsideAutoCast).Length == 0;
        var regenerated = simulation.Combat.Health[nearby] > 50f &&
                          simulation.Combat.Health[outsideAutoCast] == 50f;
        var summonedDamage = feedbackCast.Succeeded &&
                            simulation.Combat.Health[summoned] == 88f;

        var hot = new SimulationHotSnapshot(
            0xA81F20UL, simulation.CaptureRuntimeState());
        var binaryRoundTrip = SimulationHotSnapshot.TryDeserialize(
            hot.CanonicalBytes, out var parsed, out var validation) &&
            parsed is not null &&
            validation == HotSnapshotValidationCode.Success &&
            parsed.State.Abilities.Catalog.Ability(0).Levels[0]
                .AutoCastRange == 30f &&
            parsed.State.Abilities.Catalog.Ability(1).Levels[0].Effects[0]
                .SummonedValue == 12f &&
            parsed.State.Abilities.Buffs.Single().Modifier
                .HealthRegenerationAdd == 12f;
        var restoredRig = MovementTestRig.CreateOpenField(
            new Vector2(220f), 22);
        if (parsed is not null)
            restoredRig.RenderSimulation.RestoreRuntimeState(parsed.State);
        for (var index = 0; index < 30 && parsed is not null; index++)
        {
            rig.Step();
            restoredRig.Step();
        }
        var restoredExact = parsed is not null &&
                            simulation.ComputeStateHash() ==
                            restoredRig.RenderSimulation.ComputeStateHash();
        var passed = selectedByConfiguredRange && regenerated &&
                     summonedDamage && binaryRoundTrip && restoredExact;
        return new SelfTestResult(
            passed,
            $"range={selectedByConfiguredRange}, regen={regenerated}/" +
            $"{simulation.Combat.Health[nearby]:F2}/" +
            $"{simulation.Combat.Health[outsideAutoCast]:F2}, " +
            $"summoned={summonedDamage}/" +
            $"{feedbackCast.Code}/" +
            $"{simulation.Abilities.IsSummoned(summoned)}/" +
            $"{simulation.Combat.Health[summoned]:F2}, " +
            $"binary={binaryRoundTrip}/{validation}, exact={restoredExact}");
    }

    private static SelfTestResult TestBuildingUnitForm(
        AbilityCatalogSnapshot catalog)
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(420f), 28);
        var simulation = rig.RenderSimulation;
        simulation.Abilities.ConfigureCatalog(catalog);
        simulation.Economy.Players.RegisterPlayer(
            1, minerals: 1_000, vespeneGas: 0,
            supplyCapacity: 20, supplyUsed: 3);
        var effect = catalog.Ability(27).Levels[0].Effects[0];
        var builder = simulation.AddUnit(
            new Vector2(200f, 205f), effect.UnitForm.Normal, 1);
        var nearby = simulation.AddUnit(
            new Vector2(112f, 210f), effect.UnitForm.Normal, 1);
        var outside = simulation.AddUnit(
            new Vector2(35f, 210f), effect.UnitForm.Normal, 1);
        simulation.Economy.RegisterWorker(builder, 1);
        simulation.Economy.RegisterWorker(nearby, 1);
        simulation.Economy.RegisterWorker(outside, 1);
        var townHallType = new BuildingTypeProfile(
            0, "building-form-town-hall", BuildingFunctionKind.TownHall,
            new Vector2(42f, 42f), MovementClass.Small,
            new EconomyCost(0, 0), 0.05f, 200f, 0, 1f,
            ConstructionMethodKind.ContinuousWorker);
        var build = simulation.IssueConstruction(
            1, builder, townHallType, new Vector2(210f, 210f),
            new EconomyResourceNodeId(-1));
        for (var index = 0; index < 720 && build.Succeeded &&
             simulation.Construction.Observe(build.BuildingId).State !=
                 BuildingLifecycleState.Completed; index++)
            rig.Step();
        if (!build.Succeeded ||
            simulation.Construction.Observe(build.BuildingId).State !=
                BuildingLifecycleState.Completed)
            return new SelfTestResult(false, $"build={build.Code}");

        var beforeCommand = simulation.CaptureRuntimeState();
        simulation.StartCommandRecording();
        var call = simulation.IssueBuildingAbility(1, build.BuildingId, 27);
        var toggled = simulation.Abilities.ObserveBuilding(
            build.BuildingId, townHallType.Id).Single().Toggled;
        var log = simulation.CaptureCommandLog();
        var serializedLog = SimulationCommandLogSnapshot.TryDeserialize(
            log.CanonicalBytes, out var roundTripLog, out var logValidation) &&
            roundTripLog is not null &&
            roundTripLog.Entries.Length == 1 &&
            roundTripLog.Entries[0].Kind ==
                UnitOrderKind.CastBuildingAbility &&
            roundTripLog.Entries[0].Units.Length == 0;
        var replayRig = MovementTestRig.CreateOpenField(
            new Vector2(420f), 28);
        replayRig.RenderSimulation.RestoreRuntimeState(beforeCommand);
        if (roundTripLog is not null)
            replayRig.RenderSimulation.ApplyRecordedCommand(
                roundTripLog.Entries[0]);
        var replayExact = simulation.ComputeStateHash() ==
                          replayRig.RenderSimulation.ComputeStateHash();

        for (var index = 0; index < 720 &&
             simulation.Abilities.Observe(nearby).UnitTypeId !=
                 effect.UnitForm.Alternate.Id; index++)
        {
            rig.Step();
            replayRig.Step();
        }
        var rangeAndForcedBuilding =
            simulation.Abilities.Observe(nearby).UnitTypeId ==
                effect.UnitForm.Alternate.Id &&
            simulation.Abilities.Observe(outside).UnitTypeId ==
                effect.UnitForm.Normal.Id &&
            simulation.ComputeStateHash() ==
                replayRig.RenderSimulation.ComputeStateHash();

        var hot = new SimulationHotSnapshot(
            0xA11CUL, simulation.CaptureRuntimeState());
        var abilityBinary = false;
        var abilityBinaryDetail = string.Empty;
        try
        {
            using var abilityBytes = new MemoryStream();
            using (var writer = new BinaryWriter(
                       abilityBytes, System.Text.Encoding.UTF8, true))
                AbilitySerialization.WriteRuntime(
                    writer, simulation.Abilities.CaptureRuntimeState(
                        simulation.Units.Count));
            abilityBytes.Position = 0;
            using var reader = new BinaryReader(
                abilityBytes, System.Text.Encoding.UTF8, true);
            var abilityRoundTrip = AbilitySerialization.ReadRuntime(
                reader, simulation.Units.Count);
            abilityBinary = abilityRoundTrip.BuildingToggles.Length == 1;
        }
        catch (Exception exception)
        {
            abilityBinaryDetail = exception.Message;
        }
        var hotParsed = SimulationHotSnapshot.TryDeserialize(
            hot.CanonicalBytes, out var restoredHot, out var hotValidation) &&
            restoredHot is not null;
        var hotRoundTrip = hotParsed && restoredHot is not null &&
            restoredHot.State.Abilities.BuildingToggles.Length == 1 &&
            restoredHot.StateHash == simulation.ComputeStateHash();

        var back = simulation.IssueBuildingAbility(1, build.BuildingId, 27);
        for (var index = 0; index < 720 &&
             simulation.Abilities.Observe(nearby).UnitTypeId !=
                 effect.UnitForm.Normal.Id; index++)
            rig.Step();
        var returned = back.Succeeded &&
            !simulation.Abilities.ObserveBuilding(
                build.BuildingId, townHallType.Id).Single().Toggled &&
            simulation.Abilities.Observe(nearby).UnitTypeId ==
                effect.UnitForm.Normal.Id &&
            simulation.Economy.IsWorker(nearby);
        var returnedDetail =
            $"{back.Code}:toggle=" +
            $"{simulation.Abilities.ObserveBuilding(
                build.BuildingId, townHallType.Id).Single().Toggled}:" +
            $"type={simulation.Abilities.Observe(nearby).UnitTypeId}:" +
            $"worker={simulation.Economy.IsWorker(nearby)}:" +
            $"forms={simulation.Abilities.ActiveUnitFormCount}";
        var eventBatch = simulation.AbilityEvents.ReadAfter(0).Events;
        var buildingEvents = eventBatch.Count(value =>
            value.AbilityId == "T028" &&
            value.CasterUnit == -1 &&
            value.CasterBuilding == build.BuildingId.Value) == 8;
        var passed = call.Succeeded && toggled && serializedLog &&
                     logValidation.Succeeded && replayExact &&
                     rangeAndForcedBuilding && abilityBinary && hotRoundTrip &&
                     hotValidation == HotSnapshotValidationCode.Success &&
                     returned && buildingEvents;
        return new SelfTestResult(
            passed,
            $"{call.Code}/{toggled}/{serializedLog}/{replayExact}/" +
            $"{rangeAndForcedBuilding}/{hotRoundTrip}" +
            $"({hotValidation}:parsed={hotParsed}:" +
            $"toggles={restoredHot?.State.Abilities.BuildingToggles.Length}:" +
            $"ability={abilityBinary}:{abilityBinaryDetail})/" +
            $"{returned}" +
            $"({returnedDetail})/" +
            $"{buildingEvents}");
    }

    private static SelfTestResult TestUnitForm(AbilityCatalogSnapshot catalog)
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(240f), 24);
        var simulation = rig.RenderSimulation;
        simulation.Abilities.ConfigureCatalog(catalog);
        simulation.Economy.Players.RegisterPlayer(
            1, minerals: 1_000, vespeneGas: 0,
            supplyCapacity: 20, supplyUsed: 1);
        var form = catalog.Ability(26).Levels[0].Effects[0].UnitForm;
        var worker = simulation.AddUnit(
            new Vector2(38f, 120f), form.Normal, 1);
        simulation.Economy.RegisterWorker(worker, 1);
        var townHallType = new BuildingTypeProfile(
            0, "form-test-town-hall", BuildingFunctionKind.TownHall,
            new Vector2(42f, 42f), MovementClass.Small,
            new EconomyCost(0, 0), 0.05f, 200f, 0, 1f,
            ConstructionMethodKind.ContinuousWorker);
        var build = simulation.IssueConstruction(
            1, worker, townHallType, new Vector2(176f, 120f),
            new EconomyResourceNodeId(-1));
        for (var index = 0; index < 720 &&
             (!build.Succeeded ||
              simulation.Construction.Observe(build.BuildingId).State !=
                  BuildingLifecycleState.Completed); index++)
            rig.Step();
        if (!build.Succeeded ||
            simulation.Construction.Observe(build.BuildingId).State !=
                BuildingLifecycleState.Completed)
            return new SelfTestResult(false, $"build={build.Code}");

        simulation.IssueMove([worker], new Vector2(38f, 40f));
        for (var index = 0; index < 480 &&
             simulation.Units.MovementLegResults[worker] ==
                 UnitMovementLegResult.InProgress; index++)
            rig.Step();
        var cast = simulation.IssueAbility(
            1, worker, 26,
            AbilityCastTarget.Self(worker, simulation.Units.Positions[worker]));
        var approached = simulation.Abilities.Observe(worker).UnitTypeId ==
                         form.Normal.Id &&
                         simulation.Abilities.ActiveUnitFormCount == 1;
        for (var index = 0; index < 720 &&
             simulation.Abilities.Observe(worker).UnitTypeId !=
                 form.Alternate.Id; index++)
            rig.Step();
        var transformed = simulation.Abilities.Observe(worker).UnitTypeId ==
                          form.Alternate.Id &&
                          !simulation.Economy.IsWorker(worker) &&
                          simulation.Combat.Armor[worker] ==
                              form.Alternate.Combat.Armor &&
                          simulation.Units.MaxSpeeds[worker] ==
                              form.Alternate.Movement.MaximumSpeed;

        for (var index = 0; index < 10; index++) rig.Step();
        var abilityCapture = simulation.Abilities.CaptureRuntimeState(
            simulation.Units.Count);
        using var abilityStream = new MemoryStream();
        using (var writer = new BinaryWriter(
                   abilityStream, System.Text.Encoding.UTF8, leaveOpen: true))
            AbilitySerialization.WriteRuntime(writer, abilityCapture);
        abilityStream.Position = 0;
        AbilityRuntimeSnapshot binaryAbility;
        using (var reader = new BinaryReader(
                   abilityStream, System.Text.Encoding.UTF8, leaveOpen: true))
            binaryAbility = AbilitySerialization.ReadRuntime(
                reader, simulation.Units.Count);
        var binaryRoundTrip = binaryAbility.UnitForms.Length == 1 &&
            binaryAbility.UnitForms[0].Phase == AbilityUnitFormPhase.Alternate &&
            binaryAbility.Catalog.StableHash == catalog.StableHash;
        var capture = simulation.CaptureRuntimeState();
        var restoredRig = MovementTestRig.CreateOpenField(
            new Vector2(240f), 24);
        restoredRig.RenderSimulation.RestoreRuntimeState(capture);
        var restoredActive = restoredRig.RenderSimulation.Abilities
            .ActiveUnitFormCount == 1 &&
            restoredRig.RenderSimulation.Abilities.Observe(worker).UnitTypeId ==
                form.Alternate.Id;
        for (var index = 0; index < 180; index++)
        {
            rig.Step();
            restoredRig.Step();
        }
        var automaticallyReturned =
            simulation.Abilities.Observe(worker).UnitTypeId == form.Normal.Id &&
            simulation.Economy.IsWorker(worker) &&
            simulation.Abilities.ActiveUnitFormCount == 0 &&
            simulation.ComputeStateHash() ==
                restoredRig.RenderSimulation.ComputeStateHash();

        var secondCast = simulation.IssueAbility(
            1, worker, 26,
            AbilityCastTarget.Self(worker, simulation.Units.Positions[worker]));
        for (var index = 0; index < 60 &&
             simulation.Abilities.Observe(worker).UnitTypeId !=
                 form.Alternate.Id; index++)
            rig.Step();
        var returnCast = simulation.IssueAbility(
            1, worker, 26,
            AbilityCastTarget.Self(worker, simulation.Units.Positions[worker]));
        for (var index = 0; index < 60 &&
             simulation.Abilities.Observe(worker).UnitTypeId !=
                 form.Normal.Id; index++)
            rig.Step();
        var manuallyReturned = secondCast.Succeeded && returnCast.Succeeded &&
            simulation.Abilities.Observe(worker).UnitTypeId == form.Normal.Id &&
            simulation.Economy.IsWorker(worker);
        var passed = cast.Succeeded && approached && transformed &&
                     binaryRoundTrip && restoredActive &&
                     automaticallyReturned && manuallyReturned;
        return new SelfTestResult(
            passed,
            $"{cast.Code}/{approached}/{transformed}/{binaryRoundTrip}/" +
            $"{restoredActive}/" +
            $"{automaticallyReturned}/{manuallyReturned}");
    }

    private static AbilityCatalogSnapshot CreateCatalog()
    {
        static ImmutableArray<AbilityLevelProfile> Level(
            float mana,
            float cooldown,
            AbilityEffectProfile effect) =>
            [new AbilityLevelProfile(
                1, mana, cooldown, 0f, 0f, 120f, 0f,
                effect.Duration, effect.Duration, [effect])];

        var fire = new AbilityProfile(
            0, "T001", "Test Bolt", string.Empty, string.Empty, "F",
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Alive | AbilityTargetFlags.Ground |
            AbilityTargetFlags.Vulnerable,
            false, false,
            Level(20f, 0.2f, new AbilityEffectProfile(
                AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Value: 30f, DamageKind: AbilityDamageKind.Magic)),
            Projectile: new AbilityProjectileProfile(
                180f, 0.15f, Homing: true));
        var shield = new AbilityProfile(
            1, "T002", "Test Shield", string.Empty, string.Empty, "S",
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Self | AbilityTargetFlags.Unit |
            AbilityTargetFlags.Friendly | AbilityTargetFlags.Alive |
            AbilityTargetFlags.Ground | AbilityTargetFlags.Vulnerable,
            false, false,
            Level(10f, 0.2f, new AbilityEffectProfile(
                AbilityEffectKind.ApplyStatus, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                Duration: 0.5f,
                Status: AbilityStatusFlags.Invulnerable,
                BuffDispelKind: AbilityBuffDispelKind.Magic)));
        var heal = new AbilityProfile(
            2, "T003", "Test Heal", string.Empty, string.Empty, "H",
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Self | AbilityTargetFlags.Unit |
            AbilityTargetFlags.Friendly | AbilityTargetFlags.Alive |
            AbilityTargetFlags.Ground | AbilityTargetFlags.Vulnerable,
            false, true,
            Level(10f, 0.2f, new AbilityEffectProfile(
                AbilityEffectKind.Heal, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                Value: 25f)));
        var gated = new AbilityProfile(
            3, "T004", "Test Gate", string.Empty, string.Empty, "G",
            AbilityActivationKind.Instant,
            AbilityTargetFlags.Self | AbilityTargetFlags.Alive,
            false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.Heal, AbilityEffectTiming.Impact,
                    AbilityEffectSelector.Caster,
                    AbilityRelationFilter.Self,
                    Value: 1f)],
                [new AbilityRequirementProfile(
                    AbilityRequirementKind.TechnologyLevel, 99, 1)])]);
        var heroSkill = new AbilityProfile(
            4, "T005", "Test Hero Skill", string.Empty, string.Empty, "L",
            AbilityActivationKind.Instant,
            AbilityTargetFlags.Self | AbilityTargetFlags.Alive,
            true, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.Heal, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Caster, AbilityRelationFilter.Self,
                Value: 1f)),
            RequiredHeroLevel: 1);
        var magicImmunity = new AbilityProfile(
            5, "T006", "Test Magic Immunity", string.Empty, string.Empty, "I",
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Self | AbilityTargetFlags.Unit |
            AbilityTargetFlags.Friendly | AbilityTargetFlags.Alive |
            AbilityTargetFlags.Ground | AbilityTargetFlags.Vulnerable,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.ApplyStatus, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                Duration: 10f, Status: AbilityStatusFlags.MagicImmune,
                BuffId: "BIMM",
                BuffPolarity: AbilityBuffPolarity.Beneficial,
                BuffDispelKind: AbilityBuffDispelKind.None)));
        var physical = new AbilityProfile(
            6, "T007", "Test Physical", string.Empty, string.Empty, "P",
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Alive | AbilityTargetFlags.Ground |
            AbilityTargetFlags.Vulnerable,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Value: 30f, DamageKind: AbilityDamageKind.Physical)));
        var debuff = new AbilityProfile(
            7, "T008", "Test Debuff", string.Empty, string.Empty, "B",
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Alive | AbilityTargetFlags.Ground |
            AbilityTargetFlags.Vulnerable,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.ApplyStatus, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Duration: 10f,
                Modifier: new AbilityStatModifier(
                    MovementSpeedMultiplier: 0.5f),
                BuffId: "BTST",
                BuffPolarity: AbilityBuffPolarity.Harmful,
                BuffDispelKind: AbilityBuffDispelKind.Magic,
                BuffStacking: AbilityBuffStackingKind.Refresh)));
        var dispel = new AbilityProfile(
            8, "T009", "Test Dispel", string.Empty, string.Empty, "D",
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Self | AbilityTargetFlags.Unit |
            AbilityTargetFlags.Friendly | AbilityTargetFlags.Alive |
            AbilityTargetFlags.Ground,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.Dispel, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                BuffDispelKind: AbilityBuffDispelKind.Magic)));
        static AbilityProfile FilterAbility(
            int id,
            string rawId,
            AbilityTargetFlags flags) => new(
            id, rawId, rawId, string.Empty, string.Empty, string.Empty,
            AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Alive |
            AbilityTargetFlags.Ground | flags,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary,
                AbilityRelationFilter.Enemy | AbilityRelationFilter.Neutral,
                Value: 1f, DamageKind: AbilityDamageKind.Universal)));
        var defaultWard = FilterAbility(
            9, "T010", AbilityTargetFlags.Enemy);
        var allowedWard = FilterAbility(
            10, "T011", AbilityTargetFlags.Enemy | AbilityTargetFlags.Ward);
        var nonAncient = FilterAbility(
            11, "T012", AbilityTargetFlags.Enemy |
            AbilityTargetFlags.NonAncient);
        var ancient = FilterAbility(
            12, "T013", AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Ancient);
        var nonSapper = FilterAbility(
            13, "T014", AbilityTargetFlags.Enemy |
            AbilityTargetFlags.NonSapper);
        var playerControlled = FilterAbility(
            14, "T015", AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Neutral | AbilityTargetFlags.PlayerControlled);
        var experienceKill = FilterAbility(
            15, "T016", AbilityTargetFlags.Enemy);
        var aura = new AbilityProfile(
            16, "T017", "Test Aura", string.Empty, string.Empty, string.Empty,
            AbilityActivationKind.Passive, AbilityTargetFlags.None,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.ApplyStatus, AbilityEffectTiming.Aura,
                AbilityEffectSelector.AreaAtCaster,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                Radius: 240f,
                Modifier: new AbilityStatModifier(ArmorAdd: 2f),
                BuffId: "BAUR",
                BuffPolarity: AbilityBuffPolarity.Beneficial,
                BuffDispelKind: AbilityBuffDispelKind.None,
                BuffStacking: AbilityBuffStackingKind.Refresh)));
        var summonMovement = new UnitMovementProfileSnapshot(
            99, "test-summon", 6f, 80f, 400f,
            MovementClass.Small, 8f);
        var summonProfile = new AbilitySummonProfile(
            "test-summon", summonMovement,
            CombatProfileSnapshot.Standard with
            {
                MaximumHealth = 30f,
                AttackDamage = 0f
            },
            UnitPerceptionProfileSnapshot.Standard,
            0f);
        var summon = new AbilityProfile(
            17, "T018", "Test Summon", string.Empty, string.Empty, string.Empty,
            AbilityActivationKind.Instant,
            AbilityTargetFlags.Self | AbilityTargetFlags.Alive,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.Summon, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtCaster,
                AbilityRelationFilter.Self,
                MaximumTargets: 1, Summon: summonProfile)));
        var summonDispel = new AbilityProfile(
            18, "T019", "Test Summon Dispel", string.Empty, string.Empty,
            string.Empty, AbilityActivationKind.TargetPoint,
            AbilityTargetFlags.Point, false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.Dispel, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtTarget,
                AbilityRelationFilter.Any,
                SecondaryValue: 50f, Radius: 60f, MaximumTargets: 32,
                DamageKind: AbilityDamageKind.Magic,
                BuffDispelKind: AbilityBuffDispelKind.Magic)));
        var manaTransfer = new AbilityProfile(
            19, "T020", "Test Mana Transfer", string.Empty, string.Empty,
            string.Empty, AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Self | AbilityTargetFlags.Unit |
            AbilityTargetFlags.Friendly | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Neutral | AbilityTargetFlags.Alive,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.TransferMana, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Any,
                Value: 30f, SecondaryValue: 1f, HeroValue: 3f)));
        var feedback = new AbilityProfile(
            20, "T021", "Test Feedback", string.Empty, string.Empty,
            string.Empty, AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Alive,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.Mana, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Value: -20f, SecondaryValue: 1f,
                DamageKind: AbilityDamageKind.Magic,
                HeroValue: -4f, HeroSecondaryValue: 1f)));
        var holyLight = new AbilityProfile(
            21, "T022", "Test Holy Light", string.Empty, string.Empty,
            string.Empty, AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Self | AbilityTargetFlags.Unit |
            AbilityTargetFlags.Friendly | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Alive,
            false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 120f, 0f, 0f, 0f,
                [
                    new AbilityEffectProfile(
                        AbilityEffectKind.Heal, AbilityEffectTiming.Impact,
                        AbilityEffectSelector.Primary,
                        AbilityRelationFilter.Self |
                        AbilityRelationFilter.Friendly,
                        Value: 30f,
                        ExcludedUnitTraits: AbilityUnitTraits.Undead),
                    new AbilityEffectProfile(
                        AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                        AbilityEffectSelector.Primary,
                        AbilityRelationFilter.Enemy,
                        Value: 30f, DamageKind: AbilityDamageKind.Magic,
                        RequiredUnitTraits: AbilityUnitTraits.Undead)
                ])]);
        var damageBands = new AbilityProfile(
            22, "T023", "Test Damage Bands", string.Empty, string.Empty,
            string.Empty, AbilityActivationKind.TargetPoint,
            AbilityTargetFlags.Point, false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 200f, 30f, 0f, 0f,
                [
                    new AbilityEffectProfile(
                        AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                        AbilityEffectSelector.AreaAtTarget,
                        AbilityRelationFilter.Enemy,
                        Value: 30f, Radius: 10f,
                        DamageKind: AbilityDamageKind.Universal),
                    new AbilityEffectProfile(
                        AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                        AbilityEffectSelector.AreaAtTarget,
                        AbilityRelationFilter.Enemy,
                        Value: 20f, Radius: 20f,
                        DamageKind: AbilityDamageKind.Universal,
                        InnerRadius: 10f),
                    new AbilityEffectProfile(
                        AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                        AbilityEffectSelector.AreaAtTarget,
                        AbilityRelationFilter.Enemy,
                        Value: 10f, Radius: 30f,
                        DamageKind: AbilityDamageKind.Universal,
                        InnerRadius: 20f)
                ])]);
        var persistentDamage = new AbilityProfile(
            23, "T024", "Test Persistent Area", string.Empty, string.Empty,
            string.Empty, AbilityActivationKind.TargetPoint,
            AbilityTargetFlags.Point, false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 200f, 20f, 0.6f, 0f,
                [
                    new AbilityEffectProfile(
                        AbilityEffectKind.Damage,
                        AbilityEffectTiming.PersistentPulse,
                        AbilityEffectSelector.AreaAtTarget,
                        AbilityRelationFilter.Neutral,
                        Value: 5f, Radius: 20f, Duration: 0.3f,
                        Interval: 0.1f,
                        DamageKind: AbilityDamageKind.Universal,
                        PulseCount: 3, MaximumTotalValue: 8f),
                    new AbilityEffectProfile(
                        AbilityEffectKind.Damage,
                        AbilityEffectTiming.PersistentPulse,
                        AbilityEffectSelector.AreaAtTarget,
                        AbilityRelationFilter.Neutral,
                        Value: 2f, Radius: 20f, Duration: 0.4f,
                        Interval: 0.1f,
                        DamageKind: AbilityDamageKind.Universal,
                        PulseCount: 2, StartDelay: 0.2f,
                        MaximumTotalValue: 4f)
                ])]);
        var countedWaves = new AbilityProfile(
            24, "T025", "Test Counted Waves", string.Empty, string.Empty,
            string.Empty, AbilityActivationKind.ChannelPoint,
            AbilityTargetFlags.Point, false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0.3f, 240f, 20f, 0f, 0f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.Damage,
                    AbilityEffectTiming.ChannelPulse,
                    AbilityEffectSelector.AreaAtTarget,
                    AbilityRelationFilter.Neutral,
                    Value: 4f, Radius: 20f, Interval: 0.1f,
                    DamageKind: AbilityDamageKind.Universal,
                    PulseCount: 3, VisualCount: 6)])]);
        var clusteredTeleport = new AbilityProfile(
            25, "T026", "Test Clustered Teleport", string.Empty,
            string.Empty, string.Empty, AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Friendly |
            AbilityTargetFlags.Alive | AbilityTargetFlags.Ground |
            AbilityTargetFlags.NotSelf,
            false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 200f, 30f, 0f, 0f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.Teleport,
                    AbilityEffectTiming.Impact,
                    AbilityEffectSelector.AreaAtCaster,
                    AbilityRelationFilter.Self |
                    AbilityRelationFilter.Friendly,
                    Radius: 30f, MaximumTargets: 3,
                    VisualCount: 3, ClusteredPlacement: true)])]);
        var normalFormMovement = new UnitMovementProfileSnapshot(
            8, "normal-form", 7.5f, 90f, 400f,
            MovementClass.Small, 8f);
        var alternateFormMovement = normalFormMovement with
        {
            Id = 9,
            Name = "alternate-form",
            MaximumSpeed = 130f
        };
        var normalForm = new UnitTypeProfile(
            8, "normal-form", normalFormMovement,
            CombatProfileSnapshot.Standard with
            {
                MaximumHealth = 100f,
                AttackDamage = 5f,
                Armor = 0f
            }, true);
        var alternateForm = new UnitTypeProfile(
            9, "alternate-form", alternateFormMovement,
            CombatProfileSnapshot.Standard with
            {
                MaximumHealth = 100f,
                AttackDamage = 12f,
                Armor = 4f
            }, false);
        var unitForm = new AbilityProfile(
            26, "T027", "Test Unit Form", string.Empty,
            string.Empty, string.Empty, AbilityActivationKind.Toggle,
            AbilityTargetFlags.Self | AbilityTargetFlags.Alive |
            AbilityTargetFlags.Ground,
            false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.TransformUnit,
                    AbilityEffectTiming.Impact,
                    AbilityEffectSelector.Caster,
                    AbilityRelationFilter.Self,
                    UnitForm: new AbilityUnitFormProfile(
                        normalForm, alternateForm, 1f,
                        BuildingFunctionKind.TownHall))])]);
        var buildingUnitForm = new AbilityProfile(
            27, "T028", "Test Building Unit Form", string.Empty,
            string.Empty, string.Empty, AbilityActivationKind.Toggle,
            AbilityTargetFlags.Self | AbilityTargetFlags.Alive |
            AbilityTargetFlags.Ground,
            false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 0f, 100f, 1f, 1f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.TransformUnit,
                    AbilityEffectTiming.Impact,
                    AbilityEffectSelector.AreaAtCaster,
                    AbilityRelationFilter.Self |
                    AbilityRelationFilter.Friendly,
                    Radius: 100f,
                    MaximumTargets: 16,
                    UnitForm: new AbilityUnitFormProfile(
                        normalForm, alternateForm, 1f,
                        BuildingFunctionKind.TownHall))])]);
        var spellSteal = new AbilityProfile(
            29, "T030", "Test Spell Steal", string.Empty,
            string.Empty, string.Empty, AbilityActivationKind.TargetUnit,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Self |
            AbilityTargetFlags.Friendly | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Neutral | AbilityTargetFlags.Alive |
            AbilityTargetFlags.Ground | AbilityTargetFlags.Vulnerable |
            AbilityTargetFlags.Invulnerable,
            false, false,
            Level(0f, 0f, new AbilityEffectProfile(
                AbilityEffectKind.TransferBuff, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Any,
                Radius: 500f,
                BuffDispelKind: AbilityBuffDispelKind.Magic)));
        var binding = new UnitAbilityBindingProfile(
            0, true, new UnitManaProfile(100f, 100f, 0f),
            [
                new UnitAbilityEntryProfile(0, 1),
                new UnitAbilityEntryProfile(1, 1),
                new UnitAbilityEntryProfile(2, 1, true),
                new UnitAbilityEntryProfile(3, 1),
                new UnitAbilityEntryProfile(4, 0),
                new UnitAbilityEntryProfile(5, 1),
                new UnitAbilityEntryProfile(6, 1),
                new UnitAbilityEntryProfile(7, 1),
                new UnitAbilityEntryProfile(8, 1),
                new UnitAbilityEntryProfile(9, 1),
                new UnitAbilityEntryProfile(10, 1),
                new UnitAbilityEntryProfile(11, 1),
                new UnitAbilityEntryProfile(12, 1),
                new UnitAbilityEntryProfile(13, 1),
                new UnitAbilityEntryProfile(14, 1),
                new UnitAbilityEntryProfile(15, 1),
                new UnitAbilityEntryProfile(16, 1),
                new UnitAbilityEntryProfile(17, 1),
                new UnitAbilityEntryProfile(18, 1),
                new UnitAbilityEntryProfile(19, 1),
                new UnitAbilityEntryProfile(20, 1),
                new UnitAbilityEntryProfile(21, 1),
                new UnitAbilityEntryProfile(22, 1),
                new UnitAbilityEntryProfile(23, 1),
                new UnitAbilityEntryProfile(24, 1),
                new UnitAbilityEntryProfile(25, 1),
                new UnitAbilityEntryProfile(28, 1),
                new UnitAbilityEntryProfile(29, 1)
            ]);
        var airAttackHit = new AbilityProfile(
            28, "T029", "Test Air Attack Hit", string.Empty,
            string.Empty, string.Empty,
            AbilityActivationKind.Passive,
            AbilityTargetFlags.Unit | AbilityTargetFlags.Enemy |
            AbilityTargetFlags.Alive | AbilityTargetFlags.Air,
            false, false,
            [new AbilityLevelProfile(
                1, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                [new AbilityEffectProfile(
                    AbilityEffectKind.Damage,
                    AbilityEffectTiming.AttackHit,
                    AbilityEffectSelector.Primary,
                    AbilityRelationFilter.Enemy,
                    Value: 5f,
                    DamageKind: AbilityDamageKind.Magic)])]);
        return new AbilityCatalogSnapshot(
            [
                fire, shield, heal, gated, heroSkill, magicImmunity,
                physical, debuff, dispel, defaultWard, allowedWard,
                nonAncient, ancient, nonSapper, playerControlled,
                experienceKill, aura, summon, summonDispel, manaTransfer,
                feedback, holyLight, damageBands, persistentDamage,
                countedWaves, clusteredTeleport, unitForm,
                buildingUnitForm, airAttackHit, spellSteal
            ],
            [
                binding,
                new UnitAbilityBindingProfile(
                    1, false, UnitManaProfile.None, []),
                new UnitAbilityBindingProfile(
                    2, false, UnitManaProfile.None, [],
                    AbilityUnitTraits.Ward),
                new UnitAbilityBindingProfile(
                    3, false, UnitManaProfile.None, [],
                    AbilityUnitTraits.Ancient),
                new UnitAbilityBindingProfile(
                    4, false, UnitManaProfile.None, [],
                    AbilityUnitTraits.Sapper),
                new UnitAbilityBindingProfile(
                    5, false, UnitManaProfile.None, [],
                    UnitLevel: 5, ExperienceBounty: 400),
                new UnitAbilityBindingProfile(
                    6, false, new UnitManaProfile(100f, 100f, 0f), []),
                new UnitAbilityBindingProfile(
                    7, false, UnitManaProfile.None, [],
                    AbilityUnitTraits.Undead),
                new UnitAbilityBindingProfile(
                    8, false, UnitManaProfile.None,
                    [new UnitAbilityEntryProfile(26, 1)]),
                new UnitAbilityBindingProfile(
                    9, false, UnitManaProfile.None,
                    [new UnitAbilityEntryProfile(26, 1)])
            ],
            [new BuildingAbilityBindingProfile(0, [27])]);
    }
}
