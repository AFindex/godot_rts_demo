using Godot;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;

namespace War3Rts;

/// <summary>Warcraft Human operation console and live portrait viewport.</summary>
public sealed partial class War3RtsHud : Control
{
    public const float ConsoleHeight = 208f;
    private const float ConsoleChromeWidth = 1280f;
    private static readonly Color Ink = new("071019f2");
    private static readonly Color Surface = new("101923e8");
    private static readonly Color Raised = new("182431f2");
    private static readonly Color Border = new("765925");
    private static readonly Color Gold = new("e1b64e");
    private static readonly Color Text = new("f2ead5");
    private static readonly Color Muted = new("a9b2b7");
    private readonly Button[] _commandButtons = new Button[12];
    private readonly War3CommandSnapshot?[] _slotCommands =
        new War3CommandSnapshot?[12];
    private readonly Dictionary<Key, War3CommandSnapshot> _hotkeys = [];
    private Label? _goldValue;
    private Label? _lumberValue;
    private Label? _supplyValue;
    private Label? _clock;
    private Label? _selectionTitle;
    private Label? _selectionSubtitle;
    private Label? _healthText;
    private ProgressBar? _health;
    private Label? _queueLabel;
    private ProgressBar? _queue;
    private Label? _mode;
    private Label? _status;
    private GridContainer? _commandGrid;
    private War3MinimapControl? _minimap;
    private SubViewport? _portraitViewport;
    private Node3D? _portraitWorld;
    private Camera3D? _portraitCamera;
    private War3ModelActor? _portraitActor;
    private TextureRect? _portraitMask;
    private string _portraitSource = string.Empty;
    private string _commandSignature = string.Empty;
    private War3SelectionOverlay? _selectionOverlay;

    public event Action<War3CommandSnapshot>? CommandRequested;
    public event Action? ReturnRequested;
    public event Action<System.Numerics.Vector2>? MinimapFocusRequested;

    public bool PortraitReady => _portraitActor?.Loaded == true;

    public void SetDragSelection(Vector2 start, Vector2 end, bool visible) =>
        _selectionOverlay?.SetSelection(start, end, visible);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;
        CreateInterface();
        UpdateSnapshot(War3HudSnapshot.Empty);
    }

    public void UpdateSnapshot(War3HudSnapshot snapshot)
    {
        EnsureReady();
        _goldValue!.Text = snapshot.Gold.ToString();
        _lumberValue!.Text = snapshot.Lumber.ToString();
        _supplyValue!.Text = $"{snapshot.SupplyUsed}/{snapshot.SupplyCapacity}";
        _clock!.Text = FormatTime(snapshot.ElapsedSeconds);
        _selectionTitle!.Text = snapshot.Selection.Title;
        _selectionSubtitle!.Text = snapshot.Selection.Subtitle;
        _health!.MaxValue = Math.Max(1f, snapshot.Selection.MaximumHealth);
        _health.Value = Math.Clamp(snapshot.Selection.Health, 0f, (float)_health.MaxValue);
        _healthText!.Text = snapshot.Selection.MaximumHealth > 0f
            ? $"{snapshot.Selection.Health:0} / {snapshot.Selection.MaximumHealth:0}"
            : string.Empty;
        _queue!.Visible = snapshot.Selection.QueueLabel.Length > 0;
        _queueLabel!.Visible = _queue.Visible;
        _queue!.Value = Math.Clamp(snapshot.Selection.QueueProgress, 0f, 1f) * 100f;
        _queueLabel!.Text = snapshot.Selection.QueueLabel;
        _mode!.Text = snapshot.Mode;
        _status!.Text = snapshot.Status;
        UpdatePortrait(snapshot.Selection);
        RebuildCommands(snapshot.Commands);
        _minimap!.SetSnapshot(
            snapshot.WorldBounds, snapshot.Entities, snapshot.Resources);
    }

    public bool TryInvokeHotkey(Key key)
    {
        if (!_hotkeys.TryGetValue(key, out var command) || !command.Enabled)
            return false;
        CommandRequested?.Invoke(command);
        return true;
    }

    public bool BlocksWorldPointer(Vector2 viewportPosition)
    {
        var size = GetViewportRect().Size;
        return viewportPosition.Y >= size.Y - ConsoleHeight ||
               viewportPosition.Y <= 58f && viewportPosition.X >= size.X - 470f;
    }

    private void CreateInterface()
    {
        _selectionOverlay = new War3SelectionOverlay
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        _selectionOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_selectionOverlay);
        AddTopBar();
        AddStatusStrip(this);
        AddBottomConsole();
    }

    private void AddTopBar()
    {
        var top = new PanelContainer
        {
            Name = "ResourceBar",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -466f,
            OffsetRight = -12f,
            OffsetTop = 10f,
            OffsetBottom = 55f,
            MouseFilter = MouseFilterEnum.Stop
        };
        top.AddThemeStyleboxOverride("panel", Box(Ink, new Color("4d5660"), 5, 1));
        AddChild(top);
        var margin = Margin(12, 5, 8, 5);
        top.AddChild(margin);
        var row = HBox(9);
        margin.AddChild(row);
        _goldValue = AddResource(row,
            @"UI\Feedback\Resources\ResourceGold.blp", "0");
        _lumberValue = AddResource(row,
            @"UI\Feedback\Resources\ResourceLumber.blp", "0");
        _supplyValue = AddResource(row,
            @"UI\Feedback\Resources\ResourceSupply.blp", "0/0");
        _clock = LabelText("00:00", 14, Muted);
        _clock.CustomMinimumSize = new Vector2(64f, 30f);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_clock);
        var exit = ButtonControl("返回", 62, 30);
        exit.TooltipText = "返回项目入口";
        exit.Pressed += () => ReturnRequested?.Invoke();
        row.AddChild(exit);
    }

    private void AddBottomConsole()
    {
        var bottom = new Control
        {
            Name = "HumanConsole",
            AnchorTop = 1f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetTop = -ConsoleHeight,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(bottom);

        var underlay = new ColorRect
        {
            Color = new Color("05090d"),
            MouseFilter = MouseFilterEnum.Ignore
        };
        underlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bottom.AddChild(underlay);

        var chrome = new Control
        {
            Name = "HumanConsoleChrome",
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 1f,
            OffsetLeft = -ConsoleChromeWidth / 2f,
            OffsetRight = ConsoleChromeWidth / 2f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        bottom.AddChild(chrome);
        for (var index = 0; index < 4; index++)
        {
            var tile = new TextureRect
            {
                Name = $"HumanUiTile{index + 1}",
                Texture = War3RuntimeAssets.LoadTexture(
                    $@"UI\Console\Human\HumanUITile0{index + 1}.blp"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                AnchorLeft = index * 0.25f,
                AnchorRight = (index + 1) * 0.25f,
                OffsetTop = -112f,
                OffsetBottom = ConsoleHeight,
                MouseFilter = MouseFilterEnum.Ignore,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear
            };
            chrome.AddChild(tile);
        }

        AddMinimap(chrome);
        AddPortrait(chrome);
        AddSelectionInfo(chrome);
        AddCommandCard(chrome);
    }

    private void AddMinimap(Control parent)
    {
        var frame = new PanelContainer
        {
            Position = new Vector2(13f, 22f),
            Size = new Vector2(181f, 173f),
            MouseFilter = MouseFilterEnum.Stop
        };
        frame.AddThemeStyleboxOverride("panel", Box(
            new Color("07110ddd"), new Color("8e6a24"), 2, 2));
        parent.AddChild(frame);
        _minimap = new War3MinimapControl
        {
            MouseFilter = MouseFilterEnum.Stop
        };
        _minimap.FocusRequested += point => MinimapFocusRequested?.Invoke(point);
        frame.AddChild(_minimap);
    }

    private void AddPortrait(Control parent)
    {
        var opening = new SubViewportContainer
        {
            Name = "PortraitOpening",
            Position = new Vector2(270f, 36f),
            Size = new Vector2(126f, 151f),
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        parent.AddChild(opening);
        _portraitViewport = new SubViewport
        {
            Name = "PortraitViewport",
            Size = new Vector2I(252, 302),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
            TransparentBg = false,
            OwnWorld3D = true
        };
        opening.AddChild(_portraitViewport);
        _portraitWorld = new Node3D { Name = "PortraitWorld" };
        _portraitViewport.AddChild(_portraitWorld);
        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("050708"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("8aa5ba"),
                AmbientLightEnergy = 0.8f
            }
        };
        _portraitWorld.AddChild(environment);
        _portraitWorld.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28f, -35f, 0f),
            LightColor = new Color("ffe3b3"),
            LightEnergy = 1.35f
        });
        _portraitCamera = new Camera3D
        {
            Name = "PortraitCamera",
            Current = true,
            Near = 0.005f,
            Far = 100f
        };
        _portraitWorld.AddChild(_portraitCamera);

        _portraitMask = new TextureRect
        {
            Name = "PortraitMask",
            Position = new Vector2(255f, 20f),
            Size = new Vector2(157f, 181f),
            Texture = War3RuntimeAssets.LoadTexture(
                @"UI\Console\Human\HumanUIPortraitMask.blp"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        parent.AddChild(_portraitMask);
    }

    private void AddSelectionInfo(Control parent)
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(412f, 39f),
            Size = new Vector2(350f, 148f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddThemeStyleboxOverride("panel", Box(
            new Color("071019c9"), new Color("36424a"), 3, 1));
        parent.AddChild(panel);
        var margin = Margin(13, 7, 13, 7);
        panel.AddChild(margin);
        var column = VBox(2);
        margin.AddChild(column);
        _selectionTitle = LabelText("未选择单位", 20, Text);
        _selectionTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_selectionTitle);
        _selectionSubtitle = LabelText("", 13, Muted);
        column.AddChild(_selectionSubtitle);
        _health = new ProgressBar
        {
            CustomMinimumSize = new Vector2(320f, 17f),
            ShowPercentage = false,
            MaxValue = 1
        };
        _health.AddThemeStyleboxOverride("background", Box(
            new Color("06100a"), new Color("27342c"), 2, 1));
        _health.AddThemeStyleboxOverride("fill", Box(
            new Color("3c9d50"), new Color("62c36f"), 2, 1));
        column.AddChild(_health);
        _healthText = LabelText("", 11, new Color("cde7cb"));
        _healthText.HorizontalAlignment = HorizontalAlignment.Center;
        _healthText.VerticalAlignment = VerticalAlignment.Center;
        _healthText.MouseFilter = MouseFilterEnum.Ignore;
        _healthText.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _health.AddChild(_healthText);
        _queueLabel = LabelText("", 12, Gold);
        column.AddChild(_queueLabel);
        _queue = new ProgressBar
        {
            CustomMinimumSize = new Vector2(320f, 10f),
            ShowPercentage = false,
            MaxValue = 100
        };
        _queue.AddThemeStyleboxOverride("background", Box(
            new Color("10151a"), new Color("303b43"), 1, 1));
        _queue.AddThemeStyleboxOverride("fill", Box(
            new Color("b68732"), new Color("e1b64e"), 1, 1));
        column.AddChild(_queue);
    }

    private void AddCommandCard(Control parent)
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -258f,
            OffsetRight = -18f,
            OffsetTop = 14f,
            OffsetBottom = 195f,
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddThemeStyleboxOverride("panel", Box(
            new Color("070b10ba"), new Color("58451f"), 3, 1));
        parent.AddChild(panel);
        var margin = Margin(8, 7, 8, 7);
        panel.AddChild(margin);
        _commandGrid = new GridContainer
        {
            Columns = 4,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _commandGrid.AddThemeConstantOverride("h_separation", 6);
        _commandGrid.AddThemeConstantOverride("v_separation", 5);
        margin.AddChild(_commandGrid);
        for (var index = 0; index < _commandButtons.Length; index++)
        {
            var button = CommandButton();
            _commandButtons[index] = button;
            var slot = index;
            button.Pressed += () =>
            {
                if (_slotCommands[slot] is { Enabled: true } command)
                    CommandRequested?.Invoke(command);
            };
            _commandGrid.AddChild(button);
        }
    }

    private void AddStatusStrip(Control parent)
    {
        var strip = new PanelContainer
        {
            OffsetLeft = 12f,
            OffsetRight = 502f,
            OffsetTop = 10f,
            OffsetBottom = 55f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        strip.AddThemeStyleboxOverride("panel", Box(
            new Color("071019d9"), new Color("4b5860"), 4, 1));
        parent.AddChild(strip);
        var margin = Margin(12, 5, 10, 5);
        strip.AddChild(margin);
        var row = HBox(12);
        margin.AddChild(row);
        _mode = LabelText("普通选择", 13, Gold);
        _mode.CustomMinimumSize = new Vector2(120f, 30f);
        _mode.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_mode);
        _status = LabelText("就绪", 13, Text);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_status);
    }

    private void UpdatePortrait(War3SelectionSnapshot selection)
    {
        if (_portraitWorld is null || _portraitCamera is null) return;
        if (selection.PortraitSource == _portraitSource) return;
        _portraitSource = selection.PortraitSource;
        _portraitActor?.QueueFree();
        _portraitActor = null;
        if (_portraitSource.Length == 0 || !War3RuntimeAssets.Contains(_portraitSource))
            return;
        try
        {
            _portraitActor = new War3ModelActor { Name = "SelectedPortrait" };
            _portraitWorld.AddChild(_portraitActor);
            _portraitActor.Load(_portraitSource, _portraitCamera,
                War3HumanScenario.PlayerId, includeEffects: false);
            _portraitActor.PlayPreferred(true,
                selection.PortraitUsesOriginalCamera ? "Portrait" : "Stand",
                "Stand");
            _portraitActor.FrameCamera(
                _portraitCamera, selection.PortraitUsesOriginalCamera);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"war3_rts portrait failed: {_portraitSource} ({exception.Message})");
            _portraitActor?.QueueFree();
            _portraitActor = null;
        }
    }

    private void RebuildCommands(IReadOnlyList<War3CommandSnapshot> commands)
    {
        var signature = string.Join(';', commands.Select(value =>
            $"{value.Slot}:{value.Kind}:{value.DataId}:{value.Enabled}"));
        if (signature == _commandSignature) return;
        _commandSignature = signature;
        _hotkeys.Clear();
        for (var index = 0; index < _commandButtons.Length; index++)
        {
            var button = _commandButtons[index];
            foreach (var child in button.GetChildren()) child.QueueFree();
            button.Visible = false;
            button.Disabled = true;
            _slotCommands[index] = null;
        }
        foreach (var command in commands.Where(value =>
                     (uint)value.Slot < (uint)_commandButtons.Length))
        {
            var button = _commandButtons[command.Slot];
            button.Visible = true;
            button.Disabled = !command.Enabled;
            button.TooltipText = command.Tooltip;
            button.Icon = War3RuntimeAssets.LoadTexture(command.IconPath);
            _slotCommands[command.Slot] = command;
            var hotkey = LabelText(command.Hotkey, 11, Gold);
            hotkey.SetAnchorsPreset(LayoutPreset.TopLeft);
            hotkey.OffsetLeft = 3f;
            hotkey.OffsetTop = 1f;
            hotkey.MouseFilter = MouseFilterEnum.Ignore;
            button.AddChild(hotkey);
            if (TryParseHotkey(command.Hotkey, out var key)) _hotkeys[key] = command;
        }
    }

    private static bool TryParseHotkey(string value, out Key key)
    {
        key = Key.None;
        if (value.Length != 1) return false;
        key = value[0] switch
        {
            >= 'A' and <= 'Z' => (Key)((long)Key.A + value[0] - 'A'),
            >= '0' and <= '9' => (Key)((long)Key.Key0 + value[0] - '0'),
            _ => Key.None
        };
        return key != Key.None;
    }

    private Label AddResource(HBoxContainer row, string iconPath, string value)
    {
        var icon = new TextureRect
        {
            Texture = War3RuntimeAssets.LoadTexture(iconPath),
            CustomMinimumSize = new Vector2(27f, 27f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddChild(icon);
        var label = LabelText(value, 16, Text);
        label.CustomMinimumSize = new Vector2(54f, 30f);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        return label;
    }

    private static Button CommandButton()
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(51f, 51f),
            FocusMode = FocusModeEnum.None,
            ExpandIcon = true,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        button.AddThemeConstantOverride("icon_max_width", 46);
        button.AddThemeStyleboxOverride("normal", Box(
            new Color("111820e6"), new Color("7b5b20"), 2, 2));
        button.AddThemeStyleboxOverride("hover", Box(
            new Color("202b35f2"), new Color("d0a53c"), 2, 2));
        button.AddThemeStyleboxOverride("pressed", Box(
            new Color("090e13f2"), new Color("f1c95c"), 2, 2));
        button.AddThemeStyleboxOverride("disabled", Box(
            new Color("0b0f13c4"), new Color("3d4244"), 2, 1));
        return button;
    }

    private static Button ButtonControl(string text, float width, float height)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, height),
            FocusMode = FocusModeEnum.None
        };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.AddThemeStyleboxOverride("normal", Box(Surface, Border, 3, 1));
        button.AddThemeStyleboxOverride("hover", Box(Raised, Gold, 3, 1));
        button.AddThemeStyleboxOverride("pressed", Box(Ink, Gold, 3, 1));
        return button;
    }

    private static Label LabelText(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color("000000c0"));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private static HBoxContainer HBox(int separation)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", separation);
        return row;
    }

    private static VBoxContainer VBox(int separation)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        return column;
    }

    private static MarginContainer Margin(int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        return margin;
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

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }

    private void EnsureReady()
    {
        if (_goldValue is null) CreateInterface();
    }

    private sealed partial class War3MinimapControl : Control
    {
        private SimRect _bounds;
        private War3MinimapEntity[] _entities = [];
        private War3MinimapResource[] _resources = [];

        public event Action<System.Numerics.Vector2>? FocusRequested;

        public override void _Ready()
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            MouseDefaultCursorShape = CursorShape.Cross;
        }

        public void SetSnapshot(
            SimRect bounds,
            War3MinimapEntity[] entities,
            War3MinimapResource[] resources)
        {
            _bounds = bounds;
            _entities = entities;
            _resources = resources;
            QueueRedraw();
        }

        public override void _GuiInput(InputEvent inputEvent)
        {
            if (inputEvent is not InputEventMouseButton
                { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
                return;
            var normalized = new Vector2(
                Math.Clamp(mouse.Position.X / Math.Max(1f, Size.X), 0f, 1f),
                Math.Clamp(mouse.Position.Y / Math.Max(1f, Size.Y), 0f, 1f));
            FocusRequested?.Invoke(new System.Numerics.Vector2(
                Mathf.Lerp(_bounds.Min.X, _bounds.Max.X, normalized.X),
                Mathf.Lerp(_bounds.Min.Y, _bounds.Max.Y, normalized.Y)));
            AcceptEvent();
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color("0b1914"), true);
            for (var x = 0; x <= 4; x++)
                DrawLine(new Vector2(Size.X * x / 4f, 0f),
                    new Vector2(Size.X * x / 4f, Size.Y), new Color("23362e88"), 1f);
            for (var y = 0; y <= 4; y++)
                DrawLine(new Vector2(0f, Size.Y * y / 4f),
                    new Vector2(Size.X, Size.Y * y / 4f), new Color("23362e88"), 1f);
            foreach (var resource in _resources.Where(value => !value.Depleted))
            {
                var point = ToMap(resource.Position);
                DrawCircle(point, resource.Kind == RtsDemo.Simulation.EconomyResourceKind.Minerals
                    ? 3.2f : 1.8f,
                    resource.Kind == RtsDemo.Simulation.EconomyResourceKind.Minerals
                        ? new Color("efc74f") : new Color("4d9a5c"));
            }
            foreach (var entity in _entities)
            {
                var color = entity.Team == War3HumanScenario.PlayerId
                    ? new Color("42bff5")
                    : new Color("ef5f5f");
                var point = ToMap(entity.Position);
                if (entity.Building)
                    DrawRect(new Rect2(
                        point - new Vector2(2.8f, 2.8f),
                        new Vector2(5.6f, 5.6f)), color, true);
                else
                    DrawCircle(point, 2f, color);
            }
        }

        private Vector2 ToMap(System.Numerics.Vector2 point)
        {
            var width = Math.Max(1f, _bounds.Width);
            var height = Math.Max(1f, _bounds.Height);
            return new Vector2(
                (point.X - _bounds.Min.X) / width * Size.X,
                (point.Y - _bounds.Min.Y) / height * Size.Y);
        }
    }

    private sealed partial class War3SelectionOverlay : Control
    {
        private Vector2 _start;
        private Vector2 _end;
        private bool _visible;

        public void SetSelection(Vector2 start, Vector2 end, bool visible)
        {
            _start = start;
            _end = end;
            _visible = visible;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (!_visible) return;
            var rect = new Rect2(_start, _end - _start).Abs();
            DrawRect(rect, new Color("4ad4ff28"), true);
            DrawRect(rect, new Color("63ddff"), false, 1.5f);
        }
    }
}
