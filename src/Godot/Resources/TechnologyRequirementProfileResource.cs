using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class TechnologyRequirementProfileResource : Resource
{
    [Export] public TechnologyRequirementKind Kind { get; set; }
    [Export] public int TargetId { get; set; }
    [Export(PropertyHint.Range, "1,32,1,or_greater")]
    public int RequiredValue { get; set; } = 1;
}
