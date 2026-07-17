using System.Collections.Immutable;
using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class AbilitySystemSelfTest
{
    public static SelfTestResult Run()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(240f), 16);
        var simulation = rig.RenderSimulation;
        var catalog = CreateCatalog();
        simulation.Abilities.ConfigureCatalog(catalog);

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
            1, "target", movement with { Id = 1 }, combat, false);
        var caster = simulation.AddUnit(new Vector2(60f, 60f), casterType, 1);
        var ally = simulation.AddUnit(new Vector2(72f, 60f), targetType, 1);
        var enemy = simulation.AddUnit(new Vector2(84f, 60f), targetType, 2);

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

        var fire = simulation.IssueAbility(
            1, caster, 0,
            AbilityCastTarget.Unit(enemy, simulation.Units.Positions[enemy]));
        var directDamageWorked = fire.Succeeded &&
                                 simulation.Combat.Health[enemy] == 70f;

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
            new Vector2(240f), 16);
        restoredRig.RenderSimulation.RestoreRuntimeState(capture);
        var snapshotWorked = simulation.ComputeStateHash() ==
                             restoredRig.RenderSimulation.ComputeStateHash() &&
                             restoredRig.RenderSimulation.Abilities.HasStatus(
                                 ally, AbilityStatusFlags.Invulnerable) &&
                             simulation.Abilities.Catalog.StableHash ==
                             restoredRig.RenderSimulation.Abilities.Catalog.StableHash;
        for (var index = 0; index < 40; index++) rig.Step();
        var expired = !simulation.Abilities.HasStatus(
                          ally, AbilityStatusFlags.Invulnerable) &&
                      simulation.DamageUnit(ally, 5f);

        var events = simulation.AbilityEvents.ReadAfter(0).Events;
        var lifecycleWorked = events.Count(value =>
                                  value.Kind == AbilityEventKind.Started) >= 3 &&
                              events.Any(value =>
                                  value.Kind == AbilityEventKind.Impact &&
                                  value.AbilityId == "T001");

        var passed = catalog.Count == 5 && autoHealWorked &&
                     directDamageWorked && shieldApplied && blocked && expired &&
                     lifecycleWorked && snapshotWorked && requirementGateWorked &&
                     heroLearningWorked;
        return new SelfTestResult(
            passed,
            $"catalog={catalog.Count}, auto_heal={autoHealWorked}, " +
            $"damage={directDamageWorked}, shield={shieldApplied && blocked}, " +
            $"expiry={expired}, requirement={requirementGateWorked}, " +
            $"hero_learn={heroLearningWorked}, " +
            $"events={events.Length}, snapshot={snapshotWorked}");
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
                Value: 30f)));
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
                Status: AbilityStatusFlags.Invulnerable)));
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
        var binding = new UnitAbilityBindingProfile(
            0, true, new UnitManaProfile(100f, 100f, 0f),
            [
                new UnitAbilityEntryProfile(0, 1),
                new UnitAbilityEntryProfile(1, 1),
                new UnitAbilityEntryProfile(2, 1, true),
                new UnitAbilityEntryProfile(3, 1),
                new UnitAbilityEntryProfile(4, 0)
            ]);
        return new AbilityCatalogSnapshot(
            [fire, shield, heal, gated, heroSkill], [binding]);
    }
}
