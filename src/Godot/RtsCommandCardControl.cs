using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime;

public partial class RtsCommandCardControl : Control
{
    private const float HeaderHeight = 46f;
    private const float ButtonHeight = 42f;
    private const float Gap = 6f;
    private CommandCardSnapshot _snapshot = CommandCardSnapshot.Empty;

    public event Action<CommandCardActionSnapshot>? ActionRequested;
    public event Action<int>? SubgroupCycleRequested;

    public RtsCommandCardControl()
    {
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(520f, 190f);
    }

    public void SetSnapshot(CommandCardSnapshot snapshot)
    {
        _snapshot = snapshot;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawStyleBox(
            GetThemeStylebox("panel", "Panel"),
            new Rect2(Vector2.Zero, Size));
        DrawString(
            ThemeDB.FallbackFont, new Vector2(12f, 20f),
            _snapshot.Title, HorizontalAlignment.Left, Size.X - 24f, 14,
            new Color("e8f3ff"));
        DrawString(
            ThemeDB.FallbackFont, new Vector2(12f, 39f),
            _snapshot.SubgroupLabels.Length == 0
                ? "No subgroup"
                : string.Join("   ", _snapshot.SubgroupLabels.Select(
                    (value, index) => index == _snapshot.ActiveSubgroupIndex
                        ? $"[{value}]"
                        : value)),
            HorizontalAlignment.Left, Size.X - 24f, 11,
            new Color("8fb8d8"));
        for (var index = 0; index < _snapshot.Actions.Length; index++)
        {
            var rect = ActionRect(index);
            var action = _snapshot.Actions[index];
            DrawRect(rect, action.Enabled
                ? new Color("244b68")
                : new Color("29313a"), true);
            DrawRect(rect, new Color("6286a0"), false, 1f);
            DrawString(
                ThemeDB.FallbackFont, rect.Position + new Vector2(8f, 17f),
                action.Label, HorizontalAlignment.Left, rect.Size.X - 16f, 11,
                action.Enabled ? new Color("f0f7ff") : new Color("7b8791"));
            if (!string.IsNullOrWhiteSpace(action.Status))
            {
                DrawString(
                    ThemeDB.FallbackFont, rect.Position + new Vector2(8f, 34f),
                    action.Status, HorizontalAlignment.Left, rect.Size.X - 16f, 9,
                    new Color("a9bed0"));
            }
        }
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton mouse || !mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Left)
            return;
        if (mouse.Position.Y < HeaderHeight)
        {
            SubgroupCycleRequested?.Invoke(mouse.ShiftPressed ? -1 : 1);
            AcceptEvent();
            return;
        }
        for (var index = 0; index < _snapshot.Actions.Length; index++)
        {
            if (ActionRect(index).HasPoint(mouse.Position) &&
                _snapshot.Actions[index].Enabled)
            {
                ActionRequested?.Invoke(_snapshot.Actions[index]);
                AcceptEvent();
                return;
            }
        }
    }

    private Rect2 ActionRect(int index)
    {
        const int columns = 3;
        var width = (Size.X - Gap * (columns + 1)) / columns;
        var column = index % columns;
        var row = index / columns;
        return new Rect2(
            Gap + column * (width + Gap),
            HeaderHeight + Gap + row * (ButtonHeight + Gap),
            width, ButtonHeight);
    }
}
