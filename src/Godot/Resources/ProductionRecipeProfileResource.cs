using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class ProductionRecipeProfileResource : Resource
{
    [Export] public int Id { get; set; }
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export] public int ProducerBuildingTypeId { get; set; }
    [Export] public int UnitTypeId { get; set; }
    [Export(PropertyHint.Range, "0,10000,1,or_greater")]
    public int MineralCost { get; set; }
    [Export(PropertyHint.Range, "0,10000,1,or_greater")]
    public int VespeneCost { get; set; }
    [Export(PropertyHint.Range, "1,200,1,or_greater")]
    public int SupplyCost { get; set; } = 1;
    [Export(PropertyHint.Range, "0.01,600,0.01,or_greater")]
    public float ProductionSeconds { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CancelRefundFraction { get; set; } = 1f;
}
