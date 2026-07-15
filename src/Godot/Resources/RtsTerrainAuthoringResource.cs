using Godot;

namespace RtsDemo.GodotRuntime.Resources;

[GlobalClass]
public partial class RtsTerrainAuthoringResource : Resource
{
    public const int CurrentFormatVersion = 1;

    [Export]
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    [Export]
    public Rect2 WorldBounds { get; set; } = new(0f, 0f, 640f, 384f);

    [Export(PropertyHint.Range, "4,256,1,or_greater")]
    public float CellSize { get; set; } = 32f;

    [Export(PropertyHint.Range, "4,256,1,or_greater")]
    public float CliffLevelHeight { get; set; } = 48f;

    [Export]
    public int DataColumns { get; set; }

    [Export]
    public int DataRows { get; set; }

    [Export]
    public Godot.Collections.Array<RtsTerrainSurfaceAuthoringResource> Surfaces
        { get; set; } = [];

    [Export]
    public byte[] CliffLevels { get; set; } = [];

    [Export]
    public int[] SurfaceIds { get; set; } = [];

    [Export]
    public byte[] PathingMasks { get; set; } = [];

    [Export]
    public byte[] CellFlags { get; set; } = [];

    [Export]
    public byte[] RampDirections { get; set; } = [];

    [Export]
    public Godot.Collections.Array<RtsTerrainAnchorAuthoringResource> Anchors
        { get; set; } = [];

    [Export(PropertyHint.Range, "0,1024,1")]
    public int MinimumGroundIslandCells { get; set; } = 4;

    [Export(PropertyHint.Range, "0,128,0.25")]
    public float MinimumGroundRadius { get; set; } = 8f;

    [Export(PropertyHint.SaveFile, "*.tres")]
    public string RuntimeOutputPath { get; set; } =
        "res://data/authored_terrain.tres";

    public int Columns => CellSize > 0f
        ? Math.Max(1, (int)MathF.Ceiling(WorldBounds.Size.X / CellSize))
        : 0;

    public int Rows => CellSize > 0f
        ? Math.Max(1, (int)MathF.Ceiling(WorldBounds.Size.Y / CellSize))
        : 0;

    public int CellCount => Columns * Rows;

    public string CaptureCellPayloadBase64()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(CellCount);
        for (var index = 0; index < CellCount; index++)
        {
            writer.Write(CliffLevels[index]);
            writer.Write(SurfaceIds[index]);
            writer.Write(PathingMasks[index]);
            writer.Write(CellFlags[index]);
            writer.Write(RampDirections[index]);
        }
        return Convert.ToBase64String(stream.ToArray());
    }

    public void RestoreCellPayloadBase64(string payloadBase64)
    {
        var bytes = Convert.FromBase64String(payloadBase64);
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        var count = reader.ReadInt32();
        if (count != CellCount)
            throw new InvalidDataException(
                $"Expected {CellCount} authoring cells, got {count}.");
        var levels = new byte[count];
        var surfaces = new int[count];
        var pathing = new byte[count];
        var flags = new byte[count];
        var ramps = new byte[count];
        for (var index = 0; index < count; index++)
        {
            levels[index] = reader.ReadByte();
            surfaces[index] = reader.ReadInt32();
            pathing[index] = reader.ReadByte();
            flags[index] = reader.ReadByte();
            ramps[index] = reader.ReadByte();
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Authoring cell payload has trailing data.");
        CliffLevels = levels;
        SurfaceIds = surfaces;
        PathingMasks = pathing;
        CellFlags = flags;
        RampDirections = ramps;
        EmitChanged();
    }
}
