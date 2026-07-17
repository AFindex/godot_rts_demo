using Godot;

namespace War3Rts;

public enum War3CursorRace : byte
{
    Human,
    Orc,
    Undead,
    NightElf
}

public enum War3CursorMode : byte
{
    Normal,
    Select,
    Target,
    TargetSelect,
    InvalidTarget,
    HoldItem,
    ScrollLeft,
    ScrollRight,
    ScrollUp,
    ScrollDown,
    ScrollUpLeft,
    ScrollUpRight,
    ScrollDownLeft,
    ScrollDownRight
}

public readonly record struct War3CursorFrame(
    int Column,
    int Row,
    int EighthTurns,
    Vector2 Hotspot);

/// <summary>
/// Describes the exported Warcraft cursor atlases. The MDX cursor sequences
/// animate UVs over a 32x32 grid; native operating-system cursors cannot play
/// MDX animations, so the same cells and timing are reproduced here.
/// </summary>
public static class War3CursorCatalog
{
    public const int AtlasWidth = 256;
    public const int AtlasHeight = 128;
    public const int FrameSize = 32;
    public const double FrameSeconds = 1d / 15d;

    public static IReadOnlyList<War3CursorRace> Races { get; } =
        Enum.GetValues<War3CursorRace>();

    public static string AtlasPath(War3CursorRace race) => race switch
    {
        War3CursorRace.Human => @"UI\Cursor\HumanCursor.blp",
        War3CursorRace.Orc => @"UI\Cursor\OrcCursor.blp",
        War3CursorRace.Undead => @"UI\Cursor\UndeadCursor.blp",
        War3CursorRace.NightElf => @"UI\Cursor\NightElfCursor.blp",
        _ => throw new ArgumentOutOfRangeException(nameof(race))
    };

    public static War3CursorRace ParseRace(
        IEnumerable<string> arguments,
        War3CursorRace fallback = War3CursorRace.Human)
    {
        const string prefix = "--war3-cursor-race=";
        var value = arguments.FirstOrDefault(argument => argument.StartsWith(
            prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null) return fallback;
        return TryParseRace(value[prefix.Length..], out var race)
            ? race
            : fallback;
    }

    public static bool TryParseRace(string value, out War3CursorRace race)
    {
        var normalized = value.Trim().Replace("-", string.Empty)
            .Replace("_", string.Empty);
        race = normalized.ToLowerInvariant() switch
        {
            "human" => War3CursorRace.Human,
            "orc" => War3CursorRace.Orc,
            "undead" => War3CursorRace.Undead,
            "nightelf" or "elf" => War3CursorRace.NightElf,
            _ => War3CursorRace.Human
        };
        return normalized.Equals("human", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("orc", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("undead", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("nightelf", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("elf", StringComparison.OrdinalIgnoreCase);
    }

    public static War3CursorFrame ResolveFrame(
        War3CursorMode mode,
        int animationStep)
    {
        animationStep = Math.Max(0, animationStep);
        return mode switch
        {
            War3CursorMode.Normal => Pointer(0, 3),
            War3CursorMode.Select => Pointer(animationStep % 8, 0),
            War3CursorMode.Target => Target(1, 3),
            War3CursorMode.TargetSelect => Target(animationStep % 8, 2),
            War3CursorMode.InvalidTarget => Target(2, 3),
            War3CursorMode.HoldItem => Pointer(3 + animationStep % 2, 3),
            War3CursorMode.ScrollRight => Scroll(animationStep, 0),
            War3CursorMode.ScrollDownRight => Scroll(animationStep, 1),
            War3CursorMode.ScrollDown => Scroll(animationStep, 2),
            War3CursorMode.ScrollDownLeft => Scroll(animationStep, 3),
            War3CursorMode.ScrollLeft => Scroll(animationStep, 4),
            War3CursorMode.ScrollUpLeft => Scroll(animationStep, 5),
            War3CursorMode.ScrollUp => Scroll(animationStep, 6),
            War3CursorMode.ScrollUpRight => Scroll(animationStep, 7),
            _ => Pointer(0, 3)
        };
    }

    private static War3CursorFrame Pointer(int column, int row) =>
        new(column, row, 0, new Vector2(3f, 3f));

    private static War3CursorFrame Target(int column, int row) =>
        new(column, row, 0, new Vector2(16f, 16f));

    private static War3CursorFrame Scroll(int animationStep, int eighthTurns) =>
        new(5 + animationStep % 3, 3, eighthTurns, new Vector2(16f, 16f));
}

/// <summary>Installs and animates the exported Warcraft cursors.</summary>
public sealed partial class War3CursorController : Node
{
    private static readonly Input.CursorShape[] InstalledShapes =
    [
        Input.CursorShape.Arrow,
        Input.CursorShape.PointingHand,
        Input.CursorShape.Cross,
        Input.CursorShape.Forbidden,
        Input.CursorShape.Drag,
        Input.CursorShape.CanDrop,
        Input.CursorShape.Move
    ];

    private readonly Dictionary<War3CursorRace, CursorTheme> _themes = [];
    private readonly Dictionary<Input.CursorShape, War3CursorFrame>
        _appliedFrames = [];
    private War3CursorRace _race = War3CursorRace.Human;
    private War3CursorMode _mode = War3CursorMode.Normal;
    private double _animationSeconds;
    private int _appliedStep = -1;
    private bool _initialized;

    public War3CursorRace Race => _race;
    public War3CursorMode Mode => _mode;
    public int LoadedRaceCount => _themes.Count;
    public bool IsReady => _initialized && _themes.ContainsKey(_race);

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    public bool Initialize(War3CursorRace race)
    {
        _themes.Clear();
        foreach (var candidate in War3CursorCatalog.Races)
        {
            if (TryLoadTheme(candidate, out var theme))
                _themes.Add(candidate, theme);
        }
        _initialized = _themes.Count > 0;
        if (!_initialized)
        {
            GD.PushWarning("WAR3_CURSOR load=failed reason=no_cursor_atlas");
            return false;
        }
        _race = _themes.ContainsKey(race) ? race : _themes.Keys.Min();
        _mode = War3CursorMode.Normal;
        _animationSeconds = 0d;
        _appliedStep = -1;
        _appliedFrames.Clear();
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        Apply(force: true);
        GD.Print(
            $"WAR3_CURSOR load=success race={_race} themes={_themes.Count} " +
            $"mode={_mode}");
        return true;
    }

    public void SetRace(War3CursorRace race)
    {
        if (!_themes.ContainsKey(race) || _race == race) return;
        _race = race;
        _appliedStep = -1;
        _appliedFrames.Clear();
        Apply(force: true);
    }

    public void SetMode(War3CursorMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _animationSeconds = 0d;
        _appliedStep = -1;
        Apply(force: true);
    }

    public override void _Process(double delta)
    {
        if (!IsReady) return;
        _animationSeconds += Math.Max(0d, delta);
        Apply(force: false);
    }

    public override void _ExitTree()
    {
        if (!_initialized) return;
        foreach (var shape in InstalledShapes)
            Input.SetCustomMouseCursor(null, shape, Vector2.Zero);
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
    }

    private void Apply(bool force)
    {
        if (!IsReady) return;
        var step = (int)(_animationSeconds / War3CursorCatalog.FrameSeconds);
        if (!force && step == _appliedStep) return;
        _appliedStep = step;
        var theme = _themes[_race];
        ApplyShape(theme, Input.CursorShape.Arrow, _mode, step);
        if (force)
        {
            ApplyShape(theme, Input.CursorShape.PointingHand,
                War3CursorMode.Select, step);
            ApplyShape(theme, Input.CursorShape.Cross,
                War3CursorMode.TargetSelect, step);
            ApplyShape(theme, Input.CursorShape.Forbidden,
                War3CursorMode.InvalidTarget, step);
            ApplyShape(theme, Input.CursorShape.Drag,
                War3CursorMode.HoldItem, step);
            ApplyShape(theme, Input.CursorShape.CanDrop,
                War3CursorMode.HoldItem, step);
            ApplyShape(theme, Input.CursorShape.Move,
                War3CursorMode.Target, step);
            return;
        }

        switch (Input.GetCurrentCursorShape())
        {
            case Input.CursorShape.PointingHand:
                ApplyShape(theme, Input.CursorShape.PointingHand,
                    War3CursorMode.Select, step);
                break;
            case Input.CursorShape.Cross:
                ApplyShape(theme, Input.CursorShape.Cross,
                    War3CursorMode.TargetSelect, step);
                break;
            case Input.CursorShape.Drag:
                ApplyShape(theme, Input.CursorShape.Drag,
                    War3CursorMode.HoldItem, step);
                break;
            case Input.CursorShape.CanDrop:
                ApplyShape(theme, Input.CursorShape.CanDrop,
                    War3CursorMode.HoldItem, step);
                break;
        }
    }

    private void ApplyShape(
        CursorTheme theme,
        Input.CursorShape shape,
        War3CursorMode mode,
        int step)
    {
        var frame = War3CursorCatalog.ResolveFrame(mode, step);
        if (_appliedFrames.GetValueOrDefault(shape) == frame) return;
        var texture = theme.Frame(frame);
        Input.SetCustomMouseCursor(texture, shape, frame.Hotspot);
        _appliedFrames[shape] = frame;
    }

    private static bool TryLoadTheme(
        War3CursorRace race,
        out CursorTheme theme)
    {
        theme = default!;
        var texture = War3RuntimeAssets.LoadTexture(
            War3CursorCatalog.AtlasPath(race));
        var atlas = texture?.GetImage();
        if (atlas is null || atlas.IsEmpty() ||
            atlas.GetWidth() != War3CursorCatalog.AtlasWidth ||
            atlas.GetHeight() != War3CursorCatalog.AtlasHeight)
        {
            GD.PushWarning(
                $"WAR3_CURSOR_THEME race={race} load=failed path=" +
                War3CursorCatalog.AtlasPath(race));
            return false;
        }

        var frames = new ImageTexture[4, 8];
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 8; column++)
        {
            var region = atlas.GetRegion(new Rect2I(
                column * War3CursorCatalog.FrameSize,
                row * War3CursorCatalog.FrameSize,
                War3CursorCatalog.FrameSize,
                War3CursorCatalog.FrameSize));
            frames[row, column] = ImageTexture.CreateFromImage(region);
        }

        var rotatedScroll = new ImageTexture[8, 3];
        for (var turns = 0; turns < 8; turns++)
        for (var frame = 0; frame < 3; frame++)
        {
            var source = atlas.GetRegion(new Rect2I(
                (5 + frame) * War3CursorCatalog.FrameSize,
                3 * War3CursorCatalog.FrameSize,
                War3CursorCatalog.FrameSize,
                War3CursorCatalog.FrameSize));
            var image = RotateCursorImage(source, turns);
            rotatedScroll[turns, frame] = ImageTexture.CreateFromImage(image);
        }

        theme = new CursorTheme(frames, rotatedScroll);
        return true;
    }

    private static Image RotateCursorImage(Image source, int eighthTurns)
    {
        eighthTurns = ((eighthTurns % 8) + 8) % 8;
        if (eighthTurns == 0) return source;
        var size = War3CursorCatalog.FrameSize;
        var result = Image.CreateEmpty(size, size, false, source.GetFormat());
        result.Fill(new Color(0f, 0f, 0f, 0f));
        var radians = eighthTurns * MathF.PI / 4f;
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        var center = (size - 1) * 0.5f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = x - center;
            var dy = y - center;
            var sourceX = (int)MathF.Round(cosine * dx + sine * dy + center);
            var sourceY = (int)MathF.Round(-sine * dx + cosine * dy + center);
            if ((uint)sourceX >= (uint)size || (uint)sourceY >= (uint)size)
                continue;
            result.SetPixel(x, y, source.GetPixel(sourceX, sourceY));
        }
        return result;
    }

    private sealed record CursorTheme(
        ImageTexture[,] Frames,
        ImageTexture[,] RotatedScroll)
    {
        public ImageTexture Frame(in War3CursorFrame frame) =>
            frame.Column >= 5 && frame.Row == 3
                ? RotatedScroll[frame.EighthTurns, frame.Column - 5]
                : Frames[frame.Row, frame.Column];
    }
}
