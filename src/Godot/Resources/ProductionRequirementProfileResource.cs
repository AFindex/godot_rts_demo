using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class ProductionRequirementProfileResource : Resource
{
    [Export] public int RequiredBuildingTypeId { get; set; }
    [Export(PropertyHint.Range, "1,32,1")]
    public int RequiredCompletedCount { get; set; } = 1;
}
