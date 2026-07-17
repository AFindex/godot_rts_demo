using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class BuildingTypeProfileResource : Resource
{
    [Export] public int Id { get; set; }
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export] public BuildingFunctionKind Function { get; set; }
    [Export] public Vector2 Size { get; set; } = new(48f, 48f);
    [Export] public MovementClass MinimumPassageClass { get; set; } =
        MovementClass.Medium;
    [Export(PropertyHint.Range, "0,10000,1,or_greater")]
    public int MineralCost { get; set; }
    [Export(PropertyHint.Range, "0,10000,1,or_greater")]
    public int VespeneCost { get; set; }
    [Export(PropertyHint.Range, "0,200,1,or_greater")]
    public int SupplyCost { get; set; }
    [Export(PropertyHint.Range, "0.01,600,0.01,or_greater")]
    public float BuildSeconds { get; set; } = 1f;
    [Export(PropertyHint.Range, "1,100000,1,or_greater")]
    public float MaximumHealth { get; set; } = 100f;
    [Export(PropertyHint.Range, "0,200,1,or_greater")]
    public int SupplyProvided { get; set; }
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CancelRefundFraction { get; set; } = 0.75f;
    [Export] public ConstructionMethodKind ConstructionMethod { get; set; }
    [Export] public bool RequiresVespeneNode { get; set; }
    [Export(PropertyHint.Range, "0,100,0.5,or_greater")]
    public float Armor { get; set; }
    [Export] public CombatAttribute Attributes { get; set; } =
        CombatAttribute.Structure | CombatAttribute.Mechanical;
    [Export(PropertyHint.Range, "0,100,0.5,or_greater")]
    public float ArmorUpgradePerLevel { get; set; }
    [Export] public CombatArmorType ArmorType { get; set; } =
        CombatArmorType.Legacy;
}
