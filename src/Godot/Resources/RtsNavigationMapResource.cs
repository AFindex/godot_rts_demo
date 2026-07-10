using Godot;
using Godot.Collections;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsNavigationMapResource : Resource
{
    [Export]
    public int FormatVersion { get; set; } = 1;

    [Export]
    public Rect2 WorldBounds { get; set; }

    [Export]
    public Array<NavigationObstacleResource> Obstacles { get; set; } = new();

    [Export]
    public Array<NavigationPortalResource> Portals { get; set; } = new();

    [Export]
    public Array<NavigationPortalEdgeResource> Edges { get; set; } = new();

    [Export]
    public Array<NavigationChokeResource> Chokes { get; set; } = new();
}
