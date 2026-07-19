using Godot;

namespace War3Rts;

/// <summary>
/// Standalone entry point for the one-mortar production-pipeline regression.
/// It injects test-only launch arguments before instantiating the real game.
/// </summary>
public sealed partial class War3MortarProjectileRegressionScene : Node3D
{
    public override void _Ready()
    {
        War3LaunchRequest.RequestMortarProjectileRegression();
        var scene = GD.Load<PackedScene>("res://war3_rts/War3Rts.tscn");
        AddChild(scene.Instantiate());
    }
}
