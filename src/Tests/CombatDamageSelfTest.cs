using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class CombatDamageSelfTest
{
    public static SelfTestResult Run()
    {
        var weapon = new CombatWeaponDamageSnapshot(
            10f, 2, CombatAttribute.Armored, 5f, 2, 1f, 2f);
        var armored = CombatDamageResolver.Resolve(
            weapon,
            new CombatDefenseSnapshot(
                3f, CombatAttribute.Armored | CombatAttribute.Mechanical),
            100f);
        var light = CombatDamageResolver.Resolve(
            weapon,
            new CombatDefenseSnapshot(
                3f, CombatAttribute.Light | CombatAttribute.Biological),
            100f);
        var minimum = CombatDamageResolver.Resolve(
            new CombatWeaponDamageSnapshot(
                1f, 2, CombatAttribute.None, 0f, 0, 0f, 0f),
            new CombatDefenseSnapshot(20f, CombatAttribute.Armored),
            0.7f);
        var passed = armored.BonusApplied && armored.DamagePerAttack == 18f &&
                     armored.TotalDamage == 36f && armored.AttacksApplied == 2 &&
                     !light.BonusApplied && light.DamagePerAttack == 9f &&
                     light.TotalDamage == 18f &&
                     minimum.DamagePerAttack == 0.5f &&
                     minimum.TotalDamage == 0.7f && minimum.Killed;
        return new SelfTestResult(passed,
            $"armored={armored.DamagePerAttack}x{armored.AttacksApplied}, " +
            $"light={light.DamagePerAttack}x{light.AttacksApplied}, " +
            $"minimum={minimum.TotalDamage}");
    }
}
