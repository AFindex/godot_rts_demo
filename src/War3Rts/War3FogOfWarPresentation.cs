using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;

namespace War3Rts;

/// <summary>
/// Uploads the authoritative simulation visibility grid as one small R8
/// texture. The terrain samples it in world space, avoiding fog cell Nodes or
/// one draw call per cell.
/// </summary>
internal sealed class War3FogOfWarPresentation : IDisposable
{
    private const ulong UploadIntervalMilliseconds = 80;
    private const byte HiddenLight = 12;
    private const byte ExploredLight = 82;
    private const byte VisibleLight = 255;

    private readonly RtsSimulation _simulation;
    private readonly int _playerId;
    private readonly byte[] _visibilityCells;
    private readonly byte[] _lightPixels;
    private readonly Image _image;
    private readonly ImageTexture _texture;
    private ulong _nextUploadAt;
    private bool _disposed;

    public War3FogOfWarPresentation(
        RtsSimulation simulation,
        int playerId,
        War3TerrainMaterialSet materials)
    {
        _simulation = simulation;
        _playerId = playerId;
        var visibility = simulation.Visibility;
        _visibilityCells = new byte[visibility.Columns * visibility.Rows];
        _lightPixels = new byte[_visibilityCells.Length];
        _image = Image.CreateFromData(
            visibility.Columns,
            visibility.Rows,
            false,
            Image.Format.R8,
            _lightPixels);
        _texture = ImageTexture.CreateFromImage(_image);

        var origin = SimPlane3DTransform.ToWorld(simulation.World.Bounds.Min);
        var textureWorldSize = new Vector2(
            SimPlane3DTransform.ToWorldLength(
                visibility.Columns * visibility.CellSize),
            SimPlane3DTransform.ToWorldLength(
                visibility.Rows * visibility.CellSize));
        materials.ConfigureFogOfWar(
            _texture,
            new Vector2(origin.X, origin.Z),
            textureWorldSize);
        Sync(force: true);
        GD.Print(
            $"WAR3_FOG_PRESENTATION enabled=true player={playerId} " +
            $"grid={visibility.Columns}x{visibility.Rows} " +
            $"interval_ms={UploadIntervalMilliseconds}");
    }

    public void Sync(bool force = false)
    {
        if (_disposed) return;
        var now = Time.GetTicksMsec();
        if (!force && now < _nextUploadAt) return;
        _nextUploadAt = now + UploadIntervalMilliseconds;
        _simulation.Visibility.CopyCells(_playerId, _visibilityCells);
        for (var index = 0; index < _visibilityCells.Length; index++)
        {
            _lightPixels[index] = (MapVisibility)_visibilityCells[index] switch
            {
                MapVisibility.Visible => VisibleLight,
                MapVisibility.Explored => ExploredLight,
                _ => HiddenLight
            };
        }
        _image.SetData(
            _simulation.Visibility.Columns,
            _simulation.Visibility.Rows,
            false,
            Image.Format.R8,
            _lightPixels);
        _texture.Update(_image);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texture.Dispose();
        _image.Dispose();
    }
}
