using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class UnitMovementProfileResource : Resource
{
    [Export]
    public int Id { get; set; }

    [Export]
    public string DisplayName { get; set; } = string.Empty;

    [Export(PropertyHint.Range, "0.5,64,0.5,or_greater")]
    public float PhysicalRadius { get; set; } = 7.5f;

    [Export(PropertyHint.Range, "1,2048,1,or_greater")]
    public float MaximumSpeed { get; set; } = 128f;

    [Export(PropertyHint.Range, "1,8192,1,or_greater")]
    public float Acceleration { get; set; } = 720f;

    [Export(PropertyHint.Range, "0.01,200,0.01,or_greater")]
    public float TurnRateRadiansPerSecond { get; set; } =
        UnitFacing.LegacyTurnRateRadiansPerSecond;
}
