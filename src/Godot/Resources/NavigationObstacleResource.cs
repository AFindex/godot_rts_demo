using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class NavigationObstacleResource : Resource
{
    [Export]
    public Rect2 Bounds { get; set; }
}
