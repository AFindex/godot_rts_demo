using System.Numerics;

namespace RtsDemo.Simulation;

public enum MinimapMarkerKind : byte
{
    Unit,
    Building
}

public readonly record struct MinimapMarker(
    int Id,
    MinimapMarkerKind Kind,
    int Team,
    Vector2 Position,
    Vector2 Size,
    bool Selected = false);

public sealed class MinimapSnapshot
{
    public MinimapSnapshot(
        SimRect worldBounds,
        SimRect visibleWorld,
        SimRect[] staticObstacles,
        MinimapMarker[] markers)
    {
        WorldBounds = worldBounds;
        VisibleWorld = visibleWorld;
        StaticObstacles = staticObstacles;
        Markers = markers;
    }

    public SimRect WorldBounds { get; }
    public SimRect VisibleWorld { get; }
    public SimRect[] StaticObstacles { get; }
    public MinimapMarker[] Markers { get; }
}

public readonly record struct MinimapPanelRect(Vector2 Position, Vector2 Size)
{
    public bool Contains(Vector2 point) =>
        point.X >= Position.X && point.Y >= Position.Y &&
        point.X <= Position.X + Size.X && point.Y <= Position.Y + Size.Y;
}

public readonly struct MinimapTransform
{
    public MinimapTransform(SimRect worldBounds, MinimapPanelRect panel)
    {
        if (worldBounds.Width <= 0f || worldBounds.Height <= 0f ||
            panel.Size.X <= 0f || panel.Size.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(panel));
        }
        WorldBounds = worldBounds;
        Panel = panel;
    }

    public SimRect WorldBounds { get; }
    public MinimapPanelRect Panel { get; }

    public Vector2 WorldToPanel(Vector2 world)
    {
        var normalized = new Vector2(
            (world.X - WorldBounds.Min.X) / WorldBounds.Width,
            (world.Y - WorldBounds.Min.Y) / WorldBounds.Height);
        return Panel.Position + normalized * Panel.Size;
    }

    public Vector2 PanelToWorld(Vector2 panel)
    {
        var normalized = (panel - Panel.Position) / Panel.Size;
        normalized = Vector2.Clamp(normalized, Vector2.Zero, Vector2.One);
        return WorldBounds.Min + normalized *
            new Vector2(WorldBounds.Width, WorldBounds.Height);
    }

    public MinimapPanelRect WorldRectToPanel(SimRect world)
    {
        var minimum = WorldToPanel(world.Min);
        var maximum = WorldToPanel(world.Max);
        return new MinimapPanelRect(minimum, maximum - minimum);
    }
}

public enum MinimapInteractionKind : byte
{
    None,
    FocusCamera,
    SmartCommand
}

public readonly record struct MinimapInteraction(
    MinimapInteractionKind Kind,
    Vector2 WorldPosition);

public static class MinimapInteractionResolver
{
    public static MinimapInteraction Resolve(
        MinimapTransform transform,
        Vector2 panelPosition,
        bool primaryButton,
        bool secondaryButton)
    {
        if (!transform.Panel.Contains(panelPosition) ||
            (!primaryButton && !secondaryButton))
        {
            return default;
        }
        return new MinimapInteraction(
            secondaryButton
                ? MinimapInteractionKind.SmartCommand
                : MinimapInteractionKind.FocusCamera,
            transform.PanelToWorld(panelPosition));
    }
}
