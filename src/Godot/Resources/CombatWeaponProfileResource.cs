using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class CombatWeaponProfileResource : Resource
{
    [Export(PropertyHint.Range, "0,7,1")]
    public int Slot { get; set; }
    [Export] public CombatTargetLayer TargetLayers { get; set; } =
        CombatTargetLayer.All;
    [Export] public bool EnabledByDefault { get; set; } = true;
    [Export] public int RequiredTechnologyId { get; set; } = -1;
    [Export] public float AttackDamage { get; set; }
    [Export] public float AttackRange { get; set; }
    [Export] public float AttackCooldownSeconds { get; set; } = 1f;
    [Export] public float AttackWindupSeconds { get; set; }
    [Export] public CombatPositioningKind Positioning { get; set; } =
        CombatPositioningKind.Ranged;
    [Export(PropertyHint.Range, "1,32,1")]
    public int AttacksPerVolley { get; set; } = 1;
    [Export] public CombatAttribute BonusVs { get; set; }
    [Export] public float BonusDamage { get; set; }
    [Export] public float BaseUpgradeDamage { get; set; }
    [Export] public float BonusUpgradeDamage { get; set; }
    [Export] public float ProjectileSpeed { get; set; }
    [Export] public bool CanMoveDuringWindup { get; set; }
    [Export] public bool CanMoveDuringCooldown { get; set; }
    [Export] public CombatAttackType AttackType { get; set; } =
        CombatAttackType.Legacy;
    [Export] public int DamageUpgradeTechnologyId { get; set; } = -1;
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float MinimumRange { get; set; }
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float FullDamageRadius { get; set; }
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float HalfDamageRadius { get; set; }
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float QuarterDamageRadius { get; set; }
    [Export] public CombatTargetLayer AreaTargetLayers { get; set; }
    [Export] public CombatWeaponPropagationKind PropagationKind { get; set; }
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float PropagationLineDistance { get; set; }
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float PropagationRadius { get; set; }
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float PropagationDamageLossFactor { get; set; }
    [Export(PropertyHint.Range, "0,32,1")]
    public int PropagationMaximumTargets { get; set; }
    [Export] public CombatTargetLayer PropagationTargetLayers { get; set; }
    [Export] public int PropagationDistanceUpgradeTechnologyId { get; set; } = -1;
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float PropagationDistanceUpgradePerLevel { get; set; }
}
