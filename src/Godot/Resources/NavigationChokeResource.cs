using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class NavigationChokeResource : Resource
{
    [Export]
    public int Id { get; set; }

    [Export]
    public Vector2 A { get; set; }

    [Export]
    public Vector2 B { get; set; }

    [Export(PropertyHint.Range, "1,4096,1,or_greater")]
    public float Width { get; set; } = 64f;

    [Export(PropertyHint.Range, "1,4096,1,or_greater")]
    public float ApproachDistance { get; set; } = 128f;
}
