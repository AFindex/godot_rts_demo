using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsTerrainMapResource : Resource
{
    [Export]
    public int FormatVersion { get; set; } = 1;

    [Export]
    public string TerrainHash { get; set; } = string.Empty;

    [Export]
    public Rect2 WorldBounds { get; set; }

    [Export]
    public float CellSize { get; set; } = 40f;

    [Export]
    public float CliffLevelHeight { get; set; } = 48f;

    [Export]
    public int Columns { get; set; }

    [Export]
    public int Rows { get; set; }

    [Export]
    public int SurfaceCount { get; set; }

    [Export]
    public int PayloadBytes { get; set; }

    [Export(PropertyHint.MultilineText)]
    public string PayloadBase64 { get; set; } = string.Empty;
}
