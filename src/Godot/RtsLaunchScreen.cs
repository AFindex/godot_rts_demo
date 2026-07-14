using Godot;
using RtsDemo.Presentation;

namespace RtsDemo.GodotRuntime;

/// <summary>Presentation-only launcher and test browser.</summary>
public partial class RtsLaunchScreen : Control
{
    private readonly List<TestShowcaseEntry> _entries = [];
    private TestShowcaseEntry? _selected;
    private VBoxContainer? _testList;
    private Label? _detailTitle;
    private Label? _detailCategory;
    private Label? _detailSummary;
    private Label? _status;
    private Button? _runButton;
    private LineEdit? _search;
    private OptionButton? _category;

    public event Action? DemoRequested;
    public event Action? Demo3DRequested;
    public event Action<string>? TestRequested;
    public event Action? TestBrowserRequested;

    public RtsLaunchScreen()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 500;
    }

    public void Initialize(IEnumerable<TestShowcaseEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        ShowHome();
    }

    public void ShowHome()
    {
        Visible = true;
        ClearPage();
        AddBackdrop();
        var center = FullRect<CenterContainer>();
        AddChild(center);
        var panel = Panel(new Vector2(760f, 555f));
        center.AddChild(panel);
        var margin = Margin(46);
        panel.AddChild(margin);
        var content = SpacedVBox(18);
        margin.AddChild(content);
        content.AddChild(Title("GODOT 4.7 · .NET RTS LAB", 34));
        content.AddChild(Text(
            "纯 C# 确定性 RTS 原型\n群体寻路 · 经济建造 · 战斗 · AI · 回放", 19,
            new Color("a9c9df")));
        content.AddChild(Spacer(14));
        content.AddChild(ActionButton(
            "进入大型 RTS 对局",
            "12 农民开局，对抗会采矿、扩张、攀科技和持续进攻的敌方 AI。",
            () => DemoRequested?.Invoke()));
        content.AddChild(ActionButton(
            "进入 3D RTS 遭遇战",
            "同一套纯 C# 模拟与 AI；用球体、方块和低多边形展示单位、建筑与资源。",
            () => Demo3DRequested?.Invoke()));
        content.AddChild(ActionButton(
            $"打开测试中心  ·  {_entries.Count} 项",
            "浏览每项黑盒测试的中文说明，并直接切换到对应场景。",
            () => ShowBrowser()));
        content.AddChild(Spacer(10));
        content.AddChild(Text(
            "测试中心运行的是与自动回归、录像完全相同的 VisualTestCatalog 场景。",
            14, new Color("7897ad")));
    }

    public void ShowBrowser(string status = "")
    {
        Visible = true;
        ClearPage();
        AddBackdrop();
        var margin = Margin(24);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(margin);
        var page = SpacedVBox(12);
        margin.AddChild(page);

        var header = SpacedHBox(12);
        page.AddChild(header);
        var back = SmallButton("← 返回启动页", ShowHome);
        header.AddChild(back);
        var heading = Title("测试中心", 28);
        heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(heading);
        header.AddChild(Text($"{_entries.Count} 个业务场景", 15,
            new Color("8db7cf")));

        var filters = SpacedHBox(10);
        page.AddChild(filters);
        _search = new LineEdit
        {
            PlaceholderText = "搜索中文说明或 case id…",
            CustomMinimumSize = new Vector2(440f, 42f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _search.TextChanged += _ => RebuildTestList();
        filters.AddChild(_search);
        _category = new OptionButton { CustomMinimumSize = new Vector2(220f, 42f) };
        foreach (var category in TestShowcaseCatalog.Categories(_entries))
            _category.AddItem(category);
        _category.ItemSelected += _ => RebuildTestList();
        filters.AddChild(_category);

        var body = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SplitOffsets = [550]
        };
        page.AddChild(body);
        var listPanel = Panel(new Vector2(520f, 500f));
        body.AddChild(listPanel);
        var listScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        listPanel.AddChild(listScroll);
        _testList = SpacedVBox(5);
        _testList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        listScroll.AddChild(_testList);

        var detailPanel = Panel(new Vector2(470f, 500f));
        body.AddChild(detailPanel);
        var detailMargin = Margin(26);
        detailPanel.AddChild(detailMargin);
        var detail = SpacedVBox(14);
        detailMargin.AddChild(detail);
        _detailCategory = Text("请选择测试", 14, new Color("62c4ff"));
        _detailTitle = Title("从左侧选择一个场景", 25);
        _detailSummary = Text(
            "选中后这里会显示这个测试验证的业务目标。", 18,
            new Color("c3d7e5"));
        _detailSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailSummary.CustomMinimumSize = new Vector2(380f, 150f);
        detail.AddChild(_detailCategory);
        detail.AddChild(_detailTitle);
        detail.AddChild(_detailSummary);
        detail.AddChild(Spacer(8));
        _runButton = PrimaryButton("运行这个测试", RunSelected);
        _runButton.Disabled = true;
        detail.AddChild(_runButton);
        _status = Text(status, 14, new Color("7ee0a3"));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detail.AddChild(_status);
        RebuildTestList();
    }

    public void ShowRunning(TestShowcaseEntry entry)
    {
        Visible = true;
        ClearPage();
        MouseFilter = MouseFilterEnum.Ignore;
        var toolbar = new PanelContainer
        {
            Position = new Vector2(0f, 0f),
            OffsetLeft = 14f,
            OffsetTop = 52f,
            CustomMinimumSize = new Vector2(390f, 78f),
            MouseFilter = MouseFilterEnum.Stop
        };
        toolbar.AddThemeStyleboxOverride("panel", Box(
            new Color("0b141de8"), new Color("5ba9d6"), 10));
        AddChild(toolbar);
        var row = SpacedHBox(12);
        toolbar.AddChild(row);
        var text = Text($"正在运行\n{entry.Title}", 14, new Color("e7f4ff"));
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(text);
        row.AddChild(SmallButton("返回测试中心", () =>
            TestBrowserRequested?.Invoke()));
    }

    public void HideScreen()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private void RebuildTestList()
    {
        if (_testList is null) return;
        foreach (var child in _testList.GetChildren()) child.QueueFree();
        var category = _category is null || _category.Selected < 0
            ? TestShowcaseCatalog.AllCategories
            : _category.GetItemText(_category.Selected);
        var filtered = TestShowcaseCatalog.Filter(
            _entries, _search?.Text, category);
        foreach (var entry in filtered)
        {
            var button = new Button
            {
                Text = $"{entry.Title}\n{entry.Id}  ·  {entry.Category}",
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(480f, 58f),
                TooltipText = entry.Summary
            };
            button.AddThemeFontSizeOverride("font_size", 14);
            button.Pressed += () => SelectEntry(entry);
            _testList.AddChild(button);
        }
        if (filtered.Length > 0 &&
            (_selected is null || !filtered.Any(value => value.Id == _selected.Id)))
            SelectEntry(filtered[0]);
        if (filtered.Length == 0)
        {
            _selected = null;
            _testList.AddChild(Text("没有匹配的测试。", 16, new Color("bd7f88")));
            UpdateDetails();
        }
    }

    private void SelectEntry(TestShowcaseEntry entry)
    {
        _selected = entry;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (_detailTitle is null || _detailCategory is null ||
            _detailSummary is null || _runButton is null)
            return;
        if (_selected is null)
        {
            _detailCategory.Text = "没有选择";
            _detailTitle.Text = "请选择测试";
            _detailSummary.Text = "修改搜索条件或分类后再选择。";
            _runButton.Disabled = true;
            return;
        }
        _detailCategory.Text = $"{_selected.Category}  ·  {_selected.Id}";
        _detailTitle.Text = _selected.Title;
        _detailSummary.Text = _selected.Summary;
        _runButton.Disabled = false;
    }

    private void RunSelected()
    {
        if (_selected is not null) TestRequested?.Invoke(_selected.Id);
    }

    private void ClearPage()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _selected = null;
        _testList = null;
        _detailTitle = null;
        _detailCategory = null;
        _detailSummary = null;
        _status = null;
        _runButton = null;
        _search = null;
        _category = null;
        foreach (var child in GetChildren()) child.QueueFree();
    }

    private void AddBackdrop()
    {
        var background = FullRect<ColorRect>();
        background.Color = new Color("081018f7");
        background.MouseFilter = MouseFilterEnum.Stop;
        AddChild(background);
    }

    private static T FullRect<T>() where T : Control, new()
    {
        var control = new T();
        control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        return control;
    }

    private static PanelContainer Panel(Vector2 minimum)
    {
        var panel = new PanelContainer { CustomMinimumSize = minimum };
        panel.AddThemeStyleboxOverride("panel", Box(
            new Color("101c28f2"), new Color("35566d"), 14));
        return panel;
    }

    private static MarginContainer Margin(int value)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", value);
        margin.AddThemeConstantOverride("margin_right", value);
        margin.AddThemeConstantOverride("margin_top", value);
        margin.AddThemeConstantOverride("margin_bottom", value);
        return margin;
    }

    private static Label Title(string value, int size)
    {
        var label = Text(value, size, new Color("edf8ff"));
        label.AddThemeColorOverride("font_shadow_color", new Color("000000aa"));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        return label;
    }

    private static Label Text(string value, int size, Color color)
    {
        var label = new Label { Text = value };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Control Spacer(float height) => new Control
    {
        CustomMinimumSize = new Vector2(1f, height)
    };

    private static VBoxContainer ActionButton(
        string title,
        string summary,
        Action action)
    {
        var group = SpacedVBox(4);
        group.AddChild(PrimaryButton(title, action));
        group.AddChild(Text(summary, 13, new Color("799bb0")));
        return group;
    }

    private static Button PrimaryButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(320f, 54f)
        };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += action;
        return button;
    }

    private static Button SmallButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(150f, 40f)
        };
        button.Pressed += action;
        return button;
    }

    private static VBoxContainer SpacedVBox(int separation)
    {
        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", separation);
        return container;
    }

    private static HBoxContainer SpacedHBox(int separation)
    {
        var container = new HBoxContainer();
        container.AddThemeConstantOverride("separation", separation);
        return container;
    }

    private static StyleBoxFlat Box(Color background, Color border, int radius) =>
        new()
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius
        };
}
