using Godot;
using Godot.Collections;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsBuildingTypeCatalogResource : Resource
{
    [Export] public int FormatVersion { get; set; } = 2;
    [Export] public Array<BuildingTypeProfileResource> Types { get; set; } = new();
}
