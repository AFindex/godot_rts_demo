using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime;

/// <summary>Presentation-only view of the resource watch workflow.</summary>
public partial class RtsResourceWatchControl : Control
{
    private static readonly Color Background = new("101923ed");
    private static readonly Color Border = new("6aa6c8");
    private static readonly Color Good = new("67d69a");
    private static readonly Color Pending = new("f0b45d");
    private static readonly Color Error = new("ff765f");
    private ResourceReloadWorkflowSnapshot _snapshot =
        ResourceReloadWorkflowSnapshot.Idle;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(720f, 190f);
    }

    public void SetSnapshot(ResourceReloadWorkflowSnapshot snapshot)
    {
        _snapshot = snapshot;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background, true);
        DrawRect(new Rect2(Vector2.Zero, Size), Border, false, 2f);
        var color = _snapshot.State switch
        {
            ResourceReloadWorkflowState.Applied or
            ResourceReloadWorkflowState.Unchanged => Good,
            ResourceReloadWorkflowState.Failed => Error,
            _ => Pending
        };
        DrawText(new Vector2(16f, 30f),
            $"Resource Watch Workflow   {_snapshot.State}", color, 18);
        DrawText(new Vector2(16f, 64f),
            $"changes {_snapshot.Changes}   notices {_snapshot.NoticeCount}   " +
            $"attempt {_snapshot.Attempt}", Colors.White);
        DrawText(new Vector2(16f, 96f),
            $"impact {_snapshot.Impact}   commit {_snapshot.CommitCode}   " +
            $"replanned {_snapshot.ReplannedUnits}", Colors.White);
        DrawText(new Vector2(16f, 132f), _snapshot.Message, color);
        DrawText(new Vector2(16f, 162f),
            "File callbacks queue only; Fresh Load + commit run on Godot main thread.",
            Good);
    }

    private void DrawText(
        Vector2 position,
        string text,
        Color color,
        int size = 15) =>
        DrawString(ThemeDB.FallbackFont, position, text,
            HorizontalAlignment.Left, -1f, size, color);
}
