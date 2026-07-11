using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class AiDifficultyProfileResource : Resource
{
    [Export] public int Id { get; set; }
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export] public int TargetWorkers { get; set; } = 10;
    [Export] public int AttackArmySize { get; set; } = 6;
    [Export] public int MaximumIntentsPerDecision { get; set; } = 6;
    [Export] public int SupplyBuffer { get; set; } = 3;
    [Export] public int DecisionIntervalTicks { get; set; } = 12;
    [Export] public int ScoutIntervalTicks { get; set; } = 360;
    [Export] public int AttackIntervalTicks { get; set; } = 240;
    [Export] public float DefenseRadius { get; set; } = 340f;
}
