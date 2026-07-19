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
    // The texture stores state, not final brightness. Keeping the three
    // authoritative values evenly separated lets terrain shaders reconstruct
    // stable visual bands without tone mapping collapsing Explored into
    // Hidden. Linear sampling still softens only the borders between states.
    private const byte HiddenState = 0;
    private const byte ExploredState = 128;
    private const byte VisibleState = 255;

    private readonly RtsSimulation _simulation;
    private readonly int _playerId;
    private readonly byte[] _visibilityCells;
    private readonly byte[] _statePixels;
    private readonly byte[] _minimapPixels;
    private readonly Image _stateImage;
    private readonly ImageTexture _stateTexture;
    private readonly Image _minimapImage;
    private readonly ImageTexture _minimapTexture;
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
        _statePixels = new byte[_visibilityCells.Length];
        _minimapPixels = new byte[_visibilityCells.Length * 4];
        _stateImage = Image.CreateFromData(
            visibility.Columns,
            visibility.Rows,
            false,
            Image.Format.R8,
            _statePixels);
        _stateTexture = ImageTexture.CreateFromImage(_stateImage);
        _minimapImage = Image.CreateFromData(
            visibility.Columns,
            visibility.Rows,
            false,
            Image.Format.Rgba8,
            _minimapPixels);
        _minimapTexture = ImageTexture.CreateFromImage(_minimapImage);

        var origin = SimPlane3DTransform.ToWorld(simulation.World.Bounds.Min);
        var textureWorldSize = new Vector2(
            SimPlane3DTransform.ToWorldLength(
                visibility.Columns * visibility.CellSize),
            SimPlane3DTransform.ToWorldLength(
                visibility.Rows * visibility.CellSize));
        materials.ConfigureFogOfWar(
            _stateTexture,
            new Vector2(origin.X, origin.Z),
            textureWorldSize);
        Sync(force: true);
        GD.Print(
            $"WAR3_FOG_PRESENTATION enabled=true player={playerId} " +
            $"grid={visibility.Columns}x{visibility.Rows} " +
            $"interval_ms={UploadIntervalMilliseconds}");
    }

    public Texture2D MinimapTexture => _minimapTexture;
    public int HiddenCellCount { get; private set; }
    public int ExploredCellCount { get; private set; }
    public int VisibleCellCount { get; private set; }
    public bool HasAllThreeStates =>
        HiddenCellCount > 0 && ExploredCellCount > 0 && VisibleCellCount > 0;

    public void Sync(bool force = false)
    {
        if (_disposed) return;
        var now = Time.GetTicksMsec();
        if (!force && now < _nextUploadAt) return;
        _nextUploadAt = now + UploadIntervalMilliseconds;
        _simulation.Visibility.CopyCells(_playerId, _visibilityCells);
        HiddenCellCount = 0;
        ExploredCellCount = 0;
        VisibleCellCount = 0;
        for (var index = 0; index < _visibilityCells.Length; index++)
        {
            var visibility = (MapVisibility)_visibilityCells[index];
            _statePixels[index] = visibility switch
            {
                MapVisibility.Visible => VisibleState,
                MapVisibility.Explored => ExploredState,
                _ => HiddenState
            };
            var pixel = index * 4;
            switch (visibility)
            {
                case MapVisibility.Visible:
                    VisibleCellCount++;
                    WriteMinimapPixel(pixel, 54, 82, 61);
                    break;
                case MapVisibility.Explored:
                    ExploredCellCount++;
                    WriteMinimapPixel(pixel, 23, 39, 31);
                    break;
                default:
                    HiddenCellCount++;
                    WriteMinimapPixel(pixel, 3, 7, 6);
                    break;
            }
        }
        _stateImage.SetData(
            _simulation.Visibility.Columns,
            _simulation.Visibility.Rows,
            false,
            Image.Format.R8,
            _statePixels);
        _stateTexture.Update(_stateImage);
        _minimapImage.SetData(
            _simulation.Visibility.Columns,
            _simulation.Visibility.Rows,
            false,
            Image.Format.Rgba8,
            _minimapPixels);
        _minimapTexture.Update(_minimapImage);
    }

    private void WriteMinimapPixel(
        int offset,
        byte red,
        byte green,
        byte blue)
    {
        _minimapPixels[offset] = red;
        _minimapPixels[offset + 1] = green;
        _minimapPixels[offset + 2] = blue;
        _minimapPixels[offset + 3] = byte.MaxValue;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _minimapTexture.Dispose();
        _minimapImage.Dispose();
        _stateTexture.Dispose();
        _stateImage.Dispose();
    }
}
