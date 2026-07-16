using Godot;
using RtsDemo.Demos.War3;

namespace War3Rts;

/// <summary>
/// Full-screen, presentation-only map loading state. Progress is supplied by
/// the composition root after each real loading boundary completes.
/// </summary>
public sealed partial class War3MapLoadingOverlay : CanvasLayer
{
    private static readonly string[] Steps =
    [
        "读取地图包",
        "校验资源数据",
        "装载玩法目录",
        "构建地形与导航",
        "烘焙寻路净空",
        "初始化确定性模拟",
        "构建战场表现",
        "加载 HUD 与模型",
        "同步初始状态"
    ];

    private readonly Label[] _stepLabels = new Label[Steps.Length];
    private Label? _title;
    private Label? _stage;
    private Label? _detail;
    private Label? _percent;
    private TextureProgressBar? _progress;

    public bool InterfaceReady =>
        _title is not null && _stage is not null && _detail is not null &&
        _percent is not null && _progress is not null &&
        _stepLabels.All(label => label is not null);

    public void Initialize(string mapName)
    {
        EnsureInterface();
        _title!.Text = $"正在载入 · {mapName}";
        SetProgress(0, 0d, "准备读取版本化地图包与对象清单…");
    }

    public void SetProgress(int activeStep, double progress, string detail)
    {
        EnsureInterface();
        var normalized = Math.Clamp(progress, 0d, 1d);
        _progress!.Value = normalized * 100d;
        _percent!.Text = $"{normalized:P0}";
        _detail!.Text = detail;
        _stage!.Text = activeStep >= Steps.Length
            ? "战场已就绪"
            : $"第 {activeStep + 1}/{Steps.Length} 步 · " +
              Steps[Math.Clamp(activeStep, 0, Steps.Length - 1)];
        for (var index = 0; index < Steps.Length; index++)
        {
            var completed = index < activeStep;
            var current = index == activeStep && activeStep < Steps.Length;
            _stepLabels[index].Text =
                $"{(completed ? "✓" : current ? "›" : "·")}  {Steps[index]}";
            _stepLabels[index].AddThemeColorOverride(
                "font_color",
                current
                    ? new Color("f2d47a")
                    : completed
                        ? new Color("94b99b")
                        : new Color("74818b"));
        }
    }

    public void ShowFailure(string error)
    {
        EnsureInterface();
        _stage!.Text = "地图加载失败";
        _stage.AddThemeColorOverride("font_color", new Color("ef8b79"));
        _detail!.Text = error;
        _percent!.Text = "!";
    }

    private void EnsureInterface()
    {
        if (_title is not null) return;
        Layer = 250;
        var backdrop = new ColorRect
        {
            Name = "MapLoadingBackdrop",
            Color = new Color("071019f8"),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        var panel = new PanelContainer
        {
            Name = "MapLoadingPanel",
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -350f,
            OffsetTop = -220f,
            OffsetRight = 350f,
            OffsetBottom = 220f,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        panel.AddThemeStyleboxOverride("panel", Box(
            new Color("0d1721"), new Color("715827"), 6, 1));
        backdrop.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 36);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_right", 36);
        margin.AddThemeConstantOverride("margin_bottom", 26);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var kicker = LabelText("WARCRAFT III · BATTLEFIELD", 11,
            new Color("a98a49"));
        column.AddChild(kicker);
        _title = LabelText("正在载入战场", 26, new Color("f2ead5"));
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_title);

        var stageRow = new HBoxContainer();
        stageRow.AddThemeConstantOverride("separation", 12);
        column.AddChild(stageRow);
        _stage = LabelText("准备加载", 16, new Color("f2d47a"));
        _stage.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        stageRow.AddChild(_stage);
        _percent = LabelText("0%", 15, new Color("f2d47a"));
        _percent.CustomMinimumSize = new Vector2(58f, 24f);
        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        stageRow.AddChild(_percent);

        _progress = new TextureProgressBar
        {
            CustomMinimumSize = new Vector2(0f, 20f),
            MinValue = 0d,
            MaxValue = 100d,
            FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight,
            TextureProgress = War3RuntimeAssets.LoadTexture(
                @"UI\Feedback\BuildProgressBar\human-buildprogressbar-fill.blp"),
            TextureOver = War3RuntimeAssets.LoadTexture(
                @"UI\Feedback\BuildProgressBar\human-buildprogressbar-border.blp"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        column.AddChild(_progress);

        _detail = LabelText("", 13, new Color("aeb8be"));
        _detail.CustomMinimumSize = new Vector2(0f, 38f);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_detail);

        var divider = new HSeparator();
        divider.AddThemeStyleboxOverride("separator", Box(
            Colors.Transparent, new Color("725b2e88"), 0, 1));
        column.AddChild(divider);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 7);
        column.AddChild(grid);
        for (var index = 0; index < Steps.Length; index++)
        {
            var label = LabelText($"·  {Steps[index]}", 13,
                new Color("74818b"));
            label.CustomMinimumSize = new Vector2(286f, 22f);
            _stepLabels[index] = label;
            grid.AddChild(label);
        }

        var footer = LabelText(
            "正在建立地形、导航、模拟与美术表现的同一份权威状态",
            11, new Color("70808a"));
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(footer);
    }

    private static Label LabelText(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color("000000b0"));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private static StyleBoxFlat Box(
        Color background,
        Color border,
        int radius,
        int width) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = width,
        BorderWidthTop = width,
        BorderWidthRight = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius
    };
}
