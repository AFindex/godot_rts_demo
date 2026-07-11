using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime;

/// <summary>Presentation-only economy panel driven by a pure C# snapshot.</summary>
public partial class RtsEconomyControl : Control
{
    private static readonly Color Background = new("101923ed");
    private static readonly Color Border = new("6aa6c8");
    private static readonly Color Minerals = new("65c9ff");
    private static readonly Color Gas = new("61db83");
    private EconomyOverviewSnapshot? _snapshot;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(720f, 180f);
    }

    public void SetSnapshot(EconomyOverviewSnapshot snapshot)
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
        var player = _snapshot.Player;
        DrawText(new Vector2(16f, 30f),
            $"Player {player.PlayerId} Economy", Colors.White, 18);
        DrawText(new Vector2(16f, 66f),
            $"MINERALS  {player.Minerals}", Minerals, 18);
        DrawText(new Vector2(230f, 66f),
            $"VESPENE  {player.VespeneGas}", Gas, 18);
        DrawText(new Vector2(445f, 66f),
            $"SUPPLY  {player.SupplyUsed}/{player.SupplyCapacity}",
            Colors.White, 18);
        DrawText(new Vector2(16f, 106f),
            $"workers {_snapshot.Workers}   gathering {_snapshot.Gathering}   " +
            $"returning {_snapshot.Returning}   waiting {_snapshot.Waiting}",
            Colors.White);
        var depleted = _snapshot.ResourceNodes.Count(node => node.Remaining == 0);
        var remaining = _snapshot.ResourceNodes.Sum(node => node.Remaining);
        DrawText(new Vector2(16f, 142f),
            $"nodes {_snapshot.ResourceNodes.Length}   depleted {depleted}   " +
            $"remaining {remaining}", Border);
    }

    private void DrawText(Vector2 position, string text, Color color, int size = 15) =>
        DrawString(ThemeDB.FallbackFont, position, text,
            HorizontalAlignment.Left, -1f, size, color);
}
