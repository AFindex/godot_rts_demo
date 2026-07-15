using Godot;

namespace RtsDemo.GodotRuntime.Resources;

/// <summary>
/// Godot inspector wrapper for the engine-independent visual tilepoint payload.
/// The payload remains canonical; duplicated metadata makes accidental terrain
/// or authoring-resource mismatches visible before a mesh is constructed.
/// </summary>
[GlobalClass]
public partial class RtsTerrainVisualLayerResource : Resource
{
    [Export]
    public int FormatVersion { get; set; } = 1;

    [Export]
    public string VisualHash { get; set; } = string.Empty;

    [Export]
    public string SourceTerrainHash { get; set; } = string.Empty;

    [Export]
    public int PointColumns { get; set; }

    [Export]
    public int PointRows { get; set; }

    [Export]
    public int PointCount { get; set; }

    [Export]
    public int PayloadBytes { get; set; }

    [Export(PropertyHint.MultilineText)]
    public string PayloadBase64 { get; set; } = string.Empty;
}
