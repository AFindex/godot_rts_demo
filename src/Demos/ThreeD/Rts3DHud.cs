using Godot;
using RtsDemo.GodotRuntime;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// SC-style, presentation-only HUD. The node owns layout and visual state but
/// knows neither RtsSimulation nor gameplay catalogs.
/// </summary>
public partial class Rts3DHud : CanvasLayer
{
    private const int CommandSlotCount = Rts3DCommandLayout.SlotCount;
    private readonly Button[] _commandButtons = new Button[CommandSlotCount];
    private readonly CommandCardActionSnapshot?[] _commandActions =
        new CommandCardActionSnapshot?[CommandSlotCount];
    private readonly string[] _commandHotkeys = new string[CommandSlotCount];
    private readonly Button[] _controlGroupButtons = new Button[10];
    private readonly List<Button> _subgroupButtons = [];

    private Label? _mineralsLabel;
    private Label? _gasLabel;
    private Label? _supplyLabel;
    private Label? _timeLabel;
    private Button? _idleWorkerButton;
    private Label? _selectionTitle;
    private Label? _selectionSubtitle;
    private Label? _healthLabel;
    private ProgressBar? _healthBar;
    private HBoxContainer? _subgroupRow;
    private Label? _rallyLabel;
    private Label? _queueLabel;
    private Label? _commandTitle;
    private Label? _modeLabel;
    private Label? _statusLabel;
    private Label? _targetHintLabel;
    private Button? _debugButton;
    private HudOverlay? _overlay;
    private RtsMinimapControl? _minimap;
    private Rts3DHudSnapshot _snapshot = Rts3DHudSnapshot.Empty;
    private bool _debugEnabled;
    private int _subgroupSignature = int.MinValue;
    private int _commandSignature = int.MinValue;
    private int _controlGroupSignature = int.MinValue;

    public event Action<CommandCardActionSnapshot>? ActionRequested;
    public event Action<int>? SubgroupRequested;
    public event Action<int>? ControlGroupRecallRequested;
    public event Action? IdleWorkerRequested;
    public event Action? FocusRequested;
    public event Action? ToggleDebugRequested;
    public event Action? Return2DRequested;
    public event Action<NVector2>? MinimapFocusRequested;
    public event Action<NVector2>? MinimapSmartCommandRequested;

    public override void _Ready()
    {
        Layer = 20;
        EnsureReady();
    }

    public void UpdateSnapshot(Rts3DHudSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureReady();
        _snapshot = snapshot;
        _mineralsLabel!.Text = $"◆  {snapshot.Minerals:N0}";
        _gasLabel!.Text = $"⬢  {snapshot.Gas:N0}";
        _supplyLabel!.Text = $"▰  {snapshot.SupplyUsed}/{snapshot.SupplyCapacity}";
        _timeLabel!.Text = FormatTime(snapshot.ElapsedSeconds);
        _idleWorkerButton!.Text = snapshot.IdleWorkers > 0
            ? $"[F1] IDLE  {snapshot.IdleWorkers}"
            : "[F1] IDLE  0";
        _idleWorkerButton.Disabled = snapshot.IdleWorkers == 0;
        UpdateSelection(snapshot.Selection);
        UpdateCommandCard(snapshot.CommandSlots);
        UpdateControlGroups(snapshot.ControlGroups);
        SetMode(snapshot.Mode);
        SetStatus(snapshot.Status);
    }

    public bool TryInvokeShortcut(Key key)
    {
        EnsureReady();
        var token = KeyToken(key);
        if (token.Length == 0) return false;
        for (var slot = 0; slot < CommandSlotCount; slot++)
        {
            var action = _commandActions[slot];
            if (action is not null && action.Enabled &&
                string.Equals(_commandHotkeys[slot], token,
                    StringComparison.OrdinalIgnoreCase))
            {
                ActionRequested?.Invoke(action);
                return true;
            }
        }
        return false;
    }

    public void SetMode(string? value)
    {
        EnsureReady();
        _modeLabel!.Text = string.IsNullOrWhiteSpace(value)
            ? "NORMAL"
            : value.ToUpperInvariant();
    }

    public void SetStatus(string? value)
    {
        EnsureReady();
        _statusLabel!.Text = string.IsNullOrWhiteSpace(value) ? "Ready" : value;
    }

    public void SetTargetMode(Rts3DTargetModeSnapshot? target)
    {
        EnsureReady();
        _targetHintLabel!.Visible = target.HasValue;
        _targetHintLabel.Text = target.HasValue
            ? $"{target.Value.Label}  ·  {target.Value.Hint}"
            : string.Empty;
        _overlay!.TargetMode = target;
    }

    public void SetDragSelection(Vector2 start, Vector2 end, bool visible)
    {
        EnsureReady();
        _overlay!.SetDragSelection(start, end, visible);
    }

    public void ShowCommandMarker(
        Vector2 projectedWorldPoint,
        Rts3DCommandMarkerKind kind = Rts3DCommandMarkerKind.Move)
    {
        EnsureReady();
        _overlay!.ShowCommandMarker(projectedWorldPoint, kind);
    }

    public void SetDebugEnabled(bool enabled)
    {
        EnsureReady();
        _debugEnabled = enabled;
        _debugButton!.Text = enabled ? "[D] PATH ON" : "[D] PATH";
        _debugButton.ButtonPressed = enabled;
    }

    public void SetMinimapSnapshot(MinimapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureReady();
        _minimap!.SetSnapshot(snapshot);
    }

    /// <summary>Used by the camera to suppress edge-scroll over HUD chrome.</summary>
    public bool BlocksWorldPointer(Vector2 viewportPosition)
    {
        var size = GetViewport().GetVisibleRect().Size;
        return viewportPosition.Y >= size.Y - 218f ||
               viewportPosition.Y <= 56f &&
               (viewportPosition.X <= 405f || viewportPosition.X >= size.X - 535f);
    }

    private void CreateInterface()
    {
        var root = new Control
        {
            Name = "HudRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _overlay = new HudOverlay { Name = "InteractionOverlay" };
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_overlay);

        CreateResourceBar(root);
        CreateUtilityBar(root);
        CreateBottomConsole(root);

        _targetHintLabel = LabelText(15, new Color("ffe07a"));
        _targetHintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _targetHintLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _targetHintLabel.Position = new Vector2(-330f, -304f);
        _targetHintLabel.CustomMinimumSize = new Vector2(660f, 28f);
        _targetHintLabel.Visible = false;
        root.AddChild(_targetHintLabel);

        var feedback = Frame(new Color("07111be8"), new Color("3b738c"));
        feedback.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        feedback.Position = new Vector2(-310f, -268f);
        feedback.CustomMinimumSize = new Vector2(620f, 42f);
        root.AddChild(feedback);
        var feedbackBody = new HBoxContainer();
        feedbackBody.AddThemeConstantOverride("separation", 14);
        feedback.AddChild(feedbackBody);
        _modeLabel = LabelText(12, new Color("63d6ff"));
        _modeLabel.CustomMinimumSize = new Vector2(180f, 38f);
        _modeLabel.VerticalAlignment = VerticalAlignment.Center;
        _modeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel = LabelText(13, new Color("e4edf4"));
        _statusLabel.VerticalAlignment = VerticalAlignment.Center;
        _statusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        feedbackBody.AddChild(_modeLabel);
        feedbackBody.AddChild(_statusLabel);
    }

    private void CreateResourceBar(Control root)
    {
        var panel = Frame(new Color("07111bee"), new Color("37647c"));
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.Position = new Vector2(-535f, 12f);
        panel.CustomMinimumSize = new Vector2(523f, 42f);
        root.AddChild(panel);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);
        _mineralsLabel = ResourceLabel(new Color("72cfff"));
        _gasLabel = ResourceLabel(new Color("7ff2b5"));
        _supplyLabel = ResourceLabel(new Color("ffd27b"));
        _timeLabel = ResourceLabel(new Color("b7c9d5"));
        row.AddChild(_mineralsLabel);
        row.AddChild(_gasLabel);
        row.AddChild(_supplyLabel);
        row.AddChild(_timeLabel);
    }

    private void CreateUtilityBar(Control root)
    {
        var row = new HBoxContainer
        {
            Position = new Vector2(12f, 12f)
        };
        row.AddThemeConstantOverride("separation", 5);
        root.AddChild(row);
        _idleWorkerButton = SmallButton("[F1] IDLE  0");
        _idleWorkerButton.Pressed += () => IdleWorkerRequested?.Invoke();
        row.AddChild(_idleWorkerButton);
        var focus = SmallButton("[F] FOCUS");
        focus.Pressed += () => FocusRequested?.Invoke();
        row.AddChild(focus);
        _debugButton = SmallButton("[D] PATH");
        _debugButton.ToggleMode = true;
        _debugButton.Pressed += () =>
        {
            _debugEnabled = !_debugEnabled;
            SetDebugEnabled(_debugEnabled);
            ToggleDebugRequested?.Invoke();
        };
        row.AddChild(_debugButton);
        var return2D = SmallButton("2D DEMO");
        return2D.Pressed += () => Return2DRequested?.Invoke();
        row.AddChild(return2D);
    }

    private void CreateBottomConsole(Control root)
    {
        var console = Frame(new Color("040b12f5"), new Color("31566b"));
        console.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        console.OffsetLeft = 8f;
        console.OffsetTop = -218f;
        console.OffsetRight = -8f;
        console.OffsetBottom = -8f;
        root.AddChild(console);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 7);
        console.AddChild(row);
        CreateMinimapPanel(row);
        CreateSelectionPanel(row);
        CreateCommandPanel(row);
    }

    private void CreateMinimapPanel(HBoxContainer parent)
    {
        var panel = InnerPanel(new Color("08131d"));
        panel.CustomMinimumSize = new Vector2(258f, 198f);
        parent.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        panel.AddChild(body);
        _minimap = new RtsMinimapControl
        {
            Name = "Minimap",
            CustomMinimumSize = new Vector2(246f, 158f)
        };
        _minimap.FocusRequested += point => MinimapFocusRequested?.Invoke(point);
        _minimap.SmartCommandRequested += point =>
            MinimapSmartCommandRequested?.Invoke(point);
        body.AddChild(_minimap);
        var groups = new HBoxContainer();
        groups.AddThemeConstantOverride("separation", 2);
        body.AddChild(groups);
        for (var group = 0; group < _controlGroupButtons.Length; group++)
        {
            var captured = group;
            var button = SmallButton(group.ToString());
            button.CustomMinimumSize = new Vector2(22f, 28f);
            button.AddThemeFontSizeOverride("font_size", 10);
            button.Pressed += () => ControlGroupRecallRequested?.Invoke(captured);
            _controlGroupButtons[group] = button;
            groups.AddChild(button);
        }
    }

    private void CreateSelectionPanel(HBoxContainer parent)
    {
        var panel = InnerPanel(new Color("08131d"));
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.CustomMinimumSize = new Vector2(430f, 198f);
        parent.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        panel.AddChild(body);

        var heading = new HBoxContainer();
        body.AddChild(heading);
        var titleStack = new VBoxContainer();
        titleStack.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        heading.AddChild(titleStack);
        _selectionTitle = LabelText(18, new Color("ecf8ff"));
        _selectionSubtitle = LabelText(11, new Color("7f9dad"));
        titleStack.AddChild(_selectionTitle);
        titleStack.AddChild(_selectionSubtitle);
        _healthLabel = LabelText(12, new Color("82f0a8"));
        _healthLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _healthLabel.CustomMinimumSize = new Vector2(120f, 34f);
        heading.AddChild(_healthLabel);

        _healthBar = new ProgressBar
        {
            MinValue = 0d,
            MaxValue = 1d,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0f, 10f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _healthBar.AddThemeStyleboxOverride("background",
            Flat(new Color("19252d"), new Color("273d49"), 1));
        _healthBar.AddThemeStyleboxOverride("fill",
            Flat(new Color("35c96f"), new Color("69f39b"), 1));
        body.AddChild(_healthBar);

        _subgroupRow = new HBoxContainer();
        _subgroupRow.AddThemeConstantOverride("separation", 4);
        body.AddChild(_subgroupRow);
        _rallyLabel = LabelText(12, new Color("f6cf65"));
        body.AddChild(_rallyLabel);
        _queueLabel = LabelText(11, new Color("b8cfdd"));
        _queueLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _queueLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(_queueLabel);
    }

    private void CreateCommandPanel(HBoxContainer parent)
    {
        var panel = InnerPanel(new Color("08131d"));
        panel.CustomMinimumSize = new Vector2(374f, 198f);
        parent.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        panel.AddChild(body);
        _commandTitle = LabelText(12, new Color("70c8e8"));
        _commandTitle.Text = "COMMAND CARD";
        _commandTitle.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(_commandTitle);
        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        body.AddChild(grid);
        for (var slot = 0; slot < CommandSlotCount; slot++)
        {
            var captured = slot;
            var button = CommandButton();
            button.Pressed += () => EmitCommand(captured);
            _commandButtons[slot] = button;
            _commandHotkeys[slot] = string.Empty;
            grid.AddChild(button);
        }
    }

    private void UpdateSelection(Rts3DSelectionPanelSnapshot selection)
    {
        _selectionTitle!.Text = selection.Title;
        _selectionSubtitle!.Text = selection.Subtitle;
        var validHealth = selection.MaximumHealth > 0f;
        _healthBar!.Value = validHealth
            ? Math.Clamp(selection.Health / selection.MaximumHealth, 0f, 1f)
            : 0d;
        _healthLabel!.Text = validHealth
            ? $"{MathF.Ceiling(selection.Health):0} / {selection.MaximumHealth:0}"
            : string.Empty;
        _rallyLabel!.Text = selection.Rally;
        _rallyLabel.Visible = !string.IsNullOrWhiteSpace(selection.Rally);
        _queueLabel!.Text = selection.Queue.Length == 0
            ? ""
            : "QUEUE  " + string.Join("   ", selection.Queue.Select(value =>
                $"{value.Label}{(value.Count > 1 ? $" ×{value.Count}" : "")} " +
                $"{value.Progress * 100f:0}%"));
        RebuildSubgroups(selection);
    }

    private void RebuildSubgroups(Rts3DSelectionPanelSnapshot selection)
    {
        var signature = selection.Subgroups.Aggregate(
            selection.ActiveSubgroupIndex,
            (hash, value) => HashCode.Combine(
                hash, value.Key, value.Name, value.Members.Length));
        if (signature == _subgroupSignature) return;
        _subgroupSignature = signature;
        foreach (var button in _subgroupButtons)
        {
            _subgroupRow!.RemoveChild(button);
            button.QueueFree();
        }
        _subgroupButtons.Clear();
        for (var index = 0; index < selection.Subgroups.Length; index++)
        {
            var captured = index;
            var subgroup = selection.Subgroups[index];
            var button = SmallButton($"{subgroup.Name} {subgroup.Members.Length}");
            button.CustomMinimumSize = new Vector2(92f, 27f);
            button.ButtonPressed = index == selection.ActiveSubgroupIndex;
            button.ToggleMode = true;
            button.Pressed += () => SubgroupRequested?.Invoke(captured);
            _subgroupButtons.Add(button);
            _subgroupRow!.AddChild(button);
        }
    }

    private void UpdateCommandCard(Rts3DCommandSlotSnapshot[] slots)
    {
        var signature = slots.Aggregate(17, (hash, value) => HashCode.Combine(
            hash, value.Slot, value.Hotkey, value.Action.Kind,
            value.Action.DataId, value.Action.Label, value.Action.Enabled,
            value.Action.Status));
        if (signature == _commandSignature) return;
        _commandSignature = signature;
        Array.Clear(_commandActions);
        Array.Fill(_commandHotkeys, string.Empty);
        for (var slot = 0; slot < CommandSlotCount; slot++)
        {
            var button = _commandButtons[slot];
            button.Text = string.Empty;
            button.TooltipText = string.Empty;
            button.Disabled = true;
        }
        foreach (var value in slots)
        {
            if ((uint)value.Slot >= CommandSlotCount) continue;
            _commandActions[value.Slot] = value.Action;
            _commandHotkeys[value.Slot] = value.Hotkey;
            var button = _commandButtons[value.Slot];
            button.Text = $"[{value.Hotkey}]  {value.Glyph}\n{value.Action.Label}";
            button.TooltipText = $"{value.Action.Label}\n{value.Action.Status}";
            button.Disabled = !value.Action.Enabled;
        }
        _commandTitle!.Text = slots.Length == 0
            ? "COMMAND CARD  ·  NO SELECTION"
            : "COMMAND CARD";
    }

    private void UpdateControlGroups(Rts3DControlGroupSnapshot[] groups)
    {
        var signature = groups.Aggregate(17, (hash, value) => HashCode.Combine(
            hash, value.Group, value.Units, value.Buildings));
        if (signature == _controlGroupSignature) return;
        _controlGroupSignature = signature;
        var byGroup = groups.ToDictionary(value => value.Group);
        for (var group = 0; group < _controlGroupButtons.Length; group++)
        {
            var button = _controlGroupButtons[group];
            if (byGroup.TryGetValue(group, out var snapshot) && snapshot.Count > 0)
            {
                button.Text = $"{group}\n{snapshot.Count}";
                button.TooltipText = $"Group {group}: {snapshot.Units} units, " +
                                     $"{snapshot.Buildings} buildings";
                button.Disabled = false;
            }
            else
            {
                button.Text = group.ToString();
                button.TooltipText = $"Control group {group} is empty";
                button.Disabled = true;
            }
        }
    }

    private void EmitCommand(int slot)
    {
        var action = _commandActions[slot];
        if (action is not null && action.Enabled)
            ActionRequested?.Invoke(action);
    }

    private void EnsureReady()
    {
        if (_mineralsLabel is null) CreateInterface();
    }

    private static PanelContainer Frame(Color background, Color border)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", Flat(background, border, 1));
        return panel;
    }

    private static PanelContainer InnerPanel(Color color)
    {
        return Frame(color, new Color("284a5d"));
    }

    private static StyleBoxFlat Flat(Color background, Color border, int width)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = width,
            BorderWidthTop = width,
            BorderWidthRight = width,
            BorderWidthBottom = width,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 7f,
            ContentMarginTop = 5f,
            ContentMarginRight = 7f,
            ContentMarginBottom = 5f
        };
        return style;
    }

    private static Label LabelText(int size, Color color)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color("000000dd"));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private static Label ResourceLabel(Color color)
    {
        var label = LabelText(15, color);
        label.CustomMinimumSize = new Vector2(122f, 30f);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    private static Button SmallButton(string text)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(92f, 30f),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        ApplyButtonStyle(button, compact: true);
        return button;
    }

    private static Button CommandButton()
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(68f, 48f),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        button.AddThemeFontSizeOverride("font_size", 9);
        ApplyButtonStyle(button, compact: false);
        return button;
    }

    private static void ApplyButtonStyle(Button button, bool compact)
    {
        button.AddThemeStyleboxOverride("normal",
            Flat(new Color("102532"), new Color("376176"), 1));
        button.AddThemeStyleboxOverride("hover",
            Flat(new Color("17445a"), new Color("62ccef"), 1));
        button.AddThemeStyleboxOverride("pressed",
            Flat(new Color("1b6079"), new Color("91e8ff"), 2));
        button.AddThemeStyleboxOverride("disabled",
            Flat(new Color("101820"), new Color("263540"), 1));
        button.AddThemeColorOverride("font_color", new Color("eaf7ff"));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color("61717c"));
        if (compact) button.AddThemeFontSizeOverride("font_size", 10);
    }

    private static void SetMargins(
        MarginContainer margin,
        int left,
        int top,
        int right,
        int bottom)
    {
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
    }

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    }

    private static string KeyToken(Key key) => key switch
    {
        Key.A => "A", Key.C => "C", Key.E => "E", Key.H => "H",
        Key.M => "M", Key.Q => "Q", Key.R => "R", Key.S => "S",
        Key.T => "T", Key.W => "W", Key.Y => "Y", _ => string.Empty
    };

    private sealed partial class HudOverlay : Control
    {
        private const double MarkerDuration = 0.52;
        private Vector2 _dragStart;
        private Vector2 _dragEnd;
        private Vector2 _markerPosition;
        private double _markerTime;
        private bool _dragVisible;
        private Rts3DTargetModeSnapshot? _targetMode;
        private Rts3DCommandMarkerKind _markerKind;

        public Rts3DTargetModeSnapshot? TargetMode
        {
            get => _targetMode;
            set
            {
                _targetMode = value;
                QueueRedraw();
            }
        }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetProcess(true);
        }

        public void SetDragSelection(Vector2 start, Vector2 end, bool visible)
        {
            _dragStart = start;
            _dragEnd = end;
            _dragVisible = visible;
            QueueRedraw();
        }

        public void ShowCommandMarker(Vector2 position, Rts3DCommandMarkerKind kind)
        {
            _markerPosition = position;
            _markerKind = kind;
            _markerTime = MarkerDuration;
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (_markerTime > 0d)
            {
                _markerTime = Math.Max(0d, _markerTime - delta);
                QueueRedraw();
            }
            if (_targetMode.HasValue) QueueRedraw();
        }

        public override void _Draw()
        {
            if (_dragVisible)
            {
                var rect = new Rect2(_dragStart, _dragEnd - _dragStart).Abs();
                DrawRect(rect, new Color(0.12f, 0.72f, 1f, 0.14f), true);
                DrawRect(rect, new Color("55cfff"), false, 1.5f);
            }
            if (_markerTime > 0d)
            {
                var progress = 1f - (float)(_markerTime / MarkerDuration);
                var color = MarkerColor(_markerKind) with { A = 1f - progress };
                var radius = 8f + progress * 20f;
                DrawArc(_markerPosition, radius, 0f, MathF.Tau, 28, color, 2.5f);
                DrawLine(_markerPosition - Vector2.One * 5f,
                    _markerPosition + Vector2.One * 5f, color, 2f);
                DrawLine(_markerPosition + new Vector2(-5f, 5f),
                    _markerPosition + new Vector2(5f, -5f), color, 2f);
            }
            if (_targetMode.HasValue)
            {
                var cursor = GetLocalMousePosition();
                var color = TargetColor(_targetMode.Value.Kind);
                DrawArc(cursor, 14f, 0f, MathF.Tau, 24, color, 2f);
                DrawLine(cursor - new Vector2(20f, 0f),
                    cursor - new Vector2(8f, 0f), color, 2f);
                DrawLine(cursor + new Vector2(8f, 0f),
                    cursor + new Vector2(20f, 0f), color, 2f);
                DrawLine(cursor - new Vector2(0f, 20f),
                    cursor - new Vector2(0f, 8f), color, 2f);
                DrawLine(cursor + new Vector2(0f, 8f),
                    cursor + new Vector2(0f, 20f), color, 2f);
            }
        }

        private static Color MarkerColor(Rts3DCommandMarkerKind kind) => kind switch
        {
            Rts3DCommandMarkerKind.Attack => new Color("ff5364"),
            Rts3DCommandMarkerKind.Interact => new Color("66e9a3"),
            Rts3DCommandMarkerKind.Build => new Color("65f58e"),
            Rts3DCommandMarkerKind.Rally => new Color("ffd35f"),
            _ => new Color("58d8ff")
        };

        private static Color TargetColor(TargetCommandKind kind) => kind switch
        {
            TargetCommandKind.AttackMove => new Color("ff5364"),
            TargetCommandKind.Rally => new Color("ffd35f"),
            TargetCommandKind.Build => new Color("65f58e"),
            _ => new Color("58d8ff")
        };
    }
}

public enum Rts3DCommandMarkerKind : byte
{
    Move,
    Attack,
    Interact,
    Build,
    Rally
}
