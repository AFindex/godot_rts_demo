using Godot;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime;

[Flags]
public enum TerrainAuthoringOverlay
{
    Surface = 1 << 0,
    Height = 1 << 1,
    GroundPathing = 1 << 2,
    WaterPathing = 1 << 3,
    AirBlocking = 1 << 4,
    Buildable = 1 << 5,
    Vision = 1 << 6,
    Ramp = 1 << 7,
    Creep = 1 << 8,
    Validation = 1 << 9
}

[Tool]
[GlobalClass]
public partial class RtsTerrainAuthoring2D : Node2D
{
    private static readonly Color[] SurfaceColors =
    [
        new("667b55"), new("7f7562"), new("a98c58"), new("527d91"),
        new("6d6e82"), new("8b655f"), new("497a70"), new("8a744e")
    ];

    private TerrainAuthoringValidationResult? _lastValidation;
    private RtsTerrainAuthoringResource? _authoringAsset;
    private TerrainAuthoringOverlay _overlays =
        TerrainAuthoringOverlay.Surface |
        TerrainAuthoringOverlay.Height |
        TerrainAuthoringOverlay.Ramp |
        TerrainAuthoringOverlay.Validation;
    private string? _strokeBefore;
    private TerrainAuthoringDocument? _strokeDocument;
    private bool _strokeChanged;
    private int _lastStrokeColumn = -1;
    private int _lastStrokeRow = -1;

    [Export]
    public RtsTerrainAuthoringResource? AuthoringAsset
    {
        get => _authoringAsset;
        set
        {
            if (ReferenceEquals(_authoringAsset, value)) return;
            if (_authoringAsset is not null)
                _authoringAsset.Changed -= AuthoringAssetChanged;
            _authoringAsset = value;
            if (_authoringAsset is not null)
                _authoringAsset.Changed += AuthoringAssetChanged;
            _lastValidation = null;
            QueueRedraw();
        }
    }

    [Export]
    public bool SaveAuthoringAssetOnExport { get; set; } = true;

    [Export(PropertyHint.Flags,
        "Surface,Height,Ground Pathing,Water Pathing,Air Blocking," +
        "Buildable,Vision,Ramp,Creep,Validation")]
    public TerrainAuthoringOverlay Overlays
    {
        get => _overlays;
        set
        {
            if (_overlays == value) return;
            _overlays = value;
            QueueRedraw();
        }
    }

    [Export]
    public TerrainBrushKind BrushKind { get; set; } =
        TerrainBrushKind.CliffLevel;

    [Export(PropertyHint.Range, "0,16,1")]
    public int BrushRadiusCells { get; set; } = 1;

    [Export(PropertyHint.Range, "0,15,1")]
    public int BrushCliffLevel { get; set; }

    [Export(PropertyHint.Range, "0,65535,1")]
    public int BrushSurfaceId { get; set; }

    [Export(PropertyHint.Flags, "Ground,Shallow Water,Deep Water,Air Blocked")]
    public TerrainPathing BrushPathing { get; set; } = TerrainPathing.Ground;

    [Export]
    public bool BrushEnabled { get; set; } = true;

    [Export]
    public TerrainRampDirection BrushRampDirection { get; set; } =
        TerrainRampDirection.PositiveX;

    public TerrainAuthoringValidationResult? LastValidation => _lastValidation;

    public override void _Ready()
    {
        if (_authoringAsset is not null)
        {
            _authoringAsset.Changed -= AuthoringAssetChanged;
            _authoringAsset.Changed += AuthoringAssetChanged;
        }
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        if (_authoringAsset is not null)
            _authoringAsset.Changed -= AuthoringAssetChanged;
    }

    public override void _Draw()
    {
        var document = _strokeDocument;
        if (document is null && !TryDocument(out document, out _)) return;
        for (var row = 0; row < document.Rows; row++)
        {
            for (var column = 0; column < document.Columns; column++)
            {
                var cell = document.Cell(column, row);
                var rect = CellRect(document, column, row);
                DrawRect(rect, CellColor(cell), true);
                DrawOverlays(rect, cell);
                DrawRect(rect, new Color(0f, 0f, 0f, 0.22f), false, 1f);
                if ((Overlays & TerrainAuthoringOverlay.Height) != 0)
                {
                    DrawString(
                        ThemeDB.FallbackFont,
                        rect.Position + new Vector2(4f, 14f),
                        cell.CliffLevel.ToString(),
                        HorizontalAlignment.Left,
                        -1f,
                        11,
                        Colors.White);
                }
                if ((Overlays & TerrainAuthoringOverlay.Ramp) != 0 && cell.IsRamp)
                    DrawRampArrow(rect, cell.RampDirection);
            }
        }
        foreach (var anchor in document.Anchors)
            DrawAnchor(anchor);
        if ((Overlays & TerrainAuthoringOverlay.Validation) != 0 &&
            _lastValidation is not null)
        {
            DrawValidation(document, _lastValidation);
        }
    }

    public bool BeginStroke(Vector2 canvasPosition)
    {
        if (AuthoringAsset is null || !HasValidCellArrays(AuthoringAsset))
            return false;
        if (!TryDocument(out _strokeDocument, out _)) return false;
        _strokeBefore = AuthoringAsset.CaptureCellPayloadBase64();
        _strokeChanged = false;
        _lastStrokeColumn = -1;
        _lastStrokeRow = -1;
        ContinueStroke(canvasPosition);
        return true;
    }

    public bool ContinueStroke(Vector2 canvasPosition)
    {
        var document = _strokeDocument;
        if (document is null || AuthoringAsset is null)
            return false;
        var local = GetGlobalTransformWithCanvas().AffineInverse() * canvasPosition;
        var world = new NVector2(local.X, local.Y);
        if (!document.TryCellAt(world, out var column, out var row) ||
            column == _lastStrokeColumn && row == _lastStrokeRow)
        {
            return false;
        }
        var brush = CurrentBrush();
        var changed = document.ApplyBrush(column, row, brush);
        _lastStrokeColumn = column;
        _lastStrokeRow = row;
        if (changed <= 0) return false;
        _strokeChanged = true;
        _lastValidation = null;
        QueueRedraw();
        return true;
    }

    public TerrainAuthoringStrokeResult EndStroke()
    {
        if (_strokeBefore is null || AuthoringAsset is null)
            return default;
        var before = _strokeBefore;
        if (_strokeChanged && _strokeDocument is not null)
        {
            TerrainAuthoringResourceConverter.WriteCells(
                AuthoringAsset, _strokeDocument.Cells);
        }
        var after = AuthoringAsset.CaptureCellPayloadBase64();
        _strokeBefore = null;
        _strokeDocument = null;
        _strokeChanged = false;
        _lastStrokeColumn = -1;
        _lastStrokeRow = -1;
        return new TerrainAuthoringStrokeResult(before != after, before, after);
    }

    public void CancelStroke()
    {
        _strokeBefore = null;
        _strokeDocument = null;
        _strokeChanged = false;
        _lastStrokeColumn = -1;
        _lastStrokeRow = -1;
    }

    public void RestoreCellPayload(string payloadBase64)
    {
        AuthoringAsset?.RestoreCellPayloadBase64(payloadBase64);
        _lastValidation = null;
        QueueRedraw();
    }

    public TerrainAuthoringValidationResult ValidateAuthoring()
    {
        TerrainAuthoringValidationResult validation;
        if (AuthoringAsset is null)
        {
            validation = new TerrainAuthoringValidationResult([
                new TerrainAuthoringIssue(
                    TerrainAuthoringIssueSeverity.Error,
                    TerrainAuthoringErrorCode.InvalidDocument,
                    -1,
                    -1,
                    "No terrain authoring resource is assigned.")
            ]);
            _lastValidation = validation;
        }
        else if (!TerrainAuthoringResourceConverter.TryExport(
                     AuthoringAsset, out _, out validation))
        {
            _lastValidation = validation;
        }
        else
        {
            _lastValidation = validation;
        }
        QueueRedraw();
        return _lastValidation;
    }

    public TerrainAuthoringExportResult ExportRuntimeResource()
    {
        if (AuthoringAsset is null)
            return new TerrainAuthoringExportResult(false, string.Empty,
                "No terrain authoring resource is assigned.");
        if (!TerrainAuthoringResourceConverter.TryExport(
                AuthoringAsset, out var snapshot, out var validation) ||
            snapshot is null)
        {
            _lastValidation = validation;
            QueueRedraw();
            return new TerrainAuthoringExportResult(
                false,
                AuthoringAsset.RuntimeOutputPath,
                FormatValidation(validation));
        }
        var runtime = TerrainMapResourceConverter.FromSnapshot(snapshot);
        var error = ResourceSaver.Save(runtime, AuthoringAsset.RuntimeOutputPath);
        if (error != Error.Ok)
        {
            return new TerrainAuthoringExportResult(
                false,
                AuthoringAsset.RuntimeOutputPath,
                $"ResourceSaver failed: {error}.");
        }
        if (SaveAuthoringAssetOnExport &&
            !string.IsNullOrWhiteSpace(AuthoringAsset.ResourcePath))
            ResourceSaver.Save(AuthoringAsset, AuthoringAsset.ResourcePath);
        _lastValidation = validation;
        QueueRedraw();
        return new TerrainAuthoringExportResult(
            true,
            AuthoringAsset.RuntimeOutputPath,
            $"Exported {snapshot.Columns}x{snapshot.Rows}, " +
            $"hash={snapshot.StableHashText}.");
    }

    public TerrainAuthoringResizeResult ResizeAuthoringGrid()
    {
        if (AuthoringAsset is null)
            return default;
        var surfaceId = AuthoringAsset.Surfaces.Count > 0
            ? (ushort)Math.Clamp(AuthoringAsset.Surfaces[0].Id, 0, ushort.MaxValue)
            : (ushort)0;
        var result = TerrainAuthoringResourceConverter.ResizeCellArrays(
            AuthoringAsset,
            new TerrainCell(
                0,
                surfaceId,
                TerrainPathing.Ground,
                TerrainCellFlags.Buildable));
        _lastValidation = null;
        QueueRedraw();
        return result;
    }

    private TerrainBrush CurrentBrush() => new(
        BrushKind,
        Math.Clamp(BrushRadiusCells, 0, 16),
        (byte)Math.Clamp(BrushCliffLevel, 0,
            TerrainMapSnapshot.MaximumCliffLevel),
        (ushort)Math.Clamp(BrushSurfaceId, 0, ushort.MaxValue),
        BrushPathing,
        BrushEnabled,
        BrushRampDirection);

    private bool TryDocument(
        out TerrainAuthoringDocument document,
        out TerrainAuthoringValidationResult validation)
    {
        validation = new TerrainAuthoringValidationResult([]);
        if (AuthoringAsset is not null &&
            TerrainAuthoringResourceConverter.TryConvert(
                AuthoringAsset, out var converted, out validation) &&
            converted is not null)
        {
            document = converted;
            return true;
        }
        document = null!;
        return false;
    }

    private Color CellColor(TerrainCell cell)
    {
        if ((Overlays & TerrainAuthoringOverlay.Surface) != 0)
            return SurfaceColors[cell.SurfaceId % SurfaceColors.Length];
        return new Color(0.19f, 0.22f, 0.25f);
    }

    private void DrawOverlays(Rect2 rect, TerrainCell cell)
    {
        if ((Overlays & TerrainAuthoringOverlay.GroundPathing) != 0 &&
            (cell.Pathing & TerrainPathing.Ground) != 0)
            DrawRect(rect, new Color(0.15f, 0.8f, 0.3f, 0.24f), true);
        if ((Overlays & TerrainAuthoringOverlay.WaterPathing) != 0 &&
            (cell.Pathing & (TerrainPathing.ShallowWater |
                             TerrainPathing.DeepWater)) != 0)
            DrawRect(rect, new Color(0.1f, 0.55f, 0.95f, 0.35f), true);
        if ((Overlays & TerrainAuthoringOverlay.AirBlocking) != 0 &&
            (cell.Pathing & TerrainPathing.AirBlocked) != 0)
            DrawRect(rect, new Color(0.8f, 0.2f, 0.85f, 0.35f), true);
        if ((Overlays & TerrainAuthoringOverlay.Buildable) != 0)
            DrawRect(rect, cell.IsBuildable
                ? new Color(0.15f, 0.95f, 0.45f, 0.18f)
                : new Color(0.95f, 0.2f, 0.2f, 0.24f), true);
        if ((Overlays & TerrainAuthoringOverlay.Vision) != 0 &&
            (cell.Flags & TerrainCellFlags.BlocksVision) != 0)
            DrawRect(rect, new Color(0.85f, 0.85f, 0.95f, 0.48f), true);
        if ((Overlays & TerrainAuthoringOverlay.Creep) != 0 &&
            (cell.Flags & TerrainCellFlags.BlocksCreep) != 0)
            DrawRect(rect, new Color(0.68f, 0.2f, 0.78f, 0.42f), true);
    }

    private void DrawRampArrow(Rect2 rect, TerrainRampDirection direction)
    {
        var vector = direction switch
        {
            TerrainRampDirection.PositiveX => Vector2.Right,
            TerrainRampDirection.NegativeX => Vector2.Left,
            TerrainRampDirection.PositiveY => Vector2.Down,
            TerrainRampDirection.NegativeY => Vector2.Up,
            _ => Vector2.Zero
        };
        var center = rect.GetCenter();
        var half = vector * MathF.Min(rect.Size.X, rect.Size.Y) * 0.32f;
        DrawLine(center - half, center + half, new Color("ffe066"), 3f);
        var perpendicular = new Vector2(-vector.Y, vector.X) * 5f;
        DrawLine(center + half, center + half - vector * 8f + perpendicular,
            new Color("ffe066"), 3f);
        DrawLine(center + half, center + half - vector * 8f - perpendicular,
            new Color("ffe066"), 3f);
    }

    private void DrawAnchor(TerrainAuthoringAnchor anchor)
    {
        var position = new Vector2(anchor.Position.X, anchor.Position.Y);
        var color = anchor.Kind switch
        {
            TerrainAuthoringAnchorKind.Spawn => new Color("5dade2"),
            TerrainAuthoringAnchorKind.Resource => new Color("f4d03f"),
            _ => new Color("af7ac5")
        };
        DrawCircle(position, MathF.Max(5f, anchor.RequiredRadius), color, false, 3f);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(8f, -8f),
            $"{anchor.Kind} {anchor.Id}", HorizontalAlignment.Left, -1f, 12, color);
    }

    private void DrawValidation(
        TerrainAuthoringDocument document,
        TerrainAuthoringValidationResult validation)
    {
        foreach (var issue in validation.Issues)
        {
            if (issue.Column < 0 || issue.Row < 0 ||
                issue.Column >= document.Columns || issue.Row >= document.Rows)
                continue;
            var rect = CellRect(document, issue.Column, issue.Row).Grow(-2f);
            var color = issue.Severity == TerrainAuthoringIssueSeverity.Error
                ? new Color("ff3b30")
                : new Color("ffcc00");
            DrawRect(rect, color, false, 4f);
        }
    }

    private static Rect2 CellRect(
        TerrainAuthoringDocument document,
        int column,
        int row)
    {
        var bounds = document.Bounds.Min + new NVector2(
            column * document.CellSize, row * document.CellSize);
        return new Rect2(
            bounds.X,
            bounds.Y,
            MathF.Min(document.CellSize,
                document.Bounds.Max.X - bounds.X),
            MathF.Min(document.CellSize,
                document.Bounds.Max.Y - bounds.Y));
    }

    private static bool HasValidCellArrays(RtsTerrainAuthoringResource resource) =>
        resource.CliffLevels.Length == resource.CellCount &&
        resource.SurfaceIds.Length == resource.CellCount &&
        resource.PathingMasks.Length == resource.CellCount &&
        resource.CellFlags.Length == resource.CellCount &&
        resource.RampDirections.Length == resource.CellCount;

    private static string FormatValidation(
        TerrainAuthoringValidationResult validation) =>
        string.Join("\n", validation.Issues.Take(8).Select(value =>
            $"{value.Code}: {value.Message}"));

    private void AuthoringAssetChanged()
    {
        _lastValidation = null;
        QueueRedraw();
    }
}

public readonly record struct TerrainAuthoringStrokeResult(
    bool Changed,
    string BeforePayload,
    string AfterPayload);

public readonly record struct TerrainAuthoringExportResult(
    bool Succeeded,
    string OutputPath,
    string Summary);
