using Godot;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;

namespace War3Rts;

/// <summary>Warcraft Human operation console and live portrait viewport.</summary>
public sealed partial class War3RtsHud : Control
{
    public const float ConsoleHeight = 208f;
    private const float ConsoleChromeWidth = 1000f;
    private const float ConsoleTextureSize = 320f;
    private const float ConsoleTextureTop = -112f;
    private const float PortraitMaskScale = 0.98f;
    private const float PortraitBarWidth = 98f * PortraitMaskScale;
    private const float PortraitBarHeight = 14f * PortraitMaskScale;
    private static readonly Vector2 PortraitSlotPosition = new(270f, 69f);
    private static readonly Vector2 PortraitSlotSize = new(93f, 100f);
    private static readonly Vector2 PortraitMaskPosition = new(
        -21f * PortraitMaskScale,
        -115f * PortraitMaskScale);
    private static readonly Vector2 PortraitMaskSize = Vector2.One *
        (256f * PortraitMaskScale);
    private static readonly Vector2 MinimapSlotPosition = new(12f, 61f);
    private static readonly Vector2 MinimapSlotSize = new(173f, 138f);
    private static readonly Vector2 QueueBackdropPosition = new(0f, 8f);
    private static readonly Vector2 QueueBackdropSize = new(256f, 128f);
    private static readonly Vector2 ActiveQueueIconPosition = new(13f, 31f);
    private static readonly Vector2 ActiveQueueIconSize = new(42f, 42f);
    private static readonly Vector2 WaitingQueueIconOrigin = new(13f, 83f);
    private static readonly Vector2 WaitingQueueIconSize = new(30f, 30f);
    private const float WaitingQueueIconStride = 40f;
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
    private readonly Button[] _queueButtons = new Button[7];
    private readonly War3QueueItemSnapshot?[] _queueSlotItems =
        new War3QueueItemSnapshot?[7];
    private readonly Dictionary<Key, War3CommandSnapshot> _hotkeys = [];
    private Label? _goldValue;
    private Label? _lumberValue;
    private Label? _supplyValue;
    private Label? _clock;
    private Label? _selectionTitle;
    private Label? _selectionSubtitle;
    private Label? _attackValue;
    private Label? _armorValue;
    private Label? _levelValue;
    private Label? _combatTypeValue;
    private Control? _selectionDetails;
    private Control? _queuePanel;
    private TextureRect? _queueBackdrop;
    private Label? _queueActionLabel;
    private Label? _queueStateLabel;
    private TextureProgressBar? _queueProgress;
    private Label? _mode;
    private Label? _status;
    private Control? _commandGrid;
    private Control? _bottomConsole;
    private Control? _consoleChrome;
    private War3MinimapControl? _minimap;
    private Control? _portraitSlot;
    private SubViewport? _portraitViewport;
    private SubViewportContainer? _portraitOpening;
    private Node3D? _portraitWorld;
    private Camera3D? _portraitCamera;
    private War3ModelActor? _portraitActor;
    private TextureRect? _portraitMask;
    private ColorRect? _portraitHealthFill;
    private ColorRect? _portraitManaFill;
    private string _portraitSource = string.Empty;
    private bool _portraitBuildingView;
    private string _commandSignature = string.Empty;
    private string _queueSignature = string.Empty;
    private War3SelectionOverlay? _selectionOverlay;

    public event Action<War3CommandSnapshot>? CommandRequested;
    public event Action<War3QueueItemSnapshot>? QueueItemCancelRequested;
    public event Action? ReturnRequested;
    public event Action<System.Numerics.Vector2>? MinimapFocusRequested;

    public bool PortraitReady => _portraitActor?.Loaded == true;
    public int VisibleQueueItemCount =>
        _queueButtons.Count(button => button.Visible);
    public War3QueueItemKind? ActiveQueueItemKind =>
        _queueSlotItems[0]?.Kind;
    public bool ActiveQueueIconReady => _queueButtons[0]?.Icon is not null;
    public bool QueuePanelVisible => _queuePanel?.Visible == true;
    public bool SelectionDetailsVisible => _selectionDetails?.Visible == true;
    public bool QueuePresentationExclusive =>
        QueuePanelVisible != SelectionDetailsVisible;
    public bool QueueIconsAboveBackdrop =>
        _queueButtons.All(button => button is not null && button.ZIndex > 10);
    public bool MinimapAspectFitReady =>
        _minimap?.Position.IsEqualApprox(MinimapSlotPosition) == true &&
        _minimap.Size.IsEqualApprox(MinimapSlotSize) &&
        _minimap.AspectFitReady;
    public bool QueueLayoutReady =>
        _queuePanel is not null && _queueBackdrop is not null &&
        _queueProgress is not null &&
        _queueButtons[0] is not null && _queueButtons[6] is not null &&
        _queueBackdrop.Position.IsEqualApprox(QueueBackdropPosition) &&
        _queueBackdrop.Size.IsEqualApprox(QueueBackdropSize) &&
        _queueButtons[0].Position.IsEqualApprox(ActiveQueueIconPosition) &&
        _queueButtons[0].Size.IsEqualApprox(ActiveQueueIconSize) &&
        _queueButtons[6].Position.IsEqualApprox(
            WaitingQueueIconOrigin + new Vector2(
                WaitingQueueIconStride * 5f, 0f)) &&
        _queueButtons[6].Size.IsEqualApprox(WaitingQueueIconSize) &&
        _queueProgress.Position.IsEqualApprox(new Vector2(64f, 39f));
    public bool ConsoleLayoutReady =>
        _consoleChrome is not null &&
        MathF.Abs(_consoleChrome.Size.X - ConsoleChromeWidth) < 0.1f &&
        _portraitSlot?.Position.IsEqualApprox(PortraitSlotPosition) == true &&
        _portraitSlot.Size.IsEqualApprox(PortraitSlotSize) &&
        _portraitOpening?.Position.IsEqualApprox(Vector2.Zero) == true &&
        _portraitOpening.Size.IsEqualApprox(PortraitSlotSize) &&
        _portraitMask?.Position.IsEqualApprox(PortraitMaskPosition) == true &&
        _portraitMask.Size.IsEqualApprox(PortraitMaskSize) &&
        _commandGrid?.Position.IsEqualApprox(new Vector2(766f, 38f)) == true &&
        _commandButtons[11].Position.IsEqualApprox(new Vector2(174f, 116f));

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
        _attackValue!.Text = snapshot.Selection.Count > 0
            ? snapshot.Selection.AttackDamage > 0f
                ? $"{snapshot.Selection.AttackDamage:0.#}" +
                  (snapshot.Selection.WeaponUpgradeLevel > 0
                      ? $"  +{snapshot.Selection.WeaponUpgradeLevel}"
                      : string.Empty) +
                  (snapshot.Selection.WeaponCount > 1
                      ? $"  武器{snapshot.Selection.ActiveWeaponSlot + 1}/" +
                        $"{snapshot.Selection.WeaponCount}"
                      : string.Empty)
                : "—"
            : "—";
        _armorValue!.Text = snapshot.Selection.Count > 0
            ? snapshot.Selection.Armor.ToString("0.#")
            : "—";
        _levelValue!.Text = snapshot.Selection.Count > 0
            ? snapshot.Selection.Level.ToString()
            : "—";
        _combatTypeValue!.Text = snapshot.Selection.Count > 0
            ? $"{snapshot.Selection.AttackClass} / " +
              $"{snapshot.Selection.ArmorClass}" +
              (snapshot.Selection.WeaponTargetLabel.Length > 0
                  ? $" · {snapshot.Selection.WeaponTargetLabel}"
                  : string.Empty)
            : "—";
        if (_portraitHealthFill is not null)
        {
            var healthRatio = snapshot.Selection.MaximumHealth > 0f
                ? Math.Clamp(snapshot.Selection.Health /
                             snapshot.Selection.MaximumHealth, 0f, 1f)
                : 0f;
            _portraitHealthFill.Size = new Vector2(
                PortraitBarWidth * healthRatio, PortraitBarHeight);
        }
        if (_portraitManaFill is not null)
        {
            var manaRatio = snapshot.Selection.MaximumMana > 0f
                ? Math.Clamp(snapshot.Selection.Mana /
                             snapshot.Selection.MaximumMana, 0f, 1f)
                : 0f;
            _portraitManaFill.Size = new Vector2(
                PortraitBarWidth * manaRatio, PortraitBarHeight);
            _portraitManaFill.Visible = snapshot.Selection.MaximumMana > 0f;
        }
        var queueVisible = snapshot.Selection.QueueItems.Length > 0;
        _selectionDetails!.Visible = !queueVisible;
        _selectionDetails.ProcessMode = queueVisible
            ? ProcessModeEnum.Disabled
            : ProcessModeEnum.Inherit;
        _queuePanel!.Visible = queueVisible;
        _queuePanel.ProcessMode = queueVisible
            ? ProcessModeEnum.Inherit
            : ProcessModeEnum.Disabled;
        _queuePanel.MouseFilter = queueVisible
            ? MouseFilterEnum.Pass
            : MouseFilterEnum.Ignore;
        UpdateQueue(snapshot.Selection);
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

    public bool TryInvokeQueueSlot(int slot)
    {
        if ((uint)slot >= (uint)_queueSlotItems.Length ||
            _queueSlotItems[slot] is not { CanCancel: true } item)
            return false;
        QueueItemCancelRequested?.Invoke(item);
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
        _bottomConsole = bottom;

        var chrome = new Control
        {
            Name = "HumanConsoleChrome",
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 1f,
            OffsetLeft = -ConsoleChromeWidth / 2f,
            OffsetRight = ConsoleChromeWidth / 2f,
            PivotOffset = new Vector2(ConsoleChromeWidth / 2f, ConsoleHeight),
            MouseFilter = MouseFilterEnum.Ignore
        };
        bottom.AddChild(chrome);
        _consoleChrome = chrome;
        bottom.Resized += UpdateConsoleScale;

        var tileWidths = new[]
        {
            ConsoleTextureSize,
            ConsoleTextureSize,
            ConsoleTextureSize,
            40f
        };
        var tileLeft = 0f;
        for (var index = 0; index < tileWidths.Length; index++)
        {
            var tile = new TextureRect
            {
                Name = $"HumanUiTile{index + 1}",
                Texture = War3RuntimeAssets.LoadTexture(
                    $@"UI\Console\Human\HumanUITile0{index + 1}.blp"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(tileLeft, ConsoleTextureTop),
                Size = new Vector2(tileWidths[index], ConsoleTextureSize),
                MouseFilter = MouseFilterEnum.Ignore,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                ZIndex = 10
            };
            chrome.AddChild(tile);
            tileLeft += tileWidths[index];
        }

        AddMinimap(chrome);
        AddPortrait(chrome);
        AddSelectionInfo(chrome);
        AddCommandCard(chrome);
        UpdateConsoleScale();
    }

    private void UpdateConsoleScale()
    {
        if (_bottomConsole is null || _consoleChrome is null) return;
        var scale = Math.Min(1f,
            _bottomConsole.Size.X / Math.Max(1f, ConsoleChromeWidth));
        _consoleChrome.Scale = Vector2.One * scale;
    }

    private void AddMinimap(Control parent)
    {
        _minimap = new War3MinimapControl
        {
            Position = MinimapSlotPosition,
            Size = MinimapSlotSize,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 5
        };
        _minimap.FocusRequested += point => MinimapFocusRequested?.Invoke(point);
        parent.AddChild(_minimap);
    }

    private void AddPortrait(Control parent)
    {
        // HumanUITile01/02 form one continuous portrait recess.  Everything
        // belonging to the portrait is composed in that recess so the 3D
        // render continues underneath both the base chrome and the mask.
        _portraitSlot = new Control
        {
            Name = "PortraitSlot",
            Position = PortraitSlotPosition,
            Size = PortraitSlotSize,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 5
        };
        parent.AddChild(_portraitSlot);

        _portraitOpening = new SubViewportContainer
        {
            Name = "PortraitOpening",
            Position = Vector2.Zero,
            Size = PortraitSlotSize,
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 0
        };
        _portraitSlot.AddChild(_portraitOpening);
        _portraitViewport = new SubViewport
        {
            Name = "PortraitViewport",
            Size = new Vector2I(186, 200),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
            TransparentBg = false,
            OwnWorld3D = true
        };
        _portraitOpening.AddChild(_portraitViewport);
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
            // Register the mask's 95x101 transparent portrait aperture with
            // the 93x100 aperture cut into HumanUITile01/02.  The frame then
            // covers both the live portrait and the base chrome, as it does
            // in Warcraft III, instead of becoming a smaller inset frame.
            Position = PortraitMaskPosition,
            Size = PortraitMaskSize,
            Texture = War3RuntimeAssets.LoadTexture(
                @"UI\Console\Human\HumanUIPortraitMask.blp"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            ZIndex = 20
        };
        _portraitSlot.AddChild(_portraitMask);

        var healthBack = new ColorRect
        {
            Position = new Vector2(
                -PortraitMaskScale,
                (220f - 115f) * PortraitMaskScale),
            Size = new Vector2(PortraitBarWidth, PortraitBarHeight),
            Color = new Color("071009"),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 15
        };
        _portraitSlot.AddChild(healthBack);
        _portraitHealthFill = new ColorRect
        {
            Size = new Vector2(PortraitBarWidth, PortraitBarHeight),
            Color = new Color("3c9d50"),
            MouseFilter = MouseFilterEnum.Ignore
        };
        healthBack.AddChild(_portraitHealthFill);
        var manaBack = new ColorRect
        {
            Position = new Vector2(
                -PortraitMaskScale,
                (238f - 115f) * PortraitMaskScale),
            Size = new Vector2(PortraitBarWidth, PortraitBarHeight),
            Color = new Color("09203b"),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 15
        };
        _portraitSlot.AddChild(manaBack);
        _portraitManaFill = new ColorRect
        {
            Size = new Vector2(PortraitBarWidth, PortraitBarHeight),
            Color = new Color("1766bb"),
            MouseFilter = MouseFilterEnum.Ignore
        };
        manaBack.AddChild(_portraitManaFill);
    }

    private void AddSelectionInfo(Control parent)
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(376f, 62f),
            Size = new Vector2(256f, 146f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 5
        };
        panel.AddThemeStyleboxOverride("panel", Box(
            new Color("071019e8"), Colors.Transparent, 0, 0));
        parent.AddChild(panel);
        var margin = Margin(10, 6, 10, 5);
        var content = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true
        };
        panel.AddChild(content);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        content.AddChild(margin);
        _selectionDetails = margin;
        _selectionDetails.ZIndex = 5;
        var column = VBox(2);
        margin.AddChild(column);
        _selectionTitle = LabelText("未选择单位", 18, Text);
        _selectionTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_selectionTitle);
        _selectionSubtitle = LabelText("", 13, Muted);
        column.AddChild(_selectionSubtitle);
        var divider = new HSeparator { CustomMinimumSize = new Vector2(0f, 2f) };
        divider.AddThemeStyleboxOverride("separator", Box(
            Colors.Transparent, new Color("705629a0"), 0, 1));
        column.AddChild(divider);
        var stats = new GridContainer
        {
            Columns = 4,
            CustomMinimumSize = new Vector2(234f, 40f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        stats.AddThemeConstantOverride("h_separation", 6);
        stats.AddThemeConstantOverride("v_separation", 1);
        column.AddChild(stats);
        AddStat(stats, "攻击", out _attackValue);
        AddStat(stats, "护甲", out _armorValue);
        AddStat(stats, "等级", out _levelValue);
        AddStat(stats, "攻防", out _combatTypeValue, true);
        AddBuildQueuePanel(content);
    }

    private void AddBuildQueuePanel(Control parent)
    {
        _queuePanel = new Control
        {
            Name = "War3BuildQueue",
            MouseFilter = MouseFilterEnum.Ignore,
            ProcessMode = ProcessModeEnum.Disabled,
            Visible = false,
            ClipContents = true,
            ZIndex = 30
        };
        _queuePanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        parent.AddChild(_queuePanel);

        _queueBackdrop = new TextureRect
        {
            Name = "BuildQueueBackdrop",
            Position = QueueBackdropPosition,
            Size = QueueBackdropSize,
            Texture = War3RuntimeAssets.LoadTexture(
                @"UI\Widgets\Console\Human\human-unitqueue-border.blp"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ZIndex = 10
        };
        _queuePanel.AddChild(_queueBackdrop);

        _queueActionLabel = LabelText("", 14, Text);
        _queueActionLabel.Position = new Vector2(64f, 10f);
        _queueActionLabel.Size = new Vector2(183f, 24f);
        _queueActionLabel.TextOverrunBehavior =
            TextServer.OverrunBehavior.TrimEllipsis;
        _queueActionLabel.MouseFilter = MouseFilterEnum.Ignore;
        _queueActionLabel.ZIndex = 15;
        _queuePanel.AddChild(_queueActionLabel);

        _queueProgress = new TextureProgressBar
        {
            Position = new Vector2(64f, 39f),
            Size = new Vector2(126f, 16f),
            MinValue = 0d,
            MaxValue = 100d,
            Value = 0d,
            FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight,
            TextureProgress = War3RuntimeAssets.LoadTexture(
                @"UI\Feedback\BuildProgressBar\human-buildprogressbar-fill.blp"),
            TextureOver = War3RuntimeAssets.LoadTexture(
                @"UI\Feedback\BuildProgressBar\human-buildprogressbar-border.blp"),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 15
        };
        _queuePanel.AddChild(_queueProgress);

        _queueStateLabel = LabelText("", 11, Gold);
        _queueStateLabel.Position = new Vector2(194f, 37f);
        _queueStateLabel.Size = new Vector2(54f, 20f);
        _queueStateLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _queueStateLabel.MouseFilter = MouseFilterEnum.Ignore;
        _queueStateLabel.ZIndex = 15;
        _queuePanel.AddChild(_queueStateLabel);

        var hint = LabelText("点击图标取消队列项目", 10, Muted);
        hint.Position = new Vector2(64f, 57f);
        hint.Size = new Vector2(180f, 18f);
        hint.MouseFilter = MouseFilterEnum.Ignore;
        hint.ZIndex = 15;
        _queuePanel.AddChild(hint);

        for (var index = 0; index < _queueButtons.Length; index++)
        {
            var button = QueueButton(index == 0);
            button.Position = index == 0
                ? ActiveQueueIconPosition
                : WaitingQueueIconOrigin + new Vector2(
                    (index - 1) * WaitingQueueIconStride, 0f);
            button.Size = index == 0
                ? ActiveQueueIconSize
                : WaitingQueueIconSize;
            // The original BLP has an opaque black center. Queue icons must be
            // drawn over that frame; the previous z-index put every loaded icon
            // behind it, which made populated slots look empty.
            button.ZIndex = 25;
            var slot = index;
            button.Pressed += () =>
            {
                if (_queueSlotItems[slot] is { CanCancel: true } item)
                    QueueItemCancelRequested?.Invoke(item);
            };
            _queueButtons[index] = button;
            _queuePanel.AddChild(button);
        }
    }

    private void AddCommandCard(Control parent)
    {
        parent.AddChild(new ColorRect
        {
            Position = new Vector2(759f, 33f),
            Size = new Vector2(241f, 175f),
            Color = new Color("05080ceb"),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 5
        });
        _commandGrid = new Control
        {
            Position = new Vector2(766f, 38f),
            Size = new Vector2(226f, 168f),
            MouseFilter = MouseFilterEnum.Pass,
            ZIndex = 20
        };
        parent.AddChild(_commandGrid);
        for (var index = 0; index < _commandButtons.Length; index++)
        {
            var button = CommandButton();
            button.Position = new Vector2(
                index % 4 * 58f,
                index / 4 * 58f);
            button.Size = new Vector2(52f, 52f);
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
        if (selection.PortraitSource == _portraitSource &&
            selection.PortraitIsBuilding == _portraitBuildingView) return;
        _portraitSource = selection.PortraitSource;
        _portraitBuildingView = selection.PortraitIsBuilding;
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
                selection.PortraitUsesOriginalCamera && !selection.PortraitIsBuilding
                    ? "Portrait"
                    : "Stand",
                "Stand");
            _portraitActor.FrameCamera(
                _portraitCamera,
                selection.PortraitUsesOriginalCamera,
                selection.PortraitIsBuilding);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"war3_rts portrait failed: {_portraitSource} ({exception.Message})");
            _portraitActor?.QueueFree();
            _portraitActor = null;
        }
    }

    private static void AddStat(
        GridContainer parent,
        string caption,
        out Label value,
        bool wideValue = false)
    {
        var label = LabelText(caption, 11, Muted);
        label.CustomMinimumSize = new Vector2(28f, 18f);
        label.VerticalAlignment = VerticalAlignment.Center;
        parent.AddChild(label);
        value = LabelText("—", 12, Text);
        value.CustomMinimumSize = new Vector2(wideValue ? 76f : 38f, 18f);
        value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        value.VerticalAlignment = VerticalAlignment.Center;
        parent.AddChild(value);
    }

    private void UpdateQueue(War3SelectionSnapshot selection)
    {
        if (_queueActionLabel is null || _queueProgress is null ||
            _queueStateLabel is null)
            return;
        var items = selection.QueueItems;
        if (items.Length == 0)
        {
            _queueActionLabel.Text = string.Empty;
            _queueStateLabel.Text = string.Empty;
            _queueProgress.Value = 0d;
        }
        else
        {
            var active = items[0];
            _queueActionLabel.Text = selection.QueueLabel.Length > 0
                ? selection.QueueLabel
                : $"{active.StateLabel}：{active.Label}";
            _queueProgress.Value =
                Math.Clamp(active.Progress, 0f, 1f) * 100d;
            _queueStateLabel.Text = active.Progress >= 1f
                ? active.StateLabel
                : $"{active.Progress:P0}";
        }

        var signature = string.Join(';', items.Take(_queueButtons.Length)
            .Select(value =>
                $"{value.Kind}:{value.OrderId}:{value.DataId}:" +
                $"{value.IconPath}:{value.CanCancel}"));
        if (signature == _queueSignature) return;
        _queueSignature = signature;
        for (var index = 0; index < _queueButtons.Length; index++)
        {
            _queueSlotItems[index] = null;
            _queueButtons[index].Visible = false;
            _queueButtons[index].Disabled = true;
            _queueButtons[index].Icon = null;
            _queueButtons[index].TooltipText = string.Empty;
        }
        for (var index = 0;
             index < Math.Min(items.Length, _queueButtons.Length);
             index++)
        {
            var item = items[index];
            var button = _queueButtons[index];
            _queueSlotItems[index] = item;
            button.Visible = true;
            button.Disabled = !item.CanCancel;
            button.Icon = War3RuntimeAssets.LoadTexture(item.IconPath);
            button.TooltipText = item.Tooltip +
                                 (index == 0
                                     ? "\n当前项目"
                                     : $"\n队列第 {index + 1} 项");
        }
    }

    private void RebuildCommands(IReadOnlyList<War3CommandSnapshot> commands)
    {
        var signature = string.Join(';', commands.Select(value =>
            $"{value.Slot}:{value.Kind}:{value.DataId}:{value.Enabled}:" +
            $"{MathF.Ceiling(value.CooldownRemaining * 10f)}:{value.Toggled}"));
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
            button.Modulate = command.Enabled
                ? Colors.White
                : new Color(0.55f, 0.55f, 0.55f, 0.82f);
            button.TooltipText = command.Tooltip;
            button.Icon = War3RuntimeAssets.LoadTexture(command.IconPath);
            _slotCommands[command.Slot] = command;
            var hotkey = LabelText(command.Hotkey, 11, Gold);
            hotkey.SetAnchorsPreset(LayoutPreset.TopLeft);
            hotkey.OffsetLeft = 3f;
            hotkey.OffsetTop = 1f;
            hotkey.MouseFilter = MouseFilterEnum.Ignore;
            button.AddChild(hotkey);
            if (command.CooldownRemaining > 0f)
            {
                var cooldown = LabelText(
                    MathF.Ceiling(command.CooldownRemaining).ToString("0"),
                    16, Text);
                cooldown.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                cooldown.HorizontalAlignment = HorizontalAlignment.Center;
                cooldown.VerticalAlignment = VerticalAlignment.Center;
                cooldown.MouseFilter = MouseFilterEnum.Ignore;
                button.AddChild(cooldown);
            }
            if (command.Toggled)
            {
                var active = new ColorRect
                {
                    Position = new Vector2(2f, 46f),
                    Size = new Vector2(48f, 4f),
                    Color = new Color("54f08b"),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                button.AddChild(active);
            }
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
            CustomMinimumSize = new Vector2(52f, 52f),
            FocusMode = FocusModeEnum.None,
            ExpandIcon = true,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        button.AddThemeConstantOverride("icon_max_width", 50);
        button.AddThemeStyleboxOverride("normal", Box(
            Colors.Transparent, Colors.Transparent, 0, 0));
        button.AddThemeStyleboxOverride("hover", Box(
            new Color("e5b84b18"), new Color("e6bd55"), 1, 1));
        button.AddThemeStyleboxOverride("pressed", Box(
            new Color("05080c88"), new Color("ffe17a"), 1, 1));
        button.AddThemeStyleboxOverride("disabled", Box(
            Colors.Transparent, Colors.Transparent, 0, 0));
        return button;
    }

    private static Button QueueButton(bool active)
    {
        var button = new Button
        {
            FocusMode = FocusModeEnum.None,
            ExpandIcon = true,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        button.AddThemeConstantOverride("icon_max_width", active ? 42 : 31);
        button.AddThemeStyleboxOverride("normal", Box(
            Colors.Transparent, Colors.Transparent, 0, 0));
        button.AddThemeStyleboxOverride("hover", Box(
            new Color("eac85822"), new Color("ffe071"), 1, 1));
        button.AddThemeStyleboxOverride("pressed", Box(
            new Color("03060999"), new Color("fff1a0"), 1, 1));
        button.AddThemeStyleboxOverride("disabled", Box(
            Colors.Transparent, Colors.Transparent, 0, 0));
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
        private const float ContentInset = 4f;
        private SimRect _bounds;
        private War3MinimapEntity[] _entities = [];
        private War3MinimapResource[] _resources = [];

        public event Action<System.Numerics.Vector2>? FocusRequested;

        public bool AspectFitReady
        {
            get
            {
                var rect = MapRect();
                var width = Math.Max(1f, _bounds.Width);
                var height = Math.Max(1f, _bounds.Height);
                var scaleX = rect.Size.X / width;
                var scaleY = rect.Size.Y / height;
                var leftMargin = rect.Position.X;
                var rightMargin = Size.X - rect.End.X;
                var topMargin = rect.Position.Y;
                var bottomMargin = Size.Y - rect.End.Y;
                return MathF.Abs(scaleX - scaleY) < 0.0001f &&
                       MathF.Abs(leftMargin - rightMargin) < 0.001f &&
                       MathF.Abs(topMargin - bottomMargin) < 0.001f &&
                       rect.Position.X >= ContentInset - 0.001f &&
                       rect.Position.Y >= ContentInset - 0.001f &&
                       rect.End.X <= Size.X - ContentInset + 0.001f &&
                       rect.End.Y <= Size.Y - ContentInset + 0.001f;
            }
        }

        public override void _Ready()
        {
            // AddMinimap owns the exact opening in the Human console artwork.
            // Expanding to FullRect here would silently turn the minimap into
            // a 1000x208 overlay across the entire bottom console.
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
            var mapRect = MapRect();
            if (!mapRect.HasPoint(mouse.Position))
            {
                // Letterbox space is part of the console, not part of the
                // world. Consume the click without snapping the camera to a
                // seemingly unrelated map edge.
                AcceptEvent();
                return;
            }
            var normalized = new Vector2(
                Math.Clamp((mouse.Position.X - mapRect.Position.X) /
                           Math.Max(1f, mapRect.Size.X), 0f, 1f),
                Math.Clamp((mouse.Position.Y - mapRect.Position.Y) /
                           Math.Max(1f, mapRect.Size.Y), 0f, 1f));
            FocusRequested?.Invoke(new System.Numerics.Vector2(
                Mathf.Lerp(_bounds.Min.X, _bounds.Max.X, normalized.X),
                Mathf.Lerp(_bounds.Min.Y, _bounds.Max.Y, normalized.Y)));
            AcceptEvent();
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color("050a09"), true);
            var mapRect = MapRect();
            DrawRect(mapRect, new Color("0b1914"), true);
            for (var x = 0; x <= 4; x++)
                DrawLine(
                    new Vector2(
                        mapRect.Position.X + mapRect.Size.X * x / 4f,
                        mapRect.Position.Y),
                    new Vector2(
                        mapRect.Position.X + mapRect.Size.X * x / 4f,
                        mapRect.End.Y),
                    new Color("23362e88"), 1f);
            for (var y = 0; y <= 4; y++)
                DrawLine(
                    new Vector2(
                        mapRect.Position.X,
                        mapRect.Position.Y + mapRect.Size.Y * y / 4f),
                    new Vector2(
                        mapRect.End.X,
                        mapRect.Position.Y + mapRect.Size.Y * y / 4f),
                    new Color("23362e88"), 1f);
            DrawRect(mapRect, new Color("52635a"), false, 1f);
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
            var mapRect = MapRect();
            return new Vector2(
                mapRect.Position.X +
                (point.X - _bounds.Min.X) / width * mapRect.Size.X,
                mapRect.Position.Y +
                (point.Y - _bounds.Min.Y) / height * mapRect.Size.Y);
        }

        private Rect2 MapRect()
        {
            var available = new Vector2(
                Math.Max(1f, Size.X - ContentInset * 2f),
                Math.Max(1f, Size.Y - ContentInset * 2f));
            var worldWidth = Math.Max(1f, _bounds.Width);
            var worldHeight = Math.Max(1f, _bounds.Height);
            var scale = Math.Min(
                available.X / worldWidth,
                available.Y / worldHeight);
            var extent = new Vector2(
                worldWidth * scale,
                worldHeight * scale);
            return new Rect2((Size - extent) * 0.5f, extent);
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
