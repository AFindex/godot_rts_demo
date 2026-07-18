using Godot;
using Godot.Collections;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsBuildingTypeCatalogResource : Resource
{
    [Export] public int FormatVersion { get; set; } =
        BuildingTypeCatalogSnapshot.CurrentFormatVersion;
    [Export] public Array<BuildingTypeProfileResource> Types { get; set; } = new();
}
