using Godot;
using Godot.Collections;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class TechnologyProfileResource : Resource
{
    [Export] public int Id { get; set; }
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export] public int ResearcherBuildingTypeId { get; set; }
    [Export(PropertyHint.Range, "0,10000,1,or_greater")]
    public int MineralCost { get; set; }
    [Export(PropertyHint.Range, "0,10000,1,or_greater")]
    public int VespeneCost { get; set; }
    [Export(PropertyHint.Range, "0.01,600,0.01,or_greater")]
    public float ResearchSeconds { get; set; } = 1f;
    [Export(PropertyHint.Range, "1,32,1,or_greater")]
    public int MaximumLevel { get; set; } = 1;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CancelRefundFraction { get; set; } = 0.75f;
    [Export] public int ExclusiveGroupId { get; set; } = -1;
    [Export]
    public Array<TechnologyRequirementProfileResource> Requirements { get; set; } =
        new();
}
