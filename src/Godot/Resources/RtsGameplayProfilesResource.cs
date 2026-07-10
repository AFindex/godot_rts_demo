using Godot;
using Godot.Collections;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsGameplayProfilesResource : Resource
{
    [Export]
    public int FormatVersion { get; set; } = 1;

    [Export]
    public Array<UnitMovementProfileResource> UnitProfiles { get; set; } = new();

    [Export]
    public Array<BuildingFootprintProfileResource> BuildingProfiles { get; set; } = new();
}
