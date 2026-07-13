using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsCombatPresentationThemeResource : Resource
{
    [Export] public Color BoltColor { get; set; } = new("ffd166");
    [Export] public Color OrbColor { get; set; } = new("ff8c42");
    [Export] public Color VolleyColor { get; set; } = new("70d6ff");
    [Export] public Color ImpactColor { get; set; } = new("fff3b0");
    [Export] public Color BonusImpactColor { get; set; } = new("ff70a6");
    [Export] public Color MuzzleFlashColor { get; set; } = new("fff4b8");
    [Export] public Color ExpiredColor { get; set; } = new("9aa5b1");
    [Export(PropertyHint.Range, "1,20,0.5")]
    public float BoltRadius { get; set; } = 4f;
    [Export(PropertyHint.Range, "1,20,0.5")]
    public float OrbRadius { get; set; } = 6f;
    [Export(PropertyHint.Range, "1,20,0.5")]
    public float VolleyRadius { get; set; } = 5f;
    [Export(PropertyHint.Range, "0.5,8,0.5")]
    public float TrailWidth { get; set; } = 2f;
    [Export(PropertyHint.Range, "4,80,1")]
    public float ImpactRadius { get; set; } = 28f;
    [Export(PropertyHint.Range, "3,30,1")]
    public float MuzzleFlashRadius { get; set; } = 10f;
}
