using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime;

public partial class RtsTargetCommandOverlay : Node2D
{
    private TargetCommandRequest? _request;
    private Vector2 _worldPosition;
    private BuildTargetPreviewSnapshot? _buildPreview;

    public RtsTargetCommandOverlay()
    {
        ZIndex = 80;
    }

    public void SetSnapshot(
        TargetCommandRequest? request,
        Vector2 worldPosition,
        BuildTargetPreviewSnapshot? buildPreview = null)
    {
        if (ReferenceEquals(_request, request) && _worldPosition == worldPosition &&
            _buildPreview == buildPreview)
            return;
        _request = request;
        _worldPosition = worldPosition;
        _buildPreview = buildPreview;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_request is null) return;
        var color = _request.Kind == TargetCommandKind.AttackMove
            ? new Color("ff6659")
            : _request.Kind == TargetCommandKind.Build
                ? _buildPreview?.CanPlace == true
                    ? new Color("65e88a")
                    : new Color("ff6659")
            : _request.Kind == TargetCommandKind.Rally
                ? new Color("ffd166")
                : new Color("65e88a");
        if (_request.Kind == TargetCommandKind.Build && _buildPreview is not null)
        {
            var bounds = _buildPreview.Bounds;
            var rect = new Rect2(
                new Vector2(bounds.Min.X, bounds.Min.Y),
                new Vector2(bounds.Width, bounds.Height));
            DrawRect(rect, color with { A = 0.25f }, true);
            DrawRect(rect, color, false, 2f);
        }
        DrawArc(_worldPosition, 18f, 0f, MathF.Tau, 32, color, 2f);
        DrawLine(_worldPosition - new Vector2(8f, 0f),
            _worldPosition + new Vector2(8f, 0f), color, 2f);
        DrawLine(_worldPosition - new Vector2(0f, 8f),
            _worldPosition + new Vector2(0f, 8f), color, 2f);

        var panel = new Rect2(
            _worldPosition + new Vector2(24f, -27f),
            new Vector2(500f, 24f));
        DrawRect(panel, new Color(0.04f, 0.07f, 0.10f, 0.88f), true);
        DrawRect(panel, color with { A = 0.75f }, false, 1f);
        DrawString(
            ThemeDB.FallbackFont,
            panel.Position + new Vector2(8f, 16f),
            _request.Kind == TargetCommandKind.Build && _buildPreview is not null
                ? $"{_request.Label}: {_buildPreview.Status}  LMB confirm  RMB/Esc cancel"
                : $"{_request.Label}  LMB confirm  RMB/Esc cancel",
            HorizontalAlignment.Left, panel.Size.X - 16f, 11, color);
    }
}
