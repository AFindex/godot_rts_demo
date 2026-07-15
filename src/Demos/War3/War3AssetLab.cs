using System.Text.Json;
using Godot;

namespace RtsDemo.Demos.War3;

/// <summary>
/// Runtime browser for the extracted Warcraft III art pack. The asset pack is
/// kept outside Godot's importer and loaded directly so opening the project
/// does not create a second multi-gigabyte import cache.
/// </summary>
public sealed partial class War3AssetLab : Node3D
{
    private const float WarcraftUnitScale = 0.01f;
    private const float TopHeight = 58f;
    private const float LeftWidth = 314f;
    private const float RightWidth = 440f;
    private const float TimelineHeight = 178f;

    private static readonly Color Background = new("0a0d11");
    private static readonly Color Panel = new("11161d");
    private static readonly Color Raised = new("171d25");
    private static readonly Color Border = new("29323c");
    private static readonly Color Accent = new("c99b4d");
    private static readonly Color Text = new("edf0f3");
    private static readonly Color Muted = new("8f9aa7");
    private static readonly Color Success = new("65c99a");

    private readonly List<War3AssetEntry> _catalog = [];
    private readonly List<War3AssetEntry> _visibleEntries = [];
    private readonly List<Button> _categoryButtons = [];
    private readonly List<Button> _raceButtons = [];
    private readonly List<Button> _sequenceButtons = [];
    private readonly Dictionary<string, Texture2D?> _iconCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<Node3D>> _geosetNodes = [];
    private readonly Dictionary<int, List<Node3D>> _portraitGeosetNodes = [];
    private Node3D? _modelHost;
    private Node? _loadedModel;
    private AnimationPlayer? _animationPlayer;
    private War3EffectRuntime? _effects;
    private War3ModelMetadata? _metadata;
    private War3AssetEntry? _currentEntry;
    private Camera3D? _camera;
    private MeshInstance3D? _ground;
    private MeshInstance3D? _grid;
    private ItemList? _assetList;
    private LineEdit? _search;
    private Label? _catalogCount;
    private Label? _title;
    private Label? _path;
    private Label? _classification;
    private Label? _modelStats;
    private Label? _effectStats;
    private Label? _effectNote;
    private Label? _timeText;
    private Label? _status;
    private HBoxContainer? _sequenceRow;
    private HSlider? _timeline;
    private Button? _playButton;
    private Button? _effectToggle;
    private Button? _gridToggle;
    private Button? _axesToggle;
    private Node3D? _originAxes;
    private VBoxContainer? _portraitSection;
    private Label? _portraitHeading;
    private Label? _portraitCameraLabel;
    private Control? _portraitFrame;
    private SubViewportContainer? _portraitOpening;
    private SubViewport? _portraitViewport;
    private Node3D? _portraitWorld;
    private Camera3D? _portraitCamera;
    private TextureRect? _portraitMask;
    private ColorRect? _portraitHealth;
    private ColorRect? _portraitMana;
    private HBoxContainer? _portraitControls;
    private Button? _portraitIdleButton;
    private Button? _portraitTalkButton;
    private Label? _portraitSequenceLabel;
    private Node? _portraitModel;
    private AnimationPlayer? _portraitAnimationPlayer;
    private War3ModelMetadata? _portraitMetadata;
    private War3CameraDefinition? _portraitCameraDefinition;
    private int _portraitSequenceIndex;
    private War3AssetCategory? _category;
    private string _race = "all";
    private int _sequenceIndex;
    private double _localMilliseconds;
    private double _playbackSpeed = 1d;
    private bool _playing = true;
    private bool _effectsVisible = true;
    private bool _gridVisible = true;
    private bool _axesVisible = true;
    private bool _updatingTimeline;
    private bool _orbiting;
    private bool _panning;
    private float _yaw = -0.62f;
    private float _pitch = -0.18f;
    private float _distance = 3.2f;
    private Vector3 _cameraTarget = new(0f, 0.7f, 0f);

    public override void _Ready()
    {
        // AnimationPlayer evaluates imported glTF tracks at the default
        // priority. Run the Warcraft-only visibility correction afterwards.
        ProcessPriority = 100;
        CreateWorld();
        CreateInterface();
        LoadCatalog();
        SelectDefaultAsset();
        if (OS.GetCmdlineUserArgs().Contains("--war3-assets-smoke") ||
            OS.GetCmdlineUserArgs().Contains("--war3-assets-capture") ||
            OS.GetCmdlineUserArgs().Contains("--war3-unit-capture") ||
            OS.GetCmdlineUserArgs().Contains("--war3-model-capture"))
            _ = RunAutomationAsync();
    }

    public override void _Process(double delta)
    {
        if (_metadata is null || _metadata.Sequences.Count == 0) return;
        var sequence = _metadata.Sequences[_sequenceIndex];
        if (_playing)
        {
            if (_animationPlayer is not null &&
                _animationPlayer.CurrentAnimation.ToString().Length > 0)
            {
                _localMilliseconds = _animationPlayer.CurrentAnimationPosition * 1000d;
            }
            else
            {
                _localMilliseconds += delta * 1000d * _playbackSpeed;
                if (_localMilliseconds > sequence.DurationMilliseconds)
                {
                    _localMilliseconds = sequence.NonLooping
                        ? sequence.DurationMilliseconds
                        : _localMilliseconds % sequence.DurationMilliseconds;
                    if (sequence.NonLooping) SetPlaying(false);
                }
            }
            _effects?.Sync(_localMilliseconds);
        }

        if (_timeline is not null && !_updatingTimeline)
            _timeline.SetValueNoSignal(_localMilliseconds);
        if (_timeText is not null)
            _timeText.Text = $"{FormatTime(_localMilliseconds)} / {FormatTime(sequence.DurationMilliseconds)}";
        ApplyGeosetVisibility(
            _geosetNodes, _metadata, _sequenceIndex, _localMilliseconds);
        if (_portraitMetadata is not null && _portraitMetadata.Sequences.Count > 0)
        {
            var portraitMilliseconds = (_portraitAnimationPlayer?.CurrentAnimationPosition ?? 0d) * 1000d;
            ApplyGeosetVisibility(
                _portraitGeosetNodes,
                _portraitMetadata,
                _portraitSequenceIndex,
                portraitMilliseconds);
        }
        UpdateLiveEffectStats();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mouse when mouse.ButtonIndex == MouseButton.Left:
                if (mouse.Pressed && IsInsideModelViewport(mouse.Position))
                {
                    _orbiting = true;
                    GetViewport().SetInputAsHandled();
                }
                else if (!mouse.Pressed)
                {
                    _orbiting = false;
                }
                break;
            case InputEventMouseButton mouse when mouse.ButtonIndex == MouseButton.Middle:
                if (mouse.Pressed && IsInsideModelViewport(mouse.Position))
                {
                    _panning = true;
                    GetViewport().SetInputAsHandled();
                }
                else if (!mouse.Pressed)
                {
                    _panning = false;
                }
                break;
            case InputEventMouseButton mouse when mouse.Pressed &&
                                                      mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown &&
                                                      IsInsideModelViewport(mouse.Position):
                _distance *= mouse.ButtonIndex == MouseButton.WheelUp ? 0.88f : 1.14f;
                _distance = Math.Clamp(_distance, 0.25f, 180f);
                UpdateCamera();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when _panning:
                PanCamera(motion.Relative);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when _orbiting:
                _yaw -= motion.Relative.X * 0.008f;
                _pitch = Math.Clamp(_pitch - motion.Relative.Y * 0.006f, -1.2f, 1.18f);
                UpdateCamera();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo:
                if (key.Keycode == Key.Space)
                {
                    SetPlaying(!_playing);
                    GetViewport().SetInputAsHandled();
                }
                else if (key.Keycode == Key.F)
                {
                    FrameCurrentAsset();
                    GetViewport().SetInputAsHandled();
                }
                break;
        }
    }

    private void CreateWorld()
    {
        RenderingServer.SetDefaultClearColor(Background);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Background,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("8fa0b2"),
            AmbientLightEnergy = 0.72f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Filmic
        };
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = environment
        });
        AddChild(new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-54f, -28f, 0f),
            LightColor = new Color("fff0d2"),
            LightEnergy = 1.45f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 80f
        });
        AddChild(new DirectionalLight3D
        {
            Name = "FillLight",
            RotationDegrees = new Vector3(-24f, 145f, 0f),
            LightColor = new Color("8fb8dd"),
            LightEnergy = 0.62f,
            ShadowEnabled = false
        });

        _camera = new Camera3D
        {
            Name = "AssetCamera",
            Current = true,
            Fov = 42f,
            Near = 0.01f,
            Far = 500f
        };
        AddChild(_camera);
        UpdateCamera();

        _modelHost = new Node3D { Name = "ModelHost" };
        AddChild(_modelHost);
        _originAxes = CreateOriginAxes();
        _originAxes.Visible = false;
        _modelHost.AddChild(_originAxes);
        _ground = CreateGround();
        AddChild(_ground);
        _grid = CreateGrid();
        AddChild(_grid);
    }

    private void CreateInterface()
    {
        var layer = new CanvasLayer { Name = "AssetLabUI" };
        AddChild(layer);
        var root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);
        root.AddChild(CreateTopBar());
        root.AddChild(CreateCatalogPanel());
        root.AddChild(CreateInspectorPanel());
        root.AddChild(CreateTimelinePanel());
        root.AddChild(CreateViewportHint());
    }

    private Control CreateTopBar()
    {
        var panel = NewPanel("TopBar", Raised);
        SetRect(panel, 0f, 0f, 1f, 0f, 0f, 0f, 0f, TopHeight);
        var margin = NewMargin(20, 14, 18, 12);
        panel.AddChild(margin);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        margin.AddChild(row);
        var brand = NewLabel("WARCRAFT III  ·  ASSET LAB", 17, Text);
        brand.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(brand);
        var pack = NewBadge("CLASSIC EXPORT", Accent);
        row.AddChild(pack);
        _status = NewBadge("正在读取资产…", Muted);
        row.AddChild(_status);
        return panel;
    }

    private Control CreateCatalogPanel()
    {
        var panel = NewPanel("CatalogPanel", Panel);
        SetRect(panel, 0f, 0f, 0f, 1f, 0f, TopHeight, LeftWidth, 0f);
        var margin = NewMargin(14, 16, 14, 16);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 11);
        margin.AddChild(column);

        var eyebrow = NewLabel("资源目录", 12, Accent);
        column.AddChild(eyebrow);
        _search = new LineEdit
        {
            PlaceholderText = "搜索中文名、ID 或原始路径",
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(0f, 38f)
        };
        StyleLineEdit(_search);
        _search.TextChanged += _ => RefreshCatalog();
        column.AddChild(_search);

        var filters = new HFlowContainer();
        filters.AddThemeConstantOverride("h_separation", 6);
        filters.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(filters);
        AddCategoryButton(filters, "全部", null);
        foreach (var category in Enum.GetValues<War3AssetCategory>())
            AddCategoryButton(filters, War3AssetPack.CategoryLabel(category), category);

        column.AddChild(NewLabel("种族", 11, Muted));
        var raceScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 44f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        var races = new HBoxContainer();
        races.AddThemeConstantOverride("separation", 6);
        raceScroll.AddChild(races);
        column.AddChild(raceScroll);
        AddRaceButton(races, "全部", "all");
        foreach (var race in new[]
                 {
                     "human", "orc", "undead", "nightelf", "neutral",
                     "creeps", "naga", "demon", "critters", "other"
                 })
            AddRaceButton(races, RaceLabel(race), race);

        _catalogCount = NewLabel("—", 12, Muted);
        column.AddChild(_catalogCount);
        _assetList = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true,
            SameColumnWidth = true,
            IconMode = ItemList.IconModeEnum.Left,
            FixedIconSize = new Vector2I(38, 38)
        };
        StyleItemList(_assetList);
        _assetList.ItemSelected += index => LoadAsset(_visibleEntries[(int)index]);
        column.AddChild(_assetList);
        return panel;
    }

    private Control CreateInspectorPanel()
    {
        var panel = NewPanel("InspectorPanel", Panel);
        SetRect(panel, 1f, 0f, 1f, 1f, -RightWidth, TopHeight, 0f, 0f);
        var margin = NewMargin(18, 18, 18, 16);
        panel.AddChild(margin);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        margin.AddChild(scroll);
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        column.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(column);
        column.AddChild(NewLabel("当前资产", 12, Accent));
        _title = NewLabel("尚未选择", 24, Text);
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_title);
        _path = NewLabel("—", 12, Muted);
        _path.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_path);
        column.AddChild(NewDivider());
        _classification = NewLabel("分类  —", 13, Text);
        column.AddChild(_classification);
        _modelStats = NewLabel("模型  —", 13, Text);
        column.AddChild(_modelStats);
        column.AddChild(NewDivider());
        _portraitSection = CreatePortraitSection();
        column.AddChild(_portraitSection);
        column.AddChild(NewDivider());
        column.AddChild(NewLabel("运行时特效", 12, Accent));
        _effectStats = NewLabel("PE2 —  ·  Ribbon —", 15, Text);
        column.AddChild(_effectStats);
        _effectNote = NewLabel("选择带粒子或 Ribbon 的模型进行验证。", 12, Muted);
        _effectNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_effectNote);

        var toggles = new HBoxContainer();
        toggles.AddThemeConstantOverride("separation", 8);
        _effectToggle = NewButton("特效：开", 110f);
        _effectToggle.Pressed += ToggleEffects;
        toggles.AddChild(_effectToggle);
        _gridToggle = NewButton("网格：开", 110f);
        _gridToggle.Pressed += ToggleGrid;
        toggles.AddChild(_gridToggle);
        column.AddChild(toggles);

        var frameButton = NewButton("F  ·  镜头归位", 0f);
        frameButton.Pressed += FrameCurrentAsset;
        column.AddChild(frameButton);
        var help = NewLabel(
            "视口操作\n左键拖动：环绕    中键拖动：平移\n滚轮：缩放    F：镜头归位    Space：播放 / 暂停",
            12,
            Muted);
        help.AddThemeConstantOverride("line_spacing", 4);
        column.AddChild(help);
        return panel;
    }

    private VBoxContainer CreatePortraitSection()
    {
        var section = new VBoxContainer { Visible = false };
        section.AddThemeConstantOverride("separation", 9);
        var header = new HBoxContainer();
        _portraitHeading = NewLabel("关联单位肖像", 12, Accent);
        _portraitHeading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(_portraitHeading);
        _portraitCameraLabel = NewLabel("PORTRAIT CAMERA", 10, Muted);
        header.AddChild(_portraitCameraLabel);
        section.AddChild(header);

        _portraitFrame = new Control
        {
            CustomMinimumSize = new Vector2(356f, 438f),
            ClipContents = true,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        section.AddChild(_portraitFrame);
        var backdrop = new ColorRect
        {
            Color = new Color("050709"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _portraitFrame.AddChild(backdrop);

        _portraitOpening = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        SetPortraitOpeningRect(building: false);
        _portraitFrame.AddChild(_portraitOpening);
        _portraitViewport = new SubViewport
        {
            Size = new Vector2I(512, 551),
            OwnWorld3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false
        };
        _portraitOpening.AddChild(_portraitViewport);
        _portraitWorld = new Node3D { Name = "PortraitWorld" };
        _portraitViewport.AddChild(_portraitWorld);
        _portraitWorld.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("050709"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("9aabba"),
                AmbientLightEnergy = 0.82f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        });
        _portraitWorld.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-38f, -30f, 0f),
            LightColor = new Color("fff0d2"),
            LightEnergy = 1.7f,
            ShadowEnabled = false
        });
        _portraitWorld.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-15f, 145f, 0f),
            LightColor = new Color("87aed4"),
            LightEnergy = 0.65f,
            ShadowEnabled = false
        });
        _portraitCamera = new Camera3D
        {
            Current = true,
            Fov = 45f,
            Near = 0.005f,
            Far = 500f
        };
        _portraitWorld.AddChild(_portraitCamera);

        _portraitHealth = new ColorRect
        {
            Color = new Color("3e7d35"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        SetRect(_portraitHealth, 0f, 0f, 0f, 0f, 63f, 350f, 291f, 372f);
        _portraitFrame.AddChild(_portraitHealth);
        _portraitMana = new ColorRect
        {
            Color = new Color("2e6594"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        SetRect(_portraitMana, 0f, 0f, 0f, 0f, 63f, 394f, 256f, 416f);
        _portraitFrame.AddChild(_portraitMana);

        _portraitMask = new TextureRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        _portraitMask.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _portraitFrame.AddChild(_portraitMask);

        _portraitControls = new HBoxContainer();
        _portraitControls.AddThemeConstantOverride("separation", 8);
        _portraitIdleButton = NewButton("待机", 0f);
        _portraitIdleButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _portraitIdleButton.Pressed += () => SelectPortraitMode(talking: false);
        _portraitControls.AddChild(_portraitIdleButton);
        _portraitTalkButton = NewButton("说话", 0f);
        _portraitTalkButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _portraitTalkButton.Pressed += () => SelectPortraitMode(talking: true);
        _portraitControls.AddChild(_portraitTalkButton);
        section.AddChild(_portraitControls);
        _portraitSequenceLabel = NewLabel("—", 10, Muted);
        _portraitSequenceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        section.AddChild(_portraitSequenceLabel);
        return section;
    }

    private Control CreateTimelinePanel()
    {
        var panel = NewPanel("TimelinePanel", Raised);
        SetRect(panel, 0f, 1f, 1f, 1f, LeftWidth, -TimelineHeight, -RightWidth, 0f);
        var margin = NewMargin(18, 14, 18, 12);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 9);
        margin.AddChild(column);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        header.AddChild(NewLabel("动画片段", 12, Accent));
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(spacer);
        _timeText = NewLabel("00:00.000 / 00:00.000", 12, Muted);
        header.AddChild(_timeText);
        column.AddChild(header);
        var animationScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 45f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _sequenceRow = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, 30f)
        };
        _sequenceRow.AddThemeConstantOverride("separation", 6);
        animationScroll.AddChild(_sequenceRow);
        column.AddChild(animationScroll);
        var transport = new HBoxContainer();
        transport.AddThemeConstantOverride("separation", 10);
        _playButton = NewButton("暂停", 72f);
        _playButton.Pressed += () => SetPlaying(!_playing);
        transport.AddChild(_playButton);
        _timeline = new HSlider
        {
            MinValue = 0d,
            MaxValue = 1000d,
            Step = 1d,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(160f, 32f)
        };
        _timeline.DragStarted += () => _updatingTimeline = true;
        _timeline.ValueChanged += SeekTimeline;
        _timeline.DragEnded += _ => _updatingTimeline = false;
        transport.AddChild(_timeline);
        var speed = new OptionButton { CustomMinimumSize = new Vector2(84f, 34f) };
        foreach (var value in new[] { 0.25d, 0.5d, 1d, 1.5d, 2d })
            speed.AddItem($"{value:0.##}×");
        speed.Select(2);
        speed.ItemSelected += index => SetPlaybackSpeed(
            new[] { 0.25d, 0.5d, 1d, 1.5d, 2d }[(int)index]);
        StyleButton(speed);
        transport.AddChild(speed);
        column.AddChild(transport);
        return panel;
    }

    private Control CreateViewportHint()
    {
        var badge = NewPanel("ViewportHint", new Color("10151bdd"));
        badge.AnchorLeft = 0f;
        badge.AnchorTop = 0f;
        badge.OffsetLeft = LeftWidth + 16f;
        badge.OffsetTop = TopHeight + 14f;
        badge.OffsetRight = LeftWidth + 382f;
        badge.OffsetBottom = TopHeight + 54f;
        var margin = NewMargin(9, 5, 9, 5);
        badge.AddChild(margin);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 7);
        margin.AddChild(row);
        var label = NewLabel("实时 3D", 11, Muted);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.CustomMinimumSize = new Vector2(64f, 30f);
        row.AddChild(label);
        var reset = NewButton("镜头归位", 82f);
        reset.CustomMinimumSize = new Vector2(82f, 30f);
        reset.AddThemeFontSizeOverride("font_size", 11);
        reset.Pressed += FrameCurrentAsset;
        row.AddChild(reset);
        _axesToggle = NewButton("原点轴：开", 86f);
        _axesToggle.CustomMinimumSize = new Vector2(86f, 30f);
        _axesToggle.Pressed += ToggleOriginAxes;
        StyleButton(_axesToggle, _axesVisible);
        _axesToggle.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(_axesToggle);
        return badge;
    }

    private void LoadCatalog()
    {
        try
        {
            _catalog.AddRange(War3AssetPack.LoadCatalog());
            SetStatus($"{_catalog.Count:N0} 项资产", Success);
            RefreshCatalog();
        }
        catch (Exception exception)
        {
            SetStatus("目录读取失败", new Color("eb776d"));
            GD.PushError($"War3 asset catalog failed: {exception}");
        }
    }

    private void RefreshCatalog()
    {
        if (_assetList is null) return;
        var query = _search?.Text.Trim() ?? string.Empty;
        _visibleEntries.Clear();
        _visibleEntries.AddRange(_catalog.Where(entry =>
            (!_category.HasValue || entry.Category == _category.Value) &&
            (_race == "all" || NormalizeRace(entry.Race) == _race) &&
            (query.Length == 0 ||
             entry.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Source.Contains(query, StringComparison.OrdinalIgnoreCase))));
        _assetList.Clear();
        const int displayLimit = 900;
        foreach (var entry in _visibleEntries.Take(displayLimit))
        {
            var effectMark = entry.HasEffects ? "  ·  FX" : string.Empty;
            _assetList.AddItem(
                $"{entry.DisplayName}{effectMark}\n{ShortSource(entry.Source)}",
                LoadCatalogIcon(entry));
        }
        if (_visibleEntries.Count > displayLimit)
            _visibleEntries.RemoveRange(displayLimit, _visibleEntries.Count - displayLimit);
        if (_catalogCount is not null)
            _catalogCount.Text = _visibleEntries.Count == _catalog.Count
                ? $"全部  {_catalog.Count:N0}"
                : $"当前  {_visibleEntries.Count:N0}  /  {_catalog.Count:N0}";
        RefreshCategoryButtons();
        RefreshRaceButtons();
    }

    private void SelectDefaultAsset()
    {
        if (_catalog.Count == 0) return;
        var unitCapture = OS.GetCmdlineUserArgs().Contains("--war3-unit-capture");
        var preferredSource = AutomationArgument("--war3-asset-source=") ??
            (unitCapture
                ? "Units\\Human\\Footman\\Footman.mdx"
                : "Abilities\\Spells\\Undead\\FrostNova\\FrostNovaTarget.mdx");
        var entry = _catalog.FirstOrDefault(candidate => candidate.Source.Equals(
                        preferredSource, StringComparison.OrdinalIgnoreCase)) ??
                    _catalog.FirstOrDefault(candidate => candidate.HasEffects) ??
                    _catalog[0];
        _category = entry.Category;
        RefreshCatalog();
        var index = _visibleEntries.FindIndex(candidate => candidate.Source == entry.Source);
        if (index >= 0)
        {
            _assetList?.Select(index);
            _assetList?.EnsureCurrentIsVisible();
        }
        LoadAsset(entry);
    }

    private void LoadAsset(War3AssetEntry entry)
    {
        SetStatus("正在加载…", Accent);
        try
        {
            ClearLoadedAsset();
            _currentEntry = entry;
            _metadata = War3ModelMetadata.Load(entry.MetadataRelativePath);
            var document = new GltfDocument();
            var state = new GltfState();
            var modelPath = War3AssetPack.AbsolutePath(entry.ModelRelativePath);
            var materialFilters = ReadGlbMaterialFilters(modelPath);
            var error = document.AppendFromFile(modelPath, state);
            if (error != Error.Ok)
                throw new InvalidOperationException($"glTF 读取失败：{error}");
            _loadedModel = document.GenerateScene(state) ??
                           throw new InvalidOperationException("glTF 未生成场景节点");
            _loadedModel.Name = "War3Model";
            _modelHost!.AddChild(_loadedModel);
            ApplyWar3MeshMaterials(_loadedModel, materialFilters);
            IndexGeosetNodes(_loadedModel, _geosetNodes);
            _animationPlayer = FindFirst<AnimationPlayer>(_loadedModel);
            _effects = new War3EffectRuntime { Name = "War3Effects" };
            _modelHost.AddChild(_effects);
            _effects.Initialize(_loadedModel, _camera!, _metadata);
            _effects.Visible = _effectsVisible;
            ConfigureInspector(entry);
            BuildSequenceButtons();
            FrameCurrentAsset();
            SelectSequence(0);
            SetStatus("已就绪", Success);
        }
        catch (Exception exception)
        {
            SetStatus("加载失败", new Color("eb776d"));
            if (_effectNote is not null) _effectNote.Text = exception.Message;
            GD.PushError($"War3 asset load failed ({entry.Source}): {exception}");
        }
    }

    private void ConfigureInspector(War3AssetEntry entry)
    {
        if (_title is not null) _title.Text = entry.DisplayName;
        if (_path is not null) _path.Text = entry.Source;
        if (_classification is not null)
            _classification.Text = $"分类  {War3AssetPack.CategoryLabel(entry.Category)}   ·   种族  {RaceLabel(entry.Race)}";
        if (_modelStats is not null)
            _modelStats.Text = $"模型  {entry.GeosetCount} Geoset   ·   {entry.TextureCount} 贴图\n动画  {entry.SequenceCount}   ·   Event {(_metadata?.EventObjectCount ?? 0)}";
        if (_effectNote is not null)
            _effectNote.Text = _metadata?.LegacyParticleEmitterCount > 0
                ? $"PE2 与 Ribbon 已重建。另有 {_metadata.LegacyParticleEmitterCount} 个旧式 ParticleEmitter（嵌套 MDL 发射器），当前保留元数据但不生成实例。"
                : entry.HasEffects
                    ? "ParticleEmitter2 与 Ribbon 已由 Godot 运行时重建；拖动下方时间轴可逐帧检查。"
                    : "该模型没有 ParticleEmitter2 / Ribbon。静态网格与骨骼动画仍可正常检查。";
        UpdatePortraitPreview(entry);
        UpdateLiveEffectStats();
    }

    private void UpdatePortraitPreview(War3AssetEntry entry)
    {
        ClearPortrait();
        var building = entry.Category == War3AssetCategory.Buildings;
        var portrait = entry.Category switch
        {
            War3AssetCategory.Buildings => entry,
            War3AssetCategory.Portraits => entry,
            War3AssetCategory.Units or War3AssetCategory.Heroes => FindAssociatedPortrait(entry),
            _ => null
        };
        if (portrait is null || _portraitWorld is null || _portraitSection is null) return;

        try
        {
            _portraitSection.Visible = true;
            ConfigurePortraitLayout(building, entry.Race);
            if (_portraitHeading is not null)
                _portraitHeading.Text = building
                    ? "游戏内建筑视图"
                    : entry.Category == War3AssetCategory.Portraits
                        ? "游戏内肖像"
                        : "关联单位肖像";
            if (_portraitCameraLabel is not null)
                _portraitCameraLabel.Text = building ? "BUILDING VIEW" : "PORTRAIT CAMERA";

            _portraitMetadata = War3ModelMetadata.Load(portrait.MetadataRelativePath);
            _portraitCameraDefinition = building
                ? null
                : War3AssetPack.LoadPortraitCamera(portrait.Source);
            var document = new GltfDocument();
            var state = new GltfState();
            var portraitPath = War3AssetPack.AbsolutePath(portrait.ModelRelativePath);
            var materialFilters = ReadGlbMaterialFilters(portraitPath);
            var error = document.AppendFromFile(portraitPath, state);
            if (error != Error.Ok)
                throw new InvalidOperationException($"肖像 glTF 读取失败：{error}");
            _portraitModel = document.GenerateScene(state) ??
                             throw new InvalidOperationException("肖像 glTF 未生成场景节点");
            _portraitModel.Name = building ? "BuildingPreview" : "PortraitModel";
            _portraitWorld.AddChild(_portraitModel);
            ApplyWar3MeshMaterials(_portraitModel, materialFilters);
            IndexGeosetNodes(_portraitModel, _portraitGeosetNodes);
            _portraitAnimationPlayer = FindFirst<AnimationPlayer>(_portraitModel);
            FramePortrait(building);
            SelectPortraitMode(talking: false, building);
            if (_portraitCameraLabel is not null && !building)
                _portraitCameraLabel.Text = _portraitCameraDefinition is null
                    ? "AUTO CLOSE-UP"
                    : "MDX PORTRAIT CAMERA";
        }
        catch (Exception exception)
        {
            ClearPortrait();
            if (_portraitSection is not null)
            {
                _portraitSection.Visible = true;
                if (_portraitHeading is not null) _portraitHeading.Text = "肖像不可用";
                if (_portraitCameraLabel is not null) _portraitCameraLabel.Text = exception.Message;
            }
            GD.PushWarning($"War3 portrait load failed ({portrait.Source}): {exception.Message}");
        }
    }

    private War3AssetEntry? FindAssociatedPortrait(War3AssetEntry entry)
    {
        var source = entry.Source.Replace('/', '\\');
        var separator = source.LastIndexOf('\\');
        if (separator < 0) return null;
        var directory = source[..separator];
        var modelName = Path.GetFileNameWithoutExtension(source[(separator + 1)..]);
        var candidates = _catalog.Where(candidate =>
        {
            if (candidate.Category != War3AssetCategory.Portraits) return false;
            var candidateSource = candidate.Source.Replace('/', '\\');
            var candidateSeparator = candidateSource.LastIndexOf('\\');
            return candidateSeparator >= 0 && candidateSource[..candidateSeparator].Equals(
                directory, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        return candidates.FirstOrDefault(candidate =>
        {
            var stem = Path.GetFileNameWithoutExtension(candidate.Source.Replace('\\', '/'));
            var baseStem = RemovePortraitSuffix(stem);
            return baseStem.Equals(modelName, StringComparison.OrdinalIgnoreCase);
        }) ?? candidates.FirstOrDefault();
    }

    private static string RemovePortraitSuffix(string value)
    {
        var portrait = value.LastIndexOf("portrait", StringComparison.OrdinalIgnoreCase);
        if (portrait < 0) return value;
        return value[..portrait].TrimEnd('_', '-');
    }

    private void ConfigurePortraitLayout(bool building, string race)
    {
        if (_portraitFrame is null || _portraitOpening is null || _portraitViewport is null) return;
        _portraitFrame.CustomMinimumSize = building
            ? new Vector2(356f, 260f)
            : new Vector2(356f, 438f);
        SetPortraitOpeningRect(building);
        _portraitMask!.Visible = !building;
        _portraitHealth!.Visible = !building;
        _portraitMana!.Visible = !building;
        _portraitControls!.Visible = !building;
        _portraitSequenceLabel!.Visible = !building;
        if (!building) _portraitMask.Texture = LoadPortraitMask(race);
    }

    private void SetPortraitOpeningRect(bool building)
    {
        if (_portraitOpening is null) return;
        if (building)
            SetRect(_portraitOpening, 0f, 0f, 0f, 0f, 0f, 0f, 356f, 260f);
        else
            SetRect(_portraitOpening, 0f, 0f, 0f, 0f, 55f, 60f, 304f, 328f);
    }

    private Texture2D? LoadPortraitMask(string race)
    {
        var family = race.ToLowerInvariant() switch
        {
            "orc" => "Orc",
            "nightelf" or "naga" => "NightElf",
            "undead" or "demon" => "Undead",
            _ => "Human"
        };
        var imagePath = War3AssetPack.AbsolutePath(
            $"textures/UI/Console/{family}/{family}UIPortraitMask.png");
        if (!File.Exists(imagePath)) return null;
        var image = Image.LoadFromFile(imagePath);
        if (image is null || image.IsEmpty()) return null;
        var source = ImageTexture.CreateFromImage(image);
        return new AtlasTexture
        {
            Atlas = source,
            Region = new Rect2(0f, 96f, 130f, 160f),
            FilterClip = true
        };
    }

    private void SelectPortraitMode(bool talking, bool building = false)
    {
        if (_portraitMetadata is null || _portraitMetadata.Sequences.Count == 0) return;
        var candidates = _portraitMetadata.Sequences
            .Select((sequence, index) => (Sequence: sequence, Index: index))
            .Where(pair => building
                ? pair.Sequence.Name.StartsWith("Stand", StringComparison.OrdinalIgnoreCase)
                : talking
                    ? pair.Sequence.Name.StartsWith("Portrait Talk", StringComparison.OrdinalIgnoreCase)
                    : pair.Sequence.Name.StartsWith("Portrait", StringComparison.OrdinalIgnoreCase) &&
                      !pair.Sequence.Name.StartsWith("Portrait Talk", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Index)
            .ToArray();
        if (candidates.Length == 0 && talking) return;
        _portraitSequenceIndex = candidates.FirstOrDefault();
        if (candidates.Length == 0)
        {
            var stand = _portraitMetadata.Sequences
                .Select((sequence, index) => (sequence, index))
                .FirstOrDefault(pair => pair.sequence.Name.StartsWith(
                    "Stand", StringComparison.OrdinalIgnoreCase));
            _portraitSequenceIndex = stand.sequence is null ? 0 : stand.index;
        }
        var sequence = _portraitMetadata.Sequences[_portraitSequenceIndex];
        if (_portraitAnimationPlayer is not null)
        {
            var animationName = FindAnimationName(_portraitAnimationPlayer, sequence.Name);
            if (animationName is not null)
            {
                var animation = _portraitAnimationPlayer.GetAnimation(animationName);
                animation.LoopMode = Animation.LoopModeEnum.Linear;
                _portraitAnimationPlayer.Play(animationName);
                _portraitAnimationPlayer.Seek(0d, true);
            }
        }
        ApplyGeosetVisibility(
            _portraitGeosetNodes, _portraitMetadata, _portraitSequenceIndex, 0d);
        if (_portraitSequenceLabel is not null) _portraitSequenceLabel.Text = sequence.Name;
        if (_portraitIdleButton is not null) StyleButton(_portraitIdleButton, !talking);
        if (_portraitTalkButton is not null)
        {
            var hasTalk = _portraitMetadata.Sequences.Any(item => item.Name.StartsWith(
                "Portrait Talk", StringComparison.OrdinalIgnoreCase));
            _portraitTalkButton.Disabled = !hasTalk;
            StyleButton(_portraitTalkButton, talking && hasTalk);
        }
    }

    private void FramePortrait(bool building)
    {
        if (_portraitMetadata is null || _portraitModel is null || _portraitCamera is null) return;
        var bounds = _portraitMetadata.Bounds;
        var root = FindByName<Node3D>(_portraitModel, "WarcraftRoot") ?? _portraitModel as Node3D;
        var target = root is null ? bounds.Center * WarcraftUnitScale : root.ToGlobal(bounds.Center);
        if (!building && _portraitCameraDefinition is not null && root is not null)
        {
            var camera = _portraitCameraDefinition;
            var cameraPosition = root.ToGlobal(camera.Position);
            target = root.ToGlobal(camera.TargetPosition);
            _portraitCamera.Fov = Mathf.RadToDeg(camera.FieldOfViewRadians);
            _portraitCamera.Near = Math.Max(0.001f, camera.NearClip * WarcraftUnitScale);
            _portraitCamera.Far = Math.Max(
                _portraitCamera.Near + 1f, camera.FarClip * WarcraftUnitScale);
            _portraitCamera.Position = cameraPosition;
            _portraitCamera.LookAt(target, Vector3.Up);
            return;
        }
        _portraitCamera.Fov = 45f;
        _portraitCamera.Near = 0.005f;
        _portraitCamera.Far = 500f;
        if (building)
        {
            var size = Math.Max(0.2f, bounds.Size * WarcraftUnitScale);
            var distance = Math.Clamp(size * 1.65f, 0.7f, 180f);
            _portraitCamera.Position = target + new Vector3(
                distance * 0.72f, distance * 0.48f, distance * 0.92f);
        }
        else
        {
            var radius = Math.Max(30f, bounds.Radius > 0f
                ? bounds.Radius
                : bounds.Size * 0.5f) * WarcraftUnitScale;
            _portraitCamera.Position = target + new Vector3(
                radius * 1.4f, radius * 0.25f, radius * 1.7f);
        }
        _portraitCamera.LookAt(target, Vector3.Up);
    }

    private void ClearPortrait()
    {
        if (_portraitModel is not null)
        {
            _portraitWorld?.RemoveChild(_portraitModel);
            _portraitModel.QueueFree();
        }
        _portraitModel = null;
        _portraitAnimationPlayer = null;
        _portraitMetadata = null;
        _portraitCameraDefinition = null;
        _portraitGeosetNodes.Clear();
        if (_portraitSection is not null) _portraitSection.Visible = false;
    }

    private void BuildSequenceButtons()
    {
        if (_sequenceRow is null || _metadata is null) return;
        foreach (var child in _sequenceRow.GetChildren()) child.QueueFree();
        _sequenceButtons.Clear();
        for (var index = 0; index < _metadata.Sequences.Count; index++)
        {
            var captured = index;
            var sequence = _metadata.Sequences[index];
            var button = NewButton(sequence.Name, 0f);
            button.CustomMinimumSize = new Vector2(0f, 30f);
            StyleSequenceButton(button);
            button.TooltipText = $"{sequence.DurationMilliseconds / 1000d:0.###} 秒" +
                                 (sequence.NonLooping ? " · 不循环" : " · 循环");
            button.Pressed += () => SelectSequence(captured);
            _sequenceRow.AddChild(button);
            _sequenceButtons.Add(button);
        }
        if (_metadata.Sequences.Count == 0)
            _sequenceRow.AddChild(NewLabel("此资产没有动画片段", 12, Muted));
    }

    private void SelectSequence(int index)
    {
        if (_metadata is null || _metadata.Sequences.Count == 0) return;
        _sequenceIndex = Math.Clamp(index, 0, _metadata.Sequences.Count - 1);
        var sequence = _metadata.Sequences[_sequenceIndex];
        _localMilliseconds = 0d;
        if (_timeline is not null)
        {
            _timeline.MaxValue = sequence.DurationMilliseconds;
            _timeline.SetValueNoSignal(0d);
        }
        _effects?.SetSequence(_sequenceIndex);
        if (_animationPlayer is not null)
        {
            var animationName = FindAnimationName(sequence.Name);
            if (animationName is not null)
            {
                var animation = _animationPlayer.GetAnimation(animationName);
                animation.LoopMode = sequence.NonLooping
                    ? Animation.LoopModeEnum.None
                    : Animation.LoopModeEnum.Linear;
                _animationPlayer.Play(animationName, customBlend: 0.08d,
                    customSpeed: (float)_playbackSpeed);
                _animationPlayer.Seek(0d, true);
            }
        }
        SetPlaying(true);
        ApplyGeosetVisibility(
            _geosetNodes, _metadata, _sequenceIndex, _localMilliseconds);
        RefreshSequenceButtons();
    }

    private void SeekTimeline(double value)
    {
        if (_metadata is null || _metadata.Sequences.Count == 0) return;
        _localMilliseconds = value;
        var hasAnimation = (_animationPlayer?.CurrentAnimation.ToString() ?? string.Empty).Length > 0;
        _effects?.Seek(value, milliseconds =>
        {
            if (_animationPlayer is not null && hasAnimation)
                _animationPlayer.Seek(milliseconds / 1000d, true);
        });
        if (_animationPlayer is not null && hasAnimation)
            _animationPlayer.Seek(value / 1000d, true);
        ApplyGeosetVisibility(
            _geosetNodes, _metadata, _sequenceIndex, _localMilliseconds);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        if (_playButton is not null) _playButton.Text = playing ? "暂停" : "播放";
        if (_animationPlayer is not null)
            _animationPlayer.SpeedScale = playing ? (float)_playbackSpeed : 0f;
    }

    private void SetPlaybackSpeed(double speed)
    {
        _playbackSpeed = speed;
        if (_animationPlayer is not null && _playing)
            _animationPlayer.SpeedScale = (float)speed;
    }

    private StringName? FindAnimationName(string sequenceName)
    {
        return FindAnimationName(_animationPlayer, sequenceName);
    }

    private static StringName? FindAnimationName(
        AnimationPlayer? animationPlayer,
        string sequenceName)
    {
        if (animationPlayer is null) return null;
        var animations = animationPlayer.GetAnimationList();
        foreach (var name in animations)
        {
            if (name.ToString().Equals(sequenceName, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        foreach (var name in animations)
        {
            if (name.ToString().Contains(sequenceName, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        foreach (var name in animations)
        {
            if (name.ToString() != "RESET") return name;
        }
        return null;
    }

    private void FrameCurrentAsset()
    {
        if (_metadata is null || _loadedModel is null) return;
        var bounds = _metadata.Bounds;
        var root = FindByName<Node3D>(_loadedModel, "WarcraftRoot") ?? _loadedModel as Node3D;
        _cameraTarget = root is null ? bounds.Center * 0.01f : root.ToGlobal(bounds.Center);
        var size = Math.Max(0.15f, bounds.Size * WarcraftUnitScale);
        _distance = Math.Clamp(size * 1.48f, 0.45f, 180f);
        _yaw = -0.62f;
        _pitch = -0.18f;
        if (_ground is not null)
        {
            _ground.Scale = new Vector3(Math.Max(1f, size * 4f), 1f, Math.Max(1f, size * 4f));
            _ground.Position = new Vector3(_cameraTarget.X, Math.Min(0f, _cameraTarget.Y - size * 0.52f), _cameraTarget.Z);
        }
        if (_grid is not null)
        {
            _grid.Scale = Vector3.One * Math.Max(1f, size);
            _grid.Position = _ground?.Position ?? Vector3.Zero;
        }
        if (_originAxes is not null)
        {
            var globalOrigin = root?.ToGlobal(Vector3.Zero) ?? Vector3.Zero;
            _originAxes.Position = _modelHost?.ToLocal(globalOrigin) ?? globalOrigin;
            _originAxes.Scale = Vector3.One * Math.Clamp(size * 0.2f, 0.14f, 0.8f);
            _originAxes.Visible = _axesVisible;
        }
        UpdateCamera();
    }

    private void PanCamera(Vector2 relative)
    {
        if (_camera is null) return;
        var viewportHeight = Math.Max(1f,
            GetViewport().GetVisibleRect().Size.Y - TopHeight - TimelineHeight);
        var worldPerPixel = 2f * _distance *
                            MathF.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f) /
                            viewportHeight;
        var right = _camera.GlobalBasis.X.Normalized();
        var up = _camera.GlobalBasis.Y.Normalized();
        _cameraTarget += (-right * relative.X + up * relative.Y) * worldPerPixel;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        if (_camera is null) return;
        var horizontal = MathF.Cos(_pitch) * _distance;
        _camera.Position = _cameraTarget + new Vector3(
            MathF.Sin(_yaw) * horizontal,
            MathF.Sin(_pitch) * _distance,
            MathF.Cos(_yaw) * horizontal);
        _camera.LookAt(_cameraTarget, Vector3.Up);
    }

    private void ToggleEffects()
    {
        _effectsVisible = !_effectsVisible;
        if (_effects is not null) _effects.Visible = _effectsVisible;
        if (_effectToggle is not null) _effectToggle.Text = _effectsVisible ? "特效：开" : "特效：关";
    }

    private void ToggleGrid()
    {
        _gridVisible = !_gridVisible;
        if (_ground is not null) _ground.Visible = _gridVisible;
        if (_grid is not null) _grid.Visible = _gridVisible;
        if (_gridToggle is not null) _gridToggle.Text = _gridVisible ? "网格：开" : "网格：关";
    }

    private void ToggleOriginAxes()
    {
        _axesVisible = !_axesVisible;
        if (_originAxes is not null) _originAxes.Visible = _axesVisible && _loadedModel is not null;
        if (_axesToggle is null) return;
        _axesToggle.Text = _axesVisible ? "原点轴：开" : "原点轴：关";
        StyleButton(_axesToggle, _axesVisible);
        _axesToggle.AddThemeFontSizeOverride("font_size", 11);
        _axesToggle.CustomMinimumSize = new Vector2(86f, 30f);
    }

    private void UpdateLiveEffectStats()
    {
        if (_effectStats is null) return;
        _effectStats.Text = _effects is null
            ? "PE2 —  ·  Ribbon —"
            : $"PE2 {_effects.ActiveParticleEmitterCount}  ·  Ribbon {_effects.ActiveRibbonEmitterCount}" +
              (_metadata?.LegacyParticleEmitterCount > 0
                  ? $"  ·  Legacy {_metadata.LegacyParticleEmitterCount}"
                  : string.Empty) + "\n" +
              $"实时粒子 {_effects.LiveParticleCount}  ·  轨迹点 {_effects.LiveRibbonPointCount}\n" +
              $"挂点 {_effects.ResolvedEmitterCount} / " +
              $"{_effects.ActiveParticleEmitterCount + _effects.ActiveRibbonEmitterCount}";
    }

    private void ClearLoadedAsset()
    {
        ClearPortrait();
        if (_loadedModel is not null)
        {
            _modelHost?.RemoveChild(_loadedModel);
            _loadedModel.QueueFree();
        }
        if (_effects is not null)
        {
            _modelHost?.RemoveChild(_effects);
            _effects.QueueFree();
        }
        _loadedModel = null;
        _effects = null;
        _animationPlayer = null;
        _metadata = null;
        _geosetNodes.Clear();
        if (_originAxes is not null) _originAxes.Visible = false;
    }

    private async Task RunAutomationAsync()
    {
        var requestedSequence = AutomationArgument("--war3-asset-sequence=");
        if (_metadata is not null && !string.IsNullOrWhiteSpace(requestedSequence))
        {
            var requestedIndex = _metadata.Sequences
                .Select((sequence, index) => (sequence, index))
                .FirstOrDefault(pair => pair.sequence.Name.Equals(
                    requestedSequence, StringComparison.OrdinalIgnoreCase)).index;
            SelectSequence(requestedIndex);
        }
        await ToSignal(GetTree().CreateTimer(0.45d), SceneTreeTimer.SignalName.Timeout);
        if (_metadata is not null && _metadata.Sequences.Count > 0)
        {
            var requestedTime = AutomationArgument("--war3-asset-time=");
            var seek = double.TryParse(
                requestedTime, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var time)
                ? time
                : Math.Min(400d,
                    _metadata.Sequences[_sequenceIndex].DurationMilliseconds * 0.45d);
            SeekTimeline(Math.Clamp(
                seek, 0d, _metadata.Sequences[_sequenceIndex].DurationMilliseconds));
            SetPlaying(false);
        }
        await ToSignal(GetTree().CreateTimer(0.35d), SceneTreeTimer.SignalName.Timeout);
        var smoke = OS.GetCmdlineUserArgs().Contains("--war3-assets-smoke");
        var capture = OS.GetCmdlineUserArgs().Contains("--war3-assets-capture");
        var unitCapture = OS.GetCmdlineUserArgs().Contains("--war3-unit-capture");
        var modelCapture = OS.GetCmdlineUserArgs().Contains("--war3-model-capture");
        if (modelCapture)
        {
            var modelSuccess = _loadedModel is not null && _metadata is not null &&
                               _animationPlayer is not null;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var modelPath = ProjectSettings.GlobalizePath(
                "user://war3_asset_lab_model_capture.png");
            GetViewport().GetTexture().GetImage().SavePng(modelPath);
            GD.Print($"WAR3_MODEL_CAPTURE success={modelSuccess} " +
                     $"source={_currentEntry?.Source} " +
                     $"sequence={_metadata?.Sequences[_sequenceIndex].Name} " +
                     $"time={_localMilliseconds:0} path={modelPath}");
            GetTree().Quit(modelSuccess ? 0 : 1);
            return;
        }
        if (unitCapture)
        {
            _race = "human";
            RefreshCatalog();
            var raceFilterValid = _visibleEntries.Count > 0 &&
                                  _visibleEntries.All(entry => NormalizeRace(entry.Race) == "human");
            _race = "all";
            RefreshCatalog();
            var footmanListIndex = _visibleEntries.FindIndex(entry => entry.Source.Equals(
                "Units\\Human\\Footman\\Footman.mdx", StringComparison.OrdinalIgnoreCase));
            var catalogIconLoaded = footmanListIndex >= 0 &&
                                    _assetList?.GetItemIcon(footmanListIndex) is not null;
            var framedTarget = _cameraTarget;
            PanCamera(new Vector2(42f, -24f));
            var cameraPanned = _cameraTarget.DistanceTo(framedTarget) > 0.01f;
            FrameCurrentAsset();
            var cameraReset = _cameraTarget.DistanceTo(framedTarget) < 0.001f;
            var originAxesVisible = _originAxes?.Visible == true;
            var standIsolated = IsGeosetVisible(_geosetNodes, 0) &&
                                IsGeosetVisible(_geosetNodes, 1) &&
                                !IsGeosetVisible(_geosetNodes, 2) &&
                                !IsGeosetVisible(_geosetNodes, 3) &&
                                !IsGeosetVisible(_geosetNodes, 4);
            var decayIndex = _metadata?.Sequences
                .Select((sequence, index) => (sequence, index))
                .FirstOrDefault(pair => pair.sequence.Name.Equals(
                    "Decay Bone", StringComparison.OrdinalIgnoreCase)).index ?? 0;
            SelectSequence(decayIndex);
            SeekTimeline(1000d);
            SetPlaying(false);
            var decayIsolated = !IsGeosetVisible(_geosetNodes, 0) &&
                                !IsGeosetVisible(_geosetNodes, 1) &&
                                IsGeosetVisible(_geosetNodes, 4);
            SelectSequence(0);
            SeekTimeline(400d);
            SetPlaying(false);
            var unitSuccess = _loadedModel is not null && _metadata is not null &&
                              _metadata.Sequences.Count >= 10 && _animationPlayer is not null &&
                              _portraitModel is not null && _portraitMetadata is not null &&
                              _portraitCameraDefinition is not null && standIsolated &&
                              decayIsolated && raceFilterValid && catalogIconLoaded &&
                              cameraPanned && cameraReset && originAxesVisible;
            GD.Print($"WAR3_UNIT_SMOKE success={unitSuccess} source={_currentEntry?.Source} " +
                     $"stand_isolated={standIsolated} decay_isolated={decayIsolated} " +
                     $"portrait={_portraitModel is not null} " +
                     $"portrait_camera={_portraitCameraDefinition is not null} " +
                     $"race_filter_valid={raceFilterValid} catalog_icon={catalogIconLoaded} " +
                     $"camera_pan={cameraPanned} camera_reset={cameraReset} " +
                     $"origin_axes={originAxesVisible}");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var unitPath = ProjectSettings.GlobalizePath("user://war3_asset_lab_unit_capture.png");
            GetViewport().GetTexture().GetImage().SavePng(unitPath);
            GD.Print($"WAR3_UNIT_CAPTURE {unitPath}");
            GetTree().Quit(unitSuccess ? 0 : 1);
            return;
        }
        var success = _loadedModel is not null && _metadata is not null &&
                      _effects is not null && _effects.ActiveParticleEmitterCount > 0 &&
                      _effects.ActiveRibbonEmitterCount > 0 &&
                      _effects.ResolvedEmitterCount ==
                      _effects.ActiveParticleEmitterCount + _effects.ActiveRibbonEmitterCount &&
                      _effects.LiveParticleCount > 0 && _effects.LiveRibbonPointCount > 0;
        GD.Print($"WAR3_EFFECT_SMOKE success={success} source={_currentEntry?.Source} " +
                 $"sequences={_metadata?.Sequences.Count ?? 0} " +
                 $"particle_emitters={_effects?.ActiveParticleEmitterCount ?? 0} " +
                 $"ribbon_emitters={_effects?.ActiveRibbonEmitterCount ?? 0} " +
                 $"resolved_emitters={_effects?.ResolvedEmitterCount ?? 0} " +
                 $"live_particles={_effects?.LiveParticleCount ?? 0} " +
                 $"ribbon_points={_effects?.LiveRibbonPointCount ?? 0}");
        if (capture)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var path = ProjectSettings.GlobalizePath("user://war3_asset_lab_capture.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"WAR3_ASSET_CAPTURE {path}");
        }
        if (smoke)
        {
            var unit = _catalog.FirstOrDefault(entry => entry.Source.Equals(
                "Units\\Human\\Footman\\Footman.mdx", StringComparison.OrdinalIgnoreCase));
            if (unit is not null)
            {
                LoadAsset(unit);
                await ToSignal(GetTree().CreateTimer(0.4d), SceneTreeTimer.SignalName.Timeout);
            }
            var unitIconLoaded = unit is not null && unit.IconPath.Length > 0 &&
                                 LoadCatalogIcon(unit) is not null;
            var unitSuccess = unit is not null && _loadedModel is not null &&
                              _metadata is not null && _metadata.Sequences.Count >= 10 &&
                              _animationPlayer is not null && _portraitModel is not null &&
                              _portraitMetadata is not null && _raceButtons.Count >= 10 &&
                              unitIconLoaded;
            var corpseHidden = !IsGeosetVisible(_geosetNodes, 4);
            unitSuccess &= corpseHidden;
            success &= unitSuccess;
            GD.Print($"WAR3_UNIT_SMOKE success={unitSuccess} source={_currentEntry?.Source} " +
                     $"sequences={_metadata?.Sequences.Count ?? 0} " +
                     $"animation_player={_animationPlayer is not null} " +
                     $"corpse_hidden={corpseHidden} portrait={_portraitModel is not null} " +
                     $"race_filters={_raceButtons.Count} catalog_icon={unitIconLoaded}");
            if (capture && unitSuccess)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var unitPath = ProjectSettings.GlobalizePath("user://war3_asset_lab_unit_capture.png");
                GetViewport().GetTexture().GetImage().SavePng(unitPath);
                GD.Print($"WAR3_UNIT_CAPTURE {unitPath}");
            }
        }
        if (smoke || capture) GetTree().Quit(success ? 0 : 1);
    }

    private static string? AutomationArgument(string prefix) =>
        OS.GetCmdlineUserArgs()
            .FirstOrDefault(argument => argument.StartsWith(
                prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private void AddCategoryButton(
        Container parent,
        string label,
        War3AssetCategory? category)
    {
        var button = NewButton(label, 0f);
        button.Pressed += () =>
        {
            _category = category;
            RefreshCatalog();
        };
        button.SetMeta("category", category.HasValue ? (int)category.Value : -1);
        parent.AddChild(button);
        _categoryButtons.Add(button);
    }

    private void RefreshCategoryButtons()
    {
        var selected = _category.HasValue ? (int)_category.Value : -1;
        foreach (var button in _categoryButtons)
            StyleButton(button, button.GetMeta("category").AsInt32() == selected);
    }

    private void AddRaceButton(Container parent, string label, string race)
    {
        var button = NewButton(label, 0f);
        button.CustomMinimumSize = new Vector2(0f, 30f);
        button.SetMeta("race", race);
        button.Pressed += () =>
        {
            _race = race;
            RefreshCatalog();
        };
        parent.AddChild(button);
        _raceButtons.Add(button);
    }

    private void RefreshRaceButtons()
    {
        foreach (var button in _raceButtons)
            StyleButton(button, button.GetMeta("race").AsString() == _race);
    }

    private void RefreshSequenceButtons()
    {
        for (var index = 0; index < _sequenceButtons.Count; index++)
            StyleSequenceButton(_sequenceButtons[index], index == _sequenceIndex);
    }

    private void SetStatus(string text, Color color)
    {
        if (_status is null) return;
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }

    private bool IsInsideModelViewport(Vector2 point)
    {
        var size = GetViewport().GetVisibleRect().Size;
        return point.X >= LeftWidth && point.X <= size.X - RightWidth &&
               point.Y >= TopHeight && point.Y <= size.Y - TimelineHeight;
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindFirst<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static T? FindByName<T>(Node root, string name) where T : Node
    {
        if (root is T match && root.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
            return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindByName<T>(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    private static void IndexGeosetNodes(
        Node root,
        Dictionary<int, List<Node3D>> output)
    {
        output.Clear();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Node3D spatial && TryReadGeosetId(node.Name.ToString(), out var geosetId))
            {
                if (!output.TryGetValue(geosetId, out var nodes))
                {
                    nodes = [];
                    output.Add(geosetId, nodes);
                }
                nodes.Add(spatial);
            }
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
    }

    private static IReadOnlyDictionary<string, int> ReadGlbMaterialFilters(string glbPath)
    {
        var output = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bytes = File.ReadAllBytes(glbPath);
        if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546c67) return output;
        var jsonLength = BitConverter.ToInt32(bytes, 12);
        if (jsonLength <= 0 || 20 + jsonLength > bytes.Length) return output;
        using var document = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
        if (!document.RootElement.TryGetProperty("materials", out var materials)) return output;
        foreach (var material in materials.EnumerateArray())
        {
            if (!material.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                !material.TryGetProperty("extras", out var extras) ||
                !extras.TryGetProperty("war3FilterMode", out var filterElement) ||
                !filterElement.TryGetInt32(out var filterMode))
                continue;
            var name = nameElement.GetString();
            if (!string.IsNullOrWhiteSpace(name)) output[name] = filterMode;
        }
        return output;
    }

    private static void ApplyWar3MeshMaterials(
        Node root,
        IReadOnlyDictionary<string, int> materialFilters)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D meshInstance && meshInstance.Mesh is not null)
            {
                for (var surface = 0; surface < meshInstance.Mesh.GetSurfaceCount(); surface++)
                {
                    var source = meshInstance.GetActiveMaterial(surface) as StandardMaterial3D;
                    if (source is null ||
                        !materialFilters.TryGetValue(source.ResourceName, out var filterMode))
                        continue;
                    var material = (StandardMaterial3D)source.Duplicate();
                    if (filterMode >= 2)
                    {
                        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                        material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
                    }
                    material.BlendMode = filterMode switch
                    {
                        3 or 4 => BaseMaterial3D.BlendModeEnum.Add,
                        5 or 6 => BaseMaterial3D.BlendModeEnum.Mul,
                        _ => BaseMaterial3D.BlendModeEnum.Mix
                    };
                    meshInstance.SetSurfaceOverrideMaterial(surface, material);
                }
            }
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
    }

    private static bool TryReadGeosetId(string nodeName, out int geosetId)
    {
        const string marker = "_Geoset_";
        geosetId = -1;
        var markerIndex = nodeName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return false;
        var valueStart = markerIndex + marker.Length;
        var valueEnd = nodeName.IndexOf('_', valueStart);
        if (valueEnd < 0) valueEnd = nodeName.Length;
        return int.TryParse(nodeName[valueStart..valueEnd], out geosetId);
    }

    private static void ApplyGeosetVisibility(
        IReadOnlyDictionary<int, List<Node3D>> geosetNodes,
        War3ModelMetadata metadata,
        int sequenceIndex,
        double localMilliseconds)
    {
        if (metadata.Sequences.Count == 0 || metadata.GeosetAnimations.Count == 0) return;
        var sequence = metadata.Sequences[Math.Clamp(sequenceIndex, 0, metadata.Sequences.Count - 1)];
        var frame = sequence.StartFrame + Math.Clamp(
            localMilliseconds, 0d, sequence.DurationMilliseconds);
        foreach (var geosetAnimation in metadata.GeosetAnimations)
        {
            if (!geosetNodes.TryGetValue(geosetAnimation.GeosetId, out var nodes)) continue;
            var visible = geosetAnimation.Alpha.Sample(
                frame, sequence, metadata.GlobalSequences) > 0.001d;
            var scale = visible ? Vector3.One : Vector3.Zero;
            foreach (var node in nodes) node.Scale = scale;
        }
    }

    private static bool IsGeosetVisible(
        IReadOnlyDictionary<int, List<Node3D>> geosetNodes,
        int geosetId)
    {
        return geosetNodes.TryGetValue(geosetId, out var nodes) &&
               nodes.Any(node => node.Scale.LengthSquared() > 0.0001f);
    }

    private static MeshInstance3D CreateGround()
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color("0f141a"),
            Roughness = 0.94f,
            Metallic = 0.04f
        };
        return new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(4f, 4f), Material = material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
    }

    private static MeshInstance3D CreateGrid()
    {
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color("53606d55")
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        const int half = 8;
        for (var index = -half; index <= half; index++)
        {
            var position = index / (float)half * 2f;
            mesh.SurfaceAddVertex(new Vector3(-2f, 0.003f, position));
            mesh.SurfaceAddVertex(new Vector3(2f, 0.003f, position));
            mesh.SurfaceAddVertex(new Vector3(position, 0.003f, -2f));
            mesh.SurfaceAddVertex(new Vector3(position, 0.003f, 2f));
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D
        {
            Name = "Grid",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
    }

    private static Node3D CreateOriginAxes()
    {
        var root = new Node3D { Name = "ModelOriginAxes" };
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        AddAxis(mesh, Vector3.Right, new Color("ef5b5b"), Vector3.Up);
        AddAxis(mesh, Vector3.Up, new Color("60d483"), Vector3.Right);
        AddAxis(mesh, Vector3.Back, new Color("5795f2"), Vector3.Up);
        mesh.SurfaceEnd();
        root.AddChild(new MeshInstance3D
        {
            Name = "Axes",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 1000f
        });
        root.AddChild(CreateAxisLabel("X", Vector3.Right * 1.14f, new Color("ef5b5b")));
        root.AddChild(CreateAxisLabel("Y", Vector3.Up * 1.14f, new Color("60d483")));
        root.AddChild(CreateAxisLabel("Z", Vector3.Back * 1.14f, new Color("5795f2")));
        return root;
    }

    private static void AddAxis(ImmediateMesh mesh, Vector3 direction, Color color, Vector3 wing)
    {
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(Vector3.Zero);
        mesh.SurfaceAddVertex(direction);
        mesh.SurfaceAddVertex(direction);
        mesh.SurfaceAddVertex(direction * 0.82f + wing * 0.07f);
        mesh.SurfaceAddVertex(direction);
        mesh.SurfaceAddVertex(direction * 0.82f - wing * 0.07f);
    }

    private static Label3D CreateAxisLabel(string text, Vector3 position, Color color) => new()
    {
        Name = $"Axis{text}",
        Text = text,
        Position = position,
        FontSize = 24,
        PixelSize = 0.003f,
        Modulate = color,
        OutlineModulate = new Color("090c10"),
        OutlineSize = 7,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true
    };

    private static string RaceLabel(string race) => race.ToLowerInvariant() switch
    {
        "human" => "人族",
        "orc" => "兽族",
        "undead" => "亡灵",
        "nightelf" => "暗夜精灵",
        "naga" => "娜迦",
        "demon" => "恶魔",
        "creeps" => "野怪",
        "critters" => "小动物",
        "commoner" => "中立",
        "other" => "其他",
        _ => "中立"
    };

    private static string NormalizeRace(string race) => race.ToLowerInvariant() switch
    {
        "commoner" => "neutral",
        _ => race.ToLowerInvariant()
    };

    private static string ShortSource(string source)
    {
        var segments = source.Replace('/', '\\').Split('\\');
        return segments.Length <= 3 ? source : $"…\\{string.Join('\\', segments[^3..])}";
    }

    private Texture2D? LoadCatalogIcon(War3AssetEntry entry)
    {
        if (entry.IconPath.Length == 0) return null;
        if (_iconCache.TryGetValue(entry.IconPath, out var cached)) return cached;

        var virtualPath = entry.IconPath.Replace('\\', '/').TrimStart('/');
        var extension = Path.GetExtension(virtualPath);
        if (extension.Length == 0 || extension.Equals(".blp", StringComparison.OrdinalIgnoreCase))
            virtualPath = Path.ChangeExtension(virtualPath, ".png");
        virtualPath = virtualPath.ToLowerInvariant();
        var absolutePath = War3AssetPack.AbsolutePath($"textures/{virtualPath}");
        if (!File.Exists(absolutePath))
        {
            _iconCache[entry.IconPath] = null;
            return null;
        }

        try
        {
            var image = Image.LoadFromFile(absolutePath);
            var texture = image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
            _iconCache[entry.IconPath] = texture;
            return texture;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Warcraft III icon failed: {entry.IconPath} ({exception.Message})");
            _iconCache[entry.IconPath] = null;
            return null;
        }
    }

    private static string FormatTime(double milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
    }

    private static PanelContainer NewPanel(string name, Color background)
    {
        var panel = new PanelContainer { Name = name };
        panel.AddThemeStyleboxOverride("panel", NewBox(background, Border, 0));
        return panel;
    }

    private static MarginContainer NewMargin(int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        return margin;
    }

    private static Label NewLabel(string value, int size, Color color)
    {
        var label = new Label { Text = value };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Label NewBadge(string value, Color color)
    {
        var label = NewLabel(value, 11, color);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.CustomMinimumSize = new Vector2(112f, 30f);
        label.AddThemeStyleboxOverride("normal", NewBox(new Color("1b2129"), Border, 5));
        return label;
    }

    private static Button NewButton(string value, float width)
    {
        var button = new Button
        {
            Text = value,
            CustomMinimumSize = new Vector2(width, 34f),
            FocusMode = Control.FocusModeEnum.All
        };
        StyleButton(button);
        return button;
    }

    private static void StyleButton(Button button, bool selected = false)
    {
        button.AddThemeFontSizeOverride("font_size", 12);
        button.AddThemeColorOverride("font_color", selected ? new Color("16110a") : Text);
        button.AddThemeColorOverride("font_hover_color", selected ? new Color("16110a") : Text);
        button.AddThemeColorOverride("font_pressed_color", new Color("16110a"));
        button.AddThemeColorOverride("font_focus_color", selected ? new Color("16110a") : Text);
        button.AddThemeStyleboxOverride("normal", NewBox(
            selected ? Accent : new Color("191f27"), selected ? Accent : Border, 5));
        button.AddThemeStyleboxOverride("hover", NewBox(
            selected ? new Color("d5aa61") : new Color("222a34"),
            selected ? new Color("d5aa61") : new Color("465463"), 5));
        button.AddThemeStyleboxOverride("pressed", NewBox(Accent, Accent, 5));
        button.AddThemeStyleboxOverride("focus", NewBox(Colors.Transparent, Accent, 5, 2));
    }

    private static void StyleSequenceButton(Button button, bool selected = false)
    {
        StyleButton(button, selected);
        button.AddThemeFontSizeOverride("font_size", 11);
        button.CustomMinimumSize = new Vector2(button.CustomMinimumSize.X, 30f);
    }

    private static void StyleLineEdit(LineEdit edit)
    {
        edit.AddThemeFontSizeOverride("font_size", 13);
        edit.AddThemeColorOverride("font_color", Text);
        edit.AddThemeColorOverride("font_placeholder_color", Muted);
        edit.AddThemeStyleboxOverride("normal", NewBox(new Color("0d1218"), Border, 5));
        edit.AddThemeStyleboxOverride("focus", NewBox(new Color("0d1218"), Accent, 5, 2));
    }

    private static void StyleItemList(ItemList list)
    {
        list.AddThemeFontSizeOverride("font_size", 12);
        list.AddThemeColorOverride("font_color", new Color("dce2e8"));
        list.AddThemeColorOverride("font_selected_color", Text);
        list.AddThemeConstantOverride("icon_margin", 8);
        list.AddThemeConstantOverride("line_separation", 2);
        list.AddThemeStyleboxOverride("panel", NewBox(new Color("0d1218"), Border, 5));
        list.AddThemeStyleboxOverride("hovered", NewBox(new Color("151c24"), Border, 4));
        list.AddThemeStyleboxOverride("selected", NewBox(new Color("44351f"), Accent, 4, 1));
        list.AddThemeStyleboxOverride("selected_focus", NewBox(new Color("44351f"), Accent, 4, 1));
    }

    private static HSeparator NewDivider()
    {
        var divider = new HSeparator();
        divider.AddThemeStyleboxOverride("separator", NewBox(Border, Border, 0));
        return divider;
    }

    private static StyleBoxFlat NewBox(
        Color background,
        Color border,
        int radius,
        int borderWidth = 1)
    {
        var box = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            ContentMarginLeft = 10f,
            ContentMarginRight = 10f,
            ContentMarginTop = 6f,
            ContentMarginBottom = 6f
        };
        return box;
    }

    private static void SetRect(
        Control control,
        float anchorLeft,
        float anchorTop,
        float anchorRight,
        float anchorBottom,
        float offsetLeft,
        float offsetTop,
        float offsetRight,
        float offsetBottom)
    {
        control.AnchorLeft = anchorLeft;
        control.AnchorTop = anchorTop;
        control.AnchorRight = anchorRight;
        control.AnchorBottom = anchorBottom;
        control.OffsetLeft = offsetLeft;
        control.OffsetTop = offsetTop;
        control.OffsetRight = offsetRight;
        control.OffsetBottom = offsetBottom;
    }
}
