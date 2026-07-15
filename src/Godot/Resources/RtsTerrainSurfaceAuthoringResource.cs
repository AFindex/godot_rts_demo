using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsTerrainSurfaceAuthoringResource : Resource
{
    [Export]
    public int Id { get; set; }

    [Export]
    public string MaterialKey { get; set; } = string.Empty;

    [Export]
    public string DisplayName { get; set; } = string.Empty;
}
