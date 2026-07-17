using Godot;
using RtsDemo.Simulation;

namespace War3Rts;

/// <summary>
/// Warcraft's selected-point confirmation is one grayscale three-arrow model.
/// The game applies green for movement and red for attack orders at runtime.
/// </summary>
public static class War3CommandFeedbackCatalog
{
    public const string ConfirmationSource =
        @"UI\Feedback\Confirmation\Confirmation.mdx";
    public const ulong VisibleLifetimeMilliseconds = 1_150;
    public const int MaximumSimultaneousConfirmations = 12;

    public static Color Tint(War3CommandFeedbackKind kind) => kind switch
    {
        War3CommandFeedbackKind.Attack => new Color("ff3838"),
        _ => new Color("45f05a")
    };

    public static War3CommandFeedbackKind ForSmartTarget(
        SmartCommandTargetKind kind) => kind is
        SmartCommandTargetKind.EnemyUnit or
        SmartCommandTargetKind.EnemyBuilding
            ? War3CommandFeedbackKind.Attack
            : War3CommandFeedbackKind.Move;
}
