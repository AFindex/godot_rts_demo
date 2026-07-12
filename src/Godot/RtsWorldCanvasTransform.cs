using Godot;

namespace RtsDemo.GodotRuntime;

public readonly record struct RtsWorldCanvasTransform(
    Vector2 Offset,
    float Scale)
{
    public static RtsWorldCanvasTransform Identity { get; } =
        new(Vector2.Zero, 1f);

    public Vector2 Project(Vector2 worldPosition) =>
        Offset + worldPosition * Scale;

    public void Apply(Node2D layer)
    {
        layer.Position = Offset;
        layer.Scale = new Vector2(Scale, Scale);
    }
}
