using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime;

/// <summary>
/// Presentation-only placement diff panel. It consumes a pure C# snapshot and
/// never analyzes navigation or mutates placement state.
/// </summary>
public partial class RtsBuildingConnectivityDiffControl : Control
{
    private static readonly Color Background = new("101923ed");
    private static readonly Color Border = new("6aa6c8");
    private static readonly Color Safe = new("67d69a");
    private static readonly Color Blocked = new("ff765f");
    private BuildingConnectivityDiffSnapshot? _snapshot;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(720f, 218f);
    }

    public void SetSnapshot(BuildingConnectivityDiffSnapshot snapshot)
    {
        _snapshot = snapshot;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background, true);
        DrawRect(new Rect2(Vector2.Zero, Size), Border, false, 2f);
        if (_snapshot is null)
        {
            return;
        }

        DrawText(
            new Vector2(16f, 28f),
            $"Placement Connectivity Diff   " +
            $"{(_snapshot.PreservedForAll ? "SAFE" : "DISCONNECTS NAVIGATION")}",
            _snapshot.PreservedForAll ? Safe : Blocked,
            18);
        DrawText(
            new Vector2(16f, 55f),
            $"footprint {_snapshot.ProposedFootprint.Width:0}x" +
            $"{_snapshot.ProposedFootprint.Height:0}   " +
            $"dirty chunks {string.Join(',', _snapshot.DirtyChunks.Select(c => c.Id))}",
            Colors.White);
        for (var index = 0; index < _snapshot.Classes.Length; index++)
        {
            var value = _snapshot.Classes[index];
            DrawText(
                new Vector2(16f, 88f + index * 36f),
                $"{value.MovementClass,-6}  components " +
                $"{value.BaselineComponents}->{value.CandidateComponents}   " +
                $"blocked {value.BlockedCells,3}   split {value.SplitComponents}   " +
                $"disconnected {value.DisconnectedCells,4}   " +
                $"{(value.Preserved ? "PRESERVED" : "REJECT")}",
                value.Preserved ? Safe : Blocked);
        }
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
