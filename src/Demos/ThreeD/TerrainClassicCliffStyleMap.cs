using RtsDemo.Simulation;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Explicit presentation-only cliff texture identity per classic tilepoint.
/// This mirrors W3E's corner_cliff_texture field: a cliff model at (x, y)
/// reads the style stored at its bottom-left height sample. Ground material,
/// cliff height and cliff style remain independent authored fields.
/// </summary>
public sealed class TerrainClassicCliffStyleMap
{
    public const byte MaximumStyle = 14;
    private readonly byte[] _styles;

    private TerrainClassicCliffStyleMap(
        TerrainMapSnapshot terrain,
        byte[] styles)
    {
        SourceTerrainHash = terrain.StableHashText;
        Columns = terrain.Columns;
        Rows = terrain.Rows;
        _styles = styles;
    }

    public string SourceTerrainHash { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int Count => _styles.Length;

    public static TerrainClassicCliffStyleMap Uniform(
        TerrainMapSnapshot terrain,
        byte style)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ValidateStyle(style);
        return new TerrainClassicCliffStyleMap(
            terrain,
            Enumerable.Repeat(
                style, checked(terrain.Columns * terrain.Rows)).ToArray());
    }

    public static TerrainClassicCliffStyleMap FromTilepoints(
        TerrainMapSnapshot terrain,
        ReadOnlySpan<byte> styles)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var expected = checked(terrain.Columns * terrain.Rows);
        if (styles.Length != expected)
        {
            throw new ArgumentException(
                $"Classic cliff style map requires {expected} tilepoints, " +
                $"got {styles.Length}.", nameof(styles));
        }
        foreach (var style in styles)
            ValidateStyle(style);
        return new TerrainClassicCliffStyleMap(terrain, styles.ToArray());
    }

    public byte StyleAt(int column, int row)
    {
        if ((uint)column >= (uint)Columns || (uint)row >= (uint)Rows)
            throw new ArgumentOutOfRangeException(nameof(column));
        return _styles[row * Columns + column];
    }

    public bool Matches(TerrainMapSnapshot terrain) =>
        terrain.Columns == Columns &&
        terrain.Rows == Rows &&
        string.Equals(
            terrain.StableHashText,
            SourceTerrainHash,
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateStyle(byte style)
    {
        if (style > MaximumStyle)
            throw new ArgumentOutOfRangeException(nameof(style));
    }
}
