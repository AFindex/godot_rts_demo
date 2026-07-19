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
    private const float ConsoleAtlasScale = ConsoleTextureSize / 512f;
    private const float PortraitMaskScale = 0.98f;
    private const float PortraitBarWidth = 98f * PortraitMaskScale;
    private const float PortraitBarHeight = 14f * PortraitMaskScale;
    private const int SelectionGroupColumns = 6;
    private const int SelectionGroupRows = 3;
    private const int SelectionGroupPageCapacity =
        SelectionGroupColumns * SelectionGroupRows;
    private const int MaximumVisibleSelectionPageTabs = 4;
    private static readonly Vector2 PortraitSlotPosition = new(270f, 69f);
    private static readonly Vector2 PortraitSlotSize = new(93f, 100f);
    private static readonly Vector2 PortraitMaskPosition = new(
        -21f * PortraitMaskScale,
        -115f * PortraitMaskScale);
    private static readonly Vector2 PortraitMaskSize = Vector2.One *
        (256f * PortraitMaskScale);
    private static readonly Vector2 MinimapSlotPosition = new(12f, 61f);
    private static readonly Vector2 MinimapSlotSize = new(173f, 138f);
    // Coordinates below are measured in the original 1600x512 Human console
    // atlas, then converted by the same 0.625 scale as the four base tiles.
    private static readonly Vector2 InventoryCoverPosition = new(
        944f * ConsoleAtlasScale,
        ConsoleTextureTop);
    private static readonly Vector2 InventoryCoverSize = new(
        256f * ConsoleAtlasScale,
        512f * ConsoleAtlasScale);
    private static readonly Vector2 InventoryGridPosition = new(
        (1024f + 4f) * ConsoleAtlasScale,
        ConsoleTextureTop + 284f * ConsoleAtlasScale);
    private static readonly Vector2 InventorySlotSize = new(
        67f * ConsoleAtlasScale,
        67f * ConsoleAtlasScale);
    private static readonly Vector2 InventorySlotStride = new(
        80f * ConsoleAtlasScale,
        77f * ConsoleAtlasScale);
    private static readonly Vector2 CommandGridPosition = new(
        (1024f + 204f) * ConsoleAtlasScale,
        ConsoleTextureTop + 243f * ConsoleAtlasScale);
    private static readonly Vector2 CommandButtonSize = new(
        83f * ConsoleAtlasScale,
        83f * ConsoleAtlasScale);
    private static readonly Vector2 CommandButtonStride = new(
        87f * ConsoleAtlasScale,
        87f * ConsoleAtlasScale);
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
    private TextureRect? _attackIcon;
    private TextureRect? _armorIcon;
    private Control? _heroProgressPanel;
    private Label? _heroLevelLabel;
    private ProgressBar? _heroExperienceBar;
    private Label? _heroExperienceLabel;
    private Control? _inventoryPanel;
    private TextureRect? _inventoryCover;
    private readonly Button[] _inventorySlots = new Button[6];
    private readonly TextureRect[] _inventoryIcons = new TextureRect[6];
    private Control? _selectionDetails;
    private Control? _selectionGroupPanel;
    private Label? _selectionGroupHeader;
    private readonly Button[] _selectionGroupButtons =
        new Button[SelectionGroupPageCapacity];
    private readonly TextureRect[] _selectionGroupIcons =
        new TextureRect[SelectionGroupPageCapacity];
    private readonly ColorRect[] _selectionGroupHealthFills =
        new ColorRect[SelectionGroupPageCapacity];
    private readonly ColorRect[] _selectionGroupManaFills =
        new ColorRect[SelectionGroupPageCapacity];
    private readonly War3SelectionGroupEntry?[] _selectionGroupSlotEntries =
        new War3SelectionGroupEntry?[SelectionGroupPageCapacity];
    private readonly Button[] _selectionPageTabButtons =
        new Button[MaximumVisibleSelectionPageTabs];
    private readonly int[] _selectionPageTabIndices =
        new int[MaximumVisibleSelectionPageTabs];
    private Button? _selectionPreviousPageButton;
    private Button? _selectionNextPageButton;
    private War3SelectionGroupEntry[] _selectionGroupEntries = [];
    private int _selectionGroupPage;
    private string _selectionGroupActiveKey = string.Empty;
    private Control? _constructionProgressPanel;
    private TextureProgressBar? _constructionProgress;
    private Label? _constructionProgressLabel;
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
    private Texture2D? _minimapFogTexture;
    private Control? _portraitSlot;
    private SubViewport? _portraitViewport;
    private SubViewportContainer? _portraitOpening;
    private Node3D? _portraitWorld;
    private Camera3D? _portraitCamera;
    private War3ModelActor? _portraitActor;
    private TextureRect? _portraitMask;
    private ColorRect? _portraitHealthFill;
    private ColorRect? _portraitManaBack;
    private ColorRect? _portraitManaFill;
    private Label? _portraitHealthValue;
    private Label? _portraitManaValue;
    private string _portraitSource = string.Empty;
    private bool _portraitBuildingView;
    private int _portraitTeam = int.MinValue;
    private bool _portraitAnimated;
    private string _portraitAnimationProperties = string.Empty;
    private double _portraitTalkRemaining;
    private string _commandSignature = string.Empty;
    private string _queueSignature = string.Empty;
    private War3SelectionOverlay? _selectionOverlay;

    public event Action<War3CommandSnapshot>? CommandRequested;
    public event Action<War3CommandSnapshot>? CommandAutoCastRequested;
    public event Action<War3QueueItemSnapshot>? QueueItemCancelRequested;
    public event Action<War3SelectionGroupEntry>? SelectionGroupEntryRequested;
    public event Action<int>? InventoryItemRequested;
    public event Action? ReturnRequested;
    public event Action<System.Numerics.Vector2>? MinimapFocusRequested;

    public bool PortraitReady => _portraitActor?.Loaded == true;
    public bool PortraitAnimationPlaying =>
        _portraitActor?.IsAnimationPlaying == true;
    public string PortraitSequence => _portraitActor?.CurrentSequence ?? string.Empty;
    public bool PortraitManaBackgroundVisible =>
        _portraitManaBack?.Visible == true;
    public int VisibleInventoryItemCount =>
        _inventoryIcons.Count(icon => icon?.Texture is not null);
    public int VisibleQueueItemCount =>
        _queueButtons.Count(button => button.Visible);
    public War3QueueItemKind? ActiveQueueItemKind =>
        _queueSlotItems[0]?.Kind;
    public bool ActiveQueueIconReady => _queueButtons[0]?.Icon is not null;
    public bool QueuePanelVisible => _queuePanel?.Visible == true;
    public bool SelectionDetailsVisible => _selectionDetails?.Visible == true;
    public bool SelectionGroupVisible => _selectionGroupPanel?.Visible == true;
    public int VisibleSelectionGroupEntryCount =>
        _selectionGroupButtons.Count(button => button?.Visible == true);
    public int VisibleSelectionPageTabCount =>
        _selectionPageTabButtons.Count(button => button?.Visible == true);
    public int SelectionGroupPage => _selectionGroupPage;
    public int SelectionGroupPageCount => Math.Max(1,
        (_selectionGroupEntries.Length + SelectionGroupPageCapacity - 1) /
        SelectionGroupPageCapacity);
    public int SelectionGroupTotalEntryCount => _selectionGroupEntries.Length;
    public bool SelectionGroupIconsAreSquare =>
        _selectionGroupButtons
            .Where(button => button?.Visible == true)
            .All(button => MathF.Abs(button.Size.X - button.Size.Y) < 0.1f);
    public bool ConstructionProgressVisible =>
        _constructionProgressPanel?.Visible == true;
    public bool QueuePresentationExclusive =>
        new[] { QueuePanelVisible, SelectionDetailsVisible, SelectionGroupVisible }
            .Count(value => value) == 1;
    public bool QueueIconsAboveBackdrop =>
        _queueButtons.All(button => button is not null && button.ZIndex > 10);
    public bool MinimapAspectFitReady =>
        _minimap?.Position.IsEqualApprox(MinimapSlotPosition) == true &&
        _minimap.Size.IsEqualApprox(MinimapSlotSize) &&
        _minimap.AspectFitReady;
    public bool MinimapFogReady =>
        _minimapFogTexture is not null &&
        _minimap?.FogTextureReady == true;
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
        InventoryLayoutReady &&
        _commandGrid?.Position.IsEqualApprox(CommandGridPosition) == true &&
        _commandButtons[11].Position.IsEqualApprox(new Vector2(
            CommandButtonStride.X * 3f,
            CommandButtonStride.Y * 2f));

    public bool InventoryLayoutReady =>
        _inventoryCover?.Position.IsEqualApprox(InventoryCoverPosition) == true &&
        _inventoryCover.Size.IsEqualApprox(InventoryCoverSize) &&
        _inventoryCover.ZIndex > 10 &&
        _inventoryPanel?.Position.IsEqualApprox(InventoryGridPosition) == true &&
        _inventoryPanel.Size.IsEqualApprox(new Vector2(
            InventorySlotStride.X + InventorySlotSize.X,
            InventorySlotStride.Y * 2f + InventorySlotSize.Y));

    public void SetDragSelection(Vector2 start, Vector2 end, bool visible) =>
        _selectionOverlay?.SetSelection(start, end, visible);

    public bool PlayPortraitTalk()
    {
        if (!_portraitAnimated || _portraitBuildingView ||
            _portraitActor is null ||
            !_portraitActor.ReplayPreferred("Portrait Talk"))
            return false;
        _portraitTalkRemaining = Math.Max(
            0.2d, _portraitActor.CurrentSequenceDurationSeconds);
        return true;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;
        CreateInterface();
        UpdateSnapshot(War3HudSnapshot.Empty);
    }

    public override void _Process(double delta)
    {
        if (_portraitTalkRemaining <= 0d) return;
        _portraitTalkRemaining -= delta;
        if (_portraitTalkRemaining > 0d || !_portraitAnimated ||
            _portraitActor is null)
            return;
        _portraitTalkRemaining = 0d;
        _portraitActor.PlayRepeatedPreferred("Portrait", "Stand Work", "Stand");
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
        if (_attackIcon is not null)
        {
            _attackIcon.Texture = snapshot.Selection.AttackIconPath.Length > 0
                ? War3RuntimeAssets.LoadTexture(snapshot.Selection.AttackIconPath)
                : null;
            _attackIcon.Modulate = snapshot.Selection.AttackDamage > 0f
                ? Colors.White
                : new Color(1f, 1f, 1f, 0.35f);
        }
        if (_armorIcon is not null)
            _armorIcon.Texture = snapshot.Selection.ArmorIconPath.Length > 0
                ? War3RuntimeAssets.LoadTexture(snapshot.Selection.ArmorIconPath)
                : null;
        if (_portraitHealthFill is not null)
        {
            var healthRatio = snapshot.Selection.MaximumHealth > 0f
                ? Math.Clamp(snapshot.Selection.Health /
                             snapshot.Selection.MaximumHealth, 0f, 1f)
                : 0f;
            _portraitHealthFill.Size = new Vector2(
                PortraitBarWidth * healthRatio, PortraitBarHeight);
        }
        if (_portraitHealthValue is not null)
            _portraitHealthValue.Text = snapshot.Selection.MaximumHealth > 0f
                ? $"{MathF.Ceiling(snapshot.Selection.Health):0} / " +
                  $"{MathF.Ceiling(snapshot.Selection.MaximumHealth):0}"
                : string.Empty;
        var hasMana = snapshot.Selection.MaximumMana > 0f;
        if (_portraitManaBack is not null)
        {
            // The portrait mask has a transparent aperture behind this strip.
            // Keep an opaque filler when the selection has no mana so the 3D
            // portrait never leaks through the missing blue bar.
            _portraitManaBack.Visible = true;
            _portraitManaBack.Color = hasMana
                ? new Color("09203b")
                : new Color("071019");
        }
        if (_portraitManaFill is not null)
        {
            _portraitManaFill.Visible = hasMana;
            var manaRatio = hasMana
                ? Math.Clamp(snapshot.Selection.Mana /
                             snapshot.Selection.MaximumMana, 0f, 1f)
                : 0f;
            _portraitManaFill.Size = new Vector2(
                PortraitBarWidth * manaRatio, PortraitBarHeight);
        }
        if (_portraitManaValue is not null)
        {
            _portraitManaValue.Text = hasMana
                ? $"{MathF.Ceiling(snapshot.Selection.Mana):0} / " +
                  $"{MathF.Ceiling(snapshot.Selection.MaximumMana):0}"
                : string.Empty;
        }
        UpdateHeroAndInventory(snapshot.Selection);
        var queueVisible = snapshot.Selection.QueueItems.Length > 0;
        var groupVisible = !queueVisible &&
                           snapshot.Selection.GroupEntries.Length > 1;
        _selectionDetails!.Visible = !queueVisible && !groupVisible;
        _selectionDetails.ProcessMode = queueVisible || groupVisible
            ? ProcessModeEnum.Disabled
            : ProcessModeEnum.Inherit;
        _selectionGroupPanel!.Visible = groupVisible;
        _selectionGroupPanel.ProcessMode = groupVisible
            ? ProcessModeEnum.Inherit
            : ProcessModeEnum.Disabled;
        _selectionGroupPanel.MouseFilter = groupVisible
            ? MouseFilterEnum.Pass
            : MouseFilterEnum.Ignore;
        _queuePanel!.Visible = queueVisible;
        _queuePanel.ProcessMode = queueVisible
            ? ProcessModeEnum.Inherit
            : ProcessModeEnum.Disabled;
        _queuePanel.MouseFilter = queueVisible
            ? MouseFilterEnum.Pass
            : MouseFilterEnum.Ignore;
        UpdateQueue(snapshot.Selection);
        UpdateSelectionGroup(snapshot.Selection);
        UpdateConstructionProgress(snapshot.Selection);
        _mode!.Text = snapshot.Mode;
        _status!.Text = snapshot.Status;
        UpdatePortrait(snapshot.Selection);
        RebuildCommands(snapshot.Commands);
        _minimap!.SetSnapshot(
            snapshot.WorldBounds, snapshot.CameraViewBounds,
            snapshot.Entities, snapshot.Resources, snapshot.MinimapSignalMode);
    }

    public bool TryInvokeHotkey(Key key)
    {
        if (!_hotkeys.TryGetValue(key, out var command) || !command.Enabled)
            return false;
        CommandRequested?.Invoke(command);
        return true;
    }

    public void ConfigureMinimapFog(Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _minimapFogTexture = texture;
        _minimap?.SetFogTexture(texture);
    }

    public bool TryInvokeQueueSlot(int slot)
    {
        if ((uint)slot >= (uint)_queueSlotItems.Length ||
            _queueSlotItems[slot] is not { CanCancel: true } item)
            return false;
        QueueItemCancelRequested?.Invoke(item);
        return true;
    }

    public bool TryInvokeSelectionPageTab(int slot)
    {
        if ((uint)slot >= (uint)_selectionPageTabIndices.Length ||
            !_selectionPageTabButtons[slot].Visible ||
            _selectionPageTabIndices[slot] < 0)
            return false;
        SetSelectionGroupPage(_selectionPageTabIndices[slot]);
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
        AddInventory(chrome);
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
        if (_minimapFogTexture is not null)
            _minimap.SetFogTexture(_minimapFogTexture);
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
        _portraitHealthValue = PortraitBarLabel();
        healthBack.AddChild(_portraitHealthValue);
        _portraitManaBack = new ColorRect
        {
            Position = new Vector2(
                -PortraitMaskScale,
                (238f - 115f) * PortraitMaskScale),
            Size = new Vector2(PortraitBarWidth, PortraitBarHeight),
            Color = new Color("09203b"),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 15
        };
        _portraitSlot.AddChild(_portraitManaBack);
        _portraitManaFill = new ColorRect
        {
            Size = new Vector2(PortraitBarWidth, PortraitBarHeight),
            Color = new Color("1766bb"),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _portraitManaBack.AddChild(_portraitManaFill);
        _portraitManaValue = PortraitBarLabel();
        _portraitManaBack.AddChild(_portraitManaValue);
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
        AddIconStat(stats, "攻击", out _attackIcon, out _attackValue);
        AddIconStat(stats, "护甲", out _armorIcon, out _armorValue);
        AddStat(stats, "等级", out _levelValue);
        AddStat(stats, "攻防", out _combatTypeValue, true);
        _heroProgressPanel = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(234f, 22f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _heroProgressPanel.AddThemeConstantOverride("separation", 5);
        column.AddChild(_heroProgressPanel);
        _heroLevelLabel = LabelText("英雄 1", 11, Gold);
        _heroLevelLabel.CustomMinimumSize = new Vector2(48f, 20f);
        _heroProgressPanel.AddChild(_heroLevelLabel);
        _heroExperienceBar = new ProgressBar
        {
            MinValue = 0d,
            MaxValue = 100d,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(112f, 13f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _heroExperienceBar.AddThemeStyleboxOverride("background", Box(
            new Color("071019"), new Color("5b4825"), 2, 1));
        _heroExperienceBar.AddThemeStyleboxOverride("fill", Box(
            new Color("d7a938"), new Color("ffe17a"), 2, 1));
        _heroProgressPanel.AddChild(_heroExperienceBar);
        _heroExperienceLabel = LabelText("0/0", 10, Text);
        _heroExperienceLabel.CustomMinimumSize = new Vector2(60f, 20f);
        _heroExperienceLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _heroProgressPanel.AddChild(_heroExperienceLabel);
        _constructionProgressPanel = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(234f, 20f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        ((HBoxContainer)_constructionProgressPanel)
            .AddThemeConstantOverride("separation", 5);
        column.AddChild(_constructionProgressPanel);
        _constructionProgressLabel = LabelText("建造 0%", 11, Gold);
        _constructionProgressLabel.CustomMinimumSize = new Vector2(65f, 18f);
        _constructionProgressPanel.AddChild(_constructionProgressLabel);
        _constructionProgress = new TextureProgressBar
        {
            MinValue = 0d,
            MaxValue = 100d,
            Value = 0d,
            FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight,
            TextureProgress = War3RuntimeAssets.LoadTexture(
                @"UI\Feedback\BuildProgressBar\human-buildprogressbar-fill.blp"),
            TextureOver = War3RuntimeAssets.LoadTexture(
                @"UI\Feedback\BuildProgressBar\human-buildprogressbar-border.blp"),
            CustomMinimumSize = new Vector2(158f, 16f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _constructionProgressPanel.AddChild(_constructionProgress);
        AddBuildQueuePanel(content);
        AddSelectionGroupPanel(content);
    }

    private void AddSelectionGroupPanel(Control parent)
    {
        _selectionGroupPanel = new Control
        {
            Name = "War3SelectionGroup",
            Position = new Vector2(8f, 4f),
            Size = new Vector2(240f, 138f),
            MouseFilter = MouseFilterEnum.Ignore,
            ProcessMode = ProcessModeEnum.Disabled,
            Visible = false,
            ZIndex = 35
        };
        parent.AddChild(_selectionGroupPanel);
        _selectionGroupHeader = LabelText("编队", 11, Gold);
        _selectionGroupHeader.Position = new Vector2(32f, 0f);
        _selectionGroupHeader.Size = new Vector2(206f, 18f);
        _selectionGroupHeader.HorizontalAlignment = HorizontalAlignment.Center;
        _selectionGroupHeader.MouseFilter = MouseFilterEnum.Ignore;
        _selectionGroupPanel.AddChild(_selectionGroupHeader);

        var tabDivider = new ColorRect
        {
            Position = new Vector2(29f, 18f),
            Size = new Vector2(1f, 118f),
            Color = new Color("705629a0"),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _selectionGroupPanel.AddChild(tabDivider);
        _selectionPreviousPageButton = new Button
        {
            Name = "SelectionPreviousPage",
            Position = new Vector2(0f, 19f),
            Size = new Vector2(28f, 16f),
            Text = "▲",
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
            Disabled = true
        };
        StyleSelectionPageControl(_selectionPreviousPageButton);
        _selectionPreviousPageButton.Pressed += () =>
            SetSelectionGroupPage(_selectionGroupPage - 1);
        _selectionGroupPanel.AddChild(_selectionPreviousPageButton);

        for (var index = 0; index < MaximumVisibleSelectionPageTabs; index++)
        {
            var tab = new Button
            {
                Name = $"SelectionPageTab{index + 1}",
                Position = new Vector2(0f, 37f + index * 20f),
                Size = new Vector2(28f, 18f),
                FocusMode = FocusModeEnum.None,
                MouseFilter = MouseFilterEnum.Stop,
                Visible = false
            };
            StyleSelectionPageControl(tab);
            var slot = index;
            tab.Pressed += () =>
            {
                var page = _selectionPageTabIndices[slot];
                if (page >= 0) SetSelectionGroupPage(page);
            };
            _selectionGroupPanel.AddChild(tab);
            _selectionPageTabButtons[index] = tab;
            _selectionPageTabIndices[index] = -1;
        }

        _selectionNextPageButton = new Button
        {
            Name = "SelectionNextPage",
            Position = new Vector2(0f, 119f),
            Size = new Vector2(28f, 16f),
            Text = "▼",
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
            Disabled = true
        };
        StyleSelectionPageControl(_selectionNextPageButton);
        _selectionNextPageButton.Pressed += () =>
            SetSelectionGroupPage(_selectionGroupPage + 1);
        _selectionGroupPanel.AddChild(_selectionNextPageButton);

        const float gridLeft = 31f;
        const float cellWidth = 34.5f;
        const float cellHeight = 39f;
        for (var index = 0; index < SelectionGroupPageCapacity; index++)
        {
            var button = new Button
            {
                Name = $"SelectionPortrait{index + 1}",
                Position = new Vector2(
                    gridLeft + index % SelectionGroupColumns * cellWidth + 3f,
                    18f + index / SelectionGroupColumns * cellHeight + 5f),
                Size = new Vector2(28f, 28f),
                FocusMode = FocusModeEnum.None,
                MouseFilter = MouseFilterEnum.Stop,
                Visible = false
            };
            button.AddThemeStyleboxOverride(
                "normal", Box(new Color("09121be8"), Border, 1, 1));
            button.AddThemeStyleboxOverride(
                "hover", Box(Raised, Gold, 1, 2));
            button.AddThemeStyleboxOverride(
                "pressed", Box(Ink, Gold, 1, 2));
            var slot = index;
            button.Pressed += () =>
            {
                if (_selectionGroupSlotEntries[slot] is { } entry)
                    SelectionGroupEntryRequested?.Invoke(entry);
            };
            _selectionGroupPanel.AddChild(button);
            _selectionGroupButtons[index] = button;

            var portrait = new TextureRect
            {
                Position = new Vector2(2f, 2f),
                Size = new Vector2(24f, 24f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            button.AddChild(portrait);
            _selectionGroupIcons[index] = portrait;

            var healthBack = new ColorRect
            {
                Position = new Vector2(2f, 21f),
                Size = new Vector2(24f, 3f),
                Color = new Color("210909"),
                MouseFilter = MouseFilterEnum.Ignore
            };
            button.AddChild(healthBack);
            var health = new ColorRect
            {
                Size = healthBack.Size,
                Color = new Color("35b54a"),
                MouseFilter = MouseFilterEnum.Ignore
            };
            healthBack.AddChild(health);
            _selectionGroupHealthFills[index] = health;

            var manaBack = new ColorRect
            {
                Position = new Vector2(2f, 25f),
                Size = new Vector2(24f, 2f),
                Color = new Color("071629"),
                MouseFilter = MouseFilterEnum.Ignore
            };
            button.AddChild(manaBack);
            var mana = new ColorRect
            {
                Size = manaBack.Size,
                Color = new Color("1766bb"),
                MouseFilter = MouseFilterEnum.Ignore
            };
            manaBack.AddChild(mana);
            _selectionGroupManaFills[index] = mana;
        }
    }

    private static void StyleSelectionPageControl(Button button)
    {
        button.AddThemeFontSizeOverride("font_size", 9);
        button.AddThemeStyleboxOverride(
            "normal", Box(new Color("09121be8"), Border, 1, 1));
        button.AddThemeStyleboxOverride(
            "hover", Box(Raised, Gold, 1, 1));
        button.AddThemeStyleboxOverride(
            "pressed", Box(Ink, Gold, 1, 2));
        button.AddThemeStyleboxOverride(
            "disabled", Box(new Color("071019a0"), Border, 1, 1));
    }

    private void AddInventory(Control parent)
    {
        _inventoryCover = new TextureRect
        {
            Name = "InventoryCover",
            Position = InventoryCoverPosition,
            Size = InventoryCoverSize,
            Texture = War3RuntimeAssets.LoadTexture(
                @"UI\Console\Human\HumanUITile-InventoryCover.blp"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            ZIndex = 15
        };
        parent.AddChild(_inventoryCover);
        _inventoryPanel = new Control
        {
            Name = "InventorySlots",
            Position = InventoryGridPosition,
            Size = new Vector2(
                InventorySlotStride.X + InventorySlotSize.X,
                InventorySlotStride.Y * 2f + InventorySlotSize.Y),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            ZIndex = 20
        };
        parent.AddChild(_inventoryPanel);
        for (var slot = 0; slot < 6; slot++)
        {
            var frame = new Button
            {
                Name = $"InventorySlot{slot + 1}",
                Position = new Vector2(
                    slot % 2 * InventorySlotStride.X,
                    slot / 2 * InventorySlotStride.Y),
                Size = InventorySlotSize,
                FocusMode = FocusModeEnum.None,
                MouseFilter = MouseFilterEnum.Stop
            };
            frame.AddThemeStyleboxOverride("normal", Box(
                new Color("05080ccf"), new Color("806329"), 2, 1));
            frame.AddThemeStyleboxOverride("hover", Box(
                new Color("101923ef"), Gold, 2, 1));
            frame.AddThemeStyleboxOverride("pressed", Box(
                new Color("05080cef"), Gold, 2, 2));
            _inventoryPanel.AddChild(frame);
            _inventorySlots[slot] = frame;
            var inventorySlot = slot;
            frame.Pressed += () =>
                InventoryItemRequested?.Invoke(inventorySlot);
            var icon = new TextureRect
            {
                Position = new Vector2(2f, 2f),
                Size = InventorySlotSize - new Vector2(4f, 4f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            frame.AddChild(icon);
            _inventoryIcons[slot] = icon;
        }
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
            Position = CommandGridPosition,
            Size = new Vector2(
                CommandButtonStride.X * 3f + CommandButtonSize.X,
                CommandButtonStride.Y * 2f + CommandButtonSize.Y),
            MouseFilter = MouseFilterEnum.Pass,
            ZIndex = 20
        };
        parent.AddChild(_commandGrid);
        for (var index = 0; index < _commandButtons.Length; index++)
        {
            var button = CommandButton();
            button.Position = new Vector2(
                index % 4 * CommandButtonStride.X,
                index / 4 * CommandButtonStride.Y);
            button.Size = CommandButtonSize;
            _commandButtons[index] = button;
            var slot = index;
            button.Pressed += () =>
            {
                if (_slotCommands[slot] is { Enabled: true } command)
                    CommandRequested?.Invoke(command);
            };
            button.GuiInput += inputEvent =>
            {
                if (inputEvent is not InputEventMouseButton
                    { ButtonIndex: MouseButton.Right, Pressed: true } ||
                    _slotCommands[slot] is not
                    { AutoCastAvailable: true } command)
                    return;
                CommandAutoCastRequested?.Invoke(command);
                button.AcceptEvent();
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
        var animationProperties = string.Join(',',
            selection.PortraitAnimationProperties);
        if (selection.PortraitSource == _portraitSource &&
            selection.PortraitIsBuilding == _portraitBuildingView &&
            selection.PortraitTeam == _portraitTeam &&
            selection.PortraitAnimated == _portraitAnimated &&
            animationProperties == _portraitAnimationProperties) return;
        _portraitSource = selection.PortraitSource;
        _portraitBuildingView = selection.PortraitIsBuilding;
        _portraitTeam = selection.PortraitTeam;
        _portraitAnimated = selection.PortraitAnimated;
        _portraitAnimationProperties = animationProperties;
        _portraitTalkRemaining = 0d;
        _portraitActor?.QueueFree();
        _portraitActor = null;
        if (_portraitSource.Length == 0 || !War3RuntimeAssets.Contains(_portraitSource))
            return;
        try
        {
            _portraitActor = new War3ModelActor { Name = "SelectedPortrait" };
            _portraitWorld.AddChild(_portraitActor);
            _portraitActor.Load(_portraitSource, _portraitCamera,
                selection.PortraitTeam, includeEffects: false);
            if (selection.PortraitAnimated)
            {
                if (selection.PortraitIsBuilding)
                    _portraitActor.PlayRepeatedPreferred(
                        War3AnimationPropertyResolver.Portrait(
                            selection.PortraitAnimationProperties));
                else
                    _portraitActor.PlayRepeatedPreferred("Portrait", "Stand");
            }
            else if (selection.PortraitIsBuilding)
                _portraitActor.SetSequenceProgress(
                    0f, War3AnimationPropertyResolver.Portrait(
                        selection.PortraitAnimationProperties));
            else
                _portraitActor.SetSequenceProgress(0f, "Portrait", "Stand");
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

    private static void AddIconStat(
        GridContainer parent,
        string caption,
        out TextureRect icon,
        out Label value)
    {
        var cell = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(62f, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        cell.AddThemeConstantOverride("separation", 3);
        icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(18f, 18f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        cell.AddChild(icon);
        var label = LabelText(caption, 10, Muted);
        label.VerticalAlignment = VerticalAlignment.Center;
        cell.AddChild(label);
        parent.AddChild(cell);
        value = LabelText("—", 12, Text);
        value.CustomMinimumSize = new Vector2(38f, 18f);
        value.VerticalAlignment = VerticalAlignment.Center;
        parent.AddChild(value);
    }

    private static Label PortraitBarLabel()
    {
        var label = LabelText(string.Empty, 9, Colors.White);
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.ZIndex = 2;
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private void UpdateHeroAndInventory(War3SelectionSnapshot selection)
    {
        if (_heroProgressPanel is not null && _heroLevelLabel is not null &&
            _heroExperienceBar is not null && _heroExperienceLabel is not null)
        {
            _heroProgressPanel.Visible = selection.IsHero;
            _heroLevelLabel.Text = selection.IsHero
                ? $"英雄 {selection.Level}"
                : string.Empty;
            var next = Math.Max(0, selection.ExperienceForNextLevel);
            var experience = Math.Max(0, selection.HeroExperience);
            _heroExperienceBar.Value = next > 0
                ? Math.Clamp(experience / (double)next, 0d, 1d) * 100d
                : 100d;
            _heroExperienceLabel.Text = selection.IsHero
                ? next > 0
                    ? $"{experience}/{next}"
                    : "最高等级"
                : string.Empty;
        }
        if (_inventoryPanel is not null)
        {
            _inventoryPanel.Visible = selection.SupportsInventory;
            var itemsBySlot = selection.InventoryItems
                .Where(item => item.Slot >= 0)
                .ToDictionary(item => item.Slot);
            for (var index = 0; index < _inventorySlots.Length; index++)
            {
                var visible = selection.SupportsInventory &&
                              index < selection.InventorySlotCount;
                _inventorySlots[index].Visible = visible;
                if (!visible || !itemsBySlot.TryGetValue(index, out var item))
                {
                    _inventoryIcons[index].Texture = null;
                    _inventorySlots[index].TooltipText = "空物品栏";
                    _inventorySlots[index].Disabled = true;
                    _inventorySlots[index].Modulate = Colors.White;
                    continue;
                }
                _inventoryIcons[index].Texture =
                    War3RuntimeAssets.LoadTexture(item.IconPath);
                _inventorySlots[index].Disabled = !item.Usable;
                _inventorySlots[index].Modulate = item.Passive
                    ? new Color("ffd77a")
                    : item.CooldownRemaining > 0f
                        ? new Color(0.55f, 0.55f, 0.55f, 0.82f)
                        : Colors.White;
                _inventorySlots[index].TooltipText = item.Name +
                    (item.Charges > 0 ? $" · {item.Charges} 次" : string.Empty) +
                    $"\n{item.Tooltip}\n状态：{item.StateLabel}";
            }
        }
        if (_inventoryCover is not null)
            _inventoryCover.Visible = !selection.SupportsInventory;
    }

    private void UpdateConstructionProgress(War3SelectionSnapshot selection)
    {
        if (_constructionProgressPanel is null ||
            _constructionProgress is null ||
            _constructionProgressLabel is null)
            return;
        _constructionProgressPanel.Visible = selection.IsConstructing;
        var progress = Math.Clamp(selection.ConstructionProgress, 0f, 1f);
        _constructionProgress.Value = progress * 100d;
        _constructionProgressLabel.Text = selection.IsConstructing
            ? $"建造 {progress:P0}"
            : string.Empty;
    }

    private void UpdateSelectionGroup(War3SelectionSnapshot selection)
    {
        if (_selectionGroupPanel is null || _selectionGroupHeader is null)
            return;
        var entries = selection.GroupEntries;
        var activeKey = entries.FirstOrDefault(value => value.ActiveSubgroup)
            .SubgroupKey ?? string.Empty;
        var activeKeyChanged = !_selectionGroupActiveKey.Equals(
            activeKey, StringComparison.Ordinal);
        _selectionGroupEntries = entries;
        _selectionGroupActiveKey = activeKey;
        if (activeKeyChanged)
        {
            var activeEntry = Array.FindIndex(
                entries, value => value.ActiveSubgroup);
            if (activeEntry >= 0)
                _selectionGroupPage =
                    activeEntry / SelectionGroupPageCapacity;
        }
        var pageCount = SelectionGroupPageCount;
        _selectionGroupPage = Math.Clamp(
            _selectionGroupPage, 0, pageCount - 1);
        UpdateSelectionPageTabs(pageCount);
        RenderSelectionGroupPage();
    }

    private void SetSelectionGroupPage(int page)
    {
        var clamped = Math.Clamp(page, 0, SelectionGroupPageCount - 1);
        if (_selectionGroupPage == clamped) return;
        _selectionGroupPage = clamped;
        UpdateSelectionPageTabs(SelectionGroupPageCount);
        RenderSelectionGroupPage();
    }

    private void UpdateSelectionPageTabs(int pageCount)
    {
        if (_selectionGroupHeader is null) return;
        var subgroupKeys = _selectionGroupEntries
            .Select(value => value.SubgroupKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var activeIndex = Array.IndexOf(
            subgroupKeys, _selectionGroupActiveKey);
        _selectionGroupHeader.Text = _selectionGroupEntries.Length > 1
            ? $"编队 {_selectionGroupEntries.Length} · " +
              $"页 {_selectionGroupPage + 1}/{pageCount} · " +
              $"子组 {Math.Max(0, activeIndex) + 1}/" +
              $"{Math.Max(1, subgroupKeys.Length)}"
            : string.Empty;

        var firstVisiblePage = Math.Clamp(
            _selectionGroupPage - 1,
            0,
            Math.Max(0, pageCount - MaximumVisibleSelectionPageTabs));
        for (var index = 0; index < _selectionPageTabButtons.Length; index++)
        {
            var tab = _selectionPageTabButtons[index];
            var page = firstVisiblePage + index;
            if (page >= pageCount || _selectionGroupEntries.Length == 0)
            {
                _selectionPageTabIndices[index] = -1;
                tab.Visible = false;
                tab.Text = string.Empty;
                tab.TooltipText = string.Empty;
                continue;
            }
            _selectionPageTabIndices[index] = page;
            tab.Visible = true;
            tab.Text = (page + 1).ToString();
            var firstEntry = page * SelectionGroupPageCapacity + 1;
            var lastEntry = Math.Min(
                (page + 1) * SelectionGroupPageCapacity,
                _selectionGroupEntries.Length);
            tab.TooltipText =
                $"选择集合第 {page + 1} 页（{firstEntry}–{lastEntry}）";
            var active = page == _selectionGroupPage;
            tab.AddThemeStyleboxOverride(
                "normal", Box(new Color("09121be8"),
                    active ? Gold : Border, 1, active ? 2 : 1));
        }

        if (_selectionPreviousPageButton is not null)
        {
            _selectionPreviousPageButton.Visible = pageCount > 1;
            _selectionPreviousPageButton.Disabled = _selectionGroupPage == 0;
            _selectionPreviousPageButton.TooltipText = "上一页选择单位";
        }
        if (_selectionNextPageButton is not null)
        {
            _selectionNextPageButton.Visible = pageCount > 1;
            _selectionNextPageButton.Disabled =
                _selectionGroupPage >= pageCount - 1;
            _selectionNextPageButton.TooltipText = "下一页选择单位";
        }
    }

    private void RenderSelectionGroupPage()
    {
        var pageOffset = _selectionGroupPage * SelectionGroupPageCapacity;
        const float cellWidth = 34.5f;
        const float cellHeight = 39f;
        for (var index = 0; index < _selectionGroupButtons.Length; index++)
        {
            var button = _selectionGroupButtons[index];
            _selectionGroupSlotEntries[index] = null;
            var entryIndex = pageOffset + index;
            if (entryIndex >= _selectionGroupEntries.Length)
            {
                button.Visible = false;
                _selectionGroupIcons[index].Texture = null;
                continue;
            }
            var entry = _selectionGroupEntries[entryIndex];
            _selectionGroupSlotEntries[index] = entry;
            var active = entry.ActiveSubgroup;
            var size = active ? new Vector2(30f, 30f) : new Vector2(28f, 28f);
            var cellOrigin = new Vector2(
                31f + index % SelectionGroupColumns * cellWidth,
                18f + index / SelectionGroupColumns * cellHeight);
            button.Position = cellOrigin + new Vector2(
                (cellWidth - size.X) * 0.5f,
                (cellHeight - size.Y) * 0.5f);
            button.Size = size;
            button.Visible = true;
            _selectionGroupIcons[index].Texture =
                War3RuntimeAssets.LoadTexture(entry.IconPath);
            _selectionGroupIcons[index].Position = new Vector2(2f, 2f);
            _selectionGroupIcons[index].Size = size - new Vector2(4f, 4f);
            button.TooltipText = entry.Name +
                                 (entry.HeroLevel > 0
                                     ? $" · 等级 {entry.HeroLevel}"
                                     : string.Empty) +
                                 $"\n生命 {entry.HealthRatio:P0}" +
                                 (entry.ManaRatio > 0f
                                     ? $" · 法力 {entry.ManaRatio:P0}"
                                     : string.Empty) +
                                 (active
                                     ? "\n当前子组；再次点击仅选择这个单位"
                                     : "\n点击切换到该子组");
            var border = entry.Debuffed
                ? new Color("dc493f")
                : active ? Gold : Border;
            button.AddThemeStyleboxOverride(
                "normal", Box(new Color("09121be8"), border, 1, active ? 2 : 1));

            var barWidth = MathF.Max(1f, size.X - 4f);
            if (_selectionGroupHealthFills[index].GetParent() is ColorRect healthBack)
            {
                healthBack.Position = new Vector2(2f, size.Y - 7f);
                healthBack.Size = new Vector2(barWidth, 3f);
                _selectionGroupHealthFills[index].Size = new Vector2(
                    barWidth * Math.Clamp(entry.HealthRatio, 0f, 1f), 3f);
            }
            if (_selectionGroupManaFills[index].GetParent() is ColorRect manaBack)
            {
                manaBack.Visible = entry.ManaRatio > 0f;
                manaBack.Position = new Vector2(2f, size.Y - 3f);
                manaBack.Size = new Vector2(barWidth, 2f);
                _selectionGroupManaFills[index].Size = new Vector2(
                    barWidth * Math.Clamp(entry.ManaRatio, 0f, 1f), 2f);
            }
        }
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
            $"{MathF.Ceiling(value.CooldownRemaining * 10f)}:{value.Toggled}:" +
            $"{value.State}:{value.Badge}:{value.IconPath}:{value.Hotkey}:" +
            $"{value.AutoCastAvailable}"));
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
            button.Disabled = !command.Enabled && !command.AutoCastAvailable;
            button.Modulate = command.State switch
            {
                War3CommandVisualState.Unavailable =>
                    new Color(0.5f, 0.5f, 0.5f, 0.78f),
                War3CommandVisualState.Completed =>
                    new Color("8fd7c9"),
                War3CommandVisualState.Queued =>
                    new Color("d8bd72"),
                War3CommandVisualState.Learn =>
                    new Color("fff0a2"),
                War3CommandVisualState.Passive =>
                    new Color("d6e9ff"),
                _ => command.Enabled
                    ? Colors.White
                    : new Color(0.55f, 0.55f, 0.55f, 0.82f)
            };
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
            if (command.Badge.Length > 0)
            {
                var badge = LabelText(command.Badge, 9,
                    command.State == War3CommandVisualState.Completed
                        ? new Color("d6fff3")
                        : Gold);
                badge.SetAnchorsPreset(LayoutPreset.BottomRight);
                badge.OffsetLeft = -35f;
                badge.OffsetTop = -16f;
                badge.OffsetRight = -2f;
                badge.OffsetBottom = -2f;
                badge.HorizontalAlignment = HorizontalAlignment.Right;
                badge.VerticalAlignment = VerticalAlignment.Center;
                badge.MouseFilter = MouseFilterEnum.Ignore;
                badge.AddThemeColorOverride("font_shadow_color", Colors.Black);
                badge.AddThemeConstantOverride("shadow_offset_x", 1);
                badge.AddThemeConstantOverride("shadow_offset_y", 1);
                button.AddChild(badge);
            }
            if (command.State is War3CommandVisualState.Completed or
                War3CommandVisualState.Queued or War3CommandVisualState.Learn)
            {
                var color = command.State switch
                {
                    War3CommandVisualState.Completed => new Color("62d7b0"),
                    War3CommandVisualState.Queued => new Color("e0ad45"),
                    _ => new Color("ffe46c")
                };
                var marker = new ColorRect
                {
                    Position = new Vector2(1f, 1f),
                    Size = new Vector2(50f, 2f),
                    Color = color,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                button.AddChild(marker);
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
        private const int MarkerBufferStride = 12;
        private const int CircleSegmentCount = 32;
        private SimRect _bounds;
        private SimRect _cameraBounds;
        private War3MinimapEntity[] _entities = [];
        private War3MinimapResource[] _resources = [];
        private Texture2D? _fogTexture;
        private ArrayMesh? _circleMarkerMesh;
        private ArrayMesh? _squareMarkerMesh;
        private MultiMesh? _circleMarkers;
        private MultiMesh? _squareMarkers;
        private float[] _circleMarkerBuffer = [];
        private float[] _squareMarkerBuffer = [];

        public event Action<System.Numerics.Vector2>? FocusRequested;

        public bool FogTextureReady => _fogTexture is not null;

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
            MouseDefaultCursorShape = CursorShape.Arrow;
            _circleMarkerMesh = CreateMarkerMesh(circle: true);
            _squareMarkerMesh = CreateMarkerMesh(circle: false);
        }

        public void SetSnapshot(
            SimRect bounds,
            SimRect cameraBounds,
            War3MinimapEntity[] entities,
            War3MinimapResource[] resources,
            bool signalMode)
        {
            _bounds = bounds;
            _cameraBounds = cameraBounds;
            _entities = entities;
            _resources = resources;
            MouseDefaultCursorShape = signalMode
                ? CursorShape.Cross
                : CursorShape.Arrow;
            QueueRedraw();
        }

        public void SetFogTexture(Texture2D texture)
        {
            _fogTexture = texture;
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
            if (_fogTexture is not null)
            {
                DrawTextureRect(
                    _fogTexture,
                    mapRect,
                    tile: false,
                    modulate: Colors.White,
                    transpose: false);
            }
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
            var resourceCount = 0;
            for (var index = 0; index < _resources.Length; index++)
            {
                var resource = _resources[index];
                if (resource.Depleted) continue;
                resourceCount++;
            }
            var unitCount = 0;
            for (var index = 0; index < _entities.Length; index++)
            {
                if (!_entities[index].Building) unitCount++;
            }
            var circleCount = resourceCount + unitCount;
            var buildingCount = _entities.Length - unitCount;
            EnsureMarkerCapacity(
                ref _circleMarkers,
                ref _circleMarkerBuffer,
                circleCount,
                _circleMarkerMesh!);
            EnsureMarkerCapacity(
                ref _squareMarkers,
                ref _squareMarkerBuffer,
                buildingCount,
                _squareMarkerMesh!);

            var circleIndex = 0;
            for (var index = 0; index < _resources.Length; index++)
            {
                var resource = _resources[index];
                if (resource.Depleted) continue;
                var point = ToMap(resource.Position);
                WriteMarker(
                    _circleMarkerBuffer,
                    circleIndex++,
                    point,
                    resource.Kind == RtsDemo.Simulation.EconomyResourceKind.Minerals
                        ? 3.2f : 1.8f,
                    resource.Kind == RtsDemo.Simulation.EconomyResourceKind.Minerals
                        ? new Color("efc74f") : new Color("4d9a5c"));
            }
            for (var index = 0; index < _entities.Length; index++)
            {
                var entity = _entities[index];
                if (entity.Building) continue;
                var color = entity.Team == War3HumanScenario.PlayerId
                    ? new Color("42bff5")
                    : new Color("ef5f5f");
                WriteMarker(
                    _circleMarkerBuffer,
                    circleIndex++,
                    ToMap(entity.Position),
                    2f,
                    color);
            }
            SubmitMarkers(_circleMarkers!, _circleMarkerBuffer, circleIndex);
            DrawMultimesh(_circleMarkers!, null!);

            var squareIndex = 0;
            for (var index = 0; index < _entities.Length; index++)
            {
                var entity = _entities[index];
                if (!entity.Building) continue;
                var color = entity.Team == War3HumanScenario.PlayerId
                    ? new Color("42bff5")
                    : new Color("ef5f5f");
                WriteMarker(
                    _squareMarkerBuffer,
                    squareIndex++,
                    ToMap(entity.Position),
                    2.8f,
                    color);
            }
            SubmitMarkers(_squareMarkers!, _squareMarkerBuffer, squareIndex);
            DrawMultimesh(_squareMarkers!, null!);
            if (_cameraBounds.Width > 0f && _cameraBounds.Height > 0f)
            {
                var minimum = ToMap(_cameraBounds.Min);
                var maximum = ToMap(_cameraBounds.Max);
                var cameraRect = new Rect2(minimum, maximum - minimum)
                    .Intersection(mapRect);
                if (cameraRect.Size.X > 1f && cameraRect.Size.Y > 1f)
                {
                    DrawRect(cameraRect, new Color("f6eee0d9"), false, 1.4f);
                    DrawRect(cameraRect.Grow(-1.4f),
                        new Color("08110ca0"), false, 1f);
                }
            }
        }

        private static void EnsureMarkerCapacity(
            ref MultiMesh? markers,
            ref float[] buffer,
            int required,
            ArrayMesh mesh)
        {
            if (markers is not null && markers.InstanceCount >= required) return;
            var capacity = 16;
            while (capacity < required) capacity *= 2;
            markers = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = capacity,
                VisibleInstanceCount = 0
            };
            buffer = new float[capacity * MarkerBufferStride];
        }

        private static void WriteMarker(
            float[] buffer,
            int index,
            Vector2 point,
            float scale,
            Color color)
        {
            var offset = index * MarkerBufferStride;
            buffer[offset] = scale;
            buffer[offset + 1] = 0f;
            buffer[offset + 2] = 0f;
            buffer[offset + 3] = point.X;
            buffer[offset + 4] = 0f;
            buffer[offset + 5] = scale;
            buffer[offset + 6] = 0f;
            buffer[offset + 7] = point.Y;
            buffer[offset + 8] = color.R;
            buffer[offset + 9] = color.G;
            buffer[offset + 10] = color.B;
            buffer[offset + 11] = color.A;
        }

        private static void SubmitMarkers(
            MultiMesh markers,
            float[] buffer,
            int count)
        {
            if (count > 0)
                RenderingServer.MultimeshSetBuffer(markers.GetRid(), buffer);
            markers.VisibleInstanceCount = count;
        }

        private static ArrayMesh CreateMarkerMesh(bool circle)
        {
            var tool = new SurfaceTool();
            tool.Begin(Mesh.PrimitiveType.Triangles);
            tool.SetColor(Colors.White);
            if (circle)
            {
                for (var segment = 0; segment < CircleSegmentCount; segment++)
                {
                    var angle0 = Mathf.Tau * segment / CircleSegmentCount;
                    var angle1 = Mathf.Tau * (segment + 1) / CircleSegmentCount;
                    tool.AddVertex(Vector3.Zero);
                    tool.AddVertex(new Vector3(
                        MathF.Cos(angle0), MathF.Sin(angle0), 0f));
                    tool.AddVertex(new Vector3(
                        MathF.Cos(angle1), MathF.Sin(angle1), 0f));
                }
            }
            else
            {
                tool.AddVertex(new Vector3(-1f, -1f, 0f));
                tool.AddVertex(new Vector3(1f, -1f, 0f));
                tool.AddVertex(new Vector3(1f, 1f, 0f));
                tool.AddVertex(new Vector3(-1f, -1f, 0f));
                tool.AddVertex(new Vector3(1f, 1f, 0f));
                tool.AddVertex(new Vector3(-1f, 1f, 0f));
            }
            return tool.Commit();
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
