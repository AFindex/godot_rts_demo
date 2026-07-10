using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class BuildingFootprintProfileResource : Resource
{
    [Export]
    public int Id { get; set; }

    [Export]
    public string DisplayName { get; set; } = string.Empty;

    [Export]
    public BuildingFootprintClass FootprintClass { get; set; }

    [Export]
    public Vector2 Size { get; set; } = new(64f, 48f);

    [Export]
    public MovementClass MinimumPassageClass { get; set; } = MovementClass.Medium;

    [Export(PropertyHint.Range, "0,64,0.5,or_greater")]
    public float UnitPadding { get; set; } = 2f;
}
