using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class UnitTypeProfileResource : Resource
{
    [Export] public int Id { get; set; }
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export(PropertyHint.Range, "1,128,0.1,or_greater")]
    public float PhysicalRadius { get; set; } = 7.5f;
    [Export(PropertyHint.Range, "1,1000,1,or_greater")]
    public float MaximumSpeed { get; set; } = 128f;
    [Export(PropertyHint.Range, "1,5000,1,or_greater")]
    public float Acceleration { get; set; } = 720f;
    [Export(PropertyHint.Range, "1,100000,1,or_greater")]
    public float MaximumHealth { get; set; } = 45f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")]
    public float AttackDamage { get; set; } = 8f;
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float AttackRange { get; set; } = 34f;
    [Export(PropertyHint.Range, "1,2000,1,or_greater")]
    public float AcquisitionRange { get; set; } = 155f;
    [Export(PropertyHint.Range, "0.01,60,0.01,or_greater")]
    public float AttackCooldownSeconds { get; set; } = 0.72f;
    [Export(PropertyHint.Range, "0,60,0.01,or_greater")]
    public float AttackWindupSeconds { get; set; } = 0.18f;
    [Export(PropertyHint.Range, "1,5000,1,or_greater")]
    public float LeashDistance { get; set; } = 260f;
    [Export] public CombatPositioningKind Positioning { get; set; } =
        CombatPositioningKind.Ranged;
    [Export] public bool IsWorker { get; set; }
}
