using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class NavigationPortalResource : Resource
{
    [Export]
    public int Id { get; set; }

    [Export]
    public Vector2 Position { get; set; }

    [Export]
    public string DisplayName { get; set; } = string.Empty;
}
