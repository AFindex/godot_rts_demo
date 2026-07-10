using Godot;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;

namespace RtsDemo.GodotRuntime;

[Tool]
public partial class ClearancePreview2D : Node2D
{
    private static readonly Color SmallColor = new("58d68d");
    private static readonly Color MediumColor = new("f4d03f");
    private static readonly Color LargeColor = new("ec7063");
    private static readonly Color[] ComponentColors =
    [
        new("3498db"), new("9b59b6"), new("1abc9c"),
        new("e67e22"), new("f1c40f"), new("e74c3c")
    ];

    private NavigationMapSnapshot? _runtimeNavigation;
    private GameplayProfileCatalogSnapshot? _runtimeProfiles;
    private ClearanceBakeSnapshot? _runtimeClearanceBake;
    private ClearancePreviewSnapshot? _cachedPreview;
    private ulong _cachedNavigationHash;
    private ulong _cachedProfilesHash;
    private ulong _cachedBakeHash;
    private double _redrawTimer;

    [Export]
    public bool Enabled { get; set; } = true;

    [Export]
    public RtsNavigationMapResource? NavigationMapAsset { get; set; }

    [Export]
    public RtsGameplayProfilesResource? GameplayProfilesAsset { get; set; }

    [Export]
    public RtsClearanceBakeResource? ClearanceBakeAsset { get; set; }

    [Export]
    public bool DrawConnectivity { get; set; } = true;

    [Export]
    public bool DrawBakeChunks { get; set; } = true;

    [Export]
    public MovementClass ConnectivityClass { get; set; } = MovementClass.Large;

    public bool RuntimePreviewEnabled { get; private set; }

    public override void _Ready()
    {
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            return;
        }

        _redrawTimer += delta;
        if (_redrawTimer >= 0.5)
        {
            _redrawTimer = 0.0;
            QueueRedraw();
        }
    }

    public void SetRuntimeSnapshots(
        NavigationMapSnapshot? navigation,
        GameplayProfileCatalogSnapshot? profiles,
        bool enabled,
        ClearanceBakeSnapshot? clearanceBake = null)
    {
        _runtimeNavigation = navigation;
        _runtimeProfiles = profiles;
        _runtimeClearanceBake = clearanceBake;
        _cachedPreview = null;
        RuntimePreviewEnabled = enabled;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Enabled || (!Engine.IsEditorHint() && !RuntimePreviewEnabled) ||
            !TryCreatePreview(out var preview))
        {
            return;
        }

        DrawRect(ToRect(preview.WorldBounds), new Color("6c7a89"), false, 2f);
        if (DrawConnectivity)
        {
            DrawConnectivityCells(preview.Connectivity[(int)ConnectivityClass]);
        }

        if (DrawBakeChunks)
        {
            DrawChunkGrid(preview.BakeChunks);
        }

        for (var obstacleIndex = 0;
             obstacleIndex < preview.Obstacles.Length;
             obstacleIndex++)
        {
            var obstacle = preview.Obstacles[obstacleIndex];
            DrawRect(ToRect(obstacle), new Color(0.2f, 0.23f, 0.27f, 0.72f), true);
            DrawClearanceOutline(obstacle, MovementClass.Large, LargeColor);
            DrawClearanceOutline(obstacle, MovementClass.Medium, MediumColor);
            DrawClearanceOutline(obstacle, MovementClass.Small, SmallColor);
        }

        for (var edgeIndex = 0; edgeIndex < preview.Portals.Length; edgeIndex++)
        {
            var edge = preview.Portals[edgeIndex];
            var color = edge.LargeTraversable
                ? new Color("5dade2")
                : edge.MediumTraversable
                    ? MediumColor
                    : edge.SmallTraversable
                        ? SmallColor
                        : LargeColor;
            DrawLine(ToGodot(edge.From), ToGodot(edge.To), color, 3f);
            var center = ToGodot((edge.From + edge.To) * 0.5f);
            DrawLabel(
                center + new Vector2(0f, -8f),
                $"{edge.Width:0}px {edge.ClassLabel}",
                color);
        }

        DrawLegend(preview);
        DrawBuildingPalette(preview);
    }

    private bool TryCreatePreview(out ClearancePreviewSnapshot preview)
    {
        var navigation = _runtimeNavigation;
        var profiles = _runtimeProfiles;
        var clearanceBake = _runtimeClearanceBake;
        if (navigation is null && NavigationMapAsset is not null)
        {
            NavigationMapResourceConverter.TryConvert(
                NavigationMapAsset, out navigation, out _);
        }

        if (profiles is null && GameplayProfilesAsset is not null)
        {
            GameplayProfileResourceConverter.TryConvert(
                GameplayProfilesAsset, out profiles, out _);
        }

        if (clearanceBake is null && navigation is not null &&
            ClearanceBakeAsset is not null)
        {
            ClearanceBakeResourceConverter.TryConvert(
                ClearanceBakeAsset,
                navigation.StableHash,
                out clearanceBake,
                out _);
        }

        if (navigation is null || profiles is null)
        {
            preview = null!;
            return false;
        }

        if (_cachedPreview is not null &&
            _cachedNavigationHash == navigation.StableHash &&
            _cachedProfilesHash == profiles.StableHash &&
            _cachedBakeHash == (clearanceBake?.StableHash ?? 0UL))
        {
            preview = _cachedPreview;
            return true;
        }

        preview = ClearancePreviewSnapshot.Create(
            navigation, profiles, clearanceBake);
        _cachedPreview = preview;
        _cachedNavigationHash = navigation.StableHash;
        _cachedProfilesHash = profiles.StableHash;
        _cachedBakeHash = clearanceBake?.StableHash ?? 0UL;
        return true;
    }

    private void DrawConnectivityCells(NavigationConnectivitySnapshot connectivity)
    {
        for (var node = 0; node < connectivity.NodeCount; node++)
        {
            if (!connectivity.IsWalkable(node))
            {
                continue;
            }

            var component = connectivity.ComponentAt(node);
            var baseColor = ComponentColors[component % ComponentColors.Length];
            DrawRect(
                ToRect(connectivity.CellBounds(node)),
                baseColor with { A = 0.055f },
                true);
        }
    }

    private void DrawChunkGrid(ReadOnlySpan<ClearanceBakeChunk> chunks)
    {
        var color = new Color(0.35f, 0.85f, 1f, 0.28f);
        for (var index = 0; index < chunks.Length; index++)
        {
            var chunk = chunks[index];
            var rect = ToRect(chunk.WorldBounds);
            DrawRect(rect, color, false, 1f);
            DrawLabel(
                rect.Position + new Vector2(4f, 15f),
                $"C{chunk.Id}",
                color with { A = 0.75f });
        }
    }

    private void DrawClearanceOutline(
        SimRect obstacle,
        MovementClass movementClass,
        Color color)
    {
        var radius = MovementClearance.ForClass(movementClass).NavigationRadius;
        DrawRect(ToRect(obstacle.Expanded(radius)), color with { A = 0.62f }, false, 1.5f);
    }

    private void DrawLegend(ClearancePreviewSnapshot preview)
    {
        // Keep the runtime review recording clear of the demo HUD. In the editor this
        // also leaves the world-boundary and portal labels unobstructed.
        var position = ToGodot(preview.WorldBounds.Min) + new Vector2(12f, 82f);
        for (var index = 0; index < preview.Classes.Length; index++)
        {
            var value = preview.Classes[index];
            var color = value.Class switch
            {
                MovementClass.Small => SmallColor,
                MovementClass.Medium => MediumColor,
                _ => LargeColor
            };
            DrawLabel(
                position + new Vector2(0f, index * 20f),
                $"{value.Class}: r{value.NavigationRadius:0} " +
                $"width>={value.RequiredWidth:0} edges={value.TraversablePortalEdges} " +
                $"components={value.ConnectedComponents} " +
                $"source={value.ConnectivitySource}",
                color);
        }
    }

    private void DrawBuildingPalette(ClearancePreviewSnapshot preview)
    {
        var bounds = preview.WorldBounds;
        var basePosition = new System.Numerics.Vector2(
            bounds.Min.X + 90f,
            bounds.Max.Y - 65f);
        for (var index = 0; index < preview.Buildings.Length; index++)
        {
            var building = preview.Buildings[index];
            var center = basePosition + new System.Numerics.Vector2(index * 245f, 0f);
            var halfSize = building.Size * 0.5f;
            var footprint = new SimRect(center - halfSize, center + halfSize);
            var clearance = footprint.Expanded(building.RequiredPassageWidth * 0.5f);
            DrawRect(ToRect(clearance), new Color(0.35f, 0.75f, 1f, 0.42f), false, 1f);
            DrawRect(ToRect(footprint), new Color(0.12f, 0.45f, 0.7f, 0.55f), true);
            DrawLabel(
                ToGodot(center) + new Vector2(-halfSize.X, -halfSize.Y - 7f),
                $"{building.Name} {building.Size.X:0}x{building.Size.Y:0}",
                new Color("bde7ff"));
        }
    }

    private void DrawLabel(Vector2 position, string text, Color color) =>
        DrawString(
            ThemeDB.FallbackFont,
            position,
            text,
            HorizontalAlignment.Left,
            -1f,
            13,
            color);

    private static Rect2 ToRect(SimRect rect) =>
        new(ToGodot(rect.Min), ToGodot(rect.Max - rect.Min));

    private static Vector2 ToGodot(System.Numerics.Vector2 value) =>
        new(value.X, value.Y);
}
