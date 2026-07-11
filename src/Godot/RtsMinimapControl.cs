using Godot;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime;

/// <summary>
/// Presentation-only minimap. It consumes immutable pure-C# snapshots and
/// emits world-space intents; it never reads RtsSimulation or issues commands.
/// </summary>
public partial class RtsMinimapControl : Control
{
    private static readonly Color Background = new("0b1119");
    private static readonly Color Border = new("557086");
    private static readonly Color Obstacle = new("314252");
    private static readonly Color Friendly = new("50d890");
    private static readonly Color Enemy = new("ff6573");
    private static readonly Color Building = new("f0a85a");
    private static readonly Color Viewport = new(0.45f, 0.85f, 1f, 0.9f);

    private MinimapSnapshot? _snapshot;

    public event Action<NVector2>? FocusRequested;
    public event Action<NVector2>? SmartCommandRequested;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        CustomMinimumSize = new Vector2(230f, 140f);
    }

    public void SetSnapshot(MinimapSnapshot snapshot)
    {
        _snapshot = snapshot;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background, filled: true);
        if (_snapshot is null || Size.X <= 0f || Size.Y <= 0f)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), Border, filled: false, width: 2f);
            return;
        }

        var transform = CreateTransform(_snapshot);
        for (var index = 0; index < _snapshot.StaticObstacles.Length; index++)
        {
            DrawPanelRect(
                transform.WorldRectToPanel(_snapshot.StaticObstacles[index]),
                Obstacle,
                filled: true);
        }
        for (var index = 0; index < _snapshot.Markers.Length; index++)
        {
            var marker = _snapshot.Markers[index];
            var center = ToGodot(transform.WorldToPanel(marker.Position));
            var color = marker.Kind == MinimapMarkerKind.Building
                ? Building
                : marker.Team == 1
                    ? Friendly
                    : Enemy;
            if (marker.Kind == MinimapMarkerKind.Building)
            {
                var halfSize = marker.Size * 0.5f;
                var panelRect = transform.WorldRectToPanel(
                    new SimRect(marker.Position - halfSize, marker.Position + halfSize));
                var visibleSize = NVector2.Max(panelRect.Size, new NVector2(4f));
                var visibleRect = new MinimapPanelRect(
                    panelRect.Position + (panelRect.Size - visibleSize) * 0.5f,
                    visibleSize);
                DrawPanelRect(visibleRect, color, filled: true);
                continue;
            }

            const float radius = 2.2f;
            DrawCircle(center, radius, color);
            if (marker.Selected)
            {
                DrawArc(center, radius + 2f, 0f, MathF.Tau, 12, Colors.White, 1f);
            }
        }

        DrawPanelRect(
            transform.WorldRectToPanel(_snapshot.VisibleWorld),
            Viewport,
            filled: false,
            width: 1.5f);
        DrawRect(new Rect2(Vector2.Zero, Size), Border, filled: false, width: 2f);
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (_snapshot is null)
        {
            return;
        }
        if (inputEvent is InputEventMouseButton mouse && mouse.Pressed &&
            mouse.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            EmitInteraction(
                mouse.Position,
                mouse.ButtonIndex == MouseButton.Left,
                mouse.ButtonIndex == MouseButton.Right);
            AcceptEvent();
        }
        else if (inputEvent is InputEventMouseMotion motion &&
                 (motion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            EmitInteraction(motion.Position, primary: true, secondary: false);
            AcceptEvent();
        }
    }

    private void EmitInteraction(Vector2 localPosition, bool primary, bool secondary)
    {
        if (_snapshot is null)
        {
            return;
        }
        var interaction = MinimapInteractionResolver.Resolve(
            CreateTransform(_snapshot),
            ToNumerics(localPosition),
            primary,
            secondary);
        if (interaction.Kind == MinimapInteractionKind.FocusCamera)
        {
            FocusRequested?.Invoke(interaction.WorldPosition);
        }
        else if (interaction.Kind == MinimapInteractionKind.SmartCommand)
        {
            SmartCommandRequested?.Invoke(interaction.WorldPosition);
        }
    }

    private MinimapTransform CreateTransform(MinimapSnapshot snapshot) =>
        new(
            snapshot.WorldBounds,
            new MinimapPanelRect(NVector2.Zero, ToNumerics(Size)));

    private void DrawPanelRect(
        MinimapPanelRect panel,
        Color color,
        bool filled,
        float width = -1f) =>
        DrawRect(
            new Rect2(ToGodot(panel.Position), ToGodot(panel.Size)),
            color,
            filled,
            width);

    private static NVector2 ToNumerics(Vector2 value) => new(value.X, value.Y);
    private static Vector2 ToGodot(NVector2 value) => new(value.X, value.Y);
}
