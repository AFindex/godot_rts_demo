using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class NavigationPortalEdgeResource : Resource
{
    [Export]
    public int FromPortal { get; set; }

    [Export]
    public int ToPortal { get; set; }

    [Export(PropertyHint.Range, "1,4096,1,or_greater")]
    public float Width { get; set; } = 64f;

    [Export]
    public int ChokeId { get; set; } = -1;
}
