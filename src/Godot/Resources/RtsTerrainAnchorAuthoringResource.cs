using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsTerrainAnchorAuthoringResource : Resource
{
    [Export]
    public int Id { get; set; }

    [Export]
    public TerrainAuthoringAnchorKind Kind { get; set; }

    [Export]
    public Vector2 Position { get; set; }

    [Export(PropertyHint.Range, "0,128,0.25,or_greater")]
    public float RequiredRadius { get; set; } = 8f;
}
