using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime;

/// <summary>
/// Presentation-only Resource diff panel. Loading and application policy stay
/// in the Resource set model and Godot composition layer.
/// </summary>
public partial class RtsResourceReloadControl : Control
{
    private static readonly Color Background = new("101923e8");
    private static readonly Color Border = new("6aa6c8");
    private static readonly Color Good = new("67d69a");
    private static readonly Color Warning = new("f0b45d");
    private RuntimeResourceReloadPlan? _plan;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(720f, 190f);
    }

    public void SetPlan(RuntimeResourceReloadPlan plan)
    {
        _plan = plan;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background, true);
        DrawRect(new Rect2(Vector2.Zero, Size), Border, false, 2f);
        if (_plan is null)
        {
            DrawText(new Vector2(16f, 28f), "No Resource reload candidate", Warning);
            return;
        }

        var impactColor = _plan.Impact == ResourceReloadImpact.None ? Good : Warning;
        DrawText(
            new Vector2(16f, 28f),
            $"Atomic Resource Reload  impact={_plan.Impact}",
            impactColor,
            18);
        DrawText(
            new Vector2(16f, 58f),
            $"Navigation  {_plan.Current.Navigation.StableHashText} -> " +
            $"{_plan.Candidate.Navigation.StableHashText}   " +
            $"obstacles {_plan.Navigation.ChangedObstacles}  " +
            $"portals {_plan.Navigation.ChangedPortals}  edges {_plan.Navigation.ChangedEdges}",
            Colors.White);
        DrawText(
            new Vector2(16f, 88f),
            $"Profiles    {_plan.Current.GameplayProfiles.StableHashText} -> " +
            $"{_plan.Candidate.GameplayProfiles.StableHashText}   " +
            $"units {_plan.GameplayProfiles.ChangedUnitProfiles}  " +
            $"buildings {_plan.GameplayProfiles.ChangedBuildingProfiles}",
            Colors.White);
        DrawText(
            new Vector2(16f, 118f),
            $"Clearance   {_plan.Current.ClearanceBake.StableHashText} -> " +
            $"{_plan.Candidate.ClearanceBake.StableHashText}   " +
            $"source changed={_plan.ClearanceBake.SourceNavigationChanged}",
            Colors.White);
        DrawText(
            new Vector2(16f, 155f),
            "Validated as one set; live mutation is blocked when rebuild is required.",
            Good);
    }

    private void DrawText(
        Vector2 position,
        string text,
        Color color,
        int size = 15) =>
        DrawString(
            ThemeDB.FallbackFont,
            position,
            text,
            HorizontalAlignment.Left,
            -1f,
            size,
            color);
}
