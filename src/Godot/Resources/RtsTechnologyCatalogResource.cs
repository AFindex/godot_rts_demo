using Godot;
using Godot.Collections;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsTechnologyCatalogResource : Resource
{
    [Export] public int FormatVersion { get; set; } = 1;
    [Export] public Array<TechnologyProfileResource> Technologies { get; set; } =
        new();
}
