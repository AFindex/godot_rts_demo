using Godot;
using RtsDemo.GodotRuntime;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Presentation-only HUD for the 3D demo. The owner pushes immutable display
/// data in and translates the emitted intent events into gameplay commands.
/// </summary>
public partial class Rts3DHud : CanvasLayer
{
    private readonly Dictionary<Rts3DHudCommandGroup, HBoxContainer> _commandRows = [];
    private Label? _economyLabel;
    private Label? _selectionLabel;
    private Label? _enemyLabel;
    private Label? _modeLabel;
    private Label? _statusLabel;
    private Label? _buildHintLabel;
    private Button? _debugButton;
    private HudOverlay? _overlay;
    private RtsMinimapControl? _minimap;
    private bool _debugEnabled;

    public event Action? AttackMoveRequested;
    public event Action? StopRequested;
    public event Action? FocusRequested;
    public event Action<int>? BuildRequested;
    public event Action<int>? TrainRequested;
    public event Action<int>? ResearchRequested;
    public event Action? ToggleDebugRequested;
    public event Action? Return2DRequested;
    public event Action<NVector2>? MinimapFocusRequested;
    public event Action<NVector2>? MinimapSmartCommandRequested;

    public override void _Ready()
    {
        Layer = 20;
        if (_economyLabel is null)
        {
            CreateInterface();
        }
    }

    public void UpdateSnapshot(Rts3DHudSnapshot snapshot)
    {
        EnsureReady();
        _economyLabel!.Text =
            $"矿物  {snapshot.Minerals:N0}     气体  {snapshot.Gas:N0}     " +
            $"人口  {snapshot.SupplyUsed}/{snapshot.SupplyCapacity}     " +
            $"时间  {snapshot.ElapsedSeconds:0.0}s";
        _selectionLabel!.Text =
            $"选择  {snapshot.SelectedUnits} 单位 / {snapshot.SelectedBuildings} 建筑";
        _enemyLabel!.Text =
            $"敌方  {snapshot.EnemyWorkers} 农民 / {snapshot.EnemyCombatUnits} 军队 / " +
            $"{snapshot.EnemyBuildings} 建筑";
        SetMode(snapshot.Mode);
        SetStatus(snapshot.Status);
    }

    /// <summary>Replaces one command group without knowing any simulation catalog.</summary>
    public void SetCommandOptions(
        Rts3DHudCommandGroup group,
        IReadOnlyList<Rts3DHudCommandOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureReady();
        var row = _commandRows[group];
        foreach (var child in row.GetChildren())
        {
            row.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var option in options)
        {
            var capturedId = option.Id;
            var button = CreateCommandButton(
                string.IsNullOrWhiteSpace(option.Shortcut)
                    ? option.Label
                    : $"[{option.Shortcut}] {option.Label}");
            button.Disabled = !option.Enabled;
            button.TooltipText = option.Tooltip ?? string.Empty;
            button.Pressed += () => EmitCatalogIntent(group, capturedId);
            row.AddChild(button);
        }
    }

    public void SetMode(string? value)
    {
        EnsureReady();
        _modeLabel!.Text = string.IsNullOrWhiteSpace(value)
            ? "模式：普通选择"
            : $"模式：{value}";
    }

    public void SetStatus(string? value)
    {
        EnsureReady();
        _statusLabel!.Text = string.IsNullOrWhiteSpace(value)
            ? "状态：就绪"
            : $"状态：{value}";
    }

    public void SetBuildMode(string? buildingLabel)
    {
        EnsureReady();
        var active = !string.IsNullOrWhiteSpace(buildingLabel);
        _buildHintLabel!.Visible = active;
        _buildHintLabel.Text = active
            ? $"放置建筑：{buildingLabel}  ·  左键确认  ·  右键 / Esc 取消"
            : string.Empty;
        _overlay!.BuildModeActive = active;
    }

    public void SetDragSelection(Vector2 start, Vector2 end, bool visible)
    {
        EnsureReady();
        _overlay!.SetDragSelection(start, end, visible);
    }

    /// <summary>
    /// Shows a short screen-space pulse at a world point already projected by
    /// the owning camera. This keeps camera math outside the HUD.
    /// </summary>
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
        _debugButton!.Text = enabled ? "[D] 调试：开" : "[D] 调试：关";
        _debugButton.ButtonPressed = enabled;
    }

    public void SetMinimapSnapshot(MinimapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureReady();
        _minimap!.SetSnapshot(snapshot);
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

        var topPanel = CreatePanel(new Color(0.025f, 0.045f, 0.07f, 0.91f));
        topPanel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        topPanel.OffsetLeft = 14f;
        topPanel.OffsetTop = 12f;
        topPanel.OffsetRight = -14f;
        topPanel.OffsetBottom = 76f;
        root.AddChild(topPanel);

        var topMargin = new MarginContainer();
        SetMargins(topMargin, 14, 10, 14, 8);
        topPanel.AddChild(topMargin);
        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 3);
        topMargin.AddChild(top);
        _economyLabel = CreateLabel(17, new Color("eaf7ff"));
        var summaries = new HBoxContainer();
        summaries.AddThemeConstantOverride("separation", 30);
        _selectionLabel = CreateLabel(14, new Color("80dcff"));
        _enemyLabel = CreateLabel(14, new Color("ff9a9f"));
        summaries.AddChild(_selectionLabel);
        summaries.AddChild(_enemyLabel);
        top.AddChild(_economyLabel);
        top.AddChild(summaries);

        var statePanel = CreatePanel(new Color(0.025f, 0.045f, 0.07f, 0.88f));
        statePanel.Position = new Vector2(14f, 88f);
        statePanel.CustomMinimumSize = new Vector2(430f, 66f);
        root.AddChild(statePanel);
        var stateMargin = new MarginContainer();
        SetMargins(stateMargin, 12, 8, 12, 8);
        statePanel.AddChild(stateMargin);
        var state = new VBoxContainer();
        state.AddThemeConstantOverride("separation", 2);
        stateMargin.AddChild(state);
        _modeLabel = CreateLabel(14, new Color("79d8ff"));
        _statusLabel = CreateLabel(13, new Color("ffd37a"));
        state.AddChild(_modeLabel);
        state.AddChild(_statusLabel);

        _buildHintLabel = CreateLabel(16, new Color("ffe174"));
        _buildHintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _buildHintLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _buildHintLabel.Position = new Vector2(-300f, 166f);
        _buildHintLabel.CustomMinimumSize = new Vector2(600f, 34f);
        _buildHintLabel.Visible = false;
        root.AddChild(_buildHintLabel);

        CreateCommandCard(root);
        CreateMinimap(root);
        SetMode(null);
        SetStatus(null);
    }

    private void CreateCommandCard(Control root)
    {
        var panel = CreatePanel(new Color(0.025f, 0.045f, 0.07f, 0.94f));
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        panel.OffsetLeft = 14f;
        panel.OffsetTop = -206f;
        panel.OffsetRight = -264f;
        panel.OffsetBottom = -12f;
        root.AddChild(panel);

        var margin = new MarginContainer();
        SetMargins(margin, 12, 10, 12, 10);
        panel.AddChild(margin);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        margin.AddChild(body);

        var primary = new HBoxContainer();
        primary.AddThemeConstantOverride("separation", 6);
        primary.AddChild(IntentButton("[A] 攻击移动", () => AttackMoveRequested?.Invoke()));
        primary.AddChild(IntentButton("[S] 停止", () => StopRequested?.Invoke()));
        primary.AddChild(IntentButton("[F] 聚焦", () => FocusRequested?.Invoke()));
        _debugButton = IntentButton("[D] 调试：关", () =>
        {
            _debugEnabled = !_debugEnabled;
            SetDebugEnabled(_debugEnabled);
            ToggleDebugRequested?.Invoke();
        });
        _debugButton.ToggleMode = true;
        primary.AddChild(_debugButton);
        primary.AddChild(IntentButton("返回 2D", () => Return2DRequested?.Invoke()));
        body.AddChild(primary);

        AddCommandRow(body, Rts3DHudCommandGroup.Build, "建造");
        AddCommandRow(body, Rts3DHudCommandGroup.Train, "训练");
        AddCommandRow(body, Rts3DHudCommandGroup.Research, "科技");
    }

    private void AddCommandRow(
        VBoxContainer body,
        Rts3DHudCommandGroup group,
        string title)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        var heading = CreateLabel(13, new Color("8ca9bd"));
        heading.Text = title;
        heading.CustomMinimumSize = new Vector2(46f, 30f);
        heading.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(heading);
        _commandRows.Add(group, row);
        body.AddChild(row);
    }

    private void CreateMinimap(Control root)
    {
        _minimap = new RtsMinimapControl
        {
            Name = "Minimap",
            CustomMinimumSize = new Vector2(236f, 194f)
        };
        _minimap.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _minimap.Position = new Vector2(-250f, -206f);
        _minimap.FocusRequested += point => MinimapFocusRequested?.Invoke(point);
        _minimap.SmartCommandRequested += point =>
            MinimapSmartCommandRequested?.Invoke(point);
        root.AddChild(_minimap);
    }

    private void EmitCatalogIntent(Rts3DHudCommandGroup group, int id)
    {
        switch (group)
        {
            case Rts3DHudCommandGroup.Build:
                BuildRequested?.Invoke(id);
                break;
            case Rts3DHudCommandGroup.Train:
                TrainRequested?.Invoke(id);
                break;
            case Rts3DHudCommandGroup.Research:
                ResearchRequested?.Invoke(id);
                break;
        }
    }

    private void EnsureReady()
    {
        if (_economyLabel is null)
        {
            CreateInterface();
        }
    }

    private static PanelContainer CreatePanel(Color color)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = color,
            BorderColor = new Color(0.25f, 0.47f, 0.62f, 0.82f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static Label CreateLabel(int size, Color color)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color("000000cc"));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private static Button IntentButton(string text, Action intent)
    {
        var button = CreateCommandButton(text);
        button.Pressed += intent;
        return button;
    }

    private static Button CreateCommandButton(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(105f, 30f),
        FocusMode = Control.FocusModeEnum.None,
        MouseDefaultCursorShape = Control.CursorShape.PointingHand
    };

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

    private sealed partial class HudOverlay : Control
    {
        private const double MarkerDuration = 0.52;
        private Vector2 _dragStart;
        private Vector2 _dragEnd;
        private Vector2 _markerPosition;
        private double _markerTime;
        private bool _dragVisible;
        private bool _buildModeActive;
        private Rts3DCommandMarkerKind _markerKind;

        public bool BuildModeActive
        {
            get => _buildModeActive;
            set
            {
                _buildModeActive = value;
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
            if (_markerTime <= 0d)
            {
                return;
            }
            _markerTime = Math.Max(0d, _markerTime - delta);
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_dragVisible)
            {
                var rect = new Rect2(_dragStart, _dragEnd - _dragStart).Abs();
                DrawRect(rect, new Color(0.20f, 0.82f, 1f, 0.13f), true);
                DrawRect(rect, new Color(0.32f, 0.88f, 1f, 0.96f), false, 1.5f);
            }

            if (_markerTime > 0d)
            {
                var progress = 1f - (float)(_markerTime / MarkerDuration);
                var color = MarkerColor(_markerKind) with
                {
                    A = 1f - progress
                };
                var radius = 8f + progress * 20f;
                DrawArc(_markerPosition, radius, 0f, MathF.Tau, 28, color, 2.5f);
                DrawLine(
                    _markerPosition - Vector2.One * 5f,
                    _markerPosition + Vector2.One * 5f,
                    color,
                    2f);
                DrawLine(
                    _markerPosition + new Vector2(-5f, 5f),
                    _markerPosition + new Vector2(5f, -5f),
                    color,
                    2f);
            }

            if (BuildModeActive)
            {
                var cursor = GetLocalMousePosition();
                DrawArc(cursor, 13f, 0f, MathF.Tau, 24,
                    new Color(0.40f, 1f, 0.62f, 0.82f), 2f);
            }
        }

        private static Color MarkerColor(Rts3DCommandMarkerKind kind) => kind switch
        {
            Rts3DCommandMarkerKind.Attack => new Color("ff5b66"),
            Rts3DCommandMarkerKind.Interact => new Color("ffd55b"),
            Rts3DCommandMarkerKind.Build => new Color("68ff9a"),
            _ => new Color("57dcff")
        };
    }
}

public readonly record struct Rts3DHudSnapshot(
    double ElapsedSeconds,
    int Minerals,
    int Gas,
    int SupplyUsed,
    int SupplyCapacity,
    int SelectedUnits,
    int SelectedBuildings,
    int EnemyWorkers,
    int EnemyCombatUnits,
    int EnemyBuildings,
    string? Mode = null,
    string? Status = null);

public readonly record struct Rts3DHudCommandOption(
    int Id,
    string Label,
    string? Shortcut = null,
    string? Tooltip = null,
    bool Enabled = true);

public enum Rts3DHudCommandGroup : byte
{
    Build,
    Train,
    Research
}

public enum Rts3DCommandMarkerKind : byte
{
    Move,
    Attack,
    Interact,
    Build
}
