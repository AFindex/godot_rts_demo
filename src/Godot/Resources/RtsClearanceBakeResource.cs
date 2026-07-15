using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsClearanceBakeResource : Resource
{
    [Export]
    public int FormatVersion { get; set; } = 2;

    [Export]
    public string SourceNavigationHash { get; set; } = string.Empty;

    [Export]
    public string SourceTerrainHash { get; set; } = string.Empty;

    [Export]
    public string BakeHash { get; set; } = string.Empty;

    [Export]
    public float CellSize { get; set; } = 16f;

    [Export]
    public int ChunkSizeCells { get; set; } = 16;

    [Export]
    public int Columns { get; set; }

    [Export]
    public int Rows { get; set; }

    [Export]
    public int PayloadBytes { get; set; }

    [Export(PropertyHint.MultilineText)]
    public string PayloadBase64 { get; set; } = string.Empty;
}
